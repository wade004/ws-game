using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 输入缓冲只读查询与取用入口（手感设计/01 第 2.3 节末）：实验室时间轴、测试断言与表现层经 <see cref="Snapshot"/> 观察每条记录
    /// 从入槽到消费/丢弃的状态变化；动作层（动作时间线的取消窗口、连招）经 <see cref="TryConsume"/>/<see cref="ReportRejected"/> 取用意图。
    /// 实现由输入缓冲模块提供。
    /// <para>
    /// 判断记录（取用成员为什么是默认接口成员）：<see cref="Snapshot"/> 是 S0 已定的契约，ABI 只加不改；两个取用成员以 C# 8 默认接口成员
    /// 追加（默认实现 = "没有缓冲"：取不到、什么都不做），既有的只实现 <see cref="Snapshot"/> 的替身与消费方无需改动即可编译与运行。
    /// 签名与输入缓冲切片（S1）同名同形，合并时以输入缓冲切片的定义为准。
    /// </para>
    /// </summary>
    public interface IInputBufferQuery
    {
        /// <summary>行动者当前缓冲槽内的全部记录快照（按入槽顺序；无缓冲返回空列表）。</summary>
        IReadOnlyList<BufferedIntent> Snapshot(Id actorId);

        /// <summary>
        /// 取用：把排在最前的候选记录（未消费、非按住待定、未过期，按优先级降序、提交 tick 升序）标记为已消费并返回它的快照。
        /// <paramref name="accepts"/> 非空时先以它检查接受条件（例如"该类别的取消窗口此刻是否打开"），返回 false 则不取用、
        /// 记录保持原样（不让位给次优先级记录）。默认实现恒为 false。
        /// </summary>
        bool TryConsume(Id actorId, Func<BufferedIntent, bool>? accepts, out BufferedIntent consumed)
        {
            consumed = default;
            return false;
        }

        /// <summary>
        /// 报告施法管线拒绝了刚取用的记录（01 第 2.3 节第 4 点）：<paramref name="timeSolvable"/> 为 true（时间可解原因）时撤销已消费标记，
        /// 记录保留到过期、下一 tick 重试；否则记录丢弃并发 <see cref="InputBufferDroppedEvent"/>（<see cref="BufferDropReason.Rejected"/>
        /// 与 <paramref name="reasonCode"/>）。默认实现什么都不做。
        /// </summary>
        void ReportRejected(Id actorId, Id actionId, string reasonCode, bool timeSolvable)
        {
        }
    }
}
