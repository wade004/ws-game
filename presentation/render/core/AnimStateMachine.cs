using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
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
    /// <c>castTime == 0</c> 判定为瞬发（<see cref="AnimState.Attack"/>，覆盖普攻与瞬发技能，09 第 4.2
    /// 节"attack 与 cast 的具体动作剪辑由武器表现档案决定"未强制区分二者的判据，本类型选取"是否读条"
    /// 这一逻辑层已有字段作为判据）；<c>castTime > 0</c> 判定为 <see cref="AnimState.Cast"/>。三个
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

        public AnimStateMachine(IEventBus bus)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));

            _subscriptions.Add(bus.Subscribe<UnitStateChangedEvent>(CarriersEventKeys.UnitStateChanged, OnUnitStateChanged));
            _subscriptions.Add(bus.Subscribe<SkillCastStartEvent>(RulesEventKeys.SkillCastStart, OnSkillCastStart));
            _subscriptions.Add(bus.Subscribe<SkillCastSuccessEvent>(RulesEventKeys.SkillCastSuccess, evt => OnSkillCastEnd(evt.CasterId)));
            _subscriptions.Add(bus.Subscribe<SkillCastFailedEvent>(RulesEventKeys.SkillCastFailed, evt => OnSkillCastEnd(evt.CasterId)));
            _subscriptions.Add(bus.Subscribe<SkillCastInterruptedEvent>(RulesEventKeys.SkillCastInterrupted, evt => OnSkillCastEnd(evt.CasterId)));
            _subscriptions.Add(bus.Subscribe<CombatDamageDealtEvent>(RulesEventKeys.CombatDamageDealt, OnCombatDamageDealt));
            _subscriptions.Add(bus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, evt => TryEnter(evt.UnitId, AnimState.Death)));
        }

        /// <summary>当前状态；未跟踪过的实体默认 <see cref="AnimState.Idle"/>（还没收到任何该实体的
        /// 相关事件，同"新绑定的 View 默认待机姿态"直觉一致）。</summary>
        public AnimState GetState(Id entityId) =>
            _entities.TryGetValue(entityId, out var entry) ? entry.Current : AnimState.Idle;

        /// <summary>是否已进入终态（见类型注释"death 终态"）。</summary>
        public bool IsTerminal(Id entityId) =>
            _entities.TryGetValue(entityId, out var entry) && entry.Current == AnimState.Death;

        /// <summary>手工触发一次"开始"类切换，走与事件触发同一套优先级判定（见类型注释 jump 判断
        /// 记录）。不接受 <see cref="AnimState.Idle"/>/<see cref="AnimState.Move"/>（运动态只应经
        /// <c>unit.state_changed</c> 驱动，见 <see cref="OnUnitStateChanged"/>）。</summary>
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
                RevertToLocomotion(entityId, entry);
            }
        }

        /// <summary>释放对该实体的跟踪（例如 View 销毁/实体离开场景时由调用方清理，避免字典无限增长）。
        /// 不触发 <see cref="StateChanged"/>——纯粹的簿记清理，不是一次状态切换。</summary>
        public void Forget(Id entityId) => _entities.Remove(entityId);

        public void Dispose()
        {
            foreach (var sub in _subscriptions)
            {
                sub.Dispose();
            }
            _subscriptions.Clear();
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

        private void RevertToLocomotion(Id entityId, Entry entry) => SetState(entityId, entry, entry.Locomotion);

        private void SetState(Id entityId, Entry entry, AnimState next, Id? triggerSkillId = null)
        {
            if (entry.Current == next)
            {
                return;
            }

            var previous = entry.Current;
            entry.Current = next;
            StateChanged?.Invoke(entityId, previous, next);
            StateChangedWithSkill?.Invoke(entityId, previous, next, triggerSkillId);
        }

        private Entry GetOrCreate(Id entityId)
        {
            if (!_entities.TryGetValue(entityId, out var entry))
            {
                entry = new Entry();
                _entities[entityId] = entry;
            }
            return entry;
        }
    }
}
