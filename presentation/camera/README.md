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
  contracts/
    ICameraHost.cs
    CameraProfile.cs             CameraProfile / CameraBounds / ShakePreset
    ICameraFollowTarget.cs       跟随目标位置来源（解耦 view_binding）
    CameraHostOptions.cs
  core/
    CameraHost.cs                 ICameraHost 默认实现
    SimSnapshotFollowTarget.cs     ICameraFollowTarget 基于 ISimSnapshot 的实现
    DelegateFollowTarget.cs        ICameraFollowTarget 基于委托的实现
  tests/
    CameraHostTests.cs            14 个用例
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
3. **`ShakePreset.Frequency` 暂不传给 `ICamera.Shake`**：`ICamera.Shake(intensity, durationSeconds)`
   没有频率参数（见 02 第 1.13 节），字段随 `camera_profile` 表结构一并登记但当前不使用，见
   `schema/README.md`。
4. **`encounter.phase_changed` 的切档映射用 `newPhase`（int 阶段下标）而非 `Id`**：
   `EncounterPhaseChangedEvent.NewPhase` 类型是 `int`（见 `core/gameplay/encounter` 判断记录：06
   未给阶段命名 id），`CameraHostOptions.PhaseProfileSwitch` 随之用 `IReadOnlyDictionary<int, Id>`。

## 契约缺口

- `camera_profile` 表未提供 `DataRegistry`/`FromRecord` 数据行解析器，见 `schema/README.md`。
- `ShakePreset.Frequency` 无对应的 `ICamera` 参数可传递，见判断记录 3；若震屏需要频率语义，需评估
  是否给 `ICamera.Shake` 增补参数（走 02 文档变更流程）。
