using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// <c>world.vendor_stock</c> 段（任务书标注"补录，可选"）：持久化限量库存的剩余数量，避免读档后
    /// 商人库存回到满额造成"刷库存"漏洞。段 key 同 <c>core/gameplay/loot.DroppedLootPersistable</c>
    /// 判断记录——<c>IPersistable.SectionKey</c> 允许任意非空字符串，不需要触碰
    /// <c>core/foundation/save_system.SaveSections</c>。
    /// <para>
    /// 判断记录：只持久化 <see cref="VendorSellItem.StockLimit"/> 有限量的条目的剩余数量
    /// （<c>Remaining</c>），**不持久化** <c>restock_policy=timer</c> 的倒计时（<c>TimerRemaining</c>）——
    /// 任务书标注本段"可选"，倒计时读档后重新从 <c>RestockTimer</c> 满值起算是一个可接受的简化
    /// （最坏情况只是读档后第一次补货比"如果没读档"稍晚一点触发，不影响正确性，只是一处体验上的
    /// 微小近似），避免为一个可选段引入额外的存储/兼容负担。
    /// </para>
    /// </summary>
    public sealed class VendorStockPersistable : IPersistable
    {
        private readonly EconomyHost _economy;

        public VendorStockPersistable(EconomyHost economy)
        {
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        }

        public string SectionKey => "world.vendor_stock";

        public JsonValue Save()
        {
            var builder = new JsonObjectBuilder();
            foreach (var vendorId in _economy.VendorIds)
            {
                var vendor = _economy.GetVendorDef(vendorId);
                var itemsBuilder = new JsonObjectBuilder();
                var any = false;

                foreach (var sellItem in vendor.SellItems)
                {
                    if (!sellItem.StockLimit.HasValue)
                    {
                        continue;
                    }

                    var remaining = _economy.GetStock(vendorId, sellItem.ItemId);
                    if (remaining.HasValue)
                    {
                        itemsBuilder.Add(sellItem.ItemId.Value, new JsonNumber(remaining.Value));
                        any = true;
                    }
                }

                if (any)
                {
                    builder.Add(vendorId.Value, itemsBuilder.Build());
                }
            }

            return builder.Build();
        }

        public void Load(JsonValue data)
        {
            if (data is JsonNull)
            {
                return;
            }

            if (!(data is JsonObject obj))
            {
                throw new FormatException($"{SectionKey} 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
            }

            foreach (var vendorKv in obj)
            {
                if (!Id.TryParse(vendorKv.Key, out var vendorId) || !(vendorKv.Value is JsonObject itemsObj))
                {
                    throw new FormatException($"{SectionKey} 段的商人条目 \"{vendorKv.Key}\" 格式不合法");
                }

                foreach (var itemKv in itemsObj)
                {
                    if (!Id.TryParse(itemKv.Key, out var itemId) || !(itemKv.Value is JsonNumber num) || !num.TryGetInt64(out var remaining))
                    {
                        throw new FormatException($"{SectionKey} 段的物品条目 \"{itemKv.Key}\" 格式不合法");
                    }

                    _economy.SetStock(vendorId, itemId, (int)remaining);
                }
            }
        }
    }
}
