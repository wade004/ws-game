using System;
using System.Collections.Generic;
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
    /// 判断记录（N01 修复）：<see cref="Load"/> 语义是"替换"不是"叠加"——不能假定调用时该单位在
    /// <see cref="EconomyHost"/> 内余额为零（跨槽读档、运行中重新读同一存档等场景下当前余额可能已经
    /// 变化）。<see cref="Save"/> 只写非零余额，缺省视为零；因此 Load 对快照出现的货币用 <see
    /// cref="EconomyHost.SetBalance"/> 直接替换为快照值，对 <see cref="EconomyHost.CurrencyIds"/>
    /// 中快照未出现（存档时为零）的货币显式置零，使"当前状态"完全由快照决定，连续多次 Load 同一份
    /// 快照结果幂等。
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

        /// <summary>
        /// 判断记录——读档是"替换"不是"叠加"：<see cref="Save"/> 只写非零余额，缺省当零处理；因此
        /// Load 先把快照里出现的货币按 <see cref="EconomyHost.SetBalance"/> 直接替换为快照值，再把
        /// <see cref="EconomyHost.CurrencyIds"/> 中快照未出现（存档时为零）的货币显式置零，保证"当前
        /// 状态"完全由快照决定而不是与运行期余额相加；连续多次 Load 同一份快照结果幂等。</summary>
        public void Load(JsonValue data)
        {
            var seen = new HashSet<Id>();

            if (!(data is JsonNull))
            {
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

                    _economy.SetBalance(_unitId, currencyId, balance);
                    seen.Add(currencyId);
                }
            }

            foreach (var currencyId in _economy.CurrencyIds)
            {
                if (!seen.Contains(currencyId))
                {
                    _economy.SetBalance(_unitId, currencyId, 0);
                }
            }
        }
    }
}
