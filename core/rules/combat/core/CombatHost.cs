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

        /// <summary>
        /// W2b 收边补齐（判断记录 3，离散模式下 fixed_order 策略跨轮出现 combat.left → combat.entered
        /// 虚假往返的根因修复）：06 第 4.5 节"周边无存活的敌对仇恨来源"须双向判断，此前只查
        /// <paramref name="unitId"/> 自己的仇恨表（谁打过我），漏查<paramref name="unitId"/> 是否仍
        /// 挂在某个存活敌对单位的仇恨表里（我在打谁）——<see cref="ThreatTable.AddThreat"/> 只记到
        /// <c>被攻击方</c>（<c>context.TargetId</c>）的表上，主动进攻、尚未被对方反击过的一方自己的
        /// 仇恨表恒为空。
        /// <para>
        /// 复现（离散模式，<c>fixed_order</c> 先攻策略，两单位互相攻击，回归测试见
        /// <c>Tests.Gameplay.Discrete.GameplayAssemblyDiscreteWiringTests.FixedOrder_MutualCombatAcrossThreeRounds_DoesNotLeaveAndReenterCombat</c>）：
        /// <c>TimeModelSwitch.SwitchToDiscrete</c> 把 <c>LeaveCombatDelay</c> 按 <c>seconds_per_turn</c>
        /// 换算为轮数并 <c>Math.Ceiling</c> 向上取整、至少 1 轮（见该方法）——一旦换算结果恰好等于
        /// 1，<c>CombatTickHandler</c> 每轮结束调用一次 <see cref="Update"/>(1.0)，任何单位只要在
        /// 本轮参与过一次战斗事件（<c>_timeSinceLastEvent</c> 被刚刚清零），下一次轮结束时
        /// <c>elapsed</c> 必然恰好等于 <c>LeaveCombatDelay</c>（1.0），"未达延迟"这条 continue 分支
        /// （见 <see cref="Update"/> 内 <c>elapsed &lt; _options.LeaveCombatDelay</c> 判断）在离散模式
        /// 下形同虚设——每一次轮结束都会落到本方法，完全依赖仇恨表判断是否仍在交战。两个单位互相
        /// 攻击时，若某一方在本轮尚未被对方真正命中过（如对方本轮先手，尚未轮到它还手），该方自己
        /// 的仇恨表为空，本方法此前判定其"周边无存活敌对来源"而被误判脱战，随即在对方下一步反击时
        /// 经 <see cref="NotifyCombatEvent"/> 重新进战——观测到虚假的 <c>combat.left → combat.entered</c>
        /// 往返，并把该单位从 <c>TurnScheduler</c> 当前轮行动顺序里移除又重新追加到末尾（见
        /// <c>TimeModelSwitch.OnCombatLeft</c>/<c>OnCombatEntered</c> 对 <c>fixed_order</c> 策略的
        /// <c>RemoveParticipant</c>/<c>AddParticipant</c> 处理），扰乱既定的行动顺序。
        /// </para>
        /// <para>
        /// 修复：新增反向检查——<paramref name="unitId"/> 自己仇恨表为空（或没有存活敌对来源）时，
        /// 进一步查是否仍作为攻击来源挂在某个存活敌对单位的仇恨表里（即"我是否仍在主动攻击某个
        /// 活着的敌人"）。两个方向任一成立即视为"周边仍有存活敌对仇恨来源"，与主动进攻方在对方
        /// 反击之前就被判定脱战的场景相符——一旦双方任一方已经死亡或不再敌对，两个方向都不会再有
        /// 匹配，脱战判定不受影响（见 <c>CombatEnterLeaveTests.Update_AfterDelay_NoLivingHostileSource_LeavesCombatAndClearsThreat</c>
        /// 等既有用例：攻击者死亡后两个方向均查不到存活敌对来源，防御方仍会正常脱战）。
        /// </para>
        /// </summary>
        private bool HasLivingHostileThreatSource(Id unitId)
        {
            if (!_units.Exists(unitId))
            {
                return false;
            }

            var unitFaction = _units.GetFaction(unitId);

            // 方向一：unitId 自己的仇恨表——谁攻击过/仇恨过 unitId。
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

            // 方向二：unitId 是否仍作为攻击来源挂在某个存活敌对单位的仇恨表里——unitId 主动进攻
            // 某个活着的敌人，但对方尚未反击、unitId 自己的仇恨表因此为空（见本方法判断记录）。
            foreach (var trackedUnit in _threatTable.TrackedUnits)
            {
                if (trackedUnit.Equals(unitId) || !_units.Exists(trackedUnit) || !_units.IsAlive(trackedUnit))
                {
                    continue;
                }

                if (_threatTable.GetThreat(trackedUnit, unitId) <= 0.0)
                {
                    continue;
                }

                if (_factions.IsHostile(unitFaction, _units.GetFaction(trackedUnit)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
