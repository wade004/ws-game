using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// T-L14（测试覆盖剩余项 2026-10-01）：<see cref="ProcHost"/> 的直接用例。此前只经完整 <c>SkillWorld</c> 装配、
    /// 靠光环 <c>proc_trigger</c> 效果间接触发；这里直接构造宿主，用脚本化随机源/记录型回调钉死：
    /// 构造参数校验、命中率门槛（<c>roll &gt;= chance</c> 即不触发）、内部冷却的消耗与推进、
    /// <c>Detach</c> 两个重载的差异、<c>RescaleAll</c> 对剩余冷却与后续冷却的折算、回调返回值对事件的影响。
    /// </summary>
    public class ProcHostDirectTests
    {
        private static readonly Id Holder = new Id("unit.ph_holder");
        private static readonly Id Stranger = new Id("unit.ph_stranger");
        private static readonly Id Instance = new Id("aura.ph_instance");
        private static readonly Id ProcA = new Id("skill.proc_def.ph_a");
        private static readonly Id ProcB = new Id("skill.proc_def.ph_b");
        private static readonly Id TriggerSkill = new Id("skill.ph_trigger");
        private static readonly Id School = new Id("school.ph");

        private sealed class ScriptedRng : IRngHost
        {
            public readonly Queue<double> Rolls = new Queue<double>();
            public double Default = 0.0;
            public int Calls;
            public Id? LastStream;

            public double Next(Id stream)
            {
                Calls++;
                LastStream = stream;
                return Rolls.Count > 0 ? Rolls.Dequeue() : Default;
            }

            public int NextInt(Id stream, int min, int max) => throw new NotSupportedException();
            public RngStreamState GetStreamState(Id stream) => throw new NotSupportedException();
            public void SetStreamState(Id stream, RngStreamState state) => throw new NotSupportedException();
            public IReadOnlyList<Id> Streams => Array.Empty<Id>();
            public void Reset(ulong masterSeed) => throw new NotSupportedException();
            public ulong MasterSeed => 0UL;
        }

        private sealed class Fixture
        {
            public EventBus Bus = null!;
            public ScriptedRng Rng = new ScriptedRng();
            public SkillOptions Options = new SkillOptions();
            public List<(Id caster, Id skill, int targets, int depth)> Casts = new List<(Id, Id, int, int)>();
            public bool CastResult = true;
            public List<ProcTriggeredEvent> Procs = new List<ProcTriggeredEvent>();
            public ProcHost Host = null!;
        }

        private static Fixture Build()
        {
            var fx = new Fixture();
            fx.Bus = new EventBus(
                EventCatalog.FromDefinitions(new[]
                {
                    new EventDefinition(RulesEventKeys.CombatDamageDealt, "combat",
                        new[] { "sourceId", "targetId", "school", "amount", "isCrit", "hitResult" }),
                    new EventDefinition(RulesEventKeys.CombatEntered, "combat", new[] { "unitId", "hostileId" }),
                    new EventDefinition(RulesEventKeys.ProcTriggered, "skill", new[] { "unitId", "procDefId", "triggerSkillId" }),
                }),
                new EventBusOptions { StrictCatalog = false });
            fx.Bus.Subscribe<ProcTriggeredEvent>(RulesEventKeys.ProcTriggered, e => fx.Procs.Add(e));
            fx.Host = new ProcHost(
                fx.Bus, fx.Rng, new FakeExprHostFactory(), fx.Options, new InMemorySkillDiagnostics(),
                (caster, skill, targets, depth) =>
                {
                    fx.Casts.Add((caster, skill, targets.Count, depth));
                    return fx.CastResult;
                });
            return fx;
        }

        private static ProcDef Def(Id id, double chance = 1.0, double? icd = null, Id? triggerEvent = null) =>
            new ProcDef(id, triggerEvent ?? RulesEventKeys.CombatDamageDealt, null, TriggerSkill, icd, chance);

        private static CombatDamageDealtEvent Damage(Id source, int chainDepth = 0) =>
            new CombatDamageDealtEvent(source, new Id("unit.ph_victim"), School, 1, false, HitResult.Hit, triggerChainDepth: chainDepth);

        private static void Fire(Fixture fx, IEvent evt)
        {
            fx.Bus.PublishImmediate(evt);
            fx.Bus.DispatchPending();
        }

        [Fact]
        public void Constructor_RejectsEachNullDependency()
        {
            var bus = Build().Bus;
            var rng = new ScriptedRng();
            var exprs = new FakeExprHostFactory();
            var options = new SkillOptions();
            var diag = new InMemorySkillDiagnostics();
            ProcHost.TriggerCastCallback cb = (c, s, t, d) => true;

            Assert.Throws<ArgumentNullException>(() => new ProcHost(null!, rng, exprs, options, diag, cb));
            Assert.Throws<ArgumentNullException>(() => new ProcHost(bus, null!, exprs, options, diag, cb));
            Assert.Throws<ArgumentNullException>(() => new ProcHost(bus, rng, null!, options, diag, cb));
            Assert.Throws<ArgumentNullException>(() => new ProcHost(bus, rng, exprs, null!, diag, cb));
            Assert.Throws<ArgumentNullException>(() => new ProcHost(bus, rng, exprs, options, null!, cb));
            Assert.Throws<ArgumentNullException>(() => new ProcHost(bus, rng, exprs, options, diag, null!));
        }

        [Fact]
        public void AttachedProc_FiresForHolderEvent_WithEmptyTargetsAndChainDepthFromEvent()
        {
            var fx = Build();
            fx.Host.Attach(Instance, Holder, Def(ProcA));

            Fire(fx, Damage(Holder, chainDepth: 2));

            var cast = Assert.Single(fx.Casts);
            Assert.Equal(Holder, cast.caster);
            Assert.Equal(TriggerSkill, cast.skill);
            Assert.Equal(0, cast.targets);
            Assert.Equal(2, cast.depth);

            // 触发成功后发布 proc.triggered，链深度 +1，用于下一层触发的递归上限判定。
            var proc = Assert.Single(fx.Procs);
            Assert.Equal(Holder, proc.UnitId);
            Assert.Equal(ProcA, proc.ProcDefId);
            Assert.Equal(TriggerSkill, proc.TriggerSkillId);
            Assert.Equal(3, proc.TriggerChainDepth);
            Assert.Equal(fx.Options.RngStream, fx.Rng.LastStream);
        }

        [Fact]
        public void EventNotRelatedToHolder_DoesNotRollOrCast()
        {
            var fx = Build();
            fx.Host.Attach(Instance, Holder, Def(ProcA));

            Fire(fx, Damage(Stranger));

            Assert.Empty(fx.Casts);
            Assert.Equal(0, fx.Rng.Calls);
        }

        [Fact]
        public void EventKindWithoutHolderCorrelation_NeverFires_EvenIfSubscribed()
        {
            var fx = Build();
            fx.Host.Attach(Instance, Holder, Def(ProcA, triggerEvent: RulesEventKeys.CombatEntered));

            Fire(fx, new CombatEnteredEvent(Stranger));
            Assert.Empty(fx.Casts);

            Fire(fx, new CombatEnteredEvent(Holder));
            var cast = Assert.Single(fx.Casts);
            Assert.Equal(0, cast.depth); // 非触发链事件的链深度恒为 0
        }

        [Fact]
        public void Roll_AtOrAboveChance_DoesNotTrigger_BelowChanceTriggers()
        {
            var fx = Build();
            const double chance = 0.4;
            fx.Host.Attach(Instance, Holder, Def(ProcA, chance));

            fx.Rng.Rolls.Enqueue(chance);                 // 恰等于 chance：不触发（roll >= chance）
            Fire(fx, Damage(Holder));
            Assert.Empty(fx.Casts);

            fx.Rng.Rolls.Enqueue(chance + 0.2);
            Fire(fx, Damage(Holder));
            Assert.Empty(fx.Casts);

            fx.Rng.Rolls.Enqueue(chance - 0.01);
            Fire(fx, Damage(Holder));
            Assert.Single(fx.Casts);
            Assert.Equal(3, fx.Rng.Calls);
        }

        [Fact]
        public void CastCallbackReturningFalse_PublishesNoProcTriggeredEvent_ButStillConsumesCooldown()
        {
            var fx = Build();
            fx.CastResult = false;
            fx.Host.Attach(Instance, Holder, Def(ProcA, icd: 5.0));

            Fire(fx, Damage(Holder));
            Assert.Single(fx.Casts);
            Assert.Empty(fx.Procs);

            // 冷却已在回调之前写入：即使施放失败，内部冷却期内也不会再次尝试。
            Fire(fx, Damage(Holder));
            Assert.Single(fx.Casts);
        }

        [Fact]
        public void InternalCooldown_SuppressedEventsDoNotConsumeRng_AndUpdateClampsAtZero()
        {
            var fx = Build();
            const double icd = 5.0;
            fx.Host.Attach(Instance, Holder, Def(ProcA, icd: icd));

            Fire(fx, Damage(Holder));
            var rollsAfterFirst = fx.Rng.Calls;
            Fire(fx, Damage(Holder));
            Assert.Single(fx.Casts);
            Assert.Equal(rollsAfterFirst, fx.Rng.Calls); // 冷却期内的事件连随机数都不取

            fx.Host.Update(icd * 0.5);
            Fire(fx, Damage(Holder));
            Assert.Single(fx.Casts);

            fx.Host.Update(icd * 100); // 远超剩余冷却：钳到 0，不会变成负数残留
            Fire(fx, Damage(Holder));
            Assert.Equal(2, fx.Casts.Count);
        }

        [Fact]
        public void RescaleAll_ScalesRemainingCooldown_AndAppliesFactorToLaterCooldowns()
        {
            var fx = Build();
            const double icd = 4.0;
            fx.Host.Attach(Instance, Holder, Def(ProcA, icd: icd));
            Fire(fx, Damage(Holder));                 // 剩余冷却 = icd

            const double factor = 3.0;
            fx.Host.RescaleAll(factor);               // 剩余冷却 = icd * factor

            fx.Host.Update(icd * factor - 0.5);
            Fire(fx, Damage(Holder));
            Assert.Single(fx.Casts);                  // 还差 0.5：仍在冷却

            fx.Host.Update(0.5);
            Fire(fx, Damage(Holder));
            Assert.Equal(2, fx.Casts.Count);

            // 因子已累计：第二次触发写入的冷却是 icd * factor（而不是裸 icd）。
            fx.Host.Update(icd);
            Fire(fx, Damage(Holder));
            Assert.Equal(2, fx.Casts.Count);
            fx.Host.Update(icd * factor - icd);
            Fire(fx, Damage(Holder));
            Assert.Equal(3, fx.Casts.Count);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-2.0)]
        public void RescaleAll_RejectsNonPositiveFactor(double factor)
        {
            var fx = Build();
            Assert.Throws<ArgumentException>(() => fx.Host.RescaleAll(factor));
        }

        [Fact]
        public void DetachByInstance_StopsAllProcsOfThatInstance()
        {
            var fx = Build();
            fx.Host.Attach(Instance, Holder, Def(ProcA));
            fx.Host.Attach(Instance, Holder, Def(ProcB));

            Fire(fx, Damage(Holder));
            Assert.Equal(2, fx.Casts.Count);

            fx.Host.Detach(Instance);
            Fire(fx, Damage(Holder));
            Assert.Equal(2, fx.Casts.Count);
        }

        [Fact]
        public void DetachByInstanceAndProcId_RemovesOnlyThatProc()
        {
            var fx = Build();
            fx.Host.Attach(Instance, Holder, Def(ProcA));
            fx.Host.Attach(Instance, Holder, Def(ProcB));

            fx.Host.Detach(Instance, ProcA);
            Fire(fx, Damage(Holder));

            Assert.Single(fx.Casts);
            Assert.Equal(ProcB, Assert.Single(fx.Procs).ProcDefId);

            // 再摘掉最后一个后，实例条目整体清空；继续摘除是无声的空操作。
            fx.Host.Detach(Instance, ProcB);
            fx.Host.Detach(Instance, ProcB);
            fx.Host.Detach(Instance);
            fx.Casts.Clear();
            Fire(fx, Damage(Holder));
            Assert.Empty(fx.Casts);
        }

        [Fact]
        public void Detach_UnknownInstance_IsSilentNoOp()
        {
            var fx = Build();
            fx.Host.Attach(Instance, Holder, Def(ProcA));

            fx.Host.Detach(new Id("aura.ph_unknown"));
            fx.Host.Detach(new Id("aura.ph_unknown"), ProcA);
            fx.Host.Detach(Instance, ProcB); // 实例存在但没有该 proc

            Fire(fx, Damage(Holder));
            Assert.Single(fx.Casts);
        }
    }
}
