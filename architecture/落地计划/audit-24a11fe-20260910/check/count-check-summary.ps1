[CmdletBinding()]
param(
    [string]$Transcript = 'D:\workespace\ws-game-artifacts\audit-24a11fe-20260910\logs\check-skipunity-transcript.txt',
    [string]$OutputPath = 'D:\workespace\ws-game-artifacts\audit-24a11fe-20260910\docs-project\check-summary-count.txt'
)
$ErrorActionPreference = 'Stop'
$lines = Get-Content -LiteralPath $Transcript
$inSummary = $false
$rows = New-Object System.Collections.Generic.List[object]
foreach ($line in $lines) {
    if ($line.TrimStart().StartsWith('Step') -and $line.Contains('Result Seconds Detail')) { $inSummary = $true; continue }
    if ($inSummary -and $line -like '门禁通过：*') { break }
    if (-not $inSummary) { continue }
    if ($line -match '^(.*?)\s+(PASS|SKIP)\s+') {
        $rows.Add([pscustomobject]@{ Name = $Matches[1].Trim(); Result = $Matches[2] })
    }
}
$out = New-Object System.Collections.Generic.List[string]
$out.Add("steps=$($rows.Count)")
$out.Add("pass=$(($rows | Where-Object Result -eq 'PASS').Count)")
$out.Add("skip=$(($rows | Where-Object Result -eq 'SKIP').Count)")
$out.Add('skip_names:')
$rows | Where-Object Result -eq 'SKIP' | ForEach-Object { $out.Add("- $($_.Name)") }
$out | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$out | Write-Output
