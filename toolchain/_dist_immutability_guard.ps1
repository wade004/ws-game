<#
.SYNOPSIS
    已发布版本的 dist 产物不可覆盖——强制校验（2026-09-20，消费方反馈第 8 条根治）。

    背景：仓库对外承诺"标签 + dist zip + lock 不可变发布"（见 architecture/11_工程规范与测试.md 第 7 节"构建产物与版本号约定"
    一节），但此前没有任何强制手段落地这个承诺：`build.ps1`"打分发包 dist/<版本>/"一节、
    "打 zip + lock"一节、`toolchain/_lock_writeback.ps1` 的 `Write-WsGameLockFile` 三处均无
    版本存在性校验，会无条件覆盖同名产物。唯一的防线是 `-Release` 第 7 步的
    `git tag -a`（标签已存在会失败），但这道防线在三个产物已经被覆盖之后才执行；更严重的是
    `-Dist`/`-Zip` 这两条独立于 `-Release` 的打包路径完全不经过这道防线——不校验 git 状态、
    不改版本号、不提交、不打标签，只要传入一个已经发布过的版本号就会静默覆盖。复现：发布过 vX
    后单独执行 `build.ps1 -Dist X -Zip`（不用 -Release、不改版本号、不碰 git），三个产物被
    静默覆盖，同一 zip 两次打包出的哈希不同。后果：消费方按 `ws-game.lock` 锁定的哈希会对不
    上，排查时只会怀疑自己环境，而不是框架自己破坏了对外承诺。

    落地为 `Assert-DistVersionNotAlreadyReleased`：`build.ps1` 在 dist/<版本>/ 目录、
    zip+lock 两处产物真正写出（覆盖/新建）之前分别调用一次（见两处调用点自身的判断记录），
    覆盖 `-Release`/`-Dist`/`-Zip` 三条入口的任意组合——三者最终都会走到这两处共享代码块，不
    需要在每个开关分支各写一份校验。

.NOTES
    判断记录（判定"已发布"的依据：`git tag -l v<版本>` 是否存在，不是"dist/ 下文件/目录是否
    存在"）：
      - dist/ 整体 `.gitignore`（不进源码库），本机 dist/ 下有没有文件，只反映"最近一次在这台
        机器上跑没跑过打包"，不反映"这个版本号是否已经对外发布过"——可能是上次构建失败、进程
        被中途杀掉留下的半成品，也可能是一份从没打过包的干净 checkout；两种情况都不能反推
        "版本是否已发布"，用文件存在性判断反而会在"半成品残留"时误判成已发布而拒绝合法的首次
        打包，或者在"dist/ 被手动清空但版本确实发布过"时误判成未发布而放行覆盖，两个方向都会
        判错。
      - `v<版本>` 标签由 `-Release` 第 7 步在"打包完成自检"通过之后才创建（见 build.ps1 该
        步骤判断记录：自检失败根本不会走到打标签这一步），标签一旦存在就代表这个版本号确实
        产出过一份自检通过、已经对外发布的产物快照——这是仓库里唯一一处"发布"这件事真正落定
        的信号，比文件系统状态更可靠。
      - 校验对象统一用调用方传入的 `VersionForPath`（即打包路径实际使用的版本字符串，`-DryRun`
        场景下带 `-dryrun` 后缀），不是记录进 MANIFEST/lock 内容字段的"干净"版本号
        （`$ResolvedDistVersion`）：真实发布从不会给一个带 `-dryrun` 后缀的字符串打标签，所以
        `-Release -DryRun`、`-Dist X.Y.Z-dryrun` 两条 dry-run 路径下
        `git tag -l "v<带后缀的字符串>"` 天然查不到匹配，不需要在每个调用点分别记住"这里要放行
        dry-run"这条例外——检查对象与"实际会被覆盖的产物路径"用的是同一个字符串，天然不会误伤
        合法的 dry-run 验证场景。

    判断记录（例外通道 `-AllowOverwrite`，对应 build.ps1 的 `-AllowOverwriteDist` 开关，默认
    关闭）：重跑一次失败的发布（标签还没打成功、产物只是半成品）是合法需求（见 AGENTS.md
    §5"半途状态若发布提交已产生但无标签"一节），不能把覆盖完全锁死。开一个显式开关，调用方
    传入时跳过本次校验直接放行，但必须打印醒目警告点出正在覆盖哪些文件——不能悄悄放行，否则
    例外通道本身又变成一个新的"覆盖不留痕"的口子。默认关闭：这是发布不可变承诺的强制落地，
    不应该在日常调用里习惯性带上这个开关绕过校验。

    判断记录（用 `throw` 而不是内联 `Write-Host -Red; exit 1`）：与同目录
    `_unity_path_length_guard.ps1` 的 `Test-UnityWorkingTreePathLength` 同一模式——调用方
    （`build.ps1`）顶部已经设置 `$ErrorActionPreference = "Stop"`，未捕获的 `throw` 会自然
    终止整个脚本并打印异常信息，不需要函数自己调用 `exit`；同时便于
    `toolchain/tests/test_dist_immutability_guard.py` 用 `try/catch` 捕获异常消息断言内容，
    不需要额外解析进程退出码之外的输出。

    独立成本文件（同目录 `_hash.ps1`/`_version_writeback.ps1`/`_lock_writeback.ps1`/
    `_unity_path_length_guard.ps1` 同一模式）：只定义函数、无顶层副作用，供
    `toolchain/tests/test_dist_immutability_guard.py` 直接 dot-source 后单独测试，不需要跑
    完整 `build.ps1`（后者会顺带跑一大批耗时的 dotnet build/test 步骤）。

.EXAMPLE
    . (Join-Path $PSScriptRoot "_dist_immutability_guard.ps1")
    Assert-DistVersionNotAlreadyReleased `
        -RepoRoot $RepoRoot -VersionForPath $DistDirVersion `
        -ArtifactDescriptions @("dist\$DistDirVersion\ 目录（打包内容）") `
        -AllowOverwrite:$AllowOverwriteDist
#>

# 只读判断：`v<版本>` 标签是否存在。不对 $RepoRoot 是否是合法 git 仓库做额外防御——调用方
# （build.ps1）本身就要求在仓库根运行，`git tag -l` 在非 git 仓库下会直接非零退出，属于比本函数
# 自己重新校验一遍更早、更明确的失败信号。
function Test-DistVersionTagExists {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$VersionForPath
    )
    $tagName = "v$VersionForPath"
    $existing = & git -C $RepoRoot tag -l $tagName
    $existingJoined = (@($existing) -join "").Trim()
    return ($existingJoined -ne "")
}

# 强制校验主入口。$ArtifactDescriptions 是本次调用即将写出/覆盖的产物描述（相对路径 + 一句用途
# 说明），只用于拼错误信息/警告信息，不做任何文件系统操作——调用方自己决定要不要、什么时候真正
# 写这些文件，本函数只负责"要不要放行"。
function Assert-DistVersionNotAlreadyReleased {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$VersionForPath,
        [Parameter(Mandatory = $true)][string[]]$ArtifactDescriptions,
        [switch]$AllowOverwrite
    )
    $tagName = "v$VersionForPath"

    if ($AllowOverwrite) {
        Write-Host "警告：-AllowOverwriteDist 已启用，跳过'已发布版本不可覆盖'校验，即将覆盖以下产物（版本 $VersionForPath）：" -ForegroundColor Yellow
        foreach ($d in $ArtifactDescriptions) {
            Write-Host "  - $d" -ForegroundColor Yellow
        }
        Write-Host "  仅应在重跑一次失败/半途的发布（标签 $tagName 还没打成功、产物是半成品）时使用；若 $tagName 已经对应一次完整发布，这个开关会破坏本仓库'标签 + dist zip + lock 不可变发布'的对外承诺，请改为发布新版本号。" -ForegroundColor Yellow
        return
    }

    if (Test-DistVersionTagExists -RepoRoot $RepoRoot -VersionForPath $VersionForPath) {
        $lines = @("拒绝覆盖已发布版本 $VersionForPath 的产物：")
        foreach ($d in $ArtifactDescriptions) {
            $lines += "  - $d"
        }
        $lines += "已发布证据：标签 $tagName 已存在（git tag -l $tagName 有输出）。"
        $lines += "拒绝原因：本仓库承诺已发布版本的 dist 目录/zip/lock 三项产物不可变（见 architecture/11_工程规范与测试.md 第 7 节'构建产物与版本号约定'），覆盖会让已经分发出去的哈希/内容对不上，消费方据此锁定的 ws-game.lock 会失效且难以察觉。"
        $lines += "正确做法：发布一个新的版本号（通常是递增 PATCH 段）再打包/发布，不要覆盖已发布版本号的产物。"
        $lines += "例外：若这确实是重跑一次失败/半途的发布（标签 $tagName 还没打成功、产物只是半成品），可显式传 -AllowOverwriteDist 跳过本次校验（会打印醒目警告，用前请先确认标签真的没打成功）。"
        throw ($lines -join "`n")
    }
}
