using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Gameplay.Quest;

namespace Presentation.Ui
{
    /// <summary>
    /// 任务日志视图模型（见 09_表现层.md 第 7.1 节 UI 组成"任务日志"）。
    /// <para>
    /// 判断记录：<see cref="IUiDataSource.Query"/> 的 <c>player.quest.&lt;questId&gt;.*</c> 路径
    /// （见 <see cref="PlayerPathProvider"/>）要求调用方已经知道具体某个 <c>questId</c> 才能查——
    /// 天生适合"我已经知道这条任务，查它的状态/某条目标进度"，但任务日志面板需要的是"列出该单位
    /// 全部有记录的任务"，属于枚举查询，路径小语法没有为它设计语法（也不该设计——枚举整份日志
    /// 不是"按 key 查一个值"的形状）。<see cref="IQuestHost.GetLog"/> 本身就是只读查询方法（不
    /// 修改任何状态），本视图模型因此直接持有 <see cref="IQuestHost"/> 引用调用
    /// <c>GetLog</c>/<c>GetActiveObjectives</c>，不强行绕经 <see cref="IUiDataSource"/>——铁律 P1
    /// 约束的是"只读"，不是"必须经过 Query 字符串"这一种形式。
    /// </para>
    /// </summary>
    public sealed class QuestLogViewModel : IDisposable
    {
        private readonly IUiDataSource _dataSource;
        private readonly IQuestHost _quest;
        private readonly Id _playerId;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private IReadOnlyList<QuestProgress> _log = Array.Empty<QuestProgress>();
        private IReadOnlyList<(Id QuestId, int ObjectiveIndex, Id TargetRef)> _activeObjectives =
            Array.Empty<(Id, int, Id)>();

        public IReadOnlyList<QuestProgress> Log => _log;

        public IReadOnlyList<(Id QuestId, int ObjectiveIndex, Id TargetRef)> ActiveObjectives => _activeObjectives;

        public QuestLogViewModel(IUiDataSource dataSource, IQuestHost quest, Id playerId)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _quest = quest ?? throw new ArgumentNullException(nameof(quest));
            _playerId = playerId;

            _subscriptions.Add(_dataSource.Subscribe(QuestEventKeys.Accepted, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(QuestEventKeys.ObjectiveProgress, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(QuestEventKeys.Completed, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(QuestEventKeys.TurnedIn, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(QuestEventKeys.Failed, OnRelevantEvent));
            // UI-111-01 根治同惯例（见 InventoryViewModel 类型注释）：同图读档的抑制作用域会连带
            // 压住任务推进事件本身，只有在该作用域外正常派发的 save.loaded 能保证读档后整体重建。
            _subscriptions.Add(_dataSource.Subscribe(SaveEventKeys.SaveLoaded, OnRelevantEvent));

            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        public void Refresh()
        {
            _log = _quest.GetLog(_playerId);
            _activeObjectives = _quest.GetActiveObjectives(_playerId);
        }

        public void Dispose()
        {
            foreach (var handle in _subscriptions)
            {
                handle.Dispose();
            }
            _subscriptions.Clear();
        }
    }
}
