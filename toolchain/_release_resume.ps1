<#
.SYNOPSIS
    `build.ps1 -Release X -Resume` 的判定与执行库：全量门禁通过记录（状态文件）、续跑前置校验、续跑起点、
    各阶段幂等检查与执行器。只定义函数、无顶层副作用；PowerShell 5.1 兼容（不用 &&、??、三元）。

.DESCRIPTION
    背景（2026-10-03，1.96.1 发布三次失败）：全量门禁（`-Release` 第 5 步，约 50 分钟）已经通过之后，第 7 步
    打包阶段先后因 dotnet test 自检偶发红、docfx 崩溃（0xC0000409）而失败；脚本当时唯一的恢复路径是
    `git reset --soft <发布前提交>`、手工还原五个版本文件、整条 `-Release` 重跑——全量门禁又要跑一遍。

    落地：
      - 第 5 步通过时写一份被 .gitignore 覆盖的状态文件 `dist/release-<ver>.state.json`（门禁通过记录），
        其后每个阶段完成时各写一笔完成标记（见 `Get-ReleaseStageIds`）。
      - `-Resume` 只在状态文件与仓库现状严格吻合时才续跑（`Test-ReleaseResumePreconditions`），从第一个
        未完成的阶段起按序执行；每个阶段要么本身幂等、要么先检测"是否已经做过"（标签/私服包/推送/
        GitHub Release 各一份检测，见下面各 `Get-*`/`Invoke-*Stage` 函数）。
      - 正常 `-Release`（无 -Resume）与续跑共用同一份执行器，区别只在 `-Resume` 开关：正常路径不做幂等
        检测（行为、阶段顺序与改动前逐字一致），只多写状态标记。

.NOTES
    判断记录（本文件各处；build.ps1 .SYNOPSIS 与 toolchain/README.md "发布" 一节同文登记）：

    1) 状态文件放 `dist/`（被 .gitignore 覆盖）而不是进 git：它是"这台机器上这一次发布尝试"的运行记录，
       不是源码；进 git 会让发布提交之后的工作树变脏，打包自检（带 -dirty 后缀）直接失败。生成物不进 git
       是仓库硬规则（AGENTS.md §0）。`prune_dist.ps1` 把它与 lock / release-notes 同列"发布记录"一律保留。
    2) 续跑的前置校验宁严勿松：状态文件在、版本一致、HEAD 就是记录的发布提交、其父提交就是记录的发布前
       提交、发布提交只含 `$script:ReleaseWritebackFiles`（第 6 步实际 git add 的五个版本文件）里的路径、
       工作树干净、本地 VERSION 等于目标版本——任一不满足就拒绝并打印下一步怎么办。理由：门禁通过记录
       只对"门禁测过的那个提交 + 只改版本文件的发布提交"成立；HEAD 动过、发布提交夹带了别的文件、工作树
       有未提交改动，都意味着要打包/发布的东西不是门禁测过的东西，继续等于给未验证的内容盖章。
    3) 失败在第 5 步（全量门禁）或更早、或第 6 步提交之前，一律不在续跑范围：此时没有门禁通过记录或没有
       发布提交，要修好问题后重跑 `-Release`（重跑全量门禁）。续跑只覆盖"发布提交之后"的失败：打包、
       自检、打标签、私服发布、推送、创建 GitHub Release。
    4) 打包与自检视为一组：状态里 `packaging` 已完成但 `selfCheck` 未完成时，仍要整段重跑打包。理由：
       自检失败说明打包时工作树不干净（lock/MANIFEST 记 `-dirty`），那份产物不可信。重跑允许覆盖
       `dist/<ver>/` 与 zip/lock——此时标签尚未创建（标签在自检之后），沿用既有"发布不可变"守卫
       （`_dist_immutability_guard.ps1`，以标签是否存在为准）原样放行，不传 -AllowOverwriteDist；一旦标签
       已存在就由守卫拒绝覆盖。
    5) 续跑的打包阶段跳过 `dotnet test`（保留 `dotnet build`、DLL 同步、内容同步）：门禁（第 5 步）已经
       对同一棵代码树（发布提交只改版本文件）跑过完整 dotnet test，1.96.1 的失败之一正是这里的自检偶发红；
       `dotnet build` 保留是因为打包要读默认输出路径下的 DLL（门禁用的是 check.ps1 自己的产物路径）。
    6) 标签：本地已有同名标签且指向 HEAD 则跳过；指向别处则拒绝（不移动、不删除别人的标签）。
    7) 私服：同版本号的包已在私服时，比对本地 `.tgz`（`npm pack` 产物，确定性：同输入同字节，实测
       `npm publish --dry-run --json` 给出的 integrity 与 `npm pack` 产物的 sha512 逐位相同）与私服记录的
       `dist.integrity`：一致则跳过，不一致则拒绝——绝不覆盖已发布的包（"发布不可变"）。私服连不上或返回
       E404 以外的错误一律中止，不当作"没发布过"（否则会把"网络故障"误判成"可以发"）。
    8) 推送：远端已有标签且指向 HEAD、且远端分支就在 HEAD 时跳过；远端标签指向别处则拒绝；其余情形照常
       执行与正常路径相同的 `git push origin <分支> refs/tags/<标签>`。
    9) GitHub Release：不存在则按正常路径同一条 `gh release create` 创建；已存在则核对附件名：齐全则跳过，
       有缺则只 `gh release upload` 缺的那几个（不带 --clobber，绝不覆盖已上传附件）；同名附件大小与本地
       文件不符时拒绝。附件集合与正常路径 `gh release create` 的五个文件一致。
    10) 失败提示一律推荐 `build.ps1 -Release <ver> -Resume [同样的 -PublishRegistry/-Publish]`；
        `git reset --soft <发布前提交>` 只保留为"放弃本次发布"的回退路径。
    11) 状态文件时间戳统一写成带时区偏移的 ISO 8601 字符串；PowerShell 7 的 ConvertFrom-Json 会把这类
        字符串解析成 DateTime，读取时统一格式化回同样的字符串，两个宿主读写互通。

    独立成文件（同目录 `_dist_immutability_guard.ps1` 等同一模式），供 `build.ps1` dot-source 与
    `toolchain/tests/test_release_resume.py` 直接 dot-source 测试。含中文，UTF-8 带 BOM。
#>

$script:ReleaseStateSchemaVersion = 1

# ---------------------------------------------------------------------------
# 基础：时间戳、git/原生命令调用
# ---------------------------------------------------------------------------

function Get-ReleaseTimestamp {
    return (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz", [System.Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-ReleaseTimestampText {
    param($Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [datetime]) {
        return $Value.ToString("yyyy-MM-ddTHH:mm:sszzz", [System.Globalization.CultureInfo]::InvariantCulture)
    }
    return "$Value"
}

# 调用原生命令并分开拿到退出码、stdout、stderr；不让 stderr 输出在 $ErrorActionPreference=Stop 下变成终止错误。
function Invoke-ReleaseNative {
    param(
        [Parameter(Mandatory = $true)][string]$Exe,
        [string[]]$NativeArgs = @()
    )
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $all = @(& $Exe @NativeArgs 2>&1)
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prev
    }
    $out = @()
    $err = @()
    foreach ($item in $all) {
        if ($item -is [System.Management.Automation.ErrorRecord]) {
            $err += $item.ToString()
        } else {
            $out += "$item"
        }
    }
    return [PSCustomObject]@{ ExitCode = $code; Out = @($out); Err = @($err) }
}

function Invoke-ReleaseGit {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string[]]$GitArgs
    )
    $all = @("-C", $RepoRoot, "-c", "core.quotepath=false") + $GitArgs
    return (Invoke-ReleaseNative -Exe "git" -NativeArgs $all)
}

function Write-ReleaseStep {
    param([string]$Message)
    Write-Host ""
    Write-Host "==== $Message ====" -ForegroundColor Cyan
}

# ---------------------------------------------------------------------------
# 状态文件
# ---------------------------------------------------------------------------

function Get-ReleaseStatePath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Version
    )
    return (Join-Path $RepoRoot ("dist\release-" + $Version + ".state.json"))
}

# 发布涉及的私服包名（按发布顺序），单一来源 toolchain/registry/registry.json 的 packages 数组。
function Get-ReleasePackageNames {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)
    $path = Join-Path $RepoRoot "toolchain\registry\registry.json"
    if (-not (Test-Path -LiteralPath $path)) {
        throw "找不到 $path（私服包清单），无法确定发布阶段"
    }
    $obj = (Get-Content -LiteralPath $path -Raw -Encoding UTF8) | ConvertFrom-Json
    return @($obj.packages | ForEach-Object { "$_" })
}

# 阶段标识（按执行顺序）：gate -> commit -> packaging -> selfCheck -> tag -> registry:<包名>×N -> push -> githubRelease。
function Get-ReleaseStageIds {
    param([Parameter(Mandatory = $true)][string[]]$PackageNames)
    $ids = @("gate", "commit", "packaging", "selfCheck", "tag")
    foreach ($n in $PackageNames) { $ids += ("registry:" + $n) }
    $ids += "push"
    $ids += "githubRelease"
    return @($ids)
}

function ConvertTo-ReleaseStateTable {
    param($Node)
    if ($null -eq $Node) { return $null }
    if ($Node -is [System.Management.Automation.PSCustomObject]) {
        $table = [ordered]@{}
        foreach ($p in $Node.PSObject.Properties) {
            $table[$p.Name] = ConvertTo-ReleaseStateTable -Node $p.Value
        }
        return $table
    }
    if ($Node -is [datetime]) { return (ConvertTo-ReleaseTimestampText -Value $Node) }
    return $Node
}

function New-ReleaseState {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$ParentCommit,
        [Parameter(Mandatory = $true)][string]$PreviousVersion,
        [Parameter(Mandatory = $true)][string]$GateConclusion,
        [Parameter(Mandatory = $true)][string[]]$PackageNames
    )
    $now = Get-ReleaseTimestamp
    $stages = [ordered]@{}
    foreach ($id in (Get-ReleaseStageIds -PackageNames $PackageNames)) {
        $stages[$id] = [ordered]@{ done = $false; at = $null; detail = $null }
    }
    $stages["gate"]["done"] = $true
    $stages["gate"]["at"] = $now
    $stages["gate"]["detail"] = $GateConclusion
    return [ordered]@{
        schema          = $script:ReleaseStateSchemaVersion
        version         = $Version
        previousVersion = $PreviousVersion
        parentCommit    = $ParentCommit
        releaseCommit   = $null
        gateConclusion  = $GateConclusion
        createdAt       = $now
        updatedAt       = $now
        stages          = $stages
    }
}

function Save-ReleaseState {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$State
    )
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    $json = (ConvertTo-Json -InputObject $State -Depth 8)
    $json = ($json -replace "`r`n", "`n") + "`n"
    $tmp = $Path + ".tmp"
    [System.IO.File]::WriteAllText($tmp, $json, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tmp -Destination $Path -Force
}

# 读取状态文件；不存在返回 $null；存在但不是合法状态文件抛异常（调用方据此给出"状态文件损坏"的拒绝原因）。
function Read-ReleaseState {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $text = [System.IO.File]::ReadAllText($Path, (New-Object System.Text.UTF8Encoding($false)))
    try {
        $obj = $text | ConvertFrom-Json
    } catch {
        throw "状态文件不是合法 JSON：$Path（$($_.Exception.Message)）"
    }
    if ($null -eq $obj -or -not ($obj.PSObject.Properties.Name -contains "stages") -or
        -not ($obj.PSObject.Properties.Name -contains "version") -or
        -not ($obj.PSObject.Properties.Name -contains "parentCommit")) {
        throw "状态文件缺少必需字段（version/parentCommit/stages）：$Path"
    }
    $state = ConvertTo-ReleaseStateTable -Node $obj
    foreach ($key in @("createdAt", "updatedAt")) {
        if ($state.Contains($key)) { $state[$key] = ConvertTo-ReleaseTimestampText -Value $state[$key] }
    }
    foreach ($id in @($state["stages"].Keys)) {
        $st = $state["stages"][$id]
        if ($st -is [System.Collections.IDictionary] -and $st.Contains("at")) {
            $st["at"] = ConvertTo-ReleaseTimestampText -Value $st["at"]
        }
    }
    return $state
}

# 标记（或取消标记）一个阶段，并刷新 updatedAt；状态文件不存在即抛异常（不静默创建半份记录）。
function Set-ReleaseStage {
    param(
        [Parameter(Mandatory = $true)][string]$StatePath,
        [Parameter(Mandatory = $true)][string]$Stage,
        [bool]$Done = $true,
        [AllowEmptyString()][string]$Detail = ""
    )
    $state = Read-ReleaseState -Path $StatePath
    if ($null -eq $state) { throw "找不到发布状态文件：$StatePath" }
    if (-not $state["stages"].Contains($Stage)) {
        $state["stages"][$Stage] = [ordered]@{ done = $false; at = $null; detail = $null }
    }
    $now = Get-ReleaseTimestamp
    $state["stages"][$Stage]["done"] = $Done
    if ($Done) {
        $state["stages"][$Stage]["at"] = $now
        if ($Detail -ne "") { $state["stages"][$Stage]["detail"] = $Detail }
    } else {
        $state["stages"][$Stage]["at"] = $null
        $state["stages"][$Stage]["detail"] = $null
    }
    $state["updatedAt"] = $now
    Save-ReleaseState -Path $StatePath -State $state
}

# 第 6 步提交完成：记录发布提交并标记 commit 阶段。
function Set-ReleaseCommitRecorded {
    param(
        [Parameter(Mandatory = $true)][string]$StatePath,
        [Parameter(Mandatory = $true)][string]$ReleaseCommit
    )
    $state = Read-ReleaseState -Path $StatePath
    if ($null -eq $state) { throw "找不到发布状态文件：$StatePath" }
    $now = Get-ReleaseTimestamp
    $state["releaseCommit"] = $ReleaseCommit
    $state["stages"]["commit"]["done"] = $true
    $state["stages"]["commit"]["at"] = $now
    $state["stages"]["commit"]["detail"] = $ReleaseCommit
    $state["updatedAt"] = $now
    Save-ReleaseState -Path $StatePath -State $state
}

function Test-ReleaseStageDone {
    param($State, [Parameter(Mandatory = $true)][string]$Stage)
    if ($null -eq $State) { return $false }
    if (-not $State["stages"].Contains($Stage)) { return $false }
    return [bool]$State["stages"][$Stage]["done"]
}

# ---------------------------------------------------------------------------
# 续跑前置校验
# ---------------------------------------------------------------------------

function New-ReleaseResumeFailure {
    param([string]$Code, [string]$Message, [string]$Next)
    return [PSCustomObject]@{ Code = $Code; Message = $Message; Next = $Next }
}

function Get-ReleaseAbandonHint {
    param([string]$ParentCommit)
    $p = $ParentCommit
    if ([string]::IsNullOrEmpty($p)) { $p = "<发布前提交>" }
    return "若要放弃本次发布：git reset --soft $p，再按路径 git restore --staged --worktree 五个版本文件并删 dist\<版本> 产物（AGENTS.md §5 半途恢复流程）。"
}

# 返回 [PSCustomObject]@{ Ok; Failures = 失败项数组（Code/Message/Next）; State = 读到的状态（可能 $null） }。
# $AllowedCommitFiles：发布提交允许包含的文件（build.ps1 第 6 步实际 git add 的版本文件清单，调用方传入同一份）。
function Test-ReleaseResumePreconditions {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string[]]$AllowedCommitFiles
    )
    $failures = New-Object System.Collections.ArrayList
    $statePath = Get-ReleaseStatePath -RepoRoot $RepoRoot -Version $Version
    $rerun = "修好问题后重新运行 build.ps1 -Release $Version（会重跑全量门禁）。"

    $state = $null
    $stateOk = $false
    try {
        $state = Read-ReleaseState -Path $statePath
        if ($null -eq $state) {
            [void]$failures.Add((New-ReleaseResumeFailure -Code "NoState" `
                -Message "找不到全量门禁通过记录：$statePath" `
                -Next ("续跑没有凭据（本次发布没有走到第 5 步通过，或记录已被删除）；" + $rerun)))
        } else {
            $stateOk = $true
        }
    } catch {
        [void]$failures.Add((New-ReleaseResumeFailure -Code "StateUnreadable" `
            -Message "状态文件无法解析：$($_.Exception.Message)" `
            -Next ("记录已损坏，不能作为续跑凭据；" + $rerun)))
    }

    $parentRecorded = ""
    if ($stateOk) {
        $parentRecorded = "$($state['parentCommit'])"
        if ("$($state['version'])" -ne $Version) {
            [void]$failures.Add((New-ReleaseResumeFailure -Code "VersionMismatch" `
                -Message "状态文件记录的版本是 '$($state['version'])'，与 -Release $Version 不一致（文件被改动或放错位置）" `
                -Next ("不要手改状态文件；" + $rerun)))
            $stateOk = $false
        }
    }

    if ($stateOk) {
        $rc = "$($state['releaseCommit'])"
        if ([string]::IsNullOrEmpty($rc)) {
            [void]$failures.Add((New-ReleaseResumeFailure -Code "NoReleaseCommit" `
                -Message "状态文件里没有发布提交（失败发生在第 6 步提交之前，门禁通过记录尚未绑定到提交）" `
                -Next ("续跑只覆盖发布提交之后的失败；" + $rerun + " " + (Get-ReleaseAbandonHint -ParentCommit $parentRecorded))))
        } else {
            $head = Invoke-ReleaseGit -RepoRoot $RepoRoot -GitArgs @("rev-parse", "HEAD")
            $headText = ""
            if ($head.ExitCode -eq 0 -and $head.Out.Count -gt 0) { $headText = $head.Out[0].Trim() }
            if ($headText -ne $rc) {
                [void]$failures.Add((New-ReleaseResumeFailure -Code "HeadMismatch" `
                    -Message "HEAD（$headText）不是状态文件记录的发布提交（$rc）" `
                    -Next ("先把工作树切回发布提交（git log 里找 $rc 所在分支，不要在发布提交之上追加提交）；门禁通过记录只对该提交成立。 " + (Get-ReleaseAbandonHint -ParentCommit $parentRecorded))))
            }

            $exists = Invoke-ReleaseGit -RepoRoot $RepoRoot -GitArgs @("rev-parse", "--verify", "--quiet", ($rc + "^{commit}"))
            if ($exists.ExitCode -eq 0) {
                $par = Invoke-ReleaseGit -RepoRoot $RepoRoot -GitArgs @("rev-parse", ($rc + "^"))
                $parText = ""
                if ($par.ExitCode -eq 0 -and $par.Out.Count -gt 0) { $parText = $par.Out[0].Trim() }
                if ($parText -ne $parentRecorded) {
                    [void]$failures.Add((New-ReleaseResumeFailure -Code "ParentMismatch" `
                        -Message "发布提交 $rc 的父提交是 '$parText'，状态文件记录的发布前提交是 '$parentRecorded'" `
                        -Next ("发布提交不是在门禁测过的那个提交之上做的，门禁通过记录不适用。 " + $rerun)))
                }

                $diff = Invoke-ReleaseGit -RepoRoot $RepoRoot -GitArgs @("diff", "--name-only", ($rc + "^"), $rc)
                if ($diff.ExitCode -ne 0) {
                    [void]$failures.Add((New-ReleaseResumeFailure -Code "CommitFiles" `
                        -Message "无法计算发布提交 $rc 的改动文件清单（git diff 失败）" `
                        -Next ("检查仓库状态后重试；或 " + $rerun)))
                } else {
                    $allowed = @($AllowedCommitFiles | ForEach-Object { ($_ -replace '\\', '/') })
                    $changed = @($diff.Out | Where-Object { $_.Trim() -ne "" } | ForEach-Object { ($_.Trim() -replace '\\', '/') })
                    $extra = @($changed | Where-Object { $allowed -notcontains $_ })
                    if ($extra.Count -gt 0 -or $changed.Count -eq 0) {
                        $shown = @($extra | Select-Object -First 8)
                        $msg = "发布提交 $rc 的改动不只是版本文件：" + ($shown -join ", ")
                        if ($changed.Count -eq 0) { $msg = "发布提交 $rc 没有任何改动文件" }
                        [void]$failures.Add((New-ReleaseResumeFailure -Code "CommitFiles" `
                            -Message $msg `
                            -Next ("允许的文件只有第 6 步 git add 的版本文件：" + ($allowed -join ", ") + "。 " + (Get-ReleaseAbandonHint -ParentCommit $parentRecorded))))
                    }
                }
            } else {
                [void]$failures.Add((New-ReleaseResumeFailure -Code "HeadMismatch" `
                    -Message "状态文件记录的发布提交 $rc 在本仓库里不存在" `
                    -Next ("记录与仓库不匹配（换了仓库/分支被改写？）；" + $rerun)))
            }
        }
    }

    $st = Invoke-ReleaseGit -RepoRoot $RepoRoot -GitArgs @("status", "--porcelain")
    $stLines = @($st.Out | Where-Object { $_.Trim() -ne "" })
    if ($st.ExitCode -ne 0) {
        [void]$failures.Add((New-ReleaseResumeFailure -Code "DirtyTree" `
            -Message "无法读取工作树状态（git status 失败）" -Next "检查仓库后重试。"))
    } elseif ($stLines.Count -gt 0) {
        $shownLines = @($stLines | Select-Object -First 8)
        [void]$failures.Add((New-ReleaseResumeFailure -Code "DirtyTree" `
            -Message ("工作树不干净（git status --porcelain 非空）：" + ($shownLines -join " | ")) `
            -Next "续跑要求工作树干净（发布快照不能夹带未提交改动）：提交或清理这些改动后再 -Resume。"))
    }

    $versionPath = Join-Path $RepoRoot "VERSION"
    $localVersion = ""
    if (Test-Path -LiteralPath $versionPath) {
        $localVersion = (Get-Content -LiteralPath $versionPath -Raw).Trim()
    }
    if ($localVersion -ne $Version) {
        [void]$failures.Add((New-ReleaseResumeFailure -Code "LocalVersionMismatch" `
            -Message "本地 VERSION 是 '$localVersion'，不等于 -Release $Version" `
            -Next "VERSION 应在第 6 步提交里已是目标版本；确认当前检出的是发布提交（见 HEAD 校验项）。"))
    }

    return [PSCustomObject]@{ Ok = ($failures.Count -eq 0); Failures = @($failures.ToArray()); State = $state }
}

# ---------------------------------------------------------------------------
# 续跑计划
# ---------------------------------------------------------------------------

# 返回：Stages（每阶段 Id/Required/Done）、NeedPackaging（打包+自检需要（重）跑）、FirstUnfinished（首个未完成的
# 必需阶段，全部完成为 $null）、Inconsistent（状态自相矛盾的说明，空串=无）。
# 必需阶段：gate/commit/packaging/selfCheck/tag 恒必需；registry:* 仅传 -PublishRegistry；push/githubRelease
# 仅传 -Publish。打包与自检视为一组（判断记录 4）：selfCheck 未完成则 packaging 一律视为未完成。
function Get-ReleaseResumePlan {
    param(
        [Parameter(Mandatory = $true)]$State,
        [Parameter(Mandatory = $true)][string[]]$PackageNames,
        [switch]$PublishRegistry,
        [switch]$Publish
    )
    $rows = @()
    $packagingDone = Test-ReleaseStageDone -State $State -Stage "packaging"
    $selfCheckDone = Test-ReleaseStageDone -State $State -Stage "selfCheck"
    $needPackaging = (-not ($packagingDone -and $selfCheckDone))
    foreach ($id in (Get-ReleaseStageIds -PackageNames $PackageNames)) {
        $required = $false
        if (@("gate", "commit", "packaging", "selfCheck", "tag") -contains $id) { $required = $true }
        elseif ($id.StartsWith("registry:")) { $required = [bool]$PublishRegistry }
        else { $required = [bool]$Publish }
        $done = Test-ReleaseStageDone -State $State -Stage $id
        if ($needPackaging -and ($id -eq "packaging" -or $id -eq "selfCheck")) { $done = $false }
        $rows += [PSCustomObject]@{ Id = $id; Required = $required; Done = $done }
    }
    $first = $null
    foreach ($r in $rows) {
        if ($r.Required -and (-not $r.Done)) { $first = $r.Id; break }
    }
    $inconsistent = ""
    if ($needPackaging) {
        $later = @($rows | Where-Object { $_.Id -ne "gate" -and $_.Id -ne "commit" -and $_.Id -ne "packaging" -and $_.Id -ne "selfCheck" -and (Test-ReleaseStageDone -State $State -Stage $_.Id) })
        if ($later.Count -gt 0) {
            $inconsistent = "状态文件自相矛盾：打包/自检未完成，但后续阶段已标记完成（" + (($later | ForEach-Object { $_.Id }) -join ", ") + "）"
        }
    }
    return [PSCustomObject]@{
        Stages          = @($rows)
        NeedPackaging   = $needPackaging
        FirstUnfinished = $first
        Inconsistent    = $inconsistent
    }
}

# ---------------------------------------------------------------------------
# 产物路径（与 build.ps1 打包节使用的路径一致；续跑在"打包已完成"时直接用这份，不重新进入打包节）
# ---------------------------------------------------------------------------

function Get-ReleaseArtifactPaths {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Version
    )
    $packagesRoot = Join-Path $RepoRoot ("dist\" + $Version + "\packages")
    $dirs = @()
    foreach ($n in (Get-ReleasePackageNames -RepoRoot $RepoRoot)) { $dirs += (Join-Path $packagesRoot $n) }
    return [PSCustomObject]@{
        ZipPath          = (Join-Path $RepoRoot ("dist\ws-game-" + $Version + ".zip"))
        LockPath         = (Join-Path $RepoRoot ("dist\ws-game-" + $Version + ".lock"))
        SamplesZipPath   = (Join-Path $RepoRoot ("dist\ws-game-" + $Version + "-samples.zip"))
        GetFrameworkPath = (Join-Path $RepoRoot "toolchain\get_framework.ps1")
        HashPsPath       = (Join-Path $RepoRoot "toolchain\_hash.ps1")
        NotesPath        = (Join-Path $RepoRoot ("dist\release-notes-" + $Version + ".txt"))
        PackagesRoot     = $packagesRoot
        PackageDirs      = @($dirs)
    }
}

# ---------------------------------------------------------------------------
# 标签
# ---------------------------------------------------------------------------

# Status：Absent（本地无此标签）/ AtHead（指向 HEAD）/ Elsewhere（指向别的提交）。
function Get-ReleaseTagStatus {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Tag
    )
    $head = Invoke-ReleaseGit -RepoRoot $RepoRoot -GitArgs @("rev-parse", "HEAD")
    if ($head.ExitCode -ne 0 -or $head.Out.Count -eq 0) { throw "无法解析当前 HEAD（git rev-parse HEAD 失败）" }
    $headText = $head.Out[0].Trim()
    $tagRef = Invoke-ReleaseGit -RepoRoot $RepoRoot -GitArgs @("rev-parse", "--verify", "--quiet", ("refs/tags/" + $Tag + "^{commit}"))
    if ($tagRef.ExitCode -ne 0 -or $tagRef.Out.Count -eq 0) {
        return [PSCustomObject]@{ Status = "Absent"; TagCommit = ""; Head = $headText }
    }
    $tagCommit = $tagRef.Out[0].Trim()
    $status = "Elsewhere"
    if ($tagCommit -eq $headText) { $status = "AtHead" }
    return [PSCustomObject]@{ Status = $status; TagCommit = $tagCommit; Head = $headText }
}

function Invoke-ReleaseTagStage {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$ChangelogSection,
        [Parameter(Mandatory = $true)][string]$StatePath,
        [switch]$Resume
    )
    $tagName = "v$Version"
    if ($Resume) {
        $st = Get-ReleaseTagStatus -RepoRoot $RepoRoot -Tag $tagName
        if ($st.Status -eq "AtHead") {
            Write-Host "  标签 $tagName 已存在且指向当前 HEAD（$($st.Head)）：跳过打标签"
            Set-ReleaseStage -StatePath $StatePath -Stage "tag" -Detail "已存在，指向 HEAD"
            return
        }
        if ($st.Status -eq "Elsewhere") {
            throw "标签 $tagName 已存在但指向 $($st.TagCommit)，不是当前 HEAD（$($st.Head)）：拒绝续跑（不移动、不删除已有标签）。请人工核对标签来历；若要放弃本次发布，先确认该标签从未推送，再自行处理标签。"
        }
    }
    Push-Location $RepoRoot
    try {
        $tagMessageFile = Join-Path $RepoRoot ("dist\tag-message-" + $Version + ".txt")
        $tagMessageContent = "$tagName`n`n$ChangelogSection"
        [System.IO.File]::WriteAllText($tagMessageFile, $tagMessageContent, (New-Object System.Text.UTF8Encoding($false)))
        & git tag -a $tagName -F $tagMessageFile
        if ($LASTEXITCODE -ne 0) { throw "git tag 失败，退出码 $LASTEXITCODE" }
        Remove-Item -Path $tagMessageFile -Force -ErrorAction SilentlyContinue
        Write-Host "  已打标签：$tagName"
    } finally {
        Pop-Location
    }
    Set-ReleaseStage -StatePath $StatePath -Stage "tag" -Detail "已创建"
}

# ---------------------------------------------------------------------------
# 私服
# ---------------------------------------------------------------------------

# npm 风格的 tarball 摘要：Integrity = "sha512-<base64>"，Shasum = sha1 十六进制（与 npm pack/publish 同口径）。
function Get-NpmTarballIntegrity {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "找不到 $Path" }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $sha512 = [System.Security.Cryptography.SHA512]::Create()
    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    try {
        $integrity = "sha512-" + [Convert]::ToBase64String($sha512.ComputeHash($bytes))
        $shasum = (($sha1.ComputeHash($bytes) | ForEach-Object { $_.ToString("x2") }) -join "")
    } finally {
        $sha512.Dispose()
        $sha1.Dispose()
    }
    return [PSCustomObject]@{ Integrity = $integrity; Shasum = $shasum }
}

# 查私服里 <包>@<版本> 的 dist。Found=$false 仅限 npm 报 E404；其它错误（连不上、鉴权失败…）抛异常。
function Get-RegistryPackageDist {
    param(
        [Parameter(Mandatory = $true)][string]$PackageName,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$RegistryUrl,
        [Parameter(Mandatory = $true)][string]$NpmrcPath
    )
    $r = Invoke-ReleaseNative -Exe "npm" -NativeArgs @("view", "$PackageName@$Version", "dist", "--json", "--registry", $RegistryUrl, "--userconfig", $NpmrcPath)
    $text = ($r.Out -join "`n").Trim()
    $obj = $null
    if ($text -ne "") {
        try { $obj = $text | ConvertFrom-Json } catch { $obj = $null }
    }
    if ($r.ExitCode -eq 0 -and $null -ne $obj -and ($obj.PSObject.Properties.Name -contains "integrity" -or $obj.PSObject.Properties.Name -contains "shasum")) {
        $integrity = ""
        $shasum = ""
        if ($obj.PSObject.Properties.Name -contains "integrity") { $integrity = "$($obj.integrity)" }
        if ($obj.PSObject.Properties.Name -contains "shasum") { $shasum = "$($obj.shasum)" }
        return [PSCustomObject]@{ Found = $true; Integrity = $integrity; Shasum = $shasum }
    }
    if ($null -ne $obj -and ($obj.PSObject.Properties.Name -contains "error") -and "$($obj.error.code)" -eq "E404") {
        return [PSCustomObject]@{ Found = $false; Integrity = ""; Shasum = "" }
    }
    $detail = $text
    if ($detail -eq "") { $detail = ($r.Err -join " ") }
    throw "查询私服失败：npm view $PackageName@$Version（退出码 $($r.ExitCode)）：$detail。无法确认该版本是否已发布，中止（不把'查询失败'当作'没发布过'）。"
}

# Local：Get-NpmTarballIntegrity 的结果；Remote：Get-RegistryPackageDist 的结果。
# 返回 Publish（私服没有）/ SkipIdentical（已有且内容一致）/ RefuseDifferent（已有但内容不同）。
function Get-RegistryPublishDecision {
    param(
        [Parameter(Mandatory = $true)]$Local,
        [Parameter(Mandatory = $true)]$Remote
    )
    if (-not $Remote.Found) { return "Publish" }
    $tokens = @(("$($Remote.Integrity)" -split '\s+') | Where-Object { $_ -ne "" })
    $remoteSha512 = @($tokens | Where-Object { $_.StartsWith("sha512-") })
    if ($remoteSha512.Count -gt 0) {
        if ($remoteSha512 -contains $Local.Integrity) { return "SkipIdentical" }
        return "RefuseDifferent"
    }
    if ("$($Remote.Shasum)" -ne "" -and "$($Remote.Shasum)" -eq "$($Local.Shasum)") { return "SkipIdentical" }
    return "RefuseDifferent"
}

function Resolve-ReleaseRegistry {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$RegistryUrl = ""
    )
    $resolvedRegistryUrl = $RegistryUrl
    if ($resolvedRegistryUrl -eq "") {
        $registryJsonPath = Join-Path $RepoRoot "toolchain\registry\registry.json"
        if (-not (Test-Path $registryJsonPath)) {
            throw "找不到 $registryJsonPath，且未显式传 -RegistryUrl"
        }
        $resolvedRegistryUrl = ((Get-Content -Path $registryJsonPath -Raw -Encoding UTF8) | ConvertFrom-Json).url
    }
    $registryNpmrcPath = Join-Path $RepoRoot "toolchain\registry\.npmrc"
    if (-not (Test-Path $registryNpmrcPath)) {
        throw "找不到 $registryNpmrcPath（先跑 toolchain/registry/init_publisher.ps1 无人值守生成发布账号令牌）"
    }
    return [PSCustomObject]@{ Url = $resolvedRegistryUrl; NpmrcPath = $registryNpmrcPath }
}

function Invoke-ReleaseRegistryStage {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string[]]$PackageDirs,
        [string]$RegistryUrl = "",
        [Parameter(Mandatory = $true)][string]$StatePath,
        [switch]$Resume
    )
    $reg = Resolve-ReleaseRegistry -RepoRoot $RepoRoot -RegistryUrl $RegistryUrl
    $resolvedRegistryUrl = $reg.Url
    $registryNpmrcPath = $reg.NpmrcPath

    foreach ($pkgDirForPublish in $PackageDirs) {
        $pkgName = Split-Path -Leaf $pkgDirForPublish
        $stageId = "registry:" + $pkgName
        $tgzPath = Join-Path (Split-Path -Parent $pkgDirForPublish) ($pkgName + "-" + $Version + ".tgz")
        if ($Resume) {
            $state = Read-ReleaseState -Path $StatePath
            if (Test-ReleaseStageDone -State $state -Stage $stageId) {
                Write-Host "  状态文件已记录 $pkgName@$Version 发布完成：跳过 npm publish"
                continue
            }
            if (-not (Test-Path -LiteralPath $tgzPath)) {
                throw "找不到本地包 $tgzPath，无法与私服上已有的 $pkgName@$Version 比对（打包产物不完整？）"
            }
            $local = Get-NpmTarballIntegrity -Path $tgzPath
            $remote = Get-RegistryPackageDist -PackageName $pkgName -Version $Version -RegistryUrl $resolvedRegistryUrl -NpmrcPath $registryNpmrcPath
            $decision = Get-RegistryPublishDecision -Local $local -Remote $remote
            if ($decision -eq "SkipIdentical") {
                Write-Host "  私服已有 $pkgName@$Version 且内容一致（$($local.Integrity)）：跳过 npm publish"
                Set-ReleaseStage -StatePath $StatePath -Stage $stageId -Detail ("私服已有，内容一致 " + $local.Integrity)
                continue
            }
            if ($decision -eq "RefuseDifferent") {
                throw "私服已有 $pkgName@$Version，但内容与本地包不一致（私服 integrity=$($remote.Integrity)，本地 $tgzPath integrity=$($local.Integrity)）：拒绝覆盖已发布的包（发布不可变）。请人工核对来历；确需发新内容请发新版本号。"
            }
        }
        Write-Host "  npm publish $pkgDirForPublish --registry $resolvedRegistryUrl"
        & npm publish $pkgDirForPublish --registry $resolvedRegistryUrl --userconfig $registryNpmrcPath
        if ($LASTEXITCODE -ne 0) {
            throw "npm publish 失败：$pkgDirForPublish（退出码 $LASTEXITCODE；若原因是版本号已存在，说明该版本已经发布过，符合'发布不可变'，请发新版本号而不是覆盖）"
        }
        $detail = "已发布"
        if (Test-Path -LiteralPath $tgzPath) { $detail = "已发布 " + (Get-NpmTarballIntegrity -Path $tgzPath).Integrity }
        Set-ReleaseStage -StatePath $StatePath -Stage $stageId -Detail $detail
    }
    Write-Host "  已发布四个包 version=$Version 到 $resolvedRegistryUrl" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# 推送
# ---------------------------------------------------------------------------

# Status：AlreadyPushed（远端标签与远端分支都已在 HEAD）/ TagElsewhere（远端标签指向别处）/ NeedPush（其余）。
function Get-ReleaseRemoteStatus {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Branch,
        [string]$Remote = "origin"
    )
    $head = Invoke-ReleaseGit -RepoRoot $RepoRoot -GitArgs @("rev-parse", "HEAD")
    if ($head.ExitCode -ne 0 -or $head.Out.Count -eq 0) { throw "无法解析当前 HEAD（git rev-parse HEAD 失败）" }
    $headText = $head.Out[0].Trim()
    $ls = Invoke-ReleaseGit -RepoRoot $RepoRoot -GitArgs @("ls-remote", $Remote, ("refs/tags/" + $Tag), ("refs/tags/" + $Tag + "^{}"), ("refs/heads/" + $Branch))
    if ($ls.ExitCode -ne 0) {
        throw "无法读取远端 $Remote 的状态（git ls-remote 退出码 $($ls.ExitCode)）：$($ls.Err -join ' ')。无法确认是否已推送，中止。"
    }
    $tagObj = ""
    $tagPeeled = ""
    $branchSha = ""
    foreach ($line in $ls.Out) {
        $parts = @($line.Trim() -split '\s+')
        if ($parts.Count -lt 2) { continue }
        $sha = $parts[0]
        $ref = $parts[1]
        if ($ref -eq ("refs/tags/" + $Tag)) { $tagObj = $sha }
        elseif ($ref -eq ("refs/tags/" + $Tag + "^{}")) { $tagPeeled = $sha }
        elseif ($ref -eq ("refs/heads/" + $Branch)) { $branchSha = $sha }
    }
    # 轻量标签没有 ^{} 行，标签对象本身就是提交；带注释标签以剥离后的提交为准。
    $remoteTagCommit = $tagPeeled
    if ($remoteTagCommit -eq "") { $remoteTagCommit = $tagObj }
    $status = "NeedPush"
    if ($remoteTagCommit -ne "" -and $remoteTagCommit -ne $headText) {
        $status = "TagElsewhere"
    } elseif ($remoteTagCommit -eq $headText -and $branchSha -eq $headText) {
        $status = "AlreadyPushed"
    }
    return [PSCustomObject]@{ Status = $status; Head = $headText; RemoteTagCommit = $remoteTagCommit; RemoteBranchCommit = $branchSha }
}

function Invoke-ReleasePushStage {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Branch,
        [Parameter(Mandatory = $true)][string]$StatePath,
        [switch]$Resume
    )
    $tagName = "v$Version"
    $pushCmd = "git push origin $Branch refs/tags/$tagName"
    if ($Resume) {
        $rs = Get-ReleaseRemoteStatus -RepoRoot $RepoRoot -Tag $tagName -Branch $Branch
        if ($rs.Status -eq "AlreadyPushed") {
            Write-Host "  远端 origin 已有标签 $tagName 且 $Branch 已在 HEAD（$($rs.Head)）：跳过推送"
            Set-ReleaseStage -StatePath $StatePath -Stage "push" -Detail "远端已就绪"
            return
        }
        if ($rs.Status -eq "TagElsewhere") {
            throw "远端 origin 已有标签 $tagName，但指向 $($rs.RemoteTagCommit)，不是当前 HEAD（$($rs.Head)）：拒绝推送（不覆盖远端标签）。请人工核对。"
        }
    }
    Push-Location $RepoRoot
    try {
        Write-Host "  执行：$pushCmd"
        & git push origin $Branch "refs/tags/$tagName"
        if ($LASTEXITCODE -ne 0) { throw "git push 失败，退出码 $LASTEXITCODE" }
    } finally {
        Pop-Location
    }
    Set-ReleaseStage -StatePath $StatePath -Stage "push" -Detail "已推送"
}

# ---------------------------------------------------------------------------
# GitHub Release
# ---------------------------------------------------------------------------

# 已有附件（名 -> 大小，大小未知为 -1）对照应有附件（本地文件路径）。
# Action：Create（Release 不存在，调用方传 $null）/ Skip（附件齐全）/ Upload（有缺，Missing 为要补传的本地路径）
# / Refuse（同名附件大小与本地文件不符，Mismatched 列出附件名）。
function Get-ReleaseGitHubPlan {
    param(
        $ExistingAssets,
        [Parameter(Mandatory = $true)][string[]]$LocalPaths
    )
    if ($null -eq $ExistingAssets) {
        return [PSCustomObject]@{ Action = "Create"; Missing = @($LocalPaths); Mismatched = @() }
    }
    $missing = @()
    $mismatched = @()
    foreach ($p in $LocalPaths) {
        $name = Split-Path -Leaf $p
        if (-not $ExistingAssets.ContainsKey($name)) { $missing += $p; continue }
        $remoteSize = [long]$ExistingAssets[$name]
        if ($remoteSize -ge 0 -and (Test-Path -LiteralPath $p)) {
            $localSize = (Get-Item -LiteralPath $p).Length
            if ($localSize -ne $remoteSize) { $mismatched += $name }
        }
    }
    $action = "Skip"
    if ($mismatched.Count -gt 0) { $action = "Refuse" }
    elseif ($missing.Count -gt 0) { $action = "Upload" }
    return [PSCustomObject]@{ Action = $action; Missing = @($missing); Mismatched = @($mismatched) }
}

# 返回 $null（Release 不存在）或 名->大小 的哈希表；其它 gh 错误抛异常。
function Get-GitHubReleaseAssets {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Tag
    )
    Push-Location $RepoRoot
    try {
        $r = Invoke-ReleaseNative -Exe "gh" -NativeArgs @("release", "view", $Tag, "--json", "assets")
    } finally {
        Pop-Location
    }
    if ($r.ExitCode -ne 0) {
        $errText = (($r.Err + $r.Out) -join " ")
        if ($errText -match '(?i)release not found|not found') { return $null }
        throw "gh release view $Tag 失败（退出码 $($r.ExitCode)）：$errText。无法确认 Release 是否已存在，中止。"
    }
    $text = ($r.Out -join "`n").Trim()
    $obj = $text | ConvertFrom-Json
    $map = @{}
    foreach ($a in @($obj.assets)) {
        $size = -1
        if ($a.PSObject.Properties.Name -contains "size") { $size = [long]$a.size }
        $map["$($a.name)"] = $size
    }
    return $map
}

function Invoke-ReleaseGitHubStage {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string[]]$AssetPaths,
        [Parameter(Mandatory = $true)][string]$NotesPath,
        [Parameter(Mandatory = $true)][string]$StatePath,
        [switch]$Resume
    )
    $tagName = "v$Version"
    $plan = $null
    if ($Resume) {
        $existing = Get-GitHubReleaseAssets -RepoRoot $RepoRoot -Tag $tagName
        $plan = Get-ReleaseGitHubPlan -ExistingAssets $existing -LocalPaths $AssetPaths
        if ($plan.Action -eq "Skip") {
            Write-Host "  GitHub Release $tagName 已存在且附件齐全：跳过"
            Set-ReleaseStage -StatePath $StatePath -Stage "githubRelease" -Detail "已存在，附件齐全"
            return
        }
        if ($plan.Action -eq "Refuse") {
            throw "GitHub Release $tagName 上已有同名附件但大小与本地文件不符：$($plan.Mismatched -join ', ')：拒绝覆盖（不带 --clobber）。请人工核对。"
        }
    }
    Push-Location $RepoRoot
    try {
        if ($null -ne $plan -and $plan.Action -eq "Upload") {
            Write-Host "  GitHub Release $tagName 已存在，只补传缺失附件：$(($plan.Missing | ForEach-Object { Split-Path -Leaf $_ }) -join ', ')"
            $ghUploadArgs = @("release", "upload", $tagName) + @($plan.Missing)
            & gh @ghUploadArgs
            if ($LASTEXITCODE -ne 0) { throw "gh release upload 失败，退出码 $LASTEXITCODE" }
            Set-ReleaseStage -StatePath $StatePath -Stage "githubRelease" -Detail ("已补传 " + (($plan.Missing | ForEach-Object { Split-Path -Leaf $_ }) -join ", "))
        } else {
            $ghCreateArgs = @("release", "create", $tagName) + @($AssetPaths) + @("--title", $tagName, "--notes-file", $NotesPath)
            Write-Host "  执行：gh release create $tagName（$(@($AssetPaths).Count) 个附件）--title $tagName --notes-file $NotesPath"
            & gh @ghCreateArgs
            if ($LASTEXITCODE -ne 0) { throw "gh release create 失败，退出码 $LASTEXITCODE" }
            Set-ReleaseStage -StatePath $StatePath -Stage "githubRelease" -Detail "已创建"
        }
    } finally {
        Pop-Location
    }
}
