[CmdletBinding()]
param(
    [string]$FrozenRoot = 'D:\workespace\ws-game-artifacts\audit-24a11fe-frozen',
    [Parameter(Mandatory=$true)][string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$expectedHead = '24a11fe28f9647cd532c41f56f7ab18c00fb8516'
$frozenFull = [IO.Path]::GetFullPath($FrozenRoot).TrimEnd('\')
$outFull = [IO.Path]::GetFullPath($OutputRoot).TrimEnd('\')
if (-not (Test-Path -LiteralPath $frozenFull)) { throw "FrozenRoot not found: $frozenFull" }
if (Test-Path -LiteralPath $outFull) { throw "OutputRoot already exists; refusing to overwrite evidence: $outFull" }
$actualHead = (& git -C $frozenFull rev-parse HEAD).Trim()
if ($actualHead -ne $expectedHead) { throw "FrozenRoot HEAD mismatch: expected $expectedHead got $actualHead" }

$sourceRoot = $PSScriptRoot
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
foreach ($relative in @('ApiBaseline\ApiBaseline.csproj','ApiBaseline\Api.cs','ApiCurrent\ApiCurrent.csproj','ApiCurrent\Api.cs','Consumer\Consumer.csproj','Consumer\Program.cs')) {
    $source = Join-Path $sourceRoot $relative
    $dest = Join-Path $outFull $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
    Copy-Item -LiteralPath $source -Destination $dest
}
$surfaceSource = Join-Path $frozenFull 'toolchain\abi_surface'
$surfaceMirror = Join-Path $outFull 'toolchain\abi_surface'
New-Item -ItemType Directory -Force -Path $surfaceMirror | Out-Null
Get-ChildItem -LiteralPath $surfaceSource -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $surfaceMirror $_.Name)
}

function Run-Dotnet([string]$Project, [string]$OutDir, [string]$LogPath) {
    $projectName = [IO.Path]::GetFileNameWithoutExtension($Project)
    $objRoot = Join-Path $outFull ("obj\$projectName\")
    $binRoot = Join-Path $outFull ("bin\$projectName\")
    & dotnet build $Project -c Release --nologo -o $OutDir "-p:BaseIntermediateOutputPath=$objRoot" "-p:BaseOutputPath=$binRoot" *> $LogPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed: $Project (see $LogPath)" }
}

$baselineBuild = Join-Path $outFull 'build\baseline'
$currentBuild = Join-Path $outFull 'build\current'
$consumerSource = Join-Path $outFull 'Consumer'
$consumerLib = Join-Path $consumerSource 'lib'
$consumerBin = Join-Path $consumerSource 'bin-old'
$surfaceToolOut = Join-Path $outFull 'abi_surface_tool'
New-Item -ItemType Directory -Force -Path $consumerLib,$consumerBin | Out-Null
Run-Dotnet (Join-Path $outFull 'ApiBaseline\ApiBaseline.csproj') $baselineBuild (Join-Path $outFull 'build-baseline.log')
Run-Dotnet (Join-Path $outFull 'ApiCurrent\ApiCurrent.csproj') $currentBuild (Join-Path $outFull 'build-current.log')
Copy-Item -LiteralPath (Join-Path $baselineBuild 'ApiContract.dll') -Destination (Join-Path $consumerLib 'ApiContract.dll')
Run-Dotnet (Join-Path $outFull 'Consumer\Consumer.csproj') $consumerBin (Join-Path $outFull 'build-consumer.log')

$surfaceProject = Join-Path $surfaceMirror 'AbiSurface.csproj'
Run-Dotnet $surfaceProject $surfaceToolOut (Join-Path $outFull 'build-abi-surface.log')
$surfaceTool = Join-Path $surfaceToolOut 'AbiSurface.dll'
$baselineDll = Join-Path $baselineBuild 'ApiContract.dll'
$currentDll = Join-Path $currentBuild 'ApiContract.dll'
$consumer = Join-Path $consumerBin 'OldConsumer.dll'
$surfaceBaseline = Join-Path $outFull 'surface-baseline.txt'
$surfaceCurrent = Join-Path $outFull 'surface-current.txt'
$surfaceReport = Join-Path $outFull 'surface-report.txt'
$summary = New-Object System.Collections.Generic.List[string]
$summary.Add('ABI surface negative oracle: public -> protected')
$summary.Add("frozen_head=$actualHead")
$summary.Add('rebuild_consumer_after_api_change=false')
$summary.Add("baseline_api_sha256=$((Get-FileHash $baselineDll -Algorithm SHA256).Hash.ToLowerInvariant())")
$summary.Add("current_api_sha256=$((Get-FileHash $currentDll -Algorithm SHA256).Hash.ToLowerInvariant())")
$consumerHashBefore = (Get-FileHash $consumer -Algorithm SHA256).Hash.ToLowerInvariant()
$summary.Add("consumer_sha256_before=$consumerHashBefore")

Copy-Item -LiteralPath $baselineDll -Destination (Join-Path $consumerBin 'ApiContract.dll') -Force
& dotnet $consumer *> (Join-Path $outFull 'run-baseline.log')
$baselineExit = $LASTEXITCODE
$summary.Add("run_baseline_exit=$baselineExit")
Copy-Item -LiteralPath $currentDll -Destination (Join-Path $consumerBin 'ApiContract.dll') -Force
$consumerHashAfter = (Get-FileHash $consumer -Algorithm SHA256).Hash.ToLowerInvariant()
$summary.Add("consumer_sha256_after=$consumerHashAfter")
$summary.Add("consumer_hash_unchanged=$($consumerHashBefore -eq $consumerHashAfter)")
& dotnet $consumer *> (Join-Path $outFull 'run-current.log')
$currentExit = $LASTEXITCODE
$summary.Add("run_current_exit=$currentExit")

& dotnet $surfaceTool dump --out $surfaceBaseline $baselineDll *> (Join-Path $outFull 'surface-dump-baseline.log')
$dumpBaselineExit = $LASTEXITCODE
& dotnet $surfaceTool dump --out $surfaceCurrent $currentDll *> (Join-Path $outFull 'surface-dump-current.log')
$dumpCurrentExit = $LASTEXITCODE
& dotnet $surfaceTool compare $surfaceBaseline $surfaceCurrent --out $surfaceReport *> (Join-Path $outFull 'surface-compare.log')
$compareExit = $LASTEXITCODE
$summary.Add("surface_dump_baseline_exit=$dumpBaselineExit")
$summary.Add("surface_dump_current_exit=$dumpCurrentExit")
$summary.Add("surface_compare_exit=$compareExit")
$summary.Add("surface_dump_equal=$((Get-FileHash $surfaceBaseline -Algorithm SHA256).Hash -eq (Get-FileHash $surfaceCurrent -Algorithm SHA256).Hash)")
$summary.Add('oracle_result=ABI_GATE_LEAK_CONFIRMED_IF_compare_exit_0_and_current_run_nonzero')
$summary | Set-Content (Join-Path $outFull 'summary.txt') -Encoding UTF8
$summary | Write-Output
