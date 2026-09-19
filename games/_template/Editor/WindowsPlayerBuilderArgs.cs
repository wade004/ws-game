#nullable enable
// WindowsPlayerBuilderArgs：从 WindowsPlayerBuilder.cs 抽出的纯参数解析逻辑（消费方反馈第 72 条
// 补测试单）。
//
// 判断记录（为什么单独抽一个类型，而不是直接给 WindowsPlayerBuilder 的两个私有方法写测试）：
// WindowsPlayerBuilder.ResolveDevelopmentBuildFlag/ResolveOutputPath 改动前直接调用
// Environment.GetCommandLineArgs()/Environment.GetEnvironmentVariable()——跑测试这个进程自己的
// 命令行参数/环境变量会污染断言，且两个方法是 private，测试跑不到。本类型把"从给定的参数列表/
// 环境变量取值函数里解析出目标值"这一段纯逻辑抽成 public 静态方法、按依赖注入接收输入（不在内部
// 调用 Environment.* API），与 Runtime/ContentSourceRootOverride.cs（消费方反馈第 70 条）
// Resolve/ResolveAndValidate 同一个判断记录、同一种手法：调用方（WindowsPlayerBuilder）仍然只有
// BuildWindows64Player 这一个 public 入口，行为完全不变；只是新增了一个可独立单测的纯函数类型，
// 不删不改任何既有公开签名（AGENTS.md 第 3 节 ABI 只新增）。
//
// 判断记录（为什么不复用 ContentSourceRootOverride 本身）：两者字段名/命令行标志/校验语义都不同
// （本类型是"输出路径缺失时报错"+"独立的布尔存在性标志"，ContentSourceRootOverride 是"内容根缺失时
// 返回 null 不覆盖"+"额外的目录存在性校验"），语义耦合到一起会让任何一侧改动都要小心不影响另一侧，
// 不如按 Il2CppPlayerBuilder.ResolveOutputPath 的既有惯例各自独立解析。
//
// 本文件不引用 UnityEditor/UnityEngine，是纯 .NET 逻辑；但仍随 Game.Template.Editor 装配（Editor
// 专属，Core.sln 不包含本工程，同 ContentSourceRootOverride.cs 判断记录“Core.sln 不包含本工程”一节
// ——本仓库对"纯逻辑但住在游戏/编辑器专属装配里"的模块统一用 Unity 侧 EditMode（[Test]，非
// [UnityTest]，不依赖 Play Mode）单测覆盖，不额外为了自测方便另起一套 dotnet 测试工程）。
using System;
using System.Collections.Generic;

namespace Game.Template.EditorTools
{
    /// <summary>见文件头判断记录。</summary>
    public static class WindowsPlayerBuilderArgs
    {
        /// <summary>纯存在性开关的命令行 token；不跟值。</summary>
        public const string DevelopmentBuildFlag = "-gfDevelopmentBuild";

        /// <summary>输出路径命令行参数名，后跟一个值。</summary>
        public const string OutputPathFlag = "-gfOutputPath";

        /// <summary>输出路径环境变量兜底名，命令行参数未指定时读取。</summary>
        public const string OutputPathEnvironmentVariable = "GF_OUTPUT_PATH";

        /// <summary>纯存在性检查：<paramref name="commandLineArgs"/> 里出现
        /// <see cref="DevelopmentBuildFlag"/> 即返回 <c>true</c>，不需要跟一个值；不传时返回
        /// <c>false</c>——与 <see cref="WindowsPlayerBuilder"/> 新增之前直接调用内置
        /// <c>-buildWindows64Player</c> 开关的默认行为（<c>BuildOptions.None</c>）完全一致。</summary>
        public static bool ResolveDevelopmentBuildFlag(IReadOnlyList<string> commandLineArgs)
        {
            if (commandLineArgs == null) throw new ArgumentNullException(nameof(commandLineArgs));

            foreach (var arg in commandLineArgs)
            {
                if (string.Equals(arg, DevelopmentBuildFlag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>输出路径解析优先级：命令行参数 <see cref="OutputPathFlag"/> &gt; 环境变量
        /// <see cref="OutputPathEnvironmentVariable"/>；两者都没有时抛出
        /// <see cref="InvalidOperationException"/>（不猜测默认路径，避免误覆盖仓库内任何文件，见
        /// <c>Adapter.Unity.EditorTools.Il2CppPlayerBuilder.ResolveOutputPath</c> 同款判断
        /// 记录）。</summary>
        public static string ResolveOutputPath(
            IReadOnlyList<string> commandLineArgs,
            Func<string, string?> getEnvironmentVariable)
        {
            if (commandLineArgs == null) throw new ArgumentNullException(nameof(commandLineArgs));
            if (getEnvironmentVariable == null) throw new ArgumentNullException(nameof(getEnvironmentVariable));

            for (var i = 0; i < commandLineArgs.Count - 1; i++)
            {
                if (string.Equals(commandLineArgs[i], OutputPathFlag, StringComparison.Ordinal))
                {
                    return commandLineArgs[i + 1];
                }
            }

            var envPath = getEnvironmentVariable(OutputPathEnvironmentVariable);
            if (!string.IsNullOrEmpty(envPath))
            {
                return envPath;
            }

            throw new InvalidOperationException(
                "WindowsPlayerBuilder.BuildWindows64Player 需要通过 -gfOutputPath <path> " +
                "命令行参数或 GF_OUTPUT_PATH 环境变量指定构建产物输出路径。");
        }
    }
}
