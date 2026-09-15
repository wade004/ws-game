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
            GameplaySchemaCatalog.RegisterAll(registry);
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
                clockHost: options.EnableDiscreteTimeModel ? clock : null);

            var player = new PlayerUnit(options.PlayerId, options.MapId, options.PlayerFactionId, options.PlayerClassId)
            {
                Position = options.PlayerSpawnPosition,
            };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(options.PlayerId, options.PlayerClassId, options.PlayerRaceId, options.PlayerLevel);
            spatial.Register(options.PlayerId, player.Position, options.PlayerSpawnRadius);
            gameplay.Economy.RegisterUnit(options.PlayerId);

            // 判断记录（sim.* 表数据可选）：SimSchemaCatalog.RegisterAll 总是登记 schema，但数据根
            // 完全可以不提供任何 sim.anchor/sim.scenario 行（如既有端到端测试夹具的数据集，见
            // GameWorldFixture 转发调用）——registry.GetAll 对"schema 已注册但零行"返回空列表，不抛
            // 异常（DataRegistry.GetAllUnchecked 判断记录），这里按"是否有至少一行"决定是否构造类型化
            // 读取，不得为零行场景抛异常（任务书"数据里无 sim 表时为 null 或空，不得抛"）。
            var anchorTable = registry.GetAll("sim.anchor").Count > 0 ? new AnchorTable(registry) : null;
            var scenarioCatalog = registry.GetAll("sim.scenario").Count > 0 ? new ScenarioCatalog(registry) : null;

            return new HeadlessWorld(
                bus, events, registry, report, rng, world, spatial, clock, gameplay, player, fs, saveSystem,
                anchorTable, scenarioCatalog);
        }
    }
}
