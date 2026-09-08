Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = 'D:\workespace\ws-game'
$dist = Join-Path $root 'dist'
$zipPath = Join-Path $dist 'ws-game-1.4.0.zip'
$lockPath = Join-Path $dist 'ws-game-1.4.0.lock'
$manifestPath = Join-Path $dist '1.4.0\MANIFEST.txt'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
Get-Content -LiteralPath $manifestPath | Select-Object -First 5
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    "ZIP_ENTRY_COUNT=$($zip.Entries.Count)"
    $names = @('Core.Foundation.dll','Core.Numbers.dll','Core.Rules.dll','Core.Carriers.dll','Core.Gameplay.dll','Presentation.Common.dll')
    $matches = 0
    foreach ($name in $names) {
        $suffix = "ws-game-1.4.0\adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core\$name"
        $entry = $zip.Entries | Where-Object { $_.FullName -eq $suffix }
        $stream = $entry.Open()
        try { $sha = [Security.Cryptography.SHA256]::Create(); try { $actual = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() } } finally { $stream.Dispose() }
        $expected = $lock.dlls.$name
        $ok = $actual -eq $expected
        if ($ok) { $matches++ }
        "name=$name expected=$expected actual=$actual match=$ok"
    }
    "VERSION=$($lock.version) GIT_COMMIT=$($lock.git_commit) DLL_MATCHES=$matches/6"
    foreach ($label in @('GeneratePlaceholderModelAssets.cs','Resources/GameFoundation','\.prefab$','\.controller$','\.anim$')) {
        $count = @($zip.Entries | Where-Object { $_.FullName.Replace([char]92,[char]47) -match $label }).Count
        "ZIP_MATCH[$label]=$count"
    }
}
finally { $zip.Dispose() }
foreach ($tgz in Get-ChildItem -LiteralPath (Join-Path $dist '1.4.0\packages') -Filter '*.tgz') {
    $entries = @(tar -tf $tgz.FullName)
    "TGZ=$($tgz.Name) ENTRY_COUNT=$($entries.Count) MODEL_GENERATOR_MATCHES=$(@($entries | Where-Object { $_ -match 'GeneratePlaceholderModelAssets|Resources/GameFoundation|\.prefab$|\.controller$|\.anim$' }).Count) VALIDATOR_LIB_DLLS=$(@($entries | Where-Object { $_ -match 'validator/lib/.*\.dll$' }).Count)"
}
