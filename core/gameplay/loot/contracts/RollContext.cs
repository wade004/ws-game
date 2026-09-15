using Core.Foundation.Common;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// 一次 <see cref="ILootHost.Roll"/> 调用的上下文（见 08_玩法层_掉落任务对话关卡.md 第 9 节
    /// <c>LootHost.roll(tableId: Id, context: RollContext): List&lt;ItemStack&gt;</c> 契约签名、
    /// 任务书拍板字段：来源单位、可选击杀者、难度倍率、本次结算 id）。
    /// </summary>
    public readonly struct RollContext
    {
        /// <summary>掉落来源（箱子/生物等的运行期实例 id 或所属单位 id，语义同
        /// <see cref="Core.Carriers.Common.ILootRoller.Roll"/> 的 <c>sourceUnitId</c> 参数）。</summary>
        public Id SourceUnitId { get; }

        /// <summary>击杀者（供个人贡献相关规则/条件按击杀者分档使用）；无明确击杀者（开箱、采集）
        /// 为 null。</summary>
        public Id? KillerId { get; }

        /// <summary>难度倍率（见 08 第 9 节 Difficulty 行"L4 Loot（倍率）"依赖），默认 1。</summary>
        public double Multiplier { get; }

        /// <summary>本次结算 id（如实体 id），供伪随机保底计数按 <c>(contextKey, entry)</c> 累积、
        /// <c>loot.rolled</c> 事件的 <c>contextId</c> 字段使用。未显式提供时默认取
        /// <see cref="SourceUnitId"/>。</summary>
        public Id ContextId { get; }

        /// <summary>T-N2-8 新增（ADR-0032 决策 7；08 第 1.1 节修订段"物品等级来自来源（怪物等级或
        /// 难度层）"）：来源等级（通常是掉落来源单位的等级，如击杀的怪物等级）；<c>null</c> 表示未提供
        /// 来源等级（如开箱/采集一类没有"等级"概念的来源），此时物品等级退回
        /// <c>item.template.item_level</c>（见 <see cref="LootRollOutcome"/> 判断记录），本字段与
        /// <see cref="ItemLevelOffset"/> 均不参与任何随机数消耗——"物品等级来自来源"是确定性折算，
        /// 不是掷骰（见 <c>core/gameplay/loot/README.md</c>"掷骰顺序"判断记录）。</summary>
        public int? SourceLevel { get; }

        /// <summary>T-N2-8 新增（ADR-0032 决策 5；08 第 5.1 节修订段 <c>diff.tier.item_level_offset</c>）：
        /// 难度层的物品等级偏移，仅在 <see cref="SourceLevel"/> 非空时叠加（<c>物品等级 =
        /// SourceLevel + ItemLevelOffset</c>）；调用方按"难度模块提供、Loot 模块不反向依赖"的既有惯例
        /// （同 <see cref="Multiplier"/> 判断记录）自行从 <c>IDifficultyHost.ItemLevelOffset</c> 取值后
        /// 传入，本模块不直接依赖 <c>core/gameplay/difficulty</c>。缺省 0（无难度偏移）。</summary>
        public int ItemLevelOffset { get; }

        public RollContext(Id sourceUnitId, Id? killerId = null, double multiplier = 1.0, Id? contextId = null)
        {
            SourceUnitId = sourceUnitId;
            KillerId = killerId;
            Multiplier = multiplier;
            ContextId = contextId ?? sourceUnitId;
            SourceLevel = null;
            ItemLevelOffset = 0;
        }

        /// <summary>T-N2-8 新增重载（ABI 硬性规则"只允许新增"，不改既有 4 参构造函数签名）：额外接受
        /// 来源等级与难度层物品等级偏移，见 <see cref="SourceLevel"/>/<see cref="ItemLevelOffset"/>
        /// 判断记录。</summary>
        public RollContext(Id sourceUnitId, Id? killerId, double multiplier, Id? contextId, int? sourceLevel, int itemLevelOffset = 0)
        {
            SourceUnitId = sourceUnitId;
            KillerId = killerId;
            Multiplier = multiplier;
            ContextId = contextId ?? sourceUnitId;
            SourceLevel = sourceLevel;
            ItemLevelOffset = itemLevelOffset;
        }
    }
}
