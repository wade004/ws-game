using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Rules.Assembly;
using Core.Rules.Common;

namespace Tests.Gameplay.Replay
{
    /// <summary>
    /// 回放回归测试（11_工程规范与测试.md 第 6 节"回放回归：固定录像 + 固定种子重放，比对事件流
    /// 与既往基线"）的世界组装：自带一份最小自洽的嵌入式数据（惯例同
    /// <c>core/gameplay/tests/Discrete/DiscreteFightWorldBuilder.cs</c>——不依赖
    /// <c>data/_sample</c>/<c>data/_framework</c>，不受其它任务改动示例数据的影响，本模块只关心
    /// "同一份固定录像重放是否产生同一份事件流/摘要"这一件事），命中表全部分支禁用（伤害结算不含
    /// 随机波动，保证确定性）。
    /// <para>
    /// 判断记录（<see cref="WorldFactory"/> 签名要求，见 <c>Core.Foundation.SaveSystem.WorldFactory</c>
    /// 注释"不自建事件总线"）：<see cref="BuildContinuousWorld"/>/<see cref="BuildDiscreteWorld"/>
    /// 直接就是 <c>WorldFactory</c> 委托本身（签名 <c>(ulong masterSeed, IEventBus bus) =&gt;
    /// (IWorldSim, IRngHost)</c>），供 <c>ReplayPlayer</c> 在 <c>Load</c> 时调用；"录制"一侧
    /// （<see cref="RecordContinuousFight"/>/<see cref="RecordDiscreteFight"/>）额外自建一个
    /// <see cref="IEventBus"/> 独立驱动一遍相同的固定脚本，两者互不共享任何可变状态。
    /// </para>
    /// <para>
    /// 判断记录（离散场景不经 <c>TurnScheduler</c>）：同 <c>core/foundation/save_system/tests/
    /// DiscreteReplayTests.cs</c>"判断记录（不经 TurnScheduler，手工交替行动者）"——<c>sim.
    /// turn_started</c>/<c>turn_ended</c>/<c>round_ended</c> 三个事件由 <c>TurnScheduler</c> 自己
    /// <c>PublishImmediate</c>，不经过 <see cref="IWorldSim.Tick"/>，天然不在"回放 = 重放
    /// SimStep 序列给 world.Tick"这一机制覆盖范围内；<see cref="BuildDiscreteWorld"/> 因此不装配
    /// <c>TurnScheduler</c>/<c>TimeModelSwitch</c>，改由录制脚本手工交替产生
    /// <see cref="SimStep.Discrete"/>，双方的技能施放也都是脚本直接提交 <c>cast</c> 意图（不依赖
    /// AI 决策），保证"直接驱动"与"经录像重放"这两条路径覆盖的事件完全对齐。
    /// </para>
    /// </summary>
    internal static class ReplayWorldBuilder
    {
        public static readonly Id MapId = new Id("map.replay_test");
        public static readonly Id PlayerId = new Id("unit.replay_player");
        public static readonly Id NpcId = new Id("unit.replay_npc");

        public static readonly Id FactionPlayer = new Id("fac.replay_player");
        public static readonly Id FactionWildlife = new Id("fac.replay_wildlife");

        public static readonly Id ClassSample = new Id("arch.class.replay_sample");
        public static readonly Id PowerHealth = WellKnownPowers.Health;

        public static readonly Id SkillStrike = new Id("skill.replay_strike"); // 玩家技能：20 点伤害。
        public static readonly Id SkillBite = new Id("skill.replay_bite");     // NPC 技能：8 点伤害。

        public const double PlayerMaxHp = 150;
        public const double NpcStartHp = 60;

        public const double StepSeconds = 1.0;

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
                { ""id"": """ + ClassSample.Value + @""", ""name_key"": ""l10n.arch.class.replay_sample.name"",
                  ""primary_stat"": ""stat.armor"",
                  ""base_stats"": { ""stat.armor"": 5 },
                  ""power_types"": [""" + PowerHealth.Value + @"""] }
            ]
        }";

        private const string FactionJson = @"
        {
            ""table"": ""fac.faction"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""fac.replay_player"", ""name_key"": ""l10n.fac.replay_player.name"", ""default_reaction"": ""friendly"" },
                { ""id"": ""fac.replay_wildlife"", ""name_key"": ""l10n.fac.replay_wildlife.name"", ""default_reaction"": ""neutral"" }
            ]
        }";

        private const string ReactionMatrixJson = @"
        {
            ""table"": ""fac.reaction_matrix"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""fac.reaction_matrix.replay_player_to_wildlife"", ""from"": ""fac.replay_player"", ""to"": ""fac.replay_wildlife"", ""reaction"": ""hostile"" },
                { ""id"": ""fac.reaction_matrix.replay_wildlife_to_player"", ""from"": ""fac.replay_wildlife"", ""to"": ""fac.replay_player"", ""reaction"": ""hostile"" }
            ]
        }";

        // 命中表全部分支禁用：伤害结算不含随机波动（惯例同 FightWorldBuilder/DiscreteFightWorldBuilder）。
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
                { ""id"": ""target.chain.replay_nearest_enemy"", ""source"": ""nearest_in_shape"",
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
                { ""id"": ""skill.replay_strike"", ""school"": ""school.physical"", ""kind"": ""active"",
                  ""range"": 10, ""cast_time"": 0, ""respects_gcd"": false,
                  ""target_shape_ref"": ""target.chain.replay_nearest_enemy"",
                  ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 20, ""coefficient"": 0 } } ] },

                { ""id"": ""skill.replay_bite"", ""school"": ""school.physical"", ""kind"": ""active"",
                  ""range"": 10, ""cast_time"": 0, ""respects_gcd"": false,
                  ""target_shape_ref"": ""target.chain.replay_nearest_enemy"",
                  ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 8, ""coefficient"": 0 } } ] }
            ]
        }";

        public static InMemoryDataSource BuildDataSource()
        {
            return new InMemoryDataSource()
                .Add("stat.definition", StatDefinitionJson)
                .Add("arch.power_type", PowerTypeJson)
                .Add("arch.class", ArchClassJson)
                .Add("fac.faction", FactionJson)
                .Add("fac.reaction_matrix", ReactionMatrixJson)
                .Add("combat.hit_table_config", HitTableJson)
                .Add("combat.resist_curve", ResistCurveJson)
                .Add("target.chain_def", TargetChainJson)
                .Add("skill.def", SkillDefJson);
        }

        public static JsonObject CastArgs(Id skillId) =>
            new JsonObjectBuilder().Add("skill_id", new JsonString(skillId.Value)).Build();

        private static (IDataRegistry Registry, ValidationReport Report) BuildRegistry(IEventBus bus)
        {
            var registry = new DataRegistry(BuildDataSource(), bus, RulesSchemaCatalog.CreateOptions());
            RulesSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "ReplayWorldBuilder 夹具数据未通过校验：" + string.Join("; ", report.Issues));
            }
            return (registry, report);
        }

        private static (WorldSim World, WorldUnitAccessBundle Units, RulesAssembly Rules) BuildCommon(IEventBus bus)
        {
            var (registry, _) = BuildRegistry(bus);
            var rng = new RngHost(0UL); // 主种子不重要：不使用命中表随机分支，见类型顶部判断记录。
            var world = new WorldSim(bus);

            world.AddEntity(new Core.Carriers.Unit.PlayerUnit(PlayerId, MapId, FactionPlayer, ClassSample) { Position = new Vec2(0, 0) });
            world.AddEntity(new Core.Carriers.Unit.CreatureUnit(NpcId, MapId, FactionWildlife, ClassSample) { Position = new Vec2(1.0, 0) });

            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var spatial = new Adapters.Stub.StubSpatialQuery();
            spatial.Register(PlayerId, new Vec2(0, 0), 0.1);
            spatial.Register(NpcId, new Vec2(1.0, 0), 0.1);

            var rules = new RulesAssembly(bus, registry, rng, units, spatial, world);
            rules.RegisterUnit(PlayerId, ClassSample, raceId: null, level: 1);
            rules.RegisterUnit(NpcId, ClassSample, raceId: null, level: 1);

            rules.Powers.ModifyPower(NpcId, PowerHealth, NpcStartHp - PlayerMaxHp, sourceId: new Id("system.replay_fixture_setup"));

            return (world, new WorldUnitAccessBundle(units, spatial), rules);
        }

        internal sealed class WorldUnitAccessBundle
        {
            public readonly Core.Carriers.Unit.WorldUnitAccess Units;
            public readonly Adapters.Stub.StubSpatialQuery Spatial;

            public WorldUnitAccessBundle(Core.Carriers.Unit.WorldUnitAccess units, Adapters.Stub.StubSpatialQuery spatial)
            {
                Units = units;
                Spatial = spatial;
            }
        }

        /// <summary><c>Core.Foundation.SaveSystem.WorldFactory</c> 形状：连续模式世界（不装配任何
        /// 离散相关组件）。</summary>
        public static (IWorldSim World, IRngHost Rng) BuildContinuousWorld(ulong masterSeed, IEventBus bus)
        {
            var (world, _, rules) = BuildCommon(bus);
            return (world, rules.Rng);
        }

        /// <summary><c>Core.Foundation.SaveSystem.WorldFactory</c> 形状：离散模式世界——数据/单位与
        /// <see cref="BuildContinuousWorld"/> 完全相同，区别只在录制/重放脚本用
        /// <see cref="SimStep.Discrete"/> 而非 <see cref="SimStep.Continuous"/> 推进（见类型顶部
        /// 判断记录，本方法不装配 TurnScheduler）。</summary>
        public static (IWorldSim World, IRngHost Rng) BuildDiscreteWorld(ulong masterSeed, IEventBus bus)
        {
            var (world, _, rules) = BuildCommon(bus);
            return (world, rules.Rng);
        }

        // -----------------------------------------------------------------
        // 固定脚本：双方每步都尝试对彼此释放技能（cast_time=0、respects_gcd=false，瞬时结算），
        // 目标经 target_shape_ref 动态解析最近的存活敌对单位——一方死亡后另一方的施法会因目标解析
        // 落空而以 skill.cast_failed 收场，这本身也是固定、确定性的事件序列的一部分，脚本不需要
        // 感知战斗结果提前收尾。
        // -----------------------------------------------------------------

        public const int ContinuousFixedTicks = 20;
        public const int DiscreteFixedSteps = 20;

        /// <summary>直接驱动一遍固定连续脚本（不经录像），供"直跑"侧验证使用。</summary>
        public static (IWorldSim World, IEventBus Bus, InMemoryEventAudit Audit, long FinalTick) RunContinuousFixedScript()
        {
            var (bus, audit) = CreateAuditedBus();
            var (world, _) = BuildContinuousWorld(0UL, bus);

            for (long tick = 1; tick <= ContinuousFixedTicks; tick++)
            {
                world.SubmitIntent(new Intent(PlayerId, "cast", CastArgs(SkillStrike)));
                world.SubmitIntent(new Intent(NpcId, "cast", CastArgs(SkillBite)));
                world.Tick(SimStep.Continuous(StepSeconds));
            }

            return (world, bus, audit, ContinuousFixedTicks);
        }

        /// <summary>录制一遍固定连续脚本，产出 <see cref="ReplayData"/>（"录像"）。</summary>
        public static ReplayData RecordContinuousFight()
        {
            var bus = CreateAuditedBus().Bus;
            var (world, _) = BuildContinuousWorld(0UL, bus);
            var recorder = new ReplayRecorder(StepSeconds);
            recorder.BeginRecording(new Dictionary<string, RngStreamState>(StringComparer.Ordinal));

            for (long tick = 1; tick <= ContinuousFixedTicks; tick++)
            {
                recorder.RecordInput(tick, new ReplayInputRecord(tick, PlayerId, "cast", CastArgs(SkillStrike)));
                recorder.RecordInput(tick, new ReplayInputRecord(tick, NpcId, "cast", CastArgs(SkillBite)));
                world.SubmitIntent(new Intent(PlayerId, "cast", CastArgs(SkillStrike)));
                world.SubmitIntent(new Intent(NpcId, "cast", CastArgs(SkillBite)));
                var step = SimStep.Continuous(StepSeconds);
                recorder.RecordStep(tick, step);
                world.Tick(step);
            }

            recorder.SetTickCount(ContinuousFixedTicks);
            return recorder.Export();
        }

        /// <summary>直接驱动一遍固定离散脚本（不经录像），供"直跑"侧验证使用。玩家/NPC 交替行动
        /// （奇数步玩家、偶数步 NPC），各自释放固定技能。</summary>
        public static (IWorldSim World, IEventBus Bus, InMemoryEventAudit Audit, long FinalTick) RunDiscreteFixedScript()
        {
            var (bus, audit) = CreateAuditedBus();
            var (world, _) = BuildDiscreteWorld(0UL, bus);

            for (long tick = 1; tick <= DiscreteFixedSteps; tick++)
            {
                var actorId = tick % 2 == 1 ? PlayerId : NpcId;
                var skillId = tick % 2 == 1 ? SkillStrike : SkillBite;
                world.SubmitIntent(new Intent(actorId, "cast", CastArgs(skillId)));
                world.Tick(SimStep.Discrete(actorId, StepPhase.Act));
            }

            return (world, bus, audit, DiscreteFixedSteps);
        }

        /// <summary>录制一遍固定离散脚本，产出 <see cref="ReplayData"/>（"录像"）。</summary>
        public static ReplayData RecordDiscreteFight()
        {
            var bus = CreateAuditedBus().Bus;
            var (world, _) = BuildDiscreteWorld(0UL, bus);
            var recorder = new ReplayRecorder(StepSeconds);
            recorder.BeginRecording(new Dictionary<string, RngStreamState>(StringComparer.Ordinal));

            for (long tick = 1; tick <= DiscreteFixedSteps; tick++)
            {
                var actorId = tick % 2 == 1 ? PlayerId : NpcId;
                var skillId = tick % 2 == 1 ? SkillStrike : SkillBite;
                recorder.RecordInput(tick, new ReplayInputRecord(tick, actorId, "cast", CastArgs(skillId)));
                world.SubmitIntent(new Intent(actorId, "cast", CastArgs(skillId)));
                var step = SimStep.Discrete(actorId, StepPhase.Act);
                recorder.RecordStep(tick, step);
                world.Tick(step);
            }

            recorder.SetTickCount(DiscreteFixedSteps);
            return recorder.Export();
        }

        public static (IEventBus Bus, InMemoryEventAudit Audit) CreateAuditedBus()
        {
            var definitions = new List<EventDefinition>(EventKeys.All.Length);
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, Array.Empty<string>()));
            }

            var catalog = EventCatalog.FromDefinitions(definitions);
            var audit = new InMemoryEventAudit();
            var bus = new EventBus(catalog, new EventBusOptions { AuditLog = true }, audit: audit);
            return (bus, audit);
        }
    }
}
