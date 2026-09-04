using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 目标选择模块对外契约（见 06 第 5/7 节 <c>TargetHost.resolve(chainId, casterId) -&gt; List&lt;UnitRef&gt;</c>）。
    /// 由 <c>core/rules/targeting</c> 实现，供 skill（施法管线步骤 6）与 ai（Rotation 条件里选定
    /// <c>target</c> 分组的求值对象）调用。
    /// </summary>
    public interface ITargetHost
    {
        /// <summary>按 <paramref name="chainId"/> 指向的 <c>target.chain_def</c> 解析出候选目标列表
        /// （见 06 第 5 节，顺序即排序结果）。</summary>
        IReadOnlyList<Id> Resolve(Id chainId, Id casterId);

        /// <summary>
        /// 补充：带"当前目标"上下文的解析重载，供链的 <c>source = current_target</c>（见 06 第 5 节
        /// <c>TargetChainDef.source</c> 枚举示例）在无法从调用方状态隐式取得当前目标时显式传入；
        /// <paramref name="currentTarget"/> 为 null 时行为与 <see cref="Resolve(Id, Id)"/> 等价。
        /// </summary>
        IReadOnlyList<Id> Resolve(Id chainId, Id casterId, Id? currentTarget);
    }
}
