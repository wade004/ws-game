#nullable enable
// OrbitCameraController：3D 演示场景的鼠标环绕镜头状态机（ADR-0159）——纯数值，不碰引擎对象，所以可以不开窗口确定性地单测。
//
// 判断记录（目标值与平滑值分开）：鼠标拖动与滚轮只改"目标值"（偏航、俯仰偏移、缩放系数），每个控制器帧由 <see cref="Step"/> 按真实帧间隔把当前值指数逼近目标值
// （时间常数 <see cref="OrbitCameraOptions.SmoothingSeconds"/>），所以手感是平滑的，但同一串（拖动量、帧间隔）永远得到同一串当前值；逼近到 <see cref="SnapEpsilon"/> 内直接并拢，
// 复位时当前值逐位等于缺省值，不留指数尾巴。当前偏航才是镜头真正的偏航：宿主在固定步边界把它提交给相机的朝向查询（ADR-0135 后果，见 UnityCamera.SampleYawAtCommit）。
// 判断记录（偏航不设限、俯仰只在缺省俯角附近的窄区间）：偏航可以无限转（只是数值上周期性折回，见 <see cref="Normalize"/>，不改变任何三角函数结果）；俯仰存成"相对基准俯角的偏移"
// （基准 = 场景缺省俯角或模板取景给的俯角），偏移夹在 [-<see cref="OrbitCameraOptions.PitchRangeBelowBase"/>, +<see cref="OrbitCameraOptions.PitchRangeAboveBase"/>]，
// 合成后的绝对俯角再夹进 [<see cref="OrbitCameraOptions.PitchMinDegrees"/>, <see cref="OrbitCameraOptions.PitchMaxDegrees"/>]：上限留出余量不让视野顶端越过地平线（越过就会看到地面铺不到的边缘），
// 下限不到正俯视（正俯视与固定相机的构图差太大）。缩放存成"相对基准缩放的系数"，同样夹区间。
using Adapter.Unity;
using System;

namespace FeelLab.Unity
{
    /// <summary>环绕镜头的灵敏度与区间；缺省值见各属性。</summary>
    public sealed class OrbitCameraOptions
    {
        /// <summary>水平拖动 1 像素转的偏航（度）。</summary>
        public double YawDegreesPerPixel { get; set; } = 0.22;

        /// <summary>垂直拖动 1 像素改的俯角（度）；鼠标向上拖 = 俯角增大（视线抬向地平线）。</summary>
        public double PitchDegreesPerPixel { get; set; } = 0.16;

        /// <summary>俯角偏移可以低于基准俯角多少度（更接近正俯视）。</summary>
        public double PitchRangeBelowBase { get; set; } = 25.0;

        /// <summary>俯角偏移可以高于基准俯角多少度（更接近平视）。</summary>
        public double PitchRangeAboveBase { get; set; } = 20.0;

        /// <summary>绝对俯角下限（度）。</summary>
        public double PitchMinDegrees { get; set; } = 12.0;

        /// <summary>
        /// 绝对俯角上限（度）：缺省 66——视野半角（透视视场角的一半，缺省 20 度）加它要小于 90，视野顶端才不越过地平线（地面铺得下）。
        /// 自由镜头（ADR-0161）想抬头看天就把它调到 90 以上（0 = 正俯视，90 = 水平，大于 90 = 仰视），同时必须在相机上声明同样宽的俯仰范围
        /// （<see cref="EngineLabOptions.CameraPitchRange"/>，缺省 [0, 89]）：区间落在声明范围之外是舞台装配期的声明错误。越过水平后视野顶端看到的是天空而不是地面边缘，
        /// 场景要自己铺天空与远景，并让地面覆盖到视线与地面的交点（仰视时视线不再与地面相交）。
        /// </summary>
        public double PitchMaxDegrees { get; set; } = 66.0;

        /// <summary>缩放系数下限（越小越近）：基准缩放 × 系数不得低于相机缩放区间下限，所以别设得太小。</summary>
        public double ZoomMinFactor { get; set; } = 0.6;

        /// <summary>缩放系数上限（越大越远）。</summary>
        public double ZoomMaxFactor { get; set; } = 2.0;

        /// <summary>滚轮每一格（向上为正 = 拉近）乘到缩放系数上的倍率：系数 *= 该值的格数次方。</summary>
        public double ZoomFactorPerNotch { get; set; } = 0.88;

        /// <summary>当前值逼近目标值的时间常数（秒）；0 = 不平滑（立即到位）。</summary>
        public double SmoothingSeconds { get; set; } = 0.06;

        /// <summary>环绕镜头可用时演示场景地面砖的边长（世界单位）：要大到俯角上限与最大拉远时视口四角都落在地面上（固定镜头的 60 不够）。</summary>
        public float FloorSize { get; set; } = 200f;
    }

    public sealed class OrbitCameraController
    {
        /// <summary>当前值与目标值相差小于它就并拢（度 / 系数）。</summary>
        public const double SnapEpsilon = 1e-4;

        private const double NormalizeThresholdDegrees = 3600.0;

        private double _targetYaw;
        private double _targetPitchOffset;
        private double _targetZoomFactor = 1.0;

        public OrbitCameraController(OrbitCameraOptions? options = null)
        {
            Options = options ?? new OrbitCameraOptions();
        }

        public OrbitCameraOptions Options { get; }

        /// <summary>环绕是否生效（关 = 固定镜头：拖动与滚轮被忽略，姿态保持缺省）。</summary>
        public bool Enabled { get; private set; }

        /// <summary>当前偏航（度，逆时针为正，镜头真正用的值）。</summary>
        public double Yaw { get; private set; }

        /// <summary>当前俯角偏移（度，相对基准俯角）。</summary>
        public double PitchOffset { get; private set; }

        /// <summary>当前缩放系数（相对基准缩放，1 = 缺省）。</summary>
        public double ZoomFactor { get; private set; } = 1.0;

        public double TargetYaw => _targetYaw;

        public double TargetPitchOffset => _targetPitchOffset;

        public double TargetZoomFactor => _targetZoomFactor;

        /// <summary>当前姿态与缺省姿态逐位相同（偏航 0、俯角偏移 0、缩放系数 1，且目标值也在缺省处）。</summary>
        public bool IsAtDefault =>
            Yaw == 0.0 && PitchOffset == 0.0 && ZoomFactor == 1.0 && _targetYaw == 0.0 && _targetPitchOffset == 0.0 && _targetZoomFactor == 1.0;

        /// <summary>打开/关闭环绕。关闭 = 回到固定镜头：姿态立即复位到缺省（"复位"是逐位的，见 <see cref="SnapToDefault"/>）。</summary>
        public void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            if (!enabled)
            {
                SnapToDefault();
            }
        }

        /// <summary>鼠标右键拖动一帧的位移（像素；向右、向上为正）：向右拖 = 镜头向右转（偏航减小），向上拖 = 俯角增大。环绕关闭时忽略。</summary>
        public void AddDrag(double dxPixels, double dyPixels)
        {
            if (!Enabled)
            {
                return;
            }

            _targetYaw -= dxPixels * Options.YawDegreesPerPixel;
            _targetPitchOffset = Clamp(_targetPitchOffset + dyPixels * Options.PitchDegreesPerPixel, -Options.PitchRangeBelowBase, Options.PitchRangeAboveBase);
        }

        /// <summary>滚轮（格数，向上滚为正 = 拉近）。环绕关闭时忽略。</summary>
        public void AddWheel(double notches)
        {
            if (!Enabled || notches == 0.0)
            {
                return;
            }

            _targetZoomFactor = Clamp(_targetZoomFactor * Math.Pow(Options.ZoomFactorPerNotch, notches), Options.ZoomMinFactor, Options.ZoomMaxFactor);
        }

        /// <summary>直接设目标值（面板与测试用；仍受区间约束）。</summary>
        public void SetTarget(double yawDegrees, double pitchOffsetDegrees, double zoomFactor)
        {
            _targetYaw = yawDegrees;
            _targetPitchOffset = Clamp(pitchOffsetDegrees, -Options.PitchRangeBelowBase, Options.PitchRangeAboveBase);
            _targetZoomFactor = Clamp(zoomFactor, Options.ZoomMinFactor, Options.ZoomMaxFactor);
        }

        /// <summary>目标值与当前值同时跳到给定姿态（受区间约束），不经平滑。</summary>
        public void SnapTo(double yawDegrees, double pitchOffsetDegrees, double zoomFactor)
        {
            SetTarget(yawDegrees, pitchOffsetDegrees, zoomFactor);
            Yaw = _targetYaw;
            PitchOffset = _targetPitchOffset;
            ZoomFactor = _targetZoomFactor;
        }

        /// <summary>目标值与当前值同时回到缺省姿态，不经平滑（切回固定镜头用，逐位复位）。</summary>
        public void SnapToDefault() => SnapTo(0.0, 0.0, 1.0);

        /// <summary>
        /// 平滑复位：只把目标值设回缺省，当前值经 <see cref="Step"/> 逼近（偏航先折到 ±180 度内，走最短的回头路）。
        /// </summary>
        public void ResetSmooth()
        {
            var turns = Math.Round(Yaw / 360.0);
            Yaw -= 360.0 * turns;
            _targetYaw = 0.0;
            _targetPitchOffset = 0.0;
            _targetZoomFactor = 1.0;
        }

        /// <summary>
        /// 推进一个控制器帧：当前值按真实帧间隔逼近目标值；返回当前值是否变了。<paramref name="seconds"/> 非正时不推进。
        /// </summary>
        public bool Step(double seconds)
        {
            if (!Enabled || !(seconds > 0.0))
            {
                return false;
            }

            var before = (Yaw, PitchOffset, ZoomFactor);
            var blend = Options.SmoothingSeconds > 0.0 ? 1.0 - Math.Exp(-seconds / Options.SmoothingSeconds) : 1.0;
            Yaw = Approach(Yaw, _targetYaw, blend);
            PitchOffset = Approach(PitchOffset, _targetPitchOffset, blend);
            ZoomFactor = Approach(ZoomFactor, _targetZoomFactor, blend);
            Normalize();
            return (Yaw, PitchOffset, ZoomFactor) != before;
        }

        /// <summary>绝对俯角（度）：基准俯角 + 当前偏移，夹进 [<see cref="OrbitCameraOptions.PitchMinDegrees"/>, <see cref="OrbitCameraOptions.PitchMaxDegrees"/>]。</summary>
        public double EffectivePitch(double basePitchDegrees) =>
            Clamp(basePitchDegrees + PitchOffset, Options.PitchMinDegrees, Options.PitchMaxDegrees);

        private static double Approach(double current, double target, double blend)
        {
            var delta = target - current;
            return Math.Abs(delta) < SnapEpsilon ? target : current + delta * blend;
        }

        /// <summary>偏航数值折回：偏航绝对值太大时把当前值与目标值同时减去整圈（三角函数结果不变），避免长时间旋转后浮点精度下降。</summary>
        private void Normalize()
        {
            if (Math.Abs(Yaw) > NormalizeThresholdDegrees)
            {
                var turns = Math.Round(Yaw / 360.0);
                Yaw -= 360.0 * turns;
                _targetYaw -= 360.0 * turns;
            }
        }

        private static double Clamp(double value, double min, double max) => value < min ? min : (value > max ? max : value);
    }
}
