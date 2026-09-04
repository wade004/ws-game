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
    /// 契约缺口（判断记录）：<see cref="IDialogHost"/> 对剧情树提供了随时可查的只读视图
    /// （<see cref="IDialogHost.GetStoryView"/>），但对 gossip 菜单没有对应的"当前已打开菜单是
    /// 什么"只读查询——<see cref="IDialogHost.OpenGossip"/> 只在"打开的那一刻"把
    /// <see cref="GossipView"/> 作为返回值给出，此后没有方法能重新查询"这个单位当前打开的 gossip
    /// 菜单是哪一份"。本视图模型因此只能对剧情树做"任意时刻刷新都拿得到当前视图"的完整支持；
    /// gossip 菜单退化为"由打开动作的调用方把 <see cref="GossipView"/> 结果自行喂给
    /// <see cref="SetGossipView"/>"，本视图模型不会、也无法在 <see cref="Refresh"/> 里凭空重新
    /// 查出 gossip 视图。建议后续给 <see cref="IDialogHost"/> 补一个
    /// <c>GetGossipView(unitId): GossipView?</c> 只读查询，与 <see cref="IDialogHost.GetStoryView"/>
    /// 对称。
    /// </para>
    /// </summary>
    public sealed class DialogViewModel : IDisposable
    {
        private readonly IDialogHost _dialog;
        private readonly IUiDataSource _dataSource;
        private readonly Id _playerId;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();

        public StoryView? Story { get; private set; }

        /// <summary>见类型注释"契约缺口"：由调用方在打开 gossip 菜单后经 <see cref="SetGossipView"/>
        /// 手动灌入；<see cref="Refresh"/>（含事件触发的自动刷新）不会改变它，只有
        /// <see cref="DialogEventKeys.Ended"/> 时清空。</summary>
        public GossipView? Gossip { get; private set; }

        public bool IsOpen => Story != null || Gossip != null;

        public DialogViewModel(IUiDataSource dataSource, IDialogHost dialog, Id playerId)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
            _playerId = playerId;

            _subscriptions.Add(_dataSource.Subscribe(DialogEventKeys.StoryNodeEntered, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(DialogEventKeys.GossipOpened, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(DialogEventKeys.Ended, OnDialogEnded));

            Refresh();
        }

        /// <summary>见类型注释"契约缺口"：由打开 gossip 菜单的调用方把 <see cref="IDialogHost.OpenGossip"/>
        /// 的返回值传进来。</summary>
        public void SetGossipView(GossipView view) => Gossip = view;

        private void OnRelevantEvent(IEvent evt) => Refresh();

        private void OnDialogEnded(IEvent evt)
        {
            Gossip = null;
            Refresh();
        }

        public void Refresh()
        {
            Story = _dialog.GetStoryView(_playerId);
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
