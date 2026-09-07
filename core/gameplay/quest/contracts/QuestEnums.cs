namespace Core.Gameplay.Quest
{
    /// <summary>起始方式（见 08 第 2.1 节 <c>quest.def.start_method</c> 四值）。</summary>
    public enum QuestStartMethod
    {
        NpcGossip,
        ItemUse,
        AreaTrigger,
        Auto,
    }

    /// <summary>交付方式（见 08 第 2.1 节 <c>quest.def.turn_in_method</c> 两值）。</summary>
    public enum QuestTurnInMethod
    {
        NpcGossip,
        Auto,
    }

    /// <summary>可重复性（见 08 第 2.1 节 <c>quest.def.repeatable</c> 三值）。</summary>
    public enum QuestRepeatable
    {
        None,
        Daily,
        Unlimited,
    }

    /// <summary>任务日志状态机（见 08 第 2.2 节）。</summary>
    public enum QuestState
    {
        Unavailable,
        Available,
        Active,
        ObjectivesComplete,
        TurnedIn,
        Failed,
    }

    /// <summary>
    /// <c>IQuestHost.TurnIn(unitId, questId, out QuestTurnInFailure)</c> 的失败原因（N02/N11 根治，见
    /// 08 第 2.2 节交付流程勘误）。<see cref="None"/> 表示交付成功或调用方不关心原因。
    /// </summary>
    public enum QuestTurnInFailure
    {
        /// <summary>无失败（交付成功），或调用方使用不带 out 参数的重载不关心原因。</summary>
        None,

        /// <summary>当前状态不是 <see cref="QuestState.ObjectivesComplete"/>，或任务定义不存在——交付
        /// 前置条件不满足。</summary>
        NotReady,

        /// <summary>collect 且 <c>consume_on_progress=false</c> 的目标要求上交的物品，实际库存不足以
        /// 移除完整数量（N11：可能是同一批物品被另一次并发交付抢先消耗）；交付整体中止，已扣除的部分
        /// 已回滚，任务保持 <see cref="QuestState.ObjectivesComplete"/>，不发放任何奖励。</summary>
        InsufficientItems,

        /// <summary>奖励里的物品因背包已满（<c>InventoryFullPolicy.Reject</c>）无法完整发放（N02）；
        /// 交付整体中止并回滚——已上交/扣除的 collect 物品已放回背包，任务保持
        /// <see cref="QuestState.ObjectivesComplete"/>，玩家清出背包空间后可再次交付。</summary>
        InventoryFull,
    }

    public static class QuestEnumWireNames
    {
        public static readonly string[] StartMethodValues = { "npc_gossip", "item_use", "area_trigger", "auto" };
        public static readonly string[] TurnInMethodValues = { "npc_gossip", "auto" };
        public static readonly string[] RepeatableValues = { "none", "daily", "unlimited" };

        public static bool TryParseStartMethod(string text, out QuestStartMethod value)
        {
            switch (text)
            {
                case "npc_gossip": value = QuestStartMethod.NpcGossip; return true;
                case "item_use": value = QuestStartMethod.ItemUse; return true;
                case "area_trigger": value = QuestStartMethod.AreaTrigger; return true;
                case "auto": value = QuestStartMethod.Auto; return true;
                default: value = default; return false;
            }
        }

        public static bool TryParseTurnInMethod(string text, out QuestTurnInMethod value)
        {
            switch (text)
            {
                case "npc_gossip": value = QuestTurnInMethod.NpcGossip; return true;
                case "auto": value = QuestTurnInMethod.Auto; return true;
                default: value = default; return false;
            }
        }

        public static bool TryParseRepeatable(string text, out QuestRepeatable value)
        {
            switch (text)
            {
                case "none": value = QuestRepeatable.None; return true;
                case "daily": value = QuestRepeatable.Daily; return true;
                case "unlimited": value = QuestRepeatable.Unlimited; return true;
                default: value = default; return false;
            }
        }
    }
}
