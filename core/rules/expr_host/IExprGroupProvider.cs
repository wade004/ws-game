using System.Collections.Generic;
using Core.Foundation.Expr;

namespace Core.Rules.ExprHost
{
    /// <summary>
    /// 集成任务定义的扩展点：<c>world</c>/<c>quest</c>/<c>player</c> 三个宿主引用分组（见 04 第
    /// 6.2 节九个分组）不属于规则层（<c>core/rules</c>）自己的职责范围——它们分别是场景/世界状态、
    /// 任务系统、玩家专属数据，这些概念在 L4 玩法层（或具体游戏层）才存在，<c>core/rules</c>
    /// 无法（也不应该）预先知道这些分组下有哪些 <c>key</c>。<see cref="RulesExprHostFactory"/> 把
    /// 这三个分组的查询整体委托给调用方按分组名注入的 <see cref="IExprGroupProvider"/>
    /// （<c>extraGroups</c> 构造参数），未注入时按 04 第 6.3 节"引用对象暂缺"的约定返回默认值并
    /// 记一条警告（每个缺失分组只记一次，避免同一场战斗/同一局跑下来警告刷屏）。
    /// </summary>
    public interface IExprGroupProvider
    {
        /// <summary>查询本分组下的一个 <c>key</c>；<paramref name="args"/> 为调用点书写的参数
        /// （已求值），签名（返回类型、参数类型）由调用方自己的 <see cref="IExprSchema"/> 或数据
        /// 校验流程约束，本接口不做任何类型层面的强制。</summary>
        ExprValue Query(string key, IReadOnlyList<ExprValue> args);
    }
}
