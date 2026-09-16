using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
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
    /// <para>
    /// T-N5-2 补充：<c>HasConsumer_ViaExprFieldSelfStatReference_NoWarning</c>/
    /// <c>HasConsumer_ViaExprFieldTargetStatReference_NoWarning</c>/
    /// <c>NoConsumer_ExprFieldRemoved_ReportsWarning</c>/
    /// <c>NoConsumer_ExprFieldReferencesDifferentStat_StillReportsWarningForUnreferencedOne</c>
    /// 四条覆盖"Expr 字段语法树里 self.stat(&lt;id&gt;)/target.stat(&lt;id&gt;) 引用"这一新扫描
    /// 来源——自建 <c>test.expr_holder</c> 最小表（不依赖 L2 <c>skill.def</c>），因为本规则的
    /// 扫描通用覆盖"任意已注册表的任意 Expr 字段"，不需要真的用 <c>skill.def.use_condition</c>
    /// 才能验证。
    /// </para>
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
        // T-N5-2：Expr 字段扫描（self.stat(<id>)/target.stat(<id>) 引用）夹具。
        // -----------------------------------------------------------------

        /// <summary>只登记 <c>self</c>/<c>target</c> 两个分组的 <c>stat</c> 引用签名（与生产环境
        /// <c>RulesExprHostFactory.Host.QueryUnit</c> 的 <c>"stat"</c> 分支同一形状：
        /// <c>Number</c> 返回、一个 <c>Id</c> 参数），供本测试类自建的 <c>test.expr_holder</c> 表
        /// 的 <c>condition</c> 字段在加载期真正走一遍 <c>expr_parsable</c>（不是本规则内部另用的
        /// <see cref="PermissiveExprSchema"/>——那个只用于本规则自身的只读遍历，见该规则判断
        /// 记录，与这里"内容加载期用的 schema 是否知道 self.stat/target.stat"是两件事）。</summary>
        private static ExprSchema StatRefExprSchema() =>
            new ExprSchema()
                .Register("self", "stat", ExprValueKind.Number, ExprValueKind.Id)
                .Register("target", "stat", ExprValueKind.Number, ExprValueKind.Id);

        /// <summary>本测试类自建的最小表（不引用 L2 <c>skill.def</c> 等具体表——本规则的扫描通用
        /// 覆盖"任意已注册表的任意 Expr 字段"，不需要真的用 <c>skill.def.use_condition</c> 才能
        /// 验证，见规则类型判断记录第三段）：一张只有 <c>id</c> + <c>condition</c>（Expr）两个
        /// 字段的表，模拟"某处内容用 Expr 引用了一个属性"的场景。</summary>
        private static TableSchema ExprHolderSchema() => new TableSchema(
            "test.expr_holder", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("condition", FieldKind.Expr, required: false),
            });

        private static ValidationReport BuildRegistryWithExprHolder(string definitionRowsJson, string holderRowsJson)
        {
            var source = new InMemoryDataSource()
                .Add("stat.definition", definitionRowsJson)
                .Add("test.expr_holder", holderRowsJson);

            var registry = new DataRegistry(source, MakeBus(),
                new DataRegistryOptions { Strictness = DataRegistryStrictness.WarningsAllowed, ExprSchema = StatRefExprSchema() });
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(ExprHolderSchema());
            registry.RegisterValidationRule(new StatDefinitionConsumerValidationRule());
            return registry.LoadAll();
        }

        // -----------------------------------------------------------------
        // T-N5-2 正例：属性只被某条内容的 Expr 字段里 self.stat(<id>) 引用 → 不报。
        // -----------------------------------------------------------------

        [Fact]
        public void HasConsumer_ViaExprFieldSelfStatReference_NoWarning()
        {
            const string definitions = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.only_via_expr"", ""name_key"": ""l10n.a"", ""category"": ""misc"" }
                ]
            }";
            const string holders = @"
            {
                ""table"": ""test.expr_holder"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""test.expr_holder.cond_a"", ""condition"": ""self.stat(stat.only_via_expr) > 5"" }
                ]
            }";

            var report = BuildRegistryWithExprHolder(definitions, holders);

            Assert.DoesNotContain(report.Issues,
                i => i.Check == StatDefinitionConsumerValidationRule.CheckNoConsumer &&
                     i.RecordKey == "stat.only_via_expr");
        }

        // -----------------------------------------------------------------
        // T-N5-2 正例（补充）：target.stat(<id>) 同样计入消费者（同一 "stat" 引用的另一半分组）。
        // -----------------------------------------------------------------

        [Fact]
        public void HasConsumer_ViaExprFieldTargetStatReference_NoWarning()
        {
            const string definitions = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.only_via_target_expr"", ""name_key"": ""l10n.a"", ""category"": ""misc"" }
                ]
            }";
            const string holders = @"
            {
                ""table"": ""test.expr_holder"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""test.expr_holder.cond_b"", ""condition"": ""target.stat(stat.only_via_target_expr) < 1"" }
                ]
            }";

            var report = BuildRegistryWithExprHolder(definitions, holders);

            Assert.DoesNotContain(report.Issues,
                i => i.Check == StatDefinitionConsumerValidationRule.CheckNoConsumer &&
                     i.RecordKey == "stat.only_via_target_expr");
        }

        // -----------------------------------------------------------------
        // T-N5-2 负例：去掉该表达式（本条内容不再引用任何 Expr）→ 恢复报警告
        // （验收标准原文"去掉该表达式 → 报警告"）。
        // -----------------------------------------------------------------

        [Fact]
        public void NoConsumer_ExprFieldRemoved_ReportsWarning()
        {
            const string definitions = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.only_via_expr"", ""name_key"": ""l10n.a"", ""category"": ""misc"" }
                ]
            }";
            const string holders = @"
            {
                ""table"": ""test.expr_holder"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""test.expr_holder.cond_a"" }
                ]
            }";

            var report = BuildRegistryWithExprHolder(definitions, holders);

            Assert.Contains(report.Issues,
                i => i.Check == StatDefinitionConsumerValidationRule.CheckNoConsumer &&
                     i.RecordKey == "stat.only_via_expr");
        }

        // -----------------------------------------------------------------
        // T-N5-2：Expr 字段引用别的属性时，不应误把本属性计入消费者（精确匹配 self.stat 的参数，
        // 不是"字段里出现了 Expr 就全部放行"）。
        // -----------------------------------------------------------------

        [Fact]
        public void NoConsumer_ExprFieldReferencesDifferentStat_StillReportsWarningForUnreferencedOne()
        {
            const string definitions = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.only_via_expr"", ""name_key"": ""l10n.a"", ""category"": ""misc"" },
                    { ""id"": ""stat.unrelated"", ""name_key"": ""l10n.b"", ""category"": ""misc"" }
                ]
            }";
            const string holders = @"
            {
                ""table"": ""test.expr_holder"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""test.expr_holder.cond_a"", ""condition"": ""self.stat(stat.only_via_expr) > 5"" }
                ]
            }";

            var report = BuildRegistryWithExprHolder(definitions, holders);

            Assert.DoesNotContain(report.Issues,
                i => i.Check == StatDefinitionConsumerValidationRule.CheckNoConsumer &&
                     i.RecordKey == "stat.only_via_expr");
            Assert.Contains(report.Issues,
                i => i.Check == StatDefinitionConsumerValidationRule.CheckNoConsumer &&
                     i.RecordKey == "stat.unrelated");
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

        // -----------------------------------------------------------------
        // 深度复审 A（测试覆盖缺口 #5）补测：动态 DeclareReference 声明来源（类型判断记录 (1)）——
        // 当前仓库确实"尚无表以这种方式指向 stat.definition"，本条用自建的 test.declare_ref_holder
        // 最小表 + IDataRegistry.DeclareReference 手工构造这条分支，真实数据集无法触达，只有本单测
        // 能验证其正确性（同类型判断记录原文）。
        // -----------------------------------------------------------------

        /// <summary>本测试类自建的最小表：一个 <c>target_stat</c> 字段登记为 <see cref="FieldKind.Id"/>
        /// （不是 <see cref="FieldKind.Reference"/>——否则会被规则扫描逻辑第 (2) 部分"schema 级
        /// Reference 字段"扫到，测不出第 (1) 部分"动态 DeclareReference"这条独立路径），其"整字段
        /// 指向 stat.definition"这层语义完全靠 <see cref="IDataRegistry.DeclareReference(string, string, string)"/>
        /// 动态登记，不经过 schema。</summary>
        private static TableSchema DeclareRefHolderSchema() => new TableSchema(
            "test.declare_ref_holder", "id", 1,
            new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("target_stat", FieldKind.Id, required: false),
            });

        [Fact]
        public void HasConsumer_ViaDynamicDeclareReference_NoWarning_ButUnrelatedStatStillReportsWarning()
        {
            const string definitions = @"
            {
                ""table"": ""stat.definition"",
                ""schema_version"": 2,
                ""rows"": [
                    { ""id"": ""stat.only_via_declare_reference"", ""name_key"": ""l10n.a"", ""category"": ""misc"" },
                    { ""id"": ""stat.truly_unreferenced"", ""name_key"": ""l10n.b"", ""category"": ""misc"" }
                ]
            }";
            const string holders = @"
            {
                ""table"": ""test.declare_ref_holder"",
                ""schema_version"": 1,
                ""rows"": [
                    { ""id"": ""test.declare_ref_holder.entry_a"", ""target_stat"": ""stat.only_via_declare_reference"" }
                ]
            }";

            var source = new InMemoryDataSource()
                .Add("stat.definition", definitions)
                .Add("test.declare_ref_holder", holders);

            var registry = new DataRegistry(source, MakeBus(),
                new DataRegistryOptions { Strictness = DataRegistryStrictness.WarningsAllowed });
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(DeclareRefHolderSchema());
            // 核心动作：只靠动态 DeclareReference 登记"test.declare_ref_holder.target_stat 整字段
            // 指向 stat.definition"这条声明——schema 里 target_stat 是普通 FieldKind.Id，不是
            // Reference，规则扫描逻辑第 (2) 部分（schema 级 Reference/SoftReference/Map 键引用）
            // 天然看不到它，只有第 (1) 部分（GetReferenceDeclarations）能捕获。
            registry.DeclareReference("test.declare_ref_holder", "target_stat", "stat.definition");
            registry.RegisterValidationRule(new StatDefinitionConsumerValidationRule());

            var report = registry.LoadAll();

            // 被动态声明的引用命中的属性：不应报"无消费者"。
            Assert.DoesNotContain(report.Issues,
                i => i.Check == StatDefinitionConsumerValidationRule.CheckNoConsumer &&
                     i.RecordKey == "stat.only_via_declare_reference");
            // 对照组：真正没有任何消费者的属性仍然应该被报——证明"不报"不是规则整体失效，
            // 而是动态声明确实生效命中了对应属性。
            Assert.Contains(report.Issues,
                i => i.Check == StatDefinitionConsumerValidationRule.CheckNoConsumer &&
                     i.RecordKey == "stat.truly_unreferenced");
        }
    }
}
