using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// "<c>display.anim_set.blends</c> 声明可解析"检查项（手感落地 M4-D）：<c>blends</c> 的 <c>from</c>/<c>to</c> 必须是本行合并继承链后
    /// <c>clips</c> 里存在的键（按规范键判同），同一对不重复声明。形状错误（缺字段、类型不对、越界）由 schema 登记的字段类型与
    /// <c>Range</c> 报告；本规则只做 schema 表达不了的"跨字段引用"检查，严重级别为 Warning——悬空的对在运行期被
    /// <see cref="AnimSetDef.TryGetBlendSeconds"/> 忽略（回落逐键值/默认），不影响播放，只是声明白写了。
    /// </summary>
    public sealed class AnimSetBlendRule : IValidationRule
    {
        public const string CheckDangling = "anim_set_blend_dangling";
        public const string CheckDuplicate = "anim_set_blend_duplicate";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(DisplaySchemas.AnimSet.Name))
            {
                if (!record.TryGetArray("blends", out var arr) || arr.Count == 0)
                {
                    continue;
                }

                AnimSetDef def;
                try
                {
                    def = AnimSetDef.FromRecord(record, view);
                }
                catch (DataFieldException)
                {
                    continue; // 形状/继承问题由 schema 与 AnimSetPoseRule 报告，这里不叠加
                }

                var known = new HashSet<string>(StringComparer.Ordinal);
                foreach (var key in def.Clips.Keys)
                {
                    known.Add(PoseKeys.Canonicalize(key));
                }

                foreach (var pair in def.Blends)
                {
                    if (!known.Contains(PoseKeys.Canonicalize(pair.FromKey)) || !known.Contains(PoseKeys.Canonicalize(pair.ToKey)))
                    {
                        yield return Issue(record, CheckDangling,
                            $"blends 的 {pair.FromKey} -> {pair.ToKey} 引用了 clips 里不存在的键（运行期忽略这一对）");
                    }
                }

                // 同一行内重复声明同一对（合并时后者覆盖前者，前者白写）：直接看本行的原始数组。
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is Core.Foundation.Common.Json.JsonObject o
                        && o.TryGetValue("from", out var f) && f is Core.Foundation.Common.Json.JsonString fs
                        && o.TryGetValue("to", out var t) && t is Core.Foundation.Common.Json.JsonString ts
                        && !seen.Add(PoseKeys.Canonicalize(fs.Value) + "" + PoseKeys.Canonicalize(ts.Value)))
                    {
                        yield return Issue(record, CheckDuplicate, $"blends 重复声明了 {fs.Value} -> {ts.Value}");
                    }
                }
            }
        }

        private static ValidationIssue Issue(DataRecord record, string check, string message) => new ValidationIssue(
            ValidationSeverity.Warning, DisplaySchemas.AnimSet.Name, check, message, recordKey: record.Key, field: "blends");
    }
}
