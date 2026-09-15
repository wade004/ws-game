using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Item
{
    /// <summary>
    /// 装备评分契约面（分阶段落地计划 T-N2-4；ADR-0032 决策 9"实际消耗公式换成职业权重即评分，服务
    /// 比较箭头、一键换装最优、掉落升级提示；玩家看到评分而非等级与品质"；07 第 1.2 节修订段"装备
    /// 评分：实际消耗公式换成职业权重即评分……同预算的两件装备属性组合不同，评分对预算单调但不对
    /// 每个职业单调，这是配装空间"；落地改动点清单 E6"照 <c>Core.Gameplay.Loot.LootTableAnalyzer</c>
    /// 形态"）：与 <see cref="ItemBudgetCurve.ComputeConsumed"/> 同一条消耗
    /// 公式 <c>(Σ(值×权重)^k)^(1/k)</c>，唯一区别是权重来源从 <c>stat.weight</c> 顶层基础权重换成
    /// <c>class_overrides</c> 按职业覆盖后的权重（见 <see cref="ItemBudgetCurve
    /// .BuildStatBudgetInfo(IDataRegistryView, Id)"/>）。
    /// <para>
    /// 判断记录（用 <c>&lt;c&gt;</c> 而不是 <c>&lt;see cref&gt;</c> 指代
    /// <c>Core.Gameplay.Loot.LootTableAnalyzer</c>）：同 <c>RegistryCreatureTemplateQuery</c>
    /// 类型判断记录——本文件所在的 <c>Core.Carriers</c> 程序集按分层不引用 L4 <c>Core.Gameplay</c>，
    /// 写 cref 会因目标类型不可解析产生 CS1574 警告，本仓库 <c>TreatWarningsAsErrors</c> 下即编译
    /// 失败。照该类型的契约面形态（任务书原文）：静态类，纯函数，不持有任何字段/可变状态，不修改
    /// <paramref name="view"/> 或任何入参的状态，可重复调用；只读依赖经每次调用的 <see
    /// cref="IDataRegistryView"/> 参数注入，不在构造期/静态字段里缓存任何 registry 快照（避免"评分
    /// 结果偷偷绑定了某一次加载的数据"这一类陈旧数据 bug）。
    /// </para>
    /// <para>
    /// 判断记录（本任务只对模板 <c>stats</c> 评分，词缀实例值经 <paramref name="additionalStats"/>
    /// 预留入参）：物品实例带词缀的评分（词缀落值后的具体属性值）依赖 <see cref="BudgetSolver"/>
    /// 反解出的数值，而词缀反解值本身要等 T-N2-7（<c>ItemInstance.Affixes</c>）/T-N2-8（掉落三次
    /// 掷骰）落地才存在——本任务只实现"给模板 <c>stats</c> 评分"，并预留 <paramref
    /// name="additionalStats"/>（"附加属性列表"，语义同 <see cref="BudgetSolverResult.Values"/>
    /// 的 <c>Id → 点数</c> 形状，全部按 <c>op=flat</c> 语义并入）供后续任务把词缀反解值接进来，不
    /// 需要再改本方法签名。</para>
    /// <para>
    /// 判断记录（<c>exponent</c> 取显式参数，不取 <c>item.budget_curve</c> 记录）：评分本身不核算
    /// "消耗是否超过某条曲线的上限"，只是复用同一条加权公式；07/ADR-0032 均未要求评分与某一条具体
    /// <c>item.budget_curve</c> 记录绑定。若绑定某条曲线的 <c>exponent</c> 字段，会让"评分"这一
    /// 纯展示概念意外依赖"预算超标校验用的是哪条曲线"这一内容配置细节；改为显式参数，缺省 <see
    /// cref="ItemBudgetCurve.DefaultExponent"/>，调用方需要与预算校验同一 k 值时自行传入同一个
    /// <c>item.budget_curve</c> 记录的 <c>exponent</c> 字段值。</para>
    /// </summary>
    public static class EquipmentScoreAnalyzer
    {
        /// <summary>
        /// 对 <paramref name="templateId"/> 指向的 <c>item.template</c> 评分。<paramref
        /// name="classId"/> 为 <c>null</c> 时使用 <c>stat.weight</c> 顶层基础权重（不按职业覆盖，
        /// 同 <see cref="ItemBudgetCurve.BuildStatBudgetInfo(IDataRegistryView)"/>）。
        /// </summary>
        public static EquipmentScoreResult Score(
            Id templateId,
            Id? classId,
            IDataRegistryView view,
            IReadOnlyList<(Id Stat, double Value)>? additionalStats = null,
            double exponent = ItemBudgetCurve.DefaultExponent)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            var record = view.Get("item.template", templateId);
            if (record == null)
            {
                throw new ArgumentException($"物品模板 \"{templateId}\" 不存在", nameof(templateId));
            }

            if (!record.TryGetInt("item_level", out var itemLevelRaw))
            {
                throw new ArgumentException($"物品模板 \"{templateId}\" 缺少 item_level", nameof(templateId));
            }

            var itemLevel = (int)itemLevelRaw;
            var mergedStats = MergeStats(record, additionalStats);

            var statInfo = classId.HasValue
                ? ItemBudgetCurve.BuildStatBudgetInfo(view, classId.Value)
                : ItemBudgetCurve.BuildStatBudgetInfo(view);

            var score = ItemBudgetCurve.ComputeConsumed(mergedStats, statInfo, itemLevel, exponent);

            var weightsUsed = new Dictionary<Id, double>();
            foreach (var item in mergedStats)
            {
                if (item is JsonObject obj && obj.TryGetValue("stat", out var statRaw) && statRaw is JsonString statStr)
                {
                    var statId = new Id(statStr.Value);
                    weightsUsed[statId] = statInfo.TryGetValue(statId, out var info)
                        ? info.Weight
                        : ItemBudgetCurve.DefaultWeight;
                }
            }

            return new EquipmentScoreResult(templateId, classId, itemLevel, score, exponent, weightsUsed);
        }

        /// <summary>两个评分结果的大小比较（同 <c>IComparable&lt;T&gt;.CompareTo</c> 惯例：负数表示
        /// <paramref name="a"/> 更低，正数表示更高，0 表示相等）——服务任务书"比较箭头"场景，调用方
        /// 不需要自己解开 <see cref="EquipmentScoreResult.Score"/> 字段再比较。</summary>
        public static int Compare(EquipmentScoreResult a, EquipmentScoreResult b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            return a.Score.CompareTo(b.Score);
        }

        private static JsonArray MergeStats(DataRecord record, IReadOnlyList<(Id Stat, double Value)>? additionalStats)
        {
            var items = new List<JsonValue>();
            if (record.TryGetArray("stats", out var baseStats))
            {
                foreach (var s in baseStats)
                {
                    items.Add(s);
                }
            }

            if (additionalStats != null)
            {
                foreach (var (stat, value) in additionalStats)
                {
                    items.Add(new JsonObjectBuilder()
                        .Add("stat", new JsonString(stat.Value))
                        .Add("op", new JsonString("flat"))
                        .Add("value", new JsonNumber(value))
                        .Build());
                }
            }

            return new JsonArray(items);
        }
    }
}
