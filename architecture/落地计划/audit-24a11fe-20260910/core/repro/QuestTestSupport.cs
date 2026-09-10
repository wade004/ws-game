using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.Common;
using Core.Gameplay.Quest;
using Core.Gameplay.WorldState;
using Core.Rules.Common;
using Core.Rules.ExprHost;

namespace Tests.Gameplay.Quest
{
    /// <summary>最小 <see cref="IInventoryHost"/> 假实现：内存态背包，供 collect 目标测试与
    /// <c>QuestHost.TurnIn</c> 的物品扣除逻辑使用（惯例同仓库内其它模块的 Fake，只覆盖测试实际
    /// 用到的行为）。</summary>
    internal sealed class FakeInventoryHost : IInventoryHost
    {
        private readonly Dictionary<Id, List<ItemInstance>> _items = new Dictionary<Id, List<ItemInstance>>();
        private int _seq;

        /// <summary>便利方法：加入物品并返回本次写入落在的实例 id（供测试手工构造
        /// <see cref="ItemAddedEvent"/> 时使用，模拟真实 InventoryHost 会一并给出的实例 id）。</summary>
        public Id AddItemForTest(Id unitId, Id templateId, int count)
        {
            AddItem(unitId, templateId, count);
            var list = _items[unitId];
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].TemplateId.Equals(templateId))
                {
                    return list[i].InstanceId;
                }
            }
            throw new InvalidOperationException("unreachable");
        }

        /// <summary>N12 测试专用：总是创建一个全新的实例（不并入任何既有同模板堆叠），供模拟真实
        /// <c>InventoryHost.AddItem</c> 跨堆叠场景——本 Fake 的 <see cref="AddItem"/>/<see
        /// cref="AddItemForTest"/> 总是把同模板物品合并进第一个匹配的既有堆叠，无法用公开 API 表示
        /// "同一模板分布在多个独立实例上"这一真实场景，因此单独提供本方法直接构造第二个实例。</summary>
        public Id AddSeparateInstanceForTest(Id unitId, Id templateId, int count)
        {
            if (!_items.TryGetValue(unitId, out var list))
            {
                list = new List<ItemInstance>();
                _items[unitId] = list;
            }

            var instanceId = new Id("item.instance_" + (_seq++));
            list.Add(new ItemInstance(instanceId, templateId, count));
            return instanceId;
        }

        public bool AddItem(Id unitId, Id templateId, int count)
        {
            if (!_items.TryGetValue(unitId, out var list))
            {
                list = new List<ItemInstance>();
                _items[unitId] = list;
            }

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].TemplateId.Equals(templateId))
                {
                    list[i] = new ItemInstance(list[i].InstanceId, templateId, list[i].Count + count);
                    return true;
                }
            }

            var instanceId = new Id("item.instance_" + (_seq++));
            list.Add(new ItemInstance(instanceId, templateId, count));
            return true;
        }

        public bool RemoveItem(Id unitId, Id instanceId, int count)
        {
            if (!_items.TryGetValue(unitId, out var list))
            {
                return false;
            }

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].InstanceId.Equals(instanceId))
                {
                    var newCount = list[i].Count - count;
                    if (newCount < 0) return false;
                    if (newCount == 0) list.RemoveAt(i);
                    else list[i] = new ItemInstance(instanceId, list[i].TemplateId, newCount);
                    return true;
                }
            }
            return false;
        }

        public IReadOnlyList<ItemInstance> ListItems(Id unitId) =>
            _items.TryGetValue(unitId, out var list) ? list.ToArray() : Array.Empty<ItemInstance>();

        public int CountOf(Id unitId, Id templateId)
        {
            if (!_items.TryGetValue(unitId, out var list)) return 0;
            var total = 0;
            foreach (var item in list)
            {
                if (item.TemplateId.Equals(templateId)) total += item.Count;
            }
            return total;
        }

        public ItemInstance? FindInstance(Id unitId, Id instanceId)
        {
            if (!_items.TryGetValue(unitId, out var list)) return null;
            foreach (var item in list)
            {
                if (item.InstanceId.Equals(instanceId)) return item;
            }
            return null;
        }
    }

    /// <summary>最小 <see cref="IUnitAccess"/> 假实现：只支持测试用到的模板 id 查询。</summary>
    internal sealed class FakeUnitAccess : IUnitAccess
    {
        private readonly Dictionary<Id, Id> _templates = new Dictionary<Id, Id>();

        public void SetTemplate(Id unitId, Id templateId) => _templates[unitId] = templateId;

        public bool Exists(Id unitId) => true;
        public IReadOnlyList<Id> AllUnits => Array.Empty<Id>();
        public Vec2 GetPosition(Id unitId) => Vec2.Zero;
        public void SetPosition(Id unitId, Vec2 position) { }
        public Id GetFaction(Id unitId) => new Id("fac.neutral");
        public int GetLevel(Id unitId) => 1;
        public double GetFacing(Id unitId) => 0;
        public bool IsAlive(Id unitId) => true;
        public void SetAlive(Id unitId, bool alive) { }
        public Id? GetTemplateId(Id unitId) => _templates.TryGetValue(unitId, out var t) ? (Id?)t : null;
        public IReadOnlyList<Id> GetTags(Id unitId) => Array.Empty<Id>();
    }

    /// <summary>记录 <see cref="IRewardDispatcher.Grant"/> 调用，供断言"交付时结算奖励"。<see
    /// cref="ShouldFail"/> 供 N02 测试模拟"背包已满、奖励物品发放失败"这一场景，不需要真实构造一个
    /// 容量受限的 <c>InventoryHost</c>——只关心 <c>QuestHost.TurnIn</c> 在 Grant 失败时是否正确回滚/
    /// 不误置 TurnedIn，真实的物品原子回滚由 <c>RewardDispatcherTests</c> 单独覆盖。</summary>
    internal sealed class FakeRewardDispatcher : IRewardDispatcher
    {
        public readonly List<(Id UnitId, RewardBundle Bundle, Id SourceId)> Calls = new List<(Id, RewardBundle, Id)>();

        /// <summary>为 true 时 <see cref="Grant"/> 记录调用但返回 false（模拟物品奖励因背包已满未能
        /// 完整发放）。</summary>
        public bool ShouldFail;

        public bool Grant(Id unitId, RewardBundle bundle, Id sourceId)
        {
            Calls.Add((unitId, bundle, sourceId));
            return !ShouldFail;
        }
    }

    /// <summary>最小 <see cref="IExprHost"/>：把 <c>quest</c>/<c>player</c>/<c>world</c> 分组委托给
    /// 对应 <see cref="IExprGroupProvider"/>，<c>event</c> 分组按 <see cref="IExprReadableEvent"/>
    /// 读取触发事件字段，其余分组按默认值处理（惯例同 <c>core/gameplay/world_state</c> 测试的
    /// <c>WorldOnlyExprHost</c>，只覆盖本模块测试实际用到的分组，不代表
    /// <c>RulesExprHostFactory</c> 正式实现的完整语义）。</summary>
    internal sealed class TestExprHost : IExprHost
    {
        private readonly QuestExprGroupProvider _quest;
        private readonly PlayerExprGroupProvider _player;
        private readonly WorldExprGroupProvider? _world;
        private readonly IEvent? _triggeringEvent;

        public TestExprHost(QuestExprGroupProvider quest, PlayerExprGroupProvider player, WorldExprGroupProvider? world, IEvent? triggeringEvent)
        {
            _quest = quest;
            _player = player;
            _world = world;
            _triggeringEvent = triggeringEvent;
        }

        public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args)
        {
            switch (group)
            {
                case ExprGroups.Quest: return _quest.Query(key, args);
                case ExprGroups.Player: return _player.Query(key, args);
                case ExprGroups.World: return _world != null ? _world.Query(key, args) : ExprValue.OfBool(false);
                case ExprGroups.Event:
                    if (_triggeringEvent is IExprReadableEvent readable && readable.TryGetField(key, out var value))
                    {
                        return value;
                    }
                    return ExprValue.OfBool(false);
                default:
                    return ExprValue.OfBool(false);
            }
        }
    }

    internal sealed class TestExprHostFactory : IExprHostFactory
    {
        private readonly QuestExprGroupProvider _quest;
        private readonly PlayerExprGroupProvider _player;
        private readonly WorldExprGroupProvider? _world;

        public TestExprHostFactory(QuestExprGroupProvider quest, PlayerExprGroupProvider player, WorldExprGroupProvider? world = null)
        {
            _quest = quest;
            _player = player;
            _world = world;
        }

        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) =>
            new TestExprHost(_quest, _player, _world, triggeringEvent);
    }

    internal static class TestSupport
    {
        public static readonly Id Player = new Id("unit.player_hero");

        public static IEventBus NewEventBus()
        {
            var definitions = new List<EventDefinition>
            {
                new EventDefinition(Core.Gameplay.Quest.QuestEventKeys.Accepted, "quest", new[] { "unitId", "questId" }),
                new EventDefinition(Core.Gameplay.Quest.QuestEventKeys.ObjectiveProgress, "quest", new[] { "unitId", "questId", "objectiveIndex", "progress" }),
                new EventDefinition(Core.Gameplay.Quest.QuestEventKeys.Completed, "quest", new[] { "unitId", "questId" }),
                new EventDefinition(Core.Gameplay.Quest.QuestEventKeys.TurnedIn, "quest", new[] { "unitId", "questId" }),
                new EventDefinition(Core.Gameplay.Quest.QuestEventKeys.Failed, "quest", new[] { "unitId", "questId", "reason" }),
                new EventDefinition(Core.Gameplay.Dialog.DialogEventKeys.GossipOpened, "dialog", new[] { "unitId", "npcId", "menuId" }),
                new EventDefinition(Core.Gameplay.Dialog.DialogEventKeys.GossipActionExecuted, "dialog", new[] { "unitId", "menuId", "actionId" }),
                new EventDefinition(Core.Gameplay.Dialog.DialogEventKeys.StoryNodeEntered, "dialog", new[] { "unitId", "treeId", "nodeId" }),
                new EventDefinition(Core.Gameplay.Dialog.DialogEventKeys.Ended, "dialog", new[] { "unitId" }),
                new EventDefinition(RulesEventKeys.UnitDied, "unit", new[] { "unitId", "killerId" }),
                new EventDefinition(RulesEventKeys.SkillCastSuccess, "skill", new[] { "casterId", "skillId" }),
                new EventDefinition(CarriersEventKeys.ItemAdded, "item", new[] { "unitId", "itemInstanceId", "itemTemplateId", "count" }),
                new EventDefinition(CarriersEventKeys.ItemRemoved, "item", new[] { "unitId", "itemInstanceId", "count", "reason" }),
                new EventDefinition(CarriersEventKeys.GobjInteracted, "gobj", new[] { "unitId", "gobjInstanceId" }),
                new EventDefinition(new Id("area.trigger_entered"), "area", new[] { "triggerId", "unitId" }),
                new EventDefinition(new Id("test.custom_event"), "test", new[] { "unitId", "amount" }),
            };
            return new EventBus(EventCatalog.FromDefinitions(definitions));
        }
    }
}
