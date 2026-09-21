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
. (Join-Path $RepoRoot "toolchain\_unity_path_length_guard.ps1")

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
        Invoke-CheckStep "build.ps1 -SkipTests（同步 DLL）" {
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
        Invoke-CheckStep "包清单一致性（四个 npm 包版本号 + npm pack --dry-run 排除规则）" {
            $versionPath = Join-Path $RepoRoot "VERSION"
            $version = (Get-Content -Path $versionPath -Raw).Trim()

            $buildScript = Join-Path $RepoRoot "build.ps1"
            $ErrorActionPreference = "Continue"
            $buildOutputLines = New-Object System.Collections.Generic.List[string]
            & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript -SyncOnly -Dist auto 2>&1 | ForEach-Object {
                $line = $_.ToString()
                Write-Host $line
                $buildOutputLines.Add($line)
            }
            if ($LASTEXITCODE -ne 0) {
                $tailLines = $buildOutputLines | Select-Object -Last 30
                throw ("build.ps1 -SyncOnly -Dist auto 失败，退出码 $LASTEXITCODE。最后 " + $tailLines.Count + " 行输出：`n" + ($tailLines -join "`n"))
            }

            $packagesRoot = Join-Path $RepoRoot ("dist\" + $version + "\packages")
            $packageNames = @(
                "com.gamefoundation.adapter.unity",
                "com.gamefoundation.framework-data",
                "com.gamefoundation.toolchain",
                "com.gamefoundation.adapter.headless"
            )
            $forbiddenSegments = @("__pycache__", "bin", "obj", "storage")

            $problems = @()
            foreach ($pkgName in $packageNames) {
                $pkgDir = Join-Path $packagesRoot $pkgName
                $pkgJsonPath = Join-Path $pkgDir "package.json"
                if (-not (Test-Path $pkgJsonPath)) {
                    $problems += "$pkgName：找不到 $pkgJsonPath"
                    continue
                }
                $pkgObj = (Get-Content -Path $pkgJsonPath -Raw -Encoding UTF8) | ConvertFrom-Json
                if ($pkgObj.version -ne $version) {
                    $problems += "$pkgName：package.json version=$($pkgObj.version) != VERSION=$version"
                }

                $dryRunJson = & npm pack $pkgDir --dry-run --json 2>$null
                if ($LASTEXITCODE -ne 0) {
                    $problems += "$pkgName：npm pack --dry-run 失败（退出码 $LASTEXITCODE）"
                    continue
                }
                $dryRunObj = ($dryRunJson -join "`n") | ConvertFrom-Json
                $fileEntries = $dryRunObj[0].files
                $hitSegments = New-Object System.Collections.Generic.HashSet[string]
                foreach ($entry in $fileEntries) {
                    $normalizedEntryPath = $entry.path -replace '\\', '/'
                    if ($normalizedEntryPath -match '(^|/)(validator|simrunner)/bin/') {
                        continue
                    }
                    $entryPathSegments = $entry.path -split '[\\/]'
                    foreach ($seg in $forbiddenSegments) {
                        if ($entryPathSegments -contains $seg) {
                            [void]$hitSegments.Add($seg)
                        }
                    }
                }
                if ($hitSegments.Count -gt 0) {
                    $problems += ("$pkgName：npm pack --dry-run 文件清单命中排除名单：" + (($hitSegments) -join ", "))
                }

                if ($pkgName -eq "com.gamefoundation.adapter.unity") {
                    $entryPaths = @($fileEntries | ForEach-Object { ($_.path -replace '\\', '/') })
                    $requiredModelAssetSuffixes = @(
                        "Runtime/Resources/GameFoundation/models/placeholder_biped.prefab",
                        "Runtime/Resources/GameFoundation/models/placeholder_biped.controller",
                        "Runtime/Resources/GameFoundation/anim_clips/idle.anim",
                        "Runtime/Resources/GameFoundation/anim_clips/attack.anim",
                        "Runtime/Resources/GameFoundation/anim_clips/cast.anim",
                        "Runtime/Resources/GameFoundation/anim_clips/hit.anim",
                        "Editor/GeneratePlaceholderModelAssets.cs"
                    )
                    $missingModelAssets = @()
                    foreach ($suffix in $requiredModelAssetSuffixes) {
                        $hit = @($entryPaths | Where-Object { $_ -like "*$suffix" })
                        if ($hit.Count -eq 0) {
                            $missingModelAssets += $suffix
                        }
                    }
                    if ($missingModelAssets.Count -gt 0) {
                        $problems += ("$pkgName：npm pack --dry-run 文件清单缺失 model/anim 占位资产或生成器（PJ130-02）：" + ($missingModelAssets -join ", "))
                    }
                }

                if ($pkgName -eq "com.gamefoundation.toolchain") {
                    $entryPaths = @($fileEntries | ForEach-Object { ($_.path -replace '\\', '/') })
                    $requiredValidatorArtifacts = @(
                        "Tools~/validator/bin/Validator.dll",
                        "Tools~/validator/Directory.Build.props"
                    )
                    $missingValidatorArtifacts = @()
                    foreach ($suffix in $requiredValidatorArtifacts) {
                        $hit = @($entryPaths | Where-Object { $_ -like "*$suffix" })
                        if ($hit.Count -eq 0) {
                            $missingValidatorArtifacts += $suffix
                        }
                    }
                    if ($missingValidatorArtifacts.Count -gt 0) {
                        $problems += ("$pkgName：npm pack --dry-run 文件清单缺失预编译 validator 或隔离用 Directory.Build.props（消费方反馈 E1）：" + ($missingValidatorArtifacts -join ", "))
                    }

                    $requiredSimRunnerArtifacts = @(
                        "Tools~/simrunner/bin/SimRunner.dll",
                        "Tools~/simrunner/Directory.Build.props"
                    )
                    $missingSimRunnerArtifacts = @()
                    foreach ($suffix in $requiredSimRunnerArtifacts) {
                        $hit = @($entryPaths | Where-Object { $_ -like "*$suffix" })
                        if ($hit.Count -eq 0) {
                            $missingSimRunnerArtifacts += $suffix
                        }
                    }
                    if ($missingSimRunnerArtifacts.Count -gt 0) {
                        $problems += ("$pkgName：npm pack --dry-run 文件清单缺失预编译 simrunner 或隔离用 Directory.Build.props（T-N6-7）：" + ($missingSimRunnerArtifacts -join ", "))
                    }
                }

                if ($pkgName -eq "com.gamefoundation.adapter.headless") {
                    $entryPaths = @($fileEntries | ForEach-Object { ($_.path -replace '\\', '/') })
                    $hitCoreSim = @($entryPaths | Where-Object { $_ -like "*Lib~/Core.Sim.dll" })
                    if ($hitCoreSim.Count -eq 0) {
                        $problems += "$pkgName：npm pack --dry-run 文件清单缺失 Lib~/Core.Sim.dll（T-N6-7）"
                    }
                }
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
    if ($SkipUnity) {
        Add-SkippedStep "Unity 编译检查" "-SkipUnity"
        Add-SkippedStep "Unity EditMode 测试" "-SkipUnity"
        Add-SkippedStep "Unity PlayMode 测试" "-SkipUnity"
        Add-SkippedStep "独立版构建 + -gf-smoke 冒烟（连续模式默认流程）" "-SkipUnity"
        Add-SkippedStep "独立版 -gf-smoke-discrete 冒烟（离散模式链路）" "-SkipUnity"
        Add-SkippedStep "消费方演练" "-SkipUnity"
    } else {
        Test-UnityWorkingTreePathLength -RepoRoot $RepoRoot

        $resolvedUnityExe = Resolve-UnityExe -Explicit $UnityExe
        $unityProjectPath = Join-Path $RepoRoot "adapters\unity"

        Invoke-CheckStep "Unity 编译检查" {
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

        Invoke-CheckStep "Unity EditMode 测试" {
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
            if (-not (Test-Path $resultsXml)) {
                return [PSCustomObject]@{ Ok = $false; Detail = "未生成结果 XML：$resultsXml" }
            }
            [xml]$xml = Get-Content -Path $resultsXml -Raw
            $root = $xml.DocumentElement
            $editModeOk = ($root.result -eq "Passed")
            if (-not $editModeOk) {
                Invoke-UnityTestTriageOnFailure -ResultsXml $resultsXml -LogPath $log
            }
            [PSCustomObject]@{
                Ok     = $editModeOk
                Detail = "total=$($root.total) passed=$($root.passed) failed=$($root.failed)"
            }
        }

        Invoke-CheckStep "Unity PlayMode 测试" {
            Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
            $resultsXml = Join-Path $UnityOutDir "playmode.xml"
            $log = Join-Path $UnityOutDir "playmode.log"
            $proc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
                "-batchmode",
                "-projectPath", $unityProjectPath,
                "-runTests", "-testPlatform", "PlayMode",
                "-testResults", $resultsXml,
                "-logFile", $log
            )
            if ($proc.ExitCode -ne 0) {
                return [PSCustomObject]@{ Ok = $false; Detail = "Unity 退出码 $($proc.ExitCode)" }
            }
            if (-not (Test-Path $resultsXml)) {
                return [PSCustomObject]@{ Ok = $false; Detail = "未生成结果 XML：$resultsXml" }
            }
            [xml]$xml = Get-Content -Path $resultsXml -Raw
            $root = $xml.DocumentElement
            $playModeOk = ($root.result -eq "Passed")
            if (-not $playModeOk) {
                Invoke-UnityTestTriageOnFailure -ResultsXml $resultsXml -LogPath $log
            }
            [PSCustomObject]@{
                Ok     = $playModeOk
                Detail = "total=$($root.total) passed=$($root.passed) failed=$($root.failed)"
            }
        }

        Invoke-CheckStep "独立版构建 + -gf-smoke 冒烟（连续模式默认流程）" {
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
                Ok     = ($logText -match "\[GF-SMOKE\] RESULT=OK")
                Detail = "见 $smokeLog"
            }
        }

        Invoke-CheckStep "独立版 -gf-smoke-discrete 冒烟（离散模式链路）" {
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
                Ok     = ($logText -match "\[GF-SMOKE\] RESULT=OK") -and ($logText -match "step=discrete_round ok")
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

            Invoke-CheckStep "IL2CPP 独立版构建" {
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

            Invoke-CheckStep "IL2CPP 独立版 -gf-smoke 冒烟" {
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
                    Ok     = ($logText -match "\[GF-SMOKE\] RESULT=OK")
                    Detail = "见 $smokeLog"
                }
            }

            Invoke-CheckStep "IL2CPP 独立版 -gf-smoke-discrete 冒烟" {
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
                    Ok     = ($logText -match "\[GF-SMOKE\] RESULT=OK") -and ($logText -match "step=discrete_round ok")
                    Detail = "见 $smokeLog"
                }
            }
        }

        if ($SkipConsumer) {
            Add-SkippedStep "消费方演练" "-SkipConsumer"
        } else {
            Invoke-CheckStep "消费方演练（toolchain/consumer_smoke.ps1）" {
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
