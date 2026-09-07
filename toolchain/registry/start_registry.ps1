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
    监听该端口的进程并停止（见下方判断记录——PID 文件记录的 PID 不一定是真正监听端口的那个
    进程），最后核验端口确实已释放。找不到 PID 文件、记录的进程已不存在、或端口本来就没人监听时
    只打印提示，不报错退出（幂等，可重复调用）。传 -Stop 时忽略 -ConfigPath/-SkipInstall。

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
    foreach ($p in $owningPids) {
        $proc = Get-Process -Id $p -ErrorAction SilentlyContinue
        $name = "?"
        if ($null -ne $proc) { $name = $proc.ProcessName }
        Write-Host "    真实监听进程 PID $p（$name）" -ForegroundColor Green
    }
    exit 0
}

# -----------------------------------------------------------------------------
# -Stop：独立分支，直接处理完就退出。先按 -PidFile 停一次，再按端口兜底查一次仍在监听的进程一并
# 停止，最后轮询核验端口确实已释放——三步缺一不可，见脚本头判断记录二。幂等：重复调用、或本来就
# 没在跑时都正常退出 0。
# -----------------------------------------------------------------------------
if ($Stop) {
    Write-Step "-Stop：停止 Verdaccio 进程（--listen $Listen）"
    $port = Get-ListenPort -ListenValue $Listen
    $stoppedAny = $false

    if (Test-Path $PidFile) {
        $recordedPid = (Get-Content -Path $PidFile -Raw).Trim()
        $proc = Get-Process -Id $recordedPid -ErrorAction SilentlyContinue
        if ($null -ne $proc) {
            Stop-Process -Id $recordedPid -Force -Confirm:$false
            Write-Host "  已按 PID 文件停止 PID $recordedPid" -ForegroundColor Green
            $stoppedAny = $true
        } else {
            Write-Host "  PID 文件记录的 PID $recordedPid 对应进程已不存在（继续按端口兜底核实）" -ForegroundColor Yellow
        }
        Remove-Item -Path $PidFile -Force -ErrorAction SilentlyContinue
    } else {
        Write-Host "  找不到 PID 文件：$PidFile（继续按端口兜底核实）" -ForegroundColor Yellow
    }

    # 端口兜底：不管上面 PID 文件那一步有没有找到/停掉进程，都再按端口查一次真正监听 $port 的
    # 进程——覆盖"PID 文件记录的是包装进程/PID 已被复用给别的进程/PID 文件丢失但服务仍在跑"等
    # 场景（见脚本头判断记录二）。
    Start-Sleep -Milliseconds 300
    $owningPids = Get-ListenOwningProcessIds -Port $port
    foreach ($p in $owningPids) {
        $proc = Get-Process -Id $p -ErrorAction SilentlyContinue
        if ($null -ne $proc) {
            Write-Host "  端口 $port 仍被 PID $p（$($proc.ProcessName)）监听，一并停止" -ForegroundColor Yellow
            Stop-Process -Id $p -Force -Confirm:$false
            $stoppedAny = $true
        }
    }

    # 核验端口释放：最多轮询 5 秒（Stop-Process 是异步请求终止，端口释放有一点延迟）。
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

    [System.IO.File]::WriteAllText($PidFile, [string]$proc.Id, (New-Object System.Text.UTF8Encoding($false)))
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
    # 的不一致时（正常情况下不会发生——本脚本已经是直接起 node、不经过 npx 包装进程，这里只是
    # 防御未来环境/版本漂移导致的偏差）以端口查到的为准重写 -PidFile，保证 -Stop/-Status 读到的
    # 始终是真正监听端口的那个 PID。
    $realPids = Get-ListenOwningProcessIds -Port (Get-ListenPort -ListenValue $Listen)
    if ($realPids.Count -gt 0 -and ($realPids -notcontains $proc.Id)) {
        Write-Host "  提示：Start-Process 记录的 PID $($proc.Id) 与真正监听端口的 PID($($realPids -join ', '))不一致，已按端口监听结果重写 PID 文件" -ForegroundColor Yellow
        [System.IO.File]::WriteAllText($PidFile, [string]$realPids[0], (New-Object System.Text.UTF8Encoding($false)))
    }
    Write-Host "  停止：powershell -File $($MyInvocation.MyCommand.Path) -Stop"
    Write-Host "  状态：powershell -File $($MyInvocation.MyCommand.Path) -Status"
} else {
    Write-Step "前台启动 Verdaccio（--listen $Listen，Ctrl+C 停止）"
    & node $verdaccioBin --config $ConfigPath --listen $Listen
}
