using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.Loot
{
    /// <summary>本模块事件 key 常量（对应 <c>found.event_catalog</c> 登记表 <c>loot.rolled</c>/
    /// <c>loot.picked_up</c> 两行，见 <c>data/_sample/found/found.event_catalog.json</c>、08 第 9 节
    /// Loot 行"事件（发出）"）。</summary>
    public static class LootEventKeys
    {
        public static readonly Id Rolled = new Id("loot.rolled");

        public static readonly Id PickedUp = new Id("loot.picked_up");
    }

    /// <summary><see cref="LootHost.Roll"/> 完成一次掉落抽取后触发（见 found.event_catalog.json
    /// <c>loot.rolled</c> 行字段 <c>tableId</c>/<c>contextId</c>/<c>items</c>，"字段为建议值"）。
    /// <para>判断记录：<see cref="Items"/> 是 <see cref="ItemStack"/> 列表，不是 Expr 五种标量之一，
    /// <see cref="IExprReadableEvent.TryGetField"/> 对 <c>"items"</c> 返回 false（同
    /// <c>SkillCastSuccessEvent.Targets</c> 惯例，见 <c>core/rules/common/contracts/Events.cs</c>
    /// 类型注释）；<c>tableId</c>/<c>contextId</c> 按 Id 暴露，供 Proc/成就一类订阅方按表 id 过滤。</para>
    /// </summary>
    public sealed class LootRolledEvent : IEvent, IExprReadableEvent
    {
        public Id Key => LootEventKeys.Rolled;

        public Id TableId { get; }

        public Id ContextId { get; }

        public IReadOnlyList<ItemStack> Items { get; }

        public LootRolledEvent(Id tableId, Id contextId, IReadOnlyList<ItemStack> items)
        {
            TableId = tableId;
            ContextId = contextId;
            Items = (items ?? Array.Empty<ItemStack>()).ToArray();
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "tableId": value = ExprValue.OfId(TableId); return true;
                case "contextId": value = ExprValue.OfId(ContextId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>拾取掉落物完成时触发（见 found.event_catalog.json <c>loot.picked_up</c> 行字段
    /// <c>unitId</c>/<c>lootInstanceId</c>/<c>items</c>）。<see cref="Items"/> 是本次实际拾取到手的
    /// 部分（<see cref="LootPickupPolicy.Partial"/> 下可能少于地面掉落物原有的全部内容），同
    /// <see cref="LootRolledEvent"/> 判断记录，不经 <see cref="IExprReadableEvent.TryGetField"/>
    /// 暴露。</summary>
    public sealed class LootPickedUpEvent : IEvent, IExprReadableEvent
    {
        public Id Key => LootEventKeys.PickedUp;

        public Id UnitId { get; }

        public Id LootInstanceId { get; }

        public IReadOnlyList<ItemStack> Items { get; }

        public LootPickedUpEvent(Id unitId, Id lootInstanceId, IReadOnlyList<ItemStack> items)
        {
            UnitId = unitId;
            LootInstanceId = lootInstanceId;
            Items = (items ?? Array.Empty<ItemStack>()).ToArray();
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "lootInstanceId": value = ExprValue.OfId(LootInstanceId); return true;
                default: value = default; return false;
            }
        }
    }
}
