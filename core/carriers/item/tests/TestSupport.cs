using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Rules.Common;

namespace Tests.Carriers.Item
{
    /// <summary>本模块测试共用夹具：事件总线（登记四个 item.* 事件 key）、DataRegistry 构造帮助
    /// 方法（注册九张 item.* schema——T-N2-1 起含 <c>armor_curve</c>/<c>weapon_dps_curve</c>/
    /// <c>req_level_curve</c> 三条新曲线表 + stat.definition）、<see cref="IEffectSink"/>/<see
    /// cref="IUnitAccess"/> 的测试假实现、记录型 <see cref="Core.Carriers.Item.SkillGranter"/>。</summary>
    internal static class TestSupport
    {
        public static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(Core.Carriers.Common.CarriersEventKeys.ItemAdded, "item",
                    new[] { "unitId", "itemInstanceId", "itemTemplateId", "count" }),
                new EventDefinition(Core.Carriers.Common.CarriersEventKeys.ItemRemoved, "item",
                    new[] { "unitId", "itemInstanceId", "count", "reason" }),
                new EventDefinition(Core.Carriers.Common.CarriersEventKeys.ItemEquipped, "item",
                    new[] { "unitId", "itemInstanceId", "slot" }),
                new EventDefinition(Core.Carriers.Common.CarriersEventKeys.ItemUnequipped, "item",
                    new[] { "unitId", "slot", "itemInstanceId" }),
                // DataRegistry.LoadAll 自身在加载完成/校验失败时会 PublishImmediate 这两个事件
                // （见 Core.Foundation.DataRegistry.DataRegistryEventKeys），本测试夹具的 DataRegistry
                // 与本模块共用同一个 IEventBus，StrictCatalog 默认开启，不登记就会在 LoadAll 内直接抛异常。
                new EventDefinition(Core.Foundation.DataRegistry.DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(Core.Foundation.DataRegistry.DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
                // EquipmentHostTests/ItemPersistableTests 用真实 StatHost，AddModifier/
                // RemoveModifiersBySource 触发最终值变化时会 Enqueue 一次 stat.changed。
                new EventDefinition(Core.Numbers.StatBlock.StatBlockEventKeys.StatChanged, "stat",
                    new[] { "unitId", "stat", "oldValue", "newValue" }),
            });

            return new EventBus(catalog);
        }

        /// <summary>构造一个已注册六张 item.* schema + stat.definition schema、并已 <see
        /// cref="IDataRegistry.LoadAll"/> 过的 <see cref="DataRegistry"/>。调用方在 <paramref
        /// name="configure"/> 里往 <see cref="InMemoryDataSource"/> 塞表数据（信封格式
        /// <c>{"table":..., "schema_version":1, "rows":[...]}</c>），额外的 <see cref="IValidationRule"/>
        /// 由 <paramref name="rules"/> 传入。<paramref name="strictness"/>（T-N5-3 新增可选参数，默认
        /// <see cref="DataRegistryStrictness.WarningsAllowed"/>，保持既有全部调用点行为不变）供需要在
        /// <see cref="DataRegistryStrictness.WarningsBlock"/> 下断言"不可提升警告不阻断"的用例传入
        /// （见 <c>ItemValidationRulesTests</c>/<c>T_N2_6_WeaponDpsDeviationTests</c>/
        /// <c>T_N3_9_ItemGrantValueExceedsShareRuleTests</c> 里对应用例）。</summary>
        public static DataRegistry BuildRegistry(
            Action<InMemoryDataSource> configure,
            IEnumerable<IValidationRule>? rules = null,
            DataRegistryStrictness strictness = DataRegistryStrictness.WarningsAllowed)
        {
            var source = new InMemoryDataSource();
            configure(source);

            // T-N2-4：FailOnUnknownTable=false（同 core/numbers/stat_block/tests/
            // StatWeightSchemaTests.BuildRegistry 既有手法）——本模块不静态耦合 arch.class 的真实
            // schema（L3 不依赖 L1 具体表结构），但 stat.weight.class_overrides[].class 的
            // reference_integrity 检查需要能在已加载数据里找到同 id 的一条记录；调用方需要覆盖
            // 职业权重的测试用例时，在 configure 里往 "arch.class" 塞最小行（仅 id 字段，走
            // TableSchema.Unschematized 占位 schema，不需要满足 arch.class 真实 schema 的
            // name_key/primary_stat/base_stats/power_types 等必填字段），不提供时该表保持空、不
            // 影响既有测试。
            var registry = new DataRegistry(source, CreateBus(), new DataRegistryOptions { FailOnUnknownTable = false, Strictness = strictness });
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Template);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.SlotDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.QualityDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.BudgetCurve);
            // T-N2-1（ADR-0032 决策 4/5）：护甲/武器秒伤/需求等级三条新曲线表，登记同 BudgetCurve。
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.ArmorCurve);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.WeaponDpsCurve);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.ReqLevelCurve);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Set);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Affix);
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            // T-N2-3（ADR-0032 决策 3）：消耗公式读取 stat.weight/stat.rating_conversion，注册同
            // stat.definition——未在 configure 里塞行的用例，两张表 GetAll 返回空列表，
            // ItemBudgetCurve.BuildStatBudgetInfo 对没有权重记录的属性按缺省权重 1 回退（见该方法
            // 判断记录），不影响既有未提供这两张表数据的测试夹具。
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Weight);
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.RatingConversion);
            // ADR-0019 F1c：grants.skills/auras、item.set.bonuses[].aura_ref 登记为
            // Reference(skill.def)/Reference(skill.aura_def)（L3 引用 L2 程序集合法），
            // reference_integrity 内建校验因此需要这两张表已加载——本模块测试用到的 skill/aura id
            // 均为测试假数据，不代表真实 core/rules/skill 语义，各测试在自己的 configure 里按需
            // 补充对应的 skill.def/skill.aura_def 行。
            registry.RegisterSchema(Core.Rules.Skill.SkillSchemas.Def);
            registry.RegisterSchema(Core.Rules.Skill.SkillSchemas.AuraDef);

            if (rules != null)
            {
                foreach (var rule in rules)
                {
                    registry.RegisterValidationRule(rule);
                }
            }

            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "测试夹具数据未通过校验：" + string.Join("; ", report.Issues.Select(i => i.ToString())));
            }

            return registry;
        }

        // T-N1-2 判断记录：本方法此前手写一份"够用就行"的 stat.definition 最小 schema
        // （只声明 id/name_key/group/default_base），与 core/numbers/stat_block 的真实
        // StatSchemas.Definition 各自独立维护——StatHost.LoadDefinitions 从 T-N1-2 起无条件读取
        // category（不再读 group），这份手写 schema 从未声明过 category 字段，也没有 1→2 迁移链，
        // 导致本模块用真实 StatHost 构造的测试全部在加载期抛 DataFieldException。改为直接注册
        // Core.Numbers.StatBlock.StatSchemas.Definition（本文件已经通过 StatBlockEventKeys 依赖
        // 该命名空间，不是新增跨层耦合）——好处是往后 stat.definition 的字段/迁移变化只有一处
        // 权威定义，不会再有第二份手写副本悄悄过期；下面 Table() 产出的信封仍是 schema_version 1、
        // 只填 group（不填 category），走真实的 MigrateDefinitionV1ToV2 自动补齐 category，
        // 现有全部行 JSON 字面量不需要改一个字符。

        public static string Table(string name, string rowsJson) =>
            "{\"table\": \"" + name + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";
    }

    /// <summary><see cref="IUnitAccess"/> 的测试假实现：只登记本模块用得到的等级（同 <c>core/rules/
    /// skill</c> 的 <c>FakeUnitAccess</c> 惯例，最小化实现，不做空间索引/阵营矩阵）。</summary>
    internal sealed class FakeUnitAccess : IUnitAccess
    {
        private readonly HashSet<Id> _units = new HashSet<Id>();
        private readonly Dictionary<Id, int> _levels = new Dictionary<Id, int>();

        public FakeUnitAccess Add(Id id, int level = 1)
        {
            _units.Add(id);
            _levels[id] = level;
            return this;
        }

        public bool Exists(Id unitId) => _units.Contains(unitId);

        public IReadOnlyList<Id> AllUnits => _units.OrderBy(id => id.Value, StringComparer.Ordinal).ToList();

        public Vec2 GetPosition(Id unitId) => Vec2.Zero;

        public void SetPosition(Id unitId, Vec2 position)
        {
        }

        public Id GetFaction(Id unitId) => default;

        public int GetLevel(Id unitId) => _levels.TryGetValue(unitId, out var l) ? l : 1;

        public double GetFacing(Id unitId) => 0;

        public bool IsAlive(Id unitId) => true;

        public void SetAlive(Id unitId, bool alive)
        {
        }

        public Id? GetTemplateId(Id unitId) => null;

        public IReadOnlyList<Id> GetTags(Id unitId) => Array.Empty<Id>();
    }

    /// <summary><see cref="IEffectSink"/> 的测试假实现：记录 <see cref="ApplyAura"/>/<see
    /// cref="RemoveAura"/> 调用，供 <c>EquipmentHostTests</c> 断言"装备后光环施加/卸下后光环移除"。
    /// <see cref="ApplyEffect"/> 本模块用不到，返回一个恒定的空结果。
    /// <para>
    /// N09 收边补齐（外部审计 68c9bed）：<see cref="SharedSlotPerAuraDef"/> 可选开关——真实
    /// <c>AuraHost.ApplyAura</c> 在 <c>SkillOptions.AllowMultiSourceTiming == false</c>（默认）时，
    /// 同一 <c>(targetId, auraDefId)</c> 的重复施加会合并到同一个共享实例（不同 sourceId 只是叠加
    /// 层数，返回同一个 <see cref="AuraInstanceRef"/>）；<c>== true</c> 时每个 sourceId 各开一份
    /// 独立实例（返回互不相同的 <see cref="AuraInstanceRef"/>）。本假实现默认关闭（每次调用恒返回
    /// 独立 ref，对应 <c>AllowMultiSourceTiming = true</c> 的场景，也是外部审计 N09 复现所需的
    /// 行为）；<c>EquipmentHostTests</c> 里验证"默认共享槽位、卸一件不影响另一件"的既有用例
    /// （RC-05）需要显式打开本开关，才能真实模拟 <c>AllowMultiSourceTiming = false</c> 下多件装备
    /// 拿到同一个共享句柄这一前提——原假实现恒返回独立 ref，与默认模式的真实行为不符，只是恰好
    /// 被 <c>EquipmentHost</c> 当时按 <c>auraDefId</c>（而非实例句柄）计数的旧实现掩盖了这个差异
    /// （见 <c>EquipmentHost._auraHandleRefCount</c> 判断记录）。
    /// </para>
    /// </summary>
    internal sealed class FakeEffectSink : IEffectSink
    {
        public readonly struct AppliedAura
        {
            public readonly Id TargetId;
            public readonly Id AuraDefId;
            public readonly Id SourceId;

            public AppliedAura(Id targetId, Id auraDefId, Id sourceId)
            {
                TargetId = targetId;
                AuraDefId = auraDefId;
                SourceId = sourceId;
            }
        }

        private long _nextInstanceSeq = 1;
        private readonly Dictionary<(Id TargetId, Id AuraDefId), AuraInstanceRef> _sharedSlots =
            new Dictionary<(Id, Id), AuraInstanceRef>();

        /// <summary>见类型注释"N09 收边补齐"。默认 false（每次独立 ref，对应
        /// <c>AllowMultiSourceTiming = true</c>）。</summary>
        public bool SharedSlotPerAuraDef { get; set; } = false;

        public List<AppliedAura> Applied { get; } = new List<AppliedAura>();

        public List<AuraInstanceRef> Removed { get; } = new List<AuraInstanceRef>();

        public ResolveResult ApplyEffect(EffectContext context) =>
            new ResolveResult(HitResult.Hit, 0, 0, 0, immune: false, isHeal: false);

        public AuraInstanceRef ApplyAura(Id targetId, Id auraDefId, Id sourceId, double? durationOverride = null)
        {
            Applied.Add(new AppliedAura(targetId, auraDefId, sourceId));

            if (SharedSlotPerAuraDef)
            {
                var slotKey = (targetId, auraDefId);
                if (_sharedSlots.TryGetValue(slotKey, out var existing))
                {
                    return existing;
                }

                var created = new AuraInstanceRef(new Id($"aura.inst_{_nextInstanceSeq++}"));
                _sharedSlots[slotKey] = created;
                return created;
            }

            return new AuraInstanceRef(new Id($"aura.inst_{_nextInstanceSeq++}"));
        }

        public void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef)
        {
            Removed.Add(auraInstanceRef);

            if (!SharedSlotPerAuraDef)
            {
                return;
            }

            (Id, Id)? keyToRemove = null;
            foreach (var pair in _sharedSlots)
            {
                if (pair.Value.Equals(auraInstanceRef))
                {
                    keyToRemove = pair.Key;
                    break;
                }
            }

            if (keyToRemove.HasValue)
            {
                _sharedSlots.Remove(keyToRemove.Value);
            }
        }
    }

    /// <summary>记录型 <see cref="Core.Carriers.Item.SkillGranter"/>：把每次调用记录下来，供测试
    /// 断言"装备后授予技能/卸下后遗忘技能"（见任务书"记录型 SkillGranter"）。</summary>
    internal sealed class RecordingSkillGranter
    {
        public readonly struct Call
        {
            public readonly Id UnitId;
            public readonly Id SkillId;
            public readonly Id SourceId;
            public readonly bool Learn;

            public Call(Id unitId, Id skillId, Id sourceId, bool learn)
            {
                UnitId = unitId;
                SkillId = skillId;
                SourceId = sourceId;
                Learn = learn;
            }
        }

        public List<Call> Calls { get; } = new List<Call>();

        public void Grant(Id unitId, Id skillId, Id sourceId, bool learn) => Calls.Add(new Call(unitId, skillId, sourceId, learn));
    }
}
