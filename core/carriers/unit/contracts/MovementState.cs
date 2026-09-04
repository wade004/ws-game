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

        public MovementState(IReadOnlyList<Vec2>? currentPath, MoveMode mode, bool movementLocked, int pathIndex)
        {
            CurrentPath = currentPath;
            Mode = mode;
            MovementLocked = movementLocked;
            PathIndex = pathIndex;
        }

        /// <summary>未在移动、未被锁定的默认状态。</summary>
        public static readonly MovementState Idle = new MovementState(null, MoveMode.Idle, false, 0);

        /// <summary>返回一份仅 <see cref="MovementLocked"/> 不同的新实例，供控制效果施加/解除时
        /// 调用方便捷更新（其余字段照抄本实例，不受锁定状态影响，见 05 第 6.2 节"移动系统只读这些
        /// 派生状态"——锁定只影响是否推进,不清空既有路径）。</summary>
        public MovementState WithLocked(bool movementLocked) =>
            new MovementState(CurrentPath, Mode, movementLocked, PathIndex);
    }
}
