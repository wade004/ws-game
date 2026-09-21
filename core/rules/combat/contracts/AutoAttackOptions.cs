namespace Core.Rules.Combat
{
    /// <summary>
    /// ADR-0059（消费方反馈第三批第 5 条）：<see cref="AutoAttackHost"/> 的可配置项。可变类，惯例同
    /// <see cref="CombatOptions"/>——消费方经 <c>RulesAssembly.AutoAttack.Options</c> 拿到实例后
    /// 直接改属性，不需要在装配根构造期就决定好全部取值。
    /// </summary>
    public sealed class AutoAttackOptions
    {
        /// <summary>
        /// 普通攻击的最大生效射程（世界单位）。<c>0</c>（缺省）表示不做射程判定——惯例对齐 06 第
        /// 3.1 节 <c>SkillDef.Range</c>"<c>Range == 0</c> 表示无限制/作用于自身"的既有约定：06/07
        /// 均未给普通攻击规定过一个全局缺省近战距离常量（<c>weapon_profile</c> 只登记伤害/攻速，不
        /// 登记距离），框架不替消费方发明一个任意的"近战距离"魔数，而是复用 <c>SkillDef.Range</c>
        /// 已有的"0 = 无限制"语义作缺省，需要射程判定的消费方按自己的近战距离数值显式设置本属性。
        /// </summary>
        public double Range { get; set; }
    }
}
