param([Parameter(Mandatory = $true)][string]$Target, [Parameter(Mandatory = $true)][string]$LockPath)
$lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
$root = Join-Path $Target 'ws-game-1.3.0\adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core'
$names = @('Core.Foundation.dll','Core.Numbers.dll','Core.Rules.dll','Core.Carriers.dll','Core.Gameplay.dll','Presentation.Common.dll')
$matches = 0
foreach ($name in $names) {
    $path = Join-Path $root $name
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $expected = $lock.dlls.$name
    $ok = $actual -eq $expected
    if ($ok) { $matches++ }
    "name=$name expected=$expected actual=$actual match=$ok"
}
$count = (Get-ChildItem -LiteralPath $Target -File -Recurse | Measure-Object).Count
"TARGET_FILE_COUNT=$count VERSION=$($lock.version) GIT_COMMIT=$($lock.git_commit) DLL_MATCHES=$matches/$($names.Count)"
