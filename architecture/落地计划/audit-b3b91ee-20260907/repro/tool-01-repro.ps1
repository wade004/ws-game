[CmdletBinding()]
param([string]$RepoRoot = '')

$ErrorActionPreference = 'Continue'
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}
$checkPath = Join-Path $RepoRoot 'check.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($checkPath, [ref]$tokens, [ref]$parseErrors)
$fn = $ast.Find({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Test-NativeExitCode'
    }, $true)
if ($null -eq $fn) { throw "Test-NativeExitCode was not found in $checkPath" }
Invoke-Expression $fn.Extent.Text

# Establish a known zero exit code, then run a deliberately missing executable.
cmd.exe /c exit 0 | Out-Null
$knownZero = $LASTEXITCODE
$missingName = '__ws_game_audit_missing_executable__'
$result = Test-NativeExitCode $missingName @()
$actual = [bool]$result
$expected = $false
$log = @(
    "check=$checkPath"
    "powershell=$($PSVersionTable.PSVersion)"
    "known_zero_exit=$knownZero"
    "missing_executable=$missingName"
    "extracted_function=$($fn.Extent.Text.Trim())"
    "actual=$actual"
    "expected=$expected"
    "result=$($(if ($actual -eq $expected) { 'NOT_REPRODUCED' } else { 'PASS_FOR_REPRO (missing executable returned true)' }))"
)
$logPath = Join-Path $PSScriptRoot 'tool-01-repro.log'
[IO.File]::WriteAllLines($logPath, $log)
$log | Write-Output
Write-Output "LOG=$logPath"
if ($actual -eq $expected) { exit 0 } else { exit 1 }
