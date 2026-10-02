<#
发布前"含 Unity 全量回归记录"前置检查（测试覆盖第四批，2026-10-01，复盘 I-13；设计层拍板见
docs/复盘/测试覆盖剩余项-2026-10-01.md 末节"拍板"）。

背景：CI 固定 `check.ps1 -SkipUnity`（托管运行器没有 Unity 许可，不上 CI 是设计层的决定），而
`build.ps1 -Release` 过去带一个 `-ReleaseSkipUnity` 开关，可以整段跳过 Unity 的 EditMode/PlayMode/
独立版冒烟/消费方演练——结果是"发布出去的提交是否真的在有 Unity 的机器上全量通过过"没有任何强制
证据。做法：删掉 `-ReleaseSkipUnity`；`build.ps1 -Release` 在写回版本号之前先调用本文件的
`Test-ReleaseRegressionRecord`，要求 `REGRESSION_LOG.md` 里存在一行"含 Unity 全量通过"的记录，且
其对应提交满足下面之一，否则拒绝发布并打印原因：

  1. 就是当前 HEAD；
  2. 是 HEAD 的祖先，且从该祖先到 HEAD 之间改动的所有文件都是文档类路径（见
     `Test-DocsOnlyPath`：`docs/`、`architecture/` 下任意文件，任意位置的 `*.md`（含 CHANGELOG.md、
     REGRESSION_LOG.md）及其 `.meta`，以及 `timing/` 下的 `*.jsonl` 耗时记录）——也就是"记录之后只追加了
     文档/回归记录行/耗时记录行"。

判断记录：
1) 记录行格式沿用 REGRESSION_LOG.md 既有的四列表格（run_id | 结果 | 提交 sha | 日期），不新增列、
   不要求回填历史行。"含 Unity 全量通过"的识别规则（`Test-RegressionRowIsUnityFull`）：结果列以
   "通过"开头，且含"含 Unity"字样或"PlayMode N/N"计数（此前每次含 Unity 的全量记录本来就写了
   PlayMode 计数）；结果列同时出现"未跑/不含/未含/跳过 Unity"或"-SkipUnity"字样的行（只跑了非 Unity
   子集）不算。今后写含 Unity 全量记录请保证结果列含"含 Unity"或"PlayMode N/N"之一。
2) 提交列可以写多个 sha（例如"889a9736 + 收口提交"），取其中所有 7～40 位十六进制词逐个解析；只要
   有一个满足上面的 HEAD / 祖先条件即可。短 sha 用 `git rev-parse --verify <sha>^{commit}` 展开，
   解析不出（拼错、已被改写掉）的 sha 不满足条件。
3) 为什么允许"祖先 + 之后只改文档"而不是死要求等于 HEAD：记录行本身要作为一次提交落进 git，所以
   "测过的那个提交"永远比"带着记录行的提交"早一个；死要求等于 HEAD 会让记录永远对不上。
4) 判定从最新记录往旧记录找，找到第一个满足的即放行；全部不满足时，原因里报告最新的一条含 Unity
   全量记录没有被采纳的具体理由（不是祖先 / 其后改了哪些非文档文件），让操作者知道是要补跑还是
   要补登记。
5) DryRun 不拦（`build.ps1 -Release X -DryRun` 的用途是演练流水线、不产生可发布的提交/标签），
   只打印同一份判定结果作警告；真正发布（非 DryRun）才拒绝。

纯函数 + 只读 git 命令，不写任何文件；PowerShell 5.1 兼容。`toolchain/tests/
test_release_regression_guard.py` 用临时 git 仓库 + 伪造 REGRESSION_LOG 行覆盖放行/拒绝各场景。
#>

function Test-DocsOnlyPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $p = ($Path -replace '\\', '/').TrimStart('/')
    if ($p.StartsWith("docs/") -or $p.StartsWith("architecture/")) { return $true }
    if ($p.EndsWith(".md") -or $p.EndsWith(".md.meta")) { return $true }
    # 登记数据：门禁（check.ps1）自动写入的耗时记录 timing/*.jsonl（AGENTS.md 第 1c 节）。文档与登记分支提交自己的
    # 耗时行时不应让发布守卫把它当成"记录之后改了代码"而拒绝（AGENTS.md 第 1b 节末条）。只认 timing/ 下的 .jsonl，
    # 其它位置的同后缀文件（如数据、夹具里的 jsonl）仍算非文档。
    if ($p.StartsWith("timing/") -and $p.EndsWith(".jsonl")) { return $true }
    return $false
}

# 解析 REGRESSION_LOG.md 的表格行。按文件顺序返回（旧 -> 新）。不是 `| full-YYYYMMDD-NN | ... |` 形状
# 的行（表头、分隔线、说明文字）一律忽略。
function Get-RegressionLogRows {
    param([Parameter(Mandatory = $true)][string]$LogPath)
    if (-not (Test-Path -LiteralPath $LogPath)) { return @() }
    $rows = @()
    foreach ($line in (Get-Content -LiteralPath $LogPath -Encoding UTF8)) {
        $trimmed = $line.Trim()
        if (-not ($trimmed.StartsWith("|") -and $trimmed.EndsWith("|"))) { continue }
        $cells = @($trimmed.Substring(1, $trimmed.Length - 2) -split '\|' | ForEach-Object { $_.Trim() })
        if ($cells.Count -lt 4) { continue }
        if ($cells[0] -notmatch '^full-\d{8}-\d+$') { continue }
        $resultText = ($cells[1..($cells.Count - 3)] -join "|")
        $shaCell = $cells[$cells.Count - 2]
        $shas = @([regex]::Matches($shaCell, '\b[0-9a-f]{7,40}\b') | ForEach-Object { $_.Value })
        $rows += [PSCustomObject]@{
            RunId  = $cells[0]
            Result = $resultText
            ShaCell = $shaCell
            Shas   = $shas
            Date   = $cells[$cells.Count - 1]
        }
    }
    return @($rows)
}

function Test-RegressionRowIsUnityFull {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$ResultText)
    if (-not $ResultText.StartsWith("通过")) { return $false }
    if ($ResultText -match '(未跑|不含|未含|没跑|跳过)\s*Unity' -or $ResultText -match '-SkipUnity') { return $false }
    return ($ResultText -match '含\s*Unity' -or $ResultText -match 'PlayMode\s*\d+/\d+')
}

# 判断记录：固定 core.quotepath=false——默认 git 会把含非 ASCII 字符的路径（本仓库 docs/ 下大量中文文件名）
# 输出成带引号的八进制转义（"docs/å¤..."），那样路径首字符变成引号，按前缀判文档类路径会误判。
function Invoke-GuardGit {
    param([string]$RepoRoot, [string[]]$GitArgs)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $out = @(& git -C $RepoRoot -c core.quotepath=false @GitArgs 2>$null)
        return [PSCustomObject]@{ ExitCode = $LASTEXITCODE; Lines = @($out | ForEach-Object { "$_" }) }
    } finally {
        $ErrorActionPreference = $prev
    }
}

# 返回 Ok / Reason / RunId / Sha / Head。Ok=$true 时 Reason 写明采纳的是哪条记录、与 HEAD 的关系。
function Test-ReleaseRegressionRecord {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$LogPath = ""
    )
    if ($LogPath -eq "") { $LogPath = Join-Path $RepoRoot "REGRESSION_LOG.md" }

    $headResult = Invoke-GuardGit -RepoRoot $RepoRoot -GitArgs @("rev-parse", "HEAD")
    if ($headResult.ExitCode -ne 0 -or $headResult.Lines.Count -eq 0) {
        return [PSCustomObject]@{ Ok = $false; Reason = "无法解析当前 HEAD（git rev-parse HEAD 失败）"; RunId = ""; Sha = ""; Head = "" }
    }
    $head = $headResult.Lines[0].Trim()

    if (-not (Test-Path -LiteralPath $LogPath)) {
        return [PSCustomObject]@{ Ok = $false; Reason = "找不到回归记录文件 $LogPath"; RunId = ""; Sha = ""; Head = $head }
    }
    $rows = @(Get-RegressionLogRows -LogPath $LogPath)
    if ($rows.Count -eq 0) {
        return [PSCustomObject]@{ Ok = $false; Reason = "$LogPath 里没有任何 full-YYYYMMDD-NN 记录行"; RunId = ""; Sha = ""; Head = $head }
    }
    $unityRows = @($rows | Where-Object { Test-RegressionRowIsUnityFull -ResultText $_.Result })
    if ($unityRows.Count -eq 0) {
        return [PSCustomObject]@{ Ok = $false; Reason = "$LogPath 里没有任何'含 Unity 全量通过'记录（结果列须以'通过'开头并含'含 Unity'或'PlayMode N/N'，且不含'未跑 Unity'/'-SkipUnity'）"; RunId = ""; Sha = ""; Head = $head }
    }

    $newestFirst = @($unityRows)
    [array]::Reverse($newestFirst)
    $newestRejection = ""
    foreach ($row in $newestFirst) {
        $rowRejections = @()
        foreach ($sha in $row.Shas) {
            $resolved = Invoke-GuardGit -RepoRoot $RepoRoot -GitArgs @("rev-parse", "--verify", "--quiet", "$sha^{commit}")
            if ($resolved.ExitCode -ne 0 -or $resolved.Lines.Count -eq 0) {
                $rowRejections += "$sha 在本仓库里解析不到提交"
                continue
            }
            $full = $resolved.Lines[0].Trim()
            if ($full -eq $head) {
                return [PSCustomObject]@{ Ok = $true; Reason = "采纳 $($row.RunId)：记录提交 $sha 就是当前 HEAD"; RunId = $row.RunId; Sha = $full; Head = $head }
            }
            $ancestor = Invoke-GuardGit -RepoRoot $RepoRoot -GitArgs @("merge-base", "--is-ancestor", $full, $head)
            if ($ancestor.ExitCode -ne 0) {
                $rowRejections += "$sha 不是当前 HEAD 的祖先"
                continue
            }
            $diff = Invoke-GuardGit -RepoRoot $RepoRoot -GitArgs @("diff", "--name-only", $full, $head)
            if ($diff.ExitCode -ne 0) {
                $rowRejections += "$sha 到 HEAD 的差异无法计算（git diff 失败）"
                continue
            }
            $nonDoc = @($diff.Lines | Where-Object { $_.Trim() -ne "" -and -not (Test-DocsOnlyPath -Path $_) })
            if ($nonDoc.Count -eq 0) {
                return [PSCustomObject]@{ Ok = $true; Reason = "采纳 $($row.RunId)：记录提交 $sha 是 HEAD 的祖先，其后只改了文档类文件（$($diff.Lines.Count) 个）"; RunId = $row.RunId; Sha = $full; Head = $head }
            }
            $shown = @($nonDoc | Select-Object -First 8)
            $more = if ($nonDoc.Count -gt $shown.Count) { " 等共 $($nonDoc.Count) 个" } else { "" }
            $rowRejections += "$sha 之后到 HEAD 改动了非文档文件：" + ($shown -join ", ") + $more
        }
        if ($rowRejections.Count -eq 0) { $rowRejections += "记录行没有可解析的提交 sha（提交列：$($row.ShaCell)）" }
        if ($newestRejection -eq "") {
            $newestRejection = "最新的含 Unity 全量记录 $($row.RunId) 未被采纳：" + ($rowRejections -join "；")
        }
    }
    return [PSCustomObject]@{
        Ok = $false
        Reason = "$LogPath 里没有对应当前 HEAD（$($head.Substring(0, 8))）的含 Unity 全量通过记录。$newestRejection。请先在有 Unity 的机器上对当前提交跑 check.ps1 全量，并在 REGRESSION_LOG.md 追加一行（结果列含'含 Unity'或'PlayMode N/N'），再发布。"
        RunId = ""
        Sha = ""
        Head = $head
    }
}
