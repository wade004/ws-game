using System;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.Feel
{
    /// <summary>
    /// 一款游戏的手感标定（<c>feel.calibration</c> 的一行；手感设计/00 第 6 节、05 第 7 节）：把档案里的相对量换算为
    /// 绝对量的全部依据。纯值对象、不可变；解析器的最后一步调用 <see cref="ToAbsolute"/> 与
    /// <see cref="MillisecondsToTicks"/>。
    /// <para>
    /// 判断记录：模拟步长（毫秒→tick 的依据）<b>不</b>放进标定表——标定表只描述"这款游戏的世界与镜头有多大"，
    /// 步长是模拟时钟的策略配置（<c>SimLoopOptions.StepSeconds</c>），由装配根在创建解析器时传入
    /// （<see cref="FeelAssemblyOptions.StepSeconds"/>）；这样同一份标定在 30/60/120 tick/s 下都成立，
    /// 也避免两处各存一份步长。
    /// </para>
    /// </summary>
    public sealed class FeelCalibration
    {
        /// <summary>标定行 id（<c>feel.calibration</c> 主键）；测试里手工构造时可任意。</summary>
        public string Id { get; }

        /// <summary>参考身高（世界单位）：身高倍数单位的换算依据。</summary>
        public double ReferenceHeight { get; }

        /// <summary>
        /// 参考基础移速（世界单位/秒）：只用于速度倍数字段在 <see cref="ResolvedFeel"/> 绝对值视图里的换算（倍数 × 本值，调参面板与实验室展示参考速度用）。
        /// <b>不是移动的速度基准</b>：运动层的目标速度 = 倍数（标定前的相对值）× 该单位的移动速度属性（<c>MotionProfile</c> 判断记录 2、ADR-0146），
        /// 改本值不改任何单位的实际移动速度。
        /// </summary>
        public double BaseSpeed { get; }

        /// <summary>动画帧率（帧/秒）：呈现侧换算动画帧用，解析器不参与。</summary>
        public double AnimationFps { get; }

        /// <summary>参考镜头高度（世界单位，画面纵向可见范围）：画面高度比例单位的换算依据。</summary>
        public double ReferenceCameraHeight { get; }

        /// <summary>参考镜头缩放（倍率）：解析器不参与，随标定进入指纹元数据。</summary>
        public double ReferenceZoom { get; }

        /// <summary>像素密度（像素/世界单位，沿用像素与世界坐标换算约定）：解析器不参与，随标定进入指纹元数据。</summary>
        public double PixelsPerUnit { get; }

        /// <summary>标记容差（毫秒）：数据里的标记时间与剪辑标记比对的容差，解析器不参与。</summary>
        public double MarkerToleranceMs { get; }

        /// <summary>基础预设行 id（<c>feel.preset</c>，覆盖顺序第 1 层）。</summary>
        public string BasePresetId { get; }

        public FeelCalibration(
            string id,
            string basePresetId,
            double referenceHeight,
            double baseSpeed,
            double animationFps,
            double referenceCameraHeight,
            double referenceZoom,
            double pixelsPerUnit,
            double markerToleranceMs)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("标定 id 不能为空", nameof(id));
            if (string.IsNullOrEmpty(basePresetId)) throw new ArgumentException("基础预设 id 不能为空", nameof(basePresetId));
            RequirePositive(referenceHeight, nameof(referenceHeight));
            RequirePositive(baseSpeed, nameof(baseSpeed));
            RequirePositive(animationFps, nameof(animationFps));
            RequirePositive(referenceCameraHeight, nameof(referenceCameraHeight));
            RequirePositive(referenceZoom, nameof(referenceZoom));
            RequirePositive(pixelsPerUnit, nameof(pixelsPerUnit));
            if (!double.IsFinite(markerToleranceMs) || markerToleranceMs < 0)
            {
                throw new ArgumentException("标记容差必须是非负有限数", nameof(markerToleranceMs));
            }

            Id = id;
            BasePresetId = basePresetId;
            ReferenceHeight = referenceHeight;
            BaseSpeed = baseSpeed;
            AnimationFps = animationFps;
            ReferenceCameraHeight = referenceCameraHeight;
            ReferenceZoom = referenceZoom;
            PixelsPerUnit = pixelsPerUnit;
            MarkerToleranceMs = markerToleranceMs;
        }

        private static void RequirePositive(double value, string name)
        {
            if (!double.IsFinite(value) || value <= 0) throw new ArgumentException("标定值必须是正的有限数：" + name, name);
        }

        /// <summary>
        /// 把相对值按字段单位换算为绝对值（手感设计/00 第 6 节表）：毫秒保持毫秒（tick 另算）；身高倍数 × 参考身高；
        /// 速度倍数 × 参考基础移速（只是绝对值视图，运动层不用它，见 <see cref="BaseSpeed"/>）；"基础移速下的秒数" × 参考基础移速（得到世界距离，
        /// 目前没有任何字段使用这个单位）；画面高度比例 × 参考镜头高度；
        /// 其余单位（倍率、角度、角速度、强度档、计数）无需换算。
        /// </summary>
        public double ToAbsolute(FeelUnit unit, double relative)
        {
            switch (unit)
            {
                case FeelUnit.BodyHeights: return relative * ReferenceHeight;
                case FeelUnit.BaseSpeedRatio: return relative * BaseSpeed;
                case FeelUnit.BaseSpeedSeconds: return relative * BaseSpeed;
                case FeelUnit.ScreenHeightRatio: return relative * ReferenceCameraHeight;
                default: return relative;
            }
        }

        /// <summary>
        /// 毫秒按固定步长换算为 tick：四舍五入（远离零），非零值至少 1 tick，零保持零（手感设计/00 第 5 节、05 第 10 节第 7 条）。
        /// </summary>
        public static int MillisecondsToTicks(double milliseconds, double stepSeconds)
        {
            if (!double.IsFinite(stepSeconds) || stepSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(stepSeconds));
            if (!double.IsFinite(milliseconds) || milliseconds <= 0) return 0;
            // 先把比值规整到 1e-9（吃掉 25 ms / (1/60 s) 这类"本应恰为 1.5"的浮点误差），再远离零取整，
            // 保证半数边界上的结果与十进制手算一致且跨平台确定。
            var ratio = Math.Round(milliseconds / (stepSeconds * 1000.0), 9);
            var rounded = Math.Round(ratio, MidpointRounding.AwayFromZero);
            if (rounded < 1) rounded = 1;
            if (rounded > int.MaxValue) rounded = int.MaxValue;
            return (int)rounded;
        }
    }
}
