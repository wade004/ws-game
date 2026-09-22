using System.Collections.Generic;
using System.IO;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Rules.Combat;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary>
    /// ADR-0070（消费方反馈第十七批"框架原生普通攻击不广播任何事件，攻击者的表现层永远进不了
    /// AnimState.Attack"根治）：验收标准 1/2——不是只测中间层（<see cref="AutoAttackSwingEvent"/> 直接
    /// 发布），而是经**真实生产装配入口**装配：真实 <c>Core.Sim.HeadlessWorldBuilder.Build</c>
    /// （<c>core/gameplay/assembly.GameplayAssembly</c> 内部构造的 <c>RulesAssembly.AutoAttack</c> 正是
    /// 生产环境唯一的 <see cref="Core.Rules.Combat.AutoAttackHost"/> 装配点，见该类型构造site判断记录）
    /// 装出真实单位、真实武器数据、真实挥击计时，<see cref="AnimStateMachine"/> 绑定的是与
    /// <c>RulesAssembly.Bus</c> 同一个真实 <c>IEventBus</c> 实例——与生产环境里 <c>UnityViewFactory</c>
    /// 构造 <see cref="AnimStateMachine"/> 时传入的是同一根总线完全同构，唯一缺的只是 Unity 渲染本身
    /// （引擎无关部分不需要 Unity 才能验证，见 AGENTS.md §4 Unity 侧验证边界）。
    /// <para>
    /// 判断记录（本文件为什么不是 <c>core/sim/tests</c>）：<see cref="AnimStateMachine"/> 是
    /// <c>presentation/render</c>（<c>Presentation.Common.csproj</c>）模块类型，<c>core/sim</c>（L2/L3
    /// 之上的编排层）不应反向依赖表现层（00 架构总则分层方向）；<c>presentation/tests</c>
    /// （<c>Tests.PresentationCommon.csproj</c>）已经单向引用 <c>Core.Sim.csproj</c>（见该 csproj
    /// 判断记录，供 <c>NumericValidationRuleCatalogTests</c> 使用），依赖方向不倒置。<see
    /// <c>Tests.Sim.SimTestWorldFactory</c> 是 <c>Tests.Sim</c> 内部 <c>internal</c> 类型，本项目不
    /// 可见，本文件复刻其嵌入式数据集装配逻辑（<c>data/_framework</c> + <c>core/sim/tests/data</c>，
    /// 惯例完全一致）。
    /// </para>
    /// </summary>
    public class AutoAttackAnimStateMachineEndToEndTests
    {
        private static readonly Id MapId = new Id("world.sim_arena");
        private static readonly Id PlayerId = new Id("unit.sample_player");
        private static readonly Id FactionPlayer = new Id("fac.player");
        private static readonly Id ClassId = new Id("arch.class.sim_warrior");
        private static readonly Id GameId = new Id("game.sim_embedded");
        private static readonly Id CreatureWolfL1 = new Id("creature.sim_wolf_l1");
        private static readonly Id QualityId = new Id("item.quality.sim_common");
        private const double StepSeconds = 0.5;

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            // 本源文件固定位于 <repoRoot>/presentation/render/tests/AutoAttackAnimStateMachineEndToEndTests.cs，
            // 向上 3 级（tests → render → presentation）即仓库根，惯例同
            // Tests.Sim.SimTestWorldFactory.FindRepoRoot。
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
            for (var i = 0; i < 3; i++)
            {
                dir = dir.Parent!;
            }
            return dir.FullName;
        }

        private static FileSystemDataSource BuildDiskSource(StubFileSystem fs, string repoRoot, string relativeRoot)
        {
            var absoluteRoot = Path.Combine(repoRoot, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            foreach (var file in Directory.GetFiles(absoluteRoot, "*.json", SearchOption.AllDirectories))
            {
                var rel = file.Substring(absoluteRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                fs.WriteTextAtomic(relativeRoot + "/" + rel, File.ReadAllText(file));
            }
            return new FileSystemDataSource(fs, relativeRoot);
        }

        /// <summary>经真实生产装配入口（<c>Core.Sim.HeadlessWorldBuilder.Build</c> →
        /// <c>GameplayAssembly</c> → <c>RulesAssembly</c>）装配一个带真实装备武器的标准玩家 + 无头
        /// 世界，惯例同 <c>Tests.Sim.SimTestWorldFactory.BuildFromEmbeddedDataset</c>（本文件不可见该
        /// 内部类型，复刻其嵌入式数据集装配逻辑）。</summary>
        private static Core.Sim.HeadlessWorld BuildWorld(ulong seed)
        {
            var fs = new StubFileSystem();
            var repoRoot = FindRepoRoot();
            var frameworkSource = BuildDiskSource(fs, repoRoot, "data/_framework");
            var embeddedSource = BuildDiskSource(fs, repoRoot, "core/sim/tests/data");

            var world = Core.Sim.HeadlessWorldBuilder.Build(new Core.Sim.HeadlessWorldOptions
            {
                DataSources = new IDataSource[] { frameworkSource, embeddedSource },
                Seed = seed,
                FileSystem = fs,
                MapId = MapId,
                PlayerId = PlayerId,
                PlayerFactionId = FactionPlayer,
                PlayerClassId = ClassId,
                PlayerLevel = 1,
                GameId = GameId,
                StepSeconds = StepSeconds,
                FailOnUnknownTable = false,
            });

            Core.Sim.StandardPlayerBuilder.Build(world, ClassId, 1, QualityId);
            return world;
        }

        private static Id SpawnWolf(Core.Sim.HeadlessWorld world, Vec2 pos)
        {
            var creatureId = world.Gameplay.Carriers.Creatures.Spawn(CreatureWolfL1, MapId, pos, facing: System.Math.PI);
            world.Spatial.Register(creatureId, pos, 0.5);
            return creatureId;
        }

        /// <summary>
        /// 验收标准 1：真实单位开启普通攻击、有真实目标与武器数据，推进时间越过攻击周期 → 攻击者的
        /// <see cref="AnimState"/> 变为 <see cref="AnimState.Attack"/>（改动前该断言恒失败——
        /// <c>AutoAttackHost</c> 完全不广播任何事件，<see cref="AnimStateMachine"/> 收不到任何驱动
        /// Attack 的信号，攻击者会一直停在 <see cref="AnimState.Idle"/>）；目标仍照常进入
        /// <see cref="AnimState.Hit"/>（<c>combat.damage_dealt</c> 早已存在，本决策不改动这条路径，
        /// 用来确认"目标侧行为未被破坏"）；连续多次挥击（每次挥击之间显式
        /// <see cref="AnimStateMachine.NotifyTransientStateFinished"/> 模拟动画播完）能重复进入
        /// Attack，不是只有第一次生效。
        /// </summary>
        [Fact]
        public void RealAssembly_AutoAttackSwing_DrivesAttackerIntoAttack_TargetIntoHit_RepeatsAcrossSwings()
        {
            const ulong seed = 20260923_1001UL;
            var world = BuildWorld(seed);
            // 判断记录（生成在很远的位置，惯例同 Tests.Sim.AutoAttackHostIntegrationTests.
            // AutoAttack_NoWeaponAndNoIntervalFallback_WarnsExactlyOnce_NeverAttacks）：野狼生物模板
            // 挂了敌对 ai_rotation_ref/ai_behavior_ref，一旦感知到玩家会独立发起技能攻击，产生一条
            // 目标是玩家的 combat.damage_dealt，把玩家自己的 AnimState 也顶成 Hit（优先级 3，高于
            // Attack 的优先级 2）——与本用例"只看普通攻击本身驱动的 Attack"这一断言无关，摆在感知
            // 范围之外彻底避免这个既有、与本次改动无关的系统产生噪声（AutoAttackOptions.Range 缺省
            // 0 = 无限制，玩家仍能"攻击"这么远的目标，不受这个摆放位置影响）。
            var creatureId = SpawnWolf(world, new Vec2(5000, 0));

            var equipment = world.Gameplay.Carriers.Equipment;
            var interval = equipment.GetWeaponAttackIntervalSeconds(PlayerId);
            Assert.True(interval.HasValue && interval.Value > 0.0, "标准玩家应已装备带 weapon_profile.speed 的主手武器");

            // AnimStateMachine 绑定与 RulesAssembly.AutoAttack 同一根真实 IEventBus（world.Bus）——
            // 与生产环境 UnityViewFactory 构造它时传入的是同一根总线同构，见类型判断记录。
            var animMachine = new AnimStateMachine(world.Bus);
            var attackEntries = 0;
            animMachine.StateChanged += (id, from, to) =>
            {
                if (id == PlayerId && to == AnimState.Attack)
                {
                    attackEntries++;
                }
            };

            var autoAttack = world.Gameplay.Carriers.Rules.AutoAttack;
            autoAttack.SetTarget(PlayerId, creatureId);
            autoAttack.SetEnabled(PlayerId, true);

            Assert.Equal(AnimState.Idle, animMachine.GetState(PlayerId));

            world.Clock.Advance(interval!.Value);

            Assert.Equal(AnimState.Attack, animMachine.GetState(PlayerId));
            Assert.Equal(AnimState.Hit, animMachine.GetState(creatureId));
            Assert.Equal(1, attackEntries);

            // 模拟第一次 Attack 动画播完（回落到运动态），再推进一个挥击周期：应重复进入 Attack，
            // 不是只有第一次生效。
            animMachine.NotifyTransientStateFinished(PlayerId, AnimState.Attack);
            Assert.Equal(AnimState.Idle, animMachine.GetState(PlayerId));

            world.Clock.Advance(interval.Value);

            Assert.Equal(AnimState.Attack, animMachine.GetState(PlayerId));
            Assert.Equal(2, attackEntries);
        }

        /// <summary>验收标准 2：被跳过的挥击（超出 <see cref="AutoAttackOptions.Range"/>）不触发——
        /// 攻击者不进入 Attack（AutoAttackHost 一贯"超射程不结算、不诊断"口径，本决策不改变这一口径，
        /// 只是新增一条"不结算就不发挥击事件"的推论）。<c>NoAttack</c> 控制门控跳过的单元级验证见
        /// <c>Tests.Rules.Combat.AutoAttackHostSwingEventTests.Update_NoAttackControlFlag_SkipsSwing_DoesNotPublishEvent_DoesNotApplyEffect</c>
        /// （经真实生产装配触发一次"缴械"类控制光环需要额外接一整条内容驱动的光环数据，价值不高于
        /// 该单元测试已经钉住的同一条判断记录，故本文件不重复覆盖）。</summary>
        [Fact]
        public void RealAssembly_OutOfRangeSwing_IsSkipped_AttackerStaysOutOfAttack()
        {
            const ulong seed = 20260923_1002UL;
            var world = BuildWorld(seed);
            var creatureId = SpawnWolf(world, new Vec2(3, 0));

            var equipment = world.Gameplay.Carriers.Equipment;
            var interval = equipment.GetWeaponAttackIntervalSeconds(PlayerId)!.Value;

            var animMachine = new AnimStateMachine(world.Bus);

            var autoAttack = world.Gameplay.Carriers.Rules.AutoAttack;
            autoAttack.Options.Range = 1.0; // 目标在 (3,0)，玩家在原点——超出这个射程。
            autoAttack.SetTarget(PlayerId, creatureId);
            autoAttack.SetEnabled(PlayerId, true);

            world.Clock.Advance(interval * 3);

            Assert.NotEqual(AnimState.Attack, animMachine.GetState(PlayerId));
        }
    }
}
