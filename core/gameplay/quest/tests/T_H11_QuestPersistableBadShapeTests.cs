using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// T-H11（docs/复盘/测试覆盖梳理-2026-10-01.md 第 3 节）：<see cref="QuestPersistable.Load"/> 坏形状。
    /// 写法参照 <c>CORE_170_03_CurrencyAndVendorStockPersistableLoadFailureTests</c>：构造一份有多条任务
    /// 进度的"读档前状态"，用坏数据 Load，断言抛异常、且 <see cref="QuestHost.GetLog"/> 与每条任务的
    /// <see cref="QuestHost.GetState"/> 与 Load 前逐项相等（<c>ReplaceAllProgress</c> 之前整份数据先解析进临时
    /// 列表，失败时没有触碰运行期状态）。
    /// <para>
    /// 异常类型口径（测试只钉实际行为，不改口径）：根不是对象 / 条目不是对象 / <c>state</c> 缺失或类型不符或
    /// 取值非法 → <see cref="FormatException"/>；key 不是合法 <see cref="Id"/> → <see cref="ArgumentException"/>
    /// （<c>new Id(key)</c>）；key 是合法 Id 但任务定义不存在 → <see cref="ArgumentException"/>
    /// （<c>RequireDef</c>），二者与同仓库其它存档段的 <see cref="FormatException"/> 不一致，但
    /// <c>SaveSystem.Load</c> 对任意异常一视同仁地转成 <c>PersistableThrew</c> 并回滚，运行期无差别。
    /// </para>
    /// </summary>
    public sealed class T_H11_QuestPersistableBadShapeTests
    {
        private static readonly Id Player = TestSupport.Player;
        private static readonly Id QuestA = new Id("quest.sample_h11_a");
        private static readonly Id QuestB = new Id("quest.sample_h11_b");
        private const int ObjectiveCount = 3;

        private static QuestDefinition Hunt(Id id) => new QuestDefinition(
            id,
            new[] { new QuestObjective(QuestObjectiveType.Kill, new Id("creature.sample_h11_wolf"), ObjectiveCount) },
            QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None);

        private sealed class Fixture
        {
            public Harness Harness = null!;
            public QuestPersistable Persistable = null!;
        }

        /// <summary>读档前状态：A 已接取且进度推到 2/3，B 已接取且进度为 0。</summary>
        private static Fixture NewFixture()
        {
            var h = new Harness(new[] { Hunt(QuestA), Hunt(QuestB) });
            Assert.True(h.Host.Accept(Player, QuestA));
            Assert.True(h.Host.Accept(Player, QuestB));
            h.Host.UpdateProgress(Player, QuestA, 0, ObjectiveCount - 1);
            return new Fixture { Harness = h, Persistable = new QuestPersistable(h.Host, () => Player) };
        }

        /// <summary>可观测状态快照：按 questId 排序，逐项含 State / 各目标计数 / 完成次数 / 最近完成日，
        /// 再加每条任务的 <see cref="QuestHost.GetState"/>，以及 Save() 文本。</summary>
        private static string Capture(Fixture f)
        {
            var log = f.Harness.Host.GetLog(Player)
                .OrderBy(p => p.QuestId.Value, StringComparer.Ordinal)
                .Select(p => $"{p.QuestId}|{p.State}|[{string.Join(",", p.ObjectiveCounts)}]|{p.CompletionCount}|{p.LastCompletedDay?.ToString() ?? "-"}");
            var states = new[] { QuestA, QuestB }.Select(q => $"{q}={f.Harness.Host.GetState(Player, q)}");
            return string.Join(";", log) + "##" + string.Join(";", states) + "##" + JsonWriter.Write(f.Persistable.Save());
        }

        private static JsonValue Entry(JsonValue state, JsonValue? counts = null) =>
            new JsonObjectBuilder()
                .Add("state", state)
                .Add("objective_counts", counts ?? new JsonArray(new JsonValue[] { new JsonNumber(1) }))
                .Add("completion_count", new JsonNumber(0))
                .Add("last_completed_day", JsonNull.Instance)
                .Build();

        private static void AssertRejectedAndUnchanged<TException>(Fixture f, JsonValue bad) where TException : Exception
        {
            var before = Capture(f);

            var ex = Record.Exception(() => f.Persistable.Load(bad));

            Assert.IsType<TException>(ex);
            Assert.Equal(before, Capture(f));
        }

        // ---- 1. 根不是对象 ----

        [Fact]
        public void Load_RootIsString_ThrowsFormatException_AndKeepsState() =>
            AssertRejectedAndUnchanged<FormatException>(NewFixture(), new JsonString("wrong-shape"));

        [Fact]
        public void Load_RootIsArray_ThrowsFormatException_AndKeepsState() =>
            AssertRejectedAndUnchanged<FormatException>(NewFixture(), new JsonArray(Array.Empty<JsonValue>()));

        [Fact]
        public void Load_RootIsNumber_ThrowsFormatException_AndKeepsState() =>
            AssertRejectedAndUnchanged<FormatException>(NewFixture(), new JsonNumber(1));

        [Fact]
        public void Load_RootIsBool_ThrowsFormatException_AndKeepsState() =>
            AssertRejectedAndUnchanged<FormatException>(NewFixture(), JsonBool.True);

        // ---- 2. 必填字段缺失 / 条目形状不符 ----

        [Fact]
        public void Load_EntryIsNotObject_ThrowsFormatException_AndKeepsState()
        {
            var bad = new JsonObjectBuilder().Add(QuestA.Value, new JsonNumber(7)).Build();
            AssertRejectedAndUnchanged<FormatException>(NewFixture(), bad);
        }

        [Fact]
        public void Load_EntryMissingRequiredState_ThrowsFormatException_AndKeepsState()
        {
            var entryWithoutState = new JsonObjectBuilder()
                .Add("objective_counts", new JsonArray(new JsonValue[] { new JsonNumber(1) }))
                .Add("completion_count", new JsonNumber(0))
                .Build();
            var bad = new JsonObjectBuilder().Add(QuestA.Value, entryWithoutState).Build();
            AssertRejectedAndUnchanged<FormatException>(NewFixture(), bad);
        }

        // ---- 3. 字段类型不符 / 取值非法 ----

        [Fact]
        public void Load_StateIsNumber_ThrowsFormatException_AndKeepsState()
        {
            var bad = new JsonObjectBuilder().Add(QuestA.Value, Entry(new JsonNumber(1))).Build();
            AssertRejectedAndUnchanged<FormatException>(NewFixture(), bad);
        }

        [Fact]
        public void Load_StateIsUnknownEnumName_ThrowsFormatException_AndKeepsState()
        {
            var bad = new JsonObjectBuilder().Add(QuestA.Value, Entry(new JsonString("NotAQuestState"))).Build();
            AssertRejectedAndUnchanged<FormatException>(NewFixture(), bad);
        }

        // ---- 4. 引用不存在的 id / 非法 id ----

        [Fact]
        public void Load_UnknownQuestId_ThrowsArgumentException_AndKeepsState()
        {
            // 合法 Id 形状但任务定义不存在：RequireDef 抛 ArgumentException（不是 FormatException，见类注释）。
            var bad = new JsonObjectBuilder().Add("quest.sample_h11_not_defined", Entry(new JsonString("Active"))).Build();
            AssertRejectedAndUnchanged<ArgumentException>(NewFixture(), bad);
        }

        [Fact]
        public void Load_QuestKeyIsNotAValidId_ThrowsArgumentException_AndKeepsState()
        {
            var bad = new JsonObjectBuilder().Add("Not A Legal Id!!", Entry(new JsonString("Active"))).Build();
            AssertRejectedAndUnchanged<ArgumentException>(NewFixture(), bad);
        }

        // ---- 5. 原子性：合法条目在前、坏条目在后，前面的合法条目不得被提前提交 ----

        [Fact]
        public void Load_LaterEntryBad_DoesNotPartiallyCommitEarlierValidEntry()
        {
            // QuestA 条目完全合法（若被提前提交，A 的进度会从 2 变成 1、状态变 ObjectivesComplete 等），
            // QuestB 条目 state 非法 → 整份拒绝，A 必须仍是读档前的 2/3。
            var bad = new JsonObjectBuilder()
                .Add(QuestA.Value, Entry(new JsonString("Active"), new JsonArray(new JsonValue[] { new JsonNumber(1) })))
                .Add(QuestB.Value, Entry(new JsonString("NotAQuestState")))
                .Build();
            var f = NewFixture();

            AssertRejectedAndUnchanged<FormatException>(f, bad);

            var a = f.Harness.Host.GetLog(Player).Single(p => p.QuestId.Equals(QuestA));
            Assert.Equal(ObjectiveCount - 1, a.ObjectiveCounts[0]);
        }

        [Fact]
        public void Load_LaterEntryReferencesUnknownQuest_DoesNotPartiallyCommitEarlierValidEntry()
        {
            var bad = new JsonObjectBuilder()
                .Add(QuestA.Value, Entry(new JsonString("Active"), new JsonArray(new JsonValue[] { new JsonNumber(0) })))
                .Add("quest.sample_h11_not_defined", Entry(new JsonString("Active")))
                .Build();
            var f = NewFixture();

            AssertRejectedAndUnchanged<ArgumentException>(f, bad);

            var a = f.Harness.Host.GetLog(Player).Single(p => p.QuestId.Equals(QuestA));
            Assert.Equal(ObjectiveCount - 1, a.ObjectiveCounts[0]);
        }

        [Fact]
        public void Load_AfterRejectedBadData_ValidSnapshotStillLoadsNormally()
        {
            // 失败的 Load 不应留下任何"半失败"副作用：随后一份合法快照仍能正常整体替换。
            var f = NewFixture();
            var good = f.Persistable.Save();
            f.Harness.Host.UpdateProgress(Player, QuestA, 0, 1);
            Assert.ThrowsAny<Exception>(() => f.Persistable.Load(new JsonString("bad")));

            f.Persistable.Load(good);

            var a = f.Harness.Host.GetLog(Player).Single(p => p.QuestId.Equals(QuestA));
            Assert.Equal(ObjectiveCount - 1, a.ObjectiveCounts[0]);
        }
    }
}
