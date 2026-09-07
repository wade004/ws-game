# Presentation mechanism probe (1.3.0 / 5c444f1)

This is an isolated, console-only probe. It references the real `Presentation.Common` source project from `source_zip_extract` and links the real Unity-free `AnimClipResolver.cs`. Unity is not started and no Unity runtime or renderer is involved.

The probe covers:

1. `AnimStateMachine` and `AnimClipResolver`: an instant cast enters `Attack`, `SkillCastSuccess` and deferred locomotion do not revert it, same-state `Attack` does not replay, and `Hit` blocks `Move`/`Attack` by priority.
2. Real `FeedbackBinder` plus `HitFrameSyncPolicy`: two unconditional `sync=hit_frame` rules match the same damage event; one fake rig emission releases one rule, and the second is released by the 0.2 s timeout.
3. Real `FrameAnimPlayer` (10 frames at 10 fps, `Update(0.35)` reaches frame 3 and emits the frame-2 marker), then real `AnimClipResolver` selects `anim.sample_sword_swing` from a weapon style while only `anim.default.<displayId>.attack` is registered. The injected delegate calls the real player, which reports `ArgumentException` for the unregistered style clip.

The event bus is the real `Core.Foundation.EventBus`. The sink, expression host, weapon style source, and hit-frame source are harness doubles only. They do not reimplement production state, binding, policy, resolver, or player logic. `expected` values describe the targeted reproduction assertion; `actual` values are the observed result. A probe `PASS` means its reproduction assertion passed, not that the underlying behavior is a product requirement. Both are printed in `stdout.log`.

Run from this directory with:

```powershell
dotnet run --project .\PresentationMechanismProbe.csproj -c Release --no-restore
```

The source snapshots under `source/` are copied from the isolated source checkout for traceability. The production source checkout `D:\workespace\ws-game` remained read-only.
