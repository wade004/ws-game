// ADR-0103 决定 1（消费方反馈第五十一批·反馈 1）：SummonTickHandler.TryFollow 在直接跟随点不可
// 行走时的候选采样——端到端驱动真实 SummonTickHandler + MovementTickHandler。
using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Summon;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Tests.Carriers.Creature;
using Tests.Carriers.Unit;
using Xunit;

namespace Tests.Carriers.Summon
{
    /// <summary>
    /// 判断记录（不复用 <see cref="SummonTickHandlerTests"/>.Build 的 CreatureFactory/真实 StatHost/
    /// 数据注册表全套装配）：那一套面向"到期/联动/仇恨"等不依赖真实移动执行的既有用例，只捕获
    /// <c>move</c> 意图、不真正驱动位移。本文件需要真实驱动 <see cref="MovementTickHandler"/> 让
    /// 召唤物按算出的目标点实际走动，才能观察"是否收敛/是否冻结"，因此改用最小的
    /// <see cref="FakeCreatureFactory"/> 直接生成 <see cref="CreatureUnit"/>（不经数据注册表/真实
    /// StatHost），惯例同 <c>core/carriers/unit/tests</c> 的 <c>FakeStatHost</c>/<c>FakeAuraQuery</c>
    /// "只覆盖测试实际用到的行为"；导航假件复用 <c>core/carriers/unit/tests</c>（<see
    /// cref="UnitChaseTests"/>）已经在用的 <see cref="StubNavigation2D"/>（按阻挡矩形判
    /// <c>IsWalkable</c>、起终点可走则两点直线否则 <c>null</c>），不重复另建一份。
    /// </summary>
    public class SummonFollowNavigationTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id OwnerFaction = new Id("fac.test_player");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");
        private static readonly Id OwnerId = new Id("unit.owner_nav_test");
        private static readonly Id TemplateId = new Id("creature.sample_follow_nav");

        /// <summary><see cref="ICreatureFactory"/> 的最小假实现：直接在 <paramref name="position"/>
        /// 生成一个 <see cref="CreatureUnit"/> 并接入世界模拟，不经数据注册表——本文件的用例只关心
        /// 跟随目标点的可行走性采样与移动收敛，不需要真实的模板/属性装配。</summary>
        private sealed class FakeCreatureFactory : ICreatureFactory
        {
            private readonly IWorldSim _world;
            private int _counter;

            public FakeCreatureFactory(IWorldSim world)
            {
                _world = world;
            }

            public Id Spawn(Id templateId, Id mapId, Vec2 position, double facing, Id? ownerId = null)
            {
                var id = new Id($"unit.summon_nav_test_{++_counter}");
                var unit = new CreatureUnit(id, mapId, OwnerFaction, templateId)
                {
                    Position = position,
                    Facing = facing,
                };
                _world.AddEntity(unit);
                return id;
            }

            public void Despawn(Id entityId, string reason)
            {
                throw new NotSupportedException("FakeCreatureFactory 不支持 Despawn（本文件用例不需要）");
            }
        }

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public SummonHost SummonHost = null!;
            public MovementHost MovementHost = null!;
            public StubNavigation2D Navigation = null!;
            public FakeStatHost Stats = null!;
            public List<(Id UnitId, Vec2 From, Vec2 To, MoveFailReason Reason)> FailedDetailed = null!;
        }

        private static Fixture Build(SummonOptions? summonOptions = null, double summonSpeed = 5.0)
        {
            var bus = CreatureTestSupport.CreateBus();
            var world = new WorldSim(bus);

            var owner = new PlayerUnit(OwnerId, MapId, OwnerFaction, ArchetypeId) { Position = Vec2.Zero };
            world.AddEntity(owner);

            var units = new WorldUnitAccess(world);
            var factory = new FakeCreatureFactory(world);
            var summonHost = new SummonHost(factory, world, units, bus, summonOptions);

            var navigation = new StubNavigation2D();
            var stats = new FakeStatHost();
            var auras = new FakeAuraQuery();
            var movementHost = new MovementHost(world);
            var movementTickHandler = new MovementTickHandler(units, stats, auras, movementHost, bus, navigation);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, movementTickHandler);

            var summonTickHandler = new SummonTickHandler(
                summonHost, units, new FakeCombatHost(), summonOptions, diagnostics: null, aiHost: null,
                navigation: navigation);
            world.RegisterPhaseHandler(TickPhase.AiDecision, summonTickHandler);

            var failedDetailed = new List<(Id, Vec2, Vec2, MoveFailReason)>();
            movementHost.OnMoveFailedDetailed += (unitId, from, to, reason) =>
                failedDetailed.Add((unitId, from, to, reason));

            return new Fixture
            {
                World = world,
                Units = units,
                SummonHost = summonHost,
                MovementHost = movementHost,
                Navigation = navigation,
                Stats = stats,
                FailedDetailed = failedDetailed,
            };
        }

        private static Id SpawnFollower(Fixture f, Vec2 position, double speed)
        {
            var summonId = f.SummonHost.Summon(OwnerId, TemplateId, position, duration: null);
            f.Stats.SetBase(summonId, new MovementOptions().MoveSpeedStat, speed);
            return summonId;
        }

        // -----------------------------------------------------------------
        // 复现：owner 紧贴阻挡矩形，直接跟随点落在矩形内、圆上 +δ 候选可走。
        // -----------------------------------------------------------------

        [Fact]
        public void TryFollow_DirectPointBlocked_SamplesCandidate_ConvergesWithoutMoveFailure()
        {
            var f = Build(summonSpeed: 5.0);

            // owner 在原点，召唤物从正西方向 (-10,0) 接近：直接跟随点 = owner - direction*1.5 =
            // (-1.5, 0)（默认 FollowStopDistance=1.5）。阻挡矩形只盖住这一点附近的一小片
            // （x∈[-1.6,-1.4], y∈[-0.3,0.3]），不影响圆上偏移 +δ（约 22.5°）的候选，也不影响召唤物
            // 从远处接近该候选点的直线路径。
            f.Navigation.SetBlocking(MapId, new[] { new Rect(new Vec2(-1.6, -0.3), new Vec2(-1.4, 0.3)) });

            var summonId = SpawnFollower(f, new Vec2(-10, 0), speed: 5.0);

            for (var i = 0; i < 20; i++)
            {
                f.World.Tick(SimStep.Continuous(1.0));

                var distance = (f.Units.GetPosition(OwnerId) - f.Units.GetPosition(summonId)).Length;
                if (distance <= new SummonOptions().FollowDistance)
                {
                    break;
                }
            }

            var finalDistance = (f.Units.GetPosition(OwnerId) - f.Units.GetPosition(summonId)).Length;
            Assert.True(
                finalDistance <= new SummonOptions().FollowDistance,
                $"召唤物未能在 20 tick 内跟上 owner（最终距离 {finalDistance}），期间失败详情：" +
                string.Join("; ", f.FailedDetailed));
            Assert.Empty(f.FailedDetailed); // 修复前：直接跟随点落进阻挡格，逐 tick NoPath，永久冻结。
        }

        // -----------------------------------------------------------------
        // 不变量①：直接点可走 → 意图目标点与 1.83.0 公式逐位相同（阳性对照）。
        // -----------------------------------------------------------------

        [Fact]
        public void TryFollow_DirectPointWalkable_TargetUnchanged_PositiveControl()
        {
            var f = Build(summonSpeed: 5.0);
            // 不设置任何阻挡：直接跟随点必然可走。
            var summonId = SpawnFollower(f, new Vec2(-10, 0), speed: 5.0);

            f.World.Tick(SimStep.Continuous(1.0));

            // 直接跟随点 = owner(0,0) - direction(1,0)*1.5 = (-1.5, 0)；召唤物初始在 (-10,0)，
            // 速度 5、dt=1，本 tick 沿直线移动 5 个单位到 (-5, 0)（阳性对照：与 1.83.0 公式算出的
            // 直线路径逐位一致，未发生任何候选采样）。
            Assert.Equal(new Vec2(-5, 0), f.Units.GetPosition(summonId));
            Assert.Empty(f.FailedDetailed);
        }

        // -----------------------------------------------------------------
        // 不变量②：全部候选不可走 → 目标点 = owner 位置。
        // -----------------------------------------------------------------

        [Fact]
        public void TryFollow_AllCandidatesBlocked_FallsBackToOwnerPosition()
        {
            var f = Build(summonSpeed: 5.0);

            // 阻挡矩形整块盖住 owner 周围半径 1.5 的整个候选圆（边长 4 的正方形，覆盖 [-2,2]x[-2,2]
            // ⊇ 半径 1.5 的圆），召唤物只能贴到 owner 位置本身（唯一未被判定为不可走的点——
            // StubNavigation2D 的 IsWalkable 逐矩形判定，owner 自身坐标 (0,0) 落在阻挡矩形内部，
            // 但 owner 单位本身仍然"存在"于该点，不代表寻路一定成功——本用例只断言
            // ResolveFollowTargetPoint 退化到 owner 坐标这一步，不断言寻路后续是否成功，因此只跑
            // 一个 tick 直接读取本 tick 追加的 move 意图目标点）。
            f.Navigation.SetBlocking(MapId, new[] { new Rect(new Vec2(-2, -2), new Vec2(2, 2)) });

            var captured = new List<Intent>();
            f.World.RegisterPhaseHandler(TickPhase.MovementAndNavigation, new CaptureIntentsHandler(captured));

            var summonId = SpawnFollower(f, new Vec2(-10, 0), speed: 5.0);

            f.World.Tick(SimStep.Continuous(1.0));

            var moveIntent = FindMoveIntent(captured, summonId);
            Assert.NotNull(moveIntent);
            var args = moveIntent!.Value.Args;
            Assert.Equal(0.0, ((Core.Foundation.Common.Json.JsonNumber)args["x"]).Value, 6);
            Assert.Equal(0.0, ((Core.Foundation.Common.Json.JsonNumber)args["y"]).Value, 6);
        }

        // -----------------------------------------------------------------
        // 不变量③：FollowCandidates = 1 → 行为同 1.83.0（直接点不可走照发，失败由移动系统报）。
        // -----------------------------------------------------------------

        [Fact]
        public void TryFollow_FollowCandidatesDisabled_DoesNotSample_BehavesLike1830()
        {
            var options = new SummonOptions { FollowCandidates = 1 };
            var f = Build(options, summonSpeed: 5.0);

            f.Navigation.SetBlocking(MapId, new[] { new Rect(new Vec2(-1.6, -0.3), new Vec2(-1.4, 0.3)) });

            var summonId = SpawnFollower(f, new Vec2(-10, 0), speed: 5.0);

            for (var i = 0; i < 5; i++)
            {
                f.World.Tick(SimStep.Continuous(1.0));
            }

            // FollowCandidates=1：不采样，直接跟随点 (-1.5,0) 恒不可走，逐 tick 建路失败——
            // 移动系统按默认 PathFailurePolicy.KeepOldPath 处理（本用例不关心该分支的续推细节，
            // 只断言"确有失败通知"这一 1.83.0 既有行为未被本次改动的采样逻辑掩盖）。
            Assert.NotEmpty(f.FailedDetailed);
            foreach (var failure in f.FailedDetailed)
            {
                Assert.Equal(MoveFailReason.NoPath, failure.Reason);
            }
        }

        private sealed class CaptureIntentsHandler : ITickPhaseHandler
        {
            private readonly List<Intent> _sink;

            public CaptureIntentsHandler(List<Intent> sink)
            {
                _sink = sink;
            }

            public void Execute(SimStep step, IWorldSim world)
            {
                _sink.Clear();
                _sink.AddRange(world.CurrentIntents);
            }
        }

        private static Intent? FindMoveIntent(IReadOnlyList<Intent> intents, Id actorId)
        {
            for (var i = 0; i < intents.Count; i++)
            {
                if (intents[i].ActorId.Equals(actorId) && intents[i].Kind == "move")
                {
                    return intents[i];
                }
            }
            return null;
        }
    }
}
