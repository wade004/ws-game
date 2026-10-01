using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    // 时间线空间命中的跨模块契约（手感设计/03 第 2.2/2.5 节，ADR-0114）：规则层（L2）的施法管线只认接口，
    // 载体层（L3）的投射物宿主与单位访问实现它们。

    /// <summary>
    /// 单位朝向写入口（目标辅助的朝向修正落地用，手感设计/02 第 5 节）。不放进 <see cref="IUnitAccess"/>：
    /// 与 <c>SetLevel</c>/<c>SetFaction</c> 同一惯例——这不是 skill/combat/targeting/ai 经 <see cref="IUnitAccess"/>
    /// 契约通用的操作，只有声明了目标辅助的时间线动作会用到；<c>WorldUnitAccess</c> 实现本接口，施法管线对
    /// <see cref="IUnitAccess"/> 做 <c>is</c> 判断（没有实现本接口的假实现下目标辅助的朝向修正不落地，事件仍发，见 skill 模块 README）。
    /// </summary>
    public interface IUnitFacingWriter
    {
        /// <summary>写入朝向（弧度，同 <see cref="IUnitAccess.GetFacing"/>）。</summary>
        void SetFacing(Id unitId, double facing);
    }

    /// <summary>投射物的结局原因（<see cref="IProjectileHitHook.OnEnded"/>）。</summary>
    public enum ProjectileEndReason
    {
        /// <summary>命中单位后销毁（<c>impact_on_first</c> 或穿透次数耗尽）。</summary>
        Hit,

        /// <summary>被地形挡住。</summary>
        Blocked,

        /// <summary>射程耗尽/到期（<c>impact_on_expiry</c> 在此刻结算范围效果；其余命中行为到期即未命中）。</summary>
        Expired,

        /// <summary>被清场（离开地图、世界清空）。</summary>
        Cleared,
    }

    /// <summary>投射物命中一个单位时交给 <see cref="IProjectileHitHook"/> 的上下文。</summary>
    public readonly struct ProjectileHitInfo
    {
        public Id SourceId { get; }

        public Id TargetId { get; }

        public Id? SkillId { get; }

        /// <summary>碰撞点：投射物本 tick 飞行线段与目标最近的点（线段上离目标位置最近的点）。</summary>
        public Vec2 ContactPoint { get; }

        /// <summary>投射物飞行方向（单位向量；击退方向的替代来源）。</summary>
        public Vec2 FlightDirection { get; }

        /// <summary>本发投射物此前已穿透命中的单位数（本次命中前）。</summary>
        public int PierceCount { get; }

        public ProjectileHitInfo(Id sourceId, Id targetId, Id? skillId, Vec2 contactPoint, Vec2 flightDirection, int pierceCount)
        {
            SourceId = sourceId;
            TargetId = targetId;
            SkillId = skillId;
            ContactPoint = contactPoint;
            FlightDirection = flightDirection;
            PierceCount = pierceCount;
        }
    }

    /// <summary>
    /// 投射物命中钩子（手感设计/03 第 2.5 节）：时间线 <c>release</c> 标记发射的投射物，到达/碰撞时的结算沿用发射它的动作
    /// 的攻击实例 id、无敌前置检查与 <c>combat.hit_confirmed</c>（<c>contactPoint</c> 取碰撞点）。钩子随
    /// <see cref="IProjectileSpawner.Spawn(EffectContext, IEffectSink, IProjectileHitHook)"/> 交给投射物宿主，宿主在命中后效果
    /// 回灌的前后各调用一次；钩子持有的发射动作上下文与动作本身的存续无关（动作被打断/结束不影响已发射的投射物）。
    /// </summary>
    public interface IProjectileHitHook
    {
        /// <summary>
        /// 命中后效果回灌之前调用。返回 false 表示这次命中被回避（目标处于无敌窗口）：宿主不回灌效果、不计穿透次数、
        /// 投射物继续飞行（回避类结局由钩子自己发布 <c>combat.attack_avoided</c> 与 <c>combat.hit_confirmed</c>）。
        /// 返回 true 时 <paramref name="attackInstanceId"/> 是本次回灌效果要戳在 <see cref="EffectContext.AttackInstanceId"/> 上的攻击实例 id。
        /// </summary>
        bool BeforeHit(in ProjectileHitInfo info, out Id attackInstanceId);

        /// <summary>命中后效果回灌完成之后调用，<paramref name="results"/> 是每个命中后效果的结算结果（按声明顺序）。</summary>
        void AfterHit(in ProjectileHitInfo info, Id attackInstanceId, IReadOnlyList<ResolveResult> results);

        /// <summary>
        /// 投射物生成之后调用一次（默认空实现，既有实现者不受影响）。时间线投射物据此发 <c>action.projectile_launched</c>。
        /// </summary>
        void OnLaunched()
        {
        }

        /// <summary>
        /// 投射物结局确定、被销毁时调用一次（默认空实现）：命中后销毁/穿透耗尽（<see cref="ProjectileEndReason.Hit"/>）、被地形挡住
        /// （<see cref="ProjectileEndReason.Blocked"/>）、射程耗尽或到期（<see cref="ProjectileEndReason.Expired"/>）、被清场
        /// （<see cref="ProjectileEndReason.Cleared"/>）。与 <see cref="OnLaunched"/> 一一配对。
        /// </summary>
        void OnEnded(ProjectileEndReason reason)
        {
        }
    }
}
