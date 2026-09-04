using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <c>item.budget_curve.entries</c> 的线性插值（见 07 第 1.2 节"给定 item_level 与 quality，算出
    /// 该件物品全部 stats 条目允许消耗的预算总量上限……具体系数曲线由数据……item_level → budget
    /// 的分段曲线定义"）。<see cref="ItemBudgetValidationRule"/> 与未来可能需要"预算余量"信息的
    /// 内容工具共用本静态方法，不各自重复实现插值逻辑。
    /// </summary>
    public static class ItemBudgetCurve
    {
        private readonly struct Entry
        {
            public readonly int ItemLevel;
            public readonly double Budget;

            public Entry(int itemLevel, double budget)
            {
                ItemLevel = itemLevel;
                Budget = budget;
            }
        }

        /// <summary>解析 <paramref name="curveRecord"/> 的 <c>entries</c> 字段并按 <c>item_level</c>
        /// 升序排序（数据文件本身未要求有序，本方法自行排序，不信任内容作者的书写顺序）。</summary>
        public static IReadOnlyList<(int ItemLevel, double Budget)> ParseEntries(DataRecord curveRecord)
        {
            var raw = curveRecord.GetArray("entries");
            var entries = new List<Entry>(raw.Count);
            foreach (var item in raw)
            {
                if (!(item is JsonObject obj))
                {
                    throw new ArgumentException(
                        $"item.budget_curve \"{curveRecord.Key}\" 的 entries 元素不是对象");
                }

                if (!obj.TryGetValue("item_level", out var levelValue) || !(levelValue is JsonNumber levelNum) ||
                    !obj.TryGetValue("budget", out var budgetValue) || !(budgetValue is JsonNumber budgetNum))
                {
                    throw new ArgumentException(
                        $"item.budget_curve \"{curveRecord.Key}\" 的 entries 元素缺少 item_level/budget");
                }

                entries.Add(new Entry((int)levelNum.Value, budgetNum.Value));
            }

            entries.Sort((a, b) => a.ItemLevel.CompareTo(b.ItemLevel));

            var result = new List<(int, double)>(entries.Count);
            foreach (var e in entries)
            {
                result.Add((e.ItemLevel, e.Budget));
            }

            return result;
        }

        /// <summary>按 <paramref name="itemLevel"/> 在 <paramref name="entries"/>（已按 <see
        /// cref="ParseEntries"/> 排好序）上线性插值；低于最小断点夹取到首项，高于最大断点夹取到
        /// 末项（07 第 1.2 节未规定越界行为，取"夹取到边界"这一最不容易产生离谱结果的近似）。
        /// <paramref name="entries"/> 为空时返回 0（等价于"没有预算约束"，应已被
        /// <c>required_field</c> 检查拦截——<c>entries</c> 是必填字段，见 <see
        /// cref="ItemSchemas.BudgetCurve"/>）。</summary>
        public static double Interpolate(IReadOnlyList<(int ItemLevel, double Budget)> entries, int itemLevel)
        {
            if (entries.Count == 0)
            {
                return 0;
            }

            if (itemLevel <= entries[0].ItemLevel)
            {
                return entries[0].Budget;
            }

            var last = entries[entries.Count - 1];
            if (itemLevel >= last.ItemLevel)
            {
                return last.Budget;
            }

            for (var i = 0; i < entries.Count - 1; i++)
            {
                var lo = entries[i];
                var hi = entries[i + 1];
                if (itemLevel >= lo.ItemLevel && itemLevel <= hi.ItemLevel)
                {
                    if (hi.ItemLevel == lo.ItemLevel)
                    {
                        return lo.Budget;
                    }

                    var t = (double)(itemLevel - lo.ItemLevel) / (hi.ItemLevel - lo.ItemLevel);
                    return lo.Budget + t * (hi.Budget - lo.Budget);
                }
            }

            // 不可达：itemLevel 已被上面的边界分支夹在 [entries[0], entries[^1]] 之间。
            return last.Budget;
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
    }
}
