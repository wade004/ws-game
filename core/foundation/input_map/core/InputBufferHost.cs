using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;

namespace Core.Foundation.InputMap
{
    /// <summary>输入缓冲的构造期配置（<see cref="InputBufferHost"/>）。</summary>
    public sealed class InputBufferOptions
    {
        /// <summary>模拟固定步长（秒），毫秒按它换算 tick；必须与手感解析器装配时的步长一致（通常取 <c>SimLoopOptions.StepSeconds</c>）。</summary>
        public double StepSeconds { get; set; } = 1.0 / 60.0;

        /// <summary>
        /// 手感判定视图来源：缺省缓冲窗口 <c>buffer_ms</c>、槽位数 <c>buffer_slots</c>、attack/skill 类的缺省按住阈值
        /// <c>hold_threshold_ms</c> 取自行动者当前的解析结果。为空（没有手感档案）时缓冲窗口为 0、槽位数取 <see cref="DefaultSlots"/>、
        /// 不区分按住——既有输入行为不变。
        /// </summary>
        public IFeelJudgingSource? Feel { get; set; }

        /// <summary>
        /// 行动者动作时钟：缓冲过期按它计（顿帧期间不流逝，手感设计/00 第 5 节）。为空时退化为模拟时钟
        /// （每次 <see cref="InputBufferHost.BeginTick"/> 加一，没有顿帧）。
        /// </summary>
        public IActorActionClockQuery? ActionClock { get; set; }

        /// <summary>没有手感档案时的槽位数（手感设计/01 第 2.2 节缺省 2）。</summary>
        public int DefaultSlots { get; set; } = 2;
    }

    /// <summary>
    /// 每行动者输入缓冲（手感设计/01 第 2 节、ADR-0115）：缓冲槽、同类覆盖/优先级替换、按住/点按区分、重复策略、过期（行动者动作时钟）、
    /// 清空，以及动作层取用接口（<see cref="IInputBufferQuery"/>）。
    /// <para>
    /// 驱动顺序（每个模拟 tick，由 <see cref="InputBufferTickHandler"/> 挂在步骤 1 完成）：<see cref="BeginTick"/>（清掉上一 tick 已消费的记录
    /// → 丢弃过期记录 → 把采样到的按下/抬起边沿按到达顺序入槽）→ 动作层经 <see cref="IInputBufferQuery"/> 取用。采样
    /// （<see cref="Press"/>/<see cref="Release"/>/<see cref="OnButtonEdge"/>）随时可调，边沿归入"到达时的下一个模拟 tick"，
    /// 同一 tick 内按下与抬起仍是一次完整点按；真实时间戳不参与任何判定。
    /// </para>
    /// <para>
    /// 判断记录（过期边界）：记录入槽于动作时钟读数 S、缓冲窗口换算为 N tick，则 <c>ExpiresAtActionTime = S + N</c>，
    /// 动作时钟读数 <c>&gt; S + N</c> 时过期。N = 0 即"只在按下当 tick 有效"；窗口内最后一个 tick 提交（距可接受时刻恰好 N tick）的记录
    /// 在第一个可接受 tick 仍可被取用，再早一个 tick 的已过期（01 第 5 节第 1 条）。
    /// </para>
    /// <para>
    /// 判断记录（HoldPending 不过期）：<see cref="BufferHoldState.HoldPending"/> 记录按键仍按着，没有"过期"的概念——缓冲窗口从抬起（或
    /// <see cref="CompleteHold"/>）那一刻起算；它占一个槽位直到抬起/达蓄力上限/被清空/被更高优先级替换。
    /// </para>
    /// <para>
    /// 判断记录（顺序）：每个 tick 先"清掉已消费 → 丢弃过期 → 入槽新边沿"。过期放在入槽之前，是因为入槽规则 1 的"同一动作已在槽内且
    /// 未过期"必须按当前时钟判断：已经过期的旧记录不能被新按下"刷新"复活。新入槽的记录过期时刻不早于当前读数，所以同 tick 内不会被
    /// 自己丢弃。
    /// </para>
    /// <para>
    /// 判断记录（已消费记录的生命周期）：已消费记录保留到下一 tick 的 <see cref="BeginTick"/> 才清除，使施法管线在本 tick 后段
    /// （事件派发之前）报告拒绝时（<see cref="ReportRejected"/>）仍能找到它；拒绝报告晚于下一 tick 开头则找不到记录、什么都不做。
    /// </para>
    /// </summary>
    public sealed class InputBufferHost : IInputBufferQuery, IInputEdgeSink
    {
        private const long NeverExpires = long.MaxValue;

        private readonly IEventBus _bus;
        private readonly InputBufferOptions _options;
        private readonly Dictionary<Id, ActionDefinition> _definitions = new Dictionary<Id, ActionDefinition>();
        private readonly HashSet<Id> _heldOnly = new HashSet<Id>();
        private readonly Dictionary<Id, ActorBuffer> _actors = new Dictionary<Id, ActorBuffer>();
        private readonly List<Id> _actorOrder = new List<Id>();
        private readonly HashSet<Id> _known = new HashSet<Id>();

        private readonly List<Id> _graceConditionNames = new List<Id>();

        private long _actorBuffersAllocated;
        private long _tick = -1;
        private Id? _localActor;
        private Func<Vec2?>? _directionProbe;

        public InputBufferHost(IEventBus bus, InputBufferOptions? options = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? new InputBufferOptions();
            if (!(_options.StepSeconds > 0) || double.IsInfinity(_options.StepSeconds))
            {
                throw new ArgumentException("StepSeconds 必须是正的有限数", nameof(options));
            }
            if (_options.DefaultSlots < 1)
            {
                throw new ArgumentException("DefaultSlots 必须不小于 1", nameof(options));
            }
        }

        private enum EdgeKind
        {
            Press,
            Release,
        }

        private readonly struct PendingEdge
        {
            public readonly EdgeKind Kind;
            public readonly Id ActionId;
            public readonly Vec2? Direction;
            public readonly Id? SkillOverride;
            public readonly JsonObject? ExtraArgs;

            public PendingEdge(EdgeKind kind, Id actionId, Vec2? direction, Id? skillOverride = null, JsonObject? extraArgs = null)
            {
                Kind = kind;
                ActionId = actionId;
                Direction = direction;
                SkillOverride = skillOverride;
                ExtraArgs = extraArgs;
            }
        }

        private sealed class Record
        {
            public ActionDefinition Definition = null!;
            public long SubmittedTick;
            public long ExpiresAt;
            public int Priority;
            public Vec2? Direction;
            public BufferHoldState Hold;
            public int HeldTicks;
            public bool Consumed;
            public long HoldStartAction;
            public int HoldThresholdTicks;
            public int BufferTicks;
            public Id? SkillOverride;
            public JsonObject? ExtraArgs;

            public BufferedIntent ToIntent() => new BufferedIntent(
                Definition.ActionId, Definition.Class ?? ActionClass.Menu, SubmittedTick, ExpiresAt, Priority, Direction,
                Hold, HeldTicks, Consumed, Definition.EffectiveFaceOnAccept, SkillOverride, ExtraArgs);
        }

        private sealed class ActorBuffer
        {
            public readonly List<Record> Slots = new List<Record>();
            public readonly List<PendingEdge> Pending = new List<PendingEdge>();

            /// <summary>此刻被按住的输入动作（按下边沿处理时加入、抬起边沿处理时移除；<see cref="IInputBufferQuery.IsHeld"/> 的数据来源）。</summary>
            public readonly HashSet<Id> Held = new HashSet<Id>();

            /// <summary>只追踪按住状态的动作（<see cref="ActionDefinition.IsHeldTracked"/>）的当前按住集合：按下/抬起即时生效（不经待处理边沿），
            /// 缓冲清空（<see cref="InputBufferHost.Clear"/>）不影响——它反映的是按键物理状态，不是缓冲内容。</summary>
            public readonly HashSet<Id> HeldOnly = new HashSet<Id>();

            public long LastAcceptTick = -1;
        }

        // -----------------------------------------------------------------
        // 声明与接线
        // -----------------------------------------------------------------

        /// <summary>当前模拟 tick 序号（<see cref="BeginTick"/> 之前为 -1）。</summary>
        public long CurrentTick => _tick;

        /// <summary>
        /// 登记动作定义：只有按钮型且声明了非 <see cref="ActionClass.Move"/> 类别的动作经缓冲（<see cref="ActionDefinition.IsBuffered"/>）；
        /// 按钮型 <see cref="ActionClass.Move"/> 类动作只追踪按住状态（<see cref="ActionDefinition.IsHeldTracked"/>，ADR-0153，按住冲刺用）；
        /// 其余（既有行为：未声明类别、轴类）被忽略。同名重复登记以后者为准（热加载数据）。
        /// </summary>
        public void DeclareActions(IEnumerable<ActionDefinition> actions)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            foreach (var def in actions)
            {
                if (def == null) throw new ArgumentException("动作集合不能包含 null 元素", nameof(actions));
                if (def.IsBuffered)
                {
                    _definitions[def.ActionId] = def;
                    _heldOnly.Remove(def.ActionId);
                }
                else if (def.IsHeldTracked)
                {
                    _definitions.Remove(def.ActionId);
                    _heldOnly.Add(def.ActionId);
                }
                else
                {
                    _definitions.Remove(def.ActionId);
                    _heldOnly.Remove(def.ActionId);
                }
            }

            var hadGraceConditions = _graceConditionNames.Count > 0;
            RebuildGraceConditionNames();
            if (!hadGraceConditions && _graceConditionNames.Count > 0) GraceConditionsDeclared?.Invoke();
        }

        /// <summary>
        /// 宽限条件名并集从"空"变为"非空"时触发（<see cref="DeclareActions"/> 之后）：此前没有任何动作声明宽限条件，上层按需登记行动者的代码（生产装配的单位自动登记）
        /// 因此一直没有登记，此刻才需要为已有的行动者补登记（手感落地 M4-G：缓冲惰性分配，没有宽限条件时不为任何单位建缓冲）。
        /// </summary>
        public event Action? GraceConditionsDeclared;

        /// <summary>
        /// 已建立的行动者缓冲累计个数（含之后被 <see cref="RemoveActor"/> 清掉的）：度量缓冲分配用。缓冲只在行动者第一次真有边沿
        /// （<see cref="Press"/>/<see cref="Release"/>/<see cref="Submit"/>）时才建——<see cref="RegisterActor"/>（含显式调用与生产装配的单位自动登记）只记 id、不建缓冲，
        /// 所以登记再多的单位，没人按键时该数字保持不变（手感落地 M4-G、M4-W3）。
        /// </summary>
        public long ActorBuffersAllocated => _actorBuffersAllocated;

        /// <summary>
        /// 已声明的缓冲动作的宽限条件名并集（序数序、去重）：宽限追踪（<see cref="GraceTracker"/>）据此对每个有缓冲的行动者采样
        /// （手感落地 M2-B，手感设计/01 第 2.4 节）。没有动作声明宽限条件时为空。
        /// </summary>
        public IReadOnlyList<Id> GraceConditionNames => _graceConditionNames;

        /// <summary>
        /// 声明了宽限条件 <paramref name="conditionId"/> 的缓冲动作 id（序数序）：框架内置宽限条件据此找到"这个条件服务于哪些动作"，
        /// 进而取动作绑定技能的射程（手感落地 M4-G）。没有动作引用该条件时为空。
        /// </summary>
        public IReadOnlyList<Id> ActionsWithGraceCondition(Id conditionId)
        {
            var result = new List<Id>();
            foreach (var def in _definitions.Values)
            {
                for (var i = 0; i < def.GraceConditions.Count; i++)
                {
                    if (def.GraceConditions[i].Equals(conditionId))
                    {
                        result.Add(def.ActionId);
                        break;
                    }
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            return result;
        }

        /// <summary><see cref="BindLocalInput"/> 绑定的本地行动者；未绑定为 null（行动者实体被销毁不会解除绑定，见 <see cref="RemoveActor"/> 判断记录）。宽限追踪据此在玩家第一次按键之前就开始采样。</summary>
        public Id? LocalActorId => _localActor;

        private void RebuildGraceConditionNames()
        {
            _graceConditionNames.Clear();
            foreach (var def in _definitions.Values)
            {
                for (var i = 0; i < def.GraceConditions.Count; i++)
                {
                    if (!_graceConditionNames.Contains(def.GraceConditions[i])) _graceConditionNames.Add(def.GraceConditions[i]);
                }
            }

            _graceConditionNames.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
        }

        /// <summary>动作是否经缓冲。</summary>
        public bool IsBuffered(Id actionId) => _definitions.ContainsKey(actionId);

        /// <summary>取经缓冲的动作定义（供动作层读 <c>grace_conditions</c> 等）；不经缓冲返回 null。</summary>
        public ActionDefinition? GetDefinition(Id actionId) => _definitions.TryGetValue(actionId, out var def) ? def : null;

        /// <summary>
        /// 把本地输入映射的按钮边沿接给 <paramref name="actorId"/> 的缓冲：登记为 <paramref name="map"/> 的边沿接收端；
        /// <paramref name="moveActionName"/> 非空时，按下瞬间以该轴动作的当前轴值（平方长 &gt; 0.0001）作方向快照。
        /// </summary>
        public void BindLocalInput(IInputMapHost map, Id actorId, string? moveActionName = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            _localActor = actorId;
            if (moveActionName == null)
            {
                _directionProbe = null;
            }
            else
            {
                _directionProbe = () =>
                {
                    var axis = map.GetActionAxis(moveActionName);
                    return axis.SqrLength > 0.0001 ? axis : (Vec2?)null;
                };
            }
            map.SetEdgeSink(this);
        }

        /// <summary><see cref="IInputEdgeSink"/>：本地输入的按钮边沿，归属 <see cref="BindLocalInput"/> 指定的行动者；未绑定时忽略。</summary>
        public void OnButtonEdge(string actionName, bool isDown)
        {
            if (!_localActor.HasValue) return;
            var actionId = new Id(actionName);
            if (_heldOnly.Contains(actionId))
            {
                SetHeldOnly(_localActor.Value, actionId, isDown);
                return;
            }
            if (!_definitions.ContainsKey(actionId)) return;
            if (isDown) Press(_localActor.Value, actionId, _directionProbe?.Invoke());
            else Release(_localActor.Value, actionId);
        }

        // -----------------------------------------------------------------
        // 采样（随时可调，归入下一个 BeginTick）
        // -----------------------------------------------------------------

        /// <summary>某行动者按下某动作（本地输入、AI、自动战斗同一入口，手感设计/01 第 1 节）。不经缓冲的动作被忽略。</summary>
        public void Press(Id actorId, Id actionId, Vec2? direction = null)
        {
            if (_heldOnly.Contains(actionId))
            {
                SetHeldOnly(actorId, actionId, true);
                return;
            }
            if (!_definitions.ContainsKey(actionId)) return;
            GetOrCreate(actorId).Pending.Add(new PendingEdge(EdgeKind.Press, actionId, direction));
        }

        /// <summary>某行动者抬起某动作。</summary>
        public void Release(Id actorId, Id actionId)
        {
            if (_heldOnly.Contains(actionId))
            {
                SetHeldOnly(actorId, actionId, false);
                return;
            }
            if (!_definitions.ContainsKey(actionId)) return;
            GetOrCreate(actorId).Pending.Add(new PendingEdge(EdgeKind.Release, actionId, null));
        }

        /// <summary>一次完整点按（按下 + 抬起落在同一 tick 内）；AI 与自动战斗提交意图用。</summary>
        public void Submit(Id actorId, Id actionId, Vec2? direction = null)
        {
            Press(actorId, actionId, direction);
            Release(actorId, actionId);
        }

        /// <summary>
        /// 提交一条带显式技能与额外参数的点按（ADR-0143）：AI 经输入缓冲提交 <c>cast</c> 意图时用——AI 决策的是"施放哪个技能、对谁"，没有输入动作 → 技能映射，
        /// 所以记录直接携带 <paramref name="skillId"/> 与 <paramref name="extraArgs"/>（<c>targets</c>/<c>point</c> 等），接受时并入生成的意图。
        /// 其余规则与 <see cref="Submit"/> 完全相同（同一条缓冲、同一套优先级与过期、同一个取消窗口）。不经缓冲的动作被忽略。
        /// </summary>
        public void SubmitSkill(Id actorId, Id actionId, Id skillId, JsonObject? extraArgs = null, Vec2? direction = null)
        {
            if (!_definitions.ContainsKey(actionId)) return;
            var buffer = GetOrCreate(actorId);
            buffer.Pending.Add(new PendingEdge(EdgeKind.Press, actionId, direction, skillId, extraArgs));
            buffer.Pending.Add(new PendingEdge(EdgeKind.Release, actionId, null));
        }

        /// <summary>该行动者的该输入动作此刻是否仍被按住（<see cref="IInputBufferQuery.IsHeld"/>）。</summary>
        public bool IsHeld(Id actorId, Id actionId) =>
            _actors.TryGetValue(actorId, out var buffer) && (buffer.Held.Contains(actionId) || buffer.HeldOnly.Contains(actionId));

        /// <summary>动作是否只追踪按住状态（按钮型 move 类，ADR-0153）。</summary>
        public bool IsHeldTracked(Id actionId) => _heldOnly.Contains(actionId);

        private void SetHeldOnly(Id actorId, Id actionId, bool down)
        {
            var buffer = down ? GetOrCreate(actorId) : (_actors.TryGetValue(actorId, out var existing) ? existing : null);
            if (buffer == null) return;
            if (down) buffer.HeldOnly.Add(actionId);
            else buffer.HeldOnly.Remove(actionId);
        }

        /// <summary>
        /// 蓄力规则来源（可空，ADR-0143）：非空时，声明了蓄力的动作在按住满上限时自动释放，抬起时按下限判门槛。缺省 null 即既有行为。
        /// 生产装配在构造缓冲之后赋值（规则来自"输入动作 → 技能"映射与技能的 <c>timeline.charge</c>）。
        /// </summary>
        public IChargeRuleSource? ChargeRules { get; set; }

        /// <summary>
        /// 抬起边沿处理完时触发（<see cref="BeginTick"/> 内，每个抬起边沿一次；参数：行动者、动作）。动作层据此做"松键"类响应
        /// （例如可变跳高裁切）。只对经缓冲的动作触发。
        /// </summary>
        public event Action<Id, Id>? ActionReleased;

        /// <summary>
        /// 登记行动者（手感落地 M3-B）：使宽限追踪从登记起就按声明的条件逐 tick 采样（<see cref="InputBufferTickHandler"/> 对全部已登记的行动者采样）。
        /// 不登记也能用——首次按下/提交时才建缓冲，但"条件刚失效"的第一次按键没有历史可查；生产装配在有动作声明宽限条件时对世界里的单位（出生与装配时已有的）自动调用本方法。
        /// <para>
        /// 判断记录（惰性分配，手感落地 M4-G、M4-W3）：登记只记下行动者 id（宽限采样不需要缓冲槽），缓冲对象（槽位与待入槽边沿）等到第一次真的有边沿到达
        /// （<see cref="Press"/>/<see cref="Release"/>/<see cref="Submit"/>）才建，所以显式调用 N 次的代价是 N 个 id 记录、<see cref="ActorBuffersAllocated"/> 不动。
        /// "显式登记即可采样"的语义不变：<see cref="IsActorRegistered"/> 为真、<see cref="ActorIds"/> 含该行动者（登记顺序与此前建缓冲的顺序一致）。
        /// </para>
        /// 幂等；之后 <see cref="RemoveActor"/> 与销毁清理照旧。
        /// </summary>
        public void RegisterActor(Id actorId) => Know(actorId);

        /// <summary>行动者是否已登记（显式 <see cref="RegisterActor"/>，或已因按键建立过缓冲）。</summary>
        public bool IsActorRegistered(Id actorId) => _known.Contains(actorId);

        private void Know(Id actorId)
        {
            if (_known.Add(actorId)) _actorOrder.Add(actorId);
        }

        private ActorBuffer GetOrCreate(Id actorId)
        {
            Know(actorId);
            if (!_actors.TryGetValue(actorId, out var buffer))
            {
                buffer = new ActorBuffer();
                _actorBuffersAllocated++;
                _actors.Add(actorId, buffer);
            }
            return buffer;
        }

        /// <summary>已登记的行动者（显式登记或已建立过缓冲；按首次出现顺序的新快照）。</summary>
        public IReadOnlyList<Id> ActorIds => _actorOrder.ToArray();

        // -----------------------------------------------------------------
        // tick 步骤 1
        // -----------------------------------------------------------------

        /// <summary>
        /// 推进一个模拟 tick 的缓冲维护（手感设计/01 第 2.3 节第 1 点及入槽）：清掉上一 tick 已消费的记录 → 丢弃过期记录
        /// （<see cref="BufferDropReason.Expired"/>）→ 把采样到的边沿按到达顺序入槽。过期以行动者动作时钟计，顿帧暂停的行动者不过期。
        /// </summary>
        public void BeginTick()
        {
            _tick++;
            for (var a = 0; a < _actorOrder.Count; a++)
            {
                var actorId = _actorOrder[a];
                if (!_actors.TryGetValue(actorId, out var buffer)) continue;
                var now = ActionNow(actorId);

                buffer.Slots.RemoveAll(r => r.Consumed);

                for (var i = 0; i < buffer.Slots.Count;)
                {
                    var r = buffer.Slots[i];
                    if (r.Hold != BufferHoldState.HoldPending && now > r.ExpiresAt)
                    {
                        buffer.Slots.RemoveAt(i);
                        Drop(actorId, r, BufferDropReason.Expired, null);
                    }
                    else
                    {
                        i++;
                    }
                }

                if (buffer.Pending.Count > 0)
                {
                    var edges = buffer.Pending.ToArray();
                    buffer.Pending.Clear();
                    for (var e = 0; e < edges.Length; e++)
                    {
                        if (edges[e].Kind == EdgeKind.Press)
                        {
                            buffer.Held.Add(edges[e].ActionId);
                            ApplyPress(actorId, buffer, edges[e], now);
                        }
                        else
                        {
                            buffer.Held.Remove(edges[e].ActionId);
                            ApplyRelease(actorId, buffer, edges[e].ActionId, now);
                            ActionReleased?.Invoke(actorId, edges[e].ActionId);
                        }
                    }
                }

                AutoReleaseCharges(actorId, buffer, now);
            }
        }

        /// <summary>
        /// 蓄力自动释放（ADR-0143，手感设计/01 第 3.3 节 <c>charge_ready</c>）：处于 <see cref="BufferHoldState.HoldPending"/> 且按住满蓄力上限
        /// （按行动者动作时钟计，顿帧期间不流逝）的记录转为 <see cref="BufferHoldState.HoldReleased"/>（<see cref="BufferedIntent.HeldTicks"/> 恒等于上限）、
        /// 开始计缓冲窗口，并发 <see cref="InputChargeReadyEvent"/>；按键此后仍按着也不会再次触发（记录已不是 HoldPending，抬起边沿只更新按住状态）。
        /// 没有注入蓄力规则来源、动作不对应蓄力技能、规则上限为 0 的记录不受影响（既有行为）。
        /// </summary>
        private void AutoReleaseCharges(Id actorId, ActorBuffer buffer, long now)
        {
            var rules = ChargeRules;
            if (rules == null) return;

            for (var i = 0; i < buffer.Slots.Count; i++)
            {
                var r = buffer.Slots[i];
                if (r.Hold != BufferHoldState.HoldPending) continue;
                if (!rules.TryGetChargeRule(actorId, r.Definition.ActionId, out var rule) || rule.MaxTicks <= 0) continue;
                if (now - r.HoldStartAction < rule.MaxTicks) continue;

                r.Hold = BufferHoldState.HoldReleased;
                r.HeldTicks = rule.MaxTicks;
                r.ExpiresAt = now + r.BufferTicks;
                _bus.Enqueue(new InputChargeReadyEvent(actorId, r.Definition.ActionId, rule.MaxTicks));
            }
        }

        private void ApplyPress(Id actorId, ActorBuffer buffer, PendingEdge edge, long now)
        {
            var def = _definitions[edge.ActionId];
            var bufferTicks = BufferTicks(actorId, def);
            var holdTicks = HoldThresholdTicks(actorId, def);

            // 入槽规则 1：同一动作已在槽内且未过期。
            for (var i = 0; i < buffer.Slots.Count; i++)
            {
                var existing = buffer.Slots[i];
                if (!existing.Definition.ActionId.Equals(edge.ActionId)) continue;
                if (def.RepeatPolicy == InputRepeatPolicy.Ignore) return;

                existing.Direction = edge.Direction;
                existing.SkillOverride = edge.SkillOverride;
                existing.ExtraArgs = edge.ExtraArgs;
                existing.BufferTicks = bufferTicks;
                if (holdTicks > 0)
                {
                    existing.Hold = BufferHoldState.HoldPending;
                    existing.HeldTicks = 0;
                    existing.HoldStartAction = now;
                    existing.HoldThresholdTicks = holdTicks;
                    existing.ExpiresAt = NeverExpires;
                }
                else
                {
                    existing.ExpiresAt = now + bufferTicks;
                }
                return;
            }

            var record = new Record
            {
                Definition = def,
                SubmittedTick = _tick,
                Priority = def.EffectivePriority,
                Direction = edge.Direction,
                Hold = holdTicks > 0 ? BufferHoldState.HoldPending : BufferHoldState.Tap,
                HeldTicks = 0,
                Consumed = false,
                HoldStartAction = now,
                HoldThresholdTicks = holdTicks,
                BufferTicks = bufferTicks,
                ExpiresAt = holdTicks > 0 ? NeverExpires : now + bufferTicks,
                SkillOverride = edge.SkillOverride,
                ExtraArgs = edge.ExtraArgs,
            };

            // 入槽规则 2：槽满时，新意图优先级高于槽内最低者则替换之，否则丢弃新意图。
            if (buffer.Slots.Count >= SlotCount(actorId))
            {
                var lowestIndex = 0;
                for (var i = 1; i < buffer.Slots.Count; i++)
                {
                    var candidate = buffer.Slots[i];
                    var lowest = buffer.Slots[lowestIndex];
                    if (candidate.Priority < lowest.Priority
                        || (candidate.Priority == lowest.Priority && candidate.SubmittedTick < lowest.SubmittedTick))
                    {
                        lowestIndex = i;
                    }
                }

                var victim = buffer.Slots[lowestIndex];
                if (record.Priority > victim.Priority)
                {
                    buffer.Slots.RemoveAt(lowestIndex);
                    Drop(actorId, victim, BufferDropReason.Replaced, null);
                }
                else
                {
                    Drop(actorId, record, BufferDropReason.Full, null);
                    return;
                }
            }

            buffer.Slots.Add(record);
        }

        private void ApplyRelease(Id actorId, ActorBuffer buffer, Id actionId, long now)
        {
            for (var i = 0; i < buffer.Slots.Count; i++)
            {
                var r = buffer.Slots[i];
                if (!r.Definition.ActionId.Equals(actionId) || r.Hold != BufferHoldState.HoldPending) continue;

                var held = (int)Math.Max(0, now - r.HoldStartAction);
                if (held >= r.HoldThresholdTicks)
                {
                    var rule = default(ChargeRule);
                    var hasRule = ChargeRules != null && ChargeRules.TryGetChargeRule(actorId, actionId, out rule);
                    if (hasRule && rule.CancelBelowMin && held < rule.MinTicks)
                    {
                        // 蓄力不足且技能声明了 below_min: cancel：记录取消（ADR-0143）。
                        buffer.Slots.RemoveAt(i);
                        Drop(actorId, r, BufferDropReason.ChargeBelowMin, null);
                        return;
                    }

                    r.Hold = BufferHoldState.HoldReleased;
                    r.HeldTicks = hasRule && rule.MaxTicks > 0 ? Math.Min(held, rule.MaxTicks) : held;
                }
                else
                {
                    r.Hold = BufferHoldState.Tap;
                    r.HeldTicks = 0;
                }
                r.ExpiresAt = now + r.BufferTicks;
                return;
            }
        }

        // -----------------------------------------------------------------
        // 取用（IInputBufferQuery）
        // -----------------------------------------------------------------

        public IReadOnlyList<BufferedIntent> Snapshot(Id actorId)
        {
            if (!_actors.TryGetValue(actorId, out var buffer) || buffer.Slots.Count == 0)
            {
                return Array.Empty<BufferedIntent>();
            }

            var result = new BufferedIntent[buffer.Slots.Count];
            for (var i = 0; i < result.Length; i++) result[i] = buffer.Slots[i].ToIntent();
            return result;
        }

        public bool TryPeek(Id actorId, out BufferedIntent intent) => TryPeek(actorId, null, out intent);

        public bool TryPeek(Id actorId, Func<BufferedIntent, bool>? skip, out BufferedIntent intent)
        {
            var record = FindCandidate(actorId, skip, out _);
            if (record == null)
            {
                intent = default;
                return false;
            }
            intent = record.ToIntent();
            return true;
        }

        public bool TryConsume(Id actorId, Func<BufferedIntent, bool>? accepts, out BufferedIntent consumed) =>
            TryConsume(actorId, accepts, null, out consumed);

        public bool TryConsume(Id actorId, Func<BufferedIntent, bool>? accepts, Func<BufferedIntent, bool>? skip, out BufferedIntent consumed)
        {
            var record = FindCandidate(actorId, skip, out var buffer);
            if (record == null || buffer == null || (accepts != null && !accepts(record.ToIntent())))
            {
                consumed = default;
                return false;
            }

            record.Consumed = true;
            buffer.LastAcceptTick = _tick;
            consumed = record.ToIntent();
            return true;
        }

        private Record? FindCandidate(Id actorId, Func<BufferedIntent, bool>? skip, out ActorBuffer? buffer)
        {
            buffer = null;
            if (!_actors.TryGetValue(actorId, out var found)) return null;
            buffer = found;

            // 01 第 2.3 节第 2 点：顿帧暂停的行动者本 tick 不消费（记录保留，过期不推进）；第 5 点：一个 tick 至多接受一条。
            if (IsPaused(actorId) || found.LastAcceptTick == _tick) return null;

            var now = ActionNow(actorId);
            Record? best = null;
            for (var i = 0; i < found.Slots.Count; i++)
            {
                var r = found.Slots[i];
                if (r.Consumed || r.Hold == BufferHoldState.HoldPending || now > r.ExpiresAt) continue;
                if (skip != null && skip(r.ToIntent())) continue; // 永远接不了的记录不参与排序（见 IBufferedIntentSink.CanHandle）
                if (best == null || r.Priority > best.Priority || (r.Priority == best.Priority && r.SubmittedTick < best.SubmittedTick))
                {
                    best = r;
                }
            }
            return best;
        }

        public void ReportRejected(Id actorId, Id actionId, string reasonCode, bool timeSolvable)
        {
            if (!_actors.TryGetValue(actorId, out var buffer)) return;
            for (var i = 0; i < buffer.Slots.Count; i++)
            {
                var r = buffer.Slots[i];
                if (!r.Consumed || !r.Definition.ActionId.Equals(actionId)) continue;

                if (timeSolvable)
                {
                    // 撤销已消费标记，记录保留到过期；LastAcceptTick 保持不变——本 tick 不再取用，下一 tick 重试。
                    r.Consumed = false;
                }
                else
                {
                    buffer.Slots.RemoveAt(i);
                    Drop(actorId, r, BufferDropReason.Rejected, reasonCode);
                }
                return;
            }
        }

        /// <summary>管线拒绝原因码是否属于"时间可解"（01 第 2.3 节第 4 点：<c>ACTION_LOCKED</c>、<c>GCD_ACTIVE</c>、<c>ON_COOLDOWN</c>）。
        /// <c>ON_COOLDOWN</c> 还需调用方自行核对"剩余冷却不超过记录剩余缓冲"，见 <see cref="RemainingBufferTicks"/>。</summary>
        public static bool IsTimeSolvableReason(string reasonCode) =>
            reasonCode == "ACTION_LOCKED" || reasonCode == "GCD_ACTIVE" || reasonCode == "ON_COOLDOWN";

        /// <summary>某动作记录在行动者动作时钟下剩余的缓冲 tick 数（已过期或无此记录为 0；<see cref="BufferHoldState.HoldPending"/> 视为无限，返回 int.MaxValue）。</summary>
        public int RemainingBufferTicks(Id actorId, Id actionId)
        {
            if (!_actors.TryGetValue(actorId, out var buffer)) return 0;
            var now = ActionNow(actorId);
            for (var i = 0; i < buffer.Slots.Count; i++)
            {
                var r = buffer.Slots[i];
                if (!r.Definition.ActionId.Equals(actionId)) continue;
                if (r.Hold == BufferHoldState.HoldPending) return int.MaxValue;
                var left = r.ExpiresAt - now;
                return left <= 0 ? 0 : (int)Math.Min(left, int.MaxValue);
            }
            return 0;
        }

        public bool CompleteHold(Id actorId, Id actionId, int heldTicks)
        {
            if (!_actors.TryGetValue(actorId, out var buffer)) return false;
            var now = ActionNow(actorId);
            for (var i = 0; i < buffer.Slots.Count; i++)
            {
                var r = buffer.Slots[i];
                if (!r.Definition.ActionId.Equals(actionId) || r.Hold != BufferHoldState.HoldPending) continue;
                r.Hold = BufferHoldState.HoldReleased;
                r.HeldTicks = heldTicks < 0 ? 0 : heldTicks;
                r.ExpiresAt = now + r.BufferTicks;
                return true;
            }
            return false;
        }

        // -----------------------------------------------------------------
        // 清空（01 第 2.2 节：死亡、场景切换、切到离散模式、读档、销毁）
        // -----------------------------------------------------------------

        /// <summary>清空某行动者的缓冲：每条记录发 <see cref="BufferDropReason.Cleared"/>，尚未入槽的采样边沿一并丢弃。</summary>
        public void Clear(Id actorId)
        {
            if (!_actors.TryGetValue(actorId, out var buffer)) return;
            buffer.Pending.Clear();
            buffer.Held.Clear();
            var records = buffer.Slots.ToArray();
            buffer.Slots.Clear();
            for (var i = 0; i < records.Length; i++) Drop(actorId, records[i], BufferDropReason.Cleared, null);
        }

        /// <summary>清空全部行动者的缓冲（场景切换、读档、离散模式切换）。</summary>
        public void ClearAll()
        {
            for (var a = 0; a < _actorOrder.Count; a++) Clear(_actorOrder[a]);
        }

        /// <summary>行动者被销毁：清空其缓冲并释放该行动者的缓冲对象。</summary>
        public void RemoveActor(Id actorId)
        {
            Clear(actorId);
            _actors.Remove(actorId);
            if (_known.Remove(actorId)) _actorOrder.Remove(actorId);
            // 判断记录（消费方反馈 P2 缺口 6：地图切换后本地玩家再也不能出招）：本地绑定（BindLocalInput）是"这套输入属于哪个行动者 id"的角色声明，
            // 不是对某一次实体生命周期的引用——地图切换会让场景清空（ClearAll → entity.destroyed → 本方法），游戏随后用同一个 id 把玩家单位重新加回世界；
            // 此前这里顺手把本地绑定置空，重新加回的玩家就再也收不到按钮边沿（OnButtonEdge 因未绑定而忽略），攻击/闪避/技能全部静默失效。
            // 现保留绑定：缓冲对象照旧释放，实体重新创建后同一 id 的边沿直接进入新缓冲。
        }

        private void Drop(Id actorId, Record record, BufferDropReason reason, string? reasonCode)
        {
            _bus.Enqueue(new InputBufferDroppedEvent(actorId, record.Definition.ActionId, reason, reasonCode));
        }

        // -----------------------------------------------------------------
        // 时钟与档案
        // -----------------------------------------------------------------

        private long ActionNow(Id actorId) => _options.ActionClock != null ? _options.ActionClock.ActionTicks(actorId) : _tick;

        private bool IsPaused(Id actorId) => _options.ActionClock != null && _options.ActionClock.IsPaused(actorId);

        private int BufferTicks(Id actorId, ActionDefinition def)
        {
            if (def.BufferMs.HasValue) return FeelCalibration.MillisecondsToTicks(def.BufferMs.Value, _options.StepSeconds);
            if (_options.Feel != null) return _options.Feel.ResolveJudging(actorId).GetTicks(FeelFieldNames.BufferMs);
            return 0;
        }

        private int SlotCount(Id actorId)
        {
            if (_options.Feel == null) return _options.DefaultSlots;
            var slots = (int)Math.Round(_options.Feel.ResolveJudging(actorId).GetNumber(FeelFieldNames.BufferSlots));
            return slots < 1 ? 1 : slots;
        }

        /// <summary>
        /// 按住阈值（tick，0 表示不区分点按与按住）：动作自己声明的值优先；否则 attack/skill 类取手感档案的缺省按住阈值
        /// （判断记录：档案字段说明为"缺省按住阈值"，若对 interact/menu 等也生效会让每次交互都先挂起等抬起，故只对 attack/skill 生效）。
        /// </summary>
        private int HoldThresholdTicks(Id actorId, ActionDefinition def)
        {
            if (def.Class == ActionClass.Jump) return 0; // 跳跃不区分点按与按住（可变跳高由 jump_cut_ratio 表达）
            if (def.HoldThresholdMs.HasValue) return FeelCalibration.MillisecondsToTicks(def.HoldThresholdMs.Value, _options.StepSeconds);
            if (_options.Feel != null && (def.Class == ActionClass.Attack || def.Class == ActionClass.Skill))
            {
                var view = _options.Feel.ResolveJudging(actorId);
                if (view.TryGetNumber(FeelFieldNames.HoldThresholdMs, out var ms) && ms > 0)
                {
                    return FeelCalibration.MillisecondsToTicks(ms, _options.StepSeconds);
                }
            }
            return 0;
        }
    }
}
