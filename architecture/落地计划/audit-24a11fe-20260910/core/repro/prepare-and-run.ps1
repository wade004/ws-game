param(
    [Parameter(Mandatory = $true)]
    [string]$FrozenRoot,
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$expectedHead = '24a11fe28f9647cd532c41f56f7ab18c00fb8516'
$frozen = (Resolve-Path -LiteralPath $FrozenRoot).Path
$output = [IO.Path]::GetFullPath($OutputRoot)
$coreRoot = Join-Path $output 'core'
$sourceRoot = Join-Path $coreRoot 'build\core\source'
$scriptRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$outputRepro = Join-Path $coreRoot 'repro'

if (!(Test-Path -LiteralPath (Join-Path $frozen '.git'))) { throw "FrozenRoot is not a git checkout: $frozen" }
$head = (git -C $frozen rev-parse HEAD).Trim()
if ($head -ne $expectedHead) { throw "unexpected frozen HEAD: $head" }
$version = (Get-Content -LiteralPath (Join-Path $frozen 'VERSION') -Raw).Trim()
if ($version -ne '1.16.1') { throw "unexpected frozen VERSION: $version" }
if (Test-Path -LiteralPath $sourceRoot) { throw "refusing to overwrite existing build image: $sourceRoot" }

New-Item -ItemType Directory -Force -Path $sourceRoot, $outputRepro | Out-Null
foreach ($part in @('core', 'presentation', 'adapters\stub')) {
    $from = Join-Path $frozen $part
    $to = Join-Path $sourceRoot $part
    robocopy $from $to /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "copy failed: $part exit=$LASTEXITCODE" }
}
Copy-Item -LiteralPath (Join-Path $frozen 'Directory.Build.props') -Destination (Join-Path $sourceRoot 'Directory.Build.props')

if (([IO.Path]::GetFullPath($scriptRoot)).TrimEnd('\') -ne ([IO.Path]::GetFullPath($outputRepro)).TrimEnd('\')) {
    foreach ($file in Get-ChildItem -LiteralPath $scriptRoot -File) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $outputRepro $file.Name)
    }
}

$bounded = Join-Path $outputRepro 'run-core-bounded.ps1'
$independent = Join-Path $outputRepro 'run-independent-probe.ps1'
& $bounded -SourceRoot $sourceRoot -EvidenceRoot $coreRoot
if ($LASTEXITCODE -ne 0) { throw "bounded runner failed: $LASTEXITCODE" }
& $independent -SourceRoot $sourceRoot -EvidenceRoot $coreRoot
if ($LASTEXITCODE -ne 0) { throw "independent probe failed: $LASTEXITCODE" }

Write-Output "prepared_and_ran head=$head version=$version output=$output"
