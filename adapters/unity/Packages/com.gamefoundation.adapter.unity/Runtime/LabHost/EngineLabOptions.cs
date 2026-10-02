#nullable enable
// EngineLabOptions：实验室引擎宿主的运行选项（手感设计/06 第 4 节）。全部有缺省值；不声明任何选项等于"只驱动引擎侧表现、不改逻辑输入"。
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
        /// <c>camera_relative</c> 时设备轴值按真实舞台相机的右/上轴转成世界方向再注入桩摇杆（转换是宿主层行为，见 <see cref="ControlSpace"/>）。
        /// </summary>
        public string? ControlSpaceOverride { get; set; }

        /// <summary>舞台相机的偏航角（度；绕视线轴逆时针）。缺省 0 时相机相对转换是恒等变换，逻辑与世界控制空间逐字节一致。</summary>
        public double CameraYawDegrees { get; set; }

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
    }
}
