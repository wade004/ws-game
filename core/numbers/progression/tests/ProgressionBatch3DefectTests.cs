using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.Progression;
using Xunit;

namespace Tests.Numbers.Progression
{
    /// <summary>
    /// ADR-0125 第三批探针缺陷（ProgressionHost 四处）的复现 + 不变量用例：
    /// ① <c>AddXpCore</c> 的 <c>Xp += amount</c> 溢出静默回绕；② <see cref="ProgressionPersistable"/>
    /// <c>Load</c> 的 <c>level</c> (int) 截断；③ <c>RoundXp</c> 对有限但超出 long 范围的值依赖平台；
    /// ④ <c>RestoreState</c>/<c>Load</c> 不校验 xp（负数、达到本级升级门槛）。
    /// </summary>
    public sealed class ProgressionBatch3DefectTests
    {
        private static readonly Id Unit = new Id("unit.hero");
        private static readonly Id SampleCurve = new Id("prog.curve.sample");
        private static readonly Id BigCurve = new Id("prog.curve.big");
        private static readonly Id WolfSource = new Id("prog.xp.kill_wolf");
        private static readonly Id HugeSource = new Id("prog.xp.huge_weight");

        private const string CurveRows = @"[
            {
                ""id"": ""prog.curve.sample"",
                ""max_level"": 3,
                ""entries"": [
                    { ""level"": 1, ""xp_to_next"": 100, ""growth"": {} },
                    { ""level"": 2, ""xp_to_next"": 150, ""growth"": {} },
                    { ""level"": 3, ""xp_to_next"": 0, ""growth"": {} }
                ]
            },
            {
                ""id"": ""prog.curve.big"",
                ""max_level"": 3,
                ""entries"": [
                    { ""level"": 1, ""xp_to_next"": 9223372036854775807, ""growth"": {} },
                    { ""level"": 2, ""xp_to_next"": 9223372036854775807, ""growth"": {} },
                    { ""level"": 3, ""xp_to_next"": 0, ""growth"": {} }
                ]
            }
        ]";

        private const string XpSourceRows = @"[
            { ""id"": ""prog.xp.kill_wolf"", ""base_xp"": 40, ""weight"": 1.5 },
            { ""id"": ""prog.xp.huge_weight"", ""base_xp"": 40, ""weight"": 1e30 }
        ]";

        private sealed class Fixture
        {
            public ProgressionHost Host = null!;
            public IEventBus Bus = null!;
            public List<XpGainedEvent> XpEvents = new List<XpGainedEvent>();
            public int WriteCount;
            public int RemoveCount;
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static Fixture Build()
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
                new EventDefinition(ProgressionEventKeys.StateRestored, "progression",
                    new[] { "unitId", "level" }),
            });
            var bus = new EventBus(catalog);
            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition",
                    @"[ { ""id"": ""stat.strength"", ""name_key"": ""l10n.stat.strength.name"", ""group"": ""primary"" } ]"))
                .Add("prog.level_curve", Envelope("prog.level_curve", CurveRows))
                .Add("prog.xp_source", Envelope("prog.xp_source", XpSourceRows));
            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(ProgSchemas.LevelCurve);
            registry.RegisterSchema(ProgSchemas.XpSource);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var f = new Fixture { Bus = bus };
            f.Host = new ProgressionHost(registry, bus,
                (u, s, op, v, src) => f.WriteCount++,
                (u, src) => f.RemoveCount++);
            bus.Subscribe<XpGainedEvent>(ProgressionEventKeys.XpGained, e => f.XpEvents.Add(e));
            return f;
        }

        private static JsonValue Snapshot(Id curve, long level, long xp) =>
            new JsonObjectBuilder()
                .Add("curve_id", new JsonString(curve.Value))
                .Add("level", JsonNumber.FromInt64(level))
                .Add("xp", JsonNumber.FromInt64(xp))
                .Build();

        // -----------------------------------------------------------------
        // ① AddXpCore 的 Xp += amount 溢出
        // -----------------------------------------------------------------

        /// <summary>复现：xp 已接近 long.MaxValue 时再入账会回绕成负数（修前静默，xp 变成负值且
        /// XpGainedEvent 照发）。不变量：溢出 ⇔ amount &gt; long.MaxValue − 当前 xp；溢出时抛
        /// <see cref="ArgumentOutOfRangeException"/>，等级/经验/事件均不变。</summary>
        [Fact]
        public void AddXp_WouldOverflowLong_Throws_AndLeavesStateAndEventsUntouched()
        {
            var f = Build();
            var start = long.MaxValue - 1000;
            f.Host.RestoreState(Unit, BigCurve, 1, start);
            var room = long.MaxValue - f.Host.GetXp(Unit); // 规则：还能容纳的最大增量

            Assert.Throws<ArgumentOutOfRangeException>(() => f.Host.AddXp(Unit, WolfSource, room + 1));

            Assert.Equal(start, f.Host.GetXp(Unit));
            Assert.Equal(1, f.Host.GetLevel(Unit));
            Assert.Empty(f.XpEvents);
        }

        /// <summary>阳性对照：不溢出（且不到升级门槛）的大额入账照常生效。</summary>
        [Fact]
        public void AddXp_BelowOverflowAndBelowThreshold_IsAcceptedWithoutWrap()
        {
            var f = Build();
            var start = long.MaxValue - 1000;
            f.Host.RestoreState(Unit, BigCurve, 1, start);
            var threshold = f.Host.GetXpToNext(Unit);
            var amount = threshold - 1 - start; // 落在升级门槛前一点：不溢出、不升级

            f.Host.AddXp(Unit, WolfSource, amount);

            Assert.Equal(threshold - 1, f.Host.GetXp(Unit));
            Assert.Equal(1, f.Host.GetLevel(Unit));
            Assert.Single(f.XpEvents);
        }

        // -----------------------------------------------------------------
        // ③ RoundXp：有限但超出 long 范围
        // -----------------------------------------------------------------

        [Fact]
        public void GrantFromSource_FiniteMultiplierBeyondLongRange_Throws_AndStateUnchanged()
        {
            var f = Build();
            f.Host.RegisterUnit(Unit, SampleCurve);

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => f.Host.GrantFromSource(Unit, WolfSource, 1e30));

            // 在舍入处就诊断（参数名 raw），而不是借平台相关的 (long) 转换结果漏成下游的"经验增量不能为负"。
            Assert.Equal("raw", ex.ParamName);

            Assert.Equal(0, f.Host.GetXp(Unit));
            Assert.Equal(1, f.Host.GetLevel(Unit));
            Assert.Empty(f.XpEvents);
        }

        [Fact]
        public void GrantXp_SourceWeightBeyondLongRange_Throws_AndStateUnchanged()
        {
            var f = Build();
            f.Host.RegisterUnit(Unit, SampleCurve);

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => f.Host.GrantXp(Unit, HugeSource, new XpContext(1)));

            Assert.Equal("raw", ex.ParamName);

            Assert.Equal(0, f.Host.GetXp(Unit));
            Assert.Empty(f.XpEvents);
        }

        /// <summary>现行为保留：负结果钳 0，不抛、不入账。</summary>
        [Fact]
        public void GrantFromSource_NegativeMultiplier_ClampsToZero_NoThrow()
        {
            var f = Build();
            f.Host.RegisterUnit(Unit, SampleCurve);

            f.Host.GrantFromSource(Unit, WolfSource, -2.0);
            f.Host.GrantFromSource(Unit, WolfSource, -1e30);

            Assert.Equal(0, f.Host.GetXp(Unit));
            Assert.Equal(1, f.Host.GetLevel(Unit));
        }

        // -----------------------------------------------------------------
        // ② Load 的 level (int) 截断
        // -----------------------------------------------------------------

        /// <summary>复现：level = 2^32 + 1 被 (int) 截断回绕成 1，读档"成功"但读回的是错的等级；
        /// level = int.MaxValue + 1 回绕成负数则走到 ArgumentException 而不是形状错误。期望统一
        /// <see cref="FormatException"/>，宿主状态不变。</summary>
        [Theory]
        [InlineData(1L << 32)]
        [InlineData((1L << 32) + 1)]
        [InlineData((long)int.MaxValue + 1)]
        [InlineData((long)int.MinValue - 1)]
        public void Load_LevelBeyondIntRange_ThrowsFormatException_AndStateUnchanged(long level)
        {
            var f = Build();
            f.Host.RegisterUnit(Unit, SampleCurve);
            f.Host.AddXp(Unit, WolfSource, 30);
            var persistable = ProgressionPersistable.For(f.Host, Unit);

            Assert.Throws<FormatException>(() => persistable.Load(Snapshot(SampleCurve, level, 0)));

            Assert.Equal(1, f.Host.GetLevel(Unit));
            Assert.Equal(30, f.Host.GetXp(Unit));
        }

        // -----------------------------------------------------------------
        // ④ RestoreState / Load 校验 xp
        // -----------------------------------------------------------------

        [Fact]
        public void Load_NegativeXp_ThrowsFormatException_AndStateUnchanged()
        {
            var f = Build();
            f.Host.RegisterUnit(Unit, SampleCurve);
            f.Host.AddXp(Unit, WolfSource, 30);
            var writes = f.WriteCount;
            var persistable = ProgressionPersistable.For(f.Host, Unit);

            Assert.Throws<FormatException>(() => persistable.Load(Snapshot(SampleCurve, 1, -1)));

            Assert.Equal(1, f.Host.GetLevel(Unit));
            Assert.Equal(30, f.Host.GetXp(Unit));
            Assert.Equal(writes, f.WriteCount);
        }

        /// <summary>不变量：对每个低于满级的等级 L，xp 取值 [0, 门槛(L) − 1] 被接受，xp ≥ 门槛(L) 被拒绝；
        /// 门槛由宿主自己（<see cref="ProgressionHost.GetXpToNext"/>）给出，不写死。</summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void Load_XpAtOrAboveLevelThreshold_ThrowsFormatException_BelowIsAccepted(int level)
        {
            var f = Build();
            f.Host.RegisterUnit(Unit, SampleCurve, level);
            var threshold = f.Host.GetXpToNext(Unit);
            var persistable = ProgressionPersistable.For(f.Host, Unit);

            Assert.Throws<FormatException>(() => persistable.Load(Snapshot(SampleCurve, level, threshold)));
            Assert.Throws<FormatException>(() => persistable.Load(Snapshot(SampleCurve, level, threshold + 1)));
            Assert.Equal(0, f.Host.GetXp(Unit));

            persistable.Load(Snapshot(SampleCurve, level, threshold - 1));

            Assert.Equal(level, f.Host.GetLevel(Unit));
            Assert.Equal(threshold - 1, f.Host.GetXp(Unit));
        }

        [Fact]
        public void RestoreState_NegativeXpOrXpAtThreshold_Throws_AndKeepsPreviousState()
        {
            var f = Build();
            f.Host.RegisterUnit(Unit, SampleCurve);
            f.Host.AddXp(Unit, WolfSource, 30);
            var threshold = f.Host.GetXpToNext(Unit);

            Assert.Throws<FormatException>(() => f.Host.RestoreState(Unit, SampleCurve, 1, -5));
            Assert.Throws<FormatException>(() => f.Host.RestoreState(Unit, SampleCurve, 1, threshold));

            Assert.Equal(1, f.Host.GetLevel(Unit));
            Assert.Equal(30, f.Host.GetXp(Unit));
        }

        /// <summary>满级单位不再有升级门槛：xp 0 合法，负数仍被拒绝。</summary>
        [Fact]
        public void Load_AtMaxLevel_ZeroXpAccepted_NegativeRejected()
        {
            var f = Build();
            f.Host.RegisterUnit(Unit, SampleCurve);
            const int maxLevel = 3;
            var persistable = ProgressionPersistable.For(f.Host, Unit);

            persistable.Load(Snapshot(SampleCurve, maxLevel, 0));
            Assert.Equal(maxLevel, f.Host.GetLevel(Unit));

            Assert.Throws<FormatException>(() => persistable.Load(Snapshot(SampleCurve, maxLevel, -1)));
            Assert.Equal(maxLevel, f.Host.GetLevel(Unit));
        }
    }
}
