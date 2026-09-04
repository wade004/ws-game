using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 仇恨表契约（见 06 第 4.4/7 节 <c>ThreatTable</c>）。单机场景唯一用途是驱动 AI 选目标（06 第
    /// 4.4 节拍板范围声明），本接口按单位维度取得/操作各自的仇恨表，具体存储归属由
    /// <see cref="ICombatHost.GetThreatTable"/> 的实现决定。
    /// </summary>
    public interface IThreatTable
    {
        void AddThreat(Id unitId, Id sourceId, double amount);

        Id? GetTopThreat(Id unitId);

        void Clear(Id unitId);

        /// <summary>补充：读取某个仇恨来源当前的仇恨值；未记录返回 0。</summary>
        double GetThreat(Id unitId, Id sourceId);

        /// <summary>
        /// 补充：直接设置某个来源的仇恨值（见 06 第 4.4 节"专门的嘲讽类效果...可直接设置/强制置顶
        /// 仇恨值"）。可用于嘲讽类效果把某来源的仇恨值设为当前最高值 + 1 一类"强制置顶"实现。
        /// </summary>
        void SetThreat(Id unitId, Id sourceId, double amount);

        /// <summary>补充：只读列出该单位仇恨表当前的全部条目（来源、仇恨值），供调试与测试断言。
        /// 顺序不保证代表排名，取最高仇恨请用 <see cref="GetTopThreat"/>。</summary>
        IReadOnlyList<(Id source, double amount)> GetAll(Id unitId);
    }
}
