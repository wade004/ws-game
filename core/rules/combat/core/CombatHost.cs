using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
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

            // RC-02 收边补齐：订阅 entity.destroyed 做幂等战斗清理（见 OnEntityDestroyed 判断
            // 记录）——生物销毁（如 CreatureFactory.Despawn）会先同步注销 IPowerHost/IStatHost 的
            // 单位注册，本模块自己的 _inCombat/_timeSinceLastEvent 状态与 ThreatTable 双向仇恨条目
            // 此前完全不感知这一事件，只能等下一次 Update 因脱战延迟到期才尝试处理该单位，届时
            // Powers.SetInCombat 访问已注销的单位直接抛异常（审计 RC-02）。
            _bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, OnEntityDestroyed);
        }

        /// <summary>
        /// RC-02 收边补齐：单位被销毁后幂等清理战斗相关状态——不访问 <see cref="IPowerHost"/>/
        /// <see cref="IStatHost"/>（销毁时点这两者对该单位的注册通常已经被调用方提前撤销，见类型
        /// 顶部构造函数订阅处判断记录，访问会抛异常，这正是本次要修复的崩溃路径本身）。
        /// <para>
        /// 幂等：<see cref="Dictionary{TKey,TValue}.Remove"/> 对不存在的 key 是安全 no-op，
        /// <see cref="ThreatTable.Clear"/>/<see cref="ThreatTable.RemoveSourceEverywhere"/> 对空表/
        /// 不存在的来源同样是安全 no-op——同一个（理论上不会重复派发的）<c>entity.destroyed</c>
        /// 或对一个从未进过战的单位重复调用本方法都不会抛异常或产生副作用。
        /// </para>
        /// </summary>
        private void OnEntityDestroyed(EntityDestroyedEvent evt)
        {
            var unitId = evt.EntityId;

            _inCombat.Remove(unitId);
            _timeSinceLastEvent.Remove(unitId);

            // 双向仇恨清理：既清空该单位自己持有的仇恨表（谁在打它），也把它从其它仍存活单位的
            // 仇恨表里作为"来源"整体摘除（它在打谁）——见 ThreatTable.RemoveSourceEverywhere
            // 判断记录，呼应 HasLivingHostileThreatSource 的双向查询。
            _threatTable.Clear(unitId);
            _threatTable.RemoveSourceEverywhere(unitId);
        }

        public ResolveResult ResolveEffect(EffectContext context) => _resolver.Resolve(context);

        public IThreatTable GetThreatTable(Id unitId) => _threatTable;

        public bool IsInCombat(Id unitId) => _inCombat.TryGetValue(unitId, out var value) && value;

        /// <summary>
        /// C11-RELOAD 根治新增（architecture/落地计划/消费方反馈-2026-09-11-读档空间索引与复活生命
        /// 周期.md 第 2 项）：清空该单位的运行期战斗态——<see cref="_inCombat"/> 标记、脱战计时器、
        /// 双向仇恨表（<see cref="ThreatTable.Clear"/> 该单位自己持有的仇恨表 + <see
        /// cref="ThreatTable.RemoveSourceEverywhere"/> 把它从其它单位仇恨表里摘除，同 <see
        /// cref="OnEntityDestroyed"/> 同款清理惯例）——不发布 <see cref="CombatLeftEvent"/>、不触碰
        /// <see cref="IPowerHost"/>。供读档恢复流程（<c>Core.Gameplay.Assembly.GameplayAssembly</c>
        /// 的 <c>IDerivedStateRebuilder.BeforeLoad</c>）在真正回填存档 <c>in_combat</c> 之前调用一次，
        /// 把"死亡结算等已经写入、但读档不会覆盖"的运行期战斗态先行清空，避免读档后 <see
        /// cref="IsInCombat"/> 仍残留读档前的旧值（真实探针复现：保存时不在战、致死移动后同图读档，
        /// 读档后 <c>CombatHost.IsInCombat=true</c>）。
        /// </summary>
        public void ClearCombatState(Id unitId)
        {
            _inCombat.Remove(unitId);
            _timeSinceLastEvent.Remove(unitId);
            _threatTable.Clear(unitId);
            _threatTable.RemoveSourceEverywhere(unitId);
        }

        /// <summary>
        /// C11-RELOAD 根治新增：把该单位的进出战斗状态显式置为 <paramref name="inCombat"/>——<see
        /// cref="CombatHost"/> 自身的 <see cref="_inCombat"/> 是"是否在战"的唯一来源（见本类型注释、
        /// 消费方反馈第 2 项判断记录），本方法同时按既有惯例（<see cref="NotifyCombatEvent"/>/<see
        /// cref="Update"/> 两处既有写法）把同一份值同步进 <see cref="IPowerHost"/>（脱战/在战回复速率
        /// 切换依赖 <see cref="IPowerHost.SetInCombat"/>），保证两者读到的值恒一致，不再需要调用方
        /// （<c>PlayerVitalsPersistable.Load</c>）绕开本类型直接调用 <see cref="IPowerHost.SetInCombat"/>。
        /// 不发布 <see cref="CombatEnteredEvent"/>/<see cref="CombatLeftEvent"/>——读档不是一次业务
        /// 事件（同本仓库既有"读档不重发业务事件"惯例，调用方本就处于 <c>IEventBus.SuppressDispatch</c>
        /// 作用域内）。调用前应先调用 <see cref="ClearCombatState"/>（见调用方恢复顺序契约：先清空、
        /// 再恢复），本方法本身不做这一步，只管赋值。<paramref name="unitId"/> 当前不存在于世界模拟中
        /// （<see cref="IUnitAccess.Exists"/> 为 false）时仍会写入 <see cref="_inCombat"/>（字典本身
        /// 不要求单位存在），但跳过 <see cref="IPowerHost"/> 同步这一步——避免重现 <see
        /// cref="NotifyCombatEvent"/> 判断记录"直接调用 IPowerHost.SetInCombat 对已注销单位会抛
        /// InvalidOperationException"那个崩溃路径。
        /// </summary>
        public void RestoreCombatState(Id unitId, bool inCombat)
        {
            _inCombat[unitId] = inCombat;
            if (_units.Exists(unitId))
            {
                _powers.SetInCombat(unitId, inCombat);
            }
        }

        /// <summary>
        /// C02 收口（外部审计 7e63d66 第四轮）：真实 <c>CreatureFactory.Despawn</c> 会同步注销
        /// <see cref="IPowerHost"/>/<see cref="IStatHost"/> 的单位注册（见 <see cref="OnEntityDestroyed"/>
        /// 判断记录），但已施加到其它存活目标身上、来源正是这个被销毁单位的周期性效果
        /// （<c>periodic_damage</c>/<c>periodic_heal</c>）不会因为来源销毁而停止结算——
        /// <see cref="Resolver"/> 每次结算都会对结算双方各调用一次本方法（见 <see cref="Resolver"/>
        /// 构造判断记录"对结算双方各调用一次 NotifyCombatEvent(自己, 对方)"），来源一侧因此可能是
        /// 一个已经被销毁、<see cref="IPowerHost"/> 注册已撤销的单位——直接调用
        /// <see cref="IPowerHost.SetInCombat"/> 会抛 <see cref="InvalidOperationException"/>（"单位
        /// 未注册"），这正是 <c>RC-02</c> 判断记录提到的"访问已注销的单位直接抛异常"崩溃路径的
        /// 另一个未覆盖入口（RC-02 当时只补齐了 <see cref="Update"/> 因脱战延迟到期这一条路径，未
        /// 覆盖"来源已销毁但仍在产生新战斗事件"这一条）。
        /// <para>
        /// 判断记录（进战通知对不存在单位静默跳过，不是"冻结/降级"）：与 C02 另一处（周期效果读取
        /// 来源缩放属性，见 <see cref="Core.Rules.Skill.EffectDispatcher"/> 判断记录）不同，"进战"
        /// 这件事对一个已经不存在于世界中的单位没有任何有意义的语义（它既不会再被 AI/UI 观察到，
        /// 也不会再脱战——脱战判定需要的 <see cref="IUnitAccess.Exists"/> 已经为 false），静默跳过
        /// 是唯一合理的策略：不写入 <see cref="_inCombat"/>/<see cref="_timeSinceLastEvent"/>，不
        /// 访问 <see cref="IPowerHost"/>，不发布 <see cref="CombatEnteredEvent"/>。用
        /// <see cref="IUnitAccess.Exists"/> 判断"是否仍存在"——本模块已有的
        /// <see cref="HasLivingHostileThreatSource"/>/<see cref="Resolver"/> 多处判断记录同样用它
        /// 做防御性检查，惯例一致，不需要新增 <see cref="IPowerHost"/> 契约成员。
        /// </para>
        /// </summary>
        public void NotifyCombatEvent(Id unitId, Id? hostileId = null)
        {
            if (!_units.Exists(unitId))
            {
                return;
            }

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
