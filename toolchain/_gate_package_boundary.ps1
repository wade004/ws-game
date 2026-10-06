<#
门禁：包边界与独立版边界的纯函数集合（ADR-0160，P1 拆分：运行时适配包 / 手感实验室可选包 / 样板仓库）。

背景：1.100.0 之前运行时适配包（com.gamefoundation.adapter.unity）同时带着实验室试玩宿主与演示场景，主 zip 与工具链包
带着无头实验室与全部实验室数据；游戏的独立版构建也因此带上了实验室程序集。ADR-0160 把它拆成三层：运行时包只留运行时；
实验室是可选的开发期包（com.gamefoundation.feel-lab.unity 与 com.gamefoundation.feel-lab.headless）；真实美术演示场景
整体迁往另一个仓库（样板仓库），框架永不依赖它。这里的函数把"拆干净了"变成可执行、可被反例证伪的判定：

  Get-PackageBoundaryProblems   一个发布包（或其源码目录）的内容判定：
    1) 任何发布包：路径里不得出现演示场景内容（showcase、第三方演示模型、模型包装配器等）；
    2) 运行时包（除两个实验室包之外的全部发布包）：路径里不得出现实验室内容（LabHost、Lab.Kernel、FeelLab、
       试玩宿主、data/_lab*、data/_equip、lab/fixtures、Plugins/Lab），程序集定义文件（.asmdef）不得引用实验室或演示程序集，
       源码里的 InternalsVisibleTo 不得点名实验室或演示程序集；
    3) 实验室 Unity 包：每个运行期程序集必须只在编辑器编译（defineConstraints 含 UNITY_EDITOR，或只包含 Editor 平台；
       只在带 UNITY_INCLUDE_TESTS 约束的测试程序集豁免），插件 DLL 的 .meta 必须只启用 Editor 平台——这样"游戏开发期
       装上它、独立版里一个字节也没有"由包自己保证，而不是靠游戏小心。
  Get-ReleaseTreeBoundaryProblems  发布 zip 的解包树（dist/<版本>/）：只判路径，演示内容一律不许；实验室内容只许出现在
    packages/ 下两个实验室包的目录与 .tgz 里（主 zip 暂存时这两个包目录已被剔除，这里照样兜底）。
  Get-PlayerBoundaryProblems    独立版构建产物目录：不得含实验室/演示程序集、数据或资源（消费方演练对构建产物的断言）。

所有函数只返回问题文案数组（空 = 合格），不向管道漏东西；PowerShell 5.1 兼容（不用 &&、??、三元）。
由 toolchain/tests/test_package_boundary.py 用伪造目录与清单驱动：每条规则各一个反例（夹带一个实验室文件就必须被拦住），
再加一个合格对照。含中文，UTF-8 带 BOM，LF 换行。
#>

$script:FeelLabPackageNames = @("com.gamefoundation.feel-lab.unity", "com.gamefoundation.feel-lab.headless")

# 演示内容（任何发布包、任何发布树、任何独立版都不许有）。
$script:ShowcasePathPattern = '(?i)showcase|quaternius|modelgroundupright|modelpackbuilder'

# 实验室内容（只许出现在两个实验室包里）。分两类：名字类（程序集/文件名）与路径类（数据与夹具目录）。
$script:LabNamePattern = '(?i)labhost|feellab|lab\.kernel|labplayground|enginelab|feel-lab'
$script:LabDirPattern = '(^|/)data/_(lab|lab_action|equip)(/|$)|(^|/)lab/fixtures(/|$)|(^|/)Plugins/Lab(/|$)'

function Test-FeelLabPackageName {
    param([Parameter(Mandatory = $true)][string]$PackageName)
    return ($script:FeelLabPackageNames -contains $PackageName)
}

function ConvertTo-BoundaryPath {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Path)
    return (($Path -replace '\\', '/').TrimStart('/'))
}

function Read-BoundaryText {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.File]::ReadAllText($Path, (New-Object System.Text.UTF8Encoding($false)))
}

# 一个 asmdef 文本里所有"点名其它程序集"的字符串：references 与 precompiledReferences（含 GUID 形式不识别，本仓库一律按名字引用）。
function Get-AsmdefReferencedNames {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)
    $names = @()
    try {
        $obj = $Text | ConvertFrom-Json
    } catch {
        return @("<无法解析的 asmdef>")
    }
    foreach ($prop in @("references", "precompiledReferences")) {
        if ($obj.PSObject.Properties.Name -contains $prop) {
            foreach ($item in @($obj.$prop)) { if ($null -ne $item) { $names += [string]$item } }
        }
    }
    return @($names)
}

# 判定一个 asmdef 在独立版里是否根本不编译：只含 Editor 平台，或 defineConstraints 含 UNITY_EDITOR。
function Test-AsmdefEditorOnly {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)
    try {
        $obj = $Text | ConvertFrom-Json
    } catch {
        return $false
    }
    if ($obj.PSObject.Properties.Name -contains "defineConstraints") {
        if (@($obj.defineConstraints) -contains "UNITY_EDITOR") { return $true }
    }
    if ($obj.PSObject.Properties.Name -contains "includePlatforms") {
        $plats = @($obj.includePlatforms)
        if ($plats.Count -eq 1 -and $plats[0] -eq "Editor") { return $true }
    }
    return $false
}

function Test-AsmdefTestsOnly {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)
    try {
        $obj = $Text | ConvertFrom-Json
    } catch {
        return $false
    }
    if ($obj.PSObject.Properties.Name -contains "defineConstraints") {
        if (@($obj.defineConstraints) -contains "UNITY_INCLUDE_TESTS") { return $true }
    }
    return $false
}

# 插件 DLL 的 .meta 是否"只启用 Editor 平台"（Any 关闭、Editor 打开）。
function Test-PluginMetaEditorOnly {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)
    $t = $Text -replace "`r`n", "`n"
    $any = [regex]::Match($t, '(?m)first:\s*\n\s*Any:\s*\n\s*second:\s*\n\s*enabled:\s*(\d)')
    $editor = [regex]::Match($t, '(?m)first:\s*\n\s*Editor:\s*Editor\s*\n\s*second:\s*\n\s*enabled:\s*(\d)')
    if (-not $any.Success -or -not $editor.Success) { return $false }
    return ($any.Groups[1].Value -eq "0" -and $editor.Groups[1].Value -eq "1")
}

function Get-PackageBoundaryProblems {
    param(
        [Parameter(Mandatory = $true)][string]$PackageName,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$EntryPaths,
        # 可选：包（或源码包目录）在磁盘上的根；给了才做 asmdef / .meta / InternalsVisibleTo 的内容判定。
        [string]$PackageDir = ""
    )
    $problems = @()
    $isLab = Test-FeelLabPackageName -PackageName $PackageName

    $showHits = @()
    $labHits = @()
    foreach ($raw in $EntryPaths) {
        $p = ConvertTo-BoundaryPath -Path $raw
        if ($p -match $script:ShowcasePathPattern) { $showHits += $p }
        if ((-not $isLab) -and (($p -match $script:LabNamePattern) -or ($p -match $script:LabDirPattern))) { $labHits += $p }
    }
    if ($showHits.Count -gt 0) {
        $problems += ("$PackageName：发布包内含演示场景内容（演示场景属于样板仓库，任何发布包都不许带，ADR-0160）：" + (($showHits | Select-Object -First 6) -join ", "))
    }
    if ($labHits.Count -gt 0) {
        $problems += ("$PackageName：运行时包内含手感实验室内容（实验室只许在 com.gamefoundation.feel-lab.* 两个可选包里，ADR-0160）：" + (($labHits | Select-Object -First 6) -join ", "))
    }

    if ($PackageDir -ne "" -and (Test-Path -LiteralPath $PackageDir)) {
        $root = (Resolve-Path -LiteralPath $PackageDir).ProviderPath.TrimEnd('\', '/')
        function Get-RelPath {
            param([string]$Full)
            return (ConvertTo-BoundaryPath -Path $Full.Substring($root.Length))
        }
        if (-not $isLab) {
            $refHits = @()
            foreach ($f in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter "*.asmdef")) {
                $text = Read-BoundaryText -Path $f.FullName
                foreach ($n in (Get-AsmdefReferencedNames -Text $text)) {
                    if ($n -match '(?i)labhost|feellab|lab\.kernel|showcase') { $refHits += ((Get-RelPath -Full $f.FullName) + " -> " + $n) }
                }
            }
            if ($refHits.Count -gt 0) {
                $problems += ("$PackageName：运行时程序集引用了实验室或演示程序集（依赖方向必须是实验室依赖运行时，反之不行，ADR-0160）：" + (($refHits | Select-Object -First 6) -join "; "))
            }
            $ivtHits = @()
            foreach ($f in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter "*.cs")) {
                $text = Read-BoundaryText -Path $f.FullName
                foreach ($m in [regex]::Matches($text, 'InternalsVisibleTo\(\s*"([^"]+)"')) {
                    $target = $m.Groups[1].Value
                    if ($target -match '(?i)lab|showcase') { $ivtHits += ((Get-RelPath -Full $f.FullName) + " -> " + $target) }
                }
            }
            if ($ivtHits.Count -gt 0) {
                $problems += ("$PackageName：源码里的 InternalsVisibleTo 点名了实验室或演示程序集（要给实验室用的入口应改成公开的宿主驱动入口，ADR-0160）：" + (($ivtHits | Select-Object -First 6) -join "; "))
            }
        }
        if ($PackageName -eq "com.gamefoundation.feel-lab.unity") {
            $notEditorOnly = @()
            foreach ($f in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter "*.asmdef")) {
                $text = Read-BoundaryText -Path $f.FullName
                if (Test-AsmdefTestsOnly -Text $text) { continue }
                if (-not (Test-AsmdefEditorOnly -Text $text)) { $notEditorOnly += (Get-RelPath -Full $f.FullName) }
            }
            if ($notEditorOnly.Count -gt 0) {
                $problems += ("$PackageName：实验室 Unity 包的运行期程序集必须只在编辑器编译（defineConstraints 含 UNITY_EDITOR 或只含 Editor 平台），否则游戏独立版会带上它：" + ($notEditorOnly -join ", "))
            }
            $metaBad = @()
            foreach ($f in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter "*.dll.meta")) {
                $text = Read-BoundaryText -Path $f.FullName
                if (-not (Test-PluginMetaEditorOnly -Text $text)) { $metaBad += (Get-RelPath -Full $f.FullName) }
            }
            if ($metaBad.Count -gt 0) {
                $problems += ("$PackageName：实验室 Unity 包的插件 DLL 必须只启用 Editor 平台（.meta 里 Any 关闭、Editor 打开）：" + ($metaBad -join ", "))
            }
        }
    }
    return @($problems)
}

# 发布树（dist/<版本>/ 或 zip 解包树）：只判路径。packages/<实验室包名>/ 与对应 .tgz 里允许实验室内容，其余一律不许。
function Get-ReleaseTreeBoundaryProblems {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$RelativePaths)
    $showHits = @()
    $labHits = @()
    foreach ($raw in $RelativePaths) {
        $p = ConvertTo-BoundaryPath -Path $raw
        if ($p -match $script:ShowcasePathPattern) { $showHits += $p }
        $inLabPackage = $false
        foreach ($labName in $script:FeelLabPackageNames) {
            if ($p.StartsWith("packages/$labName/") -or ($p -match ('^packages/' + [regex]::Escape($labName) + '-[^/]+\.tgz$'))) { $inLabPackage = $true }
        }
        if ((-not $inLabPackage) -and (($p -match $script:LabNamePattern) -or ($p -match $script:LabDirPattern))) { $labHits += $p }
    }
    $problems = @()
    if ($showHits.Count -gt 0) {
        $problems += ("发布树含演示场景内容（任何发布产物都不许带，ADR-0160）：" + (($showHits | Select-Object -First 6) -join ", "))
    }
    if ($labHits.Count -gt 0) {
        $problems += ("发布树的运行时部分含手感实验室内容（只许在 packages/com.gamefoundation.feel-lab.* 里，ADR-0160）：" + (($labHits | Select-Object -First 6) -join ", "))
    }
    return @($problems)
}

# 独立版构建产物目录：不得含实验室/演示的程序集、数据或资源；ScriptingAssemblies.json 也不得点名它们。
function Get-PlayerBoundaryProblems {
    param([Parameter(Mandatory = $true)][string]$BuildDir)
    if (-not (Test-Path -LiteralPath $BuildDir)) {
        return @("独立版产物目录不存在：$BuildDir（无法核对实验室与演示内容是否被排除）")
    }
    $root = (Resolve-Path -LiteralPath $BuildDir).ProviderPath.TrimEnd('\', '/')
    $hits = @()
    foreach ($f in @(Get-ChildItem -LiteralPath $root -Recurse -File)) {
        $rel = ConvertTo-BoundaryPath -Path $f.FullName.Substring($root.Length)
        if (($rel -match $script:ShowcasePathPattern) -or ($rel -match $script:LabNamePattern) -or ($rel -match $script:LabDirPattern)) { $hits += $rel }
    }
    $problems = @()
    if ($hits.Count -gt 0) {
        $problems += ("独立版产物含实验室或演示内容（游戏只在开发期装实验室，独立版不得带它，ADR-0160）：" + (($hits | Select-Object -First 8) -join ", "))
    }
    $listing = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter "ScriptingAssemblies.json")
    if ($listing.Count -eq 0) {
        $problems += "独立版产物里找不到 ScriptingAssemblies.json（无法核对程序集清单是否排除实验室，构建产物不完整？）"
    }
    foreach ($f in $listing) {
        $text = Read-BoundaryText -Path $f.FullName
        $m = [regex]::Matches($text, '"([^"]+\.dll)"')
        $bad = @()
        foreach ($x in $m) {
            $n = $x.Groups[1].Value
            if (($n -match $script:LabNamePattern) -or ($n -match $script:ShowcasePathPattern)) { $bad += $n }
        }
        if ($bad.Count -gt 0) {
            $problems += ("独立版 ScriptingAssemblies.json 点名了实验室或演示程序集：" + ($bad -join ", "))
        }
    }
    return @($problems)
}
