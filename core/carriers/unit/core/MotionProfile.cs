using System;
using Core.Foundation.Feel;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>反向输入策略（手感设计/02 第 2 节 <c>reverse_policy</c>）。</summary>
    public enum ReversePolicy
    {
        /// <summary>立即反向（保留速率）。</summary>
        Instant,

        /// <summary>先沿原方向减速到零，再沿新方向加速。</summary>
        ThroughZero,
    }

    /// <summary>
    /// 运动档案（手感设计/02 第 2 节"档案移动组"的判定型字段）一次读出的值快照。
    /// <para>
    /// 判断记录：
    /// </para>
    /// <para>
    /// 1) 只经 <see cref="JudgingFeelView"/> 读取（判定型视图），毫秒字段取绝对值（毫秒保持毫秒，<c>FeelCalibration</c> 对
    /// 毫秒不做换算；运动积分按模拟步长把毫秒折成"tick 数"，见 <see cref="MotionMath.ToTicks"/>）。
    /// </para>
    /// <para>
    /// 2) 速度倍率字段（<c>walk_speed_ratio</c> 等）取<b>相对值</b>（<see cref="FeelCalibration"/> 换算前的倍数），目标速度
    /// = 该单位的移动速度属性 × 倍率：设计文档第 3.3 节写"目标速度 = 基础移速 × 模式倍率"，而单位的基础移速由属性系统承载
    /// （手感字段登记里 <c>walk_speed_ratio</c> 标注"属性已承载"）；若取标定后的绝对值（倍率 × 标定基础移速）会让
    /// 一个移速属性为 5 的单位按标定基础移速 4 去走，与既有"速度来自属性"的行为矛盾。标定换算用在距离字段上（击退距离、
    /// 动作位移距离、停止距离：身高倍数 → 世界单位）。
    /// </para>
    /// </summary>
    public readonly struct MotionProfile
    {
        /// <summary>解析版本号（<see cref="FeelHalfView.Version"/>），档案变化后下一 tick 生效。</summary>
        public int Version { get; }

        public double AccelMs { get; }

        public double DecelMs { get; }

        public string AccelCurve { get; }

        public string BrakeCurve { get; }

        public ReversePolicy Reverse { get; }

        public double TurnRateDegS { get; }

        /// <summary><c>walk</c> 模式目标速度相对移动速度属性的倍数（<c>run</c> 恒为 1）。</summary>
        public double WalkSpeedRatio { get; }

        public double ActionMoveSpeedRatio { get; }

        public bool ActionTurnLock { get; }

        public bool KeepMomentumOnActionEnd { get; }

        /// <summary>
        /// 动作位移窗口结束时是否保留末速度（<c>keep_momentum_on_motion_end</c>，手感设计/02 第 4 节）：假（缺省）立即清零，
        /// 位移距离等于声明值；真则末速度在后摇里按 <c>decel_ms</c> 滑行。与 <see cref="KeepMomentumOnActionEnd"/> 的分工：
        /// 这里管"位移窗口结束、动作还在后摇"，那里管"整个动作结束"。
        /// </summary>
        public bool KeepMomentumOnMotionEnd { get; }

        public bool ArrivalDecel { get; }

        public bool WallSlide { get; }

        public bool ApplyToPathFollowing { get; }

        /// <summary>击退抗性读哪个属性（0～1）；缺省（无值）为 null，视为 0。</summary>
        public string? KnockbackResistanceStat { get; }

        /// <summary>
        /// 单位体积半径（世界单位，<c>unit_body_radius</c> 经标定换算；手感设计/02 第 9 节）：大于 0 即该单位参与单位间体积阻挡，
        /// 缺省 0 表示无体积（不阻挡也不被阻挡，与未开启该能力时逐位一致）。
        /// </summary>
        public double UnitBodyRadius { get; }

        /// <summary>
        /// 动作位移穿过其他单位的体积的总开关（<c>dodge_through_units</c>，缺省假）；开启后具体哪些动作位移种类穿过由
        /// <see cref="PassesThroughKind"/>（<c>pass_through_motion_kinds</c>）决定，缺省 <c>dash</c>/<c>step_back</c>。
        /// </summary>
        public bool DodgeThroughUnits { get; }

        /// <summary>未写 <c>unit_separation_speed_ratio</c> 时的缺省分离速率（基础移速倍数）。</summary>
        public const double DefaultSeparationSpeedRatio = 0.5;

        /// <summary>未写 <c>forced_push_ratio</c> 时的缺省转移比例。</summary>
        public const double DefaultForcedPushRatio = 0.5;

        /// <summary>未写 <c>pass_through_motion_kinds</c> 时的缺省穿过种类（等价于此前写死的 dash、step_back）。</summary>
        public const string DefaultPassThroughKinds = "dash,step_back";

        /// <summary>
        /// 重叠分离速率（<c>unit_separation_speed_ratio</c>，基础移速的倍数，缺省 <see cref="DefaultSeparationSpeedRatio"/>）：本单位与别的有体积单位
        /// 重叠时每 tick 被推开的速率上限 = 倍数 × 该单位的移动速度属性；0 表示本单位不被推开（别的单位承担全部分离）。
        /// 只在声明了 <c>unit_body_radius</c> 时有意义。
        /// </summary>
        public double UnitSeparationSpeedRatio { get; }

        /// <summary>路径跟随与追击遇到别的单位的体积时局部绕行（<c>path_avoid_units</c>，缺省真）；假则撞到即停（开 <c>wall_slide</c> 时沿切向滑一段）。</summary>
        public bool AvoidUnitsOnPaths { get; }

        /// <summary>受控位移（击退）被别的单位体积挡住时把剩余位移转移给被撞单位（<c>forced_push_units</c>，缺省假：被挡即停，不推人）。</summary>
        public bool ForcedPushUnits { get; }

        /// <summary>转移比例（<c>forced_push_ratio</c>，0～1，缺省 <see cref="DefaultForcedPushRatio"/>）：被撞单位获得的位移 = 撞停时剩余位移 × 比例 ×（1 − 被撞单位的击退抗性）。</summary>
        public double ForcedPushRatio { get; }

        private readonly int _passKindMask;

        /// <summary>动作位移种类 <paramref name="kind"/> 是否在 <c>pass_through_motion_kinds</c> 声明的穿过集合内（还需 <see cref="DodgeThroughUnits"/> 为真才真正穿过）。</summary>
        public bool PassesThroughKind(ActionMotionKind kind) => (_passKindMask & (1 << (int)kind)) != 0;

        /// <summary>解析 <c>pass_through_motion_kinds</c> 的逗号分隔文本为种类掩码；出现未知种类名抛异常并给出字段名。</summary>
        public static int ParsePassKinds(string text)
        {
            var mask = 0;
            if (string.IsNullOrWhiteSpace(text)) return mask;
            foreach (var raw in text.Split(','))
            {
                var name = raw.Trim();
                if (name.Length == 0) continue;
                switch (name)
                {
                    case "lunge": mask |= 1 << (int)ActionMotionKind.Lunge; break;
                    case "dash": mask |= 1 << (int)ActionMotionKind.Dash; break;
                    case "step_back": mask |= 1 << (int)ActionMotionKind.StepBack; break;
                    case "charge": mask |= 1 << (int)ActionMotionKind.Charge; break;
                    default:
                        throw new InvalidOperationException(
                            $"手感字段 \"{FeelFieldNames.PassThroughMotionKinds}\" 含未知动作位移种类 \"{name}\"（合法取值 lunge|dash|step_back|charge，逗号分隔）");
                }
            }

            return mask;
        }

        /// <summary>既有 15 参数构造（物理签名不变，ABI 只加不改）：<see cref="KeepMomentumOnMotionEnd"/> 取缺省假。</summary>
        public MotionProfile(
            int version, double accelMs, double decelMs, string accelCurve, string brakeCurve, ReversePolicy reverse,
            double turnRateDegS, double walkSpeedRatio, double actionMoveSpeedRatio, bool actionTurnLock,
            bool keepMomentumOnActionEnd, bool arrivalDecel, bool wallSlide, bool applyToPathFollowing,
            string? knockbackResistanceStat)
            : this(
                version, accelMs, decelMs, accelCurve, brakeCurve, reverse, turnRateDegS, walkSpeedRatio, actionMoveSpeedRatio,
                actionTurnLock, keepMomentumOnActionEnd, false, arrivalDecel, wallSlide, applyToPathFollowing, knockbackResistanceStat)
        {
        }

        /// <summary>既有 16 参数构造（物理签名不变，ABI 只加不改）：体积字段取缺省（无体积、闪避不穿人）。</summary>
        public MotionProfile(
            int version, double accelMs, double decelMs, string accelCurve, string brakeCurve, ReversePolicy reverse,
            double turnRateDegS, double walkSpeedRatio, double actionMoveSpeedRatio, bool actionTurnLock,
            bool keepMomentumOnActionEnd, bool keepMomentumOnMotionEnd, bool arrivalDecel, bool wallSlide,
            bool applyToPathFollowing, string? knockbackResistanceStat)
            : this(
                version, accelMs, decelMs, accelCurve, brakeCurve, reverse, turnRateDegS, walkSpeedRatio, actionMoveSpeedRatio,
                actionTurnLock, keepMomentumOnActionEnd, keepMomentumOnMotionEnd, arrivalDecel, wallSlide, applyToPathFollowing,
                knockbackResistanceStat, 0.0, false)
        {
        }

        /// <summary>既有 18 参数构造（物理签名不变，ABI 只加不改）：分离/绕行/推人/穿过种类取缺省。</summary>
        public MotionProfile(
            int version, double accelMs, double decelMs, string accelCurve, string brakeCurve, ReversePolicy reverse,
            double turnRateDegS, double walkSpeedRatio, double actionMoveSpeedRatio, bool actionTurnLock,
            bool keepMomentumOnActionEnd, bool keepMomentumOnMotionEnd, bool arrivalDecel, bool wallSlide,
            bool applyToPathFollowing, string? knockbackResistanceStat, double unitBodyRadius, bool dodgeThroughUnits)
            : this(
                version, accelMs, decelMs, accelCurve, brakeCurve, reverse, turnRateDegS, walkSpeedRatio, actionMoveSpeedRatio,
                actionTurnLock, keepMomentumOnActionEnd, keepMomentumOnMotionEnd, arrivalDecel, wallSlide, applyToPathFollowing,
                knockbackResistanceStat, unitBodyRadius, dodgeThroughUnits,
                DefaultSeparationSpeedRatio, true, false, DefaultForcedPushRatio, ParsePassKinds(DefaultPassThroughKinds))
        {
        }

        /// <summary>
        /// 完整构造：在 18 参数构造之上给出单位体积的解除限制字段（重叠分离、路径绕行、受控位移推人、穿过种类掩码；
        /// 掩码由 <see cref="ParsePassKinds"/> 生成）。
        /// </summary>
        public MotionProfile(
            int version, double accelMs, double decelMs, string accelCurve, string brakeCurve, ReversePolicy reverse,
            double turnRateDegS, double walkSpeedRatio, double actionMoveSpeedRatio, bool actionTurnLock,
            bool keepMomentumOnActionEnd, bool keepMomentumOnMotionEnd, bool arrivalDecel, bool wallSlide,
            bool applyToPathFollowing, string? knockbackResistanceStat, double unitBodyRadius, bool dodgeThroughUnits,
            double unitSeparationSpeedRatio, bool avoidUnitsOnPaths, bool forcedPushUnits, double forcedPushRatio, int passKindMask)
        {
            Version = version;
            AccelMs = accelMs;
            DecelMs = decelMs;
            AccelCurve = accelCurve ?? "linear";
            BrakeCurve = brakeCurve ?? "linear";
            Reverse = reverse;
            TurnRateDegS = turnRateDegS;
            WalkSpeedRatio = walkSpeedRatio;
            ActionMoveSpeedRatio = actionMoveSpeedRatio;
            ActionTurnLock = actionTurnLock;
            KeepMomentumOnActionEnd = keepMomentumOnActionEnd;
            KeepMomentumOnMotionEnd = keepMomentumOnMotionEnd;
            ArrivalDecel = arrivalDecel;
            WallSlide = wallSlide;
            ApplyToPathFollowing = applyToPathFollowing;
            KnockbackResistanceStat = knockbackResistanceStat;
            UnitBodyRadius = unitBodyRadius > 0.0 ? unitBodyRadius : 0.0;
            DodgeThroughUnits = dodgeThroughUnits;
            UnitSeparationSpeedRatio = unitSeparationSpeedRatio > 0.0 ? unitSeparationSpeedRatio : 0.0;
            AvoidUnitsOnPaths = avoidUnitsOnPaths;
            ForcedPushUnits = forcedPushUnits;
            ForcedPushRatio = forcedPushRatio < 0.0 ? 0.0 : (forcedPushRatio > 1.0 ? 1.0 : forcedPushRatio);
            _passKindMask = passKindMask;
        }

        /// <summary>
        /// 设计文档第 2 节"全部缺省值合起来等于既有行为"的那组值：瞬时达速/停止、瞬时转向、<c>walk</c> 倍率 1、不滑墙。
        /// （<c>feel.preset.rpg_classic</c> 的运动字段与它逐项一致。）
        /// </summary>
        public static MotionProfile LegacyEquivalent { get; } = new MotionProfile(
            0, 0, 0, "linear", "linear", ReversePolicy.Instant, 0, 1.0, 0, true, false, false, false, false, null);

        /// <summary>从判定型视图读出运动档案；字段未设置（档案不完整）抛异常并给出字段名，不静默取缺省。</summary>
        public static MotionProfile Read(JudgingFeelView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            var reverseText = Text(view, FeelFieldNames.ReversePolicy);
            ReversePolicy reverse;
            switch (reverseText)
            {
                case "instant": reverse = ReversePolicy.Instant; break;
                case "through_zero": reverse = ReversePolicy.ThroughZero; break;
                default:
                    throw new InvalidOperationException($"手感字段 \"{FeelFieldNames.ReversePolicy}\" 取值 \"{reverseText}\" 不是 instant|through_zero");
            }

            string? resist = null;
            var resistValue = view.GetAbsolute(FeelFieldNames.KnockbackResistanceStat);
            if (!resistValue.IsNone) resist = resistValue.AsText();

            return new MotionProfile(
                view.Version,
                Num(view, FeelFieldNames.AccelMs),
                Num(view, FeelFieldNames.DecelMs),
                Text(view, FeelFieldNames.AccelCurve),
                Text(view, FeelFieldNames.BrakeCurve),
                reverse,
                Num(view, FeelFieldNames.TurnRateDegS),
                RawNum(view, FeelFieldNames.WalkSpeedRatio),
                RawNum(view, FeelFieldNames.ActionMoveSpeedRatio),
                Bool(view, FeelFieldNames.ActionTurnLock),
                Bool(view, FeelFieldNames.KeepMomentumOnActionEnd),
                OptionalBool(view, FeelFieldNames.KeepMomentumOnMotionEnd),
                Bool(view, FeelFieldNames.ArrivalDecel),
                Bool(view, FeelFieldNames.WallSlide),
                Bool(view, FeelFieldNames.ApplyToPathFollowing),
                resist,
                OptionalNum(view, FeelFieldNames.UnitBodyRadius),
                OptionalBool(view, FeelFieldNames.DodgeThroughUnits),
                OptionalRaw(view, FeelFieldNames.UnitSeparationSpeedRatio, DefaultSeparationSpeedRatio),
                OptionalBool(view, FeelFieldNames.PathAvoidUnits, true),
                OptionalBool(view, FeelFieldNames.ForcedPushUnits),
                OptionalRaw(view, FeelFieldNames.ForcedPushRatio, DefaultForcedPushRatio),
                ParsePassKinds(OptionalText(view, FeelFieldNames.PassThroughMotionKinds, DefaultPassThroughKinds)));
        }

        private static double Num(JudgingFeelView view, string field)
        {
            if (!view.TryGetNumber(field, out var v)) throw Missing(field);
            return v;
        }

        private static double RawNum(JudgingFeelView view, string field)
        {
            var raw = view.GetRaw(field);
            if (raw.Kind != FeelValueKind.Number) throw Missing(field);
            return raw.AsNumber();
        }

        private static string Text(JudgingFeelView view, string field)
        {
            var v = view.GetAbsolute(field);
            if (v.Kind != FeelValueKind.Text) throw Missing(field);
            return v.AsText();
        }

        private static bool Bool(JudgingFeelView view, string field)
        {
            var v = view.GetAbsolute(field);
            if (v.Kind != FeelValueKind.Bool) throw Missing(field);
            return v.AsBool();
        }

        /// <summary>可选布尔字段：没有值（档案没写）取 <paramref name="absent"/>（缺省假）。</summary>
        private static bool OptionalBool(JudgingFeelView view, string field, bool absent = false)
        {
            var v = view.GetAbsolute(field);
            return v.Kind == FeelValueKind.Bool ? v.AsBool() : absent;
        }

        /// <summary>可选相对数值字段（标定前的倍数）：没有值取 <paramref name="absent"/>。</summary>
        private static double OptionalRaw(JudgingFeelView view, string field, double absent)
        {
            var v = view.GetRaw(field);
            return v.Kind == FeelValueKind.Number ? v.AsNumber() : absent;
        }

        /// <summary>可选文本字段：没有值取 <paramref name="absent"/>。</summary>
        private static string OptionalText(JudgingFeelView view, string field, string absent)
        {
            var v = view.GetAbsolute(field);
            return v.Kind == FeelValueKind.Text ? v.AsText() : absent;
        }

        /// <summary>可选数值字段（标定后的绝对值）：没有值（档案没写）取 0。</summary>
        private static double OptionalNum(JudgingFeelView view, string field)
        {
            var v = view.GetAbsolute(field);
            return v.Kind == FeelValueKind.Number ? v.AsNumber() : 0.0;
        }

        private static InvalidOperationException Missing(string field) =>
            new InvalidOperationException($"运动档案字段 \"{field}\" 未设置（手感档案不完整，基础预设应提供全部运动组字段）");
    }
}
