// VfxPlayerColdLoadFollowTests：T-H14 剩余半（测试覆盖剩余项第四批，拍板"补齐"）。
// ADR-0121 D1 已补"冷加载中可 Stop"的复现与不变量（VfxPlayerTests）；这里补的是同一条冷加载出口
// 上与"位置"有关的三件事，对照 AGENTS.md 第 0 节"冷加载路径必须与热路径行为一致"：
//   1. attach_mode=socket（降级为 world）与 screen 的冷加载：排队、补发、Stop 取消；
//   2. 冷加载补发之后，anchor / 降级 socket 的跟随登记与热路径一致（下一次 Update 起每帧重新解析、
//      目标丢失时结束）；screen 不跟随；
//   3. 冷加载排队时位置冻结：位置在 Spawn 调用当下解析（冷、热同一时刻），排队期间目标移动不会
//      改变补发位置；补发后的第一次 Update 起与热路径收敛到同一位置。
// 期望值由规则算出：补发位置 = Spawn 时刻解析结果；Update 之后位置 = 目标当前解析结果。
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    public class VfxPlayerColdLoadFollowTests
    {
        private static readonly Id AnchorVfx = new Id("vfx.cold_anchor");
        private static readonly Id SocketVfx = new Id("vfx.cold_socket");
        private static readonly Id ScreenVfx = new Id("vfx.cold_screen");

        private static readonly Id AnchorRes = new Id("res.cold_anchor");
        private static readonly Id SocketRes = new Id("res.cold_socket");
        private static readonly Id ScreenRes = new Id("res.cold_screen");

        private static readonly Id Hero = new Id("unit.hero");
        private static readonly Id Rival = new Id("unit.rival");
        private static readonly Id HandAnchor = new Id("anchor.hand_main");
        private static readonly Id HandSocket = new Id("socket.main_hand");

        private static Dictionary<Id, VfxDef> BuildCatalog() => new Dictionary<Id, VfxDef>
        {
            [AnchorVfx] = new VfxDef(AnchorVfx, "buff", VfxAttachMode.Anchor, lifetime: null, AnchorRes),
            [SocketVfx] = new VfxDef(SocketVfx, "weapon", VfxAttachMode.Socket, lifetime: null, SocketRes),
            [ScreenVfx] = new VfxDef(ScreenVfx, "ui", VfxAttachMode.Screen, lifetime: null, ScreenRes),
        };

        /// <summary>可调平移的相机：ScreenToWorld = 屏幕坐标 + 当前平移量；可置为"找不到交点"。</summary>
        private sealed class ShiftableCamera : ICamera
        {
            public Vec2 Shift = Vec2.Zero;
            public bool Resolvable = true;

            public void Configure(double pitchDegrees, double yawDegrees, ZoomRange zoomRange) { }
            public void Follow(Vec2 planePos, double smoothing) { }
            public void SetZoom(double zoom) { }
            public Vec2 WorldToScreen(Vec2 planePos, double height) => planePos;
            public Vec2? ScreenToWorld(Vec2 screen) =>
                Resolvable ? new Vec2(screen.X + Shift.X, screen.Y + Shift.Y) : (Vec2?)null;
            public void Shake(double intensity, double durationSeconds, double frequency) { }
        }

        /// <summary>被测对象 + 可操纵的目标位置 / 存活状态。</summary>
        private sealed class Rig
        {
            public readonly FollowCapableStubRenderer2D Renderer = new FollowCapableStubRenderer2D();
            public readonly StubResourceLoader Loader = new StubResourceLoader();
            public readonly ShiftableCamera Camera = new ShiftableCamera();
            public readonly VfxPlayer Player;

            public Vec2 AnchorPos = new Vec2(1, 1);
            public Vec2 EntityPos = new Vec2(2, 2);
            public bool Alive = true;

            public Rig(VfxOptions? options = null)
            {
                Player = new VfxPlayer(
                    Renderer, Camera, BuildCatalog(), options,
                    anchorResolver: (e, a) => Alive && e.Equals(Hero) && a.Equals(HandAnchor) ? AnchorPos : (Vec2?)null,
                    entityPositionResolver: e => Alive && e.Equals(Hero) ? EntityPos : (Vec2?)null,
                    resourceLoader: Loader);
            }

            public ParticleHandle SpawnAnchor() => Spawn(AnchorVfx, VfxAttach.Anchor(Hero, HandAnchor));
            public ParticleHandle SpawnSocket() => Spawn(SocketVfx, VfxAttach.Socket(Hero, HandSocket));

            public ParticleHandle Spawn(Id vfx, VfxAttach at)
            {
                var handle = Player.Spawn(vfx, at, null);
                Assert.NotNull(handle);
                return handle!.Value;
            }

            /// <summary>当前唯一存活粒子的坐标。</summary>
            public Vec2 OnlyParticlePosition() => Assert.Single(Renderer.ParticlePositions).Value;
        }

        // -----------------------------------------------------------------
        // 1. socket（降级为 world）冷加载
        // -----------------------------------------------------------------

        [Fact]
        public void ColdLoad_SocketDowngraded_QueuesWithUsableHandle_EmitsAtSpawnTimePosition_ThenFollowsEntity()
        {
            var rig = new Rig { EntityPos = new Vec2(1, 1) };
            rig.Loader.DeferCallbacks = true;
            rig.Loader.Register(SocketRes);

            var spawnPos = rig.EntityPos;
            var handle = rig.SpawnSocket();

            Assert.Equal(1, rig.Player.PendingSpawnCount);
            Assert.Empty(rig.Renderer.ParticlePositions);

            // 排队期间宿主实体移动：补发位置仍是 Spawn 时刻的解析结果（冻结），不是补发时刻的位置。
            var laterPos = new Vec2(spawnPos.X + 3, spawnPos.Y + 4);
            rig.EntityPos = laterPos;
            rig.Player.Update(0.016); // 排队期间的 Update 不得登记/触发任何重定位。
            Assert.Equal(0, rig.Renderer.SetParticlePositionCallCount);

            rig.Loader.CompletePending(SocketRes);

            Assert.Equal(0, rig.Player.PendingSpawnCount);
            Assert.Equal(spawnPos, rig.OnlyParticlePosition());

            // 补发之后下一次 Update 起与热路径一致：每帧跟随实体当前位置。
            rig.Player.Update(0.016);
            Assert.Equal(laterPos, rig.OnlyParticlePosition());

            var movedAgain = new Vec2(laterPos.X - 7, laterPos.Y + 1);
            rig.EntityPos = movedAgain;
            rig.Player.Update(0.016);
            Assert.Equal(movedAgain, rig.OnlyParticlePosition());

            // 占位句柄仍能停到补发出来的粒子。
            rig.Player.Stop(handle);
            Assert.Empty(rig.Renderer.ParticlePositions);
        }

        [Fact]
        public void ColdLoad_SocketDowngraded_StopBeforeLoadCompletes_NeverEmits_NoFollowRegistered()
        {
            var rig = new Rig();
            rig.Loader.DeferCallbacks = true;
            rig.Loader.Register(SocketRes);

            var handle = rig.SpawnSocket();
            rig.Player.Stop(handle);
            Assert.Equal(0, rig.Player.PendingSpawnCount);

            rig.Loader.CompletePending(SocketRes);
            rig.EntityPos = new Vec2(50, 50);
            rig.Player.Update(0.016);

            Assert.Empty(rig.Renderer.ParticlePositions);
            Assert.Equal(0, rig.Renderer.SetParticlePositionCallCount);
        }

        [Fact]
        public void ColdLoad_SocketDowngraded_StopAfterLoadCompletes_UnregistersFollow()
        {
            var rig = new Rig();
            rig.Loader.DeferCallbacks = true;
            rig.Loader.Register(SocketRes);

            var handle = rig.SpawnSocket();
            rig.Loader.CompletePending(SocketRes);
            rig.Player.Update(0.016);
            var callsWhileAlive = rig.Renderer.SetParticlePositionCallCount;
            Assert.True(callsWhileAlive >= 1);

            rig.Player.Stop(handle);
            rig.EntityPos = new Vec2(80, 80);
            rig.Player.Update(0.016);

            Assert.Empty(rig.Renderer.ParticlePositions);
            Assert.Equal(callsWhileAlive, rig.Renderer.SetParticlePositionCallCount);
        }

        [Fact]
        public void ColdLoad_SocketDowngraded_EntityPositionUnresolvable_SkipsBeforeQueuing_NoLoadRequest()
        {
            var rig = new Rig { Alive = false };
            rig.Loader.DeferCallbacks = true;

            var handle = rig.Player.Spawn(SocketVfx, VfxAttach.Socket(Hero, HandSocket), null);

            // 与热路径同一出口：位置解析失败先于加载排队，不发起无意义的资源加载。
            Assert.Null(handle);
            Assert.Equal(0, rig.Player.PendingSpawnCount);
            Assert.Empty(rig.Loader.LoadRequests);
        }

        // -----------------------------------------------------------------
        // 2. screen 冷加载
        // -----------------------------------------------------------------

        [Fact]
        public void ColdLoad_Screen_QueuesWithUsableHandle_EmitsAtSpawnTimeResolvedPosition_NeverFollows()
        {
            var rig = new Rig();
            rig.Loader.DeferCallbacks = true;
            rig.Loader.Register(ScreenRes);
            var screen = new Vec2(0.25, 0.75);
            rig.Camera.Shift = new Vec2(10, 20);
            var resolvedAtSpawn = rig.Camera.ScreenToWorld(screen)!.Value;

            var handle = rig.Spawn(ScreenVfx, VfxAttach.Screen(screen));
            Assert.Equal(1, rig.Player.PendingSpawnCount);
            Assert.Empty(rig.Renderer.ParticlePositions);

            // 排队期间镜头移动：补发位置仍是 Spawn 调用当下经 ScreenToWorld 解析的世界坐标。
            rig.Camera.Shift = new Vec2(-5, 99);
            rig.Loader.CompletePending(ScreenRes);

            Assert.Equal(resolvedAtSpawn, rig.OnlyParticlePosition());

            // screen 模式本身是固定坐标语义：补发之后任何 Update 都不重定位。
            rig.Player.Update(0.016);
            rig.Player.Update(0.016);
            Assert.Equal(resolvedAtSpawn, rig.OnlyParticlePosition());
            Assert.Equal(0, rig.Renderer.SetParticlePositionCallCount);

            rig.Player.Stop(handle);
            Assert.Empty(rig.Renderer.ParticlePositions);
        }

        [Fact]
        public void ColdLoad_Screen_StopBeforeLoadCompletes_NeverEmits()
        {
            var rig = new Rig();
            rig.Loader.DeferCallbacks = true;
            rig.Loader.Register(ScreenRes);

            var handle = rig.Spawn(ScreenVfx, VfxAttach.Screen(new Vec2(0.5, 0.5)));
            rig.Player.Stop(handle);
            rig.Loader.CompletePending(ScreenRes);

            Assert.Empty(rig.Renderer.ParticlePositions);
            Assert.Equal(0, rig.Player.PendingSpawnCount);
        }

        [Fact]
        public void ColdLoad_Screen_Unresolvable_SkipOn_ReturnsNullBeforeQueuing_NoLoadRequest()
        {
            var rig = new Rig();
            rig.Loader.DeferCallbacks = true;
            rig.Camera.Resolvable = false;

            var handle = rig.Player.Spawn(ScreenVfx, VfxAttach.Screen(new Vec2(0.5, 0.5)), null);

            Assert.Null(handle);
            Assert.Equal(0, rig.Player.PendingSpawnCount);
            Assert.Empty(rig.Loader.LoadRequests);
        }

        [Fact]
        public void ColdLoad_Screen_Unresolvable_SkipOff_QueuesAndEmitsAtOrigin()
        {
            var options = new VfxOptions { SkipOnUnresolvableScreenAttach = false };
            var rig = new Rig(options);
            rig.Loader.DeferCallbacks = true;
            rig.Loader.Register(ScreenRes);
            rig.Camera.Resolvable = false;

            rig.Spawn(ScreenVfx, VfxAttach.Screen(new Vec2(0.5, 0.5)));
            Assert.Equal(1, rig.Player.PendingSpawnCount);

            rig.Camera.Resolvable = true; // 补发时刻相机已可解析：位置仍冻结在 Spawn 时刻的兜底值。
            rig.Loader.CompletePending(ScreenRes);

            Assert.Equal(Vec2.Zero, rig.OnlyParticlePosition());
        }

        // -----------------------------------------------------------------
        // 3. 冷加载后跟随注册 / 位置冻结的冷热收敛不变量
        // -----------------------------------------------------------------

        public static IEnumerable<object[]> ModePathMatrix()
        {
            foreach (var mode in new[] { "anchor", "socket" })
            {
                foreach (var path in new[] { "hot", "cold_sync", "cold_async" })
                {
                    yield return new object[] { mode, path };
                }
            }
        }

        /// <summary>不变量：不论热路径、同步命中的冷路径还是真正异步的冷路径，目标在 Spawn 之后移动，
        /// 补发（如有）之后再经过一次 Update，粒子都收敛到目标当前位置；目标丢失后都被结束且不再有
        /// 重定位调用。冷异步路径中间一帧（补发当下）按设计停在 Spawn 时刻位置。</summary>
        [Theory]
        [MemberData(nameof(ModePathMatrix))]
        public void SpawnThenMoveThenUpdate_ConvergesToCurrentTarget_AcrossHotAndColdPaths(string mode, string path)
        {
            var rig = new Rig { AnchorPos = new Vec2(1, 1), EntityPos = new Vec2(1, 1) };
            var resource = mode == "anchor" ? AnchorRes : SocketRes;
            rig.Loader.Register(resource);
            rig.Loader.DeferCallbacks = path == "cold_async";
            if (path == "hot")
            {
                rig.Loader.LoadAsync(resource, ResourceKind.Effect, (_, __) => { });
            }

            var spawnPos = new Vec2(1, 1);
            rig.Alive = true;
            var handle = mode == "anchor" ? rig.SpawnAnchor() : rig.SpawnSocket();

            var moved = new Vec2(spawnPos.X + 6, spawnPos.Y - 2);
            rig.AnchorPos = moved;
            rig.EntityPos = moved;

            if (path == "cold_async")
            {
                Assert.Equal(1, rig.Player.PendingSpawnCount);
                Assert.Empty(rig.Renderer.ParticlePositions);
                rig.Loader.CompletePending(resource);
                // 冻结：补发位置是 Spawn 时刻的解析结果。
                Assert.Equal(spawnPos, rig.OnlyParticlePosition());
            }
            else
            {
                Assert.Equal(spawnPos, rig.OnlyParticlePosition());
            }

            rig.Player.Update(0.016);
            Assert.Equal(moved, rig.OnlyParticlePosition()); // 三条路径收敛到同一位置。

            // 目标丢失：三条路径同样结束粒子，之后不再有重定位调用。
            rig.Alive = false;
            rig.Player.Update(0.016);
            Assert.Empty(rig.Renderer.ParticlePositions);

            var calls = rig.Renderer.SetParticlePositionCallCount;
            rig.Alive = true;
            rig.Player.Update(0.016);
            Assert.Equal(calls, rig.Renderer.SetParticlePositionCallCount);

            // 句柄此时已结束，再 Stop 安全忽略（冷路径下 handle 是占位句柄，同样适用）。
            Assert.Null(Record.Exception(() => rig.Player.Stop(handle)));
        }

        [Fact]
        public void ColdLoad_Anchor_TargetLostWhilePending_StillEmitsAtSpawnPosition_ThenEndsOnNextUpdate()
        {
            var rig = new Rig { AnchorPos = new Vec2(3, 3) };
            rig.Loader.DeferCallbacks = true;
            rig.Loader.Register(AnchorRes);
            var spawnPos = rig.AnchorPos;

            rig.SpawnAnchor();
            rig.Alive = false; // 排队期间宿主实体销毁。
            rig.Loader.CompletePending(AnchorRes);

            // 补发位置是 Spawn 时刻的解析结果；跟随登记与热路径一致，于是下一次 Update 起按
            // "目标丢失 → 结束"处理，不留悬空静止实例。
            Assert.Equal(spawnPos, rig.OnlyParticlePosition());
            rig.Player.Update(0.016);
            Assert.Empty(rig.Renderer.ParticlePositions);
        }

        [Fact]
        public void ColdLoad_Anchor_TwoQueuedRequests_EachFrozenAtOwnSpawnPosition_ThenEachFollowsOwnTarget()
        {
            // 第二个目标：另一个实体 Rival（同一锚点 id），独立位置。
            var renderer = new FollowCapableStubRenderer2D();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            loader.Register(AnchorRes);
            var positions = new Dictionary<Id, Vec2>
            {
                [Hero] = new Vec2(1, 1),
                [Rival] = new Vec2(-1, -1),
            };
            var player = new VfxPlayer(
                renderer, new StubCamera(), BuildCatalog(),
                anchorResolver: (e, a) => positions.TryGetValue(e, out var p) ? p : (Vec2?)null,
                resourceLoader: loader);

            var heroSpawn = positions[Hero];
            var rivalSpawn = positions[Rival];
            Assert.NotNull(player.Spawn(AnchorVfx, VfxAttach.Anchor(Hero, HandAnchor), null));
            Assert.NotNull(player.Spawn(AnchorVfx, VfxAttach.Anchor(Rival, HandAnchor), null));
            Assert.Equal(2, player.PendingSpawnCount);
            Assert.Single(loader.LoadRequests); // 同一资源只加载一次。

            positions[Hero] = new Vec2(10, 10);
            positions[Rival] = new Vec2(-10, -10);
            loader.CompletePending(AnchorRes);

            // 补发后两个粒子各自停在各自 Spawn 时刻的位置（集合比较：补发顺序不是契约）。
            var afterLoad = new List<Vec2>(renderer.ParticlePositions.Values);
            Assert.Equal(2, afterLoad.Count);
            Assert.Contains(heroSpawn, afterLoad);
            Assert.Contains(rivalSpawn, afterLoad);

            player.Update(0.016);

            var afterUpdate = new List<Vec2>(renderer.ParticlePositions.Values);
            Assert.Equal(2, afterUpdate.Count);
            Assert.Contains(positions[Hero], afterUpdate);
            Assert.Contains(positions[Rival], afterUpdate);
        }

        [Fact]
        public void ColdLoad_WithoutRepositioner_EmitsAtSpawnPosition_AndStaysThere()
        {
            // 未装配 IParticleRepositioner 的渲染器：冷加载补发后与热路径一样"生成后静止"，不抛异常。
            var renderer = new StubRenderer2D();
            var loader = new StubResourceLoader { DeferCallbacks = true };
            loader.Register(SocketRes);
            var entityPos = new Vec2(4, 4);
            var player = new VfxPlayer(
                renderer, new StubCamera(), BuildCatalog(),
                entityPositionResolver: e => entityPos, resourceLoader: loader);

            var spawnPos = entityPos;
            var handle = player.Spawn(SocketVfx, VfxAttach.Socket(Hero, HandSocket), null);
            Assert.NotNull(handle);
            entityPos = new Vec2(40, 40);
            loader.CompletePending(SocketRes);

            Assert.Null(Record.Exception(() => player.Update(0.016)));

            var emitted = Assert.Single(renderer.EmittedParticles);
            Assert.Equal(spawnPos, emitted.Value.Position);
        }
    }
}
