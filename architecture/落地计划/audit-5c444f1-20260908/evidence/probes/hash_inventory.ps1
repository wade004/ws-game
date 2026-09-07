param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$LockPath
)

Add-Type -AssemblyName System.IO.Compression.FileSystem
$lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
$zip = [IO.Compression.ZipFile]::OpenRead($ZipPath)
$names = @(
    'Core.Foundation.dll', 'Core.Numbers.dll', 'Core.Rules.dll',
    'Core.Carriers.dll', 'Core.Gameplay.dll', 'Presentation.Common.dll'
)
$matches = 0
try {
    foreach ($name in $names) {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq "ws-game-1.3.0\adapters\unity\Packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core\$name" }
        if ($null -eq $entry) { throw "missing zip entry: $name" }
        $stream = $entry.Open()
        try {
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $actual = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
            finally { $sha.Dispose() }
        }
        finally { $stream.Dispose() }
        $expected = $lock.dlls.$name
        $ok = $actual -eq $expected
        if ($ok) { $matches++ }
        "name=$name expected=$expected actual=$actual match=$ok"
    }
}
finally { $zip.Dispose() }
"VERSION=$($lock.version) GIT_COMMIT=$($lock.git_commit) DLL_MATCHES=$matches/$($names.Count)"
