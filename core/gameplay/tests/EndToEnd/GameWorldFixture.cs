using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;

// 判断记录：Core.Foundation.SaveSystem 命名空间与其内的 SaveSystem 类同名，裸写 "SaveSystem" 会被
// 编译器解析成命名空间本身而报 CS0118（同 core/gameplay/assembly/GameplayAssembly.cs 顶部对
// Core.Gameplay.WorldState 的处理），用别名区分。
using RealSaveSystem = Core.Foundation.SaveSystem.SaveSystem;

namespace Tests.Gameplay.EndToEnd
{
    /// <summary>
    /// 阶段 3 集成收尾"事项五"端到端集成测试的世界组装（<see cref="EndToEndTests"/> 唯一的数据/
    /// 世界来源，惯例同 <c>core/rules/tests/Integration/FightWorldBuilder.cs</c>）：磁盘
    /// <c>data/_sample</c> 经 <see cref="GameplaySchemaCatalog"/> 加载，<see cref="GameplayAssembly"/>
    /// 装配全部 L0～L4 宿主，真实 <see cref="SaveSystem"/>（<see cref="StubFileSystem"/> 桩文件系统）。
    /// 全部数据用中性 id（不出现任何游戏代号）。
    /// </summary>
    internal static class GameWorldFixture
    {
        public static readonly Id MapId = new Id("world.sample_field");
        public static readonly Id PlayerId = new Id("unit.sample_player");
        public static readonly Id NpcId = new Id("unit.sample_hunter_npc");
        public static readonly Id FactionPlayer = new Id("fac.player");
        public static readonly Id ClassSample = new Id("arch.class.sample_a");

        public static readonly Id SkillStrike = new Id("skill.sample_strike");

        public static readonly Id QuestHunt = new Id("quest.sample_hunt");
        public static readonly Id DialogMenu = new Id("dialog.sample_hunter");

        public static readonly Id CreatureBeast = new Id("creature.sample_beast");
        public static readonly Id SpawnBeastField = new Id("spawn.sample_beast_field");
        public static readonly Id SpawnEncounterAmbusher = new Id("spawn.sample_encounter_ambusher");
        public static readonly Id EncounterBeastFight = new Id("encounter.sample_beast_fight");

        /// <summary>收边任务补齐：与 <see cref="EncounterBeastFight"/> 同一批参战单位/胜负条件，唯一
        /// 差异是声明了 <c>combat_mode_override: "discrete"</c>（见 <c>data/_sample/encounter/
        /// encounter.def.json</c>），供 <c>EncounterModeOverrideTests</c> 验证该字段经
        /// <c>GameplayAssembly</c> 接线到 <c>TimeModelSwitch.SetPendingOverride</c> 后真正生效
        /// ——本仓库 <c>found.time_model.combat</c> 示例数据默认 <c>continuous</c>（见
        /// <c>data/_sample/found/found.time_model.json</c>），本遭遇进行期间应仍强制进入离散模式。</summary>
        public static readonly Id EncounterBeastFightModeOverrideDiscrete = new Id("encounter.sample_mode_override_discrete");

        public static readonly Id GobjSavePoint = new Id("gobj.sample_save_point");

        public static readonly Id ItemToken = new Id("item.sample_token");
        public static readonly Id ItemTonic = new Id("item.sample_tonic");
        public static readonly Id ItemBlade = new Id("item.sample_blade");
        public static readonly Id ItemSlotMainHand = new Id("item.slot.sample_main_hand");

        public static readonly Id CurrencyCoin = new Id("econ.currency.sample_coin");
        public static readonly Id AchvHunter = new Id("achv.sample_hunter");

        public static readonly Id StatStrength = new Id("stat.strength");

        public static readonly Id GameId = new Id("game.sample_e2e");
        public static readonly Id SaveSlot = new Id("save.sample_slot");

        /// <summary>固定步长：0.5 模拟秒/tick（技能读条时间与光环周期均不涉及本测试场景，取一个
        /// 比 <c>TwoUnitsFightTests</c> 更细的步长只是为了让 tick 序号与秒数更接近 1:1，不含特殊
        /// 判断记录）。</summary>
        public const double StepSeconds = 0.5;

        public const int MaxTicks = 400;

        public sealed class Fixture
        {
            public IEventBus Bus = null!;
            public List<Core.Foundation.EventBus.IEvent> Events = null!;
            public IDataRegistry Registry = null!;
            public ValidationReport LoadReport = null!;
            public IRngHost Rng = null!;
            public WorldSim World = null!;
            public StubSpatialQuery Spatial = null!;
            public SimClockHost Clock = null!;
            public GameplayAssembly Gameplay = null!;
            public PlayerUnit Player = null!;
            public StubFileSystem FileSystem = null!;
            public RealSaveSystem SaveSystem = null!;

            public void Tick(int count = 1)
            {
                for (var i = 0; i < count; i++)
                {
                    Clock.Advance(StepSeconds);
                }
            }

            public void SubmitCast(Id skillId)
            {
                var args = new JsonObjectBuilder().Add("skill_id", new JsonString(skillId.Value)).Build();
                World.SubmitIntent(new Intent(PlayerId, "cast", args));
            }

            /// <summary>反复提交施法意图直到 <paramref name="targetId"/> 死亡或到达
            /// <paramref name="maxAttempts"/> 次尝试（命中表存在随机 miss/dodge/crit 分支，见
            /// <c>data/_sample/combat/combat.hit_table_config.json</c>，不保证每次都命中，因此循环
            /// 足够多次而不是断言固定命中次数）。返回是否在超时前死亡。</summary>
            public bool CastUntilDead(Id skillId, Id targetId, int maxAttempts = 60)
            {
                for (var i = 0; i < maxAttempts; i++)
                {
                    if (!Units_IsAlive(targetId))
                    {
                        return true;
                    }
                    SubmitCast(skillId);
                    Tick();
                }
                return !Units_IsAlive(targetId);
            }

            private bool Units_IsAlive(Id unitId) => Gameplay.Carriers.Units.Exists(unitId) && Gameplay.Carriers.Units.IsAlive(unitId);
        }

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            // 本源文件固定位于 <repoRoot>/core/gameplay/tests/EndToEnd/GameWorldFixture.cs，
            // 向上 4 级（EndToEnd → tests → gameplay → core）即仓库根（惯例同
            // core/rules/tests/Integration/FightWorldBuilder.cs FindRepoRoot）。
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (var i = 0; i < 4; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException($"源文件路径层级不足，无法定位仓库根目录：{sourceFilePath}");
            }
            return dir.FullName;
        }

        private static FileSystemDataSource BuildRealSampleSource(StubFileSystem fs)
        {
            var repoRoot = FindRepoRoot();
            var sampleRoot = Path.Combine(repoRoot, "data", "_sample");

            foreach (var file in Directory.GetFiles(sampleRoot, "*.json", SearchOption.AllDirectories))
            {
                var rel = file.Substring(sampleRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                fs.WriteTextAtomic("data/_sample/" + rel, File.ReadAllText(file));
            }

            return new FileSystemDataSource(fs, "data/_sample");
        }

        /// <summary>
        /// 收边任务补齐（数据行迁移）：<c>arch.power.health</c> 已迁到
        /// <c>data/_framework/arch/arch.power_type.json</c>，<see cref="ClassSample"/>
        /// （<c>arch.class.sample_a</c>）的 <c>power_types</c> 仍然引用它——本夹具原先只加载
        /// <c>data/_sample</c> 单根，<see cref="RegisterUnit"/> 装配 <see cref="Core.Numbers.
        /// PowerSet.PowerHost"/> 时会因为找不到 <c>arch.power.health</c> 定义整体装配失败。改为与
        /// <c>core/numbers/tests/L1SampleDataTests.cs</c>/<c>core/foundation/data_registry/tests/
        /// DataRegistryTests.cs</c> 同一手法的多根加载（<c>_framework</c> + <c>_sample</c>）。
        /// </summary>
        private static FileSystemDataSource BuildRealFrameworkSource(StubFileSystem fs)
        {
            var repoRoot = FindRepoRoot();
            var frameworkRoot = Path.Combine(repoRoot, "data", "_framework");

            foreach (var file in Directory.GetFiles(frameworkRoot, "*.json", SearchOption.AllDirectories))
            {
                var rel = file.Substring(frameworkRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                fs.WriteTextAtomic("data/_framework/" + rel, File.ReadAllText(file));
            }

            return new FileSystemDataSource(fs, "data/_framework");
        }

        /// <summary>组装一整套全新世界。<paramref name="fileSystem"/> 未提供时新建一个（存档相关用例
        /// 需要在"保存"与"读档"两次 <see cref="Build"/> 之间复用同一个 <see cref="StubFileSystem"/>
        /// 实例，模拟"同一台机器上的磁盘"）。
        /// <para>
        /// 收边任务补齐（<paramref name="enableDiscreteTimeModel"/>）：默认 <c>false</c>，与此前完全
        /// 一致——<see cref="Fixture.Clock"/> 虽然一直存在，但从未传给
        /// <see cref="GameplayAssembly"/> 构造函数的 <c>clockHost</c> 参数，<c>TimeModelSwitch</c>
        /// 因此从未装配。传 <c>true</c> 时把同一个 <see cref="Fixture.Clock"/> 接给
        /// <c>clockHost</c>，供需要真实驱动 <c>encounter.def.combat_mode_override</c> 的测试
        /// （<c>EncounterModeOverrideTests</c>）使用——只做这一个新增测试的专用开关，不改变默认路径，
        /// 本仓库其余全部既有用例（不传该参数）行为不受影响。
        /// </para>
        /// </summary>
        public static Fixture Build(ulong seed = 20260905UL, StubFileSystem? fileSystem = null, bool enableDiscreteTimeModel = false)
        {
            var engine = new StubEngine();
            var fs = fileSystem ?? engine.FileSystem;

            var definitions = EventKeys.All.Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>())).ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = true });
            var events = new List<Core.Foundation.EventBus.IEvent>();
            foreach (var key in EventKeys.All)
            {
                bus.Subscribe(key, e => events.Add(e));
            }

            var frameworkSource = BuildRealFrameworkSource(fs);
            var source = BuildRealSampleSource(fs);
            var options = GameplaySchemaCatalog.CreateOptions();
            // 判断记录（阶段 4 收敛 B 追加）：data/_sample 现在同时装着 L0～L4（本类关心的）与
            // presentation/assembly.PresentationSchemaCatalog 新登记的 L5 表现层表
            // （vfx.def/sfx.def/camera_profile/... 等）；本类是纯 L4 端到端夹具，不应该为了看见
            // 磁盘上共享的 L5 示例数据文件就反过来依赖 presentation/Presentation.Common.csproj
            // （那会是一次向上依赖，违反 01_分层与依赖.md 的层次方向）。FailOnUnknownTable=false
            // 让这些未在 GameplaySchemaCatalog 登记的表按"无 schema 表"只做信封检查后加载，不阻断
            // 本类完全不关心的 L5 数据——这正是 DataRegistryOptions.FailOnUnknownTable 设计出来
            // 要处理的场景。
            options.FailOnUnknownTable = false;
            var registry = new DataRegistry(frameworkSource, bus, options);
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll(new IDataSource[] { frameworkSource, source });
            if (report.IsBlocking)
            {
                throw new InvalidOperationException(
                    "GameWorldFixture 夹具数据未通过校验：" + string.Join("; ", report.Issues));
            }

            var rng = new RngHost(seed);
            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            // 判断记录（缺口 16，ISaveSystem 归 GameplayAssembly 持有）：本夹具改在这里就地构造
            // 唯一一份 RealSaveSystem 并直接传给 GameplayAssembly 构造函数——Fixture.SaveSystem
            // 字段下方复用同一个实例（不再另建一份），与 GameplayAssembly.SaveSystem 属性等价。
            var saveSystem = new RealSaveSystem(fs, new SaveSystemOptions(GameId), bus);

            var clock = new SimClockHost(world, new SimLoopOptions { StepSeconds = StepSeconds, MaxCatchUpSteps = 4 });

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: FactionPlayer,
                clockHost: enableDiscreteTimeModel ? clock : null);

            var player = new PlayerUnit(PlayerId, MapId, FactionPlayer, ClassSample) { Position = new Vec2(0, 0) };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(PlayerId, ClassSample, raceId: null, level: 1);
            spatial.Register(PlayerId, player.Position, 0.5);
            gameplay.Economy.RegisterUnit(PlayerId);

            return new Fixture
            {
                Bus = bus,
                Events = events,
                Registry = registry,
                LoadReport = report,
                Rng = rng,
                World = world,
                Spatial = spatial,
                Clock = clock,
                Gameplay = gameplay,
                Player = player,
                FileSystem = fs,
                SaveSystem = saveSystem,
            };
        }
    }
}
