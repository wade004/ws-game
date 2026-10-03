using System;
using Core.Foundation.Common.Json;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 模拟轴（手柄摇杆、模拟扳机）的处理参数（手感设计/01 第 2.1 节末段，ADR-0143）：死区、响应曲线、平滑。
    /// 归设备与玩家设置，不归角色：作为 <c>found.input_action</c> 的可选字段（<c>dead_zone</c>/<c>response_curve</c>/<c>smoothing_ms</c>）声明默认值，
    /// 玩家设置可经 <see cref="InputMapHost.SetAxisProcessing"/> 覆盖（随设置文件的 <c>input_axis_settings</c> 持久化，不进存档）。
    /// <para>
    /// 语义：死区——摇杆向量长度不超过 <see cref="DeadZone"/> 时输出零，超过部分重标度到 0～1（<c>(len − dz) / (1 − dz)</c>，方向保持）；
    /// 响应曲线——对重标度后的幅值逐点映射（<c>linear</c> 恒等，<c>expo</c> 取平方，<c>custom:&lt;id&gt;</c> 取宿主提供的分段线性曲线，输入输出都是 0～1 的幅值）；
    /// 平滑——只作用于幅值且只作用于<b>下降沿</b>：幅值上升与方向变化即时生效，幅值下降时按"满幅在 <see cref="SmoothingMs"/> 内线性回落"限速。
    /// 因此"任何平滑不得吞掉短于一个 tick 的输入：一次按下至少产生一个 tick 的满幅意图"恒成立（上升沿不被平滑）。
    /// 只作用于模拟绑定（<c>pad_stick</c>/<c>pad_axis</c>）；键盘合成轴是数字输入，不经本处理。
    /// </para>
    /// </summary>
    public sealed class AxisProcessing
    {
        /// <summary>响应曲线取值：线性（恒等）。</summary>
        public const string Linear = "linear";

        /// <summary>响应曲线取值：指数（幅值平方）。</summary>
        public const string Expo = "expo";

        /// <summary>自定义曲线取值前缀：<c>custom:&lt;curve_id&gt;</c>。</summary>
        public const string CustomPrefix = "custom:";

        /// <summary>死区半径（0～1，不含 1）；0 即无死区。</summary>
        public double DeadZone { get; }

        /// <summary>响应曲线（<see cref="Linear"/>、<see cref="Expo"/>、<c>custom:&lt;id&gt;</c>）。</summary>
        public string ResponseCurve { get; }

        /// <summary>平滑时间（毫秒）：满幅回落到 0 所需时间；0 即不平滑。</summary>
        public double SmoothingMs { get; }

        /// <summary>三项都取缺省（无死区、线性、不平滑）：处理等价于原始值直通。</summary>
        public bool IsIdentity => DeadZone == 0.0 && SmoothingMs == 0.0 && ResponseCurve == Linear;

        public AxisProcessing(double deadZone = 0.0, string? responseCurve = null, double smoothingMs = 0.0)
        {
            if (!(deadZone >= 0.0 && deadZone < 1.0))
            {
                throw new ArgumentOutOfRangeException(nameof(deadZone), deadZone, "死区必须在 [0, 1) 内");
            }

            if (!(smoothingMs >= 0.0) || double.IsInfinity(smoothingMs))
            {
                throw new ArgumentOutOfRangeException(nameof(smoothingMs), smoothingMs, "平滑时间必须是非负有限数（毫秒）");
            }

            var curve = string.IsNullOrEmpty(responseCurve) ? Linear : responseCurve!;
            if (!IsValidCurve(curve))
            {
                throw new ArgumentException($"响应曲线取值 \"{curve}\" 不合法（{Linear}|{Expo}|{CustomPrefix}<curve_id>）", nameof(responseCurve));
            }

            DeadZone = deadZone;
            ResponseCurve = curve;
            SmoothingMs = smoothingMs;
        }

        /// <summary>响应曲线取值是否合法。</summary>
        public static bool IsValidCurve(string? value) =>
            value == Linear || value == Expo
            || (value != null && value.StartsWith(CustomPrefix, StringComparison.Ordinal) && value.Length > CustomPrefix.Length);

        /// <summary>是否自定义曲线；是则 <paramref name="curveId"/> 给出曲线 id。</summary>
        public bool TryGetCustomCurveId(out string curveId)
        {
            if (ResponseCurve.StartsWith(CustomPrefix, StringComparison.Ordinal))
            {
                curveId = ResponseCurve.Substring(CustomPrefix.Length);
                return true;
            }

            curveId = string.Empty;
            return false;
        }

        /// <summary>序列化成设置文件里的 JSON 对象（三项全写出）。</summary>
        public JsonObject ToJson() => new JsonObjectBuilder()
            .Add("dead_zone", new JsonNumber(DeadZone))
            .Add("response_curve", new JsonString(ResponseCurve))
            .Add("smoothing_ms", new JsonNumber(SmoothingMs))
            .Build();

        /// <summary>从设置文件里的 JSON 对象读回；缺项取缺省，类型不对或取值非法抛 <see cref="ArgumentException"/>。</summary>
        public static AxisProcessing FromJson(JsonObject json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            double deadZone = 0.0;
            double smoothing = 0.0;
            string? curve = null;
            if (json.TryGetValue("dead_zone", out var dz))
            {
                if (!(dz is JsonNumber dzn)) throw new ArgumentException("dead_zone 必须是数字", nameof(json));
                deadZone = dzn.Value;
            }

            if (json.TryGetValue("smoothing_ms", out var sm))
            {
                if (!(sm is JsonNumber smn)) throw new ArgumentException("smoothing_ms 必须是数字", nameof(json));
                smoothing = smn.Value;
            }

            if (json.TryGetValue("response_curve", out var rc))
            {
                if (!(rc is JsonString rcs)) throw new ArgumentException("response_curve 必须是字符串", nameof(json));
                curve = rcs.Value;
            }

            return new AxisProcessing(deadZone, curve, smoothing);
        }
    }
}
