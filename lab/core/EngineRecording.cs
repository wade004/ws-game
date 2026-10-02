using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Lab
{
    /// <summary>
    /// 引擎宿主的运行期记录（06 第 4 节）：无头宿主证明不了的东西——动画命中帧事件与逻辑命中 tick 的对齐误差、镜头冲量插值曲线、
    /// 顿帧期间 rig/粒子是否冻结、引擎侧每帧耗时分布、相机相对输入经真实相机转出的世界方向、换装场景实际加载的图层剪辑。
    /// 记录是纯数据（内核不依赖任何引擎类型），由引擎宿主填写，由 <see cref="EngineMetricGroup"/> 折算成度量。
    /// 只有引擎宿主的运行才有它（<see cref="LabRecording.Engine"/>），无头宿主的运行为 null。
    /// </summary>
    public sealed class EngineRecording
    {
        /// <summary>引擎宿主的标签（指纹之外的人读标记）。</summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>格子的平面组合：<c>2d</c>、<c>2_5d</c>、<c>3d</c>（取格子短名的前缀）。</summary>
        public string Plane { get; set; } = string.Empty;

        /// <summary>引擎侧玩家 rig 的种类：<c>sprite</c>、<c>model</c>。</summary>
        public string RigKind { get; set; } = string.Empty;

        /// <summary>输入噪声模型标签（<see cref="InputNoiseModel.Id"/>；没有为 <c>none</c>）。</summary>
        public string InputNoise { get; set; } = "none";

        public List<HitAlignSample> HitAlignments { get; } = new List<HitAlignSample>();

        public List<CameraImpulseTrace> CameraImpulses { get; } = new List<CameraImpulseTrace>();

        public List<FreezeTrace> Freezes { get; } = new List<FreezeTrace>();

        /// <summary>引擎侧每帧驱动耗时（毫秒；真实时钟）。</summary>
        public List<double> FrameMilliseconds { get; } = new List<double>();

        /// <summary>引擎侧驱动的帧数。</summary>
        public int FramesDriven { get; set; }

        public List<ControlSample> Controls { get; } = new List<ControlSample>();

        public List<LayerAuditSample> LayerAudits { get; } = new List<LayerAuditSample>();

        /// <summary>引擎侧装配/驱动中被吞掉的异常与诊断（一条一行文本）。引擎侧失败不得改变逻辑运行（会让逻辑指纹与无头宿主不一致），所以记在这里并折成度量 <c>engine_errors</c>。</summary>
        public List<string> Errors { get; } = new List<string>();

        /// <summary>引擎侧 rig 实际播放过的剪辑切换（<c>单位标签:剪辑 id</c>，按发生顺序，相邻重复不记）。观测用，不进指纹：用来看"逻辑发了攻击、引擎真的播了攻击剪辑"。</summary>
        public List<string> ClipTransitions { get; } = new List<string>();
    }

    /// <summary>一次攻击的命中对齐样本：逻辑命中 tick（动作标记 <c>hit_frame</c>）与引擎动画 hit_frame 事件到达时刻。</summary>
    public sealed class HitAlignSample
    {
        public string Actor { get; }

        public int LogicTick { get; }

        /// <summary>逻辑命中时刻（秒）：<c>LogicTick × 固定步长</c>。</summary>
        public double LogicSeconds { get; }

        /// <summary>引擎动画 hit_frame 事件到达的模拟时刻（秒）；没有到达为 NaN。</summary>
        public double EngineSeconds { get; }

        public bool Present => !double.IsNaN(EngineSeconds);

        /// <summary>引擎 − 逻辑，毫秒（没有到达时为 0，计入 <see cref="Present"/> 为假的缺失数）。</summary>
        public double ErrorMilliseconds => Present ? (EngineSeconds - LogicSeconds) * 1000.0 : 0.0;

        public HitAlignSample(string actor, int logicTick, double logicSeconds, double engineSeconds)
        {
            Actor = actor;
            LogicTick = logicTick;
            LogicSeconds = logicSeconds;
            EngineSeconds = engineSeconds;
        }
    }

    /// <summary>一次镜头冲量的插值曲线：触发时刻、参数与此后每个采样点的相机位移模长。</summary>
    public sealed class CameraImpulseTrace
    {
        public int Tick { get; }

        /// <summary>画面高度比例的幅度（反馈流水线给出）。</summary>
        public double Magnitude { get; }

        public double DecayMs { get; }

        /// <summary>峰值位移（世界单位）：幅度 × 画面可视高度。</summary>
        public double PeakOffset { get; }

        /// <summary>冲量是否有方向（零方向是各向同性噪声，位移模长不是线性衰减，曲线形状不做线性偏差比较）。</summary>
        public bool Directional { get; }

        /// <summary>衰减结束之前又来了新的冲量（位移按向量叠加，这条曲线不再是单条冲量的曲线，不参与单调、残余与线性偏差度量）。</summary>
        public bool Truncated { get; set; }

        /// <summary>（触发后秒数，位移模长）；第一个点是峰值（t=0），最后一个点在衰减结束之后。</summary>
        public List<KeyValuePair<double, double>> Curve { get; } = new List<KeyValuePair<double, double>>();

        public CameraImpulseTrace(int tick, double magnitude, double decayMs, double peakOffset, bool directional = true)
        {
            Tick = tick;
            Magnitude = magnitude;
            DecayMs = decayMs;
            PeakOffset = peakOffset;
            Directional = directional;
        }

        /// <summary>曲线与声明的线性衰减 <c>峰值 × (1 − t / 衰减时长)</c> 的最大绝对偏差（世界单位）；曲线为空返回 0。</summary>
        public double MaxLinearDeviation()
        {
            var worst = 0.0;
            var decaySeconds = DecayMs / 1000.0;
            foreach (var point in Curve)
            {
                var expected = point.Key >= decaySeconds ? 0.0 : PeakOffset * (1.0 - point.Key / decaySeconds);
                worst = Math.Max(worst, Math.Abs(point.Value - expected));
            }

            return worst;
        }
    }

    /// <summary>一次顿帧表现冻结的观测：被冻结单位的 rig 与挂在它名下的粒子在冻结期间是否推进，旁观 rig 是否照常推进。</summary>
    public sealed class FreezeTrace
    {
        public string Unit { get; }

        public int StartTick { get; }

        public int Ticks { get; }

        /// <summary>冻结期间被冻结单位的 rig 动画时间推进量（秒；应为 0）。</summary>
        public double RigAdvanceSeconds { get; set; }

        /// <summary>冻结期间被冻结单位名下粒子的播放时间推进量（秒；该冻结包没声明冻粒子时为 NaN）。</summary>
        public double ParticleAdvanceSeconds { get; set; } = double.NaN;

        /// <summary>同一区间里旁观 rig（不在命中名单里）的动画时间推进量（秒；没有旁观 rig 为 NaN；应大于 0）。</summary>
        public double BystanderAdvanceSeconds { get; set; } = double.NaN;

        /// <summary>同一区间里对照粒子（挂在旁观单位名下、不被冻结的探针粒子）的播放时间推进量（秒；没有对照为 NaN；应大于 0）——证明"被冻结单位的粒子没推进"不是因为粒子根本没在播。</summary>
        public double ParticleControlAdvanceSeconds { get; set; } = double.NaN;

        public FreezeTrace(string unit, int startTick, int ticks)
        {
            Unit = unit;
            StartTick = startTick;
            Ticks = ticks;
        }
    }

    /// <summary>相机相对输入的一个样本：设备轴（摇杆）经转换得到的世界方向，与真实相机朝向算出的期望方向之间的夹角。</summary>
    public sealed class ControlSample
    {
        public int Tick { get; }

        public Vec2 Stick { get; }

        public double YawDegrees { get; }

        public Vec2 WorldDirection { get; }

        /// <summary>转换结果与期望方向（摇杆分量 × 真实相机的右/上轴在世界平面上的投影）的夹角（度）。</summary>
        public double ErrorDegrees { get; }

        /// <summary>
        /// 真实投影检验的夹角（度）：世界方向经真实相机投影到屏幕的方向，与摇杆方向（屏幕语义：右为 x、上为 y）之间的夹角——不经过相机轴的公式，
        /// 直接看"沿转换出的世界方向走，画面上是不是朝摇杆指的方向"。没有做这项检验（样本不是真实相机产生的）为 0。
        /// </summary>
        public double ScreenErrorDegrees { get; }

        public ControlSample(int tick, Vec2 stick, double yawDegrees, Vec2 worldDirection, double errorDegrees, double screenErrorDegrees = 0.0)
        {
            Tick = tick;
            Stick = stick;
            YawDegrees = yawDegrees;
            WorldDirection = worldDirection;
            ErrorDegrees = errorDegrees;
            ScreenErrorDegrees = screenErrorDegrees;
        }
    }

    /// <summary>换装场景里一层（或一个图标）实际加载的资源核对：帧数与尺寸是否与期望一致。</summary>
    public sealed class LayerAuditSample
    {
        public string Unit { get; }

        /// <summary>层名（或图标的物品 id）。</summary>
        public string Layer { get; }

        public string Resource { get; }

        public int FrameCount { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>实际加载结果与期望（帧数 &gt; 0、尺寸 &gt; 0）是否不一致。</summary>
        public bool Mismatch { get; }

        public LayerAuditSample(string unit, string layer, string resource, int frameCount, int width, int height, bool mismatch)
        {
            Unit = unit;
            Layer = layer;
            Resource = resource;
            FrameCount = frameCount;
            Width = width;
            Height = height;
            Mismatch = mismatch;
        }
    }
}
