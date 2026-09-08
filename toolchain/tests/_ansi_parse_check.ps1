param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][ValidateSet("cp1252", "native")][string]$Mode
)

# toolchain/tests/test_powershell_scripts_ansi_safe.py 的辅助脚本：按指定模式把
# 目标 .ps1/.psm1 文件的内容解码为文本后交给 PowerShell 语言分析器（Parser）做纯语法解析
# （不执行），输出解析错误条数。
#
# -Mode cp1252：把文件字节按 Windows-1252（西欧 ANSI 代码页）解码——用来复现托管 CI 运行器上
#   Windows PowerShell 5.1 在文件没有 UTF-8 BOM 时按系统 ANSI 代码页误读脚本的场景（本机是 GBK
#   代码页，CI 运行器是 cp1252，两者都不是 UTF-8，误读的具体乱码不同，但只要源文件含非 ASCII
#   字节、又没有 BOM 提示真实编码，就都会被误读；用 cp1252 复现即可暴露同一类问题）。
# -Mode native：用 PowerShell 自身默认的 Get-Content -Raw 读取——对带 BOM 的文件，PowerShell
#   会正确识别 BOM 并按 UTF-8 解码，不需要额外模拟。
if ($Mode -eq "cp1252") {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $encoding = [System.Text.Encoding]::GetEncoding(1252)
    $text = $encoding.GetString($bytes)
} else {
    $text = Get-Content -LiteralPath $Path -Raw
}

$tokens = $null
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$tokens, [ref]$parseErrors) | Out-Null

Write-Output $parseErrors.Count
foreach ($e in $parseErrors) {
    Write-Output ("ERR: " + $e.Message)
}
