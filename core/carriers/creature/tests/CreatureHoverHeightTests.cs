using System;
using Core.Carriers.Creature;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Creature
{
    /// <summary>
    /// 悬浮生物出生高度（ADR-0172，<c>creature.template.hover_height</c>）：飞行怪、漂浮灯笼在竖直轴世界里需要"出生即悬空且不被地面吸下来"。
    /// 期望值取自模板声明本身（悬浮高度、平台与地形的数据），不写死裸数。
    /// </summary>
    public class CreatureHoverHeightTests
    {
        private static readonly Id MapId = new Id("map.hover_test");
        private static readonly Id FlyerTemplate = new Id("creature.hover_flyer");
        private static readonly Id WalkerTemplate = new Id("creature.hover_walker");
        private const double Hover = 2.4;
        private const double Dt = 1.0 / 60.0;

        private static string Rows(string hover) =>
            "[" +
            "{\"id\": \"creature.hover_flyer\", \"name_key\": \"l10n.creature.hover_flyer.name\", \"level\": 1, \"tier\": \"creature.tier.normal\", " +
            "\"base_stats\": {\"stat.power\": 10, \"stat.max_health\": 100}, \"faction_id\": \"fac.test_monster\", \"display_ref\": \"display.hover_flyer\"" + hover + "}," +
            "{\"id\": \"creature.hover_walker\", \"name_key\": \"l10n.creature.hover_walker.name\", \"level\": 1, \"tier\": \"creature.tier.normal\", " +
            "\"base_stats\": {\"stat.power\": 10, \"stat.max_health\": 100}, \"faction_id\": \"fac.test_monster\", \"display_ref\": \"display.hover_walker\"}" +
            "]";

        private sealed class Fx
        {
            public WorldSim World = null!;
            public CreatureFactory Factory = null!;
            public VerticalMotionHost Vertical = null!;
        }

        private static Fx Build(string hover)
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus, Rows(hover));
            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world);
            var stats = CreatureTestSupport.MakeStatHost(registry, bus);
            var powers = CreatureTestSupport.MakePowerHost(registry, bus, stats);
            var progression = CreatureTestSupport.MakeProgressionHost(registry, bus, stats);
            var factory = new CreatureFactory(registry, world, bus, stats, powers, progression, units, (unitId, profileId, spawnPoint, rotationId) => { });
            var terrain = new MapTerrainHeights();
            terrain.SetRegions(MapId, new[] { new TerrainRegion(new Vec2(-50, -5), new Vec2(50, 5), ground: 0.0) });
            var vertical = new VerticalMotionHost(world, new VerticalAxisOptions { Terrain = terrain, StepHeight = 0.5 }, bus);
            return new Fx { World = world, Factory = factory, Vertical = vertical };
        }

        private static CreatureUnit Spawn(Fx fx, Id template, Vec2 at) =>
            (CreatureUnit)fx.World.GetEntity(fx.Factory.Spawn(template, MapId, at, 0.0))!;

        [Fact]
        public void Gap_HeightSetAfterSpawn_IsPulledBackToTheGround_WhenTheUnitWasAlreadyObservedOnTheGround()
        {
            // 复现：没有数据声明时，游戏只能在出生后自己写 HeightOffset。竖直运动服务已经把单位观察成"贴地"，
            // 之后位置一变化，它就被地面吸回 0——游戏层无法稳定地做出飞行怪。
            var fx = Build(string.Empty);
            var walker = Spawn(fx, WalkerTemplate, new Vec2(0, 0));
            fx.Vertical.Advance(Dt);
            walker.HeightOffset = Hover;
            walker.Position = new Vec2(1, 0);
            fx.Vertical.Advance(Dt);
            Assert.Equal(0.0, walker.HeightOffset, 12);
        }

        [Fact]
        public void Spawn_WithHoverHeight_StartsAtTheDeclaredHeight_AndKeepsItWhileMoving()
        {
            var fx = Build(", \"hover_height\": 2.4");
            var flyer = Spawn(fx, FlyerTemplate, new Vec2(0, 0));
            Assert.Equal(Hover, flyer.HeightOffset, 12);
            for (var i = 0; i < 90; i++)
            {
                flyer.Position = new Vec2(i * 0.05, 0);
                fx.Vertical.Advance(Dt);
            }

            Assert.Equal(Hover, flyer.HeightOffset, 12);
            Assert.False(fx.Vertical.IsAirborne(flyer.EntityId));
        }

        [Fact]
        public void Spawn_WithoutHoverHeight_StaysOnTheGround()
        {
            var fx = Build(", \"hover_height\": 2.4");
            var walker = Spawn(fx, WalkerTemplate, new Vec2(0, 0));
            fx.Vertical.Advance(Dt);
            Assert.Equal(0.0, walker.HeightOffset, 12);
        }

        [Fact]
        public void Template_ParsesHoverHeight_AndRejectsNegativeValues()
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus, Rows(", \"hover_height\": 2.4"));
            Assert.Equal(Hover, CreatureTemplate.FromRecord(registry.Get("creature.template", FlyerTemplate)!).HoverHeight, 12);
            Assert.Equal(0.0, CreatureTemplate.FromRecord(registry.Get("creature.template", WalkerTemplate)!).HoverHeight, 12);

            var negative = Record.Exception(() =>
            {
                var registry2 = CreatureTestSupport.MakeRegistry(bus, Rows(", \"hover_height\": -1"));
                CreatureTemplate.FromRecord(registry2.Get("creature.template", FlyerTemplate)!);
            });
            Assert.NotNull(negative);
        }
    }
}
