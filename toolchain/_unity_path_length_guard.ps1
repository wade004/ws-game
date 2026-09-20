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

.EXAMPLE
    . (Join-Path $PSScriptRoot "_unity_path_length_guard.ps1")
    Test-UnityWorkingTreePathLength -RepoRoot $RepoRoot
#>

function Test-UnityWorkingTreePathLength {
    param([string]$RepoRoot)

    $dataGameRoot = Join-Path $RepoRoot "adapters\unity\Assets\StreamingAssets\GameFoundation\data\game"
    if (-not (Test-Path $dataGameRoot)) {
        # 数据根缺失是别的门禁步骤（数据校验）该管的事，这里不重复报错——按"无法评估、当作未超限"
        # 处理，避免本函数自己成为新的误报源。
        return
    }

    $longestRelative = ""
    foreach ($file in Get-ChildItem -Path $dataGameRoot -Recurse -File) {
        if ($file.Extension -eq ".meta") { continue }
        $relative = $file.FullName.Substring($dataGameRoot.Length).TrimStart("\", "/")
        if ($relative.Length -gt $longestRelative.Length) {
            $longestRelative = $relative
        }
    }
    if ($longestRelative -eq "") {
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
