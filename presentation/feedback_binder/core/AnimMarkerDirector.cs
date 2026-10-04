using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;

namespace Presentation.FeedbackBinder.Core
{
    /// <summary>
    /// <see cref="AnimMarkerDirector"/> 的可选项（全部有缺省；缺项对应的能力静默关闭）。
    /// </summary>
    public sealed class AnimMarkerOptions
    {
        /// <summary>手感来源（脚步强度档、拖尾/残影开关与拖尾特效引用）；null 时脚步与拖尾都不工作（<c>fx:</c> 与 <c>impact</c> 不依赖它）。</summary>
        public IImpactFeelSource? FeelSource { get; set; }

        /// <summary>手感音效层索引；null 时脚步不发声。</summary>
        public SfxLayerIndex? SfxLayers { get; set; }

        /// <summary>实体世界位置；脚步音效位置、世界挂接的拖尾位置用。</summary>
        public Func<Id, Vec2?>? PositionResolver { get; set; }

        /// <summary>单位脚下的地面材质（地图 <c>surface_materials</c> 区域标签，没有区域返回 null = 通用材质）。</summary>
        public Func<Id, string?>? MaterialResolver { get; set; }

        /// <summary>特效 id → 它的挂接方式（<c>vfx.def.attach_mode</c>）；拖尾据此决定怎么挂。null 或查不到时拖尾记诊断并跳过。</summary>
        public Func<Id, VfxAttachMode?>? VfxAttachModeOf { get; set; }

        /// <summary>拖尾特效是锚点挂接（<c>attach_mode = anchor</c>）时挂在持有者的哪个锚点；缺省 <c>anchor.hand_main</c>。</summary>
        public Id TrailAnchorId { get; set; } = new Id("anchor.hand_main");

        /// <summary>残影开关回调：<c>(单位, 开/关)</c>。由 <c>trail_start/trail_end</c> 标记在手感字段 <c>afterimage_enabled</c> 为真时驱动。</summary>
        public Action<Id, bool>? OnAfterimage { get; set; }
    }

    /// <summary>
    /// 动画表现类时间标记的消费方（手感设计/04 第 5 节，07 第 1、3 节，ADR-0148）：订阅 <see cref="IAnimMarkerSource"/>，
    /// <list type="bullet">
    /// <item><c>footstep</c>：脚步声。按单位手感字段 <c>sfx_footstep_tier</c> 与脚下地面材质（地图 <c>surface_materials</c>）经
    /// <see cref="SfxLayerIndex"/> 选 <c>sfx.def</c> 行，在单位位置播放；档位 0 即关闭。</item>
    /// <item><c>trail_start</c>/<c>trail_end</c>：拖尾。手感字段 <c>trail_enabled</c> 与 <c>trail_ref</c>（<c>vfx.def</c> 行）齐备时，
    /// 开始标记播该特效（锚点挂接跟随持有者，世界挂接取开始时刻的位置），结束标记停掉；<c>afterimage_enabled</c> 同步开关残影。</item>
    /// <item><c>fx:&lt;id&gt;</c>：在单位位置播 <c>vfx.def</c> 行 <c>&lt;id&gt;</c>（特效挂点触发）。</item>
    /// <item><c>impact</c>：放行以 <see cref="IAnimMarkerGate"/> 推迟到该标记的闪白（变体 <c>flash.sync = impact_marker</c>）。</item>
    /// </list>
    /// 脚步声由本类按标记唯一产生；标记缺失的单位（没有动画剪辑声明 <c>footstep</c>）退回 <c>unit.stride_completed</c> 几何步幅，
    /// 谁抑制谁由装配根的 <c>StrideEmitter.Suppress</c> 决定，本类不碰。
    /// </summary>
    public sealed class AnimMarkerDirector : IAnimMarkerGate, IDisposable
    {
        private sealed class ActiveTrail
        {
            public Id VfxId;
            public FeedbackAttachSpec Attach;
            public bool Afterimage;
        }

        private sealed class PendingGate
        {
            public Id Entity;
            public string Marker = string.Empty;
            public Action Action = null!;
            public double RemainingSeconds;
        }

        private readonly IAnimMarkerSource _source;
        private readonly IFeedbackSink _sink;
        private readonly AnimMarkerOptions _options;
        private readonly IPresentationDiagnostics _diagnostics;
        private readonly Dictionary<Id, ActiveTrail> _trails = new Dictionary<Id, ActiveTrail>();
        private readonly List<PendingGate> _gates = new List<PendingGate>();
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);
        private bool _disposed;

        /// <summary>已播放的脚步声次数（测试与诊断用）。</summary>
        public int FootstepCount { get; private set; }

        /// <summary>当前仍处于拖尾中的单位数。</summary>
        public int ActiveTrailCount => _trails.Count;

        /// <summary>当前等待标记的推迟项数。</summary>
        public int PendingGateCount => _gates.Count;

        public AnimMarkerDirector(
            IAnimMarkerSource source, IFeedbackSink sink, AnimMarkerOptions? options = null, IPresentationDiagnostics? diagnostics = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _options = options ?? new AnimMarkerOptions();
            _diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();
            _source.AnimMarkerReached += OnMarker;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _source.AnimMarkerReached -= OnMarker;
        }

        /// <summary>推进推迟项的超时（秒）；到期的照常执行。</summary>
        public void Update(double dt)
        {
            if (!(dt > 0) || _gates.Count == 0) return;
            for (var i = 0; i < _gates.Count; i++)
            {
                _gates[i].RemainingSeconds -= dt;
            }
            for (var i = 0; i < _gates.Count;)
            {
                if (_gates[i].RemainingSeconds <= 0)
                {
                    var gate = _gates[i];
                    _gates.RemoveAt(i);
                    Run(gate);
                }
                else
                {
                    i++;
                }
            }
        }

        public void DeferUntilMarker(Id entityId, string marker, Action action, double timeoutSeconds)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (!(timeoutSeconds > 0))
            {
                action();
                return;
            }
            _gates.Add(new PendingGate { Entity = entityId, Marker = marker, Action = action, RemainingSeconds = timeoutSeconds });
        }

        /// <summary>单位销毁/场景清理时调用：停掉它还在播的拖尾、丢掉等它的推迟项（不执行）。</summary>
        public void Forget(Id entityId)
        {
            if (_trails.TryGetValue(entityId, out var trail))
            {
                _trails.Remove(entityId);
                StopTrail(entityId, trail);
            }
            _gates.RemoveAll(g => g.Entity.Equals(entityId));
        }

        private void OnMarker(Id entityId, string marker)
        {
            try
            {
                switch (marker)
                {
                    case AnimMarkerNames.Footstep:
                        PlayFootstep(entityId);
                        return;
                    case AnimMarkerNames.TrailStart:
                        StartTrail(entityId);
                        return;
                    case AnimMarkerNames.TrailEnd:
                        EndTrail(entityId);
                        return;
                    case AnimMarkerNames.Impact:
                        ReleaseGates(entityId, marker);
                        return;
                }

                if (AnimMarkerNames.TryGetFxId(marker, out var fxText))
                {
                    PlayFx(entityId, fxText);
                    return;
                }

                // 其它标记（hit_frame、active_* 等）不归本类管；同样放行等它们的推迟项（目前只有 impact 会被推迟，保持通用）。
                ReleaseGates(entityId, marker);
            }
            catch (Exception ex)
            {
                _diagnostics.Warn($"动画标记 \"{marker}\"（实体 \"{entityId}\"）处理时抛出异常，已跳过：{ex}");
            }
        }

        // ------------------------------------------------------------------
        // footstep
        // ------------------------------------------------------------------

        private void PlayFootstep(Id entityId)
        {
            var feel = _options.FeelSource?.Get(entityId);
            if (feel == null) return;
            var tier = feel.TierOf(SfxFeelLayer.Footstep);
            if (tier <= 0) return;
            if (_options.SfxLayers == null)
            {
                ReportOnce("footstep:index", "脚步标记到达但没有注入 SfxLayerIndex（AnimMarkerOptions.SfxLayers），脚步不发声");
                return;
            }

            var material = _options.MaterialResolver?.Invoke(entityId) ?? SfxLayerIndex.GenericMaterial;
            if (!_options.SfxLayers.TryResolve(SfxFeelLayer.Footstep, tier, material, out var resolved))
            {
                return;
            }

            _sink.PlaySfx(resolved.SfxId, _options.PositionResolver?.Invoke(entityId));
            FootstepCount++;
        }

        // ------------------------------------------------------------------
        // trail / afterimage
        // ------------------------------------------------------------------

        private void StartTrail(Id entityId)
        {
            var feel = _options.FeelSource?.Get(entityId);
            if (feel == null) return;
            if (!feel.TrailEnabled && !feel.AfterimageEnabled) return;

            if (_trails.TryGetValue(entityId, out var running))
            {
                _trails.Remove(entityId);
                StopTrail(entityId, running);
            }

            var trail = new ActiveTrail();
            var started = false;
            if (feel.TrailEnabled && feel.TrailRef.HasValue)
            {
                var vfxId = feel.TrailRef.Value;
                if (TryBuildTrailAttach(entityId, vfxId, out var attach))
                {
                    _sink.PlayVfx(vfxId, attach);
                    trail.VfxId = vfxId;
                    trail.Attach = attach;
                    started = true;
                }
            }
            else if (feel.TrailEnabled)
            {
                ReportOnce("trail:ref:" + entityId.Value, $"单位 \"{entityId}\" 的 trail_enabled 为真但没有 trail_ref（拖尾特效），拖尾不播");
            }

            if (feel.AfterimageEnabled && _options.OnAfterimage != null)
            {
                _options.OnAfterimage(entityId, true);
                trail.Afterimage = true;
                started = true;
            }

            if (started)
            {
                _trails[entityId] = trail;
            }
        }

        private void EndTrail(Id entityId)
        {
            if (_trails.TryGetValue(entityId, out var trail))
            {
                _trails.Remove(entityId);
                StopTrail(entityId, trail);
            }
        }

        private void StopTrail(Id entityId, ActiveTrail trail)
        {
            if (trail.VfxId.Value != null)
            {
                _sink.StopVfx(trail.VfxId, trail.Attach);
            }
            if (trail.Afterimage)
            {
                _options.OnAfterimage?.Invoke(entityId, false);
            }
        }

        private bool TryBuildTrailAttach(Id entityId, Id vfxId, out FeedbackAttachSpec attach)
        {
            attach = default;
            var mode = _options.VfxAttachModeOf?.Invoke(vfxId);
            if (mode == null)
            {
                ReportOnce("trail:def:" + vfxId.Value, $"拖尾特效 \"{vfxId}\" 在 vfx.def 里查不到（或没有注入 VfxAttachModeOf），拖尾不播");
                return false;
            }

            switch (mode.Value)
            {
                case VfxAttachMode.Anchor:
                    attach = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, entityId, _options.TrailAnchorId);
                    return true;
                case VfxAttachMode.World:
                    attach = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, entityId, null);
                    return true;
                default:
                    ReportOnce("trail:mode:" + vfxId.Value, $"拖尾特效 \"{vfxId}\" 的 attach_mode 是 {mode.Value}，拖尾只支持 anchor（跟随持有者）与 world，已跳过");
                    return false;
            }
        }

        // ------------------------------------------------------------------
        // fx:<id>
        // ------------------------------------------------------------------

        private void PlayFx(Id entityId, string fxText)
        {
            if (!Id.TryParse(fxText, out var vfxId))
            {
                ReportOnce("fx:bad:" + fxText, $"动画标记 \"fx:{fxText}\" 的特效 id 格式非法，已忽略");
                return;
            }

            var mode = _options.VfxAttachModeOf?.Invoke(vfxId);
            if (mode == VfxAttachMode.Anchor)
            {
                _sink.PlayVfx(vfxId, FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, entityId, _options.TrailAnchorId));
                return;
            }
            _sink.PlayVfx(vfxId, FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, entityId, null));
        }

        // ------------------------------------------------------------------
        // 推迟项
        // ------------------------------------------------------------------

        private void ReleaseGates(Id entityId, string marker)
        {
            if (_gates.Count == 0) return;
            for (var i = 0; i < _gates.Count;)
            {
                var gate = _gates[i];
                if (gate.Entity.Equals(entityId) && string.Equals(gate.Marker, marker, StringComparison.Ordinal))
                {
                    _gates.RemoveAt(i);
                    Run(gate);
                }
                else
                {
                    i++;
                }
            }
        }

        private void Run(PendingGate gate)
        {
            try
            {
                gate.Action();
            }
            catch (Exception ex)
            {
                _diagnostics.Warn($"等待动画标记 \"{gate.Marker}\" 的推迟项执行时抛出异常，已跳过：{ex}");
            }
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
