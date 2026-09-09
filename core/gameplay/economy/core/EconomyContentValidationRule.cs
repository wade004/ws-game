using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// <c>econ.currency</c>/<c>econ.vendor</c> 专属校验规则（惯例同
    /// <c>core/carriers/creature.CreatureContentValidationRule</c>：不由 <c>data_registry</c> 自动
    /// 注册，需组装层/测试显式 <c>registry.RegisterValidationRule(new EconomyContentValidationRule())</c>）。
    /// <para>
    /// ADR-0019 / F1b 退役说明：此前本类整条委托 <see cref="EconomyDataParser"/>（运行期解析器）把
    /// <c>econ.currency</c>/<c>econ.vendor</c> 任何结构问题统一转成一条 <c>economy_content</c>
    /// <see cref="ValidationIssue"/>，再额外核对 <c>sell_items[].price_currency_id</c> 是否命中已加载的
    /// <c>econ.currency</c> 记录。两张表的全部顶层字段（<c>id</c>/<c>name_key</c>/<c>cap</c>/
    /// <c>display_ref</c>/<c>buy_price_rule</c>/<c>map_id</c>）此前就已经在 <see cref="EconomySchemas"/>
    /// 登记为顶层 <see cref="FieldSchema"/>，经 <see cref="EconomyDataParser"/> 报出的结构性坏形状其实
    /// 早已与 <c>DataRegistry</c> 的内置字段校验（<c>required_field</c>/<c>field_type</c>/
    /// <c>text_key_exists</c>/<c>expr_parsable</c>）重复报告——这是本次一并收口的既存缺口，不是 F1b
    /// 新引入的问题。ADR-0019 / F1b 新登记 <c>sell_items</c> 子结构后（见 <see cref="EconomySchemas"/>
    /// 判断记录），<c>price_currency_id</c> 改用 <see cref="FieldKind.Reference"/>，交给
    /// <c>reference_integrity</c> 检查项自动核对——手写的货币存在性核对随之整条退役。本类不再调用
    /// <see cref="EconomyDataParser"/>，改为直接读取原始 JSON 只做登记表达不了的业务判断：
    /// </para>
    /// <list type="number">
    /// <item><description><c>sell_items[].price_amount &gt;= 0</c>——数值范围约束。</description></item>
    /// <item><description><c>sell_items[].stock_limit &gt;= 0</c>（提供时）——数值范围约束。</description></item>
    /// <item><description><c>sell_items[].restock_policy == "timer"</c> 时 <c>restock_timer</c>
    /// 必须提供且 &gt; 0——条件必填 + 数值范围的复合约束，<see cref="VariantSchema"/> 要求判别字段
    /// 必填而 <c>restock_policy</c> 本身是可选字段（缺省即 <see cref="VendorRestockPolicy.None"/>），
    /// 不适用；登记表达不了，保留为业务判断。</description></item>
    /// </list>
    /// <para>
    /// 对已被 <c>DataRegistry</c> 结构校验报过 <c>required_field</c>/<c>field_type</c> 的坏形状
    /// （<c>sell_items</c> 缺失/不是数组、元素不是对象、<c>price_amount</c>/<c>stock_limit</c>/
    /// <c>restock_timer</c> 类型不对）一律静默跳过，不重复报告。<see cref="EconomyDataParser"/> 本身
    /// 不变——运行期 <see cref="EconomyHost"/> 仍需要它对任意来源做完整解析。<c>item_id</c> 的存在性
    /// 本次不新增校验（此前也从未校验过，见 <see cref="EconomySchemas"/> 判断记录）。
    /// </para>
    /// </summary>
    public sealed class EconomyContentValidationRule : IValidationRule
    {
        private const string Check = "economy_content";

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var record in view.GetAll(EconomySchemas.Vendor.Name))
            {
                if (!record.TryGetArray("sell_items", out var itemsArr))
                {
                    continue; // 缺失/类型不对：结构层已报告 required_field/field_type。
                }

                for (var i = 0; i < itemsArr.Count; i++)
                {
                    if (!(itemsArr[i] is JsonObject itemObj))
                    {
                        continue; // 结构层已报告。
                    }

                    if (itemObj.TryGetValue("price_amount", out var amtVal) && amtVal is JsonNumber amtNum
                        && amtNum.TryGetInt64(out var priceAmount) && priceAmount < 0)
                    {
                        yield return Issue(record, $"sell_items 第 {i} 项 price_amount 不能为负数");
                    }

                    if (itemObj.TryGetValue("stock_limit", out var stockVal) && stockVal is JsonNumber stockNum
                        && stockNum.TryGetInt64(out var stockLimit) && stockLimit < 0)
                    {
                        yield return Issue(record, $"sell_items 第 {i} 项 stock_limit 不能为负数");
                    }

                    string? restockPolicy = null;
                    if (itemObj.TryGetValue("restock_policy", out var policyVal) && policyVal is JsonString policyStr)
                    {
                        restockPolicy = policyStr.Value;
                    }

                    if (restockPolicy == "timer")
                    {
                        var hasPositiveTimer =
                            itemObj.TryGetValue("restock_timer", out var timerVal)
                            && timerVal is JsonNumber timerNum
                            && timerNum.Value > 0;

                        if (!hasPositiveTimer)
                        {
                            yield return Issue(record, $"sell_items 第 {i} 项 restock_policy=timer 时必须提供正数 restock_timer");
                        }
                    }
                }
            }
        }

        private static ValidationIssue Issue(DataRecord record, string message) =>
            new ValidationIssue(
                ValidationSeverity.Error, EconomySchemas.Vendor.Name, Check, message,
                recordKey: record.Key, field: "sell_items");
    }
}
