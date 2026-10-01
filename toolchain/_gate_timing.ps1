<#
门禁耗时自动记录（AGENTS.md 1c，"门禁耗时自动记录"切片，2026-10-01）：`check.ps1` 每次运行结束后
（通过或失败都写）把逐步耗时追加到仓库根 `timing/<年月日>_<分支名去前缀>.jsonl`，一行一条 JSON，
字段固定：task、branch、phase、step、start、end、seconds、result、note（与 AGENTS.md 1c 一致）。
统计用 `toolchain/timing_report.py`（只读，输出 stdout）。

判断记录：
- step 取步骤的稳定 `-Id`（ADR-0126 给 Invoke-CheckStep/Add-SkippedStep 加的标识），不用中文显示名，
  跨任务汇总才对得上；结果行没有 Id（理论上不会：`toolchain/tests/test_gate_timing_log.py` 有静态用例
  卡住"每个步骤调用点都带 -Id"）时退化为显示名，宁可多一个怪名字也不丢行。
- start/end 是每步真实起止时刻，由步骤运行器（`_gate_step_runner.ps1` 的 Invoke-CheckStep）在步骤前后
  各取一次系统时间，并行线各自在自己的子进程里取，经 JSON 带回主进程——不是"整次起点 + 秒数"推算。
  ISO 8601 本地时间精确到秒（截断），所以 end-start 与 seconds（步骤内 Stopwatch，保留 1 位小数）
  最多差不到 1 秒。
- 额外一行 step=`_total` 记脚本总墙钟（定向模式下是干活子进程的墙钟，不含父进程的判定与启动时间）。
  note 里带并行阶段墙钟与环境性 SKIP 数。
- 文件名分支部分：去掉 `feature/`、`bugfix/` 前缀；其余 `/` 换成 `-`（如 `release/1.12.x` 记成
  `release-1.12.x`）；分离头指针记为 `detached`。同一分支同一天多次运行追加到同一文件。
- **main 与游离 HEAD 上不写已跟踪文件，改写待领目录**（"待领耗时记录"切片，2026-10-01）：`timing/<年月日>_main.jsonl`
  一旦入库，之后每次在 main 上跑门禁都会往已跟踪文件追加，主检出变脏，挡住 `build.ps1 -Release`（要求干净工作树）
  与下一次 `git merge --ff-only`。所以分支名是 `main`（与 `version_label.py` 的 MAIN_BRANCH 同一判定）或游离
  HEAD 时，改写 `timing/_pending/<年月日>_main_<时分秒>.jsonl`（游离 HEAD 为 `<年月日>_detached_<时分秒>`），
  该目录被 .gitignore 覆盖、一次运行一个文件（文件名带时分秒，永不追加到已有文件）；由下一条分支用
  `toolchain/claim_pending_records.py` 领走并入库。feature/bugfix/release 等分支保持原行为。
- 写入失败（只读、磁盘满、git 取不到分支……）不影响门禁结论：Write-GateTimingFromRun 吞掉异常、
  返回错误文本，由调用方在汇总末尾打一行警告；成功返回 $null。
- 行尾 LF、UTF-8 无 BOM；每行 JSON 由本文件手工拼装（字符串字段交给 ConvertTo-Json 转义，seconds 用
  不变文化格式化），避开 Windows PowerShell 5.1 对 DateTime/浮点序列化的差异。
#>

function Format-GateTimingTimestamp {
    param([datetime]$Time)
    return $Time.ToString("yyyy-MM-ddTHH:mm:ss", [System.Globalization.CultureInfo]::InvariantCulture)
}

# 没给 -TimingTask 时的默认任务描述："check.ps1 <参数串>"；参数串取自调用方 $PSBoundParameters，
# 内部/噪声参数不进（见下面排除表）。
function Get-GateTimingDefaultTask {
    param([Parameter(Mandatory = $true)]$BoundParameters)
    $exclude = @("TargetedInner", "PlanFile", "ArtifactsPath", "LogFile", "TimingTask", "NoTiming",
        "InjectMockSleepHeavySeconds", "InjectMockSleepUnitySeconds")
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $BoundParameters.GetEnumerator()) {
        $name = [string]$entry.Key
        if ($exclude -contains $name) { continue }
        $value = $entry.Value
        if ($value -is [System.Management.Automation.SwitchParameter]) {
            if ($value.IsPresent) { $parts.Add("-$name") }
        } elseif ($value -is [array]) {
            $parts.Add("-$name " + (($value | ForEach-Object { [string]$_ }) -join ","))
        } else {
            $parts.Add("-$name $value")
        }
    }
    if ($parts.Count -eq 0) { return "check.ps1" }
    return "check.ps1 " + ($parts -join " ")
}

# 当前分支名（git rev-parse --abbrev-ref HEAD）；分离头指针返回 "detached"；取不到抛异常。
function Get-GateTimingBranch {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $out = & git -C $RepoRoot rev-parse --abbrev-ref HEAD 2>$null
        $exit = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevEap
    }
    if ($exit -ne 0 -or -not $out) {
        throw "git rev-parse --abbrev-ref HEAD 失败（退出码 $exit）"
    }
    $branch = ([string](@($out)[0])).Trim()
    if ($branch -eq "HEAD" -or $branch -eq "") { return "detached" }
    return $branch
}

# 文件名里的分支部分：去 feature/、bugfix/ 前缀，其余 / 换成 -。
function Get-GateTimingBranchSlug {
    param([Parameter(Mandatory = $true)][string]$Branch)
    $slug = $Branch -replace '^(feature|bugfix)/', ''
    return ($slug -replace '[\\/]', '-')
}

# 本次运行该写待领目录而不是 timing/ 下的已跟踪文件吗：main 与游离 HEAD（Get-GateTimingBranch 返回 "detached"）。
# 与 toolchain/version_label.py 的 MAIN_BRANCH / 游离 HEAD 判定同口径（测试里有一致性用例）。
function Test-GateTimingUsesPending {
    param([Parameter(Mandatory = $true)][string]$Branch)
    return ($Branch -eq "main" -or $Branch -eq "detached")
}

function Get-GateTimingFilePath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Branch,
        [Parameter(Mandatory = $true)][datetime]$Date
    )
    $inv = [System.Globalization.CultureInfo]::InvariantCulture
    $day = $Date.ToString("yyyyMMdd", $inv)
    $timingDir = Join-Path $RepoRoot "timing"
    if (Test-GateTimingUsesPending -Branch $Branch) {
        $clock = $Date.ToString("HHmmss", $inv)
        return (Join-Path (Join-Path $timingDir "_pending") ("{0}_{1}_{2}.jsonl" -f $day, (Get-GateTimingBranchSlug -Branch $Branch), $clock))
    }
    return (Join-Path $timingDir ("{0}_{1}.jsonl" -f $day, (Get-GateTimingBranchSlug -Branch $Branch)))
}

# 一行 JSON（字段顺序固定，同 AGENTS.md 1c）。
function ConvertTo-GateTimingLine {
    param(
        [string]$Task, [string]$Branch, [string]$Phase, [string]$Step,
        [string]$Start, [string]$End, [double]$Seconds, [string]$Result, [string]$Note = ""
    )
    $q = { param($s) ConvertTo-Json -InputObject ([string]$s) -Compress }
    $secondsText = $Seconds.ToString("0.0", [System.Globalization.CultureInfo]::InvariantCulture)
    return ('{{"task":{0},"branch":{1},"phase":{2},"step":{3},"start":{4},"end":{5},"seconds":{6},"result":{7},"note":{8}}}' -f
        (& $q $Task), (& $q $Branch), (& $q $Phase), (& $q $Step), (& $q $Start), (& $q $End),
        $secondsText, (& $q $Result), (& $q $Note))
}

function Get-GateTimingRowNote {
    param([string]$Detail)
    $text = ([string]$Detail) -replace '[\r\n]+', ' '
    $text = $text.Trim()
    if ($text.Length -gt 200) { $text = $text.Substring(0, 200) + "..." }
    return $text
}

# 纯函数：结果行 -> JSON 行数组（按 start 排序，最后一行是 _total）。
function New-GateTimingLines {
    param(
        [Parameter(Mandatory = $true)][AllowNull()][AllowEmptyCollection()]$Results,
        [Parameter(Mandatory = $true)][string]$Task,
        [Parameter(Mandatory = $true)][string]$Branch,
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][datetime]$TotalStart,
        [Parameter(Mandatory = $true)][datetime]$TotalEnd,
        [Parameter(Mandatory = $true)][double]$TotalSeconds,
        [string]$TotalResult = "PASS",
        [string]$TotalNote = ""
    )
    $fallbackTime = Format-GateTimingTimestamp $TotalEnd
    $rows = New-Object System.Collections.Generic.List[Object]
    $index = 0
    # 直接 foreach，不写 @($Results)：Windows PowerShell 5.1 对 List[Object]（门禁的 $script:Results）做
    # @() 包装会抛 "Argument types do not match"（dbg 实测）；foreach 对 $null 零次迭代、对单个对象迭代一次。
    foreach ($r in $Results) {
        if ($null -eq $r) { continue }
        $props = $r.PSObject.Properties.Name
        $id = if ($props -contains "Id") { [string]$r.Id } else { "" }
        $start = if (($props -contains "Start") -and [string]$r.Start -ne "") { [string]$r.Start } else { $fallbackTime }
        $end = if (($props -contains "End") -and [string]$r.End -ne "") { [string]$r.End } else { $start }
        $rows.Add([PSCustomObject]@{
            Index   = $index
            Start   = $start
            Line    = (ConvertTo-GateTimingLine -Task $Task -Branch $Branch -Phase $Phase `
                -Step $(if ($id -ne "") { $id } else { [string]$r.Step }) `
                -Start $start -End $end -Seconds ([double]$r.Seconds) -Result ([string]$r.Result) `
                -Note (Get-GateTimingRowNote -Detail ([string]$r.Detail)))
        })
        $index++
    }
    # 按 start 排序、同一时刻保持原顺序：排序键 = "<start>|<序号补零>" 做序数（Ordinal）字符串比较。不用
    # Sort-Object：Windows PowerShell 5.1 不保证稳定，且按属性表达式排序在本场景实测抛
    # "Argument types do not match"；Array.Sort 配键数组 + 序数比较既稳定又不依赖当前文化。
    $keys = New-Object string[] $rows.Count
    $items = New-Object string[] $rows.Count
    for ($i = 0; $i -lt $rows.Count; $i++) {
        $keys[$i] = ("{0}|{1:D8}" -f $rows[$i].Start, $rows[$i].Index)
        $items[$i] = [string]$rows[$i].Line
    }
    [System.Array]::Sort($keys, $items, [System.StringComparer]::Ordinal)
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($item in $items) { $lines.Add($item) }
    $lines.Add((ConvertTo-GateTimingLine -Task $Task -Branch $Branch -Phase $Phase -Step "_total" `
        -Start (Format-GateTimingTimestamp $TotalStart) -End (Format-GateTimingTimestamp $TotalEnd) `
        -Seconds $TotalSeconds -Result $TotalResult -Note $TotalNote))
    return $lines.ToArray()
}

# 把一次门禁运行追加进 timing/。成功返回 $null；失败返回错误文本（不抛异常，不影响门禁结论）。
function Write-GateTimingFromRun {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][AllowNull()][AllowEmptyCollection()]$Results,
        [Parameter(Mandatory = $true)][string]$Task,
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][datetime]$TotalStart,
        [Parameter(Mandatory = $true)][datetime]$TotalEnd,
        [Parameter(Mandatory = $true)][double]$TotalSeconds,
        [string]$TotalResult = "PASS",
        [string]$TotalNote = ""
    )
    try {
        $branch = Get-GateTimingBranch -RepoRoot $RepoRoot
        $path = Get-GateTimingFilePath -RepoRoot $RepoRoot -Branch $branch -Date $TotalStart
        $lines = New-GateTimingLines -Results $Results -Task $Task -Branch $branch -Phase $Phase `
            -TotalStart $TotalStart -TotalEnd $TotalEnd -TotalSeconds $TotalSeconds `
            -TotalResult $TotalResult -TotalNote $TotalNote
        $dir = Split-Path -Parent $path
        if (-not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
        }
        $text = (($lines -join "`n") + "`n")
        [System.IO.File]::AppendAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
        return $null
    } catch {
        return $_.Exception.Message
    }
}
