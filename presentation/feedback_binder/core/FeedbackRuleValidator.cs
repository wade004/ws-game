using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Presentation.FeedbackBinder.Contracts;

namespace Presentation.FeedbackBinder.Core
{
    /// <summary>
    /// <c>feedback.binding</c> 规则集的内容校验（见 04_数据与内容管线.md 第 5 节校验器检查项
    /// 清单惯例："event 已登记（EventKeys.All）、action kind 合法、params 必填"，见本任务书）。
    /// <para>
    /// 判断记录（"action kind 合法""params 必填"两项的落地方式）：<see cref="FeedbackAction"/>
    /// 是强类型判别联合（见该类型注释），非法 kind 或缺失必填 params 在
    /// <see cref="FeedbackRule.FromRecord"/> 解析期就会抛 <see cref="DataFieldException"/>——
    /// 换句话说，一条 <see cref="FeedbackRule"/> 只要能被成功构造出来，这两项检查天然已经通过；
    /// 本类型只补运行期/跨记录才能判断的一项：<c>event</c> 字段必须是已登记的事件 key（构造单条
    /// 记录时不知道全局事件目录，需要调用方传入）。同时补两条不依赖事件目录、但同样"跨记录"
    /// 才能判断的检查（id 必须落在 <c>feedback</c> domain、id 不得在规则集内重复），凑齐"内容
    /// 提交门槛"的完整性。
    /// </para>
    /// </summary>
    public static class FeedbackRuleValidator
    {
        public const string CheckEventRegistered = "feedback_event_registered";
        public const string CheckIdDomain = "feedback_id_domain";
        public const string CheckDuplicateId = "feedback_duplicate_id";

        public static IReadOnlyList<ValidationIssue> Validate(
            IReadOnlyList<FeedbackRule> rules,
            IReadOnlyCollection<Id> knownEventKeys)
        {
            var issues = new List<ValidationIssue>();
            var seenIds = new HashSet<Id>();
            var knownSet = new HashSet<Id>(knownEventKeys);

            foreach (var rule in rules)
            {
                if (rule.Id.Domain != "feedback")
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, "feedback.binding", CheckIdDomain,
                        $"id \"{rule.Id}\" 的 domain 必须是 \"feedback\"", recordKey: rule.Id.ToString()));
                }

                if (!seenIds.Add(rule.Id))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, "feedback.binding", CheckDuplicateId,
                        $"id \"{rule.Id}\" 在规则集内重复登记", recordKey: rule.Id.ToString()));
                }

                if (!knownSet.Contains(rule.EventKey))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, "feedback.binding", CheckEventRegistered,
                        $"event \"{rule.EventKey}\" 未在事件词汇表（found.event_catalog / EventKeys.All）登记",
                        recordKey: rule.Id.ToString(), field: "event"));
                }
            }

            return issues;
        }
    }
}
