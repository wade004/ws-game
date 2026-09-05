# L5 表现层 · assembly（组装根）

`PresentationSchemaCatalog`/`PresentationAssembly` 是 `presentation` 七模块（`common`/
`view_binding`/`render`/`camera`/`vfx_sfx`/`feedback_binder`/`ui`/`shell`）在
`core/gameplay/assembly.GameplayAssembly`（L0～L4）之上的最终组装根（阶段 4 收敛 B，
`GameplayAssembly` 在 L5 表现层的延续，惯例同 `core/gameplay/assembly/README.md`）。调用方
（游戏侧引擎适配层组装代码、集成测试、`toolchain/validator`）只需要：

1. `PresentationSchemaCatalog.CreateOptions()` 构造 `DataRegistryOptions`（`ExprSchema` 复用
   `GameplaySchemaCatalog.FullExprSchema`，见该类型判断记录）→ 构造 `DataRegistry`。
2. `PresentationSchemaCatalog.RegisterAll(registry)` 一次性注册 L0～L5 全部表/校验规则。
3. `registry.LoadAll()`。
4. 按既有惯例构造 `GameplayAssembly`（见 `core/gameplay/assembly/README.md`）。
5. `new PresentationAssembly(gameplay, world, registry, bus, rng, viewFactory, renderer2D, camera,
   audio, fileSystem, sceneRouter, options)` 拿到全部表现层宿主 + 十个 UI 视图模型 + Shell。
6. 不再需要表现层时调用 `PresentationAssembly.Dispose()`（见"退订"一节）。

## 目录

```
assembly/
  README.md
  PresentationSchemaCatalog.cs   L0～L5 全部 TableSchema/IValidationRule 的统一注册清单
  PresentationAssembly.cs        L5 组装根 + PresentationAssemblyOptions（可选知会点集合）
  tests/
    PresentationAssemblyTests.cs 烟雾测试（见"验收测试"）
```

## `PresentationSchemaCatalog` 登记的表清单

| 表 | Schema 类 | 示例数据 | `IValidationRule` |
|---|---|---|---|
| （L0～L4 全部表） | `Core.Gameplay.Assembly.GameplaySchemaCatalog.RegisterAll` | 见 `core/gameplay/assembly/README.md` | 同上 |
| `l10n.locale`/`l10n.text` | `Core.Foundation.Localization.L10nSchemas` | `data/_sample/l10n/` | 无（`L10nHost` 构造期自行校验"恰好一条 `is_default`"） |
| `display.map`/`display.anim_set`/`display.equip_visual` | `Core.Foundation.DisplayInfo.DisplaySchemas` | `data/_sample/display/display.map.json` | `DisplayKindFieldGroupRule`（`DisplayMapCoverageRule` 未注册，见判断记录 1） |
| `vfx.def`/`sfx.def`/`display.weapon_style` | `Presentation.VfxSfx.Schema.VfxSfxSchemas` | `data/_sample/vfx/`、`sfx/`、`display/display.weapon_style.json` | 无 |
| `feedback.binding`/`feedback.floating_text_style` | `Presentation.FeedbackBinder.Schema.FeedbackSchemas` | `data/_sample/feedback/` | 无（`FeedbackRuleValidator` 签名与 `IValidationRule` 不同，未注册，见判断记录 2） |
| `camera_profile` | `Presentation.Camera.Schema.CameraSchemas` | `data/_sample/camera/camera_profile.json` | 无 |
| `ui_layout_definition` | `Presentation.Ui.UiSchemas` | `data/_sample/ui/ui_layout_definition.json` | 无 |
| `shell_menu_definition` | `Presentation.Shell.ShellSchemas` | `data/_sample/shell/shell_menu_definition.json` | 无 |

`CreateOptions()` 的 `ExprSchema` 直接复用 `GameplaySchemaCatalog.FullExprSchema`（不新造一份表现层
专属组合），因为该组合里 `quest`/`world`/`player` 之外的 `event` 分组已经是"已登记 key 精确匹配、
未登记 key 一律放行"策略（见 `QuestExprSchemaEntries.BuildParsingSchema` 判断记录），足以覆盖
`feedback.binding.condition` 里 `event.is_crit` 这类字段路径的解析需求。

## `PresentationAssembly` 装配顺序

| # | 步骤 | 关键依赖来源 |
|---|---|---|
| 1 | `view_binding`+`render`+`camera`：`WorldSimSnapshot`（只读）→ `DisplayInfoRegistry` → `ViewBinder` → `RenderConventionHost`（无状态）→ `CameraHost`；若数据集里有 `camera_profile` 行且 `AutoConfigureCameraFromFirstProfile`（默认 true），立即用第一条记录 `Configure` + `Follow` 玩家单位 | `IWorldSim`、`IDataRegistryView`、注入的 `IViewFactory`/`ICamera` |
| 2 | `vfx_sfx`：从 `vfx.def`/`sfx.def`/`display.weapon_style` 建目录 → `VfxPlayer`/`SfxPlayer`/`WeaponStyleResolver`/`DisplayInfoResolver`；`EntityPositionResolver` 接第 1 步的只读快照，`AnchorResolver` 留空（见"契约缺口"） | 注入的 `IRenderer2D`/`ICamera`/`IAudio`/`IRngHost` |
| 3 | `feedback_binder`：解析 `feedback.binding`/`feedback.floating_text_style` → `CompositeFeedbackSink`（`play_vfx`/`play_sfx` 接第 2 步播放器，`shake_camera` 接第 1 步 `CameraHost.Shake`，飘字/顿帧/闪白转给 `PresentationAssemblyOptions` 注入回调，默认空实现）→ `FeedbackBinder` | `GameplayAssembly.ExprHostFactory`、`GameplayAssembly.Carriers.Units`（`IUnitAccess`） |
| 4 | `ui`：本装配根自行构造 `InputMapHost`/`L10nHost`（两者是 L0 基础设施，不属于 `GameplayAssembly` 十个 L4 宿主，见判断记录 3）→ 三个 `IUiPathProvider`（player/target/unit）→ `UiDataSource` → `UiIntents` → 十个视图模型 | `GameplayAssembly.Carriers.Rules.*`/`Carriers.Inventory`/`Equipment`、`GameplayAssembly.Quest`/`Economy`/`Dialog`/`AppState` |
| 5 | `shell`：`ShellMenuDefinition` 取 `shell_menu_definition` 表第一条（无数据时退化为空菜单）→ 本装配根自行构造 `ISaveSystem`/`ISettingsStore`（同判断记录 3，见下）→ `ShellHost` → `ShellViewModel` | 注入的 `ISceneRouter`/`IFileSystem`、`GameplayAssembly.Difficulty`（`IDifficultyHost`） |

## 注入点清单（调用方 / 引擎侧必须提供什么）

`PresentationAssembly` 构造函数的必填参数（均为"引擎侧注入的 L-1 接口或已构造好的上游装配根"，
不由本类型代为构造）：

| 参数 | 类型 | 来源 |
|---|---|---|
| `gameplay` | `GameplayAssembly` | 调用方按 `core/gameplay/assembly/README.md` 先构造好 |
| `world` | `IWorldSim` | 同 `gameplay` 构造时使用的同一个 `WorldSim` 实例 |
| `registry` | `IDataRegistryView` | 同 `gameplay` 构造时使用的同一个 `DataRegistry`（已 `LoadAll` 且非阻断） |
| `bus` | `IEventBus` | 同 `gameplay` 构造时使用的同一个事件总线 |
| `rng` | `IRngHost` | 表现层自己的随机源（`SfxPlayer` 变体挑选用），可以与 `gameplay` 的 `rng` 是不同实例 |
| `viewFactory` | `Presentation.Common.IViewFactory` | 引擎侧（Unity 等）实现，创建具体 `IView` |
| `renderer2D` | `Core.Foundation.EngineAdapter.IRenderer2D` | 引擎适配层 |
| `camera` | `Core.Foundation.EngineAdapter.ICamera` | 引擎适配层 |
| `audio` | `Core.Foundation.EngineAdapter.IAudio` | 引擎适配层 |
| `fileSystem` | `Core.Foundation.EngineAdapter.IFileSystem` | 引擎适配层；本装配根用它自行构造 `ISaveSystem`/`ISettingsStore`（见判断记录 3） |
| `sceneRouter` | `Core.Foundation.SceneRouter.ISceneRouter` | 建议与传给 `GameplayAssembly`（`AreaTriggerOptions.SceneRouter`）的是同一个实例，保证场景切换与区域触发一致 |
| `options` | `PresentationAssemblyOptions?` | 可选，见下"可选知会点" |

判断记录（`IRenderer3D` 是可选参数，不在必填参数里）：`PresentationAssembly` 构造函数末尾新增
`IRenderer3D? renderer3D = null`（缺口 13）——`VfxPlayer` 的 `attach_mode: socket` 真挂接（经
`IModelHandleProvider`/`ModelHandleResolver`）需要它，但纯 sprite 型游戏不接模型渲染时仍应能正常
装配，因此按"按各模块宿主实际需要的最小集合注入、未提供时降级"原则设为可选，未传时 `VfxPlayer`
的 socket 模式保持退化为 world 播放（见 `presentation/vfx_sfx/README.md` 判断记录 1）。

### `PresentationAssemblyOptions` 可选知会点（任务书点名"保留注入委托"两项 + 其余游戏内容项）

| 字段 | 默认值 | 说明 |
|---|---|---|
| `LoadedMapIdResolver` | `null`（缺口 11 恢复：`ShellHost.LoadGame` 现优先用 `LoadResult.CurrentMapId`，本委托降级为该字段为 null 时才用的可选覆盖） | 见 `presentation/shell/core/ShellHost.cs` `LoadGame` 判断记录 |
| `TargetResolver` | 恒 `null`（当前无目标） | `TargetPathProvider` 用 |
| `HudPowerTypes` | `[WellKnownPowers.Health]` | 与 `CreatureOptions.DefaultPowerTypes` 同一默认 |
| `CharacterStatConfig`/`EquipmentSlotIds`/`PauseMenuOptions`/`SettingsActionNames` | 空列表 | 架构未拍板具体游戏内容，见各字段注释 |
| `OnFloatingText`/`OnFreeze`/`OnFlash` | 空实现 | 09 §6.1 四种反馈动作里，`play_vfx`/`play_sfx`/`shake_camera` 已经有默认落地（见装配步骤 3），其余三种的具体呈现（UI 控件池、tick 节奏、材质参数）不属于任何一个 L5 模块的契约范围 |
| `AutoConfigureCameraFromFirstProfile` | `true` | 见装配步骤 1 |
| `RenderOptions` | `null`（`RenderConventionHost` 用 `RenderOptions` 默认值构造，`DirectionIndexRemap` 恒等映射） | 缺口 8：镜头朝向与 05 §3.1 默认约定不一致时的口味配置项入口 |
| `NewGameStarter` | 真正被调用时抛 `NotSupportedException`（构造期不调用，不影响"构造成功"） | 如何创建一局新游戏的起始状态是具体游戏的事，框架没有默认实现 |
| `ViewBinderOptions`/`VfxOptions`/`SfxOptions`/`CameraHostOptions`/`FeedbackOptions` | 透传各模块自己的默认值（`null`） | 各模块既有策略配置项，见各自 README |

（已解决，从本表删除）`ActionBarSlotBindingResolver`/`SaveGameId` 两项此前的"既有契约缺口"均已
解决：动作条槽位绑定改经 `gameplay.Carriers.SkillBindings`（`Core.Carriers.Unit.ISkillBindingHost`，
G1 新增，见缺口 4）；`ISaveSystem` 改由调用方在 `GameplayAssembly` 构造期传入（G1 遗留跟进），
`PresentationAssembly` 不再自建、`SaveGameId` 字段已删除，`SaveSystem` 属性直接转发
`gameplay.SaveSystem`。`SettingsLayers`（原表格一行）已随 `SettingsViewModel` 改接
`IAudioLayerVolumeHost`（缺口 12）一并删除——层清单现由 `AudioLayerVolumeHost` 从
`sfx.def.layer` 去重 + `music` 计算，不再是可覆盖的选项字段。

## 退订

`PresentationAssembly.Dispose()`（幂等）依次 `Dispose()`：`ViewBinder`、`Camera`（`CameraHost`）、
`FeedbackBinder`、`ShellHost`、`ShellViewModel`、十个 UI 视图模型——全部类型均持有
`SubscriptionHandle` 列表并在 `Dispose` 里逐一释放（见各自源码）。**`ViewBinder`/`CameraHost`
未实现 `IDisposable` 已解决（缺口 5）**：两者均已实现，构造期内部 `bus.Subscribe` 返回的句柄
现被保存并在各自 `Dispose` 里退订；测试见 `ViewBinderTests.Dispose_*`/`CameraHostTests.Dispose_*`。

## 判断记录

1. **`DisplayMapCoverageRule` 未注册**：该规则需要调用方显式声明"哪些内容表参与外形域覆盖检查"
   （构造函数要求 `sources: IReadOnlyList<(string table, string idField)>`），且对现有大量尚未
   逐一配上 `display.map` 行的 L0～L4 示例内容会产出大量误报；本类型不代为决定这份 `sources`
   清单，留给具体游戏接入阶段按 04 第 5 节校验器检查项清单自行补上（复用现成规则，不发明新的）。
2. **`FeedbackRuleValidator` 未接入 `RegisterAll`**：其 `Validate(IReadOnlyList<FeedbackRule>,
   IReadOnlyCollection<Id> knownEventKeys)` 签名要求先把 `feedback.binding` 解析成强类型列表、
   并显式传入跨越多个 L2～L4 模块的事件目录（`EventKeys.All`），与
   `IValidationRule.Validate(IDataRegistryView)` 形状不同，本类型不为其代造一个适配包装（避免
   发明新原语）；需要这项校验的调用方应在解析出 `FeedbackRule` 列表后自行调用，见
   `presentation/feedback_binder/README.md`。
3. **`InputMapHost`/`L10nHost`/`ISettingsStore` 由本装配根自行构造，不是 `GameplayAssembly` 的
   属性；`ISaveSystem` 已改由调用方在 `GameplayAssembly` 构造期传入（G1 遗留跟进）**：
   `InputMapHost`/`L10nHost` 是 L0 基础设施但从未被 `GameplayAssembly` 十个 L4 宿主持有，仍由本
   装配根自建；`ISaveSystem` 此前由本装配根自建（需要引擎侧 `IFileSystem`），现改为
   `GameplayAssembly` 构造函数的第 6 位必填参数，`PresentationAssembly.SaveSystem` 属性直接转发
   `gameplay.SaveSystem`（不再自建第二份，见类型注释"缺口 16"）——调用方只需构造一份 `ISaveSystem`
   传给 `GameplayAssembly`，`PresentationAssembly`/`RegisterPersistables` 自动共用同一实例，不再
   需要"两份实例互不知晓对方状态"的额外注意事项。`ISettingsStore` 仍由本装配根自建（构造期提前到
   第一步，供缺口 12 的 `AudioLayerVolumeHost` 使用，见装配步骤 2 注释）。
4. **`ViewBinder`/`CameraHost` 不支持退订——已解决（缺口 5）**：两者均已实现 `IDisposable`，
   构造期 `bus.Subscribe(...)` 返回的句柄现被保存并在各自 `Dispose` 里退订，`PresentationAssembly`
   共享同一个 `RenderConventionHost` 实例（见"退订"一节）。
5. **`AnchorResolver` 留空——已解决（缺口 6）**：`presentation/render` 新增 `IAnchorQuery`
   （`ViewBinder` 实现，见其类型注释"谁实现本接口"判断记录），本装配根构造 `VfxPlayer` 时改传
   `ViewBinder.GetAnchorWorldPosition` 作为 `anchorResolver`；`EntityPositionResolver` 仍作为
   `IAnchorQuery` 查不到时的兜底路径（未绑定 View/model 型外形/锚点未登记）。

## 验收测试（`tests/PresentationAssemblyTests.cs`）

| 用例 | 覆盖点 |
|---|---|
| `Construct_WithMinimalPresentationDataset_DoesNotThrow_AndExposesAllHosts` | 全部宿主非空；`ActionBar.SlotCount` 取自 `ui_layout_definition` 数据而非兜底默认值 |
| `RegisterAll_LoadsFullPresentationDataset_ZeroErrorsAndWarnings` | `PresentationSchemaCatalog.RegisterAll` + 最小表现层数据集零 Error（本夹具不加载 `l10n.text`，允许 `text_key_exists` 的 Warning） |
| `CreateOptions_UsesSameExprSchemaAsGameplaySchemaCatalog` | `ExprSchema` 复用未新造 |
| `ActionBar_FallsBackToOptionsSlotCount_WhenNoLayoutRowPresent` | 数据集没有 `ui_layout_definition` 行时退化为 `PresentationAssemblyOptions.ActionBarSlotCountFallback` |
| `DamageEvent_DispatchesFloatingText_ViaFeedbackBinder` | 一次 `combat.damage_dealt` 经 `FeedbackBinder` 落到 `OnFloatingText` 回调，飘字文本/样式/目标 id 均正确 |
| `Dispose_UnsubscribesFeedbackBinder_SameEventNoLongerDispatches` | 退订后同一事件不再触发；`Dispose` 幂等 |
| `WeaponStyle_ResolvesSwingVfx_FromSampleData` | `WeaponStyleResolver` 接的是本装配根真正建出的目录 |
| `FloatingTextStyles_ContainsSampleStyle_WithColorRef` | 飘字样式目录可读 |
| `SaveSlots_ListsNoSlots_OnFreshFileSystem` | 本装配根自建的 `ISaveSystem`/`SaveSlotsViewModel` 能正常工作 |

夹具用 `Core.Carriers.Creature.CreatureFactory.Spawn` 生成一个真正经 `Stats`/`Powers`/
`Progression` 三处注册的单位当"玩家"（见测试文件 `AddMinimalGameplayTables` 类型注释）——
`HudViewModel`/`PlayerPathProvider` 等在构造期就会真的查询这些宿主，不能像
`GameplayAssemblyTests` 那样用一个从未真正注册过的裸 `Id` 应付过去。
