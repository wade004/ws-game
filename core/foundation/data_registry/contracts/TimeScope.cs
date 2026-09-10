namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// ADR-0022（04 第 3.4 节"时间字段单位与作用域"）：一张表所属的时间模型作用域，取值对应
    /// <c>found.time_model.scope</c>（<c>exploration</c>/<c>combat</c>，见 04 第 3.1 节）。
    /// <see cref="TableSchema.TimeScope"/> 为 <see cref="None"/>（默认）表示该表不含任何受时间模型
    /// 约束的字段；非 <see cref="None"/> 时，<see cref="Core.Foundation.SimLoop.TimeModelRules"/>
    /// 用它判定该表内标记 <see cref="FieldUnit.Time"/> 的字段应对照哪个/哪些作用域的
    /// <c>found.time_model.mode</c> 检查整数约束。
    /// </summary>
    public enum TimeScope
    {
        /// <summary>该表不含时间模型字段（默认）。</summary>
        None,

        /// <summary>仅探索作用域。</summary>
        Exploration,

        /// <summary>仅战斗作用域。</summary>
        Combat,

        /// <summary>同一张表内不同字段分属探索与战斗两个作用域（如 <c>arch.power_type</c>：
        /// <c>regen_in_combat</c> 属战斗，<c>regen_out_of_combat</c>/<c>decay_out_of_combat</c>
        /// 属探索）。</summary>
        Both,
    }
}
