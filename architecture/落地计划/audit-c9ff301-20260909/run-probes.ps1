param(
    [string]$RepoRoot = "",
    [string]$FrameworkRoot = ""
)
$ErrorActionPreference = "Stop"
$AuditRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = (Resolve-Path (Join-Path $AuditRoot "..\..\..")).Path }
else { $RepoRoot = (Resolve-Path $RepoRoot).Path }
if ([string]::IsNullOrWhiteSpace($FrameworkRoot)) { $FrameworkRoot = Join-Path (Split-Path -Parent $RepoRoot) "audit-c9ff301-corebuild" }
else { $FrameworkRoot = [IO.Path]::GetFullPath($FrameworkRoot) }
$Repo = $RepoRoot
$BuildRoot = $FrameworkRoot
$ExpectedHead = "c9ff30107413083188c597c0b65cf1691c9dfe9b"
$actualHead = (git -C $Repo rev-parse HEAD).Trim()
if ($actualHead -ne $ExpectedHead) { throw "冻结 HEAD 不匹配: $actualHead" }
$version = (Get-Content (Join-Path $Repo "VERSION") -Raw).Trim()
if ($version -ne "1.13.0") { throw "VERSION 不匹配: $version" }

function Copy-SourceTree([string]$src, [string]$dst) {
    Get-ChildItem -LiteralPath $src -Force | Where-Object { $_.Name -notin @("bin", "obj") } | ForEach-Object {
        $target = Join-Path $dst $_.Name
        if ($_.PSIsContainer) { New-Item -ItemType Directory -Force -Path $target | Out-Null; Copy-SourceTree $_.FullName $target }
        else { Copy-Item -LiteralPath $_.FullName -Destination $target -Force }
    }
}
New-Item -ItemType Directory -Force -Path (Join-Path $BuildRoot "core"), (Join-Path $BuildRoot "presentation"), (Join-Path $BuildRoot "adapters\stub") | Out-Null
Copy-SourceTree (Join-Path $Repo "core") (Join-Path $BuildRoot "core")
Copy-SourceTree (Join-Path $Repo "presentation") (Join-Path $BuildRoot "presentation")
Copy-SourceTree (Join-Path $Repo "adapters\stub") (Join-Path $BuildRoot "adapters\stub")
Copy-Item -LiteralPath (Join-Path $Repo "Directory.Build.props") -Destination (Join-Path $BuildRoot "Directory.Build.props") -Force

$files = @(git -C $Repo ls-files -- core presentation adapters/stub | Where-Object { $_ -match "\.(cs|csproj)$" -and $_ -notmatch "(^|/)(bin|obj)/" })
$hashRows = @("SOURCE-COPY-HASHES baseline=$ExpectedHead", "tracked paths core/presentation/adapters/stub; extensions cs/csproj; bin/obj excluded")
$match=0; $mismatch=0; $missing=0
foreach ($rel in $files) {
    $src = Join-Path $Repo ($rel -replace "/", "\")
    $dst = Join-Path $BuildRoot ($rel -replace "/", "\")
    if (-not (Test-Path -LiteralPath $dst)) { $missing++; $hashRows += "MISSING $rel"; continue }
    $h1 = (Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash
    $h2 = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
    if ($h1 -eq $h2) { $match++; $hashRows += "MATCH $h1 $rel" }
    else { $mismatch++; $hashRows += "MISMATCH $h1 $h2 $rel" }
}
$hashRows = @($hashRows[0..1] + "FILES=$($files.Count) MATCH=$match MISMATCH=$mismatch MISSING=$missing" + $hashRows[2..($hashRows.Count-1)])
$hashRows | Set-Content (Join-Path $AuditRoot "core\evidence\source-copy-hashes.txt") -Encoding utf8
if ($mismatch -ne 0 -or $missing -ne 0) { throw "source copy hash check failed: mismatch=$mismatch missing=$missing" }

function Run-Probe([string]$Name, [string]$Project) {
    $log = Join-Path $AuditRoot "core\logs\$Name.log"
    $outputDir = Join-Path $AuditRoot "core\logs\build\$Name"
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    Write-Output "RUN $Name project=<$Project> exists=$(Test-Path -LiteralPath $Project)"
    $projectDir = Split-Path -Parent $Project
    $projectFile = Split-Path -Leaf $Project
    $assembly = [IO.Path]::GetFileNameWithoutExtension($projectFile)
    Push-Location $projectDir
    try {
        $buildOutput = @(& dotnet build $projectFile --nologo -o $outputDir -p:FrameworkRoot=$BuildRoot 2>&1)
        $buildCode = $LASTEXITCODE
        if ($buildCode -ne 0) { $buildOutput | Set-Content -LiteralPath $log -Encoding utf8; throw "$Name build=$buildCode" }
        $dll = Join-Path $outputDir ($assembly + ".dll")
        if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "$Name output dll not found: $dll" }
        $runOutput = @(& dotnet exec $dll 2>&1)
        $runCode = $LASTEXITCODE
        $buildOutput + $runOutput | Tee-Object -FilePath $log
        if ($runCode -ne 0) { throw "$Name run=$runCode" }
    }
    finally { Pop-Location }
    Set-Content ($log + ".exit.txt") $runCode
}
$repro = Join-Path $AuditRoot "core\repro"
Run-Probe "followup-core-current" (Join-Path $repro "FollowupCoreProbe.csproj")
Run-Probe "teleport-loading-current" (Join-Path $repro "TeleportLoadingBoundaryProbe.csproj")
Run-Probe "gobj-lock-boundary" (Join-Path $repro "GobjLockBoundaryProbe.csproj")
Run-Probe "skill-hot-reload-boundary" (Join-Path $repro "SkillHotReloadBoundaryProbe.csproj")
Run-Probe "quest-world-flag-value-boundary" (Join-Path $repro "QuestWorldFlagValueBoundaryProbe.csproj")
Write-Output "DONE baseline=$ExpectedHead source-files=$($files.Count) match=$match mismatch=$mismatch missing=$missing buildroot=$BuildRoot"
