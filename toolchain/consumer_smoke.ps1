<#
.SYNOPSIS
    消费方演练脚本（见 architecture/落地计划 3.5 节"新游戏如何消费本框架"、
    architecture/13_新游戏接入指南.md 第 7 节验收关卡）：从零搭建一个全新、独立于本仓库源码树的
    最小 Unity 消费方工程，只以 file: 相对路径引用本框架的分发包（dist/<version>/），完整走一遍
    games/_template/README.md 描述的接入步骤，验证"一个从未见过本仓库源码的新游戏工程，照着文档
    操作能不能真正跑起来"，而不是只靠工作台工程（adapters/unity，源码级直接引用本仓库内容）自证。

    覆盖范围（2026-09-07 补充口径，与 games/_template/README.md"已知限制"一节同一口径）：本脚本
    只验证"分发包能否被一个全新工程正确引用、装配、跑通模板自带的最小闭环（主菜单→新游戏→进图→
    移动→存档→读档→退出）"这一段基础设施链路是否通畅；不含战斗、技能、任务、掉落等任何具体游戏
    内容验收，也不代表某个真实游戏已完成接入验收——一个真实游戏仍需自行按
    architecture/13_新游戏接入指南.md 第 1 节七步逐项补齐并跑通自己的端到端验收。

.PARAMETER DistVersion
    要演练的分发包版本号。默认读取仓库根 VERSION 文件（单一版本源，见
    11_工程规范与测试.md 第 7 节）。

.PARAMETER WorkDir
    消费方工程的落地目录。默认落在本次会话 scratchpad 下的 consumer_smoke\（不进仓库、不提交）；
    每次运行都会先清空重建，模拟"全新工程"这一前提，不复用上一次运行的残留状态。

.PARAMETER UnityExe
    Unity 可执行文件完整路径。默认与 check.ps1 同一套解析逻辑（Unity Hub 常见安装位置）。

.PARAMETER SkipCleanWorkDir
    跳过"运行前清空 WorkDir"（调试用：保留上一次运行留下的工程，便于用 Unity Editor 打开检查失败
    原因）。默认不传——每次都是全新工程。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。UTF-8 with BOM（PS 5.1 默认按系统代码页读取
    不带 BOM 的脚本文件，中文字符/字符串字面量在无 BOM 时会被读错）。
    本脚本只读引用仓库内容（复制到 WorkDir 之外的独立目录），不修改仓库内任何文件（build.ps1
    -SyncOnly -Dist 会在仓库内的 dist/<version>/ 落地/覆盖分发包快照，这是 build.ps1 一贯的既有
    行为，不是本脚本新增的写入）。
#>
param(
    [string]$DistVersion = "",
    [string]$WorkDir = "",
    [string]$UnityExe = "",
    [switch]$SkipCleanWorkDir
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$VersionFilePath = Join-Path $RepoRoot "VERSION"

if ($DistVersion -eq "") {
    if (-not (Test-Path $VersionFilePath)) {
        Write-Host "找不到版本文件：$VersionFilePath" -ForegroundColor Red
        exit 1
    }
    $DistVersion = (Get-Content -Path $VersionFilePath -Raw).Trim()
}
if ($DistVersion -notmatch '^\d+\.\d+\.\d+$') {
    Write-Host "版本号格式非法：'$DistVersion'（需形如 X.Y.Z）" -ForegroundColor Red
    exit 1
}

if ($WorkDir -eq "") {
    $WorkDir = Join-Path $env:TEMP "gf_consumer_smoke"
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
$ResolvedUnityExe = Resolve-UnityExe -Explicit $UnityExe

# -----------------------------------------------------------------------------
# 步骤汇总基础设施（惯例同 check.ps1，独立一份——两个脚本各自可以单独运行，不互相依赖）。
# -----------------------------------------------------------------------------
$script:Results = New-Object System.Collections.Generic.List[Object]

function Write-StepHeader {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)
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
    $script:Results.Add([PSCustomObject]@{ Step = $Name; Result = if ($ok) { "PASS" } else { "FAIL" }; Seconds = $seconds; Detail = $detail })
    if ($ok) {
        Write-Host "[$Name] 通过，用时 ${seconds}s" -ForegroundColor Green
    } else {
        Write-Host "[$Name] 失败，用时 ${seconds}s" -ForegroundColor Red
    }
    return $ok
}

# 见 check.ps1 Invoke-NativeAndWait 同款判断记录：Unity.exe / 独立版 Shell.exe 都是 GUI 子系统
# 程序，PowerShell 的 `&` 调用运算符不会阻塞等待其退出，必须用 Start-Process -Wait/-PassThru。
# 不限时分支不再用 -Wait：PS 5.1 的 -Wait 等待的是整个进程树，Unity 退出后其 VBCSCompiler 编译
# 服务子进程还会再存活约 600 秒，-Wait 会把这约 600 秒也一起等掉；改用 Process.WaitForExit()
# 只等 Unity 自己的进程句柄，不受子进程影响，详见 check.ps1 Invoke-NativeAndWait 上方 2026-09-06
# 判断记录。两个分支都不传 -NoNewWindow，原因同样是 PS 5.1 的已知限制：-PassThru 不配 -Wait 时
# 若再传 -NoNewWindow，读回的 ExitCode 是空字符串；GUI 子系统程序加 -batchmode 本就不会弹窗，
# 去掉 -NoNewWindow 不影响无人值守效果。
function Invoke-NativeAndWait {
    param([string]$Exe, [string[]]$ArgList, [int]$TimeoutSeconds = 0)
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

# 判断记录：PowerShell 5.1 的 `-Encoding utf8` 恒带 BOM，Unity 的 manifest.json/package.json/
# asmdef JSON 解析器不容忍 BOM（实测复现："is not valid JSON: Non-whitespace before {[. Char: 65279"
# ——65279 正是 BOM 的码点）。本仓库既有的 package.json/asmdef 也一贯是不带 BOM 的 UTF-8（见
# build.ps1 判断记录），因此本脚本凡是要落地 Unity 会解析的 JSON/YAML 文本，一律用本函数写入，
# 不用 Set-Content -Encoding utf8。
function Set-Utf8NoBom {
    param([string]$Path, [string]$Content)
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Copy-TreeMirror {
    param([string]$SourceDir, [string]$DestDir)
    if (-not (Test-Path $SourceDir)) {
        return 0
    }
    if (-not (Test-Path $DestDir)) {
        New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
    }
    $count = 0
    Get-ChildItem -Path $SourceDir -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring((Resolve-Path $SourceDir).Path.Length).TrimStart('\', '/')
        $target = Join-Path $DestDir $relative
        $targetDir = Split-Path -Parent $target
        if (-not (Test-Path $targetDir)) {
            New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        }
        Copy-Item -Path $_.FullName -Destination $target -Force
        $count++
    }
    return $count
}

Write-Host "消费方演练：版本=$DistVersion，工作目录=$WorkDir，Unity=$ResolvedUnityExe" -ForegroundColor Cyan

$DistRoot = Join-Path $RepoRoot ("dist\" + $DistVersion)
$ConsumerProjectDir = Join-Path $WorkDir "ConsumerProject"
$ConsumerPackageStagingDir = Join-Path $WorkDir "packages\com.sample.game-consumer"
$UnityLogDir = Join-Path $WorkDir "logs"

# -----------------------------------------------------------------------------
# 1) 确保分发包快照存在（build.ps1 -SyncOnly -Dist <ver>，另起子进程跑，避免其 exit 语句
#    连带终止本脚本，惯例同 check.ps1 对 build.ps1 的调用方式）。
# -----------------------------------------------------------------------------
$step1 = Invoke-Step "build.ps1 -SyncOnly -Dist $DistVersion（确保分发包快照存在）" {
    $buildScript = Join-Path $RepoRoot "build.ps1"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript -SyncOnly -Dist $DistVersion
    return ($LASTEXITCODE -eq 0)
}
if (-not $step1) {
    Write-Host "分发包快照生成失败，后续步骤无法进行，提前退出。" -ForegroundColor Red
    $script:Results | Format-Table -AutoSize | Out-String -Width 4096 | Write-Host
    exit 1
}

if (-not (Test-Path $DistRoot)) {
    Write-Host "分发包快照不存在：$DistRoot" -ForegroundColor Red
    exit 1
}

# -----------------------------------------------------------------------------
# 2) 准备工作目录（全新工程，见 -SkipCleanWorkDir 参数说明）。
# -----------------------------------------------------------------------------
Invoke-Step "准备全新工作目录" {
    if ((-not $SkipCleanWorkDir) -and (Test-Path $WorkDir)) {
        Remove-Item -Path $WorkDir -Recurse -Force -Confirm:$false
    }
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
    New-Item -ItemType Directory -Force -Path $UnityLogDir | Out-Null
    $true
}

# -----------------------------------------------------------------------------
# 3) 复制并按 README 改名 games/_template -> com.sample.game-consumer（见
#    games/_template/README.md"复制为新游戏：改哪几处"1、2 两步——本脚本只做包名 +
#    三个 asmdef 的 name/引用两处改名，不改 C# 命名空间声明（任务书明确"命名空间不改"：
#    验证的是"两根合并加载 + Unity 包解析引用名"这条链路本身，不是把模板真的当一个新游戏来接）。
# -----------------------------------------------------------------------------
Invoke-Step "复制并改名 games/_template -> com.sample.game-consumer" {
    Copy-TreeMirror -SourceDir (Join-Path $DistRoot "games\_template") -DestDir $ConsumerPackageStagingDir | Out-Null

    # package.json：name 改名。
    $pkgJsonPath = Join-Path $ConsumerPackageStagingDir "package.json"
    $pkgJson = (Get-Content -Path $pkgJsonPath -Raw -Encoding UTF8) | ConvertFrom-Json
    $pkgJson.name = "com.sample.game-consumer"
    Set-Utf8NoBom -Path $pkgJsonPath -Content ($pkgJson | ConvertTo-Json -Depth 10)

    # 三个 asmdef：文件名 + JSON "name" 字段从 Game.Template(.Editor/.Tests) 改成
    # Game.Consumer(.Editor/.Tests)；Editor/Tests 两个 asmdef 的 references 数组里对
    # "Game.Template" 的引用同步改成 "Game.Consumer"（否则编辑器/测试程序集引用不到运行期程序集，
    # 见该 README 判断记录）。rootNamespace 与全部 .cs 文件的 namespace 声明均不改（"命名空间
    # 不改"，见本步骤顶部注释）。
    $asmdefRenames = @(
        @{ Dir = "Runtime"; Old = "Game.Template.asmdef"; New = "Game.Consumer.asmdef" },
        @{ Dir = "Editor"; Old = "Game.Template.Editor.asmdef"; New = "Game.Consumer.Editor.asmdef" },
        @{ Dir = "Tests\Runtime"; Old = "Game.Template.Tests.asmdef"; New = "Game.Consumer.Tests.asmdef" }
    )
    foreach ($rename in $asmdefRenames) {
        $oldPath = Join-Path $ConsumerPackageStagingDir ($rename.Dir + "\" + $rename.Old)
        $newPath = Join-Path $ConsumerPackageStagingDir ($rename.Dir + "\" + $rename.New)
        $asmdefObj = (Get-Content -Path $oldPath -Raw -Encoding UTF8) | ConvertFrom-Json
        $asmdefObj.name = $asmdefObj.name -replace "Game\.Template", "Game.Consumer"
        if ($asmdefObj.PSObject.Properties.Name -contains "references") {
            $asmdefObj.references = @($asmdefObj.references | ForEach-Object { $_ -replace "Game\.Template", "Game.Consumer" })
        }
        Set-Utf8NoBom -Path $newPath -Content ($asmdefObj | ConvertTo-Json -Depth 10)
        Remove-Item -Path $oldPath -Force
        # .meta 也需要同步改名（保留原 GUID，只改文件名部分），否则 Unity 认不出这是同一份资产的
        # 改名而不是"删一个、新增一个"（后者会分配新 GUID，问题不大，但保留原 GUID 更干净）。
        $oldMeta = $oldPath + ".meta"
        if (Test-Path $oldMeta) {
            Move-Item -Path $oldMeta -Destination ($newPath + ".meta") -Force
        }
    }

    Test-Path (Join-Path $ConsumerPackageStagingDir "Runtime\Game.Consumer.asmdef")
}

# -----------------------------------------------------------------------------
# 4) 创建空白 Unity 工程骨架（不用 -createProject——那会新建一份带默认模板包集合的工程，
#    之后还要手工摘掉一堆用不上的默认依赖；改为手写最小 ProjectSettings/Packages/manifest.json
#    骨架，与 games/_template/README.md"在新游戏的 Unity 工程中引用两个包"一节描述的最小前提
#    条件一致——一个"已经存在、只是刚起步"的 Unity 工程）。
# -----------------------------------------------------------------------------
Invoke-Step "创建消费方工程骨架" {
    New-Item -ItemType Directory -Force -Path (Join-Path $ConsumerProjectDir "Assets") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $ConsumerProjectDir "Packages") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $ConsumerProjectDir "ProjectSettings") | Out-Null

    # ProjectSettings：直接复制工作台工程的 ProjectSettings/ + Assets/Settings/（渲染管线/输入
    # 系统等工程级配置不是包依赖能表达的——尤其 ProjectSettings.asset 的 activeInputHandler
    # 必须是 Input System Package，否则 UnityUISurface 挂的 InputSystemUIInputModule 会与旧版
    # StandaloneInputModule 冲突抛异常，见该类型顶部判断记录；URP 的 2D Renderer 资产
    # （Assets/Settings/URP-2D-Pipeline.asset + Renderer2DData.asset）同样是 GraphicsSettings.asset
    # 按 GUID 引用的具体资产文件，必须随工程设置一并带过去）——与 adapters/unity 工作台工程保持
    # 一致的配置基线，避免从 Unity 默认模板起步时这些设置缺失导致的一整类不相关失败。
    $sourceProjectSettings = Join-Path $RepoRoot "adapters\unity\ProjectSettings"
    Copy-TreeMirror -SourceDir $sourceProjectSettings -DestDir (Join-Path $ConsumerProjectDir "ProjectSettings") | Out-Null
    Copy-TreeMirror -SourceDir (Join-Path $RepoRoot "adapters\unity\Assets\Settings") -DestDir (Join-Path $ConsumerProjectDir "Assets\Settings") | Out-Null

    # 判断记录：EditorBuildSettings.asset 是随上面整份 ProjectSettings 复制过来的工作台工程配置，
    # 里面登记的场景路径（GreyBox.unity/Shell.unity 等）在消费方工程里根本不存在——留着只会让
    # Build Settings 列表挂着几条失效引用，替换成一份空列表，改由第 7 步的场景构建器自己登记本
    # 工程真正拥有的两个场景。
    $emptyBuildSettings = @"
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1045 &1
EditorBuildSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Scenes: []
  m_configObjects: {}
"@
    Set-Utf8NoBom -Path (Join-Path $ConsumerProjectDir "ProjectSettings\EditorBuildSettings.asset") -Content $emptyBuildSettings

    $adapterDistPath = (Join-Path $DistRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity") -replace '\\', '/'
    $consumerPackagePath = $ConsumerPackageStagingDir -replace '\\', '/'

    # 判断记录（内置模块清单从工作台 manifest.json 派生，不在本脚本里另抄一份）：Unity 的
    # Audio/ParticleSystem/Tilemap 等"内置模块"必须显式出现在 Packages/manifest.json 的
    # dependencies 里才能被引用到（不是随编辑器自动可用，实测复现：漏了这些条目时
    # UnityAudio.cs/UnityRenderer2D.cs/UnityNavigation2D.cs 里对应类型全部报
    # "could not be found...Enable the built in package"）。工作台工程
    # adapters/unity/Packages/manifest.json 的 dependencies 已经是一份验证过可行的完整集合
    # （含 com.unity.test-framework，-runTests 需要），直接读取复用、只替换
    # "com.gamefoundation.game-template" 这一条为消费方自己的两个 file: 引用，避免本脚本另抄一份
    # 清单、两处以后各自漂移。
    $workbenchManifestPath = Join-Path $RepoRoot "adapters\unity\Packages\manifest.json"
    $workbenchManifest = (Get-Content -Path $workbenchManifestPath -Raw -Encoding UTF8) | ConvertFrom-Json

    $dependencies = [ordered]@{
        "com.gamefoundation.adapter.unity" = "file:$adapterDistPath"
        "com.sample.game-consumer" = "file:$consumerPackagePath"
    }
    # com.gamefoundation.game-template：工作台自己的 file: 相对路径引用，消费方用改名后的
    # com.sample.game-consumer 取代。
    # com.gamefoundation.conformance：契约一致性场景源码，工作台专属测试依赖（见
    # adapters/conformance/README.md 判断记录 3——"仅供 adapters/unity 这个引擎适配层工作台工程
    # 消费"），且是 file: 相对路径引用，原样复制到消费方工程会按消费方工程的物理位置重新解析、
    # 指向一个不存在的路径（实测复现：解析成 "<TEMP 根>\adapters\conformance"）。消费方不需要
    # 这个包，两者都跳过、不带进消费方 manifest。
    $workbenchOnlyPackages = @("com.gamefoundation.game-template", "com.gamefoundation.conformance")
    foreach ($prop in $workbenchManifest.dependencies.PSObject.Properties) {
        if ($workbenchOnlyPackages -contains $prop.Name) {
            continue
        }
        $dependencies[$prop.Name] = $prop.Value
    }

    $manifest = [ordered]@{
        dependencies = $dependencies
        testables = @("com.sample.game-consumer")
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 10
    Set-Utf8NoBom -Path (Join-Path $ConsumerProjectDir "Packages\manifest.json") -Content $manifestJson

    Test-Path (Join-Path $ConsumerProjectDir "Packages\manifest.json")
}

# -----------------------------------------------------------------------------
# 5) 复制框架级数据 + 模板自带数据集到 StreamingAssets（同 build.ps1 内容同步步骤的相同路径约定）
#    + TextMeshPro 运行期资源 + 占位字体（见 games/_template/README.md 新增第 7 步）。
# -----------------------------------------------------------------------------
Invoke-Step "同步内容数据集 + TextMeshPro 运行期资源到消费方工程" {
    $streamingRoot = Join-Path $ConsumerProjectDir "Assets\StreamingAssets\GameFoundation"
    $c1 = Copy-TreeMirror -SourceDir (Join-Path $DistRoot "data\_framework") -DestDir (Join-Path $streamingRoot "data\_framework")
    $c2 = Copy-TreeMirror -SourceDir (Join-Path $ConsumerPackageStagingDir "data\game") -DestDir (Join-Path $streamingRoot "data\game")

    $tmpDest = Join-Path $ConsumerProjectDir "Assets\TextMesh Pro"
    $c3 = Copy-TreeMirror -SourceDir (Join-Path $DistRoot "assets\textmesh_pro_essentials") -DestDir $tmpDest

    $fontsSrc = Join-Path $DistRoot "assets\_placeholder\fonts"
    $fontsDestDir = Join-Path $ConsumerProjectDir "Assets\Framework\Resources\Fonts"
    New-Item -ItemType Directory -Force -Path $fontsDestDir | Out-Null
    $c4 = 0
    if (Test-Path $fontsSrc) {
        Get-ChildItem -Path $fontsSrc -File | Where-Object { $_.Extension -eq ".otf" -or $_.Extension -eq ".ttf" } | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination (Join-Path $fontsDestDir $_.Name) -Force
            $c4++
        }
    }

    # 加固J3 新增：占位场景/导航资源文件（供 Core.Foundation.SceneRouter.SceneRouter.LoadScene
    # 通过 games/_template 的 world.template_field 记录里 scene_ref="scene.template_field"/
    # nav_ref="nav.template_field" 分别以 ResourceKind.Scene/ResourceKind.NavMesh 解析出的
    # UnityResourceLoader 路径 StreamingAssets/GameFoundation/scene/template_field.json、
    # StreamingAssets/GameFoundation/nav_mesh/template_field.json 各自找到一个可读文件；内容不重要
    # （SceneRouter 只要求文件存在且可解码为文本，从不解析其内容），惯例与文件格式完全照抄
    # build.ps1"占位场景/导航资源文件"一节判断记录——但 build.ps1 只把这两个文件直接生成进工作台
    # 自己的 Assets/StreamingAssets/GameFoundation/（构建期产物，不进 dist 快照，见该判断记录"为
    # 什么在 build.ps1 生成而不是放进 data/_sample 或 assets/_placeholder"），consumer_smoke.ps1
    # 搭建的是一个全新的、独立于工作台的消费方工程，不会经过工作台那次 build.ps1 运行，因此这两个
    # 文件必须在本脚本里另外生成一份，否则 -gf-smoke-template 冒烟第一次 "新游戏" 就会卡在
    # SceneRouter.LoadScene 这一步（Page 停在 MainMenu 不再前进，见任务实跑复现：加固J3 把
    # consumer_smoke.ps1 第 10 步从"限时引导自检"改成真正驱动新游戏之前，这个缺口从未被这条演练
    # 流程实际触达过，因此一直没有暴露）。
    $sceneResourceDir = Join-Path $streamingRoot "scene"
    New-Item -ItemType Directory -Force -Path $sceneResourceDir | Out-Null
    $navResourceDir = Join-Path $streamingRoot "nav_mesh"
    New-Item -ItemType Directory -Force -Path $navResourceDir | Out-Null
    $placeholderResourceContent = '{"_placeholder":true,"_note":"SceneRouter 场景/导航资源占位字节，内容不被解析，见 build.ps1/consumer_smoke.ps1 判断记录"}'
    Set-Utf8NoBom -Path (Join-Path $sceneResourceDir "template_field.json") -Content $placeholderResourceContent
    Set-Utf8NoBom -Path (Join-Path $navResourceDir "template_field.json") -Content $placeholderResourceContent
    $c5 = 2

    [PSCustomObject]@{
        Ok = ($c1 -gt 0) -and ($c2 -gt 0) -and ($c3 -gt 0) -and ($c4 -gt 0) -and ($c5 -eq 2)
        Detail = "data/_framework=$c1 files, data/game=$c2 files, TMP essentials=$c3 files, fonts=$c4 files, scene/nav placeholders=$c5 files"
    }
}

# -----------------------------------------------------------------------------
# 6) 首次批处理编译（解析新包依赖 + 首次 Library 导入，正常耗时较长）：0 编译错误。
# -----------------------------------------------------------------------------
Invoke-Step "首次批处理编译（包解析 + 0 编译错误）" {
    $log = Join-Path $UnityLogDir "01_compile.log"
    $proc = Invoke-NativeAndWait -Exe $ResolvedUnityExe -ArgList @(
        "-batchmode", "-nographics", "-quit",
        "-projectPath", $ConsumerProjectDir,
        "-logFile", $log
    ) -TimeoutSeconds 900
    if ($proc.TimedOut) {
        return [PSCustomObject]@{ Ok = $false; Detail = "首次编译超过 900s 未完成（可能是包解析卡住），见 $log" }
    }
    $errorLines = @()
    if (Test-Path $log) {
        $errorLines = Select-String -Path $log -Pattern "error CS" -SimpleMatch:$false -ErrorAction SilentlyContinue
    }
    [PSCustomObject]@{
        Ok = ($proc.ExitCode -eq 0) -and ($errorLines.Count -eq 0)
        Detail = "Unity 退出码 $($proc.ExitCode)，error CS 命中 $($errorLines.Count) 处，见 $log"
    }
}

# -----------------------------------------------------------------------------
# 7) 用模板的场景构建器生成场景（-executeMethod，命名空间未改，仍是 Game.Template.EditorTools）。
# -----------------------------------------------------------------------------
Invoke-Step "用场景构建器生成 Shell + Map 场景" {
    $log = Join-Path $UnityLogDir "02_scene_builder.log"
    $proc = Invoke-NativeAndWait -Exe $ResolvedUnityExe -ArgList @(
        "-batchmode", "-nographics", "-quit",
        "-projectPath", $ConsumerProjectDir,
        "-executeMethod", "Game.Template.EditorTools.GameSceneBuilder.BuildAll",
        "-logFile", $log
    ) -TimeoutSeconds 300
    if ($proc.TimedOut) {
        return [PSCustomObject]@{ Ok = $false; Detail = "场景生成超过 300s 未完成，见 $log" }
    }
    $shellScene = Join-Path $ConsumerProjectDir "Assets\Framework\Scenes\GameTemplateShell.unity"
    $mapScene = Join-Path $ConsumerProjectDir "Assets\Framework\Scenes\GameTemplateMap.unity"
    [PSCustomObject]@{
        Ok = ($proc.ExitCode -eq 0) -and (Test-Path $shellScene) -and (Test-Path $mapScene)
        Detail = "Unity 退出码 $($proc.ExitCode)，Shell 场景存在=$(Test-Path $shellScene)，Map 场景存在=$(Test-Path $mapScene)，见 $log"
    }
}

# -----------------------------------------------------------------------------
# 8) 跑模板的 PlayMode 测试（-testFilter 限定到 Game.Template.Tests 命名空间）：全过。
# -----------------------------------------------------------------------------
Invoke-Step "模板 PlayMode 测试（-testFilter Game.Template.Tests）" {
    $resultsXml = Join-Path $UnityLogDir "03_playmode.xml"
    $log = Join-Path $UnityLogDir "03_playmode.log"
    $proc = Invoke-NativeAndWait -Exe $ResolvedUnityExe -ArgList @(
        "-batchmode",
        "-projectPath", $ConsumerProjectDir,
        "-runTests", "-testPlatform", "PlayMode",
        "-testFilter", "Game.Template.Tests",
        "-testResults", $resultsXml,
        "-logFile", $log
    ) -TimeoutSeconds 600
    if ($proc.TimedOut) {
        return [PSCustomObject]@{ Ok = $false; Detail = "PlayMode 测试超过 600s 未完成，见 $log" }
    }
    if (-not (Test-Path $resultsXml)) {
        return [PSCustomObject]@{ Ok = $false; Detail = "未生成结果 XML：$resultsXml，Unity 退出码 $($proc.ExitCode)，见 $log" }
    }
    [xml]$xml = Get-Content -Path $resultsXml -Raw
    $root = $xml.DocumentElement
    [PSCustomObject]@{
        Ok = ($root.result -eq "Passed")
        Detail = "total=$($root.total) passed=$($root.passed) failed=$($root.failed)"
    }
}

# -----------------------------------------------------------------------------
# 9) 构建独立版。
# -----------------------------------------------------------------------------
$exePath = Join-Path $UnityLogDir "ConsumerShell.exe"
$buildOk = Invoke-Step "构建独立版" {
    $log = Join-Path $UnityLogDir "04_build.log"
    $proc = Invoke-NativeAndWait -Exe $ResolvedUnityExe -ArgList @(
        "-batchmode", "-nographics", "-quit",
        "-projectPath", $ConsumerProjectDir,
        "-buildWindows64Player", $exePath,
        "-logFile", $log
    ) -TimeoutSeconds 600
    if ($proc.TimedOut) {
        return [PSCustomObject]@{ Ok = $false; Detail = "独立版构建超过 600s 未完成，见 $log" }
    }
    [PSCustomObject]@{
        Ok = ($proc.ExitCode -eq 0) -and (Test-Path $exePath)
        Detail = "Unity 构建退出码 $($proc.ExitCode)，产物存在=$(Test-Path $exePath)，见 $log"
    }
}

# -----------------------------------------------------------------------------
# 10) 无人值守冒烟（加固J3：games/_template 新增 Game.Template.TemplateSmokeRunner，
#     "-gf-smoke-template" 命令行标志驱动，日志格式/退出码约定沿用 Adapter.Unity.Shell.SmokeRunner
#     ——见该类型头注释判断记录。命令行标志与工作台的 "-gf-smoke" 不同，理由同样见该类型头注释：
#     Game.Template.asmdef 引用了 Adapter.Unity，独立版产物里两个 SmokeRunner 类型的静态钩子同处
#     一个进程，沿用同一个标志字符串会撞车）。真正驱动一遍"数据零阻断 → 主菜单 → 新游戏 → 进图 →
#     移动 1 秒 → 存档 slot.smoke → 读档 → 退出"，断言日志出现 "RESULT=OK"，不再是限时观察 + 日志
#     关键字排除法的"引导自检"退化版本。
# -----------------------------------------------------------------------------
if ($buildOk) {
    Invoke-Step "-gf-smoke-template 无人值守冒烟" {
        $log = Join-Path $UnityLogDir "05_smoke_template.log"
        $proc = Invoke-NativeAndWait -Exe $exePath -TimeoutSeconds 60 -ArgList @(
            "-batchmode", "-gf-smoke-template",
            "-logFile", $log,
            "-screen-width", "800", "-screen-height", "600"
        )
        if ($proc.TimedOut) {
            return [PSCustomObject]@{ Ok = $false; Detail = "-gf-smoke-template 冒烟超过 60s 未退出，已强制结束（可能挂死），见 $log" }
        }
        if ($proc.ExitCode -ne 0) {
            return [PSCustomObject]@{ Ok = $false; Detail = "独立版退出码 $($proc.ExitCode)，见 $log" }
        }
        if (-not (Test-Path $log)) {
            return [PSCustomObject]@{ Ok = $false; Detail = "未生成冒烟日志：$log" }
        }
        $logText = Get-Content -Path $log -Raw
        [PSCustomObject]@{
            Ok = ($logText -match "\[GF-SMOKE\] RESULT=OK")
            Detail = "见 $log"
        }
    } | Out-Null
} else {
    Write-StepHeader "-gf-smoke-template 无人值守冒烟"
    Write-Host "已跳过：上一步独立版构建未成功，没有可运行的产物。" -ForegroundColor Yellow
    $script:Results.Add([PSCustomObject]@{ Step = "-gf-smoke-template 无人值守冒烟"; Result = "SKIP"; Seconds = 0; Detail = "独立版构建未成功" })
}

# -----------------------------------------------------------------------------
# 汇总
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "==== 消费方演练汇总 ====" -ForegroundColor Cyan
$script:Results | Format-Table -AutoSize Step, Result, Seconds, Detail | Out-String -Width 4096 | Write-Host

$failed = @($script:Results | Where-Object { $_.Result -eq "FAIL" })
if ($failed.Count -gt 0) {
    Write-Host "消费方演练失败：$($failed.Count) 步未通过（共 $($script:Results.Count) 步）。" -ForegroundColor Red
    exit 1
} else {
    Write-Host "消费方演练通过：全部 $($script:Results.Count) 步。" -ForegroundColor Green
    exit 0
}
