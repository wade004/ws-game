#nullable enable
// WindowsPlayerBuilderArgsTests：消费方反馈第 72 条根治——验证
// Editor/WindowsPlayerBuilderArgs.cs 纯逻辑（Development Build 存在性标志、输出路径命令行参数
// 优先于环境变量、两者都未指定时显式报错不静默降级）。全部用例都是 [Test]（不是 [UnityTest]），
// 不依赖 Play Mode，也不触碰真实 Environment.GetCommandLineArgs()/
// Environment.GetEnvironmentVariable()——被测方法本身按依赖注入设计（见该类型文件头判断记录），
// 测试喂入确定的假输入，不受跑测试这个进程自己的命令行参数/环境变量影响，同
// ContentSourceRootOverrideTests.cs 同款手法。
using System;
using System.Collections.Generic;
using Game.Template.EditorTools;
using NUnit.Framework;

namespace Game.Template.EditorTests
{
    public sealed class WindowsPlayerBuilderArgsTests
    {
        private static Func<string, string?> EnvLookup(string? value) => _ => value;

        // ---- ResolveDevelopmentBuildFlag ----

        [Test]
        public void ResolveDevelopmentBuildFlag_FlagPresent_ReturnsTrue()
        {
            var args = new List<string> { "-batchmode", WindowsPlayerBuilderArgs.DevelopmentBuildFlag, "-quit" };

            var result = WindowsPlayerBuilderArgs.ResolveDevelopmentBuildFlag(args);

            Assert.IsTrue(result, "命令行出现 -gfDevelopmentBuild 时应视为请求 Development Build");
        }

        [Test]
        public void ResolveDevelopmentBuildFlag_FlagAbsent_ReturnsFalse()
        {
            var args = new List<string> { "-batchmode", "-quit" };

            var result = WindowsPlayerBuilderArgs.ResolveDevelopmentBuildFlag(args);

            Assert.IsFalse(result,
                "不传 -gfDevelopmentBuild 时应返回 false，与本类型新增之前 BuildOptions.None 的默认行为一致");
        }

        [Test]
        public void ResolveDevelopmentBuildFlag_EmptyArgs_ReturnsFalse()
        {
            var result = WindowsPlayerBuilderArgs.ResolveDevelopmentBuildFlag(new List<string>());

            Assert.IsFalse(result);
        }

        // ---- ResolveOutputPath ----

        [Test]
        public void ResolveOutputPath_CommandLineFlagPresent_TakesPrecedenceOverEnvironmentVariable()
        {
            var args = new List<string> { "-batchmode", WindowsPlayerBuilderArgs.OutputPathFlag, "D:/cli-output.exe", "-quit" };

            var result = WindowsPlayerBuilderArgs.ResolveOutputPath(args, EnvLookup("D:/env-output.exe"));

            Assert.AreEqual("D:/cli-output.exe", result, "命令行参数应优先于环境变量");
        }

        [Test]
        public void ResolveOutputPath_OnlyEnvironmentVariablePresent_ReturnsEnvironmentValue()
        {
            var args = new List<string> { "-batchmode", "-quit" };

            var result = WindowsPlayerBuilderArgs.ResolveOutputPath(args, EnvLookup("D:/env-output.exe"));

            Assert.AreEqual("D:/env-output.exe", result);
        }

        [Test]
        public void ResolveOutputPath_EnvironmentVariableEmptyString_TreatedAsNotSet_Throws()
        {
            var args = new List<string>();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                WindowsPlayerBuilderArgs.ResolveOutputPath(args, EnvLookup(string.Empty)));

            StringAssert.Contains(WindowsPlayerBuilderArgs.OutputPathFlag, ex!.Message);
        }

        [Test]
        public void ResolveOutputPath_CommandLineFlagIsLastToken_NoValueFollowing_FallsBackToEnvironmentVariable()
        {
            // 判断记录：命令行参数循环条件是 i < commandLineArgs.Count - 1，标志出现在最后一个 token
            // （没有后续值可取）时不会越界读取，也不应把标志本身之后"不存在的值"当成有效结果，而是
            // 落到环境变量兜底——与 ContentSourceRootOverride.Resolve 同款边界处理一致。
            var args = new List<string> { "-batchmode", WindowsPlayerBuilderArgs.OutputPathFlag };

            var result = WindowsPlayerBuilderArgs.ResolveOutputPath(args, EnvLookup("D:/env-output.exe"));

            Assert.AreEqual("D:/env-output.exe", result);
        }

        [Test]
        public void ResolveOutputPath_NeitherSpecified_ThrowsWithClearMessage()
        {
            var args = new List<string> { "-batchmode", "-quit" };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                WindowsPlayerBuilderArgs.ResolveOutputPath(args, EnvLookup(null)));

            Assert.IsFalse(string.IsNullOrWhiteSpace(ex!.Message),
                "不能静默失败：必须有明确的报错信息（AGENTS.md 第 3 节\"运行时路径不静默降级\"）");
            StringAssert.Contains(WindowsPlayerBuilderArgs.OutputPathFlag, ex.Message,
                "错误信息应提示是哪个命令行参数");
            StringAssert.Contains(WindowsPlayerBuilderArgs.OutputPathEnvironmentVariable, ex.Message,
                "错误信息应提示是哪个环境变量兜底");
        }
    }
}
