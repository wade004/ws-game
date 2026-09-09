using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
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
    /// 判断记录（缺口 4 恢复，取代此前"注入 <see cref="Func{Int32, Nullable}"/> 回调"的搁置）：
    /// 槽位绑定改由构造期注入的 <see cref="ISkillBindingHost"/>（<c>core/carriers/unit</c>，G1 新增，
    /// 见 <c>PresentationAssembly</c> 接线处判断记录）提供，不再靠调用方自备一份平行的绑定管理器；
    /// 槽位键固定为 <c>"slot_&lt;i&gt;"</c>（与 <see cref="ISkillBindingHost"/> 类型注释"约定"一致）。
    /// 订阅 <c>unit.skill_binding_changed</c>（<see cref="CarriersEventKeys.UnitSkillBindingChanged"/>）
    /// 与既有三个施法事件一起触发 <see cref="Refresh"/>，绑定变化（如技能书面板拖放绑定）后动作条
    /// 立即反映。
    /// </para>
    /// </summary>
    public sealed class ActionBarViewModel : IDisposable
    {
        private readonly IUiDataSource _dataSource;
        private readonly ISkillBindingHost _skillBindings;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private ActionBarSlotSnapshot[] _slots;

        /// <summary>本视图模型绑定的玩家单位 id（见 <see cref="HudViewModel.PlayerId"/> 同款判断
        /// 记录：查询本身已经经 <see cref="IUiDataSource"/> 的 <c>player.*</c> 路径隐式绑定）。</summary>
        public Id PlayerId { get; }

        /// <summary>槽位数量，来自 <c>ui_layout_definition</c>（panel=action_bar）的 <c>slots</c>
        /// 字段（见 <see cref="UiLayoutDefinition.Slots"/>）。</summary>
        public int SlotCount { get; }

        public IReadOnlyList<ActionBarSlotSnapshot> Slots => _slots;

        /// <summary>槽位序号 → <see cref="ISkillBindingHost"/> 槽位键（见类型注释）。</summary>
        public static string SlotKey(int slot) => $"slot_{slot}";

        public ActionBarViewModel(IUiDataSource dataSource, Id playerId, int slotCount, ISkillBindingHost skillBindings)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            PlayerId = playerId;
            if (slotCount < 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            SlotCount = slotCount;
            _skillBindings = skillBindings ?? throw new ArgumentNullException(nameof(skillBindings));
            _slots = new ActionBarSlotSnapshot[slotCount];

            _subscriptions.Add(_dataSource.Subscribe(RulesEventKeys.SkillCastStart, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(RulesEventKeys.SkillCastSuccess, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(RulesEventKeys.SkillCastFailed, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(CarriersEventKeys.UnitSkillBindingChanged, OnRelevantEvent));
            // UI-111-01 根治同惯例（见 InventoryViewModel 类型注释）：同图读档的抑制作用域会连带
            // 压住施法/绑定事件本身，只有在该作用域外正常派发的 save.loaded 能保证读档后整体重建。
            _subscriptions.Add(_dataSource.Subscribe(SaveEventKeys.SaveLoaded, OnRelevantEvent));

            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        public void Refresh()
        {
            var bindings = _skillBindings.GetBindings(PlayerId);
            var next = new ActionBarSlotSnapshot[SlotCount];
            for (var i = 0; i < SlotCount; i++)
            {
                if (!bindings.TryGetValue(SlotKey(i), out var skillId))
                {
                    next[i] = new ActionBarSlotSnapshot(null, 0);
                    continue;
                }

                var cooldown = _dataSource.Query($"player.skill.{skillId}.cooldown");
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
