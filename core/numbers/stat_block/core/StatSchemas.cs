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
                new FieldSchema("description", FieldKind.String, required: false,
                    description: "属性说明文本，供编辑器/文档展示，可为空"),
            }).WithOwnership(SchemaLayer.Numbers, "stats");

        /// <summary><c>stat.rating_conversion</c>：点数到百分比的换算曲线。分阶段落地计划 T-N0-4
        /// （落地清单 2.1 C4）：<c>entries</c> 迁移到 04 第 3.6 节通用断点表形态 <c>{x, y}</c>
        /// （横轴语义 <see cref="CurveAxis.Level"/>，<c>x</c> = 单位等级，<c>y</c> = 该等级下每 1% 效果
        /// 所需点数），schema 版本 1→2，迁移环节把 v1 的 <c>{level, points_per_percent}</c> 逐元素改名。
        /// <c>y &gt; 0</c> 的范围登记沿用 v1 对 <c>points_per_percent</c> 的判断记录（ADR-0021：
        /// <c>StatHost.DivideByPointsPerPercent</c> 对 0 特判返回 0 是"内容错误被静默降级"的同款模式，
        /// 负值没有合理含义，提前到加载期拦下）。通用规则 <c>curve_monotonic_finite</c> 自动覆盖本表
        /// （数值总纲第 3 节原则 1：除数随等级不递减）。</summary>
        public static TableSchema RatingConversion { get; } = new TableSchema(
            name: "stat.rating_conversion",
            primaryKey: "id",
            currentSchemaVersion: 2,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "stat.rating.<name>"),
                CurveSchema.BreakpointsField("entries", CurveAxis.Level, required: true,
                    description: "断点表 [{x: 单位等级(Int), y: 每 1% 效果所需点数(Number, > 0)}]，按 x 线性插值、" +
                        "越界夹取到端点（04 第 3.6 节通用曲线形态；v1 字段名 level/points_per_percent 经 1→2 迁移改名）",
                    xDescription: "单位等级（StatHostOptions.LevelLookup 查到的等级，不是评级原始值）",
                    yDescription: "该等级下每 1% 效果所需的评级点数，> 0",
                    yRange: FieldRange.Range(min: 0, minExclusive: true)),
            },
            migrations: new[]
            {
                new TableMigration(1, 2, row => CurveSchema.MigrateBreakpointsFieldNames(row, "entries", "level", "points_per_percent")),
            }).WithOwnership(SchemaLayer.Numbers, "stats");
    }
}
