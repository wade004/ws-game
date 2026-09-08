using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Numbers.Progression;
using Xunit;

namespace Tests.Numbers.Progression
{
    /// <summary>
    /// W1 收边补齐（A4 审计 F1）：<c>player.progression</c> 存档段（<see cref="ProgressionPersistable"/>）
    /// 存读往返用例。夹具与曲线数据复用 <see cref="ProgressionHostTests"/> 同款三级曲线，避免重复
    /// 定义一份等价数据。
    /// </summary>
    public sealed class ProgressionPersistableTests
    {
        private static readonly Id Unit = new Id("unit.hero");
        private static readonly Id CurveId = new Id("prog.curve.sample");

        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
                new EventDefinition(ProgressionEventKeys.LevelUp, "progression",
                    new[] { "unitId", "oldLevel", "newLevel" }),
                new EventDefinition(ProgressionEventKeys.XpGained, "progression",
                    new[] { "unitId", "sourceId", "amount" }),
                // R08 收边补齐：RestoreState（读档）结尾发布 ProgressionRestoredEvent，见该类型判断记录。
                new EventDefinition(ProgressionEventKeys.StateRestored, "progression",
                    new[] { "unitId", "level" }),
            });
            return new EventBus(catalog);
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        // 同 ProgressionHostTests 的标准三级曲线：1→2 需 100 经验（成长 {stat.strength:2, stat.vitality:1}），
        // 2→3 需 50 经验（成长 {stat.strength:3}），3 级（满级）xp_to_next=0。
        private const string CurveRows = @"[
            {
                ""id"": ""prog.curve.sample"",
                ""max_level"": 3,
                ""entries"": [
                    { ""level"": 1, ""xp_to_next"": 100, ""growth"": {} },
                    { ""level"": 2, ""xp_to_next"": 50, ""growth"": { ""stat.strength"": 2, ""stat.vitality"": 1 } },
                    { ""level"": 3, ""xp_to_next"": 0, ""growth"": { ""stat.strength"": 3 } }
                ]
            }
        ]";

        private static DataRegistry MakeRegistry(out IEventBus bus)
        {
            bus = MakeBus();
            var source = new InMemoryDataSource().Add("prog.level_curve", Envelope("prog.level_curve", CurveRows));
            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(ProgSchemas.LevelCurve);
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "测试数据未通过校验：" + string.Join("; ", report.Issues));
            }
            return registry;
        }

        private sealed class RecordingWriters
        {
            public readonly List<(Id UnitId, Id Stat, string Op, double Value, Id SourceId)> WriteCalls = new();
            public readonly List<(Id UnitId, Id SourceId)> RemoveCalls = new();

            public void Write(Id unitId, Id stat, string op, double value, Id sourceId) =>
                WriteCalls.Add((unitId, stat, op, value, sourceId));

            public void Remove(Id unitId, Id sourceId) => RemoveCalls.Add((unitId, sourceId));
        }

        private static ProgressionHost MakeHost(IDataRegistryView registry, IEventBus bus, RecordingWriters writers) =>
            new ProgressionHost(registry, bus, writers.Write, writers.Remove);

        [Fact]
        public void SectionKey_MatchesSaveSections()
        {
            var registry = MakeRegistry(out var bus);
            var host = MakeHost(registry, bus, new RecordingWriters());
            var persistable = ProgressionPersistable.For(host, Unit);

            Assert.Equal(SaveSections.PlayerProgression, persistable.SectionKey);
        }

        [Fact]
        public void RoundTrips_LevelAndXp_AndReappliesGrowth()
        {
            var registry = MakeRegistry(out var bus);
            var sourceWriters = new RecordingWriters();
            var sourceHost = MakeHost(registry, bus, sourceWriters);
            sourceHost.RegisterUnit(Unit, CurveId, startLevel: 1);
            sourceHost.AddXp(Unit, new Id("system.test"), 120); // 100 升到 2 级，剩余 20 经验。

            Assert.Equal(2, sourceHost.GetLevel(Unit));
            Assert.Equal(20, sourceHost.GetXp(Unit));

            var saved = ProgressionPersistable.For(sourceHost, Unit).Save();

            // 全新的 ProgressionHost/writers（模拟"重新构造世界后读档"）。
            var targetWriters = new RecordingWriters();
            var targetHost = MakeHost(registry, bus, targetWriters);
            ProgressionPersistable.For(targetHost, Unit).Load(saved);

            Assert.Equal(2, targetHost.GetLevel(Unit));
            Assert.Equal(20, targetHost.GetXp(Unit));

            // 成长修正应按 1..2 级重新聚合（等价于正常升级路径的 ApplyGrowth 结果）：
            // 先移除 prog.growth 来源旧修正，再写入 2 级累计的 {stat.strength:2, stat.vitality:1}。
            Assert.Single(targetWriters.RemoveCalls);
            Assert.Equal(2, targetWriters.WriteCalls.Count);
            Assert.Contains(targetWriters.WriteCalls, c => c.Stat.Value == "stat.strength" && c.Value == 2);
            Assert.Contains(targetWriters.WriteCalls, c => c.Stat.Value == "stat.vitality" && c.Value == 1);
        }

        [Fact]
        public void Save_UnregisteredUnit_Throws()
        {
            var registry = MakeRegistry(out var bus);
            var host = MakeHost(registry, bus, new RecordingWriters());

            Assert.Throws<ArgumentException>(() => ProgressionPersistable.For(host, Unit).Save());
        }

        /// <summary>
        /// AUD-02 根治（architecture/落地计划/audit-85f1f4f-20260908，P2）：修复前
        /// <c>Load</c> 对 <c>JsonNull</c> 直接 no-op 返回，运行期已有的等级/经验原样保留（同一
        /// 宿主先后加载两个存档槽时，缺本段的旧档不会清掉前一个槽留下的等级）。本用例覆盖"该单位
        /// 已注册"这一分支：先升到 2 级，再 <c>Load(JsonNull)</c>，断言重置回 1 级、0 经验——与
        /// 下面 <see cref="Load_NullData_LeavesUnitUnregistered"/>（该单位从未注册，没有曲线可
        /// 沿用，保持未注册不抛异常）是两个不同前提的分支。
        /// </summary>
        [Fact]
        public void Load_NullData_ResetsRegisteredUnitToLevelOneAndZeroXp()
        {
            var registry = MakeRegistry(out var bus);
            var host = MakeHost(registry, bus, new RecordingWriters());
            host.RegisterUnit(Unit, CurveId, startLevel: 1);
            host.AddXp(Unit, new Id("system.test"), 120); // 升到 2 级，剩余 20 经验。
            Assert.Equal(2, host.GetLevel(Unit));

            ProgressionPersistable.For(host, Unit).Load(JsonNull.Instance);

            Assert.Equal(1, host.GetLevel(Unit));
            Assert.Equal(0, host.GetXp(Unit));
        }

        [Fact]
        public void Load_NullData_LeavesUnitUnregistered()
        {
            var registry = MakeRegistry(out var bus);
            var host = MakeHost(registry, bus, new RecordingWriters());

            ProgressionPersistable.For(host, Unit).Load(JsonNull.Instance);

            Assert.Throws<ArgumentException>(() => host.GetLevel(Unit));
        }

        [Fact]
        public void Load_WrongShape_Throws()
        {
            var registry = MakeRegistry(out var bus);
            var host = MakeHost(registry, bus, new RecordingWriters());

            Assert.Throws<FormatException>(() => ProgressionPersistable.For(host, Unit).Load(new JsonString("nope")));
        }

        [Fact]
        public void RestoreState_LevelOutOfCurveRange_Throws()
        {
            var registry = MakeRegistry(out var bus);
            var host = MakeHost(registry, bus, new RecordingWriters());

            Assert.Throws<ArgumentException>(() => host.RestoreState(Unit, CurveId, level: 99, xp: 0));
        }
    }
}
