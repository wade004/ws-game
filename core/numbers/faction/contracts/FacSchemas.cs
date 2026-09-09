using Core.Foundation.DataRegistry;

namespace Core.Numbers.Faction
{
    /// <summary>
    /// <c>fac.faction</c> / <c>fac.reaction_matrix</c> 的 <see cref="TableSchema"/>（见
    /// 01_分层与依赖.md L1 模块表 <c>faction</c> 行、04_数据与内容管线.md 第 1.1 节表清单
    /// "阵营定义：id、默认敌友矩阵行" / "阵营对阵营的默认反应（敌对/中立/友好）"、
    /// 00_架构总则.md 第 3 节"阵营矩阵……缩到小矩阵（敌对/中立/友好等有限枚举）"）。
    /// <para>
    /// 判断记录：04 未给出这两张表的完整字段表，字段为实现期按任务书 T2-3 给出的最小字段集
    /// 补录，取舍见 <c>schema/README.md</c>。
    /// </para>
    /// </summary>
    public static class FacSchemas
    {
        private static readonly string[] ReactionValues = { "hostile", "neutral", "friendly" };

        public static readonly TableSchema Faction = new TableSchema(
            name: "fac.faction",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "阵营 id，格式 fac.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "显示名文本键"),
                new FieldSchema("default_reaction", FieldKind.Enum, required: true, enumValues: ReactionValues,
                    description: "与未在 fac.reaction_matrix 中显式登记的阵营之间的默认关系"),
            });

        public static readonly TableSchema ReactionMatrix = new TableSchema(
            name: "fac.reaction_matrix",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "矩阵行 id，格式 fac.reaction.<name>"),
                new FieldSchema("from", FieldKind.Reference, required: true, referenceTable: "fac.faction",
                    description: "引用 fac.faction，反应发起方阵营"),
                new FieldSchema("to", FieldKind.Reference, required: true, referenceTable: "fac.faction",
                    description: "引用 fac.faction，反应目标方阵营"),
                new FieldSchema("reaction", FieldKind.Enum, required: true, enumValues: ReactionValues,
                    description: "from 对 to 的显式反应；矩阵不要求对称，缺失方向按其 from 端的 default_reaction 回退"),
            });
    }
}
