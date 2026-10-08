<#
.SYNOPSIS
    发布候选阶段（ADR-0160，2026-10-06）：`build.ps1 -Release X` 在打标签之前，先把 `X-rc.N` 发到本地私服，
    让样板仓库（`ws-game-samples`）升级到这个候选版本并跑完它自己的全量门禁；绿才继续打标签/发布，红就在打标签前停下。

.DESCRIPTION
    判断记录（不另开 ADR，决定与理由登记在 ADR-0160 与 toolchain/README.md"发布候选"一节；这里是实现层的细节判断）：

    1) 为什么要有候选阶段：框架拆出样板仓库之后，样板只依赖框架的已发布版本；框架改动可能让样板编译不过或断言变红，
       而样板在框架之外（不进框架门禁）。发布前不先让样板验一遍，红了只能在版本发出去之后才发现，而已发布版本不可变。
       候选版本号 `X.Y.Z-rc.N` 是合法 semver 预发布标识，只发到本地私服（不打标签、不推送、不建 GitHub Release），
       用 `npm publish --tag rc` 发布，不会占用 `latest` 标签，也不会让只装稳定版的消费方升级到它。

    2) 阶段位置：发布提交（commit）之后、打包（packaging）之前。理由：此时版本号已写回并提交，候选包的内容就是最终
       发布包的内容（只有 package.json 里的 version 不同）；候选失败时发布提交与门禁通过记录原样保留，修好后
       `-Resume` 续跑，不重跑全量门禁。阶段标识 `candidate`，见 `_release_resume.ps1` 的 `Get-ReleaseStageIds`。

    3) rc 序号：每次进入候选阶段都取 `1 + 私服里该版本已有的最大 rc 序号`（逐包查询取最大）。续跑重发一个新的 rc.N+1，
       绝不覆盖旧候选（私服包不可变，且旧候选的内容已经被判红）。

    4) 样板仓库缺失：默认拒绝发布并说明原因；只有显式传 `-SkipSamplesCandidate` 才放行，并且放行本身被记录——
       控制台打醒目警告、阶段完成标记的说明里写 `OPT-OUT`，状态文件随发布留痕。不提供"静默跳过"。

    5) 样板仓库位置：`-SamplesRepo` 显式给定 > 环境变量 `WS_GAME_SAMPLES` > 主工作树同级目录 `ws-game-samples`
       （从 worktree 里发布时 `git rev-parse --git-common-dir` 指向主仓库，以主仓库为准）。
       样板仓库必须有 `check.ps1`（它自己的门禁）与 `tools/upgrade_framework.ps1`（升级到指定框架版本）。

    6) 样板的升级改动绝不落在样板主工作树（2026-10-09 决定，ADR-0160 补注）：主工作树是用户的试玩目录（Unity 工程打开着、
       有未提交的编辑器改动），此前候选阶段直接在它上面把 manifest 改成 X.Y.Z-rc.N 且不提交，试玩目录被留在一个候选版本上。
       现在候选阶段在样板仓库的**临时工作树**里做升级与样板门禁：`git -C <样板仓库> worktree add --detach <D:\wt\samples-…> HEAD`
       （基线 = 样板仓库当前 HEAD 的已提交状态；根目录用 `-SamplesWorktreeRoot`，缺省环境变量 `WS_GAME_WT_ROOT`，再缺省 `D:\wt`，
       短路径是为了 Unity 批处理不触 MAX_PATH）。门禁绿 -> 移除临时工作树；红 -> 保留现场供排查（消息里给出路径与清理命令）。
       `-SamplesRepo` 若本身就是一个**已关联的工作树**（`.git` 是文件而不是目录），则直接用它、不再另建（用于预热过 Library 的
       长期发布门禁工作树）；若指向主工作树（`.git` 是目录）则一律走临时工作树。阶段前后各取一次样板主工作树的 HEAD 与
       `git status` 快照，不一致直接失败（兜底：升级脚本或样板门禁被改坏时不能悄悄弄脏试玩目录）。
       门禁绿时的"升到正式版"由发布后收尾在样板仓库里提交，不依赖这里的改动。

    7) 所有外部动作（构建、发布、查询、升级、跑样板门禁）都通过可注入的脚本块执行，
       供 `toolchain/tests/test_release_candidate.py` 用伪造的实现覆盖成功/各种失败路径，不依赖私服、Unity 与样板仓库。

    独立成文件（同目录 `_release_resume.ps1` 同一模式），供 `build.ps1` 与测试 dot-source；必须先 dot-source
    `_release_resume.ps1`（用到它的状态文件与私服函数）。含中文，UTF-8 带 BOM。
#>

$script:CandidateRcPattern = '^(\d+\.\d+\.\d+)-rc\.(\d+)$'

# 纯函数：已有版本号列表里属于 $Version 的 rc 序号最大值 + 1；没有则 1。非 rc 版本、别的版本的 rc 一律忽略。
function Get-NextCandidateVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [AllowEmptyCollection()][string[]]$ExistingVersions = @()
    )
    $max = 0
    foreach ($v in @($ExistingVersions)) {
        if ($v -match $script:CandidateRcPattern -and $Matches[1] -eq $Version) {
            $n = [int]$Matches[2]
            if ($n -gt $max) { $max = $n }
        }
    }
    return ("{0}-rc.{1}" -f $Version, ($max + 1))
}

# 纯函数：把 npm view versions --json 的输出（单个字符串、数组或空）转成字符串数组。
function ConvertFrom-NpmVersionsJson {
    param([AllowEmptyString()][string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return @() }
    $obj = $null
    try { $obj = $Text | ConvertFrom-Json } catch { return @() }
    if ($null -eq $obj) { return @() }
    if ($obj -is [string]) { return @($obj) }
    if ($obj.PSObject.Properties.Name -contains "error") { return @() }
    return @($obj | ForEach-Object { "$_" })
}

# 样板仓库路径：显式 > 环境变量 > 主工作树同级 ws-game-samples。返回绝对路径（不核对是否存在）。
function Resolve-SamplesRepoPath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$Explicit = "",
        [string]$EnvValue = ""
    )
    if ($Explicit -ne "") { return [System.IO.Path]::GetFullPath($Explicit) }
    if ($EnvValue -ne "") { return [System.IO.Path]::GetFullPath($EnvValue) }
    $mainRoot = $RepoRoot
    $common = Invoke-ReleaseNative -Exe "git" -NativeArgs @("-C", $RepoRoot, "rev-parse", "--path-format=absolute", "--git-common-dir")
    if ($common.ExitCode -eq 0 -and $common.Out.Count -gt 0) {
        $gitDir = $common.Out[0].Trim()
        if ($gitDir -ne "" -and (Split-Path -Leaf $gitDir) -eq ".git") { $mainRoot = Split-Path -Parent $gitDir }
    }
    return [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $mainRoot) "ws-game-samples"))
}

# 样板仓库就绪判定：返回问题清单（空 = 就绪）。
function Get-SamplesRepoProblems {
    param([Parameter(Mandatory = $true)][string]$SamplesRepo)
    $problems = @()
    if (-not (Test-Path -LiteralPath $SamplesRepo -PathType Container)) {
        return @("样板仓库目录不存在：$SamplesRepo")
    }
    if (-not (Test-Path -LiteralPath (Join-Path $SamplesRepo ".git"))) { $problems += "不是 git 仓库（缺 .git）：$SamplesRepo" }
    if (-not (Test-Path -LiteralPath (Join-Path $SamplesRepo "check.ps1"))) { $problems += "缺样板门禁脚本：$(Join-Path $SamplesRepo 'check.ps1')" }
    if (-not (Test-Path -LiteralPath (Join-Path $SamplesRepo "tools\upgrade_framework.ps1"))) { $problems += "缺升级脚本：$(Join-Path $SamplesRepo 'tools\upgrade_framework.ps1')" }
    return @($problems)
}

# 原生命令的输出逐行回显到控制台（不进返回值），stderr 合并显示但不触发 $ErrorActionPreference=Stop 的终止错误；返回退出码。
function Invoke-CandidateNativeStreaming {
    param(
        [Parameter(Mandatory = $true)][string]$Exe,
        [string[]]$NativeArgs = @()
    )
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $Exe @NativeArgs 2>&1 | ForEach-Object { Write-Host "$_" }
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prev
    }
    return $code
}

# 样板仓库路径本身是不是"已关联的工作树"（.git 是文件而不是目录）。主工作树的 .git 是目录。
function Test-SamplesPathIsLinkedWorktree {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Test-Path -LiteralPath (Join-Path $Path ".git") -PathType Leaf)
}

# 临时工作树根目录：显式 > 环境变量 WS_GAME_WT_ROOT > D:\wt。
function Resolve-SamplesWorktreeRoot {
    param([string]$Explicit = "", [string]$EnvValue = "")
    if ($Explicit -ne "") { return [System.IO.Path]::GetFullPath($Explicit) }
    if ($EnvValue -ne "") { return [System.IO.Path]::GetFullPath($EnvValue) }
    return "D:\wt"
}

# 样板主工作树的状态快照（HEAD + 未提交/未跟踪改动清单）；不是可用的 git 仓库时返回 $null（不做对比）。
function Get-SamplesMainSnapshot {
    param([Parameter(Mandatory = $true)][string]$SamplesRepo)
    $head = Invoke-ReleaseNative -Exe "git" -NativeArgs @("-C", $SamplesRepo, "rev-parse", "HEAD")
    if ($head.ExitCode -ne 0) { return $null }
    $status = Invoke-ReleaseNative -Exe "git" -NativeArgs @("-C", $SamplesRepo, "status", "--porcelain=v1", "--untracked-files=normal")
    if ($status.ExitCode -ne 0) { return $null }
    return ("HEAD " + ($head.Out -join "") + "`n" + ($status.Out -join "`n"))
}

# 执行候选阶段。成功返回 [PSCustomObject]@{ Status = "Passed"/"OptOut"/"AlreadyDone"; Candidate; Detail }；失败 throw（调用方让发布流程终止）。
function Invoke-ReleaseCandidateStage {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$StatePath,
        [string]$SamplesRepo = "",
        [string]$RegistryUrl = "",
        [string]$NpmrcPath = "",
        [string]$SamplesWorktreeRoot = "",
        [switch]$SkipSamplesCandidate,
        [switch]$Resume,
        # 注入点（默认实现见下）。ListVersions: param($pkg) -> string[]；BuildPackages: param($rc) -> 候选包所在的 dist 目录（含 packages\）；
        # PublishPackage: param($pkgDir, $rc) -> 发布失败 throw；UpgradeSamples: param($samples, $rc, $registryUrl)；
        # RunSamplesGate: param($samples) -> @{ ExitCode; Conclusion }（UpgradeSamples/RunSamplesGate 收到的是**临时工作树**路径，不是样板主工作树）；
        # CreateSamplesWorktree: param($samplesRepo, $rc, $root) -> 新建的临时工作树路径；RemoveSamplesWorktree: param($samplesRepo, $path)。
        [scriptblock]$ListVersions = $null,
        [scriptblock]$BuildPackages = $null,
        [scriptblock]$PublishPackage = $null,
        [scriptblock]$UpgradeSamples = $null,
        [scriptblock]$RunSamplesGate = $null,
        [scriptblock]$CreateSamplesWorktree = $null,
        [scriptblock]$RemoveSamplesWorktree = $null
    )

    if ($Resume) {
        $state = Read-ReleaseState -Path $StatePath
        if (Test-ReleaseStageDone -State $state -Stage "candidate") {
            Write-Host "  状态文件已记录候选阶段完成：跳过（$($state['stages']['candidate']['detail'])）"
            return [PSCustomObject]@{ Status = "AlreadyDone"; Candidate = ""; Detail = "$($state['stages']['candidate']['detail'])" }
        }
    }

    if ($SkipSamplesCandidate) {
        Write-Host ""
        Write-Host "!!!! -SkipSamplesCandidate：本次发布跳过样板仓库候选验证（OPT-OUT，已记入发布状态文件）。样板可能在新版本上已经变红而没人知道。 !!!!" -ForegroundColor Yellow
        $detail = "OPT-OUT -SkipSamplesCandidate：未对样板仓库做候选验证（" + (Get-ReleaseTimestamp) + "）"
        Set-ReleaseStage -StatePath $StatePath -Stage "candidate" -Detail $detail
        return [PSCustomObject]@{ Status = "OptOut"; Candidate = ""; Detail = $detail }
    }

    $samples = Resolve-SamplesRepoPath -RepoRoot $RepoRoot -Explicit $SamplesRepo -EnvValue "$env:WS_GAME_SAMPLES"
    $problems = @(Get-SamplesRepoProblems -SamplesRepo $samples)
    if ($problems.Count -gt 0) {
        throw ("拒绝发布：样板仓库不可用，无法做候选验证（ADR-0160）。" + ($problems -join "；") +
            "。用 -SamplesRepo <路径> 指定样板仓库；确需在没有样板仓库时发布，显式传 -SkipSamplesCandidate（会被记录为 OPT-OUT）。")
    }

    $registryUrlResolved = $RegistryUrl
    $npmrcResolved = $NpmrcPath
    if ($registryUrlResolved -eq "" -or $npmrcResolved -eq "") {
        $reg = Resolve-ReleaseRegistry -RepoRoot $RepoRoot -RegistryUrl $RegistryUrl
        $registryUrlResolved = $reg.Url
        $npmrcResolved = $reg.NpmrcPath
    }
    $packageNames = @(Get-ReleasePackageNames -RepoRoot $RepoRoot)

    if ($null -eq $ListVersions) {
        $ListVersions = {
            param($pkg)
            $r = Invoke-ReleaseNative -Exe "npm" -NativeArgs @("view", $pkg, "versions", "--json", "--registry", $registryUrlResolved, "--userconfig", $npmrcResolved)
            $text = ($r.Out -join "`n").Trim()
            if ($r.ExitCode -ne 0) {
                if ($text -match 'E404' -or (($r.Err -join " ") -match 'E404')) { return @() }
                throw "查询私服版本失败：npm view $pkg versions（退出码 $($r.ExitCode)）：$text $($r.Err -join ' ')。无法确定候选序号，中止。"
            }
            return @(ConvertFrom-NpmVersionsJson -Text $text)
        }
    }
    if ($null -eq $BuildPackages) {
        $BuildPackages = {
            param($rc)
            $buildScript = Join-Path $RepoRoot "build.ps1"
            $code = Invoke-CandidateNativeStreaming -Exe "powershell" -NativeArgs @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $buildScript, "-SkipTests", "-Dist", $rc)
            if ($code -ne 0) { throw "构建候选包失败：build.ps1 -SkipTests -Dist $rc（退出码 $code）" }
            return (Join-Path $RepoRoot ("dist\" + $rc))
        }
    }
    if ($null -eq $PublishPackage) {
        $PublishPackage = {
            param($pkgDir, $rc)
            $code = Invoke-CandidateNativeStreaming -Exe "npm" -NativeArgs @("publish", $pkgDir, "--registry", $registryUrlResolved, "--userconfig", $npmrcResolved, "--tag", "rc")
            if ($code -ne 0) { throw "发布候选包失败：npm publish $pkgDir（退出码 $code）" }
        }
    }
    if ($null -eq $UpgradeSamples) {
        $UpgradeSamples = {
            param($samplesRepo, $rc, $url)
            $script = Join-Path $samplesRepo "tools\upgrade_framework.ps1"
            $code = Invoke-CandidateNativeStreaming -Exe "powershell" -NativeArgs @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $script, "-Version", $rc, "-RegistryUrl", $url)
            if ($code -ne 0) { throw "样板仓库升级到 $rc 失败：$script（退出码 $code）" }
        }
    }
    if ($null -eq $RunSamplesGate) {
        $RunSamplesGate = {
            param($samplesRepo)
            $script = Join-Path $samplesRepo "check.ps1"
            $conclusion = ""
            $prevEap = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            try {
                & powershell -NoProfile -ExecutionPolicy Bypass -File $script 2>&1 | ForEach-Object {
                    Write-Host "$_"
                    if ("$_" -match '^\s*门禁通过') { $conclusion = ("$_").Trim() }
                }
                $gateCode = $LASTEXITCODE
            } finally {
                $ErrorActionPreference = $prevEap
            }
            return [PSCustomObject]@{ ExitCode = $gateCode; Conclusion = $conclusion }
        }
    }

    if ($null -eq $CreateSamplesWorktree) {
        $CreateSamplesWorktree = {
            param($samplesRepo, $rc, $root)
            if (-not (Test-Path -LiteralPath $root)) { New-Item -ItemType Directory -Force -Path $root | Out-Null }
            $path = Join-Path $root ("samples-" + ($rc -replace '[^0-9A-Za-z]+', '-'))
            if (Test-Path -LiteralPath $path) { $path = $path + "-" + (Get-Date -Format "HHmmss") }
            $r = Invoke-ReleaseNative -Exe "git" -NativeArgs @("-C", $samplesRepo, "worktree", "add", "--detach", $path, "HEAD")
            if ($r.ExitCode -ne 0) {
                throw "创建样板临时工作树失败：git -C $samplesRepo worktree add --detach $path HEAD（退出码 $($r.ExitCode)）：$($r.Out -join ' ') $($r.Err -join ' ')"
            }
            return $path
        }
    }
    if ($null -eq $RemoveSamplesWorktree) {
        $RemoveSamplesWorktree = {
            param($samplesRepo, $path)
            $r = Invoke-ReleaseNative -Exe "git" -NativeArgs @("-C", $samplesRepo, "worktree", "remove", "--force", $path)
            if ($r.ExitCode -ne 0) {
                Write-Host "  警告：移除样板临时工作树失败（退出码 $($r.ExitCode)），请手工清理：git -C $samplesRepo worktree remove --force $path" -ForegroundColor Yellow
            }
        }
    }

    $existing = @()
    foreach ($pkg in $packageNames) { $existing += @(& $ListVersions $pkg) }
    $rc = Get-NextCandidateVersion -Version $Version -ExistingVersions @($existing)
    Write-ReleaseStep "候选阶段：发布 $rc 到本地私服，并让样板仓库升级后跑它自己的门禁（ADR-0160）"
    Write-Host "  样板仓库：$samples"
    Write-Host "  私服：$registryUrlResolved；候选版本：$rc（npm 标签 rc，不占 latest）"

    $distDir = & $BuildPackages $rc
    $distDir = @($distDir)[-1]
    foreach ($pkg in $packageNames) {
        $pkgDir = Join-Path (Join-Path "$distDir" "packages") $pkg
        if (-not (Test-Path -LiteralPath $pkgDir)) { throw "候选包目录缺失：$pkgDir（构建候选包没有产出这个包）" }
        Write-Host "  npm publish $pkg@$rc"
        & $PublishPackage $pkgDir $rc
    }

    # 升级与样板门禁一律在临时工作树（或调用方显式给的已关联工作树）里做，样板主工作树不动（判断记录 6）。
    $mainSnapshotBefore = Get-SamplesMainSnapshot -SamplesRepo $samples
    $workSamples = $samples
    $tempWorktree = ""
    if (Test-SamplesPathIsLinkedWorktree -Path $samples) {
        Write-Host "  -SamplesRepo 本身是已关联的工作树：直接使用，不另建临时工作树"
    } else {
        $wtRoot = Resolve-SamplesWorktreeRoot -Explicit $SamplesWorktreeRoot -EnvValue "$env:WS_GAME_WT_ROOT"
        $tempWorktree = "$(@(& $CreateSamplesWorktree $samples $rc $wtRoot)[-1])"
        $workSamples = $tempWorktree
        Write-Host "  样板临时工作树：$workSamples（基于样板仓库当前 HEAD；样板主工作树不会被改动）"
    }

    & $UpgradeSamples $workSamples $rc $registryUrlResolved
    $gate = & $RunSamplesGate $workSamples
    $gate = @($gate)[-1]

    $mainSnapshotAfter = Get-SamplesMainSnapshot -SamplesRepo $samples
    if ($null -ne $mainSnapshotBefore -and $mainSnapshotBefore -ne $mainSnapshotAfter) {
        throw ("候选阶段改动了样板主工作树（HEAD 或未提交改动清单与阶段开始前不同），这违反判断记录 6：主工作树是用户的试玩目录。" +
            "请检查样板仓库 $samples 的 git status，并排查升级脚本/样板门禁为什么写到了主工作树。")
    }

    if ([int]$gate.ExitCode -ne 0) {
        if ($tempWorktree -ne "") { $keep = "样板临时工作树保留作现场：$workSamples（排查完用 git -C $samples worktree remove --force $workSamples 清理）。" }
        else { $keep = "样板工作树保留在升级后的现场：$workSamples。" }
        throw ("样板仓库在候选版本 $rc 上的门禁未通过（退出码 $($gate.ExitCode)），发布在打标签前终止（ADR-0160）。" + $keep +
            "修好问题后用 -Resume 续跑（会发布新的候选版本，不覆盖 $rc，不重跑框架门禁）。")
    }
    if ($tempWorktree -ne "") {
        & $RemoveSamplesWorktree $samples $tempWorktree
    }
    $conclusionText = "$($gate.Conclusion)"
    if ($conclusionText -eq "") { $conclusionText = "样板门禁退出码 0（输出里未捕获到'门禁通过'结论行）" }
    $detail = "$rc；样板仓库门禁通过：$conclusionText"
    Set-ReleaseStage -StatePath $StatePath -Stage "candidate" -Detail $detail
    Write-Host "  候选验证通过：$detail" -ForegroundColor Green
    return [PSCustomObject]@{ Status = "Passed"; Candidate = $rc; Detail = $detail }
}
