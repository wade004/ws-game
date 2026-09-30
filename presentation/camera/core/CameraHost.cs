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
            _camera.SetZoom(profile.ZoomDefault);
        }

        public void Follow(Id entityId)
        {
            _followEntityId = entityId;
            _lastFollowPosition = null;
            _targetLostReported = false;
        }

        public void Update(double alpha)
        {
            if (_currentProfile == null || _followEntityId == null)
            {
                return;
            }

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
                    _camera.Follow(_lastFollowPosition.Value, _currentProfile.FollowLerp);
                }

                return;
            }

            _targetLostReported = false;

            if (_currentProfile.Bounds.HasValue)
            {
                pos = _currentProfile.Bounds.Value.Clamp(pos);
            }

            _lastFollowPosition = pos;
            _camera.Follow(pos, _currentProfile.FollowLerp);
        }

        public void SetZoom(double zoom)
        {
            if (_currentProfile == null)
            {
                throw new InvalidOperationException("SetZoom 前必须先 Configure 一个 CameraProfile");
            }

            var clamped = Math.Clamp(zoom, _currentProfile.ZoomMin, _currentProfile.ZoomMax);
            _camera.SetZoom(clamped);
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
