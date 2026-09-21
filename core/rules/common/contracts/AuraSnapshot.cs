using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 消费方反馈第 4 条（2026-09-21，ADR-0056）：<see cref="IAuraQuery.GetActiveAuraSnapshots"/>
    /// 返回的单条光环快照——把此前 <see cref="IAuraQuery.GetActiveAuraDefs"/>/<see
    /// cref="IAuraQuery.GetStacks"/> 只能分别、粗粒度回答的"带了哪些/第几层"，与此前完全没有查询
    /// 出口的"剩余/总时长"合并成表现层增益/减益列表一次查询就够用的完整视图。
    /// <para>
    /// 判断记录（转发原始值/文本键，不解析显示文本、不做本地化）：同 <c>HudViewModel.TargetName</c>/
    /// <c>ActionBarSlotSnapshot.NameKey</c> 一贯"转发文本键不本地化"的既有惯例（ADR-0048）——
    /// <see cref="NameKey"/> 是 <c>skill.aura_def.name_key</c> 文本键原样转发，不是已解析好的显示
    /// 文本；本类型不持有任何 <c>IL10nHost</c> 依赖。
    /// </para>
    /// <para>
    /// 一个发现的交付缺口，随后补齐（2026-09-21，<see cref="Polarity"/>/<see cref="IconRef"/>，
    /// ADR-0060）：消费方原始反馈第 4 条"期望行为"一并提出的图标引用与极性字段，ADR-0056 当时
    /// 拍板收口范围只覆盖身份/层数/剩余/总时长/名称键、把图标引用与极性字段留给后续独立评审
    /// （需要先确定 <c>display</c> 模块的图标引用惯例、极性取值集合，见 ADR-0056 判断记录）；
    /// ADR-0060 完成该项评审后补齐这两个字段，均为纯加法。
    /// </para>
    /// </summary>
    public sealed class AuraSnapshot
    {
        /// <summary>光环定义 id（<c>skill.aura_def.id</c>）——原始身份引用，不解析显示文本（同
        /// <c>HudViewModel.TargetId</c> 一贯惯例）。</summary>
        public Id AuraDefId { get; }

        /// <summary>当前层数（见 <see cref="IAuraQuery.GetStacks"/>）。</summary>
        public int Stacks { get; }

        /// <summary>剩余持续时间；永久光环（<c>duration</c> 未声明）或该查询无法得知剩余时间时为
        /// <c>null</c>——两种语义与 <see cref="IAuraQuery.GetActiveAuraDefs"/> 既有的"只列出带了
        /// 哪些"惯例一致：本字段是尽力而为的补充信息，不是本快照存在与否的判据。</summary>
        public double? Remaining { get; }

        /// <summary>本次（重新）施加声明的一次完整持续时间（供接入方计算"已经过去多久"）；永久光环
        /// 或无法得知时为 <c>null</c>，语义同 <see cref="Remaining"/>。</summary>
        public double? Total { get; }

        /// <summary>显示名文本键（<c>skill.aura_def.name_key</c>）；未声明该字段或无法解析时为
        /// <c>null</c>，接入方据此决定不渲染名称，不回退占位文案（同 ADR-0048 惯例）。</summary>
        public Id? NameKey { get; }

        /// <summary>
        /// 一个发现的交付缺口（2026-09-21，[ADR-0060](../../../../architecture/adr/0060-光环极性与图标引用字段补全.md)）：
        /// 光环极性，供接入方画出正负边框/分区。结构化枚举 <see cref="AuraPolarity"/>（不是字符串
        /// 也不是布尔，同批交付另一条不可用原因 <c>ActionBarSlotBlockReason</c> 同一惯例，字符串
        /// 让接入方只能自己拼字面量比较、拼错编译期不报错，布尔表达不出"未声明"）。
        /// <see cref="AuraPolarity.Undeclared"/> 表示未声明——不编造默认极性，也不用可空类型
        /// 表达"未声明"（惯例同 <see cref="NameKey"/> 判断记录，但这里用枚举自身的显式取值，见
        /// <see cref="AuraPolarity"/> 判断记录）。
        /// </summary>
        public AuraPolarity Polarity { get; }

        /// <summary>
        /// 一个发现的交付缺口（2026-09-21，ADR-0060）：光环图标资源引用（<c>skill.aura_def.icon_ref</c>
        /// 原样转发，类别前缀 <c>icon</c>，见 ADR-0038/0039）。<c>null</c> 表示未声明，惯例同
        /// <see cref="Polarity"/>/<see cref="NameKey"/>。
        /// </summary>
        public Id? IconRef { get; }

        public AuraSnapshot(Id auraDefId, int stacks, double? remaining, double? total, Id? nameKey)
        {
            AuraDefId = auraDefId;
            Stacks = stacks;
            Remaining = remaining;
            Total = total;
            NameKey = nameKey;
            Polarity = AuraPolarity.Undeclared;
            IconRef = null;
        }

        /// <summary>
        /// 一个发现的交付缺口新增重载（2026-09-21，ADR-0060）：携带 <see cref="Polarity"/>/
        /// <see cref="IconRef"/>。判断记录（不是给既有构造函数追加两个可选参数）：同 <c>AuraDef</c>
        /// 新增重载判断记录同一套 ABI 兼容惯例——既有构造函数追加参数会改变其物理 IL 签名；本重载
        /// 七个参数全部不带默认值，与既有构造函数（恰好五个参数）参数个数不重叠，互不冲突。本重载
        /// 尚未随任何版本发布（随本次改动一并落地），协调方拍板"极性改用结构化枚举"时直接改了本
        /// 重载的参数类型（<c>string? polarity</c> → <c>AuraPolarity polarity</c>），不再叠加第三个
        /// 重载——同一未发布签名允许直接改，不必背 ABI 包袱。
        /// </summary>
        public AuraSnapshot(Id auraDefId, int stacks, double? remaining, double? total, Id? nameKey, AuraPolarity polarity, Id? iconRef)
        {
            AuraDefId = auraDefId;
            Stacks = stacks;
            Remaining = remaining;
            Total = total;
            NameKey = nameKey;
            Polarity = polarity;
            IconRef = iconRef;
        }
    }
}
