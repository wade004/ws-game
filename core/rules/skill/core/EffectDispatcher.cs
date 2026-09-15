using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// <see cref="IEffectSink"/> 实现：19 个 Effect 原语（06 第 3.2 节）的落地出口，按
    /// <see cref="EffectKind"/> 分派（见落地方案 T2-4 必读"效果交给 ICombatHost.ResolveEffect 即时
    /// 结算，由 combat 决定落地时机"）。<c>school_damage</c>/<c>weapon_damage_pct</c>/<c>heal</c>
    /// 三类先做本模块职责内的免疫短路与数值组装（基础值 + 系数 × 缩放属性、SpellMod
    /// <c>effect_value</c>/<c>crit_chance</c> 修正），再交给 <see cref="ICombatHost.ResolveEffect"/>；
    /// 06 第 4.1 节结算管线本身（命中/暴击/减免/吸收/落地/后置）完全是 combat 的职责，本类不重复
    /// 实现。<c>projectile</c> 收边任务补齐后转交 <see cref="IProjectileSpawner"/>（见该接口判断
    /// 记录"依赖倒置"，专用依赖倒置接口而非六合一扩展点，理由见该接口注释）；<c>summon</c>/
    /// <c>open_lock</c>/<c>create_item</c>/<c>set_world_flag</c>/<c>script</c> 五类转交
    /// <see cref="IEffectExtension"/>（见该接口注释）。
    /// </summary>
    public sealed class EffectDispatcher : IEffectSink
    {
        private readonly AuraHost _auraHost;
        private readonly CooldownTracker _cooldowns;
        private readonly SkillDefCache _defs;
        private readonly IPowerHost _powerHost;
        private readonly IUnitAccess _units;
        private readonly ICombatHost _combatHost;
        private readonly IStatHost _statHost;
        private readonly SpellModResolver _spellMods;
        private readonly IEffectExtension? _extension;
        private readonly IProjectileSpawner? _projectileSpawner;
        private readonly IWeaponDamageQuery? _weaponDamageQuery;
        private readonly ISkillDiagnostics _diagnostics;
        private readonly ProcHost.TriggerCastCallback _triggerCast;
        private readonly Action<Id, Id, Id?, double> _interrupt;
        private readonly Action<Id, Id> _learnSkill;

        /// <summary>T-N3-3（ADR-0031 决策 2/10）：<c>weapon_damage_pct</c> 分支解析
        /// <see cref="SkillOptions.BudgetRuleId"/> 所需——只在该分支使用，其余全部分支不读本字段。
        /// <c>null</c>（旧十五参数兼容构造函数、未显式传入）时按 <c>new SkillOptions().BudgetRuleId</c>
        /// 缺省值处理（见 <see cref="ResolveBeatSeconds"/>），不是"功能关闭"——旧调用方不会得到
        /// 任何行为变化以外的降级，只是无法自定义 <c>BudgetRuleId</c>。</summary>
        private readonly SkillOptions? _options;

        /// <summary>T-N3-7（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md)
        /// 决策 4；06 第 3.3 节 2026-09-14 修订段"周期效果动态计算，不做快照……对象已被移除则冻结为
        /// 最后一次算出的每跳值"）：周期性效果（<c>periodic_damage</c>/<c>periodic_heal</c>）"最后
        /// 一次来源仍注册时算出的完整效果值"缓存——键为 (光环实例 id, 效果原语类型, 学派)，值为
        /// <see cref="ApplyDamageOrHeal"/> 算出的 <c>value</c>（<c>base_value</c> + Σscaling，
        /// SpellMod 应用之前）。只在来源仍注册（<see cref="IStatHost.IsRegistered"/>）时写入；来源
        /// 已注销时优先读本缓存而不重算（见该方法判断记录 C02 修订）。
        /// <para>
        /// 判断记录（缓存物理位置选在本类型，不是 <c>Core.Rules.Skill.AuraHost.AuraInstanceState</c>）：
        /// 06 原文"光环实例为此缓存最后一跳值"字面上把归属写在光环实例上，但"base + Σscaling"这条
        /// 公式的唯一权威实现是 <see cref="ApplyDamageOrHeal"/> 本身——如果缓存写入放在
        /// <c>AuraHost.FirePeriodic</c>，该方法就需要独立重算一遍同一公式才能算出要缓存的值，
        /// 两处分别维护同一公式，日后任一处改动都可能悄悄产生分歧。缓存键含光环实例 id 已经把值
        /// 锁定到具体的光环实例，逻辑上仍是"光环实例的缓存"，只是不借助 <c>AuraInstanceState</c>
        /// 存储；这样只需扩大本类型内部状态（无需改动 <see cref="Core.Rules.Common.EffectContext"/>/
        /// <see cref="IEffectSink"/> 任何公开契约）即可实现，符合硬性规则"ABI 只允许新增"。
        /// </para>
        /// <para>
        /// 不做生命周期清理：光环实例移除后本字典的对应条目不会被主动删除。<c>AuraHost._seq</c>
        /// 单调递增生成实例 id、不重用，残留条目不会造成"读到另一个实例的值"这类错误，只是随进程
        /// 生命周期内出现过的光环实例总数线性增长——单次游戏会话的总量级可接受，不引入无界增长
        /// 风险（同本模块其余"不主动清理、靠不重用 id 保证正确性"的既有惯例）。
        /// </para>
        /// </summary>
        private readonly Dictionary<(Id AuraInstanceId, EffectKind Kind, Id School), double> _lastPeriodicEffectValue =
            new Dictionary<(Id AuraInstanceId, EffectKind Kind, Id School), double>();

        /// <summary>
        /// ADR-0026《技能位移的连续模式》：<c>move</c> 效果原语 <c>motion: continuous</c> 分支的
        /// 依赖倒置出口，由 <c>Core.Carriers.Assembly.CarriersAssembly</c> 在装配期经
        /// <see cref="Core.Rules.Skill.SkillHost.DisplacementSink"/> 这个新增可写属性注入（不是
        /// 构造函数参数——见该属性判断记录"ABI 安全：新增属性而非新增构造参数，避免改动本类型/
        /// <see cref="SkillHost"/>/<see cref="Core.Rules.Assembly.RulesAssembly"/>/
        /// <see cref="Core.Carriers.Assembly.CarriersAssembly"/> 任何一处既有构造函数的物理签名"）。
        /// 未注入（<c>null</c>，典型场景：只装配 <c>core/rules</c> 不装配 <c>core/carriers</c> 的纯
        /// L2 测试/集成）时 <see cref="ApplyMove"/> 的 <c>motion: continuous</c> 分支退化为直接按算出
        /// 的终点整体 <c>SetPosition</c>（同 <c>instant</c> 语义，只是跳过逐 tick 推进/碰撞裁决），
        /// 并记一条警告，不抛异常（惯例同 <see cref="ApplyProjectile"/> 对未注入
        /// <see cref="IProjectileSpawner"/> 的既有降级）。
        /// </summary>
        public IControlledDisplacementSink? DisplacementSink { get; set; }

        /// <summary>
        /// T-N3-3 新增 <paramref name="skillOptions"/>（ABI 安全：新增重载而非在既有物理签名上加
        /// 参数，惯例同 <see cref="CastPipeline"/> 十二/十三参数构造函数判断记录——已编译的旧调用方
        /// 若省略本参数，物理上绑定的是下方 <see cref="Obsolete"/> 标注的十五参数兼容重载，不会因为
        /// 本次改动抛 <c>MissingMethodException</c>）。<paramref name="skillOptions"/> 为 <c>null</c>
        /// 时（含旧重载转发）<c>weapon_damage_pct</c> 分支按 <c>new SkillOptions().BudgetRuleId</c>
        /// 缺省值解析一拍常数（见 <see cref="ResolveBeatSeconds"/>），其余全部行为不受影响。
        /// </summary>
        public EffectDispatcher(
            AuraHost auraHost,
            CooldownTracker cooldowns,
            SkillDefCache defs,
            IPowerHost powerHost,
            IUnitAccess units,
            ICombatHost combatHost,
            IStatHost statHost,
            SpellModResolver spellMods,
            IEffectExtension? extension,
            ISkillDiagnostics diagnostics,
            ProcHost.TriggerCastCallback triggerCast,
            Action<Id, Id, Id?, double> interrupt,
            Action<Id, Id> learnSkill,
            IProjectileSpawner? projectileSpawner,
            IWeaponDamageQuery? weaponDamageQuery,
            SkillOptions? skillOptions)
        {
            _auraHost = auraHost ?? throw new ArgumentNullException(nameof(auraHost));
            _cooldowns = cooldowns ?? throw new ArgumentNullException(nameof(cooldowns));
            _defs = defs ?? throw new ArgumentNullException(nameof(defs));
            _powerHost = powerHost ?? throw new ArgumentNullException(nameof(powerHost));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _combatHost = combatHost ?? throw new ArgumentNullException(nameof(combatHost));
            _statHost = statHost ?? throw new ArgumentNullException(nameof(statHost));
            _spellMods = spellMods ?? throw new ArgumentNullException(nameof(spellMods));
            _extension = extension;
            _projectileSpawner = projectileSpawner;
            _weaponDamageQuery = weaponDamageQuery;
            _options = skillOptions;
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _triggerCast = triggerCast ?? throw new ArgumentNullException(nameof(triggerCast));
            _interrupt = interrupt ?? throw new ArgumentNullException(nameof(interrupt));
            _learnSkill = learnSkill ?? throw new ArgumentNullException(nameof(learnSkill));
        }

        /// <summary>
        /// ABI 兼容 façade（T-N3-3 补充 <see cref="SkillOptions"/> 参数之前的物理十五参数构造签名，
        /// 同 <see cref="CastPipeline"/> 十二/十三参数构造函数判断记录同一套推导）：本重载最后两个
        /// 参数（<c>projectileSpawner</c>/<c>weaponDamageQuery</c>）均带默认值，与上方主构造函数
        /// （15 个不带默认值的参数 + <c>skillOptions</c> 恰好第 16 个）参数个数不重叠时精确匹配本
        /// 重载，恰好传 16 个参数时精确匹配主构造函数，互不冲突，保证已编译好、以"省略
        /// projectileSpawner/weaponDamageQuery/skillOptions 中若干个"方式调用本构造函数的既有二进制
        /// 消费方不需要重新编译。<c>skillOptions</c> 固定传 <c>null</c>——旧调用方不会得到自定义
        /// <see cref="SkillOptions.BudgetRuleId"/> 的能力，<c>weapon_damage_pct</c> 分支对这类实例
        /// 按缺省 <c>BudgetRuleId</c> 解析一拍常数（同未注入 <see cref="IWeaponDamageQuery"/> 的既有
        /// 降级惯例），其余行为与本重载补充之前完全一致。
        /// </summary>
        [Obsolete("T-N3-3 之前的十五参数构造签名，仅为源码/二进制兼容保留；新代码请使用带 skillOptions 的十六参数构造函数。")]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public EffectDispatcher(
            AuraHost auraHost,
            CooldownTracker cooldowns,
            SkillDefCache defs,
            IPowerHost powerHost,
            IUnitAccess units,
            ICombatHost combatHost,
            IStatHost statHost,
            SpellModResolver spellMods,
            IEffectExtension? extension,
            ISkillDiagnostics diagnostics,
            ProcHost.TriggerCastCallback triggerCast,
            Action<Id, Id, Id?, double> interrupt,
            Action<Id, Id> learnSkill,
            IProjectileSpawner? projectileSpawner = null,
            IWeaponDamageQuery? weaponDamageQuery = null)
            : this(auraHost, cooldowns, defs, powerHost, units, combatHost, statHost, spellMods,
                extension, diagnostics, triggerCast, interrupt, learnSkill,
                projectileSpawner, weaponDamageQuery, skillOptions: null)
        {
        }

        /// <summary>
        /// 判断记录（效果免疫统一门，2026-09-10 根治"消费方反馈-2026-09-10-打断免疫"）：06 第 3.3
        /// 节 <c>AuraDef.immunity</c>"对指定学派/效果类型免疫"与 07 第 2.1 节
        /// <c>CreatureUnit.Immunities</c> 的 <c>effect.&lt;kind&gt;</c> 写法均按
        /// (<c>school</c>, <see cref="EffectKind"/>) 泛化定义、不限定于
        /// <c>school_damage</c>/<c>heal</c>（见 <see cref="IStaticImmunityProvider"/>/
        /// <c>CreatureImmunityProvider</c> 类型注释）——此前只有 <see cref="ApplyDamageOrHeal"/>
        /// 一处落地了这份语义，<c>interrupt</c>/<c>dispel</c>/<c>energize</c>/<c>teleport</c>/
        /// <c>move</c> 等其余分支从未查询过免疫（<see cref="AuraHost.IsImmune"/> 内部已合并静态
        /// <see cref="IStaticImmunityProvider"/> 查询，见该方法实现），导致打断免疫等动态光环/静态
        /// 标记对这些效果原语完全不生效（外部消费方 M-C06 前置核验复现）。改为在分发入口统一判定
        /// 并短路（不进入任何具体分支、不产生该效果的事件/状态变化，含 <c>interrupt</c> 免疫时不
        /// 触发学派锁定——被拦截效果的全部后续影响都不该发生），<see cref="ApplyDamageOrHeal"/>
        /// 内原有的同一判定随之收口到这里，不再重复查询、不产生两条 immune 记录。
        /// <para>
        /// 例外：<see cref="EffectKind.ApplyAura"/> 不纳入本统一判定——对"施加一个光环实例"这个
        /// 操作本身的免疫是另一层语义（免疫的是"获得光环"这件事本身，不是光环生效后内部某个子
        /// 效果），06 未就此拍板；且按 <see cref="AuraHost.IsImmune"/> 既有规则"未声明
        /// <c>effect_kinds</c> 时只看学派、不挑 kind"，若在这里也拦截 <c>apply_aura</c>，会让任何
        /// 只声明 <c>schools</c> 的免疫光环（如"火免疫"）意外连带挡住该学派下全部增益光环的施加
        /// （含友方治疗类光环），超出本次打断免疫缺口修复的范围。<c>apply_aura</c> 的控制类光环
        /// 免疫仍按既有实现在 <see cref="AuraHost.ApplyAura"/> 内部逐条判定
        /// （<see cref="IStaticImmunityProvider.GetControlImmunity"/> 只剔除本次施加的控制标志位，
        /// 不影响光环其余子效果落地），不在这里重复判定。
        /// </para>
        /// </summary>
        public ResolveResult ApplyEffect(EffectContext context)
        {
            if (context.Kind != EffectKind.ApplyAura && _auraHost.IsImmune(context.TargetId, context.School, context.Kind))
            {
                return new ResolveResult(HitResult.Miss, 0, 0, 0, immune: true, isHeal: context.Kind == EffectKind.Heal);
            }

            switch (context.Kind)
            {
                case EffectKind.SchoolDamage:
                case EffectKind.WeaponDamagePct:
                case EffectKind.Heal:
                    return ApplyDamageOrHeal(context);

                case EffectKind.ApplyAura:
                    return ApplyAuraEffectPrimitive(context);

                case EffectKind.Dispel:
                    return ApplyDispel(context);

                case EffectKind.Energize:
                    return ApplyEnergize(context);

                case EffectKind.TriggerSpell:
                    return ApplyTriggerSpell(context);

                case EffectKind.ModifyCooldown:
                    return ApplyModifyCooldown(context);

                case EffectKind.AddCharge:
                    return ApplyAddCharge(context);

                case EffectKind.Interrupt:
                    return ApplyInterrupt(context);

                case EffectKind.Teleport:
                    return ApplyTeleport(context);

                case EffectKind.Move:
                    return ApplyMove(context);

                case EffectKind.LearnSkill:
                    return ApplyLearnSkill(context);

                case EffectKind.Projectile:
                    return ApplyProjectile(context);

                case EffectKind.Summon:
                case EffectKind.OpenLock:
                case EffectKind.CreateItem:
                case EffectKind.SetWorldFlag:
                case EffectKind.Script:
                    return ApplyExtension(context);

                default:
                    throw new InvalidOperationException($"未知 EffectKind：{context.Kind}");
            }
        }

        // 判断记录：本方法实现 IEffectSink.ApplyAura（接口签名不带 tags，见该接口判断记录），
        // 恒不传标签（AuraHost.ApplyAura 的 tags 参数缺省 null）——真正携带标签的施加路径是
        // ApplyAuraEffectPrimitive 直接调用 AuraHost.ApplyAura 的重载，不经过本方法。
        public AuraInstanceRef ApplyAura(Id targetId, Id auraDefId, Id sourceId, double? durationOverride = null) =>
            _auraHost.ApplyAura(targetId, auraDefId, sourceId, durationOverride);

        public void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef) => _auraHost.RemoveAura(targetId, auraInstanceRef);

        /// <summary>T-N3-7 补（见 <see cref="IEffectSink.ForgetPeriodicCache"/> 判断记录）：从
        /// <see cref="_lastPeriodicEffectValue"/> 中移除属于 <paramref name="auraInstanceId"/> 的
        /// 全部缓存条目——缓存键是 (光环实例 id, 效果原语类型, 学派) 三元组，一个光环实例最多只对应
        /// 该实例定义里声明的周期效果条目数（现实内容通常 1～2 条），逐一扫描全部缓存键、按
        /// 光环实例 id 过滤后删除，即使在缓存较大时单次调用的扫描成本也只随"当前存活的周期光环
        /// 实例数量"线性增长（随每次实例移除持续收口，不会无界累积），不引入额外的按实例 id 二级
        /// 索引结构（当前规模没有必要，保持实现简单）。<paramref name="auraInstanceId"/> 未出现在
        /// 缓存里（该光环从未成功进行过一次来源仍注册的周期结算，或本来就不含周期效果）时是安全的
        /// 空操作。</summary>
        public void ForgetPeriodicCache(Id auraInstanceId)
        {
            if (_lastPeriodicEffectValue.Count == 0)
            {
                return;
            }

            List<(Id AuraInstanceId, EffectKind Kind, Id School)>? toRemove = null;
            foreach (var key in _lastPeriodicEffectValue.Keys)
            {
                if (key.AuraInstanceId.Equals(auraInstanceId))
                {
                    (toRemove ??= new List<(Id, EffectKind, Id)>()).Add(key);
                }
            }

            if (toRemove == null)
            {
                return;
            }

            foreach (var key in toRemove)
            {
                _lastPeriodicEffectValue.Remove(key);
            }
        }

        /// <summary>T-N3-7 补（见 <see cref="IEffectSink.ClearPeriodicCache"/> 判断记录）：清空全部
        /// 冻结缓存条目，不区分光环实例。</summary>
        public void ClearPeriodicCache() => _lastPeriodicEffectValue.Clear();

        /// <summary>T-N3-7 补：只读暴露 <see cref="_lastPeriodicEffectValue"/> 的条目数，供测试断言
        /// "光环实例移除后缓存条目确实被清理"（不必反射私有字段）。生产代码不消费本属性（缓存本身
        /// 是纯粹的内部实现细节，不构成任何行为契约）。</summary>
        public int PeriodicCacheCount => _lastPeriodicEffectValue.Count;

        // -----------------------------------------------------------------
        // school_damage / weapon_damage_pct / heal
        // -----------------------------------------------------------------

        // 判断记录：免疫判定已收口到 ApplyEffect 分发入口（见该方法判断记录"效果免疫统一门"），本
        // 方法不再重复查询 _auraHost.IsImmune——免疫命中时 ApplyEffect 已提前返回，不会走到这里。
        private ResolveResult ApplyDamageOrHeal(EffectContext context)
        {
            double value;
            double coefficient = context.Coefficient;

            if (context.Kind == EffectKind.WeaponDamagePct)
            {
                // T-N3-3（ADR-0031 决策 1/2；06 第 3.2 节 2026-09-14 修订段"weapon_damage_pct 改为
                // 基于武器秒伤 × 一拍常数 × 百分比而不是基于单次武器伤害"）：不再读取
                // IWeaponDamageQuery.GetWeaponBaseDamage（damage_min/damage_max 均值，硬性规则
                // "禁止在效果里读武器单次伤害"），改经 GetWeaponDps 取武器秒伤（item.weapon_dps_curve
                // (item_level) × 品质预算倍率 × 武器槽位系数，T-N2-6，无武器为 0，见该方法判断记录）
                // 再乘以一拍常数（ResolveBeatSeconds，来自 skill.budget_rule.beat_seconds）与 pct。
                // GetWeaponBaseDamage 方法本身保留、签名不变（硬性规则"该方法本身保留供其它消费方"），
                // 只是本分支不再调用它。
                var pct = ParamsX.GetNumber(context.Params, "pct", context.BaseValue);
                var weaponDps = _weaponDamageQuery?.GetWeaponDps(context.SourceId) ?? 0.0;
                var beatSeconds = ResolveBeatSeconds();
                value = weaponDps * beatSeconds * pct;
            }
            else
            {
                // T-N3-7（ADR-0031 决策 4；06 第 3.3 节 2026-09-14 修订段"周期效果动态计算，不做
                // 快照……对象已被移除则冻结为最后一次算出的每跳值"）：只有周期性效果（携带
                // AuraInstanceId）在来源已注销、且已有缓存值时才走冻结分支——直接复用缓存，完全
                // 跳过下面 base_curve_ref/scaling 的重新计算（不查询 _units/_statHost，值与来源
                // 是否还活着无关）。非周期效果（AuraInstanceId 为 null）与"周期效果但尚无缓存"
                // （见下方冷启动分支）都落入 else 分支正常计算。
                var isPeriodicAura = context.IsPeriodic && context.AuraInstanceId.HasValue;
                var periodicCacheKey = isPeriodicAura
                    ? (context.AuraInstanceId!.Value, context.Kind, context.School)
                    : default;
                var sourceRegistered = _statHost.IsRegistered(context.SourceId);

                if (isPeriodicAura && !sourceRegistered &&
                    _lastPeriodicEffectValue.TryGetValue(periodicCacheKey, out var frozenValue))
                {
                    value = frozenValue;
                    // coefficient 字段本身不参与冻结值的计算（frozenValue 已是 base_value+scaling
                    // 的最终结果），只用于下面重建 outbound 上下文时原样回填——仍按 params 声明的
                    // 原始值回填，保持与"来源仍在"分支同一字段语义，不因走了冻结分支就退化成 0。
                    coefficient = ParamsX.GetNumber(context.Params, "coefficient", context.Coefficient);
                }
                else
                {
                    // T-N3-2（ADR-0031 决策 1"基础值可选引用等级曲线（base_curve_ref），默认为零"；
                    // 06 第 3.2 节 2026-09-14 修订段）：base_curve_ref 存在且能解析出曲线时取代
                    // base_value——按施法者当前等级在 skill.base_curve 上取值，每次结算都重新查询
                    // （同 06 第 3.3 节周期效果"动态计算，不做快照"惯例，不缓存某一时刻的等级——
                    // 这里的"不缓存"指来源仍存在这条主路径；来源已注销后的冻结走上面的分支，两者
                    // 不冲突）。来源单位已不存在时按等级 1 处理，不抛异常（同 Core.Rules.Combat.
                    // Resolver.ResolveEffectiveLevel 判断记录"已从世界移除时按等级 1 处理"同一
                    // 防御姿态，只在"周期效果尚无冻结缓存"这一冷启动边界才会用到）；base_curve_ref
                    // 引用的曲线未注册/不存在（表未注册，见 SkillDefCache.TryGetBaseCurve 判断
                    // 记录）时静默回退 base_value，不阻断结算——加载期 reference_integrity 已经会
                    // 拦下"引用不存在的曲线"这类内容错误，运行期这里只是防御性兜底。
                    var baseCurveRef = ParamsX.GetIdOpt(context.Params, "base_curve_ref");
                    double baseValue;
                    if (baseCurveRef.HasValue && _defs.TryGetBaseCurve(baseCurveRef.Value, out var baseCurve))
                    {
                        var casterLevel = _units.Exists(context.SourceId) ? _units.GetLevel(context.SourceId) : 1;
                        baseValue = baseCurve.Evaluate(casterLevel);
                    }
                    else
                    {
                        baseValue = ParamsX.GetNumber(context.Params, "base_value", context.BaseValue);
                    }

                    coefficient = ParamsX.GetNumber(context.Params, "coefficient", context.Coefficient);

                    // C02 收口（外部审计 7e63d66 第四轮；T-N3-7 修订，见 06 第 3.3 节 2026-09-14
                    // 修订段）：周期性效果（periodic_damage/periodic_heal）的 EffectContext.SourceId
                    // 恒是施加光环时的施法者（AuraHost.FirePeriodic 每次都用 instance.SourceId
                    // 重建上下文），真实 CreatureFactory.Despawn 会同步注销来源的 IStatHost 注册
                    // （见 CreatureFactory.Despawn），但光环实例只在"目标"被销毁时才由
                    // AuraHost.OnEntityDestroyed 摘除（判断记录：来源销毁不代表已施加到其他目标身上
                    // 的光环应当消失，06 未规定这种情形）——来源销毁后光环仍会继续按周期结算，此前
                    // 无条件调用 _statHost.GetStat(context.SourceId, ...) 会因来源已注销直接抛
                    // InvalidOperationException（外部审计复现：真实 Despawn 施法者后下一次周期 tick）。
                    // <para>
                    // 判断记录（T-N3-7 修订，取代此前"降级为无缩放，不做快照冻结"的判断）：06 原文
                    // 已在 2026-09-14 明确"对象已被移除则冻结为最后一次算出的每跳值"，不再是"未规定"
                    // 的边界——本方法只在这里（周期效果、来源确已缺失、且完全没有任何一次成功缓存过
                    // 的情形，即施加光环与第一跳之间来源就已经消失，见上方 <see
                    // cref="_lastPeriodicEffectValue"/> 判断记录）保留"缩放贡献按 0、只保留
                    // base_value"这条兜底：06 未规定"来源在施加与第一跳之间消失"这一更细的边界，本
                    // 兜底是临时判断（待设计层确认），取"确定安全"的退化路径而不是让本方法反过来
                    // 在这里重新实现一遍 AuraHost.ApplyAura 时点的"求值并缓存初值"逻辑（那样会把同一
                    // 条公式的权威实现拆成两处，见 <see cref="_lastPeriodicEffectValue"/> 判断记录）。
                    // 这一支算出的值随后仍会被下面"缓存"一并写入，从下一跳起就会命中冻结分支，不会
                    // 每跳都重新退化一次。非周期效果的 SourceId 通常是"正在执行的施法者"，理论上不会
                    // 遇到未注册的情形，本次改动同时覆盖它是为了不在"什么时候会未注册"这件事上做
                    // 额外的路径区分（非周期效果没有 AuraInstanceId，不写入 <see
                    // cref="_lastPeriodicEffectValue"/>，也就没有冻结分支可言，每次都会落到这里）。
                    // </para>
                    //
                    // T-N3-2（ADR-0031 决策 1；06 第 3.2 节 2026-09-14 修订段）：scaling 列表优先——
                    // 存在时对每一项取 coefficient × 来源属性最终值求和；列表缺失（含尚未经 1→2 迁移的
                    // 旧数据）时回退旧单字段 scaling_stat/顶层 coefficient 读取路径（硬性规则"禁止删除
                    // 旧 scaling_stat 读取路径"）。两条路径互斥、不叠加——声明了 scaling 列表就不再读
                    // scaling_stat；两条路径共享同一条"来源未注册按 0 处理"降级规则（C02 判断记录，
                    // 仅周期效果冷启动、以及理论上不会命中的非周期效果兜底才会触发）。
                    double scalingContribution = 0;
                    var scalingEntries = ParamsX.GetObjectArray(context.Params, "scaling");
                    if (scalingEntries.Count > 0)
                    {
                        if (sourceRegistered)
                        {
                            foreach (var entry in scalingEntries)
                            {
                                var entryStat = ParamsX.GetIdOpt(entry, "stat");
                                if (!entryStat.HasValue) continue;
                                var entryCoefficient = ParamsX.GetNumber(entry, "coefficient", 0);
                                scalingContribution += entryCoefficient * _statHost.GetStat(context.SourceId, entryStat.Value);
                            }
                        }
                        else
                        {
                            _diagnostics.Warn(
                                $"效果的来源 \"{context.SourceId}\" 未注册（很可能已被销毁），" +
                                $"scaling 列表的缩放贡献已按 0 处理（见 EffectDispatcher.ApplyDamageOrHeal 判断记录 C02）");
                        }
                    }
                    else
                    {
                        var scalingStat = ParamsX.GetIdOpt(context.Params, "scaling_stat");
                        if (scalingStat.HasValue)
                        {
                            if (sourceRegistered)
                            {
                                scalingContribution = coefficient * _statHost.GetStat(context.SourceId, scalingStat.Value);
                            }
                            else
                            {
                                _diagnostics.Warn(
                                    $"效果的来源 \"{context.SourceId}\" 未注册（很可能已被销毁），" +
                                    $"scaling_stat \"{scalingStat.Value}\" 的缩放贡献已按 0 处理（见 EffectDispatcher.ApplyDamageOrHeal 判断记录 C02）");
                            }
                        }
                    }

                    value = baseValue + scalingContribution;

                    // T-N3-7：只有来源仍注册时才写入/刷新缓存——来源已注销的冷启动分支算出的
                    // "只剩 base_value"的值不应该被当作"来源存活时的最后一跳值"缓存下来，否则会把
                    // 一个从未真正观测到过的"来源存活"状态错误地固化为"冻结值"的定义（虽然数值上
                    // 与不缓存时后续每次冷启动重算的结果相同，但语义上不应该混为一谈——见上方冷
                    // 启动判断记录，那条兜底的分支保持"未缓存、每次都重算"，只是恰好每次结果一致）。
                    if (isPeriodicAura && sourceRegistered)
                    {
                        _lastPeriodicEffectValue[periodicCacheKey] = value;
                    }
                }
            }

            // 契约缺口已补齐：EffectContext.Tags 携带技能标签集合（来自 skill.def.tags），
            // effect_value/crit_chance 维度的 SpellMod 过滤现在按学派/技能 id/标签三个维度一并
            // 匹配（见 SkillFilter.Matches）。CastPipeline.ExecuteEffectsOnly 构造 EffectContext
            // 时会传入 def.Tags；周期性光环效果（AuraHost.FirePeriodic）W1 收边补齐后也会带上
            // 施加该光环时传入的标签（见 AuraInstanceState.Tags/IEffectSink.ApplyAura 的 tags
            // 参数），不再恒为空——只有调用方本身没传标签（如种族被动光环、装备 grants.auras）时
            // 才退化为原先"按学派/技能 id"匹配的行为。
            value = _spellMods.Apply(context.SourceId, SpellModDimension.EffectValue, context.SkillId, context.School, context.Tags, value);

            var critBonus = _spellMods.Apply(context.SourceId, SpellModDimension.CritChance, context.SkillId, context.School, context.Tags, 0);
            var mergedParams = critBonus != 0 ? ParamsX.MergeNumber(context.Params, "crit_chance_bonus", critBonus) : context.Params;

            // 攻击实例 id 遗留根治（architecture/落地计划/audit-3224ca1-20260908/AUDIT_REPORT.md）：
            // 本方法重建 outbound 上下文时（应用 SpellMod 后的最终数值），必须与 TriggerChainDepth
            // 一样原样转发 context.AttackInstanceId，否则 CastPipeline.ExecuteEffectsOnly 构造时
            // 携带的攻击实例 id 会在这里被静默丢弃，永远到不了 Resolver.Resolve 与落地事件——见
            // EffectContext.AttackInstanceId 判断记录。
            //
            // T-N1-6（ADR-0030 决策 5）同一条惯例：必须原样转发 context.SourceKind，不重新经
            // IUnitAccess.GetSourceKind 查询——本方法收到的 context 已经是 CastPipeline/AuraHost/
            // ProjectileHost 三个生产构造点之一按各自来源正确查询过的结果，在这里重新查询不但多余，
            // 若 context.SourceId 与"真正的来源单位"不一致（本方法不持有这层业务知识，只是原样转发
            // 已经算好的值）还会算出错误结果；不转发则会让 06 第 4.1 节"目标乘区"步骤读到的
            // sourceKind 静默退化为 SourceKind.Unknown（T-N1-6 风险段点名的"漏一处默认值"正是这类
            // 遗漏）。GroundPoint 字段本任务不改动其既有转发行为（该字段结算管线本身不消费，见其
            // 判断记录，且本方法此前就不转发它，非本任务改动范围）。
            var outbound = new EffectContext(
                context.SourceId, context.TargetId, context.SkillId, context.Kind, context.School,
                value, coefficient, mergedParams, context.AuraInstanceId, context.IsPeriodic, context.CanCrit, context.CanMiss,
                context.Tags, context.TriggerChainDepth, context.AttackInstanceId, groundPoint: null, sourceKind: context.SourceKind);

            return _combatHost.ResolveEffect(outbound);
        }

        /// <summary>T-N3-3（ADR-0031 决策 2/10；06 第 3.2/3.10 节）：解析
        /// <c>weapon_damage_pct</c> 分支需要的一拍常数——按 <see cref="_options"/>（<c>null</c> 时
        /// 视同缺省 <see cref="SkillOptions"/>，见该字段判断记录）的 <see cref="SkillOptions.BudgetRuleId"/>
        /// 查 <see cref="SkillDefCache.TryGetBeatSeconds"/>；表未注册或该 id 没有记录时按缺省 1.0
        /// 处理并记一条警告（不阻断，同 <see cref="ApplyMove"/> 对未注入
        /// <see cref="IControlledDisplacementSink"/> 的既有"降级 + 警告"惯例）——1.0 恰好是"改乘一拍
        /// 常数之前"的等效行为（乘 1 不改变结果），保证 <c>skill.budget_rule</c> 尚未落地完整数据的
        /// 项目不会因为本次改动出现结算异常。</summary>
        /// <summary>与 <see cref="SkillOptions.BudgetRuleId"/> 默认值字面量一致（判断记录：不用
        /// <c>new SkillOptions().BudgetRuleId</c> 每次结算都分配一个临时实例——weapon_damage_pct
        /// 在战斗中可能高频结算，这里直接复制字面量，两处字面量须保持同步，已在双方注释互相
        /// 交叉引用）。</summary>
        private static readonly Id DefaultBudgetRuleId = new Id("skill.budget_rule.default");

        private double ResolveBeatSeconds()
        {
            var budgetRuleId = _options?.BudgetRuleId ?? DefaultBudgetRuleId;
            if (_defs.TryGetBeatSeconds(budgetRuleId, out var beatSeconds))
            {
                return beatSeconds;
            }

            _diagnostics.Warn(
                $"skill.budget_rule \"{budgetRuleId}\" 未找到记录（表未注册或该 id 没有对应记录），" +
                $"weapon_damage_pct 的一拍常数按缺省 1.0 处理（见 EffectDispatcher.ResolveBeatSeconds 判断记录，ADR-0031 决策 2/10）");
            return 1.0;
        }

        // -----------------------------------------------------------------
        // apply_aura / dispel / energize
        // -----------------------------------------------------------------

        private ResolveResult ApplyAuraEffectPrimitive(EffectContext context)
        {
            var auraDefId = ParamsX.GetId(context.Params, "aura_def", default);
            double? durationOverride = context.Params.ContainsKey("duration_override")
                ? ParamsX.GetNumber(context.Params, "duration_override")
                : (double?)null;

            // W1 收边补齐（A3 审计 #9）：把触发本次 apply_aura 的 EffectContext.Tags（源自
            // skill.def.tags，见 CastPipeline.ExecuteEffectsOnly）转发给 AuraHost，供后续周期效果
            // 结算按标签过滤 SpellMod（此前恒不传，见 AuraHost.FirePeriodic/AuraInstanceState.Tags）。
            _auraHost.ApplyAura(context.TargetId, auraDefId, context.SourceId, durationOverride, context.Tags, context.TriggerChainDepth);
            return NoOp(context);
        }

        private ResolveResult ApplyDispel(EffectContext context)
        {
            var category = ParamsX.GetId(context.Params, "category", default);
            var count = (int)ParamsX.GetNumber(context.Params, "count", 1);
            // N04 收边补齐：把本次结算的 TriggerChainDepth 传给 AuraHost.Dispel，使 dispel 产生的
            // aura.removed 携带正确深度，见 AuraRemovedEvent 类型注释。
            _auraHost.Dispel(context.TargetId, category, count, context.TriggerChainDepth);
            return NoOp(context);
        }

        private ResolveResult ApplyEnergize(EffectContext context)
        {
            var powerType = ParamsX.GetId(context.Params, "power_type", default);
            var amount = ParamsX.GetNumber(context.Params, "amount");
            _powerHost.ModifyPower(context.TargetId, powerType, amount, context.SourceId);
            return NoOp(context);
        }

        // -----------------------------------------------------------------
        // trigger_spell / modify_cooldown / add_charge / interrupt
        // -----------------------------------------------------------------

        private ResolveResult ApplyTriggerSpell(EffectContext context)
        {
            var skillId = ParamsX.GetId(context.Params, "skill_id", default);
            // RC-01 收边补齐：传入当前效果上下文自身的触发链深度（同步嵌套调用，见
            // ProcHost.TriggerCastCallback 类型注释——trigger_spell 是直接方法调用，深度就是
            // "正在执行的这一层"，由 CastPipeline.TriggerCast 校验后 +1 向下传播）。
            _triggerCast(context.SourceId, skillId, new[] { context.TargetId }, context.TriggerChainDepth);
            return NoOp(context);
        }

        private ResolveResult ApplyModifyCooldown(EffectContext context)
        {
            var skillId = ParamsX.GetIdOpt(context.Params, "skill_id");
            var category = ParamsX.GetIdOpt(context.Params, "category");
            var delta = ParamsX.GetNumber(context.Params, "delta");

            if (skillId.HasValue)
            {
                _cooldowns.ModifyCooldown(context.TargetId, skillId.Value, delta, isCategory: false);
            }
            else if (category.HasValue)
            {
                _cooldowns.ModifyCooldown(context.TargetId, category.Value, delta, isCategory: true);
            }
            else
            {
                _diagnostics.Warn("modify_cooldown 效果缺少 skill_id/category 参数，已忽略");
            }

            return NoOp(context);
        }

        private ResolveResult ApplyAddCharge(EffectContext context)
        {
            var skillId = ParamsX.GetId(context.Params, "skill_id", default);
            var amount = (int)ParamsX.GetNumber(context.Params, "amount", 1);

            if (_defs.TryGetSkillDef(skillId, out var def))
            {
                _cooldowns.AddCharge(context.TargetId, def, amount);
            }
            else
            {
                _diagnostics.Warn($"add_charge 效果引用的技能 \"{skillId}\" 不存在，已忽略");
            }

            return NoOp(context);
        }

        private ResolveResult ApplyInterrupt(EffectContext context)
        {
            var lockSchool = ParamsX.GetIdOpt(context.Params, "lock_school");
            var lockDuration = ParamsX.GetNumber(context.Params, "lock_duration");
            _interrupt(context.TargetId, context.SourceId, lockSchool, lockDuration);
            return NoOp(context);
        }

        // -----------------------------------------------------------------
        // move / teleport / learn_skill
        // -----------------------------------------------------------------

        private ResolveResult ApplyTeleport(EffectContext context)
        {
            var point = ParamsX.GetVec2(context.Params, "point", _units.GetPosition(context.TargetId));
            _units.SetPosition(context.TargetId, point);
            return NoOp(context);
        }

        /// <summary>
        /// <c>move</c> 效果原语（见 06 第 3.2 节"子类型 charge|leap|knockback"）。判断记录：本模块
        /// 只写目标最终位置，不做寻路/碰撞——06 原文本节未规定位移的插值/寻路细节，由 L3 移动系统
        /// 在表现层/物理层精化（本模块只保证逻辑位置的最终落点正确，呼应 05 对象模型"移动系统"
        /// 分工）。
        /// <para>
        /// ADR-0026《技能位移的连续模式》：新增可选参数 <c>motion</c>（<c>instant</c>，缺省，本方法
        /// 原有的直接 <c>SetPosition</c> 语义；<c>continuous</c>，转交 <see cref="DisplacementSink"/>
        /// 逐 tick 推进）。判断记录（<c>motion</c> 命名，不复用既有 <c>mode</c> 字段）：本原语早已有
        /// 一个名为 <c>mode</c> 的参数表示子类型（<c>charge|leap|knockback</c>，见下方 switch），若
        /// "瞬移/连续"这一正交维度也叫 <c>mode</c> 会与既有字段撞名、破坏既有数据/测试对 <c>mode</c>
        /// 取值集合 <c>{charge,leap,knockback}</c> 的假设——两个维度正交（子类型决定"移动到哪"，
        /// <c>motion</c> 决定"怎么移过去"），改用 <c>motion</c> 避免命名碰撞，见 ADR-0026 决策记录。
        /// 本分支只负责判别并转发，<see cref="ApplyMove"/> 原有的 <c>instant</c> switch 分支代码
        /// 逐字节未改动（新分支在其之前短路返回）。
        /// </para>
        /// </summary>
        private ResolveResult ApplyMove(EffectContext context)
        {
            var motion = ParamsX.GetString(context.Params, "motion", "instant");
            if (motion == "continuous")
            {
                return ApplyContinuousMove(context);
            }

            var mode = ParamsX.GetString(context.Params, "mode", "charge");

            switch (mode)
            {
                case "leap":
                {
                    var point = ParamsX.GetVec2(context.Params, "point", _units.GetPosition(context.SourceId));
                    _units.SetPosition(context.SourceId, point);
                    break;
                }

                case "knockback":
                {
                    var distance = ParamsX.GetNumber(context.Params, "distance", 5);
                    var from = _units.GetPosition(context.SourceId);
                    var to = _units.GetPosition(context.TargetId);
                    var direction = to - from;
                    var length = direction.Length;
                    var normalized = length > 1e-9 ? direction * (1.0 / length) : new Vec2(1, 0);
                    _units.SetPosition(context.TargetId, to + normalized * distance);
                    break;
                }

                case "charge":
                default:
                {
                    var stopDistance = ParamsX.GetNumber(context.Params, "stop_distance", 1.0);
                    var from = _units.GetPosition(context.SourceId);
                    var to = _units.GetPosition(context.TargetId);
                    var direction = to - from;
                    var length = direction.Length;
                    if (length > stopDistance)
                    {
                        var normalized = direction * (1.0 / length);
                        _units.SetPosition(context.SourceId, to - normalized * stopDistance);
                    }

                    break;
                }
            }

            return NoOp(context);
        }

        /// <summary>
        /// ADR-0026《技能位移的连续模式》：<c>move</c> 效果原语 <c>motion: continuous</c> 分支——按
        /// 与瞬移分支完全相同的 <c>mode</c>（<c>charge|leap|knockback</c>）算出被位移单位与目标点
        /// （逐子类型的几何计算与上面 <see cref="ApplyMove"/> 的 <c>switch</c> 分支各自独立实现，不
        /// 共享代码，保证 <c>instant</c> 分支不受本方法任何改动影响，见 ADR-0026 兼容性 4"无阻挡时
        /// 连续/瞬移终点一致"——两段独立代码算出的目标点在无阻挡场景下逐字节相同，由测试锁定），
        /// 随后不直接 <c>SetPosition</c>，而是组装 <see cref="ControlledDisplacementRequest"/> 交给
        /// <see cref="DisplacementSink"/>（真正逐 tick 推进的是 L3 <c>MovementHost</c>/
        /// <c>MovementTickHandler</c>，见该接口判断记录）。
        /// </summary>
        private ResolveResult ApplyContinuousMove(EffectContext context)
        {
            var mode = ParamsX.GetString(context.Params, "mode", "charge");
            Id movingUnitId;
            Vec2 origin;
            Vec2 target;

            switch (mode)
            {
                case "leap":
                {
                    movingUnitId = context.SourceId;
                    origin = _units.GetPosition(context.SourceId);
                    target = ParamsX.GetVec2(context.Params, "point", origin);
                    break;
                }

                case "knockback":
                {
                    movingUnitId = context.TargetId;
                    var from = _units.GetPosition(context.SourceId);
                    var to = _units.GetPosition(context.TargetId);
                    var distance = ParamsX.GetNumber(context.Params, "distance", 5);
                    var direction = to - from;
                    var length = direction.Length;
                    var normalized = length > 1e-9 ? direction * (1.0 / length) : new Vec2(1, 0);
                    origin = to;
                    target = to + normalized * distance;
                    break;
                }

                case "charge":
                default:
                {
                    movingUnitId = context.SourceId;
                    var from = _units.GetPosition(context.SourceId);
                    var to = _units.GetPosition(context.TargetId);
                    var stopDistance = ParamsX.GetNumber(context.Params, "stop_distance", 1.0);
                    var direction = to - from;
                    var length = direction.Length;
                    origin = from;
                    target = length > stopDistance ? to - direction * (1.0 / length) * stopDistance : from;
                    break;
                }
            }

            var declaredSpeed = ParamsX.GetNumber(context.Params, "speed", 0);
            var distanceTotal = (target - origin).Length;
            double speed;
            if (declaredSpeed > 0)
            {
                speed = declaredSpeed;
            }
            else
            {
                var duration = ParamsX.GetNumber(context.Params, "duration", 0);
                speed = duration > 1e-9 ? distanceTotal / duration : 0;
            }

            if (speed <= 0 || distanceTotal <= 1e-9)
            {
                // 无效速度（既未声明 speed 也未声明可用的 duration）或零距离：no-op，不提交任何
                // 位移请求（同瞬移分支"charge 已在停止距离内"时不动的既有语义）。
                return NoOp(context);
            }

            var blockingText = ParamsX.GetString(context.Params, "blocking", "stop");
            var blocking = blockingText == "revert" ? DisplacementBlockingPolicy.Revert : DisplacementBlockingPolicy.Stop;
            var sampleStep = ParamsX.GetNumber(context.Params, "sample_step", 0);

            if (DisplacementSink == null)
            {
                // 降级（见 DisplacementSink 判断记录）：未装配 L3 受控位移宿主时退化为直接
                // SetPosition，保证"没有 L3 时至少落到与瞬移一致的最终位置"，不静默丢弃这次位移。
                _diagnostics.Warn(
                    "EffectKind.Move motion=continuous 未注入 IControlledDisplacementSink（见 " +
                    "core/rules/common/contracts/IControlledDisplacementSink.cs 判断记录\"依赖倒置\"，" +
                    "通常经 CarriersAssembly 注入），已退化为按目标点直接 SetPosition");
                _units.SetPosition(movingUnitId, target);
                return NoOp(context);
            }

            DisplacementSink.BeginControlledDisplacement(
                new ControlledDisplacementRequest(movingUnitId, origin, target, speed, blocking, sampleStep));
            return NoOp(context);
        }

        private ResolveResult ApplyLearnSkill(EffectContext context)
        {
            var skillId = ParamsX.GetId(context.Params, "skill_id", default);
            _learnSkill(context.TargetId, skillId);
            return NoOp(context);
        }

        // -----------------------------------------------------------------
        // projectile（收边任务补齐：从"未实现/委托六合一扩展点"改为委托 IProjectileSpawner，
        // 见该接口判断记录"依赖倒置"）
        // -----------------------------------------------------------------

        private ResolveResult ApplyProjectile(EffectContext context)
        {
            if (_projectileSpawner != null)
            {
                _projectileSpawner.Spawn(context, this);
                return NoOp(context);
            }

            _diagnostics.Warn(
                "EffectKind.Projectile 未注入 IProjectileSpawner（见 core/rules/common/contracts/" +
                "IProjectileSpawner.cs 判断记录\"依赖倒置\"，通常经 CarriersAssembly 注入），已按未处理返回");
            return NoOp(context);
        }

        // -----------------------------------------------------------------
        // 扩展点：summon / open_lock / create_item / set_world_flag / script
        // -----------------------------------------------------------------

        private ResolveResult ApplyExtension(EffectContext context)
        {
            if (_extension != null && _extension.TryHandle(context, out var result))
            {
                return result;
            }

            _diagnostics.Warn(
                $"EffectKind.{context.Kind} 未被 IEffectExtension 处理（见 core/rules/skill/README.md" +
                " \"五类效果原语委托扩展点\"），已按未处理返回");
            return NoOp(context);
        }

        private static ResolveResult NoOp(EffectContext context) =>
            new ResolveResult(HitResult.Hit, 0, 0, 0, immune: false, isHeal: context.Kind == EffectKind.Heal);
    }
}
