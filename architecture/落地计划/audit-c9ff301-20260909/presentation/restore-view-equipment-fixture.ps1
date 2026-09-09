param([string]$CopyRoot = 'D:\workespace\ws-game-unity-audit-c9ff301\copyRoot')
$ErrorActionPreference = 'Stop'
$path = Join-Path $CopyRoot 'data\_sample\item\item.template.json'
$backup = Join-Path $PSScriptRoot 'fixture-item.template.original'
$hashFile = Join-Path $PSScriptRoot 'fixture-item.template.original.sha256'
$expected = (Get-Content -LiteralPath $hashFile -Raw).Trim().ToLowerInvariant()
$backupHash = (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash.ToLowerInvariant()
if ($backupHash -ne $expected) { throw "fixture backup hash mismatch: $backupHash != $expected" }
[IO.File]::WriteAllBytes($path, [IO.File]::ReadAllBytes($backup))
$restoredHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
if ($restoredHash -ne $expected) { throw "fixture restore hash mismatch: $restoredHash != $expected" }
Push-Location $CopyRoot
try { & .\build.ps1 -SyncContent }
finally { Pop-Location }
$streaming = Join-Path $CopyRoot 'adapters\unity\Assets\StreamingAssets\GameFoundation\data\_sample\item\item.template.json'
$streamingHash = (Get-FileHash -LiteralPath $streaming -Algorithm SHA256).Hash.ToLowerInvariant()
if ($streamingHash -ne $expected) { throw "StreamingAssets restore hash mismatch: $streamingHash != $expected" }
"fixture_restore=OK;original_sha256=$expected;source_sha256=$restoredHash;streaming_sha256=$streamingHash" | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'fixture-restore.log') -Encoding utf8
