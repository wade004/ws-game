<#
门禁"测试步骤用例数下限"共享判定（2026-10-01，复盘 docs/复盘/测试覆盖梳理-2026-10-01.md 第 4 节 I-2）：
dotnet test / pytest / Unity EditMode / Unity PlayMode 四个测试步骤此前只看"退出码 0 / 结果
Passed"。如果某次改动把整批用例悄悄丢掉（测试程序集没编进来、发现规则被改坏、批量 Assert.Ignore、
过滤器写错），剩下的用例照样全绿，门禁照样 PASS，覆盖面却已经塌了。本文件把"实际跑了多少、过了
多少、跳过多少"从结果文件里解析出来，与 `toolchain/gate_floors.json`（下限的唯一出处）比较：

  - passed 低于 min_passed            -> FAIL（用例被丢了）
  - skipped + inconclusive 超过 max_skipped -> FAIL（用例被批量跳过了）
  - failed > 0                        -> FAIL（兜底，退出码/结果字段本身已经会挡，这里再拦一次）
  - total/passed/skipped/inconclusive 四个数不论成败都写进步骤 Detail

判断记录：
1) 为什么解析结构化结果文件、不解析控制台文本：控制台输出格式随工具版本变、本地化、行缓冲会变；
   dotnet test 已经在用 `--logger trx`（见 _gate_line_heavy.ps1 性能基线诊断），直接读 trx 的
   `<Counters>`；pytest 加 `--junitxml`；Unity 本来就产出 NUnit 结果 XML。三种都是稳定的机器可读
   格式，伪造夹具也简单（toolchain/tests/test_gate_floors_logic.py）。
2) 计数口径：trx 的 skipped = max(total-executed, Counters.notExecuted)（被跳过/被忽略的用例；
   xunit 的 Skip 只体现在 total>executed，见 Get-TrxTestCounts 注释）；junit 的 passed =
   tests - failures - errors - skipped；NUnit 直接取根节点 total/passed/failed/inconclusive/
   skipped 属性（Assert.Ignore / [Ignore] 在 NUnit 3 结果里计入 skipped）。
3) 下限取值规则与"套件明显增长后要抬高下限"的维护义务写在 check.ps1 头部判断记录（唯一出处），
   数字本身只在 toolchain/gate_floors.json；本文件不含任何具体数字。
4) 缺文件、缺套件登记、缺结果文件一律 FAIL，不静默放行（否则删掉登记就能绕过门禁）。

所有函数只用 Write-Host 输出日志、不向管道漏东西——调用点是 Invoke-CheckStep 的 scriptblock，
多余的管道输出会被 F1 防线判为"返回多个对象"（见 _gate_step_runner.ps1）。PowerShell 5.1 兼容。
#>

function Get-GateFloorsConfig {
    param([Parameter(Mandatory = $true)][string]$FloorsPath)
    if (-not (Test-Path -LiteralPath $FloorsPath)) {
        throw "找不到用例数下限登记文件：$FloorsPath"
    }
    return (Get-Content -LiteralPath $FloorsPath -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function New-GateTestCounts {
    param([int]$Total, [int]$Passed, [int]$Failed, [int]$Skipped, [int]$Inconclusive)
    return [PSCustomObject]@{
        Total        = $Total
        Passed       = $Passed
        Failed       = $Failed
        Skipped      = $Skipped
        Inconclusive = $Inconclusive
    }
}

# dotnet test --logger trx --results-directory <dir>：每个测试工程一个 .trx，逐个读 Counters 求和。
# 目录里没有任何 .trx 时返回 $null（调用方判 FAIL）。
function Get-TrxTestCounts {
    param([Parameter(Mandatory = $true)][string]$TrxDir)
    if (-not (Test-Path -LiteralPath $TrxDir)) { return $null }
    $files = @(Get-ChildItem -LiteralPath $TrxDir -Filter "*.trx" -File -Recurse -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { return $null }
    $total = 0; $passed = 0; $failed = 0; $skipped = 0; $inconclusive = 0
    foreach ($f in $files) {
        $doc = New-Object System.Xml.XmlDocument
        $doc.Load($f.FullName)
        $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
        $ns.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
        $c = $doc.SelectSingleNode("//t:ResultSummary/t:Counters", $ns)
        if ($null -eq $c) { continue }
        $total += [int]$c.GetAttribute("total")
        $passed += [int]$c.GetAttribute("passed")
        $failed += [int]$c.GetAttribute("failed") + [int]$c.GetAttribute("error") + [int]$c.GetAttribute("timeout") + [int]$c.GetAttribute("aborted")
        # 被跳过的用例数：xunit 的 [Fact(Skip=...)] 在 trx 里记成 outcome=NotExecuted，但 Counters 的
        # notExecuted 属性实测是 0，只体现在 total 比 executed 多（2026-10-01 实测：total=941
        # executed=940 notExecuted=0）。所以取 total-executed 与 notExecuted 里较大者，不单信
        # notExecuted 一个字段（单信它会让批量 Skip 整批逃过 max_skipped）。
        $execDiff = [int]$c.GetAttribute("total") - [int]$c.GetAttribute("executed")
        $skipped += [Math]::Max($execDiff, [int]$c.GetAttribute("notExecuted"))
        $inconclusive += [int]$c.GetAttribute("inconclusive")
    }
    return (New-GateTestCounts -Total $total -Passed $passed -Failed $failed -Skipped $skipped -Inconclusive $inconclusive)
}

# pytest --junitxml：根为 <testsuites> 或直接 <testsuite>，属性 tests/failures/errors/skipped。
function Get-JUnitTestCounts {
    param([Parameter(Mandatory = $true)][string]$XmlPath)
    if (-not (Test-Path -LiteralPath $XmlPath)) { return $null }
    $doc = New-Object System.Xml.XmlDocument
    $doc.Load($XmlPath)
    $suites = @($doc.SelectNodes("//testsuite"))
    if ($suites.Count -eq 0) { return $null }
    $total = 0; $failed = 0; $skipped = 0
    foreach ($s in $suites) {
        $total += [int]$s.GetAttribute("tests")
        $failed += [int]$s.GetAttribute("failures") + [int]$s.GetAttribute("errors")
        $skipped += [int]$s.GetAttribute("skipped")
    }
    $passed = $total - $failed - $skipped
    return (New-GateTestCounts -Total $total -Passed $passed -Failed $failed -Skipped $skipped -Inconclusive 0)
}

# Unity Test Runner（NUnit 3）结果 XML：根节点 <test-run> 自带汇总属性。
function Get-NUnitTestCounts {
    param([Parameter(Mandatory = $true)][string]$XmlPath)
    if (-not (Test-Path -LiteralPath $XmlPath)) { return $null }
    $doc = New-Object System.Xml.XmlDocument
    $doc.Load($XmlPath)
    $root = $doc.DocumentElement
    if ($null -eq $root) { return $null }
    return (New-GateTestCounts `
        -Total ([int]$root.GetAttribute("total")) `
        -Passed ([int]$root.GetAttribute("passed")) `
        -Failed ([int]$root.GetAttribute("failed")) `
        -Skipped ([int]$root.GetAttribute("skipped")) `
        -Inconclusive ([int]$root.GetAttribute("inconclusive")))
}

# 纯判定：给定计数与该套件的下限登记，返回 Ok/Detail（Detail 恒含四个数）。
function Test-GateTestCounts {
    param(
        [Parameter(Mandatory = $true)]$Counts,
        [Parameter(Mandatory = $true)]$Floor,
        [Parameter(Mandatory = $true)][string]$Suite
    )
    $minPassed = [int]$Floor.min_passed
    $maxSkipped = [int]$Floor.max_skipped
    $skipLike = $Counts.Skipped + $Counts.Inconclusive
    $detail = "total=$($Counts.Total) passed=$($Counts.Passed) skipped=$($Counts.Skipped) inconclusive=$($Counts.Inconclusive)" +
        "（下限 passed>=$minPassed，允许 skipped+inconclusive<=$maxSkipped，见 toolchain/gate_floors.json）"
    $problems = @()
    if ($Counts.Failed -gt 0) {
        $problems += "failed=$($Counts.Failed)"
    }
    if ($Counts.Passed -lt $minPassed) {
        $problems += "passed=$($Counts.Passed) 低于下限 $minPassed（用例被丢了？确属有意删减时才在 gate_floors.json 下调并写明理由）"
    }
    if ($skipLike -gt $maxSkipped) {
        $problems += "skipped+inconclusive=$skipLike 超过允许值 $maxSkipped（用例被批量跳过了？）"
    }
    if ($problems.Count -gt 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "[$Suite] " + ($problems -join "；") + " | " + $detail }
    }
    return [PSCustomObject]@{ Ok = $true; Detail = $detail }
}

# 解析 + 判定一步到位，供三条门禁线与 pytest 共用。Kind = Trx | JUnit | NUnit。
function Invoke-GateTestFloorCheck {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("Trx", "JUnit", "NUnit")][string]$Kind,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Suite,
        [Parameter(Mandatory = $true)][string]$FloorsPath
    )
    try {
        $cfg = Get-GateFloorsConfig -FloorsPath $FloorsPath
    } catch {
        return [PSCustomObject]@{ Ok = $false; Detail = $_.Exception.Message }
    }
    $floor = $null
    if ($cfg.PSObject.Properties.Name -contains "suites") {
        $prop = $cfg.suites.PSObject.Properties[$Suite]
        if ($null -ne $prop) { $floor = $prop.Value }
    }
    if ($null -eq $floor) {
        return [PSCustomObject]@{ Ok = $false; Detail = "toolchain/gate_floors.json 没有登记套件 '$Suite' 的下限（缺登记不放行）" }
    }
    $counts = $null
    if ($Kind -eq "Trx") { $counts = Get-TrxTestCounts -TrxDir $Path }
    elseif ($Kind -eq "JUnit") { $counts = Get-JUnitTestCounts -XmlPath $Path }
    else { $counts = Get-NUnitTestCounts -XmlPath $Path }
    if ($null -eq $counts) {
        return [PSCustomObject]@{ Ok = $false; Detail = "[$Suite] 未找到可解析的测试结果（$Kind：$Path），无法核对用例数下限" }
    }
    return (Test-GateTestCounts -Counts $counts -Floor $floor -Suite $Suite)
}

# -----------------------------------------------------------------------------
# 第四批追加（2026-10-01，复盘 I-2 余项 / I-5 缩减版 / I-8 余项）
# -----------------------------------------------------------------------------

# 判断记录（I-2 余项）：dotnet test 步骤原先找不到性能基线诊断行（PerfBaselineTests 被意外排除或没编进
# 本次运行）时只 Write-Host 一行黄色警告、步骤照样 PASS，整个 Perf 类别塌了也无人察觉。解析抽成本函数
# 让调用点能判 FAIL，也让 toolchain/tests/test_gate_floors_logic.py 能用伪造 trx 夹具直接验证。正则与
# 此前内联版逐字相同。目录不存在或没有 trx 返回空数组（调用方据此判 FAIL，不在这里抛）。
function Get-PerfDiagnosticLines {
    param([Parameter(Mandatory = $true)][string]$TrxDir)
    if (-not (Test-Path -LiteralPath $TrxDir)) { return @() }
    $lines = @(Get-ChildItem -LiteralPath $TrxDir -Filter "*.trx" -Recurse -ErrorAction SilentlyContinue |
        Select-String -Pattern '^\s*<StdOut>perf \S+_WithinBaselineThreshold median=.*factor=.*reference=' |
        ForEach-Object { ($_.Line.Trim() -replace '^<StdOut>', '') -replace '</StdOut>$', '' })
    return @($lines | Sort-Object -Unique)
}

# 判断记录（I-5 缩减版）：全量门禁额外跑的两类 pytest（不设 PYTHONUTF8 的环境矩阵、PowerShell 5.1/7
# 双宿主矩阵）没有各自的下限登记，判据统一为：junit 可解析、failed=0、skipped+inconclusive=0（环境性
# skip 在矩阵里一律 FAIL——矩阵的意义就是"这些用例在这个环境里真的跑了"，宿主缺失/条件不满足被
# skip 吞掉等于没跑）、passed 不低于调用方给的 MinPassed（防整批用例被丢）。
function Test-GateExtraPytestRun {
    param(
        [Parameter(Mandatory = $true)][string]$JUnitPath,
        [Parameter(Mandatory = $true)][string]$Label,
        [int]$MinPassed = 1
    )
    $counts = Get-JUnitTestCounts -XmlPath $JUnitPath
    if ($null -eq $counts) {
        return [PSCustomObject]@{ Ok = $false; Detail = "[$Label] 未找到可解析的 junit 结果：$JUnitPath" }
    }
    $detail = "total=$($counts.Total) passed=$($counts.Passed) skipped=$($counts.Skipped)（要求 passed>=$MinPassed，skipped 必须为 0）"
    $problems = @()
    if ($counts.Failed -gt 0) { $problems += "failed=$($counts.Failed)" }
    if ($counts.Skipped + $counts.Inconclusive -gt 0) { $problems += "skipped+inconclusive=$($counts.Skipped + $counts.Inconclusive)（环境性 skip 在矩阵步骤里一律 FAIL）" }
    if ($counts.Passed -lt $MinPassed) { $problems += "passed=$($counts.Passed) 低于要求 $MinPassed" }
    if ($problems.Count -gt 0) {
        return [PSCustomObject]@{ Ok = $false; Detail = "[$Label] " + ($problems -join "；") + " | " + $detail }
    }
    return [PSCustomObject]@{ Ok = $true; Detail = "[$Label] $detail" }
}
