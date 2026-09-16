using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <see cref="IBudgetSolver"/> 的实现（分阶段落地计划 T-N2-4；ADR-0032 决策 3/7；07 第 1.2/1.6
    /// 节修订段）：<see cref="ItemBudgetCurve.ComputeConsumed"/> 消耗公式
    /// <c>实际消耗 = (Σ(值_i × 权重_i)^k)^(1/k)</c> 的反函数。
    /// <para>
    /// 数学推导：把 <paramref name="statMix"/>（下同）的每一项 <c>(stat_i, ratio_i)</c> 理解为"该属性
    /// 的加权贡献 <c>term_i = value_i × weight_i</c> 占总加权贡献的比例"——记 <c>S</c> 为一个待定的
    /// 缩放常数，令 <c>term_i = ratio_i × S</c>，代入消耗公式：
    /// <c>C = (Σ term_i^k)^(1/k) = (Σ (ratio_i × S)^k)^(1/k) = S × (Σ ratio_i^k)^(1/k)</c>。
    /// 令 <c>C</c> 等于目标预算 <c>B</c>（<see cref="BudgetSolverResult.TargetBudget"/>），解出
    /// <c>S = B / (Σ ratio_i^k)^(1/k)</c>；再对每一项 <c>value_i = term_i / weight_i = (ratio_i × S) /
    /// weight_i</c>。<c>k=1</c> 时 <c>(Σ ratio_i)^(1/1) = Σ ratio_i = 1</c>（比例之和为 1 是本类型
    /// 的输入校验前提，见下），退化为 <c>S = B</c>，公式自然覆盖 k=1 的线性情形，不需要单独分支。
    /// </para>
    /// <para>
    /// 判断记录（<paramref name="statMix"/> 比例之和须严格为 1，不是"不超过一"——**上报待设计层
    /// 确认**）：见 <see cref="IBudgetSolver"/> 类型判断记录"<c>statMix</c> 比例语义"一节——07 第 1.6
    /// 节 <c>item.affix.stat_mix</c> 的"之和不超过一"是对<b>存量数据</b>的校验上界（<see
    /// cref="ItemAffixStatMixRatioSumRule"/> 只在超过 1+1e-9 时报错，允许"之和小于一"这一合法数据
    /// 形态存在）；但本方法作为通用反解工具，"目标预算"与"份额"已经由 <paramref
    /// name="shareOfBudget"/> 独立表达（见 <see cref="IBudgetSolver"/> 判断记录），<paramref
    /// name="statMix"/> 只负责"这份已确定的目标预算如何在各属性间分配"——比例之和必须恰为 1，否则
    /// "反解出的属性值"与"传入的目标预算"之间无法建立"各属性各自占多少"这一直观对应关系（之和
    /// &lt; 1 时无法判断剩余份额该算作"故意浪费"还是"输入错误"，之和 &gt; 1 更是直接与"占比"语义
    /// 矛盾）。未来若 T-N2-8 调用方需要表达"词缀 stat_mix 之和小于一"的存量数据，由调用方自行在
    /// 传入前按 <c>ratio_i / Σratio_i</c> 归一化（归一化不改变各项之间的相对比例，因此不改变
    /// <c>value_i</c> 之间的相对大小，只是让"总目标预算"与<paramref name="shareOfBudget"/> 精确对齐）。
    /// </para>
    /// <para>
    /// 判断记录（权重来源不展开 <c>class_overrides</c>）：同 <see
    /// cref="ItemBudgetCurve.BuildStatBudgetInfo(IDataRegistryView)"/> 判断记录——反解服务的是"这件
    /// 物品/这条词缀本身该有多少属性值"，不针对任何具体职业；装备评分（<see
    /// cref="EquipmentScoreAnalyzer"/>，按职业权重）是不同场景。
    /// </para>
    /// <para>
    /// 禁止事项核对：本类型不持有任何字段（无可变状态），<c>Solve</c> 的每次调用都是纯函数
    /// （给定相同输入，含相同 <paramref name="view"/> 快照，必然产出相同结果），不缓存、不记录调用
    /// 历史——任务书"禁止 Analyzer 持有可变状态"虽然字面只点名 <see cref="EquipmentScoreAnalyzer"/>，
    /// 本类型同样遵守，避免两个新契约面风格不一致。
    /// </para>
    /// </summary>
    public sealed class BudgetSolver : IBudgetSolver
    {
        /// <summary>显式转发 <see cref="IBudgetSolver"/> 的默认接口成员（<c>shareOfBudget</c>
        /// 缺省 1.0 的 6 参重载）——同 <c>Tests.Presentation.Assembly
        /// .InterfaceDefaultMemberForwardingTests</c> 门禁要求：具体实现类不得悄悄落回接口默认实现，
        /// 一律显式转发或登记豁免，避免"组合/包装实现方忘了转发新成员"这类遗漏被默认值掩盖。本方法
        /// 转发的默认值（1.0）本身就是正确语义（同 <see cref="IBudgetSolver"/> 判断记录），显式声明
        /// 只是满足门禁、不改变语义。</summary>
        public BudgetSolverResult Solve(
            int itemLevel,
            Id qualityId,
            Id slotId,
            IReadOnlyList<(Id Stat, double Ratio)> statMix,
            Id budgetCurveId,
            IDataRegistryView view)
            => Solve(itemLevel, qualityId, slotId, statMix, budgetCurveId, 1.0, view);

        /// <summary>
        /// 2026-09-16 深度复审 B-S1：显式转发 <see cref="IBudgetSolver"/> 新增的 8 参默认接口成员
        /// （门禁要求同上方 6 参重载的判断记录），且真正跳过内部重建——直接把调用方传入的
        /// <paramref name="statBudgetInfo"/> 转给 <see cref="SolveCore"/>，不再调用一次
        /// <see cref="ItemBudgetCurve.BuildStatBudgetInfo(IDataRegistryView)"/>。</summary>
        public BudgetSolverResult Solve(
            int itemLevel,
            Id qualityId,
            Id slotId,
            IReadOnlyList<(Id Stat, double Ratio)> statMix,
            Id budgetCurveId,
            double shareOfBudget,
            IDataRegistryView view,
            IReadOnlyDictionary<Id, ItemBudgetCurve.StatBudgetInfo> statBudgetInfo)
        {
            if (statBudgetInfo == null) throw new ArgumentNullException(nameof(statBudgetInfo));
            return SolveCore(itemLevel, qualityId, slotId, statMix, budgetCurveId, shareOfBudget, view, statBudgetInfo);
        }

        public BudgetSolverResult Solve(
            int itemLevel,
            Id qualityId,
            Id slotId,
            IReadOnlyList<(Id Stat, double Ratio)> statMix,
            Id budgetCurveId,
            double shareOfBudget,
            IDataRegistryView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            // 同 ItemBudgetValidationRule 惯例：不展开 class_overrides（见类型判断记录）。本重载没有
            // 调用方预先构建好的 StatBudgetInfo 可复用，只能自己现场建一次——一次性调用场景（未跨
            // 多条词缀复用）性能影响可忽略；需要跨多条词缀复用的调用方应改用下方 8 参重载，见 B-S1
            // 判断记录。
            var statInfo = ItemBudgetCurve.BuildStatBudgetInfo(view);
            return SolveCore(itemLevel, qualityId, slotId, statMix, budgetCurveId, shareOfBudget, view, statInfo);
        }

        /// <summary>2026-09-16 深度复审 B-S1 抽取：两个公开 <c>Solve</c> 重载（自建 <c>statInfo</c>／
        /// 复用调用方传入的 <c>statInfo</c>）共用的核心反解逻辑，此前完全重复的一份代码现在只有一处。
        /// 逻辑本身逐字保留自改动前的 7 参 <c>Solve</c>，不改变任何既有行为/输出。</summary>
        private static BudgetSolverResult SolveCore(
            int itemLevel,
            Id qualityId,
            Id slotId,
            IReadOnlyList<(Id Stat, double Ratio)> statMix,
            Id budgetCurveId,
            double shareOfBudget,
            IDataRegistryView view,
            IReadOnlyDictionary<Id, ItemBudgetCurve.StatBudgetInfo> statInfo)
        {
            if (statMix == null) throw new ArgumentNullException(nameof(statMix));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (statMix.Count == 0)
            {
                throw new ArgumentException("statMix 不能为空", nameof(statMix));
            }

            if (shareOfBudget <= 0.0 || shareOfBudget > 1.0 + 1e-9)
            {
                throw new ArgumentException(
                    $"shareOfBudget 须在 (0,1] 区间内，当前为 {shareOfBudget}", nameof(shareOfBudget));
            }

            double ratioSum = 0;
            foreach (var entry in statMix)
            {
                if (entry.Ratio <= 0.0)
                {
                    throw new ArgumentException(
                        $"statMix 属性 \"{entry.Stat}\" 的 ratio 必须 > 0（当前 {entry.Ratio}）", nameof(statMix));
                }

                ratioSum += entry.Ratio;
            }

            if (Math.Abs(ratioSum - 1.0) > 1e-9)
            {
                throw new ArgumentException(
                    $"statMix 的 ratio 之和须为 1（±1e-9），当前为 {ratioSum}", nameof(statMix));
            }

            var curveRecord = view.Get("item.budget_curve", budgetCurveId);
            if (curveRecord == null)
            {
                throw new ArgumentException($"预算曲线 \"{budgetCurveId}\" 不存在", nameof(budgetCurveId));
            }

            var qualityRecord = view.Get("item.quality_definition", qualityId);
            if (qualityRecord == null)
            {
                throw new ArgumentException($"品质 \"{qualityId}\" 不存在", nameof(qualityId));
            }

            var slotRecord = view.Get("item.slot_definition", slotId);
            if (slotRecord == null)
            {
                throw new ArgumentException($"槽位 \"{slotId}\" 不存在", nameof(slotId));
            }

            // T-N0-4 惯例：解析一次为 PiecewiseCurve 再插值，不重复实现插值逻辑（禁止事项"禁止复制
            // 插值实现"）。
            var curve = ItemBudgetCurve.ParseCurve(curveRecord);
            var exponent = curveRecord.TryGetNumber("exponent", out var exponentValue)
                ? exponentValue
                : ItemBudgetCurve.DefaultExponent;

            var qualityMultiplier = qualityRecord.TryGetNumber("budget_multiplier", out var qm) ? qm : 1.0;
            var slotCoefficient = slotRecord.TryGetNumber("budget_coefficient", out var sc) ? sc : 1.0;

            var itemBudgetLimit = ItemBudgetCurve.Interpolate(curve, itemLevel) * qualityMultiplier * slotCoefficient;
            var targetBudget = itemBudgetLimit * shareOfBudget;

            // 2026-09-16 深度复审 B-S1：statInfo 改为方法参数（由调用方——上面两个公开 Solve 重载之一
            // ——提供，不在本方法内部重建），同 ItemBudgetValidationRule 惯例：不展开
            // class_overrides（见类型判断记录）。
            double sumRatioPow = 0;
            foreach (var entry in statMix)
            {
                sumRatioPow += Math.Pow(entry.Ratio, exponent);
            }

            // sumRatioPow > 0 恒成立：上面已校验每个 ratio > 0 且 statMix 非空。
            var scale = targetBudget <= 0.0 ? 0.0 : targetBudget / Math.Pow(sumRatioPow, 1.0 / exponent);

            var values = new Dictionary<Id, double>(statMix.Count);
            var weightsUsed = new Dictionary<Id, double>(statMix.Count);
            var verifyStats = new List<JsonValue>(statMix.Count);

            foreach (var entry in statMix)
            {
                var weight = statInfo.TryGetValue(entry.Stat, out var info)
                    ? info.Weight
                    : ItemBudgetCurve.DefaultWeight;

                if (weight == 0.0)
                {
                    throw new ArgumentException(
                        $"statMix 属性 \"{entry.Stat}\" 的权重登记为 0，无法按比例反解出有限值", nameof(statMix));
                }

                var term = entry.Ratio * scale;
                var value = term / weight;

                values[entry.Stat] = value;
                weightsUsed[entry.Stat] = weight;

                verifyStats.Add(new JsonObjectBuilder()
                    .Add("stat", new JsonString(entry.Stat.Value))
                    .Add("op", new JsonString("flat"))
                    .Add("value", new JsonNumber(value))
                    .Build());
            }

            // 正向复算：验证可逆性（供 BudgetSolverResult.ActualConsumed 与调用方自行断言使用），
            // 同 ItemBudgetCurve.ComputeConsumed 既有公式，不另写一套简化版本。
            var actualConsumed = ItemBudgetCurve.ComputeConsumed(new JsonArray(verifyStats), statInfo, itemLevel, exponent);

            return new BudgetSolverResult(
                itemLevel, qualityId, slotId, itemBudgetLimit, shareOfBudget, targetBudget,
                exponent, actualConsumed, values, weightsUsed);
        }
    }
}
