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

        /// <summary>
        /// 竖直轴能力包补完的扩展记录（脚本声明了 <see cref="ScriptSpaceOptions"/> 且格子带竖直轴时才有；否则为 null，度量组 <c>space_ext</c>
        /// 不出现）。
        /// </summary>
        public SpaceExtRecording? Ext { get; set; }

        public SpaceRecording(string model, bool verticalAxis, double gravity, double jumpHeight, bool depthLocked)
        {
            Model = model;
            VerticalAxis = verticalAxis;
            Gravity = gravity;
            JumpHeight = jumpHeight;
            DepthLocked = depthLocked;
        }
    }

    /// <summary>一次空中姿势请求的解析记录（脚本声明了合成姿势键表才有）。</summary>
    public sealed class AirPoseRecord
    {
        public int Tick { get; }

        /// <summary>请求来源：<c>phase</c>（玩家跳跃阶段）、<c>hit</c>（目标受击）、<c>attack</c>（玩家出手）。</summary>
        public string Kind { get; }

        public string Entity { get; }

        /// <summary>最具体的请求键（链头）。</summary>
        public string Requested { get; }

        /// <summary>实际取到的键；链上无一命中为空串。</summary>
        public string Resolved { get; }

        /// <summary>回落深度（0 = 链头命中）。</summary>
        public int Depth { get; }

        public AirPoseRecord(int tick, string kind, string entity, string requested, string resolved, int depth)
        {
            Tick = tick;
            Kind = kind;
            Entity = entity;
            Requested = requested;
            Resolved = resolved;
            Depth = depth;
        }
    }

    /// <summary>
    /// 空间语义扩展记录（ADR-0130 追加决定：空中控制/多段跳、地形、空中姿势）。采样口径同 <see cref="SpaceRecording"/>：每个宿主固定步末尾一份。
    /// </summary>
    public sealed class SpaceExtRecording
    {
        public ScriptSpaceOptions Options { get; }

        /// <summary>每个固定步末尾玩家是否腾空（竖直运动服务口径，不是"高度大于 0"）。</summary>
        public List<bool> PlayerAirborne { get; } = new List<bool>();

        /// <summary>每个固定步末尾玩家的竖直速度。</summary>
        public List<double> PlayerVerticalSpeeds { get; } = new List<double>();

        /// <summary>每个固定步玩家是否提交了移动请求。</summary>
        public List<bool> PlayerMoveRequested { get; } = new List<bool>();

        /// <summary>每个固定步末尾玩家的空中阶段（<c>rise</c>/<c>fall</c>/<c>land</c>；没有为空串；没装姿势装置时全空）。</summary>
        public List<string> PlayerAirPhases { get; } = new List<string>();

        /// <summary>被接受的跳跃里请求发生时玩家已腾空的次数（空中跳跃）。</summary>
        public int AirJumpStarts { get; set; }

        /// <summary>请求发生时玩家已腾空、被拒绝的跳跃请求数。</summary>
        public int AirJumpRefusals { get; set; }

        /// <summary>空中姿势解析记录（按发生顺序）。</summary>
        public List<AirPoseRecord> AirPoses { get; } = new List<AirPoseRecord>();

        /// <summary>
        /// 寻路与地形热切换的运行期记录（脚本含 <c>move_to</c>/<c>terrain_swap</c> 事件时才有；否则为 null，度量组 <c>space_nav</c> 不出现，
        /// 既有空间扩展脚本的指纹逐字不变）。
        /// </summary>
        public SpaceNavRecording? Nav { get; set; }

        public SpaceExtRecording(ScriptSpaceOptions options)
        {
            Options = options;
        }
    }

    /// <summary>点击移动与地形热切换的运行期事实（M4-W1a）：移动请求、失败（<c>MoveFailedDetailed</c>）、停止原因（<c>MoveStopped</c>）、地形切换次数。</summary>
    public sealed class SpaceNavRecording
    {
        /// <summary>脚本里的 <c>move_to</c> 请求目标（按发生顺序）。</summary>
        public List<Core.Foundation.Common.Vec2> Targets { get; } = new List<Core.Foundation.Common.Vec2>();

        /// <summary>每个目标请求发生的宿主固定步序号。</summary>
        public List<int> TargetTicks { get; } = new List<int>();

        /// <summary>移动失败（寻路失败等）：tick:原因。</summary>
        public List<string> Failures { get; } = new List<string>();

        /// <summary>移动停止：tick:原因。</summary>
        public List<string> Stops { get; } = new List<string>();

        /// <summary>地形热切换的次数。</summary>
        public int TerrainSwaps { get; set; }
    }
}
