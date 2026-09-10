param(
    [Parameter(Mandatory = $true)] [string]$FrozenRoot,
    [Parameter(Mandatory = $true)] [string]$OutputRoot
)

$ErrorActionPreference = "Stop"
$source = Join-Path $OutputRoot "build\source"
$expectedHead = "4faab73e7081f2984e7addb88fee051b6c0d3d02"
$marker = Join-Path $source ".frozen-head"
if (Test-Path -LiteralPath $source) {
    $stamp = if (Test-Path -LiteralPath $marker) { (Get-Content -LiteralPath $marker -Raw).Trim() } else { "" }
    if ($stamp -eq $expectedHead) { return $source }
    $entries = @(Get-ChildItem -LiteralPath $source -Force)
    if ($entries.Count -gt 0) { throw "Refusing to overwrite source mirror: $source; choose a new OutputRoot" }
}
New-Item -ItemType Directory -Force -Path $source | Out-Null
foreach ($name in @("core", "presentation", "toolchain")) {
    & robocopy (Join-Path $FrozenRoot $name) (Join-Path $source $name) /E /XD bin obj tests /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "robocopy failed for $name with exit code $LASTEXITCODE" }
}
Copy-Item -LiteralPath (Join-Path $FrozenRoot "Directory.Build.props") -Destination (Join-Path $source "Directory.Build.props")
Set-Content -LiteralPath $marker -Value $expectedHead -NoNewline
return $source
