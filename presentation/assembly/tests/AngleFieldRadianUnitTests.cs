using System.Collections.Generic;
using System.Text.RegularExpressions;
using Core.Foundation.DataRegistry;
using Presentation.Assembly;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 消费方反馈第 61 条（2026-09-18）验收测试：全仓库角度语义字段是否都已登记
    /// <see cref="FieldUnit.Radian"/>——04/05 文档"单位约定"只是人工可读的集中声明，本测试是配套的
    /// 机器可读回归防线，防止后续新增角度字段遗漏 <see cref="FieldSchema.WithUnit"/> 登记。
    /// <para>
    /// 判断记录（遍历范围）：对 <see cref="SchemaAudit.EnumerateRegisteredSchemas"/>（复用
    /// <see cref="ContentValidationAssembly.CreateRegistry"/> 同一份只登记 schema、不加载数据的装配，
    /// 见该方法判断记录）给出的全部已登记 <see cref="TableSchema"/> 递归遍历 <see cref="FieldSchema.Fields"/>/
    /// <see cref="FieldSchema.Item"/>/<see cref="FieldSchema.Variants"/>/<see cref="FieldSchema.Map"/>
    /// 四种子结构，与 <see cref="SchemaFieldRangeExport.Walk"/> 同一套"祖先链按对象引用比较"防环策略
    /// （字段路径记法不必与其它导出一致——本测试只用于人类可读的失败消息，不对外输出）。
    /// </para>
    /// <para>
    /// 判断记录（匹配范围：只查 <see cref="FieldKind.Number"/>）：<see cref="FieldKind.Id"/>/
    /// <see cref="FieldKind.Enum"/>/<see cref="FieldKind.Object"/> 等种类即便字段名命中关键字（如
    /// <c>ai_rotation_override</c>/<c>direction_count</c>/<c>rotation_ref</c> 一类引用/计数/枚举字段），
    /// 也不是"以弧度为单位的连续角度值"，不在本条回归覆盖范围内——这也是允许例外清单初始为空的原因：
    /// 全仓库当前唯一一批"字段名命中关键字 且 FieldKind.Number"的字段就是 61 条已知清单那十个，均已
    /// 补齐登记。
    /// </para>
    /// </summary>
    public class AngleFieldRadianUnitTests
    {
        /// <summary>字段名命中即视为"角度语义字段"，大小写不敏感（同消费方反馈原文列出的关键字集合，
        /// 不含 <c>arc</c>——<c>arc_height</c> 一类字段是长度而非角度，见类型顶部判断记录、
        /// <c>SkillSchemas.EffectsItemSchema</c> 的 <c>arc_height</c> 判断记录）。</summary>
        private static readonly Regex AngleFieldNamePattern = new Regex(
            "angle|rotation|facing|heading", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>显式例外清单：字段名命中关键字、种类是 Number，但确认不是角度语义、刻意不登记
        /// Radian 的字段——"表名.字段路径" 形式。初始为空：全仓库当前没有这样的字段（见类型顶部
        /// 判断记录）。新增例外前必须先确认该字段确实不是角度语义，并在这里写明理由。</summary>
        private static readonly HashSet<string> Exceptions = new HashSet<string>();

        [Fact]
        public void AllAngleNamedNumberFields_AreRegisteredAsRadian()
        {
            var schemas = SchemaAudit.EnumerateRegisteredSchemas();
            var violations = new List<string>();

            foreach (var schema in schemas)
            {
                if (schema.IsUnschematized)
                {
                    continue;
                }

                var ancestors = new List<FieldSchema>();
                foreach (var field in schema.Fields)
                {
                    Walk(schema.Name, field, field.Name, depth: 0, violations, ancestors);
                }
            }

            Assert.True(violations.Count == 0,
                "以下字段名命中 angle|rotation|facing|heading 且种类为 Number，但未登记 FieldUnit.Radian" +
                "（消费方反馈第 61 条；确认不是角度语义可加进 AngleFieldRadianUnitTests.Exceptions 并写明理由）：\n" +
                string.Join("\n", violations));
        }

        private const int MaxDepth = 32;

        private static void Walk(
            string tableName, FieldSchema field, string path, int depth,
            List<string> violations, List<FieldSchema> ancestors)
        {
            if (ancestors.Contains(field))
            {
                return; // 自引用环，同 SchemaFieldRangeExport.Walk 判断记录。
            }

            if (field.Kind == FieldKind.Number && AngleFieldNamePattern.IsMatch(field.Name))
            {
                var key = $"{tableName}.{path}";
                if (field.Unit != FieldUnit.Radian && !Exceptions.Contains(key))
                {
                    violations.Add(key);
                }
            }

            if (depth >= MaxDepth)
            {
                return;
            }

            if (field.Kind != FieldKind.Object && field.Kind != FieldKind.Array)
            {
                return;
            }

            ancestors.Add(field);
            try
            {
                if (field.Kind == FieldKind.Object)
                {
                    var map = field.Map;
                    var variants = field.Variants;
                    var subFields = field.Fields;

                    if (map != null)
                    {
                        Walk(tableName, map.ValueSchema, path + "[*]", depth + 1, violations, ancestors);
                    }
                    else if (variants != null)
                    {
                        foreach (var kv in variants.Cases)
                        {
                            var caseFields = kv.Value;
                            if (caseFields == null)
                            {
                                continue;
                            }

                            var casePath = path + "{" + variants.Discriminator + "=" + kv.Key + "}";
                            for (var i = 0; i < caseFields.Count; i++)
                            {
                                Walk(tableName, caseFields[i], casePath + "." + caseFields[i].Name, depth + 1, violations, ancestors);
                            }
                        }

                        var commonFields = variants.CommonFields;
                        if (commonFields != null)
                        {
                            for (var i = 0; i < commonFields.Count; i++)
                            {
                                Walk(tableName, commonFields[i], path + "." + commonFields[i].Name, depth + 1, violations, ancestors);
                            }
                        }
                    }
                    else if (subFields != null)
                    {
                        for (var i = 0; i < subFields.Count; i++)
                        {
                            Walk(tableName, subFields[i], path + "." + subFields[i].Name, depth + 1, violations, ancestors);
                        }
                    }
                }
                else
                {
                    var item = field.Item;
                    if (item != null)
                    {
                        Walk(tableName, item, path + "[]", depth + 1, violations, ancestors);
                    }
                }
            }
            finally
            {
                ancestors.RemoveAt(ancestors.Count - 1);
            }
        }
    }
}
