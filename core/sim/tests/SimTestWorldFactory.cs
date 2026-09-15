using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Tests.Sim
{
    /// <summary>
    /// T-N6-1 确定性证明测试的世界组装：与 <c>Tests.Gameplay.EndToEnd.GameWorldFixture</c> 同一惯例
    /// （见 <c>core/rules/tests/Integration/FightWorldBuilder.cs</c>/
    /// <c>core/gameplay/tests/EndToEnd/GameWorldFixture.cs</c> 的 <c>FindRepoRoot</c>）——本类型是
    /// <c>Tests.Sim</c> 自己的一份仓库路径定位小工具，不依赖 <c>Tests.Gameplay</c>（两者是并列的测试
    /// 工程，互不引用），只负责"从磁盘读 data/_framework + data/_sample 构造
    /// <see cref="FileSystemDataSource"/>，再调用生产代码 <see cref="HeadlessWorldBuilder.Build"/>"，
    /// 与 <see cref="Core.Sim.HeadlessWorldBuilder"/> 本身不含仓库路径逻辑的判断记录一致。
    /// </summary>
    internal static class SimTestWorldFactory
    {
        public static readonly Id MapId = new Id("world.sample_field");
        public static readonly Id PlayerId = new Id("unit.sample_player");
        public static readonly Id FactionPlayer = new Id("fac.player");
        public static readonly Id ClassSample = new Id("arch.class.sample_a");
        public static readonly Id SkillStrike = new Id("skill.sample_strike");
        public static readonly Id SpawnBeastField = new Id("spawn.sample_beast_field");
        public static readonly Id GameId = new Id("game.sample_e2e");

        // T-N6-2b：嵌入式最小仿真数据集（core/sim/tests/data，见该目录 README.md）专用常量——与上方
        // data/_sample 夹具的常量并列、互不混用（两套数据集的 world.map/arch.class 等 id 并不通用）。
        public static readonly Id EmbeddedMapId = new Id("world.sim_arena");
        public static readonly Id EmbeddedClassId = new Id("arch.class.sim_warrior");
        public static readonly Id EmbeddedSkillBookId = new Id("skill.book.sim_warrior");
        public static readonly Id EmbeddedGameId = new Id("game.sim_embedded");
        public static readonly Id EmbeddedCreatureWolfL1 = new Id("creature.sim_wolf_l1");

        public const double StepSeconds = 0.5;

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            // 本源文件固定位于 <repoRoot>/core/sim/tests/SimTestWorldFactory.cs，向上 3 级
            // （tests → sim → core）即仓库根。
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (var i = 0; i < 3; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException($"源文件路径层级不足，无法定位仓库根目录：{sourceFilePath}");
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

        /// <summary>构造一个全新的无头世界（framework + sample 两根，惯例同
        /// <c>GameWorldFixture.BuildRealFrameworkSource</c>/<c>BuildRealSampleSource</c>）。</summary>
        public static Core.Sim.HeadlessWorld BuildWorld(ulong seed)
        {
            var fs = new StubFileSystem();
            var repoRoot = FindRepoRoot();
            var frameworkSource = BuildDiskSource(fs, repoRoot, "data/_framework");
            var sampleSource = BuildDiskSource(fs, repoRoot, "data/_sample");

            return Core.Sim.HeadlessWorldBuilder.Build(new Core.Sim.HeadlessWorldOptions
            {
                DataSources = new IDataSource[] { frameworkSource, sampleSource },
                Seed = seed,
                FileSystem = fs,
                MapId = MapId,
                PlayerId = PlayerId,
                PlayerFactionId = FactionPlayer,
                PlayerClassId = ClassSample,
                GameId = GameId,
                StepSeconds = StepSeconds,
                FailOnUnknownTable = false,
            });
        }

        /// <summary>T-N6-2b：构造一个基于嵌入式最小仿真数据集（<c>core/sim/tests/data</c>，随
        /// <c>data/_framework</c> 一起加载，惯例同 <see cref="BuildWorld"/> 的 framework+sample 两根）
        /// 的无头世界。数据根镜像 <c>data/_sample</c> 的目录/文件命名（<c>&lt;域&gt;/&lt;表名&gt;.json</c>），
        /// 内容与 <c>data/_sample</c> 完全独立（各自的 <c>world.map</c>/<c>arch.class</c> 等 id 不通用，
        /// 见 <see cref="EmbeddedMapId"/> 等常量），玩家职业固定为 <see cref="EmbeddedClassId"/>
        /// （<c>arch.class.sim_warrior</c>）。</summary>
        public static Core.Sim.HeadlessWorld BuildFromEmbeddedDataset(ulong seed, int playerLevel = 1)
        {
            var fs = new StubFileSystem();
            var repoRoot = FindRepoRoot();
            var frameworkSource = BuildDiskSource(fs, repoRoot, "data/_framework");
            var embeddedSource = BuildDiskSource(fs, repoRoot, "core/sim/tests/data");

            return Core.Sim.HeadlessWorldBuilder.Build(new Core.Sim.HeadlessWorldOptions
            {
                DataSources = new IDataSource[] { frameworkSource, embeddedSource },
                Seed = seed,
                FileSystem = fs,
                MapId = EmbeddedMapId,
                PlayerId = PlayerId,
                PlayerFactionId = FactionPlayer,
                PlayerClassId = EmbeddedClassId,
                PlayerLevel = playerLevel,
                GameId = EmbeddedGameId,
                StepSeconds = StepSeconds,
                FailOnUnknownTable = false,
            });
        }

        /// <summary>T-N6-2b：嵌入式数据集专用的固定脚本——不依赖 <c>spawn.table</c>/<c>encounter</c>
        /// （装配根本就不需要它们，见 <c>core/sim/tests/data/README.md</c> 判断记录"不含 spawn/encounter"）：
        /// 直接用 <see cref="Core.Carriers.Creature.CreatureFactory.Spawn"/> 生成一只
        /// <paramref name="creatureId"/>（默认 <see cref="EmbeddedCreatureWolfL1"/>，1 级普通怪），
        /// 玩家按 <see cref="EmbeddedSkillBookId"/> 在 <paramref name="playerLevel"/> 级
        /// <c>LearnFromBook</c>，随后反复施放学到的第一个主动技能直至一方死亡或达到
        /// <paramref name="maxAttempts"/> 次。</summary>
        public static EmbeddedFightScriptResult RunEmbeddedFightScript(
            ulong seed, Id? creatureId = null, int playerLevel = 1, int maxAttempts = 200)
        {
            var world = BuildFromEmbeddedDataset(seed, playerLevel);

            world.Gameplay.Carriers.Rules.Skill.LearnFromBook(PlayerId, EmbeddedSkillBookId, playerLevel);
            var knownSkills = world.Gameplay.Carriers.Rules.Skill.GetKnownSkills(PlayerId);
            if (knownSkills.Count == 0)
            {
                throw new InvalidOperationException(
                    $"LearnFromBook({EmbeddedSkillBookId}, level={playerLevel}) 之后玩家未学到任何技能");
            }
            var attackSkillId = new Id("skill.sim_warrior_strike");

            var actualCreatureId = creatureId ?? EmbeddedCreatureWolfL1;
            var beastId = world.Gameplay.Carriers.Creatures.Spawn(actualCreatureId, EmbeddedMapId, new Vec2(3, 0), facing: 0);
            var beastPos = world.Gameplay.Carriers.Units.GetPosition(beastId);
            world.Spatial.Register(beastId, beastPos, 0.5);

            var snapshots = new List<string>();
            var tick = 0;
            var died = false;
            for (var i = 0; i < maxAttempts; i++)
            {
                if (!(world.Gameplay.Carriers.Units.Exists(beastId) && world.Gameplay.Carriers.Units.IsAlive(beastId)))
                {
                    died = true;
                    break;
                }

                var eventsBefore = world.Events.Count;
                SubmitCast(world, attackSkillId);
                world.Clock.Advance(StepSeconds);
                tick++;
                snapshots.Add(BuildTickSnapshot(world, tick, eventsBefore, beastId));
            }

            if (!died)
            {
                died = !(world.Gameplay.Carriers.Units.Exists(beastId) && world.Gameplay.Carriers.Units.IsAlive(beastId));
            }

            var playerAlive = world.Gameplay.Carriers.Units.Exists(PlayerId) && world.Gameplay.Carriers.Units.IsAlive(PlayerId);
            return new EmbeddedFightScriptResult(died, playerAlive, tick, knownSkills.Count, snapshots);
        }

        /// <summary>一次固定脚本的完整记录：进图 → 生成 sample_beast_field 的野兽 → 反复对其施放
        /// <see cref="SkillStrike"/> 直至死亡或达到 <paramref name="maxAttempts"/> 次，逐 tick 记录
        /// "本 tick 事件流（事件键 + 载荷序列化文本）+ 玩家/目标关键状态（等级、资源当前值、位置）"
        /// 的快照文本。</summary>
        public static FightScriptResult RunFightScript(ulong seed, int maxAttempts = 80)
        {
            var world = BuildWorld(seed);
            world.Gameplay.EnterMap(MapId, PlayerId);

            var spawnRecord = world.Gameplay.Spawn.GetSpawnRecord(SpawnBeastField);
            if (spawnRecord == null || !spawnRecord.EntityId.HasValue)
            {
                throw new InvalidOperationException("spawn.sample_beast_field 在 EnterMap 后应已生成一个实体");
            }
            var beastId = spawnRecord.EntityId.Value;

            // 判断记录（同 EndToEndTests.EnterAndAcceptQuest）：CreatureFactory.Spawn 不会自动把新生成
            // 的实体同步进本测试自己的 StubSpatialQuery，手动登记一次，保证目标链/命中判定能找到它。
            var beastPos = world.Gameplay.Carriers.Units.GetPosition(beastId);
            world.Spatial.Register(beastId, beastPos, 0.5);

            var snapshots = new List<string>();
            var tick = 0;
            bool died = false;

            for (var i = 0; i < maxAttempts; i++)
            {
                if (!(world.Gameplay.Carriers.Units.Exists(beastId) && world.Gameplay.Carriers.Units.IsAlive(beastId)))
                {
                    died = true;
                    break;
                }

                var eventsBefore = world.Events.Count;
                SubmitCast(world, SkillStrike);
                world.Clock.Advance(StepSeconds);
                tick++;

                snapshots.Add(BuildTickSnapshot(world, tick, eventsBefore, beastId));
            }

            if (!died)
            {
                died = !(world.Gameplay.Carriers.Units.Exists(beastId) && world.Gameplay.Carriers.Units.IsAlive(beastId));
            }

            return new FightScriptResult(snapshots, died, tick);
        }

        private static void SubmitCast(Core.Sim.HeadlessWorld world, Id skillId)
        {
            var args = new JsonObjectBuilder().Add("skill_id", new JsonString(skillId.Value)).Build();
            world.World.SubmitIntent(new Intent(PlayerId, "cast", args));
        }

        private static string BuildTickSnapshot(Core.Sim.HeadlessWorld world, int tick, int eventsBefore, Id beastId)
        {
            var sb = new StringBuilder();
            sb.Append("tick=").Append(tick.ToString(CultureInfo.InvariantCulture));

            sb.Append("|events=");
            for (var i = eventsBefore; i < world.Events.Count; i++)
            {
                sb.Append('[').Append(SerializeEvent(world.Events[i])).Append(']');
            }

            var playerLevel = world.Gameplay.Carriers.Rules.Progression.GetLevel(PlayerId);
            world.Gameplay.Carriers.Rules.Powers.TryGetPower(PlayerId, WellKnownPowers.Health, out var playerHealth);
            var playerPos = world.Gameplay.Carriers.Units.GetPosition(PlayerId);
            sb.Append("|player.level=").Append(playerLevel.ToString(CultureInfo.InvariantCulture));
            sb.Append(";player.health=").Append(playerHealth.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(";player.pos=").Append(FormatVec2(playerPos));

            var beastAlive = world.Gameplay.Carriers.Units.Exists(beastId) && world.Gameplay.Carriers.Units.IsAlive(beastId);
            sb.Append("|beast.alive=").Append(beastAlive);
            if (beastAlive)
            {
                world.Gameplay.Carriers.Rules.Powers.TryGetPower(beastId, WellKnownPowers.Health, out var beastHealth);
                var beastPos = world.Gameplay.Carriers.Units.GetPosition(beastId);
                sb.Append(";beast.health=").Append(beastHealth.ToString("R", CultureInfo.InvariantCulture));
                sb.Append(";beast.pos=").Append(FormatVec2(beastPos));
            }

            return sb.ToString();
        }

        private static string FormatVec2(Vec2 v) =>
            v.X.ToString("R", CultureInfo.InvariantCulture) + "," + v.Y.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// 判断记录：事件契约（<see cref="IEvent"/>）只保证 <see cref="IEvent.Key"/>，具体载荷字段由
        /// 各业务模块自行定义（<see cref="IExprReadableEvent"/> 只按需暴露给条件语言用的一部分字段，
        /// 并非所有事件都实现它）——本方法用公开属性反射通用序列化任意具体事件类型的全部公开只读属性，
        /// 不为每个事件类型手写一份字段清单，"事件键 + 载荷序列化文本"的载荷部分即这些属性按名排序后
        /// 拼接的文本，足以支撑逐 tick 完全一致比对（两次独立装配的结果只要属性值相同即文本相同）。
        /// </summary>
        private static string SerializeEvent(IEvent e)
        {
            var sb = new StringBuilder();
            sb.Append(e.Key.Value).Append('(');

            var props = e.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name != nameof(IEvent.Key) && p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList();

            for (var i = 0; i < props.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(props[i].Name).Append('=').Append(FormatValue(props[i].GetValue(e)));
            }

            sb.Append(')');
            return sb.ToString();
        }

        private static string FormatValue(object? value)
        {
            switch (value)
            {
                case null:
                    return "null";
                case string s:
                    return s;
                case System.Collections.IEnumerable enumerable when value is not string:
                    var items = enumerable.Cast<object?>().Select(FormatValue);
                    return "[" + string.Join(",", items) + "]";
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return value.ToString() ?? "null";
            }
        }
    }

    internal sealed class FightScriptResult
    {
        public IReadOnlyList<string> TickSnapshots { get; }

        public bool TargetDied { get; }

        public int TicksUsed { get; }

        public FightScriptResult(IReadOnlyList<string> tickSnapshots, bool targetDied, int ticksUsed)
        {
            TickSnapshots = tickSnapshots;
            TargetDied = targetDied;
            TicksUsed = ticksUsed;
        }
    }

    /// <summary>T-N6-2b：<see cref="SimTestWorldFactory.RunEmbeddedFightScript"/> 的结果——比
    /// <see cref="FightScriptResult"/> 更精简（不逐 tick 记录快照，本脚本不用于确定性比对，只用于
    /// "装配→学技能→打死一只怪"这条端到端链路的功能性证明）。</summary>
    internal sealed class EmbeddedFightScriptResult
    {
        /// <summary>目标（怪物）是否死亡。</summary>
        public bool TargetDied { get; }

        /// <summary>玩家在脚本结束时是否仍存活（"玩家获胜"= <see cref="TargetDied"/> 且本字段为
        /// <c>true</c>）。</summary>
        public bool PlayerAlive { get; }

        /// <summary>实际消耗的 tick 数。</summary>
        public int TicksUsed { get; }

        /// <summary><c>LearnFromBook</c> 之后玩家学到的技能数量。</summary>
        public int KnownSkillCount { get; }

        /// <summary>逐 tick 快照（惯例同 <see cref="FightScriptResult.TickSnapshots"/>），供确定性
        /// 比对（同种子两次独立装配逐 tick 完全一致）使用。</summary>
        public IReadOnlyList<string> TickSnapshots { get; }

        public EmbeddedFightScriptResult(
            bool targetDied, bool playerAlive, int ticksUsed, int knownSkillCount, IReadOnlyList<string> tickSnapshots)
        {
            TargetDied = targetDied;
            PlayerAlive = playerAlive;
            TicksUsed = ticksUsed;
            KnownSkillCount = knownSkillCount;
            TickSnapshots = tickSnapshots;
        }
    }
}
