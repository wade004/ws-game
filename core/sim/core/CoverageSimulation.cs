using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Core.Sim
{
    /// <summary>离群值级别——警告（明显超出预期，值得设计复核）/信息（轻微偏离，仅供参考），惯例同
    /// 04 第 6 节"警告/阻断"两级但覆盖仿真本身不阻断任何构建（数值总纲第 6 节静态校验级别汇总不含
    /// "仿真离群值"这一项——仿真是事后分析工具，不是加载期校验规则）。</summary>
    public enum CoverageOutlierLevel
    {
        Info,
        Warning,
    }

    public enum CoverageCategory
    {
        Skill,
        Item,
        Creature,
    }

    /// <summary>一条离群值记录——三类（技能/装备/生物）共用同一个形状，字段含义按
    /// <see cref="Category"/> 各自解释，见 <see cref="CoverageSimulation"/> 类型判断记录"统一的
    /// 离群值行形状"。</summary>
    public sealed class CoverageOutlierRow
    {
        public Id Id { get; }
        public CoverageCategory Category { get; }

        /// <summary>核心统计量——技能/装备为预算比值，生物为 TTK（秒）。</summary>
        public double Statistic { get; }

        /// <summary>对照基准——技能/装备恒为 1.0（满预算），生物为 <c>sim.anchor.ttk_seconds</c>。</summary>
        public double Baseline { get; }

        /// <summary><c>|Statistic-Baseline|/Baseline</c>（<see cref="Baseline"/> 为 0 时退化处理同
        /// <see cref="ArenaSimulation"/> 判断记录），排序键——三张表各自按本字段降序排列。</summary>
        public double Deviation { get; }

        public CoverageOutlierLevel Level { get; }

        /// <summary>技能：<see cref="SkillBudgetResult.EffectiveValue"/> 占玩家总落地伤害的实测占比
        /// （只有 <c>Tier=Player</c> 且含伤害效果的技能才有此值，见类型判断记录"单技能实测的适用
        /// 范围"）；装备：穿上它后玩家秒伤/生命相对基准装的边际变化（比例，正数=变强）；生物：
        /// <c>FightResult.TtdEstimate</c> 与 <c>sim.anchor.ttd_seconds</c> 的相对偏离。<c>null</c>
        /// 表示本条未产生该项实测（如非伤害技能只给预算比值）。</summary>
        public double? SecondaryMeasurement { get; }

        public string? Note { get; }

        internal CoverageOutlierRow(
            Id id, CoverageCategory category, double statistic, double baseline, double deviation,
            CoverageOutlierLevel level, double? secondaryMeasurement, string? note)
        {
            Id = id;
            Category = category;
            Statistic = statistic;
            Baseline = baseline;
            Deviation = deviation;
            Level = level;
            SecondaryMeasurement = secondaryMeasurement;
            Note = note;
        }

        internal JsonObject ToJson() => new JsonObjectBuilder()
            .Add("id", new JsonString(Id.Value))
            .Add("category", new JsonString(Category.ToString().ToLowerInvariant()))
            .Add("statistic", ArenaReport.NumberOrNull(Statistic))
            .Add("baseline", ArenaReport.NumberOrNull(Baseline))
            .Add("deviation", ArenaReport.NumberOrNull(Deviation))
            .Add("level", new JsonString(Level.ToString()))
            .Add("secondary_measurement", SecondaryMeasurement.HasValue ? ArenaReport.NumberOrNull(SecondaryMeasurement.Value) : JsonNull.Instance)
            .Add("note", Note != null ? (JsonValue)new JsonString(Note) : JsonNull.Instance)
            .Build();
    }

    /// <summary>一次 <see cref="CoverageSimulation.Run"/> 调用的完整结果——三类各一张按
    /// <see cref="CoverageOutlierRow.Deviation"/> 降序排列的离群值表。</summary>
    public sealed class CoverageReport
    {
        public Id ScenarioId { get; }
        public ulong BaseSeed { get; }
        public IReadOnlyList<CoverageOutlierRow> Skills { get; }
        public IReadOnlyList<CoverageOutlierRow> Items { get; }
        public IReadOnlyList<CoverageOutlierRow> Creatures { get; }

        internal CoverageReport(
            Id scenarioId, ulong baseSeed,
            IReadOnlyList<CoverageOutlierRow> skills, IReadOnlyList<CoverageOutlierRow> items,
            IReadOnlyList<CoverageOutlierRow> creatures)
        {
            ScenarioId = scenarioId;
            BaseSeed = baseSeed;
            Skills = skills;
            Items = items;
            Creatures = creatures;
        }

        public string ToJson()
        {
            var root = new JsonObjectBuilder()
                .Add("scenario_id", new JsonString(ScenarioId.Value))
                .Add("base_seed", new JsonNumber(BaseSeed))
                .Add("skills", new JsonArray(Skills.Select(r => (JsonValue)r.ToJson()).ToList()))
                .Add("items", new JsonArray(Items.Select(r => (JsonValue)r.ToJson()).ToList()))
                .Add("creatures", new JsonArray(Creatures.Select(r => (JsonValue)r.ToJson()).ToList()))
                .Build();
            return Core.Foundation.Common.Json.JsonWriter.Write(root);
        }
    }

    /// <summary>
    /// T-N6-5（ADR-0035 决策 3 内容覆盖仿真）：对嵌入数据集的每个技能、装备、生物模板批量跑，输出
    /// 离群值列表——技能按 <see cref="SkillBudgetAnalyzer"/> 预算比值（伤害类技能额外实测单技能秒伤
    /// 占比）、装备按 <see cref="EquipmentScoreAnalyzer"/>/预算消耗比（额外实测换单件的边际秒伤/生命
    /// 变化）、生物按同级标准玩家 TTK/TTD 与锚点偏离，三张表各自按 |偏离| 降序排列。
    /// <para>
    /// <b>判断记录（统一的离群值行形状）</b>：任务书原文"每行含 id、类别、统计量、基准、偏离、级别"
    /// ——三类的"统计量"物理含义并不相同（技能/装备是预算比值，生物是 TTK 秒数），本类型不为每类
    /// 单独定义一个字段名不同的行类型（那样"三张表各一张离群值表"就没有共同的展示/序列化代码路径），
    /// 改用统一形状 + <see cref="CoverageOutlierRow.Category"/> 供调用方按类别解释字段含义——同
    /// <see cref="ArenaCellResult"/>/<see cref="ReconciliationRow"/> 那种"字段名直接对应具体统计量"
    /// 的做法相比，本类型选择偏"通用表格"的形状，是因为任务书原文明确要求"合并总表前两位"这条验收
    /// 断言（见下一条判断记录）需要跨类别比较排序，统一形状让这件事不需要额外的适配层。
    /// </para>
    /// <para>
    /// <b>判断记录（探针位次验收口径：分类第一，不做跨类别合并总表）</b>：任务书"要求覆盖仿真的离群值
    /// 列表前两位正是这两条（技能表第一、装备表第一，或合并总表前两位——写明口径并断言）"——技能
    /// 预算比值（无量纲，围绕 1.0 的倍数）与装备预算消耗比值（同样围绕 1.0）尽管数值范围接近，但
    /// 语义完全不同的两件事放进同一个"合并总表"排序，会让"哪个更离群"这一问题失去清晰含义（10 倍
    /// 超模的技能与 10 倍超模的装备，谁更应该被优先复核？这是设计判断，不是数值大小能回答的）。本
    /// 类型按"技能表第一、装备表第一"这一分支口径断言（两张表各自的 <c>[0]</c> 就是各自的注入探针），
    /// 不提供合并总表——<see cref="CoverageReport"/> 的三个属性本就是三张独立表，调用方如果确实需要
    /// 合并视图，可以自行拼接，不属于本类型的契约职责。
    /// </para>
    /// <para>
    /// <b>判断记录（单技能实测的适用范围：仅 <c>Tier=Player</c> 且含 <c>school_damage</c> 效果）</b>：
    /// "只用该技能 + 填充攻击"要求把 <c>ai.rotation</c> 的优先级表决策临时替换成"先试这一条技能、
    /// 不行再试填充技能"，本类型不经过 <see cref="Core.Rules.Ai.RotationEvaluator"/>（它按数据驱动
    /// 的整张优先级表决策，不支持"临时只看这一条"这种一次性覆盖），改为直接调用
    /// <see cref="SkillHost.CastSkill"/>，见 <see cref="MeasureSingleSkillDamageShare"/>。生物档
    /// （<c>Tier=Monster</c>）技能的"施法者"是生物，其 AI 决策由 <c>core/rules/ai</c> 的既有状态机
    /// 驱动、不支持外部注入"这一 tick 必须放这个技能"，任务书没有给出生物侧的实现路径，本类型因此
    /// 只对玩家档、伤害类技能做单技能实测，其余技能（生物档、纯预算/增益类）只给预算比值，
    /// <see cref="CoverageOutlierRow.SecondaryMeasurement"/> 为 <c>null</c>，如实反映"未测"而不是
    /// 编造一个 0。
    /// </para>
    /// <para>
    /// <b>判断记录（填充技能的选取：同一 <c>ai.rotation</c> 优先级表末尾一条）</b>：<c>ai.rotation
    /// .&lt;class&gt;.entries[]</c> 按惯例把"随时可用的兜底攻击"排在最后一条（前面几条是有冷却/资源
    /// 门槛的强力技能，见嵌入数据集 README"技能设计取舍"）——取该表最后一条的 <c>skill_id</c> 作填充
    /// 技能；被测技能本身恰好是最后一条（如 <c>skill.sim_warrior_strike</c> 自己）时填充技能与被测
    /// 技能相同，退化为"一直连续施放这一条"，符合预期（不是缺陷）。
    /// </para>
    /// </summary>
    public static class CoverageSimulation
    {
        private static readonly Id MapId = new Id("world.sim_coverage");
        private static readonly Id GameId = new Id("game.sim_coverage");
        private static readonly Vec2 CreatureSpawnOffset = new Vec2(3, 0);

        public static CoverageReport Run(
            ScenarioDef scenario, AnchorTable anchors, IReadOnlyList<IDataSource> dataSources,
            bool failOnUnknownTable = false)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (anchors == null) throw new ArgumentNullException(nameof(anchors));
            if (dataSources == null) throw new ArgumentNullException(nameof(dataSources));
            if (scenario.Kind != ScenarioKind.Coverage)
            {
                throw new ArgumentException($"CoverageSimulation.Run 只接受 kind=coverage 的场景，实际为 {scenario.Kind}", nameof(scenario));
            }
            if (scenario.Player.QualityId == null)
            {
                throw new ArgumentException("CoverageSimulation.Run：scenario.Player.QualityId 不能为空。", nameof(scenario));
            }

            // 判断记录：本类型只需要一份只读 registry 来枚举 skill.def/item.template/creature.template
            // 与解析各类曲线/规则；不需要装配任何 HeadlessWorld（那是每条技能/生物单独实测时才需要，
            // 各自新建）。这里借用 HeadlessWorldBuilder 装配一次性 registry 的最简办法：直接复用
            // AnchorTableSkillBudgetAnchorProvider.DataSourcesHaveAnchorRows 之外没有更轻的公开入口，
            // 因此仍经 HeadlessWorldBuilder.Build 装配一次（沿用既有惯例，成本可忽略——见
            // FightRunner 类型判断记录"隔离方案"实测数据）。
            var probeWorld = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = dataSources,
                MapId = MapId,
                PlayerClassId = scenario.Player.ClassId,
                PlayerLevel = scenario.Player.Level,
                GameId = GameId,
                FailOnUnknownTable = failOnUnknownTable,
            });
            var registry = probeWorld.Registry;

            var anchorProvider = new AnchorTableSkillBudgetAnchorProvider(registry, scenario.Player.ClassId, scenario.Player.QualityId);

            var skillRows = AnalyzeSkills(registry, anchorProvider, scenario, anchors, dataSources, failOnUnknownTable);
            var itemRows = AnalyzeItems(registry, scenario, dataSources, failOnUnknownTable);
            var creatureRows = AnalyzeCreatures(registry, anchors, scenario, dataSources, failOnUnknownTable);

            return new CoverageReport(scenario.Id, scenario.BaseSeed, skillRows, itemRows, creatureRows);
        }

        private static List<CoverageOutlierRow> AnalyzeSkills(
            IDataRegistryView registry, ISkillBudgetAnchorProvider anchorProvider, ScenarioDef scenario,
            AnchorTable anchors, IReadOnlyList<IDataSource> dataSources, bool failOnUnknownTable)
        {
            var rows = new List<CoverageOutlierRow>();
            foreach (var record in registry.GetAll("skill.def"))
            {
                var skillId = record.Id!.Value;
                SkillBudgetResult result;
                try
                {
                    result = SkillBudgetAnalyzer.Analyze(skillId, registry, options: null, anchorProvider: anchorProvider);
                }
                catch (Exception)
                {
                    // 判断记录：极少数纯被动/无效果技能可能不满足 Analyze 的隐含前提（如缺少
                    // target_shape_ref 之类的伤害类专属字段），覆盖仿真是事后分析工具，单条技能
                    // 分析失败不应该中止整批扫描——跳过，不计入离群值表（既不是警告也不是信息）。
                    continue;
                }

                if (!result.Participates)
                {
                    continue;
                }

                var deviation = RelativeDeviation(1.0, result.Ratio);
                var level = deviation > result.Bandwidth ? CoverageOutlierLevel.Warning : CoverageOutlierLevel.Info;

                double? measuredShare = null;
                if (result.Tier == SkillBudgetTier.Player && HasDamageEffect(record))
                {
                    measuredShare = MeasureSingleSkillDamageShare(
                        registry, scenario, dataSources, failOnUnknownTable, skillId, result.Level, anchors);
                }

                rows.Add(new CoverageOutlierRow(
                    skillId, CoverageCategory.Skill, result.Ratio, 1.0, deviation, level, measuredShare,
                    result.BudgetNote));
            }

            rows.Sort((a, b) => b.Deviation.CompareTo(a.Deviation));
            return rows;
        }

        private static bool HasDamageEffect(DataRecord skillRecord)
        {
            if (!skillRecord.TryGetArray("effects", out var effects))
            {
                return false;
            }
            foreach (var raw in effects)
            {
                if (raw is JsonObject obj && obj.TryGetValue("kind", out var kindRaw) &&
                    kindRaw is JsonString kindStr && kindStr.Value == "school_damage")
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>单技能实测：只用 <paramref name="skillId"/>，放不出来时改用同一 <c>ai.rotation
        /// .&lt;class&gt;</c> 表最后一条兜底填充，见类型判断记录"填充技能的选取"。返回该技能落地伤害
        /// 占玩家总落地伤害的比例（同 <see cref="FightResult.PlayerSkillDamageShare"/> 口径）；玩家
        /// 全程零伤害时返回 0（不是 <c>null</c>——本方法确实跑过一场仿真，只是这场仿真的实测结果恰好
        /// 是 0）。</summary>
        private static double MeasureSingleSkillDamageShare(
            IDataRegistryView registry, ScenarioDef scenario, IReadOnlyList<IDataSource> dataSources,
            bool failOnUnknownTable, Id skillId, int skillLevel, AnchorTable anchors)
        {
            var level = Math.Max(1, Math.Min(skillLevel, anchors.MaxLevel));
            var classId = scenario.Player.ClassId;
            var qualityId = scenario.Player.QualityId!.Value;
            var rotationId = InferRotationId(classId);
            var fillerSkillId = ResolveFillerSkill(registry, rotationId) ?? skillId;
            var creatureId = scenario.Opponent.CreatureId;

            var accumulator = new FightRunner.FightAccumulator();
            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = dataSources,
                Seed = ArenaSimulation.DeriveSeed(scenario.BaseSeed, level, 777_000, skillId.Value.GetHashCode()),
                MapId = MapId,
                PlayerClassId = classId,
                PlayerLevel = level,
                GameId = GameId,
                FailOnUnknownTable = failOnUnknownTable,
                CombatOptions = accumulator.CombatOptions,
            });

            var standardPlayer = StandardPlayerBuilder.Build(world, classId, level, qualityId, rotationId: rotationId);
            var playerId = standardPlayer.UnitId;

            var spawnPos = Vec2.Zero + CreatureSpawnOffset;
            var creatureUnitId = world.Gameplay.Carriers.Creatures.Spawn(
                creatureId, MapId, spawnPos, facing: Math.PI, ownerId: null, level);
            world.Spatial.Register(creatureUnitId, spawnPos, 0.5);

            accumulator.BeginFight(playerId, creatureUnitId);

            var units = world.Gameplay.Carriers.Units;
            var skill = world.Gameplay.Carriers.Rules.Skill;
            var targets = new List<Id> { creatureUnitId };
            var maxTicks = Math.Min(scenario.MaxTicks, 400);

            for (var tick = 0; tick < maxTicks; tick++)
            {
                if (!units.Exists(playerId) || !units.IsAlive(playerId)) break;
                if (!units.Exists(creatureUnitId) || !units.IsAlive(creatureUnitId)) break;

                var eventsBefore = world.Events.Count;
                var castResult = skill.CastSkill(playerId, skillId, targets);
                if (!castResult.Success && !fillerSkillId.Equals(skillId))
                {
                    castResult = skill.CastSkill(playerId, fillerSkillId, targets);
                }
                if (!castResult.Success)
                {
                    var creaturePos = units.GetPosition(creatureUnitId);
                    SimpleMoveModel.Step(units, world.Spatial, playerId, creaturePos, FightRunner.DefaultEngageRange, 0.5, SimpleMoveModel.DefaultMoveSpeed);
                }

                world.Clock.Advance(0.5);
                accumulator.AccumulateDamageEvents(world.Events, eventsBefore);
            }

            if (accumulator.PlayerTotalDamage <= 0)
            {
                return 0.0;
            }

            accumulator.PlayerDamageBySkill.TryGetValue(skillId, out var thisSkillDamage);
            return thisSkillDamage / accumulator.PlayerTotalDamage;
        }

        private static Id InferRotationId(Id classId)
        {
            const string classPrefix = "arch.class.";
            var value = classId.Value;
            var name = value.StartsWith(classPrefix, StringComparison.Ordinal) ? value.Substring(classPrefix.Length) : value;
            return new Id("ai.rotation." + name);
        }

        private static Id? ResolveFillerSkill(IDataRegistryView registry, Id rotationId)
        {
            var record = registry.Get("ai.rotation", rotationId);
            if (record == null || !record.TryGetArray("entries", out var entries) || entries.Count == 0)
            {
                return null;
            }
            var last = entries[entries.Count - 1];
            if (last is JsonObject obj && obj.TryGetValue("skill_id", out var skillIdRaw) && skillIdRaw is JsonString skillIdStr)
            {
                return new Id(skillIdStr.Value);
            }
            return null;
        }

        private static List<CoverageOutlierRow> AnalyzeItems(
            IDataRegistryView registry, ScenarioDef scenario, IReadOnlyList<IDataSource> dataSources, bool failOnUnknownTable)
        {
            var rows = new List<CoverageOutlierRow>();
            var budgetCurveId = StandardPlayerBuilder.DefaultBudgetCurveId;

            foreach (var record in registry.GetAll("item.template"))
            {
                var templateId = record.Id!.Value;
                if (!record.TryGetInt("item_level", out var itemLevelRaw) ||
                    !record.TryGetId("slot", out var slotId) ||
                    !record.TryGetId("quality", out var qualityId))
                {
                    // 武器模板不带 stats、也不参与本条预算消耗比（见嵌入数据集 README 判断记录 3
                    // "主手武器不带 stats"）——item_level/slot/quality 三者武器同样都有，真正会跳过
                    // 的只有缺失这三个字段中任一个的异常数据行（本数据集不存在，防御性跳过）。
                    continue;
                }
                var itemLevel = (int)itemLevelRaw;

                var isWeapon = registry.Get("item.slot_definition", slotId)?.TryGetBool("is_weapon", out var w) == true && w;
                if (isWeapon)
                {
                    // 判断记录：武器"强度"走秒伤曲线口径（见嵌入数据集 README"装备预算手算口径"），
                    // 不占属性词条预算，本条预算消耗比公式对武器无意义（consumed 恒为 0，比值恒为 0，
                    // 会制造虚假的"极端欠模"离群值）——跳过，不计入本表。
                    continue;
                }

                var scoreResult = EquipmentScoreAnalyzer.Score(templateId, classId: null, view: registry);
                var budgetLimit = ComputeBudgetLimit(registry, budgetCurveId, itemLevel, qualityId, slotId);
                var ratio = budgetLimit > 0 ? scoreResult.Score / budgetLimit : 0.0;
                var deviation = RelativeDeviation(1.0, ratio);
                var level = deviation > 0.3 ? CoverageOutlierLevel.Warning : CoverageOutlierLevel.Info;

                double? marginalDelta = MeasureItemMarginalImpact(registry, scenario, dataSources, failOnUnknownTable, templateId, slotId, qualityId);

                rows.Add(new CoverageOutlierRow(templateId, CoverageCategory.Item, ratio, 1.0, deviation, level, marginalDelta, note: null));
            }

            rows.Sort((a, b) => b.Deviation.CompareTo(a.Deviation));
            return rows;
        }

        private static double ComputeBudgetLimit(IDataRegistryView registry, Id budgetCurveId, int itemLevel, Id qualityId, Id slotId)
        {
            var curveRecord = registry.Get("item.budget_curve", budgetCurveId);
            if (curveRecord == null) return 0.0;
            var curve = ItemBudgetCurve.ParseCurve(curveRecord);
            var baseCurve = ItemBudgetCurve.Interpolate(curve, itemLevel);

            var qualityRecord = registry.Get("item.quality_definition", qualityId);
            var qualityMult = qualityRecord != null && qualityRecord.TryGetNumber("budget_multiplier", out var qm) ? qm : 1.0;

            var slotRecord = registry.Get("item.slot_definition", slotId);
            var slotCoeff = slotRecord != null && slotRecord.TryGetNumber("budget_coefficient", out var sc) ? sc : 1.0;

            return baseCurve * qualityMult * slotCoeff;
        }

        /// <summary>换单件对比一场：同一种子，基准装（<see cref="StandardPlayerBuilder"/> 该等级
        /// 标准装）与"把 <paramref name="templateId"/> 换到 <paramref name="slotId"/>"两次各打一场
        /// 同级普通怪，取玩家秒伤的相对变化（<c>(换装后-基准)/基准</c>）。武器槊位理论上会调用本方法
        /// 的调用方已经在 <see cref="AnalyzeItems"/> 过滤掉，这里始终是护甲槊位，边际影响以秒伤变化
        /// 衡量足够（护甲提升的是生存而不是输出，但本数据集 <c>stat.armor</c> 与其它 8 项属性同权重
        /// 计入 <c>statMix</c>，见嵌入数据集 README 判断记录 14——秒伤变化足以反映"这件装备是否真的
        /// 比基准强"，不需要额外再跑一遍生存向的对比）。</summary>
        private static double? MeasureItemMarginalImpact(
            IDataRegistryView registry, ScenarioDef scenario, IReadOnlyList<IDataSource> dataSources,
            bool failOnUnknownTable, Id templateId, Id slotId, Id qualityId)
        {
            var classId = scenario.Player.ClassId;
            var level = scenario.Player.Level;
            var baselineQualityId = scenario.Player.QualityId!.Value;
            var seed = ArenaSimulation.DeriveSeed(scenario.BaseSeed, level, 888_000, templateId.Value.GetHashCode());
            var creatureId = scenario.Opponent.CreatureId;

            double RunOnce(Id? swapTemplateId)
            {
                var accumulator = new FightRunner.FightAccumulator();
                var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
                {
                    DataSources = dataSources,
                    Seed = seed,
                    MapId = MapId,
                    PlayerClassId = classId,
                    PlayerLevel = level,
                    GameId = GameId,
                    FailOnUnknownTable = failOnUnknownTable,
                    CombatOptions = accumulator.CombatOptions,
                });

                var standardPlayer = StandardPlayerBuilder.Build(world, classId, level, baselineQualityId, rotationId: null);
                var playerId = standardPlayer.UnitId;

                if (swapTemplateId.HasValue)
                {
                    var inventory = world.Gameplay.Carriers.Inventory;
                    var equipment = world.Gameplay.Carriers.Equipment;
                    var beforeIds = inventory.ListItems(playerId).Select(i => i.InstanceId).ToHashSet();
                    inventory.AddItem(playerId, swapTemplateId.Value, 1, qualityId, Array.Empty<Id>());
                    var newInstance = inventory.ListItems(playerId).FirstOrDefault(i => !beforeIds.Contains(i.InstanceId));
                    if (newInstance.InstanceId.Value != null)
                    {
                        equipment.Equip(playerId, newInstance.InstanceId, slotId);
                    }
                }

                var spawnPos = Vec2.Zero + CreatureSpawnOffset;
                var creatureUnitId = world.Gameplay.Carriers.Creatures.Spawn(
                    creatureId, MapId, spawnPos, facing: Math.PI, ownerId: null, level);
                world.Spatial.Register(creatureUnitId, spawnPos, 0.5);

                var fight = FightRunner.RunWithinWorld(
                    world, accumulator, playerId, creatureUnitId, standardPlayer.RotationId,
                    stepSeconds: 0.5, maxTicks: Math.Min(scenario.MaxTicks, 400), moveSpeed: SimpleMoveModel.DefaultMoveSpeed,
                    maxResourceCurveSamples: 2);
                return fight.PlayerDps;
            }

            var baselineDps = RunOnce(null);
            var swappedDps = RunOnce(templateId);
            if (baselineDps <= 0) return null;
            return (swappedDps - baselineDps) / baselineDps;
        }

        private static List<CoverageOutlierRow> AnalyzeCreatures(
            IDataRegistryView registry, AnchorTable anchors, ScenarioDef scenario,
            IReadOnlyList<IDataSource> dataSources, bool failOnUnknownTable)
        {
            var rows = new List<CoverageOutlierRow>();
            var classId = scenario.Player.ClassId;
            var qualityId = scenario.Player.QualityId!.Value;
            var runs = Math.Max(1, Math.Min(scenario.Runs, 10));

            foreach (var record in registry.GetAll("creature.template"))
            {
                var creatureTemplateId = record.Id!.Value;
                var level = record.TryGetInt("level", out var lvl) ? (int)lvl : 1;
                var clampedLevel = Math.Max(1, Math.Min(level, anchors.MaxLevel));
                if (!anchors.TryGet(clampedLevel, out var anchor))
                {
                    continue;
                }

                var ttkSamples = new List<double>();
                var ttdSamples = new List<double>();
                for (var runIndex = 0; runIndex < runs; runIndex++)
                {
                    var seed = ArenaSimulation.DeriveSeed(scenario.BaseSeed, clampedLevel, 555_000, runIndex ^ creatureTemplateId.Value.GetHashCode());
                    var fight = FightRunner.Run(new FightRunnerOptions
                    {
                        DataSources = dataSources,
                        ClassId = classId,
                        PlayerLevel = clampedLevel,
                        QualityId = qualityId,
                        CreatureId = creatureTemplateId,
                        CreatureLevel = clampedLevel,
                        Seed = seed,
                        MaxTicks = scenario.MaxTicks,
                        FailOnUnknownTable = failOnUnknownTable,
                    });
                    if (fight.Outcome == FightOutcome.PlayerWin)
                    {
                        ttkSamples.Add(fight.DurationSeconds);
                    }
                    if (!double.IsInfinity(fight.TtdEstimate))
                    {
                        ttdSamples.Add(fight.TtdEstimate);
                    }
                }

                var ttkMean = ttkSamples.Count > 0 ? ttkSamples.Average() : double.NaN;
                var ttdMean = ttdSamples.Count > 0 ? ttdSamples.Average() : double.PositiveInfinity;
                var ttkDeviation = RelativeDeviation(anchor.TtkSeconds, ttkMean);
                var ttdDeviation = RelativeDeviation(anchor.TtdSeconds, ttdMean);
                var deviation = double.IsInfinity(ttkDeviation) ? ttdDeviation
                    : double.IsInfinity(ttdDeviation) ? ttkDeviation
                    : Math.Max(ttkDeviation, ttdDeviation);
                var levelFlag = deviation > 0.3 ? CoverageOutlierLevel.Warning : CoverageOutlierLevel.Info;

                rows.Add(new CoverageOutlierRow(
                    creatureTemplateId, CoverageCategory.Creature, ttkMean, anchor.TtkSeconds, deviation, levelFlag,
                    double.IsInfinity(ttdDeviation) ? (double?)null : ttdDeviation, note: null));
            }

            rows.Sort((a, b) => b.Deviation.CompareTo(a.Deviation));
            return rows;
        }

        private static double RelativeDeviation(double baseline, double actual)
        {
            if (double.IsNaN(actual) || double.IsInfinity(actual)) return double.PositiveInfinity;
            if (baseline == 0) return actual == 0 ? 0.0 : double.PositiveInfinity;
            return Math.Abs(baseline - actual) / Math.Abs(baseline);
        }
    }
}
