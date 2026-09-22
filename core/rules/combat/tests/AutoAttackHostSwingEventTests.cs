using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Combat;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// ADR-0070（消费方反馈第十七批"框架原生普通攻击不广播任何事件，表现层永远进不了
    /// AnimState.Attack"根治）：<see cref="AutoAttackHost"/> 挥击广播（<see
    /// cref="AutoAttackSwingEvent"/>）的单元级用例——不经完整 <c>RulesAssembly</c>/<c>HeadlessWorld</c>
    /// 装配（那条端到端链路见 <c>core/sim/tests/AutoAttackHostIntegrationTests.cs</c> 新增用例），只
    /// 摆布 <see cref="AutoAttackHost"/> 自己的直接依赖（Fake <see cref="IUnitAccess"/>/<see
    /// cref="IAuraQuery"/> + 本文件自带的 Fake <see cref="IEffectSink"/>/<see cref="IWeaponDamageQuery"/>），
    /// 精确钉住"发布时机"（<c>ApplyEffect</c> 之前）、"被跳过的挥击不发事件"、"旧构造不发事件"这几条
    /// 判断记录。
    /// </summary>
    public class AutoAttackHostSwingEventTests
    {
        private static readonly Id Caster = new Id("unit.swing_caster");
        private static readonly Id Target = new Id("unit.swing_target");
        private static readonly Id Faction = new Id("fac.swing_test");
        private static readonly Id PhysicalSchool = new Id("school.swing_physical");

        /// <summary>记录调用顺序的共享探针——<see cref="RecordingEventBus.Enqueue"/> 与
        /// <see cref="RecordingEffectSink.ApplyEffect"/> 都往同一份列表里追加标签，用真实调用发生的
        /// 先后顺序钉住"挥击事件先于伤害结算"这条判断记录（不依赖读源码猜测顺序）。</summary>
        private static List<string> _callOrder = null!;

        private sealed class RecordingEventBus : IEventBus
        {
            private readonly IEventBus _inner;
            public RecordingEventBus(IEventBus inner) => _inner = inner;

            public SubscriptionHandle Subscribe(Id key, Core.Foundation.EventBus.EventHandler handler) => _inner.Subscribe(key, handler);
            public SubscriptionHandle Subscribe<T>(Id key, Core.Foundation.EventBus.EventHandler<T> handler) where T : IEvent => _inner.Subscribe(key, handler);

            public void Enqueue(IEvent evt)
            {
                _callOrder.Add("enqueue:" + evt.Key.Value);
                _inner.Enqueue(evt);
            }

            public int DispatchPending() => _inner.DispatchPending();
            public void PublishImmediate(IEvent evt) => _inner.PublishImmediate(evt);
        }

        private sealed class RecordingEffectSink : IEffectSink
        {
            public int ApplyEffectCallCount;

            public ResolveResult ApplyEffect(EffectContext context)
            {
                _callOrder.Add("apply_effect");
                ApplyEffectCallCount++;
                return new ResolveResult(HitResult.Hit, 10, 10, 0, false, false, null);
            }

            public AuraInstanceRef ApplyAura(Id targetId, Id auraDefId, Id sourceId, double? durationOverride = null) =>
                throw new NotSupportedException("AutoAttackHost 不调用 ApplyAura");

            public void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef) =>
                throw new NotSupportedException("AutoAttackHost 不调用 RemoveAura");
        }

        private sealed class FixedWeaponDamageQuery : IWeaponDamageQuery
        {
            public double? IntervalSeconds = 1.0;
            public Id? School = PhysicalSchool;

            public double GetWeaponBaseDamage(Id unitId) => 10.0;
            public double GetWeaponDps(Id unitId) => 10.0;
            public double? GetWeaponAttackIntervalSeconds(Id unitId) => IntervalSeconds;
            public Id? GetWeaponSchool(Id unitId) => School;
        }

        private sealed class Fixture
        {
            public IEventBus RealBus = null!;
            public RecordingEventBus Bus = null!;
            public FakeUnitAccess Units = null!;
            public FakeAuraQuery Auras = null!;
            public RecordingEffectSink EffectSink = null!;
            public FixedWeaponDamageQuery WeaponQuery = null!;
            public List<AutoAttackSwingEvent> CapturedSwings = null!;
        }

        private static Fixture Build()
        {
            _callOrder = new List<string>();
            var realBus = CombatTestSupport.MakeBus();
            var bus = new RecordingEventBus(realBus);

            var captured = new List<AutoAttackSwingEvent>();
            realBus.Subscribe<AutoAttackSwingEvent>(RulesEventKeys.CombatAutoAttackSwing, e => captured.Add(e));

            var units = new FakeUnitAccess().Add(Caster, Faction).Add(Target, Faction);
            var auras = new FakeAuraQuery();

            return new Fixture
            {
                RealBus = realBus,
                Bus = bus,
                Units = units,
                Auras = auras,
                EffectSink = new RecordingEffectSink(),
                WeaponQuery = new FixedWeaponDamageQuery(),
                CapturedSwings = captured,
            };
        }

        private static AutoAttackHost MakeHost(Fixture fx, IEventBus? bus) =>
            bus == null
                ? new AutoAttackHost(fx.Units, fx.Auras, fx.EffectSink, fx.WeaponQuery, PhysicalSchool)
                : new AutoAttackHost(
                    fx.Units, fx.Auras, fx.EffectSink, fx.WeaponQuery, PhysicalSchool,
                    bus: bus, attackIntervalFallback: null, diagnostics: null, options: null);

        [Fact]
        public void Update_SwingResolves_PublishesSwingEvent_WithSourceAndTargetId_BeforeApplyEffect()
        {
            var fx = Build();
            var host = MakeHost(fx, fx.Bus);
            host.SetTarget(Caster, Target);
            host.SetEnabled(Caster, true);

            host.Update(fx.WeaponQuery.IntervalSeconds!.Value);
            fx.RealBus.DispatchPending();

            Assert.Single(fx.CapturedSwings);
            Assert.Equal(Caster, fx.CapturedSwings[0].SourceId);
            Assert.Equal(Target, fx.CapturedSwings[0].TargetId);
            Assert.Equal(1, fx.EffectSink.ApplyEffectCallCount);

            // 顺序：挥击事件的 Enqueue 调用必须先于 ApplyEffect 调用（见 AutoAttackHost.Update 判断
            // 记录"事件发布时机"）。
            Assert.Equal(
                new[] { "enqueue:combat.auto_attack_swing", "apply_effect" },
                _callOrder);
        }

        [Fact]
        public void Update_OutOfRange_SkipsSwing_DoesNotPublishEvent_DoesNotApplyEffect()
        {
            var fx = Build();
            var host = MakeHost(fx, fx.Bus);
            host.Options.Range = 1.0;
            fx.Units.SetPosition(Caster, new Vec2(0, 0));
            fx.Units.SetPosition(Target, new Vec2(100, 0));
            host.SetTarget(Caster, Target);
            host.SetEnabled(Caster, true);

            host.Update(fx.WeaponQuery.IntervalSeconds!.Value);
            fx.RealBus.DispatchPending();

            Assert.Empty(fx.CapturedSwings);
            Assert.Equal(0, fx.EffectSink.ApplyEffectCallCount);
        }

        [Fact]
        public void Update_NoAttackControlFlag_SkipsSwing_DoesNotPublishEvent_DoesNotApplyEffect()
        {
            var fx = Build();
            fx.Auras.SetControlFlags(Caster, ControlFlags.NoAttack);
            var host = MakeHost(fx, fx.Bus);
            host.SetTarget(Caster, Target);
            host.SetEnabled(Caster, true);

            host.Update(fx.WeaponQuery.IntervalSeconds!.Value);
            fx.RealBus.DispatchPending();

            Assert.Empty(fx.CapturedSwings);
            Assert.Equal(0, fx.EffectSink.ApplyEffectCallCount);
        }

        [Fact]
        public void Update_MissingInterval_SkipsSwing_DoesNotPublishEvent()
        {
            var fx = Build();
            fx.WeaponQuery.IntervalSeconds = null;
            var host = MakeHost(fx, fx.Bus);
            host.SetTarget(Caster, Target);
            host.SetEnabled(Caster, true);

            host.Update(5.0);
            fx.RealBus.DispatchPending();

            Assert.Empty(fx.CapturedSwings);
            Assert.Equal(0, fx.EffectSink.ApplyEffectCallCount);
        }

        /// <summary>旧构造（不带 <c>bus</c> 参数）行为与本决策落地前完全一致：不发事件、不抛异常。</summary>
        [Fact]
        public void Update_LegacyConstructor_WithoutBus_DoesNotPublishEvent_StillAppliesEffect()
        {
            var fx = Build();
            var host = MakeHost(fx, bus: null);
            host.SetTarget(Caster, Target);
            host.SetEnabled(Caster, true);

            var ex = Record.Exception(() => host.Update(fx.WeaponQuery.IntervalSeconds!.Value));

            Assert.Null(ex);
            Assert.Empty(fx.CapturedSwings);
            Assert.Equal(1, fx.EffectSink.ApplyEffectCallCount);
        }

        [Fact]
        public void Update_RepeatedSwings_PublishesOneEventPerResolvedSwing()
        {
            var fx = Build();
            var host = MakeHost(fx, fx.Bus);
            host.SetTarget(Caster, Target);
            host.SetEnabled(Caster, true);

            host.Update(fx.WeaponQuery.IntervalSeconds!.Value);
            host.Update(fx.WeaponQuery.IntervalSeconds!.Value);
            host.Update(fx.WeaponQuery.IntervalSeconds!.Value);
            fx.RealBus.DispatchPending();

            Assert.Equal(3, fx.CapturedSwings.Count);
            Assert.Equal(3, fx.EffectSink.ApplyEffectCallCount);
            foreach (var swing in fx.CapturedSwings)
            {
                Assert.Equal(Caster, swing.SourceId);
                Assert.Equal(Target, swing.TargetId);
            }
        }
    }
}
