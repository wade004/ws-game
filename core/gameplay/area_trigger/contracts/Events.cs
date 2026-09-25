using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json <c>area.trigger_entered</c>/<c>area.trigger_left</c>
    /// 两行）。惯例同 <c>Core.Carriers.Common.CarriersEventKeys</c>：模块自持一份常量。</summary>
    public static class AreaTriggerEventKeys
    {
        public static readonly Id TriggerEntered = new Id("area.trigger_entered");

        public static readonly Id TriggerLeft = new Id("area.trigger_left");
    }

    /// <summary>单位进入触发范围且满足 <c>condition</c>（<c>one_shot</c> 已触发的除外）时发出（见
    /// 05 第 7.1 节、found.event_catalog <c>area.trigger_entered</c> 行字段原文）。</summary>
    public sealed class AreaTriggerEnteredEvent : IEvent, IExprReadableEvent
    {
        public Id Key => AreaTriggerEventKeys.TriggerEntered;

        public Id TriggerId { get; }

        public Id UnitId { get; }

        public AreaTriggerEnteredEvent(Id triggerId, Id unitId)
        {
            TriggerId = triggerId;
            UnitId = unitId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "triggerId": value = ExprValue.OfId(TriggerId); return true;
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>
    /// ADR-0090（消费方第三十六批相邻缺口）：<see cref="AreaTriggerLeftEvent"/> 的离开成因分类——
    /// 单位真正移动出触发范围（<c>Moved</c>，此前唯一的隐含语义）、触发体所在区域被整体卸载
    /// （<c>Unloaded</c>，见 <see cref="IAreaTriggerHost.UnloadMap"/>/<see cref="IAreaTriggerHost.Unregister"/>
    /// 判断记录）、单位本身消失（<c>Despawned</c>，见 <c>AreaTriggerHost</c> 判断记录"卸载/消失补发
    /// 离开事件"——本版本未落地这一分支，见该判断记录"已知限制"）。
    /// </summary>
    public enum AreaTriggerLeaveReason
    {
        /// <summary>单位真正移动出触发范围（<see cref="AreaTriggerHost.Evaluate"/> 检测到几何位置不再
        /// 落在 <c>Shape</c> 内）。</summary>
        Moved,

        /// <summary>触发体所在区域被整体卸载（<see cref="IAreaTriggerHost.UnloadMap"/>/
        /// <see cref="IAreaTriggerHost.Unregister"/>），区域本身不存在了，单位因此不再处于其中。</summary>
        Unloaded,

        /// <summary>单位本身消失（未落地，见 <c>AreaTriggerHost</c> 判断记录"已知限制"：本模块没有
        /// 现成的"单位消失"检测点，不为此新建机制）。</summary>
        Despawned,
    }

    /// <summary>单位离开触发范围（此前已作为"进入"处理过）时发出（见 05 第 7.1 节、
    /// found.event_catalog <c>area.trigger_left</c> 行字段原文）。
    /// <para>
    /// ADR-0090 新增 <see cref="Reason"/>（ABI 只加法：新增只读属性 + 新增三参构造重载，旧二参构造
    /// 原样保留、转调三参构造并固定传 <see cref="AreaTriggerLeaveReason.Moved"/>——此前唯一的调用点
    /// <c>AreaTriggerHost.HandleLeave</c> 就是"移动出范围"这一种成因，行为不变）：区分"真正走出"与
    /// "区域被整体卸载"两类离开成因，供 <c>feedback.binding</c>/任务等订阅方按需过滤（如循环音效只想
    /// 响应真实走出，条件写 <c>event.reason == "moved"</c>，见 <c>AreaTriggerHost</c> 判断记录）。
    /// </para>
    /// </summary>
    public sealed class AreaTriggerLeftEvent : IEvent, IExprReadableEvent
    {
        public Id Key => AreaTriggerEventKeys.TriggerLeft;

        public Id TriggerId { get; }

        public Id UnitId { get; }

        /// <summary>见类型判断记录（ADR-0090）：离开成因分类，默认 <see cref="AreaTriggerLeaveReason.Moved"/>。</summary>
        public AreaTriggerLeaveReason Reason { get; }

        /// <summary>ADR-0090 之前的原始签名，保留不变（ABI 只加法）：恒发 <see cref="AreaTriggerLeaveReason.Moved"/>。</summary>
        public AreaTriggerLeftEvent(Id triggerId, Id unitId)
            : this(triggerId, unitId, AreaTriggerLeaveReason.Moved)
        {
        }

        public AreaTriggerLeftEvent(Id triggerId, Id unitId, AreaTriggerLeaveReason reason)
        {
            TriggerId = triggerId;
            UnitId = unitId;
            Reason = reason;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "triggerId": value = ExprValue.OfId(TriggerId); return true;
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                // 枚举名转小写字符串（惯例同 core/sim/core/FightRunner.cs HitResult 同类转发、
                // core/sim/core/CoverageSimulation.cs Category 同类转发：ToLowerInvariant 不依赖
                // 当前文化，确定性同本仓库其它 Expr 字符串字段）。
                case "reason": value = ExprValue.OfString(Reason.ToString().ToLowerInvariant()); return true;
                default: value = default; return false;
            }
        }
    }
}
