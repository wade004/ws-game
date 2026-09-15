using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.Economy
{
    /// <summary>本模块事件 key 常量（对应 <c>found.event_catalog.json</c> 的 <c>economy.*</c> 行）。
    /// <para>
    /// 判断记录（T-N4-7，<see cref="CurrencyOverflow"/> 暂未登记进 <c>found.event_catalog.json</c>/
    /// 经 <c>toolchain/gen_event_constants.py</c> 重生成 <c>EventKeys.g.cs</c>）：核实
    /// <c>toolchain/gen_event_constants.py --check</c> 只比较该登记表与已提交的生成文件两者自身是否
    /// 一致，不反向扫描代码里手写的 <see cref="Id"/> 事件 key 常量（本类型这四个既有常量与新增的
    /// <see cref="CurrencyOverflow"/> 均是直接 <c>new Id(...)</c>，不经生成器），因此本次不登记也不会
    /// 让该门禁失败——ADR-0034 修订记录与分阶段落地计划把"<c>economy.charged</c>/
    /// <c>economy.currency_overflow</c> 两条新事件登记 <c>found.event_catalog</c> 并重生成常量"整体
    /// 列为 T-N4-8 的任务范围（T-N4-8 依赖 T-N4-7），本任务只新增事件类型与发布点，登记与常量重生成
    /// 留给 T-N4-8 与另一条新事件（<c>economy.charged</c>）一并处理，避免与该任务重复改动同一份登记
    /// 表产生合并冲突。</para></summary>
    public static class EconomyEventKeys
    {
        public static readonly Id CurrencyChanged = new Id("economy.currency_changed");

        public static readonly Id ItemPurchased = new Id("economy.item_purchased");

        public static readonly Id ItemSold = new Id("economy.item_sold");

        public static readonly Id VendorRestocked = new Id("economy.vendor_restocked");

        /// <summary>T-N4-7（ADR-0034 决策 4；08 第 7.4 节修订段）：见 <see cref="CurrencyOverflowEvent"/>。</summary>
        public static readonly Id CurrencyOverflow = new Id("economy.currency_overflow");
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

    /// <summary>
    /// T-N4-7（ADR-0034 决策 4；08 第 7.4 节修订段"达到 econ.currency.cap 时超出部分丢弃并发
    /// economy.currency_overflow { unitId, currencyId, discarded }"）：<see
    /// cref="EconomyHost.Add"/> 使某单位某货币余额被夹取到 <see cref="CurrencyDef.Cap"/> 时触发，
    /// <see cref="Discarded"/> 为本次调用被丢弃的超出量（正数）。<see cref="EconomyHost.SetBalance"/>
    /// 不触发本事件，见该方法判断记录。事件登记（<c>found.event_catalog.json</c>）与常量重生成留给
    /// T-N4-8（见 <see cref="EconomyEventKeys"/> 判断记录）。
    /// </summary>
    public sealed class CurrencyOverflowEvent : IEvent, IExprReadableEvent
    {
        public Id Key => EconomyEventKeys.CurrencyOverflow;

        public Id UnitId { get; }

        public Id CurrencyId { get; }

        /// <summary>本次入账被丢弃的超出量（正数）：<c>raw - cap</c>，<c>raw</c> 为未夹取前的
        /// 余额和。</summary>
        public long Discarded { get; }

        public CurrencyOverflowEvent(Id unitId, Id currencyId, long discarded)
        {
            UnitId = unitId;
            CurrencyId = currencyId;
            Discarded = discarded;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "currencyId": value = ExprValue.OfId(CurrencyId); return true;
                case "discarded": value = ExprValue.OfInt(Discarded); return true;
                default: value = default; return false;
            }
        }
    }
}
