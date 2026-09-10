param(
    [Parameter(Mandatory = $true)] [string]$FrozenRoot,
    [Parameter(Mandatory = $true)] [string]$OutputRoot
)

$ErrorActionPreference = "Stop"
$resolvedFrozen = (Resolve-Path -LiteralPath $FrozenRoot).Path
$resolvedOutput = (New-Item -ItemType Directory -Force -Path $OutputRoot).FullName
$expectedHead = "4faab73e7081f2984e7addb88fee051b6c0d3d02"
$actualHead = (& git -C $resolvedFrozen rev-parse HEAD).Trim()
if ($actualHead -ne $expectedHead) { throw "FrozenRoot HEAD mismatch: expected $expectedHead, got $actualHead" }
$version = (Get-Content -LiteralPath (Join-Path $resolvedFrozen "VERSION") -Raw).Trim()
if ($version -ne "1.16.2") { throw "FrozenRoot VERSION mismatch: expected 1.16.2, got $version" }
$raw = Join-Path $resolvedOutput "raw"
$log = Join-Path $raw "presentation-consumer-rerun.log"
if (Test-Path -LiteralPath $log) { throw "Refusing to overwrite existing raw log: $log; choose a new OutputRoot" }
New-Item -ItemType Directory -Force -Path $raw | Out-Null
$build = Join-Path $resolvedOutput "build\pconsumer"
$source = Join-Path $resolvedOutput "build\source"
New-Item -ItemType Directory -Force -Path $build | Out-Null
$reproRoot = $PSScriptRoot
& (Join-Path $reproRoot "prepare-source.ps1") -FrozenRoot $resolvedFrozen -OutputRoot $resolvedOutput
$project = Join-Path $reproRoot "PresentationConsumer.csproj"
dotnet build $project -c Release -p:SourceRoot=$source -o $build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath (Join-Path $resolvedFrozen "toolchain\schema_audit_allowlist.json") -Destination (Join-Path $build "schema_audit_allowlist.json")
$env:VALIDATION_ALLOWLIST = Join-Path $build "schema_audit_allowlist.json"
dotnet (Join-Path $build "PresentationConsumer.dll") *>&1 | Tee-Object -FilePath $log
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
