using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.SimLoop;
using Core.Gameplay.Economy;
using Core.Numbers.Progression;

namespace Core.Sim
{
    /// <summary>一个等级的成长轨迹样本——实测四条轨迹（时长/装备等级/命中率/金币）与各自的对照基准/
    /// 偏离/带宽内判定，见 <see cref="GrowthSimulation"/> 类型判断记录"四条轨迹的对照基准"。</summary>
    public sealed class GrowthLevelSample
    {
        public int Level { get; }

        /// <summary>本级完成升级实际打了多少场胜利战斗（不含超时/落败的异常终止场次，见类型判断
        /// 记录"提前终止"）。</summary>
        public int Kills { get; }

        public double ActualDurationSeconds { get; }
        public double ExpectedDurationSeconds { get; }
        public double DurationDeviation { get; }
        public bool DurationPass { get; }

        public double AvgItemLevel { get; }
        public double ExpectedItemLevel { get; }
        public double ItemLevelDeviation { get; }
        public bool ItemLevelPass { get; }

        public double HitRate { get; }
        public double ExpectedHitRate { get; }
        public double HitRateDeviation { get; }
        public bool HitRatePass { get; }

        public double CumulativeGold { get; }
        public double ExpectedCumulativeGold { get; }
        public double GoldDeviation { get; }
        public bool GoldPass { get; }

        internal GrowthLevelSample(
            int level, int kills,
            double actualDurationSeconds, double expectedDurationSeconds, double durationDeviation, bool durationPass,
            double avgItemLevel, double expectedItemLevel, double itemLevelDeviation, bool itemLevelPass,
            double hitRate, double expectedHitRate, double hitRateDeviation, bool hitRatePass,
            double cumulativeGold, double expectedCumulativeGold, double goldDeviation, bool goldPass)
        {
            Level = level;
            Kills = kills;
            ActualDurationSeconds = actualDurationSeconds;
            ExpectedDurationSeconds = expectedDurationSeconds;
            DurationDeviation = durationDeviation;
            DurationPass = durationPass;
            AvgItemLevel = avgItemLevel;
            ExpectedItemLevel = expectedItemLevel;
            ItemLevelDeviation = itemLevelDeviation;
            ItemLevelPass = itemLevelPass;
            HitRate = hitRate;
            ExpectedHitRate = expectedHitRate;
            HitRateDeviation = hitRateDeviation;
            HitRatePass = hitRatePass;
            CumulativeGold = cumulativeGold;
            ExpectedCumulativeGold = expectedCumulativeGold;
            GoldDeviation = goldDeviation;
            GoldPass = goldPass;
        }

        internal JsonObject ToJson() => new JsonObjectBuilder()
            .Add("level", new JsonNumber(Level))
            .Add("kills", new JsonNumber(Kills))
            .Add("actual_duration_seconds", ArenaReport.NumberOrNull(ActualDurationSeconds))
            .Add("expected_duration_seconds", ArenaReport.NumberOrNull(ExpectedDurationSeconds))
            .Add("duration_deviation", ArenaReport.NumberOrNull(DurationDeviation))
            .Add("duration_pass", DurationPass ? JsonBool.True : JsonBool.False)
            .Add("avg_item_level", ArenaReport.NumberOrNull(AvgItemLevel))
            .Add("expected_item_level", ArenaReport.NumberOrNull(ExpectedItemLevel))
            .Add("item_level_deviation", ArenaReport.NumberOrNull(ItemLevelDeviation))
            .Add("item_level_pass", ItemLevelPass ? JsonBool.True : JsonBool.False)
            .Add("hit_rate", ArenaReport.NumberOrNull(HitRate))
            .Add("expected_hit_rate", ArenaReport.NumberOrNull(ExpectedHitRate))
            .Add("hit_rate_deviation", ArenaReport.NumberOrNull(HitRateDeviation))
            .Add("hit_rate_pass", HitRatePass ? JsonBool.True : JsonBool.False)
            .Add("cumulative_gold", ArenaReport.NumberOrNull(CumulativeGold))
            .Add("expected_cumulative_gold", ArenaReport.NumberOrNull(ExpectedCumulativeGold))
            .Add("gold_deviation", ArenaReport.NumberOrNull(GoldDeviation))
            .Add("gold_pass", GoldPass ? JsonBool.True : JsonBool.False)
            .Build();
    }

    /// <summary>一次 <see cref="GrowthSimulation.Run"/> 调用的完整、稳定、可序列化结果——
    /// <see cref="Levels"/> 为 <c>runs</c> 个种子的逐等级均值，另附两组"同一数量、两条独立路径分别
    /// 算出"的累计经验/金币总量，供调用方/测试验证"确实是被仿真模块发放/入账的，不是本运行器自己
    /// 手算的"，见类型判断记录"经验/金币的双路径自证"。</summary>
    public sealed class GrowthReport
    {
        public Id ScenarioId { get; }

        public ulong BaseSeed { get; }

        public int Runs { get; }

        /// <summary>按 <c>level_from</c>..<c>level_to-1</c> 顺序，每个"实际经历过成长"的等级一行
        /// （<c>level_to</c> 本身是满级终点，不产生"该级时长"这一概念，不在此列，见类型判断记录）。</summary>
        public IReadOnlyList<GrowthLevelSample> Levels { get; }

        /// <summary>全程 <see cref="IProgressionHost.GrantXp"/> 返回值（真实发放额度）之和，
        /// runs 个种子的均值。</summary>
        public double CumulativeXpGrantedViaApi { get; }

        /// <summary>同一次运行内，独立扫描事件流 <c>progression.xp_gained</c>
        /// （<see cref="XpGainedEvent"/>，按玩家单位过滤）累加出的总量，runs 个种子的均值——与
        /// <see cref="CumulativeXpGrantedViaApi"/> 理应逐值相等（同型两条路径互证，见类型判断
        /// 记录）。</summary>
        public double CumulativeXpGrantedViaEvents { get; }

        /// <summary>全程结束时 <see cref="IEconomyHost.GetBalance"/> 读到的金币余额，runs 个种子的
        /// 均值。</summary>
        public double CumulativeGoldViaBalance { get; }

        /// <summary>同一次运行内，独立扫描事件流 <c>economy.currency_changed</c>
        /// （<see cref="CurrencyChangedEvent"/>，按玩家单位与金币币种过滤）累加
        /// <c>NewValue-OldValue</c> 算出的净额，runs 个种子的均值——与
        /// <see cref="CumulativeGoldViaBalance"/> 理应逐值相等。</summary>
        public double CumulativeGoldViaEvents { get; }

        internal GrowthReport(
            Id scenarioId, ulong baseSeed, int runs, IReadOnlyList<GrowthLevelSample> levels,
            double cumulativeXpGrantedViaApi, double cumulativeXpGrantedViaEvents,
            double cumulativeGoldViaBalance, double cumulativeGoldViaEvents)
        {
            ScenarioId = scenarioId;
            BaseSeed = baseSeed;
            Runs = runs;
            Levels = levels;
            CumulativeXpGrantedViaApi = cumulativeXpGrantedViaApi;
            CumulativeXpGrantedViaEvents = cumulativeXpGrantedViaEvents;
            CumulativeGoldViaBalance = cumulativeGoldViaBalance;
            CumulativeGoldViaEvents = cumulativeGoldViaEvents;
        }

        /// <summary>把本报告写成确定性 JSON 文本，惯例同 <see cref="ArenaReport.ToJson"/>。</summary>
        public string ToJson()
        {
            var levelsArray = Levels.Select(l => (JsonValue)l.ToJson()).ToList();
            var root = new JsonObjectBuilder()
                .Add("scenario_id", new JsonString(ScenarioId.Value))
                .Add("base_seed", new JsonNumber(BaseSeed))
                .Add("runs", new JsonNumber(Runs))
                .Add("levels", new JsonArray(levelsArray))
                .Add("cumulative_xp_granted_via_api", ArenaReport.NumberOrNull(CumulativeXpGrantedViaApi))
                .Add("cumulative_xp_granted_via_events", ArenaReport.NumberOrNull(CumulativeXpGrantedViaEvents))
                .Add("cumulative_gold_via_balance", ArenaReport.NumberOrNull(CumulativeGoldViaBalance))
                .Add("cumulative_gold_via_events", ArenaReport.NumberOrNull(CumulativeGoldViaEvents))
                .Build();
            return JsonWriter.Write(root);
        }
    }

    /// <summary>
    /// T-N6-5（ADR-0035 决策 3 成长仿真）：以战斗仿真（<see cref="FightRunner"/>）为输入，模拟标准玩家
    /// 从 <c>level_from</c> 到 <c>level_to</c> 的整条成长曲线——真实战斗、真实击杀/任务经验发放
    /// （<see cref="IProgressionHost.GrantXp"/>）、真实掉落（<see cref="Core.Gameplay.Loot.LootHost"/>
    /// 内部的 <c>RollDetailed</c>，由死亡事件自动触发，见类型判断记录"掉落与货币走真实监听器，不重复
    /// 掷骰"）、真实经济入账/拾取/换装/出售（<see cref="IEconomyHost"/>/<see
    /// cref="Core.Carriers.Item.EquipmentHost"/>），输出每级实际时长、装备等级轨迹、命中率、金币累积
    /// 四条轨迹与对照基准/偏离。
    /// <para>
    /// <b>简化路径模型</b>（任务书"标准玩家行为是简化模型：按怪当量击杀、按占比做任务"，ADR-0035
    /// 决策 3"负面后果"原文已预先声明）：玩家在每一级循环"对同级普通怪打一场 → 处理战果 → 加
    /// <c>G(L)</c> 秒击杀间隔"，直至真实升级；每次击杀后另外按 <c>Q(L)</c> 当量顺带发放一次
    /// 任务/探索等价经验与金币（见类型判断记录"任务当量的顺带折算"），不单独模拟任务/探索这两类
    /// 独立活动本身的时间开销——升级只由真实 <c>GrantXp</c> 的阈值判定触发，本运行器不预判"打了
    /// 每级怪当量(L)只怪就该升级"，实际击杀次数由真实战斗/经验结算的随机波动决定。
    /// </para>
    /// <para>
    /// <b>判断记录（掉落与货币走真实监听器，不重复掷骰）</b>：<c>Core.Gameplay.Assembly
    /// .GameplayAssembly</c> 无条件装配 <c>CreatureDeathLootListener</c>（订阅 <c>unit.died</c>，
    /// 任何 <see cref="HeadlessWorld"/> 都逃不开这一条既有接线，见该类型源码）——生物死亡时它已经
    /// 调用了一次 <c>LootHost.RollDetailed</c>，货币条目按 <c>CurrencyDepositPolicy.OnKill</c>（本
    /// 装配根默认值）直接入账给击杀者，物品条目落地为地面 <see cref="DroppedLootEntity"/>。若本类型
    /// 再自行调用一次 <c>RollDetailed</c>，等于对同一次死亡多掷了一次骰子（重复消耗
    /// <c>IRngHost</c> 序列、双倍入账/掉落），是明确的正确性缺陷，不是"更贴合任务书字面提到
    /// RollDetailed"的合规写法。本类型因此不再手动调用 <c>RollDetailed</c>，而是消费监听器已经
    /// 产生的结果——货币经 <see cref="IEconomyHost.GetBalance"/> 前后差值/<c>economy
    /// .currency_changed</c> 事件确认到账，物品经 <see cref="WorldSim.QueryEntities"/>
    /// （<c>Kind=</c><see cref="EntityKinds.Loot"/>）找到掉落实体后调用真实的
    /// <c>LootHost.PickUp</c> 拾取进背包——<c>RollDetailed</c> 本身仍然是真正被调用的那份代码，只是
    /// 调用方是监听器而不是本类型，链路上没有任何一步是"手算"。为保证拾取一定成功（默认
    /// <c>LootOptions.PickupRange</c> 3.0 可能小于本数据集技能射程 5.0），<see cref="Run"/> 装配世界
    /// 时经新增的 <see cref="HeadlessWorldOptions.LootOptions"/> 放宽了这个半径，见该属性判断记录。
    /// </para>
    /// <para>
    /// <b>判断记录（击杀/任务经验为何直接调用 <c>GrantXp</c>，不依赖 <c>CreatureDeathXpListener</c>）</b>：
    /// 该监听器同样无条件装配，但它的默认击杀经验来源 id 是 <c>CreatureDeathXpListener
    /// .DefaultKillXpSourceId</c>（字面 <c>"prog.xp_source.kill"</c>），本数据集登记的却是
    /// <c>prog.xp_source.sim_kill</c>（<c>sim_</c> 前缀惯例，见数据集 README"判断记录 1"）——两者不
    /// 是同一个 id，监听器内部 <c>HasXpSource(_killXpSourceId)</c> 查不到会静默跳过（该类型判断记录
    /// "未登记的来源 id 不阻断死亡结算"），不会发放，也不会与本类型下面的显式调用重复发放（两者不会
    /// 同时命中同一个来源 id）。<see cref="HeadlessWorldOptions"/> 本可以新增
    /// <c>ProgressionOptions</c> 转发属性去配置监听器的 <c>KillXpSourceId</c> 让监听器接管，但那样
    /// 还是要解决"任务/探索当量经验监听器完全不发放（没有对应的击杀事件）"这一半的缺口，直接调用
    /// <c>GrantXp(unitId, sourceId, XpContext)</c> 两条来源都发（击杀＋任务当量）更简单、路径统一，
    /// 且 <c>GrantXp</c> 本身就是任务书原文指名的"真实 API"（只传入来源等级/当量，实际发放额度仍由
    /// <c>ProgressionHost</c> 内部公式计算，不是本类型手算）。
    /// </para>
    /// <para>
    /// <b>判断记录（任务当量的顺带折算）</b>：数值总纲 4.7 节"升级所需(L) = 击杀基数(L) ×
    /// 每级怪当量(L) × (1+Q(L))"——<c>每级怪当量(L)</c> 本身就是"只看击杀"这一部分对应的目标击杀
    /// 场次数：击杀 <c>monsterEquivalent(L)</c> 次、每次 <c>killBase(L)</c> 经验，总击杀经验恰为
    /// <c>killBase×monsterEquivalent</c>；要让这 <c>monsterEquivalent(L)</c> 次击杀正好把总需求
    /// （<c>killBase×monsterEquivalent×(1+Q)</c>）填满、不多不少，每次击杀还需额外发
    /// <c>killBase(L)×Q(L)</c> 的任务当量经验——即 <c>Equivalent=Q(L)</c>（<c>GrantXp</c> 内部按
    /// <c>Equivalent×baseAmount</c> 计），本类型每次真实击杀后紧接着发一次"任务当量"经验
    /// （<c>XpContext(sourceLevel: L, equivalent: Q(L))</c>，<c>kind=quest</c>）与金币（
    /// <c>IEconomyHost.TryGetGoldBaseAmount(L)</c>——同样是真实 API，不是 <c>econ.gold_base_curve</c>
    /// 曲线的手工重算——乘以同一当量后经 <see cref="IEconomyHost.Add"/> 入账），让"击杀:任务"的经验/
    /// 金币比例在整条轨迹上持续保持 4.7/4.8 节设计的比例，而不是先攒一大笔击杀经验、升级前再补发一次
    /// 任务经验的"事后找齐"写法——两种写法数值总量相同，前者更贴合"边打边接任务"的真实体验节奏，且
    /// 不需要额外判断"这一级还差多少经验才该发任务"。判断记录（联调排错追记）：本字段初版误写成
    /// <c>Q(L)/(1-Q(L))</c>（把"任务份额相对击杀份额的比例"和"任务份额相对总需求的比例"搞混），会让
    /// 每次击杀实际发放的经验变成 <c>killBase/(1-Q)</c>（比正确值 <c>killBase×(1+Q)</c> 更高——两者
    /// 仅在 Q 很小时接近），达标所需击杀次数因此变成 <c>monsterEquivalent×(1-Q²)</c>，比设计意图的
    /// <c>monsterEquivalent</c> 次更少——完整场景联调测试实测 L3/L4/L8 三个等级"实际击杀数明显少于
    /// monsterEquivalent、每级时长系统性偏短"，定位到此处后改用 <c>Q(L)</c> 本身，见
    /// <see cref="RunOnce"/> 内该行同款判断记录与 <c>core/sim/tests/data/README.md</c>
    /// "T-N6-5 调参记录"。
    /// </para>
    /// <para>
    /// <b>判断记录（四条轨迹的对照基准）</b>：时长对 <c>sim.anchor.level_duration_seconds(L)</c>、
    /// 装备等级对 <c>sim.anchor.expected_item_level(L)</c>，均是任务书原文点名的锚点字段，口径
    /// 直接。命中率与金币锚点表没有对应字段，按任务书"写明口径"处理——命中率对照同等级、偏移 0 的
    /// 一场独立 <see cref="FightRunner.Run"/>（与 <see cref="ArenaSimulation"/> offset=0 格子同一
    /// 语义，只是本类型不跑整张矩阵，只在需要的等级各跑一场，种子取
    /// <c>DeriveSeed(baseSeed, level, "growth_hit_rate_ref")</c>，与成长轨迹本身的种子互不重叠）；
    /// 金币按 4.8 节公式解析推算期望累计——<c>Σ_{l=level_from}^{L} 每级怪当量(l) × 金币基数(l) ×
    /// (1 + Q(l))</c>（与经验联动同一口径，见 <see cref="ExpectedCumulativeGoldAt"/>），
    /// 只计入"击杀+任务"两类当量收入，不含出售旧装备的额外收入（出售收入依赖具体掉落序列，无法用
    /// 闭式公式表达）——若某一级卖出装备的收入占比不可忽略，实测累计金币会略高于这条期望曲线，属于
    /// 已知、可接受的正向偏离（带宽 0.35 相对宽松，正常情况下不会因此不达标）。
    /// </para>
    /// <para>
    /// <b>判断记录（换装决策：评分口径与 <see cref="StandardPlayerBuilder"/> 完全同构）</b>：新装备与
    /// 当前装备均按 <see cref="EquipmentScoreAnalyzer.Score"/>（模板 <c>stats[]</c> + 词缀反解值，
    /// 词缀反解沿用 <see cref="StandardPlayerBuilder.ComputeAffixContribution"/>——与
    /// <c>EquipmentHost.ApplyGrants</c>/<c>ApplyAffixValues</c> 实际穿戴时执行的同一份运算）评分，
    /// 新分数高则穿上（<c>EquipmentHost.Equip</c> 自动把原槽位物品放回背包并在返回值里带出，见该方法
    /// 判断记录"换装"）、随后卖掉被换下来的旧装备；新分数不高于当前则直接卖掉新掉落的这件，从不让
    /// 背包无限堆积——与"一键换装最优"（07 第 1.2 节）同一评分口径，只是决策对象从"全部槽位一次性
    /// 重算"收窄成"这一件掉落物该不该顶替这一个槽位"（成长仿真是逐件掉落决策，不是一键重装）。
    /// </para>
    /// <para>
    /// <b>判断记录（生物模板按等级换挡，不复用 <see cref="AnchorCreatureLevelScaler"/> 缩放同一模板）</b>：
    /// <see cref="ArenaSimulation"/> 全程只用 <c>scenario.Opponent.CreatureId</c> 这一个模板、靠
    /// <see cref="AnchorCreatureLevelScaler"/> 把它的基础属性缩放到任意目标等级——但该模板的
    /// <c>loot_table_ref"</c> 是模板自身的静态字段，缩放属性并不会连带把掉落表换成"更高等级该有的
    /// 掉落"（本数据集 <c>creature.sim_wolf_l1.loot_table_ref</c> 恒指向 <c>loot.table.sim_wolf_l1</c>
    /// 那份低额度掉落）。成长仿真恰恰需要"随玩家等级同步变化的金币/装备产出"，因此改为在
    /// <c>creature.sim_wolf_{l1,l5,l10,l15,l20}</c>（数据集已经登记的五档同一生物家族模板，各自的
    /// <c>loot_table_ref</c> 已按档独立标定）之间按"不超过当前玩家等级的最大档位"切换——同一约定
    /// 也用于 <see cref="CoverageSimulation"/>。见 <see cref="ResolveCreatureTemplateForLevel"/>。
    /// </para>
    /// <para>
    /// <b>判断记录（数据集补齐：loot.table 追加护甲三槽掉落条目）</b>：T-N6-2b/T-N6-4 阶段的
    /// <c>loot.table.*</c> 只登记了主手与头部两个槽位（供最小烟雾测试与越级矩阵用），成长仿真需要
    /// 全部 5 个装备槽位都有机会被替换掉——否则胸/腿/脚三槽永远停留在 1 级出生时
    /// <see cref="StandardPlayerBuilder"/> 给的装备，"各槽平均装备等级"轨迹不可能追上
    /// <c>expected_item_level(L)</c>。本任务给 5 档普通怪掉落表各追加了 <c>chest/legs/feet</c> 三条
    /// common 品质条目（权重 0.3，与既有主手条目同一惯例），不改任何已有条目的取值，见数据集
    /// README"T-N6-5 调参记录"。
    /// </para>
    /// </summary>
    public static class GrowthSimulation
    {
        /// <summary>安全阀：单级如果打了这么多场胜利战斗仍未真实升级（理论上按 4.7 节公式不该发生，
        /// 只有数据配置严重失衡时才会触达），判定为数据异常并中止本级、如实记录已发生的部分——不
        /// 无限循环，也不伪造一个"没有真的升级"的假样本。</summary>
        public const int MaxKillsPerLevelSafety = 400;

        private static readonly Id PlayerId = new Id("unit.sim_growth_player");
        private static readonly Id MapId = new Id("world.sim_growth");
        private static readonly Id GameId = new Id("game.sim_growth");
        private static readonly Vec2 CreatureSpawnOffset = new Vec2(3, 0);
        private static readonly Id KillIntervalRestSourceId = new Id("sim.growth_kill_interval_rest");

        public static GrowthReport Run(
            ScenarioDef scenario, AnchorTable anchors, IReadOnlyList<IDataSource> dataSources,
            bool failOnUnknownTable = false)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (anchors == null) throw new ArgumentNullException(nameof(anchors));
            if (dataSources == null) throw new ArgumentNullException(nameof(dataSources));
            if (scenario.Kind != ScenarioKind.Growth)
            {
                throw new ArgumentException($"GrowthSimulation.Run 只接受 kind=growth 的场景，实际为 {scenario.Kind}", nameof(scenario));
            }
            if (scenario.Player.QualityId == null)
            {
                throw new ArgumentException("GrowthSimulation.Run：scenario.Player.QualityId 不能为空。", nameof(scenario));
            }
            if (scenario.LevelFrom == null || scenario.LevelTo == null)
            {
                throw new ArgumentException("GrowthSimulation.Run：scenario.LevelFrom/LevelTo 均不能为空。", nameof(scenario));
            }

            var levelFrom = scenario.LevelFrom.Value;
            var levelTo = scenario.LevelTo.Value;

            var perRunSamples = new List<List<GrowthLevelSample>>();
            var xpViaApi = new List<double>();
            var xpViaEvents = new List<double>();
            var goldViaBalance = new List<double>();
            var goldViaEvents = new List<double>();

            for (var runIndex = 0; runIndex < scenario.Runs; runIndex++)
            {
                var seed = ArenaSimulation.DeriveSeed(scenario.BaseSeed, levelFrom, runIndex, 0);
                var run = RunOnce(scenario, anchors, dataSources, seed, failOnUnknownTable);
                perRunSamples.Add(run.Levels);
                xpViaApi.Add(run.CumulativeXpGrantedViaApi);
                xpViaEvents.Add(run.CumulativeXpGrantedViaEvents);
                goldViaBalance.Add(run.CumulativeGoldViaBalance);
                goldViaEvents.Add(run.CumulativeGoldViaEvents);
            }

            var mergedLevels = MergeLevelSamples(scenario, perRunSamples, levelFrom, levelTo);

            return new GrowthReport(
                scenario.Id, scenario.BaseSeed, scenario.Runs, mergedLevels,
                xpViaApi.Count > 0 ? xpViaApi.Average() : 0.0,
                xpViaEvents.Count > 0 ? xpViaEvents.Average() : 0.0,
                goldViaBalance.Count > 0 ? goldViaBalance.Average() : 0.0,
                goldViaEvents.Count > 0 ? goldViaEvents.Average() : 0.0);
        }

        /// <summary>单个种子的完整运行结果，供 <see cref="Run"/> 跨种子取均值，也供测试直接调用单个
        /// 种子做确定性/双路径互证检查（本方法与其返回类型均为 <c>internal</c>——见
        /// <see cref="GrowthRunResult"/> 判断记录）。</summary>
        internal static GrowthRunResult RunOnce(
            ScenarioDef scenario, AnchorTable anchors, IReadOnlyList<IDataSource> dataSources,
            ulong seed, bool failOnUnknownTable)
        {
            var levelFrom = scenario.LevelFrom!.Value;
            var levelTo = scenario.LevelTo!.Value;
            var classId = scenario.Player.ClassId;
            var qualityId = scenario.Player.QualityId!.Value;

            var accumulator = new FightRunner.FightAccumulator();
            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = dataSources,
                Seed = seed,
                MapId = MapId,
                PlayerId = PlayerId,
                PlayerClassId = classId,
                PlayerLevel = levelFrom,
                GameId = GameId,
                FailOnUnknownTable = failOnUnknownTable,
                CombatOptions = accumulator.CombatOptions,
                LootOptions = new Core.Gameplay.Loot.LootOptions { PickupRange = 50.0 },
            });

            var standardPlayer = StandardPlayerBuilder.Build(world, classId, levelFrom, qualityId, rotationId: null);
            var rotationId = standardPlayer.RotationId;

            var registry = world.Registry;
            var progression = world.Gameplay.Carriers.Rules.Progression;
            var economy = world.Gameplay.Economy;
            var inventory = world.Gameplay.Carriers.Inventory;
            var equipment = world.Gameplay.Carriers.Equipment;

            var killXpSourceId = ResolveXpSourceId(registry, "kill");
            var questXpSourceId = ResolveXpSourceId(registry, "quest") ?? ResolveXpSourceId(registry, "discovery");
            var vendorRows = registry.GetAll("econ.vendor");
            Id? vendorId = vendorRows.Count > 0 ? vendorRows[0].Id!.Value : (Id?)null;
            var currencyRows = registry.GetAll("econ.currency");
            Id? goldCurrencyId = currencyRows.Count > 0 ? currencyRows[0].Id!.Value : (Id?)null;
            var equipmentSlots = registry.GetAll("item.slot_definition")
                .Where(r => !r.TryGetBool("is_equipment", out var eq) || eq)
                .Select(r => r.GetId("id"))
                .ToList();
            var creatureFamily = ResolveCreatureFamily(registry, scenario.Opponent);
            var budgetSolver = new BudgetSolver();

            // 复审整合项 2（B-S1）：本次仿真运行内 classId 全程固定，ProcessAcquiredItems 每次拾取都会
            // 对若干候选装备评分/反解，循环外（单次 RunOnce 只构建一次）建好 StatBudgetInfo、贯穿全程
            // 复用，避免每件候选装备都重新扫描 stat.definition/stat.weight/stat.rating_conversion。
            var statBudgetInfo = ItemBudgetCurve.BuildStatBudgetInfo(registry, classId);

            double cumulativeXpViaApi = 0.0;
            var levels = new List<GrowthLevelSample>();

            var currentLevel = progression.GetLevel(PlayerId);

            while (currentLevel < levelTo && currentLevel < levelFrom + (levelTo - levelFrom) * 4)
            {
                var level = currentLevel;
                var anchor = anchors.TryGet(level, out var row) ? row : anchors.Get(anchors.MaxLevel);
                var creatureTemplateId = ResolveCreatureTemplateForLevel(creatureFamily, level);

                double levelDuration = 0.0;
                var hitRates = new List<double>();
                var kills = 0;

                while (currentLevel == level && kills < MaxKillsPerLevelSafety)
                {
                    var spawnPos = Vec2.Zero + CreatureSpawnOffset;
                    var creatureId = world.Gameplay.Carriers.Creatures.Spawn(
                        creatureTemplateId, MapId, spawnPos, facing: Math.PI, ownerId: null, level);
                    world.Spatial.Register(creatureId, spawnPos, 0.5);

                    var fight = FightRunner.RunWithinWorld(
                        world, accumulator, PlayerId, creatureId, rotationId,
                        stepSeconds: 0.5, maxTicks: scenario.MaxTicks, moveSpeed: SimpleMoveModel.DefaultMoveSpeed,
                        maxResourceCurveSamples: 4);

                    levelDuration += fight.DurationSeconds;
                    hitRates.Add(fight.PlayerHitRate);

                    if (fight.Outcome != FightOutcome.PlayerWin)
                    {
                        // 判断记录（提前终止）：玩家落败/超时是本简化模型没有处理的分支（真实玩家会
                        // 撤退/求助，标准玩家生成器没有"重新来一次"这一行为）——如实中止本级循环，
                        // 不伪造后续场次,不影响已经记录在案的其它等级样本。本数据集按 T-N6-4/4b 校准的
                        // 锚点强度下不预期这一分支被触发,只作安全网。
                        goto FinishLevel;
                    }

                    kills++;

                    // 判断记录（击杀货币不在这里手动发放）：生物死亡（上面 FightRunner.RunWithinWorld
                    // 内部的 world.Clock.Advance 触发 unit.died）时，Core.Gameplay.Assembly
                    // .GameplayAssembly 无条件装配的 CreatureDeathLootListener 已经调用真实
                    // LootHost.RollDetailed 掷骰、按 CurrencyDepositPolicy.OnKill（本装配根默认值）
                    // 把货币条目直接入账给击杀者——本方法不重复调用 RollDetailed（重复调用等于对同一次
                    // 死亡多掷一次骰子，见类型判断记录"掉落与货币走真实监听器，不重复掷骰"）。此处只需
                    // 紧接着发放击杀经验（真实 GrantXp，不是监听器职责——见类型判断记录"击杀/任务经验
                    // 为何直接调用 GrantXp"）。
                    if (killXpSourceId.HasValue)
                    {
                        cumulativeXpViaApi += progression.GrantXp(PlayerId, killXpSourceId.Value, new XpContext(level));
                    }

                    if (questXpSourceId.HasValue && anchor.QuestShare < 1.0)
                    {
                        // 判断记录（当量取 Q(L) 本身，不是 Q(L)/(1-Q(L))）：4.7 节
                        // "升级所需(L)=killBase(L)×monsterEquivalent(L)×(1+Q(L))"——monsterEquivalent(L)
                        // 本身已经是"只看击杀这一部分"的目标击杀数，击杀 monsterEquivalent(L) 次、
                        // 每次 killBase(L) 经验，总击杀经验恰为 killBase×monsterEquivalent，正好是
                        // 总需求的 1/(1+Q) 那一份；要让 monsterEquivalent(L) 次击杀正好配上总需求
                        // 达标（不多不少），每次击杀还需额外发 killBase(L)×Q(L) 的任务当量经验——
                        // 即 Equivalent=Q(L)（GrantXp 内部按 Equivalent×baseAmount 计），不是
                        // Q(L)/(1-Q(L))（那会让总任务经验份额相对总需求变成 Q/(1+Q)，比设计意图
                        // 的 Q/(1+Q)... 见下方注：写成 Q/(1-Q) 时总经验变成
                        // killBase×monsterEquivalent×(1+Q/(1-Q))=killBase×monsterEquivalent/(1-Q)，
                        // 达标所需的击杀次数只需 monsterEquivalent×(1-Q²) 次——比设计意图的
                        // monsterEquivalent 次更快升级，正是本任务联调阶段实测"L3/L4/L8 实际击杀数
                        // 明显少于 monsterEquivalent、每级时长因此系统性偏短"的根因，已改用 Q(L) 修正）。
                        var questEquivalent = anchor.QuestShare;
                        if (questEquivalent > 0)
                        {
                            cumulativeXpViaApi += progression.GrantXp(
                                PlayerId, questXpSourceId.Value, new XpContext(level, equivalent: questEquivalent));

                            var goldBase = economy.TryGetGoldBaseAmount(level) ?? 0.0;
                            var questGold = (long)Math.Round(goldBase * questEquivalent, MidpointRounding.AwayFromZero);
                            if (questGold > 0 && goldCurrencyId.HasValue)
                            {
                                economy.Add(PlayerId, goldCurrencyId.Value, questGold, sourceId: questXpSourceId.Value);
                            }
                        }
                    }

                    PickUpAllGroundLoot(world, PlayerId, MapId);
                    ProcessAcquiredItems(
                        registry, budgetSolver, inventory, equipment, economy,
                        classId, vendorId, goldCurrencyId, equipmentSlots, statBudgetInfo);

                    world.Clock.Advance(anchor.KillIntervalSeconds);
                    levelDuration += anchor.KillIntervalSeconds;

                    // 判断记录（击杀间隔视作"玩家歇口气再打下一只"，回满生命/资源）：真实玩家在两次
                    // 交手之间会自然恢复（走位喘息/等待冷却/喝药），本数据集的属性系统不含"场外被动
                    // 生命恢复"这一机制（stat.definition 未登记任何 regen 类字段），若不显式回满，
                    // 玩家的生命值会随着一场接一场的战斗单调下降、迟早在远未打够"每级怪当量(L)"只怪
                    // 之前意外阵亡——这不是"简化路径模型"刻意要验的"战斗强度是否合理"（那已经由
                    // ArenaSimulation 的越级矩阵验过），是本仿真编排层需要显式补的一步。复用
                    // Powers.RefillAll（同 HeadlessWorldBuilder"升级回满"既有惯例），sourceId 用本
                    // 类型自己的标记，不冒用 ProgressionEventKeys.LevelUp（这不是升级触发的回满）。
                    world.Gameplay.Carriers.Rules.Powers.RefillAll(PlayerId, KillIntervalRestSourceId);

                    currentLevel = progression.GetLevel(PlayerId);
                }

                FinishLevel:
                var avgItemLevel = ComputeAvgEquippedItemLevel(registry, equipment, PlayerId, equipmentSlots);
                var cumulativeGold = goldCurrencyId.HasValue ? (double)economy.GetBalance(PlayerId, goldCurrencyId.Value) : 0.0;
                var referenceHitRate = ResolveReferenceHitRate(scenario, dataSources, level, failOnUnknownTable);
                var expectedGold = ExpectedCumulativeGoldAt(registry, economy, anchors, creatureFamily, levelFrom, level);

                var expectedDuration = anchor.LevelDurationSeconds;
                var expectedItemLevel = anchor.ExpectedItemLevel;
                var bandwidth = ResolveBandwidths(scenario);

                var durationDeviation = RelativeDeviation(expectedDuration, levelDuration);
                var itemLevelDeviation = RelativeDeviation(expectedItemLevel, avgItemLevel);
                var hitRateDeviation = RelativeDeviation(referenceHitRate, hitRates.Count > 0 ? hitRates.Average() : 0.0);
                var goldDeviation = RelativeDeviation(expectedGold, cumulativeGold);

                levels.Add(new GrowthLevelSample(
                    level, kills,
                    levelDuration, expectedDuration, durationDeviation, durationDeviation <= bandwidth.Duration,
                    avgItemLevel, expectedItemLevel, itemLevelDeviation, itemLevelDeviation <= bandwidth.ItemLevel,
                    hitRates.Count > 0 ? hitRates.Average() : 0.0, referenceHitRate, hitRateDeviation, hitRateDeviation <= bandwidth.HitRate,
                    cumulativeGold, expectedGold, goldDeviation, goldDeviation <= bandwidth.Gold));

                if (currentLevel == level)
                {
                    // 提前终止分支（见上方 goto 判断记录）：本级未真实升级就退出了内层循环，不再继续
                    // 尝试后续等级——世界状态已经不可信（玩家可能已经死亡）。
                    break;
                }
            }

            var xpViaEvents = world.Events.OfType<XpGainedEvent>().Where(e => e.UnitId.Equals(PlayerId)).Sum(e => (double)e.Amount);
            var goldEventsSum = goldCurrencyId.HasValue
                ? world.Events.OfType<CurrencyChangedEvent>()
                    .Where(e => e.UnitId.Equals(PlayerId) && e.CurrencyId.Equals(goldCurrencyId.Value))
                    .Sum(e => (double)(e.NewValue - e.OldValue))
                : 0.0;
            var goldFinalBalance = goldCurrencyId.HasValue ? (double)economy.GetBalance(PlayerId, goldCurrencyId.Value) : 0.0;

            return new GrowthRunResult(levels, cumulativeXpViaApi, xpViaEvents, goldFinalBalance, goldEventsSum);
        }

        private static void PickUpAllGroundLoot(HeadlessWorld world, Id playerId, Id mapId)
        {
            var lootEntities = world.World.QueryEntities(new EntityFilter(kind: EntityKinds.Loot, mapId: mapId));
            foreach (var entity in lootEntities)
            {
                world.Gameplay.Loot.PickUp(playerId, entity.EntityId);
            }
        }

        /// <summary>逐件处理刚拾取到手的新物品实例——按槽位评分对比当前已装备物品，评分更高则换装
        /// （<c>EquipmentHost.Equip</c> 自动把旧物品放回背包并带出，再卖掉旧物品），否则直接卖掉新
        /// 拿到的这件，见类型判断记录"换装决策"。
        /// <para>判断记录（如何识别"新增的实例"，不需要额外的前后差集）：本方法在每次
        /// <see cref="PickUpAllGroundLoot"/> 之后立即调用一次，且自身逢非升级物品必卖、逢升级物品
        /// 必换装（换下来的旧物品同样立即卖出）——两条分支都会把处理过的物品从背包移除（<c>Equip</c>
        /// 内部 <c>TryTakeWhole</c>/<c>Sell</c> 内部 <c>RemoveItem</c>），因此"背包里尚未被装备槽
        /// 占用的物品"在本方法返回后天然清空，下一次调用时背包里的"未装备物品"必然全部是这次新拾取
        /// 到的——不需要调用方额外做一次 <c>ListItems</c> 前后差集。</para></summary>
        private static void ProcessAcquiredItems(
            IDataRegistryView registry, IBudgetSolver solver,
            Core.Carriers.Item.InventoryHost inventory, Core.Carriers.Item.EquipmentHost equipment,
            Core.Gameplay.Economy.EconomyHost economy,
            Id classId, Id? vendorId, Id? goldCurrencyId, IReadOnlyList<Id> equipmentSlots,
            IReadOnlyDictionary<Id, ItemBudgetCurve.StatBudgetInfo> statBudgetInfo)
        {
            var items = inventory.ListItems(PlayerId);
            var equippedInstanceIds = new HashSet<Id>();
            foreach (var slot in equipmentSlots)
            {
                var equippedRef = equipment.GetEquipped(PlayerId, slot);
                if (equippedRef.HasValue)
                {
                    equippedInstanceIds.Add(equippedRef.Value.InstanceId);
                }
            }

            foreach (var item in items.ToList())
            {
                if (equippedInstanceIds.Contains(item.InstanceId))
                {
                    continue;
                }
                if (item.TemplateId.Domain == "econ")
                {
                    continue;
                }

                var template = registry.Get("item.template", item.TemplateId);
                if (template == null || !template.TryGetId("slot", out var slotId) || !equipmentSlots.Contains(slotId))
                {
                    // 非装备类物品（本数据集掉落表不产出，防御性跳过，不阻断成长仿真本身）。
                    continue;
                }

                var itemLevel = (int)template.GetInt("item_level");
                var newScore = ScoreCandidate(registry, solver, classId, item.TemplateId, item.Quality, item.Affixes, itemLevel, slotId, statBudgetInfo);

                var currentRef = equipment.GetEquipped(PlayerId, slotId);
                double currentScore = double.NegativeInfinity;
                if (currentRef.HasValue)
                {
                    var currentInstance = inventory.FindInstance(PlayerId, currentRef.Value.InstanceId);
                    if (currentInstance.HasValue)
                    {
                        var currentTemplate = registry.Get("item.template", currentInstance.Value.TemplateId);
                        var currentItemLevel = currentTemplate != null && currentTemplate.TryGetInt("item_level", out var lvl) ? (int)lvl : 1;
                        currentScore = ScoreCandidate(
                            registry, solver, classId, currentInstance.Value.TemplateId,
                            currentInstance.Value.Quality, currentInstance.Value.Affixes, currentItemLevel, slotId, statBudgetInfo);
                    }
                }

                if (newScore > currentScore)
                {
                    var result = equipment.Equip(PlayerId, item.InstanceId, slotId);
                    if (result.Success && result.Replaced.HasValue && vendorId.HasValue)
                    {
                        economy.Sell(PlayerId, vendorId.Value, result.Replaced.Value.InstanceId, 1);
                    }
                }
                else if (vendorId.HasValue)
                {
                    economy.Sell(PlayerId, vendorId.Value, item.InstanceId, 1);
                }
            }
        }

        private static double ScoreCandidate(
            IDataRegistryView registry, IBudgetSolver solver, Id classId,
            Id templateId, Id qualityId, IReadOnlyList<Id> affixIds, int itemLevel, Id slotId,
            IReadOnlyDictionary<Id, ItemBudgetCurve.StatBudgetInfo> statBudgetInfo)
        {
            var budgetCurveId = StandardPlayerBuilder.DefaultBudgetCurveId;
            var affixContribution = StandardPlayerBuilder.ComputeAffixContribution(
                registry, solver, budgetCurveId, itemLevel, qualityId, slotId, affixIds, statBudgetInfo);
            var additionalStats = affixContribution.Select(kv => (kv.Key, kv.Value)).ToList();
            var result = EquipmentScoreAnalyzer.Score(templateId, classId, registry, statBudgetInfo, additionalStats);
            return result.Score;
        }

        private static double ComputeAvgEquippedItemLevel(
            IDataRegistryView registry, Core.Carriers.Item.EquipmentHost equipment, Id playerId, IReadOnlyList<Id> equipmentSlots)
        {
            var equipped = equipment.GetAllEquippedInstances(playerId);
            var levels = new List<double>();
            foreach (var slot in equipmentSlots)
            {
                if (equipped.TryGetValue(slot, out var instance))
                {
                    var template = registry.Get("item.template", instance.TemplateId);
                    if (template != null && template.TryGetInt("item_level", out var lvl))
                    {
                        levels.Add(lvl);
                    }
                }
            }
            return levels.Count > 0 ? levels.Average() : 0.0;
        }

        /// <summary>命中率对照基准：同等级、偏移 0 的若干场独立战斗均值（见类型判断记录"四条轨迹的
        /// 对照基准"）。种子刻意与成长轨迹本身的种子空间不重叠（<c>ArenaSimulation.DeriveSeed</c> 的
        /// <c>offset</c> 参数借用一个成长轨迹不会用到的极大值区隔）。判断记录（取 5 场均值，不是单
        /// 一场）：命中率本身来自命中判定的伯努利抽样，单场的样本量（一场战斗里的攻击次数）不大时
        /// 方差不小——完整场景联调测试实测单场取样在个别等级（如 L15）撞到偏低的一次，与
        /// <c>ArenaCellResult.PlayerHitRateMean</c> 取 <c>runs</c> 场均值同一做法，缩小这类偶然
        /// 抖动，不改变本对照基准"真实同级战斗实测"的性质。</summary>
        private static double ResolveReferenceHitRate(
            ScenarioDef scenario, IReadOnlyList<IDataSource> dataSources, int level, bool failOnUnknownTable)
        {
            const int referenceRuns = 5;
            var creatureId = scenario.Opponent.CreatureId;
            var hitRates = new List<double>(referenceRuns);
            for (var runIndex = 0; runIndex < referenceRuns; runIndex++)
            {
                var seed = ArenaSimulation.DeriveSeed(scenario.BaseSeed, level, 999_000, runIndex);
                var result = FightRunner.Run(new FightRunnerOptions
                {
                    DataSources = dataSources,
                    ClassId = scenario.Player.ClassId,
                    PlayerLevel = level,
                    QualityId = scenario.Player.QualityId!.Value,
                    CreatureId = creatureId,
                    CreatureLevel = level,
                    Seed = seed,
                    MaxTicks = scenario.MaxTicks,
                    FailOnUnknownTable = failOnUnknownTable,
                });
                hitRates.Add(result.PlayerHitRate);
            }
            return hitRates.Average();
        }

        /// <summary>4.8 节公式的闭式期望累计——"击杀 + 任务当量"两类当量收入按公式解析计算，另加一项
        /// "出售掉落装备"的期望收入估计（见类型判断记录"四条轨迹的对照基准"追记：初版只算前两类，
        /// 实测发现出售收入在中高等级远非可忽略的小尾巴——本数据集掉落表命中率不低
        /// （主手/头/胸/腿/脚合计期望每次击杀掉落 ≈1.25 件）、<c>econ.value_curve</c> 从 1 级到 20 级
        /// 增长 11 倍，两者相乘后出售收入随等级增长的速度远超击杀+任务收入，若不计入会让"期望"曲线
        /// 系统性偏低、偏离随等级单调扩大到远超带宽——因此这里按同一份真实数据补上估计：对每一级的
        /// 生物家族当前档位 <c>loot_table_ref</c>，逐条非货币条目取 <c>weight_or_chance</c>（掉落
        /// 概率）× <see cref="EconomyPriceFormula.TryComputeBaseValue"/>（真实基准价值公式，读同一份
        /// <c>econ.value_curve</c>/<c>item.quality_definition.price_multiplier</c>/<c>item.slot_definition
        /// .price_coefficient</c>）× <c>sellRatio</c>（<see cref="EconomyOptions.DefaultBuyPricePct"/>
        /// 的默认值 0.25——本类型装配世界时从未覆盖这个选项，见 <see cref="RunOnce"/>，因此这里按同一
        /// 默认值折算是"真实生效值"而不是猜测），近似"这次掉落无论是直接卖掉还是换装后卖掉旧的，
        /// 都会产生一笔价值相近的出售交易"（简化，见判断记录——不区分"卖新的"还是"卖旧的换下来的"，
        /// 两者物品等级都紧贴 <c>expected_item_level(L)</c>，价值量级相近）。</summary>
        private static double ExpectedCumulativeGoldAt(
            IDataRegistryView registry, Core.Gameplay.Economy.EconomyHost economy, AnchorTable anchors,
            IReadOnlyList<(int Level, Id TemplateId)> creatureFamily, int levelFrom, int uptoLevelInclusive)
        {
            const double sellRatio = 0.25; // Core.Gameplay.Economy.EconomyOptions.DefaultBuyPricePct 默认值。
            var valueCurveId = new Id("econ.value.default"); // Core.Gameplay.Economy.EconomyOptions.ValueCurveId 默认值。

            double total = 0.0;
            for (var l = levelFrom; l <= uptoLevelInclusive; l++)
            {
                var anchor = anchors.TryGet(l, out var row) ? row : anchors.Get(anchors.MaxLevel);
                if (anchor.QuestShare >= 1.0) continue;
                var monsterEquivalent = anchor.LevelDurationSeconds / (anchor.TtkSeconds + anchor.KillIntervalSeconds);
                var goldBase = economy.TryGetGoldBaseAmount(l) ?? 0.0;
                // 与 RunOnce 内"任务当量取 Q(L) 本身"同一口径（见该处判断记录）：每次击杀 goldBase(l)
                // 金币，外加 goldBase(l)×Q(l) 的任务当量金币，monsterEquivalent(l) 次击杀总计
                // monsterEquivalent(l)×goldBase(l)×(1+Q(l))。
                var killAndQuestGold = monsterEquivalent * goldBase * (1.0 + anchor.QuestShare);

                var creatureTemplateId = ResolveCreatureTemplateForLevel(creatureFamily, l);
                var expectedSellPerKill = ExpectedSellIncomePerKill(registry, creatureTemplateId, valueCurveId, sellRatio);

                total += killAndQuestGold + monsterEquivalent * expectedSellPerKill;
            }
            return total;
        }

        /// <summary>期望出售收入（每次击杀一次）：<paramref name="creatureTemplateId"/>.
        /// <c>loot_table_ref</c> 里全部非货币条目 <c>weight_or_chance × 基准价值 × sellRatio</c> 之和。</summary>
        private static double ExpectedSellIncomePerKill(
            IDataRegistryView registry, Id creatureTemplateId, Id valueCurveId, double sellRatio)
        {
            var creatureRecord = registry.Get("creature.template", creatureTemplateId);
            if (creatureRecord == null || !creatureRecord.TryGetId("loot_table_ref", out var lootTableId))
            {
                return 0.0;
            }
            var lootTableRecord = registry.Get("loot.table", lootTableId);
            if (lootTableRecord == null || !lootTableRecord.TryGetArray("groups", out var groups))
            {
                return 0.0;
            }

            double total = 0.0;
            foreach (var groupRaw in groups)
            {
                if (groupRaw is not JsonObject groupObj || !groupObj.TryGetValue("entries", out var entriesRaw) ||
                    entriesRaw is not JsonArray entries)
                {
                    continue;
                }

                foreach (var entryRaw in entries)
                {
                    if (entryRaw is not JsonObject entryObj) continue;
                    if (!entryObj.TryGetValue("ref", out var refRaw) || refRaw is not JsonString refStr) continue;

                    var templateId = new Id(refStr.Value);
                    if (templateId.Domain == "econ") continue; // 货币条目已由击杀/任务当量公式计入。

                    var chance = entryObj.TryGetValue("weight_or_chance", out var chanceRaw) && chanceRaw is JsonNumber chanceNum
                        ? chanceNum.Value
                        : 0.0;
                    if (chance <= 0) continue;

                    var itemTemplateRecord = registry.Get("item.template", templateId);
                    if (itemTemplateRecord == null) continue;

                    var baseValue = EconomyPriceFormula.TryComputeBaseValue(registry, itemTemplateRecord, valueCurveId) ?? 0.0;
                    total += chance * baseValue * sellRatio;
                }
            }
            return total;
        }

        private static double RelativeDeviation(double expected, double actual)
        {
            if (double.IsNaN(actual) || double.IsInfinity(actual)) return double.PositiveInfinity;
            if (expected == 0) return actual == 0 ? 0.0 : double.PositiveInfinity;
            return Math.Abs(expected - actual) / Math.Abs(expected);
        }

        private readonly struct Bandwidths
        {
            public double Duration { get; }
            public double ItemLevel { get; }
            public double HitRate { get; }
            public double Gold { get; }

            public Bandwidths(double duration, double itemLevel, double hitRate, double gold)
            {
                Duration = duration;
                ItemLevel = itemLevel;
                HitRate = hitRate;
                Gold = gold;
            }
        }

        private static Bandwidths ResolveBandwidths(ScenarioDef scenario)
        {
            double Get(string key, double fallback) => scenario.Bandwidths.TryGetValue(key, out var v) ? v : fallback;
            return new Bandwidths(
                Get("level_duration", 0.35), Get("item_level", 0.35), Get("hit_rate", 0.35), Get("gold", 0.35));
        }

        /// <summary>按 <c>kind</c>（<c>"kill"</c>/<c>"quest"</c>/<c>"discovery"</c>）在 <c>prog
        /// .xp_source</c> 全表里找第一条匹配的来源 id；找不到返回 <c>null</c>（调用方按"该来源本
        /// 数据集未提供"处理，不强行发放）。</summary>
        private static Id? ResolveXpSourceId(IDataRegistryView registry, string kind)
        {
            foreach (var record in registry.GetAll("prog.xp_source"))
            {
                if (record.TryGetString("kind", out var k) && string.Equals(k, kind, StringComparison.Ordinal))
                {
                    return record.Id!.Value;
                }
            }
            return null;
        }

        /// <summary>生物模板家族：按 <see cref="ScenarioOpponentSpec.TierId"/>（未提供时取
        /// <c>scenario.Opponent.CreatureId</c> 自身登记的 <c>tier</c>）筛出全部同分档的
        /// <c>creature.template</c>，按 <c>level</c> 升序排列——见类型判断记录"生物模板按等级换挡"。</summary>
        internal static List<(int Level, Id TemplateId)> ResolveCreatureFamily(IDataRegistryView registry, ScenarioOpponentSpec opponent)
        {
            Id? tierId = opponent.TierId;
            if (tierId == null)
            {
                var ownRecord = registry.Get("creature.template", opponent.CreatureId);
                if (ownRecord != null && ownRecord.TryGetId("tier", out var t))
                {
                    tierId = t;
                }
            }

            var family = new List<(int Level, Id TemplateId)>();
            foreach (var record in registry.GetAll("creature.template"))
            {
                if (tierId.HasValue && (!record.TryGetId("tier", out var recordTier) || !recordTier.Equals(tierId.Value)))
                {
                    continue;
                }
                var level = record.TryGetInt("level", out var lvl) ? (int)lvl : 1;
                family.Add((level, record.Id!.Value));
            }

            family.Sort((a, b) => a.Level.CompareTo(b.Level));
            if (family.Count == 0)
            {
                family.Add((1, opponent.CreatureId));
            }
            return family;
        }

        /// <summary>家族内"不超过当前等级的最大档位"，全部档位都高于当前等级时退回最低档位（不会
        /// 出现"没有任何可用模板"的情形，见 <see cref="ResolveCreatureFamily"/> 至少含一条兜底）。</summary>
        internal static Id ResolveCreatureTemplateForLevel(IReadOnlyList<(int Level, Id TemplateId)> family, int level)
        {
            Id? best = null;
            var bestLevel = int.MinValue;
            foreach (var (candidateLevel, templateId) in family)
            {
                if (candidateLevel <= level && candidateLevel > bestLevel)
                {
                    best = templateId;
                    bestLevel = candidateLevel;
                }
            }
            return best ?? family[0].TemplateId;
        }

        private static List<GrowthLevelSample> MergeLevelSamples(
            ScenarioDef scenario, List<List<GrowthLevelSample>> perRunSamples, int levelFrom, int levelTo)
        {
            var bandwidth = ResolveBandwidths(scenario);
            var merged = new List<GrowthLevelSample>();
            for (var level = levelFrom; level < levelTo; level++)
            {
                var samplesAtLevel = perRunSamples
                    .Select(run => run.FirstOrDefault(s => s.Level == level))
                    .Where(s => s != null)
                    .Select(s => s!)
                    .ToList();
                if (samplesAtLevel.Count == 0) continue;

                double Avg(Func<GrowthLevelSample, double> selector) => samplesAtLevel.Select(selector).Average();
                var kills = (int)Math.Round(samplesAtLevel.Select(s => (double)s.Kills).Average());

                var duration = Avg(s => s.ActualDurationSeconds);
                var expectedDuration = samplesAtLevel[0].ExpectedDurationSeconds;
                var durationDeviation = RelativeDeviation(expectedDuration, duration);

                var avgItemLevel = Avg(s => s.AvgItemLevel);
                var expectedItemLevel = samplesAtLevel[0].ExpectedItemLevel;
                var itemLevelDeviation = RelativeDeviation(expectedItemLevel, avgItemLevel);

                var hitRate = Avg(s => s.HitRate);
                var expectedHitRate = samplesAtLevel[0].ExpectedHitRate;
                var hitRateDeviation = RelativeDeviation(expectedHitRate, hitRate);

                var gold = Avg(s => s.CumulativeGold);
                var expectedGold = samplesAtLevel[0].ExpectedCumulativeGold;
                var goldDeviation = RelativeDeviation(expectedGold, gold);

                // 判断记录：带宽本身对全部种子相同（来自同一份场景数据），直接用场景自己的带宽阈值
                // 重新判定均值偏离是否达标——不使用任何从单个样本反推的近似值。
                var durationPass = durationDeviation <= bandwidth.Duration;
                var itemLevelPass = itemLevelDeviation <= bandwidth.ItemLevel;
                var hitRatePass = hitRateDeviation <= bandwidth.HitRate;
                var goldPass = goldDeviation <= bandwidth.Gold;

                merged.Add(new GrowthLevelSample(
                    level, kills,
                    duration, expectedDuration, durationDeviation, durationPass,
                    avgItemLevel, expectedItemLevel, itemLevelDeviation, itemLevelPass,
                    hitRate, expectedHitRate, hitRateDeviation, hitRatePass,
                    gold, expectedGold, goldDeviation, goldPass));
            }
            return merged;
        }
    }

    /// <summary>T-N6-5：<see cref="GrowthSimulation.RunOnce"/> 的返回类型——单个种子的逐等级样本 +
    /// 双路径互证的经验/金币总量。<c>internal</c>（不是 <see cref="GrowthReport"/> 那样的
    /// <c>public</c>）：只服务 <see cref="GrowthSimulation.Run"/> 内部跨种子聚合与测试对单个种子的
    /// 白盒检查，不是面向调用方的稳定契约面（调用方只应该消费 <see cref="GrowthReport"/>）。</summary>
    internal sealed class GrowthRunResult
    {
        public List<GrowthLevelSample> Levels { get; }
        public double CumulativeXpGrantedViaApi { get; }
        public double CumulativeXpGrantedViaEvents { get; }
        public double CumulativeGoldViaBalance { get; }
        public double CumulativeGoldViaEvents { get; }

        public GrowthRunResult(
            List<GrowthLevelSample> levels, double cumulativeXpGrantedViaApi, double cumulativeXpGrantedViaEvents,
            double cumulativeGoldViaBalance, double cumulativeGoldViaEvents)
        {
            Levels = levels;
            CumulativeXpGrantedViaApi = cumulativeXpGrantedViaApi;
            CumulativeXpGrantedViaEvents = cumulativeXpGrantedViaEvents;
            CumulativeGoldViaBalance = cumulativeGoldViaBalance;
            CumulativeGoldViaEvents = cumulativeGoldViaEvents;
        }
    }
}
