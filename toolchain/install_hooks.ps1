<#
.SYNOPSIS
    安装/卸载仓库自带的版本化 git 钩子目录 .githooks/（工程收尾 K 新增，见仓库根 README.md
    「提交前钩子」一节）。

.DESCRIPTION
    只做一件事：把 `git config core.hooksPath` 指向仓库根的 `.githooks/`（相对路径，随仓库
    一起移动不失效）。git 的 core.hooksPath 默认未设置，此时 git 只认 `.git/hooks/` 下的钩子
    （不受版本控制、不随克隆分发）；设置后 git 改为只认 core.hooksPath 指向的目录，`.git/hooks/`
    下任何脚本都不再生效。

.PARAMETER Uninstall
    还原为默认值（`git config --unset core.hooksPath`），之后 git 钩子重新只认 `.git/hooks/`。

.NOTES
    幂等：重复安装/重复卸载都不报错（`git config core.hooksPath` 本身允许覆盖写；`--unset`
    在配置项本就不存在时 git 会以非零退出码报告，本脚本对此单独判断、不当作失败处理）。
    PowerShell 5.1 兼容：不使用 &&、??、三元运算符。
#>
param(
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $RepoRoot
try {
    if ($Uninstall) {
        $current = & git config --get core.hooksPath 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrEmpty($current)) {
            Write-Host "core.hooksPath 本来就未设置，无需卸载。" -ForegroundColor Yellow
            exit 0
        }
        & git config --unset core.hooksPath
        if ($LASTEXITCODE -ne 0) {
            Write-Host "git config --unset core.hooksPath 失败，退出码 $LASTEXITCODE" -ForegroundColor Red
            exit 1
        }
        Write-Host "已卸载：core.hooksPath 还原为 git 默认值（.git/hooks/）。" -ForegroundColor Green
        exit 0
    }

    $hooksDir = Join-Path $RepoRoot ".githooks"
    if (-not (Test-Path $hooksDir)) {
        Write-Host "找不到 .githooks 目录：$hooksDir" -ForegroundColor Red
        exit 1
    }

    # 用相对路径（".githooks"）而不是绝对路径：git config 里的相对路径按仓库根解析，
    # 仓库整体挪动/换一台机器克隆到不同盘符都不受影响；绝对路径会把本机这次的具体路径写死进
    # .git/config，对协作者没有意义。
    & git config core.hooksPath ".githooks"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "git config core.hooksPath 失败，退出码 $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }

    Write-Host "已安装：core.hooksPath -> .githooks（pre-commit 会在每次 git commit 前自动跑 check.ps1 -SkipUnity -Quick）。" -ForegroundColor Green
    Write-Host "紧急情况需要跳过时用 'git commit --no-verify'（不建议常态化使用）。" -ForegroundColor Yellow
    exit 0
} finally {
    Pop-Location
}
