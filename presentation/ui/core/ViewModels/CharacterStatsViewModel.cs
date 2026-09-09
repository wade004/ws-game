using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Localization;
using Core.Foundation.SaveSystem;
using Core.Numbers.StatBlock;

namespace Presentation.Ui
{
    /// <summary>一条属性展示行：属性 id、聚合后数值、本地化显示名。</summary>
    public readonly struct CharacterStatEntry
    {
        public Id StatId { get; }

        public double Value { get; }

        public string DisplayName { get; }

        public CharacterStatEntry(Id statId, double value, string displayName)
        {
            StatId = statId;
            Value = value;
            DisplayName = displayName;
        }
    }

    /// <summary>
    /// 角色属性面板视图模型（见 09_表现层.md 第 7.1 节 UI 组成、任务书"属性列表，显示名经
    /// IL10nHost.Text(name_key)"）。构造期注入要展示的属性清单（<c>(statId, nameKey)</c>
    /// 对——具体展示哪些属性、以何种顺序，是数据/游戏层的口味，本模块不硬编码 <c>stat.definition</c>
    /// 全表遍历逻辑，见 <c>core/foundation/data_registry</c> 才是读取内容表全集的正确入口）。
    /// </summary>
    public sealed class CharacterStatsViewModel : IDisposable
    {
        private readonly IUiDataSource _dataSource;
        private readonly IL10nHost _l10n;
        private readonly IReadOnlyList<(Id StatId, Id NameKey)> _statConfig;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private readonly List<CharacterStatEntry> _entries = new List<CharacterStatEntry>();

        /// <summary>本视图模型绑定的玩家单位 id（见 <see cref="HudViewModel.PlayerId"/> 同款判断
        /// 记录）。</summary>
        public Id PlayerId { get; }

        public IReadOnlyList<CharacterStatEntry> Entries => _entries;

        public CharacterStatsViewModel(
            IUiDataSource dataSource,
            IL10nHost l10n,
            Id playerId,
            IReadOnlyList<(Id StatId, Id NameKey)> statConfig)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _l10n = l10n ?? throw new ArgumentNullException(nameof(l10n));
            PlayerId = playerId;
            _statConfig = statConfig ?? throw new ArgumentNullException(nameof(statConfig));

            _subscriptions.Add(_dataSource.Subscribe(StatBlockEventKeys.StatChanged, OnRelevantEvent));
            // UI-111-01 根治同惯例（见 InventoryViewModel 类型注释）：同图读档的抑制作用域会连带
            // 压住 stat.changed 本身，只有在该作用域外正常派发的 save.loaded 能保证读档后整体重建。
            _subscriptions.Add(_dataSource.Subscribe(SaveEventKeys.SaveLoaded, OnRelevantEvent));

            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        public void Refresh()
        {
            _entries.Clear();
            foreach (var (statId, nameKey) in _statConfig)
            {
                var value = _dataSource.Query($"player.stat.{statId}");
                var displayName = _l10n.Text(nameKey);
                _entries.Add(new CharacterStatEntry(statId, value.HasValue ? value.Value.AsNumber : 0, displayName));
            }
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
