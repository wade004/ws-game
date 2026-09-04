using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.Dialog
{
    /// <summary>本模块发出的事件 key 常量（对应 <c>found.event_catalog</c> 登记表，见
    /// data/_sample/found/found.event_catalog.json <c>dialog.*</c> 四行）。</summary>
    public static class DialogEventKeys
    {
        public static readonly Id GossipOpened = new Id("dialog.gossip_opened");
        public static readonly Id GossipActionExecuted = new Id("dialog.gossip_action_executed");
        public static readonly Id StoryNodeEntered = new Id("dialog.story_node_entered");
        public static readonly Id Ended = new Id("dialog.ended");
    }

    /// <summary>打开 gossip 菜单、应用状态机进入 Dialog 子状态时触发（见 found.event_catalog
    /// <c>dialog.gossip_opened</c> 行、03 第 2 节状态表）。</summary>
    public sealed class GossipOpenedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => DialogEventKeys.GossipOpened;

        public Id UnitId { get; }

        public Id NpcId { get; }

        public Id MenuId { get; }

        public GossipOpenedEvent(Id unitId, Id npcId, Id menuId)
        {
            UnitId = unitId;
            NpcId = npcId;
            MenuId = menuId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "npcId": value = ExprValue.OfId(NpcId); return true;
                case "menuId": value = ExprValue.OfId(MenuId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>gossip 菜单动作项被执行时触发（见 found.event_catalog
    /// <c>dialog.gossip_action_executed</c> 行）。<see cref="ActionId"/> 判断记录：08 第 3.1 节
    /// <c>GossipAction</c> 结构没有单独的 id 字段，本类型取该动作的 <c>ref</c>（存在时）或
    /// <c>dialog.action.&lt;kind&gt;</c>（<c>ref</c> 为空时，如 <c>save</c>）作为标识，
    /// 见 <c>DialogHost</c> 判断记录。</summary>
    public sealed class GossipActionExecutedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => DialogEventKeys.GossipActionExecuted;

        public Id UnitId { get; }

        public Id MenuId { get; }

        public Id ActionId { get; }

        public GossipActionExecutedEvent(Id unitId, Id menuId, Id actionId)
        {
            UnitId = unitId;
            MenuId = menuId;
            ActionId = actionId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "menuId": value = ExprValue.OfId(MenuId); return true;
                case "actionId": value = ExprValue.OfId(ActionId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>剧情对话树推进到某节点时触发（见 found.event_catalog <c>dialog.story_node_entered</c>
    /// 行、03 第 5 节同步小节示例）。</summary>
    public sealed class StoryNodeEnteredEvent : IEvent, IExprReadableEvent
    {
        public Id Key => DialogEventKeys.StoryNodeEntered;

        public Id UnitId { get; }

        public Id TreeId { get; }

        public Id NodeId { get; }

        public StoryNodeEnteredEvent(Id unitId, Id treeId, Id nodeId)
        {
            UnitId = unitId;
            TreeId = treeId;
            NodeId = nodeId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "treeId": value = ExprValue.OfId(TreeId); return true;
                case "nodeId": value = ExprValue.OfId(NodeId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>对话（gossip 或剧情）结束、应用状态机从 Dialog 子状态退回 Explore 时触发（见
    /// found.event_catalog <c>dialog.ended</c> 行、03 第 2 节状态表）。</summary>
    public sealed class DialogEndedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => DialogEventKeys.Ended;

        public Id UnitId { get; }

        public DialogEndedEvent(Id unitId)
        {
            UnitId = unitId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                default: value = default; return false;
            }
        }
    }
}
