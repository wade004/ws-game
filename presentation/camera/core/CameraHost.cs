using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SceneRouter;
using Core.Gameplay.Encounter;
using Presentation.VfxSfx.Contracts;

namespace Presentation.Camera
{
    /// <summary>
    /// <see cref="ICameraHost"/> 的默认实现（见 01 L5 模块表 <c>camera</c> 行、09 第 3.5、8 节）。
    /// 全部操作经 <see cref="ICamera"/>（L-1）完成（铁律 P4）；跟随目标位置经
    /// <see cref="ICameraFollowTarget"/> 只读获取（铁律 P1）。
    /// </summary>
    public sealed class CameraHost : ICameraHost, IDisposable
    {
        private readonly ICamera _camera;
        private readonly ICameraFollowTarget _followTarget;
        private readonly CameraHostOptions _options;

        private readonly Dictionary<Id, CameraProfile> _profiles = new Dictionary<Id, CameraProfile>();

        /// <summary>缺口 5（退订）：构造期建立的全部事件订阅句柄（<paramref name="bus"/> 为 null 时
        /// 恒为空列表），供 <see cref="Dispose"/> 统一退订。</summary>
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private bool _disposed;

        private CameraProfile? _currentProfile;
        private Id? _followEntityId;

        private readonly IPresentationDiagnostics _diagnostics;

        /// <summary>ADR-0121 第 6 条（D6）：跟随目标最近一次成功取到并已交给 <see cref="ICamera.Follow"/> 的
        /// （已按 profile 边界夹取的）位置；目标丢失期间保持它。<see cref="Follow"/> 换目标时清空。</summary>
        private Vec2? _lastFollowPosition;

        /// <summary>目标丢失诊断的去重标志：一次"丢失"只记一条（多帧不刷），目标重新出现后复位，
        /// 下一次丢失重新记一条。</summary>
        private bool _targetLostReported;

        // ------------------------------------------------------------------
        // 手感镜头（手感设计/07 第 2 节）：全部是可选增量，未 EnableFeel 时本类行为与改动前逐位一致。
        // ------------------------------------------------------------------

        /// <summary><see cref="EnableFeel"/> 注入的"实体 id → 镜头手感档案"来源；null 表示未启用手感镜头。</summary>
        private Func<Id, CameraFeelProfile?>? _feelSource;

        private readonly CameraFeelFollower _feelFollower = new CameraFeelFollower();
        private double _shakeReferenceHeight = 1.0;
        private Func<double>? _shakeReferenceHeightSource;
        private bool _impulseFallbackReported;

        /// <summary>战斗缩放：基准缩放（Configure/SetZoom 设定的值，夹取后）、当前战斗缩放系数（1 = 无战斗缩放）、
        /// 上一次实际下发给适配层的系数、是否处于战斗中。</summary>
        private double _baseZoom = 1.0;
        private double _zoomFactor = 1.0;
        private double _appliedZoomFactor = 1.0;
        private bool _inCombat;

        /// <summary>未显式给出 dt 的 <see cref="Update(double)"/> 用的帧间隔（秒）。判断记录：<see cref="ICameraHost.Update"/>
        /// 既有签名只有插值系数 alpha、没有 dt，且为 ABI 只加法不改它；手感镜头需要 dt 时由调用方改用
        /// <see cref="Update(double, double)"/>，旧调用点（三个生产装配入口）保持不变时按固定 1/60 秒推进——
        /// 对固定步长的宿主这就是准确值；三个生产装配入口改调带 dt 的重载，按真实帧间隔推进（见 camera/README.md 判断记录 8 帧间隔口径），
        /// 本默认值只服务仍调用旧签名的宿主（设计决定：旧签名没有 dt 来源，按固定 1/60 秒推进）。</summary>
        public double FeelFrameSeconds { get; set; } = 1.0 / 60.0;

        /// <summary>退化为 <see cref="ICamera.Shake"/> 时使用的震屏频率（Hz）。</summary>
        public double ImpulseFallbackShakeFrequency { get; set; } = 30.0;

        public CameraHost(
            ICamera camera,
            ICameraFollowTarget followTarget,
            IEventBus? bus = null,
            CameraHostOptions? options = null)
            : this(camera, followTarget, bus, options, diagnostics: null)
        {
        }

        /// <summary>ADR-0121 第 6 条（D6）新增的重载：额外接受诊断出口（跟随目标丢失记一条警告）。不在既有
        /// 四参构造上加可选参数（ABI 只新增，见 AGENTS 第 3 节）；传 null 时自建
        /// <see cref="PresentationDiagnosticsRecorder"/>，经 <see cref="Diagnostics"/> 暴露。</summary>
        public CameraHost(
            ICamera camera,
            ICameraFollowTarget followTarget,
            IEventBus? bus,
            CameraHostOptions? options,
            IPresentationDiagnostics? diagnostics)
        {
            _diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _followTarget = followTarget ?? throw new ArgumentNullException(nameof(followTarget));
            _options = options ?? new CameraHostOptions();

            if (bus != null)
            {
                _subscriptions.Add(bus.Subscribe<SceneLoadFinishedEvent>(SceneRouterEventKeys.LoadFinished, _ => OnSceneLoadFinished()));
                _subscriptions.Add(bus.Subscribe<EncounterPhaseChangedEvent>(EncounterEventKeys.PhaseChanged, OnEncounterPhaseChanged));
            }
        }

        /// <summary>退订构造期建立的全部事件订阅（缺口 5）。退订后场景加载完成/遭遇阶段变化不再
        /// 更新镜头；幂等，多次调用只生效一次。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            foreach (var sub in _subscriptions)
            {
                sub.Dispose();
            }
            _subscriptions.Clear();
        }

        /// <summary>当前生效的 profile；未 <see cref="Configure"/> 前为 null。</summary>
        public CameraProfile? CurrentProfile => _currentProfile;

        /// <summary>当前跟随目标；未 <see cref="Follow"/> 前为 null。</summary>
        public Id? FollowEntityId => _followEntityId;

        /// <summary>构造期注入（或默认自建）的诊断出口，供适配层轮询转发（惯例同 <c>ViewBinder.Diagnostics</c>）。</summary>
        public IPresentationDiagnostics Diagnostics => _diagnostics;

        public void RegisterProfile(CameraProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            _profiles[profile.Id] = profile;
        }

        public void Configure(CameraProfile profile)
        {
            RegisterProfile(profile);
            _currentProfile = profile;

            _camera.Configure(profile.PitchDegrees, profile.YawDegrees, new ZoomRange(profile.ZoomMin, profile.ZoomMax));
            _baseZoom = profile.ZoomDefault;
            _camera.SetZoom(profile.ZoomDefault);
            _appliedZoomFactor = 1.0;
            _zoomFactor = 1.0;
            _feelFollower.Reset();
        }

        public void Follow(Id entityId)
        {
            _followEntityId = entityId;
            _lastFollowPosition = null;
            _targetLostReported = false;
            _feelFollower.Reset();
        }

        public void Update(double alpha) => Update(alpha, FeelFrameSeconds);

        /// <summary>带帧间隔的更新（手感镜头需要 dt：前瞻速度估计与一阶滞后系数）。<paramref name="dt"/> 是本帧秒数。
        /// 未 <see cref="EnableFeel"/>、或跟随实体的档案 <see cref="CameraFeelProfile.IsFollowNeutral"/> 时，
        /// 跟随调用与 <see cref="Update(double)"/> 改动前逐位一致（<c>ICamera.Follow(pos, FollowLerp)</c>）。</summary>
        public void Update(double alpha, double dt)
        {
            AdvanceFeelClock(dt);
            if (_currentProfile == null || _followEntityId == null)
            {
                return;
            }

            CameraFeelProfile? feel = null;
            if (_feelSource != null)
            {
                feel = _feelSource(_followEntityId.Value);
            }
            var useFeelFollow = feel != null && !feel.IsFollowNeutral;
            if (!useFeelFollow)
            {
                _feelFollower.Reset();
            }
            var smoothing = useFeelFollow ? CameraFeelFollower.SmoothingFor(feel!, _currentProfile.FollowLerp) : _currentProfile.FollowLerp;

            // ADR-0121 第 6 条（D6）：目标以"可能不存在"为契约。目标缺失时保持上一位置（继续向最后一次
            // 有效位置 Follow，相机收敛并停在那里；从未取到过位置则不动）、每次丢失只记一条诊断（多帧不
            // 刷）；目标重新出现则继续跟随并复位去重标志。
            if (!_followTarget.TryGetPosition(_followEntityId.Value, alpha, out var pos))
            {
                if (!_targetLostReported)
                {
                    _targetLostReported = true;
                    _diagnostics.Warn(
                        $"相机跟随目标丢失：实体 \"{_followEntityId.Value}\" 当前不存在，镜头保持最后位置" +
                        "（目标重新出现后自动继续跟随；持续丢失期间不重复记录）");
                }

                if (_lastFollowPosition.HasValue)
                {
                    _camera.Follow(_lastFollowPosition.Value, smoothing);
                }

                UpdateCombatZoom(feel, dt);
                return;
            }

            _targetLostReported = false;

            if (_currentProfile.Bounds.HasValue)
            {
                pos = _currentProfile.Bounds.Value.Clamp(pos);
            }

            if (useFeelFollow)
            {
                pos = _feelFollower.Step(pos, dt, feel!);
                if (_currentProfile.Bounds.HasValue)
                {
                    pos = _currentProfile.Bounds.Value.Clamp(pos);
                }
            }

            _lastFollowPosition = pos;
            _camera.Follow(pos, smoothing);
            UpdateCombatZoom(feel, dt);
        }

        /// <summary>
        /// 启用手感镜头（手感设计/07 第 2 节）：<paramref name="feelSource"/> 按实体 id 给出镜头手感档案（装配根接
        /// <see cref="CameraFeelProfile.FromPresenting"/>）；<paramref name="shakeReferenceHeight"/> 是镜头冲击退化为
        /// <see cref="ICamera.Shake"/> 时"画面高度比例 → 震屏强度"的换算系数（取手感标定的参考镜头高度）。
        /// 传 null 关闭手感镜头，回到改动前的行为。
        /// </summary>
        public void EnableFeel(Func<Id, CameraFeelProfile?>? feelSource, double shakeReferenceHeight = 1.0)
        {
            if (!(shakeReferenceHeight > 0) || double.IsInfinity(shakeReferenceHeight))
            {
                throw new ArgumentOutOfRangeException(nameof(shakeReferenceHeight), "必须是正的有限数");
            }

            _feelSource = feelSource;
            _shakeReferenceHeight = shakeReferenceHeight;
            _shakeReferenceHeightSource = null;
            _feelFollower.Reset();
        }

        /// <summary>
        /// 启用手感镜头，震屏换算系数取实时来源（手感落地 M3-B）：每次镜头冲击退化为 <see cref="ICamera.Shake"/> 时读一次
        /// <paramref name="shakeReferenceHeightSource"/>（装配根接手感标定的参考镜头高度，标定行热加载后立即生效）。来源返回非正数或非有限数时本次回落到 1。
        /// </summary>
        public void EnableFeel(Func<Id, CameraFeelProfile?>? feelSource, Func<double> shakeReferenceHeightSource)
        {
            if (shakeReferenceHeightSource == null) throw new ArgumentNullException(nameof(shakeReferenceHeightSource));
            _feelSource = feelSource;
            _shakeReferenceHeight = 1.0;
            _shakeReferenceHeightSource = shakeReferenceHeightSource;
            _feelFollower.Reset();
        }

        private double CurrentShakeReferenceHeight()
        {
            if (_shakeReferenceHeightSource == null) return _shakeReferenceHeight;
            var value = _shakeReferenceHeightSource();
            return value > 0 && !double.IsInfinity(value) ? value : 1.0;
        }

        /// <summary>进入/离开战斗（战斗缩放的目标：战斗中取 <c>camera_combat_zoom_delta</c>，否则 1）。装配根订阅
        /// <c>combat.entered</c>/<c>combat.left</c> 调用；档案战斗缩放中性（倍率 1）时无任何可见效果。</summary>
        public void SetInCombat(bool inCombat) => _inCombat = inCombat;

        /// <summary>当前是否处于战斗（<see cref="SetInCombat"/> 最近一次的值）。</summary>
        public bool InCombat => _inCombat;

        /// <summary>当前战斗缩放系数（1 = 无）。</summary>
        public double CombatZoomFactor => _zoomFactor;

        /// <summary>适配层是否真正支持镜头冲击能力（<see cref="ICameraImpulse"/>）。</summary>
        public bool SupportsImpulse => _camera is ICameraImpulse impulse && impulse.SupportsCameraImpulse;

        /// <summary>
        /// 镜头冲击：沿 <paramref name="direction"/>（单位方向，零向量 = 无方向）把镜头推开 <paramref name="magnitude"/>（画面高度比例）
        /// 并在 <paramref name="decayMs"/> 内衰减回零。适配层支持 <see cref="ICameraImpulse"/> 时直接转发；不支持时退化为
        /// <see cref="ICamera.Shake"/>（强度 = 比例 × <c>shakeReferenceHeight</c>、时长 = 衰减时长、频率 =
        /// <see cref="ImpulseFallbackShakeFrequency"/>）并记一条去重诊断（手感设计/05 第 7 节降级）。幅度非正时忽略。
        /// <para>
        /// 出口统一处理（ADR-0148）：先乘玩家强度（<see cref="Intensity"/> 的镜头冲击系数），再按 <c>shake_cap</c>（跟随实体手感档案，
        /// 画面高度比例；0 或未启用手感镜头 = 相机侧不限制）把<b>所有仍在衰减的冲击与当前震屏的合成幅度</b>压在上限之内：
        /// 新冲击超出余量时按比例缩小，没有余量则丢弃。反馈包流水线只做批内合并（取最大、间隔限频），总量截断在这里。
        /// </para>
        /// </summary>
        public void Impulse(Vec2 direction, double magnitude, double decayMs)
        {
            if (Intensity != null)
            {
                magnitude *= Intensity.Get(FeelIntensityKind.Impulse);
            }
            if (!(magnitude > 0))
            {
                return;
            }

            var decay = decayMs > 1 ? decayMs : 1;
            var native = _camera is ICameraImpulse nativeImpulse && nativeImpulse.SupportsCameraImpulse;

            var cap = CurrentShakeCap();
            if (cap > 0)
            {
                var fitted = native
                    ? FitImpulse(direction, magnitude, decay, cap)
                    : FitShake(magnitude, decay, cap);
                if (fitted < magnitude)
                {
                    CapClampedCount++;
                    magnitude = fitted;
                }
                if (!(magnitude > 0))
                {
                    return;
                }
            }

            if (native)
            {
                TrackImpulse(direction, magnitude, decay);
                ((ICameraImpulse)_camera).Impulse(direction, magnitude, decay);
                return;
            }

            if (!_impulseFallbackReported)
            {
                _impulseFallbackReported = true;
                _diagnostics.Warn(
                    "镜头冲击：适配层不支持 ICameraImpulse（supportsCameraImpulse 为假），退化为 ICamera.Shake" +
                    "（强度 = 画面高度比例 × 参考镜头高度；仅首次记录，之后同样退化）");
            }

            TrackShake(magnitude, decay);
            _camera.Shake(magnitude * CurrentShakeReferenceHeight(), decay / 1000.0, ImpulseFallbackShakeFrequency);
        }

        /// <summary>
        /// 缩放脉冲（可选能力 <see cref="ICameraZoomPunch"/>，ADR-0148）：命中瞬间可视范围收窄 <paramref name="magnitude"/>（比例）再回落。
        /// 乘玩家强度的镜头冲击系数；适配层不支持时记一条去重诊断后忽略（没有对等降级通道）。不计入 <c>shake_cap</c> 合成幅度（它是缩放量、不是位移）。
        /// </summary>
        public void ZoomPunch(double magnitude, double decayMs)
        {
            if (Intensity != null)
            {
                magnitude *= Intensity.Get(FeelIntensityKind.Impulse);
            }
            if (!(magnitude > 0))
            {
                return;
            }

            if (_camera is ICameraZoomPunch punch && punch.SupportsCameraZoomPunch)
            {
                punch.ZoomPunch(magnitude, decayMs > 1 ? decayMs : 1);
                return;
            }

            if (!_zoomPunchUnsupportedReported)
            {
                _zoomPunchUnsupportedReported = true;
                _diagnostics.Warn("缩放脉冲：适配层不支持 ICameraZoomPunch（supportsCameraZoomPunch 为假），已忽略（仅首次记录）");
            }
        }

        /// <summary>适配层是否真正支持缩放脉冲能力。</summary>
        public bool SupportsZoomPunch => _camera is ICameraZoomPunch punch && punch.SupportsCameraZoomPunch;

        // ------------------------------------------------------------------
        // 相机侧合成幅度（ADR-0148）：跟踪本宿主发出的、仍在衰减的冲击与震屏，供 shake_cap 总量截断。
        // 衰减口径与 Unity 适配层一致（线性回落到 0）；时钟由 Update(alpha, dt) 推进。
        // ------------------------------------------------------------------

        private struct TrackedImpulse
        {
            public double X;
            public double Y;
            public bool Isotropic;
            public double Peak;
            public double StartMs;
            public double DurationMs;
        }

        private readonly List<TrackedImpulse> _tracked = new List<TrackedImpulse>();
        private bool _shakeTracked;
        private double _shakeAmplitude;
        private double _shakeStartMs;
        private double _shakeDurationMs;
        private double _feelClockMs;
        private bool _zoomPunchUnsupportedReported;

        /// <summary>玩家强度来源（装配根注入 <see cref="FeelIntensityHost"/>）；null 表示不缩放（系数恒 1）。</summary>
        public IFeelIntensity? Intensity { get; set; }

        /// <summary>被 <c>shake_cap</c> 缩小过的冲击/震屏次数（观察截断是否生效；丢弃也计入）。</summary>
        public int CapClampedCount { get; private set; }

        /// <summary>
        /// 当前合成幅度（画面高度比例）：方向冲击向量和的模 + 各向同性冲击幅度 + 震屏幅度，均按剩余时间线性衰减后计。
        /// 是 <c>shake_cap</c> 约束的量；只含经本宿主发出的冲击与震屏。
        /// </summary>
        public double CompositeMagnitude => CompositeAt(_feelClockMs, includeShake: true);

        /// <summary>手感镜头时钟（毫秒，<see cref="Update(double, double)"/> 累加的 dt）。</summary>
        public double FeelClockMs => _feelClockMs;

        private void AdvanceFeelClock(double dt)
        {
            if (dt > 0 && !double.IsInfinity(dt))
            {
                _feelClockMs += dt * 1000.0;
            }
            for (var i = _tracked.Count - 1; i >= 0; i--)
            {
                if (_feelClockMs >= _tracked[i].StartMs + _tracked[i].DurationMs)
                {
                    _tracked.RemoveAt(i);
                }
            }
            if (_shakeTracked && _feelClockMs >= _shakeStartMs + _shakeDurationMs)
            {
                _shakeTracked = false;
            }
        }

        private double CurrentShakeCap()
        {
            if (_feelSource == null || _followEntityId == null)
            {
                return 0.0;
            }
            var feel = _feelSource(_followEntityId.Value);
            return feel != null ? feel.ShakeCap : 0.0;
        }

        private void TrackImpulse(Vec2 direction, double magnitude, double decayMs)
        {
            var sqr = direction.X * direction.X + direction.Y * direction.Y;
            var iso = !(sqr > 1e-12);
            var len = iso ? 1.0 : Math.Sqrt(sqr);
            _tracked.Add(new TrackedImpulse
            {
                X = iso ? 0.0 : direction.X / len,
                Y = iso ? 0.0 : direction.Y / len,
                Isotropic = iso,
                Peak = magnitude,
                StartMs = _feelClockMs,
                DurationMs = decayMs,
            });
        }

        private void TrackShake(double amplitudeRatio, double decayMs)
        {
            _shakeTracked = true;
            _shakeAmplitude = amplitudeRatio;
            _shakeStartMs = _feelClockMs;
            _shakeDurationMs = decayMs;
        }

        private static double Remaining(double now, double start, double duration)
        {
            var f = 1.0 - (now - start) / duration;
            return f > 0 ? (f < 1.0 ? f : 1.0) : 0.0;
        }

        private double CompositeAt(double t, bool includeShake)
        {
            double sx = 0, sy = 0, iso = 0;
            for (var i = 0; i < _tracked.Count; i++)
            {
                var tr = _tracked[i];
                var f = Remaining(t, tr.StartMs, tr.DurationMs) * tr.Peak;
                if (tr.Isotropic) iso += f;
                else { sx += tr.X * f; sy += tr.Y * f; }
            }
            var shake = includeShake && _shakeTracked ? _shakeAmplitude * Remaining(t, _shakeStartMs, _shakeDurationMs) : 0.0;
            return Math.Sqrt(sx * sx + sy * sy) + iso + shake;
        }

        /// <summary>
        /// 断点时刻（现在，以及每个仍在衰减的轨道的结束时刻）：合成幅度是各轨道线性函数的向量和之模再相加，
        /// 两个断点之间是凸函数，最大值只会出现在断点上，所以只需在断点检查上限。
        /// </summary>
        private List<double> Breakpoints(bool includeShake)
        {
            var points = new List<double> { _feelClockMs };
            for (var i = 0; i < _tracked.Count; i++)
            {
                points.Add(_tracked[i].StartMs + _tracked[i].DurationMs);
            }
            if (includeShake && _shakeTracked)
            {
                points.Add(_shakeStartMs + _shakeDurationMs);
            }
            return points;
        }

        /// <summary>新方向冲击可用的最大幅度（不超过 <paramref name="magnitude"/>）：在所有断点上，加入新冲击后的合成幅度不超过 <paramref name="cap"/>。</summary>
        private double FitImpulse(Vec2 direction, double magnitude, double decayMs, double cap)
        {
            var sqr = direction.X * direction.X + direction.Y * direction.Y;
            var iso = !(sqr > 1e-12);
            var dx = iso ? 0.0 : direction.X / Math.Sqrt(sqr);
            var dy = iso ? 0.0 : direction.Y / Math.Sqrt(sqr);
            var best = magnitude;
            var newEnd = _feelClockMs + decayMs;
            var points = Breakpoints(includeShake: true);
            points.Add(newEnd);
            foreach (var t in points)
            {
                if (t > newEnd) continue;
                var f = Remaining(t, _feelClockMs, decayMs); // 新冲击在 t 时刻的剩余比例
                if (!(f > 0)) continue;
                double sx = 0, sy = 0, isoSum = 0;
                for (var i = 0; i < _tracked.Count; i++)
                {
                    var tr = _tracked[i];
                    var v = Remaining(t, tr.StartMs, tr.DurationMs) * tr.Peak;
                    if (tr.Isotropic) isoSum += v;
                    else { sx += tr.X * v; sy += tr.Y * v; }
                }
                if (_shakeTracked)
                {
                    isoSum += _shakeAmplitude * Remaining(t, _shakeStartMs, _shakeDurationMs);
                }

                double allowed; // 新冲击在 t 时刻的幅度 x = m * f 的上限
                if (iso)
                {
                    allowed = cap - Math.Sqrt(sx * sx + sy * sy) - isoSum;
                }
                else
                {
                    var head = cap - isoSum;
                    var sLen2 = sx * sx + sy * sy;
                    if (head <= 0 || sLen2 > head * head)
                    {
                        allowed = 0;
                    }
                    else
                    {
                        var sd = sx * dx + sy * dy;
                        var disc = sd * sd - sLen2 + head * head;
                        allowed = disc <= 0 ? 0 : -sd + Math.Sqrt(disc);
                    }
                }
                var m = allowed / f;
                if (m < best) best = m;
            }
            return best > 0 ? best : 0.0;
        }

        /// <summary>新震屏（替换当前震屏）可用的最大幅度：在断点上，冲击合成 + 新震屏不超过 <paramref name="cap"/>。</summary>
        private double FitShake(double amplitude, double durationMs, double cap)
        {
            var best = amplitude;
            var newEnd = _feelClockMs + durationMs;
            var points = Breakpoints(includeShake: false);
            points.Add(newEnd);
            foreach (var t in points)
            {
                if (t > newEnd) continue;
                var f = Remaining(t, _feelClockMs, durationMs);
                if (!(f > 0)) continue;
                var head = cap - CompositeAt(t, includeShake: false);
                var m = head / f;
                if (m < best) best = m;
            }
            return best > 0 ? best : 0.0;
        }

        private void UpdateCombatZoom(CameraFeelProfile? feel, double dt)
        {
            if (_currentProfile == null)
            {
                return;
            }

            var delta = feel != null && !feel.IsZoomNeutral ? feel.CombatZoomDelta : 1.0;
            var target = _inCombat ? delta : 1.0;
            if (_zoomFactor == target)
            {
                return;
            }

            var blendMs = feel?.CombatZoomBlendMs ?? 0.0;
            if (blendMs <= 0 || !(dt > 0))
            {
                _zoomFactor = target;
            }
            else
            {
                // 线性过渡：每毫秒移动 |战斗倍率 - 1| / 过渡毫秒数，到达目标即停（进出对称）。
                var span = Math.Abs(feel!.CombatZoomDelta - 1.0);
                var step = span * (dt * 1000.0) / blendMs;
                _zoomFactor = _zoomFactor < target ? Math.Min(target, _zoomFactor + step) : Math.Max(target, _zoomFactor - step);
            }

            ApplyZoom();
        }

        private void ApplyZoom()
        {
            if (_currentProfile == null)
            {
                return;
            }

            var zoom = _zoomFactor == 1.0
                ? _baseZoom
                : Math.Clamp(_baseZoom * _zoomFactor, _currentProfile.ZoomMin, _currentProfile.ZoomMax);
            _appliedZoomFactor = _zoomFactor;
            _camera.SetZoom(zoom);
        }

        public void SetZoom(double zoom)
        {
            if (_currentProfile == null)
            {
                throw new InvalidOperationException("SetZoom 前必须先 Configure 一个 CameraProfile");
            }

            var clamped = Math.Clamp(zoom, _currentProfile.ZoomMin, _currentProfile.ZoomMax);
            _baseZoom = clamped;
            if (_appliedZoomFactor == 1.0)
            {
                _camera.SetZoom(clamped);
            }
            else
            {
                _camera.SetZoom(Math.Clamp(clamped * _appliedZoomFactor, _currentProfile.ZoomMin, _currentProfile.ZoomMax));
            }
        }

        public void Shake(Id shakePresetId)
        {
            if (_currentProfile == null)
            {
                throw new InvalidOperationException("Shake 前必须先 Configure 一个 CameraProfile");
            }

            var presets = _currentProfile.ShakePresets;
            for (var i = 0; i < presets.Count; i++)
            {
                if (presets[i].Id.Equals(shakePresetId))
                {
                    var amplitude = presets[i].Amplitude;
                    if (Intensity != null)
                    {
                        amplitude *= Intensity.Get(FeelIntensityKind.Shake);
                        if (!(amplitude > 0))
                        {
                            return; // 玩家关闭震屏
                        }
                    }

                    // 相机侧总量上限（ADR-0148）：震屏幅度（世界单位）按参考镜头高度折成画面高度比例，与仍在衰减的冲击合成后不超过 shake_cap。
                    var cap = CurrentShakeCap();
                    if (cap > 0 && amplitude > 0)
                    {
                        var refHeight = CurrentShakeReferenceHeight();
                        var durationMs = presets[i].Duration * 1000.0;
                        var ratio = amplitude / refHeight;
                        var fitted = durationMs > 0 ? FitShake(ratio, durationMs, cap) : ratio;
                        if (fitted < ratio)
                        {
                            CapClampedCount++;
                            if (!(fitted > 0))
                            {
                                return;
                            }
                            ratio = fitted;
                            amplitude = fitted * refHeight;
                        }
                        if (durationMs > 0)
                        {
                            TrackShake(ratio, durationMs);
                        }
                    }

                    _camera.Shake(amplitude, presets[i].Duration, presets[i].Frequency);
                    return;
                }
            }

            throw new ArgumentException(
                $"震屏档位 \"{shakePresetId}\" 未在当前 profile \"{_currentProfile.Id}\" 的 shake_presets 登记",
                nameof(shakePresetId));
        }

        public void SwitchProfile(Id profileId)
        {
            if (!_profiles.TryGetValue(profileId, out var profile))
            {
                throw new ArgumentException($"profile \"{profileId}\" 未登记，请先 Configure/RegisterProfile", nameof(profileId));
            }

            Configure(profile);
        }

        /// <summary>见 <see cref="CameraHostOptions.ResetFollowOnSceneLoadFinished"/>：重置跟随目标，
        /// 由调用方在新场景初始化完成后重新 <see cref="Follow"/>（见 09 第 8 节）。
        /// <para>
        /// PRES-118-CAMERA 根治：重置之后，若 <see cref="CameraHostOptions.FollowTargetResolverOnReset"/>
        /// 已装配，立即调用它决定新的跟随目标——同一次处理内先重置后重新指定，不依赖调用方在
        /// <c>SceneRouter</c> PostLoad 钩子与 <c>scene.load_finished</c> 事件之间抢时序（见该属性
        /// 判断记录）。解析函数返回 <c>null</c> 时维持"已重置、无跟随目标"，与未装配解析函数时完全
        /// 一致。
        /// </para>
        /// </summary>
        private void OnSceneLoadFinished()
        {
            if (!_options.ResetFollowOnSceneLoadFinished)
            {
                return;
            }

            _followEntityId = null;
            _lastFollowPosition = null;
            _targetLostReported = false;

            var resolved = _options.FollowTargetResolverOnReset?.Invoke();
            if (resolved.HasValue)
            {
                _followEntityId = resolved.Value;
            }
        }

        private void OnEncounterPhaseChanged(EncounterPhaseChangedEvent evt)
        {
            if (_options.PhaseProfileSwitch == null)
            {
                return;
            }

            if (_options.PhaseProfileSwitch.TryGetValue(evt.NewPhase, out var profileId))
            {
                SwitchProfile(profileId);
            }
        }
    }
}
