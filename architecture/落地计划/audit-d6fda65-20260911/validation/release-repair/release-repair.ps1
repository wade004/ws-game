$ErrorActionPreference = 'Stop'
$repo = 'D:\workespace\ws-game-audit-d6fda65-20260911'
$sourceRepo = 'D:\workespace\ws-game'
$root = 'D:\workespace\ws-game-artifacts\audit-d6fda65-20260911\validation\release-repair'
$sourceZip = Join-Path $sourceRepo 'dist\ws-game-1.18.0.zip'
$sourceLock = Join-Path $sourceRepo 'dist\ws-game-1.18.0.lock'
$zip = Join-Path $root 'ws-game-1.18.0-mutated.zip'
$lockOriginal = Join-Path $root 'ws-game-1.18.0-original.lock'
$lockRebuilt = Join-Path $root 'ws-game-1.18.0-repaired.lock'
Copy-Item -LiteralPath $sourceZip -Destination $zip -Force
Copy-Item -LiteralPath $sourceLock -Destination $lockOriginal -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Update)
try {
    $entryName = 'ws-game-1.18.0\adapters\headless\Adapters.Stub.dll'
    $entry = $archive.Entries | Where-Object { $_.FullName -eq $entryName } | Select-Object -First 1
    if ($null -eq $entry) { throw "missing $entryName" }
    $memory = New-Object IO.MemoryStream
    $stream = $entry.Open()
    try { $stream.CopyTo($memory) } finally { $stream.Dispose() }
    $bytes = $memory.ToArray()
    $memory.Dispose()
    $entry.Delete()
    $newEntry = $archive.CreateEntry($entryName)
    $out = $newEntry.Open()
    try {
        $out.Write($bytes, 0, $bytes.Length)
        $out.WriteByte(0xA5)
    } finally { $out.Dispose() }
} finally { $archive.Dispose() }

# Exact equivalent of release.yml lines 489-513: read six core DLL bytes and MANIFEST.txt from the verified zip.
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $core = @('Core.Foundation','Core.Numbers','Core.Rules','Core.Carriers','Core.Gameplay','Presentation.Common')
    $shaMap = [ordered]@{}
    foreach ($asm in $core) {
        $entryPath = "ws-game-1.18.0\adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core\$asm.dll"
        $entry = $archive.Entries | Where-Object { $_.FullName -eq $entryPath } | Select-Object -First 1
        if ($null -eq $entry) { throw "missing $entryPath" }
        $temp = [IO.Path]::GetTempFileName()
        try {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $temp, $true)
            $shaMap["$asm.dll"] = (Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash.ToLowerInvariant()
        } finally { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
    }
    $manifestEntry = $archive.Entries | Where-Object { $_.FullName -eq 'ws-game-1.18.0\MANIFEST.txt' } | Select-Object -First 1
    $reader = New-Object IO.StreamReader($manifestEntry.Open())
    try { $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $match = [regex]::Match($manifest, 'git_commit:\s*(\S+)')
    $commit = $match.Groups[1].Value
    $lock = [ordered]@{ version = '1.18.0'; git_commit = $commit; dlls = $shaMap }
    [IO.File]::WriteAllText($lockRebuilt, ($lock | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
} finally { $archive.Dispose() }

function Get-JsonSummary($path) {
    $obj = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    [PSCustomObject]@{
        path = $path
        top_fields = (($obj.PSObject.Properties.Name) -join ',')
        dlls_count = @($obj.dlls.PSObject.Properties).Count
        headless_dlls_present = ($null -ne $obj.headless_dlls)
        validator_dlls_present = ($null -ne $obj.validator_dlls)
        samples_present = ($null -ne $obj.samples)
    }
}
Get-JsonSummary $sourceLock | ConvertTo-Json -Compress
Get-JsonSummary $lockRebuilt | ConvertTo-Json -Compress
Write-Output "mutated_zip_sha256=$((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant())"
Write-Output "original_lock_sha256=$((Get-FileHash -LiteralPath $lockOriginal -Algorithm SHA256).Hash.ToLowerInvariant())"
Write-Output "rebuilt_lock_sha256=$((Get-FileHash -LiteralPath $lockRebuilt -Algorithm SHA256).Hash.ToLowerInvariant())"
