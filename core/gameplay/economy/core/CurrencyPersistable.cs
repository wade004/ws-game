using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// <c>player.currencies</c> 段（见 10 第 2.2 节 <c>currencies: Map&lt;Id, Int&gt;</c>、第 3 节
    /// 汇总顺序步骤 6）。<see cref="SaveSections.PlayerCurrencies"/> 已在
    /// <c>core/foundation/save_system</c> 登记，本类直接复用，不需要"补录"（对照
    /// <c>DroppedLootPersistable</c>/<c>VendorStockPersistable</c> 两个确实需要补录字面量段 key 的情形）。
    /// <para>
    /// 判断记录：<see cref="Load"/> 假定调用时该单位在 <see cref="EconomyHost"/> 内尚无任何余额记录
    /// （游戏启动读档的通常时序：先构造空的 <see cref="EconomyHost"/>，再依次跑各 <see
    /// cref="IPersistable.Load"/>），因此用 <see cref="EconomyHost.Add"/>（增量）而不是"直接覆写字典"
    /// 也能达到"设置为存档值"的效果（<c>0 + savedValue = savedValue</c>），不需要 <see
    /// cref="IEconomyHost"/> 额外暴露一个"Set"方法——惯例同
    /// <c>core/carriers/item.EquipmentPersistable.Load</c>"读档时重新走一遍真实业务逻辑（含产生
    /// 事件），不直接摆状态"的既有判断记录。若调用方在非空余额状态下调用 <see cref="Load"/>，效果是
    /// "叠加"而非"覆盖"，不在本类型的设计目标场景内。
    /// </para>
    /// </summary>
    public sealed class CurrencyPersistable : IPersistable
    {
        private readonly Id _unitId;
        private readonly EconomyHost _economy;

        public CurrencyPersistable(Id unitId, EconomyHost economy)
        {
            _unitId = unitId;
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        }

        public string SectionKey => SaveSections.PlayerCurrencies;

        public JsonValue Save()
        {
            var builder = new JsonObjectBuilder();
            foreach (var currencyId in _economy.CurrencyIds)
            {
                var balance = _economy.GetBalance(_unitId, currencyId);
                if (balance != 0)
                {
                    builder.Add(currencyId.Value, new JsonNumber(balance));
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

            foreach (var kv in obj)
            {
                if (!Id.TryParse(kv.Key, out var currencyId))
                {
                    throw new FormatException($"{SectionKey} 段的货币键 \"{kv.Key}\" 不是合法 Id");
                }

                if (!(kv.Value is JsonNumber num) || !num.TryGetInt64(out var balance))
                {
                    throw new FormatException($"{SectionKey} 段的货币 \"{kv.Key}\" 值不是合法整数");
                }

                _economy.Add(_unitId, currencyId, balance, sourceId: _unitId);
            }
        }
    }
}
