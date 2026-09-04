using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.Difficulty
{
    /// <summary>本模块发出的事件 key 常量（对应 found.event_catalog 登记表，见
    /// data/_sample/found/found.event_catalog.json <c>difficulty.applied</c> 行）。惯例同
    /// <c>core/gameplay/world_state</c> 的 <c>WorldStateEventKeys</c>：模块自持一份常量，不依赖
    /// 生成物。</summary>
    public static class DifficultyEventKeys
    {
        public static readonly Id Applied = new Id("difficulty.applied");
    }

    /// <summary>
    /// <see cref="IDifficultyHost.Apply"/> 应用成功后触发（见 found.event_catalog
    /// <c>difficulty.applied</c> 行字段表 <c>{scopeId, tierId}</c>、08 第 9 节契约汇总表）。
    /// <para>
    /// 判断记录（<see cref="ScopeId"/> 的取值约定）：found.event_catalog 只给出字段名
    /// <c>scopeId</c>，未规定 <see cref="DifficultyScope.Global"/> 时它应该是什么——任务书拍板
    /// 用固定哨兵 <see cref="DifficultyHost.GlobalScopeId"/>（<c>"diff.scope.global"</c>）表示
    /// 全局作用域，<see cref="DifficultyScope.Map"/> 时取实际 <c>mapId</c>，二者统一用 Id 承载，
    /// 不改成 <c>ExprValue</c>/字符串联合类型（Expr 无联合类型，见 <see cref="IExprReadableEvent"/>
    /// 类型注释同一惯例）。
    /// </para>
    /// </summary>
    public sealed class DifficultyAppliedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => DifficultyEventKeys.Applied;

        public Id ScopeId { get; }

        public Id TierId { get; }

        public DifficultyAppliedEvent(Id scopeId, Id tierId)
        {
            ScopeId = scopeId;
            TierId = tierId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "scopeId": value = ExprValue.OfId(ScopeId); return true;
                case "tierId": value = ExprValue.OfId(TierId); return true;
                default: value = default; return false;
            }
        }
    }
}
