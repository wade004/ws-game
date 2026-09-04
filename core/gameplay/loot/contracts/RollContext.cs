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

        public RollContext(Id sourceUnitId, Id? killerId = null, double multiplier = 1.0, Id? contextId = null)
        {
            SourceUnitId = sourceUnitId;
            KillerId = killerId;
            Multiplier = multiplier;
            ContextId = contextId ?? sourceUnitId;
        }
    }
}
