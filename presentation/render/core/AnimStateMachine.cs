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
    /// （只要 <c>casterId</c> 匹配、当前状态仍是 Attack/Cast）一律驱动回落到当前运动状态。</item>
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
            TryEnter(evt.CasterId, evt.CastTime <= 0 ? AnimState.Attack : AnimState.Cast);

        private void OnSkillCastEnd(Id casterId)
        {
            if (_entities.TryGetValue(casterId, out var entry)
                && (entry.Current == AnimState.Attack || entry.Current == AnimState.Cast))
            {
                RevertToLocomotion(casterId, entry);
            }
        }

        private void OnCombatDamageDealt(CombatDamageDealtEvent evt) => TryEnter(evt.TargetId, AnimState.Hit);

        private void TryEnter(Id entityId, AnimState state)
        {
            var entry = GetOrCreate(entityId);
            if (entry.Current == AnimState.Death)
            {
                return;
            }

            if (Priority[state] >= Priority[entry.Current])
            {
                SetState(entityId, entry, state);
            }
        }

        private void RevertToLocomotion(Id entityId, Entry entry) => SetState(entityId, entry, entry.Locomotion);

        private void SetState(Id entityId, Entry entry, AnimState next)
        {
            if (entry.Current == next)
            {
                return;
            }

            var previous = entry.Current;
            entry.Current = next;
            StateChanged?.Invoke(entityId, previous, next);
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
