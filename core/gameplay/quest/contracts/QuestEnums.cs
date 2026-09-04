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
