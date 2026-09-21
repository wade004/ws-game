using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Core.Rules.Combat
{
    /// <summary>
    /// ADR-0059（消费方反馈第三批第 5 条"普通攻击缺少框架原生执行机制"）：普通攻击的框架原生一等
    /// 执行路径——由挥击计时驱动（不是每帧/每 tick 无条件结算一次），命中时复用既有
    /// <c>weapon_damage_pct</c> 技能效果原语 + <see cref="Core.Rules.Combat.Resolver"/> 结算管线
    /// 产生伤害（不新写伤害公式），落地事件与技能效果完全同构（<c>combat.damage_dealt</c>/
    /// <c>unit.died</c> 等——既有的经验/掉落/光环触发监听者不需要为普通攻击特判）。
    /// <para>
    /// 判断记录（为什么是"重新构造一个 <see cref="EffectContext"/> 直接调用
    /// <see cref="IEffectSink.ApplyEffect"/>"，不是"走完整 <c>CastPipeline.CastSkill</c>"）：
    /// <c>weapon_damage_pct</c> 效果原语的数值组装 + 落地（<c>EffectDispatcher.ApplyDamageOrHeal</c>
    /// → <c>ICombatHost.ResolveEffect</c> → <see cref="Resolver.Resolve"/>）与"施法管线"（读条/引导/
    /// GCD/冷却/法术队列）是两件事——06 第 4.1 节结算管线九步固定顺序本就独立于"这次结算是被
    /// 哪种施法方式触发的"，<see cref="EffectContext"/> 不要求 <c>SkillId</c> 指向一条真实注册的
    /// <c>skill.def</c>（<c>ApplyDamageOrHeal</c> 的 <c>weapon_damage_pct</c> 分支只读
    /// <c>context.Params</c>/<c>context.BaseValue</c>，不反查 <c>SkillDefCache</c>，见该方法判断
    /// 记录）。普通攻击的"周期"由武器攻速/生物模板驱动，不是任何 <c>skill.def.cast_time</c>/
    /// <c>cooldown</c>，若强行包一层真实 <c>skill.def</c> 走完整施法管线，反而要么迁就"瞬发无
    /// 冷却"技能的既有 GCD/法术队列语义（与普通攻击"独立于全局冷却"的既定认知冲突），要么污染
    /// <c>skill.book</c>/<c>ai.rotation</c> 内容登记（普通攻击不该出现在任何职业的技能书里）。直接
    /// 复用结算管线、跳过施法管线，是"只借用已经存在的拼图（<c>weapon_profile</c>/
    /// <c>IWeaponDamageQuery</c>/<c>weapon_damage_pct</c>/<c>ControlFlags.NoAttack</c>），不新增
    /// 平行的技能定义"这一诉求最直接的落地方式。
    /// </para>
    /// <para>
    /// 判断记录（<c>ControlFlags.NoAttack</c> 单独识别，不要求叠加 <c>NoCast</c>/<c>NoMove</c>）：
    /// <c>CastPipeline.TryStartCast</c> 的"完全失能"总闸要求 <c>NoCast|NoMove|NoAttack</c> 三位同时
    /// 命中才拒绝施法（服务于"技能施放"这一更宽的判定面），本类只判定普通攻击自己的门控，直接检查
    /// <c>(GetControlFlags(casterId) &amp; ControlFlags.NoAttack) != 0</c>——一个只打
    /// <c>no_attack</c>（不叠加 <c>no_cast</c>/<c>no_move</c>）的"缴械"类控制光环，经本类型即可
    /// 正确拦截普通攻击，不需要消费方在游戏侧再手工判定一遍（见消费方反馈原文"框架自身的
    /// CastPipeline 从不单独检查 NoAttack"缺口）。命中该门控的这一拍视为"被拦截、消耗掉"（不结算、
    /// 不诊断——控制类光环的正常拦截语义，同 <c>CastPipeline</c> 对 <c>Silenced</c> 等既有失败原因
    /// "不是配置错误，不需要诊断"的一贯口径），挥击计时器照常清零重新计时，不会在控制解除的瞬间
    /// 补发一次"攒下来的"挥击（同一口径也用于射程判定，见 <see cref="Update"/> 判断记录）。
    /// </para>
    /// <para>
    /// 判断记录（目标消失/死亡/超出射程的行为）：
    /// <list type="bullet">
    /// <item>目标消失（<see cref="IUnitAccess.Exists"/> 为假，如被 Despawn）或目标死亡
    /// （<see cref="IUnitAccess.IsAlive"/> 为假）：当前目标被清空（<see cref="GetTarget"/> 变为
    /// <c>null</c>），状态回到 <see cref="AutoAttackState.NoTarget"/>（"未攻击"，任务书原句）——
    /// 与 <c>CastPipeline</c>"目标已死亡的技能施放请求"惯例一致（技能同样不会对着尸体继续生效），
    /// 不诊断（这是普通游戏进程中的正常状态迁移，不是配置错误）。<see cref="Enabled"/> 本身不受
    /// 影响——消费方后续 <see cref="SetTarget"/> 一个新目标即可继续攻击，不需要重新
    /// <see cref="SetEnabled"/>。</item>
    /// <item>超出 <see cref="AutoAttackOptions.Range"/>（非零时才判定，惯例同
    /// <c>SkillDef.Range</c>"0 = 无限制"）：本次挥击被跳过（不结算、不诊断，同 <c>NoAttack</c>
    /// 门控口径），计时器清零重新计时——距离是瞬时可变的战斗状态（消费方/AI 通常会追击拉近距离），
    /// 不是需要诊断提醒的配置缺陷。目标本身不被清空（不同于"消失/死亡"）：消费方拉近距离后下一次
    /// 挥击自然恢复结算，不需要重新 <see cref="SetTarget"/>。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 判断记录（挥击间隔缺失——运行时不静默降级，AGENTS.md §3）：<see
    /// cref="IWeaponDamageQuery.GetWeaponAttackIntervalSeconds"/>（已装备武器）与
    /// <see cref="IAttackIntervalFallbackProvider.GetAttackIntervalSeconds"/>（生物模板
    /// <c>attack_interval</c>，无武器时的回退）均为 <c>null</c> 时，普通攻击本次 tick 不会发生任何
    /// 攻击（计时器不推进——没有周期就谈不上"挥击到点"），经既有 <see cref="ICombatDiagnostics"/>
    /// 诊断出口告警一次（同一门控持续存在期间只警告一次，避免同一原因刷屏；恢复到"缺失"态之前若
    /// 数据变得可用过（如中途装备武器），下一次再次缺失会重新警告一次，见 <see cref="Update"/> 内
    /// <c>_missingIntervalWarned</c> 维护逻辑）。
    /// </para>
    /// </summary>
    public sealed class AutoAttackHost
    {
        /// <summary>普通攻击结算落地事件携带的 <c>SkillId</c>——不对应任何真实注册的
        /// <c>skill.def</c> 记录（见类型判断记录"不要求 SkillId 指向真实技能"），仅供表现层/日志/
        /// 事件订阅方识别"这次结算来自普通攻击，不是某个具体技能"。</summary>
        public static readonly Id NativeSkillId = new Id("skill.native_auto_attack");

        private sealed class Slot
        {
            public bool Enabled;
            public Id? TargetId;
            public double Elapsed;
            public bool MissingIntervalWarned;
        }

        private readonly IUnitAccess _units;
        private readonly IAuraQuery _auras;
        private readonly IEffectSink _effectSink;
        private readonly IWeaponDamageQuery _weaponDamageQuery;
        private readonly IAttackIntervalFallbackProvider _attackIntervalFallback;
        private readonly ICombatDiagnostics _diagnostics;
        private readonly Id _physicalSchool;

        private readonly Dictionary<Id, Slot> _slots = new Dictionary<Id, Slot>();

        /// <summary>可变配置（<see cref="AutoAttackOptions"/> 惯例同 <see cref="CombatOptions"/>）——
        /// 构造完成后仍可继续修改，不需要在装配期就决定好全部取值。</summary>
        public AutoAttackOptions Options { get; }

        /// <summary>
        /// <paramref name="physicalSchool"/>：武器未登记 <c>weapon_school</c>（<see
        /// cref="IWeaponDamageQuery.GetWeaponSchool"/> 返回 <c>null</c>）时的缺省学派——通常传
        /// <c>CombatOptions.PhysicalSchool</c>（"空手/未声明学派的普通攻击按物理伤害处理"，与近战
        /// 武器缺省物理学派的常见约定一致）。<paramref name="attackIntervalFallback"/>/<paramref
        /// name="diagnostics"/>/<paramref name="options"/> 缺省时分别取
        /// <see cref="NullAttackIntervalFallbackProvider.Instance"/>/新建
        /// <see cref="InMemoryCombatDiagnostics"/>/新建 <see cref="AutoAttackOptions"/>。
        /// </summary>
        public AutoAttackHost(
            IUnitAccess units,
            IAuraQuery auras,
            IEffectSink effectSink,
            IWeaponDamageQuery weaponDamageQuery,
            Id physicalSchool,
            IAttackIntervalFallbackProvider? attackIntervalFallback = null,
            ICombatDiagnostics? diagnostics = null,
            AutoAttackOptions? options = null)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _auras = auras ?? throw new ArgumentNullException(nameof(auras));
            _effectSink = effectSink ?? throw new ArgumentNullException(nameof(effectSink));
            _weaponDamageQuery = weaponDamageQuery ?? throw new ArgumentNullException(nameof(weaponDamageQuery));
            _physicalSchool = physicalSchool;
            _attackIntervalFallback = attackIntervalFallback ?? NullAttackIntervalFallbackProvider.Instance;
            _diagnostics = diagnostics ?? new InMemoryCombatDiagnostics();
            Options = options ?? new AutoAttackOptions();
        }

        /// <summary>开启/关闭 <paramref name="casterId"/> 的普通攻击（任务书"开关"状态）。关闭时
        /// 挥击计时器清零（重新开启从零开始计时，不会因为关闭期间"攒时间"而一开启就立即挥击）；
        /// 当前目标不受影响（<see cref="GetTarget"/> 保留，重新开启后继续对同一目标计时）——"开关"
        /// 与"目标"是两个正交状态（任务书原句），关闭不等于清空目标。</summary>
        public void SetEnabled(Id casterId, bool enabled)
        {
            var slot = GetOrCreateSlot(casterId);
            slot.Enabled = enabled;
            if (!enabled)
            {
                slot.Elapsed = 0.0;
                slot.MissingIntervalWarned = false;
            }
        }

        /// <summary>设置/切换/清空 <paramref name="casterId"/> 的当前普通攻击目标（任务书"目标"
        /// 状态）。切换目标时挥击计时器清零（同 <see cref="SetEnabled"/> 判断记录"不攒时间"，避免
        /// 切目标后立即命中新目标，这与"挥击计时驱动"而非"每次改目标就打一下"的定位一致）。</summary>
        public void SetTarget(Id casterId, Id? targetId)
        {
            var slot = GetOrCreateSlot(casterId);
            slot.TargetId = targetId;
            slot.Elapsed = 0.0;
            slot.MissingIntervalWarned = false;
        }

        public bool IsEnabled(Id casterId) => _slots.TryGetValue(casterId, out var slot) && slot.Enabled;

        public Id? GetTarget(Id casterId) => _slots.TryGetValue(casterId, out var slot) ? slot.TargetId : null;

        /// <summary>见 <see cref="AutoAttackState"/> 判断记录——"开关"×"目标"两个正交状态合并成的
        /// 可观测快照。</summary>
        public AutoAttackState GetState(Id casterId)
        {
            if (!_slots.TryGetValue(casterId, out var slot) || !slot.Enabled)
            {
                return AutoAttackState.Off;
            }

            return slot.TargetId.HasValue ? AutoAttackState.Attacking : AutoAttackState.NoTarget;
        }

        /// <summary>
        /// 挥击计时驱动的核心推进——供 <see cref="AutoAttackTickHandler"/> 每个连续时间 tick 调用
        /// 一次（离散战斗模式本版不驱动，见 <see cref="AutoAttackTickHandler"/> 判断记录），
        /// <paramref name="deltaSeconds"/> 为本次 tick 经过的真实秒数。
        /// </summary>
        public void Update(double deltaSeconds)
        {
            if (deltaSeconds <= 0.0 || _slots.Count == 0)
            {
                return;
            }

            // 确定性（AGENTS.md §3"不依赖字典枚举顺序"）：多个施法者可能在同一 tick 内各自触发一次
            // 结算，结算会消费 IRngHost（命中表掷骰）——.NET Dictionary<TKey,TValue> 不保证遍历顺序
            // 稳定，直接 foreach _slots 会让同一批施法者的掷骰顺序在不同运行间/不同 .NET 版本间
            // 漂移，破坏仿真回放确定性。按 Id 序数排序后再遍历，惯例同 IUnitAccess.AllUnits/
            // EquipmentHost.TryGetFirstWeaponSlot 既有"逐 Id 序数排序"写法。
            var casterIds = new List<Id>(_slots.Keys);
            casterIds.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));

            foreach (var casterId in casterIds)
            {
                var slot = _slots[casterId];

                if (!slot.Enabled || !slot.TargetId.HasValue)
                {
                    continue;
                }

                if (!_units.Exists(casterId) || !_units.IsAlive(casterId))
                {
                    // 施法者自身消失/死亡：不清空目标（避免死亡瞬间的其它清理路径与本类竞争同一个
                    // 单位的状态；施法者复活/重新出现后目标若仍然合法，攻击可以自然恢复），只是本
                    // tick 不推进计时——一直"暂停"直到施法者恢复存活。
                    continue;
                }

                var targetId = slot.TargetId.Value;
                if (!_units.Exists(targetId) || !_units.IsAlive(targetId))
                {
                    // 目标消失/死亡：回到"未攻击"（见类型判断记录）。
                    slot.TargetId = null;
                    slot.Elapsed = 0.0;
                    slot.MissingIntervalWarned = false;
                    continue;
                }

                var interval = _weaponDamageQuery.GetWeaponAttackIntervalSeconds(casterId)
                    ?? _attackIntervalFallback.GetAttackIntervalSeconds(casterId);

                if (!interval.HasValue || interval.Value <= 0.0)
                {
                    if (!slot.MissingIntervalWarned)
                    {
                        _diagnostics.Warn(
                            $"单位 \"{casterId}\" 已开启普通攻击，但既没有装备带 weapon_profile.speed " +
                            "的武器，所属生物模板也没有声明 attack_interval——本次 tick 不会发生任何" +
                            "攻击（AutoAttackHost 判断记录\"运行时不静默降级\"）。");
                        slot.MissingIntervalWarned = true;
                    }
                    continue; // 没有周期，谈不上"挥击到点"，计时器不推进。
                }

                slot.MissingIntervalWarned = false;
                slot.Elapsed += deltaSeconds;
                if (slot.Elapsed < interval.Value)
                {
                    continue; // 未到下一次挥击——这是"挥击计时驱动"与"每帧打一次"的分界。
                }

                // 保留余数、不清零：多个 tick 累积的推进不会因为"整除取余"丢失精度，长期不产生漂移
                // （同 CastPipeline 连续/离散模式换算"保留余数"的既有惯例）。
                slot.Elapsed -= interval.Value;

                if (Options.Range > 0.0 &&
                    Vec2.Distance(_units.GetPosition(casterId), _units.GetPosition(targetId)) > Options.Range)
                {
                    continue; // 超出射程：这一拍被跳过，不结算、不诊断（见类型判断记录）。
                }

                var control = _auras.GetControlFlags(casterId);
                if ((control & ControlFlags.NoAttack) != 0)
                {
                    continue; // 被 NoAttack 控制拦截：这一拍被拦截，不结算、不诊断（见类型判断记录）。
                }

                var school = _weaponDamageQuery.GetWeaponSchool(casterId) ?? _physicalSchool;

                // weapon_damage_pct、pct=100%（baseValue 兜底 pct，未提供 Params 时
                // EffectDispatcher.ApplyDamageOrHeal 直接读 context.BaseValue，见该方法判断记录）：
                // 一次挥击 = 武器秒伤 × 一拍常数 × 100%，与 06/ADR-0031 既有 weapon_damage_pct 换算
                // 公式完全一致，不新增任何伤害计算路径。
                var context = new EffectContext(
                    casterId, targetId, NativeSkillId, EffectKind.WeaponDamagePct, school,
                    baseValue: 1.0, coefficient: 0.0);
                _effectSink.ApplyEffect(context);
            }
        }

        private Slot GetOrCreateSlot(Id casterId)
        {
            if (!_slots.TryGetValue(casterId, out var slot))
            {
                slot = new Slot();
                _slots[casterId] = slot;
            }
            return slot;
        }
    }
}
