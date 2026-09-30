<#
.SYNOPSIS
    数值仿真基线比对门禁的 Added 拦截判定（从 toolchain/_gate_line_heavy.ps1 "数值仿真基线比对"步骤
    抽出的纯函数，可独立调用与测试；docs/复盘/测试覆盖梳理-2026-10-01.md I-3）。

    判断记录：`simrunner` 自身退出码 0 只承诺"无 Exceeded/Removed"，不把 `Added`（基线里从未记录过的
    统计量）算作阻断（契约见 core/sim/README.md"命令行入口"一节）；AGENTS.md 第 4 节因一次新增测试
    技能漏烘焙 coverage 基线、`Added` 差异在 31/31 全 PASS 下潜伏 4 天的事故，要求门禁额外解析
    `simrunner` 每个场景摘要行里的 `added=<n>` 字段并拦截 n>0。这段解析原先内联在门禁线脚本里，
    没有任何自动化测试守护；抽成函数后由 toolchain/tests/test_gate_sim_added_guard.py 用伪造输出
    直接验证"added=0 放行、added>0 拦截、摘要行缺失或格式漂移也拦截"。

    只定义函数、无顶层副作用（同目录 `_unity_path_length_guard.ps1`、`_hash.ps1` 同一模式）。

    场景摘要行格式（toolchain/simrunner/Program.cs 每个场景一行，正则与门禁历史版本逐字相同）：
        scenario=<id> kind=<k> stats=<n> exceeded=<n> added=<n> removed=<n> result=<PASS|FAIL>

    缺行/格式漂移也拦截的理由：simrunner 退出码 0 时必然已经至少打印了一行场景摘要（无场景本身就是
    退出码 2）。若一行都解析不出来，说明摘要行格式被改动，原正则悄悄失配——此时"没解析到 added>0"
    不等于"没有 Added"，与 4 天潜伏事故是同一类"门禁判据比规则字面更松"的漏洞，因此显式拦截。
#>

function Get-SimRunnerAddedVerdict {
    param([AllowEmptyCollection()][AllowNull()][string[]]$OutputLines)

    $summaryLineCount = 0
    $malformedLines = @()
    $addedScenarios = @()
    foreach ($line in @($OutputLines)) {
        if ($null -eq $line) { continue }
        if ($line -match '^scenario=(\S+)\s+kind=\S+\s+stats=\d+\s+exceeded=\d+\s+added=(\d+)\s+removed=\d+\s+result=') {
            $summaryLineCount++
            $addedCount = [int]$Matches[2]
            if ($addedCount -gt 0) {
                $addedScenarios += [PSCustomObject]@{ Id = $Matches[1]; Count = $addedCount }
            }
        } elseif ($line -match '^scenario=') {
            $malformedLines += $line
        }
    }

    $reason = ""
    if ($addedScenarios.Count -gt 0) {
        $reason = "added"
    } elseif ($malformedLines.Count -gt 0) {
        $reason = "malformed_summary_line"
    } elseif ($summaryLineCount -eq 0) {
        $reason = "no_summary_line"
    }

    return [PSCustomObject]@{
        Blocked          = ($reason -ne "")
        Reason           = $reason
        SummaryLineCount = $summaryLineCount
        AddedScenarios   = $addedScenarios
        MalformedLines   = $malformedLines
    }
}
