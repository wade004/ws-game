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
    }
}
