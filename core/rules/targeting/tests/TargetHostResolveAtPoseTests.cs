using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Targeting
{
    /// <summary>
    /// 手感落地（时间线空间命中）：<c>TargetHost.TryGetChainShape</c>（取链的形状模板）与 <c>TargetHost.ResolveAtPose</c>
    /// （按给定位置与朝向重新锚定形状，<c>continuous</c> 命中在攻击方位姿之间插值采样时用）。
    /// </summary>
    public class TargetHostResolveAtPoseTests
    {
        private static readonly Id HeroFaction = new Id("fac.target_test_hero");
        private static readonly Id MonsterFaction = new Id("fac.target_test_monster");
        private static readonly Id Caster = new Id("unit.rapose_caster");

        private const string Rows = @"[
            { ""id"": ""target.chain.rapose_cone"", ""source"": ""all_in_shape"",
              ""shape"": { ""kind"": ""cone"", ""radius"": 50, ""angle"": 1.5707963267948966 },
              ""filters"": [""relation:hostile""], ""max_targets"": 0 },
            { ""id"": ""target.chain.rapose_circle"", ""source"": ""nearest_in_shape"",
              ""shape"": { ""kind"": ""circle"", ""radius"": 30 }, ""filters"": [""relation:hostile""] },
            { ""id"": ""target.chain.rapose_rect"", ""source"": ""all_in_shape"",
              ""shape"": { ""kind"": ""rect"", ""length"": 40, ""width"": 10 }, ""max_targets"": 0 },
            { ""id"": ""target.chain.rapose_noshape"", ""source"": ""current_target"" }
        ]";

        private static void Place(TargetHostTests.Fixture fx, Id id, Id faction, Vec2 pos, double facing = 0)
        {
            fx.Units.Add(id, faction, pos, facing: facing);
            fx.Spatial.Register(id, pos, 0);
        }

        [Fact]
        public void TryGetChainShape_ReturnsTheUnanchoredTemplate_AndFalseForChainsWithoutShape()
        {
            var fx = TargetHostTests.Build(Rows, (units, spatial, powers, threat) => { });

            Assert.True(fx.Host.TryGetChainShape(new Id("target.chain.rapose_cone"), out var cone));
            Assert.Equal(ShapeKind.Cone, cone.Kind);
            Assert.Equal(50.0, cone.Radius);
            Assert.Equal(Math.PI / 2, cone.Angle, 12);
            Assert.Equal(Vec2.Zero, cone.Origin);
            Assert.Equal(0.0, cone.Direction);

            Assert.True(fx.Host.TryGetChainShape(new Id("target.chain.rapose_rect"), out var rect));
            Assert.Equal(ShapeKind.Rect, rect.Kind);
            Assert.Equal(new Vec2(20, 5), rect.HalfExtents);

            Assert.False(fx.Host.TryGetChainShape(new Id("target.chain.rapose_noshape"), out _));
        }

        [Fact]
        public void ResolveAtPose_AnchorsTheShapeAtTheGivenPoseNotAtTheCaster()
        {
            var fx = TargetHostTests.Build(Rows, (units, spatial, powers, threat) => { });
            Place(fx, Caster, HeroFaction, new Vec2(0, 0), facing: 0);
            var east = new Id("unit.rapose_east");
            var west = new Id("unit.rapose_west");
            Place(fx, east, MonsterFaction, new Vec2(110, 0));
            Place(fx, west, MonsterFaction, new Vec2(90, 0));

            // 位姿 = (100, 0) 朝 -X：形状只覆盖西侧；施法者自己的位置与朝向（原点、+X）被忽略。
            var atPose = fx.Host.ResolveAtPose(new Id("target.chain.rapose_cone"), Caster, new Vec2(100, 0), Math.PI);
            Assert.Equal(new[] { west }, atPose.Targets.Select(t => t.Target).ToArray());

            // 同一位姿朝 +X：只覆盖东侧。
            var facingEast = fx.Host.ResolveAtPose(new Id("target.chain.rapose_cone"), Caster, new Vec2(100, 0), 0.0);
            Assert.Equal(new[] { east }, facingEast.Targets.Select(t => t.Target).ToArray());
        }

        [Fact]
        public void ResolveAtPose_RectRotatesWithTheFacing()
        {
            var fx = TargetHostTests.Build(Rows, (units, spatial, powers, threat) => { });
            Place(fx, Caster, HeroFaction, new Vec2(0, 0));
            var north = new Id("unit.rapose_north");
            Place(fx, north, MonsterFaction, new Vec2(0, 15));

            Assert.Empty(fx.Host.ResolveAtPose(new Id("target.chain.rapose_rect"), Caster, Vec2.Zero, 0.0).Targets.Where(t => t.Target.Equals(north)));
            var rotated = fx.Host.ResolveAtPose(new Id("target.chain.rapose_rect"), Caster, Vec2.Zero, Math.PI / 2);
            Assert.Contains(north, rotated.Targets.Select(t => t.Target));
        }

        [Fact]
        public void ResolveAtPose_AtTheCastersOwnPose_EqualsResolveWithCoefficients_AndSortsFromThePoseOrigin()
        {
            var fx = TargetHostTests.Build(Rows, (units, spatial, powers, threat) => { });
            Place(fx, Caster, HeroFaction, new Vec2(5, 5), facing: 0.7);
            Place(fx, new Id("unit.rapose_a"), MonsterFaction, new Vec2(15, 9));
            Place(fx, new Id("unit.rapose_b"), MonsterFaction, new Vec2(9, 6));

            var chain = new Id("target.chain.rapose_circle");
            var viaPose = fx.Host.ResolveAtPose(chain, Caster, new Vec2(5, 5), 0.7);
            var viaDefault = fx.Host.ResolveWithCoefficients(chain, Caster);
            Assert.Equal(viaDefault.Targets, viaPose.Targets);

            // nearest_in_shape 的距离基准是给定位姿的原点：位姿移到 (15, 9) 旁边，最近的变成 a。
            var moved = fx.Host.ResolveAtPose(chain, Caster, new Vec2(15.5, 9.5), 0.0);
            Assert.Equal(new Id("unit.rapose_a"), Assert.Single(moved.Targets).Target);
        }

        [Fact]
        public void ResolveAtPose_DoesNotPublishTargetingResolved()
        {
            var fx = TargetHostTests.Build(
                Rows, (units, spatial, powers, threat) => { },
                options: new Core.Rules.Targeting.TargetingOptions { EmitResolvedEvent = true });
            Place(fx, Caster, HeroFaction, new Vec2(0, 0));
            Place(fx, new Id("unit.rapose_t"), MonsterFaction, new Vec2(10, 0));
            var seen = new List<TargetingResolvedEvent>();
            fx.Bus.Subscribe<TargetingResolvedEvent>(RulesEventKeys.TargetingResolved, e => seen.Add(e));

            fx.Host.ResolveAtPose(new Id("target.chain.rapose_circle"), Caster, Vec2.Zero, 0.0);
            fx.Bus.DispatchPending();
            Assert.Empty(seen);

            fx.Host.Resolve(new Id("target.chain.rapose_circle"), Caster);
            fx.Bus.DispatchPending();
            Assert.Single(seen);
        }
    }
}
