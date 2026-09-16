using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// T-N6-3a（ADR-0035 决策 2；数值总纲第 4.4 节）：<see cref="ExpectedStatCalculator"/> 在嵌入式最小
    /// 仿真数据集上的验收——<c>Compute(L)</c> 在 L=1/10/20 三级的数值与"手算"一致。手算按
    /// <c>core/sim/tests/data/README.md</c>"锚点推导"一节给出的职业成长闭式公式（<c>strength(L) = 12 +
    /// 3·(L-1)</c>/<c>stamina(L) = 150 + 50·(L-1)</c>/<c>attack_power(L) = strength(L)</c>）加上独立
    /// 调用 <see cref="IBudgetSolver.Solve"/>（与 <see cref="ExpectedStatCalculator"/> 内部使用同一份
    /// statMix 策略——<c>stat.weight</c> 全表按权重归一化，见该类型判断记录"Σ槽位 的槽位范围与
    /// statMix 来源"）算出的装备贡献相加，在测试里独立写一遍，不反射/调用 <see
    /// cref="ExpectedStatCalculator"/> 内部实现。
    /// </summary>
    public sealed class ExpectedStatCalculatorTests
    {
        private static readonly Id ClassId = SimTestWorldFactory.EmbeddedClassId;
        private static readonly Id QualityCommon = new Id("item.quality.sim_common");
        private static readonly Id BudgetCurveId = new Id("item.budget.default");
        private static readonly Id[] NonWeaponSlots =
        {
            new Id("item.slot.sim_head"),
            new Id("item.slot.sim_chest"),
            new Id("item.slot.sim_legs"),
            new Id("item.slot.sim_feet"),
        };
        private static readonly Id[] MixStats =
        {
            new Id("stat.strength"), new Id("stat.agility"), new Id("stat.intellect"), new Id("stat.stamina"),
            new Id("stat.attack_power"), new Id("stat.crit_rating"), new Id("stat.dodge_rating"),
            new Id("stat.hit_rating"), new Id("stat.armor"),
        };

        private static Dictionary<Id, double> HandComputeEquipmentFlat(Core.Sim.HeadlessWorld world, int level)
        {
            var itemLevel = (int)Math.Round(world.AnchorTable!.Get(level).ExpectedItemLevel);
            var solver = new BudgetSolver();
            var statMix = MixStats.Select(s => (Stat: s, Ratio: 1.0 / MixStats.Length)).ToList();

            var flat = new Dictionary<Id, double>();
            foreach (var slotId in NonWeaponSlots)
            {
                var solved = solver.Solve(itemLevel, QualityCommon, slotId, statMix, BudgetCurveId, world.Registry);
                foreach (var kv in solved.Values)
                {
                    flat.TryGetValue(kv.Key, out var existing);
                    flat[kv.Key] = existing + kv.Value;
                }
            }
            return flat;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(20)]
        public void Compute_MatchesHandCalculatedGrowthPlusEquipment(int level)
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 20260916300UL + (ulong)level, playerLevel: 1);
            var calculator = new ExpectedStatCalculator(
                world.Registry, world.AnchorTable!, ClassId, QualityCommon, new BudgetSolver(), BudgetCurveId);

            var computed = calculator.Compute(level);
            var flat = HandComputeEquipmentFlat(world, level);

            var handStrength = 12.0 + 3.0 * (level - 1) + flat.GetValueOrDefault(new Id("stat.strength"), 0.0);
            var handAgility = 6.0 + flat.GetValueOrDefault(new Id("stat.agility"), 0.0);
            var handIntellect = 4.0 + flat.GetValueOrDefault(new Id("stat.intellect"), 0.0);
            var handStamina = 150.0 + 50.0 * (level - 1) + flat.GetValueOrDefault(new Id("stat.stamina"), 0.0);
            // attack_power = strength 最终值（含其自身装备贡献，派生系数 1.0，见
            // arch.class.sim_warrior.derivation_overrides）× 1.0 + attack_power 自身在 statMix 里
            // 获得的装备贡献（派生属性的 flat 修正在其自身派生基础值之上直接叠加——同
            // Core.Numbers.StatBlock.StatHost.ComputeFinal "value = baseValue + flatSum" 的既有聚合
            // 顺序，ExpectedStatCalculator 按同一顺序实现）。
            var handAttackPower = handStrength + flat.GetValueOrDefault(new Id("stat.attack_power"), 0.0);

            Assert.True(Math.Abs(computed[new Id("stat.strength")] - handStrength) < 1e-6,
                $"L{level} strength: expected {handStrength}, got {computed[new Id("stat.strength")]}");
            Assert.True(Math.Abs(computed[new Id("stat.agility")] - handAgility) < 1e-6,
                $"L{level} agility: expected {handAgility}, got {computed[new Id("stat.agility")]}");
            Assert.True(Math.Abs(computed[new Id("stat.intellect")] - handIntellect) < 1e-6,
                $"L{level} intellect: expected {handIntellect}, got {computed[new Id("stat.intellect")]}");
            Assert.True(Math.Abs(computed[new Id("stat.stamina")] - handStamina) < 1e-6,
                $"L{level} stamina: expected {handStamina}, got {computed[new Id("stat.stamina")]}");
            Assert.True(Math.Abs(computed[new Id("stat.attack_power")] - handAttackPower) < 1e-6,
                $"L{level} attack_power: expected {handAttackPower}, got {computed[new Id("stat.attack_power")]}");
        }

        [Fact]
        public void Compute_CachesResultAcrossCalls()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 20260916399UL, playerLevel: 1);
            var calculator = new ExpectedStatCalculator(
                world.Registry, world.AnchorTable!, ClassId, QualityCommon, new BudgetSolver(), BudgetCurveId);

            var first = calculator.Compute(10);
            var second = calculator.Compute(10);

            Assert.Same(first, second);
        }

        [Fact]
        public void Compute_ArmorStatClampedToNonNegative()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 20260916398UL, playerLevel: 1);
            var calculator = new ExpectedStatCalculator(
                world.Registry, world.AnchorTable!, ClassId, QualityCommon, new BudgetSolver(), BudgetCurveId);

            var computed = calculator.Compute(1);

            Assert.True(computed[new Id("stat.armor")] >= 0.0);
        }

        // -----------------------------------------------------------------
        // 消费方反馈第 45 条（2026-09-17）：阻断态下不抛异常 + 降级标记如实反映
        // -----------------------------------------------------------------

        private static IEventBus MakeMinimalBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
            });
            return new EventBus(catalog);
        }

        // 与 AnchorTableTests.ThreeLevelsJson 完全一致（该文件已验证能通过 SimAnchorValidationRule
        // 的连续性/单调性检查），本文件独立复制一份而不是跨文件引用私有常量。
        private const string AnchorRowsJson =
            "[" +
            "{\"id\": \"sim.anchor.l1\", \"level\": 1, \"hp\": 100, \"dps\": 10, \"ttk_seconds\": 5, " +
            "\"ttd_seconds\": 8, \"expected_item_level\": 1, \"level_duration_seconds\": 300, " +
            "\"kill_interval_seconds\": 15, \"quest_share\": 0.3}," +
            "{\"id\": \"sim.anchor.l2\", \"level\": 2, \"hp\": 140, \"dps\": 14, \"ttk_seconds\": 5, " +
            "\"ttd_seconds\": 8, \"expected_item_level\": 2, \"level_duration_seconds\": 320, " +
            "\"kill_interval_seconds\": 15, \"quest_share\": 0.3}" +
            "]";

        private const string ArchClassJson =
            "[{\"id\": \"arch.class.n45\", \"base_stats\": {\"stat.strength\": 12}}]";

        private const string StatDefJson =
            "[{\"id\": \"stat.strength\", \"category\": \"primary\", \"default_base\": 0}]";

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        /// <summary>
        /// 消费方反馈第 45 条最小夹具：只登记 <see cref="ExpectedStatCalculator"/> 构造函数/<see
        /// cref="ExpectedStatCalculator.Compute"/> 实际用到的最小表集（<c>arch.class</c>/
        /// <c>stat.definition</c>/<c>sim.anchor</c>），<c>stat.weight</c> 故意不登记任何行（保持
        /// 空表）——<c>_statMix</c> 因此为空，<see cref="ExpectedStatCalculator.Compute"/> 内部
        /// 跳过整个 <see cref="IBudgetSolver.Solve"/> 装备反解分支，不需要
        /// <c>item.slot_definition</c>/<c>item.quality_definition</c>/<c>item.budget_curve</c> 三张
        /// 表参与，把夹具收窄到"验证阻断态下不抛异常"这一件事本身需要的最小范围。<paramref
        /// name="withBlockingUnrelatedWidget"/> 为 <c>true</c> 时额外登记与上述表完全无关的
        /// <c>test.widget</c>/<c>test.owner</c> 并注入一条坏引用触发阻断。</summary>
        private static DataRegistry BuildMinimalRegistryCore(bool withBlockingUnrelatedWidget, out ValidationReport report)
        {
            var source = new InMemoryDataSource()
                .Add("arch.class", Envelope("arch.class", ArchClassJson))
                .Add("stat.definition", Envelope("stat.definition", StatDefJson))
                .Add("sim.anchor", Envelope("sim.anchor", AnchorRowsJson));

            if (withBlockingUnrelatedWidget)
            {
                source.Add("test.widget", Envelope("test.widget", "[{\"id\": \"test.widget.a\", \"owner\": \"test.owner.ghost\"}]"));
            }

            var registry = new DataRegistry(source, MakeMinimalBus(), new DataRegistryOptions { FailOnUnknownTable = false });
            SimSchemaCatalog.RegisterAll(registry);

            if (withBlockingUnrelatedWidget)
            {
                registry.RegisterSchema(new TableSchema(
                    "test.widget", "id", 1,
                    new[]
                    {
                        new FieldSchema("id", FieldKind.Id, required: true),
                        new FieldSchema("owner", FieldKind.Reference, required: false, referenceTable: "test.owner"),
                    }));
                registry.RegisterSchema(new TableSchema(
                    "test.owner", "id", 1,
                    new[] { new FieldSchema("id", FieldKind.Id, required: true) }));
            }

            report = registry.LoadAll();
            return registry;
        }

        /// <summary><see cref="AnchorTable"/> 自身在构造期就对 <c>sim.anchor</c> 做严格读取（其
        /// 判断记录明确"调用方总是在 LoadAll 通过校验之后才可能走到构造 AnchorTable 这一步"，不属于
        /// 本次反馈第 45 条要修的"只读分析入口"之列——它是运行期宿主 <see cref="HeadlessWorldBuilder"/>
        /// 唯一的构造路径，本任务书硬性规则也未把它列入可改动文件清单）——registry 阻断态是整体
        /// 级别的，即便 <c>sim.anchor</c> 本身与触发阻断的记录无关，<see cref="AnchorTable"/> 构造
        /// 时仍会照常抛异常。本测试因此始终从一个不阻断的干净 registry 构造 <see cref="AnchorTable"/>，
        /// 只把"阻断态 registry"传给 <see cref="ExpectedStatCalculator"/> 的 <c>view</c> 参数——这
        /// 正是本条反馈描述的真实场景：内容工具持有的 <see cref="AnchorTable"/> 快照构造于早先某次
        /// 成功加载，用户随后把数据改坏触发阻断，仍想用旧快照 + 当前（阻断的）registry 重算预算/
        /// 期望属性展示。</summary>
        private static (DataRegistry Registry, AnchorTable Anchors) BuildMinimalRegistry(bool withBlockingUnrelatedWidget, out ValidationReport report)
        {
            var cleanRegistry = BuildMinimalRegistryCore(withBlockingUnrelatedWidget: false, out var cleanReport);
            Assert.False(cleanReport.IsBlocking);
            var anchors = new AnchorTable(cleanRegistry);

            var registry = BuildMinimalRegistryCore(withBlockingUnrelatedWidget, out report);
            return (registry, anchors);
        }

        [Fact]
        public void Compute_NonBlockingState_MatchesPreChangeResult_NotDegraded()
        {
            var (registry, anchors) = BuildMinimalRegistry(withBlockingUnrelatedWidget: false, out var report);
            Assert.False(report.IsBlocking);

            var calculator = new ExpectedStatCalculator(
                registry, anchors, new Id("arch.class.n45"), new Id("item.quality.n45"), new BudgetSolver());

            var computed = calculator.Compute(1);

            Assert.Equal(12.0, computed[new Id("stat.strength")], 9);
            Assert.False(calculator.IsDegraded);
            Assert.Empty(calculator.MissingTables);
        }

        [Fact]
        public void Constructor_And_Compute_BlockingState_UnrelatedReferenceIntegrityError_DoesNotThrow_AndNotDegraded()
        {
            // 反馈原文复现：registry 因与本次计算用到的 arch.class/stat.definition/sim.anchor 完全
            // 无关的坏引用（test.widget.owner 指向不存在的 test.owner.ghost）整体阻断。
            var (registry, anchors) = BuildMinimalRegistry(withBlockingUnrelatedWidget: true, out var report);
            Assert.True(report.IsBlocking);
            Assert.Throws<InvalidOperationException>(() => registry.GetAll("arch.class"));

            // 修复前：下面这行会在构造函数内部读 arch.class/stat.definition 时抛
            // InvalidOperationException——即使这些表与触发阻断的 test.widget.owner 毫无关系。
            // 修复后：不抛异常，构造与 Compute 的结果与非阻断态完全一致（不真的降级）。
            var calculator = new ExpectedStatCalculator(
                registry, anchors, new Id("arch.class.n45"), new Id("item.quality.n45"), new BudgetSolver());

            Assert.False(calculator.IsDegraded);
            Assert.Empty(calculator.MissingTables);

            var computed = calculator.Compute(1);

            Assert.Equal(12.0, computed[new Id("stat.strength")], 9);
            Assert.False(calculator.IsDegraded);
            Assert.Empty(calculator.MissingTables);
        }

        [Fact]
        public void Constructor_BlockingState_ArchClassGenuinelyMissing_StillThrowsArgumentException()
        {
            // 判断记录回归：阻断态下"记录本就不存在"（调用方传了个不存在的职业 id）与"因阻断读不到"
            // 两种情形要分开处理——前者维持既有行为，继续抛 ArgumentException（调用方用法错误），
            // 不能被本次修复误吞成静默返回一个全零的降级结果。
            var (registry, anchors) = BuildMinimalRegistry(withBlockingUnrelatedWidget: true, out var report);
            Assert.True(report.IsBlocking);

            Assert.Throws<ArgumentException>(() => new ExpectedStatCalculator(
                registry, anchors, new Id("arch.class.does_not_exist"), new Id("item.quality.n45"), new BudgetSolver()));
        }
    }
}
