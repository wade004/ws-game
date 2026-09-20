using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Presentation.Ui
{
    /// <summary>
    /// 消费方反馈第三批第 2 条（2026-09-21，[ADR-0057](../../../architecture/adr/0057-动作条槽位补冷却总时长充能与结构化不可用原因.md)）：
    /// 某槽位技能"此刻若施放会被什么挡下"的结构化原因——只覆盖 <see cref="SkillReadiness.BlockingSources"/>
    /// 已经裁决的范围（技能自身/分类冷却、充能、公共冷却、使用条件、节拍锁；不含存活/控制、学派锁定、
    /// 资源、目标合法性、距离与视线，见该类型文档"覆盖范围"）。枚举取值与
    /// <see cref="SkillReadinessBlockers"/> 逐项对应，是把该 <c>[Flags]</c> 位组合按确定性优先级
    /// 折叠成的单一值（同一时刻可能有多个阻塞位同时置位，见下方 <c>ResolveBlockReason</c> 判断记录）。
    /// </summary>
    public enum ActionBarSlotBlockReason
    {
        /// <summary>不受任何已覆盖原因阻塞（等价 <see cref="SkillReadinessBlockers.None"/>）。槽位为
        /// 空、或未使用携带 <see cref="SkillReadiness"/> 的新构造函数重载时同样是本值——不代表"已确认
        /// 可用"，只代表"本视图模型未发现阻塞"，见 <see cref="ActionBarSlotSnapshot.Available"/> 的
        /// 既有窄口径。</summary>
        None = 0,

        /// <summary>技能使用条件（<c>skill.def.use_condition</c>）求值为假（对应
        /// <see cref="SkillReadinessBlockers.ConditionNotMet"/>）。</summary>
        ConditionNotMet = 1,

        /// <summary>技能自身冷却剩余 &gt; 0（对应 <see cref="SkillReadinessBlockers.SkillCooldown"/>）。</summary>
        SkillCooldown = 2,

        /// <summary>所属冷却分类剩余 &gt; 0（对应 <see cref="SkillReadinessBlockers.CategoryCooldown"/>）。</summary>
        CategoryCooldown = 3,

        /// <summary>配置了充能且当前充能数为 0（对应 <see cref="SkillReadinessBlockers.NoCharges"/>）。</summary>
        NoCharges = 4,

        /// <summary>公共冷却（GCD）剩余 &gt; 0（对应 <see cref="SkillReadinessBlockers.GlobalCooldown"/>）。</summary>
        GlobalCooldown = 5,

        /// <summary>节拍锁——施法者正处于另一个技能的动作时长内（对应
        /// <see cref="SkillReadinessBlockers.ActionLocked"/>）。</summary>
        ActionLocked = 6,

        /// <summary>取不到完整的技能就绪数据（<see cref="SkillReadiness.EffectiveCooldownDuration"/>
        /// 为 <c>null</c>，即命中了 <see cref="ISkillBookQuery.GetSkillReadiness"/> 的降级默认接口
        /// 实现，而不是生产宿主的真实转发）——与"确认无阻塞"（<see cref="None"/>）刻意区分，不能用
        /// <see cref="None"/> 冒充，见 <see cref="ActionBarViewModel"/> 判断记录"运行时不静默降级"。
        /// 出现本值时 <see cref="ActionBarSlotSnapshot.EffectiveCooldownDuration"/>/
        /// <see cref="ActionBarSlotSnapshot.MaxCharges"/>/<see cref="ActionBarSlotSnapshot.CurrentCharges"/>
        /// 同时为 <c>null</c>，且 <see cref="ActionBarViewModel"/> 已经向注入的 <see cref="IUiDiagnostics"/>
        /// 记过一条警告。</summary>
        Unknown = 7,
    }

    /// <summary>一个动作条槽位的快照。</summary>
    public readonly struct ActionBarSlotSnapshot
    {
        /// <summary>绑定的技能 id；槽位为空时为 null。</summary>
        public Id? SkillId { get; }

        public double Cooldown { get; }

        /// <summary>消费方反馈第 3 条（2026-09-20，ADR-0048）：绑定技能的 <c>skill.def.name_key</c>
        /// （见 <see cref="ISkillBookQuery.GetNameKey"/>）；槽位为空或技能未声明该字段时为 <c>null</c>，
        /// 表现层据此不渲染名称，不回退占位文案（同 <c>greeting_key</c> 判断记录）。</summary>
        public Id? NameKey { get; }

        /// <summary>
        /// 消费方反馈第三批第 2 条（2026-09-21，ADR-0057）：修饰后的完整冷却/充能恢复周期总时长，
        /// 与 <see cref="Cooldown"/>（冷却剩余）同一口径——转发
        /// <see cref="SkillReadiness.EffectiveCooldownDuration"/>，供接入方用
        /// <c>remaining / total</c> 画径向冷却遮罩（消费方原文用法）。<c>null</c> 表示"未知"：槽位
        /// 为空、未使用携带 <see cref="ISkillBookQuery"/>+<see cref="IUiDiagnostics"/> 的构造函数
        /// 重载、或取不到完整就绪数据（见 <see cref="BlockReason"/> == <see cref="ActionBarSlotBlockReason.Unknown"/>）
        /// 三种情形——与 <see cref="SkillReadiness.EffectiveCooldownDuration"/> 本身"未知"的可空语义
        /// 一致，不额外发明 0 这个哨兵值（0 是合法的真实总时长，例如某些设计上"无冷却"的技能）。
        /// </summary>
        public double? EffectiveCooldownDuration { get; }

        /// <summary>
        /// 消费方反馈第三批第 2 条（2026-09-21，ADR-0057）：修饰后有效充能上限，转发
        /// <see cref="SkillReadiness.MaxCharges"/>。技能未配置 <c>charges</c>、槽位为空、未使用新构造
        /// 函数重载、或就绪数据不可用时为 <c>null</c>——"该槽位不使用充能"这一表现（消费方要求"缺失时
        /// 该槽位表现为不使用充能，与改动前逐位一致"）与"充能数未知"共用 <c>null</c>，两者对接入方而言
        /// 都是"不画充能层数 UI"，不需要额外区分。
        /// </summary>
        public int? MaxCharges { get; }

        /// <summary>消费方反馈第三批第 2 条（2026-09-21，ADR-0057）：当前充能数，转发
        /// <see cref="SkillReadiness.CurrentCharges"/>；<c>null</c> 语义同 <see cref="MaxCharges"/>。</summary>
        public int? CurrentCharges { get; }

        /// <summary>
        /// 消费方反馈第三批第 2 条（2026-09-21，ADR-0057）：结构化的"此刻不可用原因"，取值集合与
        /// 推导依据见 <see cref="ActionBarSlotBlockReason"/> 类型文档；多个阻塞原因同时成立时的
        /// 优先级见 <see cref="ActionBarViewModel"/> 判断记录 <c>ResolveBlockReason</c>。默认
        /// <see cref="ActionBarSlotBlockReason.None"/>（既有构造函数重载、空槽位、未启用新能力时的
        /// 缺省值，与 <see cref="Available"/> 既有默认行为一致，不产生看似"确认可用"但其实未判定的
        /// 错觉——本字段与 <see cref="Available"/> 是两套独立、互不改写彼此计算方式的字段，见
        /// <see cref="Available"/> 判断记录）。
        /// </summary>
        public ActionBarSlotBlockReason BlockReason { get; }

        /// <summary>是否可用（有绑定技能且冷却已就绪；不含资源是否足够——资源检查属于施法结算，
        /// 见 06 第 3.6 节，本视图模型只做只读展示不重复该判定）。
        /// <para>
        /// 判断记录（ADR-0057，本字段计算方式保持改动前逐字节不变）：本属性的既有窄口径——只看
        /// <see cref="Cooldown"/>——刻意不随本次新增的 <see cref="BlockReason"/> 而扩大到"不受公共
        /// 冷却/充能/使用条件/节拍锁阻塞"，避免既有消费方（沿用旧字面意思"技能自身冷却已就绪"）行为
        /// 被悄悄改变。需要完整判定的新接入方应改用 <c>BlockReason == ActionBarSlotBlockReason.None</c>
        /// （且 <see cref="SkillId"/> 有值），本属性保留只是向后兼容窄口径。
        /// </para>
        /// </summary>
        public bool Available => SkillId.HasValue && Cooldown <= 0;

        public ActionBarSlotSnapshot(Id? skillId, double cooldown)
        {
            SkillId = skillId;
            Cooldown = cooldown;
            NameKey = null;
            EffectiveCooldownDuration = null;
            MaxCharges = null;
            CurrentCharges = null;
            BlockReason = ActionBarSlotBlockReason.None;
        }

        /// <summary>消费方反馈第 3 条新增重载（2026-09-20，ADR-0048）：携带 <see cref="NameKey"/>。
        /// 判断记录：既有两参数构造函数保持字节级不变（ABI 兼容），本重载三个参数不与其重叠。</summary>
        public ActionBarSlotSnapshot(Id? skillId, double cooldown, Id? nameKey)
        {
            SkillId = skillId;
            Cooldown = cooldown;
            NameKey = nameKey;
            EffectiveCooldownDuration = null;
            MaxCharges = null;
            CurrentCharges = null;
            BlockReason = ActionBarSlotBlockReason.None;
        }

        /// <summary>消费方反馈第三批第 2 条新增重载（2026-09-21，ADR-0057）：携带完整就绪数据。
        /// 判断记录：纯加法，既有两个构造函数重载保持字节级不变。</summary>
        public ActionBarSlotSnapshot(
            Id? skillId, double cooldown, Id? nameKey,
            double? effectiveCooldownDuration, int? maxCharges, int? currentCharges,
            ActionBarSlotBlockReason blockReason)
        {
            SkillId = skillId;
            Cooldown = cooldown;
            NameKey = nameKey;
            EffectiveCooldownDuration = effectiveCooldownDuration;
            MaxCharges = maxCharges;
            CurrentCharges = currentCharges;
            BlockReason = blockReason;
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
        private readonly ISkillBookQuery? _skillCatalog;
        private readonly IUiDiagnostics? _diagnostics;
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

        /// <summary>消费方反馈第 3 条新增重载（2026-09-20，ADR-0048）：携带 <see cref="ISkillBookQuery"/>
        /// 以解析 <see cref="ActionBarSlotSnapshot.NameKey"/>。判断记录（不改既有四参数构造函数）：
        /// 同类型注释"缺口 4 恢复"既有惯例——新增能力走新增构造函数重载，不给既有调用方强加新依赖；
        /// 接受本重载末尾多一次 <see cref="Refresh"/>（构造期同步刷新一次）的轻微效率损耗。</summary>
        public ActionBarViewModel(IUiDataSource dataSource, Id playerId, int slotCount, ISkillBindingHost skillBindings, ISkillBookQuery skillCatalog)
            : this(dataSource, playerId, slotCount, skillBindings)
        {
            _skillCatalog = skillCatalog ?? throw new ArgumentNullException(nameof(skillCatalog));
            Refresh();
        }

        /// <summary>
        /// 消费方反馈第三批第 2 条新增重载（2026-09-21，ADR-0057）：额外携带 <see cref="IUiDiagnostics"/>，
        /// 启用 <see cref="ActionBarSlotSnapshot.EffectiveCooldownDuration"/>/<c>MaxCharges</c>/
        /// <c>CurrentCharges</c>/<c>BlockReason</c> 四个新字段——<see cref="Refresh"/> 据此改为额外调用
        /// <see cref="ISkillBookQuery.GetSkillReadiness"/>（取代裸查 <c>player.skill.&lt;id&gt;.cooldown</c>
        /// 的既有 <see cref="Cooldown"/> 计算方式保持不变，只是新增取值，不重复/替换）。
        /// <para>
        /// 判断记录（为什么新字段绑定诊断出口、不与携带 <see cref="ISkillBookQuery"/> 的既有五参数
        /// 构造函数合并）：AGENTS.md §3"运行时路径不静默降级"——取不到完整就绪数据（<see
        /// cref="ISkillBookQuery.GetSkillReadiness"/> 落到降级默认接口实现）时必须走诊断出口告警，
        /// 不能悄悄返回一个"看起来正常"的值（例如把总时长填 0，容易被误读成"技能声明了 0 秒冷却"而
        /// 不是"取不到数据"）。既有五参数构造函数的既有调用方没有传过 <see cref="IUiDiagnostics"/>，
        /// 给它们凭空造一个诊断出口于事无补（它们仍然没有配置真正的诊断转发目的地）；新增本重载让
        /// "启用新字段"与"提供诊断出口"绑定成同一个显式选择，调用方不传本重载就保持改动前的行为
        /// （新字段恒为缺省值，见 <see cref="ActionBarSlotSnapshot"/> 三参数构造函数），不会有调用方
        /// 在没打算处理诊断的情况下意外获得不完整/构造不出的新字段。
        /// </para>
        /// </summary>
        public ActionBarViewModel(
            IUiDataSource dataSource, Id playerId, int slotCount, ISkillBindingHost skillBindings,
            ISkillBookQuery skillCatalog, IUiDiagnostics diagnostics)
            : this(dataSource, playerId, slotCount, skillBindings, skillCatalog)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        /// <summary>
        /// 消费方反馈第三批第 2 条（2026-09-21，ADR-0057）：把 <see cref="SkillReadiness.BlockingSources"/>
        /// 这个 <c>[Flags]</c> 位组合折叠成单一 <see cref="ActionBarSlotBlockReason"/>，多个原因同时
        /// 成立时按显式优先级取一个——不依赖字典/枚举底层数值的迭代顺序（AGENTS.md §3），用一份写死
        /// 的 if-链逐项判定。
        /// <para>
        /// 判断记录（优先级依据）：顺序对齐 06 施法管线的步骤先后（<c>core/rules/skill/core/
        /// CastPipeline.cs</c>/<c>SkillHost.GetSkillReadiness</c> 判断记录引用的步骤号）——
        /// <see cref="ActionBarSlotBlockReason.ConditionNotMet"/> 是步骤 1.5（使用条件），发生在冷却/
        /// 公共冷却判定之前；<see cref="ActionBarSlotBlockReason.SkillCooldown"/>/<c>CategoryCooldown</c>/
        /// <c>NoCharges</c> 是步骤 3（冷却/充能），其中 <c>SkillCooldown</c> 与 <c>CategoryCooldown</c>
        /// 理论上可同时置位（<c>CooldownTracker.IsSkillReady</c> 先查技能自身冷却、再查分类冷却，
        /// 本优先级取同一顺序）、<c>NoCharges</c> 与前两者互斥（有充能配置的技能从不检查自身/分类
        /// 冷却，见 <see cref="SkillReadinessBlockers.NoCharges"/> 判断记录，三者不会同时置位，相对
        /// 先后顺序因此不影响实际取值）；<see cref="ActionBarSlotBlockReason.GlobalCooldown"/>/
        /// <c>ActionLocked</c> 是步骤 4（公共冷却/节拍锁，两者互斥，见 <see
        /// cref="SkillReadinessBlockers.ActionLocked"/> 判断记录"两者互斥"）。这一顺序如实反映"若此刻
        /// 真的调用 <c>CastSkill</c>，管线会先在哪一步短路"，不是凭空排列。
        /// </para>
        /// </summary>
        private static ActionBarSlotBlockReason ResolveBlockReason(SkillReadinessBlockers blocking)
        {
            if ((blocking & SkillReadinessBlockers.ConditionNotMet) != 0) return ActionBarSlotBlockReason.ConditionNotMet;
            if ((blocking & SkillReadinessBlockers.SkillCooldown) != 0) return ActionBarSlotBlockReason.SkillCooldown;
            if ((blocking & SkillReadinessBlockers.CategoryCooldown) != 0) return ActionBarSlotBlockReason.CategoryCooldown;
            if ((blocking & SkillReadinessBlockers.NoCharges) != 0) return ActionBarSlotBlockReason.NoCharges;
            if ((blocking & SkillReadinessBlockers.GlobalCooldown) != 0) return ActionBarSlotBlockReason.GlobalCooldown;
            if ((blocking & SkillReadinessBlockers.ActionLocked) != 0) return ActionBarSlotBlockReason.ActionLocked;
            return ActionBarSlotBlockReason.None;
        }

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
                var nameKey = _skillCatalog?.GetNameKey(skillId);
                var cooldownValue = cooldown.HasValue ? cooldown.Value.AsNumber : 0;

                if (_skillCatalog != null && _diagnostics != null)
                {
                    var readiness = _skillCatalog.GetSkillReadiness(PlayerId, skillId);
                    if (readiness.EffectiveCooldownDuration.HasValue)
                    {
                        next[i] = new ActionBarSlotSnapshot(
                            skillId, cooldownValue, nameKey,
                            readiness.EffectiveCooldownDuration, readiness.MaxCharges, readiness.CurrentCharges,
                            ResolveBlockReason(readiness.BlockingSources));
                        continue;
                    }

                    // 运行时不静默降级（AGENTS.md §3）：EffectiveCooldownDuration 为 null 说明本次查询
                    // 落到了 ISkillBookQuery.GetSkillReadiness 的降级默认接口实现（生产宿主的真实转发
                    // 恒返回非 null 的总时长，见该方法判断记录），不是"这个技能真的没有冷却/充能"。
                    _diagnostics.Warn(
                        $"动作条槽位 {i}（技能 {skillId}）取不到完整的技能就绪数据（ISkillBookQuery." +
                        "GetSkillReadiness 落到降级默认实现）：冷却总时长/充能层数/不可用原因均标记为未知，" +
                        "不当作\"无阻塞\"处理。");
                    next[i] = new ActionBarSlotSnapshot(
                        skillId, cooldownValue, nameKey,
                        null, null, null, ActionBarSlotBlockReason.Unknown);
                    continue;
                }

                next[i] = new ActionBarSlotSnapshot(skillId, cooldownValue, nameKey);
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
