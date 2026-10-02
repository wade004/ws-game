using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Tests.Foundation.Feel;
using Xunit;
using static Tests.Foundation.InputMap.BufferRig;

namespace Tests.Foundation.InputMap
{
    /// <summary>
    /// 宽限窗口剩余量与行动者登记（手感落地 M3-B）：<see cref="IGraceQuery.RemainingGraceTicks"/> 给施法管线的排队快照用；
    /// <see cref="InputBufferHost.RegisterActor"/> 让非本地行动者从登记起就进入每 tick 采样，不必等到第一次按键。
    /// </summary>
    public class GraceRemainingAndRegistrationTests
    {
        private sealed class MutableCondition : IGraceConditionEvaluator
        {
            public readonly HashSet<(Id, Id)> True = new HashSet<(Id, Id)>();

            public bool Evaluate(Id actorId, Id conditionId) => True.Contains((actorId, conditionId));
        }

        /// <summary>只实现旧三个成员加 AreAllSatisfied 的第三方查询对象：不实现新成员也能编译、运行，缺省实现保守。</summary>
        private sealed class LegacyQuery : IGraceQuery
        {
            public bool Satisfied;
            public bool InGrace;

            public bool IsSatisfied(Id actorId, Id conditionId) => Satisfied;

            public bool IsInGrace(Id actorId, Id conditionId) => InGrace;

            public long LastTrueTick(Id actorId, Id conditionId) => Satisfied ? 0 : -1;

            public bool AreAllSatisfied(Id actorId, IReadOnlyList<Id> conditionIds) => Satisfied;
        }

        /// <summary>
        /// 复现用例：条件在 tick 0..9 为真，之后为假。剩余量从"当前为真 = int.MaxValue"变为失效后的 grace_ticks - 1、... 、0，窗口外 -1；
        /// 不变量：<c>IsSatisfied</c> 恒等于"剩余量 &gt;= 0"，<c>IsInGrace</c> 恒等于"0 &lt;= 剩余量 &lt; int.MaxValue"（两套查询任何时刻互相印证）。
        /// </summary>
        [Fact]
        public void RemainingGraceTicks_CountsDownAfterTheConditionFails_AndAgreesWithIsSatisfiedAndIsInGrace()
        {
            var step = 1.0 / 60.0;
            var resolver = new FeelResolver(FeelTestSupport.FrameworkProfiles(), FeelTestSupport.CalA("feel.preset.arpg_responsive"), step);
            var cond = new Id("input.grace.target_in_range");
            var evaluator = new MutableCondition();
            var tracker = new GraceTracker(evaluator, resolver);
            tracker.Register(Actor, new[] { cond });
            var graceTicks = FeelCalibration.MillisecondsToTicks(resolver.ResolveJudging(Actor).GetNumber("grace_ms"), step);
            Assert.True(graceTicks >= 2);
            const int lostAt = 10;

            Assert.Equal(-1, tracker.RemainingGraceTicks(Actor, cond)); // 从未为真
            for (var t = 0; t < lostAt + graceTicks + 4; t++)
            {
                if (t < lostAt) evaluator.True.Add((Actor, cond)); else evaluator.True.Remove((Actor, cond));
                tracker.Sample(t);
                var remaining = tracker.RemainingGraceTicks(Actor, cond);

                if (t < lostAt) Assert.Equal(int.MaxValue, remaining);
                else if (t < lostAt + graceTicks) Assert.Equal(graceTicks - (t - lostAt + 1), remaining);
                else Assert.Equal(-1, remaining);

                Assert.Equal(remaining >= 0, tracker.IsSatisfied(Actor, cond));
                Assert.Equal(remaining >= 0 && remaining < int.MaxValue, tracker.IsInGrace(Actor, cond));
            }
        }

        /// <summary>不变量：没有手感档案（grace_ms 视为 0）时只有"当前为真"才有剩余量，失效的下一个 tick 起即 -1，与没有宽限一致。</summary>
        [Fact]
        public void RemainingGraceTicks_WithoutAFeelSource_ExpiresTheMomentTheConditionFails()
        {
            var cond = new Id("input.grace.a");
            var evaluator = new MutableCondition();
            var tracker = new GraceTracker(evaluator);
            tracker.Register(Actor, new[] { cond });
            evaluator.True.Add((Actor, cond));
            tracker.Sample(0);
            Assert.Equal(int.MaxValue, tracker.RemainingGraceTicks(Actor, cond));

            evaluator.True.Clear();
            tracker.Sample(1);
            Assert.Equal(-1, tracker.RemainingGraceTicks(Actor, cond));
        }

        /// <summary>不变量：旧的第三方查询对象不实现新成员也能用，缺省实现只依赖旧成员——不满足 -1、当前为真 int.MaxValue、在宽限内保守地返回 0。</summary>
        [Fact]
        public void RemainingGraceTicks_DefaultImplementation_IsConservativeForLegacyQueries()
        {
            IGraceQuery query = new LegacyQuery { Satisfied = false };
            var cond = new Id("input.grace.a");
            Assert.Equal(-1, query.RemainingGraceTicks(Actor, cond));

            query = new LegacyQuery { Satisfied = true, InGrace = false };
            Assert.Equal(int.MaxValue, query.RemainingGraceTicks(Actor, cond));

            query = new LegacyQuery { Satisfied = true, InGrace = true };
            Assert.Equal(0, query.RemainingGraceTicks(Actor, cond));
        }

        /// <summary>
        /// 复现用例：没有按过键的行动者，缓冲里的行动者数从 0 变为 1（<c>RegisterActor</c> 之后），宽限追踪据此从登记起就每 tick 采样；
        /// 不变量：重复登记无效果；登记不产生缓冲记录；<c>RemoveActor</c> 之后可再登记。
        /// </summary>
        [Fact]
        public void RegisterActor_AddsAnEmptyBufferWithoutAPress_AndIsIdempotent()
        {
            var attack = Def("input.action.attack", ActionClass.Attack);
            var rig = new BufferRig(new[] { attack }, "feel.preset.arpg_responsive");
            var other = new Id("unit.other");

            Assert.False(rig.Buffer.IsActorRegistered(other));
            Assert.DoesNotContain(other, rig.Buffer.ActorIds.ToList());

            rig.Buffer.RegisterActor(other);
            rig.Buffer.RegisterActor(other);

            Assert.True(rig.Buffer.IsActorRegistered(other));
            Assert.Single(rig.Buffer.ActorIds.Where(id => id.Equals(other)));
            Assert.Empty(rig.Buffer.Snapshot(other)); // 登记不产生任何缓冲记录

            rig.Buffer.RemoveActor(other);
            Assert.False(rig.Buffer.IsActorRegistered(other));
            rig.Buffer.RegisterActor(other);
            Assert.True(rig.Buffer.IsActorRegistered(other));
        }

        /// <summary>
        /// 复现用例（登记 → 采样）：宽限追踪按"缓冲里每个行动者"采样（与 <c>InputBufferTickHandler</c> 同一口径）。没有登记、没按过键的行动者最近一次为真 = -1；
        /// 登记之后的第一次采样起为当前 tick。
        /// </summary>
        [Fact]
        public void RegisteredActor_IsSampledFromRegistration_WhileAnUnregisteredOneHasNoHistory()
        {
            var cond = new Id("input.grace.always");
            var attack = new ActionDefinition(new Id("input.action.attack"), ActionKind.Button, new[] { "key:j" }, "default", null,
                ActionClass.Attack, null, null, null, InputRepeatPolicy.Refresh, null, new[] { cond });
            var rig = new BufferRig(new[] { attack }, "feel.preset.arpg_responsive");
            var evaluator = new MutableCondition();
            var registered = new Id("unit.registered");
            var unregistered = new Id("unit.unregistered");
            evaluator.True.Add((registered, cond));
            evaluator.True.Add((unregistered, cond));
            var tracker = new GraceTracker(evaluator);

            rig.Buffer.RegisterActor(registered);
            for (var t = 0; t < 3; t++)
            {
                rig.Step();
                foreach (var actor in rig.Buffer.ActorIds) tracker.Register(actor, rig.Buffer.GraceConditionNames);
                tracker.Sample(t);
            }

            Assert.Equal(2, tracker.LastTrueTick(registered, cond));
            Assert.Equal(-1, tracker.LastTrueTick(unregistered, cond));
        }

        /// <summary>
        /// 复现用例（手感落地 M4-W3，显式登记惰性分配）：对 N 个行动者显式 <c>RegisterActor</c>——此前每个建一份缓冲（<c>ActorBuffersAllocated</c> 0 → N），
        /// 现在只记 id（保持 0）；已登记、出现在 <c>ActorIds</c>（"显式登记即可采样"的语义不变，登记顺序即遍历顺序）；经过逐 tick 的缓冲维护不出错；
        /// 第一次真有边沿（按下）才为那一个行动者建缓冲（0 → 1），其余仍不建；按下之后与此前一样入槽、可被取用。
        /// </summary>
        [Fact]
        public void RegisterActor_IsLazy_RecordsOnlyTheId_AndTheBufferIsBuiltAtTheFirstEdge()
        {
            const int count = 12;
            var attack = Def("input.action.attack", ActionClass.Attack);
            var rig = new BufferRig(new[] { attack }, "feel.preset.arpg_responsive");
            var actors = Enumerable.Range(0, count).Select(i => new Id("unit.npc" + i)).ToList();

            foreach (var id in actors) rig.Buffer.RegisterActor(id);
            foreach (var id in actors) rig.Buffer.RegisterActor(id); // 幂等

            Assert.Equal(0, rig.Buffer.ActorBuffersAllocated);
            Assert.Equal(actors, rig.Buffer.ActorIds.ToList());
            Assert.All(actors, id => Assert.True(rig.Buffer.IsActorRegistered(id)));

            rig.Step();
            rig.Step();
            Assert.Equal(0, rig.Buffer.ActorBuffersAllocated);

            var pressed = actors[5];
            rig.Step(arrivals: () => rig.Buffer.Press(pressed, attack.ActionId));
            Assert.Equal(1, rig.Buffer.ActorBuffersAllocated);
            Assert.Equal(actors, rig.Buffer.ActorIds.ToList()); // 登记顺序不变
            Assert.Single(rig.Buffer.Snapshot(pressed));
            Assert.Empty(rig.Buffer.Snapshot(actors[0]));

            // 未登记的行动者第一次按键：登记与建缓冲同时发生（与此前一致，排在登记者之后）。
            var stranger = new Id("unit.stranger");
            rig.Step(arrivals: () => rig.Buffer.Press(stranger, attack.ActionId));
            Assert.Equal(2, rig.Buffer.ActorBuffersAllocated);
            Assert.True(rig.Buffer.IsActorRegistered(stranger));
            Assert.Equal(stranger, rig.Buffer.ActorIds.Last());
        }

        /// <summary>
        /// 不变量：只登记没建缓冲的行动者，<c>RemoveActor</c> 一样清掉登记（已登记 → 未登记）、不触发缓冲丢弃事件、之后可再登记；
        /// <c>ClearAll</c>/<c>Clear</c> 对没有缓冲的登记者是无操作。登记不改变 <c>ActorIds</c> 的去重与顺序。
        /// </summary>
        [Fact]
        public void RemoveAndClear_TreatARegisteredActorWithoutABuffer_AsHavingNothingToDrop()
        {
            var attack = Def("input.action.attack", ActionClass.Attack);
            var rig = new BufferRig(new[] { attack }, "feel.preset.arpg_responsive");
            var a = new Id("unit.a");
            var b = new Id("unit.b");
            rig.Buffer.RegisterActor(a);
            rig.Buffer.RegisterActor(b);

            rig.Buffer.Clear(a);
            rig.Buffer.ClearAll();
            rig.Buffer.RemoveActor(a);

            Assert.False(rig.Buffer.IsActorRegistered(a));
            Assert.True(rig.Buffer.IsActorRegistered(b));
            Assert.Equal(new[] { b }, rig.Buffer.ActorIds.ToArray());
            Assert.Empty(rig.Drops);
            Assert.Equal(0, rig.Buffer.ActorBuffersAllocated);

            rig.Buffer.RegisterActor(a);
            Assert.Equal(new[] { b, a }, rig.Buffer.ActorIds.ToArray());
        }
    }
}
