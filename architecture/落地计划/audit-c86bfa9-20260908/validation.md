# Validation evidence — ws-game 1.4.0

- Source repository: `D:\workespace\ws-game` (read-only for this audit).
- Baseline: `c86bfa98475dbf348f129c0fa507d117d238914c`; `VERSION=1.4.0`.
- Audit root: `C:\Users\1\.codex\visualizations\2026\09\07\01a07bfd-c935-7323-9a34-6eae278d83b6\audit-c86bfa9-20260908`.
- Initial live status was empty. Final live status was rechecked after all work: `HEAD` is unchanged and `git status --porcelain=v1` has 0 lines. This subtask did not start or stop processes or touch the registry.

## Isolated source and gate

`git archive --format=zip --output=<audit>\source_snapshot.zip c86bfa98475dbf348f129c0fa507d117d238914c` exited 0; the archive was expanded to `<audit>\source_snapshot` (2035 files). The first archive-only `check.ps1 -SkipUnity` run is preserved in [check-skipunity.log](check-skipunity.log): 13 PASS, 1 FAIL, 6 SKIP, exit 1. The sole failure was `python -m pytest toolchain/tests -q` (47 passed, 1 failed) because the archive has no `.git` and the git-aware markdown-link test ran `git ls-files`/`git check-ignore`; this is an archive-environment limitation, not a production failure.

An external no-hardlinks clone at `<audit>\git_snapshot_final` was detached at the same commit. The full command `powershell -NoProfile -ExecutionPolicy Bypass -File .\check.ps1 -SkipUnity -ArtifactsPath <audit>\check_git_artifacts_final2` was run there and captured in [check-git-aware-skipunity.log](check-git-aware-skipunity.log). Exit code was 0: 14 non-Unity steps PASS and the six Unity/consumer steps are explicitly SKIP. The six .NET test projects passed 655 + 106 + 297 + 401 + 478 + 457 = 2394 tests, with 0 failures/skips; build reported 0 warnings and 0 errors. The git-aware toolchain test was 48 passed.

All test/probe source additions under `source_snapshot` and `probes` are audit-only files. They are outside the live checkout and are not claims about the original archive contents.

## Release package and offline consumption

The live package was read only from `D:\workespace\ws-game\dist\1.4.0`:

- `MANIFEST.txt`: version 1.4.0, git commit `c86bfa9`, adapter 211 files, template 75, toolchain 33, placeholder assets 97, framework data 5, TMP essentials 81.
- `ws-game-1.4.0.zip`: 976 entries, SHA256 `324419CFD2F2B0E33FFAE7DB0AC53A4E8B5F0DB95610364D9374766ED6D4CA3A`.
- `ws-game-1.4.0.lock`: version 1.4.0 and git commit `c86bfa9`; all six DLL hashes match the ZIP (`DLL_MATCHES=6/6`). Exact expected/actual hashes are preserved in [release_inventory_raw.log](probes/release_inventory_raw.log).
- ZIP contains the placeholder model generator (4 matching entries), `Resources/GameFoundation` entries (26 after slash-normalized matching), 2 `.prefab`, 2 `.controller`, and 8 `.anim` entries. The adapter tgz has 211 entries and 14 model/generator matches; the toolchain tgz has 41 entries and 6 validator library DLLs. The framework-data tgz has 185 entries.

The command `get_framework.ps1 -Version 1.4.0 -FromLocalDist D:\workespace\ws-game\dist\ws-game-1.4.0.zip -Target <audit>\valid_target_evidence3 -LockPath <audit>\valid_target_evidence3.lock` exited 0. Its complete six-hash stdout is in [get_framework_raw.log](probes/get_framework_raw.log). The package Python validator and explicit `dotnet run --project toolchain\validator` both exited 0: 5 tables, 124 records, 0 errors, 1 warning (missing `l10n.text` lookup); the package validator build emitted three CS8632 warnings. This does not claim zero warnings for the package validator.

## Consumer compile comparison

The temporary external consumer project compiled against the actual 1.3.0 DLL set with exit 0. The same source against the actual 1.4.0 DLL set exited 1 with `CS0117` for `ViewKind.GameObject` and `CS1061` for `ICharacterRig.HitFrameReached`. Raw outputs are [consumer13_build_final.log](probes/consumer13_build_final.log) and [consumer14_build_final.log](probes/consumer14_build_final.log). This is a compile-compatibility counterexample to the 1.4 changelog claim for these two existing call forms.

## Core mechanism probes

The following probes execute isolated source copies and use real framework implementations. A probe exit 0 means the observation was reproduced and recorded; it is not a claim that the observed behavior is correct.

1. **Cross-map custom teleport resolver:** [crossmap_custom_resolver.log](probes/crossmap_custom_resolver.log), source `source_snapshot\core\gameplay\assembly\tests\GameplayAssemblyGobjTeleportInteractionTests.cs`. Real `GameplayAssembly`/`SceneRouter` flow was given a custom resolver returning B-map `(99,88)`. The scene changed to B, but the player landed at built-in default `(30,40)`; test exit 1 with the expected-vs-actual assertion.
2. **Permanent equipment aura across world clear:** [equipment_aura_mapclear.log](probes/equipment_aura_mapclear.log), [equipment_aura\Program.cs](probes/equipment_aura/Program.cs). Real `CarriersAssembly`, `EquipmentHost`, and `AuraHost` equipped an aura-bearing item, then executed `WorldSim.ClearAll`, dispatched `entity.destroyed`, and re-added the same player id. The equipment ledger remained (`afterEquipped=True`) while the aura was gone (`afterAura=False`); reproduced=True, process exit 0. This is a lifecycle mechanism probe, not a full Unity or router E2E.
3. **Full chest with real Reject inventory:** [inventory_reject_chest.log](probes/inventory_reject_chest.log), source [AuditInventoryRejectChestTests.cs](source_snapshot/core/carriers/gobj/tests/AuditInventoryRejectChestTests.cs). Real `GameObjectHost` was connected to real `InventoryHost(MaxSlots=1, FullPolicy=Reject)`. With a filler item occupying the only slot, opening a chest returned success, marked `open_state=true`, rolled once, and added zero reward. After removing the filler, a second interaction still added zero and did not reroll (`Loot.Calls=1`). The focused test passed (exit 0), recording the permanent lost/retry-blocked outcome.

## Release rebuild hash branch evidence

The read-only comparison in [rebuild-hash-comparison.log](probes/rebuild-hash-comparison.log) found live dist lock `Core.Foundation.dll=c6b549f2690b1b7af85e173c2fce2929ccf637449847d8eea47b987064e617d5`, while the fixed `git_snapshot_final` Release DLL at the same HEAD `c86bfa98475dbf348f129c0fa507d117d238914c` was `210d60ecd4c2fc9f1b8ebefae3db35ccd0241831d725a20e50ce91e99df79204` (`MATCH=False`). This supports PJ140-02: when a ZIP is missing while an old lock/TGZ remains, a rebuilt attachment cannot be assumed to be the same batch. It does not simulate remote upload and does not establish that the existing normal package is erroneous.

## Unity scope and final machine state

Unity was not launched by this non-Unity subtask. A separate presentation validation agent ran its bounded Unity checks; see [presentation-validation.md](probes-presentation/presentation-validation.md). Installed editor probe: `C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe` exists; `Unity`/`UnityHub` process count was 0 at final check. `-SkipUnity` intentionally left Unity compile, EditMode, PlayMode, standalone smoke, discrete smoke, and consumer steps skipped. This report therefore does not establish Unity runtime/importer or full game E2E behavior.

The combined raw evidence bundle is [repro.log](probes/repro.log).




