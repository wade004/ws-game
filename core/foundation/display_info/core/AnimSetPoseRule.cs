using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// "姿势集符合姿势契约"检查项（手感设计/04 第 3、7、8 节，ADR-0119）：
    /// <list type="number">
    /// <item><b>键语法</b>（警告，仅对选入清单的姿势集）：<c>clips</c> 的键须为点分小写字母/数字/下划线（<see cref="PoseKeys.IsWellFormed"/>）。
    /// 既有数据里的自由键名不受影响：默认严格级别下警告也会阻断加载，所以对未选入的姿势集不检查键语法。</item>
    /// <item><b>继承</b>（错误，对所有姿势集）：<c>extends</c> 指向自己或成环。指向不存在的记录由 <c>extends</c> 字段的引用完整性检查负责，这里不重复。</item>
    /// <item><b>标准姿势清单</b>：仅对"按清单发布"的姿势集（<c>pose_standard: true</c>，或框架级 <c>display.anim_set.std_*</c>，见
    /// <see cref="PoseChecklist.AppliesTo"/>）——沿继承链合并后，必备键缺失为<b>错误</b>、推荐键缺失为<b>警告</b>（消息里写明运行期回落到哪个键）；
    /// 可选键缺失静默。</item>
    /// </list>
    /// <para>
    /// 判断记录：清单检查是<b>选入制</b>——既有的姿势集（只有 7 个状态键、无 <c>move.walk/run</c>）不声明 <c>pose_standard</c>
    /// 就完全不受影响（"缺省行为不变"，且默认数据根/模板数据根要求零警告）；要发布成标准姿势库的集（框架级 <c>std_</c> 集自动、
    /// 游戏级集显式声明）才付出"必备键齐全"的约束，对应 04 第 8 节"缺必备键拒绝发布"。
    /// </para>
    /// </summary>
    public sealed class AnimSetPoseRule : IValidationRule
    {
        public const string CheckKeySyntax = "anim_set_key_syntax";
        public const string CheckExtends = "anim_set_extends";
        public const string CheckRequired = "anim_set_pose_required";
        public const string CheckRecommended = "anim_set_pose_recommended";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var records = view.GetAll(DisplaySchemas.AnimSet.Name);
            foreach (var record in records)
            {
                var cycle = FindExtendsProblem(view, record);
                if (cycle != null)
                {
                    yield return Issue(ValidationSeverity.Error, CheckExtends, record, cycle, "extends");
                    continue; // 继承有问题时合并结果不可信，不再叠加清单检查
                }

                var flag = record.TryGetBool("pose_standard", out var f) && f;
                if (!PoseChecklist.AppliesTo(record.Key, flag))
                {
                    continue;
                }

                if (record.TryGetObject("clips", out var clips))
                {
                    foreach (var entry in clips)
                    {
                        if (!PoseKeys.IsWellFormed(entry.Key))
                        {
                            yield return Issue(ValidationSeverity.Warning, CheckKeySyntax, record,
                                $"剪辑键 \"{entry.Key}\" 不符合姿势键语法（点分小写字母/数字/下划线，见 04 第 2.1 节）", "clips");
                        }
                    }
                }

                var keys = CollectMergedKeys(view, record);
                var report = PoseChecklist.Evaluate(keys);
                foreach (var finding in report.MissingRequired)
                {
                    yield return Issue(ValidationSeverity.Error, CheckRequired, record, finding.Describe(), "clips");
                }
                foreach (var finding in report.MissingRecommended)
                {
                    yield return Issue(ValidationSeverity.Warning, CheckRecommended, record, finding.Describe(), "clips");
                }
            }
        }

        /// <summary>沿 extends 链合并后的全部声明键（含祖先）；调用前已确认无环。</summary>
        internal static IReadOnlyList<string> CollectMergedKeys(IDataRegistryView view, DataRecord record)
        {
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cursor = record;
            var guard = new HashSet<string>(StringComparer.Ordinal);
            while (cursor != null && guard.Add(cursor.Key))
            {
                if (cursor.TryGetObject("clips", out var clips))
                {
                    foreach (var entry in clips)
                    {
                        if (seen.Add(entry.Key)) keys.Add(entry.Key);
                    }
                }
                cursor = cursor.TryGetId("extends", out var parentRef) ? view.Get(record.Table.Name, parentRef) : null;
            }
            return keys;
        }

        private static string? FindExtendsProblem(IDataRegistryView view, DataRecord record)
        {
            if (!record.TryGetId("extends", out var first))
            {
                return null;
            }

            var visited = new List<string> { record.Key };
            var cursor = record;
            var next = first;
            while (true)
            {
                if (string.Equals(next.Value, record.Key, StringComparison.Ordinal))
                {
                    return visited.Count == 1
                        ? "姿势集不能 extends 自己"
                        : $"姿势集继承成环：{string.Join(" -> ", visited)} -> {next.Value}";
                }

                var parent = view.Get(record.Table.Name, next);
                if (parent == null)
                {
                    return null; // 不存在的父集由引用完整性检查报告
                }
                if (visited.Contains(parent.Key))
                {
                    // 环在祖先之间（不经过本记录）：由环上的那条记录自己报告，这里不重复
                    return null;
                }
                visited.Add(parent.Key);
                cursor = parent;
                if (!cursor.TryGetId("extends", out next))
                {
                    return null;
                }
            }
        }

        private static ValidationIssue Issue(ValidationSeverity severity, string check, DataRecord record, string message, string field) =>
            new ValidationIssue(severity, DisplaySchemas.AnimSet.Name, check, message, recordKey: record.Key, field: field);
    }
}
