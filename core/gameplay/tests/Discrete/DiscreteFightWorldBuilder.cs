using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Rules.Assembly;
using Core.Rules.Common;
using AppStateEnum = Core.Foundation.AppLifecycle.AppState;

namespace Tests.Gameplay.Discrete
{
    /// <summary>
    /// 离散时间模型（ADR-0013）"两单位战斗"测试世界组装：惯例同
    /// <c>core/rules/tests/Integration/FightWorldBuilder.cs</c>（连续模式版本），本类额外装配
    /// <see cref="Core.Foundation.SimLoop.TurnScheduler"/>/<see cref="Core.Gameplay.Assembly.TimeModelSwitch"/>/
    /// <see cref="IAppStateHost"/>，并把 <c>found.time_model.combat</c> 声明为 <c>discrete</c>
    /// （<c>initiative_stat</c> 策略，先攻属性 <c>stat.strength</c>）。命中表全部分支禁用（同
    /// <c>FightWorldBuilder</c>，伤害结算不含随机波动，保证确定性/回放测试可比较事件序列）。
    /// </summary>
    internal static class DiscreteFightWorldBuilder
    {
        public static readonly Id MapId = new Id("map.discrete_fight_test");
        public static readonly Id PlayerId = new Id("unit.d_player");
        public static readonly Id NpcId = new Id("unit.d_npc");

        public static readonly Id FactionPlayer = new Id("fac.d_player");
        public static readonly Id FactionWildlife = new Id("fac.d_wildlife");

        /// <summary>ADR-0013 补齐任务新增：与玩家/野生动物阵营都不互为敌对的中立阵营（
        /// <c>default_reaction: neutral</c>，未在 <see cref="ReactionMatrixJson"/> 显式登记任何
        /// 关系），供 <c>TimeModelSwitchParticipantTests</c> 验证
        /// <c>Core.Gameplay.Assembly.TimeModelSwitch.ResolveParticipants</c> 的阵营过滤——半径内的
        /// 中立旁观者不应被拉进战斗（见该方法判断记录）。仅新增一条未被任何既有测试引用的阵营
        /// 登记行，不影响本夹具其它既有用例。</summary>
        public static readonly Id FactionNeutral = new Id("fac.d_neutral");

        public static readonly Id ClassSample = new Id("arch.class.d_sample");
        public static readonly Id CurveSample = new Id("prog.curve.d_sample");

        public static readonly Id PowerHealth = WellKnownPowers.Health;

        public static readonly Id SkillStrike = new Id("skill.d_sample_strike");
        public static readonly Id SkillBite = new Id("skill.d_sample_bite");

        public static readonly Id TargetChainNearestEnemy = new Id("target.chain.d_nearest_enemy");

        public static readonly Id AiProfileSample = new Id("ai.profile.d_sample");
        public static readonly Id AiRotationSample = new Id("ai.rotation.d_sample");

        public static readonly Id StatStrength = new Id("stat.d_strength");

        public const double PlayerMaxHp = 150;
        public const double NpcStartHp = 60;

        public const int MaxDiscreteSteps = 400;

        private const string StatDefinitionJson = @"
        {
            ""table"": ""stat.definition"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""stat.d_strength"", ""name_key"": ""l10n.stat.d_strength.name"", ""group"": ""primary"" },
                { ""id"": ""stat.armor"", ""name_key"": ""l10n.stat.armor.name"", ""group"": ""secondary"", ""default_base"": 0 },
                { ""id"": ""stat.damage_done_pct"", ""name_key"": ""l10n.stat.damage_done_pct.name"", ""group"": ""secondary"", ""default_base"": 0 },
                { ""id"": ""stat.damage_taken_pct"", ""name_key"": ""l10n.stat.damage_taken_pct.name"", ""group"": ""secondary"", ""default_base"": 0 },
                { ""id"": ""stat.healing_done_pct"", ""name_key"": ""l10n.stat.healing_done_pct.name"", ""group"": ""secondary"", ""default_base"": 0 }
            ]
        }";

        private static string PowerTypeJson => @"
        {
            ""table"": ""arch.power_type"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": """ + PowerHealth.Value + @""", ""name_key"": ""l10n.power.health.name"",
                  ""max_source"": {""kind"": ""fixed"", ""value"": " + PlayerMaxHp.ToString(CultureInfo.InvariantCulture) + @"},
                  ""regen_in_combat"": 0, ""regen_out_of_combat"": 0, ""decay_out_of_combat"": 0,
                  ""refill_on_leave_combat"": false, ""start_full"": true, ""allow_overflow"": false, ""min"": 0 }
            ]
        }";

        private static string ArchClassJson => @"
        {
            ""table"": ""arch.class"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": """ + ClassSample.Value + @""", ""name_key"": ""l10n.arch.class.d_sample.name"",
                  ""primary_stat"": ""stat.armor"",
                  ""base_stats"": { ""stat.armor"": 5 },
                  ""power_types"": [""" + PowerHealth.Value + @"""],
                  ""level_curve_ref"": """ + CurveSample.Value + @""" }
            ]
        }";

        private const string ProgLevelCurveJson = @"
        {
            ""table"": ""prog.level_curve"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""prog.curve.d_sample"", ""max_level"": 1,
                  ""entries"": [ { ""level"": 1, ""xp_to_next"": 0, ""growth"": {} } ] }
            ]
        }";

        private const string FactionJson = @"
        {
            ""table"": ""fac.faction"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""fac.d_player"", ""name_key"": ""l10n.fac.d_player.name"", ""default_reaction"": ""friendly"" },
                { ""id"": ""fac.d_wildlife"", ""name_key"": ""l10n.fac.d_wildlife.name"", ""default_reaction"": ""neutral"" },
                { ""id"": ""fac.d_neutral"", ""name_key"": ""l10n.fac.d_neutral.name"", ""default_reaction"": ""neutral"" }
            ]
        }";

        private const string ReactionMatrixJson = @"
        {
            ""table"": ""fac.reaction_matrix"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""fac.reaction_matrix.d_player_to_wildlife"", ""from"": ""fac.d_player"", ""to"": ""fac.d_wildlife"", ""reaction"": ""hostile"" },
                { ""id"": ""fac.reaction_matrix.d_wildlife_to_player"", ""from"": ""fac.d_wildlife"", ""to"": ""fac.d_player"", ""reaction"": ""hostile"" }
            ]
        }";

        // 命中表全部分支禁用：伤害结算不含随机波动（确定性/回放测试要求，见 FightWorldBuilder 同款惯例）。
        private const string HitTableJson = @"
        {
            ""table"": ""combat.hit_table_config"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""combat.hit_table.default"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0 }
            ]
        }";

        private const string ResistCurveJson = @"
        {
            ""table"": ""combat.resist_curve"",
            ""schema_version"": 1,
            ""rows"": []
        }";

        private const string TargetChainJson = @"
        {
            ""table"": ""target.chain_def"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""target.chain.d_nearest_enemy"", ""source"": ""nearest_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 },
                  ""filters"": [""relation:hostile"", ""alive""],
                  ""sort_by"": { ""key"": ""distance"", ""direction"": ""asc"" },
                  ""max_targets"": 1 }
            ]
        }";

        private const string SkillDefJson = @"
        {
            ""table"": ""skill.def"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""skill.d_sample_strike"", ""school"": ""school.physical"", ""kind"": ""active"",
                  ""range"": 10, ""cast_time"": 0, ""respects_gcd"": false,
                  ""target_shape_ref"": ""target.chain.d_nearest_enemy"",
                  ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 20, ""coefficient"": 0 } } ] },

                { ""id"": ""skill.d_sample_bite"", ""school"": ""school.physical"", ""kind"": ""active"",
                  ""range"": 10, ""cast_time"": 0, ""respects_gcd"": false,
                  ""target_shape_ref"": ""target.chain.d_nearest_enemy"",
                  ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 8, ""coefficient"": 0 } } ] }
            ]
        }";

        private const string AiRotationJson = @"
        {
            ""table"": ""ai.rotation"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""ai.rotation.d_sample"",
                  ""entries"": [ { ""priority"": 1, ""condition"": ""target.hp_pct > 0"", ""skill_id"": ""skill.d_sample_bite"" } ] }
            ]
        }";

        private const string AiBehaviorProfileJson = @"
        {
            ""table"": ""ai.behavior_profile"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""ai.profile.d_sample"", ""perception_radius"": 50, ""leash_range"": 50,
                  ""combat_return_policy"": ""stay"", ""rotation_ref"": ""ai.rotation.d_sample"", ""decision_interval"": 0.5 }
            ]
        }";

        // ADR-0013：exploration 连续，combat 离散（initiative_stat 策略，先攻属性 stat.d_strength，
        // seconds_per_turn=6，movement_budget_rule=distance——本夹具不使用移动，仅为满足 schema 必填）。
        private const string TimeModelJson = @"
        {
            ""table"": ""found.time_model"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""found.time_model.d_exploration"", ""scope"": ""exploration"", ""mode"": ""continuous"" },
                { ""id"": ""found.time_model.d_combat"", ""scope"": ""combat"", ""mode"": ""discrete"",
                  ""seconds_per_turn"": 6, ""initiative_policy"": ""initiative_stat"",
                  ""initiative_stat"": ""stat.d_strength"", ""movement_budget_rule"": ""distance"" }
            ]
        }";

        public static InMemoryDataSource BuildDataSource()
        {
            return new InMemoryDataSource()
                .Add("stat.definition", StatDefinitionJson)
                .Add("arch.power_type", PowerTypeJson)
                .Add("arch.class", ArchClassJson)
                .Add("prog.level_curve", ProgLevelCurveJson)
                .Add("fac.faction", FactionJson)
                .Add("fac.reaction_matrix", ReactionMatrixJson)
                .Add("combat.hit_table_config", HitTableJson)
                .Add("combat.resist_curve", ResistCurveJson)
                .Add("target.chain_def", TargetChainJson)
                .Add("skill.def", SkillDefJson)
                .Add("ai.rotation", AiRotationJson)
                .Add("ai.behavior_profile", AiBehaviorProfileJson)
                .Add("found.time_model", TimeModelJson);
        }

        public static IEventBus BuildEventBus(out List<IEvent> events)
        {
            var definitions = new List<EventDefinition>(EventKeys.All.Length);
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, Array.Empty<string>()));
            }

            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
            var collected = new List<IEvent>();
            events = collected;
            foreach (var key in EventKeys.All)
            {
                bus.Subscribe(key, e => collected.Add(e));
            }
            return bus;
        }

        public sealed class Fixture
        {
            public IEventBus Bus = null!;
            public IDataRegistry Registry = null!;
            public ValidationReport LoadReport = null!;
            public IRngHost Rng = null!;
            public WorldSim World = null!;
            public Core.Carriers.Unit.WorldUnitAccess Units = null!;
            public Adapters.Stub.StubSpatialQuery Spatial = null!;
            public SimClockHost Clock = null!;
            public RulesAssembly Rules = null!;
            public IAppStateHost AppState = null!;
            public Core.Foundation.SimLoop.TurnScheduler Scheduler = null!;
            public Core.Gameplay.Assembly.TimeModelSwitch TimeModelSwitch = null!;
            public List<IEvent> Events = null!;
            public SubStateId AwaitingInputSubState;
            public SubStateId PlayingBackSubState;

            /// <summary>推进一次连续 tick，并（若配置了脚本）提交玩家意图；用于战斗打响前的探索阶段。</summary>
            public void ContinuousTickWithPlayerCast(Id skillId)
            {
                World.SubmitIntent(new Intent(DiscreteFightWorldBuilder.PlayerId, "cast", CastArgs(skillId)));
                Clock.Advance(1.0);
            }

            /// <summary>驱动一个离散步：轮到玩家时提交 <paramref name="playerSkillId"/> 的施法意图，
            /// 轮到 AI 时不介入（AiTickHandler 的离散分支自动产生意图）。返回是否成功推进了一步
            /// （<c>false</c> 表示不在离散模式，或本轮已经打不动了——调用方应停止循环）。</summary>
            public bool StepDiscrete(Id playerSkillId)
            {
                if (TimeModelSwitch.CurrentMode != TimeModelMode.Discrete)
                {
                    return false;
                }

                var step = Scheduler.NextStep();
                if (step == null)
                {
                    Scheduler.SubmitIntent(DiscreteFightWorldBuilder.PlayerId, new Intent(DiscreteFightWorldBuilder.PlayerId, "cast", CastArgs(playerSkillId)));
                    step = Scheduler.NextStep();
                    if (step == null)
                    {
                        return false;
                    }
                }

                World.Tick(step.Value);
                Scheduler.NotifyStepConsumed(step.Value.ActorId!.Value);
                return true;
            }

            /// <summary>驱动整场战斗直到一方死亡或到达 <see cref="MaxDiscreteSteps"/>。</summary>
            public bool Run()
            {
                // 阶段一：连续模式下打出第一击，触发 combat.entered → TimeModelSwitch 切到离散模式。
                for (var i = 0; i < 20 && TimeModelSwitch.CurrentMode == TimeModelMode.Continuous; i++)
                {
                    ContinuousTickWithPlayerCast(DiscreteFightWorldBuilder.SkillStrike);
                }

                // 阶段二：离散模式逐步推进直到分出胜负或步数上限。
                for (var i = 0; i < MaxDiscreteSteps; i++)
                {
                    if (!Units.IsAlive(DiscreteFightWorldBuilder.PlayerId) || !Units.IsAlive(DiscreteFightWorldBuilder.NpcId))
                    {
                        return true;
                    }

                    if (!StepDiscrete(DiscreteFightWorldBuilder.SkillStrike))
                    {
                        break;
                    }
                }

                return !Units.IsAlive(DiscreteFightWorldBuilder.PlayerId) || !Units.IsAlive(DiscreteFightWorldBuilder.NpcId);
            }

            private static JsonObject CastArgs(Id skillId) =>
                new JsonObjectBuilder().Add("skill_id", new JsonString(skillId.Value)).Build();
        }

        public static Fixture Build(ulong seed = 20260905UL)
        {
            var bus = BuildEventBus(out var events);

            var registry = new DataRegistry(BuildDataSource(), bus, RulesSchemaCatalog.CreateOptions());
            RulesSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "DiscreteFightWorldBuilder 夹具数据未通过校验：" + string.Join("; ", report.Issues));
            }

            var rng = new RngHost(seed);
            var world = new WorldSim(bus);

            world.AddEntity(new Core.Carriers.Unit.PlayerUnit(PlayerId, MapId, FactionPlayer, ClassSample) { Position = new Vec2(0, 0) });
            world.AddEntity(new Core.Carriers.Unit.CreatureUnit(NpcId, MapId, FactionWildlife, ClassSample) { Position = new Vec2(1.0, 0) });

            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var spatial = new Adapters.Stub.StubSpatialQuery();
            spatial.Register(PlayerId, new Vec2(0, 0), 0.1);
            spatial.Register(NpcId, new Vec2(1.0, 0), 0.1);

            var rules = new RulesAssembly(bus, registry, rng, units, spatial, world);
            rules.RegisterUnit(PlayerId, ClassSample, raceId: null, level: 1);
            rules.RegisterUnit(NpcId, ClassSample, raceId: null, level: 1, aiProfileId: AiProfileSample, aiSpawnPoint: new Vec2(1.0, 0));

            rules.Powers.ModifyPower(NpcId, PowerHealth, NpcStartHp - PlayerMaxHp, sourceId: new Id("system.fixture_setup"));

            var appStateConfig = AppStateMachineConfig.Default();
            var awaitingInput = appStateConfig.AddCustomSubState("AwaitingInput");
            var playingBack = appStateConfig.AddCustomSubState("PlayingBack");
            appStateConfig.AllowSubTransition(SubStateId.Combat, awaitingInput);
            appStateConfig.AllowSubTransition(awaitingInput, SubStateId.Combat);
            appStateConfig.AllowSubTransition(SubStateId.Combat, playingBack);
            appStateConfig.AllowSubTransition(playingBack, SubStateId.Combat);

            var appState = new AppStateHost(bus, appStateConfig);
            appState.RequestTransition(AppStateEnum.MainMenu);
            appState.RequestTransition(AppStateEnum.Loading);
            appState.RequestTransition(AppStateEnum.InWorld);

            var clock = new SimClockHost(world, new SimLoopOptions { StepSeconds = 1.0, MaxCatchUpSteps = 2 });

            double InitiativeProvider(Id unitId) => rules.Stats.GetStat(unitId, StatStrength);
            bool IsPlayerActor(Id unitId) => unitId.Equals(PlayerId);

            var scheduler = new Core.Foundation.SimLoop.TurnScheduler(world, InitiativeProvider, IsPlayerActor, bus);
            var timeModelSwitch = new Core.Gameplay.Assembly.TimeModelSwitch(
                scheduler, clock, appState, world, units, spatial, bus, registry,
                factions: rules.Factions, combatOptions: rules.CombatOptions);

            return new Fixture
            {
                Bus = bus,
                Registry = registry,
                LoadReport = report,
                Rng = rng,
                World = world,
                Units = units,
                Spatial = spatial,
                Clock = clock,
                Rules = rules,
                AppState = appState,
                Scheduler = scheduler,
                TimeModelSwitch = timeModelSwitch,
                Events = events,
                AwaitingInputSubState = awaitingInput,
                PlayingBackSubState = playingBack,
            };
        }
    }
}
