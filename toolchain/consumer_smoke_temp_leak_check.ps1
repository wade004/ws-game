<#
.SYNOPSIS
    消费方演练临时目录泄漏冒烟：跑一次 check.ps1（触发 toolchain/consumer_smoke.ps1），断言 %TEMP% 下
    `gf_consumer_smoke_*` 目录的个数与总字节数都没有增加，且本检出自己的默认演练目录已被删除。
    背景见 consumer_smoke.ps1 头部 .NOTES"演练结束即删 WorkDir"：此前每个检出每次演练在 %TEMP% 留一份约 3GB 的死目录。

.PARAMETER CheckArgs
    传给 check.ps1 的参数。默认只带 -NoTiming，即跑一次全量门禁（check.ps1 目前没有"只跑消费方演练"的开关；
    定向模式 -Changed 要有命中 consumer_drill 的改动才会触发演练，所以默认不用它）。需要更小的组合时自己传，
    例如在只改了版本文件的工作树里传 "-Changed","main","-NoTiming"。本脚本会核对演练确实在本次运行里跑过
    （consumer_smoke.log 的写入时间晚于本次起点），没跑过判失败，避免"没触发所以没泄漏"的空洞通过。

.PARAMETER TempRoot
    被统计的临时目录根，默认 $env:TEMP（与 consumer_smoke.ps1 默认 WorkDir 的取法一致）。

.PARAMETER ArtifactsPath
    传给 check.ps1 的 -ArtifactsPath，默认 <仓库根>\bin\_check_artifacts。check.ps1 的完整输出另存到其下
    consumer_smoke_temp_leak_check.log（bin/ 被 .gitignore 覆盖，不入库）。

.NOTES
    PowerShell 5.1 兼容。UTF-8 with BOM。退出码：0 = 个数与字节数都没有增加；1 = 增加了 / 本检出目录没删 / 演练没跑。
    统计的是 TempRoot 下全部 `gf_consumer_smoke_*`，不只是本检出的那个：别的会话（其它工作树）若在本脚本运行期间
    同时跑演练，会在这里造成误报——脚本会列出前后有差异的目录名，由人判断是不是别人的；脚本从不删除任何旧目录。
    期望值全部来自"前快照"，不写死数字。
#>
param(
    [string[]]$CheckArgs = @("-NoTiming"),
    [string]$TempRoot = "",
    [string]$ArtifactsPath = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "_unity_smoke_wait_scope_guard.ps1")

if ($TempRoot -eq "") { $TempRoot = $env:TEMP }
if ($ArtifactsPath -eq "") { $ArtifactsPath = Join-Path $RepoRoot "bin\_check_artifacts" }
if (-not (Test-Path -LiteralPath $ArtifactsPath)) {
    New-Item -ItemType Directory -Force -Path $ArtifactsPath | Out-Null
}

# 目录名 -> 总字节数；Count / Bytes 为汇总。目录在枚举期间被别的会话删掉时忽略错误（按已消失处理）。
function Get-SmokeTempSnapshot {
    $sizes = @{}
    $total = [long]0
    foreach ($dir in @(Get-ChildItem -LiteralPath $TempRoot -Directory -Filter "gf_consumer_smoke_*" -Force -ErrorAction SilentlyContinue)) {
        $sum = (Get-ChildItem -LiteralPath $dir.FullName -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
        if ($null -eq $sum) { $sum = 0 }
        $sizes[$dir.Name] = [long]$sum
        $total += [long]$sum
    }
    return [PSCustomObject]@{ Count = $sizes.Count; Bytes = $total; Sizes = $sizes }
}

$ownWorkDir = Get-ConsumerSmokeDefaultWorkDir -RepoRoot $RepoRoot -TempRoot $TempRoot
$smokeLog = Join-Path $ArtifactsPath "consumer_smoke.log"
$checkLog = Join-Path $ArtifactsPath "consumer_smoke_temp_leak_check.log"

$before = Get-SmokeTempSnapshot
Write-Host ("前快照：gf_consumer_smoke_* 共 {0} 个，合计 {1} 字节（本检出的演练目录 {2}）" -f $before.Count, $before.Bytes, $ownWorkDir) -ForegroundColor Cyan

$startedAt = Get-Date
$checkScript = Join-Path $RepoRoot "check.ps1"
Write-Host ("运行：check.ps1 {0}（完整输出 -> {1}）" -f ($CheckArgs -join " "), $checkLog) -ForegroundColor Cyan
& powershell -NoProfile -ExecutionPolicy Bypass -File $checkScript @CheckArgs -ArtifactsPath $ArtifactsPath | Out-File -FilePath $checkLog -Encoding utf8
$checkExit = $LASTEXITCODE
Write-Host "check.ps1 退出码 $checkExit（门禁本身的结论不是本脚本的判据，见下）" -ForegroundColor Cyan

$after = Get-SmokeTempSnapshot
Write-Host ("后快照：gf_consumer_smoke_* 共 {0} 个，合计 {1} 字节" -f $after.Count, $after.Bytes) -ForegroundColor Cyan

$problems = New-Object System.Collections.Generic.List[string]

# 演练必须真的在本次运行里跑过（consumer_smoke.ps1 每次运行都会重写自己的 transcript 日志）。
$drillRan = (Test-Path -LiteralPath $smokeLog) -and ((Get-Item -LiteralPath $smokeLog).LastWriteTime -ge $startedAt)
if (-not $drillRan) {
    $problems.Add("本次 check.ps1 没有触发消费方演练（$smokeLog 未在本次运行期间更新），无法证明没有泄漏——请换能触发 consumer_drill 的参数组合")
}
if ($after.Count -gt $before.Count) {
    $problems.Add("目录个数增加：$($before.Count) -> $($after.Count)")
}
if ($after.Bytes -gt $before.Bytes) {
    $problems.Add("总字节数增加：$($before.Bytes) -> $($after.Bytes)（+$($after.Bytes - $before.Bytes)）")
}
if ($drillRan -and (Test-Path -LiteralPath $ownWorkDir)) {
    $problems.Add("本检出的演练目录演练结束后仍然存在：$ownWorkDir")
}

# 前后有差异的目录逐个列出，方便区分"本检出泄漏"与"别的会话同时在跑演练"（后者是误报，不要去删别人的目录）。
$names = @($before.Sizes.Keys) + @($after.Sizes.Keys) | Sort-Object -Unique
foreach ($name in $names) {
    $b = if ($before.Sizes.ContainsKey($name)) { $before.Sizes[$name] } else { $null }
    $a = if ($after.Sizes.ContainsKey($name)) { $after.Sizes[$name] } else { $null }
    if ($b -ne $a) {
        $tag = if ((Join-Path $TempRoot $name) -ieq $ownWorkDir) { "本检出" } else { "其它检出/会话" }
        Write-Host ("  差异：{0}（{1}）前={2} 后={3}" -f $name, $tag, $(if ($null -eq $b) { "无" } else { $b }), $(if ($null -eq $a) { "无" } else { $a })) -ForegroundColor Yellow
    }
}

if ($problems.Count -gt 0) {
    Write-Host ""
    foreach ($p in $problems) { Write-Host "失败：$p" -ForegroundColor Red }
    Write-Host "（若差异里出现「其它检出/会话」的目录，可能是别的会话同时在跑演练造成的误报；本脚本不会删除任何目录。）" -ForegroundColor Yellow
    exit 1
}
Write-Host ("通过：演练已跑，目录个数 {0} -> {1}，总字节 {2} -> {3}，均未增加，本检出演练目录已删除。" -f $before.Count, $after.Count, $before.Bytes, $after.Bytes) -ForegroundColor Green
exit 0
