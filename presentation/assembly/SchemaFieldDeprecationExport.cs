using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Presentation.Assembly
{
    /// <summary>一条已登记的字段废弃元数据命中：<see cref="FieldPath"/> 记法与
    /// <see cref="SchemaFieldRangeExport"/>/<see cref="SchemaAuditIssue.FieldPath"/> 完全一致（顶层
    /// 字段即字段名本身，递归进入子结构后 <c>Object.Fields</c>/<c>Variants.CommonFields</c> 追加
    /// <c>".子字段名"</c>，<c>Array.Item</c> 追加 <c>"[]"</c>，<c>Object.Map</c> 值追加 <c>"[*]"</c>，
    /// <c>Variants.Cases[case]</c> 追加 <c>"{判别字段=case}"</c> 再追加 <c>".子字段名"</c>）。</summary>
    public sealed class DeprecatedFieldInfo
    {
        public string FieldPath { get; }

        /// <summary>见 <see cref="FieldSchema.DeprecatedSince"/>，<c>"X.Y.Z"</c> 形式，非空。</summary>
        public string Since { get; }

        /// <summary>见 <see cref="FieldSchema.ReplacedBy"/>；<c>null</c> 表示废弃后无同表替代字段。
        /// 判断记录：这里存的是登记时的兄弟字段名原样（同 <c>AppendFieldMetaJson</c> 里 "deprecated"
        /// 键 "replaced_by" 子键的既有语义），不解析成完整路径——ReplacedBy 按登记约定只能指向同一
        /// 级兄弟字段（见 <see cref="FieldSchema.WithDeprecated"/> 判断记录），消费方按需自行与
        /// <see cref="FieldPath"/> 的父路径拼接。</summary>
        public string? ReplacedBy { get; }

        /// <summary>见 <see cref="FieldSchema.DeprecationNote"/>；可为 <c>null</c>。</summary>
        public string? Note { get; }

        public DeprecatedFieldInfo(string fieldPath, string since, string? replacedBy, string? note)
        {
            FieldPath = fieldPath ?? throw new ArgumentNullException(nameof(fieldPath));
            Since = since ?? throw new ArgumentNullException(nameof(since));
            ReplacedBy = replacedBy;
            Note = note;
        }
    }

    /// <summary>
    /// 消费方反馈第 46 条收口（验收报告"必须修项"根治，1.38.0）：<c>toolchain/validator
    /// --list-tables --json</c> 此前的 <c>field_meta.deprecated</c> 只覆盖顶层字段（04 第 3.4 节明文
    /// 限定），嵌套在 <c>Fields</c>/<c>Item</c>/<c>Map</c> 值/<c>Variants</c> 分支内的废弃字段
    /// （如 <c>quest.def.rewards.xp</c>、<c>skill.def</c> 效果参数 <c>scaling_stat</c>/
    /// <c>coefficient</c>）虽然 <see cref="FieldSchema.IsDeprecated"/> 已正确登记，CLI 导出路径本身
    /// 却吐不出来——内容工具只能从 <c>FieldSchema</c> 直接读（编辑器等消费方不便直接引用 C# 契约类型）。
    /// 本类型按表递归收集全表（含顶层与任意深度嵌套）挂了 <see cref="FieldSchema.IsDeprecated"/> 的
    /// 字段，供 <c>--list-tables --json</c> 新增的表级 <c>deprecated_paths</c> 使用（见
    /// <c>toolchain/validator/Program.cs</c> <c>PrintJson</c> 判断记录）。
    /// <para>
    /// 判断记录：与 <see cref="SchemaFieldRangeExport"/> 同一套记法、同一套"祖先链按对象引用比较"防环
    /// 策略——两者都是对同一份静态 schema 图的只读遍历，只是收集条件不同（本类型不限定
    /// <see cref="FieldKind"/>，任意种类字段都可能被标记废弃）。不复用同一个私有 Walk 方法是因为两者
    /// 收集的目标类型与命中条件都不同，硬凑到一起反而会让两边都多出一堆与自己无关的分支判断——同
    /// <see cref="SchemaFieldRangeExport"/> 类型顶部"不复用 SchemaAudit.WalkField"判断记录同一个理由。
    /// </para>
    /// <para>
    /// 判断记录（"全量导出"，与 <c>architecture/04_数据与内容管线.md</c> 第 3.4 节勘误呼应）：本类型
    /// 不区分顶层/嵌套，凡是 <see cref="FieldSchema.IsDeprecated"/> 为真的字段一律收进结果——顶层字段
    /// 会与既有 <c>field_meta.deprecated</c> 重复出现一次，但消费方如果只想要"这张表所有废弃字段"的
    /// 单一权威清单，不需要再对照两个键取并集；仍需要"顶层字段是否废弃"这一件事本身的消费方，继续用
    /// 既有 <c>field_meta.deprecated</c>（该键的既有形状/语义不受本次改动影响）。
    /// </para>
    /// </summary>
    public static class SchemaFieldDeprecationExport
    {
        /// <summary>递归深度上限，与 <see cref="SchemaFieldRangeExport"/>/<see cref="SchemaAudit"/>/
        /// <c>DataRegistry.MaxSubstructureDepth</c> 取值一致，理由同源（防御自引用 schema 图的无限
        /// 递归）。</summary>
        private const int MaxDepth = 32;

        /// <summary>按 <see cref="TableSchema.Fields"/> 出现顺序、深度优先收集本表全部
        /// <see cref="FieldSchema.IsDeprecated"/> 为真的字段（含顶层与任意深度嵌套）。
        /// <see cref="TableSchema.IsUnschematized"/> 的表返回空列表（没有字段结构可遍历）。</summary>
        public static IReadOnlyList<DeprecatedFieldInfo> Collect(TableSchema schema)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));

            var result = new List<DeprecatedFieldInfo>();
            if (schema.IsUnschematized) return result;

            var ancestors = new List<FieldSchema>();
            var fields = schema.Fields;
            for (var i = 0; i < fields.Count; i++)
            {
                Walk(fields[i], fields[i].Name, depth: 0, result, ancestors);
            }
            return result;
        }

        private static void Walk(FieldSchema field, string path, int depth, List<DeprecatedFieldInfo> result, List<FieldSchema> ancestors)
        {
            if (ancestors.Contains(field)) return; // 自引用环，同 SchemaFieldRangeExport.Walk 判断记录。

            if (field.IsDeprecated)
            {
                result.Add(new DeprecatedFieldInfo(path, field.DeprecatedSince!, field.ReplacedBy, field.DeprecationNote));
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
