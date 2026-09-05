using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Core.Rules.Combat
{
    /// <summary>
    /// <see cref="ICombatHost"/> 默认实现（见 06_规则层_属性技能战斗AI.md 第 4.5/4.7 节）。
    /// 构造期从 <see cref="IDataRegistryView"/> 一次性加载 <c>combat.hit_table_config</c>/
    /// <c>combat.resist_curve</c>（见 <see cref="CombatDataLoader"/>），组装内部唯一的
    /// <see cref="Resolver"/> 与唯一的 <see cref="ThreatTable"/>（<see cref="GetThreatTable"/>
    /// 对任意 <c>unitId</c> 都返回同一个共享实例，判断记录见 <see cref="Combat.ThreatTable"/>）。
    /// </summary>
    public sealed class CombatHost : ICombatHost
    {
        private readonly IUnitAccess _units;
        private readonly IFactionMatrix _factions;
        private readonly IPowerHost _powers;
        private readonly IEventBus _bus;
        private readonly CombatOptions _options;
        private readonly ThreatTable _threatTable;
        private readonly Resolver _resolver;

        // 判断记录：06 第 4.5 节的"进出战斗"状态与脱战计时器是 CombatHost 自身的运行期状态
        // （不属于 IUnitAccess——那是"存在性/位置/阵营/等级/朝向/存活/模板/标签"门面，不含战斗
        // 状态），只对"曾经调用过 NotifyCombatEvent 的单位"分配存储，未参战单位不占用内存。
        private readonly Dictionary<Id, bool> _inCombat = new Dictionary<Id, bool>();
        private readonly Dictionary<Id, double> _timeSinceLastEvent = new Dictionary<Id, double>();

        public CombatHost(
            IStatHost stats,
            IPowerHost powers,
            IUnitAccess units,
            IAuraQuery auras,
            IFactionMatrix factions,
            IRngHost rng,
            IEventBus bus,
            IDataRegistryView registry,
            CombatOptions? options = null,
            ICombatDiagnostics? diagnostics = null,
            IStaticImmunityProvider? staticImmunity = null)
        {
            if (stats == null) throw new ArgumentNullException(nameof(stats));
            _powers = powers ?? throw new ArgumentNullException(nameof(powers));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            if (auras == null) throw new ArgumentNullException(nameof(auras));
            _factions = factions ?? throw new ArgumentNullException(nameof(factions));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            _options = options ?? new CombatOptions();
            var diag = diagnostics ?? new InMemoryCombatDiagnostics();

            var hitTables = CombatDataLoader.LoadHitTables(registry);
            var resistCurves = CombatDataLoader.LoadResistCurvesBySchool(registry);

            _threatTable = new ThreatTable(units, bus, _options.MaxThreatEntries);
            _resolver = new Resolver(
                stats, powers, units, auras, factions, rng, bus, _options,
                hitTables, resistCurves, diag, _threatTable, NotifyCombatEvent, staticImmunity);
        }

        public ResolveResult ResolveEffect(EffectContext context) => _resolver.Resolve(context);

        public IThreatTable GetThreatTable(Id unitId) => _threatTable;

        public bool IsInCombat(Id unitId) => _inCombat.TryGetValue(unitId, out var value) && value;

        public void NotifyCombatEvent(Id unitId, Id? hostileId = null)
        {
            _timeSinceLastEvent[unitId] = 0.0;

            if (IsInCombat(unitId))
            {
                return;
            }

            _inCombat[unitId] = true;
            _powers.SetInCombat(unitId, true);
            _bus.Enqueue(new CombatEnteredEvent(unitId, hostileId));
        }

        /// <summary>
        /// 脱战判定（见 06 第 4.5 节"脱离战斗：一段时间内（可配置）未产生新的战斗事件、且周边无
        /// 存活的敌对仇恨来源，置位 combatState = out，并触发仇恨表清空、资源回复规则切换"）。
        /// 按当前在战单位的 Id 序数遍历，保证确定性（见任务书"设计"一节"顺序按 Id 序"）。
        /// </summary>
        public void Update(double timeUnits)
        {
            if (timeUnits < 0)
            {
                throw new ArgumentException("timeUnits 不能为负数", nameof(timeUnits));
            }

            if (timeUnits == 0.0)
            {
                return;
            }

            var inCombatUnits = new List<Id>();
            foreach (var pair in _inCombat)
            {
                if (pair.Value)
                {
                    inCombatUnits.Add(pair.Key);
                }
            }
            inCombatUnits.Sort();

            foreach (var unitId in inCombatUnits)
            {
                var elapsed = (_timeSinceLastEvent.TryGetValue(unitId, out var v) ? v : 0.0) + timeUnits;
                _timeSinceLastEvent[unitId] = elapsed;

                if (elapsed < _options.LeaveCombatDelay)
                {
                    continue;
                }

                if (HasLivingHostileThreatSource(unitId))
                {
                    continue;
                }

                _inCombat[unitId] = false;
                _threatTable.Clear(unitId);
                _powers.SetInCombat(unitId, false);
                _bus.Enqueue(new CombatLeftEvent(unitId));
            }
        }

        private bool HasLivingHostileThreatSource(Id unitId)
        {
            if (!_units.Exists(unitId))
            {
                return false;
            }

            var unitFaction = _units.GetFaction(unitId);
            foreach (var (source, _) in _threatTable.GetAll(unitId))
            {
                if (!_units.Exists(source) || !_units.IsAlive(source))
                {
                    continue;
                }

                if (_factions.IsHostile(unitFaction, _units.GetFaction(source)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
