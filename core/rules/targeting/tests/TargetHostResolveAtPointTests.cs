using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Targeting
{
    /// <summary>
    /// T-L14（测试覆盖剩余项 2026-10-01）：<c>TargetHost.ResolveAtPoint</c>（ADR-0027 地面坐标施法）的直接用例。
    /// 此前只经 <c>CastPipeline.CastSkillAtGround</c> 间接触及，且多用 Fake 目标宿主，真实 <c>TargetHost</c> 的
    /// "锚点换成显式坐标、朝向固定 +X、不发布 targeting.resolved、不继承 currentTarget"几条判断记录没有断言。
    /// </summary>
    public class TargetHostResolveAtPointTests
    {
        private static readonly Id HeroFaction = new Id("fac.target_test_hero");
        private static readonly Id MonsterFaction = new Id("fac.target_test_monster");
        private static readonly Id Caster = new Id("unit.rap_caster");
        private static readonly Vec2 Point = new Vec2(200, 0);

        private static void Place(TargetHostTests.Fixture fx, Id id, Id faction, Vec2 pos, double facing = 0)
        {
            fx.Units.Add(id, faction, pos, facing: facing);
            fx.Spatial.Register(id, pos, 0);
        }

        [Fact]
        public void Circle_AnchorsAtPoint_NotAtCaster()
        {
            const string rows = @"[
                { ""id"": ""target.chain.rap_circle"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 20 }, ""max_targets"": 0 }
            ]";
            var nearPoint = new Id("unit.rap_near_point");
            var nearCaster = new Id("unit.rap_near_caster");
            var fx = TargetHostTests.Build(rows, (units, spatial, powers, threat) => { });
            Place(fx, Caster, HeroFaction, new Vec2(0, 0));
            Place(fx, nearPoint, MonsterFaction, new Vec2(Point.X + 5, 0));
            Place(fx, nearCaster, MonsterFaction, new Vec2(5, 0));

            var atPoint = fx.Host.ResolveAtPoint(new Id("target.chain.rap_circle"), Caster, Point);
            var atCaster = fx.Host.Resolve(new Id("target.chain.rap_circle"), Caster);

            Assert.Equal(new[] { nearPoint }, atPoint);
            Assert.DoesNotContain(nearPoint, atCaster);
            Assert.Contains(nearCaster, atCaster);
        }

        [Fact]
        public void NearestInShape_MeasuresDistanceFromPoint()
        {
            const string rows = @"[
                { ""id"": ""target.chain.rap_nearest"", ""source"": ""nearest_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 500 } }
            ]";
            var closeToPointFarFromCaster = new Id("unit.rap_a_far_from_caster");
            var closeToCasterFarFromPoint = new Id("unit.rap_b_close_to_caster");
            var fx = TargetHostTests.Build(rows, (units, spatial, powers, threat) => { });
            Place(fx, Caster, HeroFaction, new Vec2(0, 0));
            Place(fx, closeToCasterFarFromPoint, MonsterFaction, new Vec2(10, 0));
            Place(fx, closeToPointFarFromCaster, MonsterFaction, new Vec2(Point.X + 3, 0));

            var result = fx.Host.ResolveAtPoint(new Id("target.chain.rap_nearest"), Caster, Point);

            Assert.Equal(new[] { closeToPointFarFromCaster }, result);
        }

        [Fact]
        public void DirectionalShape_FacingIsFixedAlongPositiveX_IgnoringCasterFacing()
        {
            const string rows = @"[
                { ""id"": ""target.chain.rap_cone"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""cone"", ""radius"": 50, ""angle"": 1.5707963267948966 }, ""max_targets"": 0 }
            ]";
            var ahead = new Id("unit.rap_ahead");
            var behind = new Id("unit.rap_behind");
            var fx = TargetHostTests.Build(rows, (units, spatial, powers, threat) => { });
            // 施法者朝向 -X（facing=π）：若 ResolveAtPoint 继承施法者朝向，扇区会指向 -X 而选中 behind。
            Place(fx, Caster, HeroFaction, new Vec2(0, 0), facing: Math.PI);
            Place(fx, ahead, MonsterFaction, new Vec2(Point.X + 10, 0));
            Place(fx, behind, MonsterFaction, new Vec2(Point.X - 10, 0));

            var atPoint = fx.Host.ResolveAtPoint(new Id("target.chain.rap_cone"), Caster, Point);

            Assert.Equal(new[] { ahead }, atPoint);
        }

        [Fact]
        public void Filters_StillUseCaster_HostileOnlyAndNotSelf()
        {
            const string rows = @"[
                { ""id"": ""target.chain.rap_filtered"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 30 },
                  ""filters"": [""relation:hostile"", ""relation:not_self""], ""max_targets"": 0 }
            ]";
            var hostile = new Id("unit.rap_hostile");
            var friendly = new Id("unit.rap_friendly");
            var fx = TargetHostTests.Build(rows, (units, spatial, powers, threat) => { });
            // 施法者本人站在落点上：not_self 必须仍把他剔除。
            Place(fx, Caster, HeroFaction, Point);
            Place(fx, hostile, MonsterFaction, new Vec2(Point.X + 4, 0));
            Place(fx, friendly, HeroFaction, new Vec2(Point.X - 4, 0));

            var result = fx.Host.ResolveAtPoint(new Id("target.chain.rap_filtered"), Caster, Point);

            Assert.Equal(new[] { hostile }, result);
        }

        [Fact]
        public void EmptyPrimary_FallbackChain_IsAlsoAnchoredAtPoint()
        {
            const string rows = @"[
                { ""id"": ""target.chain.rap_primary"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 5 }, ""fallback"": ""target.chain.rap_fallback"" },
                { ""id"": ""target.chain.rap_fallback"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 40 }, ""max_targets"": 0 }
            ]";
            var outsidePrimary = new Id("unit.rap_outside_primary");
            var fx = TargetHostTests.Build(rows, (units, spatial, powers, threat) => { });
            Place(fx, Caster, HeroFaction, new Vec2(0, 0));
            Place(fx, outsidePrimary, MonsterFaction, new Vec2(Point.X + 25, 0));

            var result = fx.Host.ResolveAtPoint(new Id("target.chain.rap_primary"), Caster, Point);

            Assert.Equal(new[] { outsidePrimary }, result);
        }

        [Fact]
        public void CurrentTargetSource_YieldsEmpty_BecauseGroundRequestHasNoCurrentTarget()
        {
            const string rows = @"[
                { ""id"": ""target.chain.rap_current"", ""source"": ""current_target"" }
            ]";
            var fx = TargetHostTests.Build(rows, (units, spatial, powers, threat) => { });
            Place(fx, Caster, HeroFaction, new Vec2(0, 0));

            var result = fx.Host.ResolveAtPoint(new Id("target.chain.rap_current"), Caster, Point);

            Assert.Empty(result);
        }

        [Fact]
        public void UnknownChain_ThrowsArgumentException()
        {
            var fx = TargetHostTests.Build("[]", (units, spatial, powers, threat) => { });
            Place(fx, Caster, HeroFaction, new Vec2(0, 0));

            Assert.Throws<ArgumentException>(() =>
                fx.Host.ResolveAtPoint(new Id("target.chain.rap_missing"), Caster, Point));
        }

        [Fact]
        public void DoesNotPublishTargetingResolved_WhileResolveDoes()
        {
            const string rows = @"[
                { ""id"": ""target.chain.rap_event"", ""source"": ""all_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 20 }, ""max_targets"": 0 }
            ]";
            var fx = TargetHostTests.Build(
                rows, (units, spatial, powers, threat) => { },
                options: new Core.Rules.Targeting.TargetingOptions { EmitResolvedEvent = true });
            Place(fx, Caster, HeroFaction, new Vec2(0, 0));
            Place(fx, new Id("unit.rap_event_target"), MonsterFaction, new Vec2(Point.X, 0));
            var seen = new List<TargetingResolvedEvent>();
            fx.Bus.Subscribe<TargetingResolvedEvent>(RulesEventKeys.TargetingResolved, e => seen.Add(e));

            fx.Host.ResolveAtPoint(new Id("target.chain.rap_event"), Caster, Point);
            fx.Bus.DispatchPending();
            Assert.Empty(seen);

            fx.Host.Resolve(new Id("target.chain.rap_event"), Caster);
            fx.Bus.DispatchPending();
            Assert.Single(seen);
        }
    }
}
