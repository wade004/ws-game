using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Rules.Common;
using Xunit;
using AppStateEnum = Core.Foundation.AppLifecycle.AppState;

namespace Tests.Gameplay.Discrete
{
    /// <summary>
    /// W2 收边补齐（A3 审计 #4/#5/#7，DECISIONS 拍板 4）：<see cref="GameplayAssembly"/> 自身的
    /// 离散模式接线端到端验证——`skill.def.action_cost` 真正扣减 <see cref="TurnScheduler"/> 的
    /// 行动点账本、离散步真正跳过公共冷却、`ai.rotation` 的 `time.is_my_turn` 条件经
    /// `GameplayAssembly` 组装出的 `RulesAssembly`（内部 `RulesExprHostFactory`）真实求值为 true。
    /// <para>
    /// 判断记录（不复用 <see cref="Tests.Gameplay.Discrete.DiscreteFightWorldBuilder"/>）：该夹具
    /// 直接 <c>new RulesAssembly(...)</c>（不经 <see cref="GameplayAssembly"/>），不会触达本任务
    /// 新增的 <see cref="GameplayAssembly"/> 接线代码（`resolvedSkillOptions.TryConsumeActionPoints`/
    /// `IsDiscreteStep` 回填、`discreteTurnIndexProvider`/`discreteCurrentActorProvider` 传给
    /// `CarriersAssembly`）——本文件专门验证这条接线本身，自建一份独立的最小 `GameplayAssembly`
    /// 夹具。命中表全部分支禁用（伤害结算不含随机波动，惯例同
    /// <c>DiscreteFightWorldBuilder</c>），进入离散模式用真实命中触发（<c>Build</c> 判断记录：
    /// 曾尝试合成 <c>CombatEnteredEvent</c> 抄近路，但会与真实伤害触发的进战记账互相打架，改回
    /// 与 <c>DiscreteFightWorldBuilder.Run</c> 阶段一相同的手法）。全部数据表用独立 id 前缀
    /// （<c>w2d_*</c>），不接触 <c>data/_sample</c>/<c>data/_framework</c>，不影响任何既有测试。
    /// </para>
    /// <para>
    /// 判断记录（<c>ImmediatePacingPolicy</c> 而非默认 <c>WaitForPlaybackPacingPolicy</c>）：
    /// 本文件只验证离散步的行动点/GCD/Expr 接线，不涉及表现层回放门，用 Immediate 让
    /// <c>GameplayAssembly.Advance</c> 一次调用内连续推进直到轮到玩家（awaiting_input）或战斗
    /// 结束，不需要额外构造 <c>PresentationAssembly</c> 或手动 <c>NotifyPlaybackFinished</c>。
    /// </para>
    /// </summary>
    public sealed class GameplayAssemblyDiscreteWiringTests
    {
        private static readonly Id MapId = new Id("map.w2d_test");
        private static readonly Id PlayerId = new Id("unit.w2d_player");
        private static readonly Id NpcId = new Id("unit.w2d_npc");
        private static readonly Id FactionPlayer = new Id("fac.w2d_player");
        private static readonly Id FactionWildlife = new Id("fac.w2d_wildlife");
        private static readonly Id ClassSample = new Id("arch.class.w2d_sample");
        private static readonly Id CurveSample = new Id("prog.curve.w2d_sample");
        private static readonly Id PowerHealth = WellKnownPowers.Health;
        private static readonly Id StatStrength = new Id("stat.w2d_strength");
        private static readonly Id TargetChain = new Id("target.chain.w2d_nearest_enemy");
        private static readonly Id AiProfile = new Id("ai.profile.w2d_sample");
        private static readonly Id AiRotation = new Id("ai.rotation.w2d_sample");

        // 便宜技能：action_cost=1，配合 action_points_per_turn=1 的组合分别验证"够用则成功"
        // 与（换一条 action_cost=2 的技能）"不够则 InsufficientActionPoints"。
        private static readonly Id SkillCheap = new Id("skill.w2d_cheap");
        private static readonly Id SkillCostly = new Id("skill.w2d_costly");

        // AI 专用：仅当 time.is_my_turn 为真时才会命中的旋转条目对应技能，用于证实 Expr 接线。
        private static readonly Id SkillAiTurnOnly = new Id("skill.w2d_ai_turn_only");

        private const double NpcHp = 1000; // 足够高，全程不会被打死，避免战斗提前结束干扰观测。
        private const int MaxSteps = 40;

        private static InMemoryDataSource BuildDataSource(int actionPointsPerTurn, string initiativePolicy = "fixed_order") => new InMemoryDataSource()
            .Add("stat.definition", @"
            { ""table"": ""stat.definition"", ""schema_version"": 1, ""rows"": [
                { ""id"": """ + StatStrength.Value + @""", ""name_key"": ""l10n.stat.w2d_strength.name"", ""group"": ""primary"" }
            ] }")
            .Add("arch.power_type", @"
            { ""table"": ""arch.power_type"", ""schema_version"": 1, ""rows"": [
                { ""id"": """ + PowerHealth.Value + @""", ""name_key"": ""l10n.power.health.name"",
                  ""max_source"": {""kind"": ""fixed"", ""value"": " + NpcHp + @"},
                  ""regen_in_combat"": 0, ""regen_out_of_combat"": 0, ""decay_out_of_combat"": 0,
                  ""refill_on_leave_combat"": false, ""start_full"": true, ""allow_overflow"": false, ""min"": 0 }
            ] }")
            .Add("arch.class", @"
            { ""table"": ""arch.class"", ""schema_version"": 1, ""rows"": [
                { ""id"": """ + ClassSample.Value + @""", ""name_key"": ""l10n.arch.class.w2d_sample.name"",
                  ""primary_stat"": """ + StatStrength.Value + @""", ""base_stats"": { """ + StatStrength.Value + @""": 5 },
                  ""power_types"": [""" + PowerHealth.Value + @"""], ""level_curve_ref"": """ + CurveSample.Value + @""" }
            ] }")
            .Add("prog.level_curve", @"
            { ""table"": ""prog.level_curve"", ""schema_version"": 1, ""rows"": [
                { ""id"": """ + CurveSample.Value + @""", ""max_level"": 1,
                  ""entries"": [ { ""level"": 1, ""xp_to_next"": 0, ""growth"": {} } ] }
            ] }")
            .Add("fac.faction", @"
            { ""table"": ""fac.faction"", ""schema_version"": 1, ""rows"": [
                { ""id"": """ + FactionPlayer.Value + @""", ""name_key"": ""l10n.fac.w2d_player.name"", ""default_reaction"": ""friendly"" },
                { ""id"": """ + FactionWildlife.Value + @""", ""name_key"": ""l10n.fac.w2d_wildlife.name"", ""default_reaction"": ""neutral"" }
            ] }")
            .Add("fac.reaction_matrix", @"
            { ""table"": ""fac.reaction_matrix"", ""schema_version"": 1, ""rows"": [
                { ""id"": ""fac.reaction_matrix.w2d_p2w"", ""from"": """ + FactionPlayer.Value + @""", ""to"": """ + FactionWildlife.Value + @""", ""reaction"": ""hostile"" },
                { ""id"": ""fac.reaction_matrix.w2d_w2p"", ""from"": """ + FactionWildlife.Value + @""", ""to"": """ + FactionPlayer.Value + @""", ""reaction"": ""hostile"" }
            ] }")
            .Add("combat.hit_table_config", @"
            { ""table"": ""combat.hit_table_config"", ""schema_version"": 1, ""rows"": [
                { ""id"": ""combat.hit_table.default"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0 }
            ] }")
            .Add("combat.resist_curve", @"{ ""table"": ""combat.resist_curve"", ""schema_version"": 1, ""rows"": [] }")
            .Add("target.chain_def", @"
            { ""table"": ""target.chain_def"", ""schema_version"": 1, ""rows"": [
                { ""id"": """ + TargetChain.Value + @""", ""source"": ""nearest_in_shape"",
                  ""shape"": { ""kind"": ""circle"", ""radius"": 50 },
                  ""filters"": [""relation:hostile"", ""alive""],
                  ""sort_by"": { ""key"": ""distance"", ""direction"": ""asc"" }, ""max_targets"": 1 }
            ] }")
            .Add("skill.def", @"
            { ""table"": ""skill.def"", ""schema_version"": 1, ""rows"": [
                { ""id"": """ + SkillCheap.Value + @""", ""school"": ""school.physical"", ""kind"": ""active"",
                  ""range"": 10, ""cast_time"": 0, ""respects_gcd"": true, ""action_cost"": 1,
                  ""target_shape_ref"": """ + TargetChain.Value + @""",
                  ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 1, ""coefficient"": 0 } } ] },
                { ""id"": """ + SkillCostly.Value + @""", ""school"": ""school.physical"", ""kind"": ""active"",
                  ""range"": 10, ""cast_time"": 0, ""respects_gcd"": true, ""action_cost"": 2,
                  ""target_shape_ref"": """ + TargetChain.Value + @""",
                  ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 1, ""coefficient"": 0 } } ] },
                { ""id"": """ + SkillAiTurnOnly.Value + @""", ""school"": ""school.physical"", ""kind"": ""active"",
                  ""range"": 10, ""cast_time"": 0, ""respects_gcd"": false,
                  ""target_shape_ref"": """ + TargetChain.Value + @""",
                  ""effects"": [ { ""kind"": ""school_damage"", ""params"": { ""base_value"": 1, ""coefficient"": 0 } } ] }
            ] }")
            .Add("ai.rotation", @"
            { ""table"": ""ai.rotation"", ""schema_version"": 1, ""rows"": [
                { ""id"": """ + AiRotation.Value + @""",
                  ""entries"": [ { ""priority"": 10, ""condition"": ""time.is_my_turn"", ""skill_id"": """ + SkillAiTurnOnly.Value + @""" } ] }
            ] }")
            .Add("ai.behavior_profile", @"
            { ""table"": ""ai.behavior_profile"", ""schema_version"": 1, ""rows"": [
                { ""id"": """ + AiProfile.Value + @""", ""perception_radius"": 50, ""leash_range"": 50,
                  ""combat_return_policy"": ""stay"", ""rotation_ref"": """ + AiRotation.Value + @""", ""decision_interval"": 0.5 }
            ] }")
            .Add("found.time_model", @"
            { ""table"": ""found.time_model"", ""schema_version"": 1, ""rows"": [
                { ""id"": ""found.time_model.w2d_exploration"", ""scope"": ""exploration"", ""mode"": ""continuous"" },
                { ""id"": ""found.time_model.w2d_combat"", ""scope"": ""combat"", ""mode"": ""discrete"",
                  ""seconds_per_turn"": 6, ""initiative_policy"": """ + initiativePolicy + @""",
                  ""movement_budget_rule"": ""distance"", ""action_points_per_turn"": " + actionPointsPerTurn + @" }
            ] }");

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
            public List<Core.Foundation.EventBus.IEvent> Events = null!;

            public void SubmitPlayerCast(Id skillId)
            {
                var args = new JsonObjectBuilder().Add("skill_id", new JsonString(skillId.Value)).Build();
                World.SubmitIntent(new Intent(PlayerId, "cast", args));
            }

            /// <summary>推进到"轮到玩家、等待输入"（一次 Advance 会自动跑完期间全部 AI 回合，见
            /// <see cref="ImmediatePacingPolicy"/> 判断记录）。</summary>
            public void AdvanceUntilAwaitingPlayerInput()
            {
                for (var i = 0; i < MaxSteps; i++)
                {
                    Gameplay.Advance(0);
                    if (Gameplay.TurnScheduler != null && PlayerId.Equals(Gameplay.TurnScheduler.GetCurrentActor()))
                    {
                        return;
                    }
                }

                throw new InvalidOperationException("AdvanceUntilAwaitingPlayerInput：未能在步数上限内轮到玩家");
            }
        }

        private static Fixture Build(
            int actionPointsPerTurn, string initiativePolicy = "fixed_order", IPacingPolicy? pacingPolicy = null)
        {
            var definitions = EventKeys.All.Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>())).ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
            var events = new List<Core.Foundation.EventBus.IEvent>();
            foreach (var key in EventKeys.All)
            {
                bus.Subscribe(key, e => events.Add(e));
            }

            var options = Core.Gameplay.Assembly.GameplaySchemaCatalog.CreateOptions();
            var registry = new DataRegistry(BuildDataSource(actionPointsPerTurn, initiativePolicy), bus, options);
            Core.Gameplay.Assembly.GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "GameplayAssemblyDiscreteWiringTests 夹具数据未通过校验：" + string.Join("; ", report.Issues));
            }

            var rng = new RngHost(20260907UL);
            var world = new WorldSim(bus);
            world.AddEntity(new Core.Carriers.Unit.PlayerUnit(PlayerId, MapId, FactionPlayer, ClassSample) { Position = new Vec2(0, 0) });
            world.AddEntity(new Core.Carriers.Unit.CreatureUnit(NpcId, MapId, FactionWildlife, ClassSample) { Position = new Vec2(1.0, 0) });

            var spatial = new StubSpatialQuery();
            spatial.Register(PlayerId, new Vec2(0, 0), 0.1);
            spatial.Register(NpcId, new Vec2(1.0, 0), 0.1);

            var fs = new StubFileSystem();
            var saveSystem = new Core.Foundation.SaveSystem.SaveSystem(fs, new SaveSystemOptions(new Id("game.w2d_sample")));

            var clock = new SimClockHost(world, new SimLoopOptions { StepSeconds = 1.0, MaxCatchUpSteps = 2 });

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: FactionPlayer,
                clockHost: clock,
                pacingPolicy: pacingPolicy ?? new ImmediatePacingPolicy(),
                // GcdEnabled 显式开启（默认 false，见 SkillOptions 判断记录）：不开启的话公共冷却
                // 检查恒直接通过，"离散步跳过 GCD"这条用例会无论接线是否正确都通过（假阳性）。
                // GcdDuration 给一个远超单次测试执行时间的值，确保"若未正确跳过"这条分支必然可观测。
                skillOptions: new Core.Rules.Skill.SkillOptions { GcdEnabled = true, GcdDuration = 60.0 });

            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ClassSample, raceId: null, level: 1);
            gameplay.Carriers.Rules.RegisterUnit(
                NpcId, ClassSample, raceId: null, level: 1, aiProfileId: AiProfile, aiSpawnPoint: new Vec2(1.0, 0));

            gameplay.AppState.RequestTransition(AppStateEnum.MainMenu);
            gameplay.AppState.RequestTransition(AppStateEnum.Loading);
            gameplay.AppState.RequestTransition(AppStateEnum.InWorld);

            var fx = new Fixture { Bus = bus, World = world, Gameplay = gameplay, Events = events };

            // 判断记录（真实命中触发进战，不用合成 combat.entered 事件）：早期草稿直接
            // PublishImmediate 一条合成 CombatEnteredEvent（惯例同 adapters/unity
            // DiscreteCombatTests"用合成事件触发离散模式，不走真实攻击"）——但那个夹具全程不
            // 产生任何真实伤害，本文件的用例恰恰需要真实施法（验证 action_cost/GCD），一旦发生
            // 真实伤害，core/rules/combat.Resolver 会独立调用 NotifyCombatEvent 走一遍真实的
            // "进战"记账；这条真实记账此前没有被合成事件同步过（CombatHost 内部并不知道"已经
            // 在合成层面进战"），实测会出现真实伤害后紧跟一次 CombatLeftEvent（真实记账认为
            // "刚刚才是第一次进战"与合成状态产生的中间态冲突）导致 TurnScheduler 被重新配置、
            // 行动点账本 action_points_per_turn 的取值不再稳定（回归测试曾在此处失败，见交付
            // 记录）。改为与 core/gameplay/tests/Discrete/DiscreteFightWorldBuilder.Run 阶段一
            // 同样的手法：连续模式下反复提交玩家施法意图直到真实命中触发 TimeModelSwitch 切到
            // 离散模式，全程只有一条真实的进战记账，不存在合成/真实两份状态互相打架的问题。
            for (var i = 0; i < 20 && fx.Gameplay.TimeModelSwitch!.CurrentMode == TimeModelMode.Continuous; i++)
            {
                fx.SubmitPlayerCast(SkillCheap);
                fx.Gameplay.Advance(1.0);
            }

            if (fx.Gameplay.TimeModelSwitch!.CurrentMode != TimeModelMode.Discrete)
            {
                throw new InvalidOperationException("Build：未能在尝试上限内触发离散模式（连续模式下的预热施法应命中 NPC 并触发 combat.entered）");
            }

            // 预热阶段产生的施法事件（含一次 SkillCastSuccessEvent）不属于任何用例关心的断言范围
            // （用例只关心进入离散模式之后的行为），清空避免干扰各用例里"只有一次成功/失败"一类计数断言。
            events.Clear();

            return fx;
        }

        // -----------------------------------------------------------------
        // action_cost → 行动点账本
        // -----------------------------------------------------------------

        /// <summary>
        /// W2b 判断记录（探测时机修正）：本夹具用 <c>ImmediatePacingPolicy</c>，玩家出手后若恰好是
        /// 本轮最后一位行动者，单次 <see cref="GameplayAssembly.Advance"/> 调用不会在本轮结束处停下
        /// ——只有两个参战单位（玩家/NPC）交替行动时，Advance 必然继续跑完 NPC 的下一轮首个行动，
        /// 直到再次轮到玩家等待输入才返回；届时 <c>TurnScheduler.NotifyStepConsumed</c> 早已为
        /// 新一轮调用过 <c>ResetActionPointsForRound</c>，玩家的行动点账本已按新一轮满额重置。在
        /// <c>Advance</c> 返回之后才读取账本，读到的是"下一轮的满额预算"，不能证明"本轮 SkillCheap
        /// 是否真的扣掉了预算"——这条断言此前能通过，是因为 <see cref="Core.Rules.Combat.CombatHost"/>
        /// 判断记录 3 描述的那个跨轮虚假 <c>combat.left → combat.entered</c> 往返，恰好把玩家从
        /// <c>TurnScheduler</c> 行动顺序里移除又重新追加（<c>fixed_order</c> 的
        /// <c>AddParticipant</c> 不补行动点账本条目），副作用性地把账本条目整个抹掉、
        /// <c>TryConsumeActionPoints</c> 因查不到条目而返回 <c>false</c>——凑巧与断言预期一致，但
        /// 验证的不是"账本被 SkillCheap 正确扣减"这件事。该虚假往返修复后，玩家不再被移出行动顺序，
        /// 账本改为正常地按新一轮重置，原断言因此失真。改为订阅 <c>sim.round_ended</c>（本轮结束、
        /// 下一轮 <c>ResetActionPointsForRound</c> 执行之前那一刻同步触发，见
        /// <c>TurnScheduler.AdvanceToNextActor</c>），在这个精确时刻探测账本——此时新一轮尚未开始，
        /// 读到的仍是本轮结束时刻的真实剩余预算。
        /// </summary>
        [Fact]
        public void DiscreteTurn_ActionCostSkill_SufficientBudget_ConsumesActionPointsAndSucceeds()
        {
            var fx = Build(actionPointsPerTurn: 1);
            Assert.Equal(TimeModelMode.Discrete, fx.Gameplay.TimeModelSwitch!.CurrentMode);

            fx.AdvanceUntilAwaitingPlayerInput();

            bool? canStillConsumeAtRoundEnd = null;
            fx.Bus.Subscribe<SimRoundEndedEvent>(SimEventKeys.RoundEnded, _ =>
                canStillConsumeAtRoundEnd ??= fx.Gameplay.TurnScheduler!.TryConsumeActionPoints(PlayerId, 0.01));

            fx.SubmitPlayerCast(SkillCheap); // action_cost = 1，budget = 1，刚好够。
            fx.Gameplay.Advance(0);

            Assert.Contains(fx.Events, e => e is SkillCastSuccessEvent sc && sc.SkillId.Equals(SkillCheap));
            Assert.DoesNotContain(fx.Events, e => e is SkillCastFailedEvent);
            // 账本已耗尽：budget=1 且刚才已被 SkillCheap 的 action_cost 扣掉 1，本轮结束那一刻
            // （下一轮重置之前）再扣任意正数必然失败，侧面证实 SkillCheap 那次真实消耗了账本里的
            // 行动点（而不是恰好绕过了检查）。
            Assert.NotNull(canStillConsumeAtRoundEnd);
            Assert.False(canStillConsumeAtRoundEnd!.Value);
        }

        [Fact]
        public void DiscreteTurn_ActionCostSkill_InsufficientBudget_FailsWithInsufficientActionPoints()
        {
            var fx = Build(actionPointsPerTurn: 1);
            fx.AdvanceUntilAwaitingPlayerInput();

            fx.SubmitPlayerCast(SkillCostly); // action_cost = 2 > budget = 1，应失败。
            fx.Gameplay.Advance(0);

            var failure = Assert.Single(fx.Events.OfType<SkillCastFailedEvent>(), e => e.CasterId.Equals(PlayerId));
            Assert.Equal(SkillCostly, failure.SkillId);
            Assert.Equal(CastFailureReason.InsufficientActionPoints, failure.ReasonCode);
            Assert.DoesNotContain(fx.Events, e => e is SkillCastSuccessEvent sc && sc.CasterId.Equals(PlayerId));
        }

        // -----------------------------------------------------------------
        // 离散步跳过 GCD
        // -----------------------------------------------------------------

        [Fact]
        public void DiscreteTurn_RespectsGcdSkill_CastTwiceInSameTurn_NeitherBlockedByGcd()
        {
            // 判断记录（用 action_points 先攻策略把两次施法压在玩家"同一次行动串"内，不跨回合）：
            // TurnScheduler.NotifyStepConsumed 在 action_points 策略下"行动点未耗尽则继续同一
            // 行动者"（见该方法判断记录）——action_points_per_turn=4，每个离散步无条件先扣 1（同一
            // 处判断记录），skill_cheap 的 action_cost=1 额外经 TryConsumeActionPoints 再扣 1，两次
            // 施法共消耗 2×(1+1)=4，恰好用完整个预算，全程停留在玩家回合、不需要经过 AI 回合与
            // 回合/轮次边界（曾尝试"两个独立回合各施法一次"的写法，但两次回合切换之间会观察到一次
            // 真实的 combat.left→combat.entered 往返导致 TurnScheduler 被重新配置、玩家的行动点
            // 账本未被正确按新一轮重置——不是本任务待修的生产代码缺口，见交付报告"做不了的事"，
            // 这里换一种不依赖跨回合的写法规避）。离散步之间 Dt 恒为 0，模拟时间完全不推进——若
            // GCD 未被正确跳过，公共冷却（默认时长 > 0）此刻必然仍在冷却中，第二次施法会以
            // GcdActive 失败；若正确跳过，则应再次成功。
            var fx = Build(actionPointsPerTurn: 4, initiativePolicy: "action_points");

            fx.AdvanceUntilAwaitingPlayerInput();
            fx.SubmitPlayerCast(SkillCheap);
            fx.Gameplay.Advance(0); // 第一次施法：respects_gcd=true，离散步应跳过 GCD 检查与计时。

            // 判断记录（第二次不能再走 AdvanceUntilAwaitingPlayerInput）：action_points 策略下玩家
            // 行动点未耗尽时 NextStep 会继续把同一行动者作为"当前行动者"，但 awaiting_input 的判定
            // 只看"是否有待处理意图"——上一次 Advance(0) 处理完第一次施法后已经回到 awaiting_input
            // （GetCurrentActor 仍是玩家），直接再提交一次意图即可，不需要（也不能）再多推进。
            Assert.Equal(PlayerId, fx.Gameplay.TurnScheduler!.GetCurrentActor());
            fx.SubmitPlayerCast(SkillCheap);
            fx.Gameplay.Advance(0); // 第二次施法：若 GCD 被误判为"未跳过"，这里会以 GcdActive 失败。

            var playerFailures = fx.Events.OfType<SkillCastFailedEvent>().Where(e => e.CasterId.Equals(PlayerId)).ToList();
            Assert.True(playerFailures.Count == 0, "player cast failed: " + string.Join(",", playerFailures.Select(f => f.ReasonCode)));

            var playerSuccesses = fx.Events.OfType<SkillCastSuccessEvent>()
                .Count(e => e.CasterId.Equals(PlayerId) && e.SkillId.Equals(SkillCheap));
            Assert.Equal(2, playerSuccesses);
        }

        // -----------------------------------------------------------------
        // ai.rotation 条件 time.is_my_turn
        // -----------------------------------------------------------------

        // -----------------------------------------------------------------
        // W2b 判断记录 3（回归测试）：fixed_order 策略下单个 Advance 调用内跨越一次完整轮边界，
        // 不应观测到虚假的 combat.left → combat.entered 往返。
        // -----------------------------------------------------------------

        /// <summary>
        /// 根因见 <see cref="Core.Rules.Combat.CombatHost"/> 的
        /// <c>HasLivingHostileThreatSource</c> 判断记录：离散模式下 <c>LeaveCombatDelay</c> 换算为
        /// 轮数后常等于 1 轮，<c>CombatTickHandler</c> 每轮结束调用一次 <c>CombatHost.Update(1.0)</c>，
        /// "未到脱战延迟"的 continue 分支因此形同虚设，完全依赖仇恨表——此前仇恨表只单向记到被攻击
        /// 方身上，主动进攻、尚未被对方反击过的一方会在跨轮的那一刻被误判"周边无存活敌对来源"而
        /// 脱战，随即又被对方下一步反击重新拉回战斗，产生本用例要防止的虚假往返，并伴随
        /// <c>TurnScheduler</c>（<c>fixed_order</c> 策略）里该单位被移出行动顺序又追加到末尾。
        /// <para>
        /// 本用例驱动玩家与 NPC（<see cref="Build"/> 夹具，<c>fixed_order</c> 先攻策略、NPC 的
        /// <c>ai.rotation</c> 唯一条目 <c>time.is_my_turn</c> 命中即攻击玩家）互相攻击跨三轮——单次
        /// <see cref="GameplayAssembly.Advance"/> 调用（<c>ImmediatePacingPolicy</c>）内会连续处理
        /// 玩家出手 → 回合/轮结束 → NPC 出手 → 下一轮开始，恰好跨越任务书描述的"单个 Advance 调用内
        /// 跨越一次完整轮边界"。全程双方均存活、持续互相攻击，不应出现任何 <c>combat.left</c>——
        /// 断言 <c>combat.left</c> 事件数为 0（比任务书"只在死亡或连续 N 轮无敌对行动后触发一次"更
        /// 严格：本场景双方全程都有敌对行动，脱战次数应恰为 0），并复核 <c>TurnScheduler</c> 行动
        /// 顺序仍然只含玩家与 NPC 各一次（没有被误移除/重新追加打乱）。
        /// </para>
        /// </summary>
        [Fact]
        public void FixedOrder_MutualCombatAcrossThreeRounds_DoesNotLeaveAndReenterCombat()
        {
            var fx = Build(actionPointsPerTurn: 1);
            Assert.Equal(TimeModelMode.Discrete, fx.Gameplay.TimeModelSwitch!.CurrentMode);

            for (var round = 0; round < 3; round++)
            {
                fx.AdvanceUntilAwaitingPlayerInput();
                fx.SubmitPlayerCast(SkillCheap);
                fx.Gameplay.Advance(0);
            }

            var trace = string.Join("\n", fx.Events.Select(e => e.GetType().Name));
            var leftCount = fx.Events.OfType<CombatLeftEvent>().Count();
            Assert.True(leftCount == 0, $"leftCount={leftCount}，不应出现任何 combat.left\n{trace}");

            var order = fx.Gameplay.TurnScheduler!.GetOrder();
            Assert.Equal(1, order.Count(id => id.Equals(PlayerId)));
            Assert.Equal(1, order.Count(id => id.Equals(NpcId)));
        }

        // -----------------------------------------------------------------
        // W2b 收边补齐：GameplayAssembly.InterpolationAlpha（插值系数暴露，离散模式恒 1.0）——
        // 连续模式的用例见 core/gameplay/tests/EndToEndTests.cs 的
        // Advance_ContinuousMode_InterpolationAlpha_TracksAccumulatorAcrossSteps。
        // -----------------------------------------------------------------

        [Fact]
        public void DiscreteMode_InterpolationAlpha_IsAlwaysOne()
        {
            var fx = Build(actionPointsPerTurn: 1);
            Assert.Equal(TimeModelMode.Discrete, fx.Gameplay.TimeModelSwitch!.CurrentMode);

            // Advance 离散分支不调用 ISimClockHost.Advance（见该分支判断记录），无论本次调用是
            // "轮到玩家、停下等待输入"（本例）还是"继续推进 AI 回合"，InterpolationAlpha 都应恒为
            // 1.0——realDeltaSeconds 传入非零值也不影响这一点（离散步之间没有可插值的位置差）。
            fx.Gameplay.Advance(0.3);
            Assert.Equal(1.0, fx.Gameplay.InterpolationAlpha);

            fx.SubmitPlayerCast(SkillCheap);
            fx.Gameplay.Advance(0.3);
            Assert.Equal(1.0, fx.Gameplay.InterpolationAlpha);
        }

        [Fact]
        public void AiRotation_TimeIsMyTurnCondition_TrueDuringOwnDiscreteTurn_CastsConditionedSkill()
        {
            // ai.rotation.w2d_sample 唯一一条 entry 的 condition 就是 time.is_my_turn——若
            // GameplayAssembly 未把 discreteCurrentActorProvider 接到 RulesAssembly 内部
            // 感知 SkillHost 的 RulesExprHostFactory（供 AiHost 使用），该 key 恒返回 false
            // （见 RulesExprHostFactory 判断记录"未注入 turnIndexProvider 时恒返回默认值"），
            // AI 在自己的离散回合里会因为没有任何 rotation 条目命中而不施放任何技能；接线正确
            // 则该条件在 AI 自己的回合里恒为 true，AiTickHandler 会据此产生施法意图。
            var fx = Build(actionPointsPerTurn: 3);

            // 先让玩家什么都不做地"过掉"自己的回合（提交一个不存在意图会被拒绝，改为直接消耗一次
            // 己方回合——用最便宜技能顺带验证不干扰 AI 观测，也可以：这里选择真实施放一次，
            // 使得 NextStep 能推进到 AI 回合）。
            fx.AdvanceUntilAwaitingPlayerInput();
            fx.SubmitPlayerCast(SkillCheap);
            fx.Gameplay.Advance(0); // 玩家回合结束后，Advance 内部循环继续处理 AI 的回合。

            Assert.Contains(
                fx.Events,
                e => e is SkillCastSuccessEvent sc && sc.CasterId.Equals(NpcId) && sc.SkillId.Equals(SkillAiTurnOnly));
        }

        // -----------------------------------------------------------------
        // 根治修复（W5c，第三轮审计"离散回放门‘零事件步骤’无自动通知"仍保留项收口）：
        // GameplayAssembly.Advance 的 WaitForPlayback 分支 + WaitForPlaybackPacingPolicy.
        // HasPendingPlayback 探针端到端验证——本组用例用默认的 WaitForPlaybackPacingPolicy（而不是
        // 其余全部用例使用的 ImmediatePacingPolicy），经 GameplayAssembly.SetPendingPlaybackProbe
        // 手工接一个可控的探针（不装配 PresentationAssembly，直接控制"是否有待回放内容"这一布尔值），
        // 验证节奏门本身的行为，不依赖真实表现层——真实表现层接线（Feedback.Queue.PendingCount）的
        // 验证见 presentation/assembly/tests/PresentationAssemblyTests.cs。
        // -----------------------------------------------------------------

        /// <summary>零反馈离散步（探针恒返回 false）：Advance 不应停在 playing_back，应像
        /// ImmediatePacingPolicy 一样一次调用内继续推进直到下一次真正需要停下（本例是再次轮到玩家
        /// 等待输入）——不调用 NotifyPlaybackFinished 也不应卡死，这正是本次要根治的缺陷。</summary>
        [Fact]
        public void DiscretePacing_ZeroFeedbackStep_DoesNotEnterPlayingBack_AdvancesWithoutNotify()
        {
            var fx = Build(actionPointsPerTurn: 1, pacingPolicy: new WaitForPlaybackPacingPolicy());
            fx.Gameplay.SetPendingPlaybackProbe(() => false);

            fx.AdvanceUntilAwaitingPlayerInput();
            fx.SubmitPlayerCast(SkillCheap);
            fx.Gameplay.Advance(0);

            Assert.False(
                fx.Gameplay.AppState.CurrentSubState.HasValue &&
                fx.Gameplay.AppState.CurrentSubState.Value.Equals(fx.Gameplay.PlayingBackSubState),
                "探针恒返回 false（零反馈）时不应停在 playing_back 子态");
            Assert.Contains(fx.Events, e => e is SkillCastSuccessEvent sc && sc.SkillId.Equals(SkillCheap));

            var pacing = Assert.IsType<WaitForPlaybackPacingPolicy>(fx.Gameplay.Pacing);
            Assert.True(pacing.IsPlaybackFinished, "零反馈步之后节奏门应处于开启（已完成）状态");
        }

        /// <summary>有反馈离散步（探针恒返回 true）：Advance 应停在 playing_back 并保持，直到调用方
        /// 调用 NotifyPlaybackFinished 才解除——不再依赖任何"零事件兜底"，验证的正是"确有内容才
        /// 关闭节奏门"这条恒等语义在有内容时仍然成立（不被本次修复误伤成"恒不等待"）。</summary>
        [Fact]
        public void DiscretePacing_PendingFeedbackStep_EntersPlayingBack_WaitsForNotifyPlaybackFinished()
        {
            var fx = Build(actionPointsPerTurn: 1, pacingPolicy: new WaitForPlaybackPacingPolicy());
            var hasPending = true; // 模拟"这一步确有反馈动作已入队、尚未播放完"。
            fx.Gameplay.SetPendingPlaybackProbe(() => hasPending);

            fx.AdvanceUntilAwaitingPlayerInput();
            fx.SubmitPlayerCast(SkillCheap);
            fx.Gameplay.Advance(0);

            var pacing = Assert.IsType<WaitForPlaybackPacingPolicy>(fx.Gameplay.Pacing);
            Assert.False(pacing.IsPlaybackFinished, "探针返回 true 时应关闭节奏门（playing_back）");
            Assert.True(
                fx.Gameplay.AppState.CurrentSubState.HasValue &&
                fx.Gameplay.AppState.CurrentSubState.Value.Equals(fx.Gameplay.PlayingBackSubState),
                "有待回放动作时应停在 playing_back 子态");

            // 节奏门仍关闭：与生产接线一致（GameFoundationBootstrap.OnFixedStep 同款判断），调用方
            // 应先查 IsPlaybackFinished 再决定是否调用 Advance；这里不重复调用 Advance，只验证状态
            // 保持关闭，直到显式解除。
            Assert.False(pacing.IsPlaybackFinished);

            // 模拟表现层把队列播放完毕（真实场景下 PlaybackQueue.Finished 触发时 PendingCount 已经
            // 归零，探针自然改口）之后才发出 presentation.playback_finished → NotifyPlaybackFinished。
            hasPending = false;
            fx.Gameplay.NotifyPlaybackFinished();
            Assert.True(pacing.IsPlaybackFinished, "NotifyPlaybackFinished 后节奏门应解除");

            fx.Gameplay.Advance(0);
            Assert.False(
                fx.Gameplay.AppState.CurrentSubState.HasValue &&
                fx.Gameplay.AppState.CurrentSubState.Value.Equals(fx.Gameplay.PlayingBackSubState),
                "节奏门解除后 Advance 应继续推进，不再停在 playing_back");
        }

        /// <summary>连续多步混合：同一场战斗里，探针在"有反馈"与"零反馈"之间切换，验证
        /// WaitForPlaybackPacingPolicy 每步都重新读取当前探针值（不缓存上一步的判定结果）——零反馈的
        /// 步骤照常直接推进，有反馈的步骤仍然正确停下等待。</summary>
        [Fact]
        public void DiscretePacing_MixedFeedbackAndZeroFeedbackSteps_GatesOnlyWhenPending()
        {
            var fx = Build(actionPointsPerTurn: 1, pacingPolicy: new WaitForPlaybackPacingPolicy());
            var hasPending = false;
            fx.Gameplay.SetPendingPlaybackProbe(() => hasPending);
            var pacing = Assert.IsType<WaitForPlaybackPacingPolicy>(fx.Gameplay.Pacing);

            // 第一步：零反馈，应直接推进到下一次轮到玩家等待输入，不停在 playing_back。
            fx.AdvanceUntilAwaitingPlayerInput();
            fx.SubmitPlayerCast(SkillCheap);
            fx.Gameplay.Advance(0);
            Assert.True(pacing.IsPlaybackFinished, "第一步（零反馈）之后节奏门应保持开启");

            // 第二步：切到"有反馈"，应停在 playing_back，直到手动解除。
            hasPending = true;
            fx.SubmitPlayerCast(SkillCheap);
            fx.Gameplay.Advance(0);
            Assert.False(pacing.IsPlaybackFinished, "第二步（有反馈）应关闭节奏门");
            fx.Gameplay.NotifyPlaybackFinished();
            Assert.True(pacing.IsPlaybackFinished);

            // 第三步：切回零反馈，应再次直接推进，不需要 NotifyPlaybackFinished。
            hasPending = false;
            fx.Gameplay.Advance(0); // 上一步已停在 playing_back 解除后回到 awaiting_input，需要新意图。
            fx.SubmitPlayerCast(SkillCheap);
            fx.Gameplay.Advance(0);
            Assert.True(pacing.IsPlaybackFinished, "第三步（零反馈）之后节奏门应再次保持开启");
        }
    }
}
