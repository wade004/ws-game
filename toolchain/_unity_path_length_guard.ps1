<#
.SYNOPSIS
    Unity 相关门禁步骤开跑前的路径长度快速失败守卫（2026-09-20，Windows MAX_PATH 260 字符限制
    根治）。

    判断记录：在深层 scratchpad 工作树（`git worktree add` 到系统临时目录下，路径本身就比主检出
    深很多）里跑 Unity 测试会踩 Windows 260 字符 MAX_PATH——具体触发点是
    `games/_template/Tests/Runtime/GameTemplateResidentTests.cs` 的
    `ResidentRunner_DatasetRootOverride_LoadsProbeTable_FromOverrideRootOnly` 用例（见该文件
    类型头 2026-09-19 判断记录）：运行期把 `adapters/unity/Assets/StreamingAssets/GameFoundation/
    data/game` 整棵目录树（不含 `.meta`）复制到同级一个新目录
    `gf_test_dataset_root_override_<32 位十六进制 GUID>`——这个目录名比原来的 "game" 长 59 个
    字符，工作树根路径一旦较深，复制出来的文件绝对路径就可能超过 260。更麻烦的是 Mono/.NET 旧式
    路径 API 在这种情况下抛的是 `DirectoryNotFoundException` 而不是 `PathTooLongException`，
    症状会伪装成"目录没建出来"/数据装配失败，很容易被误判成产品缺陷（本仓库已经误判过一次）。

    调用方（`check.ps1` Unity 相关四步 + 消费方演练的入口分支）要在真正调用任何 Unity 批处理之前
    调用 `Test-UnityWorkingTreePathLength`，且不经 `Invoke-CheckStep` 包裹——`Invoke-CheckStep`
    的约定是"任一步骤失败都不会中断后续步骤"，但路径过深是环境性前提问题，一旦成立，后面几步
    Unity 批处理必然全部朝着同一个根因失败，继续跑只是白白耗掉几分钟到十几分钟，所以这里要用
    会终止整个脚本的 `throw`，不能只记一条 FAIL 然后接着跑。

    不硬编码"最长路径是多少字符"：函数实际扫一遍 `data/game` 下最长的相对路径，代入覆盖目录名
    模板重新算一次——`data/game` 下的文件将来增删（加表、改文件名）时这道校验能跟着更新，不会
    因为写死的数字过期而失去保护力。

    独立成本文件（不直接写进 check.ps1 内联，同目录 `_hash.ps1`/`_version_writeback.ps1` 同一
    模式）：只定义函数、无顶层副作用，供 `toolchain/tests/test_unity_path_length_guard.py`
    dot-source 后单独测试，不需要跑完整 `check.ps1`（后者会顺带跑一大批耗时的构建/测试步骤）。

    判断记录（2026-09-20 二次修复，复审发现的缺口）：最初版本只扫
    `adapters/unity/Assets/StreamingAssets/GameFoundation/data/game`——这是 `.gitignore` 显式
    忽略的生成目录（由 `build.ps1 -SyncOnly` 从 `games/_template/data/game` 同步生成），新建的
    工作树在跑过一次同步之前这个目录根本不存在。而"agent 在深层 scratchpad 里新建工作树、
    立刻跑 Unity 步骤"恰恰是本守卫要防的头号场景——旧实现在这种场景下会因为生成目录不存在直接
    静默 `return`，在最该拦截的时候完全失效，等于白写。根治：改为同时看两个根——
    `games/_template/data/game`（随仓库提交的源目录，任何检出/工作树里都在，是生成目录的
    来源）与 `adapters/unity/.../data/game`（生成目录，可能不存在）；两者都存在时内容应当一致
    （生成目录就是从源目录同步出来的，不含 `.meta` 差异），逐个存在的根分别求最长相对路径后
    取 max——天然覆盖"只有源目录存在""只有生成目录存在""两者都在"三种场景，不用猜此刻到底
    该信哪一个。

.EXAMPLE
    . (Join-Path $PSScriptRoot "_unity_path_length_guard.ps1")
    Test-UnityWorkingTreePathLength -RepoRoot $RepoRoot
#>

function Test-UnityWorkingTreePathLength {
    param([string]$RepoRoot)

    # 两个候选根：源目录在前（随仓库提交、必然存在，是估算依据的主力），生成目录在后（可能不
    # 存在，只在已经跑过 build.ps1 -SyncOnly 的工作树里才有）。
    $candidateDataGameRoots = @(
        (Join-Path $RepoRoot "games\_template\data\game"),
        (Join-Path $RepoRoot "adapters\unity\Assets\StreamingAssets\GameFoundation\data\game")
    )

    $longestRelative = ""
    $anyRootFound = $false
    foreach ($dataGameRoot in $candidateDataGameRoots) {
        if (-not (Test-Path $dataGameRoot)) { continue }
        $anyRootFound = $true
        foreach ($file in Get-ChildItem -Path $dataGameRoot -Recurse -File) {
            if ($file.Extension -eq ".meta") { continue }
            $relative = $file.FullName.Substring($dataGameRoot.Length).TrimStart("\", "/")
            if ($relative.Length -gt $longestRelative.Length) {
                $longestRelative = $relative
            }
        }
    }

    if (-not $anyRootFound) {
        # 判断记录：两个候选根都不存在——不是"生成目录还没同步"这种正常态（那种情况源目录
        # games/_template/data/game 仍然在），而是仓库数据目录结构已经变化、本守卫的扫描路径
        # 已经过期，找不到任何基准数据可估算。按 AGENTS.md §3"只读分析类入口在遇到阻断态时
        # 降级要显式标记（不能悄悄吞掉问题当作正常返回）"处理：本函数是只读分析（不写任何
        # 文件），选择显式警告后继续（不阻断门禁）而不是 throw 硬失败——理由是本守卫本身是
        # "尽力估算"的启发式保护，不是不可或缺的产品行为；两个基准根同时缺失通常意味着仓库
        # 结构调整没有同步更新这里，用一条硬失败去挡住与本次改动完全无关的正常 Unity 工作，
        # 代价（挡住整条门禁）比让这一次估算"缺力但可见"更大。但必须让这条降级足够醒目——不能
        # 复刻本次要根治的"静默放行"缺口——因此用显式 Write-Host 警告，而不是普通 return。
        Write-Host ("[Unity 路径长度守卫] 警告：games/_template/data/game 与 adapters/unity/" +
            "Assets/StreamingAssets/GameFoundation/data/game 均不存在，无法扫描基准数据估算 " +
            "Unity 测试可能生成的最长路径——本次未执行路径长度检查。深层工作树若确实过深，仍可能" +
            "在 Unity 测试阶段以 DirectoryNotFoundException 的伪装症状失败（见本文件头判断" +
            "记录）。请确认仓库数据目录结构是否已变化，必要时同步更新本守卫的扫描路径。") `
            -ForegroundColor Yellow
        return
    }
    if ($longestRelative -eq "") {
        # 至少一个根存在，但两个存在的根下都没有非 .meta 文件（空目录）——没有可估算的文件，
        # 与"目录整体缺失"是不同的情况，不算仓库结构损坏，按"无法评估、当作未超限"处理即可。
        return
    }

    # 覆盖目录名模板：前缀字面量取自 GameTemplateResidentTests.cs
    # ResidentRunner_DatasetRootOverride_LoadsProbeTable_FromOverrideRootOnly 方法体里的
    # "gf_test_dataset_root_override_" + Guid.NewGuid().ToString("N")；"N" 格式固定输出 32 位
    # 十六进制字符，用等长占位字符串代入即可，不需要真的生成一个 GUID。
    $overrideDirNamePlaceholder = "gf_test_dataset_root_override_" + ("0" * 32)
    $simulatedRelativePath = "adapters\unity\Assets\StreamingAssets\GameFoundation\data\" +
        "$overrideDirNamePlaceholder\$longestRelative"

    $repoRootTrimmed = $RepoRoot.TrimEnd("\", "/")
    # +1：仓库根与相对路径之间的路径分隔符本身也占一个字符。
    $estimatedMaxPathLength = $repoRootTrimmed.Length + 1 + $simulatedRelativePath.Length

    # Windows MAX_PATH 上限 260 字符（含结尾 null 终止符，习惯上仍按 260 整数比较）；留 10 字符
    # 余量，覆盖"覆盖目录名模板/GUID 格式今后如果略有变化"这类小幅度漂移，而不是卡着上限走。
    $windowsMaxPath = 260
    $safetyMargin = 10
    $threshold = $windowsMaxPath - $safetyMargin

    if ($estimatedMaxPathLength -gt $threshold) {
        throw @"
[Unity 路径长度守卫] 当前工作树根路径过深，预计会撞上 Windows MAX_PATH（260 字符）限制，导致
Unity 测试以"DirectoryNotFoundException / 目录没建出来"的伪装症状失败（真正原因是路径过长，
不是产品缺陷——见 GameTemplateResidentTests.cs 类型头判断记录）。

  当前工作树根路径长度：$($repoRootTrimmed.Length) 字符（$repoRootTrimmed）
  预估最长生成路径长度：$estimatedMaxPathLength 字符
    = 根路径长度 + 1（分隔符） + "$simulatedRelativePath"（$($simulatedRelativePath.Length) 字符）
    该形态来自 games/_template/Tests/Runtime/GameTemplateResidentTests.cs 的
    ResidentRunner_DatasetRootOverride_LoadsProbeTable_FromOverrideRootOnly 用例：运行期把
    data/game 整棵目录树复制到 StreamingAssets 下一个带 32 位十六进制 GUID 的新目录。
  Windows MAX_PATH 上限：260 字符（本次校验阈值 $threshold 字符 = 260 - $safetyMargin 字符余量）

怎么办：不要在这棵深层工作树 / scratchpad 目录下跑 Unity 相关步骤（Unity 编译检查 / EditMode /
PlayMode / 独立版构建 / 消费方演练）——换到主检出（D:\workespace\ws-game）或路径足够短的工作树下
跑这些步骤；本工作树内跑 `check.ps1 -Quick`（隐含 -SkipUnity）等非 Unity 步骤不受本守卫影响。
"@
    }
}
