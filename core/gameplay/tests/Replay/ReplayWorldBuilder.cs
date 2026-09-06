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
using Core.Numbers.PowerSet;
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
    /// 判断记录已作废（离散场景不经 <c>TurnScheduler</c>，本次改动前的做法）：此前
    /// <c>core/foundation/save_system/tests/DiscreteReplayTests.cs</c>"判断记录（不经
    /// TurnScheduler，手工交替行动者）"指出——<c>sim.turn_started</c>/<c>turn_ended</c>/
    /// <c>round_ended</c> 三个事件由 <c>TurnScheduler</c> 自己 <c>PublishImmediate</c>，不经过
    /// <see cref="IWorldSim.Tick"/>，"回放 = 重放 SimStep 序列给 world.Tick"这一（旧）机制天然覆盖
    /// 不到；本类因此此前不装配 <c>TurnScheduler</c>，改由录制脚本手工交替产生
    /// <see cref="SimStep.Discrete"/>。离散模式回放完整性任务（04/10 号文档"回放=意图序列"、
    /// 03 §3.2 步骤 6）改用 <c>Core.Foundation.SaveSystem.IReplayPlayer.LoadDiscrete</c>——重放
    /// 时真正持有并驱动一个 <c>TurnScheduler</c>（见 <see cref="BuildDiscreteWorldWithScheduler"/>），
    /// <c>sim.turn_*</c>/<c>sim.round_ended</c> 三个事件在"直跑"与"经录像重放"两条路径下都由这个
    /// （各自独立构造、但被同一份录像/确定性输入驱动的）<c>TurnScheduler</c> 发出，天然对齐，不再
    /// 需要"回避这三个事件"这条折中；旧判断记录的技术前提仍然成立（<c>TurnScheduler</c> 确实不经
    /// <c>world.Tick</c> 发事件），只是应对方式从"回避"改成了"重放侧也真正驱动一个调度器"。
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
                { ""id"": ""stat.healing_done_pct"", ""name_key"": ""l10n.stat.healing_done_pct.name"", ""group"": ""secondary"", ""default_base"": 0 },
                { ""id"": ""stat.replay_initiative"", ""name_key"": ""l10n.stat.replay_initiative.name"", ""group"": ""secondary"", ""default_base"": 0 }
            ]
        }";

        /// <summary>离散模式回放完整性任务新增：<c>TurnScheduler</c>（<c>initiative_stat</c> 策略）用
        /// 的先攻属性——两个单位注册后各自 <c>SetBase</c> 成不同值（玩家更高，先手），保证行动顺序
        /// 由 <c>TurnScheduler</c> 自己按先攻规则算出来，不是脚本硬编码的"奇数步玩家、偶数步 NPC"
        /// （见 <see cref="BuildDiscreteWorldWithScheduler"/> 判断记录）。</summary>
        public static readonly Id InitiativeStat = new Id("stat.replay_initiative");
        public const double PlayerInitiative = 20.0;
        public const double NpcInitiative = 10.0;

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

        /// <summary>F6 收口新增：世界 → 该世界所属 <see cref="PowerHost"/> 的弱引用映射，供
        /// <see cref="PowersOf"/>/<see cref="HpState"/> 在 <see cref="BuildCommon"/> 之外按已构造好
        /// 的 <see cref="IWorldSim"/> 反查——用弱引用是因为本类型是静态类，不随任何一次测试用例的
        /// 世界生命周期回收，若用普通 <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>
        /// 会造成每个测试用例构造的世界都被本静态字段永久强引用、内存只增不减。</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IWorldSim, PowerHost> PowersByWorld =
            new System.Runtime.CompilerServices.ConditionalWeakTable<IWorldSim, PowerHost>();

        /// <summary>F6 收口新增：取某个由本类型构造的世界对应的 <see cref="PowerHost"/>（供
        /// <see cref="HpState"/>/回放摘要测试按实体查 HP，见 <see cref="ReplayBaselineTests"/>）。
        /// <paramref name="world"/> 必须是本类型某个 Build* 方法返回过的世界，否则抛异常——不静默
        /// 返回 null，调用方传错世界属于用法错误，应立即暴露。</summary>
        public static PowerHost PowersOf(IWorldSim world)
        {
            if (!PowersByWorld.TryGetValue(world, out var powers))
            {
                throw new InvalidOperationException("PowersOf: world 不是本类型构造过的世界（未在 PowersByWorld 登记）");
            }

            return powers;
        }

        /// <summary>F6 收口新增：某实体当前 HP 的稳定文本形式，供
        /// <see cref="Core.Foundation.SaveSystem.WorldSnapshot.Capture"/> 的
        /// <c>entityStateProvider</c> 使用——实体未注册该资源类型（理论上不会发生，本夹具的两个
        /// 单位都注册了 <see cref="PowerHealth"/>）时返回空列表，不抛异常（Capture 对空列表的处理
        /// 等价于"这个实体没有额外状态字段"）。</summary>
        public static IReadOnlyList<string> HpState(IWorldSim world, Id unitId)
        {
            var powers = PowersOf(world);
            return powers.HasPower(unitId, PowerHealth)
                ? new[] { powers.GetPower(unitId, PowerHealth).ToString("R", CultureInfo.InvariantCulture) }
                : Array.Empty<string>();
        }

        /// <summary>P1-04 收口：世界工厂现在必须诚实使用调用方传入的 <paramref name="masterSeed"/>
        /// 构造 <see cref="IRngHost"/>（见 <c>Core.Foundation.SaveSystem.WorldFactory</c> 判断记录
        /// ——录制起点之后才第一次被访问的流依赖这个主种子派生），此前本方法恒用 <c>0UL</c>；本仓库
        /// 现有全部调用点本来就固定传 <c>0UL</c>（不使用命中表随机分支，见类型顶部判断记录"命中表
        /// 全部分支禁用"），因此改动不影响任何既有测试结果，只是让代码不再对参数说谎。</summary>
        private static (WorldSim World, WorldUnitAccessBundle Units, RulesAssembly Rules) BuildCommon(ulong masterSeed, IEventBus bus)
        {
            var (registry, _) = BuildRegistry(bus);
            var rng = new RngHost(masterSeed);
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

            PowersByWorld.Add(world, rules.Powers);

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
            var (world, _, rules) = BuildCommon(masterSeed, bus);
            return (world, rules.Rng);
        }

        /// <summary>
        /// <c>Core.Foundation.SaveSystem.DiscreteWorldFactory</c> 形状（离散模式回放完整性任务
        /// 新增，取代此前"不装配 TurnScheduler、录制脚本手工交替行动者"的做法，见本类型顶部旧
        /// 判断记录"离散场景不经 TurnScheduler"——该判断记录随本次改动一并作废）：数据/单位与
        /// <see cref="BuildContinuousWorld"/> 相同，额外装配一个真正的
        /// <see cref="TurnScheduler"/>（<c>initiative_stat</c> 策略）并 <c>BeginCombat</c>——行动
        /// 顺序从此由 <see cref="TurnScheduler"/> 按先攻属性（<see cref="InitiativeStat"/>，玩家
        /// <see cref="PlayerInitiative"/> &gt; NPC <see cref="NpcInitiative"/>，先手）自己算出来，
        /// 不再是脚本硬编码的"奇数步玩家、偶数步 NPC"。
        /// <para>
        /// 判断记录（双方都是"外部输入"行动者，不接 AI）：<paramref name="isExternalActor"/>（本方法
        /// 内联的 <c>IsPlayerActor</c> 委托，见 <see cref="TurnScheduler"/> 构造函数同名参数）对
        /// <see cref="PlayerId"/>/<see cref="NpcId"/> 都返回 <c>true</c>——本夹具刻意不接
        /// <c>RulesAssembly</c> 的 AI 决策管线（<c>AiTickHandler</c>，见类型顶部"固定脚本"注释：
        /// 双方都是脚本直接提交 <c>cast</c> 意图，不依赖 AI），"NPC 也需要外部输入"只是意味着
        /// "谁来决定 NPC 这一步做什么"这件事由脚本／录像扮演，而不是由真正的 AI 扮演；这不影响本
        /// 方法要验证的核心性质——<b>行动顺序</b>本身仍然是 <see cref="TurnScheduler"/> 按先攻规则
        /// 真实算出来的，录像只提供"轮到某个行动者时它具体做什么"，不提供"轮到谁"。
        /// </para>
        /// </summary>
        public static (IWorldSim World, IRngHost Rng, TurnScheduler Scheduler) BuildDiscreteWorldWithScheduler(ulong masterSeed, IEventBus bus)
        {
            var (world, _, rules) = BuildCommon(masterSeed, bus);

            rules.Stats.SetBase(PlayerId, InitiativeStat, PlayerInitiative);
            rules.Stats.SetBase(NpcId, InitiativeStat, NpcInitiative);

            double InitiativeProvider(Id unitId) => rules.Stats.GetStat(unitId, InitiativeStat);
            bool IsExternalActor(Id unitId) => true;

            var scheduler = new TurnScheduler(world, InitiativeProvider, IsExternalActor, bus);
            scheduler.Configure(InitiativePolicy.InitiativeStat, new Dictionary<string, object>(StringComparer.Ordinal));
            scheduler.BeginCombat(new[] { PlayerId, NpcId });

            return (world, rules.Rng, scheduler);
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
            recorder.BeginRecording(0UL, new Dictionary<string, RngStreamState>(StringComparer.Ordinal));

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

        /// <summary>离散行动者这一步该释放哪个技能——玩家/NPC 各自固定一招（惯例同此前的手工
        /// 交替脚本），供 <see cref="RunDiscreteFixedScript"/>/<see cref="RecordDiscreteFight"/>
        /// 共用，保证"直跑"与"录制"两条路径的决策逻辑完全一致（唯一差别是要不要额外记一份
        /// 录像）。</summary>
        private static Id SkillFor(Id actorId) => actorId.Equals(PlayerId) ? SkillStrike : SkillBite;

        /// <summary>直接驱动一遍固定离散脚本（不经录像），供"直跑"侧验证使用（离散模式回放完整性
        /// 任务改写：不再手工交替行动者，改由 <see cref="BuildDiscreteWorldWithScheduler"/> 装配的
        /// 真实 <see cref="TurnScheduler"/> 决定"轮到谁"——与
        /// <see cref="Core.Gameplay.Assembly.GameplayAssembly.Advance"/> 同一驱动算法（<c>NextStep</c>
        /// 非空即 <c>Tick</c>+<c>NotifyStepConsumed</c>；为空即代表轮到的行动者需要外部输入，本方法
        /// 用 <see cref="SkillFor"/> 固定给出这一步的技能选择），只是本方法在离线测试夹具里原样
        /// 内联这段算法，不经过 L4 的 <c>GameplayAssembly</c> 本身，见
        /// <c>Core.Foundation.SaveSystem.IReplayPlayer.LoadDiscrete</c> 判断记录同一分层理由）。</summary>
        public static (IWorldSim World, IEventBus Bus, InMemoryEventAudit Audit, long FinalTick) RunDiscreteFixedScript()
        {
            var (bus, audit) = CreateAuditedBus();
            var (world, _, scheduler) = BuildDiscreteWorldWithScheduler(0UL, bus);

            long ticksAdvanced = 0;
            while (ticksAdvanced < DiscreteFixedSteps)
            {
                var step = scheduler.NextStep();
                if (step == null)
                {
                    var actorId = scheduler.GetCurrentActor()!.Value;
                    scheduler.SubmitIntent(actorId, new Intent(actorId, "cast", CastArgs(SkillFor(actorId))));
                    continue;
                }

                world.Tick(step.Value);
                scheduler.NotifyStepConsumed(step.Value.ActorId!.Value);
                ticksAdvanced++;
            }

            return (world, bus, audit, ticksAdvanced);
        }

        /// <summary>录制一遍固定离散脚本，产出 <see cref="ReplayData"/>（"录像"）——与
        /// <see cref="RunDiscreteFixedScript"/> 完全同一套决策逻辑（<see cref="SkillFor"/>），额外
        /// 在每次"轮到的行动者需要外部输入"时用 <see cref="ReplayRecorder.RecordInput"/> 记下
        /// "第几个 tick、哪个行动者、提交了什么意图"这个决策时刻（不记录"这个 tick 到底是谁的回合"
        /// ——那是 <c>TurnScheduler</c> 的内部决策，见 <c>IReplayPlayer.LoadDiscrete</c> 判断记录）；
        /// <see cref="ReplayRecorder.RecordStep"/> 仍然照记（诊断用途，见 <see cref="IReplayRecorder.RecordStep"/>
        /// 判断记录），不影响 <see cref="ReplayPlayer.LoadDiscrete"/> 播放路径。</summary>
        public static ReplayData RecordDiscreteFight()
        {
            var bus = CreateAuditedBus().Bus;
            var (world, _, scheduler) = BuildDiscreteWorldWithScheduler(0UL, bus);
            var recorder = new ReplayRecorder(StepSeconds);
            recorder.BeginRecording(0UL, new Dictionary<string, RngStreamState>(StringComparer.Ordinal));

            long ticksAdvanced = 0;
            while (ticksAdvanced < DiscreteFixedSteps)
            {
                var step = scheduler.NextStep();
                if (step == null)
                {
                    var actorId = scheduler.GetCurrentActor()!.Value;
                    var tickNumber = ticksAdvanced + 1;
                    var skillId = SkillFor(actorId);
                    recorder.RecordInput(tickNumber, new ReplayInputRecord(tickNumber, actorId, "cast", CastArgs(skillId)));
                    scheduler.SubmitIntent(actorId, new Intent(actorId, "cast", CastArgs(skillId)));
                    continue;
                }

                recorder.RecordStep(ticksAdvanced + 1, step.Value); // 诊断信息，见方法注释。
                world.Tick(step.Value);
                scheduler.NotifyStepConsumed(step.Value.ActorId!.Value);
                ticksAdvanced++;
            }

            recorder.SetTickCount(ticksAdvanced);
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
