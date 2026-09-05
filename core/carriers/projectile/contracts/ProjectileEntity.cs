using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Core.Carriers.Projectile
{
    /// <summary>
    /// 飞行物运行期实体（见 05_对象模型与世界.md 第 1.4 节 <c>Projectile</c> 字段表）。只持有 05
    /// 明确列出的五个字段——飞行推进、命中判定、命中后效果回灌等运行期簿记（瞄准目标、飞行速度、
    /// 已飞行距离/时间、已命中单位集合、命中后效果列表、效果回灌出口……）不属于 05 定义的对象模型
    /// 字段，由 <see cref="ProjectileHost"/> 用内部字典管理（惯例同 <c>core/rules/combat</c>
    /// <c>ThreatTable</c>/<c>core/rules/skill</c> <c>AuraHost</c>——L3/L2 宿主为运行期簿记维护
    /// 私有状态，不塞进 <see cref="Entity"/> 子类本身）。
    /// </summary>
    public sealed class ProjectileEntity : Entity
    {
        public override string Kind => EntityKinds.Projectile;

        /// <summary>发射者（见 05 第 1.4 节 <c>sourceUnitId</c>）。</summary>
        public Id SourceUnitId { get; }

        /// <summary>关联的技能效果上下文，供命中后结算使用（见 05 第 1.4 节
        /// <c>skillContextId</c>）——对应发起本次投射物的 <c>EffectContext.SkillId</c>。</summary>
        public Id SkillContextId { get; }

        /// <summary>平面速度矢量（见 05 第 1.4 节 <c>velocity</c>）：<c>straight</c>/<c>arc</c>
        /// 飞行方式下构造后不再改变，<c>homing</c> 飞行方式下 <see cref="ProjectileHost"/> 每 tick
        /// 按当前瞄准目标位置重新计算并写回本属性。</summary>
        public Vec2 Velocity { get; set; }

        /// <summary>飞行方式（见 05 第 1.4 节 <c>travelMode</c>）：
        /// <c>straight</c>/<c>homing</c>/<c>arc</c>。</summary>
        public string TravelMode { get; }

        /// <summary>命中行为（见 05 第 1.4 节 <c>hitBehavior</c>）：
        /// <c>impact_on_first</c>/<c>pierce</c>/<c>impact_on_expiry</c>。</summary>
        public string HitBehavior { get; }

        /// <summary>高度偏移（见 05 第 3.3 节"附加在 Unit/Projectile 上的可选表现参数"），默认 0，
        /// 只供表现层渲染读取，不参与平面距离与碰撞计算——惯例同 <c>core/carriers/unit</c>
        /// <c>Unit.HeightOffset</c>。<c>arc</c> 飞行方式下 <see cref="ProjectileHost"/> 每 tick
        /// 按飞行进度写入一条抛物线曲线；<c>straight</c>/<c>homing</c> 飞行方式下恒为 0。</summary>
        public double HeightOffset { get; set; }

        public ProjectileEntity(
            Id entityId, Id mapId, Id sourceUnitId, Id skillContextId, string travelMode, string hitBehavior)
            : base(entityId, mapId)
        {
            SourceUnitId = sourceUnitId;
            SkillContextId = skillContextId;
            TravelMode = travelMode;
            HitBehavior = hitBehavior;
        }
    }
}
