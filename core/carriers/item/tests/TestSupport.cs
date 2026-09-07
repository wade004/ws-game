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
    /// 方法（注册六张 item.* schema + stat.definition）、<see cref="IEffectSink"/>/<see
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
        /// 由 <paramref name="rules"/> 传入。</summary>
        public static DataRegistry BuildRegistry(
            Action<InMemoryDataSource> configure,
            IEnumerable<IValidationRule>? rules = null)
        {
            var source = new InMemoryDataSource();
            configure(source);

            var registry = new DataRegistry(source, CreateBus());
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Template);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.SlotDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.QualityDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.BudgetCurve);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Set);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Affix);
            registry.RegisterSchema(StatDefinitionSchema());

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

        private static TableSchema StatDefinitionSchema() => new TableSchema(
            name: "stat.definition",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("name_key", FieldKind.TextKey, required: true),
                new FieldSchema("group", FieldKind.Enum, required: true,
                    enumValues: new[] { "primary", "secondary", "derived", "resistance" }),
                new FieldSchema("default_base", FieldKind.Number, required: false),
            });

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
    /// <see cref="ApplyEffect"/> 本模块用不到，返回一个恒定的空结果。</summary>
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

        public List<AppliedAura> Applied { get; } = new List<AppliedAura>();

        public List<AuraInstanceRef> Removed { get; } = new List<AuraInstanceRef>();

        public ResolveResult ApplyEffect(EffectContext context) =>
            new ResolveResult(HitResult.Hit, 0, 0, 0, immune: false, isHeal: false);

        public AuraInstanceRef ApplyAura(Id targetId, Id auraDefId, Id sourceId, double? durationOverride = null)
        {
            Applied.Add(new AppliedAura(targetId, auraDefId, sourceId));
            return new AuraInstanceRef(new Id($"aura.inst_{_nextInstanceSeq++}"));
        }

        public void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef) => Removed.Add(auraInstanceRef);
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
