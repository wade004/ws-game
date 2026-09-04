using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Numbers.Faction
{
    /// <summary>
    /// 阵营矩阵契约（见 01_分层与依赖.md L1 模块表 <c>faction</c> 行"契约接口名：
    /// FactionMatrix"，本模块按仓库既有惯例把契约命名为 <c>IFactionMatrix</c>、默认实现命名为
    /// <see cref="FactionMatrix"/>）：查询/覆盖两个阵营之间的反应。矩阵不要求对称——
    /// <c>GetReaction(a,b)</c> 与 <c>GetReaction(b,a)</c> 可以不同（任务书原文）。
    /// </summary>
    public interface IFactionMatrix
    {
        /// <summary>
        /// <paramref name="from"/> 对 <paramref name="to"/> 的反应，按以下优先级解析：
        /// (1) <paramref name="from"/> == <paramref name="to"/> → 恒为
        /// <see cref="Reaction.Friendly"/>（同阵营）；(2) <see cref="SetReaction"/> 写入的运行期
        /// 覆盖；(3) <c>fac.reaction_matrix</c> 显式登记的行；(4) <paramref name="from"/> 的
        /// <c>default_reaction</c>。<paramref name="from"/>/<paramref name="to"/> 任一是未登记
        /// 的阵营时抛 <see cref="System.ArgumentException"/>。
        /// </summary>
        Reaction GetReaction(Id from, Id to);

        /// <summary>
        /// 运行期覆盖 <c>(from, to)</c> 这一方向的反应（不影响 <c>(to, from)</c>）。改动前后的值
        /// 不同才发 <see cref="FactionRelationChangedEvent"/>。<paramref name="from"/>/
        /// <paramref name="to"/> 任一是未登记的阵营时抛 <see cref="System.ArgumentException"/>。
        /// </summary>
        void SetReaction(Id from, Id to, Reaction reaction);

        /// <summary>清空全部 <see cref="SetReaction"/> 写入的运行期覆盖，恢复到数据加载时的
        /// 状态（显式登记行 + 默认反应回退）。不发任何事件。</summary>
        void ResetOverrides();

        /// <summary>便捷方法：<c>GetReaction(a, b) == Reaction.Hostile</c>。</summary>
        bool IsHostile(Id a, Id b);

        /// <summary>全部已登记阵营，顺序为 <c>fac.faction</c> 数据行的加载顺序。</summary>
        IReadOnlyList<Id> Factions { get; }
    }
}
