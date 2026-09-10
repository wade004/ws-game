using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SceneRouter;
using Core.Gameplay.Encounter;

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

        public CameraHost(
            ICamera camera,
            ICameraFollowTarget followTarget,
            IEventBus? bus = null,
            CameraHostOptions? options = null)
        {
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
        }

        public void Update(double alpha)
        {
            if (_currentProfile == null || _followEntityId == null)
            {
                return;
            }

            var pos = _followTarget.GetPosition(_followEntityId.Value, alpha);
            if (_currentProfile.Bounds.HasValue)
            {
                pos = _currentProfile.Bounds.Value.Clamp(pos);
            }

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
