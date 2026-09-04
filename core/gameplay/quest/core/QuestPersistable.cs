using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// <c>player.quest_state</c> 存档段（见 10 第 2.2 节 <c>quest_state: Map&lt;Id, QuestProgress&gt;</c>、
    /// 第 3 节步骤 6）。<see cref="QuestHost"/> 本身不实现 <see cref="IPersistable"/>——判断记录：
    /// <c>QuestHost</c> 的运行期状态是"每个单位的每条任务"两维记录（<c>(unitId, questId)</c>），而
    /// 10 第 2.2 节 <c>quest_state</c> 字段表原文形状是单个玩家的 <c>Map&lt;questId, QuestProgress&gt;</c>
    /// （单机单人游戏只有一个玩家单位）；本类型按存档段的原文形状序列化"当前玩家"一个单位的记录，
    /// 与 <c>core/gameplay/quest/core/QuestExprGroupProvider.cs</c> 判断记录同一惯例——构造期注入
    /// <see cref="Func{Id}"/> 解析当前玩家单位，而不是把多单位结构塞进单一存档段。
    /// </summary>
    public sealed class QuestPersistable : IPersistable
    {
        private readonly QuestHost _host;
        private readonly Func<Id> _playerUnitProvider;

        public QuestPersistable(QuestHost host, Func<Id> playerUnitProvider)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _playerUnitProvider = playerUnitProvider ?? throw new ArgumentNullException(nameof(playerUnitProvider));
        }

        public string SectionKey => SaveSections.PlayerQuestState;

        public JsonValue Save()
        {
            var player = _playerUnitProvider();
            var builder = new JsonObjectBuilder();

            foreach (var (unitId, progress) in _host.SnapshotAll())
            {
                if (!unitId.Equals(player))
                {
                    continue;
                }

                var countsArray = new List<JsonValue>(progress.ObjectiveCounts.Count);
                foreach (var count in progress.ObjectiveCounts)
                {
                    countsArray.Add(new JsonNumber(count, count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }

                var entry = new JsonObjectBuilder()
                    .Add("state", new JsonString(progress.State.ToString()))
                    .Add("objective_counts", new JsonArray(countsArray))
                    .Add("completion_count", new JsonNumber(progress.CompletionCount, progress.CompletionCount.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    .Add("last_completed_day", progress.LastCompletedDay.HasValue
                        ? new JsonNumber(progress.LastCompletedDay.Value, progress.LastCompletedDay.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        : (JsonValue)JsonNull.Instance)
                    .Build();

                builder.Add(progress.QuestId.Value, entry);
            }

            return builder.Build();
        }

        public void Load(JsonValue data)
        {
            if (data is JsonNull)
            {
                return;
            }
            if (!(data is JsonObject obj))
            {
                throw new FormatException($"player.quest_state 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
            }

            var player = _playerUnitProvider();

            foreach (var kv in obj)
            {
                var questId = new Id(kv.Key);
                if (!(kv.Value is JsonObject entry))
                {
                    throw new FormatException($"player.quest_state[\"{kv.Key}\"] 不是 JSON 对象");
                }

                if (!entry.TryGetValue("state", out var stateVal) || !(stateVal is JsonString stateStr) ||
                    !Enum.TryParse<QuestState>(stateStr.Value, out var state))
                {
                    throw new FormatException($"player.quest_state[\"{kv.Key}\"].state 缺失或取值非法");
                }

                var counts = new List<int>();
                if (entry.TryGetValue("objective_counts", out var countsVal) && countsVal is JsonArray countsArr)
                {
                    foreach (var c in countsArr)
                    {
                        if (c is JsonNumber n && n.TryGetInt64(out var i))
                        {
                            counts.Add((int)i);
                        }
                    }
                }

                var completionCount = 0;
                if (entry.TryGetValue("completion_count", out var ccVal) && ccVal is JsonNumber ccNum && ccNum.TryGetInt64(out var ccLong))
                {
                    completionCount = (int)ccLong;
                }

                long? lastCompletedDay = null;
                if (entry.TryGetValue("last_completed_day", out var lcdVal) && lcdVal is JsonNumber lcdNum && lcdNum.TryGetInt64(out var lcdLong))
                {
                    lastCompletedDay = lcdLong;
                }

                _host.RestoreProgress(player, new QuestProgress(questId, state, counts, completionCount, lastCompletedDay));
            }
        }
    }
}
