using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>T-N3-8（ADR-0031 决策 6、拍板 7）：手工登记的"带分配系数"解析结果，供
        /// <see cref="ResolveWithCoefficients"/> 直接返回——真实 <c>TargetHost</c> 的系数来自
        /// <c>target.chain_def.overflow_policy</c>/<c>max_targets</c> 与候选收集/排序管线（见
        /// <c>Core.Rules.Targeting.TargetHost</c>），本假实现不搭建完整数据/空间查询环境，改由测试
        /// 直接给定"这条链应该解析出哪些目标、各自什么系数"，只验证"目标层→EffectDispatcher"这一段
        /// 的系数透传/缩放契约（同 <see cref="_filters"/> 判断记录"只验证透传契约，不重复覆盖
        /// TargetHost 自己的候选收集/排序/超出策略行为，那部分由 targeting/tests 覆盖"）。</summary>
        private readonly Dictionary<Id, TargetResolution> _chainsWithCoefficients = new Dictionary<Id, TargetResolution>();

        public FakeTargetHost SetChain(Id chainId, params Id[] targets)
        {
            _chains[chainId] = targets;
            return this;
        }

        /// <summary>见 <see cref="_chainsWithCoefficients"/> 判断记录。同时用
        /// <paramref name="targets"/> 的目标 Id 覆盖 <see cref="SetChain"/>（保持旧签名
        /// <see cref="Resolve(Id, Id)"/> 与本方法配置的目标集合一致，便于测试对照两条入口）。</summary>
        public FakeTargetHost SetChainWithCoefficients(
            Id chainId, TargetOverflowPolicy policy, int cap, params (Id Target, double Coefficient)[] targets)
        {
            _chainsWithCoefficients[chainId] = new TargetResolution(targets, policy, cap);
            _chains[chainId] = targets.Select(t => t.Target).ToArray();
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

        /// <summary>见 <see cref="_chainsWithCoefficients"/> 判断记录：登记过时直接返回；未登记的
        /// 链退化为接口默认实现同一语义（旧 <see cref="Resolve(Id, Id, Id?)"/> 结果整体赋系数 1、
        /// <see cref="TargetOverflowPolicy.Truncate"/>、<c>cap=0</c>），不强制每个测试都显式配置。</summary>
        public TargetResolution ResolveWithCoefficients(Id chainId, Id casterId, Id? currentTarget = null) =>
            _chainsWithCoefficients.TryGetValue(chainId, out var resolution)
                ? resolution
                : new TargetResolution(
                    Resolve(chainId, casterId, currentTarget).Select(id => (id, 1.0)), TargetOverflowPolicy.Truncate, cap: 0);

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
