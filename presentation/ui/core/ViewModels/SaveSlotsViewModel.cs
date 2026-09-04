using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;

namespace Presentation.Ui
{
    /// <summary>
    /// 存档槽视图模型（见 09_表现层.md 第 9 节 Shell "存档槽：展示存档摘要（见
    /// 10_存档与持久化.md 的 meta 段字段）"、任务书"ISaveSystem.ListSlots 摘要"）。同一套 UI 框架
    /// 供游戏内"读档/另存"面板与 Shell 的存档槽界面共用（见 09 第 9 节"Shell 与游戏内 HUD 共用
    /// 同一套 UI 框架与数据绑定方式"），因此把它放在 <c>presentation/ui</c> 而不是
    /// <c>presentation/shell</c>——<c>presentation/shell</c> 的 <see cref="ShellViewModel"/>
    /// 只持有"当前显示哪个槽位摘要文本"这类 Shell 专属状态，槽位枚举本身复用本类型。
    /// </summary>
    public sealed class SaveSlotsViewModel : IDisposable
    {
        private readonly IUiDataSource _dataSource;
        private readonly ISaveSystem _saveSystem;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private IReadOnlyList<SaveSlotInfo> _slots = Array.Empty<SaveSlotInfo>();

        public IReadOnlyList<SaveSlotInfo> Slots => _slots;

        public SaveSlotsViewModel(IUiDataSource dataSource, ISaveSystem saveSystem)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _saveSystem = saveSystem ?? throw new ArgumentNullException(nameof(saveSystem));

            _subscriptions.Add(_dataSource.Subscribe(SaveEventKeys.SaveCompleted, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(SaveEventKeys.SaveLoaded, OnRelevantEvent));

            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        public void Refresh()
        {
            _slots = _saveSystem.ListSlots();
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
