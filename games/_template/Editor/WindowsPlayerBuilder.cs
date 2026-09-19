#nullable enable
// WindowsPlayerBuilder：批处理独立版构建入口，补齐消费方反馈第 72 条缺口——内置命令行开关
// `-buildWindows64Player <path>`（toolchain/consumer_smoke.ps1 第 9 步既有用法）按 ProjectSettings
// .asset 当前保存的设置构建，本身不接受"这次临时勾选 Development Build"的参数，任何批处理调用方
// 都无法在不永久修改工程设置的前提下产出一份 Development 版独立版可执行文件（BuildOptions
// .Development：开发者控制台、profiler 联机、脚本调试符号等）。
//
// 判断记录（为什么放在 games/_template/Editor 而不是 adapters/unity/Assets/Editor，同
// GameSceneBuilder.cs 顶部判断记录）：`toolchain/consumer_smoke.ps1` 构建的消费方工程是"从分发包
// file: 引用起步的全新工程"，只会把 `games/_template` 整份复制进去（见该脚本"复制并改名
// games/_template -> com.sample.game-consumer"步骤），`adapters/unity/Assets/Editor/` 是工作台
// 自己的 Assets/Editor（不在任何 asmdef 包/被分发的目录里），消费方工程里根本不存在——放在那里的
// `-executeMethod` 入口对消费方工程不可达。本类型因此与 `Il2CppPlayerBuilder.cs`
//（`adapters/unity/Assets/Editor/`，服务工作台自身 `check.ps1 -Il2cpp` 步骤）分处两个不同目录，
// 结构相似但不合并：两者分别服务"工作台自测"与"消费方演练"两条不共享 Unity 工程的流水线。
//
// 判断记录（与 Il2CppPlayerBuilder.cs 同款结构，不需要它的 PlayerSettings 还原逻辑）：本类型不涉及
// 临时切脚本后端，BuildOptions 只是本次调用的传参，不写入 ProjectSettings.asset，构建完成后不留
// 任何痕迹，没有"临时切换、用后必须还原"的 try/finally 需求；仍然保留同一套 -executeMethod +
// -gfOutputPath 输出路径解析惯例（同一命令行形状），避免在两个几乎并列的构建入口之间引入不一致的
// 参数命名风格。
//
// 命令行调用（见 toolchain/consumer_smoke.ps1 `-DevelopmentBuild` 参数、games/_template/README.md
// "构建独立版"一节）：
//   Unity.exe -batchmode -nographics -quit -projectPath <消费方工程>
//     -executeMethod Game.Template.EditorTools.WindowsPlayerBuilder.BuildWindows64Player
//     -gfOutputPath <绝对路径>\ConsumerShell.exe
//     [-gfDevelopmentBuild]
// 输出路径解析优先级：命令行参数 `-gfOutputPath <path>` > 环境变量 GF_OUTPUT_PATH；两者都没有时
// 报错退出（不猜测默认路径，避免误覆盖仓库内任何文件，同 Il2CppPlayerBuilder.ResolveOutputPath
// 判断记录）。`-gfDevelopmentBuild` 是纯存在性开关（不带值），不传时按 BuildOptions.None 构建
// ——与本类型新增之前直接调用内置 `-buildWindows64Player` 开关的默认行为完全一致；调用方要保持
// "默认构建不是 Development"这一既有约定，只需要不传这个标志，不需要额外做任何事。
using System;
using System.Linq;
using UnityEditor;

namespace Game.Template.EditorTools
{
    public static class WindowsPlayerBuilder
    {
        private const string DevelopmentBuildFlag = "-gfDevelopmentBuild";

        public static void BuildWindows64Player()
        {
            var outputPath = ResolveOutputPath();
            var development = ResolveDevelopmentBuildFlag();

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = development ? BuildOptions.Development : BuildOptions.None,
            });

            var success = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;

            UnityEngine.Debug.Log(
                $"[WindowsPlayerBuilder] development={development} " +
                $"result={report.summary.result} " +
                $"totalErrors={report.summary.totalErrors} " +
                $"totalWarnings={report.summary.totalWarnings} " +
                $"totalSize={report.summary.totalSize} " +
                $"totalTime={report.summary.totalTime}");

            EditorApplication.Exit(success ? 0 : 1);
        }

        /// <summary>纯存在性检查：命令行 token 里出现 <see cref="DevelopmentBuildFlag"/> 即视为
        /// 请求 Development Build，不需要跟一个值——与 <c>-gf-smoke</c>/<c>-gf-smoke-template</c>
        /// 等运行期布尔标志同款判定手法（<c>Environment.GetCommandLineArgs().Contains(...)</c>，见
        /// <c>Runtime/TemplateSmokeRunner.cs</c>/<c>Adapter.Unity.Shell.SmokeRunner</c>）。不传时
        /// 返回 false，<see cref="BuildWindows64Player"/> 按 <c>BuildOptions.None</c> 构建，与本类型
        /// 新增之前的默认行为完全一致。</summary>
        private static bool ResolveDevelopmentBuildFlag() =>
            Environment.GetCommandLineArgs().Contains(DevelopmentBuildFlag, StringComparer.Ordinal);

        /// <summary>见类型头判断记录：命令行参数 `-gfOutputPath <path>` 优先于环境变量
        /// GF_OUTPUT_PATH；两者都没有时报错退出，不猜测默认路径。结构同
        /// <c>Adapter.Unity.EditorTools.Il2CppPlayerBuilder.ResolveOutputPath</c>（仅环境变量名不同
        /// ——本类型不是 IL2CPP 专属，env var 名不带 IL2CPP 前缀）。</summary>
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

            var envPath = Environment.GetEnvironmentVariable("GF_OUTPUT_PATH");
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
