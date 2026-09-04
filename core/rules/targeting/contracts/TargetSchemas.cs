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
                new FieldSchema("shape", FieldKind.Object, required: false,
                    description: "{kind: circle|cone|line|rect, radius, angle, length, width}；" +
                        "origin/direction/rotation 不在数据里声明，由 TargetHost 在解析时按施法者当前坐标/朝向" +
                        "重新锚定（见 schema/README.md 判断记录）。"),
                new FieldSchema("filters", FieldKind.Array, required: false,
                    description: "Expr 文本或内置简写（relation:hostile|friendly|neutral|not_self、alive、" +
                        "tag:<id>）组成的数组；全部条件按 AND 组合（见 schema/README.md 判断记录）。"),
                new FieldSchema("sort_by", FieldKind.Object, required: false,
                    description: "{key: distance|hp_pct|threat|level, direction: asc|desc}；省略 direction 时按 asc。"),
                new FieldSchema("max_targets", FieldKind.Int, required: false,
                    description: "默认 1；0 表示不限。"),
                new FieldSchema("fallback", FieldKind.Reference, required: false, referenceTable: "target.chain_def",
                    description: "候选为空时改用的另一条链；不得成环（见 ChainDefValidationRule）。"),
            });
    }
}
