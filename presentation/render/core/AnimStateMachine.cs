using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Presentation.Render
{
    /// <summary>
    /// <see cref="AnimState"/> 七态状态机的引擎无关实现（见 09_表现层.md 第 4.2 节"状态切换由表现层
    /// 依据订阅到的事件与只读运动状态自行决定，不由逻辑层直接下发……播放哪个动画"）。本类型只回答
    /// "当前应该处于哪个 <see cref="AnimState"/>"，不知道也不关心具体该播放哪个动画剪辑——那是
    /// <see cref="ICharacterRig"/>/<see cref="IFrameAnimPlayer"/> 的职责（见 09 第 4.1 节 CharacterRig
    /// 职责表"动画状态机驱动"一行）。
    /// <para>
    /// 铁律遵守：只经 <see cref="IEventBus.Subscribe{T}"/> 订阅事件（P2），不持有任何逻辑层写入能力
    /// （P1/P3），本类型自身不做任何绘制/播放调用（P4）——只暴露 <see cref="StateChanged"/> 供
    /// <see cref="ICharacterRig"/> 一类消费方驱动实际表现。
    /// </para>
    /// <para>
    /// 判断记录（事件→状态映射，09 未给出具体事件词汇表对照，本类型按 06 第 8 节事件词汇表逐条判断）：
    /// <list type="bullet">
    /// <item><b>移动（idle ⇄ move）</b>：不用 <c>unit.moved</c>（只携带位置增量，见
    /// <see cref="Core.Carriers.Common.UnitMovedEvent"/>，无法区分"正在移动"与"瞬移"），改用
    /// <c>unit.state_changed</c>（<see cref="Core.Carriers.Common.UnitStateChangedEvent"/>）——该事件
    /// key 同时被 <c>core/carriers/unit.MovementTickHandler</c>（携带 <c>MoveMode</c> 的
    /// <c>ToString()</c>：<c>"Idle"</c>/<c>"Walk"</c>/<c>"Run"</c>/<c>"Forced"</c>，帕斯卡命名）与
    /// <c>core/rules/common</c> 的 AI 行为状态机（携带 <c>idle</c>/<c>patrol</c>/<c>chase</c>/
    /// <c>combat</c>/<c>return</c>/<c>flee</c>，全小写，见 06 第 5 节 AI 状态表）两种调用方复用（见
    /// <see cref="UnitStateChangedEvent"/> 类型注释"是同一个事件 key 在不同调用方下的不同用法之一"）；
    /// 两套取值大小写不重叠，本类型只精确匹配移动模式的四个帕斯卡命名取值，其余（含 AI 状态机取值）
    /// 一律忽略，不产生误判。</item>
    /// <item><b>attack / cast</b>：<c>skill.cast_start</c>（<see cref="SkillCastStartEvent"/>）按
    /// <c>castTime == 0</c> 判定为瞬发（<see cref="AnimState.Attack"/>，覆盖瞬发技能，09 第 4.2
    /// 节"attack 与 cast 的具体动作剪辑由武器表现档案决定"未强制区分二者的判据，本类型选取"是否读条"
    /// 这一逻辑层已有字段作为判据）；<c>castTime > 0</c> 判定为 <see cref="AnimState.Cast"/>。普通攻击
    /// （框架原生一等执行路径，ADR-0059）刻意不走施法管线，不发 <c>skill.cast_start</c>——改由
    /// <c>combat.auto_attack_swing</c>（<see cref="AutoAttackSwingEvent"/>，ADR-0070）单独驱动
    /// <see cref="AnimState.Attack"/>（见 <see cref="OnAutoAttackSwing"/>），与瞬发技能共用同一个
    /// <see cref="AnimState.Attack"/> 状态值、同一套优先级/瞬态重触发规则，只是触发来源不同的事件。三个
    /// 收尾事件 <c>skill.cast_success</c>/<c>skill.cast_failed</c>/<c>skill.cast_interrupted</c>
    /// 只驱动 <see cref="AnimState.Cast"/> 回落（读条/引导的视觉时长本就等于逻辑层的
    /// <c>cast_time</c>/<c>channel_time</c>，二者天然同步）；<see cref="AnimState.Attack"/>（瞬发）
    /// 不在这三个事件里回落——<c>skill.cast_start</c>/<c>skill.cast_success</c> 在瞬发时同一派发
    /// 批次内背靠背发出，若靠事件收尾会让 Attack 播放形态在同一帧内被切回，动画播不出来（N19 根治，
    /// architecture/落地计划/audit-68c9bed-20260907/code-review.md），改由 <see
    /// cref="NotifyTransientStateFinished"/> 独家驱动，见 <see cref="OnSkillCastEnd"/> 判断记录。</item>
    /// <item><b>hit</b>：<c>combat.damage_dealt</c>（<see cref="CombatDamageDealtEvent"/>）的
    /// <c>targetId</c> 匹配本实体时触发；本类型不知道受击动画播多久，回落时机改由消费方在受击动画
    /// 播放完成后调用 <see cref="NotifyTransientStateFinished"/> 显式通知（同 <see cref="AnimState.Jump"/>
    /// 判断记录，见下）。</item>
    /// <item><b>death</b>：<c>unit.died</c>（<see cref="UnitDiedEvent"/>）触发，终态——本类型对已进入
    /// Death 的实体后续全部事件不再处理（见 <see cref="IsTerminal"/>），与 09 第 1 节"表现层是唯一
    /// 看得见的一层"无关，纯粹是"死亡后不应该再切回 idle/move"的显而易见约束。</item>
    /// <item><b>战斗姿态（ADR-0111）</b>：<c>combat.entered</c>/<c>combat.left</c>（
    /// <see cref="CombatEnteredEvent"/>/<see cref="CombatLeftEvent"/>，逐单位发布）驱动一个与
    /// <see cref="AnimState"/> <b>正交</b>的布尔维度（<see cref="IsInCombatStance"/>/
    /// <see cref="CombatStanceChanged"/>），<b>不新增 <see cref="AnimState"/> 成员</b>——新增成员会波及
    /// 所有按 Idle/Move 判断运动态的代码与消费方的穷举分支，而正交维度对全部状态一视同仁，将来"战斗中
    /// 移动/受击"用另一套剪辑不需要再改契约。本类型只维护这个布尔值，不切换状态、不选剪辑；姿态变化时
    /// 瞬态状态不被打断，其回落（<see cref="RevertToLocomotion"/>）触发的 <see cref="StateChanged"/> 由
    /// 订阅方按当时姿态解析剪辑；Death 终态不受影响。</item>
    /// <item><b>jump</b>：06 第 8 节事件词汇表当前没有 <c>unit.jumped</c>/<c>unit.landed</c> 一类事件
    /// （跳跃目前只体现为 05 第 3 节的逻辑高度值，读取该值需要按帧轮询 <c>ISimSnapshot.GetHeight</c>，
    /// 会让本模块反过来依赖 <c>presentation/view_binding</c>，与 <c>presentation/camera</c> 模块"同层
    /// 不产生编译期依赖"的既有判断记录矛盾，见 <c>presentation/camera/README.md</c>）——本版本不做
    /// 按帧高度轮询，改为暴露 <see cref="RequestOverride"/> 扩展点，供具体游戏在自己的跳跃实现（技能
    /// 效果/移动扩展）里主动调用触发 Jump 显示；这是已知简化，留待事件词汇表补齐专门的跳跃事件后再
    /// 收口为事件驱动，见模块 README"契约缺口"。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 判断记录（受击反应驱动，ADR-0147，手感设计/03 第 4.5 节）：构造时带 <see cref="IHitReactionQuery"/> 的重载进入<b>反应驱动模式</b>
    /// （世界装配了手感受击裁决时由装配根传入；缺省不传，行为与此前逐位一致）。该模式下受击不再由 <c>combat.damage_dealt</c> 触发——
    /// 伤害落地不等于有受击反应（霸体、回避、反应为 none 不播任何受击动画）——改由裁决事件驱动：<c>combat.reaction_applied</c>
    /// （<c>flinch</c> / <c>stagger_light</c> → <c>hit.light</c>，<c>stagger</c> → <c>hit.heavy</c>，<c>knockback</c> 与 <c>knockdown</c> 硬直段 → <c>hit.knockback</c>）、
    /// <c>unit.knocked_down</c>（→ <c>hit.knockdown</c>）、<c>unit.getup_started</c>（→ <c>hit.getup</c>）、
    /// <c>combat.hit_confirmed</c> 的 <c>Block</c>（→ <c>hit.block</c>）。当前子键经 <see cref="GetHitPoseSub"/> 给解析方，状态仍是 <see cref="AnimState.Hit"/>
    /// （状态枚举不增不减，子键只是同一状态的姿势键后缀，缺键沿回落链退到 <c>hit</c>）。有硬直的反应进入"保持"：<see cref="NotifyTransientStateFinished"/>
    /// 对 Hit 的完成回调在硬直未结束时被忽略（剪辑播完停在末帧），每个 <c>sim.tick_finished</c> 查 <see cref="IHitReactionQuery"/>
    /// （硬直中或尚未起算的顿帧期，<c>RemainingStaggerTicks</c> &gt; 0），硬直（含倒地、起身、空中硬直直到落地）结束的那个 tick 回落到运动态——
    /// 与逻辑层的硬直时钟同源（顿帧期间自然不走），不在表现层自己数 tick。判定规则：①<c>flinch</c> 不打断动作——处于
    /// Attack/Cast 时忽略（逻辑层语义"不打断"），处于硬直保持中也忽略（不把倒地姿势换成轻抖动）；②有硬直的反应只在它刷新了硬直记录时生效
    /// （<c>RemainingStaggerTicks</c> 不大于事件的 <c>durationTicks</c>，较弱的反应打在更长的硬直上不换姿势）；③格挡抖动同样不进入动作与保持，
    /// 且被格挡命中随后的 <c>flinch</c> 反应不再重播一次轻抖动。
    /// </para>
    /// <para>
    /// 判断记录（优先级/打断规则，09 原文只说"由 09 判断优先级"但未给出具体表，本类型按下表拍板并
    /// 记录）：数值越大优先级越高；<see cref="AnimState.Idle"/>/<see cref="AnimState.Move"/>（合称
    /// "运动态"，互斥、总是可以互相覆盖）优先级 0，<see cref="AnimState.Jump"/> 优先级 1，
    /// <see cref="AnimState.Attack"/>/<see cref="AnimState.Cast"/>（合称"动作态"）优先级 2，
    /// <see cref="AnimState.Hit"/> 优先级 3，<see cref="AnimState.Death"/> 优先级 4（终态）。"开始"类
    /// 事件（attack/cast/hit/jump 的触发事件、death）只在自身优先级 ≥ 当前状态优先级时才切换（保证
    /// 受击不会被一个正在进行的普攻覆盖，但可以打断普攻——普攻与受击若要严格互斥需要逻辑层显式发
    /// <c>skill.cast_interrupted</c>，本状态机不代为决定"该不该打断"，只按优先级表决定"视觉上谁盖过
    /// 谁"）；运动态更新（<c>unit.state_changed</c>）永远记录到 <see cref="_locomotion"/>，但只有当前
    /// 状态优先级 ≤ 0（即当前本就是运动态）时才立即生效，否则等到"回落"事件把状态收回运动态时才用得
    /// 上最新记录的运动态（见 <see cref="RevertToLocomotion"/>）。"回落"类操作（
    /// <see cref="NotifyTransientStateFinished"/>、施法收尾事件）只在当前状态确实等于被回落的具体状态
    /// 时才生效，避免过期的回落信号误伤后来居上的新状态（例如受击回落信号在受击后又立刻被死亡覆盖的
    /// 场景下到达，此时不应该把状态从 Death 拉回运动态）。
    /// </para>
    /// </summary>
    public sealed class AnimStateMachine : IDisposable
    {
        private static readonly IReadOnlyDictionary<AnimState, int> Priority = new Dictionary<AnimState, int>
        {
            [AnimState.Idle] = 0,
            [AnimState.Move] = 0,
            [AnimState.Jump] = 1,
            [AnimState.Attack] = 2,
            [AnimState.Cast] = 2,
            [AnimState.Hit] = 3,
            [AnimState.Death] = 4,
        };

        private sealed class Entry
        {
            public AnimState Current = AnimState.Idle;
            public AnimState Locomotion = AnimState.Idle;
            public bool InCombat;

            // ADR-0147：反应驱动模式下当前受击姿势的子键（null = 基础 hit）与硬直保持标志。
            public string? HitSub;
            public bool Hold;
        }

        private readonly Dictionary<Id, Entry> _entities = new Dictionary<Id, Entry>();
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();

        /// <summary>状态发生变化时触发（entityId, from, to）；同一状态"重复进入"不触发（见各
        /// <c>TryEnter</c>/<c>RevertToLocomotion</c> 调用点的相等性检查）。</summary>
        public event Action<Id, AnimState, AnimState>? StateChanged;

        /// <summary>
        /// ADR-0017 决策 c 新增：与 <see cref="StateChanged"/> 同一次切换背靠背触发，额外携带触发这次
        /// 切换的技能 id——支持 09 第 4.4 节 <c>cast_anim_override</c>（按技能 id 覆盖施法动作剪辑）：
        /// <see cref="StateChanged"/> 的既有三元组签名（<c>entityId, from, to</c>）不携带技能 id（见
        /// 04/W3b 既有判断记录"AnimStateMachine.StateChanged 事件只携带三元组，不携带触发这次切换的
        /// 技能 id"），本事件是"新增重载"的落地形状——C# 事件委托签名固定，不能给既有委托追加参数，
        /// 因此另起一个事件而不是改 <see cref="StateChanged"/> 本身，保持既有订阅方（如 W3b
        /// <c>AnimClipResolver</c>）不必跟着改签名也能继续编译通过。<c>triggerSkillId</c> 只在由
        /// <see cref="OnSkillCastStart"/>（<c>skill.cast_start</c>）驱动的切换（进入
        /// <see cref="AnimState.Attack"/>/<see cref="AnimState.Cast"/>）时非空；其余全部切换来源
        /// （移动、受击、死亡、<see cref="RequestOverride"/>、回落）恒为 null。
        /// </summary>
        public event Action<Id, AnimState, AnimState, Id?>? StateChangedWithSkill;

        /// <summary>
        /// H5b 根治（游戏侧复核发现 2"同状态重入不重播"）：瞬态状态（<see cref="AnimState.Jump"/>/
        /// <see cref="AnimState.Attack"/>/<see cref="AnimState.Cast"/>/<see cref="AnimState.Hit"/>）
        /// 的"开始"类事件（<see cref="TryEnter"/> 的全部调用来源——<see cref="OnSkillCastStart"/>/
        /// <see cref="OnCombatDamageDealt"/>/<see cref="RequestOverride"/>）在目标状态与当前状态相同
        /// 时，此前直接静默返回（同 <see cref="SetState"/> 的"同一状态不重复触发 <see cref="StateChanged"/>"
        /// 幂等约定），代价是"连续两次普攻，第二次在第一次动画播完前到达"这类场景下第二次攻击不会
        /// 重播剪辑（<c>AnimClipResolver</c> 只在 <see cref="StateChangedWithSkill"/> 触发时才调用
        /// <c>playClip</c>，同一状态不切换就不会再调一次）——对 <see cref="AnimState.Idle"/>/
        /// <see cref="AnimState.Move"/> 这两个持续态而言"同状态不重播"是正确的幂等行为（移动方向不变
        /// 不需要每帧重播一遍待机/移动剪辑），但对瞬态态而言，"游戏行为再次发生"（又打了一下、又读了
        /// 一次条、又挨了一下）理应重播一遍完整的剪辑，不能被"状态数值没变"这一巧合掩盖。本事件因此
        /// 单独区分"重触发"（同状态重入）与"切换"（<see cref="StateChanged"/>，状态数值真的变了）
        /// 两种情形，供 <c>AnimClipResolver</c> 一类消费方对两者调用同一套"解析剪辑并播放"逻辑——
        /// 播放器（<c>IFrameAnimPlayer.Play</c>/<c>IRenderer3D.PlayAnim</c>）本身对"再调用一次 Play"
        /// 的语义就是"从头重新播放"（见 <see cref="Presentation.Render.FrameAnimPlayer.Play"/> 判断
        /// 记录"不论是否已在播放同一剪辑，Play 恒重置 <c>_elapsedSeconds</c>/<c>_lastFrame</c>"），
        /// 本状态机只需要在该重播的时机把这次调用转发出去，不需要自己实现任何"重播"机制。
        /// <para>
        /// 只在 <paramref name="state"/>（重入的目标状态）不是 <see cref="AnimState.Idle"/>/
        /// <see cref="AnimState.Move"/> 时触发（见 <see cref="TryEnter"/> 判断记录）——运动态保持
        /// 幂等，不受本事件影响；<see cref="AnimState.Death"/> 终态在 <see cref="TryEnter"/> 更早的
        /// "当前已是 Death"检查里就已经返回，永远不会走到这里，不需要额外排除。
        /// </para>
        /// </summary>
        public event Action<Id, AnimState, Id?>? StateRetriggered;

        /// <summary>
        /// ADR-0111：战斗姿态（"是否处于战斗姿态"，与 <see cref="AnimState"/> 正交的一个布尔维度，
        /// 见 <see cref="IsInCombatStance"/>）发生变化时触发（entityId, inCombat）；姿态没变时不触发。
        /// 状态机本身不切换 <see cref="AnimState"/>、不选剪辑——姿态变化后该不该换剪辑、换哪个，由
        /// 订阅方（<c>AnimClipResolver</c> 一类）按"当前状态 + 新姿态"重新解析决定。
        /// </summary>
        public event Action<Id, bool>? CombatStanceChanged;

        private readonly Func<Id, bool>? _combatProbe;
        private readonly IHitReactionQuery? _reactions;
        private readonly HashSet<Id> _holding = new HashSet<Id>();
        private readonly List<Id> _scratch = new List<Id>();

        public AnimStateMachine(IEventBus bus) : this(bus, null)
        {
        }

        /// <summary>
        /// ADR-0147：是否处于受击反应驱动模式（构造时带了 <see cref="IHitReactionQuery"/>）。是则 Hit 由裁决事件驱动、
        /// 带反应子键（<see cref="GetHitPoseSub"/>）；否则由 <c>combat.damage_dealt</c> 驱动（此前行为）。
        /// </summary>
        public bool IsReactionDriven => _reactions != null;

        /// <summary>
        /// ADR-0147：当前受击姿势的子键（<c>light/heavy/knockback/knockdown/getup/block</c>，见 <see cref="PoseKeys"/>）；
        /// 不在 Hit 状态、非反应驱动模式或无子键时为 null（解析基础键 <c>hit</c>）。
        /// </summary>
        public string? GetHitPoseSub(Id entityId) =>
            _entities.TryGetValue(entityId, out var entry) && entry.Current == AnimState.Hit ? entry.HitSub : null;

        /// <summary>
        /// ADR-0111：带"战斗中探针"的构造重载。<paramref name="combatProbe"/> 是只读查询（"该单位此刻是否
        /// 在战斗中"，生产装配接到规则层的 <c>ICombatHost.IsInCombat</c>），只在<b>首次跟踪某实体</b>时
        /// 调用一次，确定该实体的初始战斗姿态——视图晚于 <c>combat.entered</c> 创建（读档、重生、单位
        /// 进入视野时已在战中）时事件已经错过，没有探针初始姿态会恒为"非战斗"。探针只读，不给表现层任何
        /// 逻辑层写入能力（09 第 1 节铁律不变）；<c>null</c> 时初始姿态一律为非战斗（即
        /// <see cref="AnimStateMachine(IEventBus)"/> 的既有行为）。探针抛出的异常不吞，原样传播（运行时
        /// 路径不静默降级）。
        /// </summary>
        public AnimStateMachine(IEventBus bus, Func<Id, bool>? combatProbe) : this(bus, combatProbe, null)
        {
        }

        /// <summary>
        /// ADR-0147：带受击反应查询的构造重载（<paramref name="reactions"/> 非空即进入反应驱动模式，见类型注释）；为 null 与
        /// <see cref="AnimStateMachine(IEventBus, Func{Id, bool}?)"/> 逐位一致。
        /// </summary>
        public AnimStateMachine(IEventBus bus, Func<Id, bool>? combatProbe, IHitReactionQuery? reactions)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _combatProbe = combatProbe;
            _reactions = reactions;

            _subscriptions.Add(bus.Subscribe<UnitStateChangedEvent>(CarriersEventKeys.UnitStateChanged, OnUnitStateChanged));
            _subscriptions.Add(bus.Subscribe<SkillCastStartEvent>(RulesEventKeys.SkillCastStart, OnSkillCastStart));
            _subscriptions.Add(bus.Subscribe<AutoAttackSwingEvent>(RulesEventKeys.CombatAutoAttackSwing, OnAutoAttackSwing));
            _subscriptions.Add(bus.Subscribe<SkillCastSuccessEvent>(RulesEventKeys.SkillCastSuccess, evt => OnSkillCastEnd(evt.CasterId)));
            _subscriptions.Add(bus.Subscribe<SkillCastFailedEvent>(RulesEventKeys.SkillCastFailed, evt => OnSkillCastEnd(evt.CasterId)));
            _subscriptions.Add(bus.Subscribe<SkillCastInterruptedEvent>(RulesEventKeys.SkillCastInterrupted, evt => OnSkillCastEnd(evt.CasterId)));
            if (_reactions == null)
            {
                _subscriptions.Add(bus.Subscribe<CombatDamageDealtEvent>(RulesEventKeys.CombatDamageDealt, OnCombatDamageDealt));
            }
            else
            {
                // ADR-0147：反应驱动模式——受击由裁决事件驱动（见类型注释），伤害落地本身不再触发 Hit。
                _subscriptions.Add(bus.Subscribe<CombatReactionAppliedEvent>(RulesEventKeys.CombatReactionApplied, OnReactionApplied));
                _subscriptions.Add(bus.Subscribe<UnitKnockedDownEvent>(RulesEventKeys.UnitKnockedDown, evt => OnReactionPhase(evt.UnitId, PoseKeys.HitSubKnockdown)));
                _subscriptions.Add(bus.Subscribe<UnitGetupStartedEvent>(RulesEventKeys.UnitGetupStarted, evt => OnReactionPhase(evt.UnitId, PoseKeys.HitSubGetup)));
                _subscriptions.Add(bus.Subscribe<CombatHitConfirmedEvent>(RulesEventKeys.CombatHitConfirmed, OnHitConfirmedForBlock));
                _subscriptions.Add(bus.Subscribe<SimTickFinishedEvent>(SimEventKeys.TickFinished, _ => ReleaseFinishedHolds()));
            }
            _subscriptions.Add(bus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, evt => TryEnter(evt.UnitId, AnimState.Death)));
            // ADR-0111：combat.entered/combat.left 逐单位携带 unitId（CombatHost 对每个单位各发一次，
            // 见 CombatEnteredEvent/CombatLeftEvent），读档不补发（CombatHost.RestoreCombatState 注释）
            // ——读档/重生后的初始姿态靠构造重载的探针，不靠事件。
            _subscriptions.Add(bus.Subscribe<CombatEnteredEvent>(RulesEventKeys.CombatEntered, evt => SetCombatStance(evt.UnitId, true)));
            _subscriptions.Add(bus.Subscribe<CombatLeftEvent>(RulesEventKeys.CombatLeft, evt => SetCombatStance(evt.UnitId, false)));
        }

        /// <summary>当前状态；未跟踪过的实体默认 <see cref="AnimState.Idle"/>（还没收到任何该实体的
        /// 相关事件，同"新绑定的 View 默认待机姿态"直觉一致）。</summary>
        public AnimState GetState(Id entityId) =>
            _entities.TryGetValue(entityId, out var entry) ? entry.Current : AnimState.Idle;

        /// <summary>是否已进入终态（见类型注释"death 终态"）。</summary>
        public bool IsTerminal(Id entityId) =>
            _entities.TryGetValue(entityId, out var entry) && entry.Current == AnimState.Death;

        /// <summary>
        /// ADR-0111：该实体当前是否处于战斗姿态。只由 <c>combat.entered</c>/<c>combat.left</c> 事件驱动
        /// （首次跟踪时的初始值来自构造重载的探针）；<b>未跟踪过的实体恒为 false</b>（本查询不触发探针、
        /// 不创建跟踪记录）——调用方若需要"视图刚创建时已在战中"的初始姿态，先调用 <see cref="Track"/>。
        /// 姿态与 <see cref="AnimState"/> 正交：任何状态（含 <see cref="AnimState.Death"/>）下都可读。
        /// </summary>
        public bool IsInCombatStance(Id entityId) =>
            _entities.TryGetValue(entityId, out var entry) && entry.InCombat;

        /// <summary>
        /// ADR-0111：显式开始跟踪该实体——首次跟踪时用构造重载的探针确定初始战斗姿态（不触发任何事件，
        /// 这是"初始值"不是"变化"）；已在跟踪则什么都不做。供视图挂接方在视图创建那一刻调用，使此后
        /// 的姿态变化都以"视图已知的姿态"为基线做增量。
        /// </summary>
        public void Track(Id entityId) => GetOrCreate(entityId);

        /// <summary>
        /// 手工触发一次"开始"类切换，走与事件触发同一套优先级判定（见类型注释 jump 判断记录）。不
        /// 接受 <see cref="AnimState.Idle"/>/<see cref="AnimState.Move"/>（运动态只应经
        /// <c>unit.state_changed</c> 驱动，见 <see cref="OnUnitStateChanged"/>）。
        /// <para>
        /// ADR-0070 判断记录（定位澄清：本方法是"游戏专属"表现触发点，不是框架自有动作的驱动方式）：
        /// 本方法的设计定位是供**具体游戏自己拥有、框架不知道的表现触发**（如跳跃——见类型注释
        /// jump 判断记录）主动调用；框架自己拥有的动作（普通攻击/技能施法/受击/死亡）一律由框架在
        /// 对应结算完成时广播事件驱动（分别见 <see cref="OnAutoAttackSwing"/>/<see cref="OnSkillCastStart"/>/
        /// <see cref="OnCombatDamageDealt"/>/构造函数 <c>unit.died</c> 订阅），不应该也不需要由消费方
        /// 改用本方法代劳——框架自己拥有的触发点必须留在框架内部，否则每个消费方各自接线会出现两套
        /// 不一致的口径（同一份"什么时候算一次普通攻击"的判断逻辑被复制到每个游戏各自的接入代码里）。
        /// </para>
        /// </summary>
        public void RequestOverride(Id entityId, AnimState state)
        {
            if (state == AnimState.Idle || state == AnimState.Move)
            {
                throw new ArgumentException(
                    "运动态只能经 unit.state_changed 驱动，不接受 RequestOverride 手工触发", nameof(state));
            }

            TryEnter(entityId, state);
        }

        /// <summary>消费方（<see cref="ICharacterRig"/>/<see cref="IFrameAnimPlayer"/> 的
        /// <c>OnComplete</c> 回调）在一个瞬态状态（<see cref="AnimState.Hit"/>/<see cref="AnimState.Jump"/>
        /// /<see cref="AnimState.Attack"/>/<see cref="AnimState.Cast"/>）对应的动画播放完毕后调用，
        /// 通知本状态机回落到当前运动态；只有 <paramref name="finishedState"/> 与当前状态一致时才生效
        /// （见类型注释"回落类操作"判断记录，避免过期信号误伤新状态）。<see cref="AnimState.Death"/>
        /// 不接受回落（终态）。</summary>
        public void NotifyTransientStateFinished(Id entityId, AnimState finishedState)
        {
            if (finishedState == AnimState.Death)
            {
                return;
            }

            if (_entities.TryGetValue(entityId, out var entry) && entry.Current == finishedState)
            {
                // ADR-0147：硬直保持中，受击剪辑播完不回落（停在末帧），回落由硬直结束驱动。
                if (finishedState == AnimState.Hit && entry.Hold && IsStaggerActive(entityId))
                {
                    return;
                }

                RevertToLocomotion(entityId, entry);
            }
        }

        /// <summary>释放对该实体的跟踪（例如 View 销毁/实体离开场景时由调用方清理，避免字典无限增长）。
        /// 同时清除战斗姿态（ADR-0111）。不触发 <see cref="StateChanged"/>/<see cref="CombatStanceChanged"/>
        /// ——纯粹的簿记清理，不是一次状态切换。</summary>
        public void Forget(Id entityId)
        {
            _entities.Remove(entityId);
            _holding.Remove(entityId);
        }

        public void Dispose()
        {
            foreach (var sub in _subscriptions)
            {
                sub.Dispose();
            }
            _subscriptions.Clear();
            if (_airSource != null)
            {
                _airSource.ContextChanged -= OnAirContextChanged;
                _airSource = null;
            }
        }

        private IPoseContextSource? _airSource;

        /// <summary>
        /// ADR-0130 追加决定（空中姿势）：挂接空中阶段来源（通常是 <c>PoseSelector</c>，由 <c>AirPoseFeeder</c> 喂入阶段）。
        /// 挂接后：①运动态（Idle/Move）的实体腾空（<see cref="AirPhase.Rise"/>/<see cref="AirPhase.Fall"/>）时进入 <see cref="AnimState.Jump"/>；
        /// ②Jump 状态下空中阶段回到 <see cref="AirPhase.None"/>（落地保持窗口结束）时回落到运动态；
        /// ③瞬态（Attack/Cast/Hit）结束时若实体仍在空中，回落到 Jump 而不是运动态。
        /// 不挂接（缺省）时行为与改动前逐位一致——Jump 仍只经 <see cref="RequestOverride"/> 进入。重复挂接替换来源。
        /// </summary>
        public void AttachAirPhaseSource(IPoseContextSource source) => AttachAirPhaseSource(source, subscribe: true);

        /// <summary>
        /// 同 <see cref="AttachAirPhaseSource(IPoseContextSource)"/>，但可选择不订阅来源的 <c>ContextChanged</c>：<paramref name="subscribe"/> 为假时，
        /// 空中阶段的变化由持有者显式调用 <see cref="ApplyAirPhase"/> 驱动（引擎侧由 <c>AnimClipResolver</c> 在它既有的那一个上下文订阅里先调本方法
        /// 再重新解析剪辑——来源上只有一个订阅者，且"先进出 Jump、后解析剪辑"的顺序有保证）。
        /// </summary>
        public void AttachAirPhaseSource(IPoseContextSource source, bool subscribe)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (_airSource != null) _airSource.ContextChanged -= OnAirContextChanged;
            _airSource = source;
            if (subscribe) _airSource.ContextChanged += OnAirContextChanged;
        }

        /// <summary>按来源当前的空中阶段让实体进出 Jump（没有挂空中阶段来源时什么都不做）。</summary>
        public void ApplyAirPhase(Id entityId) => OnAirContextChanged(entityId);

        private void OnAirContextChanged(Id entityId)
        {
            if (_airSource == null) return;
            var air = _airSource.GetContext(entityId).Air;
            var state = GetState(entityId);
            if ((air == AirPhase.Rise || air == AirPhase.Fall) && (state == AnimState.Idle || state == AnimState.Move))
            {
                TryEnter(entityId, AnimState.Jump);
            }
            else if (air == AirPhase.None && state == AnimState.Jump)
            {
                NotifyTransientStateFinished(entityId, AnimState.Jump);
            }
        }

        // ------------------------------------------------------------------

        private void OnUnitStateChanged(UnitStateChangedEvent evt)
        {
            AnimState locomotion;
            switch (evt.NewState)
            {
                case "Idle": locomotion = AnimState.Idle; break;
                case "Walk":
                case "Run":
                case "Sprint": // ADR-0147：冲刺模式，与 Run 同属移动态（步态剪辑由姿势解析按速度档位选）
                case "Forced": locomotion = AnimState.Move; break;
                default: return; // 非移动模式取值（如 AI 行为状态机复用同一事件 key），不属本状态机管辖。
            }

            var entry = GetOrCreate(evt.UnitId);
            if (entry.Current == AnimState.Death)
            {
                return;
            }

            entry.Locomotion = locomotion;
            if (Priority[entry.Current] <= 0)
            {
                SetState(evt.UnitId, entry, locomotion);
            }
        }

        private void OnSkillCastStart(SkillCastStartEvent evt) =>
            TryEnter(evt.CasterId, evt.CastTime <= 0 ? AnimState.Attack : AnimState.Cast, evt.SkillId);

        /// <summary>ADR-0070：普通攻击每结算一次挥击（<c>AutoAttackHost.Update</c>）驱动一次，走与
        /// <see cref="OnSkillCastStart"/> 瞬发分支完全相同的 <see cref="AnimState.Attack"/> 优先级/
        /// 瞬态重触发规则（见 <see cref="TryEnter"/>）——普通攻击是高频重复的瞬态动作，连续多次挥击
        /// （包括上一次 Attack 动画尚未播完时又到点的情形）都能正确重复进入/重触发。不携带
        /// <c>triggerSkillId</c>（<c>AutoAttackSwingEvent</c> 本就不对应任何真实 <c>skill.def</c>，
        /// 见 <see cref="AutoAttackSwingEvent"/> 类型判断记录），<see cref="StateChangedWithSkill"/>/
        /// <see cref="StateRetriggered"/> 对本次触发恒收到 <c>triggerSkillId == null</c>。</summary>
        private void OnAutoAttackSwing(AutoAttackSwingEvent evt) => TryEnter(evt.SourceId, AnimState.Attack);

        /// <summary>
        /// 判断记录（N19 根治，architecture/落地计划/audit-68c9bed-20260907/code-review.md）：
        /// <c>skill.cast_success</c>/<c>skill.cast_failed</c>/<c>skill.cast_interrupted</c> 三个
        /// 收尾事件共用本方法。旧实现对 <see cref="AnimState.Attack"/>/<see cref="AnimState.Cast"/>
        /// 一视同仁，收到即立即回落——但瞬发（<see cref="AnimState.Attack"/>，覆盖普攻与瞬发技能，
        /// 见类型注释）的 <c>skill.cast_start</c>（进入 Attack）与 <c>skill.cast_success</c>（本方法）
        /// 在逻辑层同一次派发批次内背靠背发出（步骤 8 立即完成，不经历任何 tick），若这里立即回落，
        /// Attack 播放形态会在同一帧内又切回 Idle/Move，Attack 动画剪辑根本没有机会真正播出（09
        /// 表现层"逻辑结算与动画播放时长相互独立"这一原则要求二者不能靠同一个逻辑事件同步收尾）。
        /// <para>
        /// 修复：只有 <see cref="AnimState.Cast"/>（真正的读条/引导，视觉时长本就等于
        /// <c>cast_time</c>/<c>channel_time</c>，与逻辑收尾天然同步，收到收尾事件立即回落是正确的）
        /// 继续在这里回落；<see cref="AnimState.Attack"/> 一律改由 <see
        /// cref="NotifyTransientStateFinished"/> 独家负责——由动画播放器的完成回调驱动（见
        /// <c>Adapter.Unity.Presentation.UnityViewFactory.AttachDefaultAnimation</c> 判断记录
        /// "GP-02 根治"，<c>IFrameAnimPlayer.OnComplete</c> 已经接回本状态机），保证 Attack 至少
        /// 完整播放一遍剪辑才回落。<c>SkillCastFailedEvent</c>/<c>SkillCastInterruptedEvent</c> 理论
        /// 上不会在 Attack 状态期间到达（瞬发在 <c>CastPipeline</c> 内同步完成，没有可被打断的窗口，
        /// 见该类型"步骤 8 立即完成"分支），即便到达也遵循同一条规则，不特殊处理。
        /// </para>
        /// </summary>
        private void OnSkillCastEnd(Id casterId)
        {
            if (_entities.TryGetValue(casterId, out var entry) && entry.Current == AnimState.Cast)
            {
                RevertToLocomotion(casterId, entry);
            }
        }

        private void OnCombatDamageDealt(CombatDamageDealtEvent evt) => TryEnter(evt.TargetId, AnimState.Hit);

        // ------------------------------------------------------------------ ADR-0147：受击反应驱动

        private bool IsStaggerActive(Id entityId) =>
            _reactions != null && (_reactions.IsStaggered(entityId) || _reactions.RemainingStaggerTicks(entityId) > 0);

        private void OnReactionApplied(CombatReactionAppliedEvent evt)
        {
            if (evt.Reaction == HitReaction.None || evt.Reaction == HitReaction.Death)
            {
                return; // none 不播任何受击动画；死亡由 unit.died 驱动。
            }

            var entry = GetOrCreate(evt.TargetId);
            if (entry.Current == AnimState.Death)
            {
                return;
            }

            if (evt.DurationTicks <= 0)
            {
                // flinch：单次受击动画，不进硬直、不打断动作；硬直保持中不换成轻抖动；刚显示过格挡抖动的命中不再重播一次。
                if (entry.Current == AnimState.Attack || entry.Current == AnimState.Cast || entry.Hold)
                {
                    return;
                }

                if (entry.Current == AnimState.Hit && entry.HitSub == PoseKeys.HitSubBlock)
                {
                    return;
                }

                EnterHit(evt.TargetId, entry, PoseKeys.HitSubLight, hold: false);
                return;
            }

            // 有硬直的反应：只在它刷新了硬直记录时生效（较弱的反应打在更长的硬直上不换姿势）。
            if (_reactions!.RemainingStaggerTicks(evt.TargetId) > evt.DurationTicks)
            {
                return;
            }

            string sub;
            switch (evt.Reaction)
            {
                case HitReaction.StaggerLight: sub = PoseKeys.HitSubLight; break;
                case HitReaction.Stagger: sub = PoseKeys.HitSubHeavy; break;
                case HitReaction.Knockdown when evt.StunTicks <= 0 && evt.DownedTicks > 0: sub = PoseKeys.HitSubKnockdown; break;
                default: sub = PoseKeys.HitSubKnockback; break;
            }

            EnterHit(evt.TargetId, entry, sub, hold: true);
        }

        private void OnReactionPhase(Id unitId, string sub)
        {
            var entry = GetOrCreate(unitId);
            if (entry.Current == AnimState.Death || !IsStaggerActive(unitId))
            {
                return;
            }

            if (entry.Current == AnimState.Hit && entry.HitSub == sub)
            {
                return; // 同一阶段姿势已经在播（例如硬直段为 0 的倒地已按分段直接进入躺姿）。
            }

            EnterHit(unitId, entry, sub, hold: true);
        }

        private void OnHitConfirmedForBlock(CombatHitConfirmedEvent evt)
        {
            if (evt.HitResult != HitResult.Block)
            {
                return;
            }

            var entry = GetOrCreate(evt.TargetId);
            if (entry.Current == AnimState.Death || entry.Hold || entry.Current == AnimState.Attack || entry.Current == AnimState.Cast)
            {
                return; // 格挡抖动不进硬直，也不打断动作（带格挡窗口的动作自己播格挡剪辑）。
            }

            EnterHit(evt.TargetId, entry, PoseKeys.HitSubBlock, hold: false);
        }

        private void EnterHit(Id entityId, Entry entry, string sub, bool hold)
        {
            var wasHit = entry.Current == AnimState.Hit;
            entry.HitSub = sub; // 先设子键：StateChanged/StateRetriggered 的订阅方在回调里读 GetHitPoseSub。
            if (hold)
            {
                entry.Hold = true;
                _holding.Add(entityId);
            }
            else if (wasHit)
            {
                entry.Hold = false;
                _holding.Remove(entityId);
            }

            TryEnter(entityId, AnimState.Hit);
            if (entry.Current != AnimState.Hit)
            {
                entry.HitSub = null; // 没进成（例如被终态挡住）：不留残余子键。
                entry.Hold = false;
                _holding.Remove(entityId);
            }
        }

        private void ReleaseFinishedHolds()
        {
            if (_holding.Count == 0)
            {
                return;
            }

            _scratch.Clear();
            _scratch.AddRange(_holding);
            for (var i = 0; i < _scratch.Count; i++)
            {
                var id = _scratch[i];
                if (IsStaggerActive(id))
                {
                    continue;
                }

                _holding.Remove(id);
                if (_entities.TryGetValue(id, out var entry))
                {
                    entry.Hold = false;
                    if (entry.Current == AnimState.Hit)
                    {
                        RevertToLocomotion(id, entry);
                    }
                }
            }
        }

        private void TryEnter(Id entityId, AnimState state, Id? triggerSkillId = null)
        {
            var entry = GetOrCreate(entityId);
            if (entry.Current == AnimState.Death)
            {
                return;
            }

            if (entry.Current == state)
            {
                // 见 StateRetriggered 判断记录：同状态重入——运动态保持幂等（不触发），瞬态状态
                // （本方法当前唯一可能的取值范围，Idle/Move 从不经本方法进入，见 RequestOverride
                // 判断记录）改为重触发一次，使调用方重播一遍完整剪辑。
                if (state != AnimState.Idle && state != AnimState.Move)
                {
                    StateRetriggered?.Invoke(entityId, state, triggerSkillId);
                }
                return;
            }

            if (Priority[state] >= Priority[entry.Current])
            {
                SetState(entityId, entry, state, triggerSkillId);
            }
        }

        private void RevertToLocomotion(Id entityId, Entry entry)
        {
            // 空中姿势（AttachAirPhaseSource）：瞬态在空中结束时回落到 Jump（继续上升/下降姿势），不是运动态。
            if (_airSource != null && entry.Current != AnimState.Jump && _airSource.GetContext(entityId).IsAirborne)
            {
                SetState(entityId, entry, AnimState.Jump);
                return;
            }

            SetState(entityId, entry, entry.Locomotion);
        }

        private void SetState(Id entityId, Entry entry, AnimState next, Id? triggerSkillId = null)
        {
            if (entry.Current == next)
            {
                return;
            }

            var previous = entry.Current;
            entry.Current = next;
            if (previous == AnimState.Hit)
            {
                entry.HitSub = null;
                entry.Hold = false;
                _holding.Remove(entityId);
            }

            StateChanged?.Invoke(entityId, previous, next);
            StateChangedWithSkill?.Invoke(entityId, previous, next, triggerSkillId);
        }

        /// <summary>
        /// ADR-0111：战斗姿态变化。姿态与 <see cref="AnimState"/> 正交，本方法不改 <see cref="Entry.Current"/>
        /// ——瞬态状态（Attack/Cast/Hit/Jump）不被打断，其回落时 <see cref="RevertToLocomotion"/> 触发的
        /// <see cref="StateChanged"/> 由订阅方按<b>当时</b>的姿态解析剪辑；Death 终态不受影响（姿态仍被记录，
        /// 只是没有订阅方需要据此切运动态剪辑）。姿态没变不触发事件。
        /// <para>
        /// 判断记录（事件首次跟踪的实体不调用探针）：<c>combat.entered</c> 是 false→true 的迁移、
        /// <c>combat.left</c> 是 true→false 的迁移（CombatHost 只在真实迁移时发布），因此由这两个事件
        /// 首次创建跟踪记录时，迁移前的姿态是确定的（取事件值的反面），不需要也不应该问探针——探针读的是
        /// 规则层"此刻"的值，而事件是入队后才派发的，探针此刻可能已经等于事件值，用它当基线会把这次迁移
        /// 误判为"没变"而吞掉事件。
        /// </para>
        /// </summary>
        private void SetCombatStance(Id entityId, bool inCombat)
        {
            var entry = GetOrCreate(entityId, priorCombatStance: !inCombat);
            if (entry.InCombat == inCombat)
            {
                return;
            }

            entry.InCombat = inCombat;
            CombatStanceChanged?.Invoke(entityId, inCombat);
        }

        private Entry GetOrCreate(Id entityId, bool? priorCombatStance = null)
        {
            if (!_entities.TryGetValue(entityId, out var entry))
            {
                entry = new Entry();
                // ADR-0111：首次跟踪某实体时确定初始战斗姿态——调用方已知迁移前姿态（战斗事件首次创建
                // 记录，见 SetCombatStance 判断记录）就直接用，否则调用一次探针（只在创建时调用一次，
                // 此后姿态由事件驱动）。
                if (priorCombatStance.HasValue)
                {
                    entry.InCombat = priorCombatStance.Value;
                }
                else if (_combatProbe != null)
                {
                    entry.InCombat = _combatProbe(entityId);
                }
                _entities[entityId] = entry;
            }
            return entry;
        }
    }
}
