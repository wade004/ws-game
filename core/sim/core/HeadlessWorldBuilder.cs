using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;

// 判断记录（同 GameWorldFixture 既有判断记录）：Core.Foundation.SaveSystem 命名空间与其内的
// SaveSystem 类同名，裸写 "SaveSystem" 会被编译器解析成命名空间本身而报 CS0118，用别名区分。
using RealSaveSystem = Core.Foundation.SaveSystem.SaveSystem;

namespace Core.Sim
{
    /// <summary>
    /// T-N6-1（ADR-0035 决策 1）：无头世界装配根的构造期选项。默认值与本类型上提之前
    /// <c>Tests.Gameplay.EndToEnd.GameWorldFixture.Build</c> 的硬编码取值逐项一致（见各字段
    /// 注释），保证 <see cref="HeadlessWorldBuilder.Build"/> 落地后 <c>GameWorldFixture</c>
    /// 转发调用不改变任何既有用例的行为。
    /// </summary>
    public sealed class HeadlessWorldOptions
    {
        /// <summary>
        /// 判断记录：装配根不得含 <c>FindRepoRoot</c>/读仓库磁盘路径的逻辑（那属于具体宿主——
        /// <c>GameWorldFixture</c> 或本模块自己的测试工厂——的职责），数据来源必须由调用方构造好
        /// 后注入。至少含一个数据源；多根加载语义（顺序即层次，先声明的根是前层）见
        /// <see cref="Core.Foundation.DataRegistry.DataRegistry.LoadAll(IReadOnlyList{IDataSource})"/>。
        /// </summary>
        public IReadOnlyList<IDataSource> DataSources { get; set; } = Array.Empty<IDataSource>();

        /// <summary>分流随机源主种子。默认值与 <c>GameWorldFixture.Build</c> 此前的硬编码默认值
        /// 一致（该值本身不含语义，只是一个固定的可复现种子）。</summary>
        public ulong Seed { get; set; } = 20260905UL;

        /// <summary>桩文件系统。未提供时新建一个；存档相关场景需要在"保存"与"读档"两次
        /// <see cref="HeadlessWorldBuilder.Build"/> 之间复用同一个实例，模拟"同一台机器上的磁盘"
        /// （用法惯例同 <c>GameWorldFixture.Build</c> 的 <c>fileSystem</c> 参数）。</summary>
        public StubFileSystem? FileSystem { get; set; }

        /// <summary>默认 <c>false</c>：<see cref="HeadlessWorld.Clock"/> 虽然一直存在，但不接给
        /// <see cref="GameplayAssembly"/> 构造函数的 <c>clockHost</c> 参数，<c>TimeModelSwitch</c>
        /// 因此不装配。传 <c>true</c> 时把同一个 <see cref="HeadlessWorld.Clock"/> 接给
        /// <c>clockHost</c>（惯例同 <c>GameWorldFixture.Build</c> 的
        /// <c>enableDiscreteTimeModel</c> 参数）。</summary>
        public bool EnableDiscreteTimeModel { get; set; }

        /// <summary>默认 <c>null</c>：就地新建一份 <c>new SaveSystemOptions(GameId)</c>
        /// （<c>AutoSave</c> 保持默认值）。传入非空值时改用调用方提供的这一份。</summary>
        public SaveSystemOptions? SaveSystemOptions { get; set; }

        /// <summary>默认 <c>false</c>，与 <c>GameWorldFixture.Build</c> 此前的硬编码取值一致——
        /// 允许磁盘上存在未在装配所用 schema 目录登记的表（按"无 schema 表"只做信封检查后加载），
        /// 见 <see cref="DataRegistryOptions.FailOnUnknownTable"/>。</summary>
        public bool FailOnUnknownTable { get; set; }

        /// <summary>玩家出生地图 id。默认值取自既有夹具的 <c>world.sample_field</c>。</summary>
        public Id MapId { get; set; } = new Id("world.sample_field");

        /// <summary>玩家单位 id。默认值取自既有夹具的 <c>unit.sample_player</c>。</summary>
        public Id PlayerId { get; set; } = new Id("unit.sample_player");

        /// <summary>玩家阵营 id。默认值取自既有夹具的 <c>fac.player</c>。</summary>
        public Id PlayerFactionId { get; set; } = new Id("fac.player");

        /// <summary>玩家职业 id。默认值取自既有夹具的 <c>arch.class.sample_a</c>。</summary>
        public Id PlayerClassId { get; set; } = new Id("arch.class.sample_a");

        /// <summary>玩家种族 id，可选。既有夹具从未传过种族，默认 <c>null</c> 与其行为一致。</summary>
        public Id? PlayerRaceId { get; set; }

        /// <summary>玩家初始等级。既有夹具硬编码为 1，默认值与之一致；作为选项开放是为后续标准
        /// 玩家生成器（ADR-0035 决策 2，T-N6 后续任务）铺路，本任务不消费非默认值。</summary>
        public int PlayerLevel { get; set; } = 1;

        /// <summary>玩家出生位置。既有夹具硬编码为原点，默认值与之一致。</summary>
        public Vec2 PlayerSpawnPosition { get; set; } = new Vec2(0, 0);

        /// <summary>玩家空间查询半径。既有夹具硬编码为 0.5，默认值与之一致。</summary>
        public double PlayerSpawnRadius { get; set; } = 0.5;

        /// <summary>存档游戏 id，供 <see cref="Core.Foundation.SaveSystem.SaveSystemOptions"/>
        /// 默认构造使用。默认值取自既有夹具的 <c>game.sample_e2e</c>。</summary>
        public Id GameId { get; set; } = new Id("game.sample_e2e");

        /// <summary>固定步长（模拟秒/tick）。既有夹具默认 0.5 秒/tick，默认值与之一致。</summary>
        public double StepSeconds { get; set; } = 0.5;

        /// <summary>主循环最大补偿步数。既有夹具硬编码为 4，默认值与之一致。</summary>
        public int MaxCatchUpSteps { get; set; } = 4;

        /// <summary>
        /// T-N6-3a（ADR-0035 决策 4 锚点表接入）：标准玩家期望装备品质，供自动装配的
        /// <see cref="AnchorTableSkillBudgetAnchorProvider"/> 构造 <see cref="ExpectedStatCalculator"/>
        /// 时使用。默认 <c>null</c>——提供者按 <c>sim.scenario</c> 首行的 <c>player.quality_id</c>/
        /// <c>item.quality_definition</c> 最低 <c>sort_weight</c> 回退解析（见该提供者类型判断记录
        /// "如何决定用哪一对 (职业,品质) 求值"），调用方只在需要覆盖这一自动解析结果时才需要显式赋值。
        /// 本字段仅当数据源含 <c>sim.anchor</c> 时才被消费（见 <see cref="HeadlessWorldBuilder.Build"/>
        /// "数据源含 sim.anchor 才自动装配锚点提供者"判断记录），数据源不含 <c>sim.anchor</c> 时本字段
        /// 无效果（不会因为设置了本字段就强行要求存在 sim.anchor 数据）。
        /// </summary>
        public Id? ExpectedQualityId { get; set; }

        /// <summary>
        /// T-N6-4 新增（ADR-0035 决策 3）：转发给 <c>GameplayAssembly</c> 构造函数既有的
        /// <c>combatOptions</c> 参数（该参数早已存在，本装配根此前从未使用它、恒隐式传 <c>null</c>，
        /// 见 <see cref="Build"/> 判断记录）。默认 <c>null</c> 时行为不变（<c>CombatHost</c> 就地
        /// 新建一份默认 <c>CombatOptions</c>）。战斗仿真运行器（<see cref="FightRunner"/>）借这个口子
        /// 注入一份携带 <c>CombatOptions.ResolveTrace</c> 回调的实例，按"结算即被观测"的方式把每一次
        /// 伤害/治疗结算归属到具体 <c>skill.def</c>（见 <see cref="FightRunner"/> 判断记录"采样口径"）
        /// ——本属性本身只是纯粹的传参转发，不改变 <c>Core.Rules.Combat</c> 任何既有行为（未设置
        /// <c>ResolveTrace</c> 等回调字段时，<c>CombatOptions</c> 的其余默认值与装配根此前隐式传
        /// <c>null</c> 时 <c>CombatHost</c> 就地新建的默认实例逐项相同）。
        /// </summary>
        public Core.Rules.Combat.CombatOptions? CombatOptions { get; set; }

        /// <summary>
        /// T-N6-5 新增（ADR-0035 决策 3 成长仿真）：转发给 <c>GameplayAssembly</c> 构造函数既有的
        /// <c>lootOptions</c> 参数（同 <see cref="CombatOptions"/> 判断记录一贯做法——该参数早已存在，
        /// 本装配根此前从未使用它、恒隐式传 <c>null</c>）。默认 <c>null</c> 时行为不变（<c>LootHost</c>
        /// 就地新建一份默认 <c>LootOptions</c>，<c>PickupRange</c> 默认 3.0）。成长仿真运行器（<see
        /// cref="GrowthSimulation"/>）借这个口子把 <c>PickupRange</c> 放宽——生物死亡位置由
        /// <c>CreatureDeathLootListener</c> 按死亡单位当前坐标落地掉落物，而战斗交手距离取决于
        /// <c>ai.rotation</c> 技能射程（本数据集为 5，见 <see cref="FightRunner.DefaultEngageRange"/>），
        /// 可能大于默认拾取半径 3.0，成长仿真需要在同一个持续存活的世界里对每次击杀掉落物"确定能捡到"
        /// （否则装备/金币掉落会在地面白白丢弃，掉落条目虽然真实掷出但从未进入背包/入账），而不是像
        /// <see cref="FightRunner"/> 那样每场战斗都新建、从不需要处理拾取。本属性同样只是纯粹的传参
        /// 转发，不改变 <c>Core.Gameplay.Loot</c> 任何既有行为。
        /// </summary>
        public Core.Gameplay.Loot.LootOptions? LootOptions { get; set; }
    }

    /// <summary>
    /// T-N6-1：无头世界装配的结果——持有装配出的整套 L0～L4 宿主与配套设施，供无头运行器（本任务
    /// 只交付装配根本身，三级仿真见 ADR-0035 决策 3 之后的任务）与既有端到端测试夹具共用。
    /// </summary>
    public sealed class HeadlessWorld
    {
        public IEventBus Bus { get; }
        public List<IEvent> Events { get; }
        public IDataRegistry Registry { get; }
        public ValidationReport LoadReport { get; }
        public IRngHost Rng { get; }
        public WorldSim World { get; }
        public StubSpatialQuery Spatial { get; }
        public SimClockHost Clock { get; }
        public GameplayAssembly Gameplay { get; }
        public PlayerUnit Player { get; }
        public StubFileSystem FileSystem { get; }
        public RealSaveSystem SaveSystem { get; }

        /// <summary>T-N6-2a：<c>sim.anchor</c> 的类型化只读读取；数据根未提供任何 <c>sim.anchor</c>
        /// 行时为 <c>null</c>（不抛异常，见 <see cref="HeadlessWorldBuilder.Build"/> 判断记录
        /// "sim.* 表数据可选"）。</summary>
        public AnchorTable? AnchorTable { get; }

        /// <summary>T-N6-2a：<c>sim.scenario</c> 的类型化只读读取；数据根未提供任何 <c>sim.scenario</c>
        /// 行时为 <c>null</c>，同 <see cref="AnchorTable"/>。</summary>
        public ScenarioCatalog? ScenarioCatalog { get; }

        internal HeadlessWorld(
            IEventBus bus,
            List<IEvent> events,
            IDataRegistry registry,
            ValidationReport loadReport,
            IRngHost rng,
            WorldSim world,
            StubSpatialQuery spatial,
            SimClockHost clock,
            GameplayAssembly gameplay,
            PlayerUnit player,
            StubFileSystem fileSystem,
            RealSaveSystem saveSystem,
            AnchorTable? anchorTable,
            ScenarioCatalog? scenarioCatalog)
        {
            Bus = bus;
            Events = events;
            Registry = registry;
            LoadReport = loadReport;
            Rng = rng;
            World = world;
            Spatial = spatial;
            Clock = clock;
            Gameplay = gameplay;
            Player = player;
            FileSystem = fileSystem;
            SaveSystem = saveSystem;
            AnchorTable = anchorTable;
            ScenarioCatalog = scenarioCatalog;
        }
    }

    /// <summary>
    /// T-N6-1（ADR-0035 决策 1）：无头世界装配根——把此前分散在
    /// <c>Tests.Gameplay.EndToEnd.GameWorldFixture.Build</c> 里的"组装一整套世界"逻辑（事件目录/
    /// 总线、DataRegistry 装载、RngHost、WorldSim、StubSpatialQuery、RealSaveSystem、
    /// SimClockHost、GameplayAssembly、玩家单位注册）上提为框架交付物，供无头运行器与既有测试
    /// 夹具共用。不含任何读仓库磁盘路径的逻辑——数据来源经 <see cref="HeadlessWorldOptions.DataSources"/>
    /// 注入。
    /// </summary>
    public static class HeadlessWorldBuilder
    {
        public static HeadlessWorld Build(HeadlessWorldOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.DataSources == null || options.DataSources.Count == 0)
            {
                throw new ArgumentException(
                    "HeadlessWorldOptions.DataSources 不能为空——装配根不读仓库磁盘路径，数据来源必须由调用方注入。",
                    nameof(options));
            }

            var fs = options.FileSystem ?? new StubFileSystem();

            var definitions = EventKeys.All.Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>())).ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = true });
            var events = new List<IEvent>();
            foreach (var key in EventKeys.All)
            {
                bus.Subscribe(key, e => events.Add(e));
            }

            var registryOptions = GameplaySchemaCatalog.CreateOptions();
            registryOptions.FailOnUnknownTable = options.FailOnUnknownTable;
            var registry = new DataRegistry(options.DataSources[0], bus, registryOptions);

            // T-N6-3a（ADR-0035 决策 4 锚点表接入；06 第 405 行勘误）：判断记录（"数据源含 sim.anchor
            // 才自动装配锚点提供者"，任务书"HeadlessWorldOptions 新增选项（默认自动：数据源含
            // sim.anchor 则装配提供者）"）——本决策必须在 registry.LoadAll 之前作出（SkillBudget
            // ValidationRule/ItemGrantValueExceedsShareRule 的构造参数须在 GameplaySchemaCatalog
            // .RegisterAll 时就确定，见两条规则类型判断记录"为 null 时整条跳过"），但此时数据尚未
            // 加载、无法用"registry.GetAll("sim.anchor").Count > 0"这一 AnchorTable/ScenarioCatalog
            // 已有的判断法（见本方法下方那两个属性的既有判断记录）。改用
            // AnchorTableSkillBudgetAnchorProvider.DataSourcesHaveAnchorRows 预扫描（见该方法判断
            // 记录"为何不能只看 ListTables 是否列出该表名"——games/_template 登记了一份零行的
            // sim.anchor.json，只看表名存在会误判）。真正装配的 AnchorTableSkillBudgetAnchorProvider
            // 只持有 registry 引用（不在此处读取任何 sim.anchor 行），见该类型判断记录"惰性持有……
            // 不在构造期读取任何数据行"。
            Core.Rules.Common.ISkillBudgetAnchorProvider? anchorProvider =
                AnchorTableSkillBudgetAnchorProvider.DataSourcesHaveAnchorRows(options.DataSources)
                    ? new AnchorTableSkillBudgetAnchorProvider(registry, options.PlayerClassId, options.ExpectedQualityId)
                    : null;

            GameplaySchemaCatalog.RegisterAll(
                registry, itemBudgetCurveId: null, creatureTemplateQuery: null,
                economyValueCurveId: null, economyPriceDeviationThreshold: null, anchorProvider: anchorProvider);
            // T-N6-2a（ADR-0035 决策 4）：sim.anchor/sim.scenario 仅无头仿真与内容工具读取、运行期
            // 宿主不读（见 SimSchemaCatalog 类型注释判断记录"为何不并入 GameplaySchemaCatalog"）——
            // 装配根既服务运行期宿主（GameWorldFixture 转发调用）又服务无头仿真本身，两张表因此在
            // GameplaySchemaCatalog.RegisterAll 之后单独追加登记，不进 GameplaySchemaCatalog 本身。
            SimSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll(options.DataSources);
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "HeadlessWorldBuilder 装配数据未通过校验：" + string.Join("; ", report.Issues));
            }

            var rng = new RngHost(options.Seed);
            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            // 判断记录（缺口 16 沿用，随本次上提搬迁）：ISaveSystem 归 GameplayAssembly 持有——
            // 这里就地构造唯一一份 RealSaveSystem 并直接传给 GameplayAssembly 构造函数，
            // HeadlessWorld.SaveSystem 字段下方复用同一个实例（不再另建一份）。
            var saveSystem = new RealSaveSystem(fs, options.SaveSystemOptions ?? new SaveSystemOptions(options.GameId), bus);

            var clock = new SimClockHost(world, new SimLoopOptions { StepSeconds = options.StepSeconds, MaxCatchUpSteps = options.MaxCatchUpSteps });

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => options.PlayerId,
                playerFactionId: options.PlayerFactionId,
                clockHost: options.EnableDiscreteTimeModel ? clock : null,
                combatOptions: options.CombatOptions,
                lootOptions: options.LootOptions);

            var player = new PlayerUnit(options.PlayerId, options.MapId, options.PlayerFactionId, options.PlayerClassId)
            {
                Position = options.PlayerSpawnPosition,
            };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(options.PlayerId, options.PlayerClassId, options.PlayerRaceId, options.PlayerLevel);

            // T-N6-4b 根治（设计层复核发现"L20 越级矩阵断层"，根因见判断记录"玩家按等级 > 1 直接
            // 出生时成长未写入"）：Core.Rules.Assembly.RulesAssembly.RegisterUnit（供玩家使用的
            // 版本）只调用 Progression.RegisterUnit（登记"当前在哪条曲线的第几级"这一记账状态），
            // 不像 Core.Carriers.Creature.CreatureFactory.SpawnCore 那样紧接着调用
            // Progression.ApplyGrowthToCurrentLevel 把"2 级到出生等级"的曲线成长写成属性修正——这不是
            // 遗漏个例，是 Core.Numbers.Progression.IProgressionHost.RegisterUnit 契约文档原文明确
            // 要求调用方自己做的第二步："startLevel > 1 时，调用方如果需要把……曲线成长一次性写为
            // 修正，紧随其后显式调用 ApplyGrowthToCurrentLevel"（见该方法契约注释）。此前仓库内全部
            // 调用 HeadlessWorldOptions.PlayerLevel 的既有场景恒为默认值 1（T-N6-1 该选项判断记录
            // "本任务本身只消费默认值"），这条"调用方自己负责第二步"的义务从未被触发过、因此从未
            // 暴露——直到 T-N6-4 的 ArenaSimulation 第一次真正让玩家在等级 5/10/15/20 直接出生。
            // 未补这一步时，玩家的基础属性（含 stat.stamina，驱动生命值上限）停留在 1 级的
            // arch.class.base_stats 原始值，装备加成仍会正常叠加，但"应有的成长量"完全缺失——
            // 诊断复现：L20 标准玩家在未打过一次 RecomputeMax 之前 GetPowerMax(Health) 只有 150
            // （1 级基础值 + 尚未叠加成长），而不是仿真实测应有的 ~1022；越级矩阵里"L20 行胜率沿
            // 偏移骤变、不单调"正是这一缺陷的放大镜——玩家实际生命远低于预期，任何一点等级差表/
            // 生物伤害曲线的正常小幅波动都会在"玩家只有 150 血"这个错误基线上被放大成"多打一拍就是
            // 死、少打一拍就是活"的临界效应（见 core/sim/tests/data/README.md"T-N6-4b 根因排查"）。
            // 本修复只是把 Core.Carriers.Creature.CreatureFactory 早已示范过的同一套两步调用顺序
            // （RegisterUnit 紧接 ApplyGrowthToCurrentLevel）在玩家这一侧也照做一遍，不改动
            // core/rules/core/numbers 任何一行、不新增任何公开成员，纯粹是装配根内部调用顺序的
            // 补全；guard 条件复刻 RulesAssembly.RegisterUnit 内部判断
            // classRecord.TryGetId("level_curve_ref", ...) 是否成立的同一逻辑（该类没有登记成长
            // 曲线时 Progression.RegisterUnit 从未被调用，直接调用 ApplyGrowthToCurrentLevel 会因
            // "单位未注册"抛 ArgumentException，见该方法契约注释）。
            var playerClassRecord = registry.Get("arch.class", options.PlayerClassId);
            if (playerClassRecord != null && playerClassRecord.TryGetId("level_curve_ref", out _))
            {
                gameplay.Carriers.Rules.Progression.ApplyGrowthToCurrentLevel(options.PlayerId);

                // 判断记录（补写成长之后为何还要手动 RecomputeMax + RefillAll，不能只指望
                // RulesAssembly 既有的 stat.changed → Powers.RecomputeMax 订阅自动生效）：
                // CreatureFactory.SpawnCore 对生物是"先成长、后 Powers.RegisterUnit"，资源池注册
                // 那一刻读到的基础属性已经是成长后的值，天生正确，不需要任何后续补救。但
                // RulesAssembly.RegisterUnit（玩家走的路径）里 Powers.RegisterUnit 是在
                // Archetypes.ApplyTo 内部完成的、且发生在本方法上面调用 RegisterUnit 那一行——
                // 也就是先于这里才补上的 ApplyGrowthToCurrentLevel，这个顺序在
                // RulesAssembly.RegisterUnit 内部固定、不可重排（该方法属于"被仿真模块"，本任务
                // 硬性规则不得修改）。资源池初次注册时已经按"成长前"的基础属性值把当前值/上限都
                // 定格为 StartFull 的那个（偏低的）数字；stat.changed 订阅只会调用
                // Powers.RecomputeMax（重新按最新属性算上限），其判断记录原文明确"上限下降导致
                // 当前值超出新上限时才夹取当前值"——只处理下降夹取，不处理上限上升后当前值该不该
                // 跟着涨，因此单靠这条既有订阅无法让"当前值"追上成长后应有的上限。显式调用
                // RecomputeMax（保证不依赖事件何时被 DispatchPending 处理——本调用发生在首次
                // world.Clock.Advance 之前，若单纯指望事件订阅，"出生即成长"的玩家会在第一个
                // tick 结算之前始终顶着注册时的偏低生命上限）+ RefillAll（sourceId 复用
                // ProgressionEventKeys.LevelUp，同 T-N4-5"升级回满"既有惯例的来源标记）两步，是
                // "凭空出生在等级 N"与"从 1 级一路升到 N 级、每级都回满一次"这两条路径在效果上
                // 应该等价的最小补齐——不新增任何 core/rules/core/numbers 的公开成员，只是把它们
                // 已经公开的既有方法在正确的时机调用一遍。
                gameplay.Carriers.Rules.Powers.RecomputeMax(options.PlayerId);
                gameplay.Carriers.Rules.Powers.RefillAll(options.PlayerId, Core.Numbers.Progression.ProgressionEventKeys.LevelUp);

                // 复审整合项 5（交叉引用，2026-09-16）：本节"先成长、再手动 RecomputeMax +
                // RefillAll"是装配根对"凭空出生在等级 N"这一特殊场景的补救，不是通用升级路径本身。
                // 同一类"回满时序"问题在真实升级路径（逐级 GrantXp 到达新等级）上，已由
                // ProgressionHost.AddXpCore（在最后一级发布 LevelUpEvent 前先写入聚合成长）与
                // RulesAssembly 订阅（先 RecomputeMax 再 RefillAll）根治，见深度复审 D-M2；两处是
                // 同一构型问题在两条不同路径（出生即高等级 vs 逐级升级）各自的落地点，互不重复也
                // 互不依赖，此处仅作交叉引用，不改变本节既有逻辑。
            }

            spatial.Register(options.PlayerId, player.Position, options.PlayerSpawnRadius);
            gameplay.Economy.RegisterUnit(options.PlayerId);

            // 判断记录（sim.* 表数据可选）：SimSchemaCatalog.RegisterAll 总是登记 schema，但数据根
            // 完全可以不提供任何 sim.anchor/sim.scenario 行（如既有端到端测试夹具的数据集，见
            // GameWorldFixture 转发调用）——registry.GetAll 对"schema 已注册但零行"返回空列表，不抛
            // 异常（DataRegistry.GetAllUnchecked 判断记录），这里按"是否有至少一行"决定是否构造类型化
            // 读取，不得为零行场景抛异常（任务书"数据里无 sim 表时为 null 或空，不得抛"）。
            var anchorTable = registry.GetAll("sim.anchor").Count > 0 ? new AnchorTable(registry) : null;
            var scenarioCatalog = registry.GetAll("sim.scenario").Count > 0 ? new ScenarioCatalog(registry) : null;

            // T-N6-4（ADR-0035 决策 3）：数据源含 sim.anchor 时才装配真实的等级缩放器——与
            // anchorProvider（本方法上方）同一惯例"数据源含 sim.anchor 才自动装配"，但本处没有等价的
            // 构造期预扫描手段（见 CreatureFactory.LevelScaler 判断记录"改为构造后可写的公开属性"），
            // 必须等 anchorTable 在这里真正构造出来之后才能判断、才能装配。未装配时
            // CreatureFactory.LevelScaler 保持默认 null，6 参 Spawn(...,level) 仍按
            // ICreatureLevelScaler 类型判断记录"未注入时等级改、属性不变"退化，不抛异常、不阻断。
            if (anchorTable != null)
            {
                gameplay.Carriers.Creatures.LevelScaler = new AnchorCreatureLevelScaler(registry, anchorTable);
            }

            return new HeadlessWorld(
                bus, events, registry, report, rng, world, spatial, clock, gameplay, player, fs, saveSystem,
                anchorTable, scenarioCatalog);
        }
    }
}
