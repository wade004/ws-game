using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Presentation.Assembly
{
    /// <summary>一条已登记的元素数量约束命中：<see cref="FieldPath"/> 记法与
    /// <see cref="SchemaFieldRangeExport"/>/<see cref="SchemaFieldDeprecationExport"/>/
    /// <see cref="SchemaAuditIssue.FieldPath"/> 完全一致（顶层字段即字段名本身，递归进入子结构后
    /// <c>Object.Fields</c>/<c>Variants.CommonFields</c> 追加 <c>".子字段名"</c>，<c>Array.Item</c>
    /// 追加 <c>"[]"</c>，<c>Object.Map</c> 值追加 <c>"[*]"</c>，<c>Variants.Cases[case]</c> 追加
    /// <c>"{判别字段=case}"</c> 再追加 <c>".子字段名"</c>）。</summary>
    public sealed class FieldItemCountInfo
    {
        public string FieldPath { get; }

        /// <summary><c>"IdList"</c> 或 <c>"Array"</c>（<see cref="FieldKind"/> 的字符串形式——
        /// <see cref="FieldSchema.WithItemCount"/> 只允许挂在这两种种类上，见该方法判断记录）。</summary>
        public string Kind { get; }

        public int? MinItems { get; }

        public int? MaxItems { get; }

        public FieldItemCountInfo(string fieldPath, string kind, int? minItems, int? maxItems)
        {
            FieldPath = fieldPath ?? throw new ArgumentNullException(nameof(fieldPath));
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            MinItems = minItems;
            MaxItems = maxItems;
        }
    }

    /// <summary>
    /// 消费方反馈第 60 条（04 第 4 节勘误"元素数量约束"）："导出给内容工具"：从已登记的
    /// <see cref="TableSchema"/> 里递归收集全部挂了 <see cref="FieldSchema.MinItems"/>/
    /// <see cref="FieldSchema.MaxItems"/> 的 IdList/Array 字段，供 <c>toolchain/validator
    /// --list-tables --json</c> 与编辑器基础套件做输入侧校验（不必等到 <c>DataRegistry.LoadAll</c>
    /// 才发现元素数不足/超出）。
    /// <para>
    /// 判断记录：与 <see cref="SchemaFieldRangeExport"/>/<see cref="SchemaFieldDeprecationExport"/>
    /// 同一套记法、同一套"祖先链按对象引用比较"防环策略——三者都是对同一份静态 schema 图的只读遍历，
    /// 只是收集条件不同（本类型只收集 <see cref="FieldSchema.MinItems"/>/<see cref="FieldSchema.MaxItems"/>
    /// 任一非 null 的字段）。不复用同一个私有 Walk 方法，理由同 <see cref="SchemaFieldRangeExport"/>
    /// 类型顶部判断记录"不复用 SchemaAudit.WalkField"：各类型收集的目标与命中条件都不同，硬凑到一起
    /// 反而让每一处都多出一堆与自己无关的分支判断。
    /// </para>
    /// </summary>
    public static class SchemaFieldItemCountExport
    {
        /// <summary>递归深度上限，与 <see cref="SchemaFieldRangeExport"/>/<see cref="SchemaFieldDeprecationExport"/>/
        /// <see cref="SchemaAudit"/>/<c>DataRegistry.MaxSubstructureDepth</c> 取值一致，理由同源（防御
        /// 自引用 schema 图的无限递归）。</summary>
        private const int MaxDepth = 32;

        /// <summary>按 <see cref="TableSchema.Fields"/> 出现顺序、深度优先收集本表全部挂了
        /// <see cref="FieldSchema.MinItems"/>/<see cref="FieldSchema.MaxItems"/> 的 IdList/Array 字段
        /// （含顶层与任意深度嵌套）。未登记的字段不出现；<see cref="TableSchema.IsUnschematized"/> 的表
        /// 返回空列表（没有字段结构可遍历）。</summary>
        public static IReadOnlyList<FieldItemCountInfo> Collect(TableSchema schema)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));

            var result = new List<FieldItemCountInfo>();
            if (schema.IsUnschematized) return result;

            var ancestors = new List<FieldSchema>();
            var fields = schema.Fields;
            for (var i = 0; i < fields.Count; i++)
            {
                Walk(fields[i], fields[i].Name, depth: 0, result, ancestors);
            }
            return result;
        }

        private static void Walk(FieldSchema field, string path, int depth, List<FieldItemCountInfo> result, List<FieldSchema> ancestors)
        {
            if (ancestors.Contains(field)) return; // 自引用环，同 SchemaFieldRangeExport.Walk 判断记录。

            if (field.MinItems.HasValue || field.MaxItems.HasValue)
            {
                result.Add(new FieldItemCountInfo(path, field.Kind.ToString(), field.MinItems, field.MaxItems));
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
                        // 路径记法与 DataRegistry.ValidateMapObject/SchemaFieldRangeExport 一致用
                        // "[*]" 表示"任意键"（本类型走的是静态 schema 图，没有具体数据键可用）。
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
