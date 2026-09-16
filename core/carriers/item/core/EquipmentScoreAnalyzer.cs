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

            // 消费方反馈第 45 条判断记录：先包一层 TolerantRegistryView 再传给下面两步，让
            // BuildStatBudgetInfo（stat.* 三张支持表）与 ScoreCore（item.template 本身）共用同一个
            // 包装实例——TolerantRegistryView.Wrap 对已经是该类型的入参直接复用，不会重复包装，
            // 两步的降级记录（MissingTables）因此汇总在同一份 tolerant.Diagnostics 里，不会互相
            // 看不见对方那一半。
            var tolerant = TolerantRegistryView.Wrap(view);

            // 2026-09-16 深度复审 B-S1：本重载没有调用方预先构建好的 StatBudgetInfo 可复用，只能
            // 自己现场按 classId 建一次——单次评分场景性能影响可忽略；需要在遍历大量模板/等级点位时
            // 反复调用 Score 的场景（如 core/sim/core/CoverageSimulation.cs、GrowthSimulation.cs）
            // 应改用下方接受预构建 statBudgetInfo 的重载，见该重载判断记录。
            var statInfo = classId.HasValue
                ? ItemBudgetCurve.BuildStatBudgetInfo(tolerant, classId.Value)
                : ItemBudgetCurve.BuildStatBudgetInfo(tolerant);

            return ScoreCore(templateId, classId, tolerant, statInfo, additionalStats, exponent);
        }

        /// <summary>
        /// 2026-09-16 深度复审 B-S1 新增重载（纯新增，不改既有签名）：同上，额外接受调用方已经预先
        /// 构建好的 <paramref name="statBudgetInfo"/>（<see
        /// cref="ItemBudgetCurve.BuildStatBudgetInfo(IDataRegistryView)"/>/<see
        /// cref="ItemBudgetCurve.BuildStatBudgetInfo(IDataRegistryView, Id)"/> 的返回值，调用方需自行
        /// 保证与 <paramref name="classId"/> 口径一致——本方法不做二次校验，同本类型顶部判断记录
        /// "不持有任何字段/可变状态"，缓存与复用的责任归调用方）——复审报告 B-S1 指出
        /// <c>CoverageSimulation</c>/<c>GrowthSimulation</c> 在遍历大量模板/等级点位时循环调用
        /// <c>Score()</c>，每次都重新扫描 <c>stat.definition</c>/<c>stat.weight</c>/
        /// <c>stat.rating_conversion</c> 三张表——本重载让调用方在外层循环外构建一次、循环内复用。
        /// </summary>
        public static EquipmentScoreResult Score(
            Id templateId,
            Id? classId,
            IDataRegistryView view,
            IReadOnlyDictionary<Id, ItemBudgetCurve.StatBudgetInfo> statBudgetInfo,
            IReadOnlyList<(Id Stat, double Value)>? additionalStats = null,
            double exponent = ItemBudgetCurve.DefaultExponent)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (statBudgetInfo == null) throw new ArgumentNullException(nameof(statBudgetInfo));

            return ScoreCore(templateId, classId, view, statBudgetInfo, additionalStats, exponent);
        }

        /// <summary>2026-09-16 深度复审 B-S1 抽取：两个公开 <c>Score</c> 重载共用的核心评分逻辑，
        /// 逐字保留自改动前的公开 <c>Score</c> 方法，不改变任何既有行为/输出。</summary>
        /// <summary>
        /// 消费方反馈第 45 条（2026-09-17）判断记录：<paramref name="view"/> 一律先包一层 <see
        /// cref="TolerantRegistryView"/>（若已经是该类型则直接复用，见该类型 <c>Wrap</c> 判断记录），
        /// 内部对 <c>item.template</c> 本身及 <see cref="ItemBudgetCurve.ComputeConsumed"/> 前置的
        /// <c>stat.*</c> 支持表的读取因此从"阻断态一律抛异常"变成"读不到就按空/降级处理"——registry
        /// 阻断态是整体级别的（<c>DataRegistry.EnsureReadable</c> 不按表/记录粒度），即便触发阻断的
        /// 记录/字段与 <paramref name="templateId"/> 本身及三张支持表完全无关，此前也会直接抛
        /// <see cref="InvalidOperationException"/>，炸穿"编辑器用户改坏了别的记录，仍想看这件装备
        /// 评分"的场景（同 <c>ItemBudgetCurve.BuildStatBudgetInfo</c> 判断记录，一样的复现步骤）。
        /// <paramref name="templateId"/> 本身"因阻断读不到"（<c>view.TryGet</c> 返回 <c>false</c>）
        /// 与"确实不存在"（返回 <c>true</c> 但记录为 <c>null</c>）两种情形分开处理：前者不再抛
        /// <see cref="ArgumentException"/>，改为返回一条 <see cref="EquipmentScoreResult.IsDegraded"/>
        /// = <c>true</c>、<see cref="EquipmentScoreResult.Score"/> = 0 的降级结果（连"物品等级"都
        /// 拿不到，无法计算任何有意义的评分，0 是最安全的占位值，不是"算出来是 0"）；后者维持既有
        /// 行为（调用方传了个不存在的模板 id，是调用方用法错误，继续抛异常）。
        /// </summary>
        private static EquipmentScoreResult ScoreCore(
            Id templateId,
            Id? classId,
            IDataRegistryView view,
            IReadOnlyDictionary<Id, ItemBudgetCurve.StatBudgetInfo> statInfo,
            IReadOnlyList<(Id Stat, double Value)>? additionalStats,
            double exponent)
        {
            var tolerant = TolerantRegistryView.Wrap(view);

            var record = tolerant.Get("item.template", templateId);
            if (record == null)
            {
                if (tolerant.WasMissing("item.template"))
                {
                    // 阻断态读不到（与 templateId 是否存在无关）：见方法判断记录，降级返回而不是抛异常。
                    return new EquipmentScoreResult(
                        templateId, classId, itemLevel: 0, score: 0, exponent, new Dictionary<Id, double>(),
                        isDegraded: true, missingTables: tolerant.MissingTables);
                }

                throw new ArgumentException($"物品模板 \"{templateId}\" 不存在", nameof(templateId));
            }

            if (!record.TryGetInt("item_level", out var itemLevelRaw))
            {
                throw new ArgumentException($"物品模板 \"{templateId}\" 缺少 item_level", nameof(templateId));
            }

            var itemLevel = (int)itemLevelRaw;
            var mergedStats = MergeStats(record, additionalStats);

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

            return new EquipmentScoreResult(
                templateId, classId, itemLevel, score, exponent, weightsUsed,
                isDegraded: tolerant.IsDegraded, missingTables: tolerant.MissingTables);
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
