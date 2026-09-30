using System;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// ADR-0125 第三批探针缺陷：<see cref="QuestPersistable.Load"/> 用 <c>Enum.TryParse</c> 解析 <c>state</c>，
    /// 数字串、负数、枚举值按位或的组合串都能解析成功，把未定义的 <see cref="QuestState"/> 值写进运行期。
    /// 期望：只接受枚举名本身（<c>Enum.GetNames</c>），其余一律 <see cref="FormatException"/>，
    /// 且失败时任务状态与 Load 前逐项相等。
    /// </summary>
    public sealed class ADR0125_QuestPersistableStateEnumTests
    {
        private static readonly Id Player = TestSupport.Player;
        private static readonly Id QuestId = new Id("quest.sample_adr0125");

        private static Harness NewHarness() => new Harness(new[]
        {
            new QuestDefinition(QuestId,
                new[] { new QuestObjective(QuestObjectiveType.Kill, new Id("creature.sample_wolf"), 3) },
                QuestStartMethod.NpcGossip, QuestTurnInMethod.NpcGossip, QuestRepeatable.None),
        });

        private static JsonValue Snapshot(string state) =>
            new JsonObjectBuilder().Add(QuestId.Value, new JsonObjectBuilder().Add("state", new JsonString(state)).Build()).Build();

        public static System.Collections.Generic.IEnumerable<object[]> NonNameStates()
        {
            var all = (QuestState[])Enum.GetValues(typeof(QuestState));
            var undefined = all.Max(v => (int)v) + 1;
            yield return new object[] { undefined.ToString() };                                       // 未定义的数字
            yield return new object[] { "-1" };                                                        // 负数
            foreach (var v in all)
            {
                yield return new object[] { ((int)v).ToString() };                                     // 已定义值的数字串
            }
            // 两个枚举名按位或：值恰好等于另一个已定义枚举值时，IsDefined 也拦不住，必须按名字精确匹配。
            var a = QuestState.Available;
            var b = QuestState.Active;
            yield return new object[] { a + "," + b };
            yield return new object[] { "Active,TurnedIn" };
        }

        [Theory]
        [MemberData(nameof(NonNameStates))]
        public void Load_StateNotAnEnumName_ThrowsFormatException_AndStateUnchanged(string state)
        {
            var h = NewHarness();
            Assert.True(h.Host.Accept(Player, QuestId));
            var p = new QuestPersistable(h.Host, () => Player);
            var before = h.Host.GetState(Player, QuestId);

            Assert.Throws<FormatException>(() => p.Load(Snapshot(state)));

            Assert.Equal(before, h.Host.GetState(Player, QuestId));
        }

        [Fact]
        public void Load_EveryDefinedEnumName_IsAccepted()
        {
            foreach (var name in Enum.GetNames(typeof(QuestState)))
            {
                var h = NewHarness();
                var p = new QuestPersistable(h.Host, () => Player);

                p.Load(Snapshot(name));

                Assert.Equal(name, h.Host.GetLog(Player).Single(x => x.QuestId.Equals(QuestId)).State.ToString());
            }
        }
    }
}
