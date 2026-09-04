using System.Collections.Generic;
using Core.Foundation.Expr;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// 本模块新增的唯一 Expr 引用：<c>player.currency(currencyId): Int</c>（任务书拍板，供任务/成就/
    /// 对话条件按玩家当前某种货币数量判断，如"某货币余额 &gt;= 100 才能开启某选项"）。见任务书
    /// "Expr 键登记只暴露为本模块静态 XxxExprSchemaEntries……不要改 RulesExprSchema"。
    /// <para>
    /// 判断记录（<c>player</c> 分组的合并方式）：<c>player</c> 分组同时可能有其它模块（如
    /// <c>core/gameplay/quest</c>）登记的键（如 <c>player.quest_progress</c>），任务书"二选一"——
    /// 本模块提供 <see cref="ChainedExprGroupProvider"/>（"链式包装"选项）：把
    /// <see cref="PlayerCurrencyExprGroupProvider"/> 与其它模块的 <c>IExprGroupProvider"</c> 按顺序
    /// 串联，每个 provider 对自己不认识的 key 抛 <see cref="System.Collections.Generic.KeyNotFoundException"/>
    /// （见 <see cref="PlayerCurrencyExprGroupProvider"/> 类型注释），链继续尝试下一个；组装层若不想
    /// 使用本类型，也可以自己按同样约定写一个分发式 provider（"组装层用合成提供者"选项），二者等价，
    /// 任选其一。
    /// </para>
    /// </summary>
    public static class EconomyExprSchemaEntries
    {
        public static readonly IReadOnlyList<(string Group, string Key, ExprSignature Signature)> Entries = new[]
        {
            (ExprGroups.Player, "currency", new ExprSignature(ExprValueKind.Int, new[] { ExprValueKind.Id })),
        };

        /// <summary>把 <see cref="Entries"/> 登记进一份可编程 <see cref="ExprSchema"/>，供组装层需要
        /// 独立/合并 schema 时复用（惯例同
        /// <c>core/gameplay/world_state.WorldExprSchemaEntries.RegisterInto</c>）。</summary>
        public static ExprSchema RegisterInto(ExprSchema schema)
        {
            foreach (var (group, key, signature) in Entries)
            {
                schema.Register(group, key, signature.ReturnKind, ToArray(signature));
            }

            return schema;
        }

        private static ExprValueKind[] ToArray(ExprSignature signature)
        {
            var args = new ExprValueKind[signature.ArgKinds.Count];
            for (var i = 0; i < args.Length; i++)
            {
                args[i] = signature.ArgKinds[i];
            }

            return args;
        }
    }
}
