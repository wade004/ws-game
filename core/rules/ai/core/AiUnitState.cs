using Core.Foundation.Common;
using Core.Rules.Common;

namespace Core.Rules.Ai
{
    /// <summary>
    /// 每个已注册单位的行为外壳运行期状态（见任务书拍板字段列表）。可变引用类型（不是只读值
    /// 类型）：<see cref="AiHost"/> 在状态机推进过程中原地修改这些字段，不整体替换实例。
    /// 本类型只在本模块内部使用，不对外暴露——外部只能经 <see cref="IAiHost.GetBehaviorState"/>
    /// 等只读查询方法观察状态机的当前状态，不能直接持有/修改本类型实例（见 01 第 6 节禁止事项第 4 条
    /// "禁止同层模块互相持有对方内部状态"）。
    /// </summary>
    internal sealed class AiUnitState
    {
        public BehaviorState State;

        public Id ProfileId;

        public Id RotationId;

        public Id? Target;

        public Vec2 SpawnPoint;

        public int PatrolIndex;

        /// <summary>pingpong 模式下的行进方向：+1 或 -1；loop 模式下恒为 +1，不使用。</summary>
        public int PatrolDir;

        /// <summary>combat 态下距上次 <see cref="IAiHost.Evaluate"/> 求值累计的时间，
        /// 达到 <see cref="AiBehaviorProfile.DecisionInterval"/> 才再次求值一次。</summary>
        public double DecisionAccumulator;
    }
}
