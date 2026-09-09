<#
.SYNOPSIS
    启动本地 Verdaccio 私服（npm 兼容注册表，供 Unity 侧作用域注册表 com.gamefoundation 使用）。
    见仓库根 README.md"版本与发布"一节"私服通道"、本目录 README.md。

.PARAMETER ConfigPath
    Verdaccio 配置文件路径，默认本目录 config.yaml。

.PARAMETER Listen
    监听地址，默认 127.0.0.1:4873（仅本机）；局域网内其它机器需要访问时传 0.0.0.0:4873
    （config.yaml 本身不写死监听地址，见该文件判断记录）。

.PARAMETER Detach
    后台常驻启动：用 Start-Process 拉起一个独立的 node 进程跑 verdaccio（不是 npx 的 cmd 包装
    进程，见下方判断记录），把 PID 写入 -PidFile，轮询 /-/ping 确认服务已就绪后立即返回，不阻塞
    调用方终端。省略时前台阻塞运行（Ctrl+C 停止），适合交互式调试。

.PARAMETER PidFile
    -Detach 模式下写入 PID 的文件路径，默认本目录 verdaccio.pid（.gitignore 已忽略）。

.PARAMETER Stop
    停止 Verdaccio 进程。先按 -PidFile 记录的 PID 停一次，再按 -Listen 指定的端口兜底查找真正
    监听该端口的进程并停止（见下方判断记录二——PID 文件记录的 PID 不一定是真正监听端口的那个
    进程），最后核验端口确实已释放。找不到 PID 文件、记录的进程已不存在、或端口本来就没人监听时
    只打印提示，不报错退出（幂等，可重复调用）。传 -Stop 时忽略 -ConfigPath/-SkipInstall。
    PJ130-03 根治新增（见下方判断记录三）：拿到 PID 后一律先核验进程身份（可执行文件名、命令行
    是否含 verdaccio 与本次启动记录的配置路径、启动时间是否晚于 PID 文件写入时间），身份不符时
    拒绝停止该 PID、打印具体原因、不触碰该进程，本次调用以非零退出码结束——不再是"拿到 PID 就
    无条件强杀"，端口上的无关进程绝不受影响。

.PARAMETER Status
    查看 Verdaccio 运行状态：-PidFile 记录的 PID 是否存活，以及 -Listen 指定端口当前真正监听的
    进程 PID（可能与 PID 文件不一致，见下方判断记录）。只读，不改变任何状态。传 -Status 时忽略
    -ConfigPath/-SkipInstall。

.PARAMETER SkipInstall
    跳过 `npm ci`（本目录 node_modules 已经安装过、且 package.json/package-lock.json 未变化时可用，
    加快重复调用）。默认每次都跑一遍 `npm ci`，保证依赖版本与 package.json 锁定的一致。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。
    判断记录（为什么直接起 node 而不是 npx verdaccio）：`npx verdaccio` 在 Windows 上实际是
    `npx.cmd` 起一个 cmd.exe 包装进程，包装进程再拉起真正跑 Verdaccio 的 node.exe 子进程；
    `Start-Process` 拿到的 PID 是那个 cmd.exe 包装进程的 PID，`Stop-Process` 杀掉它并不保证会
    连带杀掉底下真正监听端口的 node.exe 子进程（Windows 的进程树不是父进程退出子进程必然跟着退出），
    会留下无法用本脚本 -Stop 干净收尾的残留监听进程。直接用 `node <node_modules>\verdaccio\bin\
    verdaccio` 启动，`Start-Process` 拿到的就是真正监听端口的那个 node.exe 进程本身的 PID，
    -Stop 一定能对应上要杀的那个进程。

    判断记录二（P07 根治，2026-09-08，-Stop 停不掉私服）：仅凭上面"直接起 node"这一条，仍不能
    保证 100% 可靠——verdaccio 未来版本变化、或运行环境导致 Start-Process 拿到的 PID 与实际监听
    端口的进程出现偏差都不能完全排除，且 -PidFile 本身可能因为人为删除/磁盘异常而丢失或损坏。
    因此改为双保险：-Detach 就绪后额外按 -Listen 端口用 `Get-NetTCPConnection -LocalPort <port>
    -State Listen` 查一次真正监听该端口的进程 PID，与 Start-Process 记录的 PID 不一致时以端口
    查到的为准重写 -PidFile；-Stop 时先按 -PidFile 停一次，再按端口兜底查一次仍在监听的进程一并
    停止，最后轮询确认端口已释放才算真正停止成功（不再是"PID 文件对应进程已不存在就当作已停止"
    这种可能与实际监听状态脱节的判定）。-Status 同样以端口实际监听到的 PID 为准做诊断输出。

    判断记录三（PJ130-03 根治，2026-09-08，见 architecture/落地计划/audit-5c444f1-20260908/
    AUDIT_REPORT.md PJ130-03）：判断记录二解决了"PID 文件记录的 PID 是否与实际监听端口的进程
    一致"，但两条停止路径（按 -PidFile、按端口兜底）拿到 PID 后都直接 `Stop-Process`，从未核对
    "这个 PID 现在对应的进程是不是本脚本启动的那个 Verdaccio"——如果 PID 文件过期/被系统复用给了
    另一个无关进程，或者端口被一个完全不相关的进程（例如别人手工起的另一个服务）占用，旧逻辑会
    不加区分地强杀它。根治：新增 `Test-VerdaccioProcessIdentity`，停止前对拿到的每个 PID 核验三项
    身份特征——(a) 可执行文件名必须是 `node`；(b) `Win32_Process.CommandLine` 必须同时包含
    `verdaccio` 字样与本次 `-Detach` 启动时记录的配置路径（或至少是本目录下的 verdaccio 安装
    路径 `node_modules\verdaccio\bin\verdaccio`，脚本目录固定、不随 -ConfigPath 变化）；(c) 该
    进程的实际启动时间不得早于 PID 文件被写入的时间（覆盖"PID 复用给了一个更早启动、纯属巧合还在
    跑的无关进程"这种情况——我们自己启动的 Verdaccio 进程，其启动时刻必然 >= 我们写 PID 文件的
    时刻）。三项任一不符即拒绝停止该 PID、打印具体原因，不删除 PID 文件（留给人工核实），且绝不
    影响该端口上其它无关进程；三项全部核验通过才 `Stop-Process`。身份特征持久化到
    `-PidFile` 同目录的 `<PidFile>.meta.json`（-Detach 写 PID 文件时一并写入 config_path/
    verdaccio_bin/pid_file_written_at_utc），供之后任意一次 -Stop 调用读取核对——即使中间隔了
    很久、-Stop 本身不重新接收 -ConfigPath（文档既有约定"传 -Stop 时忽略 -ConfigPath"），也能核对
    到启动时真正使用的配置路径，而不是 -Stop 调用时刻 -ConfigPath 的默认值。找不到该元数据文件
    时（例如本次修复之前遗留的旧 PID 文件）身份核验退化为只核对 (a) 可执行文件名 + (b) 命令行含
    verdaccio 与本目录 verdaccio 安装路径，不核对配置路径与启动时间——仍然能挡住"完全不相关的
    进程"（如占用同端口的另一个 HTTP 服务），只是对"另一个目录下的 Verdaccio 实例复用了同一
    PID"这类边界情况覆盖弱一些，好于完全不核验。

    判断记录四（2026-09-10 根治，-Stop 误拒真实进程）：判断记录三新增的条件 (c) 曾有一处实现漏洞
    ——"双保险"重写 -PidFile 分支（判断记录二：就绪后按端口核实到的 PID 与 Start-Process 记录的
    PID 不一致时以端口查到的为准重写）曾在重写时重新取一次 `[datetime]::UtcNow` 作为新的
    `pid_file_written_at_utc`（记为 T2），而不是用真正监听端口的那个进程自己的启动时间。已实测
    复现：先 `-Detach` 起一个真正监听端口的实例（PID A，真实启动时刻 S，此时未触发重写），不停止
    它、再次 `-Detach`——新 `Start-Process` 起的进程因端口已被占用而随即退出，但 `/-/ping` 命中的
    仍是仍在监听的 PID A，触发重写分支，把元数据里的 PID 重写为 A、`pid_file_written_at_utc` 重写
    为 T2（重写发生的当下时刻）；由于 PID A 早在 S（远早于 T2，两次调用间隔较长时可达数分钟）就已
    经启动并持续监听，之后任意一次 `-Status`/`-Stop` 核验条件 (c) 时必然发现 `S < T2`（超出 2 秒
    容差），把这个仍在正常提供服务、货真价实由本脚本管理的 Verdaccio 进程误判成"PID 被系统复用给
    了另一个更早启动的无关进程"而拒绝停止——这与"正常情况下不会发生"的原判断相反：只要 -Detach
    在端口已被占用（含前一次 -Detach 未停止就再次调用）的情况下调用一次，重写分支必然触发，条件
    (c) 必然误判（除非两次调用间隔小于 2 秒，现实中不能保证）。

    第一版修复曾改为重写分支沿用本次调用最初写 PID 文件时记录的 `$pidWrittenAtUtc`（T1，本次
    `Start-Process` 之后取的当下时刻），比重新取 T2 更接近真相，但用自动化回归测试实测仍不够：
    当真正监听端口的 PID A 是"另一次更早调用"启动的（本例即是），T1 是"本次调用"的当下时刻，
    依然可能晚于 A 的真实启动时刻 S 超过 2 秒（两次 `-Detach` 调用之间只要穿插了一次 -Detach
    就绪轮询或一次 Get-Process 诊断调用，实测间隔即可达 2 秒以上）——T1 与 T2 本质上是同一类
    "取本次调用当下时刻"的代理值，只是取的时间点更早、误判概率更低，不是从根上排除误判。真正
    唯一保证不早于 PID A 真实启动时刻的值只有它自己的 `(Get-Process -Id A).StartTime`——最终改为
    重写分支直接查询真正监听端口的那个进程自身的启动时间（转 UTC）作为 `pid_file_written_at_utc`
    写入，不使用任何"本次调用内部产生的时间代理值"；查询失败（进程在极短窗口内退出的罕见竞态）
    时退化为 `$pidWrittenAtUtc`（T1）兜底，好于完全不写。
#>
param(
    [string]$ConfigPath = "",
    [string]$Listen = "127.0.0.1:4873",
    [switch]$Detach,
    [string]$PidFile = "",
    [switch]$Stop,
    [switch]$Status,
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot
if ($ConfigPath -eq "") { $ConfigPath = Join-Path $ScriptDir "config.yaml" }
if ($PidFile -eq "") { $PidFile = Join-Path $ScriptDir "verdaccio.pid" }

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

# 从 "host:port" 形式的 -Listen 取出端口号（Get-NetTCPConnection 按端口查询要用到）。
function Get-ListenPort {
    param([string]$ListenValue)
    $parts = $ListenValue -split ":"
    return [int]$parts[-1]
}

# 按端口查真正处于 Listen 状态的进程 PID（去重）：见脚本头判断记录二——这是比 -PidFile 更权威的
# "谁在真正监听这个端口"信息源，-Detach 就绪校验、-Stop 兜底、-Status 诊断三处共用。查询本身失败
# （权限/网络栈异常等，非常罕见）时返回空数组，调用方按"未发现"处理，不中断主流程。
function Get-ListenOwningProcessIds {
    param([int]$Port)
    try {
        $conns = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop
    } catch {
        return @()
    }
    return @($conns | Select-Object -ExpandProperty OwningProcess -Unique)
}

# PID 元数据文件路径（PJ130-03 根治新增，见脚本头判断记录三）：与 -PidFile 同目录、文件名加
# ".meta.json" 后缀，记录 -Detach 启动时的 config_path/verdaccio_bin/pid_file_written_at_utc，
# 供之后任意一次 -Stop 调用核对进程身份。
$PidMetaFile = $PidFile + ".meta.json"

# 本目录固定的 verdaccio 安装路径（不随 -ConfigPath 变化，脚本目录一旦确定就唯一确定）：身份核验
# 兜底锚点之一，PID 元数据文件缺失时仍可用它判断"命令行是不是在跑本目录这一份 verdaccio 安装"。
$VerdaccioBinForIdentity = Join-Path $ScriptDir "node_modules\verdaccio\bin\verdaccio"

# 核验一个 PID 现在对应的进程是否确实是本脚本启动/管理的那个 Verdaccio 实例（PJ130-03 根治新增，
# 见脚本头判断记录三）：核验通过前，调用方不得对该 PID 执行 Stop-Process。三项特征依次核验，
# 任一不符立即返回 Ok=$false 并给出具体原因（不做部分核验就放行）：
#   (a) 可执行文件名必须是 "node"（Verdaccio 直接以 node 启动，见脚本头判断记录一）；
#   (b) Win32_Process.CommandLine 必须同时包含 "verdaccio" 字样，以及 $ExpectedConfigPath（若有）
#       或至少 $ExpectedVerdaccioBin——把"这是一个 node 进程"进一步收窄到"这是在跑本机这一份
#       Verdaccio 安装/配置的进程"，而不是恰好也叫 node.exe 的其它无关进程；
#   (c) 若提供 $NotBeforeUtc（PID 文件写入时间），该进程的实际启动时间不得早于这个时间点（允许
#       2 秒时钟粒度误差）——挡住"PID 恰好被系统复用给了一个更早启动、纯属巧合仍在跑的无关进程"。
# 找不到该 PID 对应的进程、或 Win32_Process 查询失败（命令行读不到）都判定为核验不通过，不放行——
# "查不清楚身份"与"身份明确不符"同样不能停止，避免在信息不全时冒险强杀。
function Test-VerdaccioProcessIdentity {
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [string]$ExpectedConfigPath = "",
        [string]$ExpectedVerdaccioBin = "",
        # 判断记录：故意不用 [System.Nullable[datetime]]——PowerShell 5.1 对 Nullable<T> 类型参数的
        # 绑定在"调用方传入一个来自 PSCustomObject 属性、类型为普通 [datetime] 的值"这种场景下
        # 曾实测触发 ".Value" 调用 "You cannot call a method on a null-valued expression"（值类型
        # 装箱/拆箱路径与预期不一致），改用无类型约束的 [object]，非空时就是一个普通 [datetime]
        # 对象，直接用、不再需要 .Value，规避这个绑定坑。
        [object]$NotBeforeUtc = $null
    )

    $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $proc) {
        return [PSCustomObject]@{ Ok = $false; Reason = "PID $ProcessId 对应的进程已不存在" }
    }
    if ($proc.ProcessName -ne "node") {
        return [PSCustomObject]@{ Ok = $false; Reason = "PID $ProcessId 可执行文件名是 '$($proc.ProcessName)'，不是 'node'，判定为无关进程" }
    }

    $cim = $null
    try {
        $cim = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction Stop
    } catch {
        $cim = $null
    }
    if (($null -eq $cim) -or [string]::IsNullOrEmpty($cim.CommandLine)) {
        return [PSCustomObject]@{ Ok = $false; Reason = "PID $ProcessId 无法读取命令行（Win32_Process 查询失败或为空），身份不明" }
    }
    $cmdLine = $cim.CommandLine
    if ($cmdLine -notmatch "verdaccio") {
        return [PSCustomObject]@{ Ok = $false; Reason = "PID $ProcessId 命令行不含 'verdaccio'（实际命令行：$cmdLine），判定为无关进程" }
    }

    $anchorMatched = $true
    if (-not [string]::IsNullOrEmpty($ExpectedConfigPath)) {
        $anchorMatched = $cmdLine.Contains($ExpectedConfigPath)
        if (-not $anchorMatched) {
            return [PSCustomObject]@{ Ok = $false; Reason = "PID $ProcessId 命令行不含本次启动记录的配置路径 '$ExpectedConfigPath'（实际命令行：$cmdLine），可能是另一个 Verdaccio 实例" }
        }
    } elseif (-not [string]::IsNullOrEmpty($ExpectedVerdaccioBin)) {
        $anchorMatched = $cmdLine.Contains($ExpectedVerdaccioBin)
        if (-not $anchorMatched) {
            return [PSCustomObject]@{ Ok = $false; Reason = "PID $ProcessId 命令行不含本目录 verdaccio 安装路径 '$ExpectedVerdaccioBin'（实际命令行：$cmdLine），可能是另一个 Verdaccio 实例" }
        }
    }

    if ($null -ne $NotBeforeUtc) {
        $startTimeUtc = $null
        try { $startTimeUtc = $proc.StartTime.ToUniversalTime() } catch { $startTimeUtc = $null }
        if ($null -eq $startTimeUtc) {
            return [PSCustomObject]@{ Ok = $false; Reason = "PID $ProcessId 无法读取启动时间，无法核验是否晚于 PID 文件写入时间" }
        }
        $notBeforeUtcValue = [datetime]$NotBeforeUtc
        if ($startTimeUtc -lt $notBeforeUtcValue.AddSeconds(-2)) {
            return [PSCustomObject]@{ Ok = $false; Reason = ("PID $ProcessId 启动时间 " + $startTimeUtc.ToString("o") + " 早于 PID 文件写入时间 " + $notBeforeUtcValue.ToString("o") + "，可能是 PID 被系统复用给了另一个更早启动的无关进程") }
        }
    }

    return [PSCustomObject]@{ Ok = $true; Reason = "" }
}

# 读取 -PidFile 同目录的元数据文件（若存在），返回本次 -Stop 用于身份核验的 config_path 与
# pid_file_written_at_utc；解析失败或文件不存在时返回空值，调用方按脚本头判断记录三"退化核验"
# 处理，不中断主流程。
function Get-VerdaccioIdentityAnchors {
    param([string]$MetaPath)
    $result = [PSCustomObject]@{ ConfigPath = ""; NotBeforeUtc = $null }
    if (-not (Test-Path $MetaPath)) {
        return $result
    }
    try {
        $metaObj = (Get-Content -Path $MetaPath -Raw -Encoding UTF8) | ConvertFrom-Json
        if ($metaObj.config_path) {
            $result.ConfigPath = [string]$metaObj.config_path
        }
        if ($metaObj.pid_file_written_at_utc) {
            $result.NotBeforeUtc = [datetime]::Parse([string]$metaObj.pid_file_written_at_utc, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
        }
    } catch {
        Write-Host "  PID 元数据文件解析失败（$MetaPath），身份核验退化为不校验配置路径/启动时间" -ForegroundColor Yellow
    }
    return $result
}

# 写入/覆盖 PID 元数据文件（PJ130-03 根治新增，见脚本头判断记录三）：-Detach 首次写 -PidFile、
# 以及就绪后按端口核实发现 PID 不一致而重写 -PidFile 时，都调用本函数同步写一份配套的
# <PidFile>.meta.json，记录之后 -Stop 身份核验需要的 config_path/verdaccio_bin/
# pid_file_written_at_utc。
function Write-VerdaccioIdentityMeta {
    param(
        [Parameter(Mandatory = $true)][string]$MetaPath,
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [string]$ConfigPathValue,
        [string]$VerdaccioBinValue,
        [string]$ListenValue,
        [Parameter(Mandatory = $true)][datetime]$WrittenAtUtc
    )
    $metaObj = [ordered]@{
        pid                     = $ProcessId
        config_path             = $ConfigPathValue
        verdaccio_bin           = $VerdaccioBinValue
        listen                  = $ListenValue
        pid_file_written_at_utc = $WrittenAtUtc.ToString("o")
    }
    ($metaObj | ConvertTo-Json) | Set-Content -Path $MetaPath -Encoding utf8
}

# -----------------------------------------------------------------------------
# -Status：独立分支，只读诊断，直接处理完就退出。
# -----------------------------------------------------------------------------
if ($Status) {
    Write-Step "-Status：查看 Verdaccio 运行状态（--listen $Listen）"
    $port = Get-ListenPort -ListenValue $Listen

    Write-Host "  PID 文件：$PidFile"
    if (Test-Path $PidFile) {
        $filePid = (Get-Content -Path $PidFile -Raw).Trim()
        $fileProc = Get-Process -Id $filePid -ErrorAction SilentlyContinue
        if ($null -ne $fileProc) {
            Write-Host "    记录 PID $filePid，进程存活（$($fileProc.ProcessName)）" -ForegroundColor Green
        } else {
            Write-Host "    记录 PID $filePid，对应进程已不存在（残留 PID 文件，未反映真实状态）" -ForegroundColor Yellow
        }
    } else {
        Write-Host "    不存在"
    }

    $owningPids = Get-ListenOwningProcessIds -Port $port
    Write-Host "  端口 $port 监听状态（权威判据，见脚本头判断记录二）："
    if ($owningPids.Count -eq 0) {
        Write-Host "    未监听：服务未运行（或实际监听地址与 -Listen $Listen 指定的不一致）" -ForegroundColor Yellow
        exit 0
    }
    $statusAnchors = Get-VerdaccioIdentityAnchors -MetaPath $PidMetaFile
    foreach ($p in $owningPids) {
        $proc = Get-Process -Id $p -ErrorAction SilentlyContinue
        $name = "?"
        if ($null -ne $proc) { $name = $proc.ProcessName }
        $identity = Test-VerdaccioProcessIdentity -ProcessId $p -ExpectedConfigPath $statusAnchors.ConfigPath -ExpectedVerdaccioBin $VerdaccioBinForIdentity -NotBeforeUtc $statusAnchors.NotBeforeUtc
        if ($identity.Ok) {
            Write-Host "    真实监听进程 PID $p（$name），身份核验：本脚本管理的 Verdaccio" -ForegroundColor Green
        } else {
            Write-Host "    真实监听进程 PID $p（$name），身份核验未通过（$($identity.Reason)）——不是本脚本管理的 Verdaccio，-Stop 不会碰它" -ForegroundColor Yellow
        }
    }
    exit 0
}

# -----------------------------------------------------------------------------
# -Stop：独立分支，直接处理完就退出。先按 -PidFile 停一次，再按端口兜底查一次仍在监听的进程一并
# 停止，最后轮询核验端口确实已释放——三步缺一不可，见脚本头判断记录二。幂等：重复调用、或本来就
# 没在跑时都正常退出 0。PJ130-03 根治新增（见脚本头判断记录三）：两条路径拿到 PID 后都先经
# Test-VerdaccioProcessIdentity 核验身份，核验不通过一律拒绝 Stop-Process、打印具体原因、保留
# 现场（不删 PID 文件），且绝不影响端口上的无关进程；只要出现一次身份核验不通过，本次调用整体
# 以非零退出码结束（不能悄悄"部分成功"却报告为正常退出，让调用方误以为已经安全停止）。
# -----------------------------------------------------------------------------
if ($Stop) {
    Write-Step "-Stop：停止 Verdaccio 进程（--listen $Listen）"
    $port = Get-ListenPort -ListenValue $Listen
    $stoppedAny = $false
    $identityBlocked = $false

    $stopAnchors = Get-VerdaccioIdentityAnchors -MetaPath $PidMetaFile

    if (Test-Path $PidFile) {
        $recordedPid = (Get-Content -Path $PidFile -Raw).Trim()
        $proc = Get-Process -Id $recordedPid -ErrorAction SilentlyContinue
        if ($null -ne $proc) {
            $identity = Test-VerdaccioProcessIdentity -ProcessId $recordedPid -ExpectedConfigPath $stopAnchors.ConfigPath -ExpectedVerdaccioBin $VerdaccioBinForIdentity -NotBeforeUtc $stopAnchors.NotBeforeUtc
            if ($identity.Ok) {
                Stop-Process -Id $recordedPid -Force -Confirm:$false
                Write-Host "  已按 PID 文件停止 PID $recordedPid（身份核验通过）" -ForegroundColor Green
                $stoppedAny = $true
                Remove-Item -Path $PidFile -Force -ErrorAction SilentlyContinue
                Remove-Item -Path $PidMetaFile -Force -ErrorAction SilentlyContinue
            } else {
                Write-Host "  PID 文件记录的 PID $recordedPid 身份核验未通过（$($identity.Reason)），拒绝停止该进程；保留 PID 文件供人工核实，不影响该进程继续运行" -ForegroundColor Red
                $identityBlocked = $true
            }
        } else {
            Write-Host "  PID 文件记录的 PID $recordedPid 对应进程已不存在（继续按端口兜底核实）" -ForegroundColor Yellow
            Remove-Item -Path $PidFile -Force -ErrorAction SilentlyContinue
            Remove-Item -Path $PidMetaFile -Force -ErrorAction SilentlyContinue
        }
    } else {
        Write-Host "  找不到 PID 文件：$PidFile（继续按端口兜底核实）" -ForegroundColor Yellow
    }

    # 端口兜底：不管上面 PID 文件那一步有没有找到/停掉进程，都再按端口查一次真正监听 $port 的
    # 进程——覆盖"PID 文件记录的是包装进程/PID 已被复用给别的进程/PID 文件丢失但服务仍在跑"等
    # 场景（见脚本头判断记录二）。PJ130-03 根治新增：每个端口上查到的 PID 同样先经身份核验，
    # 核验不通过（例如端口被完全无关的另一个进程占用）一律不停止、不影响它，只记诊断并标记
    # $identityBlocked——端口上的无关进程绝不受影响，见脚本头判断记录三。
    Start-Sleep -Milliseconds 300
    $owningPids = Get-ListenOwningProcessIds -Port $port
    foreach ($p in $owningPids) {
        $identity = Test-VerdaccioProcessIdentity -ProcessId $p -ExpectedConfigPath $stopAnchors.ConfigPath -ExpectedVerdaccioBin $VerdaccioBinForIdentity -NotBeforeUtc $stopAnchors.NotBeforeUtc
        if ($identity.Ok) {
            $proc = Get-Process -Id $p -ErrorAction SilentlyContinue
            $name = "?"
            if ($null -ne $proc) { $name = $proc.ProcessName }
            Write-Host "  端口 $port 仍被 PID $p（$name）监听，身份核验通过，一并停止" -ForegroundColor Yellow
            Stop-Process -Id $p -Force -Confirm:$false
            $stoppedAny = $true
        } else {
            Write-Host "  端口 $port 上的 PID $p 身份核验未通过（$($identity.Reason)），判定为无关进程，不停止、不影响它" -ForegroundColor Red
            $identityBlocked = $true
        }
    }

    if ($identityBlocked) {
        Write-Host "  存在身份核验未通过、被拒绝停止的进程（详见上方 Red 提示）；这是保护无关进程的预期结果，不代表脚本执行失败，但也不能确认 Verdaccio 已经停止，退出码非零，请人工核实" -ForegroundColor Red
        exit 1
    }

    # 核验端口释放：最多轮询 5 秒（Stop-Process 是异步请求终止，端口释放有一点延迟）。只有在没有
    # 身份核验被拒绝的情况下才需要这一步——上面 $identityBlocked 分支已经提前退出。
    $released = $false
    for ($i = 0; $i -lt 10; $i++) {
        if ((Get-ListenOwningProcessIds -Port $port).Count -eq 0) { $released = $true; break }
        Start-Sleep -Milliseconds 500
    }

    if (-not $released) {
        Write-Host "  端口 $port 停止后仍处于监听状态，请手工排查：Get-NetTCPConnection -LocalPort $port -State Listen" -ForegroundColor Red
        exit 1
    }

    if ($stoppedAny) {
        Write-Host "  已停止，端口 $port 已释放" -ForegroundColor Green
    } else {
        Write-Host "  未发现在跑的 Verdaccio 进程（PID 文件与端口均未命中），端口 $port 本来就是释放状态，无需操作" -ForegroundColor Yellow
    }
    exit 0
}

if (-not (Test-Path $ConfigPath)) {
    Write-Host "找不到配置文件：$ConfigPath" -ForegroundColor Red
    exit 1
}

# -----------------------------------------------------------------------------
# 1. npm ci 安装 verdaccio（本地到本目录 node_modules，不装全局）。
# -----------------------------------------------------------------------------
if (-not $SkipInstall) {
    Write-Step "npm ci（安装 verdaccio 到本目录 node_modules，不装全局）"
    Push-Location $ScriptDir
    try {
        & npm ci
        if ($LASTEXITCODE -ne 0) {
            throw "npm ci 失败，退出码 $LASTEXITCODE"
        }
    } finally {
        Pop-Location
    }
} else {
    Write-Step "已跳过 npm ci（-SkipInstall）"
}

$verdaccioBin = Join-Path $ScriptDir "node_modules\verdaccio\bin\verdaccio"
if (-not (Test-Path $verdaccioBin)) {
    Write-Host "找不到 $verdaccioBin（npm ci 是否成功？）" -ForegroundColor Red
    exit 1
}

# -----------------------------------------------------------------------------
# 2. 启动。前台：直接阻塞运行；-Detach：Start-Process 后台常驻 + 轮询 /-/ping 就绪。
# -----------------------------------------------------------------------------
if ($Detach) {
    Write-Step "后台启动 Verdaccio（--listen $Listen）"

    $stdoutLog = Join-Path $ScriptDir "verdaccio.out.log"
    $stderrLog = Join-Path $ScriptDir "verdaccio.err.log"

    $proc = Start-Process -FilePath "node" `
        -ArgumentList @($verdaccioBin, "--config", $ConfigPath, "--listen", $Listen) `
        -WorkingDirectory $ScriptDir `
        -RedirectStandardOutput $stdoutLog `
        -RedirectStandardError $stderrLog `
        -WindowStyle Hidden `
        -PassThru

    $pidWrittenAtUtc = [datetime]::UtcNow
    [System.IO.File]::WriteAllText($PidFile, [string]$proc.Id, (New-Object System.Text.UTF8Encoding($false)))
    Write-VerdaccioIdentityMeta -MetaPath $PidMetaFile -ProcessId $proc.Id -ConfigPathValue $ConfigPath -VerdaccioBinValue $verdaccioBin -ListenValue $Listen -WrittenAtUtc $pidWrittenAtUtc
    Write-Host "  已拉起 PID $($proc.Id)，日志：$stdoutLog / $stderrLog"

    $pingUrl = "http://" + $Listen + "/-/ping"
    if ($Listen.StartsWith("0.0.0.0")) {
        $pingUrl = "http://127.0.0.1" + $Listen.Substring("0.0.0.0".Length) + "/-/ping"
    }

    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            $resp = Invoke-WebRequest -Uri $pingUrl -UseBasicParsing -TimeoutSec 2
            if ($resp.StatusCode -eq 200) { $ready = $true; break }
        } catch {
            # 尚未就绪，继续轮询
        }
    }

    if (-not $ready) {
        Write-Host "  等待 30 秒后仍未就绪（$pingUrl 无 200 响应），请检查 $stderrLog" -ForegroundColor Red
        exit 1
    }
    Write-Host "  就绪：$pingUrl 返回 200" -ForegroundColor Green

    # 双保险（见脚本头判断记录二）：就绪后按端口再核实一次真正监听的 PID，与 Start-Process 记录
    # 的不一致时（例如端口已被占用导致本次 Start-Process 的新进程随即退出、而 /-/ping 命中的是
    # 早先已在监听的旧进程——见脚本头判断记录四实测复现）以端口查到的为准重写 -PidFile。判断记录
    # 四根治点：重写时刻的元数据必须记成"端口查到的那个真实进程自己的启动时间"，不能是任何形式的
    # "当下时刻"代理值——曾先后试过两种"当下时刻"代理都不可靠：(1) 重写发生当下重新取 UtcNow（记
    # 为 T2），必然晚于真正监听端口的那个进程的启动时间；(2) 沿用本次调用最初写 PID 文件时记录的
    # $pidWrittenAtUtc（记为 T1，本次调用自己的 Start-Process 之后），当真正监听端口的是"另一次
    # 更早调用"启动的进程时，T1 仍然可能晚于那个更早进程的真实启动时间（两次调用间隔一旦超过 2
    # 秒容差就会误判——实测两次连续调用间隔可达 2 秒以上，例如中间穿插了一次 -Detach 就绪轮询或
    # 一次 Get-Process 诊断调用）。唯一必然不早于该进程真实启动时间的值就是它自己的
    # `(Get-Process -Id <realPid>).StartTime`——直接查出来当作写入的时间戳，不使用任何本次调用
    # 内部产生的时间代理值；查询失败（进程在极短窗口内退出等罕见竞态）时退化为 $pidWrittenAtUtc
    # （T1）兜底，好于完全不写。
    $realPids = Get-ListenOwningProcessIds -Port (Get-ListenPort -ListenValue $Listen)
    if ($realPids.Count -gt 0 -and ($realPids -notcontains $proc.Id)) {
        Write-Host "  提示：Start-Process 记录的 PID $($proc.Id) 与真正监听端口的 PID($($realPids -join ', '))不一致，已按端口监听结果重写 PID 文件" -ForegroundColor Yellow
        [System.IO.File]::WriteAllText($PidFile, [string]$realPids[0], (New-Object System.Text.UTF8Encoding($false)))
        $realProcForRewrite = Get-Process -Id $realPids[0] -ErrorAction SilentlyContinue
        $rewriteWrittenAtUtc = $pidWrittenAtUtc
        if ($null -ne $realProcForRewrite) {
            try { $rewriteWrittenAtUtc = $realProcForRewrite.StartTime.ToUniversalTime() } catch { $rewriteWrittenAtUtc = $pidWrittenAtUtc }
        }
        Write-VerdaccioIdentityMeta -MetaPath $PidMetaFile -ProcessId $realPids[0] -ConfigPathValue $ConfigPath -VerdaccioBinValue $verdaccioBin -ListenValue $Listen -WrittenAtUtc $rewriteWrittenAtUtc
    }
    Write-Host "  停止：powershell -File $($MyInvocation.MyCommand.Path) -Stop"
    Write-Host "  状态：powershell -File $($MyInvocation.MyCommand.Path) -Status"
} else {
    Write-Step "前台启动 Verdaccio（--listen $Listen，Ctrl+C 停止）"
    & node $verdaccioBin --config $ConfigPath --listen $Listen
}
