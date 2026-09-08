<#
.SYNOPSIS
    共享的 SHA256 文件哈希计算函数（第九轮审计工具链条目根治，见
    architecture/落地计划/audit-85f1f4f-20260908/）。判断记录：本机 Windows PowerShell 5.1
    环境下曾观察到内置 `Get-FileHash` cmdlet 不可用（原因未查明，怀疑与 PSModulePath 相关，
    `pwsh`（PowerShell 7）下同一台机器可正常调用），`toolchain/get_framework.ps1`（游戏侧拉取
    发布产物后校验六个核心 DLL 哈希）、`toolchain/sync_package_content.ps1`（私服通道同步内容前
    比较源/目标文件是否一致）、`build.ps1`（内容树同步 + 独立版构建产物哈希核对）三处都直接依赖
    该 cmdlet，任一处不可用都会让对应步骤整体失败。根治：提供一个不依赖 `Get-FileHash` 的兜底
    实现（直接用 .NET `System.Security.Cryptography.SHA256` 流式计算，该类型是 BCL 一部分，不
    依赖任何模块加载），`Get-FileHash` 可用时优先使用（两者算法一致，产出应当相同），不可用或调用
    过程中抛错时透明退化到兜底路径，调用方不需要关心当前宿主是否具备该 cmdlet。

    分发说明：本文件放在 `toolchain/` 目录下，随 `build.ps1 -Dist` 整体拷贝 `toolchain/` 目录进
    `dist/<ver>/toolchain/`（进而随 `com.gamefoundation.toolchain` 包 `Tools~/` 一并分发到消费
    游戏仓库，见 `build.ps1` "组装三个可发布包"一节），与同目录下 `get_framework.ps1`/
    `sync_package_content.ps1` 总是一起travel，dot-source 用 `$PSScriptRoot` 相对定位即可，不
    存在"脚本单独被复制、找不到本文件"的场景。

.EXAMPLE
    . (Join-Path $PSScriptRoot "_hash.ps1")
    $sha = Get-Sha256FileHash -Path "C:\some\file.dll"
#>

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
