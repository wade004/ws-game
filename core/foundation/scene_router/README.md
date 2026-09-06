# L0 基础层 · scene_router 场景路由

职责：地图/场景切换的标准流程、加载画面进度、存档/游戏层钩子挂载点（见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L0 模块表 `scene_router` 行、
[03_运行时骨架.md](../../../architecture/03_运行时骨架.md) 第 6、9 节）。本模块提供：
`SceneDescriptor`（从 `world.map` 记录抽取场景路由需要的最小字段）、`ISceneRouter`/
`SceneRouter`（按 03 第 6 节六步流程发起/推进场景加载，转移应用状态机、触发
`pre_unload`/`post_load` 钩子、发出 `scene.load_started`/`scene.load_finished`/
`scene.unloaded` 三个事件）。

依赖：`core/foundation/common`（`Id`、`Vec2`）、`core/foundation/data_registry`
（`TableSchema`、`FieldSchema`、`DataRecord`、`IDataRegistryView`）、
`core/foundation/engine_adapter`（`IResourceLoader`、`ResourceKind`）、
`core/foundation/app_lifecycle`（`IAppStateHost`、`AppState`）、
`core/foundation/sim_loop`（`IWorldSim`，调用 `ClearAll`）、
`core/foundation/hook_registry`（`IHookRegistry`、`WellKnownHooks`、`HookArgs`）、
`core/foundation/event_bus`（`IEvent`、`IEventBus`）与 .NET 标准库；不引用任何引擎适配层
具体实现、不使用系统时间、不使用多线程、不使用反射、不使用系统级 `Random`。

不负责什么：

- 不构建新场景的 `WorldSim` 初始状态（03 第 6 节步骤 5"依据 Spawn 刷新规则与当前 WorldState
  构建新场景初始状态"）——这属于 `spawn_system`/L4 世界模块的职责；本模块在
  `scene.load_finished` 发出、`post_load` 钩子触发之后即完成自己的职责，具体的刷新执行由
  `post_load` 回调（游戏层或 `spawn_system` 注册）承担。
- 不做 `world.map` 完整字段的 schema 登记与校验——只登记场景路由需要读取的
  `id`/`scene_ref`/`nav_ref`/`spawn_points` 四个字段（见 `core/WorldMapSchema.cs` 判断记录），
  `regions`/`teleport_points`/`music_ref`/`allowed_difficulties` 等字段留给 `world.map` 真正
  的归属模块（L4 对象模型与世界）登记。
- 不实现加载画面的具体呈现——`LoadProgress` 只是一个数值，加载画面的界面、动画、文案
  由游戏层经 `presentation/shell` 提供（见 01 模块表 `scene_router` 行"配置项：加载画面呈现
  方式，游戏层可替换"）。
- 不新增 `scene.load_failed` 一类事件——`found.event_catalog.json` 未登记该事件，新增事件
  按 [12_扩展与变更流程.md](../../../architecture/12_扩展与变更流程.md) 走审批，不是本任务
  能单方面决定的事；加载失败改为"记诊断 + 状态回落 Idle + 尝试转回 MainMenu"，见下方判断
  记录 3。

## 目录

```
scene_router/
  README.md
  contracts/
    SceneDescriptor.cs   SceneDescriptor（FromRecord）
    SceneRouterState.cs  SceneRouterState 枚举
    SceneHookCallback.cs SceneHookCallback 委托
    ISceneDiagnostics.cs ISceneDiagnostics
    ISceneRouter.cs      ISceneRouter
    Events.cs            SceneRouterEventKeys、Scene{LoadStarted,LoadFinished,Unloaded}Event
  core/
    WorldMapSchema.cs        world.map 的部分字段 TableSchema 登记
    InMemorySceneDiagnostics.cs ISceneDiagnostics 默认实现
    SceneRouter.cs            ISceneRouter 默认实现
  schema/
    README.md              world.map 被读取字段的说明与归属说明
  tests/
    SceneRouterTestSupport.cs
    SceneDescriptorTests.cs
    SceneRouterLoadFlowTests.cs
    SceneRouterFailureAndGuardTests.cs
```

## 设计要点与判断记录

1. **`nav_ref` 在 `SceneDescriptor` 上暴露为可空 `string?`，但 `WorldMapSchema.Table` 仍按
   05 原文把它登记为必填**：05_对象模型与世界.md 第 4.1 节字段表原文 `nav_ref` 必填"是"，
   任务书描述 `SceneDescriptor` 需要的字段时却写"导航资源引用（可空）"，两处字面矛盾。处理
   为：数据契约层面遵循 05（`WorldMapSchema` 仍要求必填，缺失时正常触发
   `required_field` 校验错误），C# 类型层面按任务书字面要求把 `SceneDescriptor.NavRef`
   做成可空类型并用 `TryGetString` 防御性读取——多一层防御不违反 05 的必填要求，也满足
   任务书对类型形状的字面拍板；`SceneRouter.LoadScene` 因此对 `NavRef` 做 null 检查，
   只有非空时才发起该资源的加载。已在 `SceneDescriptor.cs` 类型注释记录，供设计层复核
   两处文档口径是否需要统一。

2. **`world.map` 只登记场景路由读取的四个字段，不登记完整 05 第 4.1 节字段表**：任务书给了
   两种处理方式供选择。经查当前 `DataRegistry.RunFieldValidation` 实现，字段校验只遍历
   `TableSchema.Fields` 里登记的字段，从不检查记录中是否存在未登记的额外字段——也就是说
   两种处理方式在当前实现下效果等价（`regions`/`teleport_points`/`music_ref`/
   `allowed_difficulties` 等未登记字段的存在都不会触发任何校验错误）。选择更简单的"只登记
   本模块读取的字段"，减少本模块对 05 归属字段的重复维护，详见 `core/WorldMapSchema.cs`
   类型注释。

3. **加载失败路径：不新增事件，记诊断 + 状态回落 Idle + 尝试 `RequestTransition(MainMenu)`**：
   `found.event_catalog.json` 未登记 `scene.load_failed`，新增事件需要走 12 的扩展流程，不
   是本任务能单方面拍板新增的。任务书据此明确改为"记诊断、状态回到 Idle、
   `app.RequestTransition(MainMenu)`"。**已知缺口已解决**：`AppStateMachineConfig.Default()`
   （`app_lifecycle` 模块）T1-9 收尾修正已补上 `AllowTransition(AppState.Loading,
   AppState.MainMenu)` 这条转移（默认表现为 Boot→MainMenu、MainMenu→Loading、
   Loading→{InWorld,MainMenu}、InWorld→{Pause,MainMenu,Loading}、Pause→{InWorld,MainMenu}）
   ——按默认配置调用加载失败路径时，`RequestTransition(MainMenu)` 现在可以正常返回 true，
   应用状态机不会再卡在 Loading。

4. **资源种类映射：已由 ADR-0016 解决**：`scene_ref`/`nav_ref` 此前都借用
   `ResourceKind.DataTable`（`IResourceLoader.LoadAsync` 当时只有 `Image|Audio|Font|DataTable`
   四种，没有一种精确对应场景/导航资源）；ADR-0016 决策 5 给 `ResourceKind` 增补了
   `Scene`/`NavMesh`/`Effect` 三个取值，本模块现改用 `ResourceKind.Scene`/`ResourceKind.NavMesh`
   分别加载 `scene_ref`/`nav_ref`。

5. **场景卸载级联清理**：`SceneRouter` 可选注入 `ISpatialQuery`/`INavigation2D`
   （ADR-0016 决策 7）；卸载旧场景时，`IWorldSim.ClearAll` 已经把每个实体的 `entity.destroyed`
   入队，`DispatchPending` 后逐实体经 `ISpatialQuery.Unregister` 同步移除登记（见
   `core/carriers/assembly/EntitySpatialSyncHost`），本模块再额外调用一次
   `ISpatialQuery.Clear()`/`INavigation2D.Clear(mapId)` 做整图兜底清空（`mapId` 取被卸载的
   旧场景 id——判断记录：05/03 未见"场景 id 与地图 id 是否同一 Id"的显式条款，按既有惯例
   `Entity.MapId` 与所属场景 `world.map` 行 id 同值处理，供设计层复核）。

5. **03 第 6 节步骤 4"先 pre_unload 再卸载旧场景"的时序：放在异步加载完成之后（`Update`
   内），不是 `LoadScene` 调用当下**：03 原文步骤编号顺序是 1 触发 → 2 转 Loading → 3 异步
   加载 → 4 pre_unload+卸载旧场景 → 5 构建新场景 → 6 转 InWorld+post_load+
   scene.load_finished；也就是说卸载旧场景发生在异步加载**之后**，不是与"发起加载"同时。
   这样处理的原因（任务书原文即如此判断）：避免加载中途失败时已经把旧场景清空，导致
   "新场景没加载成功、旧场景又已经被销毁"的双输局面。本实现严格按此时序：`LoadScene`
   只发起加载，`Update` 检测到全部资源就绪后才触发 `pre_unload`/`ClearAll`/`scene.unloaded`，
   紧接着转 InWorld/`post_load`/`scene.load_finished`。

6. **`RegisterPreUnloadHook`/`RegisterPostLoadHook` 返回 `SubscriptionHandle`**：03 第 9 节
   原文签名是 `void`，但两者是对 `IHookRegistry.Register`（返回 `SubscriptionHandle`）的
   便捷封装；不透传返回值会让调用方失去"取消这次注册"的能力，与 `IHookRegistry.Register`
   本身的契约不一致。按"便捷封装应尽量透传底层能力"的原则改为返回 `SubscriptionHandle`，
   供设计层复核是否需要同步更新 03 第 9 节伪代码。

7. **`ClearAll` 之后、`scene.unloaded` 之前显式调用一次 `IEventBus.DispatchPending`**：
   `IWorldSim.ClearAll` 只把每个实体的 `entity.destroyed` 加入事件队列，不立即派发（与
   `WorldSim.Tick` 阶段 8 同样的"只 Enqueue"约定，见 sim_loop 模块）。`SceneRouter` 不运行在
   `WorldSim.Tick` 循环内，没有别的地方会替它把这批事件发出去；若不显式派发，订阅者会在
   收到 `scene.unloaded`（`PublishImmediate`，立即同步派发）之后才收到这批 `entity.destroyed`
   （留到调用方下次驱动 tick 或手动 `DispatchPending` 时才会送达），顺序与任务书"pre_unload
   先于 ClearAll（实体计数归零、entity.destroyed 送达）先于 scene.unloaded"的要求相反。因此
   `FinishLoading` 在 `ClearAll` 之后、`PublishImmediate(SceneUnloadedEvent)` 之前插入一次
   `_bus.DispatchPending()`，保证 `entity.destroyed` 确实先于 `scene.unloaded` 送达订阅者。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| 场景加载六步流程的实现机制、状态机、事件时序 | 是 | 具体场景清单（`world.map` 数据行） |
| `pre_unload`/`post_load` 挂载点声明与便捷注册 | 是 | 挂载点上的具体回调实现（存档、Spawn 刷新等） |
| 加载进度轮询（`LoadProgress`） | 是 | 加载画面的具体呈现方式 |
| 加载失败的诊断记录与状态回落 | 是 | 是否需要 `Loading→MainMenu` 转移、对应 UI 提示 |
