using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Sim
{
    /// <summary>一个 (玩家等级, 越级偏移) 格子上 <c>runs</c> 场战斗的聚合统计。</summary>
    public sealed class ArenaCellResult
    {
        public int PlayerLevel { get; }

        public int LevelOffset { get; }

        /// <summary>本格子生物实际出生等级 = <c>max(1, PlayerLevel + LevelOffset)</c>（见
        /// <see cref="ArenaSimulation"/> 判断记录"越级矩阵下界夹到 1"）。</summary>
        public int CreatureLevel { get; }

        public int Runs { get; }

        public double WinRate { get; }

        /// <summary>玩家获胜场次里的击杀时长（秒）均值；本格子玩家一场未胜时为 <see cref="double.NaN"/>。</summary>
        public double TtkMeanSeconds { get; }

        public double TtkMedianSeconds { get; }

        public double TtkP10Seconds { get; }

        public double TtkP90Seconds { get; }

        /// <summary>全部 <c>runs</c> 场 <see cref="FightResult.TtdEstimate"/> 的均值（跳过
        /// <see cref="double.PositiveInfinity"/> 的场次，全部场次皆为 Infinity 时本值也是
        /// Infinity——见 <see cref="ArenaSimulation"/> 判断记录"TTD 均值如何处理 Infinity"）。</summary>
        public double TtdEstimateMean { get; }

        public double PlayerHitRateMean { get; }

        public double PlayerDpsMean { get; }

        /// <summary>玩家最大生命——同一格子内与种子无关的常量（见 <see cref="FightResult.PlayerMaxHealth"/>
        /// 判断记录），取第一场的值。</summary>
        public double PlayerMaxHealth { get; }

        /// <summary>技能 id → 该技能落地伤害占比在 <c>runs</c> 场里的均值（缺席场次按 0 计入）。</summary>
        public IReadOnlyDictionary<Id, double> SkillShareMean { get; }

        internal ArenaCellResult(
            int playerLevel, int levelOffset, int creatureLevel, int runs, double winRate,
            double ttkMeanSeconds, double ttkMedianSeconds, double ttkP10Seconds, double ttkP90Seconds,
            double ttdEstimateMean, double playerHitRateMean, double playerDpsMean, double playerMaxHealth,
            IReadOnlyDictionary<Id, double> skillShareMean)
        {
            PlayerLevel = playerLevel;
            LevelOffset = levelOffset;
            CreatureLevel = creatureLevel;
            Runs = runs;
            WinRate = winRate;
            TtkMeanSeconds = ttkMeanSeconds;
            TtkMedianSeconds = ttkMedianSeconds;
            TtkP10Seconds = ttkP10Seconds;
            TtkP90Seconds = ttkP90Seconds;
            TtdEstimateMean = ttdEstimateMean;
            PlayerHitRateMean = playerHitRateMean;
            PlayerDpsMean = playerDpsMean;
            PlayerMaxHealth = playerMaxHealth;
            SkillShareMean = skillShareMean;
        }

        internal JsonObject ToJson()
        {
            var skillShareArray = new List<JsonValue>();
            foreach (var kv in SkillShareMean.OrderBy(kv => kv.Key.Value, StringComparer.Ordinal))
            {
                skillShareArray.Add(new JsonObjectBuilder()
                    .Add("skill_id", new JsonString(kv.Key.Value))
                    .Add("share", ArenaReport.NumberOrNull(kv.Value))
                    .Build());
            }

            return new JsonObjectBuilder()
                .Add("player_level", new JsonNumber(PlayerLevel))
                .Add("level_offset", new JsonNumber(LevelOffset))
                .Add("creature_level", new JsonNumber(CreatureLevel))
                .Add("runs", new JsonNumber(Runs))
                .Add("win_rate", ArenaReport.NumberOrNull(WinRate))
                .Add("ttk_mean_seconds", ArenaReport.NumberOrNull(TtkMeanSeconds))
                .Add("ttk_median_seconds", ArenaReport.NumberOrNull(TtkMedianSeconds))
                .Add("ttk_p10_seconds", ArenaReport.NumberOrNull(TtkP10Seconds))
                .Add("ttk_p90_seconds", ArenaReport.NumberOrNull(TtkP90Seconds))
                .Add("ttd_estimate_mean", ArenaReport.NumberOrNull(TtdEstimateMean))
                .Add("player_hit_rate_mean", ArenaReport.NumberOrNull(PlayerHitRateMean))
                .Add("player_dps_mean", ArenaReport.NumberOrNull(PlayerDpsMean))
                .Add("player_max_health", ArenaReport.NumberOrNull(PlayerMaxHealth))
                .Add("skill_damage_share_mean", new JsonArray(skillShareArray))
                .Build();
        }
    }

    /// <summary>一个等级上的对账等式核对行——数值总纲第 5 节"锚点表与对账等式"。</summary>
    public sealed class ReconciliationRow
    {
        public int Level { get; }

        public double DpsTopDown { get; }
        public double DpsBottomUp { get; }
        public double DpsDeviation { get; }
        public double DpsBandwidth { get; }
        public bool DpsPass { get; }

        public double HpTopDown { get; }
        public double HpBottomUp { get; }
        public double HpDeviation { get; }
        public double HpBandwidth { get; }
        public bool HpPass { get; }

        public double TtdTopDown { get; }
        public double TtdBottomUp { get; }
        public double TtdDeviation { get; }
        public double TtdBandwidth { get; }
        public bool TtdPass { get; }

        internal ReconciliationRow(
            int level,
            double dpsTopDown, double dpsBottomUp, double dpsDeviation, double dpsBandwidth, bool dpsPass,
            double hpTopDown, double hpBottomUp, double hpDeviation, double hpBandwidth, bool hpPass,
            double ttdTopDown, double ttdBottomUp, double ttdDeviation, double ttdBandwidth, bool ttdPass)
        {
            Level = level;
            DpsTopDown = dpsTopDown; DpsBottomUp = dpsBottomUp; DpsDeviation = dpsDeviation; DpsBandwidth = dpsBandwidth; DpsPass = dpsPass;
            HpTopDown = hpTopDown; HpBottomUp = hpBottomUp; HpDeviation = hpDeviation; HpBandwidth = hpBandwidth; HpPass = hpPass;
            TtdTopDown = ttdTopDown; TtdBottomUp = ttdBottomUp; TtdDeviation = ttdDeviation; TtdBandwidth = ttdBandwidth; TtdPass = ttdPass;
        }

        internal JsonObject ToJson() => new JsonObjectBuilder()
            .Add("level", new JsonNumber(Level))
            .Add("dps_top_down", ArenaReport.NumberOrNull(DpsTopDown))
            .Add("dps_bottom_up", ArenaReport.NumberOrNull(DpsBottomUp))
            .Add("dps_deviation", ArenaReport.NumberOrNull(DpsDeviation))
            .Add("dps_bandwidth", ArenaReport.NumberOrNull(DpsBandwidth))
            .Add("dps_pass", DpsPass ? JsonBool.True : JsonBool.False)
            .Add("hp_top_down", ArenaReport.NumberOrNull(HpTopDown))
            .Add("hp_bottom_up", ArenaReport.NumberOrNull(HpBottomUp))
            .Add("hp_deviation", ArenaReport.NumberOrNull(HpDeviation))
            .Add("hp_bandwidth", ArenaReport.NumberOrNull(HpBandwidth))
            .Add("hp_pass", HpPass ? JsonBool.True : JsonBool.False)
            .Add("ttd_top_down", ArenaReport.NumberOrNull(TtdTopDown))
            .Add("ttd_bottom_up", ArenaReport.NumberOrNull(TtdBottomUp))
            .Add("ttd_deviation", ArenaReport.NumberOrNull(TtdDeviation))
            .Add("ttd_bandwidth", ArenaReport.NumberOrNull(TtdBandwidth))
            .Add("ttd_pass", TtdPass ? JsonBool.True : JsonBool.False)
            .Build();
    }

    /// <summary>一次 <see cref="ArenaSimulation.Run"/> 调用的完整、稳定、可序列化的纯数据结果。</summary>
    public sealed class ArenaReport
    {
        public Id ScenarioId { get; }

        public ulong BaseSeed { get; }

        /// <summary>按 <c>scenario.Levels × scenario.Opponent.LevelOffsets</c> 的嵌套遍历顺序
        /// （外层 level、内层 offset，与两个源列表的原始顺序一致）——确定性排列，供
        /// <see cref="ToJson"/> 与"胜率矩阵"表格直接按顺序渲染。</summary>
        public IReadOnlyList<ArenaCellResult> Cells { get; }

        /// <summary>按 <c>scenario.Levels</c> 顺序，每个等级一行（<c>level_offset == 0</c> 那个格子）。</summary>
        public IReadOnlyList<ReconciliationRow> Reconciliation { get; }

        internal ArenaReport(Id scenarioId, ulong baseSeed, IReadOnlyList<ArenaCellResult> cells, IReadOnlyList<ReconciliationRow> reconciliation)
        {
            ScenarioId = scenarioId;
            BaseSeed = baseSeed;
            Cells = cells;
            Reconciliation = reconciliation;
        }

        /// <summary>把本报告写成确定性 JSON 文本（同一份 <see cref="Cells"/>/<see cref="Reconciliation"/>
        /// 内容两次调用逐字节相同——经 <see cref="Core.Foundation.Common.Json.JsonWriter"/>，键顺序
        /// 固定、数字格式固定、不含任何时间戳/随机成分）。</summary>
        public string ToJson()
        {
            var cellsArray = Cells.Select(c => (JsonValue)c.ToJson()).ToList();
            var reconciliationArray = Reconciliation.Select(r => (JsonValue)r.ToJson()).ToList();

            var root = new JsonObjectBuilder()
                .Add("scenario_id", new JsonString(ScenarioId.Value))
                .Add("base_seed", new JsonNumber(BaseSeed))
                .Add("cells", new JsonArray(cellsArray))
                .Add("reconciliation", new JsonArray(reconciliationArray))
                .Build();

            return JsonWriter.Write(root);
        }

        /// <summary>NaN/Infinity 在 JSON 里没有合法表示（<c>JsonWriter</c> 对此直接抛异常，见该类型
        /// "F-01 根治"判断记录）——本报告里的均值/百分位数在"分母场次数为 0"（如某格子玩家一场未胜，
        /// <see cref="ArenaCellResult.TtkMeanSeconds"/> 恒 NaN）或"生物全程零伤害"
        /// （<see cref="ArenaCellResult.TtdEstimateMean"/> 可能是 <see cref="double.PositiveInfinity"/>）
        /// 时会产生这类值，写成 JSON <c>null</c>（"本次未产生该统计量"，不是 0，调用方/测试需要按
        /// <c>null</c> 单独处理，不能当 0 用）。</summary>
        internal static JsonValue NumberOrNull(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? (JsonValue)JsonNull.Instance : new JsonNumber(value);
    }

    /// <summary>
    /// T-N6-4（ADR-0035 决策 3）：场景运行器——对 <c>kind=arena</c> 的 <see cref="ScenarioDef"/> 按
    /// <c>levels[i] × opponent.level_offsets[j]</c> 的每个格子各跑 <c>runs</c> 场 <see cref="FightRunner"/>，
    /// 聚合输出 <see cref="ArenaReport"/>。
    /// <para>
    /// 判断记录（种子派生：不依赖 <c>Core.Foundation.Rng.SeedDerivation</c>，本类型自带一份最小混合
    /// 函数）：<c>SeedDerivation</c> 是 <c>core/foundation/rng</c> 的 <c>internal</c> 类型，
    /// <c>Core.Sim</c> 拿不到；本类型的"多种子"需求也不是"给一个 <c>Id</c> 流分流"，而是"给
    /// (等级, 偏移, 第几次重复) 这个三元组派生一个确定性种子"，语义不同，没有必要为了复用而放宽
    /// <c>SeedDerivation</c> 的可见性。<see cref="DeriveSeed"/> 自带一份基于 SplitMix64 终结步骤
    /// （同架构惯例，见 <c>SeedDerivation.SplitMix64</c>）的最小混合：把 <c>base_seed</c> 与三个下标
    /// 分别乘以不同的固定质数常量后异或，再过一遍 SplitMix64 终结步骤去相关——纯函数、只依赖入参本身，
    /// 不使用系统时间/线程调度，同输入恒同输出（满足"同一场景同 <c>base_seed</c> 两次
    /// <c>ArenaReport.ToJson()</c> 逐字节相同"的确定性要求）；不同 <c>runIndex</c>/<c>level</c>/
    /// <c>offset</c> 组合产生的种子两两不同（三个下标各自的常量互质、混合后碰撞概率可忽略），保证
    /// 每个格子的 <c>runs</c> 场战斗确实是"随机但确定性"的独立种子，不是同一种子重复跑 N 次。
    /// </para>
    /// <para>
    /// 判断记录（越级矩阵下界夹到 1，不追加二次生物等级校准）：<c>player.level + offset</c> 可能为
    /// 非正数（如等级 1、偏移 -5 = -4）——生物不可能以非正等级出生，<see cref="RunCell"/> 按
    /// <c>Math.Max(1, level + offset)</c> 夹到最低 1 级（越级矩阵矮的那一侧的格子因此实际测的是
    /// "1 级生物 vs 该等级玩家"，比数据字面 <c>level+offset</c> 更弱，但这正确反映了"生物不能比
    /// 1 级更弱"这一游戏内在约束，不是仿真运行器的缺陷）；上界不做类似夹取——生物等级本身可以任意
    /// 高于 <see cref="AnchorTable.MaxLevel"/>（只是 <see cref="AnchorCreatureLevelScaler"/> 内部
    /// 换算基础属性时会把锚点查询夹到 <c>MaxLevel</c>，见该类型判断记录），两者是不同层次的"越界"，
    /// 互不影响。
    /// </para>
    /// <para>
    /// 判断记录（TTD 均值如何处理 Infinity）：单场 <see cref="FightResult.TtdEstimate"/> 在生物本场
    /// 零伤害时是 <see cref="double.PositiveInfinity"/>（见该属性判断记录）——直接对含 Infinity 的
    /// 序列取算术平均，只要有一个 Infinity 整个均值就是 Infinity，会掩盖其余场次的真实数值。
    /// <see cref="Aggregate"/> 因此过滤掉 Infinity 场次后再求均值（有限场次一场都没有时，均值本身
    /// 也退化为 Infinity，如实反映"这 <c>runs</c> 场生物从未伤到玩家"）——这不是要凑出一个好看的
    /// 有限数字，而是不让"生物完全没输出"这一真实情况被少数正常场次的均值稀释掉。
    /// </para>
    /// </summary>
    public static class ArenaSimulation
    {
        public static ArenaReport Run(
            ScenarioDef scenario, AnchorTable anchors, IReadOnlyList<IDataSource> dataSources,
            bool failOnUnknownTable = false)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (anchors == null) throw new ArgumentNullException(nameof(anchors));
            if (dataSources == null) throw new ArgumentNullException(nameof(dataSources));
            if (scenario.Kind != ScenarioKind.Arena)
            {
                throw new ArgumentException($"ArenaSimulation.Run 只接受 kind=arena 的场景，实际为 {scenario.Kind}", nameof(scenario));
            }
            if (scenario.Player.QualityId == null)
            {
                throw new ArgumentException("ArenaSimulation.Run：scenario.Player.QualityId 不能为空（标准玩家生成器需要期望装备品质）。", nameof(scenario));
            }

            var cells = new List<ArenaCellResult>();
            foreach (var level in scenario.Levels)
            {
                foreach (var offset in scenario.Opponent.LevelOffsets)
                {
                    cells.Add(RunCell(scenario, dataSources, level, offset, failOnUnknownTable));
                }
            }

            var reconciliation = BuildReconciliation(scenario, anchors, cells);
            return new ArenaReport(scenario.Id, scenario.BaseSeed, cells, reconciliation);
        }

        private static ArenaCellResult RunCell(
            ScenarioDef scenario, IReadOnlyList<IDataSource> dataSources, int level, int offset, bool failOnUnknownTable)
        {
            var creatureLevel = Math.Max(1, level + offset);
            var results = new List<FightResult>(scenario.Runs);

            for (var runIndex = 0; runIndex < scenario.Runs; runIndex++)
            {
                var seed = DeriveSeed(scenario.BaseSeed, level, offset, runIndex);
                var options = new FightRunnerOptions
                {
                    DataSources = dataSources,
                    ClassId = scenario.Player.ClassId,
                    PlayerLevel = level,
                    QualityId = scenario.Player.QualityId!.Value,
                    CreatureId = scenario.Opponent.CreatureId,
                    CreatureLevel = creatureLevel,
                    Seed = seed,
                    MaxTicks = scenario.MaxTicks,
                    FailOnUnknownTable = failOnUnknownTable,
                };
                results.Add(FightRunner.Run(options));
            }

            return Aggregate(level, offset, creatureLevel, results);
        }

        private static ArenaCellResult Aggregate(int level, int offset, int creatureLevel, IReadOnlyList<FightResult> results)
        {
            var runs = results.Count;
            var wins = results.Count(r => r.Outcome == FightOutcome.PlayerWin);
            var winRate = runs > 0 ? (double)wins / runs : 0.0;

            var winDurations = results.Where(r => r.Outcome == FightOutcome.PlayerWin)
                .Select(r => r.DurationSeconds).OrderBy(x => x).ToList();
            var ttkMean = winDurations.Count > 0 ? winDurations.Average() : double.NaN;
            var ttkMedian = Percentile(winDurations, 0.5);
            var ttkP10 = Percentile(winDurations, 0.10);
            var ttkP90 = Percentile(winDurations, 0.90);

            var finiteTtd = results.Select(r => r.TtdEstimate).Where(v => !double.IsInfinity(v)).ToList();
            var ttdMean = finiteTtd.Count > 0 ? finiteTtd.Average() : double.PositiveInfinity;

            var hitRateMean = runs > 0 ? results.Average(r => r.PlayerHitRate) : double.NaN;
            var dpsMean = runs > 0 ? results.Average(r => r.PlayerDps) : double.NaN;
            var playerMaxHealth = results.Count > 0 ? results[0].PlayerMaxHealth : double.NaN;

            var skillIds = results.SelectMany(r => r.PlayerSkillDamageShare.Keys).Distinct().ToList();
            var skillShareMean = new Dictionary<Id, double>();
            foreach (var skillId in skillIds)
            {
                var sum = results.Sum(r => r.PlayerSkillDamageShare.TryGetValue(skillId, out var s) ? s : 0.0);
                skillShareMean[skillId] = runs > 0 ? sum / runs : 0.0;
            }

            return new ArenaCellResult(
                level, offset, creatureLevel, runs, winRate,
                ttkMean, ttkMedian, ttkP10, ttkP90, ttdMean, hitRateMean, dpsMean, playerMaxHealth, skillShareMean);
        }

        /// <summary>线性插值百分位数（<paramref name="p"/> ∈ [0,1]）；<paramref name="sorted"/> 为空
        /// 时返回 <see cref="double.NaN"/>。<paramref name="sorted"/> 须已按升序排列。</summary>
        private static double Percentile(IReadOnlyList<double> sorted, double p)
        {
            if (sorted.Count == 0) return double.NaN;
            if (sorted.Count == 1) return sorted[0];

            var rank = p * (sorted.Count - 1);
            var lowerIndex = (int)Math.Floor(rank);
            var upperIndex = (int)Math.Ceiling(rank);
            if (lowerIndex == upperIndex) return sorted[lowerIndex];

            var frac = rank - lowerIndex;
            return sorted[lowerIndex] * (1 - frac) + sorted[upperIndex] * frac;
        }

        private static List<ReconciliationRow> BuildReconciliation(
            ScenarioDef scenario, AnchorTable anchors, IReadOnlyList<ArenaCellResult> cells)
        {
            var dpsBandwidth = scenario.Bandwidths.TryGetValue("dps", out var db) ? db : 0.25;
            var hpBandwidth = scenario.Bandwidths.TryGetValue("hp", out var hb) ? hb : 0.25;
            var ttdBandwidth = scenario.Bandwidths.TryGetValue("ttd", out var tb) ? tb : 0.25;

            var rows = new List<ReconciliationRow>();
            foreach (var level in scenario.Levels)
            {
                var cell = cells.First(c => c.PlayerLevel == level && c.LevelOffset == 0);
                var anchor = anchors.Get(level);

                var dpsDeviation = RelativeDeviation(anchor.Dps, cell.PlayerDpsMean);
                var hpDeviation = RelativeDeviation(anchor.Hp, cell.PlayerMaxHealth);
                var ttdDeviation = RelativeDeviation(anchor.TtdSeconds, cell.TtdEstimateMean);

                rows.Add(new ReconciliationRow(
                    level,
                    anchor.Dps, cell.PlayerDpsMean, dpsDeviation, dpsBandwidth, dpsDeviation <= dpsBandwidth,
                    anchor.Hp, cell.PlayerMaxHealth, hpDeviation, hpBandwidth, hpDeviation <= hpBandwidth,
                    anchor.TtdSeconds, cell.TtdEstimateMean, ttdDeviation, ttdBandwidth, ttdDeviation <= ttdBandwidth));
            }
            return rows;
        }

        private static double RelativeDeviation(double topDown, double bottomUp)
        {
            if (double.IsNaN(bottomUp) || double.IsInfinity(bottomUp)) return double.PositiveInfinity;
            if (topDown == 0) return bottomUp == 0 ? 0.0 : double.PositiveInfinity;
            return Math.Abs(topDown - bottomUp) / Math.Abs(topDown);
        }

        /// <summary>见类型判断记录"种子派生"：纯函数，(baseSeed, level, offset, runIndex) → 确定性种子。</summary>
        internal static ulong DeriveSeed(ulong baseSeed, int level, int offset, int runIndex)
        {
            unchecked
            {
                var x = baseSeed;
                x ^= (ulong)(uint)level * 0x9E3779B97F4A7C15UL;
                x ^= (ulong)(uint)(offset + 1000) * 0xC2B2AE3D27D4EB4FUL;
                x ^= (ulong)(uint)runIndex * 0x165667B19E3779F9UL;

                // SplitMix64 终结步骤（去相关，避免三个异或分量的低位模式直接透传到输出）。
                x += 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                x ^= x >> 31;
                return x;
            }
        }
    }
}
