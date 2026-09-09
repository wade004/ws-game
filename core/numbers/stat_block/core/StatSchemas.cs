using Core.Foundation.DataRegistry;

namespace Core.Numbers.StatBlock
{
    /// <summary>
    /// <c>stat.definition</c>/<c>stat.rating_conversion</c> 的 <see cref="TableSchema"/>
    /// 声明（见 04_数据与内容管线.md 第 1.1 节表清单、schema/README.md 字段表）。调用方在
    /// 构造 <see cref="Core.Foundation.DataRegistry.IDataRegistry"/> 后需要
    /// <c>RegisterSchema(StatSchemas.Definition)</c>/<c>RegisterSchema(StatSchemas.RatingConversion)</c>
    /// 才能加载对应数据文件（本模块不自动注册，注册时机由宿主统一掌控，见 11 第 2 节模块范式）。
    /// </summary>
    public static class StatSchemas
    {
        /// <summary>属性分组枚举合法取值（06 第 1 节、04 第 1.1 节）。<c>resistance</c> 分组是
        /// 00 第 3 节"可选属性维度"的落地，是否生效由 <see cref="StatHostOptions.EnableResistanceGroup"/>
        /// 控制，与本表校验无关——校验只保证取值在合法集合内。</summary>
        public static readonly string[] GroupValues = { "primary", "secondary", "derived", "resistance" };

        public static TableSchema Definition { get; } = new TableSchema(
            name: "stat.definition",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "属性 id，stat.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "显示名文本键"),
                new FieldSchema("group", FieldKind.Enum, required: true, enumValues: GroupValues,
                    description: "聚合分组：primary/secondary/derived/resistance"),
                new FieldSchema("default_base", FieldKind.Number, required: false,
                    description: "未显式 SetBase 时的基础值，缺省 0"),
                new FieldSchema("min", FieldKind.Number, required: false,
                    description: "最终值下限（可空）"),
                new FieldSchema("max", FieldKind.Number, required: false,
                    description: "最终值上限（可空）"),
                new FieldSchema("is_rating", FieldKind.Bool, required: false,
                    description: "评级换算启用时，本属性是否先过曲线，缺省 false"),
                new FieldSchema("rating_conversion_ref", FieldKind.Reference, required: false,
                    referenceTable: "stat.rating_conversion",
                    description: "指向 stat.rating_conversion 的曲线引用，仅 is_rating=true 时有意义"),
                new FieldSchema("description", FieldKind.String, required: false),
            });

        public static TableSchema RatingConversion { get; } = new TableSchema(
            name: "stat.rating_conversion",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "stat.rating.<name>"),
                new FieldSchema("entries", FieldKind.Array, required: true,
                    item: new FieldSchema("<rating_entry>", FieldKind.Object, required: true, fields: new[]
                    {
                        new FieldSchema("level", FieldKind.Int, required: true),
                        new FieldSchema("points_per_percent", FieldKind.Number, required: true),
                    }),
                    description: "[{level: Int, points_per_percent: Number}, ...]，按 level 升序" +
                        "（StatHost.LoadRatingConversions 对缺失 level/points_per_percent 抛异常，两者均必填）"),
            });
    }
}
