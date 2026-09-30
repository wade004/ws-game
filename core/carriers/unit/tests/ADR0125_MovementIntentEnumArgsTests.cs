using System;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Unit
{
    /// <summary>
    /// ADR-0125 第三批探针缺陷：<see cref="MovementTickHandler"/> 解码意图参数时用 <c>Enum.TryParse</c>
    /// 解析 <c>mode</c>（<see cref="MoveMode"/>）与 <c>blocking</c>（<see cref="DisplacementBlockingPolicy"/>），
    /// 数字串（含未定义的数字）会被当作枚举值采用。期望：只认枚举名，其余一律走"无法解析 → 回退默认值"的既有分支。
    /// </summary>
    public sealed class ADR0125_MovementIntentEnumArgsTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id HeroId = new Id("unit.hero");

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public PlayerUnit Player = null!;
        }

        private static Fixture Build(StubNavigation2D? navigation = null)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            var player = new PlayerUnit(HeroId, MapId, new Id("fac.player"), new Id("arch.class.sample")) { Position = Vec2.Zero };
            world.AddEntity(player);
            var stats = new FakeStatHost();
            stats.SetBase(HeroId, new MovementOptions().MoveSpeedStat, 10.0);
            var host = new MovementHost(world);
            var handler = new MovementTickHandler(new WorldUnitAccess(world), stats, new FakeAuraQuery(), host, bus, navigation, null);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);
            return new Fixture { World = world, Player = player };
        }

        private static MoveMode OtherThan(MoveMode mode)
        {
            foreach (MoveMode candidate in Enum.GetValues(typeof(MoveMode)))
            {
                if (candidate != mode && candidate != MoveMode.Idle && candidate != MoveMode.Forced)
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException("MoveMode 至少应有三个可用于方向移动的值");
        }

        /// <summary>方向移动意图缺省 mode = Walk。<c>mode</c> 给成"另一个枚举值的数字串"或未定义数字时，必须仍回退到 Walk
        /// （修前：数字串被采用，单位进入 mode 对应的状态或未定义状态）。</summary>
        [Fact]
        public void DirectionMove_ModeGivenAsNumber_FallsBackToDefaultWalk()
        {
            var other = OtherThan(MoveMode.Walk);
            var undefined = 0;
            foreach (MoveMode v in Enum.GetValues(typeof(MoveMode))) { undefined = Math.Max(undefined, (int)v); }
            foreach (var text in new[] { ((int)other).ToString(), (undefined + 1).ToString(), "-1" })
            {
                var f = Build();
                f.World.SubmitIntent(new Intent(HeroId, "move", new JsonObjectBuilder()
                    .Add("dx", new JsonNumber(1)).Add("dy", new JsonNumber(0))
                    .Add("mode", new JsonString(text)).Build()));

                f.World.Tick(SimStep.Continuous(0.1));

                Assert.True(MoveMode.Walk == f.Player.MovementState.Mode,
                    $"mode=\"{text}\" 应回退为默认 Walk，实际 {f.Player.MovementState.Mode}");
            }
        }

        [Fact]
        public void DirectionMove_ModeGivenAsEnumName_IsHonoured()
        {
            var other = OtherThan(MoveMode.Walk);
            var f = Build();
            f.World.SubmitIntent(new Intent(HeroId, "move", new JsonObjectBuilder()
                .Add("dx", new JsonNumber(1)).Add("dy", new JsonNumber(0))
                .Add("mode", new JsonString(other.ToString())).Build()));

            f.World.Tick(SimStep.Continuous(0.1));

            Assert.Equal(other, f.Player.MovementState.Mode);
        }

        private static JsonObject DisplaceArgs(string blocking) => new JsonObjectBuilder()
            .Add("originX", new JsonNumber(1)).Add("originY", new JsonNumber(0))
            .Add("targetX", new JsonNumber(3)).Add("targetY", new JsonNumber(0))
            .Add("speed", new JsonNumber(5.0))
            .Add("blocking", new JsonString(blocking))
            .Add("sampleStep", new JsonNumber(0))
            .Build();

        /// <summary>墙前受控位移：Stop 停在墙前（x 落在 (1,1.5)），Revert 回到起点。<c>blocking</c> 给成 Revert 的数字串或未定义数字时，
        /// 必须按"无法解析 → 默认 Stop"处理（修前：Revert 的数字串被采用，单位被送回起点）。</summary>
        [Fact]
        public void Displace_BlockingGivenAsNumber_FallsBackToDefaultStop_NotRevert()
        {
            var undefined = 0;
            foreach (DisplacementBlockingPolicy v in Enum.GetValues(typeof(DisplacementBlockingPolicy))) { undefined = Math.Max(undefined, (int)v); }
            foreach (var text in new[] { ((int)DisplacementBlockingPolicy.Revert).ToString(), (undefined + 1).ToString() })
            {
                var nav = new StubNavigation2D();
                nav.SetBlocking(MapId, new[] { new Rect(new Vec2(1.5, -1), new Vec2(2, 1)) });
                var f = Build(nav);
                f.Player.Position = new Vec2(1, 0);
                f.World.SubmitIntent(new Intent(HeroId, "move_displace", DisplaceArgs(text)));

                f.World.Tick(SimStep.Continuous(1.0));

                var x = f.Player.Position.X;
                Assert.True(x > 1.0 && x < 1.5, $"blocking=\"{text}\" 应回退为默认 Stop（停在墙前），实际 x={x}");
            }
        }

        [Fact]
        public void Displace_BlockingGivenAsEnumNameRevert_IsHonoured()
        {
            var nav = new StubNavigation2D();
            nav.SetBlocking(MapId, new[] { new Rect(new Vec2(1.5, -1), new Vec2(2, 1)) });
            var f = Build(nav);
            f.Player.Position = new Vec2(1, 0);
            f.World.SubmitIntent(new Intent(HeroId, "move_displace", DisplaceArgs(DisplacementBlockingPolicy.Revert.ToString())));

            f.World.Tick(SimStep.Continuous(1.0));

            Assert.Equal(new Vec2(1, 0), f.Player.Position);
        }
    }
}
