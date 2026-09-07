# ws-game 1.3.0 validation

Snapshot date: 2026-09-08 (Asia/Tokyo)

## Baseline and isolation

- Source: `D:\workespace\ws-game` (read-only), `HEAD=5c444f1dc2f3729c8a03a68f78cacc28d24c2a2f`, commit `5c444f1 发布 1.3.0`.
- The initial source snapshot had empty `git status --short`. The validator's closing observation recorded 14 parallel uncommitted modifications across conformance, Unity engine/presentation, renderer, animation, and placeholder-generator files; those changes were outside this audit and were not included in the isolated archive or its results. The later main-review snapshot is recorded in [working-tree-drift.md](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/working-tree-drift.md).
- Archive commands: `git archive --format=tar --output=<audit>\source.tar 5c444f1...` (exit 0), then Windows `tar -xf` (exit 1: several Chinese pathnames reported `Invalid empty pathname`). A second archive was made with `git archive --format=zip --output=<audit>\source.zip 5c444f1...` (exit 0) and expanded to `<audit>\source_zip_extract` (1992 files). All validation commands ran from that complete isolated archive. The partial tar destination remains evidence of the Windows extractor limitation; no source files were touched.
- Audit root: `C:\Users\1\.codex\visualizations\2026\09\07\01a07bfd-c935-7323-9a34-6eae278d83b6\audit-5c444f1-20260908`.
- Independent archive comparison over the original `source.zip`'s 1992 tracked files found `Missing=[]`; the only tracked-file byte change in the isolated working copy was the audit-only `core/rules/tests/Tests.Rules.csproj` project reference. The added `AuditEconomyProbeTests.cs` and `AuditCoreMechanismProbeTests.cs` files are extra audit harness files absent from the original archive; they are not production or source-document changes. Build outputs are likewise confined to the isolated audit copy.

## Gate execution

Command: `powershell -NoProfile -ExecutionPolicy Bypass -File <audit>\source_zip_extract\check.ps1 -SkipUnity -ArtifactsPath <audit>\check_artifacts -LogFile <audit>\check-skipunity.log`

Exit code: **0**. Summary: **20 steps, 14 PASS, 6 SKIP** (Unity compile, EditMode, PlayMode, standalone build/continuous smoke, discrete smoke, consumer rehearsal). No Unity process was started.

- `dotnet build Core.sln -c Release`: exit 0, 0 warnings, 0 errors.
- `dotnet test ... --no-build`: exit 0; 106 + 654 + 297 + 390 + 470 + 452 = **2369 passed**, 0 failed, 0 skipped.
- Merged data validation: exit 0; 60 files, 59 tables, 282 records, 0 errors, 0 warnings, 1 override.
- Framework-only data validation: exit 0; 5 files/tables, 124 records, 0 errors, 1 known missing-l10n warning.
- Event constants: exit 0; 90 constants consistent.
- Placeholder assets: exit 0; 92/92 passed.
- Sample asset import check: exit 0; map 10, vfx 3, sfx 3, world 1, 0 issues.
- Toolchain pytest: exit 0; **47 passed**.
- Banned-word/architecture scans and version consistency: exit 0.
- `build.ps1 -SkipTests` and package dry-run consistency: exit 0. The isolated build emitted `fatal: not a git repository` while generating its own manifest commit field; this is expected for a git archive without `.git` and is not used as a release error claim.

Full transcript: `check-skipunity.log`.

## Existing 1.3.0 release evidence

- `dist/1.3.0/MANIFEST.txt`: version `1.3.0`, date `2026-09-08 03:27:03`, `git_commit: 5c444f1`.
- `dist/ws-game-1.3.0.lock`: version `1.3.0`, `git_commit: 5c444f1`; six DLL hashes equal the six DLLs inside the actual ZIP.
- Actual ZIP `dist/ws-game-1.3.0.zip`: 930 entries; `get_framework.ps1 -Version 1.3.0 -FromLocalDist <zip> -Target <audit>\valid_target -LockPath <audit>\valid_target.lock` exited **0**, verified all six hashes, and landed 907 files.
- Package-contained toolchain: extracted package `validate_data.py --data-root ...\data\_framework` exited **0** (5 tables, 124 records, 0 errors, 1 l10n warning); package `dotnet run --project ...\toolchain\validator -- --data-root ...\data\_framework` exited **0**. `validator/lib` contains 6 core DLLs.
- Manifest count note: `MANIFEST.txt` says `toolchain: 32 files`, while the current `dist/1.3.0/toolchain` directory has 38 files; the extra six are `validator/lib/*.dll`. The three package tarballs have entries: adapter 191, framework-data 185, toolchain 40 (including directory entries).

## Lock/hash mismatch guard

The six Release-hash values in the isolated LF build differed from the existing ZIP/lock values (all six). An external mixed artifact was made from the existing ZIP plus a lock containing the isolated hashes. Exact command: `powershell -NoProfile -ExecutionPolicy Bypass -File <audit>\source_zip_extract\toolchain\get_framework.ps1 -Version 1.3.0 -FromLocalDist <audit>\mixed_dist\ws-game-1.3.0.zip -Target <audit>\mixed_target -LockPath <audit>\mixed_target.lock`.

Result: exit **1**; all six DLLs reported sha256 mismatch; `mixed_target` and `mixed_target.lock` were not created. This demonstrates the fallback new-lock/old-ZIP combination is rejected before landing.

Machine-verifiable raw evidence for the normal ZIP hash inventory, successful consumer target, mixed-artifact rejection, and package validators is under `probes/release_hash_inventory_raw.log`, `probes/valid_target_inventory_raw.log`, `probes/get_framework_mixed_raw_final.log`, and `probes/package_validator_raw.log`. The normal ZIP and landed target inventories each report `VERSION=1.3.0`, `GIT_COMMIT=5c444f1`, and `DLL_MATCHES=6/6`; the mixed run reports `EXIT_CODE=1`, `TARGET_EXISTS=False`, and `LOCK_EXISTS=False`.

## Core mechanism probes

The isolated archive contains audit-only `AuditEconomyProbeTests`, `AuditCoreMechanismProbeTests`, and the minimal project reference needed to link the real `RewardDispatcher`. No files under the live source checkout were changed by this audit.

- Economy: real `InventoryHost` (`MaxSlots=1`, `Partial`) seeded A×5 and real `QuestHost` consume-on-progress A×1. A failed Buy A×6 left three queued events; after `DispatchPending`, inventory was A×4 and quest progress was 1. Raw output: `probes/economy.log`.
- Rewards: real `RewardDispatcher` passed `quest.audit_skill_reward` as source to `SkillHost.LearnSkill`; legal `skill.audit_reward` was known before save, absent from the permanent snapshot, remained known after same-host empty-snapshot load, and was absent after fresh-host load. Raw output: `probes/core_mechanism_final.log`.
- Charges: real `CooldownTracker`, factor `0.2`, max `2`, recharge `10`; after two uses and two units, one charge returned, then consuming it exposed a next recharge of `10` where the scaled value would be `2`; after two further units it remained at zero charges with `8` remaining. Raw output: `probes/core_mechanism_final.log`.
- Channel: real `CastPipeline` with `channel_time=0.5`, `tick_interval=1`, and `Update(1.0)` produced exactly one effect resolve and finished casting. Raw output: `probes/core_mechanism_final.log`.

Commands: `dotnet test <audit>\source_zip_extract\core\rules\tests\Tests.Rules.csproj -c Release --no-restore --filter FullyQualifiedName~AuditCoreMechanismProbeTests --logger console;verbosity=normal` (exit **0**, 3/3), and the corresponding isolated `AuditEconomyProbeTests` filter in `Tests.Gameplay.csproj` (exit **0**, 1/1), **4/4 total**. These are isolated non-Unity mechanism reproductions; they do not establish Unity Runtime rendering or importer behavior.

## Model asset/package coverage

Tracked source at this HEAD contains `adapters/unity/Assets/Editor/GeneratePlaceholderModelAssets.cs` (9217 bytes), `Resources/GameFoundation/anim_clips/{attack,cast,idle}.anim`, `models/placeholder_biped.controller`, and `models/placeholder_biped.prefab`; `Resources/GameFoundation` has 12 files total including `.meta` files.

The existing 1.3.0 ZIP has **0** exact matches for `GeneratePlaceholderModelAssets`, `Resources/GameFoundation`, `models/`, `anim_clips/`, `.prefab`, `.controller`, or `.anim`. The adapter and framework 1.3.0 `.tgz` files also have **0** such matches. Thus the code/package tests are present, but the newly tracked model placeholder assets and editor generator are not in the current release attachments.

## Unity scope

Installed Unity: `C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe` exists. At final snapshot `Unity_process_count=0`. Unity compile, EditMode, PlayMode, standalone build/smoke, consumer rehearsal, and Unity Runtime behavior were not executed. The RewardDispatcher/KnownSkillsPersistable local Save/Load path has mechanism evidence above; this report does not claim a complete game disk-save vertical slice.

## Presentation mechanism probes

The isolated console probe at `probes-presentation/` compiled the real `Presentation.Common` source project from `source_zip_extract` and linked the real Unity-free `AnimClipResolver.cs`. The harness used the real `Core.Foundation.EventBus`; only sink/expression/weapon-style/hit-frame adapters were fakes. No Unity process was started.

Command: `dotnet restore .\PresentationMechanismProbe.csproj` (exit **0**, 732 ms), then `dotnet run --project .\PresentationMechanismProbe.csproj -c Release --no-restore` (exit **0**, 1596 ms). The raw output is `probes-presentation/stdout.log`.

- `anim-state-and-resolver`: **PASS**; actual Attack flow `Attack,Attack,Attack,Attack`, same-state replay delta `0`; Hit priority remained `Hit` after move/attack events.
- `feedback-binder-hit-frame`: **PASS**; two non-mutually-exclusive same-damage `sync=hit_frame` rules observed `0,1,2` SFX across before one rig emission, after one emission, and 0.3 s timeout.
- `frame-player-and-weapon-override`: **PASS**; real `FrameAnimPlayer` reached frame `3` at `Update(0.35)` and emitted one frame-2 marker; real resolver selected `anim.sample_sword_swing` while only synthetic default attack was registered, and the real player reported `ArgumentException`.

These are mechanism reproductions in a non-Unity runtime. They do not establish Unity rendering, importer, renderer callback, or end-to-end gameplay behavior. `PASS` records that the expected reproduction assertion matched the actual result.

The validator's closing observation recorded 14 working-tree modifications (`git status --porcelain=v1`) across conformance, Unity engine/presentation, renderer, animation, and placeholder-generator files. Their provenance is not assigned by this bounded probe. This probe wrote only under the audit root and did not alter or clean those live-source files; live `HEAD` remained `5c444f1dc2f3729c8a03a68f78cacc28d24c2a2f`. The later main-review snapshot is recorded in [working-tree-drift.md](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/working-tree-drift.md).

## Artifacts

- `check-skipunity.log` — complete gate transcript.
- `source.zip`, `source.tar`, `source_zip_extract` — archive/isolation evidence.
- `valid_target/` and `valid_target.lock` — successful actual-ZIP offline consumer proof.
- `mixed_dist/` — external mixed ZIP/new-lock negative proof.
- `probes/economy.log`, `probes/core_mechanism_final.log` — raw isolated mechanism test output.
- `probes/repro.log` — aggregate raw probe transcript with section markers.
- `probes/release_hash_inventory_raw.log`, `probes/get_framework_mixed_raw_final.log`, `probes/package_validator_raw.log` — raw machine-verifiable artifact/hash/validator output.
- `probes/valid_target_inventory_raw.log` — raw successful consumer target/hash inventory.
- `probes-presentation/` — presentation source snapshots, probe csproj/Program, README, and raw stdout.
