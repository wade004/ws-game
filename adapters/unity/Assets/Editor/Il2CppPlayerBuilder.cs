#nullable enable
// Il2CppPlayerBuilder：IL2CPP 脚本后端独立版构建入口（工程收尾 K 新增，见任务书"IL2CPP 发布路径
// 验证"一节）。
//
// 判断记录（为什么不能像默认流程一样直接用内置 `-buildWindows64Player` 命令行开关）：check.ps1
// 默认流程（"独立版构建 + -gf-smoke 冒烟"步骤）用的内置开关按 ProjectSettings.asset 里当前已保存
// 的脚本后端构建（Windows Standalone 平台默认 Mono），命令行本身不提供"临时切到 IL2CPP 再构建"
// 的开关；必须走自定义 `-executeMethod`，在构建前用 PlayerSettings.SetScriptingBackend 把
// NamedBuildTarget.Standalone 的后端临时切到 IL2CPP，BuildPipeline.BuildPlayer 结束后再切回
// 构建前读到的原始值——任务书硬性规则要求"ProjectSettings 不永久切换后端"，见下方 Build 方法
// try/finally。
//
// 判断记录（2026-09-06 实跑修正：finally 里的显式还原不是"防御性写法"，是必需的）：本方法最初
// 的判断记录曾以为"只要不调用 AssetDatabase.SaveAssets()，改动就不会落盘"，实测证伪——
// BuildPipeline.BuildPlayer 跑过一次构建之后，即使本方法自己不调用 SaveAssets，Unity 在
// -quit 退出编辑器域的过程中仍然把 PlayerSettings 序列化落盘了一次（把此前隐式的默认值
// `scriptingBackend: {}` 写成显式的 `scriptingBackend: {Standalone: N}`）。因此 finally 里的
// `PlayerSettings.SetScriptingBackend(namedTarget, originalBackend)` 不是"防御性写法"，而是
// 保证磁盘上最终落地的值仍然等于构建前原始值的必需步骤——本方法执行完毕后 ProjectSettings.asset
// 里 Standalone 对应的脚本后端字段值与构建前一致（只是表示形式可能从"键不存在（取隐式默认值）"
// 变成"键存在且值等于默认值"，语义不变，Unity 后续任何读取路径两者等价），没有真正"永久切换到
// IL2CPP"；但如果不还原，构建后磁盘上会真的变成 IL2CPP，是文档硬性规则明确禁止的"永久切换"。
//
// 命令行调用（见 check.ps1 -Il2cpp 步骤）：
//   Unity.exe -batchmode -nographics -quit -projectPath <repo>\adapters\unity
//     -executeMethod Adapter.Unity.EditorTools.Il2CppPlayerBuilder.BuildWindows64PlayerIl2cpp
//     -gfOutputPath <绝对路径>\Shell.exe
// 输出路径解析优先级：命令行参数 `-gfOutputPath <path>` > 环境变量 GF_IL2CPP_OUTPUT_PATH；
// 两者都没有时报错退出（不猜测默认路径，避免误覆盖仓库内任何文件）。
//
// 判断记录（不传 -quit 单独跑行不行）：本方法自己在 finally 之后调用 EditorApplication.Exit，
// 无论传不传 -quit 都会在方法执行完毕后立即退出进程，因此与其它 -executeMethod 脚本（如
// ShellSceneBuilder，靠外层 -quit 结束进程）不同，这里 -quit 是可选的（check.ps1 仍然传，保持
// 与其它 Unity 批处理调用统一的命令行形状，双重保险）。
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace Adapter.Unity.EditorTools
{
    public static class Il2CppPlayerBuilder
    {
        public static void BuildWindows64PlayerIl2cpp()
        {
            var outputPath = ResolveOutputPath();
            var namedTarget = NamedBuildTarget.Standalone;
            var originalBackend = PlayerSettings.GetScriptingBackend(namedTarget);

            var success = false;
            UnityEditor.Build.Reporting.BuildReport? report = null;
            try
            {
                PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.IL2CPP);

                var scenes = EditorBuildSettings.scenes
                    .Where(s => s.enabled)
                    .Select(s => s.path)
                    .ToArray();

                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                });

                success = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            }
            finally
            {
                // 判断记录见类型顶部注释：本还原不调用 AssetDatabase.SaveAssets，只影响进程内存，
                // 磁盘上的 ProjectSettings.asset 全程未被触碰。
                PlayerSettings.SetScriptingBackend(namedTarget, originalBackend);
            }

            if (report != null)
            {
                UnityEngine.Debug.Log(
                    $"[Il2CppPlayerBuilder] result={report.summary.result} " +
                    $"totalErrors={report.summary.totalErrors} " +
                    $"totalWarnings={report.summary.totalWarnings} " +
                    $"totalSize={report.summary.totalSize} " +
                    $"totalTime={report.summary.totalTime}");
            }

            EditorApplication.Exit(success ? 0 : 1);
        }

        private static string ResolveOutputPath()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-gfOutputPath")
                {
                    return args[i + 1];
                }
            }

            var envPath = Environment.GetEnvironmentVariable("GF_IL2CPP_OUTPUT_PATH");
            if (!string.IsNullOrEmpty(envPath))
            {
                return envPath;
            }

            throw new InvalidOperationException(
                "Il2CppPlayerBuilder.BuildWindows64PlayerIl2cpp 需要通过 -gfOutputPath <path> " +
                "命令行参数或 GF_IL2CPP_OUTPUT_PATH 环境变量指定构建产物输出路径。");
        }
    }
}
