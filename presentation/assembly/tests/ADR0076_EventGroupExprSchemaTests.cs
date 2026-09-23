using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.Quest;
using Presentation.Assembly;
using Presentation.FeedbackBinder.Schema;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// ADR-0076 根治验收：消费方第二十批第 3 条——<c>event.&lt;key&gt;</c> 未登记字段此前经
    /// <c>QuestExprSchemaEntries.EventGroupPermissiveSchema</c> 被静态期谎称"返回 Bool"，
    /// <c>event.hit_result == "Hit"</c>（运行期实际返回 String）一类条件在 <see cref="DataRegistry.LoadAll"/>
    /// 的 <c>expr_parsable</c> 校验中被误报"比较两侧类型不一致"。本文件直接用
    /// <see cref="PresentationSchemaCatalog.FullExprSchema"/>——
    /// <c>PresentationAssembly</c> 硬编码构造 <c>FeedbackRule</c>/<c>FeedbackBinder</c> 时实际使用的
    /// 那一份生产 schema（同 <see cref="FullExprSchemaKnownKeysTests"/> 使用的实例）——经真实
    /// <see cref="DataRegistry.LoadAll"/> 加载一份真实 <c>feedback.binding</c> 数据验证：
    /// <list type="number">
    /// <item>字符串字段比较（<c>event.hit_result == "Hit"</c>，命中结果，见
    /// <c>Core.Rules.Common.CombatDamageDealtEvent.TryGetField</c> "hitResult" -&gt; String）不再报错；</item>
    /// <item>Id 字段比较（<c>event.school == skill.school.fire</c>，见同方法 "school" -&gt; Id）不再报错；</item>
    /// <item>语法错误、参数个数不对仍然照常报 Error（未整体放水）。</item>
    /// </list>
    /// 运行期真实求值（条件按事件真实字段值判真/假）见
    /// <see cref="PresentationAssemblyTests.EventHitResultStringCondition_ThroughRealFeedbackBinder_EvaluatesTrueOnMatch_ADR0076"/>/
    /// <c>EvaluatesFalseOnMismatch_ADR0076</c>/<c>EventSchoolIdCondition_ThroughRealFeedbackBinder_EvaluatesTrueOnMatch_ADR0076</c>
    /// （经真实 <see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/>/<c>RulesExprHostFactory</c>
    /// 管线，本文件只覆盖静态校验期）。"未知分组仍报错"（<see cref="ExprValidator"/> 层面，解析器本就
    /// 不会为真实内容产出未知分组的引用节点，见 <see cref="ExprValidator"/> 既有测试同款判断记录）另见
    /// <see cref="UnknownGroup_StillReportedError_NotSwallowedByEventPermissiveWrapping"/>。
    /// </summary>
    public class ADR0076_EventGroupExprSchemaTests
    {
        private static IEventBus CreateBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        /// <summary>只登记 <c>feedback.binding</c> 一张表——<c>expr_parsable</c> 校验只依赖
        /// <see cref="DataRegistryOptions.ExprSchema"/>，不依赖其它内容表是否已加载，因此不需要像
        /// <c>PresentationAssemblyTests.Build</c> 那样拉起全部 L0～L5 表即可验证本条目标行为。</summary>
        private static ValidationReport LoadBindingTable(string rowsJson)
        {
            var source = new InMemoryDataSource();
            source.Add("feedback.binding", Envelope("feedback.binding", rowsJson));

            var options = PresentationSchemaCatalog.CreateOptions();
            options.FailOnUnknownTable = false;
            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, CreateBus(), options);
            registry.RegisterSchema(FeedbackSchemas.Binding);
            return registry.LoadAll();
        }

        [Fact]
        public void EventHitResultStringCompare_NoLongerReportsCompareTypeMismatch()
        {
            // 改动前实测（根治前的 EventGroupPermissiveSchema.TryGetSignature 对 event 恒返回固定
            // Bool 签名）：本条会产出 "[Error] expr_parsable ...: 比较两侧类型不一致：Bool 与 String"，
            // 与消费方报告的报错文本一致。
            var report = LoadBindingTable("[{" +
                "\"id\": \"feedback.adr0076_hit_result_ok\", \"event\": \"combat.damage_dealt\", " +
                "\"condition\": \"event.hit_result == \\\"Hit\\\"\", " +
                "\"actions\": [{\"kind\": \"freeze\", \"params\": {\"duration_ms\": 10}}]" +
                "}]");

            Assert.DoesNotContain(report.Issues, i => i.Check == "expr_parsable" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void EventSchoolIdCompare_NoLongerReportsCompareTypeMismatch()
        {
            // Id 字面量比较分支（04 第 6.3 节 event.<字段> 返回 Id 的场景，见
            // CombatDamageDealtEvent.TryGetField "school" -> ExprValue.OfId）：改动前同样会因为
            // EventGroupPermissiveSchema 谎称 event.school 恒返回 Bool 而报 "Bool 与 Id 类型不一致"。
            var report = LoadBindingTable("[{" +
                "\"id\": \"feedback.adr0076_school_ok\", \"event\": \"combat.damage_dealt\", " +
                "\"condition\": \"event.school == skill.school.fire\", " +
                "\"actions\": [{\"kind\": \"freeze\", \"params\": {\"duration_ms\": 10}}]" +
                "}]");

            Assert.DoesNotContain(report.Issues, i => i.Check == "expr_parsable" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void SyntaxError_StillReportsError_NotSwallowedByEventPermissiveWrapping()
        {
            // 反面用例：condition 文本本身不合法（比较符右侧缺操作数），expr_parsable 必须照常报 Error
            // ——证明本次改动没有把 event 分组的校验整体放水成"恒不报错"。
            var report = LoadBindingTable("[{" +
                "\"id\": \"feedback.adr0076_syntax_error\", \"event\": \"combat.damage_dealt\", " +
                "\"condition\": \"event.hit_result == \", " +
                "\"actions\": [{\"kind\": \"freeze\", \"params\": {\"duration_ms\": 10}}]" +
                "}]");

            Assert.Contains(report.Issues, i => i.Check == "expr_parsable" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void ArgCountMismatch_NonEventGroup_StillReportsError()
        {
            // 反面用例（非 event 分组）：quest.is_active 在生产 schema 里精确登记为一个 Id 参数
            // （QuestExprSchemaEntries.RegisterInto），传两个参数应仍报 ArgCountMismatch——证明本次
            // 改动只影响 event 分组，其它分组的参数个数校验行为不变。
            var report = LoadBindingTable("[{" +
                "\"id\": \"feedback.adr0076_arg_count_error\", \"event\": \"combat.damage_dealt\", " +
                "\"condition\": \"quest.is_active(quest.sample_a, quest.sample_b)\", " +
                "\"actions\": [{\"kind\": \"freeze\", \"params\": {\"duration_ms\": 10}}]" +
                "}]");

            Assert.Contains(report.Issues, i => i.Check == "expr_parsable" && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void UnknownGroup_StillReportedError_NotSwallowedByEventPermissiveWrapping()
        {
            // ADR-0015 之后 ExprParser 本就不会为真实内容文本产出"未知分组"的引用节点（未登记的
            // group.key 要么落回 Id 字面量、要么——仅 event 分组——落回"类型未知的引用"，见
            // ExprParser.ParseIdentTerm/ExprValidatorTests 同款判断记录"解析器本身不会产出未知分组的
            // 引用节点，这里手工构造以验证校验器独立检查"）；本用例延续既有惯例，直接对生产 schema
            // 手工构造一个未知分组引用节点，证明 ExprValidator.UnknownGroup 检查在生产 schema 上仍然
            // 成立，没有被本次改动连带放宽。
            var schema = PresentationSchemaCatalog.FullExprSchema;
            var node = new ExprReferenceNode("bogus_group_adr0076", "foo", System.Array.Empty<ExprNode>());
            var issues = ExprValidator.Validate(node, schema);

            Assert.Contains(issues, i => i.Kind == ExprIssueKind.UnknownGroup);
        }

        /// <summary>ADR-0076 判断记录延续验证：<c>QuestExprSchemaEntries.BuildParsingSchema()</c>
        /// 对未登记的 <c>event.&lt;key&gt;</c> 如实返回 <c>false</c>（不再注入固定签名），交给
        /// <see cref="ExprValidator"/> 既有的"类型未知则跳过比较类型检查"分支处理。</summary>
        [Fact]
        public void QuestExprSchemaEntries_TryGetSignature_UnregisteredEventKey_ReturnsFalse()
        {
            var schema = QuestExprSchemaEntries.BuildParsingSchema();

            Assert.False(schema.TryGetSignature(ExprGroups.Event, "hit_result", out _));
        }

        /// <summary>ExprParser 的解析期判定不依赖 schema 对 event 分组返回 true（见
        /// <c>ExprParser.ParseIdentTerm</c> 的 <c>isEventFallbackReference</c> 独立分支，本次未改动）：
        /// 未登记的 <c>event.&lt;key&gt;</c> 仍解析成 <see cref="ExprReferenceNode"/>，不会退化成 Id
        /// 字面量——本次修复只改了静态类型推断，没有改变解析期行为。</summary>
        [Fact]
        public void EventReference_StillParsesAsReferenceNode_NotIdLiteral()
        {
            var schema = QuestExprSchemaEntries.BuildParsingSchema();

            var node = ExprParser.Parse("event.hit_result == \"Hit\"", schema);
            var compare = Assert.IsType<ExprCompareNode>(node);
            var reference = Assert.IsType<ExprReferenceNode>(compare.Left);
            Assert.Equal(ExprGroups.Event, reference.Group);
            Assert.Equal("hit_result", reference.Key);
        }
    }
}
