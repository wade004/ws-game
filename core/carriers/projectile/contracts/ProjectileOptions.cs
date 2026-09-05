using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Projectile
{
    /// <summary>
    /// <see cref="ProjectileHost"/> 的口味配置项（惯例同 <c>core/carriers/unit</c> 的
    /// <c>MovementOptions</c>）：命中判定用哪些空间索引标签、效果参数缺省时的兜底数值、离散模式
    /// 下每 tick 的等效秒数。具体一发投射物的飞行方式/命中行为/速度/射程等由
    /// <c>projectile</c> 效果的 <c>EffectContext.Params</c> 逐次指定（见
    /// <c>core/carriers/projectile/README.md</c>"参数字段"一节），本类只提供数据未显式给出时的
    /// 全局默认值，不是"这一发投射物"的配置。
    /// </summary>
    public sealed class ProjectileOptions
    {
        /// <summary>离散模式（ADR-0013）下每 tick 的等效秒数，用于换算飞行距离（惯例同
        /// <c>MovementOptions.DiscreteTurnEquivalentSeconds</c>：复用"速度 × 时间"的位移公式，不
        /// 为离散模式另写一套）。默认 1.0。</summary>
        public double DiscreteTickEquivalentSeconds { get; set; } = 1.0;

        /// <summary><c>params.speed</c> 缺失时的缺省飞行速度（单位距离/秒）。默认 10。</summary>
        public double DefaultSpeed { get; set; } = 10.0;

        /// <summary><c>params.max_range</c> 缺失时的缺省最大射程：超过该距离仍未命中/到期则视为
        /// 未命中并销毁（见 README"未命中处理"）。默认 30。</summary>
        public double DefaultMaxRange { get; set; } = 30.0;

        /// <summary><c>params.arc_height</c> 缺失时的缺省抛物线峰值高度（仅 <c>arc</c> 飞行方式使用，
        /// 纯表现参数，即 05 第 3.3 节"高度偏移"，不参与平面距离与碰撞计算）。默认 2。</summary>
        public double DefaultArcHeight { get; set; } = 2.0;

        /// <summary><c>params.impact_radius</c> 缺失时的缺省命中判定半径（<c>impact_on_expiry</c>
        /// 命中行为到期时按此半径查询范围内单位，见 README"命中行为"一节）。默认 1。</summary>
        public double DefaultImpactRadius { get; set; } = 1.0;

        /// <summary>命中判定（<see cref="Core.Foundation.EngineAdapter.ISpatialQuery"/> 查询）要求
        /// 候选对象携带的标签：默认只命中打了 <c>"unit"</c> 标签的对象（<c>CarriersAssembly.
        /// DefaultSpatialSyncKinds</c> 给 <c>creature</c>/<c>player</c> 两类 Unit 默认打此标签），
        /// 不会命中 <c>gobj</c> 等未打该标签的对象（同 <c>CarriersAssembly.DefaultSpatialSyncKinds</c>
        /// 判断记录"避免'最近敌人'捞到物件"的同一顾虑）。</summary>
        public IReadOnlyList<string> HitQueryTags { get; set; } = new[] { "unit" };

        /// <summary>命中判定的"已到达/已越过路点"距离阈值（惯例同 <c>MovementOptions.
        /// ArrivalEpsilon</c>）。默认 0.01。</summary>
        public double ArrivalEpsilon { get; set; } = 0.01;
    }
}
