[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputZip
)

$ErrorActionPreference = "Stop"
$auditRoot = (Split-Path -Parent $MyInvocation.MyCommand.Path)
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$stageName = "ws-game-audit-" + [guid]::NewGuid().ToString("N")
$stage = [IO.Path]::GetFullPath((Join-Path $tempRoot $stageName))
if (-not $stage.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    -not ([IO.Path]::GetFileName($stage)).StartsWith("ws-game-audit-", [StringComparison]::Ordinal)) {
    throw "Refusing unsafe temporary staging path: $stage"
}
New-Item -ItemType Directory -Path $stage | Out-Null

function Add-File([string]$relative) {
    $source = Join-Path $auditRoot $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required evidence file is missing: $relative" }
    $target = Join-Path $stage $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}

try {
    @("AUDIT_REPORT.md", "README.md", "evidence-index.md", "make-portable-archive.ps1", "run-probes.ps1", "core\core-findings.md", "core\scope-review.md", "presentation\presentation-findings.md", "presentation\scope-review.md", "docs-project\scope-and-evidence.md") | ForEach-Object { Add-File $_ }

    Get-ChildItem (Join-Path $auditRoot "docs-project") -File -Recurse |
        Where-Object {
            $_.FullName -notmatch "[\\/](check-artifacts|oldzip|repro-run[^\\/]*|bin|obj)[\\/]" -and
            $_.Extension -notin @(".dll", ".pdb", ".exe", ".zip", ".nupkg") -and
            $_.Name -notmatch "\.(deps|runtimeconfig)\.json$"
        } |
        ForEach-Object {
            Add-File ($_.FullName.Substring($auditRoot.Length + 1))
        }

    foreach ($prefix in @("core\logs", "core\repro", "core\evidence", "presentation\evidence", "presentation\logs")) {
        $dir = Join-Path $auditRoot $prefix
        if (Test-Path -LiteralPath $dir -PathType Container) {
            Get-ChildItem $dir -File -Recurse |
                Where-Object {
                    $_.FullName -notmatch "[\\/](bin|obj|Library|build)[\\/]" -and
                    $_.Extension -notin @(".dll", ".pdb", ".exe") -and
                    $_.Name -notmatch "\.(deps|runtimeconfig)\.json$"
                } |
                ForEach-Object { Add-File ($_.FullName.Substring($auditRoot.Length + 1)) }
        }
    }

    @(
        "presentation\DataHotReloadDeletedOverlayAuditTests.cs",
        "presentation\DataHotReloadProductionAuditTests.cs",
        "presentation\ExistingViewEquipmentSaveLoadAuditTests.cs",
        "presentation\prepare-view-equipment-fixture.ps1",
        "presentation\restore-view-equipment-fixture.ps1",
        "presentation\fixture-item.template.original",
        "presentation\fixture-item.template.original.sha256",
        "presentation\fixture-restore.log",
        "presentation\presentation-findings.md",
        "presentation\run-view-equipment-filtered.ps1",
        "presentation\data-hotreload-edit-evidence.log",
        "presentation\data-hotreload-play-evidence.log",
        "presentation\editmode-full-final.log",
        "presentation\editmode-full-final.xml",
        "presentation\playmode-full-final.log",
        "presentation\playmode-full-final.xml",
        "presentation\view-equipment-filtered-final.log",
        "presentation\view-equipment-filtered-final.xml",
        "presentation\six-dll-hashes.log",
        "presentation\tracked-adapter-cs-hashes.log",
        "presentation\tracked-conformance-cs-hashes.log",
        "presentation\tracked-cs-hashes-all.log",
        "presentation\tracked-template-cs-hashes.log",
        "presentation\view-equipment-filtered-runner.log",
        "presentation\view-equipment-filtered-runner.xml",
        "presentation\view-equipment-runner-verification.log"
    ) | ForEach-Object { Add-File $_ }

    $manifest = Join-Path $stage "portable-archive-manifest.txt"
    $manifestLines = Get-ChildItem $stage -File -Recurse |
        Where-Object { $_.FullName -ne $manifest } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($stage.Length + 1).Replace("\", "/")
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
            "$hash  $relative"
        }
    [IO.File]::WriteAllText($manifest, (($manifestLines -join [Environment]::NewLine) + [Environment]::NewLine), (New-Object Text.UTF8Encoding($false)))

    $zip = [IO.Path]::GetFullPath($OutputZip)
    New-Item -ItemType Directory -Force -Path (Split-Path $zip) | Out-Null
    if (Test-Path -LiteralPath $zip -PathType Leaf) { throw "Refusing to overwrite existing archive: $zip" }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
    Write-Output "portable_archive=$zip"
    Write-Output "files=$((Get-ChildItem $stage -File -Recurse).Count)"
}
finally {
    $stageCheck = [IO.Path]::GetFullPath($stage)
    $safePrefix = $tempRoot + [IO.Path]::DirectorySeparatorChar + "ws-game-audit-"
    if ((Test-Path -LiteralPath $stageCheck -PathType Container) -and
        $stageCheck.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase) -and
        $stageCheck -ne $tempRoot) {
        Remove-Item -LiteralPath $stageCheck -Recurse -Force
    } elseif (Test-Path -LiteralPath $stageCheck) {
        throw "Refusing unsafe staging cleanup path: $stageCheck"
    }
}
