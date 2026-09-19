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
   audio, fileSystem, sceneRouter, options)` 拿到全部表现层宿主 + 十一个 UI 视图模型 + Shell。
6. 不再需要表现层时调用 `PresentationAssembly.Dispose()`（见"退订"一节）。

## 目录

```
assembly/
  README.md
  PresentationSchemaCatalog.cs      L0～L5 全部 TableSchema/IValidationRule 的统一注册清单
  PresentationAssembly.cs           L5 组装根 + PresentationAssemblyOptions（可选知会点集合）
  ContentValidationAssembly.cs      校验装配入口（ADR-0018 决策 3，见下"校验装配入口"一节）
  tests/
    PresentationAssemblyTests.cs        烟雾测试（见"验收测试"）
    ContentValidationAssemblyTests.cs   校验装配入口验收测试
```

## 校验装配入口（`ContentValidationAssembly`，ADR-0018 决策 3）

[ADR-0018](../../architecture/adr/0018-编辑器随游戏走与框架为此提供的交付物.md) 决策第 3 条
要求把此前只内联在 `toolchain/validator/Program.cs` 里的"调用 `PresentationSchemaCatalog` 汇总
注册目录 + `SpawnSummonOnlyCreatureRule`/`DisplayMapCoverageRule` 两个可选规则的接线参数 +
`DataRegistryOptions` 装配选项"这段逻辑抽为核心库内的单一公开入口，供 `toolchain/validator` 与
编辑器基础套件（ADR-0018 决策 1/2 定义的独立消费方项目，随游戏走、不在本仓库）共同调用——两个
消费方用同一份装配代码，天然不会出现两边分叉的注册顺序/选项默认值，这是"编辑器里看到的红线 =
门禁会报的错"这一验收标准的落地方式。

```csharp
public sealed class ContentValidationOptions {
    DataRegistryStrictness Strictness = WarningsAllowed;
    bool FailOnUnknownTable = true;
    Id? ItemBudgetCurveId;
    ICreatureTemplateQuery? CreatureTemplateQuery;        // 未提供 -> 默认改用 RegistryCreatureTemplateQuery（基于登记表视图），规则默认启用
    IReadOnlyList<(string table, string idField)>? DisplayMapCoverageSources; // 未提供 -> 默认改用 PresentationSchemaCatalog.DefaultDisplayMapCoverageSources，规则默认启用
    IEventBus? Bus;                                       // 未提供 -> 内部建一个 StrictCatalog=false 的总线
}
public sealed class ContentValidationRun {
    ValidationReport Report; IDataRegistryView Registry; IReadOnlyList<OverrideDiagnostic> Overrides;
    int TableCount; int RecordCount;
    IReadOnlyList<string> DisabledOptionalRules;   // 本次因未提供接线参数而未启用的可选规则名
    IReadOnlyList<string> EnabledOptionalRules;
}
public static class ContentValidationAssembly {
    public static IReadOnlyList<string> OptionalRuleNames { get; }  // 固定清单，见下
    public static ContentValidationRun Run(IReadOnlyList<IDataSource> sources, ContentValidationOptions? options = null);
    public static IDataRegistry CreateRegistry(IDataSource primary, ContentValidationOptions options, out IReadOnlyList<string> disabledOptionalRules);
}
```

`Run` 内部装配顺序：`PresentationSchemaCatalog.CreateOptions()` 起步 + `options` 覆盖
`FailOnUnknownTable`/`Strictness` → `new DataRegistry(sources[0], bus, registryOptions)` →
解析 `options.CreatureTemplateQuery ?? new RegistryCreatureTemplateQuery(registry)` →
`PresentationSchemaCatalog.RegisterAll(registry, options.ItemBudgetCurveId, creatureTemplateQuery)`
（`SpawnSummonOnlyCreatureRule` 是否注册仍由该调用内部按 `creatureTemplateQuery` 是否为空决定，见
该方法判断记录；但本入口解析后传入的值只在调用方显式传 `CreatureTemplateQuery` 为空引用时才可能
为空，其余情况恒非空）→ 无条件注册 `DisplayMapCoverageRule`（接线源用
`options.DisplayMapCoverageSources ?? PresentationSchemaCatalog.DefaultDisplayMapCoverageSources`）
→ `registry.LoadAll(sources)` → 汇总 `ValidationReport`/`Overrides`/`TableCount`/`RecordCount`/两个
可选规则清单为 `ContentValidationRun`。`CreateRegistry` 只做到"注册完成、不加载"这一步，供需要先
持有 registry、再自行决定何时/用哪些数据源加载的宿主使用（如编辑器需要在用户操作间隙重复
`IDataRegistry.Reload` 单表）。

判断记录（四个可选规则的"未启用"语义，消费方反馈第 43/44/56 条修正与追问）：`OptionalRuleNames`
现是固定的四项——`"SpawnSummonOnlyCreatureRule"`、`"DisplayMapCoverageRule"`、
`"QuestPrerequisiteIsolationRule"`、`"TalentTreeIsolationRule"`——分两类不同的"默认是否启用"机制：

- 前两条现均在本入口默认启用（`DisplayMapCoverageRule` 自消费方反馈第 34 条起、
  `SpawnSummonOnlyCreatureRule` 自消费方反馈第 44 条根治起）：`options.CreatureTemplateQuery`/
  `options.DisplayMapCoverageSources` 未提供时，本入口分别改用内置默认接线——
  `Core.Carriers.Creature.RegistryCreatureTemplateQuery`（基于刚构造出的 `DataRegistry` 现读现解析，
  见该类型判断记录）、`PresentationSchemaCatalog.DefaultDisplayMapCoverageSources`——而不是把接线
  参数原样透传为 `null`/`Empty`。这套"未提供接线参数即禁用"的机制本身没有删除，只是这两条规则都已
  升级为"未提供则用默认接线"。
- 后两条（消费方反馈第 56 条追问，2026-09-18，`core/gameplay/quest/core/QuestPrerequisiteIsolationRule.cs`/
  `core/numbers/archetype/core/TalentTreeIsolationRule.cs`）用的是不同机制——**显式布尔开关**
  `ContentValidationOptions.EnableGraphIsolationDiagnostics`（默认 `false`，同时控制两条规则，没有
  "提供某个依赖对象即启用"这一档），默认路径下两条恒不注册。这是本入口第一次出现"命令行式可选规则
  开关"（`toolchain/validator` 对应新增 `--enable-graph-isolation`，见 `toolchain/README.md`），与前两条
  "未提供依赖则退化为默认接线"的机制刻意不同——这两条规则是纯展示性提示（孤立节点本身是合法内容
  形态，判断记录见 `core/gameplay/quest/README.md`/`core/numbers/archetype/README.md` 对应小节），没有
  "默认接线"这个概念可退化，只能是"开"或"关"。

`DisabledOptionalRules`/`EnabledOptionalRules` 因此不再有"前者恒为空"的结论：默认选项下
`DisabledOptionalRules` 固定含后两条规则名，`EnabledOptionalRules` 固定含前两条；`EnableGraphIsolationDiagnostics=true`
时四条全部出现在 `EnabledOptionalRules`。本类型不改变底层 `GameplaySchemaCatalog.RegisterAll`/
`RegisterValidationRule` 各自的注册行为（`creatureTemplateQuery`/`displayMapCoverageSources` 仍是决定
是否注册的入参，只是本入口不再把它们原样传 `null`），只是把"这次到底注册没注册"这件事从"调用方自己
看代码才知道"变成"结构化返回值"。

消费方反馈第 43 条：`OptionalRules`（`IReadOnlyList<OptionalRuleDescriptor>`）是"规则名 ↔ 检查名"
关联的单一来源——每项 `CheckName` 直接引用规则类型自己公开的 `CheckName` 常量
（`SpawnSummonOnlyCreatureRule.CheckName`/`DisplayMapCoverageRule.CheckName`），不是另行抄写的字面
量；`OptionalRuleNames` 现由 `OptionalRules` 投影得到。`TryGetOptionalRuleByCheck(checkName, out
descriptor)` 按检查名反查描述符，供内容工具（如编辑器问题面板）判断某条诊断的 `Check` 是否来自
某个可选规则、来自哪个，不必自行维护 PascalCase→snake_case 映射表。

`RecordCount` 取值惯例同 `toolchain/validator/Program.cs` 此前的写法——订阅 `DataRegistry.LoadAll`
内部发出的 `data.load_completed` 事件读 `RecordCount` 字段（阻断态下事件仍会照常发出），不是事后
逐表 `GetAll(..).Count` 求和（`IDataRegistryView.GetAll` 契约：未通过校验时抛异常，阻断态下无法
这样求和）。

`toolchain/validator` 的接线方式（新增 `--display-map-sources` 命令行参数，`--json` 输出新增
`disabled_optional_rules`/`enabled_optional_rules` 字段，文本输出末尾追加一行）见
`toolchain/README.md`"`toolchain/validator`"一节。

## `PresentationSchemaCatalog` 登记的表清单

| 表 | Schema 类 | 示例数据 | `IValidationRule` |
|---|---|---|---|
| （L0～L4 全部表） | `Core.Gameplay.Assembly.GameplaySchemaCatalog.RegisterAll` | 见 `core/gameplay/assembly/README.md` | 同上 |
| `l10n.locale`/`l10n.text` | `Core.Foundation.Localization.L10nSchemas` | `data/_sample/l10n/` | 无（`L10nHost` 构造期自行校验"恰好一条 `is_default`"） |
| `display.map`/`display.anim_set`/`display.equip_visual` | `Core.Foundation.DisplayInfo.DisplaySchemas` | `data/_sample/display/display.map.json` | `DisplayKindFieldGroupRule`/`AnimSetEventsShapeRule`/`EquipVisualModeFieldGroupRule`（消费方反馈第 63 条新增最后一条；`DisplayMapCoverageRule` 未注册，见判断记录 1） |
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
| 2 | `vfx_sfx`：从 `vfx.def`/`sfx.def`/`display.weapon_style` 建目录 → `VfxPlayer`/`SfxPlayer`/`WeaponStyleResolver`/`DisplayInfoResolver`；`EntityPositionResolver` 接第 1 步的只读快照，`AnchorResolver` 接第 1 步 `ViewBinder.GetAnchorWorldPosition`（缺口 6 已解决，见判断记录 5；`EntityPositionResolver` 仍作为查不到锚点时的兜底路径） | 注入的 `IRenderer2D`/`ICamera`/`IAudio`/`IRngHost` |
| 3 | `feedback_binder`：本步提前构造 `L10nHost`（缺口 7，供 `textResolver` 使用，见判断记录 6）→ 解析 `feedback.binding`/`feedback.floating_text_style` → `CompositeFeedbackSink`（`play_vfx`/`play_sfx` 接第 2 步播放器，`shake_camera` 接第 1 步 `CameraHost.Shake`，`flash` 默认经 `ViewBinder.TryGetView` + `IHasCharacterRig` 接到 `ICharacterRig.ProceduralAnim.Flash`——拍板 6，见判断记录 7；飘字/顿帧仍转给 `PresentationAssemblyOptions` 注入回调，默认空实现）→ `FeedbackBinder`（`textResolver: key => L10n.Text(key)`）→ 拍板 5：按 `opts.FeedbackQueueMode` 或跟随 `gameplay.TimeModelSwitch.CurrentMode` 自动切换播放队列模式（见判断记录 8） | `GameplayAssembly.ExprHostFactory`、`GameplayAssembly.Carriers.Units`（`IUnitAccess`）、`GameplayAssembly.TimeModelSwitch` |
| 4 | `ui`：本装配根自行构造 `InputMapHost`（L0 基础设施，不属于 `GameplayAssembly` 十个 L4 宿主，见判断记录 3；`L10nHost` 已提前到第 3 步）→ 三个 `IUiPathProvider`（player/target/unit）→ `UiDataSource` → `UiIntents` → 十一个视图模型（含拍板 7 新增 `ShopViewModel`，直接持有 `gameplay.Economy`，见 `presentation/ui/README.md`） | `GameplayAssembly.Carriers.Rules.*`/`Carriers.Inventory`/`Equipment`、`GameplayAssembly.Quest`/`Economy`/`Dialog`/`AppState` |
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

判断记录（PR130-06 根治，第六轮文档—代码深度审计，`architecture/落地计划/audit-5c444f1-20260908/`）：
上一条"未传时退化为 world"是本模块自身对"没有 provider"这一状态的正确宽容处理，但三处生产装配根
（`games/_template.GameBootstrap`/`Adapter.Unity.Bootstrap.GameFoundationBootstrap`/
`Adapter.Unity.Shell.FrameworkResidentHost`）此前只把 `_host.Renderer3D` 传给了 `UnityViewFactory`
（用于创建 model 型 View），构造 `PresentationAssembly` 时却漏传本参数——不是"游戏没有模型渲染"，而
是三处装配根都确实持有同一个 `IRenderer3D` 实例、只是没有转发给这里，socket 特效因此在选用 model
型外形的游戏里也固定降级为 world 坐标。三处装配根现已统一把同一个 `_host.Renderer3D` 实例同时传给
`UnityViewFactory` 与 `PresentationAssembly`，与 `hitFrameSource`/`weaponStyleSource` 同一套"三处
装配根共享同一份 provider 实例"惯例。

判断记录（`hitFrameSource` 是可选参数，W6 收口新增，ADR-0017 决策 d 遗留缺口收口）：构造函数末尾
再新增 `IHitFrameSource? hitFrameSource = null`，原样透传给内部 `Feedback`（`FeedbackBinderCore`）
的同名构造参数——此前本类型完全没有暴露这个参数，即便引擎侧装配根（如
`adapters/unity/.../GameFoundationBootstrap.cs`）构造了 `CharacterRigHitFrameSource` 传给
`ViewFactory` 完成"rig 登记表"这一半机制，也没有路径接给 `Feedback` 内部真正做命中帧等待判定的
`HitFrameSyncPolicy`，命中帧同步因此此前只有机制、没有生产入口。是否真正生效仍由
`PresentationAssemblyOptions.RenderOptions`/`FeedbackOptions` 各自的 `HitFrameSync` 开关决定（见
`Presentation.Render.RenderOptions.HitFrameSync`/`Presentation.FeedbackBinder.Contracts.
FeedbackOptions.HitFrameSync` 判断记录"两个落点，装配层负责保持一致"）——本参数只负责接线；不传时
（默认）行为与改动前完全一致（`sync: hit_frame` 声明被忽略，全部动作立即派发）。装配方还需要把
**同一个** `RenderOptions` 实例传给引擎侧 `IViewFactory` 实现用于构造具体 `ICharacterRig`（如
`Adapter.Unity.Presentation.UnityViewFactory` 新增的 `renderOptions` 构造参数），否则即便这里接好了
`hitFrameSource`，rig 构造期实际拿到的 `HitFrameSync` 仍是默认 `LogicDriven`，`HitFrameReached` 事件
永远不会触发（这是比"`PresentationAssembly` 未暴露 `hitFrameSource`"更深一层、W6 收口时才发现的
缺口，接线步骤完整版见 `adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md`"命中帧
同步接线步骤"一节）。

### `PresentationAssemblyOptions` 可选知会点（任务书点名"保留注入委托"两项 + 其余游戏内容项）

| 字段 | 默认值 | 说明 |
|---|---|---|
| `LoadedMapIdResolver` | `null`（缺口 11 恢复：`ShellHost.LoadGame` 现优先用 `LoadResult.CurrentMapId`，本委托降级为该字段为 null 时才用的可选覆盖） | 见 `presentation/shell/core/ShellHost.cs` `LoadGame` 判断记录 |
| `TargetResolver` | 恒 `null`（当前无目标） | `TargetPathProvider` 用 |
| `HudPowerTypes` | `[WellKnownPowers.Health]` | 与 `CreatureOptions.DefaultPowerTypes` 同一默认 |
| `CharacterStatConfig`/`EquipmentSlotIds`/`PauseMenuOptions`/`SettingsActionNames` | 空列表 | 架构未拍板具体游戏内容，见各字段注释 |
| `OnFloatingText`/`OnFreeze` | 空实现 | 09 §6.1 反馈动作里，`play_vfx`/`play_sfx`/`shake_camera` 已经有默认落地（见装配步骤 3），飘字/顿帧的具体呈现（UI 控件池、tick 节奏）不属于任何一个 L5 模块的契约范围 |
| `OnFlash` | `null`（默认改走 `ICharacterRig.ProceduralAnim.Flash`，见判断记录 7；显式提供时完全覆盖默认行为） | 拍板 6（09 §4 动画层缺口 6 恢复）：此前空实现，现经 `ViewBinder`+`IHasCharacterRig` 接到程序动画原语 |
| `FlashProfileResolver` | `null`（恒返回 `FlashParams.Default`，忽略 `profileId`） | 09 §6.1 `Flash(profileId, target)` 未定义 `flash_profile` 登记表，具体强度/时长解析留给游戏层；仅在 `OnFlash` 未被显式覆盖时生效 |
| `AutoConfigureCameraFromFirstProfile` | `true` | 见装配步骤 1 |
| `RenderOptions` | `null`（`RenderConventionHost` 用 `RenderOptions` 默认值构造，`DirectionIndexRemap` 恒等映射） | 缺口 8：镜头朝向与 05 §3.1 默认约定不一致时的口味配置项入口 |
| `FeedbackQueueMode` | `null`（跟随 `gameplay.TimeModelSwitch.CurrentMode` 自动切换：离散 `Sequential`、连续 `Immediate`；显式提供时一次性设定且不再自动跟随） | 拍板 5（09 §6.4 离散回放门），见判断记录 8 |
| `NewGameStarter` | 真正被调用时抛 `NotSupportedException`（构造期不调用，不影响"构造成功"） | 如何创建一局新游戏的起始状态是具体游戏的事，框架没有默认实现 |
| `ViewBinderOptions`/`VfxOptions`/`SfxOptions`/`CameraHostOptions`/`FeedbackOptions` | 透传各模块自己的默认值（`null`） | 各模块既有策略配置项，见各自 README |
| `EquipmentVisualSource` | `null`（`ViewBinder.OnSaveLoaded` 不做装备外观对账，行为与改动前完全一致） | P2-08 根治（第十四轮审核 c9ff301）新增：透传给 `ViewBinder` 构造参数同名字段，供同图内继续存活的既有 View 按读档后的真实装备快照对账；装配方需传入与传给 `UnityViewFactory` 的 `equipmentVisualSource` 参数同一个 `EquipmentVisualSource` 实例，见 `presentation/view_binding/README.md` 判断记录 6、`render/README.md` 判断记录 19 |

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
6. **`L10nHost` 从第 4 步 `ui` 提前到第 3 步 `feedback_binder` 构造（拍板 6/缺口 7）**：
   `FeedbackBinder` 新增可选 `textResolver: Func<Id,string>` 参数解析 `text_source: literal` 飘字
   文本键（09 第 7.3 节"文案一律经本地化表用 key 间接引用"），需要在构造 `FeedbackBinder` 之前就
   有一个可用的 `IL10nHost` 实例；`InputMapHost` 与本步无关，仍留在第 4 步原位构造，不随之提前。
7. **`OnFlash` 默认接线改走 `ICharacterRig.ProceduralAnim.Flash`（拍板 6，09 §4 动画层缺口 6 恢复）**：
   此前 `OnFlash` 默认是空实现（09 §6.1 四种"非 play_vfx/play_sfx/shake_camera"反馈动作里唯一没有
   落地的一个）；本装配根现按事件携带的实体 id 经 `ViewBinder.TryGetView` 查 View，若其实现
   `IHasCharacterRig`（`presentation/render` 新增能力接口）则调用 `Rig.ProceduralAnim.Flash`
   （具体强度/时长经 `FlashProfileResolver` 解析，见上表）；查不到 View 或 View 未持有
   `ICharacterRig`（如自定义 `model` 型 View 尚未接 rig）时静默跳过，不抛异常——`OnFlash` 显式提供
   时仍完全覆盖本默认行为。
8. **播放队列模式跟随时间模型自动切换（拍板 5，09 §6.4 离散回放门）**：`FeedbackBinder.Queue.Mode`
   本就是运行期可写属性；本装配根在 `opts.FeedbackQueueMode` 为 `null` 且
   `gameplay.TimeModelSwitch != null`（即调用方为 `GameplayAssembly` 传入了 `clockHost`，装配了
   离散模式）时，订阅与 `TimeModelSwitch` 相同的 `combat.entered`/`combat.left`/`unit.died` 三个
   事件 key，在处理函数里按 `TimeModelSwitch.CurrentMode` 同步切换（离散 → `Sequential`、连续 →
   `Immediate`）。判断记录（为何不需要额外的时序同步机制）：`TimeModelSwitch` 在
   `GameplayAssembly` 构造期（早于本装配根）就已订阅同一批事件并在处理函数内部同步更新
   `CurrentMode`；`Core.Foundation.EventBus.EventBus` 按订阅注册顺序派发同一事件 key 的全部订阅者
   （`List<SubscriberEntry>` 尾插 + 遍历），本装配根的订阅注册在后，收到通知时读到的
   `CurrentMode` 必然已经是切换后的最新值。此前 Unity 引导侧靠"零事件兜底"短路的临时手法随本次
   收口废弃。
9b. **PRES-118-CAMERA 根治（第十八轮审核）：`AutoConfigureCameraFromFirstProfile` 打开时默认补一个
   `CameraHostOptions.FollowTargetResolverOnReset`**：`CameraHost` 默认 `ResetFollowOnSceneLoadFinished`
   为真，`scene.load_finished` 到达时会清空跟随目标（见 `presentation/camera/README.md` 判断记录
   "PRES-118-CAMERA 根治"）；本装配根构造 `Camera` 之前，若调用方未通过 `opts.CameraHostOptions` 显式
   装配 `FollowTargetResolverOnReset`，且 `AutoConfigureCameraFromFirstProfile` 打开，就补一个
   `() => _playerId` 的默认解析函数——与"打开该开关时构造期立即 `Follow(_playerId)`"同一语义，只是
   延伸到后续每一次场景加载完成都重新生效，不是只在构造那一刻生效一次。调用方已经自己装配了
   `FollowTargetResolverOnReset`（哪怕值是 `null` 的委托本身不算"装配"，只有 `CameraHostOptions`
   构造出的对象非空且该属性非 `null` 才算）时完全尊重其选择，不覆盖；`AutoConfigureCameraFromFirstProfile`
   关闭时也不补，保持"未启用自动配置镜头"这条路径的既有语义（调用方完全自行决定何时 `Configure`/
   `Follow`）。三个生产装配入口（`FrameworkResidentHost`/`GameFoundationBootstrap`/`games/_template`
   `GameBootstrap`）均未覆盖 `AutoConfigureCameraFromFirstProfile`（默认 true）且构造 `CameraHostOptions`
   时未装配自定义解析函数，因此全部自动获得这条默认接线，不需要各自改代码。

9c. **PRES-118-SFX 根治（第十八轮审核）：新增 `PresentationAssembly.UpdatePlaybackMaintenance` 统一
   逐帧维护入口**：`Feedback.Update`/`Vfx.Update`/`Sfx.Update` 三步"不受 InWorld/Pause 状态门槛限制、
   必须逐帧维护"的表现步骤，此前由三个生产装配入口各自在 `OnFrameTick` 里手工罗列，`GameFoundationBootstrap`
   与 `games/_template` `GameBootstrap` 两处都漏抄了 `Sfx.Update`（对照 `FrameworkResidentHost` 有
   调用）——连续两次播放同一个缺失音效资源时，`SfxPlayer` 内部记录过的资源 id 不会再次触发加载，
   第二次请求没有任何未来回调可等，只能靠 `Sfx.Update` 内部的超时扫描清理，没有生产入口驱动时永久
   卡在 pending。本方法把三步收敛为一处，接受可选的 `stepRunner: Action<Action>` 参数——
   `FrameworkResidentHost`/`GameFoundationBootstrap` 均有"拍板 12 表现层异常隔离"的
   `RunPresentationStep`（一步抛异常不阻塞同一帧内其余步骤），两者传入各自的该方法即可保留原有隔离
   粒度（三步仍然分别独立 try/catch，不因为集中到本方法就合并成一次 try/catch）；`games/_template`
   `GameBootstrap` 当前没有这层隔离机制，不传 `stepRunner`，行为与改动前对 `Feedback`/`Vfx` 两步完全
   一致（直接调用）。三个入口现全部改为调用本方法，不再各自罗列，避免未来新增第四个入口或改动播放器
   清单时重蹈"抄漏一项"的覆辙。

9. **离散回放门"零事件步骤"永久卡死已根治（W5c，第三轮审计"仍保留项"收口）**：`PlaybackQueue.
   Finished` 只在队列"由非空变空"的边沿触发，一个没有产生任何反馈动作的离散步不会触发该边沿，
   `GameplayAssembly.Advance` 此前每个离散步都无条件进入 `playing_back` 等待，会永久卡死——本
   装配根在 `Feedback`（第 3 步）构造完成后，立即经 `GameplayAssembly.SetPendingPlaybackProbe`
   把 `() => Feedback.HasPendingPlayback` 接入 `gameplay.Pacing`（若其具体类型是
   `Core.Foundation.SimLoop.WaitForPlaybackPacingPolicy`，见该方法与该类型判断记录）；未装配
   离散模式或调用方显式传入自定义 `IPacingPolicy` 时 `SetPendingPlaybackProbe` 内部静默跳过，不
   影响装配成功。生产代码不再需要 Unity 引导侧任何轮询兜底（`adapters/unity` 的
   `GameFoundationBootstrap`/`FrameworkResidentHost` 判断记录同步更新）。跟进（GP-PRES-03）：
   探针原接 `() => Feedback.Queue.PendingCount > 0`，但 `FeedbackOptions.MergeWindow > 0` 时数值
   飘字会先暂存在 `FloatingTextMerger` 内部、窗口到期前既不派发也不进入 `Queue`，队列此刻可能恰好
   为空——只看队列长度会误判"没有待回放内容"而提前放行节奏门；改接 `Feedback.HasPendingPlayback`
   这一统一查询（`Queue.PendingCount > 0 || 合并窗口仍有待派发飘字`）后不再漏看这部分表现，见
   `feedback_binder/README.md` 判断记录。

10. **诊断转发到引擎控制台跟进（2026-09-19）：`VfxPlayer`/`SfxPlayer`/`FeedbackBinder`（含
    `HitFrameSyncPolicy`，两者共享同一诊断实例）/`ViewBinder` 补上 `Diagnostics` 公开出口**：
    1.45.0 落地的诊断转发到引擎控制台只覆盖了 `SpriteCharacterRig.Diagnostics` 一条链路——上述四个
    类型此前虽然都已接受可选构造参数 `diagnostics`，却都没有公开属性能读到自己内部持有的那份
    `IPresentationDiagnostics` 实例，本装配根构造它们时也从未传入外部实例，`adapters/unity` 因此
    完全拿不到引用、无法转发，真实游戏里这四条链路出问题（如 `vfx.def`/`sfx.def` 未登记、锚点查询
    失败、命中帧同步超时等）仍然静默。本次给四个类型各加 `public IPresentationDiagnostics
    Diagnostics { get; }`（ABI 只新增只读属性，`ViewBinder` 保留的 1.12.0/1.13.0 七参数兼容重载
    不受影响）。
    <br/>**设计取舍（共享 vs. 各自独立 `PresentationDiagnosticsRecorder`，本条拍板）**：选择
    **各自独立**——本装配根构造这四个类型时依旧不传 `diagnostics` 构造参数，保持"未注入时各自默认
    `new PresentationDiagnosticsRecorder()`"这一改动前行为不变，`Vfx`/`Sfx`（接口类型，
    `IVfxPlayer`/`ISfxPlayer` 未声明 `Diagnostics`）新增 `VfxDiagnostics`/`SfxDiagnostics` 两个
    转发属性省去 adapters/unity 侧的向下转型，`Feedback`/`ViewBinder` 本身是具体类型，直接读
    `Feedback.Diagnostics`/`ViewBinder.Diagnostics` 即可。理由：①共享一个实例带来的"一次轮询取
    全部诊断"这一好处并不成立——`SpriteRigDiagnosticsPump.Pump` 本就按 recorder 实例身份（对象
    相等性）分别记账已转发到第几条（见 `PresentationDiagnosticsConsoleForwarding.cs`
    `_lastForwardedCount` 判断记录），转发调用点始终是"每个不同的 recorder 各调用一次 `Pump`"，
    与 recorder 是否共享无关，四个独立 recorder 只是多四行 `Pump` 调用，成本可忽略；②控制台侧的
    去重/LRU 上限（500）本就落在 `PresentationDiagnosticsConsoleGate` 这一层、由 adapters/unity
    在单一进程内共用同一个 gate 实例（"一个控制台"这件事本身决定了 gate 必然共享，与 recorder
    结构无关），因此"共享 recorder 混进同一张去重表、被高频子系统占满"这个顾虑对本条取舍其实是
    中立的——真正决定 gate 是否隔离的是 adapters/unity 侧给这四条新链路配几个 gate，不是这里的
    recorder 数量；③各自独立的 recorder 保留了"某一子系统的 `Warnings` 内存列表只含该子系统自己
    产生的消息"这一属性，对未来任何按子系统查诊断的调试工具/测试更友好，不会被其它子系统的消息
    污染。因此本条选择的真正价值是"隔离性"，不是"轮询开销"——后者在两种方案下都一样小。
    <br/>接线落地在 `adapters/unity`（不改本文件已构造好的对象关系）：见
    `adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md` 对应判断记录。

10b. **诊断转发到引擎控制台第三批（2026-09-19）：补上第二批扫漏的第五条可达诊断源
    `CompositeFeedbackSink.Diagnostics`**：第二批只覆盖了 `VfxPlayer`/`SfxPlayer`/`FeedbackBinder`
    （含 `HitFrameSyncPolicy`）/`ViewBinder` 四条，但本装配根第 3 步构造的 `feedbackSink`
    （`Presentation.FeedbackBinder.Core.CompositeFeedbackSink` 局部变量，`play_vfx`/`play_sfx` 找
    不到可用坐标/锚点时记警告）同样持有一份独立的 `IPresentationDiagnostics`，此前既没有公开出口也
    没有被本装配根转发——是与第二批完全同类的遗漏（都是"局部构造、未转发"），因此按同一套模式补齐，
    不另起一套：`CompositeFeedbackSink` 新增 `public IPresentationDiagnostics Diagnostics { get; }`
    （ABI 只新增只读属性，取舍同判断记录 10 的四个类型），本装配根新增
    `FeedbackSinkDiagnostics` 转发属性（同 `VfxDiagnostics`/`SfxDiagnostics` 惯例，构造期不传
    `diagnostics` 参数、保持"未注入时默认自建一份 `PresentationDiagnosticsRecorder`"不变，只转发已经
    构造好的实例）。`adapters/unity` 侧 `PresentationAssemblyDiagnosticsForwarder` 新增一个可选参数
    重载（不改既有 4 源构造函数签名，ABI 只新增重载）接住第五个来源，三处生产装配入口的构造调用同步
    补上 `presentation.FeedbackSinkDiagnostics`，见
    `adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md` 对应判断记录。
    <br/>**扫描结论（本轮对 `IPresentationDiagnostics` 全部持有者逐一复核，不留第六条）**：
    `ViewBinder`/`VfxPlayer`/`SfxPlayer`/`FeedbackBinder`（`HitFrameSyncPolicy` 与其共享同一诊断
    实例，非独立来源）/`CompositeFeedbackSink`/`SpriteCharacterRig` 是仓库内实现或持有
    `Presentation.VfxSfx.Contracts.IPresentationDiagnostics` 的全部六个类型，前五个经本文件/
    `PresentationAssemblyDiagnosticsForwarder` 覆盖，`SpriteCharacterRig` 走独立的
    `SpriteRigDiagnosticsPump` 路径覆盖（判断记录 10 已述，未走构造期注入模式）——不存在第七个
    未覆盖来源。`presentation/ui` 的 `IUiDiagnostics`（`UiDataSource` 持有）是另一套独立契约（自己的
    接口/默认实现，未实现 `IPresentationDiagnostics`，仿 `Core.Rules.Skill.ISkillDiagnostics`
    惯例），不在本条转发基础设施的接线范围内，接入需要先决定是否要合并两套诊断契约，这是设计取舍、
    不是同类小改动，留待设计层另行拍板，不在本次任务范围内顺手接上。

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
| `FlashAction_DefaultWiring_RoutesThroughCharacterRig_WhenViewHasRig` | 拍板 6：`OnFlash` 默认经 `ViewBinder`+`IHasCharacterRig` 落到 `ICharacterRig.ProceduralAnim.Flash`，参数为 `FlashParams.Default` |
| `FeedbackQueueMode_AutoSwitchesWithTimeModel_SequentialInDiscreteCombat_ImmediateOnceBack` | 拍板 5：进战切离散 → `Queue.Mode=Sequential`（事件先入队，`presentation.playback_finished` 在队列清空时发出且仅发一次）；脱战切回连续 → `Immediate` |
| `FeedbackQueueMode_ExplicitOption_OverridesAutoSwitch_NeverChanges` | `opts.FeedbackQueueMode` 显式提供时不再跟随时间模型切换 |
| `PendingPlaybackProbe_WiresToGameplayPacing_TracksFeedbackQueuePendingCount` | W5c：`GameplayAssembly.SetPendingPlaybackProbe` 接线正确，探针实时反映 `Feedback.Queue.PendingCount` 变化（入队变 true、播放耗尽变回 false） |
| `PendingPlaybackProbe_HoldsWhileMergeWindowPending_UntilMergedTextPlayed` | GP-PRES-03：`MergeWindow > 0` 时队列播空但合并窗口未到期，探针仍为 `true`、`presentation.playback_finished` 不发出，验证装配根接的是 `Feedback.HasPendingPlayback` 而不是队列长度 |
| `VfxSocketAttach_EndToEnd_AttachesToResolvedHostHandle_ViaRenderer3D` | 缺口 13：`IModelHandleProvider.TryGetModelHandle` 经 `ViewBinder` 解析、真正驱动 `IRenderer3D.AttachToSocket` |
| `Shop_OpenVendor_ListsSellItems_FromSampleData`（及同组用例） | 拍板 7：`ShopViewModel` 读 `gameplay.Economy` 的商人出售清单/库存/价格，事件驱动刷新，未知商人 id 静默退化 |

夹具用 `Core.Carriers.Creature.CreatureFactory.Spawn` 生成一个真正经 `Stats`/`Powers`/
`Progression` 三处注册的单位当"玩家"（见测试文件 `AddMinimalGameplayTables` 类型注释）——
`HudViewModel`/`PlayerPathProvider` 等在构造期就会真的查询这些宿主，不能像
`GameplayAssemblyTests` 那样用一个从未真正注册过的裸 `Id` 应付过去。

## 验收测试（`tests/ContentValidationAssemblyTests.cs`）

| 用例 | 覆盖点 |
|---|---|
| `OptionalRuleNames_IsFixedFourEntryList` | 固定清单恰好四项（消费方反馈第 56 条追问新增两项后），顺序稳定 |
| `Run_DefaultOptions_BothOptionalRulesEnabledByDefault` | 消费方反馈第 44 条根治后：默认选项（不传接线参数）下前两条可选规则出现在 `EnabledOptionalRules`；后两条新增可选规则（`EnableGraphIsolationDiagnostics` 默认 `false`）出现在 `DisabledOptionalRules` |
| `Run_WithBothOptionalRuleDependencies_BothEnabled_AndDisplayMapCoverageRuleActuallyFires` | 提供两个接线参数后前两条均出现在 `EnabledOptionalRules`；`DisplayMapCoverageRule` 真实生效（构造一条未被 `display.map` 覆盖的 `creature.template` 行，断言产出 `display_map_coverage` 错误）；后两条新增规则不受这两个接线参数影响，仍在 `DisabledOptionalRules` |
| `Run_EnableGraphIsolationDiagnostics_TwoIsolationRulesBecomeEnabled` | 消费方反馈第 56 条追问：`EnableGraphIsolationDiagnostics=true` 时 `QuestPrerequisiteIsolationRule`/`TalentTreeIsolationRule` 从 `DisabledOptionalRules` 移入 `EnabledOptionalRules` |
| `Run_WarningsBlockStrictness_WarningOnlyReport_IsBlocking` | 同一份只含 Warning（`text_key_exists`，`l10n.text` 未加载）的数据集：`WarningsAllowed` 不阻断，`WarningsBlock` 阻断 |
| `Run_ProducesSameIssueSet_AsDirectPresentationSchemaCatalogRegisterAll` | 同一份数据分别经 `ContentValidationAssembly.Run` 与手工 `PresentationSchemaCatalog.RegisterAll` + `LoadAll` 两条路径，问题集合（格式化字符串排序后逐条比较）与 `IsBlocking` 完全一致 |

## 04 第 5 节数值类校验项分级表：集中登记清单（`NumericValidationRuleCatalog.cs`，T-N5-3）

分阶段落地计划 T-N5-3：`Presentation.Assembly.NumericValidationRuleCatalog`（新文件）是 04 第 5 节
"数值类校验项分级表"全部规则的只读集中登记清单——`(RuleId, CheckName, Severity, NonEscalatable,
GradingItemName, Group, RequiresAnchor)` 逐字段直接引用各规则类自己公开的 `RuleId`/`CheckName`
常量，不写字面量（同 `ContentValidationAssembly.OptionalRuleDescriptor` 先例）。
`ContentValidationAssembly.NumericRules` 是它的单一来源转发（同 `OptionalRules` 惯例），
`toolchain/validator --json` 的 `rules[]` 据此追加 `category`/`group`/`check_names`/
`requires_anchor`/`enabled` 五个字段。

**判断记录（数量口径，T-N5-3 如实上报的核对表内部不一致）**：`数值规则核对表-N5.md` 自称"阻断 11
+ 警告 7 = 18 项"，但该表第 1 节实际列出 13 行（`曲线单调有限` 一个 04 概念条目因 `prog.level_curve`/
`combat.resist_curve` 两张密集枚举表各自需要独立 `IValidationRule` 实现，拆成 B1/B1a/B1b 三行），
第 3 节"小结"的"16 已落地 + 3 部分 + 1 缺"合计 20 也印证了这一点。本清单按可独立验证注册的技术行数
登记——阻断 13 行 + 警告 7 行 = 20 行，是 18 个概念条目的超集，不会因为按概念条目数收窄而漏验证
`ProgLevelCurveValidationRule`/`CombatResistCurveValidationRule` 两个独立实现类的注册。详见该文件
类型级判断记录。

**判断记录（三条锚点依赖）**：`skill_budget_hard_cap_exceeded`/`skill_budget_deviation`（均来自
`SkillBudgetValidationRule`）与 `item_grant_value_exceeds_share`（`ItemGrantValueExceedsShareRule`）
依赖阶段 N6 才接入的 `ISkillBudgetAnchorProvider`；`RulesSchemaCatalog`/`CarriersSchemaCatalog`
默认注册时都不注入真实实现，`rules[].requires_anchor=true` 的这三行 `enabled` 固定为 `false`，如实
反映"已注册但锚点未接入前不产生任何问题"，不是动态探测。

**判断记录（2026-09-16，深度复审 E-M1：并入"仿真"分组三行，总数 20→23）**：04 第 5 节分级表在 N6
收尾（T-N6-2a）时把 `Core.Sim.SimAnchorValidationRule`（`sim_anchor_level_continuity` 阻断 +
`sim_anchor_expected_item_level_monotonic` 警告）与 `Core.Sim.SimScenarioValidationRule`
（`sim_scenario_level_coverage_required` 阻断）正式并入分级表，此前 T-N6-8a 曾记录"目录不收录"
（理由是这三条属框架工具专用，经 `ContentValidationOptions.ExtraSchemaRegistration` 按需接入，不
在默认注册链路上）；深度复审领域 E（E-M1）发现该口径与 04 文档"23 行"不一致后，设计层裁定推翻
"不收录"、采纳并入，本清单当时登记 23 行（阻断 15 + 警告 8），新增分组"仿真"。这些行的 `RuleId`/
`CheckName` 用字符串字面量而不是 `nameof(...)`/常量引用——`Presentation.Common` 是 `build.ps1`
`$CoreAssemblies` 六个发布给 Unity 的核心 DLL 之一，不含 `Core.Sim`，编译期引用 `Core.Sim` 类型会
让 `Presentation.Common.dll` 产生对 `Core.Sim.dll` 的硬依赖，在只拿到六个核心 DLL 的游戏工程里
加载 `NumericValidationRuleCatalog` 类型即抛异常；`presentation/tests/Tests.PresentationCommon.csproj`
已引用 `Core.Sim`（`ExtraSchemaRegistration` 接线需要），改用测试反射比对锁死字面量与
`Core.Sim` 真实常量一致，详见该文件类型级判断记录与 `NumericValidationRuleCatalogTests.
Catalog_SimGroupLiterals_MatchCoreSimRealConstants`。

**判断记录（2026-09-16，深度复审 E-S2：新增带宽键名拼写检查，总数 23→24）**：`sim.scenario.bandwidths`
是自由字符串键的 map（schema 不枚举合法键），拼错键名此前全链路（schema、校验规则、
`BaselineComparer.ResolveTolerance`、两处仿真运行器的 `ResolveBandwidths`）静默回退默认值，无任何
诊断。`SimScenarioValidationRule` 新增警告级检查 `sim_scenario_bandwidth_key_unknown`（不可提升），
已知键集合来自新增的 `Core.Sim.SimBandwidthKeys.KnownKeys`（与 `BaselineCompareOptions.
DefaultLeafBandwidthKeys` 的取值单一来源，改一处两处都跟着变）；本清单登记 24 行（阻断 15 + 警告
9），"仿真"分组扩到四行。`data/_sample`/`games/_template`/嵌入数据集（`core/sim/tests/data`）已核对
过全部 `bandwidths` 键均在已知集合内，不会触发本项警告。

**判断记录（2026-09-17，消费方反馈第 53 条：新增成长仿真同档位歧义检查，总数 24→25）**：
`Core.Sim.GrowthSimulation.ResolveCreatureFamily` 按 `tier`+`level` 定位候选 `creature.template`
时，若同档位存在多条对玩家阵营敌对的候选，运行时按登记顺序第一条生效（详见
`core/sim/README.md` 消费方反馈第 52-53 条判断记录），但此前内容校验层没有任何离线检查能提前
发现这类数据配平疏漏。新增 `Core.Sim.SimGrowthOpponentAmbiguityValidationRule`，检查名
`sim_growth_opponent_ambiguous`，Warning 级、不可提升——静态按 `fac.faction.default_reaction`/
`fac.reaction_matrix` 复算"对玩家阵营（假定 `fac.player`）是否敌对"（不能像运行时那样构造真实
`StandardPlayer` 实例，只能假定这一固定 id，`fac.player`/`fac.faction` 未登记时优雅跳过、不误报），
按 `tier`+`level` 分组，组内敌对候选 ≥2 条即对组内每条记录各产出一条问题。同批本清单登记 25 行
（阻断 15 + 警告 10），"仿真"分组扩到五行、`sim.anchor`/`sim.scenario`/`creature.template` 三表
专属五项。字符串字面量而非 `nameof(...)`/常量引用的理由与上面三条"仿真"分组条目相同（见
`NumericValidationRuleCatalog.cs` 类型级判断记录）。`data/_sample` 一度因 `creature.sample_summon_
totem`（`summon_only` 占位生物）与 `creature.sample_beast` 同 `tier=creature.tier.sample_normal`、
`level=1` 触发过本项警告——修数据（把 `sample_summon_totem` 挪到独立 tier `creature.tier.
sample_summon`）后恢复 0 警告，见 `data/README.md` 对应判断记录，不是放宽本规则。

## 验收测试（`tests/NumericValidationRuleCatalogTests.cs`）

| 用例 | 覆盖点 |
|---|---|
| `Catalog_HasFifteenBlockingRowsAndNineWarningRows_TwentyFourTotal` | 阻断 15 行 + 警告 9 行 = 24 行（见上"数量口径"判断记录，含深度复审 E-M1/E-S2 并入的"仿真"分组四行） |
| `Catalog_TotalCount_MatchesArchitecture04SectionFiveIntroSentence` | 交叉核对：本清单总数/阻断数/警告数与 04 文档第 5 节引言句解析出的数字一致，04 文档改总数而本清单未跟进（或反过来）会立刻失败 |
| `Catalog_AllWarningEntries_AreNonEscalatable` | 04 分级表"警告"整组不可提升 |
| `Catalog_CheckNames_AreAllDistinct` | 24 行检查名互不相同 |
| `Catalog_ExactlyThreeEntries_RequireAnchor` | 仅"技能预算硬上限/偏离""授予价值超特效占比"三行依赖阶段 N6 锚点 |
| `Catalog_SimGroupLiterals_MatchCoreSimRealConstants` | "仿真"分组四行的字符串字面量（`RuleId`/`CheckName`）与 `Core.Sim` 真实常量/`GetType().Name` 逐一比对一致 |
| `Catalog_EveryDistinctRuleId_IsActuallyRegistered_AgainstSampleData` | 核心验收——对 `data/_framework`+`data/_sample` 跑真实 `ContentValidationAssembly.Run`，清单里每个不同 `RuleId` 都必须出现在 `ValidationReport.Rules` 里且级别/`NonEscalatable` 一致（"禁止漏注册"的可执行断言），并顺带验证零阻断 |
| `ContentValidationAssembly_NumericRules_IsSameInstanceAsCatalogEntries` | 单一来源转发，不是两份拷贝 |
