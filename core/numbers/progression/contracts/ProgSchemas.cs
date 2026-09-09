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
                    item: new FieldSchema("<level_entry>", FieldKind.Object, required: true, fields: new[]
                    {
                        new FieldSchema("level", FieldKind.Int, required: true),
                        new FieldSchema("xp_to_next", FieldKind.Int, required: true),
                        // growth 是 Map<stat_id, Number>（键为 stat.definition 的 id，动态键）：
                        // ADR-0019 通用规则 5，Map 型对象不登记子结构，保持"存在且是对象"。
                        new FieldSchema("growth", FieldKind.Object, required: false,
                            description: "Map<stat_id, Number>，见 ADR-0019 通用规则 5，不登记子结构"),
                    }),
                    description: "Array<{level:Int, xp_to_next:Int, growth:Object<stat_id,Number>}>，" +
                        "level 从 1 连续到 max_level（连续性/数量一致性业务判断留在 " +
                        "ProgLevelCurveValidationRule，登记层只表达无条件必填/类型）"),
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
