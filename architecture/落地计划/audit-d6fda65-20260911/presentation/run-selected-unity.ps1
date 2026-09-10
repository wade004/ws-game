$ErrorActionPreference = 'Stop'
$base = $PSScriptRoot
$project = Join-Path $base 'UnityCopy\adapters\unity'
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe'
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$args = @('-batchmode','-projectPath',$project,'-runTests','-testPlatform','PlayMode',
    '-testFilter','EquipmentVisualSaveLoadResetTests;VfxAnchorFollowTests;HitFrameSyncEndToEndTests;SharedBootstrapDiscreteTests;Audit118PresentationTests',
    '-testResults',(Join-Path $base "playmode-selected-$stamp.xml"),
    '-logFile',(Join-Path $base "playmode-selected-$stamp.log"))
$process = Start-Process -FilePath $unity -ArgumentList $args -PassThru -Wait -WindowStyle Hidden
Write-Output "Unity exit=$($process.ExitCode); inspect XML assertion results, not only exit code."
