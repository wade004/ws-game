namespace Core.Gameplay.Dialog
{
    /// <summary>gossip 菜单动作类型（见 08_玩法层_掉落任务对话关卡.md 第 3.1 节十种 Action 类型表）。
    /// 新增类型走审批（惯例同 <c>Core.Gameplay.Quest.QuestObjectiveType</c>）。</summary>
    public enum DialogActionKind
    {
        Vendor,
        QuestAccept,
        QuestTurnIn,
        Teleport,
        Save,
        SetFlag,
        StartEncounter,
        CastSkill,
        StartStory,
        Script,
    }

    public static class DialogActionKinds
    {
        public static readonly string[] EnumValues =
        {
            "vendor", "quest_accept", "quest_turn_in", "teleport", "save",
            "set_flag", "start_encounter", "cast_skill", "start_story", "script",
        };

        public static string ToWireString(DialogActionKind kind)
        {
            switch (kind)
            {
                case DialogActionKind.Vendor: return "vendor";
                case DialogActionKind.QuestAccept: return "quest_accept";
                case DialogActionKind.QuestTurnIn: return "quest_turn_in";
                case DialogActionKind.Teleport: return "teleport";
                case DialogActionKind.Save: return "save";
                case DialogActionKind.SetFlag: return "set_flag";
                case DialogActionKind.StartEncounter: return "start_encounter";
                case DialogActionKind.CastSkill: return "cast_skill";
                case DialogActionKind.StartStory: return "start_story";
                case DialogActionKind.Script: return "script";
                default: throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "未知 gossip 动作类型");
            }
        }

        public static bool TryParse(string text, out DialogActionKind kind)
        {
            switch (text)
            {
                case "vendor": kind = DialogActionKind.Vendor; return true;
                case "quest_accept": kind = DialogActionKind.QuestAccept; return true;
                case "quest_turn_in": kind = DialogActionKind.QuestTurnIn; return true;
                case "teleport": kind = DialogActionKind.Teleport; return true;
                case "save": kind = DialogActionKind.Save; return true;
                case "set_flag": kind = DialogActionKind.SetFlag; return true;
                case "start_encounter": kind = DialogActionKind.StartEncounter; return true;
                case "cast_skill": kind = DialogActionKind.CastSkill; return true;
                case "start_story": kind = DialogActionKind.StartStory; return true;
                case "script": kind = DialogActionKind.Script; return true;
                default: kind = default; return false;
            }
        }

        /// <summary>该动作类型的语义是否需要 <c>ref</c> 字段。判断记录：08 第 3.1 节
        /// <c>GossipAction</c> 结构原文把 <c>ref</c> 整体标注为 <c>Optional&lt;Id&gt;</c>（对全部
        /// kind 一视同仁地"可选"），但 <c>quest_accept</c>/<c>quest_turn_in</c>/<c>teleport</c>/
        /// <c>set_flag</c>/<c>start_encounter</c>/<c>cast_skill</c>/<c>start_story</c>/<c>script</c>
        /// 八种动作离开具体引用目标就无法执行（<c>DialogHost.ExecuteAction</c> 必然需要一个
        /// questId/传送点/flagKey/遭遇 id/技能 id/剧情树 id/钩子 id 才有意义），本方法收紧为
        /// "按语义实际需要"而非机械照抄"全部可选"，在内容解析期就能拦住缺失引用的坏数据，比留到
        /// 运行期触发空引用异常更早发现问题。<c>save</c>（发起存档不指向任何具体 id）与
        /// <c>vendor</c>（NPC 与商店可以是一对一关系，<c>ref</c> 为空时按当前 NPC 自身推断，见
        /// 08 第 3.1 节该行"打开该 NPC 的 econ.vendor 界面"——NPC 本身已经是隐含的引用目标）
        /// 两种不强制要求 <c>ref</c>。</summary>
        public static bool RequiresRef(DialogActionKind kind) =>
            kind != DialogActionKind.Save && kind != DialogActionKind.Vendor;
    }
}
