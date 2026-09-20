using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Combat;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// ADR-0049：<c>combat.resist_curve</c> 饱和分支新增截距字段 <c>k0</c>（<c>denom = value +
    /// k × attackerLevel + k0</c>）。<c>k0</c> 缺省 0 时必须与改动前的公式（<c>denom = value +
    /// k × attackerLevel</c>）逐位一致——这条不靠推理断言，直接对仓库随附的全部
    /// <c>combat.resist_curve</c> 数据集（默认示例数据根、<c>&lt;game&gt;</c> 模板数据根、内置
    /// 数值仿真测试数据根）里 <c>kind=saturation</c> 的每一条既有记录构造对比测试。
    /// <para>
    /// 判断记录：没有用 <c>[Theory]</c>/<c>[MemberData]</c> 把 <see cref="ResistCurve"/> 实例当
    /// 参数传递——仓库既有 <c>MemberData</c> 用例（<c>RecordExprSchemaExprFieldExclusionTests</c>）
    /// 只传值类型/字符串，没有传自定义引用类型的先例，为避免测试框架对复杂对象参数的序列化/显示
    /// 名生成行为不确定，改用单个 <see cref="Fact"/> 内部循环 + 收集全部不一致项一次性断言的写法，
    /// 失败时能在一条断言消息里看到具体是哪个数据集文件、哪个曲线 id、哪组 (value, attackerLevel)
    /// 不一致，不比 <c>Theory</c> 逐用例失败的可读性差。
    /// </para>
    /// </summary>
    public sealed class ResistCurveInterceptBackCompatTests
    {
        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            // 本文件固定位于 <repoRoot>/core/rules/combat/tests/ResistCurveInterceptBackCompatTests.cs，
            // 向上 4 级（tests → combat → rules → core）即仓库根（同 core/numbers/tests/L1SampleDataTests.cs
            // FindRepoRoot 的既有手法，层数按本文件实际路径深度调整）。
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (int i = 0; i < 4; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException($"源文件路径层级不足，无法定位仓库根目录：{sourceFilePath}");
            }
            return dir.FullName;
        }

        /// <summary>随仓库提交的 <c>combat.resist_curve</c> 数据集——默认示例数据根、
        /// <c>&lt;game&gt;</c> 模板数据根、内置数值仿真测试数据根三处（不含
        /// <c>architecture/落地计划</c> 下的历史审计归档快照，那些是一次性证据、不是随版本演进的
        /// 数据集）。</summary>
        private static readonly string[] DatasetRelativePaths =
        {
            "data/_sample/combat/combat.resist_curve.json",
            "games/_template/data/game/combat/combat.resist_curve.json",
            "core/sim/tests/data/combat/combat.resist_curve.json",
        };

        /// <summary>改动前的公式（逐字复刻 <c>ResistCurve.ComputeReduction</c> 饱和分支迁移前的实现），
        /// 作为比对基准。</summary>
        private static double LegacyComputeReduction(double k, double maxReduction, double value, int attackerLevel)
        {
            var denom = value + k * attackerLevel;
            var raw = denom <= 0.0 ? 0.0 : value / denom;
            if (raw < 0.0) raw = 0.0;
            return Math.Min(raw, maxReduction);
        }

        private sealed class ExistingCurveCase
        {
            public string SourceFile = "";
            public string CurveId = "";
            public ResistCurve Curve = null!;
            public double K;
            public double MaxReduction;
        }

        private static List<ExistingCurveCase> LoadExistingSaturationCurves()
        {
            var result = new List<ExistingCurveCase>();
            var repoRoot = FindRepoRoot();
            foreach (var relPath in DatasetRelativePaths)
            {
                var fullPath = Path.Combine(repoRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    throw new InvalidOperationException($"数据集文件不存在：{fullPath}");
                }

                var raw = (JsonObject)JsonReader.Parse(File.ReadAllText(fullPath));
                var rows = (JsonArray)raw["rows"];
                foreach (var rowVal in rows)
                {
                    var row = (JsonObject)rowVal;
                    if (!row.TryGetValue("kind", out var kindVal) || !(kindVal is JsonString kindStr) || kindStr.Value != "saturation")
                    {
                        continue;
                    }

                    var key = ((JsonString)row["id"]).Value;
                    var curve = new ResistCurve(new DataRecord(CombatSchemas.ResistCurve, key, new Id(key), row));
                    var k = ((JsonNumber)row["k"]).Value;
                    var maxReduction = row.TryGetValue("max_reduction", out var mr) && mr is JsonNumber mrn ? mrn.Value : 0.75;

                    result.Add(new ExistingCurveCase { SourceFile = relPath, CurveId = key, Curve = curve, K = k, MaxReduction = maxReduction });
                }
            }
            return result;
        }

        [Fact]
        public void ExistingSaturationCurves_DefaultK0_MatchLegacyFormulaBitForBit_AcrossAllRepositoryDatasets()
        {
            // 覆盖点：等级为 0（背景第 2 点描述的退化区间）、常规等级、极端高护甲值/极小护甲值，
            // 足以暴露"分母加 0"是否真的在所有分支都保持逐位恒等。
            var levelsAndValues = new (double Value, int Level)[]
            {
                (0, 0), (0, 1), (0, 60), (1, 0), (1, 1), (100, 1), (100, 60), (5000, 60), (1_000_000, 60),
            };

            var allCurves = LoadExistingSaturationCurves();
            Assert.NotEmpty(allCurves);

            // 判断记录：data/_sample 里 combat.resist.physical_with_floor 是本次改动为演示 k0
            // 新能力新增的探针样例（显式登记 k0=200），不是"改动前就存在、未登记 k0"的既有记录——
            // 它天然不满足"缺省 0"这个前提，不应该被计入逐位兼容比对的分母，否则这条测试会把
            // "新增样例正确地使用了新字段"误判成"兼容性破坏了"。该样例自身的正确性由下面的
            // NewSampleCurve_WithK0_FixesLowLevelDegeneration_ComparedToK0Zero 测试单独覆盖。
            var existingCurves = allCurves.Where(c => c.Curve.K0 == 0.0).ToList();
            Assert.NotEmpty(existingCurves);
            Assert.True(existingCurves.Count == allCurves.Count - 1,
                $"预期恰好一条样例登记了非零 k0（新增探针），实际既有（k0=0）记录 {existingCurves.Count}/{allCurves.Count}");

            var mismatches = new List<string>();
            var coveredInstanceCount = 0;

            foreach (var testCase in existingCurves)
            {
                coveredInstanceCount++;
                foreach (var (value, level) in levelsAndValues)
                {
                    var expected = LegacyComputeReduction(testCase.K, testCase.MaxReduction, value, level);
                    var actual = testCase.Curve.ComputeReduction(value, level);
                    if (expected != actual)
                    {
                        mismatches.Add($"{testCase.SourceFile} 的 {testCase.CurveId}，value={value}，attackerLevel={level}：" +
                            $"旧公式={expected}，新公式（k0 缺省）={actual}");
                    }
                }
            }

            Assert.True(mismatches.Count == 0, "新旧公式不逐位一致：\n" + string.Join("\n", mismatches));

            // 反向护栏：确认上面的逐位兼容比对不是"空对空"——数据集三处文件里 kind=saturation 的
            // 既有记录数量各自至少一条，覆盖到了三处数据根，而不是全部落在同一个文件里。
            var coveredSourceFiles = existingCurves.Select(c => c.SourceFile).Distinct().ToList();
            foreach (var relPath in DatasetRelativePaths)
            {
                Assert.Contains(relPath, coveredSourceFiles);
            }

            // 记录覆盖到的既有实例总数，供汇报引用（xunit 断言失败信息会带上这条，方便核对）。
            Assert.True(coveredInstanceCount == existingCurves.Count,
                $"覆盖既有 saturation 实例 {coveredInstanceCount}/{existingCurves.Count} 个");
        }

        /// <summary>新能力可用性证明（不只是"缺省不变"）：<c>data/_sample</c> 新增的
        /// <c>combat.resist.physical_with_floor</c>（<c>k=20, k0=200</c>）在攻击者等级很低时，
        /// 相比同系数但 <c>k0=0</c> 的旧公式，减免明显更低——这正是背景里"低等级段退化"问题被
        /// 修正的直接证据：旧公式在 attackerLevel=0 时任意正护甲值都换算出 100%（夹到
        /// max_reduction 前），新字段让这一段曲线不再退化。</summary>
        [Fact]
        public void NewSampleCurve_WithK0_FixesLowLevelDegeneration_ComparedToK0Zero()
        {
            var repoRoot = FindRepoRoot();
            var samplePath = Path.Combine(repoRoot, "data", "_sample", "combat", "combat.resist_curve.json");
            var raw = (JsonObject)JsonReader.Parse(File.ReadAllText(samplePath));
            var rows = (JsonArray)raw["rows"];
            var probeRow = rows.Cast<JsonObject>().Single(r => ((JsonString)r["id"]).Value == "combat.resist.physical_with_floor");

            var curveWithFloor = new ResistCurve(new DataRecord(CombatSchemas.ResistCurve, "combat.resist.physical_with_floor",
                new Id("combat.resist.physical_with_floor"), probeRow));

            Assert.Equal(200.0, curveWithFloor.K0);

            const double value = 50.0;
            const int attackerLevel = 0; // 背景第 2 点描述的退化区间：等级 0 内容。

            var legacyWithK0Zero = LegacyComputeReduction(curveWithFloor.K, curveWithFloor.MaxReduction, value, attackerLevel);
            var actualWithK0 = curveWithFloor.ComputeReduction(value, attackerLevel);

            // 旧公式（等价于 k0=0）在 attackerLevel=0 时退化为 value/value=1，夹到 max_reduction=0.75。
            Assert.Equal(0.75, legacyWithK0Zero);
            // 新公式：50 / (50 + 20*0 + 200) = 50/250 = 0.2，远低于封顶值——曲线在低等级段恢复了
            // 实际的调参空间，不再是"任意正护甲值都封顶"。
            Assert.Equal(0.2, actualWithK0);
            Assert.True(actualWithK0 < legacyWithK0Zero);
        }
    }
}
