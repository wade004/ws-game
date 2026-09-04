using System.Collections.Generic;
using Core.Foundation.Expr;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// 本模块的 Expr 登记表出口（见任务书"Expr 键登记只暴露为本模块静态
    /// XxxExprSchemaEntries……不要改 RulesExprSchema"）。<see cref="LootEntry.Condition"/> 只使用既有
    /// <c>self</c>/<c>target</c>/<c>world</c>/<c>quest</c>/<c>player</c> 等分组（<c>self</c> =
    /// <c>context.KillerId ?? context.SourceUnitId</c>，<c>target</c> = <c>context.SourceUnitId</c>，
    /// 见 <see cref="LootHost"/> 判断记录），不新增任何 <c>group.key</c>，因此本类型是一份空登记表，
    /// 仅作为"本模块是否新增 Expr 引用"这一问题的显式、可发现的答案（惯例同任务书对
    /// "无新增分组"模块的要求），供组装层按统一约定扫描各模块的 <c>XxxExprSchemaEntries</c> 时不必
    /// 对本模块特殊处理。
    /// </summary>
    public static class LootExprSchemaEntries
    {
        public static readonly IReadOnlyList<(string Group, string Key, ExprSignature Signature)> Entries =
            System.Array.Empty<(string, string, ExprSignature)>();
    }
}
