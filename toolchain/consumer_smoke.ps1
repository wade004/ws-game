<#
.SYNOPSIS
    消费方演练脚本（见 architecture/落地计划 3.5 节"新游戏如何消费本框架"、
    architecture/13_新游戏接入指南.md 第 7 节验收关卡）：从零搭建一个全新、独立于本仓库源码树的
    最小 Unity 消费方工程，只以 file: 相对路径引用本框架的分发包（dist/<version>/），完整走一遍
    games/_template/README.md 描述的接入步骤，验证"一个从未见过本仓库源码的新游戏工程，照着文档
    操作能不能真正跑起来"，而不是只靠工作台工程（adapters/unity，源码级直接引用本仓库内容）自证。

    覆盖范围（2026-09-07 补充口径，与 games/_template/README.md 的「范围与设计决定（本模板"最小闭环"故意不覆盖的部分）」一节同一口径）：本脚本
    只验证"分发包能否被一个全新工程正确引用、装配、跑通模板自带的最小闭环（主菜单→新游戏→进图→
    移动→存档→读档→退出）"这一段基础设施链路是否通畅；不含战斗、技能、任务、掉落等任何具体游戏
    内容验收，也不代表某个真实游戏已完成接入验收——一个真实游戏仍需自行按
    architecture/13_新游戏接入指南.md 第 1 节七步逐项补齐并跑通自己的端到端验收。

.PARAMETER DistVersion
    要演练的分发包版本号。默认读取仓库根 VERSION 文件（单一版本源，见
    11_工程规范与测试.md 第 7 节）并追加 "-dryrun" 后缀，避免覆盖已发布版本的 dist 产物
    （见下方取值处判断记录）。显式传入时接受 X.Y.Z 或 X.Y.Z-dryrun 两种形式。

.PARAMETER WorkDir
    消费方工程的落地目录。默认 `<TEMP>\gf_consumer_smoke_<8 位哈希>`，哈希由本仓库根的绝对路径派生
    （主检出与每个工作树各自一份，互不清空；见 _unity_smoke_wait_scope_guard.ps1 的
    Get-ConsumerSmokeDefaultWorkDir），不进仓库、不提交；显式传入则以传入值为准。
    每次运行都会先清空重建，模拟"全新工程"这一前提，不复用上一次运行的残留状态。

.PARAMETER UnityExe
    Unity 可执行文件完整路径。默认与 check.ps1 同一套解析逻辑（Unity Hub 常见安装位置）。

.PARAMETER SkipCleanWorkDir
    跳过"运行前清空 WorkDir"（调试用：保留上一次运行留下的工程，便于用 Unity Editor 打开检查失败
    原因）。默认不传——每次都是全新工程。

.PARAMETER ArtifactsPath
    本脚本自己的完整控制台记录（Start-Transcript）落盘目录，默认 <仓库根>\bin\_check_artifacts
    （与 check.ps1 -ArtifactsPath 默认值同一约定）。日志文件固定名 consumer_smoke.log（覆盖
    上一次运行）。见下方判断记录（P07 根治之二）——check.ps1 全量门禁调用本脚本时会用
    `| Out-Null` 吞掉本脚本子进程的全部控制台输出，这份落盘记录是失败后唯一能追溯到的完整现场。

.PARAMETER DevelopmentBuild
    消费方反馈第 72 条根治：第 9 步"构建独立版"改勾选 BuildOptions.Development（开发者控制台/
    profiler 联机/脚本调试符号），供需要联调独立版而非验收发布包的场景使用。默认不传——第 9 步
    仍是内置 `-buildWindows64Player <path>` 开关，构建结果与本参数新增之前逐字节一致；传了本开关后
    改走自定义 `-executeMethod Game.Template.EditorTools.WindowsPlayerBuilder.BuildWindows64Player`
    （见该类型），带上 `-gfDevelopmentBuild` 标志。第 10 步的 `-gf-smoke-template` 无人值守冒烟不
    区分独立版是否为 Development Build，两种产物都可以直接拿去跑。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。UTF-8 with BOM（PS 5.1 默认按系统代码页读取
    不带 BOM 的脚本文件，中文字符/字符串字面量在无 BOM 时会被读错）。
    本脚本只读引用仓库内容（复制到 WorkDir 之外的独立目录），不修改仓库内任何文件（build.ps1
    -SyncOnly -Dist 会在仓库内的 dist/<version>/ 落地/覆盖分发包快照，这是 build.ps1 一贯的既有
    行为，不是本脚本新增的写入）。

    判断记录（P07 根治之一，起 Unity 前按撞车范围等待残留进程，不等系统里所有 Unity.exe）：
    check.ps1 全量门禁里，前面 Unity 编译检查/EditMode/PlayMode/独立版构建四步刚跑完就紧接着跑
    本脚本，本脚本自己内部又要另外拉起四次全新的 Unity.exe（首次编译、场景构建器、PlayMode 测试、
    独立版构建）——Unity.exe 本体退出（Process.WaitForExit 返回）到它彻底走完许可协商释放/临时
    文件清理之间存在滞后，曾实测复现"上一个 Unity.exe 刚退出、下一个 Unity.exe 紧接着启动"时的
    瞬时失败（不是真的代码/数据问题，是启动时机撞上了残留清理窗口）。这个前提决定了"要不要等"不能
    只看"系统里有没有 Unity.exe"，也不能完全不看工程路径：等待对象只应该是可能与本次演练撞车的
    残留——本仓库根目录下的工程（check.ps1 前几步刚跑完的 adapters/unity，正是要防的那种残留）、
    本脚本自己的工作目录 $WorkDir 下的工程（消费方演练自己刚起的临时工程，四步之间彼此也可能撞车）；
    别的仓库里长时间正常运行的 Unity 进程（例如另一个项目自己在跑的回归批处理）不是"刚退出还在
    清理"，等多久都不会消失，继续等只会拖垮门禁而没有任何好处。因此 Wait-NoResidualUnityProcess
    在本脚本每一次拉起 Unity 批处理之前轮询系统里的 Unity.exe 进程，对每个进程调用
    _unity_smoke_wait_scope_guard.ps1 的纯函数 Get-UnitySmokeProcessWaitDecision（输入进程命令行
    + $RepoRoot + $WorkDir，输出 "Wait"/"NoWait"/"Unknown" 三态）分类：落在上述两处之一的算
    "Wait"；命令行里明确解析出 -projectPath/-createProject 且落在两处之外的算"NoWait"，不计入等待
    对象，但每次检测到都会打一行提示（PID + 命令行）说明"不属于本演练，不等待"，不静默忽略，留痕方便
    出问题时现场可追溯；命令行里没有 -projectPath/-createProject 的同样算"NoWait"（2026-10-06，
    fix/hub-serve_20261006：用户开着 Unity Hub 时，Hub 常驻的 `unity.exe serve` 后台进程不带工程路径、
    不是 Editor、永远不会退出，原先按"无法判断归属"保守等待，60 秒后必然判演练失败，每次门禁都挂；
    本演练自己拉起的批处理 Editor 一律显式带 -projectPath，没有工程路径参数的进程不可能是"本演练刚
    退出、还在清理的 Editor"）；只有拿不到命令行（空）的算"Unknown"，无法判断归属，按原口径当作
    "要等"处理。只有当前一轮扫描到的全部 Unity.exe 进程都判定为 NoWait（或系统里已经没有
    Unity.exe）时才放行，最多等 60 秒，超时才报错并给出诊断（PID + 命令行），不会无限期挂起、也
    不会假装没看见继续往下跑导致更难定位的失败。与 check.ps1 自己的 Test-NoResidualUnityProcess
    语义不同——那个函数只在乎"同一工程"、发现即报错不等待，因为残留可能是人正在交互使用的 Editor
    窗口，不该代为等待；两个脚本各自保留一份独立实现（惯例同上——两个脚本各自可以单独运行，不互相
    依赖），语义按各自场景分别设计，不是简单复制粘贴。

    判断记录（P07 根治之二，2026-09-08，check.ps1 全量门禁下失败现场丢失）：check.ps1 用
    `& powershell @consumerArgs | Out-Null` 调用本脚本（判断记录见 check.ps1 该处调用点注释——
    `Invoke-CheckStep` 把子进程整段 stdout 当返回值，不吃掉会把每一步 PASS/FAIL 明细和最终汇总表
    都错判成一个非空数组、恒定判 PASS），代价是本脚本自己 Write-Host 出来的全部步骤明细、失败
    Detail、汇总表都被吞掉，check.ps1 自己的 -LogFile transcript 里也不会有这些内容——本脚本一旦
    真的失败，唯一的失败现场就此丢失，只能重新手工单独跑一遍才能看到发生了什么。改法：本脚本自己
    用 Start-Transcript 把从此处开始的全部控制台输出（含 Unity 四步各自的 Detail、汇总表）额外落盘
    到 -ArtifactsPath\consumer_smoke.log，不依赖调用方是否吞掉了自己的 stdout；脚本任何一个失败
    退出点在 Stop-Transcript 之后都会把该日志文件最后 30 行重新打到控制台，方便本脚本被直接单独
    运行（未经 check.ps1 的 Out-Null 包裹）时不用另外打开文件就能看到关键片段。
#>
param(
    [string]$DistVersion = "",
    [string]$WorkDir = "",
    [string]$UnityExe = "",
    [switch]$SkipCleanWorkDir,
    [string]$ArtifactsPath = "",
    [switch]$DevelopmentBuild
)

$ErrorActionPreference = "Stop"

# Wait-NoResidualUnityProcess 的"从命令行判断是否需要等待"纯逻辑抽在这里，见该函数上方判断记录
# 与 _unity_smoke_wait_scope_guard.ps1 文件头注释。
. (Join-Path $PSScriptRoot "_unity_smoke_wait_scope_guard.ps1")
# 包管理器子进程中途消失（"IPC stream failed to read"）的失败现场抓取，见该文件头判断记录。
. (Join-Path $PSScriptRoot "_upm_evidence.ps1")
# ADR-0160 包边界：独立版产物不得含实验室/演示内容（第 9 步构建后断言）。
. (Join-Path $PSScriptRoot "_gate_package_boundary.ps1")

$RepoRoot = Split-Path -Parent $PSScriptRoot
$VersionFilePath = Join-Path $RepoRoot "VERSION"

if ($DistVersion -eq "") {
    if (-not (Test-Path $VersionFilePath)) {
        Write-Host "找不到版本文件：$VersionFilePath" -ForegroundColor Red
        exit 1
    }
    # 判断记录（为什么默认版本号要带 "-dryrun" 后缀）：本脚本第 1 步会 build.ps1 -SyncOnly -Dist
    # <版本> 重建 dist\<版本>\，而 VERSION 文件在一次发布落地后就等于那个已发布且已打标签的版本
    # 号——直接用它等于反复重写已经分发出去的产物，正是 toolchain/_dist_immutability_guard.ps1
    # 要拦的事（2026-09-23 实测命中："拒绝覆盖已发布版本 1.62.0 的产物"，门禁本步骤失败）。
    # "-dryrun" 是合法 semver 预发布标识、build.ps1 明确支持（$DistVersionDryRunPattern），且
    # git tag -l "v<带后缀>" 天然查不到匹配，是守卫预留给"只验证打包/演练、不是真发布"调用点的
    # 放行方式。演练验证的是分发包内容与消费方装配流程，与版本号字符串本身无关。显式传
    # -DistVersion X.Y.Z 仍然可用（例如演练一个已经打好的历史版本快照）。
    $DistVersion = (Get-Content -Path $VersionFilePath -Raw).Trim() + "-dryrun"
}
if ($DistVersion -notmatch '^\d+\.\d+\.\d+(-dryrun)?$') {
    Write-Host "版本号格式非法：'$DistVersion'（需形如 X.Y.Z 或 X.Y.Z-dryrun）" -ForegroundColor Red
    exit 1
}

# 判断记录（2026-10-01，默认工作目录按检出隔离）：此前所有检出共用 `<TEMP>\gf_consumer_smoke`，
# 主检出与各工作树的演练会互相清空对方的工程、互相把对方的 Unity 进程当成要等的残留。现在默认
# 目录由仓库根派生（`<TEMP>\gf_consumer_smoke_<仓库根归一化路径的 SHA-256 前 8 位>`，见
# _unity_smoke_wait_scope_guard.ps1 的 Get-ConsumerSmokeDefaultWorkDir），显式 -WorkDir 仍然优先。
if ($WorkDir -eq "") {
    $WorkDir = Get-ConsumerSmokeDefaultWorkDir -RepoRoot $RepoRoot -TempRoot $env:TEMP
}

if ($ArtifactsPath -eq "") {
    $ArtifactsPath = Join-Path $RepoRoot "bin\_check_artifacts"
}
if (-not (Test-Path $ArtifactsPath)) {
    New-Item -ItemType Directory -Force -Path $ArtifactsPath | Out-Null
}
$LogFile = Join-Path $ArtifactsPath "consumer_smoke.log"
# 包管理器失败现场的存放目录（门禁产物目录下，bin/ 被 .gitignore 覆盖，不入库）。
$UpmEvidenceRoot = Join-Path $ArtifactsPath "upm_evidence"

# 判断记录（P07 根治之二）见本文件头 .NOTES："check.ps1 全量门禁下失败现场丢失"一节——本脚本
# 从这里开始把全部控制台输出（Write-Host/Write-Output 均含）额外落盘到 $LogFile，独立于调用方
# 是否吞掉了本脚本子进程自己的 stdout。Start-Transcript 在极少数环境下可能因为已有另一个未正常
# 关闭的 transcript 会话而抛错（例如上一次运行被强杀、没走到 Stop-Transcript）——这种情况不应该
# 阻断整个演练，只是丢失这一份落盘记录，因此吞掉异常继续往下跑。
$script:TranscriptStarted = $false
try {
    Start-Transcript -Path $LogFile -Force | Out-Null
    $script:TranscriptStarted = $true
} catch {
    Write-Host "警告：Start-Transcript 失败（$($_.Exception.Message)），本次运行不落盘 $LogFile，不影响演练本身" -ForegroundColor Yellow
}

# 失败/成功退出前统一调用：停止落盘记录；失败时把 $LogFile 最后 30 行重新打到控制台——本脚本被
# check.ps1 用 `| Out-Null` 包裹调用时这段打印同样会被吞掉（预期内，届时应查 $LogFile 本身），但
# 本脚本被人直接单独运行时，不用另外打开文件就能立刻看到失败现场，见头部 .NOTES。
function Stop-TranscriptAndReport {
    param([bool]$Failed)
    if ($script:TranscriptStarted) {
        try { Stop-Transcript | Out-Null } catch {}
    }
    if ($Failed -and (Test-Path $LogFile)) {
        Write-Host ""
        Write-Host "==== 失败诊断：$LogFile 最后 30 行 ====" -ForegroundColor Yellow
        Get-Content -Path $LogFile -Tail 30 | ForEach-Object { Write-Host $_ }
    }
}

function Resolve-UnityExe {
    param([string]$Explicit)
    if ($Explicit -ne "") {
        return $Explicit
    }
    $candidate = Join-Path $env:ProgramFiles "Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe"
    if (Test-Path $candidate) {
        return $candidate
    }
    return "Unity.exe"
}
$ResolvedUnityExe = Resolve-UnityExe -Explicit $UnityExe

# 判断记录（P07 根治之一）见本文件头 .NOTES："起 Unity 前不检查残留进程"一节——每次拉起 Unity
# 批处理（Editor，不含独立版产物自己的 exe）前先调用本函数，等到系统里没有可能撞车的 Unity.exe 进程了
# 才真正启动，避免与上一步刚退出、还没走完清理流程的 Unity.exe 撞车导致的瞬时失败。"可能撞车"的范围
# 由 Get-UnitySmokeProcessWaitDecision 判定（本仓库根/本演练工作目录下的工程要等；别的工程、无工程路径
# 的辅助进程如 Unity Hub 后台 serve 不等）。与 check.ps1 的 Test-NoResidualUnityProcess 语义不同，见
# 该函数上方判断记录及本文件头 .NOTES 的区分说明。
function Wait-NoResidualUnityProcess {
    param([int]$TimeoutSeconds = 60)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    # 同一次调用内，"属于其它工程、不等待"的提示每个 PID 只打一次，避免 60 秒轮询期间刷屏——
    # 不影响可追溯性：第一次检测到时就已经留痕。
    $notifiedOutOfScopePids = New-Object 'System.Collections.Generic.HashSet[int]'
    while ($true) {
        try {
            $procs = @(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop)
        } catch {
            # 查询进程列表本身失败（权限/WMI 服务异常等）：不能确认"没有残留"，但不应该让整个
            # 演练因为一次诊断性查询失败而卡死——按"未发现残留"处理，放行。
            return
        }

        $relevantProcs = New-Object System.Collections.Generic.List[Object]
        foreach ($proc in $procs) {
            $decision = Get-UnitySmokeProcessWaitDecision -CommandLine $proc.CommandLine -RepoRoot $RepoRoot -WorkDir $WorkDir
            if ($decision -eq "NoWait") {
                $residualProcId = [int]$proc.ProcessId
                if (-not $notifiedOutOfScopePids.Contains($residualProcId)) {
                    Write-Host "[消费方演练残留进程等待] PID $residualProcId：属于其它工程或非 Editor 辅助进程（如 Unity Hub 后台 serve），不等待（命令行：$($proc.CommandLine)）" -ForegroundColor DarkGray
                    $notifiedOutOfScopePids.Add($residualProcId) | Out-Null
                }
                continue
            }
            $relevantProcs.Add($proc)
        }

        if ($relevantProcs.Count -eq 0) {
            return
        }
        if ((Get-Date) -ge $deadline) {
            $detail = ($relevantProcs | ForEach-Object { "PID $($_.ProcessId): $($_.CommandLine)" }) -join "; "
            throw "等待 $TimeoutSeconds 秒后仍检测到残留 Unity.exe 进程未退出（$detail），本脚本不会代为结束——请先手工确认该进程状态（是否是人正在交互使用的 Editor 窗口）后重跑。"
        }
        Start-Sleep -Milliseconds 1000
    }
}

# -----------------------------------------------------------------------------
# 步骤汇总基础设施（惯例同 check.ps1，独立一份——两个脚本各自可以单独运行，不互相依赖）。
# -----------------------------------------------------------------------------
$script:Results = New-Object System.Collections.Generic.List[Object]

function Write-StepHeader {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)
    Write-StepHeader $Name
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $ok = $false
    $detail = ""
    try {
        $result = & $Action
        if ($null -eq $result) {
            $ok = $true
        } elseif ($result -is [bool]) {
            $ok = $result
        } elseif ($result -is [pscustomobject] -and ($result.PSObject.Properties.Name -contains "Ok")) {
            $ok = [bool]$result.Ok
            if (($result.PSObject.Properties.Name -contains "Detail") -and $result.Detail) {
                $detail = [string]$result.Detail
            }
        } else {
            $ok = [bool]$result
        }
    } catch {
        $ok = $false
        $detail = $_.Exception.Message
        Write-Host $detail -ForegroundColor Red
    }
    $sw.Stop()
    $seconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1)
    $script:Results.Add([PSCustomObject]@{ Step = $Name; Result = if ($ok) { "PASS" } else { "FAIL" }; Seconds = $seconds; Detail = $detail })
    if ($ok) {
        Write-Host "[$Name] 通过，用时 ${seconds}s" -ForegroundColor Green
    } else {
        Write-Host "[$Name] 失败，用时 ${seconds}s" -ForegroundColor Red
    }
    return $ok
}

# 见 check.ps1 Invoke-NativeAndWait 同款判断记录：Unity.exe / 独立版 Shell.exe 都是 GUI 子系统
# 程序，PowerShell 的 `&` 调用运算符不会阻塞等待其退出，必须用 Start-Process -Wait/-PassThru。
# 不限时分支不再用 -Wait：PS 5.1 的 -Wait 等待的是整个进程树，Unity 退出后其 VBCSCompiler 编译
# 服务子进程还会再存活约 600 秒，-Wait 会把这约 600 秒也一起等掉；改用 Process.WaitForExit()
# 只等 Unity 自己的进程句柄，不受子进程影响，详见 check.ps1 Invoke-NativeAndWait 上方 2026-09-06
# 判断记录。两个分支都不传 -NoNewWindow，原因同样是 PS 5.1 的已知限制：-PassThru 不配 -Wait 时
# 若再传 -NoNewWindow，读回的 ExitCode 是空字符串；GUI 子系统程序加 -batchmode 本就不会弹窗，
# 去掉 -NoNewWindow 不影响无人值守效果。
function Invoke-NativeAndWait {
    param([string]$Exe, [string[]]$ArgList, [int]$TimeoutSeconds = 0)
    if ($TimeoutSeconds -le 0) {
        $proc = Start-Process -FilePath $Exe -ArgumentList $ArgList -PassThru
        $proc.WaitForExit()
        $proc.Refresh()
        return [PSCustomObject]@{ ExitCode = $proc.ExitCode; TimedOut = $false }
    }
    $proc = Start-Process -FilePath $Exe -ArgumentList $ArgList -PassThru
    $exited = $proc.WaitForExit($TimeoutSeconds * 1000)
    if (-not $exited) {
        try { $proc.Kill() } catch { }
        return [PSCustomObject]@{ ExitCode = -1; TimedOut = $true }
    }
    $proc.Refresh()
    return [PSCustomObject]@{ ExitCode = $proc.ExitCode; TimedOut = $false }
}

# 判断记录：PowerShell 5.1 的 `-Encoding utf8` 恒带 BOM，Unity 的 manifest.json/package.json/
# asmdef JSON 解析器不容忍 BOM（实测复现："is not valid JSON: Non-whitespace before {[. Char: 65279"
# ——65279 正是 BOM 的码点）。本仓库既有的 package.json/asmdef 也一贯是不带 BOM 的 UTF-8（见
# build.ps1 判断记录），因此本脚本凡是要落地 Unity 会解析的 JSON/YAML 文本，一律用本函数写入，
# 不用 Set-Content -Encoding utf8。
function Set-Utf8NoBom {
    param([string]$Path, [string]$Content)
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Copy-TreeMirror {
    param([string]$SourceDir, [string]$DestDir)
    if (-not (Test-Path $SourceDir)) {
        return 0
    }
    if (-not (Test-Path $DestDir)) {
        New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
    }
    $count = 0
    Get-ChildItem -Path $SourceDir -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring((Resolve-Path $SourceDir).Path.Length).TrimStart('\', '/')
        $target = Join-Path $DestDir $relative
        $targetDir = Split-Path -Parent $target
        if (-not (Test-Path $targetDir)) {
            New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        }
        Copy-Item -Path $_.FullName -Destination $target -Force
        $count++
    }
    return $count
}

Write-Host "消费方演练：版本=$DistVersion，工作目录=$WorkDir，Unity=$ResolvedUnityExe" -ForegroundColor Cyan

$DistRoot = Join-Path $RepoRoot ("dist\" + $DistVersion)
$ConsumerProjectDir = Join-Path $WorkDir "ConsumerProject"
$ConsumerPackageStagingDir = Join-Path $WorkDir "packages\com.sample.game-consumer"
$UnityLogDir = Join-Path $WorkDir "logs"

# -----------------------------------------------------------------------------
# 1) 确保分发包快照存在（build.ps1 -SyncOnly -Dist <ver>，另起子进程跑，避免其 exit 语句
#    连带终止本脚本，惯例同 check.ps1 对 build.ps1 的调用方式）。
# -----------------------------------------------------------------------------
$step1 = Invoke-Step "build.ps1 -SyncOnly -Dist $DistVersion（确保分发包快照存在）" {
    $buildScript = Join-Path $RepoRoot "build.ps1"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript -SyncOnly -Dist $DistVersion
    return ($LASTEXITCODE -eq 0)
}
if (-not $step1) {
    Write-Host "分发包快照生成失败，后续步骤无法进行，提前退出。" -ForegroundColor Red
    $script:Results | Format-Table -AutoSize | Out-String -Width 4096 | Write-Host
    Stop-TranscriptAndReport -Failed $true
    exit 1
}

if (-not (Test-Path $DistRoot)) {
    Write-Host "分发包快照不存在：$DistRoot" -ForegroundColor Red
    Stop-TranscriptAndReport -Failed $true
    exit 1
}

# -----------------------------------------------------------------------------
# 2) 准备工作目录（全新工程，见 -SkipCleanWorkDir 参数说明）。
# -----------------------------------------------------------------------------
Invoke-Step "准备全新工作目录" {
    if ((-not $SkipCleanWorkDir) -and (Test-Path $WorkDir)) {
        Remove-Item -Path $WorkDir -Recurse -Force -Confirm:$false
    }
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
    New-Item -ItemType Directory -Force -Path $UnityLogDir | Out-Null
    $true
}

# -----------------------------------------------------------------------------
# 3) 复制并按 README 改名 games/_template -> com.sample.game-consumer（见
#    games/_template/README.md"复制为新游戏：改哪几处"1、2 两步——本脚本只做包名 +
#    四个 asmdef 的 name/引用两处改名，不改 C# 命名空间声明（任务书明确"命名空间不改"：
#    验证的是"两根合并加载 + Unity 包解析引用名"这条链路本身，不是把模板真的当一个新游戏来接）。
# -----------------------------------------------------------------------------
Invoke-Step "复制并改名 games/_template -> com.sample.game-consumer" {
    Copy-TreeMirror -SourceDir (Join-Path $DistRoot "games\_template") -DestDir $ConsumerPackageStagingDir | Out-Null

    # package.json：name 改名。
    $pkgJsonPath = Join-Path $ConsumerPackageStagingDir "package.json"
    $pkgJson = (Get-Content -Path $pkgJsonPath -Raw -Encoding UTF8) | ConvertFrom-Json
    $pkgJson.name = "com.sample.game-consumer"
    Set-Utf8NoBom -Path $pkgJsonPath -Content ($pkgJson | ConvertTo-Json -Depth 10)

    # 四个 asmdef：文件名 + JSON "name" 字段从 Game.Template(.Editor/.Tests/.EditorTests) 改成
    # Game.Consumer(.Editor/.Tests/.EditorTests)；Editor/Tests/Tests\Editor 三个 asmdef 的
    # references 数组里对 "Game.Template" 的引用同步改成 "Game.Consumer"（否则编辑器/测试程序集
    # 引用不到运行期程序集，见该 README 判断记录）。rootNamespace 与全部 .cs 文件的 namespace
    # 声明均不改（"命名空间不改"，见本步骤顶部注释）。
    $asmdefRenames = @(
        @{ Dir = "Runtime"; Old = "Game.Template.asmdef"; New = "Game.Consumer.asmdef" },
        @{ Dir = "Editor"; Old = "Game.Template.Editor.asmdef"; New = "Game.Consumer.Editor.asmdef" },
        @{ Dir = "Tests\Runtime"; Old = "Game.Template.Tests.asmdef"; New = "Game.Consumer.Tests.asmdef" },
        @{ Dir = "Tests\Editor"; Old = "Game.Template.EditorTests.asmdef"; New = "Game.Consumer.EditorTests.asmdef" }
    )
    foreach ($rename in $asmdefRenames) {
        $oldPath = Join-Path $ConsumerPackageStagingDir ($rename.Dir + "\" + $rename.Old)
        $newPath = Join-Path $ConsumerPackageStagingDir ($rename.Dir + "\" + $rename.New)
        $asmdefObj = (Get-Content -Path $oldPath -Raw -Encoding UTF8) | ConvertFrom-Json
        $asmdefObj.name = $asmdefObj.name -replace "Game\.Template", "Game.Consumer"
        if ($asmdefObj.PSObject.Properties.Name -contains "references") {
            $asmdefObj.references = @($asmdefObj.references | ForEach-Object { $_ -replace "Game\.Template", "Game.Consumer" })
        }
        Set-Utf8NoBom -Path $newPath -Content ($asmdefObj | ConvertTo-Json -Depth 10)
        Remove-Item -Path $oldPath -Force
        # .meta 也需要同步改名（保留原 GUID，只改文件名部分），否则 Unity 认不出这是同一份资产的
        # 改名而不是"删一个、新增一个"（后者会分配新 GUID，问题不大，但保留原 GUID 更干净）。
        $oldMeta = $oldPath + ".meta"
        if (Test-Path $oldMeta) {
            Move-Item -Path $oldMeta -Destination ($newPath + ".meta") -Force
        }
    }

    Test-Path (Join-Path $ConsumerPackageStagingDir "Runtime\Game.Consumer.asmdef")
}

# -----------------------------------------------------------------------------
# 4) 创建空白 Unity 工程骨架（不用 -createProject——那会新建一份带默认模板包集合的工程，
#    之后还要手工摘掉一堆用不上的默认依赖；改为手写最小 ProjectSettings/Packages/manifest.json
#    骨架，与 games/_template/README.md"在新游戏的 Unity 工程中引用两个包"一节描述的最小前提
#    条件一致——一个"已经存在、只是刚起步"的 Unity 工程）。
# -----------------------------------------------------------------------------
Invoke-Step "创建消费方工程骨架" {
    New-Item -ItemType Directory -Force -Path (Join-Path $ConsumerProjectDir "Assets") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $ConsumerProjectDir "Packages") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $ConsumerProjectDir "ProjectSettings") | Out-Null

    # ProjectSettings：直接复制工作台工程的 ProjectSettings/ + Assets/Settings/（渲染管线/输入
    # 系统等工程级配置不是包依赖能表达的——尤其 ProjectSettings.asset 的 activeInputHandler
    # 必须是 Input System Package，否则 UnityUISurface 挂的 InputSystemUIInputModule 会与旧版
    # StandaloneInputModule 冲突抛异常，见该类型顶部判断记录；URP 的 2D Renderer 资产
    # （Assets/Settings/URP-2D-Pipeline.asset + Renderer2DData.asset）同样是 GraphicsSettings.asset
    # 按 GUID 引用的具体资产文件，必须随工程设置一并带过去）——与 adapters/unity 工作台工程保持
    # 一致的配置基线，避免从 Unity 默认模板起步时这些设置缺失导致的一整类不相关失败。
    $sourceProjectSettings = Join-Path $RepoRoot "adapters\unity\ProjectSettings"
    Copy-TreeMirror -SourceDir $sourceProjectSettings -DestDir (Join-Path $ConsumerProjectDir "ProjectSettings") | Out-Null
    Copy-TreeMirror -SourceDir (Join-Path $RepoRoot "adapters\unity\Assets\Settings") -DestDir (Join-Path $ConsumerProjectDir "Assets\Settings") | Out-Null

    # 判断记录：EditorBuildSettings.asset 是随上面整份 ProjectSettings 复制过来的工作台工程配置，
    # 里面登记的场景路径（GreyBox.unity/Shell.unity 等）在消费方工程里根本不存在——留着只会让
    # Build Settings 列表挂着几条失效引用，替换成一份空列表，改由第 7 步的场景构建器自己登记本
    # 工程真正拥有的两个场景。
    $emptyBuildSettings = @"
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1045 &1
EditorBuildSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Scenes: []
  m_configObjects: {}
"@
    Set-Utf8NoBom -Path (Join-Path $ConsumerProjectDir "ProjectSettings\EditorBuildSettings.asset") -Content $emptyBuildSettings

    $adapterDistPath = (Join-Path $DistRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity") -replace '\\', '/'
    $consumerPackagePath = $ConsumerPackageStagingDir -replace '\\', '/'

    # 判断记录（内置模块清单从工作台 manifest.json 派生，不在本脚本里另抄一份）：Unity 的
    # Audio/ParticleSystem/Tilemap 等"内置模块"必须显式出现在 Packages/manifest.json 的
    # dependencies 里才能被引用到（不是随编辑器自动可用，实测复现：漏了这些条目时
    # UnityAudio.cs/UnityRenderer2D.cs/UnityNavigation2D.cs 里对应类型全部报
    # "could not be found...Enable the built in package"）。工作台工程
    # adapters/unity/Packages/manifest.json 的 dependencies 已经是一份验证过可行的完整集合
    # （含 com.unity.test-framework，-runTests 需要），直接读取复用、只替换
    # "com.gamefoundation.game-template" 这一条为消费方自己的两个 file: 引用，避免本脚本另抄一份
    # 清单、两处以后各自漂移。
    $workbenchManifestPath = Join-Path $RepoRoot "adapters\unity\Packages\manifest.json"
    $workbenchManifest = (Get-Content -Path $workbenchManifestPath -Raw -Encoding UTF8) | ConvertFrom-Json

    # ADR-0160：消费方开发期装上可选的手感实验室 Unity 包（取自 dist 的 packages\ 目录，版本与依赖已写好），
    # 这样第 9 步"独立版里没有任何实验室内容"的断言才有意义：包在工程里、编辑器里能用，构建产物里一个字节也没有。
    $feelLabUnityDistPath = (Join-Path $DistRoot "packages\com.gamefoundation.feel-lab.unity") -replace '\\', '/'
    $dependencies = [ordered]@{
        "com.gamefoundation.adapter.unity" = "file:$adapterDistPath"
        "com.gamefoundation.feel-lab.unity" = "file:$feelLabUnityDistPath"
        "com.sample.game-consumer" = "file:$consumerPackagePath"
    }
    # com.gamefoundation.game-template：工作台自己的 file: 相对路径引用，消费方用改名后的
    # com.sample.game-consumer 取代。
    # com.gamefoundation.conformance：契约一致性场景源码，工作台专属测试依赖（见
    # adapters/conformance/README.md 判断记录 3——"仅供 adapters/unity 这个引擎适配层工作台工程
    # 消费"），且是 file: 相对路径引用，原样复制到消费方工程会按消费方工程的物理位置重新解析、
    # 指向一个不存在的路径（实测复现：解析成 "<TEMP 根>\adapters\conformance"）。消费方不需要
    # 这个包，两者都跳过、不带进消费方 manifest。
    $workbenchOnlyPackages = @("com.gamefoundation.game-template", "com.gamefoundation.conformance")
    foreach ($prop in $workbenchManifest.dependencies.PSObject.Properties) {
        if ($workbenchOnlyPackages -contains $prop.Name) {
            continue
        }
        $dependencies[$prop.Name] = $prop.Value
    }

    $manifest = [ordered]@{
        dependencies = $dependencies
        testables = @("com.sample.game-consumer")
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 10
    Set-Utf8NoBom -Path (Join-Path $ConsumerProjectDir "Packages\manifest.json") -Content $manifestJson

    Test-Path (Join-Path $ConsumerProjectDir "Packages\manifest.json")
}

# -----------------------------------------------------------------------------
# 5) 复制框架级数据 + 模板自带数据集到 StreamingAssets（同 build.ps1 内容同步步骤的相同路径约定）
#    + TextMeshPro 运行期资源 + 占位字体（见 games/_template/README.md 新增第 7 步）。
# -----------------------------------------------------------------------------
Invoke-Step "同步内容数据集 + TextMeshPro 运行期资源到消费方工程" {
    $streamingRoot = Join-Path $ConsumerProjectDir "Assets\StreamingAssets\GameFoundation"
    $c1 = Copy-TreeMirror -SourceDir (Join-Path $DistRoot "data\_framework") -DestDir (Join-Path $streamingRoot "data\_framework")
    # 手感接入补缺（M1 收口）：分发包里的 data/_feel 是与 data/_framework 并列的框架根；模板在 GameOptions.FeelOptions 开启时
    # 自动把它接进数据加载顺序，消费方照模板 README"手感开关与数据根"一节把它同步进 StreamingAssets，与 data/_framework 同一约定。
    $c6 = Copy-TreeMirror -SourceDir (Join-Path $DistRoot "data\_feel") -DestDir (Join-Path $streamingRoot "data\_feel")
    $c2 = Copy-TreeMirror -SourceDir (Join-Path $ConsumerPackageStagingDir "data\game") -DestDir (Join-Path $streamingRoot "data\game")

    $tmpDest = Join-Path $ConsumerProjectDir "Assets\TextMesh Pro"
    $c3 = Copy-TreeMirror -SourceDir (Join-Path $DistRoot "assets\textmesh_pro_essentials") -DestDir $tmpDest

    $fontsSrc = Join-Path $DistRoot "assets\_placeholder\fonts"
    $fontsDestDir = Join-Path $ConsumerProjectDir "Assets\Framework\Resources\Fonts"
    New-Item -ItemType Directory -Force -Path $fontsDestDir | Out-Null
    $c4 = 0
    if (Test-Path $fontsSrc) {
        Get-ChildItem -Path $fontsSrc -File | Where-Object { $_.Extension -eq ".otf" -or $_.Extension -eq ".ttf" } | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination (Join-Path $fontsDestDir $_.Name) -Force
            $c4++
        }
    }

    # 加固J3 新增：占位场景/导航资源文件（供 Core.Foundation.SceneRouter.SceneRouter.LoadScene
    # 通过 games/_template 的 world.template_field 记录里 scene_ref="scene.template_field"/
    # nav_ref="nav.template_field" 分别以 ResourceKind.Scene/ResourceKind.NavMesh 解析出的
    # UnityResourceLoader 路径 StreamingAssets/GameFoundation/scene/template_field.json、
    # StreamingAssets/GameFoundation/nav_mesh/template_field.json 各自找到一个可读文件；内容不重要
    # （SceneRouter 只要求文件存在且可解码为文本，从不解析其内容），惯例与文件格式完全照抄
    # build.ps1"占位场景/导航资源文件"一节判断记录——但 build.ps1 只把这两个文件直接生成进工作台
    # 自己的 Assets/StreamingAssets/GameFoundation/（构建期产物，不进 dist 快照，见该判断记录"为
    # 什么在 build.ps1 生成而不是放进 data/_sample 或 assets/_placeholder"），consumer_smoke.ps1
    # 搭建的是一个全新的、独立于工作台的消费方工程，不会经过工作台那次 build.ps1 运行，因此这两个
    # 文件必须在本脚本里另外生成一份，否则 -gf-smoke-template 冒烟第一次 "新游戏" 就会卡在
    # SceneRouter.LoadScene 这一步（Page 停在 MainMenu 不再前进，见任务实跑复现：加固J3 把
    # consumer_smoke.ps1 第 10 步从"限时引导自检"改成真正驱动新游戏之前，这个缺口从未被这条演练
    # 流程实际触达过，因此一直没有暴露）。
    $sceneResourceDir = Join-Path $streamingRoot "scene"
    New-Item -ItemType Directory -Force -Path $sceneResourceDir | Out-Null
    $navResourceDir = Join-Path $streamingRoot "nav_mesh"
    New-Item -ItemType Directory -Force -Path $navResourceDir | Out-Null
    $placeholderResourceContent = '{"_placeholder":true,"_note":"SceneRouter 场景/导航资源占位字节，内容不被解析，见 build.ps1/consumer_smoke.ps1 判断记录"}'
    Set-Utf8NoBom -Path (Join-Path $sceneResourceDir "template_field.json") -Content $placeholderResourceContent
    Set-Utf8NoBom -Path (Join-Path $navResourceDir "template_field.json") -Content $placeholderResourceContent
    $c5 = 2

    [PSCustomObject]@{
        Ok = ($c1 -gt 0) -and ($c2 -gt 0) -and ($c3 -gt 0) -and ($c4 -gt 0) -and ($c5 -eq 2) -and ($c6 -gt 0)
        Detail = "data/_framework=$c1 files, data/_feel=$c6 files, data/game=$c2 files, TMP essentials=$c3 files, fonts=$c4 files, scene/nav placeholders=$c5 files"
    }
}

# -----------------------------------------------------------------------------
# 5b) 手感实验室随发布产物分发的消费方验收（手感落地 M2-D；06 第 8 节第 2 步"用实验室在自己的数据上校准手感"）：
#     只消费发布产物的游戏要能自己跑实验室。这一步在消费方工作目录里搭一个"实验室根"（工作目录下同时有
#     data/_framework、data/_feel、data/_lab、data/_lab_action、data/_equip、lab/fixtures——脚本里的 extraDataRoots 与默认夹具目录
#     都按工作目录相对解析），框架数据与手感数据取自上一步已经同步进消费方工程的那两份（消费方自己的副本，
#     不回头读仓库），实验室数据集、占位装备集与夹具取自 dist 快照，然后用 dist 里的预编译命令行
#     `dist\<版本>\packages\com.gamefoundation.feel-lab.headless\Tools~\feellab\bin\FeelLab.dll`（ADR-0160 起命令行随可选包
#     feel-lab.headless 分发，实验室数据集/夹具取自该包自带的 labroot）跑 `suite`（全部标准脚本 x 六个格子对基线）与 `invariants`
#     （跨格子不变量）：二者都必须退出码 0 且 RESULT 行无差异。命令行不需要 Unity，只需要 dotnet 运行时；
#     实验室根放在 ConsumerProject 之外（WorkDir\feellab_root），不会被 Unity 当 Assets 导入。
#     判断记录：跑完整 suite 而不是子集——全量约几秒（数据与脚本都很小），子集反而要多维护一份过滤约定；
#     不跑 `--update-baseline`、不写 lab/out 以外的任何东西（suite 只读基线）。
# -----------------------------------------------------------------------------
Invoke-Step "手感实验室（dist 内 feellab 预编译命令行）在消费方工作目录跑 suite + invariants" {
    $feelLabPkgDir = Join-Path $DistRoot "packages\com.gamefoundation.feel-lab.headless\Tools~\feellab"
    $feelLabDll = Join-Path $feelLabPkgDir "bin\FeelLab.dll"
    if (-not (Test-Path $feelLabDll)) {
        return [PSCustomObject]@{ Ok = $false; Detail = "分发包里没有预编译的手感实验室命令行：$feelLabDll" }
    }
    $streamingRoot = Join-Path $ConsumerProjectDir "Assets\StreamingAssets\GameFoundation"
    $labRoot = Join-Path $WorkDir "feellab_root"
    $copied = 0
    $copied += Copy-TreeMirror -SourceDir (Join-Path $streamingRoot "data\_framework") -DestDir (Join-Path $labRoot "data\_framework")
    $copied += Copy-TreeMirror -SourceDir (Join-Path $streamingRoot "data\_feel") -DestDir (Join-Path $labRoot "data\_feel")
    # 可选包自带的自包含实验室根（先定下来，下面的模板根与实验室数据都取自它）。
    $packagedLabRoot = Join-Path $feelLabPkgDir "labroot"
    # 默认手感模板根（ADR-0142）：标准脚本 feel_tpl_* 把它声明为额外数据根，取自 dist 快照（不经 StreamingAssets）。
    $copied += Copy-TreeMirror -SourceDir (Join-Path $packagedLabRoot "data\_feel_templates") -DestDir (Join-Path $labRoot "data\_feel_templates")
    # 实验室数据集、动作数据、占位装备集、标准脚本与基线夹具：取自可选包自带的自包含实验室根（dist 主树里不再有这些）。
    foreach ($labPart in @("data\_lab", "data\_lab_action", "data\_equip", "lab\fixtures")) {
        $copied += Copy-TreeMirror -SourceDir (Join-Path $packagedLabRoot $labPart) -DestDir (Join-Path $labRoot $labPart)
    }
    foreach ($needed in @("data\_framework", "data\_feel", "data\_feel_templates", "data\_lab", "data\_lab_action", "data\_equip", "lab\fixtures\scripts", "lab\fixtures\baselines")) {
        if (-not (Test-Path (Join-Path $labRoot $needed))) {
            return [PSCustomObject]@{ Ok = $false; Detail = "实验室根缺少 $needed（分发包或消费方数据目录不完整）" }
        }
    }

    $logPath = Join-Path $UnityLogDir "feellab.log"
    $summaries = @()
    $allOk = $true
    $previousErrorAction = $ErrorActionPreference
    Push-Location $labRoot
    try {
        foreach ($command in @("suite", "invariants")) {
            # PS 5.1 下 $ErrorActionPreference=Stop 时，合并 stderr（2>&1）的原生命令一旦写 stderr 就会被当成终止错误；
            # 命令行出错时（退出码非 0）恰恰会写 stderr，这里临时放宽，让失败走下面的退出码/RESULT 判定并带上输出末尾。
            $ErrorActionPreference = "Continue"
            $text = (& dotnet $feelLabDll $command 2>&1 | Out-String)
            $exitCode = $LASTEXITCODE
            $ErrorActionPreference = $previousErrorAction
            Add-Content -Path $logPath -Value ("==== feellab " + $command + "（退出码 " + $exitCode + "）====`r`n" + $text) -Encoding UTF8
            if ($command -eq "suite") {
                $m = [regex]::Match($text, 'RESULT total=(\d+) pass=(\d+) diff=(\d+) missing=(\d+) not_runnable=(\d+)')
                $good = $m.Success -and ($m.Groups[1].Value -ne "0") -and ($m.Groups[1].Value -eq $m.Groups[2].Value) `
                    -and ($m.Groups[3].Value -eq "0") -and ($m.Groups[4].Value -eq "0") -and ($m.Groups[5].Value -eq "0")
                $summaries += $(if ($m.Success) { "suite " + $m.Value } else { "suite 未找到 RESULT 行" })
            } else {
                $m = [regex]::Match($text, 'RESULT invariants total=(\d+) pass=(\d+) fail=(\d+)')
                $good = $m.Success -and ($m.Groups[1].Value -ne "0") -and ($m.Groups[1].Value -eq $m.Groups[2].Value) -and ($m.Groups[3].Value -eq "0")
                $summaries += $(if ($m.Success) { "invariants " + $m.Value } else { "invariants 未找到 RESULT 行" })
            }
            if (($exitCode -ne 0) -or (-not $good)) {
                $allOk = $false
                Write-Host ("feellab " + $command + " 未通过（退出码 " + $exitCode + "），输出末尾：") -ForegroundColor Red
                Write-Host (($text -split "`r?`n" | Select-Object -Last 15) -join "`n")
            }
        }
    } finally {
        Pop-Location
    }
    [PSCustomObject]@{
        Ok = $allOk
        Detail = (($summaries -join "；") + "；实验室根=" + $labRoot + "（拷入 $copied 个文件），日志 " + $logPath)
    }
}

# -----------------------------------------------------------------------------
# 6) 首次批处理编译（解析新包依赖 + 首次 Library 导入，正常耗时较长）：0 编译错误。
# -----------------------------------------------------------------------------
Invoke-Step "首次批处理编译（包解析 + 0 编译错误）" {
    Wait-NoResidualUnityProcess
    $log = Join-Path $UnityLogDir "01_compile.log"
    $proc = Invoke-NativeAndWait -Exe $ResolvedUnityExe -ArgList @(
        "-batchmode", "-nographics", "-quit",
        "-projectPath", $ConsumerProjectDir,
        "-logFile", $log
    ) -TimeoutSeconds 900
    if ($proc.TimedOut) {
        # 超时被强杀（退出码 -1）时同样按签名留证：日志含 IPC 断流签名才抓，否则不留证、不改 Detail。
        $upmNote = Get-UpmEvidenceDetailSuffix -EngineLogPath $log -EngineExitCode $proc.ExitCode -EvidenceRoot $UpmEvidenceRoot -Tag "consumer_01_compile"
        return [PSCustomObject]@{ Ok = $false; Detail = "首次编译超过 900s 未完成（可能是包解析卡住），见 $log$upmNote" }
    }
    $errorLines = @()
    if (Test-Path $log) {
        $errorLines = Select-String -Path $log -Pattern "error CS" -SimpleMatch:$false -ErrorAction SilentlyContinue
    }
    $upmNote = Get-UpmEvidenceDetailSuffix -EngineLogPath $log -EngineExitCode $proc.ExitCode -EvidenceRoot $UpmEvidenceRoot -Tag "consumer_01_compile"
    [PSCustomObject]@{
        Ok = ($proc.ExitCode -eq 0) -and ($errorLines.Count -eq 0)
        Detail = "Unity 退出码 $($proc.ExitCode)，error CS 命中 $($errorLines.Count) 处，见 $log$upmNote"
    }
}

# -----------------------------------------------------------------------------
# 7) 用模板的场景构建器生成场景（-executeMethod，命名空间未改，仍是 Game.Template.EditorTools）。
# -----------------------------------------------------------------------------
Invoke-Step "用场景构建器生成 Shell + Map 场景" {
    Wait-NoResidualUnityProcess
    $log = Join-Path $UnityLogDir "02_scene_builder.log"
    $proc = Invoke-NativeAndWait -Exe $ResolvedUnityExe -ArgList @(
        "-batchmode", "-nographics", "-quit",
        "-projectPath", $ConsumerProjectDir,
        "-executeMethod", "Game.Template.EditorTools.GameSceneBuilder.BuildAll",
        "-logFile", $log
    ) -TimeoutSeconds 300
    if ($proc.TimedOut) {
        # 超时被强杀（退出码 -1）时同样按签名留证：日志含 IPC 断流签名才抓，否则不留证、不改 Detail。
        $upmNote = Get-UpmEvidenceDetailSuffix -EngineLogPath $log -EngineExitCode $proc.ExitCode -EvidenceRoot $UpmEvidenceRoot -Tag "consumer_02_scene_builder"
        return [PSCustomObject]@{ Ok = $false; Detail = "场景生成超过 300s 未完成，见 $log$upmNote" }
    }
    $shellScene = Join-Path $ConsumerProjectDir "Assets\Framework\Scenes\GameTemplateShell.unity"
    $mapScene = Join-Path $ConsumerProjectDir "Assets\Framework\Scenes\GameTemplateMap.unity"
    $upmNote = Get-UpmEvidenceDetailSuffix -EngineLogPath $log -EngineExitCode $proc.ExitCode -EvidenceRoot $UpmEvidenceRoot -Tag "consumer_02_scene_builder"
    [PSCustomObject]@{
        Ok = ($proc.ExitCode -eq 0) -and (Test-Path $shellScene) -and (Test-Path $mapScene)
        Detail = "Unity 退出码 $($proc.ExitCode)，Shell 场景存在=$(Test-Path $shellScene)，Map 场景存在=$(Test-Path $mapScene)，见 $log$upmNote"
    }
}

# -----------------------------------------------------------------------------
# 8) 跑模板的 PlayMode 测试（-testFilter 限定到 Game.Template.Tests 命名空间）：全过。
# -----------------------------------------------------------------------------
Invoke-Step "模板 PlayMode 测试（-testFilter Game.Template.Tests）" {
    Wait-NoResidualUnityProcess
    $resultsXml = Join-Path $UnityLogDir "03_playmode.xml"
    $log = Join-Path $UnityLogDir "03_playmode.log"
    $proc = Invoke-NativeAndWait -Exe $ResolvedUnityExe -ArgList @(
        "-batchmode",
        "-projectPath", $ConsumerProjectDir,
        "-runTests", "-testPlatform", "PlayMode",
        "-testFilter", "Game.Template.Tests",
        "-testResults", $resultsXml,
        "-logFile", $log
    ) -TimeoutSeconds 600
    if ($proc.TimedOut) {
        # 超时被强杀（退出码 -1）时同样按签名留证：日志含 IPC 断流签名才抓，否则不留证、不改 Detail。
        $upmNote = Get-UpmEvidenceDetailSuffix -EngineLogPath $log -EngineExitCode $proc.ExitCode -EvidenceRoot $UpmEvidenceRoot -Tag "consumer_03_playmode"
        return [PSCustomObject]@{ Ok = $false; Detail = "PlayMode 测试超过 600s 未完成，见 $log$upmNote" }
    }
    # 引擎非零退出且日志含包管理器 IPC 断流签名时留证（只追加 Detail 后缀，不改判定）。
    $upmNote = Get-UpmEvidenceDetailSuffix -EngineLogPath $log -EngineExitCode $proc.ExitCode -EvidenceRoot $UpmEvidenceRoot -Tag "consumer_03_playmode"
    if (-not (Test-Path $resultsXml)) {
        return [PSCustomObject]@{ Ok = $false; Detail = "未生成结果 XML：$resultsXml，Unity 退出码 $($proc.ExitCode)，见 $log$upmNote" }
    }
    # 按 XML 声明（utf-8）读字节，不用 Get-Content -Raw（5.1 按 ANSI 解码会让中文 CDATA 吞掉 "]]>"，同
    # toolchain/_gate_unity_verdicts.ps1 的 Get-UnityTestRunVerdict）。
    $xml = New-Object System.Xml.XmlDocument
    $xml.Load((Resolve-Path -LiteralPath $resultsXml).ProviderPath)
    $root = $xml.DocumentElement
    [PSCustomObject]@{
        Ok = ($root.result -eq "Passed")
        Detail = "total=$($root.total) passed=$($root.passed) failed=$($root.failed)$upmNote"
    }
}

# -----------------------------------------------------------------------------
# 9) 构建独立版。
# -----------------------------------------------------------------------------
$exePath = Join-Path $UnityLogDir "ConsumerShell.exe"
$playerDataDir = [System.IO.Path]::Combine($UnityLogDir, ([System.IO.Path]::GetFileNameWithoutExtension($exePath) + "_Data"))
$buildOk = Invoke-Step "构建独立版" {
    Wait-NoResidualUnityProcess
    $log = Join-Path $UnityLogDir "04_build.log"
    # 消费方反馈第 72 条根治：默认（不传 -DevelopmentBuild）仍是内置 -buildWindows64Player 开关，
    # 与本参数新增之前完全一致；传了才改走 WindowsPlayerBuilder 自定义 -executeMethod，带
    # -gfDevelopmentBuild 让其按 BuildOptions.Development 构建（见该类型头判断记录、
    # .PARAMETER DevelopmentBuild 说明）。
    if ($DevelopmentBuild) {
        $buildArgList = @(
            "-batchmode", "-nographics", "-quit",
            "-projectPath", $ConsumerProjectDir,
            "-executeMethod", "Game.Template.EditorTools.WindowsPlayerBuilder.BuildWindows64Player",
            "-gfOutputPath", $exePath,
            "-gfDevelopmentBuild",
            "-logFile", $log
        )
    } else {
        $buildArgList = @(
            "-batchmode", "-nographics", "-quit",
            "-projectPath", $ConsumerProjectDir,
            "-buildWindows64Player", $exePath,
            "-logFile", $log
        )
    }
    $proc = Invoke-NativeAndWait -Exe $ResolvedUnityExe -ArgList $buildArgList -TimeoutSeconds 600
    if ($proc.TimedOut) {
        # 超时被强杀（退出码 -1）时同样按签名留证：日志含 IPC 断流签名才抓，否则不留证、不改 Detail。
        $upmNote = Get-UpmEvidenceDetailSuffix -EngineLogPath $log -EngineExitCode $proc.ExitCode -EvidenceRoot $UpmEvidenceRoot -Tag "consumer_04_build"
        return [PSCustomObject]@{ Ok = $false; Detail = "独立版构建超过 600s 未完成，见 $log$upmNote" }
    }
    $upmNote = Get-UpmEvidenceDetailSuffix -EngineLogPath $log -EngineExitCode $proc.ExitCode -EvidenceRoot $UpmEvidenceRoot -Tag "consumer_04_build"
    $built = ($proc.ExitCode -eq 0) -and (Test-Path $exePath)
    # ADR-0160：消费方工程装了手感实验室 Unity 包，独立版里却不得有它的任何程序集/数据/资源（包自己的"只在编辑器编译"约束兑现）。
    $boundaryProblems = @()
    if ($built) {
        $boundaryProblems = @(Get-PlayerBoundaryProblems -BuildDir $playerDataDir)
        foreach ($bp in $boundaryProblems) { Write-Host $bp -ForegroundColor Red }
    }
    [PSCustomObject]@{
        Ok = $built -and ($boundaryProblems.Count -eq 0)
        Detail = "Unity 构建退出码 $($proc.ExitCode)，DevelopmentBuild=$($DevelopmentBuild.IsPresent)，产物存在=$(Test-Path $exePath)，独立版边界问题=$($boundaryProblems.Count)（已装 feel-lab.unity，独立版须不含实验室/演示内容），见 $log$upmNote"
    }
}

# -----------------------------------------------------------------------------
# 10) 无人值守冒烟（加固J3：games/_template 新增 Game.Template.TemplateSmokeRunner，
#     "-gf-smoke-template" 命令行标志驱动，日志格式/退出码约定沿用 Adapter.Unity.Shell.SmokeRunner
#     ——见该类型头注释判断记录。命令行标志与工作台的 "-gf-smoke" 不同，理由同样见该类型头注释：
#     Game.Template.asmdef 引用了 Adapter.Unity，独立版产物里两个 SmokeRunner 类型的静态钩子同处
#     一个进程，沿用同一个标志字符串会撞车）。真正驱动一遍"数据零阻断 → 主菜单 → 新游戏 → 进图 →
#     移动 1 秒 → 存档 slot.smoke → 读档 → 退出"，断言日志出现 "RESULT=OK"，不再是限时观察 + 日志
#     关键字排除法的"引导自检"退化版本。
# -----------------------------------------------------------------------------
if ($buildOk) {
    Invoke-Step "-gf-smoke-template 无人值守冒烟" {
        $log = Join-Path $UnityLogDir "05_smoke_template.log"
        $proc = Invoke-NativeAndWait -Exe $exePath -TimeoutSeconds 60 -ArgList @(
            "-batchmode", "-gf-smoke-template",
            "-logFile", $log,
            "-screen-width", "800", "-screen-height", "600"
        )
        if ($proc.TimedOut) {
            return [PSCustomObject]@{ Ok = $false; Detail = "-gf-smoke-template 冒烟超过 60s 未退出，已强制结束（可能挂死），见 $log" }
        }
        if ($proc.ExitCode -ne 0) {
            return [PSCustomObject]@{ Ok = $false; Detail = "独立版退出码 $($proc.ExitCode)，见 $log" }
        }
        if (-not (Test-Path $log)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成冒烟日志：$log" }
        }
        $logText = Get-Content -Path $log -Raw
        [PSCustomObject]@{
            Ok = ($logText -match "\[GF-SMOKE\] RESULT=OK")
            Detail = "见 $log"
        }
    } | Out-Null
} else {
    Write-StepHeader "-gf-smoke-template 无人值守冒烟"
    Write-Host "已跳过：上一步独立版构建未成功，没有可运行的产物。" -ForegroundColor Yellow
    $script:Results.Add([PSCustomObject]@{ Step = "-gf-smoke-template 无人值守冒烟"; Result = "SKIP"; Seconds = 0; Detail = "独立版构建未成功" })
}

# -----------------------------------------------------------------------------
# 11) registry 安装形态下 additive 占位材质冒烟（消费方反馈第二十四批根治，ADR-0074"落地缺陷与
#     修复"小节）：上面 1~10 步自始至终只用 file: 引用本框架分发包（见步骤 4 判断记录），这正是
#     ADR-0074 当初交付时"additive 占位材质在经本地 npm 私服注册表安装的不可变包形态下取不到"这个
#     缺陷从未被本脚本拦住的根本原因——file: 引用与"注册表安装"在 Unity Package Manager 内部走的
#     不是同一套包解析/资产处理路径（实测复现：同一份内容，file: 形态下 Resources.Load 能取到，
#     registry 形态下取不到；这是 Unity 自身对"registry 来源只读包"内 Resources 文件夹的既有限制，
#     不是本仓库打包缺陷——发布出去的 .tgz 内 .mat/.mat.meta/.shader/.shader.meta 四个文件路径与
#     GUID 均正确，AssetDatabase 也确实完成了导入，见 EffectSequencePlayer.GetAdditiveMaterial
#     判断记录）。本步骤专门补上这条路径：不复用上面的 games/_template 全量工程（避免为验证一个
#     材质承受模板全量编译/PlayMode/构建独立版的耗时），另起一个独立、轻量的消费方工程，用本地
#     Verdaccio 私服 + 按版本号（不是 file:）引用 com.gamefoundation.adapter.unity，驱动一次真实的
#     EffectSequencePlayer.Play(..., VfxBlendMode.Additive)，断言最终渲染材质的着色器确实是
#     GameFoundation/Vfx/AdditiveUnlit（不是降级后的默认材质）——这是本脚本里唯一真正走过"注册表
#     安装"这条链路的步骤，防的就是同一类缺陷（"只验证过 file:/内嵌形态"）再次经这条脚本溜过去。
# -----------------------------------------------------------------------------
Invoke-Step "registry 安装形态下 additive 占位材质冒烟" {
    $registryJsonPath = Join-Path $RepoRoot "toolchain\registry\registry.json"
    if (-not (Test-Path $registryJsonPath)) {
        return [PSCustomObject]@{ Ok = $false; Detail = "找不到 $registryJsonPath，无法解析私服地址" }
    }
    $registryUrl = ((Get-Content -Path $registryJsonPath -Raw -Encoding UTF8) | ConvertFrom-Json).url
    $registryNpmrcPath = Join-Path $RepoRoot "toolchain\registry\.npmrc"
    $startRegistryScript = Join-Path $RepoRoot "toolchain\registry\start_registry.ps1"

    function Test-LocalRegistryPing {
        param([string]$Url)
        try {
            $resp = Invoke-WebRequest -Uri ($Url.TrimEnd('/') + "/-/ping") -UseBasicParsing -TimeoutSec 5
            return $resp.StatusCode -eq 200
        } catch {
            return $false
        }
    }

    # 判断记录（本脚本自己负责把私服"带起来"，不假设调用方已经起好）：私服是本机常驻服务，可能
    # 早已被人手工起过（见 toolchain/registry/README.md"快速开始"），也可能全新环境从未起过——两种
    # 情况都要能跑通，因此先探活，没探到才 -Detach 起一个，不重复启动已经在跑的实例。
    #
    # 判断记录（2026-10-01 冷启动挂起根治，全量回归实测卡 16 分钟）：此前这里写的是
    # `& powershell ... start_registry.ps1 -Detach | Out-Null`。start_registry.ps1 -Detach 会
    # Start-Process 拉起一个常驻的 node（verdaccio）后台进程，该进程继承了这条 `| Out-Null` 管道的
    # 写端句柄；start_registry.ps1 自己早就退出了，但管道写端还被常驻的 node 攥着，`Out-Null` 永远
    # 等不到管道关闭（EOF），整个演练脚本就此无限挂起。只有"私服此前没在跑、需要现起"才会走到这一
    # 行，所以本机私服常驻时门禁一直是绿的，冷启动环境（新机器、私服刚被停掉）才必现。
    # 修法：不经管道。用 Start-Process 把 start_registry.ps1 -Detach 作为一个独立进程拉起（不重定向
    # 任何输出，因此不存在需要继承的管道句柄），只对这个辅助进程本身带超时地等它退出
    # （Process.WaitForExit(毫秒)，不等它的后代），随后仍由下面的探活循环（30 秒超时）判定私服是否
    # 真正起来。辅助进程超时未退出时只杀这个辅助进程，不碰 node。
    if (-not (Test-LocalRegistryPing -Url $registryUrl)) {
        Write-Host "  本地私服未响应，尝试 -Detach 启动：$startRegistryScript" -ForegroundColor DarkGray
        $startRegistryProc = Start-Process -FilePath "powershell" -PassThru -WindowStyle Hidden -ArgumentList @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $startRegistryScript, "-Detach")
        if (-not $startRegistryProc.WaitForExit(60000)) {
            Write-Host "  start_registry.ps1 -Detach 60 秒未退出，强制结束该辅助进程（不影响已拉起的 node），继续探活" -ForegroundColor Yellow
            try { $startRegistryProc.Kill() } catch { }
        }
        $deadline = (Get-Date).AddSeconds(30)
        while (-not (Test-LocalRegistryPing -Url $registryUrl)) {
            if ((Get-Date) -ge $deadline) {
                return [PSCustomObject]@{ Ok = $false; Detail = "启动私服后 30 秒仍未探活：$registryUrl" }
            }
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not (Test-Path $registryNpmrcPath)) {
        Write-Host "  找不到发布账号凭据，尝试跑一次 init_publisher.ps1" -ForegroundColor DarkGray
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoRoot "toolchain\registry\init_publisher.ps1") | Out-Null
        if (-not (Test-Path $registryNpmrcPath)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "init_publisher.ps1 跑完后仍找不到 $registryNpmrcPath" }
        }
    }

    # 判断记录（发布前先尽力 unpublish 一次，忽略结果）：本步骤用的版本号与步骤 1 的 $DistVersion
    # 相同（"-dryrun" 后缀），每次重跑本脚本时 dist 内容可能已经变化（改了代码、重新构建）——如果
    # 该版本号已经发布过旧内容，直接 npm publish 会因为"版本号已存在"报错，与"发布不可变"这条既有
    # 语义冲突。本脚本代表的是"全新一次性演练"场景（同步骤 2 判断记录），因此在演练自己的私服上
    # 把这个 -dryrun 版本号当作可重新发布的临时内容处理，先尽力 unpublish（首次运行该版本号还不
    # 存在，unpublish 必然失败，忽略即可）。
    $pkgDirForPublish = Join-Path $DistRoot "packages\com.gamefoundation.adapter.unity"
    if (-not (Test-Path $pkgDirForPublish)) {
        return [PSCustomObject]@{ Ok = $false; Detail = "找不到 $pkgDirForPublish（步骤 1 应已生成，见 build.ps1 5.15 节）" }
    }
    # 判断记录（unpublish 前后临时切 $ErrorActionPreference = "Continue"）：npm unpublish --force
    # 会在 stderr 打一行 "npm warn using --force..."，PowerShell 对原生命令 stderr 输出在
    # $ErrorActionPreference = "Stop"（本脚本头部全局设置）下会当成终止性错误抛出，与这里"忽略
    # unpublish 结果，只是尽力而为"的意图冲突（首次运行该版本号还不存在时必然走到这行）——本脚本
    # 唯一目的是清出一个干净的版本号槽位，不关心 unpublish 本身成功与否。
    $prevErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & npm unpublish "com.gamefoundation.adapter.unity@$DistVersion" --registry $registryUrl --userconfig $registryNpmrcPath --force 2>&1 | Out-Null
    } catch {
    }
    $ErrorActionPreference = $prevErrorActionPreference
    # --tag：npm 对 semver 预发布版本号（"-dryrun" 后缀）默认拒绝隐式打上 "latest" 标签发布，
    # 必须显式指定一个非 "latest" 的 dist-tag（见实测报错 "You must specify a tag using --tag..."）；
    # 这个 dist-tag 只影响"不带版本号的 npm install 默认解析到谁"，Unity 侧始终按精确版本号
    # （$DistVersion）引用，不受影响。
    & npm publish $pkgDirForPublish --registry $registryUrl --userconfig $registryNpmrcPath --tag consumer-smoke-dryrun --silent | Out-Null
    if ($LASTEXITCODE -ne 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "npm publish 失败（退出码 $LASTEXITCODE）：$pkgDirForPublish -> $registryUrl" }
    }

    # 另起一个独立、轻量的消费方工程（不复用 $ConsumerProjectDir，理由见本步骤头部注释）。
    $probeProjectDir = Join-Path $WorkDir "RegistryProbe\ConsumerProject"
    $probeLogDir = Join-Path $WorkDir "RegistryProbe\logs"
    New-Item -ItemType Directory -Force -Path (Join-Path $probeProjectDir "Assets\Editor") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $probeProjectDir "Packages") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $probeProjectDir "ProjectSettings") | Out-Null
    New-Item -ItemType Directory -Force -Path $probeLogDir | Out-Null

    Copy-TreeMirror -SourceDir (Join-Path $RepoRoot "adapters\unity\ProjectSettings") -DestDir (Join-Path $probeProjectDir "ProjectSettings") | Out-Null
    Copy-TreeMirror -SourceDir (Join-Path $RepoRoot "adapters\unity\Assets\Settings") -DestDir (Join-Path $probeProjectDir "Assets\Settings") | Out-Null
    Set-Utf8NoBom -Path (Join-Path $probeProjectDir "ProjectSettings\EditorBuildSettings.asset") -Content @"
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1045 &1
EditorBuildSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Scenes: []
  m_configObjects: {}
"@

    # manifest.json：依赖集合取自工作台 manifest.json 的内置模块（同步骤 4 判断记录），去掉工作台
    # 专属的 file: 包，adapter.unity 改按版本号引用私服（不是 file:），不加 testables——testables
    # 只对本地/内嵌包生效，registry 来源的包本来就不会编译其 Tests 程序集，不需要也不应该声明。
    $workbenchManifestPath = Join-Path $RepoRoot "adapters\unity\Packages\manifest.json"
    $workbenchManifest = (Get-Content -Path $workbenchManifestPath -Raw -Encoding UTF8) | ConvertFrom-Json
    $probeDependencies = [ordered]@{ "com.gamefoundation.adapter.unity" = $DistVersion }
    $probeSkipPackages = @("com.gamefoundation.conformance", "com.gamefoundation.game-template")
    foreach ($prop in $workbenchManifest.dependencies.PSObject.Properties) {
        if ($probeSkipPackages -contains $prop.Name) { continue }
        $probeDependencies[$prop.Name] = $prop.Value
    }
    $probeManifest = [ordered]@{
        scopedRegistries = @(
            [ordered]@{ name = "ws-game private registry (consumer_smoke probe)"; url = $registryUrl; scopes = @("com.gamefoundation") }
        )
        dependencies = $probeDependencies
    }
    Set-Utf8NoBom -Path (Join-Path $probeProjectDir "Packages\manifest.json") -Content ($probeManifest | ConvertTo-Json -Depth 10)

    # 探针脚本：驱动一次真实的 EffectSequencePlayer.Play(..., VfxBlendMode.Additive)，断言最终渲染
    # 材质的着色器名——不是自己另写一套"直接查 Resources.Load 找不找得到"的旁路判据，走的是产品代码
    # 真实调用路径（同 adapters/unity 工作台 UnityRenderer2DTests.EmitParticle_WithAdditiveBlendMode_
    # SwitchesToAdditiveMaterial 断言口径）。
    $probeCs = @"
using System;
using System.IO;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.EngineAdapter;
using UnityEditor;
using UnityEngine;

public static class AdditiveMaterialRegistryProbe
{
    public static void Run()
    {
        string resultPath = Path.Combine(Application.dataPath, "..", "registry_probe_result.txt");
        try
        {
            var go = new GameObject("Probe");
            var player = go.AddComponent<EffectSequencePlayer>();
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            player.Play(new[] { sprite }, new double[] { 1.0 }, false, VfxBlendMode.Additive);

            var renderer = go.GetComponent<SpriteRenderer>();
            var shaderName = (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null)
                ? renderer.sharedMaterial.shader.name
                : "<null>";
            bool pass = shaderName == "GameFoundation/Vfx/AdditiveUnlit";
            File.WriteAllText(resultPath, (pass ? "RESULT=PASS" : "RESULT=FAIL") + " shader=" + shaderName);
        }
        catch (Exception ex)
        {
            File.WriteAllText(resultPath, "RESULT=EXCEPTION " + ex);
        }
        finally
        {
            EditorApplication.Exit(0);
        }
    }
}
"@
    Set-Utf8NoBom -Path (Join-Path $probeProjectDir "Assets\Editor\AdditiveMaterialRegistryProbe.cs") -Content $probeCs

    # 判断记录（探针脚本需要一个显式 asmdef，不能是默认 Assembly-CSharp-Editor）：
    # com.gamefoundation.adapter.unity 包的 Runtime/Editor asmdef 都设了 "autoReferenced": false
    # （见两处 asmdef 判断记录——包内类型不希望被消费方工程里随手的松散脚本隐式带到），探针脚本要
    # 引用 Adapter.Unity.EngineAdapter.EffectSequencePlayer 与 Core.Foundation.EngineAdapter.
    # VfxBlendMode，必须有自己的 asmdef 显式 reference "Adapter.Unity"，并把 Core.Foundation.dll
    # 加进 precompiledReferences（VfxBlendMode 定义在这个precompiled 程序集里，"Adapter.Unity"
    # 对它的引用不会传递给消费方）。
    $probeAsmdef = @"
{
    "name": "RegistryProbe.Editor",
    "references": ["Adapter.Unity"],
    "includePlatforms": ["Editor"],
    "precompiledReferences": ["Core.Foundation.dll"],
    "overrideReferences": true,
    "autoReferenced": true
}
"@
    Set-Utf8NoBom -Path (Join-Path $probeProjectDir "Assets\Editor\RegistryProbe.Editor.asmdef") -Content $probeAsmdef

    Wait-NoResidualUnityProcess
    $probeLog = Join-Path $probeLogDir "registry_probe.log"
    $proc = Invoke-NativeAndWait -Exe $ResolvedUnityExe -ArgList @(
        "-batchmode", "-nographics", "-quit",
        "-projectPath", $probeProjectDir,
        "-executeMethod", "AdditiveMaterialRegistryProbe.Run",
        "-logFile", $probeLog
    ) -TimeoutSeconds 600
    if ($proc.TimedOut) {
        # 超时被强杀（退出码 -1）时同样按签名留证：日志含 IPC 断流签名才抓，否则不留证、不改 Detail。
        $upmNote = Get-UpmEvidenceDetailSuffix -EngineLogPath $probeLog -EngineExitCode $proc.ExitCode -EvidenceRoot $UpmEvidenceRoot -Tag "consumer_05_registry_probe"
        return [PSCustomObject]@{ Ok = $false; Detail = "registry 安装形态探针超过 600s 未完成，见 $probeLog$upmNote" }
    }

    $upmNote = Get-UpmEvidenceDetailSuffix -EngineLogPath $probeLog -EngineExitCode $proc.ExitCode -EvidenceRoot $UpmEvidenceRoot -Tag "consumer_05_registry_probe"
    $resultPath = Join-Path $probeProjectDir "registry_probe_result.txt"
    if (-not (Test-Path $resultPath)) {
        return [PSCustomObject]@{ Ok = $false; Detail = "Unity 退出码 $($proc.ExitCode)，未生成探针结果文件，见 $probeLog$upmNote" }
    }
    $resultText = (Get-Content -Path $resultPath -Raw).Trim()
    [PSCustomObject]@{
        Ok = $resultText.StartsWith("RESULT=PASS")
        Detail = "$resultText（Unity 退出码 $($proc.ExitCode)，见 $probeLog）$upmNote"
    }
}

# -----------------------------------------------------------------------------
# 汇总
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "==== 消费方演练汇总 ====" -ForegroundColor Cyan
$script:Results | Format-Table -AutoSize Step, Result, Seconds, Detail | Out-String -Width 4096 | Write-Host

$failed = @($script:Results | Where-Object { $_.Result -eq "FAIL" })
if ($failed.Count -gt 0) {
    Write-Host "消费方演练失败：$($failed.Count) 步未通过（共 $($script:Results.Count) 步）。" -ForegroundColor Red
    Stop-TranscriptAndReport -Failed $true
    exit 1
} else {
    Write-Host "消费方演练通过：全部 $($script:Results.Count) 步。" -ForegroundColor Green
    Stop-TranscriptAndReport -Failed $false
    exit 0
}
