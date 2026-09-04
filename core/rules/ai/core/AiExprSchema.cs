using Core.Foundation.Expr;

namespace Core.Rules.Ai
{
    /// <summary>
    /// 本模块自有的 <see cref="IExprSchema"/>：供 <see cref="ExprParser.Parse"/> 解析
    /// <c>ai.rotation.entries[].condition</c> 与 <c>ai.behavior_profile.transitions</c> 里的
    /// Expr 文本时消歧"点分标识符是引用还是 Id 字面量"（见 04 第 6.3 节、ADR-0015）。
    /// <para>
    /// 判断记录：<see cref="IExprHostFactory"/> 的默认实现（把 self/target/combat/enemies 等分组
    /// 接到具体宿主契约上）属于集成任务，不在本任务范围内（见 <c>common/README.md</c>"不负责什么"）；
    /// 但 <c>ExprParser.Parse</c> 仍然要求一份 <see cref="IExprSchema"/> 才能完成解析（区分引用/字面量，
    /// 尤其是带参数列表的引用如 <c>enemies.count_in_range(5)</c>）。本类只登记 04 第 6.2 节
    /// "宿主引用分组"表格里与 AI 直接相关、且文档已给出示例引用的最小词汇集合，供本模块自己解析
    /// 内容作者写的 Expr 文本；不代表运行期 <see cref="IExprHostFactory"/> 的最终实现只支持这些
    /// key——运行期 Query 出口如何解释这些 key 由该实现决定，本类只影响"解析期"的语法消歧与本模块
    /// 自带的 <c>AiContentValidationRule</c> 静态校验，不影响求值语义。内容作者若使用了本表未登记的
    /// group.key 组合，解析会失败（无参引用会被当作 Id 字面量接受，带参数列表的引用会报错，见
    /// <c>ExprParser.ParseIdentTerm</c> 判断记录）——这是本任务范围内已知的词汇表覆盖面限制，
    /// 后续集成任务如需要扩展词汇表，直接在本类追加 <c>Register</c> 调用即可。
    /// </para>
    /// </summary>
    internal static class AiExprSchema
    {
        public static readonly IExprSchema Instance = Build();

        private static ExprSchema Build()
        {
            return new ExprSchema()
                .Register(ExprGroups.Self, "hp_pct", ExprValueKind.Number)
                .Register(ExprGroups.Target, "hp_pct", ExprValueKind.Number)
                .Register(ExprGroups.Target, "faction", ExprValueKind.Id)
                .Register(ExprGroups.Combat, "in_combat", ExprValueKind.Bool)
                .Register(ExprGroups.Combat, "cast_school", ExprValueKind.Id)
                .Register(ExprGroups.Enemies, "count_in_range", ExprValueKind.Int, ExprValueKind.Int)
                .Register(ExprGroups.Time, "since_combat_start", ExprValueKind.Number)
                .Register(ExprGroups.Time, "day_cycle", ExprValueKind.Number)
                .Register(ExprGroups.Time, "turn_index", ExprValueKind.Int)
                .Register(ExprGroups.Time, "round_index", ExprValueKind.Int)
                .Register(ExprGroups.Time, "is_my_turn", ExprValueKind.Bool);
        }
    }
}
