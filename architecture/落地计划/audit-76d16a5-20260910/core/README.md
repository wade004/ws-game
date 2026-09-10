# Core probe run notes

Freeze: `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen` at `76d16a54e54f0f11204d97d7460563c8a0dc8cd8`.

The portable runner is `run-probes.ps1`. It accepts `-RepoRoot`, `-BuildRoot`, and `-EvidenceRoot`; it verifies the freeze HEAD, rebuilds an isolated framework mirror excluding `bin/obj`, builds each probe with `FrameworkRoot`, and executes the exact output DLL. It throws on copy, restore, build, or run failure. The present run used the mirror at `build/core/source` and exact DLL paths below:

- `build/core/out/GobjLockBoundaryProbe/GobjLockBoundaryProbe.dll`
- `build/core/out/QuestWorldFlagValueBoundaryProbe/QuestWorldFlagValueBoundaryProbe.dll`
- `build/core/out/SkillHotReloadBoundaryProbe/SkillHotReloadBoundaryProbe.dll`
- `build/core/out/FollowupCoreProbe/FollowupCoreProbe.dll`
- `build/core/out/TeleportLoadingBoundaryProbe/TeleportLoadingBoundaryProbe.dll`
- `build/core/out/CoreBoundaryProbe/CoreBoundaryProbe.dll`

Probe project intermediate files are directed to `build/core/obj/<probe>` by `BaseIntermediateOutputPath`; exact DLL output is under `build/core/out/<probe>`. Any historical `core/repro/obj` or `core/repro/bin` is scratch and must be excluded from the evidence archive. Interpret output with `core-findings.md`: exit 0 records diagnostic completion and is not a semantic PASS.
