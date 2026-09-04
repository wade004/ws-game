using Core.Foundation.DataRegistry;

namespace Core.Numbers.Progression
{
    /// <summary>
    /// <c>prog.level_curve</c> / <c>prog.xp_source</c> 的 <see cref="TableSchema"/>（见
    /// 01_分层与依赖.md L1 模块表 <c>progression</c> 行、04_数据与内容管线.md 第 1.1 节表清单
    /// "等级到所需经验、到属性成长系数的曲线表" / "经验来源 id 到经验值与限制规则"）。
    /// <para>
    /// 判断记录：04 未给出这两张表的完整字段表（只有一句话描述），字段为实现期按任务书 T2-3
    /// 给出的最小字段集补录，详细取舍与判断见 <c>schema/README.md</c>。
    /// </para>
    /// </summary>
    public static class ProgSchemas
    {
        public static readonly TableSchema LevelCurve = new TableSchema(
            name: "prog.level_curve",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("max_level", FieldKind.Int, required: true,
                    description: "曲线的最大等级，必须等于 entries 的元素个数"),
                new FieldSchema("entries", FieldKind.Array, required: true,
                    description: "Array<{level:Int, xp_to_next:Int, growth:Object<stat_id,Number>}>，" +
                        "level 从 1 连续到 max_level；结构由本模块自行解析（04 第 5 节 field_type 对 " +
                        "Array 只做\"存在且是数组\"检查），连续性由 ProgLevelCurveValidationRule 校验"),
            });

        public static readonly TableSchema XpSource = new TableSchema(
            name: "prog.xp_source",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("base_xp", FieldKind.Int, required: true),
                new FieldSchema("weight", FieldKind.Number, required: false,
                    description: "省略时按 1 处理（见 IProgressionHost.GrantFromSource）"),
                new FieldSchema("condition", FieldKind.Expr, required: false,
                    description: "本任务只登记字段类型（供未来内容校验 expr_parsable 使用），" +
                        "IProgressionHost 本身不对该字段求值"),
            });
    }
}
