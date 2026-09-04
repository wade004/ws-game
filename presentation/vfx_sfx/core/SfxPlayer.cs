using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Rng;
using Presentation.VfxSfx.Contracts;

namespace Presentation.VfxSfx.Core
{
    /// <summary>
    /// <see cref="ISfxPlayer"/> 的默认实现（见 09_表现层.md 第 5.3、5.5 节）：查 <c>sfx.def</c>、
    /// <c>variants</c> 非空时用 <see cref="IRngHost"/>（独立于规则层随机流，见
    /// <see cref="SfxOptions.RngStream"/>）随机挑一个变体、同层按 <c>priority</c> 抢占、经
    /// <see cref="IAudio"/> 播放（表现层铁律 P4）。
    /// <para>
    /// 判断记录（<c>priority</c> 大小方向，09 原文未定义）：本实现采用"数值越大优先级越高"，
    /// 未声明 <c>priority</c>（<see cref="SfxDef.Priority"/> 为 null）按最低优先级
    /// （<see cref="int.MinValue"/>）处理，即同层容量不足时最先被抢占停止。
    /// </para>
    /// <para>
    /// 契约缺口（见 vfx_sfx/README.md）：<see cref="ISfxPlayer.Play"/> 的 <c>at</c>（定位坐标）
    /// 在当前 <see cref="IAudio"/> 契约（<c>PlaySfx(Id, volume, pitch)</c>，无位置参数/无 3D
    /// 声像重载）下无处传递，本实现只记录不使用；需要位置化音频时需先在 L-1 补接口。
    /// </para>
    /// </summary>
    public sealed class SfxPlayer : ISfxPlayer
    {
        private sealed class ActivePlayback
        {
            public SfxHandle Handle;
            public string Layer = string.Empty;
            public int Priority;
            public long InsertionSeq;
        }

        private readonly IAudio _audio;
        private readonly IRngHost _rng;
        private readonly IReadOnlyDictionary<Id, SfxDef> _catalog;
        private readonly SfxOptions _options;
        private readonly IPresentationDiagnostics _diagnostics;

        private readonly Dictionary<string, List<ActivePlayback>> _activeByLayer = new Dictionary<string, List<ActivePlayback>>(StringComparer.Ordinal);
        private readonly Dictionary<SfxHandle, ActivePlayback> _byHandle = new Dictionary<SfxHandle, ActivePlayback>();
        private readonly Dictionary<string, double> _layerVolume = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly HashSet<string> _mutedLayers = new HashSet<string>(StringComparer.Ordinal);
        private long _seq;

        public SfxPlayer(
            IAudio audio,
            IRngHost rng,
            IReadOnlyDictionary<Id, SfxDef> catalog,
            SfxOptions? options = null,
            IPresentationDiagnostics? diagnostics = null)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _options = options ?? new SfxOptions();
            _diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();
        }

        public SfxHandle? Play(Id sfxId, Vec2? at)
        {
            if (!_catalog.TryGetValue(sfxId, out var def))
            {
                _diagnostics.Warn($"sfx.def 未登记 id=\"{sfxId}\"，跳过播放");
                return null;
            }

            var resourceRef = PickResource(def);
            var layer = def.Layer;
            var priority = def.Priority ?? int.MinValue;

            MakeRoomIfNeeded(layer);

            var volume = ResolveVolume(layer);
            var handle = _audio.PlaySfx(resourceRef, volume, pitch: 1.0);

            var playback = new ActivePlayback { Handle = handle, Layer = layer, Priority = priority, InsertionSeq = _seq++ };
            GetOrCreateLayerList(layer).Add(playback);
            _byHandle[handle] = playback;

            return handle;
        }

        public void Stop(SfxHandle handle)
        {
            if (_byHandle.TryGetValue(handle, out var playback))
            {
                RemoveFromLayer(playback);
                _byHandle.Remove(handle);
            }

            _audio.StopSfx(handle);
        }

        public void SetLayerVolume(string layer, double volume)
        {
            _layerVolume[layer] = volume;
        }

        public void SetLayerMuted(string layer, bool muted)
        {
            if (muted)
            {
                _mutedLayers.Add(layer);
            }
            else
            {
                _mutedLayers.Remove(layer);
            }
        }

        private Id PickResource(SfxDef def)
        {
            if (def.Variants == null || def.Variants.Count == 0)
            {
                return def.ResourceRef;
            }

            var index = _rng.NextInt(_options.RngStream, 0, def.Variants.Count - 1);
            return def.Variants[index];
        }

        private double ResolveVolume(string layer)
        {
            if (_mutedLayers.Contains(layer))
            {
                return 0.0;
            }

            return _layerVolume.TryGetValue(layer, out var v) ? v : 1.0;
        }

        private void MakeRoomIfNeeded(string layer)
        {
            var max = _options.MaxConcurrentPerLayer.TryGetValue(layer, out var m) ? m : _options.DefaultMaxConcurrent;
            if (max <= 0)
            {
                return;
            }

            var list = GetOrCreateLayerList(layer);
            if (list.Count < max)
            {
                return;
            }

            var victimIndex = 0;
            var victim = list[0];
            for (var i = 1; i < list.Count; i++)
            {
                var candidate = list[i];
                if (candidate.Priority < victim.Priority ||
                    (candidate.Priority == victim.Priority && candidate.InsertionSeq < victim.InsertionSeq))
                {
                    victim = candidate;
                    victimIndex = i;
                }
            }

            list.RemoveAt(victimIndex);
            _byHandle.Remove(victim.Handle);
            _audio.StopSfx(victim.Handle);
        }

        private void RemoveFromLayer(ActivePlayback playback)
        {
            if (_activeByLayer.TryGetValue(playback.Layer, out var list))
            {
                list.Remove(playback);
            }
        }

        private List<ActivePlayback> GetOrCreateLayerList(string layer)
        {
            if (!_activeByLayer.TryGetValue(layer, out var list))
            {
                list = new List<ActivePlayback>();
                _activeByLayer[layer] = list;
            }
            return list;
        }
    }
}
