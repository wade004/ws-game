using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

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
        /// "1.0" 记作"100 点预算"，与 <c>flat</c> 修正的数值刻度对齐）。</summary>
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
