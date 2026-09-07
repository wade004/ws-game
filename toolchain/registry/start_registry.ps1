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
    停止由 -Detach 启动、PID 记录在 -PidFile 的 Verdaccio 进程；找不到 PID 文件或进程已不存在时
    只打印提示，不报错退出（幂等）。传 -Stop 时忽略其余参数（-Listen/-ConfigPath 等）。

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
#>
param(
    [string]$ConfigPath = "",
    [string]$Listen = "127.0.0.1:4873",
    [switch]$Detach,
    [string]$PidFile = "",
    [switch]$Stop,
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

# -----------------------------------------------------------------------------
# -Stop：独立分支，直接处理完就退出。
# -----------------------------------------------------------------------------
if ($Stop) {
    Write-Step "-Stop：停止后台 Verdaccio 进程"
    if (-not (Test-Path $PidFile)) {
        Write-Host "  找不到 PID 文件：$PidFile（未在跑，或从未用 -Detach 启动过），无需操作" -ForegroundColor Yellow
        exit 0
    }
    $recordedPid = (Get-Content -Path $PidFile -Raw).Trim()
    $proc = Get-Process -Id $recordedPid -ErrorAction SilentlyContinue
    if ($null -eq $proc) {
        Write-Host "  PID $recordedPid 对应的进程已不存在，直接清理 PID 文件" -ForegroundColor Yellow
        Remove-Item -Path $PidFile -Force
        exit 0
    }
    Stop-Process -Id $recordedPid -Force -Confirm:$false
    Start-Sleep -Milliseconds 300
    Remove-Item -Path $PidFile -Force -ErrorAction SilentlyContinue
    Write-Host "  已停止 PID $recordedPid，已清理 $PidFile" -ForegroundColor Green
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
    Write-Host "  停止：powershell -File $($MyInvocation.MyCommand.Path) -Stop"
} else {
    Write-Step "前台启动 Verdaccio（--listen $Listen，Ctrl+C 停止）"
    & node $verdaccioBin --config $ConfigPath --listen $Listen
}
