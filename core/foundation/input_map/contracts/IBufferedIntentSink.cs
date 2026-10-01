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
    }
}
