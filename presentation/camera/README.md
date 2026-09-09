# L5 表现层 · camera（镜头）

职责：落地 [01_分层与依赖.md](../../architecture/01_分层与依赖.md) L5 模块表 `camera` 行（契约接口名
`CameraHost`）、[09_表现层.md](../../architecture/09_表现层.md) 第 3.5、8 节：固定俯角镜头配置、
跟随、缩放、震屏、过场切档，全部操作经 `ICamera`（L-1）完成。

依赖：`Presentation.Common.csproj`（传递引用 `Core.Gameplay`，本模块用到其中的
`Core.Foundation.SceneRouter.SceneLoadFinishedEvent`、`Core.Gameplay.Encounter.EncounterPhaseChangedEvent`
两个事件类型）。

## 目录

```
camera/
  README.md
  schema/
    README.md                  camera_profile 表字段表
    CameraSchemas.cs            camera_profile 的 TableSchema 登记（P4-2 新增）
  contracts/
    ICameraHost.cs
    CameraProfile.cs             CameraProfile / CameraBounds / ShakePreset / FromRecord（P4-2 新增数据行解析器）
    ICameraFollowTarget.cs       跟随目标位置来源（解耦 view_binding）
    CameraHostOptions.cs
  core/
    CameraHost.cs                 ICameraHost 默认实现
    SimSnapshotFollowTarget.cs     ICameraFollowTarget 基于 ISimSnapshot 的实现
    DelegateFollowTarget.cs        ICameraFollowTarget 基于委托的实现
  tests/
    CameraHostTests.cs            14 个用例
    CameraProfileFromRecordTests.cs  2 个用例（P4-2 新增）
```

## 谁实现 / 谁调用

| 类型 | 谁实现 | 谁调用 |
|---|---|---|
| `ICameraHost`（`CameraHost`） | 本模块 | 主循环组装代码（每帧调用 `Update(alpha)`）；玩法层经事件触发切档/震屏（09 第 8 节"镜头切换……由玩法层通过事件/钩子触发"） |
| `ICameraFollowTarget` | 本模块提供两个实现 | `CameraHost` |

## 判断记录

1. **不直接依赖 `presentation/view_binding`**：`CameraHost` 只依赖 `ICameraFollowTarget`，具体是
   包一层 `ISimSnapshot`（`SimSnapshotFollowTarget`，不插值）还是包一层
   `ViewBinder.GetInterpolatedPosition`（`DelegateFollowTarget`）由组装代码决定，两个 L5 同层模块
   之间不产生编译期依赖（呼应 01 第 3 节"同一层内的模块之间只经契约接口与事件总线交互"）。
2. **`SimSnapshotFollowTarget` 不插值**：`ICamera.Follow(planePos, smoothing)` 本身带平滑系数，
   逐帧用"当前"位置驱动、由引擎侧 lerp 平滑即可，不强求跟随位置与角色精灵像素级同步。
3. **`ShakePreset.Frequency` 已传给 `ICamera.Shake`（ADR-0016 解决，勘误更正判断记录）**：
   `ICamera.Shake(intensity, durationSeconds, frequency)`（见 02 第 1.13 节）现有频率参数，
   `CameraHost.Shake` 调用时把 `ShakePresets[i].Frequency` 一并传入，强度/时长/频率三项完整落地，
   不再是"登记但不使用"的字段，见 `schema/README.md`、下"契约缺口"一节的同款结论。
4. **`encounter.phase_changed` 的切档映射用 `newPhase`（int 阶段下标）而非 `Id`**：
   `EncounterPhaseChangedEvent.NewPhase` 类型是 `int`（见 `core/gameplay/encounter` 判断记录：06
   未给阶段命名 id），`CameraHostOptions.PhaseProfileSwitch` 随之用 `IReadOnlyDictionary<int, Id>`。

5. **ADR-0019 F1c 子结构登记**：`bounds`（`{min:Vec2, max:Vec2}`，均必填）、`shake_presets`
   （`[{id:Id, amplitude:Number, duration:Number, frequency:Number?}]`，`frequency` 缺省 0）按
   `CameraProfile.FromRecord` 权威解析登记为 `Fields`/`Item`。

## 契约缺口

- （已由 ADR-0016 解决）`ShakePreset.Frequency` 此前无对应的 `ICamera` 参数可传递（见判断记录 3
  历史记录）；`ICamera.Shake` 现增加 `frequency` 参数，`CameraHost.Shake` 已把
  `ShakePresets[i].Frequency` 传给它，强度/时长/频率三项完整经这一个方法传递。
- （P4-2 已修补，不再是契约缺口）`camera_profile` 表原先未提供 `DataRegistry`/`FromRecord` 数据行
  解析器，现由 `CameraSchemas.Profile` + `CameraProfile.FromRecord` 提供，见 `schema/README.md`
  "数据行解析"一节。
