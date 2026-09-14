using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Numbers.StatBlock
{
    /// <summary>
    /// 分阶段落地计划 T-N1-9 验收（ADR-0030 决策 8/9；04 第 5 节"属性无消费者"）：
    /// <see cref="StatDefinitionConsumerValidationRule"/> 无消费者警告正负例各 ≥ 1，外加
    /// 覆盖"跨表 Reference 字段扫描""framework 内置消费者豁免""NonEscalatable 在
    /// WarningsBlock 下不阻断"三条关键行为。全部用例只登记 <c>stat.definition</c> 与
    /// <c>stat.weight</c> 两张表（不引入 archetype/combat 等其它模块的 schema），保持本规则
    /// "不硬编码具体消费表名、通用扫描"这一设计的最小可验证范围。
    /// </summary>
    public sealed class StatDefinitionConsumerValidationRuleTests
    {
        private static IEventBus MakeBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private static IDataRegistry BuildRegistry(
            string definitionRowsJson, string? weightRowsJson, DataRegistryStrictness strictness,
            out ValidationReport report)
        {
            var source = new InMemoryDataSource().Add("stat.definition", definitionRowsJson);
            if (weightRowsJson != null)
            {
                source.Add("stat.weight", weightRowsJson);
            }

            var registry = new DataRegistry(source, MakeBus(), new DataRegistryOptions { Strictness = strictness });
            registry.RegisterSchema(StatSchemas.Definition);
            if (weightRowsJson != null)
            {
                registry.RegisterSchema(StatSchemas.Weight);
            }

            registry.RegisterValidationRule(new StatDefinitionConsumerValidationRule());
            report = registry.LoadAll();
            return registry;
        }

        // -----------------------------------------------------------------
        // 负例：属性未被任何已注册表的引用字段引用 → 报 Warning。
        // -----------------------------------------------------------------

        [Fact]
        public void NoConsumer_UnreferencedStat_ReportsWarning()
        {
            const string definitions = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.orphan"", ""name_key"": ""l10n.a"", ""category"": ""misc"" }
                ]
            }";

            BuildRegistry(definitions, weightRowsJson: null, DataRegistryStrictness.WarningsAllowed, out var report);

            var issue = Assert.Single(report.Issues, i => i.Check == StatDefinitionConsumerValidationRule.CheckNoConsumer);
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal("stat.orphan", issue.RecordKey);
            Assert.Equal(StatDefinitionConsumerValidationRule.CheckNoConsumer, issue.Check);
        }

        // -----------------------------------------------------------------
        // 正例 1：属性被 stat.weight.stat 引用 → 不报。
        // -----------------------------------------------------------------

        [Fact]
        public void HasConsumer_ViaStatWeightReference_NoWarning()
        {
            const string definitions = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.weighted"", ""name_key"": ""l10n.a"", ""category"": ""misc"" }
                ]
            }";
            const string weights = @"
            {
                ""table"": ""stat.weight"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""stat.weight.weighted"", ""stat"": ""stat.weighted"", ""weight"": 1.0 }
                ]
            }";

            BuildRegistry(definitions, weights, DataRegistryStrictness.WarningsAllowed, out var report);

            Assert.DoesNotContain(report.Issues, i => i.Check == StatDefinitionConsumerValidationRule.CheckNoConsumer);
        }

        // -----------------------------------------------------------------
        // 正例 2：属性作为另一条派生属性 derived_from[].stat 的来源被引用 → 不报（同一张表内部
        // 的 Reference 字段，验证扫描不局限于"跨表"）。
        // -----------------------------------------------------------------

        [Fact]
        public void HasConsumer_ViaDerivedFromSource_NoWarning()
        {
            const string definitions = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.source_only"", ""name_key"": ""l10n.a"", ""category"": ""primary"" },
                    { ""id"": ""stat.derived_only"", ""name_key"": ""l10n.b"", ""category"": ""derived"",
                      ""derived_from"": [ { ""stat"": ""stat.source_only"", ""coefficient"": 2.0 } ] }
                ]
            }";

            BuildRegistry(definitions, weightRowsJson: null, DataRegistryStrictness.WarningsAllowed, out var report);

            // stat.source_only 被 stat.derived_only.derived_from 引用，不应报；
            // stat.derived_only 自己没有任何消费者，应当报。
            Assert.DoesNotContain(report.Issues,
                i => i.Check == StatDefinitionConsumerValidationRule.CheckNoConsumer && i.RecordKey == "stat.source_only");
            Assert.Contains(report.Issues,
                i => i.Check == StatDefinitionConsumerValidationRule.CheckNoConsumer && i.RecordKey == "stat.derived_only");
        }

        // -----------------------------------------------------------------
        // 正例 3：框架内置消费者属性名清单豁免（CombatOptions 默认属性名，规则扫描不到运行时选项，
        // 见规则类型判断记录）。
        // -----------------------------------------------------------------

        [Fact]
        public void HasConsumer_ViaFrameworkBuiltinExemption_NoWarning()
        {
            const string definitions = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.armor"", ""name_key"": ""l10n.a"", ""category"": ""defense"" }
                ]
            }";

            BuildRegistry(definitions, weightRowsJson: null, DataRegistryStrictness.WarningsAllowed, out var report);

            Assert.DoesNotContain(report.Issues, i => i.Check == StatDefinitionConsumerValidationRule.CheckNoConsumer);
        }

        // -----------------------------------------------------------------
        // NonEscalatable：WarningsBlock 下仍不阻断（04 第 5 节"警告级这一组登记为不可提升"）。
        // -----------------------------------------------------------------

        [Fact]
        public void NoConsumerWarning_UnderWarningsBlock_DoesNotBlock()
        {
            // 本用例需要 report.IsBlocking 只反映本规则自己的 Warning，因此显式补上 l10n.locale/
            // l10n.text（否则"文本键存在"内置检查因 l10n.text 未注册另降级出一条可提升的 Warning，
            // 会一并阻断，掩盖本规则本身"不可提升"的行为，见 BuildRegistry 共用夹具未覆盖的场景）。
            const string definitions = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.orphan_block"", ""name_key"": ""l10n.a"", ""category"": ""misc"" }
                ]
            }";
            const string locales = @"
            {
                ""table"": ""l10n.locale"",
                ""schema_version"": 1,
                ""rows"": [ { ""id"": ""l10n.locale.zh_cn"", ""is_default"": true } ]
            }";
            const string texts = @"
            {
                ""table"": ""l10n.text"",
                ""schema_version"": 1,
                ""rows"": [ { ""key"": ""l10n.a"", ""locale"": ""l10n.locale.zh_cn"", ""text"": ""占位"" } ]
            }";

            var source = new InMemoryDataSource()
                .Add("stat.definition", definitions)
                .Add("l10n.locale", locales)
                .Add("l10n.text", texts);
            var registry = new DataRegistry(source, MakeBus(),
                new DataRegistryOptions { Strictness = DataRegistryStrictness.WarningsBlock });
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(Core.Foundation.Localization.L10nSchemas.Locale);
            registry.RegisterSchema(Core.Foundation.Localization.L10nSchemas.Text);
            registry.RegisterValidationRule(new StatDefinitionConsumerValidationRule());
            var report = registry.LoadAll();

            var ownIssue = Assert.Single(report.Issues, i => i.Check == StatDefinitionConsumerValidationRule.CheckNoConsumer);
            Assert.Equal(ValidationSeverity.Warning, ownIssue.Severity);
            Assert.Equal(1, report.WarningCount);
            Assert.Equal(1, report.NonEscalatableWarningCount);
            Assert.False(report.IsBlocking);
        }
    }
}
