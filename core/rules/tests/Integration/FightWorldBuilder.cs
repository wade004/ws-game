using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Rules.Assembly;
using Core.Rules.Common;

namespace Tests.Rules.Integration
{
    /// <summary>
    /// 落地计划 T2-11"两个单位互殴"集成测试的世界组装（<see cref="TwoUnitsFightTests"/> 唯一的
    /// 数据/世界来源，避免七八个测试各自重复一遍近 200 行 JSON）。全部数据用中性 id（不出现任何
    /// 游戏代号），只覆盖跑通一次完整"施法 → 结算 → 光环周期 → 仇恨 → AI 决策 → 死亡 → 脱战"链路
    /// 需要的最小字段集。
    /// </summary>
    internal static class FightWorldBuilder
    {
        public static readonly Id MapId = new Id("map.fight_test");
        public static readonly Id PlayerId = new Id("unit.player");
        public static readonly Id NpcId = new Id("unit.npc");

        public static readonly Id FactionPlayer = new Id("fac.player");
        public static readonly Id FactionWildlife = new Id("fac.wildlife");

        public static readonly Id ClassSample = new Id("arch.class.sample_a");
        public static readonly Id CurveSample = new Id("prog.curve.sample");

        public static readonly Id PowerHealth = WellKnownPowers.Health;
        public static readonly Id PowerMana = new Id("arch.power.mana");

        public static readonly Id SkillStrike = new Id("skill.sample_strike");
        public static readonly Id SkillBurn = new Id("skill.sample_burn");
        public static readonly Id SkillBite = new Id("skill.sample_bite");

        /// <summary>RC-08 收边补齐（<c>MovementInterruptWiringTests</c> 专用）：唯一声明
        /// <c>interrupt_flags: ["movement"]</c> 的技能——既有三个技能（strike/burn/bite）均未声明
        /// movement 中断，无法验证"unit.moved 是否真的接到 CastPipeline.NotifyMoved"这条生产接线
        /// （见 RulesAssembly 判断记录"RC-08 收边补齐"）。不出现在 <see cref="AiRotationJson"/> 里，
        /// 不影响 AI 既有行为，只由 <c>MovementInterruptWiringTests</c> 显式 CastSkill。</summary>
        public static readonly Id SkillChannelMovementInterrupt = new Id("skill.sample_channel_movement_interrupt");

        // 判断记录：任务书原句写的光环 id 是 "skill.aura.sample_burn"，本类改用
        // "skill.aura_def.sample_burn"——skill 模块 schema/README.md 与全部既有测试
        // （AuraEffectTests.cs 等）统一约定 aura_def 的 id 前缀是 "skill.aura_def.<name>"（见
        // SkillSchemas.AuraDef 字段注释"skill.aura_def.<name>"），沿用任务书字面 id 会与模块自身
        // 的命名惯例不一致，这里按模块惯例改一个字，语义不变。
        public static readonly Id AuraBurn = new Id("skill.aura_def.sample_burn");

        public static readonly Id TargetChainNearestEnemy = new Id("target.chain.nearest_enemy");

        public static readonly Id AiProfileSample = new Id("ai.profile.sample");
        public static readonly Id AiRotationSample = new Id("ai.rotation.sample");

        public static readonly Id HitTableDefault = new Id("combat.hit_table.default");

        public static readonly Id StatArmor = new Id("stat.armor");
        public static readonly Id StatDamageDonePct = new Id("stat.damage_done_pct");
        public static readonly Id StatDamageTakenPct = new Id("stat.damage_taken_pct");
        public static readonly Id StatHealingDonePct = new Id("stat.healing_done_pct");

        public static readonly Id SchoolPhysical = new Id("school.physical");

        /// <summary>固定步长：1 模拟秒/tick——刻意选取一个与技能读条时间（1.0）、光环周期
        /// （interval 1.0）同量级的步长，让"tick 序号"与"技能/光环的时间线"直接对应，判断记录
        /// 见 <see cref="TwoUnitsFightTests"/> 顶部注释。</summary>
        public const double StepSeconds = 1.0;

        public const int MaxTicks = 200;

        /// <summary>初始生命值：npc 明显更低，让战斗结果不依赖任何随机分支即可在个位数 tick 内
        /// 分出胜负（本场景命中表全部分支禁用，见 <see cref="HitTableJson"/>，伤害本身不含随机
        /// 波动，只是"谁的生命值池更小"的确定性计算题）。</summary>
        public const double PlayerMaxHp = 150;
        public const double NpcStartHp = 60;
        public const double MaxMana = 100;

        private const string StatDefinitionJson = @"
        {
            ""table"": ""stat.definition"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""stat.armor"", ""name_key"": ""l10n.stat.armor.name"", ""group"": ""secondary"", ""default_base"": 0 },
                { ""id"": ""stat.damage_done_pct"", ""name_key"": ""l10n.stat.damage_done_pct.name"", ""group"": ""secondary"", ""default_base"": 0 },
                { ""id"": ""stat.damage_taken_pct"", ""name_key"": ""l10n.stat.damage_taken_pct.name"", ""group"": ""secondary"", ""default_base"": 0 },
                { ""id"": ""stat.healing_done_pct"", ""name_key"": ""l10n.stat.healing_done_pct.name"", ""group"": ""secondary"", ""default_base"": 0 }
            ]
        }";

        /// <summary>RC-06 收边补齐（<c>PowerMaxRecomputeWiringTests</c> 专用）：唯一一个
        /// <c>max_source.kind == "stat"</c> 的资源类型——PlayerHealth/Mana 都是 fixed，无法用来验证
        /// "属性变化后 PowerHost.RecomputeMax 是否被生产接线自动触发"（见 RulesAssembly 判断记录
        /// "RC-06 收边补齐"）。上限来源即 <see cref="StatArmor"/>（本类已有的属性，零额外数据）。
        /// 不加入 <see cref="ClassSample"/> 的 <c>power_types</c>，不影响任何既有测试——需要它的
        /// 单位由 <c>PowerMaxRecomputeWiringTests</c> 自己直接调用 <c>Powers.RegisterUnit</c> 注册。</summary>
        public static readonly Id PowerShield = new Id("arch.power.test_shield");

        private static string PowerTypeJson => @"
        {
            ""table"": ""arch.power_type"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": """ + PowerHealth.Value + @""", ""name_key"": ""l10n.power.health.name"",
                  ""max_source"": {""kind"": ""fixed"", ""value"": " + PlayerMaxHp.ToString(System.Globalization.CultureInfo.InvariantCulture) + @"},
                  ""regen_in_combat"": 0, ""regen_out_of_combat"": 0, ""decay_out_of_combat"": 0,
                  ""refill_on_leave_combat"": false, ""start_full"": true, ""allow_overflow"": false, ""min"": 0 },
                { ""id"": """ + PowerMana.Value + @""", ""name_key"": ""l10n.power.mana.name"",
                  ""max_source"": {""kind"": ""fixed"", ""value"": " + MaxMana.ToString(System.Globalization.CultureInfo.InvariantCulture) + @"},
                  ""regen_in_combat"": 0, ""regen_out_of_combat"": 0, ""decay_out_of_combat"": 0,
                  ""refill_on_leave_combat"": false, ""start_full"": true, ""allow_overflow"": false, ""min"": 0 },
                { ""id"": """ + PowerShield.Value + @""", ""name_key"": ""l10n.power.test_shield.name"",
                  ""max_source"": {""kind"": ""stat"", ""stat"": """ + StatArmor.Value + @"""},
                  ""regen_in_combat"": 0, ""regen_out_of_combat"": 0, ""decay_out_of_combat"": 0,
                  ""refill_on_leave_combat"": false, ""start_full"": true, ""allow_overflow"": false, ""min"": 0 }
            ]
        }";

        private static string ArchClassJson => @"
        {
            ""table"": ""arch.class"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": """ + ClassSample.Value + @""", ""name_key"": ""l10n.arch.class.sample_a.name"",
                  ""primary_stat"": ""stat.armor"",
                  ""base_stats"": { ""stat.armor"": 5 },
                  ""power_types"": [""" + PowerHealth.Value + @""", """ + PowerMana.Value + @"""],
                  ""level_curve_ref"": """ + CurveSample.Value + @""" }
            ]
        }";

        private const string ProgLevelCurveJson = @"
        {
            ""table"": ""prog.level_curve"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""prog.curve.sample"", ""max_level"": 1,
                  ""entries"": [ { ""level"": 1, ""xp_to_next"": 0, ""growth"": {} } ] }
            ]
        }";

        private const string FactionJson = @"
        {
            ""table"": ""fac.faction"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""fac.player"", ""name_key"": ""l10n.fac.player.name"", ""default_reaction"": ""friendly"" },
                { ""id"": ""fac.wildlife"", ""name_key"": ""l10n.fac.wildlife.name"", ""default_reaction"": ""neutral"" }
            ]
        }";

        private const string ReactionMatrixJson = @"
        {
            ""table"": ""fac.reaction_matrix"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""fac.reaction_matrix.player_to_wildlife"", ""from"": ""fac.player"", ""to"": ""fac.wildlife"", ""reaction"": ""hostile"" },
                { ""id"": ""fac.reaction_matrix.wildlife_to_player"", ""from"": ""fac.wildlife"", ""to"": ""fac.player"", ""reaction"": ""hostile"" }
            ]
        }";

        // 命中表全部分支禁用：整场战斗的伤害结算不含任何随机波动（不消耗 combat.hit RngStream 一
        // 个随机数），见 TwoUnitsFightTests 顶部关于"不同种子"判断记录。
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

        // 判断记录：CombatDataLoader 要求 "combat.resist_curve" 表本身存在（即便零行）——本场景
        // 全部效果都是 school.physical（用护甲，不查抗性曲线，见 06 第 4.3 节），零行足够。
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
                { ""id"": ""target.chain.nearest_enemy"", ""source"": ""nearest_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 },
                  ""filters"": [""relation:hostile"", ""alive""],
                  ""sort_by"": { ""key"": ""distance"", ""direction"": ""asc"" },
                  ""max_targets"": 1 }
            ]
        }";

        private static string SkillDefJson => @"
        {
            ""table"": ""skill.def"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""skill.sample_strike"", ""school"": ""school.physical"", ""kind"": ""active"",
                  ""range"": 3, ""cast_time"": 0, ""respects_gcd"": false,
                  ""cost"": [ { ""power_type"": ""arch.power.mana"", ""amount"": 10 } ],
                  ""target_shape_ref"": ""target.chain.nearest_enemy"",
                  ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 25, ""coefficient"": 0 } } ] },

                { ""id"": ""skill.sample_burn"", ""school"": ""school.physical"", ""kind"": ""active"",
                  ""range"": 3, ""cast_time"": 1.0, ""respects_gcd"": false,
                  ""cost"": [ { ""power_type"": ""arch.power.mana"", ""amount"": 5 } ],
                  ""target_shape_ref"": ""target.chain.nearest_enemy"",
                  ""effects"": [ { ""kind"": ""apply_aura"", ""params"": { ""aura_def"": ""skill.aura_def.sample_burn"" } } ] },

                { ""id"": ""skill.sample_bite"", ""school"": ""school.physical"", ""kind"": ""active"",
                  ""range"": 3, ""cast_time"": 0, ""respects_gcd"": false,
                  ""target_shape_ref"": ""target.chain.nearest_enemy"",
                  ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 15, ""coefficient"": 0 } } ] },

                { ""id"": """ + SkillChannelMovementInterrupt.Value + @""", ""school"": ""school.physical"", ""kind"": ""active"",
                  ""range"": 0, ""cast_time"": 3.0, ""respects_gcd"": false,
                  ""target_shape_ref"": ""target.chain.nearest_enemy"", ""interrupt_flags"": [""movement""],
                  ""effects"": [] }
            ]
        }";

        private const string SkillAuraDefJson = @"
        {
            ""table"": ""skill.aura_def"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""skill.aura_def.sample_burn"", ""duration"": 4, ""max_stacks"": 1,
                  ""effects"": [ { ""kind"": ""periodic_damage"",
                    ""params"": { ""interval"": 1.0, ""base_value"": 5, ""coefficient"": 0, ""school"": ""school.physical"" } } ] }
            ]
        }";

        private const string AiRotationJson = @"
        {
            ""table"": ""ai.rotation"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""ai.rotation.sample"",
                  ""entries"": [ { ""priority"": 1, ""condition"": ""target.hp_pct > 0"", ""skill_id"": ""skill.sample_bite"" } ] }
            ]
        }";

        private const string AiBehaviorProfileJson = @"
        {
            ""table"": ""ai.behavior_profile"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""ai.profile.sample"", ""perception_radius"": 50, ""leash_range"": 50,
                  ""combat_return_policy"": ""stay"", ""rotation_ref"": ""ai.rotation.sample"", ""decision_interval"": 0.5 }
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
                .Add("skill.aura_def", SkillAuraDefJson)
                .Add("ai.rotation", AiRotationJson)
                .Add("ai.behavior_profile", AiBehaviorProfileJson);
        }

        /// <summary>用 <see cref="EventKeys.All"/> 构造事件目录（惯例同
        /// <c>core/foundation/tests/Determinism/DeterministicWorld.BuildCatalog</c>），保证 L2 四
        /// 模块与本测试自己订阅的全部事件 key 都已登记，<see cref="EventBusOptions.AuditLog"/>
        /// 打开供 <see cref="InMemoryEventAudit"/> 记录完整的事件流。</summary>
        public static IEventBus BuildEventBus(out InMemoryEventAudit audit)
        {
            var definitions = new List<EventDefinition>(EventKeys.All.Length);
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, Array.Empty<string>()));
            }

            var catalog = EventCatalog.FromDefinitions(definitions);
            audit = new InMemoryEventAudit();
            return new EventBus(catalog, new EventBusOptions { AuditLog = true }, audit: audit);
        }

        public sealed class Fixture
        {
            public IEventBus Bus = null!;
            public InMemoryEventAudit Audit = null!;
            public IDataRegistry Registry = null!;
            public ValidationReport LoadReport = null!;
            public IRngHost Rng = null!;
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public StubSpatialQuery Spatial = null!;
            public SimClockHost Clock = null!;
            public RulesAssembly Rules = null!;
            public List<IEvent> Events = null!;

            /// <summary>推进到"一方死亡后再多跑 <paramref name="bufferTicksAfterDeath"/> 个 tick"或
            /// <see cref="MaxTicks"/>（先到者为准），期间按 <see cref="PlayerScript"/> 提交玩家意图。
            /// 返回实际跑到的 tick 数。</summary>
            public int Run(int bufferTicksAfterDeath = 15)
            {
                int? deathTick = null;

                for (var tick = 1; tick <= MaxTicks; tick++)
                {
                    var skillId = PlayerScript(tick);
                    if (skillId.HasValue)
                    {
                        World.SubmitIntent(new Intent(PlayerId, "cast", CastArgs(skillId.Value)));
                    }

                    Clock.Advance(StepSeconds);

                    if (deathTick == null && (!Units.IsAlive(PlayerId) || !Units.IsAlive(NpcId)))
                    {
                        deathTick = tick;
                    }

                    if (deathTick.HasValue && tick >= deathTick.Value + bufferTicksAfterDeath)
                    {
                        return tick;
                    }
                }

                return MaxTicks;
            }
        }

        /// <summary>玩家意图脚本（任务书原句）：tick 1 cast strike、tick 3 cast burn、之后每 2 tick
        /// cast strike（5, 7, 9, ...）。</summary>
        public static Id? PlayerScript(int tick)
        {
            if (tick == 1) return SkillStrike;
            if (tick == 3) return SkillBurn;
            if (tick >= 5 && (tick - 3) % 2 == 0) return SkillStrike;
            return null;
        }

        private static JsonObject CastArgs(Id skillId) =>
            new JsonObjectBuilder().Add("skill_id", new JsonString(skillId.Value)).Build();

        /// <summary>组装一整套全新的世界（每次调用都是独立实例，供"重复运行两次比较事件流"的
        /// 确定性用例使用）。</summary>
        public static Fixture Build(ulong seed = 20260905UL)
        {
            var bus = BuildEventBus(out var audit);
            var events = new List<IEvent>();
            foreach (var key in EventKeys.All)
            {
                bus.Subscribe(key, e => events.Add(e));
            }

            var registry = new DataRegistry(BuildDataSource(), bus, RulesSchemaCatalog.CreateOptions());
            RulesSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "FightWorldBuilder 夹具数据未通过校验：" + string.Join("; ", report.Issues));
            }

            var rng = new RngHost(seed);
            var world = new WorldSim(bus);

            world.AddEntity(new TestUnit(PlayerId, MapId, FactionPlayer) { Position = new Vec2(0, 0) });
            world.AddEntity(new TestUnit(NpcId, MapId, FactionWildlife) { Position = new Vec2(1.5, 0) });

            var units = new WorldUnitAccess(world);
            var spatial = new StubSpatialQuery();
            spatial.Register(PlayerId, new Vec2(0, 0), 0.1);
            spatial.Register(NpcId, new Vec2(1.5, 0), 0.1);

            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, new MoveIntentHandler(spatial));

            var rules = new RulesAssembly(bus, registry, rng, units, spatial, world);

            rules.RegisterUnit(PlayerId, ClassSample, raceId: null, level: 1);
            rules.RegisterUnit(NpcId, ClassSample, raceId: null, level: 1, aiProfileId: AiProfileSample, aiSpawnPoint: new Vec2(1.5, 0));

            // npc 起始生命值人为调低（见类型顶部 NpcStartHp 判断记录），使战斗结果在个位数 tick 内
            // 确定性分出胜负，不依赖任何随机分支。
            rules.Powers.ModifyPower(NpcId, PowerHealth, NpcStartHp - PlayerMaxHp, sourceId: new Id("system.fixture_setup"));

            var clock = new SimClockHost(world, new SimLoopOptions { StepSeconds = StepSeconds, MaxCatchUpSteps = 2 });

            return new Fixture
            {
                Bus = bus,
                Audit = audit,
                Registry = registry,
                LoadReport = report,
                Rng = rng,
                World = world,
                Units = units,
                Spatial = spatial,
                Clock = clock,
                Rules = rules,
                Events = events,
            };
        }
    }
}
