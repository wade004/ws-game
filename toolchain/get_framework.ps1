<#
.SYNOPSIS
    游戏仓库用的框架引用工具：按版本号拉取本框架（ws-game）的发布产物（`ws-game-<ver>.zip` +
    `ws-game-<ver>.lock`），校验六个核心 DLL 的 sha256（锁文件存在 `headless_dlls`/`validator_dlls`
    字段时一并校验无头适配层 DLL/预编译 validator 的 DLL，见 ADR-0018 决策 3、消费方反馈 E1；
    老锁文件没有这两个字段时跳过对应校验并提示，向后兼容）与锁文件一致后解压到
    `<Target>/ws-game-<ver>/`，并在游戏仓库根写入/校验 `ws-game.lock`（见仓库根 README.md
    "版本与发布"一节、`architecture/落地计划/落地方案与分阶段计划.md` 第 3.5 节"新游戏如何消费
    本框架"）。本脚本运行在游戏仓库那一侧，不属于框架仓库自身的构建/门禁链路，只是框架随发布产物
    一并提供、供游戏侧调用的工具。

    判断记录（消费方反馈 E2 根治，2026-09-10，见
    architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E2）：本脚本此前用哈希校验依赖同目录
    `_hash.ps1`（`Get-Sha256FileHash` 共享函数）dot-source 加载——但游戏仓库侧引用本脚本的典型
    方式恰恰是"只下载 get_framework.ps1 单个文件"（GitHub Release 附件里单独下载，见根 README.md
    "游戏侧引用与升级"一节；本脚本自身尚未落地前，游戏侧也无法先跑它去拉取 `toolchain/` 整个
    目录），`_hash.ps1` 只随 `ws-game-<ver>.zip`/UPM 包分发，不单独作为 Release 附件——形成"跑
    本脚本前得先有 `_hash.ps1`，但拿到 `_hash.ps1` 的唯一途径是先用本脚本拉取"的引导死锁。现已把
    `Get-Sha256FileHash` 函数体原样内联进本文件（与 `toolchain/_hash.ps1` 保持逐字节一致，
    `toolchain/tests/test_get_framework_hash_inline_consistency.py` 静态比对两处函数体，任一处
    改动而另一处未同步会让该测试失败），本脚本自身不再依赖任何同目录文件，可以单独下载/复制使用。
    `toolchain/_hash.ps1` 本身继续保留在 `toolchain/` 目录下供仓库内其它脚本（`build.ps1`、
    `sync_package_content.ps1`）共用；GitHub Release 附件集合新增 `get_framework.ps1`（自包含后
    的推荐下载方式），并为兼容消费方现有"下载 get_framework.ps1 + `_hash.ps1` 两个文件"的还原脚本
    继续一并附上 `_hash.ps1`（内容不变，本脚本不再读取它，纯粹是向后兼容附件）。

.PARAMETER Version
    要拉取的框架版本号，形如 X.Y.Z（必填）。

.PARAMETER Target
    解压目标的根目录，默认当前目录下 `packages`；实际解压目标是 `<Target>/ws-game-<Version>/`
    （若该目录已存在会被整体删除重建，视为一次全新拉取）。

.PARAMETER LockPath
    写入/校验的 `ws-game.lock` 路径，默认当前目录下 `ws-game.lock`（约定放在游戏仓库根，见 3.5
    节"游戏仓库根放 ws-game.lock"）。

.PARAMETER Repo
    框架发布所在的 GitHub 仓库（`owner/repo` 形式），默认 `wade004/ws-game`。只在在线拉取路径
    （未传 `-FromLocalDist`）时使用。

.PARAMETER FromLocalDist
    离线来源：直接指定本机已有的 `ws-game-<ver>.zip` 文件路径（同目录下需要有同名的
    `.lock` 文件，如 `ws-game-1.0.0.zip` 配 `ws-game-1.0.0.lock`），跳过 `gh release download`，
    用于本机验证发布产物、或没有网络访问 GitHub 权限的场景。

.PARAMETER AllowVersionMismatch
    判断记录（P04 根治，2026-09-07，审计 architecture/落地计划/audit-7e63d66-20260907/
    project-review.md P04）：默认严格模式——锁文件里记录的实际版本号（`ws-game-<Version>.lock`
    的 `version` 字段，即这份 zip 真正打包的版本）与调用方 `-Version` 请求的版本号不一致时直接
    报错退出，不落地、不删除/不覆盖任何已有的 `-Target` 子目录（此前只 warning 后继续，会按
    "请求版本号"命名落地目录并可能先删除该目录，导致例如请求 `-Version 1.0.1` 但本地归档实际是
    1.0.0 时，1.0.0 的真实内容被落进名为 `ws-game-1.0.1` 的目录、且可能先删除了原本正确的
    `ws-game-1.0.1` 目录——目录名与实际内容身份不符，是比"覆盖/删除"更根本的问题）。这个开关
    默认关闭；显式传入后放行版本不一致的请求，但落地目录名与打印的引用示例改用锁文件记录的
    *实际* 版本号（不是请求的 `-Version`），确保目录名永远与其内容真实身份一致；同时打印源
    （锁文件 version）与目标（本次请求的 -Version，以及最终落地目录名）两侧身份，避免静默生效。
    只影响 zip 通道的版本号一致性判定，不影响 DLL 哈希校验（哈希校验始终执行、不受本开关影响）。

.PARAMETER FromRegistry
    私服交付通道新增：与上面"拉 zip 解压到 -Target"是完全不同的另一条通道，不下载/不解压任何
    文件——UPM 私服场景下，"拉包"这件事由 Unity 编辑器自己在打开工程/刷新包管理器时向注册表发
    请求完成，本脚本不代劳，只负责两件事：1）生成/更新游戏工程的 `Packages/manifest.json`，写入
    作用域注册表条目与三个包依赖（版本号 = `-Version`）；2）写 `ws-game.lock`，记录本次引用的
    来源是私服（注册表地址、作用域、三个包名）与版本号，与 zip 通道写的锁文件同一个文件、不同的
    `source.channel` 取值（`registry` / `zip`），供后续升级/核对时区分当前用的是哪条通道。传了
    `-FromRegistry` 后 `-Target`/`-Repo`/`-FromLocalDist` 不生效（忽略，不报错）。

.PARAMETER RegistryUrl
    配合 `-FromRegistry` 使用：私服地址。省略时按顺序尝试：1）本脚本同目录下 `registry/
    registry.json` 的 `url` 字段（框架仓库自己的工作树布局，或随 `com.gamefoundation.toolchain`
    包分发时若该文件恰好在场）；2）都找不到则退化为默认值 `http://127.0.0.1:4873`。

.PARAMETER ManifestPath
    配合 `-FromRegistry` 使用：要写入/更新的 `Packages/manifest.json` 路径。省略时依次尝试当前
    目录下的 `Packages/manifest.json`（游戏仓库根调用本脚本时的常见相对位置）；两者都找不到时不
    报错，只把应该写入的片段打印到控制台，由调用方自行合并进自己的 `manifest.json`（可能路径不
    是常见位置，或调用方想自己手工检查这一步改动）。

.PARAMETER WithSamples
    消费方反馈 E4 根治新增（仅对 zip 通道有意义，`-FromRegistry` 时忽略）：额外下载/校验并解压
    `ws-game-<ver>-samples.zip`（内含 `data/_sample`、`assets/_sample` 两棵验收数据集目录树，
    build.ps1 -Dist/-Release 同一次打包新增产出，见根 README.md"版本与发布"一节），合并落地到与
    主 zip 相同的 `<Target>/ws-game-<Version>/` 目录下（两者内容路径不重叠）。在线拉取
    （未传 `-FromLocalDist`）时按 `ws-game-<ver>-samples.zip` 命名约定一并 `gh release download`；
    `-FromLocalDist <zip 路径>` 时按同目录同版本号的兄弟文件名约定查找（如
    `ws-game-1.0.0.zip` 配 `ws-game-1.0.0-samples.zip`）。按锁文件 `samples.sha256` 字段校验整份
    samples zip 的完整性；锁文件缺该字段（版本早于本次功能落地）时报错退出，提示改用不带
    `-WithSamples` 的调用或升级到更新的框架版本。校验失败时与主 zip 同样语义：不落地任何内容到
    `-Target`。

.NOTES
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。
    在线拉取依赖 `gh`（GitHub CLI）已登录且对 `-Repo` 指定的仓库有读权限；离线校验（
    `-FromLocalDist`）不依赖 `gh`、不需要网络；`-FromRegistry` 依赖私服本身可访问
    （`-RegistryUrl`/-/ping 返回 200），不依赖 `gh`。
    校验失败（DLL 哈希与锁文件不一致、或版本号不匹配）时不会把任何内容落地到 `-Target`，保持
    "校验通过才落地"的语义；已存在的旧版本目录只有在本次校验通过后才会被替换。这条"校验后才
    落地"的语义只适用于 zip 通道——`-FromRegistry` 通道的完整性校验交给 npm/UPM 自己的包传输
    机制（tarball 校验和），本脚本不重复实现。
    判断记录（PJ150-01 根治，2026-09-08，审计 architecture/落地计划/audit-3224ca1-20260908/
    AUDIT_REPORT.md PJ150-01）：`-AllowVersionMismatch` 放行版本不一致后，锁文件 `version` 字段
    此前未经格式校验就被用于拼接落地目录路径并触发 `Remove-Item -Recurse -Force`，构造成
    `x/../../outside_sentinel` 形式可越界删除 `-Target` 之外的目录，六个 DLL 的哈希校验不覆盖
    这个元数据字段。根治两层缺一不可：1）锁文件 `version` 与 `-Version` 同样严格校验语义化版本
    格式；2）落地路径规范化后必须仍是 `-Target` 的严格子目录（`Test-IsStrictSubPath` 函数），
    写入/删除前完成校验。同时把 zip 解压从无差别的 `Expand-Archive` 改为逐条目手动解压 + 同一套
    边界校验，堵住 zip 内条目路径本身携带 `../`（zip slip）的越界口子。详见 toolchain/README.md
    "`get_framework.ps1`（游戏侧按版本号引用本框架）"一节"路径边界判断记录"、回归测试
    `toolchain/tests/test_get_framework_path_boundary.py`。
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Target = "packages",
    [string]$LockPath = "",
    [string]$Repo = "wade004/ws-game",
    [string]$FromLocalDist = "",
    [switch]$AllowVersionMismatch,
    [switch]$FromRegistry,
    [string]$RegistryUrl = "",
    [string]$ManifestPath = "",
    [switch]$WithSamples
)

$ErrorActionPreference = "Stop"

# 判断记录（消费方反馈 E2 根治，2026-09-10）：此前 dot-source 同目录 toolchain/_hash.ps1 提供的
# Get-Sha256FileHash 共享函数——游戏仓库侧引用本脚本的典型方式是"只下载 get_framework.ps1 单个
# 文件"（本脚本自身尚未落地前无法先跑它去拉取 toolchain/ 整个目录），形成引导死锁，见文件头
# 判断记录。现原样内联函数体（与 toolchain/_hash.ps1 逐字节一致，
# toolchain/tests/test_get_framework_hash_inline_consistency.py 静态比对两处函数体），本脚本自身
# 不再依赖任何同目录文件。
# BEGIN INLINE Get-Sha256FileHash（与 toolchain/_hash.ps1 保持逐字节一致，见上方判断记录）
function Get-Sha256FileHash {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $cmd = Get-Command -Name "Get-FileHash" -ErrorAction SilentlyContinue
    if ($cmd) {
        try {
            return (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLower()
        } catch {
            # 命令存在但调用时实际抛错（例如内部依赖的模块/程序集加载失败）：不当场失败，落到下面
            # 不依赖该 cmdlet 的兜底路径——两条路径算法一致，产出应当相同。
            Write-Host "  [警告] Get-FileHash 调用失败（$($_.Exception.Message)），改用内置 SHA256 兜底计算" -ForegroundColor Yellow
        }
    }

    $stream = $null
    $sha256 = $null
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        $stream = [System.IO.File]::OpenRead($Path)
        $hashBytes = $sha256.ComputeHash($stream)
        return ([System.BitConverter]::ToString($hashBytes) -replace "-", "").ToLower()
    } finally {
        if ($stream) { $stream.Dispose() }
        if ($sha256) { $sha256.Dispose() }
    }
}
# END INLINE Get-Sha256FileHash

$VersionFormatPattern = '^\d+\.\d+\.\d+$'
if ($Version -notmatch $VersionFormatPattern) {
    Write-Host "-Version 格式非法：'$Version'（需形如 X.Y.Z）" -ForegroundColor Red
    exit 1
}

# 判断记录（P150-01 根治，2026-09-08，审计 architecture/落地计划/audit-3224ca1-20260908/
# AUDIT_REPORT.md PJ150-01）：锁文件里的 `version` 字段此前只在“等于 -Version”时才被信任；一旦
# 调用方传了 -AllowVersionMismatch 放行版本不一致，脚本会把锁文件 version 原样赋给
# $EffectiveVersion 并直接拼进落地目录路径（`<Target>/ws-game-<EffectiveVersion>/`），随后对该
# 路径执行 `Remove-Item -Recurse -Force`——锁文件是随 zip 一起搬运的数据文件，其 `version`
# 字段未经任何格式校验，构造成形如 `x/../../outside_sentinel` 的值即可让拼出的路径规范化后落到
# `-Target` 之外的任意兄弟目录，脚本会先删除那里已有的内容再落地框架文件（哈希校验只覆盖六个
# DLL 字节，不覆盖这个元数据字段，看不出异常）。根治两层：1）锁文件 version 与 `-Version` 参数
# 同样严格校验格式（允许可选的语义化版本预发布后缀，供本机验证/迁移场景使用非正式版本号归档，
# 但不允许出现路径分隔符/`..`/绝对路径等非版本号字符）；2）无论格式校验是否通过，落地目录规范化
# 后必须仍是 `-Target` 的严格子目录，任何写入/删除前都要经过这道边界检查——两层任一层单独失守
# 都不足以杜绝越界，必须同时具备。
$LockVersionFormatPattern = '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$'

# TOOL-118-LOCK 根治（codex 第十八轮，audit-d6fda65-20260911）：`headless_dlls`/`validator_dlls`
# 缺字段的"向后兼容跳过"此前对任何锁文件一视同仁——只要字段是 $null 就跳过对应校验、只打印提示，
# 不管这份锁文件的 version 字段实际是多少。这在锁文件由正常 `build.ps1` 生成时没问题（老版本
# 锁文件本来就没有这两个字段），但 `.github/workflows/release.yml`"缺附件修复"分支曾经手写过一份
# 不含这两个字段的锁文件（见该文件"Repair missing assets from existing zip"步骤判断记录）——对于
# `headless_dlls` 字段从 1.13.0 起、`validator_dlls` 字段从 1.15.0 起就应该随正常 `build.ps1` 一起
# 出现的版本，"缺字段"不再是"老版本正常没有"，而是"这份锁文件本身残缺/生成方式有问题"，继续跳过
# 校验会让 `Adapters.Stub.dll`/`Validator.dll` 被篡改也检测不出来（见
# validation/release-repair 下的复现记录）。用统一阈值 1.15.0（两个字段里更晚引入的那个，简化
# 判断口径，不逐字段各自维护一个阈值）：锁文件 version < 1.15.0 时任一字段缺失仍按老逻辑跳过并
# 提示；version >= 1.15.0 时任一字段缺失直接判定锁文件损坏、报错退出，不落地。
function ConvertTo-ComparableVersion {
    param([Parameter(Mandatory = $true)][string]$VersionText)
    # 只取形如 X.Y.Z 的核心部分参与比较，预发布后缀（-dryrun 等）不影响"是否达到某个基线版本"的
    # 判断——[version] 类型不接受预发布后缀，且发布流程里预发布版本本来就不会进正式 Release 锁文件。
    $core = ($VersionText -split '-', 2)[0]
    return [version]$core
}

function Test-LockVersionAtLeast {
    param(
        [Parameter(Mandatory = $true)][string]$VersionText,
        [Parameter(Mandatory = $true)][version]$Threshold
    )
    try {
        $v = ConvertTo-ComparableVersion -VersionText $VersionText
    } catch {
        # version 字段格式此前已经过 $LockVersionFormatPattern 校验（或走的是与 -Version 一致、
        # 未触发格式校验的默认路径，此时字段值就是 -Version 本身，格式同样受调用方约束），理论上
        # 不会解析失败；解析失败时保守地当作"达到阈值"处理（不放宽缺字段校验，宁可误报损坏也不
        # 漏判——落地前拒绝远比落地一份完整性覆盖不全的框架安全）。
        return $true
    }
    return $v -ge $Threshold
}

$HeadlessValidatorDllsRequiredSinceVersion = [version]"1.15.0"

# 判断记录（P150-01 根治，同上）：把“规范化落点是否为 -Target 的严格子目录”抽成一个可复用函数，
# 落地目录计算（zip 通道）与后续任何需要做同类边界校验的地方（例如 zip 内条目路径）共用同一份
# 判定逻辑，不重复实现、不因为遗漏某处校验而留下另一个越界口子。严格子目录：规范化后的绝对路径，
# 去掉结尾分隔符后再补一个分隔符作为前缀比较，故意不接受“等于根目录本身”（落地目录理应是根目录
# 下的一层子目录，不应该出现落地路径与 -Target 本身相同、进而在 Remove-Item 时把 -Target 自己删掉
# 的情况）。
function Test-IsStrictSubPath {
    param(
        [Parameter(Mandatory = $true)][string]$CandidatePath,
        [Parameter(Mandatory = $true)][string]$RootPath
    )
    $rootFull = [System.IO.Path]::GetFullPath($RootPath)
    $candidateFull = [System.IO.Path]::GetFullPath($CandidatePath)
    $rootWithSep = $rootFull.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    return $candidateFull.StartsWith($rootWithSep, [System.StringComparison]::OrdinalIgnoreCase)
}

# 判断记录（消费方反馈 E4 根治，2026-09-10）：把 P150-01 根治引入的"逐条目手动解压 + zip slip
# 边界校验"抽成一个可复用函数——此前只有主 zip 一处解压逻辑，内联写还看不出重复；E4 新增的
# samples zip（-WithSamples）需要同一套安全解压语义（同样不可信来源、同样不能无差别
# Expand-Archive），抽出来后两处调用同一份实现，不会出现"改了一处忘了改另一处"的分裂风险。
# 行为与原内联版本完全一致：$DestDir 不存在会被创建；每个条目（含目录条目）规范化后必须仍是
# $DestDir 的严格子目录，不满足直接 throw、不解压任何后续条目。
function Expand-ZipEntriesSafely {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$DestDir
    )
    New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $zipArchive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($zipEntry in $zipArchive.Entries) {
            # 判断记录：本仓库 `build.ps1` 打的 zip 里目录条目的 `FullName` 以 `\`（Windows 分隔符）
            # 结尾（例如 `ws-game-1.5.0\adapters\`），不是 zip 规范惯例的 `/`；.NET 的
            # `ZipArchiveEntry.Name` 只按 `/` 切分，遇到这类条目时 `Name` 不为空（等于整个
            # `FullName`），不能再用"`Name` 是否为空"判断是否为目录条目——改为直接看 `FullName`
            # 是否以 `/` 或 `\` 结尾，兼容两种分隔符约定的 zip 生产者。
            $isDirEntry = $zipEntry.FullName.EndsWith("/") -or $zipEntry.FullName.EndsWith("\")
            if ($isDirEntry) {
                $entryDirPath = Join-Path $DestDir $zipEntry.FullName
                if (-not (Test-IsStrictSubPath -CandidatePath $entryDirPath -RootPath $DestDir)) {
                    throw "zip 条目路径越界（zip slip），已拒绝解压：'$($zipEntry.FullName)'"
                }
                New-Item -ItemType Directory -Force -Path $entryDirPath | Out-Null
                continue
            }
            $entryDestPath = Join-Path $DestDir $zipEntry.FullName
            if (-not (Test-IsStrictSubPath -CandidatePath $entryDestPath -RootPath $DestDir)) {
                throw "zip 条目路径越界（zip slip），已拒绝解压：'$($zipEntry.FullName)'"
            }
            $entryDestDir = [System.IO.Path]::GetDirectoryName($entryDestPath)
            if (-not (Test-Path $entryDestDir)) {
                New-Item -ItemType Directory -Force -Path $entryDestDir | Out-Null
            }
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($zipEntry, $entryDestPath, $true)
        }
    } finally {
        $zipArchive.Dispose()
    }
}

if ($LockPath -eq "") {
    $LockPath = Join-Path (Get-Location).Path "ws-game.lock"
}

# -----------------------------------------------------------------------------
# 私服交付通道新增：-FromRegistry 是独立分支，做完就退出，不进入下面 zip 通道的任何逻辑
# （下载/解压/DLL 哈希校验对这条通道没有意义，见 .PARAMETER FromRegistry 说明）。
# -----------------------------------------------------------------------------
if ($FromRegistry) {
    function Write-RegistryStep {
        param([string]$Message)
        Write-Host ""
        Write-Host "==== $Message ====" -ForegroundColor Cyan
    }

    $ThreePackageNames = @(
        "com.gamefoundation.adapter.unity",
        "com.gamefoundation.framework-data",
        "com.gamefoundation.toolchain"
    )
    # ADR-0018 决策 3 新增：第四个私服包 com.gamefoundation.adapter.headless（无头适配层）不是
    # Unity 依赖（不供游戏工程的包解析器使用），因此不写入 Packages/manifest.json 的
    # dependencies——只在下面 ws-game.lock 的 source.optional_packages 里登记为"本次引用的框架
    # 版本额外提供、按需自取"的可选包，见 toolchain/registry/manifests/adapter-headless/README.md
    # 判断记录"为什么本包不写入 Packages/manifest.json"。
    $OptionalPackageNames = @(
        "com.gamefoundation.adapter.headless"
    )
    $RegistryScope = "com.gamefoundation"

    if ($RegistryUrl -eq "") {
        $siblingRegistryJson = Join-Path $PSScriptRoot "registry\registry.json"
        if (Test-Path $siblingRegistryJson) {
            $RegistryUrl = ((Get-Content -Path $siblingRegistryJson -Raw -Encoding UTF8) | ConvertFrom-Json).url
            Write-Host "未传 -RegistryUrl，取本机 $siblingRegistryJson 的 url：$RegistryUrl" -ForegroundColor Cyan
        } else {
            $RegistryUrl = "http://127.0.0.1:4873"
            Write-Host "未传 -RegistryUrl，且找不到 $siblingRegistryJson，退化为默认值：$RegistryUrl" -ForegroundColor Yellow
        }
    }
    $RegistryUrl = $RegistryUrl.TrimEnd('/')

    Write-RegistryStep "生成/更新 manifest.json 片段（作用域注册表 + 三个依赖，version=$Version）"

    $scopedRegistryEntry = [ordered]@{
        name   = "ws-game private registry"
        url    = $RegistryUrl
        scopes = @($RegistryScope)
    }

    if ($ManifestPath -eq "") {
        $defaultManifestPath = Join-Path (Get-Location).Path "Packages\manifest.json"
        if (Test-Path $defaultManifestPath) {
            $ManifestPath = $defaultManifestPath
            Write-Host "  未传 -ManifestPath，找到默认位置：$ManifestPath"
        }
    }

    if ($ManifestPath -ne "" -and (Test-Path $ManifestPath)) {
        $manifestRaw = Get-Content -Path $ManifestPath -Raw -Encoding UTF8
        $manifestObj = $manifestRaw | ConvertFrom-Json

        # scopedRegistries：按 url 去重覆盖（同一个私服地址只保留一条，避免重复调用本脚本时
        # manifest.json 里堆出多条一样的条目）；不存在则新增数组。
        $existingScoped = @()
        if ($manifestObj.PSObject.Properties.Name -contains "scopedRegistries") {
            $existingScoped = @($manifestObj.scopedRegistries | Where-Object { $_.url -ne $RegistryUrl })
        }
        $existingScoped += [PSCustomObject]$scopedRegistryEntry
        if ($manifestObj.PSObject.Properties.Name -contains "scopedRegistries") {
            $manifestObj.scopedRegistries = $existingScoped
        } else {
            $manifestObj | Add-Member -MemberType NoteProperty -Name "scopedRegistries" -Value $existingScoped
        }

        # dependencies：三个包依赖写入/覆盖为目标版本号，保留 manifest.json 里已有的其它依赖不变。
        if (-not ($manifestObj.PSObject.Properties.Name -contains "dependencies")) {
            $manifestObj | Add-Member -MemberType NoteProperty -Name "dependencies" -Value ([PSCustomObject]@{})
        }
        foreach ($pkgName in $ThreePackageNames) {
            if ($manifestObj.dependencies.PSObject.Properties.Name -contains $pkgName) {
                $manifestObj.dependencies.$pkgName = $Version
            } else {
                $manifestObj.dependencies | Add-Member -MemberType NoteProperty -Name $pkgName -Value $Version
            }
        }

        $manifestJson = ($manifestObj | ConvertTo-Json -Depth 10)
        [System.IO.File]::WriteAllText($ManifestPath, $manifestJson, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "  已写入：$ManifestPath" -ForegroundColor Green
    } else {
        Write-Host "  未找到可写入的 manifest.json（未传 -ManifestPath 且当前目录下没有 Packages\manifest.json），改为打印片段，请自行合并：" -ForegroundColor Yellow
        Write-Host ""
        Write-Host (@{
            scopedRegistries = @($scopedRegistryEntry)
            dependencies     = @{
                "com.gamefoundation.adapter.unity"   = $Version
                "com.gamefoundation.framework-data"  = $Version
                "com.gamefoundation.toolchain"       = $Version
            }
        } | ConvertTo-Json -Depth 10)
    }

    Write-RegistryStep "写入/校验 $LockPath"
    $registryLockObj = [ordered]@{
        version = $Version
        source  = [ordered]@{
            channel            = "registry"
            registry_url       = $RegistryUrl
            scope              = $RegistryScope
            packages           = $ThreePackageNames
            optional_packages  = $OptionalPackageNames
        }
    }
    $registryLockJson = ($registryLockObj | ConvertTo-Json -Depth 5)
    $utf8NoBomRegistry = New-Object System.Text.UTF8Encoding($false)
    if (Test-Path $LockPath) {
        $existingRegistryLockRaw = Get-Content -Path $LockPath -Raw -Encoding UTF8
        if ($existingRegistryLockRaw.Trim() -eq $registryLockJson.Trim()) {
            Write-Host "  $LockPath 内容已一致，无需改写"
        } else {
            Write-Host "  $LockPath 已存在，改写为 version=$Version，source.channel=registry" -ForegroundColor Yellow
            [System.IO.File]::WriteAllText($LockPath, $registryLockJson, $utf8NoBomRegistry)
            Write-Host "  已改写：$LockPath"
        }
    } else {
        [System.IO.File]::WriteAllText($LockPath, $registryLockJson, $utf8NoBomRegistry)
        Write-Host "  已新建：$LockPath"
    }

    # ADR-0018 决策 3 新增提示：可选包不写入 manifest.json 的 dependencies，如实告知调用方按需自取
    # （不是 Unity 依赖，见 $OptionalPackageNames 判断记录）。
    Write-Host ""
    Write-Host ("  可选包（不写入 manifest.json 的 dependencies，编辑器/无头宿主按需 npm install）：" + ($OptionalPackageNames -join ", ")) -ForegroundColor Cyan
    Write-Host ("    npm install " + $OptionalPackageNames[0] + "@" + $Version + " --registry " + $RegistryUrl)

    Write-Host ""
    Write-Host "get_framework.ps1 -FromRegistry 完成：version=$Version，registry=$RegistryUrl" -ForegroundColor Green
    exit 0
}

# 六个需要发布给 Unity 端的核心 DLL——与框架仓库 build.ps1 的 $CoreAssemblies 同一份清单，
# 只列 DLL 文件名（校验对象是锁文件里记录的 sha256，不需要 build.ps1 那边的源目录信息）。
$ExpectedDllNames = @(
    "Core.Foundation.dll",
    "Core.Numbers.dll",
    "Core.Rules.dll",
    "Core.Carriers.dll",
    "Core.Gameplay.dll",
    "Presentation.Common.dll"
)

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

# -----------------------------------------------------------------------------
# 1. 取得 zip + lock（在线用 gh release download，离线用 -FromLocalDist 指定的本地 zip 文件，
#    约定同目录下有同名 .lock 文件——与 build.ps1 -Release 的产物落位方式一致：
#    dist/ws-game-<ver>.zip 配 dist/ws-game-<ver>.lock）。
# -----------------------------------------------------------------------------
$WorkTempRoot = Join-Path $env:TEMP ("ws_game_get_framework_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $WorkTempRoot | Out-Null

$ZipPath = ""
$LockSourcePath = ""
# 消费方反馈 E4 根治新增：-WithSamples 时才会被填充，其余场景保持空字符串（下面所有使用处都先判
# 断 -WithSamples 再读取，空字符串不会被误当作一个真实路径使用）。
$SamplesZipPath = ""

try {
    if ($FromLocalDist -ne "") {
        Write-Step "离线来源：-FromLocalDist $FromLocalDist"
        if (-not (Test-Path $FromLocalDist)) {
            throw "找不到 -FromLocalDist 指定的文件：$FromLocalDist"
        }
        $fromItem = Get-Item $FromLocalDist
        if ($fromItem.PSIsContainer) {
            throw "-FromLocalDist 需要指向一个 .zip 文件，收到的是目录：$FromLocalDist"
        }
        if ($fromItem.Extension -ne ".zip") {
            throw "-FromLocalDist 需要指向一个 .zip 文件，收到：$FromLocalDist"
        }
        $ZipPath = $fromItem.FullName
        $LockSourcePath = [System.IO.Path]::ChangeExtension($ZipPath, ".lock")
        if (-not (Test-Path $LockSourcePath)) {
            throw "找不到与 zip 同目录、同名的锁文件：$LockSourcePath（build.ps1 -Release 打包时 zip/lock 成对生成，见其 .PARAMETER Release 说明）"
        }
        Write-Host "  zip: $ZipPath"
        Write-Host "  lock: $LockSourcePath"

        if ($WithSamples) {
            # 判断记录（消费方反馈 E4 根治）：samples zip 与主 zip 同目录、同版本号，文件名按
            # build.ps1 5.65 节约定加 "-samples" 后缀（"ws-game-<ver>.zip" 配
            # "ws-game-<ver>-samples.zip"）——与 .lock 兄弟文件同一套"同目录同版本号"约定，不引入
            # 第二套命名规则。
            $SamplesZipPath = $ZipPath -replace '\.zip$', '-samples.zip'
            if (-not (Test-Path $SamplesZipPath)) {
                throw "传了 -WithSamples，但找不到与 zip 同目录的 samples 包：$SamplesZipPath（build.ps1 -Dist/-Release 是否已升级到支持 E4 的版本？）"
            }
            Write-Host "  samples zip: $SamplesZipPath"
        }
    } else {
        Write-Step "在线拉取：gh release download v$Version --repo $Repo"
        $ghCmd = Get-Command gh -ErrorAction SilentlyContinue
        if (-not $ghCmd) {
            throw "找不到 gh（GitHub CLI），无法在线拉取；请先安装并登录 gh，或改用 -FromLocalDist 指定本地 zip 文件"
        }
        $downloadDir = Join-Path $WorkTempRoot "download"
        New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null

        $downloadPatterns = @(
            ("ws-game-" + $Version + ".zip"),
            ("ws-game-" + $Version + ".lock")
        )
        if ($WithSamples) {
            $downloadPatterns += ("ws-game-" + $Version + "-samples.zip")
        }
        $ghDownloadArgs = @("release", "download", "v$Version", "--repo", $Repo, "--dir", $downloadDir, "--clobber")
        foreach ($p in $downloadPatterns) { $ghDownloadArgs += @("--pattern", $p) }
        & gh @ghDownloadArgs
        if ($LASTEXITCODE -ne 0) {
            throw "gh release download 失败（退出码 $LASTEXITCODE），请确认版本号 v$Version 对应的 Release 已发布且 gh 已登录有权限访问 $Repo"
        }

        $ZipPath = Join-Path $downloadDir ("ws-game-" + $Version + ".zip")
        $LockSourcePath = Join-Path $downloadDir ("ws-game-" + $Version + ".lock")
        if (-not (Test-Path $ZipPath)) {
            throw "gh release download 完成但找不到 $ZipPath（Release 资产命名是否与约定一致？）"
        }
        if (-not (Test-Path $LockSourcePath)) {
            throw "gh release download 完成但找不到 $LockSourcePath（Release 资产命名是否与约定一致？）"
        }
        Write-Host "  已下载 zip: $ZipPath"
        Write-Host "  已下载 lock: $LockSourcePath"

        if ($WithSamples) {
            $SamplesZipPath = Join-Path $downloadDir ("ws-game-" + $Version + "-samples.zip")
            if (-not (Test-Path $SamplesZipPath)) {
                throw "传了 -WithSamples，但 gh release download 完成后找不到 $SamplesZipPath（该版本 Release 是否已升级到支持 E4 的版本？）"
            }
            Write-Host "  已下载 samples zip: $SamplesZipPath"
        }
    }

    # -----------------------------------------------------------------------------
    # 2. 读锁文件，做基本校验（版本号字段应与 -Version 一致）。判断记录（P04 根治，见
    #    .PARAMETER AllowVersionMismatch 说明）：默认严格模式，不一致直接报错退出、不落地、不
    #    删除/不覆盖任何已有目标目录；显式 -AllowVersionMismatch 才放行，且放行后落地目录名与
    #    引用示例一律改用锁文件记录的实际版本号（$EffectiveVersion），不再使用请求的 -Version。
    # -----------------------------------------------------------------------------
    Write-Step "读取锁文件并校验"
    $lockRaw = Get-Content -Path $LockSourcePath -Raw -Encoding UTF8
    $lockObj = $lockRaw | ConvertFrom-Json
    if ($null -eq $lockObj.dlls) {
        throw "锁文件 $LockSourcePath 缺少 dlls 字段（格式不是本工具认识的 ws-game.lock 结构）"
    }
    $EffectiveVersion = $Version
    if ($lockObj.version -ne $Version) {
        if (-not $AllowVersionMismatch) {
            throw ("锁文件 version=" + $lockObj.version + " 与请求的 -Version=" + $Version + " 不一致，默认严格模式下拒绝继续" +
                   "（未落地、未删除/覆盖任何已有目标目录）。若确认要用这份实际版本号不同的归档（例如本机验证/迁移场景），" +
                   "显式传 -AllowVersionMismatch 放行——放行后落地目录名与引用示例改用锁文件的实际版本号 " + $lockObj.version +
                   "，不使用请求的 -Version，避免目录名与实际内容身份不符。")
        }
        if ($lockObj.version -notmatch $LockVersionFormatPattern) {
            throw ("锁文件 " + $LockSourcePath + " 的 version 字段格式非法：'" + $lockObj.version +
                   "'（需形如 X.Y.Z 或 X.Y.Z-<预发布标识>，不允许路径分隔符/`..`/空白等字符）。" +
                   "该字段会被用于拼接落地目录名，格式校验失败前不会做任何写入/删除。")
        }
        $EffectiveVersion = $lockObj.version
        Write-Host ("  警告：锁文件 version=" + $lockObj.version + " 与请求的 -Version=" + $Version + " 不一致——已传 -AllowVersionMismatch，放行。") -ForegroundColor Yellow
        Write-Host ("  源（锁文件实际版本）=" + $lockObj.version + "；目标（本次请求版本）=" + $Version + "；落地目录名与引用示例将使用源版本号 " + $EffectiveVersion) -ForegroundColor Yellow
    }
    Write-Host "  锁文件 version=$($lockObj.version)，git_commit=$($lockObj.git_commit)"

    # -----------------------------------------------------------------------------
    # 3. 解压到临时目录，按锁文件校验六个核心 DLL 的 sha256；校验通过前不改动 -Target。
    #    zip 内顶层目录名固定形如 "ws-game-<ver>"（build.ps1 -Release 打包约定，DryRun 产物额外
    #    带 -dryrun 后缀），这里不假设具体名字，取解压结果下唯一的顶层目录即可，保证 DryRun/正式
    #    两种命名都能正确处理。
    # -----------------------------------------------------------------------------
    Write-Step "解压并校验六个核心 DLL 的 sha256"
    $extractTempDir = Join-Path $WorkTempRoot "extract"
    # 判断记录（P150-01 根治，zip slip 部分；消费方反馈 E4 根治时抽成 Expand-ZipEntriesSafely
    # 函数，见其定义处判断记录）：不再直接用 `Expand-Archive` 无差别解压——zip 条目自身的文件名
    # 可以携带 `../`（zip slip），一个被篡改或恶意构造的 zip 即使六个 DLL 哈希都对得上（哈希校验
    # 只挑六个固定名字的 DLL 比对，不校验其它条目），仍可能借助其它条目的路径把内容写到
    # $extractTempDir 之外。
    Expand-ZipEntriesSafely -ZipPath $ZipPath -DestDir $extractTempDir

    $topDirs = @(Get-ChildItem -Path $extractTempDir -Directory)
    if ($topDirs.Count -ne 1) {
        throw "zip 解压后顶层应恰好有 1 个目录，实际 $($topDirs.Count) 个（zip 内容是否符合 'ws-game-<ver>/...' 约定？）"
    }
    $innerRoot = $topDirs[0].FullName

    $pluginsCoreDir = Join-Path $innerRoot "adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core"
    if (-not (Test-Path $pluginsCoreDir)) {
        throw "解压产物里找不到 $pluginsCoreDir（zip 内容是否完整？）"
    }

    $hashMismatches = @()
    foreach ($dllName in $ExpectedDllNames) {
        $expectedSha = $lockObj.dlls.$dllName
        $dllPath = Join-Path $pluginsCoreDir $dllName
        if (-not (Test-Path $dllPath)) {
            $hashMismatches += "$dllName：解压产物中不存在（$dllPath）"
            continue
        }
        if ([string]::IsNullOrEmpty($expectedSha)) {
            $hashMismatches += "$dllName：锁文件未记录该 DLL 的 sha256"
            continue
        }
        $actualSha = Get-Sha256FileHash -Path $dllPath
        if ($actualSha -ne $expectedSha.ToLower()) {
            $hashMismatches += "$dllName：sha256 不一致（锁文件=$expectedSha，实际=$actualSha）"
        } else {
            Write-Host "  [通过] $dllName sha256=$actualSha"
        }
    }

    if ($hashMismatches.Count -gt 0) {
        throw ("DLL 哈希校验失败，未落地到 -Target（内容可能被篡改或下载不完整）：`n  " + ($hashMismatches -join "`n  "))
    }
    Write-Host "  六个核心 DLL 哈希全部与锁文件一致"

    # 判断记录（ADR-0018 决策 3，向后兼容；TOOL-118-LOCK 根治收紧了"向后兼容"的适用范围，见上面
    # `$HeadlessValidatorDllsRequiredSinceVersion` 判断记录）：锁文件里若存在 `headless_dlls`
    # 字段（build.ps1 新增，见其 5.6 节判断记录）则一并校验无头适配层 DLL 的哈希；老版本锁文件
    # （锁文件 version < 1.15.0）没有这个字段时，`$lockObj.headless_dlls` 是 $null，跳过这项校验
    # 并打印提示，不报错、不阻断——旧锁文件描述的那个版本本来就没有这份交付物，"缺字段"不代表
    # "内容被篡改"；但 version >= 1.15.0 仍缺字段，说明锁文件不是由正常 `build.ps1` 生成（例如
    # `release.yml` 缺附件修复分支手写的残缺版本），按损坏处理、拒绝落地。
    if ($null -ne $lockObj.headless_dlls) {
        $headlessDir = Join-Path $innerRoot "adapters\headless"
        $headlessHashMismatches = @()
        foreach ($headlessDllProp in $lockObj.headless_dlls.PSObject.Properties) {
            $headlessDllName = $headlessDllProp.Name
            $expectedHeadlessSha = $headlessDllProp.Value
            $headlessDllPath = Join-Path $headlessDir $headlessDllName
            if (-not (Test-Path $headlessDllPath)) {
                $headlessHashMismatches += "$headlessDllName：解压产物中不存在（$headlessDllPath）"
                continue
            }
            if ([string]::IsNullOrEmpty($expectedHeadlessSha)) {
                $headlessHashMismatches += "$headlessDllName：锁文件未记录该 DLL 的 sha256"
                continue
            }
            $actualHeadlessSha = Get-Sha256FileHash -Path $headlessDllPath
            if ($actualHeadlessSha -ne $expectedHeadlessSha.ToLower()) {
                $headlessHashMismatches += "$headlessDllName：sha256 不一致（锁文件=$expectedHeadlessSha，实际=$actualHeadlessSha）"
            } else {
                Write-Host "  [通过] $headlessDllName sha256=$actualHeadlessSha（无头适配层，ADR-0018 决策 3）"
            }
        }
        if ($headlessHashMismatches.Count -gt 0) {
            throw ("无头适配层 DLL 哈希校验失败，未落地到 -Target（内容可能被篡改或下载不完整）：`n  " + ($headlessHashMismatches -join "`n  "))
        }
        Write-Host "  无头适配层 DLL 哈希全部与锁文件一致"
    } elseif (Test-LockVersionAtLeast -VersionText $lockObj.version -Threshold $HeadlessValidatorDllsRequiredSinceVersion) {
        throw ("锁文件 " + $LockSourcePath + " 的 version=" + $lockObj.version + "（>= " +
               $HeadlessValidatorDllsRequiredSinceVersion.ToString() + "）缺少 headless_dlls 字段——该版本正常构建流程" +
               "（build.ps1）总会写出这个字段，缺失说明这份锁文件不是完整正常生成的（例如发布流程的" +
               "缺附件修复分支手写了残缺版本），按损坏处理，拒绝落地。")
    } else {
        Write-Host "  锁文件无 headless_dlls 字段（version=$($lockObj.version) 早于 $($HeadlessValidatorDllsRequiredSinceVersion.ToString())，早于无头适配层交付落地，或来自尚未升级的旧构建），跳过该项校验" -ForegroundColor Yellow
    }

    # 判断记录（消费方反馈 E1 根治，2026-09-10；TOOL-118-LOCK 根治收紧了"向后兼容"的适用范围，
    # 见上面 `$HeadlessValidatorDllsRequiredSinceVersion` 判断记录）：锁文件里若存在
    # `validator_dlls` 字段（build.ps1 新增，见其 5.057 节判断记录）则一并校验预编译
    # toolchain/validator/bin/Validator.dll 的哈希，与上面 headless_dlls 同一套模式（可选字段、
    # 缺字段不报错——但仅限锁文件 version < 1.15.0；>= 1.15.0 仍缺字段按损坏拒绝）。这份 DLL
    # 会被 toolchain/validate_data.py 直接 `dotnet <Validator.dll>` 执行，与六个核心 DLL/无头适配层
    # DLL 一样纳入完整性校验，保证"校验通过才落地"的语义同样覆盖这份可执行产物。
    if ($null -ne $lockObj.validator_dlls) {
        $validatorBinDir = Join-Path $innerRoot "toolchain\validator\bin"
        $validatorHashMismatches = @()
        foreach ($validatorDllProp in $lockObj.validator_dlls.PSObject.Properties) {
            $validatorDllName = $validatorDllProp.Name
            $expectedValidatorSha = $validatorDllProp.Value
            $validatorDllPath = Join-Path $validatorBinDir $validatorDllName
            if (-not (Test-Path $validatorDllPath)) {
                $validatorHashMismatches += "$validatorDllName：解压产物中不存在（$validatorDllPath）"
                continue
            }
            if ([string]::IsNullOrEmpty($expectedValidatorSha)) {
                $validatorHashMismatches += "$validatorDllName：锁文件未记录该 DLL 的 sha256"
                continue
            }
            $actualValidatorSha = Get-Sha256FileHash -Path $validatorDllPath
            if ($actualValidatorSha -ne $expectedValidatorSha.ToLower()) {
                $validatorHashMismatches += "$validatorDllName：sha256 不一致（锁文件=$expectedValidatorSha，实际=$actualValidatorSha）"
            } else {
                Write-Host "  [通过] $validatorDllName sha256=$actualValidatorSha（预编译 validator，消费方反馈 E1）"
            }
        }
        if ($validatorHashMismatches.Count -gt 0) {
            throw ("预编译 validator DLL 哈希校验失败，未落地到 -Target（内容可能被篡改或下载不完整）：`n  " + ($validatorHashMismatches -join "`n  "))
        }
        Write-Host "  预编译 validator DLL 哈希全部与锁文件一致"
    } elseif (Test-LockVersionAtLeast -VersionText $lockObj.version -Threshold $HeadlessValidatorDllsRequiredSinceVersion) {
        throw ("锁文件 " + $LockSourcePath + " 的 version=" + $lockObj.version + "（>= " +
               $HeadlessValidatorDllsRequiredSinceVersion.ToString() + "）缺少 validator_dlls 字段——该版本正常构建流程" +
               "（build.ps1）总会写出这个字段，缺失说明这份锁文件不是完整正常生成的（例如发布流程的" +
               "缺附件修复分支手写了残缺版本），按损坏处理，拒绝落地。")
    } else {
        Write-Host "  锁文件无 validator_dlls 字段（version=$($lockObj.version) 早于 $($HeadlessValidatorDllsRequiredSinceVersion.ToString())，早于预编译 validator 交付落地，或来自尚未升级的旧构建），跳过该项校验" -ForegroundColor Yellow
    }

    # -----------------------------------------------------------------------------
    # 3.5 消费方反馈 E4 根治：-WithSamples 时按锁文件 samples.sha256 校验整份 samples zip 的完整性，
    #     通过后解压合并进 $innerRoot（与主 zip 解压结果同一棵目录树），随后统一走下面第 4 步的
    #     "校验通过才落地"语义——samples 内容一旦校验失败，同主 zip 一样不落地任何内容到 -Target。
    # -----------------------------------------------------------------------------
    if ($WithSamples) {
        Write-Step "校验并合并 samples zip（消费方反馈 E4 根治）"
        if ($null -eq $lockObj.samples -or [string]::IsNullOrEmpty($lockObj.samples.sha256)) {
            throw "传了 -WithSamples，但锁文件 $LockSourcePath 没有 samples.sha256 字段（该版本早于消费方反馈 E4 落地，或来自尚未升级的旧构建），无法校验；请改用不带 -WithSamples 的调用，或升级到更新的框架版本"
        }
        $expectedSamplesSha = $lockObj.samples.sha256
        $actualSamplesSha = Get-Sha256FileHash -Path $SamplesZipPath
        if ($actualSamplesSha -ne $expectedSamplesSha.ToLower()) {
            throw ("samples zip 哈希校验失败，未落地到 -Target（内容可能被篡改或下载不完整）：锁文件=" + $expectedSamplesSha + "，实际=" + $actualSamplesSha)
        }
        Write-Host "  [通过] samples zip sha256=$actualSamplesSha"

        # 解压到独立临时目录（同样走 zip slip 安全解压），再把 data/_sample、assets/_sample 两棵
        # 目录树合并进 $innerRoot——samples zip 内顶层目录名与主 zip 同一约定（"ws-game-<ver>/"），
        # 取其解压结果下唯一的顶层目录，与主 zip 处理方式一致（见上方"3. 解压..."判断记录）。
        $samplesExtractTempDir = Join-Path $WorkTempRoot "extract_samples"
        Expand-ZipEntriesSafely -ZipPath $SamplesZipPath -DestDir $samplesExtractTempDir
        $samplesTopDirs = @(Get-ChildItem -Path $samplesExtractTempDir -Directory)
        if ($samplesTopDirs.Count -ne 1) {
            throw "samples zip 解压后顶层应恰好有 1 个目录，实际 $($samplesTopDirs.Count) 个（zip 内容是否符合 'ws-game-<ver>/...' 约定？）"
        }
        $samplesInnerRoot = $samplesTopDirs[0].FullName

        # 判断记录：不能像 zip slip 校验那样只看"顶层条目"是否同名——samples zip 顶层是 data/、
        # assets/ 两个目录，主 zip 解压结果（$innerRoot）本身也有同名的 data/、assets/ 顶层目录
        # （分别装 data/_framework、assets/_placeholder 等，见 build.ps1 打分发包一节），顶层目录名
        # 本来就会重叠，这是预期之内的（要合并到同一棵 data/、assets/ 目录树下），不代表冲突。真正
        # 需要检测的冲突粒度是"文件"：逐个文件按相对路径合并，只有当同一相对路径下主 zip 那边已经
        # 存在同名文件时才是真正的冲突（预期不会发生——主 zip 不含 data/_sample、assets/_sample，
        # 见 build.ps1 5.65 节判断记录），必要的中间目录按需创建。
        $samplesFiles = @(Get-ChildItem -Path $samplesInnerRoot -Recurse -File -Force)
        foreach ($samplesFile in $samplesFiles) {
            $relativePath = $samplesFile.FullName.Substring($samplesInnerRoot.Length).TrimStart('\', '/')
            $mergeDestFile = Join-Path $innerRoot $relativePath
            if (Test-Path -LiteralPath $mergeDestFile -PathType Leaf) {
                throw "samples zip 内文件 '$relativePath' 与主 zip 解压结果同名，无法合并（预期主 zip 不包含 data/_sample、assets/_sample，见 build.ps1 判断记录）"
            }
            $mergeDestDir = Split-Path -Parent $mergeDestFile
            if (-not (Test-Path -LiteralPath $mergeDestDir)) {
                New-Item -ItemType Directory -Force -Path $mergeDestDir | Out-Null
            }
            Move-Item -LiteralPath $samplesFile.FullName -Destination $mergeDestFile -Force
        }
        Write-Host "  已合并 data/_sample、assets/_sample 到解压结果"
    }

    # -----------------------------------------------------------------------------
    # 4. 校验通过：落地到 <Target>/ws-game-<Version>/（已存在则整体删除重建，视为一次全新拉取）。
    # -----------------------------------------------------------------------------
    Write-Step "落地到 $Target/ws-game-$EffectiveVersion/"
    $targetRootFull = $Target
    if (-not [System.IO.Path]::IsPathRooted($targetRootFull)) {
        $targetRootFull = Join-Path (Get-Location).Path $Target
    }
    if (-not (Test-Path $targetRootFull)) {
        New-Item -ItemType Directory -Force -Path $targetRootFull | Out-Null
    }
    # 判断记录（P04 根治）：落地目录名固定用 $EffectiveVersion（未触发 -AllowVersionMismatch 时
    # 等于 $Version，二者相同；触发且放行后等于锁文件实际版本号），保证目录名与实际落地内容的
    # 身份始终一致，不会出现"请求版本号命名的目录，装着另一个版本的真实内容"。
    $extractDir = Join-Path $targetRootFull ("ws-game-" + $EffectiveVersion)

    # 判断记录（P150-01 根治，同上）：无论 $EffectiveVersion 的格式校验是否已经拦下了非法字符，
    # 落地前再做一次独立的路径边界校验——两层防御互不依赖，任一层单独失效时另一层仍能拦截。这里
    # 用规范化后的绝对路径比较，而不是只看字符串是否包含 `..`（`GetFullPath` 会把 `a/../../b`
    # 这类构造实际解析成的越界路径原形毕露，字符串黑名单容易漏判）。校验失败直接 throw，不做任何
    # `Test-Path`/`Remove-Item`/`New-Item`。
    if (-not (Test-IsStrictSubPath -CandidatePath $extractDir -RootPath $targetRootFull)) {
        throw ("拒绝落地：规范化后的目标路径 '" + [System.IO.Path]::GetFullPath($extractDir) +
               "' 不是 -Target '" + [System.IO.Path]::GetFullPath($targetRootFull) +
               "' 的严格子目录（可能是锁文件 version 字段包含路径穿越片段）。未对该路径或 -Target 之外的任何目录执行写入/删除。")
    }
    if (Test-Path $extractDir) {
        Write-Host "  目标目录已存在，整体删除重建：$extractDir" -ForegroundColor Yellow
        Remove-Item -Path $extractDir -Recurse -Force -Confirm:$false
    }
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
    Get-ChildItem -Path $innerRoot -Force | ForEach-Object {
        Move-Item -Path $_.FullName -Destination $extractDir -Force
    }
    Write-Host "  已落地：$extractDir"
    Write-Host "  引用示例（游戏工程 Packages/manifest.json，相对路径按实际目录层级调整）："
    Write-Host "    `"com.gamefoundation.adapter.unity`": `"file:.../$Target/ws-game-$EffectiveVersion/adapters/unity/Packages/com.gamefoundation.adapter.unity`""

    # -----------------------------------------------------------------------------
    # 5. 写入/校验游戏仓库根的 ws-game.lock：内容与本次下载的锁文件一致则跳过（已是最新）；
    #    不存在则新建；存在但内容不同则覆盖并打印旧/新版本号对比，方便看出这是一次升级/降级。
    #    私服交付通道新增：额外记一个 source 字段（channel=zip + 具体来源），与 -FromRegistry
    #    通道写的锁文件用同一个 source.channel 字段区分，见该分支说明。
    #
    #    判断记录（消费方反馈 E3 根治，2026-09-10，见
    #    architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E3）：`-FromLocalDist` 场景此前把
    #    调用方本机的绝对路径原样写进 `source.local_path`——`ws-game.lock` 是提交进游戏仓库的文件
    #    （见 .PARAMETER LockPath"约定放在游戏仓库根"），消费方一旦提交这份锁文件、换一台机器（不同
    #    本地路径，甚至同一台机器换了个盘符/目录名）重新运行本脚本，`source.local_path` 就会改变，
    #    产生一次与"框架引用内容"完全无关、纯粹因本机路径不同而触发的锁文件 diff，污染提交历史。
    #    根治两层：a) `source` 不再写绝对路径——`channel` 字段保持不变（`editor/docs/
    #    编辑器产品文档.md` 已将 `source.channel` 记为编辑器探测框架来源通道的契约字段，第 3.3/7.2
    #    节按 `channel: "zip"`/`"registry"` 分支处理，不能改名/删除），只把 `local_path` 换成与机器
    #    无关的相对信息 `zip_file_name`（文件名，不含目录）；b) 判定"锁文件是否需要改写"时不再整份
    #    JSON 字符串比较，改为只比较
    #    `version`/`git_commit`/`dlls`/`headless_dlls`/`validator_dlls`/`samples` 这几个描述"框架
    #    引用内容本身"的字段（见下方 Test-LockContentEquivalent 函数），`source` 字段的任何差异
    #    （含新旧字段名不同、旧锁文件带 `local_path` 这种历史格式）都不再触发改写判定——两台机器用
    #    不同本地路径对同一份 zip 跑本脚本，生成的锁文件在"是否需要改写"这件事上表现一致；带旧格式
    #    `source.local_path` 的既有锁文件仍能被正常读取/接受（`ConvertFrom-Json` 对多余字段不报错，
    #    只是不再参与比较）。回归测试见
    #    toolchain/tests/test_get_framework_lock_source_no_local_path.py。
    # -----------------------------------------------------------------------------
    Write-Step "写入/校验 $LockPath"
    if ($FromLocalDist -ne "") {
        $lockSourceInfo = [ordered]@{ channel = "zip"; zip_file_name = (Split-Path -Path $ZipPath -Leaf) }
    } else {
        $lockSourceInfo = [ordered]@{ channel = "zip"; repo = $Repo }
    }
    $lockObj | Add-Member -MemberType NoteProperty -Name "source" -Value ([PSCustomObject]$lockSourceInfo) -Force
    $newLockJson = ($lockObj | ConvertTo-Json -Depth 5)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

    # 判断记录（消费方反馈 E3 根治，同上）：只比较描述"框架引用内容本身"的字段，忽略 source
    # （来源信息，天然随调用环境/本机路径变化，不代表引用的框架内容发生了变化）与两侧 JSON 序列化
    # 本身的格式差异（属性顺序、空白）。任一侧缺失某字段时按 $null 处理（等价于"未记录"），新旧
    # 锁文件在 headless_dlls/validator_dlls/samples 这类后添加的可选字段上不对齐时也不会被误判为
    # "内容不同"——只要双方都没有该字段就仍视为一致；一方有一方没有，才是真正的内容差异。
    function Test-LockContentEquivalent {
        param(
            [Parameter(Mandatory = $true)]$ExistingLockObj,
            [Parameter(Mandatory = $true)]$NewLockObj
        )
        $fieldsToCompare = @("version", "git_commit", "dlls", "headless_dlls", "validator_dlls", "samples")
        foreach ($fieldName in $fieldsToCompare) {
            $existingVal = $ExistingLockObj.$fieldName
            $newVal = $NewLockObj.$fieldName
            $existingJson = "null"
            if ($null -ne $existingVal) { $existingJson = ($existingVal | ConvertTo-Json -Depth 6 -Compress) }
            $newJson = "null"
            if ($null -ne $newVal) { $newJson = ($newVal | ConvertTo-Json -Depth 6 -Compress) }
            if ($existingJson -ne $newJson) { return $false }
        }
        return $true
    }

    if (Test-Path $LockPath) {
        $existingLockRaw = Get-Content -Path $LockPath -Raw -Encoding UTF8
        $existingLockObj = $null
        try { $existingLockObj = $existingLockRaw | ConvertFrom-Json } catch {}
        $existingVersion = "(无法解析)"
        if ($null -ne $existingLockObj) { $existingVersion = $existingLockObj.version }

        if (($null -ne $existingLockObj) -and (Test-LockContentEquivalent -ExistingLockObj $existingLockObj -NewLockObj $lockObj)) {
            Write-Host "  $LockPath 内容已与本次下载一致（version/git_commit/dlls 等引用内容字段均相同，忽略 source 差异），无需改写"
        } else {
            Write-Host "  $LockPath 已存在（version=$existingVersion），改写为 version=$($lockObj.version)" -ForegroundColor Yellow
            [System.IO.File]::WriteAllText($LockPath, $newLockJson, $utf8NoBom)
            Write-Host "  已改写：$LockPath"
        }
    } else {
        [System.IO.File]::WriteAllText($LockPath, $newLockJson, $utf8NoBom)
        Write-Host "  已新建：$LockPath"
    }
} finally {
    Remove-Item -Path $WorkTempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "get_framework.ps1 完成：version=$EffectiveVersion 已校验并落地到 $Target/ws-game-$EffectiveVersion/" -ForegroundColor Green
exit 0
