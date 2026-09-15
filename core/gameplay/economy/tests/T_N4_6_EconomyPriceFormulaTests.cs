using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Gameplay.Economy;
using Tests.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Economy
{
    /// <summary>
    /// 分阶段落地计划 T-N4-6 验收（ADR-0034 决策 2；08 第 7.4 节；04 第 1.1/5 节）：
    /// <c>econ.value_curve</c>/<c>econ.gold_base_curve</c> 表、价格公式（基准价值 =
    /// econ.value_curve(item_level) × 品质价格倍率 × 槽位价格系数，<c>value_override</c> 优先）、
    /// <c>price_amount</c> 可选与缺省走公式、售价比例策略项、手填偏离警告。买价/售价 ≥ 4 组
    /// （含 <c>value_override</c> 优先一组）；偏离警告正负例。
    /// </summary>
    public sealed class T_N4_6_EconomyPriceFormulaTests
    {
        private const string CurrencyRow =
            "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.coin\", \"display_ref\": \"display.coin\"}]";

        // 品质：common 价格倍率 1.0；rare 价格倍率 1.5。
        private const string QualityRows =
            "[{\"id\": \"item.quality.sample_common\", \"name_key\": \"l10n.q1\", \"price_multiplier\": 1.0}," +
            "{\"id\": \"item.quality.sample_rare\", \"name_key\": \"l10n.q2\", \"price_multiplier\": 1.5}]";

        // 槽位：slot_a 价格系数 1.0；slot_b 价格系数 2.0。
        private const string SlotRows =
            "[{\"id\": \"item.slot.sample_a\", \"name_key\": \"l10n.s1\", \"price_coefficient\": 1.0}," +
            "{\"id\": \"item.slot.sample_b\", \"name_key\": \"l10n.s2\", \"price_coefficient\": 2.0}]";

        // 曲线：econ.value.default，物品等级 1→10、10→100（线性），供 item_level=10 精确取 100。
        private const string ValueCurveRows =
            "[{\"id\": \"econ.value.default\", \"entries\": [{\"x\": 1, \"y\": 10}, {\"x\": 10, \"y\": 100}]}]";

        private const string ItemRows =
            "[" +
            // 基准价值 = 曲线(10)=100 × 品质(common)1.0 × 槽位(a)1.0 = 100。
            "{\"id\": \"item.sample_potion\", \"slot\": \"item.slot.sample_a\", \"quality\": \"item.quality.sample_common\", " +
            "\"item_level\": 10, \"display_ref\": \"display.potion\", \"stack_size\": 1, \"name_key\": \"l10n.i1\"}," +
            // 基准价值 = 曲线(10)=100 × 品质(rare)1.5 × 槽位(b)2.0 = 300。
            "{\"id\": \"item.sample_gem\", \"slot\": \"item.slot.sample_b\", \"quality\": \"item.quality.sample_rare\", " +
            "\"item_level\": 10, \"display_ref\": \"display.gem\", \"stack_size\": 1, \"name_key\": \"l10n.i2\"}," +
            // value_override 存在时整体取代——即便品质/槽位会算出 300，override 优先给 250。
            "{\"id\": \"item.sample_ring\", \"slot\": \"item.slot.sample_b\", \"quality\": \"item.quality.sample_rare\", " +
            "\"item_level\": 10, \"display_ref\": \"display.ring\", \"stack_size\": 1, \"name_key\": \"l10n.i3\", " +
            "\"value_override\": 250}" +
            "]";

        private static IEventBus NewBus() => EconomyTestSupport.NewEventBus();

        private static string Envelope(string table, string rowsJson) => EconomyTestSupport.Envelope(table, rowsJson);

        private static DataRegistry MakeRegistry(string vendorRowsJson, string? extraItemRow = null)
        {
            var itemRows = extraItemRow == null ? ItemRows : ItemRows.TrimEnd(']') + "," + extraItemRow + "]";

            var source = new InMemoryDataSource()
                .Add(EconomySchemas.Currency.Name, Envelope(EconomySchemas.Currency.Name, CurrencyRow))
                .Add(EconomySchemas.Vendor.Name, Envelope(EconomySchemas.Vendor.Name, vendorRowsJson))
                .Add(EconomySchemas.ValueCurve.Name, Envelope(EconomySchemas.ValueCurve.Name, ValueCurveRows))
                .Add(ItemSchemas.QualityDefinition.Name, Envelope(ItemSchemas.QualityDefinition.Name, QualityRows))
                .Add(ItemSchemas.SlotDefinition.Name, Envelope(ItemSchemas.SlotDefinition.Name, SlotRows))
                .Add(ItemSchemas.Template.Name, Envelope(ItemSchemas.Template.Name, itemRows));

            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.Vendor);
            registry.RegisterSchema(EconomySchemas.ValueCurve);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterValidationRule(new EconomyContentValidationRule());

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        private static EconomyHost NewHost(DataRegistry registry, out Core.Foundation.EventBus.IEventBus bus)
        {
            bus = EconomyTestSupport.NewEventBus();
            var inventory = new FakeInventoryHost();
            return new EconomyHost(registry, bus, inventory, new FakeNumericExprHostFactory());
        }

        // -----------------------------------------------------------------
        // 买价：price_amount 未填时走价格公式。
        // -----------------------------------------------------------------

        [Fact]
        public void Buy_UsesFormula_WhenPriceAmountUnset()
        {
            var vendor =
                "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_potion\", \"price_currency_id\": \"econ.currency.sample_coin\"}]}]";
            var registry = MakeRegistry(vendor);
            var host = NewHost(registry, out var bus);
            var unitId = new Id("player.sample_1");

            host.Add(unitId, new Id("econ.currency.sample_coin"), 1000, sourceId: unitId);
            var result = host.Buy(unitId, new Id("econ.vendor.sample_shop"), new Id("item.sample_potion"), 1);

            Assert.True(result.Success);
            Assert.Equal(100, result.Price); // 曲线(10)=100 × 品质 1.0 × 槽位 1.0
        }

        [Fact]
        public void Buy_UsesFormula_AppliesQualityAndSlotMultipliers()
        {
            var vendor =
                "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_gem\", \"price_currency_id\": \"econ.currency.sample_coin\"}]}]";
            var registry = MakeRegistry(vendor);
            var host = NewHost(registry, out _);
            var unitId = new Id("player.sample_1");

            host.Add(unitId, new Id("econ.currency.sample_coin"), 1000, sourceId: unitId);
            var result = host.Buy(unitId, new Id("econ.vendor.sample_shop"), new Id("item.sample_gem"), 1);

            Assert.True(result.Success);
            Assert.Equal(300, result.Price); // 曲线(10)=100 × 品质(rare) 1.5 × 槽位(b) 2.0
        }

        [Fact]
        public void Buy_PrefersValueOverride_OverCurveFormula()
        {
            var vendor =
                "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_ring\", \"price_currency_id\": \"econ.currency.sample_coin\"}]}]";
            var registry = MakeRegistry(vendor);
            var host = NewHost(registry, out _);
            var unitId = new Id("player.sample_1");

            host.Add(unitId, new Id("econ.currency.sample_coin"), 1000, sourceId: unitId);
            var result = host.Buy(unitId, new Id("econ.vendor.sample_shop"), new Id("item.sample_ring"), 1);

            Assert.True(result.Success);
            // value_override=250 整体取代基准价值——品质(rare)1.5 × 槽位(b)2.0 会算出 300，不采用。
            Assert.Equal(250, result.Price);
        }

        [Fact]
        public void Buy_PrefersExplicitPriceAmount_OverFormula()
        {
            var vendor =
                "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_potion\", \"price_currency_id\": \"econ.currency.sample_coin\", " +
                "\"price_amount\": 555}]}]";
            var registry = MakeRegistry(vendor);
            var host = NewHost(registry, out _);
            var unitId = new Id("player.sample_1");

            host.Add(unitId, new Id("econ.currency.sample_coin"), 1000, sourceId: unitId);
            var result = host.Buy(unitId, new Id("econ.vendor.sample_shop"), new Id("item.sample_potion"), 1);

            Assert.True(result.Success);
            // 公式会算出 100（见 Buy_UsesFormula_WhenPriceAmountUnset），手填 555 优先生效。
            Assert.Equal(555, result.Price);
        }

        // -----------------------------------------------------------------
        // 售价：无 buy_price_rule 时缺省 = 基准价值 × 售价比例；buy_price_rule 存在时既有语义不变。
        // -----------------------------------------------------------------

        [Fact]
        public void Sell_DefaultsToBaseValueTimesSellPriceRatio_WhenNoBuyPriceRule()
        {
            var vendor =
                "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_potion\", \"price_currency_id\": \"econ.currency.sample_coin\", " +
                "\"price_amount\": 999}]}]"; // price_amount 只影响买价，不影响售价公式。
            var registry = MakeRegistry(vendor);
            var unitId = new Id("player.sample_1");
            var inventory = new FakeInventoryHost();
            var bus = EconomyTestSupport.NewEventBus();
            var host = new EconomyHost(registry, bus, inventory, new FakeNumericExprHostFactory());

            inventory.AddItem(unitId, new Id("item.sample_potion"), 1);
            var instanceId = inventory.ListItems(unitId)[0].InstanceId;

            var result = host.Sell(unitId, new Id("econ.vendor.sample_shop"), instanceId, 1);

            Assert.True(result.Success);
            // 基准价值 100 × 售价比例 0.25（EconomyOptions.DefaultBuyPricePct 默认值）= 25。
            Assert.Equal(25, result.Price);
        }

        [Fact]
        public void Sell_StillUsesBuyPriceRule_WhenPresent_RegardlessOfFormulaData()
        {
            var vendor =
                "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", " +
                "\"buy_price_rule\": \"self.level\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_potion\", \"price_currency_id\": \"econ.currency.sample_coin\"}]}]";
            var registry = MakeRegistry(vendor);
            var exprFactory = new FakeNumericExprHostFactory();
            var inventory = new FakeInventoryHost();
            var bus = EconomyTestSupport.NewEventBus();
            var host = new EconomyHost(registry, bus, inventory, exprFactory);
            var unitId = new Id("player.sample_1");
            exprFactory.Levels[unitId] = 42;

            inventory.AddItem(unitId, new Id("item.sample_potion"), 1);
            var instanceId = inventory.ListItems(unitId)[0].InstanceId;

            var result = host.Sell(unitId, new Id("econ.vendor.sample_shop"), instanceId, 1);

            Assert.True(result.Success);
            // buy_price_rule 存在——硬性规则"禁止改 buy_price_rule 表达式语义"：价格公式数据（曲线/
            // 品质/槽位）即便可用也不介入，结果仍是 Expr 求值本身（回归，同既有
            // Sell_UsesBuyPriceRule_WhenVendorDeclaresOne 断言）。
            Assert.Equal(42, result.Price);
        }

        // -----------------------------------------------------------------
        // 手填价格偏离公式（EconomyPriceDeviatesFormulaRule，检查名 econ_price_deviates_formula）。
        // -----------------------------------------------------------------

        [Fact]
        public void DeviationRule_PriceAmountWithinBandwidth_NoWarning()
        {
            // 公式值 100，手填 110，偏离 10% < 20% 阈值。
            var vendor = "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_potion\", \"price_currency_id\": \"econ.currency.sample_coin\", " +
                "\"price_amount\": 110}]}]";
            var report = RunDeviationRule(vendor);

            Assert.DoesNotContain(report.Issues, i => i.Check == EconomyPriceDeviatesFormulaRule.Check);
        }

        [Fact]
        public void DeviationRule_PriceAmountExceedsBandwidth_ReportsWarning()
        {
            // 公式值 100，手填 500，偏离 400% >> 20% 阈值。
            var vendor = "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_potion\", \"price_currency_id\": \"econ.currency.sample_coin\", " +
                "\"price_amount\": 500}]}]";
            var report = RunDeviationRule(vendor);

            var issue = Assert.Single(report.Issues, i => i.Check == EconomyPriceDeviatesFormulaRule.Check);
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal("sell_items", issue.Field);
        }

        [Fact]
        public void DeviationRule_ValueOverrideWithinBandwidth_NoWarning()
        {
            // item.sample_ring 的 value_override=250，公式值（品质 rare 1.5 × 槽位 b 2.0 × 曲线 100）=300；
            // 偏离 |250-300|/300 ≈ 16.7% < 20% 阈值。
            var vendor = "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_ring\", \"price_currency_id\": \"econ.currency.sample_coin\", " +
                "\"price_amount\": 250}]}]"; // 与偏离规则无关，只是让 sell_items 合法
            var report = RunDeviationRule(vendor);

            Assert.DoesNotContain(report.Issues,
                i => i.Check == EconomyPriceDeviatesFormulaRule.Check && i.Field == "value_override");
        }

        [Fact]
        public void DeviationRule_ValueOverrideExceedsBandwidth_ReportsWarning()
        {
            // 新增一条 item.template：value_override=1（公式值 100），偏离 99% >> 20% 阈值。
            const string extraItem =
                "{\"id\": \"item.sample_cheap\", \"slot\": \"item.slot.sample_a\", \"quality\": \"item.quality.sample_common\", " +
                "\"item_level\": 10, \"display_ref\": \"display.cheap\", \"stack_size\": 1, \"name_key\": \"l10n.i4\", " +
                "\"value_override\": 1}";
            var vendor = "[{\"id\": \"econ.vendor.sample_shop\", \"name_key\": \"l10n.shop\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_potion\", \"price_currency_id\": \"econ.currency.sample_coin\", " +
                "\"price_amount\": 100}]}]"; // 该条不偏离，只用于让 vendor 合法；本用例焦点是 value_override 分支
            var report = RunDeviationRule(vendor, extraItem);

            var issue = Assert.Single(report.Issues,
                i => i.Check == EconomyPriceDeviatesFormulaRule.Check && i.Field == "value_override");
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal("item.sample_cheap", issue.RecordKey);
        }

        [Fact]
        public void DeviationRule_NonEscalatable_UnderWarningsBlock_DoesNotBlock()
        {
            // 本用例需要 report.IsBlocking 只反映本规则自己的 Warning，因此用最小自包含数据集
            // （各表各一条记录）并显式补上 l10n.locale/l10n.text 覆盖全部 name_key（否则"文本键
            // 存在"内置检查因 l10n.text 未注册另降级出一批可提升的 Warning，会一并阻断，掩盖本规则
            // 本身"不可提升"的行为——同 core/numbers/stat_block.
            // StatDefinitionConsumerValidationRuleTests.NoConsumerWarning_UnderWarningsBlock_DoesNotBlock
            // 同一处理惯例）。
            const string currency =
                "[{\"id\": \"econ.currency.c1\", \"name_key\": \"l10n.c1\", \"display_ref\": \"display.c1\"}]";
            const string vendor =
                "[{\"id\": \"econ.vendor.v1\", \"name_key\": \"l10n.v1\", \"sell_items\": [" +
                "{\"item_id\": \"item.i1\", \"price_currency_id\": \"econ.currency.c1\", \"price_amount\": 500}]}]";
            const string quality = "[{\"id\": \"item.quality.q1\", \"name_key\": \"l10n.q1\", \"price_multiplier\": 1.0}]";
            const string slot = "[{\"id\": \"item.slot.s1\", \"name_key\": \"l10n.s1\", \"price_coefficient\": 1.0}]";
            const string valueCurve =
                "[{\"id\": \"econ.value.default\", \"entries\": [{\"x\": 1, \"y\": 5}, {\"x\": 10, \"y\": 100}]}]";
            const string item =
                "[{\"id\": \"item.i1\", \"slot\": \"item.slot.s1\", \"quality\": \"item.quality.q1\", " +
                "\"item_level\": 10, \"display_ref\": \"display.i1\", \"stack_size\": 1, \"name_key\": \"l10n.i1\"}]";
            const string locales = "[{\"id\": \"l10n.locale.zh_cn\", \"is_default\": true}]";
            const string texts =
                "[{\"key\": \"l10n.c1\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"占位\"}," +
                "{\"key\": \"l10n.v1\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"占位\"}," +
                "{\"key\": \"l10n.q1\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"占位\"}," +
                "{\"key\": \"l10n.s1\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"占位\"}," +
                "{\"key\": \"l10n.i1\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"占位\"}]";

            var source = new InMemoryDataSource()
                .Add(EconomySchemas.Currency.Name, Envelope(EconomySchemas.Currency.Name, currency))
                .Add(EconomySchemas.Vendor.Name, Envelope(EconomySchemas.Vendor.Name, vendor))
                .Add(EconomySchemas.ValueCurve.Name, Envelope(EconomySchemas.ValueCurve.Name, valueCurve))
                .Add(ItemSchemas.QualityDefinition.Name, Envelope(ItemSchemas.QualityDefinition.Name, quality))
                .Add(ItemSchemas.SlotDefinition.Name, Envelope(ItemSchemas.SlotDefinition.Name, slot))
                .Add(ItemSchemas.Template.Name, Envelope(ItemSchemas.Template.Name, item))
                .Add("l10n.locale", Envelope("l10n.locale", locales))
                .Add("l10n.text", Envelope("l10n.text", texts));

            var registry = new DataRegistry(source, NewBus(),
                new DataRegistryOptions { Strictness = DataRegistryStrictness.WarningsBlock });
            registry.RegisterSchema(EconomySchemas.Currency);
            registry.RegisterSchema(EconomySchemas.Vendor);
            registry.RegisterSchema(EconomySchemas.ValueCurve);
            registry.RegisterSchema(ItemSchemas.QualityDefinition);
            registry.RegisterSchema(ItemSchemas.SlotDefinition);
            registry.RegisterSchema(ItemSchemas.Template);
            registry.RegisterSchema(Core.Foundation.Localization.L10nSchemas.Locale);
            registry.RegisterSchema(Core.Foundation.Localization.L10nSchemas.Text);
            registry.RegisterValidationRule(new EconomyContentValidationRule());
            registry.RegisterValidationRule(new EconomyPriceDeviatesFormulaRule(new Id("econ.value.default")));

            var report = registry.LoadAll();

            var issue = Assert.Single(report.Issues, i => i.Check == EconomyPriceDeviatesFormulaRule.Check);
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal(1, report.WarningCount);
            Assert.Equal(1, report.NonEscalatableWarningCount);
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        private static ValidationReport RunDeviationRule(string vendorRowsJson, string? extraItemRow = null)
        {
            var registry = MakeRegistry(vendorRowsJson, extraItemRow);
            registry.RegisterValidationRule(new EconomyPriceDeviatesFormulaRule(new Id("econ.value.default")));
            return registry.Validate();
        }

        // -----------------------------------------------------------------
        // 新表 schema 覆盖：econ.value_curve/econ.gold_base_curve 曲线形态。
        // -----------------------------------------------------------------

        [Fact]
        public void ValueCurve_And_GoldBaseCurve_LoadWithoutErrors()
        {
            const string valueCurve =
                "[{\"id\": \"econ.value.sample\", \"entries\": [{\"x\": 1, \"y\": 5}, {\"x\": 50, \"y\": 500}]}]";
            const string goldBaseCurve =
                "[{\"id\": \"econ.gold_base.sample\", \"entries\": [{\"x\": 1, \"y\": 1}, {\"x\": 50, \"y\": 50}]}]";

            var source = new InMemoryDataSource()
                .Add(EconomySchemas.ValueCurve.Name, Envelope(EconomySchemas.ValueCurve.Name, valueCurve))
                .Add(EconomySchemas.GoldBaseCurve.Name, Envelope(EconomySchemas.GoldBaseCurve.Name, goldBaseCurve));

            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions());
            registry.RegisterSchema(EconomySchemas.ValueCurve);
            registry.RegisterSchema(EconomySchemas.GoldBaseCurve);

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void ValueCurve_NonMonotonic_ReportsCurveMonotonicFiniteError()
        {
            // 通用曲线单调有限规则（T-N0-3）对全部登记为断点表形态的字段统一生效——本表未把该规则
            // 自行注册也应受其约束一致，此用例只验证 entries 形态登记正确（field_curve_shape 元数据
            // 门禁另由 SchemaAudit 覆盖，非本任务重复登记），越界排序按 x 升序取值即可，不在本用例
            // 重复断言 curve_monotonic_finite 的通用规则本身（该规则由调用方决定是否注册）。
            var curveField = EconomySchemas.ValueCurve.GetField("entries")!;
            Assert.NotNull(curveField.Curve);
            Assert.Equal(CurveShape.Breakpoints, curveField.Curve!.Shape);
            Assert.Equal(CurveAxis.ItemLevel, curveField.Curve!.Axis);

            var goldField = EconomySchemas.GoldBaseCurve.GetField("entries")!;
            Assert.Equal(CurveShape.Breakpoints, goldField.Curve!.Shape);
            Assert.Equal(CurveAxis.Level, goldField.Curve!.Axis);
        }
    }
}
