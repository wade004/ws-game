using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Core.Foundation.DataRegistry;
using Lab;

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

        /// <summary>夹具目录里全部脚本（旧标准脚本 + 手感场景脚本）。</summary>
        public static List<InputScript> AllScripts() => LabFixtures.LoadScripts(FixturesDir);

        /// <summary>旧标准脚本（十个，不开手感装配）：既有基线与既有测试的口径。</summary>
        public static List<InputScript> StandardScripts() => AllScripts().FindAll(s => !s.Meta.Feel);

        /// <summary>手感场景脚本（<c>meta.feel</c> 为真，格式版本 3）。</summary>
        public static List<InputScript> FeelScripts() => AllScripts().FindAll(s => s.Meta.Feel);

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
