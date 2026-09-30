using System;
using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// ADR-0125 第三批探针缺陷：<see cref="DroppedLootPersistable.Load"/> 先 <c>ClearDroppedExcept</c>（销毁既有掉落）、
    /// 后 <c>RestoreDropped</c>；快照里某元素 <c>entityId</c> 与世界里非掉落物实体重名时，<c>RestoreDropped</c> 内
    /// <c>AddEntity</c> 抛 <see cref="InvalidOperationException"/>，此时既有掉落已被销毁——违反"校验先于提交，
    /// 失败不动原状态"。不变量：Load 抛异常后，<see cref="LootHost.ActiveLootIds"/>、世界实体数、每个既有掉落物的
    /// 存在性与 <c>Save()</c> 文本与 Load 前逐项相等；快照里排在冲突元素之前的合法元素也不得被半提交。
    /// </summary>
    public sealed class ADR0125_DroppedLootPersistableAtomicLoadTests
    {
        private static readonly Id MapId = new Id("map.a");
        private static readonly Id Ore = new Id("item.sample_ore");
        private static readonly Id PlayerId = new Id("player.sample_1");

        private sealed class Fixture
        {
            public LootHost Host = null!;
            public IWorldSim World = null!;
            public DroppedLootPersistable Persistable = null!;
            public Id Existing;
        }

        private static Fixture NewFixture()
        {
            var bus = LootTestSupport.NewEventBus();
            var registry = LootTestSupport.MakeRegistry(bus, "[]");
            var world = LootTestSupport.NewWorld(bus);
            var host = new LootHost(registry, new RngHost(1), bus, world, new Core.Carriers.Unit.WorldUnitAccess(world),
                new FakeInventoryHost(), new FakeExprHostFactory(), () => 0.0);
            LootTestSupport.AddPlayer(world, PlayerId, MapId, new Vec2(0, 0));
            var existing = host.Drop(MapId, new Vec2(1, 1), new[] { new ItemStack(Ore, 2) });
            return new Fixture { Host = host, World = world, Persistable = new DroppedLootPersistable(host), Existing = existing };
        }

        private static string Capture(Fixture f)
        {
            var ids = string.Join(",", f.Host.ActiveLootIds.Select(i => i.Value));
            return ids + "##" + f.World.EntityCount + "##" + (f.World.GetEntity(f.Existing) != null) + "##" +
                   JsonWriter.Write(f.Persistable.Save());
        }

        private static JsonValue Element(string entityId, int count = 1) =>
            new JsonObjectBuilder()
                .Add("entityId", new JsonString(entityId))
                .Add("mapId", new JsonString(MapId.Value))
                .Add("position", new JsonObjectBuilder().Add("x", new JsonNumber(1)).Add("y", new JsonNumber(2)).Build())
                .Add("items", new JsonArray(new JsonValue[]
                {
                    new JsonObjectBuilder().Add("templateId", new JsonString(Ore.Value)).Add("count", new JsonNumber(count)).Build(),
                }))
                .Build();

        /// <summary>复现：元素 entityId 是世界里已有的玩家实体 id。修前：既有掉落先被销毁再抛；修后：抛，且一切不变。</summary>
        [Fact]
        public void Load_EntityIdCollidingWithNonLootEntity_Throws_AndLeavesExistingDropsUntouched()
        {
            var f = NewFixture();
            var before = Capture(f);

            Assert.ThrowsAny<Exception>(() => f.Persistable.Load(new JsonArray(new[] { Element(PlayerId.Value) })));

            Assert.True(f.Host.TryGetDropped(f.Existing, out _));
            Assert.Equal(before, Capture(f));
        }

        /// <summary>冲突元素排在后面时，排在它前面的合法新掉落也不能被半提交。</summary>
        [Fact]
        public void Load_CollisionAfterValidElement_RestoresNothing()
        {
            var f = NewFixture();
            var before = Capture(f);
            const string fresh = "loot.inst_9000";

            Assert.ThrowsAny<Exception>(() => f.Persistable.Load(
                new JsonArray(new[] { Element(fresh), Element(PlayerId.Value) })));

            Assert.False(f.Host.TryGetDropped(new Id(fresh), out _));
            Assert.Equal(before, Capture(f));
        }

        /// <summary>阳性对照：无冲突的快照照常"归零重建"——不在快照里的既有掉落被清掉，快照里的被恢复。</summary>
        [Fact]
        public void Load_ValidSnapshot_ReplacesExistingDrops()
        {
            var f = NewFixture();
            var fresh = new Id("loot.inst_9000");

            f.Persistable.Load(new JsonArray(new[] { Element(fresh.Value, count: 3) }));

            Assert.Equal(new[] { fresh }, f.Host.ActiveLootIds.ToArray());
            Assert.True(f.Host.TryGetDropped(fresh, out var restored));
            Assert.Equal(3, restored.Items.Single().Count);
        }

        /// <summary>阳性对照：快照里的 id 与既有掉落物（同类实体）同名是合法的"原地覆写"，不属于冲突。</summary>
        [Fact]
        public void Load_SnapshotIdEqualToExistingDrop_IsOverwriteNotCollision()
        {
            var f = NewFixture();

            f.Persistable.Load(new JsonArray(new[] { Element(f.Existing.Value, count: 5) }));

            Assert.Equal(new[] { f.Existing }, f.Host.ActiveLootIds.ToArray());
            Assert.True(f.Host.TryGetDropped(f.Existing, out var entity));
            Assert.Equal(5, entity.Items.Single().Count);
        }
    }
}
