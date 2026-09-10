param(
    [ValidateSet('filtered','full')]
    [string]$Mode = 'filtered'
)

$ErrorActionPreference = 'Stop'
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe'
$project = 'D:\workespace\ws-game-unity-audit-76d16a5\copyRoot\adapters\unity'
$evidence = 'D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\presentation\rerun'
New-Item -ItemType Directory -Force $evidence | Out-Null

function Invoke-UnityTests([string]$platform, [switch]$headless, [string]$filter) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $stem = "$platform-$Mode-$stamp"
    $xml = Join-Path $evidence "$stem.xml"
    $log = Join-Path $evidence "$stem.log"
    $args = @('-batchmode','-projectPath',$project,'-runTests','-testPlatform',$platform,
              '-testResults',$xml,'-logFile',$log)
    if ($headless) { $args += '-nographics' }
    if ($filter) { $args += @('-testFilter',$filter) }
    $proc = Start-Process -FilePath $unity -ArgumentList $args -PassThru -Wait -WindowStyle Hidden
    "platform=$platform filter=$filter exit=$($proc.ExitCode) xml=$xml log=$log"
}

if ($Mode -eq 'filtered') {
    Invoke-UnityTests 'EditMode' -headless 'DataHotReloadDeletedOverlayTests'
    Invoke-UnityTests 'PlayMode' 'EquipmentVisualSaveLoadResetTests|VfxAnchorFollowTests'
} else {
    Invoke-UnityTests 'EditMode' -headless
    Invoke-UnityTests 'PlayMode'
}
