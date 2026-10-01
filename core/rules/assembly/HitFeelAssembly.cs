using System;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Combat;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Core.Rules.Assembly
{
    /// <summary>
    /// 已装配的局部顿帧与受击裁决：行动者动作时钟 + 受击裁决宿主 + 事件订阅的生命周期。释放时退订全部事件。
    /// </summary>
    public sealed class HitFeelSystem : IDisposable
    {
        private readonly IDisposable _clockSubscription;

        /// <summary>行动者动作时钟（随 <c>sim.tick_started</c>/<c>sim.tick_finished</c> 前进）。</summary>
        public ActorActionClock Clock { get; }

        public HitFeelHost Host { get; }

        internal HitFeelSystem(ActorActionClock clock, IDisposable clockSubscription, HitFeelHost host)
        {
            Clock = clock;
            _clockSubscription = clockSubscription;
            Host = host;
        }

        public void Dispose()
        {
            Host.Dispose();
            _clockSubscription.Dispose();
        }
    }

    /// <summary>
    /// 局部顿帧与受击裁决的装配（手感设计/03 第 3/4 节，ADR-0117）。<b>装配条件</b>：手感系统已装配（调用方持有
    /// <see cref="IFeelJudgingSource"/>）；没有手感系统时调用方不调用本类，既有行为逐位不变。
    /// <para>
    /// <b>订阅顺序</b>：先创建并 <c>Attach</c> 行动者动作时钟，再创建 <see cref="HitFeelHost"/>——两者都订阅
    /// <c>sim.tick_finished</c>，时钟先于宿主，宿主才读得到本 tick 末刚到期的冻结（<c>feel.hitstop_ended</c> 不晚一个 tick）。
    /// 本方法保证这个顺序；自行拼装的调用方需自己遵守。
    /// </para>
    /// <para>
    /// 打断口：<paramref name="skills"/> 非空时注册一个"硬直打断读条/引导"的适配（<see cref="ISkillHost.Interrupt"/>，不锁学派）；
    /// 动作时间线的打断口（<c>action.cancelled{reason: Stagger}</c>）由时间线切片经 <see cref="HitFeelHost.AddInterruptSink"/> 注册。
    /// 击退口（<see cref="HitFeelHost.Knockback"/>）由运动层装配（<c>MotionHitFeelWiring</c>）设置。
    /// </para>
    /// </summary>
    public static class HitFeelAssembly
    {
        public static HitFeelSystem Attach(
            IEventBus bus,
            IUnitAccess units,
            IFeelJudgingSource feel,
            IStatHost stats,
            double stepSeconds,
            HitFeelOptions? options = null,
            IPowerHost? powers = null,
            IActionStateQuery? actions = null,
            IAuraQuery? auras = null,
            ISkillHost? skills = null)
        {
            var clock = new ActorActionClock();
            var subscription = clock.Attach(bus);
            var host = new HitFeelHost(bus, units, feel, clock, stats, stepSeconds, options, powers, actions, auras);
            if (skills != null) host.AddInterruptSink(new SkillHostStaggerInterruptSink(skills));
            return new HitFeelSystem(clock, subscription, host);
        }
    }

    /// <summary>硬直打断读条/引导：转发到 <see cref="ISkillHost.Interrupt"/>（不锁学派；没有进行中的读条时无效果，幂等）。</summary>
    public sealed class SkillHostStaggerInterruptSink : IStaggerInterruptSink
    {
        private readonly ISkillHost _skills;

        public SkillHostStaggerInterruptSink(ISkillHost skills)
        {
            _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        }

        public void InterruptByStagger(Core.Foundation.Common.Id unitId, Core.Foundation.Common.Id sourceId)
        {
            if (_skills.IsCasting(unitId)) _skills.Interrupt(unitId, sourceId, null, 0.0);
        }
    }
}
