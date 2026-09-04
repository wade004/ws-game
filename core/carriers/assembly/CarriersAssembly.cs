using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Carriers.Gobj;
using Core.Carriers.Item;
using Core.Carriers.Summon;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
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
    /// L3（<c>core/carriers</c> 五模块：<c>unit</c>/<c>item</c>/<c>creature</c>/<c>summon</c>/
    /// <c>gobj</c>）在 <see cref="RulesAssembly"/>（L0～L2）之上的组装根（阶段 3 整理"事项四"）。
    /// 接收一批"环境依赖"（事件总线、已加载数据、随机源、世界模拟、空间/导航查询，以及可选的
    /// L4 回调），按正确顺序装配全部 L3 宿主、把契约缺口用具名委托接线（<see cref="SkillGranter"/>
    /// → <see cref="SkillHost.LearnSkill"/>/<see cref="SkillHost.ForgetSkill"/>，
    /// <see cref="AiRegistrar"/> → <see cref="AiHost.RegisterUnit"/>/<see cref="AiHost.SetRotation"/>，
    /// <see cref="IStaticImmunityProvider"/> → <see cref="CreatureImmunityProvider"/>），把
    /// <c>create_item</c>/<c>open_lock</c>/<c>summon</c> 三类效果原语的真实实现组合后换入
    /// <see cref="RulesAssembly.EffectExtension"/>，最终把全部宿主暴露为只读属性。
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

        public CarriersAssembly(
            IEventBus bus,
            IDataRegistryView registry,
            IRngHost rng,
            IWorldSim world,
            ISpatialQuery spatial,
            INavigation2D? navigation = null,
            ISpatialIndexSync? spatialSync = null,
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
            IReadOnlyList<IExprSchema>? extraSchemas = null)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (spatial == null) throw new ArgumentNullException(nameof(spatial));

            // ---------------------------------------------------------
            // 1) WorldUnitAccess（core/carriers/unit 的 IUnitAccess 真实实现，RulesAssembly 需要
            //    调用方注入这份"环境依赖"，见 core/rules/assembly/README.md 步骤 0/1）。
            // ---------------------------------------------------------
            Units = new WorldUnitAccess(world, spatialSync);

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
                autoRegisterTickHandlers: false);

            // ---------------------------------------------------------
            // 4) EquipmentHost：SkillGranter 委托接线到 Rules.Skill.LearnSkill/ForgetSkill（见
            //    SkillGranter.cs 判断记录）。
            // ---------------------------------------------------------
            var resolvedItemOptions = itemOptions ?? new ItemOptions();
            SkillGranter skillGranter = (unitId, skillId, learn) =>
            {
                if (learn)
                {
                    Rules.Skill.LearnSkill(unitId, skillId);
                }
                else
                {
                    Rules.Skill.ForgetSkill(unitId, skillId);
                }
            };
            Equipment = new EquipmentHost(
                registry, bus, Inventory, Rules.Stats, Rules.Skill.EffectSink, skillGranter, Units,
                resolvedItemOptions);

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
            var movementTickHandler = new MovementTickHandler(
                Units, Rules.Stats, Rules.Skill.AuraQuery, Movement, bus, navigation, resolvedMovementOptions);
            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, movementTickHandler);
        }
    }
}
