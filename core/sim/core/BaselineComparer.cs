using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Core.Sim
{
    /// <summary>一条 <see cref="BaselineDiff"/> 行的状态分类。</summary>
    public enum BaselineDiffStatus
    {
        /// <summary>当前值与基线值的差异小于 <see cref="BaselineCompareOptions.ExactMatchEpsilon"/>——
        /// 同种子重跑理应总是落在这一档（见类型判断记录"Same 与浮点比较的关系"）。</summary>
        Same,

        /// <summary>差异存在但未超出容差。</summary>
        Within,

        /// <summary>差异超出容差。</summary>
        Exceeded,

        /// <summary>该路径只在当前报告出现，基线里没有（新增统计量）。</summary>
        Added,

        /// <summary>该路径只在基线出现，当前报告没有（统计量消失）。</summary>
        Removed,
    }

    /// <summary>
    /// <see cref="BaselineComparer.Compare"/> 的容差配置——数值总纲/任务书原文"容差来源优先级：场景
    /// <c>bandwidths</c> 中同名统计量……&gt;每类统计量的默认相对容差"。
    /// </summary>
    public sealed class BaselineCompareOptions
    {
        public const double DefaultRelativeTolerance = 0.01;
        public const double DefaultAbsoluteToleranceForZeroBaseline = 1e-9;
        public const double DefaultExactMatchEpsilon = 1e-9;

        /// <summary>没有场景 <c>bandwidths</c> 覆盖时使用的默认相对容差。</summary>
        public double RelativeTolerance { get; set; } = DefaultRelativeTolerance;

        /// <summary>基线值的绝对值不超过本阈值时，改用绝对容差（本阈值本身）判定，避免"基线恰好是
        /// 0 或极接近 0"时相对容差退化为 0（除以接近 0 的数）。</summary>
        public double AbsoluteToleranceForZeroBaseline { get; set; } = DefaultAbsoluteToleranceForZeroBaseline;

        /// <summary>
        /// 判断记录（Same 与浮点比较的关系：全程只用阈值比较，不出现 <c>==</c>）：本任务硬性规则
        /// "禁止精确相等比对浮点统计量"——<see cref="BaselineDiffStatus.Same"/> 不是靠
        /// <c>current == baseline</c> 判定，而是"两者的绝对差小于本阈值"这一标准的 epsilon 阈值比较
        /// （标准做法本身就是浮点比较应该怎么做，不是这条硬性规则要禁止的对象；规则真正禁止的是拿
        /// <c>a == b</c> 本身当作"通过/失败"判据）。默认值 <c>1e-9</c> 远小于任何真实的设计层容差
        /// （默认相对容差 1%、场景带宽 25%~35%），只用来把"同种子重跑、逐位相同"这类真正意义上没有
        /// 变化的重复实验标记为 <see cref="BaselineDiffStatus.Same"/>；任何刻意引入的微小扰动（即便小
        /// 到 1e-12 这个量级）都会落在本阈值之上、被分类为 <see cref="BaselineDiffStatus.Within"/>，
        /// 见 <c>BaselineComparerTests</c> 对应用例与本类型 <see cref="Compare"/> 方法判断记录。
        /// </summary>
        public double ExactMatchEpsilon { get; set; } = DefaultExactMatchEpsilon;

        /// <summary>
        /// T-N6-8b 根治（取代 <c>core/sim/README.md</c> 判断记录 38 原先的"叶子名子串匹配"方案，
        /// 见该文件新增判断记录）：统计量叶子名（<see cref="SimStat.Path"/> 最后一段）到
        /// <see cref="SimReport.Bandwidths"/> 键名的显式精确映射表——只有叶子名在本表里有登记的键，
        /// 且该键在当前报告的 <c>Bandwidths</c> 里确有取值，才用场景带宽覆盖默认相对容差；叶子名
        /// 不在本表里（或映射到的键当前场景没有登记）时恒退回 <see cref="RelativeTolerance"/>，不再
        /// 做任何形式的子串/前缀猜测。默认值 <see cref="BaselineCompareOptions.DefaultLeafBandwidthKeys"/>
        /// 覆盖当前全部会产生统计量的叶子名（见该字段判断记录逐项列出的审计结果），调用方可整份替换
        /// 本属性以覆盖/扩展这张表（如游戏层新增了自己的统计量类别）。
        /// </summary>
        public IReadOnlyDictionary<string, string> LeafBandwidthKeys { get; set; } = DefaultLeafBandwidthKeys;

        /// <summary>
        /// T-N6-8b：<see cref="LeafBandwidthKeys"/> 的默认表——对
        /// <c>core/sim/core/SimReport.cs</c> 三个 <c>Extract*Stats</c> 方法产出的全部固定叶子名逐一
        /// 审计后的结果（<c>git grep -n "new SimStat("</c> 核对齐全，动态叶子名——<c>arena</c>
        /// 越级矩阵的 <c>skill_share.&lt;skillId&gt;</c>、<c>coverage</c> 的
        /// <c>&lt;category&gt;.&lt;id&gt;</c>——按 id 取值，不是固定字符串，不登记在本表，恒退回默认
        /// 相对容差，见 <see cref="ResolveTolerance"/> 判断记录）：
        /// <list type="bullet">
        /// <item><description><c>arena</c> 越级矩阵每格（<c>ArenaCellResult</c>）：<c>win_rate</c>
        /// （无场景登记同名带宽，映射到自身纯为审计完整性，恒退回默认）、
        /// <c>ttk_mean_seconds</c>/<c>ttk_median_seconds</c>/<c>ttk_p10_seconds</c>/
        /// <c>ttk_p90_seconds</c> → <c>ttk</c>、<c>ttd_estimate_mean</c> → <c>ttd</c>、
        /// <c>player_hit_rate_mean</c> → <c>hit_rate</c>、<c>player_dps_mean</c> → <c>dps</c>。
        /// <b>不登记</b> <c>player_max_health</c>（生命值统计量与 <c>hp</c> 带宽概念不同——本行是
        /// "标准玩家生命上限"这一装配期常量的回归比对，不是像对账等式一样需要锚定设计带宽的量，
        /// 精确匹配下恒用默认容差；旧子串实现里字面上并不会误配（<c>"player_max_health"</c> 不含
        /// 子串 <c>"hp"</c>），本表仍显式排除它并配同名测试，是为锁死这条不变量，防止未来任何
        /// 改名/新增字段意外引入误配）。</description></item>
        /// <item><description><c>arena.reconciliation.*</c> 对账等式行：<c>dps</c>/<c>hp</c>/
        /// <c>ttd</c> 三个叶子名本就与场景登记的带宽键同名（<c>SimReport</c> 类型判断记录"叶子名
        /// 对齐带宽键名"），映射到自身。</description></item>
        /// <item><description><c>growth</c> 每级轨迹行：<c>level_duration</c>/<c>item_level</c>/
        /// <c>hit_rate</c>/<c>gold</c> 同上，映射到自身；<c>kills</c> 不登记（无设计带宽概念，
        /// 恒退回默认）。</description></item>
        /// <item><description><c>growth.summary.*</c> 汇总行（<c>cumulative_xp_granted_via_api/
        /// events</c>、<c>cumulative_gold_via_balance/via_events</c>）：<b>刻意不登记</b>——这四行是
        /// "两条独立记账路径（API 调用 vs 事件流）应当彼此一致"这一代码正确性核对，不是设计带宽
        /// 覆盖的对象；旧子串实现会因叶子名含 <c>"gold"</c> 子串而误配成 25% 的宽松带宽，反而削弱了
        /// 这条核对本该有的敏感度，本次连带修正（见任务书"若映射修正导致某统计量容差变化，基线本身
        /// 不变，只看比对结果"——这四行改用更严格的默认 1% 容差后，同种子重跑仍逐位相同，落在
        /// <see cref="BaselineCompareOptions.ExactMatchEpsilon"/> 分支，不受此次收紧影响，见
        /// <c>BaselineComparerTests.Compare_SameSeedRerun_AllThreeScenarios_AllSame</c>）。</description></item>
        /// <item><description><c>coverage</c> 覆盖仿真离群值行：<c>ttk_seconds</c> → <c>ttk</c>
        /// （与 <c>arena</c> 的 TTK 类统计量共用同一条带宽概念）；<c>budget_ratio</c>/
        /// <c>secondary_measurement</c> 不登记（无对应设计带宽键，恒退回默认）。</description></item>
        /// </list>
        /// </summary>
        public static IReadOnlyDictionary<string, string> DefaultLeafBandwidthKeys { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["win_rate"] = "win_rate",
                ["ttk_mean_seconds"] = "ttk",
                ["ttk_median_seconds"] = "ttk",
                ["ttk_p10_seconds"] = "ttk",
                ["ttk_p90_seconds"] = "ttk",
                ["ttd_estimate_mean"] = "ttd",
                ["player_hit_rate_mean"] = "hit_rate",
                ["player_dps_mean"] = "dps",
                ["dps"] = "dps",
                ["hp"] = "hp",
                ["ttd"] = "ttd",
                ["level_duration"] = "level_duration",
                ["item_level"] = "item_level",
                ["hit_rate"] = "hit_rate",
                ["gold"] = "gold",
                ["ttk_seconds"] = "ttk",
            };
    }

    /// <summary>
    /// 判断记录（2026-09-16，深度复审 E-S2）：<c>sim.scenario.bandwidths</c>（schema 里登记为
    /// <see cref="Core.Foundation.DataRegistry.MapSchema.FreeKeyed"/> 自由字符串键，见
    /// <c>SimSchemas.cs</c>"本表不枚举合法键"）
    /// 的合法带宽键集合，与 <see cref="BaselineCompareOptions.DefaultLeafBandwidthKeys"/> 单一来源——
    /// 直接取该映射表全部取值（去重），不新开一张平行维护的键清单，改一处两处都跟着变。供
    /// <c>SimScenarioValidationRule</c>（<c>sim_scenario_bandwidth_key_unknown</c> 警告级检查，
    /// E-S2）校验内容作者填的 <c>bandwidths</c> 键名是否拼写错误——此前全链路（schema、校验规则、
    /// <see cref="BaselineComparer.ResolveTolerance"/>、<c>GrowthSimulation</c>/
    /// <c>ArenaSimulation</c> 的 <c>ResolveBandwidths</c>）都只用 <c>TryGetValue</c> 静默回退默认
    /// 容差/带宽，拼错键名不会在任何环节报出诊断。
    /// </summary>
    public static class SimBandwidthKeys
    {
        public static readonly IReadOnlyCollection<string> KnownKeys =
            new HashSet<string>(BaselineCompareOptions.DefaultLeafBandwidthKeys.Values, StringComparer.Ordinal);
    }

    /// <summary>一条统计量的基线对比结果——<see cref="BaselineComparer.Compare"/> 的输出粒度。</summary>
    public sealed class BaselineDiffRow
    {
        public string Path { get; }

        public double? BaselineValue { get; }

        public double? CurrentValue { get; }

        public double? AbsDeviation { get; }

        public double? RelDeviation { get; }

        /// <summary>本行实际生效的容差（已按"场景 bandwidths 同名覆盖 > 默认相对容差"解析、并换算成
        /// 绝对值），<see cref="Status"/> 为 <see cref="BaselineDiffStatus.Added"/>/
        /// <see cref="BaselineDiffStatus.Removed"/> 时无意义，恒为 0。</summary>
        public double Tolerance { get; }

        public BaselineDiffStatus Status { get; }

        internal BaselineDiffRow(
            string path, double? baselineValue, double? currentValue, double? absDeviation, double? relDeviation,
            double tolerance, BaselineDiffStatus status)
        {
            Path = path;
            BaselineValue = baselineValue;
            CurrentValue = currentValue;
            AbsDeviation = absDeviation;
            RelDeviation = relDeviation;
            Tolerance = tolerance;
            Status = status;
        }

        internal JsonObject ToJson() => new JsonObjectBuilder()
            .Add("path", new JsonString(Path))
            .Add("baseline", BaselineValue.HasValue ? ArenaReport.NumberOrNull(BaselineValue.Value) : JsonNull.Instance)
            .Add("current", CurrentValue.HasValue ? ArenaReport.NumberOrNull(CurrentValue.Value) : JsonNull.Instance)
            .Add("abs_deviation", AbsDeviation.HasValue ? ArenaReport.NumberOrNull(AbsDeviation.Value) : JsonNull.Instance)
            .Add("rel_deviation", RelDeviation.HasValue ? ArenaReport.NumberOrNull(RelDeviation.Value) : JsonNull.Instance)
            .Add("tolerance", new JsonNumber(Tolerance))
            .Add("status", new JsonString(Status.ToString()))
            .Build();
    }

    /// <summary><see cref="BaselineComparer.Compare"/> 的完整结果——按 <see cref="BaselineDiffRow.Path"/>
    /// 联集（当前 ∪ 基线）逐行比对。</summary>
    public sealed class BaselineDiff
    {
        public Id ScenarioId { get; }

        public string Kind { get; }

        /// <summary>当前报告与基线的 <c>dataset_fingerprint</c> 是否不同——不同不代表比对失败（数据
        /// 本身有意变化是正常的内容迭代），只是提示"本次差异可能来自数据改动而不是代码改动"，见类型
        /// 判断记录"DatasetChanged 只提示不阻断"。</summary>
        public bool DatasetChanged { get; }

        public IReadOnlyList<BaselineDiffRow> Rows { get; }

        public int SameCount { get; }
        public int WithinCount { get; }
        public int ExceededCount { get; }
        public int AddedCount { get; }
        public int RemovedCount { get; }

        /// <summary>是否存在需要人工复核的差异——<see cref="BaselineDiffStatus.Exceeded"/> 或
        /// <see cref="BaselineDiffStatus.Removed"/>（统计量消失同样值得警觉，见类型判断记录）。</summary>
        public bool HasBlockingDifference => ExceededCount > 0 || RemovedCount > 0;

        internal BaselineDiff(Id scenarioId, string kind, bool datasetChanged, IReadOnlyList<BaselineDiffRow> rows)
        {
            ScenarioId = scenarioId;
            Kind = kind;
            DatasetChanged = datasetChanged;
            Rows = rows;
            SameCount = rows.Count(r => r.Status == BaselineDiffStatus.Same);
            WithinCount = rows.Count(r => r.Status == BaselineDiffStatus.Within);
            ExceededCount = rows.Count(r => r.Status == BaselineDiffStatus.Exceeded);
            AddedCount = rows.Count(r => r.Status == BaselineDiffStatus.Added);
            RemovedCount = rows.Count(r => r.Status == BaselineDiffStatus.Removed);
        }

        private static int StatusRank(BaselineDiffStatus status) => status switch
        {
            BaselineDiffStatus.Exceeded => 0,
            BaselineDiffStatus.Removed => 1,
            BaselineDiffStatus.Added => 2,
            BaselineDiffStatus.Within => 3,
            BaselineDiffStatus.Same => 4,
            _ => 5,
        };

        /// <summary>按状态（<see cref="BaselineDiffStatus.Exceeded"/> 在前）与 <c>|AbsDeviation|</c>
        /// 降序排列后的行——供 <see cref="ToText"/>/调用方展示使用。</summary>
        public IReadOnlyList<BaselineDiffRow> SortedRows() =>
            Rows.OrderBy(r => StatusRank(r.Status))
                .ThenByDescending(r => r.AbsDeviation ?? 0.0)
                .ThenBy(r => r.Path, StringComparer.Ordinal)
                .ToList();

        public string ToJson()
        {
            var rowsArray = Rows.Select(r => (JsonValue)r.ToJson()).ToList();
            var root = new JsonObjectBuilder()
                .Add("scenario_id", new JsonString(ScenarioId.Value))
                .Add("kind", new JsonString(Kind))
                .Add("dataset_changed", DatasetChanged ? JsonBool.True : JsonBool.False)
                .Add("same_count", new JsonNumber(SameCount))
                .Add("within_count", new JsonNumber(WithinCount))
                .Add("exceeded_count", new JsonNumber(ExceededCount))
                .Add("added_count", new JsonNumber(AddedCount))
                .Add("removed_count", new JsonNumber(RemovedCount))
                .Add("rows", new JsonArray(rowsArray))
                .Build();
            return JsonWriter.Write(root);
        }

        /// <summary>人读文本报告：表头 + 按 <see cref="SortedRows"/> 顺序逐行——供
        /// <c>toolchain/simrunner</c> 写入 <c>*.diff.txt</c>。</summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append("scenario=").Append(ScenarioId.Value)
                .Append(" kind=").Append(Kind)
                .Append(" dataset_changed=").Append(DatasetChanged ? "true" : "false")
                .Append(" same=").Append(SameCount.ToString(CultureInfo.InvariantCulture))
                .Append(" within=").Append(WithinCount.ToString(CultureInfo.InvariantCulture))
                .Append(" exceeded=").Append(ExceededCount.ToString(CultureInfo.InvariantCulture))
                .Append(" added=").Append(AddedCount.ToString(CultureInfo.InvariantCulture))
                .Append(" removed=").Append(RemovedCount.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
            sb.AppendLine("status    path                                                          baseline           current            abs_dev            rel_dev   tolerance");

            foreach (var row in SortedRows())
            {
                sb.Append(row.Status.ToString().PadRight(10))
                    .Append(row.Path.PadRight(62))
                    .Append(FormatCell(row.BaselineValue).PadLeft(18)).Append(' ')
                    .Append(FormatCell(row.CurrentValue).PadLeft(18)).Append(' ')
                    .Append(FormatCell(row.AbsDeviation).PadLeft(18)).Append(' ')
                    .Append(FormatCell(row.RelDeviation).PadLeft(9)).Append(' ')
                    .Append(row.Tolerance.ToString("R", CultureInfo.InvariantCulture))
                    .AppendLine();
            }

            return sb.ToString();
        }

        private static string FormatCell(double? value) =>
            value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "-";
    }

    /// <summary>
    /// T-N6-6（ADR-0035 决策 5"基线对比工具输出改动前后统计量差异"）：把当前 <see cref="SimReport"/>
    /// 与既往 <see cref="SimBaseline"/> 逐 <see cref="SimStat.Path"/> 比对，产出 <see cref="BaselineDiff"/>。
    /// <para>
    /// <b>判断记录（容差解析：精确匹配显式映射表，T-N6-8b 根治）</b>：对每个路径，先取其"叶子统计量
    /// 名"（<see cref="SimStat.Path"/> 最后一段，如 <c>arena.L10.off+3.win_rate</c> 的
    /// <c>win_rate</c>），在 <see cref="BaselineCompareOptions.LeafBandwidthKeys"/>
    /// （默认 <see cref="BaselineCompareOptions.DefaultLeafBandwidthKeys"/>）里查这个叶子名是否有
    /// 精确登记的带宽键、且该键在本报告 <see cref="SimReport.Bandwidths"/> 里确有取值——两者都成立
    /// 才用该带宽值作相对容差，否则退回 <see cref="BaselineCompareOptions.RelativeTolerance"/>
    /// （默认 1%）。此前用"叶子名子串匹配"（取最长匹配 key）实现同一个"一对多复用同一条设计带宽"
    /// 需求，存在把语义不相关的叶子误配到带宽键的隐患（如某统计量叶子名恰好含 <c>"gold"</c>
    /// 子串却与金币设计带宽无关——本次审计发现 <c>growth.summary.cumulative_gold_via_*</c> 两行
    /// 正是这一情况，见 <see cref="BaselineCompareOptions.DefaultLeafBandwidthKeys"/> 判断记录逐项
    /// 说明），精确匹配显式表彻底消除这类误配，代价是新增统计量类别时需要显式登记（而不是"恰好
    /// 命名得像"就自动生效），这笔代价是有意为之：容差覆盖是否生效应该是可审计、可预期的决定，不是
    /// 命名巧合的副作用。相对容差按 <c>|baselineValue| × relativeTolerance</c> 换算成本行生效的
    /// 绝对容差；当 <c>|baselineValue|</c> 小到不超过
    /// <see cref="BaselineCompareOptions.AbsoluteToleranceForZeroBaseline"/> 时，直接用该阈值本身
    /// 作绝对容差（避免除以接近 0 的基线值把容差压缩到几乎 0，任何微小噪声都会被误判超限）。
    /// </para>
    /// <para>
    /// <b>判断记录（DatasetChanged 只提示不阻断）</b>：<see cref="BaselineDiff.DatasetChanged"/> 仅
    /// 反映 <c>dataset_fingerprint</c> 是否不同，不影响任何一行的 <see cref="BaselineDiffRow.Status"/>
    /// 判定——数据集本就允许随内容迭代变化（如调整某条曲线数值），此时统计量差异是预期结果，不应该
    /// 被当作"代码回归"处理；调用方（<c>toolchain/simrunner</c> 控制台摘要/diff 文本）如实展示这个
    /// 标记，让人工复核时能第一时间区分"这次差异是不是因为数据变了"，但退出码仍然只看
    /// <see cref="BaselineDiff.HasBlockingDifference"/>。
    /// </para>
    /// <para>
    /// <b>判断记录（NaN/Infinity 走独立分支，不与容差阈值做减法/比值）</b>：<see cref="SimStat.Value"/>
    /// 可能是 <see cref="double.NaN"/>/<see cref="double.PositiveInfinity"/>（见
    /// <see cref="ArenaReport.NumberOrNull"/> 判断记录"分母场次数为 0 时产生 NaN/Infinity"，
    /// <see cref="SimBaseline.Parse"/> 对 JSON <c>null</c> 同样还原为 NaN）——对这类值做
    /// <c>Math.Abs(a-b)</c> 或除法会产出 NaN（NaN 参与任何比较恒为 <c>false</c>，若不特殊处理，
    /// "是否超出容差"这条判断会被静默判定为不成立，等价于永远不报 <see cref="BaselineDiffStatus.Exceeded"/>，
    /// 是明确的正确性缺陷）。<see cref="Compare"/> 因此对两侧任一为 NaN/Infinity 的路径单独分支：按
    /// "特殊值类别是否相同"（都是 NaN，或都是同符号 Infinity）判定 <see cref="BaselineDiffStatus.Same"/>/
    /// <see cref="BaselineDiffStatus.Exceeded"/>，不落入下方阈值比较分支——这是"特殊值类别比较"，
    /// 不是本任务硬性规则"禁止精确相等比对浮点统计量"约束的对象（该规则针对的是正常有限数值的通过/
    /// 失败判据，不是 NaN/Infinity 这类需要按位模式识别的特殊值）。
    /// </para>
    /// </summary>
    public static class BaselineComparer
    {
        public static BaselineDiff Compare(SimReport current, SimBaseline baseline, BaselineCompareOptions? options = null)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            options ??= new BaselineCompareOptions();

            var currentByPath = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var stat in current.Stats)
            {
                currentByPath[stat.Path] = stat.Value;
            }

            var allPaths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in currentByPath.Keys) allPaths.Add(path);
            foreach (var path in baseline.Stats.Keys) allPaths.Add(path);

            var rows = new List<BaselineDiffRow>(allPaths.Count);
            foreach (var path in allPaths)
            {
                var hasCurrent = currentByPath.TryGetValue(path, out var currentValue);
                var hasBaseline = baseline.Stats.TryGetValue(path, out var baselineValue);

                if (hasCurrent && !hasBaseline)
                {
                    rows.Add(new BaselineDiffRow(path, null, currentValue, null, null, 0.0, BaselineDiffStatus.Added));
                    continue;
                }

                if (!hasCurrent && hasBaseline)
                {
                    rows.Add(new BaselineDiffRow(path, baselineValue, null, null, null, 0.0, BaselineDiffStatus.Removed));
                    continue;
                }

                var isSpecial = !double.IsFinite(currentValue) || !double.IsFinite(baselineValue);
                var tolerance = ResolveTolerance(path, baselineValue, current.Bandwidths, options);

                if (isSpecial)
                {
                    var sameSpecial =
                        (double.IsNaN(currentValue) && double.IsNaN(baselineValue)) ||
                        (double.IsPositiveInfinity(currentValue) && double.IsPositiveInfinity(baselineValue)) ||
                        (double.IsNegativeInfinity(currentValue) && double.IsNegativeInfinity(baselineValue));
                    rows.Add(new BaselineDiffRow(
                        path, baselineValue, currentValue, null, null, tolerance,
                        sameSpecial ? BaselineDiffStatus.Same : BaselineDiffStatus.Exceeded));
                    continue;
                }

                var absDeviation = Math.Abs(currentValue - baselineValue);
                var absBaseline = Math.Abs(baselineValue);
                double? relDeviation = absBaseline > options.AbsoluteToleranceForZeroBaseline
                    ? absDeviation / absBaseline
                    : (double?)null;

                BaselineDiffStatus status;
                if (absDeviation < options.ExactMatchEpsilon)
                {
                    status = BaselineDiffStatus.Same;
                }
                else if (absDeviation <= tolerance)
                {
                    status = BaselineDiffStatus.Within;
                }
                else
                {
                    status = BaselineDiffStatus.Exceeded;
                }

                rows.Add(new BaselineDiffRow(path, baselineValue, currentValue, absDeviation, relDeviation, tolerance, status));
            }

            var datasetChanged = !string.Equals(current.DatasetFingerprint, baseline.DatasetFingerprint, StringComparison.Ordinal);
            return new BaselineDiff(current.ScenarioId, current.Kind, datasetChanged, rows);
        }

        /// <summary>
        /// T-N6-8b 根治：公开这一纯函数（此前是 <c>private</c>）供单元测试直接构造边界叶子名
        /// （如 <c>player_max_health</c>、含 <c>"_hp_"</c> 的技能 id 叶子）验证"精确匹配、不做
        /// 子串/前缀猜测"这条不变量，不需要为每个边界情形另外跑一整套仿真+装配只为凑出一条真实
        /// 统计量路径（见 <c>BaselineComparerTests</c> 对应用例判断记录）。<paramref name="path"/>
        /// 取最后一段作叶子名，其余解析见 <see cref="Compare"/> 类型判断记录"容差解析"。
        /// </summary>
        public static double ResolveTolerance(
            string path, double baselineValue, IReadOnlyDictionary<string, double> bandwidths, BaselineCompareOptions options)
        {
            var lastDot = path.LastIndexOf('.');
            var leaf = lastDot >= 0 ? path.Substring(lastDot + 1) : path;

            var relativeTolerance = options.RelativeTolerance;
            if (options.LeafBandwidthKeys.TryGetValue(leaf, out var bandwidthKey) &&
                bandwidths.TryGetValue(bandwidthKey, out var bandwidthValue))
            {
                relativeTolerance = bandwidthValue;
            }

            var absBaseline = Math.Abs(baselineValue);
            return absBaseline > options.AbsoluteToleranceForZeroBaseline
                ? relativeTolerance * absBaseline
                : options.AbsoluteToleranceForZeroBaseline;
        }
    }
}
