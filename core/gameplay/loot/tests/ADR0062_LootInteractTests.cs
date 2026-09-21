using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// ADR-0062（消费方反馈第五批第 1 条）：<see cref="LootHost.Interact"/>（<c>interact</c> 意图
    /// 原生分流）冒烟——距离内成功拾取、距离外/不存在/权限拒绝三种失败原因、以及 <see
    /// cref="LootHost.Diagnostics"/> 是否按判断记录记诊断。夹具惯例照抄 <c>LootDropPickupTests</c>。
    /// </summary>
    public class ADR0062_LootInteractTests
    {
        private const string EmptyTable = "[]";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public IWorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public FakeInventoryHost Inventory = null!;
            public LootHost Host = null!;
            public InMemoryLootDiagnostics Diagnostics = null!;
        }

        private static Fixture NewFixture(LootOptions? options = null)
        {
            var fixture = new Fixture();
            fixture.Bus = LootTestSupport.NewEventBus();
            var registry = LootTestSupport.MakeRegistry(fixture.Bus, EmptyTable);
            fixture.World = LootTestSupport.NewWorld(fixture.Bus);
            fixture.Units = new WorldUnitAccess(fixture.World);
            fixture.Inventory = new FakeInventoryHost();
            fixture.Diagnostics = new InMemoryLootDiagnostics();

            fixture.Host = new LootHost(
                registry, new RngHost(1), fixture.Bus, fixture.World, fixture.Units, fixture.Inventory,
                new FakeExprHostFactory(), () => 0.0, options,
                diagnostics: null, conditionSchema: null, economyHost: null, goldMultiplierProvider: null,
                lootDiagnostics: fixture.Diagnostics);

            return fixture;
        }

        private static readonly Id MapId = new Id("map.sample_1");

        [Fact]
        public void Interact_WithinRange_PicksUpAndRemovesFromActiveLootIds()
        {
            var f = NewFixture();
            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, MapId, new Vec2(0, 0));
            var lootId = f.Host.Drop(MapId, new Vec2(1, 0), new[] { new ItemStack(new Id("item.sample_ore"), 3) });

            var result = f.Host.Interact(unitId, lootId);

            Assert.True(result.Success);
            Assert.Equal(3, f.Inventory.CountOf(unitId, new Id("item.sample_ore")));
            Assert.DoesNotContain(lootId, f.Host.ActiveLootIds);
        }

        [Fact]
        public void Interact_OutOfRange_ReturnsTooFar_LootRemains_NoDiagnostic()
        {
            var f = NewFixture(new LootOptions { PickupRange = 1.0 });
            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, MapId, new Vec2(0, 0));
            var lootId = f.Host.Drop(MapId, new Vec2(10, 0), new[] { new ItemStack(new Id("item.sample_ore"), 1) });

            var result = f.Host.Interact(unitId, lootId);

            Assert.False(result.Success);
            Assert.Equal(LootPickupFailureReason.TooFar, result.Reason);
            Assert.Contains(lootId, f.Host.ActiveLootIds);
            // 判断记录（同 CreatureInteractionHost.Interact 对"距离过远"的既有处理）：正常游玩中随时
            // 会发生的瞬时状态，不记诊断，逐帧重复交互尝试不应该刷诊断。
            Assert.Empty(f.Diagnostics.Warnings);
        }

        [Fact]
        public void Interact_UnknownLootInstanceId_ReturnsNotFound_WithDiagnostic()
        {
            var f = NewFixture();
            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, MapId, new Vec2(0, 0));

            var result = f.Host.Interact(unitId, new Id("loot.inst_not_exist"));

            Assert.False(result.Success);
            Assert.Equal(LootPickupFailureReason.NotFound, result.Reason);
            Assert.Single(f.Diagnostics.Warnings);
        }

        [Fact]
        public void Interact_PermissionCheckerDenies_ReturnsPermissionDenied_LootRemains_WithDiagnostic()
        {
            var f = NewFixture(new LootOptions
            {
                PickupPermissionChecker = (unitId, lootInstanceId, ownerHint) => false,
            });
            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, MapId, new Vec2(0, 0));
            var lootId = f.Host.Drop(MapId, new Vec2(1, 0), new[] { new ItemStack(new Id("item.sample_ore"), 1) });

            var result = f.Host.Interact(unitId, lootId);

            Assert.False(result.Success);
            Assert.Equal(LootPickupFailureReason.PermissionDenied, result.Reason);
            Assert.Contains(lootId, f.Host.ActiveLootIds);
            Assert.Equal(0, f.Inventory.CountOf(unitId, new Id("item.sample_ore")));
            Assert.Single(f.Diagnostics.Warnings);
        }

        [Fact]
        public void Interact_NoPermissionCheckerConfigured_DefaultsToAllow()
        {
            var f = NewFixture();
            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, MapId, new Vec2(0, 0));
            var lootId = f.Host.Drop(MapId, new Vec2(1, 0), new[] { new ItemStack(new Id("item.sample_ore"), 1) });

            var result = f.Host.Interact(unitId, lootId);

            Assert.True(result.Success);
        }

        [Fact]
        public void Interact_DirectPickUp_DoesNotConsultPermissionChecker()
        {
            // 判断记录（LootOptions.PickupPermissionChecker 类型注释）：权限接缝只在 interact 意图
            // 分流（Interact）里生效，直接调用既有 PickUp 签名不受影响——ABI/行为不变。
            var f = NewFixture(new LootOptions
            {
                PickupPermissionChecker = (unitId, lootInstanceId, ownerHint) => false,
            });
            var unitId = new Id("player.sample_1");
            LootTestSupport.AddPlayer(f.World, unitId, MapId, new Vec2(0, 0));
            var lootId = f.Host.Drop(MapId, new Vec2(1, 0), new[] { new ItemStack(new Id("item.sample_ore"), 1) });

            var result = f.Host.PickUp(unitId, lootId);

            Assert.True(result.Success);
        }
    }
}
