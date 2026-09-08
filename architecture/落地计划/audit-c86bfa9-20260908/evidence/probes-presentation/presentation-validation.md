# Presentation validation — 1.4.0 / c86bfa9

All execution used the external Unity copy at `unity_workspace/adapters/unity`, whose source was copied from `audit-c86bfa9-20260908/source_snapshot`. The live checkout `D:\workespace\ws-game` was not used as a writable project and was not modified. Unity `6000.3.23f1` was installed and runnable; no player build was run.

## Unity execution

The isolated editor asset builder generated a real Animator controller/prefab fixture and a staging prefab:

```text
Unity.exe -batchmode -nographics -quit -projectPath unity_workspace/adapters/unity -executeMethod BuildPresentationProbeAssets.Build -logFile editor-build-assets-3.log
```

The builder log ends with `Batchmode quit successfully invoked` and return code 0. The targeted PlayMode command was:

```text
Unity.exe -batchmode -nographics -runTests -projectPath unity_workspace/adapters/unity -testPlatform PlayMode -testFilter PresentationTargetedMechanismTests -testResults presentation-unity-results-3.xml -logFile presentation-unity-run-3.log
```

Final Unity result: exit code **0**, NUnit XML `total=3 passed=3 failed=0 skipped=0`, targeted suite duration `0.158342` seconds. An intermediate run (`presentation-unity-run-2.log`) had exit code 2 because the updated staging fixture had not yet been generated; that failure is retained as execution evidence and was resolved by rebuilding only the isolated fixture. No production source was changed.

Observed mechanism outputs:

- Blob: expected camera-facing normal magnitude `>0.1`; actual `abs(dot)=1.1920929E-07`, `blob.forward=(0,-1,0)`, `camera.forward=(0,0,1)`. The reproduction assertion passed because the real Quad is side-on.
- Animator: expected completion signal count `1`; actual `finished=0`, while `current_idle=True` and `normalized=1.951345`. The real `Attack`→`Idle` exit-time transition therefore loses the renderer completion event.
- Conditional async swap: actual `parent_real=True`, `placeholder_all_off=True`, `old_child_destroyed=True`, `child_lookup_still_tracked=True`, `child_lookup_unity_alive=False`, `detach_exception=MissingReferenceException`, `new_shadow_on=True`. The real callback replaced the parent visual after `Resources` import; the attached child was destroyed with the old visual, its handle remained tracked, and the `ShadowMode.None` setting was not replayed.

The Unity fixture and test code are under `unity_workspace/adapters/unity/Assets/Editor/BuildPresentationProbeAssets.cs` and `unity_workspace/adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/PresentationTargetedMechanismTests.cs`. The assertions deliberately capture the observed defect mechanisms; a test `PASS` means the observed reproduction matched those assertions, not that the behavior is desired.

## Pure .NET AoE probe

The real `Presentation.Common` source project from `source_snapshot` was linked by `aoe/FeedbackHitFrameAoEProbe.csproj`. The real `Core.Foundation.EventBus`, `FeedbackBinder`, and `HitFrameSyncPolicy` were exercised with only fake sink, expression host, and hit-frame source adapters.

Commands: `dotnet restore .\FeedbackHitFrameAoEProbe.csproj` exit **0**, 752 ms; the final rerun `dotnet run --project .\FeedbackHitFrameAoEProbe.csproj -c Release --no-restore` exit **0**, 1116 ms. Raw output: `aoe/aoe-raw.log`.

Two damage events for targets A/B under one attacker produced `0` target-bound feedbacks before a hit frame, `1` after the first frame, then target C was queued and the second frame produced `2` total with release order `A,B`. A 0.5 s update produced `3` total with final order `A,B,C`. The target identity came from the real `FloatingTextAction` target-bound feedback path, so this is an order probe rather than an aggregate SFX count. Final result was `PASS` for the reproduction assertion that one attacker hit-frame signal releases only the first pending entry and that subsequent same-attacker entries are serialized by the same queue.

## Boundaries and raw evidence

These runs prove engine and pure .NET mechanisms in an isolated snapshot. They do not establish full game UX, network behavior, performance, or player-build behavior. Unity generated a temporary test prefab during the run and its `.meta` marker remains inside the isolated `unity_workspace` only. Unity was not left running.

Raw logs and result XML:

- `presentation-raw.log` — combined final Unity command/result excerpts and AoE output.
- `unity_workspace/adapters/unity/editor-build-assets-3.log`
- `unity_workspace/adapters/unity/presentation-unity-run-3.log`
- `unity_workspace/adapters/unity/presentation-unity-results-3.xml`
- `aoe/aoe-raw.log` and `aoe/restore.log`
