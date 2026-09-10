param(
    [Parameter(Mandatory = $true)] [string]$FrozenRoot,
    [Parameter(Mandatory = $true)] [string]$OutputRoot
)

$ErrorActionPreference = "Stop"
$resolvedFrozen = (Resolve-Path -LiteralPath $FrozenRoot).Path
$resolvedOutput = (New-Item -ItemType Directory -Force -Path $OutputRoot).FullName
$expectedHead = "24a11fe28f9647cd532c41f56f7ab18c00fb8516"
$actualHead = (& git -C $resolvedFrozen rev-parse HEAD).Trim()
if ($actualHead -ne $expectedHead) { throw "FrozenRoot HEAD mismatch: expected $expectedHead, got $actualHead" }
$version = (Get-Content -LiteralPath (Join-Path $resolvedFrozen "VERSION") -Raw).Trim()
if ($version -ne "1.16.1") { throw "FrozenRoot VERSION mismatch: expected 1.16.1, got $version" }
$raw = Join-Path $resolvedOutput "raw"
$log = Join-Path $raw "presentation-consumer-rerun.log"
if (Test-Path -LiteralPath $log) { throw "Refusing to overwrite existing raw log: $log; choose a new OutputRoot" }
New-Item -ItemType Directory -Force -Path $raw | Out-Null
$build = Join-Path $resolvedOutput "build\pconsumer"
$source = Join-Path $resolvedOutput "build\source"
New-Item -ItemType Directory -Force -Path $build | Out-Null
& (Join-Path $resolvedOutput "repro\prepare-source.ps1") -FrozenRoot $resolvedFrozen -OutputRoot $resolvedOutput
$project = Join-Path $resolvedOutput "repro\PresentationConsumer.csproj"
dotnet build $project -c Release -p:SourceRoot=$source -o $build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet (Join-Path $build "PresentationConsumer.dll") *>&1 | Tee-Object -FilePath $log
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
