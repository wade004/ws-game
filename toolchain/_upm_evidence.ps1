<#
.SYNOPSIS
    Unity 包管理器（UnityPackageManager.exe）子进程中途消失时的失败现场自动抓取（2026-10-01，
    bugfix/upm-evidence-stale-pid）。

    背景：门禁里的 Unity 批处理（check.ps1 的编译检查/EditMode/PlayMode/独立版构建、
    consumer_smoke.ps1 的各引擎子步骤）偶发几秒内退出，引擎日志形如
        IPCStream (Upm-xxxxx): IPC stream failed to read (Not connected)
        [Package Manager] Failed to resolve packages: operation cancelled.
    排查已证明这是包管理器子进程在解析途中消失（中途结束进程可稳定复现同签名），但真正的触发者没查到：
    `%LOCALAPPDATA%\Unity\Editor\upm.log` 与子进程退出码会被下一次运行覆盖，事后无从对证。本文件把
    "判定是不是这一签名 + 立刻留证"写成纯函数，由各引擎步骤在引擎退出码非零时调用：命中签名就把
    upm.log、引擎日志相关片段、子进程退出码、当时的 Unity 相关进程快照存进门禁产物目录（bin/ 下，
    被 .gitignore 覆盖，绝不入库），并给步骤 Detail 返回一行摘要。

    判断记录：
    1) 只在"引擎退出码非零 且 日志含 `IPC stream failed to read`"时才抓取：退出码为 0 或日志不含签名
       说明不是这一类失败，不留证、不改 Detail，避免噪声。
    2) 抓取只产生 Detail 后缀字符串，从不参与步骤 Ok 判定；全部逻辑包在 try/catch 里，路径不存在、
       磁盘写失败、日志读不了都只让摘要变成"现场抓取失败：…"或直接返回空串，绝不抛出（调用方在
       $ErrorActionPreference = "Stop" 下也安全）。
    3) 子进程退出码来自引擎日志里的 "[Package Manager] Server process stopped with exit code `N`" 行
       （Unity 6 编辑器侧在包管理器服务进程退出时打印，2026-10-01 实测：数字外有反引号，且按无符号
       32 位打印——被 Stop-Process 结束的 -1 打成 4294967295），本文件把它折回有符号值；包管理器可能被
       Unity 拉起多次（"Server process restart attempt #2"），每次退出打一行，摘要按出现顺序全列。
       解读口径：-1/1 是被外部强行结束的特征，101 是包管理器自身崩溃的特征。找不到该行时摘要写
       "未在引擎日志中找到"，不猜。注意：该签名在"Unity 重拉起包管理器后成功恢复"的运行里也会出现
       （引擎退出码 0），所以只在引擎退出码非零时才抓取。
    4) upm.log 候选路径可能不止一个（不同 Unity 版本/平台位置不同），存在的都复制；候选列表可由参数
       覆盖，供单测在不依赖真机的情况下验证。

    只定义函数、无顶层副作用；PowerShell 5.1/7 兼容。单测见 toolchain/tests/test_upm_evidence.py。
#>

# 引擎日志里这一签名出现，说明 Unity 与包管理器子进程之间的 IPC 流被对端断开。
$script:UpmIpcFailureSignature = "IPC stream failed to read"

# 判定：引擎退出码非零且日志文本含签名。
function Test-UpmIpcFailureSignature {
    param(
        [AllowEmptyString()][AllowNull()][string]$LogText,
        [int]$ExitCode
    )
    if ($ExitCode -eq 0) { return $false }
    if ([string]::IsNullOrEmpty($LogText)) { return $false }
    return ($LogText.IndexOf($script:UpmIpcFailureSignature, [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
}

# 把日志里的退出码数字转成有符号 32 位整数：Unity 把 Windows 的 DWORD 退出码按无符号打印
# （实测 Stop-Process/Kill 的 -1 打成 4294967295，崩溃的 0xC0000005 打成 3221225477），这里折回有符号值，
# 摘要里才能直接对上 -1/1/101 的口径。超出 32 位范围或解析不了的返回 $null（调用方跳过，不猜）。
function ConvertTo-SignedExitCode {
    param([Parameter(Mandatory = $true)][string]$Text)
    try { $v = [int64]$Text } catch { return $null }
    if ($v -ge -2147483648 -and $v -le 2147483647) { return [int]$v }
    if ($v -gt 2147483647 -and $v -le 4294967295) { return [int]($v - 4294967296) }
    return $null
}

# 从引擎日志里取包管理器子进程的全部退出码（按日志出现顺序，有符号值；包管理器可能被 Unity 拉起多次，
# 每次退出都会打一行）。优先匹配 "Server process stopped with exit code `N`"（Unity 6 实测格式，数字外有
# 反引号，大小写与反引号/引号都容错），其次匹配带 Package Manager/UPM 字样的其它 "exit code N" 行。
# 一个都找不到返回空数组（不猜）。
function Get-UpmChildExitCodes {
    param([AllowEmptyString()][AllowNull()][string]$LogText)
    $codes = New-Object System.Collections.Generic.List[int]
    if (-not [string]::IsNullOrEmpty($LogText)) {
        $ms = [System.Text.RegularExpressions.Regex]::Matches(
            $LogText, '(?i)Server process (?:stopped|exited)[^\r\n]*?exit(?:ed)?\s*code\s*[:=]?\s*[`''"]?(-?\d+)')
        if ($ms.Count -eq 0) {
            $ms = [System.Text.RegularExpressions.Regex]::Matches(
                $LogText, '(?im)^[^\r\n]*(?:Package Manager|UnityPackageManager|\bUPM\b)[^\r\n]*?exit(?:ed)?\s*(?:with\s*)?(?:exit\s*)?code\s*[:=]?\s*[`''"]?(-?\d+)')
        }
        foreach ($m in $ms) {
            $signed = ConvertTo-SignedExitCode -Text $m.Groups[1].Value
            if ($null -ne $signed) { $codes.Add([int]$signed) }
        }
    }
    # 不用 `return ,@(...)`：调用方统一写 @(Get-UpmChildExitCodes ...)，再多包一层会变成数组套数组。
    return $codes.ToArray()
}

# 最后一次退出码（最终导致解析中断的那次）；找不到返回 $null。
function Get-UpmChildExitCode {
    param([AllowEmptyString()][AllowNull()][string]$LogText)
    $codes = @(Get-UpmChildExitCodes -LogText $LogText)
    if ($codes.Count -eq 0) { return $null }
    return $codes[$codes.Count - 1]
}

# upm.log 的候选路径（不检查是否存在，由抓取函数过滤）。
function Get-UpmLogCandidatePaths {
    $paths = New-Object System.Collections.Generic.List[string]
    $roots = @()
    if ($env:LOCALAPPDATA) { $roots += $env:LOCALAPPDATA }
    try {
        $special = [Environment]::GetFolderPath('LocalApplicationData')
        if ($special) { $roots += $special }
    } catch { }
    if ($env:USERPROFILE) { $roots += (Join-Path $env:USERPROFILE "AppData\Local") }
    foreach ($root in $roots) {
        $p = Join-Path $root "Unity\Editor\upm.log"
        if (-not $paths.Contains($p)) { $paths.Add($p) }
    }
    return $paths.ToArray()
}

# 在引擎日志文本里取与包管理器有关的行（含签名/Package Manager/IPC/退出码行），连同日志尾部若干行，
# 写成一个文本供留证。
function Get-UpmEngineLogExcerpt {
    param([AllowEmptyString()][AllowNull()][string]$LogText, [int]$TailLines = 60)
    $lines = $LogText -split "`r?`n"
    $related = @($lines | Where-Object { $_ -match '(?i)Package Manager|IPC stream|IPCStream|UnityPackageManager|Server process|\bupm\b' })
    $tail = @($lines | Select-Object -Last $TailLines)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("==== 与包管理器有关的行（共 $($related.Count) 行）====")
    foreach ($l in $related) { [void]$sb.AppendLine($l) }
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("==== 引擎日志最后 $($tail.Count) 行 ====")
    foreach ($l in $tail) { [void]$sb.AppendLine($l) }
    return $sb.ToString()
}

# 当前系统里 Unity 相关进程快照（名称/PID/父 PID/启动时间/命令行），用于事后判断"是不是别的会话
# 的清理动作误杀了包管理器子进程"。查询失败返回说明文字，不抛。
function Get-UnityProcessSnapshotText {
    try {
        $procs = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop |
            Where-Object { $_.Name -match '^(Unity|UnityPackageManager|UnityHelper|VBCSCompiler|Unity\.Licensing\.Client)' -or ($_.CommandLine -and $_.CommandLine -match 'UnityPackageManager') })
        $sb = New-Object System.Text.StringBuilder
        [void]$sb.AppendLine("抓取时刻：" + (Get-Date -Format "yyyy-MM-ddTHH:mm:ss"))
        [void]$sb.AppendLine("Unity 相关进程 $($procs.Count) 个（Name PID ParentPID Created CommandLine）：")
        foreach ($p in $procs) {
            [void]$sb.AppendLine(("{0} {1} {2} {3} {4}" -f $p.Name, $p.ProcessId, $p.ParentProcessId, $p.CreationDate, $p.CommandLine))
        }
        return $sb.ToString()
    } catch {
        return "进程快照查询失败：" + $_.Exception.Message
    }
}

# 抓取前 $WindowMinutes 分钟内可能解释"包管理器子进程为什么消失"的 Windows 事件：应用程序日志里的
# Error/Warning（崩溃会留 Application Error / Windows Error Reporting 条目，外部结束不会——据此区分
# 自身崩溃与被结束）以及 Defender 运行日志（安全软件拦截/隔离）。取不到（权限、日志不存在）写失败原因，
# 不抛、不重试；整体限时由 Get-WinEvent 的 StartTime 过滤保证很快。
function Get-UpmWindowsEventsText {
    param([int]$WindowMinutes = 5, [int]$MaxPerLog = 60)
    $since = (Get-Date).AddMinutes(-1 * $WindowMinutes)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("时间窗：$($since.ToString('yyyy-MM-ddTHH:mm:ss')) 起 $WindowMinutes 分钟内")
    $queries = @(
        @{ Title = "Application 日志（Error/Warning，Level<=3）"; Filter = @{ LogName = "Application"; StartTime = $since; Level = 1, 2, 3 } },
        @{ Title = "Windows Defender/Operational"; Filter = @{ LogName = "Microsoft-Windows-Windows Defender/Operational"; StartTime = $since } }
    )
    foreach ($q in $queries) {
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("==== $($q.Title) ====")
        try {
            # SilentlyContinue：窗口内没有匹配事件时 Get-WinEvent 会报"找不到任何与指定的选择条件匹配的事件"，
            # 那不是故障，不要让它进控制台记录（transcript 会把它当终止性错误刷一行）。
            $events = @(Get-WinEvent -FilterHashtable $q.Filter -MaxEvents $MaxPerLog -ErrorAction SilentlyContinue)
            [void]$sb.AppendLine("共 $($events.Count) 条（最多取 $MaxPerLog；0 条可能是窗口内无事件，也可能是该日志不可读）")
            foreach ($e in $events) {
                $msg = ([string]$e.Message) -replace '\s+', ' '
                if ($msg.Length -gt 400) { $msg = $msg.Substring(0, 400) + "..." }
                [void]$sb.AppendLine(("{0} Id={1} {2} {3}" -f $e.TimeCreated.ToString("HH:mm:ss"), $e.Id, $e.ProviderName, $msg))
            }
        } catch {
            [void]$sb.AppendLine("无事件或查询失败：" + $_.Exception.Message)
        }
    }
    return $sb.ToString()
}

# 抓取主函数。返回对象：
#   Matched   ：是否命中签名（退出码非零且日志含签名）；未命中时其余字段为空，什么都不写。
#   Dir       ：留证目录（命中且创建成功时）。
#   ExitCode  ：解析到的包管理器子进程最后一次退出码（有符号），找不到为 $null；ExitCodes 是按日志出现顺序的全部。
#   Copied    ：已复制/写出的文件路径数组。
#   Summary   ：给步骤 Detail 用的一行摘要。
# 永不抛出。
function Save-UpmFailureEvidence {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$EngineLogPath,
        [int]$EngineExitCode,
        [Parameter(Mandatory = $true)][string]$EvidenceRoot,
        [Parameter(Mandatory = $true)][string]$Tag,
        [string[]]$UpmLogCandidates = $null
    )
    $empty = [PSCustomObject]@{ Matched = $false; Dir = ""; ExitCode = $null; ExitCodes = @(); Copied = @(); Summary = "" }
    if ($EngineExitCode -eq 0) { return $empty }

    $logText = $null
    try {
        if ($EngineLogPath -and (Test-Path -LiteralPath $EngineLogPath)) {
            $logText = [System.IO.File]::ReadAllText($EngineLogPath)
        }
    } catch {
        return $empty
    }
    if (-not (Test-UpmIpcFailureSignature -LogText $logText -ExitCode $EngineExitCode)) { return $empty }

    $copied = New-Object System.Collections.Generic.List[string]
    $errors = New-Object System.Collections.Generic.List[string]
    $childExit = $null
    $childExits = @()
    $dir = ""
    try {
        $childExits = @(Get-UpmChildExitCodes -LogText $logText)
        if ($childExits.Count -gt 0) { $childExit = $childExits[$childExits.Count - 1] }
    } catch { $errors.Add("解析退出码失败：" + $_.Exception.Message) }
    $codeText = if ($childExits.Count -eq 0) { "未在引擎日志中找到" } else { ($childExits | ForEach-Object { [string]$_ }) -join "," }

    try {
        $safeTag = ($Tag -replace '[^0-9A-Za-z_.-]', '_')
        $dir = Join-Path $EvidenceRoot ((Get-Date -Format "yyyyMMdd_HHmmss_fff") + "_" + $safeTag)
        # 用 .NET 的 CreateDirectory：父路径里有同名文件时抛异常；New-Item -Force 在这种情况下既不报错
        # 也建不出目录（2026-10-01 单测实测），会让后面的写文件全部连环失败，不能拿来建留证目录。
        [void][System.IO.Directory]::CreateDirectory($dir)
    } catch {
        $dir = ""
        $errors.Add("建留证目录失败：" + $_.Exception.Message)
    }

    if ($dir -ne "") {
        $candidates = $UpmLogCandidates
        if ($null -eq $candidates) {
            try { $candidates = Get-UpmLogCandidatePaths } catch { $candidates = @(); $errors.Add("取 upm.log 候选路径失败：" + $_.Exception.Message) }
        }
        $index = 0
        foreach ($candidate in @($candidates)) {
            $index++
            try {
                if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                    $target = Join-Path $dir ("upm_candidate{0}.log" -f $index)
                    Copy-Item -LiteralPath $candidate -Destination $target -Force
                    $copied.Add($target)
                }
            } catch { $errors.Add("复制 upm.log 失败（$candidate）：" + $_.Exception.Message) }
        }
        try {
            $excerptPath = Join-Path $dir "engine_log_excerpt.txt"
            [System.IO.File]::WriteAllText($excerptPath, (Get-UpmEngineLogExcerpt -LogText $logText), (New-Object System.Text.UTF8Encoding $false))
            $copied.Add($excerptPath)
        } catch { $errors.Add("写引擎日志片段失败：" + $_.Exception.Message) }
        try {
            $procPath = Join-Path $dir "unity_processes.txt"
            [System.IO.File]::WriteAllText($procPath, (Get-UnityProcessSnapshotText), (New-Object System.Text.UTF8Encoding $false))
            $copied.Add($procPath)
        } catch { $errors.Add("写进程快照失败：" + $_.Exception.Message) }
        try {
            $evPath = Join-Path $dir "windows_events.txt"
            [System.IO.File]::WriteAllText($evPath, (Get-UpmWindowsEventsText), (New-Object System.Text.UTF8Encoding $false))
            $copied.Add($evPath)
        } catch { $errors.Add("写 Windows 事件失败：" + $_.Exception.Message) }
        try {
            $metaPath = Join-Path $dir "summary.txt"
            $metaText = "引擎日志：$EngineLogPath`r`n引擎退出码：$EngineExitCode`r`n包管理器子进程退出码（按出现顺序，有符号）：$codeText`r`nupm.log 副本数：" +
                @($copied | Where-Object { $_ -like '*upm_candidate*' }).Count + "`r`n"
            [System.IO.File]::WriteAllText($metaPath, $metaText, (New-Object System.Text.UTF8Encoding $false))
            $copied.Add($metaPath)
        } catch { $errors.Add("写 summary 失败：" + $_.Exception.Message) }
    }

    $summary = "包管理器子进程退出码=$codeText（-1/1 疑似外部结束，101 疑似自身崩溃）"
    if ($dir -ne "") {
        $summary += "；现场已存 $dir"
    }
    if ($errors.Count -gt 0) {
        $summary += "；现场抓取部分失败：" + ($errors -join "；")
    }
    return [PSCustomObject]@{ Matched = $true; Dir = $dir; ExitCode = $childExit; ExitCodes = @($childExits); Copied = @($copied.ToArray()); Summary = $summary }
}

# 给步骤 Detail 用的便捷封装：命中签名返回 "；" + 摘要，否则返回空串；任何异常都返回空串（或一行
# 抓取失败说明），绝不抛出、绝不影响步骤结论。
function Get-UpmEvidenceDetailSuffix {
    param(
        [AllowEmptyString()][string]$EngineLogPath,
        [int]$EngineExitCode,
        [string]$EvidenceRoot,
        [string]$Tag,
        [string[]]$UpmLogCandidates = $null
    )
    try {
        $result = Save-UpmFailureEvidence -EngineLogPath $EngineLogPath -EngineExitCode $EngineExitCode `
            -EvidenceRoot $EvidenceRoot -Tag $Tag -UpmLogCandidates $UpmLogCandidates
        if ($result -and $result.Matched) {
            Write-Host ("  [包管理器现场] " + $result.Summary) -ForegroundColor Yellow
            return ("；" + $result.Summary)
        }
        return ""
    } catch {
        return ("；包管理器现场抓取异常：" + $_.Exception.Message)
    }
}

# 从一份落盘的控制台记录（例如 consumer_smoke.ps1 的 consumer_smoke.log）里取出各引擎子步骤打过的包管理器
# 现场摘要行，供调用方（check.ps1 Unity 线的消费方演练步骤，其子进程输出被 | Out-Null 吞掉）把它们并进
# 自己的 Detail。没有命中、文件不存在、读失败一律返回空串，绝不抛出。
function Get-UpmEvidenceSummaryFromLog {
    param([AllowEmptyString()][string]$LogPath)
    try {
        if (-not $LogPath -or -not (Test-Path -LiteralPath $LogPath)) { return "" }
        $found = New-Object System.Collections.Generic.List[string]
        foreach ($line in [System.IO.File]::ReadAllLines($LogPath)) {
            $m = [System.Text.RegularExpressions.Regex]::Match($line, '包管理器子进程退出码=.*$')
            if ($m.Success -and -not $found.Contains($m.Value.Trim())) { $found.Add($m.Value.Trim()) }
        }
        if ($found.Count -eq 0) { return "" }
        return ("；" + ($found -join "；"))
    } catch {
        return ""
    }
}
