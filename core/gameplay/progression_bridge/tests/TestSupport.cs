using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Numbers.Progression;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.ProgressionBridge
{
    /// <summary>供本模块（<c>core/gameplay/progression_bridge</c>）测试共用的最小装配帮助（惯例同
    /// <c>core/gameplay/loot/tests/LootTestSupport.cs</c>；本模块两个监听器直接构造事件对象经
    /// <see cref="IEventBus.Enqueue"/>/<see cref="IEventBus.DispatchPending"/> 派发，不经过
    /// <c>CombatHost</c>/<c>AreaTriggerHost</c> 的真实评估管线——同该目录既有
    /// <c>T_N2_8b_CreatureDeathSourceLevelTests</c> 一类"直接构造事件驱动监听器"的写法）。</summary>
    internal static class ProgressionBridgeTestSupport
    {
        public static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        public static IEventBus NewEventBus() =>
            new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        /// <summary>占位 <c>combat.level_diff_table</c> schema——真实字段表归
        /// <c>Core.Rules.Combat.CombatSchemas.LevelDiffTable</c>；<c>Tests.Gameplay</c> 与
        /// <c>Tests.Numbers</c> 是两个独立程序集，不能跨程序集复用
        /// <c>Tests.Numbers.Progression.ProgressionHostTests</c> 里的同名私有 stub（惯例同该文件
        /// 判断记录），这里按同样最小字段集（只登记 <see cref="ProgressionHost"/> 实际会读的
        /// <c>id</c>/<c>xp_factor</c> 两个字段）另建一份。</summary>
        public static readonly TableSchema LevelDiffTableStub = new TableSchema(
            name: "combat.level_diff_table",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "占位"),
                CurveSchema.BreakpointsField("xp_factor", CurveAxis.LevelDiff, required: true,
                    description: "经验系数断点表，横轴 Δ（T-N4-3 测试站位登记）"),
            });

        /// <summary>标准夹具：一条领取者等级曲线（1..20 级，每级门槛巨大，用例里的入账值不会意外
        /// 升级）、一条击杀基数曲线（<c>Evaluate(x)=100x</c>，两点线性，供手算核对）、一条等级差表
        /// （Δ=-5→0.1、Δ=0→1.0、Δ=5→1.5）、<paramref name="xpSourceRowsJson"/> 指定的经验来源行。
        /// 惯例同 <c>ProgressionHostTests.MakeGrantXpRegistry</c>（不同程序集，另建一份，字段与数值
        /// 完全对齐以便复核）。</summary>
        public static DataRegistry MakeProgressionRegistry(IEventBus bus, string xpSourceRowsJson)
        {
            const string curveRows = @"[{""id"":""prog.curve.pb"",""max_level"":20,""entries"":[" +
                "{\"level\":1,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":2,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":3,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":4,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":5,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":6,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":7,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":8,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":9,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":10,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":11,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":12,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":13,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":14,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":15,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":16,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":17,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":18,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":19,\"xp_to_next\":100000,\"growth\":{}}," +
                "{\"level\":20,\"xp_to_next\":0,\"growth\":{}}]}]";

            const string baseCurveRows =
                "[{\"id\":\"prog.xp_base_curve.pb\",\"entries\":[{\"x\":1,\"y\":100},{\"x\":11,\"y\":1100}]}]";

            const string levelDiffRows =
                "[{\"id\":\"combat.level_diff.pb\",\"xp_factor\":[{\"x\":-5,\"y\":0.1},{\"x\":0,\"y\":1.0},{\"x\":5,\"y\":1.5}]}]";

            var source = new InMemoryDataSource()
                .Add("prog.level_curve", Envelope("prog.level_curve", curveRows))
                .Add("prog.xp_source", Envelope("prog.xp_source", xpSourceRowsJson))
                .Add("prog.xp_base_curve", Envelope("prog.xp_base_curve", baseCurveRows))
                .Add("combat.level_diff_table", Envelope("combat.level_diff_table", levelDiffRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(ProgSchemas.LevelCurve);
            registry.RegisterSchema(ProgSchemas.XpSource);
            registry.RegisterSchema(ProgSchemas.XpBaseCurve);
            registry.RegisterSchema(LevelDiffTableStub);
            registry.RegisterValidationRule(new ProgLevelCurveValidationRule());

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        public static ProgressionHost MakeProgressionHost(IDataRegistryView registry, IEventBus bus) =>
            new ProgressionHost(registry, bus, (unitId, stat, op, value, sourceId) => { }, (unitId, sourceId) => { });

        public static IWorldSim NewWorld(IEventBus bus) => new WorldSim(bus);

        public static Core.Carriers.Unit.PlayerUnit AddPlayer(IWorldSim world, Id entityId, Id mapId, Vec2 position)
        {
            var unit = new Core.Carriers.Unit.PlayerUnit(entityId, mapId, new Id("fac.pb_player"), new Id("archetype.pb_test"))
            {
                Position = position,
            };
            world.AddEntity(unit);
            return unit;
        }

        public static Core.Carriers.Unit.CreatureUnit AddCreature(IWorldSim world, Id entityId, Id mapId, Id templateId, Vec2 position)
        {
            var unit = new Core.Carriers.Unit.CreatureUnit(entityId, mapId, new Id("fac.pb_monster"), templateId)
            {
                Position = position,
            };
            world.AddEntity(unit);
            return unit;
        }

        /// <summary>按 <see cref="CreatureTemplate.FromRecord"/> 构造一条最小合法模板，
        /// <paramref name="tierId"/> 供 <see cref="Core.Gameplay.ProgressionBridge.CreatureDeathXpListener"/>
        /// 的 <c>ResolveTierId</c> 测试核对（惯例同 <c>LootTestSupport.MakeCreatureTemplate</c>，本模块
        /// 需要能自定义分档 id，另建一份而不是复用该私有帮助）。</summary>
        public static CreatureTemplate MakeCreatureTemplate(Id templateId, Id tierId)
        {
            var json = "{\"id\": \"" + templateId.Value + "\", \"name_key\": \"l10n.creature.sample.name\", " +
                "\"level\": 1, \"tier\": \"" + tierId.Value + "\", \"base_stats\": {}, " +
                "\"faction_id\": \"fac.pb_monster\", \"display_ref\": \"display.sample\"}";
            var obj = (JsonObject)JsonReader.Parse(json);
            var record = new DataRecord(CreatureSchemas.Template, templateId.Value, templateId, obj);
            return CreatureTemplate.FromRecord(record);
        }

        internal sealed class FakeCreatureTemplateQuery : ICreatureTemplateQuery
        {
            private readonly Dictionary<Id, CreatureTemplate> _templates = new Dictionary<Id, CreatureTemplate>();

            public void Add(Id templateId, Id tierId) => _templates[templateId] = MakeCreatureTemplate(templateId, tierId);

            public CreatureTemplate Get(Id templateId) =>
                _templates.TryGetValue(templateId, out var t) ? t : throw new ArgumentException($"未登记的模板 \"{templateId}\"");

            public bool HasFlag(Id templateId, NpcFlag flag) => false;
        }

        /// <summary><see cref="ISummonHost"/> 的最小测试假实现：只支持 <see cref="SetOwner"/> 配置过的
        /// 召唤物 → 主人映射；<see cref="Summon"/>/<see cref="Dismiss"/>/<see cref="GetSummons"/> 本模块
        /// 测试不需要，抛异常暴露误用。</summary>
        internal sealed class FakeSummonHost : ISummonHost
        {
            private readonly Dictionary<Id, Id> _owners = new Dictionary<Id, Id>();

            public void SetOwner(Id summonId, Id ownerId) => _owners[summonId] = ownerId;

            public Id? GetOwner(Id summonId) => _owners.TryGetValue(summonId, out var owner) ? owner : (Id?)null;

            public Id Summon(Id ownerId, Id creatureTemplateId, Vec2 position, double? duration = null) =>
                throw new NotSupportedException("本假实现只支持 GetOwner");

            public void Dismiss(Id summonId) => throw new NotSupportedException("本假实现只支持 GetOwner");

            public IReadOnlyList<Id> GetSummons(Id ownerId) => throw new NotSupportedException("本假实现只支持 GetOwner");
        }

        /// <summary>记录调用参数的 <see cref="IProgressionHost.GrantXp"/> 间谍，供
        /// <see cref="Core.Gameplay.ProgressionBridge.CreatureDeathXpListener"/> 的 <c>TierId</c> 接线
        /// 测试核对——本类只覆盖监听器实际会调用的成员，其余成员抛异常暴露误用。</summary>
        internal sealed class SpyProgressionHost : IProgressionHost
        {
            public readonly List<(Id UnitId, Id SourceId, XpContext Context)> Calls =
                new List<(Id, Id, XpContext)>();

            public long GrantXp(Id unitId, Id sourceId, XpContext context)
            {
                Calls.Add((unitId, sourceId, context));
                return 0;
            }

            public void RegisterUnit(Id unitId, Id curveId, int startLevel = 1) => throw new NotSupportedException();

            public int GetLevel(Id unitId) => throw new NotSupportedException();

            public long GetXp(Id unitId) => throw new NotSupportedException();

            public long GetXpToNext(Id unitId) => throw new NotSupportedException();

            public void AddXp(Id unitId, Id sourceId, long amount) => throw new NotSupportedException();

            public void GrantFromSource(Id unitId, Id xpSourceId, double multiplier = 1) => throw new NotSupportedException();
        }
    }
}
