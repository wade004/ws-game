using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>一次进行中的时间线动作的运行状态（手感设计/01 第 3 节）。挂在 <c>CastState.Run</c> 上，随施法终结一起丢弃。</summary>
    internal sealed class ActionRun
    {
        public Id CastInstanceId;
        public Id SkillId;
        public SkillDef Def = default!;
        public TimelineDef Timeline = default!;
        public TimelineSchedule Schedule = default!;

        /// <summary>连招链的根技能（链上每一段共用；非连招动作等于自身）。</summary>
        public Id ChainRoot;

        public int ComboIndex;
        public double ChargeRatio;

        /// <summary>本动作的效果值倍率（<c>charge.value_scale</c> 按蓄力比例插值；未声明恒为 1）。</summary>
        public double ChargeValueScale = 1.0;

        public Vec2? Direction;

        /// <summary>已经过的动作时钟 tick（顿帧暂停期间不增长）。</summary>
        public int Elapsed;

        /// <summary>下一个待触发的调度事件下标。</summary>
        public int NextEvent;

        public ActionPhase Phase = ActionPhase.Startup;

        /// <summary>上一次读取的动作时钟累计 tick（增量推进用）。</summary>
        public long LastClock;

        /// <summary>开始时的 Update 序号：开始 tick 本身不推进（见 CastPipeline.Timeline.cs 判断记录）。</summary>
        public int StartSeq;

        public bool ActiveEntered;
        public bool CostPaid;
        public bool CooldownStarted;
        public bool FirstHitSeen;
        public bool Invulnerable;
        public bool SuperArmor;

        /// <summary>格挡窗口（<c>guard_start</c>～<c>guard_end</c>，手感设计/03 第 4 节，ADR-0145）。</summary>
        public bool Guarding;

        /// <summary><c>guard_start</c> 触发时的 <see cref="Elapsed"/>；弹反窗口按 <c>Elapsed - GuardStartedAt</c> 判。</summary>
        public int GuardStartedAt;

        public bool MotionOpen;

        /// <summary>动作被接受时落定的位移段快照（声明了 <c>motion</c> 块才有；运动仲裁器经 <see cref="ActionState.Motion"/> 读取）。</summary>
        public ActionMotionState? MotionState;

        /// <summary>动作自然结束后连招链保留的动作时钟 tick 数（档案 <c>combo_reset_ms</c> 换算，开始时快照）。</summary>
        public int ComboResetTicks;

        // ---- 空间命中（手感设计/03 第 2.2 节；见 CastPipeline.TimelineHit.cs）----

        /// <summary>命中路径是否已解析（首次需要时按技能数据解析一次并缓存）。</summary>
        public bool HitPathResolved;

        /// <summary>true = 空间命中；false = instant 结算（S3a 行为）。</summary>
        public bool Spatial;

        /// <summary>目标选择链的形状模板（<see cref="Spatial"/> 为真时有值）。</summary>
        public Shape Template;

        /// <summary>
        /// 攻击实例命中集合（随动作实例存在，动作结束/取消/硬直时随 <c>ActionRun</c> 一起丢弃）：键为 (目标, 段序号)，值为命中的动作 tick。
        /// 回避类结局（无敌）同样记入——"同一攻击实例对同一目标只判定一次"。
        /// </summary>
        public readonly Dictionary<(Id Target, int Segment), int> HitLedger = new Dictionary<(Id Target, int Segment), int>();

        /// <summary>同一目标最近一次命中的动作 tick（<c>rehit_interval_ms</c> 的 marker 策略用）。</summary>
        public readonly Dictionary<Id, int> LastHitTick = new Dictionary<Id, int>();

        /// <summary>上一次推进结束时攻击方的位姿（<c>continuous</c> 两 tick 之间插值采样的起点）。</summary>
        public Vec2 PrevPosition;

        public double PrevFacing;

        /// <summary>目标辅助挑出的目标（动作被接受时解析一次；没有目标辅助或没有候选为 null）。</summary>
        public Id? AssistTarget;

        // continuous 命中的当前推进区间（BeginContinuousSpan 每次推进开始时落定，采样与调度事件按时间顺序交错执行，
        // 保证命中事件先于其后相位切换事件发布，反馈侧的挥空窗口不会被提前关闭）。
        public int SpanFrom;
        public int SpanTo;
        public int SpanCount;
        public int SpanNext;
        public Vec2 SpanStartPosition;
        public Vec2 SpanEndPosition;
        public double SpanStartFacing;
        public double SpanTurn;

        /// <summary>声明了 <c>release</c> 标记：投射物效果在该标记处发射，<c>hit</c> 标记处只结算其余效果。</summary>
        public bool HasReleaseMarker;
    }

    /// <summary>每个行动者一条的连招链状态（见 <c>CastPipeline.ApplyComboRedirect</c>）。</summary>
    internal sealed class ComboChain
    {
        public Id Root;
        public Id? Next;
        public int NextIndex;

        /// <summary>链上当前一段是否仍在进行中。</summary>
        public bool Running;

        /// <summary>动作自然结束后链保留到的动作时钟 tick（含）。</summary>
        public long ExpiresAt;
    }

    /// <summary>手感落地：动作时间线对施法管线的接入（手感设计/01 第 3 节、03 第 2 节；ADR-0115）。</summary>
    public sealed partial class CastPipeline : IActionStateQuery
    {
        private readonly struct ComboRedirect
        {
            public int ComboIndex { get; }

            public Id Root { get; }

            public ComboRedirect(int comboIndex, Id root)
            {
                ComboIndex = comboIndex;
                Root = root;
            }
        }

        private readonly struct PendingStart
        {
            /// <summary>true 表示来自取消进入/窗口内连招（连招序号与链根由调用方给定，不再做链重定向）。</summary>
            public bool Explicit { get; }

            public int ComboIndex { get; }

            public Id ChainRoot { get; }

            public ActionCastContext Context { get; }

            public PendingStart(bool isExplicit, int comboIndex, Id chainRoot, ActionCastContext context)
            {
                Explicit = isExplicit;
                ComboIndex = comboIndex;
                ChainRoot = chainRoot;
                Context = context;
            }
        }

        private readonly struct CancelDecision
        {
            public Id SkillId { get; }

            public int ComboIndex { get; }

            public bool IsCombo { get; }

            public CancelDecision(Id skillId, int comboIndex, bool isCombo)
            {
                SkillId = skillId;
                ComboIndex = comboIndex;
                IsCombo = isCombo;
            }
        }

        private TimelineServices? _timeline;
        private readonly Dictionary<Id, ComboChain> _comboChains = new Dictionary<Id, ComboChain>();
        private PendingStart? _pendingStart;

        /// <summary>本次施法请求携带的宽限条件名（带宽限条件的 CastSkillWithContext 设置，调用结束清空）。</summary>
        private IReadOnlyList<Id>? _graceConditions;

        /// <summary>
        /// 手感落地 M3-B（手感设计/01 第 2.4 节）：排队中的施法进入队列那一刻记下的宽限快照。<see cref="Conditions"/> 是发起请求的输入动作声明的条件名；
        /// <see cref="CoversStep7"/> 为真表示"进入队列时这些条件全部满足、且至少一个仅靠宽限满足"，此时宽限窗口剩余多少个 tick 已在那一刻算定，
        /// 按行动者动作时钟记成绝对到期读数 <see cref="ExpiresAtActionTick"/>（含）——顿帧期间动作时钟不走，窗口随之暂停。
        /// 出队执行时快照与实时宽限查询取并集：实时查询仍满足（例如条件又成立了）或快照未过期，步骤 7 都放行；两者都不满足则按原规则拒绝。
        /// </summary>
        private sealed class GraceSnapshot
        {
            public readonly IReadOnlyList<Id> Conditions;
            public readonly bool CoversStep7;
            public readonly long ExpiresAtActionTick;

            public GraceSnapshot(IReadOnlyList<Id> conditions, bool coversStep7, long expiresAtActionTick)
            {
                Conditions = conditions;
                CoversStep7 = coversStep7;
                ExpiresAtActionTick = expiresAtActionTick;
            }
        }

        /// <summary>出队执行的排队请求带着的宽限快照（只在 <see cref="FinishCast"/> 续跑排队请求期间非空，结束清空）。</summary>
        private GraceSnapshot? _graceSnapshot;

        /// <summary>
        /// 为正要进入队列的请求记宽限快照：本次请求没有携带宽限条件、或没有宽限查询时返回 null（排队行为与此前逐位一致）。
        /// </summary>
        private GraceSnapshot? CaptureGraceSnapshot(Id casterId)
        {
            var conditions = _graceConditions;
            var grace = _timeline?.Grace;
            if (conditions == null || grace == null)
            {
                return null;
            }

            var covers = false;
            long expires = 0;
            if (grace.AreAllSatisfied(casterId, conditions))
            {
                var remaining = int.MaxValue;
                for (var i = 0; i < conditions.Count; i++)
                {
                    if (grace.IsInGrace(casterId, conditions[i]))
                    {
                        covers = true;
                        remaining = Math.Min(remaining, grace.RemainingGraceTicks(casterId, conditions[i]));
                    }
                }

                if (covers)
                {
                    expires = ActionNow(casterId) + remaining;
                }
            }

            return new GraceSnapshot(conditions, covers, expires);
        }

        /// <summary>true 时 TryStartCast 在步骤 8 之前返回、Fail 不发事件（取消进入的"先验证再取消"探测，见 TryStartCancelInto）。</summary>
        private bool _probeOnly;

        /// <summary>非空时该施法者的"当前动作正要被取消"，步骤 4 节拍锁不拒绝。</summary>
        private Id? _cancelIntoCaster;

        private int _updateSeq;
        private bool _inUpdate;

        /// <summary>组装期注入时间线的协作者（见 <see cref="TimelineServices"/>）；可重复调用，后一次覆盖前一次。</summary>
        internal void AttachTimelineServices(TimelineServices services)
        {
            _timeline = services ?? throw new ArgumentNullException(nameof(services));
        }

        /// <summary>
        /// 带上下文的施法请求（按下瞬间方向、蓄力按住时长）：与 <see cref="CastSkill"/> 完全一致，只是把上下文交给随后
        /// 开始的时间线动作；非时间线技能忽略上下文。
        /// </summary>
        public CastResult CastSkillWithContext(Id casterId, Id skillId, IReadOnlyList<Id> targets, ActionCastContext context) =>
            CastSkillWithContext(casterId, skillId, targets, context, null);

        /// <summary>
        /// 带宽限条件的施法请求（手感设计/01 第 2.4 节）：<paramref name="graceConditions"/> 是发起这次施法的输入动作声明的宽限条件名
        /// （<c>found.input_action.grace_conditions</c>）。非时间线技能的步骤 7（距离与视线）本应拒绝时，若这些条件全部满足
        /// （<see cref="IGraceQuery.AreAllSatisfied"/>）且至少有一个"当前为假、仅因宽限而满足"，则视为满足；其余步骤不受影响。
        /// 为空或没有宽限查询（<see cref="TimelineServices.Grace"/>）时与四参数重载完全一致。
        /// </summary>
        public CastResult CastSkillWithContext(
            Id casterId, Id skillId, IReadOnlyList<Id> targets, ActionCastContext context, IReadOnlyList<Id>? graceConditions)
        {
            _pendingStart = new PendingStart(false, 0, default, context);
            _graceConditions = graceConditions != null && graceConditions.Count > 0 ? graceConditions : null;
            try
            {
                // 手感落地 M4-G：请求自己携带的单位目标是这次施法的瞄点，宽限条件优先以它求值（没有携带目标的请求不记瞄点，目标由技能的选择链解析，行为不变）。
                if (_graceConditions != null && targets.Count > 0)
                {
                    NoteGraceAim(casterId, skillId, targets[0], null);
                }

                return CastSkill(casterId, skillId, targets);
            }
            finally
            {
                _pendingStart = null;
                _graceConditions = null;
            }
        }

        /// <summary>
        /// 把本次施法请求携带的目标（单位目标或地面落点）连同技能射程交给宽限追踪（<see cref="IGraceAimSink"/>，手感落地 M4-G）：依赖瞄点的宽限条件
        /// （<c>target</c> 分组、框架内置的 <c>event.aim_*</c> 上下文变量）据此求值。宽限查询对象不接收瞄点（第三方实现）或本次没有宽限条件时什么都不做。
        /// </summary>
        private void NoteGraceAim(Id casterId, Id skillId, Id? target, Vec2? point)
        {
            if (_graceConditions == null || !(_timeline?.Grace is IGraceAimSink sink))
            {
                return;
            }

            var range = _defs.TryGetSkillDef(skillId, out var def) ? def.Range : 0;
            if (target.HasValue)
            {
                sink.NoteAim(casterId, GraceAim.OfTarget(target.Value, range));
            }
            else if (point.HasValue)
            {
                sink.NoteAim(casterId, GraceAim.OfPoint(point.Value, range));
            }
        }

        /// <summary>
        /// 步骤 7 的宽限判定（手感设计/01 第 2.4 节）：本次施法携带的宽限条件全部满足、且至少一个仅靠宽限满足（当前为假）时为 true。
        /// 条件当前全为真却仍被步骤 7 拒绝，说明这些条件与被拒的几何无关，不放行。
        /// </summary>
        private bool GraceCoversStep7(Id casterId)
        {
            var conditions = _graceConditions;
            var grace = _timeline?.Grace;
            if (conditions == null || grace == null)
            {
                return false;
            }

            // 手感落地 M3-B：排队请求出队执行时，进入队列那一刻记下的快照仍在动作时钟窗口内则放行（与实时查询取并集）。
            var snapshot = _graceSnapshot;
            if (snapshot != null && snapshot.CoversStep7 && ActionNow(casterId) <= snapshot.ExpiresAtActionTick)
            {
                return true;
            }

            if (!grace.AreAllSatisfied(casterId, conditions))
            {
                return false;
            }

            for (var i = 0; i < conditions.Count; i++)
            {
                if (grace.IsInGrace(casterId, conditions[i]))
                {
                    return true;
                }
            }

            return false;
        }

        // -----------------------------------------------------------------
        // 查询（IActionStateQuery）
        // -----------------------------------------------------------------

        private ActionRun? RunOf(Id unitId) =>
            _casting.TryGetValue(unitId, out var state) ? state.Run : null;

        public ActionState? Current(Id unitId)
        {
            var run = RunOf(unitId);
            if (run == null)
            {
                return null;
            }

            return new ActionState(run.SkillId, run.CastInstanceId, run.Phase, run.Elapsed, run.ComboIndex, run.MotionState);
        }

        public bool IsCancelOpen(Id unitId, ActionClass actionClass)
        {
            var run = RunOf(unitId);
            return run != null && run.Schedule.IsCancelOpen(actionClass, run.Elapsed);
        }

        public bool IsInvulnerable(Id unitId)
        {
            var run = RunOf(unitId);
            return run != null && run.Invulnerable;
        }

        /// <summary>霸体窗口（<c>armor_start</c>～<c>armor_end</c>）：镜像 <see cref="IsInvulnerable"/>，受击裁决经它读取（手感设计/03 第 4 节）。</summary>
        public bool IsSuperArmor(Id unitId)
        {
            var run = RunOf(unitId);
            return run != null && run.SuperArmor;
        }

        /// <summary>格挡窗口（<c>guard_start</c>～<c>guard_end</c>）：受击方防御裁决经它读取（手感设计/03 第 4 节，ADR-0145）。</summary>
        public bool IsGuarding(Id unitId)
        {
            var run = RunOf(unitId);
            return run != null && run.Guarding;
        }

        /// <summary>格挡开始后经过的动作时钟 tick 数（顿帧期间不增长）；不在格挡中为 0。</summary>
        public int GuardElapsedTicks(Id unitId)
        {
            var run = RunOf(unitId);
            return run != null && run.Guarding ? Math.Max(0, run.Elapsed - run.GuardStartedAt) : 0;
        }

        public bool IsActionClockPaused(Id unitId) => _timeline?.Clock != null && _timeline.Clock.IsPaused(unitId);

        /// <summary>
        /// 就绪查询用的"节拍锁剩余"：时间线动作进行中恒视为被锁（<see cref="CastSkill"/> 对时间线动作不排队，一律
        /// <c>ActionLocked</c>），返回极大值；其余动作返回读条/引导剩余。供 <c>SkillHost.GetSkillReadiness</c> 与施法结论对齐。
        /// </summary>
        internal double? GetActionLockRemaining(Id unitId)
        {
            if (!_casting.TryGetValue(unitId, out var state))
            {
                return null;
            }

            return state.Run != null ? double.MaxValue : state.Remaining;
        }

        // -----------------------------------------------------------------
        // 开始
        // -----------------------------------------------------------------

        private long ActionNow(Id actorId) => _timeline?.Clock != null ? _timeline.Clock.ActionTicks(actorId) : _updateSeq;

        /// <summary>
        /// 连招链重定向（手感设计/01 第 3.6 节）：链上一段已自然结束且仍在 <c>combo_reset_ms</c> 之内时，对链根技能的施法请求
        /// 启动链上下一段（并带上递增的连招序号）。窗口内的连招（动作进行中接受 attack 类记录）不经这里，由
        /// <see cref="TryPullIntent"/> 直接启动下一段。
        /// </summary>
        private void ApplyComboRedirect(Id casterId, ref Id skillId, ref SkillDef def, out ComboRedirect? redirect)
        {
            redirect = null;
            if (def.Timeline == null || (_pendingStart.HasValue && _pendingStart.Value.Explicit))
            {
                return;
            }

            if (!_comboChains.TryGetValue(casterId, out var chain) || chain.Running || !chain.Next.HasValue || !chain.Root.Equals(skillId))
            {
                return;
            }

            if (ActionNow(casterId) > chain.ExpiresAt)
            {
                return;
            }

            if (!_defs.TryGetSkillDef(chain.Next.Value, out var nextDef) || nextDef.Timeline == null)
            {
                return;
            }

            redirect = new ComboRedirect(chain.NextIndex, chain.Root);
            skillId = chain.Next.Value;
            def = nextDef;
        }

        private static double FeelNumber(ResolvedFeel feel, string field, double fallback) =>
            feel.Judging.TryGetNumber(field, out var v) ? v : fallback;

        private CastResult EnterTimeline(
            Id casterId, Id skillId, SkillDef def, IReadOnlyList<Id> explicitTargets,
            IReadOnlyList<(Id PowerType, double Amount)> modifiedCost, Id? presetCastInstanceId, ComboRedirect? redirect)
        {
            var tl = def.Timeline!;
            var step = _options.ActionStepSeconds;
            var castInstanceId = presetCastInstanceId ?? NextCastInstanceId();

            var pending = _pendingStart;
            _pendingStart = null;
            var context = pending.HasValue ? pending.Value.Context : default;
            var comboIndex = 0;
            var chainRoot = skillId;
            if (pending.HasValue && pending.Value.Explicit)
            {
                comboIndex = pending.Value.ComboIndex;
                chainRoot = pending.Value.ChainRoot;
            }
            else if (redirect.HasValue)
            {
                comboIndex = redirect.Value.ComboIndex;
                chainRoot = redirect.Value.Root;
            }

            // 动作开始快照（手感设计/05 第 8 节"正在进行的动作沿用其开始时的快照"）。
            var feel = _timeline?.Feel?.BeginAction(casterId, castInstanceId, tl.FeelRef);
            var scaling = TimelineScaling.Identity;
            var comboResetTicks = 0;
            if (feel != null)
            {
                scaling = new TimelineScaling(
                    FeelNumber(feel, FeelFieldNames.PhaseScaleStartup, 1.0),
                    FeelNumber(feel, FeelFieldNames.PhaseScaleActive, 1.0),
                    FeelNumber(feel, FeelFieldNames.PhaseScaleRecovery, 1.0),
                    FeelNumber(feel, FeelFieldNames.CancelWindowScale, 1.0),
                    FeelNumber(feel, FeelFieldNames.ComboWindowScale, 1.0),
                    Math.Max(FeelNumber(feel, FeelFieldNames.MinActionMs, 0.0), _options.MinActionSeconds * 1000.0));
                comboResetTicks = Foundation.Feel.FeelCalibration.MillisecondsToTicks(FeelNumber(feel, FeelFieldNames.ComboResetMs, 0.0), step);
            }
            else if (_options.MinActionSeconds > 0)
            {
                scaling = new TimelineScaling(1, 1, 1, 1, 1, _options.MinActionSeconds * 1000.0);
            }

            // 速率重映射（手感设计/01 第 3.5 节）：系数 = 经 SpellMod 与急速折算后的动作时长 / 声明时长。
            // 复用既有 ComputeCastTime（含 HasteAffectsActionTime 开关、MaxHastePct 与 MinActionSeconds 下限），不另起一套。
            var factor = def.CastTime > 0 ? ComputeCastTime(casterId, def) / def.CastTime : 1.0;
            var schedule = TimelineSchedule.Build(tl, step, factor, scaling);

            var chargeRatio = 0.0;
            if (tl.Charge != null && context.HeldTicks > 0 && tl.Charge.MaxMs > tl.Charge.MinMs)
            {
                var heldMs = context.HeldTicks * step * 1000.0;
                chargeRatio = Math.Min(1.0, Math.Max(0.0, (heldMs - tl.Charge.MinMs) / (tl.Charge.MaxMs - tl.Charge.MinMs)));
            }

            var totalSeconds = schedule.TotalTicks * step;
            _bus.Enqueue(new SkillCastStartEvent(casterId, skillId, totalSeconds, castInstanceId));

            // 目标辅助（手感设计/02 第 5 节，缺省关闭）：动作被接受时解析一次，朝向修正当场落地，辅助目标与距离缩放交给位移段快照。
            var assist = ResolveTargetAssist(casterId, castInstanceId, def, tl, feel);

            var run = new ActionRun
            {
                CastInstanceId = castInstanceId,
                SkillId = skillId,
                Def = def,
                Timeline = tl,
                Schedule = schedule,
                ChainRoot = chainRoot,
                ComboIndex = comboIndex,
                ChargeRatio = chargeRatio,
                ChargeValueScale = tl.Charge != null ? tl.Charge.ValueScaleAt(chargeRatio) : 1.0,
                Direction = context.Direction,
                LastClock = _timeline?.Clock != null ? _timeline.Clock.ActionTicks(casterId) : 0,
                StartSeq = _inUpdate ? _updateSeq : _updateSeq + 1,
                ComboResetTicks = comboResetTicks,
                MotionState = tl.Motion.HasValue
                    ? BuildMotionState(casterId, tl.Motion.Value, schedule, feel, context, explicitTargets, assist)
                    : (ActionMotionState?)null,
                PrevPosition = _units.GetPosition(casterId),
                PrevFacing = _units.GetFacing(casterId),
                HasReleaseMarker = HasMarker(tl, "release"),
                AssistTarget = assist.HasValue ? assist.Value.Outcome.TargetId : (Id?)null,
            };

            var state = new CastState
            {
                SkillId = skillId,
                Def = def,
                Targets = explicitTargets,
                TargetCoefficients = null,
                IsChannel = false,
                Remaining = totalSeconds,
                TickInterval = 0,
                ModifiedCost = modifiedCost,
                CastTimeSeconds = totalSeconds,
                CastInstanceId = castInstanceId,
                Run = run,
            };

            // 连招链：声明了 combo 的动作建立（或延续）链，没有 combo 的动作结束链（链上最后一段/非连招动作）。
            if (tl.Combo != null)
            {
                _comboChains[casterId] = new ComboChain
                {
                    Root = chainRoot,
                    Next = tl.Combo.Next,
                    NextIndex = comboIndex + 1,
                    Running = true,
                    ExpiresAt = long.MaxValue,
                };
            }
            else
            {
                _comboChains.Remove(casterId);
            }

            _casting[casterId] = state;

            // 资源与冷却的"动作开始"时刻（缺省）；其余时刻由事件推进到达时触发。
            if (tl.CostAt == TimelineCostAt.Commit)
            {
                PayRunCost(casterId, state, run);
            }

            if (tl.CooldownAt == TimelineCooldownAt.Commit)
            {
                StartRunCooldown(casterId, state, run);
            }

            _bus.Enqueue(new ActionStartedEvent(
                casterId, skillId, castInstanceId, comboIndex, schedule.TotalTicks, chargeRatio, IsAttackAction(def, tl),
                schedule.StartupRate, schedule.ActiveRate, schedule.RecoveryRate));
            if (assist.HasValue)
            {
                _bus.Enqueue(new ActionTargetAssistedEvent(
                    casterId, castInstanceId, assist.Value.Outcome.TargetId, assist.Value.Outcome.FacingDeltaDeg, assist.Value.Outcome.DistanceAdjust));
            }

            // 开始 tick（动作时间 0）：tick 0 的调度事件（进入首个相位、tick 0 的标记）立即触发。
            // 前摇为 0 的 continuous 动作：tick 0 就是判定相的第一个 tick（时间范围 (-1, 0]，只有终点采样落在判定相内）。
            BeginContinuousSpan(casterId, run, -1, 0);
            ProcessRunEvents(casterId, state, run);
            return CastResult.Ok(castInstanceId);
        }

        /// <summary>
        /// 动作被接受时落定位移段快照（手感设计/02 第 4 节；运动仲裁器只消费它）：窗口取 <c>motion_start</c>/<c>motion_end</c> 标记换算后的 tick
        /// （缺失分别取动作起点/终点——校验规则已要求成对声明）；距离经标定从身高倍数换算为世界单位（无手感解析器时原样使用）；
        /// 方向在此落定（<c>facing</c> = 当前朝向；<c>input_snapshot</c> = 按下瞬间输入方向，无输入回落朝向；<c>toward_target</c> = 指向显式目标，
        /// 无目标回落朝向）；<c>toward_target</c>/<c>charge</c> 的目标取显式目标，没有显式目标时取目标辅助挑出的目标（<see cref="ResolveTargetAssist"/>）。
        /// </summary>
        private ActionMotionState BuildMotionState(
            Id casterId, ActionMotion motion, TimelineSchedule schedule, ResolvedFeel? feel, ActionCastContext context,
            IReadOnlyList<Id> explicitTargets, AssistResult? assist = null)
        {
            var start = schedule.FirstTickOf("motion_start") ?? 0;
            var end = schedule.FirstTickOf("motion_end") ?? schedule.TotalTicks;
            var calibration = _timeline?.Feel?.Calibration;
            var distanceWorld = calibration != null ? calibration.ToAbsolute(FeelUnit.BodyHeights, motion.Distance) : motion.Distance;
            if (assist.HasValue && assist.Value.Outcome.DistanceAdjust != 0.0)
            {
                // close_distance：位移距离缩放到判定形状恰好覆盖目标（不超过声明距离、不为负，见 TargetAssistEvaluator）。
                distanceWorld = Math.Max(0.0, distanceWorld + assist.Value.Outcome.DistanceAdjust);
            }

            var facing = _units.GetFacing(casterId);
            var direction = new Vec2(Math.Cos(facing), Math.Sin(facing));
            Id? targetId = null;
            var towardTarget = motion.Kind == ActionMotionKind.Charge || motion.Direction == ActionMotionDirection.TowardTarget;
            if (towardTarget && explicitTargets.Count > 0)
            {
                targetId = explicitTargets[0];
            }
            else if (towardTarget && assist.HasValue)
            {
                // 没有显式目标时 toward_target/charge 用目标辅助挑出的目标（手感设计/02 第 5 节）。
                targetId = assist.Value.Outcome.TargetId;
            }

            if (motion.Direction == ActionMotionDirection.InputSnapshot && context.Direction.HasValue && context.Direction.Value.Length > 1e-9)
            {
                var d = context.Direction.Value;
                var len = d.Length;
                direction = new Vec2(d.X / len, d.Y / len);
            }
            else if (motion.Direction == ActionMotionDirection.TowardTarget && targetId.HasValue)
            {
                var delta = _units.GetPosition(targetId.Value) - _units.GetPosition(casterId);
                var len = delta.Length;
                if (len > 1e-9)
                {
                    direction = new Vec2(delta.X / len, delta.Y / len);
                }
            }

            var stopDistance = 0.0;
            if (motion.Kind == ActionMotionKind.Charge && feel != null && feel.Judging.TryGetNumber(FeelFieldNames.StopDistance, out var stop))
            {
                stopDistance = stop;
            }

            return new ActionMotionState(motion, distanceWorld, start, end, direction, targetId, stopDistance);
        }

        private void PayRunCost(Id casterId, CastState state, ActionRun run)
        {
            if (run.CostPaid)
            {
                return;
            }

            run.CostPaid = true;
            DeductResources(casterId, run.Def.Id, state.ModifiedCost);
        }

        private void StartRunCooldown(Id casterId, CastState state, ActionRun run)
        {
            if (run.CooldownStarted)
            {
                return;
            }

            run.CooldownStarted = true;
            StartCooldownAndGcd(casterId, run.Def);
        }

        // -----------------------------------------------------------------
        // 推进
        // -----------------------------------------------------------------

        private bool IsLive(Id casterId, CastState state) =>
            _casting.TryGetValue(casterId, out var current) && ReferenceEquals(current, state);

        private void AdvanceTimelineRun(Id casterId, CastState state)
        {
            var run = state.Run!;

            // 开始 tick 本身不推进（动作时间 0 就是开始那一 tick）：只记下当前动作时钟读数，下一次 Update 起才有增量。
            if (run.StartSeq >= _updateSeq)
            {
                if (_timeline?.Clock != null)
                {
                    run.LastClock = _timeline.Clock.ActionTicks(casterId);
                }

                return;
            }

            int delta;
            var clock = _timeline?.Clock;
            if (clock != null)
            {
                var now = clock.ActionTicks(casterId);
                delta = (int)Math.Max(0, now - run.LastClock);
                run.LastClock = now;
            }
            else
            {
                delta = 1;
            }

            if (delta == 0)
            {
                // 动作时钟被暂停（顿帧）：时间线不推进、不拉取缓冲（手感设计/01 第 2.3 节第 2 点）。
                return;
            }

            var previousElapsed = run.Elapsed;
            run.Elapsed += delta;
            state.Remaining = Math.Max(0, (run.Schedule.TotalTicks - run.Elapsed) * _options.ActionStepSeconds);

            // continuous 命中：本次推进跨过的动作时间区间 (previousElapsed, Elapsed] 内落在判定相的部分逐段采样解析
            // （采样在 ProcessRunEvents 里与调度事件按时间顺序交错执行）。
            BeginContinuousSpan(casterId, run, previousElapsed, run.Elapsed);
            if (!ProcessRunEvents(casterId, state, run))
            {
                return;
            }

            run.PrevPosition = _units.GetPosition(casterId);
            run.PrevFacing = _units.GetFacing(casterId);

            if (run.Elapsed >= run.Schedule.TotalTicks)
            {
                FinishRun(casterId, state, run);
                return;
            }

            TryPullIntent(casterId, state, run);
        }

        /// <summary>按时间顺序触发 <c>run.Elapsed</c> 及之前的全部调度事件（一次推进跨过多个标记时一个不漏）。返回动作是否仍在进行。</summary>
        private bool ProcessRunEvents(Id casterId, CastState state, ActionRun run)
        {
            var events = run.Schedule.Events;
            while (run.NextEvent < events.Count && events[run.NextEvent].Tick <= run.Elapsed)
            {
                // 先把严格早于该事件的 continuous 采样做完（命中事件先于其后的相位切换/标记事件发布）。
                if (!EvaluateSamplesBefore(casterId, state, run, events[run.NextEvent].Tick, inclusive: false))
                {
                    return false;
                }

                var ev = events[run.NextEvent++];
                if (!run.ActiveEntered && ev.Tick >= run.Schedule.StartupTicks)
                {
                    EnterActiveHooks(casterId, state, run);
                    if (!IsLive(casterId, state))
                    {
                        return false;
                    }
                }

                DispatchRunEvent(casterId, state, run, ev);
                if (!IsLive(casterId, state))
                {
                    return false;
                }
            }

            if (!EvaluateSamplesBefore(casterId, state, run, double.PositiveInfinity, inclusive: true))
            {
                return false;
            }

            if (!run.ActiveEntered && run.Elapsed >= run.Schedule.StartupTicks)
            {
                EnterActiveHooks(casterId, state, run);
            }

            return IsLive(casterId, state);
        }

        private void EnterActiveHooks(Id casterId, CastState state, ActionRun run)
        {
            run.ActiveEntered = true;
            if (run.Timeline.CostAt == TimelineCostAt.Active)
            {
                PayRunCost(casterId, state, run);
            }

            if (run.Timeline.CooldownAt == TimelineCooldownAt.Active)
            {
                StartRunCooldown(casterId, state, run);
            }
        }

        private void DispatchRunEvent(Id casterId, CastState state, ActionRun run, TimelineEvent ev)
        {
            switch (ev.Kind)
            {
                case TimelineEventKind.PhaseEnter:
                    run.Phase = ev.Phase;
                    _bus.Enqueue(new ActionPhaseChangedEvent(casterId, run.CastInstanceId, ev.Phase));
                    return;

                case TimelineEventKind.CancelOpen:
                case TimelineEventKind.CancelClose:
                case TimelineEventKind.ComboOpen:
                case TimelineEventKind.ComboClose:
                    _bus.Enqueue(new ActionMarkerEvent(casterId, run.CastInstanceId, ev.Name, ev.Args));
                    return;
            }

            switch (ev.Name)
            {
                case TimelineDef.HitMarker:
                    HandleHitMarker(casterId, state, run, ev);
                    return;
                case "invuln_start":
                    run.Invulnerable = true;
                    break;
                case "invuln_end":
                    run.Invulnerable = false;
                    break;
                case "armor_start":
                    run.SuperArmor = true;
                    break;
                case "armor_end":
                    run.SuperArmor = false;
                    break;
                case "guard_start":
                    run.Guarding = true;
                    run.GuardStartedAt = run.Elapsed;
                    break;
                case "guard_end":
                    run.Guarding = false;
                    break;
                case "motion_start":
                    run.MotionOpen = true;
                    break;
                case "motion_end":
                    run.MotionOpen = false;
                    break;
                case "release":
                    _bus.Enqueue(new ActionMarkerEvent(casterId, run.CastInstanceId, ev.Name, ev.Args));
                    ReleaseProjectiles(casterId, state, run, ev);
                    return;
            }

            _bus.Enqueue(new ActionMarkerEvent(casterId, run.CastInstanceId, ev.Name, ev.Args));
        }

        private void HandleHitMarker(Id casterId, CastState state, ActionRun run, TimelineEvent ev)
        {
            var customResolver = _timeline?.HitResolver;

            // continuous 命中：hit 标记只是时间线上的记号（照发 action.marker），结算由逐 tick 采样负责；
            // 资源的 first_hit 扣除也留到第一次真正命中时（见 EvaluateContinuous）。设置了自定义命中解析钩子时钩子接管，不走这条。
            if (customResolver == null && run.Timeline.HitPolicy == TimelineHitPolicy.Continuous && IsSpatialHit(run))
            {
                _bus.Enqueue(new ActionMarkerEvent(casterId, run.CastInstanceId, ev.Name, ev.Args));
                return;
            }

            NoteFirstHit(casterId, state, run, ev.Args);

            _bus.Enqueue(new ActionMarkerEvent(casterId, run.CastInstanceId, ev.Name, ev.Args));

            var segment = 0;
            if (ev.Args.TryGetValue("segment", out var segText))
            {
                int.TryParse(segText, NumberStyles.Integer, CultureInfo.InvariantCulture, out segment);
            }

            if (customResolver == null && IsSpatialHit(run))
            {
                SpatialMarkerHit(casterId, state, run, segment);
                return;
            }

            var resolver = customResolver ?? DefaultHitResolver;
            var settlement = new TimelineSettlement(this, casterId, run, state.Targets, segment);
            resolver.ResolveHit(new TimelineHitContext(
                casterId, run.Def, run.CastInstanceId, run.ComboIndex, segment, state.Targets, settlement));
        }

        /// <summary>首次命中：派生 <c>cost</c> 标记（<c>cost_at: first_hit</c>，手感设计/01 第 3.3 节），扣费发生在首个命中结算之前。</summary>
        private void NoteFirstHit(Id casterId, CastState state, ActionRun run, IReadOnlyDictionary<string, string> args)
        {
            if (run.FirstHitSeen)
            {
                return;
            }

            run.FirstHitSeen = true;
            if (run.Timeline.CostAt == TimelineCostAt.FirstHit)
            {
                PayRunCost(casterId, state, run);
                _bus.Enqueue(new ActionMarkerEvent(casterId, run.CastInstanceId, "cost", args));
            }
        }

        private static readonly ITimelineHitResolver DefaultHitResolver = new InstantSettlementHitResolver();

        /// <summary>命中解析器的结算出口：复用 instant 模式的目标选择链与效果结算。</summary>
        private sealed class TimelineSettlement : ITimelineSettlement
        {
            private readonly CastPipeline _owner;
            private readonly Id _casterId;
            private readonly ActionRun _run;
            private readonly SkillDef _def;
            private readonly IReadOnlyList<Id> _explicit;
            private readonly int _segment;
            private IReadOnlyDictionary<Id, double>? _coefficients;

            public TimelineSettlement(CastPipeline owner, Id casterId, ActionRun run, IReadOnlyList<Id> explicitTargets, int segment)
            {
                _owner = owner;
                _casterId = casterId;
                _run = run;
                _def = run.Def;
                _explicit = explicitTargets;
                _segment = segment;
            }

            public IReadOnlyList<Id> ResolveTargets()
            {
                var resolution = _owner._targetHost.ResolveWithCoefficients(_def.TargetShapeRef, _casterId);
                var ids = new List<Id>(resolution.Targets.Count);
                Dictionary<Id, double>? coefficients = null;
                foreach (var (target, coefficient) in resolution.Targets)
                {
                    ids.Add(target);
                    if (coefficient != 1.0)
                    {
                        coefficients ??= new Dictionary<Id, double>();
                        coefficients[target] = coefficient;
                    }
                }

                _coefficients = coefficients;
                return ids;
            }

            public void SettleOn(IReadOnlyList<Id> targets)
            {
                var live = _owner.FilterDestroyedTargets(targets);
                if (live.Count > 0 && _owner._options.SpatialRangeHitWindow && _def.Range > 0)
                {
                    // 命中窗口的射程门（SkillOptions.SpatialRangeHitWindow）：instant 结算与自定义命中钩子的结算同样受它约束。
                    var casterPos = _owner._units.GetPosition(_casterId);
                    var inRange = new List<Id>(live.Count);
                    for (var i = 0; i < live.Count; i++)
                    {
                        if (_owner.WithinHitWindowRange(_casterId, _def, casterPos, live[i]))
                        {
                            inRange.Add(live[i]);
                        }
                    }

                    live = inRange;
                }

                if (live.Count == 0)
                {
                    return;
                }

                // 手感落地：instant 路径的时间线结算同样发 combat.hit_confirmed、做无敌前置检查（手感设计/03 第 2.3/2.4 节，
                // 两种结算路径统一）；不做命中集合去重（调用方——自定义钩子或 instant 缺省——自己决定何时结算）。
                _owner.SettleTimelineBatch(
                    _casterId, _run, live, _coefficients, _segment, _owner.PoseGeometry(_casterId, closest: false),
                    _owner.EffectSubsetFor(_run));
            }

            public void SettleInstant()
            {
                IReadOnlyList<Id> targets;
                if (_explicit.Count > 0)
                {
                    targets = _owner._targetHost.FilterExplicitTargets(_def.TargetShapeRef, _casterId, _explicit);
                    _coefficients = null;
                }
                else
                {
                    targets = ResolveTargets();
                }

                SettleOn(targets);
            }
        }

        // -----------------------------------------------------------------
        // 结束与终止
        // -----------------------------------------------------------------

        private void FinishRun(Id casterId, CastState state, ActionRun run)
        {
            _casting.Remove(casterId);

            // cooldown_at: finish 在自然结束时起算（其余时刻在开始/进入判定相时已起算）。
            if (run.Timeline.CooldownAt == TimelineCooldownAt.Finish)
            {
                StartRunCooldown(casterId, state, run);
            }

            if (_comboChains.TryGetValue(casterId, out var chain))
            {
                chain.Running = false;
                if (chain.Next.HasValue && run.ComboResetTicks > 0)
                {
                    chain.ExpiresAt = ActionNow(casterId) + run.ComboResetTicks;
                }
                else
                {
                    _comboChains.Remove(casterId);
                }
            }

            _timeline?.Feel?.EndAction(run.CastInstanceId);

            _bus.Enqueue(new ActionFinishedEvent(casterId, run.CastInstanceId));
            _bus.Enqueue(new SkillCastSuccessEvent(
                casterId, state.SkillId, state.Targets, isInstant: false, castTimeSeconds: state.CastTimeSeconds,
                castInstanceId: state.CastInstanceId));
        }

        /// <summary>时间线动作未自然结束即终止（<see cref="TerminateCastWithReason"/> 调用）：收尾冷却/链/手感快照并发 <c>action.cancelled</c>。</summary>
        private void OnRunCancelled(Id casterId, CastState state, ActionCancelReason reason, Id? nextSkillId)
        {
            var run = state.Run!;
            run.Invulnerable = false;
            run.SuperArmor = false;
            run.Guarding = false;
            run.MotionOpen = false;

            // cooldown_at: finish 的动作被取消/打断时在终止那一刻起算（不让"取消"成为绕开冷却的手段）；清空（场景/模式切换）不起算。
            if (run.Timeline.CooldownAt == TimelineCooldownAt.Finish && reason != ActionCancelReason.Cleared)
            {
                StartRunCooldown(casterId, state, run);
            }

            // 连招链：硬直、死亡、清空重置；取消进入由随后开始的新动作接管链（开始时覆盖或清除）。
            if (reason != ActionCancelReason.CancelInto)
            {
                _comboChains.Remove(casterId);
            }

            _timeline?.Feel?.EndAction(run.CastInstanceId);
            _bus.Enqueue(new ActionCancelledEvent(casterId, run.CastInstanceId, reason, nextSkillId));
        }

        /// <summary>清空全部进行中的时间线动作与连招链（时间模型切换，reason: cleared）。</summary>
        private void ClearTimelineRuns()
        {
            List<Id>? keys = null;
            foreach (var kv in _casting)
            {
                if (kv.Value.Run != null)
                {
                    keys ??= new List<Id>();
                    keys.Add(kv.Key);
                }
            }

            _comboChains.Clear();
            if (keys == null)
            {
                return;
            }

            foreach (var key in keys)
            {
                var state = _casting[key];
                _casting.Remove(key);
                TerminateCastWithReason(key, state, key, null, 0, ActionCancelReason.Cleared, null, "CLEARED");
            }
        }

        /// <summary>
        /// 移动输入到来（手感设计/01 第 3.4 节）：时间线动作进行中，只有 <c>move</c> 类取消窗口此刻打开才生效（取消进入，无后续动作），
        /// 否则忽略。移动轴不入缓冲，窗口一开当 tick 即生效；动作时钟被顿帧暂停期间不生效。
        /// </summary>
        public void NotifyMoveIntent(Id unitId)
        {
            if (!_casting.TryGetValue(unitId, out var state) || state.Run == null)
            {
                return;
            }

            var run = state.Run;
            if (IsActionClockPaused(unitId) || !run.Schedule.IsCancelOpen(ActionClass.Move, run.Elapsed))
            {
                return;
            }

            _casting.Remove(unitId);
            TerminateCastWithReason(unitId, state, unitId, null, 0, ActionCancelReason.CancelInto, null, "CANCELLED");
        }

        /// <summary>
        /// 终止行动者进行中的时间线动作（受击硬直、清空等外部终止入口）：发 <c>action.cancelled{reason}</c> 与
        /// <c>skill.cast_interrupted</c>；没有时间线动作时空操作。
        /// </summary>
        public void CancelAction(Id unitId, ActionCancelReason reason)
        {
            if (!_casting.TryGetValue(unitId, out var state) || state.Run == null)
            {
                return;
            }

            _casting.Remove(unitId);
            var code = reason == ActionCancelReason.CancelInto ? "CANCELLED" : reason == ActionCancelReason.Cleared ? "CLEARED" : null;
            TerminateCastWithReason(unitId, state, unitId, null, 0, reason, null, code);
        }

        // -----------------------------------------------------------------
        // 取消与连招（拉取缓冲）
        // -----------------------------------------------------------------

        private CancelDecision? Classify(Id casterId, ActionRun run, BufferedIntent record)
        {
            var elapsed = run.Elapsed;

            // 窗口内连招：attack 类记录在连招窗口内启动 next（携带 comboIndex + 1），不需要输入动作→技能映射。
            if (record.Class == ActionClass.Attack && run.Timeline.Combo != null && run.Schedule.IsComboOpen(elapsed))
            {
                return new CancelDecision(run.Timeline.Combo.Next, run.ComboIndex + 1, true);
            }

            // 取消进入：该类别的取消窗口此刻打开，且输入动作能映射到技能。
            if (run.Schedule.IsCancelOpen(record.Class, elapsed)
                && _timeline?.Binding != null
                && _timeline.Binding.TryResolveSkill(casterId, record, out var skillId))
            {
                return new CancelDecision(skillId, 0, false);
            }

            return null;
        }

        /// <summary>
        /// 这条记录是否永远接不了（M4 清扫）：既不能走连招（只有带 <c>combo</c> 块的动作里的 attack 类记录不需要映射），也没有输入动作 → 技能映射能给出技能。
        /// 拉取时先把它们剔出候选，使一条永远接不了的记录不在过期前挡住次优先级候选；窗口此刻没开不算"永远"（窗口可能稍后打开），仍按最前候选处理。
        /// </summary>
        private bool IsNeverAcceptable(Id casterId, ActionRun run, BufferedIntent record)
        {
            if (record.Class == ActionClass.Attack && run.Timeline.Combo != null)
            {
                return false;
            }

            return _timeline?.Binding == null || !_timeline.Binding.TryResolveSkill(casterId, record, out _);
        }

        private void TryPullIntent(Id casterId, CastState state, ActionRun run)
        {
            var input = _timeline?.Input;
            if (input == null)
            {
                return;
            }

            if (!input.TryConsume(casterId, rec => Classify(casterId, run, rec).HasValue, rec => IsNeverAcceptable(casterId, run, rec), out var record))
            {
                return;
            }

            var decision = Classify(casterId, run, record);
            if (!decision.HasValue)
            {
                input.ReportRejected(casterId, record.ActionId, "NOT_ACCEPTED", true);
                return;
            }

            var d = decision.Value;
            var context = new ActionCastContext(
                record.DirectionSnapshot, record.HoldState == BufferHoldState.HoldReleased ? record.HeldTicks : 0);

            // 先验证再取消：新动作能开始才终止当前动作（验证失败时当前动作原样继续，记录按原因分流）。
            _cancelIntoCaster = casterId;
            CastResult probe;
            _probeOnly = true;
            try
            {
                probe = TryStartCast(casterId, d.SkillId, Array.Empty<Id>());
            }
            finally
            {
                _probeOnly = false;
            }

            if (!probe.Success)
            {
                _cancelIntoCaster = null;
                input.ReportRejected(casterId, record.ActionId, probe.Reason.ToString(), IsTimeSolvable(casterId, d.SkillId, probe.Reason, record));
                return;
            }

            _casting.Remove(casterId);
            TerminateCastWithReason(
                casterId, state, casterId, null, 0, ActionCancelReason.CancelInto, d.SkillId, "CANCELLED");

            // 接受时朝向对齐（M4 清扫，手感设计/01 第 2.3 节 face_on_accept）：与缓冲出口（BufferedActionIntentSink）同一口径——
            // 记录要求对齐且带按下瞬间方向快照时，在取消进入被接受的那一刻把朝向瞬时对齐到该方向，新动作的位姿快照据此取朝向。
            AlignFacingOnAccept(casterId, record);

            _pendingStart = new PendingStart(true, d.ComboIndex, run.ChainRoot, context);
            try
            {
                var started = TryStartCast(casterId, d.SkillId, Array.Empty<Id>());
                if (!started.Success)
                {
                    _diagnostics.Warn(
                        $"取消进入验证通过但开始失败（casterId=\"{casterId}\", skillId=\"{d.SkillId}\", reason={started.Reason}），当前动作已被取消");
                }
            }
            finally
            {
                _pendingStart = null;
                _cancelIntoCaster = null;
            }
        }

        private void AlignFacingOnAccept(Id casterId, BufferedIntent record)
        {
            if (!record.FaceOnAccept || !record.DirectionSnapshot.HasValue)
            {
                return;
            }

            if (_units is IUnitFacingWriter writer)
            {
                var d = record.DirectionSnapshot.Value;
                writer.SetFacing(casterId, Math.Atan2(d.Y, d.X));
            }
            else
            {
                _diagnostics.Warn(
                    $"取消进入要求接受时朝向对齐（actor=\"{casterId}\"），但 IUnitAccess 实现没有提供 IUnitFacingWriter，朝向未改动");
            }
        }

        /// <summary>
        /// 管线拒绝原因是否"时间可解"（手感设计/01 第 2.3 节第 4 点）：<c>ACTION_LOCKED</c>、<c>GCD_ACTIVE</c>、<c>BUSY</c>，
        /// 以及剩余冷却不超过记录剩余缓冲的 <c>ON_COOLDOWN</c>（剩余缓冲按动作时钟与 <see cref="BufferedIntent.ExpiresAtActionTime"/> 计）。
        /// </summary>
        private bool IsTimeSolvable(Id casterId, Id skillId, CastFailureReason reason, BufferedIntent record)
        {
            switch (reason)
            {
                case CastFailureReason.ActionLocked:
                case CastFailureReason.GcdActive:
                case CastFailureReason.Busy:
                    return true;
                case CastFailureReason.OnCooldown:
                    if (_timeline?.Clock == null)
                    {
                        return false;
                    }

                    var remainingBuffer = (record.ExpiresAtActionTime - _timeline.Clock.ActionTicks(casterId)) * _options.ActionStepSeconds;
                    return _cooldowns.GetSkillCooldownRemaining(casterId, skillId) <= remainingBuffer;
                default:
                    return false;
            }
        }
    }
}
