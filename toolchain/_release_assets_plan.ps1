<#
.SYNOPSIS
    release.yml"Check for existing release assets"步骤的附件处置判定（纯函数，无副作用）。

    输入：该标签对应 GitHub Release 上已有的附件名清单、本次发布应有的全部附件名清单。
    输出：处置结论 Action 三选一——
      - Skip    ：应有附件已全部存在，什么都不做（"本机已验证产物优先"）。
      - Repair  ：zip 存在、其余附件有缺，由工作流从这份已验证 zip 里抽取/重算补齐（不构建）。
      - Blocked ：工作流不得继续，必须明确失败并打印人工指引（zip 缺失的两种情形，见下）。

.NOTES
    判断记录（"全部附件缺失"不再走托管运行器全量重建，改为明确失败，2026-10-02，
    v1.85.0～v1.91.0 的 Release 运行红灯）：
      - 旧路径：Release 上一个附件都没有时，工作流在 tag 触发的运行里调用
        `build.ps1 -SyncOnly -Dist <ver> -Zip` 全量重建。但这条路径在 tag 触发的运行里天然走不通：
        检出的就是 `v<ver>` 标签提交，`build.ps1` 的发布不可变守卫（`_dist_immutability_guard.ps1`）
        看到标签已存在而拒绝写 dist/zip/lock，运行在最后一步才红灯，日志只有一句"拒绝覆盖已发布版本"。
      - 两个修法：(A) 给 `build.ps1` 放行"从标签提交原样重建"（守卫只认标签是否存在，需新增开关并
        改守卫）；(B) 在工作流里提前明确失败并给人工指引。选 B，理由：
          1. 不可变发布要求同一版本号只有一套字节。本机 `build.ps1 -Release` 已经在含 Unity 的全量
             门禁下产出过这个版本的 zip/lock/tgz（`dist/` 里留有，私服里发过同版本号的包）；确定性
             构建仍把 checkout 绝对路径编进调试信息，托管运行器重建出的 DLL 字节、sha256 必然与本机
             不同。放行重建等于让同一版本号出现两套字节（Release 附件 vs 私服包），正是守卫要防的事，
             只是换成"重建"的名义；它也没有任何手段证明重建物与本机验证过的那套同源。
          2. 放行需要改 `build.ps1` 与守卫（发布不可变的唯一强制点），为一条"本机已发布过却缺附件"
             的异常路径放宽它，风险远大于收益；B 只动工作流，守卫一行不改。
          3. 可验证：本函数是纯函数，`toolchain/tests/test_release_assets_plan.py` 对应用例直接给
             "已有附件清单"断言结论；另有用例把工作流该步骤的 `run:` 正文原样抽出，在本机用桩 `gh`
             模拟"Release 不存在 / 全部缺失"的 tag 触发运行，断言失败退出码与指引文案。
      - 与旧"zip 缺失但 lock/tgz 仍在"阻断（混批）合并为同一个 Blocked 结论，两种情形用 Reason 区分
        （MixedBatch / NoVerifiedAssets），指引文案不同。
      - 工作流因此不再有任何构建步骤（`build.ps1 -SkipTests`、`dotnet tool restore`、
        `build.ps1 -SyncOnly -Dist -Zip` 三步随旧路径删除）；"缺附件修复"只剩从已验证 zip 抽取一条路。
      - 代价：只推 tag、本机未上传 Release 附件的情形，工作流不再自动出 Release，而是红灯并告诉你
        怎么做；这是有意的——Release 附件的唯一来源是本机 `build.ps1 -Release` 打印的 `gh release create`。

    判断记录（`get_framework.ps1`/`_hash.ps1` 不算批次敏感附件）：两个纯源文件任何机器上逐字节相同，
    见工作流"Stage self-contained get_framework.ps1 assets"步骤判断记录；它们存在与否不改变 zip 缺失
    时的结论（只要 zip/lock/samples/tgz 一个都没有就是 NoVerifiedAssets）。

    文案一律英文且纯 ASCII：它们会被 `shell: powershell` 的 `run:` 步骤打印，且本文件由 5.1 宿主
    dot-source（见 release.yml 文件头"run: 脚本正文必须是纯 ASCII"判断记录）。

    独立成文件（同目录 `_lock_writeback.ps1` 等同一模式），供 pytest 直接 dot-source。

.EXAMPLE
    . (Join-Path $PSScriptRoot "_release_assets_plan.ps1")
    $plan = Get-WsGameReleaseAssetsPlan -Tag "v1.94.0" -Version "1.94.0" `
        -ExistingNames @() -RequiredNames $requiredNames
    if ($plan.Action -eq "Blocked") { $plan.Message | ForEach-Object { Write-Host $_ }; exit 1 }
#>

function Get-WsGameReleaseAssetsPlan {
    param(
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Version,
        [AllowEmptyCollection()][string[]]$ExistingNames = @(),
        [Parameter(Mandatory = $true)][string[]]$RequiredNames,
        # 默认把两个纯源文件排除在"批次敏感"之外（见 .NOTES）。
        [string[]]$PlainSourceNames = @("get_framework.ps1", "_hash.ps1")
    )

    $existing = @($ExistingNames)
    $missing = @($RequiredNames | Where-Object { $existing -notcontains $_ })
    $zipName = "ws-game-$Version.zip"
    $batchSensitive = @($RequiredNames | Where-Object { $PlainSourceNames -notcontains $_ })

    $plan = [ordered]@{
        Action       = ""
        Reason       = ""
        MissingNames = $missing
        Message      = @()
    }

    if ($missing.Count -eq 0) {
        $plan.Action = "Skip"
        $plan.Reason = "AllPresent"
        $plan.Message = @("Release $Tag already has all required assets ($($RequiredNames -join ', ')). Skipping upload to avoid overwriting verified artifacts.")
        return [pscustomobject]$plan
    }

    if ($missing -notcontains $zipName) {
        $plan.Action = "Repair"
        $plan.Reason = "RepairFromZip"
        $plan.Message = @(
            "Release $Tag is missing: $($missing -join ', ').",
            "The zip exists; will repair the missing assets by extracting from the existing zip (no rebuild on the hosted runner)."
        )
        return [pscustomobject]$plan
    }

    # zip is missing from here on.
    $otherPresent = @($existing | Where-Object { $batchSensitive -contains $_ -and $_ -ne $zipName })
    $plan.Action = "Blocked"
    if ($otherPresent.Count -gt 0) {
        $plan.Reason = "MixedBatch"
        $plan.Message = @(
            "Release $Tag is missing: $($missing -join ', ').",
            "BLOCKED: the zip ($zipName) is missing, but other release assets already exist: $($otherPresent -join ', ').",
            "A hosted-runner rebuild would produce a new zip with different DLL bytes/hashes than a local build (deterministic builds encode the checkout path), so there is no way to prove the existing assets belong to the same batch as a freshly rebuilt zip.",
            "Guidance: on the release machine, upload the locally verified $zipName from dist/ to release $Tag manually alongside the existing assets (after checking its lock/tgz hashes agree with the assets already there); or delete ALL existing batch assets on release $Tag and re-upload the full locally verified set. Then re-run this workflow with workflow_dispatch (tag=$Tag) to verify."
        )
    } else {
        $plan.Reason = "NoVerifiedAssets"
        $plan.Message = @(
            "Release $Tag is missing: $($missing -join ', ').",
            "BLOCKED: release $Tag has no verified assets at all (zip, lock, samples zip and all .tgz are missing). This workflow does not rebuild them on the hosted runner.",
            "Why: (1) a tag-triggered run checks out the tag commit itself, and the build.ps1 -Dist/-Zip immutability guard refuses to write artifacts for an already tagged version; (2) even if it were allowed, a hosted rebuild would produce different DLL bytes/hashes than the locally verified build (deterministic builds encode the checkout path), i.e. a second byte-set for the same version number next to the local dist and the private registry packages.",
            "Guidance: on the release machine, upload the assets produced by the local 'build.ps1 -Release' run for $Version (dist/ws-game-$Version.zip, .lock, -samples.zip, dist/$Version/packages/*.tgz, toolchain/get_framework.ps1, toolchain/_hash.ps1) with the 'gh release create $Tag ...' command that build.ps1 printed at the end of the release (or 'gh release upload $Tag <files>' if the release already exists). Then re-run this workflow with workflow_dispatch (tag=$Tag): it will verify the zip/lock commit and repair any remaining missing .tgz from the zip."
        )
    }
    return [pscustomobject]$plan
}
