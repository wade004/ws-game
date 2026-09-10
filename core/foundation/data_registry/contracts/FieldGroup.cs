namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// ADR-0022（04 第 3.4 节"字段分组元数据"）：字段的语义分组，与 <c>editor/docs/编辑器产品文档.md</c>
    /// 5.4.1 节通用表编辑器表单视图的五个分组一一对应，供编辑器按分组折叠/排序字段。
    /// <para>
    /// 判断记录（默认值而非逐字段手工登记）：全仓已登记字段超过 800 个，逐个手工调用
    /// <see cref="FieldSchema.WithGroup"/> 既难以保证与本节分类规则一致，也会在后续新增字段时
    /// 持续产生"忘记登记"的遗漏风险。<see cref="FieldSchema.Group"/> 因此按 ADR-0022 决策 2 给出的
    /// 分类规则（主键/名称/描述类=基础；Reference/IdList/Id 类=引用；Number/Int 类=数值；
    /// 名称含表现/资源类关键字=表现；其余=高级）计算默认值——效果等价于对全部已登记字段"手工填齐"
    /// （同一规则产出同一结果，不因新增字段而遗漏），<see cref="FieldSchema.WithGroup"/> 仍保留用于
    /// 规则误判时的显式覆盖（见该方法判断记录与调用点，如 <c>display.map.icon_id</c> 等）。
    /// </para>
    /// </summary>
    public enum FieldGroup
    {
        /// <summary>基础：主键、名称/描述类文本键、schema 元字段。</summary>
        Basic,

        /// <summary>引用：<see cref="FieldKind.Reference"/>/<see cref="FieldKind.IdList"/>/
        /// <see cref="FieldKind.Id"/>（不含被归为"基础"的主键字段本身）。</summary>
        Reference,

        /// <summary>数值：<see cref="FieldKind.Number"/>/<see cref="FieldKind.Int"/>。</summary>
        Numeric,

        /// <summary>表现：外观/动效/音效/UI 呈现相关字段。</summary>
        Presentation,

        /// <summary>高级：其余不落入以上四类的可选/进阶字段。</summary>
        Advanced,
    }
}
