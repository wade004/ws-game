using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Presentation.VfxSfx.Contracts;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>
    /// 打击反馈包变体的命中结局维度（手感设计/07 第 1 节）。<see cref="Whiff"/> 是本实现对设计的加法：挥空
    /// （判定相结束时零命中）没有"命中"可选变体，允许反馈包为它单独登记一条（缺省只播 <c>whiff</c> 音效层）。
    /// </summary>
    public enum ImpactOutcome
    {
        Hit,
        Crit,
        Kill,
        Avoided,
        Whiff,
    }

    /// <summary>反馈包特效的挂接位置（手感设计/07 第 1 节 <c>vfx.attach</c>）。</summary>
    public enum ImpactVfxAttach
    {
        /// <summary>接触点（世界坐标，缺接触点时退回目标实体位置）。</summary>
        Contact,
        Target,
        Source,
    }

    /// <summary>反馈包特效的朝向来源。</summary>
    public enum ImpactVfxOrient
    {
        None,
        ContactNormal,
        WorldDirection,
    }

    /// <summary>顿帧期间冻结哪些表现层（手感设计/07 第 5 节）：骨骼/序列帧恒冻，粒子与拖尾按反馈包声明。</summary>
    public readonly struct ImpactFreezeLayers : IEquatable<ImpactFreezeLayers>
    {
        /// <summary>骨骼/序列帧时间轴（设计规定必冻，恒为真）。</summary>
        public bool Skeleton => true;

        public bool Particles { get; }

        public bool Trail { get; }

        public ImpactFreezeLayers(bool particles, bool trail)
        {
            Particles = particles;
            Trail = trail;
        }

        /// <summary>缺省：只冻骨骼/序列帧，粒子与拖尾照常（顿帧最小侵入）。</summary>
        public static ImpactFreezeLayers Default => new ImpactFreezeLayers(false, false);

        public bool Equals(ImpactFreezeLayers other) => Particles == other.Particles && Trail == other.Trail;

        public override bool Equals(object? obj) => obj is ImpactFreezeLayers other && Equals(other);

        public override int GetHashCode() => (Particles ? 1 : 0) | (Trail ? 2 : 0);
    }

    /// <summary>反馈包变体里的闪白声明。</summary>
    public sealed class ImpactFlashSpec
    {
        public Id ProfileId { get; }

        /// <summary>闪白对象：<see cref="FeedbackAttachTarget.Target"/> 或 <see cref="FeedbackAttachTarget.Source"/>。</summary>
        public FeedbackAttachTarget Target { get; }

        public ImpactFlashSpec(Id profileId, FeedbackAttachTarget target)
        {
            if (target == FeedbackAttachTarget.World)
            {
                throw new ArgumentException("闪白对象只能是 target 或 source", nameof(target));
            }
            ProfileId = profileId;
            Target = target;
        }
    }

    /// <summary>反馈包变体里的特效声明。</summary>
    public sealed class ImpactVfxSpec
    {
        public Id VfxId { get; }

        public ImpactVfxAttach Attach { get; }

        public ImpactVfxOrient Orient { get; }

        /// <summary>按 <c>amountRatio</c> 缩放特效大小的曲线（x = amountRatio，y = 倍率）；null 表示不按比例缩放。</summary>
        public PiecewiseCurve? ScaleByRatio { get; }

        public ImpactVfxSpec(Id vfxId, ImpactVfxAttach attach, ImpactVfxOrient orient, PiecewiseCurve? scaleByRatio)
        {
            VfxId = vfxId;
            Attach = attach;
            Orient = orient;
            ScaleByRatio = scaleByRatio;
        }
    }

    /// <summary>反馈包变体里的一个音效层引用：层 + 可选强度档（缺省取攻击方手感表对应的 <c>sfx_*_tier</c>）。</summary>
    public readonly struct ImpactSfxSpec
    {
        public SfxFeelLayer Layer { get; }

        /// <summary>变体显式给出的强度档（覆盖手感表档位）；null 表示取手感表。</summary>
        public int? Tier { get; }

        public ImpactSfxSpec(SfxFeelLayer layer, int? tier)
        {
            Layer = layer;
            Tier = tier;
        }
    }

    /// <summary>反馈包变体里的镜头声明。</summary>
    public sealed class ImpactCameraSpec
    {
        /// <summary>缺省衰减时长（毫秒）。</summary>
        public const double DefaultDecayMs = 120.0;

        /// <summary>
        /// 镜头冲击增益<b>乘数</b>（无量纲，缺省 1）：最终幅度 = 手感表 <c>camera_impulse_gain</c>（画面高度比例）× 本乘数 × 强度缩放。
        /// 判断记录：07 第 2 节把手感字段与反馈包字段都标"画面高度比例"并要求相乘，两个比例相乘没有物理意义，
        /// 故变体侧取无量纲乘数，量纲只由手感字段一处承载。
        /// </summary>
        public double ImpulseGain { get; }

        /// <summary>可选震屏档（当前相机档 <c>shake_presets</c> 的 id）；经既有 <c>ShakeCamera</c> 通道播放。</summary>
        public Id? ShakeProfile { get; }

        public double DecayMs { get; }

        public ImpactCameraSpec(double impulseGain, Id? shakeProfile, double decayMs)
        {
            if (!(impulseGain >= 0) || double.IsInfinity(impulseGain))
            {
                throw new ArgumentOutOfRangeException(nameof(impulseGain), "冲击增益乘数必须是非负有限数");
            }
            if (!(decayMs > 0) || double.IsInfinity(decayMs))
            {
                throw new ArgumentOutOfRangeException(nameof(decayMs), "衰减时长必须是正有限数");
            }
            ImpulseGain = impulseGain;
            ShakeProfile = shakeProfile;
            DecayMs = decayMs;
        }
    }

    /// <summary>反馈包变体里的拖尾声明（仅承载，框架缺省 sink 不渲染拖尾，见 feedback_binder/README.md）。</summary>
    public sealed class ImpactTrailSpec
    {
        /// <summary><c>active_start</c> 或 <c>hit</c>。</summary>
        public string Start { get; }

        public string End { get; }

        public ImpactTrailSpec(string start, string end)
        {
            Start = start ?? throw new ArgumentNullException(nameof(start));
            End = end ?? throw new ArgumentNullException(nameof(end));
        }
    }

    /// <summary>幅度类字段的缩放规则：<c>ratio_curve(amountRatio) × (暴击 ? crit_multiplier : 1) × (击杀 ? kill_multiplier : 1)</c>。</summary>
    public sealed class ImpactIntensity
    {
        public static ImpactIntensity Neutral { get; } = new ImpactIntensity(null, 1.0, 1.0);

        public PiecewiseCurve? RatioCurve { get; }

        public double CritMultiplier { get; }

        public double KillMultiplier { get; }

        public ImpactIntensity(PiecewiseCurve? ratioCurve, double critMultiplier, double killMultiplier)
        {
            if (!(critMultiplier >= 0) || double.IsInfinity(critMultiplier)) throw new ArgumentOutOfRangeException(nameof(critMultiplier));
            if (!(killMultiplier >= 0) || double.IsInfinity(killMultiplier)) throw new ArgumentOutOfRangeException(nameof(killMultiplier));
            RatioCurve = ratioCurve;
            CritMultiplier = critMultiplier;
            KillMultiplier = killMultiplier;
        }

        /// <summary>按 <c>amountRatio</c> 的曲线因子（无曲线为 1）。</summary>
        public double RatioFactor(double amountRatio) => RatioCurve == null || RatioCurve.Count == 0 ? 1.0 : RatioCurve.Evaluate(amountRatio);

        /// <summary>暴击/击杀倍率因子。</summary>
        public double OutcomeFactor(bool isCrit, bool isKill) =>
            (isCrit ? CritMultiplier : 1.0) * (isKill ? KillMultiplier : 1.0);
    }

    /// <summary>一个 <c>(impactClass, outcome)</c> 变体（手感设计/07 第 1 节 <c>ImpactVariant</c>）。</summary>
    public sealed class ImpactVariant
    {
        public string ImpactClass { get; }

        public ImpactOutcome Outcome { get; }

        public ImpactFlashSpec? Flash { get; }

        public ImpactVfxSpec? Vfx { get; }

        public IReadOnlyList<ImpactSfxSpec> Sfx { get; }

        public ImpactCameraSpec? Camera { get; }

        public Id? FloatingTextStyle { get; }

        public ImpactTrailSpec? Trail { get; }

        public ImpactFreezeLayers FreezeLayers { get; }

        public ImpactIntensity Intensity { get; }

        public ImpactVariant(
            string impactClass, ImpactOutcome outcome, ImpactFlashSpec? flash, ImpactVfxSpec? vfx,
            IReadOnlyList<ImpactSfxSpec>? sfx, ImpactCameraSpec? camera, Id? floatingTextStyle,
            ImpactTrailSpec? trail, ImpactFreezeLayers freezeLayers, ImpactIntensity? intensity)
        {
            ImpactClass = impactClass ?? throw new ArgumentNullException(nameof(impactClass));
            Outcome = outcome;
            Flash = flash;
            Vfx = vfx;
            Sfx = sfx ?? Array.Empty<ImpactSfxSpec>();
            Camera = camera;
            FloatingTextStyle = floatingTextStyle;
            Trail = trail;
            FreezeLayers = freezeLayers;
            Intensity = intensity ?? ImpactIntensity.Neutral;
        }
    }

    /// <summary>
    /// 打击反馈包（<c>feedback.impact_profile</c>，手感设计/07 第 1 节）：一条记录把一次命中的全部表现打包。
    /// <para>
    /// 变体选择（<see cref="Select"/>）：先 <c>(impactClass, outcome)</c> 精确；缺项按 outcome 回落 <see cref="ImpactOutcome.Hit"/>
    /// （<see cref="ImpactOutcome.Avoided"/> 与 <see cref="ImpactOutcome.Whiff"/> 不回落到 Hit——回避/挥空不得播成功命中的完整反馈）；
    /// 仍缺项再按 impactClass 回落 <c>medium</c>，在 medium 上重复同样的 outcome 回落；全缺返回 null（本次不播反馈包）。
    /// </para>
    /// </summary>
    public sealed class ImpactProfile
    {
        public const string FallbackClass = "medium";

        private readonly Dictionary<(string, ImpactOutcome), ImpactVariant> _variants =
            new Dictionary<(string, ImpactOutcome), ImpactVariant>();

        public Id Id { get; }

        public IReadOnlyList<ImpactVariant> Variants { get; }

        public ImpactProfile(Id id, IReadOnlyList<ImpactVariant> variants)
        {
            Id = id;
            Variants = variants ?? throw new ArgumentNullException(nameof(variants));
            foreach (var v in variants)
            {
                var key = (v.ImpactClass, v.Outcome);
                if (_variants.ContainsKey(key))
                {
                    throw new ArgumentException($"反馈包 \"{id}\" 的变体 ({v.ImpactClass}, {v.Outcome}) 重复登记", nameof(variants));
                }
                _variants[key] = v;
            }
        }

        public ImpactVariant? Select(string impactClass, ImpactOutcome outcome)
        {
            var variant = SelectInClass(impactClass, outcome);
            if (variant != null || string.Equals(impactClass, FallbackClass, StringComparison.Ordinal))
            {
                return variant;
            }
            return SelectInClass(FallbackClass, outcome);
        }

        private ImpactVariant? SelectInClass(string impactClass, ImpactOutcome outcome)
        {
            if (_variants.TryGetValue((impactClass, outcome), out var exact))
            {
                return exact;
            }
            if (outcome == ImpactOutcome.Crit || outcome == ImpactOutcome.Kill)
            {
                if (_variants.TryGetValue((impactClass, ImpactOutcome.Hit), out var hit))
                {
                    return hit;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        // 数据解析
        // ------------------------------------------------------------------

        public static ImpactProfile FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var array = record.GetArray("variants");
            var variants = new List<ImpactVariant>(array.Count);
            for (var i = 0; i < array.Count; i++)
            {
                if (!(array[i] is JsonObject obj))
                {
                    throw Bad(record, "variants", $"第 {i} 个元素不是对象");
                }
                variants.Add(ParseVariant(record, i, obj));
            }

            try
            {
                return new ImpactProfile(id, variants);
            }
            catch (ArgumentException ex)
            {
                throw Bad(record, "variants", ex.Message);
            }
        }

        private static DataFieldException Bad(DataRecord record, string field, string message) =>
            new DataFieldException(record.Table.Name, record.Key, field, message);

        private static ImpactVariant ParseVariant(DataRecord record, int index, JsonObject obj)
        {
            var where = $"variants[{index}]";
            var impactClass = ReqString(record, where, obj, "class");
            var outcome = ParseOutcome(record, where, ReqString(record, where, obj, "outcome"));

            ImpactFlashSpec? flash = null;
            if (TryObject(obj, "flash", out var flashObj))
            {
                var target = ReqString(record, where + ".flash", flashObj, "target");
                var attach = target == "target" ? FeedbackAttachTarget.Target
                    : target == "source" ? FeedbackAttachTarget.Source
                    : throw Bad(record, where + ".flash.target", $"取值非法：\"{target}\"（只能是 target|source）");
                flash = new ImpactFlashSpec(ReqId(record, where + ".flash", flashObj, "profile_id"), attach);
            }

            ImpactVfxSpec? vfx = null;
            if (TryObject(obj, "vfx", out var vfxObj))
            {
                var attachText = ReqString(record, where + ".vfx", vfxObj, "attach");
                var attach = attachText == "contact" ? ImpactVfxAttach.Contact
                    : attachText == "target" ? ImpactVfxAttach.Target
                    : attachText == "source" ? ImpactVfxAttach.Source
                    : throw Bad(record, where + ".vfx.attach", $"取值非法：\"{attachText}\"（contact|target|source）");
                var orientText = OptString(vfxObj, "orient") ?? "none";
                var orient = orientText == "none" ? ImpactVfxOrient.None
                    : orientText == "contact_normal" ? ImpactVfxOrient.ContactNormal
                    : orientText == "world_direction" ? ImpactVfxOrient.WorldDirection
                    : throw Bad(record, where + ".vfx.orient", $"取值非法：\"{orientText}\"（none|contact_normal|world_direction）");
                vfx = new ImpactVfxSpec(
                    ReqId(record, where + ".vfx", vfxObj, "vfx_id"), attach, orient,
                    ParseCurve(record, where + ".vfx.scale_by_ratio", vfxObj, "scale_by_ratio"));
            }

            var sfx = new List<ImpactSfxSpec>();
            if (obj.TryGetValue("sfx", out var sfxVal) && sfxVal is JsonArray sfxArr)
            {
                for (var i = 0; i < sfxArr.Count; i++)
                {
                    if (!(sfxArr[i] is JsonObject sfxObj))
                    {
                        throw Bad(record, $"{where}.sfx[{i}]", "不是对象");
                    }
                    var layerName = ReqString(record, $"{where}.sfx[{i}]", sfxObj, "layer");
                    if (!SfxFeelLayers.TryParse(layerName, out var layer))
                    {
                        throw Bad(record, $"{where}.sfx[{i}].layer", $"未知的手感音效层：\"{layerName}\"");
                    }
                    int? tier = null;
                    if (sfxObj.TryGetValue("tier", out var tierVal) && tierVal is JsonNumber tierNum)
                    {
                        tier = (int)tierNum.Value;
                    }
                    sfx.Add(new ImpactSfxSpec(layer, tier));
                }
            }

            ImpactCameraSpec? camera = null;
            if (TryObject(obj, "camera", out var camObj))
            {
                Id? shake = null;
                if (camObj.TryGetValue("shake_profile", out var shakeVal) && shakeVal is JsonString shakeStr)
                {
                    if (!Id.TryParse(shakeStr.Value, out var shakeId))
                    {
                        throw Bad(record, where + ".camera.shake_profile", $"不是合法 Id：\"{shakeStr.Value}\"");
                    }
                    shake = shakeId;
                }
                try
                {
                    camera = new ImpactCameraSpec(
                        OptNumber(camObj, "impulse_gain") ?? 1.0, shake, OptNumber(camObj, "decay_ms") ?? ImpactCameraSpec.DefaultDecayMs);
                }
                catch (ArgumentException ex)
                {
                    throw Bad(record, where + ".camera", ex.Message);
                }
            }

            Id? textStyle = null;
            if (TryObject(obj, "floating_text", out var textObj))
            {
                textStyle = ReqId(record, where + ".floating_text", textObj, "style_id");
            }

            ImpactTrailSpec? trail = null;
            if (TryObject(obj, "trail", out var trailObj))
            {
                var start = ReqString(record, where + ".trail", trailObj, "start");
                var end = ReqString(record, where + ".trail", trailObj, "end");
                if (start != "active_start" && start != "hit")
                {
                    throw Bad(record, where + ".trail.start", $"取值非法：\"{start}\"（active_start|hit）");
                }
                if (end != "active_end")
                {
                    throw Bad(record, where + ".trail.end", $"取值非法：\"{end}\"（active_end）");
                }
                trail = new ImpactTrailSpec(start, end);
            }

            var freeze = ImpactFreezeLayers.Default;
            if (TryObject(obj, "freeze_layers", out var freezeObj))
            {
                freeze = new ImpactFreezeLayers(OptBool(freezeObj, "particles") ?? false, OptBool(freezeObj, "trail") ?? false);
            }

            ImpactIntensity? intensity = null;
            if (TryObject(obj, "intensity", out var intensityObj))
            {
                try
                {
                    intensity = new ImpactIntensity(
                        ParseCurve(record, where + ".intensity.ratio_curve", intensityObj, "ratio_curve"),
                        OptNumber(intensityObj, "crit_multiplier") ?? 1.0,
                        OptNumber(intensityObj, "kill_multiplier") ?? 1.0);
                }
                catch (ArgumentException ex)
                {
                    throw Bad(record, where + ".intensity", ex.Message);
                }
            }

            return new ImpactVariant(impactClass, outcome, flash, vfx, sfx, camera, textStyle, trail, freeze, intensity);
        }

        private static ImpactOutcome ParseOutcome(DataRecord record, string where, string text)
        {
            switch (text)
            {
                case "hit": return ImpactOutcome.Hit;
                case "crit": return ImpactOutcome.Crit;
                case "kill": return ImpactOutcome.Kill;
                case "avoided": return ImpactOutcome.Avoided;
                case "whiff": return ImpactOutcome.Whiff;
                default: throw Bad(record, where + ".outcome", $"取值非法：\"{text}\"（hit|crit|kill|avoided|whiff）");
            }
        }

        private static PiecewiseCurve? ParseCurve(DataRecord record, string where, JsonObject obj, string field)
        {
            if (!obj.TryGetValue(field, out var value) || value.Kind == JsonKind.Null)
            {
                return null;
            }
            if (!(value is JsonArray arr))
            {
                throw Bad(record, where, "曲线必须是 [{x,y}] 数组");
            }
            var points = new List<CurvePoint>(arr.Count);
            for (var i = 0; i < arr.Count; i++)
            {
                if (!(arr[i] is JsonObject p) || !(p.TryGetValue("x", out var x) && x is JsonNumber xn)
                    || !(p.TryGetValue("y", out var y) && y is JsonNumber yn))
                {
                    throw Bad(record, $"{where}[{i}]", "断点必须是 {x:数值, y:数值}");
                }
                points.Add(new CurvePoint(xn.Value, yn.Value));
            }
            return new PiecewiseCurve(points);
        }

        private static bool TryObject(JsonObject obj, string field, out JsonObject value)
        {
            if (obj.TryGetValue(field, out var v) && v is JsonObject o)
            {
                value = o;
                return true;
            }
            value = null!;
            return false;
        }

        private static string ReqString(DataRecord record, string where, JsonObject obj, string field)
        {
            if (obj.TryGetValue(field, out var v) && v is JsonString s)
            {
                return s.Value;
            }
            throw Bad(record, where + "." + field, "缺少必填字符串字段");
        }

        private static Id ReqId(DataRecord record, string where, JsonObject obj, string field)
        {
            if (obj.TryGetValue(field, out var v) && v is JsonString s && Id.TryParse(s.Value, out var id))
            {
                return id;
            }
            throw Bad(record, where + "." + field, "缺少必填 Id 字段");
        }

        private static string? OptString(JsonObject obj, string field) =>
            obj.TryGetValue(field, out var v) && v is JsonString s ? s.Value : null;

        private static double? OptNumber(JsonObject obj, string field) =>
            obj.TryGetValue(field, out var v) && v is JsonNumber n ? (double?)n.Value : null;

        private static bool? OptBool(JsonObject obj, string field) =>
            obj.TryGetValue(field, out var v) && v is JsonBool b ? (bool?)b.Value : null;
    }
}
