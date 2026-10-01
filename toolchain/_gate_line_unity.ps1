<#
门禁"Unity 串行链"子脚本（工程收尾 gate-speed 任务新增，2026-09-22）：收拢 DLL 同步
（build.ps1 -SkipTests）、包清单一致性、Unity 编译检查/EditMode/PlayMode/独立版构建+两种冒烟、
IL2CPP 三步（-Il2cpp 显式开启才跑）、消费方演练——由 `check.ps1` 用 `Start-Job -FilePath` 启动为
独立子进程，与 `toolchain/_gate_line_heavy.ps1`（非 Unity 重步骤线）并行跑。

判断记录（DLL 同步/包清单一致性为什么放在本线而不是"非 Unity 重步骤线"，尽管它们本身不调用
Unity）：DLL 同步（build.ps1 -SkipTests）是 Unity 编译检查等四步的硬性前置条件——Unity 工程
实际加载的适配层 DLL 是 `Runtime/Plugins/Core/*.dll` 这份构建产物副本，不重新构建+同步，Unity
侧验证的就是陈旧代码（见 AGENTS.md 第 4 节"验证任何 Unity 侧行为前，必须先确认 DLL 与源码
同步"）。包清单一致性依赖 DLL 同步之后落在 `bin\$Configuration\netstandard2.1\` 下的产物
（`build.ps1 -SyncOnly -Dist auto` 要求这些 DLL 已存在，见该步骤判断记录）。两者与"非 Unity 重
步骤线"没有依赖关系，但与本线的 Unity 四步共享同一次 `dotnet build` 产物，放在同一个子进程里
顺序执行最省心，也符合任务书"同步 DLL 必须在 Unity 步骤之前"这条已知真实依赖。

本线内部保持串行（不额外起第二个 Unity 进程）：Unity 编译检查/EditMode/PlayMode 三步要求
"同一工程不能有残留 Unity.exe"（见 Test-NoResidualUnityProcess），独立版构建/两种冒烟/消费方
演练同样只应有一个 Unity 相关进程在跑，串行是唯一安全的编排方式。
#>
param(
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][string]$ArtifactsPath,
    [Parameter(Mandatory = $true)][string]$UnityOutDir,
    [string]$Configuration = "Release",
    [switch]$Quick,
    [switch]$DocsOnly,
    [switch]$FailFast,
    [switch]$SkipUnity,
    [switch]$SkipSmoke,
    [switch]$SkipConsumer,
    [switch]$Il2cpp,
    [string]$UnityExe = "",
    [string]$FailFastFlagPath = "",
    # 定向门禁（ADR-0126）：判定 JSON，语义同 _gate_line_heavy.ps1 同名参数；空串 = 非定向模式。
    [string]$PlanFile = "",
    [Parameter(Mandatory = $true)][string]$ResultsJsonPath,
    [string]$TranscriptPath = "",
    # 仅供并行编排的自证测试使用，见 _gate_line_heavy.ps1 同名参数判断记录；默认 0 不生效。
    [int]$InjectMockSleepSeconds = 0
)

$ErrorActionPreference = "Stop"

if ($TranscriptPath -ne "") {
    try { Start-Transcript -Path $TranscriptPath -Force | Out-Null } catch {}
}

$script:Results = New-Object System.Collections.Generic.List[Object]
$script:GateFailed = $false
$script:FailFastFlagPath = if ($FailFastFlagPath -ne "") { $FailFastFlagPath } else { $null }

. (Join-Path $RepoRoot "toolchain\_gate_step_runner.ps1")
. (Join-Path $RepoRoot "toolchain\_gate_test_floors.ps1")
. (Join-Path $RepoRoot "toolchain\_unity_path_length_guard.ps1")
# 第四批（复盘 I-8 余项）：结果 XML / 冒烟日志 / 包清单三处判定逻辑抽成纯函数，见该文件头。
. (Join-Path $RepoRoot "toolchain\_gate_unity_verdicts.ps1")
Import-GatePlan -Path $PlanFile

try {
    # 判断记录同 toolchain/_gate_line_heavy.ps1 同名段落：故意不经过 Invoke-CheckStep，不受
    # -DocsOnly/-FailFast/-Quick/-SkipUnity 任何一个开关短路，默认 0 时是空操作。
    if ($InjectMockSleepSeconds -gt 0) {
        Write-Host ""
        Write-Host "==== （并行编排自证）Unity 线模拟慢步骤 ====" -ForegroundColor Cyan
        Start-Sleep -Seconds $InjectMockSleepSeconds
        Write-Host "[（并行编排自证）Unity 线模拟慢步骤] Start-Sleep $InjectMockSleepSeconds s 完成" -ForegroundColor Green
    }

    # -------------------------------------------------------------------
    # 9. build.ps1 -SkipTests（同步六个核心 DLL 到 Unity 适配层包 + 同步内容数据集）——判断记录
    #    （为什么不受 -SkipUnity 门控、只受 -Quick 门控）与原 check.ps1 完全一致，原样搬入：
    #    -Quick 本身不跑任何 Unity 步骤，跳过本步不影响 -Quick 覆盖的 dotnet/python 校验结论；
    #    但单独传 -SkipUnity（不传 -Quick）时，本步骤仍然会跑——这不是本次任务改动引入的新行为，
    #    是迁移前 check.ps1 的既有语义，本次只搬运位置、不改判定条件。
    # -------------------------------------------------------------------
    if ($Quick) {
        Add-SkippedStep "build.ps1 -SkipTests（同步 DLL）" "-Quick"
    } else {
        Invoke-CheckStep "build.ps1 -SkipTests（同步 DLL）" -Id "sync_dll" {
            $buildScript = Join-Path $RepoRoot "build.ps1"
            & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript -SkipTests | Out-Null
            return ($LASTEXITCODE -eq 0)
        }
    }

    # -------------------------------------------------------------------
    # 9.5 包清单一致性（四个 npm 包版本号 + npm pack --dry-run 排除规则 + 关键资产/预编译产物
    #     存在性）——判断记录同原 check.ps1，原样搬入。
    # -------------------------------------------------------------------
    if ($Quick) {
        Add-SkippedStep "包清单一致性（四个 npm 包版本号 + npm pack --dry-run 排除规则）" "-Quick"
    } else {
        Invoke-CheckStep "包清单一致性（四个 npm 包版本号 + npm pack --dry-run 排除规则）" -Id "pkg_manifest" {
            $versionPath = Join-Path $RepoRoot "VERSION"
            $version = (Get-Content -Path $versionPath -Raw).Trim()

            # 判断记录（为什么打到 dist\<VERSION>-dryrun\ 而不是 -Dist auto 的 dist\<VERSION>\）：
            # VERSION 文件在一次发布落地后就等于那个已发布版本号（发布提交把它写成 X.Y.Z 并打上
            # 标签 vX.Y.Z），而本步骤会 Remove-Item 整个 dist\<版本>\ 再重新打一遍包——那正是
            # toolchain/_dist_immutability_guard.ps1 承诺"已发布版本产物不可变"要拦的事，门禁因此
            # 会在每次发布之后、下一次发布把 VERSION 顶上去之前一直失败（2026-09-23 实测命中：
            # "拒绝覆盖已发布版本 1.62.0 的产物"）。改用带 "-dryrun" 后缀的版本号：该后缀是合法的
            # semver 预发布标识、build.ps1 明确支持（见其 $DistVersionDryRunPattern 一节），且
            # git tag -l "v<带后缀>" 天然查不到匹配，正是不可变守卫文件头判断记录里预留给这类
            # "只想验证打包逻辑、不是真发布"调用点的放行方式。本步骤校验的是"四个包版本号彼此一致
            # 且等于本次请求打包的版本号"，用后缀版本号同样成立。
            $distVersion = "$version-dryrun"

            $buildScript = Join-Path $RepoRoot "build.ps1"
            $ErrorActionPreference = "Continue"
            $buildOutputLines = New-Object System.Collections.Generic.List[string]
            & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript -SyncOnly -Dist $distVersion 2>&1 | ForEach-Object {
                $line = $_.ToString()
                Write-Host $line
                $buildOutputLines.Add($line)
            }
            if ($LASTEXITCODE -ne 0) {
                $tailLines = $buildOutputLines | Select-Object -Last 30
                throw ("build.ps1 -SyncOnly -Dist $distVersion 失败，退出码 $LASTEXITCODE。最后 " + $tailLines.Count + " 行输出：`n" + ($tailLines -join "`n"))
            }

            $packagesRoot = Join-Path $RepoRoot ("dist\" + $distVersion + "\packages")
            $packageNames = @(
                "com.gamefoundation.adapter.unity",
                "com.gamefoundation.framework-data",
                "com.gamefoundation.toolchain",
                "com.gamefoundation.adapter.headless"
            )
            $problems = @()
            foreach ($pkgName in $packageNames) {
                $pkgDir = Join-Path $packagesRoot $pkgName
                $pkgJsonPath = Join-Path $pkgDir "package.json"
                if (-not (Test-Path $pkgJsonPath)) {
                    $problems += "$pkgName：找不到 $pkgJsonPath"
                    continue
                }
                $pkgObj = (Get-Content -Path $pkgJsonPath -Raw -Encoding UTF8) | ConvertFrom-Json
                if ($pkgObj.version -ne $distVersion) {
                    $problems += "$pkgName：package.json version=$($pkgObj.version) != 本次打包版本=$distVersion（VERSION=$version）"
                }

                $dryRunJson = & npm pack $pkgDir --dry-run --json 2>$null
                if ($LASTEXITCODE -ne 0) {
                    $problems += "$pkgName：npm pack --dry-run 失败（退出码 $LASTEXITCODE）"
                    continue
                }
                $dryRunObj = ($dryRunJson -join "`n") | ConvertFrom-Json
                $fileEntries = $dryRunObj[0].files
                # 判断记录（第四批，复盘 I-8 余项）：排除名单与各包必需文件清单的判定抽成纯函数
                # Get-PackageManifestProblems（toolchain/_gate_unity_verdicts.ps1，文案与判定条件和
                # 抽取前逐字一致），由 toolchain/tests/test_gate_unity_verdicts.py 用伪造清单直接验证。
                $entryPathList = @($fileEntries | ForEach-Object { [string]$_.path })
                $problems += @(Get-PackageManifestProblems -PackageName $pkgName -EntryPaths $entryPathList)
            }

            if ($problems.Count -gt 0) {
                throw ("包清单一致性校验失败：`n  " + ($problems -join "`n  "))
            }
            [PSCustomObject]@{ Ok = $true; Detail = "四个包 version=$version 一致，npm pack --dry-run 清单均不含排除项，adapter.unity 包含 model/anim 占位资产与生成器，toolchain 包含预编译 validator+simrunner，adapter.headless 包含 Core.Sim.dll" }
        }
    }

    # -------------------------------------------------------------------
    # Unity 相关四步 + IL2CPP 三步 + 消费方演练（-SkipUnity 时整体跳过）
    # -------------------------------------------------------------------
    $GateFloorsPath = Join-Path $RepoRoot "toolchain\gate_floors.json"
    # 定向门禁（ADR-0126）：引擎侧待跑的 PlayMode 分类过滤串（T1 仅交互例外、T2 为命中模块分类 + shared
    # + 交互例外；T3 或非定向模式为空串 = 不过滤、跑全部）。分号分隔，直接作为 Unity 的 -testCategory 值。
    $playModeCategoryFilter = ""
    if ($script:GatePlan -and [string]$script:GatePlan.engine.mode -eq "filtered") {
        $playModeCategoryFilter = [string]$script:GatePlan.engine.playmode_filter
    }
    if ($SkipUnity) {
        Add-SkippedStep "Unity 编译检查" "-SkipUnity"
        Add-SkippedStep "Unity EditMode 测试" "-SkipUnity"
        if ($playModeCategoryFilter -ne "") {
            Add-SkippedStep "Unity PlayMode 测试" "-SkipUnity（引擎侧待跑，由主会话执行：-testCategory '$playModeCategoryFilter'）"
        } else {
            Add-SkippedStep "Unity PlayMode 测试" "-SkipUnity"
        }
        Add-SkippedStep "独立版构建 + -gf-smoke 冒烟（连续模式默认流程）" "-SkipUnity"
        Add-SkippedStep "独立版 -gf-smoke-discrete 冒烟（离散模式链路）" "-SkipUnity"
        Add-SkippedStep "消费方演练" "-SkipUnity"
    } else {
        Test-UnityWorkingTreePathLength -RepoRoot $RepoRoot

        $resolvedUnityExe = Resolve-UnityExe -Explicit $UnityExe
        $unityProjectPath = Join-Path $RepoRoot "adapters\unity"

        Invoke-CheckStep "Unity 编译检查" -Id "unity_compile" {
            Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
            $log = Join-Path $UnityOutDir "compile.log"
            $proc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
                "-batchmode", "-nographics", "-quit",
                "-projectPath", $unityProjectPath,
                "-logFile", $log
            )
            [PSCustomObject]@{
                Ok     = ($proc.ExitCode -eq 0)
                Detail = "Unity 退出码 $($proc.ExitCode)"
            }
        }

        Invoke-CheckStep "Unity EditMode 测试" -Id "unity_editmode" {
            Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
            $resultsXml = Join-Path $UnityOutDir "editmode.xml"
            $log = Join-Path $UnityOutDir "editmode.log"
            $proc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
                "-batchmode", "-nographics",
                "-projectPath", $unityProjectPath,
                "-runTests", "-testPlatform", "EditMode",
                "-testResults", $resultsXml,
                "-logFile", $log
            )
            if ($proc.ExitCode -ne 0) {
                return [PSCustomObject]@{ Ok = $false; Detail = "Unity 退出码 $($proc.ExitCode)" }
            }
            # 结果 XML 判定抽成 Get-UnityTestRunVerdict（toolchain/_gate_unity_verdicts.ps1，第四批 I-8 余项）。
            $verdict = Get-UnityTestRunVerdict -ResultsXml $resultsXml
            if (-not $verdict.Exists) {
                return [PSCustomObject]@{ Ok = $false; Detail = "未生成结果 XML：$resultsXml" }
            }
            $editModeOk = $verdict.Ok
            if (-not $editModeOk) {
                Invoke-UnityTestTriageOnFailure -ResultsXml $resultsXml -LogPath $log
            }
            # 用例数下限（复盘 I-2）：result==Passed 之外，还要 passed 不低于 gate_floors.json 的
            # unity_editmode.min_passed、skipped+inconclusive 不超过 max_skipped；四个数写进 Detail。
            $floorResult = Invoke-GateTestFloorCheck -Kind NUnit -Path $resultsXml -Suite "unity_editmode" -FloorsPath $GateFloorsPath
            if (-not $floorResult.Ok) {
                Write-Host "用例数下限未达：$($floorResult.Detail)" -ForegroundColor Red
            }
            [PSCustomObject]@{
                Ok     = ($editModeOk -and $floorResult.Ok)
                Detail = "failed=$($verdict.Failed) result=$($verdict.Result) " + $floorResult.Detail
            }
        }

        $playModeStepName = "Unity PlayMode 测试"
        if ($playModeCategoryFilter -ne "") {
            $playModeStepName = "Unity PlayMode 测试（定向：-testCategory $playModeCategoryFilter）"
        }
        Invoke-CheckStep $playModeStepName -Id "unity_playmode" {
            Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
            $resultsXml = Join-Path $UnityOutDir "playmode.xml"
            $log = Join-Path $UnityOutDir "playmode.log"
            $playModeArgs = @(
                "-batchmode",
                "-projectPath", $unityProjectPath,
                "-runTests", "-testPlatform", "PlayMode",
                "-testResults", $resultsXml,
                "-logFile", $log
            )
            # 定向门禁（ADR-0126）：-testCategory 取分号分隔的 NUnit 分类名（模块分类 module:<名>、
            # module:shared、交互例外分类）。与 -testFilter 是"取交集"语义，所以交互例外也登记成分类、
            # 不另传 -testFilter。
            if ($playModeCategoryFilter -ne "") {
                $playModeArgs += @("-testCategory", $playModeCategoryFilter)
            }
            $proc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList $playModeArgs
            if ($proc.ExitCode -ne 0) {
                return [PSCustomObject]@{ Ok = $false; Detail = "Unity 退出码 $($proc.ExitCode)" }
            }
            # 结果 XML 判定抽成 Get-UnityTestRunVerdict（toolchain/_gate_unity_verdicts.ps1，第四批 I-8 余项）。
            $verdict = Get-UnityTestRunVerdict -ResultsXml $resultsXml
            if (-not $verdict.Exists) {
                return [PSCustomObject]@{ Ok = $false; Detail = "未生成结果 XML：$resultsXml" }
            }
            $playModeOk = $verdict.Ok
            if (-not $playModeOk) {
                Invoke-UnityTestTriageOnFailure -ResultsXml $resultsXml -LogPath $log
            }
            # 用例数下限（复盘 I-2）：result==Passed 之外，还要 passed 不低于 gate_floors.json 的
            # unity_playmode.min_passed、skipped+inconclusive 不超过 max_skipped；四个数写进 Detail。
            # 按分类过滤的定向运行只跑了子集，全套件下限不适用，改用子集底线（passed>=1 等）。
            if ($playModeCategoryFilter -ne "") {
                $floorResult = Invoke-GateTargetedCountsCheck -Kind NUnit -Path $resultsXml -Suite "unity_playmode" -FloorsPath $GateFloorsPath
            } else {
                $floorResult = Invoke-GateTestFloorCheck -Kind NUnit -Path $resultsXml -Suite "unity_playmode" -FloorsPath $GateFloorsPath
            }
            if (-not $floorResult.Ok) {
                Write-Host "用例数下限未达：$($floorResult.Detail)" -ForegroundColor Red
            }
            [PSCustomObject]@{
                Ok     = ($playModeOk -and $floorResult.Ok)
                Detail = "failed=$($verdict.Failed) result=$($verdict.Result) " + $floorResult.Detail
            }
        }

        Invoke-CheckStep "独立版构建 + -gf-smoke 冒烟（连续模式默认流程）" -Id "unity_build_smoke" {
            Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
            $buildLog = Join-Path $UnityOutDir "build.log"
            $exePath = Join-Path $UnityOutDir "Shell.exe"
            $buildProc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
                "-batchmode", "-nographics", "-quit",
                "-projectPath", $unityProjectPath,
                "-buildWindows64Player", $exePath,
                "-logFile", $buildLog
            )
            if ($buildProc.ExitCode -ne 0) {
                return [PSCustomObject]@{ Ok = $false; Detail = "Unity 构建退出码 $($buildProc.ExitCode)" }
            }
            if (-not (Test-Path $exePath)) {
                return [PSCustomObject]@{ Ok = $false; Detail = "未生成独立版产物：$exePath" }
            }

            if ($SkipSmoke) {
                Write-Host "已跳过 -gf-smoke 冒烟子步骤（-SkipSmoke），只验证了构建产物存在。" -ForegroundColor Yellow
                return [PSCustomObject]@{ Ok = $true; Detail = "已跳过 -gf-smoke（-SkipSmoke），只验证构建产物存在" }
            }

            $smokeLog = Join-Path $UnityOutDir "smoke_player.log"
            $smokeProc = Invoke-NativeAndWait -Exe $exePath -TimeoutSeconds 180 -ArgList @(
                "-batchmode", "-gf-smoke",
                "-logFile", $smokeLog,
                "-screen-width", "800", "-screen-height", "600"
            )
            if ($smokeProc.TimedOut) {
                return [PSCustomObject]@{ Ok = $false; Detail = "-gf-smoke 冒烟超过 180s 未退出，已强制结束（可能挂死）" }
            }
            if ($smokeProc.ExitCode -ne 0) {
                return [PSCustomObject]@{ Ok = $false; Detail = "独立版退出码 $($smokeProc.ExitCode)" }
            }
            if (-not (Test-Path $smokeLog)) {
                return [PSCustomObject]@{ Ok = $false; Detail = "未生成冒烟日志：$smokeLog" }
            }
            $logText = Get-Content -Path $smokeLog -Raw
            [PSCustomObject]@{
                Ok     = (Test-SmokeLogText -LogText $logText)
                Detail = "见 $smokeLog"
            }
        }

        Invoke-CheckStep "独立版 -gf-smoke-discrete 冒烟（离散模式链路）" -Id "unity_smoke_discrete" {
            $exePath = Join-Path $UnityOutDir "Shell.exe"
            if (-not (Test-Path $exePath)) {
                return [PSCustomObject]@{ Ok = $false; Detail = "未生成独立版产物：$exePath" }
            }

            if ($SkipSmoke) {
                Write-Host "已跳过 -gf-smoke-discrete 冒烟子步骤（-SkipSmoke）。" -ForegroundColor Yellow
                return [PSCustomObject]@{ Ok = $true; Detail = "已跳过 -gf-smoke-discrete（-SkipSmoke）" }
            }

            $smokeLog = Join-Path $UnityOutDir "smoke_player_discrete.log"
            $smokeProc = Invoke-NativeAndWait -Exe $exePath -TimeoutSeconds 180 -ArgList @(
                "-batchmode", "-gf-smoke-discrete",
                "-logFile", $smokeLog,
                "-screen-width", "800", "-screen-height", "600"
            )
            if ($smokeProc.TimedOut) {
                return [PSCustomObject]@{ Ok = $false; Detail = "-gf-smoke-discrete 冒烟超过 180s 未退出，已强制结束（可能挂死）" }
            }
            if ($smokeProc.ExitCode -ne 0) {
                return [PSCustomObject]@{ Ok = $false; Detail = "独立版退出码 $($smokeProc.ExitCode)" }
            }
            if (-not (Test-Path $smokeLog)) {
                return [PSCustomObject]@{ Ok = $false; Detail = "未生成冒烟日志：$smokeLog" }
            }
            $logText = Get-Content -Path $smokeLog -Raw
            [PSCustomObject]@{
                Ok     = (Test-SmokeLogText -LogText $logText -Discrete)
                Detail = "见 $smokeLog"
            }
        }

        if (-not $Il2cpp) {
            Add-SkippedStep "IL2CPP 独立版构建" "未传 -Il2cpp"
            Add-SkippedStep "IL2CPP 独立版 -gf-smoke 冒烟" "未传 -Il2cpp"
            Add-SkippedStep "IL2CPP 独立版 -gf-smoke-discrete 冒烟" "未传 -Il2cpp"
        } else {
            $il2cppOutDir = Join-Path $UnityOutDir "il2cpp"
            if (-not (Test-Path $il2cppOutDir)) {
                New-Item -ItemType Directory -Force -Path $il2cppOutDir | Out-Null
            }
            $il2cppExePath = Join-Path $il2cppOutDir "Shell_il2cpp.exe"

            Invoke-CheckStep "IL2CPP 独立版构建" -Id "il2cpp_build" {
                Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
                $buildLog = Join-Path $UnityOutDir "build_il2cpp.log"
                $prevEnv = $env:GF_IL2CPP_OUTPUT_PATH
                $env:GF_IL2CPP_OUTPUT_PATH = $il2cppExePath
                try {
                    $buildProc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
                        "-batchmode", "-nographics", "-quit",
                        "-projectPath", $unityProjectPath,
                        "-executeMethod", "Adapter.Unity.EditorTools.Il2CppPlayerBuilder.BuildWindows64PlayerIl2cpp",
                        "-logFile", $buildLog
                    )
                } finally {
                    $env:GF_IL2CPP_OUTPUT_PATH = $prevEnv
                }
                if ($buildProc.ExitCode -ne 0) {
                    return [PSCustomObject]@{ Ok = $false; Detail = "Unity 构建退出码 $($buildProc.ExitCode)，见 $buildLog" }
                }
                if (-not (Test-Path $il2cppExePath)) {
                    return [PSCustomObject]@{ Ok = $false; Detail = "未生成 IL2CPP 独立版产物：$il2cppExePath" }
                }
                [PSCustomObject]@{ Ok = $true; Detail = "见 $buildLog" }
            }

            Invoke-CheckStep "IL2CPP 独立版 -gf-smoke 冒烟" -Id "il2cpp_smoke" {
                if (-not (Test-Path $il2cppExePath)) {
                    return [PSCustomObject]@{ Ok = $false; Detail = "未生成 IL2CPP 独立版产物：$il2cppExePath" }
                }
                if ($SkipSmoke) {
                    return [PSCustomObject]@{ Ok = $true; Detail = "已跳过（-SkipSmoke），只验证构建产物存在" }
                }
                $smokeLog = Join-Path $UnityOutDir "smoke_player_il2cpp.log"
                $smokeProc = Invoke-NativeAndWait -Exe $il2cppExePath -TimeoutSeconds 180 -ArgList @(
                    "-batchmode", "-gf-smoke",
                    "-logFile", $smokeLog,
                    "-screen-width", "800", "-screen-height", "600"
                )
                if ($smokeProc.TimedOut) {
                    return [PSCustomObject]@{ Ok = $false; Detail = "-gf-smoke 冒烟超过 180s 未退出，已强制结束（可能挂死）" }
                }
                if ($smokeProc.ExitCode -ne 0) {
                    return [PSCustomObject]@{ Ok = $false; Detail = "独立版退出码 $($smokeProc.ExitCode)" }
                }
                if (-not (Test-Path $smokeLog)) {
                    return [PSCustomObject]@{ Ok = $false; Detail = "未生成冒烟日志：$smokeLog" }
                }
                $logText = Get-Content -Path $smokeLog -Raw
                [PSCustomObject]@{
                    Ok     = (Test-SmokeLogText -LogText $logText)
                    Detail = "见 $smokeLog"
                }
            }

            Invoke-CheckStep "IL2CPP 独立版 -gf-smoke-discrete 冒烟" -Id "il2cpp_smoke_discrete" {
                if (-not (Test-Path $il2cppExePath)) {
                    return [PSCustomObject]@{ Ok = $false; Detail = "未生成 IL2CPP 独立版产物：$il2cppExePath" }
                }
                if ($SkipSmoke) {
                    return [PSCustomObject]@{ Ok = $true; Detail = "已跳过（-SkipSmoke）" }
                }
                $smokeLog = Join-Path $UnityOutDir "smoke_player_il2cpp_discrete.log"
                $smokeProc = Invoke-NativeAndWait -Exe $il2cppExePath -TimeoutSeconds 180 -ArgList @(
                    "-batchmode", "-gf-smoke-discrete",
                    "-logFile", $smokeLog,
                    "-screen-width", "800", "-screen-height", "600"
                )
                if ($smokeProc.TimedOut) {
                    return [PSCustomObject]@{ Ok = $false; Detail = "-gf-smoke-discrete 冒烟超过 180s 未退出，已强制结束（可能挂死）" }
                }
                if ($smokeProc.ExitCode -ne 0) {
                    return [PSCustomObject]@{ Ok = $false; Detail = "独立版退出码 $($smokeProc.ExitCode)" }
                }
                if (-not (Test-Path $smokeLog)) {
                    return [PSCustomObject]@{ Ok = $false; Detail = "未生成冒烟日志：$smokeLog" }
                }
                $logText = Get-Content -Path $smokeLog -Raw
                [PSCustomObject]@{
                    Ok     = (Test-SmokeLogText -LogText $logText -Discrete)
                    Detail = "见 $smokeLog"
                }
            }
        }

        if ($SkipConsumer) {
            Add-SkippedStep "消费方演练" "-SkipConsumer"
        } else {
            Invoke-CheckStep "消费方演练（toolchain/consumer_smoke.ps1）" -Id "consumer_drill" {
                $consumerScript = Join-Path $RepoRoot "toolchain\consumer_smoke.ps1"
                $consumerArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $consumerScript)
                if ($UnityExe -ne "") {
                    $consumerArgs += @("-UnityExe", $resolvedUnityExe)
                }
                & powershell @consumerArgs | Out-Null
                return ($LASTEXITCODE -eq 0)
            }
        }
    }
} catch {
    $script:Results.Add([PSCustomObject]@{
        Step    = "Unity 串行线：子进程异常"
        Result  = "FAIL"
        Seconds = 0
        Detail  = $_.Exception.Message
    })
} finally {
    Write-GateResultsJson -Path $ResultsJsonPath
    if ($TranscriptPath -ne "") {
        try { Stop-Transcript | Out-Null } catch {}
    }
}
