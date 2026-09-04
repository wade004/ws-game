using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;

namespace Presentation.Ui
{
    /// <summary>一个动作条槽位的快照。</summary>
    public readonly struct ActionBarSlotSnapshot
    {
        /// <summary>绑定的技能 id；槽位为空时为 null。</summary>
        public Id? SkillId { get; }

        public double Cooldown { get; }

        /// <summary>是否可用（有绑定技能且冷却已就绪；不含资源是否足够——资源检查属于施法结算，
        /// 见 06 第 3.6 节，本视图模型只做只读展示不重复该判定）。</summary>
        public bool Available => SkillId.HasValue && Cooldown <= 0;

        public ActionBarSlotSnapshot(Id? skillId, double cooldown)
        {
            SkillId = skillId;
            Cooldown = cooldown;
        }
    }

    /// <summary>
    /// 动作条视图模型（见 09_表现层.md 第 7.1 节 UI 组成"动作条"、任务书"槽位→技能 id、冷却进度、
    /// 可用性；槽数由 UiLayoutDefinition 数据决定"）。
    /// <para>
    /// 契约缺口（判断记录）：架构与既有契约集合里没有一个只读宿主暴露"槽位 → 技能 id"这份绑定
    /// （10_存档与持久化.md 第 2.2 节 <c>player.skill_bindings: Map&lt;String, Id&gt;</c> 只说明它
    /// 存在于存档段，未给出运行期查询接口）。本视图模型因此把槽位绑定表达为构造期注入的
    /// <see cref="_slotBindingResolver"/> 回调，由调用方（游戏层组装代码，持有存档段/自定义绑定
    /// 管理器）提供，不在本模块内新造一个绑定宿主契约（超出本任务改动范围：不得新增 core/ 契约）。
    /// </para>
    /// </summary>
    public sealed class ActionBarViewModel : IDisposable
    {
        private readonly IUiDataSource _dataSource;
        private readonly Func<int, Id?> _slotBindingResolver;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private ActionBarSlotSnapshot[] _slots;

        /// <summary>本视图模型绑定的玩家单位 id（见 <see cref="HudViewModel.PlayerId"/> 同款判断
        /// 记录：查询本身已经经 <see cref="IUiDataSource"/> 的 <c>player.*</c> 路径隐式绑定）。</summary>
        public Id PlayerId { get; }

        /// <summary>槽位数量，来自 <c>ui_layout_definition</c>（panel=action_bar）的 <c>slots</c>
        /// 字段（见 <see cref="UiLayoutDefinition.Slots"/>）。</summary>
        public int SlotCount { get; }

        public IReadOnlyList<ActionBarSlotSnapshot> Slots => _slots;

        public ActionBarViewModel(IUiDataSource dataSource, Id playerId, int slotCount, Func<int, Id?> slotBindingResolver)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            PlayerId = playerId;
            if (slotCount < 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            SlotCount = slotCount;
            _slotBindingResolver = slotBindingResolver ?? throw new ArgumentNullException(nameof(slotBindingResolver));
            _slots = new ActionBarSlotSnapshot[slotCount];

            _subscriptions.Add(_dataSource.Subscribe(RulesEventKeys.SkillCastStart, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(RulesEventKeys.SkillCastSuccess, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(RulesEventKeys.SkillCastFailed, OnRelevantEvent));

            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        public void Refresh()
        {
            var next = new ActionBarSlotSnapshot[SlotCount];
            for (var i = 0; i < SlotCount; i++)
            {
                var skillId = _slotBindingResolver(i);
                if (!skillId.HasValue)
                {
                    next[i] = new ActionBarSlotSnapshot(null, 0);
                    continue;
                }

                var cooldown = _dataSource.Query($"player.skill.{skillId.Value}.cooldown");
                next[i] = new ActionBarSlotSnapshot(skillId, cooldown.HasValue ? cooldown.Value.AsNumber : 0);
            }

            _slots = next;
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
