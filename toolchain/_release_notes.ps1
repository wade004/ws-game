<#
.SYNOPSIS
    发布说明文本的构造函数（ADR-0127）：首行写版本标签 `<新版本>_release`，空一行后接 CHANGELOG.md
    `## [X.Y.Z]` 条目正文。

    抽成独立可 dot-source 的函数文件（与同目录 _version_writeback.ps1 同一模式），供 build.ps1 -Release
    第 6 步写 dist/release-notes-<ver>.txt 使用，也供 toolchain/tests/test_version_label.py 直接调用做回归断言，
    不需要跑一遍完整 -Release 流程。

.EXAMPLE
    . (Join-Path $PSScriptRoot "_release_notes.ps1")
    New-ReleaseNotesText -Version "1.93.0" -ChangelogSection "## [1.93.0] - 2026-10-02`n..."
#>

# 判断记录：首行用 `<新版本>_release` 是用户对"合并到 main 后的版本号格式"的规定；它只出现在发布说明（给人看的文本）里，
# 不进 VERSION、包版本、发布标签与发布包名（这些必须是纯语义化版本，见 ADR-0127）。
function New-ReleaseNotesText {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$ChangelogSection
    )
    return ($Version + "_release`n`n" + $ChangelogSection)
}
