using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Core.Rules.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;

namespace Presentation.FeedbackBinder.Core
{
    /// <summary>顿帧期间被冻结的表现单位登记（手感设计/07 第 5 节）：渲染 rig/粒子宿主按它查询"我这个单位现在是否被冻、冻哪些层"。</summary>
    public sealed class ImpactFreezeRegistry
    {
        private readonly Dictionary<Id, ImpactFreezeLayers> _frozen = new Dictionary<Id, ImpactFreezeLayers>();

        public int FrozenCount => _frozen.Count;

        public bool IsFrozen(Id unitId) => _frozen.ContainsKey(unitId);

        public bool TryGetLayers(Id unitId, out ImpactFreezeLayers layers) => _frozen.TryGetValue(unitId, out layers);

        internal void Freeze(IReadOnlyList<Id> unitIds, ImpactFreezeLayers layers)
        {
            foreach (var id in unitIds) _frozen[id] = layers;
        }

        internal void Release(IReadOnlyList<Id> unitIds)
        {
            foreach (var id in unitIds) _frozen.Remove(id);
        }
    }

    /// <summary>
    /// 打击反馈包流水线（手感设计/07）：纯逻辑、无渲染依赖，把一个 tick 内到达的命中收成一批，按
    /// "选变体 → 缩放幅度 → 映射音效层 → 同 tick 合并镜头冲击 → 限频限数"算出表现计划（<see cref="ImpactBatch"/>），
    /// 由 <c>FeedbackBinder</c> 落到 <see cref="IFeedbackSink"/>。
    /// <para>
    /// 生命周期：<see cref="Offer"/> 收命中 → <see cref="Flush"/> 出批（<c>FeedbackBinder</c> 在 <c>sim.tick_finished</c> 与
    /// <c>Update</c> 时调用）。时钟：收到过 <c>sim.tick_finished</c> 后以 <c>tickIndex × StepSeconds</c> 为准（模拟时间，可复现），
    /// 此前以 <see cref="Advance"/> 累计的帧时间为准。
    /// </para>
    /// <para>
    /// 判断记录：
    /// </para>
    /// <para>
    /// 1) 反馈包引用先取攻击方手感的 <c>impact_profile_ref</c>，攻击方没有再取受击方的——07 第 1 节写"受击方"，但该字段在登记里
    /// 是武器为主合成（"这把武器打出去的东西"，FeelFields 判断记录 2），受击生物的手感表通常只有预设值；两边都取、攻击方优先，
    /// 既满足设计文字（受击方可提供）又让武器反馈包生效。
    /// </para>
    /// <para>
    /// 2) 镜头冲击的幅度基数取攻击方（武器）的 <c>camera_impulse_gain</c>；上限、最小间隔、距离衰减、玩家强度设置属于"这台镜头"，
    /// 取镜头拥有者（<see cref="ImpactOptions.CameraOwnerResolver"/>）的手感，没有拥有者时取本批第一个命中的攻击方手感。
    /// </para>
    /// <para>
    /// 3) 同 tick 合并：幅度取各命中幅度（已乘玩家强度）的最大值，再 <c>min(max, shake_cap)</c>；方向取"幅度加权的方向和"的单位化，
    /// 全零则无方向。<c>impulse_min_interval_ms</c> 内的后续批整批丢弃镜头内容（含震屏档），不影响其它表现。
    /// </para>
    /// <para>
    /// 4) 音效限数：本批前 <c>min(MaxImpactsPerTick, 第一个攻击方的 sfx_max_concurrent)</c> 个命中播音效，其余静默（07 第 3 节）；
    /// <c>sfx_max_concurrent</c> 在此作为"每批并发上限"落地，不是跨 tick 的活跃声部计数——同层活跃并发仍由 <c>SfxPlayer</c>
    /// 的层名额（ADR-0105）负责。回避/挥空结局不播 <c>impact</c> 层（07 第 7 节第 6 条），由流水线强制而不依赖数据自觉。
    /// </para>
    /// </summary>
    public sealed class ImpactPipeline
    {
        private sealed class Candidate
        {
            public ImpactPlan Plan = null!;
            public ImpactFeel? AttackerFeel;
            public double Magnitude;
            public Vec2 Direction;
            public double DecayMs;
            public Id? ShakeProfile;
            public bool HasCamera;
        }

        private sealed class RawHitstop
        {
            public bool IsStart;
            public IReadOnlyList<Id> UnitIds = Array.Empty<Id>();
            public int Ticks;
            public Id AttackInstanceId;
        }

        private static readonly ImpactVariant DefaultWhiffVariant = new ImpactVariant(
            ImpactProfile.FallbackClass, ImpactOutcome.Whiff, null, null,
            new[] { new ImpactSfxSpec(SfxFeelLayer.Whiff, null) }, null, null, null, ImpactFreezeLayers.Default, null);

        private readonly ImpactOptions _options;
        private readonly IPresentationDiagnostics _diagnostics;
        private readonly List<Candidate> _group = new List<Candidate>();
        private readonly List<RawHitstop> _hitstops = new List<RawHitstop>();

        /// <summary>手感落地 M3-C：已出批命中的冻结层声明（反馈包 <c>freeze_layers</c>），供稍后才到达的 <c>feel.hitstop_started</c> 解析层。
        /// 判断记录：顿帧是在 tick 末由判定型宿主落地并以排队事件发出的（<c>HitFeelHost.FlushHitstop</c>），到达时命中自己的打击计划通常已在
        /// 前一次出批里下发过，所以只看"同批计划"（此前的做法）几乎总是找不到相关命中、层声明恒为缺省——<c>freeze_layers.particles/trail</c>
        /// 在生产链路里从未生效（单测把命中与顿帧塞进同一批才看不出来）。保留最近 <see cref="RecentGenerations"/> 次出批的命中供匹配，
        /// 更老的丢弃（顿帧最多滞后一两次出批；不无限保留，免得把很久以前的反馈包套到无关的顿帧上）。</summary>
        private readonly List<RecentHit> _recentHits = new List<RecentHit>();
        private int _generation;
        private const int RecentGenerations = 3;

        private readonly struct RecentHit
        {
            public readonly int Generation;
            public readonly ImpactHit Hit;
            public readonly ImpactFreezeLayers Layers;

            public RecentHit(int generation, ImpactHit hit, ImpactFreezeLayers layers)
            {
                Generation = generation;
                Hit = hit;
                Layers = layers;
            }
        }

        private sealed class WhiffWindow
        {
            /// <summary>窗口内（含判定相结束之后、投射物结局之前）收到的命中数。</summary>
            public int Hits;

            /// <summary>判定相是否已结束；结束时还有飞行中的投射物则窗口留着等它们的结局。</summary>
            public bool Closed;
        }

        // 挥空窗口：键 = (行动者, 动作实例)。动作实例为 null 的窗口来自没有施法实例 id 的旧调用方式（按行动者配对）。
        private readonly Dictionary<(Id Actor, Id? Cast), WhiffWindow> _whiffWindows = new Dictionary<(Id Actor, Id? Cast), WhiffWindow>();
        // 不带攻击的动作实例（action.started.isAttack = false）：不开挥空窗口。没有收到过 action.started 的实例按带攻击处理（旧调用方式）。
        private readonly HashSet<(Id Actor, Id Cast)> _nonAttackCasts = new HashSet<(Id Actor, Id Cast)>();
        // 飞行中的投射物数：键 = (行动者, 动作实例)。独立于窗口登记（release 标记可能先于判定相的相位事件到达）。
        private readonly Dictionary<(Id Actor, Id Cast), int> _inFlight = new Dictionary<(Id Actor, Id Cast), int>();
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);

        private long _tick;
        private bool _tickClockSeen;
        private double _frameMs;
        private double _lastCueMs = double.NegativeInfinity;

        public ImpactPipeline(ImpactOptions options, IPresentationDiagnostics? diagnostics = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();
            Freezes = new ImpactFreezeRegistry();
        }

        public ImpactFreezeRegistry Freezes { get; }

        /// <summary>当前时钟（毫秒），见类型注释"时钟"。</summary>
        public double NowMs => _tickClockSeen ? _tick * _options.StepSeconds * 1000.0 : _frameMs;

        /// <summary>本批待出的命中数（含挥空）。</summary>
        public int PendingCount => _group.Count;

        public bool HasPending => _group.Count > 0 || _hitstops.Count > 0;

        public void Advance(double dt)
        {
            if (dt > 0) _frameMs += dt * 1000.0;
        }

        public void SetTick(long tickIndex)
        {
            _tickClockSeen = true;
            _tick = tickIndex;
        }

        // ------------------------------------------------------------------
        // 收命中
        // ------------------------------------------------------------------

        /// <summary>
        /// 收一次命中。<paramref name="explicitProfileId"/> 非 null 用显式反馈包（<c>play_impact.profile_id</c>），为 null 走
        /// <c>from_feel</c>。返回是否产生了表现计划（取不到反馈包/变体时为 false，不抛异常）。
        /// </summary>
        public bool Offer(ImpactHit hit, Id? explicitProfileId)
        {
            if (hit == null) throw new ArgumentNullException(nameof(hit));

            ObserveHit(hit);

            var attackerFeel = _options.FeelSource?.Get(hit.SourceId);
            var profileId = explicitProfileId ?? attackerFeel?.ProfileRef;
            if (profileId == null)
            {
                profileId = _options.FeelSource?.Get(hit.TargetId)?.ProfileRef;
            }
            if (profileId == null)
            {
                return false;
            }

            var profile = ResolveProfile(profileId.Value);
            var variant = profile?.Select(hit.ImpactClass, hit.Outcome);
            if (variant == null)
            {
                return false;
            }

            var plan = new ImpactPlan(profileId, variant, hit.Outcome, hit, hit.SourceId, hit.TargetId);
            var cand = new Candidate { Plan = plan, AttackerFeel = attackerFeel };

            if (variant.Flash != null)
            {
                plan.FlashProfile = variant.Flash.ProfileId;
                plan.FlashEntity = variant.Flash.Target == FeedbackAttachTarget.Source ? hit.SourceId : hit.TargetId;
            }

            if (variant.Camera != null)
            {
                var intensity = variant.Intensity;
                var baseGain = attackerFeel?.CameraImpulseGain ?? 0.0;
                cand.Magnitude = baseGain * variant.Camera.ImpulseGain
                    * intensity.RatioFactor(hit.AmountRatio) * intensity.OutcomeFactor(hit.IsCrit, hit.IsKill)
                    * DistanceFactor(hit, attackerFeel);
                if (!(cand.Magnitude > 0)) cand.Magnitude = 0;
                cand.Direction = Normalize(hit.WorldDirection);
                cand.DecayMs = variant.Camera.DecayMs;
                cand.ShakeProfile = variant.Camera.ShakeProfile;
                cand.HasCamera = true;
            }

            _group.Add(cand);
            return true;
        }

        private ImpactProfile? ResolveProfile(Id profileId)
        {
            var profile = _options.ProfileResolver?.Invoke(profileId);
            if (profile == null)
            {
                ReportOnce("profile:" + profileId.Value, $"打击反馈包 \"{profileId}\" 不存在（或未注入 ProfileResolver），本次不播反馈包");
            }
            return profile;
        }

        private double DistanceFactor(ImpactHit hit, ImpactFeel? attackerFeel)
        {
            var owner = _options.CameraOwnerResolver?.Invoke();
            var ownerFeel = owner.HasValue ? _options.FeelSource?.Get(owner.Value) : null;
            var curveRef = (ownerFeel ?? attackerFeel)?.CameraDistanceAttenuation;
            if (string.IsNullOrEmpty(curveRef) || curveRef == "linear")
            {
                return 1.0;
            }

            var curve = _options.CurveResolver?.Invoke(curveRef!);
            if (curve == null)
            {
                ReportOnce("curve:" + curveRef, $"镜头距离衰减曲线 \"{curveRef}\" 解析不到（未注入 CurveResolver 或曲线不存在），按不衰减处理");
                return 1.0;
            }

            if (!owner.HasValue || _options.PositionResolver == null)
            {
                return 1.0;
            }
            var ownerPos = _options.PositionResolver(owner.Value);
            var hitPos = hit.ContactPoint ?? _options.PositionResolver(hit.TargetId);
            if (!ownerPos.HasValue || !hitPos.HasValue)
            {
                return 1.0;
            }

            var referenceHeight = _options.ReferenceHeightSource != null ? _options.ReferenceHeightSource() : _options.ReferenceHeight;
            var bodyHeights = Vec2.Distance(ownerPos.Value, hitPos.Value) / (referenceHeight > 0 ? referenceHeight : 1.0);
            var factor = curve.Evaluate(bodyHeights);
            return factor > 0 ? factor : 0.0;
        }

        private static Vec2 Normalize(Vec2 v)
        {
            var len = v.Length;
            return len > 1e-12 ? new Vec2(v.X / len, v.Y / len) : Vec2.Zero;
        }

        // ------------------------------------------------------------------
        // 挥空（手感设计/07 第 6 节）
        // ------------------------------------------------------------------

        /// <summary>
        /// 记录一次命中接触（任何结局，包括被回避——"打到了但被闪避"与挥空分开）：命中数加一。
        /// 判断记录：窗口按 (行动者, 动作实例) 配对（手感设计/03 第 2.4 节 <c>combat.hit_confirmed.castInstanceId</c> 与
        /// <c>action.marker</c>/<c>action.phase_changed</c> 的施法实例 id 是同一个值）：带动作实例 id 的命中只计入同一动作实例的窗口，
        /// 上一段连招的迟到命中不会误计入下一段、投射物在动作结束后命中也不会污染别的窗口。没有动作实例 id 的命中
        /// （instant 的 <c>combat.damage_dealt</c>/<c>combat.attack_avoided</c>、旧的按行动者调用方式）计入该行动者全部打开的窗口。
        /// 同一动作内多段判定（多个 hit 标记）合成一个窗口，窗口内只要有任何一次接触就不算挥空——已知局限，写在 feedback_binder/README.md。
        /// </summary>
        public void ObserveHit(ImpactHit hit)
        {
            if (_whiffWindows.Count == 0)
            {
                return;
            }

            if (hit.CastInstanceId.HasValue)
            {
                var key = (hit.SourceId, (Id?)hit.CastInstanceId);
                if (_whiffWindows.TryGetValue(key, out var window))
                {
                    window.Hits++;
                }

                return;
            }

            foreach (var kv in _whiffWindows)
            {
                if (kv.Key.Actor.Equals(hit.SourceId))
                {
                    kv.Value.Hits++;
                }
            }
        }

        /// <summary>
        /// 动作开始（<c>action.started</c>）：<paramref name="isAttack"/> 为假的动作（闪避、位移、纯增益）不开挥空窗口——
        /// 它们没有"打空"（手感设计/07 第 6 节）。没有调用过本方法的动作实例按带攻击处理，窗口行为与此前一致。
        /// </summary>
        public void OnActionStarted(Id actorId, Id castInstanceId, bool isAttack)
        {
            if (isAttack)
            {
                _nonAttackCasts.Remove((actorId, castInstanceId));
            }
            else
            {
                _nonAttackCasts.Add((actorId, castInstanceId));
            }
        }

        /// <summary>动作结束或被取消（<c>action.finished</c>/<c>action.cancelled</c>）：清掉该实例的"不带攻击"登记。</summary>
        public void OnActionEnded(Id actorId, Id castInstanceId) => _nonAttackCasts.Remove((actorId, castInstanceId));

        /// <summary>
        /// 动作发射了一发投射物（<c>action.projectile_launched</c>）：该动作实例的挥空判定要等这发投射物有结局才能定
        /// （命中就不是挥空；穿透、到期、被挡住而没有任何接触才是挥空）。
        /// </summary>
        public void OnProjectileLaunched(Id actorId, Id castInstanceId)
        {
            var key = (actorId, castInstanceId);
            _inFlight.TryGetValue(key, out var n);
            _inFlight[key] = n + 1;
        }

        /// <summary>
        /// 投射物有了结局（<c>action.projectile_ended</c>）。该动作实例的最后一发投射物结束、判定相早已结束且全程没有任何接触时，
        /// 此刻补发挥空；<paramref name="cleared"/>（被清场）不算挥空，只放弃等待。命中确认总先于结局事件到达，所以
        /// "命中后销毁"的那一发在这里看到的命中数已经是正的。
        /// </summary>
        public void OnProjectileEnded(Id actorId, Id castInstanceId, bool cleared)
        {
            var key = (actorId, castInstanceId);
            if (!_inFlight.TryGetValue(key, out var n))
            {
                return; // 没有登记过的结局（发射事件早于流水线注入等），忽略。
            }

            if (n > 1)
            {
                _inFlight[key] = n - 1;
                return;
            }

            _inFlight.Remove(key);
            var windowKey = (actorId, (Id?)castInstanceId);
            if (_whiffWindows.TryGetValue(windowKey, out var window) && window.Closed)
            {
                _whiffWindows.Remove(windowKey);
                if (!cleared && window.Hits == 0 && _options.WhiffFeedback)
                {
                    OfferWhiff(actorId);
                }
            }
        }

        public void OnActionMarker(Id actorId, string name) => OnActionMarker(actorId, name, null);

        /// <summary><see cref="OnActionMarker(Id, string)"/> 的按动作实例版本：窗口键为 (行动者, <paramref name="castInstanceId"/>)。</summary>
        public void OnActionMarker(Id actorId, string name, Id? castInstanceId)
        {
            if (name == "active_start")
            {
                OpenWindow(actorId, castInstanceId, reset: true);
            }
            else if (name == "active_end")
            {
                CloseWindow(actorId, castInstanceId);
            }
        }

        public void OnActionPhase(Id actorId, ActionPhase phase) => OnActionPhase(actorId, phase, null);

        /// <summary><see cref="OnActionPhase(Id, ActionPhase)"/> 的按动作实例版本。</summary>
        public void OnActionPhase(Id actorId, ActionPhase phase, Id? castInstanceId)
        {
            if (phase == ActionPhase.Active)
            {
                OpenWindow(actorId, castInstanceId, reset: false);
            }
            else
            {
                CloseWindow(actorId, castInstanceId);
            }
        }

        private void OpenWindow(Id actorId, Id? castInstanceId, bool reset)
        {
            if (castInstanceId.HasValue && _nonAttackCasts.Contains((actorId, castInstanceId.Value)))
            {
                return; // 不带攻击的动作没有"打空"。
            }

            var key = (actorId, castInstanceId);
            if (reset || !_whiffWindows.ContainsKey(key))
            {
                _whiffWindows[key] = new WhiffWindow();
            }
        }

        private void CloseWindow(Id actorId, Id? castInstanceId)
        {
            var key = (actorId, castInstanceId);
            if (!_whiffWindows.TryGetValue(key, out var window) || window.Closed)
            {
                return;
            }

            if (castInstanceId.HasValue && _inFlight.ContainsKey((actorId, castInstanceId.Value)))
            {
                // 还有投射物在飞：窗口留着，等它们的结局再定（OnProjectileEnded）；之后到达的命中仍计入这个窗口。
                window.Closed = true;
                return;
            }

            _whiffWindows.Remove(key);
            if (window.Hits == 0 && _options.WhiffFeedback)
            {
                OfferWhiff(actorId);
            }
        }

        private void OfferWhiff(Id actorId)
        {
            var feel = _options.FeelSource?.Get(actorId);
            ImpactVariant? variant = null;
            Id? profileId = feel?.ProfileRef;
            if (profileId.HasValue)
            {
                variant = ResolveProfile(profileId.Value)?.Select(ImpactProfile.FallbackClass, ImpactOutcome.Whiff);
            }

            if (variant == null)
            {
                if (feel == null)
                {
                    return;
                }
                variant = DefaultWhiffVariant;
                profileId = null;
            }

            var plan = new ImpactPlan(profileId, variant, ImpactOutcome.Whiff, null, actorId, null);
            if (variant.Flash != null && variant.Flash.Target == FeedbackAttachTarget.Source)
            {
                plan.FlashProfile = variant.Flash.ProfileId;
                plan.FlashEntity = actorId;
            }
            _group.Add(new Candidate { Plan = plan, AttackerFeel = feel });
        }

        // ------------------------------------------------------------------
        // 顿帧
        // ------------------------------------------------------------------

        public void OnHitstopStarted(IReadOnlyList<Id> unitIds, int ticks, Id attackInstanceId) =>
            _hitstops.Add(new RawHitstop { IsStart = true, UnitIds = unitIds, Ticks = ticks, AttackInstanceId = attackInstanceId });

        public void OnHitstopEnded(IReadOnlyList<Id> unitIds) =>
            _hitstops.Add(new RawHitstop { IsStart = false, UnitIds = unitIds });

        // ------------------------------------------------------------------
        // 出批
        // ------------------------------------------------------------------

        /// <summary>出批并清空；没有任何待出内容返回 null。</summary>
        public ImpactBatch? Flush()
        {
            if (!HasPending)
            {
                return null;
            }

            var group = new List<Candidate>(_group);
            var rawHitstops = new List<RawHitstop>(_hitstops);
            _group.Clear();
            _hitstops.Clear();

            var plans = new List<ImpactPlan>(group.Count);
            var sfxLimit = _options.MaxImpactsPerTick;
            ImpactFeel? firstFeel = null;
            foreach (var c in group)
            {
                if (c.AttackerFeel != null) { firstFeel = c.AttackerFeel; break; }
            }
            if (firstFeel != null && firstFeel.MaxConcurrent > 0 && (sfxLimit <= 0 || firstFeel.MaxConcurrent < sfxLimit))
            {
                sfxLimit = firstFeel.MaxConcurrent;
            }

            var sfxUsed = 0;
            var vfxUsed = 0;
            foreach (var cand in group)
            {
                var plan = cand.Plan;
                var variant = plan.Variant;
                var hit = plan.Hit;

                if (variant.Vfx != null && (_options.MaxVfxPerTick <= 0 || vfxUsed < _options.MaxVfxPerTick))
                {
                    FillVfx(plan, variant.Vfx, hit, cand.AttackerFeel);
                    vfxUsed++;
                }

                if (variant.Sfx.Count > 0 && (sfxLimit <= 0 || sfxUsed < sfxLimit))
                {
                    FillSfx(plan, variant, cand.AttackerFeel);
                    sfxUsed++;
                }

                if (variant.FloatingTextStyle.HasValue && hit != null
                    && hit.Outcome != ImpactOutcome.Avoided && hit.Amount > 0)
                {
                    plan.FloatingTextStyle = variant.FloatingTextStyle;
                    plan.FloatingTextEntity = hit.TargetId;
                    plan.FloatingTextAmount = hit.Amount;
                }

                plans.Add(plan);
            }

            var camera = BuildCamera(group, out var dropped);

            var ops = new List<ImpactHitstopOp>(rawHitstops.Count);
            _generation++;
            _recentHits.RemoveAll(r => _generation - r.Generation > RecentGenerations);
            foreach (var raw in rawHitstops)
            {
                if (raw.IsStart)
                {
                    var layers = LayersFor(raw, plans, _recentHits);
                    Freezes.Freeze(raw.UnitIds, layers);
                    ops.Add(new ImpactHitstopOp(true, raw.UnitIds, raw.Ticks, layers));
                }
                else
                {
                    Freezes.Release(raw.UnitIds);
                    ops.Add(new ImpactHitstopOp(false, raw.UnitIds, 0, ImpactFreezeLayers.Default));
                }
            }

            // 本批命中登记进"最近命中"，供之后到达的顿帧解析层（本批自己的顿帧上面已经同时看过 plans 了）。
            foreach (var plan in plans)
            {
                if (plan.Hit != null)
                {
                    _recentHits.Add(new RecentHit(_generation, plan.Hit, plan.Variant.FreezeLayers));
                }
            }

            return new ImpactBatch(plans, camera, dropped, ops);
        }

        private static ImpactFreezeLayers LayersFor(RawHitstop op, List<ImpactPlan> plans, List<RecentHit> recent)
        {
            var particles = false;
            var trail = false;
            var found = false;

            void Consider(ImpactHit? hit, ImpactFreezeLayers layers)
            {
                if (hit == null) return;
                var related = (hit.AttackInstanceId.HasValue && hit.AttackInstanceId.Value.Equals(op.AttackInstanceId))
                    || Contains(op.UnitIds, hit.SourceId) || Contains(op.UnitIds, hit.TargetId);
                if (!related) return;
                found = true;
                particles |= layers.Particles;
                trail |= layers.Trail;
            }

            for (var i = 0; i < recent.Count; i++) Consider(recent[i].Hit, recent[i].Layers);
            foreach (var plan in plans) Consider(plan.Hit, plan.Variant.FreezeLayers);
            return found ? new ImpactFreezeLayers(particles, trail) : ImpactFreezeLayers.Default;
        }

        private static bool Contains(IReadOnlyList<Id> ids, Id id)
        {
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i].Equals(id)) return true;
            }
            return false;
        }

        private void FillVfx(ImpactPlan plan, ImpactVfxSpec spec, ImpactHit? hit, ImpactFeel? feel)
        {
            var source = plan.SourceId;
            FeedbackAttachSpec attach;
            switch (spec.Attach)
            {
                case ImpactVfxAttach.Source:
                    attach = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, source, null);
                    break;
                case ImpactVfxAttach.Target when plan.TargetId.HasValue:
                    attach = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Target, plan.TargetId.Value, null);
                    break;
                case ImpactVfxAttach.Contact when hit?.ContactPoint != null:
                    attach = FeedbackAttachSpec.ForWorld(hit.ContactPoint.Value);
                    break;
                default:
                    if (plan.TargetId.HasValue)
                    {
                        attach = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Target, plan.TargetId.Value, null);
                    }
                    else
                    {
                        attach = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, source, null);
                    }
                    break;
            }

            var ratio = hit?.AmountRatio ?? 0.0;
            var intensity = plan.Variant.Intensity;
            var curveFactor = spec.ScaleByRatio != null && spec.ScaleByRatio.Count > 0
                ? spec.ScaleByRatio.Evaluate(ratio)
                : intensity.RatioFactor(ratio);
            var scale = (feel?.VfxScale ?? 1.0) * curveFactor * intensity.OutcomeFactor(hit?.IsCrit ?? false, hit?.IsKill ?? false);

            var parameters = new Dictionary<string, double>(StringComparer.Ordinal) { ["scale"] = scale };
            if (hit != null && spec.Orient != ImpactVfxOrient.None)
            {
                var dir = spec.Orient == ImpactVfxOrient.ContactNormal ? hit.ContactNormal : hit.WorldDirection;
                if (dir.Length > 1e-12)
                {
                    parameters["orient_rad"] = Math.Atan2(dir.Y, dir.X);
                }
            }

            plan.VfxId = spec.VfxId;
            plan.VfxAttach = attach;
            plan.VfxParameters = parameters;
        }

        private void FillSfx(ImpactPlan plan, ImpactVariant variant, ImpactFeel? feel)
        {
            if (_options.SfxLayers == null)
            {
                ReportOnce("sfxindex", "打击反馈包声明了音效层但没有注入 SfxLayerIndex（ImpactOptions.SfxLayers），音效层不发声");
                return;
            }

            var avoidedLike = plan.Outcome == ImpactOutcome.Avoided || plan.Outcome == ImpactOutcome.Whiff;
            var material = feel?.Material ?? SfxLayerIndex.GenericMaterial;
            plan.SfxPosition = plan.Hit?.ContactPoint;
            foreach (var spec in variant.Sfx)
            {
                if (avoidedLike && spec.Layer == SfxFeelLayer.Impact)
                {
                    ReportOnce("avoid-impact:" + plan.ProfileId, "回避/挥空结局的反馈包变体声明了 impact 音效层，已忽略（回避类结局不播命中音效）");
                    continue;
                }

                var tier = spec.Tier ?? feel?.TierOf(spec.Layer) ?? 0;
                if (_options.SfxLayers.TryResolve(spec.Layer, tier, material, out var resolved))
                {
                    plan.Sfx.Add(new ImpactSfxPlay(spec.Layer, resolved.Tier, resolved.Material, resolved.SfxId, resolved.FellBack));
                }
            }
        }

        private ImpactCameraCue? BuildCamera(List<Candidate> group, out bool droppedByInterval)
        {
            droppedByInterval = false;

            var cameraCands = new List<Candidate>();
            foreach (var c in group)
            {
                if (c.HasCamera) cameraCands.Add(c);
            }
            if (cameraCands.Count == 0)
            {
                return null;
            }

            var owner = _options.CameraOwnerResolver?.Invoke();
            var paramsFeel = owner.HasValue ? _options.FeelSource?.Get(owner.Value) : null;
            if (paramsFeel == null)
            {
                foreach (var c in cameraCands)
                {
                    if (c.AttackerFeel != null) { paramsFeel = c.AttackerFeel; break; }
                }
            }

            var userScale = 1.0;
            if (paramsFeel?.CameraUserIntensitySetting != null && _options.UserIntensity != null)
            {
                userScale = Math.Clamp(_options.UserIntensity(paramsFeel.CameraUserIntensitySetting), 0.0, 1.0);
            }
            if (!(userScale > 0))
            {
                return null;
            }

            var cap = paramsFeel?.CameraShakeCap ?? 0.0;
            var minInterval = paramsFeel?.CameraImpulseMinIntervalMs ?? 0.0;

            double max = 0;
            Candidate? top = null;
            double sx = 0, sy = 0;
            foreach (var c in cameraCands)
            {
                var m = c.Magnitude * userScale;
                sx += c.Direction.X * m;
                sy += c.Direction.Y * m;
                if (top == null || m > max)
                {
                    max = m;
                    top = c;
                }
            }

            Id? shake = null;
            foreach (var c in cameraCands)
            {
                if (c == top && c.ShakeProfile.HasValue) { shake = c.ShakeProfile; break; }
            }
            if (!shake.HasValue)
            {
                foreach (var c in cameraCands)
                {
                    if (c.ShakeProfile.HasValue) { shake = c.ShakeProfile; break; }
                }
            }

            var merged = Math.Min(max, cap);
            if (!(merged > 0) && !shake.HasValue)
            {
                return null;
            }

            var now = NowMs;
            if (now - _lastCueMs < minInterval)
            {
                droppedByInterval = true;
                return null;
            }
            _lastCueMs = now;

            var direction = Normalize(new Vec2(sx, sy));
            var decay = top!.DecayMs;
            var ticks = FeelCalibration.MillisecondsToTicks(decay, _options.StepSeconds);
            return new ImpactCameraCue(direction, merged > 0 ? merged : 0.0, max, decay, ticks, shake, cameraCands.Count);
        }

        private void ReportOnce(string key, string message)
        {
            if (_reported.Add(key))
            {
                _diagnostics.Warn(message);
            }
        }
    }
}
