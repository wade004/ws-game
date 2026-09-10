param(
  [string]$RepoRoot = 'D:\workespace\ws-game-artifacts\audit-76d16a5-frozen',
  [string]$BuildRoot = 'D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\build\core\source',
  [string]$EvidenceRoot = (Join-Path $PSScriptRoot '..')
)
$ErrorActionPreference='Stop'
if(!(Test-Path (Join-Path $RepoRoot '.git'))){ throw "RepoRoot is not a git checkout: $RepoRoot" }
$head=(git -C $RepoRoot rev-parse HEAD).Trim()
if($head -ne '76d16a54e54f0f11204d97d7460563c8a0dc8cd8'){throw "unexpected freeze HEAD: $head"}
New-Item -ItemType Directory -Force -Path $BuildRoot | Out-Null
$src=(Resolve-Path -LiteralPath $BuildRoot).Path
New-Item -ItemType Directory -Force -Path $src | Out-Null
foreach($part in @('core','presentation','adapters\stub')){
  $from=Join-Path $RepoRoot $part; $to=Join-Path $src $part
  robocopy $from $to /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
  if($LASTEXITCODE -gt 7){throw "copy failed: $part exit=$LASTEXITCODE"}
}
Copy-Item (Join-Path $RepoRoot 'Directory.Build.props') (Join-Path $src 'Directory.Build.props') -Force
$probes=@('GobjLockBoundaryProbe','QuestWorldFlagValueBoundaryProbe','SkillHotReloadBoundaryProbe','FollowupCoreProbe','TeleportLoadingBoundaryProbe','CoreBoundaryProbe')
foreach($probe in $probes){
  $cs=Join-Path $EvidenceRoot ('core\repro\'+$probe+'.csproj')
  $out=Join-Path $EvidenceRoot ('build\core\out\'+$probe)
  New-Item -ItemType Directory -Force -Path $out | Out-Null
  dotnet restore $cs "--property:FrameworkRoot=$src"
  if($LASTEXITCODE -ne 0){throw "restore failed: $probe exit=$LASTEXITCODE"}
  dotnet build $cs -c Release -o $out --no-restore "--property:FrameworkRoot=$src"
  $buildExit=$LASTEXITCODE
  if($buildExit -ne 0){throw "build failed: $probe exit=$buildExit"}
  & dotnet exec (Join-Path $out ($probe+'.dll'))
  $runExit=$LASTEXITCODE
  if($runExit -ne 0){throw "probe failed: $probe exit=$runExit"}
}
Write-Host 'All probes completed; interpret logs using core-findings.md and coverage-old-fix-matrix.md.'
