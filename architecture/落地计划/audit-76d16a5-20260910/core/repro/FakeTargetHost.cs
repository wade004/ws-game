using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Tests.Rules.Skill
{
    /// <summary><see cref="ITargetHost"/> 的测试假实现：手工登记每条链的解析结果。</summary>
    internal sealed class FakeTargetHost : ITargetHost
    {
        private readonly Dictionary<Id, IReadOnlyList<Id>> _chains = new Dictionary<Id, IReadOnlyList<Id>>();

        /// <summary>N10 补充：手工登记每条链的"额外目标条件"简化模拟——真实 <c>TargetHost</c> 按
        /// <c>TargetChainDef.Filters</c>（tag/expr 文本）逐条求值，本假实现改用一个委托直接表达
        /// "候选是否通过"，测试按需配置（如"要求 undead 标签"），不需要搭建完整的 Expr/数据登记
        /// 环境。</summary>
        private readonly Dictionary<Id, Func<Id, bool>> _filters = new Dictionary<Id, Func<Id, bool>>();

        public FakeTargetHost SetChain(Id chainId, params Id[] targets)
        {
            _chains[chainId] = targets;
            return this;
        }

        /// <summary>N10 补充：为 <paramref name="chainId"/> 配置一个"额外目标条件"谓词，供
        /// <see cref="FilterExplicitTargets"/> 校验显式给定的目标；未配置的链视为无额外条件
        /// （全部通过），与 <c>TargetChainDef.Filters</c> 为空数组时的真实语义一致。</summary>
        public FakeTargetHost SetFilter(Id chainId, Func<Id, bool> predicate)
        {
            _filters[chainId] = predicate;
            return this;
        }

        public IReadOnlyList<Id> Resolve(Id chainId, Id casterId) =>
            _chains.TryGetValue(chainId, out var targets) ? targets : Array.Empty<Id>();

        public IReadOnlyList<Id> Resolve(Id chainId, Id casterId, Id? currentTarget) => Resolve(chainId, casterId);

        public IReadOnlyList<Id> FilterExplicitTargets(Id chainId, Id casterId, IReadOnlyList<Id> targets)
        {
            if (targets.Count == 0 || !_filters.TryGetValue(chainId, out var predicate))
            {
                return targets;
            }

            var result = new List<Id>();
            foreach (var id in targets)
            {
                if (predicate(id))
                {
                    result.Add(id);
                }
            }

            return result;
        }
    }
}
