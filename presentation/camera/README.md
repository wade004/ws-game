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
    CameraFeelProfile.cs         镜头手感档案（十四个呈现型字段的不可变视图，判断记录 8）
    CameraHostOptions.cs
  core/
    CameraHost.cs                 ICameraHost 默认实现
    SimSnapshotFollowTarget.cs     ICameraFollowTarget 基于 ISimSnapshot 的实现
    DelegateFollowTarget.cs        ICameraFollowTarget 基于委托的实现
    CameraFeelFollower.cs          手感跟随计算（前瞻/死区/阻尼，判断记录 8）
  tests/
    CameraHostTests.cs            14 个用例
    CameraProfileFromRecordTests.cs  2 个用例（P4-2 新增）
    CameraFeelTests.cs            11 个用例（手感镜头，判断记录 8）
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

6. **PRES-118-CAMERA 根治（第十八轮审核）：`CameraHostOptions` 新增 `FollowTargetResolverOnReset`**：
   `ResetFollowOnSceneLoadFinished` 为真时，`scene.load_finished` 会先清空跟随目标；此前调用方只能
   在 `SceneRouter` PostLoad 钩子里"提前"重新 `Follow`，但钩子先于该事件派发，提前设置的目标必然被
   随后到达的重置覆盖掉（`presentation/assembly/README.md` 判断记录、`core/foundation/scene_router`
   判断记录"PostLoad 先于 load_finished"）。新增的 `FollowTargetResolverOnReset` 委托在
   `CameraHost.OnSceneLoadFinished` 内部、重置之后同一次处理内立即调用，从根本上避免"钩子与事件谁先
   谁后"这个时序竞争，不是新增一个需要调用方自己排时序的钩子。新增一个三参构造函数重载（不修改原
   两参构造函数签名），只新增不修改，见 `CameraHostOptions.cs` 判断记录"ABI 兼容"。
   `Presentation.Assembly.PresentationAssembly` 在 `AutoConfigureCameraFromFirstProfile` 打开、调用方
   未显式装配本委托时，默认补一个"继续跟随同一玩家单位"的解析函数，见该类型判断记录。
7. **ADR-0121 第 6 条（D6）：跟随目标以"可能不存在"为契约，丢失时保持最后位置**：
   被跟随实体销毁后，`SimSnapshotFollowTarget`（`ISimSnapshot.GetPosition` 对缺失实体抛）与包
   `ViewBinder.GetInterpolatedPosition` 的 `DelegateFollowTarget` 会让 `CameraHost.Update` 每帧抛异常。
   `ICameraFollowTarget` 新增**默认接口成员** `TryGetPosition(entityId, alpha, out position)`（默认实现原样包装
   `GetPosition`，旧实现不改也能编译；ABI 只新增），两个内置实现覆写为不抛（前者先查 `Exists`，后者把解析
   函数抛出的任何异常视为"目标当前不可取"）。`CameraHost.Update` 目标缺失时继续向最后一次有效（已按边界夹取的）
   位置 `Follow`，相机停在那里；每次丢失只记一条诊断（`_targetLostReported` 去重，多帧不刷），目标重现则继续
   跟随并复位标志，再次丢失重新记一条；`Follow(newId)`/`scene.load_finished` 重置会清空最后位置与去重标志。
   诊断出口经**新增构造重载** `CameraHost(camera, followTarget, bus, options, diagnostics)` 注入（不在既有四参构造上加
   可选参数），默认自建 `PresentationDiagnosticsRecorder`，经 `CameraHost.Diagnostics` 暴露。自定义
   `ICameraFollowTarget` 实现若不覆写 `TryGetPosition`，`GetPosition` 抛出的异常仍照旧向上传播（默认成员不吞异常）。
   `PresentationAssembly` 已把装配层共享诊断（即 `FeedbackSinkDiagnostics`）经该重载传给 `CameraHost`（ADR-0121 收口接线，见 `presentation/assembly/README.md`），装配路径下 `Camera.Diagnostics` 与之同一实例。
   用例：`presentation/camera/tests/CameraFollowTargetLossTests.cs`。

8. **手感镜头档案（2026-10-02，手感落地第 1 波 S4；设计见 `architecture/手感设计/07` 第 2 节）：跟随滞后/前瞻/死区/阻尼/战斗缩放/镜头冲击**：
   - **契约增量（ABI 只加法）**：`Core.Foundation.EngineAdapter.ICameraImpulse`（可选能力接口：`SupportsCameraImpulse` + `Impulse(direction, magnitude, decayMs)`，独立成新接口而不是给必需的 `ICamera` 加成员，探测写法 `camera is ICameraImpulse i && i.SupportsCameraImpulse`）；
     `CameraFeelProfile`（十四个呈现型 `camera_*` 字段的不可变视图，`FromPresenting(PresentingFeelView)`）；`CameraFeelFollower`（纯数学，前瞻 -> 死区 -> 分轴一阶滞后）；
     `CameraHost` 新增 `Update(alpha, dt)`、`EnableFeel`、`SetInCombat`、`InCombat`、`CombatZoomFactor`、`SupportsImpulse`、`Impulse`、`FeelFrameSeconds`、`ImpulseFallbackShakeFrequency`；既有 `Update(alpha)` 原样保留并转调。
   - **缺省档案逐位一致**：`IsFollowNeutral`（跟随滞后、前瞻、前瞻滞后、死区宽高、两轴阻尼全为 0）时 `CameraHost` 完全旁路档案计算，沿用 `CameraProfile.FollowLerp` 直接 `ICamera.Follow`；`IsZoomNeutral`（战斗缩放倍率 = 1）时不发任何战斗缩放调用。`rpg_classic` 预设的镜头组字段全部取中性值，因此注入手感解析器但不换档案时输出序列与不注入时逐位相同（`CameraFeelTests.DefaultProfile_CameraOutputSequence_IsBitIdenticalToWithoutFeel` 用 `BitConverter.DoubleToInt64Bits` 比较 120 帧）。
   - **单位**：`LookAhead`/死区是世界距离（身高倍数经标定换算成绝对值）；`ImpulseGain`/`ShakeCap` 是画面高度比例，取 `GetRaw`（不乘参考镜头高度）。
   - **跟随算法**：每帧 `a(τ) = 1 - exp(-dt/τ)`（τ = 0 时 a = 1）；速度 = 目标位移 / dt，前瞻期望偏移 = 速度单位方向 × `LookAhead` 按 `LookAheadLagMs` 滞后追随（方向反转时偏移穿过零，不甩动）；死区以当前关注点为中心的矩形，目标在内不动、在外把关注点拖到"目标 - 半宽"；两轴各自按 `DampingXMs`/`DampingYMs` 一阶滞后。`FollowLagMs > 0` 时换算成 `ICamera.Follow` 的平滑参数（秒），为 0 沿用 `FollowLerp`。首帧或换目标/换档案（`Reset`）直接对齐到目标，不产生滑入瞬态。关注点最终仍按 `Bounds` 夹取。
   - **战斗缩放**：战斗中取 `camera_combat_zoom_delta`，否则 1，按 `camera_combat_zoom_blend_ms` 线性过渡（进出对称，过渡时长 0 = 立即）；`SetZoom` 在战斗缩放期间把新基准叠上当前系数，夹到 profile 的 `[ZoomMin, ZoomMax]`。装配根订阅玩家单位的 `combat.entered`/`combat.left` 调用 `SetInCombat`。
   - **镜头冲击**：适配层实现 `ICameraImpulse` 且 `SupportsCameraImpulse` 为真时直接转发；否则退化为 `ICamera.Shake`（强度 = 画面高度比例 × 参考镜头高度，时长 = 衰减时长，频率 = `ImpulseFallbackShakeFrequency`）并记一条去重诊断。幅度非正时忽略。合并/限频/上限截断由反馈包流水线负责（`feedback_binder/README.md` 判断记录 23），本类不二次处理。
   - **已知局限（原文）**：
     - 对固定步长的宿主这就是准确值，对变帧率宿主是近似（`ICameraHost.Update` 既有签名只有插值系数、没有 dt，且为 ABI 只加法不改它；旧调用点按固定 1/60 秒推进，变帧率宿主应改用 `Update(alpha, dt)`）。
     - （已解决，见判断记录 9）Unity 适配层 `UnityCamera` 已实现 `ICameraImpulse`。
   用例：`presentation/camera/tests/CameraFeelTests.cs`（11 条：缺省逐位一致、非缺省输出确有不同、阻尼 = 一阶滞后公式、跟随滞后换算、死区、前瞻、前瞻滞后过零、战斗缩放线性过渡、战斗中 `SetZoom`、冲击能力转发/退化、非正幅度忽略）。

9. **引擎侧镜头冲击：`UnityCamera` 实现 `ICameraImpulse`（2026-10-02，手感落地 M2-A，手感设计/07 第 2/5 节）**：
   - `UnityCamera` 恒声明 `SupportsCameraImpulse`，`CameraHost.Impulse` 因此直接转发，不再退化为 `Shake`；`PlayImpact` 触发的镜头冲量（`feedback_binder` 判断记录 23 的 `ImpactCameraCue`）在引擎里是真正的方向性推移。
   - **单位与几何**：`magnitude` 是画面高度比例，位移峰值 = `magnitude × 2 × orthographicSize`（按触发那一刻的缩放换算）；沿 `direction` 推开，位移从峰值线性衰减回零，历时 `decayMs`；零方向取各向同性（Perlin 噪声采样的二维偏移，幅度同样线性衰减）。多次冲击位移向量相加（合并、限频、上限截断由反馈包流水线负责）；冲击位移单独维护，最终写回的是 `基准 + 震屏 + 冲击`，不污染跟随基准。幅度非正、衰减非正的调用忽略。
   - 取舍：不复用 `Shake` 的随机抖动实现——`Shake` 没有方向、强度单位也不同（世界距离），硬套会让"沿命中方向推一下"变成随机晃；冲击是独立的第二路偏移。
   - 复现/不变量：`UnityCameraTests.Impulse_Directional_PeakOffsetIsMagnitudeTimesScreenHeight_ThenDecaysToZero`（峰值 = 幅度 × 画面高度、线性衰减单调不增、过期回零）、`..._ZeroDirection_UsesIsotropicOffsetWithinPeak_AndInvalidArgumentsAreIgnored`、`..._DoesNotDisturbFollowBase_AfterItEnds`；端到端见 `HitFrameSyncEndToEndTests.FeelEngine_Hit_TriggersEngineCameraImpulse_MagnitudeFromProfile`（命中 → 引擎相机的冲量幅度 = `min(基础增益 × 变体增益, 震屏上限)`）。

## 契约缺口

- （已由 ADR-0016 解决）`ShakePreset.Frequency` 此前无对应的 `ICamera` 参数可传递（见判断记录 3
  历史记录）；`ICamera.Shake` 现增加 `frequency` 参数，`CameraHost.Shake` 已把
  `ShakePresets[i].Frequency` 传给它，强度/时长/频率三项完整经这一个方法传递。
- （P4-2 已修补，不再是契约缺口）`camera_profile` 表原先未提供 `DataRegistry`/`FromRecord` 数据行
  解析器，现由 `CameraSchemas.Profile` + `CameraProfile.FromRecord` 提供，见 `schema/README.md`
  "数据行解析"一节。
