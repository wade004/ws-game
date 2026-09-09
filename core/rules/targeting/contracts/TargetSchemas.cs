using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Core.Rules.Targeting
{
    /// <summary>
    /// <c>target.chain_def</c> 的 <see cref="TableSchema"/>（见 06_规则层_属性技能战斗AI.md 第 5
    /// 节 <c>TargetChainDef</c> 结构、01_分层与依赖.md L2 <c>targeting</c> 行"表：
    /// target.chain_def"）。字段取舍判断记录见 <c>schema/README.md</c>。
    /// </summary>
    public static class TargetSchemas
    {
        public static readonly string[] ShapeKindValues = { "circle", "cone", "line", "rect" };
        public static readonly string[] SortKeyValues = { "distance", "hp_pct", "threat", "level" };
        public static readonly string[] SortDirectionValues = { "asc", "desc" };

        /// <summary><c>shape</c>：判别字段 <c>kind</c> 与 <c>radius</c>/<c>angle</c>/<c>length</c>/
        /// <c>width</c> 同处一个对象内，Variants 适用。<c>TargetChainDef.ParseShape</c> 的
        /// <c>GetNumber</c> 对缺失字段一律返回 0（不抛异常），四个数值子字段均登记为非必填。</summary>
        public static readonly FieldSchema ShapeSchema = new FieldSchema(
            "shape", FieldKind.Object, required: false, variants: BuildShapeVariants(),
            description: "{kind: circle|cone|line|rect, radius?, angle?, length?, width?}；" +
                "origin/direction/rotation 不在数据里声明，由 TargetHost 在解析时按施法者当前坐标/朝向" +
                "重新锚定（见 schema/README.md 判断记录）。");

        private static VariantSchema BuildShapeVariants()
        {
            var radius = new FieldSchema("radius", FieldKind.Number, required: false, description: "缺省 0");
            var angle = new FieldSchema("angle", FieldKind.Number, required: false, description: "缺省 0");
            var length = new FieldSchema("length", FieldKind.Number, required: false, description: "缺省 0");
            var width = new FieldSchema("width", FieldKind.Number, required: false, description: "缺省 0");

            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(System.StringComparer.Ordinal)
            {
                ["circle"] = new[] { radius },
                ["cone"] = new[] { angle, radius },
                ["line"] = new[] { length, width },
                // 判断记录：rect 复用 line 的 length/width 命名（换算成 halfExtents，见
                // TargetChainDef.ParseShape 该分支注释），不是独立字段集。
                ["rect"] = new[] { length, width },
            };
            return new VariantSchema("kind", cases);
        }

        public static readonly TableSchema ChainDef = new TableSchema(
            name: "target.chain_def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true),
                new FieldSchema("source", FieldKind.String, required: true,
                    description: "目标来源策略名，须已在 TargetStrategyRegistry 登记；内置六种见 " +
                        "BuiltinTargetStrategies，游戏层可注册更多（见 00 第 4 节原则 10、01 第 8 节第 3 种" +
                        "合法调用方式）。本模块不把 source 声明为 Enum——枚举取值固定、无法被游戏层扩展。"),
                ShapeSchema,
                new FieldSchema("filters", FieldKind.Array, required: false,
                    item: new FieldSchema("<filter>", FieldKind.String, required: true),
                    description: "Expr 文本或内置简写（relation:hostile|friendly|neutral|not_self、alive、" +
                        "tag:<id>）组成的数组；全部条件按 AND 组合（见 schema/README.md 判断记录）。" +
                        "判断记录：不登记为 FieldKind.Expr——内置简写（如 relation:hostile）不是合法 Expr " +
                        "语法，登记为 Expr 会对合法简写误报 expr_parsable；混合语法解析继续由 " +
                        "ChainDefValidationRule 负责（该规则已知晓 shorthand 前缀，不与本登记双报）。"),
                new FieldSchema("sort_by", FieldKind.Object, required: false, fields: new[]
                {
                    new FieldSchema("key", FieldKind.Enum, required: true, enumValues: SortKeyValues),
                    new FieldSchema("direction", FieldKind.Enum, required: false, enumValues: SortDirectionValues,
                        description: "缺省 asc"),
                },
                    description: "{key: distance|hp_pct|threat|level, direction: asc|desc}；省略 direction 时按 asc。"),
                new FieldSchema("max_targets", FieldKind.Int, required: false,
                    description: "默认 1；0 表示不限。"),
                new FieldSchema("fallback", FieldKind.Reference, required: false, referenceTable: "target.chain_def",
                    description: "候选为空时改用的另一条链；不得成环（见 ChainDefValidationRule）。"),
            });
    }
}
