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
                    if (group == ExprGroups.Event && _evt is IExprReadableEvent readable)
                    {
                        // 同 Core.Rules.ExprHost.RulesExprHostFactory.Host.QueryEvent 的
                        // "先原样查找，查不到再转 camelCase 重试"惯例（P4-3），这里是精简版复刻——
                        // 本 Fake 只服务 event.* 求值，没有理由让"字段名映射"这条规则只在生产实现里
                        // 生效、测试夹具里失效。
                        if (readable.TryGetField(key, out var value))
                        {
                            return value;
                        }

                        var camelCaseKey = SnakeCaseToCamelCase(key);
                        if (camelCaseKey != key && readable.TryGetField(camelCaseKey, out var convertedValue))
                        {
                            return convertedValue;
                        }
                    }
                    return ExprValue.OfBool(false);
                }

                private static string SnakeCaseToCamelCase(string snakeCase)
                {
                    if (string.IsNullOrEmpty(snakeCase) || snakeCase.IndexOf('_') < 0)
                    {
                        return snakeCase;
                    }

                    var parts = snakeCase.Split('_');
                    var sb = new System.Text.StringBuilder(snakeCase.Length);
                    var isFirstSegment = true;
                    foreach (var part in parts)
                    {
                        if (part.Length == 0) continue;
                        if (isFirstSegment)
                        {
                            sb.Append(part);
                            isFirstSegment = false;
                        }
                        else
                        {
                            sb.Append(char.ToUpperInvariant(part[0]));
                            if (part.Length > 1) sb.Append(part, 1, part.Length - 1);
                        }
                    }
                    return sb.ToString();
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

        // 判断记录（更新，P4-3 契约缺口已修补，见 feedback_binder/README.md、
        // core/rules/expr_host/RulesExprHostFactory.cs 类型 remarks"event.<field> 字段名映射"）：
        // 本判断记录曾记录"Core.Foundation.Expr.ExprLexer 的标识符只接受全小写 a-z/0-9/_，而
        // IExprReadableEvent 约定事件字段名为 camelCase（如 CombatDamageDealtEvent 的
        // "isCrit"/"sourceId"），含大写字母的字段名一律无法出现在 event.<field> 引用里"这一契约
        // 缺口，并因此改用天然全小写的 amount 字段做数值阈值分流演示（而非 09 第 6.1 节原文示例的
        // is_crit 分流）。P4-3 已在 RulesExprHostFactory.Host.QueryEvent 补上"原样查找失败时转
        // camelCase 重试"的映射，event.is_crit 一类 snake_case 引用现在可以直接命中
        // IExprReadableEvent 登记的 "isCrit" 字段——下面两条规则改回按 09 原文示例用
        // event.is_crit 分流（保留 amount 字段供飘字文本取值），验证契约缺口已修补；见
        // FeedbackBinderTests.OnEvent_ConditionTrue_DispatchesFloatingTextAndShakeCamera/
        // OnEvent_ConditionFalse_RoutesToTheOtherRule（两条既有测试的事件夹具本就分别传
        // isCrit: true/false，规则条件文本改用 event.is_crit 后行为不变，只是不再受词法限制）。
        public const string CritDamageRuleRow = @"
        {
          ""id"": ""feedback.crit_damage_text"",
          ""event"": ""combat.damage_dealt"",
          ""condition"": ""event.is_crit"",
          ""actions"": [
            {""kind"": ""floating_text"", ""params"": {""style_id"": ""feedback.style.crit"", ""text_source"": ""amount""}},
            {""kind"": ""shake_camera"", ""params"": {""profile_id"": ""feedback.shake.crit""}}
          ]
        }";

        public const string NormalDamageRuleRow = @"
        {
          ""id"": ""feedback.normal_damage_text"",
          ""event"": ""combat.damage_dealt"",
          ""condition"": ""not event.is_crit"",
          ""actions"": [
            {""kind"": ""floating_text"", ""params"": {""style_id"": ""feedback.style.normal"", ""text_source"": ""amount""}}
          ]
        }";

        /// <summary>N17 测试专用：只含一个 play_sfx 动作（不带 floating_text，避免 <see
        /// cref="Presentation.FeedbackBinder.Contracts.FeedbackOptions.MergeWindow"/> 引入的合并窗口
        /// 干扰"队列清空但 sink 仍 pending"这一场景的断言）。</summary>
        public const string PlaySfxOnlyRuleRow = @"
        {
          ""id"": ""feedback.sfx_only"",
          ""event"": ""combat.damage_dealt"",
          ""actions"": [
            {""kind"": ""play_sfx"", ""params"": {""sfx_id"": ""sfx.sample_cold""}}
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

        // 判断记录：09 第 5.6 节"逻辑 id"特指技能/光环/物品/生物模板 id，from_display: target 需要
        // "运行期实体 id → 模板 id"这层映射（见 FeedbackBinder.ResolveEntityLogicalId 判断记录，
        // P4-2 起默认经 IUnitAccess.GetTemplateId 提供）。
        public const string TargetVfxFromDisplayRuleRow = @"
        {
          ""id"": ""feedback.target_vfx_from_display"",
          ""event"": ""combat.damage_dealt"",
          ""actions"": [
            {""kind"": ""play_vfx"", ""params"": {""from_display"": ""target"", ""attach"": ""target""}}
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
