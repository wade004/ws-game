param(
    [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe',
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot,
    [string]$EvidenceRoot = (Join-Path $PSScriptRoot '..'),
    [ValidateSet('filtered', 'full')]
    [string]$Mode = 'filtered'
)

$ErrorActionPreference = 'Stop'
$evidence = (Resolve-Path -LiteralPath $EvidenceRoot).Path
$project = (Resolve-Path -LiteralPath $ProjectRoot).Path
if (!(Test-Path -LiteralPath $UnityExe)) { throw "Unity executable not found: $UnityExe" }
if (!(Test-Path -LiteralPath (Join-Path $project 'ProjectSettings\ProjectVersion.txt'))) { throw "not a Unity project: $project" }
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'logs') | Out-Null

function Invoke-UnityTests([string]$platform, [string]$filter, [string]$stem, [switch]$NoGraphics) {
    $xml = Join-Path $evidence "$stem.xml"
    $log = Join-Path $evidence "logs\$stem.log"
    $exitFile = Join-Path $evidence "$stem.exit.txt"
    if ((Test-Path -LiteralPath $xml) -or (Test-Path -LiteralPath $log) -or (Test-Path -LiteralPath $exitFile)) {
        throw "refusing to overwrite immutable raw capture: $xml, $log, or $exitFile"
    }
    $args = @('-batchmode', '-projectPath', $project, '-runTests', '-testPlatform', $platform,
        '-testResults', $xml, '-logFile', $log)
    if ($filter) { $args += @('-testFilter', $filter) }
    if ($NoGraphics) { $args += '-nographics' }
    $proc = Start-Process -FilePath $UnityExe -ArgumentList $args -PassThru -Wait -WindowStyle Hidden
    Set-Content -LiteralPath $exitFile -Value $proc.ExitCode -Encoding ascii
    if ($proc.ExitCode -ne 0) { throw "$platform tests failed: exit=$($proc.ExitCode) xml=$xml log=$log" }
    Write-Output "platform=$platform filter=$filter exit=$($proc.ExitCode) xml=$xml log=$log"
}

if ($Mode -eq 'filtered') {
    Invoke-UnityTests 'EditMode' 'DataHotReloadDeletedOverlayTests' 'editmode-current' -NoGraphics
    Invoke-UnityTests 'PlayMode' 'EquipmentVisualSaveLoadResetTests|VfxAnchorFollowTests' 'playmode-current'
} else {
    Invoke-UnityTests 'EditMode' '' 'full-edit' -NoGraphics
    Invoke-UnityTests 'PlayMode' '' 'full-play'
}
