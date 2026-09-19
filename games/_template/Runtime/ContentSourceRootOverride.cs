#nullable enable
// ContentSourceRootOverride：消费方反馈第 70 条根治——让外部工具/开发者通过命令行参数或环境变量
// 指定 GameBootstrap 数据加载与 Runtime/DataHotReload.cs 监视所使用的"内容根"，替换掉默认的部署
// 副本根（Application.streamingAssetsPath/GameFoundation，见
// Adapter.Unity.EngineAdapter.UnityFileSystem 类型头注释）。
//
// 判断记录（缺口范围，见勘察结论）：UnityFileSystem 的构造函数早已支持可选 contentRoot 参数
// （见该类型 79/82 行），此前只是 GameBootstrap.cs 从未传——本类型只负责"从命令行/环境变量解析出
// 这个可选覆盖值"这一段接线，不是新造底层能力。
//
// 判断记录（为何是命令行参数 + 环境变量两种入口，不是 GameOptions 字段）：
// 1）本项是"这一次进程/这台开发机"级别的开发期覆盖（通常指向某个具体游戏仓库自己的数据源目录，
//    例如 "D:/workespace/<game>/data"），不是随游戏一起分发、随存档/口味走的配置项——不适合放进
//    Inspector 序列化的 GameOptions（那类字段容易被误当成游戏设计口味提交进版本库，与
//    UnityFileSystem.UserDataRootOverride"全局一次性覆盖，不是构造参数"是同一类判断）。
// 2）沿用 adapters/unity/Assets/Editor/Il2CppPlayerBuilder.cs.ResolveOutputPath() 已有的
//    "-gfXxx <value> 命令行参数 + 同名环境变量兜底"惯例（命令行优先于环境变量），不新造一套规则；
//    环境变量入口方便在编辑器 Play 模式（无法附加命令行参数）下同样生效，PlayMode 测试也用它注入。
//
// 判断记录（不存在的目录必须显式报错，不能静默回退，AGENTS.md 第 3 节"运行时路径不静默降级"）：
// 拼错路径/目录还没建好时，若静默回退到默认部署副本根，开发者会误以为"热重载已经在监视内容源
// 目录"，实际仍在监视旧的部署副本，制造比直接报错更隐蔽、更难排查的问题。
//
// 判断记录（纯函数 + 依赖注入，供单元测试）：Resolve/ResolveAndValidate 只接收调用方传入的
// commandLineArgs/getEnvironmentVariable/directoryExists，不在内部直接调用
// Environment.GetCommandLineArgs()/Environment.GetEnvironmentVariable()/Directory.Exists——
// 跑单元测试的进程自己的命令行参数/环境变量/磁盘状态会干扰断言，注入后测试可以喂入任意确定的
// 输入，不依赖真实进程环境（真实调用点见 GameBootstrap.Bootstrap()）。本文件不引用 UnityEngine，
// 是纯 .NET 逻辑（仍随 Game.Template 装配，Core.sln 不包含本工程，见该类型测试文件判断记录）。
using System;
using System.Collections.Generic;

namespace Game.Template
{
    /// <summary>见文件头判断记录。</summary>
    public static class ContentSourceRootOverride
    {
        /// <summary>命令行参数名（后跟一个值，即目标内容源目录的绝对/相对路径），惯例同
        /// <c>Il2CppPlayerBuilder.ResolveOutputPath</c> 的 <c>-gfOutputPath</c>。</summary>
        public const string CommandLineFlag = "-gfContentRoot";

        /// <summary>环境变量名兜底，命令行参数未指定时读取。</summary>
        public const string EnvironmentVariable = "GF_CONTENT_ROOT";

        /// <summary>解析原始覆盖值（未做存在性校验）：命令行参数优先于环境变量；两者都未指定（或
        /// 环境变量为空字符串）时返回 <c>null</c>——即"不覆盖，沿用 UnityFileSystem 默认内容根"，
        /// 与本次改动前完全一致的行为。</summary>
        public static string? Resolve(IReadOnlyList<string> commandLineArgs, Func<string, string?> getEnvironmentVariable)
        {
            if (commandLineArgs == null) throw new ArgumentNullException(nameof(commandLineArgs));
            if (getEnvironmentVariable == null) throw new ArgumentNullException(nameof(getEnvironmentVariable));

            for (var i = 0; i < commandLineArgs.Count - 1; i++)
            {
                if (string.Equals(commandLineArgs[i], CommandLineFlag, StringComparison.Ordinal))
                {
                    return commandLineArgs[i + 1];
                }
            }

            var env = getEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrEmpty(env) ? null : env;
        }

        /// <summary>在 <see cref="Resolve"/> 基础上做存在性校验：未指定覆盖值时直接返回
        /// <c>null</c>（<paramref name="directoryExists"/> 不会被调用，行为与改动前完全一致）；
        /// 指定了但目录不存在时抛出 <see cref="InvalidOperationException"/>（见文件头判断记录
        /// "不存在的目录必须显式报错"）。</summary>
        public static string? ResolveAndValidate(
            IReadOnlyList<string> commandLineArgs,
            Func<string, string?> getEnvironmentVariable,
            Func<string, bool> directoryExists)
        {
            if (directoryExists == null) throw new ArgumentNullException(nameof(directoryExists));

            var raw = Resolve(commandLineArgs, getEnvironmentVariable);
            if (raw == null)
            {
                return null;
            }

            if (!directoryExists(raw))
            {
                throw new InvalidOperationException(
                    $"[GameBootstrap] 通过 {CommandLineFlag}/{EnvironmentVariable} 指定的内容源目录不存在：\"{raw}\"。" +
                    "请核对路径拼写；不传该参数/环境变量则沿用默认部署副本根（Application.streamingAssetsPath/GameFoundation）。");
            }

            return raw;
        }
    }
}
