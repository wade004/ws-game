using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.Economy
{
    /// <summary>本模块事件 key 常量（对应 <c>found.event_catalog.json</c> 的 <c>economy.*</c> 四行）。</summary>
    public static class EconomyEventKeys
    {
        public static readonly Id CurrencyChanged = new Id("economy.currency_changed");

        public static readonly Id ItemPurchased = new Id("economy.item_purchased");

        public static readonly Id ItemSold = new Id("economy.item_sold");

        public static readonly Id VendorRestocked = new Id("economy.vendor_restocked");
    }

    /// <summary>货币数量变化时触发（见 found.event_catalog.json <c>economy.currency_changed</c> 行）。</summary>
    public sealed class CurrencyChangedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => EconomyEventKeys.CurrencyChanged;

        public Id UnitId { get; }

        public Id CurrencyId { get; }

        public long OldValue { get; }

        public long NewValue { get; }

        public CurrencyChangedEvent(Id unitId, Id currencyId, long oldValue, long newValue)
        {
            UnitId = unitId;
            CurrencyId = currencyId;
            OldValue = oldValue;
            NewValue = newValue;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "currencyId": value = ExprValue.OfId(CurrencyId); return true;
                case "oldValue": value = ExprValue.OfInt(OldValue); return true;
                case "newValue": value = ExprValue.OfInt(NewValue); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>从商人购买物品完成时触发（见 found.event_catalog.json <c>economy.item_purchased</c> 行）。</summary>
    public sealed class ItemPurchasedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => EconomyEventKeys.ItemPurchased;

        public Id UnitId { get; }

        public Id VendorId { get; }

        public Id ItemTemplateId { get; }

        public int Count { get; }

        public long Price { get; }

        public ItemPurchasedEvent(Id unitId, Id vendorId, Id itemTemplateId, int count, long price)
        {
            UnitId = unitId;
            VendorId = vendorId;
            ItemTemplateId = itemTemplateId;
            Count = count;
            Price = price;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "vendorId": value = ExprValue.OfId(VendorId); return true;
                case "itemTemplateId": value = ExprValue.OfId(ItemTemplateId); return true;
                case "count": value = ExprValue.OfInt(Count); return true;
                case "price": value = ExprValue.OfInt(Price); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>向商人出售物品完成时触发（见 found.event_catalog.json <c>economy.item_sold</c> 行）。</summary>
    public sealed class ItemSoldEvent : IEvent, IExprReadableEvent
    {
        public Id Key => EconomyEventKeys.ItemSold;

        public Id UnitId { get; }

        public Id VendorId { get; }

        public Id ItemInstanceId { get; }

        public long Price { get; }

        public ItemSoldEvent(Id unitId, Id vendorId, Id itemInstanceId, long price)
        {
            UnitId = unitId;
            VendorId = vendorId;
            ItemInstanceId = itemInstanceId;
            Price = price;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "vendorId": value = ExprValue.OfId(VendorId); return true;
                case "itemInstanceId": value = ExprValue.OfId(ItemInstanceId); return true;
                case "price": value = ExprValue.OfInt(Price); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>商人限量库存按刷新周期重置时触发（见 found.event_catalog.json
    /// <c>economy.vendor_restocked</c> 行）。</summary>
    public sealed class VendorRestockedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => EconomyEventKeys.VendorRestocked;

        public Id VendorId { get; }

        public VendorRestockedEvent(Id vendorId)
        {
            VendorId = vendorId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "vendorId": value = ExprValue.OfId(VendorId); return true;
                default: value = default; return false;
            }
        }
    }
}
