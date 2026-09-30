using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;
using Core.Gameplay.Achievement;
using Xunit;

namespace Tests.Gameplay.Achievement
{
    /// <summary>
    /// T-H11（docs/复盘/测试覆盖梳理-2026-10-01.md 第 3 节）：<see cref="AchievementHost.Load"/> 坏形状。
    /// 写法参照 <c>CORE_170_03_CurrencyAndVendorStockPersistableLoadFailureTests</c>：先造出一份含"进度中 /
    /// 待领奖 / 已解锁"三种状态的读档前现场，用坏数据 Load，断言抛 <see cref="FormatException"/>，且逐成就的
    /// 解锁位、各 criterion 计数与 <see cref="AchievementHost.Save"/> 文本与 Load 前逐项相等。
    /// <para>
    /// 口径说明（测试只钉现状，不改口径）：
    /// <list type="bullet">
    /// <item>必须拒绝：根不是对象；已知成就 id 的条目不是对象。</item>
    /// <item>引用不存在的成就 id：<b>被忽略、不阻断读档</b>（内容变更兼容，见 Load 注释"存档引用了当前内容集里
    /// 已不存在的成就 id"），且忽略发生在形状校验之前——未知 id 对应的条目即使形状非法也不抛。</item>
    /// <item>条目内部字段（<c>unlocked</c> / <c>pending_reward</c> / <c>current</c>）类型不符：Load 不抛，按
    /// "缺省值"解释（false / false / 全 0）。这是现状（宽松读取），带 Characterization 前缀的用例只是把它钉住、
    /// 以便口径变化时显式可见；是否应改成严格抛 FormatException 待设计层确认。</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class T_H11_AchievementHostLoadBadShapeTests
    {
        private static readonly Id Player = new Id("unit.sample_player");
        private static readonly Id Kill = new Id("achv.sample_kill");
        private static readonly Id Collect = new Id("achv.sample_collect");
        private static readonly Id Once = new Id("achv.sample_once");

        private const int KillCount = 3;
        private const int CollectCount = 5;

        private const string DefRows = "[" +
            "{\"id\": \"achv.sample_kill\", \"name_key\": \"l10n.achv.sample_kill.name\", " +
            "\"criteria\": [{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", " +
            "\"target_ref\": \"creature.sample_monster\", \"count\": 3}]}," +
            "{\"id\": \"achv.sample_collect\", \"name_key\": \"l10n.achv.sample_collect.name\", " +
            "\"criteria\": [{\"type\": \"collect_count\", \"observe_event\": \"item.added\", " +
            "\"target_ref\": \"item.sample_herb\", \"count\": 5}]}," +
            "{\"id\": \"achv.sample_once\", \"name_key\": \"l10n.achv.sample_once.name\", " +
            "\"criteria\": [{\"type\": \"kill_count\", \"observe_event\": \"unit.died\", " +
            "\"target_ref\": \"creature.sample_monster\", \"count\": 1}], \"rewards\": {\"xp\": 10}}" +
            "]";

        private sealed class Fixture
        {
            public AchievementHost Host = null!;
            public FakeRewardDispatcher Rewards = null!;
        }

        private static JsonObject Entry(bool unlocked, int current, bool pending) =>
            new JsonObjectBuilder()
                .Add("unlocked", unlocked ? JsonBool.True : JsonBool.False)
                .Add("current", new JsonArray(new JsonValue[] { new JsonNumber(current) }))
                .Add("pending_reward", pending ? JsonBool.True : JsonBool.False)
                .Build();

        /// <summary>读档前现场：kill 进度 KillCount-1 未解锁；collect 已满足但待领奖；once 已解锁。</summary>
        private static Fixture NewFixture()
        {
            var bus = TestSupport.CreateBus();
            var registry = TestSupport.MakeRegistry(bus, DefRows);
            var rewards = new FakeRewardDispatcher();
            var host = new AchievementHost(
                registry, bus, new FakeUnitAccess(), new FakeExprHostFactory(), rewards,
                new AchievementOptions(() => Player));

            host.Load(new JsonObjectBuilder()
                .Add(Kill.Value, Entry(false, KillCount - 1, false))
                .Add(Collect.Value, Entry(false, CollectCount, true))
                .Add(Once.Value, Entry(true, 1, false))
                .Build());
            return new Fixture { Host = host, Rewards = rewards };
        }

        private static string Capture(Fixture f)
        {
            var parts = new[] { Kill, Collect, Once }.Select(id =>
                $"{id}|unlocked={f.Host.IsUnlocked(Player, id)}|" +
                string.Join(",", f.Host.GetProgress(Player, id).Select(p => $"{p.Current}/{p.Target}")));
            return string.Join(";", parts) + "##grants=" + f.Rewards.Grants.Count + "##" + JsonWriter.Write(f.Host.Save());
        }

        private static void AssertRejectedAndUnchanged(Fixture f, JsonValue bad)
        {
            var before = Capture(f);

            var ex = Record.Exception(() => f.Host.Load(bad));

            Assert.IsType<FormatException>(ex);
            Assert.Equal(before, Capture(f));
        }

        [Fact]
        public void Fixture_PreLoadStateIsAsDesigned()
        {
            // 防止夹具失效使后面的"状态不变"断言空转：三种状态确实都存在。
            var f = NewFixture();
            Assert.Equal(KillCount - 1, f.Host.GetProgress(Player, Kill)[0].Current);
            Assert.False(f.Host.IsUnlocked(Player, Kill));
            Assert.False(f.Host.IsUnlocked(Player, Collect));
            Assert.True(f.Host.IsUnlocked(Player, Once));
            Assert.Contains("\"pending_reward\":true", JsonWriter.Write(f.Host.Save()).Replace(" ", string.Empty));
        }

        // ---- 1. 根不是对象 ----

        [Fact]
        public void Load_RootIsString_ThrowsFormatException_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), new JsonString("wrong-shape"));

        [Fact]
        public void Load_RootIsArray_ThrowsFormatException_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), new JsonArray(Array.Empty<JsonValue>()));

        [Fact]
        public void Load_RootIsNumber_ThrowsFormatException_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), new JsonNumber(1));

        [Fact]
        public void Load_RootIsBool_ThrowsFormatException_AndKeepsState() =>
            AssertRejectedAndUnchanged(NewFixture(), JsonBool.False);

        // ---- 2. 条目形状不符（已知成就 id） ----

        [Fact]
        public void Load_KnownAchievementEntryIsNotObject_ThrowsFormatException_AndKeepsState()
        {
            var bad = new JsonObjectBuilder().Add(Kill.Value, new JsonString("not-an-object")).Build();
            AssertRejectedAndUnchanged(NewFixture(), bad);
        }

        [Fact]
        public void Load_KnownAchievementEntryIsArray_ThrowsFormatException_AndKeepsState()
        {
            var bad = new JsonObjectBuilder().Add(Collect.Value, new JsonArray(Array.Empty<JsonValue>())).Build();
            AssertRejectedAndUnchanged(NewFixture(), bad);
        }

        // ---- 3. 原子性：合法条目在前、坏条目在后，前面的不得被提前提交 ----

        [Fact]
        public void Load_LaterKnownEntryBad_DoesNotPartiallyCommitEarlierValidEntries()
        {
            // kill 条目合法（若被提前提交，进度会被改成 0 且清掉既有 pending/unlocked），once 条目非法。
            var bad = new JsonObjectBuilder()
                .Add(Kill.Value, Entry(true, KillCount, false))
                .Add(Once.Value, JsonBool.True)
                .Build();
            var f = NewFixture();

            AssertRejectedAndUnchanged(f, bad);

            Assert.False(f.Host.IsUnlocked(Player, Kill));
            Assert.Equal(KillCount - 1, f.Host.GetProgress(Player, Kill)[0].Current);
            Assert.True(f.Host.IsUnlocked(Player, Once));
        }

        [Fact]
        public void Load_AfterRejectedBadData_ValidSnapshotStillLoadsNormally()
        {
            var f = NewFixture();
            var good = f.Host.Save();
            Assert.Throws<FormatException>(() => f.Host.Load(new JsonString("bad")));

            f.Host.Load(good);

            Assert.Equal(KillCount - 1, f.Host.GetProgress(Player, Kill)[0].Current);
            Assert.True(f.Host.IsUnlocked(Player, Once));
        }

        // ---- 4. 引用不存在的成就 id：被忽略，不阻断读档 ----

        [Fact]
        public void Load_UnknownAchievementId_IsIgnored_KnownEntriesStillApplied()
        {
            var f = NewFixture();
            var data = new JsonObjectBuilder()
                .Add("achv.sample_removed_from_content", Entry(true, 9, false))
                .Add(Kill.Value, Entry(false, 1, false))
                .Build();

            f.Host.Load(data); // 不抛

            Assert.Equal(1, f.Host.GetProgress(Player, Kill)[0].Current);
            // Load 是"整体替换该玩家全部成就记录"：未出现在快照里的已知成就被清空（collect 的 pending、once 的解锁都没了）。
            Assert.False(f.Host.IsUnlocked(Player, Once));
            Assert.Equal(0, f.Host.GetProgress(Player, Collect)[0].Current);
            Assert.DoesNotContain("achv.sample_removed_from_content", JsonWriter.Write(f.Host.Save()));
            Assert.Empty(f.Rewards.Grants);
        }

        [Fact]
        public void Load_UnknownAchievementIdWithMalformedEntry_IsIgnoredBeforeShapeValidation()
        {
            // 口径：未知 id 在形状校验之前就被 continue 掉，所以形状非法的"孤儿条目"不会让整份存档读档失败。
            var f = NewFixture();
            var data = new JsonObjectBuilder()
                .Add("achv.sample_removed_from_content", new JsonNumber(42))
                .Add(Kill.Value, Entry(false, 2, false))
                .Build();

            var ex = Record.Exception(() => f.Host.Load(data));

            Assert.Null(ex);
            Assert.Equal(2, f.Host.GetProgress(Player, Kill)[0].Current);
        }

        // ---- 5. 字段类型不符：现状是宽松读取（待设计层确认，见类注释） ----

        [Fact]
        public void Characterization_UnlockedFieldWrongType_ReadAsFalse_NoThrow()
        {
            var f = NewFixture();
            var entry = new JsonObjectBuilder()
                .Add("unlocked", new JsonString("yes"))
                .Add("current", new JsonArray(new JsonValue[] { new JsonNumber(1) }))
                .Build();

            var ex = Record.Exception(() => f.Host.Load(new JsonObjectBuilder().Add(Once.Value, entry).Build()));

            Assert.Null(ex);
            Assert.False(f.Host.IsUnlocked(Player, Once));
            Assert.Equal(1, f.Host.GetProgress(Player, Once)[0].Current);
        }

        [Fact]
        public void Characterization_CurrentFieldWrongType_ReadAsZeros_NoThrow()
        {
            var f = NewFixture();
            var entry = new JsonObjectBuilder()
                .Add("unlocked", JsonBool.False)
                .Add("current", new JsonString("not-an-array"))
                .Build();

            var ex = Record.Exception(() => f.Host.Load(new JsonObjectBuilder().Add(Kill.Value, entry).Build()));

            Assert.Null(ex);
            Assert.Equal(0, f.Host.GetProgress(Player, Kill)[0].Current);
        }

        [Fact]
        public void Characterization_CurrentElementWrongType_ReadAsZero_NoThrow()
        {
            var f = NewFixture();
            var entry = new JsonObjectBuilder()
                .Add("unlocked", JsonBool.False)
                .Add("current", new JsonArray(new JsonValue[] { new JsonString("2") }))
                .Build();

            var ex = Record.Exception(() => f.Host.Load(new JsonObjectBuilder().Add(Kill.Value, entry).Build()));

            Assert.Null(ex);
            Assert.Equal(0, f.Host.GetProgress(Player, Kill)[0].Current);
        }

        [Fact]
        public void Characterization_EntryMissingAllOptionalFields_ReadAsEmptyProgress_NoThrow()
        {
            // 本段没有"必填字段"：空对象条目等价于"无进度、未解锁、无待领奖"。
            var f = NewFixture();

            var ex = Record.Exception(() => f.Host.Load(new JsonObjectBuilder().Add(Kill.Value, new JsonObjectBuilder().Build()).Build()));

            Assert.Null(ex);
            Assert.False(f.Host.IsUnlocked(Player, Kill));
            Assert.Equal(0, f.Host.GetProgress(Player, Kill)[0].Current);
        }
    }
}
