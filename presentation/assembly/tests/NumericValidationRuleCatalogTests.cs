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
        /// 不同 RuleId 各占一行 + B2～B11 各一行）、警告 7 行，合计 20 行，是 18 个概念条目的超集。</summary>
        [Fact]
        public void Catalog_HasThirteenBlockingRowsAndSevenWarningRows_TwentyTotal()
        {
            Assert.Equal(20, NumericValidationRuleCatalog.Entries.Count);
            Assert.Equal(13, NumericValidationRuleCatalog.Entries.Count(e => e.Severity == ValidationSeverity.Error));
            Assert.Equal(7, NumericValidationRuleCatalog.Entries.Count(e => e.Severity == ValidationSeverity.Warning));
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
        /// </summary>
        [Fact]
        public void Catalog_EveryDistinctRuleId_IsActuallyRegistered_AgainstSampleData()
        {
            var sources = BuildSampleDataSources();
            var options = new ContentValidationOptions();
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
