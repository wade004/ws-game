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
$auditLog = Join-Path $raw "validator-schema-audit-rerun.json"
$sampleLog = Join-Path $raw "validator-sample-list-rerun.json"
foreach ($log in @($auditLog, $sampleLog)) {
    if (Test-Path -LiteralPath $log) { throw "Refusing to overwrite existing raw log: $log; choose a new OutputRoot" }
}
$allowlist = Join-Path $resolvedFrozen "toolchain\schema_audit_allowlist.json"
$sampleRoot = Join-Path $resolvedFrozen "data\_sample"
if (-not (Test-Path -LiteralPath $allowlist)) { throw "Missing frozen allowlist: $allowlist" }
New-Item -ItemType Directory -Force -Path $raw | Out-Null
$build = Join-Path $resolvedOutput "build\validator"
$source = Join-Path $resolvedOutput "build\source"
New-Item -ItemType Directory -Force -Path $build | Out-Null
& (Join-Path $resolvedOutput "repro\prepare-source.ps1") -FrozenRoot $resolvedFrozen -OutputRoot $resolvedOutput
$project = Join-Path $source "toolchain\validator\Validator.csproj"
dotnet build $project -c Release -o $build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$validator = Join-Path $build "Validator.dll"
dotnet $validator --schema-audit --allowlist $allowlist --json *>&1 | Tee-Object -FilePath $auditLog
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet $validator --data-root $sampleRoot --json --list-tables *>&1 | Tee-Object -FilePath $sampleLog
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
