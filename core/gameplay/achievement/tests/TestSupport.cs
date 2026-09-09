using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Gameplay.Achievement
{
    /// <summary>供本模块测试共用的最小装配帮助（惯例同 <c>core/carriers/creature/tests</c> 的
    /// <c>CreatureTestSupport</c>）。</summary>
    internal static class TestSupport
    {
        public static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        public static IEventBus CreateBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        /// <summary>
        /// 装配一个已注册 <c>achv.def</c> 与最小 <c>item.*</c> 三表的 <see cref="Core.Foundation.DataRegistry.DataRegistry"/>。
        /// ADR-0019 / F1b 判断记录：<c>achv.def.rewards</c> 直接复用
        /// <c>Core.Gameplay.Quest.QuestSchemas.RewardsFields</c>（见 <c>AchievementSchemas.Def</c>
        /// 类型注释），<c>rewards.items[].itemId</c> 因此是 <c>Reference("item.template")</c>——
        /// <paramref name="extraItemIds"/> 供含 <c>rewards.items</c> 的用例（如
        /// <c>AchievementHostTests.Unlock_RewardGrantFails_...</c>）把引用到的 item id 一并登记进
        /// <c>item.template</c>，避免因目标表未加载而报 <c>reference_integrity</c>（做法同
        /// <c>Tests.Gameplay.Quest.QuestSchemaCoverageTests.Load</c> 的 <c>extraItemIds</c> 参数）；
        /// 不含 <c>rewards.items</c> 的既有调用点无需改动（<paramref name="extraItemIds"/> 缺省空，
        /// <c>item.template</c> 登记但不加载任何记录，不影响这些用例）。
        /// </summary>
        public static Core.Foundation.DataRegistry.DataRegistry MakeRegistry(IEventBus bus, string defRowsJson, params string[] extraItemIds)
        {
            var itemRows = "[" + string.Join(",", Array.ConvertAll(extraItemIds, ItemTemplateRow)) + "]";
            var source = new InMemoryDataSource()
                .Add(Core.Gameplay.Achievement.AchievementSchemas.Def.Name,
                    Envelope(Core.Gameplay.Achievement.AchievementSchemas.Def.Name, defRowsJson))
                .Add("item.template", Envelope("item.template", itemRows))
                .Add("item.slot_definition", Envelope("item.slot_definition",
                    "[{\"id\": \"item.slot.consumable\", \"name_key\": \"l10n.slot.consumable\"}]"))
                .Add("item.quality_definition", Envelope("item.quality_definition",
                    "[{\"id\": \"item.quality.common\", \"name_key\": \"l10n.quality.common\"}]"));

            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(Core.Gameplay.Achievement.AchievementSchemas.Def);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.Template);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.SlotDefinition);
            registry.RegisterSchema(Core.Carriers.Item.ItemSchemas.QualityDefinition);

            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            return registry;
        }

        private static string ItemTemplateRow(string id) =>
            "{\"id\": \"" + id + "\", \"slot\": \"item.slot.consumable\", \"quality\": \"item.quality.common\", " +
            "\"item_level\": 1, \"display_ref\": \"display.item.placeholder\", \"stack_size\": 1, " +
            "\"name_key\": \"l10n." + id + "\"}";
    }

    /// <summary>最小 <see cref="IUnitAccess"/> 假实现：只覆盖 achievement 模块用到的
    /// Exists/GetTemplateId。</summary>
    internal sealed class FakeUnitAccess : IUnitAccess
    {
        private readonly Dictionary<string, Id> _templates = new Dictionary<string, Id>(StringComparer.Ordinal);

        public void SetTemplate(Id unitId, Id templateId) => _templates[unitId.Value] = templateId;

        public bool Exists(Id unitId) => _templates.ContainsKey(unitId.Value);

        public IReadOnlyList<Id> AllUnits => throw new NotImplementedException();

        public Vec2 GetPosition(Id unitId) => throw new NotImplementedException();

        public void SetPosition(Id unitId, Vec2 position) => throw new NotImplementedException();

        public Id GetFaction(Id unitId) => throw new NotImplementedException();

        public int GetLevel(Id unitId) => throw new NotImplementedException();

        public double GetFacing(Id unitId) => throw new NotImplementedException();

        public bool IsAlive(Id unitId) => throw new NotImplementedException();

        public void SetAlive(Id unitId, bool alive) => throw new NotImplementedException();

        public Id? GetTemplateId(Id unitId) => _templates.TryGetValue(unitId.Value, out var t) ? (Id?)t : null;

        public IReadOnlyList<Id> GetTags(Id unitId) => throw new NotImplementedException();
    }

    /// <summary>最小 <see cref="IRewardDispatcher"/> 假实现：只记录 <see cref="Grant"/> 调用。</summary>
    internal sealed class FakeRewardDispatcher : IRewardDispatcher
    {
        public readonly List<(Id UnitId, RewardBundle Bundle, Id SourceId)> Grants = new List<(Id, RewardBundle, Id)>();

        public bool Grant(Id unitId, RewardBundle bundle, Id sourceId)
        {
            Grants.Add((unitId, bundle, sourceId));
            return true;
        }
    }

    /// <summary>最小 <see cref="IExprHostFactory"/> 假实现：只支持 <c>event.&lt;field&gt;</c>
    /// 分组（本模块 <c>custom_event</c> criterion 测试专用），委托给触发事件的
    /// <see cref="IExprReadableEvent.TryGetField"/>；其余分组一律返回 <see cref="ExprValue.OfBool(bool)"/>
    /// <c>false</c>（惯例同 <c>core/gameplay/world_state/tests</c> 的 <c>WorldOnlyExprHost</c>：
    /// 只覆盖测试实际用到的分组，不代表 <c>RulesExprHostFactory</c> 正式实现的完整语义）。</summary>
    internal sealed class FakeExprHostFactory : IExprHostFactory
    {
        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new Host(triggeringEvent);

        private sealed class Host : IExprHost
        {
            private readonly IEvent? _evt;

            public Host(IEvent? evt) => _evt = evt;

            public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args)
            {
                if (group == ExprGroups.Event && _evt is IExprReadableEvent readable && readable.TryGetField(key, out var value))
                {
                    return value;
                }
                return ExprValue.OfBool(false);
            }
        }
    }

    /// <summary>测试用 <c>quest.turned_in</c> 事件（quest 模块由并行任务开发，尚未提供强类型事件类，
    /// 见 <c>AchievementHost.TryMatch</c> 判断记录"只依赖 found.event_catalog 登记的字段名字与
    /// IExprReadableEvent 协议"——本类型只是按该协议伪造一个满足字段表的事件，不代表正式实现）。</summary>
    internal sealed class FakeQuestTurnedInEvent : IEvent, IExprReadableEvent
    {
        public Id Key { get; } = new Id("quest.turned_in");

        public Id UnitId { get; }

        public Id QuestId { get; }

        public FakeQuestTurnedInEvent(Id unitId, Id questId)
        {
            UnitId = unitId;
            QuestId = questId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "questId": value = ExprValue.OfId(QuestId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>测试用 <c>area.trigger_entered</c> 事件（area_trigger 模块由并行任务开发，理由同
    /// <see cref="FakeQuestTurnedInEvent"/>）。</summary>
    internal sealed class FakeAreaTriggerEnteredEvent : IEvent, IExprReadableEvent
    {
        public Id Key { get; } = new Id("area.trigger_entered");

        public Id TriggerId { get; }

        public Id UnitId { get; }

        public FakeAreaTriggerEnteredEvent(Id triggerId, Id unitId)
        {
            TriggerId = triggerId;
            UnitId = unitId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "triggerId": value = ExprValue.OfId(TriggerId); return true;
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>测试用自定义事件（<c>custom_event</c> criterion 专用），携带一个 Int 字段
    /// <c>amount</c> 供 filter Expr 引用。</summary>
    internal sealed class FakeCustomAmountEvent : IEvent, IExprReadableEvent
    {
        public Id Key { get; }

        public int Amount { get; }

        public FakeCustomAmountEvent(Id key, int amount)
        {
            Key = key;
            Amount = amount;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            if (name == "amount")
            {
                value = ExprValue.OfInt(Amount);
                return true;
            }
            value = default;
            return false;
        }
    }
}
