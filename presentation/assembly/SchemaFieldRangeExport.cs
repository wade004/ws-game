using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Presentation.Assembly
{
    /// <summary>一条已登记的数值范围命中：<see cref="FieldPath"/> 记法与
    /// <see cref="SchemaAuditIssue.FieldPath"/> 完全一致（顶层字段即字段名本身，递归进入子结构后
    /// <c>Object.Fields</c>/<c>Variants.CommonFields</c> 追加 <c>".子字段名"</c>，<c>Array.Item</c>
    /// 追加 <c>"[]"</c>，<c>Variants.Cases[case]</c> 追加 <c>"{判别字段=case}"</c> 再追加
    /// <c>".子字段名"</c>）。</summary>
    public sealed class FieldRangeInfo
    {
        public string FieldPath { get; }

        /// <summary><c>"Number"</c> 或 <c>"Int"</c>（<see cref="FieldKind"/> 的字符串形式）。</summary>
        public string Kind { get; }

        public FieldRange Range { get; }

        public FieldRangeInfo(string fieldPath, string kind, FieldRange range)
        {
            FieldPath = fieldPath ?? throw new ArgumentNullException(nameof(fieldPath));
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            Range = range ?? throw new ArgumentNullException(nameof(range));
        }
    }

    /// <summary>
    /// ADR-0021（04 第 4 节勘误"范围约束"）决策 4："导出给内容工具"：从已登记的
    /// <see cref="TableSchema"/> 里递归收集全部挂了 <see cref="FieldSchema.Range"/> 的
    /// Number/Int 字段，供 <c>toolchain/validator --list-tables --json</c> 与编辑器基础套件做
    /// 输入侧校验（不必等到 <c>DataRegistry.LoadAll</c> 才发现越界）。
    /// <para>
    /// 判断记录：递归遍历结构（子结构展开、自引用环检测）刻意与 <see cref="SchemaAudit.Run"/> 的
    /// <c>WalkField</c> 保持同一套记法与同一套"祖先链按对象引用比较"防环策略——两者都是对同一份
    /// 静态 schema 图的只读遍历，字段路径记法必须一致，否则编辑器/文档对照两处输出时会对不上号
    /// （见 <see cref="FieldRangeInfo.FieldPath"/>）。不复用同一个私有方法是因为
    /// <c>SchemaAudit.WalkField</c> 的职责是"边走边报审计问题"，混入"顺便收集 Range"会让那个已经
    /// 很长的方法承担第二个不相关职责；本类型只做"收集"，不产生任何 <see cref="SchemaAuditIssue"/>，
    /// 也不依赖白名单，是两个正交的只读消费者。
    /// </para>
    /// </summary>
    public static class SchemaFieldRangeExport
    {
        /// <summary>递归深度上限，与 <see cref="SchemaAudit"/>/<c>DataRegistry.MaxSubstructureDepth</c>
        /// 取值一致，理由同源（防御自引用 schema 图的无限递归）。</summary>
        private const int MaxDepth = 32;

        /// <summary>按 <see cref="TableSchema.Fields"/> 出现顺序、深度优先收集本表全部挂了 Range 的
        /// Number/Int 字段。未登记 Range 的字段不出现；<see cref="TableSchema.IsUnschematized"/> 的表
        /// 返回空列表（没有字段结构可遍历）。</summary>
        public static IReadOnlyList<FieldRangeInfo> Collect(TableSchema schema)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));

            var result = new List<FieldRangeInfo>();
            if (schema.IsUnschematized) return result;

            var ancestors = new List<FieldSchema>();
            var fields = schema.Fields;
            for (var i = 0; i < fields.Count; i++)
            {
                Walk(fields[i], fields[i].Name, depth: 0, result, ancestors);
            }
            return result;
        }

        private static void Walk(FieldSchema field, string path, int depth, List<FieldRangeInfo> result, List<FieldSchema> ancestors)
        {
            if (ancestors.Contains(field)) return; // 自引用环，同 SchemaAudit.WalkField 判断记录。

            if (field.Range != null && (field.Kind == FieldKind.Number || field.Kind == FieldKind.Int))
            {
                result.Add(new FieldRangeInfo(path, field.Kind.ToString(), field.Range));
            }

            if (depth >= MaxDepth) return;
            if (field.Kind != FieldKind.Object && field.Kind != FieldKind.Array) return;

            ancestors.Add(field);
            try
            {
                if (field.Kind == FieldKind.Object)
                {
                    var variants = field.Variants;
                    var subFields = field.Fields;
                    var map = field.Map;

                    if (map != null)
                    {
                        // ADR-0024（04 第 3.3 节"映射登记"）：映射值按 MapSchema.ValueSchema 递归，
                        // 路径记法与 DataRegistry.ValidateMapObject 一致用 "[*]" 表示"任意键"（本类型
                        // 走的是静态 schema 图，没有具体数据键可用，同 Array.Item 用 "[]" 的既有惯例）。
                        Walk(map.ValueSchema, path + "[*]", depth + 1, result, ancestors);
                    }
                    else if (variants != null)
                    {
                        var commonFields = variants.CommonFields;
                        foreach (var kv in variants.Cases)
                        {
                            var caseFields = kv.Value;
                            if (caseFields == null) continue;
                            var casePath = path + "{" + variants.Discriminator + "=" + kv.Key + "}";
                            for (var i = 0; i < caseFields.Count; i++)
                            {
                                Walk(caseFields[i], casePath + "." + caseFields[i].Name, depth + 1, result, ancestors);
                            }
                        }

                        if (commonFields != null)
                        {
                            for (var i = 0; i < commonFields.Count; i++)
                            {
                                Walk(commonFields[i], path + "." + commonFields[i].Name, depth + 1, result, ancestors);
                            }
                        }
                    }
                    else if (subFields != null)
                    {
                        for (var i = 0; i < subFields.Count; i++)
                        {
                            Walk(subFields[i], path + "." + subFields[i].Name, depth + 1, result, ancestors);
                        }
                    }
                }
                else
                {
                    var item = field.Item;
                    if (item != null)
                    {
                        Walk(item, path + "[]", depth + 1, result, ancestors);
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
