using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// <c>econ.value_curve</c> 价格公式（分阶段落地计划 T-N4-6；ADR-0034 决策 2；08 第 7.4 节
    /// "基准价值 = econ.value_curve(item_level) × item.quality_definition.price_multiplier ×
    /// item.slot_definition.price_coefficient；买价 = 基准价值（或 item.template.value_override）；
    /// 售价 = 基准价值 × 售价比例"）。<see cref="EconomyHost"/>（运行期 <c>Buy</c>/<c>Sell</c>）与
    /// <see cref="EconomyPriceDeviatesFormulaRule"/>（内容校验，比较手填价格与公式的偏离）共用本
    /// 静态类型的同一份算法，不各自重复实现一遍曲线×倍率×系数（惯例同
    /// <c>core/carriers/item.ItemBudgetCurve</c> 服务运行时与校验两侧）。
    /// <para>
    /// 判断记录（<c>value_override</c> 是"基准价值覆盖"，不是曲线输入的覆盖）：
    /// <c>item.template.value_override</c> 字段类型注释（T-N2-1 登记）与本任务书"N2 先例：
    /// <c>item.template.value_override</c>（T-N2-1：模板级价值覆盖——价格公式应优先
    /// <c>value_override</c>）"均指向"该字段存在时整体取代基准价值本身"，不是"取代曲线部分、
    /// 品质/槽位倍率仍叠加"——<see cref="TryComputeBaseValue"/> 因此在 <c>value_override</c> 存在时
    /// 直接返回该值，跳过曲线/品质/槽位三项查表；<see cref="TryComputeFormulaValue"/>
    /// （纯公式值，忽略 <c>value_override</c>）专供偏离警告用作比较基准——警告的意图正是"这个手填值
    /// （<c>value_override</c> 或 <c>price_amount</c>）偏离公式本该算出的值多远"，比较基准不能是
    /// 手填值自己。
    /// </para>
    /// <para>
    /// 判断记录（模板/曲线数据缺失时返回 <c>null</c> 而不是 0 或抛异常）：<see cref="EconomyHost"/>
    /// 与 <see cref="EconomyPriceDeviatesFormulaRule"/> 各自对"公式算不出来"这一情形有不同的兜底
    /// 口径（前者回退旧的"任一商人手填价格"以保证既有隔离测试场景不受影响，见该类型
    /// <c>ComputeSellPrice</c> 判断记录；后者直接跳过该条记录、不产生告警，因为没有公式值就无从
    /// 比较偏离）——两处口径不同，因此本类型只负责"算得出就返回值，算不出就返回 null"，把"算不出
    /// 时怎么办"这一决策留给各自调用方。
    /// </para>
    /// </summary>
    public static class EconomyPriceFormula
    {
        /// <summary><c>item.template</c> 表名（同 <c>core/gameplay/loot.LootHost</c>
        /// <c>ItemTemplateTable</c> 判断记录：L4 按表名字符串读取 L3 记录，不引用
        /// <c>Core.Carriers.Item</c> 的强类型 schema 常量，避免本模块与该模块产生编译期硬耦合）。</summary>
        public const string ItemTemplateTable = "item.template";

        public const string ItemQualityDefinitionTable = "item.quality_definition";

        public const string ItemSlotDefinitionTable = "item.slot_definition";

        /// <summary>
        /// 纯公式值（忽略 <c>value_override</c>）：<c>econ.value_curve(item_level) × 品质价格倍率
        /// （未登记 <c>item.quality_definition</c> 记录或未填 <c>price_multiplier</c> 时按 1.0）×
        /// 槽位价格系数（未登记 <c>item.slot_definition</c> 记录或未填 <c>price_coefficient</c> 时按
        /// 1.0）</c>。<paramref name="templateRecord"/> 缺少合法 <c>item_level</c>，或
        /// <paramref name="valueCurveId"/> 对应的 <c>econ.value_curve</c> 记录未加载，均返回
        /// <c>null</c>（"无法算出"，不是"算出 0"）。
        /// </summary>
        public static double? TryComputeFormulaValue(IDataRegistryView view, DataRecord templateRecord, Id valueCurveId)
        {
            if (!templateRecord.TryGetInt("item_level", out var itemLevel))
            {
                return null;
            }

            var curveRecord = view.Get(EconomySchemas.ValueCurve.Name, valueCurveId);
            if (curveRecord == null)
            {
                return null;
            }

            var curve = CurveSchema.ReadBreakpoints(curveRecord, "entries");
            var value = curve.Evaluate((int)itemLevel);

            if (templateRecord.TryGetId("quality", out var qualityId))
            {
                var qualityRecord = view.Get(ItemQualityDefinitionTable, qualityId);
                if (qualityRecord != null && qualityRecord.TryGetNumber("price_multiplier", out var qualityMultiplier))
                {
                    value *= qualityMultiplier;
                }
            }

            if (templateRecord.TryGetId("slot", out var slotId))
            {
                var slotRecord = view.Get(ItemSlotDefinitionTable, slotId);
                if (slotRecord != null && slotRecord.TryGetNumber("price_coefficient", out var slotCoefficient))
                {
                    value *= slotCoefficient;
                }
            }

            return value;
        }

        /// <summary>
        /// 基准价值：<c>item.template.value_override</c> 存在时直接返回该值（整体取代，见类型判断
        /// 记录）；否则委托 <see cref="TryComputeFormulaValue"/>。两者都不可用时返回 <c>null</c>。
        /// </summary>
        public static double? TryComputeBaseValue(IDataRegistryView view, DataRecord templateRecord, Id valueCurveId)
        {
            if (templateRecord.TryGetNumber("value_override", out var overrideValue))
            {
                return overrideValue;
            }

            return TryComputeFormulaValue(view, templateRecord, valueCurveId);
        }
    }
}
