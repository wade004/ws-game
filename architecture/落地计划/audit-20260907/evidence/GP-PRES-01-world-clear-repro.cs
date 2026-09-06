// 有界复现附件：只复用真实 Core.Foundation.WorldSim 与 Core.Gameplay.Loot.DroppedLootEntity，
// 对应 ShellHost.LoadGame -> SaveSystem.Load 恢复世界段后，SceneRouter.FinishLoading 的 World.ClearAll。
// 不调用 Unity，不修改生产代码；完整 Shell 装配仍需 Unity 集成测试验证。
using System;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Gameplay.Loot;

var bus = new EventBus(
    EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
    new EventBusOptions { StrictCatalog = false });
var world = new WorldSim(bus);
var player = new PlayerUnit(
    new Id("unit.player"), new Id("map.sample"),
    new Id("faction.player"), new Id("archetype.player"));
var restoredLoot = new DroppedLootEntity(
    new Id("loot.dropped_1"), new Id("map.sample"),
    new[] { new ItemStack(new Id("item.token"), 1) }, ownerHint: null, expireAt: null)
{
    Position = new Vec2(3, 4),
    TemplateId = DroppedLootEntity.GenericDisplayTemplateId,
};

// RestoreDropped 已完成：恢复实体已经在旧 WorldSim 中。
world.AddEntity(player);
world.AddEntity(restoredLoot);
if (world.GetEntity(restoredLoot.EntityId) == null)
    throw new Exception("restore setup failed");

// SceneRouter.FinishLoading 的真实清理调用。
world.ClearAll();
bus.DispatchPending();
var survived = world.GetEntity(restoredLoot.EntityId) != null;
Console.WriteLine($"restored_before_clear=true; loot_after_scene_clear={survived}");
if (survived)
    throw new Exception("expected restored loot to be removed by ClearAll");
