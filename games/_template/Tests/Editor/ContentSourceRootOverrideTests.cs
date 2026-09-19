#nullable enable
// ContentSourceRootOverrideTests：消费方反馈第 70 条根治——验证 Runtime/ContentSourceRootOverride.cs
// 纯逻辑（命令行参数优先于环境变量、都未指定时不覆盖、指定了不存在的目录显式报错）。全部用例都是
// [Test]（不是 [UnityTest]），不依赖 Play Mode，也不触碰真实 Environment.GetCommandLineArgs()/
// Environment.GetEnvironmentVariable()/Directory.Exists——被测方法本身按依赖注入设计（见该类型文件
// 头判断记录），测试喂入确定的假输入，不受跑测试这个进程自己的命令行参数/环境变量影响。
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Template.EditorTests
{
    public sealed class ContentSourceRootOverrideTests
    {
        private static Func<string, string?> EnvLookup(string? value) => _ => value;

        [Test]
        public void Resolve_CommandLineFlagPresent_TakesPrecedenceOverEnvironmentVariable()
        {
            var args = new List<string> { "-batchmode", ContentSourceRootOverride.CommandLineFlag, "D:/cli-root", "-quit" };

            var result = ContentSourceRootOverride.Resolve(args, EnvLookup("D:/env-root"));

            Assert.AreEqual("D:/cli-root", result, "命令行参数应优先于环境变量");
        }

        [Test]
        public void Resolve_OnlyEnvironmentVariablePresent_ReturnsEnvironmentValue()
        {
            var args = new List<string> { "-batchmode", "-quit" };

            var result = ContentSourceRootOverride.Resolve(args, EnvLookup("D:/env-root"));

            Assert.AreEqual("D:/env-root", result);
        }

        [Test]
        public void Resolve_NeitherSpecified_ReturnsNull()
        {
            var args = new List<string> { "-batchmode", "-quit" };

            var result = ContentSourceRootOverride.Resolve(args, EnvLookup(null));

            Assert.IsNull(result, "两者都未指定时应返回 null（=不覆盖，沿用默认内容根）");
        }

        [Test]
        public void Resolve_EnvironmentVariableEmptyString_TreatedAsNotSet()
        {
            var args = new List<string>();

            var result = ContentSourceRootOverride.Resolve(args, EnvLookup(string.Empty));

            Assert.IsNull(result, "空字符串环境变量应等价于未设置");
        }

        [Test]
        public void Resolve_CommandLineFlagIsLastToken_NoValueFollowing_FallsBackToEnvironmentVariable()
        {
            // 判断记录：命令行参数循环条件是 i < commandLineArgs.Count - 1，标志出现在最后一个 token
            // （没有后续值可取）时不会越界读取，也不应把标志本身之后"不存在的值"当成有效结果，而是
            // 落到环境变量兜底——与 Il2CppPlayerBuilder.ResolveOutputPath 同款参数解析的边界处理一致。
            var args = new List<string> { "-batchmode", ContentSourceRootOverride.CommandLineFlag };

            var result = ContentSourceRootOverride.Resolve(args, EnvLookup("D:/env-root"));

            Assert.AreEqual("D:/env-root", result);
        }

        [Test]
        public void ResolveAndValidate_NotSpecified_ReturnsNull_AndNeverCallsDirectoryExists()
        {
            var args = new List<string>();
            var directoryExistsCalled = false;

            var result = ContentSourceRootOverride.ResolveAndValidate(args, EnvLookup(null), _ =>
            {
                directoryExistsCalled = true;
                return false;
            });

            Assert.IsNull(result);
            Assert.IsFalse(directoryExistsCalled,
                "未指定覆盖值时不应该调用 directoryExists——本次改动前的默认行为不应受到任何额外磁盘探测影响");
        }

        [Test]
        public void ResolveAndValidate_DirectoryExists_ReturnsRawValue()
        {
            var args = new List<string> { ContentSourceRootOverride.CommandLineFlag, "D:/some/content-root" };

            var result = ContentSourceRootOverride.ResolveAndValidate(args, EnvLookup(null), _ => true);

            Assert.AreEqual("D:/some/content-root", result);
        }

        [Test]
        public void ResolveAndValidate_DirectoryDoesNotExist_ThrowsWithClearMessage()
        {
            var args = new List<string> { ContentSourceRootOverride.CommandLineFlag, "D:/typo-root" };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ContentSourceRootOverride.ResolveAndValidate(args, EnvLookup(null), _ => false));

            StringAssert.Contains("D:/typo-root", ex!.Message, "错误信息应包含实际指定的路径，方便定位拼写问题");
            StringAssert.Contains(ContentSourceRootOverride.CommandLineFlag, ex.Message, "错误信息应提示是哪个入口触发的覆盖");
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message), "不能静默失败：必须有明确的报错信息（AGENTS.md 第 3 节\"运行时路径不静默降级\"）");
        }
    }
}
