using System;
using System.Collections.Generic;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 单个字段的静态声明：类型、是否必填、枚举合法集合、引用目标（见 04 第 5 节校验器
    /// 检查项清单 "引用完整性" "枚举合法" "文本键存在" 与第 4 节 <c>declareReference</c>）。
    /// <para>
    /// ADR-0019 扩展（04 第 3.2 节"复合字段子结构登记"）：<see cref="FieldKind.Object"/>/
    /// <see cref="FieldKind.Array"/> 两种字段种类可选登记机器可读的子结构（<see cref="Fields"/>/
    /// <see cref="Item"/>/<see cref="Variants"/>），供 <c>DataRegistry</c> 递归校验；不登记时行为
    /// 与登记前完全一致（只检查"存在且类型匹配"），向后兼容。
    /// </para>
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

        /// <summary>仅 <see cref="FieldKind.Object"/> 可设：该对象的子字段清单，可递归嵌套
        /// <see cref="FieldKind.Object"/>/<see cref="FieldKind.Array"/>；与 <see cref="Variants"/>
        /// 互斥（见 ADR-0019）。</summary>
        public IReadOnlyList<FieldSchema>? Fields { get; }

        private readonly Func<FieldSchema>? _itemFactory;
        private FieldSchema? _item;
        private bool _itemResolved;

        /// <summary>仅 <see cref="FieldKind.Array"/> 可设：数组元素的结构；元素本身可以是任意
        /// <see cref="FieldKind"/>（含 <see cref="FieldKind.Object"/>/<see cref="FieldKind.Array"/>）。
        /// 若构造时传入 <c>itemFactory</c>，首次访问本属性时求值并缓存（见类型顶部"惰性递归"，
        /// 支持自引用结构如 <c>projectile.params.on_hit_effects</c> 复用 <c>effects</c> 自身的元素
        /// 结构）。</summary>
        public FieldSchema? Item
        {
            get
            {
                if (!_itemResolved)
                {
                    _item = _itemFactory != null ? _itemFactory() : _item;
                    _itemResolved = true;
                }
                return _item;
            }
        }

        private readonly Func<VariantSchema>? _variantsFactory;
        private VariantSchema? _variants;
        private bool _variantsResolved;

        /// <summary>仅 <see cref="FieldKind.Object"/> 可设，且与 <see cref="Fields"/> 互斥：按判别
        /// 字段分派的变体子结构（见 <see cref="VariantSchema"/>）。惰性求值语义同 <see cref="Item"/>。</summary>
        public VariantSchema? Variants
        {
            get
            {
                if (!_variantsResolved)
                {
                    _variants = _variantsFactory != null ? _variantsFactory() : _variants;
                    _variantsResolved = true;
                }
                return _variants;
            }
        }

        public FieldSchema(
            string name,
            FieldKind kind,
            bool required,
            IReadOnlyList<string>? enumValues = null,
            string? referenceTable = null,
            string? referenceDomain = null,
            string? description = null,
            IReadOnlyList<FieldSchema>? fields = null,
            FieldSchema? item = null,
            VariantSchema? variants = null,
            Func<FieldSchema>? itemFactory = null,
            Func<VariantSchema>? variantsFactory = null)
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

            // ADR-0019：子结构登记的非法组合检查（04 第 3.2 节）。
            if (fields != null && kind != FieldKind.Object)
            {
                throw new ArgumentException($"字段 \"{name}\"：Fields 仅 Object 字段可设", nameof(fields));
            }
            if ((variants != null || variantsFactory != null) && kind != FieldKind.Object)
            {
                throw new ArgumentException($"字段 \"{name}\"：Variants 仅 Object 字段可设", nameof(variants));
            }
            if ((item != null || itemFactory != null) && kind != FieldKind.Array)
            {
                throw new ArgumentException($"字段 \"{name}\"：Item 仅 Array 字段可设", nameof(item));
            }
            if (item != null && itemFactory != null)
            {
                throw new ArgumentException($"字段 \"{name}\"：item 与 itemFactory 至多设置一个", nameof(itemFactory));
            }
            if (variants != null && variantsFactory != null)
            {
                throw new ArgumentException($"字段 \"{name}\"：variants 与 variantsFactory 至多设置一个", nameof(variantsFactory));
            }
            if (fields != null && (variants != null || variantsFactory != null))
            {
                throw new ArgumentException($"字段 \"{name}\"：Fields 与 Variants 互斥", nameof(variants));
            }

            Name = name;
            Kind = kind;
            Required = required;
            EnumValues = enumValues;
            ReferenceTable = referenceTable;
            ReferenceDomain = referenceDomain;
            Description = description;
            Fields = fields;

            _item = item;
            _itemFactory = itemFactory;
            _itemResolved = itemFactory == null;

            _variants = variants;
            _variantsFactory = variantsFactory;
            _variantsResolved = variantsFactory == null;
        }
    }
}
