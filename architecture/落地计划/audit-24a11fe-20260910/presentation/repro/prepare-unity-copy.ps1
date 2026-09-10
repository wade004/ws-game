param(
    [Parameter(Mandatory = $true)]
    [string]$FrozenRoot,
    [Parameter(Mandatory = $true)]
    [string]$DllRoot,
    [Parameter(Mandatory = $true)]
    [string]$CopyRoot
)

$ErrorActionPreference = 'Stop'
$expectedHead = '24a11fe28f9647cd532c41f56f7ab18c00fb8516'
$frozen = (Resolve-Path -LiteralPath $FrozenRoot).Path
$copy = [IO.Path]::GetFullPath($CopyRoot)
if (!(Test-Path -LiteralPath (Join-Path $frozen '.git'))) { throw "FrozenRoot is not a git checkout: $frozen" }
$head = (git -C $frozen rev-parse HEAD).Trim()
if ($head -ne $expectedHead) { throw "unexpected frozen HEAD: $head" }
$version = (Get-Content -LiteralPath (Join-Path $frozen 'VERSION') -Raw).Trim()
if ($version -ne '1.16.1') { throw "unexpected frozen VERSION: $version" }
if (Test-Path -LiteralPath $copy) { throw "refusing to overwrite existing Unity copy: $copy" }
New-Item -ItemType Directory -Force -Path $copy | Out-Null

foreach ($part in @('adapters\unity', 'adapters\conformance', 'games\_template', 'data', 'assets')) {
    $from = Join-Path $frozen $part
    $to = Join-Path $copy $part
    robocopy $from $to /E /XD Library Temp Logs UserSettings /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "copy failed: $part exit=$LASTEXITCODE" }
}

$plugin = Join-Path $copy 'adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core'
New-Item -ItemType Directory -Force -Path $plugin | Out-Null
foreach ($name in @('Core.Carriers.dll', 'Core.Foundation.dll', 'Core.Gameplay.dll', 'Core.Numbers.dll', 'Core.Rules.dll', 'Presentation.Common.dll')) {
    $from = Join-Path (Join-Path $DllRoot ($name -replace '\.dll$','')) (Join-Path 'release' $name)
    if (!(Test-Path -LiteralPath $from)) { throw "missing current DLL: $from" }
    Copy-Item -LiteralPath $from -Destination (Join-Path $plugin $name)
}

Write-Output "unity_copy_ready head=$head version=$version copy=$copy"
