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
        /// </summary>
        double GetWeaponBaseDamage(Id unitId);
    }
}
