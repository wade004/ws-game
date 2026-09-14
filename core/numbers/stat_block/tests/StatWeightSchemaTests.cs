using System;
using System.IO;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Numbers.StatBlock
{
    /// <summary>
    /// 分阶段落地计划 T-N1-5（ADR-0030 决策 7；04 第 1.1 节表清单 <c>stat.weight</c> 行）：
    /// <see cref="StatSchemas.Weight"/> 的 schema 覆盖测试（合法记录、缺必填、引用不存在、
    /// <c>class_overrides</c> 子结构坏形状，惯例同 <c>StatSchemaCoverageTests.cs</c>）与
    /// "<see cref="StatHost"/> 不读该表"防御测试（源码扫描 + 行为双重覆盖，见类型顶部禁止事项
    /// "禁止 StatHost 消费权重表"）。
    /// </summary>
    public sealed class StatWeightSchemaTests
    {
        private static string EnvelopeV2(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":2,\"rows\":" + rowsJson + "}";

        private static string Envelope1(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":" + rowsJson + "}";

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        /// <summary>装配一个只登记 <c>stat.definition</c>/<c>stat.weight</c> 两张真 schema 的最小
        /// 注册表；<c>arch.class</c> 按 <c>core/numbers/tests/L1SampleDataTests.cs
        /// BuildWorld</c> 既有手法以 <c>FailOnUnknownTable=false</c> 加载为无 schema 占位表——本模块
        /// （<c>stat_block</c>）不依赖 <c>Core.Numbers.Archetype</c> 程序集（同 06 第 1 节"L1 各
        /// 模块间不静态耦合表结构"分层边界），测试只需要 <c>class_overrides[].class</c> 引用完整性
        /// 检查能找到一条同 id 的已加载记录，不需要真正构造 <c>ArchSchemas.Class</c>。</summary>
        private static DataRegistry BuildRegistry(string definitionRowsJson, string weightRowsJson, string? archClassRowsJson = null)
        {
            var source = new InMemoryDataSource()
                .Add(StatSchemas.Definition.Name, EnvelopeV2(StatSchemas.Definition.Name, definitionRowsJson))
                .Add(StatSchemas.Weight.Name, Envelope1(StatSchemas.Weight.Name, weightRowsJson));
            if (archClassRowsJson != null)
            {
                source.Add("arch.class", Envelope1("arch.class", archClassRowsJson));
            }

            var registry = new DataRegistry(source, NewBus(), new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(StatSchemas.Weight);
            return registry;
        }

        // -----------------------------------------------------------------
        // T-N1-5：stat.weight 合法记录 / 缺必填 / 引用不存在 / class_overrides 子结构坏形状。
        // -----------------------------------------------------------------

        [Fact]
        public void Weight_WellFormed_LoadsWithoutErrors()
        {
            var defRows = "[{\"id\":\"stat.cov_w_src\",\"name_key\":\"l10n.w_src\",\"category\":\"primary\",\"group\":\"primary\"}]";
            var weightRows = "[{\"id\":\"stat.weight.cov_w_src\",\"stat\":\"stat.cov_w_src\",\"weight\":1.0," +
                "\"class_overrides\":[{\"class\":\"arch.class.cov_sample\",\"weight\":1.5}]}]";
            var archRows = "[{\"id\":\"arch.class.cov_sample\"}]";

            var registry = BuildRegistry(defRows, weightRows, archRows);

            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Weight_MissingWeightField_ReportsRequiredField()
        {
            var defRows = "[{\"id\":\"stat.cov_w_src2\",\"name_key\":\"l10n.w_src2\",\"category\":\"primary\",\"group\":\"primary\"}]";
            var weightRows = "[{\"id\":\"stat.weight.cov_bad\",\"stat\":\"stat.cov_w_src2\"}]";

            var registry = BuildRegistry(defRows, weightRows);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "weight");
        }

        [Fact]
        public void Weight_StatReferenceNotExist_ReportsReferenceIntegrity()
        {
            var defRows = "[]";
            var weightRows = "[{\"id\":\"stat.weight.cov_dangling\",\"stat\":\"stat.does_not_exist\",\"weight\":1.0}]";

            var registry = BuildRegistry(defRows, weightRows);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "stat");
        }

        [Fact]
        public void Weight_ClassOverrides_MissingWeight_ReportsRequiredField()
        {
            var defRows = "[{\"id\":\"stat.cov_w_src3\",\"name_key\":\"l10n.w_src3\",\"category\":\"primary\",\"group\":\"primary\"}]";
            var weightRows = "[{\"id\":\"stat.weight.cov_bad_override\",\"stat\":\"stat.cov_w_src3\",\"weight\":1.0," +
                "\"class_overrides\":[{\"class\":\"arch.class.cov_sample2\"}]}]";
            var archRows = "[{\"id\":\"arch.class.cov_sample2\"}]";

            var registry = BuildRegistry(defRows, weightRows, archRows);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "required_field" && i.Field == "class_overrides[0].weight");
        }

        [Fact]
        public void Weight_ClassOverrides_ClassReferenceNotExist_ReportsReferenceIntegrity()
        {
            var defRows = "[{\"id\":\"stat.cov_w_src4\",\"name_key\":\"l10n.w_src4\",\"category\":\"primary\",\"group\":\"primary\"}]";
            var weightRows = "[{\"id\":\"stat.weight.cov_dangling_override\",\"stat\":\"stat.cov_w_src4\",\"weight\":1.0," +
                "\"class_overrides\":[{\"class\":\"arch.class.does_not_exist\",\"weight\":1.0}]}]";

            var registry = BuildRegistry(defRows, weightRows);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "reference_integrity" && i.Field == "class_overrides[0].class");
        }

        [Fact]
        public void Weight_NegativeWeight_ReportsFieldRange()
        {
            var defRows = "[{\"id\":\"stat.cov_w_src5\",\"name_key\":\"l10n.w_src5\",\"category\":\"primary\",\"group\":\"primary\"}]";
            var weightRows = "[{\"id\":\"stat.weight.cov_negative\",\"stat\":\"stat.cov_w_src5\",\"weight\":-1.0}]";

            var registry = BuildRegistry(defRows, weightRows);

            var report = registry.LoadAll();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "weight");
        }

        // -----------------------------------------------------------------
        // 禁止事项"禁止 StatHost 消费权重表"防御：源码扫描 + 行为测试双重覆盖。
        // -----------------------------------------------------------------

        /// <summary>定位本源文件在磁盘上的绝对路径（同 <c>core/numbers/tests/L1SampleDataTests.cs
        /// FindRepoRoot</c> 手法），不依赖运行期程序集目录——测试按任务书要求用 --artifacts-path
        /// 输出到仓库外的 scratch 目录，不能假设输出目录与源码目录同构。本文件固定位于
        /// <c>core/numbers/stat_block/tests/StatWeightSchemaTests.cs</c>，同目录的上一级
        /// （<c>tests</c> 的父目录）即 <c>stat_block</c>，从那里拼 <c>core/StatHost.cs</c> 即目标
        /// 源文件。</summary>
        private static string StatHostSourcePath([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var testsDir = Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空");
            var statBlockDir = new DirectoryInfo(testsDir).Parent
                ?? throw new InvalidOperationException($"源文件路径层级不足：{sourceFilePath}");
            return Path.Combine(statBlockDir.FullName, "core", "StatHost.cs");
        }

        [Fact]
        public void StatHostSource_DoesNotReferenceStatWeightTable()
        {
            var path = StatHostSourcePath();
            Assert.True(File.Exists(path), $"StatHost.cs 源文件未找到：{path}");

            var text = File.ReadAllText(path);

            Assert.DoesNotContain("stat.weight", text, StringComparison.Ordinal);
        }

        [Fact]
        public void StatHost_TablesLoaded_WithStatWeightPresent_DoesNotThrowAndIgnoresIt()
        {
            var defRows = "[{\"id\":\"stat.cov_host_a\",\"name_key\":\"l10n.host_a\",\"category\":\"primary\",\"group\":\"primary\"," +
                "\"default_base\":10}]";
            var weightRows = "[{\"id\":\"stat.weight.cov_host_a\",\"stat\":\"stat.cov_host_a\",\"weight\":1.0}]";

            var registry = BuildRegistry(defRows, weightRows);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var bus = NewBus();
            // StatHost 构造/注册/取值全程正常——stat.weight 表已加载进 registry，但 StatHost 不读取
            // 它（源码扫描断言见 StatHostSource_DoesNotReferenceStatWeightTable），本用例断言"不抛
            // 异常、取值不受影响"这一行为层面的结论，与源码扫描互为补充证据。
            var statHost = new StatHost(registry, bus, new StatHostOptions());
            var unitId = new Id("unit.cov_host");
            statHost.RegisterUnit(unitId);

            var value = statHost.GetStat(unitId, new Id("stat.cov_host_a"));

            Assert.Equal(10.0, value);
        }
    }
}
