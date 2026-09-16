using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Ai;
using Core.Rules.Combat;
using Core.Rules.Common;

namespace Core.Sim
{
    /// <summary>单场战斗的结果——胜负、时长、双方伤害与命中统计、技能输出占比、资源曲线、TTD 估计。
    /// 见 <see cref="FightRunner"/> 判断记录"采样口径"。</summary>
    public sealed class FightResult
    {
        public FightOutcome Outcome { get; }

        /// <summary>战斗时长（模拟秒）= 实耗 tick 数 × <see cref="FightRunnerOptions.StepSeconds"/>。</summary>
        public double DurationSeconds { get; }

        public int TicksUsed { get; }

        public double PlayerTotalDamage { get; }

        /// <summary><see cref="PlayerTotalDamage"/> ÷ <see cref="DurationSeconds"/>；
        /// <see cref="DurationSeconds"/> 为 0 时为 0（不除零）。</summary>
        public double PlayerDps { get; }

        public double CreatureTotalDamage { get; }

        public double CreatureDps { get; }

        /// <summary>玩家对生物的命中率——见类型判断记录"命中率口径"。</summary>
        public double PlayerHitRate { get; }

        /// <summary>T-N6-4b 新增：生物对玩家的命中率，口径与 <see cref="PlayerHitRate"/> 对称（分子＝
        /// <c>ResolveTrace</c> 里 <c>Hit ∉ {Miss,Dodge,Parry}</c> 的计数、分母＝该方向全部
        /// <c>ResolveTrace</c> 回调计数）——诊断"生物越级矩阵为何在某些格子胜率骤变"时定位
        /// 未命中/命中占比用，见 <c>ArenaSimulation</c>/<c>core/sim/tests/data/README.md</c>
        /// "T-N6-4b 根因排查"一节。</summary>
        public double CreatureHitRate { get; }

        /// <summary>技能 id → 该技能落地伤害占玩家总落地伤害的比例，之和为 1（<see cref="PlayerTotalDamage"/>
        /// 为 0 时为空字典，之和为 0，见类型判断记录"技能占比之和恒为 1 的前提"）。</summary>
        public IReadOnlyDictionary<Id, double> PlayerSkillDamageShare { get; }

        /// <summary>玩家逐资源类型的资源曲线（下采样到 ≤ <see cref="FightRunnerOptions.MaxResourceCurveSamples"/> 点）。</summary>
        public IReadOnlyDictionary<Id, IReadOnlyList<ResourceSample>> PlayerResourceCurves { get; }

        public IReadOnlyDictionary<Id, IReadOnlyList<ResourceSample>> CreatureResourceCurves { get; }

        /// <summary>玩家最大生命 ÷ 生物观测秒伤（<see cref="CreatureDps"/>）——生物本场未造成任何伤害时为
        /// <see cref="double.PositiveInfinity"/>（"按当前观测，生物永远杀不死玩家"，不是错误）。</summary>
        public double TtdEstimate { get; }

        /// <summary>玩家最大生命（<c>IPowerHost.GetPowerMax(playerId, WellKnownPowers.Health)</c>，
        /// 战斗结束时刻的快照）——同一 (职业,等级,期望品质) 组合下与种子无关（<see cref="StandardPlayerBuilder"/>
        /// 生成的装备/属性不掷骰），供 <see cref="ArenaSimulation"/> 对账表"hp: 玩家最大生命对
        /// anchor.hp"一行直接读取，不需要重新装配一次标准玩家。</summary>
        public double PlayerMaxHealth { get; }

        public Id PlayerUnitId { get; }

        public Id CreatureUnitId { get; }

        internal FightResult(
            FightOutcome outcome, double durationSeconds, int ticksUsed,
            double playerTotalDamage, double creatureTotalDamage, double playerHitRate, double creatureHitRate,
            IReadOnlyDictionary<Id, double> playerSkillDamageShare,
            IReadOnlyDictionary<Id, IReadOnlyList<ResourceSample>> playerResourceCurves,
            IReadOnlyDictionary<Id, IReadOnlyList<ResourceSample>> creatureResourceCurves,
            double playerMaxHealth, Id playerUnitId, Id creatureUnitId)
        {
            Outcome = outcome;
            DurationSeconds = durationSeconds;
            TicksUsed = ticksUsed;
            PlayerTotalDamage = playerTotalDamage;
            PlayerDps = durationSeconds > 0 ? playerTotalDamage / durationSeconds : 0.0;
            CreatureTotalDamage = creatureTotalDamage;
            CreatureDps = durationSeconds > 0 ? creatureTotalDamage / durationSeconds : 0.0;
            PlayerHitRate = playerHitRate;
            CreatureHitRate = creatureHitRate;
            PlayerSkillDamageShare = playerSkillDamageShare;
            PlayerResourceCurves = playerResourceCurves;
            CreatureResourceCurves = creatureResourceCurves;
            TtdEstimate = CreatureDps > 0 ? playerMaxHealth / CreatureDps : double.PositiveInfinity;
            PlayerMaxHealth = playerMaxHealth;
            PlayerUnitId = playerUnitId;
            CreatureUnitId = creatureUnitId;
        }
    }

    public enum FightOutcome
    {
        /// <summary>玩家击杀生物，自己仍存活。</summary>
        PlayerWin,

        /// <summary>生物击杀玩家。</summary>
        CreatureWin,

        /// <summary>双方在同一 tick 互相击杀（极端场景，见判断记录）。</summary>
        Draw,

        /// <summary>达到 <see cref="FightRunnerOptions.MaxTicks"/> 仍未分出胜负。</summary>
        Timeout,
    }

    /// <summary>一次资源采样点：<see cref="Tick"/>（第几个 tick，从 1 起）与该 tick 结束时的资源当前值
    /// （<c>IPowerHost.GetPower</c> 原始值，非百分比——任务书"每 tick 记录双方资源当前值"）。</summary>
    public readonly struct ResourceSample
    {
        public int Tick { get; }

        public double Value { get; }

        public ResourceSample(int tick, double value)
        {
            Tick = tick;
            Value = value;
        }
    }

    /// <summary>单场战斗的输入。</summary>
    public sealed class FightRunnerOptions
    {
        public IReadOnlyList<IDataSource> DataSources { get; set; } = Array.Empty<IDataSource>();

        public Id MapId { get; set; } = new Id("world.sim_arena");

        public Id PlayerId { get; set; } = new Id("unit.sim_fight_player");

        public Id PlayerFactionId { get; set; } = new Id("fac.player");

        public Id GameId { get; set; } = new Id("game.sim_fight");

        public double StepSeconds { get; set; } = 0.5;

        public bool FailOnUnknownTable { get; set; }

        public Id ClassId { get; set; }

        public int PlayerLevel { get; set; }

        public Id QualityId { get; set; }

        /// <summary>缺省按 <see cref="StandardPlayerBuilder"/> 命名约定推断。</summary>
        public Id? RotationId { get; set; }

        public Id CreatureId { get; set; }

        public int CreatureLevel { get; set; }

        /// <summary>生物出生点相对玩家出生点（<see cref="HeadlessWorldOptions.PlayerSpawnPosition"/>
        /// 默认 <c>(0,0)</c>）的偏移，默认 <c>(3,0)</c>——同 <c>SimTestWorldFactory.RunEmbeddedFightScript</c>
        /// 既有惯例，落在本数据集全部主动攻击技能的射程（5）之内，绝大多数战斗不需要触发
        /// <see cref="SimpleMoveModel"/> 就能立即交手；越级矩阵等场景仍可能因为不同数据集技能射程
        /// 更短而需要移动，<see cref="SimpleMoveModel"/> 因此仍是必要的兜底，不是摆设。</summary>
        public Vec2 CreatureSpawnOffset { get; set; } = new Vec2(3, 0);

        public ulong Seed { get; set; }

        public int MaxTicks { get; set; } = 1200;

        public double MoveSpeed { get; set; } = SimpleMoveModel.DefaultMoveSpeed;

        public int MaxResourceCurveSamples { get; set; } = 64;
    }

    /// <summary>
    /// T-N6-4（ADR-0035 决策 3）：单场战斗仿真运行器——标准玩家对指定生物模板按指定等级出生，按
    /// <c>ai.rotation</c> 智能释放优先级表逐 tick 交手直至一方死亡或超时，采样命中/伤害/资源曲线，
    /// 输出 <see cref="FightResult"/>。
    /// <para>
    /// 判断记录（隔离方案：每场新建一个 <see cref="HeadlessWorld"/>，不复用）：任务书要求"先测量
    /// <c>HeadlessWorldBuilder.Build</c> 耗时，≤50ms 则每场新建世界，否则同一世界内重生重置并说明
    /// RNG 流如何按种子重置"。实测（见 <c>core/sim/tests/FightRunnerPerfTests.cs</c>，20 次连续
    /// <c>SimTestWorldFactory.BuildFromEmbeddedDataset</c> 取平均）远低于 50ms 这条线（多数运行环境
    /// 个位数毫秒），因此选择更简单、风险更低的方案：每场调用 <see cref="Run"/> 都经
    /// <see cref="HeadlessWorldBuilder.Build"/> 新建一整套世界（新 <c>DataRegistry</c>/
    /// <c>RngHost</c>/<c>WorldSim</c>/<c>GameplayAssembly</c>），战斗结束后整个世界连同其内部状态
    /// 一起丢弃。这避免了"同一世界内重生重置"方案必须解决的一整类问题——光环/仇恨/冷却/AI 行为状态
    /// 如何清零，任务书原文"禁止给被仿真模块加重置功能"——本方案完全不需要触碰这些模块的任何重置
    /// 能力，新世界天生是"干净"的。代价是仿真吞吐量受限于"重新加载数据 + 重新生成标准玩家"的固定
    /// 开销（远大于"只重置几个宿主"），<see cref="ArenaSimulation"/> 因此把测试用参数（<c>runs</c>/
    /// <c>levels</c>/<c>level_offsets</c>）缩小以控制总耗时，见该类型判断记录。
    /// </para>
    /// <para>
    /// 判断记录（种子如何传导到 RNG）：<see cref="FightRunnerOptions.Seed"/> 原样传给
    /// <see cref="HeadlessWorldOptions.Seed"/>，经 <see cref="Core.Foundation.Rng.RngHost"/> 的既有
    /// "主种子 + 流 id 派生初始状态"机制（<c>SeedDerivation.DeriveInitialState</c>）分流到命中判定/
    /// AI 决策等各自的随机流——本类型不直接触碰 <c>IRngHost</c>，只是把 <see cref="FightRunnerOptions.Seed"/>
    /// 转发给装配根的 <c>Seed</c> 选项，同一 <see cref="FightRunnerOptions.Seed"/> 两次独立 <see cref="Run"/>
    /// 逐 tick 完全确定（复用 T-N6-1 已证明的"同种子两次 Build 逐 tick 一致"结论，本类型只是在其上跑
    /// 一段固定脚本，不引入任何额外不确定性来源——不使用系统时钟/线程调度顺序等）。多种子分布/矩阵扫描
    /// 的种子派生方式见 <see cref="ArenaSimulation"/> 判断记录"种子派生"。
    /// </para>
    /// <para>
    /// 判断记录（采样口径：<c>combat.damage_dealt</c> 与 <c>CombatOptions.ResolveTrace</c> 各司其职，
    /// 不是同一份数据的两次重复采样）：<c>combat.damage_dealt</c>（<c>Resolver.Resolve</c> 只在
    /// <c>!immune</c> 分支才 <c>Enqueue</c>，<c>Miss</c>/<c>Dodge</c>/<c>Parry</c> 在更早的"terminal"
    /// 分支直接返回、根本不会走到这一步，见该方法源码）——只承载"确实落地的一次伤害"，天然适合直接
    /// 累加成 <see cref="FightResult.PlayerTotalDamage"/>/<see cref="FightResult.CreatureTotalDamage"/>
    /// （字段含 <c>sourceId</c>/<c>targetId</c>/<c>amount</c>，逐 tick 从 <c>HeadlessWorld.Events</c>
    /// 扫描新增的 <c>CombatDamageDealtEvent</c> 即可，同 <c>SimTestWorldFactory</c> 既有
    /// "eventsBefore/之后新增事件"惯例）。但它不携带 <c>skillId</c>（事件契约本就没有这个字段），也
    /// 不覆盖 <c>Miss</c>/<c>Dodge</c>/<c>Parry</c>/<c>Immune</c> 这些"打空了"的结算——若命中率只用
    /// 这份数据的计数当分子、另一份数据当分母，两者天然一致，但命中率**本身**（多少次尝试里有多少次
    /// 真正落地）必须知道"总尝试次数"，这份信息只有 <see cref="CombatOptions.ResolveTrace"/>（对
    /// <em>全部</em> <c>Resolver.Resolve</c> 调用无条件回调，含 Miss/Dodge/Parry/Immune）能提供；
    /// <see cref="EffectContext.SkillId"/> 同样只有经 <c>ResolveTrace</c> 拿到的 <c>EffectContext</c>
    /// 才有（<c>combat.damage_dealt</c> 事件没有）。本类型因此：<see cref="FightResult.PlayerHitRate"/>
    /// 分子＝落地事件计数、分母＝<c>ResolveTrace</c> 总回调计数（玩家→生物方向，排除治疗分支——本数据
    /// 集玩家技能没有治疗效果，此判据是面向未来数据集的防御性排除）；
    /// <see cref="FightResult.PlayerSkillDamageShare"/> 完全经 <c>ResolveTrace</c> 按
    /// <c>EffectContext.SkillId</c> 分组累加 <c>ResolveResult.FinalAmount</c>（<c>Immune</c>/
    /// <c>Miss</c> 等 <c>FinalAmount</c> 恒为 0，天然不贡献份额，不需要额外过滤）。两份数据在"确实
    /// 落地"的交集上逐条一致（同一次结算，<c>combat.damage_dealt.Amount</c> ==
    /// <c>ResolveResult.FinalAmount</c>），可以互相校验但不是同一份数据白白采两遍——各自承担各自
    /// 唯一能提供的那部分信息。
    /// </para>
    /// <para>
    /// 判断记录（技能占比之和恒为 1 的前提）：<see cref="FightResult.PlayerSkillDamageShare"/> 只在
    /// <see cref="FightResult.PlayerTotalDamage"/> &gt; 0 时按份额归一化（分母是玩家总落地伤害，任一
    /// 场景只要玩家命中过至少一次就恒 &gt; 0）；<see cref="FightResult.PlayerTotalDamage"/> == 0（玩家
    /// 全程未造成任何伤害——理论上只会发生在武器/技能完全无法命中生物这种数据错误场景）时返回空字典，
    /// 之和为 0 而非 1，调用方（<see cref="ArenaSimulation"/>/测试）不应该对这种退化场景断言"之和为
    /// 1"，应先保证战斗本身能正常出伤害。
    /// </para>
    /// <para>
    /// 判断记录（移动模型的接入点：优先级表选不出技能才移动，不是每 tick 都判定距离）：
    /// <see cref="RotationEvaluator.Evaluate"/> 内部已经用 <c>ISkillHost.CastSkill</c> 的
    /// <c>CastFailureReason.OutOfRange</c> 隐式处理了"射程不够就跳过这一条、看下一条"，本类型不重复
    /// 判定距离——只有当 <em>全部</em> 候选条目都被跳过（<c>Evaluate</c> 返回 <c>null</c>，可能是射程
    /// 不够、也可能是没有任何条件成立/全部在冷却）时，才调用 <see cref="SimpleMoveModel.Step"/> 尝试
    /// 缩短与目标的距离——这保证"能打就打，打不到才动"，与真实玩家的直觉行为一致，也避免了"每 tick
    /// 都强制寻路"这种与优先级表决策脱节的移动节奏。
    /// </para>
    /// </summary>
    public static class FightRunner
    {
        /// <summary>无法从 <c>ai.rotation</c> 反查出任何非零射程时的兜底交手距离——本数据集全部主动
        /// 攻击技能射程均为 5（见 <c>core/sim/tests/data/skill/skill.def.json</c>），选它作为通用
        /// 兜底而不是 0（0 会导致目标必须与玩家完全重合才能交手，不现实）。</summary>
        public const double DefaultEngageRange = 5.0;

        /// <summary>
        /// T-N6-5 新增：一次 <see cref="Run"/> 调用内部"单场战斗结算追踪状态 + <see cref="CombatOptions"/>
        /// 绑定"的可复用载体——从 <see cref="Run"/> 原有的局部闭包（<c>OnResolve</c>/若干局部计数变量）
        /// 抽出为独立类型，供 <see cref="RunWithinWorld"/>（成长仿真复用同一世界打多场战斗）与
        /// <see cref="Run"/> 自身共用。<see cref="Run"/> 每次调用仍各自新建一份（其"每场新建世界"隔离
        /// 方案不变，见类型判断记录"隔离方案"），<see cref="RunWithinWorld"/> 的调用方（<see
        /// cref="Core.Sim.GrowthSimulation"/>）在整条成长轨迹里新建一份、随世界一起长期存活，每场战斗
        /// 前调用 <see cref="BeginFight"/> 重置计数并切换追踪目标——<see cref="CombatOptions"/> 只在
        /// <see cref="Core.Sim.HeadlessWorldBuilder.Build"/> 时绑定一次（<c>ResolveTrace</c> 委托指向
        /// 本类型的 <see cref="OnResolve"/> 方法，之后只读它的可变字段，不需要为每场战斗重新装配世界）。
        /// </summary>
        public sealed class FightAccumulator
        {
            private Id _playerId;
            private Id _creatureId;
            private bool _active;

            public double PlayerTotalDamage { get; private set; }
            public double CreatureTotalDamage { get; private set; }
            public int PlayerAttempts { get; private set; }
            public int PlayerLanded { get; private set; }
            public int CreatureAttempts { get; private set; }
            public int CreatureLanded { get; private set; }
            public Dictionary<Id, double> PlayerDamageBySkill { get; } = new Dictionary<Id, double>();

            /// <summary>本追踪状态对外暴露的 <see cref="CombatOptions"/>——<see
            /// cref="HeadlessWorldOptions.CombatOptions"/> 只需要在世界装配时设一次。</summary>
            public CombatOptions CombatOptions { get; }

            public FightAccumulator()
            {
                CombatOptions = new CombatOptions { ResolveTrace = OnResolve };
            }

            /// <summary>开始追踪一场新战斗：清空全部计数、切换 <paramref name="playerId"/>/<paramref
            /// name="creatureId"/> 归属判定目标。<see cref="Run"/>/<see cref="RunWithinWorld"/> 均须在
            /// 进入逐 tick 循环之前调用一次。</summary>
            public void BeginFight(Id playerId, Id creatureId)
            {
                _playerId = playerId;
                _creatureId = creatureId;
                _active = true;
                PlayerTotalDamage = 0.0;
                CreatureTotalDamage = 0.0;
                PlayerAttempts = 0;
                PlayerLanded = 0;
                CreatureAttempts = 0;
                CreatureLanded = 0;
                PlayerDamageBySkill.Clear();
            }

            private static bool IsLandedHit(HitResult hit) =>
                hit != HitResult.Miss && hit != HitResult.Dodge && hit != HitResult.Parry;

            private void OnResolve(EffectContext ctx, ResolveResult result)
            {
                if (!_active || result.IsHeal) return;

                if (ctx.SourceId.Equals(_playerId) && ctx.TargetId.Equals(_creatureId))
                {
                    PlayerAttempts++;
                    if (IsLandedHit(result.Hit)) PlayerLanded++;
                    PlayerDamageBySkill.TryGetValue(ctx.SkillId, out var existing);
                    PlayerDamageBySkill[ctx.SkillId] = existing + result.FinalAmount;
                }
                else if (ctx.SourceId.Equals(_creatureId) && ctx.TargetId.Equals(_playerId))
                {
                    CreatureAttempts++;
                    if (IsLandedHit(result.Hit)) CreatureLanded++;
                }
            }

            /// <summary><see cref="HeadlessWorld.Events"/>（<paramref name="events"/>）里下标
            /// <c>[eventsFrom, events.Count)</c> 区间新增的 <see cref="CombatDamageDealtEvent"/> 累加进
            /// <see cref="PlayerTotalDamage"/>/<see cref="CreatureTotalDamage"/>——与 <see
            /// cref="OnResolve"/> 各自承担各自唯一能提供的信息，见 <see cref="FightRunner"/> 判断记录
            /// "采样口径"，本方法只是把原来内联在 <see cref="Run"/> 循环体里的这几行搬到这里，供
            /// <see cref="RunWithinWorld"/> 复用。</summary>
            public void AccumulateDamageEvents(IReadOnlyList<Core.Foundation.EventBus.IEvent> events, int eventsFrom)
            {
                for (var i = eventsFrom; i < events.Count; i++)
                {
                    if (events[i] is CombatDamageDealtEvent dealt)
                    {
                        if (dealt.SourceId.Equals(_playerId) && dealt.TargetId.Equals(_creatureId))
                        {
                            PlayerTotalDamage += dealt.Amount;
                        }
                        else if (dealt.SourceId.Equals(_creatureId) && dealt.TargetId.Equals(_playerId))
                        {
                            CreatureTotalDamage += dealt.Amount;
                        }
                    }
                }
            }
        }

        public static FightResult Run(FightRunnerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            var accumulator = new FightAccumulator();

            var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
            {
                DataSources = options.DataSources,
                Seed = options.Seed,
                MapId = options.MapId,
                PlayerId = options.PlayerId,
                PlayerFactionId = options.PlayerFactionId,
                PlayerClassId = options.ClassId,
                PlayerLevel = options.PlayerLevel,
                PlayerSpawnPosition = Vec2.Zero,
                GameId = options.GameId,
                StepSeconds = options.StepSeconds,
                FailOnUnknownTable = options.FailOnUnknownTable,
                CombatOptions = accumulator.CombatOptions,
            });

            var standardPlayer = StandardPlayerBuilder.Build(
                world, options.ClassId, options.PlayerLevel, options.QualityId, options.RotationId);

            var creatureSpawnPos = Vec2.Zero + options.CreatureSpawnOffset;
            var creatureId = world.Gameplay.Carriers.Creatures.Spawn(
                options.CreatureId, options.MapId, creatureSpawnPos, facing: Math.PI, ownerId: null, options.CreatureLevel);
            world.Spatial.Register(creatureId, creatureSpawnPos, 0.5);

            return RunWithinWorld(
                world, accumulator, options.PlayerId, creatureId, standardPlayer.RotationId,
                options.StepSeconds, options.MaxTicks, options.MoveSpeed, options.MaxResourceCurveSamples);
        }

        /// <summary>
        /// T-N6-5 新增：在一个已经装配好、玩家已经存在的 <paramref name="world"/> 内打一场战斗——与
        /// <see cref="Run"/> 唯一的区别是不新建世界、不重新生成标准玩家，只针对已经生成好的
        /// <paramref name="creatureId"/> 跑逐 tick 循环直至一方死亡或超时。<paramref name="world"/>
        /// 装配时的 <c>HeadlessWorldOptions.CombatOptions</c> 必须是 <paramref name="accumulator"/>.
        /// <see cref="FightAccumulator.CombatOptions"/>（否则 <see cref="FightAccumulator.OnResolve"/>
        /// 永远不会被调用，命中/伤害计数恒为 0）。调用方（<see cref="Core.Sim.GrowthSimulation"/>）
        /// 负责在调用本方法之前把 <paramref name="creatureId"/> 生成并注册进 <c>world.Spatial</c>，
        /// 战斗结束后是否销毁尸体/清理生物同样是调用方的职责（本方法不做任何生物生命周期管理）。
        /// </summary>
        public static FightResult RunWithinWorld(
            HeadlessWorld world,
            FightAccumulator accumulator,
            Id playerId,
            Id creatureId,
            Id rotationId,
            double stepSeconds,
            int maxTicks,
            double moveSpeed,
            int maxResourceCurveSamples)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (accumulator == null) throw new ArgumentNullException(nameof(accumulator));

            accumulator.BeginFight(playerId, creatureId);

            var rotationEvaluator = new RotationEvaluator(
                world.Registry, world.Gameplay.Carriers.Rules.Skill,
                world.Gameplay.Carriers.Rules.ExprHostFactory, world.Gameplay.Carriers.Rules.ExprSchema);
            var engageRange = ResolveEngageRange(world.Registry, rotationId);

            var powerTypeIds = world.Registry.GetAll("arch.power_type").Select(r => r.GetId("id")).ToList();
            var playerCurves = powerTypeIds.ToDictionary(id => id, _ => new List<ResourceSample>());
            var creatureCurves = powerTypeIds.ToDictionary(id => id, _ => new List<ResourceSample>());

            var units = world.Gameplay.Carriers.Units;
            var powers = world.Gameplay.Carriers.Rules.Powers;

            var tick = 0;
            var outcome = FightOutcome.Timeout;

            for (; tick < maxTicks; tick++)
            {
                var playerAliveBefore = units.Exists(playerId) && units.IsAlive(playerId);
                var creatureAliveBefore = units.Exists(creatureId) && units.IsAlive(creatureId);
                if (!playerAliveBefore && !creatureAliveBefore) { outcome = FightOutcome.Draw; break; }
                if (!creatureAliveBefore) { outcome = FightOutcome.PlayerWin; break; }
                if (!playerAliveBefore) { outcome = FightOutcome.CreatureWin; break; }

                var eventsBefore = world.Events.Count;

                var castRequest = rotationEvaluator.Evaluate(playerId, rotationId, creatureId);
                if (castRequest == null)
                {
                    var creaturePos = units.GetPosition(creatureId);
                    SimpleMoveModel.Step(units, world.Spatial, playerId, creaturePos, engageRange, stepSeconds, moveSpeed);
                }

                world.Clock.Advance(stepSeconds);

                accumulator.AccumulateDamageEvents(world.Events, eventsBefore);

                var tickNumber = tick + 1;
                foreach (var powerId in powerTypeIds)
                {
                    if (units.Exists(playerId) && units.IsAlive(playerId) && powers.HasPower(playerId, powerId))
                    {
                        playerCurves[powerId].Add(new ResourceSample(tickNumber, powers.GetPower(playerId, powerId)));
                    }
                    if (units.Exists(creatureId) && units.IsAlive(creatureId) && powers.HasPower(creatureId, powerId))
                    {
                        creatureCurves[powerId].Add(new ResourceSample(tickNumber, powers.GetPower(creatureId, powerId)));
                    }
                }
            }

            if (outcome == FightOutcome.Timeout)
            {
                var playerAliveAfter = units.Exists(playerId) && units.IsAlive(playerId);
                var creatureAliveAfter = units.Exists(creatureId) && units.IsAlive(creatureId);
                if (!playerAliveAfter && !creatureAliveAfter) outcome = FightOutcome.Draw;
                else if (!creatureAliveAfter) outcome = FightOutcome.PlayerWin;
                else if (!playerAliveAfter) outcome = FightOutcome.CreatureWin;
            }

            var durationSeconds = tick * stepSeconds;

            IReadOnlyDictionary<Id, double> skillShare;
            if (accumulator.PlayerTotalDamage > 0)
            {
                skillShare = accumulator.PlayerDamageBySkill
                    .Where(kv => kv.Value > 0)
                    .ToDictionary(kv => kv.Key, kv => kv.Value / accumulator.PlayerTotalDamage);
            }
            else
            {
                skillShare = new Dictionary<Id, double>();
            }

            double playerHitRate = accumulator.PlayerAttempts > 0 ? (double)accumulator.PlayerLanded / accumulator.PlayerAttempts : 0.0;
            double creatureHitRate = accumulator.CreatureAttempts > 0 ? (double)accumulator.CreatureLanded / accumulator.CreatureAttempts : 0.0;

            var playerCurvesOut = playerCurves.ToDictionary(
                kv => kv.Key, kv => (IReadOnlyList<ResourceSample>)Downsample(kv.Value, maxResourceCurveSamples));
            var creatureCurvesOut = creatureCurves.ToDictionary(
                kv => kv.Key, kv => (IReadOnlyList<ResourceSample>)Downsample(kv.Value, maxResourceCurveSamples));

            var playerMaxHealth = powers.GetPowerMax(playerId, WellKnownPowers.Health);

            return new FightResult(
                outcome, durationSeconds, tick, accumulator.PlayerTotalDamage, accumulator.CreatureTotalDamage,
                playerHitRate, creatureHitRate, skillShare, playerCurvesOut, creatureCurvesOut,
                playerMaxHealth, playerId, creatureId);
        }

        private static double ResolveEngageRange(IDataRegistryView registry, Id rotationId)
        {
            var rotationRecord = registry.Get("ai.rotation", rotationId);
            if (rotationRecord == null || !rotationRecord.TryGetArray("entries", out var entries))
            {
                return DefaultEngageRange;
            }

            double maxRange = 0;
            foreach (var raw in entries)
            {
                if (raw is JsonObject obj &&
                    obj.TryGetValue("skill_id", out var skillIdRaw) && skillIdRaw is JsonString skillIdStr)
                {
                    var skillRecord = registry.Get("skill.def", new Id(skillIdStr.Value));
                    if (skillRecord != null && skillRecord.TryGetNumber("range", out var range) && range > maxRange)
                    {
                        maxRange = range;
                    }
                }
            }

            return maxRange > 0 ? maxRange : DefaultEngageRange;
        }

        /// <summary>把 <paramref name="samples"/> 下采样到 ≤ <paramref name="maxSamples"/> 点：不超过
        /// 上限时原样返回；超过时按等距下标抽样，首尾恒保留（供调用方看到"起点/终点"两个边界值）。</summary>
        internal static IReadOnlyList<ResourceSample> Downsample(IReadOnlyList<ResourceSample> samples, int maxSamples)
        {
            if (samples.Count <= maxSamples || maxSamples <= 1)
            {
                return samples;
            }

            var result = new List<ResourceSample>(maxSamples);
            for (var i = 0; i < maxSamples; i++)
            {
                var idx = (int)Math.Round(i * (samples.Count - 1) / (double)(maxSamples - 1));
                result.Add(samples[idx]);
            }
            return result;
        }
    }
}
