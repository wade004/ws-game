using System;
using Core.Foundation.Feel;

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

        public bool ArrivalDecel { get; }

        public bool WallSlide { get; }

        public bool ApplyToPathFollowing { get; }

        /// <summary>击退抗性读哪个属性（0～1）；缺省（无值）为 null，视为 0。</summary>
        public string? KnockbackResistanceStat { get; }

        public MotionProfile(
            int version, double accelMs, double decelMs, string accelCurve, string brakeCurve, ReversePolicy reverse,
            double turnRateDegS, double walkSpeedRatio, double actionMoveSpeedRatio, bool actionTurnLock,
            bool keepMomentumOnActionEnd, bool arrivalDecel, bool wallSlide, bool applyToPathFollowing,
            string? knockbackResistanceStat)
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
            ArrivalDecel = arrivalDecel;
            WallSlide = wallSlide;
            ApplyToPathFollowing = applyToPathFollowing;
            KnockbackResistanceStat = knockbackResistanceStat;
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
                Bool(view, FeelFieldNames.ArrivalDecel),
                Bool(view, FeelFieldNames.WallSlide),
                Bool(view, FeelFieldNames.ApplyToPathFollowing),
                resist);
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

        private static InvalidOperationException Missing(string field) =>
            new InvalidOperationException($"运动档案字段 \"{field}\" 未设置（手感档案不完整，基础预设应提供全部运动组字段）");
    }
}
