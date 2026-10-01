using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Carriers.Unit
{
    /// <summary>
    /// 时间线目标辅助的载体层实现（手感设计/02 第 5 节）：<see cref="TargetChainAssistResolver"/>（链候选 → 第一个在距离与角度内的存活目标）
    /// 与 <see cref="ActionTargetAssistAdapter"/>（朝向修正不超过档案上限、<c>close_distance</c> 距离缩放）、
    /// 以及 <see cref="WorldUnitAccess.SetFacing"/>（<see cref="IUnitFacingWriter"/>）。
    /// </summary>
    public class ActionTargetAssistTests
    {
        private static readonly Id MapId = new Id("map.assist");
        private static readonly Id Faction = new Id("fac.assist");
        private static readonly Id Archetype = new Id("arch.class.sample");
        private static readonly Id Chain = new Id("target.chain.assist");
        private static readonly Id Actor = new Id("unit.assist_actor");

        private sealed class ChainHost : ITargetHost
        {
            public IReadOnlyList<Id> Candidates = Array.Empty<Id>();

            public IReadOnlyList<Id> Resolve(Id chainId, Id casterId) => Candidates;

            public IReadOnlyList<Id> Resolve(Id chainId, Id casterId, Id? currentTarget) => Candidates;

            public IReadOnlyList<Id> FilterExplicitTargets(Id chainId, Id casterId, IReadOnlyList<Id> targets) => targets;
        }

        private static (WorldUnitAccess Units, ChainHost Host, WorldSim World) Build()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world, bus);
            world.AddEntity(new PlayerUnit(Actor, MapId, Faction, Archetype) { Position = Vec2.Zero });
            return (units, new ChainHost(), world);
        }

        private static Id Add(WorldSim world, string id, Vec2 position)
        {
            var unit = new PlayerUnit(new Id(id), MapId, Faction, Archetype) { Position = position };
            world.AddEntity(unit);
            return unit.EntityId;
        }

        private static double Deg(double radians) => radians * 180.0 / Math.PI;

        [Fact]
        public void SetFacing_WritesTheUnitFacing_AndUnknownUnitThrows()
        {
            var (units, _, _) = Build();
            units.SetFacing(Actor, 1.25);
            Assert.Equal(1.25, units.GetFacing(Actor));
            Assert.Throws<InvalidOperationException>(() => units.SetFacing(new Id("unit.assist_unknown"), 0.5));
        }

        [Fact]
        public void Resolver_PicksTheFirstChainCandidateInsideDistanceAndAngle()
        {
            var (units, host, world) = Build();
            var tooFar = Add(world, "unit.assist_far", new Vec2(9, 0));
            var behind = Add(world, "unit.assist_behind", new Vec2(-2, 0));
            var dead = Add(world, "unit.assist_dead", new Vec2(1, 0));
            var good = Add(world, "unit.assist_good", new Vec2(2, 1));
            var later = Add(world, "unit.assist_later", new Vec2(3, 0));
            units.SetAlive(dead, false);
            host.Candidates = new[] { Actor, tooFar, behind, dead, good, later };

            var resolver = new TargetChainAssistResolver(host, units);
            Assert.True(resolver.TryResolve(new TargetAssistRequest(Actor, Chain.Value, 5.0, 60.0, TargetAssistMode.FaceOnly), out var candidate));
            Assert.Equal(good, candidate.TargetId);
            Assert.Equal(new Vec2(2, 1), candidate.Position);

            // 角度上限收紧到比目标的 26.6 度还小：下一个满足的是正前方的 later。
            Assert.True(resolver.TryResolve(new TargetAssistRequest(Actor, Chain.Value, 5.0, 10.0, TargetAssistMode.FaceOnly), out var narrow));
            Assert.Equal(later, narrow.TargetId);

            host.Candidates = new[] { Actor, tooFar, behind };
            Assert.False(resolver.TryResolve(new TargetAssistRequest(Actor, Chain.Value, 5.0, 60.0, TargetAssistMode.FaceOnly), out _));
        }

        [Fact]
        public void Adapter_FacingDelta_FollowsTheTargetBearing_NeverBeyondTheProfileCap()
        {
            var (units, host, world) = Build();
            var target = Add(world, "unit.assist_t", new Vec2(1, 2)); // 方位约 63.4 度。
            host.Candidates = new[] { target };
            var adapter = new ActionTargetAssistAdapter(new TargetChainAssistResolver(host, units), units);
            var bearing = Deg(Math.Atan2(2, 1));

            foreach (var cap in new[] { 0.0, 20.0, 45.0, 90.0 })
            {
                var request = new ActionAssistRequest(Actor, new Id("cast.assist"), Chain, 10.0, 90.0, TimelineAssistMode.FaceOnly, cap, 0.0, 0.0);
                Assert.True(adapter.TryAssist(request, out var outcome));
                Assert.Equal(target, outcome.TargetId);
                Assert.Equal(Math.Min(bearing, cap), outcome.FacingDeltaDeg, 9);
                Assert.InRange(Math.Abs(outcome.FacingDeltaDeg), 0.0, cap + 1e-9);
                Assert.Equal(0.0, outcome.DistanceAdjust);
            }
        }

        [Fact]
        public void Adapter_CloseDistance_ShrinksTheDashToWhatTheShapeNeedsToCoverTheTarget()
        {
            var (units, host, world) = Build();
            var target = Add(world, "unit.assist_t", new Vec2(4, 0));
            host.Candidates = new[] { target };
            var adapter = new ActionTargetAssistAdapter(new TargetChainAssistResolver(host, units), units);

            ActionAssistOutcome Run(double declared, double reach)
            {
                var request = new ActionAssistRequest(Actor, new Id("cast.assist"), Chain, 10.0, 90.0, TimelineAssistMode.CloseDistance, 45.0, declared, reach);
                Assert.True(adapter.TryAssist(request, out var outcome));
                return outcome;
            }

            // 目标 4 远、形状覆盖 1：需要位移 3。声明 5 时缩到 3（调整 -2）；声明 2 时不超过声明（调整 0 的反面：2-2=0，目标够不着也不多跑）。
            Assert.Equal(3.0 - 5.0, Run(5.0, 1.0).DistanceAdjust, 9);
            Assert.Equal(0.0, Run(2.0, 1.0).DistanceAdjust, 9);
            Assert.Equal(0.0 - 5.0, Run(5.0, 6.0).DistanceAdjust, 9); // 形状已经够得着：不需要位移。
        }

        [Fact]
        public void Adapter_NoCandidateInRange_IsSilent()
        {
            var (units, host, world) = Build();
            host.Candidates = new[] { Add(world, "unit.assist_far", new Vec2(50, 0)) };
            var adapter = new ActionTargetAssistAdapter(new TargetChainAssistResolver(host, units), units);
            var request = new ActionAssistRequest(Actor, new Id("cast.assist"), Chain, 10.0, 90.0, TimelineAssistMode.FaceOnly, 45.0, 0.0, 0.0);
            Assert.False(adapter.TryAssist(request, out _));
        }
    }
}
