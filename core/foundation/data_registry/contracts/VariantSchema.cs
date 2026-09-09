using System;
using System.Collections.Generic;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 按判别字段分派的复合字段子结构登记（见 ADR-0019、04 第 3.2 节"复合字段子结构登记"、
    /// <see cref="FieldSchema.Variants"/>）：某个 <see cref="FieldKind.Object"/> 字段的子字段清单
    /// 由同一层级内的 <see cref="Discriminator"/> 字段取值决定——<see cref="Discriminator"/> 必须是
    /// 字符串且落在 <see cref="Cases"/> 的键集合内（登记本身即等价于对判别字段的一次枚举校验），
    /// 命中的取值对应的字段清单再叠加 <see cref="CommonFields"/>（全部取值共有的子字段，判别字段
    /// 本身不必重复登记）一起作为该对象的完整子字段清单递归校验。
    /// </summary>
    public sealed class VariantSchema
    {
        /// <summary>同一层级内的判别字段名（如效果条目的 <c>"kind"</c>）。</summary>
        public string Discriminator { get; }

        /// <summary>键 = 判别字段的合法取值；值 = 该取值下额外生效的子字段清单（不含判别字段本身、
        /// 不含 <see cref="CommonFields"/>，两者由校验器合并）。</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<FieldSchema>> Cases { get; }

        /// <summary>全部取值共有的子字段（可选）；判别字段本身不必出现在这里，校验器按判别字段的
        /// 既有语义（必填、必须是字符串、必须在 <see cref="Cases"/> 键集合内）单独处理。</summary>
        public IReadOnlyList<FieldSchema>? CommonFields { get; }

        public VariantSchema(
            string discriminator,
            IReadOnlyDictionary<string, IReadOnlyList<FieldSchema>> cases,
            IReadOnlyList<FieldSchema>? commonFields = null)
        {
            if (string.IsNullOrEmpty(discriminator))
            {
                throw new ArgumentException("Discriminator 不能为空", nameof(discriminator));
            }
            if (cases == null || cases.Count == 0)
            {
                throw new ArgumentException("Cases 不能为空", nameof(cases));
            }

            Discriminator = discriminator;
            Cases = cases;
            CommonFields = commonFields;
        }
    }
}
