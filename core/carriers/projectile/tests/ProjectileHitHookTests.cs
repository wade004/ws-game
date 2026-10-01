using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Projectile
{
    /// <summary>
    /// 投射物命中钩子（<see cref="IProjectileHitHook"/>，手感设计/03 第 2.5 节）：时间线 <c>release</c> 标记发射的投射物经它做
    /// 无敌前置检查、取得攻击实例 id、在效果落地后回报结果；没有钩子时行为完全不变（既有用例覆盖）。
    /// </summary>
    public class ProjectileHitHookTests
    {
        private static readonly Id Source = new Id("unit.hook_source");
        private static readonly Id SkillId = new Id("skill.hook_sample_bolt");
        private static readonly Id School = new Id("school.physical");

        private sealed class RecordingHook : IProjectileHitHook
        {
            public readonly HashSet<Id> Reject = new HashSet<Id>();
            public readonly List<ProjectileHitInfo> Before = new List<ProjectileHitInfo>();
            public readonly List<(ProjectileHitInfo Info, Id AttackInstanceId, int ResultCount)> After =
                new List<(ProjectileHitInfo, Id, int)>();

            private int _next;

            public bool BeforeHit(in ProjectileHitInfo info, out Id attackInstanceId)
            {
                Before.Add(info);
                attackInstanceId = new Id("atk." + (++_next));
                return !Reject.Contains(info.TargetId);
            }

            public void AfterHit(in ProjectileHitInfo info, Id attackInstanceId, IReadOnlyList<ResolveResult> results) =>
                After.Add((info, attackInstanceId, results.Count));

            public int Launched;
            public readonly List<ProjectileEndReason> Ended = new List<ProjectileEndReason>();

            public void OnLaunched() => Launched++;

            public void OnEnded(ProjectileEndReason reason) => Ended.Add(reason);
        }

        private static EffectContext Context(Id target, params (string Key, Core.Foundation.Common.Json.JsonValue Value)[] extra)
        {
            var fields = new List<(string, Core.Foundation.Common.Json.JsonValue)>
            {
                ("hit_behavior", J.S("pierce")),
                ("speed", J.N(20)),
                ("max_range", J.N(30)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(5))))))),
            };
            foreach (var e in extra)
            {
                fields.RemoveAll(f => f.Item1 == e.Key); // 额外字段覆盖同名缺省字段（如 max_range）。
                fields.Add((e.Key, e.Value));
            }
            return new EffectContext(Source, target, SkillId, EffectKind.Projectile, School, 0, 0, J.O(fields.ToArray()));
        }

        private static (ProjectileWorld World, Id[] Targets) Setup(int targetCount)
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            var targets = new Id[targetCount];
            for (var i = 0; i < targetCount; i++)
            {
                targets[i] = w.AddUnit("unit.hook_target_" + i, new Vec2(5 + 4 * i, 0));
            }

            return (w, targets);
        }

        [Fact]
        public void Pierce_MaxPierceCount2_HitsTheFirstTwoTargets_ThenTheProjectileDisappears()
        {
            var (w, targets) = Setup(3);
            var hook = new RecordingHook();
            w.Host.Spawn(Context(targets[0], ("max_pierce_count", J.N(2))), w.Sink, hook);

            w.Host.Advance(1.0); // 位移 20，经过 5/9/13 三个目标。

            Assert.Equal(new[] { targets[0], targets[1] }, w.Sink.Applied.Select(c => c.TargetId).ToArray());
            Assert.Equal(new[] { targets[0], targets[1] }, hook.After.Select(a => a.Info.TargetId).ToArray());
            Assert.Equal(0, w.Host.ActiveCount); // 第二个穿透命中后销毁。
        }

        [Fact]
        public void RejectedTarget_IsSkippedBeforeAnyEffect_AndDoesNotConsumeThePierceCount()
        {
            var (w, targets) = Setup(3);
            var hook = new RecordingHook();
            hook.Reject.Add(targets[0]); // 第一个目标处于无敌窗口：前置检查拒绝。
            w.Host.Spawn(Context(targets[0], ("max_pierce_count", J.N(2))), w.Sink, hook);

            w.Host.Advance(1.0);

            Assert.Equal(new[] { targets[1], targets[2] }, w.Sink.Applied.Select(c => c.TargetId).ToArray());
            Assert.Equal(targets, hook.Before.Select(b => b.TargetId).ToArray()); // 被拒绝的目标也问过一次，只问一次。
            Assert.DoesNotContain(hook.After, a => a.Info.TargetId.Equals(targets[0]));
            Assert.Equal(0, w.Host.ActiveCount);
        }

        [Fact]
        public void EachHit_UsesTheAttackInstanceIdTheHookIssued_AndReportsTheEffectResults()
        {
            var (w, targets) = Setup(2);
            var hook = new RecordingHook();
            w.Host.Spawn(Context(targets[0]), w.Sink, hook);

            w.Host.Advance(1.0);

            Assert.Equal(2, w.Sink.Applied.Count);
            for (var i = 0; i < 2; i++)
            {
                Assert.Equal(hook.After[i].AttackInstanceId, w.Sink.Applied[i].AttackInstanceId);
                Assert.Equal(1, hook.After[i].ResultCount);
            }

            Assert.NotEqual(hook.After[0].AttackInstanceId, hook.After[1].AttackInstanceId);
        }

        [Fact]
        public void HitInfo_CarriesTheCollisionPointAndFlightDirection_AndDedupsAcrossAdvances()
        {
            var (w, targets) = Setup(1);
            var hook = new RecordingHook();
            w.Host.Spawn(Context(targets[0]), w.Sink, hook);

            for (var i = 0; i < 6; i++) w.Host.Advance(0.1); // 位移 2 每步：多个相邻步长都覆盖目标，仍只命中一次。

            var info = Assert.Single(hook.Before);
            Assert.Equal(Source, info.SourceId);
            Assert.Equal(targets[0], info.TargetId);
            Assert.Equal(SkillId, info.SkillId);
            Assert.Equal(5.0, info.ContactPoint.X, 6); // 碰撞点 = 飞行线段上离目标最近的点。
            Assert.Equal(0.0, info.ContactPoint.Y, 6);
            Assert.Equal(1.0, info.FlightDirection.X, 9);
            Assert.Equal(0.0, info.FlightDirection.Y, 9);
            Assert.Single(w.Sink.Applied);
        }

        [Fact]
        public void WithoutAHook_BehaviourIsUnchanged_AndTheAttackInstanceIdStaysUnset()
        {
            var (w, targets) = Setup(2);
            w.Host.Spawn(Context(targets[0], ("max_pierce_count", J.N(1))), w.Sink);

            w.Host.Advance(1.0);

            Assert.Single(w.Sink.Applied);
            Assert.Null(w.Sink.Applied[0].AttackInstanceId);
            Assert.Equal(0, w.Host.ActiveCount);
        }

        [Fact]
        public void ImpactOnExpiry_AlsoGoesThroughTheHook()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            var near = w.AddUnit("unit.hook_aoe_near", new Vec2(20.5, 0));
            var far = w.AddUnit("unit.hook_aoe_far", new Vec2(50, 0));
            var hook = new RecordingHook();
            var context = new EffectContext(Source, near, SkillId, EffectKind.Projectile, School, 0, 0, J.O(
                ("hit_behavior", J.S("impact_on_expiry")),
                ("speed", J.N(20)),
                ("max_range", J.N(20)),
                ("impact_radius", J.N(3)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(5)))))))));
            w.Host.Spawn(context, w.Sink, hook);

            w.Host.Advance(1.0);

            Assert.Equal(new[] { near }, hook.Before.Select(b => b.TargetId).ToArray());
            Assert.DoesNotContain(hook.Before, b => b.TargetId.Equals(far));
            Assert.Single(w.Sink.Applied);
        }

        // ------------------------------------------------------------------ 生命周期（OnLaunched / OnEnded）：每发恰好一次，原因四选一

        [Fact]
        public void Lifecycle_SpawnNotifiesLaunchedOnce_AndAPierceThatRunsOutOfCountEndsWithHit()
        {
            var (w, targets) = Setup(2);
            var hook = new RecordingHook();
            w.Host.Spawn(Context(targets[0], ("max_pierce_count", J.N(1))), w.Sink, hook);
            Assert.Equal(1, hook.Launched);
            Assert.Empty(hook.Ended); // 还在飞：没有结局。

            w.Host.Advance(1.0);

            Assert.Equal(new[] { ProjectileEndReason.Hit }, hook.Ended);
            Assert.Equal(1, hook.Launched);
        }

        [Fact]
        public void Lifecycle_APiercingProjectileThatKeepsFlyingReportsItsEndOnlyOnce_NotPerHit()
        {
            var (w, targets) = Setup(2);
            var hook = new RecordingHook();
            w.Host.Spawn(Context(targets[0], ("max_pierce_count", J.N(5)), ("max_range", J.N(12))), w.Sink, hook);

            w.Host.Advance(0.3); // 位移 6：穿过 5 处的第一个目标，仍在飞。
            Assert.Single(hook.After);
            Assert.Empty(hook.Ended);

            w.Host.Advance(1.0); // 射程耗尽（12）：穿过第二个目标（9），最终到期。
            Assert.Equal(new[] { ProjectileEndReason.Expired }, hook.Ended);
        }

        [Fact]
        public void Lifecycle_ATerrainBlockEndsWithBlocked_AndNoHitWasReported()
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            var target = w.AddUnit("unit.hook_target_far", new Vec2(10, 0));
            w.Navigation.SetBlocking(ProjectileWorld.MapId, new[]
            {
                new Core.Foundation.Common.Rect(new Vec2(4, -1), new Vec2(5, 1)),
            });
            var hook = new RecordingHook();
            w.Host.Spawn(Context(target), w.Sink, hook);

            w.Host.Advance(1.0);

            Assert.Equal(new[] { ProjectileEndReason.Blocked }, hook.Ended);
            Assert.Empty(hook.Before);
        }

        [Fact]
        public void Lifecycle_RunningOutOfRangeWithoutAContactEndsWithExpired()
        {
            var (w, targets) = Setup(1);
            var hook = new RecordingHook();
            w.Host.Spawn(Context(targets[0], ("max_range", J.N(3))), w.Sink, hook); // 目标在 5：够不着。

            w.Host.Advance(1.0);

            Assert.Equal(new[] { ProjectileEndReason.Expired }, hook.Ended);
            Assert.Empty(hook.Before);
        }

        [Fact]
        public void Lifecycle_ClearAllNotifiesEveryLiveProjectileOnceWithCleared()
        {
            var (w, targets) = Setup(1);
            var first = new RecordingHook();
            var second = new RecordingHook();
            w.Host.Spawn(Context(targets[0]), w.Sink, first);
            w.Host.Spawn(Context(targets[0]), w.Sink, second);

            w.World.ClearAll();
            w.Host.ClearAll();
            w.Host.Advance(1.0); // 清场之后再推进也不会重复通知。

            Assert.Equal(new[] { ProjectileEndReason.Cleared }, first.Ended);
            Assert.Equal(new[] { ProjectileEndReason.Cleared }, second.Ended);
        }

        [Fact]
        public void Lifecycle_AHookThatOnlyImplementsTheOriginalMembersStillWorks_DefaultMembersAreNoOps()
        {
            var (w, targets) = Setup(1);
            var hook = new LegacyHook();
            w.Host.Spawn(Context(targets[0]), w.Sink, hook);

            w.Host.Advance(1.0);

            Assert.Single(w.Sink.Applied);
            Assert.Equal(1, hook.BeforeCount);
        }

        private sealed class LegacyHook : IProjectileHitHook
        {
            public int BeforeCount;

            public bool BeforeHit(in ProjectileHitInfo info, out Id attackInstanceId)
            {
                BeforeCount++;
                attackInstanceId = new Id("atk.legacy");
                return true;
            }

            public void AfterHit(in ProjectileHitInfo info, Id attackInstanceId, IReadOnlyList<ResolveResult> results)
            {
            }
        }
    }
}
