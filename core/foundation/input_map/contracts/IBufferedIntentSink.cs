using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 动作层对缓冲记录的接受判定与意图生成（手感设计/01 第 2.3 节第 3 点）：由 <see cref="InputBufferTickHandler"/> 在 tick 步骤 1
    /// 对每个有候选记录的行动者调用一次。输入缓冲在 L0，不知道行动者有没有进行中的动作、取消窗口是否打开、技能映射——这些是规则层
    /// 的事，经本接口倒置注入（动作时间线切片的取消窗口逻辑、或游戏装配层的"动作 → 技能"映射实现它）。
    /// </summary>
    public interface IBufferedIntentSink
    {
        /// <summary>
        /// 记录 <paramref name="record"/>（该行动者最前的候选）此刻能否被接受：不能返回 false（记录保留，下一 tick 再问）；
        /// 能则返回 true 并给出要提交的意图（通常是 <c>cast</c>），调用方随后把记录标记为已消费并把意图追加进本 tick 的意图列表。
        /// </summary>
        bool TryAccept(Id actorId, BufferedIntent record, out Intent intent);

        /// <summary>
        /// 本出口是否可能接受这条记录（手感落地 M4 清扫）。与 <see cref="TryAccept"/> 的"此刻能否接受"不同：只有<b>永远接不了</b>
        /// （例如输入动作没有映射到任何技能）才返回 false，处理器据此跳过它、改问下一条候选，使一条永远接不了的记录不会在过期前
        /// 挡住优先级更低的候选；"此刻不能接受"（动作锁、硬直、冷却）仍然返回 true 并由 <see cref="TryAccept"/> 返回 false，
        /// 次优先级记录照旧不让位（高优先级的闪避不被低优先级攻击旁路）。默认接口成员，缺省恒为 true（既有实现原样工作）。
        /// </summary>
        bool CanHandle(Id actorId, BufferedIntent record) => true;

        /// <summary>
        /// <see cref="TryAccept"/> 返回 true 时，这条记录是否产生一条要追加进意图列表的意图（ADR-0143）。缺省 true（既有实现原样工作）；
        /// 返回 false 表示本出口在 <see cref="TryAccept"/> 里已经直接完成了动作（例如 <see cref="ActionClass.Jump"/> 类记录对竖直轴能力包的起跳请求，
        /// 没有施法意图），处理器只把记录标记为已消费，不追加意图（此时 <c>TryAccept</c> 的 <c>intent</c> 输出被忽略）。
        /// </summary>
        bool ProducesIntent(Id actorId, BufferedIntent record) => true;
    }
}
