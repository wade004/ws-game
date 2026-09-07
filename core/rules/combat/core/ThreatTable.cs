using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;

namespace Core.Rules.Combat
{
    /// <summary>
    /// <see cref="IThreatTable"/> 默认实现（见 06_规则层_属性技能战斗AI.md 第 4.4 节）。
    /// <para>
    /// 判断记录：<see cref="IThreatTable"/> 的全部方法签名都携带 <c>unitId</c> 参数（即使
    /// <c>ICombatHost.GetThreatTable(unitId)</c> 已经按单位取得"专属"实例）——本类据此实现为
    /// 单个全局共享实例：<see cref="CombatHost.GetThreatTable"/> 对任何 <paramref name="unitId"/>
    /// 都返回同一个 <see cref="ThreatTable"/> 引用，内部按 <c>unitId</c> 分桶存储，避免为每个
    /// 单位分别 new 一份、徒增复杂度而不改变外部可观察行为。
    /// </para>
    /// </summary>
    public sealed class ThreatTable : IThreatTable
    {
        private readonly IUnitAccess _units;
        private readonly IEventBus _bus;
        private readonly int _maxEntries;

        // 判断记录：外层用 Dictionary（无需按 unitId 排序遍历——GetOrder 靠 CombatHost.Update 自行
        // 排序），内层用 SortedDictionary<Id, double> 保证同一单位的来源按 Id 序数确定性遍历
        // （见 GetTopThreat/EnforceCap 的"平局按 Id 序"判断记录）。
        private readonly Dictionary<Id, SortedDictionary<Id, double>> _tables = new Dictionary<Id, SortedDictionary<Id, double>>();

        public ThreatTable(IUnitAccess units, IEventBus bus, int maxEntries)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            if (maxEntries <= 0)
            {
                throw new ArgumentException("maxEntries 必须为正数", nameof(maxEntries));
            }
            _maxEntries = maxEntries;
        }

        /// <summary>当前持有至少一条仇恨记录的单位，按 Id 序数排序（供治疗仇恨的"敌对来源"
        /// 反查使用，见 <see cref="Resolver"/> 治疗分支判断记录）。</summary>
        public IReadOnlyList<Id> TrackedUnits
        {
            get
            {
                var list = new List<Id>(_tables.Keys);
                list.Sort();
                return list;
            }
        }

        public void AddThreat(Id unitId, Id sourceId, double amount)
        {
            // 06 第 4.4 节"规则"：仇恨来源必须是存在且存活的单位，否则忽略本次调用（不创建条目、
            // 不发事件）——嘲讽/伤害经过一个已经死亡或已移除的来源时，静默丢弃即可。
            if (!_units.Exists(sourceId) || !_units.IsAlive(sourceId))
            {
                return;
            }

            var table = GetOrCreateTable(unitId);
            var oldValue = table.TryGetValue(sourceId, out var existing) ? existing : 0.0;
            var newValue = oldValue + amount;
            SetInternal(unitId, table, sourceId, oldValue, newValue);
            EnforceCap(unitId, table);
        }

        public Id? GetTopThreat(Id unitId)
        {
            if (!_tables.TryGetValue(unitId, out var table) || table.Count == 0)
            {
                return null;
            }

            Id? best = null;
            var bestValue = double.NegativeInfinity;
            // SortedDictionary 按 Id 序数升序遍历：只在严格更大时替换，天然让并列最高值中
            // Id 序数最小的一个胜出（"平局按 Id 序"，见 IThreatTable.GetTopThreat 文档）。
            foreach (var pair in table)
            {
                if (pair.Value > bestValue)
                {
                    bestValue = pair.Value;
                    best = pair.Key;
                }
            }

            return best;
        }

        public void Clear(Id unitId)
        {
            if (_tables.TryGetValue(unitId, out var table))
            {
                table.Clear();
            }
        }

        public double GetThreat(Id unitId, Id sourceId)
        {
            if (_tables.TryGetValue(unitId, out var table) && table.TryGetValue(sourceId, out var value))
            {
                return value;
            }

            return 0.0;
        }

        public void SetThreat(Id unitId, Id sourceId, double amount)
        {
            var table = GetOrCreateTable(unitId);
            var oldValue = table.TryGetValue(sourceId, out var existing) ? existing : 0.0;
            SetInternal(unitId, table, sourceId, oldValue, amount);
            EnforceCap(unitId, table);
        }

        public IReadOnlyList<(Id source, double amount)> GetAll(Id unitId)
        {
            if (!_tables.TryGetValue(unitId, out var table))
            {
                return Array.Empty<(Id, double)>();
            }

            var result = new List<(Id, double)>(table.Count);
            foreach (var pair in table)
            {
                result.Add((pair.Key, pair.Value));
            }

            return result;
        }

        /// <summary>补充：把 <paramref name="unitId"/> 仇恨表里已不存在/已死亡的来源整体移除
        /// （见落地方案 T2-8 行"禁止仇恨表大小无上限增长（需有清理策略）"，本方法是显式清理入口；
        /// <see cref="AddThreat"/> 本身已经拒绝对死亡来源新增/累加，本方法额外清理"曾经存活、
        /// 后来死亡"的历史条目）。</summary>
        public void PruneDead(Id unitId)
        {
            if (!_tables.TryGetValue(unitId, out var table))
            {
                return;
            }

            var toRemove = new List<Id>();
            foreach (var pair in table)
            {
                if (!_units.Exists(pair.Key) || !_units.IsAlive(pair.Key))
                {
                    toRemove.Add(pair.Key);
                }
            }

            foreach (var sourceId in toRemove)
            {
                var oldValue = table[sourceId];
                table.Remove(sourceId);
                _bus.Enqueue(new CombatThreatChangedEvent(unitId, sourceId, oldValue, 0.0));
            }
        }

        /// <summary>
        /// RC-02 收边补齐：把 <paramref name="sourceId"/> 从全部单位的仇恨表里作为"来源"整体移除
        /// （不同于 <see cref="Clear"/>——那只清空 <paramref name="sourceId"/> 自己持有的表，不触碰
        /// 它作为来源挂在其它单位表上的条目）。供 <see cref="CombatHost"/> 在某单位被销毁
        /// （<c>entity.destroyed</c>）时做"双向仇恨"清理：其它仍存活单位的仇恨表里不应该继续挂着
        /// 一条指向已销毁单位的僵尸条目——<see cref="PruneDead"/> 能做到同样的效果，但需要逐个
        /// <paramref name="unitId"/> 显式调用，销毁事件驱动的清理不应该依赖调用方之后是否记得
        /// 对每个可能持有该来源条目的单位调用它。
        /// </summary>
        public void RemoveSourceEverywhere(Id sourceId)
        {
            foreach (var unitId in TrackedUnits)
            {
                if (!_tables.TryGetValue(unitId, out var table) || !table.TryGetValue(sourceId, out var oldValue))
                {
                    continue;
                }

                table.Remove(sourceId);
                _bus.Enqueue(new CombatThreatChangedEvent(unitId, sourceId, oldValue, 0.0));
            }
        }

        private SortedDictionary<Id, double> GetOrCreateTable(Id unitId)
        {
            if (!_tables.TryGetValue(unitId, out var table))
            {
                table = new SortedDictionary<Id, double>();
                _tables[unitId] = table;
            }

            return table;
        }

        private void SetInternal(Id unitId, SortedDictionary<Id, double> table, Id sourceId, double oldValue, double newValue)
        {
            table[sourceId] = newValue;
            if (!newValue.Equals(oldValue))
            {
                _bus.Enqueue(new CombatThreatChangedEvent(unitId, sourceId, oldValue, newValue));
            }
        }

        /// <summary>条目数超过上限时移除当前值最小的条目；并列最小值按 Id 序数最小者优先移除
        /// （见 06 §4.4"规则"、落地方案 T2-8 行"需有清理策略并测试覆盖"）。</summary>
        private void EnforceCap(Id unitId, SortedDictionary<Id, double> table)
        {
            while (table.Count > _maxEntries)
            {
                Id? victim = null;
                var victimValue = double.PositiveInfinity;
                foreach (var pair in table)
                {
                    if (pair.Value < victimValue)
                    {
                        victimValue = pair.Value;
                        victim = pair.Key;
                    }
                }

                if (!victim.HasValue)
                {
                    break;
                }

                table.Remove(victim.Value);
                _bus.Enqueue(new CombatThreatChangedEvent(unitId, victim.Value, victimValue, 0.0));
            }
        }
    }
}
