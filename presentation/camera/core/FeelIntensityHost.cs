using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Presentation.VfxSfx.Contracts;

namespace Presentation.Camera
{
    /// <summary>
    /// 玩家手感强度宿主（ADR-0148）：四个系数（震屏、镜头冲击、闪白、手柄震动）读写设置文件
    /// （<see cref="ISettingsStore"/>，键 <c>feel.intensity.*</c>），缺省 1，夹在 [0, 1]。持久化粒度同
    /// <c>AudioLayerVolumeHost</c>：每次 <see cref="Set"/> 先读整份文档、只替换本键、再整体写回，不覆盖其它设置键。
    /// 另提供 <see cref="GetSetting"/> 给反馈包流水线的"命名强度设置"（<c>camera_user_intensity_setting</c>）读任意设置键。
    /// </summary>
    public sealed class FeelIntensityHost : IFeelIntensity
    {
        private readonly ISettingsStore _store;
        private readonly IPresentationDiagnostics _diagnostics;
        private readonly double[] _values = { 1.0, 1.0, 1.0, 1.0 };

        public FeelIntensityHost(ISettingsStore store, IPresentationDiagnostics? diagnostics = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();
            var data = _store.Load();
            foreach (FeelIntensityKind kind in Enum.GetValues(typeof(FeelIntensityKind)))
            {
                _values[(int)kind] = ReadClamped(data, FeelIntensityKeys.Of(kind), 1.0);
            }
        }

        public double Get(FeelIntensityKind kind) => _values[(int)kind];

        /// <summary>设置一个强度系数（夹到 [0, 1]；非有限数抛 <see cref="ArgumentOutOfRangeException"/>）并持久化。</summary>
        public void Set(FeelIntensityKind kind, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "强度必须是有限数");
            }
            value = value < 0 ? 0 : (value > 1 ? 1 : value);
            _values[(int)kind] = value;

            var key = FeelIntensityKeys.Of(kind);
            var data = _store.Load();
            var builder = new JsonObjectBuilder();
            var written = false;
            foreach (var entry in data)
            {
                if (string.Equals(entry.Key, key, StringComparison.Ordinal))
                {
                    builder.Add(key, new JsonNumber(value));
                    written = true;
                }
                else
                {
                    builder.Add(entry.Key, entry.Value);
                }
            }
            if (!written)
            {
                builder.Add(key, new JsonNumber(value));
            }
            if (!_store.Save(builder.Build()))
            {
                _diagnostics.Warn($"手感强度设置持久化失败：ISettingsStore.Save 返回 false，\"{key}\" = {value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} 本次会话内有效，重启后不会恢复");
            }
        }

        /// <summary>读任意命名强度设置（0..1，缺省 1）。每次读设置文档，调用频率低（只在命中批出口）。</summary>
        public double GetSetting(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            return ReadClamped(_store.Load(), name, 1.0);
        }

        private static double ReadClamped(JsonObject data, string key, double fallback)
        {
            if (data.TryGetValue(key, out var value) && value is JsonNumber number
                && !double.IsNaN(number.Value) && !double.IsInfinity(number.Value))
            {
                return number.Value < 0 ? 0 : (number.Value > 1 ? 1 : number.Value);
            }
            return fallback;
        }
    }
}
