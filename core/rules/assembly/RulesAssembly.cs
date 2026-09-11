using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Numbers.Archetype;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Core.Rules.Ai;
using Core.Rules.Combat;
using Core.Rules.Common;
using Core.Rules.ExprHost;
using Core.Rules.Skill;
using Core.Rules.Targeting;

// 判断记录：Progression 与 Archetype 两个模块各自声明了同名委托 StatModifierWriter（签名相同，
// 见各自模块 README"依赖"一节——两模块并行开发、互不引用对方类型），同时 using 两个命名空间会在
// 裸写 "StatModifierWriter" 时产生二义性，惯例同 core/numbers/tests/L1SampleDataTests.cs，用别名
// 区分。
using ProgStatModifierWriter = Core.Numbers.Progression.StatModifierWriter;
using ProgStatModifierRemover = Core.Numbers.Progression.StatModifierRemover;
using ArchStatModifierWriter = Core.Numbers.Archetype.StatModifierWriter;

namespace Core.Rules.Assembly
{
    /// <summary>
    /// L1（<c>Core.Numbers</c>）+ L2（<c>core/rules</c> 四模块）+ Expr 宿主的组装根（落地方案 T2-11
    /// 集成任务"三、组装根"）：接收一批"环境依赖"（事件总线、已加载数据、随机源、单位/空间/导航
    /// 查询、世界模拟），按正确顺序装配全部 L1/L2 宿主、把四个 tick 处理器挂到
    /// <see cref="IWorldSim"/> 对应阶段，最终把每个宿主都暴露为只读属性，供调用方（游戏层引导
    /// 代码、集成测试）直接使用，不需要自己摸清十个类型之间谁先谁后、谁依赖谁。
    /// <para>
    /// 装配顺序图、循环依赖处理、tick 阶段挂载表见 <c>core/rules/assembly/README.md</c>。
    /// </para>
    /// </summary>
    public sealed class RulesAssembly
    {
        private static readonly Id ThreatTablePlaceholderId = new Id("system.threat_table_placeholder");

        public IEventBus Bus { get; }
        public IDataRegistryView Registry { get; }
        public IRngHost Rng { get; }
        public IUnitAccess Units { get; }
        public ISpatialQuery Spatial { get; }
        public INavigation2D? Navigation { get; }
        public IWorldSim World { get; }

        public StatHost Stats { get; }
        public PowerHost Powers { get; }
        public ProgressionHost Progression { get; }
        public ArchetypeRegistry Archetypes { get; }
        public FactionMatrix Factions { get; }
        public RulesExprHostFactory ExprHostFactory { get; }
        public TargetHost Targeting { get; }
        public CombatHost Combat { get; }

        /// <summary>本次装配实际使用的 <see cref="CombatOptions"/> 实例（构造参数为空时是本类型
        /// 内部新建的默认值，非空时就是调用方传入的那个实例——ADR-0013 离散时间模型：
        /// <c>Core.Gameplay.Assembly.TimeModelSwitch</c> 需要在切入/切出离散模式时改写
        /// <see cref="CombatOptions.LeaveCombatDelay"/>（按 <c>seconds_per_turn</c> 换算轮数，
        /// 见该类型判断记录），必须拿到与 <see cref="Combat"/> 内部实际使用的同一个实例，本属性
        /// 是它唯一的对外暴露点）。</summary>
        public CombatOptions CombatOptions { get; }

        public SkillHost Skill { get; }
        public AiHost Ai { get; }

        /// <summary>
        /// CORE-170-01 根治（architecture/落地计划/audit-8160178-20260908，P2）：跨来源（装备
        /// <c>grants.auras</c>、套装门槛加成、种族/职业被动）共享的 Aura 实例句柄引用计数账本，
        /// 见 <see cref="AuraHandleLedger"/> 类型注释。<c>CarriersAssembly</c> 把本实例注入
        /// <see cref="Core.Carriers.Item.EquipmentHost"/>（替代此前 <c>EquipmentHost</c> 私有维护的
        /// 那份不与种族共享的计数），本类自己的 <see cref="ReapplyRacePassiveAuras"/>/种族初始注册
        /// （见 <c>archAuraApplier</c> 判断记录）也经同一实例登记引用，使三类来源共享同一份计数。
        /// </summary>
        public AuraHandleLedger AuraHandles { get; }

        /// <summary>
        /// CORE-170-01 根治：种族被动光环各自的私有簿记——"这个单位的种族被动 <c>aura_def</c> 当前
        /// 持有哪一个实例句柄"，惯例同 <see cref="Core.Carriers.Item.EquipmentHost"/> 的
        /// <c>_grantedAuras</c>（该类维护"这件装备实例授予了哪些句柄"，本字典维护"种族这一来源
        /// 对每个 <c>aura_def</c> 持有哪一个句柄"，key 用 <c>(unitId, auraDefId)</c> 而不是
        /// <c>(unitId, instanceId)</c>——种族不是"物品实例"，一个单位同时只有一个种族，直接以
        /// <c>auraDefId</c> 为粒度足够，不需要再套一层"来源实例"分组）。<see
        /// cref="ReapplyRacePassiveAuras"/> 用它判断"这个 <c>aura_def</c> 我（种族）是否已经登记过
        /// 一份引用、且那份引用指向的实例是否仍然是 <c>AuraHost</c> 认得的活实例"——不能只用
        /// <c>HasAura</c>（那只说明"这个 <c>aura_def</c> 在目标身上生效"，不说明生效的是不是种族
        /// 自己持有的引用；装备与种族共享同一 <c>auraDef</c> 时，装备先重放会让 <c>HasAura</c> 变
        /// true，但种族从未为自己登记引用，见 CORE-170-01 判断记录）。<see
        /// cref="OnRaceAuraInstanceReplaced"/> 订阅 <c>InstanceReplaced</c> 保持这份记录与
        /// <c>StackOverflowPolicy.Replace</c> 换句柄同步；跨图 <c>World.ClearAll</c> 不发这个事件
        /// （<c>AuraHost</c> 整个清空该目标的全部实例状态），旧记录变成"指向一个已经不存在的实例"，
        /// 由 <see cref="ReapplyRacePassiveAuras"/> 逐条用 <see cref="IAuraQuery.TryGetInstanceRef"/>
        /// 核实后自行发现并丢弃。
        /// </summary>
        private readonly Dictionary<(Id UnitId, Id AuraDefId), AuraInstanceRef> _raceAuraHandles =
            new Dictionary<(Id, Id), AuraInstanceRef>();

        /// <summary>阶段 3 整理"事项一"：本次装配实际使用的 <see cref="IExprSchema"/>——未传入
        /// <c>extraSchemas</c> 时就是 <see cref="RulesExprSchema.Base"/> 本身，传入时经
        /// <see cref="RulesExprSchema.Compose"/> 与之合并。供调用方（游戏层引导代码）在自己的
        /// <c>DataRegistryOptions.ExprSchema</c>（L4 数据校验）或另行调用 <see cref="ExprParser"/>
        /// 时复用同一份登记表，避免"装配用一份、数据校验用另一份"的偏差（同 ADR-0015 决策 3
        /// "校验期与运行期须用同一份登记表"）。</summary>
        public IExprSchema ExprSchema { get; }

        /// <summary>阶段 3 整理"事项四"：<see cref="Skill"/> 内部持有的 <see cref="IEffectExtension"/>
        /// 延迟绑定代理（见 <see cref="DeferredEffectExtension"/> 判断记录——L3 的组合实现要等
        /// <see cref="Ai"/> 构造完成后才能装配出来，本属性供调用方（<c>CarriersAssembly</c>）在那之后
        /// 调 <see cref="DeferredEffectExtension.Bind"/> 换上真实实现）。构造参数 <c>effectExtension</c>
        /// 非空时已经预先 <c>Bind</c> 过一次，仍可再调 <c>Bind</c> 覆盖。</summary>
        public DeferredEffectExtension EffectExtension { get; }

        /// <summary>RC-11 收边补齐：<see cref="Skill"/> 内部持有的 <see cref="IWeaponDamageQuery"/>
        /// 延迟绑定代理（见 <see cref="DeferredWeaponDamageQuery"/> 判断记录——真实实现
        /// <c>core/carriers/item.EquipmentHost</c> 要等 <see cref="Stats"/>/<see cref="Skill"/> 都
        /// 构造完成后才能装配出来，本属性供调用方（<c>CarriersAssembly</c>）在那之后调
        /// <see cref="DeferredWeaponDamageQuery.Bind"/> 换上真实实现）。未绑定期间
        /// <c>weapon_damage_pct</c> 效果按"无武器"处理（返回 0，见该接口方法注释）。</summary>
        public DeferredWeaponDamageQuery WeaponDamageQuery { get; }

        // -----------------------------------------------------------------
        // time：simTimeProvider/combatStartTimeProvider（见 README"时间来源"一节）
        // -----------------------------------------------------------------

        private double _simTime;
        private readonly Dictionary<Id, double> _combatStartTimes = new Dictionary<Id, double>(EqualityComparer<Id>.Default);

        public double SimTime => _simTime;

        /// <param name="discreteTurnIndexProvider">
        /// W1 收边补齐（A3 审计 #7、#11）：转发给 <see cref="ExprHostFactory"/> 的
        /// <c>time.turn_index</c> provider（见 <see cref="RulesExprHostFactory"/> 构造函数同名参数
        /// 判断记录）。<see cref="RulesExprHostFactory"/> 本身早已支持并测试过这三个可选委托
        /// （<c>core/rules/expr_host/tests/RulesExprHostTests.cs</c> 的
        /// <c>Time_TurnIndexAndRoundIndex_ReadFromInjectedProviders_WhenDiscreteModeWired</c>/
        /// <c>Time_IsMyTurn_TrueWhenCurrentActorMatchesSelf_FalseOtherwise</c> 两例已验证注入假
        /// provider 后三个 Expr key 返回真实值），此前缺的只是"生产组装时传入真实
        /// <c>ITurnScheduler</c>"这一步——本参数就是那个缺口的窄注入点：调用方（<c>core/gameplay/
        /// assembly.GameplayAssembly</c>，持有真正的 <c>ITurnScheduler</c> 实例）在离散模式接通后
        /// 应传 <c>() =&gt; scheduler.CurrentTurnIndex</c>；三者均为 null（默认）时行为与本次改动
        /// 之前完全一致（<c>time.turn_index</c>/<c>time.round_index</c>/<c>time.is_my_turn</c> 恒
        /// 占位值 0/0/false）。
        /// </param>
        /// <param name="discreteRoundIndexProvider">同上，对应 <c>time.round_index</c>，调用方应传
        /// <c>() =&gt; scheduler.RoundIndex</c>。</param>
        /// <param name="discreteCurrentActorProvider">同上，对应 <c>time.is_my_turn</c>，调用方应传
        /// <c>() =&gt; scheduler.GetCurrentActor()</c>。</param>
        public RulesAssembly(
            IEventBus bus,
            IDataRegistryView registry,
            IRngHost rng,
            IUnitAccess units,
            ISpatialQuery spatial,
            IWorldSim world,
            INavigation2D? navigation = null,
            StatHostOptions? statOptions = null,
            CombatOptions? combatOptions = null,
            SkillOptions? skillOptions = null,
            TargetingOptions? targetingOptions = null,
            AiOptions? aiOptions = null,
            IReadOnlyList<IExprSchema>? extraSchemas = null,
            IStaticImmunityProvider? staticImmunity = null,
            IEffectExtension? effectExtension = null,
            bool autoRegisterTickHandlers = true,
            IProjectileSpawner? projectileSpawner = null,
            Func<int>? discreteTurnIndexProvider = null,
            Func<int>? discreteRoundIndexProvider = null,
            Func<Id?>? discreteCurrentActorProvider = null,
            LevelSync? levelSync = null)
        {
            Bus = bus ?? throw new ArgumentNullException(nameof(bus));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Units = units ?? throw new ArgumentNullException(nameof(units));
            Spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
            World = world ?? throw new ArgumentNullException(nameof(world));
            Navigation = navigation;

            ExprSchema = extraSchemas == null || extraSchemas.Count == 0
                ? RulesExprSchema.Base
                : RulesExprSchema.Compose(extraSchemas.ToArray());

            EffectExtension = new DeferredEffectExtension();
            if (effectExtension != null)
            {
                EffectExtension.Bind(effectExtension);
            }

            WeaponDamageQuery = new DeferredWeaponDamageQuery();

            TrackSimTime();

            // -------------------------------------------------------------
            // 1) StatHost（LevelLookup 接 ProgressionHost，见下方判断记录 1：闭包捕获尚未赋值的
            //    局部变量，惯例同 core/rules/skill/core/SkillHost.cs 组合根 ProcHost 触发回调的
            //    处理方式——只要真正调用发生在构造完成之后，提前绑定是安全的）。
            // -------------------------------------------------------------
            ProgressionHost progression = null!;
            var resolvedStatOptions = statOptions ?? new StatHostOptions();
            if (resolvedStatOptions.LevelLookup == null)
            {
                resolvedStatOptions.LevelLookup = unitId => progression.GetLevel(unitId);
            }
            Stats = new StatHost(Registry, Bus, resolvedStatOptions);

            // -------------------------------------------------------------
            // 2) PowerHost（StatLookup 接 StatHost）。
            // -------------------------------------------------------------
            var powerTypes = new List<PowerTypeDefinition>();
            foreach (var record in Registry.GetAll("arch.power_type"))
            {
                powerTypes.Add(new PowerTypeDefinition(record));
            }
            StatLookup statLookup = (unitId, stat) => Stats.GetStat(unitId, stat);
            Powers = new PowerHost(powerTypes, Bus, statLookup);

            // P2-05 同类缓存收口（外部审计 audit-c9ff301-20260909 followup-2026-09-10，见
            // Core.Numbers.PowerSet.PowerHost.Reload 判断记录）：PowerHost 本身是 L1，刻意不持有
            // registry，构造签名只接收预解析的 powerTypes 列表——与 GameplayAssembly 对
            // QuestHost.Reload/DialogHost.Reload 的接线同一惯例，本装配根持有 Registry 与已构造好
            // 的 Powers 引用，订阅一次 DataLoadCompletedEvent，重新解析 arch.power_type 表后整体
            // 替换定义（不触碰任何单位已注册的当前/上限值，见 PowerHost.Reload 判断记录）。
            Bus.Subscribe<DataLoadCompletedEvent>(DataRegistryEventKeys.LoadCompleted, _ =>
            {
                var reloadedPowerTypes = new List<PowerTypeDefinition>();
                foreach (var record in Registry.GetAll("arch.power_type"))
                {
                    reloadedPowerTypes.Add(new PowerTypeDefinition(record));
                }

                Powers.Reload(reloadedPowerTypes);
            });

            // -------------------------------------------------------------
            // 3) ProgressionHost / ArchetypeRegistry（writers 接 Stat/Power）。
            // -------------------------------------------------------------
            ProgStatModifierWriter progressionWriter = (unitId, stat, op, value, sourceId) =>
                Stats.AddModifier(unitId, new StatModifier(stat, ParseOp(op), value, sourceId));
            ProgStatModifierRemover progressionRemover = (unitId, sourceId) => Stats.RemoveModifiersBySource(unitId, sourceId);
            // CORE-170-02 根治：levelSync 原样转发给 ProgressionHost——见 LevelSync 判断记录，
            // RulesAssembly 本身（L2）同样不知道、也不该知道 Unit/PlayerUnit（L3）这个类型，只是
            // 沿途转发调用方（CarriersAssembly）传入的真实实现。
            Progression = new ProgressionHost(Registry, Bus, progressionWriter, progressionRemover, levelSync: levelSync);
            progression = Progression; // 回填第 1 步的闭包捕获。

            // -------------------------------------------------------------
            // RC-06 收边补齐：派生上限（PowerHost.RecomputeMax）/评级换算属性缓存（StatHost.
            // RecomputeRatingStats）此前只在注册时算一次，或要求调用方显式手动调用——升级、装备、
            // 光环变化后都会读到过期的上限/评级值（见外部审计 RC-06、StatHost.RecomputeRatingStats/
            // PowerHost.IsRegistered 判断记录）。这里统一订阅两个已有事件：
            //   1) stat.changed → Powers.RecomputeMax(unitId)：覆盖"属性变化"本身，以及"装备/光环
            //      变化"——装备（EquipmentHost.ApplyGrants/RevertGrants）与光环（AuraHost 的
            //      ApplyStatMods/ReapplyStatMods）都经 IStatHost.AddModifier/RemoveModifiersBySource
            //      写入，真正改变属性值时已经会 Enqueue 一次 stat.changed（见 StatHost.AddModifier/
            //      RemoveModifiersBySource），本订阅只是把"属性变了"这件事再传播给"依赖这个属性
            //      的资源上限"，不需要分别订阅装备/光环各自的领域事件。
            //   2) progression.level_up → Stats.RecomputeRatingStats(unitId)：覆盖"等级"这个不经过
            //      AddModifier/SetBase/RemoveModifiersBySource 三个写入入口、因此不会自动更新
            //      GetStat 缓存的特殊维度（见该方法判断记录）；若某个评级属性因此改变，
            //      RecomputeRatingStats 自己也会 Enqueue stat.changed，继而经上面第 1 条订阅联动
            //      触发 Powers.RecomputeMax——两个订阅合起来覆盖"属性/等级/装备/光环"全部四个来源，
            //      不需要更多订阅点。
            //   3) R08 收边补齐（外部审计 5e779c6，P2）：progression.state_restored →
            //      Stats.RecomputeRatingStats(unitId)——读档恢复等级（ProgressionHost.RestoreState）
            //      不经过 AddXp 的正常升级路径，不会发布 progression.level_up（见
            //      ProgressionRestoredEvent 判断记录"为什么不复用 LevelUpEvent"），第 2 条订阅覆盖
            //      不到"读档"这个来源，评级换算属性缓存会一直停留在读档前的值（外部审计复现）。
            //      与第 2 条调的是完全同一个方法，只是多一个触发来源。
            // -------------------------------------------------------------
            Bus.Subscribe<StatChangedEvent>(StatBlockEventKeys.StatChanged, evt =>
            {
                if (Powers.IsRegistered(evt.UnitId))
                {
                    Powers.RecomputeMax(evt.UnitId);
                }
            });
            Bus.Subscribe<LevelUpEvent>(ProgressionEventKeys.LevelUp, evt => Stats.RecomputeRatingStats(evt.UnitId));
            Bus.Subscribe<ProgressionRestoredEvent>(ProgressionEventKeys.StateRestored, evt => Stats.RecomputeRatingStats(evt.UnitId));

            StatBaseWriter archBaseWriter = (unitId, stat, value) => Stats.SetBase(unitId, stat, value);
            ArchStatModifierWriter archModifierWriter = (unitId, stat, op, value, sourceId) =>
                Stats.AddModifier(unitId, new StatModifier(stat, ParseOp(op), value, sourceId));
            PowerRegistrar archPowerRegistrar = (unitId, types) => Powers.RegisterUnit(unitId, types);

            // W1 收边补齐（race.passive_auras，A3 审计 #8）：闭包提前捕获尚未赋值的 skill 局部
            // 变量，与上面第 1 步 progression 闭包同一种处理手法——ArchetypeRegistry（第 3 步）
            // 构造时 SkillHost（第 5 步）还不存在，只要真正调用（RegisterUnit/ApplyTo）发生在
            // 构造完成之后（第 5 步之后回填 skill = Skill），提前绑定是安全的。
            SkillHost skill = null!;
            // CORE-170-01 根治：种族被动光环的初始施加（RegisterUnit → ArchetypeRegistry.ApplyTo，
            // 单位首次注册、此前从未对这个 aura_def 登记过任何引用，见 _raceAuraHandles 判断记录）
            // 同样要经 AuraHandles 登记一份引用，并记进 _raceAuraHandles——否则同一玩家第一次
            // EnterMap（RegisterUnit 之后、还没发生过 ClearAll）时，ReapplyRacePassiveAuras 会因为
            // _raceAuraHandles 里找不到记录，误判"种族还没登记过"，对着已经存在的实例再登记一份
            // 引用，把计数错误地记成 2（多算一次，卸装时不会归零，光环反而永久残留）。auraHandles
            // 同 skill 一样用局部变量提前捕获闭包、构造完成后回填（见上方 skill 判断记录同一惯例）。
            AuraHandleLedger auraHandles = null!;
            AuraApplier archAuraApplier = (unitId, auraDefId, sourceId) =>
            {
                var granted = skill.EffectSink.ApplyAura(unitId, auraDefId, sourceId);
                auraHandles.Register(unitId, granted.AuraInstanceId);
                _raceAuraHandles[(unitId, auraDefId)] = granted;
            };
            Archetypes = new ArchetypeRegistry(Registry, Bus, archBaseWriter, archModifierWriter, archPowerRegistrar, archAuraApplier);

            // -------------------------------------------------------------
            // 4) FactionMatrix。
            // -------------------------------------------------------------
            Factions = new FactionMatrix(Registry, Bus);

            // -------------------------------------------------------------
            // 5) RulesExprHostFactory / TargetHost / CombatHost / SkillHost：
            //    见下方判断记录 2（IAuraQuery 延迟绑定代理，处理 CombatHost ↔ SkillHost 循环依赖）。
            // -------------------------------------------------------------
            var deferredAuras = new DeferredAuraQuery();

            var strategyRegistry = new TargetStrategyRegistry();
            BuiltinTargetStrategies.RegisterAll(strategyRegistry);
            var resolvedTargetingOptions = targetingOptions ?? new TargetingOptions();

            var resolvedCombatOptions = combatOptions ?? new CombatOptions();
            CombatOptions = resolvedCombatOptions;
            Combat = new CombatHost(
                Stats, Powers, Units, deferredAuras, Factions, Rng, Bus, Registry, resolvedCombatOptions,
                diagnostics: null, staticImmunity: staticImmunity);

            // P2-01 收口（此前判断记录 3 的做法：ExprHostFactory 构造时 skillHost 传 null，
            // TargetHost/SkillHost 永久绑定这份"不带 skillHost"的工厂；下面第 7 步只重新构造第二份
            // 带 skillHost 的工厂给 AiHost 单独用——self.is_casting/combat.is_casting 在
            // Targeting 的 target chain filters 与 Skill 自己的 ProcHost 条件求值里因此恒为
            // false，见 audit-20260907/foundation-rules.md P2-01）。改用与 deferredAuras 同一惯例
            // 的延迟绑定代理 DeferredSkillCastQuery（见本文件下方判断记录 2.5）：SkillHost 构造完成
            // 后立即 Bind，全部三个消费方（Targeting/Skill/Ai）自此共享同一份、也是唯一一份
            // ExprHostFactory，不再需要重新构造第二份工厂。
            var deferredSkillHost = new DeferredSkillCastQuery();

            ExprHostFactory = new RulesExprHostFactory(
                Units, Stats, Powers, deferredAuras, Combat, Combat.GetThreatTable(ThreatTablePlaceholderId),
                Spatial, Factions, () => _simTime, GetCombatStartTime,
                extraGroups: null, skillHost: deferredSkillHost, diagnostics: null, extraSchemas: extraSchemas,
                turnIndexProvider: discreteTurnIndexProvider, roundIndexProvider: discreteRoundIndexProvider,
                currentActorProvider: discreteCurrentActorProvider);

            Targeting = new TargetHost(
                strategyRegistry, Registry, Units, Spatial, Factions, Powers, ExprHostFactory,
                Combat.GetThreatTable(ThreatTablePlaceholderId),
                resolvedTargetingOptions.EmitResolvedEvent ? Bus : null,
                resolvedTargetingOptions);

            var resolvedSkillOptions = skillOptions ?? new SkillOptions();
            // ADR-0027《地面坐标施法请求》：Navigation（本类型第 7 个构造参数，此前只转发给下方
            // 第 7 步 AiHost）一并接进 SkillHost/CastPipeline——地面坐标施法请求的可行走校验
            // （CastPipeline.ValidateGroundPoint）需要同一份 INavigation2D，未注入时该校验跳过
            // （同 Spatial 既有降级惯例），不引入新的必填依赖。
            Skill = new SkillHost(
                Registry, Bus, Units, Stats, Powers, Rng, Combat, Targeting, ExprHostFactory, Spatial,
                resolvedSkillOptions, effectExtension: EffectExtension, diagnostics: null, exprSchema: null,
                staticImmunity: staticImmunity, projectileSpawner: projectileSpawner,
                weaponDamageQuery: WeaponDamageQuery, factions: Factions, navigation: Navigation);
            skill = Skill; // 回填第 3 步 archAuraApplier 闭包捕获的局部变量。

            // CORE-170-01 根治：AuraHandles 需要真实的 IEffectSink（RemoveAura 出口）与
            // IAuraQuery（订阅 InstanceReplaced 自动迁移换句柄后的计数，见 AuraHandleLedger 判断
            // 记录）——两者都要求 Skill 已经构造完成，因此放在 skill = Skill 回填之后。
            AuraHandles = new AuraHandleLedger(Skill.EffectSink, Skill.AuraQuery);
            auraHandles = AuraHandles; // 回填第 3 步 archAuraApplier 闭包捕获的局部变量。
            Skill.AuraQuery.InstanceReplaced += OnRaceAuraInstanceReplaced;

            // -------------------------------------------------------------
            // RC-08 收边补齐：movement 施法中断（skill.def.interrupt_flags 含 "movement" 时读条/
            // 引导应被打断，见 06 第 3.1 节）此前只有 SkillHost.NotifyMoved 这个方法本身，生产装配
            // 从未有人调用它——真实移动只发 unit.moved 事件（core/carriers/unit.MovementTickHandler），
            // core/rules/skill 没有订阅（见外部审计 RC-08）。这里订阅 EventKeys.UnitMoved（<see
            // cref="Core.Foundation.EventBus.EventKeys"/> 由 found.event_catalog 生成的全局事件 key
            // 常量，不是某个具体强类型事件类），转调 Skill.NotifyMoved——本模块（core/rules）不
            // 依赖 core/carriers（见 Core.Rules.csproj 只引用 Core.Numbers，架构分层禁止 L2 反向
            // 依赖 L3），因此不能直接引用 core/carriers/common 定义的强类型 UnitMovedEvent 类，改用
            // 全部事件都实现的 IExprReadableEvent.TryGetField("unitId", ...) 通用读出字段（同一套
            // 机制供 Expr 引擎按事件求值，语义与强类型属性完全一致，见 IExprReadableEvent 类型
            // 注释）。SkillHost.NotifyMoved 内部已经会检查"该单位是否正在读条/引导 且 声明了
            // InterruptFlags.Movement"，本订阅本身不做任何过滤——移动了但没有读条中的技能、或
            // 读条的技能没声明 movement 中断，NotifyMoved 都是安全 no-op（见该方法判断记录）。
            // -------------------------------------------------------------
            Bus.Subscribe(EventKeys.UnitMoved, evt =>
            {
                if (evt is IExprReadableEvent readable &&
                    readable.TryGetField("unitId", out var unitIdValue) &&
                    unitIdValue.Kind == ExprValueKind.Id)
                {
                    Skill.NotifyMoved(unitIdValue.AsId);
                }
            });

            // -------------------------------------------------------------
            // 6) IAuraQuery / ISkillHost 回接：combat 此前拿到的 deferredAuras 代理现在指向真实的
            //    SkillHost.AuraQuery（见判断记录 2）；ExprHostFactory 此前拿到的 deferredSkillHost
            //    代理现在指向真实的 SkillHost 本身（见判断记录 2.5，P2-01 收口）——Targeting/Skill/
            //    Ai 共享的同一份 ExprHostFactory 自此都能读到真实 IsCasting。
            // -------------------------------------------------------------
            deferredAuras.Bind(Skill.AuraQuery);
            deferredSkillHost.Bind(Skill);

            // -------------------------------------------------------------
            // 7) AiHost：与 Targeting/Skill 共享同一份第 5 步的 ExprHostFactory（P2-01 收口后不再
            //    重新构造第二份），self/target/combat 的 is_casting 与 Targeting/Skill 内部求值
            //    读到的是同一个真实 SkillHost。
            // -------------------------------------------------------------
            var resolvedAiOptions = aiOptions ?? new AiOptions();
            Ai = new AiHost(
                Registry, Units, Factions, Powers, Spatial, Skill, Combat.GetThreatTable(ThreatTablePlaceholderId),
                ExprHostFactory, Bus, Rng, Navigation, resolvedAiOptions);

            // -------------------------------------------------------------
            // 8) tick 处理器挂载（见 README"tick 阶段挂载表"）——除非调用方要求延后
            // （autoRegisterTickHandlers=false，见该参数与 RegisterTickHandlers 判断记录）。
            // -------------------------------------------------------------
            if (autoRegisterTickHandlers)
            {
                RegisterTickHandlers();
            }

            TrackCombatStartTimes();
        }

        /// <summary>
        /// 阶段 3 整理"事项四"：把 L2 四个 tick 处理器挂到各自阶段——构造函数默认会自动调用一次
        /// （<c>autoRegisterTickHandlers</c> 缺省 true）；调用方需要在某个阶段内让另一个处理器排在
        /// <see cref="AiTickHandler"/> 之前时（典型场景：<c>CarriersAssembly</c> 的
        /// <c>SummonTickHandler</c> 按任务书拍板必须"先于 AiTickHandler 注册"到
        /// <see cref="TickPhase.AiDecision"/>，见该类型注释——<see cref="IWorldSim.RegisterPhaseHandler"/>
        /// 只能追加、不能插队，一旦 <see cref="AiTickHandler"/> 先注册就再也排不到它前面了），构造期
        /// 传 <c>autoRegisterTickHandlers: false</c> 跳过这一步，等自己把需要排在前面的处理器注册
        /// 完之后，再手动调用本方法补上 L2 的四个（可重复调用会重复挂载，调用方需自行保证只调一次）。
        /// </summary>
        public void RegisterTickHandlers()
        {
            World.RegisterPhaseHandler(TickPhase.AiDecision, new AiTickHandler(Ai));
            World.RegisterPhaseHandler(TickPhase.SkillPipeline, new SkillTickHandler(Skill, bus: Bus));
            World.RegisterPhaseHandler(TickPhase.CombatResolution, new CombatTickHandler(Combat, bus: Bus));
            World.RegisterPhaseHandler(TickPhase.TriggerEvaluation, new PowerTickHandler(Powers));
        }

        /// <summary>
        /// 一站式注册：Stat（<see cref="StatHost.RegisterUnit"/>）+ Archetype 应用
        /// （<see cref="ArchetypeRegistry.ApplyTo"/>，内部按 <c>arch.class</c>/<c>arch.race</c>
        /// 写基础属性/修正、经 <see cref="PowerRegistrar"/> 注册资源池）+ Progression（若
        /// <c>arch.class.level_curve_ref</c> 存在则 <see cref="ProgressionHost.RegisterUnit"/>，
        /// 否则跳过——见判断记录 4）+ AI（<paramref name="aiProfileId"/> 非空时
        /// <see cref="AiHost.RegisterUnit"/>，否则跳过，"AI 可选"）。
        /// </summary>
        /// <param name="factionId">
        /// 判断记录 5：本参数目前不会被本方法使用并写入任何地方——<see cref="IUnitAccess"/> 契约
        /// 只暴露 <see cref="IUnitAccess.GetFaction"/>（只读查询），没有配套的写入方法，单位的
        /// 阵营归属由 <see cref="Units"/> 具体实现自己的注册通道决定（如集成测试
        /// <c>WorldUnitAccess</c> 经 <c>Entity</c> 子类的构造参数设置）。任务书原句把
        /// <c>factionId?</c> 列进 <c>RegisterUnit</c> 签名，本实现原样保留这个参数位置（不破坏
        /// 签名形状），但如实标注"当前不生效"，不假装做了实际没有契约支持的事情；调用方必须在
        /// 调用本方法之前，经 <see cref="Units"/> 自己的机制把该单位的阵营设置好。
        /// </param>
        public void RegisterUnit(
            Id unitId,
            Id classId,
            Id? raceId,
            int level = 1,
            Id? factionId = null,
            Id? aiProfileId = null,
            Vec2 aiSpawnPoint = default)
        {
            Stats.RegisterUnit(unitId);
            Archetypes.ApplyTo(unitId, classId, raceId);

            var classRecord = Registry.Get("arch.class", classId);
            if (classRecord != null && classRecord.TryGetId("level_curve_ref", out var curveId))
            {
                Progression.RegisterUnit(unitId, curveId, level);
            }

            if (aiProfileId.HasValue)
            {
                Ai.RegisterUnit(unitId, aiProfileId.Value, aiSpawnPoint);
            }
        }

        /// <summary>
        /// 种族被动光环跨图丢失根治（architecture/落地计划/audit-85f1f4f-20260908，静态候选转已
        /// 确认）：<see cref="RegisterUnit"/> 首次注册单位时经 <see cref="ArchetypeRegistry.ApplyTo"/>
        /// 施加 <c>race.PassiveAuras</c>，其中属性修正（<c>race.StatMods</c>，经
        /// <see cref="Stats"/> 的修正登记表）与被动光环（经 <see cref="Skill"/> 的
        /// <see cref="AuraHost"/> 运行时实例）生命周期并不一致——跨图切换的既有实现
        /// <c>World.ClearAll</c> 只清空后者（<c>AuraHost</c> 响应 <c>entity.destroyed</c>），属性
        /// 修正登记表不受影响。真实内容（非空 <c>arch.race.passive_auras</c>）复现：玩家实体
        /// <c>ClearAll</c> → 重新 <c>AddEntity</c> → <c>GameplayAssembly.EnterMap</c> 之后，种族
        /// 属性加成还在，被动光环的 buff 状态/触发效果却已经消失，直到下一次显式换种族（重新走
        /// 一遍 <see cref="RegisterUnit"/>）才会被动补上。
        /// <para>
        /// 供 <c>GameplayAssembly.EnterMap</c> 在装备 grants 重放（<see
        /// cref="Core.Carriers.Item.EquipmentHost.ReapplyGrants"/>，CR140-02 收口）之后一并调用
        /// ——不是重新调用完整的 <see cref="ArchetypeRegistry.ApplyTo"/>（那会重复写基础属性/资源池
        /// 注册，属性修正本就没丢，重新写一遍会产生错误的双重叠加，见 <c>ApplyTo</c> 判断记录），
        /// 只重放 <c>race.PassiveAuras</c> 这一项运行时确实会丢失的状态。<paramref name="raceId"/>
        /// 未知（内容已变更的旧存档）时静默跳过，不抛异常。
        /// </para>
        /// <para>
        /// CORE-170-01 根治（architecture/落地计划/audit-8160178-20260908，P2）：此前按 <see
        /// cref="SkillHost.AuraQuery"/>.<c>HasAura</c> 判断是否已经生效、生效就跳过——这个判断只问
        /// "这个 <c>aura_def</c> 在目标身上有没有活实例"，不问"生效的这份实例，种族自己有没有登记
        /// 过一份引用"。装备 <c>grants.auras</c> 与种族 <c>passive_auras</c> 配置同一个 <c>aura_def</c>
        /// 时，<c>EnterMap</c> 先重放装备（见 <c>EquipmentHost.ReapplyGrants</c>）创建了共享实例、
        /// 装备自己在 <see cref="AuraHandles"/> 上登记了引用，种族重放看到 <c>HasAura=true</c> 直接
        /// 跳过、从未为自己登记引用；随后卸下装备释放这唯一一份引用、计数归零，把种族仍然依赖的
        /// 共享实例一并删除（真实探针复现：<c>after_unequip</c> 从预期的
        /// <c>hasAura=true,stacks=1,power=61</c> 变成 <c>hasAura=false,stacks=0,power=11</c>）。
        /// 改为按 <see cref="_raceAuraHandles"/> 判断"种族这个来源自己是否已经持有一份引用、且那份
        /// 引用指向的实例是否仍然是 <c>AuraHost</c> 认得的活实例"（<see
        /// cref="IAuraQuery.TryGetInstanceRef"/>，不能用 <c>ApplyAura</c> 换取句柄——见该方法判断
        /// 记录 stacking 语义）：
        /// <list type="bullet">
        /// <item>有记录且记录指向的实例仍然存活——幂等跳过，种族已经持有一份有效引用，不重复登记
        /// （典型如本方法被意外连续调用两次，或压根没有发生过 <c>ClearAll</c>）。</item>
        /// <item>有记录但记录指向的实例已经不是 <c>AuraHost</c> 认得的活实例（跨图 <c>ClearAll</c>
        /// 摘除了全部实例状态，且没有触发 <see cref="IAuraQuery.InstanceReplaced"/>——那只覆盖
        /// <c>StackOverflowPolicy.Replace</c> 换句柄，不覆盖整个清空）——丢弃这份陈旧簿记（<see
        /// cref="AuraHandleLedger.Forget"/>，目标已经不存在，不调用 <see
        /// cref="IEffectSink.RemoveAura"/>），按"没有记录"处理。</item>
        /// <item>没有记录：如果 <c>AuraHost</c> 上已经存在别的来源（装备/套装门槛加成）施加的活实例
        /// （典型如本方法在装备重放之后运行），只为种族登记一份新引用（<see
        /// cref="AuraHandleLedger.Register"/>），不重新 <c>ApplyAura</c>——否则会被
        /// <c>AuraHost.ReapplyExisting</c> 当作又一次独立施加而叠加层数；如果确实还没有任何来源
        /// 施加过（本方法在装备重放之前运行，或压根没有装备共享同一 <c>aura_def</c>），才真正调用
        /// <c>ApplyAura</c> 施加并登记。</item>
        /// </list>
        /// 两种触发顺序（装备先/种族先重放）都能落到正确的最终状态：不管谁先跑，种族与装备（及套装
        /// 门槛加成）三类来源最终都会在 <see cref="AuraHandles"/> 上各自持有恰好一份引用，任一来源
        /// 单独失效（卸装/换种族）只释放自己那一份，只有全部来源都释放完毕才真正移除共享实例。
        /// </para>
        /// </summary>
        public void ReapplyRacePassiveAuras(Id unitId, Id raceId)
        {
            var race = Archetypes.GetRace(raceId);
            if (race == null)
            {
                return;
            }

            foreach (var auraDefId in race.PassiveAuras)
            {
                var key = (unitId, auraDefId);

                if (_raceAuraHandles.TryGetValue(key, out var recorded))
                {
                    var current = Skill.AuraQuery.TryGetInstanceRef(unitId, auraDefId);
                    if (current.HasValue && current.Value.Equals(recorded))
                    {
                        // 种族已经持有一份有效引用，幂等跳过。
                        continue;
                    }

                    // 记录指向的实例已经不再是 AuraHost 认得的活实例（典型如跨图 ClearAll）——
                    // 丢弃这份陈旧簿记，落到下面"没有记录"的分支重新登记/施加。
                    AuraHandles.Forget(unitId, recorded.AuraInstanceId);
                    _raceAuraHandles.Remove(key);
                }

                var existing = Skill.AuraQuery.TryGetInstanceRef(unitId, auraDefId);
                if (existing.HasValue)
                {
                    // 别的来源（装备 grants.auras/套装门槛加成）已经把这个共享实例施加到位，种族
                    // 只需要为自己登记一份引用，不能重新调用 ApplyAura（见方法判断记录 stacking
                    // 语义）。
                    AuraHandles.Register(unitId, existing.Value.AuraInstanceId);
                    _raceAuraHandles[key] = existing.Value;
                    continue;
                }

                var granted = Skill.EffectSink.ApplyAura(unitId, auraDefId, raceId);
                AuraHandles.Register(unitId, granted.AuraInstanceId);
                _raceAuraHandles[key] = granted;
            }
        }

        /// <summary>
        /// CORE-180-03 根治（architecture/落地计划/audit-e070e3f-20260908，P2，已确认）：读档后
        /// （同图 <c>GameplayAssembly.RestoreFromSlot</c> 与跨图 <c>EnterMap</c> 统一走同一处，见
        /// <c>core/gameplay/assembly/README.md</c>"CORE-180-01/03 根治"一节）按存档写入的新
        /// <paramref name="classId"/>/<paramref name="raceId"/> 重新聚合职业基础属性、种族属性修正
        /// 与种族被动光环——旧实现（<c>UnitPersistable.ArchetypeIdPersistable</c>/<c>RaceIdPersistable</c>.
        /// <c>Load</c>）只写 <c>PlayerUnit.ArchetypeId</c>/<c>RaceId</c> 两个字段本身，从不触碰
        /// <see cref="Stats"/>/<see cref="Skill"/> 任何运行期状态；跨图路径此前靠 <c>GameplayAssembly.
        /// EnterMap</c> 显式调用 <see cref="ReapplyRacePassiveAuras"/> 补种族被动光环这一项，但
        /// 从未处理"种族属性修正来自旧种族、换了种族后旧修正该被移除"这一步（跨图 <c>ClearAll</c>
        /// 不影响 <see cref="StatHost"/> 的修正登记表，见 <c>GameplayAssembly.EnterMap</c> 判断记录
        /// "CR140-02"）；同图路径干脆不调用 <c>EnterMap</c>，连种族被动光环这一项也没有，真实探针
        /// 复现：同图加载另一个有效角色存档后，字段变成新种族，但属性/光环仍是旧种族的（见
        /// architecture/落地计划/audit-e070e3f-20260908/core/core-findings.md CORE-180-03）。
        /// <para>
        /// 判断记录（不复用 <see cref="ArchetypeRegistry.ApplyTo"/> 整体重新调用）：<c>ApplyTo</c>
        /// 内部会经 <c>PowerRegistrar</c> 调用 <see cref="PowerHost.RegisterUnit"/>，而本方法的调用
        /// 场景（读档后重新聚合）里单位早已在游戏开局注册过一次，<c>PowerHost.RegisterUnit</c> 对
        /// 已注册单位会抛 <see cref="InvalidOperationException"/>（"不能重复注册"）——本方法只重放
        /// "基础属性/属性修正/被动光环"三项运行期确实需要跟随存档重新聚合的状态，不触碰资源池注册
        /// 这一步（资源类型集合不随读档变化，见 06 文档"资源类型登记在游戏开局完成一次"惯例）。
        /// </para>
        /// <para>
        /// 判断记录（职业基础属性只"重新 SetBase"，不先移除旧职业贡献）：<see cref="StatHost.SetBase"/>
        /// 是覆盖写入而非累加（<c>unit.Base[stat] = value</c>），对"新旧职业都显式声明了同一个属性
        /// 键"的情形天然幂等正确；若新职业没有声明旧职业曾经声明过的某个属性键，该属性会残留旧职业
        /// 的基础值——这是已知的收边范围边界（见本方法调用点、<c>GameplayAssembly</c> 测试 fixture
        /// 判断记录：验收用的职业 A/B 各自完整声明同一组基础属性键，规避这个边界情形），不在本次
        /// CORE-180-03 的确认范围内（报告本身把 archetype 列为候选而非已确认，只有种族属性修正/
        /// 光环残留是真实探针确认的运行时事实）。
        /// </para>
        /// <para>
        /// 种族部分的移除按 <paramref name="previousRaceId"/> 与 <paramref name="raceId"/> 是否
        /// 相同分两步处理，均为幂等操作：
        /// <list type="bullet">
        /// <item>不同（含"旧值有、新值无"）：先用 <see cref="StatHost.RemoveModifiersBySource"/>
        /// 移除旧种族来源的属性修正（<see cref="ArchetypeRegistry.ApplyTo"/> 写入种族属性修正时用
        /// 种族自身 id 当来源，见该方法判断记录，因此可以直接按旧种族 id 精确移除，不影响其它来源）；
        /// 再逐个旧种族 <c>PassiveAuras</c> 检查 <see cref="_raceAuraHandles"/> 是否仍记录着一份
        /// 有效引用，有则调用 <see cref="AuraHandleLedger.Release"/>（不是 <see
        /// cref="AuraHandleLedger.Forget"/>——本方法处理的是"实例仍然存活、只是种族这个来源不再需要
        /// 它"的场景，必须走正常的引用计数递减，命中零才真正移除共享实例，与装备/套装门槛加成等其它
        /// 共享来源的计数保持一致；<c>Forget</c> 只用于"实例已经因跨图 <c>ClearAll</c> 之类原因不复
        /// 存在，不能再调用 <c>RemoveAura</c>"的场景，见 <see cref="ReapplyRacePassiveAuras"/> 判断
        /// 记录，本方法处理的同图场景不满足这个前提）。</item>
        /// <item>相同：不移除、不重新写入——旧修正本就没有过期，重复写入 <see cref="StatHost.AddModifier"/>
        /// （累加语义）会造成双重叠加，跳过整段移除+重新施加，只在下面统一调用
        /// <see cref="ReapplyRacePassiveAuras"/>（其自身逻辑已经是"已经持有有效引用则跳过"的幂等
        /// 实现）。</item>
        /// </list>
        /// </para>
        /// </summary>
        /// <summary>
        /// CORE-110-02 根治（architecture/落地计划/audit-ac3b622-20260909，P2，已确认）：本方法此前
        /// 完全忽略 <paramref name="previousClassId"/>（见旧判断记录"职业基础属性只重新
        /// SetBase，不先移除旧职业贡献"），换职业时只对新旧职业**共同**声明的基础属性键做覆盖写入
        /// ——旧职业独有的基础属性键从未被任何调用触碰，永久残留旧职业的基础值；<see
        /// cref="PowerHost"/> 的资源类型集合同样从未随职业切换重新对账，旧职业独有的资源类型（如
        /// 法力）在新职业不再声明它之后仍可查询。真实探针复现：A 职业声明
        /// <c>StatClassLegacy=5</c>/<c>Mana</c>，B 职业只声明共同键 <c>StatClassPower</c>；同图从 A
        /// 读到 B 后，<c>StatClassPower</c> 正确变成 B 的值，但 <c>StatClassLegacy</c> 仍是 5、
        /// <c>Mana</c> 仍可查询（应分别为 0/不可查询），见 core-findings.md CORE-110-02。
        /// <para>
        /// 判断记录（清理顺序：先旧种族修正 → 旧职业独有基础键 → 新职业完整基础键 → 新种族修正/
        /// 光环 → 资源类型对账，与 <see cref="ArchetypeRegistry.ApplyTo"/> 首次施加时"基础属性 →
        /// 种族修正/光环 → 资源池注册"的既有顺序保持一致，只是多了"先清理旧值"这一步）：资源类型
        /// 对账必须放在**最后**——<see cref="PowerHost.RegisterUnit"/> 按注册那一刻的 <see
        /// cref="StatHost.GetStat"/> 聚合值初始化资源池上限（<c>max_source.kind == "stat"</c> 时），
        /// 若先重新注册资源池、再应用新职业基础属性/新种族修正，初始上限会用到"还没换完"的中间态
        /// 属性值，产生错误的过渡上限；因此本方法把新增的资源类型对账放在原有全部属性/光环逻辑
        /// 之后（10 文档"依赖顺序"惯例：读档成功后装配根重建顺序是"进度→评级→属性→资源池上限→
        /// 当前值 clamp→种族/职业被动"，资源池永远晚于驱动它上限的属性）。
        /// </para>
        /// <para>
        /// 判断记录（旧职业独有基础键"移除"用 <see cref="StatHost.ResetBase"/> 而非 <c>SetBase</c>
        /// 成某个猜测值）：<see cref="StatHost"/> 没有为"这个键当前的值来自哪个职业"单独记账（不像
        /// 属性修正区分 <c>sourceId</c>），基础值语义上就是"当前生效的唯一一份"；<see
        /// cref="StatHost.ResetBase"/> 把它退回"从未显式设置过"（即 <c>stat.definition.default_base</c>），
        /// 是唯一不需要额外查表就能拿到正确"清理后应该是什么值"的操作。只清理旧职业声明、新职业
        /// **未**声明的键——两边都声明的键之后会被下方"应用新职业完整基础键"覆盖写入，不需要也不
        /// 应该先清理再写入（清理会先触发一次值变化事件，写入再触发第二次，多一次无意义的抖动）。
        /// </para>
        /// <para>
        /// 判断记录（资源类型集合对账用 Unregister+Register 整体替换，不是逐个增删）：<see
        /// cref="IPowerHost"/> 契约（06 原文只给 <c>getPower</c>/<c>getPowerMax</c>/<c>modifyPower</c>
        /// 三个方法，本类型的扩展成员见该接口注释）没有"给已注册单位追加/摘除单个资源类型"的原语，
        /// 只有整体 <see cref="PowerHost.RegisterUnit"/>/<see cref="PowerHost.UnregisterUnit"/>；本方法
        /// 只在新旧职业声明的资源类型集合（忽略顺序，按集合比较）确实不同时才整体替换，集合相同
        /// （含职业未变）时完全跳过，不重置任何资源池的当前值/上限（幂等，避免同职业重复调用本方法
        /// 时把当前生命值之类的运行期状态清零重置）。集合确实不同时，替换会把该单位全部资源池的
        /// 当前值重置为各自资源类型的初始值（<c>start_full</c>），这是可以接受的过渡态——本方法只在
        /// <c>SaveSystem.Load</c> 逐段回放（成功路径的 <c>player.race_id</c> 段，或本次一并根治的
        /// 回滚路径，见 <c>IDerivedStateRebuilder</c>/<c>SaveSystem</c> 判断记录）中被调用，随后
        /// <c>player.equipment</c> 段会重算上限、<c>player.vitals</c> 段会用存档里的真实当前值覆盖
        /// 这份初始值，最终结果不受这个过渡态影响。
        /// </para>
        /// </summary>
        public void ReloadArchetypeAndRace(Id unitId, Id classId, Id? raceId, Id? previousClassId, Id? previousRaceId)
        {
            var raceChanged = !previousRaceId.HasValue || !raceId.HasValue || !previousRaceId.Value.Equals(raceId.Value);

            if (previousRaceId.HasValue && raceChanged)
            {
                Stats.RemoveModifiersBySource(unitId, previousRaceId.Value);

                var oldRace = Archetypes.GetRace(previousRaceId.Value);
                if (oldRace != null)
                {
                    foreach (var auraDefId in oldRace.PassiveAuras)
                    {
                        var key = (unitId, auraDefId);
                        if (_raceAuraHandles.TryGetValue(key, out var recorded))
                        {
                            AuraHandles.Release(unitId, recorded);
                            _raceAuraHandles.Remove(key);
                        }
                    }
                }
            }

            var classChanged = !previousClassId.HasValue || !previousClassId.Value.Equals(classId);
            var oldClass = previousClassId.HasValue ? Archetypes.GetClass(previousClassId.Value) : null;
            var cls = Archetypes.GetClass(classId) ?? throw new ArgumentException($"未知职业 \"{classId}\"", nameof(classId));

            if (classChanged && oldClass != null)
            {
                var newKeys = new HashSet<string>(cls.BaseStats.Select(kv => kv.Key), StringComparer.Ordinal);
                foreach (var kv in oldClass.BaseStats)
                {
                    if (!newKeys.Contains(kv.Key))
                    {
                        Stats.ResetBase(unitId, new Id(kv.Key));
                    }
                }
            }

            foreach (var kv in cls.BaseStats)
            {
                Stats.SetBase(unitId, new Id(kv.Key), kv.Value);
            }

            if (raceId.HasValue)
            {
                if (raceChanged)
                {
                    var race = Archetypes.GetRace(raceId.Value) ?? throw new ArgumentException($"未知种族 \"{raceId.Value}\"", nameof(raceId));
                    foreach (var kv in race.StatMods)
                    {
                        Stats.AddModifier(unitId, new StatModifier(new Id(kv.Key), StatModifierOp.Flat, kv.Value, raceId.Value));
                    }
                }

                // 复用既有幂等施加逻辑：已经持有有效引用则跳过，AuraHost 上已有别的来源施加过的
                // 共享实例只补登记引用，都没有才真正 ApplyAura（见 ReapplyRacePassiveAuras 判断记录）。
                ReapplyRacePassiveAuras(unitId, raceId.Value);
            }

            if (classChanged && oldClass != null && Powers.IsRegistered(unitId) &&
                !SamePowerTypeSet(oldClass.PowerTypes, cls.PowerTypes))
            {
                Powers.UnregisterUnit(unitId);
                Powers.RegisterUnit(unitId, cls.PowerTypes);
            }
        }

        /// <summary>集合比较（忽略顺序、忽略重复）——供 <see cref="ReloadArchetypeAndRace"/> 判断新旧
        /// 职业声明的资源类型集合是否需要整体替换，见该方法判断记录。</summary>
        private static bool SamePowerTypeSet(IReadOnlyList<Id> a, IReadOnlyList<Id> b)
        {
            var setA = new HashSet<Id>(a);
            var setB = new HashSet<Id>(b);
            return setA.SetEquals(setB);
        }

        /// <summary>CORE-170-01 根治：<see cref="_raceAuraHandles"/> 自己的簿记——
        /// <c>StackOverflowPolicy.Replace</c> 换句柄时把种族记录里仍引用 <paramref
        /// name="oldInstanceId"/> 的条目原子迁移到 <paramref name="newInstanceId"/>，惯例同
        /// <c>EquipmentHost.OnAuraInstanceReplaced</c> 对 <c>_grantedAuras</c> 的迁移（各自维护各自
        /// 的簿记，互不代劳；<see cref="AuraHandles"/> 自身的计数迁移由它自己订阅同一事件独立完成，
        /// 见 <see cref="AuraHandleLedger"/> 判断记录）。种族一个来源对同一 <c>auraDefId</c> 至多
        /// 持有一条记录（key 已经是 <c>(unitId, auraDefId)</c>），比 <c>_grantedAuras</c>
        /// 简单，不需要遍历全部条目找匹配。</summary>
        private void OnRaceAuraInstanceReplaced(Id targetId, Id defId, Id oldInstanceId, Id newInstanceId)
        {
            var key = (targetId, defId);
            if (_raceAuraHandles.TryGetValue(key, out var recorded) && recorded.AuraInstanceId.Equals(oldInstanceId))
            {
                _raceAuraHandles[key] = new AuraInstanceRef(newInstanceId);
            }
        }

        private static StatModifierOp ParseOp(string op)
        {
            switch (op)
            {
                case "flat": return StatModifierOp.Flat;
                case "pct": return StatModifierOp.Pct;
                case "mult": return StatModifierOp.Mult;
                default: throw new InvalidOperationException($"未知的 StatModifier op \"{op}\"");
            }
        }

        private double GetCombatStartTime(Id unitId) =>
            _combatStartTimes.TryGetValue(unitId, out var t) ? t : _simTime;

        /// <summary>订阅 <c>sim.tick_started</c>（<see cref="WorldSim"/> 每 tick 用
        /// <c>PublishImmediate</c> 同步派发，见该事件注释）累加 <see cref="_simTime"/>——
        /// <see cref="IWorldSim"/> 契约本身不暴露"当前累计模拟秒数"，本类按 03 第 9 节
        /// <c>sim.tick_started</c> 携带的 <c>dt</c> 字段自行累加，供
        /// <see cref="RulesExprHostFactory"/> 的 <c>time.sim_time</c> 使用。</summary>
        private void TrackSimTime()
        {
            Bus.Subscribe<SimTickStartedEvent>(SimEventKeys.TickStarted, e => _simTime += e.Dt);
        }

        /// <summary>订阅 <c>combat.entered</c> 记录每个单位"最近一次进战"时刻的模拟时间，供
        /// <see cref="RulesExprHostFactory"/> 的 <c>time.since_combat_start</c> 使用。</summary>
        private void TrackCombatStartTimes()
        {
            Bus.Subscribe<CombatEnteredEvent>(RulesEventKeys.CombatEntered, e => _combatStartTimes[e.UnitId] = _simTime);
        }

        /// <summary>
        /// 判断记录 2：<see cref="CombatHost"/> 构造函数要求一个非空 <see cref="IAuraQuery"/>
        /// （光环免疫/吸收判定），而光环状态由 <see cref="SkillHost"/> 管理
        /// （<see cref="SkillHost.AuraQuery"/>）；<see cref="SkillHost"/> 构造函数又要求一个非空
        /// <see cref="ICombatHost"/>（效果落地的结算入口）。两者互相需要对方，任一个先构造都拿
        /// 不到还不存在的另一个。任务书给出两个可选方案（"先建 combat 时传入一个可延迟绑定的
        /// IAuraQuery 代理"或"在 combat 提供 SetAuraQuery"），本类选前者：<see cref="DeferredAuraQuery"/>
        /// 是一个满足 <see cref="IAuraQuery"/> 契约的透明代理，内部持有一个可空的"真实实现"引用，
        /// <see cref="DeferredAuraQuery.Bind"/> 之前调用任何查询方法都会抛
        /// <see cref="InvalidOperationException"/>（不会静默返回错误数据）——选它而不是"在
        /// <c>CombatHost</c> 上加 <c>SetAuraQuery</c>"的理由：后者要求修改 <c>core/rules/combat</c>
        /// 模块源码，超出本任务"允许改动"范围（不含 <c>core/rules/combat</c>），代理模式不需要
        /// 触碰 <c>combat</c>/<c>skill</c> 任何一行代码就能解开循环。
        /// </summary>
        private sealed class DeferredAuraQuery : IAuraQuery
        {
            private IAuraQuery? _real;

            public void Bind(IAuraQuery real) => _real = real ?? throw new ArgumentNullException(nameof(real));

            private IAuraQuery Real => _real ?? throw new InvalidOperationException(
                "DeferredAuraQuery 尚未绑定真实的 IAuraQuery（RulesAssembly 构造尚未完成，" +
                "不应该在组合根构造函数返回之前调用任何查询方法）");

            public bool HasAura(Id unitId, Id auraDefId) => Real.HasAura(unitId, auraDefId);
            public int GetStacks(Id unitId, Id auraDefId) => Real.GetStacks(unitId, auraDefId);
            public ControlFlags GetControlFlags(Id unitId) => Real.GetControlFlags(unitId);
            public bool IsImmune(Id unitId, Id school, EffectKind kind) => Real.IsImmune(unitId, school, kind);
            public double ConsumeAbsorb(Id unitId, Id school, double amount) => Real.ConsumeAbsorb(unitId, school, amount);

            // C03 收口：必须显式转发这个重载，不能依赖 IAuraQuery 默认接口方法的隐式转发——
            // 那会经由本类自己的三参数重载把 triggerChainDepth 悄悄丢回 0，绕开代理直接把
            // Resolver 传入的真实深度吞掉，见 IAuraQuery.ConsumeAbsorb(Id, Id, double, int) 判断记录。
            public double ConsumeAbsorb(Id unitId, Id school, double amount, int triggerChainDepth) =>
                Real.ConsumeAbsorb(unitId, school, amount, triggerChainDepth);
            public IReadOnlyList<Id> GetActiveAuraDefs(Id unitId) => Real.GetActiveAuraDefs(unitId);
            public IReadOnlyList<Id> GetActiveSpellModRefs(Id unitId) => Real.GetActiveSpellModRefs(unitId);
            public Id? ResolveSkillOverride(Id unitId, Id skillId) => Real.ResolveSkillOverride(unitId, skillId);

            // C08 收口：必须显式转发 add/remove，不能依赖 IAuraQuery 默认接口成员的隐式空实现——
            // 那会让订阅者以为自己订阅成功，实际上永远收不到 Real 触发的事件。见
            // IAuraQuery.InstanceReplaced 判断记录、ConsumeAbsorb(Id, Id, double, int) 同一惯例。
            public event Action<Id, Id, Id, Id> InstanceReplaced
            {
                add => Real.InstanceReplaced += value;
                remove => Real.InstanceReplaced -= value;
            }

            // CORE-170-01 根治：同一惯例——必须显式转发，不能依赖 IAuraQuery 默认接口方法的隐式
            // 空实现，否则一旦 Bind 完成，经本代理调用仍会读到默认值 null，绕开 Real 的真实结果。
            public AuraInstanceRef? TryGetInstanceRef(Id unitId, Id auraDefId) => Real.TryGetInstanceRef(unitId, auraDefId);
        }

        /// <summary>
        /// 判断记录 2.5（P2-01 收口，<see cref="ISkillHost"/> 延迟绑定代理，惯例同上方
        /// <see cref="DeferredAuraQuery"/>）：<see cref="RulesExprHostFactory"/>（第 5 步）需要一个
        /// 非空 <see cref="ISkillHost"/> 才能正确求值 <c>self.is_casting</c>/<c>combat.is_casting</c>
        /// （见 <c>core/rules/expr_host/RulesExprHostFactory.cs</c> 判断记录"null skillHost 时按默认值
        /// false + 警告一次处理"），而真正的 <see cref="SkillHost"/>（第 5 步同一批构造）反过来需要
        /// 已经构造好的 <see cref="ExprHostFactory"/> 才能构造自己——与 <c>CombatHost</c> ↔
        /// <c>SkillHost</c>（<see cref="DeferredAuraQuery"/>）同一种循环依赖，同一种解法：先给
        /// <see cref="RulesExprHostFactory"/> 一个代理，<see cref="SkillHost"/> 构造完成后立即
        /// <see cref="Bind"/> 到真实实例（见构造函数第 6 步）。此前的做法是构造两份
        /// <see cref="RulesExprHostFactory"/>（一份 <c>skillHost: null</c> 给 Targeting/Skill 内部
        /// 求值 filters/proc 条件用，一份 <c>skillHost: Skill</c> 只给 AiHost 用）——前者会让
        /// Targeting 的 target chain filters 与 Skill 自己的 <c>ProcHost</c> 条件求值里
        /// <c>is_casting</c> 恒为 false，即便 Skill 本身确实在读条中（见
        /// <c>audit-20260907/foundation-rules.md</c> P2-01）。改用本代理后全部三个消费方
        /// （Targeting/Skill/Ai）自此共享同一份、也是唯一一份 <see cref="ExprHostFactory"/>，不再需要
        /// 重新构造第二份工厂，也不会再有"哪个消费方拿到的是旧工厂"这种隐性差异。
        /// <para>
        /// <see cref="ISkillHost"/> 是一个较宽的接口（<c>core/rules/skill</c> 对外的完整契约，见该
        /// 接口类型注释"供 combat/targeting/ai 三个模块调用"），本代理逐一转发全部成员——不是只转发
        /// <see cref="RulesExprHostFactory"/> 实际用到的 <c>IsCasting</c>，因为 <c>skillHost</c>
        /// 参数的静态类型就是 <see cref="ISkillHost"/> 整个接口，代理必须实现完整契约才能满足类型
        /// 系统，其余成员目前没有调用方经这条路径触达，但仍需要一个转发实现（而不是抛
        /// <see cref="NotSupportedException"/>）以保持"未绑定前调用任何成员都给出同一种可诊断的
        /// 异常信息"这一惯例，与 <see cref="DeferredAuraQuery"/> 一致。
        /// </para>
        /// </summary>
        private sealed class DeferredSkillCastQuery : ISkillHost
        {
            private ISkillHost? _real;

            public void Bind(ISkillHost real) => _real = real ?? throw new ArgumentNullException(nameof(real));

            private ISkillHost Real => _real ?? throw new InvalidOperationException(
                "DeferredSkillCastQuery 尚未绑定真实的 ISkillHost（RulesAssembly 构造尚未完成，" +
                "不应该在组合根构造函数返回之前调用任何查询方法）");

            public Vec2 GetPosition(Id unitId) => Real.GetPosition(unitId);
            public IReadOnlyList<Id> FindUnits(Shape shape, Vec2 origin, UnitFilter filter) => Real.FindUnits(shape, origin, filter);
            public void ApplyStatMod(Id sourceId, Id unitId, Id stat, StatModifierOp op, double value) => Real.ApplyStatMod(sourceId, unitId, stat, op, value);
            public CastResult CastSkill(Id casterId, Id skillId, IReadOnlyList<Id> targets) => Real.CastSkill(casterId, skillId, targets);

            // ADR-0027《地面坐标施法请求》：同上方 CastSkill 及下方 GetSkillReadiness 判断记录，
            // 必须显式转发，不能依赖 ISkillHost.CastSkillAtGround 的默认接口方法隐式兜底（该默认
            // 实现恒返回 GroundTargetUnsupported，见该方法判断记录）——否则本代理绑定完成后经它
            // 调用 CastSkillAtGround 会绕开 Real（真正的 SkillHost）已经能提供的真实裁决，永远
            // 落回"不支持"，见 InterfaceDefaultMemberForwardingTests 门禁。
            public CastResult CastSkillAtGround(Id casterId, Id skillId, GroundCastRequest request) => Real.CastSkillAtGround(casterId, skillId, request);

            public double GetCooldown(Id unitId, Id skillId) => Real.GetCooldown(unitId, skillId);
            public bool IsCasting(Id unitId) => Real.IsCasting(unitId);
            public void Interrupt(Id unitId, Id interrupterId, Id? lockSchool, double lockDuration) => Real.Interrupt(unitId, interrupterId, lockSchool, lockDuration);

            // 消费方反馈（2026-09-11"冷却充能与公共冷却缺少统一只读查询接口"）：同上方全部成员一样
            // 必须显式转发，不能依赖 ISkillHost.GetSkillReadiness 的默认接口方法隐式兜底——否则本代理
            // 绑定完成后经它调用 GetSkillReadiness 仍会落回默认降级快照（其余字段全 null），绕开
            // Real（真正的 SkillHost）已经能提供的精确结果，见 InterfaceDefaultMemberForwardingTests
            // 门禁与该接口成员判断记录。
            public SkillReadiness GetSkillReadiness(Id unitId, Id skillId) => Real.GetSkillReadiness(unitId, skillId);
        }
    }
}
