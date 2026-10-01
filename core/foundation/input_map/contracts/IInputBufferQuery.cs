using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 输入缓冲只读查询（手感设计/01 第 2.3 节末）：实验室时间轴、测试断言与表现层经它观察每条记录从入槽到消费/丢弃的
    /// 状态变化；实现由输入缓冲模块提供（后续切片），本接口只定义契约。
    /// </summary>
    public interface IInputBufferQuery
    {
        /// <summary>行动者当前缓冲槽内的全部记录快照（按入槽顺序；无缓冲返回空列表）。</summary>
        IReadOnlyList<BufferedIntent> Snapshot(Id actorId);
    }
}
