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
    # 第四批（复盘 I-5 缩减版）新增：环境矩阵两步（不设 PYTHONUTF8 的 pytest、PowerShell 5.1/7 双宿主
    # pytest）只在全量门禁里跑，-Quick 与 -SkipUnity 都不跑（-SkipUnity 是 CI 的固定形态，设计层拍板
    # "日常切片/CI 不受影响"），所以本线也要知道 -SkipUnity；本线其余步骤不受它影响。
    [switch]$SkipUnity,
    [string]$FailFastFlagPath = "",
    # 定向门禁（ADR-0126）：toolchain/change_impact.py 写出的判定 JSON；空串 = 非定向模式（全部步骤
    # 照旧）。给了则各 Invoke-CheckStep 的 -Id 按判定结果决定跑还是改判 SKIP，dotnet test 按判定里的
    # 测试工程列表逐个跑。
    [string]$PlanFile = "",
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
. (Join-Path $RepoRoot "toolchain\_gate_test_floors.ps1")
. (Join-Path $RepoRoot "toolchain\_sim_added_guard.ps1")
Import-GatePlan -Path $PlanFile

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
    Invoke-CheckStep "dotnet build Core.sln -c $Configuration" -Id "dotnet_build" {
        Test-NativeExitCode "dotnet" @("build", $SolutionPath, "-c", $Configuration, "--artifacts-path", $ArtifactsPath)
    }

    # -----------------------------------------------------------------------
    # 2. dotnet test（测试工程数以 Core.sln 里 Tests.*.csproj 的条目数为准，步骤名里的数字由脚本
    #    数出来、不写死，新增/删减测试工程时步骤名自动跟着变——此前写死"六工程"，实际早已是八个，
    #    复盘 I-11；性能基线机器归一化诊断行处理同原 check.ps1，判断记录见该函数头）
    #    用例数下限（复盘 I-2）：trx 是这一步本来就在产出的机器可读结果，直接读 Counters 求和，
    #    passed 低于 toolchain/gate_floors.json 的 dotnet_test.min_passed、或 skipped 超过
    #    max_skipped 即 FAIL，判断记录见 toolchain/_gate_test_floors.ps1 与 check.ps1 头部。
    # -----------------------------------------------------------------------
    $PerfTrxDir = Join-Path $ArtifactsPath "perf_trx"
    $GateFloorsPath = Join-Path $RepoRoot "toolchain\gate_floors.json"
    $TestProjectCount = @(Select-String -LiteralPath $SolutionPath -Pattern '^Project\(.*,\s*"[^"]*Tests\.[^"\\]*\.csproj"').Count
    # 定向模式（ADR-0126 T1/T2）：判定里 dotnet_test.mode=projects 时只跑命中层（T2 另加下游一层）的
    # 测试工程，逐个 `dotnet test <工程> --no-build`；用例数下限换成子集底线（见
    # toolchain/_gate_test_floors.ps1 Invoke-GateTargetedCountsCheck）。mode=solution（T3）或没有判定
    # 文件时走原来的 Core.sln 全量。
    $targetedTestProjects = @()
    if ($script:GatePlan -and [string]$script:GatePlan.dotnet_test.mode -eq "projects") {
        $targetedTestProjects = @($script:GatePlan.dotnet_test.projects | ForEach-Object { [string]$_ })
    }
    $dotnetTestName = "dotnet test Core.sln -c $Configuration --no-build（$TestProjectCount 个测试工程，含 Perf 类别；用例数下限见 gate_floors.json）"
    if ($targetedTestProjects.Count -gt 0) {
        $dotnetTestName = "dotnet test（定向：" + (($targetedTestProjects | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_) }) -join "、") + "，--no-build）"
    }
    Invoke-CheckStep $dotnetTestName -Id "dotnet_test" {
        if (Test-Path $PerfTrxDir) {
            Remove-Item $PerfTrxDir -Recurse -Force
        }

        if ($targetedTestProjects.Count -gt 0) {
            $allProjectsOk = $true
            foreach ($proj in $targetedTestProjects) {
                $projPath = Join-Path $RepoRoot $proj
                $projOk = Test-NativeExitCode "dotnet" @(
                    "test", $projPath, "-c", $Configuration, "--no-build", "--artifacts-path", $ArtifactsPath,
                    "--logger", "trx", "--results-directory", $PerfTrxDir)
                if (-not $projOk) { $allProjectsOk = $false }
            }
            $targetedResult = Invoke-GateTargetedCountsCheck -Kind Trx -Path $PerfTrxDir -Suite "dotnet_test" -FloorsPath $GateFloorsPath
            if ($targetedResult.Ok) {
                Write-Host "定向子集计数：$($targetedResult.Detail)" -ForegroundColor DarkGray
            } else {
                Write-Host "定向子集计数未达：$($targetedResult.Detail)" -ForegroundColor Red
            }
            return [PSCustomObject]@{ Ok = ($allProjectsOk -and $targetedResult.Ok); Detail = $targetedResult.Detail }
        }

        $ok = Test-NativeExitCode "dotnet" @(
            "test", $SolutionPath, "-c", $Configuration, "--no-build", "--artifacts-path", $ArtifactsPath,
            "--logger", "trx", "--results-directory", $PerfTrxDir)

        # 判断记录（复盘 I-2 余项，2026-10-01）：性能基线诊断行缺失此前只打黄色警告、步骤照样 PASS，
        # PerfBaselineTests 被意外排除/没编进来时 Perf 类别整个塌了也无人察觉。现在缺失直接 FAIL
        # （解析抽成 _gate_test_floors.ps1 的 Get-PerfDiagnosticLines，伪造 trx 夹具见
        # toolchain/tests/test_gate_floors_logic.py）。
        $perfLines = @(Get-PerfDiagnosticLines -TrxDir $PerfTrxDir)
        $perfDiagOk = ($perfLines.Count -gt 0)
        if ($perfDiagOk) {
            Write-Host "---- 性能基线机器归一化诊断（Perf 类别，见 core/gameplay/tests/Perf/README.md） ----" -ForegroundColor Cyan
            $perfLines | ForEach-Object { Write-Host $_ }
        } else {
            Write-Host "未能从 trx 结果中找到性能基线诊断行（PerfBaselineTests 是否被意外排除或未编译进本次运行？），判 FAIL" -ForegroundColor Red
        }

        $floorResult = Invoke-GateTestFloorCheck -Kind Trx -Path $PerfTrxDir -Suite "dotnet_test" -FloorsPath $GateFloorsPath
        if ($floorResult.Ok) {
            Write-Host "用例数下限：$($floorResult.Detail)" -ForegroundColor DarkGray
        } else {
            Write-Host "用例数下限未达：$($floorResult.Detail)" -ForegroundColor Red
        }
        $perfDetail = if ($perfDiagOk) { "perf 诊断行 $($perfLines.Count) 条" } else { "性能基线诊断行缺失（PerfBaselineTests 被排除或未编译进本次运行）" }
        [PSCustomObject]@{ Ok = ($ok -and $floorResult.Ok -and $perfDiagOk); Detail = ($floorResult.Detail + "；" + $perfDetail) }
    }

    # -----------------------------------------------------------------------
    # 2b. ABI 探针（toolchain/abi_probe.ps1）：判断记录同原 check.ps1（PJ114-02、`-Command` 调用
    #     形状下 `exit` 透传等），原样搬入。
    # -----------------------------------------------------------------------
    if ($Quick) {
        Add-SkippedStep "ABI 探针（toolchain/abi_probe.ps1）" "-Quick" -Id "abi_probe"
    } else {
        Invoke-CheckStep "ABI 探针（toolchain/abi_probe.ps1）" -Id "abi_probe" {
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
        Add-SkippedStep "python toolchain/gen_placeholder_assets.py --check" "-Quick" -Id "placeholder_assets"
    } else {
        Invoke-CheckStep "python toolchain/gen_placeholder_assets.py --check" -Id "placeholder_assets" {
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
        Add-SkippedStep "样例导入幂等性门禁（重跑 import_sample_assets.py 应零 diff）" "-Quick" -Id "sample_import_idem"
    } else {
        Invoke-CheckStep "样例导入幂等性门禁（重跑 import_sample_assets.py 应零 diff）" -Id "sample_import_idem" {
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
    #    判断记录（gate-parallel-ctrlc 修复，2026-09-22 追加 --ignore）：
    #    `test_registry_stop_pidfile_rewrite_timestamp.py::test_detach_twice_then_stop_succeeds`
    #    已从本套件抽出、改到 check.ps1 阶段一（两条并行线派生之前）单独串行跑，见 check.ps1
    #    .SYNOPSIS 判断记录 6)（根因：该用例启动多个 CREATE_NEW_CONSOLE 独立控制台的 PowerShell
    #    子进程，是全套件里唯一对"控制台控制事件"敏感的用例，本线与 Unity 串行线并行执行的这段
    #    窗口是全流程耗时最长、统计上暴露风险最大的阶段）。这里加 `--ignore` 排除该文件，避免
    #    与阶段一的串行调用重复跑两遍。
    # -----------------------------------------------------------------------
    if ($Quick) {
        Add-SkippedStep "python -m pytest toolchain/tests -q" "-Quick" -Id "toolchain_pytest"
    } else {
        # 用例数下限（复盘 I-2）：加 --junitxml 产出机器可读结果，读 tests/failures/errors/skipped
        # 与 toolchain/gate_floors.json 的 pytest 登记比较（判断记录见 toolchain/_gate_test_floors.ps1）。
        # 该下限只覆盖本步骤实际跑的用例（已 --ignore 掉阶段一单独串行跑的那个文件）。
        $pytestJunit = Join-Path $ArtifactsPath "pytest_junit.xml"
        Invoke-CheckStep "python -m pytest toolchain/tests -q（用例数下限见 gate_floors.json）" -Id "toolchain_pytest" {
            if (Test-Path -LiteralPath $pytestJunit) {
                Remove-Item -LiteralPath $pytestJunit -Force
            }
            $prevPythonUtf8 = $env:PYTHONUTF8
            $env:PYTHONUTF8 = "1"
            Push-Location $RepoRoot
            try {
                $pytestOk = Test-NativeExitCode "python" @(
                    "-m", "pytest", "toolchain/tests", "-q",
                    "--ignore=toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py",
                    "--junitxml=$pytestJunit")
            } finally {
                Pop-Location
                $env:PYTHONUTF8 = $prevPythonUtf8
            }
            $floorResult = Invoke-GateTestFloorCheck -Kind JUnit -Path $pytestJunit -Suite "pytest" -FloorsPath $GateFloorsPath
            if ($floorResult.Ok) {
                Write-Host "用例数下限：$($floorResult.Detail)" -ForegroundColor DarkGray
            } else {
                Write-Host "用例数下限未达：$($floorResult.Detail)" -ForegroundColor Red
            }
            [PSCustomObject]@{ Ok = ($pytestOk -and $floorResult.Ok); Detail = $floorResult.Detail }
        }
    }

    # -----------------------------------------------------------------------
    # 6b. 数值仿真基线比对（toolchain/simrunner）：判断记录（复用步骤 1 已构建产物、Added 也算
    #     差异等）同原 check.ps1，原样搬入。
    # -----------------------------------------------------------------------
    if ($Quick) {
        Add-SkippedStep "数值仿真基线比对（toolchain/simrunner）" "-Quick" -Id "sim_baseline"
    } else {
        Invoke-CheckStep "数值仿真基线比对（toolchain/simrunner）" -Id "sim_baseline" {
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

            # 判断记录：Added 拦截判定抽成 toolchain/_sim_added_guard.ps1 的 Get-SimRunnerAddedVerdict
            # （正则与此前内联版逐字相同），由 toolchain/tests/test_gate_sim_added_guard.py 直接验证。
            # 与内联版唯一的行为差异是有意收紧：simrunner 退出码 0 时必然已打印至少一行场景摘要，
            # 若一行都解析不出来（或出现以 scenario= 开头却匹配不上的行），说明摘要行格式被改动、
            # 原正则悄悄失配，此时不能当作"没有 Added"放行。
            $addedVerdict = Get-SimRunnerAddedVerdict -OutputLines ([string[]]@($simOutputLines | ForEach-Object { "$_" }))
            $addedScenarios = @($addedVerdict.AddedScenarios)

            if ($addedVerdict.Reason -in @("no_summary_line", "malformed_summary_line")) {
                $driftDetail = if ($addedVerdict.Reason -eq "no_summary_line") {
                    "simrunner 退出码=0 却没有输出任何场景摘要行"
                } else {
                    "simrunner 输出里有 $(@($addedVerdict.MalformedLines).Count) 行以 scenario= 开头却不符合场景摘要行格式：" +
                    ((@($addedVerdict.MalformedLines) | Select-Object -First 3) -join " | ")
                }
                return [PSCustomObject]@{ Ok = $false; Detail = "数值仿真基线比对：$driftDetail——无法确认 added=<n> 是否为 0。通常是 toolchain/simrunner/Program.cs 的场景摘要行格式被改动而本门禁的解析（toolchain/_sim_added_guard.ps1）没有同步；先对齐两端格式，不要直接放行。" }
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
    # -----------------------------------------------------------------------
    # 6c / 6d. 工具链测试环境矩阵（测试覆盖第四批，复盘 I-5 缩减版，设计层拍板见
    #     docs/复盘/测试覆盖剩余项-2026-10-01.md 末节）。只在全量门禁（不含 -Quick、不含 -SkipUnity，
    #     即不含 CI 的固定形态）里跑，日常切片与 CI 不受影响；时长约各 +3 分钟 / +3.5 分钟，因本线与
    #     Unity 串行线并行（Unity 线数分钟起），不拉长全量门禁墙钟。
    #
    #     6c 默认编码矩阵：步骤 6 的主 pytest 固定 PYTHONUTF8=1 跑（这是日常开发环境的惯例），把
    #     PYTHONUTF8 与 PYTHONIOENCODING 都摘掉再整套跑一遍，抓"依赖 UTF-8 模式才能工作"的编码缺陷
    #     （系统 ANSI 代码页下的文件读写、子进程输出解码）。判据：junit 可解析、failed=0、skipped=0，
    #     passed 不低于 gate_floors.json 里 pytest 的 min_passed（同一批用例，下限共用，不另登记数字）。
    #
    #     6d PowerShell 双宿主矩阵：toolchain/tests 里启动 PowerShell 子进程跑 .ps1 的那批用例（各测试
    #     文件自己 shutil.which 选宿主，惯例 5.1 优先，所以过去永远只在 5.1 下跑）用环境变量
    #     WS_GAME_PS_HOST=5.1 / 7 各整批跑一遍，开关由 toolchain/tests/conftest.py 实现（会话开始时真的
    #     启动所选宿主核对主版本，宿主缺失或解析错位直接 pytest.exit，不会悄悄退化）。"这批用例"=
    #     toolchain/tests/test_*.py 里出现带引号的 "powershell"/"pwsh" 字样、或引用共用工具 _ps_harness 的文件（动态筛选，新增同类
    #     测试自动纳入；筛不出任何文件判 FAIL，防筛选规则坏了静默变空）；test_registry_stop_pidfile_
    #     rewrite_timestamp.py 排除，它按 check.ps1 阶段一的既有判断记录单独串行跑。判据同 6c 但
    #     MinPassed 取 1，另要求两个宿主的 total 相等（两遍跑的是同一批用例）。环境性 skip 一律 FAIL。
    # -----------------------------------------------------------------------
    $matrixSkipReason = if ($Quick) { "-Quick" } elseif ($SkipUnity) { "-SkipUnity（环境矩阵只在全量门禁里跑，CI 的 -SkipUnity 形态不含）" } else { "" }

    if ($matrixSkipReason -ne "") {
        Add-SkippedStep "python -m pytest toolchain/tests -q（环境矩阵 6c：不设 PYTHONUTF8）" $matrixSkipReason -Id "pytest_env_matrix_utf8"
    } else {
        $noUtf8Junit = Join-Path $ArtifactsPath "pytest_junit_noutf8.xml"
        Invoke-CheckStep "python -m pytest toolchain/tests -q（环境矩阵 6c：不设 PYTHONUTF8）" -Id "pytest_env_matrix_utf8" {
            if (Test-Path -LiteralPath $noUtf8Junit) {
                Remove-Item -LiteralPath $noUtf8Junit -Force
            }
            $prevPythonUtf8 = $env:PYTHONUTF8
            $prevPythonIoEncoding = $env:PYTHONIOENCODING
            Remove-Item Env:\PYTHONUTF8 -ErrorAction SilentlyContinue
            Remove-Item Env:\PYTHONIOENCODING -ErrorAction SilentlyContinue
            Push-Location $RepoRoot
            try {
                $noUtf8Ok = Test-NativeExitCode "python" @(
                    "-m", "pytest", "toolchain/tests", "-q",
                    "--ignore=toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py",
                    "--junitxml=$noUtf8Junit")
            } finally {
                Pop-Location
                $env:PYTHONUTF8 = $prevPythonUtf8
                $env:PYTHONIOENCODING = $prevPythonIoEncoding
            }
            $floorsCfg = Get-GateFloorsConfig -FloorsPath $GateFloorsPath
            $minPassed = [int]$floorsCfg.suites.pytest.min_passed
            $noUtf8Result = Test-GateExtraPytestRun -JUnitPath $noUtf8Junit -Label "pytest 不设 PYTHONUTF8" -MinPassed $minPassed
            if (-not $noUtf8Result.Ok) {
                Write-Host "环境矩阵未达：$($noUtf8Result.Detail)" -ForegroundColor Red
            }
            [PSCustomObject]@{ Ok = ($noUtf8Ok -and $noUtf8Result.Ok); Detail = $noUtf8Result.Detail }
        }
    }

    if ($matrixSkipReason -ne "") {
        Add-SkippedStep "python -m pytest（环境矩阵 6d：PowerShell 脚本类用例在 5.1 与 7 两个宿主各跑一遍）" $matrixSkipReason -Id "pytest_env_matrix_pshost"
    } else {
        Invoke-CheckStep "python -m pytest（环境矩阵 6d：PowerShell 脚本类用例在 5.1 与 7 两个宿主各跑一遍）" -Id "pytest_env_matrix_pshost" {
            $testsDir = Join-Path $RepoRoot "toolchain\tests"
            $psTestFiles = @(Get-ChildItem -LiteralPath $testsDir -Filter "test_*.py" -File |
                Where-Object { $_.Name -ne "test_registry_stop_pidfile_rewrite_timestamp.py" } |
                Where-Object { Select-String -LiteralPath $_.FullName -Pattern '(["''](powershell|pwsh)(\.exe)?["'']|_ps_harness)' -Quiet } |
                ForEach-Object { "toolchain/tests/" + $_.Name })
            if ($psTestFiles.Count -eq 0) {
                return [PSCustomObject]@{ Ok = $false; Detail = "没有筛选出任何 PowerShell 脚本类 pytest 文件（筛选规则失效？）" }
            }
            Write-Host "PowerShell 脚本类 pytest 文件 $($psTestFiles.Count) 个：$($psTestFiles -join ' ')"

            $prevPythonUtf8 = $env:PYTHONUTF8
            $prevPsHost = $env:WS_GAME_PS_HOST
            $env:PYTHONUTF8 = "1"
            $allOk = $true
            $details = @()
            $totals = @()
            Push-Location $RepoRoot
            try {
                foreach ($psHost in @("5.1", "7")) {
                    $hostJunit = Join-Path $ArtifactsPath ("pytest_junit_pshost_" + $psHost.Replace(".", "") + ".xml")
                    if (Test-Path -LiteralPath $hostJunit) {
                        Remove-Item -LiteralPath $hostJunit -Force
                    }
                    $env:WS_GAME_PS_HOST = $psHost
                    $runOk = Test-NativeExitCode "python" (@("-m", "pytest") + $psTestFiles + @("-q", "-p", "no:cacheprovider", "--junitxml=$hostJunit"))
                    $hostResult = Test-GateExtraPytestRun -JUnitPath $hostJunit -Label "PowerShell $psHost" -MinPassed 1
                    if (-not $hostResult.Ok) {
                        Write-Host "环境矩阵未达：$($hostResult.Detail)" -ForegroundColor Red
                    }
                    if (-not ($runOk -and $hostResult.Ok)) { $allOk = $false }
                    $details += $hostResult.Detail
                    $hostCounts = Get-JUnitTestCounts -XmlPath $hostJunit
                    if ($null -ne $hostCounts) { $totals += $hostCounts.Total }
                }
            } finally {
                Pop-Location
                $env:PYTHONUTF8 = $prevPythonUtf8
                $env:WS_GAME_PS_HOST = $prevPsHost
            }
            if ($allOk -and $totals.Count -eq 2 -and $totals[0] -ne $totals[1]) {
                $allOk = $false
                $details += "两个宿主跑的用例总数不一致（5.1=$($totals[0]) 7=$($totals[1])）"
            }
            [PSCustomObject]@{ Ok = $allOk; Detail = ($details -join " | ") }
        }
    }

} catch {
    # 兜底：本线内任何一处未被 Invoke-CheckStep 自己 try/catch 接住的异常（理论上不应该发生，
    # 因为每个真正的检查都已经包在 Invoke-CheckStep 里；这里只防"本脚本自身的胶水代码"，如
    # dot-source 失败、路径拼接异常），记一行 FAIL，保证本线仍会正常落盘 JSON、不会让主进程
    # 的 Wait-Job 永远等到一个没有输出结果文件的僵死状态。
    $script:Results.Add((New-GateResultRow -Step "非 Unity 重步骤线：子进程异常" -Result "FAIL" -Seconds 0 -Detail $_.Exception.Message -StepId "line_heavy_error"))
} finally {
    Write-GateResultsJson -Path $ResultsJsonPath
    if ($TranscriptPath -ne "") {
        try { Stop-Transcript | Out-Null } catch {}
    }
}
