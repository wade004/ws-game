using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Numbers.StatBlock
{
    public class StatHostTests
    {
        // -----------------------------------------------------------------
        // 公共夹具
        // -----------------------------------------------------------------

        // T-N1-1 判断记录：stat.test_d 的 group 从 primary 改为 secondary——1→2 迁移把
        // group=secondary + is_rating=true 映射为 category=percent（与既有样例 crit_rating/
        // dodge_rating 同一惯例），若仍是 primary 会映射成 category=primary，与同时迁移出的
        // conversion_ref 冲突，触发 StatDefinitionValidationRule.CheckConversionRefRequiresPercentCategory
        // （新增于 T-N1-1）。group 本身在本版本行为上未被 StatHost 区分 primary/secondary（唯一被
        // 特殊处理的是 resistance），改动不影响本文件任何既有断言。
        // T-N1-3 新增 stat.test_e/f/g：三种换算曲线形态的测试夹具——
        // stat.test_e：category=percent 但不带 conversion_ref（"恒等"形态，无 clamp）；
        // stat.test_f：category=percent 带 conversion_ref 指向 RatingConversionJson 的
        //   stat.rating.test_saturation（"变态版：饱和曲线"形态）；
        // stat.test_g：category=percent 但不带 conversion_ref，同时带 min/max（"保守版：恒等加
        //   clamp 硬上限"形态——clamp 由 ComputeFinal 在换算之后的聚合末尾夹取，不是换算曲线本身）。
        private const string StatDefinitionJson = @"
        {
            ""table"": ""stat.definition"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""stat.test_a"", ""name_key"": ""l10n.stat.test_a.name"", ""group"": ""primary"", ""default_base"": 0 },
                { ""id"": ""stat.test_b"", ""name_key"": ""l10n.stat.test_b.name"", ""group"": ""primary"", ""default_base"": 0 },
                { ""id"": ""stat.test_c"", ""name_key"": ""l10n.stat.test_c.name"", ""group"": ""primary"", ""default_base"": 0, ""min"": 0, ""max"": 100 },
                { ""id"": ""stat.test_d"", ""name_key"": ""l10n.stat.test_d.name"", ""group"": ""secondary"", ""default_base"": 0, ""is_rating"": true, ""rating_conversion_ref"": ""stat.rating.test_curve"" },
                { ""id"": ""stat.test_r"", ""name_key"": ""l10n.stat.test_r.name"", ""group"": ""resistance"", ""default_base"": 0 },
                { ""id"": ""stat.test_e"", ""name_key"": ""l10n.stat.test_e.name"", ""group"": ""secondary"", ""default_base"": 0, ""is_rating"": true },
                { ""id"": ""stat.test_f"", ""name_key"": ""l10n.stat.test_f.name"", ""group"": ""secondary"", ""default_base"": 0, ""is_rating"": true, ""rating_conversion_ref"": ""stat.rating.test_saturation"" },
                { ""id"": ""stat.test_g"", ""name_key"": ""l10n.stat.test_g.name"", ""group"": ""secondary"", ""default_base"": 0, ""is_rating"": true, ""min"": 0, ""max"": 100 }
            ]
        }";

        // 判断记录（2026-09-05，设计层裁定）：entries[].level 就是单位等级（取代此前"level 是
        // 评级原始值自己的插值断点"的判断），见 StatHost.ConvertRating 源码注释。测试曲线的两个
        // 断点固定在 level 1 与 level 10。
        // T-N1-3 新增 stat.rating.test_saturation：二元饱和形态 {k: 10, cap: 0.5}——k/cap 取值
        // 保证下面两组用例分别落在"未触顶"（raw=5, level=1 -> 5/15=1/3 < 0.5）与"触顶"
        // （raw=90, level=1 -> 90/100=0.9 > 0.5，夹到 0.5）两侧，同时覆盖公式与 cap 封顶两条路径。
        private const string RatingConversionJson = @"
        {
            ""table"": ""stat.rating_conversion"",
            ""schema_version"": 1,
            ""rows"": [
                {
                    ""id"": ""stat.rating.test_curve"",
                    ""entries"": [
                        { ""level"": 1, ""points_per_percent"": 10 },
                        { ""level"": 10, ""points_per_percent"": 5 }
                    ]
                },
                {
                    ""id"": ""stat.rating.test_saturation"",
                    ""saturation"": { ""k"": 10, ""cap"": 0.5 }
                }
            ]
        }";

        private static readonly Id StatA = new Id("stat.test_a");
        private static readonly Id StatB = new Id("stat.test_b");
        private static readonly Id StatC = new Id("stat.test_c");
        private static readonly Id StatD = new Id("stat.test_d");
        private static readonly Id StatR = new Id("stat.test_r");
        private static readonly Id StatE = new Id("stat.test_e");
        private static readonly Id StatF = new Id("stat.test_f");
        private static readonly Id StatG = new Id("stat.test_g");

        private static IEventBus MakeBus(List<StatChangedEvent> captured)
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
                new EventDefinition(StatBlockEventKeys.StatChanged, "stat",
                    new[] { "unitId", "stat", "oldValue", "newValue" }),
            });
            var bus = new EventBus(catalog);
            bus.Subscribe<StatChangedEvent>(StatBlockEventKeys.StatChanged, e => captured.Add(e));
            return bus;
        }

        private static (IDataRegistry Registry, ValidationReport Report) BuildRegistry(
            IEventBus bus,
            string statDefinitionJson,
            string? ratingConversionJson,
            IValidationRule? extraRule = null)
        {
            var source = new InMemoryDataSource().Add("stat.definition", statDefinitionJson);
            if (ratingConversionJson != null)
            {
                source.Add("stat.rating_conversion", ratingConversionJson);
            }

            var registry = new DataRegistry(source, bus);
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(StatSchemas.RatingConversion);
            if (extraRule != null)
            {
                registry.RegisterValidationRule(extraRule);
            }

            var report = registry.LoadAll();
            return (registry, report);
        }

        // T-N1-3：不再接受 enableRatingConversion 参数——换算层始终启用，
        // StatHostOptions.EnableRatingConversion 已废弃且 StatHost 不再读取它（见该属性判断记录）。
        private static (StatHost Host, List<StatChangedEvent> Captured, IEventBus Bus, ValidationReport Report) BuildHost(
            bool enableResistanceGroup = true,
            LevelLookup? levelLookup = null)
        {
            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (registry, report) = BuildRegistry(bus, StatDefinitionJson, RatingConversionJson, new StatDefinitionValidationRule());
            Assert.False(report.IsBlocking);

            var host = new StatHost(registry, bus, new StatHostOptions
            {
                EnableResistanceGroup = enableResistanceGroup,
                LevelLookup = levelLookup,
            });
            return (host, captured, bus, report);
        }

        // -----------------------------------------------------------------
        // 1~5. 三段式聚合手算用例（验收要求 >= 5 组）
        // -----------------------------------------------------------------

        [Fact]
        public void Aggregate_BaseOnly()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t1");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0);

            Assert.Equal(10.0, host.GetStat(unit, StatA), 10);
        }

        [Fact]
        public void Aggregate_BasePlusFlat()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t2");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0);
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 5.0, new Id("src.gear1")));

            // (10 + 5) = 15
            Assert.Equal(15.0, host.GetStat(unit, StatA), 10);
        }

        [Fact]
        public void Aggregate_BasePlusFlatPlusPct()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t3");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0);
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 5.0, new Id("src.gear1")));
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Pct, 0.20, new Id("src.buff1")));

            // (10 + 5) * (1 + 0.20) = 18
            Assert.Equal(18.0, host.GetStat(unit, StatA), 10);
        }

        [Fact]
        public void Aggregate_TwoDifferentMultGroupsMultiplyAcrossGroups()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t4");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0);
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 5.0, new Id("src.gear1")));
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Pct, 0.20, new Id("src.buff1")));
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Mult, 0.10, new Id("src.aura1"), "group_a"));
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Mult, 0.05, new Id("src.aura2"), "group_b"));

            // ((10 + 5) * 1.20) * 1.10 * 1.05 = 18 * 1.10 * 1.05 = 20.79
            Assert.Equal(20.79, host.GetStat(unit, StatA), 8);
        }

        [Fact]
        public void Aggregate_SameMultGroupAddsInsteadOfMultiplying()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t5");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0);
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Mult, 0.10, new Id("src.aura1"), "same_group"));
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Mult, 0.20, new Id("src.aura2"), "same_group"));

            // 组内相加: 10 * (1 + 0.10 + 0.20) = 13, 而不是 10 * 1.10 * 1.20 = 13.2
            Assert.Equal(13.0, host.GetStat(unit, StatA), 10);
            Assert.NotEqual(13.2, host.GetStat(unit, StatA), 5);
        }

        // -----------------------------------------------------------------
        // 6. RemoveModifiersBySource：跨多个属性、一次性回退
        // -----------------------------------------------------------------

        [Fact]
        public void RemoveModifiersBySource_RevertsAllAffectedStats()
        {
            var (host, captured, bus, _) = BuildHost();
            var unit = new Id("unit.t6");
            var source = new Id("src.full_gear_piece");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0);
            host.SetBase(unit, StatB, 20.0);
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 5.0, source));
            host.AddModifier(unit, new StatModifier(StatB, StatModifierOp.Flat, 3.0, source));
            bus.DispatchPending();

            Assert.Equal(15.0, host.GetStat(unit, StatA), 10);
            Assert.Equal(23.0, host.GetStat(unit, StatB), 10);

            captured.Clear();
            host.RemoveModifiersBySource(unit, source);
            bus.DispatchPending();

            Assert.Equal(10.0, host.GetStat(unit, StatA), 10);
            Assert.Equal(20.0, host.GetStat(unit, StatB), 10);
            Assert.Equal(2, captured.Count);
            Assert.Contains(captured, e => e.Stat == StatA && e.OldValue == 15.0 && e.NewValue == 10.0);
            Assert.Contains(captured, e => e.Stat == StatB && e.OldValue == 23.0 && e.NewValue == 20.0);
        }

        // -----------------------------------------------------------------
        // 7. min/max 夹取
        // -----------------------------------------------------------------

        [Fact]
        public void Clamp_MinAndMax()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t7");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatC, 150.0);
            Assert.Equal(100.0, host.GetStat(unit, StatC), 10);

            host.SetBase(unit, StatC, -50.0);
            Assert.Equal(0.0, host.GetStat(unit, StatC), 10);
        }

        // -----------------------------------------------------------------
        // 8~12. 评级换算——"标准版：等级索引除数"形态（2026-09-05 设计层裁定：entries[].level 是
        // 单位等级，不是评级原始值）。T-N1-3 起换算层始终启用，不再需要 enableRatingConversion 参数。
        // -----------------------------------------------------------------

        [Fact]
        public void RatingConversion_SameRawValueDiffersByUnitLevel()
        {
            var levelByUnit = new Dictionary<Id, int>();
            LevelLookup lookup = unitId => levelByUnit.TryGetValue(unitId, out var lvl) ? lvl : 1;

            var (host, _, _, _) = BuildHost(levelLookup: lookup);
            var unitLevel1 = new Id("unit.rating_lvl1");
            var unitLevel10 = new Id("unit.rating_lvl10");
            host.RegisterUnit(unitLevel1);
            host.RegisterUnit(unitLevel10);
            levelByUnit[unitLevel1] = 1;
            levelByUnit[unitLevel10] = 10;

            host.SetBase(unitLevel1, StatD, 50.0);
            host.SetBase(unitLevel10, StatD, 50.0);

            // 同一评级原始值 50：1 级 ppp=10 -> percent=5；10 级 ppp=5 -> percent=10。
            Assert.Equal(5.0, host.GetStat(unitLevel1, StatD), 10);
            Assert.Equal(10.0, host.GetStat(unitLevel10, StatD), 10);
        }

        // -----------------------------------------------------------------
        // RC-06（见外部审计 architecture/落地计划/audit-b3b91ee-20260907/code-review.md RC-06）：
        // GetStat 的缓存只在 SetBase/AddModifier/RemoveModifiersBySource 写入时更新，等级变化
        // （LevelLookup 结果改变）不经过这三个入口——RecomputeRatingStats 是补上的显式重算入口，
        // 供 RulesAssembly 订阅 progression.level_up 后调用（见该组装根判断记录）。
        // -----------------------------------------------------------------

        [Fact]
        public void RecomputeRatingStats_AfterLevelLookupChanges_UpdatesCacheAndFiresStatChanged()
        {
            var level = 1;
            LevelLookup lookup = _ => level;

            var (host, captured, bus, _) = BuildHost(levelLookup: lookup);
            var unit = new Id("unit.rating_recompute");
            host.RegisterUnit(unit);
            host.SetBase(unit, StatD, 50.0);
            bus.DispatchPending();

            // 1 级：ppp=10 -> percent=5。
            Assert.Equal(5.0, host.GetStat(unit, StatD), 10);

            level = 10; // LevelLookup 结果已经变了，但不经过 SetBase/AddModifier/RemoveModifiersBySource。
            captured.Clear();

            // 修复前唯一入口 GetStat 只读缓存，不会重新调用 LevelLookup——本断言证明"缓存本身确实
            // 不会自动感知等级变化"，这是既有设计（缓存的意义就在于不每次都重算），不是缺陷本身；
            // 缺陷是"没有任何显式入口能在等级变化后主动刷新它"。
            Assert.Equal(5.0, host.GetStat(unit, StatD), 10);
            Assert.Empty(captured);

            host.RecomputeRatingStats(unit);
            bus.DispatchPending();

            // 10 级：ppp=5 -> percent=10——RecomputeRatingStats 之后 GetStat 才反映新等级，且期间
            // 应该正确广播一次 stat.changed（供依赖它的下游，如 RC-06 另一半 PowerHost.RecomputeMax
            // 联动，见 core/rules/tests/Integration/PowerMaxRecomputeWiringTests.cs）。
            Assert.Equal(10.0, host.GetStat(unit, StatD), 10);
            Assert.Single(captured);
            Assert.Equal(unit, captured[0].UnitId);
            Assert.Equal(StatD, captured[0].Stat);
            Assert.Equal(5.0, captured[0].OldValue, 10);
            Assert.Equal(10.0, captured[0].NewValue, 10);
        }

        [Fact]
        public void RecomputeRatingStats_UnregisteredUnit_IsIdempotentNoOp()
        {
            var (host, _, _, _) = BuildHost();

            var exception = Record.Exception(() => host.RecomputeRatingStats(new Id("unit.never_registered")));

            Assert.Null(exception);
        }

        [Fact]
        public void RatingConversion_MidLevelInterpolatesBetweenEntries()
        {
            LevelLookup lookup = _ => 5; // entries 断点 1..10 之间，t = (5-1)/(10-1) = 4/9

            var (host, _, _, _) = BuildHost(levelLookup: lookup);
            var unit = new Id("unit.rating_mid");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatD, 50.0);

            // ppp = 10 + 4/9*(5-10) = 70/9；percent = 50 / (70/9) = 45/7
            Assert.Equal(45.0 / 7.0, host.GetStat(unit, StatD), 10);
        }

        [Fact]
        public void RatingConversion_NoLevelLookup_DefaultsToLevelOne()
        {
            var (host, _, _, _) = BuildHost(); // 不传 levelLookup
            var unit = new Id("unit.rating_nolookup");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatD, 50.0);

            // 无委托按 1 级处理 -> ppp=10（entries[0]） -> percent = 5
            Assert.Equal(5.0, host.GetStat(unit, StatD), 10);
        }

        [Fact]
        public void RatingConversion_LevelOutOfRangeClampsToEndpoint()
        {
            LevelLookup lookupAboveMax = _ => 999; // 越界高端，取 entries[10].ppp=5
            LevelLookup lookupBelowMin = _ => -5;  // 越界低端，取 entries[1].ppp=10

            var (hostHigh, _, _, _) = BuildHost(levelLookup: lookupAboveMax);
            var unitHigh = new Id("unit.rating_high");
            hostHigh.RegisterUnit(unitHigh);
            hostHigh.SetBase(unitHigh, StatD, 50.0);
            Assert.Equal(10.0, hostHigh.GetStat(unitHigh, StatD), 10); // 50/5

            var (hostLow, _, _, _) = BuildHost(levelLookup: lookupBelowMin);
            var unitLow = new Id("unit.rating_low");
            hostLow.RegisterUnit(unitLow);
            hostLow.SetBase(unitLow, StatD, 50.0);
            Assert.Equal(5.0, hostLow.GetStat(unitLow, StatD), 10); // 50/10
        }

        // -----------------------------------------------------------------
        // T-N1-3：换算层三种曲线形态——恒等（无 conversion_ref）、按等级除数（entries，上面 8~12
        // 已覆盖）、饱和（saturation）；以及"不存在关闭路径"的行为/反射用例。
        // -----------------------------------------------------------------

        // 恒等形态 1/2：category=percent 但不带 conversion_ref——base+Σflat 原样即最终值，不经
        // 任何曲线（ADR-0030 决策 3"保守版：恒等加 clamp 硬上限"的"恒等"部分）。
        [Fact]
        public void RatingConversion_Identity_NoConversionRef_PassesRawValueThrough()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.identity_a");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatE, 50.0);

            Assert.Equal(50.0, host.GetStat(unit, StatE), 10);
        }

        // 恒等形态 2/2："保守版：恒等加 clamp 硬上限"完整形态——恒等曲线之后，clamp 仍在
        // ComputeFinal 聚合末尾夹取（拍板 2，与换算层本身无关，换算只是恒等直通）。
        [Fact]
        public void RatingConversion_Identity_WithClamp_HardCapsAfterIdentity()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.identity_b");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatG, 150.0);
            Assert.Equal(100.0, host.GetStat(unit, StatG), 10); // clamp.max = 100

            host.SetBase(unit, StatG, -50.0);
            Assert.Equal(0.0, host.GetStat(unit, StatG), 10); // clamp.min = 0
        }

        // 饱和形态 1/2：raw=5, level=1, k=10, cap=0.5 -> 5/(5+10*1)=1/3，未触顶，验证公式本身
        // （"变态版：饱和曲线，除数随等级增长"，公式同 combat.resist_curve 饱和分支）。
        [Fact]
        public void RatingConversion_Saturation_ComputesValueOverValuePlusKTimesLevel()
        {
            var (host, _, _, _) = BuildHost(levelLookup: _ => 1);
            var unit = new Id("unit.saturation_a");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatF, 5.0);

            Assert.Equal(5.0 / 15.0, host.GetStat(unit, StatF), 10);
        }

        // 饱和形态 2/2：raw=90, level=1, k=10, cap=0.5 -> 90/(90+10*1)=0.9，超过 cap，夹到 0.5，
        // 验证封顶分支；同一 raw 换到 level=3 -> 90/(90+30)=0.75，仍触顶——用不同等级证明"除数随
        // 等级增长"这一形态特征本身不会绕开 cap。
        [Fact]
        public void RatingConversion_Saturation_CapsOutputAtCap()
        {
            var (hostLvl1, _, _, _) = BuildHost(levelLookup: _ => 1);
            var unitLvl1 = new Id("unit.saturation_b1");
            hostLvl1.RegisterUnit(unitLvl1);
            hostLvl1.SetBase(unitLvl1, StatF, 90.0);
            Assert.Equal(0.5, hostLvl1.GetStat(unitLvl1, StatF), 10);

            var (hostLvl3, _, _, _) = BuildHost(levelLookup: _ => 3);
            var unitLvl3 = new Id("unit.saturation_b2");
            hostLvl3.RegisterUnit(unitLvl3);
            hostLvl3.SetBase(unitLvl3, StatF, 90.0);
            Assert.Equal(0.5, hostLvl3.GetStat(unitLvl3, StatF), 10);
        }

        // -----------------------------------------------------------------
        // "不存在关闭路径"（T-N1-3 设计层裁定，见 StatHostOptions.EnableRatingConversion 判断
        // 记录）：一条行为用例（显式构造 EnableRatingConversion=false 仍不影响换算）+ 一条反射用例
        // （StatHostOptions 上不存在任何未标 [Obsolete] 的、名字含 RatingConversion/Conversion 的
        // 布尔属性——防止未来有人加一个新开关重新引入关闭路径）。
        // -----------------------------------------------------------------

        [Fact]
        public void EnableRatingConversion_HasNoEffect_PercentStatsStillConvert()
        {
            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (registry, report) = BuildRegistry(bus, StatDefinitionJson, RatingConversionJson, new StatDefinitionValidationRule());
            Assert.False(report.IsBlocking);

#pragma warning disable CS0618 // 本用例故意构造已废弃的 EnableRatingConversion=false，证明它没有任何效果（唯一允许的读取点）
            var host = new StatHost(registry, bus, new StatHostOptions { EnableRatingConversion = false });
#pragma warning restore CS0618
            var unit = new Id("unit.no_disable_path");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatD, 50.0);

            // 即便显式把已废弃的 EnableRatingConversion 设为 false，StatD（category=percent，带
            // conversion_ref）仍按 1 级（未传 LevelLookup）经曲线换算得 percent=5（50/ppp[0]=50/10），
            // 不是直通原值 50——证明该属性确实"无任何效果"，不是"换算层默认值恰好等于其语义"的假象。
            Assert.Equal(5.0, host.GetStat(unit, StatD), 10);
        }

        [Fact]
        public void StatHostOptions_NoUnobsoleteConversionSwitch_Exists()
        {
            var offenders = new List<string>();
            foreach (var property in typeof(StatHostOptions).GetProperties())
            {
                if (property.PropertyType != typeof(bool))
                {
                    continue;
                }

                var nameHintsConversionSwitch =
                    property.Name.IndexOf("RatingConversion", StringComparison.Ordinal) >= 0 ||
                    property.Name.IndexOf("Conversion", StringComparison.Ordinal) >= 0;
                if (!nameHintsConversionSwitch)
                {
                    continue;
                }

                var isObsolete = property.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).Length > 0;
                if (!isObsolete)
                {
                    offenders.Add(property.Name);
                }
            }

            Assert.True(offenders.Count == 0,
                "StatHostOptions 上存在未标 [Obsolete] 的换算相关布尔属性（可能重新引入了关闭换算层的" +
                "路径），违反 ADR-0030 决策 3 换算层始终启用：" + string.Join(", ", offenders));
        }

        // -----------------------------------------------------------------
        // 11~12. stat.changed 事件字段与"只在变化时发出"
        // -----------------------------------------------------------------

        [Fact]
        public void StatChanged_FiresOnlyOnActualChange_WithCorrectFields()
        {
            var (host, captured, bus, _) = BuildHost();
            var unit = new Id("unit.t11");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0); // 0 -> 10
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 5.0, new Id("src.gear1"))); // 10 -> 15
            bus.DispatchPending();

            Assert.Equal(2, captured.Count);

            Assert.Equal(unit, captured[0].UnitId);
            Assert.Equal(StatA, captured[0].Stat);
            Assert.Equal(0.0, captured[0].OldValue, 10);
            Assert.Equal(10.0, captured[0].NewValue, 10);

            Assert.Equal(unit, captured[1].UnitId);
            Assert.Equal(StatA, captured[1].Stat);
            Assert.Equal(10.0, captured[1].OldValue, 10);
            Assert.Equal(15.0, captured[1].NewValue, 10);
        }

        [Fact]
        public void SetBase_SameValue_DoesNotFireEvent()
        {
            var (host, captured, bus, _) = BuildHost();
            var unit = new Id("unit.t12");
            host.RegisterUnit(unit);

            // default_base 已经是 0，设成 0 不应触发事件
            host.SetBase(unit, StatA, 0.0);
            bus.DispatchPending();
            Assert.Empty(captured);

            host.SetBase(unit, StatA, 10.0); // 0 -> 10，触发一次
            bus.DispatchPending();
            Assert.Single(captured);

            captured.Clear();
            host.SetBase(unit, StatA, 10.0); // 同值再设一次，不触发
            bus.DispatchPending();
            Assert.Empty(captured);
        }

        // -----------------------------------------------------------------
        // 13. 抗性维度关闭
        // -----------------------------------------------------------------

        [Fact]
        public void ResistanceGroup_DisabledReturnsZeroAndWarns()
        {
            var (host, _, _, _) = BuildHost(enableResistanceGroup: false);
            var unit = new Id("unit.t13");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatR, 30.0);

            Assert.Equal(0.0, host.GetStat(unit, StatR));
            Assert.NotEmpty(host.Warnings);
            Assert.Contains(host.Warnings, w => w.Contains("stat.test_r"));
        }

        [Fact]
        public void ResistanceGroup_EnabledAggregatesNormally()
        {
            var (host, _, _, _) = BuildHost(enableResistanceGroup: true);
            var unit = new Id("unit.t13b");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatR, 30.0);

            Assert.Equal(30.0, host.GetStat(unit, StatR), 10);
        }

        // T-N1-2：直接用 v2 原生 category=defense 数据验证判定条件确实已经从 group=="resistance"
        // 改为 category=="defense"（而不是恰好通过 1→2 迁移链间接测到）。
        [Fact]
        public void ResistanceGroup_DisabledReturnsZero_ForV2NativeDefenseCategory_NotDrivenByGroup()
        {
            const string v2Json = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.v2_defense"", ""name_key"": ""l10n.v2_defense"", ""category"": ""defense"", ""default_base"": 0 }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (registry, report) = BuildRegistry(bus, v2Json, null, new StatDefinitionValidationRule());
            Assert.False(report.IsBlocking);

            var hostDisabled = new StatHost(registry, bus, new StatHostOptions { EnableResistanceGroup = false });
            var unit = new Id("unit.v2_defense");
            hostDisabled.RegisterUnit(unit);

            hostDisabled.SetBase(unit, new Id("stat.v2_defense"), 30.0);

            Assert.Equal(0.0, hostDisabled.GetStat(unit, new Id("stat.v2_defense")));
            Assert.Contains(hostDisabled.Warnings, w => w.Contains("stat.v2_defense"));
        }

        // -----------------------------------------------------------------
        // 14~15. 未注册单位 / 未知属性异常
        // -----------------------------------------------------------------

        [Fact]
        public void UnregisteredUnit_Throws()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.never_registered");

            Assert.Throws<InvalidOperationException>(() => host.GetStat(unit, StatA));
            Assert.Throws<InvalidOperationException>(() => host.SetBase(unit, StatA, 1.0));
            Assert.Throws<InvalidOperationException>(() => host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 1.0, new Id("src.x"))));
            Assert.Throws<InvalidOperationException>(() => host.RemoveModifiersBySource(unit, new Id("src.x")));
        }

        [Fact]
        public void UnknownStat_Throws()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.t15");
            host.RegisterUnit(unit);

            var unknownStat = new Id("stat.does_not_exist");
            Assert.Throws<ArgumentException>(() => host.GetStat(unit, unknownStat));
            Assert.Throws<ArgumentException>(() => host.SetBase(unit, unknownStat, 1.0));
            Assert.Throws<ArgumentException>(() => host.AddModifier(unit, new StatModifier(unknownStat, StatModifierOp.Flat, 1.0, new Id("src.x"))));
        }

        // -----------------------------------------------------------------
        // 16~17. IValidationRule 正反
        // -----------------------------------------------------------------

        [Fact]
        public void ValidationRule_ValidData_NoIssuesFromCustomRule()
        {
            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, StatDefinitionJson, RatingConversionJson, new StatDefinitionValidationRule());

            Assert.False(report.IsBlocking);
            foreach (var issue in report.Issues)
            {
                Assert.NotEqual(StatDefinitionValidationRule.CheckMinMaxOrder, issue.Check);
                Assert.NotEqual(StatDefinitionValidationRule.CheckRatingRefRequiresIsRating, issue.Check);
            }
        }

        [Fact]
        public void ValidationRule_RatingRefWithoutIsRating_ReportsError()
        {
            const string badJson = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""stat.bad_rating"", ""name_key"": ""l10n.stat.bad_rating.name"", ""group"": ""primary"", ""rating_conversion_ref"": ""stat.rating.test_curve"" }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, badJson, RatingConversionJson, new StatDefinitionValidationRule());

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == StatDefinitionValidationRule.CheckRatingRefRequiresIsRating
                                                 && i.RecordKey == "stat.bad_rating");
        }

        [Fact]
        public void ValidationRule_MinGreaterThanMax_ReportsError()
        {
            const string badJson = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""stat.bad_minmax"", ""name_key"": ""l10n.stat.bad_minmax.name"", ""group"": ""primary"", ""min"": 10, ""max"": 5 }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, badJson, null, new StatDefinitionValidationRule());

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == StatDefinitionValidationRule.CheckMinMaxOrder
                                                 && i.RecordKey == "stat.bad_minmax");
        }

        // T-N1-2 补充：只填新字段 clamp{min,max}、不带废弃平级 min/max 的纯 v2 记录同样要受
        // CheckMinMaxOrder 约束（见 StatDefinitionValidationRule 判断记录）。
        [Fact]
        public void ValidationRule_ClampMinGreaterThanClampMax_ReportsError()
        {
            const string badJson = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.bad_clamp"", ""name_key"": ""l10n.stat.bad_clamp.name"", ""category"": ""primary"",
                      ""clamp"": { ""min"": 10, ""max"": 5 } }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, badJson, null, new StatDefinitionValidationRule());

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == StatDefinitionValidationRule.CheckMinMaxOrder
                                                 && i.RecordKey == "stat.bad_clamp");
        }

        // -----------------------------------------------------------------
        // T-N1-1 新增：derived_from 仅限 derived、conversion_ref 仅限 percent 正反例
        // -----------------------------------------------------------------

        [Fact]
        public void ValidationRule_DerivedFromWithNonDerivedCategory_ReportsError()
        {
            const string badJson = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.bad_derived_from"", ""name_key"": ""l10n.a"", ""category"": ""primary"", ""group"": ""primary"",
                      ""derived_from"": [ { ""stat"": ""stat.bad_derived_from"", ""coefficient"": 1.0 } ] }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, badJson, null, new StatDefinitionValidationRule());

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == StatDefinitionValidationRule.CheckDerivedFromRequiresDerivedCategory
                                                 && i.RecordKey == "stat.bad_derived_from");
        }

        [Fact]
        public void ValidationRule_DerivedFromWithDerivedCategory_NoError()
        {
            const string okJson = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.src"", ""name_key"": ""l10n.src"", ""category"": ""primary"", ""group"": ""primary"" },
                    { ""id"": ""stat.ok_derived"", ""name_key"": ""l10n.b"", ""category"": ""derived"", ""group"": ""derived"",
                      ""derived_from"": [ { ""stat"": ""stat.src"", ""coefficient"": 2.0 } ] }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, okJson, null, new StatDefinitionValidationRule());

            Assert.False(report.IsBlocking);
            foreach (var issue in report.Issues)
            {
                Assert.NotEqual(StatDefinitionValidationRule.CheckDerivedFromRequiresDerivedCategory, issue.Check);
            }
        }

        [Fact]
        public void ValidationRule_ConversionRefWithNonPercentCategory_ReportsError()
        {
            const string badJson = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.bad_conversion_ref"", ""name_key"": ""l10n.c"", ""category"": ""primary"", ""group"": ""primary"",
                      ""conversion_ref"": ""stat.rating.test_curve"" }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, badJson, RatingConversionJson, new StatDefinitionValidationRule());

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == StatDefinitionValidationRule.CheckConversionRefRequiresPercentCategory
                                                 && i.RecordKey == "stat.bad_conversion_ref");
        }

        [Fact]
        public void ValidationRule_ConversionRefWithPercentCategory_NoError()
        {
            const string okJson = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.ok_conversion_ref"", ""name_key"": ""l10n.d"", ""category"": ""percent"", ""group"": ""secondary"",
                      ""conversion_ref"": ""stat.rating.test_curve"" }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, okJson, RatingConversionJson, new StatDefinitionValidationRule());

            Assert.False(report.IsBlocking);
            foreach (var issue in report.Issues)
            {
                Assert.NotEqual(StatDefinitionValidationRule.CheckConversionRefRequiresPercentCategory, issue.Check);
            }
        }

        // -----------------------------------------------------------------
        // T-N1-2 新增：StatDefinitionDerivationCycleValidationRule 派生无环校验正反例
        // -----------------------------------------------------------------

        [Fact]
        public void ValidationRule_DerivationCycle_SelfLoop_ReportsError()
        {
            const string selfLoopJson = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.self_loop"", ""name_key"": ""l10n.e"", ""category"": ""derived"",
                      ""derived_from"": [ { ""stat"": ""stat.self_loop"", ""coefficient"": 1.0 } ] }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, selfLoopJson, null, new StatDefinitionDerivationCycleValidationRule());

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == StatDefinitionDerivationCycleValidationRule.CheckName
                                                 && i.RecordKey == "stat.self_loop");
        }

        [Fact]
        public void ValidationRule_DerivationCycle_TwoNodeCycle_ReportsError()
        {
            const string twoNodeCycleJson = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.cycle_a"", ""name_key"": ""l10n.f"", ""category"": ""derived"",
                      ""derived_from"": [ { ""stat"": ""stat.cycle_b"", ""coefficient"": 1.0 } ] },
                    { ""id"": ""stat.cycle_b"", ""name_key"": ""l10n.g"", ""category"": ""derived"",
                      ""derived_from"": [ { ""stat"": ""stat.cycle_a"", ""coefficient"": 1.0 } ] }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, twoNodeCycleJson, null, new StatDefinitionDerivationCycleValidationRule());

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == StatDefinitionDerivationCycleValidationRule.CheckName);
        }

        [Fact]
        public void ValidationRule_DerivationCycle_AcyclicChain_NoError()
        {
            const string acyclicChainJson = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.chain_src"", ""name_key"": ""l10n.h"", ""category"": ""primary"" },
                    { ""id"": ""stat.chain_mid"", ""name_key"": ""l10n.i"", ""category"": ""derived"",
                      ""derived_from"": [ { ""stat"": ""stat.chain_src"", ""coefficient"": 1.0 } ] },
                    { ""id"": ""stat.chain_end"", ""name_key"": ""l10n.j"", ""category"": ""derived"",
                      ""derived_from"": [ { ""stat"": ""stat.chain_mid"", ""coefficient"": 1.0 } ] }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (_, report) = BuildRegistry(bus, acyclicChainJson, null, new StatDefinitionDerivationCycleValidationRule());

            Assert.False(report.IsBlocking);
            foreach (var issue in report.Issues)
            {
                Assert.NotEqual(StatDefinitionDerivationCycleValidationRule.CheckName, issue.Check);
            }
        }

        // -----------------------------------------------------------------
        // 18. 浮点确定性：相同插入顺序两次运行结果逐位相等
        // -----------------------------------------------------------------

        [Fact]
        public void Determinism_SameInsertionOrder_ProducesBitExactResults()
        {
            var (host, _, _, _) = BuildHost();
            var unitX = new Id("unit.determinism_x");
            var unitY = new Id("unit.determinism_y");
            host.RegisterUnit(unitX);
            host.RegisterUnit(unitY);

            void ApplySequence(Id unit)
            {
                host.SetBase(unit, StatA, 10.0);
                host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 3.0, new Id("src.a")));
                host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 2.0, new Id("src.b")));
                host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Pct, 0.1, new Id("src.c")));
                host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Mult, 0.05, new Id("src.d"), "group1"));
                host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Mult, 0.02, new Id("src.e"), "group2"));
            }

            ApplySequence(unitX);
            ApplySequence(unitY);

            var valueX = host.GetStat(unitX, StatA);
            var valueY = host.GetStat(unitY, StatA);

            Assert.Equal(valueX, valueY); // 逐位相等（xunit 对 double 的默认 Equal 是精确比较）
        }

        // -----------------------------------------------------------------
        // W1 收边补齐（A3 审计 #16）：IsRegistered/UnregisterUnit/GetBase/GetModifiers 无直接测试
        // -----------------------------------------------------------------

        [Fact]
        public void IsRegistered_FalseBeforeRegister_TrueAfter_FalseAfterUnregister()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.registration");

            Assert.False(host.IsRegistered(unit));

            host.RegisterUnit(unit);
            Assert.True(host.IsRegistered(unit));

            host.UnregisterUnit(unit);
            Assert.False(host.IsRegistered(unit));
        }

        [Fact]
        public void UnregisterUnit_UnknownUnit_DoesNotThrow()
        {
            var (host, _, _, _) = BuildHost();

            var ex = Record.Exception(() => host.UnregisterUnit(new Id("unit.never_registered")));

            Assert.Null(ex);
        }

        [Fact]
        public void UnregisterUnit_SubsequentGetStat_ThrowsAsUnregistered()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.unregister_then_read");
            host.RegisterUnit(unit);
            host.SetBase(unit, StatA, 5.0);

            host.UnregisterUnit(unit);

            Assert.Throws<InvalidOperationException>(() => host.GetStat(unit, StatA));
        }

        [Fact]
        public void GetBase_ReturnsDefaultBase_WhenNeverSet()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.get_base_default");
            host.RegisterUnit(unit);

            Assert.Equal(0.0, host.GetBase(unit, StatA));
        }

        [Fact]
        public void GetBase_ReturnsSetValue_UnaffectedByModifiers()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.get_base_set");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatA, 10.0);
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 100.0, new Id("src.a")));

            Assert.Equal(10.0, host.GetBase(unit, StatA)); // GetBase 只看基础值，不含修正。
            Assert.Equal(110.0, host.GetStat(unit, StatA)); // 对照：GetStat 含修正。
        }

        [Fact]
        public void GetModifiers_EmptyBeforeAnyAdded_ThenReflectsAddedModifiers()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.get_modifiers");
            host.RegisterUnit(unit);

            Assert.Empty(host.GetModifiers(unit, StatA));

            var mod1 = new StatModifier(StatA, StatModifierOp.Flat, 3.0, new Id("src.a"));
            var mod2 = new StatModifier(StatA, StatModifierOp.Pct, 0.1, new Id("src.b"));
            host.AddModifier(unit, mod1);
            host.AddModifier(unit, mod2);

            var modifiers = host.GetModifiers(unit, StatA);
            Assert.Equal(2, modifiers.Count);
            Assert.Contains(modifiers, m => m.SourceId == mod1.SourceId && m.Op == StatModifierOp.Flat);
            Assert.Contains(modifiers, m => m.SourceId == mod2.SourceId && m.Op == StatModifierOp.Pct);

            // 其它属性的修正列表不受影响（GetModifiers 按 stat 独立索引）。
            Assert.Empty(host.GetModifiers(unit, StatB));
        }

        [Fact]
        public void GetModifiers_ReturnsSnapshot_NotLiveView()
        {
            var (host, _, _, _) = BuildHost();
            var unit = new Id("unit.get_modifiers_snapshot");
            host.RegisterUnit(unit);
            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 1.0, new Id("src.a")));

            var snapshot = host.GetModifiers(unit, StatA);
            Assert.Single(snapshot);

            host.AddModifier(unit, new StatModifier(StatA, StatModifierOp.Flat, 2.0, new Id("src.b")));

            // 快照本身（GetModifiers 内部 ToArray()）不随后续 AddModifier 变化。
            Assert.Single(snapshot);
            Assert.Equal(2, host.GetModifiers(unit, StatA).Count);
        }

        // -----------------------------------------------------------------
        // T-N1-2 新增：两轮拓扑序聚合、clamp 在第二轮后夹取、失效传播、拓扑序稳定、
        // 派生成环的加载期防御（ADR-0030 决策 2）
        // -----------------------------------------------------------------

        // 判断记录：本组测试用独立的 v2 原生 fixture（schema_version 2，直接写 category，不写已废弃的
        // group——T-N1-2 已把 group 放宽为 required:false，见 StatSchemas.GroupValues 判断记录），
        // 与上面沿用 v1→v2 迁移链的既有 fixture（StatDefinitionJson）分开，互不干扰。
        //
        // 图（用于两轮聚合/失效传播用例）：
        //   stat.src_a、stat.src_b：category=primary，独立主属性。
        //   stat.src_clamped：category=primary，clamp.max=20。
        //   stat.derived_x：category=derived，来源 [src_a×2.0, src_b×0.5]，无自身 clamp。
        //   stat.derived_neg：category=derived，来源 [src_a×(-1.0)]（负系数，拍板 11）。
        //   stat.derived_clamped_self：category=derived，来源 [src_a×1.0]，clamp.max=50。
        //   stat.derived_from_clamped_src：category=derived，来源 [src_clamped×2.0]，无自身 clamp——
        //     用于区分"来源被夹取后再派生"（本条）与"派生自身夹取"（derived_clamped_self）。
        private const string DerivedStatDefinitionJson = @"
        {
            ""table"": ""stat.definition"",
            ""schema_version"": 2,
            ""rows"": [
                { ""id"": ""stat.src_a"", ""name_key"": ""l10n.stat.src_a.name"", ""category"": ""primary"", ""default_base"": 0 },
                { ""id"": ""stat.src_b"", ""name_key"": ""l10n.stat.src_b.name"", ""category"": ""primary"", ""default_base"": 0 },
                { ""id"": ""stat.src_clamped"", ""name_key"": ""l10n.stat.src_clamped.name"", ""category"": ""primary"", ""default_base"": 0, ""clamp"": { ""max"": 20 } },
                { ""id"": ""stat.derived_x"", ""name_key"": ""l10n.stat.derived_x.name"", ""category"": ""derived"",
                  ""derived_from"": [ { ""stat"": ""stat.src_a"", ""coefficient"": 2.0 }, { ""stat"": ""stat.src_b"", ""coefficient"": 0.5 } ] },
                { ""id"": ""stat.derived_neg"", ""name_key"": ""l10n.stat.derived_neg.name"", ""category"": ""derived"",
                  ""derived_from"": [ { ""stat"": ""stat.src_a"", ""coefficient"": -1.0 } ] },
                { ""id"": ""stat.derived_clamped_self"", ""name_key"": ""l10n.stat.derived_clamped_self.name"", ""category"": ""derived"",
                  ""derived_from"": [ { ""stat"": ""stat.src_a"", ""coefficient"": 1.0 } ], ""clamp"": { ""max"": 50 } },
                { ""id"": ""stat.derived_from_clamped_src"", ""name_key"": ""l10n.stat.derived_from_clamped_src.name"", ""category"": ""derived"",
                  ""derived_from"": [ { ""stat"": ""stat.src_clamped"", ""coefficient"": 2.0 } ] }
            ]
        }";

        // 与上面完全相同的七行记录，只是数组顺序整体反过来——用于"拓扑序稳定：同一输入不同登记
        // 顺序结果一致"用例（禁止事项：拓扑序不得依赖字典/数组枚举顺序）。
        private const string DerivedStatDefinitionJsonReversedOrder = @"
        {
            ""table"": ""stat.definition"",
            ""schema_version"": 2,
            ""rows"": [
                { ""id"": ""stat.derived_from_clamped_src"", ""name_key"": ""l10n.stat.derived_from_clamped_src.name"", ""category"": ""derived"",
                  ""derived_from"": [ { ""stat"": ""stat.src_clamped"", ""coefficient"": 2.0 } ] },
                { ""id"": ""stat.derived_clamped_self"", ""name_key"": ""l10n.stat.derived_clamped_self.name"", ""category"": ""derived"",
                  ""derived_from"": [ { ""stat"": ""stat.src_a"", ""coefficient"": 1.0 } ], ""clamp"": { ""max"": 50 } },
                { ""id"": ""stat.derived_neg"", ""name_key"": ""l10n.stat.derived_neg.name"", ""category"": ""derived"",
                  ""derived_from"": [ { ""stat"": ""stat.src_a"", ""coefficient"": -1.0 } ] },
                { ""id"": ""stat.derived_x"", ""name_key"": ""l10n.stat.derived_x.name"", ""category"": ""derived"",
                  ""derived_from"": [ { ""stat"": ""stat.src_a"", ""coefficient"": 2.0 }, { ""stat"": ""stat.src_b"", ""coefficient"": 0.5 } ] },
                { ""id"": ""stat.src_clamped"", ""name_key"": ""l10n.stat.src_clamped.name"", ""category"": ""primary"", ""default_base"": 0, ""clamp"": { ""max"": 20 } },
                { ""id"": ""stat.src_b"", ""name_key"": ""l10n.stat.src_b.name"", ""category"": ""primary"", ""default_base"": 0 },
                { ""id"": ""stat.src_a"", ""name_key"": ""l10n.stat.src_a.name"", ""category"": ""primary"", ""default_base"": 0 }
            ]
        }";

        private static readonly Id StatSrcA = new Id("stat.src_a");
        private static readonly Id StatSrcB = new Id("stat.src_b");
        private static readonly Id StatSrcClamped = new Id("stat.src_clamped");
        private static readonly Id StatDerivedX = new Id("stat.derived_x");
        private static readonly Id StatDerivedNeg = new Id("stat.derived_neg");
        private static readonly Id StatDerivedClampedSelf = new Id("stat.derived_clamped_self");
        private static readonly Id StatDerivedFromClampedSrc = new Id("stat.derived_from_clamped_src");

        private static (StatHost Host, List<StatChangedEvent> Captured, IEventBus Bus) BuildDerivedHost(string definitionJson)
        {
            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            var (registry, report) = BuildRegistry(bus, definitionJson, null,
                new StatDefinitionValidationRule());
            Assert.False(report.IsBlocking);

            var host = new StatHost(registry, bus, new StatHostOptions());
            return (host, captured, bus);
        }

        // 1/6：主属性三段式在两轮聚合改动之后仍然不变（category=primary 走第一轮，行为与 T-N1-2
        // 之前完全一致）。
        [Fact]
        public void TwoRoundAggregation_PrimaryStatStillAggregatesThreeSegment()
        {
            var (host, _, _) = BuildDerivedHost(DerivedStatDefinitionJson);
            var unit = new Id("unit.two_round_primary");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatSrcA, 100.0);
            host.AddModifier(unit, new StatModifier(StatSrcA, StatModifierOp.Flat, 5.0, new Id("src.gear")));
            host.AddModifier(unit, new StatModifier(StatSrcA, StatModifierOp.Pct, 0.10, new Id("src.buff")));

            // (100 + 5) * 1.10 = 115.5
            Assert.Equal(115.5, host.GetStat(unit, StatSrcA), 10);
        }

        // 2/6：派生属性由两条来源各带系数——基础值 = Σ(来源最终值 × 系数)，再走自己的三段式。
        [Fact]
        public void TwoRoundAggregation_DerivedStat_SumsTwoWeightedSources()
        {
            var (host, _, _) = BuildDerivedHost(DerivedStatDefinitionJson);
            var unit = new Id("unit.two_round_derived");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatSrcA, 100.0);
            host.SetBase(unit, StatSrcB, 10.0);

            // derived_x 基础值 = 100*2.0 + 10*0.5 = 205，自身无修正，最终值即 205。
            Assert.Equal(205.0, host.GetStat(unit, StatDerivedX), 10);

            // 派生属性自己也能再叠加 flat/pct/mult（06 第 1.1 节修订段"装备直接加的攻击强度等落在
            // 第二轮的 flat 段"）。
            host.AddModifier(unit, new StatModifier(StatDerivedX, StatModifierOp.Flat, 15.0, new Id("src.gear")));
            Assert.Equal(220.0, host.GetStat(unit, StatDerivedX), 10);
        }

        // 3/6：派生系数允许为负（拍板 11）。
        [Fact]
        public void TwoRoundAggregation_DerivedStat_NegativeCoefficient()
        {
            var (host, _, _) = BuildDerivedHost(DerivedStatDefinitionJson);
            var unit = new Id("unit.two_round_negative");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatSrcA, 100.0);

            // derived_neg 基础值 = 100 * (-1.0) = -100。
            Assert.Equal(-100.0, host.GetStat(unit, StatDerivedNeg), 10);
        }

        // 4/6：clamp 在第二轮聚合之后夹取——"来源被夹取后再派生"：src_clamped 自己先夹到 20，
        // derived_from_clamped_src 拿到的是夹取之后的 20，而不是夹取之前的原始值 100。
        [Fact]
        public void TwoRoundAggregation_Clamp_SourceClampedBeforeFeedingDerived()
        {
            var (host, _, _) = BuildDerivedHost(DerivedStatDefinitionJson);
            var unit = new Id("unit.two_round_clamp_source");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatSrcClamped, 100.0);
            Assert.Equal(20.0, host.GetStat(unit, StatSrcClamped), 10); // 来源自己先被夹到 20

            // derived_from_clamped_src 基础值 = ResolveFinal(src_clamped) * 2.0 = 20 * 2 = 40——
            // 如果错误地使用夹取前的原始值 100，这里会算出 200，测试能区分两种实现。
            Assert.Equal(40.0, host.GetStat(unit, StatDerivedFromClampedSrc), 10);
        }

        // 5/6：clamp 在第二轮聚合之后夹取——"派生自身夹取"：derived_clamped_self 自己的最终值
        // 超过 clamp.max 时在自己这一轮聚合结束后被夹取，不影响来源 src_a 本身的值。
        [Fact]
        public void TwoRoundAggregation_Clamp_DerivedClampsItsOwnFinalValue()
        {
            var (host, _, _) = BuildDerivedHost(DerivedStatDefinitionJson);
            var unit = new Id("unit.two_round_clamp_derived");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatSrcA, 100.0);

            // derived_clamped_self 基础值 = 100 * 1.0 = 100，clamp.max=50 → 夹到 50。
            Assert.Equal(50.0, host.GetStat(unit, StatDerivedClampedSelf), 10);
            // 来源属性本身不受下游派生属性 clamp 的影响。
            Assert.Equal(100.0, host.GetStat(unit, StatSrcA), 10);
        }

        // 6/6：拓扑序稳定——同一输入、不同登记（数组）顺序，两个 StatHost 实例对同一操作序列算出
        // 逐位相等的结果（禁止事项：不得依赖字典枚举顺序）。
        [Fact]
        public void TwoRoundAggregation_TopoOrder_StableAcrossDifferentRegistrationOrder()
        {
            var (hostForward, _, _) = BuildDerivedHost(DerivedStatDefinitionJson);
            var (hostReversed, _, _) = BuildDerivedHost(DerivedStatDefinitionJsonReversedOrder);

            void ApplySequence(StatHost host, Id unit)
            {
                host.RegisterUnit(unit);
                host.SetBase(unit, StatSrcA, 100.0);
                host.SetBase(unit, StatSrcB, 10.0);
                host.SetBase(unit, StatSrcClamped, 100.0);
            }

            var unitForward = new Id("unit.topo_forward");
            var unitReversed = new Id("unit.topo_reversed");
            ApplySequence(hostForward, unitForward);
            ApplySequence(hostReversed, unitReversed);

            Assert.Equal(hostForward.GetStat(unitForward, StatDerivedX), hostReversed.GetStat(unitReversed, StatDerivedX));
            Assert.Equal(hostForward.GetStat(unitForward, StatDerivedNeg), hostReversed.GetStat(unitReversed, StatDerivedNeg));
            Assert.Equal(hostForward.GetStat(unitForward, StatDerivedClampedSelf), hostReversed.GetStat(unitReversed, StatDerivedClampedSelf));
            Assert.Equal(hostForward.GetStat(unitForward, StatDerivedFromClampedSrc), hostReversed.GetStat(unitReversed, StatDerivedFromClampedSrc));
        }

        // 1/2：来源属性的 flat 修正变化（AddModifier）后，已缓存的派生属性自动重算并广播
        // stat.changed（失效传播）。
        [Fact]
        public void InvalidationPropagation_SourceModifierChange_UpdatesCachedDerivedStat()
        {
            var (host, captured, bus) = BuildDerivedHost(DerivedStatDefinitionJson);
            var unit = new Id("unit.propagation_modifier");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatSrcA, 100.0);
            host.SetBase(unit, StatSrcB, 10.0);
            Assert.Equal(205.0, host.GetStat(unit, StatDerivedX), 10); // 先查询一次，让 derived_x 进缓存
            bus.DispatchPending();
            captured.Clear();

            host.AddModifier(unit, new StatModifier(StatSrcA, StatModifierOp.Flat, 50.0, new Id("src.gear")));
            bus.DispatchPending();

            // src_a: 100 -> 150；derived_x: 205 -> 150*2 + 10*0.5 = 305，不需要再调用一次 GetStat
            // 才刷新——PropagateDerivedInvalidation 在 AddModifier 内部已经把新值写回缓存。
            Assert.Equal(305.0, host.GetStat(unit, StatDerivedX), 10);
            Assert.Contains(captured, e => e.Stat == StatSrcA && e.OldValue == 100.0 && e.NewValue == 150.0);
            Assert.Contains(captured, e => e.Stat == StatDerivedX && e.OldValue == 205.0 && e.NewValue == 305.0);
        }

        // 2/2：RemoveModifiersBySource 撤销来源属性上的修正后，同样传播给已缓存的派生属性；
        // 未被缓存过的派生属性不主动补算（与 RecomputeAllCachedStatsAfterReload 同一判断记录口径），
        // 但下一次查询时自然读到最新状态。
        [Fact]
        public void InvalidationPropagation_RemoveModifiersBySource_UpdatesCachedDerivedStat_UncachedStaysLazy()
        {
            var (host, captured, bus) = BuildDerivedHost(DerivedStatDefinitionJson);
            var unit = new Id("unit.propagation_remove");
            var gearSource = new Id("src.full_gear_piece");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatSrcA, 100.0);
            host.AddModifier(unit, new StatModifier(StatSrcA, StatModifierOp.Flat, 50.0, gearSource));

            // 只查询 derived_x，不查询 derived_neg/derived_clamped_self——后两者此时都还没有缓存条目。
            Assert.Equal(300.0, host.GetStat(unit, StatDerivedX), 10); // (100+50)*2 + 0*0.5 = 300
            bus.DispatchPending();
            captured.Clear();

            host.RemoveModifiersBySource(unit, gearSource);
            bus.DispatchPending();

            // src_a: 150 -> 100；derived_x（已缓存）: 300 -> 200，自动传播。
            Assert.Equal(200.0, host.GetStat(unit, StatDerivedX), 10);
            Assert.Contains(captured, e => e.Stat == StatDerivedX && e.OldValue == 300.0 && e.NewValue == 200.0);

            // derived_neg（此前从未被查询过、没有缓存条目）：传播阶段按判断记录跳过，但现在第一次
            // 查询时用的已经是 RemoveModifiersBySource 之后的最新 src_a（100），结果正确，不存在
            // 过期风险——ComputeFinal 从不记忆旧输入。
            Assert.Equal(-100.0, host.GetStat(unit, StatDerivedNeg), 10);
        }

        // 派生成环：StatHost 加载期的防御性检查（正常数据流程应已被
        // StatDefinitionDerivationCycleValidationRule 在内容校验阶段拦下，这里模拟"校验规则被遗漏
        // 注册"场景，验证 BuildDerivationGraph 自己的兜底仍然生效）。
        [Fact]
        public void BuildDerivationGraph_CyclicDerivedFrom_ThrowsAsLoadTimeDefense()
        {
            const string cyclicJson = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.cycle_a"", ""name_key"": ""l10n.a"", ""category"": ""derived"",
                      ""derived_from"": [ { ""stat"": ""stat.cycle_b"", ""coefficient"": 1.0 } ] },
                    { ""id"": ""stat.cycle_b"", ""name_key"": ""l10n.b"", ""category"": ""derived"",
                      ""derived_from"": [ { ""stat"": ""stat.cycle_a"", ""coefficient"": 1.0 } ] }
                ]
            }";

            var captured = new List<StatChangedEvent>();
            var bus = MakeBus(captured);
            // 故意不注册 StatDefinitionDerivationCycleValidationRule，模拟内容校验被跳过的场景。
            var (registry, report) = BuildRegistry(bus, cyclicJson, null, extraRule: null);
            Assert.False(report.IsBlocking);

            var ex = Assert.Throws<InvalidOperationException>(() => new StatHost(registry, bus, new StatHostOptions()));
            Assert.Contains("环", ex.Message);
        }

        // 显式 SetBase 对派生属性优先生效（ResolveBaseValue 判断记录，待设计层确认）：写过之后不再
        // 理会 derived_from，与 DefaultBase 对主属性的既有回退规则同构。
        [Fact]
        public void ExplicitSetBase_OnDerivedStat_OverridesComputedDerivedBase()
        {
            var (host, _, _) = BuildDerivedHost(DerivedStatDefinitionJson);
            var unit = new Id("unit.explicit_override_derived");
            host.RegisterUnit(unit);

            host.SetBase(unit, StatSrcA, 100.0);
            host.SetBase(unit, StatSrcB, 10.0);
            Assert.Equal(205.0, host.GetStat(unit, StatDerivedX), 10); // 未显式覆盖时走 Σ(来源×系数)

            host.SetBase(unit, StatDerivedX, 999.0);
            Assert.Equal(999.0, host.GetStat(unit, StatDerivedX), 10); // 显式覆盖优先，derived_from 不再生效
        }
    }
}
