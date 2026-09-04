namespace Core.Gameplay.Quest
{
    /// <summary>
    /// 任务目标类型（见 08_玩法层_掉落任务对话关卡.md 第 2.1 节 <c>QuestObjective.type</c> 八值表）。
    /// 新增类型走审批（08 第 9 节"基础架构提供 / 游戏层提供"表"新增目标类型走审批"）。
    /// </summary>
    public enum QuestObjectiveType
    {
        Kill,
        Collect,
        Interact,
        Explore,
        Escort,
        Event,
        Cast,
        Talk,
    }

    /// <summary>
    /// <see cref="QuestObjectiveType"/> 与数据表字符串取值（<c>quest.def.objectives[].type</c>）、
    /// <c>target_ref</c> 应指向的域名（见 08 第 2.1 节 type/targetRef 对照表）之间的转换帮助方法。
    /// </summary>
    public static class QuestObjectiveTypes
    {
        public static readonly string[] EnumValues =
        {
            "kill", "collect", "interact", "explore", "escort", "event", "cast", "talk",
        };

        public static string ToWireString(QuestObjectiveType type)
        {
            switch (type)
            {
                case QuestObjectiveType.Kill: return "kill";
                case QuestObjectiveType.Collect: return "collect";
                case QuestObjectiveType.Interact: return "interact";
                case QuestObjectiveType.Explore: return "explore";
                case QuestObjectiveType.Escort: return "escort";
                case QuestObjectiveType.Event: return "event";
                case QuestObjectiveType.Cast: return "cast";
                case QuestObjectiveType.Talk: return "talk";
                default: throw new System.ArgumentOutOfRangeException(nameof(type), type, "未知任务目标类型");
            }
        }

        public static bool TryParse(string text, out QuestObjectiveType type)
        {
            switch (text)
            {
                case "kill": type = QuestObjectiveType.Kill; return true;
                case "collect": type = QuestObjectiveType.Collect; return true;
                case "interact": type = QuestObjectiveType.Interact; return true;
                case "explore": type = QuestObjectiveType.Explore; return true;
                case "escort": type = QuestObjectiveType.Escort; return true;
                case "event": type = QuestObjectiveType.Event; return true;
                case "cast": type = QuestObjectiveType.Cast; return true;
                case "talk": type = QuestObjectiveType.Talk; return true;
                default: type = default; return false;
            }
        }

        /// <summary>
        /// <c>target_ref</c> 必须落在的域名（<see cref="Core.Foundation.Common.Id.Domain"/>），
        /// 见 08 第 2.1 节对照表："kill→creature、collect→item、interact→gobj、explore→area、
        /// cast→skill、talk→dialog、escort→creature、event→任意"（<c>event</c> 返回 null 表示
        /// 不限制域名——它指向 06/08 事件词汇表中的具体事件 key，事件 key 的 domain 不固定，见
        /// <c>found.event_catalog</c> 各行 <c>domain</c> 字段可以是 <c>skill</c>/<c>combat</c>/
        /// <c>unit</c> 等任意值）。<c>talk</c> 判断记录：目标可以是 <c>dialog.gossip_menu</c> 的
        /// <c>id</c>，也可以是 <c>dialog.story_tree</c> 某个节点的 <c>id</c>（见 08 第 2.1 节
        /// targetRef 列"dialog.gossip_menu 或 dialog.story_tree 节点"）——两者的 <see cref="Core.Foundation.Common.Id"/>
        /// 都不强制要求 domain 段等于 <c>"dialog"</c>（<c>story_tree</c> 节点 id 由内容作者自行
        /// 命名），因此本表对 <c>talk</c> 返回 null（不做域名强制），只在
        /// <c>QuestHost</c> 运行期按实际触发的 gossip/story 事件字段匹配，不在校验期做域名检查
        /// ——这是本表与 08 原文字面"kill→creature"等强对照关系的一处偏差，判断记录见
        /// <c>QuestContentValidationRule</c>。</summary>
        public static string? RequiredTargetDomain(QuestObjectiveType type)
        {
            switch (type)
            {
                case QuestObjectiveType.Kill: return "creature";
                case QuestObjectiveType.Collect: return "item";
                case QuestObjectiveType.Interact: return "gobj";
                case QuestObjectiveType.Explore: return "area";
                case QuestObjectiveType.Escort: return "creature";
                case QuestObjectiveType.Cast: return "skill";
                case QuestObjectiveType.Event: return null;
                case QuestObjectiveType.Talk: return null;
                default: throw new System.ArgumentOutOfRangeException(nameof(type), type, "未知任务目标类型");
            }
        }

        /// <summary><c>count</c> 是否必须恒为 1（08 第 2.1 节对照表 explore/escort/talk 行"恒为 1"）。</summary>
        public static bool RequiresCountOne(QuestObjectiveType type) =>
            type == QuestObjectiveType.Explore || type == QuestObjectiveType.Escort || type == QuestObjectiveType.Talk;
    }
}
