using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Numbers.Faction;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// <see cref="ITargetLockHost"/> 的默认实现（ADR-0177）：持有各单位的"当前目标"，并按数据执行"受击自动选中"。
    /// <para>
    /// <b>数据</b>：<c>creature.template.target_lock</c>（可选对象）——<c>auto_select_on_hit</c>（布尔，缺省 false = 关闭，既有游戏行为不变）、
    /// <c>modes</c>（可选字符串列表：只在单位的操控模式标签（<see cref="SetControlMode"/>）在列表里时生效；缺省或空 = 任何模式都生效）。
    /// 玩家单位也是 <c>creature.template</c> 生成的，所以同一个字段覆盖玩家与怪物。
    /// </para>
    /// <para>
    /// <b>规则</b>（订阅 <c>combat.damage_dealt</c>，按事件派发顺序逐个处理）：受击者有启用的规则、自己存活，且当前<b>没有有效目标</b>
    /// （从未选过 / 目标已死亡 / 目标不存在 / 目标不在同一地图）时，把"攻击者"设为目标。已有有效目标一律不抢。
    /// 攻击者 = 伤害来源沿召唤者归属链（<see cref="ISummonHost.GetOwner"/>，最多追 <see cref="MaxOwnerChainDepth"/> 层）归到的最终单位
    /// （投射物伤害的来源本就是施放者；召唤物伤害归到召唤者）。攻击者须存在、存活、与受击者同图、不是受击者自己，且阵营对受击者敌对
    /// （<see cref="IFactionMatrix.IsHostile"/>）——友方误伤、环境伤害（来源不是单位）、来源已死亡的持续伤害都不选中。
    /// 同一批事件里有多个攻击者时，先派发的伤害事件先选中，之后的因"已有有效目标"而不再改变——结果只取决于事件的确定性派发顺序，
    /// 不依赖字典枚举顺序、距离或随机。
    /// </para>
    /// <para>
    /// <b>失效清除</b>：目标死亡（<c>unit.died</c>）、目标离开世界（<c>entity.destroyed</c>）时清除并发 <c>unit.target_changed</c>；
    /// 持有者自己离开世界时直接丢弃其状态（不发事件）。距离过远、换图等"游戏自己的失效口径"由游戏调用 <see cref="SetTarget"/> 清除。
    /// </para>
    /// <para>
    /// 判断记录（只认伤害落地）：触发源是 <c>combat.damage_dealt</c>——未命中 / 被回避（<c>combat.attack_avoided</c>）、不带伤害的控制效果
    /// 不会触发自动选中；这是有意的最小口径（"被打了"以伤害落地为准，与仇恨表的伤害来源口径一致）。
    /// </para>
    /// </summary>
    public sealed class TargetLockHost : ITargetLockHost, IDisposable
    {
        /// <summary>沿归属链追溯攻击者的最大层数（防环 / 防异常深链；正常的召唤物只有一层）。</summary>
        public const int MaxOwnerChainDepth = 8;

        /// <summary>数据表与字段名。</summary>
        public const string TemplateTable = "creature.template";
        public const string RuleField = "target_lock";

        private readonly IEventBus _bus;
        private readonly IDataRegistryView _registry;
        private readonly IUnitAccess _units;
        private readonly IFactionMatrix _factions;
        private readonly ISummonHost? _summons;
        private readonly Dictionary<Id, Id> _targets = new Dictionary<Id, Id>();
        private readonly Dictionary<Id, string> _modes = new Dictionary<Id, string>();
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private readonly List<Id> _scratch = new List<Id>();

        public TargetLockHost(IEventBus bus, IDataRegistryView registry, IUnitAccess units, IFactionMatrix factions, ISummonHost? summons)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _factions = factions ?? throw new ArgumentNullException(nameof(factions));
            _summons = summons;
            _subscriptions.Add(bus.Subscribe<CombatDamageDealtEvent>(RulesEventKeys.CombatDamageDealt, OnDamageDealt));
            _subscriptions.Add(bus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, OnUnitDied));
            _subscriptions.Add(bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, OnEntityDestroyed));
        }

        public void Dispose()
        {
            foreach (var handle in _subscriptions)
            {
                handle.Dispose();
            }

            _subscriptions.Clear();
        }

        public Id? GetTarget(Id unitId) =>
            _targets.TryGetValue(unitId, out var target) && IsValidTarget(unitId, target) ? target : (Id?)null;

        public bool SetTarget(Id unitId, Id? targetId) => Change(unitId, targetId, TargetChangeCause.Manual, sourceId: null, requireValid: true);

        public string? GetControlMode(Id unitId) => _modes.TryGetValue(unitId, out var mode) ? mode : null;

        public void SetControlMode(Id unitId, string? mode)
        {
            if (string.IsNullOrEmpty(mode))
            {
                _modes.Remove(unitId);
            }
            else
            {
                _modes[unitId] = mode!;
            }
        }

        // ------------------------------------------------------------------ 状态变更

        private bool Change(Id holder, Id? target, string cause, Id? sourceId, bool requireValid)
        {
            if (target.HasValue && requireValid && !IsValidTarget(holder, target.Value))
            {
                return false;
            }

            var hadStored = _targets.TryGetValue(holder, out var stored);
            if (target.HasValue)
            {
                if (hadStored && stored.Equals(target.Value))
                {
                    return true;
                }

                _targets[holder] = target.Value;
            }
            else
            {
                if (!hadStored)
                {
                    return true;
                }

                _targets.Remove(holder);
            }

            _bus.Enqueue(new UnitTargetChangedEvent(holder, target, hadStored ? stored : (Id?)null, cause, sourceId));
            return true;
        }

        private bool IsValidTarget(Id holder, Id target)
        {
            if (!_units.Exists(target) || !_units.IsAlive(target))
            {
                return false;
            }

            var holderMap = _units.GetMapId(holder);
            var targetMap = _units.GetMapId(target);
            return !holderMap.HasValue || !targetMap.HasValue || holderMap.Value.Equals(targetMap.Value);
        }

        // ------------------------------------------------------------------ 受击自动选中

        private void OnDamageDealt(CombatDamageDealtEvent e)
        {
            var victim = e.TargetId;
            if (!_units.Exists(victim) || !_units.IsAlive(victim) || !RuleEnabled(victim))
            {
                return;
            }

            if (GetTarget(victim).HasValue)
            {
                return; // 已有有效目标：不抢。
            }

            var attacker = ResolveAttacker(e.SourceId);
            if (!attacker.HasValue || attacker.Value.Equals(victim))
            {
                return;
            }

            var a = attacker.Value;
            if (!_units.Exists(a) || !_units.IsAlive(a) || !IsValidTarget(victim, a))
            {
                return;
            }

            if (!_factions.IsHostile(_units.GetFaction(victim), _units.GetFaction(a)))
            {
                return;
            }

            Change(victim, a, TargetChangeCause.AutoHit, e.SourceId, requireValid: false);
        }

        /// <summary>伤害来源沿召唤者归属链归到最终单位；来源不是单位（环境伤害）返回 null。</summary>
        private Id? ResolveAttacker(Id sourceId)
        {
            if (!_units.Exists(sourceId))
            {
                return null;
            }

            var current = sourceId;
            for (var depth = 0; depth < MaxOwnerChainDepth && _summons != null; depth++)
            {
                var owner = _summons.GetOwner(current);
                if (!owner.HasValue || !_units.Exists(owner.Value))
                {
                    break;
                }

                current = owner.Value;
            }

            return current;
        }

        private bool RuleEnabled(Id victim)
        {
            var template = _units.GetTemplateId(victim);
            if (!template.HasValue)
            {
                return false;
            }

            var row = _registry.Get(TemplateTable, template.Value);
            if (row == null || !row.TryGetObject(RuleField, out var rule))
            {
                return false;
            }

            if (!rule.TryGetValue("auto_select_on_hit", out var enabled) || !(enabled is JsonBool flag) || !flag.Value)
            {
                return false;
            }

            if (!rule.TryGetValue("modes", out var modesValue) || !(modesValue is JsonArray modes) || modes.Count == 0)
            {
                return true;
            }

            var current = GetControlMode(victim);
            if (current == null)
            {
                return false;
            }

            for (var i = 0; i < modes.Count; i++)
            {
                if (modes[i] is JsonString text && string.Equals(text.Value, current, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ 失效清除

        private void OnUnitDied(UnitDiedEvent e) => ClearHoldersTargeting(e.UnitId, TargetChangeCause.TargetDied);

        private void OnEntityDestroyed(EntityDestroyedEvent e)
        {
            _modes.Remove(e.EntityId);
            _targets.Remove(e.EntityId);
            ClearHoldersTargeting(e.EntityId, TargetChangeCause.TargetGone);
        }

        private void ClearHoldersTargeting(Id gone, string cause)
        {
            _scratch.Clear();
            foreach (var pair in _targets)
            {
                if (pair.Value.Equals(gone))
                {
                    _scratch.Add(pair.Key);
                }
            }

            if (_scratch.Count == 0)
            {
                return;
            }

            _scratch.Sort((x, y) => string.CompareOrdinal(x.Value, y.Value));
            foreach (var holder in _scratch)
            {
                Change(holder, null, cause, sourceId: null, requireValid: false);
            }
        }
    }
}
