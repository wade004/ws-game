# Coverage matrix — current 1.14.0

| Area | Current entrypoint/probe | Oracle result | Status |
|---|---|---|---|
| Gobj lock expected | `GobjLockBoundaryProbe.current-v2.log` | missing expected: formal 1 error and blocked; expected=true: 0 errors + parser success | fixed/current pass |
| Quest reward ExprValue array | `QuestWorldFlagValueBoundaryProbe.current-v3.log` | array: formal shape error + blocked; true: 0 errors and parsed count=1 | fixed/current pass |
| Quest reward invalid Id | same | `{$id:BAD}`: formal Run throws ArgumentException | CORE114-04 |
| Skill cache reload | `SkillHotReloadBoundaryProbe.current-v2.log` | resident second cast 99/CD5 equals fresh host | fixed/current pass |
| QuestHost objective cardinality | `CoreBoundaryProbe.current-v5.log` | resident progress update throws IndexOutOfRangeException | CORE114-01 |
| Stat query cache | same | queried A remains 0 while B=77; GetBase A/B=77 | CORE114-03 |
| Economy none→timer | same | resident stock 0→0 and timer null; fresh control 0→2 | CORE114-02 |
| Save rollback / vitals / class / race / UI | `FollowupCoreProbe.current-v3.log` | all listed current oracles observed | current pass |
| Loading teleport / WorldSim re-add | `TeleportLoadingBoundaryProbe.current-v2.log` | consecutive choices complete with router/player/world consistent on B | current pass |
| Equipment set threshold migration | static `EquipmentHost.cs:857-896` | no independent valid resident repro in this run | candidate only |
| Power fixed max / talent / summon discrete / custom FindUnits | source/document review | explicit design or consumer boundary; no failure oracle | candidate only |

Runner `core/run-probes.ps1` was executed with the frozen RepoRoot and isolated BuildRoot; it completed all six exact DLLs with exit 0. Only rows marked P2 have a reproduced mismatch against a stated general contract. Exit code 0 is diagnostic completion, not a semantic pass.


