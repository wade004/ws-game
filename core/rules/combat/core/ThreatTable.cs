using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Numbers.Faction;
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
        private readonly IFactionMatrix? _factions;

        // 判断记录：外层用 Dictionary（无需按 unitId 排序遍历——GetOrder 靠 CombatHost.Update 自行
        // 排序），内层用 SortedDictionary<Id, double> 保证同一单位的来源按 Id 序数确定性遍历
        // （见 GetTopThreat/EnforceCap 的"平局按 Id 序"判断记录）。
        private readonly Dictionary<Id, SortedDictionary<Id, double>> _tables = new Dictionary<Id, SortedDictionary<Id, double>>();

        public ThreatTable(IUnitAccess units, IEventBus bus, int maxEntries)
            : this(units, bus, maxEntries, factions: null)
        {
        }

        /// <summary>ADR-0088（消费方第三十三批反馈2根治）：新增重载，注入 <paramref name="factions"/>
        /// 后订阅 <see cref="RulesEventKeys.UnitFactionChanged"/>，在运行期单位改阵营时对全部仇恨表
        /// 清理"来源与表主人已不再敌对"的条目（见 <see cref="PruneAfterFactionChange"/> 判断记录）。
        /// <paramref name="factions"/> 为 <c>null</c>（原三参数构造函数的既有调用方）时不订阅，行为
        /// 与本次改动之前完全一致——生产装配（<see cref="CombatHost"/>）已改为传入真实
        /// <see cref="IFactionMatrix"/>，测试假实现/未接入阵营矩阵的调用方不受影响。</summary>
        public ThreatTable(IUnitAccess units, IEventBus bus, int maxEntries, IFactionMatrix? factions)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            if (maxEntries <= 0)
            {
                throw new ArgumentException("maxEntries 必须为正数", nameof(maxEntries));
            }
            _maxEntries = maxEntries;
            _factions = factions;

            if (_factions != null)
            {
                _bus.Subscribe<UnitFactionChangedEvent>(
                    RulesEventKeys.UnitFactionChanged, evt => PruneAfterFactionChange(evt.UnitId));
            }
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
                RemoveSource(unitId, sourceId);
            }
        }

        /// <summary>ADR-0088：<see cref="IThreatTable.RemoveSource"/> 的真实实现——不存在则
        /// no-op（同 <see cref="IThreatTable.RemoveSource"/> 文档"若存在"）。<see
        /// cref="PruneAfterFactionChange"/> 与 <see cref="RemoveSourceEverywhere"/> 均改为经本方法
        /// 统一完成"移除 + 变化归零事件"，不再各自重复同一段逻辑。</summary>
        public void RemoveSource(Id unitId, Id sourceId)
        {
            if (!_tables.TryGetValue(unitId, out var table) || !table.TryGetValue(sourceId, out var oldValue))
            {
                return;
            }

            table.Remove(sourceId);
            _bus.Enqueue(new CombatThreatChangedEvent(unitId, sourceId, oldValue, 0.0));
        }

        /// <summary>ADR-0088（消费方第三十三批反馈2根治）：<see cref="RulesEventKeys.UnitFactionChanged"/>
        /// 订阅回调——<paramref name="changedUnitId"/> 阵营变化后，对全部仇恨表清理"来源与表主人
        /// 已不再敌对"的条目，双向都要检查：
        /// <list type="bullet">
        /// <item>(a) <paramref name="changedUnitId"/> 自己的仇恨表：逐条来源用变化后的新阵营现场判定
        /// <see cref="IFactionMatrix.IsHostile"/>，不再敌对即删（消费方反馈场景：A 仇恨顶端是 B，
        /// B 运行期改判 A 同阵营后，A 表里的 B 应被删除）。</item>
        /// <item>(b) <paramref name="changedUnitId"/> 作为"来源"挂在其它单位表上的条目：对每个持有
        /// 该来源的表主人，同样现场判定是否仍敌对，不再敌对即删（双向清理的另一半——变友方的单位
        /// 自己若曾经是别人的仇恨来源，那些条目也要跟着失效）。</item>
        /// </list>
        /// 不做任何缓存——两个方向都直接读 <see cref="IUnitAccess.GetFaction"/> 的当前值，事件本身
        /// 只用于"知道该重新判定谁了"，不携带用于判定的阵营快照。<see cref="_factions"/> 为 null
        /// （未订阅）时不会走到本方法。</summary>
        private void PruneAfterFactionChange(Id changedUnitId)
        {
            if (_factions == null || !_units.Exists(changedUnitId))
            {
                return;
            }

            var changedFaction = _units.GetFaction(changedUnitId);

            if (_tables.TryGetValue(changedUnitId, out var ownTable))
            {
                var toRemove = new List<Id>();
                foreach (var pair in ownTable)
                {
                    if (!_units.Exists(pair.Key))
                    {
                        continue; // 已死亡/已移除的来源交给 PruneDead 处理，不在本方法职责内。
                    }

                    if (!_factions.IsHostile(changedFaction, _units.GetFaction(pair.Key)))
                    {
                        toRemove.Add(pair.Key);
                    }
                }

                foreach (var sourceId in toRemove)
                {
                    RemoveSource(changedUnitId, sourceId);
                }
            }

            foreach (var trackedUnitId in TrackedUnits)
            {
                if (trackedUnitId.Equals(changedUnitId))
                {
                    continue; // (a) 已处理。
                }

                if (!_tables.TryGetValue(trackedUnitId, out var table) || !table.ContainsKey(changedUnitId))
                {
                    continue;
                }

                if (!_units.Exists(trackedUnitId))
                {
                    continue;
                }

                var ownerFaction = _units.GetFaction(trackedUnitId);
                if (!_factions.IsHostile(ownerFaction, changedFaction))
                {
                    RemoveSource(trackedUnitId, changedUnitId);
                }
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
