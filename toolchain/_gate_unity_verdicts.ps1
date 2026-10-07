<#
门禁 Unity 线内部判定逻辑的纯函数集合（测试覆盖第四批，2026-10-01，复盘 I-8 余项）：
`toolchain/_gate_line_unity.ps1` 原先把"Unity 结果 XML 是否 Passed""冒烟日志是否含成功标记""npm pack
清单是否缺必需文件/命中排除名单"这几处判定逻辑内联在 Invoke-CheckStep 的 scriptblock 里，只有真跑
一遍 Unity 才执行得到，门禁自身的判定逻辑因此从来没有被单独验证过（判错放行或判错拦截都要等线上
才发现）。这里抽成不依赖 Unity、不依赖 npm 的纯函数，由 `toolchain/tests/test_gate_unity_verdicts.py`
用伪造的 XML / 日志文本 / 清单路径子进程驱动测试；`_gate_line_unity.ps1` dot-source 本文件后调用同一
份函数，判定条件与抽取前逐字等价（含包清单必需文件清单、各自的报错文案）。

所有函数只返回值、不向管道漏东西，PowerShell 5.1 兼容。
#>

# NUnit 3（Unity Test Runner）结果 XML 的运行级判定：根节点 result 属性等于 Passed 才算通过。
# 返回 Exists（文件在不在）/ Ok / Result / Failed（根节点 failed 属性原文）。文件不存在时
# Exists=$false、Ok=$false，由调用方决定怎么报（沿用此前"未生成结果 XML"的文案）。
function Get-UnityTestRunVerdict {
    param([Parameter(Mandatory = $true)][string]$ResultsXml)
    if (-not (Test-Path -LiteralPath $ResultsXml)) {
        return [PSCustomObject]@{ Exists = $false; Ok = $false; Result = ""; Failed = "" }
    }
    # 不用 `[xml]$x = Get-Content -Raw`：Windows PowerShell 5.1 的 Get-Content 无 BOM 时按 ANSI 代码页（GBK）
    # 解码，Unity 写进 CDATA 的中文原因（如 Assert.Ignore 消息以全角句号结尾，字节 E3 80 82 紧邻 "]]>"）
    # 会吞掉 "]]>" 的第一个 "]"，CDATA 无法闭合，整份结果 XML 转换失败（1.98.0 发布门禁 PlayMode 619/619
    # 全过却判 FAIL）。XmlDocument.Load 按 XML 声明（utf-8）读字节，不随宿主默认编码变。
    $xml = New-Object System.Xml.XmlDocument
    $xml.Load((Resolve-Path -LiteralPath $ResultsXml).ProviderPath)
    $root = $xml.DocumentElement
    return [PSCustomObject]@{
        Exists = $true
        Ok     = ($root.result -eq "Passed")
        Result = [string]$root.result
        Failed = [string]$root.failed
    }
}

# 独立版 -gf-smoke / -gf-smoke-discrete 冒烟日志的成功判定：连续模式要 `[GF-SMOKE] RESULT=OK`，
# 离散模式（-Discrete）在此之上还要 `step=discrete_round ok`。IL2CPP 两个冒烟步骤复用同一判定。
function Test-SmokeLogText {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$LogText,
        [switch]$Discrete
    )
    $resultOk = ($LogText -match "\[GF-SMOKE\] RESULT=OK")
    if (-not $Discrete) { return $resultOk }
    return ($resultOk -and ($LogText -match "step=discrete_round ok"))
}

# 四个可发布 npm 包 `npm pack --dry-run --json` 文件清单的内容判定。EntryPaths 是清单里每个文件的
# path 原文（可能带反斜杠）。返回问题文案数组（空数组 = 清单合格）；包 version 与 dist 版本号是否一致
# 由调用方单独核对（读 package.json，不是清单内容），不在本函数里。
#
# 判断记录：
# 1) 排除名单：路径段命中 __pycache__/bin/obj/storage 即问题；唯一例外是预编译工具产物
#    `validator/bin/`、`simrunner/bin/`、`feellab/bin/`（toolchain 包 Tools~/ 下随包分发的 Validator.dll /
#    SimRunner.dll / FeelLab.dll 就放在那里，见 build.ps1 打包判断记录）。
# 2) 必需文件清单按包分流，缺一个就报一个问题，文案沿用抽取前的版本（含审计/反馈编号，便于从门禁
#    输出直接追溯来由）：adapter.unity 要有 model/anim 占位资产与生成器（PJ130-02）；toolchain 要有
#    预编译 validator（消费方反馈 E1）、simrunner（T-N6-7）、feellab（手感实验室，M2-D，另含自包含实验室根
#    Tools~/feellab/labroot/ 的代表文件）及各自的隔离用 Directory.Build.props；
#    adapter.headless 要有 Lib~/Core.Sim.dll（T-N6-7）。必需文件按"路径以该后缀结尾"匹配（-like），
#    与抽取前一致。
function Get-PackageManifestProblems {
    param(
        [Parameter(Mandatory = $true)][string]$PackageName,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$EntryPaths
    )
    $forbiddenSegments = @("__pycache__", "bin", "obj", "storage")
    $problems = @()

    $hitSegments = New-Object System.Collections.Generic.HashSet[string]
    foreach ($rawPath in $EntryPaths) {
        $normalized = $rawPath -replace '\\', '/'
        if ($normalized -match '(^|/)(validator|simrunner|feellab)/bin/') {
            continue
        }
        $segments = $rawPath -split '[\\/]'
        foreach ($seg in $forbiddenSegments) {
            if ($segments -contains $seg) {
                [void]$hitSegments.Add($seg)
            }
        }
    }
    if ($hitSegments.Count -gt 0) {
        $problems += ("$PackageName：npm pack --dry-run 文件清单命中排除名单：" + (($hitSegments) -join ", "))
    }

    $normalizedPaths = @($EntryPaths | ForEach-Object { ($_ -replace '\\', '/') })
    function Get-MissingSuffixes {
        param([string[]]$Suffixes)
        $missing = @()
        foreach ($suffix in $Suffixes) {
            $hit = @($normalizedPaths | Where-Object { $_ -like "*$suffix" })
            if ($hit.Count -eq 0) { $missing += $suffix }
        }
        return $missing
    }

    if ($PackageName -eq "com.gamefoundation.adapter.unity") {
        $missing = @(Get-MissingSuffixes -Suffixes @(
            "Runtime/Resources/GameFoundation/models/placeholder_biped.prefab",
            "Runtime/Resources/GameFoundation/models/placeholder_biped.controller",
            "Runtime/Resources/GameFoundation/anim_clips/idle.anim",
            "Runtime/Resources/GameFoundation/anim_clips/attack.anim",
            "Runtime/Resources/GameFoundation/anim_clips/cast.anim",
            "Runtime/Resources/GameFoundation/anim_clips/hit.anim",
            "Editor/GeneratePlaceholderModelAssets.cs"))
        if ($missing.Count -gt 0) {
            $problems += ("$PackageName：npm pack --dry-run 文件清单缺失 model/anim 占位资产或生成器（PJ130-02）：" + ($missing -join ", "))
        }
    }

    if ($PackageName -eq "com.gamefoundation.toolchain") {
        $missingValidator = @(Get-MissingSuffixes -Suffixes @(
            "Tools~/validator/bin/Validator.dll",
            "Tools~/validator/Directory.Build.props"))
        if ($missingValidator.Count -gt 0) {
            $problems += ("$PackageName：npm pack --dry-run 文件清单缺失预编译 validator 或隔离用 Directory.Build.props（消费方反馈 E1）：" + ($missingValidator -join ", "))
        }
        $missingSimRunner = @(Get-MissingSuffixes -Suffixes @(
            "Tools~/simrunner/bin/SimRunner.dll",
            "Tools~/simrunner/Directory.Build.props"))
        if ($missingSimRunner.Count -gt 0) {
            $problems += ("$PackageName：npm pack --dry-run 文件清单缺失预编译 simrunner 或隔离用 Directory.Build.props（T-N6-7）：" + ($missingSimRunner -join ", "))
        }
        # ADR-0160：手感实验室不再随 toolchain 包分发（改由 com.gamefoundation.feel-lab.headless 承载），这里反向断言。
        $leakedFeelLab = @($normalizedPaths | Where-Object { $_ -match '(^|/)feellab(/|$)' })
        if ($leakedFeelLab.Count -gt 0) {
            $problems += ("$PackageName：npm pack --dry-run 文件清单含手感实验室命令行（ADR-0160 起应只在 com.gamefoundation.feel-lab.headless 里）：" + (($leakedFeelLab | Select-Object -First 3) -join ", "))
        }
    }

    if ($PackageName -eq "com.gamefoundation.framework-data") {
        # 手感落地 S1（收 S0 遗留 (b)）：发版产物必须带 data/_feel（含缺省标定行），否则消费方的框架根拿不到手感档案。
        $missingFeel = @(Get-MissingSuffixes -Suffixes @(
            "Data~/data/_feel/feel/feel.calibration.json",
            "Data~/data/_feel/feel/feel.preset.json",
            "Data~/data/_feel_templates/feel/feel.preset.json",
            "Data~/data/_starter_kit/l10n/l10n.text.json"))
        if ($missingFeel.Count -gt 0) {
            $problems += ("$PackageName：npm pack --dry-run 文件清单缺失 data/_feel 手感框架数据（S1 发版打包断言）：" + ($missingFeel -join ", "))
        }
    }

    if ($PackageName -eq "com.gamefoundation.feel-lab.headless") {
        # 手感实验室命令行（M2-D，ADR-0160 起独立成包）：预编译命令行 + 隔离用 Directory.Build.props + 自包含实验室根
        # （框架数据、手感数据、实验室数据集、实验室动作数据、标准脚本与基线夹具各至少一个代表文件）。
        $missingFeelLab = @(Get-MissingSuffixes -Suffixes @(
            "Tools~/feellab/bin/FeelLab.dll",
            "Tools~/feellab/bin/Lab.Kernel.dll",
            "Tools~/feellab/Directory.Build.props",
            "Tools~/feellab/labroot/data/_feel/feel/feel.calibration.json",
            "Tools~/feellab/labroot/data/_feel_templates/feel/feel.preset.json",
            "Tools~/feellab/labroot/data/_lab/lab/lab.scenario.json",
            "Tools~/feellab/labroot/data/_equip/item/item.template.json",
            "Tools~/feellab/labroot/lab/fixtures/scripts/feel_kill.script.json",
            "Tools~/feellab/labroot/lab/fixtures/baselines/feel_kill.baseline.json"))
        if ($missingFeelLab.Count -gt 0) {
            $problems += ("$PackageName：npm pack --dry-run 文件清单缺失预编译 feellab、隔离用 Directory.Build.props 或自包含实验室根（M2-D）：" + ($missingFeelLab -join ", "))
        }
    }

    if ($PackageName -eq "com.gamefoundation.feel-lab.unity") {
        # 手感实验室 Unity 包（ADR-0160）：自包含实验室根（LabRoot~，引擎宿主在没有框架仓库的环境里靠它找数据与夹具）。
        $missingLabUnity = @(Get-MissingSuffixes -Suffixes @(
            "LabRoot~/data/_lab/lab/lab.scenario.json",
            "LabRoot~/data/_equip/item/item.template.json",
            "LabRoot~/lab/fixtures/scripts/feel_kill.script.json",
            "LabRoot~/lab/fixtures/baselines/feel_kill.baseline.json"))
        if ($missingLabUnity.Count -gt 0) {
            $problems += ("$PackageName：npm pack --dry-run 文件清单缺失自包含实验室根 LabRoot~（ADR-0160）：" + ($missingLabUnity -join ", "))
        }
    }

    if ($PackageName -eq "com.gamefoundation.adapter.headless") {
        $missingCoreSim = @(Get-MissingSuffixes -Suffixes @("Lib~/Core.Sim.dll"))
        if ($missingCoreSim.Count -gt 0) {
            $problems += "$PackageName：npm pack --dry-run 文件清单缺失 Lib~/Core.Sim.dll（T-N6-7）"
        }
    }

    return @($problems)
}
