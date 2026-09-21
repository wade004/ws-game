using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// RC-11 收边补齐：<c>weapon_damage_pct</c> 效果原语（06 第 3.2 节"基于当前装备武器伤害的
    /// 百分比造成伤害"）的武器基础伤害查询——依赖倒置接口，惯例同 <see cref="IProjectileSpawner"/>
    /// 判断记录"依赖倒置"：武器数据（<c>item.template.weapon_profile</c>）属于
    /// <c>core/carriers/item</c>（L3），<c>core/rules</c>（L2）不得反向依赖 L3（见 00 架构总则分层
    /// 规则、<c>Core.Rules.csproj</c> 只引用 <c>Core.Numbers</c>），本接口由
    /// <c>core/carriers/item.EquipmentHost</c> 实现，经 <c>Core.Rules.Assembly.
    /// DeferredWeaponDamageQuery</c> 延迟绑定注入 <c>EffectDispatcher</c>（同
    /// <c>DeferredEffectExtension</c>/<c>DeferredAuraQuery</c> 判断记录"A 需要 B、B 的真实实现却要等
    /// A 构造完成之后才能装配出来"同一种循环，见 <c>RulesAssembly</c> 装配顺序）。
    /// </summary>
    public interface IWeaponDamageQuery
    {
        /// <summary>
        /// <paramref name="unitId"/> 当前装备的武器（<c>item.slot_definition.is_weapon == true</c>
        /// 的槽位，见实现判断记录）的"基础伤害"——判断记录（06/07 均未给出 <c>weapon_damage_pct</c>
        /// 换算武器基础伤害的具体公式，本接口按以下拍板落地）：取
        /// <c>item.template.weapon_profile.damage_min</c>/<c>damage_max</c> 的算术平均值；多个武器槽
        /// （双持）时取按槽位 id 序数最先的一个；未装备任何武器槽（或该槽没有
        /// <c>weapon_profile</c>）时返回 0——06 未定义"徒手基数"这个概念，按"无武器则无武器伤害
        /// 贡献"处理（<c>weapon_damage_pct</c> 单独出现在一条效果里、且施法者当前空手时，最终伤害
        /// 为 0，需要非空手也能造成伤害的技能应改用 <c>school_damage</c> 或另外叠加
        /// <c>base_value</c>/<c>coefficient</c>）。
        /// <para>
        /// 判断记录（T-N2-6，本方法口径不变）：ADR-0032 决策 4 落地后武器改走"秒伤预算"（见 <see
        /// cref="GetWeaponDps"/>）。
        /// </para>
        /// <para>
        /// 更新（T-N3-3，ADR-0031 决策 1/2；06 第 3.2 节 2026-09-14 修订段）：<c>weapon_damage_pct</c>
        /// 原语已改接"秒伤 × 一拍常数 × 百分比"（一拍常数登记在新表 <c>skill.budget_rule</c>，最小
        /// 骨架随 T-N3-3 一并落地，完整字段留给 T-N3-9，见 <c>SkillSchemas.BudgetRule</c> 类型
        /// 注释）——<c>Core.Rules.Skill.EffectDispatcher.ApplyDamageOrHeal</c> 的 <c>WeaponDamagePct</c>
        /// 分支不再调用本方法，改调 <see cref="GetWeaponDps"/>。本方法本身**保留**、签名与既有
        /// <c>(damage_min+damage_max)/2</c> 语义均不改变（硬性规则"禁止改既有 GetWeaponBaseDamage
        /// 签名"；"该方法本身保留供其它消费方"）——目前框架内已知无其它消费方，保留是为了不做无
        /// 必要的破坏性移除，若后续某个消费方（如伤害预览/编辑器工具提示）需要"单次挥击基础伤害"
        /// 这个口径，仍可继续调用它。
        /// </para>
        /// </summary>
        double GetWeaponBaseDamage(Id unitId);

        /// <summary>
        /// T-N2-6 新增（ADR-0032 决策 4；07 第 1.2 节修订段"武器秒伤 = item.weapon_dps_curve
        /// (item_level) × 品质预算倍率 × 武器槽位系数"）：<paramref name="unitId"/> 当前装备的武器
        /// （取法同 <see cref="GetWeaponBaseDamage"/>——<c>item.slot_definition.is_weapon == true</c>
        /// 的槽位，多个武器槽时取按槽位 id 序数最先的一个）的"秒伤"（每秒伤害期望值，不是单次挥击
        /// 伤害；单次挥击伤害 = 秒伤 × <c>weapon_profile.speed</c>，见 07 第 1.2 节"伤害范围"公式与
        /// <c>ItemWeaponDamageDeviatesDpsCurveRule</c>）。
        /// <para>
        /// 未装备任何武器槽、武器模板没有登记 <c>item_level</c>（理论上不会发生，<c>item.template
        /// .item_level</c> 必填）、或曲线 id 在已加载数据里找不到对应的 <c>item.weapon_dps_curve</c>
        /// 记录时，返回 <c>0.0</c>——同 <see cref="GetWeaponBaseDamage"/>"无武器则无贡献"与
        /// <c>EquipmentHost.ApplyArmorValue</c>"曲线缺失按不写处理"两处既有口径一致的"缺失即零"
        /// 处理，不抛异常。默认实现返回 <c>0.0</c>（等价于"未装备/曲线缺失"这一always-safe 缺省值），
        /// 真正的求值逻辑由 <c>EquipmentHost</c> 显式转发实现（<c>Tests.Presentation.Assembly.
        /// InterfaceDefaultMemberForwardingTests</c> 门禁要求）；组合/代理实现（如 <c>Core.Rules
        /// .Assembly.DeferredWeaponDamageQuery</c>）同样需要显式转发到内层真实实现。
        /// </para>
        /// </summary>
        double GetWeaponDps(Id unitId) => 0.0;

        /// <summary>
        /// ADR-0059 新增（消费方反馈第三批第 5 条"普通攻击缺少框架原生执行机制"）：
        /// <paramref name="unitId"/> 当前装备武器的挥击间隔（秒），取值直接等于
        /// <c>item.template.weapon_profile.speed</c>（07 第 1.2 节"伤害范围 = 武器秒伤 ×
        /// weapon_profile.speed"公式里的同一个 <c>speed</c>——该公式把 <c>speed</c> 定义为"每次挥击
        /// 覆盖的秒数"，与本方法"挥击间隔"是同一个量，不是另一套新概念）。取法同
        /// <see cref="GetWeaponBaseDamage"/>/<see cref="GetWeaponDps"/>：按
        /// <c>item.slot_definition.is_weapon == true</c> 的槽位查找，多个武器槽（双持）时取按槽位 id
        /// 序数最先命中的一个。未装备任何武器槽（或该槽没有 <c>weapon_profile</c>）时返回
        /// <c>null</c>——区别于 <see cref="GetWeaponBaseDamage"/>/<see cref="GetWeaponDps"/>"无武器
        /// 返回 0"（0 在那两个方法里是合法的伤害值，语义是"这次贡献为零"）：挥击间隔为 0 没有对应的
        /// 安全语义（会让挥击计时器每 tick 都判定"到点"，等价于"每帧打一次"，正是
        /// <c>Core.Rules.Combat.AutoAttackHost</c> 需要避免的行为），因此用 <c>null</c>
        /// 显式表达"没有武器数据可用"，调用方（<c>AutoAttackHost</c>）据此转而查询
        /// <see cref="Core.Rules.Common.IAttackIntervalFallbackProvider"/>（生物模板 <c>attack_interval</c>
        /// 字段），两者都没有时按"运行时不静默降级"处理——记诊断、当次不攻击，不擅自取一个默认间隔。
        /// 默认实现返回 <c>null</c>（等价于"未装备/无数据"），真正的求值逻辑由 <c>EquipmentHost</c>
        /// 显式转发实现（<c>Tests.Presentation.Assembly.InterfaceDefaultMemberForwardingTests</c>
        /// 门禁要求）；组合/代理实现（<c>Core.Rules.Assembly.DeferredWeaponDamageQuery</c>）同样需要
        /// 显式转发到内层真实实现。
        /// </summary>
        double? GetWeaponAttackIntervalSeconds(Id unitId) => null;

        /// <summary>
        /// ADR-0059 新增：<paramref name="unitId"/> 当前装备武器的学派（<c>item.template
        /// .weapon_profile.weapon_school</c>），供 <c>AutoAttackHost</c> 结算普通攻击伤害时确定
        /// <see cref="EffectContext.School"/>（免疫/抗性/护甲按学派区分，见 06 第 4.1/4.3 节）——
        /// 普通攻击复用既有 <c>weapon_damage_pct</c> 效果原语与既有结算管线，理应像技能一样把武器
        /// 学派带进结算，而不是硬编码成固定学派。取法同 <see cref="GetWeaponAttackIntervalSeconds"/>；
        /// 未装备武器、或武器没有登记 <c>weapon_school</c>（07 第 1.1 节该子字段本就可选）时返回
        /// <c>null</c>——调用方按 <c>null</c> 退回 <c>CombatOptions.PhysicalSchool</c>（"空手/未声明
        /// 学派的普通攻击按物理伤害处理"，与近战武器缺省物理学派的常见约定一致，且与
        /// <see cref="WeaponSchool"/> 字段本身"数据未提供时为 null"的既有防御性处理同一口径）。默认
        /// 实现返回 <c>null</c>，真正求值逻辑与转发要求同 <see cref="GetWeaponAttackIntervalSeconds"/>。
        /// </summary>
        Id? GetWeaponSchool(Id unitId) => null;
    }
}
