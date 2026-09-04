namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 数据表字段的类型标签（见 04_数据与内容管线.md 各表字段表用到的记法：
    /// Bool/Int/Number/String/Id/List&lt;Id&gt;/Optional&lt;Id&gt;（引用）/文本键/Expr/枚举/
    /// Vec2/Map/List）。<see cref="FieldSchema"/> 用它声明每个字段应如何被
    /// <c>field_type</c> 校验项检查、如何被 <see cref="DataRecord"/> 的类型化访问器读取。
    /// </summary>
    public enum FieldKind
    {
        Bool,
        Int,
        Number,
        String,

        /// <summary>单个逻辑 id（<c>Core.Foundation.Common.Id</c> 格式），不隐含引用完整性检查——
        /// 是否需要检查目标存在，由 <see cref="FieldKind.Reference"/>、
        /// <see cref="IDataRegistry.DeclareReference"/> 或本类型自身决定。</summary>
        Id,

        /// <summary>Id 列表（04 记法 <c>List&lt;Id&gt;</c>）。</summary>
        IdList,

        /// <summary>指向别的表的外键：值本身是 <see cref="Id"/> 格式的字符串，
        /// <c>reference_integrity</c> 校验项检查其在 <see cref="FieldSchema.ReferenceTable"/>
        /// 或 <see cref="FieldSchema.ReferenceDomain"/> 声明的目标处存在。</summary>
        Reference,

        /// <summary>本地化文本键：值必须能在 <c>l10n.text</c> 表按
        /// <see cref="DataRegistryOptions.DefaultLocale"/> 查到（<c>text_key_exists</c> 校验项）。</summary>
        TextKey,

        /// <summary>Expr 条件表达式文本（04 第 6 节）；<c>expr_parsable</c> 校验项负责解析与静态校验。</summary>
        Expr,

        /// <summary>枚举字符串，取值必须在 <see cref="FieldSchema.EnumValues"/> 内。</summary>
        Enum,

        /// <summary>二维坐标，JSON 表示为 <c>{"x": Number, "y": Number}</c>
        /// （04 未规定具体 JSON 形状，本模块的判断记录见 schema/README.md）。</summary>
        Vec2,

        /// <summary>结构未知/由上层模块自行解释的 JSON 对象，本模块只做"存在且是对象"检查。</summary>
        Object,

        /// <summary>结构未知/由上层模块自行解释的 JSON 数组，本模块只做"存在且是数组"检查。</summary>
        Array,
    }
}
