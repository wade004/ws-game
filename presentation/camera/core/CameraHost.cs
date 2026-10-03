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
        /// 合并、限频、上限截断由调用方（反馈包流水线）负责，本方法不再二次处理。
        /// </summary>
        public void Impulse(Vec2 direction, double magnitude, double decayMs)
        {
            if (!(magnitude > 0))
            {
                return;
            }

            var decay = decayMs > 1 ? decayMs : 1;
            if (_camera is ICameraImpulse impulse && impulse.SupportsCameraImpulse)
            {
                impulse.Impulse(direction, magnitude, decay);
                return;
            }

            if (!_impulseFallbackReported)
            {
                _impulseFallbackReported = true;
                _diagnostics.Warn(
                    "镜头冲击：适配层不支持 ICameraImpulse（supportsCameraImpulse 为假），退化为 ICamera.Shake" +
                    "（强度 = 画面高度比例 × 参考镜头高度；仅首次记录，之后同样退化）");
            }

            _camera.Shake(magnitude * CurrentShakeReferenceHeight(), decay / 1000.0, ImpulseFallbackShakeFrequency);
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
                    _camera.Shake(presets[i].Amplitude, presets[i].Duration, presets[i].Frequency);
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
