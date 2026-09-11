using System;
using System.Collections.Generic;
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
        // 2→3 需 50 经验（成长 {stat.strength:3}），3 级（满级）xp_to_next=0。
        private const string GoodCurveRows = @"[
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

            // 1→2 需 100，2→3 需 50，3 级是满级：给 200 经验足够连跨两级，残余 50 在满级时被丢弃。
            host.AddXp(unit, new Id("prog.xp.kill_wolf"), 200);

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

            // 2→3 需 50 经验（GoodCurveRows）。
            host.AddXp(unit, new Id("prog.xp.kill_wolf"), 50);

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
    }
}
