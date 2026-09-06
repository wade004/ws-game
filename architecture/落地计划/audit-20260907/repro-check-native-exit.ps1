[CmdletBinding()]
param()

# Bounded audit probe for the actual check.ps1 function AST
# (Write-StepHeader, Invoke-CheckStep, Test-NativeExitCode; check.ps1:138-195
# and 229-237). This file is an audit attachment; it does not modify
# production scripts or data.
$ErrorActionPreference = 'Stop'

# Parse and load only the three function definitions from the repository's
# current check.ps1. No top-level check.ps1 code is dot-sourced or executed.
$checkPath = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\check.ps1')).Path
$parseErrors = $null
$checkAst = [System.Management.Automation.Language.Parser]::ParseFile($checkPath, [ref]$null, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw "check.ps1 parse errors: $parseErrors" }

$script:Results = New-Object System.Collections.Generic.List[Object]
foreach ($name in @('Write-StepHeader', 'Invoke-CheckStep', 'Test-NativeExitCode')) {
    $fn = $checkAst.Find({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true) | Select-Object -First 1
    if ($null -eq $fn) { throw "Function not found in check.ps1: $name" }
    Invoke-Expression $fn.Extent.Text
}

Invoke-CheckStep -Name 'AUDIT_EXPECTED_FAILURE' -Action {
    Test-NativeExitCode 'powershell.exe' @('-NoProfile', '-Command', 'Write-Output AUDIT_EXPECTED_FAILURE; exit 7')
}

$result = & {
    Test-NativeExitCode 'powershell.exe' @('-NoProfile', '-Command', 'Write-Output AUDIT_EXPECTED_FAILURE; exit 7')
}
"RESULT_TYPE=$($result.GetType().FullName)"
"RESULT_COUNT=$(@($result).Count)"
"ACTUAL_CHECKSTEP_RESULT=$($script:Results[0].Result)"

if ($script:Results[0].Result -ne 'PASS' -or @($result).Count -ne 2) {
    throw 'Probe did not reproduce the expected output-array false positive.'
}

Write-Output 'REPRODUCED_FALSE_POSITIVE=True'
