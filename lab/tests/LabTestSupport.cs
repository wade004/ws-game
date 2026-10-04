using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Core.Foundation.DataRegistry;
using Lab;
using Xunit;

namespace Tests.Lab
{
    /// <summary>测试共用：定位仓库根、装载 <c>data/_framework</c> + <c>data/_lab</c>、读标准脚本夹具。</summary>
    internal static class LabTestSupport
    {
        public static readonly string[] AllCells =
        {
            "2d_targeted", "2d_action", "2_5d_targeted", "2_5d_action", "3d_targeted", "3d_action",
        };

        public static readonly string[] PlanarTargetedCells = { "2d_targeted", "2_5d_targeted", "3d_targeted" };

        private static readonly Lazy<LabRunner> SharedRunner = new Lazy<LabRunner>(() => CreateRunner(null));

        public static string RepoRoot([CallerFilePath] string sourceFilePath = "")
        {
            // 本源文件固定位于 <repoRoot>/lab/tests/LabTestSupport.cs，向上 2 级（tests → lab）即仓库根。
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (var i = 0; i < 2; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException($"源文件路径层级不足：{sourceFilePath}");
            }

            return dir.FullName;
        }

        public static string FixturesDir => Path.Combine(RepoRoot(), "lab", "fixtures");

        /// <summary>共享（只读）运行器：数据集装载一次，各用例复用。</summary>
        public static LabRunner Runner => SharedRunner.Value;

        /// <summary>
        /// 新建运行器；<paramref name="rewrite"/> 非空时对数据表文本做内存改写（不动磁盘），
        /// 参数为（表名，原文本），返回非空即替换。
        /// </summary>
        public static LabRunner CreateRunner(Func<string, string, string?>? rewrite)
        {
            var root = RepoRoot();
            var sources = new List<IDataSource>
            {
                LabDataSources.FromDirectory(Path.Combine(root, "data", "_framework")),
                LabDataSources.FromDirectory(Path.Combine(root, "data", "_lab")),
            };
            if (rewrite != null)
            {
                for (var i = 0; i < sources.Count; i++)
                {
                    sources[i] = new TableOverlayDataSource(sources[i], rewrite);
                }
            }

            return new LabRunner(LabDataset.Load(sources), null, rel => LabDataSources.FromDirectory(Path.Combine(root, rel)));
        }

        /// <summary>
        /// 对夹具基线做确定性比较：逻辑类与表现类逐项判定，实时类（帧耗时、每帧分配，依赖真实时钟）排除在外——
        /// 满载机器上的墙钟抖动不应让"基线是否一致"这类用例变红；实时上限由 <c>suite</c> 命令行与
        /// <c>LabKernelTests.Comparer_RealTimeMetrics_AreCeilingChecksNotByteComparisons</c> 单独把关。
        /// </summary>
        public static List<CellResult> CheckDeterministic(LabRunner runner, string fixturesDir, string? onlyScript = null, string? onlyCell = null) =>
            LabSuite.Check(runner, fixturesDir, onlyScript, onlyCell, includeRealTime: false);

        /// <summary>未通过格子的人读报告：状态、消息、基线差异明细与没过的期望；全部通过时为空串。</summary>
        public static string DescribeFailures(IEnumerable<CellResult> results)
        {
            var sb = new StringBuilder();
            foreach (var r in results.Where(x => x.Status != CellStatus.Pass))
            {
                sb.Append(r.Script).Append(" @ ").Append(r.Cell).Append(' ').Append(r.Status);
                if (r.Message.Length > 0)
                {
                    sb.Append("：").Append(r.Message);
                }

                sb.Append("\n");
                if (r.Diff != null)
                {
                    sb.Append(r.Diff.Format());
                }

                foreach (var e in r.Expectations.Where(x => !x.Ok))
                {
                    sb.Append("  期望未过 ").Append(e).Append("\n");
                }
            }

            return sb.ToString();
        }

        /// <summary>断言全部格子通过；失败时消息带每个未通过格子的差异明细（不只是状态）。</summary>
        public static void AssertAllPass(IEnumerable<CellResult> results, string what)
        {
            var failures = DescribeFailures(results);
            Assert.True(failures.Length == 0, what + "：\n" + failures);
        }

        /// <summary>夹具目录里全部脚本（旧标准脚本 + 手感场景脚本）。</summary>
        public static List<InputScript> AllScripts() => LabFixtures.LoadScripts(FixturesDir);

        /// <summary>旧标准脚本（十个，不开手感装配）：既有基线与既有测试的口径。</summary>
        public static List<InputScript> StandardScripts() => AllScripts().FindAll(s => !s.Meta.Feel);

        /// <summary>空间语义脚本的 id 前缀（手感设计/06 第 1.2 节）：这些脚本除六个平面格子外还适用四个空间格子，由 <c>SpaceSemanticsTests</c> 专门验收。</summary>
        public const string SpaceScriptPrefix = "space.";

        /// <summary>手感场景脚本（<c>meta.feel</c> 为真，格式版本 3），不含空间语义脚本（它们的格子集合是十个，见 <see cref="SpaceScripts"/>）。</summary>
        public static List<InputScript> FeelScripts() =>
            AllScripts().FindAll(s => s.Meta.Feel && !s.Meta.ScriptId.StartsWith(SpaceScriptPrefix, StringComparison.Ordinal));

        /// <summary>空间语义脚本（<c>space.*</c>，手感场景脚本的一支，适用六个平面格子 + 四个空间格子）。</summary>
        public static List<InputScript> SpaceScripts() =>
            AllScripts().FindAll(s => s.Meta.ScriptId.StartsWith(SpaceScriptPrefix, StringComparison.Ordinal));

        /// <summary>按 JSON 往返复制一份脚本（保留全部元信息），再改帧率上限。</summary>
        public static InputScript CloneWithFrameRate(InputScript script, int frameRateCap)
        {
            var clone = InputScript.Parse(script.ToJson());
            clone.Meta.FrameRateCap = frameRateCap;
            return clone;
        }

        public static InputScript Script(string id)
        {
            foreach (var s in AllScripts())
            {
                if (s.Meta.ScriptId == id)
                {
                    return s;
                }
            }

            throw new InvalidOperationException($"没有标准脚本 {id}");
        }

        /// <summary>构造一份内存脚本（同标准脚本的字段约定）。</summary>
        public static InputScript Build(string id, int durationTicks, IEnumerable<ScriptEvent> events, params string[] groups)
        {
            var meta = new ScriptMeta { ScriptId = id, DurationTicks = durationTicks };
            meta.DummyGroups.AddRange(groups);
            return new InputScript(meta, new List<ScriptEvent>(events));
        }

        public static InputScript WithFrameRate(InputScript script, int frameRateCap)
        {
            var meta = new ScriptMeta
            {
                ScriptId = script.Meta.ScriptId,
                ScriptVersion = script.Meta.ScriptVersion,
                Description = script.Meta.Description,
                DatasetRoot = script.Meta.DatasetRoot,
                PresetId = script.Meta.PresetId,
                CalibrationVersion = script.Meta.CalibrationVersion,
                PoseSet = script.Meta.PoseSet,
                TickRate = script.Meta.TickRate,
                FrameRateCap = frameRateCap,
                DurationTicks = script.Meta.DurationTicks,
                PlayerStart = script.Meta.PlayerStart,
            };
            meta.DummyGroups.AddRange(script.Meta.DummyGroups);
            return new InputScript(meta, script.Events);
        }
    }
}
