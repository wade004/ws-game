using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;
using Core.Rules.Combat;
using Core.Rules.Skill;

namespace Core.Rules.Assembly
{
    /// <summary>
    /// 规则层手感接线的装配选项（手感落地 S10）。<b>缺省不启用</b>：不把本对象传给任何装配入口，既有行为逐位不变。
    /// 载体层有一个继承本类的 <c>CarriersFeelOptions</c>，补上运动层与输入映射相关的成员。
    /// </summary>
    public class RulesFeelOptions
    {
        /// <summary>
        /// 模拟固定步长（秒）：毫秒到 tick 的换算、动作时间线的 <c>SkillOptions.ActionStepSeconds</c>、受击裁决与输入缓冲都用它，
        /// 必须与 <c>SimLoopOptions.StepSeconds</c> 相等。<c>null</c> 时取 <c>SkillOptions.ActionStepSeconds</c>
        /// （经 <c>GameplayAssembly</c> 装配时取时钟宿主的步长，并校验与显式值一致）。
        /// </summary>
        public double? StepSeconds { get; set; }

        /// <summary>受击裁决策略；<c>null</c> 用 <see cref="HitFeelOptions"/> 的缺省值。装配时只补全其中为空的 <c>IsTimelineSkill</c>/<c>IsDiscreteMode</c>。</summary>
        public HitFeelOptions? HitFeel { get; set; }

        /// <summary>是否处于离散（回合制）时间模型；经 <c>GameplayAssembly</c> 装配时由它按时钟宿主模式填入，缺省 null 即连续。</summary>
        public Func<bool>? IsDiscreteMode { get; set; }

        /// <summary>
        /// 时间线 <c>hit</c> 标记的命中解析钩子（自定义实现）。<b>缺省 null 即由施法管线按技能数据选内建路径</b>（链声明 <c>shape</c> 走空间命中，
        /// 否则 instant 式结算），两种内建路径与自定义钩子的命中都由时间线路径自己发一条 <c>combat.hit_confirmed</c>（含顿帧/受击裁决字段，
        /// 由装配接入的 <see cref="TimelineServices.HitFeel"/> 填写）；装配无条件把 <c>HitFeelOptions.IsTimelineSkill</c> 接到
        /// <see cref="SkillHost.IsTimelineSkill"/>，受击裁决的 instant 适配器只处理非时间线技能，避免同一次命中重复确认。
        /// </summary>
        public ITimelineHitResolver? TimelineHitResolver { get; set; }
    }

    /// <summary>已装配的规则层手感接线：共用的动作时钟、受击裁决、时间线协作者。</summary>
    public sealed class RulesFeelSystem : IDisposable
    {
        /// <summary>全装配唯一的动作解析器（经 <see cref="InvalidatingActionFeelResolver"/> 包装的那一个）。</summary>
        public IFeelResolver Resolver { get; }

        public HitFeelSystem HitFeel { get; }

        /// <summary>全装配唯一的行动者动作时钟（输入缓冲、时间线、局部顿帧、运动层共用同一个实例）。</summary>
        public ActorActionClock Clock => HitFeel.Clock;

        /// <summary>注入给动作时间线的协作者对象（装配层随后经属性赋值补 <c>Input</c>/<c>Binding</c>，管线持有的是同一个对象引用）。</summary>
        public TimelineServices Timeline { get; }

        public double StepSeconds { get; }

        internal RulesFeelSystem(IFeelResolver resolver, HitFeelSystem hitFeel, TimelineServices timeline, double stepSeconds)
        {
            Resolver = resolver;
            HitFeel = hitFeel;
            Timeline = timeline;
            StepSeconds = stepSeconds;
        }

        public void Dispose() => HitFeel.Dispose();
    }

    /// <summary>
    /// 规则层手感接线（手感落地 S10）：局部顿帧与受击裁决（<see cref="HitFeelAssembly.Attach"/>，它创建的动作时钟即全装配唯一的时钟）、
    /// 动作时间线的协作者（<see cref="SkillHost.AttachTimelineServices"/>）、移动输入通知（<see cref="TimelineMoveIntentTickHandler"/>）。
    /// <para>
    /// 判断记录（受击打断时间线）：<see cref="HitFeelAssembly.Attach"/> 传入技能宿主后会注册 <see cref="SkillHostStaggerInterruptSink"/>，
    /// 它调用 <see cref="SkillHost.Interrupt"/>；对时间线动作，该入口终止动作并发 <c>action.cancelled{Stagger}</c> 与 <c>skill.cast_interrupted</c>
    /// （S3a 判断记录），所以"时间线经 AddInterruptSink 注册打断口"这一项由本接线满足，不另写第二个适配。
    /// </para>
    /// <para>
    /// 判断记录（为什么是静态接线而不是 <see cref="RulesAssembly"/> 的构造参数）：<see cref="RulesAssembly"/> 的构造函数已经历多轮追加参数，
    /// 再加一个会是第 23 个；解析器本身要读装备/生物模板（载体层数据）才能装配，规则层装配时拿不到。所以由持有解析器的上层
    /// （<c>CarriersAssembly</c>）在构造完成后调用本方法，行为与"构造期传入"等价，且不改任何物理签名。
    /// </para>
    /// </summary>
    public static class RulesFeelAssembly
    {
        /// <summary>
        /// 把手感机制接进 <paramref name="rules"/>。<paramref name="resolver"/> 应已是 <see cref="InvalidatingActionFeelResolver"/> 包装后的解析器
        /// （由调用方保证；本方法不再二次包装）。每个 <see cref="RulesAssembly"/> 只应调用一次。
        /// </summary>
        public static RulesFeelSystem Attach(RulesAssembly rules, IFeelResolver resolver, RulesFeelOptions options, double stepSeconds)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!double.IsFinite(stepSeconds) || stepSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepSeconds), "手感接线的步长必须是正的有限数");
            }

            var hitOptions = options.HitFeel ?? new HitFeelOptions();
            if (hitOptions.IsDiscreteMode == null && options.IsDiscreteMode != null)
            {
                hitOptions.IsDiscreteMode = options.IsDiscreteMode;
            }

            // 时间线技能的命中（缺省空间命中、链无 shape 的 instant 结算、自定义钩子、投射物命中钩子）都由时间线路径自己发一条
            // combat.hit_confirmed 并做无敌前置检查，所以无条件让受击裁决的 instant 适配器放过时间线技能（S11 订正 S10 判断记录 4）。
            if (hitOptions.IsTimelineSkill == null)
            {
                var skill = rules.Skill;
                hitOptions.IsTimelineSkill = skillId => skillId.HasValue && skill.IsTimelineSkill(skillId.Value);
            }

            var hitFeel = HitFeelAssembly.Attach(
                rules.Bus, rules.Units, resolver, rules.Stats, stepSeconds, hitOptions, rules.Powers,
                rules.Skill.ActionStateQuery, rules.Skill.AuraQuery, rules.Skill);

            var timeline = new TimelineServices
            {
                Clock = hitFeel.Clock,
                Feel = resolver,
                HitResolver = options.TimelineHitResolver,
                HitFeel = hitFeel.Host,
            };
            rules.Skill.AttachTimelineServices(timeline);

            // 移动输入通知：紧随 SkillTickHandler 之后（同一阶段按注册顺序执行）。
            rules.World.RegisterPhaseHandler(TickPhase.SkillPipeline, new TimelineMoveIntentTickHandler(rules.Skill));

            return new RulesFeelSystem(resolver, hitFeel, timeline, stepSeconds);
        }
    }

    /// <summary>
    /// 动作开始/结束时使该单位的手感缓存失效的解析器包装（手感设计/05 第 8 节"动作层随动作开始/结束变化"）。
    /// <para>
    /// 判断记录（为什么要包装）：<see cref="IFeelResolver.BeginAction"/>/<see cref="IFeelResolver.EndAction"/> 本身不使缓存失效，而判定型消费者
    /// （受击裁决读攻击方的顿帧毫秒、运动层读加速度）读的是缓存的 <see cref="IFeelResolver.Resolve"/>，进行中动作的"攻击期间武器临时覆盖"
    /// 与动作层手感引用只有在缓存按"是否在动作中"重算后才对它们可见。动作时间线（<c>CastPipeline</c>）不依赖手感模块的失效事件，
    /// 也不该为此改动，所以在装配根用这个包装在 Begin/End 两端失效——失效放在底层调用<b>之后</b>，因为时间线在 BeginAction 返回之后才登记进行中动作、
    /// 在 EndAction 之前已移除，下一次重算读到的动作状态才是新的（S3a 的调用顺序，见 <c>CastPipeline.Timeline</c>）。
    /// </para>
    /// </summary>
    public sealed class InvalidatingActionFeelResolver : IFeelResolver
    {
        private readonly IFeelResolver _inner;
        private readonly Dictionary<Id, Id> _unitByInstance = new Dictionary<Id, Id>();

        public InvalidatingActionFeelResolver(IFeelResolver inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>被包装的解析器。</summary>
        public IFeelResolver Inner => _inner;

        public FeelFieldSet Fields => _inner.Fields;

        public FeelCalibration Calibration => _inner.Calibration;

        public JudgingFeelView ResolveJudging(Id unitId) => _inner.ResolveJudging(unitId);

        public PresentingFeelView ResolvePresenting(Id unitId) => _inner.ResolvePresenting(unitId);

        public ResolvedFeel Resolve(Id unitId) => _inner.Resolve(unitId);

        public void Invalidate(Id unitId, string reason) => _inner.Invalidate(unitId, reason);

        public void InvalidateAll(string reason) => _inner.InvalidateAll(reason);

        public IReadOnlyList<FeelProvenanceEntry> GetProvenance(Id unitId, string field) => _inner.GetProvenance(unitId, field);

        public int GetVersion(Id unitId) => _inner.GetVersion(unitId);

        public ResolvedFeel BeginAction(Id unitId, Id castInstanceId, string? actionFeelRef)
        {
            var snapshot = _inner.BeginAction(unitId, castInstanceId, actionFeelRef);
            _unitByInstance[castInstanceId] = unitId;
            _inner.Invalidate(unitId, "action_begin");
            return snapshot;
        }

        public ResolvedFeel? GetSnapshot(Id castInstanceId) => _inner.GetSnapshot(castInstanceId);

        public void EndAction(Id castInstanceId)
        {
            _inner.EndAction(castInstanceId);
            if (_unitByInstance.Remove(castInstanceId, out var unitId))
            {
                _inner.Invalidate(unitId, "action_end");
            }
        }
    }
}
