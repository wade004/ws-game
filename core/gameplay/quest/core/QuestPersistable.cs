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

                // 以下三处改用 JsonNumber.FromInt64（第十七方深度审核 V-02 收口后新增的统一工厂）代替
                // 手写的 ToString(InvariantCulture)，行为不变（本就是精确整数原始文本），只是收敛到
                // 一个公开工厂，避免各处各写一份格式化代码。
                var countsArray = new List<JsonValue>(progress.ObjectiveCounts.Count);
                foreach (var count in progress.ObjectiveCounts)
                {
                    countsArray.Add(JsonNumber.FromInt64(count));
                }

                var entry = new JsonObjectBuilder()
                    .Add("state", new JsonString(progress.State.ToString()))
                    .Add("objective_counts", new JsonArray(countsArray))
                    .Add("completion_count", JsonNumber.FromInt64(progress.CompletionCount))
                    .Add("last_completed_day", progress.LastCompletedDay.HasValue
                        ? JsonNumber.FromInt64(progress.LastCompletedDay.Value)
                        : (JsonValue)JsonNull.Instance)
                    .Build();

                builder.Add(progress.QuestId.Value, entry);
            }

            return builder.Build();
        }

        public void Load(JsonValue data)
        {
            var player = _playerUnitProvider();

            // GP-01 根治（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：旧实现
            // 只对快照里出现的 questId 调用 RestoreProgress，快照未覆盖到的任务（包括"空档"——
            // 即当前 questId 集合为空，运行期却已接取/完成了任务）原样残留，读档无法回滚到保存点。
            // 改法：JsonNull（段显式为空）与"空对象快照"都按同一语义处理——把该玩家单位的任务状态
            // 整体替换为快照内容（空快照即清空全部任务），而不是"什么都不做"。
            if (data is JsonNull)
            {
                _host.ReplaceAllProgress(player, Array.Empty<QuestProgress>());
                return;
            }
            if (!(data is JsonObject obj))
            {
                throw new FormatException($"player.quest_state 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
            }

            // 先把整段 JSON 完整解析/校验进临时列表——任何一条格式非法都会在这里抛异常，此时还
            // 没有触碰 QuestHost 的任何运行期状态；全部校验通过后才一次性提交替换，避免"解析到一半
            // 失败、部分任务已被清空"的半提交状态（见 GP-01 验收"临时状态校验后一次提交"）。
            var snapshot = new List<QuestProgress>(obj.Count);
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

                snapshot.Add(new QuestProgress(questId, state, counts, completionCount, lastCompletedDay));
            }

            _host.ReplaceAllProgress(player, snapshot);
        }
    }
}
