<#
.SYNOPSIS
    共享的"写回版本号到已跟踪源码文件"函数（root-cause 修复 CRLF 缺陷，2026-09-10：build.ps1
    -Release 第 4 步此前把两个 package.json 通过 ConvertTo-Json 往返写回时，写出的文本里混入了
    PowerShell 5.1 ConvertTo-Json 自带的 `\r\n` 行尾，即使外层用
    [System.IO.File]::WriteAllText + UTF8Encoding($false) 避免了 BOM，也保存不住 LF——
    .gitattributes 对这两个文件声明的是 `eol=lf`，实测复现见发布流程记录）。

    抽成独立可 dot-source 的函数文件（与同目录 _hash.ps1 同一模式），供 build.ps1 -Release 第 4 步
    调用，也供 toolchain/tests/test_build_version_writeback_lf.py 直接对临时副本调用做回归断言，
    不需要跑一遍完整 -Release 流程即可验证写回逻辑本身不产生 CRLF。

    三个函数均只做"写回单个版本号字段"这一件事，不做门禁、不做 git 操作、不依赖 build.ps1 的其余
    全局状态（只接收显式参数），可在任意临时目录的文件副本上独立调用。

.EXAMPLE
    . (Join-Path $PSScriptRoot "_version_writeback.ps1")
    Set-VersionFileContent -Path "C:\some\VERSION" -Version "1.15.0"
    Set-SourcePackageJsonVersion -JsonPath "C:\some\package.json" -Version "1.15.0"
    Set-PackagesLockGameTemplateDependency -JsonPath "C:\some\packages-lock.json" -Version "1.15.0"
#>

# 判断记录：VERSION 文件是不带 BOM、不带尾随换行的纯 ASCII 文本（既有约定，见仓库根 VERSION 文件
# 实际内容——git ls-files --eol 对它显示 i/none w/none，未被任何 .gitattributes 文本规则约束，
# 换行策略完全由这里的写入方式决定）。$Version 参数本身是形如 "X.Y.Z" 的纯数字点号字符串，不含任何
# 换行，因此这里天然不会写出 \r；仍然保留 WriteAllText + UTF8Encoding($false) 的显式写法（不用
# PowerShell 内置的按行文本写入 cmdlet 族——那一族在 -Encoding utf8 时固定带 BOM），与另外两个
# 函数统一写法、便于测试用同一种方式断言"无 BOM"。
function Set-VersionFileContent {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Version
    )
    [System.IO.File]::WriteAllText($Path, $Version, (New-Object System.Text.UTF8Encoding($false)))
}

# 判断记录（本次根治核心）：两个 package.json 是不带 BOM 的 UTF-8（含中文 description 字段），
# .gitattributes 声明 `*.json text eol=lf`，检出/入库都应为纯 LF。PowerShell 5.1 的
# `ConvertTo-Json` 返回的字符串本身携带 `\r\n` 行尾（与 -Depth/-Compress 等参数无关，是该 cmdlet
# 在 5.1 上的固有行为），此前直接把这个字符串传给
# [System.IO.File]::WriteAllText（外层只管了编码/BOM，没管字符串内容本身的换行符），写出的文件
# 因此带 CRLF——`git status`/`git diff --stat` 能看到"CRLF will be replaced by LF"警告，`git
# ls-files --eol` 显示 w/crlf，触发门禁"工作树文本文件无 CR"步骤 FAIL（2026-09-10 -Release
# 1.15.0 实测复现）。
#
# 根治：写回前对 ConvertTo-Json 的输出做一次行尾归一化——`\r\n` -> `\n`，并保险起见再处理单独出现
# 的裸 `\r`（`-replace "\r\n", "\n"` 之后再 `-replace "\r", "\n"`，覆盖极端情况下混用行尾的输入，
# 当前 PowerShell 5.1 ConvertTo-Json 实测只产生 `\r\n`，第二条替换是防御性的，不影响正常路径）。
# 另外 ConvertTo-Json 返回的字符串本身不带尾随换行，而两个源文件原本都以 "}\n" 收尾（既有约定，
# git show HEAD 对两个文件的字节内容核实过），因此归一化后再补一个尾随 `\n`，与原文件约定保持一致
# （不判断"原文件是否有尾随换行"再决定——两个源文件当前都有，写死更简单；如果未来这两个文件的既有
# 换行约定发生变化，属于另一个需要单独判断记录的改动）。
function Set-SourcePackageJsonVersion {
    param(
        [Parameter(Mandatory = $true)][string]$JsonPath,
        [Parameter(Mandatory = $true)][string]$Version
    )
    if (-not (Test-Path $JsonPath)) {
        throw "找不到 $JsonPath，无法回写版本号"
    }
    $obj = (Get-Content -Path $JsonPath -Raw -Encoding UTF8) | ConvertFrom-Json
    $obj.version = $Version
    if (($obj.PSObject.Properties.Name -contains "dependencies") -and
        ($obj.dependencies.PSObject.Properties.Name -contains "com.gamefoundation.adapter.unity")) {
        $obj.dependencies."com.gamefoundation.adapter.unity" = $Version
    }
    $jsonText = ($obj | ConvertTo-Json -Depth 10)
    $jsonText = $jsonText -replace "`r`n", "`n"
    $jsonText = $jsonText -replace "`r", "`n"
    if (-not $jsonText.EndsWith("`n")) {
        $jsonText += "`n"
    }
    [System.IO.File]::WriteAllText($JsonPath, $jsonText, (New-Object System.Text.UTF8Encoding($false)))
}

# 判断记录：packages-lock.json 是 UPM 自动生成/维护的大文件（行尾见根 .gitattributes 对应例外
# 条目判断记录：Unity/UPM 实测写出 LF、无 BOM、2 空格缩进，键顺序由 UPM 决定），整体
# ConvertFrom-Json/ConvertTo-Json 往返会打乱这些格式（PowerShell 5.1 的 ConvertTo-Json 缩进/换行符
# 与 UPM 原始输出不一致），导致下次 UPM 打开工程时产生一大片与本次改动无关的格式 diff。改用最小化
# 正则文本替换，只动 com.gamefoundation.game-template 依赖块下这一个字段的值，文件其余内容与换行
# 风格原样保留（`[System.IO.File]::ReadAllText` + 正则替换不会引入/改变任何换行符，与两个
# package.json 用完整 JSON 往返、因而需要额外归一化的写法不同——这个函数本身不需要、也不做行尾
# 归一化，2026-09-10 CRLF 根治复查已确认此函数不是问题源头，见调用方 build.ps1 判断记录）。
function Set-PackagesLockGameTemplateDependency {
    param(
        [Parameter(Mandatory = $true)][string]$JsonPath,
        [Parameter(Mandatory = $true)][string]$Version
    )
    if (-not (Test-Path $JsonPath)) {
        throw "找不到 $JsonPath，无法回写版本号"
    }
    $raw = [System.IO.File]::ReadAllText($JsonPath)
    $pattern = '("com\.gamefoundation\.adapter\.unity":\s*")\d+\.\d+\.\d+(")'
    $hitCount = [regex]::Matches($raw, $pattern).Count
    if ($hitCount -ne 1) {
        throw "$JsonPath 中 'com.gamefoundation.adapter.unity' 依赖字段命中 $hitCount 处（预期 1 处），格式可能已变化，拒绝盲目替换"
    }
    $newRaw = [regex]::Replace($raw, $pattern, ('${1}' + $Version + '${2}'))
    [System.IO.File]::WriteAllText($JsonPath, $newRaw, (New-Object System.Text.UTF8Encoding($false)))
}
