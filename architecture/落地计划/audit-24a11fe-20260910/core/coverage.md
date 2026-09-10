# Core bounded coverage — 1.16.1

Runner: [repro/run-core-bounded.ps1](repro/run-core-bounded.ps1)

Raw evidence: [logs/runner-current.log](logs/runner-current.log), [logs/runner-current.exit.txt](logs/runner-current.exit.txt). The runner selected current in-repo regression tests by fully-qualified name and completed 92/92 passing tests. An additional independent probe covers three resident reload cases plus malformed Id validation in [logs/CoreBoundaryProbe.current.log](logs/CoreBoundaryProbe.current.log).

| Area | Selected scope | Resident/fresh or formal oracle | Result |
|---|---|---|---|
| CORE114-01 Quest | 14 tests: increase, reduce, reorder, count changes, collect/non-collect additions, removal, failed state | resident `Reload` then new index/read; state and event assertions | 14/14 pass; no array exception |
| CORE114-02 Economy | 5 tests: none/timer, timer/none, timer period, limit shrink/raise | resident stock state, reload event, `Update`; conservation/clamp assertions | 5/5 pass |
| CORE114-03 Stat | 4 tests: query order, explicit base, changed/unchanged event | resident cached A vs fresh query B after reload | 4/4 pass |
| CORE114-04 validation | 7 Quest/Dialog tests: malformed Id + Bool/Int/Number/String/Id | formal `ContentValidationAssembly.Run`; report/block and legal-shape oracle | 7/7 pass |
| Equipment set candidate | 2 tests: threshold deletion and threshold raise | resident equip → reload → unequip; aura/stat oracle | 2/2 pass; reload-time no-op remains documented |
| 73cb55e immunity | 13 tests: interrupt, dispel, energize, teleport, movement, static/dynamic, cost/CD | effect result and live state/event oracle | 13/13 pass |
| FindUnits/Gobj boundary | `EntitySpatialSyncHostTests` + `ISkillHost_FindUnitsTests` | default kind list excludes Gobj; explicit Gobj uses tag; trigger-only excluded | selected tests pass; no default-chain defect |
| Quest/save/teleport | QuestPersistable and TP-111 selected tests | same-host snapshot replacement/idempotence, cross-unit isolation, loading state | selected tests pass |
| Progression/resource | ProgressionPersistable and P2-05 PowerHost reload selected tests | level/xp/growth and current/max resource behavior | selected tests pass |
| Item/unit state | ItemPersistable and UnitPersistable selected tests | complete snapshot replacement, equipment linkage, map/position/archetype/race | selected tests pass |

## Evidence counts

| Test project | Filtered tests | Passed | Exit |
|---|---:|---:|---:|
| `Tests.Gameplay` | 32 | 32 | 0 |
| `Tests.Numbers` | 14 | 14 | 0 |
| `Tests.Carriers` | 46 | 46 | 0 |
| **Total** | **92** | **92** | **0** |

## Not covered in this bounded run

Unity Editor/PlayMode, IL2CPP, published package consumption, real file watcher callbacks, full `check.ps1`, and the complete solution gate were not run. These are separate evidence tiers. Existing old-audit raw logs were not rewritten or used as current proof.
