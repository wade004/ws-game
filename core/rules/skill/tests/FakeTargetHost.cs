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

        public FakeTargetHost SetChain(Id chainId, params Id[] targets)
        {
            _chains[chainId] = targets;
            return this;
        }

        public IReadOnlyList<Id> Resolve(Id chainId, Id casterId) =>
            _chains.TryGetValue(chainId, out var targets) ? targets : Array.Empty<Id>();

        public IReadOnlyList<Id> Resolve(Id chainId, Id casterId, Id? currentTarget) => Resolve(chainId, casterId);
    }
}
