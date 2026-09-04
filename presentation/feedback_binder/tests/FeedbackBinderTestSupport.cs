using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Core.Rules.ExprHost;
using Presentation.FeedbackBinder.Schema;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>测试共用夹具（同 <c>Tests.Presentation.VfxSfx.VfxSfxTestSupport</c> 惯例）。</summary>
    internal static class FeedbackBinderTestSupport
    {
        public static IEventBus CreateBus(bool strict = false)
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(RulesEventKeys.CombatDamageDealt, "combat", new[] { "sourceId", "targetId", "school", "amount", "isCrit", "hitResult" }),
                new EventDefinition(RulesEventKeys.CombatHealDone, "combat", new[] { "sourceId", "targetId", "amount", "isCrit" }),
                new EventDefinition(RulesEventKeys.AuraApplied, "aura", new[] { "targetId", "auraDefId", "sourceId", "stacks" }),
                new EventDefinition(EventKeys.PresentationPlaybackFinished, "presentation", System.Array.Empty<string>()),
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
            });

            return new EventBus(catalog, new EventBusOptions { StrictCatalog = strict });
        }

        /// <summary>最小 <see cref="IExprHostFactory"/> 假实现：只支持 <c>event.*</c> 求值（经
        /// <see cref="IExprReadableEvent"/>），<c>self</c>/<c>target</c>/<c>combat</c>/<c>enemies</c>/
        /// <c>time</c> 一律按类型默认值处理——本模块的行为测试只关心"按事件字段分流"（09 第 6.1 节
        /// 举例 <c>event.is_crit</c>），不需要拉起完整的 <c>Core.Rules.ExprHost.RulesExprHostFactory</c>
        /// 那一整套 <c>IUnitAccess</c>/<c>IStatHost</c> 等依赖。</summary>
        public sealed class FakeExprHostFactory : IExprHostFactory
        {
            public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new FakeHost(triggeringEvent);

            private sealed class FakeHost : IExprHost
            {
                private readonly IEvent? _evt;

                public FakeHost(IEvent? evt) => _evt = evt;

                public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args)
                {
                    if (group == ExprGroups.Event && _evt is IExprReadableEvent readable && readable.TryGetField(key, out var value))
                    {
                        return value;
                    }
                    return ExprValue.OfBool(false);
                }
            }
        }

        public static (IDataRegistry Registry, ValidationReport Report) BuildRegistry(IReadOnlyDictionary<string, string> tables)
        {
            var source = new InMemoryDataSource();
            foreach (var kv in tables)
            {
                source.Add(kv.Key, Envelope(kv.Key, kv.Value));
            }

            var registry = new Core.Foundation.DataRegistry.DataRegistry(
                source, CreateBus(), new DataRegistryOptions { ExprSchema = RulesExprSchema.Base });

            registry.RegisterSchema(FeedbackSchemas.Binding);
            registry.RegisterSchema(FeedbackSchemas.FloatingTextStyle);

            var report = registry.LoadAll();
            return (registry, report);
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        // 判断记录（Expr 词法与事件字段命名约定的契约缺口，见 feedback_binder/README.md）：
        // Core.Foundation.Expr.ExprLexer 的标识符只接受全小写 a-z/0-9/_（IsIdentBodyChar，同
        // Id 格式），而 IExprReadableEvent 约定事件字段名为 camelCase（如 CombatDamageDealtEvent
        // 的 "isCrit"/"sourceId"）——含大写字母的字段名一律无法出现在 event.<field> 引用里
        // （词法阶段直接报"非法字符"）。这是 Expr 模块（不属于本任务契约范围）与事件字段命名约定
        // 之间的既有不一致，本任务不修改 core/foundation/expr；这里改用天然全小写的 amount 字段做
        // 数值阈值分流演示同一事件按条件路由到不同规则（09 第 6.1 节原文示例的 is_crit 分流意图
        // 不变，只是受限于当前 Expr 词法用可解析的字段代替）。
        public const string CritDamageRuleRow = @"
        {
          ""id"": ""feedback.crit_damage_text"",
          ""event"": ""combat.damage_dealt"",
          ""condition"": ""event.amount >= 20"",
          ""actions"": [
            {""kind"": ""floating_text"", ""params"": {""style_id"": ""feedback.style.crit"", ""text_source"": ""amount""}},
            {""kind"": ""shake_camera"", ""params"": {""profile_id"": ""feedback.shake.crit""}}
          ]
        }";

        public const string NormalDamageRuleRow = @"
        {
          ""id"": ""feedback.normal_damage_text"",
          ""event"": ""combat.damage_dealt"",
          ""condition"": ""event.amount < 20"",
          ""actions"": [
            {""kind"": ""floating_text"", ""params"": {""style_id"": ""feedback.style.normal"", ""text_source"": ""amount""}}
          ]
        }";

        public const string AuraAppliedRuleRow = @"
        {
          ""id"": ""feedback.aura_applied_vfx"",
          ""event"": ""aura.applied"",
          ""actions"": [
            {""kind"": ""play_vfx"", ""params"": {""from_display"": ""skill"", ""attach"": ""target""}},
            {""kind"": ""play_sfx"", ""params"": {""sfx_id"": ""sfx.buff_apply""}},
            {""kind"": ""flash"", ""params"": {""profile_id"": ""feedback.flash.buff"", ""target"": ""target""}},
            {""kind"": ""freeze"", ""params"": {""duration_ms"": 40}}
          ]
        }";

        public const string CritStyleRow = @"
        {
          ""id"": ""feedback.style.crit"",
          ""color_ref"": ""color.crit_yellow"",
          ""size_scale"": 1.4,
          ""motion_profile"": ""motion.pop_up""
        }";
    }
}
