using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Gobj;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary>
    /// T-H11（docs/复盘/测试覆盖梳理-2026-10-01.md 第 3 节）：<see cref="GobjPendingLootPersistable.Load"/> 坏形状。
    /// <para>
    /// 本段与 Currency / VendorStock / Spawn 等段的口径<b>不同</b>，不能照搬"抛 FormatException 且原状态不动"：
    /// 类型注释明确规定 Load 对坏形状<b>安全丢弃、记诊断（<see cref="ISaveDiagnostics.Warn"/>）、不抛异常</b>，
    /// 且 Load 语义是"读档 = 归零重建"——用解析出的表整体替换既有待补发台账。因此本文件断言的是该设计口径：
    /// <list type="bullet">
    /// <item>根不是对象 / 缺 <c>pending_loot</c> / <c>pending_loot</c> 不是数组：不抛，台账被替换为空表
    /// （<b>既有台账被清空</b>，且此分支不记任何诊断——待设计层确认）；</item>
    /// <item>数组元素级坏形状（元素不是对象、缺 / 非法 originKey、仅有 1.5 旧字段 gobjInstanceId、缺 items、items 内
    /// 物品字段缺失 / 类型不符 / id 非法）：只丢弃该条、每条记一条 Warn，其余合法条目照常恢复；</item>
    /// <item>items 内物品的 <c>count&lt;=0</c>：<b>Load 抛 <see cref="ArgumentOutOfRangeException"/></b>（来自
    /// <see cref="ItemStack"/> 构造），与类型注释"绝不抛"相悖；抛出发生在替换台账之前，因此既有台账不变——
    /// 汇报中登记，待设计层确认。</item>
    /// </list>
    /// "引用不存在的 id"无校验：originKey / templateId 只校验 id 格式，不校验对应实体 / 物品模板是否存在。
    /// </para>
    /// </summary>
    public sealed class T_H11_GobjPendingLootPersistableBadShapeTests
    {
        private static readonly Id KeepA = new Id("origin.sample_h11_a");
        private static readonly Id KeepB = new Id("origin.sample_h11_b");
        private static readonly Id Ore = new Id("item.sample_h11_ore");
        private static readonly Id Gem = new Id("item.sample_h11_gem");

        private sealed class Fixture
        {
            public GobjWorld World = null!;
            public InMemorySaveDiagnostics Diagnostics = null!;
            public GobjPendingLootPersistable Persistable = null!;
        }

        private static Fixture NewFixture()
        {
            var world = new GobjWorldBuilder().Build();
            var diagnostics = new InMemorySaveDiagnostics();
            world.Host.RestorePendingLoot(new Dictionary<Id, IReadOnlyList<ItemStack>>
            {
                [KeepA] = new List<ItemStack> { new ItemStack(Ore, 4) },
                [KeepB] = new List<ItemStack> { new ItemStack(Gem, 2), new ItemStack(Ore, 1) },
            });
            return new Fixture
            {
                World = world,
                Diagnostics = diagnostics,
                Persistable = new GobjPendingLootPersistable(world.Host, diagnostics),
            };
        }

        private static string Capture(Fixture f) =>
            string.Join(";", f.World.Host.PendingLootSnapshot()
                .OrderBy(kv => kv.Key.Value, StringComparer.Ordinal)
                .Select(kv => kv.Key + "=" + string.Join(",", kv.Value.Select(s => $"{s.TemplateId}x{s.Count}"))));

        private static JsonObject Item(JsonValue? templateId, JsonValue? count)
        {
            var b = new JsonObjectBuilder();
            if (templateId != null) b.Add("templateId", templateId);
            if (count != null) b.Add("count", count);
            return b.Build();
        }

        private static JsonObject Entry(string originKey, params JsonValue[] items) =>
            new JsonObjectBuilder()
                .Add("originKey", new JsonString(originKey))
                .Add("items", new JsonArray(items))
                .Build();

        private static JsonObject Section(params JsonValue[] entries) =>
            new JsonObjectBuilder().Add("pending_loot", new JsonArray(entries)).Build();

        private static JsonValue GoodItem() => Item(new JsonString(Ore.Value), new JsonNumber(7));

        private const string GoodOrigin = "origin.sample_h11_good";
        private const string GoodCapture = "origin.sample_h11_good=item.sample_h11_orex7";

        [Fact]
        public void Fixture_PreLoadStateIsAsDesigned()
        {
            var f = NewFixture();
            Assert.Equal(2, f.World.Host.PendingLootSnapshot().Count);
            Assert.Equal(4, f.World.Host.PendingLootSnapshot()[KeepA].Single().Count);
        }

        // ---- 1. 根不是对象 / 段形状不符：不抛，台账被替换为空表 ----

        [Fact]
        public void Load_RootIsString_DoesNotThrow_ReplacesLedgerWithEmpty()
        {
            var f = NewFixture();

            var ex = Record.Exception(() => f.Persistable.Load(new JsonString("wrong-shape")));

            Assert.Null(ex);
            Assert.Empty(f.World.Host.PendingLootSnapshot());
        }

        [Fact]
        public void Load_RootIsArray_DoesNotThrow_ReplacesLedgerWithEmpty()
        {
            var f = NewFixture();

            var ex = Record.Exception(() => f.Persistable.Load(new JsonArray(Array.Empty<JsonValue>())));

            Assert.Null(ex);
            Assert.Empty(f.World.Host.PendingLootSnapshot());
        }

        [Fact]
        public void Load_ObjectWithoutPendingLootKey_DoesNotThrow_ReplacesLedgerWithEmpty()
        {
            var f = NewFixture();

            var ex = Record.Exception(() => f.Persistable.Load(new JsonObjectBuilder().Add("other", JsonBool.True).Build()));

            Assert.Null(ex);
            Assert.Empty(f.World.Host.PendingLootSnapshot());
        }

        [Fact]
        public void Load_PendingLootIsNotArray_DoesNotThrow_ReplacesLedgerWithEmpty()
        {
            var f = NewFixture();

            var ex = Record.Exception(() => f.Persistable.Load(
                new JsonObjectBuilder().Add("pending_loot", new JsonString("nope")).Build()));

            Assert.Null(ex);
            Assert.Empty(f.World.Host.PendingLootSnapshot());
        }

        // ---- 2. 元素级坏形状：丢弃该条 + 一条 Warn，合法条目保留 ----

        private static void AssertOnlyGoodEntrySurvives_WithOneWarning(Fixture f, JsonValue badEntry)
        {
            var good = Entry(GoodOrigin, GoodItem());

            var ex = Record.Exception(() => f.Persistable.Load(Section(badEntry, good)));

            Assert.Null(ex);
            Assert.Equal(GoodCapture, Capture(f));
            Assert.Single(f.Diagnostics.Warnings);
            Assert.Empty(f.Diagnostics.Errors);
        }

        [Fact]
        public void Load_ElementIsNotObject_DiscardsThatElementOnly() =>
            AssertOnlyGoodEntrySurvives_WithOneWarning(NewFixture(), new JsonNumber(3));

        [Fact]
        public void Load_EntryMissingOriginKey_DiscardsThatEntryOnly() =>
            AssertOnlyGoodEntrySurvives_WithOneWarning(NewFixture(),
                new JsonObjectBuilder().Add("items", new JsonArray(new[] { GoodItem() })).Build());

        [Fact]
        public void Load_EntryOriginKeyWrongType_DiscardsThatEntryOnly() =>
            AssertOnlyGoodEntrySurvives_WithOneWarning(NewFixture(),
                new JsonObjectBuilder().Add("originKey", new JsonNumber(1)).Add("items", new JsonArray(new[] { GoodItem() })).Build());

        [Fact]
        public void Load_EntryOriginKeyNotALegalId_DiscardsThatEntryOnly() =>
            AssertOnlyGoodEntrySurvives_WithOneWarning(NewFixture(), Entry("Not A Legal Id", GoodItem()));

        [Fact]
        public void Load_LegacyGobjInstanceIdEntry_DiscardedWithWarning_NotMigrated()
        {
            var f = NewFixture();
            var legacy = new JsonObjectBuilder()
                .Add("gobjInstanceId", new JsonString("gobj.inst_12"))
                .Add("items", new JsonArray(new[] { GoodItem() }))
                .Build();

            AssertOnlyGoodEntrySurvives_WithOneWarning(f, legacy);

            Assert.Contains("gobjInstanceId", f.Diagnostics.Warnings[0]);
        }

        [Fact]
        public void Load_EntryMissingItems_DiscardsThatEntryOnly() =>
            AssertOnlyGoodEntrySurvives_WithOneWarning(NewFixture(),
                new JsonObjectBuilder().Add("originKey", new JsonString("origin.sample_h11_bad")).Build());

        [Fact]
        public void Load_EntryItemsWrongType_DiscardsThatEntryOnly() =>
            AssertOnlyGoodEntrySurvives_WithOneWarning(NewFixture(),
                new JsonObjectBuilder().Add("originKey", new JsonString("origin.sample_h11_bad")).Add("items", new JsonString("x")).Build());

        [Fact]
        public void Load_ItemNotObject_DiscardsWholeEntry_EvenIfOtherItemsAreValid() =>
            AssertOnlyGoodEntrySurvives_WithOneWarning(NewFixture(),
                Entry("origin.sample_h11_bad", GoodItem(), new JsonNumber(1)));

        [Fact]
        public void Load_ItemMissingTemplateId_DiscardsWholeEntry() =>
            AssertOnlyGoodEntrySurvives_WithOneWarning(NewFixture(),
                Entry("origin.sample_h11_bad", Item(null, new JsonNumber(1))));

        [Fact]
        public void Load_ItemTemplateIdNotALegalId_DiscardsWholeEntry() =>
            AssertOnlyGoodEntrySurvives_WithOneWarning(NewFixture(),
                Entry("origin.sample_h11_bad", Item(new JsonString("Bad"), new JsonNumber(1))));

        [Fact]
        public void Load_ItemMissingCount_DiscardsWholeEntry() =>
            AssertOnlyGoodEntrySurvives_WithOneWarning(NewFixture(),
                Entry("origin.sample_h11_bad", Item(new JsonString(Ore.Value), null)));

        [Fact]
        public void Load_ItemCountWrongType_DiscardsWholeEntry() =>
            AssertOnlyGoodEntrySurvives_WithOneWarning(NewFixture(),
                Entry("origin.sample_h11_bad", Item(new JsonString(Ore.Value), new JsonString("3"))));

        [Fact]
        public void Load_EntryWithEmptyItemsArray_IsDroppedSilently_WithoutWarning()
        {
            // 合法形状但没有任何物品：不进表（items.Count>0 才保留），此路径不记诊断。
            var f = NewFixture();

            var ex = Record.Exception(() => f.Persistable.Load(
                Section(Entry("origin.sample_h11_empty"), Entry(GoodOrigin, GoodItem()))));

            Assert.Null(ex);
            Assert.Equal(GoodCapture, Capture(f));
            Assert.Empty(f.Diagnostics.Warnings);
        }

        [Fact]
        public void Load_DuplicateOriginKey_LaterEntryWins()
        {
            var f = NewFixture();

            f.Persistable.Load(Section(
                Entry(GoodOrigin, Item(new JsonString(Gem.Value), new JsonNumber(1))),
                Entry(GoodOrigin, GoodItem())));

            Assert.Equal(GoodCapture, Capture(f));
        }

        // ---- 3. 引用不存在的 id：无校验，照常恢复 ----

        [Fact]
        public void Load_UnknownOriginKeyAndItemTemplate_AreNotValidated_RestoredAsIs()
        {
            var f = NewFixture();

            f.Persistable.Load(Section(Entry("origin.sample_h11_never_spawned", Item(new JsonString("item.sample_h11_not_in_any_table"), new JsonNumber(2)))));

            Assert.Equal("origin.sample_h11_never_spawned=item.sample_h11_not_in_any_tablex2", Capture(f));
            Assert.Empty(f.Diagnostics.Warnings);
        }

        // ---- 4. count<=0：Load 抛 ArgumentOutOfRangeException，既有台账不变（与类型注释"绝不抛"相悖，待设计层确认） ----

        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        public void Load_ItemCountNotPositive_ThrowsArgumentOutOfRange_AndKeepsExistingLedger(int count)
        {
            var f = NewFixture();
            var before = Capture(f);

            var ex = Record.Exception(() => f.Persistable.Load(Section(
                Entry(GoodOrigin, GoodItem()),
                Entry("origin.sample_h11_bad", Item(new JsonString(Ore.Value), new JsonNumber(count))))));

            Assert.IsType<ArgumentOutOfRangeException>(ex);
            Assert.Equal(before, Capture(f));
        }
    }
}
