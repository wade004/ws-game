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
    /// T-H11（docs/复盘/测试覆盖梳理-2026-10-01.md 第 3 节）：<see cref="DroppedLootPersistable.Load"/> 坏形状。
    /// 写法参照 <c>CORE_170_03_CurrencyAndVendorStockPersistableLoadFailureTests</c>：读档前已有两个地面掉落物
    /// （一个带 ownerHint / expireAt），用坏数据 Load，断言抛异常，且 <see cref="LootHost.ActiveLootIds"/>、
    /// 每个掉落物在世界里的存在性、<see cref="DroppedLootPersistable.Save"/> 文本与 Load 前逐项相等
    /// （Load 先把整份数据解析进临时列表、之后才 <c>ClearDroppedExcept</c>，坏形状不会触碰既有掉落）。
    /// <para>
    /// 异常类型口径（测试只钉"抛 + 状态不变"，不钉具体类型，不改口径）：根不是数组 / 元素不是对象 →
    /// <see cref="FormatException"/>；元素内部字段级坏形状则是未经包装的底层异常——必填键缺失 →
    /// <see cref="KeyNotFoundException"/>，类型不符 → <see cref="InvalidCastException"/>，id 格式非法 →
    /// <see cref="ArgumentException"/>，<c>count&lt;=0</c> → <see cref="ArgumentOutOfRangeException"/>。
    /// 与同仓库其它存档段统一抛 <see cref="FormatException"/> 的口径不一致，但 <c>SaveSystem.Load</c> 对任意
    /// 异常一视同仁，运行期无差别（汇报中登记，待设计层确认）。
    /// </para>
    /// </summary>
    public sealed class T_H11_DroppedLootPersistableBadShapeTests
    {
        private static readonly Id MapId = new Id("map.sample_h11");
        private static readonly Id Ore = new Id("item.sample_ore");

        private const string EmptyLootTables = "[]";

        private sealed class Fixture
        {
            public LootHost Host = null!;
            public IWorldSim World = null!;
            public DroppedLootPersistable Persistable = null!;
            public Id DropWithOwner;
            public Id DropPlain;
        }

        private static Fixture NewFixture()
        {
            var bus = LootTestSupport.NewEventBus();
            var registry = LootTestSupport.MakeRegistry(bus, EmptyLootTables);
            var world = LootTestSupport.NewWorld(bus);
            var host = new LootHost(
                registry, new RngHost(1), bus, world, new Core.Carriers.Unit.WorldUnitAccess(world),
                new FakeInventoryHost(), new FakeExprHostFactory(), () => 0.0);

            var owned = host.Drop(MapId, new Vec2(1, 2), new[] { new ItemStack(Ore, 3) }, ownerHint: new Id("unit.sample_owner"));
            var plain = host.Drop(MapId, new Vec2(5, 6), new[] { new ItemStack(Ore, 1), new ItemStack(new Id("item.sample_gem"), 2) });
            return new Fixture
            {
                Host = host,
                World = world,
                Persistable = new DroppedLootPersistable(host),
                DropWithOwner = owned,
                DropPlain = plain,
            };
        }

        private static string Capture(Fixture f)
        {
            var ids = string.Join(",", f.Host.ActiveLootIds.Select(i => i.Value).OrderBy(v => v, StringComparer.Ordinal));
            var inWorld = string.Join(",", new[] { f.DropWithOwner, f.DropPlain }.Select(i => $"{i}={f.World.GetEntity(i) != null}"));
            return ids + "##" + inWorld + "##" + JsonWriter.Write(f.Persistable.Save());
        }

        private static Exception AssertRejectedAndUnchanged(Fixture f, JsonValue bad)
        {
            var before = Capture(f);

            var ex = Record.Exception(() => f.Persistable.Load(bad));

            Assert.NotNull(ex);
            Assert.Equal(before, Capture(f));
            return ex!;
        }

        // ---- 构造合法 / 非法元素的小工具 ----

        private static JsonObject Item(JsonValue? templateId, JsonValue? count)
        {
            var b = new JsonObjectBuilder();
            if (templateId != null) b.Add("templateId", templateId);
            if (count != null) b.Add("count", count);
            return b.Build();
        }

        private static JsonObject Pos(JsonValue? x, JsonValue? y)
        {
            var b = new JsonObjectBuilder();
            if (x != null) b.Add("x", x);
            if (y != null) b.Add("y", y);
            return b.Build();
        }

        private static JsonObject Element(JsonValue? entityId, JsonValue? mapId, JsonValue? position, JsonValue? items)
        {
            var b = new JsonObjectBuilder();
            if (entityId != null) b.Add("entityId", entityId);
            if (mapId != null) b.Add("mapId", mapId);
            if (position != null) b.Add("position", position);
            if (items != null) b.Add("items", items);
            return b.Build();
        }

        private static JsonObject ValidElement(string entityId = "loot.inst_900") => Element(
            new JsonString(entityId), new JsonString(MapId.Value), Pos(new JsonNumber(1), new JsonNumber(2)),
            new JsonArray(new JsonValue[] { Item(new JsonString(Ore.Value), new JsonNumber(1)) }));

        private static JsonArray Wrap(params JsonValue[] elements) => new JsonArray(elements);

        [Fact]
        public void Fixture_PreLoadStateIsAsDesigned()
        {
            var f = NewFixture();
            Assert.Equal(2, f.Host.ActiveLootIds.Count);
            Assert.NotNull(f.World.GetEntity(f.DropWithOwner));
            Assert.NotNull(f.World.GetEntity(f.DropPlain));
        }

        // ---- 1. 根不是数组 ----

        [Fact]
        public void Load_RootIsObject_ThrowsFormatException_AndKeepsState()
        {
            var ex = AssertRejectedAndUnchanged(NewFixture(), new JsonObjectBuilder().Build());
            Assert.IsType<FormatException>(ex);
        }

        [Fact]
        public void Load_RootIsString_ThrowsFormatException_AndKeepsState()
        {
            var ex = AssertRejectedAndUnchanged(NewFixture(), new JsonString("wrong-shape"));
            Assert.IsType<FormatException>(ex);
        }

        [Fact]
        public void Load_RootIsNumber_ThrowsFormatException_AndKeepsState()
        {
            var ex = AssertRejectedAndUnchanged(NewFixture(), new JsonNumber(1));
            Assert.IsType<FormatException>(ex);
        }

        [Fact]
        public void Load_RootIsBool_ThrowsFormatException_AndKeepsState()
        {
            var ex = AssertRejectedAndUnchanged(NewFixture(), JsonBool.True);
            Assert.IsType<FormatException>(ex);
        }

        // ---- 2. 元素不是对象 ----

        [Fact]
        public void Load_ElementIsNotObject_ThrowsFormatException_AndKeepsState()
        {
            var ex = AssertRejectedAndUnchanged(NewFixture(), Wrap(new JsonNumber(3)));
            Assert.IsType<FormatException>(ex);
        }

        // ---- 3. 必填字段缺失（元素四个必填键 + position 子键 + item 子键） ----

        [Fact]
        public void Load_ElementMissingEntityId_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(null, new JsonString(MapId.Value),
                Pos(new JsonNumber(1), new JsonNumber(2)), Wrap())));

        [Fact]
        public void Load_ElementMissingMapId_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonString("loot.inst_900"), null,
                Pos(new JsonNumber(1), new JsonNumber(2)), Wrap())));

        [Fact]
        public void Load_ElementMissingPosition_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonString("loot.inst_900"),
                new JsonString(MapId.Value), null, Wrap())));

        [Fact]
        public void Load_ElementMissingItems_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonString("loot.inst_900"),
                new JsonString(MapId.Value), Pos(new JsonNumber(1), new JsonNumber(2)), null)));

        [Fact]
        public void Load_PositionMissingX_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonString("loot.inst_900"),
                new JsonString(MapId.Value), Pos(null, new JsonNumber(2)), Wrap())));

        [Fact]
        public void Load_ItemMissingTemplateId_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonString("loot.inst_900"),
                new JsonString(MapId.Value), Pos(new JsonNumber(1), new JsonNumber(2)),
                Wrap(Item(null, new JsonNumber(1))))));

        [Fact]
        public void Load_ItemMissingCount_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonString("loot.inst_900"),
                new JsonString(MapId.Value), Pos(new JsonNumber(1), new JsonNumber(2)),
                Wrap(Item(new JsonString(Ore.Value), null)))));

        // ---- 4. 字段类型不符 ----

        [Fact]
        public void Load_EntityIdIsNumber_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonNumber(1), new JsonString(MapId.Value),
                Pos(new JsonNumber(1), new JsonNumber(2)), Wrap())));

        [Fact]
        public void Load_PositionIsString_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonString("loot.inst_900"),
                new JsonString(MapId.Value), new JsonString("1,2"), Wrap())));

        [Fact]
        public void Load_PositionXIsString_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonString("loot.inst_900"),
                new JsonString(MapId.Value), Pos(new JsonString("1"), new JsonNumber(2)), Wrap())));

        [Fact]
        public void Load_ItemsIsObject_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonString("loot.inst_900"),
                new JsonString(MapId.Value), Pos(new JsonNumber(1), new JsonNumber(2)),
                new JsonObjectBuilder().Build())));

        [Fact]
        public void Load_ItemIsNotObject_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonString("loot.inst_900"),
                new JsonString(MapId.Value), Pos(new JsonNumber(1), new JsonNumber(2)), Wrap(new JsonNumber(1)))));

        [Fact]
        public void Load_ItemCountIsString_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonString("loot.inst_900"),
                new JsonString(MapId.Value), Pos(new JsonNumber(1), new JsonNumber(2)),
                Wrap(Item(new JsonString(Ore.Value), new JsonString("1"))))));

        // ---- 5. id 格式非法 / 数量非法（引用校验：模块不校验 templateId 是否存在于物品表，只校验 id 格式与正数） ----

        [Fact]
        public void Load_EntityIdNotALegalId_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(ValidElement("Not A Legal Id")));

        [Fact]
        public void Load_ItemTemplateIdNotALegalId_Throws_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonString("loot.inst_900"),
                new JsonString(MapId.Value), Pos(new JsonNumber(1), new JsonNumber(2)),
                Wrap(Item(new JsonString("Bad"), new JsonNumber(1))))));

        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        public void Load_ItemCountNotPositive_Throws_AndKeepsState(int count) =>
            AssertRejectedAndUnchanged(NewFixture(), Wrap(Element(new JsonString("loot.inst_900"),
                new JsonString(MapId.Value), Pos(new JsonNumber(1), new JsonNumber(2)),
                Wrap(Item(new JsonString(Ore.Value), new JsonNumber(count))))));

        // ---- 6. 原子性：合法元素在前、坏元素在后，既有掉落不得被提前清掉 ----

        [Fact]
        public void Load_LaterElementBad_DoesNotClearExistingDrops_NorPartiallyRestoreEarlierElement()
        {
            var f = NewFixture();
            var bad = Wrap(ValidElement("loot.inst_901"), new JsonNumber(3));

            AssertRejectedAndUnchanged(f, bad);

            Assert.Equal(2, f.Host.ActiveLootIds.Count);
            Assert.NotNull(f.World.GetEntity(f.DropWithOwner));
            Assert.NotNull(f.World.GetEntity(f.DropPlain));
            Assert.Null(f.World.GetEntity(new Id("loot.inst_901")));
        }

        [Fact]
        public void Load_AfterRejectedBadData_ValidSnapshotStillLoadsNormally()
        {
            var f = NewFixture();
            var good = f.Persistable.Save();
            Assert.ThrowsAny<Exception>(() => f.Persistable.Load(new JsonString("bad")));

            f.Persistable.Load(good);

            Assert.Equal(2, f.Host.ActiveLootIds.Count);
            Assert.True(f.Host.TryGetDropped(f.DropWithOwner, out var owned));
            Assert.Equal(new Id("unit.sample_owner"), owned.OwnerHint);
        }
    }
}
