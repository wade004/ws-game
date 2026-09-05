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

# $Action 是一个不带参数的 scriptblock：约定返回值有三种形状——
#   1) $null：通过，明细列留空（既有大多数步骤的写法）；
#   2) $true/$false：直接就是通过/失败，明细列留空；
#   3) [PSCustomObject]@{ Ok = <bool>; Detail = <string> }：通过/失败取 Ok，明细列取 Detail
#      （H5 新增，供 Unity EditMode/PlayMode 步骤把 total/passed/failed 计数写进汇总表）。
# 抛异常同样记为失败（异常消息进明细列）。任一步骤失败都不会中断后续步骤（"顺序执行并汇总"，
# 见任务书）。
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

# H5 新增：跑一个"GUI 子系统"原生可执行文件（Unity.exe / 独立版 Shell.exe）并真正等待其退出、
# 拿到真实退出码。
#
# 判断记录（根因 + 为什么不能继续用 `& $Exe @ArgList` + $LASTEXITCODE）：Unity.exe 与独立版
# Shell.exe 都是 Windows GUI 子系统程序（不是控制台子系统程序）。PowerShell 的调用运算符 `&`
# 对 GUI 子系统程序只负责"启动"，不会阻塞等待其退出（这是 Windows 进程创建层面的行为，不是
# PowerShell 的 bug）——`& $Exe @ArgList` 这一行执行完就立即往下走，此时 Unity 可能才刚开始加载
# 许可证/域重载，$LASTEXITCODE 读到的是上一条别的原生命令留下的陈旧值。H4 版 check.ps1 的 Unity
# 四步之所以"0 秒内结束、编译步误判 PASS、其余步骤 FAIL"，根因正在这里：Unity 编译检查那一步
# 的 `& Unity.exe -quit` 一启动就立即返回（判定用的 $LASTEXITCODE 是陈旧值，凑巧还是 0），脚本
# 立即往下跑 EditMode/PlayMode/独立版构建，而这些新启动的 Unity 实例发现同一个工程已经被上一个
# （其实还在后台跑）Unity 实例打开，直接失败退出（EditMode/PlayMode 各自的 xml/log 里只有许可证
# 握手几行）。
#
# 改法：不用 `&`，改用 Start-Process -FilePath/-ArgumentList/-Wait/-PassThru/-NoNewWindow——
# -Wait 是 Start-Process 自己实现的"轮询进程句柄直到退出"，不依赖 GUI/控制台子系统的差异；
# -PassThru 拿到 Process 对象读真实 ExitCode（不再依赖 $LASTEXITCODE）。-ArgumentList 传数组
# （不是拼接成一整根字符串）：Start-Process 内部按数组元素分别加引号，含空格/中文的路径参数
# （如仓库路径、-logFile 输出路径）不需要调用方自己转义。
#
# $TimeoutSeconds > 0 时（独立版无人值守冒烟两步用到，见下方判断记录：无人值守冒烟一旦挂死，
# 没有人会去按任何键，必须有兜底）：不能再用 -Wait（-Wait 本身不接受超时参数），改成不传 -Wait
# 只传 -PassThru 拿到 Process 对象后，手工调用 Process.WaitForExit(毫秒) 限时等待；超时后
# WaitForExit 返回 $false，尝试 Kill 掉这个挂死的子进程（仅 Kill 本函数自己刚刚 Start-Process
# 启动的这一个句柄，不误杀任何其它进程），$TimedOut 置 $true、ExitCode 记为 -1（明确的失败标记，
# 不会与任何真实退出码混淆——真实 Windows 退出码不会是负数）。
#
# 判断记录（限时分支不传 -NoNewWindow，与"不限时"分支不同——已实测复现的 Windows PowerShell 5.1
# Start-Process 已知限制）：本函数最初两个分支都传 -NoNewWindow，实测（独立版冒烟两步）发现限时
# 分支下 `$proc.ExitCode` 读回空字符串——进程确实已退出（HasExited 为真、日志也证实 Shell.exe
# 内部 Application.Quit(0) 正常收尾），但 Start-Process 在"-PassThru 且不传 -Wait 却传了
# -NoNewWindow"这一种组合下返回的 Process 对象丢失了退出码句柄（去掉 -NoNewWindow 后同样的
# 手工 WaitForExit 流程能正确读到 ExitCode，实测反复复现两次，确认是这一个参数组合触发，不是
# 偶发）。"不限时"分支（-Wait -PassThru -NoNewWindow 三个一起传）不受影响——Start-Process 自己
# 实现的 -Wait 走的是另一条内部代码路径，能正确保留退出码，这也是任务书要求的默认写法，原样保留。
# 限时分支因此改为只传 -PassThru：本来就是 GUI 子系统程序（Unity/独立版 Shell.exe 本身不分配
# 控制台窗口），-batchmode 命令行参数已经保证不出现可见窗口，去掉 -NoNewWindow 不影响实际的
# "无人值守"效果，只是绕开这一条 Start-Process 自身的退出码丢失路径。
function Invoke-NativeAndWait {
    param(
        [string]$Exe,
        [string[]]$ArgList,
        [int]$TimeoutSeconds = 0
    )

    if ($TimeoutSeconds -le 0) {
        $proc = Start-Process -FilePath $Exe -ArgumentList $ArgList -Wait -PassThru -NoNewWindow
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

# H5 新增：Unity 四步开跑前检查"同一工程"是否已经有一个残留的 Unity.exe 进程在跑（例如上一次
# check.ps1 被中断、或另一个终端窗口手工开着 Unity Editor 独占了同一个工程的 .lock）——按命令行
# 里是否含本工程路径判断，不是"任意 Unity.exe 都算残留"（开发机上完全可能同时开着别的工程的
# Unity Editor）。发现残留只报告明确的失败信息（含 PID，方便手工定位/结束），不代为 Kill——
# 任务书硬性规则"不 kill 非本脚本启动的进程"，残留进程可能是人正在交互使用的 Editor 窗口。
function Test-NoResidualUnityProcess {
    param([string]$ProjectPath)

    try {
        $procs = Get-CimInstance -ClassName Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop
    } catch {
        # 查询进程列表本身失败（权限/WMI 服务异常等）：不能确认"没有残留"，但也不应该让整个门禁
        # 因为一次诊断性查询失败而跳过 Unity 步骤——按"未发现残留"处理，继续往下跑。
        return
    }

    $needle = $ProjectPath.TrimEnd("\", "/")
    $hit = $procs | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($needle) }
    if ($hit) {
        $pids = ($hit | ForEach-Object { $_.ProcessId }) -join ", "
        throw "检测到同一工程已有残留 Unity.exe 进程在运行（PID: $pids，工程路径 $ProjectPath），本脚本不会代为结束——请先手工关闭该 Unity Editor 窗口/进程，确认 $ProjectPath\Temp\UnityLockfile 已释放后重跑。"
    }
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
# 8. 版本一致性（版本可追溯任务新增，见 11_工程规范与测试.md 第 7 节"版本号必须可追溯到
#    对应的架构文档版本与数据 schema 版本组合"）：单一版本源仓库根 VERSION 文件必须与
#    adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json、
#    games/_template/package.json 两处 version 字段一致（含 games/_template 对适配层包的
#    依赖版本号），避免三处手改漏掉其中一处导致 dist 快照与源码割裂。只读比较，不改写任何文件。
# -----------------------------------------------------------------------------
Invoke-CheckStep "版本一致性：VERSION 与两个 package.json" {
    $versionPath = Join-Path $RepoRoot "VERSION"
    if (-not (Test-Path $versionPath)) {
        throw "找不到版本文件：$versionPath"
    }
    $version = (Get-Content -Path $versionPath -Raw).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "VERSION 内容格式非法：'$version'（需形如 X.Y.Z）"
    }

    $adapterPkgPath = Join-Path $RepoRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\package.json"
    $templatePkgPath = Join-Path $RepoRoot "games\_template\package.json"

    # 判断记录：两个 package.json 是不带 BOM 的 UTF-8；Windows PowerShell 5.1 的 Get-Content
    # 在没有 BOM 时按系统 ANSI 代码页猜编码，读中文会乱码甚至让 ConvertFrom-Json 报错，
    # 必须显式 -Encoding UTF8（与 build.ps1 打包步骤同一判断记录）。
    $adapterPkg = (Get-Content -Path $adapterPkgPath -Raw -Encoding UTF8) | ConvertFrom-Json
    $templatePkg = (Get-Content -Path $templatePkgPath -Raw -Encoding UTF8) | ConvertFrom-Json

    $mismatches = @()
    if ($adapterPkg.version -ne $version) {
        $mismatches += "adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json version=$($adapterPkg.version) != VERSION=$version"
    }
    if ($templatePkg.version -ne $version) {
        $mismatches += "games/_template/package.json version=$($templatePkg.version) != VERSION=$version"
    }
    $templateDepVersion = $templatePkg.dependencies."com.gamefoundation.adapter.unity"
    if ($templateDepVersion -ne $version) {
        $mismatches += "games/_template/package.json dependencies.com.gamefoundation.adapter.unity=$templateDepVersion != VERSION=$version"
    }

    if ($mismatches.Count -gt 0) {
        throw ("版本不一致：`n" + ($mismatches -join "`n"))
    }
    [PSCustomObject]@{ Ok = $true; Detail = "VERSION=$version，两个 package.json 一致" }
}

# -----------------------------------------------------------------------------
# 9. build.ps1 -SkipTests（同步六个核心 DLL 到 Unity 适配层包 + 同步内容数据集）
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

    # H5 起：Unity 四步一律用 Invoke-NativeAndWait（不再用 Test-NativeExitCode 那套
    # `& $Exe @ArgList` + $LASTEXITCODE——见该函数判断记录，对 GUI 子系统程序不阻塞，会导致本步骤
    # 在 Unity 真正跑完之前就误判结束）；每步开跑前先查一次同工程有没有残留 Unity.exe（见
    # Test-NoResidualUnityProcess）。

    Invoke-CheckStep "Unity 编译检查" {
        Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
        $log = Join-Path $UnityOutDir "compile.log"
        $proc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
            "-batchmode", "-nographics", "-quit",
            "-projectPath", $unityProjectPath,
            "-logFile", $log
        )
        [PSCustomObject]@{
            Ok     = ($proc.ExitCode -eq 0)
            Detail = "Unity 退出码 $($proc.ExitCode)"
        }
    }

    Invoke-CheckStep "Unity EditMode 测试" {
        Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
        $resultsXml = Join-Path $UnityOutDir "editmode.xml"
        $log = Join-Path $UnityOutDir "editmode.log"
        $proc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
            "-batchmode", "-nographics",
            "-projectPath", $unityProjectPath,
            "-runTests", "-testPlatform", "EditMode",
            "-testResults", $resultsXml,
            "-logFile", $log
        )
        if ($proc.ExitCode -ne 0) {
            return [PSCustomObject]@{ Ok = $false; Detail = "Unity 退出码 $($proc.ExitCode)" }
        }
        if (-not (Test-Path $resultsXml)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成结果 XML：$resultsXml" }
        }
        # NUnit 结果 XML：根节点 result 属性非 Passed 视为失败（覆盖 Unity 某些版本"有失败用例
        # 但进程退出码仍为 0"的已知情况，不能只信退出码）。
        [xml]$xml = Get-Content -Path $resultsXml -Raw
        $root = $xml.DocumentElement
        [PSCustomObject]@{
            Ok     = ($root.result -eq "Passed")
            Detail = "total=$($root.total) passed=$($root.passed) failed=$($root.failed)"
        }
    }

    Invoke-CheckStep "Unity PlayMode 测试" {
        Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
        $resultsXml = Join-Path $UnityOutDir "playmode.xml"
        $log = Join-Path $UnityOutDir "playmode.log"
        # PlayMode 不加 -nographics（见 adapters/unity/README.md 判断记录：需要真实渲染/输入子系统）。
        $proc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
            "-batchmode",
            "-projectPath", $unityProjectPath,
            "-runTests", "-testPlatform", "PlayMode",
            "-testResults", $resultsXml,
            "-logFile", $log
        )
        if ($proc.ExitCode -ne 0) {
            return [PSCustomObject]@{ Ok = $false; Detail = "Unity 退出码 $($proc.ExitCode)" }
        }
        if (-not (Test-Path $resultsXml)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成结果 XML：$resultsXml" }
        }
        [xml]$xml = Get-Content -Path $resultsXml -Raw
        $root = $xml.DocumentElement
        # H5 新增：除根节点 result 外，把 total/passed/failed 计数写进汇总表 Detail 列。
        [PSCustomObject]@{
            Ok     = ($root.result -eq "Passed")
            Detail = "total=$($root.total) passed=$($root.passed) failed=$($root.failed)"
        }
    }

    Invoke-CheckStep "独立版构建 + -gf-smoke 冒烟（连续模式默认流程）" {
        Test-NoResidualUnityProcess -ProjectPath $unityProjectPath
        $buildLog = Join-Path $UnityOutDir "build.log"
        $exePath = Join-Path $UnityOutDir "Shell.exe"
        $buildProc = Invoke-NativeAndWait -Exe $resolvedUnityExe -ArgList @(
            "-batchmode", "-nographics", "-quit",
            "-projectPath", $unityProjectPath,
            "-buildWindows64Player", $exePath,
            "-logFile", $buildLog
        )
        if ($buildProc.ExitCode -ne 0) {
            return [PSCustomObject]@{ Ok = $false; Detail = "Unity 构建退出码 $($buildProc.ExitCode)" }
        }
        if (-not (Test-Path $exePath)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成独立版产物：$exePath" }
        }

        if ($SkipSmoke) {
            Write-Host "已跳过 -gf-smoke 冒烟子步骤（-SkipSmoke），只验证了构建产物存在。" -ForegroundColor Yellow
            return [PSCustomObject]@{ Ok = $true; Detail = "已跳过 -gf-smoke（-SkipSmoke），只验证构建产物存在" }
        }

        # 注意不加 -nographics（见包 README"独立版无头冒烟"判断记录）。H5 新增：加 180s 超时保护
        # ——无人值守冒烟一旦挂死（例如场景资源加载死锁），没有人会去按任何键，Invoke-NativeAndWait
        # 的 -Wait 会无限期挂住整个门禁脚本，必须有兜底。
        $smokeLog = Join-Path $UnityOutDir "smoke_player.log"
        $smokeProc = Invoke-NativeAndWait -Exe $exePath -TimeoutSeconds 180 -ArgList @(
            "-batchmode", "-gf-smoke",
            "-logFile", $smokeLog,
            "-screen-width", "800", "-screen-height", "600"
        )
        if ($smokeProc.TimedOut) {
            return [PSCustomObject]@{ Ok = $false; Detail = "-gf-smoke 冒烟超过 180s 未退出，已强制结束（可能挂死）" }
        }
        if ($smokeProc.ExitCode -ne 0) {
            return [PSCustomObject]@{ Ok = $false; Detail = "独立版退出码 $($smokeProc.ExitCode)" }
        }
        if (-not (Test-Path $smokeLog)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成冒烟日志：$smokeLog" }
        }
        $logText = Get-Content -Path $smokeLog -Raw
        [PSCustomObject]@{
            Ok     = ($logText -match "\[GF-SMOKE\] RESULT=OK")
            Detail = "见 $smokeLog"
        }
    }

    # H4 新增：离散链路冒烟（-gf-smoke-discrete，见 Adapter.Unity.Shell.SmokeRunner.RunDiscreteSequence
    # 判断记录）——同一份独立版构建产物（上一步已生成），另起一次进程跑离散分支，验证"进入战斗→
    # awaiting_input→结束回合→AI 行动→战斗结束"这条链路本身（-SkipSmoke 时同样跳过，只验证过
    # 构建产物存在这一步已经在上一步做过，本步不重复）。
    Invoke-CheckStep "独立版 -gf-smoke-discrete 冒烟（离散模式链路）" {
        $exePath = Join-Path $UnityOutDir "Shell.exe"
        if (-not (Test-Path $exePath)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成独立版产物：$exePath" }
        }

        if ($SkipSmoke) {
            Write-Host "已跳过 -gf-smoke-discrete 冒烟子步骤（-SkipSmoke）。" -ForegroundColor Yellow
            return [PSCustomObject]@{ Ok = $true; Detail = "已跳过 -gf-smoke-discrete（-SkipSmoke）" }
        }

        # H5 新增：同上一步，180s 超时保护。
        $smokeLog = Join-Path $UnityOutDir "smoke_player_discrete.log"
        $smokeProc = Invoke-NativeAndWait -Exe $exePath -TimeoutSeconds 180 -ArgList @(
            "-batchmode", "-gf-smoke-discrete",
            "-logFile", $smokeLog,
            "-screen-width", "800", "-screen-height", "600"
        )
        if ($smokeProc.TimedOut) {
            return [PSCustomObject]@{ Ok = $false; Detail = "-gf-smoke-discrete 冒烟超过 180s 未退出，已强制结束（可能挂死）" }
        }
        if ($smokeProc.ExitCode -ne 0) {
            return [PSCustomObject]@{ Ok = $false; Detail = "独立版退出码 $($smokeProc.ExitCode)" }
        }
        if (-not (Test-Path $smokeLog)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成冒烟日志：$smokeLog" }
        }
        $logText = Get-Content -Path $smokeLog -Raw
        [PSCustomObject]@{
            Ok     = ($logText -match "\[GF-SMOKE\] RESULT=OK") -and ($logText -match "step=discrete_round ok")
            Detail = "见 $smokeLog"
        }
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
