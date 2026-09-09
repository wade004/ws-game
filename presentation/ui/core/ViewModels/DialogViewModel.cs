using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Gameplay.Dialog;
using Core.Gameplay.Quest;
using Core.Gameplay.WorldState;

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
    /// <para>
    /// GP-05 根治补充（architecture/落地计划/audit-b3b91ee-20260907/code-review.md）：<c>gossip_menu.
    /// options[].visible_if</c>/<c>story_node.branches[].condition</c> 可以引用任意 Expr 分组（任务
    /// 状态、物品数量、世界标志……），此前本视图模型只在对话框自身的三个事件
    /// （StoryNodeEntered/GossipOpened/Ended）上刷新——对话框开着的时候，若任务被其它渠道推进/
    /// 完成、物品被拾取/消耗、世界标志被翻转，当前显示的选项列表不会跟着刷新，玩家会看到一份
    /// "过期"的可见性快照（仍然能点，直到 <see cref="IDialogHost.ChooseOption"/>/
    /// <see cref="IDialogHost.AdvanceStory"/> 内部重验才会被拒绝——见这两个方法的判断记录）。
    /// 改法：额外订阅 Quest 四类推进事件、物品增减事件、世界标志变化事件，命中即
    /// <see cref="Refresh"/>，让"显示"与"可执行"用的是同一份最新状态，不必等玩家点击才发现选项
    /// 已经失效。
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

            // GP-05 新增：VisibleIf/Condition 常见的引用来源——任务状态变化、背包物品增减、世界
            // 标志翻转——命中任意一个都刷新，见类型判断记录。
            _subscriptions.Add(_dataSource.Subscribe(QuestEventKeys.Accepted, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(QuestEventKeys.ObjectiveProgress, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(QuestEventKeys.Completed, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(QuestEventKeys.TurnedIn, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(QuestEventKeys.Failed, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(CarriersEventKeys.ItemAdded, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(CarriersEventKeys.ItemRemoved, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(WorldStateEventKeys.FlagChanged, OnRelevantEvent));
            // UI-111-01 根治同惯例（见 InventoryViewModel 类型注释）：同图读档的抑制作用域会连带
            // 压住上述业务事件本身，只有在该作用域外正常派发的 save.loaded 能保证读档后整体重建。
            _subscriptions.Add(_dataSource.Subscribe(SaveEventKeys.SaveLoaded, OnRelevantEvent));

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
