using System;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// ADR-0024（04 第 3.3 节"映射登记"）：<see cref="FieldKind.Object"/> 字段的动态键映射子结构
    /// 登记——键本身不是固定清单（与 <see cref="FieldSchema.Fields"/> 表达的"固定键对象"互斥），
    /// 而是"键的合法性约束 + 每个键对应值的登记"，典型例子是 <c>arch.class.base_stats</c>
    /// （键为 <c>stat.definition</c> 的引用、值为数值）。由 <see cref="FieldSchema.WithMap"/> 挂到
    /// 具体字段上；<see cref="DataRegistry"/> 在字段级校验阶段据此递归检查每个已出现的键/值
    /// （键按 <see cref="KeyReferenceTable"/>/<see cref="KeyReferenceDomain"/> 并入
    /// <c>reference_integrity</c> 检查项，值按 <see cref="ValueSchema"/> 递归校验，沿用 ADR-0019
    /// 子结构路径记法，如 <c>base_stats[stat.strength]</c>）。
    /// <para>
    /// 判断记录（三选一而非可独立组合的三个属性）：键约束只能是"引用某表""引用某 domain""自由字符串"
    /// 三者之一，与 <see cref="FieldSchema"/> 的 <see cref="FieldSchema.ReferenceTable"/>/
    /// <see cref="FieldSchema.ReferenceDomain"/>（至多设置一个、可以都不设）不同——本类型故意不留
    /// "三者都不设"的中间态（<see cref="FieldSchema.IdList"/> 的 <see cref="FieldSchema.FreeIds"/>
    /// 允许"忘记声明"从而被 <c>SchemaAudit</c> 的 <c>idlist_reference_target</c> 检查项事后发现，
    /// 那是因为 <see cref="FieldSchema.ReferenceTable"/>/<see cref="FieldSchema.ReferenceDomain"/>/
    /// <see cref="FieldSchema.FreeIds"/> 三者是各自独立设置的属性，历史上允许"都不设"这个非法中间态
    /// 存在过）——本类型只有三个私有静态工厂（<see cref="ReferenceKeyTable"/>/
    /// <see cref="ReferenceKeyDomain"/>/<see cref="FreeKeys"/>），构造完成的实例天然满足"三选一"，
    /// 不需要 <c>SchemaAudit</c> 再补一道"键约束遗漏"检查——这是本类型相对 IdList 既有模式的一处
    /// 有意简化，见 <c>FreeKeys</c> 判断记录。
    /// </para>
    /// </summary>
    public sealed class MapSchema
    {
        /// <summary>键必须在此表中存在（<c>reference_integrity</c> 检查项，等价于
        /// <see cref="FieldSchema.ReferenceTable"/> 的键版本）；与
        /// <see cref="KeyReferenceDomain"/>/<see cref="FreeKeys"/> 互斥。</summary>
        public string? KeyReferenceTable { get; }

        /// <summary>键的 domain 段必须等于此值，且必须能在某张已加载、首段等于该 domain 的表里找到
        /// （等价于 <see cref="FieldSchema.ReferenceDomain"/> 的键版本）；与
        /// <see cref="KeyReferenceTable"/>/<see cref="FreeKeys"/> 互斥。</summary>
        public string? KeyReferenceDomain { get; }

        /// <summary>键不指向任何已登记表/domain（自由字符串，如材质参数名、局部声明的槽位/锚点名）；
        /// 与 <see cref="KeyReferenceTable"/>/<see cref="KeyReferenceDomain"/> 互斥。</summary>
        public bool FreeKeys { get; }

        /// <summary>见 <see cref="FreeKeys"/>；<see cref="Core.Foundation.DataRegistry.MapSchema"/>
        /// 处于 <see cref="FreeKeys"/> 形态时必填，供门禁报告与人工审阅（同
        /// <see cref="FieldSchema.FreeIdsReason"/> 惯例）。</summary>
        public string? FreeKeysReason { get; }

        /// <summary>每个值的登记：种类/范围/子结构均可用，与顶层 <see cref="FieldSchema"/> 完全同构
        /// （递归复用同一套 <see cref="DataRegistry"/> 校验实现，见类型顶部判断记录）。<c>Name</c> 仅
        /// 作占位（不出现在校验路径里，路径由键本身决定，见 <see cref="DataRegistry"/>
        /// <c>ValidateMapObject</c> 判断记录），惯例传 <c>"value"</c>。</summary>
        public FieldSchema ValueSchema { get; }

        private MapSchema(string? keyReferenceTable, string? keyReferenceDomain, bool freeKeys, string? freeKeysReason, FieldSchema valueSchema)
        {
            KeyReferenceTable = keyReferenceTable;
            KeyReferenceDomain = keyReferenceDomain;
            FreeKeys = freeKeys;
            FreeKeysReason = freeKeysReason;
            ValueSchema = valueSchema ?? throw new ArgumentNullException(nameof(valueSchema));
        }

        /// <summary>键必须是 <paramref name="referenceTable"/> 的既有记录主键。</summary>
        public static MapSchema ReferenceKeyTable(string referenceTable, FieldSchema valueSchema)
        {
            if (string.IsNullOrEmpty(referenceTable)) throw new ArgumentException("referenceTable 不能为空", nameof(referenceTable));
            return new MapSchema(referenceTable, null, false, null, valueSchema);
        }

        /// <summary>键的 domain 段必须是 <paramref name="referenceDomain"/>，且能在该 domain 下某张
        /// 已加载表里找到。</summary>
        public static MapSchema ReferenceKeyDomain(string referenceDomain, FieldSchema valueSchema)
        {
            if (string.IsNullOrEmpty(referenceDomain)) throw new ArgumentException("referenceDomain 不能为空", nameof(referenceDomain));
            return new MapSchema(null, referenceDomain, false, null, valueSchema);
        }

        /// <summary>键是自由字符串，不做任何存在性/格式校验；<paramref name="reason"/> 必填，说明
        /// 为何该映射的键确属自由字符串（同 <see cref="FieldSchema.WithFreeIds"/> 惯例）。</summary>
        public static MapSchema FreeKeyed(string reason, FieldSchema valueSchema)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("reason 不能为空——需说明为何该映射的键确属自由字符串", nameof(reason));
            return new MapSchema(null, null, true, reason, valueSchema);
        }
    }
}
