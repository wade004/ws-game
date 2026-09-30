using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.SaveSystem;
using Presentation.VfxSfx.Contracts;

namespace Presentation.VfxSfx.Core
{
    /// <summary>
    /// <see cref="IAudioLayerVolumeHost"/> 的默认实现（缺口 12，见接口注释）。
    /// <para>
    /// 判断记录（层清单来源）：任务书"层清单来自 sfx.def.layer 去重 + music"——sfx 层的音量落地经
    /// <see cref="ISfxPlayer.SetLayerVolume"/>（<c>SfxPlayer</c> 播放时按层查表乘算，见其源码）；
    /// 音乐没有独立的"层"概念，<see cref="IAudio"/> 只在 <see cref="AudioBus"/> 粒度提供
    /// <see cref="IAudio.SetBusVolume"/>，本类型把 <c>"music"</c> 固定映射到
    /// <see cref="AudioBus.Music"/>，作为 <see cref="Layers"/> 清单里唯一一个不经 <c>SfxPlayer</c>
    /// 而直接经总线音量落地的特殊层名（与 sfx 层同一份 <see cref="Layers"/> 列表统一暴露给 UI，UI
    /// 侧不需要区分"这是总线还是分层"）。
    /// </para>
    /// <para>
    /// 判断记录（持久化粒度）：<see cref="ISettingsStore"/> 是"整份文档读写"契约（见其类型注释），
    /// 本类型每次 <see cref="SetVolume"/> 都先 <c>Load()</c> 当前完整设置文档、只替换/追加
    /// <c>audio.volume.&lt;layer&gt;</c> 这一个键、其余键原样保留后整体 <c>Save()</c> 回去——不覆盖
    /// <c>ShellHost</c>/其它设置写入方已经写在同一份文档里的其它字段（如 <c>input_bindings</c>）。
    /// 构造期对称地 <c>Load()</c> 一次，把已持久化的音量立即应用到 <see cref="IAudio"/>/
    /// <see cref="ISfxPlayer"/>，使上次调整在本次启动时真正生效（不只是 <see cref="GetVolume"/>
    /// 读得到），对应任务书验收"重建宿主后恢复"。
    /// </para>
    /// </summary>
    public sealed class AudioLayerVolumeHost : IAudioLayerVolumeHost
    {
        /// <summary>音乐层的固定层名（见类型注释判断记录）。</summary>
        public const string MusicLayer = "music";

        private const double DefaultVolume = 1.0;

        private readonly IAudio _audio;
        private readonly ISfxPlayer _sfxPlayer;
        private readonly ISettingsStore _settingsStore;
        private readonly List<string> _layers;
        private readonly Dictionary<string, double> _volumes = new Dictionary<string, double>(StringComparer.Ordinal);

        public IReadOnlyList<string> Layers => _layers;

        /// <summary>ADR-0125 D21 新增：持久化失败（<see cref="ISettingsStore.Save"/> 返回 false）等降级路径的诊断出口；
        /// 4 参构造默认自建一份 <see cref="PresentationDiagnosticsRecorder"/>，装配根把 <c>SfxPlayer</c> 的诊断实例
        /// 传给 5 参构造，使这类警告与其它音频诊断一起被引擎侧轮询转发。</summary>
        public IPresentationDiagnostics Diagnostics { get; }

        public AudioLayerVolumeHost(
            IReadOnlyList<string> sfxLayers,
            ISfxPlayer sfxPlayer,
            IAudio audio,
            ISettingsStore settingsStore)
            : this(sfxLayers, sfxPlayer, audio, settingsStore, null)
        {
        }

        /// <summary>ADR-0125 D21 新增重载（旧 4 参签名原样保留并转调）：额外注入诊断出口。</summary>
        public AudioLayerVolumeHost(
            IReadOnlyList<string> sfxLayers,
            ISfxPlayer sfxPlayer,
            IAudio audio,
            ISettingsStore settingsStore,
            IPresentationDiagnostics? diagnostics)
        {
            if (sfxLayers == null) throw new ArgumentNullException(nameof(sfxLayers));
            _sfxPlayer = sfxPlayer ?? throw new ArgumentNullException(nameof(sfxPlayer));
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            Diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();

            _layers = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var layer in sfxLayers)
            {
                if (seen.Add(layer))
                {
                    _layers.Add(layer);
                }
            }
            if (seen.Add(MusicLayer))
            {
                _layers.Add(MusicLayer);
            }

            var data = _settingsStore.Load();
            foreach (var layer in _layers)
            {
                var volume = DefaultVolume;
                if (data.TryGetValue(VolumeKey(layer), out var value) && value is JsonNumber number
                    && !double.IsNaN(number.Value) && !double.IsInfinity(number.Value))
                {
                    // ADR-0125 D21：持久化文件可能被手改或来自旧版本（旧版本不夹取），恢复时同样夹取到 [0,1]；
                    // 非有限数值（如 1e999 解析为 Infinity）视为无效，退回默认满音量。
                    volume = LayerVolume.Clamp(number.Value);
                }
                _volumes[layer] = volume;
                ApplyToAudio(layer, volume);
            }
        }

        public double GetVolume(string layer) =>
            _volumes.TryGetValue(layer, out var volume) ? volume : DefaultVolume;

        public void SetVolume(string layer, double volume)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (!_volumes.ContainsKey(layer))
            {
                // ADR-0125 D21：未知层名不写入新键（此前会在内存表与设置文档里留下永远不会被读取/列出的孤儿键）。
                throw new ArgumentException($"未知音量层 \"{layer}\"，可用层见 Layers", nameof(layer));
            }

            // ADR-0125 D21：NaN/Infinity 抛 ArgumentOutOfRangeException；有限值夹取到 [0,1]，
            // 应用到后端与持久化的都是夹取后的值。
            volume = LayerVolume.Normalize(volume, nameof(volume));

            _volumes[layer] = volume;
            ApplyToAudio(layer, volume);
            Persist(layer, volume);
        }

        private void ApplyToAudio(string layer, double volume)
        {
            if (string.Equals(layer, MusicLayer, StringComparison.Ordinal))
            {
                _audio.SetBusVolume(AudioBus.Music, volume);
            }
            else
            {
                _sfxPlayer.SetLayerVolume(layer, volume);
            }
        }

        private void Persist(string layer, double volume)
        {
            var key = VolumeKey(layer);
            var data = _settingsStore.Load();

            var builder = new JsonObjectBuilder();
            var written = false;
            foreach (var entry in data)
            {
                if (string.Equals(entry.Key, key, StringComparison.Ordinal))
                {
                    builder.Add(key, new JsonNumber(volume));
                    written = true;
                }
                else
                {
                    builder.Add(entry.Key, entry.Value);
                }
            }
            if (!written)
            {
                builder.Add(key, new JsonNumber(volume));
            }

            if (!_settingsStore.Save(builder.Build()))
            {
                // ADR-0125 D21：此前 Save 的返回值被忽略。内存里与后端已生效的音量保留（本次调整当前会话仍然有效），
                // 只记一条诊断说明"重启后不会恢复"，不抛异常、不回滚。
                Diagnostics.Warn(
                    $"音量设置持久化失败：ISettingsStore.Save 返回 false，层 \"{layer}\" 的音量 {volume.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} " +
                    "本次会话内有效，但重启后不会恢复");
            }
        }

        private static string VolumeKey(string layer) => $"audio.volume.{layer}";
    }
}
