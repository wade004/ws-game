<#
门禁"非 Unity 重步骤线"子脚本（工程收尾 gate-speed 任务新增，2026-09-22）：`check.ps1` 把不需要
Unity、但耗时明显的几步——`dotnet build`/`dotnet test`、ABI 探针、占位资产生成器一致性检查、
样例导入幂等性门禁、`toolchain` 自身 pytest 套件、数值仿真基线比对——收拢到本脚本，由
`check.ps1` 用 `Start-Job -FilePath` 启动为独立子进程，与 `toolchain/_gate_line_unity.ps1`
（Unity 串行链）并行跑。两条线各自完全独立的输出目录/产物文件（见下方各步骤判断记录），不共享
可写状态，因此可以安全并行，不需要额外加锁。

判断记录（为什么这几步归为一条"线"、顺序如何定）：`dotnet test`（步骤 2）、ABI 探针（步骤
2b）、数值仿真基线比对（步骤 6b）三者都要读步骤 1 `dotnet build --artifacts-path $ArtifactsPath`
产出的 DLL（`dotnet test --no-build`、`abi_probe.ps1` 读同一 `$ArtifactsPath` 下的正式 DLL、
`SimRunner.dll` 同样在 `$ArtifactsPath\bin\SimRunner\...` 下），必须排在步骤 1 之后——这是
真实存在的依赖，保留原顺序。占位资产生成器检查（5）、样例导入幂等性门禁（5d）、pytest（6）三者
不依赖步骤 1 的构建产物，理论上可以插在任意位置；这里统一放在"依赖步骤 1 的三步"之间/之后，
让"用到刚构建产物的检查"相邻、日志读起来连贯，同时 pytest（本线单项最耗时，约 89s）放在最后，
使本线内前面几步的失败能在 pytest 真正跑起来之前就被发现（FailFast 下收益最大；非 FailFast 下
不影响总用时，只影响"日志里第一个失败出现的时间点"）。

判断记录（与 Unity 线共享 $ArtifactsPath 根目录，为什么不冲突）：`check.ps1` 主进程在派生两条
子线之前已经创建好 `$ArtifactsPath` 与 `$ArtifactsPath\unity`（Unity 线专用子目录）两级目录（
避免两个子进程各自 `New-Item -Force` 同一路径产生竞态）。本线在 `$ArtifactsPath` 下只写
`bin\`（步骤 1 的 `--artifacts-path` 产物，含 `bin\SimRunner\...\SimRunner.dll`）、
`perf_trx\`、`abi_probe.log`、`sim_out\` 几个子路径；Unity 线只写 `$ArtifactsPath\unity\` 一个
子目录（compile.log/editmode.*/playmode.*/build.log/smoke_*.log/il2cpp\）。两条线的写入路径在
文件系统层面天然不相交，不需要额外隔离。另有一路潜在冲突已核实不存在：Unity 线里"9. build.ps1
-SkipTests"会另跑一次不带 `--artifacts-path` 的 `dotnet build`（产物落在各工程自己默认的
`bin\$Configuration\netstandard2.1\`/`obj\`，是 Unity 侧 DLL 同步的既有前提，见 AGENTS.md 第
4 节"dotnet 命令一律带 --artifacts-path"例外条款），与本线步骤 1 用 `--artifacts-path` 重定向
到 `$ArtifactsPath` 的产物目录是两棵完全不同的物理目录树（各工程默认 `bin\`/`obj\` vs
`$ArtifactsPath` 下的 artifacts 输出布局），两个 `dotnet build` 进程各自的中间产物不会互相踩踏；
`dotnet`/NuGet 自身对并发访问全局包缓存、编译服务器（VBCSCompiler/MSBuild Server）已有内建的
跨进程锁/复用机制，属于工具链自身职责，本脚本不需要额外处理。
#>
param(
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][string]$ArtifactsPath,
    [string]$Configuration = "Release",
    [switch]$Quick,
    [switch]$DocsOnly,
    [switch]$FailFast,
    [switch]$AbiStrict,
    [string]$FailFastFlagPath = "",
    [Parameter(Mandatory = $true)][string]$ResultsJsonPath,
    [string]$TranscriptPath = "",
    # 仅供并行编排的自证测试使用（见 check.ps1 验收记录）：在本线第一步之前插入一个可控耗时的
    # Start-Sleep 步骤，用来在不依赖真实 Unity 环境的前提下证明两条线确实同时在跑（对比总墙钟
    # 是否明显小于两线各自耗时之和）。默认 0 表示不插入，不影响正常门禁行为。
    [int]$InjectMockSleepSeconds = 0
)

$ErrorActionPreference = "Stop"

if ($TranscriptPath -ne "") {
    try { Start-Transcript -Path $TranscriptPath -Force | Out-Null } catch {}
}

$SolutionPath = Join-Path $RepoRoot "Core.sln"
$script:Results = New-Object System.Collections.Generic.List[Object]
$script:GateFailed = $false
$script:FailFastFlagPath = if ($FailFastFlagPath -ne "") { $FailFastFlagPath } else { $null }

. (Join-Path $RepoRoot "toolchain\_gate_step_runner.ps1")

try {
    # 判断记录：故意不经过 Invoke-CheckStep（不受 -DocsOnly/-FailFast/-Quick 任何一个开关的短路
    # 逻辑影响）——这是一个纯测试钩子，唯一目的是在验收/pytest 里证明"两条线确实并发"，不应该
    # 因为验收时顺手传了 -DocsOnly 之类的开关就被短路掉、量不出真实墙钟。默认 0 时这段代码等于
    # 空操作，不影响任何正式门禁运行的行为/耗时/步骤计数。
    if ($InjectMockSleepSeconds -gt 0) {
        Write-Host ""
        Write-Host "==== （并行编排自证）非 Unity 线模拟慢步骤 ====" -ForegroundColor Cyan
        Start-Sleep -Seconds $InjectMockSleepSeconds
        Write-Host "[（并行编排自证）非 Unity 线模拟慢步骤] Start-Sleep $InjectMockSleepSeconds s 完成" -ForegroundColor Green
    }

    # -----------------------------------------------------------------------
    # 1. dotnet build（原步骤编号沿用，方便与旧日志/文档对照）
    # -----------------------------------------------------------------------
    Invoke-CheckStep "dotnet build Core.sln -c $Configuration" {
        Test-NativeExitCode "dotnet" @("build", $SolutionPath, "-c", $Configuration, "--artifacts-path", $ArtifactsPath)
    }

    # -----------------------------------------------------------------------
    # 2. dotnet test（六工程；性能基线机器归一化诊断行处理同原 check.ps1，判断记录见该函数头）
    # -----------------------------------------------------------------------
    $PerfTrxDir = Join-Path $ArtifactsPath "perf_trx"
    Invoke-CheckStep "dotnet test Core.sln -c $Configuration --no-build（六工程，含 Perf 类别）" {
        if (Test-Path $PerfTrxDir) {
            Remove-Item $PerfTrxDir -Recurse -Force
        }

        $ok = Test-NativeExitCode "dotnet" @(
            "test", $SolutionPath, "-c", $Configuration, "--no-build", "--artifacts-path", $ArtifactsPath,
            "--logger", "trx", "--results-directory", $PerfTrxDir)

        if (Test-Path $PerfTrxDir) {
            $perfLines = Get-ChildItem $PerfTrxDir -Filter "*.trx" -Recurse -ErrorAction SilentlyContinue |
                Select-String -Pattern '^\s*<StdOut>perf \S+_WithinBaselineThreshold median=.*factor=.*reference=' |
                ForEach-Object { ($_.Line.Trim() -replace '^<StdOut>', '') -replace '</StdOut>$', '' }
            if ($perfLines) {
                Write-Host "---- 性能基线机器归一化诊断（Perf 类别，见 core/gameplay/tests/Perf/README.md） ----" -ForegroundColor Cyan
                $perfLines | Sort-Object -Unique | ForEach-Object { Write-Host $_ }
            } else {
                Write-Host "警告：未能从 trx 结果中找到性能基线诊断行（PerfBaselineTests 是否被意外排除或未编译进本次运行？）" -ForegroundColor Yellow
            }
        }

        $ok
    }

    # -----------------------------------------------------------------------
    # 2b. ABI 探针（toolchain/abi_probe.ps1）：判断记录同原 check.ps1（PJ114-02、`-Command` 调用
    #     形状下 `exit` 透传等），原样搬入。
    # -----------------------------------------------------------------------
    if ($Quick) {
        Add-SkippedStep "ABI 探针（toolchain/abi_probe.ps1）" "-Quick"
    } else {
        Invoke-CheckStep "ABI 探针（toolchain/abi_probe.ps1）" {
            $abiProbeScript = Join-Path $RepoRoot "toolchain\abi_probe.ps1"
            if (-not (Test-Path -LiteralPath $ArtifactsPath)) {
                New-Item -ItemType Directory -Force -Path $ArtifactsPath | Out-Null
            }
            $abiProbeLog = Join-Path $ArtifactsPath "abi_probe.log"
            $skipMissingLiteral = if ($AbiStrict) { '$false' } else { '$true' }
            $quotedScript = "'" + $abiProbeScript.Replace("'", "''") + "'"
            $quotedArtifacts = "'" + $ArtifactsPath.Replace("'", "''") + "'"
            $quotedConfiguration = "'" + $Configuration.Replace("'", "''") + "'"
            $abiProbeCommand = "& $quotedScript -ArtifactsPath $quotedArtifacts -Configuration $quotedConfiguration -SkipIfBaselineMissing:$skipMissingLiteral; exit `$LASTEXITCODE"
            & powershell -NoProfile -ExecutionPolicy Bypass -Command $abiProbeCommand *> $abiProbeLog
            $abiExit = $LASTEXITCODE

            if ($abiExit -eq 3) {
                return [PSCustomObject]@{ Skip = $true; Reason = "基线发行包不存在（详见 $abiProbeLog）" }
            }
            if ($abiExit -ne 0) {
                Get-Content -LiteralPath $abiProbeLog | Write-Host
                return [PSCustomObject]@{ Ok = $false; Detail = "abi_probe.ps1 退出码=$abiExit，详见 $abiProbeLog" }
            }
            return $true
        }
    }

    # -----------------------------------------------------------------------
    # 5. 占位资产生成器一致性检查（-Quick 跳过：需要 Pillow 且逐张比较占位图，不是秒级步骤）
    # -----------------------------------------------------------------------
    if ($Quick) {
        Add-SkippedStep "python toolchain/gen_placeholder_assets.py --check" "-Quick"
    } else {
        Invoke-CheckStep "python toolchain/gen_placeholder_assets.py --check" {
            Push-Location $RepoRoot
            try {
                Test-NativeExitCode "python" @("toolchain/gen_placeholder_assets.py", "--check")
            } finally {
                Pop-Location
            }
        }
    }

    # -----------------------------------------------------------------------
    # 5d. 样例导入幂等性门禁（重跑 import_sample_assets.py 应零 diff）——判断记录同原 check.ps1。
    # -----------------------------------------------------------------------
    if ($Quick) {
        Add-SkippedStep "样例导入幂等性门禁（重跑 import_sample_assets.py 应零 diff）" "-Quick"
    } else {
        Invoke-CheckStep "样例导入幂等性门禁（重跑 import_sample_assets.py 应零 diff）" {
            Push-Location $RepoRoot
            try {
                $watchPaths = @("data/_sample", "assets/_sample")

                $preStatus = & git status --porcelain -- $watchPaths
                if ($LASTEXITCODE -ne 0) {
                    return [PSCustomObject]@{ Ok = $false; Detail = "git status 执行失败（退出码 $LASTEXITCODE），无法判断工作树是否干净" }
                }
                if ($preStatus) {
                    Write-Host "检测到 $($watchPaths -join ', ') 下已有未提交改动，跳过本步骤（避免把本地正当改动误判为门禁失败）：" -ForegroundColor Yellow
                    $preStatus | Out-Host
                    return [PSCustomObject]@{ Skip = $true; Reason = "data/_sample 或 assets/_sample 已有未提交改动，先提交/还原后再跑本步骤" }
                }

                $rerunOk = Test-NativeExitCode "python" @("toolchain/import_sample_assets.py")
                if (-not $rerunOk) {
                    return [PSCustomObject]@{ Ok = $false; Detail = "python toolchain/import_sample_assets.py 重跑本身失败（非 diff 问题，见上方输出）" }
                }

                $postStatus = & git status --porcelain -- $watchPaths
                if ($LASTEXITCODE -ne 0) {
                    return [PSCustomObject]@{ Ok = $false; Detail = "git status 执行失败（退出码 $LASTEXITCODE），无法判断重跑后是否产生 diff" }
                }

                if (-not $postStatus) {
                    return $true
                }

                Write-Host "样例导入重跑后 $($watchPaths -join ', ') 出现 diff（幂等性被破坏）：" -ForegroundColor Red
                $postStatus | Out-Host
                $diffSummary = (& git diff --stat -- $watchPaths | Out-String).Trim()
                if ($diffSummary) {
                    Write-Host $diffSummary -ForegroundColor Red
                }

                & git checkout -- $watchPaths 2>$null | Out-Null
                & git clean -fd -- $watchPaths 2>$null | Out-Null

                $detailLines = @($postStatus | Select-Object -First 20)
                $detail = "重跑 import_sample_assets.py 后 data/_sample 或 assets/_sample 出现非预期 diff：" + ($detailLines -join "; ")
                return [PSCustomObject]@{ Ok = $false; Detail = $detail }
            } finally {
                Pop-Location
            }
        }
    }

    # -----------------------------------------------------------------------
    # 6. toolchain 自身的 pytest 套件（-Quick 跳过）。判断记录（是否启用 pytest-xdist 并行）：
    #    gate-speed 任务要求"先查 anaconda 环境里是否已装 pytest-xdist，已装就用 -n auto，没装就
    #    跳过不装"——本次实测（`base`/`python13` 两个 conda 环境均执行 `pip show pytest-xdist`）
    #    均未安装，按任务书"没装就不装，本项跳过并在报告说明"处理，不追加 `-n auto`，本步骤命令行
    #    与原 check.ps1 完全一致。
    # -----------------------------------------------------------------------
    if ($Quick) {
        Add-SkippedStep "python -m pytest toolchain/tests -q" "-Quick"
    } else {
        Invoke-CheckStep "python -m pytest toolchain/tests -q" {
            $prevPythonUtf8 = $env:PYTHONUTF8
            $env:PYTHONUTF8 = "1"
            Push-Location $RepoRoot
            try {
                Test-NativeExitCode "python" @("-m", "pytest", "toolchain/tests", "-q")
            } finally {
                Pop-Location
                $env:PYTHONUTF8 = $prevPythonUtf8
            }
        }
    }

    # -----------------------------------------------------------------------
    # 6b. 数值仿真基线比对（toolchain/simrunner）：判断记录（复用步骤 1 已构建产物、Added 也算
    #     差异等）同原 check.ps1，原样搬入。
    # -----------------------------------------------------------------------
    if ($Quick) {
        Add-SkippedStep "数值仿真基线比对（toolchain/simrunner）" "-Quick"
    } else {
        Invoke-CheckStep "数值仿真基线比对（toolchain/simrunner）" {
            $simOutDir = Join-Path $ArtifactsPath "sim_out"
            if (Test-Path -LiteralPath $simOutDir) {
                Remove-Item -LiteralPath $simOutDir -Recurse -Force
            }
            New-Item -ItemType Directory -Force -Path $simOutDir | Out-Null

            $simRunnerDll = Join-Path $ArtifactsPath ("bin\SimRunner\" + $Configuration.ToLowerInvariant() + "\SimRunner.dll")
            if (-not (Test-Path -LiteralPath $simRunnerDll)) {
                return [PSCustomObject]@{ Ok = $false; Detail = "找不到已构建的 $simRunnerDll——请确认步骤 1（dotnet build Core.sln --artifacts-path $ArtifactsPath）已成功" }
            }

            $simVersion = "unknown"
            $versionFilePath = Join-Path $RepoRoot "VERSION"
            if (Test-Path -LiteralPath $versionFilePath) {
                $simVersion = (Get-Content -LiteralPath $versionFilePath -Raw).Trim()
            }

            $simArgs = @(
                $simRunnerDll, "run",
                "--scenario", "all",
                "--framework-root", "data/_framework",
                "--data-root", "core/sim/tests/data",
                "--out", $simOutDir,
                "--baseline-dir", "core/sim/tests/baseline",
                "--version", $simVersion
            )

            Push-Location $RepoRoot
            try {
                $ErrorActionPreference = "Continue"
                $simOutputLines = @(& dotnet @simArgs)
                $simExitCode = $LASTEXITCODE
                $simOutputLines | ForEach-Object { Write-Host $_ }
            } finally {
                Pop-Location
            }

            if ($simExitCode -ne 0) {
                $diffFiles = @(Get-ChildItem -Path $simOutDir -Filter "*.diff.txt" -File -ErrorAction SilentlyContinue)
                foreach ($diffFile in $diffFiles) {
                    Write-Host "---- $($diffFile.FullName) ----" -ForegroundColor Yellow
                    Get-Content -LiteralPath $diffFile.FullName | Write-Host
                }
                return [PSCustomObject]@{ Ok = $false; Detail = "toolchain/simrunner 退出码=$simExitCode（0=全部场景无 Exceeded/Removed，1=存在 Exceeded/Removed，2=参数/数据装载错误，3=基线文件缺失；见上方场景摘要/RESULT 行与 diff 全文）" }
            }

            $addedScenarios = @()
            foreach ($line in $simOutputLines) {
                if ($line -match '^scenario=(\S+)\s+kind=\S+\s+stats=\d+\s+exceeded=\d+\s+added=(\d+)\s+removed=\d+\s+result=') {
                    $addedCount = [int]$Matches[2]
                    if ($addedCount -gt 0) {
                        $addedScenarios += [PSCustomObject]@{ Id = $Matches[1]; Count = $addedCount }
                    }
                }
            }

            if ($addedScenarios.Count -gt 0) {
                $diffFiles = @(Get-ChildItem -Path $simOutDir -Filter "*.diff.txt" -File -ErrorAction SilentlyContinue)
                foreach ($diffFile in $diffFiles) {
                    $addedLines = @(Get-Content -LiteralPath $diffFile.FullName | Where-Object { $_ -match '^Added\s' })
                    if ($addedLines.Count -gt 0) {
                        Write-Host "---- $($diffFile.FullName)（含基线里从未记录过的 Added 统计量） ----" -ForegroundColor Yellow
                        $addedLines | Write-Host
                    }
                }
                $scenarioSummary = ($addedScenarios | ForEach-Object { "$($_.Id)(+$($_.Count))" }) -join "、"
                $detail = "数值仿真基线比对：simrunner 退出码=0（其契约 0=无 Exceeded/Removed 不含 Added，" +
                    "见 core/sim/README.md 命令行入口一节），但门禁额外要求 Added 也算差异（AGENTS.md §4）——" +
                    "以下场景出现基线里从未记录过的统计量：$scenarioSummary，具体键见上方各 *.diff.txt 打印的 " +
                    "Added 行。这通常是新增探针/技能/内容扩大了 coverage 一类场景的统计面，属于合法新增，" +
                    "正确做法是同一提交里重新烘焙基线，不能带着未烘焙的 Added 差异合并：先跑 " +
                    "'./toolchain/sim_baseline.ps1 -Scenario all' 确认这条 Added 确实是本次改动预期引入的" +
                    "新统计维度，再跑 './toolchain/sim_baseline.ps1 -Scenario all -UpdateBaseline' 重新生成" +
                    "三份基线并把改动并入同一提交（提交信息按 core/sim/README.md 基线更新流程一节第 5 步注明）。" +
                    "若这条 Added 出乎意料、不是本次改动引入的新内容，说明基线或数据另有问题，不要用烘焙掩盖，先定位再处理。"
                return [PSCustomObject]@{ Ok = $false; Detail = $detail }
            }

            return $true
        }
    }
} catch {
    # 兜底：本线内任何一处未被 Invoke-CheckStep 自己 try/catch 接住的异常（理论上不应该发生，
    # 因为每个真正的检查都已经包在 Invoke-CheckStep 里；这里只防"本脚本自身的胶水代码"，如
    # dot-source 失败、路径拼接异常），记一行 FAIL，保证本线仍会正常落盘 JSON、不会让主进程
    # 的 Wait-Job 永远等到一个没有输出结果文件的僵死状态。
    $script:Results.Add([PSCustomObject]@{
        Step    = "非 Unity 重步骤线：子进程异常"
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
