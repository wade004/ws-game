[CmdletBinding()]
param(
  [string]$AuditRoot = "",
  [string]$OutputZip = ""
)
if ([string]::IsNullOrWhiteSpace($AuditRoot)) { $AuditRoot = $PSScriptRoot }
if ([string]::IsNullOrWhiteSpace($OutputZip)) { $OutputZip = Join-Path $PSScriptRoot 'framework-scope-review.zip' }
$ErrorActionPreference = 'Stop'
$AuditRoot = [IO.Path]::GetFullPath($AuditRoot)
$OutputZip = [IO.Path]::GetFullPath($OutputZip)
if (-not (Test-Path -LiteralPath $AuditRoot -PathType Container)) { throw "AuditRoot not found: $AuditRoot" }
if (Test-Path -LiteralPath $OutputZip) { throw "Refusing to overwrite existing archive: $OutputZip" }
$forbiddenSegments = @('bin','obj','build','repro-run','repro','temporary-AuditTemplate')
$forbiddenExtensions = @('.dll','.pdb','.exe','.deps.json','.runtimeconfig.json','.zip')
function Assert-SafeSource([string]$Path) {
  $full = [IO.Path]::GetFullPath($Path)
  if (-not $full.StartsWith($AuditRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Source outside AuditRoot: $full" }
  foreach ($segment in ($full.Substring($AuditRoot.Length).TrimStart('\') -split '\\')) {
    if ($forbiddenSegments -contains $segment.ToLowerInvariant()) { throw "Forbidden source segment: $full" }
  }
  $ext = [IO.Path]::GetExtension($full).ToLowerInvariant()
  if ($forbiddenExtensions -contains $ext -or $full -match '(?i)\.(deps|runtimeconfig)\.json$') { throw "Forbidden artifact extension: $full" }
}
$guid = [Guid]::NewGuid().ToString('N')
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$stage = [IO.Path]::GetFullPath((Join-Path $tempRoot "ws-game-audit-$guid"))
$stagePrefix = $tempRoot + '\ws-game-audit-'
try {
  New-Item -ItemType Directory -Path $stage -Force | Out-Null
  $manifest = New-Object System.Collections.Generic.List[string]
  function Add-File([string]$RelativeSource, [string]$ArchivePath = $RelativeSource) {
    $source = Join-Path $AuditRoot $RelativeSource
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required file missing: $source" }
    Assert-SafeSource $source
    $dest = Join-Path $stage $ArchivePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $dest -Force
    $manifest.Add("$ArchivePath`t$((Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash.ToLowerInvariant())")
  }
  $required = @(
    'README.md','AUDIT_REPORT.md','evidence-index.md','make-portable-archive.ps1',
    'docs-project/doc-code-matrix.md','docs-project/project-findings.md','docs-project/validation.md','docs-project/scope-and-evidence.md',
    'docs-project/verify-formal-release.ps1',
    'docs-project/api-compat/Program.cs','docs-project/api-compat/SkillHostAbiConsumer.csproj','docs-project/api-compat/run-skillhost-abi.ps1',
    'docs-project/logs/api-compat/api-compat.log','docs-project/logs/api-compat/build-old.log','docs-project/logs/api-compat/run-old-formal.log','docs-project/logs/api-compat/run-new-formal.log',
    'logs/release-formal-1.14.0.log','baseline.txt',
    'core/core-findings.md','core/scope-review.md','presentation/presentation-findings.md','presentation/scope-review.md'
  )
  foreach ($item in $required) { Add-File $item }
  foreach ($subdir in @('core','presentation','hashes')) {
    $dir = Join-Path $AuditRoot $subdir
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { throw "Required report directory missing: $dir" }
    foreach ($file in (Get-ChildItem -LiteralPath $dir -Recurse -File | Sort-Object FullName)) {
      $rel = $file.FullName.Substring($AuditRoot.Length).TrimStart('\')
      $segments = $rel -split '\\'
      if ($segments | Where-Object { $forbiddenSegments -contains $_.ToLowerInvariant() }) { continue }
      Add-File $rel $rel
    }
  }
  $manifest | Sort-Object | Set-Content -LiteralPath (Join-Path $stage 'MANIFEST.sha256.tsv') -Encoding UTF8
  Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $OutputZip -CompressionLevel Optimal
  Write-Output "archive=$OutputZip"
  Write-Output "files=$($manifest.Count)"
  Write-Output "sha256=$((Get-FileHash -LiteralPath $OutputZip -Algorithm SHA256).Hash.ToLowerInvariant())"
} finally {
  if (Test-Path -LiteralPath $stage) {
    $resolved = [IO.Path]::GetFullPath($stage)
    if ($resolved -eq $tempRoot -or -not $resolved.StartsWith($stagePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing unsafe stage cleanup: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
