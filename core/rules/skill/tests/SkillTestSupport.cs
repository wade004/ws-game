using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Tests.Rules.Skill
{
    /// <summary>JSON 树构造帮助方法（供测试直接手搭 <c>skill.*</c>/<c>stat.definition</c> 表行，
    /// 不依赖任何游戏专属技能名——测试技能一律用 <c>skill.sample_*</c> 命名，见落地方案
    /// 反作弊禁止事项）。</summary>
    internal static class J
    {
        public static JsonValue S(string s) => new JsonString(s);

        public static JsonValue N(double n) => new JsonNumber(n);

        public static JsonValue B(bool b) => JsonBool.Of(b);

        public static JsonObject O(params (string Key, JsonValue Value)[] fields)
        {
            var builder = new JsonObjectBuilder();
            foreach (var (key, value) in fields)
            {
                builder.Add(key, value);
            }

            return builder.Build();
        }

        public static JsonArray A(params JsonValue[] items) => new JsonArray(items);

        public static JsonValue Ids(params string[] ids) => new JsonArray(ids.Select(id => (JsonValue)new JsonString(id)));

        public static JsonValue Vec(double x, double y) => O(("x", N(x)), ("y", N(y)));
    }

    /// <summary>已构建好的一整套测试宿主：真实 <see cref="StatHost"/>/<see cref="PowerHost"/>/
    /// <see cref="RngHost"/>/<see cref="EventBus"/>/<see cref="DataRegistry"/>（经
    /// <see cref="InMemoryDataSource"/>）+ 假实现 <see cref="FakeUnitAccess"/>/
    /// <see cref="FakeCombatHost"/>/<see cref="FakeTargetHost"/>/<see cref="FakeExprHostFactory"/>。</summary>
    internal sealed class SkillWorld
    {
        public IDataRegistry Registry = default!;
        public IEventBus Bus = default!;
        public IStatHost Stats = default!;
        public IPowerHost Powers = default!;
        public IRngHost Rng = default!;
        public FakeUnitAccess Units = default!;
        public FakeCombatHost Combat = default!;
        public FakeTargetHost Targets = default!;
        public FakeExprHostFactory Exprs = default!;
        public SkillOptions Options = default!;
        public SkillHost Host = default!;
        public InMemorySkillDiagnostics Diagnostics = default!;

        /// <summary>本 world 构造时登记的全部资源类型 id（见 <see cref="SkillWorldBuilder.Power"/>），
        /// 供 <see cref="AddUnit"/> 统一给新单位注册全部资源类型。</summary>
        public IReadOnlyList<Id> PowerTypeIds = Array.Empty<Id>();

        /// <summary>已派发（<see cref="IEventBus.DispatchPending"/> 之后）的全部事件，按派发顺序。
        /// 每个测试在触发动作后调用 <see cref="Flush"/> 才会填充本列表。</summary>
        public readonly List<IEvent> Events = new List<IEvent>();

        public void Flush() => Bus.DispatchPending();

        public IEnumerable<T> Of<T>() where T : IEvent => Events.OfType<T>();

        /// <summary>登记一个测试单位：同时接入 <see cref="Units"/>（假实现）、真实
        /// <see cref="Stats"/>（<see cref="IStatHost.RegisterUnit"/>）与真实 <see cref="Powers"/>
        /// （<see cref="IPowerHost.RegisterUnit"/>，注册本 world 声明的全部资源类型）——
        /// <see cref="StatHost"/>/<see cref="PowerHost"/> 均要求单位先注册才能读写，见两者
        /// <c>RequireUnit</c> 检查。</summary>
        public FakeUnitAccess AddUnit(Id id, Vec2? position = null, bool alive = true, Id? faction = null, int level = 1)
        {
            Units.Add(id, position, alive, faction, level);
            Stats.RegisterUnit(id);
            if (PowerTypeIds.Count > 0)
            {
                Powers.RegisterUnit(id, PowerTypeIds);
            }

            return Units;
        }
    }

    internal sealed class SkillWorldBuilder
    {
        private readonly InMemoryDataSource _source = new InMemoryDataSource();
        private readonly List<JsonObject> _skillDefs = new List<JsonObject>();
        private readonly List<JsonObject> _auraDefs = new List<JsonObject>();
        private readonly List<JsonObject> _procDefs = new List<JsonObject>();
        private readonly List<JsonObject> _spellModDefs = new List<JsonObject>();
        private readonly List<JsonObject> _books = new List<JsonObject>();
        private readonly List<JsonObject> _statDefs = new List<JsonObject>();
        private readonly List<(string Id, double Max, bool StartFull)> _powerTypes = new List<(string, double, bool)>();
        private readonly List<IValidationRule> _extraRules = new List<IValidationRule>();

        public SkillOptions Options { get; } = new SkillOptions();

        public ISpatialQuery? SpatialQuery { get; set; }

        public IEffectExtension? Extension { get; set; }

        /// <summary>阶段 3 整理"事项三"：注入 <see cref="AuraHost"/> 的静态免疫查询（供
        /// <see cref="Tests.Rules.Skill.AuraEffectTests"/> 验证 control_immune 一类内容驱动的
        /// 免疫不吃控制类光环），未设置时 <see cref="SkillHost"/> 缺省用
        /// <see cref="NullStaticImmunityProvider"/>（不改变既有行为）。</summary>
        public IStaticImmunityProvider? StaticImmunity { get; set; }

        /// <summary>RC-11 收边补齐：注入 <see cref="IWeaponDamageQuery"/>（<c>weapon_damage_pct</c>
        /// 效果原语的武器基础伤害来源，见该接口判断记录"依赖倒置"），未设置时 <see cref="SkillHost"/>
        /// 缺省不注入（<c>weaponBase</c> 恒为 0，见 <c>EffectDispatcher.ApplyDamageOrHeal</c> 判断
        /// 记录），与生产装配 <see cref="Core.Rules.Assembly.RulesAssembly"/> 在
        /// <c>CarriersAssembly</c> 绑定真实 <c>EquipmentHost</c> 之前的行为一致。</summary>
        public IWeaponDamageQuery? WeaponDamageQuery { get; set; }

        public ulong RngSeed { get; set; } = 1;

        /// <summary>只跑到"注册 schema/校验规则 + LoadAll"这一步，返回校验报告，不构造
        /// <see cref="SkillHost"/> 及其余真实宿主——供只关心校验结果、数据本身预期不合法（因而
        /// 不适合走 <see cref="Build"/>，那里数据不合法会直接抛异常）的测试使用（见
        /// <c>SkillValidationRuleTests</c>）。</summary>
        public ValidationReport Validate()
        {
            _source.Add("skill.def", TableJson("skill.def", _skillDefs));
            _source.Add("skill.aura_def", TableJson("skill.aura_def", _auraDefs));
            _source.Add("skill.proc_def", TableJson("skill.proc_def", _procDefs));
            _source.Add("skill.spell_mod_def", TableJson("skill.spell_mod_def", _spellModDefs));
            _source.Add("skill.book", TableJson("skill.book", _books));
            _source.Add("stat.definition", TableJson("stat.definition", _statDefs));

            var bus = CreateBus();
            var registry = new DataRegistry(_source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(SkillSchemas.Def);
            registry.RegisterSchema(SkillSchemas.AuraDef);
            registry.RegisterSchema(SkillSchemas.ProcDef);
            registry.RegisterSchema(SkillSchemas.SpellModDef);
            registry.RegisterSchema(SkillSchemas.Book);
            registry.RegisterSchema(StatSchemas.Definition);

            foreach (var rule in _extraRules)
            {
                registry.RegisterValidationRule(rule);
            }

            return registry.LoadAll();
        }

        public SkillWorldBuilder SkillDef(JsonObject row) { _skillDefs.Add(row); return this; }

        public SkillWorldBuilder AuraDef(JsonObject row) { _auraDefs.Add(row); return this; }

        public SkillWorldBuilder ProcDef(JsonObject row) { _procDefs.Add(row); return this; }

        public SkillWorldBuilder SpellModDef(JsonObject row) { _spellModDefs.Add(row); return this; }

        public SkillWorldBuilder Book(JsonObject row) { _books.Add(row); return this; }

        public SkillWorldBuilder Stat(string id, double defaultBase = 0)
        {
            _statDefs.Add(J.O(
                ("id", J.S(id)),
                ("name_key", J.S("l10n.stat." + LastSegment(id) + ".name")),
                ("group", J.S("primary")),
                ("default_base", J.N(defaultBase))));
            return this;
        }

        public SkillWorldBuilder Power(string id, double max, bool startFull = true)
        {
            _powerTypes.Add((id, max, startFull));
            return this;
        }

        public SkillWorldBuilder ValidationRule(IValidationRule rule)
        {
            _extraRules.Add(rule);
            return this;
        }

        public SkillWorld Build()
        {
            _source.Add("skill.def", TableJson("skill.def", _skillDefs));
            _source.Add("skill.aura_def", TableJson("skill.aura_def", _auraDefs));
            _source.Add("skill.proc_def", TableJson("skill.proc_def", _procDefs));
            _source.Add("skill.spell_mod_def", TableJson("skill.spell_mod_def", _spellModDefs));
            _source.Add("skill.book", TableJson("skill.book", _books));
            _source.Add("stat.definition", TableJson("stat.definition", _statDefs));

            var bus = CreateBus();
            var registry = new DataRegistry(_source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(SkillSchemas.Def);
            registry.RegisterSchema(SkillSchemas.AuraDef);
            registry.RegisterSchema(SkillSchemas.ProcDef);
            registry.RegisterSchema(SkillSchemas.SpellModDef);
            registry.RegisterSchema(SkillSchemas.Book);
            registry.RegisterSchema(StatSchemas.Definition);

            foreach (var rule in _extraRules)
            {
                registry.RegisterValidationRule(rule);
            }

            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "测试数据未通过校验：\n" + string.Join("\n", report.Issues.Select(i => i.ToString())));
            }

            var stats = new StatHost(registry, bus);
            var powers = new PowerHost(_powerTypes.Select(p => PowerType(p.Id, p.Max, p.StartFull)), bus);
            var rng = new RngHost(RngSeed);
            var units = new FakeUnitAccess();
            var combat = new FakeCombatHost();
            var targets = new FakeTargetHost();
            var exprs = new FakeExprHostFactory();
            var diagnostics = new InMemorySkillDiagnostics();

            var host = new SkillHost(
                registry, bus, units, stats, powers, rng, combat, targets, exprs, SpatialQuery,
                Options, Extension, diagnostics, exprSchema: null, staticImmunity: StaticImmunity,
                weaponDamageQuery: WeaponDamageQuery);

            var world = new SkillWorld
            {
                Registry = registry,
                Bus = bus,
                Stats = stats,
                Powers = powers,
                Rng = rng,
                Units = units,
                Combat = combat,
                Targets = targets,
                Exprs = exprs,
                Options = Options,
                Host = host,
                Diagnostics = diagnostics,
                PowerTypeIds = _powerTypes.Select(p => new Id(p.Id)).ToList(),
            };

            SubscribeAll(bus, world.Events);
            return world;
        }

        private static string TableJson(string name, IReadOnlyList<JsonObject> rows)
        {
            var root = J.O(
                ("table", J.S(name)),
                ("schema_version", J.N(1)),
                ("rows", new JsonArray(rows.Cast<JsonValue>())));
            return JsonWriter.Write(root);
        }

        private static string LastSegment(string id)
        {
            var idx = id.LastIndexOf('.');
            return idx < 0 ? id : id.Substring(idx + 1);
        }

        private static PowerTypeDefinition PowerType(string id, double max, bool startFull)
        {
            var json = J.O(
                ("id", J.S(id)),
                ("name_key", J.S("l10n.power." + LastSegment(id) + ".name")),
                ("max_source", J.O(("kind", J.S("fixed")), ("value", J.N(max)))),
                ("start_full", J.B(startFull)),
                ("allow_overflow", J.B(true)));
            var record = new DataRecord(PowerSchemas.PowerType, id, new Id(id), json);
            return new PowerTypeDefinition(record);
        }

        internal static IEventBus CreateBus()
        {
            var definitions = new[]
            {
                new EventDefinition(RulesEventKeys.SkillCastStart, "skill", new[] { "casterId", "skillId", "castTime" }),
                new EventDefinition(RulesEventKeys.SkillCastSuccess, "skill", new[] { "casterId", "skillId", "targets" }),
                new EventDefinition(RulesEventKeys.SkillCastFailed, "skill", new[] { "casterId", "skillId", "reasonCode" }),
                new EventDefinition(RulesEventKeys.SkillCastInterrupted, "skill", new[] { "casterId", "skillId", "interrupterId" }),
                new EventDefinition(RulesEventKeys.CombatDamageDealt, "combat", new[] { "sourceId", "targetId", "school", "amount", "isCrit", "hitResult" }),
                new EventDefinition(RulesEventKeys.CombatHealDone, "combat", new[] { "sourceId", "targetId", "amount", "isCrit" }),
                new EventDefinition(RulesEventKeys.CombatThreatChanged, "combat", new[] { "unitId", "sourceId", "oldValue", "newValue" }),
                new EventDefinition(RulesEventKeys.CombatEntered, "combat", new[] { "unitId", "hostileId" }),
                new EventDefinition(RulesEventKeys.CombatLeft, "combat", new[] { "unitId" }),
                new EventDefinition(RulesEventKeys.AuraApplied, "aura", new[] { "targetId", "auraDefId", "sourceId", "stacks" }),
                new EventDefinition(RulesEventKeys.AuraRemoved, "aura", new[] { "targetId", "auraDefId", "reason" }),
                new EventDefinition(RulesEventKeys.AuraStackChanged, "aura", new[] { "targetId", "auraDefId", "oldStacks", "newStacks" }),
                new EventDefinition(RulesEventKeys.ProcTriggered, "skill", new[] { "unitId", "procDefId", "triggerSkillId" }),
                new EventDefinition(RulesEventKeys.UnitDied, "unit", new[] { "unitId", "killerId" }),
                new EventDefinition(RulesEventKeys.UnitRespawned, "unit", new[] { "unitId", "policy" }),
                new EventDefinition(RulesEventKeys.AiStateChanged, "ai", new[] { "unitId", "oldState", "newState" }),
                // RC-03 收边补齐：CastPipeline 订阅 entity.destroyed 取消施法（见该类型构造函数
                // 判断记录），登记该 key 供测试用 Enqueue(new EntityDestroyedEvent(...)) 模拟销毁。
                new EventDefinition(SimEventKeys.EntityDestroyed, "entity", new[] { "entityId" }),
            };

            return new EventBus(EventCatalog.FromDefinitions(definitions), new EventBusOptions { StrictCatalog = false });
        }

        private static void SubscribeAll(IEventBus bus, List<IEvent> sink)
        {
            void Add(Id key) => bus.Subscribe(key, evt => sink.Add(evt));

            Add(RulesEventKeys.SkillCastStart);
            Add(RulesEventKeys.SkillCastSuccess);
            Add(RulesEventKeys.SkillCastFailed);
            Add(RulesEventKeys.SkillCastInterrupted);
            Add(RulesEventKeys.CombatDamageDealt);
            Add(RulesEventKeys.CombatHealDone);
            Add(RulesEventKeys.CombatThreatChanged);
            Add(RulesEventKeys.CombatEntered);
            Add(RulesEventKeys.CombatLeft);
            Add(RulesEventKeys.AuraApplied);
            Add(RulesEventKeys.AuraRemoved);
            Add(RulesEventKeys.AuraStackChanged);
            Add(RulesEventKeys.ProcTriggered);
            Add(RulesEventKeys.UnitDied);
            Add(RulesEventKeys.UnitRespawned);
            Add(RulesEventKeys.AiStateChanged);
            Add(SimEventKeys.EntityDestroyed);
        }
    }
}
