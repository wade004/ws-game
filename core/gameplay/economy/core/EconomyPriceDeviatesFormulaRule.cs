using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// 手填价格偏离公式（分阶段落地计划 T-N4-6；ADR-0034 决策 2；04 第 5 节数值类校验项警告级
    /// "手填价格偏离公式：<c>econ.vendor.sell_items[].priceAmount</c> 或
    /// <c>item.template.value_override</c> 与价值公式的偏离超过带宽"）。
    /// <para>
    /// 判断记录（检查名——04 第 5 节该行原文未给出具体检查名，与该节其它同类警告行"检查名……设计层
    /// 裁定"同一惯例）：设计层裁定（2026-09-16）：采纳 <see cref="Check"/>=<c>econ_price_deviates_formula</c>；
    /// <see cref="NonEscalatable"/> 为 <c>true</c>——04 第 5 节警告组整体"抓意图不抓手滑"口径，
    /// 同 <c>Core.Carriers.Item.ItemWeaponDamageDeviatesDpsCurveRule</c>。
    /// </para>
    /// <para>
    /// 判断记录（两处手填、同一比较基准）：本规则核对两处独立的手填数值——
    /// <c>sell_items[].price_amount</c>（手填买价）与 <c>item.template.value_override</c>（手填基准
    /// 价值）——各自与 <see cref="EconomyPriceFormula.TryComputeFormulaValue"/> 算出的纯公式值
    /// （忽略 <c>value_override</c> 本身，见该方法判断记录）比较偏离比例；两者中任一无法解析
    /// （模板缺 <c>item_level</c>、曲线未加载、公式值 &lt;= 0 无法计算相对偏离）均静默跳过，不报告。
    /// </para>
    /// </summary>
    public sealed class EconomyPriceDeviatesFormulaRule : IValidationRule
    {
        public const string Check = "econ_price_deviates_formula";

        /// <summary>偏离阈值缺省值（见 <see cref="Core.Gameplay.Economy.EconomyOptions.PriceDeviationWarningThreshold"/>
        /// 判断记录：与同组既有警告规则 <c>ItemWeaponDamageDeviatesDpsCurveRule.DefaultDeviationThreshold</c>
        /// 一致，04 第 5 节本行未给出具体阈值，按同组既定阈值类推）。</summary>
        public const double DefaultDeviationThreshold = 0.2;

        public bool NonEscalatable => true;

        private readonly Id _valueCurveId;
        private readonly double _deviationThreshold;

        /// <summary>沿用缺省偏离阈值（<see cref="DefaultDeviationThreshold"/>）的构造签名。</summary>
        public EconomyPriceDeviatesFormulaRule(Id valueCurveId)
            : this(valueCurveId, DefaultDeviationThreshold)
        {
        }

        /// <summary>显式指定偏离阈值的构造重载。</summary>
        public EconomyPriceDeviatesFormulaRule(Id valueCurveId, double deviationThreshold)
        {
            _valueCurveId = valueCurveId;
            _deviationThreshold = deviationThreshold;
        }

        public IEnumerable<ValidationIssue> Validate(IDataRegistryView view)
        {
            foreach (var template in view.GetAll(EconomyPriceFormula.ItemTemplateTable))
            {
                if (!template.TryGetNumber("value_override", out var overrideValue))
                {
                    continue;
                }

                var formulaValue = EconomyPriceFormula.TryComputeFormulaValue(view, template, _valueCurveId);
                if (!formulaValue.HasValue || formulaValue.Value <= 0)
                {
                    continue;
                }

                var deviation = Math.Abs(overrideValue - formulaValue.Value) / formulaValue.Value;
                if (deviation > _deviationThreshold)
                {
                    yield return new ValidationIssue(
                        ValidationSeverity.Warning, EconomyPriceFormula.ItemTemplateTable, Check,
                        $"value_override {overrideValue:0.###} 偏离价值公式期望值 {formulaValue.Value:0.###} 达 " +
                        $"{deviation:P1}，超过阈值 {_deviationThreshold:P0}：抓意图不抓手滑，可能是价值覆盖忘记" +
                        "跟随物品等级/品质/槽位调整",
                        recordKey: template.Key, field: "value_override");
                }
            }

            foreach (var vendor in view.GetAll(EconomySchemas.Vendor.Name))
            {
                if (!vendor.TryGetArray("sell_items", out var itemsArr))
                {
                    continue; // 结构层已报告 required_field/field_type，本规则不重复。
                }

                for (var i = 0; i < itemsArr.Count; i++)
                {
                    if (!(itemsArr[i] is JsonObject itemObj) ||
                        !itemObj.TryGetValue("item_id", out var itemIdRaw) || !(itemIdRaw is JsonString itemIdStr) ||
                        !itemObj.TryGetValue("price_amount", out var amtRaw) || !(amtRaw is JsonNumber amtNum))
                    {
                        continue; // 未填 price_amount：走公式，没有"手填值"可比较，不报。
                    }

                    var templateRecord = view.Get(EconomyPriceFormula.ItemTemplateTable, itemIdStr.Value);
                    if (templateRecord == null)
                    {
                        continue;
                    }

                    var formulaValue = EconomyPriceFormula.TryComputeFormulaValue(view, templateRecord, _valueCurveId);
                    if (!formulaValue.HasValue || formulaValue.Value <= 0)
                    {
                        continue;
                    }

                    var deviation = Math.Abs(amtNum.Value - formulaValue.Value) / formulaValue.Value;
                    if (deviation > _deviationThreshold)
                    {
                        yield return new ValidationIssue(
                            ValidationSeverity.Warning, EconomySchemas.Vendor.Name, Check,
                            $"sell_items 第 {i} 项 price_amount {amtNum.Value:0.###} 偏离价值公式期望值 " +
                            $"{formulaValue.Value:0.###} 达 {deviation:P1}，超过阈值 {_deviationThreshold:P0}" +
                            $"（item_id={itemIdStr.Value}）：抓意图不抓手滑，可能是手填价格忘记跟随物品等级" +
                            "调整",
                            recordKey: vendor.Key, field: "sell_items");
                    }
                }
            }
        }
    }
}
