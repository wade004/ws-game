using System;
using Core.Foundation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.VfxSfx.Contracts;

namespace Presentation.FeedbackBinder.Core
{
    /// <summary>
    /// <see cref="IFeedbackSink"/> 的默认实现（见 09_表现层.md 第 6.1 节"本模块只发指令"）：
    /// <c>play_vfx</c>/<c>play_sfx</c> 转给注入的 <see cref="Presentation.VfxSfx.Contracts.IVfxPlayer"/>/
    /// <see cref="Presentation.VfxSfx.Contracts.ISfxPlayer"/>；其余四种（飘字/顿帧/震屏/闪白）转给
    /// 调用方注入的委托——这四种动作的具体落地（UI 控件池、tick 节奏、镜头/材质参数）不属于
    /// <c>vfx_sfx</c>/<c>feedback_binder</c> 两个模块的契约范围，见 feedback_binder/README.md。
    /// </summary>
    public sealed class CompositeFeedbackSink : IFeedbackSink
    {
        private readonly IVfxPlayer _vfxPlayer;
        private readonly ISfxPlayer _sfxPlayer;
        private readonly EntityPositionResolver? _entityPositionResolver;
        private readonly Action<Id, Id, string> _onFloatingText;
        private readonly Action<double> _onFreeze;
        private readonly Action<Id> _onShakeCamera;
        private readonly Action<Id, Id> _onFlash;
        private readonly Presentation.VfxSfx.Contracts.IPresentationDiagnostics _diagnostics;

        public CompositeFeedbackSink(
            IVfxPlayer vfxPlayer,
            ISfxPlayer sfxPlayer,
            Action<Id, Id, string> onFloatingText,
            Action<double> onFreeze,
            Action<Id> onShakeCamera,
            Action<Id, Id> onFlash,
            EntityPositionResolver? entityPositionResolver = null,
            Presentation.VfxSfx.Contracts.IPresentationDiagnostics? diagnostics = null)
        {
            _vfxPlayer = vfxPlayer ?? throw new ArgumentNullException(nameof(vfxPlayer));
            _sfxPlayer = sfxPlayer ?? throw new ArgumentNullException(nameof(sfxPlayer));
            _onFloatingText = onFloatingText ?? throw new ArgumentNullException(nameof(onFloatingText));
            _onFreeze = onFreeze ?? throw new ArgumentNullException(nameof(onFreeze));
            _onShakeCamera = onShakeCamera ?? throw new ArgumentNullException(nameof(onShakeCamera));
            _onFlash = onFlash ?? throw new ArgumentNullException(nameof(onFlash));
            _entityPositionResolver = entityPositionResolver;
            _diagnostics = diagnostics ?? new Presentation.VfxSfx.Contracts.PresentationDiagnosticsRecorder();
        }

        public void FloatingText(Id entityId, Id styleId, string text) => _onFloatingText(entityId, styleId, text);

        public void PlayVfx(Id vfxId, FeedbackAttachSpec attach)
        {
            var vfxAttach = ResolveVfxAttach(vfxId, attach);
            if (vfxAttach == null)
            {
                return;
            }

            _vfxPlayer.Spawn(vfxId, vfxAttach.Value, null);
        }

        public void PlaySfx(Id sfxId, Vec2? at) => _sfxPlayer.Play(sfxId, at);

        public void Freeze(double durationMs) => _onFreeze(durationMs);

        public void ShakeCamera(Id profileId) => _onShakeCamera(profileId);

        public void Flash(Id entityId, Id profileId) => _onFlash(entityId, profileId);

        private VfxAttach? ResolveVfxAttach(Id vfxId, FeedbackAttachSpec attach)
        {
            if (attach.Target == FeedbackAttachTarget.World)
            {
                if (attach.WorldPosition.HasValue)
                {
                    return VfxAttach.World(attach.WorldPosition.Value);
                }

                _diagnostics.Warn($"vfx \"{vfxId}\" 的反馈动作 attach=world 但没有可用世界坐标，跳过播放");
                return null;
            }

            var entityId = attach.EntityId!.Value;

            if (attach.AnchorId.HasValue)
            {
                return VfxAttach.Anchor(entityId, attach.AnchorId.Value);
            }

            var pos = _entityPositionResolver?.Invoke(entityId);
            if (pos.HasValue)
            {
                return VfxAttach.World(pos.Value);
            }

            _diagnostics.Warn($"vfx \"{vfxId}\" 的反馈动作未声明 anchor_id 且查不到实体 \"{entityId}\" 的位置，跳过播放");
            return null;
        }
    }
}
