namespace Core.Rules.Common
{
    /// <summary>
    /// <c>SpellModDef.targetDimension</c> 的固定枚举（见 06 第 3.5 节 <c>SpellModDef</c>）：
    /// 天赋/装备特效可修改施法管线的哪个维度。
    /// </summary>
    public enum SpellModDimension
    {
        CastTime,
        Cost,
        Cooldown,
        CritChance,
        EffectValue,
        Charges,
    }

    /// <summary><c>SpellModDef.op</c> 的固定枚举（见 06 第 3.5 节），与
    /// <see cref="Core.Numbers.StatBlock.StatModifierOp"/> 的 <c>Flat</c>/<c>Pct</c> 同名同义，但
    /// SpellMod 只作用于施法管线维度而非属性，不复用 <see cref="Core.Numbers.StatBlock.StatModifierOp"/>
    /// 本身（那个类型还含 <c>Mult</c> 独立乘区，SpellMod 语境不存在这一概念，06 原文只给
    /// <c>flat | pct</c> 两值）。</summary>
    public enum SpellModOp
    {
        Flat,
        Pct,
    }
}
