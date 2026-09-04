using Core.Foundation.Expr;

namespace Core.Rules.Targeting
{
    /// <summary>
    /// <c>target.chain_def.filters</c> 里 Expr 文本解析用的登记表（见 06 第 5 节"filters:
    /// List&lt;FilterExpr&gt;"、任务书"Expr 过滤经 IExprHostFactory.CreateFor(caster, candidate,
    /// null) 求值"——caster 绑定到 <c>self</c> 分组、candidate 绑定到 <c>target</c> 分组）。
    /// <para>
    /// 契约缺口判断记录（见 targeting/README.md）：<c>ExprParser.Parse</c> 强制要求一个
    /// <see cref="IExprSchema"/> 才能区分"点分标识符是宿主引用还是 Id 字面量"（ADR-0015），但
    /// <see cref="Core.Rules.Common.IExprHostFactory"/> 只声明了"怎么拿到一个绑定了具体上下文的
    /// <see cref="IExprHost"/>"，没有配套声明"这个宿主开放哪些 group.key 签名"的静态登记表——
    /// 具体把 self/target 分组接到哪些查询 key 属于集成任务（见 IExprHostFactory.cs 判断记录），
    /// 本模块此刻并不知道游戏层会往 self/target 下挂哪些 key。本类型的取舍：只要 group 是
    /// <c>self</c> 或 <c>target</c> 就一律判定为"合法引用"（不关心具体 key、不关心声明的返回类型——
    /// <see cref="ExprEvaluator"/> 求值时只看 <see cref="IExprHost.Query"/> 的实际返回值，从不读取
    /// 这里登记的 <see cref="ExprSignature.ReturnKind"/>），其余七个分组
    /// （event/world/quest/player/combat/enemies/time）与任何其它点分标识符一律按 ADR-0015 落回
    /// Id 字面量分类，不在本模块的过滤语境里生效。
    /// </para>
    /// </summary>
    internal sealed class TargetFilterExprSchema : IExprSchema
    {
        public bool TryGetSignature(string group, string key, out ExprSignature signature)
        {
            if (group == ExprGroups.Self || group == ExprGroups.Target)
            {
                // ReturnKind/ArgKinds 只是占位——见本类型上方判断记录，ExprParser/ExprEvaluator 均
                // 不消费这里的具体取值，只用 TryGetSignature 的布尔返回值做归类判定。
                signature = new ExprSignature(ExprValueKind.Number, System.Array.Empty<ExprValueKind>());
                return true;
            }

            signature = default;
            return false;
        }
    }
}
