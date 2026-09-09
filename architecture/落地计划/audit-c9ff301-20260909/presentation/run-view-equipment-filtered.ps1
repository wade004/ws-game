param(
    [string]$CopyRoot = 'D:\workespace\ws-game-unity-audit-c9ff301\copyRoot',
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe',
    [string]$OutputDirectory = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
if ([IO.Path]::GetFullPath($CopyRoot).TrimEnd('\') -ne 'D:\workespace\ws-game-unity-audit-c9ff301\copyRoot') {
    throw 'Refusing a CopyRoot outside the external audit copy.'
}
$prepare = Join-Path $PSScriptRoot 'prepare-view-equipment-fixture.ps1'
$restore = Join-Path $PSScriptRoot 'restore-view-equipment-fixture.ps1'
$runtimeTests = Join-Path $CopyRoot 'games\_template\Tests\Runtime'
$source = Join-Path $PSScriptRoot 'ExistingViewEquipmentSaveLoadAuditTests.cs'
$meta = Join-Path $runtimeTests 'ExistingViewEquipmentSaveLoadAuditTests.cs.meta'
$target = Join-Path $runtimeTests 'ExistingViewEquipmentSaveLoadAuditTests.cs'
$disabled = Join-Path $runtimeTests 'ExistingViewEquipmentSaveLoadAuditTests.cs.audit-disabled'
$disabledMeta = Join-Path $runtimeTests 'ExistingViewEquipmentSaveLoadAuditTests.cs.audit-disabled.meta'
$xml = Join-Path $OutputDirectory 'view-equipment-filtered-runner.xml'
$log = Join-Path $OutputDirectory 'view-equipment-filtered-runner.log'
$hadTarget = Test-Path -LiteralPath $target
$hadMeta = Test-Path -LiteralPath $meta
$hadDisabled = Test-Path -LiteralPath $disabled
$hadDisabledMeta = Test-Path -LiteralPath $disabledMeta
Copy-Item -LiteralPath $source -Destination $target -Force
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'ExistingViewEquipmentSaveLoadAuditTests.cs.meta')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ExistingViewEquipmentSaveLoadAuditTests.cs.meta') -Destination $meta -Force
}
& $prepare -CopyRoot $CopyRoot
try {
    if (Test-Path -LiteralPath $xml) { [IO.File]::Delete($xml) }
    if (Test-Path -LiteralPath $log) { [IO.File]::Delete($log) }
    $args = @('-batchmode','-nographics','-projectPath',(Join-Path $CopyRoot 'adapters\unity'),'-runTests','-testPlatform','PlayMode','-testFilter','ExistingViewEquipmentSaveLoadAuditTests','-testResults',$xml,'-logFile',$log)
    $process = Start-Process -FilePath $UnityPath -ArgumentList $args -PassThru -Wait -WindowStyle Hidden
    $unityExit = $process.ExitCode
    if ($unityExit -notin @(0,2)) { throw "Unity exit code $unityExit" }
    if (!(Test-Path -LiteralPath $xml)) { throw 'Filtered XML was not produced by this run' }
    [xml]$results = Get-Content -LiteralPath $xml -Raw
    $cases = @($results.SelectNodes('//test-case'))
    if ($cases.Count -ne 1) { throw "Expected exactly one filtered test case, got $($cases.Count)" }
    $failureText = $cases[0].failure.InnerText
    if ($cases[0].result -ne 'Failed' -or $failureText -notmatch 'Expected:\s*0' -or $failureText -notmatch 'But was:\s*1') { throw 'Filtered result did not capture the expected stale-visual oracle failure' }
    $text = Get-Content -LiteralPath $log -Raw
    foreach ($needle in @('equipment_after_load=0','socket_childCount_before=1','socket_childCount_after=1','view_same=True','socket_same=True','socket_wait expected=0;actual=1')) {
        if ($text.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { throw "Missing required oracle evidence: $needle" }
    }
    "runner=OK;unity_exit=$unityExit;cases=$($cases.Count);result=$($cases[0].result);expected_socket_after=0;actual_socket_after=1;view_same=True;socket_same=True" | Set-Content -LiteralPath (Join-Path $OutputDirectory 'view-equipment-runner-verification.log') -Encoding utf8
}
finally {
    & $restore -CopyRoot $CopyRoot
    if ($hadTarget) {
        Copy-Item -LiteralPath $source -Destination $target -Force
    } else {
        [IO.File]::Delete($target)
    }
    if (-not $hadMeta -and (Test-Path -LiteralPath $meta)) { [IO.File]::Delete($meta) }
    if ($hadDisabled) {
        if (!(Test-Path -LiteralPath $disabled)) { Copy-Item -LiteralPath $source -Destination $disabled -Force }
    } elseif (Test-Path -LiteralPath $disabled) { [IO.File]::Delete($disabled) }
    if ($hadDisabledMeta) {
        if (!(Test-Path -LiteralPath $disabledMeta) -and (Test-Path -LiteralPath $meta)) { Copy-Item -LiteralPath $meta -Destination $disabledMeta -Force }
    } elseif (Test-Path -LiteralPath $disabledMeta) { [IO.File]::Delete($disabledMeta) }
}
