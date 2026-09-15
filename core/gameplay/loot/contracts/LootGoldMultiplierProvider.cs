using Core.Foundation.Common;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// T-N6-3b 新增（N4 遗留第 7 项；ADR-0034 决策 3 延伸；08 第 1.1/7.4 节"怪物掉钱 = 当量 ×
    /// econ.gold_base_curve(怪物等级) × 分档倍率 × diff.tier.loot_multiplier"）：由调用方（游戏侧/
    /// 装配根）注入的分档金币倍率委托——取法与
    /// <see cref="Core.Numbers.Progression.ProgressionXpMultiplierProvider"/>（分档经验倍率窄委托，
    /// T-N4-4）一致，只是本委托的调用方（<see cref="LootHost.ResolveCurrencyOutcome"/>）手上只有
    /// <see cref="RollContext.TierId"/>，没有"领取经验的单位/来源 id"这类概念，因此签名比
    /// <c>ProgressionXpMultiplierProvider</c> 更窄，只接受 <paramref name="tierId"/>。
    /// <para>
    /// <paramref name="tierId"/> 是掉落来源单位的 <c>creature.tier_definition</c> 分档 id（见
    /// <see cref="RollContext.TierId"/> 判断记录），<c>null</c> 表示未能解析出分档信息，调用方应退化
    /// 为"无分档金币加成"（返回 1）。默认不设置（<see cref="LootHost"/> 对应可选构造参数为
    /// <c>null</c>），等价于恒为 1，不影响既有掉钱计算。
    /// </para>
    /// </summary>
    public delegate double LootGoldMultiplierProvider(Id? tierId);
}
