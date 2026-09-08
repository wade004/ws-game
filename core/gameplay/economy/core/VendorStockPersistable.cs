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
    /// AUD-03 根治（architecture/落地计划/audit-85f1f4f-20260908，P2，10 第 2.3 节勘误）：此前的
    /// 判断记录"不持久化 <c>restock_policy=timer</c> 的倒计时，读档后重新从满值起算"只在"读档同时
    /// 重新构造一个全新 <see cref="EconomyHost"/>"的场景下成立——那种场景下倒计时反正会在构造函数
    /// 里按 <c>RestockTimer</c> 满值初始化，读档不读它确实等价于满值起算。但真实探针复现了另一种
    /// 同样合法的调用方式："原地读档"——同一个已经在运行、倒计时已经跑了一段时间的 <see
    /// cref="EconomyHost"/> 实例被要求 <c>Load</c> 回某个更早的存档点，此时旧判断记录的"读档后重新
    /// 满值起算"承诺没有兑现：<see cref="EconomyHost.SetStock"/> 只写 <c>Remaining</c>，从不触碰
    /// <c>TimerRemaining</c>，读档后倒计时仍是读档前那个正在跑的旧值，会让补货比存档快照那一刻
    /// 应有的时间提前触发。现在按 <c>timer</c> 策略物品可选持久化 <c>timer_remaining</c>：存档时有
    /// 值就写（新格式），读档时按 <see cref="EconomyHost.SetStock"/> 新增的可选参数语义处理——旧格式
    /// 存档（该字段缺失）或该物品不是 <c>timer</c> 策略时，由 <c>SetStock</c> 内部重置为完整周期，
    /// 不再是"保持读档前的旧倒计时不动"。
    /// </para>
    /// <para>
    /// 向后兼容：<see cref="Load"/> 同时接受"纯数字"（1.5/1.6 早期格式，只有 <c>Remaining</c>）与
    /// "<c>{remaining, timer_remaining}</c> 对象"（本次新增，只用于 <c>timer</c> 策略物品）两种条目
    /// 形状；<see cref="Save"/> 只在物品是 <c>timer</c> 策略时才写对象形状，其它物品继续写纯数字，
    /// 不改变旧存档已经在用的形状。
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
                    if (!remaining.HasValue)
                    {
                        continue;
                    }

                    if (sellItem.RestockPolicy == VendorRestockPolicy.Timer)
                    {
                        var timerRemaining = _economy.GetStockTimerRemaining(vendorId, sellItem.ItemId);
                        var itemBuilder = new JsonObjectBuilder().Add("remaining", new JsonNumber(remaining.Value));
                        if (timerRemaining.HasValue)
                        {
                            itemBuilder.Add("timer_remaining", new JsonNumber(timerRemaining.Value));
                        }

                        itemsBuilder.Add(sellItem.ItemId.Value, itemBuilder.Build());
                    }
                    else
                    {
                        itemsBuilder.Add(sellItem.ItemId.Value, new JsonNumber(remaining.Value));
                    }

                    any = true;
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
            // AUD-02 根治（architecture/落地计划/audit-85f1f4f-20260908，P2）：本段整体缺失
            // （data is JsonNull）时必须清空/重置到内容定义的默认态（满库存、补货计时满周期），
            // 不能 no-op 保留读档前的运行期库存——否则同一宿主先后加载两个存档槽，缺本段的旧档
            // 不会清掉前一个槽留下的库存残留（真实探针复现：先设库存为 2，再读一份只含 meta 的
            // 存档，库存原样保留为 2 而不是回到内容定义的满库存 5）。
            if (data is JsonNull)
            {
                ResetAllStockToDefault();
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
                    if (!Id.TryParse(itemKv.Key, out var itemId))
                    {
                        throw new FormatException($"{SectionKey} 段的物品条目 \"{itemKv.Key}\" 格式不合法");
                    }

                    if (itemKv.Value is JsonNumber num && num.TryGetInt64(out var remaining))
                    {
                        // 旧格式（纯数字）：不携带 timer_remaining，SetStock 按其可选参数语义
                        // 兜底重置为完整周期（若该物品确实是 timer 策略）。
                        _economy.SetStock(vendorId, itemId, (int)remaining);
                        continue;
                    }

                    if (itemKv.Value is JsonObject itemObj
                        && itemObj.TryGetValue("remaining", out var remainingRaw)
                        && remainingRaw is JsonNumber remainingNum && remainingNum.TryGetInt64(out var remainingLong))
                    {
                        double? timerRemaining = null;
                        if (itemObj.TryGetValue("timer_remaining", out var timerRaw) && timerRaw is JsonNumber timerNum)
                        {
                            timerRemaining = timerNum.Value;
                        }

                        _economy.SetStock(vendorId, itemId, (int)remainingLong, timerRemaining);
                        continue;
                    }

                    throw new FormatException($"{SectionKey} 段的物品条目 \"{itemKv.Key}\" 格式不合法");
                }
            }
        }

        /// <summary>见 <see cref="Load"/> 判断记录：本段缺失时的默认态——按内容定义把全部限量库存的
        /// 物品重置为满库存，<c>timer</c> 策略的倒计时一并重置为完整周期（经 <see
        /// cref="EconomyHost.SetStock"/> 的 <c>timerRemaining=null</c> 兜底语义）。</summary>
        private void ResetAllStockToDefault()
        {
            foreach (var vendorId in _economy.VendorIds)
            {
                var vendor = _economy.GetVendorDef(vendorId);
                foreach (var sellItem in vendor.SellItems)
                {
                    if (!sellItem.StockLimit.HasValue)
                    {
                        continue;
                    }

                    _economy.SetStock(vendorId, sellItem.ItemId, sellItem.StockLimit);
                }
            }
        }
    }
}
