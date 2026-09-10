<##
.SYNOPSIS
    Stream-check formal ws-game release zips against their sibling lock files.

    This is an audit reproducer. It never rebuilds or extracts a formal release
    zip. Each DLL and manifest is read through ZipArchiveEntry.Open(), and each
    lock hash is compared with the selected entry bytes.
##>
[CmdletBinding()]
param(
    [string]$SourceRoot = "D:\workespace\ws-game",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path (Get-Location).Path "formal-release-stream-check.txt"
}
$outputDir = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
if ($outputDir) { New-Item -ItemType Directory -Force -Path $outputDir | Out-Null }

function Normalize-Entry([string]$name) {
    return $name.Replace('\', '/').TrimStart('/')
}

function Get-EntryBySuffix($archive, [string]$suffix) {
    $normalized = $suffix.Replace('\', '/').TrimStart('/')
    $matches = @($archive.Entries | Where-Object {
        (Normalize-Entry $_.FullName).EndsWith($normalized, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matches.Count -ne 1) {
        throw "expected exactly one zip entry ending '$suffix', got $($matches.Count)"
    }
    return $matches[0]
}

function Get-EntryBytes($entry) {
    $stream = $entry.Open()
    try {
        $memory = New-Object IO.MemoryStream
        try { $stream.CopyTo($memory); return $memory.ToArray() }
        finally { $memory.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-EntrySha256($entry) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = $entry.Open()
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}

function Get-EntryText($entry) {
    $bytes = Get-EntryBytes $entry
    return [Text.Encoding]::UTF8.GetString($bytes)
}

function Get-RootPackageEntry($archive, [string]$packageName) {
    $matches = @($archive.Entries | Where-Object {
        (Normalize-Entry $_.FullName) -match "^[^/]+/packages/$([regex]::Escape($packageName))/package\.json$"
    })
    if ($matches.Count -ne 1) {
        throw "expected exactly one root package manifest for '$packageName', got $($matches.Count)"
    }
    return $matches[0]
}

$versions = @('1.12.0', '1.14.0', '1.15.0', '1.16.0', '1.16.1')
$lines = New-Object Collections.Generic.List[string]
$lines.Add("formal release stream check")
$lines.Add("source_root=$SourceRoot")
$lines.Add("formal_zips_are_read_only=true")
$lines.Add("rebuild_performed=false")
$lines.Add("")

foreach ($version in $versions) {
    $zipPath = Join-Path $SourceRoot "dist\ws-game-$version.zip"
    $lockPath = Join-Path $SourceRoot "dist\ws-game-$version.lock"
    if (-not (Test-Path -LiteralPath $zipPath)) { throw "missing zip: $zipPath" }
    if (-not (Test-Path -LiteralPath $lockPath)) { throw "missing lock: $lockPath" }

    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    if ([string]$lock.version -ne $version) { throw "lock version mismatch: $lockPath" }
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $all = @($archive.Entries)
        $dllEntries = @($all | Where-Object { (Normalize-Entry $_.FullName) -match '\.dll$' })
        $packageEntries = @($all | Where-Object { (Normalize-Entry $_.FullName) -match '^ws-game-[^/]+/packages/[^/]+/package\.json$' })
        $lines.Add("[$version]")
        $lines.Add("zip_sha256=$((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant())")
        $lines.Add("zip_entries=$($all.Count) dll_entries=$($dllEntries.Count) npm_package_manifests=$($packageEntries.Count)")

        $expected = [ordered]@{}
        foreach ($property in $lock.dlls.PSObject.Properties) { $expected[$property.Name] = [string]$property.Value }
        if ($null -ne $lock.headless_dlls) {
            foreach ($property in $lock.headless_dlls.PSObject.Properties) { $expected[$property.Name] = [string]$property.Value }
        }
        if ($null -ne $lock.validator_dlls) {
            foreach ($property in $lock.validator_dlls.PSObject.Properties) { $expected[$property.Name] = [string]$property.Value }
        }
        foreach ($name in $expected.Keys) {
            if ($name -eq 'Adapters.Stub.dll') { $suffix = "adapters/headless/$name" }
            elseif ($name -eq 'Validator.dll') { $suffix = "toolchain/validator/bin/$name" }
            else { $suffix = "toolchain/validator/lib/$name" }
            $entry = Get-EntryBySuffix $archive $suffix
            $actual = Get-EntrySha256 $entry
            $result = if ($actual -eq $expected[$name]) { 'MATCH' } else { 'MISMATCH' }
            $lines.Add("lock_dll=$name result=$result sha256=$actual entry=$(Normalize-Entry $entry.FullName)")
            if ($result -ne 'MATCH') { throw "lock hash mismatch for $version $name" }

            $sameNameEntries = @($all | Where-Object {
                (Normalize-Entry $_.FullName) -match "/$([regex]::Escape($name))$"
            })
            $sameNameHashes = @($sameNameEntries | ForEach-Object { Get-EntrySha256 $_ } | Sort-Object -Unique)
            $copiesResult = if ($sameNameHashes.Count -eq 1 -and $sameNameHashes[0] -eq $expected[$name]) { 'MATCH' } else { 'MISMATCH' }
            $lines.Add("lock_dll_all_copies=$name result=$copiesResult copies=$($sameNameEntries.Count) unique_sha256=$($sameNameHashes -join ',')")
            if ($copiesResult -ne 'MATCH') { throw "same-name DLL copy hash mismatch for $version $name" }
        }

        $manifestNames = @(
            'com.gamefoundation.adapter.unity',
            'com.gamefoundation.framework-data',
            'com.gamefoundation.toolchain',
            'com.gamefoundation.adapter.headless'
        )
        foreach ($manifestName in $manifestNames) {
            if ($manifestName -eq 'com.gamefoundation.adapter.headless' -and $version -eq '1.12.0') {
                $lines.Add("manifest=$manifestName result=ABSENT_EXPECTED version=$version")
                continue
            }
            $entry = Get-RootPackageEntry $archive $manifestName
            $json = (Get-EntryText $entry).TrimStart([char]0xFEFF) | ConvertFrom-Json
            $result = if ([string]$json.version -eq $version) { 'MATCH' } else { 'MISMATCH' }
            $lines.Add("manifest=$manifestName result=$result version=$($json.version) entry=$(Normalize-Entry $entry.FullName)")
            if ($result -ne 'MATCH') { throw "package version mismatch for $version $manifestName" }
        }

        $manifest = Get-EntryBySuffix $archive 'MANIFEST.txt'
        $manifestText = Get-EntryText $manifest
        $lines.Add("manifest_txt_present=true sha256=$(Get-EntrySha256 $manifest)")
        if ($manifestText -notmatch "version:\s+$([regex]::Escape($version))") { throw "MANIFEST version mismatch for $version" }
        if ($manifestText -notmatch 'Core.Foundation\.dll: sha256=') { throw "MANIFEST core DLL section missing for $version" }
        $lines.Add("result=PASS")
    }
    finally { $archive.Dispose() }
    $lines.Add("")
}

Set-Content -LiteralPath $OutputPath -Value $lines -Encoding UTF8
$lines | Write-Output
