param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot '..\build\core\source'),
    [string]$EvidenceRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
$log = Join-Path $EvidenceRoot 'logs\runner-current.log'
$exitFile = Join-Path $EvidenceRoot 'logs\runner-current.exit.txt'
$outRoot = Join-Path $EvidenceRoot 'build\core\out'
New-Item -ItemType Directory -Force -Path (Split-Path $log), $outRoot | Out-Null

$projects = @(
    @{ Name = 'GameplayCore114'; Project = Join-Path $SourceRoot 'core\gameplay\tests\Tests.Gameplay.csproj'; Filter = 'FullyQualifiedName~CORE114_01|FullyQualifiedName~CORE114_02|FullyQualifiedName~CORE114_04|FullyQualifiedName~QuestPersistableTests|FullyQualifiedName~TP_111_FollowupAuditTests' },
    @{ Name = 'NumbersCore114'; Project = Join-Path $SourceRoot 'core\numbers\tests\Tests.Numbers.csproj'; Filter = 'FullyQualifiedName~CORE114_03|FullyQualifiedName~P2_05_PowerHostReloadTests|FullyQualifiedName~ProgressionPersistableTests' },
    @{ Name = 'EquipmentAndImmunity'; Project = Join-Path $SourceRoot 'core\carriers\tests\Tests.Carriers.csproj'; Filter = 'FullyQualifiedName~CORE114_EquipmentSetBonusReloadOrphanTests|FullyQualifiedName~EffectImmunityGateTests|FullyQualifiedName~ItemPersistableTests|FullyQualifiedName~UnitPersistableTests|FullyQualifiedName~EntitySpatialSyncHostTests|FullyQualifiedName~ISkillHost_FindUnitsTests' }
)

if ((Test-Path -LiteralPath $log) -or (Test-Path -LiteralPath $exitFile)) {
    throw "refusing to overwrite immutable raw log: $log"
}
foreach ($entry in $projects) {
    $consoleLog = Join-Path $outRoot "$($entry.Name).console.log"
    if (Test-Path -LiteralPath $consoleLog) {
        throw "refusing to overwrite immutable raw capture: $consoleLog"
    }
}
"CORE-BOUNDED-RUN baseline=24a11fe28f9647cd532c41f56f7ab18c00fb8516 version=1.16.1" | Set-Content -LiteralPath $log
"source=$SourceRoot" | Add-Content -LiteralPath $log
$overall = 0
foreach ($entry in $projects) {
    "PROJECT=$($entry.Name) FILTER=$($entry.Filter)" | Add-Content -LiteralPath $log
    $output = & dotnet test $entry.Project -c Release --filter $entry.Filter --logger 'console;verbosity=normal' 2>&1
    $code = $LASTEXITCODE
    $output | Tee-Object -FilePath (Join-Path $outRoot "$($entry.Name).console.log") | Add-Content -LiteralPath $log
    "PROJECT_EXIT=$code" | Add-Content -LiteralPath $log
    if ($code -ne 0) { $overall = $code }
}
"RUNNER_EXIT=$overall" | Add-Content -LiteralPath $log
Set-Content -LiteralPath $exitFile -Value $overall
exit $overall
