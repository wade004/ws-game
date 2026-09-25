using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.FeedbackBinder.Contracts;
using Presentation.VfxSfx.Contracts;

namespace Presentation.FeedbackBinder.Core
{
    /// <summary>
    /// <see cref="IFeedbackSink"/> 的默认实现（见 09_表现层.md 第 6.1 节"本模块只发指令"）：
    /// <c>play_vfx</c>/<c>stop_vfx</c>/<c>play_sfx</c> 转给注入的
    /// <see cref="Presentation.VfxSfx.Contracts.IVfxPlayer"/>/<see cref="Presentation.VfxSfx.Contracts.ISfxPlayer"/>
    /// （<c>stop_vfx</c> 见 ADR-0075、<see cref="StopVfx"/> 判断记录）；其余四种（飘字/顿帧/震屏/闪白）
    /// 转给调用方注入的委托——这四种动作的具体落地（UI 控件池、tick 节奏、镜头/材质参数）不属于
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

        /// <summary>ADR-0075：(vfxId, 附着实体) → 当前仍在播的粒子句柄，供 <see cref="StopVfx"/> 按同一
        /// 对键定位。判断记录（为什么是"覆盖最近一次"而不是维护一个列表"全停"）：同一 (vfxId, 实体)
        /// 组合再次 <see cref="PlayVfx"/>（如同一光环刷新/重新施加）时，新句柄直接覆盖旧记录——本类型
        /// 不据此反过来停止旧实例（旧实例是否要跟着停不是本类型能替上层拍板的语义，交给调用方自己先发
        /// <c>stop_vfx</c> 再发 <c>play_vfx</c>），<see cref="StopVfx"/> 只停止"当前记录着的这一个"
        /// （最近一次）；选这个规则而不是维护每对键一份列表，是因为字典大小因此只随"当前有多少对不同
        /// 的 (vfxId, 实体) 组合"增长（同一对键反复重播不增长），不会像列表方案那样在"从未调用
        /// <c>stop_vfx</c> 的同一对键反复播放"这一常见场景下无界增长——ADR-0075 动机场景（眩晕特效随
        /// 光环状态显隐）本就是同一对键至多一个逻辑实例在播，这条规则已完全覆盖。只在
        /// <see cref="FeedbackAttachSpec.EntityId"/> 有值（<c>attach</c> 不是 world）时才登记，见
        /// <see cref="PlayVfx"/>。</summary>
        private readonly Dictionary<(Id VfxId, Id EntityId), ParticleHandle> _activeVfxByKey =
            new Dictionary<(Id, Id), ParticleHandle>();

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

            // N17 根治：把 vfx/sfx 两路"pending 计数可能变化"的信号汇聚成统一的一路，供
            // FeedbackBinder 订阅——见 IFeedbackSink.PendingPlaybackChanged 判断记录。
            _vfxPlayer.PendingSpawnCountChanged += RaisePendingPlaybackChanged;
            _sfxPlayer.PendingPlayCountChanged += RaisePendingPlaybackChanged;
        }

        private void RaisePendingPlaybackChanged() => PendingPlaybackChanged?.Invoke();

        public void FloatingText(Id entityId, Id styleId, string text) => _onFloatingText(entityId, styleId, text);

        public void PlayVfx(Id vfxId, FeedbackAttachSpec attach)
        {
            var vfxAttach = ResolveVfxAttach(vfxId, attach);
            if (vfxAttach == null)
            {
                return;
            }

            var handle = _vfxPlayer.Spawn(vfxId, vfxAttach.Value, null);

            // ADR-0075：只在这次播放确有实体可键（attach 不是 world）且确实产生了句柄（未被
            // vfx.def 未登记/挂接目标不可解析等原因跳过，见 IVfxPlayer.Spawn 判断记录"返回 null"）
            // 时才登记，供 StopVfx 按同一对键定位，见 _activeVfxByKey 判断记录。
            if (handle.HasValue && attach.EntityId.HasValue)
            {
                _activeVfxByKey[(vfxId, attach.EntityId.Value)] = handle.Value;
            }
        }

        /// <summary>ADR-0075：按 (<paramref name="vfxId"/>, <paramref name="attach"/> 的附着实体) 定位
        /// <see cref="PlayVfx"/> 登记过的当前在播实例并停止；<see cref="IVfxPlayer.Stop"/> 契约本身承诺
        /// "handle 不存在/已停止时安全忽略"，因此即便记录的句柄已经自然超时回收，本方法也不会抛异常
        /// （见 <c>Presentation.VfxSfx.Core.VfxPlayer.StopInternal</c> 判断记录）。<paramref name="attach"/>
        /// 没有实体（world）或这对键从未登记过（没播过/已经被停过一次）时静默返回，不写诊断——"没有
        /// 在播实例"是数据驱动路径下完全正常会发生的时序，不是缺陷信号（见 ADR-0075 验收标准）。</summary>
        public void StopVfx(Id vfxId, FeedbackAttachSpec attach)
        {
            if (!attach.EntityId.HasValue)
            {
                return;
            }

            var key = (vfxId, attach.EntityId.Value);
            if (!_activeVfxByKey.TryGetValue(key, out var handle))
            {
                return;
            }

            _activeVfxByKey.Remove(key);
            _vfxPlayer.Stop(handle);
        }

        public void PlaySfx(Id sfxId, Vec2? at) => _sfxPlayer.Play(sfxId, at);

        /// <summary>ADR-0089：<paramref name="attach"/> 有具体实体时转给
        /// <see cref="ISfxPlayer.PlayAttached"/>（是否真正登记跟踪由 <c>sfx.def.loop</c> 决定，见该
        /// 方法判断记录）；world（<see cref="FeedbackAttachSpec.EntityId"/> 为 null）时退化为
        /// <see cref="PlaySfx(Id, Vec2?)"/>，与本方法新增前的既有行为一致。</summary>
        public void PlaySfx(Id sfxId, Vec2? at, FeedbackAttachSpec attach)
        {
            if (attach.EntityId.HasValue)
            {
                _sfxPlayer.PlayAttached(sfxId, attach.EntityId.Value, at);
                return;
            }

            _sfxPlayer.Play(sfxId, at);
        }

        /// <summary>ADR-0089：<paramref name="attach"/> 没有实体（world）时静默返回（同
        /// <see cref="StopVfx"/> 惯例——理论上数据驱动路径已经在 <c>StopSfxAction</c> 构造期拒绝
        /// world，这里仍保留防御性判断），否则转给 <see cref="ISfxPlayer.StopAttached"/>。</summary>
        public void StopSfx(Id sfxId, FeedbackAttachSpec attach)
        {
            if (!attach.EntityId.HasValue)
            {
                return;
            }

            _sfxPlayer.StopAttached(sfxId, attach.EntityId.Value);
        }

        public void Freeze(double durationMs) => _onFreeze(durationMs);

        public void ShakeCamera(Id profileId) => _onShakeCamera(profileId);

        public void Flash(Id entityId, Id profileId) => _onFlash(entityId, profileId);

        /// <summary>GP-09 根治：直接转发 <see cref="IVfxPlayer.PendingSpawnCount"/>/
        /// <see cref="ISfxPlayer.PendingPlayCount"/>，见 <see cref="IFeedbackSink.HasPendingPlayback"/>
        /// 判断记录。</summary>
        public bool HasPendingPlayback => _vfxPlayer.PendingSpawnCount > 0 || _sfxPlayer.PendingPlayCount > 0;

        /// <summary>N17 根治：见类型构造函数判断记录，直接转发 vfx/sfx 两路信号。</summary>
        public event Action? PendingPlaybackChanged;

        /// <summary>诊断转发到引擎控制台跟进第三批（presentation/assembly/README.md 判断记录 10
        /// 追加）：对外暴露构造期注入（或默认自建）的 <see cref="Presentation.VfxSfx.Contracts.IPresentationDiagnostics"/>
        /// 实例，供 <c>adapters/unity</c> 侧轮询转发到引擎控制台——本类型此前虽然已经接受可选构造参数
        /// <c>diagnostics</c>（用于 <see cref="ResolveVfxAttach"/> 找不到可用坐标时记警告），却一直没有
        /// 公开出口，是第二批（<c>VfxPlayer</c>/<c>SfxPlayer</c>/<c>FeedbackBinder</c>/<c>ViewBinder</c>）
        /// 落地时扫漏的第五条可达诊断源。ABI 只新增只读属性。</summary>
        public Presentation.VfxSfx.Contracts.IPresentationDiagnostics Diagnostics => _diagnostics;

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
