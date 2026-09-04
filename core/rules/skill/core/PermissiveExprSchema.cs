using Core.Foundation.Expr;

namespace Core.Rules.Skill
{
    /// <summary>
    /// <see cref="IExprSchema"/> 的最小实现，供本模块解析 <c>skill.proc_def.condition</c> 一类
    /// Expr 字段使用。判断记录：04 第 6.2 节固定了九个宿主分组（self/target/event/world/quest/
    /// player/combat/enemies/time），但每个分组具体开放哪些 <c>key</c> 由各宿主自行登记，属于
    /// "集成任务，不在本任务范围内"（见 <c>core/rules/common/README.md</c>"不负责什么"第 4
    /// 条——<c>IExprHostFactory</c> 的具体实现属于集成任务）。本模块只需要能把
    /// <c>group.key(...)</c> 形式的点分标识符解析成 <see cref="ExprReferenceNode"/>（而不是被
    /// 误判成一个 Id 字面量），因此这里对九个已知分组一律放行、不区分具体 <c>key</c>——静态签名
    /// 校验（<c>ExprValidator</c>）不在本模块调用范围内，运行期真正的合法性由注入的
    /// <see cref="IExprHostFactory"/> 具体实现决定。
    /// </summary>
    internal sealed class PermissiveExprSchema : IExprSchema
    {
        public static readonly PermissiveExprSchema Instance = new PermissiveExprSchema();

        private static readonly ExprSignature AnySignature =
            new ExprSignature(ExprValueKind.Bool, System.Array.Empty<ExprValueKind>());

        public bool TryGetSignature(string group, string key, out ExprSignature signature)
        {
            if (ExprGroups.IsKnown(group))
            {
                signature = AnySignature;
                return true;
            }

            signature = default;
            return false;
        }
    }
}
