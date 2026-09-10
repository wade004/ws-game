param(
    [string]$EvidenceRoot = (Join-Path $PSScriptRoot '..'),
    [string]$SourceRoot = (Join-Path $PSScriptRoot '..\build\core\source')
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $EvidenceRoot 'repro\CoreBoundaryProbe.csproj'
$log = Join-Path $EvidenceRoot 'logs\CoreBoundaryProbe.current.log'
$exitFile = Join-Path $EvidenceRoot 'logs\CoreBoundaryProbe.current.exit.txt'
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
if ((Test-Path -LiteralPath $log) -or (Test-Path -LiteralPath $exitFile)) {
    throw "refusing to overwrite immutable raw log: $log"
}
$output = & dotnet run --project $project -c Release --property:FrameworkRoot=$SourceRoot 2>&1
$code = $LASTEXITCODE
$output | Set-Content -LiteralPath $log
Set-Content -LiteralPath $exitFile -Value $code
exit $code
