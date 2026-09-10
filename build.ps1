<#
.SYNOPSIS
    构建 Core.sln，把六个核心 DLL 同步进 Unity 工作台工程的引擎适配层包，
    并可选地打一份分发包到 dist/<version>/。

.PARAMETER Configuration
    dotnet 构建配置，默认 Release。

.PARAMETER SkipTests
    跳过 dotnet test。

.PARAMETER SyncOnly
    跳过 dotnet build/test，只把 core\*\bin\$Configuration\netstandard2.1\ 下已有的构建产物
    同步进 Runtime/Plugins/Core/（要求这些产物已经存在，即之前至少成功 build 过一次）。
    同步步骤本身（无论是否传 -SyncOnly）一律按文件哈希比较，未变化的 DLL 不重新拷贝，
    避免每次都触发 Unity 重新导入全部六个 DLL。

.PARAMETER SyncContent
    U2-1 新增。内容数据集同步（data/_sample、assets/_placeholder、assets/_sample -> Unity 工程
    Assets/StreamingAssets/GameFoundation/）本身在默认构建流程与 -SyncOnly 下都会无条件执行
    （见下）；本开关单独传且不带 -SyncOnly 时，额外跳过 dotnet build/test 与 DLL 同步两步，
    只做内容同步（"只改了 data/_sample 或 assets/_placeholder、assets/_sample、没有改任何 C# 代码"
    时的快速路径）。内容同步一律按文件哈希比较、只拷变化的文件，并镜像删除源目录里已经不存在、
    但上次同步残留在目标目录里的文件。

.PARAMETER Dist
    版本可追溯任务新增语义（见 11_工程规范与测试.md 第 7 节"版本号必须可追溯到对应的架构文档
    版本与数据 schema 版本组合"；单一版本源见仓库根 VERSION 文件）：
      - 传入具体版本号（如 0.2.0）：以参数为准，并校验格式必须形如 X.Y.Z（三段纯数字，用点号
        分隔），格式非法直接报错退出，不落地任何 dist 产物。
      - 传入字面量 "auto"：不由调用方指定具体版本号，改为读取仓库根 VERSION 文件的内容作为
        本次打包版本（"不传版本时读它"——PowerShell 5.1 的 [string] 类型参数无法在完全不给值的
        情况下识别"传了这个开关但没给值"，因此用 "auto" 这个不会是合法版本号的字面量表达
        "不指定版本、从单一版本源取值"这一语义，同时保留 `-Dist <显式版本号>` 这一行之前就有、
        任务验收命令仍在用的调用方式不变）。
      - 不传（默认空字符串）：跳过打包步骤，行为与此前一致。
    打包时会把最终解析出的版本号写入 dist 内两个 package.json 的 version 字段（含
    games/_template/package.json 对适配层包的依赖版本号），并生成扩展后的 MANIFEST.txt
    （version/date/git_commit/各目录文件数/architecture_docs/data_schemas/core_assemblies/
    headless_assemblies——最后一项 ADR-0018 决策 3 新增，见下）。ADR-0018 决策 3 新增：无头适配层
    （`Adapters.Stub`，对外称"无头适配层"，此前仅测试用不对外发布）额外拷贝进
    dist/<ver>/adapters/headless/Adapters.Stub.dll + README.md。
    私服交付通道新增：额外把四个可发布包（com.gamefoundation.adapter.unity/framework-data/
    toolchain/adapter.headless——第四个包同为 ADR-0018 决策 3 新增）各自组装出正确版本号的
    package.json + 内容到 dist/<ver>/packages/{四个包名}/，并对每个目录跑一遍
    `npm pack --pack-destination` 产出四个 .tgz 到同一目录；无论 -Dist 还是 -Release 都会执行这
    一步（-DryRun 时同样打包，只是不会有后续 -PublishRegistry 发布动作，见
    .PARAMETER PublishRegistry）。详见 toolchain/registry/README.md、落地计划 3.5 节"私服通道"。

.PARAMETER Release
    版本管理方案新增：走一次完整的"发布"流程（校验 -> 更新版本号 -> 全量门禁 -> 提交 -> 打包 ->
    打标签），产出可直接对外发布的版本快照。传入目标版本号（形如 X.Y.Z），流程：
      1. 校验版本号格式，且必须严格大于仓库根 VERSION 文件当前值（语义化版本数值比较，不是字符串
         比较）。
      2. 校验 `git status --porcelain` 为空（工作树干净），否则报错退出——发布快照必须对应一个
         干净的提交状态，不能夹带未提交的改动。
      3. 校验仓库根 CHANGELOG.md 已存在形如 `## [X.Y.Z]` 的条目（不含该条目直接报错退出，提示先
         在 CHANGELOG.md 补齐该版本的变更记录）。
      4. 非 `-DryRun` 时：把该版本号写回仓库根 VERSION 文件、两个 package.json（含
         `games/_template/package.json` 对适配层包的依赖版本号）与
         `adapters/unity/Packages/packages-lock.json`（`com.gamefoundation.game-template` 条目下
         对适配层包依赖版本号的镜像字段，UPM 打开工程时会自行核对/改写这个字段，写回步骤同步覆盖
         避免下一步门禁跑出一份未提交的改动）——这一步是本次发布"成为新的当前版本"的唯一写入点，
         `-DryRun` 时跳过，不触碰任何源码文件。
      5. 跑一遍 `check.ps1`（默认全量，含 Unity 相关步骤与消费方演练；`-ReleaseSkipUnity` 传
         `-SkipUnity` 给 check.ps1，用于没有装 Unity 的机器，但默认要求全量门禁通过才能发布）。
      6. 非 `-DryRun` 时：门禁通过后立即提交 VERSION/两个 package.json/packages-lock.json/
         CHANGELOG.md 的改动（提交信息 `发布 <ver>`），并在 `dist/release-notes-<ver>.txt` 落一份
         CHANGELOG.md 该版本条目正文（供 `gh release create --notes-file` 使用）——先于下一步打包，
         使打包阶段 `git rev-parse HEAD` 就是这次发布提交本身、工作树干净，`dist/ws-game-<ver>.lock`
         与 `MANIFEST.txt` 的 `git_commit` 字段因此指向一个真实存在的发布提交而不是带 `-dirty`
         后缀的占位值（时序判断记录见脚本内该步骤注释）。
      7. 打包 `dist/<ver>/`（复用 `-Dist` 打包逻辑）、`dist/ws-game-<ver>.zip`（`Compress-Archive`，
         zip 内顶层目录为 `ws-game-<ver>/`）与 `dist/ws-game-<ver>.lock`（版本号、git_commit、
         六个核心 DLL 的 sha256，供游戏仓库复制为自己的 `ws-game.lock`）；非 `-DryRun` 时打包完成
         后自检 lock/MANIFEST 的 `git_commit` 必须等于上一步的发布提交且不带 `-dirty` 后缀，不满足
         则报错退出（此时提交已产生但未打标签，按脚本打印的提示 `git reset --soft` 回退后修复重跑）；
         自检通过后打带注释标签 `v<ver>`（标签信息取 CHANGELOG.md 该版本条目正文）。
      8. 打印后续需要人工/设计层执行的两条命令（`git push origin <当前分支> refs/tags/v<ver>`——
         当前分支取自 `git rev-parse --abbrev-ref HEAD`，本步骤全程不切换分支，因此就是打标签
         所在的那个分支；只推本次新建的这一个标签，不带 `--tags` 全量推送，见 P05 根治判断记录，
         与 `gh release create v<ver> ...`）；若版本号的 MAJOR 或 MINOR 段发生了变化（而不仅是
         PATCH 递增），额外打印建议的维护分支创建命令 `git branch release/X.Y.x vX.Y.0`（见仓库根
         README.md"维护分支与 PATCH 发布流程"一节）。

.PARAMETER DryRun
    仅与 `-Release` 同传有效。跑完上面第 1～3、5 步的全部校验，以及第 7 步里"打包"这一半（打包
    目标目录/文件名额外带 `-dryrun` 后缀，如 `dist/1.0.0-dryrun/`、`dist/ws-game-1.0.0-dryrun.zip`，
    避免与真实发布产物混淆或互相覆盖），但跳过第 4、6 步与第 7 步里"自检 + 打标签"那一半——不改写
    VERSION/package.json/packages-lock.json/CHANGELOG.md、不 `git commit`、不做打包完成自检、不
    `git tag`。用于在真正发布前验证整条发布流水线是否能跑通。

.PARAMETER Publish
    仅与 `-Release`（且未传 `-DryRun`）同传有效。第 7 步自检 + 打完标签后，自动依次执行第 8 步
    打印的两条命令（`git push origin <当前分支> refs/tags/v<ver>`、`gh release create ...`），
    不再需要人工另行复制粘贴执行。省略时（默认）只打印这两条命令，不自动执行，由人工/设计层
    确认后自行运行。

.PARAMETER ReleaseSkipUnity
    仅与 `-Release` 同传有效。第 5 步跑 `check.ps1` 时额外传 `-SkipUnity`，跳过 Unity 相关四步与
    消费方演练（没有装 Unity 或 Unity 被占用的机器上用）。省略时（默认）要求 `check.ps1` 全量通过
    才能发布——"发布"这个动作本身就意味着要对外承诺质量，默认不放宽。

.PARAMETER Zip
    独立于 `-Release` 使用：与 `-Dist`/`-Dist auto` 同传时，额外打一份 `dist/ws-game-<ver>.zip`
    与 `dist/ws-game-<ver>.lock`（与 `-Release` 第 7 步同一份打包逻辑），但不做 `-Release`
    的版本号校验、写回、`check.ps1` 门禁、提交、打标签——只是"把已经存在的 dist/<ver>/ 目录再打成
    zip+lock 两个可上传附件"这一件事。用途：`.github/workflows/release.yml` 在 CI 里对一个已经由
    本机 `-Release`（未传 `-Publish`）提交并打好标签的版本重新打包上传附件，这种场景不需要也不
    应该重新走版本号写回/提交/打标签（那些已经在本机完成）。`-Release` 本身已经隐含这份打包
    （不需要再显式传 `-Zip`）。

.PARAMETER PublishRegistry
    私服交付通道新增。仅与 `-Release`（且未传 `-DryRun`）同传有效，独立于 `-Publish` 单独控制
    （`-Publish` 只管 `git push`/`gh release create` 这两条命令，与是否发注册表无关；两个开关可以
    任意组合同传或都不传）。第 7 步自检 + 打标签完成后，对 `dist/<ver>/packages/` 下四个包目录
    （ADR-0018 决策 3 起，第四个包 com.gamefoundation.adapter.headless 一并纳入）依次执行
    `npm publish --registry <url> --userconfig toolchain/registry/.npmrc`（该 `.npmrc`
    由 `toolchain/registry/init_publisher.ps1` 无人值守生成，见该脚本头注释）。目标版本号一旦
    发布成功即不可覆盖——`npm publish` 对已存在的版本号本身就会失败，与 `-Release` 的"发布不可变"
    语义天然一致，不需要额外加校验。要求 `toolchain/registry/.npmrc` 已存在（先跑一遍
    `init_publisher.ps1`），否则直接报错退出，不会跑到一半失败。

.PARAMETER RegistryUrl
    配合 `-PublishRegistry` 使用，显式指定私服地址；省略时读取 `toolchain/registry/registry.json`
    的 `url` 字段（默认 `http://127.0.0.1:4873`）。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$SyncOnly,
    [switch]$SyncContent,
    [string]$Dist = "",
    [string]$Release = "",
    [switch]$DryRun,
    [switch]$Publish,
    [switch]$ReleaseSkipUnity,
    [switch]$Zip,
    [switch]$PublishRegistry,
    [string]$RegistryUrl = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$SolutionPath = Join-Path $RepoRoot "Core.sln"
$PluginsCoreDir = Join-Path $RepoRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core"
$VersionFilePath = Join-Path $RepoRoot "VERSION"
$VersionFormatPattern = '^\d+\.\d+\.\d+$'

# 判断记录（第九轮审计工具链条目，本机 Get-FileHash 在部分 Windows PowerShell 5.1 环境下不可用，
# 原因未查明）：全部哈希计算改用 toolchain/_hash.ps1 提供的 Get-Sha256FileHash 共享函数，
# Get-FileHash 可用时优先用、不可用时透明退化到不依赖该 cmdlet 的 .NET SHA256 兜底实现。
. (Join-Path $RepoRoot "toolchain\_hash.ps1")

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

# 版本可追溯任务新增：单一版本源读取 + -Dist 参数解析（见上方 .PARAMETER Dist 说明）。
# 校验在脚本一开始就做（哪怕本次调用根本不带 -Dist），提前暴露 VERSION 文件本身格式错误。
function Get-FrameworkVersionFromFile {
    if (-not (Test-Path $VersionFilePath)) {
        throw "找不到版本文件：$VersionFilePath（单一版本源，见 11_工程规范与测试.md 第 7 节）"
    }
    $v = (Get-Content -Path $VersionFilePath -Raw).Trim()
    if ($v -notmatch $VersionFormatPattern) {
        throw "$VersionFilePath 内容格式非法：'$v'（需形如 X.Y.Z）"
    }
    return $v
}

$DistRequested = ($Dist -ne "")
$ResolvedDistVersion = ""
$DistDirVersion = ""
# 私服交付通道新增：允许 -Dist 直接传 "X.Y.Z-dryrun" 这一种形式（不经过完整 -Release -DryRun
# 流程，那个流程强制要求 git 工作树干净，不适合"仓库里还有其它并行改动、只想单独验证打包/npm
# pack/npm publish 这一段逻辑"这种场景）。"-dryrun" 后缀本身是合法的语义化版本预发布标识
# （semver 允许 "X.Y.Z-<prerelease>"），因此这里不像 -Release -DryRun 内部那样剥离后缀
# 另算一个"干净版本号"——$ResolvedDistVersion 就是这个带后缀的完整字符串，原样写进四个包的
# package.json version 字段、MANIFEST.txt 等，npm publish 出去的也就是这个明显带"这是一次
# dryrun 验证、不是真实发布"标记的版本号，天然不会与任何真实版本号的发布产物混淆或互相覆盖，
# 也不需要额外的目录名后缀区分（$DistDirVersion 与 $ResolvedDistVersion 相同）。
$DistVersionDryRunPattern = '^\d+\.\d+\.\d+-dryrun$'
if ($DistRequested) {
    if ($Dist -eq "auto") {
        $ResolvedDistVersion = Get-FrameworkVersionFromFile
        Write-Host "-Dist auto：从 $VersionFilePath 读取版本号 -> $ResolvedDistVersion" -ForegroundColor Cyan
    } elseif ($Dist -match $DistVersionDryRunPattern) {
        $ResolvedDistVersion = $Dist
        Write-Host "-Dist $Dist：'-dryrun' 后缀形式（合法 semver 预发布标识），打包内容与目录名均使用这个完整字符串" -ForegroundColor Cyan
    } else {
        if ($Dist -notmatch $VersionFormatPattern) {
            Write-Host "-Dist 版本号格式非法：'$Dist'（需形如 X.Y.Z，或 X.Y.Z-dryrun，或传 'auto' 从 VERSION 文件读取）" -ForegroundColor Red
            exit 1
        }
        $ResolvedDistVersion = $Dist
    }
    $DistDirVersion = $ResolvedDistVersion
}

# -----------------------------------------------------------------------------
# 版本管理方案新增：-Release 发布流程（见上方 .PARAMETER Release/DryRun/Publish/ReleaseSkipUnity
# 说明）。本节只做第 1～3 步的前置校验并把 -Release 转译成等价的 -Dist 请求（复用下方既有的打包
# 逻辑，只是打包目录名在 -DryRun 时额外带 -dryrun 后缀，见 $DistDirVersion）；第 4 步（写回源码
# 版本号）紧跟本节之后单独一节；check.ps1 门禁、zip/lock 产物、提交与打标签在打包完成之后（脚本
# 末尾）另起一节，因为它们依赖打包已经产出的 dist/<dir>/ 目录与其中的 DLL 哈希。
# -----------------------------------------------------------------------------
$ReleaseRequested = ($Release -ne "")

if ((-not $ReleaseRequested) -and ($DryRun -or $Publish -or $ReleaseSkipUnity -or $PublishRegistry)) {
    Write-Host "-DryRun/-Publish/-ReleaseSkipUnity/-PublishRegistry 仅在同传 -Release 时有效" -ForegroundColor Red
    exit 1
}
if ($DryRun -and $Publish) {
    Write-Host "-DryRun 与 -Publish 不能同传（-DryRun 语义上不产生任何可发布的提交/标签）" -ForegroundColor Red
    exit 1
}
if ($DryRun -and $PublishRegistry) {
    Write-Host "-DryRun 下不会真正发布到注册表（只 npm pack 不 publish），-PublishRegistry 无意义，请去掉其中一个" -ForegroundColor Red
    exit 1
}
if ($Zip -and (-not $DistRequested) -and (-not $ReleaseRequested)) {
    Write-Host "-Zip 需要同传 -Dist/-Dist auto（或 -Release，其本身已隐含 -Zip 的效果）——没有 dist/<ver>/ 目录可打包" -ForegroundColor Red
    exit 1
}

# 用于判断"MAJOR/MINOR 是否变化"（决定是否建议开维护分支）与语义化版本数值比较（不能用字符串比较，
# 例如 "10.0.0" 按字符串比较会小于 "9.0.0"）。
function ConvertTo-SemVerParts {
    param([string]$Version)
    $segments = $Version -split '\.'
    return [PSCustomObject]@{
        Major = [int]$segments[0]
        Minor = [int]$segments[1]
        Patch = [int]$segments[2]
    }
}

# 返回 -1/0/1，语义同 [string]::Compare 但按数值逐段比较。
function Compare-SemVer {
    param([string]$VersionA, [string]$VersionB)
    $a = ConvertTo-SemVerParts -Version $VersionA
    $b = ConvertTo-SemVerParts -Version $VersionB
    if ($a.Major -ne $b.Major) { if ($a.Major -gt $b.Major) { return 1 } else { return -1 } }
    if ($a.Minor -ne $b.Minor) { if ($a.Minor -gt $b.Minor) { return 1 } else { return -1 } }
    if ($a.Patch -ne $b.Patch) { if ($a.Patch -gt $b.Patch) { return 1 } else { return -1 } }
    return 0
}

$ReleaseBumpIsMajorOrMinor = $false
$ReleaseChangelogSection = ""
$ReleaseCurrentVersion = ""

if ($ReleaseRequested) {
    Write-Step "-Release $Release：发布流程前置校验"

    # 第 1 步：版本号格式 + 严格大于当前 VERSION。
    if ($Release -notmatch $VersionFormatPattern) {
        Write-Host "-Release 版本号格式非法：'$Release'（需形如 X.Y.Z）" -ForegroundColor Red
        exit 1
    }
    $ReleaseCurrentVersion = Get-FrameworkVersionFromFile
    $cmp = Compare-SemVer -VersionA $Release -VersionB $ReleaseCurrentVersion
    if ($cmp -le 0) {
        Write-Host "目标版本 $Release 必须严格大于当前版本 $ReleaseCurrentVersion（语义化版本数值比较）" -ForegroundColor Red
        exit 1
    }
    $oldParts = ConvertTo-SemVerParts -Version $ReleaseCurrentVersion
    $newParts = ConvertTo-SemVerParts -Version $Release
    if (($newParts.Major -ne $oldParts.Major) -or ($newParts.Minor -ne $oldParts.Minor)) {
        $ReleaseBumpIsMajorOrMinor = $true
    }
    Write-Host "  版本号校验通过：$ReleaseCurrentVersion -> $Release"

    # 第 2 步：工作树必须干净（发布快照不能夹带未提交的改动）。DryRun 同样校验——DryRun 的目的是
    # 验证"整条发布流水线打完收工时工作树会是什么状态"，跳过这一步校验会让 DryRun 失去意义。
    Push-Location $RepoRoot
    try {
        $releaseGitStatus = & git status --porcelain
        $releaseGitDirty = $false
        if ($null -ne $releaseGitStatus) {
            $releaseGitStatusJoined = ($releaseGitStatus -join "`n").Trim()
            if ($releaseGitStatusJoined -ne "") { $releaseGitDirty = $true }
        }
    } finally {
        Pop-Location
    }
    if ($releaseGitDirty) {
        Write-Host "工作树不干净（git status --porcelain 非空），发布前请先提交或清理改动" -ForegroundColor Red
        exit 1
    }
    Write-Host "  git 工作树干净：校验通过"

    # 第 3 步：CHANGELOG.md 必须已有该版本的条目（形如 "## [X.Y.Z]"，允许行尾附日期等其余文本）。
    $changelogPath = Join-Path $RepoRoot "CHANGELOG.md"
    if (-not (Test-Path $changelogPath)) {
        Write-Host "找不到 $changelogPath，无法核对该版本的变更记录" -ForegroundColor Red
        exit 1
    }
    $changelogLines = Get-Content -Path $changelogPath -Encoding UTF8
    $headingPattern = '^##\s*\[' + [regex]::Escape($Release) + '\]'
    $headingIndex = -1
    for ($i = 0; $i -lt $changelogLines.Count; $i++) {
        if ($changelogLines[$i] -match $headingPattern) { $headingIndex = $i; break }
    }
    if ($headingIndex -lt 0) {
        Write-Host "CHANGELOG.md 缺少 '## [$Release]' 条目，请先在 CHANGELOG.md 中补充该版本的变更记录再发布" -ForegroundColor Red
        exit 1
    }
    $sectionEndIndex = $changelogLines.Count
    for ($i = $headingIndex + 1; $i -lt $changelogLines.Count; $i++) {
        if ($changelogLines[$i] -match '^##\s*\[') { $sectionEndIndex = $i; break }
    }
    $ReleaseChangelogSection = ($changelogLines[$headingIndex..($sectionEndIndex - 1)] -join "`n").Trim()
    Write-Host "  CHANGELOG.md 已找到 [$Release] 条目：校验通过"

    # -Dist 与 -Release 二选一：-Release 内部转译为一次 -Dist 请求，复用下方既有打包逻辑；
    # 若调用方同时显式传了 -Dist，以 -Release 为准（更明确的意图），并提示一句。
    if ($DistRequested -and ($ResolvedDistVersion -ne $Release)) {
        Write-Host "  同时传了 -Dist $Dist 与 -Release $Release，以 -Release 的版本号为准" -ForegroundColor Yellow
    }
    $DistRequested = $true
    $ResolvedDistVersion = $Release
    if ($DryRun) {
        $DistDirVersion = $Release + "-dryrun"
        Write-Host "  -DryRun：打包目录/产物名额外带 -dryrun 后缀（$DistDirVersion），不写回任何源码文件、不提交、不打标签"
    } else {
        $DistDirVersion = $Release
    }

    # 第 4 步：写回源码版本号（VERSION + 两个 package.json，含模板对适配层包的依赖版本号）。
    # DryRun 时跳过——这是本次发布"成为新的当前版本"的唯一写入点。
    if (-not $DryRun) {
        Write-Step "写回版本号 $Release -> VERSION、两个 package.json"

        # 判断记录：VERSION 文件是不带 BOM、不带尾随换行的纯 ASCII 文本（既有约定，见仓库根
        # VERSION 文件实际内容）；两个 package.json 是不带 BOM 的 UTF-8（含中文 description 字段）。
        # PowerShell 5.1 的 `Set-Content -Encoding utf8` 固定带 BOM，因此这里改用
        # [System.IO.File]::WriteAllText + 显式 UTF8Encoding($false) 避免污染已提交的源码文件
        # （与本文件下方 Set-DistPackageJsonVersion 只作用于 dist/ 构建产物、允许带 BOM 不同——
        # 那些是不入库的构建产物，这里是要提交进仓库的源码文件）。
        [System.IO.File]::WriteAllText($VersionFilePath, $Release, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  已写回 $VersionFilePath -> $Release"

        function Set-SourcePackageJsonVersion {
            param([string]$JsonPath, [string]$Version)
            if (-not (Test-Path $JsonPath)) {
                throw "找不到 $JsonPath，无法回写版本号"
            }
            $obj = (Get-Content -Path $JsonPath -Raw -Encoding UTF8) | ConvertFrom-Json
            $obj.version = $Version
            if (($obj.PSObject.Properties.Name -contains "dependencies") -and
                ($obj.dependencies.PSObject.Properties.Name -contains "com.gamefoundation.adapter.unity")) {
                $obj.dependencies."com.gamefoundation.adapter.unity" = $Version
            }
            $jsonText = ($obj | ConvertTo-Json -Depth 10)
            [System.IO.File]::WriteAllText($JsonPath, $jsonText, (New-Object System.Text.UTF8Encoding($false)))
            Write-Host "  已写回 $JsonPath -> $Version"
        }

        Set-SourcePackageJsonVersion -JsonPath (Join-Path $RepoRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\package.json") -Version $Release
        Set-SourcePackageJsonVersion -JsonPath (Join-Path $RepoRoot "games\_template\package.json") -Version $Release

        # 写回遗漏根治（2026-09-07）：adapters/unity/Packages/packages-lock.json 里
        # "com.gamefoundation.game-template" 条目下 dependencies."com.gamefoundation.adapter.unity"
        # 是 games/_template/package.json 同名依赖版本号的镜像（Unity Package Manager 读本地文件
        # 依赖时自动写入的锁定值），此前 -Release 写回没有覆盖它——门禁第 5 步跑 check.ps1 里的
        # Unity 相关步骤时，UPM 会自己把这个字段改成新版本号，导致发布提交完成后工作树仍然
        # 不干净（该改动没能进入发布提交，1.0.0 首次发布实测复现，见 CHANGELOG.md [1.0.0] 修复
        # 记录）。这里在写回两个 package.json 之后同步写回这个字段，使门禁跑完时 UPM 发现文件已经
        # 是它自己会写的值、不需要再改，工作树保持干净。
        function Set-PackagesLockGameTemplateDependency {
            param([string]$JsonPath, [string]$Version)
            if (-not (Test-Path $JsonPath)) {
                throw "找不到 $JsonPath，无法回写版本号"
            }
            # 判断记录：packages-lock.json 是 UPM 自动生成/维护的大文件（行尾见根 .gitattributes
            # 对应例外条目判断记录：Unity/UPM 实测写出 LF、无 BOM、2 空格缩进，键顺序由 UPM 决定），
            # 整体 ConvertFrom-Json/ConvertTo-Json 往返会打乱这些格式
            # （PowerShell 5.1 的 ConvertTo-Json 缩进/换行符与 UPM 原始输出不一致），导致下次 UPM
            # 打开工程时产生一大片与本次改动无关的格式 diff。改用最小化正则文本替换，只动
            # com.gamefoundation.game-template 依赖块下这一个字段的值，文件其余内容与换行风格
            # 原样保留（与两个 package.json 用完整 JSON 往返的写法不同，是保守写法，同一判断
            # 也适用于 check.ps1 的版本一致性只读校验——那边同样不整体解析成对象比较）。
            $raw = [System.IO.File]::ReadAllText($JsonPath)
            # 全文件唯一一处 `"com.gamefoundation.adapter.unity": "<版本号>"`（键名 + 字符串值这一
            # 形态；该包自己的顶层条目是 `"com.gamefoundation.adapter.unity": {`，对象值，不会被
            # 这个正则误命中，已用 Grep 核实全文件只有一处字符串值形态的命中）。
            $pattern = '("com\.gamefoundation\.adapter\.unity":\s*")\d+\.\d+\.\d+(")'
            $hitCount = [regex]::Matches($raw, $pattern).Count
            if ($hitCount -ne 1) {
                throw "$JsonPath 中 'com.gamefoundation.adapter.unity' 依赖字段命中 $hitCount 处（预期 1 处），格式可能已变化，拒绝盲目替换"
            }
            $newRaw = [regex]::Replace($raw, $pattern, ('${1}' + $Version + '${2}'))
            [System.IO.File]::WriteAllText($JsonPath, $newRaw, (New-Object System.Text.UTF8Encoding($false)))
            Write-Host "  已写回 $JsonPath -> com.gamefoundation.game-template.dependencies.com.gamefoundation.adapter.unity=$Version"
        }

        Set-PackagesLockGameTemplateDependency -JsonPath (Join-Path $RepoRoot "adapters\unity\Packages\packages-lock.json") -Version $Release
    }

    # 第 5 步：全量门禁（-ReleaseSkipUnity 时传 -SkipUnity 给 check.ps1）。DryRun 同样跑——
    # DryRun 的目的正是验证"发布流水线全流程能否走通"，门禁本身不写文件，天然安全。
    #
    # 判断记录（固定传 -AbiStrict，外部审计 audit-76d16a5-20260910 PJ114-02 根治）：发布机在跑到
    # 这一步之前，第 4 步已经确保历史版本 dist/ 产物齐备（`-Release` 打包本身就要求能追溯到基线
    # 版本，见 toolchain/abi_probe_baseline.txt 手动推进说明），"ABI 探针基线缺失"在发布链路上不是
    # 正常状态，必须让 check.ps1 的 ABI 步骤在这种情况下判 FAIL 而不是安静 SKIP——不传本开关会让
    # 一次因为环境问题（例如发布机 dist/ 被误清空）而完全没跑起来的 ABI 探针被门禁静默放行。
    Write-Step "check.ps1 门禁（-Release 第 5 步）"
    $checkScript = Join-Path $RepoRoot "check.ps1"
    $checkArgs = @("-AbiStrict")
    if ($ReleaseSkipUnity) { $checkArgs += "-SkipUnity" }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $checkScript @checkArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "check.ps1 未通过（退出码 $LASTEXITCODE），发布流程终止" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host "  check.ps1 通过"

    # 第 6 步（时序缺陷根治，2026-09-07）：门禁通过后立即提交版本号改动，先于下面的打包步骤。
    # DryRun 时跳过——这与第 4 步写回是同一个"不碰源码"的边界。
    #
    # 判断记录（为什么提交要先于打包，而不是像此前那样打完包再提交）：dist/ws-game-<ver>.lock 与
    # MANIFEST.txt 里的 git_commit 字段是给游戏仓库锁定"这份产物对应仓库的哪个提交"用的（见
    # toolchain/get_framework.ps1、games/_template/README.md 接入说明），必须指向一个真实存在、
    # 可 `git checkout` 的发布提交本身。此前打包发生在提交之前，打包时工作树还带着尚未提交的版本号
    # 写回改动，`git rev-parse --short HEAD` 拿到的是发布提交的上一个提交、还要再拼 "-dirty" 后缀，
    # 是一个既不指向发布提交、也不指向任何干净提交的占位值——1.0.0 首次发布（2026-09-07）实测踩中，
    # 发布后才发现 lock/MANIFEST 记录的 git_commit 与实际发布提交对不上，见 CHANGELOG.md [1.0.0]
    # 修复记录。改成提交先行后，打包阶段（第 7 步）的 `git rev-parse HEAD` 就是这次发布提交本身。
    #
    # 判断记录（为什么打标签仍然留在打包之后，不跟着提交一起挪到这里）：标签是"这个提交对应一个
    # 完整、验证过的发布产物"的公开承诺；如果提交完成后打包才失败（例如本机没装 node 导致
    # `npm pack` 失败、zip 压缩中途出错），这时不应该已经存在一个指向"产物不完整"的提交的标签——
    # 保留提交、不打标签，让操作者能看清"提交已产生但发布未完成"这一中间状态，按下面打印的提示
    # 用 `git reset --soft` 回退再重跑，而不是留下一个名不副实的标签还需要额外 `git tag -d` 清理。
    if (-not $DryRun) {
        Write-Step "-Release 第 6 步：门禁通过，提交版本号改动（先于打包）"

        Push-Location $RepoRoot
        try {
            $ReleaseParentCommitHash = (& git rev-parse HEAD).Trim()
        } finally {
            Pop-Location
        }

        New-Item -ItemType Directory -Force -Path (Join-Path $RepoRoot "dist") | Out-Null
        $releaseNotesPath = Join-Path $RepoRoot ("dist\release-notes-" + $Release + ".txt")
        [System.IO.File]::WriteAllText($releaseNotesPath, $ReleaseChangelogSection, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  已生成 $releaseNotesPath（CHANGELOG.md [$Release] 条目正文，供 gh release create --notes-file 使用）"

        Push-Location $RepoRoot
        try {
            & git add "VERSION" "adapters/unity/Packages/com.gamefoundation.adapter.unity/package.json" "games/_template/package.json" "adapters/unity/Packages/packages-lock.json" "CHANGELOG.md"
            if ($LASTEXITCODE -ne 0) { throw "git add 失败，退出码 $LASTEXITCODE" }

            $commitMessage = "发布 $Release"
            & git commit -m $commitMessage
            if ($LASTEXITCODE -ne 0) { throw "git commit 失败，退出码 $LASTEXITCODE" }

            $ReleaseCommitHash = (& git rev-parse HEAD).Trim()
            $ReleaseCommitShort = (& git rev-parse --short HEAD).Trim()
            Write-Host "  已提交：$commitMessage（$ReleaseCommitHash）"
        } finally {
            Pop-Location
        }

        Write-Host "  提示：若接下来的打包步骤失败，提交 $ReleaseCommitHash 已产生但未打标签；请先修复失败原因，再执行 'git reset --soft $ReleaseParentCommitHash' 回退这次半途的发布提交后重新运行 -Release。" -ForegroundColor Yellow
    }
}

# 六个需要发布给 Unity 端的核心 DLL；不拷贝 Adapters.Stub、不拷贝任何测试或 xunit 相关程序集。
$CoreAssemblies = @(
    @{ Name = "Core.Foundation"; Dir = "core\foundation" },
    @{ Name = "Core.Numbers"; Dir = "core\numbers" },
    @{ Name = "Core.Rules"; Dir = "core\rules" },
    @{ Name = "Core.Carriers"; Dir = "core\carriers" },
    @{ Name = "Core.Gameplay"; Dir = "core\gameplay" },
    @{ Name = "Presentation.Common"; Dir = "presentation" }
)

# 只有目标文件不存在或哈希不同才真正拷贝；返回 $true 表示发生了拷贝，$false 表示跳过。
# 用哈希而不是时间戳/文件大小比较，避免"内容相同但时间戳不同"（例如同一份产物被重复构建）
# 触发不必要的拷贝，从而不必要地让 Unity 重新导入插件 DLL（编辑器重新加载程序集很慢）。
function Copy-IfChanged {
    param(
        [string]$SourcePath,
        [string]$DestPath
    )

    if (Test-Path $DestPath) {
        $srcHash = Get-Sha256FileHash -Path $SourcePath
        $dstHash = Get-Sha256FileHash -Path $DestPath
        if ($srcHash -eq $dstHash) {
            return $false
        }
    }

    $destDir = Split-Path -Parent $DestPath
    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    }

    Copy-Item -Path $SourcePath -Destination $DestPath -Force
    return $true
}

# U2-1 判断记录："只做内容同步"的快速路径是 -SyncContent 单独传（不带 -SyncOnly）；
# 若两者同传，-SyncOnly 的语义（跳过 build/test、仍同步 DLL）优先，内容同步照常无条件执行。
$ContentOnlyMode = $SyncContent -and (-not $SyncOnly)

if ($ContentOnlyMode) {
    Write-Step "已启用 -SyncContent（未同时传 -SyncOnly）：只做内容同步，跳过 dotnet build/test 与 DLL 同步"
} elseif ($SyncOnly) {
    Write-Step "已启用 -SyncOnly：跳过 dotnet build/test"
} else {
    # ---------------------------------------------------------------------------
    # 1. dotnet build
    # ---------------------------------------------------------------------------
    Write-Step "dotnet build Core.sln -c $Configuration"
    & dotnet build $SolutionPath -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Host "dotnet build 失败，退出码 $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    # ---------------------------------------------------------------------------
    # 2. dotnet test（可跳过）
    # ---------------------------------------------------------------------------
    if (-not $SkipTests) {
        Write-Step "dotnet test Core.sln -c $Configuration --no-build"
        & dotnet test $SolutionPath -c $Configuration --no-build
        if ($LASTEXITCODE -ne 0) {
            Write-Host "dotnet test 失败，退出码 $LASTEXITCODE" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    } else {
        Write-Step "已跳过 dotnet test（-SkipTests）"
    }
}

# ---------------------------------------------------------------------------
# 3. 同步六个核心 DLL 到 Unity 引擎适配层包的 Runtime/Plugins/Core/（哈希不同才拷贝）
#    -SyncContent 单独传（ContentOnlyMode）时跳过本步——那条路径明确"只改了内容数据/资产，
#    没有改任何 C# 代码"，DLL 本身没有变化。
# ---------------------------------------------------------------------------
if (-not $ContentOnlyMode) {
    Write-Step "同步核心 DLL 到 Runtime/Plugins/Core/（哈希不同才拷贝）"

    if (-not (Test-Path $PluginsCoreDir)) {
        New-Item -ItemType Directory -Force -Path $PluginsCoreDir | Out-Null
    }

    $syncedCount = 0
    $skippedCount = 0

    foreach ($asm in $CoreAssemblies) {
        $srcPath = Join-Path $RepoRoot ($asm.Dir + "\bin\$Configuration\netstandard2.1\" + $asm.Name + ".dll")
        if (-not (Test-Path $srcPath)) {
            Write-Host "找不到构建产物：$srcPath" -ForegroundColor Red
            if ($SyncOnly) {
                Write-Host "（-SyncOnly 要求产物已存在，请先不带 -SyncOnly 跑一次完整构建）" -ForegroundColor Red
            }
            exit 1
        }

        $destPath = Join-Path $PluginsCoreDir ($asm.Name + ".dll")
        $changed = Copy-IfChanged -SourcePath $srcPath -DestPath $destPath
        $sizeBytes = (Get-Item $destPath).Length

        if ($changed) {
            $syncedCount++
            Write-Host ("  [同步] {0}.dll  ({1} bytes)" -f $asm.Name, $sizeBytes)
        } else {
            $skippedCount++
            Write-Host ("  [跳过] {0}.dll  ({1} bytes，内容未变化）" -f $asm.Name, $sizeBytes)
        }
    }

    Write-Host ("已同步 {0} 个 / 跳过 {1} 个" -f $syncedCount, $skippedCount)

    $copiedDllCount = (Get-ChildItem -Path $PluginsCoreDir -Filter "*.dll" -File).Count
    if ($copiedDllCount -ne 6) {
        Write-Host "Runtime/Plugins/Core/ 下 DLL 数量应为 6，实际为 $copiedDllCount" -ForegroundColor Red
        exit 1
    }
    Write-Host "Runtime/Plugins/Core/ 下 DLL 数量核对通过：$copiedDllCount"
} else {
    Write-Step "ContentOnlyMode：跳过 DLL 同步"
}

# ---------------------------------------------------------------------------
# 4. 内容数据集同步（U2-1 新增，见 -SyncContent 参数说明；数据目录框架/游戏分层任务追加
#    data/_framework 同步）：
#      data/_framework          -> Assets/StreamingAssets/GameFoundation/data/_framework
#        （框架级数据表，见 data/README.md"两类目录"一节；与 data/_sample 各自独立子目录，
#        不合并成一份文件树——Unity 侧引导代码按两个数据根分别加载，见 EngineAdapter 判断记录）
#      data/_sample            -> Assets/StreamingAssets/GameFoundation/data/_sample
#      assets/_placeholder     -> Assets/StreamingAssets/GameFoundation/assets/_placeholder（整体镜像）
#      assets/_placeholder/sprites + assets/_sample/sprites -> Assets/StreamingAssets/GameFoundation/sprites
#        （UnityResourceLoader 期望的 Image 路径规则，见该类型顶部注释；两个数据集的精灵集同步进
#        同一棵目标目录树，_sample 侧目录名已按 "<category>_<name>" 规则与 _placeholder 侧的占位
#        精灵集不同名，正常不会互相覆盖）
#      assets/_placeholder/sfx + assets/_sample/sfx -> Assets/StreamingAssets/GameFoundation/audio
#        （UnityResourceLoader 期望的 Audio 路径规则；源目录名 "sfx" 与目标目录名 "audio" 不同，
#        是加载器一侧的固定子目录约定，见该类型判断记录）
#      assets/_placeholder/vfx + assets/_sample/vfx -> Assets/StreamingAssets/GameFoundation/vfx
#    以上 sprites/audio/vfx 三处传两个源目录（Sync-ContentTree 的 $SourceDirs 数组，见其函数
#    注释），任一源目录不存在仍照旧跳过，不影响另一个正常同步。
#    无条件执行（默认构建流程、-SyncOnly、-SyncContent 三种模式下都会执行，见参数说明）。
# ---------------------------------------------------------------------------
Write-Step "同步内容数据集到 StreamingAssets/GameFoundation/（哈希不同才拷贝，镜像删除源目录已不存在的文件）"

$StreamingAssetsRoot = Join-Path $RepoRoot "adapters\unity\Assets\StreamingAssets\GameFoundation"

# 按（一个或多个）源目录 -> 目标目录镜像同步；返回 拷贝/跳过/删除 计数，一律用绝对路径
# （不含尾部分隔符）参与 Substring 计算相对路径，避免路径分隔符/结尾斜杠的边界情况算错相对路径。
# $SourceDirs 支持传多个源目录（数组）：keepRelative（决定目标目录里保留哪些相对路径、
# 删除哪些残留文件）取全部源目录相对路径的并集；多个源目录出现同名相对路径时，按数组顺序
# 后列源目录覆盖前者（内容以后列为准）并打印警告——调用方按"优先级从低到高"的顺序传入。
# 单个源目录不存在时照旧跳过它（不影响其余源目录正常同步）；全部源目录都不存在则整体跳过。
function Sync-ContentTree {
    param(
        [string[]]$SourceDirs,
        [string]$DestDir
    )

    $resolvedSources = @()
    foreach ($dir in $SourceDirs) {
        if (Test-Path $dir) {
            $resolvedSources += (Resolve-Path $dir).Path.TrimEnd('\', '/')
        } else {
            Write-Host "  源目录不存在，跳过同步：$dir" -ForegroundColor Yellow
        }
    }
    if ($resolvedSources.Count -eq 0) {
        return @{ Copied = 0; Skipped = 0; Removed = 0; Total = 0 }
    }

    if (-not (Test-Path $DestDir)) {
        New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
    }
    $resolvedDest = (Resolve-Path $DestDir).Path.TrimEnd('\', '/')

    $copied = 0
    $skipped = 0
    $keepRelative = New-Object System.Collections.Generic.HashSet[string]

    foreach ($resolvedSource in $resolvedSources) {
        $sourceFiles = Get-ChildItem -Path $resolvedSource -Recurse -File
        foreach ($file in $sourceFiles) {
            $relative = $file.FullName.Substring($resolvedSource.Length).TrimStart('\', '/')
            if ($keepRelative.Contains($relative)) {
                Write-Host ("  警告：多个源目录都提供了相对路径 '{0}'，以后列源目录为准（当前来自 '{1}'）" -f $relative, $resolvedSource) -ForegroundColor Yellow
            }
            [void]$keepRelative.Add($relative)
            $destPath = Join-Path $resolvedDest $relative
            $changed = Copy-IfChanged -SourcePath $file.FullName -DestPath $destPath
            if ($changed) { $copied++ } else { $skipped++ }
        }
    }

    $removed = 0
    if (Test-Path $resolvedDest) {
        $destFiles = Get-ChildItem -Path $resolvedDest -Recurse -File
        foreach ($destFile in $destFiles) {
            $relative = $destFile.FullName.Substring($resolvedDest.Length).TrimStart('\', '/')
            if (-not $keepRelative.Contains($relative)) {
                Remove-Item -Path $destFile.FullName -Force
                $removed++
            }
        }
    }

    return @{ Copied = $copied; Skipped = $skipped; Removed = $removed; Total = $keepRelative.Count }
}

$dataFrameworkSyncResult = Sync-ContentTree -SourceDirs @((Join-Path $RepoRoot "data\_framework")) -DestDir (Join-Path $StreamingAssetsRoot "data\_framework")
Write-Host ("  data/_framework -> StreamingAssets/GameFoundation/data/_framework：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $dataFrameworkSyncResult.Total, $dataFrameworkSyncResult.Copied, $dataFrameworkSyncResult.Skipped, $dataFrameworkSyncResult.Removed)

$dataSyncResult = Sync-ContentTree -SourceDirs @((Join-Path $RepoRoot "data\_sample")) -DestDir (Join-Path $StreamingAssetsRoot "data\_sample")
Write-Host ("  data/_sample -> StreamingAssets/GameFoundation/data/_sample：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $dataSyncResult.Total, $dataSyncResult.Copied, $dataSyncResult.Skipped, $dataSyncResult.Removed)

# games/_template 自带的最小数据集（见 games/_template/data/README.md）同步进工作台 StreamingAssets，
# 供 games/_template/Runtime/GameBootstrap.cs 的 PlayMode 测试（在工作台里跑，见
# games/_template/Tests/Runtime）默认 GameOptions（_gameDatasetRoot = "data/game"）能找到数据；
# 与 data/_framework、data/_sample 同一治理方式（构建期产物、gitignore，不进源码库）。
$templateDataSyncResult = Sync-ContentTree -SourceDirs @((Join-Path $RepoRoot "games\_template\data\game")) -DestDir (Join-Path $StreamingAssetsRoot "data\game")
Write-Host ("  games/_template/data/game -> StreamingAssets/GameFoundation/data/game（模板自带最小数据集，供模板 PlayMode 测试使用）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $templateDataSyncResult.Total, $templateDataSyncResult.Copied, $templateDataSyncResult.Skipped, $templateDataSyncResult.Removed)

$placeholderMirrorResult = Sync-ContentTree -SourceDirs @((Join-Path $RepoRoot "assets\_placeholder")) -DestDir (Join-Path $StreamingAssetsRoot "assets\_placeholder")
Write-Host ("  assets/_placeholder -> StreamingAssets/GameFoundation/assets/_placeholder（整体镜像）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $placeholderMirrorResult.Total, $placeholderMirrorResult.Copied, $placeholderMirrorResult.Skipped, $placeholderMirrorResult.Removed)

# sprites/audio/vfx 三处同时同步 assets/_placeholder/<x>（占位素材）与 assets/_sample/<x>
# （toolchain/import_sample_assets.py 导入的样例资产）到同一棵目标目录树，见上方 4 节头注释。
# 判断记录（P07 根治，2026-09-07，审计 architecture/落地计划/audit-7e63d66-20260907/
# project-review.md P07）：source -> target 子目录名映射（sprites->sprites、sfx->audio、
# vfx->vfx）此前在本文件与 toolchain/sync_package_content.ps1（私服交付通道，同步进消费方 Unity
# 工程）各自维护一份，后者完全漏掉了这一步（只整体镜像 assets/_placeholder 本身，消费方按原始
# 子目录名找不到 UnityResourceLoader 实际按目标子目录名查找的资源）。改为两处都从
# toolchain/resource_layout_map.json 读取同一张表，不再各自硬编码，见该文件判断记录。
$resourceLayoutMapPath = Join-Path $RepoRoot "toolchain\resource_layout_map.json"
if (-not (Test-Path $resourceLayoutMapPath)) {
    Write-Host "找不到 $resourceLayoutMapPath（sprites/audio/vfx 目标目录映射表，P07 根治新增，见 sync_package_content.ps1 同一份判断记录）" -ForegroundColor Red
    exit 1
}
$resourceLayoutMap = (Get-Content -Path $resourceLayoutMapPath -Raw -Encoding UTF8) | ConvertFrom-Json
foreach ($mapping in $resourceLayoutMap.mappings) {
    $sourceSubdir = $mapping.source
    $targetSubdir = $mapping.target
    $mappingSyncResult = Sync-ContentTree -SourceDirs @((Join-Path $RepoRoot ("assets\_placeholder\" + $sourceSubdir)), (Join-Path $RepoRoot ("assets\_sample\" + $sourceSubdir))) -DestDir (Join-Path $StreamingAssetsRoot $targetSubdir)
    Write-Host ("  assets/_placeholder/{0} + assets/_sample/{0} -> StreamingAssets/GameFoundation/{1}（加载器路径规则，见 toolchain/resource_layout_map.json）：共 {2} 个文件，拷贝 {3}，跳过 {4}，删除 {5}" -f $sourceSubdir, $targetSubdir, $mappingSyncResult.Total, $mappingSyncResult.Copied, $mappingSyncResult.Skipped, $mappingSyncResult.Removed)
}

$totalContentFiles = (Get-ChildItem -Path $StreamingAssetsRoot -Recurse -File -ErrorAction SilentlyContinue).Count
Write-Host ("StreamingAssets/GameFoundation/ 下文件总数（含以上五棵树的并集，sprites/audio/vfx 与 assets/_placeholder 下同名文件各自独立计数）：{0}" -f $totalContentFiles)

# ---------------------------------------------------------------------------
# 4.05 字体资源同步（缺口 1 新增）：assets/_placeholder/fonts/*.otf|*.ttf 哈希比对同步到
#      adapters/unity/Assets/Framework/Resources/Fonts/（注意目标不是 StreamingAssets——字体
#      资源不走 UnityResourceLoader 的通用"后台读字节"路径，必须是已被 Unity 资产管线导入过的
#      UnityEngine.Font 对象，见该类型顶部"判断记录（Font 资源种类）"）；供 UnityResourceLoader/
#      UnityUISurface 按 "font.<name>" id 规则（去掉 "font." 前缀、点号换下划线）以
#      "Resources.Load<Font>(\"Fonts/<name>\")" 解析，规则见包 README"资源 id → 路径规则"。
#      只同步 .otf/.ttf 两个扩展名本身（不含该目录下 README.md/LICENSE-OFL.txt 等说明文件——
#      Assets/Framework/Resources/ 是 Unity 会整体扫描导入的目录，混入非字体文件会被当成多余
#      资产一并导入，不属于本步骤职责）；已提交的同名文件哈希一致则不拷贝（避免不必要的 Unity
#      重新导入）；源目录里已删除的字体文件会被镜像删除（连同其 .meta，否则下次放回同名文件会
#      被 Unity 复用旧 .meta 里过期的导入设置）。
# ---------------------------------------------------------------------------
$fontsSourceDir = Join-Path $RepoRoot "assets\_placeholder\fonts"
$fontsDestDir = Join-Path $RepoRoot "adapters\unity\Assets\Framework\Resources\Fonts"
$fontCopied = 0
$fontSkipped = 0
$fontRemoved = 0
$fontTotal = 0

if (Test-Path $fontsSourceDir) {
    if (-not (Test-Path $fontsDestDir)) {
        New-Item -ItemType Directory -Force -Path $fontsDestDir | Out-Null
    }

    $fontSourceFiles = Get-ChildItem -Path $fontsSourceDir -Recurse -File |
        Where-Object { $_.Extension -eq ".otf" -or $_.Extension -eq ".ttf" }
    $fontKeepNames = New-Object System.Collections.Generic.HashSet[string]
    foreach ($fontFile in $fontSourceFiles) {
        $fontTotal++
        [void]$fontKeepNames.Add($fontFile.Name)
        $destPath = Join-Path $fontsDestDir $fontFile.Name
        $changed = Copy-IfChanged -SourcePath $fontFile.FullName -DestPath $destPath
        if ($changed) { $fontCopied++ } else { $fontSkipped++ }
    }

    if (Test-Path $fontsDestDir) {
        $existingFontFiles = Get-ChildItem -Path $fontsDestDir -File |
            Where-Object { $_.Extension -eq ".otf" -or $_.Extension -eq ".ttf" }
        foreach ($existing in $existingFontFiles) {
            if (-not $fontKeepNames.Contains($existing.Name)) {
                Remove-Item -Path $existing.FullName -Force
                $fontRemoved++
                $metaPath = $existing.FullName + ".meta"
                if (Test-Path $metaPath) {
                    Remove-Item -Path $metaPath -Force
                }
            }
        }
    }
} else {
    Write-Host "  源目录不存在，跳过字体同步：$fontsSourceDir" -ForegroundColor Yellow
}

Write-Host ("  assets/_placeholder/fonts/*.otf|*.ttf -> Assets/Framework/Resources/Fonts（字体资源 id -> 路径规则）：共 {0} 个文件，拷贝 {1}，跳过 {2}，删除 {3}" -f $fontTotal, $fontCopied, $fontSkipped, $fontRemoved)

# ---------------------------------------------------------------------------
# 4.1 占位场景/导航资源文件（供 Core.Foundation.SceneRouter.SceneRouter.LoadScene 通过 world.map
#     记录里既有的 scene_ref="scene.sample_field"/nav_ref="nav.sample_field" 分别以
#     ResourceKind.Scene/ResourceKind.NavMesh 解析出的 UnityResourceLoader 路径
#     StreamingAssets/GameFoundation/scene/sample_field.json、
#     StreamingAssets/GameFoundation/nav_mesh/sample_field.json 各自找到一个可读文件（ADR-0016
#     决策 5 给 ResourceKind 增补了 Scene/NavMesh 专用取值，两类资源不再共用同一个 "data/"
#     路径——此前两者去掉类别前缀后恰好是同一个文件名，借用同一份字节，是 Scene/NavMesh 取值
#     补齐之前的过渡写法，现按各自子目录分别生成，内容仍然不重要：SceneRouter 只要求文件存在
#     且可解码为文本，从不解析其内容，见 SceneRouter.cs 类型注释）。
#
#     判断记录（为什么在 build.ps1 生成而不是放进 data/_sample 或 assets/_placeholder）：
#     data/_sample 的可改动范围限定于"UI/Shell 需要补的示例行"（登记表数据），这个文件不是任何
#     登记表的一行，塞进去会被 toolchain/validate_data.py 当成一张缺失 schema 的表校验报错；
#     assets/_placeholder 是本任务硬性规则明确不动的目录。该资源在 SceneRouter 眼里是纯粹的
#     "占位字节"（内容完全不解析），属于构建期产物而非源内容，因此选择在 build.ps1（内容同步
#     步骤，允许改动）里直接生成，与 StreamingAssets/GameFoundation/ 其余产物同一治理方式
#     （构建时产生、.gitignore、不进源码库）。框架层面的"场景/导航资源内容管线"仍是已知能力
#     缺口（ResourceKind 已经区分种类，但具体场景/导航数据格式与生产管线不在本次范围内）。
# ---------------------------------------------------------------------------
$sceneResourceDir = Join-Path $StreamingAssetsRoot "scene"
if (-not (Test-Path $sceneResourceDir)) {
    New-Item -ItemType Directory -Force -Path $sceneResourceDir | Out-Null
}
$sceneResourcePath = Join-Path $sceneResourceDir "sample_field.json"
$sceneResourceContent = '{"_placeholder":true,"_note":"SceneRouter 场景资源占位字节，内容不被解析，见 build.ps1 判断记录"}'
Set-Content -Path $sceneResourcePath -Value $sceneResourceContent -NoNewline -Encoding utf8
Write-Host "  已生成占位场景资源：$sceneResourcePath（scene.sample_field）"

$navResourceDir = Join-Path $StreamingAssetsRoot "nav_mesh"
if (-not (Test-Path $navResourceDir)) {
    New-Item -ItemType Directory -Force -Path $navResourceDir | Out-Null
}
$navResourcePath = Join-Path $navResourceDir "sample_field.json"
$navResourceContent = '{"_placeholder":true,"_note":"SceneRouter 导航资源占位字节，内容不被解析，见 build.ps1 判断记录"}'
Set-Content -Path $navResourcePath -Value $navResourceContent -NoNewline -Encoding utf8
Write-Host "  已生成占位导航资源：$navResourcePath（nav.sample_field）"

# 同上，为 games/_template 自带的最小地图（world.template_field，scene_ref=scene.template_field/
# nav_ref=nav.template_field，见 games/_template/data/game/world/world.map.json）补一份同款占位
# 场景/导航资源，供模板 PlayMode 测试里的 SceneRouter.LoadScene 找到可读文件（否则新游戏会在
# LoadScene 这一步失败，见 presentation/shell/core/ShellHost.cs NewGame 判断记录）。
$templateSceneResourcePath = Join-Path $sceneResourceDir "template_field.json"
Set-Content -Path $templateSceneResourcePath -Value $sceneResourceContent -NoNewline -Encoding utf8
Write-Host "  已生成占位场景资源：$templateSceneResourcePath（scene.template_field，供 games/_template 测试用）"

$templateNavResourcePath = Join-Path $navResourceDir "template_field.json"
Set-Content -Path $templateNavResourcePath -Value $navResourceContent -NoNewline -Encoding utf8
Write-Host "  已生成占位导航资源：$templateNavResourcePath（nav.template_field，供 games/_template 测试用）"

# ---------------------------------------------------------------------------
# 5. 可选：打分发包 dist/<version>/
# ---------------------------------------------------------------------------
if ($DistRequested) {
    Write-Step "打分发包 dist/$DistDirVersion/"

    $DistRoot = Join-Path $RepoRoot ("dist\" + $DistDirVersion)
    if (Test-Path $DistRoot) {
        Remove-Item -Path $DistRoot -Recurse -Force -Confirm:$false
    }
    New-Item -ItemType Directory -Force -Path $DistRoot | Out-Null

    function Copy-DistDir {
        param(
            [string]$SourceRelative,
            [string]$DestName,
            [string[]]$ExcludeDirNames = @()
        )
        $src = Join-Path $RepoRoot $SourceRelative
        $dst = Join-Path $DistRoot $DestName
        New-Item -ItemType Directory -Force -Path $dst | Out-Null

        Get-ChildItem -Path $src -Recurse -Force | ForEach-Object {
            $item = $_
            $relative = $item.FullName.Substring($src.Length).TrimStart('\')
            if ($relative -eq "") { return }

            # 判断记录（2026-09-05 收尾修复）：排除判断此前只匹配"排除目录名出现在相对路径最前面"
            # 这一种情况（如 ".venv" 在 "toolchain/.venv/..."），漏掉了排除目录名出现在更深层级的
            # 情况（如 "__pycache__" 出现在 "toolchain/asset_import/__pycache__/..." ——
            # asset_import 是普通业务目录，其下的 __pycache__ 子目录也需要被排除）。改为按路径分段
            # 逐段比较（$relative 以 "\" 分隔），只要任意一段的名字精确等于某个排除名即命中，不再
            # 依赖"是否出现在开头"这一前提，覆盖"排除目录出现在任意深度"的情况。
            $skip = $false
            $relativeSegments = $relative -split '\\'
            foreach ($ex in $ExcludeDirNames) {
                if ($relativeSegments -contains $ex) {
                    $skip = $true
                    break
                }
            }
            if ($skip) { return }

            $targetPath = Join-Path $dst $relative
            if ($item.PSIsContainer) {
                New-Item -ItemType Directory -Force -Path $targetPath | Out-Null
            } else {
                $targetDir = Split-Path -Parent $targetPath
                if (-not (Test-Path $targetDir)) {
                    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
                }
                Copy-Item -Path $item.FullName -Destination $targetPath -Force
            }
        }

        $fileCount = (Get-ChildItem -Path $dst -Recurse -File).Count
        Write-Host ("  {0} -> dist\{1}\{2}  ({3} files)" -f $SourceRelative, $DistDirVersion, $DestName, $fileCount)
        return $fileCount
    }

    $adapterFileCount = Copy-DistDir -SourceRelative "adapters\unity\Packages\com.gamefoundation.adapter.unity" -DestName "adapters\unity\Packages\com.gamefoundation.adapter.unity"
    $templateFileCount = Copy-DistDir -SourceRelative "games\_template" -DestName "games\_template"
    # 私服交付通道新增：额外排除 "registry"（toolchain/registry/ 自身——私服运行时基础设施，不
    # 随游戏侧分发，见该目录 README.md）、"node_modules"（registry 子目录下 npm ci 安装产物，
    # 双重保险）、"bin"/"obj"（toolchain/validator/ 的 .NET 构建产物，不预编译随包分发，见
    # com.gamefoundation.toolchain 包 README.md"依赖安装"一节）。
    $toolchainFileCount = Copy-DistDir -SourceRelative "toolchain" -DestName "toolchain" -ExcludeDirNames @(".venv", "__pycache__", "registry", "node_modules", "bin", "obj")
    $assetsFileCount = Copy-DistDir -SourceRelative "assets\_placeholder" -DestName "assets\_placeholder"
    # 数据目录框架/游戏分层任务新增：分发包只带框架级数据表（data/_framework），不带
    # data/_sample（那是本仓库自测用的示例数据，不代表任何真实游戏内容，见 data/README.md）。
    # 新游戏按 games/_template/README.md 的接入方式是"分发包 data/_framework + 自己的 data/<game>"
    # 两根合并加载/校验（见 data/README.md"多根加载与合并规则"）。
    $dataFrameworkFileCount = Copy-DistDir -SourceRelative "data\_framework" -DestName "data\_framework"

    # 消费方演练任务新增（toolchain/consumer_smoke.ps1 实跑暴露的 dist 缺口，见该脚本判断记录）：
    # TextMeshPro 是 games/_template 主菜单 UI（TemplateShellUi 经 UnityUISurface.DrawText）的运行期
    # 硬依赖，但 "TMP Essential Resources"（TMP_Settings.asset、SDF 着色器等）从未随 dist 分发——
    # 工作台工程（adapters/unity）把这些内容当作普通 Assets 提交在
    # adapters/unity/Assets/TextMesh Pro/（见 UnityUISurface.cs 顶部"判断记录（TMP 运行期依赖）"：
    # AssetDatabase.ImportPackage 在批处理下是异步的、无法可靠等待完成，因此不能指望新消费方工程
    # 自己在批处理流程里"导入 TMP Essential Resources"），此前 dist 只打包了
    # adapters/unity/Packages/com.gamefoundation.adapter.unity（Packages 目录），不包含
    # adapters/unity/Assets/ 下任何内容，导致任何新游戏工程照 games/_template/README.md 走完接入
    # 步骤后，主菜单一渲染文字就会因为 TMP_Settings 单例不存在而抛异常——首次跑
    # consumer_smoke.ps1 实测复现。现补一份 assets/textmesh_pro_essentials/，新消费方工程按
    # games/_template/README.md 的指引整份拷进自己的 Assets/TextMesh Pro/。
    $tmpEssentialsFileCount = Copy-DistDir -SourceRelative "adapters\unity\Assets\TextMesh Pro" -DestName "assets\textmesh_pro_essentials"

    # -------------------------------------------------------------------
    # 5.055 PJ130-02 根治新增（审计 architecture/落地计划/audit-5c444f1-20260908/AUDIT_REPORT.md
    #      PJ130-02）：ADR-0017 W6-B 的 model 型外形占位资产（Assets/Resources/GameFoundation/
    #      {models,anim_clips}，胶囊体 + AnimatorController + 四条 AnimationClip，见
    #      Editor/GeneratePlaceholderModelAssets.cs 顶部判断记录）此前从未随 dist 分发——上面
    #      Copy-DistDir 只拷 adapters/unity/Packages/com.gamefoundation.adapter.unity（UPM 包目录
    #      本身），这批资产连同其生成器都提交在工作台工程的 adapters/unity/Assets/ 下（Packages
    #      目录之外），与 TMP Essentials 是同一种"提交在 Assets/、从未随包分发"的缺口（同上一节
    #      判断记录），独立消费方只有发行包时无法拿到这套占位 model/动画，也无法自行重新生成。
    #
    #      判断记录（选择"塞进 adapter 包的 Runtime/Resources/"而不是"framework-data 包的
    #      Data~/"）：UnityResourceLoader 的约定路径固定为 Resources.Load 可解析的
    #      "Resources/GameFoundation/models(或 anim_clips)/<name>"（见该类型判断记录），这批资产
    #      本身是已被 Unity 资产管线导入过的原生序列化文件（.prefab/.controller/.anim，带
    #      .meta 里的 GUID），不是"任意字节数组"，不能像 data/_framework、assets/_placeholder 那样
    #      放进 framework-data 包的 Data~/（Unity 不扫描 ~ 后缀目录，消费方需要手工整份拷进自己的
    #      Assets/ 才能被导入——那是给"非 Unity 原生格式"的通用兜底方案，见 framework-data 包
    #      README）。Unity 会自动扫描并导入任意包（含通过 UPM 依赖引入的包）里名字精确为
    #      "Resources"（不带 ~ 后缀）的目录，因此把这批资产原样放进
    #      com.gamefoundation.adapter.unity 包的 Runtime/Resources/GameFoundation/ 下，任何消费方
    #      工程只要依赖了这个包（games/_template/README.md 接入步骤本来就要求这一步），Unity 打开
    #      工程时就会自动导入这批资产，Resources.Load 立即可用——不需要额外的"整份拷进自己
    #      Assets/"手工步骤，比 framework-data/Data~ 或 TMP Essentials 那种手工拷贝更贴合"消费方
    #      能直接用占位模型"这条验收标准。这里只写入 dist 内的包副本
    #      （$DistRoot\adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Resources\
    #      ...），不改动 adapters/unity 源码树本身（源码树里这批资产仍在
    #      Assets/Resources/GameFoundation/ 供工作台工程自身的 EditMode/PlayMode 测试使用，两者是
    #      两份独立文件，源目录不受影响）；下面 5.15 节组装 com.gamefoundation.adapter.unity 包时
    #      整份拷贝这个 dist 内的包目录，因此这批资产也会自动进入对应 .tgz。
    #
    #      Editor/GeneratePlaceholderModelAssets.cs 同理拷进 dist 内包目录的 Editor/（该目录已有
    #      Adapter.Unity.Editor.asmdef，引用 Adapter.Unity 且不限制平台内容——脚本用到的
    #      UnityEditor/UnityEditor.Animations/UnityEngine 均为内置模块，Core.Foundation.Common 由
    #      Runtime/Plugins/Core/ 下的预编译 DLL 按 Unity 的精简引用平台设置自动提供，不需要在
    #      asmdef 的 references 里显式列出），消费方装了这个包后可以在自己工程里直接用 Unity
    #      菜单/脚本重新生成这批占位资产（例如自定义规格调整后）。同一份内容因此既随 dist zip
    #      分发（下面 MANIFEST/lock 覆盖的 adapters/unity/Packages/... 路径本来就含这两处新增），
    #      也随 com.gamefoundation.adapter.unity-<ver>.tgz 分发，不需要单独再打一份。
    # -------------------------------------------------------------------
    Write-Step "补齐 dist\$DistDirVersion\...\com.gamefoundation.adapter.unity\Runtime\Resources\GameFoundation\{models,anim_clips} 与 Editor 生成器（PJ130-02 根治）"
    $distAdapterPkgDir = Join-Path $DistRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity"
    $srcModelResourcesDir = Join-Path $RepoRoot "adapters\unity\Assets\Resources\GameFoundation"
    $dstModelResourcesDir = Join-Path $distAdapterPkgDir "Runtime\Resources\GameFoundation"
    if (-not (Test-Path $srcModelResourcesDir)) {
        Write-Host "打分发包失败：找不到 $srcModelResourcesDir（model 占位资产源目录缺失）" -ForegroundColor Red
        exit 1
    }
    New-Item -ItemType Directory -Force -Path $dstModelResourcesDir | Out-Null
    Copy-Item -Path (Join-Path $srcModelResourcesDir "models") -Destination (Join-Path $dstModelResourcesDir "models") -Recurse -Force
    Copy-Item -Path (Join-Path $srcModelResourcesDir "anim_clips") -Destination (Join-Path $dstModelResourcesDir "anim_clips") -Recurse -Force

    $srcModelGeneratorPath = Join-Path $RepoRoot "adapters\unity\Assets\Editor\GeneratePlaceholderModelAssets.cs"
    $srcModelGeneratorMetaPath = $srcModelGeneratorPath + ".meta"
    if ((-not (Test-Path $srcModelGeneratorPath)) -or (-not (Test-Path $srcModelGeneratorMetaPath))) {
        Write-Host "打分发包失败：找不到 $srcModelGeneratorPath 或其 .meta（model 占位资产生成器缺失）" -ForegroundColor Red
        exit 1
    }
    $dstAdapterEditorDir = Join-Path $distAdapterPkgDir "Editor"
    New-Item -ItemType Directory -Force -Path $dstAdapterEditorDir | Out-Null
    Copy-Item -Path $srcModelGeneratorPath -Destination (Join-Path $dstAdapterEditorDir "GeneratePlaceholderModelAssets.cs") -Force
    Copy-Item -Path $srcModelGeneratorMetaPath -Destination (Join-Path $dstAdapterEditorDir "GeneratePlaceholderModelAssets.cs.meta") -Force

    # 上面两次 Copy-Item 发生在 5. 节最前面 Copy-DistDir 调用之后，需要重新统计一次
    # $adapterFileCount（MANIFEST.txt 的 directory_file_counts 才能反映新增文件）。
    $adapterFileCount = (Get-ChildItem -Path $distAdapterPkgDir -Recurse -File).Count
    Write-Host ("  已补齐 model/anim_clips 占位资产 + 生成器 -> dist\{0}\adapters\unity\Packages\com.gamefoundation.adapter.unity\（当前共 {1} files）" -f $DistDirVersion, $adapterFileCount)

    # -------------------------------------------------------------------
    # 5.05 P02 根治新增（审计 architecture/落地计划/audit-7e63d66-20260907/project-review.md
    #      P02）：toolchain/validator/Validator.csproj 在"源码树不存在"（本 dist ZIP、下方 5.15
    #      组装出的 com.gamefoundation.toolchain UPM 包 Tools~/validator/）场景下改用
    #      <Reference HintPath="lib\*.dll"> 直接引用编译好的六个核心 DLL（见该 csproj 判断记录），
    #      不再要求随包分发 presentation/、core/ 源码。这里把六个 DLL 额外拷贝一份到
    #      dist\<ver>\toolchain\validator\lib\，源头是上面 Copy-DistDir 已经拷进 dist 的适配层包
    #      Runtime\Plugins\Core\（与源码仓库里 $PluginsCoreDir 同步的那一份内容一致，见"3. 同步
    #      六个核心 DLL"步骤），不是重新构建，只是同一份文件再放一份到这个新位置——保证 ZIP 内
    #      toolchain/validator 与下方 UPM 包内 Tools~/validator 都能独立于 adapters/unity 目录
    #      找到自己需要的 DLL（UPM 的 com.gamefoundation.toolchain 是与 com.gamefoundation.
    #      adapter.unity 完全分开发布的独立包，不能假设消费者两个包都装了）。
    # -------------------------------------------------------------------
    Write-Step "补齐 dist\$DistDirVersion\toolchain\validator\lib\（P02 根治：独立包内 Validator 自包含所需的核心 DLL）"
    $distValidatorLibDir = Join-Path $DistRoot "toolchain\validator\lib"
    New-Item -ItemType Directory -Force -Path $distValidatorLibDir | Out-Null
    $distAdapterPluginsCoreDir = Join-Path $DistRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core"
    foreach ($asm in $CoreAssemblies) {
        $srcDllForValidatorLib = Join-Path $distAdapterPluginsCoreDir ($asm.Name + ".dll")
        if (-not (Test-Path $srcDllForValidatorLib)) {
            Write-Host "打分发包失败：找不到 $srcDllForValidatorLib（无法为 toolchain/validator/lib 补齐核心 DLL）" -ForegroundColor Red
            exit 1
        }
        Copy-Item -Path $srcDllForValidatorLib -Destination (Join-Path $distValidatorLibDir ($asm.Name + ".dll")) -Force
    }
    Write-Host ("  已补齐 {0} 个核心 DLL -> dist\{1}\toolchain\validator\lib\" -f $CoreAssemblies.Count, $DistDirVersion)

    # -------------------------------------------------------------------
    # 5.056 ADR-0018 决策 3 新增（无头适配层交付）：Adapters.Stub（对外称"无头适配层"，桩清单/
    #      判断记录见 adapters/stub/README.md）由此前"仅测试用、不对外发布"转正为框架交付物，
    #      拷贝进 dist\<ver>\adapters\headless\Adapters.Stub.dll，并附一份从源码仓库
    #      adapters\headless\README.md 派生的精简说明（是什么、依赖哪个核心 DLL、怎么
    #      new StubEngine()）。源 DLL 取自 adapters\stub\bin\$Configuration\netstandard2.1\
    #      （Adapters.Stub 是 Core.sln 的一个直接项目，见该 csproj；-SyncOnly 场景下与六个核心
    #      DLL 同一前提——要求之前至少完整构建过一次，找不到时给出同款报错并退出，不静默跳过）。
    # -------------------------------------------------------------------
    Write-Step "补齐 dist\$DistDirVersion\adapters\headless\Adapters.Stub.dll（ADR-0018 决策 3：无头适配层交付）"
    $srcHeadlessDllPath = Join-Path $RepoRoot ("adapters\stub\bin\$Configuration\netstandard2.1\Adapters.Stub.dll")
    if (-not (Test-Path $srcHeadlessDllPath)) {
        Write-Host "打分发包失败：找不到 $srcHeadlessDllPath（-SyncOnly 要求产物已存在，请先不带 -SyncOnly 跑一次完整构建）" -ForegroundColor Red
        exit 1
    }
    $distHeadlessDir = Join-Path $DistRoot "adapters\headless"
    New-Item -ItemType Directory -Force -Path $distHeadlessDir | Out-Null
    Copy-Item -Path $srcHeadlessDllPath -Destination (Join-Path $distHeadlessDir "Adapters.Stub.dll") -Force
    $srcHeadlessReadmePath = Join-Path $RepoRoot "adapters\headless\README.md"
    if (-not (Test-Path $srcHeadlessReadmePath)) {
        Write-Host "打分发包失败：找不到 $srcHeadlessReadmePath（无头适配层说明文档源文件缺失）" -ForegroundColor Red
        exit 1
    }
    Copy-Item -Path $srcHeadlessReadmePath -Destination (Join-Path $distHeadlessDir "README.md") -Force
    $headlessAssemblyShaMap = [ordered]@{
        "Adapters.Stub.dll" = (Get-Sha256FileHash -Path (Join-Path $distHeadlessDir "Adapters.Stub.dll"))
    }
    Write-Host ("  已补齐 -> dist\{0}\adapters\headless\Adapters.Stub.dll + README.md（sha256={1}）" -f $DistDirVersion, $headlessAssemblyShaMap["Adapters.Stub.dll"])

    # -------------------------------------------------------------------
    # 5.057 消费方反馈 E1 根治（architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E1）：
    #      toolchain/validate_data.py 第二道校验此前一律用 `dotnet run --project toolchain/validator`
    #      现场编译——解压产物落在消费方仓库内时，MSBuild 按项目目录向上找 Directory.Build.props
    #      会继承到消费方自己的设置（如 TreatWarningsAsErrors=true），把本工具 XML 文档注释里原本
    #      无害的告警（如未指定重载的 cref 歧义 CS0419）提升为编译错误，消费方连校验都跑不起来。
    #      根治两层：
    #      a) 随包携带预编译产物：Core.sln 第 1 步 `dotnet build` 已经把 Validator 项目连同它引用的
    #         六个核心 DLL 一并构建到 toolchain\validator\bin\$Configuration\<tfm>\ 下（.NET SDK
    #         对 Exe 项目的标准输出布局：Validator.dll/.deps.json/.runtimeconfig.json + 全部
    #         ProjectReference 输出的拷贝），原样整份拷进 dist 的 toolchain\validator\bin\——消费方
    #         校验时优先 `dotnet <Validator.dll 路径>` 直接执行已编译好的程序集，完全不触发 MSBuild/
    #         Directory.Build.props 解析，从根上绕开这一整类"消费方构建设置污染"问题（见
    #         validate_data.py 对应改动）。
    #      b) 防御性兜底：找不到预编译产物时 validate_data.py 仍会退回 `dotnet run --project`
    #         现场编译（例如源码仓库内自测、或消费方精简掉了 bin\ 目录），因此仍随 dist 在
    #         toolchain\validator\ 下内置一份空 `Directory.Build.props`（`<Project></Project>`）
    #         ——MSBuild 找 Directory.Build.props 只取"向上遇到的第一份"，不会继续再往上找，这份
    #         空文件就此彻底挡住消费方仓库根的 Directory.Build.props 被隐式继承，无论其内容是什么。
    #         判断记录：不在源码仓库树里常驻同名文件——源码仓库根 Directory.Build.props 本身就是
    #         Validator 项目（Core.sln 的一部分）依赖的正常设置来源（LangVersion/Nullable/
    #         TreatWarningsAsErrors/GenerateDocumentationFile），常驻一份空文件会截断这份继承，
    #         削弱仓库内 `TreatWarningsAsErrors=true` 这条门禁（任务书"不放宽断言"），因此只在打包
    #         这一步为 dist/UPM 产物生成，不进源码树、不提交。
    #      c) cref 歧义本身也已在源头修（toolchain/validator/Program.cs 与全仓其它 CS0419 类歧义
    #         cref，见提交记录），(a)/(b) 是即便未来又出现类似告警也不会再复现的结构性根治。
    #      lock 文件新增 `validator_dlls`（Validator.dll 的 sha256，与 `dlls`/`headless_dlls` 同一
    #      模式），见下面"5.6 zip + lock"节；get_framework.ps1 存在该字段时一并校验（向后兼容，
    #      见其判断记录）。四个私服包里的 com.gamefoundation.toolchain 包内容取自这里已经补齐的
    #      dist\<ver>\toolchain\（下方 5.15 节 `Copy-Item ... Tools~` 整份拷贝），因此本节必须排在
    #      5.15 之前，不需要为私服通道单独重复一遍同样的逻辑。
    # -------------------------------------------------------------------
    Write-Step "补齐 dist\$DistDirVersion\toolchain\validator\bin\ + 空 Directory.Build.props（消费方反馈 E1 根治：预编译 validator，杜绝消费方构建设置污染）"
    $srcValidatorBinParent = Join-Path $RepoRoot "toolchain\validator\bin\$Configuration"
    if (-not (Test-Path $srcValidatorBinParent)) {
        Write-Host "打分发包失败：找不到 $srcValidatorBinParent（-SyncOnly 要求 Validator 项目已完整构建过一次，请先不带 -SyncOnly 跑一次完整构建）" -ForegroundColor Red
        exit 1
    }
    $srcValidatorTfmDirs = @(Get-ChildItem -Path $srcValidatorBinParent -Directory)
    if ($srcValidatorTfmDirs.Count -ne 1) {
        Write-Host ("打分发包失败：$srcValidatorBinParent 下应恰好有 1 个目标框架目录，实际 {0} 个" -f $srcValidatorTfmDirs.Count) -ForegroundColor Red
        exit 1
    }
    $srcValidatorTfmDir = $srcValidatorTfmDirs[0].FullName
    $srcValidatorDllPath = Join-Path $srcValidatorTfmDir "Validator.dll"
    if (-not (Test-Path $srcValidatorDllPath)) {
        Write-Host "打分发包失败：找不到 $srcValidatorDllPath（Validator 项目构建产物缺失）" -ForegroundColor Red
        exit 1
    }
    $distValidatorBinDir = Join-Path $DistRoot "toolchain\validator\bin"
    New-Item -ItemType Directory -Force -Path $distValidatorBinDir | Out-Null
    Get-ChildItem -Path $srcValidatorTfmDir -File | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination (Join-Path $distValidatorBinDir $_.Name) -Force
    }
    $validatorAssemblyShaMap = [ordered]@{
        "Validator.dll" = (Get-Sha256FileHash -Path (Join-Path $distValidatorBinDir "Validator.dll"))
    }
    # 空 Directory.Build.props：内容固定为 `<Project></Project>` 换行结尾（LF，无 BOM，任务书硬性
    # 规则"生成的文本文件一律 LF"）。放在 toolchain\validator\ 下（与 Validator.csproj 同级），
    # 是 MSBuild 沿项目目录向上查找时会命中的第一份，彻底挡住消费方仓库根同名文件被隐式继承。
    $distValidatorDbpPath = Join-Path $DistRoot "toolchain\validator\Directory.Build.props"
    $dbpContent = "<Project>`n</Project>`n"
    [System.IO.File]::WriteAllText($distValidatorDbpPath, $dbpContent, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("  已补齐 {0} 个文件 -> dist\{1}\toolchain\validator\bin\（Validator.dll sha256={2}）+ 空 Directory.Build.props" -f (Get-ChildItem -Path $distValidatorBinDir -File).Count, $DistDirVersion, $validatorAssemblyShaMap["Validator.dll"])

    # -------------------------------------------------------------------
    # 5.1 版本可追溯任务新增：把解析出的版本号写回 dist 内两个 package.json
    #     （含 games/_template 对适配层包的依赖版本号），保持"单一版本源"——
    #     源码仓库里的两个 package.json 已经在提交时同步改成当前 VERSION，这里
    #     针对的是"显式传入与仓库当前 VERSION 不同的版本号打历史/预发布快照"这一种
    #     场景（例如 -Dist auto 之外的显式覆盖），确保 dist 产物里的 package.json
    #     永远与本次打包的 $ResolvedDistVersion 一致，不依赖调用方提前手改源码。
    # -------------------------------------------------------------------
    function Set-DistPackageJsonVersion {
        param(
            [string]$JsonPath,
            [string]$Version
        )
        if (-not (Test-Path $JsonPath)) {
            Write-Host "  未找到 $JsonPath，跳过版本回写" -ForegroundColor Yellow
            return
        }
        # 判断记录：这些 package.json 是不带 BOM 的 UTF-8（git 常见约定）。Windows PowerShell 5.1
        # 的 Get-Content 在没有 BOM 时按系统 ANSI 代码页猜编码，会把文件里的中文字符读成乱码
        # （ConvertFrom-Json 甚至可能因此报"Invalid object passed in"）；必须显式 -Encoding UTF8。
        $obj = (Get-Content -Path $JsonPath -Raw -Encoding UTF8) | ConvertFrom-Json
        $obj.version = $Version
        if (($obj.PSObject.Properties.Name -contains "dependencies") -and
            ($obj.dependencies.PSObject.Properties.Name -contains "com.gamefoundation.adapter.unity")) {
            $obj.dependencies."com.gamefoundation.adapter.unity" = $Version
        }
        ($obj | ConvertTo-Json -Depth 10) | Set-Content -Path $JsonPath -Encoding utf8
        Write-Host "  已回写版本号 $Version -> $JsonPath"
    }

    Set-DistPackageJsonVersion -JsonPath (Join-Path $DistRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\package.json") -Version $ResolvedDistVersion
    Set-DistPackageJsonVersion -JsonPath (Join-Path $DistRoot "games\_template\package.json") -Version $ResolvedDistVersion

    # -------------------------------------------------------------------
    # 5.15 私服交付通道新增：组装四个可发布包（ADR-0018 决策 3 新增第四个包
    #      com.gamefoundation.adapter.headless，见下）到 dist/<ver>/packages/{包名}/，并 npm pack
    #      出四个 .tgz 到同一目录（见 .PARAMETER Dist 私服交付通道新增说明、toolchain/registry/
    #      README.md）。无论本次是 -Dist 还是 -Release、是否 -DryRun 都会执行——打包本身不是
    #      "发布"这个有副作用的动作，`npm pack` 只在本地生成 tar 包，不联网、不改变任何远端状态；
    #      真正有副作用的 `npm publish` 由下面 -Release 第 7 步之后的 -PublishRegistry 单独控制。
    # -------------------------------------------------------------------
    Write-Step "打四个 npm 包（私服交付通道）：dist\$DistDirVersion\packages\"

    $PackagesRoot = Join-Path $DistRoot "packages"
    New-Item -ItemType Directory -Force -Path $PackagesRoot | Out-Null

    function Set-PackageJsonVersionInline {
        param([string]$JsonPath, [string]$Version)
        $obj = (Get-Content -Path $JsonPath -Raw -Encoding UTF8) | ConvertFrom-Json
        $obj.version = $Version
        ($obj | ConvertTo-Json -Depth 10) | Set-Content -Path $JsonPath -Encoding utf8
    }

    # 包 1：com.gamefoundation.adapter.unity —— 整份拷贝 dist 内已经同步过版本号的适配层包目录
    # （上面 5.1 节已经把这份 package.json 的 version 字段改成 $ResolvedDistVersion，这里不需要
    # 重复改写）。
    $pkgAdapterDir = Join-Path $PackagesRoot "com.gamefoundation.adapter.unity"
    Copy-Item -Path (Join-Path $DistRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity") -Destination $pkgAdapterDir -Recurse -Force
    Write-Host "  已组装 $pkgAdapterDir"

    # 包 2：com.gamefoundation.framework-data —— package.json/README.md 取自源码里维护的包清单
    # toolchain/registry/manifests/framework-data/；内容取自 dist 内已经打包好的 data/_framework、
    # assets/_placeholder、assets/textmesh_pro_essentials 三棵目录树（见上方 Copy-DistDir 调用
    # 列表），放进包内 Data~/（Unity 不导入该目录、游戏侧按路径读字节，见该包 README.md 判断
    # 记录）。
    $pkgDataDir = Join-Path $PackagesRoot "com.gamefoundation.framework-data"
    New-Item -ItemType Directory -Force -Path $pkgDataDir | Out-Null
    $frameworkDataManifestDir = Join-Path $RepoRoot "toolchain\registry\manifests\framework-data"
    Copy-Item -Path (Join-Path $frameworkDataManifestDir "package.json") -Destination (Join-Path $pkgDataDir "package.json") -Force
    Copy-Item -Path (Join-Path $frameworkDataManifestDir "README.md") -Destination (Join-Path $pkgDataDir "README.md") -Force
    Set-PackageJsonVersionInline -JsonPath (Join-Path $pkgDataDir "package.json") -Version $ResolvedDistVersion
    $pkgDataDataTilde = Join-Path $pkgDataDir "Data~"
    New-Item -ItemType Directory -Force -Path $pkgDataDataTilde | Out-Null
    Copy-Item -Path (Join-Path $DistRoot "data\_framework") -Destination (Join-Path $pkgDataDataTilde "data\_framework") -Recurse -Force
    Copy-Item -Path (Join-Path $DistRoot "assets\_placeholder") -Destination (Join-Path $pkgDataDataTilde "assets\_placeholder") -Recurse -Force
    Copy-Item -Path (Join-Path $DistRoot "assets\textmesh_pro_essentials") -Destination (Join-Path $pkgDataDataTilde "assets\textmesh_pro_essentials") -Recurse -Force
    Write-Host "  已组装 $pkgDataDir"

    # 包 3：com.gamefoundation.toolchain —— package.json/README.md 同上取自
    # toolchain/registry/manifests/toolchain/；内容取自 dist 内已经打包好的 toolchain/（上方
    # Copy-DistDir 调用已排除 .venv/__pycache__/registry/node_modules/bin/obj），放进包内 Tools~/。
    $pkgToolDir = Join-Path $PackagesRoot "com.gamefoundation.toolchain"
    New-Item -ItemType Directory -Force -Path $pkgToolDir | Out-Null
    $toolchainManifestDir = Join-Path $RepoRoot "toolchain\registry\manifests\toolchain"
    Copy-Item -Path (Join-Path $toolchainManifestDir "package.json") -Destination (Join-Path $pkgToolDir "package.json") -Force
    Copy-Item -Path (Join-Path $toolchainManifestDir "README.md") -Destination (Join-Path $pkgToolDir "README.md") -Force
    Set-PackageJsonVersionInline -JsonPath (Join-Path $pkgToolDir "package.json") -Version $ResolvedDistVersion
    Copy-Item -Path (Join-Path $DistRoot "toolchain") -Destination (Join-Path $pkgToolDir "Tools~") -Recurse -Force
    Write-Host "  已组装 $pkgToolDir"

    # 包 4：com.gamefoundation.adapter.headless（ADR-0018 决策 3 新增，无头适配层交付）——
    # package.json/README.md 同上取自 toolchain/registry/manifests/adapter-headless/；内容取自
    # 上面 5.056 节已经拷进 dist 的 adapters\headless\Adapters.Stub.dll，放进包内 Lib~/（Unity 不
    # 导入该目录；本包本身也不是 Unity 依赖，见该包 README.md 判断记录"为什么本包不写入
    # Packages/manifest.json"）。
    $pkgHeadlessDir = Join-Path $PackagesRoot "com.gamefoundation.adapter.headless"
    New-Item -ItemType Directory -Force -Path $pkgHeadlessDir | Out-Null
    $adapterHeadlessManifestDir = Join-Path $RepoRoot "toolchain\registry\manifests\adapter-headless"
    Copy-Item -Path (Join-Path $adapterHeadlessManifestDir "package.json") -Destination (Join-Path $pkgHeadlessDir "package.json") -Force
    Copy-Item -Path (Join-Path $adapterHeadlessManifestDir "README.md") -Destination (Join-Path $pkgHeadlessDir "README.md") -Force
    Set-PackageJsonVersionInline -JsonPath (Join-Path $pkgHeadlessDir "package.json") -Version $ResolvedDistVersion
    $pkgHeadlessLibTilde = Join-Path $pkgHeadlessDir "Lib~"
    New-Item -ItemType Directory -Force -Path $pkgHeadlessLibTilde | Out-Null
    Copy-Item -Path (Join-Path $DistRoot "adapters\headless\Adapters.Stub.dll") -Destination (Join-Path $pkgHeadlessLibTilde "Adapters.Stub.dll") -Force
    Write-Host "  已组装 $pkgHeadlessDir"

    foreach ($pkgDirForPack in @($pkgAdapterDir, $pkgDataDir, $pkgToolDir, $pkgHeadlessDir)) {
        & npm pack $pkgDirForPack --pack-destination $PackagesRoot --silent | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "npm pack 失败：$pkgDirForPack（退出码 $LASTEXITCODE，本机是否已安装 node/npm？）"
        }
    }
    $tgzFiles = @(Get-ChildItem -Path $PackagesRoot -Filter "*.tgz" -File)
    Write-Host ("  已生成 {0} 个 .tgz：{1}" -f $tgzFiles.Count, (($tgzFiles | ForEach-Object { $_.Name }) -join ", "))

    # -------------------------------------------------------------------
    # 5.2 版本可追溯任务新增：git_commit（工作树不干净时加 -dirty 后缀）
    # -------------------------------------------------------------------
    Push-Location $RepoRoot
    try {
        $gitCommitShort = (& git rev-parse --short HEAD).Trim()
        $gitStatusPorcelain = & git status --porcelain
        $gitDirty = $false
        if ($null -ne $gitStatusPorcelain) {
            $joined = ($gitStatusPorcelain -join "`n").Trim()
            if ($joined -ne "") { $gitDirty = $true }
        }
        if ($gitDirty) {
            $gitCommit = "$gitCommitShort-dirty"
        } else {
            $gitCommit = $gitCommitShort
        }
    } catch {
        $gitCommit = "(unknown：$($_.Exception.Message))"
    } finally {
        Pop-Location
    }

    # -------------------------------------------------------------------
    # 5.3 版本可追溯任务新增：architecture_docs —— 逐篇解析 architecture/0*.md、1*.md
    #     首行标题里的版本号（形如"# xxx vN"）。dist 本身不打包 architecture/ 目录（见
    #     Copy-DistDir 调用列表），这里只是把"打这份快照时，架构文档集处于哪个版本组合"
    #     记录进 MANIFEST，供 11 第 7 节要求的可追溯性核对。
    # -------------------------------------------------------------------
    $archDocLines = @()
    $archDir = Join-Path $RepoRoot "architecture"
    $archFiles = @()
    $archFiles += Get-ChildItem -Path $archDir -Filter "0*.md" -File -ErrorAction SilentlyContinue
    $archFiles += Get-ChildItem -Path $archDir -Filter "1*.md" -File -ErrorAction SilentlyContinue
    $archFiles = $archFiles | Sort-Object Name
    foreach ($af in $archFiles) {
        $firstLine = Get-Content -Path $af.FullName -TotalCount 1 -Encoding UTF8
        $m = [regex]::Match($firstLine, 'v(\d+)\s*$')
        if ($m.Success) {
            $archDocLines += ("  {0}: v{1}" -f $af.Name, $m.Groups[1].Value)
        } else {
            $archDocLines += ("  {0}: (未识别到版本号，首行：{1})" -f $af.Name, $firstLine.Trim())
        }
    }

    # -------------------------------------------------------------------
    # 5.4 版本可追溯任务新增：data_schemas —— 遍历 data/_framework 下每张表的
    #     table/schema_version（data/_sample 不随 dist 分发，因此不列入；见 data/README.md
    #     "两类目录"一节与 build.ps1 判断记录）。
    # -------------------------------------------------------------------
    $dataSchemaLines = @()
    $dataFrameworkSrcDir = Join-Path $RepoRoot "data\_framework"
    if (Test-Path $dataFrameworkSrcDir) {
        $schemaJsonFiles = Get-ChildItem -Path $dataFrameworkSrcDir -Filter "*.json" -File -Recurse | Sort-Object FullName
        foreach ($sjf in $schemaJsonFiles) {
            try {
                $tableObj = (Get-Content -Path $sjf.FullName -Raw -Encoding UTF8) | ConvertFrom-Json
                $dataSchemaLines += ("  {0}: schema_version={1}" -f $tableObj.table, $tableObj.schema_version)
            } catch {
                $dataSchemaLines += ("  {0}: 解析失败（{1}）" -f $sjf.Name, $_.Exception.Message)
            }
        }
    }

    # -------------------------------------------------------------------
    # 5.5 版本可追溯任务新增：core_assemblies —— 六个核心 DLL 的 sha256，取 dist 内
    #     刚拷贝进适配层包的那一份（与实际分发物一致，而不是仓库内 core/*/bin/ 下的构建产物，
    #     两者理论上内容相同，但直接对 dist 内文件取哈希更贴合"这份快照实际包含什么"）。
    # -------------------------------------------------------------------
    $coreAssemblyLines = @()
    # 版本管理方案新增：与 $coreAssemblyLines（人读文本）并行记一份哈希映射表，供下面 -Release
    # 流程生成 ws-game.lock（机读 JSON）复用，避免重新计算或反解析上面那行文本。
    $coreAssemblyShaMap = [ordered]@{}
    $distPluginsCoreDir = Join-Path $DistRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core"
    foreach ($asm in $CoreAssemblies) {
        $distDllPath = Join-Path $distPluginsCoreDir ($asm.Name + ".dll")
        if (Test-Path $distDllPath) {
            $sha = Get-Sha256FileHash -Path $distDllPath
            $coreAssemblyLines += ("  {0}.dll: sha256={1}" -f $asm.Name, $sha)
            $coreAssemblyShaMap[$asm.Name + ".dll"] = $sha
        } else {
            $coreAssemblyLines += ("  {0}.dll: 未找到（{1}）" -f $asm.Name, $distDllPath)
            $coreAssemblyShaMap[$asm.Name + ".dll"] = $null
        }
    }

    $manifestPath = Join-Path $DistRoot "MANIFEST.txt"
    $manifestLines = @(
        "version: $ResolvedDistVersion",
        "date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "git_commit: $gitCommit",
        "",
        "[directory_file_counts]",
        "adapters/unity/Packages/com.gamefoundation.adapter.unity: $adapterFileCount files",
        "games/_template: $templateFileCount files",
        "toolchain: $toolchainFileCount files",
        "assets/_placeholder: $assetsFileCount files",
        "data/_framework: $dataFrameworkFileCount files",
        "assets/textmesh_pro_essentials: $tmpEssentialsFileCount files",
        "",
        "[architecture_docs]"
    ) + $archDocLines + @(
        "",
        "[data_schemas]"
    ) + $dataSchemaLines + @(
        "",
        "[core_assemblies]"
    ) + $coreAssemblyLines + @(
        "",
        "[headless_assemblies]",
        ("  Adapters.Stub.dll: sha256=" + $headlessAssemblyShaMap["Adapters.Stub.dll"]),
        "",
        "[validator]",
        ("  Validator.dll: sha256=" + $validatorAssemblyShaMap["Validator.dll"])
    )

    Set-Content -Path $manifestPath -Value $manifestLines -Encoding utf8
    Write-Host "已生成 $manifestPath"

    # -------------------------------------------------------------------
    # 5.6 版本管理方案新增：zip + lock。默认只在 -Release 时跑（单纯 -Dist/-Dist auto 只需要
    #     dist/<version>/ 目录本身，不需要额外打 zip/lock，见既有调用方 consumer_smoke.ps1/
    #     games/_template/README.md 的用法——都是直接指向 dist/<version>/ 目录，不消费 zip）；
    #     独立开关 -Zip 可以在不走 -Release 校验/提交/打标签的前提下单独触发这一步（见
    #     .PARAMETER Zip 说明，`.github/workflows/release.yml` 用这条路径）。
    # -------------------------------------------------------------------
    if ($ReleaseRequested -or $Zip) {
        Write-Step "打 zip + lock（dist/ws-game-$DistDirVersion.zip / .lock）"

        $zipPath = Join-Path $RepoRoot ("dist\ws-game-" + $DistDirVersion + ".zip")
        $zipTopLevelName = "ws-game-" + $DistDirVersion
        $zipStagingRoot = Join-Path $env:TEMP ("ws_game_zip_staging_" + [guid]::NewGuid().ToString("N"))
        $zipStagingDir = Join-Path $zipStagingRoot $zipTopLevelName
        New-Item -ItemType Directory -Force -Path $zipStagingDir | Out-Null
        try {
            # Copy-Item -Recurse 复制 $DistRoot 的内容（不含 $DistRoot 自身这层目录名）到
            # $zipStagingDir，这样 Compress-Archive 传入 $zipStagingDir 时，zip 内顶层目录名
            # 就是 $zipStagingDir 的 basename（即 "ws-game-<ver>"），而不是 "<ver>"。
            Copy-Item -Path (Join-Path $DistRoot "*") -Destination $zipStagingDir -Recurse -Force
            if (Test-Path $zipPath) {
                Remove-Item -Path $zipPath -Force
            }
            Compress-Archive -Path $zipStagingDir -DestinationPath $zipPath -CompressionLevel Optimal
        } finally {
            Remove-Item -Path $zipStagingRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
        $zipSizeBytes = (Get-Item $zipPath).Length
        $zipSizeMb = [Math]::Round($zipSizeBytes / 1MB, 2)
        Write-Host ("  已生成 {0}（{1} MB，zip 内顶层目录 {2}/）" -f $zipPath, $zipSizeMb, $zipTopLevelName)

        # -------------------------------------------------------------------
        # 5.65 消费方反馈 E4 根治（architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E4）：
        #      主 zip（上面这份）从不带 data/_sample、assets/_sample——那是本仓库自测用的验收数据集
        #      （见 data/README.md"两类目录"一节），不代表任何真实游戏内容，因此不随 -Dist 打进
        #      dist/<ver>/ 快照（见前面"打分发包"一节 Copy-DistDir 调用列表，只带 data/_framework）。
        #      但消费方反馈：没有这份验收数据集，游戏侧新工程接入后想验证"框架端到端能不能跑起来"
        #      缺一份现成的、已知合法的样例数据/资源可用（自己从零手写一份 world.map/quest.def 等
        #      成本高、还可能踩数据格式的坑）。根治：额外单独打一份
        #      dist/ws-game-<ver>-samples.zip（主 zip 内容不变，向后兼容——已经按主 zip 校验通过的
        #      消费方接入流程不受影响），内含 data/_sample、assets/_sample 两棵目录树，zip 内顶层
        #      目录名与主 zip 同一约定（"ws-game-<ver>/"），解压后可以直接与主 zip 的解压结果合并到
        #      同一个 <Target>/ws-game-<ver>/ 目录下（两者内容路径不重叠：主 zip 没有 data/_sample、
        #      assets/_sample 这两棵目录）。lock 文件新增 `samples.sha256` 字段；
        #      `toolchain/get_framework.ps1 -WithSamples` 下载/解压并按该字段校验（见该脚本判断
        #      记录）。源文件本身在仓库里已经是 LF（见工程规范"生成的文本文件一律 LF"，data/_sample、
        #      assets/_sample 下的 JSON/MD 均受此约束），Compress-Archive 按字节原样打包，不需要
        #      额外转换行尾。
        # -------------------------------------------------------------------
        Write-Step "打 samples zip（dist/ws-game-$DistDirVersion-samples.zip，消费方反馈 E4 根治）"
        $samplesZipPath = Join-Path $RepoRoot ("dist\ws-game-" + $DistDirVersion + "-samples.zip")
        $samplesStagingRoot = Join-Path $env:TEMP ("ws_game_samples_zip_staging_" + [guid]::NewGuid().ToString("N"))
        $samplesStagingDir = Join-Path $samplesStagingRoot $zipTopLevelName
        New-Item -ItemType Directory -Force -Path $samplesStagingDir | Out-Null
        try {
            $srcSampleDataDir = Join-Path $RepoRoot "data\_sample"
            $srcSampleAssetsDir = Join-Path $RepoRoot "assets\_sample"
            if (-not (Test-Path $srcSampleDataDir)) {
                Write-Host "打分发包失败：找不到 $srcSampleDataDir（验收数据集源目录缺失）" -ForegroundColor Red
                exit 1
            }
            if (-not (Test-Path $srcSampleAssetsDir)) {
                Write-Host "打分发包失败：找不到 $srcSampleAssetsDir（验收数据集源目录缺失）" -ForegroundColor Red
                exit 1
            }
            Copy-Item -Path $srcSampleDataDir -Destination (Join-Path $samplesStagingDir "data\_sample") -Recurse -Force
            Copy-Item -Path $srcSampleAssetsDir -Destination (Join-Path $samplesStagingDir "assets\_sample") -Recurse -Force
            if (Test-Path $samplesZipPath) {
                Remove-Item -Path $samplesZipPath -Force
            }
            Compress-Archive -Path $samplesStagingDir -DestinationPath $samplesZipPath -CompressionLevel Optimal
        } finally {
            Remove-Item -Path $samplesStagingRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
        $samplesZipSha = Get-Sha256FileHash -Path $samplesZipPath
        $samplesZipSizeMb = [Math]::Round(((Get-Item $samplesZipPath).Length) / 1MB, 2)
        Write-Host ("  已生成 {0}（{1} MB，sha256={2}）" -f $samplesZipPath, $samplesZipSizeMb, $samplesZipSha)

        # ws-game.lock 示例锁文件：版本号、git_commit、六个核心 DLL 的 sha256（复用上面 5.5 节已经
        # 算好的 $coreAssemblyShaMap，不重复计算）、无头适配层 DLL 的 sha256（ADR-0018 决策 3 新增
        # `headless_dlls` 字段，复用上面 5.056 节已经算好的 $headlessAssemblyShaMap）。字段内容一律
        # 用干净版本号 $ResolvedDistVersion（不带 -dryrun 后缀）——DryRun 只是产物文件名带后缀以
        # 避免覆盖真实发布产物，锁文件内容描述的仍然是"这是版本 X.Y.Z 的锁定信息"这一事实本身。
        # 游戏仓库拿到这份文件后原样复制为自己的 ws-game.lock（见 toolchain/get_framework.ps1）；
        # `headless_dlls`/`validator_dlls` 均为可选字段，老版本锁文件没有该字段时
        # get_framework.ps1 跳过对应校验并提示，保持向后兼容（见该脚本判断记录）。`validator_dlls`
        # 是消费方反馈 E1 根治新增（复用上面 5.057 节已经算好的 $validatorAssemblyShaMap）：记录
        # dist 内预编译 toolchain\validator\bin\Validator.dll 的 sha256，与 `dlls`/`headless_dlls`
        # 同一模式（文件名 -> sha256 的映射），供 get_framework.ps1 一并校验完整性。`samples` 是
        # 消费方反馈 E4 根治新增：记录 dist/ws-game-<ver>-samples.zip 自身（整份 zip，不是内部
        # 单个文件）的 sha256，`toolchain/get_framework.ps1 -WithSamples` 下载后据此校验。
        $lockPath = Join-Path $RepoRoot ("dist\ws-game-" + $DistDirVersion + ".lock")
        $lockObj = [ordered]@{
            version      = $ResolvedDistVersion
            git_commit   = $gitCommit
            dlls         = $coreAssemblyShaMap
            headless_dlls = $headlessAssemblyShaMap
            validator_dlls = $validatorAssemblyShaMap
            samples      = [ordered]@{ sha256 = $samplesZipSha }
        }
        $lockJson = ($lockObj | ConvertTo-Json -Depth 5)
        [System.IO.File]::WriteAllText($lockPath, $lockJson, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  已生成 $lockPath"

        # -------------------------------------------------------------------
        # 5.7 版本管理方案新增：-Release 第 7 步——非 DryRun 时打包完成自检 + 打标签（提交已经在
        #     第 6 步、打包之前完成，见该步骤判断记录）；DryRun 到此为止（本步骤生成的 zip/lock
        #     已经落在 dist/ 下的 -dryrun 后缀路径，dist/ 整体 .gitignore，不影响"结束时工作树
        #     干净"这条要求）。
        # -------------------------------------------------------------------
        if ($ReleaseRequested -and (-not $DryRun)) {
            Write-Step "-Release：打包完成自检 + 打带注释标签 v$Release"

            # 自检（时序缺陷根治新增，见上方"第 6 步"判断记录）：提交已经在打包之前完成，这里核对
            # 5.2 节算出的 $gitCommit 确实就是那次提交、且工作树干净（不带 "-dirty" 后缀）。提交
            # 与这里之间只有 DLL 同步、内容数据集同步两步，二者都只写 .gitignore 覆盖的路径
            # （见仓库根 .gitignore "Runtime/Plugins/Core/"、"Assets/StreamingAssets/" 两条），
            # 正常不会让工作树变脏；一旦触发说明有已入库文件被意外改动（例如字体或工程设置被
            # 写入了新内容），必须在打标签前挡住——标签一旦打在一个内容与 lock/MANIFEST 记录的
            # git_commit 对不上的提交上，就是一句关于"这份产物对应哪个提交"的谎言。
            if ($gitDirty -or ($gitCommitShort -ne $ReleaseCommitShort)) {
                Write-Host "打包完成自检失败：lock/MANIFEST 记录的 git_commit=$gitCommit，期望的发布提交=$ReleaseCommitShort（干净、不带 -dirty）" -ForegroundColor Red
                Write-Host "提交 $ReleaseCommitHash（发布 $Release）已产生但未打标签。请先排查是谁改动了已入库文件并修复/清理，然后执行：" -ForegroundColor Red
                Write-Host "  git reset --soft $ReleaseParentCommitHash" -ForegroundColor Red
                Write-Host "回退这次半途的发布提交，再重新运行 -Release。" -ForegroundColor Red
                exit 1
            }
            Write-Host "  自检通过：git_commit=$gitCommit 即发布提交 $ReleaseCommitHash，打包时工作树干净"

            Push-Location $RepoRoot
            try {
                $tagName = "v$Release"
                $tagMessageFile = Join-Path $RepoRoot ("dist\tag-message-" + $Release + ".txt")
                $tagMessageContent = "$tagName`n`n$ReleaseChangelogSection"
                [System.IO.File]::WriteAllText($tagMessageFile, $tagMessageContent, (New-Object System.Text.UTF8Encoding($false)))
                & git tag -a $tagName -F $tagMessageFile
                if ($LASTEXITCODE -ne 0) { throw "git tag 失败，退出码 $LASTEXITCODE" }
                Remove-Item -Path $tagMessageFile -Force -ErrorAction SilentlyContinue
                Write-Host "  已打标签：$tagName"
            } finally {
                Pop-Location
            }

            # -PublishRegistry：私服交付通道新增，独立于 -Publish 单独控制（见 .PARAMETER
            # PublishRegistry 说明）。npm publish 对已存在的版本号本身会失败，天然满足"发布不
            # 可变"，不需要本脚本额外加校验。
            if ($PublishRegistry) {
                Write-Step "-PublishRegistry：npm publish 四个包到私服"

                $resolvedRegistryUrl = $RegistryUrl
                if ($resolvedRegistryUrl -eq "") {
                    $registryJsonPath = Join-Path $RepoRoot "toolchain\registry\registry.json"
                    if (-not (Test-Path $registryJsonPath)) {
                        throw "找不到 $registryJsonPath，且未显式传 -RegistryUrl"
                    }
                    $resolvedRegistryUrl = ((Get-Content -Path $registryJsonPath -Raw -Encoding UTF8) | ConvertFrom-Json).url
                }
                $registryNpmrcPath = Join-Path $RepoRoot "toolchain\registry\.npmrc"
                if (-not (Test-Path $registryNpmrcPath)) {
                    throw "找不到 $registryNpmrcPath（先跑 toolchain/registry/init_publisher.ps1 无人值守生成发布账号令牌）"
                }

                foreach ($pkgDirForPublish in @($pkgAdapterDir, $pkgDataDir, $pkgToolDir, $pkgHeadlessDir)) {
                    Write-Host "  npm publish $pkgDirForPublish --registry $resolvedRegistryUrl"
                    & npm publish $pkgDirForPublish --registry $resolvedRegistryUrl --userconfig $registryNpmrcPath
                    if ($LASTEXITCODE -ne 0) {
                        throw "npm publish 失败：$pkgDirForPublish（退出码 $LASTEXITCODE；若原因是版本号已存在，说明该版本已经发布过，符合'发布不可变'，请发新版本号而不是覆盖）"
                    }
                }
                Write-Host "  已发布四个包 version=$Release 到 $resolvedRegistryUrl" -ForegroundColor Green
            }

            # 第 8 步：打印后续需要人工/设计层执行的两条命令；-Publish 时自动执行。
            # 判断记录（P05 根治，2026-09-07，审计 architecture/落地计划/audit-7e63d66-20260907/
            # project-review.md P05）：此前硬编码 `git push origin main --tags`——无论 -Release
            # 实际在哪个分支上执行（例如维护分支 release/1.0.x 上打 PATCH 版本），都固定推 main，
            # 且 `--tags` 会把本地全部标签一起推送，不是"只推本次新建的这一个标签"。改为取当前
            # 实际检出的分支（`git rev-parse --abbrev-ref HEAD`，-Release 全程不切换分支，此时
            # 就是打标签所在的那个分支）+ 只推本次创建的这一个标签的完整 ref（`refs/tags/<tag>`，
            # 避免裸标签名在极端情况下与分支名同名产生的歧义），维护分支场景下 main 不会被隐式
            # 推进；main 分支上按正常发布，效果与改动前的"推 main"完全一致（当前分支就是 main）。
            $currentBranchForPush = (& git rev-parse --abbrev-ref HEAD).Trim()
            if ([string]::IsNullOrEmpty($currentBranchForPush) -or $currentBranchForPush -eq "HEAD") {
                throw "无法确定当前分支（detached HEAD 或 git rev-parse 失败），-Release/-Publish 要求在一个具名分支（main 或维护分支 release/X.Y.x）上执行"
            }
            # 判断记录（消费方反馈 E2 根治，2026-09-10，见
            # architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E2）：Release 附件集合新增
            # toolchain/get_framework.ps1（自包含后，游戏侧只下载这一个文件即可用，见该脚本文件头
            # 判断记录）；继续一并附上 toolchain/_hash.ps1，兼容消费方现有"下载 get_framework.ps1 +
            # _hash.ps1 两个文件"的还原脚本（本脚本自身不再读取它，纯粹是向后兼容附件，见
            # .github/workflows/release.yml 同步的必需附件集合判断）。两个文件都取自源码仓库
            # toolchain/ 下当前提交的版本（与本次发布提交内容一致，不是从 $DistRoot 里再拷一份）。
            $getFrameworkAttachPath = Join-Path $RepoRoot "toolchain\get_framework.ps1"
            $hashPsAttachPath = Join-Path $RepoRoot "toolchain\_hash.ps1"
            # 消费方反馈 E4 根治新增附件：dist/ws-game-<ver>-samples.zip（上面 5.65 节已生成）。
            $pushCmd = "git push origin $currentBranchForPush refs/tags/$tagName"
            $releaseCmd = "gh release create $tagName `"$zipPath`" `"$lockPath`" `"$samplesZipPath`" `"$getFrameworkAttachPath`" `"$hashPsAttachPath`" --title `"$tagName`" --notes-file `"$releaseNotesPath`""

            Write-Host ""
            Write-Host "==== -Release 完成：$ReleaseCurrentVersion -> $Release（已提交 + 已打标签 $tagName） ====" -ForegroundColor Green
            Write-Host "后续需要人工/设计层执行（-Publish 可自动执行，本次未传则仅打印）：" -ForegroundColor Cyan
            Write-Host "  1) $pushCmd"
            Write-Host "  2) $releaseCmd"

            if ($ReleaseBumpIsMajorOrMinor) {
                $branchName = "release/" + $newParts.Major + "." + $newParts.Minor + ".x"
                $branchPoint = "v" + $newParts.Major + "." + $newParts.Minor + ".0"
                Write-Host ""
                Write-Host "本次版本号 MAJOR 或 MINOR 段发生了变化，建议开一条维护分支（见根 README.md" -ForegroundColor Cyan
                Write-Host "'维护分支与 PATCH 发布流程'一节）：" -ForegroundColor Cyan
                Write-Host "  git branch $branchName $branchPoint"
            }

            if ($Publish) {
                Write-Step "-Publish：自动执行上面两条命令"
                Push-Location $RepoRoot
                try {
                    Write-Host "  执行：$pushCmd"
                    & git push origin $currentBranchForPush "refs/tags/$tagName"
                    if ($LASTEXITCODE -ne 0) { throw "git push 失败，退出码 $LASTEXITCODE" }

                    Write-Host "  执行：$releaseCmd"
                    & gh release create $tagName $zipPath $lockPath $samplesZipPath $getFrameworkAttachPath $hashPsAttachPath --title $tagName --notes-file $releaseNotesPath
                    if ($LASTEXITCODE -ne 0) { throw "gh release create 失败，退出码 $LASTEXITCODE" }
                } finally {
                    Pop-Location
                }
                Write-Host "  -Publish 完成：已推送并创建 GitHub Release $tagName"
            }
        } elseif ($ReleaseRequested -and $DryRun) {
            Write-Host ""
            Write-Host "==== -DryRun 完成：$Release 的发布流水线全流程校验 + 打包已跑通，未改写任何源码文件、未提交、未打标签 ====" -ForegroundColor Green
            Write-Host "  dist/$DistDirVersion/、$zipPath、$lockPath、$samplesZipPath 均为验证产物（dist/ 已 .gitignore，可随时删除）"
            Write-Host "  dist/$DistDirVersion/packages/ 下四个包目录 + .tgz 同样已生成（npm pack，本地打包不联网）；-DryRun 不会 npm publish，见 .PARAMETER PublishRegistry"
        } else {
            Write-Host ""
            Write-Host "==== -Zip 完成：$zipPath、$lockPath、$samplesZipPath 已生成，未涉及版本号写回/提交/打标签（-Zip 独立于 -Release 使用） ====" -ForegroundColor Green
        }
    }
} else {
    Write-Step "未传 -Dist/-Release，跳过打包步骤"
}

Write-Host ""
Write-Host "build.ps1 完成。" -ForegroundColor Green
exit 0
