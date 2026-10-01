using Core.Foundation.Common;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// 行动者动作时钟的只读查询（手感设计/00 第 5 节、03 第 3 节）：每个行动者一个，随模拟 tick 前进，
    /// 被局部顿帧暂停。动作时间线推进、取消/连招/无敌窗口、缓冲过期都以它计时；冷却、光环、寻路不受影响。
    /// 实现由动作/顿帧切片提供，本接口只定义契约。
    /// </summary>
    public interface IActorActionClockQuery
    {
        /// <summary>行动者的动作时钟是否被暂停（顿帧中）。</summary>
        bool IsPaused(Id actorId);

        /// <summary>剩余暂停 tick 数（未暂停为 0）。</summary>
        int RemainingPausedTicks(Id actorId);

        /// <summary>行动者动作时钟累计 tick 数：每个未暂停的模拟 tick +1，暂停期间不变。</summary>
        long ActionTicks(Id actorId);

        /// <summary>该行动者当前未释放的暂停句柄数（测试断言"顿帧结束后所有冻结句柄为零"用）。</summary>
        int PauseHandleCount(Id actorId);

        /// <summary>全部行动者未释放的暂停句柄总数。</summary>
        int TotalPauseHandleCount { get; }
    }

    /// <summary>
    /// 行动者动作时钟的写入侧（局部顿帧施加与释放，手感设计/03 第 3 节）。
    /// <para>
    /// 语义约定（实现必须满足）：<see cref="Pause"/> 嵌套时取剩余时长与新时长的较大者，不相加；
    /// 到期自动解除；<see cref="ReleaseAll(Id)"/> 无条件解除并清零（死亡、销毁、场景卸载、离散模式切换时调用）。
    /// 顿帧时长不随动作速率缩放。
    /// </para>
    /// </summary>
    public interface IActorActionClockControl : IActorActionClockQuery
    {
        /// <summary>暂停行动者动作时钟 <paramref name="ticks"/> 个 tick（嵌套取大）；<paramref name="ticks"/> 必须为正。</summary>
        void Pause(Id actorId, int ticks);

        /// <summary>无条件解除该行动者的全部暂停并清零句柄。</summary>
        void ReleaseAll(Id actorId);

        /// <summary>无条件解除全部行动者的暂停（场景卸载、模式切换）。</summary>
        void ReleaseAll();
    }
}
