<#
门禁分线共享步骤运行器（工程收尾 gate-speed 任务新增，2026-09-22）：`check.ps1` 原先把
`Invoke-CheckStep`/`Add-SkippedStep`/`Test-NativeExitCode`/`Invoke-NativeAndWait`/
`Test-NoResidualUnityProcess`/`Invoke-UnityTestTriageOnFailure`/`Resolve-UnityExe` 这些函数与
步骤汇总基础设施全部定义在自己内部；把"Unity 串行链"与"非 Unity 重步骤线"拆成两个独立子进程
并行跑之后，两条线各自的子脚本（`toolchain/_gate_line_unity.ps1`、
`toolchain/_gate_line_heavy.ps1`）与 `check.ps1` 自己的"快速前置步骤"阶段都需要同一套函数——
抽到本文件用 dot-source（`. <本文件路径>`）复用，不三处各写一份、也不用把函数塞进模块清单
（仓库其余 `toolchain/_xxx.ps1` 辅助脚本，如 `_hash.ps1`/`_version_writeback.ps1`/
`_unity_path_length_guard.ps1`，都是同一种"独立文件 + dot-source"惯例，选它是为了与既有风格
一致，也方便 `toolchain/tests` 用同一套 dot-source 手法单独测试）。

判断记录（为什么用 dot-source 而不是显式传参把 $DocsOnly/$FailFast/$script:Results 都作为
函数参数）：dot-source 把本文件顶层代码"原样嵌入"调用方脚本的当前作用域——之后定义的函数
（如 `Invoke-CheckStep`）在被调用时，其内部对 `$DocsOnly`/`$FailFast`/`$script:Results`/
`$script:GateFailed`/`$script:FailFastFlagPath` 等变量的引用，会沿作用域链找到调用方脚本自己
的同名变量（三个宿主脚本——`check.ps1`、`toolchain/_gate_line_unity.ps1`、
`toolchain/_gate_line_heavy.ps1`——各自在 dot-source 本文件之前都已经用 `param()`/
`$script:xxx = ...` 声明好这些变量）。这与本文件被抽出之前、`Invoke-CheckStep` 直接定义在
`check.ps1` 内部时的取值方式完全一致，只是把函数定义挪到了另一个文件里，不改变调用惯例，因此
`check.ps1` 原有对 `Invoke-CheckStep -DocRelevant` 的全部调用点不需要任何改动。

调用方在 dot-source 本文件之前必须已经声明（否则函数内部引用会因变量不存在而按 $null 处理，
在 `strict mode` 下会报错，本仓库未开 `Set-StrictMode`，但仍应显式声明避免"看似能跑、实则用的
是 $null"这种隐性错误）：
  - `[switch]$DocsOnly`（无该开关时按普通 param 默认值 $false 处理即可，`check.ps1` 与两条子线
    脚本均把它设成显式 param，语义与原 check.ps1 完全一致）；
  - `[switch]$FailFast`（新增开关，见 check.ps1 `.PARAMETER FailFast` 判断记录）；
  - `$script:Results`（`System.Collections.Generic.List[Object]`，步骤结果累积列表）；
  - `$script:GateFailed`（bool，`Invoke-CheckStep` 内部维护，FailFast 下用于"本线自己是否已经
    失败过一次，后续步骤全部改判可见 SKIP"）；
  - `$script:FailFastFlagPath`（可选字符串；非空时表示"存在与本线并行跑的另一条线"，
    `Invoke-CheckStep` 在 FailFast 下会在真正执行 $Action 之前先看这个路径对应的文件是否存在——
    存在说明另一条并行线已经失败，本线也应立即停止启动新步骤；本线自己失败时会创建这个文件，
    让对方线在它自己的下一步开始前感知到。两条线各自是独立的 `powershell.exe` 子进程（用
    `Start-Job -FilePath` 启动，`$using:`/内存共享变量跨进程不可见），只能靠共享文件系统上的
    这一个标记文件通信——`check.ps1` 自己的"快速前置步骤"阶段跑在主进程里、不涉及跨进程，
    不需要设置这个变量（保持 $null 即可，`Invoke-CheckStep` 对 $null/空字符串的检查会跳过这一
    整段逻辑，退化成"只看本进程内 $script:GateFailed"，与旧行为等价）。
#>

# -----------------------------------------------------------------------------
# 步骤汇总基础设施（原 check.ps1 内联实现原样搬入，含全部既有判断记录；调用惯例不变）
# -----------------------------------------------------------------------------

function Write-StepHeader {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

# $Action 是一个不带参数的 scriptblock：约定返回值有三种形状——
#   1) $null：通过，明细列留空（既有大多数步骤的写法）；
#   2) $true/$false：直接就是通过/失败，明细列留空；
#   3) [PSCustomObject]@{ Ok = <bool>; Detail = <string> }：通过/失败取 Ok，明细列取 Detail
#      （H5 新增，供 Unity EditMode/PlayMode 步骤把 total/passed/failed 计数写进汇总表）；
#   4) [PSCustomObject]@{ Skip = $true; Reason = <string> }：判定为可见 SKIP（不是 PASS，也不是
#      FAIL），明细列取 Reason（外部审计 audit-76d16a5-20260910 PJ114-02 根治新增，等价于
#      Add-SkippedStep，但用在"是否跳过要等脚本内部跑了一步才知道"的场景）。
# 抛异常同样记为失败（异常消息进明细列）。任一步骤失败都不会中断"本线"后续步骤（除非
# -FailFast 生效，见下方"FailFast 短路"判断记录）——"顺序执行并汇总"仍是默认行为，见任务书。
#
# -DocRelevant（提交前钩子分级任务新增）：调用点显式标注"这一步跟文档相关，-DocsOnly 下也要
# 跑"。$DocsOnly 为真且调用点没有传 -DocRelevant 时，整个 $Action 都不会被求值——直接改判
# Add-SkippedStep，原因固定标"-DocsOnly"。
#
# 判断记录（FailFast 短路，gate-speed 任务新增）：`-FailFast` 开关（`check.ps1` 顶层新增，
# `build.ps1 -Release` 调用门禁时默认传）打开后，任一步骤判定为 FAIL 会把 `$script:GateFailed`
# 置真；本函数入口处先检查这个标记——为真就不再对后续每个步骤求值（$Action 根本不会执行），
# 直接记一行 SKIP，原因写清楚"上游步骤已失败"。两条并行线之间的联动见 `$script:FailFastFlagPath`
# 判断记录（本文件头部）：本线失败时除了置本进程内的 `$script:GateFailed`，还会（若该变量非空）
# 在共享标记文件上留痕，供另一条并行子进程在它自己下一步开始前感知到并同样停下——不满足"立刻
# 掐断对方正在执行中的那一步"（跨进程强行终止另一个子进程正在跑的原生命令会有更高的实现复杂度与
# 风险，如 dotnet/Unity 子进程被腰斩可能留下半吊子产物），只满足"对方不再启动新的步骤"，这一
# 折衷已在任务书"按实现难度选，写进判断记录"授权范围内。
function Invoke-CheckStep {
    param(
        [string]$Name,
        [scriptblock]$Action,
        [switch]$DocRelevant
    )

    if ($DocsOnly -and -not $DocRelevant) {
        Add-SkippedStep $Name "-DocsOnly（非文档相关步骤，仅纯文档改动的提交跳过）"
        return
    }

    if ($FailFast) {
        if ($script:GateFailed) {
            Add-SkippedStep $Name "上游步骤已失败（-FailFast，本线不再启动新步骤）"
            return
        }
        if ($script:FailFastFlagPath -and (Test-Path -LiteralPath $script:FailFastFlagPath)) {
            $script:GateFailed = $true
            Add-SkippedStep $Name "并行的另一条线已失败（-FailFast，本线不再启动新步骤）"
            return
        }
    }

    Write-StepHeader $Name
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $ok = $false
    $detail = ""
    try {
        $result = & $Action

        # F1 根治第二层防护（Test-NativeExitCode 本身已经不再泄漏原生命令的 stdout 到管道，见该
        # 函数判断记录；这里额外加一道防线，防止今后有人写出新的 helper 同样把中间输出漏进
        # scriptblock 的返回值）：$result 若是"多元素集合"，说明 $Action 在真正的结果之前还
        # 产生过别的管道输出——按 PowerShell"scriptblock 最后一条表达式的值即返回值"的惯例，
        # 只取最后一个元素参与判定，同时显式告警，让这种"本不该发生"的情况在日志里可见，而不是
        # 被 [bool] 数组转换悄悄吞成恒真（这正是 F1 复现的根因：非空 System.Object[] 经 [bool]
        # 转换恒为 $true，与内容/元素数量无关）。单元素集合（PowerShell 常见的"标量结果被包成
        # 一元数组"情形）直接拆包，不告警。
        if ($result -is [array]) {
            if ($result.Count -gt 1) {
                Write-Host "[$Name] 警告：检查步骤脚本块返回了 $($result.Count) 个对象（应恰好一个），只取最后一个参与判定——前面的对象可能是原生命令泄漏的输出，请检查该步骤实现" -ForegroundColor Yellow
            }
            $result = if ($result.Count -gt 0) { $result[-1] } else { $null }
        }

        if ($result -is [pscustomobject] -and ($result.PSObject.Properties.Name -contains "Skip") -and [bool]$result.Skip) {
            $skipReason = ""
            if (($result.PSObject.Properties.Name -contains "Reason") -and $result.Reason) {
                $skipReason = [string]$result.Reason
            }
            $sw.Stop()
            $seconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1)
            $script:Results.Add([PSCustomObject]@{
                Step    = $Name
                Result  = "SKIP"
                Seconds = $seconds
                Detail  = $skipReason
            })
            Write-Host "[$Name] 已跳过：$skipReason" -ForegroundColor Yellow
            return
        }

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

    $script:Results.Add([PSCustomObject]@{
        Step    = $Name
        Result  = if ($ok) { "PASS" } else { "FAIL" }
        Seconds = $seconds
        Detail  = $detail
    })

    if ($ok) {
        Write-Host "[$Name] 通过，用时 ${seconds}s" -ForegroundColor Green
    } else {
        Write-Host "[$Name] 失败，用时 ${seconds}s" -ForegroundColor Red
        if ($FailFast) {
            $script:GateFailed = $true
            if ($script:FailFastFlagPath) {
                try {
                    if (-not (Test-Path -LiteralPath $script:FailFastFlagPath)) {
                        New-Item -ItemType File -Force -Path $script:FailFastFlagPath | Out-Null
                    }
                } catch {
                    Write-Host "警告：写入 FailFast 标记文件失败（$($_.Exception.Message)），并行的另一条线可能感知不到本线已失败" -ForegroundColor Yellow
                }
            }
        }
    }
}

function Add-SkippedStep {
    param([string]$Name, [string]$Reason)
    Write-StepHeader $Name
    Write-Host "已跳过：$Reason" -ForegroundColor Yellow
    $script:Results.Add([PSCustomObject]@{
        Step    = $Name
        Result  = "SKIP"
        Seconds = 0
        Detail  = $Reason
    })
}

# 跑一个原生可执行文件并按退出码判定通过/失败。判断记录（F1/TOOL-01 根治、局部降级
# $ErrorActionPreference）与原 check.ps1 完全一致，原样搬入，不重复贴一遍长注释——详见本仓库
# git 历史 `check.ps1` 对应函数（2026-09 之前）与 `architecture/落地计划/audit-20260907/
# delivery-validation.md` F1、`architecture/落地计划/audit-b3b91ee-20260907/code-review.md`
# TOOL-01。
function Test-NativeExitCode {
    param(
        [string]$Exe,
        [string[]]$ArgList
    )
    $resolved = Get-Command -Name $Exe -CommandType Application, ExternalScript -ErrorAction SilentlyContinue
    if (-not $resolved) {
        Write-Host "[Test-NativeExitCode] 可执行文件 '$Exe' 未找到（不在 PATH 中，或路径不存在），判定为 FAIL" -ForegroundColor Red
        return $false
    }
    $ErrorActionPreference = "Continue"
    & $Exe @ArgList | Out-Host
    return ($LASTEXITCODE -eq 0)
}

# H5 新增：跑一个"GUI 子系统"原生可执行文件（Unity.exe / 独立版 Shell.exe）并真正等待其退出、
# 拿到真实退出码。判断记录（GUI 子系统 `&` 不阻塞、VBCSCompiler 残留子进程拖住 -Wait 约 10
# 分钟等）与原 check.ps1 完全一致，原样搬入。
function Invoke-NativeAndWait {
    param(
        [string]$Exe,
        [string[]]$ArgList,
        [int]$TimeoutSeconds = 0
    )

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

# H5 新增：Unity 四步开跑前检查"同一工程"是否已经有一个残留的 Unity.exe 进程在跑。判断记录与
# 原 check.ps1 完全一致，原样搬入（不代为 Kill，只报告）。
function Test-NoResidualUnityProcess {
    param([string]$ProjectPath)

    try {
        $procs = Get-CimInstance -ClassName Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop
    } catch {
        return
    }

    $needle = $ProjectPath.TrimEnd("\", "/")
    $hit = $procs | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($needle) }
    if ($hit) {
        $pids = ($hit | ForEach-Object { $_.ProcessId }) -join ", "
        throw "检测到同一工程已有残留 Unity.exe 进程在运行（PID: $pids，工程路径 $ProjectPath），本脚本不会代为结束——请先手工关闭该 Unity Editor 窗口/进程，确认 $ProjectPath\Temp\UnityLockfile 已释放后重跑。"
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

# 排查复盘 2026-09-15 落地：Unity EditMode/PlayMode 测试步骤判定为失败时，自动跑一遍
# toolchain/unity_test_triage.py。判断记录与原 check.ps1 完全一致，原样搬入。
function Invoke-UnityTestTriageOnFailure {
    param(
        [string]$ResultsXml,
        [string]$LogPath
    )
    $pythonCmd = Get-Command -Name "python" -CommandType Application -ErrorAction SilentlyContinue
    if (-not $pythonCmd) {
        Write-Host "[Unity 测试分诊] 未找到 python，跳过自动分诊（可手工运行：python toolchain/unity_test_triage.py --xml `"$ResultsXml`" --log `"$LogPath`"）" -ForegroundColor Yellow
        return
    }
    Write-Host ""
    Write-Host "--- Unity 测试分诊（toolchain/unity_test_triage.py，失败分支自动触发） ---" -ForegroundColor Yellow
    $ErrorActionPreference = "Continue"
    Push-Location $RepoRoot
    try {
        & python "toolchain/unity_test_triage.py" "--xml" $ResultsXml "--log" $LogPath | Out-Host
    } catch {
        Write-Host "[Unity 测试分诊] 运行分诊脚本本身出错（不影响门禁判定）：$($_.Exception.Message)" -ForegroundColor Yellow
    } finally {
        Pop-Location
    }
    Write-Host "--- Unity 测试分诊结束 ---" -ForegroundColor Yellow
}

# 两条并行子线（`_gate_line_unity.ps1`/`_gate_line_heavy.ps1`）跑完后，把 `$script:Results`
# 写成 JSON 落盘，供主进程 `check.ps1` 读回合并进自己的汇总表——不用 `Receive-Job` 直接拿返回
# 对象，是为了避开 PowerShell 后台作业跨进程对象序列化的深度/类型还原不确定性（见
# `Start-Job`/`Receive-Job` 官方文档"反序列化对象"一节的已知限制），JSON 是更可控、可读、
# 也方便调试（落盘文件可以直接打开看）的选择。
function Write-GateResultsJson {
    param([string]$Path)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    $script:Results | ConvertTo-Json -Depth 5 | Out-File -FilePath $Path -Encoding utf8
}
