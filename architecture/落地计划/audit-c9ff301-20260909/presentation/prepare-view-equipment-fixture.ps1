param([string]$CopyRoot = 'D:\workespace\ws-game-unity-audit-c9ff301\copyRoot')
$ErrorActionPreference = 'Stop'
$path = Join-Path $CopyRoot 'data\_sample\item\item.template.json'
$backup = Join-Path $PSScriptRoot 'fixture-item.template.original'
$hashFile = Join-Path $PSScriptRoot 'fixture-item.template.original.sha256'
if (!(Test-Path -LiteralPath $backup)) {
    [IO.File]::WriteAllBytes($backup, [IO.File]::ReadAllBytes($path))
    (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath $hashFile -NoNewline
}
$doc = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
if (@($doc.rows | Where-Object id -eq 'item.sample_model_sword').Count -eq 0) {
    $doc.rows += [pscustomobject]@{
        id = 'item.sample_model_sword'; slot = 'item.slot.sample_main_hand'; quality = 'item.quality.sample_common'; item_level = 1
        stats = @([pscustomobject]@{ stat = 'stat.strength'; op = 'flat'; value = 2 })
        weapon_profile = [pscustomobject]@{ damage_min = 3; damage_max = 6; speed = 1.5; weapon_school = 'school.physical' }
        display_ref = 'display.map.sample_model_sword'; stack_size = 1; name_key = 'l10n.item.sample_blade.name'
    }
    [IO.File]::WriteAllText($path, ($doc | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false)))
}
if (@((Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).rows | Where-Object id -eq 'item.sample_model_sword').Count -ne 1) { throw 'fixture row verification failed' }
Push-Location $CopyRoot
try { & .\build.ps1 -SyncContent }
finally { Pop-Location }
