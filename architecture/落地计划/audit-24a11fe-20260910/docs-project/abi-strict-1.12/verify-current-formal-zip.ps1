[CmdletBinding()]
param(
    [string]$SourceRoot = 'D:\workespace\ws-game',
    [string]$OutDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$version = '1.16.1'
$zipPath = Join-Path $SourceRoot "dist\ws-game-$version.zip"
$binSource = Join-Path $PSScriptRoot 'consumer\bin\Release\net8.0'
$runDir = Join-Path $OutDir 'formal-zip-consumer'
$libDir = Join-Path $OutDir 'formal-current-lib'
New-Item -ItemType Directory -Force -Path $runDir, $libDir | Out-Null
Get-ChildItem -LiteralPath $binSource | Copy-Item -Destination $runDir -Recurse -Force

function Norm([string]$Name) { return $Name.Replace('\', '/').TrimStart('/') }
function EntryBySuffix($Archive, [string]$Suffix) {
    $hits = @($Archive.Entries | Where-Object { (Norm $_.FullName).EndsWith($Suffix, [StringComparison]::OrdinalIgnoreCase) })
    if ($hits.Count -ne 1) { throw "expected one entry '$Suffix', got $($hits.Count)" }
    return $hits[0]
}
function CopyEntry($Entry, [string]$Path) {
    $input = $Entry.Open(); try { $output = [IO.File]::Create($Path); try { $input.CopyTo($output) } finally { $output.Dispose() } } finally { $input.Dispose() }
}

$dlls = @('Core.Foundation.dll','Core.Numbers.dll','Core.Carriers.dll','Core.Rules.dll','Core.Gameplay.dll','Presentation.Common.dll')
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($name in $dlls) {
        $entry = EntryBySuffix $archive "toolchain/validator/lib/$name"
        $dest = Join-Path $libDir $name
        CopyEntry $entry $dest
        Copy-Item -LiteralPath $dest -Destination (Join-Path $runDir $name) -Force
    }
}
finally { $archive.Dispose() }

$consumer = Join-Path $runDir 'AbiProbeConsumer.dll'
$before = (Get-FileHash $consumer -Algorithm SHA256).Hash.ToLowerInvariant()
& dotnet $consumer *> (Join-Path $OutDir 'run-against-current-formal-zip.log')
$exitCode = $LASTEXITCODE
$after = (Get-FileHash $consumer -Algorithm SHA256).Hash.ToLowerInvariant()
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("formal_zip=$zipPath")
$lines.Add("formal_zip_sha256=$((Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant())")
$lines.Add('consumer_rebuilt_after_zip_swap=false')
$lines.Add("consumer_sha256_before=$before")
$lines.Add("consumer_sha256_after=$after")
$lines.Add("consumer_hash_unchanged=$($before -eq $after)")
$lines.Add("consumer_exit=$exitCode")
foreach ($name in $dlls) { $lines.Add("formal_zip_dll=$name sha256=$((Get-FileHash (Join-Path $libDir $name) -Algorithm SHA256).Hash.ToLowerInvariant())") }
$lines.Add("result=$(if($exitCode -eq 0 -and $before -eq $after){'PASS'}else{'FAIL'})")
$lines | Set-Content (Join-Path $OutDir 'formal-zip-abi-summary.txt') -Encoding UTF8
$lines | Write-Output
