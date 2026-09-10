param([string]$ZipPath = 'D:\workespace\ws-game\dist\ws-game-1.14.0.zip',[string]$LockPath = 'D:\workespace\ws-game\dist\ws-game-1.14.0.lock',[string]$OutPath = 'D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\logs\release-formal-1.14.0.log')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$lock=Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
$lines=[System.Collections.Generic.List[string]]::new()
$lines.Add("zip=$ZipPath")
$lines.Add("zip_sha256=$((Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant())")
$lines.Add("lock=$LockPath")
$lines.Add("lock_version=$($lock.version)")
$lines.Add("lock_git_commit=$($lock.git_commit)")
$z=[IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
  $prefix='ws-game-1.14.0\packages\com.gamefoundation.adapter.unity\Runtime\Plugins\Core\'
  foreach($name in @('Core.Foundation.dll','Core.Numbers.dll','Core.Carriers.dll','Core.Rules.dll','Core.Gameplay.dll','Presentation.Common.dll')) {
    $entryName=$prefix+$name
    $entry=$z.GetEntry($entryName)
    if($null -eq $entry){$lines.Add("ZIP_ENTRY_MISSING $entryName");continue}
    $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create();try{$hash=($sha.ComputeHash($stream)|ForEach-Object ToString x2)-join ''}finally{$stream.Dispose();$sha.Dispose()}
    $expected=$lock.dlls.PSObject.Properties[$name].Value
    $lines.Add("ZIP_ENTRY $entryName length=$($entry.Length) sha256=$hash lock=$expected match=$($hash -eq $expected.ToLowerInvariant())")
  }
  $headless='ws-game-1.14.0\packages\com.gamefoundation.adapter.headless\Lib~\Adapters.Stub.dll'
  $entry=$z.GetEntry($headless)
  if($null -eq $entry){$lines.Add("ZIP_ENTRY_MISSING $headless")}else{$stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create();try{$hash=($sha.ComputeHash($stream)|ForEach-Object ToString x2)-join ''}finally{$stream.Dispose();$sha.Dispose()};$expected=$lock.headless_dlls.PSObject.Properties['Adapters.Stub.dll'].Value;$lines.Add("ZIP_ENTRY $headless length=$($entry.Length) sha256=$hash lock=$expected match=$($hash -eq $expected.ToLowerInvariant())")}
  foreach($mp in @('ws-game-1.14.0\packages\com.gamefoundation.adapter.unity\package.json','ws-game-1.14.0\packages\com.gamefoundation.framework-data\package.json','ws-game-1.14.0\packages\com.gamefoundation.toolchain\package.json','ws-game-1.14.0\packages\com.gamefoundation.adapter.headless\package.json')){$me=$z.GetEntry($mp);if($null -eq $me){$lines.Add("MANIFEST_MISSING $mp")}else{$reader=[IO.StreamReader]::new($me.Open());try{$obj=($reader.ReadToEnd()|ConvertFrom-Json)}finally{$reader.Dispose()};$lines.Add("MANIFEST $mp version=$($obj.version) exact_1_14=$($obj.version -eq '1.14.0')")}}
} finally {$z.Dispose()}
$lines | Set-Content -LiteralPath $OutPath -Encoding UTF8
Get-Content -LiteralPath $OutPath
