using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.InputMap;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>时间线块的作者态权威来源（手感设计/01 第 3.2 节）；运行期永远只读本块，本字段只影响校验器的比对口径。</summary>
    public enum TimelineSource
    {
        /// <summary>本块的数值是权威，剪辑里的对应标记由校验器比对，偏差超过容差给警告。</summary>
        Data,

        /// <summary>剪辑的标记是权威，内容导入工具把标记时间抄写进本块，校验器要求两者一致，不一致为错误。</summary>
        Clip,
    }

    /// <summary>资源扣除时刻（手感设计/01 第 3.1 节 <c>cost_at</c>）。</summary>
    public enum TimelineCostAt
    {
        /// <summary>动作开始（缺省）。</summary>
        Commit,

        /// <summary>进入判定相的那一 tick（本实现新增的取值，见 skill 模块 README 判断记录）。</summary>
        Active,

        /// <summary>首个 <c>hit</c> 标记到达的那一 tick（派生 <c>cost</c> 标记）。</summary>
        FirstHit,
    }

    /// <summary>冷却起算时刻（手感设计/01 第 3.1 节 <c>cooldown_at</c>）。</summary>
    public enum TimelineCooldownAt
    {
        /// <summary>动作开始（缺省）。</summary>
        Commit,

        /// <summary>进入判定相的那一 tick（本实现新增的取值）。</summary>
        Active,

        /// <summary>动作结束（自然结束或被取消/打断的那一刻）。</summary>
        Finish,
    }

    /// <summary>命中解析方式（手感设计/01 第 3.1 节、03 第 2.2 节）；本切片只登记字段与解析，空间命中由时间线命中切片消费。</summary>
    public enum TimelineHitPolicy
    {
        /// <summary>每个 <c>hit</c> 标记到达时解析一次目标并结算。</summary>
        Marker,

        /// <summary>
        /// 判定相内逐 tick 解析（时间线空间命中切片实现，手感设计/03 第 2.2 节）：每 tick 用同一条目标选择链以攻击方当前位姿解析一次，
        /// 两个 tick 位姿之间按 <c>sample_step_ms</c>（缺省一个 tick 的 1/4）线性插值多次采样、并按形状尺寸加密，高速形状不漏目标；
        /// 同一攻击实例对同一目标只命中一次（<c>rehit_interval_ms</c> 声明后按间隔分段，可再次命中）。<c>hit</c> 标记不再触发结算。
        /// </summary>
        Continuous,
    }

    /// <summary>命中解析走哪条路径（<c>skill.def.timeline.hit_mode</c>，时间线空间命中切片新增）。</summary>
    public enum TimelineHitMode
    {
        /// <summary>缺省：目标选择链声明了 <c>shape</c> 则空间命中，没有则保持 instant 结算（S3a 行为）。</summary>
        Auto,

        /// <summary>强制空间命中（链没有 <c>shape</c> 时校验器报错）。</summary>
        Spatial,

        /// <summary>强制 instant 结算（即便链有 <c>shape</c>）：hit 标记处按目标选择链解析并结算，不做去重与无敌前置检查。</summary>
        Instant,
    }

    /// <summary>
    /// 地面落点技能的命中锚点（<c>skill.def.timeline.hit_anchor</c>，手感落地 M5-S2a，手感设计/03 第 2.2 节）：只对声明了 <c>ground_target</c> 的技能有意义，
    /// 经 <c>CastSkillAtGround</c> 施放时让技能的 <c>timeline</c> 生效。缺省 <see cref="None"/> 保持既有行为（时间线被忽略、校验给警告）。
    /// </summary>
    public enum TimelineHitAnchor
    {
        /// <summary>缺省：不声明，地面落点技能忽略 <c>timeline</c>（既有行为）。</summary>
        None,

        /// <summary>命中形状以施法者的位姿为锚点（同单位目标的时间线动作），落点只随效果上下文带给效果原语（召唤、传送、投射物落点等）。</summary>
        Caster,

        /// <summary>命中形状以落点为锚点，朝向取施法时刻施法者指向落点的方向（落点与施法者重合时取施法者朝向）；落点同样随效果上下文带给效果原语。</summary>
        GroundPoint,
    }

    /// <summary>目标辅助的模式（手感设计/02 第 5 节 <c>target_assist.mode</c>）。</summary>
    public enum TimelineAssistMode
    {
        /// <summary>只把朝向对齐到辅助目标（转角不超过档案 <c>turn_assist_deg</c>）。</summary>
        FaceOnly,

        /// <summary>还把本次位移距离缩放到"判定相形状恰好覆盖目标"所需的值（不超过声明距离）。</summary>
        CloseDistance,
    }

    /// <summary>目标辅助声明（<c>skill.def.timeline.target_assist</c>，手感设计/02 第 5 节）。缺省不声明即没有目标辅助。</summary>
    public sealed class TimelineTargetAssist
    {
        /// <summary>候选解析用的目标选择链（<c>target.chain_def</c> id）。</summary>
        public Id ChainRef { get; }

        /// <summary>候选允许的最大距离（身高倍数，经标定换算为世界单位，同 <c>motion.distance</c>）。</summary>
        public double MaxDistance { get; }

        /// <summary>候选相对朝向允许的最大角度（度，单侧）。</summary>
        public double MaxAngleDeg { get; }

        public TimelineAssistMode Mode { get; }

        public TimelineTargetAssist(Id chainRef, double maxDistance, double maxAngleDeg, TimelineAssistMode mode)
        {
            ChainRef = chainRef;
            MaxDistance = maxDistance;
            MaxAngleDeg = maxAngleDeg;
            Mode = mode;
        }
    }

    /// <summary>一个字段的蓄力倍率区间：蓄力比例为 0 / 1 时的倍率，之间线性插值。</summary>
    public readonly struct ChargeScaleRange
    {
        public double Min { get; }

        public double Max { get; }

        public ChargeScaleRange(double min, double max)
        {
            Min = min;
            Max = max;
        }

        public static ChargeScaleRange Identity => new ChargeScaleRange(1.0, 1.0);

        public double At(double ratio) => Min + (Max - Min) * ratio;
    }

    /// <summary>
    /// 蓄力对手感的缩放（<c>timeline.charge.feel_scale</c>，手感落地 M5-S2a，手感设计/03 第 2.4 节）：按蓄力比例线性缩放命中的顿帧与击退/击飞，
    /// 只列四个判定型字段（<c>attacker_hitstop_ms</c>/<c>target_hitstop_ms</c>/<c>knockback_distance</c>/<c>launch_height</c>），没有声明的字段倍率为 1。
    /// 缩放发生在档案取值之后、限幅之前（顿帧仍受 <c>*_hitstop_cap_ms</c> 限幅），不改解析后的手感表本身。
    /// </summary>
    public sealed class TimelineChargeFeelScale
    {
        public ChargeScaleRange AttackerHitstop { get; }

        public ChargeScaleRange TargetHitstop { get; }

        public ChargeScaleRange KnockbackDistance { get; }

        public ChargeScaleRange LaunchHeight { get; }

        public TimelineChargeFeelScale(
            ChargeScaleRange attackerHitstop, ChargeScaleRange targetHitstop, ChargeScaleRange knockbackDistance, ChargeScaleRange launchHeight)
        {
            AttackerHitstop = attackerHitstop;
            TargetHitstop = targetHitstop;
            KnockbackDistance = knockbackDistance;
            LaunchHeight = launchHeight;
        }

        /// <summary>给定蓄力比例（0～1）的四个倍率。</summary>
        public HitFeelScale At(double ratio) =>
            new HitFeelScale(AttackerHitstop.At(ratio), TargetHitstop.At(ratio), KnockbackDistance.At(ratio), LaunchHeight.At(ratio));
    }

    /// <summary>蓄力相声明（手感设计/01 第 3.1 节 <c>charge</c>）。</summary>
    public sealed class TimelineCharge
    {
        public double MinMs { get; }

        public double MaxMs { get; }

        /// <summary>
        /// 蓄力比例为 0 / 1 时的效果值倍率（手感设计/01 第 3.3 节"效果值可按蓄力比例缩放"；M4 清扫）。
        /// 缺省两端都是 1（不缩放，与未声明等价）。倍率随蓄力比例线性插值：<c>min + (max − min) × 比例</c>。
        /// </summary>
        public double ValueScaleMin { get; }

        public double ValueScaleMax { get; }

        /// <summary>蓄力对手感（顿帧、击退、击飞）的缩放（<c>feel_scale</c>）；null = 未声明，不缩放。</summary>
        public TimelineChargeFeelScale? FeelScale { get; }

        public TimelineCharge(double minMs, double maxMs)
            : this(minMs, maxMs, 1.0, 1.0)
        {
        }

        public TimelineCharge(double minMs, double maxMs, double valueScaleMin, double valueScaleMax)
        {
            MinMs = minMs;
            MaxMs = maxMs;
            ValueScaleMin = valueScaleMin;
            ValueScaleMax = valueScaleMax;
        }

        /// <summary>携带 <see cref="FeelScale"/> 的重载（新增重载，不改既有物理签名）。</summary>
        public TimelineCharge(double minMs, double maxMs, double valueScaleMin, double valueScaleMax, TimelineChargeFeelScale? feelScale)
            : this(minMs, maxMs, valueScaleMin, valueScaleMax)
        {
            FeelScale = feelScale;
        }

        /// <summary>给定蓄力比例（0～1）的效果值倍率。</summary>
        public double ValueScaleAt(double ratio) => ValueScaleMin + (ValueScaleMax - ValueScaleMin) * ratio;
    }

    /// <summary>一个时间标记（手感设计/01 第 3.3 节）：<c>hit:&lt;段&gt;</c> 写法在解析时归一为名 <c>hit</c> + 参数 <c>segment</c>。</summary>
    public sealed class TimelineMarker
    {
        public string Name { get; }

        /// <summary>相对动作开始（不含蓄力）的毫秒数，未经速率重映射。</summary>
        public double AtMs { get; }

        public IReadOnlyDictionary<string, string> Args { get; }

        public TimelineMarker(string name, double atMs, IReadOnlyDictionary<string, string> args)
        {
            Name = name;
            AtMs = atMs;
            Args = args;
        }
    }

    /// <summary>一个取消窗口（手感设计/01 第 3.4 节）。</summary>
    public sealed class TimelineCancelWindow
    {
        public ActionClass Class { get; }

        public double OpenMs { get; }

        /// <summary>关闭时刻；null 表示到动作结束。</summary>
        public double? CloseMs { get; }

        public TimelineCancelWindow(ActionClass actionClass, double openMs, double? closeMs)
        {
            Class = actionClass;
            OpenMs = openMs;
            CloseMs = closeMs;
        }
    }

    /// <summary>连招接续块（手感设计/01 第 3.6 节）。</summary>
    public sealed class TimelineCombo
    {
        public Id Next { get; }

        public double OpenMs { get; }

        public double CloseMs { get; }

        public TimelineCombo(Id next, double openMs, double closeMs)
        {
            Next = next;
            OpenMs = openMs;
            CloseMs = closeMs;
        }
    }

    /// <summary>
    /// <c>skill.def.timeline</c> 块的解析结果（手感设计/01 第 3.1 节 <c>ActionTimeline</c>）。运行期只读本类型，不读剪辑
    /// （规则层不读表现域，手感设计/01 第 3.2 节）。未声明 <c>timeline</c> 的技能 <see cref="SkillDef.Timeline"/> 为 null，
    /// 行为与引入本块之前逐位一致。
    /// </summary>
    public sealed class TimelineDef
    {
        public TimelineSource Source { get; }

        public TimelineCharge? Charge { get; }

        public double StartupMs { get; }

        public double ActiveMs { get; }

        public double RecoveryMs { get; }

        public IReadOnlyList<TimelineMarker> Markers { get; }

        public IReadOnlyList<TimelineCancelWindow> CancelWindows { get; }

        public TimelineCombo? Combo { get; }

        public TimelineHitPolicy HitPolicy { get; }

        public TimelineCostAt CostAt { get; }

        public TimelineCooldownAt CooldownAt { get; }

        public ActionMotion? Motion { get; }

        /// <summary>当前动作层的手感覆盖行 id（<c>feel.action</c>），可空。</summary>
        public string? FeelRef { get; }

        /// <summary>命中解析路径（缺省 <see cref="TimelineHitMode.Auto"/>）。</summary>
        public TimelineHitMode HitMode { get; }

        /// <summary>
        /// <c>continuous</c> 命中两 tick 位姿之间的采样步长（毫秒）；0 即缺省"一个 tick 的 1/4"（手感设计/03 第 2.2 节
        /// <c>sample_step</c>，本实现按毫秒声明）。运行期还会按形状尺寸加密，保证位移不超过形状特征尺寸的一半。
        /// </summary>
        public double SampleStepMs { get; }

        /// <summary>
        /// 多段命中的最小再命中间隔（毫秒，0 = 未声明）。<c>marker</c> 策略：同一目标在不同段之间、距上次命中不足该间隔时不再命中；
        /// <c>continuous</c> 策略：判定相按该间隔切成段，每段是一份新的命中集合（同一目标每个间隔可命中一次）。
        /// </summary>
        public double RehitIntervalMs { get; }

        /// <summary>目标辅助声明；null 即没有目标辅助。</summary>
        public TimelineTargetAssist? TargetAssist { get; }

        /// <summary>
        /// 显式的"这个动作带攻击"声明（<c>is_attack</c>，M4 清扫）：null 取按技能内容的推断（含伤害类/投射物效果，或有 hit/release 标记），
        /// 真/假直接覆盖推断——例如发射治疗投射物的动作写 false，使反馈侧不为它开挥空窗口。
        /// </summary>
        public bool? IsAttack { get; }

        /// <summary>地面落点技能的命中锚点（缺省 <see cref="TimelineHitAnchor.None"/>：不声明，地面落点施放时时间线被忽略）。</summary>
        public TimelineHitAnchor HitAnchor { get; }

        private IReadOnlyDictionary<int, string>? _segmentFeelRefs;

        /// <summary>
        /// 分段手感覆盖（手感落地 M5-S2a）：键为段序号，值为该段 <c>hit</c>/<c>release</c> 标记 <c>args.feel_ref</c> 声明的 <c>feel.action</c> 行 id。
        /// 同一段多个标记声明时取时间线上最早的一个；<c>release</c> 标记没有写 <c>segment</c> 时按第 0 段。没有任何分段覆盖时为空字典。
        /// </summary>
        public IReadOnlyDictionary<int, string> SegmentFeelRefs => _segmentFeelRefs ??= BuildSegmentFeelRefs(Markers);

        private static IReadOnlyDictionary<int, string> BuildSegmentFeelRefs(IReadOnlyList<TimelineMarker> markers)
        {
            var map = new Dictionary<int, string>();
            for (var i = 0; i < markers.Count; i++)
            {
                var marker = markers[i];
                if ((marker.Name != HitMarker && marker.Name != "release") || !marker.Args.TryGetValue("feel_ref", out var feelRef) || string.IsNullOrEmpty(feelRef))
                {
                    continue;
                }

                var segment = 0;
                if (marker.Args.TryGetValue("segment", out var segmentText))
                {
                    int.TryParse(segmentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out segment);
                }

                if (!map.ContainsKey(segment))
                {
                    map[segment] = feelRef;
                }
            }

            return map;
        }

        /// <summary>三相之和（毫秒，不含蓄力）。</summary>
        public double TotalMs => StartupMs + ActiveMs + RecoveryMs;

        public TimelineDef(
            TimelineSource source,
            TimelineCharge? charge,
            double startupMs,
            double activeMs,
            double recoveryMs,
            IReadOnlyList<TimelineMarker> markers,
            IReadOnlyList<TimelineCancelWindow> cancelWindows,
            TimelineCombo? combo,
            TimelineHitPolicy hitPolicy,
            TimelineCostAt costAt,
            TimelineCooldownAt cooldownAt,
            ActionMotion? motion,
            string? feelRef)
            : this(
                source, charge, startupMs, activeMs, recoveryMs, markers, cancelWindows, combo, hitPolicy, costAt, cooldownAt,
                motion, feelRef, TimelineHitMode.Auto, 0.0, 0.0, null)
        {
        }

        public TimelineDef(
            TimelineSource source,
            TimelineCharge? charge,
            double startupMs,
            double activeMs,
            double recoveryMs,
            IReadOnlyList<TimelineMarker> markers,
            IReadOnlyList<TimelineCancelWindow> cancelWindows,
            TimelineCombo? combo,
            TimelineHitPolicy hitPolicy,
            TimelineCostAt costAt,
            TimelineCooldownAt cooldownAt,
            ActionMotion? motion,
            string? feelRef,
            TimelineHitMode hitMode,
            double sampleStepMs,
            double rehitIntervalMs,
            TimelineTargetAssist? targetAssist)
            : this(
                source, charge, startupMs, activeMs, recoveryMs, markers, cancelWindows, combo, hitPolicy, costAt, cooldownAt,
                motion, feelRef, hitMode, sampleStepMs, rehitIntervalMs, targetAssist, null)
        {
        }

        public TimelineDef(
            TimelineSource source,
            TimelineCharge? charge,
            double startupMs,
            double activeMs,
            double recoveryMs,
            IReadOnlyList<TimelineMarker> markers,
            IReadOnlyList<TimelineCancelWindow> cancelWindows,
            TimelineCombo? combo,
            TimelineHitPolicy hitPolicy,
            TimelineCostAt costAt,
            TimelineCooldownAt cooldownAt,
            ActionMotion? motion,
            string? feelRef,
            TimelineHitMode hitMode,
            double sampleStepMs,
            double rehitIntervalMs,
            TimelineTargetAssist? targetAssist,
            bool? isAttack)
            : this(
                source, charge, startupMs, activeMs, recoveryMs, markers, cancelWindows, combo, hitPolicy, costAt, cooldownAt,
                motion, feelRef, hitMode, sampleStepMs, rehitIntervalMs, targetAssist, isAttack, TimelineHitAnchor.None)
        {
        }

        /// <summary>手感落地 M5-S2a 新增重载：携带 <see cref="HitAnchor"/>（新增重载，不改既有物理签名）。</summary>
        public TimelineDef(
            TimelineSource source,
            TimelineCharge? charge,
            double startupMs,
            double activeMs,
            double recoveryMs,
            IReadOnlyList<TimelineMarker> markers,
            IReadOnlyList<TimelineCancelWindow> cancelWindows,
            TimelineCombo? combo,
            TimelineHitPolicy hitPolicy,
            TimelineCostAt costAt,
            TimelineCooldownAt cooldownAt,
            ActionMotion? motion,
            string? feelRef,
            TimelineHitMode hitMode,
            double sampleStepMs,
            double rehitIntervalMs,
            TimelineTargetAssist? targetAssist,
            bool? isAttack,
            TimelineHitAnchor hitAnchor)
        {
            HitAnchor = hitAnchor;
            IsAttack = isAttack;
            HitMode = hitMode;
            SampleStepMs = sampleStepMs;
            RehitIntervalMs = rehitIntervalMs;
            TargetAssist = targetAssist;
            Source = source;
            Charge = charge;
            StartupMs = startupMs;
            ActiveMs = activeMs;
            RecoveryMs = recoveryMs;
            Markers = markers ?? Array.Empty<TimelineMarker>();
            CancelWindows = cancelWindows ?? Array.Empty<TimelineCancelWindow>();
            Combo = combo;
            HitPolicy = hitPolicy;
            CostAt = costAt;
            CooldownAt = cooldownAt;
            Motion = motion;
            FeelRef = feelRef;
        }

        /// <summary><c>hit</c> 标记名。</summary>
        public const string HitMarker = "hit";

        /// <summary>解析 <c>skill.def.timeline</c> 的 JSON 对象（字段已经 schema 校验，这里只做结构化）。</summary>
        public static TimelineDef Parse(JsonObject obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            var source = Str(obj, "source") == "clip" ? TimelineSource.Clip : TimelineSource.Data;

            TimelineCharge? charge = null;
            if (obj.TryGetValue("charge", out var chargeVal) && chargeVal is JsonObject chargeObj)
            {
                var scaleMin = 1.0;
                var scaleMax = 1.0;
                if (chargeObj.TryGetValue("value_scale", out var scaleVal) && scaleVal is JsonObject scaleObj)
                {
                    scaleMin = Num(scaleObj, "min");
                    scaleMax = Num(scaleObj, "max");
                }

                TimelineChargeFeelScale? feelScale = null;
                if (chargeObj.TryGetValue("feel_scale", out var feelScaleVal) && feelScaleVal is JsonObject feelScaleObj)
                {
                    feelScale = new TimelineChargeFeelScale(
                        ScaleRange(feelScaleObj, "attacker_hitstop_ms"), ScaleRange(feelScaleObj, "target_hitstop_ms"),
                        ScaleRange(feelScaleObj, "knockback_distance"), ScaleRange(feelScaleObj, "launch_height"));
                }

                charge = new TimelineCharge(Num(chargeObj, "min_ms"), Num(chargeObj, "max_ms"), scaleMin, scaleMax, feelScale);
            }

            var markers = new List<TimelineMarker>();
            if (obj.TryGetValue("markers", out var markersVal) && markersVal is JsonArray markerArr)
            {
                var hitOrdinal = 0;
                for (var i = 0; i < markerArr.Count; i++)
                {
                    var m = (JsonObject)markerArr[i];
                    var rawName = Str(m, "name") ?? string.Empty;
                    var at = Num(m, "at_ms");
                    var args = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    if (m.TryGetValue("args", out var argsVal) && argsVal is JsonObject argsObj)
                    {
                        foreach (var kv in argsObj)
                        {
                            args[kv.Key] = ScalarText(kv.Value);
                        }
                    }

                    var name = rawName;
                    // 多段命中约定：剪辑事件名 hit:<segment>（手感设计/01 第 3.3 节"可多个，每个带 segment 序号"）。
                    if (rawName.StartsWith(HitMarker + ":", StringComparison.Ordinal))
                    {
                        name = HitMarker;
                        args["segment"] = rawName.Substring(HitMarker.Length + 1);
                    }

                    if (name == HitMarker)
                    {
                        if (!args.ContainsKey("segment"))
                        {
                            args["segment"] = hitOrdinal.ToString(CultureInfo.InvariantCulture);
                        }

                        hitOrdinal++;
                    }

                    markers.Add(new TimelineMarker(name, at, args));
                }
            }

            var windows = new List<TimelineCancelWindow>();
            if (obj.TryGetValue("cancel_windows", out var winVal) && winVal is JsonArray winArr)
            {
                for (var i = 0; i < winArr.Count; i++)
                {
                    var w = (JsonObject)winArr[i];
                    var cls = ParseActionClass(Str(w, "class")!);
                    double? close = w.TryGetValue("close_ms", out var cv) && cv is JsonNumber cn ? cn.Value : (double?)null;
                    windows.Add(new TimelineCancelWindow(cls, Num(w, "open_ms"), close));
                }
            }

            TimelineCombo? combo = null;
            if (obj.TryGetValue("combo", out var comboVal) && comboVal is JsonObject comboObj)
            {
                combo = new TimelineCombo(new Id(Str(comboObj, "next")!), Num(comboObj, "open_ms"), Num(comboObj, "close_ms"));
            }

            var hitPolicy = Str(obj, "hit_policy") == "continuous" ? TimelineHitPolicy.Continuous : TimelineHitPolicy.Marker;
            var costAt = Str(obj, "cost_at") switch
            {
                "active" => TimelineCostAt.Active,
                "first_hit" => TimelineCostAt.FirstHit,
                _ => TimelineCostAt.Commit,
            };
            var cooldownAt = Str(obj, "cooldown_at") switch
            {
                "active" => TimelineCooldownAt.Active,
                "finish" => TimelineCooldownAt.Finish,
                _ => TimelineCooldownAt.Commit,
            };

            ActionMotion? motion = null;
            if (obj.TryGetValue("motion", out var motionVal) && motionVal is JsonObject motionObj)
            {
                motion = ParseMotion(motionObj);
            }

            var feelRef = Str(obj, "feel_ref");

            var hitMode = Str(obj, "hit_mode") switch
            {
                "spatial" => TimelineHitMode.Spatial,
                "instant" => TimelineHitMode.Instant,
                _ => TimelineHitMode.Auto,
            };

            TimelineTargetAssist? assist = null;
            if (obj.TryGetValue("target_assist", out var assistVal) && assistVal is JsonObject assistObj)
            {
                var assistMode = Str(assistObj, "mode") == "close_distance" ? TimelineAssistMode.CloseDistance : TimelineAssistMode.FaceOnly;
                assist = new TimelineTargetAssist(
                    new Id(Str(assistObj, "chain_ref")!), Num(assistObj, "max_distance"), Num(assistObj, "max_angle_deg"), assistMode);
            }

            var hitAnchor = Str(obj, "hit_anchor") switch
            {
                "caster" => TimelineHitAnchor.Caster,
                "ground_point" => TimelineHitAnchor.GroundPoint,
                _ => TimelineHitAnchor.None,
            };

            return new TimelineDef(
                source, charge, Num(obj, "startup_ms"), Num(obj, "active_ms"), Num(obj, "recovery_ms"),
                markers, windows, combo, hitPolicy, costAt, cooldownAt, motion, string.IsNullOrEmpty(feelRef) ? null : feelRef,
                hitMode, Num(obj, "sample_step_ms"), Num(obj, "rehit_interval_ms"), assist,
                obj.TryGetValue("is_attack", out var isAttackVal) && isAttackVal is JsonBool isAttackBool ? isAttackBool.Value : (bool?)null,
                hitAnchor);
        }

        private static ActionMotion ParseMotion(JsonObject m)
        {
            var driver = Str(m, "driver") == "root_motion" ? ActionMotionDriver.RootMotion : ActionMotionDriver.Code;
            var kind = Str(m, "kind") switch
            {
                "dash" => ActionMotionKind.Dash,
                "step_back" => ActionMotionKind.StepBack,
                "charge" => ActionMotionKind.Charge,
                _ => ActionMotionKind.Lunge,
            };
            var direction = Str(m, "direction") switch
            {
                "input_snapshot" => ActionMotionDirection.InputSnapshot,
                "toward_target" => ActionMotionDirection.TowardTarget,
                _ => ActionMotionDirection.Facing,
            };
            var blocking = Str(m, "blocking") == "slide" ? ActionMotionBlocking.Slide : ActionMotionBlocking.Stop;
            // max_turn_deg 缺省 180（不限制偏转）：只有 toward_target/charge 逐 tick 重算方向时才读它，charge 必须声明（校验规则）。
            var maxTurn = m.TryGetValue("max_turn_deg", out var mt) && mt is JsonNumber mtn ? mtn.Value : 180.0;
            var curve = Str(m, "curve");
            return new ActionMotion(
                driver, kind, Num(m, "distance"), string.IsNullOrEmpty(curve) ? "linear" : curve!, direction, maxTurn, blocking);
        }

        /// <summary>输入类别名（小写，同 <c>found.input_action.class</c> 取值）→ <see cref="ActionClass"/>。</summary>
        public static ActionClass ParseActionClass(string name)
        {
            switch (name)
            {
                case "move": return ActionClass.Move;
                case "attack": return ActionClass.Attack;
                case "skill": return ActionClass.Skill;
                case "dodge": return ActionClass.Dodge;
                case "interact": return ActionClass.Interact;
                case "item": return ActionClass.Item;
                case "menu": return ActionClass.Menu;
                default: throw new ArgumentException("未知输入类别：" + name, nameof(name));
            }
        }

        /// <summary><see cref="ActionClass"/> → 小写类别名。</summary>
        public static string ActionClassName(ActionClass actionClass)
        {
            switch (actionClass)
            {
                case ActionClass.Move: return "move";
                case ActionClass.Attack: return "attack";
                case ActionClass.Skill: return "skill";
                case ActionClass.Dodge: return "dodge";
                case ActionClass.Interact: return "interact";
                case ActionClass.Item: return "item";
                default: return "menu";
            }
        }

        private static string? Str(JsonObject o, string key) =>
            o.TryGetValue(key, out var v) && v is JsonString s ? s.Value : null;

        private static ChargeScaleRange ScaleRange(JsonObject o, string key) =>
            o.TryGetValue(key, out var v) && v is JsonObject range
                ? new ChargeScaleRange(Num(range, "min"), Num(range, "max"))
                : ChargeScaleRange.Identity;

        private static double Num(JsonObject o, string key) =>
            o.TryGetValue(key, out var v) && v is JsonNumber n ? n.Value : 0.0;

        private static string ScalarText(JsonValue v)
        {
            switch (v)
            {
                case JsonString s: return s.Value;
                case JsonNumber n: return n.Value.ToString("R", CultureInfo.InvariantCulture);
                case JsonBool b: return b.Value ? "true" : "false";
                default: return string.Empty;
            }
        }
    }
}
