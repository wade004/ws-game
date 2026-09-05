<#
.SYNOPSIS
    仓库根一键门禁脚本（见 11_工程规范与测试.md 第 8 节"提交门槛清单"）：依次跑 .NET 构建与
    测试、Python 数据/工具链校验、禁用词扫描、构建产物同步、Unity 编译检查与测试、独立版构建
    与无人值守冒烟，逐步打印耗时与结果，结束时汇总一张表；任一步失败，整体以非 0 退出码结束。

.PARAMETER SkipUnity
    跳过 Unity 相关四步（编译检查、EditMode、PlayMode、独立版构建 + 冒烟）；只跑 .NET/Python/
    禁用词/DLL 同步几步。同一仓库内并行有人独占 Unity 编辑器时用这个开关。

.PARAMETER SkipSmoke
    仍跑 Unity 独立版构建，但跳过"-gf-smoke 无人值守冒烟"这一子步骤（-SkipUnity 已整体跳过
    Unity 时本开关不生效）。

.PARAMETER ArtifactsPath
    dotnet build/test 的 --artifacts-path。默认 <仓库根>\bin\_check_artifacts（"bin"
    这一层已被 .gitignore 的 `bin/` 规则忽略，不会误入库）；Unity 编译日志/测试结果 XML/
    独立版构建产物也落在这个目录的 `unity\` 子目录下。

.PARAMETER UnityExe
    Unity 可执行文件完整路径。默认按 Unity Hub 常见安装位置尝试
    `%ProgramFiles%\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe`；找不到则退化为裸文件名
    `Unity.exe`（要求已在 PATH 上），仍找不到时对应步骤记为失败并在明细里提示改用本参数显式
    指定。

.PARAMETER Configuration
    dotnet 构建配置，默认 Release。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。
    本脚本只读跑校验/测试/构建，不修改仓库内容（`toolchain/gen_event_constants.py`/
    `gen_placeholder_assets.py` 都用 `--check` 只读校验模式，不落地写文件）。
#>
param(
    [switch]$SkipUnity,
    [switch]$SkipSmoke,
    [string]$ArtifactsPath = "",
    [string]$UnityExe = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$SolutionPath = Join-Path $RepoRoot "Core.sln"

if ($ArtifactsPath -eq "") {
    $ArtifactsPath = Join-Path $RepoRoot "bin\_check_artifacts"
}
if (-not (Test-Path $ArtifactsPath)) {
    New-Item -ItemType Directory -Force -Path $ArtifactsPath | Out-Null
}
$UnityOutDir = Join-Path $ArtifactsPath "unity"
if (-not (Test-Path $UnityOutDir)) {
    New-Item -ItemType Directory -Force -Path $UnityOutDir | Out-Null
}

# -----------------------------------------------------------------------------
# 步骤汇总基础设施
# -----------------------------------------------------------------------------

$script:Results = New-Object System.Collections.Generic.List[Object]

function Write-StepHeader {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

# $Action 是一个不带参数的 scriptblock：约定返回 $true 表示通过、$false 表示失败；抛异常同样
# 记为失败（异常消息进明细列）。任一步骤失败都不会中断后续步骤（"顺序执行并汇总"，见任务书）。
function Invoke-CheckStep {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-StepHeader $Name
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $ok = $false
    $detail = ""
    try {
        $result = & $Action
        if ($null -eq $result) {
            $ok = $true
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

# 跑一个原生可执行文件并按退出码判定通过/失败（PowerShell 5.1 对原生命令非零退出码不会抛出
# 终止性异常，需要手动读 $LASTEXITCODE；命令本身找不到会抛异常，由 Invoke-CheckStep 的 catch
# 接住）。
function Test-NativeExitCode {
    param(
        [string]$Exe,
        [string[]]$ArgList
    )
    & $Exe @ArgList
    return ($LASTEXITCODE -eq 0)
}

# -----------------------------------------------------------------------------
# 1. dotnet build
# -----------------------------------------------------------------------------
Invoke-CheckStep "dotnet build Core.sln -c $Configuration" {
    Test-NativeExitCode "dotnet" @("build", $SolutionPath, "-c", $Configuration, "--artifacts-path", $ArtifactsPath)
}

# -----------------------------------------------------------------------------
# 2. dotnet test（六工程；未加任何 --filter，默认含 Category=Perf 的性能基线测试）
# -----------------------------------------------------------------------------
Invoke-CheckStep "dotnet test Core.sln -c $Configuration --no-build（六工程，含 Perf 类别）" {
    Test-NativeExitCode "dotnet" @("test", $SolutionPath, "-c", $Configuration, "--no-build", "--artifacts-path", $ArtifactsPath)
}

# -----------------------------------------------------------------------------
# 3. 数据校验（合并根：data/_framework + data/_sample）
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/validate_data.py（合并根）" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/validate_data.py")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 4. 事件常量生成器一致性检查
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/gen_event_constants.py --check" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/gen_event_constants.py", "--check")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 5. 占位资产生成器一致性检查
# -----------------------------------------------------------------------------
Invoke-CheckStep "python toolchain/gen_placeholder_assets.py --check" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("toolchain/gen_placeholder_assets.py", "--check")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 6. toolchain 自身的 pytest 套件
# -----------------------------------------------------------------------------
Invoke-CheckStep "python -m pytest toolchain/tests -q" {
    Push-Location $RepoRoot
    try {
        Test-NativeExitCode "python" @("-m", "pytest", "toolchain/tests", "-q")
    } finally {
        Pop-Location
    }
}

# -----------------------------------------------------------------------------
# 7. 禁用词扫描
#    a) 全仓库不得出现某个具体游戏代号（见 CLAUDE.md 硬性规则；本文件下面用字符串拼接构造该词、
#       不直接拼出完整拼写，避免本脚本自身的源码触发这一步扫描），排除 .git/bin/obj/Library/
#       StreamingAssets/dist 几个构建期/缓存目录，并排除本脚本自身（同一原因）。
#    b) architecture 正文（00~14 号文档 + adr/）不得出现具体引擎/语言/框架/工具名，
#       immunity 例外（含 unity 子串但不是该词本身）。
# -----------------------------------------------------------------------------
function Get-ScannableFiles {
    param([string]$Root, [string[]]$ExtraExcludeFullNames)
    $excludedDirs = @(".git", "bin", "obj", "Library", "StreamingAssets", "dist")
    Get-ChildItem -Path $Root -Recurse -File -Force -ErrorAction SilentlyContinue | Where-Object {
        $relative = $_.FullName.Substring($Root.Length).TrimStart("\", "/")
        $segments = $relative -split "[\\/]"
        $hit = $false
        foreach ($seg in $segments) {
            foreach ($ex in $excludedDirs) {
                if ($seg -ieq $ex) { $hit = $true }
            }
        }
        if ($ExtraExcludeFullNames -contains $_.FullName) { $hit = $true }
        -not $hit
    }
}

Invoke-CheckStep "禁用词扫描：全仓库不出现具体游戏代号" {
    # 见上方注释：字符串拼接构造被扫描词，避免脚本自身源码里出现完整拼写。
    $bannedCodename = "note" + "moss"
    $files = Get-ScannableFiles -Root $RepoRoot -ExtraExcludeFullNames @($PSCommandPath)
    $hits = @()
    foreach ($f in $files) {
        try {
            $m = Select-String -Path $f.FullName -Pattern $bannedCodename -SimpleMatch -CaseSensitive:$false -ErrorAction SilentlyContinue
            if ($m) { $hits += $m }
        } catch {
            # 二进制文件等读取失败直接跳过，不计入命中
        }
    }
    if ($hits.Count -gt 0) {
        $lines = $hits | ForEach-Object { "$($_.Path):$($_.LineNumber)" }
        throw "发现 $($hits.Count) 处具体游戏代号命中：`n$($lines -join "`n")"
    }
    $true
}

Invoke-CheckStep "禁用词扫描：architecture 正文不出现引擎/语言/框架/工具名（immunity 例外）" {
    $targets = @()
    $targets += Get-ChildItem -Path (Join-Path $RepoRoot "architecture") -Filter "0*.md" -File -ErrorAction SilentlyContinue
    $targets += Get-ChildItem -Path (Join-Path $RepoRoot "architecture") -Filter "1*.md" -File -ErrorAction SilentlyContinue
    $adrDir = Join-Path $RepoRoot "architecture\adr"
    if (Test-Path $adrDir) {
        $targets += Get-ChildItem -Path $adrDir -Filter "*.md" -File -ErrorAction SilentlyContinue
    }

    # unity 单独用左右非字母边界匹配，避免命中 immunity；其余几个词本身不太会作为其它中文/英文
    # 词的子串出现，按普通子串匹配即可（与任务验收命令 grep -niwE 的整词语义等价）。
    $unityPattern = "(?<![A-Za-z])unity(?![A-Za-z])"
    $plainWords = @("c#", "csharp", "\.net", "xunit", "python", "powershell")
    $combinedPattern = $unityPattern + "|" + ($plainWords -join "|")

    $hits = @()
    foreach ($f in $targets) {
        $m = Select-String -Path $f.FullName -Pattern $combinedPattern -AllMatches -CaseSensitive:$false -ErrorAction SilentlyContinue
        if ($m) { $hits += $m }
    }
    if ($hits.Count -gt 0) {
        $lines = $hits | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
        throw "发现 $($hits.Count) 处技术名命中：`n$($lines -join "`n")"
    }
    $true
}

# -----------------------------------------------------------------------------
# 8. build.ps1 -SkipTests（同步六个核心 DLL 到 Unity 适配层包 + 同步内容数据集）
#    另起一个 powershell 子进程跑，避免 build.ps1 内部的 exit 语句连带终止本脚本。
# -----------------------------------------------------------------------------
Invoke-CheckStep "build.ps1 -SkipTests（同步 DLL）" {
    $buildScript = Join-Path $RepoRoot "build.ps1"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript -SkipTests
    return ($LASTEXITCODE -eq 0)
}

# -----------------------------------------------------------------------------
# Unity 相关四步（-SkipUnity 时整体跳过；见 adapters/unity/README.md"命令行跑测试"一节，
# 命令写法与该节保持一致：-runTests 不与 -quit 同传，PlayMode 不加 -nographics）。
# -----------------------------------------------------------------------------
function Resolve-UnityExe {
    param([string]$Explicit)
    if ($Explicit -ne "") {
        return $Explicit
    }
    $candidate = Join-Path $env:ProgramFiles "Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe"
    if (Test-Path $candidate) {
        return $candidate
    }
    # 找不到固定安装路径时退化为裸文件名，指望 PATH 上能解析到；解析不到会在调用处抛异常。
    return "Unity.exe"
}

if ($SkipUnity) {
    Add-SkippedStep "Unity 编译检查" "-SkipUnity"
    Add-SkippedStep "Unity EditMode 测试" "-SkipUnity"
    Add-SkippedStep "Unity PlayMode 测试" "-SkipUnity"
    Add-SkippedStep "独立版构建 + -gf-smoke 冒烟（连续模式默认流程）" "-SkipUnity"
    Add-SkippedStep "独立版 -gf-smoke-discrete 冒烟（离散模式链路）" "-SkipUnity"
} else {
    $resolvedUnityExe = Resolve-UnityExe -Explicit $UnityExe
    $unityProjectPath = Join-Path $RepoRoot "adapters\unity"

    Invoke-CheckStep "Unity 编译检查" {
        $log = Join-Path $UnityOutDir "compile.log"
        Test-NativeExitCode $resolvedUnityExe @(
            "-batchmode", "-nographics", "-quit",
            "-projectPath", $unityProjectPath,
            "-logFile", $log
        )
    }

    Invoke-CheckStep "Unity EditMode 测试" {
        $resultsXml = Join-Path $UnityOutDir "editmode.xml"
        $log = Join-Path $UnityOutDir "editmode.log"
        $exitOk = Test-NativeExitCode $resolvedUnityExe @(
            "-batchmode", "-nographics",
            "-projectPath", $unityProjectPath,
            "-runTests", "-testPlatform", "EditMode",
            "-testResults", $resultsXml,
            "-logFile", $log
        )
        if (-not $exitOk) { return $false }
        if (-not (Test-Path $resultsXml)) { return $false }
        # NUnit 结果 XML：根节点 result 属性非 Passed 视为失败（覆盖 Unity 某些版本"有失败用例
        # 但进程退出码仍为 0"的已知情况，不能只信退出码）。
        [xml]$xml = Get-Content -Path $resultsXml -Raw
        $root = $xml.DocumentElement
        return ($root.result -eq "Passed")
    }

    Invoke-CheckStep "Unity PlayMode 测试" {
        $resultsXml = Join-Path $UnityOutDir "playmode.xml"
        $log = Join-Path $UnityOutDir "playmode.log"
        # PlayMode 不加 -nographics（见 adapters/unity/README.md 判断记录：需要真实渲染/输入子系统）。
        $exitOk = Test-NativeExitCode $resolvedUnityExe @(
            "-batchmode",
            "-projectPath", $unityProjectPath,
            "-runTests", "-testPlatform", "PlayMode",
            "-testResults", $resultsXml,
            "-logFile", $log
        )
        if (-not $exitOk) { return $false }
        if (-not (Test-Path $resultsXml)) { return $false }
        [xml]$xml = Get-Content -Path $resultsXml -Raw
        $root = $xml.DocumentElement
        return ($root.result -eq "Passed")
    }

    Invoke-CheckStep "独立版构建 + -gf-smoke 冒烟（连续模式默认流程）" {
        $buildLog = Join-Path $UnityOutDir "build.log"
        $exePath = Join-Path $UnityOutDir "Shell.exe"
        $buildOk = Test-NativeExitCode $resolvedUnityExe @(
            "-batchmode", "-nographics", "-quit",
            "-projectPath", $unityProjectPath,
            "-buildWindows64Player", $exePath,
            "-logFile", $buildLog
        )
        if (-not $buildOk) { return $false }
        if (-not (Test-Path $exePath)) { return $false }

        if ($SkipSmoke) {
            Write-Host "已跳过 -gf-smoke 冒烟子步骤（-SkipSmoke），只验证了构建产物存在。" -ForegroundColor Yellow
            return $true
        }

        # 注意不加 -nographics（见包 README"独立版无头冒烟"判断记录）。
        $smokeLog = Join-Path $UnityOutDir "smoke_player.log"
        $smokeOk = Test-NativeExitCode $exePath @(
            "-batchmode", "-gf-smoke",
            "-logFile", $smokeLog,
            "-screen-width", "800", "-screen-height", "600"
        )
        if (-not $smokeOk) { return $false }
        if (-not (Test-Path $smokeLog)) { return $false }
        $logText = Get-Content -Path $smokeLog -Raw
        return ($logText -match "\[GF-SMOKE\] RESULT=OK")
    }

    # H4 新增：离散链路冒烟（-gf-smoke-discrete，见 Adapter.Unity.Shell.SmokeRunner.RunDiscreteSequence
    # 判断记录）——同一份独立版构建产物（上一步已生成），另起一次进程跑离散分支，验证"进入战斗→
    # awaiting_input→结束回合→AI 行动→战斗结束"这条链路本身（-SkipSmoke 时同样跳过，只验证过
    # 构建产物存在这一步已经在上一步做过，本步不重复）。
    Invoke-CheckStep "独立版 -gf-smoke-discrete 冒烟（离散模式链路）" {
        $exePath = Join-Path $UnityOutDir "Shell.exe"
        if (-not (Test-Path $exePath)) { return $false }

        if ($SkipSmoke) {
            Write-Host "已跳过 -gf-smoke-discrete 冒烟子步骤（-SkipSmoke）。" -ForegroundColor Yellow
            return $true
        }

        $smokeLog = Join-Path $UnityOutDir "smoke_player_discrete.log"
        $smokeOk = Test-NativeExitCode $exePath @(
            "-batchmode", "-gf-smoke-discrete",
            "-logFile", $smokeLog,
            "-screen-width", "800", "-screen-height", "600"
        )
        if (-not $smokeOk) { return $false }
        if (-not (Test-Path $smokeLog)) { return $false }
        $logText = Get-Content -Path $smokeLog -Raw
        return ($logText -match "\[GF-SMOKE\] RESULT=OK") -and ($logText -match "step=discrete_round ok")
    }
}

# -----------------------------------------------------------------------------
# 汇总
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "==== 汇总 ====" -ForegroundColor Cyan
$script:Results | Format-Table -AutoSize Step, Result, Seconds, Detail | Out-String -Width 4096 | Write-Host

$failed = $script:Results | Where-Object { $_.Result -eq "FAIL" }
$totalSeconds = ($script:Results | Measure-Object -Property Seconds -Sum).Sum

if ($failed.Count -gt 0) {
    Write-Host "门禁失败：$($failed.Count) 步未通过（共 $($script:Results.Count) 步，总用时 ${totalSeconds}s）。" -ForegroundColor Red
    exit 1
} else {
    Write-Host "门禁通过：全部 $($script:Results.Count) 步（总用时 ${totalSeconds}s）。" -ForegroundColor Green
    exit 0
}
