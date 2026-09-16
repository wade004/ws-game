using System;
using System.Collections.Generic;
using System.Text;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.Progression;
using Xunit;

namespace Tests.Numbers.Progression
{
    public class ProgressionHostTests
    {
        // -----------------------------------------------------------------
        // 公共夹具
        // -----------------------------------------------------------------

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
            });
            return new EventBus(catalog);
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        // 标准三级曲线：1→2 需 100 经验（成长 {stat.strength:2, stat.vitality:1}），
        // 2→3 需 100 经验（成长 {stat.strength:3}），3 级（满级）xp_to_next=0。
        // T-N0-5：xp_to_next 沿等级不递减（level_curve_xp_monotonic），2→3 由 50 改为 100（允许相等）。
        private const string GoodCurveRows = @"[
            {
                ""id"": ""prog.curve.sample"",
                ""max_level"": 3,
                ""entries"": [
                    { ""level"": 1, ""xp_to_next"": 100, ""growth"": {} },
                    { ""level"": 2, ""xp_to_next"": 100, ""growth"": { ""stat.strength"": 2, ""stat.vitality"": 1 } },
                    { ""level"": 3, ""xp_to_next"": 0, ""growth"": { ""stat.strength"": 3 } }
                ]
            }
        ]";

        private const string XpSourceRows = @"[
            { ""id"": ""prog.xp.kill_wolf"", ""base_xp"": 40, ""weight"": 1.5 }
        ]";

        /// <summary>ADR-0024 第二批登记：<c>prog.level_curve.entries[].growth</c> 现登记
        /// <c>MapSchema.ReferenceKeyTable("stat.definition", ...)</c>，<see cref="GoodCurveRows"/> 用到的
        /// stat.strength/stat.vitality 两个键必须能在 stat.definition 表里查到（其余曲线夹具的 growth
        /// 均为空对象，不受影响）。</summary>
        private const string StatDefinitionRows = @"[
            { ""id"": ""stat.strength"", ""name_key"": ""l10n.stat.strength.name"", ""group"": ""primary"" },
            { ""id"": ""stat.vitality"", ""name_key"": ""l10n.stat.vitality.name"", ""group"": ""primary"" }
        ]";

        private static DataRegistry MakeRegistry(string curveRowsJson, out IEventBus bus, bool registerCurveRule = false)
        {
            bus = MakeBus();
            var source = new InMemoryDataSource()
                .Add("stat.definition", Envelope("stat.definition", StatDefinitionRows))
                .Add("prog.level_curve", Envelope("prog.level_curve", curveRowsJson))
                .Add("prog.xp_source", Envelope("prog.xp_source", XpSourceRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(Core.Numbers.StatBlock.StatSchemas.Definition);
            registry.RegisterSchema(ProgSchemas.LevelCurve);
            registry.RegisterSchema(ProgSchemas.XpSource);
            if (registerCurveRule)
            {
                registry.RegisterValidationRule(new ProgLevelCurveValidationRule());
            }
            return registry;
        }

        private sealed class WriterCall
        {
            public Id UnitId;
            public Id Stat;
            public string Op = "";
            public double Value;
            public Id SourceId;
        }

        private sealed class RecordingWriters
        {
            public readonly List<WriterCall> WriteCalls = new List<WriterCall>();
            public readonly List<(Id UnitId, Id SourceId)> RemoveCalls = new List<(Id, Id)>();

            public void Write(Id unitId, Id stat, string op, double value, Id sourceId) =>
                WriteCalls.Add(new WriterCall { UnitId = unitId, Stat = stat, Op = op, Value = value, SourceId = sourceId });

            public void Remove(Id unitId, Id sourceId) => RemoveCalls.Add((unitId, sourceId));
        }

        private static ProgressionHost MakeHost(
            IDataRegistryView registry, IEventBus bus, RecordingWriters writers, IProgressionDiagnostics? diagnostics = null) =>
            new ProgressionHost(registry, bus, writers.Write, writers.Remove, diagnostics);

        /// <summary>T-N4-4：接 <see cref="ProgressionOptions"/> 的构造重载，供
        /// <see cref="ProgressionOptions.ExtraXpMultiplierProvider"/> 系列用例使用。</summary>
        private static ProgressionHost MakeHostWithOptions(
            IDataRegistryView registry, IEventBus bus, RecordingWriters writers, ProgressionOptions options) =>
            new ProgressionHost(registry, bus, writers.Write, writers.Remove, options);

        // -----------------------------------------------------------------
        // T-N4-2 夹具：grantXp(XpContext) 三种来源公式、GetXpToNext/grantXp 满级归零、
        // GrantFromSource 曲线优先兼容。见 IProgressionHost.GrantXp/ComputeCurveBasedRawAmount
        // 判断记录（公式出处）。
        // -----------------------------------------------------------------

        /// <summary>生成一条"每级 <paramref name="xpToNextPerLevel"/> 经验、末级 0"的等级曲线行，
        /// 供只关心"满级判定"/"当量折算"、不关心具体升级门槛的用例复用（升级门槛设得足够大，
        /// 避免用例里的少量经验意外触发升级，干扰对"入账值"本身的断言）。</summary>
        private static string BuildFlatCurveRows(string id, int maxLevel, long xpToNextPerLevel)
        {
            var sb = new StringBuilder();
            sb.Append("[{\"id\":\"").Append(id).Append("\",\"max_level\":").Append(maxLevel).Append(",\"entries\":[");
            for (var level = 1; level <= maxLevel; level++)
            {
                if (level > 1) sb.Append(',');
                var xpToNext = level == maxLevel ? 0 : xpToNextPerLevel;
                sb.Append("{\"level\":").Append(level).Append(",\"xp_to_next\":").Append(xpToNext).Append(",\"growth\":{}}");
            }
            sb.Append("]}]");
            return sb.ToString();
        }

        // 领取者曲线：1..20 级，每级门槛 100000（用例里的入账值都远小于这个数，不会意外升级），
        // 20 级（曲线自身 max_level）xp_to_next=0。与来源方（怪物/任务/区域）的等级刻意分开建模，
        // 覆盖 Δ = 来源等级 − 领取者等级可正可负的场景。
        private static readonly string N42CurveRows = BuildFlatCurveRows("prog.curve.n42", 20, 100_000);

        // 击杀基数曲线：Evaluate(x) = 100x（x∈[1,11] 内精确成立，两点线性），供手算核对。
        private const string N42XpBaseCurveRows =
            "[{\"id\":\"prog.xp_base_curve.n42\",\"entries\":[{\"x\":1,\"y\":100},{\"x\":11,\"y\":1100}]}]";

        // 等级差系数：Δ=-5→0.1（越级碾压大幅削减）、Δ=0→1.0（同级基准）、Δ=5→1.5（越级挑战小幅
        // 加成），三点两段线性，与 combat.level_diff_table.xp_factor 的登记形态（CurveAxis.LevelDiff）
        // 一致（ADR-0033 决策 3/5）。
        private const string N42LevelDiffRows =
            "[{\"id\":\"combat.level_diff.n42\",\"xp_factor\":[{\"x\":-5,\"y\":0.1},{\"x\":0,\"y\":1.0},{\"x\":5,\"y\":1.5}]}]";

        // 六条经验来源：kill/quest 各带 base_curve_ref + level_diff_ref；discovery 一条不接
        // level_diff_ref（验证"探索本就不查表"），另一条故意也接上 level_diff_ref（验证"即便接了
        // 也不生效"，见 ComputeCurveBasedRawAmount 判断记录）；倒数第二条只用于"旧字段兼容：曲线
        // 优先"用例，故意同时保留 base_xp/weight 与 base_curve_ref、不填 kind（兜底按 kill 处理）；
        // 末一条（2026-09-16 深度复审 D-S3）只登记 base_curve_ref（base_xp 只是满足 schema
        // required:true、不影响折算——base_curve_ref 存在时优先），不填 kind，专供
        // GrantXp_NoKind_FallsBackToKillBranch_UsesExtraXpMultiplierProvider 用例锁定
        // "kind 缺省确实执行 kill 分支代码路径"（不是仅数值上恰好与 kill 分支一致）。
        private const string N42XpSourceRows = @"[
            { ""id"": ""prog.xp.kill_n42"", ""kind"": ""kill"", ""base_xp"": 1,
              ""base_curve_ref"": ""prog.xp_base_curve.n42"", ""level_diff_ref"": ""combat.level_diff.n42"" },
            { ""id"": ""prog.xp.quest_n42"", ""kind"": ""quest"", ""base_xp"": 1,
              ""base_curve_ref"": ""prog.xp_base_curve.n42"", ""level_diff_ref"": ""combat.level_diff.n42"" },
            { ""id"": ""prog.xp.discovery_n42"", ""kind"": ""discovery"", ""base_xp"": 1,
              ""base_curve_ref"": ""prog.xp_base_curve.n42"" },
            { ""id"": ""prog.xp.discovery_ignorediff_n42"", ""kind"": ""discovery"", ""base_xp"": 1,
              ""base_curve_ref"": ""prog.xp_base_curve.n42"", ""level_diff_ref"": ""combat.level_diff.n42"" },
            { ""id"": ""prog.xp.legacy_with_curve_n42"", ""base_xp"": 999, ""weight"": 2,
              ""base_curve_ref"": ""prog.xp_base_curve.n42"" },
            { ""id"": ""prog.xp.no_kind_n42"", ""base_xp"": 1, ""base_curve_ref"": ""prog.xp_base_curve.n42"" }
        ]";

        /// <summary>占位 <c>combat.level_diff_table</c> schema——真实字段表归 <c>Core.Rules.Combat.
        /// CombatSchemas.LevelDiffTable</c>（本项目 <c>Tests.Numbers</c> 不引用 <c>Core.Rules</c>
        /// 程序集，同 <c>ProgSchemaCoverageTests.cs</c>"combat.level_diff_table 归 Core.Rules"判断
        /// 记录），这里只登记 <see cref="ProgressionHost"/> 实际会读的两个字段（<c>id</c>/
        /// <c>xp_factor</c>），供 <c>reference_integrity</c> 校验与 <c>CurveSchema.ReadBreakpoints</c>
        /// 使用。</summary>
        private static readonly TableSchema LevelDiffTableStub = new TableSchema(
            name: "combat.level_diff_table",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "占位"),
                CurveSchema.BreakpointsField("xp_factor", CurveAxis.LevelDiff, required: true,
                    description: "经验系数断点表，横轴 Δ（T-N4-2 测试站位登记）"),
            });

        private static DataRegistry MakeGrantXpRegistry(out IEventBus bus)
        {
            bus = MakeBus();
            var source = new InMemoryDataSource()
                .Add("prog.level_curve", Envelope("prog.level_curve", N42CurveRows))
                .Add("prog.xp_source", Envelope("prog.xp_source", N42XpSourceRows))
                .Add("prog.xp_base_curve", Envelope("prog.xp_base_curve", N42XpBaseCurveRows))
                .Add("combat.level_diff_table", Envelope("combat.level_diff_table", N42LevelDiffRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(ProgSchemas.LevelCurve);
            registry.RegisterSchema(ProgSchemas.XpSource);
            registry.RegisterSchema(ProgSchemas.XpBaseCurve);
            registry.RegisterSchema(LevelDiffTableStub);
            registry.RegisterValidationRule(new ProgLevelCurveValidationRule());
            return registry;
        }

        // -----------------------------------------------------------------
        // 1. 曲线加载与校验（不连续报错）
        // -----------------------------------------------------------------

        [Fact]
        public void ProgressionHost_Constructor_DiscontinuousEntries_Throws()
        {
            const string badRows = @"[
                {
                    ""id"": ""prog.curve.bad"",
                    ""max_level"": 3,
                    ""entries"": [
                        { ""level"": 1, ""xp_to_next"": 100, ""growth"": {} },
                        { ""level"": 3, ""xp_to_next"": 50, ""growth"": {} },
                        { ""level"": 4, ""xp_to_next"": 0, ""growth"": {} }
                    ]
                }
            ]";

            var registry = MakeRegistry(badRows, out var bus);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking); // 未注册 ProgLevelCurveValidationRule，内置检查不识别这条业务规则

            var writers = new RecordingWriters();
            Assert.Throws<ArgumentException>(() => MakeHost(registry, bus, writers));
        }

        [Fact]
        public void ProgLevelCurveValidationRule_DiscontinuousEntries_ReportsBlockingError()
        {
            const string badRows = @"[
                {
                    ""id"": ""prog.curve.bad"",
                    ""max_level"": 2,
                    ""entries"": [
                        { ""level"": 1, ""xp_to_next"": 100, ""growth"": {} },
                        { ""level"": 1, ""xp_to_next"": 0, ""growth"": {} }
                    ]
                }
            ]";

            var registry = MakeRegistry(badRows, out _, registerCurveRule: true);
            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "level_curve_continuity");
        }

        // -----------------------------------------------------------------
        // 2. 加经验不升级
        // -----------------------------------------------------------------

        [Fact]
        public void AddXp_BelowThreshold_NoLevelUp_NoGrowthWritten()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.hero_01");
            host.RegisterUnit(unit, new Id("prog.curve.sample"));

            LevelUpEvent? levelUp = null;
            bus.Subscribe<LevelUpEvent>(ProgressionEventKeys.LevelUp, e => levelUp = e);
            XpGainedEvent? xpGained = null;
            bus.Subscribe<XpGainedEvent>(ProgressionEventKeys.XpGained, e => xpGained = e);

            host.AddXp(unit, new Id("prog.xp.kill_wolf"), 30);

            Assert.Equal(1, host.GetLevel(unit));
            Assert.Equal(30, host.GetXp(unit));
            Assert.Null(levelUp);
            Assert.NotNull(xpGained);
            Assert.Equal(30, xpGained!.Amount);
            Assert.Empty(writers.WriteCalls);
            Assert.Empty(writers.RemoveCalls);
        }

        // -----------------------------------------------------------------
        // 3. 恰好升级
        // -----------------------------------------------------------------

        [Fact]
        public void AddXp_ExactThreshold_LevelsUpOnce_AndWritesGrowth()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.hero_02");
            host.RegisterUnit(unit, new Id("prog.curve.sample"));

            var levelUps = new List<LevelUpEvent>();
            bus.Subscribe<LevelUpEvent>(ProgressionEventKeys.LevelUp, e => levelUps.Add(e));

            host.AddXp(unit, new Id("prog.xp.kill_wolf"), 100);

            Assert.Equal(2, host.GetLevel(unit));
            Assert.Equal(0, host.GetXp(unit));
            Assert.Single(levelUps);
            Assert.Equal(1, levelUps[0].OldLevel);
            Assert.Equal(2, levelUps[0].NewLevel);

            Assert.Single(writers.RemoveCalls);
            Assert.Equal((unit, new Id("prog.growth")), writers.RemoveCalls[0]);

            Assert.Equal(2, writers.WriteCalls.Count);
            var strength = writers.WriteCalls.Find(c => c.Stat.Value == "stat.strength");
            var vitality = writers.WriteCalls.Find(c => c.Stat.Value == "stat.vitality");
            Assert.NotNull(strength);
            Assert.Equal(2, strength!.Value);
            Assert.Equal("flat", strength.Op);
            Assert.Equal("prog.growth", strength.SourceId.Value);
            Assert.NotNull(vitality);
            Assert.Equal(1, vitality!.Value);
        }

        // -----------------------------------------------------------------
        // 4. 一次跨两级发两次 level_up 且成长累计写入正确
        // -----------------------------------------------------------------

        [Fact]
        public void AddXp_CrossesTwoLevels_FiresTwoLevelUpEvents_AndWritesCumulativeGrowthOnce()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.hero_03");
            host.RegisterUnit(unit, new Id("prog.curve.sample"));

            var levelUps = new List<LevelUpEvent>();
            bus.Subscribe<LevelUpEvent>(ProgressionEventKeys.LevelUp, e => levelUps.Add(e));

            // 1→2 需 100，2→3 需 100，3 级是满级：给 250 经验足够连跨两级，残余 50 在满级时被丢弃。
            host.AddXp(unit, new Id("prog.xp.kill_wolf"), 250);

            Assert.Equal(3, host.GetLevel(unit));
            Assert.Equal(0, host.GetXp(unit)); // 满级残余经验被丢弃

            Assert.Equal(2, levelUps.Count);
            Assert.Equal((1, 2), (levelUps[0].OldLevel, levelUps[0].NewLevel));
            Assert.Equal((2, 3), (levelUps[1].OldLevel, levelUps[1].NewLevel));

            // 成长累计只写一次：level2 + level3 的 growth 之和。
            Assert.Single(writers.RemoveCalls);
            var strengthTotal = writers.WriteCalls.Find(c => c.Stat.Value == "stat.strength");
            var vitalityTotal = writers.WriteCalls.Find(c => c.Stat.Value == "stat.vitality");
            Assert.NotNull(strengthTotal);
            Assert.Equal(5, strengthTotal!.Value); // 2 (level2) + 3 (level3)
            Assert.NotNull(vitalityTotal);
            Assert.Equal(1, vitalityTotal!.Value); // 只在 level2 出现
        }

        // -----------------------------------------------------------------
        // N08（外部审计 68c9bed，P2）：level_up 发布顺序——先提交等级、再发布事件
        // -----------------------------------------------------------------

        /// <summary>修复前 <c>PublishImmediate(LevelUpEvent)</c> 早于 <c>unit.Level = newLevel</c>
        /// 赋值；事件处理器内如果不读事件自带的 <c>NewLevel</c> 字段、而是反查
        /// <see cref="IProgressionHost.GetLevel"/>（如 <c>RulesAssembly</c> 一类按等级重算派生
        /// 属性的消费者，见 core/rules/assembly/RulesAssembly.cs:227），会读到升级前的旧等级。</summary>
        [Fact]
        public void AddXp_LevelUpHandler_ReadsGetLevel_SeesNewLevelAlready()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.hero_leveluporder");
            host.RegisterUnit(unit, new Id("prog.curve.sample"));

            var levelObservedInsideHandler = -1;
            bus.Subscribe<LevelUpEvent>(ProgressionEventKeys.LevelUp, e => levelObservedInsideHandler = host.GetLevel(unit));

            host.AddXp(unit, new Id("prog.xp.kill_wolf"), 100);

            Assert.Equal(2, host.GetLevel(unit));
            // 修复前该断言会失败：levelObservedInsideHandler 会是 1（发布事件时 unit.Level 还没提交）。
            Assert.Equal(2, levelObservedInsideHandler);
        }

        /// <summary>一次跨两级时，第一次 level_up 事件的处理器内查询到的等级应恰好是第一次跳变后的
        /// 等级（2），不是最终等级（3）——验证的是"逐级提交再逐级发布"，不是"先跑完循环再统一发布"。</summary>
        [Fact]
        public void AddXp_CrossesTwoLevels_EachHandlerInvocation_SeesLevelAtThatPointInTime()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.hero_leveluporder2");
            host.RegisterUnit(unit, new Id("prog.curve.sample"));

            var observedLevels = new List<int>();
            bus.Subscribe<LevelUpEvent>(ProgressionEventKeys.LevelUp, e => observedLevels.Add(host.GetLevel(unit)));

            host.AddXp(unit, new Id("prog.xp.kill_wolf"), 200);

            Assert.Equal(new List<int> { 2, 3 }, observedLevels);
        }

        // -----------------------------------------------------------------
        // 5. 满级丢弃
        // -----------------------------------------------------------------

        [Fact]
        public void AddXp_AtMaxLevel_DiscardsXp_NoEvent_LogsDiagnostic()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var diagnostics = new InMemoryProgressionDiagnostics();
            var host = MakeHost(registry, bus, writers, diagnostics);
            var unit = new Id("unit.hero_04");
            host.RegisterUnit(unit, new Id("prog.curve.sample"), startLevel: 3);

            XpGainedEvent? xpGained = null;
            bus.Subscribe<XpGainedEvent>(ProgressionEventKeys.XpGained, e => xpGained = e);

            host.AddXp(unit, new Id("prog.xp.kill_wolf"), 999);

            Assert.Equal(3, host.GetLevel(unit));
            Assert.Equal(0, host.GetXp(unit));
            Assert.Null(xpGained);
            Assert.Empty(writers.WriteCalls);
            Assert.Empty(writers.RemoveCalls);
            Assert.NotEmpty(diagnostics.Warnings);
        }

        // -----------------------------------------------------------------
        // 6. GrantFromSource 按 weight 计算
        // -----------------------------------------------------------------

        [Fact]
        public void GrantFromSource_ComputesAmountFromBaseXpWeightAndMultiplier()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.hero_05");
            host.RegisterUnit(unit, new Id("prog.curve.sample"));

            XpGainedEvent? xpGained = null;
            bus.Subscribe<XpGainedEvent>(ProgressionEventKeys.XpGained, e => xpGained = e);

            // base_xp=40, weight=1.5, multiplier=0.5 → 30（刻意留在 1 级门槛 100 之下，
            // 避免这条只测"数值换算"的用例意外触发升级，升级路径由专门的用例覆盖）
            host.GrantFromSource(unit, new Id("prog.xp.kill_wolf"), multiplier: 0.5);

            Assert.NotNull(xpGained);
            Assert.Equal(30, xpGained!.Amount);
            Assert.Equal(30, host.GetXp(unit));
        }

        [Fact]
        public void GrantFromSource_UnknownSource_Throws()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.hero_06");
            host.RegisterUnit(unit, new Id("prog.curve.sample"));

            Assert.Throws<ArgumentException>(() => host.GrantFromSource(unit, new Id("prog.xp.unknown")));
        }

        // -----------------------------------------------------------------
        // 7. 未注册单位异常
        // -----------------------------------------------------------------

        [Fact]
        public void GetLevel_UnregisteredUnit_Throws()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);

            Assert.Throws<ArgumentException>(() => host.GetLevel(new Id("unit.ghost")));
            Assert.Throws<ArgumentException>(() => host.AddXp(new Id("unit.ghost"), new Id("prog.xp.kill_wolf"), 1));
        }

        // -----------------------------------------------------------------
        // 8. 额外覆盖：起始等级非法 / 满级 GetXpToNext
        // -----------------------------------------------------------------

        [Fact]
        public void RegisterUnit_InvalidStartLevel_Throws()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);

            Assert.Throws<ArgumentException>(() => host.RegisterUnit(new Id("unit.hero_07"), new Id("prog.curve.sample"), startLevel: 0));
            Assert.Throws<ArgumentException>(() => host.RegisterUnit(new Id("unit.hero_08"), new Id("prog.curve.sample"), startLevel: 4));
        }

        [Fact]
        public void GetXpToNext_AtMaxLevel_ReturnsConfiguredValue()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.hero_09");
            host.RegisterUnit(unit, new Id("prog.curve.sample"), startLevel: 3);

            Assert.Equal(0, host.GetXpToNext(unit));
        }

        [Fact]
        public void RegisterUnit_UnknownCurve_Throws()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);

            Assert.Throws<ArgumentException>(() => host.RegisterUnit(new Id("unit.hero_10"), new Id("prog.curve.unknown")));
        }

        // -----------------------------------------------------------------
        // 9. 消费方反馈第 36 条根治：ApplyGrowthToCurrentLevel（供出生等级 > 1 的单位一次性补写
        // 成长，与 AddXp/RestoreState 共用同一段聚合实现，见 ProgressionHost 判断记录 8）
        // -----------------------------------------------------------------

        [Fact]
        public void ApplyGrowthToCurrentLevel_RegisterAtLevel2_WritesLevel2GrowthOnly()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.hero_e36_01");
            host.RegisterUnit(unit, new Id("prog.curve.sample"), startLevel: 2);

            host.ApplyGrowthToCurrentLevel(unit);

            Assert.Single(writers.RemoveCalls);
            Assert.Equal((unit, new Id("prog.growth")), writers.RemoveCalls[0]);

            Assert.Equal(2, writers.WriteCalls.Count);
            var strength = writers.WriteCalls.Find(c => c.Stat.Value == "stat.strength");
            var vitality = writers.WriteCalls.Find(c => c.Stat.Value == "stat.vitality");
            Assert.NotNull(strength);
            Assert.Equal(2, strength!.Value); // 只有 level2 的成长，不含 level3
            Assert.NotNull(vitality);
            Assert.Equal(1, vitality!.Value);
        }

        /// <summary>出生等级 2 之后紧接着真实升到 3 级（AddXp）：<see cref="ApplyGrowthToCurrentLevel"/>
        /// 先写入的 level2 段修正必须被 <see cref="IProgressionHost.AddXp"/> 触发的 <c>ApplyGrowth</c>
        /// 整段覆盖为 level2+level3 的合计值，不是在 level2 的基础上再叠加 level3——这正是消费方
        /// 反馈第 36 条要求的"出生施加"与"升级"共用同一份聚合实现、不重复计入的验收断言。</summary>
        [Fact]
        public void ApplyGrowthToCurrentLevel_ThenAddXpLevelsUp_FinalGrowthIsNotDoubleCounted()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.hero_e36_02");
            host.RegisterUnit(unit, new Id("prog.curve.sample"), startLevel: 2);
            host.ApplyGrowthToCurrentLevel(unit);

            // 2→3 需 100 经验（GoodCurveRows）。
            host.AddXp(unit, new Id("prog.xp.kill_wolf"), 100);

            Assert.Equal(3, host.GetLevel(unit));
            Assert.Equal(2, writers.RemoveCalls.Count); // 出生施加一次 + 升级再一次，均是整体重写前先清空

            // 最终一次写入的才是"当前生效"的修正：level2(2)+level3(3)=5，不是 2+5=7。
            var lastStrengthWrite = writers.WriteCalls.FindLast(c => c.Stat.Value == "stat.strength");
            Assert.NotNull(lastStrengthWrite);
            Assert.Equal(5, lastStrengthWrite!.Value);
        }

        [Fact]
        public void ApplyGrowthToCurrentLevel_IsIdempotent_RepeatedCallsDoNotAccumulate()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.hero_e36_03");
            host.RegisterUnit(unit, new Id("prog.curve.sample"), startLevel: 3);

            host.ApplyGrowthToCurrentLevel(unit);
            host.ApplyGrowthToCurrentLevel(unit);
            host.ApplyGrowthToCurrentLevel(unit);

            Assert.Equal(3, writers.RemoveCalls.Count); // 每次调用都先清空
            // 每次重算都是同一个整体累计值（level2+level3=5），不会因为调用三次而变成 15。
            foreach (var call in writers.WriteCalls)
            {
                if (call.Stat.Value == "stat.strength")
                {
                    Assert.Equal(5, call.Value);
                }
            }
        }

        [Fact]
        public void ApplyGrowthToCurrentLevel_AtBirthLevel1_NoGrowthCurve_NoOp()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.hero_e36_04");
            host.RegisterUnit(unit, new Id("prog.curve.sample")); // 默认 startLevel=1

            host.ApplyGrowthToCurrentLevel(unit);

            Assert.Single(writers.RemoveCalls); // 幂等清空仍会发生，但没有任何成长可写
            Assert.Empty(writers.WriteCalls);
        }

        [Fact]
        public void ApplyGrowthToCurrentLevel_UnregisteredUnit_Throws()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);

            Assert.Throws<ArgumentException>(() => host.ApplyGrowthToCurrentLevel(new Id("unit.ghost_e36")));
        }

        /// <summary>本方法不是"升级"也不是"读档恢复"，不应该发布 <see cref="LevelUpEvent"/> 或
        /// <see cref="ProgressionRestoredEvent"/>（见判断记录"语义诚实"）。</summary>
        [Fact]
        public void ApplyGrowthToCurrentLevel_DoesNotPublishLevelUpOrRestoredEvent()
        {
            var registry = MakeRegistry(GoodCurveRows, out var bus, registerCurveRule: true);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.hero_e36_05");
            host.RegisterUnit(unit, new Id("prog.curve.sample"), startLevel: 2);

            var levelUpFired = false;
            bus.Subscribe<LevelUpEvent>(ProgressionEventKeys.LevelUp, _ => levelUpFired = true);

            host.ApplyGrowthToCurrentLevel(unit);

            Assert.False(levelUpFired);
        }

        // -----------------------------------------------------------------
        // 10. T-N4-2：grantXp(XpContext) 三种来源手算（ADR-0033 决策 3）
        // -----------------------------------------------------------------

        /// <summary>击杀 · 用例 1：Δ=+5（怪物比领取者高 5 级）→ xp_factor=1.5（越级挑战加成）。
        /// baseAmount=xp_base_curve.Evaluate(6)=600；raw=600×1(倍率钩子未设置)×1.5=900。</summary>
        [Fact]
        public void GrantXp_Kill_PositiveDelta_AppliesNonOneXpFactor()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.n42_kill_01");
            host.RegisterUnit(unit, new Id("prog.curve.n42"), startLevel: 1);

            XpGainedEvent? xpGained = null;
            bus.Subscribe<XpGainedEvent>(ProgressionEventKeys.XpGained, e => xpGained = e);

            var granted = host.GrantXp(unit, new Id("prog.xp.kill_n42"), new XpContext(sourceLevel: 6));

            Assert.Equal(900, granted);
            Assert.NotNull(xpGained);
            Assert.Equal(900, xpGained!.Amount);
        }

        /// <summary>击杀 · 用例 2：Δ=-5（怪物比领取者低 5 级）→ xp_factor=0.1（越级碾压削减）。
        /// baseAmount=xp_base_curve.Evaluate(3)=300；raw=300×1×0.1=30。</summary>
        [Fact]
        public void GrantXp_Kill_NegativeDelta_AppliesNonOneXpFactor()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.n42_kill_02");
            host.RegisterUnit(unit, new Id("prog.curve.n42"), startLevel: 8);

            var granted = host.GrantXp(unit, new Id("prog.xp.kill_n42"), new XpContext(sourceLevel: 3));

            Assert.Equal(30, granted);
        }

        /// <summary>
        /// 2026-09-16 深度复审 D-S3：<c>prog.xp.no_kind_n42</c> 不登记 <c>kind</c> 字段——
        /// <c>ComputeCurveBasedRawAmount</c> 的判断记录明确"kind 缺省按 kill 处理"，但此前没有任何
        /// 用例真正锁定这一行为，只是恰好数值上与其它分支撞在一起（因为多数测试都没配置
        /// <see cref="ProgressionOptions.ExtraXpMultiplierProvider"/>，kill 分支与 quest/discovery
        /// 分支在那种配置下算出同一个数）。本用例故意配置一个非 1 的
        /// <c>ExtraXpMultiplierProvider</c>——只有 <c>kill</c>（包含 kind 缺省兜底）分支会调用它，
        /// <c>quest</c>/<c>discovery</c> 分支完全不读这个委托：baseAmount=Evaluate(5)=500，
        /// 若真的走了 kill 分支：500×3.0(倍率)×1.0(无 level_diff_ref)=1500；若误落到 quest/discovery
        /// 分支：500×1.0(Equivalent 缺省)×1.0=500——断言 1500 而不是 500，才能真正证明"kind 缺省"
        /// 执行的是 kill 分支代码路径，不只是数值上凑巧相同。
        /// </summary>
        [Fact]
        public void GrantXp_NoKind_FallsBackToKillBranch_UsesExtraXpMultiplierProvider()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var options = new ProgressionOptions { ExtraXpMultiplierProvider = (unitId, sourceId, tierId) => 3.0 };
            var host = MakeHostWithOptions(registry, bus, writers, options);
            var unit = new Id("unit.n4_s3_no_kind_01");
            host.RegisterUnit(unit, new Id("prog.curve.n42"), startLevel: 5);

            var granted = host.GrantXp(unit, new Id("prog.xp.no_kind_n42"), new XpContext(sourceLevel: 5));

            Assert.Equal(1500, granted);
        }

        // -----------------------------------------------------------------
        // T-N4-4：ExtraXpMultiplierProvider（分档倍率 × 难度倍率注入钩子，签名扩展为
        // (unitId, sourceId, tierId) → 倍率）
        // -----------------------------------------------------------------

        /// <summary>倍率组 1：Δ=0（xp_factor=1.0）、ExtraXpMultiplierProvider 恒返回 3.0（即
        /// GameplayAssembly 装配出的"分档 1.5 × 难度 2"乘积）→ baseAmount=Evaluate(5)=500，
        /// granted=500×3.0×1.0=1500。</summary>
        [Fact]
        public void GrantXp_Kill_WithExtraXpMultiplierProvider_AppliesMultiplier()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var options = new ProgressionOptions { ExtraXpMultiplierProvider = (unitId, sourceId, tierId) => 3.0 };
            var host = MakeHostWithOptions(registry, bus, writers, options);
            var unit = new Id("unit.n44_kill_multiplier_01");
            host.RegisterUnit(unit, new Id("prog.curve.n42"), startLevel: 5);

            var granted = host.GrantXp(unit, new Id("prog.xp.kill_n42"), new XpContext(sourceLevel: 5));

            Assert.Equal(1500, granted);
        }

        /// <summary>倍率组 2：未设置 <see cref="ProgressionOptions.ExtraXpMultiplierProvider"/>
        /// （<c>null</c>，缺省）→ 等价于恒为 1，行为与 T-N4-2 之前完全一致（回归锁死）——
        /// baseAmount=Evaluate(6)=600，Δ=+5 → xp_factor=1.5，granted=600×1×1.5=900（同
        /// <see cref="GrantXp_Kill_PositiveDelta_AppliesNonOneXpFactor"/>，用带 Options 的构造
        /// 重载重新核对一遍，确认新增参数不改变缺省路径）。</summary>
        [Fact]
        public void GrantXp_Kill_WithoutExtraXpMultiplierProvider_DefaultsToOne()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHostWithOptions(registry, bus, writers, new ProgressionOptions());
            var unit = new Id("unit.n44_kill_multiplier_02");
            host.RegisterUnit(unit, new Id("prog.curve.n42"), startLevel: 1);

            var granted = host.GrantXp(unit, new Id("prog.xp.kill_n42"), new XpContext(sourceLevel: 6));

            Assert.Equal(900, granted);
        }

        /// <summary>委托签名扩展核对（T-N4-4）：<see cref="XpContext.TierId"/> 原样转发给
        /// <see cref="ProgressionOptions.ExtraXpMultiplierProvider"/> 的第三个参数——用一个会记录
        /// 收到的参数的委托核对接线正确，不是碰巧返回 1。</summary>
        [Fact]
        public void GrantXp_Kill_ExtraXpMultiplierProvider_ReceivesUnitSourceAndTierIdFromContext()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            (Id UnitId, Id SourceId, Id? TierId)? received = null;
            var options = new ProgressionOptions
            {
                ExtraXpMultiplierProvider = (unitId, sourceId, tierId) =>
                {
                    received = (unitId, sourceId, tierId);
                    return 1.0;
                },
            };
            var host = MakeHostWithOptions(registry, bus, writers, options);
            var unit = new Id("unit.n44_kill_multiplier_03");
            var sourceId = new Id("prog.xp.kill_n42");
            var tierId = new Id("creature.tier.n44_probe");
            host.RegisterUnit(unit, new Id("prog.curve.n42"), startLevel: 1);

            host.GrantXp(unit, sourceId, new XpContext(sourceLevel: 1, tierId: tierId));

            Assert.NotNull(received);
            Assert.Equal(unit, received!.Value.UnitId);
            Assert.Equal(sourceId, received.Value.SourceId);
            Assert.Equal(tierId, received.Value.TierId);
        }

        // -----------------------------------------------------------------
        // T-N4-4 附带任务（设计层裁定）：HasXpSource 显式查询，取代监听器的
        // try/catch(ArgumentException)
        // -----------------------------------------------------------------

        [Fact]
        public void HasXpSource_RegisteredSource_ReturnsTrue()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var host = MakeHost(registry, bus, new RecordingWriters());

            Assert.True(host.HasXpSource(new Id("prog.xp.kill_n42")));
        }

        [Fact]
        public void HasXpSource_UnregisteredSource_ReturnsFalse()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var host = MakeHost(registry, bus, new RecordingWriters());

            Assert.False(host.HasXpSource(new Id("prog.xp_source.n44_nonexistent")));
        }

        /// <summary>任务 · 用例 1：Δ=0（任务等级与领取者同级）→ xp_factor=1.0。
        /// baseAmount=xp_base_curve.Evaluate(5)=500；当量 3.5；raw=3.5×500×1=1750。</summary>
        [Fact]
        public void GrantXp_Quest_ZeroDelta_EquivalentTimesBaseAmount()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.n42_quest_01");
            host.RegisterUnit(unit, new Id("prog.curve.n42"), startLevel: 5);

            var granted = host.GrantXp(unit, new Id("prog.xp.quest_n42"),
                new XpContext(sourceLevel: 5, equivalent: 3.5));

            Assert.Equal(1750, granted);
        }

        /// <summary>任务 · 用例 2：Δ=+5 → xp_factor=1.5，当量=2。
        /// baseAmount=xp_base_curve.Evaluate(6)=600；raw=2×600×1.5=1800。</summary>
        [Fact]
        public void GrantXp_Quest_PositiveDelta_EquivalentTimesBaseAmountTimesXpFactor()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.n42_quest_02");
            host.RegisterUnit(unit, new Id("prog.curve.n42"), startLevel: 1);

            var granted = host.GrantXp(unit, new Id("prog.xp.quest_n42"),
                new XpContext(sourceLevel: 6, equivalent: 2));

            Assert.Equal(1800, granted);
        }

        /// <summary>探索 · 用例 1：无 <c>level_diff_ref</c>，当量省略即按 1 处理。
        /// baseAmount=xp_base_curve.Evaluate(9)=900；raw=1×900=900。</summary>
        [Fact]
        public void GrantXp_Discovery_DefaultEquivalent_EqualsBaseAmount()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.n42_discovery_01");
            host.RegisterUnit(unit, new Id("prog.curve.n42"), startLevel: 3);

            var granted = host.GrantXp(unit, new Id("prog.xp.discovery_n42"), new XpContext(sourceLevel: 9));

            Assert.Equal(900, granted);
        }

        /// <summary>探索 · 用例 2：来源同时登记了 <c>level_diff_ref</c>（Δ=+5 本会得到 xp_factor=1.5），
        /// 但 ADR-0033 决策 3 的探索公式没有等级差项——即便登记了也不生效，raw 仍是
        /// 1×baseAmount=1×600=600（不是 600×1.5=900），证明 <c>discovery</c> 分支不查 Δ 表。</summary>
        [Fact]
        public void GrantXp_Discovery_IgnoresLevelDiffRef_EvenWhenConfigured()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.n42_discovery_02");
            host.RegisterUnit(unit, new Id("prog.curve.n42"), startLevel: 1);

            var granted = host.GrantXp(unit, new Id("prog.xp.discovery_ignorediff_n42"), new XpContext(sourceLevel: 6));

            Assert.Equal(600, granted);
        }

        // -----------------------------------------------------------------
        // 11. T-N4-2：满级归零（ADR-0033 决策 9）
        // -----------------------------------------------------------------

        /// <summary><see cref="ProgressionOptions.MaxLevel"/> 收紧到 10（曲线自身 max_level=20），
        /// 有效满级=min(10,20)=10；level10 条目本身的 xp_to_next=100000（非零，见
        /// <see cref="BuildFlatCurveRows"/>，只有曲线自身 20 级才是 0）——<see
        /// cref="IProgressionHost.GetXpToNext"/> 仍必须返回 0，证明满级判定看的是"有效满级"而不是
        /// "曲线数据在这一级恰好写了 0"。</summary>
        [Fact]
        public void GetXpToNext_EffectiveMaxLevelFromOptionsCap_ReturnsZero_EvenWhenCurveEntryNonZero()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var options = new ProgressionOptions { MaxLevel = 10 };
            var host = new ProgressionHost(registry, bus, writers.Write, writers.Remove, options);
            var unit = new Id("unit.n42_cap_01");
            host.RegisterUnit(unit, new Id("prog.curve.n42"), startLevel: 10);

            Assert.Equal(0, host.GetXpToNext(unit));
        }

        /// <summary>单位在曲线自身满级（20 级，<see cref="ProgressionOptions"/> 使用缺省值 0 即
        /// "不设全局上限"）时调用 <see cref="IProgressionHost.GrantXp"/>：返回 0，且不发
        /// <see cref="XpGainedEvent"/>（ADR-0033 决策 9"满级后……不发 progression.xp_gained"）。</summary>
        [Fact]
        public void GrantXp_AtMaxLevel_ReturnsZero_NoXpGainedEvent()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers); // 旧构造函数，ProgressionOptions 缺省（MaxLevel=0）
            var unit = new Id("unit.n42_maxed_01");
            host.RegisterUnit(unit, new Id("prog.curve.n42"), startLevel: 20); // 曲线自身满级

            XpGainedEvent? xpGained = null;
            bus.Subscribe<XpGainedEvent>(ProgressionEventKeys.XpGained, e => xpGained = e);

            var granted = host.GrantXp(unit, new Id("prog.xp.kill_n42"), new XpContext(sourceLevel: 6));

            Assert.Equal(0, granted);
            Assert.Null(xpGained);
            Assert.Equal(20, host.GetLevel(unit));
        }

        // -----------------------------------------------------------------
        // 12. T-N4-2：GrantFromSource 旧字段兼容——base_curve_ref 存在时以曲线为准
        // -----------------------------------------------------------------

        /// <summary><c>prog.xp.legacy_with_curve_n42</c> 同时保留 <c>base_xp=999</c>/<c>weight=2</c>
        /// 与新字段 <c>base_curve_ref</c>：拍板 4"<c>base_curve_ref</c> 存在时优先"——即便调用方仍在
        /// 用旧入口 <see cref="IProgressionHost.GrantFromSource"/>，也必须走曲线折算，不能落回
        /// <c>base_xp×weight×multiplier</c>（那样会得到 999×2×2=3996，与断言的期望值明显不同）。
        /// 该来源未登记 <c>kind</c>，兜底按 <c>kill</c> 处理（见 <c>ComputeCurveBasedRawAmount</c>
        /// 判断记录）；未登记 <c>level_diff_ref</c>，Δ 系数恒 1；<see cref="IProgressionHost.GrantFromSource"/>
        /// 的"契约疑点上报"判断记录：旧签名没有来源等级参数，按"领取者当前等级"隐式作为
        /// <c>sourceLevel</c>（此处等于 5），故 baseAmount=xp_base_curve.Evaluate(5)=500，
        /// <c>multiplier</c>=2 直接相乘：raw=500×2=1000。</summary>
        [Fact]
        public void GrantFromSource_SourceHasBothLegacyFieldsAndBaseCurveRef_UsesCurveNotLegacyFormula()
        {
            var registry = MakeGrantXpRegistry(out var bus);
            Assert.False(registry.LoadAll().IsBlocking);

            var writers = new RecordingWriters();
            var host = MakeHost(registry, bus, writers);
            var unit = new Id("unit.n42_legacy_01");
            host.RegisterUnit(unit, new Id("prog.curve.n42"), startLevel: 5);

            XpGainedEvent? xpGained = null;
            bus.Subscribe<XpGainedEvent>(ProgressionEventKeys.XpGained, e => xpGained = e);

            host.GrantFromSource(unit, new Id("prog.xp.legacy_with_curve_n42"), multiplier: 2);

            Assert.NotNull(xpGained);
            Assert.Equal(1000, xpGained!.Amount); // 不是旧算法的 999×2×2=3996
        }
    }
}
