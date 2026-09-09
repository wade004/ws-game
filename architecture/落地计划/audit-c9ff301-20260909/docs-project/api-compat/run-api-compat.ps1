[CmdletBinding()]
param(
    [string]$ReleaseRepo = "D:\workespace\ws-game",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path -LiteralPath $ReleaseRepo).Path
$OutputDir = if ([string]::IsNullOrWhiteSpace($OutputDir)) { Join-Path $PSScriptRoot ("repro-run-" + [guid]::NewGuid().ToString("N")) } else { $OutputDir }
$oldZip = Join-Path $repo "dist\ws-game-1.12.0.zip"
$newZip = Join-Path $repo "dist\ws-game-1.13.0.zip"
$oldEntry = "ws-game-1.12.0/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Plugins/Core/Core.Foundation.dll"
$newEntry = "ws-game-1.13.0/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Plugins/Core/Core.Foundation.dll"
$out = [IO.Path]::GetFullPath($OutputDir)
if (Test-Path -LiteralPath $out) { throw "OutputDir already exists; choose a new empty path: $out" }
New-Item -ItemType Directory -Force -Path (Join-Path $out "lib") | Out-Null

function Get-ZipEntry([string]$zipPath, [string]$entryName, [string]$destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entry = $archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -ceq $entryName }
        if ($null -eq $entry) { throw "Missing exact ZIP entry: $entryName" }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        $stream = $entry.Open()
        try {
            $target = [IO.File]::Create($destination)
            try { $stream.CopyTo($target) } finally { $target.Dispose() }
        } finally { $stream.Dispose() }
    } finally { $archive.Dispose() }
}

function Hash([string]$path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant() }
function Invoke-Capture([string]$exe, [string[]]$arguments, [string]$log) {
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & $exe @arguments *> $log
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
}

$source = Join-Path $PSScriptRoot "Program.cs"
$project = Join-Path $PSScriptRoot "FieldSchemaConsumer.csproj"
Copy-Item $source (Join-Path $out "Program.cs")
Copy-Item $project (Join-Path $out "FieldSchemaConsumer.csproj")
$oldDll = Join-Path $out "lib\Core.Foundation.dll"
Get-ZipEntry $oldZip $oldEntry $oldDll
$newDll = Join-Path $out "formal-1.13\Core.Foundation.dll"
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("release_repo=$repo")
$lines.Add("old_zip=$oldZip")
$lines.Add("new_zip=$newZip")
$lines.Add("old_entry=$oldEntry sha256=$(Hash $oldDll)")

$buildLog = Join-Path $out "build.log"
$buildExit = Invoke-Capture "dotnet" @("build", (Join-Path $out "FieldSchemaConsumer.csproj"), "-c", "Release", "--nologo") $buildLog
$lines.Add("build_exit=$buildExit")
if ($buildExit -ne 0) { throw "consumer build failed; see $buildLog" }
$runDll = Join-Path $out "bin\Release\net8.0\FieldSchemaConsumer.dll"
$oldRunLog = Join-Path $out "run-old.log"
$oldExit = Invoke-Capture "dotnet" @($runDll) $oldRunLog
$lines.Add("run_old_exit=$oldExit")
$lines.Add("run_old_output=$((Get-Content $oldRunLog -Raw).Trim())")
if ($oldExit -ne 0) { throw "old consumer run failed; see $oldRunLog" }

Get-ZipEntry $newZip $newEntry $newDll
$lines.Add("new_entry=$newEntry sha256=$(Hash $newDll)")
Copy-Item $newDll (Join-Path (Split-Path $runDll) "Core.Foundation.dll") -Force
$newRunLog = Join-Path $out "run-replaced-1.13.log"
$newExit = Invoke-Capture "dotnet" @($runDll) $newRunLog
$newOutput = (Get-Content $newRunLog -Raw).Trim()
$lines.Add("run_replaced_1.13_exit=$newExit")
$lines.Add("run_replaced_1.13_output=$newOutput")
$observed = ($newOutput -match "MissingMethodException")
$lines.Add("expected_missing_method_observed=$observed")
if (-not $observed) { throw "expected MissingMethodException was not observed" }
$lines.Add("result=PASS_EXPECTED_ABI_BREAK")
$lines | Set-Content -LiteralPath (Join-Path $PSScriptRoot "api-compat-repro.log") -Encoding UTF8
$lines | ForEach-Object { Write-Output $_ }
