using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Carriers.Gobj;
using Core.Carriers.Item;
using Core.Carriers.Projectile;
using Core.Carriers.Summon;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Ai;
using Core.Rules.Assembly;
using Core.Rules.Combat;
using Core.Rules.Common;
using Core.Rules.Skill;
using Core.Rules.Targeting;

namespace Core.Carriers.Assembly
{
    /// <summary>
    /// L3（<c>core/carriers</c> 六模块：<c>unit</c>/<c>item</c>/<c>creature</c>/<c>summon</c>/
    /// <c>gobj</c>/<c>projectile</c>）在 <see cref="RulesAssembly"/>（L0～L2）之上的组装根（阶段 3
    /// 整理"事项四"，<c>projectile</c> 为收边任务补齐）。
    /// 接收一批"环境依赖"（事件总线、已加载数据、随机源、世界模拟、空间/导航查询，以及可选的
    /// L4 回调），按正确顺序装配全部 L3 宿主、把契约缺口用具名委托接线（<see cref="SkillGranter"/>
    /// → <see cref="SkillHost.LearnSkill"/>/<see cref="SkillHost.ForgetSkill"/>，
    /// <see cref="AiRegistrar"/> → <see cref="AiHost.RegisterUnit"/>/<see cref="AiHost.SetRotation"/>，
    /// <see cref="IStaticImmunityProvider"/> → <see cref="CreatureImmunityProvider"/>），把
    /// <c>create_item</c>/<c>open_lock</c>/<c>summon</c> 三类效果原语的真实实现组合后换入
    /// <see cref="RulesAssembly.EffectExtension"/>；<c>projectile</c> 效果原语走独立的依赖倒置
    /// 接口 <see cref="Core.Rules.Common.IProjectileSpawner"/>（不经 <c>EffectExtension</c>，见该
    /// 接口判断记录），直接在 <see cref="RulesAssembly"/> 构造期传入 <see cref="Projectiles"/>。
    /// 最终把全部宿主暴露为只读属性。
    /// <para>
    /// 装配顺序、tick 阶段挂载表见 <c>core/carriers/assembly/README.md</c>。L4 回调
    /// （<see cref="IWorldFlags"/>/<see cref="ILootRoller"/>/<see cref="DialogOpenerDelegate"/> 等）
    /// 作为可选参数留空位——未注入时分别退化为 <see cref="NullWorldFlags"/>、"不产出任何掉落"、
    /// "on_use: dialog 分发失败并记警告"，不阻断构造（分层：本类是 L3，不得对 L4 产生编译期依赖，
    /// 见 <see cref="IWorldFlags"/>/<see cref="ILootRoller"/> 顶部判断记录"依赖倒置"）。
    /// </para>
    /// </summary>
    public sealed class CarriersAssembly
    {
        public RulesAssembly Rules { get; }

        public WorldUnitAccess Units { get; }
        public InventoryHost Inventory { get; }
        public EquipmentHost Equipment { get; }
        public CreatureFactory Creatures { get; }
        public SummonHost Summons { get; }
        public GameObjectFactory GameObjects { get; }
        public GameObjectHost GameObjectInteractions { get; }
        public MovementHost Movement { get; }

        /// <summary>收边任务补齐：投射物生成/飞行推进宿主，同时是注入给 <see cref="RulesAssembly"/>
        /// 的 <see cref="Core.Rules.Common.IProjectileSpawner"/> 实现（见该接口判断记录"依赖倒置"，
        /// <c>projectile</c> 效果原语委托 L3 生成）。</summary>
        public ProjectileHost Projectiles { get; }

        /// <summary>本次装配实际使用的 <see cref="Core.Carriers.Unit.MovementOptions"/> 实例（构造
        /// 参数为空时是本类型内部新建的默认值）。ADR-0013 离散时间模型补齐：
        /// <c>Core.Gameplay.Assembly.GameplayAssembly</c> 需要在装配出
        /// <c>Core.Foundation.SimLoop.TurnScheduler</c> 之后回填
        /// <see cref="Core.Carriers.Unit.MovementOptions.MovementBudgetRule"/>/
        /// <see cref="Core.Carriers.Unit.MovementOptions.TryConsumeActionPoints"/> 等字段（见该类型
        /// 判断记录"构造后回填而非构造期传入"），必须拿到与 <see cref="MovementTickHandler"/>（本
        /// 类内部持有，未对外暴露）内部实际使用的同一个实例，本属性是它唯一的对外暴露点（惯例同
        /// <c>Core.Rules.Assembly.RulesAssembly.CombatOptions</c> 判断记录）。</summary>
        public MovementOptions MovementOptions { get; }

        /// <summary>缺口 4：技能槽位绑定宿主（见 <see cref="ISkillBindingHost"/>）。</summary>
        public SkillBindingHost SkillBindings { get; }

        /// <summary>
        /// 参与 <see cref="ISpatialQuery"/> 空间索引登记的 <see cref="Entity.Kind"/> 清单默认值
        /// （见 <see cref="EntitySpatialSyncHost"/> 判断记录）：默认只登记 <c>creature</c>/
        /// <c>player</c> 两类 Unit（打 <c>"unit"</c> 标签），半径统一默认 0.1（同此前
        /// <c>WorldUnitAccess</c> 的默认 <c>spatialRadius</c>）。调用方可通过构造函数的
        /// <c>spatialSyncKinds</c> 参数整体覆盖（例如接入体型差异更大的游戏时按 kind 给不同半径）。
        /// <para>
        /// 判断记录（默认不登记 <c>gobj</c>）：ADR-0016 背景一节提到"gobj 创建/销毁按需登记（带 tag
        /// 区分 unit/gobj，避免'最近敌人'捞到物件）"，字面上鼓励把 gobj 也登记进同一份空间索引。
        /// 但勘察 <c>core/rules/targeting/core/BuiltinTargetStrategies.NearestInShapeStrategy</c>
        /// 发现它用 <c>QueryFilter.None</c>（不做标签过滤）查询空间索引后直接对每个候选调用
        /// <c>IUnitAccess.GetPosition</c>，不像 <c>Core.Rules.Ai.AiHost.FindNearestHostile</c> 那样
        /// 先用 <c>IUnitAccess.Exists</c> 防御性过滤——若 gobj 也登记进同一索引，任何用
        /// <c>nearest_in_shape</c> 策略、不显式加 <c>RequiredTags:["unit"]</c> 过滤的技能目标解析都
        /// 会在候选集合里混入 gobj id，进而在 <c>WorldUnitAccess.Require</c> 抛
        /// <see cref="System.InvalidOperationException"/>（这正是 Unity 侧
        /// <c>GameFoundationBootstrap</c> 此前手工登记空间索引时特意跳过 gobj 的原因，见该类型
        /// "判断记录"，实测 PlayMode 复现过）。在 <c>core/rules/targeting</c> 各内置策略普遍加上
        /// 标签过滤之前，默认清单按"不引入新的隐患"原则不含 <c>gobj</c>；确有需要把 gobj 纳入同一
        /// 空间索引（如实现"最近可交互物件"一类查询）的游戏，应显式传入包含 <c>gobj</c> 条目的
        /// <c>spatialSyncKinds</c>，并自行确保所有会遍历该索引全部候选的调用方都正确按标签过滤或
        /// 防御性检查 <c>IUnitAccess.Exists</c>。
        /// </para>
        /// <para>
        /// 判断记录（加固任务：补 <see cref="Core.Foundation.EngineAdapter.CollisionLayers.UnitBlock"/>
        /// 标签、新增 <c>area_trigger</c> 条目）：05 §3.6 碰撞层规划落地——`creature`/`player` 在既有
        /// `"unit"` 标签之外再打一份 <c>CollisionLayers.UnitBlock</c>（是否据此阻挡移动由
        /// <c>Core.Carriers.Unit.MovementOptions.UnitBlocking</c> 这个口味开关决定，默认 false，
        /// 打标签本身不改变现状行为）。<c>EntityKinds.AreaTrigger</c> 加入白名单、打
        /// <c>CollisionLayers.TriggerOnly</c> 单一标签（不含 `"unit"`）——与判断记录"默认不含 gobj"
        /// 同一顾虑相反：这里刻意登记，目的是让"按 `trigger_only` 标签查询范围内触发体"这类未来
        /// 查询成为可能；因此本任务同时要求全仓库排查所有不带 `RequiredTags` 的既有空间查询点
        /// （`core/rules/targeting/core/BuiltinTargetStrategies.cs`、`core/rules/ai/core/AiHost.cs`、
        /// `core/rules/expr_host/RulesExprHostFactory.cs`、`core/gameplay/assembly/TimeModelSwitch.cs`）
        /// 补 `ExcludedTags = [CollisionLayers.TriggerOnly]`，避免区域触发体被误当命中/感知候选（见
        /// 各自判断记录）；带 `RequiredTags: ["unit"]` 的查询（如 `core/carriers/projectile`
        /// `ProjectileOptions.HitQueryTags` 默认值）天然排除，不需要改动。半径用固定近似值 0.5（本类
        /// 型是 L3，01 第 3 节依赖矩阵禁止依赖 L4，不能在这里引用 `Core.Gameplay.AreaTrigger.
        /// AreaTriggerEntity` 按其 `Shape` 精确计算外接半径）；真正装配全部 L0～L4 的
        /// `Core.Gameplay.Assembly.GameplayAssembly` 用 <see cref="EntitySpatialSyncHost.KindConfig.RadiusResolver"/>
        /// 覆盖这一条目，按 `AreaTriggerEntity.BoundingRadius` 精确计算（见该类型判断记录）。
        /// </para>
        /// </summary>
        public static IReadOnlyDictionary<string, EntitySpatialSyncHost.KindConfig> DefaultSpatialSyncKinds { get; } =
            new Dictionary<string, EntitySpatialSyncHost.KindConfig>(StringComparer.Ordinal)
            {
                [EntityKinds.Creature] = new EntitySpatialSyncHost.KindConfig(0.1, new[] { "unit", CollisionLayers.UnitBlock }),
                [EntityKinds.Player] = new EntitySpatialSyncHost.KindConfig(0.1, new[] { "unit", CollisionLayers.UnitBlock }),
                [EntityKinds.AreaTrigger] = new EntitySpatialSyncHost.KindConfig(0.5, new[] { CollisionLayers.TriggerOnly }),
            };

        public EntitySpatialSyncHost SpatialSync { get; }

        public CarriersAssembly(
            IEventBus bus,
            IDataRegistryView registry,
            IRngHost rng,
            IWorldSim world,
            ISpatialQuery spatial,
            INavigation2D? navigation = null,
            IReadOnlyDictionary<string, EntitySpatialSyncHost.KindConfig>? spatialSyncKinds = null,
            IWorldFlags? worldFlags = null,
            ILootRoller? lootRoller = null,
            StatHostOptions? statOptions = null,
            CombatOptions? combatOptions = null,
            SkillOptions? skillOptions = null,
            TargetingOptions? targetingOptions = null,
            AiOptions? aiOptions = null,
            InventoryOptions? inventoryOptions = null,
            ItemOptions? itemOptions = null,
            CreatureOptions? creatureOptions = null,
            SummonOptions? summonOptions = null,
            GobjOptions? gobjOptions = null,
            MovementOptions? movementOptions = null,
            ProjectileOptions? projectileOptions = null,
            IReadOnlyList<IExprSchema>? extraSchemas = null,
            // W1 收边补齐（A3 审计 #7/#11）：原样转发给 RulesAssembly 同名三个参数——
            // core/gameplay/assembly.GameplayAssembly（持有真正的 ITurnScheduler）在离散模式接通
            // 后应传入 () => scheduler.CurrentTurnIndex / RoundIndex / GetCurrentActor()，让
            // time.turn_index/round_index/is_my_turn 三个 Expr key 不再恒占位值（见
            // RulesAssembly.cs 构造函数同名参数的完整判断记录）。三者均为 null（默认）时行为与
            // 本次改动之前完全一致。
            Func<int>? discreteTurnIndexProvider = null,
            Func<int>? discreteRoundIndexProvider = null,
            Func<Id?>? discreteCurrentActorProvider = null)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (spatial == null) throw new ArgumentNullException(nameof(spatial));

            // ---------------------------------------------------------
            // 0) EntitySpatialSyncHost：订阅 entity.created/entity.destroyed，按 Kind 登记/注销
            //    空间索引（创建、销毁两个时机，见该类型判断记录）；必须先于任何会创建实体的宿主
            //    构造（本类第 5/7 步的 CreatureFactory/GameObjectFactory），否则会错过它们构造期
            //    间可能触发的创建事件。
            // ---------------------------------------------------------
            SpatialSync = new EntitySpatialSyncHost(bus, world, spatial, spatialSyncKinds ?? DefaultSpatialSyncKinds);

            // ---------------------------------------------------------
            // 1) WorldUnitAccess（core/carriers/unit 的 IUnitAccess 真实实现，RulesAssembly 需要
            //    调用方注入这份"环境依赖"，见 core/rules/assembly/README.md 步骤 0/1）。移动时机的
            //    空间索引同步（UpdatePosition）经这里注入的 spatial 完成，创建/销毁时机见上一步。
            //    W1 收边补齐（拍板 3 前置：死亡复活链路）：HealthFractionSetter 委托闭包提前捕获
            //    尚未赋值的 powers 局部变量（与本类第 9 步 Rules 构造完成后回填同一种处理循环依赖
            //    的手法——PowerHost 由 RulesAssembly 内部构造，而 RulesAssembly 构造又需要先拿到
            //    WorldUnitAccess 作为 IUnitAccess 传入，两者互相需要对方，只要真正调用（Revive）
            //    发生在构造完成之后即可安全提前绑定，见 core/rules/assembly/RulesAssembly.cs
            //    progression/archAuraApplier 同款判断记录）。
            // ---------------------------------------------------------
            PowerHost powers = null!;
            HealthFractionSetter healthFractionSetter = (unitId, fraction) =>
            {
                var max = powers.GetPowerMax(unitId, WellKnownPowers.Health);
                var current = powers.GetPower(unitId, WellKnownPowers.Health);
                powers.ModifyPower(unitId, WellKnownPowers.Health, max * fraction - current, new Id("system.revive"));
            };
            Units = new WorldUnitAccess(world, spatial, healthFractionSetter);

            // ---------------------------------------------------------
            // 2) CreatureImmunityProvider（IStaticImmunityProvider 的默认实现）+ InventoryHost +
            //    ItemEffectExtension——三者都只需要构造期已有的依赖（world/registry/bus），不依赖
            //    RulesAssembly 内部任何宿主，可以在 RulesAssembly 之前先造好。
            // ---------------------------------------------------------
            var staticImmunity = new CreatureImmunityProvider(world);

            var resolvedInventoryOptions = inventoryOptions ?? new InventoryOptions();
            Inventory = new InventoryHost(registry, bus, resolvedInventoryOptions);
            var itemExtension = new ItemEffectExtension(Inventory);

            // ---------------------------------------------------------
            // 2.5) ProjectileHost（IProjectileSpawner 实现，收边任务补齐）：只依赖构造期已有的
            //    world/Units/spatial/navigation，不依赖 RulesAssembly 内部任何宿主（命中后效果的
            //    回灌出口 IEffectSink 由每次 Spawn 调用自带，不需要构造期持有 Rules.Skill），可以
            //    在 RulesAssembly 之前先造好并直接传给它（不像 gobj/summon 那样需要延迟绑定，见
            //    DeferredEffectExtension 判断记录——那是"L3 组合实现要等 Ai 构造完成"的情形，
            //    ProjectileHost 没有这层依赖）。
            // ---------------------------------------------------------
            var resolvedProjectileOptions = projectileOptions ?? new ProjectileOptions();
            Projectiles = new ProjectileHost(world, Units, spatial, navigation, resolvedProjectileOptions);

            // ---------------------------------------------------------
            // 3) RulesAssembly（L0～L2）：staticImmunity 直接注入；effectExtension 先只挂
            //    itemExtension（create_item 已经可用），gobj/summon 两类要等本类后续步骤把
            //    CreatureFactory/SummonHost/GameObjectHost 都造出来才能组合，届时经
            //    Rules.EffectExtension.Bind 换成完整版（见 DeferredEffectExtension 判断记录）。
            //    autoRegisterTickHandlers: false——SummonTickHandler 必须先于 AiTickHandler 挂到
            //    TickPhase.AiDecision（任务书拍板），本类在下面第 8 步手动控制注册顺序。
            // ---------------------------------------------------------
            Rules = new RulesAssembly(
                bus, registry, rng, Units, spatial, world, navigation,
                statOptions, combatOptions, skillOptions, targetingOptions, aiOptions,
                extraSchemas, staticImmunity, itemExtension,
                autoRegisterTickHandlers: false, projectileSpawner: Projectiles,
                discreteTurnIndexProvider: discreteTurnIndexProvider,
                discreteRoundIndexProvider: discreteRoundIndexProvider,
                discreteCurrentActorProvider: discreteCurrentActorProvider);
            powers = Rules.Powers; // 回填第 1 步 healthFractionSetter 闭包捕获的局部变量。

            // ---------------------------------------------------------
            // 3a) SkillBindingHost（缺口 4）：KnownSkillQuery 委托接线到 Rules.Skill.Knows（惯例同
            //     下面第 4 步 SkillGranter 对 Rules.Skill.LearnSkill/ForgetSkill 的接线——L3 不得
            //     直接引用 L2 的具体宿主类型，见 KnownSkillQuery 判断记录）。
            // ---------------------------------------------------------
            KnownSkillQuery knownSkillQuery = (unitId, skillId) => Rules.Skill.Knows(unitId, skillId);
            SkillBindings = new SkillBindingHost(bus, knownSkillQuery);

            // ---------------------------------------------------------
            // 4) EquipmentHost：SkillGranter 委托接线到 Rules.Skill.LearnSkill/ForgetSkill（见
            //    SkillGranter.cs 判断记录）。
            // ---------------------------------------------------------
            var resolvedItemOptions = itemOptions ?? new ItemOptions();
            // RC-05 收边勘误：接线到带来源的 LearnSkill/ForgetSkill 重载（sourceId 传装备实例 id，
            // 见 SkillGranter.cs 判断记录）——SkillHost 侧按来源做引用计数，两件装备授予同一技能
            // 时卸下一件不会影响另一件仍在授予的同一技能，也不会连永久学习（天赋/任务/技能书/
            // 读档，走不带来源的 LearnSkill(Id,Id) 重载）一并遗忘。
            // CR130-02 根治（外部审计 audit-5c444f1-20260908）：显式传 permanent: false——装备授予
            // 是本类型判断记录里"临时：装备/光环授予"的分类，跟随宿主生命周期、卸下即失效，不应被
            // KnownSkillsPersistable.Save 快照进存档（见 SkillHost.LearnSkill(Id,Id,Id,bool) 判断
            // 记录）。三参重载（不带 permanent）默认按永久处理，是给奖励/任务一类路径用的，装备联动
            // 必须显式走四参重载，不能依赖默认值。
            SkillGranter skillGranter = (unitId, skillId, sourceId, learn) =>
            {
                if (learn)
                {
                    Rules.Skill.LearnSkill(unitId, skillId, sourceId, permanent: false);
                }
                else
                {
                    Rules.Skill.ForgetSkill(unitId, skillId, sourceId);
                }
            };
            // C08 收口：注入 Rules.Skill.AuraQuery（真实 AuraHost）——StackOverflowPolicy.Replace
            // 换句柄时，EquipmentHost 借此同步更新自己记录的授予句柄，见 EquipmentHost 构造函数
            // 判断记录、IAuraQuery.InstanceReplaced 判断记录。
            Equipment = new EquipmentHost(
                registry, bus, Inventory, Rules.Stats, Rules.Skill.EffectSink, skillGranter, Units,
                resolvedItemOptions, auraQuery: Rules.Skill.AuraQuery);

            // RC-11 收边补齐：EquipmentHost 实现 IWeaponDamageQuery（见该类型 GetWeaponBaseDamage
            // 判断记录），换上 RulesAssembly 构造期先用的延迟绑定代理（见 DeferredWeaponDamageQuery
            // 判断记录——真实实现要等 Equipment 在这里构造完成才存在，与 EffectExtension 的循环
            // 依赖是同一种模式）。
            Rules.WeaponDamageQuery.Bind(Equipment);

            // ---------------------------------------------------------
            // 5) CreatureFactory：AiRegistrar 委托接线到 Rules.Ai.RegisterUnit/SetRotation（见
            //    AiRegistrar.cs 判断记录）。
            // ---------------------------------------------------------
            AiRegistrar aiRegistrar = (unitId, profileId, spawnPoint, rotationId) =>
            {
                Rules.Ai.RegisterUnit(unitId, profileId, spawnPoint);
                if (rotationId.HasValue)
                {
                    Rules.Ai.SetRotation(unitId, rotationId.Value);
                }
            };
            Creatures = new CreatureFactory(
                registry, world, bus, Rules.Stats, Rules.Powers, Rules.Progression, Units, aiRegistrar,
                creatureOptions);

            // ---------------------------------------------------------
            // 6) SummonHost（依赖 4 的 Creatures）+ SummonEffectExtension。
            // ---------------------------------------------------------
            var resolvedSummonOptions = summonOptions ?? new SummonOptions();
            Summons = new SummonHost(Creatures, world, Units, bus, resolvedSummonOptions);
            var summonExtension = new SummonEffectExtension(Summons, Units);

            // ---------------------------------------------------------
            // 7) GameObjectFactory（生成实体）+ GameObjectHost（交互/开锁）+ GobjEffectExtension。
            //    worldFlags/lootRoller 未注入时分别退化为 NullWorldFlags/null（GameObjectHost 的
            //    loot 参数本就可空，见该类型构造函数）。
            // ---------------------------------------------------------
            GameObjects = new GameObjectFactory(world);
            GameObjectInteractions = new GameObjectHost(
                registry, world, bus, worldFlags ?? NullWorldFlags.Instance, Units, Inventory, Rules.Stats,
                Rules.Skill, lootRoller, gobjOptions);
            var gobjExtension = new GobjEffectExtension(GameObjectInteractions);

            // ---------------------------------------------------------
            // 8) 换上完整版 IEffectExtension（item + gobj + summon 三类组合），随后按拍板顺序补挂
            //    tick 处理器：SummonTickHandler 先注册到 AiDecision，再调用
            //    Rules.RegisterTickHandlers() 补上 L2 的四个（AiTickHandler 排在 SummonTickHandler
            //    之后），最后注册 MovementTickHandler 到 MovementAndNavigation。
            // ---------------------------------------------------------
            Rules.EffectExtension.Bind(new CompositeEffectExtension(itemExtension, gobjExtension, summonExtension));

            var summonTickHandler = new SummonTickHandler(Summons, Units, Rules.Combat, resolvedSummonOptions);
            world.RegisterPhaseHandler(TickPhase.AiDecision, summonTickHandler);

            Rules.RegisterTickHandlers();

            Movement = new MovementHost(world);
            var resolvedMovementOptions = movementOptions ?? new MovementOptions();
            MovementOptions = resolvedMovementOptions;
            // spatial 注入（加固任务：unit_block 阻挡判定，见 MovementTickHandler 判断记录）：
            // MovementTickHandler 构造期已有的 spatial 就是本方法参数，不需要额外装配。
            var movementTickHandler = new MovementTickHandler(
                Units, Rules.Stats, Rules.Skill.AuraQuery, Movement, bus, navigation, resolvedMovementOptions,
                spatial: spatial);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, movementTickHandler);

            // 投射物飞行推进：紧随 MovementTickHandler 之后注册到同一阶段，见
            // ProjectileTickHandler 判断记录"挂载阶段：移动步之后、战斗结算步之前"。
            var projectileTickHandler = new ProjectileTickHandler(Projectiles, resolvedProjectileOptions);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, projectileTickHandler);

            // 交互意图消费（03 第 4.2 节步骤 6"触发评估"，见 InteractIntentTickHandler 判断记录：
            // 该步骤此前只有文档约定、没有实现——本次补上）。
            world.RegisterPhaseHandler(TickPhase.TriggerEvaluation, new InteractIntentTickHandler(GameObjectInteractions));
        }
    }
}
