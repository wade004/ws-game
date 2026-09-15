using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Numbers.StatBlock;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <c>item.budget_curve.entries</c> 的解析与插值（见 07 第 1.2 节"给定 item_level 与 quality，算出
    /// 该件物品全部 stats 条目允许消耗的预算总量上限……具体系数曲线由数据……item_level → budget
    /// 的分段曲线定义"）。<see cref="ItemBudgetValidationRule"/> 与未来可能需要"预算余量"信息的
    /// 内容工具共用本静态类型，不各自重复实现。
    /// <para>
    /// 分阶段落地计划 T-N0-4（落地清单 2.1 C2/C4）：本类型不再自带插值循环——解析委托
    /// <see cref="CurveSchema.ReadBreakpoints(DataRecord, string)"/>、插值委托
    /// <see cref="PiecewiseCurve.Evaluate"/>（式子与迁移前逐运算相同，见 <see cref="PiecewiseCurve"/>
    /// 判断记录 2，既有数据迁移后结果逐位一致）。既有公开签名 <see cref="ParseEntries"/>/
    /// <see cref="Interpolate(IReadOnlyList{ValueTuple{int, double}}, int)"/> 保留为兼容 façade
    /// （ABI/源码兼容），新调用方用 <see cref="ParseCurve"/>/<see cref="Interpolate(PiecewiseCurve, int)"/>
    /// 免去每次插值重建曲线。
    /// </para>
    /// <para>
    /// 判断记录（旧字段名读取路径，T-N0-4 禁止事项"禁止删除旧字段读取路径"）：v1 的
    /// <c>{item_level, budget}</c> 元素在经 <see cref="DataRegistry"/> 加载时已由 1→2 迁移环节改名为
    /// <c>{x, y}</c>；<see cref="ParseEntries"/>/<see cref="ParseCurve"/> 对未经迁移、直接构造的
    /// <see cref="DataRecord"/>（测试与工具场景）仍接受 v1 元素名——先按 <c>{x, y}</c> 读，读不到再按
    /// <c>{item_level, budget}</c> 读，两者都缺才抛异常。
    /// </para>
    /// </summary>
    public static class ItemBudgetCurve
    {
        private const string LegacyXName = "item_level";
        private const string LegacyYName = "budget";

        /// <summary>解析 <paramref name="curveRecord"/> 的 <c>entries</c> 字段为 <see cref="PiecewiseCurve"/>
        /// （按 <c>x</c> 稳定排序）。元素既不是 <c>{x, y}</c> 也不是 v1 的 <c>{item_level, budget}</c> 时抛
        /// <see cref="ArgumentException"/>（沿本类型既有异常类型）。</summary>
        public static PiecewiseCurve ParseCurve(DataRecord curveRecord)
        {
            if (curveRecord == null) throw new ArgumentNullException(nameof(curveRecord));

            var raw = curveRecord.GetArray("entries");
            var points = new CurvePoint[raw.Count];
            for (var i = 0; i < raw.Count; i++)
            {
                if (!(raw[i] is JsonObject obj))
                {
                    throw new ArgumentException(
                        $"item.budget_curve \"{curveRecord.Key}\" 的 entries 元素不是对象");
                }

                if (!TryReadNumber(obj, CurveSchema.XFieldName, out var x) || !TryReadNumber(obj, CurveSchema.YFieldName, out var y))
                {
                    if (!TryReadNumber(obj, LegacyXName, out x) || !TryReadNumber(obj, LegacyYName, out y))
                    {
                        throw new ArgumentException(
                            $"item.budget_curve \"{curveRecord.Key}\" 的 entries 元素缺少 x/y（或 v1 的 item_level/budget）");
                    }
                }

                points[i] = new CurvePoint(x, y);
            }

            return new PiecewiseCurve(points);
        }

        /// <summary>按 <paramref name="itemLevel"/> 在 <paramref name="curve"/> 上取预算上限（线性插值、
        /// 越界夹取到端点、空曲线为 0，语义同迁移前）。</summary>
        public static double Interpolate(PiecewiseCurve curve, int itemLevel)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            return curve.Evaluate(itemLevel);
        }

        /// <summary>兼容 façade：解析 <paramref name="curveRecord"/> 的 <c>entries</c> 并按物品等级升序
        /// 返回 <c>(ItemLevel, Budget)</c> 元组列表（迁移前的返回形态；<c>x</c> 截断为整数与迁移前
        /// <c>(int)levelNum.Value</c> 一致）。新调用方请用 <see cref="ParseCurve"/>。</summary>
        public static IReadOnlyList<(int ItemLevel, double Budget)> ParseEntries(DataRecord curveRecord)
        {
            var curve = ParseCurve(curveRecord);
            var result = new List<(int, double)>(curve.Count);
            for (var i = 0; i < curve.Count; i++)
            {
                result.Add(((int)curve.Points[i].X, curve.Points[i].Y));
            }
            return result;
        }

        /// <summary>兼容 façade：在 <see cref="ParseEntries"/> 返回的元组列表上按 <paramref name="itemLevel"/>
        /// 插值（每次调用重建一条 <see cref="PiecewiseCurve"/>；热路径请改用
        /// <see cref="Interpolate(PiecewiseCurve, int)"/>）。</summary>
        public static double Interpolate(IReadOnlyList<(int ItemLevel, double Budget)> entries, int itemLevel)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            var points = new CurvePoint[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                points[i] = new CurvePoint(entries[i].ItemLevel, entries[i].Budget);
            }

            return new PiecewiseCurve(points).Evaluate(itemLevel);
        }

        /// <summary>一件物品 <c>stats</c> 条目消耗的预算总量：Σ|value|，<c>pct</c>/<c>mult</c> 按
        /// ×100 折算（见任务书"Σ|stats.value|（pct/mult 按 ×100 折算）"，即百分比/乘区类修正的
        /// "1.0" 记作"100 点预算"，与 <c>flat</c> 修正的数值刻度对齐）。
        /// <para>
        /// 判断记录（T-N2-3，本方法保留但不再被 <see cref="ItemBudgetValidationRule"/> 调用）：ADR-0032
        /// 决策 3 把消耗公式改为 <see cref="ComputeConsumed"/> 的加权 <c>(Σ(值×权重)^k)^(1/k)</c>，本方法
        /// 是该公式落地前的旧式子（等价于该公式在"权重全 1、k=1"下的特例）——硬性规则 5（ABI 只允许
        /// 新增，既有公开签名不得删除）与"保留读取兼容一个周期"的一贯口径，本方法原样保留，仅不再是
        /// 校验规则的调用路径，供仍直接引用它的外部工具/测试继续编译通过。
        /// </para></summary>
        public static double SumConsumed(JsonArray stats)
        {
            double total = 0;
            foreach (var item in stats)
            {
                if (!(item is JsonObject obj) || !obj.TryGetValue("value", out var valueRaw) ||
                    !(valueRaw is JsonNumber valueNum))
                {
                    continue;
                }

                var value = Math.Abs(valueNum.Value);
                var op = obj.TryGetValue("op", out var opRaw) && opRaw is JsonString opStr ? opStr.Value : "flat";
                if (op == "pct" || op == "mult")
                {
                    value *= 100.0;
                }

                total += value;
            }

            return total;
        }

        /// <summary>消耗公式默认指数 <c>k</c>（ADR-0032 决策 3"k 默认 1.5，数据配置"；登记位置见
        /// <see cref="ItemSchemas.BudgetCurve"/> 判断记录）。</summary>
        public const double DefaultExponent = 1.5;

        /// <summary>缺省属性权重（<see cref="BuildStatBudgetInfo"/> 判断记录："stat.weight 没有该属性
        /// 对应记录"（不同于显式登记 <c>weight: 0</c>）时的回退值）。</summary>
        public const double DefaultWeight = 1.0;

        /// <summary>
        /// 一个 <c>stat.definition</c> 在预算消耗公式（<see cref="ComputeConsumed"/>）里用得到的静态
        /// 信息：权重、是否 <c>percent</c> 分类、（<c>percent</c> 分类且引用了 <c>stat.rating_conversion</c>
        /// 时）换算曲线形态。由 <see cref="BuildStatBudgetInfo"/> 一次性从 <see cref="IDataRegistryView"/>
        /// 构建，供同一次校验内逐模板复用，不必每条记录各自重新查表。
        /// </summary>
        public readonly struct StatBudgetInfo
        {
            /// <summary>一点该属性相当于多少点主属性当量（<c>stat.weight.weight</c>，见 <see
            /// cref="BuildStatBudgetInfo"/> 判断记录：没有对应 <c>stat.weight</c> 记录时按 <see
            /// cref="DefaultWeight"/> 回退，不是 0）。</summary>
            public double Weight { get; }

            /// <summary><c>stat.definition.category == "percent"</c>。</summary>
            public bool IsPercentCategory { get; }

            /// <summary>该属性引用的换算曲线形态；<c>null</c> 表示恒等换算（未引用
            /// <c>stat.rating_conversion</c>，或 <see cref="IsPercentCategory"/> 为 <c>false</c>）。</summary>
            public RatingConversionShape? ConversionShape { get; }

            /// <summary><see cref="ConversionShape"/> 为 <see cref="RatingConversionShape.Breakpoints"/>
            /// 时有意义。</summary>
            public PiecewiseCurve? ConversionCurve { get; }

            /// <summary><see cref="ConversionShape"/> 为 <see cref="RatingConversionShape.Saturation"/>
            /// 时有意义。</summary>
            public double ConversionK { get; }

            /// <summary><see cref="ConversionShape"/> 为 <see cref="RatingConversionShape.Saturation"/>
            /// 时有意义。</summary>
            public double ConversionCap { get; }

            public StatBudgetInfo(double weight, bool isPercentCategory, RatingConversionShape? conversionShape,
                PiecewiseCurve? conversionCurve, double conversionK, double conversionCap)
            {
                Weight = weight;
                IsPercentCategory = isPercentCategory;
                ConversionShape = conversionShape;
                ConversionCurve = conversionCurve;
                ConversionK = conversionK;
                ConversionCap = conversionCap;
            }
        }

        /// <summary>
        /// 从 <paramref name="view"/> 读取 <c>stat.definition</c>/<c>stat.weight</c>/
        /// <c>stat.rating_conversion</c> 三张（L1 <c>core/numbers/stat_block</c>）表，为每个已登记
        /// <c>stat.definition</c> 构建一条 <see cref="StatBudgetInfo"/>（分阶段落地计划 T-N2-3；ADR-0032
        /// 决策 3）。
        /// <para>
        /// 判断记录（缺省权重取 <see cref="DefaultWeight"/>=1，不是 0）：<c>stat.weight</c> 表类型
        /// 顶部判断记录已明确"<c>weight: 0</c> 是'这个属性不计入预算消耗'的合法显式表达"——但那说的是
        /// <b>显式登记</b>为 0 的记录，不是"这个属性在 <c>stat.weight</c> 里根本没有记录"。本任务把
        /// 两者区分处理：没有记录时按 <see cref="DefaultWeight"/>=1 回退（相当于"未特别配置权重的属性
        /// 按一点主属性当量计入"），显式登记 <c>weight: 0</c> 时按 0 计入（term=0，不贡献消耗）——理由
        /// 一是避免"游戏层尚未给全部属性登记权重"这一过渡态下消耗公式整体退化为恒 0（预算利用率警告
        /// 会对每件装备无差别命中，校验形同虚设）；二是本模块既有测试夹具（<c>ItemValidationRulesTests
        /// .BuildView</c>）与 T-N2-1/2 既有样例均未注册 <c>stat.weight</c> 表，按 1 回退使既有
        /// <c>BudgetRule_WithinBudget_NoIssue</c>/<c>BudgetRule_ExceedsBudget_ReportsIssue</c>/
        /// <c>BudgetRule_PctOp_FoldedByHundred_ExceedsBudget</c> 三条既有用例（单一属性词条，权重取 1
        /// 时消耗值与迁移前 <see cref="SumConsumed"/> 逐位相同）无需改动即继续成立——ADR-0030
        /// 决策 7/07 第 1.2 节均未明文规定"缺省权重"取值——设计层裁定（2026-09-15）：采纳。
        /// </para>
        /// <para>
        /// 判断记录（不读 <c>stat.weight.class_overrides</c>）：ADR-0030 决策 7 允许按职业覆盖权重，
        /// 但内容校验阶段（<c>ItemBudgetValidationRule</c>）核算的是"这件物品模板"本身，不针对任何
        /// 具体职业/角色——没有"当前职业"这个上下文可供按覆盖表查找。本方法只读顶层 <c>weight</c> 字段
        /// （基础权重），不展开 <c>class_overrides</c>；装备评分（T-N2-4，按职业权重）与本方法服务的
        /// 场景不同，届时自行按需处理覆盖。
        /// </para>
        /// </summary>
        public static IReadOnlyDictionary<Id, StatBudgetInfo> BuildStatBudgetInfo(IDataRegistryView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            var (categories, conversions) = BuildCategoriesAndConversions(view);

            var weights = new Dictionary<Id, double>();
            foreach (var w in view.GetAll("stat.weight"))
            {
                if (w.TryGetId("stat", out var statId) && w.TryGetNumber("weight", out var weight))
                {
                    weights[statId] = weight;
                }
            }

            return ComposeStatBudgetInfo(categories, conversions, statId => weights.TryGetValue(statId, out var w2) ? w2 : DefaultWeight);
        }

        /// <summary>
        /// T-N2-4（ADR-0032 决策 9；07 第 1.2 节"装备评分"）判断记录：与 <see cref="BuildStatBudgetInfo
        /// (IDataRegistryView)"/>（不展开 <c>class_overrides</c>，服务"这件物品模板本身"的内容校验
        /// 场景，见该重载判断记录）不同，本重载额外接受 <paramref name="classId"/>——装备评分场景
        /// 天然有"当前职业"这个上下文（服务比较箭头/一键换装/掉落升级提示，都是站在某个职业玩家
        /// 视角）。对每个属性优先查找该属性对应 <c>stat.weight</c> 记录的 <c>class_overrides</c> 里
        /// <c>class == classId</c> 的条目（命中则用该条目的 <c>weight</c>），未命中该覆盖时回退到
        /// 同一条记录顶层的 <c>weight</c> 字段，该属性完全没有 <c>stat.weight</c> 记录时回退 <see
        /// cref="DefaultWeight"/>——三级回退优先级与既有重载完全一致，只是多插入"职业覆盖"这一层
        /// 最高优先级查找，供 <see cref="EquipmentScoreAnalyzer"/> 消费。
        /// </summary>
        public static IReadOnlyDictionary<Id, StatBudgetInfo> BuildStatBudgetInfo(IDataRegistryView view, Id classId)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            var (categories, conversions) = BuildCategoriesAndConversions(view);

            var weights = new Dictionary<Id, double>();
            foreach (var w in view.GetAll("stat.weight"))
            {
                if (!w.TryGetId("stat", out var statId) || !w.TryGetNumber("weight", out var baseWeight))
                {
                    continue;
                }

                var resolvedWeight = baseWeight;
                if (w.TryGetArray("class_overrides", out var overrides))
                {
                    foreach (var item in overrides)
                    {
                        if (item is JsonObject ov &&
                            ov.TryGetValue("class", out var classRaw) && classRaw is JsonString classStr &&
                            string.Equals(classStr.Value, classId.Value, StringComparison.Ordinal) &&
                            ov.TryGetValue("weight", out var overrideWeightRaw) && overrideWeightRaw is JsonNumber overrideWeightNum)
                        {
                            resolvedWeight = overrideWeightNum.Value;
                            break;
                        }
                    }
                }

                weights[statId] = resolvedWeight;
            }

            return ComposeStatBudgetInfo(categories, conversions, statId => weights.TryGetValue(statId, out var w2) ? w2 : DefaultWeight);
        }

        private static (Dictionary<Id, (bool IsPercent, Id? ConversionRef)> Categories,
            Dictionary<Id, (RatingConversionShape Shape, PiecewiseCurve? Curve, double K, double Cap)> Conversions)
            BuildCategoriesAndConversions(IDataRegistryView view)
        {
            var categories = new Dictionary<Id, (bool IsPercent, Id? ConversionRef)>();
            foreach (var def in view.GetAll("stat.definition"))
            {
                var isPercent = def.TryGetString("category", out var category) && category == "percent";
                var conversionRef = def.TryGetId("conversion_ref", out var refId) ? (Id?)refId : null;
                categories[def.GetId("id")] = (isPercent, conversionRef);
            }

            var conversions = new Dictionary<Id, (RatingConversionShape Shape, PiecewiseCurve? Curve, double K, double Cap)>();
            foreach (var conv in view.GetAll("stat.rating_conversion"))
            {
                if (conv.TryGetArray("entries", out var entriesRaw) && entriesRaw.Count > 0)
                {
                    if (CurveSchema.TryReadBreakpoints(entriesRaw, out var curve, out _))
                    {
                        conversions[conv.GetId("id")] = (RatingConversionShape.Breakpoints, curve, 0, 0);
                    }
                    continue;
                }

                if (conv.TryGetObject("saturation", out var satObj) &&
                    TryReadNumber(satObj, CurveSchema.SaturationKFieldName, out var k))
                {
                    var cap = TryReadNumber(satObj, CurveSchema.SaturationCapFieldName, out var capValue) ? capValue : 1.0;
                    conversions[conv.GetId("id")] = (RatingConversionShape.Saturation, null, k, cap);
                }
            }

            return (categories, conversions);
        }

        private static IReadOnlyDictionary<Id, StatBudgetInfo> ComposeStatBudgetInfo(
            Dictionary<Id, (bool IsPercent, Id? ConversionRef)> categories,
            Dictionary<Id, (RatingConversionShape Shape, PiecewiseCurve? Curve, double K, double Cap)> conversions,
            Func<Id, double> resolveWeight)
        {
            var result = new Dictionary<Id, StatBudgetInfo>();
            foreach (var pair in categories)
            {
                var statId = pair.Key;
                var (isPercent, conversionRef) = pair.Value;
                var weight = resolveWeight(statId);

                RatingConversionShape? shape = null;
                PiecewiseCurve? curve = null;
                double k2 = 0, cap2 = 0;
                if (isPercent && conversionRef.HasValue && conversions.TryGetValue(conversionRef.Value, out var conv2))
                {
                    shape = conv2.Shape;
                    curve = conv2.Curve;
                    k2 = conv2.K;
                    cap2 = conv2.Cap;
                }

                result[statId] = new StatBudgetInfo(weight, isPercent, shape, curve, k2, cap2);
            }

            return result;
        }

        /// <summary>
        /// 装备预算消耗公式（分阶段落地计划 T-N2-3；ADR-0032 决策 3；07 第 1.2 节修订段；数值总纲
        /// 第 4.4 节）：<c>实际消耗 = (Σ(属性值_i × 权重_i)^k)^(1/k)</c>。
        /// <para>
        /// 判断记录（"属性值"取哪个量，op 与 category 的组合语义——设计层裁定（2026-09-15）：
        /// 采纳）：ADR-0032/07/数值总纲三处原文均只给"百分比属性先经换算曲线折回点数
        /// 再乘权重"一句，没有展开到"op=flat/pct/mult 分别对应什么"。本方法的操作化定义依据
        /// <c>EquipmentHost.ApplyGrants</c>（<c>stats[]</c> 元素的权威消费者，<see cref="ItemSchemas
        /// .StatsItemSchema"/> 判断记录引用）与 <c>StatHost.ComputeFinal</c> 的运行时聚合管线
        /// （<c>op=flat</c> 的值贡献进"换算前点数"和、随后 <c>category=percent</c> 的属性整体过一次
        /// <c>ConvertRating</c>；<c>op=pct</c>/<c>mult</c> 的值直接乘进换算<em>之后</em>的百分比/乘区，
        /// 不经过换算层）反推：
        /// <list type="bullet">
        /// <item><description><c>category != percent</c>：<c>op=flat</c> 的值就是"属性值"本身；
        /// <c>op=pct</c>/<c>mult</c> 沿用 <see cref="SumConsumed"/> 既有 ×100 折算（未变，非本任务
        /// 改动范围——该属性没有"点数"概念，×100 只是把百分比/乘区量纲折算到与 flat 数值同一尺度）。
        /// </description></item>
        /// <item><description><c>category == percent</c> 且 <c>op=flat</c>：值已经是"点数"（运行时
        /// 直接贡献进换算前的 flat 和），不需要再经换算曲线——"折回点数"这一步对它是恒等操作。
        /// </description></item>
        /// <item><description><c>category == percent</c> 且 <c>op=pct</c>/<c>mult</c>：值是作者按
        /// "最终百分比效果"填写的（运行时绕开换算层直接乘进百分比/乘区），这正是 ADR-0032 原文"先经
        /// 换算曲线折回点数"要处理的那一种——经 <see cref="RatingConversionEvaluator.ToPoints"/>（该
        /// 属性引用的换算曲线的反函数）折算回点数，折算所需的"单位等级"取 <paramref name="level"/>
        /// （见 <see cref="ComputeConsumed"/> 判断记录"等级输入"）；未引用换算曲线（恒等曲线）时折算
        /// 是恒等操作，值本身就是点数。</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// 判断记录（等级输入）：换算需要"单位等级"（<see cref="RatingConversionEvaluator.ToPoints"/>
        /// 第二参数）。装备校验期没有运行中的单位，只有物品模板数据——设计层裁定（2026-09-15）：
        /// 采纳，本方法用
        /// 调用方传入的 <paramref name="level"/>，<see cref="ItemBudgetValidationRule"/> 一侧按物品
        /// 等级（<c>item.template.item_level</c>）传入，不是 <c>requirements.level</c>（后者未填时
        /// 本就要靠 <c>item.req_level_curve</c> 反推，随 T-N2-9 落地，本任务不依赖它；物品等级本身
        /// 是当前记录必填字段，直接可用，且与预算上限公式的横轴同一量纲，口径一致）。
        /// </para>
        /// <para>
        /// 判断记录（权重为 0 且折算结果为无穷大时不产生 NaN）：<c>weight == 0</c> 时直接把该项计为
        /// 0（不先算 <c>points × 0</c>）——<see cref="RatingConversionEvaluator.ToPoints"/> 在
        /// <c>percent &gt;= 1</c>（饱和曲线永远达不到的百分比，见该方法判断记录）时返回
        /// <see cref="double.PositiveInfinity"/>，<c>Infinity × 0</c> 在 IEEE754 下是 <c>NaN</c>，会
        /// 把整件物品的消耗污染成 <c>NaN</c>（<c>NaN &gt; budget</c> 恒为 false，超预算阻断校验因此
        /// 被静默绕过）——用户判断权重 0 语义是"不计入"，提前短路避免这一浮点陷阱。</para>
        /// </summary>
        public static double ComputeConsumed(
            JsonArray stats,
            IReadOnlyDictionary<Id, StatBudgetInfo> statInfo,
            int level,
            double exponent = DefaultExponent)
        {
            if (stats == null) throw new ArgumentNullException(nameof(stats));
            if (statInfo == null) throw new ArgumentNullException(nameof(statInfo));

            double sumPow = 0;
            foreach (var item in stats)
            {
                if (!(item is JsonObject obj) || !obj.TryGetValue("value", out var valueRaw) ||
                    !(valueRaw is JsonNumber valueNum))
                {
                    continue;
                }

                var raw = Math.Abs(valueNum.Value);
                var op = obj.TryGetValue("op", out var opRaw) && opRaw is JsonString opStr ? opStr.Value : "flat";

                var weight = DefaultWeight;
                var isPercent = false;
                StatBudgetInfo info = default;
                var hasInfo = false;
                if (obj.TryGetValue("stat", out var statRaw) && statRaw is JsonString statStr &&
                    statInfo.TryGetValue(new Id(statStr.Value), out info))
                {
                    hasInfo = true;
                    weight = info.Weight;
                    isPercent = info.IsPercentCategory;
                }

                double points;
                if (isPercent)
                {
                    if (op == "flat")
                    {
                        points = raw;
                    }
                    else if (hasInfo && info.ConversionShape.HasValue)
                    {
                        points = RatingConversionEvaluator.ToPoints(
                            info.ConversionShape.Value, info.ConversionCurve, info.ConversionK, info.ConversionCap,
                            raw, level);
                    }
                    else
                    {
                        points = raw; // 恒等换算：未引用曲线时 percent 与点数同一刻度。
                    }
                }
                else
                {
                    points = raw;
                    if (op == "pct" || op == "mult")
                    {
                        points *= 100.0;
                    }
                }

                var term = weight == 0.0 ? 0.0 : points * weight;
                sumPow += Math.Pow(term, exponent);
            }

            return Math.Pow(sumPow, 1.0 / exponent);
        }

        private static bool TryReadNumber(JsonObject obj, string key, out double value)
        {
            if (obj.TryGetValue(key, out var raw) && raw is JsonNumber num)
            {
                value = num.Value;
                return true;
            }

            value = 0;
            return false;
        }
    }
}
