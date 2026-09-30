<#
.SYNOPSIS
    生成 ws-game 框架手册（API 参考 + 模块 README 概念文档 + 架构文档）到 docs/manual/_site/。

.DESCRIPTION
    步骤（任一步失败即以非零退出码终止，不静默跳过）：
      1. dotnet tool restore        —— 按 .config/dotnet-tools.json 恢复 docfx（需能访问 NuGet 源）。
      2. dotnet build Core.sln -c Release —— 保证源码可编译、XML 注释格式无误（可用 -SkipBuild 跳过，
         build.ps1 -Dist 调用时已经构建过就跳过）。
      3. python toolchain/gen_manual_toc.py —— 扫描全部已入库 README 与 architecture/ 文档，
         生成 docs/manual/concepts/toc.yml 与 concepts/architecture/toc.yml。
      4. dotnet docfx docfx.json     —— 先从 6 个核心类库 + 表现层 + 桩适配层 + 诊断转发工程的源码注释
         抽取 API 元数据（docs/manual/api/*.yml），再和概念文档一起构建站点。

    产物：docs/manual/_site/（静态站点）。API 元数据、toc、站点、日志都是生成物，已在 .gitignore。
    浏览：dotnet docfx serve docs/manual/_site（直接用 file:// 打开会缺搜索与目录脚本）。

    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。

.PARAMETER SkipBuild
    跳过第 2 步 dotnet build（调用方已经构建过 Release 产物时使用）。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File docs\manual\build.ps1
#>
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$ManualDir = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $ManualDir "..\..")).Path
$SiteDir = Join-Path $ManualDir "_site"
$LogPath = Join-Path $ManualDir "_docfx.log"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

function Stop-Failed {
    param([string]$Message)
    Write-Host ""
    Write-Host "手册生成失败：$Message" -ForegroundColor Red
    exit 1
}

# 原生命令输出含中文路径，统一按 UTF-8 解码；结束时还原。
$PreviousOutputEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Push-Location $RepoRoot
try {
    # ---- 1. 恢复 docfx -------------------------------------------------
    Write-Step "1/4 dotnet tool restore（恢复 docfx）"
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        Stop-Failed "dotnet tool restore 失败（退出码 $LASTEXITCODE）。docfx 由 .config/dotnet-tools.json 声明，恢复需要能访问 NuGet 源（nuget.org）；离线机器请先在联网环境执行一次以填充本机 NuGet 缓存。"
    }

    # ---- 2. 构建 -------------------------------------------------------
    if ($SkipBuild) {
        Write-Step "2/4 dotnet build（已按 -SkipBuild 跳过）"
    } else {
        Write-Step "2/4 dotnet build Core.sln -c Release"
        & dotnet build Core.sln -c Release --nologo
        if ($LASTEXITCODE -ne 0) {
            Stop-Failed "dotnet build Core.sln -c Release 失败（退出码 $LASTEXITCODE）。"
        }
    }

    # ---- 3. 生成概念文档目录 ---------------------------------------------
    Write-Step "3/4 生成概念文档与架构文档目录（toolchain/gen_manual_toc.py）"
    $python = $null
    foreach ($candidate in @("python", "python3")) {
        $cmd = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($cmd) { $python = $cmd.Source; break }
    }
    if (-not $python) {
        Stop-Failed "找不到 python（需要 3.8+ 运行 toolchain/gen_manual_toc.py）。"
    }
    & $python (Join-Path $RepoRoot "toolchain\gen_manual_toc.py")
    if ($LASTEXITCODE -ne 0) {
        Stop-Failed "gen_manual_toc.py 失败（退出码 $LASTEXITCODE）。"
    }

    # ---- 4. docfx（元数据 + 站点）-----------------------------------------
    Write-Step "4/4 dotnet docfx docs/manual/docfx.json"
    if (Test-Path $SiteDir) {
        Remove-Item -Path $SiteDir -Recurse -Force -Confirm:$false
    }
    $stale = Get-ChildItem -Path (Join-Path $ManualDir "api") -Filter "*.yml" -File -ErrorAction SilentlyContinue
    if ($stale) {
        $stale | Remove-Item -Force -Confirm:$false
    }

    $docfxOutput = & dotnet docfx (Join-Path $ManualDir "docfx.json")
    $docfxExit = $LASTEXITCODE
    $docfxOutput | Out-File -FilePath $LogPath -Encoding utf8

    # 控制台只回显非逐条警告的行（警告逐条落日志，避免刷屏）。
    $docfxOutput | Where-Object { $_ -notmatch '^warning:|: warning ' } | ForEach-Object { Write-Host $_ }

    $warningCount = @($docfxOutput | Where-Object { $_ -match '^warning:|: warning ' }).Count
    $errorCount = @($docfxOutput | Where-Object { $_ -match '^error:' }).Count
    $summaryError = $docfxOutput | Select-String -Pattern '^\s*(\d+) error\(s\)' | Select-Object -Last 1
    if ($summaryError) {
        $errorCount = [Math]::Max($errorCount, [int]$summaryError.Matches[0].Groups[1].Value)
    }

    if ($docfxExit -ne 0 -or $errorCount -gt 0) {
        Stop-Failed "docfx 退出码 $docfxExit，error $errorCount 条；完整输出见 $LogPath。"
    }
    if (-not (Test-Path (Join-Path $SiteDir "index.html"))) {
        Stop-Failed "docfx 报告成功，但 $SiteDir\index.html 不存在。"
    }

    # 概念文档页数必须与 gen_manual_toc.py 从 git 清单算出的期望值一致：多了说明通配吃进了本地
    # 被忽略的文件（需补 docfx.json 的 exclude），少了说明有文档被丢。
    $expectedPages = [int](Get-Content -Path (Join-Path $ManualDir "concepts\expected_pages.txt") -TotalCount 1)
    $conceptPages = @(Get-ChildItem -Path (Join-Path $SiteDir "concepts") -Recurse -Filter "*.html" -File | Where-Object { $_.Name -ne "toc.html" }).Count
    if ($conceptPages -ne $expectedPages) {
        Stop-Failed "概念文档页数不符：站点 concepts/ 下 $conceptPages 个 html，git 清单期望 $expectedPages 个。"
    }

    $apiPages =@(Get-ChildItem -Path (Join-Path $SiteDir "api") -Filter "*.html" -File).Count
    $siteFiles = @(Get-ChildItem -Path $SiteDir -Recurse -File).Count
    Write-Host ""
    Write-Host ("手册已生成：{0}（{1} 个文件，其中 API 页面 {2} 个、概念/架构文档页 {3} 个；docfx warning {4} 条，error 0 条；完整输出 {5}）" -f $SiteDir, $siteFiles, $apiPages, $conceptPages, $warningCount, $LogPath) -ForegroundColor Green
}
finally {
    Pop-Location
    [Console]::OutputEncoding = $PreviousOutputEncoding
}

exit 0
