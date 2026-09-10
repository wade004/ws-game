param(
    [string]$BaselineZip = "D:\workespace\ws-game\dist\ws-game-1.13.0.zip",
    [string]$FormalZip = "D:\workespace\ws-game\dist\ws-game-1.14.0.zip",
    [string]$OutputDir = ""
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path (Get-Location) ("abi-skillhost-" + [Guid]::NewGuid().ToString("N"))
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputDir)
if (Test-Path -LiteralPath $resolvedOutput) { throw "OutputDir already exists; refusing to delete or overwrite: $resolvedOutput" }
New-Item -ItemType Directory -Force -Path (Join-Path $resolvedOutput "lib") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $resolvedOutput "consumer") | Out-Null
$consumerDir = Join-Path $resolvedOutput "consumer"
$sourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $sourceDir "SkillHostAbiConsumer.csproj"
$program = Join-Path $sourceDir "Program.cs"
$entryRoot = "ws-game-1.13.0\packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core\"
$names = @("Core.Foundation.dll","Core.Numbers.dll","Core.Carriers.dll","Core.Rules.dll","Core.Gameplay.dll")
function Extract-Core([string]$zipPath, [string]$destDir, [string]$rootPrefix) {
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        foreach ($name in $names) {
            $entryName = $rootPrefix + $name
            $entry = $zip.GetEntry($entryName)
            if ($null -eq $entry) { throw "Formal ZIP missing exact entry: $entryName" }
            $dest = Join-Path $destDir $name
            $stream = $entry.Open()
            try { $out = [IO.File]::Create($dest); try { $stream.CopyTo($out) } finally { $out.Dispose() } } finally { $stream.Dispose() }
            Write-Output ("EXTRACT " + $zipPath + " :: " + $entryName + " -> " + $dest)
        }
    } finally { $zip.Dispose() }
}
Write-Output ("BASELINE_ZIP=" + [IO.Path]::GetFullPath($BaselineZip))
Write-Output ("FORMAL_ZIP=" + [IO.Path]::GetFullPath($FormalZip))
Write-Output ("OUTPUT_DIR=" + $resolvedOutput)
Write-Output "SOURCE_HASHES"
Get-FileHash $project,$program -Algorithm SHA256 | ForEach-Object { Write-Output ($_.Path + " sha256=" + $_.Hash.ToLowerInvariant()) }
$oldLib = Join-Path $resolvedOutput "lib"
$buildProject = Join-Path $resolvedOutput "consumer-source.csproj"
$buildText = Get-Content -LiteralPath $project -Raw
$buildText = $buildText.Replace([string]::Concat('lib', [char]92), [string]::Concat($oldLib, [char]92))
Set-Content -LiteralPath $buildProject -Value $buildText -Encoding UTF8
Copy-Item -LiteralPath $program -Destination (Join-Path $resolvedOutput "Program.cs")
$project = $buildProject
Extract-Core $BaselineZip $oldLib $entryRoot
Write-Output "OLD_FORMAL_DLL_HASHES"
Get-ChildItem $oldLib -Filter *.dll | Sort-Object Name | Get-FileHash -Algorithm SHA256 | ForEach-Object { Write-Output ($_.Path + " sha256=" + $_.Hash.ToLowerInvariant()) }
$oldBuildLog = Join-Path $resolvedOutput "build-old.log"
$oldObj = (Join-Path $resolvedOutput "obj-old") + "\\"
& dotnet build $project -c Release --nologo -o $consumerDir -p:BaseOutputPath=$oldObj -p:BaseIntermediateOutputPath=$oldObj *> $oldBuildLog
if ($LASTEXITCODE -ne 0) { Get-Content $oldBuildLog | Write-Output; throw "consumer old build failed" }
$oldRunLog = Join-Path $resolvedOutput "run-old-formal.log"
& dotnet (Join-Path $consumerDir "SkillHostAbiConsumer.dll") *> $oldRunLog
$oldExit = $LASTEXITCODE
Write-Output ("OLD_FORMAL_RUN_EXIT=" + $oldExit)
Get-Content $oldRunLog | Write-Output
if ($oldExit -ne 0) { throw "old formal consumer run failed" }
$formalLib = Join-Path $resolvedOutput "formal-lib"
New-Item -ItemType Directory -Force -Path $formalLib | Out-Null
$formalRoot = "ws-game-1.14.0\packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core\"
Extract-Core $FormalZip $formalLib $formalRoot
Write-Output "NEW_FORMAL_DLL_HASHES"
Get-ChildItem $formalLib -Filter *.dll | Sort-Object Name | Get-FileHash -Algorithm SHA256 | ForEach-Object { Write-Output ($_.Path + " sha256=" + $_.Hash.ToLowerInvariant()) }
foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $formalLib $name) -Destination (Join-Path $consumerDir $name) }
$newRunLog = Join-Path $resolvedOutput "run-new-formal.log"
& dotnet (Join-Path $consumerDir "SkillHostAbiConsumer.dll") *> $newRunLog
$newExit = $LASTEXITCODE
Write-Output ("NEW_FORMAL_RUN_EXIT=" + $newExit)
Get-Content $newRunLog | Write-Output
if ($newExit -eq 11) { Write-Output "RESULT=CONFIRMED_BINARY_BREAK: old 17-parameter consumer fails against formal 1.14"; exit 11 }
throw "unexpected new-formal result; expected MissingMethodException exit 11"







