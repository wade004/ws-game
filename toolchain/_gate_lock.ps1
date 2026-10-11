<#
.SYNOPSIS
    门禁独占锁（机制，不靠自觉）：启动门禁/发布前自动检查全机独占锁文件，别人持有时拒绝启动。

    判断记录（2026-10-08，收尾四件之 1）：前两个阶段都发生过"先启动门禁后看锁"（上一阶段一次 14 分钟
    重叠），靠提示词里的纪律挡不住，所以把锁做成脚本机制。框架 `check.ps1`、`build.ps1 -Release`
    与样板仓库 `check.ps1`（它有自己的一份同逻辑副本 `tools/gate_lock.ps1`，样板不得依赖框架源码）
    启动时调用本文件的函数。

    约定（两个仓库一致）：
    - 锁文件默认 `D:\wt\_exclusive_gate.lock`，环境变量 `WS_GATE_LOCK_PATH` 可覆盖（测试与其它机器用）。
      内容是一行 JSON：`holder`（持有者名）、`started`（开始时间，ISO 8601 本地时间）、`pid`、`pidStart`
      （该进程的启动时刻，用来识别 PID 被系统复用）。
    - 调用者身份 = `-AcquireExclusiveLock <持有者名>` 的值，或环境变量 `WS_GATE_LOCK_HOLDER`。获取锁成功
      后本进程把该环境变量设成持有者名，子进程（定向门禁的内层、`build.ps1 -Release` 调起的 `check.ps1`、
      发布候选阶段调起的样板 `check.ps1`、git 钩子）因此都被识别为同一持有者，不会自己卡死自己。
    - 启动检查：锁不存在 -> 放行；锁存在且持有者 = 调用者 -> 放行（不重复获取）；锁里的 PID 已不存在 ->
      陈旧锁，打印警告后放行（带 `-AcquireExclusiveLock` 时接管）；否则拒绝，打印持有者、开始时间、PID。
      锁内容不是本格式（如手写的一行文本）且读不出 PID 时按"存活"处理，持有者名取其中 `持有者=` 之后的词。
    - 释放：获取锁时起一个隐藏的看守进程，持有者进程不管怎么结束（正常、`exit N`、未接住的异常、被强杀）都由它
      在持有者消失后删除锁（只删 PID 仍是持有者自己的锁）；看守没赶上的由"PID 已不存在即陈旧"兜底，下一次
      启动自动接管。因此释放有约一秒内的延迟，但不会让后来者被僵尸锁挡住。
    - 不加任何"忽略锁"的开关：要等就等，持有者结束后自然释放。

    用法（check.ps1 / build.ps1 在 dot-source 本文件后调用）：
        if (-not (Enter-GateLockGuard -AcquireName $AcquireExclusiveLock -Context "check.ps1")) { exit 1 }
#>

function Get-GateLockPath {
    if ($env:WS_GATE_LOCK_PATH) { return $env:WS_GATE_LOCK_PATH }
    return "D:\wt\_exclusive_gate.lock"
}

function Get-GateLockProcessStamp {
    param([int]$ProcessId)
    try {
        $p = Get-Process -Id $ProcessId -ErrorAction Stop
        return $p.StartTime.ToUniversalTime().ToString("o")
    } catch {
        return $null
    }
}

function Read-GateLock {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $raw = ""
    try {
        $raw = [System.IO.File]::ReadAllText($Path, (New-Object System.Text.UTF8Encoding($false))).Trim()
    } catch {
        return [PSCustomObject]@{ Holder = "(锁文件读不出来)"; Started = ""; Pid = $null; PidStart = $null; Raw = "" }
    }
    $holder = ""
    $started = ""
    $lockPid = $null
    $pidStart = $null
    $parsed = $false
    if ($raw.StartsWith("{")) {
        try {
            $o = $raw | ConvertFrom-Json
            $holder = "$($o.holder)"
            if ($null -ne $o.pid) { $lockPid = [int]$o.pid }
            # 时间字段直接从原文取：PowerShell 7 的 ConvertFrom-Json 会把 ISO 时间串转成 DateTime，再转回字符串就变样了。
            $ms = [regex]::Match($raw, '"started"\s*:\s*"([^"]*)"')
            if ($ms.Success) { $started = $ms.Groups[1].Value }
            $mp = [regex]::Match($raw, '"pidStart"\s*:\s*"([^"]*)"')
            if ($mp.Success) { $pidStart = $mp.Groups[1].Value }
            $parsed = $true
        } catch { $parsed = $false }
    }
    if (-not $parsed) {
        $m = [regex]::Match($raw, '持有者\s*[=:：]\s*([^\s,;，；]+)')
        if ($m.Success) { $holder = $m.Groups[1].Value } else { $holder = $raw }
        $t = [regex]::Match($raw, '开始时间\s*[=:：]\s*(\S+(?:\s+\d{1,2}:\d{2}(?::\d{2})?)?)')
        if ($t.Success) { $started = $t.Groups[1].Value }
    }
    return [PSCustomObject]@{ Holder = $holder; Started = $started; Pid = $lockPid; PidStart = $pidStart; Raw = $raw }
}

# 锁里记的进程已不存在（或同一 PID 已是另一个进程）= 陈旧。读不出 PID 的锁无法判断，按存活处理。
function Test-GateLockStale {
    param($Lock)
    if ($null -eq $Lock -or $null -eq $Lock.Pid) { return $false }
    $stamp = Get-GateLockProcessStamp -ProcessId $Lock.Pid
    if ($null -eq $stamp) { return $true }
    if ($Lock.PidStart) {
        try {
            $a = [DateTime]::Parse($stamp, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
            $b = [DateTime]::Parse($Lock.PidStart, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
            if ([Math]::Abs(($a - $b).TotalSeconds) -gt 2) { return $true }
        } catch { }
    }
    return $false
}

function New-GateLockFile {
    param([string]$Path, [string]$Holder)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $body = [ordered]@{
        holder   = $Holder
        started  = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss")
        pid      = $PID
        pidStart = (Get-GateLockProcessStamp -ProcessId $PID)
    } | ConvertTo-Json -Compress
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($body)
    # CreateNew + FileShare.None：两个进程同时抢，只有一个能创建成功。
    $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try { $fs.Write($bytes, 0, $bytes.Length) } finally { $fs.Dispose() }
}

# 释放靠一个隐藏的看守进程：持有者进程无论怎么结束（正常、exit N、未接住的异常、被强杀），看守都会在它消失后把
# "PID 仍是它自己"的锁删掉。不用 PowerShell.Exiting 事件——实测 powershell.exe / pwsh 用 -File 跑脚本时该事件的
# Action 在进程退出前不会执行，锁会残留。看守只是清理；即使看守没来得及删，PID 已不存在的锁下次启动也按陈旧处理。
function Register-GateLockRelease {
    param([string]$Path)
    $ownerPid = $PID
    $watcher = @"
`$ErrorActionPreference = 'SilentlyContinue'
`$owner = $ownerPid
`$path = '$($Path.Replace("'", "''"))'
Wait-Process -Id `$owner
Start-Sleep -Milliseconds 300
try {
    `$o = [System.IO.File]::ReadAllText(`$path) | ConvertFrom-Json
    if ([int]`$o.pid -eq `$owner) { Remove-Item -LiteralPath `$path -Force }
} catch { }
"@
    $encoded = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($watcher))
    $hostExe = (Get-Process -Id $PID).Path
    try {
        Start-Process -FilePath $hostExe -ArgumentList @("-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass", "-EncodedCommand", $encoded) -WindowStyle Hidden | Out-Null
    } catch {
        Write-Host "警告：启动独占锁看守进程失败（$($_.Exception.Message)）；锁不会在进程结束时立即删除，但 PID 失效后下次启动会按陈旧锁接管。" -ForegroundColor Yellow
    }
}

# 立即释放（正常路径里想提前放锁时用；没拿过锁或锁不是自己的什么都不做）。
function Exit-GateLock {
    $p = Get-GateLockPath
    $lock = Read-GateLock -Path $p
    if ($null -ne $lock -and $null -ne $lock.Pid -and [int]$lock.Pid -eq $PID) {
        Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue
    }
}

# 启动检查 + 可选获取。返回 $true = 可以继续；$false = 被别人持有的锁拒绝（已打印原因，调用方 exit 1）。
function Enter-GateLockGuard {
    param([string]$AcquireName = "", [string]$Context = "门禁")
    $path = Get-GateLockPath
    $caller = if ($AcquireName -ne "") { $AcquireName } else { "$env:WS_GATE_LOCK_HOLDER" }
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        $lock = Read-GateLock -Path $path
        if ($null -eq $lock) {
            if ($AcquireName -eq "") { return $true }
            try {
                New-GateLockFile -Path $path -Holder $AcquireName
            } catch [System.IO.IOException] {
                continue   # 刚被别人抢先创建，重新读一遍再判断
            }
            $env:WS_GATE_LOCK_HOLDER = $AcquireName
            Register-GateLockRelease -Path $path
            Write-Host "[gate-lock:acquired] 已获取门禁独占锁：$path（持有者 $AcquireName，PID $PID；进程结束自动释放）" -ForegroundColor Cyan
            return $true
        }
        if (Test-GateLockStale -Lock $lock) {
            Write-Host "[gate-lock:stale] 警告：独占锁 $path 的持有者 $($lock.Holder)（开始于 $($lock.Started)）的进程 PID $($lock.Pid) 已不存在，按陈旧锁处理。" -ForegroundColor Yellow
            if ($AcquireName -eq "") { return $true }
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            continue   # 删掉后走"不存在"分支接管
        }
        if ($caller -ne "" -and $lock.Holder -eq $caller) {
            if ($AcquireName -ne "") { $env:WS_GATE_LOCK_HOLDER = $AcquireName }
            return $true   # 同一持有者（含子进程）放行，不重复获取
        }
        Write-Host "[gate-lock:refused] $Context 拒绝启动：全机独占锁 $path 由 [$($lock.Holder)] 持有，开始于 $($lock.Started)，PID $($lock.Pid)（进程存活）。" -ForegroundColor Red
        Write-Host "  等持有者结束（锁自动释放）后重跑。合并前全量/发布要独占机器时用 -AcquireExclusiveLock <持有者名> 获取。" -ForegroundColor Red
        return $false
    }
    Write-Host "[gate-lock:refused] $Context 拒绝启动：反复竞争独占锁 $path 失败，请稍后重跑。" -ForegroundColor Red
    return $false
}
