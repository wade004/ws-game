using System;
using System.Collections.Generic;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 单个字段的静态声明：类型、是否必填、枚举合法集合、引用目标（见 04 第 5 节校验器
    /// 检查项清单 "引用完整性" "枚举合法" "文本键存在" 与第 4 节 <c>declareReference</c>）。
    /// </summary>
    public sealed class FieldSchema
    {
        public string Name { get; }

        public FieldKind Kind { get; }

        public bool Required { get; }

        /// <summary><see cref="FieldKind.Enum"/> 字段的合法取值集合；其余类型为 null。</summary>
        public IReadOnlyList<string>? EnumValues { get; }

        /// <summary><see cref="FieldKind.Reference"/> 字段指向的具体表名（与
        /// <see cref="ReferenceDomain"/> 至多设置一个）。</summary>
        public string? ReferenceTable { get; }

        /// <summary><see cref="FieldKind.Reference"/> 字段指向的 domain（值的 domain 段必须等于
        /// 它，且必须能在某张已加载、首段等于该 domain 的表里找到，见 04 第 2.2 节域名清单）；
        /// 与 <see cref="ReferenceTable"/> 至多设置一个。</summary>
        public string? ReferenceDomain { get; }

        public string? Description { get; }

        public FieldSchema(
            string name,
            FieldKind kind,
            bool required,
            IReadOnlyList<string>? enumValues = null,
            string? referenceTable = null,
            string? referenceDomain = null,
            string? description = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("字段名不能为空", nameof(name));
            if (referenceTable != null && referenceDomain != null)
            {
                throw new ArgumentException("ReferenceTable 与 ReferenceDomain 至多设置一个", nameof(referenceDomain));
            }
            if (kind == FieldKind.Enum && (enumValues == null || enumValues.Count == 0))
            {
                throw new ArgumentException($"字段 \"{name}\" 声明为 Enum 但未提供 EnumValues", nameof(enumValues));
            }

            Name = name;
            Kind = kind;
            Required = required;
            EnumValues = enumValues;
            ReferenceTable = referenceTable;
            ReferenceDomain = referenceDomain;
            Description = description;
        }
    }
}
