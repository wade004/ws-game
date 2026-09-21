namespace Core.Rules.Combat
{
    /// <summary>
    /// ADR-0059（消费方反馈第三批第 5 条"普通攻击缺少框架原生执行机制"）：<see cref="AutoAttackHost"/>
    /// 对外暴露的普通攻击可观测状态——"开启/关闭"与"当前目标"两个状态（任务书拍板"状态迁移要可
    /// 观测"）合并成一个三值快照，供消费方（表现层/AI/测试）一次查询即可判断当前该不该播放挥击
    /// 表现，不需要分别查两个布尔/可空字段再自己组合语义。
    /// </summary>
    public enum AutoAttackState
    {
        /// <summary>未开启（<see cref="AutoAttackHost.SetEnabled"/> 传 <c>false</c>，或该单位从未
        /// 开启过）。</summary>
        Off,

        /// <summary>已开启，但当前没有有效目标——"未攻击"态（任务书原句）：从未
        /// <see cref="AutoAttackHost.SetTarget"/> 过、或目标消失/死亡后被清空，见
        /// <see cref="AutoAttackHost"/> 判断记录"目标消失/死亡的行为"。</summary>
        NoTarget,

        /// <summary>已开启且当前有目标——挥击计时器正在为该目标推进（是否命中受
        /// <c>ControlFlags.NoAttack</c>/射程等瞬时门控影响，那些门控不改变本状态本身，只影响
        /// 某一次挥击是否真正结算，见 <see cref="AutoAttackHost"/> 判断记录）。</summary>
        Attacking,
    }
}
