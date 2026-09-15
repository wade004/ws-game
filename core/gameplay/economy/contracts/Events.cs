using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.Economy
{
    /// <summary>本模块事件 key 常量（对应 <c>found.event_catalog.json</c> 的 <c>economy.*</c> 行）。
    /// <para>
    /// 判断记录（T-N4-8 收口 T-N4-7 遗留项）：T-N4-7 落地 <see cref="CurrencyOverflow"/> 时暂未登记进
    /// <c>found.event_catalog.json</c>/重生成 <c>EventKeys.g.cs</c>（当时的判断记录：分阶段落地计划把
    /// "两条新经济事件登记并重生成常量"整体列为 T-N4-8 范围，避免两个任务并行改同一份登记表产生合并
    /// 冲突）——本任务（T-N4-8）把 <see cref="CurrencyOverflow"/> 与新增的 <see cref="Charged"/> 一并
    /// 登记进 <c>data/_framework/found/found.event_catalog.json</c> 并跑
    /// <c>toolchain/gen_event_constants.py</c> 重生成 <c>core/foundation/event_bus/generated/
    /// EventKeys.g.cs</c>（<c>EconomyCharged</c>/<c>EconomyCurrencyOverflow</c> 两个新常量）。本类型
    /// 这六个 <see cref="Id"/> 常量本身仍然是直接 <c>new Id(...)</c> 手写、不引用生成的
    /// <c>EventKeys.g.cs</c>——两套常量并存不是遗漏：<c>EconomyEventKeys</c> 是本模块内部长期以来的
    /// 既有惯例（订阅/发布都引用它，改成员类型/引用来源属于超出本任务范围的重构），生成的
    /// <c>EventKeys</c> 常量供不方便直接依赖 <c>Core.Gameplay.Economy</c> 的外部消费方（如编辑器工具
    /// 链）使用；两者的 <see cref="Id"/> 值必须逐字相同（<c>gen_event_constants.py --check</c> 只保证
    /// 登记表与生成文件自身一致，不反查本类型，值是否一致靠人工核对，见本次登记的两行 JSON 与本类型
    /// 常量逐字比对）。</para></summary>
    public static class EconomyEventKeys
    {
        public static readonly Id CurrencyChanged = new Id("economy.currency_changed");

        /// <summary>T-N4-8（ADR-0034 决策 5；08 第 7.4 节修订段）：见 <see cref="EconomyChargedEvent"/>。</summary>
        public static readonly Id Charged = new Id("economy.charged");

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

    /// <summary>
    /// T-N4-8（ADR-0034 决策 5；08 第 7.4 节修订段"tryCharge/TryPay(unitId, currencyId, amount,
    /// reason)——余额足够时一次性扣除并发 economy.charged{unitId, currencyId, amount, reason}"）：
    /// <see cref="EconomyHost.TryPay(Id,Id,long,string)"/> 原子扣费成功时触发（见 <see
    /// cref="EconomyHost.TryPay(Id,Id,long)"/> 判断记录：旧无 reason 签名转发本方法并传入占位
    /// <c>"unspecified"</c>，因此旧调用路径从 T-N4-8 起同样会触发本事件，不只限于新签名的调用方）。
    /// </summary>
    public sealed class EconomyChargedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => EconomyEventKeys.Charged;

        public Id UnitId { get; }

        public Id CurrencyId { get; }

        public long Amount { get; }

        /// <summary>这次扣费是为了什么，如 <c>"vendor_buy"</c>/<c>"respawn_fee"</c>；旧无 reason
        /// 签名转发产生的事件此处恒为 <c>"unspecified"</c>，见 <see cref="EconomyHost.TryPay(Id,Id,long)"/>
        /// 判断记录。</summary>
        public string Reason { get; }

        public EconomyChargedEvent(Id unitId, Id currencyId, long amount, string reason)
        {
            UnitId = unitId;
            CurrencyId = currencyId;
            Amount = amount;
            Reason = reason;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "unitId": value = ExprValue.OfId(UnitId); return true;
                case "currencyId": value = ExprValue.OfId(CurrencyId); return true;
                case "amount": value = ExprValue.OfInt(Amount); return true;
                case "reason": value = ExprValue.OfString(Reason); return true;
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
    /// 不触发本事件，见该方法判断记录。事件登记（<c>found.event_catalog.json</c>）与常量重生成 T-N4-8
    /// 已收口完成（见 <see cref="EconomyEventKeys"/> 判断记录）。
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
