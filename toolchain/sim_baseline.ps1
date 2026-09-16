<#
.SYNOPSIS
    数值仿真基线更新流程的薄封装（T-N6-6）——对 `toolchain/simrunner` 的一层参数简化，不包含任何
    独立的比对/装配逻辑（真正的报告生成/基线比对/差异分类全部在 `Core.Sim.SimReport`/
    `Core.Sim.SimBaseline`/`Core.Sim.BaselineComparer`，本脚本只是拼好 `dotnet run`/已构建产物的
    调用参数，惯例同 `toolchain/validate_data.py` 之于 `toolchain/validator` 的关系——骨架/参数
    拼装与真实校验逻辑分离，不重复实现判断）。

.PARAMETER Scenario
    场景短 id（如 `sim_arena_matrix`）或 `all`（默认）。

.PARAMETER UpdateBaseline
    传入时等价于 `simrunner run --update-baseline`——用当前报告覆盖 `-BaselineDir` 下对应的基线
    文件。**更新基线前请先确认这是一次有意的数值/结算行为变化**，惯例同
    `core/gameplay/tests/Replay/README.md`"如何更新基线"一节：比对失败本身不说明对错，只说明
    结果变了；不要把基线更新悄悄混进一次无关改动的提交里，提交信息里必须注明本次更新了基线以及
    原因。

.PARAMETER Out
    报告/差异文件输出目录，默认仓库根下 `.sim_out`（建议加入 `.gitignore`，不提交）。

.PARAMETER BaselineDir
    基线文件所在目录，默认 `core/sim/tests/baseline`。

.PARAMETER FrameworkRoot
    框架级数据根，默认 `data/_framework`。

.PARAMETER DataRoot
    数据根（可传多个），默认仅 `core/sim/tests/data`（嵌入式最小仿真数据集）。

.PARAMETER Runs
    覆盖场景登记的 `runs`（仅用于快速冒烟，见 `simrunner run --runs`），可选。

.PARAMETER Version
    写入报告/基线的框架版本字符串，可选，默认取仓库根 `VERSION` 文件内容（读不到则为
    `"unknown"`）。

.PARAMETER ArtifactsPath
    透传给 `dotnet run --artifacts-path`（未构建产物时按需现场编译）；未提供时使用 `dotnet run`
    默认的 `obj`/`bin` 输出位置。

.EXAMPLE
    # 基线更新流程五步（同 core/gameplay/tests/Replay/README.md 同一原理，见
    # core/sim/README.md"基线更新流程"一节）：
    # 1. 改动确实是有意的数值/结算行为变化（不是意外回归）。
    # 2. 跑一次不带 -UpdateBaseline 的比对，看差异：
    ./toolchain/sim_baseline.ps1 -Scenario all
    # 3. 打开 .sim_out/<scenario>.diff.txt 人工审阅每一条差异是否对应你在第 1 步确认的改动。
    # 4. 确认无误后，同一份改动里加 -UpdateBaseline 重新生成基线：
    ./toolchain/sim_baseline.ps1 -Scenario all -UpdateBaseline
    # 5. 提交信息里注明"更新了 sim 基线以及原因"，与本次数值/结算改动同一提交。
#>
[CmdletBinding()]
param(
    [string]$Scenario = "all",
    [switch]$UpdateBaseline,
    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) ".sim_out"),
    [string]$BaselineDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "core/sim/tests/baseline"),
    [string]$FrameworkRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) "data/_framework"),
    [string[]]$DataRoot = @((Join-Path (Split-Path -Parent $PSScriptRoot) "core/sim/tests/data")),
    [Nullable[int]]$Runs = $null,
    [string]$Version = $null,
    [string]$ArtifactsPath = $null
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $Version) {
    $versionFile = Join-Path $repoRoot "VERSION"
    if (Test-Path $versionFile) {
        $Version = (Get-Content -Path $versionFile -Raw).Trim()
    } else {
        $Version = "unknown"
    }
}

# 判断记录（T-N6-7：项目路径改为相对本脚本自身目录解析，而不是硬编码 "toolchain/simrunner"）：
# 本脚本此前假设自己恒常驻在 "<repo_root>/toolchain/sim_baseline.ps1"（"toolchain/simrunner" 是
# 相对 $repoRoot=Split-Path -Parent $PSScriptRoot 拼出来的）——这一假设在框架源码仓库内成立，
# 但本脚本现按任务书要求随 com.gamefoundation.toolchain 包一起分发到 Tools~/sim_baseline.ps1
# （同目录下是 Tools~/simrunner/，不是 <包根>/toolchain/simrunner/），硬编码路径在包内会解析到
# 一个不存在的目录。改为 `Join-Path $PSScriptRoot "simrunner"`（本脚本自身目录的兄弟目录
# "simrunner"，两种布局下都与本脚本直接同级，惯例同 toolchain/validate_data.py"改为相对自身
# 文件所在目录解析 validator/ 子目录"判断记录），两种场景下都能正确解析，不需要为包内布局单独
# 分支。SimRunner.csproj 本身在包内因 `Exists('..\..\core\sim\Core.Sim.csproj')` 为假会自动
# 走 lib/ 回退分支现场编译（见该 csproj 判断记录），不需要本脚本额外处理。
#
# 判断记录：一次性拼一个扁平数组按顺序传给 dotnet，不分两段拼接——--artifacts-path 是
# dotnet run 自身的参数（属于 "run" 与 "--" 之间那一段），"--" 之后全部是转发给 SimRunner
# 自己的参数，两段顺序固定、不能颠倒，用同一个数组顺序拼接比"数组切片再拼接"更不容易出错。
$simRunnerProjectPath = Join-Path $PSScriptRoot "simrunner"
$dotnetArgs = @("run", "--project", $simRunnerProjectPath)
if ($ArtifactsPath) {
    $dotnetArgs += @("--artifacts-path", $ArtifactsPath)
}
$dotnetArgs += "--"
$dotnetArgs += @("run", "--scenario", $Scenario, "--framework-root", $FrameworkRoot)
foreach ($root in $DataRoot) {
    $dotnetArgs += @("--data-root", $root)
}
$dotnetArgs += @("--out", $Out, "--baseline-dir", $BaselineDir, "--version", $Version)

if ($UpdateBaseline) {
    $dotnetArgs += "--update-baseline"
}
if ($null -ne $Runs) {
    $dotnetArgs += @("--runs", $Runs.ToString())
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null

Push-Location $repoRoot
try {
    & dotnet $dotnetArgs
    $exitCode = $LASTEXITCODE
} finally {
    Pop-Location
}

exit $exitCode
