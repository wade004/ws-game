using Core.Foundation.Expr;

namespace Tests.Foundation.Expr
{
    /// <summary>
    /// 供 <see cref="ExprParserTests"/>/<see cref="ExprEvaluatorTests"/> 共用的默认登记表：
    /// 覆盖两个测试类里反复出现的 group.key。ADR-0015 之后 <c>ExprParser.Parse</c> 需要一份
    /// <see cref="IExprSchema"/> 才能判定点分标识符是引用还是 Id 字面量，因此几乎每个测试都
    /// 要传一份 schema——把公共部分收敛到这里，个别测试自己需要的特殊登记（或"故意不登记"
    /// 以制造 Id 字面量/歧义场景）在各自测试方法里另建一份 <see cref="ExprSchema"/>。
    /// </summary>
    internal static class TestSchema
    {
        public static ExprSchema Build() => new ExprSchema()
            .Register("self", "hp_pct", ExprValueKind.Number)
            .Register("self", "range", ExprValueKind.Number)
            .Register("self", "has_aura", ExprValueKind.Bool, ExprValueKind.Id)
            .Register("combat", "in_combat", ExprValueKind.Bool)
            .Register("target", "hp_pct", ExprValueKind.Number)
            .Register("target", "faction", ExprValueKind.Id)
            .Register("player", "level", ExprValueKind.Int)
            .Register("event", "school", ExprValueKind.String)
            .Register("event", "damage_amount", ExprValueKind.Int)
            .Register("time", "since_combat_start", ExprValueKind.Number)
            .Register("quest", "objective_progress", ExprValueKind.Int, ExprValueKind.Id, ExprValueKind.Int)
            .Register("quest", "a", ExprValueKind.Id)
            .Register("enemies", "count_in_range", ExprValueKind.Int, ExprValueKind.Number);
    }
}
