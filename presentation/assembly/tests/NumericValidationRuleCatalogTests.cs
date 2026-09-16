using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Presentation.Assembly;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 分阶段落地计划 T-N5-3 验收：<see cref="NumericValidationRuleCatalog"/>（04 第 5 节数值类校验项
    /// 分级表 18 条的集中只读登记清单）与实际装配注册的规则集合一致——"漏注册即失败"的核心断言，见
    /// <see cref="Catalog_EveryDistinctRuleId_IsActuallyRegistered_AgainstSampleData"/>：对
    /// <c>data/_framework</c> + <c>data/_sample</c> 跑一遍真实的
    /// <see cref="ContentValidationAssembly.Run"/>（与 <c>toolchain/validator --json</c> 走的是同一条
    /// 装配路径，见 <see cref="ContentValidationAssembly"/> 类型注释"两个消费方用同一份装配代码"），
    /// 从 <see cref="ValidationReport.Rules"/> 反查——本清单登记了但装配根忘记
    /// <c>RegisterValidationRule</c> 的规则，在这个反查里找不到对应 <c>RuleId</c>，测试失败；同时
    /// 顺带验证验收标准 1"对 <c>data/_sample</c> 零阻断"。
    /// </summary>
    public class NumericValidationRuleCatalogTests
    {
        // -----------------------------------------------------------------
        // 只读磁盘 IFileSystem：加载仓库真实 data/_framework、data/_sample 用（惯例同
        // toolchain/validator/DiskFileSystem.cs——adapters/stub 的 StubFileSystem 是纯内存实现，
        // 接触不到真实磁盘，本文件不能引用 toolchain 项目（presentation 层不依赖工具链），照抄一份
        // 最小只读实现）。
        // -----------------------------------------------------------------
        private sealed class ReadOnlyDiskFileSystem : IFileSystem
        {
            public string GetUserDataDir() => Directory.GetCurrentDirectory();

            public string GetContentRootDir() => Directory.GetCurrentDirectory();

            public string? ReadText(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

            public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

            public IReadOnlyList<string> ListFiles(string dirPath)
            {
                if (!Directory.Exists(dirPath))
                {
                    return Array.Empty<string>();
                }

                var normalizedDir = dirPath.Replace('\\', '/').TrimEnd('/');
                var prefixLength = normalizedDir.Length + 1;
                var results = new List<string>();
                foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
                {
                    results.Add(file.Replace('\\', '/').Substring(prefixLength));
                }

                results.Sort(StringComparer.Ordinal);
                return results;
            }

            public bool WriteTextAtomic(string path, string content) =>
                throw new NotSupportedException("只读夹具：本测试不修改数据目录");

            public bool DeleteFile(string path) =>
                throw new NotSupportedException("只读夹具：本测试不修改数据目录");
        }

        /// <summary>同 <c>SchemaAuditTests.FindRepoRoot</c> 惯例：从本源文件路径向上找仓库根——
        /// <c>presentation/assembly/tests/</c> 向上三级是仓库根。</summary>
        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (var i = 0; i < 3; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException($"源文件路径层级不足，无法定位仓库根目录：{sourceFilePath}");
            }
            return dir.FullName;
        }

        private static IReadOnlyList<IDataSource> BuildSampleDataSources()
        {
            var repoRoot = FindRepoRoot();
            var fs = new ReadOnlyDiskFileSystem();
            return new IDataSource[]
            {
                new FileSystemDataSource(fs, Path.Combine(repoRoot, "data", "_framework").Replace('\\', '/')),
                new FileSystemDataSource(fs, Path.Combine(repoRoot, "data", "_sample").Replace('\\', '/')),
            };
        }

        /// <summary>见 <see cref="NumericValidationRuleCatalog"/> 类型级判断记录"数量口径"（T-N5-3
        /// 如实上报的核对表内部不一致）：04 分级表"阻断 11 + 警告 7 = 18"是概念条目数（把 B1 家族的
        /// 三个独立实现类算作一条），本清单按可独立验证注册的技术行数登记——阻断 13 行（B1/B1a/B1b 三个
        /// 不同 RuleId 各占一行 + B2～B11 各一行）、警告 7 行，合计 20 行，是 18 个概念条目的超集；
        /// 深度复审 E-M1（2026-09-16）后并入 <c>sim.anchor</c>/<c>sim.scenario</c> 三行（阻断 2 +
        /// 警告 1），总数改为 23 行（阻断 15 + 警告 8）；深度复审同批 E-S2 又给
        /// <c>sim_scenario_bandwidth_key_unknown</c> 新增一条警告，总数再改为 24 行（阻断 15 + 警告
        /// 9），见类型级判断记录"2026-09-16，深度复审 E-M1"/"深度复审 E-S2"。
        /// </summary>
        [Fact]
        public void Catalog_HasFifteenBlockingRowsAndNineWarningRows_TwentyFourTotal()
        {
            Assert.Equal(24, NumericValidationRuleCatalog.Entries.Count);
            Assert.Equal(15, NumericValidationRuleCatalog.Entries.Count(e => e.Severity == ValidationSeverity.Error));
            Assert.Equal(9, NumericValidationRuleCatalog.Entries.Count(e => e.Severity == ValidationSeverity.Warning));
        }

        /// <summary>
        /// 交叉核对（深度复审 E-M1"测试覆盖缺口 4"补齐）：04 文档第 5 节分级表引言句自己写明的总数
        /// （"落地为 24 条独立规则（阻断 15 + 警告 9）"）必须与本清单实际登记的总数一致——04 文档改了
        /// 总数（比如又并入新一批检查）而本清单没跟着改，或反过来，本测试立刻失败，不会像 E-M1 那样
        /// 悄悄漂移数年无人发现。
        /// <para>
        /// 判断记录（为何解析引言句而不是直接数分级表 markdown 表格的行数）：04 第 5 节分级表本身按
        /// "概念条目"记行（"曲线单调有限"一个概念条目在表里只占一行，但在本清单里因为
        /// <see cref="ProgLevelCurveValidationRule"/>/<see cref="CombatResistCurveValidationRule"/>
        /// 两个密集枚举分支各自需要独立 <c>RuleId</c>+<c>CheckName</c> 而拆成三条技术行，见类型级
        /// 判断记录"数量口径"），逐行数表格行数会得到 22（18 个原概念行 + N6 新增 3 行 + E-S2 新增
        /// 1 行），比本清单的 24 少 2——这个"曲线单调有限 1 概念行 vs 3 技术行"的差额是 T-N5-3 就如实
        /// 记录、E-M1/E-S2 范围外的既有口径差，不是本次交叉核对要抓的问题；04 文档引言句自己已经把
        /// "落地为 24 条独立规则"这个技术行总数显式写出来（与本清单的记账口径一致），解析这句话比数
        /// 表格行数更贴合本清单要害的一致性问题（"引言句总数"与"本清单总数"是否同步），因此本测试
        /// 解析引言句而不是表格行数。
        /// </para>
        /// <para>
        /// 判断记录（为何取最后一个匹配，不是第一个）：04 文档顶部"变更记录"表按时间顺序累积历次
        /// 勘误说明，历史行里会原样引用当时的旧总数原文（如"本表落地为 20 条独立规则（阻断 13 +
        /// 警告 7）"这句话逐字出现在 N5 落地那一行变更记录里），与本节正文当前生效的那一句共享完全
        /// 相同的措辞形状，<c>Regex.Match</c> 取第一个匹配会误命中历史变更记录里的旧数字而不是当前
        /// 生效值；改用锚定第 5 节该段落固定前缀"按级别分两组"（04 文档内唯一出现，只在本节正文，不
        /// 在变更记录表里）后取紧跟着的第一个"落地为…"匹配，确保定位到当前生效的那一句，而不是简单
        /// 取最后一个匹配（正文本身在变更记录表之后，取最后一个凑巧也能命中，但锚定语义更明确、不
        /// 依赖"正文总在变更记录表之后"这一物理布局假设）。
        /// </para>
        /// </summary>
        [Fact]
        public void Catalog_TotalCount_MatchesArchitecture04SectionFiveIntroSentence()
        {
            var repoRoot = FindRepoRoot();
            var docPath = Path.Combine(repoRoot, "architecture", "04_数据与内容管线.md");
            var text = File.ReadAllText(docPath, System.Text.Encoding.UTF8);

            var match = System.Text.RegularExpressions.Regex.Match(
                text, @"按级别分两组[\s\S]*?落地为\s*(\d+)\s*条独立规则（阻断\s*(\d+)\s*\+\s*警告\s*(\d+)");
            Assert.True(match.Success,
                "未能在 architecture/04_数据与内容管线.md 第 5 节找到\"按级别分两组……落地为 N 条独立" +
                "规则（阻断 X + 警告 Y）\"这句引言——该句措辞若被改写，需要同步更新本测试的解析正则，" +
                "而不是让本测试静默失效。");

            var docTotal = int.Parse(match.Groups[1].Value);
            var docErrors = int.Parse(match.Groups[2].Value);
            var docWarnings = int.Parse(match.Groups[3].Value);

            Assert.Equal(docTotal, NumericValidationRuleCatalog.Entries.Count);
            Assert.Equal(docErrors, NumericValidationRuleCatalog.Entries.Count(e => e.Severity == ValidationSeverity.Error));
            Assert.Equal(docWarnings, NumericValidationRuleCatalog.Entries.Count(e => e.Severity == ValidationSeverity.Warning));
        }

        [Fact]
        public void Catalog_AllWarningEntries_AreNonEscalatable()
        {
            // 04 第 5 节"警告"整组登记为不可提升——见 IValidationRule.NonEscalatable 类型级判断记录
            // "抓意图不抓手滑"。注意：阻断级不能反过来断言"全部 NonEscalatable=false"——
            // ItemBudgetValidationRule/SkillBudgetValidationRule 两个类的 NonEscalatable 是"整个规则
            // 实例"的属性（不是逐检查名的），它们各自的阻断检查（item_budget_exceeded/
            // skill_budget_hard_cap_exceeded）与警告检查共享同一个类实例、同一个 NonEscalatable=true，
            // 只是 NonEscalatable 对 Error 严重级不产生实际效果（Error 恒阻断，见该属性类型级判断
            // 记录）——本清单如实登记这两行的 NonEscalatable 为 true，不能为了凑"阻断=false"的直觉
            // 而登记一个与真实规则实例不符的值。
            foreach (var entry in NumericValidationRuleCatalog.Entries.Where(e => e.Severity == ValidationSeverity.Warning))
            {
                Assert.True(entry.NonEscalatable, $"{entry.RuleId}/{entry.CheckName} 应为不可提升警告");
            }
        }

        [Fact]
        public void Catalog_CheckNames_AreAllDistinct()
        {
            // 检查名是报告 issues[].check 的实际取值，理应各不相同（即便同一 RuleId 产出多个检查名，
            // 如 StatDefinitionValidationRule 两条）。
            var checkNames = NumericValidationRuleCatalog.Entries.Select(e => e.CheckName).ToList();
            Assert.Equal(checkNames.Distinct().Count(), checkNames.Count);
        }

        /// <summary>
        /// 深度复审 E-M1："仿真"分组三条改用字符串字面量而非 <c>nameof(...)</c>/规则类常量引用
        /// （见 <see cref="NumericValidationRuleCatalog"/> 类型级判断记录"为何这三条不像其余 20 条
        /// 那样用 nameof(...) ……改用字符串字面量"——本文件所在 <c>Presentation.Common</c> 不能
        /// 引用 <c>Core.Sim</c>，否则会让 <c>build.ps1</c> 打包给 Unity 的六个核心 DLL 之一产生对
        /// <c>Core.Sim.dll</c> 的硬依赖）。字面量失去了编译期"规则改名、清单自动跟着变"的保障，本
        /// 测试用反射读取 <c>Core.Sim.SimAnchorValidationRule</c>/<c>SimScenarioValidationRule</c>
        /// 的真实 <c>RuleId</c>（<c>GetType().Name</c>）与检查名常量，逐一比对本清单三条字面量，
        /// 补回等价的"改一处、测试跟着炸"保障——本测试项目（<c>Tests.PresentationCommon.csproj</c>）
        /// 已经引用 <c>Core.Sim</c>（<c>ExtraSchemaRegistration</c> 接线需要），不新增依赖。
        /// </summary>
        [Fact]
        public void Catalog_SimGroupLiterals_MatchCoreSimRealConstants()
        {
            var anchorRuleId = nameof(Core.Sim.SimAnchorValidationRule);
            var scenarioRuleId = nameof(Core.Sim.SimScenarioValidationRule);

            var levelContinuity = NumericValidationRuleCatalog.Entries.Single(
                e => e.CheckName == Core.Sim.SimAnchorValidationRule.LevelContinuityCheck);
            Assert.Equal(anchorRuleId, levelContinuity.RuleId);
            Assert.Equal(ValidationSeverity.Error, levelContinuity.Severity);
            Assert.True(levelContinuity.NonEscalatable);
            Assert.Equal("仿真", levelContinuity.Group);
            Assert.False(levelContinuity.RequiresAnchor);

            var expectedItemLevelMonotonic = NumericValidationRuleCatalog.Entries.Single(
                e => e.CheckName == Core.Sim.SimAnchorValidationRule.ExpectedItemLevelMonotonicCheck);
            Assert.Equal(anchorRuleId, expectedItemLevelMonotonic.RuleId);
            Assert.Equal(ValidationSeverity.Warning, expectedItemLevelMonotonic.Severity);
            Assert.True(expectedItemLevelMonotonic.NonEscalatable);
            Assert.Equal("仿真", expectedItemLevelMonotonic.Group);
            Assert.False(expectedItemLevelMonotonic.RequiresAnchor);

            var scenarioLevelCoverage = NumericValidationRuleCatalog.Entries.Single(
                e => e.CheckName == Core.Sim.SimScenarioValidationRule.LevelCoverageCheck);
            Assert.Equal(scenarioRuleId, scenarioLevelCoverage.RuleId);
            Assert.Equal(ValidationSeverity.Error, scenarioLevelCoverage.Severity);
            Assert.True(scenarioLevelCoverage.NonEscalatable);
            Assert.Equal("仿真", scenarioLevelCoverage.Group);
            Assert.False(scenarioLevelCoverage.RequiresAnchor);

            // 深度复审 E-S2 新增（同一 SimScenarioValidationRule 类实例的第二条检查名）。
            var bandwidthKeyUnknown = NumericValidationRuleCatalog.Entries.Single(
                e => e.CheckName == Core.Sim.SimScenarioValidationRule.BandwidthKeyUnknownCheck);
            Assert.Equal(scenarioRuleId, bandwidthKeyUnknown.RuleId);
            Assert.Equal(ValidationSeverity.Warning, bandwidthKeyUnknown.Severity);
            Assert.True(bandwidthKeyUnknown.NonEscalatable);
            Assert.Equal("仿真", bandwidthKeyUnknown.Group);
            Assert.False(bandwidthKeyUnknown.RequiresAnchor);

            // 与真实规则实例的 NonEscalatable 比对（同 Catalog_EveryDistinctRuleId_IsActuallyRegistered_
            // AgainstSampleData 的比对口径，但直接构造实例而不必跑一遍完整装配）。
            Assert.Equal(new Core.Sim.SimAnchorValidationRule().NonEscalatable, levelContinuity.NonEscalatable);
            Assert.Equal(new Core.Sim.SimAnchorValidationRule().NonEscalatable, expectedItemLevelMonotonic.NonEscalatable);
            Assert.Equal(new Core.Sim.SimScenarioValidationRule().NonEscalatable, scenarioLevelCoverage.NonEscalatable);
            Assert.Equal(new Core.Sim.SimScenarioValidationRule().NonEscalatable, bandwidthKeyUnknown.NonEscalatable);
        }

        /// <summary>仅 04 第 5 节"技能预算硬上限""技能预算偏离""授予价值超特效占比"三条依赖阶段 N6
        /// 才接入的 <c>ISkillBudgetAnchorProvider</c>（见 <see cref="NumericValidationRuleCatalog"/>
        /// 类型判断记录）。</summary>
        [Fact]
        public void Catalog_ExactlyThreeEntries_RequireAnchor()
        {
            var anchorDependent = NumericValidationRuleCatalog.Entries.Where(e => e.RequiresAnchor).ToList();
            Assert.Equal(3, anchorDependent.Count);
            Assert.Equal(
                new[] { "skill_budget_hard_cap_exceeded", "skill_budget_deviation", "item_grant_value_exceeds_share" },
                anchorDependent.Select(e => e.CheckName).ToArray());
        }

        /// <summary>
        /// 核心验收：本清单登记的每一个不同 <see cref="NumericValidationRuleDescriptor.RuleId"/>，都必须
        /// 能在 <see cref="ContentValidationAssembly.Run"/>（真实装配 + 真实数据）产出的
        /// <see cref="ValidationReport.Rules"/> 里找到同名条目，且默认级别/是否不可提升与本清单一致——
        /// 这是"禁止漏注册"的可执行断言：如果有人从
        /// <c>Core.Rules.Assembly.RulesSchemaCatalog.RegisterAll</c>/
        /// <c>Core.Carriers.Assembly.CarriersSchemaCatalog.RegisterAll</c>/
        /// <c>Core.Gameplay.Assembly.GameplaySchemaCatalog.RegisterAll</c>/
        /// <c>Presentation.Assembly.PresentationSchemaCatalog.RegisterAll</c> 任意一处删掉了某条
        /// <c>RegisterValidationRule(new XxxRule())</c> 调用，该 RuleId 就不会出现在
        /// <c>report.Rules</c> 里，本测试立刻失败。顺带验证验收标准 1"对 data/_sample 零阻断"。
        /// <para>
        /// 判断记录（T-N6-2a：<c>ExtraSchemaRegistration</c> 接入 <c>Core.Sim.SimSchemaCatalog</c>）：
        /// <c>data/_sample</c> 新增 <c>sim/</c> 域（<c>sim.anchor</c>/<c>sim.scenario</c>，仅无头
        /// 仿真与内容工具读取，不进 <see cref="PresentationSchemaCatalog"/>，见
        /// <c>Core.Sim.SimSchemaCatalog</c> 类型判断记录"为何不并入 GameplaySchemaCatalog"）之后，
        /// 本测试若只调用默认 <see cref="ContentValidationAssembly.Run"/>（不接线
        /// <see cref="ContentValidationOptions.ExtraSchemaRegistration"/>）会因为
        /// <c>FailOnUnknownTable</c> 默认 <c>true</c> 而报 <c>sim.anchor</c>/<c>sim.scenario</c>
        /// "未通过 RegisterSchema 登记"的 envelope 错误——本测试的核心断言只关心
        /// <see cref="NumericValidationRuleCatalog"/> 与实际装配的规则集合是否一致，不关心 sim.* 是否
        /// 参与，接入该钩子（同 <c>toolchain/validator/Program.cs</c> 的接入方式）只是让"对
        /// <c>data/_sample</c> 全量零阻断"这条顺带断言继续成立，不改变本测试的核心验收逻辑。
        /// </para>
        /// </summary>
        [Fact]
        public void Catalog_EveryDistinctRuleId_IsActuallyRegistered_AgainstSampleData()
        {
            var sources = BuildSampleDataSources();
            var options = new ContentValidationOptions
            {
                ExtraSchemaRegistration = Core.Sim.SimSchemaCatalog.RegisterAll,
            };
            var run = ContentValidationAssembly.Run(sources, options);

            Assert.False(run.Report.IsBlocking,
                "data/_sample 应零阻断：\n" + string.Join("\n", run.Report.Issues.Where(i => i.Severity == ValidationSeverity.Error)));

            var registeredById = run.Report.Rules.ToDictionary(r => r.RuleId, r => r);

            var distinctByRuleId = NumericValidationRuleCatalog.Entries
                .GroupBy(e => e.RuleId)
                .ToList();

            foreach (var group in distinctByRuleId)
            {
                Assert.True(registeredById.TryGetValue(group.Key, out var summary),
                    $"数值规则 {group.Key} 已登记在 NumericValidationRuleCatalog，但未出现在 " +
                    "ContentValidationAssembly.Run 的 ValidationReport.Rules 里——可能是装配根漏注册" +
                    "（RulesSchemaCatalog/CarriersSchemaCatalog/GameplaySchemaCatalog/" +
                    "PresentationSchemaCatalog 之一少了一行 RegisterValidationRule）。");

                // NonEscalatable 是 IValidationRule 实现类整体的属性（不是逐检查名的，见
                // IValidationRule.NonEscalatable 类型级判断记录），本清单里同一 RuleId 的多个条目
                // 理应彼此一致——先自检，再跟真实注册结果（ValidationRuleSummary.NonEscalatable，
                // 直接读自规则实例）比对。Severity 不做同类断言：ItemBudgetValidationRule/
                // SkillBudgetValidationRule 两个类各自的两条检查名一条 Error 一条 Warning，
                // 本就不同，是每条 ValidationIssue 自己的严重级，不是规则整体的单一属性（见
                // NumericValidationRuleDescriptor.Severity 判断记录）。
                var expectedNonEscalatable = group.First().NonEscalatable;
                foreach (var entry in group)
                {
                    Assert.Equal(expectedNonEscalatable, entry.NonEscalatable);
                }

                Assert.Equal(expectedNonEscalatable, summary.NonEscalatable);
            }
        }

        /// <summary>ContentValidationAssembly.NumericRules 只是 NumericValidationRuleCatalog.Entries
        /// 的单一来源转发（见该属性判断记录），两者必须是同一份列表，不是各自维护的两份拷贝。</summary>
        [Fact]
        public void ContentValidationAssembly_NumericRules_IsSameInstanceAsCatalogEntries()
        {
            Assert.Same(NumericValidationRuleCatalog.Entries, ContentValidationAssembly.NumericRules);
        }
    }
}
