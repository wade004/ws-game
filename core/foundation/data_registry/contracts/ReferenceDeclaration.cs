using System;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 消费方反馈第 37 条（04 第 4 节勘误"declareReference 读回"，2026-09-12，见
    /// architecture/落地计划/消费方反馈-2026-09-12-编辑器-第37条.md）：一条经
    /// <see cref="IDataRegistry.DeclareReference(string, string, string)"/>（及带来源标注的重载
    /// <see cref="IDataRegistry.DeclareReference(string, string, string, string)"/>）登记的"某表某
    /// 字段整体指向另一张表"声明的只读快照，供 <see cref="IDataRegistryView.GetReferenceDeclarations"/>
    /// 回吐给内容工具——此前 <c>DeclareReference</c> 只写入 <see cref="IDataRegistry"/> 内部私有状态
    /// （驱动加载期 <c>reference_integrity</c> 检查），没有任何公开读回方式，内容工具只能按值匹配
    /// 弱推断该字段是否是引用（见反馈原文"仅靠值恰好相等退化为 Inferred，而不是 Soft"）。
    /// <para>
    /// 判断记录（<see cref="ToDomain"/> 恒为 <c>null</c>）：<c>DeclareReference</c> 当前物理签名只接受
    /// 单一目标表名，不支持声明目标 domain（与 <see cref="FieldSchema.ReferenceDomain"/>/
    /// <see cref="FieldSchema.SoftReferenceDomain"/> 的"表/domain 二选一"不同）。<see cref="ToDomain"/>
    /// 字段先按"目标表/域二选一"的既有惯例（同 <see cref="FieldSchema"/> 系列判断记录）预留，当前恒为
    /// <c>null</c>、<see cref="ToTable"/> 恒非空——不是遗漏，是物理能力尚未扩展；未来若
    /// <c>DeclareReference</c> 新增按 domain 声明的重载，无需再破坏性改动本类型。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="IsOptional"/> 的口径）：不是"这条引用声明本身是否可以不生效"——
    /// <c>DeclareReference</c> 登记后，只要源字段在某条记录上出现（<see cref="DataRecord.Has"/>），
    /// 加载期就会做 <c>reference_integrity</c> 硬校验，不存在"软"引用检查这一档（那是
    /// <see cref="FieldSchema.SoftReferenceTable"/> 的职责，两者是不同机制）。<see cref="IsOptional"/>
    /// 反映的是源字段本身在 <see cref="TableSchema"/> 里是否登记为 <see cref="FieldSchema.Required"/>——
    /// 即"这条记录允许完全不填这个字段"，字段缺失时 <c>reference_integrity</c> 检查天然跳过（见
    /// <c>DataRegistry.RunFieldValidation</c> 判断记录），供内容工具据此判断"留空是否合法"。源表当前
    /// 未注册 <see cref="TableSchema"/>（<see cref="IDataRegistryView.GetSchema"/> 返回 <c>null</c>，
    /// 理论上不应发生——<c>DeclareReference</c> 通常晚于源表 <c>RegisterSchema</c> 调用，但接口本身不
    /// 强制顺序）或源表 schema 里找不到该字段时，保守按 <c>true</c>（视为可选，不误导内容工具认为
    /// "必填"）。
    /// </para>
    /// </summary>
    public readonly struct ReferenceDeclaration
    {
        /// <summary>声明引用的源表名。</summary>
        public string FromTable { get; }

        /// <summary>源字段路径（当前 <see cref="IDataRegistry.DeclareReference(string, string, string)"/>
        /// 只支持记录顶层的标量字段，暂不支持嵌套路径，见该方法判断记录"只支持某表某个标量 Id 字段
        /// 整体指向另一张表"）。</summary>
        public string FieldPath { get; }

        /// <summary>目标表名；<see cref="ToDomain"/> 至多设置一个，见该属性判断记录（当前恒非空，
        /// <see cref="ToDomain"/> 恒为 <c>null</c>）。</summary>
        public string ToTable { get; }

        /// <summary>目标 domain；见类型判断记录——预留字段，当前恒为 <c>null</c>。</summary>
        public string? ToDomain { get; }

        /// <summary>源字段是否允许缺失（<see cref="FieldSchema.Required"/> 取反）；见类型判断记录
        /// "IsOptional 的口径"。</summary>
        public bool IsOptional { get; }

        /// <summary>声明来源标注（通常是调用 <c>DeclareReference</c> 的 catalog 类型名，如
        /// <c>"RulesSchemaCatalog"</c>），供内容工具在多个 catalog 交叉排查时定位登记点；旧的三参数
        /// <see cref="IDataRegistry.DeclareReference(string, string, string)"/> 调用未提供来源标注时为
        /// <c>null</c>（见该方法判断记录）。</summary>
        public string? Source { get; }

        public ReferenceDeclaration(string fromTable, string fieldPath, string toTable, string? toDomain, bool isOptional, string? source)
        {
            FromTable = fromTable ?? throw new ArgumentNullException(nameof(fromTable));
            FieldPath = fieldPath ?? throw new ArgumentNullException(nameof(fieldPath));
            ToTable = toTable ?? throw new ArgumentNullException(nameof(toTable));
            ToDomain = toDomain;
            IsOptional = isOptional;
            Source = source;
        }

        public override string ToString() =>
            $"{FromTable}.{FieldPath} -> {ToTable}{(IsOptional ? " (optional)" : "")}{(Source == null ? "" : $" [{Source}]")}";
    }
}
