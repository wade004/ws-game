using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Tests.Carriers.Summon
{
    /// <summary>
    /// <see cref="ICombatHost"/> 的最小测试假实现：按单位 id 可编程"是否在战斗中"，记录
    /// <see cref="NotifyCombatEvent"/> 调用（供 <c>SummonTickHandler</c> 的进出战斗联动测试使用）。
    /// 惯例同 <c>core/rules/skill/tests/FakeCombatHost.cs</c>：只覆盖本模块测试实际用到的行为。
    /// </summary>
    internal sealed class FakeCombatHost : ICombatHost
    {
        private readonly HashSet<Id> _inCombat = new HashSet<Id>();

        // 收边任务补齐（缺口 (c) ShareThreat）：与真实 core/rules/combat.ThreatTable/CombatHost.
        // GetThreatTable 同一惯例——全部单位共用同一个 IThreatTable 实例（GetThreatTable 忽略传入的
        // unitId 参数，见该方法），按单位维度存取只体现在 IThreatTable 各方法自己的 unitId 参数上。
        private readonly FakeThreatTable _threatTable = new FakeThreatTable();

        public List<Id> NotifyCalls { get; } = new List<Id>();

        public void SetInCombat(Id unitId, bool inCombat)
        {
            if (inCombat)
            {
                _inCombat.Add(unitId);
            }
            else
            {
                _inCombat.Remove(unitId);
            }
        }

        public bool IsInCombat(Id unitId) => _inCombat.Contains(unitId);

        public void NotifyCombatEvent(Id unitId, Id? hostileId = null)
        {
            NotifyCalls.Add(unitId);
            _inCombat.Add(unitId);
        }

        public ResolveResult ResolveEffect(EffectContext context) =>
            throw new NotSupportedException("FakeCombatHost 不支持 ResolveEffect（本模块测试不需要）");

        public IThreatTable GetThreatTable(Id unitId) => _threatTable;

        public void Update(double timeUnits)
        {
        }
    }

    /// <summary>最小 <see cref="IThreatTable"/> 假实现：单位维度的仇恨表，惯例同真实
    /// <c>core/rules/combat.ThreatTable</c>（一个实例内部按 unitId 分桶），供
    /// <c>SummonTickHandlerTests</c> 的 ShareThreat 用例验证跨单位合并/回写行为。</summary>
    internal sealed class FakeThreatTable : IThreatTable
    {
        private readonly Dictionary<string, Dictionary<string, double>> _buckets =
            new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);

        public void AddThreat(Id unitId, Id sourceId, double amount)
        {
            var bucket = Bucket(unitId);
            bucket[sourceId.Value] = (bucket.TryGetValue(sourceId.Value, out var existing) ? existing : 0.0) + amount;
        }

        public Id? GetTopThreat(Id unitId)
        {
            if (!_buckets.TryGetValue(unitId.Value, out var bucket) || bucket.Count == 0)
            {
                return null;
            }

            string? topSource = null;
            var topAmount = double.NegativeInfinity;
            foreach (var kv in bucket)
            {
                if (kv.Value > topAmount)
                {
                    topAmount = kv.Value;
                    topSource = kv.Key;
                }
            }
            return topSource == null ? (Id?)null : new Id(topSource);
        }

        public void Clear(Id unitId) => _buckets.Remove(unitId.Value);

        public double GetThreat(Id unitId, Id sourceId) =>
            _buckets.TryGetValue(unitId.Value, out var bucket) && bucket.TryGetValue(sourceId.Value, out var amount) ? amount : 0.0;

        public void SetThreat(Id unitId, Id sourceId, double amount) => Bucket(unitId)[sourceId.Value] = amount;

        public IReadOnlyList<(Id source, double amount)> GetAll(Id unitId)
        {
            var result = new List<(Id, double)>();
            if (_buckets.TryGetValue(unitId.Value, out var bucket))
            {
                foreach (var kv in bucket)
                {
                    result.Add((new Id(kv.Key), kv.Value));
                }
            }
            return result;
        }

        private Dictionary<string, double> Bucket(Id unitId)
        {
            if (!_buckets.TryGetValue(unitId.Value, out var bucket))
            {
                bucket = new Dictionary<string, double>(StringComparer.Ordinal);
                _buckets[unitId.Value] = bucket;
            }
            return bucket;
        }
    }
}
