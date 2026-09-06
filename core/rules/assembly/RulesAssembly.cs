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
            Func<Id?>? discreteCurrentActorProvider = null)
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

            // -------------------------------------------------------------
            // 3) ProgressionHost / ArchetypeRegistry（writers 接 Stat/Power）。
            // -------------------------------------------------------------
            ProgStatModifierWriter progressionWriter = (unitId, stat, op, value, sourceId) =>
                Stats.AddModifier(unitId, new StatModifier(stat, ParseOp(op), value, sourceId));
            ProgStatModifierRemover progressionRemover = (unitId, sourceId) => Stats.RemoveModifiersBySource(unitId, sourceId);
            Progression = new ProgressionHost(Registry, Bus, progressionWriter, progressionRemover);
            progression = Progression; // 回填第 1 步的闭包捕获。

            StatBaseWriter archBaseWriter = (unitId, stat, value) => Stats.SetBase(unitId, stat, value);
            ArchStatModifierWriter archModifierWriter = (unitId, stat, op, value, sourceId) =>
                Stats.AddModifier(unitId, new StatModifier(stat, ParseOp(op), value, sourceId));
            PowerRegistrar archPowerRegistrar = (unitId, types) => Powers.RegisterUnit(unitId, types);

            // W1 收边补齐（race.passive_auras，A3 审计 #8）：闭包提前捕获尚未赋值的 skill 局部
            // 变量，与上面第 1 步 progression 闭包同一种处理手法——ArchetypeRegistry（第 3 步）
            // 构造时 SkillHost（第 5 步）还不存在，只要真正调用（RegisterUnit/ApplyTo）发生在
            // 构造完成之后（第 5 步之后回填 skill = Skill），提前绑定是安全的。
            SkillHost skill = null!;
            AuraApplier archAuraApplier = (unitId, auraDefId, sourceId) =>
                skill.EffectSink.ApplyAura(unitId, auraDefId, sourceId);
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

            ExprHostFactory = new RulesExprHostFactory(
                Units, Stats, Powers, deferredAuras, Combat, Combat.GetThreatTable(ThreatTablePlaceholderId),
                Spatial, Factions, () => _simTime, GetCombatStartTime,
                extraGroups: null, skillHost: null, diagnostics: null, extraSchemas: extraSchemas,
                turnIndexProvider: discreteTurnIndexProvider, roundIndexProvider: discreteRoundIndexProvider,
                currentActorProvider: discreteCurrentActorProvider);
            // 判断记录 3：ExprHostFactory 构造时 skillHost 传 null——此刻 SkillHost 还不存在
            // （SkillHost 的构造反过来需要 IExprHostFactory，见判断记录 2 同一循环）。
            // RulesExprHostFactory 对 skillHost 缺失的处理已经是"按默认值 false + 警告一次"
            // （见 core/rules/expr_host/README.md），不是硬性依赖；SkillHost 构造完成后
            // 用 <see cref="BindSkillHostIntoExprFactory"/> 反射式手段行不通（字段是 readonly），
            // 因此改为下面第 7 步把 ExprHostFactory 换成一个感知到 SkillHost 的新实例——见该步骤
            // 注释。

            Targeting = new TargetHost(
                strategyRegistry, Registry, Units, Spatial, Factions, Powers, ExprHostFactory,
                Combat.GetThreatTable(ThreatTablePlaceholderId),
                resolvedTargetingOptions.EmitResolvedEvent ? Bus : null,
                resolvedTargetingOptions);

            var resolvedSkillOptions = skillOptions ?? new SkillOptions();
            Skill = new SkillHost(
                Registry, Bus, Units, Stats, Powers, Rng, Combat, Targeting, ExprHostFactory, Spatial,
                resolvedSkillOptions, effectExtension: EffectExtension, diagnostics: null, exprSchema: null,
                staticImmunity: staticImmunity, projectileSpawner: projectileSpawner);
            skill = Skill; // 回填第 3 步 archAuraApplier 闭包捕获的局部变量。

            // -------------------------------------------------------------
            // 6) IAuraQuery 回接：combat 此前拿到的 deferredAuras 代理现在指向真实的
            //    SkillHost.AuraQuery（见判断记录 2）。
            // -------------------------------------------------------------
            deferredAuras.Bind(Skill.AuraQuery);

            // -------------------------------------------------------------
            // 7) 重新构造一份感知到 SkillHost 的 ExprHostFactory，供 AiHost 使用（self/target/
            //    combat 的 is_casting 需要 ISkillHost，见判断记录 3）。TargetHost/SkillHost 已经
            //    绑定了第 5 步那份"不带 skillHost"的工厂——它们只在各自模块内部消费
            //    IExprHostFactory 来求值 filters/proc 条件，不涉及 is_casting，继续用旧实例不影响
            //    正确性，不重新构造它们；只有 AiHost（构造期注入的 IExprHostFactory 用于
            //    Rotation/transitions 条件，很可能引用 self.is_casting/combat.is_casting）换上
            //    带 skillHost 的新工厂。<see cref="ExprHostFactory"/> 属性对外暴露的是这份"完整版"。
            // -------------------------------------------------------------
            ExprHostFactory = new RulesExprHostFactory(
                Units, Stats, Powers, deferredAuras, Combat, Combat.GetThreatTable(ThreatTablePlaceholderId),
                Spatial, Factions, () => _simTime, GetCombatStartTime,
                extraGroups: null, skillHost: Skill, diagnostics: null, extraSchemas: extraSchemas,
                turnIndexProvider: discreteTurnIndexProvider, roundIndexProvider: discreteRoundIndexProvider,
                currentActorProvider: discreteCurrentActorProvider);

            // -------------------------------------------------------------
            // 8) AiHost。
            // -------------------------------------------------------------
            var resolvedAiOptions = aiOptions ?? new AiOptions();
            Ai = new AiHost(
                Registry, Units, Factions, Powers, Spatial, Skill, Combat.GetThreatTable(ThreatTablePlaceholderId),
                ExprHostFactory, Bus, Rng, Navigation, resolvedAiOptions);

            // -------------------------------------------------------------
            // 9) tick 处理器挂载（见 README"tick 阶段挂载表"）——除非调用方要求延后
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
            public IReadOnlyList<Id> GetActiveAuraDefs(Id unitId) => Real.GetActiveAuraDefs(unitId);
            public IReadOnlyList<Id> GetActiveSpellModRefs(Id unitId) => Real.GetActiveSpellModRefs(unitId);
            public Id? ResolveSkillOverride(Id unitId, Id skillId) => Real.ResolveSkillOverride(unitId, skillId);
        }
    }
}
