using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Projectile
{
    /// <summary>
    /// 投射物命中高度窗口（<c>hit_height</c>，ADR-0171）：窗口相对发射者发射瞬间的脚下高度；声明窗口的投射物只命中脚下高度落在窗口内的单位，
    /// 没有声明的投射物对高度视而不见（既有行为，逐位不变）。期望值由窗口声明（相对高度 + 发射者高度）在用例里算出。
    /// </summary>
    public class ProjectileHitHeightTests
    {
        private static readonly Id Source = new Id("unit.hh_source");
        private static readonly Id Target = new Id("unit.hh_target");
        private static readonly Id SkillId = new Id("skill.hh_wave");
        private static readonly Id School = new Id("school.physical");
        private const double WaveTop = 0.5;

        private static EffectContext Wave(Core.Foundation.Common.Json.JsonObject extra) => new EffectContext(
            Source, Target, SkillId, EffectKind.Projectile, School, 0, 0, extra);

        private static Core.Foundation.Common.Json.JsonObject Params(params (string, Core.Foundation.Common.Json.JsonValue)[] more)
        {
            var list = new System.Collections.Generic.List<(string, Core.Foundation.Common.Json.JsonValue)>
            {
                ("speed", J.N(10)), ("max_range", J.N(30)),
                ("on_hit_effects", J.A(J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(7))))))),
            };
            list.AddRange(more);
            return J.O(list.ToArray());
        }

        private static ProjectileWorld Build(double sourceHeight, double targetHeight)
        {
            var w = ProjectileWorldBuilder.Build();
            w.AddUnit(Source.Value, new Vec2(0, 0));
            w.AddUnit(Target.Value, new Vec2(10, 0));
            ((PlayerUnit)w.World.GetEntity(Source)!).HeightOffset = sourceHeight;
            ((PlayerUnit)w.World.GetEntity(Target)!).HeightOffset = targetHeight;
            return w;
        }

        private static int Hits(ProjectileWorld w) => w.Sink.Applied.Count;

        private static void Fly(ProjectileWorld w)
        {
            for (var i = 0; i < 400 && w.Host.ActiveCount > 0; i++) w.Host.Advance(1.0 / 60.0);
        }

        [Fact]
        public void Gap_WithoutAHeightWindow_AGroundWaveCannotBeJumped_TheJumpingTargetIsStillHit()
        {
            var w = Build(0, 1.2); // 目标起跳，脚下 1.2 高于冲击波
            w.Host.Spawn(Wave(Params()), w.Sink);
            Fly(w);
            Assert.Equal(1, Hits(w)); // 缺口复现：投射物命中判定只看平面，起跳也躲不开
        }

        [Fact]
        public void WithAHeightWindow_ATargetWhoseFeetAreAboveTheWindowIsNotHit_AndTheWaveFliesOn()
        {
            var w = Build(0, WaveTop + 0.7);
            w.Host.Spawn(Wave(Params(("hit_height", J.O(("max", J.N(WaveTop)))))), w.Sink);
            Fly(w);
            Assert.Equal(0, Hits(w));
            Assert.Equal(0, w.Host.ActiveCount); // 飞完射程才消失，没有命中
        }

        [Fact]
        public void WithAHeightWindow_AGroundedTargetIsHit_AndTheWindowEdgeIsInclusive()
        {
            var grounded = Build(0, 0);
            grounded.Host.Spawn(Wave(Params(("hit_height", J.O(("max", J.N(WaveTop)))))), grounded.Sink);
            Fly(grounded);
            Assert.Equal(1, Hits(grounded));

            var edge = Build(0, WaveTop);
            edge.Host.Spawn(Wave(Params(("hit_height", J.O(("max", J.N(WaveTop)))))), edge.Sink);
            Fly(edge);
            Assert.Equal(1, Hits(edge));
        }

        [Fact]
        public void TheWindowIsRelativeToTheShootersFeetAtLaunch_SoAWaveFromAPlatformHitsTargetsOnThatPlatform()
        {
            const double platform = 4.0;
            var onPlatform = Build(platform, platform + 0.1);
            onPlatform.Host.Spawn(Wave(Params(("hit_height", J.O(("min", J.N(-0.2)), ("max", J.N(WaveTop)))))), onPlatform.Sink);
            Fly(onPlatform);
            Assert.Equal(1, Hits(onPlatform));

            var below = Build(platform, 0.0); // 平台下面的地面：低于窗口下沿
            below.Host.Spawn(Wave(Params(("hit_height", J.O(("min", J.N(-0.2)), ("max", J.N(WaveTop)))))), below.Sink);
            Fly(below);
            Assert.Equal(0, Hits(below));
        }

        [Fact]
        public void AWindowWithoutBounds_BehavesLikeNoWindow()
        {
            var w = Build(0, 9.0);
            w.Host.Spawn(Wave(Params(("hit_height", J.O()))), w.Sink);
            Fly(w);
            Assert.Equal(1, Hits(w)); // 空对象：没有上下沿，等同不限（任何高度都命中，与没有声明窗口一致）
        }
    }
}
