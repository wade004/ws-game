using Core.Foundation.Common;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// ADR-0051：<see cref="Core.Carriers.Common.ICreatureInteractionHost.Interact"/> 分发到对话系统
    /// 时的 L4 回调（见 <see cref="CreatureInteractOptions.GossipOpener"/>）。判断记录（参数命名对齐
    /// ADR-0044 <c>Core.Carriers.Gobj.DialogOpenerWithSourceDelegate(Id unitId, Id gobjInstanceId, Id
    /// dialogRef)</c> 的委托形状，但如实反映本委托携带的是生物实例身份而非 gobj 实例身份）：
    /// <paramref name="creatureInstanceId"/> 是触发本次交互的生物实例自身 id（<c>CreatureUnit</c> 的
    /// <c>EntityId</c>），供调用方把它当作一个稳定、真实的"交互对象"身份转发给下游（如装配根接线
    /// <c>DialogHost.OpenGossip(unitId, creatureInstanceId, dialogRef)</c>），不需要像 ADR-0044 之前
    /// 的 gobj 那样拿 <paramref name="dialogRef"/>（多个生物可能共用同一份菜单）顶替。
    /// </summary>
    public delegate void CreatureGossipOpenerDelegate(Id unitId, Id creatureInstanceId, Id dialogRef);

    /// <summary>
    /// <see cref="Core.Carriers.Creature.CreatureInteractionHost"/> 的口味配置项 + L4 回调注入点（惯例
    /// 同 <c>core/carriers/gobj</c> 的 <c>GobjOptions</c>：<c>dialog.gossip_menu</c> 分发是 L4 玩法层
    /// 职责，本模块——L3 载体层——不得直接引用它，改由游戏组装根注入；未注入时按 <see
    /// cref="Core.Carriers.Creature.CreatureInteractionHost"/> 顶部判断记录降级并记诊断，不静默）。
    /// </summary>
    public sealed class CreatureInteractOptions
    {
        /// <summary><see cref="Core.Carriers.Creature.CreatureInteractionHost.Interact"/> 允许的最大
        /// 交互距离（惯例同 <c>Core.Carriers.Gobj.GobjOptions.InteractRange</c>），默认 2。</summary>
        public double InteractRange { get; set; } = 2;

        /// <summary>见 <see cref="CreatureGossipOpenerDelegate"/>；未注入时生物的 <c>gossip_menu_ref</c>
        /// 分发失败（记诊断，返回 <see cref="Core.Carriers.Common.InteractOutcome.NoAction"/>，
        /// <c>Success=false</c>）。</summary>
        public CreatureGossipOpenerDelegate? GossipOpener { get; set; }
    }
}
