using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.Diagnostics;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Adapter.Unity.Diagnostics.Tests
{
    /// <summary>ADR-0047 运行期校验报告落盘出口的 dotnet test 侧验证——覆盖
    /// <see cref="ValidationReportFileOutlet"/> 的选项解析、落盘 JSON 形状、单调递增序号、原子写。
    /// 每个测试用例用独立的临时目录（<see cref="NewScratchDir"/>），互不干扰，也不依赖
    /// <see cref="ValidationReportFileOutlet"/> 内部按路径分别计数的序号在跨测试用例之间保持初始值
    /// ——只断言同一路径下"后一次比前一次大"，不断言绝对值。</summary>
    public class ValidationReportFileOutletTests : IDisposable
    {
        private readonly List<string> _scratchDirs = new List<string>();

        private string NewScratchDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "gf-valfile-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _scratchDirs.Add(dir);
            return dir;
        }

        public void Dispose()
        {
            foreach (var dir in _scratchDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // 测试环境清理失败不影响断言结果，忽略。
                }
            }
        }

        private static ValidationIssue MakeIssue(string check = "required_field", string? recordKey = "sample.row", string? field = "hp") =>
            new ValidationIssue(ValidationSeverity.Error, "found.sample_table", check, "示例问题消息", recordKey, field);

        [Fact]
        public void ResolvePath_CommandLineFlag_TakesPrecedenceOverEnvironmentVariable()
        {
            var args = new[] { "game.exe", ValidationReportFileOutlet.CommandLineFlag, "C:/from-cli/report.json" };
            var path = ValidationReportFileOutlet.ResolvePath(args, name =>
                name == ValidationReportFileOutlet.EnvironmentVariable ? "C:/from-env/report.json" : null);

            Assert.Equal("C:/from-cli/report.json", path);
        }

        [Fact]
        public void ResolvePath_EnvironmentVariable_UsedWhenCommandLineFlagAbsent()
        {
            var args = new[] { "game.exe" };
            var path = ValidationReportFileOutlet.ResolvePath(args, name =>
                name == ValidationReportFileOutlet.EnvironmentVariable ? "C:/from-env/report.json" : null);

            Assert.Equal("C:/from-env/report.json", path);
        }

        [Fact]
        public void ResolvePath_NeitherSpecified_ReturnsNull()
        {
            var path = ValidationReportFileOutlet.ResolvePath(new[] { "game.exe" }, _ => null);
            Assert.Null(path);
        }

        [Fact]
        public void WriteIfConfigured_PathNotConfigured_DoesNotCreateAnyFile()
        {
            var dir = NewScratchDir();
            var wouldBePath = Path.Combine(dir, "report.json");

            ValidationReportFileOutlet.WriteIfConfigured(null, ValidationReportTriggerSource.Startup, new[] { MakeIssue() });
            ValidationReportFileOutlet.WriteIfConfigured(string.Empty, ValidationReportTriggerSource.Startup, new[] { MakeIssue() });

            Assert.False(File.Exists(wouldBePath));
            Assert.Empty(Directory.GetFileSystemEntries(dir));
        }

        [Fact]
        public void Write_OnFailure_FileContentMatchesCommandLineJsonElementShape()
        {
            var dir = NewScratchDir();
            var path = Path.Combine(dir, "report.json");
            var issue = MakeIssue();

            ValidationReportFileOutlet.Write(path, ValidationReportTriggerSource.Startup, new[] { issue }, () => new DateTime(2026, 9, 20, 3, 4, 5, 6, DateTimeKind.Utc));

            var text = File.ReadAllText(path);
            var root = Assert.IsType<JsonObject>(JsonReader.Parse(text));

            Assert.True(root.TryGetValue("sequence", out var sequenceValue));
            Assert.True(Assert.IsType<JsonNumber>(sequenceValue).TryGetInt64(out var sequence));
            Assert.Equal(1, sequence);

            Assert.True(root.TryGetValue("source", out var sourceValue));
            Assert.Equal(ValidationReportTriggerSource.Startup, Assert.IsType<JsonString>(sourceValue).Value);

            Assert.True(root.TryGetValue("timestamp_utc", out var timestampValue));
            Assert.Equal("2026-09-20T03:04:05.006Z", Assert.IsType<JsonString>(timestampValue).Value);

            Assert.True(root.TryGetValue("issues", out var issuesValue));
            var issuesArray = Assert.IsType<JsonArray>(issuesValue);
            Assert.Single(issuesArray);
            var issueObj = Assert.IsType<JsonObject>(issuesArray[0]);

            // 元素字段形状必须与 toolchain/validator --json 的 issues[] 元素完全一致——直接对照
            // ValidationIssueJsonWriter.AppendIssueJson 独立产出的同一条 issue，逐字节相等。
            var expectedIssueJson = new System.Text.StringBuilder();
            ValidationIssueJsonWriter.AppendIssueJson(expectedIssueJson, issue);
            var expectedIssueObj = Assert.IsType<JsonObject>(JsonReader.Parse(expectedIssueJson.ToString()));
            Assert.Equal(expectedIssueObj.Select(kv => kv.Key), issueObj.Select(kv => kv.Key));

            Assert.Equal("error", Assert.IsType<JsonString>(issueObj["severity"]).Value);
            Assert.Equal("found.sample_table", Assert.IsType<JsonString>(issueObj["table"]).Value);
            Assert.Equal("required_field", Assert.IsType<JsonString>(issueObj["check"]).Value);
        }

        [Fact]
        public void Write_OnSuccess_WritesEmptyIssuesArray_NotOmitted()
        {
            var dir = NewScratchDir();
            var path = Path.Combine(dir, "report.json");

            ValidationReportFileOutlet.Write(path, ValidationReportTriggerSource.Startup, Array.Empty<ValidationIssue>());

            var root = Assert.IsType<JsonObject>(JsonReader.Parse(File.ReadAllText(path)));
            Assert.True(root.TryGetValue("issues", out var issuesValue));
            Assert.Empty(Assert.IsType<JsonArray>(issuesValue));
        }

        [Fact]
        public void Write_SequenceIsMonotonicallyIncreasing_AcrossStartupAndHotReload()
        {
            var dir = NewScratchDir();
            var path = Path.Combine(dir, "report.json");

            var first = ValidationReportFileOutlet.Write(path, ValidationReportTriggerSource.Startup, Array.Empty<ValidationIssue>());
            var second = ValidationReportFileOutlet.Write(path, ValidationReportTriggerSource.HotReload, new[] { MakeIssue() });
            var third = ValidationReportFileOutlet.Write(path, ValidationReportTriggerSource.HotReload, Array.Empty<ValidationIssue>());

            Assert.True(second > first);
            Assert.True(third > second);

            var root = Assert.IsType<JsonObject>(JsonReader.Parse(File.ReadAllText(path)));
            Assert.True(Assert.IsType<JsonNumber>(root["sequence"]).TryGetInt64(out var writtenSequence));
            Assert.Equal(third, writtenSequence);
        }

        [Fact]
        public void Write_DifferentPaths_EachGetsItsOwnSequenceStartingAtOne()
        {
            var dir = NewScratchDir();
            var pathA = Path.Combine(dir, "a.json");
            var pathB = Path.Combine(dir, "b.json");

            var a1 = ValidationReportFileOutlet.Write(pathA, ValidationReportTriggerSource.Startup, Array.Empty<ValidationIssue>());
            var b1 = ValidationReportFileOutlet.Write(pathB, ValidationReportTriggerSource.Startup, Array.Empty<ValidationIssue>());
            var a2 = ValidationReportFileOutlet.Write(pathA, ValidationReportTriggerSource.HotReload, Array.Empty<ValidationIssue>());

            Assert.Equal(1, a1);
            Assert.Equal(1, b1);
            Assert.Equal(2, a2);
        }

        [Fact]
        public void Write_CreatesMissingParentDirectory()
        {
            var dir = NewScratchDir();
            var path = Path.Combine(dir, "nested", "sub", "report.json");

            ValidationReportFileOutlet.Write(path, ValidationReportTriggerSource.Startup, Array.Empty<ValidationIssue>());

            Assert.True(File.Exists(path));
        }

        [Fact]
        public void Write_NeverLeavesTemporaryFileBehind_OnSuccess()
        {
            var dir = NewScratchDir();
            var path = Path.Combine(dir, "report.json");

            ValidationReportFileOutlet.Write(path, ValidationReportTriggerSource.Startup, new[] { MakeIssue() });
            ValidationReportFileOutlet.Write(path, ValidationReportTriggerSource.HotReload, Array.Empty<ValidationIssue>());

            var entries = Directory.GetFileSystemEntries(dir);
            Assert.Single(entries);
            Assert.Equal(path, entries[0]);
        }

        [Fact]
        public void Write_WithoutTableArgument_WritesJsonNullTable_BackwardCompatible()
        {
            // ADR-0055 前的既有三/四参数重载（不带 table）必须继续可用，且行为与新增重载显式传
            // table: null 完全一致——覆盖"既有调用点原样不动"这条向后兼容承诺。
            var dir = NewScratchDir();
            var path = Path.Combine(dir, "report.json");

            ValidationReportFileOutlet.Write(path, ValidationReportTriggerSource.Startup, new[] { MakeIssue() });

            var root = Assert.IsType<JsonObject>(JsonReader.Parse(File.ReadAllText(path)));
            Assert.True(root.TryGetValue("table", out var tableValue));
            Assert.IsType<JsonNull>(tableValue);

            // 既有四个字段必须原样都在，一个不少：本次改动是纯加法，不删改既有字段。
            Assert.True(root.TryGetValue("sequence", out _));
            Assert.True(root.TryGetValue("source", out _));
            Assert.True(root.TryGetValue("timestamp_utc", out _));
            Assert.True(root.TryGetValue("issues", out _));
        }

        [Fact]
        public void Write_HotReloadWithTable_WritesTableFieldEqualToReloadedTableName()
        {
            // ADR-0055（消费方反馈第 78 条）核心验收：hot_reload 来源落盘的 table 字段等于本次
            // 实际重载的那张表的名字——期望值由用例自己算出来（这里就是传入 Write 的那个字符串本身），
            // 不写死裸数。
            var dir = NewScratchDir();
            var path = Path.Combine(dir, "report.json");
            var reloadedTable = "skill.definition";

            ValidationReportFileOutlet.Write(path, ValidationReportTriggerSource.HotReload, Array.Empty<ValidationIssue>(), reloadedTable);

            var root = Assert.IsType<JsonObject>(JsonReader.Parse(File.ReadAllText(path)));
            Assert.True(root.TryGetValue("table", out var tableValue));
            Assert.Equal(reloadedTable, Assert.IsType<JsonString>(tableValue).Value);
            Assert.Equal(ValidationReportTriggerSource.HotReload, Assert.IsType<JsonString>(root["source"]).Value);
        }

        [Fact]
        public void Write_StartupSource_TableFieldIsJsonNull_NotEmptyStringOrSentinel()
        {
            // 启动全量校验没有单一确定的表：口径是 JSON null，不是空字符串，也不是 "all" 这类会被
            // 误读成真实表名的占位值（ADR-0055 决策）。
            var dir = NewScratchDir();
            var path = Path.Combine(dir, "report.json");

            ValidationReportFileOutlet.Write(path, ValidationReportTriggerSource.Startup, Array.Empty<ValidationIssue>(), table: null);

            var root = Assert.IsType<JsonObject>(JsonReader.Parse(File.ReadAllText(path)));
            Assert.True(root.TryGetValue("table", out var tableValue));
            Assert.IsType<JsonNull>(tableValue);
        }

        [Fact]
        public void WriteIfConfigured_WithTableArgument_PropagatesToFile()
        {
            var dir = NewScratchDir();
            var path = Path.Combine(dir, "report.json");

            ValidationReportFileOutlet.WriteIfConfigured(path, ValidationReportTriggerSource.HotReload, Array.Empty<ValidationIssue>(), "found.sample_table");

            var root = Assert.IsType<JsonObject>(JsonReader.Parse(File.ReadAllText(path)));
            Assert.Equal("found.sample_table", Assert.IsType<JsonString>(root["table"]).Value);
        }

        [Fact]
        public void BuildJson_WithoutTableOverload_DelegatesToNewOverloadWithNullTable_ByteForByteEqual()
        {
            // 既有不带 table 的 BuildJson 重载委托给新增重载并显式传 table: null（见该重载源码），
            // 两者必须逐字节相等——不是两份各自维护的实现巧合相等，且新字段 table 必须以 JSON null
            // 出现，不是被整体省略。
            var issue = MakeIssue();
            var timestamp = new DateTime(2026, 9, 21, 1, 2, 3, 4, DateTimeKind.Utc);

            var withoutTableOverload = ValidationReportFileOutlet.BuildJson(1, ValidationReportTriggerSource.Startup, timestamp, new[] { issue });
            var withNullTable = ValidationReportFileOutlet.BuildJson(1, ValidationReportTriggerSource.Startup, timestamp, new[] { issue }, table: null);

            Assert.Equal(withNullTable, withoutTableOverload);
            var root = Assert.IsType<JsonObject>(JsonReader.Parse(withoutTableOverload));
            Assert.True(root.TryGetValue("table", out var tableValue));
            Assert.IsType<JsonNull>(tableValue);
        }

        [Fact]
        public void Write_WhenTargetPathIsAnExistingDirectory_ThrowsAndCleansUpTempFile_FileEitherAbsentOrIntact()
        {
            // 原子写断言方式（AGENTS.md 要求"文件要么不存在要么完整"）：把落盘路径本身占用成一个目录，
            // 迫使内部 File.Move 失败（目标是已存在的目录，既不会被当作"已存在的旧文件"删除，也无法
            // 被改名覆盖），验证：① 异常向外传播、不静默吞掉；② 失败路径清理掉临时文件，不遗留
            // ".tmp-*" 半成品；③ 目标路径本身没有被写成任何中间态（此场景下仍然是目录，不是文件）。
            var dir = NewScratchDir();
            var path = Path.Combine(dir, "report.json");
            Directory.CreateDirectory(path);

            Assert.ThrowsAny<IOException>(() =>
                ValidationReportFileOutlet.Write(path, ValidationReportTriggerSource.Startup, Array.Empty<ValidationIssue>()));

            Assert.False(File.Exists(path));
            Assert.True(Directory.Exists(path));
            var leftoverTempFiles = Directory.GetFiles(dir, "*.tmp-*", SearchOption.AllDirectories);
            Assert.Empty(leftoverTempFiles);
        }
    }
}
