using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>当前移动模式（见 05 第 6.2 节 <c>MovementState.moveMode</c>
    /// <c>idle｜walk｜run｜forced</c>）。</summary>
    public enum MoveMode
    {
        Idle,
        Walk,
        Run,

        /// <summary>被击退/传送等非自主位移强制置入的模式（见 05 第 6.2 节 <c>forced</c>）。</summary>
        Forced,
    }

    /// <summary>
    /// 挂在 <see cref="Unit"/> 上的移动状态（见 05 第 6.2 节 <c>MovementState</c> 字段表）。不可变值
    /// 类型：每次状态变化由 <c>MovementTickHandler</c> 构造一份新实例整体替换
    /// <see cref="Unit.MovementState"/>，避免"部分字段更新到一半"的中间态泄漏。
    /// </summary>
    public readonly struct MovementState
    {
        /// <summary>当前寻路结果；无正在进行的路径跟随时为 null（见 05 第 6.2 节
        /// <c>currentPath</c>）。</summary>
        public IReadOnlyList<Vec2>? CurrentPath { get; }

        public MoveMode Mode { get; }

        /// <summary>是否被控制效果禁止移动（见 05 第 6.2 节 <c>movementLocked</c>）。</summary>
        public bool MovementLocked { get; }

        /// <summary><see cref="CurrentPath"/> 中下一个尚未到达的路点索引（05 原文未列出该字段，
        /// 是本模块补充：路径跟随需要记住"走到哪了"才能跨多个 tick 继续推进，否则每 tick 都要
        /// 从起点重新出发）。</summary>
        public int PathIndex { get; }

        /// <summary>
        /// 游戏侧通用能力需求（05 第 6 节勘误）：建立 <see cref="CurrentPath"/> 时记录的
        /// <see cref="Core.Foundation.EngineAdapter.INavigation2D.GetBlockingVersion"/> 快照，供
        /// <c>MovementTickHandler</c> 在每次推进前比较"该地图的动态阻挡是否在建路之后又变化过"——
        /// 相等（含都为 0，即未装配导航或导航实现不支持版本追踪）视为"无需重验"，不同则按
        /// <see cref="MovementOptions.BlockingChangePolicy"/> 处理。默认 0（与
        /// <see cref="Core.Foundation.EngineAdapter.INavigation2D.GetBlockingVersion"/> 默认实现的
        /// "不支持版本追踪"取值一致，不建立路径的状态——如 <see cref="MoveMode.Idle"/>/方向移动——
        /// 这个字段没有意义，取默认值即可）。
        /// </summary>
        public int NavVersion { get; }

        /// <summary>
        /// ADR-0026《技能位移的连续模式》：非 null 表示该单位当前正处于一次"受控位移"中（见
        /// <see cref="MovementHost.BeginControlledDisplacement"/>）。与 <see cref="CurrentPath"/>
        /// 互斥——两套机制分别由 <c>move</c>/<c>move_displace</c> 两种不同 <c>Intent.Kind</c> 驱动，
        /// 同一单位同一时刻至多处于其中一种（受控位移期间普通移动意图被拒绝，见
        /// <c>MovementTickHandler.ApplyIntent</c> 判断记录）。</summary>
        public ControlledDisplacementState? Displacement { get; }

        /// <summary>ADR-0026 决策 3"MovementState 暴露受控位移进行中标志"——新增只读属性，等价于
        /// <c>Displacement.HasValue</c>，供调用方不必自行判空 <see cref="Displacement"/>。</summary>
        public bool IsControlledDisplacementActive => Displacement.HasValue;

        public MovementState(
            IReadOnlyList<Vec2>? currentPath, MoveMode mode, bool movementLocked, int pathIndex, int navVersion = 0)
            : this(currentPath, mode, movementLocked, pathIndex, navVersion, null)
        {
        }

        /// <summary>ADR-0026 新增重载（不改动上面既有 5 参数构造函数的物理签名，见
        /// <c>architecture/adr/0026-*.md</c> 判断记录"ABI 安全：新增构造函数重载而非新增参数"）：
        /// 额外携带 <paramref name="displacement"/>。</summary>
        public MovementState(
            IReadOnlyList<Vec2>? currentPath, MoveMode mode, bool movementLocked, int pathIndex, int navVersion,
            ControlledDisplacementState? displacement)
        {
            CurrentPath = currentPath;
            Mode = mode;
            MovementLocked = movementLocked;
            PathIndex = pathIndex;
            NavVersion = navVersion;
            Displacement = displacement;
        }

        /// <summary>未在移动、未被锁定的默认状态。</summary>
        public static readonly MovementState Idle = new MovementState(null, MoveMode.Idle, false, 0);

        /// <summary>返回一份仅 <see cref="MovementLocked"/> 不同的新实例，供控制效果施加/解除时
        /// 调用方便捷更新（其余字段照抄本实例，不受锁定状态影响，见 05 第 6.2 节"移动系统只读这些
        /// 派生状态"——锁定只影响是否推进,不清空既有路径）。<see cref="Displacement"/> 同样原样带过
        /// （ADR-0026：锁定/解锁控制效果不应该悄悄打断或凭空产生一次受控位移，位移的开始/结束只由
        /// <c>MovementTickHandler</c> 显式管理）。</summary>
        public MovementState WithLocked(bool movementLocked) =>
            new MovementState(CurrentPath, Mode, movementLocked, PathIndex, NavVersion, Displacement);
    }
}
