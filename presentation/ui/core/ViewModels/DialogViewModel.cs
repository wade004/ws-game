using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Gameplay.Dialog;

namespace Presentation.Ui
{
    /// <summary>
    /// 对话框视图模型（见 09_表现层.md 第 7.1 节 UI 组成"对话框"、任务书"当前菜单/剧情节点与
    /// 选项"）。
    /// <para>
    /// 契约缺口补齐（P4-2）：<see cref="IDialogHost"/> 现已提供对称于 <see cref="IDialogHost.GetStoryView"/>
    /// 的只读查询 <see cref="IDialogHost.GetGossipView"/>，<see cref="Refresh"/> 与剧情视图同一惯例
    /// 直接查询即可，不再需要调用方手动灌入（见下 <see cref="SetGossipView"/> 判断记录）。
    /// </para>
    /// </summary>
    public sealed class DialogViewModel : IDisposable
    {
        private readonly IDialogHost _dialog;
        private readonly IUiDataSource _dataSource;
        private readonly Id _playerId;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();

        public StoryView? Story { get; private set; }

        public GossipView? Gossip { get; private set; }

        public bool IsOpen => Story != null || Gossip != null;

        public DialogViewModel(IUiDataSource dataSource, IDialogHost dialog, Id playerId)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
            _playerId = playerId;

            _subscriptions.Add(_dataSource.Subscribe(DialogEventKeys.StoryNodeEntered, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(DialogEventKeys.GossipOpened, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(DialogEventKeys.Ended, OnRelevantEvent));

            Refresh();
        }

        /// <summary>判断记录（P4-2 遗留兼容点）：保留本方法，供尚未升级到直接依赖
        /// <see cref="IDialogHost.GetGossipView"/> 的既有调用方按旧用法手动灌入；<see cref="Refresh"/>
        /// 本身已不依赖它——下一次 <see cref="Refresh"/>（含事件触发的自动刷新）会用
        /// <see cref="IDialogHost.GetGossipView"/> 的查询结果覆盖本次手动灌入的值。</summary>
        public void SetGossipView(GossipView view) => Gossip = view;

        private void OnRelevantEvent(IEvent evt) => Refresh();

        public void Refresh()
        {
            Story = _dialog.GetStoryView(_playerId);
            Gossip = _dialog.GetGossipView(_playerId);
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
