using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 输入缓冲只读查询与取用入口（手感设计/01 第 2.3 节末、ADR-0115）：实验室时间轴、测试断言与表现层经
    /// <see cref="Snapshot"/> 观察每条记录从入槽到消费/丢弃的状态变化；动作层（动作时间线、取消窗口、连招）经
    /// <see cref="TryPeek"/>/<see cref="TryConsume"/>/<see cref="ReportRejected"/> 取用意图。实现为
    /// <see cref="InputBufferHost"/>。
    /// <para>
    /// 判断记录（取用成员为什么是默认接口成员，而不是另起接口）：<see cref="Snapshot"/> 是 S0 已定的契约，ABI 只加不改；新增的四个
    /// 取用成员以 C# 8 默认接口成员追加（默认实现 = "没有缓冲"：查不到、取不到、什么都不做），既有的只实现 <see cref="Snapshot"/>
    /// 的替身与消费方无需改动即可编译与运行。动作层只依赖这一个接口，不需要另持有写入侧引用。
    /// </para>
    /// <para>
    /// 判断记录（取用语义，对应 01 第 2.3 节）：候选 = 未消费、非 <see cref="BufferHoldState.HoldPending"/>、未过期的记录，按优先级降序、
    /// <see cref="BufferedIntent.SubmittedTick"/> 升序排序，<b>只看排在最前的那一条</b>（设计文本"取优先级最高、submittedTick 最早的
    /// 可消费记录，检查接受条件"）：它不被接受时本 tick 不让位给次优先级记录——否则高优先级的闪避会被低优先级的攻击绕过而在
    /// 取消窗口打开前被"旁路消费"。行动者动作时钟处于顿帧暂停时恒取不到；同一 tick 内每个行动者至多取用一条（01 第 2.3 节第 5 点）。
    /// </para>
    /// </summary>
    public interface IInputBufferQuery
    {
        /// <summary>行动者当前缓冲槽内的全部记录快照（按入槽顺序；无缓冲返回空列表）。已消费的记录保留到下一 tick 开头才清除。</summary>
        IReadOnlyList<BufferedIntent> Snapshot(Id actorId);

        /// <summary>
        /// 只看不取：当前可被取用的最前一条记录（见接口注释的取用语义）。无候选、行动者动作时钟暂停、本 tick 已取用过都返回 false。
        /// 默认实现恒为 false。
        /// </summary>
        bool TryPeek(Id actorId, out BufferedIntent intent)
        {
            intent = default;
            return false;
        }

        /// <summary>
        /// 取用：把排在最前的候选记录标记为已消费并返回它的快照（<see cref="BufferedIntent.Consumed"/> 为 true）。
        /// <paramref name="accepts"/> 非空时先以它检查接受条件（例如"该类别的取消窗口此刻是否打开"），返回 false 则不取用、
        /// 记录保持原样（不让位给次优先级记录）。取用后同一 tick 内再次调用恒返回 false。默认实现恒为 false。
        /// </summary>
        bool TryConsume(Id actorId, Func<BufferedIntent, bool>? accepts, out BufferedIntent consumed)
        {
            consumed = default;
            return false;
        }

        /// <summary>
        /// 报告施法管线拒绝了刚取用的记录（01 第 2.3 节第 4 点）：<paramref name="timeSolvable"/> 为 true（时间可解原因：
        /// <c>ACTION_LOCKED</c>、<c>GCD_ACTIVE</c>、剩余冷却不超过记录剩余缓冲的 <c>ON_COOLDOWN</c>）时撤销已消费标记，
        /// 记录保留到过期、下一 tick 重试；否则记录丢弃并发 <see cref="InputBufferDroppedEvent"/>（<see cref="BufferDropReason.Rejected"/>
        /// 与 <paramref name="reasonCode"/>）。找不到对应的已消费记录时什么都不做。默认实现什么都不做。
        /// </summary>
        void ReportRejected(Id actorId, Id actionId, string reasonCode, bool timeSolvable)
        {
        }

        /// <summary>
        /// 蓄力达到上限（<c>charge_ready</c> 标记）：把该动作处于 <see cref="BufferHoldState.HoldPending"/> 的记录自动转为
        /// <see cref="BufferHoldState.HoldReleased"/>（<see cref="BufferedIntent.HeldTicks"/> = <paramref name="heldTicks"/>）并开始计缓冲窗口，
        /// 之后即可被取用（手感设计/01 第 3.3 节）。返回是否找到并转换了记录。默认实现恒为 false。
        /// </summary>
        bool CompleteHold(Id actorId, Id actionId, int heldTicks) => false;

        /// <summary>
        /// 同 <see cref="TryPeek(Id, out BufferedIntent)"/>，但先把 <paramref name="skip"/> 判为真的记录从候选里剔除（手感落地 M4 清扫）：
        /// 用来跳过"永远接不了"的记录（动作没有映射到任何技能），使它们不在过期前挡住次优先级候选；<paramref name="skip"/> 为 null 等价于
        /// 无参版本。默认实现忽略 <paramref name="skip"/>，退回无参版本。
        /// </summary>
        bool TryPeek(Id actorId, Func<BufferedIntent, bool>? skip, out BufferedIntent intent) => TryPeek(actorId, out intent);

        /// <summary>
        /// 同 <see cref="TryConsume(Id, Func{BufferedIntent, bool}, out BufferedIntent)"/>，但候选先剔除 <paramref name="skip"/> 判为真的记录
        /// （见 <see cref="TryPeek(Id, Func{BufferedIntent, bool}, out BufferedIntent)"/>）；<paramref name="accepts"/> 只检查剔除后排在最前的
        /// 那一条。默认实现忽略 <paramref name="skip"/>，退回三参版本。
        /// </summary>
        bool TryConsume(Id actorId, Func<BufferedIntent, bool>? accepts, Func<BufferedIntent, bool>? skip, out BufferedIntent consumed) =>
            TryConsume(actorId, accepts, out consumed);

        /// <summary>
        /// 该行动者的该输入动作此刻是否仍被按住（按下边沿已入缓冲处理、抬起边沿尚未处理；ADR-0143）。按住维持型动作（格挡、按住冲刺、持续引导）的
        /// 动作层据此决定"继续维持还是退出"；与记录是否还在槽内无关（记录被消费、过期、替换后，按键仍可能按着）。默认实现恒为 false（没有缓冲）。
        /// </summary>
        bool IsHeld(Id actorId, Id actionId) => false;
    }
}
