using System;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>消费方反馈第 53 条根治验收：<see cref="GrowthSimulation"/> 选同 <c>tier</c>+<c>level</c>
    /// 生物模板时按阵营过滤，排除与标准玩家同阵营/非敌对的候选，过滤后为空时明确报错，过滤后仍有多条
    /// 敌对候选时产出 <see cref="GrowthReport.OpponentAmbiguities"/> 诊断——见
    /// <c>core/sim/core/GrowthSimulation.cs</c> <c>ResolveCreatureFamily</c> 判断记录。
    /// <para>
    /// 判断记录（复现手法：为何不直接调用 internal 的 <c>ResolveCreatureFamily</c>）：<c>Core.Sim</c>
    /// 未对 <c>Tests.Sim</c> 声明 <c>InternalsVisibleTo</c>（勘察确认全仓无此接线），本测试类因此只能
    /// 经公开入口 <see cref="GrowthSimulation.Run"/> 端到端验证——用最小的 <c>level_from=1,level_to=2,
    /// runs=1</c> 场景（一级只需真正打赢一场就升级，验证成本低）+ 覆盖数据源叠一层自定义
    /// <c>creature.template</c>/<c>creature.tier_definition</c> 行，观测公开可见的结果
    /// （<c>report.Levels[0].Kills</c>/<c>report.OpponentAmbiguities</c>/抛出的异常），不直接白盒断言
    /// 内部选中了哪个模板 id。</para></summary>
    public class GrowthSimulationOpponentFactionFilterTests
    {
        /// <summary>组装一个"framework + 嵌入式仿真数据集 + 自定义覆盖"三源世界；覆盖源的
        /// <paramref name="overlayFirst"/> 控制覆盖源在 <c>DataSources</c> 列表中排在嵌入式数据集之前
        /// 还是之后——两个数据源为同一张表（<c>creature.template</c>）各自贡献不同 id 的行时，
        /// <c>registry.GetAll</c> 按数据源处理顺序累积，借此可以精确控制"哪一条候选先被扫描到"，
        /// 复现消费方反馈第 53 条原文"占位生物在文件内声明在前"的场景（不依赖任何不稳定的排序假设）。</summary>
        private static (Core.Sim.HeadlessWorld World, System.Collections.Generic.IReadOnlyList<IDataSource> DataSources) BuildWorldWithOverlay(
            string overlayCreatureTemplateJson, string? overlayTierDefinitionJson, string overlayScenarioJson, bool overlayFirst)
        {
            var fs = new StubFileSystem();
            fs.WriteTextAtomic("overlay/creature/creature.template.json", overlayCreatureTemplateJson);
            if (overlayTierDefinitionJson != null)
            {
                fs.WriteTextAtomic("overlay/creature/creature.tier_definition.json", overlayTierDefinitionJson);
            }
            fs.WriteTextAtomic("overlay/sim/sim.scenario.json", overlayScenarioJson);
            var overlaySource = new FileSystemDataSource(fs, "overlay");

            var frameworkAndEmbedded = SimTestWorldFactory.BuildEmbeddedDataSources();
            var allSources = overlayFirst
                ? new[] { overlaySource }.Concat(frameworkAndEmbedded).ToList()
                : frameworkAndEmbedded.Concat(new[] { overlaySource }).ToList();

            var world = Core.Sim.HeadlessWorldBuilder.Build(new Core.Sim.HeadlessWorldOptions
            {
                DataSources = allSources,
                Seed = 1,
                MapId = SimTestWorldFactory.EmbeddedMapId,
                PlayerId = SimTestWorldFactory.PlayerId,
                PlayerFactionId = SimTestWorldFactory.FactionPlayer,
                PlayerClassId = SimTestWorldFactory.EmbeddedClassId,
                GameId = SimTestWorldFactory.EmbeddedGameId,
                FailOnUnknownTable = false,
            });
            return (world, allSources);
        }

        /// <summary>复现 + 根治验收：同 tier（既有 <c>creature.tier.sim_normal</c>）+ 同 level（1）下，
        /// 一条"标准玩家同阵营"的占位生物（<c>faction_id=fac.player</c>）在数据源扫描顺序上排在真正的
        /// 敌对怪物（<c>creature.sim_wolf_l1</c>，<c>faction_id=fac.sim_hostile</c>）之前——改动前的
        /// 算法会因"取扫描顺序第一条"选中这条玩家阵营占位生物，导致战斗对象是非敌对生物，一级怎么打都
        /// 不会真正分出胜负、<c>Kills</c> 恒为 0（消费方反馈第 53 条原文症状）。根治后应正确过滤掉占位
        /// 生物，选中 <c>creature.sim_wolf_l1</c>，一级应能真实分出胜负（<c>Kills&gt;=1</c>），且这条
        /// 占位生物因为"非敌对"被直接排除、不计入 <see cref="GrowthReport.OpponentAmbiguities"/>
        /// （只有"多条敌对候选"才算歧义，见该属性判断记录）。</summary>
        [Fact]
        public void Run_PlayerFactionPlaceholderScannedFirst_ExcludedFromOpponentFamily_RealFightHappens()
        {
            const string overlayCreatureTemplateJson = @"{
  ""table"": ""creature.template"",
  ""schema_version"": 1,
  ""rows"": [
    {
      ""id"": ""creature.sim_growth_test_ally_placeholder"",
      ""name_key"": ""l10n.creature.sim_wolf_l1.name"",
      ""level"": 1,
      ""tier"": ""creature.tier.sim_normal"",
      ""base_stats"": { ""stat.stamina"": 999999, ""stat.strength"": 1 },
      ""faction_id"": ""fac.player"",
      ""display_ref"": ""display.map.sim_wolf_l1""
    }
  ]
}";
            const string overlayScenarioJson = @"{
  ""table"": ""sim.scenario"",
  ""schema_version"": 1,
  ""rows"": [
    {
      ""id"": ""sim.scenario.sim_growth_test_faction_filter"",
      ""kind"": ""growth"",
      ""player"": { ""class_id"": ""arch.class.sim_warrior"", ""level"": 1, ""quality_id"": ""item.quality.sim_common"" },
      ""opponent"": { ""creature_id"": ""creature.sim_wolf_l1"", ""tier_id"": ""creature.tier.sim_normal"" },
      ""level_from"": 1,
      ""level_to"": 2,
      ""runs"": 1,
      ""base_seed"": 770001,
      ""max_ticks"": 1200,
      ""bandwidths"": { ""level_duration"": 0.35, ""item_level"": 0.35, ""hit_rate"": 0.35, ""gold"": 0.35 }
    }
  ]
}";
            // overlayFirst=true：占位生物所在数据源排在嵌入式数据集（真正的 sim_wolf_l1）之前，
            // 复现反馈原文"占位生物文件内声明在前"这一扫描顺序。
            var (world, dataSources) = BuildWorldWithOverlay(
                overlayCreatureTemplateJson, overlayTierDefinitionJson: null, overlayScenarioJson, overlayFirst: true);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_growth_test_faction_filter"));

            var report = GrowthSimulation.Run(scenario, world.AnchorTable!, dataSources);

            Assert.True(report.Levels.Count > 0, "应至少产出一个等级样本");
            Assert.True(report.Levels[0].Kills >= 1,
                "根治后应选中真正敌对的 creature.sim_wolf_l1 作战对手，一级应能真实分出胜负；" +
                "Kills=0 说明又选中了非敌对的占位生物（回归）");
            Assert.Empty(report.OpponentAmbiguities); // 占位生物被阵营过滤直接排除，不计入"多条敌对候选"歧义。
        }

        /// <summary>根治新增诊断验收：同 tier+level 下存在两条都对玩家敌对的候选（数据配平疏漏本身，
        /// 不是"误把占位生物混进来"），根治后仍按"保持现有登记顺序首条"确定性选择，但应把这一歧义
        /// 通过 <see cref="GrowthReport.OpponentAmbiguities"/> 如实上报，入选者为登记顺序第一条。</summary>
        [Fact]
        public void Run_TwoHostileCandidatesShareTierAndLevel_ReportsAmbiguity_ChoosesFirstRegistered()
        {
            const string overlayTierDefinitionJson = @"{
  ""table"": ""creature.tier_definition"",
  ""schema_version"": 1,
  ""rows"": [
    {
      ""id"": ""creature.tier.sim_growth_test_ambiguous"",
      ""name_key"": ""l10n.creature.tier.sim_normal.name"",
      ""stat_multiplier"": 1.0,
      ""control_immune"": false,
      ""sort_weight"": 1,
      ""xp_multiplier"": 1.0
    }
  ]
}";
            const string overlayCreatureTemplateJson = @"{
  ""table"": ""creature.template"",
  ""schema_version"": 1,
  ""rows"": [
    {
      ""id"": ""creature.sim_growth_test_hostile_a"",
      ""name_key"": ""l10n.creature.sim_wolf_l1.name"",
      ""level"": 1,
      ""tier"": ""creature.tier.sim_growth_test_ambiguous"",
      ""base_stats"": { ""stat.stamina"": 119.4, ""stat.strength"": 12 },
      ""faction_id"": ""fac.sim_hostile"",
      ""ai_rotation_ref"": ""ai.rotation.sim_creature"",
      ""ai_behavior_ref"": ""ai.behavior_profile.sim_creature"",
      ""loot_table_ref"": ""loot.table.sim_wolf_l1"",
      ""display_ref"": ""display.map.sim_wolf_l1""
    },
    {
      ""id"": ""creature.sim_growth_test_hostile_b"",
      ""name_key"": ""l10n.creature.sim_wolf_l1.name"",
      ""level"": 1,
      ""tier"": ""creature.tier.sim_growth_test_ambiguous"",
      ""base_stats"": { ""stat.stamina"": 119.4, ""stat.strength"": 12 },
      ""faction_id"": ""fac.sim_hostile"",
      ""ai_rotation_ref"": ""ai.rotation.sim_creature"",
      ""ai_behavior_ref"": ""ai.behavior_profile.sim_creature"",
      ""loot_table_ref"": ""loot.table.sim_wolf_l1"",
      ""display_ref"": ""display.map.sim_wolf_l1""
    }
  ]
}";
            const string overlayScenarioJson = @"{
  ""table"": ""sim.scenario"",
  ""schema_version"": 1,
  ""rows"": [
    {
      ""id"": ""sim.scenario.sim_growth_test_ambiguous"",
      ""kind"": ""growth"",
      ""player"": { ""class_id"": ""arch.class.sim_warrior"", ""level"": 1, ""quality_id"": ""item.quality.sim_common"" },
      ""opponent"": { ""creature_id"": ""creature.sim_growth_test_hostile_a"", ""tier_id"": ""creature.tier.sim_growth_test_ambiguous"" },
      ""level_from"": 1,
      ""level_to"": 2,
      ""runs"": 1,
      ""base_seed"": 770002,
      ""max_ticks"": 1200,
      ""bandwidths"": { ""level_duration"": 0.35, ""item_level"": 0.35, ""hit_rate"": 0.35, ""gold"": 0.35 }
    }
  ]
}";
            var (world, dataSources) = BuildWorldWithOverlay(
                overlayCreatureTemplateJson, overlayTierDefinitionJson, overlayScenarioJson, overlayFirst: false);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_growth_test_ambiguous"));

            var report = GrowthSimulation.Run(scenario, world.AnchorTable!, dataSources);

            var ambiguity = Assert.Single(report.OpponentAmbiguities);
            Assert.Equal("creature.tier.sim_growth_test_ambiguous", ambiguity.TierId.Value);
            Assert.Equal(1, ambiguity.Level);
            Assert.Equal("creature.sim_growth_test_hostile_a", ambiguity.ChosenTemplateId.Value);
            Assert.Equal(new[] { "creature.sim_growth_test_hostile_b" }, ambiguity.DiscardedTemplateIds.Select(id => id.Value));

            // 序列化不破坏基线判断记录的另一半：非空歧义时应能在 JSON 里看到这个新键（结构性冒烟，
            // 不是逐字节基线比对——基线比对由三份仿真基线负责，本测试只证明"非空时确实写出"）。
            Assert.Contains("\"opponent_ambiguities\"", report.ToJson());
        }

        /// <summary>根治边界验收：同 tier 下全部候选都被阵营过滤排除（一个敌对生物都没有）——按硬性规则
        /// "运行时路径不静默降级"，应明确抛出 <see cref="InvalidOperationException"/>，不能悄悄回退到
        /// 选中一个非敌对生物继续跑。</summary>
        [Fact]
        public void Run_AllCandidatesFilteredOut_ThrowsInvalidOperationException()
        {
            const string overlayTierDefinitionJson = @"{
  ""table"": ""creature.tier_definition"",
  ""schema_version"": 1,
  ""rows"": [
    {
      ""id"": ""creature.tier.sim_growth_test_allally"",
      ""name_key"": ""l10n.creature.tier.sim_normal.name"",
      ""stat_multiplier"": 1.0,
      ""control_immune"": false,
      ""sort_weight"": 1,
      ""xp_multiplier"": 1.0
    }
  ]
}";
            const string overlayCreatureTemplateJson = @"{
  ""table"": ""creature.template"",
  ""schema_version"": 1,
  ""rows"": [
    {
      ""id"": ""creature.sim_growth_test_ally_only"",
      ""name_key"": ""l10n.creature.sim_wolf_l1.name"",
      ""level"": 1,
      ""tier"": ""creature.tier.sim_growth_test_allally"",
      ""base_stats"": { ""stat.stamina"": 119.4, ""stat.strength"": 12 },
      ""faction_id"": ""fac.player"",
      ""display_ref"": ""display.map.sim_wolf_l1""
    }
  ]
}";
            const string overlayScenarioJson = @"{
  ""table"": ""sim.scenario"",
  ""schema_version"": 1,
  ""rows"": [
    {
      ""id"": ""sim.scenario.sim_growth_test_allally"",
      ""kind"": ""growth"",
      ""player"": { ""class_id"": ""arch.class.sim_warrior"", ""level"": 1, ""quality_id"": ""item.quality.sim_common"" },
      ""opponent"": { ""creature_id"": ""creature.sim_growth_test_ally_only"", ""tier_id"": ""creature.tier.sim_growth_test_allally"" },
      ""level_from"": 1,
      ""level_to"": 2,
      ""runs"": 1,
      ""base_seed"": 770003,
      ""max_ticks"": 1200,
      ""bandwidths"": { ""level_duration"": 0.35, ""item_level"": 0.35, ""hit_rate"": 0.35, ""gold"": 0.35 }
    }
  ]
}";
            var (world, dataSources) = BuildWorldWithOverlay(
                overlayCreatureTemplateJson, overlayTierDefinitionJson, overlayScenarioJson, overlayFirst: false);
            var scenario = world.ScenarioCatalog!.Get(new Id("sim.scenario.sim_growth_test_allally"));

            var ex = Assert.Throws<InvalidOperationException>(
                () => GrowthSimulation.Run(scenario, world.AnchorTable!, dataSources));
            Assert.Contains("creature.tier.sim_growth_test_allally", ex.Message);
        }
    }
}
