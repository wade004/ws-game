using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Rules.Ai;
using Core.Rules.Common;
using Core.Rules.Skill;
using Core.Rules.Targeting;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Rules.Ai
{
    // -----------------------------------------------------------------
    // Fake IUnitAccess：可编程位置/存活/阵营的最小假实现。
    // -----------------------------------------------------------------
    internal sealed class FakeUnitAccess : IUnitAccess
    {
        private sealed class UnitRecord
        {
            public Vec2 Position;
            public Id Faction;
            public bool Alive = true;
            public int Level = 1;
            public double Facing = 0;
            public Id? TemplateId = null;
            public List<Id> Tags = new List<Id>();
        }

        private readonly Dictionary<string, UnitRecord> _units = new Dictionary<string, UnitRecord>(StringComparer.Ordinal);
        private readonly List<Id> _order = new List<Id>();

        public FakeUnitAccess Add(Id unitId, Vec2 position, Id faction, bool alive = true)
        {
            _units[unitId.Value] = new UnitRecord { Position = position, Faction = faction, Alive = alive };
            _order.Add(unitId);
            _order.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            return this;
        }

        public bool Exists(Id unitId) => _units.ContainsKey(unitId.Value);

        public IReadOnlyList<Id> AllUnits => _order;

        public Vec2 GetPosition(Id unitId) => Get(unitId).Position;

        public void SetPosition(Id unitId, Vec2 position) => Get(unitId).Position = position;

        public Id GetFaction(Id unitId) => Get(unitId).Faction;

        public int GetLevel(Id unitId) => Get(unitId).Level;

        public double GetFacing(Id unitId) => Get(unitId).Facing;

        public bool IsAlive(Id unitId) => Get(unitId).Alive;

        public void SetAlive(Id unitId, bool alive) => Get(unitId).Alive = alive;

        public Id? GetTemplateId(Id unitId) => Get(unitId).TemplateId;

        public IReadOnlyList<Id> GetTags(Id unitId) => Get(unitId).Tags;

        private UnitRecord Get(Id unitId)
        {
            if (_units.TryGetValue(unitId.Value, out var record)) return record;
            throw new InvalidOperationException($"未登记的测试单位 \"{unitId}\"");
        }
    }

    // -----------------------------------------------------------------
    // Fake ISkillHost：可编程每个 skillId 的 CastResult，记录调用。
    // -----------------------------------------------------------------
    internal sealed class FakeSkillHost : ISkillHost
    {
        public readonly List<(Id caster, Id skillId, IReadOnlyList<Id> targets)> Calls =
            new List<(Id, Id, IReadOnlyList<Id>)>();

        private readonly Dictionary<string, CastResult> _results = new Dictionary<string, CastResult>(StringComparer.Ordinal);

        public FakeSkillHost Program(Id skillId, CastResult result)
        {
            _results[skillId.Value] = result;
            return this;
        }

        public Vec2 GetPosition(Id unitId) => Vec2.Zero;

        public IReadOnlyList<Id> FindUnits(Core.Foundation.EngineAdapter.Shape shape, Vec2 origin, UnitFilter filter) =>
            Array.Empty<Id>();

        public void ApplyStatMod(Id sourceId, Id unitId, Id stat, Core.Numbers.StatBlock.StatModifierOp op, double value)
        {
        }

        public CastResult CastSkill(Id casterId, Id skillId, IReadOnlyList<Id> targets)
        {
            Calls.Add((casterId, skillId, targets));
            if (_results.TryGetValue(skillId.Value, out var result))
            {
                return result;
            }

            return CastResult.Ok(new Id("cast.instance_test"));
        }

        public double GetCooldown(Id unitId, Id skillId) => 0;

        public bool IsCasting(Id unitId) => false;

        public void Interrupt(Id unitId, Id interrupterId, Id? lockSchool, double lockDuration)
        {
        }
    }

    // -----------------------------------------------------------------
    // Fake IThreatTable：简单的按来源累加仇恨值，最高者为 top threat。
    // -----------------------------------------------------------------
    internal sealed class FakeThreatTable : IThreatTable
    {
        private readonly Dictionary<string, Dictionary<string, double>> _tables = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);

        public void AddThreat(Id unitId, Id sourceId, double amount)
        {
            var table = TableFor(unitId);
            table.TryGetValue(sourceId.Value, out var current);
            table[sourceId.Value] = current + amount;
        }

        public Id? GetTopThreat(Id unitId)
        {
            if (!_tables.TryGetValue(unitId.Value, out var table) || table.Count == 0)
            {
                return null;
            }

            string? bestKey = null;
            var bestValue = double.MinValue;
            foreach (var kv in table)
            {
                if (kv.Value > bestValue || (kv.Value == bestValue && string.CompareOrdinal(kv.Key, bestKey) < 0))
                {
                    bestValue = kv.Value;
                    bestKey = kv.Key;
                }
            }

            return bestKey == null ? (Id?)null : new Id(bestKey);
        }

        public void Clear(Id unitId) => _tables.Remove(unitId.Value);

        public double GetThreat(Id unitId, Id sourceId)
        {
            if (_tables.TryGetValue(unitId.Value, out var table) && table.TryGetValue(sourceId.Value, out var value))
            {
                return value;
            }

            return 0;
        }

        public void SetThreat(Id unitId, Id sourceId, double amount) => TableFor(unitId)[sourceId.Value] = amount;

        public IReadOnlyList<(Id source, double amount)> GetAll(Id unitId)
        {
            if (!_tables.TryGetValue(unitId.Value, out var table))
            {
                return Array.Empty<(Id, double)>();
            }

            var list = new List<(Id, double)>();
            foreach (var kv in table)
            {
                list.Add((new Id(kv.Key), kv.Value));
            }
            return list;
        }

        private Dictionary<string, double> TableFor(Id unitId)
        {
            if (!_tables.TryGetValue(unitId.Value, out var table))
            {
                table = new Dictionary<string, double>(StringComparer.Ordinal);
                _tables[unitId.Value] = table;
            }
            return table;
        }
    }

    // -----------------------------------------------------------------
    // Fake IExprHostFactory / IExprHost：可编程键值（忽略 args，按 "group.key" 编程）。
    // -----------------------------------------------------------------
    internal sealed class FakeExprHost : IExprHost
    {
        public readonly Dictionary<string, ExprValue> Values = new Dictionary<string, ExprValue>(StringComparer.Ordinal);

        public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args)
        {
            var lookupKey = group + "." + key;
            if (Values.TryGetValue(lookupKey, out var value))
            {
                return value;
            }

            throw new InvalidOperationException($"FakeExprHost 未编程的查询：{lookupKey}");
        }
    }

    internal sealed class FakeExprHostFactory : IExprHostFactory
    {
        public readonly FakeExprHost Host = new FakeExprHost();
        public readonly List<(Id selfId, Id? targetId)> Calls = new List<(Id, Id?)>();

        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent)
        {
            Calls.Add((selfId, targetId));
            return Host;
        }
    }

    // -----------------------------------------------------------------
    // 通用测试装配：DataRegistry + EventBus + PowerHost + FactionMatrix + RngHost。
    // -----------------------------------------------------------------
    internal static class AiTestSupport
    {
        public static readonly Id FactionMonster = new Id("fac.test_monster");
        public static readonly Id FactionPlayer = new Id("fac.test_player");

        public static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
                new EventDefinition(FactionEventKeys.RelationChanged, "faction",
                    new[] { "from", "to", "oldReaction", "newReaction" }),
                new EventDefinition(PowerEventKeys.Changed, "power",
                    new[] { "unitId", "powerType", "oldValue", "newValue" }),
                new EventDefinition(PowerEventKeys.Depleted, "power",
                    new[] { "unitId", "powerType" }),
                new EventDefinition(RulesEventKeys.AiStateChanged, "ai",
                    new[] { "unitId", "oldState", "newState" }),
                new EventDefinition(RulesEventKeys.AiDecisionMade, "ai",
                    new[] { "unitId", "decisionId" }),
                // AiHost 订阅 entity.destroyed 做场景卸载级联清理（见 AiHostCascadeCleanupTests，
                // ADR-0016 背景一节联动发现的既有缺口），测试需要能合法 PublishImmediate 这个 key。
                new EventDefinition(SimEventKeys.EntityCreated, "entity",
                    new[] { "entityId", "kind", "displayId" }),
                new EventDefinition(SimEventKeys.EntityDestroyed, "entity",
                    new[] { "entityId" }),
                // W1 收边补齐：AiDiscreteTurnBudgetTests 需要驱动一个真实 WorldSim + TurnScheduler
                // （ADR-0013 决策 6 集成场景），二者在 BeginCombat/NextStep/NotifyStepConsumed/
                // Tick 过程中会 PublishImmediate 这四个 sim.* 事件，此前本目录的事件目录未登记它们，
                // StrictCatalog（默认开启）会直接抛异常——只登记 key/domain，不逐字段校验（惯例同
                // core/rules/tests/Integration/FightWorldBuilder.cs 用 EventKeys.All 构造目录）。
                new EventDefinition(SimEventKeys.TickStarted, "sim", Array.Empty<string>()),
                new EventDefinition(SimEventKeys.TickFinished, "sim", Array.Empty<string>()),
                new EventDefinition(SimEventKeys.TurnStarted, "sim", Array.Empty<string>()),
                new EventDefinition(SimEventKeys.TurnEnded, "sim", Array.Empty<string>()),
                new EventDefinition(SimEventKeys.RoundEnded, "sim", Array.Empty<string>()),
                new EventDefinition(SimEventKeys.AwaitingInput, "sim", Array.Empty<string>()),
            });
            return new EventBus(catalog);
        }

        public static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        /// <summary>装配一个已加载 ai.* 三张表的 <see cref="DataRegistry"/>；<paramref name="profilesJson"/>/
        /// <paramref name="rotationsJson"/>/<paramref name="patrolsJson"/> 均为 JSON 数组文本（可以是 "[]"）。</summary>
        public static DataRegistry MakeRegistry(IEventBus bus, string profilesJson, string rotationsJson, string patrolsJson) =>
            MakeRegistry(bus, profilesJson, rotationsJson, patrolsJson, "[]", "[]");

        /// <summary>
        /// RC-10 收边补齐（<c>AiRotationTargetingTests</c> 专用）：额外可选装配 <c>skill.def</c>/
        /// <c>target.chain_def</c> 两张表——<see cref="AiHost.LoadRotations"/> 现在会读它们判定
        /// <c>ai.rotation.entries[].skill_id</c> 是不是"敌对单体"（见该方法判断记录
        /// "RC-10 收边补齐"），既有全部 AiHost 测试用例都不需要这两张表（本模块只按 skillId 断言，
        /// 从不检查 CastSkill 收到的 targets 内容），保留上面那个四参数重载不变，行为完全不变；
        /// 只有需要验证目标类型分类的新用例才用本重载显式提供这两张表。</summary>
        public static DataRegistry MakeRegistry(
            IEventBus bus, string profilesJson, string rotationsJson, string patrolsJson,
            string skillDefJson, string targetChainDefJson)
        {
            // ADR-0019 F1c：ai.rotation.entries[].skill_id 现登记为 Reference(skill.def)，本模块
            // 数十个既有测试用例各自在 rotationsJson 里内联了任意假 skill_id（"skill.a"/"skill.never"/
            // ...），逐个测试文件手动补 skill.def 行成本过高且容易遗漏。改为集中在这里从
            // rotationsJson 正则提取全部 skill_id 值，自动合成最小合法 skill.def 行（显式传入的
            // skillDefJson——如 AiRotationTargetingTests 需要真实 target_shape_ref 分类——里已有的
            // id 不重复合成，以显式传入为准）。
            var syntheticSkillDefJson = SynthesizeMissingSkillDefs(rotationsJson, skillDefJson);

            var source = new InMemoryDataSource()
                .Add(AiSchemas.BehaviorProfile.Name, Envelope(AiSchemas.BehaviorProfile.Name, profilesJson))
                .Add(AiSchemas.Rotation.Name, Envelope(AiSchemas.Rotation.Name, rotationsJson))
                .Add(AiSchemas.PatrolPath.Name, Envelope(AiSchemas.PatrolPath.Name, patrolsJson))
                .Add("skill.def", Envelope("skill.def", syntheticSkillDefJson))
                .Add("target.chain_def", Envelope("target.chain_def", targetChainDefJson));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(AiSchemas.BehaviorProfile);
            registry.RegisterSchema(AiSchemas.Rotation);
            registry.RegisterSchema(AiSchemas.PatrolPath);
            registry.RegisterSchema(SkillSchemas.Def);
            registry.RegisterSchema(TargetSchemas.ChainDef);

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        /// <summary>从 <paramref name="rotationsJson"/> 正则提取全部 <c>"skill_id": "..."</c> 值，
        /// 为其中未出现在 <paramref name="explicitSkillDefJson"/>（按 <c>"id": "..."</c> 提取）里的
        /// id 各合成一条最小合法 <c>skill.def</c> 行（本模块测试只按 skillId 断言 <see
        /// cref="FakeSkillHost"/> 的调用记录，从不依赖这些技能的真实效果/学派语义），与显式提供的
        /// 行拼接成最终的 <c>skill.def</c> 表 JSON。</summary>
        private static string SynthesizeMissingSkillDefs(string rotationsJson, string explicitSkillDefJson)
        {
            var explicitIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                explicitSkillDefJson, "\"id\"\\s*:\\s*\"([^\"]+)\""))
            {
                explicitIds.Add(m.Groups[1].Value);
            }

            var synthesized = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                rotationsJson, "\"skill_id\"\\s*:\\s*\"([^\"]+)\""))
            {
                var id = m.Groups[1].Value;
                if (explicitIds.Contains(id) || !seen.Add(id))
                {
                    continue;
                }

                synthesized.Add(
                    "{\"id\": \"" + id + "\", \"school\": \"skill.school.ai_test\", \"kind\": \"active\"," +
                    " \"range\": 0, \"cast_time\": 0, \"respects_gcd\": true," +
                    " \"target_shape_ref\": \"target.ai_test\", \"effects\": []}");
            }

            var explicitRows = explicitSkillDefJson.Trim();
            var explicitBody = explicitRows.Length >= 2 ? explicitRows.Substring(1, explicitRows.Length - 2).Trim() : "";

            var allRows = new List<string>();
            if (explicitBody.Length > 0)
            {
                allRows.Add(explicitBody);
            }
            allRows.AddRange(synthesized);

            return "[" + string.Join(",", allRows) + "]";
        }

        /// <summary>装配 fac.test_monster 敌对 fac.test_player 的最小 FactionMatrix。</summary>
        public static FactionMatrix MakeFactionMatrix(IEventBus bus)
        {
            var factionRows = "[" +
                "{\"id\": \"fac.test_monster\", \"name_key\": \"l10n.fac.test_monster.name\", \"default_reaction\": \"hostile\"}," +
                "{\"id\": \"fac.test_player\", \"name_key\": \"l10n.fac.test_player.name\", \"default_reaction\": \"neutral\"}" +
                "]";

            var source = new InMemoryDataSource()
                .Add("fac.faction", Envelope("fac.faction", factionRows))
                .Add("fac.reaction_matrix", Envelope("fac.reaction_matrix", "[]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(FacSchemas.Faction);
            registry.RegisterSchema(FacSchemas.ReactionMatrix);

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking);

            return new FactionMatrix(registry, bus);
        }

        /// <summary>真实 PowerHost，只登记 health 一种固定上限资源。</summary>
        public static PowerHost MakePowerHost(IEventBus bus, double maxHealth = 100)
        {
            var json = "{"
                + "\"id\": \"" + WellKnownPowers.Health.Value + "\","
                + "\"name_key\": \"l10n.power.health.name\","
                + "\"max_source\": {\"kind\": \"fixed\", \"value\": " + maxHealth.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},"
                + "\"regen_in_combat\": 0,"
                + "\"regen_out_of_combat\": 0,"
                + "\"decay_out_of_combat\": 0,"
                + "\"refill_on_leave_combat\": false,"
                + "\"start_full\": true,"
                + "\"allow_overflow\": false,"
                + "\"min\": 0"
                + "}";

            var obj = (Core.Foundation.Common.Json.JsonObject)Core.Foundation.Common.Json.JsonReader.Parse(json);
            var record = new DataRecord(PowerSchemas.PowerType, WellKnownPowers.Health.Value, WellKnownPowers.Health, obj);
            var definition = new PowerTypeDefinition(record);

            return new PowerHost(new[] { definition }, bus);
        }

        public static IReadOnlyList<Id> HealthList => new[] { WellKnownPowers.Health };
    }
}
