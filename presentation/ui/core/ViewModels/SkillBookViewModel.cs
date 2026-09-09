using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Rules.Common;

namespace Presentation.Ui
{
    /// <summary>一条已知技能的快照。</summary>
    public readonly struct SkillBookEntrySnapshot
    {
        public Id SkillId { get; }

        public double Cooldown { get; }

        public bool Ready => Cooldown <= 0;

        public SkillBookEntrySnapshot(Id skillId, double cooldown)
        {
            SkillId = skillId;
            Cooldown = cooldown;
        }
    }

    /// <summary>
    /// 技能书视图模型（见 09_表现层.md 第 7.1 节 UI 组成、任务书 10 个视图模型清单
    /// "SkillBookViewModel"）：列出该单位全部已知技能与各自冷却状态。
    /// </summary>
    public sealed class SkillBookViewModel : IDisposable
    {
        private readonly IUiDataSource _dataSource;
        private readonly ISkillBookQuery _skillBook;
        private readonly Id _playerId;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private readonly List<SkillBookEntrySnapshot> _entries = new List<SkillBookEntrySnapshot>();

        public IReadOnlyList<SkillBookEntrySnapshot> Entries => _entries;

        public SkillBookViewModel(IUiDataSource dataSource, ISkillBookQuery skillBook, Id playerId)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _skillBook = skillBook ?? throw new ArgumentNullException(nameof(skillBook));
            _playerId = playerId;

            _subscriptions.Add(_dataSource.Subscribe(RulesEventKeys.SkillCastStart, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(RulesEventKeys.SkillCastSuccess, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(RulesEventKeys.SkillCastFailed, OnRelevantEvent));
            // UI-111-01 根治同惯例（见 InventoryViewModel 类型注释）：同图读档的抑制作用域会连带
            // 压住施法事件本身，只有在该作用域外正常派发的 save.loaded 能保证读档后已知技能/冷却
            // 整体重建。
            _subscriptions.Add(_dataSource.Subscribe(SaveEventKeys.SaveLoaded, OnRelevantEvent));

            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        public void Refresh()
        {
            _entries.Clear();
            foreach (var skillId in _skillBook.GetKnownSkills(_playerId))
            {
                _entries.Add(new SkillBookEntrySnapshot(skillId, _skillBook.GetCooldown(_playerId, skillId)));
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
