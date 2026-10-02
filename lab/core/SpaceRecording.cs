using System;
using System.Collections.Generic;

namespace Lab
{
    /// <summary>
    /// 空间语义的运行期记录（手感设计/06 第 10 节勘误 9：<c>plane</c>/<c>side_2d</c>/<c>volume</c> 三个空间取值在无头宿主上的真实语义）。
    /// 只有"这次运行与空间语义有关"时才有（格子带竖直轴、脚本含跳跃事件，或靶子声明了出生高度），否则 <see cref="LabRecording.Space"/> 为
    /// null，度量组 <c>space</c> 不出现，既有脚本的指纹与基线逐字不变。
    /// <para>
    /// 采样口径：每个宿主固定步末尾（<c>Advance</c> 返回后）记一份玩家与各靶子的脚下高度。注意核心层 tick 内的阶段顺序——
    /// 技能管线（命中判定）早于移动与导航（竖直积分），所以 tick T 里的命中看到的是 <b>tick T-1 末尾</b>的高度。
    /// </para>
    /// </summary>
    public sealed class SpaceRecording
    {
        /// <summary>本次运行实际采用的空间模型（<c>plane</c>/<c>side_2d</c>/<c>volume</c>；变体 <see cref="LabRunVariant.SpaceOverride"/> 可覆盖格子声明）。</summary>
        public string Model { get; }

        /// <summary>世界是否装配了竖直轴（重力、跳跃、击飞、命中高度窗口）。</summary>
        public bool VerticalAxis { get; }

        public double Gravity { get; }

        public double JumpHeight { get; }

        /// <summary>深度轴是否被控制空间锁死（横版二维：输入的竖直分量不是深度）。</summary>
        public bool DepthLocked { get; }

        /// <summary>脚本里的跳跃请求数（含被拒绝的）。</summary>
        public int JumpRequests { get; set; }

        /// <summary>被竖直运动服务接受、真正起跳的次数。</summary>
        public int JumpsStarted => JumpStartTicks.Count;

        /// <summary>起跳的宿主固定步序号（请求被接受的那一步；该步 <c>Advance</c> 就积分第一步）。</summary>
        public List<int> JumpStartTicks { get; } = new List<int>();

        /// <summary>被"丢掉竖直分量"的轴事件数（横版二维深度锁）。</summary>
        public int DepthInputsDropped { get; set; }

        /// <summary>靶子出生时声明的高度（出场标签 → 声明值），按出场顺序；只记录声明大于 0 的。</summary>
        public List<KeyValuePair<string, double>> DeclaredDummyHeights { get; } = new List<KeyValuePair<string, double>>();

        /// <summary>每个宿主固定步末尾玩家的脚下高度。</summary>
        public List<double> PlayerHeights { get; } = new List<double>();

        /// <summary>每个宿主固定步末尾玩家的世界 y（深度）。</summary>
        public List<double> PlayerDepths { get; } = new List<double>();

        /// <summary>每个靶子的逐步脚下高度（出场标签 → 序列）。</summary>
        public SortedDictionary<string, List<double>> DummyHeights { get; } = new SortedDictionary<string, List<double>>(StringComparer.Ordinal);

        public SpaceRecording(string model, bool verticalAxis, double gravity, double jumpHeight, bool depthLocked)
        {
            Model = model;
            VerticalAxis = verticalAxis;
            Gravity = gravity;
            JumpHeight = jumpHeight;
            DepthLocked = depthLocked;
        }
    }
}
