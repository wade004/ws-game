<#
.SYNOPSIS
    共享的"构造/写出 dist/ws-game-<ver>.lock"函数（TOOL-118-LOCK 根治，codex 第十八轮，
    audit-d6fda65-20260911）：此前 `build.ps1`"打 zip + lock"步骤与
    `.github/workflows/release.yml`"缺附件修复"分支各自手写了一份锁文件对象构造逻辑——前者
    （正常路径）从新构建产物直接算哈希，字段齐全（`dlls`/`headless_dlls`/`validator_dlls`/
    `samples`）；后者（repair 路径，zip 已存在但缺 lock 时从已验证 zip 里抽取字节重算哈希）手写
    时漏掉了 `headless_dlls`/`validator_dlls` 两个字段，产出的锁文件让
    `toolchain/get_framework.ps1` 的"缺字段=旧版本，跳过校验"向后兼容分支被误触发，`Adapters.Stub.dll`/
    `Validator.dll` 被篡改也检测不出来（见 validation/release-repair 下的复现记录、
    AUDIT_REPORT.md TOOL-118-LOCK）。

    抽成本文件、两处调用点都改为 dot-source 后调用同一个函数（与同目录 `_hash.ps1`/
    `_version_writeback.ps1` 同一模式），从结构上杜绝"两份手写逻辑再次漂移"：`New-WsGameLockObject`
    的三个 DLL 哈希映射参数都是 Mandatory，调用方必须显式传全，没有"忘记传就悄悄留空"的空子；字段
    顺序、`ConvertTo-Json` 深度、`samples` 字段的"有值才写、无值整体省略该键"语义只在这一处实现。

    另提供 `New-WsGameLockObjectFromZip`——"从一份已验证的发布 zip 里抽取六个核心 DLL +
    Adapters.Stub.dll + Validator.dll 的字节重算哈希、解析 MANIFEST.txt 拿 git_commit、拼出完整
    锁对象"这一整套逻辑本身也只在这一处实现（任务书原话："从 zip 生成 lock"的逻辑抽到 toolchain/
    下一个共用脚本）。`release.yml`"缺附件修复"分支（zip 已存在但 lock 缺失时，不重新构建、直接
    从已验证 zip 重建 lock）调用这个函数；`build.ps1` 正常路径手上已经有新构建产物、不需要"从 zip
    读"，只调用下面的 `New-WsGameLockObject`（两个函数内部字段构造走的是同一段代码，见
    `New-WsGameLockObjectFromZip` 实现里对 `New-WsGameLockObject` 的调用）。

    本文件不做门禁判断（版本阈值等策略留给 `get_framework.ps1`）、不依赖调用方的其余全局状态（只
    接收显式参数），可在任意临时目录/任意一份 zip 上独立调用，便于 `toolchain/tests` 直接对真实
    `dist/ws-game-<ver>.zip` 或临时构造的 fixture zip 做回归断言（例如"用本机正式 zip 重新生成的
    lock 应与仓库里已发布的 lock 逐字段一致"）。

.EXAMPLE
    . (Join-Path $PSScriptRoot "_lock_writeback.ps1")
    # build.ps1 正常路径：哈希来自新构建产物。
    $lockObj = New-WsGameLockObject `
        -Version "1.18.0" -GitCommit "d6fda65..." `
        -CoreDlls $coreAssemblyShaMap -HeadlessDlls $headlessAssemblyShaMap -ValidatorDlls $validatorAssemblyShaMap `
        -SamplesSha256 $samplesZipSha
    Write-WsGameLockFile -Path "dist\ws-game-1.18.0.lock" -LockObject $lockObj

    # release.yml repair 分支：哈希来自已验证 zip 内的条目字节。
    $lockObj2 = New-WsGameLockObjectFromZip -ZipPath "dist\ws-game-1.18.0.zip" -Version "1.18.0" -SamplesSha256 $samplesSha
    Write-WsGameLockFile -Path "dist\ws-game-1.18.0.lock" -LockObject $lockObj2
#>

# 判断记录：三个 DLL 哈希映射参数（`CoreDlls`/`HeadlessDlls`/`ValidatorDlls`）均 Mandatory，不给
# 默认值、不允许 $null——这是本次根治的核心约束。旧的两处手写逻辑之所以会漂移，根源就是"忘记传
# 某个字段"在弱类型的 `[ordered]@{}` 手工拼接里不会有任何提示，只有运行时下游 `get_framework.ps1`
# 拿到残缺锁文件才会暴露。改成显式具名参数后，任何调用点漏传都会在调用当场（PowerShell 参数绑定
# 阶段）直接报错，不会产出一份残缺锁文件。`SamplesSha256` 允许省略/传 $null 或空字符串——
# `samples` 字段确实存在"当前这次调用算不出该值就整体不写这个键"的合法场景（见 build.ps1
# 5.6 节与 release.yml repair 分支两处判断记录：例如 repair 分支既取不到刚重建的 samples 哈希、
# 也下载不到已发布的 samples 附件时），因此单独放宽为可选参数，语义与旧代码一致（有值才加
# `samples` 键，无值整个键都不出现，不是"键存在但值为 null"）。
function New-WsGameLockObject {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$GitCommit,
        [Parameter(Mandatory = $true)]$CoreDlls,
        [Parameter(Mandatory = $true)]$HeadlessDlls,
        [Parameter(Mandatory = $true)]$ValidatorDlls,
        [AllowNull()][AllowEmptyString()][string]$SamplesSha256 = $null
    )
    $lockObj = [ordered]@{
        version        = $Version
        git_commit     = $GitCommit
        dlls           = $CoreDlls
        headless_dlls  = $HeadlessDlls
        validator_dlls = $ValidatorDlls
    }
    if (-not [string]::IsNullOrEmpty($SamplesSha256)) {
        $lockObj["samples"] = [ordered]@{ sha256 = $SamplesSha256 }
    }
    return $lockObj
}

# 判断记录：从一个已打开的 ZipArchive 里按条目全路径抽取单个文件到临时路径、算 sha256、删除
# 临时文件——`New-WsGameLockObjectFromZip` 的六个核心 DLL + headless + validator 共八次调用全部
# 复用这一个函数，不重复实现同一段"抽取到临时文件再哈希"的逻辑。找不到条目时直接 throw（调用方
# 不需要，也不应该，在"zip 里缺应有条目"这种情况下继续往下走产出一份看似正常的锁文件）。
#
# 判断记录（路径分隔符归一化）：实测 `Compress-Archive`（build.ps1 打包真实用的 cmdlet）在
# Windows 下产出的 zip，`ZipArchiveEntry.FullName` 报告的是反斜杠（尽管 zip 格式标准与大多数
# 跨平台工具——包括本文件配套 pytest 用 Python `zipfile` 构造的测试 fixture——用正斜杠）；.NET 的
# `ZipArchiveEntry.FullName` 不做归一化，原样透传central directory里存的分隔符字符，不随读取时的
# 操作系统变化。调用方传入的 `$EntryPath` 目前固定用反斜杠拼接（贴近真实产物），比较前把两侧都
# 换成正斜杠，即可同时兼容两种产物来源，不需要调用方关心具体是哪种 zip 实现写出来的。
function Get-WsGameZipEntrySha256 {
    param(
        [Parameter(Mandatory = $true)]$ZipReader,
        [Parameter(Mandatory = $true)][string]$EntryPath
    )
    $normalizedTarget = $EntryPath.Replace('\', '/')
    $entry = $ZipReader.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $normalizedTarget }
    if (-not $entry) {
        throw "zip 内找不到条目：$EntryPath"
    }
    $tmpFile = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $tmpFile, $true)
        return (Get-Sha256FileHash -Path $tmpFile)
    } finally {
        Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue
    }
}

# 判断记录（TOOL-118-LOCK 根治核心）："从一份已验证 zip 生成完整锁对象"这套逻辑（六个核心 DLL +
# Adapters.Stub.dll + Validator.dll 的字节哈希、MANIFEST.txt 里的 git_commit）此前只存在于
# release.yml"缺附件修复"分支的内联 PowerShell 里，且当时只抽取了六个核心 DLL——这是
# headless_dlls/validator_dlls 两个字段缺失的根源。现在整套逻辑收进这一个函数，release.yml 与
# 任何需要"给定一份 zip，重建它的锁文件"能力的调用方（含 toolchain/tests 的直接回归断言）都只有
# 这一份实现可用，不存在"重写一遍时漏掉某个 DLL"的空间。依赖 `Get-Sha256FileHash`（本文件不自带，
# 由调用方在 dot-source 本文件之前先 dot-source `_hash.ps1`，与 build.ps1 顶部两行 dot-source 的
# 既有顺序一致；`release.yml` 的 `run:` 步骤同样先 dot-source `_hash.ps1` 再 dot-source 本文件）。
function New-WsGameLockObjectFromZip {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$Version,
        [AllowNull()][AllowEmptyString()][string]$SamplesSha256 = $null
    )
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipTopLevelName = "ws-game-$Version"
    $coreAssemblies = @("Core.Foundation", "Core.Numbers", "Core.Rules", "Core.Carriers", "Core.Gameplay", "Presentation.Common")
    $zr = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $coreShaMap = [ordered]@{}
        foreach ($asm in $coreAssemblies) {
            $entryPath = "$zipTopLevelName\adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core\$asm.dll"
            $coreShaMap[$asm + ".dll"] = Get-WsGameZipEntrySha256 -ZipReader $zr -EntryPath $entryPath
        }
        $headlessShaMap = [ordered]@{
            "Adapters.Stub.dll" = Get-WsGameZipEntrySha256 -ZipReader $zr -EntryPath "$zipTopLevelName\adapters\headless\Adapters.Stub.dll"
        }
        $validatorShaMap = [ordered]@{
            "Validator.dll" = Get-WsGameZipEntrySha256 -ZipReader $zr -EntryPath "$zipTopLevelName\toolchain\validator\bin\Validator.dll"
        }
        $manifestEntryTarget = "$zipTopLevelName/MANIFEST.txt"
        $manifestEntry = $zr.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $manifestEntryTarget }
        if (-not $manifestEntry) {
            throw "zip 内找不到条目：$zipTopLevelName\MANIFEST.txt"
        }
        $manifestReader = New-Object System.IO.StreamReader($manifestEntry.Open())
        $manifestText = $manifestReader.ReadToEnd()
        $manifestReader.Close()
        $gitCommitMatch = [regex]::Match($manifestText, 'git_commit:\s*(\S+)')
        $gitCommit = $gitCommitMatch.Groups[1].Value
    } finally {
        $zr.Dispose()
    }
    return New-WsGameLockObject `
        -Version $Version -GitCommit $gitCommit `
        -CoreDlls $coreShaMap -HeadlessDlls $headlessShaMap -ValidatorDlls $validatorShaMap `
        -SamplesSha256 $SamplesSha256
}

# 判断记录：`ConvertTo-Json -Depth 5` 与 `[System.IO.File]::WriteAllText` + 无 BOM 的
# `UTF8Encoding($false)` 均照搬自 build.ps1 此前内联的写出逻辑——PowerShell 5.1 的
# `ConvertTo-Json`/`Out-File` 默认行为会带 BOM 或 `\r\n` 行尾，锁文件是纯 JSON 数据文件（不在
# `.gitattributes` eol=lf 的源码文本范畴，但仍应保持跨平台可读、不携带无意义的 BOM 字节），显式
# 走 `WriteAllText` 是两处调用点此前各自遵守、现在合并到一处强制遵守的既有约定。
function Write-WsGameLockFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$LockObject
    )
    $lockJson = ($LockObject | ConvertTo-Json -Depth 5)
    [System.IO.File]::WriteAllText($Path, $lockJson, (New-Object System.Text.UTF8Encoding($false)))
}
