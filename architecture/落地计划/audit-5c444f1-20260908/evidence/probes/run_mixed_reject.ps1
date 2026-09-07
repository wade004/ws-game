$ErrorActionPreference = 'Continue'
$log = 'C:\Users\1\.codex\visualizations\2026\09\07\01a07bfd-c935-7323-9a34-6eae278d83b6\audit-5c444f1-20260908\probes\get_framework_mixed_raw_final.log'
$target = 'C:\Users\1\.codex\visualizations\2026\09\07\01a07bfd-c935-7323-9a34-6eae278d83b6\audit-5c444f1-20260908\probes\mixed_reject_target_final'
$lock = 'C:\Users\1\.codex\visualizations\2026\09\07\01a07bfd-c935-7323-9a34-6eae278d83b6\audit-5c444f1-20260908\probes\mixed_reject_target_final.lock'
Remove-Item -LiteralPath $target -Force -Recurse -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $lock -Force -ErrorAction SilentlyContinue
try {
    & 'C:\Users\1\.codex\visualizations\2026\09\07\01a07bfd-c935-7323-9a34-6eae278d83b6\audit-5c444f1-20260908\source_zip_extract\toolchain\get_framework.ps1' -Version 1.3.0 -FromLocalDist 'C:\Users\1\.codex\visualizations\2026\09\07\01a07bfd-c935-7323-9a34-6eae278d83b6\audit-5c444f1-20260908\mixed_dist\ws-game-1.3.0.zip' -Target $target -LockPath $lock 2>&1 | Out-File -LiteralPath $log -Encoding utf8
    $exitCode = if ($LASTEXITCODE) { $LASTEXITCODE } else { 0 }
} catch {
    $_ | Out-File -LiteralPath $log -Encoding utf8
    $exitCode = 1
}
Add-Content -LiteralPath $log -Value "EXIT_CODE=$exitCode`nTARGET_EXISTS=$(Test-Path -LiteralPath $target)`nLOCK_EXISTS=$(Test-Path -LiteralPath $lock)"
