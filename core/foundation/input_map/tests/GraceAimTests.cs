using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Tests.Foundation.Feel;
using Xunit;
using static Tests.Foundation.InputMap.BufferRig;

namespace Tests.Foundation.InputMap
{
    /// <summary>
    /// 施法瞄点与宽限历史（手感落地 M4-G，手感设计/01 第 2.4 节）：施法请求自己携带的目标/落点优先于求值器的缺省目标；依赖瞄点的条件的历史属于那个瞄点，
    /// 换了瞄点就作废，不拿另一个目标刚才够得着的记录放行；不依赖瞄点的条件与不接收瞄点的求值器行为不变。期望值由 <c>grace_ms</c> 换算规则算出。
    /// </summary>
    public class GraceAimTests
    {
        private static readonly Id AimCond = new Id("input.grace.test_aim_in_range");
        private static readonly Id PlainCond = new Id("input.grace.test_plain");
        private static readonly Id TargetA = new Id("unit.target_a");
        private static readonly Id TargetB = new Id("unit.target_b");
        private const double Step = 1.0 / 60.0;

        /// <summary>模拟的世界：目标到行动者的距离、缺省目标、射程；"瞄点条件"依赖瞄点，"普通条件"只看一个开关。</summary>
        private sealed class WorldEvaluator : IGraceConditionEvaluator
        {
            public readonly Dictionary<Id, double> Distance = new Dictionary<Id, double>();
            public Id? Default;
            public double DefaultRange = 3;
            public bool Plain;
            public readonly List<string> Calls = new List<string>();

            private bool InRange(Id? target, double range) =>
                target.HasValue && Distance.TryGetValue(target.Value, out var d) && d <= range;

            public bool Evaluate(Id actorId, Id conditionId) => Evaluate(actorId, conditionId, GraceAim.None);

            public bool Evaluate(Id actorId, Id conditionId, GraceAim aim)
            {
                Calls.Add(conditionId.Value + ":" + (aim.IsNone ? "none" : aim.TargetId?.Value ?? "point"));
                if (conditionId.Equals(PlainCond)) return Plain;
                return InRange(aim.TargetId ?? Default, aim.Range > 0 ? aim.Range : DefaultRange);
            }

            public bool UsesAim(Id conditionId) => conditionId.Equals(AimCond);

            public Id? DefaultAimTarget(Id actorId) => Default;
        }

        /// <summary>只实现旧成员（两参数 Evaluate）的第三方求值器：不实现新成员也能编译、运行，瞄点对它没有任何影响。</summary>
        private sealed class LegacyEvaluator : IGraceConditionEvaluator
        {
            public bool Value;

            public bool Evaluate(Id actorId, Id conditionId) => Value;
        }

        private static (GraceTracker Tracker, WorldEvaluator World, int GraceTicks) Rig()
        {
            var resolver = new FeelResolver(FeelTestSupport.FrameworkProfiles(), FeelTestSupport.CalA("feel.preset.arpg_responsive"), Step);
            var world = new WorldEvaluator { Default = TargetA };
            world.Distance[TargetA] = 1;
            world.Distance[TargetB] = 1;
            var tracker = new GraceTracker(world, resolver);
            tracker.Register(Actor, new[] { AimCond, PlainCond });
            var graceTicks = FeelCalibration.MillisecondsToTicks(resolver.ResolveJudging(Actor).GetNumber("grace_ms"), Step);
            Assert.True(graceTicks >= 2, "标定下 grace_ms 应换算出至少 2 个 tick，用例才有意义");
            return (tracker, world, graceTicks);
        }

        private static void Run(GraceTracker tracker, ref int tick, int count)
        {
            for (var i = 0; i < count; i++) tracker.Sample(tick++);
        }

        /// <summary>
        /// 复现用例（目标来源）：缺省目标 A 在射程内、显式瞄点 B 在射程外。不带瞄点（此前的求值方式）条件为真，带瞄点（施法请求携带的目标 B）条件为假——
        /// 求值取瞄点的目标而不是缺省目标；瞄点作废后回落到缺省目标，条件又为真。
        /// </summary>
        [Fact]
        public void TheAimTarget_WinsOverTheDefaultTarget_AndTheDefaultComesBackWhenTheAimLapses()
        {
            var (tracker, world, graceTicks) = Rig();
            world.Distance[TargetB] = 10; // 射程之外
            var tick = 0;
            Run(tracker, ref tick, 2);
            Assert.Equal(tick - 1, tracker.LastTrueTick(Actor, AimCond)); // 缺省目标 A 在射程内：此前的求值方式

            tracker.NoteAim(Actor, GraceAim.OfTarget(TargetB, 3));
            Assert.Equal(-1, tracker.LastTrueTick(Actor, AimCond)); // 换了瞄点：A 的历史作废，B 不在射程内，没有新历史
            Run(tracker, ref tick, 1);
            Assert.Equal(-1, tracker.LastTrueTick(Actor, AimCond)); // 瞄点仍有效时按 B 求值，不回落到 A

            Run(tracker, ref tick, graceTicks + 1); // 没有新记录：瞄点作废，回落到缺省目标 A
            Assert.True(tracker.CurrentAim(Actor).IsNone);
            Assert.Equal(tick - 1, tracker.LastTrueTick(Actor, AimCond));
        }

        /// <summary>
        /// 复现用例（历史属于瞄点）：目标 B 先在射程内（条件为真），随后离开。窗口内再次对 B 施法：历史还在，宽限内满足（0 → 1）；
        /// 换成目标 A 施法（A 也在射程外）：B 的历史作废，不满足——不拿 B 刚才够得着的记录放行 A。
        /// </summary>
        [Fact]
        public void History_BelongsToTheAim_ASecondAttemptOnTheSameTargetIsCovered_AnotherTargetIsNot()
        {
            var (tracker, world, graceTicks) = Rig();
            world.Default = null;
            var tick = 0;
            tracker.Sample(tick++);

            tracker.NoteAim(Actor, GraceAim.OfTarget(TargetB, 3)); // B 在射程内：当场为真
            Assert.Equal(tracker.CurrentTick, tracker.LastTrueTick(Actor, AimCond));
            Assert.True(tracker.IsSatisfied(Actor, AimCond));
            Assert.False(tracker.IsInGrace(Actor, AimCond));

            world.Distance[TargetB] = 10; // B 离开射程
            world.Distance[TargetA] = 10;
            Run(tracker, ref tick, 1);
            tracker.NoteAim(Actor, GraceAim.OfTarget(TargetB, 3)); // 同一瞄点再记一次：不作废历史，刷新瞄点
            Assert.True(tracker.IsInGrace(Actor, AimCond), "窗口内、同一目标：宽限满足");
            Assert.True(tracker.RemainingGraceTicks(Actor, AimCond) >= 0);

            tracker.NoteAim(Actor, GraceAim.OfTarget(TargetA, 3)); // 换成另一个同样够不着的目标
            Assert.Equal(-1, tracker.LastTrueTick(Actor, AimCond));
            Assert.False(tracker.IsSatisfied(Actor, AimCond));
            Assert.False(tracker.IsInGrace(Actor, AimCond));
            Assert.True(graceTicks >= 2);
        }

        /// <summary>
        /// 不变量（窗口边界）：同一目标离开射程后，条件在距最近一次为真不超过 <c>grace_ms</c> 换算 tick 数以内满足、之后不满足；瞄点每个 tick 刷新（游戏逐 tick 喂准星）。
        /// </summary>
        [Fact]
        public void TheWindowBoundaryFollowsGraceMs_WhenTheAimIsRefreshedEveryTick()
        {
            var (tracker, world, graceTicks) = Rig();
            world.Default = null;
            var tick = 0;
            tracker.Sample(tick++);
            tracker.NoteAim(Actor, GraceAim.OfTarget(TargetB, 3));
            var lastTrue = tracker.LastTrueTick(Actor, AimCond);
            Assert.True(lastTrue >= 0);

            world.Distance[TargetB] = 10;
            for (var elapsed = 1; elapsed <= graceTicks + 3; elapsed++)
            {
                tracker.Sample(tick++);
                tracker.NoteAim(Actor, GraceAim.OfTarget(TargetB, 3));
                Assert.Equal(tracker.CurrentTick - lastTrue, elapsed);
                Assert.Equal(elapsed <= graceTicks, tracker.IsSatisfied(Actor, AimCond));
            }
        }

        /// <summary>
        /// 不变量：新瞄点的目标就是缺省目标时，此前以缺省目标积累的历史仍然有效（最近一次为真保持不变，不被作废）；换成别的目标才作废（保持 → -1）。
        /// 目标全部离开射程后再记瞄点，用来区分"历史被保留"与"作废后当场重新求值为真"。
        /// </summary>
        [Fact]
        public void AnAimEqualToTheDefaultTarget_KeepsTheHistory_AnotherTargetDropsIt()
        {
            var (tracker, world, _) = Rig();
            var tick = 0;
            Run(tracker, ref tick, 3);
            var before = tracker.LastTrueTick(Actor, AimCond);
            Assert.Equal(tick - 1, before);

            world.Distance[TargetA] = 10;
            world.Distance[TargetB] = 10;
            Run(tracker, ref tick, 1);
            Assert.Equal(before, tracker.LastTrueTick(Actor, AimCond)); // 缺省目标离开射程：历史停在离开之前

            tracker.NoteAim(Actor, GraceAim.OfTarget(TargetA, 3)); // A 就是缺省目标
            Assert.Equal(before, tracker.LastTrueTick(Actor, AimCond));

            tracker.NoteAim(Actor, GraceAim.OfTarget(TargetB, 3)); // 另一个目标
            Assert.Equal(-1, tracker.LastTrueTick(Actor, AimCond));
        }

        /// <summary>不变量：不依赖瞄点的条件，历史不受瞄点变化影响（不作废）。</summary>
        [Fact]
        public void ConditionsThatDoNotUseTheAim_KeepTheirHistoryAcrossAimChanges()
        {
            var (tracker, world, _) = Rig();
            world.Plain = true;
            var tick = 0;
            Run(tracker, ref tick, 2);
            var before = tracker.LastTrueTick(Actor, PlainCond);
            Assert.Equal(tick - 1, before);

            world.Plain = false;
            tracker.NoteAim(Actor, GraceAim.OfTarget(TargetB, 3));
            tracker.NoteAim(Actor, GraceAim.OfPoint(new Vec2(4, 0), 3));
            tracker.ClearAim(Actor);

            Assert.Equal(before, tracker.LastTrueTick(Actor, PlainCond));
        }

        /// <summary>不变量（缺省不变）：只实现旧成员的求值器收不到瞄点——带不带瞄点，每个 tick 的"最近一次为真"序列逐 tick 相同。</summary>
        [Fact]
        public void ALegacyEvaluator_IsUnaffectedByAims()
        {
            var plain = new GraceTracker(new LegacyEvaluator { Value = true });
            var withAim = new GraceTracker(new LegacyEvaluator { Value = true });
            plain.Register(Actor, new[] { AimCond });
            withAim.Register(Actor, new[] { AimCond });

            for (var t = 0; t < 6; t++)
            {
                if (t == 2) withAim.NoteAim(Actor, GraceAim.OfTarget(TargetB, 3));
                if (t == 4) withAim.NoteAim(Actor, GraceAim.OfPoint(new Vec2(1, 1), 3));
                plain.Sample(t);
                withAim.Sample(t);
                Assert.Equal(plain.LastTrueTick(Actor, AimCond), withAim.LastTrueTick(Actor, AimCond));
                Assert.Equal(plain.IsSatisfied(Actor, AimCond), withAim.IsSatisfied(Actor, AimCond));
            }
        }

        /// <summary>不变量：没有记录过瞄点时，采样传给求值器的瞄点恒为 None（与引入瞄点之前的求值一致）；行动者被注销后瞄点一并清掉。</summary>
        [Fact]
        public void WithoutAnyNotedAim_TheEvaluatorSeesNoAim_AndUnregisterForgetsTheAim()
        {
            var (tracker, world, _) = Rig();
            var tick = 0;
            Run(tracker, ref tick, 2);
            Assert.All(world.Calls, c => Assert.EndsWith(":none", c));

            tracker.NoteAim(Actor, GraceAim.OfTarget(TargetB, 3));
            Assert.False(tracker.CurrentAim(Actor).IsNone);
            tracker.Unregister(Actor);
            Assert.True(tracker.CurrentAim(Actor).IsNone);
        }
    }
}
