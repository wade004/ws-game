using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// <c>econ.currency</c>/<c>econ.vendor</c> 专属校验规则（惯例同
    /// <c>core/carriers/creature.CreatureContentValidationRule</c>：不由 <c>data_registry</c> 自动
    /// 注册，需组装层/测试显式 <c>registry.RegisterValidationRule(new EconomyContentValidationRule())</c>）。
    /// 结构/取值范围校验复用 <see cref="EconomyDataParser"/>（解析失败即报错）；本类另外核对
    /// <c>sell_items[].price_currency_id</c> 是否命中已加载的 <c>econ.currency</c> 记录——这一步不能
    /// 放进 <see cref="EconomySchemas"/> 的字段声明（<c>price_currency_id</c> 嵌在数组元素里，不是
    /// 顶层字段，见该类型注释）。
    /// </summary>
    public sealed class EconomyContentValidationRule : IValidationRule
    {
        private const string Check = "economy_content";

        private readonly IExprSchema? _conditionSchema;

        /// <summary><paramref name="conditionSchema"/> 见 <c>EconomyDataParser</c>/<c>EconomyHost</c>
        /// 判断记录：未提供时默认 <c>RulesExprSchema.Base</c>。</summary>
        public EconomyContentValidationRule(IExprSchema? conditionSchema = null)
        {
            _conditionSchema = conditionSchema;
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            var currencyIds = new HashSet<Id>();
            foreach (var record in view.GetAll(EconomySchemas.Currency.Name))
            {
                DataFieldException? failure = null;
                try
                {
                    currencyIds.Add(EconomyDataParser.ParseCurrency(record).Id);
                }
                catch (DataFieldException ex)
                {
                    // CS1631：catch 子句体内不允许 yield return，先捕获异常引用，离开 catch 块后再产出
                    // （同 core/gameplay/loot.LootContentValidationRule 判断记录）。
                    failure = ex;
                }

                if (failure != null)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, EconomySchemas.Currency.Name, Check, failure.Message, recordKey: record.Key);
                }
            }

            foreach (var record in view.GetAll(EconomySchemas.Vendor.Name))
            {
                VendorDef? def = null;
                DataFieldException? failure = null;
                try
                {
                    def = EconomyDataParser.ParseVendor(record, _conditionSchema);
                }
                catch (DataFieldException ex)
                {
                    failure = ex;
                }

                if (failure != null)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Error, EconomySchemas.Vendor.Name, Check, failure.Message, recordKey: record.Key);
                    continue;
                }

                foreach (var sellItem in def!.SellItems)
                {
                    if (!currencyIds.Contains(sellItem.PriceCurrencyId))
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Error, EconomySchemas.Vendor.Name, Check,
                            $"sell_items 引用了未登记的货币 \"{sellItem.PriceCurrencyId}\"",
                            recordKey: record.Key, field: "sell_items");
                    }
                }
            }
        }
    }
}
