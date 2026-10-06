#nullable enable
// EngineLabOptions：实验室引擎宿主的运行选项（手感设计/06 第 4 节）。全部有缺省值；不声明任何选项等于"只驱动引擎侧表现、不改逻辑输入"。
using System;
using Lab;

namespace Adapter.Unity.LabHost
{
    public sealed class EngineLabOptions
    {
        /// <summary>
        /// 引擎侧渲染隔离层（缺省 30）：舞台把自己的全部物体放进这一层，舞台相机只渲染这一层，并在舞台存续期间把这一层从场内其它相机的
        /// 剔除遮罩里摘掉，销毁时原样恢复；这样实验室舞台不会画进测试/编辑器场景里别的相机，也不受别的相机影响。
        /// </summary>
        public int IsolationLayer { get; set; } = 30;

        /// <summary>
        /// 控制空间：<c>null</c> 取格子自己声明的值（<c>world</c> 或 <c>camera_relative</c>）；显式给出则覆盖。
        /// <c>camera_relative</c> 时移动动作按框架原生的相机相对控制空间声明（<c>InputControlSpace.CameraRelative</c>），轴值由输入映射按舞台相机的
        /// 偏航换算成世界方向；宿主只做三向检验（见 <see cref="ControlSpace"/>），不再自己换算。
        /// </summary>
        public string? ControlSpaceOverride { get; set; }

        /// <summary>舞台相机的偏航角（度；绕视线轴逆时针）。缺省 0 时相机相对转换是恒等变换，逻辑与世界控制空间逐字节一致。</summary>
        public double CameraYawDegrees { get; set; }

        /// <summary>
        /// 舞台相机的固定俯仰角（度，0 = 正俯视，夹在 [0, 89]）；<c>null</c>（缺省）= 不声明俯仰（相机保持正俯视，与引入俯仰能力之前逐位一致），
        /// 但 <see cref="HonorCellCameraMode"/> 打开且格子相机模式是 <c>fixed_pitch</c> 时取 <see cref="FixedPitchDegrees"/>。
        /// </summary>
        public double? CameraPitchDegrees { get; set; }

        /// <summary>舞台相机是否用透视投影：<c>null</c>（缺省）= 不声明（正交），但 <see cref="HonorCellCameraMode"/> 打开且格子相机模式是 <c>fixed_pitch</c> 时为真。</summary>
        public bool? CameraPerspective { get; set; }

        /// <summary>透视投影的垂直视场角（度，缺省 40；只在透视投影下生效）。</summary>
        public double CameraFieldOfViewDegrees { get; set; } = 40.0;

        /// <summary>
        /// 是否按格子声明的相机模式装配舞台相机（缺省 <c>false</c>：相机只受上面的显式选项影响，既有运行与基线不变）：打开后相机模式
        /// <c>fixed_pitch</c> 的格子用 <see cref="FixedPitchDegrees"/> 的俯角与透视投影，<c>ortho_top</c> 仍是正俯视正交。
        /// </summary>
        public bool HonorCellCameraMode { get; set; }

        /// <summary><c>fixed_pitch</c> 相机模式的缺省俯角（度，缺省 45；只在 <see cref="HonorCellCameraMode"/> 打开时用）。</summary>
        public double FixedPitchDegrees { get; set; } = 45.0;

        /// <summary>
        /// 是否测 GPU 帧耗时（缺省 <c>true</c>）：有图形设备时，舞台每帧把舞台相机真实渲染一次到一张小的离屏纹理并等 GPU 完成，计入 <c>engine.gpu_ms_*</c>；
        /// 没有图形设备（批处理无图形模式）时不渲染，度量标为 <c>unavailable</c>。关闭则始终 <c>unavailable</c>。渲染不改任何逻辑状态。
        /// </summary>
        public bool GpuTiming { get; set; } = true;

        /// <summary>输入噪声模型（系统延迟与抖动，见 <see cref="InputNoiseModel"/>）；<c>null</c> 为无噪声。噪声在脚本进入宿主之前施加，逻辑与引擎两侧共同看到带噪输入。</summary>
        public InputNoiseModel? Noise { get; set; }

        /// <summary>输入噪声回放：给出上一次录下的噪声记录（<see cref="InputNoiseRecord"/>），逐事件还原当时的带噪脚本；优先于 <see cref="Noise"/>。</summary>
        public InputNoiseRecord? NoiseReplay { get; set; }

        /// <summary>为每次顿帧冻结的单位生成一个挂在它名下的探针粒子，用来观测粒子冻结（实验室打击反馈包本身不含特效）。缺省开。</summary>
        public bool ProbeParticles { get; set; } = true;

        /// <summary>每个新视图创建之后，等待共享资源加载器把在途加载做完的上限（毫秒）。</summary>
        public int MaxLoadWaitMs { get; set; } = 3000;

        /// <summary>引擎侧驱动出错时是否让运行失败：缺省 false，错误记入 <c>engine.engine_errors</c>，逻辑运行不受影响。</summary>
        public bool ThrowOnEngineError { get; set; }

        /// <summary>
        /// 演示场景（ADR-0154，只在 <see cref="Interactive"/> 下生效）：同一套逻辑与手感运行时，只把呈现换成真实美术——
        /// 外形登记按单位种类给英雄/小怪/精英/木桩外形，地面与场景道具换成美术资源，命中火花/挥砍拖影/尘土/冲击波环与 HUD 数据由
        /// <see cref="ShowcaseDirector"/> 只读逻辑事件驱动。关着（缺省）时舞台与原来逐位一致。
        /// </summary>
        public bool Showcase { get; set; }

        /// <summary>
        /// 玩家闪白强度来源（ADR-0148，<c>feel.intensity.flash</c>，0..1，只在 <see cref="Interactive"/> 下生效；缺省 <c>null</c> = 不缩放，与此前逐位一致）：
        /// 与精灵管线的 <c>FlashReceiver.IntensityScale</c> 同一条规则——系数 0 即关闭闪白（光敏类无障碍），这次闪白不落地、不计入
        /// <see cref="EngineLabStage.FlashesApplied"/>；系数 s 时闪白强度 = 默认强度 × s。精灵与模型两种外形都按它缩放。
        /// </summary>
        public Func<double>? FlashIntensityScale { get; set; }

        /// <summary>
        /// 人手试玩模式（缺省 false，关着时舞台与此前逐位一致）：舞台相机真正渲染到屏幕并跟随玩家、带音频监听器与地面网格、
        /// 震屏与闪白落到引擎（缺省它们是空实现）；不再测 GPU 帧耗时。逻辑不受影响（表现不回流逻辑）。
        /// </summary>
        public bool Interactive { get; set; }

        /// <summary>
        /// 鼠标环绕镜头（ADR-0159，只在 <see cref="Interactive"/> 且舞台相机带俯仰——格子相机模式 <c>fixed_pitch</c> 且 <see cref="HonorCellCameraMode"/> 打开——时生效；缺省 false，
        /// 舞台与此前逐位一致）：舞台装一个 <see cref="OrbitCameraController"/>，相机的偏航/俯仰/缩放由它驱动，相机的朝向查询改为"固定步边界提交"的采样方式
        /// （<c>UnityCamera.SampleYawAtCommit</c>）。移动动作是否按镜头偏航换算由 <see cref="ControlSpaceOverride"/>（<c>camera_relative</c>）声明，两者独立。
        /// </summary>
        public bool OrbitCamera { get; set; }

        /// <summary>环绕镜头的灵敏度与区间；<c>null</c> 取 <see cref="OrbitCameraOptions"/> 缺省。</summary>
        public OrbitCameraOptions? OrbitOptions { get; set; }

        /// <summary>环绕镜头开局是否打开（缺省 true；关 = 固定镜头，面板可以切换）。</summary>
        public bool OrbitCameraStartsEnabled { get; set; } = true;

        /// <summary>试玩模式的镜头缩放（正交半高 / 透视下的取景半高，世界单位；缺省 2.8：占位精灵里的人只有画幅三分之一高，取景要近才看得清）。</summary>
        public double InteractiveZoom { get; set; } = 2.8;

        /// <summary>试玩模式的相机跟随平滑时间常数（秒，缺省 0.12；0 = 硬跟随）。</summary>
        public double InteractiveFollowSmoothing { get; set; } = 0.12;

        /// <summary>呈现通道开关（震屏/闪白/音效/镜头冲击）；<c>null</c> = 全部放行且不计数。试玩宿主传入同一个对象，面板读写它。</summary>
        public LabEffectFilter? Effects { get; set; }
    }
}
