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
}
