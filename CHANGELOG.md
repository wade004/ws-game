# 变更日志

本文件记录 ws-game（游戏技术基础架构框架仓库）各构建产物版本号之间的变更，格式遵循
[Keep a Changelog](https://keepachangelog.com/) 惯例；版本号遵循语义化版本
（[SemVer](https://semver.org/)）：`MAJOR.MINOR.PATCH`——MAJOR 表示不兼容变更（走 ADR 审批的
契约签名变化、存档格式不兼容、数据表字段删改）；MINOR 表示向后兼容的新增能力；PATCH 表示缺陷
修复与文档勘误。单一版本源见仓库根 `VERSION` 文件；版本号与发布流程见根 `README.md`"版本与发布"
一节。

## [Unreleased]

（尚未发布的变更累积在此，随下一次 `build.ps1 -Release` 归档为对应版本号的条目。）

## [1.10.0] - 2026-09-09

导航与移动公共接口补齐（W9，响应游戏侧 5 项需求：停止/取消接口、端点契约与精确接合、寻路与
Raycast 拐角判定统一、寻路失败公共处理契约、动态阻挡后现有路径处理），逐项落地见下"新增"/
"生命周期与事件顺序"/"迁移说明"三节；核心侧与引擎侧改动、`core` 六工程 2533/2533 与 Unity
EditMode 60/60、PlayMode 263/263 验收过程中另发现并根治两处真实缺陷（非本次新增功能，见"修复"
一节）。均属核心载体层/引擎适配层能力补齐，无数据表字段删改，无存档格式变更。

### 新增

- **`Core.Carriers.Unit.MovementHost.Stop(Id unitId)`**（需求 1 落地）：经 `IWorldSim.SubmitIntent`
  提交一条 `Kind == "move_stop"` 的意图，下一次移动与导航阶段生效；不检查 `MovementLocked`/
  `NoMove`（停止不受控制效果限制）；同一 tick 内幂等（重复 `Stop` 只触发一次 `OnMoveStopped`）。
- **`Core.Carriers.Unit.MovementHost.OnMoveStopped`**（需求 1 落地）：
  `delegate void MoveStoppedHandler(Id unitId, Vec2 position, MoveStopReason reason);`，
  `enum MoveStopReason { Requested, PathFailed, BlockingChanged, Replaced }`——分别对应主动
  `Stop`、按 `PathFailurePolicy.Stop` 因寻路/重算失败停止、按 `BlockingChangePolicy.Stop` 因阻挡
  变化停止、新 `Request` 替换仍存在的旧路径。
- **`Core.Carriers.Unit.MovementHost.OnMoveFailedDetailed`**（需求 4 落地）：
  `delegate void MoveFailedDetailedHandler(Id unitId, Vec2 from, Vec2 to, MoveFailReason reason);`，
  `enum MoveFailReason { NoPath, BlockingChanged }`；与既有 `OnMoveFailed`（签名/触发时机不变）在
  同一失败点同时触发，是补充而非替代。
- **`Core.Carriers.Unit.MovementOptions.PathFailurePolicy`**（需求 4 落地，新增顶层枚举
  `{ KeepOldPath（默认）, Stop }` + 同名属性）：寻路失败或阻挡重算失败时的公共处理策略。
- **`Core.Carriers.Unit.MovementOptions.BlockingChangePolicy`**（需求 5 落地，新增顶层枚举
  `{ Replan（默认）, Revalidate, Stop, Ignore }` + 同名属性）：导航阻挡发生变化后，对单位已持有
  的现存路径的处理策略。
- **`Core.Carriers.Unit.MovementState.NavVersion`**（需求 5 落地，`int`，构造函数追加末位可选
  参数 `navVersion = 0`，源码兼容）：记录该单位当前路径建立时所依据的导航阻挡版本号，供阻挡重验
  比对。
- **`Core.Foundation.EngineAdapter.INavigation2D.GetBlockingVersion(Id mapId) => 0`**（需求 5
  落地，默认接口成员）：每次 `SetBlocking`/`Clear`/`BuildNavMesh` 使某地图可行走判定结果变化时
  递增（各 `mapId` 独立计数）；返回 0 表示不支持版本追踪，调用方对 0 视为"不做自动重验"——未
  重写本成员的既有实现（含未来新增的引擎适配层实现）源码/二进制兼容，行为等价于"不支持版本
  追踪"。`Adapters.Stub.StubNavigation2D`、Unity `UnityNavigation2D` 均已实现真实的按地图计数。
- **`INavigation2D.FindPath` 端点契约精确化**（需求 2 落地，契约文档 + `StubNavigation2D`/
  `UnityNavigation2D` 网格实现同步）：`from`/`to` 任一不可行走返回 `null`（优先于零长度判断）；
  `|from-to| <= 1e-6` 返回单元素路径 `[from]`；成功路径 `path[0]` 精确等于 `from`、
  `path[^1]` 精确等于 `to`（网格路径与精确端点的"接合段"复用与 `Raycast` 相同的判定，接合失败
  退化到相邻可行走格或返回 `null`）。
- **`Raycast`/`FindPath` 统一可通行规则**（需求 3 落地，契约文档 + `StubNavigation2D`/
  `UnityNavigation2D` 同步）：线段与阻挡区域**内部**相交才受阻，仅边界/角点相切不算受阻；
  `FindPath` 返回路径的每一段 `Raycast` 必为 `null`；网格实现对角相邻格仅当两个正交邻居都可
  行走时才允许联通（禁止切角）。
- **`adapters/conformance` 一致性场景**：`Navigation2DScenarios` 新增 5 个场景（端点精确接合、
  零长度路径、不可行走端点返回 null、双矩形拐角每段 `Raycast` 不受阻、`GetBlockingVersion`
  随三类变更操作递增——该场景对返回恒为 0 的实现走 `assert.Skip`，视为合法退化），
  `INavigation2D` 场景数由 4 增至 9，跨桩实现与 Unity 实现同源驱动。

### 生命周期与事件顺序

`Core.Carriers.Unit.MovementTickHandler.Execute` 按固定四步推进（原三步基础上插入阻挡重验一
步，未改变既有推进逻辑本身）：

1. **停止**：处理本 tick 全部 `move_stop` 意图——清空 `CurrentPath`、状态收回 `Idle`，丢弃同一
   tick 内在它之前提交的该单位 `move` 意图（之后提交的照常生效）；确有路径或被丢弃的意图时触发
   `OnMoveStopped(Requested)` 一次，否则静默（幂等）。
2. **移动**：处理存活的 `move` 意图——零长度目标不建路径、不动、不回调；寻路失败触发
   `OnMoveFailed` + `OnMoveFailedDetailed(NoPath)`，按 `PathFailurePolicy` 处理；新路径替换仍
   存在的旧路径时触发 `OnMoveStopped(Replaced)`，随后立即推进本 tick 位移。
3. **阻挡重验**（仅未被前两步处理、仍持有路径的单位）：比较 `GetBlockingVersion` 与
   `MovementState.NavVersion`，不同则按 `BlockingChangePolicy` 处理（`Replan` 直接重算；
   `Revalidate` 先逐段 `Raycast`、受阻再委托重算；`Stop` 直接清空并触发
   `OnMoveStopped(BlockingChanged)`；`Ignore` 不处理）；重算/重验失败触发
   `OnMoveFailed` + `OnMoveFailedDetailed(BlockingChanged)`，再按 `PathFailurePolicy`（`Stop`
   分支触发 `OnMoveStopped(PathFailed)`，与"直接因阻挡变化而停止"的 `BlockingChanged` 原因区分
   开）。
4. **推进**：`ContinuePathCore`（逻辑不变）。

重入安全：`Stop`/`Request` 都只是 `SubmitIntent`（下一 tick 生效），回调内同步调用二者不会在本次
`Execute` 内递归触发新的失败/停止回调。

### 修复

- **`ReplanPath` 重算失败时未推进 `NavVersion`，导致同一次阻挡变化在后续每个 tick 都重复触发一
  次 `OnMoveFailedDetailed`**：默认 `PathFailurePolicy.KeepOldPath`（旧路径原样保留）分支下，
  `ReplanPath` 重算失败只触发了失败回调，没有把 `MovementState.NavVersion` 前移到本次读到的
  `currentVersion`，下一个 tick 阻挡重验比较仍判定"版本已变化"，对同一次阻挡变化重新调用一次
  `ReplanPath`——再次失败、再次回调，此后每个 tick 都重复，直到阻挡状况本身改变。改为该分支下
  显式把 `NavVersion` 前移到 `currentVersion`（语义与"未受阻分支只更新版本号"一致，标记"已经按
  这个版本处理过，虽然重算失败"）；`Stop` 分支路径已被清空，不受影响。新增回归测试
  `MovementTickHandlerTests.BlockingChangePolicy_ReplanFails_DefaultKeepOldPathPolicy_
  DoesNotRepeatFailureEachTick`（默认策略下重算失败后再跑 10 个 tick，失败计数仍为 1，修复前会
  变成 11）。
- **Unity PlayMode 新增测试夹具耗尽跨批次共享的存档槽配额，连带导致同一批次里无关用例静默失败**：
  `SaveSystemOptions.MaxSlots`（默认 20）跨整个 `-runTests` 单次批处理进程共享、只增不减；新增的
  `MovementStopAndBlockingPlayModeTests` 最初每条用例各建一个独一无二的新槽，把既有用例累计已接近
  上限的运行推过 20，导致按夹具名排在更后面的 `VerticalSliceTests` 5 条用例在尝试新建槽时命中
  `SaveFailureReason.SlotLimitReached`，`ShellHost.NewGame` 因 `_saveSystem.Save(...).Success` 为
  `false` 直接 `return false`，不会走到 `_sceneRouter.LoadScene`（单独跑各夹具都各自全绿，只有混
  在完整套件里跑才复现，与仓库既有 `GlobalPlayModeTestSetup.cs` 描述的历史根因同一模式）。改为
  本套件全体用例改用同一个共享存档槽 id——第一次调用消耗 1 份新建配额，此后每条用例的 `NewGame`
  对同一个已存在的槽只是覆盖重写，不再消耗新配额；`PlayModeIsolation.TearDownAfterTest` 已在每条
  用例结束时 `World.ClearAll`，复用同一槽 id 不影响各用例世界状态隔离。

### 迁移说明

- **默认口味下行为逐位一致**：`PathFailurePolicy.KeepOldPath` + `BlockingChangePolicy.Replan`
  是默认值，且未显式实现 `GetBlockingVersion` 的导航实现恒返回 0（阻挡重验整体不生效）——升级
  前后在默认配置下行为逐位一致，回归测试覆盖全部既有用例（`core` 六工程与 Unity EditMode/
  PlayMode 既有用例原样通过，未修改任何既有断言）。
- **既有 `OnMoveFailed` 保留，不是替代关系**：签名与触发时机均不变，新增的 `OnMoveFailedDetailed`
  在同一失败点额外触发，只订阅旧事件的调用方无需任何改动。
- **自定义 `INavigation2D` 实现若要启用自动阻挡重验，需要显式实现 `GetBlockingVersion`**：该成员
  是默认接口方法，不实现不影响编译，但也不会得到自动重验能力（等价于"未支持"，不是"选择
  `BlockingChangePolicy.Ignore`"）——需要在自身的 `SetBlocking`/`Clear`/`BuildNavMesh` 落地方法
  内对相应 `mapId` 递增一个私有版本号计数器并在本方法中返回。
- **`FindPath` 端点精确化对依赖"格子中心"输出的调用方有影响**：升级前部分网格实现可能返回贴近
  网格中心而非精确等于传入 `from`/`to` 的路径端点；升级后 `path[0]`/`path[^1]` 精确等于调用方
  传入的浮点坐标。若调用方此前对首/末点做过"对齐到格子中心"之类的后处理补偿，该后处理现在是
  多余的（不会再有偏差需要补），可以安全移除，不移除也不会出错（幂等对齐同一点）。
- **`Raycast`/`FindPath` 边界相切语义修正（`StubNavigation2D.ClipAxis` 由闭区间改为开区间）**：
  升级前贴边/擦角（线段与阻挡矩形边界或角点相切、不进入内部）会被判定为"受阻"；升级后改为"内部
  相交才受阻，边界/角点相切不算受阻"，与"网格路径的每一段 `Raycast` 必为 `null`"这一契约保持
  一致（此前贴边场景下二者可能矛盾）。依赖旧行为（把贴边当受阻）的调用方需要重新评估——
  `IsWalkable`（点包含判定）未改动，仍是闭区间，本次统一可通行规则的范围限定于线段判定
  （`Raycast`/`FindPath`），不涉及单点判定。
- **`games/_template`/`architecture/13` 未新增对应口味配置项**：`GameOptions.BuildMovementOptions()`
  目前只接了 `DiscreteTurnEquivalentSeconds`/`UnitBlocking` 两项，未暴露
  `PathFailurePolicy`/`BlockingChangePolicy` 作为口味配置项；两个策略经 `MovementOptions` 在
  装配根（`GameBootstrap`/组合根构造 `MovementOptions` 处）直接配置，默认值即为框架推荐值，游戏
  层如需覆盖自行在装配根按需传入，不是缺失能力。

### 版本判据说明

- MINOR：`MovementHost.Stop`/`OnMoveStopped`/`OnMoveFailedDetailed` 均为新增公开成员；
  `MovementOptions`/`MovementState` 均只新增属性/带默认值的可选构造参数；
  `INavigation2D.GetBlockingVersion` 是默认接口方法；`FindPath`/`Raycast` 的契约精确化与边界
  语义修正均不改变方法签名，且默认口味 + `GetBlockingVersion` 恒为 0 时行为与升级前逐位一致。
  无删改既有公开签名，无存档格式变更。

## [1.9.0] - 2026-09-09

第十三方深度审核（codex 第十一轮，基线 `e070e3f`，即 1.8.0 发布提交）3 项确认缺陷
（CORE-180-01～03）+ 1 项候选转已确认（CORE-180-CAND-01）+ 1 项表现层候选转已确认（PRES-180）逐条
核实并根治，另处理 5 项文档漂移/工程证据勘误。逐条核实表、文档更新与能力分类处理、验收结果见
[audit-e070e3f-20260908/followup-2026-09-08g.md](architecture/落地计划/audit-e070e3f-20260908/followup-2026-09-08g.md)。
均属核心存档/规则层缺陷修复与表现层读档对账补强，无数据表字段删改，无存档格式变更。

### 新增

- **`Core.Foundation.SaveSystem.IDerivedStateRebuilder`**（新增契约，`BeforeLoad()`/
  `OnSectionLoaded(string sectionKey)`，CORE-180-01 根治）：可选注入到 `SaveSystem` 的回调，完全
  绕开事件总线（不产生任何可观察事件，不受 `SuppressDispatch` 影响）；`SaveSystem.Load` 在逐段读档
  前调用一次 `BeforeLoad()`，每段成功 `Load()` 后立即调用一次 `OnSectionLoaded(sectionKey)`，供
  装配根在读档期间就地重算评级换算属性、资源池上限等派生缓存，不等读档全部完成、不依赖事件重放。
- **`Core.Foundation.SaveSystem.ISaveSystem.SetDerivedStateRebuilder`**（默认接口方法，默认为
  no-op，CORE-180-01 根治）：注入上述重建器；自定义 `ISaveSystem` 实现方无需新增任何代码即可编译
  通过。
- **`Core.Rules.Assembly.RulesAssembly.ReloadArchetypeAndRace`**（新增公开方法，CORE-180-03 +
  CORE-180-CAND-01 根治）：同图读档后重新聚合单位的种族属性修正/被动光环与职业基础属性，不重复
  调用 `PowerHost.RegisterUnit`（避免对已注册单位抛"不能重复注册"异常）；种族切换时精确移除旧种族
  来源的属性修正、按引用计数递减释放旧种族被动光环，覆盖写入职业基础属性。
- **`Presentation.Common.ISimSnapshot.GetAllEntityIds`/`GetRawKind`**（新增只读成员，均为默认接口
  方法，PRES-180 根治，2026-09-09 版本判据勘误后改写）：`GetAllEntityIds()` 返回当前存活实体 id
  全量列表（默认返回空集合），`GetRawKind(Id)` 返回映射前的原始 `Entity.Kind` 字符串（默认返回
  `null`）；供 `ViewBinder` 在 `save.loaded` 后与已绑定 View 表做全量对账。唯一生产实现
  `WorldSimSnapshot` 已同步覆盖为真实实现；自定义 `ISimSnapshot` 实现方无需新增任何代码即可编译
  通过——未覆盖这两个成员时 `ViewBinder.OnSaveLoaded` 的对账安全退化为"只销毁已不存在实体的
  View，跳过按存活实体补建 View"，详见下"迁移说明"与 `ISimSnapshot`/`ViewBinder.OnSaveLoaded`
  源码判断记录。
- **`ViewBinder` 读档后视图对账**（PRES-180 根治）：构造函数新增订阅 `save.loaded`
  （`SaveEventKeys.SaveLoaded`），收到后立即（同步，不等下一次 `sim.tick_finished`）做一次双向全量
  对账——`ISimSnapshot.Exists` 为假但仍持有绑定的按 `OnEntityDestroyed` 销毁，`GetAllEntityIds()`
  中存在但未绑定的按 `OnEntityCreated` 补建，补齐"读档期间 `SuppressDispatch` 抑制丢弃
  `entity.created`/`entity.destroyed`导致 View 与逻辑实体不一致"这一缺口，幂等（重复读档不重复
  创建/销毁）。

### 修复

- **成功读档后评级换算属性/资源池上限未重算（CORE-180-01，P1）**：`SaveSystem.Load` 整段包在
  `IEventBus.SuppressDispatch` 抑制作用域内，`stat.changed`/`PowerChanged` 等事件被抑制丢弃，导致
  依赖这些事件重算的评级换算属性、资源池上限恢复到读档前的旧值，即便等级/装备等原始字段已正确
  恢复。`player.vitals` 段按"存档值与当前值差额"调用 `ModifyPower` 时若上限仍是旧值，差额被 clamp
  到旧上限，`RecomputeMax` 只在 `Current > newMax` 时下调、不会在 `newMax` 变大时补回 `Current`，
  当前值即便后续再重算上限也不会跟着回升。改为经 `IDerivedStateRebuilder.OnSectionLoaded
  (PlayerEquipment)` 在读到 `player.vitals` 段之前完成一次重算，完全绕开事件总线，不影响既有"读档
  期间业务事件零泄漏"回归。
- **后段读档失败时逆序回滚顺序与依赖方向相反（CORE-180-02，P2）**：`RollbackLoadedSections` 此前
  从后往前遍历，装备重新装备（复用真实 `EquipmentHost.Equip`，内部校验等级需求）先于等级本身被
  回滚恢复，导致等级需求校验用的是本次失败读档写入的低等级值，装备重新装备失败、物品被迫留在
  背包。改为与正常读档同一顺序（从前往后）遍历，回滚本质上变成"再做一次读档，只是文档换成读档前
  的快照"，不需要为回滚单独维护一套依赖顺序规则。
- **同图读档只切换种族/职业字段，未重新聚合对应的属性修正与光环（CORE-180-03 + CAND-01，P2）**：
  `GameplayAssembly.RestoreFromSlot` 判定目标地图与当前地图相同时不触发 `EnterMap`，此前读到
  `player.race_id`/`player.archetype` 段只覆盖字段本身，不会重新聚合旧种族属性修正的移除、新种族
  属性修正/光环的应用，也不会覆盖写入新职业的基础属性。改为经 `IDerivedStateRebuilder.
  OnSectionLoaded(PlayerRaceId)` 触发 `RulesAssembly.ReloadArchetypeAndRace`，同图与跨图路径统一
  覆盖。已知收边范围：新旧职业基础属性键集合不同、或 `power_types` 集合不同时的联动不在本次范围内
  （详见 `RulesAssembly.ReloadArchetypeAndRace` 源码注释与 followup 文档判断记录）。
- **同图读档期间被抑制丢弃的 `entity.created`/`entity.destroyed` 导致 View 与逻辑实体不同步
  （PRES-180，候选转已确认）**：`SaveSystem.Load` 抑制作用域内若某段 `Load`（如
  `DroppedLootPersistable.Load`）往 `WorldSim` 加实体，产生的 `entity.created` 永久丢失，逻辑实体
  已恢复但对应 View 未绑定；`ViewBinder` 此前只在构造期订阅事件，没有读档完成后的补扫入口。改为
  订阅 `save.loaded` 并做全量对账（见上"新增"一节）。

### 迁移说明

- **自定义装配根需注册派生状态重建器**（CORE-180-01/03）：若具体游戏/适配层提供了自定义
  `ISaveSystem`/装配根替代框架默认的 `GameplayAssembly`，且存在依赖事件重算的派生缓存（评级换算
  属性、资源池上限、种族/职业属性修正与光环等），需要实现 `IDerivedStateRebuilder` 并调用
  `ISaveSystem.SetDerivedStateRebuilder` 注册，否则读档后这些派生缓存不会重算，行为等同于本次修复
  之前的框架默认实现。不注册不影响编译（`SetDerivedStateRebuilder` 是默认接口方法），只影响读档后
  派生缓存的正确性。
- **自定义 `ISimSnapshot` 实现可选覆盖两个新成员**（PRES-180，2026-09-09 版本判据勘误后改写）：
  `GetAllEntityIds`/`GetRawKind` 是 C#8 默认接口方法（默认分别返回空集合/`null`），与本版本其它
  新增接口成员同一惯例——任何自定义 `ISimSnapshot` 实现无需新增任何代码即可继续编译通过。
  `GetAllEntityIds()` 语义为"当前存活实体 id 全量列表"，`GetRawKind(Id)` 语义为"映射前的原始
  `Entity.Kind` 字符串，实体不存在返回 null"，均为只读查询、不持有 `Entity` 引用本身（铁律 P1）；
  框架内唯一生产实现 `WorldSimSnapshot` 已同步覆盖为真实实现。若不覆盖，`ViewBinder` 的
  `save.loaded` 全量对账会安全退化：销毁"绑定表里指向已不存在实体"的陈旧 View 这一半不受影响
  （只依赖原有强制成员 `Exists`），"按当前存活实体补建 View"这一半因 `GetAllEntityIds()` 返回
  空集合而天然是空操作——即读档期间被 `SuppressDispatch` 抑制丢弃的 `entity.created` 不会被这类
  自定义实现补扫到，需要完整对账能力的自定义 `ISimSnapshot` 实现方应显式覆盖这两个成员。
- **自定义 `IViewFactory` 实现须保证创建幂等**（PRES-180）：`ViewBinder` 的读档后对账在
  `GetAllEntityIds()` 中存在但未绑定的实体上复用既有 `OnEntityCreated` 路径调用
  `IViewFactory.Create`；若某具体游戏/适配层的 `IViewFactory` 实现对同一实体重复调用 `Create` 会
  产生副作用（如资源重复分配），需要确认其自身具备"同一实体已存在 View 时安全跳过或替换"的幂等
  处理——框架侧 `ViewBinder` 已经用绑定表去重、不会对同一实体重复调用 `Create`，此处针对的是
  `IViewFactory` 实现自身在异常路径下被多次调用时的健壮性。
- **版本判据说明**：本次为 MINOR（`1.8.0` → `1.9.0`）。"新增"一节列出的成员均为新增（新增
  类型/默认接口方法/公开方法/接口成员），无删改既有公开签名；`ISimSnapshot` 新增的
  `GetAllEntityIds`/`GetRawKind` 两个成员（2026-09-09 版本判据勘误后改为）同样是默认接口方法，
  第三方 `ISimSnapshot` 实现方无需新增代码即可继续编译，不构成源码级破坏，按 MINOR 处理不需要
  任何例外说明。（此前一版曾把这两个成员定义为普通接口方法并按"源码级破坏但按 MINOR 处理"的
  例外记录在案，属于版本判据误判——已改为默认接口方法根治，不再需要该例外，具体见上方"新增"/
  "迁移说明"两处改写内容。）

## [1.8.0] - 2026-09-08

第十二方深度审核（codex 第十轮，基线 `8160178`，即本版本发布前的最新提交）4 项发现（CORE-170-01～03、
PRES-170-01）逐条核实并根治，CORE-170-03(a) 审查全仓 `IPersistable` 实现后另发现 8 处同类"先改状态
后校验/边解析边提交"缺陷一并根治。逐条核实表、文档更新与能力分类处理、验收结果见
[audit-8160178-20260908/followup-2026-09-08f.md](architecture/落地计划/audit-8160178-20260908/followup-2026-09-08f.md)。
均属核心规则/存档/玩法层缺陷修复与表现层/引擎适配层缺陷修复，无数据表字段删改，无存档格式变更。

### 新增

- **`Core.Rules.Common.AuraHandleLedger`**（跨来源光环句柄账本，CORE-170-01 根治）：把装备/套装门槛
  加成原有的"跨来源引用计数账本"上移为独立类型，由 `RulesAssembly` 持有单一实例，`CarriersAssembly`
  注入给 `EquipmentHost`，`RulesAssembly` 自身（种族被动）也用同一实例，装备/套装/种族三类来源共享
  同一条账本，互不覆盖对方持有的引用。
- **`Core.Rules.Common.IAuraQuery.TryGetInstanceRef`**（默认接口方法，默认返回 `null`，CORE-170-01
  根治）：按单位与光环定义查询该单位当前是否持有一份有效引用；自定义 `IAuraQuery` 实现方无需新增
  任何代码即可编译通过，默认实现对不参与跨来源账本的测试假实现是安全的等价空实现。
- **`Core.Numbers.Progression.LevelSync`**（委托类型）与 **`Core.Carriers.Unit.WorldUnitAccess.
  SetLevel`**（CORE-170-02 根治）：`ProgressionHost` 在 `RegisterUnit`/`AddXp`/`RestoreState` 三个
  等级确立/变化的时机调用该委托，`CarriersAssembly` 接到 `WorldUnitAccess.SetLevel`（不进
  `IUnitAccess` 接口，仅供组装期委托闭包使用），确立 Progression 为单位等级唯一权威并同步实体
  字段与查询结果。
- **`Core.Foundation.EventBus.IEventBus.SuppressDispatch`**（默认接口方法，默认返回一个 no-op
  `IDisposable`，CORE-170-03(b) 根治）：调用方在 `using` 作用域内产生的领域事件（`Enqueue`/
  `PublishImmediate`）直接丢弃，不派发给订阅者；`SaveSystem.Load` 用它把逐段读档 + 失败回滚整体
  包进抑制作用域，避免回滚期间重放的领域事件被业务消费者（如 `AchievementHost`）误计数。自定义
  `IEventBus` 实现方无需新增任何代码即可编译通过。
- **`Core.Foundation.SaveSystem.SaveSections.KnownOrder` 纳入 `world.gobj_pending_loot`**：此前该
  段未登记进 `KnownOrder`，落入"自定义段"分支按 key 序数排序；现按 10 号文档"7a.
  .../spawn_state/gobj_pending_loot"既有文字顺序固定登记，只影响读档时的段处理顺序，不改变段的
  存在性或字段形状。

### 修复

- **跨图重放后卸装误删种族 aura（CORE-170-01，P2）**：装备/套装/种族共享同一 `auraDef` 时，种族
  被动此前只按 `HasAura` 判断是否需要重放，没有独立的来源账本；卸下装备会连带清除种族来源的同一份
  光环及其属性修正。改为三类来源各自维护"我持有哪个句柄"的簿记，互不代劳。
- **Progression 等级与实体等级分叉（CORE-170-02，P2）**：`ProgressionHost.AddXp`/`RestoreState`
  更新内部等级后未同步 `PlayerUnit.Level`，导致 `WorldUnitAccess.GetLevel` 与规则层查询到的等级
  不一致，等级需求装备可能因此误判 `RequirementNotMet`。改为写入时同步，Progression 是唯一权威。
- **存档失败段自身不回滚，且回滚期间产生的领域事件污染业务消费者（CORE-170-03，P2，两个已确认
  表现）**：(a) `EquipmentPersistable.Load` 及审查全仓 `IPersistable` 实现后另发现的 8 处同类
  缺陷（`AchievementHost`/`SpawnHost`/`WorldState`/`DifficultyHost`/`CurrencyPersistable`/
  `VendorStockPersistable`/`RngStreamsPersistable`/`SkillBindingPersistable`）此前均"先改变运行期
  状态、后校验数据形状"或"边解析边直接调用 live host 写方法"，坏存档会在状态已被部分或全部改动后
  才抛异常，且 `SaveSystem.Load` 此前只把"此前已成功加载"的段纳入回滚列表，抛异常的段自身不在
  其中；现全部改为"先解析校验成临时恢复计划、再一次性提交"，`SaveSystem.Load` 额外把失败段自身
  纳入回滚列表兜底。(b) `SaveSystem.Load` 逆序回滚时会调用 live host 的真实写方法（如
  `EquipmentPersistable.Load` 复用真实 Equip/Unequip 逻辑），产生的真实领域事件被业务消费者
  （`AchievementHost`）当成真实玩家操作再次计数，导致成就进度被错误推高甚至误解锁；现读档与回滚
  期间整体抑制领域事件派发，`SaveMigratedEvent`/`SaveLoadedEvent` 仍在读档完成后正常派发。
- **共享 `AnimationClip` 被空事件配置和跨 factory 状态污染（PRES-170-01，P2）**：`UnityViewFactory.
  RegisterModelClipEvents` 此前对空 `events` 配置直接跳过、不建立任何基线或隔离；首个非空配置从
  当前共享剪辑资产捕获"pristine"快照后把合并结果写回该**共享**资产；承载基线/签名/覆盖状态的三张
  表此前是 factory 实例字段。三者叠加导致同一 factory 内空配置 anim_set 会看到另一个非空配置写入
  的数据事件，新建 factory（典型触发：场景重进）会把旧 factory 写入共享资产的事件误当成美术自带
  基线保留。现改为进程级静态表缓存美术自带基线，任何非空配置都以基线为底合并出一份运行期私有
  副本，只经 `AnimatorOverrideController` 套用到具体 `ModelHandle` 实例，共享剪辑资产自始至终
  不被写入。

### 迁移说明

- **单位等级唯一权威改为 `Core.Numbers.Progression.ProgressionHost`**（CORE-170-02）：直接改写
  `PlayerUnit.Level` 字段而不经 `ProgressionHost.RegisterUnit`/`AddXp`/`RestoreState` 的具体游戏
  代码，其改动会在下一次上述三个方法被调用时被 `LevelSync` 覆盖同步；需要设置单位等级的具体游戏
  代码应统一改走 `ProgressionHost` 相应方法，不要再直接写 `PlayerUnit.Level` 字段。
- **读档与回滚期间领域事件被抑制**（CORE-170-03(b)）：依赖"读档期间正常成功加载某段会让该段产生
  的事件到达外部订阅者"这一行为的具体游戏代码（例如监听 `ItemEquipped` 来更新 UI）需要改为在
  `SaveLoadedEvent`（读档完成后正常派发）到达后按当前状态重建一次，不能再假设读档过程中会收到
  逐段变化事件；`SaveMigratedEvent`/`SaveLoadedEvent` 本身不受影响，仍会正常派发。
- **自定义 `IPersistable` 实现须遵循"先解析校验，再一次性提交"**（CORE-170-03(a)）：`IPersistable.
  Load` 契约注释已更新为要求实现方在触碰任何运行期状态之前完整校验数据形状，只有整份数据校验
  通过才提交；`SaveSystem.Load` 新增的失败段自身回滚只对遵循该约定的实现是安全的幂等 no-op，不
  遵循该约定的自定义实现在读档失败时仍可能残留部分改动的状态，建议对照框架自身 8 处修复的模式
  （见上"修复"一节）同步改造。
- **自定义 `IEventBus`/`IAuraQuery` 实现的新增成员**（CORE-170-03(b)、CORE-170-01）：
  `IEventBus.SuppressDispatch`/`IAuraQuery.TryGetInstanceRef` 均为 C#8 默认接口方法，自定义实现
  方不重写这两个成员即自动获得默认行为（分别为 no-op 抑制作用域、返回 `null`），不需要任何代码
  改动即可继续编译通过；如果自定义 `IEventBus` 实现有自己的事件派发路径且希望"读档抑制"语义生效，
  需要显式实现 `SuppressDispatch` 并让派发路径检查抑制状态。
- **版本判据说明**：本次为 MINOR（`1.7.0` → `1.8.0`）。"新增"一节列出的全部成员均为新增（新增
  类型/默认接口方法/委托/公开方法/`KnownOrder` 登记项），无删改既有公开签名；构造函数新增参数均为
  可选参数且默认值保持既有行为；`SaveSystem.Load` 失败段自身回滚、读档期间事件抑制、单位等级权威
  改为 Progression 是行为契约变更但不改变任何公开类型签名，不构成 MAJOR。

## [1.7.0] - 2026-09-08

第十一方深度审核（codex 第九轮，基线 `85f1f4f`，即本版本发布前的最新提交）10 项发现（AUD-01～05、
种族被动光环跨图、owner/day/vendor 装配扩展点、动画剪辑事件登记契约差异、工具链两条）逐条核实并
根治。逐条核实表、文档更新与边界清单处理、验收结果见
[audit-85f1f4f-20260908/followup-2026-09-08e.md](architecture/落地计划/audit-85f1f4f-20260908/followup-2026-09-08e.md)。
均属核心存档/规则/玩法层缺陷修复、引擎适配层资源合同修复、工具链健壮性加固与文档口径统一；
存档格式变更见下"迁移说明"。

### 新增

- **`Core.Foundation.SaveSystem.SaveSystem.Load` 新增按逆序回滚已成功加载段的机制**（AUD-01
  根治）：某个已注册段 `Load()` 抛异常时，对此前已成功 `Load()` 的段按逆序重新 `Load` 读档前
  快照，尽力恢复到读档前状态；最终 `LoadStatus` 仍是 `PersistableThrew`，回滚不改变这一结果；
  回滚自身失败也只记诊断，继续处理其它段。
- **6 处既有 `IPersistable` 实现补齐缺段清空覆盖面**（AUD-02 根治）：`ItemPersistable.
  InventoryPersistable`/`SkillBindingPersistable`/`VendorStockPersistable`/`DroppedLootPersistable`/
  `PlayerVitalsPersistable`/`ProgressionPersistable` 六处此前缺段（`JsonNull`）时直接返回、不清空
  既有运行期状态，现均已补齐清空逻辑；`UnitPersistable`（`CurrentMapId`/`ArchetypeId`/
  `CurrentPosition` 三段）与 `RngStreamsPersistable` 显式声明 1.6.0 新增的
  `IPersistable.KeepStateWhenSectionMissing => true` 例外并写明理由。
- **`Core.Gameplay.Economy.EconomyHost.SetStock` 新增可选参数 `timerRemaining`；新增
  `GetStockTimerRemaining`**（AUD-03 根治）：商人补货倒计时剩余时间可持久化，原地读档不再继承
  旧计时器；`world.vendor_stock` 段内 `timer` 策略物品条目形状扩展为可选对象
  `{remaining, timer_remaining}`，向后兼容纯数字旧格式。
- **`Core.Carriers.Gobj.GameObjectHost.PendingChestLootSnapshot`/`RestorePendingChestLoot`
  过时别名**（AUD-04 根治）：`[Obsolete]` 转发到 1.6.0 改名后的 `PendingLootSnapshot`/
  `RestorePendingLoot`，1.5.0 风格调用点本版本仍可编译通过（带过时警告），计划下一个 MINOR 版本
  随该窗口期结束一并移除。
- **`Core.Carriers.Unit.PlayerUnit.RaceId`/`Core.Carriers.Unit.UnitPersistable.RaceId`**（新增
  可选存档段 `player.race_id`）与 **`Core.Rules.Assembly.RulesAssembly.ReapplyRacePassiveAuras`**
  （种族被动光环跨图重放根治）：`GameplayAssembly.EnterMap` 已接入调用，此前 `World.ClearAll`
  后种族被动光环消失但种族属性修正不受影响，两者生命周期不一致的缺陷已根治。
- **`Core.Gameplay.Assembly.GameplayAssembly` 构造函数新增 `questOwnerResolver`/
  `questDayProvider`/`vendorOpenRequested` 三个可选参数**（owner/day/vendor 装配扩展点根治）：
  直接转发进内部装配的 `QuestHost`/`DialogHost`（此前恒传 `null`）；`games/_template.GameOptions`
  新增三个同名可选字段、两处引擎适配层随附的示例组合根新增三个同名可选公开属性，均已补齐透传，
  全部默认仍是 `null`，不改变未显式提供时的既有行为。
- **`Core.Foundation.EngineAdapter.IResourceLoader.ResourceKind` 新增 `AnimationClip`**（动画剪辑
  事件登记契约差异根治）：动画剪辑资源经加载器统一登记，`UnityViewFactory.RegisterModelClipEvents`
  不再整体覆盖美术自带事件，改为合并、并按 `anim_set` 的事件配置签名隔离生效范围。
- **`UnityResourceLoader.TryGetOrLoadSlotMesh`**（AUD-05 根治）：`display.equip_visual.mesh_ref`
  资源合同——预制体按槽位子对象/首个网格渲染组件提取网格，独立网格资产可直接使用；缺失/未加载时
  保留当前槽位网格不清空为不可见。
- **`toolchain/_hash.ps1`**（工具链健壮性加固）：共用哈希函数 `Get-Sha256FileHash`，
  `Get-FileHash` 可用时优先用，否则透明退化到 .NET SHA256 流式计算；`get_framework.ps1`/
  `sync_package_content.ps1`/`build.ps1` 三处哈希校验改用该函数。

### 修复

- **旧格式非空 `world.gobj_pending_loot` 读档抛异常且失败段不回滚（AUD-01，P1）**、**六处
  `IPersistable` 实现缺段不清空（AUD-02，P2）**、**商人补货计时器原地读档丢失（AUD-03，P2）**、
  **公开 API 改名无过时别名（AUD-04，P2）**、**样例 model 槽位换装把 prefab 引用当 Mesh 读取、
  槽位网格被清空（AUD-05，P2）**、**种族被动光环跨图重放丢失**、**owner/day/vendor 装配扩展点
  三处示例组合根未透传**、**动画剪辑事件登记整体覆盖美术自带事件、未按 anim_set 隔离**、
  **`toolchain` pytest 子进程按宿主默认编码解码可能崩溃**、**`get_framework.ps1` 等三处直接依赖
  `Get-FileHash` 无兜底**：10 项均为第十一方深度审核（codex 第九轮）发现，逐条根因、复现测试、
  修复位置、验收结果见上文引用的 followup 文档，不在此重复展开。
- **`toolchain/_hash.ps1` 缺少 UTF-8 BOM 导致在部分执行宿主上按系统默认代码页误读脚本源码、触发
  语法解析错误**：发布前 CI 复核发现（本机开发代码页下未复现，另一台系统默认代码页不同的宿主上
  可稳定复现），已补回 BOM（内容不变）并新增门禁测试
  `toolchain/tests/test_powershell_scripts_ansi_safe.py`，把"脚本含非 ASCII 字符必须带 BOM"
  固化为门禁校验项，见 `architecture/11_工程规范与测试.md` 第 8 节对应勘误行与
  `toolchain/README.md` 判断记录。

### 迁移说明

- **`world.gobj_pending_loot` 段读档兼容旧字段名**（AUD-01）：1.5.0 期间产生、字段名与当前版本
  不一致的旧 `pending` 记录，能按值映射的字段会被迁移，无法映射时安全丢弃单条（不影响其它条目或
  其它段），不再抛异常；`Save()` 输出格式不变，仍写 `originKey`。
- **`SaveSystem.Load` 段失败时对此前已成功加载的段按逆序尽力（best-effort）回滚**（AUD-01，行为
  契约变更）：`Load` 开始逐段读取前先对全部已注册段各取一份读档前状态快照；某段 `load()` 抛异常
  后，只对已经取得成功快照的此前成功段按逆序重新 `load(快照)`，尝试恢复到读档前状态；快照缺失、
  回滚自身再次失败、或段间存在联动时仍可能残留部分状态，不保证消除"部分加载"中间态；最终
  `LoadStatus` 枚举值不变，仍是 `PersistableThrew`。回滚不覆盖失败段自身，也不改变
  `PersistableThrew` 这一最终结果。准确合同以 `architecture/10_存档与持久化.md` 当前正文为准。
- **6 处既有 `IPersistable` 实现的缺段行为收紧**（AUD-02）：`ItemPersistable.InventoryPersistable`/
  `SkillBindingPersistable`/`VendorStockPersistable`/`DroppedLootPersistable`/
  `PlayerVitalsPersistable`/`ProgressionPersistable` 六处此前缺段时保留运行期状态不动，现改为清空
  到内容默认态；自行实现 `IPersistable` 且依赖"缺段时保留当前状态不动"这一行为的具体游戏代码，
  需要显式覆盖 1.6.0 新增的 `KeepStateWhenSectionMissing => true`（该新增接口默认方法本身不要求
  任何既有实现新增代码即可通过编译，本条只影响上述 6 处框架自身实现的默认行为）。
- **`world.vendor_stock` 段 timer 物品条目新增可选字段**（AUD-03）：`timer` 策略物品条目形状从
  纯数字变为可选对象 `{remaining, timer_remaining}`；`Load` 完全向后兼容纯数字旧格式，无需游戏侧
  改动。
- **`GameObjectHost.PendingChestLootSnapshot`/`RestorePendingChestLoot` 改名别名（AUD-04）**：本
  版本已补回旧名的 `[Obsolete]` 转发方法（行为与新名完全一致），1.5.0 风格调用点本版本仍可编译
  通过（带过时警告），计划下一个 MINOR 版本随该窗口期结束一并移除；升级到下一个 MINOR 版本前请把
  仍在用的旧名调用点改为新名。
- **`player.race_id` 新存档段**（种族被动光环跨图重放）：新增可选段，旧档缺失时清空为 `null`，
  不影响读档；调用方（游戏引导代码）需要在调用 `RulesAssembly.RegisterUnit` 传入非空 `raceId` 时
  同步写入 `PlayerUnit.RaceId`——框架不自动同步（分层边界，`RulesAssembly`/L2 不知道
  `PlayerUnit`/L3 类型存在）。
- **`display.equip_visual.mesh_ref` 资源合同首次明文**（AUD-05）：引用与 `model_ref` 同一条
  `model` 种类资源；预制体按槽位子对象/首个网格渲染组件提取，独立网格资产可直接使用；缺失/未加载
  须保留当前槽位网格，不得清空为不可见。字段数据形状（04 第 7.1.2 节字段表）不变。
- **`display.anim_set.clips[*].events` 的引擎侧登记行为契约首次明文**（动画剪辑事件登记）：必须
  与剪辑资产原有事件合并、不得整体覆盖；同一 `resource_ref` 被不同事件配置的 `display.anim_set`
  共同引用时必须按 anim_set 隔离生效范围。字段数据形状不变。
- **版本判据说明**：本次为 MINOR（`1.6.0` → `1.7.0`）。"新增"一节列出的全部成员均为新增（新增
  类型/字段/段/可选构造参数/可选公开属性/共用脚本函数），无删改既有公开签名；6 处 `IPersistable`
  实现的缺段行为收紧、AUD-01 的段失败回滚是行为契约变更但不改变任何公开类型签名，不构成 MAJOR。

## [1.6.0] - 2026-09-08

游戏侧复核 1.5.0 发现两处边界并根治：① 框架常驻壳（`FrameworkResidentHost`）命中帧同步开关此前是
私有编译期常量，是三处装配根（另两处 `GameFoundationBootstrap`/`games/_template.GameBootstrap`）
里唯一不支持"具体游戏配置开启"的一处，与包 README"命中帧同步接线步骤"一节对三处装配根一视同仁
的既有描述不符；② 命中帧同步端到端测试（`Tests/Runtime/HitFrameSyncEndToEndTests.cs`）只用一个
远大于默认超时（0.5s）的等待死线判断"VFX 是否终于入队"，未排除"命中帧链路完全断线、只是超时兜底
先一步释放，让断言恰好也通过"这一假通过可能性。均属表现层/引擎适配层缺陷修复与测试加固，无数据
表字段删改，无存档格式变更。

第十方深度审核（codex 第八轮，基线 `3224ca1`，即本版本发布前的最新提交）8 项发现（CR150-01～04、
PR150-01～03、PJ150-01）逐条核实并根治，另根治 1.5.0 遗留的一处已知局限（PR140-04 的"极短 GCD
内连续独立攻击被误合并为同一命中帧同步批次"，本版本补齐 `AttackInstanceId` 链路后消除）。逐条
核实表、上轮九项复核对照、文档处理清单见
[audit-3224ca1-20260908/followup-2026-09-08d.md](architecture/落地计划/audit-3224ca1-20260908/followup-2026-09-08d.md)。
均属核心规则/表现层/引擎适配层缺陷修复、下载脚本安全加固与文档口径统一，无数据表字段删改；
存档格式变更见下"迁移说明"。

### 新增

- **`Presentation.FeedbackBinder.Core.HitFrameSyncReleaseReason`**（枚举，`HitFrame`/`Timeout`）与
  只读诊断属性 `HitFrameSyncPolicy.LastReleaseReason`/`FeedbackBinder.LastHitFrameSyncReleaseReason`：
  命中帧同步等待队列最近一次批次释放究竟是命中帧事件真正到达，还是超时兜底，供测试与诊断直接断言
  区分两条释放路径，不再只能靠"某个副作用计数是否增加"间接判断。
- **`Core.Carriers.Gobj.GameObjectEntity.OriginKey`**（CR150-02，可空 `Id` 属性）：经
  `GameObjectFactory.Spawn` 按"地图+位置+模板"自动合成的稳定摆放位置键，跨运行期实体重建
  （地图卸载/重入分配新实体 id）保持不变；`Spawn` 新增同名可选参数，未显式传入时自动合成，既有
  调用方无需改动。
- **`Core.Foundation.SaveSystem.IPersistable.KeepStateWhenSectionMissing`**（CR150-03，默认接口
  方法，默认返回 `false`）：已注册的存档段在读取的文档里整段缺失时，`SaveSystem.Load` 默认仍会
  调用一次该段 `Load(JsonNull.Instance)`（清空既有运行期状态）；需要保留旧行为（缺段视为"不动
  当前状态"）的具体实现可显式覆盖为 `true`。纯加法，既有 `IPersistable` 实现无需改动即可编译。
- **`Core.Carriers.Gobj.GobjOptions.GatherNodeLootPolicy`**（CR150-04，`GobjLootDeliveryPolicy`
  枚举，默认 `Partial`）：独立于既有 `ChestLootPolicy` 的口味配置项，控制采集节点满包时的交付
  协议（`Reject` 整批回滚可立即重试；`Partial` 部分交付+余量记账）。
- **`Core.Rules.Common.EffectContext.AttackInstanceId`**（可空 `Id`，攻击实例 id 遗留根治）与
  `CombatDamageDealtEvent`/`CombatHealDoneEvent` 同名新字段：`CastPipeline.ExecuteEffectsOnly`
  每次调用固定分配一个全新实例 id，经 `Resolver` 转发到落地伤害/治疗事件，供 `FeedbackBinder`
  按值精确区分同一攻击者的多次独立攻击各自的命中帧同步批次，不再依赖"是否还有未释放批次"这一
  时序代理。均为新增可空字段/新增可选构造参数，默认 `null`，不改变既有调用点在缺省参数下的
  观测行为。
- **`Adapter.Unity.EngineAdapter.AnimStateFinishRelay`**（PR150-02，新增
  `StateMachineBehaviour` 子类，不属于 `IRenderer3D` 契约）：挂到具体引擎适配层动画状态机的
  目标状态上后，把动画系统同步触发的进入/退出事件转发回渲染器，作为既有"外部轮询采样"判断动画
  播放完成的并行判定路径（两者取 OR），修复自动过渡整个落在两次采样之间导致完成事件永久漏发的
  问题；未挂接时完全不影响既有行为，占位资产已随框架预置到五个内建状态。
- **`toolchain/get_framework.ps1` 下载脚本落点边界校验**（PJ150-01，安全加固）：新增锁文件
  `version` 字段格式校验（`-AllowVersionMismatch` 放行分支）与落地目录必须是 `-Target` 严格
  子目录的校验，恶意/畸形值直接 `throw` 不做任何写入/删除；`Expand-Archive` 改为逐条目手动解压
  并同样校验每个条目落点（顺带根治 zip slip）。

### 修复

- **`Adapter.Unity.Shell.FrameworkResidentHost` 命中帧同步开关改为口味配置**：私有编译期常量
  `HitFrameSyncEnabled` 改为与 `GameFoundationBootstrap` 同名同型的
  `[SerializeField] private bool _hitFrameSyncEnabled`——具体游戏可以像摆放 `GameFoundationBootstrap`
  一样在自己的场景里预先放置一个 `FrameworkResidentHost` 组件并在 Inspector 里开启该字段，
  `Ensure()` 会优先复用这个已配置好的实例（`FindFirstObjectByType` 既有查找逻辑）而不是另建一个
  默认值实例；默认仍是 `false`（`LogicDriven`），未预放置时行为与改动前完全一致。
- **命中帧同步端到端测试加固，排除超时兜底假通过**：`Tests/Runtime/HitFrameSyncEndToEndTests.cs`
  既有用例新增断言真实释放原因必须是 `HitFrame`；新增镜像反例
  `ModelAttacker_HitFrameSync_NeverFires_TimesOutWithTimeoutReason`（刻意不播放攻击动画，验证超时
  兜底确实只在命中帧从未到达时才触发、且诊断如实报告 `Timeout`）。
  `presentation/feedback_binder/tests/HitFrameSyncPolicyTests.cs`/`FeedbackBinderHitFrameSyncTests.cs`
  的等价单元测试同步补上释放原因断言，覆盖同一缺口的单元测试层面。
- **跨图重放共享光环误删（CR150-01，P2）**、**宝箱/采集节点余量按运行期实体 id 记账跨重建失联
  （CR150-02，P2）**、**旧档缺失可选段不清空当前台账（CR150-03，P2）**、**满包采集先提交冷却
  再忽略入包失败（CR150-04，P2）**、**新精灵视图创建早于绑定导致初始装备重放被过滤（PR150-01，
  P2）**、**动画完成检测依赖外部轮询采样、自动过渡落在两次采样之间会漏发完成事件（PR150-02，
  P2）**、**挂点缺失时不登记挂接意图、挂点补上后无法重放（PR150-03，P2）**、**下载脚本锁文件
  `version` 字段未经校验即拼入落地路径、可越界写删（PJ150-01，P0 安全）**：8 项均为第十方深度
  审核（codex 第八轮）发现，逐条根因、复现测试、修复位置、验收结果见上文引用的 followup 文档，
  不在此重复展开。

### 迁移说明

- **旧存档缺失可选段时默认清空该段运行期状态**（CR150-03，行为收紧）：`SaveSystem.Load` 此前对
  "已注册但当次读取的文档里整段缺失"的段直接跳过、不调用 `Load`；本版本改为默认仍调用一次
  `Load(JsonNull.Instance)`。已审计全部既有 `IPersistable` 实现（16 个），均已在 `Load` 开头
  显式处理 `JsonNull`（清空重置或无副作用 no-op），当前无一需要调整；自行实现 `IPersistable` 且
  依赖"旧档缺段时保留当前运行期状态不动"这一（未在架构文档承诺过的）旧行为的具体游戏代码，需要
  显式覆盖新增的默认接口方法 `KeepStateWhenSectionMissing => true`。该新增成员是默认接口实现，
  不要求任何既有实现新增代码即可通过编译。
- **`world.gobj_pending_loot` 段 JSON 字段名与值语义变更**（CR150-02，非公开承诺字段的一次性
  调整）：数组元素字段名 `gobjInstanceId`（运行期实体 id）改为 `originKey`（稳定摆放位置键）。
  本段是 1.5.0 新增的可选附加段，此前未在 `architecture/10_存档与持久化.md` 正式登记、未对外
  承诺过字段级兼容；产生于 1.5.0 期间、字段名与当前版本不一致的旧 `pending` 记录，读取时的实际
  处理口径（能按值映射的字段是否迁移、无法映射时如何安全丢弃、单段失败是否回滚）以
  `architecture/10_存档与持久化.md` 当前记载为准，不在本条目重复展开、也不承诺与早期草稿描述
  一致；建议对极少数处于这一窗口期、且确实存在未交付宝箱/采集节点掉落余量的存档，升级前先进入
  相关地图把余量交互清空一次，规避任何处理口径下都可能出现的"旧余量丢失"结果。
- **自定义 `IRenderer3D` 实现建议接入动画状态机的状态退出回调**（PR150-02，非强制）：本版本
  新增的 `AnimStateFinishRelay` 只是既有"外部轮询采样"判断动画完成的一个并行判定路径，不替代
  也不要求废弃采样路径；具体引擎适配层若已经能通过采样正确判断完成（未撞见"自动过渡整个落在两次
  采样之间"这一边界），无需任何改动。若自定义实现同样依赖外部轮询采样判断完成，建议参考本版本
  接入方式补一条由动画系统事件驱动的完成检测路径，避免同一类漏发问题。
- **版本判据说明**：本次为 MINOR（`1.5.0` → `1.6.0`）。"新增"一节列出的全部成员均为新增
  类型/新增可选构造参数/新增可空字段/新增默认接口方法，均不改变既有调用点在缺省参数下的观测
  行为，不删除、不改名任何已有公开签名，不破坏既有调用方编译；`world.gobj_pending_loot` 段的
  字段改名属于上述"非公开承诺字段"的例外说明，不计入 MAJOR 判据（该段本身从未进入过
  `SaveSections.KnownOrder`/架构文档正式登记）。

## [1.5.0] - 2026-09-08

第九方深度审核（codex 第七轮，基线 `c86bfa9`，即 1.4.0 自身）9 项发现（CR140-01～03、
PR140-01～04、PJ140-01～02）逐条核实并根治，另补齐审计未覆盖的两处遗留（宝箱 Partial 余量存读档
持久化、新 View 初始装备外观重放）。逐条核实表、旧 17 项复核对照、文档漂移处理见
[audit-c86bfa9-20260908/followup-2026-09-08c.md](architecture/落地计划/audit-c86bfa9-20260908/followup-2026-09-08c.md)。
均属核心规则/表现层/引擎适配层缺陷修复与能力补齐，无数据表字段删改，无存档格式不兼容变更（新增
`world.gobj_pending_loot` 段是可选附加段；缺段时的实际读取/清空语义以
`architecture/10_存档与持久化.md` 当前记载为准，不在本条目重复展开）。

### 新增

- **`Core.Carriers.Gobj.GobjLootDeliveryPolicy`**（枚举，`Reject`/`Partial`，CR140-01）：
  `GobjOptions.ChestLootPolicy`（默认 `Partial`）决定 `chest` 一次性开箱的交付协议——`Reject`
  经 `IBatchableInventoryHost` 事务整批交付，任一堆放不下即整体回滚，不标记 `open_state`；
  `Partial` 逐堆按实际落地量交付，未交付部分记入进程内台账供下次交互补发。
- **`Core.Carriers.Gobj.GameObjectHost.PendingChestLootSnapshot`/`RestorePendingChestLoot`**
  + 新增 `GobjPendingLootPersistable`（CR140-01 存读档收口）：`Partial` 策略下未交付的宝箱余量
  补齐可选存档段 `world.gobj_pending_loot`（字段名 `pending_loot`）；缺段时的实际读取/清空语义
  以 `architecture/10_存档与持久化.md` 当前记载为准；`GameplayAssembly.RegisterPersistables`
  已注册。
- **`Core.Carriers.Item.EquipmentHost.ReapplyGrants(Id unitId)`**（CR140-02）：按当前 `_equipped`
  记录重放每件装备的 `grants.auras` 并重新核实/施加套装门槛加成，幂等经 `IAuraQuery.HasAura`
  核实；`GameplayAssembly.EnterMap`（post-load 统一钩子）已接入调用。
- **`Core.Carriers.Common.GobjInteractedEvent.ResolvedTeleportTarget`**（`(Id MapId, Vec2
  Position)?`，CR140-03）：跨图传送在 `GameObjectHost.DoTeleport` 判定确实需要跨地图时，随原始
  `TeleportTargetRef` 一并携带已解析结果；`GameplayAssembly` 新增 `ApplyResolvedTeleport` 只负责
  落地，`gobj.interacted` 订阅不再反查模板独立重新解析。
- **`Adapter.Unity.EngineAdapter.UnityResourceLoader.TryLoadModelSync`**（ADR-0017 决策 1 收紧）：
  统一的模型资源缓存优先/未命中同步解析入口，`UnityRenderer3D` 不再直接调用引擎资源读取接口，
  `FinishModelLoad` 异步路径复用同一方法，渲染器只消费已加载资源、不再自行决定加载责任边界。
- **`Adapter.Unity.Presentation.UnitySpriteView` 的 `equipVisualByItemInstanceId` 构造参数**
  （PR140 文档漂移根治）：sprite 外形路线补齐装备外观入口，`UnityViewFactory` sprite 分支已接入
  同一份表；新增示例数据 `data/_sample/display/display.equip_visual.json` 一行。
- **`Presentation.Render.EquipmentSnapshotResolver`/`EquippedItemRef`（窄契约委托）+
  `EquipmentVisualSource.ReplayEquippedForUnit`**（第 0 步补齐，新 View 初始装备外观重放）：按
  单位 id 查询当前全部已装备物品（生产装配根通常包一层
  `EquipmentHost.GetAllEquippedInstances`），供跨图新 View / 存档恢复后创建的 View 在 `CreateView`
  时合成一次 `ItemEquippedEvent` 调用 `OnEvent`，不再要求先等到一次真正的装备/卸装事件才能看见
  已有装备的外观；三处生产装配根（`GameFoundationBootstrap`/`FrameworkResidentHost`/
  `games/_template.GameBootstrap`）已接线。
- **兼容层（源码兼容，`[Obsolete]`，恢复 PJ140-01）**：`Presentation.Render.ICharacterRig.
  HitFrameReached`（默认接口实现，转发到 `IHitFrameEmitter`，探测不到时静默 no-op）；
  `Presentation.Common.ViewKind.GameObject`（与 `Gobj` 数值相同的过时别名）。两者均不要求任何
  既有 `ICharacterRig` 实现/`ViewKind` 消费方改动代码即可继续编译。
- **`HitFrameSyncPolicy.WaitForHitFrame(Id, object batchToken, Action)` 重载 + `event
  Action<Id>? BatchReleased`**（PR140-04）：同一 `batchToken` 的多条等待项视为一个不可拆分批次，
  命中帧或超时都原子释放整批；`FeedbackBinder` 按攻击者维护当前批次 token。

### 修复

9 条逐条判断记录、复现测试、修复位置、验收测试见
[audit-c86bfa9-20260908/followup-2026-09-08c.md](architecture/落地计划/audit-c86bfa9-20260908/followup-2026-09-08c.md)
核实表，概要：
- **核心侧**（CR140-01～03）：宝箱一次性开箱不再在发奖前就永久标记已开、真实满包时不再吞掉/重复
  发放奖励；跨图 `World.ClearAll` 清场后按装备台账重建光环，不再出现"持久装备集合与运行期光环
  脱节"；跨图传送携带已解析结果，不再被内置默认 resolver 二次解析覆盖自定义结果。
- **表现/引擎侧**（PR140-01～04）：Blob 影子固定绕 X 轴转 90° 的旧 XZ 地面约定遗留写法改为随
  当前 XY 地面平面动态朝向相机；异步模型替换恢复 socket 子实例与投影阴影状态，不再销毁挂点子模型
  /阴影状态回退到默认值；Animator 自动过渡在两次检测帧之间完成时不再永久漏发完成事件；同一次
  攻击命中多个目标的命中帧反馈按批次原子释放，不再按 FIFO 逐条错帧/超时。
- **契约兼容性**（PJ140-01）：恢复 `ICharacterRig.HitFrameReached`/`ViewKind.GameObject` 两处
  1.4.0 内直接改名/删除造成的 1.3 消费方源码兼容性破坏。
- **交付流程**（PJ140-02）：Release 附件检测新增 lock 段 `git_commit` 一致性校验，zip 缺失但其余
  必需附件仍存在时直接阻断（不再全量重建后只上传缺失文件，消除"新 zip + 旧附件"混批且无法证明
  同源的窗口）。

### 迁移说明

- **1.3 消费方源码现可直接对 1.4.0 之后的 DLL 编译，无需改动**（PJ140-01 兼容层恢复）：见上
  "新增"一节两处 `[Obsolete]` 兼容成员；两处均计划在下一个 MAJOR 发布中随旧签名一并移除，
  过时成员按 [11_工程规范与测试.md 第 7 节](architecture/11_工程规范与测试.md) 判据至少保留一个
  MINOR 发布周期，本版本是该周期的第一个 MINOR。
- **自定义 `IRenderer3D` 实现的完成事件语义收紧**（PR140-03 收口）：非循环剪辑自然播放完成时必须
  恰好发出一次完成事件（`anim_event.finished`），即便动画状态机在两次 `Tick` 检测帧之间已经自动
  过渡离开目标状态——自实现方若只在"当前状态精确等于目标状态"时才判定完成，会漏发这一窗口内的
  完成事件，导致瞬态状态锁永久残留；`UnityRenderer3D.IsAnimatorStateFinished` 的
  `everEnteredTarget` 记账机制可作为参考实现。
- **gobj 存档新增可选段 `world.gobj_pending_loot`**：字段名 `pending_loot`，数组元素
  `{gobjInstanceId, items:[{templateId, count}]}`；只有使用 `GobjLootDeliveryPolicy.Partial`
  策略且确实产生过未交付余量时才有内容，未注册该段的既有装配根/未升级读取该段的旧存档均不受
  影响（读到 `JsonNull` 视为空表）。
- **版本判据说明**：本次为 MINOR（`1.4.0` → `1.5.0`），"新增"一节列出的全部成员均为新增
  类型/新增可选构造参数/新增枚举/恢复的过时别名或默认实现，不删除、不改名任何已有公开签名，
  不破坏既有调用方编译。

## [1.4.0] - 2026-09-08

两轮修复合并发布：① 游戏侧复核 1.3.0 发现并根治三项问题——model 型动画状态机永久卡死、瞬态状态
同状态重入不重播、框架常驻壳（`FrameworkResidentHost`）未接通 model 型外形（提交 `9f5695d`/
`360ff5f`）。② 第八方深度审核（codex 第六轮，基线 `5c444f1`）17 项发现（PR130-01～08、
PJ130-01～04、CR130-01～05）逐条核实并在当时验证范围内根治，逐条核实表、旧 13 项复核对照、
文档漂移与链接处理见
[audit-5c444f1-20260908/followup-2026-09-08b.md](architecture/落地计划/audit-5c444f1-20260908/followup-2026-09-08b.md)。
均属表现层/引擎适配层/核心规则/交付工具链缺陷修复与能力补齐，无数据表字段删改，无存档格式不兼容
变更（CR130-02 只改变运行期判定逻辑，`player.known_skills` 段 JSON 形状不变）。**收窄说明**：
第七轮外部审核（codex，基线 `c86bfa9`，即本版本自身）复核这 17 项时发现其中两项当时的根治
范围未覆盖全部子路径——CR130-01（购买/拾取事务化）未覆盖一次性宝箱直发路径（新记为
CR140-01，P1）、CR130-05（跨图传送 resolver）只修了同图/null 分支、跨图分支仍被内置 resolver
覆盖（新记为 CR140-03）；这两项不因"17 项全部根治"这句话被视为已闭合，实际状态与验收标准见
[audit-c86bfa9-20260908/AUDIT_REPORT.md](architecture/落地计划/audit-c86bfa9-20260908/AUDIT_REPORT.md)。

### 新增

- **`Presentation.Render.IHitFrameEmitter`**（可选接口，PJ130-04）：承载 `HitFrameReached: Event<Id>`
  命中帧到达事件，`SpriteCharacterRig`/`ModelCharacterRig` 均实现；`ICharacterRig` 本身不再强制要求
  该成员（迁移说明见下）。
- **`IBatchableInventoryHost` 事务覆盖购买/拾取**（CR130-01）：`EconomyHost.Buy`/`Sell`、
  `LootHost.PickUpReject` 涉及"先落地再补偿"的路径改用既有 `IBatchableInventoryHost.BeginBatch()`
  事务，与 `RewardDispatcher.GrantItems`/`QuestHost.TurnIn` 此前已用的惯例统一，失败时连已缓存的
  `item.added`/`item.removed` 事件一并回滚。
- **`SkillHost.LearnSkill` 永久来源参数与 `ForgetAllPermanentGrants`**（CR130-02）：新增
  `LearnSkill(Id unitId, Id skillId, Id sourceId, bool permanent)`；来源分类从"是否等于哨兵"改为
  逐来源记录是否永久；新增 `ForgetAllPermanentGrants(unitId, skillId)` 一次性撤销某技能全部永久
  来源，供 C09 存档替换语义正确覆盖奖励来源技能。
- **`Core.Rules.Skill.ProcHost.RescaleAll(double factor)`**（CR130-03）：混合时间模式切换时同步
  折算 Proc 的 ICD 存量，与 `CooldownTracker`/`AuraHost`/`CastPipeline` 各自的 `RescaleAll` 判断
  记录同款。
- **`Core.Carriers.Common.GobjInteractedEvent.TeleportTargetRef`**（`Id?`，CR130-05）：`on_use` 不
  可分发时 `GameObjectHost.Interact` 算出的传送目标引用随事件一并携带，`GameplayAssembly` 改为直接
  消费该引用，不再反查模板独立重新解析。
- **`Presentation.Assembly.PresentationAssembly` 的 `renderer3D` 参数完整接线**（PR130-06）：该
  构造参数早已声明为可选，三处生产装配根（`GameFoundationBootstrap`/`FrameworkResidentHost`/
  `games/_template.GameBootstrap`）此前只转发给 `UnityViewFactory`、未转发给 `PresentationAssembly`
  的接线遗漏本次补齐。
- **`Presentation.Render.EquipmentVisualSource`**（PR130-07）：新增默认的"实体 → 装备外观"来源
  实现，订阅 `item.added`/`item.equipped`/`item.unequipped` 维护"物品实例 id → 装备外观引用"活
  字典，按 `display.equip_visual.item_id` 索引；`UnityViewFactory` 新增
  `equipVisualByItemInstanceId` 构造参数，三处装配根已接线；新增示例数据
  `data/_sample/display/display.equip_visual.json`。
- **`Core.Foundation.EngineAdapter.UnityRenderer3D.Tick()`/`AnimStateMachine.StateRetriggered`
  事件/`anim_event.finished` 完成事件**（游戏侧复核收口，见上"② 概述"提交 `9f5695d`/`360ff5f`）：
  model 路线动画状态机的完成回调与同状态重入重播通道，详见下"修复"与"迁移说明"。
- **dist 纳入模型占位资源与生成器**（PJ130-02）：`build.ps1` 新增"5.055"节，把
  `adapters/unity/Assets/Resources/GameFoundation/{models,anim_clips}` 与
  `Assets/Editor/GeneratePlaceholderModelAssets.cs` 补进 dist 内适配层包副本；`check.ps1` 包清单
  一致性步骤新增对应必需路径核对。
- **私服停止身份核验**（PJ130-03）：`toolchain/registry/start_registry.ps1` 新增
  `Test-VerdaccioProcessIdentity`（可执行文件名/`CommandLine` 锚点/启动时间三项核验），`-Stop`
  两条路径（PID 文件/端口兜底）均先核验身份，不通过即拒绝停止并非零退出；`-Status` 同步显示核验
  结果。
- **文档相对链接检查测试**（`toolchain/tests/test_markdown_relative_links.py`，交付侧文档漂移
  处理附带产出）：枚举被跟踪 `*.md` 的相对链接并做文件存在性校验，随 `pytest toolchain/tests`
  一并执行。

### 修复

17 条逐条判断记录、复现测试、修复位置、验收测试见
[audit-5c444f1-20260908/followup-2026-09-08b.md](architecture/落地计划/audit-5c444f1-20260908/followup-2026-09-08b.md)
核实表，概要：
- **表现/Unity 侧**（PR130-01/03/04/05/06/07/08）：model 放置与相机投影现共用同一 2.5D 平面、
  影子不随高度抬离地面；武器/技能覆盖剪辑按需登记后再播放，不再因未预注册而无法播放；同一命中帧
  的多条反馈规则原子合批释放；model 缺资源路径落地占位并异步替换，不再直接抛异常；三处生产装配根
  一致转发 `renderer3D` 给 `PresentationAssembly`；model 换装卸载按实例反查精确清理槽位/挂点，不
  再残留挂件。
- **交付与工具链**（PJ130-01/02/03）：Release 缺附件时优先从已验证 commit 一致的旧 zip 原地补齐
  lock/tgz，不再整体重建混入新构建批次；dist 补齐模型占位资源与生成器；registry 停止前核验进程
  身份，不再误杀端口复用的无关进程。
- **契约版本语义**（PJ130-04）：`ICharacterRig.HitFrameReached` 改由可选接口 `IHitFrameEmitter`
  承载，恢复 1.3.0 MINOR 发布号与"不新增强制成员"语义一致。
- **核心规则/玩法**（CR130-01～05）：购买/拾取失败的补偿路径原子化，已派发事件一并回滚；一次性
  奖励技能按来源正确分类为永久/临时并可正确遗忘；混合时间模式折算补齐充能第二窗口、Proc ICD、
  施法学派锁三处遗漏；引导结束后的时间余量不再多结算一跳周期效果；自定义传送 resolver 结果不再
  被内置默认传送或独立重新解析覆盖。

### 迁移说明

- **`ICharacterRig.HitFrameReached` 不再是接口本身的强制成员**（PJ130-04，恢复 1.3.0 引入前的
  兼容性）：任何自定义 `ICharacterRig` 实现无需再实现该事件即可编译；需要命中帧同步的代码改为
  `(rig as IHitFrameEmitter)?.HitFrameReached`，或改持有具体类型（`SpriteCharacterRig`/
  `ModelCharacterRig` 仍直接声明该事件）。`CharacterRigHitFrameSource.RegisterRig` 对不支持
  `IHitFrameEmitter` 的 rig 仍登记成功（`HasRig` 为 true），只是不转发任何事件。
- **`IRenderer3D` 实现新增契约义务**（02 第 1.12 节勘误，游戏侧复核收口引入）：`PlayAnim` 播放的
  非循环剪辑（`loop=false`）自然播放完成时，必须经既有 `OnAnimEvent` 通道额外发出一次约定的
  "播放完成"事件（循环剪辑不发）；具体事件 id 由消费方（表现层装配代码）约定，本仓库 Unity 实现
  固定用 `anim_event.finished`（与 `Presentation.Render.ModelCharacterRig.AnimFinishedEventId`
  逐字相等）。自行实现 `IRenderer3D`（迁移到其它引擎，见 02 第 4 节迁移步骤）的具体游戏，必须在
  自己的实现里补齐这条完成事件，否则 model 型外形的 Attack/Hit/Cast 等瞬态动画状态会永久卡死，
  无法回落。`adapters/conformance/Runtime/Renderer3DScenarios.cs`/`adapters/stub/StubRenderer3D.cs`
  新增对应契约一致性场景与测试专用完成钩子（`CompleteAnimForTest`），供自实现方按同一套场景验证。
- **自定义库存实现建议实现 `IBatchableInventoryHost`**（CR130-01）：未实现该接口的库存宿主，
  `EconomyHost.Buy`/`Sell`、`LootHost.PickUpReject` 会退回历史行为（逐项补偿，不保证已派发事件的
  原子回滚）；建议按 `InventoryHost` 既有实现补齐，获得失败路径的完整原子性。
- **`SkillHost.LearnSkill` 三参调用语义**（CR130-02）：不带 `permanent` 的三参重载
  `LearnSkill(Id,Id,Id)` 现在等价于 `permanent: true`（对绝大多数既有调用方是无感知的行为收紧）；
  生产代码中唯一"曾经依赖三参重载被当作临时来源"的调用方（`Core.Carriers.Assembly.CarriersAssembly`
  装备 `SkillGranter`）已同步改为显式四参调用（`permanent: false`）；自行组装 `SkillHost` 的调用方
  若依赖三参重载表示临时来源，需要同步改为显式四参调用。
- **版本判据说明**：本次为 MINOR（`1.3.0` → `1.4.0`），新增成员（`IHitFrameEmitter`、
  `ProcHost.RescaleAll`、`GobjInteractedEvent.TeleportTargetRef`、`SkillHost` 新重载/新方法、
  `EquipmentVisualSource`、`PresentationAssembly`/`UnityViewFactory` 新构造参数）均为可选/附加成员，
  不破坏既有调用方编译。**收窄**：上一句"不破坏既有调用方编译"仅覆盖上述新增成员，不覆盖下面
  "已知源码兼容性破坏"一条列出的两处改名/删除——那两处已经过独立编译验证证实会破坏未迁移的
  1.3 消费方源码，不属于本条"新增成员均可选"的范围，不应被本条带过。
- **已知源码兼容性破坏与本版恢复的兼容层**（PJ140-01，第七轮外部审核，基线 `c86bfa9` 发现）：
  1.3.0 引入的强制成员 `ICharacterRig.HitFrameReached` 在本版本内被直接从接口移除（迁移到新增的
  可选接口 `IHitFrameEmitter`），以及 `Presentation.Common.Contracts.ViewKind` 的枚举成员
  `GameObject` 被直接改名为 `Gobj`（消除与具体引擎核心类型同名造成的技术名模糊误报，见提交
  `88a0778`）——这两处都不是"新增可选/附加成员"，而是对已发布公开签名的改名/删除，用独立的 1.3
  消费方工程针对真实 1.4.0 DLL 编译可复现失败：`ViewKind.GameObject` 报 `CS0117`、
  `ICharacterRig.HitFrameReached` 报 `CS1061`（复现日志见
  [audit-c86bfa9-20260908/AUDIT_REPORT.md PJ140-01](architecture/落地计划/audit-c86bfa9-20260908/AUDIT_REPORT.md)）。
  按 [11_工程规范与测试.md 第 7 节](architecture/11_工程规范与测试.md) 的判据，公开枚举成员改名/
  删除、接口成员删除同属不兼容变更，本应按 MAJOR 处理或至少保留过时别名/默认实现一个 MINOR
  周期，而不是在同一个 MINOR 发布内直接改名/删除且不留兼容路径。为把当前状态收回到"MINOR 内不
  破坏既有编译"的既有承诺内，本版本恢复了对应的兼容层：`ViewKind` 补回过时别名
  `GameObject`（与 `Gobj` 同值，标记为已过时，建议新代码改用 `Gobj`）；`ICharacterRig` 恢复
  `HitFrameReached` 作为带默认实现的成员（标记为已过时，建议改用 `IHitFrameEmitter`），未覆写该
  默认实现的既有实现类型无需改动即可继续编译。两处兼容层均计划在下一个 MAJOR 发布中随旧签名一并
  移除；具体实现类型与提交见后续修订版本记录。
- 占位模型资产新增 `hit` 状态与 `anim_clips/hit.anim`（`adapters/unity/Assets/Editor/GeneratePlaceholderModelAssets.cs`
  可重复运行生成）；`data/_sample/display/display.anim_set.json` 的
  `display.anim_set.placeholder_biped` 补 `hit` 剪辑声明，供"受击后继续攻击"端到端验收使用。

## [1.3.0] - 2026-09-08

W6 表现能力补齐：补齐"能力边界与未默认接入能力索引"表中长期标记"未接入"的三项表现能力——装备
外观（`model` 型）、武器动画（`auto_attack_anim`/`cast_anim_override`）、关键帧反馈
（`anim_keyframe_driven`）——的引擎无关部分（`presentation/**`）与引擎适配层真实实现
（`adapters/unity/**`），并收口三项能力共同依赖的命中帧同步链路最后一段接线缺口，使其在生产装配根
"默认可接线（开关）"。决策见 [ADR-0017](architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md)
（模型型外形默认路线补齐与命中帧同步）。另附工具链两项修正。

### 新增能力

- **装备外观（`model` 型外形）**：`ModelCharacterRig`（`presentation/render/core/ModelCharacterRig.cs`）
  从占位收口为真实实现——`ApplyEquipVisual`/`ClearSlot`/`ClearSocket` 驱动装备外观替换、
  `SyncPlacement` 落实八原语基准姿态；`Adapter.Unity.EngineAdapter.UnityRenderer3D` 提供
  `IRenderer3D` 真实实现（模型实例化/骨骼动画播放/动画事件/挂点槽位/材质参数/阴影），资源路径约定
  `Resources/GameFoundation/models/<资源引用 id 去类别前缀>`；占位模型资产
  `Assets/Resources/GameFoundation/models/placeholder_biped.*` 与可重复运行的生成脚本
  `Assets/Editor/GeneratePlaceholderModelAssets.cs` 一并提供。
- **武器动画（`auto_attack_anim`/`cast_anim_override`）**：新增 `IWeaponStyleSource`/
  `EquipmentWeaponStyleSource`（`presentation/vfx_sfx/**`，按实体查其当前装备的武器风格引用，复用
  既有 `item.template.display_ref → display.map.weapon_style_ref` 关联链路，未新增任何数据表字段）；
  `Adapter.Unity.Presentation.AnimClipResolver` 接入该来源与新增的
  `AnimStateMachine.StateChangedWithSkill` 事件，Attack 状态用 `AutoAttackAnim`、Cast 状态按触发
  技能 id 命中 `CastAnimOverride` 时用覆盖剪辑，sprite/model 两条路线共用同一份决策逻辑。
- **关键帧反馈（`anim_keyframe_driven`）**：命中帧统一为 `ICharacterRig.HitFrameReached`（sprite
  经序列帧关键帧、model 经 `IRenderer3D.OnAnimEvent` 命中固定事件 id
  `ModelCharacterRig.HitFrameEventId`）；`feedback.binding` 新增 `sync: "hit_frame"` 字段（默认对
  `combat.damage_dealt` 开启）；`presentation/feedback_binder` 新增 `IHitFrameSource`/
  `CharacterRigHitFrameSource`/`HitFrameSyncPolicy`（等待队列，0.5 秒超时兜底，逻辑结算不受影响，
  只调节呈现时机）。**本次收口**：`PresentationAssembly`/`UnityViewFactory` 均补齐接线参数（见下
  "接口变更"），三处引擎侧装配根默认可用一个口味配置项一键切换，不再需要游戏层手工绕过
  `PresentationAssembly` 自行接线。

### 接口变更

- **新增枚举值** `Core.Foundation.EngineAdapter.ResourceKind.Model`。
- **新增契约成员** `Presentation.Render.ICharacterRig.HitFrameReached`（`event Action<Id>?`，已从
  `SpriteCharacterRig` 专属成员提升进接口本身）。**迁移（破坏性，需自定义实现方补齐）**：任何自定义
  `ICharacterRig` 实现（`SpriteCharacterRig`/`ModelCharacterRig` 两个框架内置实现已补齐）必须新增
  实现本事件成员，否则无法通过编译；不打算支持命中帧同步的实现可以让该事件永不触发（等价于
  `LogicDriven` 策略下的既有行为）。
- **新增事件** `Presentation.Render.AnimStateMachine.StateChangedWithSkill`（携带触发技能 id，与既有
  `StateChanged` 三元组事件并存、`StateChanged` 签名不变）。
- **新增数据字段** `feedback.binding.sync`（可选枚举，当前仅 `"hit_frame"`；未提供时按 `event` 是否
  为 `combat.damage_dealt` 决定默认值，见 `FeedbackRule.Sync` 判断记录）。
- **新增可选构造参数**：
  - `Presentation.FeedbackBinder.Core.FeedbackBinder` 新增 `IHitFrameSource? hitFrameSource = null`；
  - `Presentation.Assembly.PresentationAssembly` 新增 `IHitFrameSource? hitFrameSource = null`（原样
    转发给内部 `FeedbackBinderCore` 同名参数——**本次收口新增**，此前该类型完全没有暴露这个参数）；
  - `Adapter.Unity.Presentation.UnityViewFactory` 新增 `IRenderer3D? renderer3D`、
    `IHitFrameSource? hitFrameSource`、`IWeaponStyleSource? weaponStyleSource`、
    `RenderOptions? renderOptions`（最后一项**本次收口新增**——此前即便别处已把
    `RenderOptions.HitFrameSync` 切到 `AnimKeyframeDriven`，`UnityViewFactory` 构造
    `UnitySpriteView`/`UnityModelView` 时仍从不传这份 `RenderOptions`，rig 构造期实际拿到的永远是
    默认 `LogicDriven`，是比"`PresentationAssembly` 未暴露 `hitFrameSource`"更深一层、本次才发现的
    接线缺口，一并收口）。
  以上均为可选参数，不传时行为与改动前完全一致，不影响既有调用方编译或运行期行为。
- **新增字段** `Presentation.Render.RenderOptions.HitFrameSync`（`HitFrameSyncStrategy`，默认
  `LogicDriven`）、`Presentation.FeedbackBinder.Contracts.FeedbackOptions.HitFrameSync`/
  `HitFrameSyncTimeoutSeconds`（默认 `LogicDriven`/0.5 秒）——两者是同一个口味配置项在渲染侧/反馈
  绑定侧的两个落点，装配层需要保持一致（见下"游戏侧接入步骤"第 6 条）。
- **诊断/测试专用新增成员（非契约）**：`Adapter.Unity.EngineAdapter.UnityRenderer2D.EmitParticleCallCount`
  （累计 `EmitParticle` 调用次数，同 `UnityAudio.PlaySfxCallCount` 一类既有诊断计数惯例）。

### 游戏侧接入步骤（新游戏若要使用以上三项能力）

1. **model 型外形**：`display.map` 填一行 `kind: "model"`，`model_ref` 指向三维模型资源引用 id，
   `anim_set_ref` 指向 `display.anim_set` 表一行（`clips[*]` 声明动画剪辑，`events[*]` 声明关键帧，
   固定名字 `"hit_frame"` 是框架约定的命中帧标记，见 `AnimSetEventsShapeRule` 校验）；`sockets`/
   `slots` 数组声明挂点/换装槽位 id。
2. **模型预制体放置约定路径**：`Resources/GameFoundation/models/<资源引用 id 去类别前缀>`（模型）、
   `Resources/GameFoundation/anim_clips/<资源引用 id 去类别前缀>`（动画剪辑）；挂点/槽位对象命名须
   与 `display.map.sockets`/`slots` 逐字一致（含域前缀）。
3. **武器风格**：`display.weapon_style` 表填一行（`auto_attack_anim`/`cast_anim_override`），经
   `item.template.display_ref → display.map.logical_id → weapon_style_ref` 关联；装配根构造一个
   `Presentation.VfxSfx.Core.EquipmentWeaponStyleSource`（需提供
   `MainHandWeaponTemplateResolver` 委托）传给 `UnityViewFactory` 的 `weaponStyleSource` 参数。
4. **主手槽位 id**：09/04 未定义全局槽位登记表，本次在三处装配根（灰盒 `GameFoundationBootstrap`
   的 `_mainHandSlotId`、模板 `GameOptions.MainHandSlotId`）各暴露一个口味配置项承载，具体游戏按
   自己的装备槽位登记表填入。
5. **命中帧同步开关**：装配根构造一个 `CharacterRigHitFrameSource`，同一个实例分别传给
   `UnityViewFactory` 的 `hitFrameSource` 参数与 `PresentationAssembly` 的 `hitFrameSource` 参数；
   构造**同一个** `RenderOptions` 实例（`HitFrameSync = AnimKeyframeDriven`）分别传给
   `UnityViewFactory` 的 `renderOptions` 参数与 `PresentationAssemblyOptions.RenderOptions`，并把
   `PresentationAssemblyOptions.FeedbackOptions.HitFrameSync` 同步切到 `AnimKeyframeDriven`——五处
   必须两两取同一实例/同一策略值，任一处遗漏或不一致都会让命中帧同步整体或部分失效（完整步骤见
   `adapters/unity` 包 README"命中帧同步接线步骤"一节）。三处框架自带装配根
   （`GameFoundationBootstrap`/`Adapter.Unity.Shell.FrameworkResidentHost`/
   `games/_template.GameBootstrap`）均已按上述步骤接线，各暴露一个布尔口味配置项
   （`_hitFrameSyncEnabled`/`GameOptions.HitFrameSyncEnabled`）一键切换，默认 `false`
   （`LogicDriven`，行为与本次收口前完全一致）。

### 工具链修正

- `toolchain/registry/start_registry.ps1`：私服停止逻辑改为以端口监听进程为准（不再依赖可能已经
  漂移的 pid 文件/进程句柄），新增 `-Status` 查询当前私服运行状态。
- `toolchain/consumer_smoke.ps1`：消费方演练在启动 Unity 前先等待同名残留进程退出，避免与新启动的
  实例互相冲突；冒烟结果落盘为 `consumer_smoke.log`。

### 迁移说明

- **自定义 `ICharacterRig` 实现**必须新增实现 `HitFrameReached` 事件成员（破坏性接口变更，详见上
  "接口变更"）；不需要命中帧同步的实现可以让该事件永不触发。
- 使用框架自带三处装配根（`GameFoundationBootstrap`/`FrameworkResidentHost`/`games/_template.
  GameBootstrap`）的游戏无需任何改动即可编译运行，命中帧同步默认关闭（`LogicDriven`），行为与
  1.2.0 完全一致；需要启用时按上面"游戏侧接入步骤"第 5 条打开对应口味配置项即可。
- 自行组装 `PresentationAssembly`/`UnityViewFactory` 的游戏（未使用框架自带装配根）：新增参数均为
  可选、默认 `null`，不传不影响现有行为，可按需选择性升级到新能力。

## [1.2.0] - 2026-09-08

第七方深度审核（codex 第五轮，基线 `1.1.0`/`5e779c6`，报告见
`architecture/落地计划/audit-5e779c6-20260907/`）13 项主发现（GP26-01～03、FR-01～05、U01～05）+
2 项验证阶段追加复现（同图读档孤儿实体、`skill.def.charges`/`cost[]` 嵌套形状校验缺口）+ WA 报告
记录的 3 条相邻缺口（施放当下按当前时间模式折算冷却/充能/光环 duration、周期累加器同步换算、
`grants.auras` 重复引用数据提醒）+ 本轮补齐的 2 条相邻缺口（`QuestHost.TurnIn` 回滚事务化、
`CastPipeline` 施放当下折算 `cast_time`/`channel_time`/`modify_cooldown` delta）全部核实并根治，
20 条核实表、判断记录、文档漂移处理逐条见
[audit-5e779c6-20260907/followup-2026-09-08.md](architecture/落地计划/audit-5e779c6-20260907/followup-2026-09-08.md)。
本条目记录变更内容与迁移说明。

### 修复（概要，逐条详见 followup 文档）

- 玩法：奖励发放失败回滚现在连已入队的 `item.added`/`item.removed` 事件一并撤销，不再被其它任务
  的 `consumeOnProgress` 目标误当新获得而错误推进进度（GP26-01）；`QuestHost.TurnIn` 步骤 2/3 失败
  的回滚同样改走事务，不再靠"移除又放回"产生虚假 `item.added`（STEP0-1）；直接交互（非 gossip）
  跨地图 teleporter 类物件现在能正确触发场景切换（GP26-03）；同图读档若命中"快照仍在倒计时、
  当下已有孤儿实体"会主动清理孤儿实体，不再与倒计时到期新生实体重复（附加-R14）。
- 规则/技能：套装门槛加成与普通装备 `grants.auras` 现在共享同一份光环来源引用计数，卸装备不再
  误删仍满足门槛的套装光环（GP26-02）；连续/离散时间模式切换现在同步换算技能冷却、光环剩余时间
  （FR-01），且施放/施加**当下**（不只是切换那一刻）就会按当前生效模式正确折算 `cooldown_duration`/
  `charges.recharge_time`/光环 `duration`/周期 `interval`/`cast_time`/`channel_time`/引导
  `tick_interval`/`modify_cooldown` 的 `delta`（WA-GAP-1/2、STEP0-2；`add_charge` 的 `amount` 是
  离散计数不受影响）；`charges.recharge_time<=0` 时充能耗尽后不再永久卡死，改为即时恢复（FR-02）；
  大步长推进不再让光环到期后的时间余量多算周期结算次数（FR-03）；读档恢复等级现在会失效重算评级
  属性缓存（FR-04）；技能 `effects[]`/`charges`/`cost[]` 的嵌套坏字段现在在数据校验阶段就会被拦下
  阻断合入，不再拖到首次施法才崩溃（FR-05 + 附加-Shape）；`item.template.grants.auras` 内重复引用
  同一光环新增数据校验 Warning 提醒（WA-GAP-3）。
- 表现：修正音乐交叉淡入/停止选错音源导致新旧音源互换（U01）；SFX 自然播放结束现在会回收对象池
  位，不再无限增长（U02）；序列帧动画大步长跨帧现在按序补发每一个跨过的关键帧，不再丢失中途命中
  特效（U03）；默认序列帧渲染器（`UnityFrameAnimPlayer` 挂载点）现在正确接入高度偏移/淡出/闪色，
  与纸娃娃层表现一致（U04，此前 WB 初判"无法复现"，WD 复核推翻，见 followup 文档 U04 行"过程"）。
- 交付：Release 工作流附件存在性检查现在核对完整五件套（zip/lock/三个 UPM tgz），部分缺失时只
  补传缺失的文件，不再因为 zip 已存在就整体跳过、遗漏其余附件（U05）。
- 文档：01/03/04/06/07/08/10/13 共 8 份架构文档按 12 §5 格式勘误（L5 查询/命令边界口径统一、
  固定步/计时器描述统一、时间字段"施放当下折算"补充说明、`grants.auras` 契约缺口清单更新等）；
  `core/rules/skill`、`core/carriers/item`、`core/gameplay/{common,quest,assembly}`、
  `core/numbers/progression`、`core/rules/expr_host`、`core/numbers/archetype/schema`、
  `presentation/vfx_sfx`、`core/foundation/sim_loop` 等模块 README 判断记录同步更新；"能力边界与
  未默认接入能力索引"补齐 `day_cycle`/ATB/孤儿检查(`DisplayMapCoverageRule`)/`FeedbackRuleValidator`
  四项，现收录两份审计报告表格给出的全部条目。

### 接口 / 事件 / 数据变更与迁移说明

- **新增事件** `sim.time_model_rescaled`（`Core.Rules.Common.TimeModelRescaledEvent{double Factor}`，
  `PublishImmediate`）：连续/离散模式切换时发出，驱动 `CooldownTracker`/`AuraHost`/`CastPipeline`
  各自的 `RescaleAll`。**迁移**：使用标准 `TimeModelSwitch`/`SkillHost` 装配（`GameplayAssembly`
  默认路径）的游戏无需任何改动，事件已自动接线。若游戏层自行实现了不经过 `TimeModelSwitch` 的
  模式切换逻辑，需要自己在切换点 `PublishImmediate` 这个事件才能让技能冷却/光环/读条正确折算。
- **新增事件** `progression.state_restored`（`Core.Numbers.Progression.ProgressionRestoredEvent
  {Id UnitId, int Level}`，`PublishImmediate`）：读档恢复等级时发出。**迁移**：标准装配无需改动，
  `RulesAssembly` 已默认订阅并转发到 `Stats.RecomputeRatingStats`；若游戏层维护了自己的等级相关
  缓存且未监听 `progression.level_up`，可能需要额外订阅这个新事件。
- **新增接口** `Core.Carriers.Common.IInventoryTransaction`（`Commit()` + `IDisposable`）、
  `IBatchableInventoryHost`（`BeginBatch(): IInventoryTransaction`）：`InventoryHost` 已实现。
  **迁移（需自定义实现方补实现）**：自定义 `IInventoryHost` 实现若不实现 `IBatchableInventoryHost`，
  `RewardDispatcher.GrantItems`/`QuestHost.TurnIn` 会自动回退到旧的"逐项精确量回滚"历史行为，
  行为不变、无需改动；若自定义实现选择实现该接口以获得"失败时事件也一并撤销"的完整保证，
  **必须支持嵌套调用**——`BeginBatch()` 在自身已处于一个未提交/未回滚的事务中时，应返回一个
  "加入外层事务"的透传句柄（其 `Commit`/`Dispose` 均为 no-op，不影响外层事务状态），而不是抛异常：
  `QuestHost.TurnIn` 会持有一个未提交的事务再调用 `RewardDispatcher.Grant`，后者若也需要发放物品
  会再次调用 `BeginBatch()`，两者必须能安全组合，见 `IInventoryTransaction.cs`
  `IBatchableInventoryHost.BeginBatch` 判断记录、`InventoryHost.BeginBatch`/`Transaction` 实现。
- **新增委托/配置** `core/gameplay/spawn/contracts/SpawnOptions.cs` 的 `GobjDespawnerDelegate`/
  `SpawnOptions.GobjDespawner`（可选）：供 `SpawnHost.Load` 在命中"同图读档孤儿实体"场景时移除
  `gobj` 域的孤儿实体（生物域经已持有的 `ICreatureFactory` 处理，无需额外配置）。**迁移**：使用
  `GameplayAssembly` 默认装配的游戏无需改动，已默认注入；自行组装 `SpawnHost` 的游戏若希望获得
  这一修复的完整效果，需要自己注入 `GobjDespawner`，否则保持旧行为（孤儿实体脱离追踪但不移除）
  并记一条诊断，不强制。
- **新增校验规则**（均已在 `RulesSchemaCatalog`/`CarriersSchemaCatalog` 的 `RegisterAll` 注册，
  `toolchain/validator` 自动继承，无需游戏层改动装配代码）：
  - `ChargesRechargeTimeZeroWarningRule`（Warning，check 名 `charges_recharge_time_zero`）
  - `ChargesShapeRule`（**Error**，check 名 `charges_max_missing`/`charges_max_invalid`/
    `charges_recharge_time_missing`/`charges_recharge_time_not_number`）
  - `CostEntryShapeRule`（**Error**，check 名 `cost_entry_not_object`/`cost_power_type_invalid`/
    `cost_amount_invalid`）
  - `ItemGrantsAurasDuplicateRule`（Warning，check 名 `item_grants_auras_duplicate`）
  - `EffectKindRegisteredRule` 行为变更（非新增）：新增 check 名 `effect_entry_not_object`/
    `effect_kind_missing`/`effect_kind_not_string`
  **迁移（需要内容作者关注）**：`ChargesShapeRule`/`CostEntryShapeRule`/`EffectKindRegisteredRule`
  的新增检查项是 **Error 级**——此前能以 0 error 通过 `DataRegistry.LoadAll()`、只在首次施法才
  崩溃的坏数据（`effects[]` 缺 `kind`、`charges`/`cost[]` 内部子字段缺失或类型错误），现在会在
  数据校验阶段直接阻断合入。已存在类似坏数据的内容仓库升级后首次跑 `validate_data.py`/
  `toolchain/validator` 会新增报错，需要修正数据（这些数据即便不修，本来也会在运行时抛异常，
  阻断合入是提前暴露问题，不是收紧了原本合法的用法）。
- **`CooldownTracker.ModifyCooldown` 行为变更**（签名不变）：`delta` 参数现按当前生效时间模式的
  `_currentFactor` 折算后再应用，与 `cooldown_duration` 同一口径。**迁移**：若游戏内容的
  `modify_cooldown` 效果原语按"与该技能 `cooldown_duration` 同一份连续秒 authoring"的惯例填写
  `delta`（本仓库默认假设，多数内容应该已经是这样），无需改动数据，离散模式下的实际效果会比
  修复前更符合直觉；若有内容特意依赖"离散模式下 `delta` 按当前轮数直接解释、不折算"的旧（有缺陷
  的）行为，需要重新核对该效果在离散战斗中的数值表现。`add_charge` 的 `amount` **不**受本次改动
  影响（离散充能计数，不是时间量）。
- **`CooldownTracker`/`AuraHost`/`CastPipeline` 新增公开方法** `RescaleAll(double factor)`：三者
  均由 `SkillHost` 构造期统一订阅 `sim.time_model_rescaled` 并转发，标准装配下不需要游戏层直接
  调用。
- **`UnityRenderer2D` 新增具体类型方法** `GetLayersRoot(SpriteHandle)`、
  `RegisterAnimRootRenderer(SpriteHandle, SpriteRenderer)`（均不进入 `IRenderer2D` 契约）；
  **`UnityFrameAnimPlayer` 新增公开属性** `SpriteRenderer`（只读，转发既有私有访问器）。
  **迁移**：仅供 `UnityViewFactory.AttachDefaultAnimation` 内部使用，游戏层通常无需直接调用；
  自定义 `IRenderer2D` 实现若也想让默认序列帧动画正确响应 height/flash/fade，可参考这一实现模式
  （把序列帧渲染器纳入与纸娃娃层同一套变换/颜色遍历）。
- **`IInventoryHost`/`IAudio`/`IFrameAnimPlayer` 等既有公开契约签名均未变化**（`UnityAudio` 新增
  的 `ActiveMusicSource`/`SfxPoolSize` 等均为 `internal` 测试专用访问器，不进入跨模块契约）。
- **无存档格式变更**：本轮全部修复均不改动任何 `IPersistable.Save()`/`Load()` 段的 JSON 结构。
- **`.github/workflows/release.yml` 行为变更**（CI 逻辑，非代码契约）：附件存在性判定改为核对
  完整五件套，游戏侧消费方无需改动，只影响本仓库自己的发布 CI 行为。

## [1.1.0] - 2026-09-07

第六方深度审核（codex 第四轮，基线 `1.0.0`/`7e63d66`，报告见
`architecture/落地计划/audit-7e63d66-20260907/`）19 条发现（C01～C12 共 12 条代码问题、P01～P07
共 7 条项目/交付问题）全部核实成立并根治，详见
`architecture/落地计划/audit-7e63d66-20260907/followup-2026-09-07d.md`。本条目记录变更内容。

### 修复（概要，逐条详见 followup 文档）

- 存档读档：旧备份候选核对 `meta.slot_id` 归属，避免跨槽误读（C01）；候选筛选核对 meta 必填字段，
  避免语义损坏文件挡住健康备份（C10）。
- 规则/技能：施法来源被销毁后周期效果缩放属性降级为 0、进战判定静默跳过而非抛异常（C02）；吸收
  耗尽的连锁移除正确传递触发深度，纳入 `MaxTriggerDepth` 收敛预算（C03）；装备 Replace 换句柄后
  另一件装备同步迁移引用，不再误清光环（C08）；技能读档改为替换语义而非只增不减（C09）。
- 玩法：Encounter/Achievement 发奖失败后保留可重试状态，不提前提交终态（C04）；`RewardDispatcher`
  按实际落地量回滚，不假设"请求量=落地量"（C05）；任务扣除物品改为先核验总量、不够不碰库存的原子
  操作（C06）；刷新点存档倒计时在同图读档时优先于当下世界状态（C11）；`reload_save` 现在也发布
  复活事件，动画状态机不再卡在死亡态（C12）。
- 表现：VFX/SFX 资源冷加载超时也会完整走完播放完成信号链，`ISfxPlayer` 新增独立时钟入口接入
  Unity 生产帧循环（C07）。
- 交付：Release 工作流在打包前补一步默认构建，干净 checkout 也能出传统路径 DLL（P01）；发行 ZIP
  与 UPM 工具链包的 validator 自包含（编译好的 DLL 或源码引用二选一），不再依赖包内不存在的源码树
  （P02）；同步脚本改清单制，只清理框架自己上次写入的文件，不再误删消费者文件（P03）；
  `get_framework.ps1` 默认严格校验请求版本与本地归档版本一致，不一致需显式 `-AllowVersionMismatch`
  （P04）；发布只推当前分支与本次新建的单个标签，不再固定推 `main` 与全部标签（P05）；私服禁止
  自注册取得发布权限，发布/删包限定到显式发布账号（P06）；sprite/audio/vfx 三类资源的同步路径与
  Unity loader 实际查找路径统一到共享映射表 `toolchain/resource_layout_map.json`（P07）。

### 接口变更与迁移说明

非破坏性增补（C# 默认接口方法，未覆盖的既有实现自动获得历史行为，无需改动）：

- `Core.Carriers.Common.IInventoryHost` 新增 `bool TryAddItem(Id unitId, Id templateId, int count, out int actualCount)`。
- `Core.Rules.Common.IAuraQuery` 新增带触发深度参数的移除重载，以及 `InstanceReplaced` 事件（默认空
  `add`/`remove`）。

破坏性增补（自定义实现方需补实现；仓库内既有实现均已补齐）：

- `Core.Gameplay.Achievement.IAchievementHost` 新增 `IReadOnlyList<Id> RetryPendingRewards(Id unitId)`
  ——仓库内唯一实现 `AchievementHost` 已补齐。
- `Presentation.VfxSfx.Contracts.ISfxPlayer` 新增 `void Update(double dt)`——仓库内 `SfxPlayer` 与
  测试用 `RecordingSfxPlayer`（`presentation/vfx_sfx/tests/AudioLayerVolumeHostTests.cs`）已补齐。

行为变更（签名不变，语义/时序变化，下游若按旧假设编写逻辑需要重新核对）：

- `Core.Gameplay.Death.RespawnPolicy.ReloadSave` 读档成功时现在也经 `IEventBus.Enqueue` 补发一次
  `Core.Rules.Common.UnitRespawnedEvent`（此前只有 `RespawnPoint` 策略发布该事件）。
- 存档段 `player.achievement_state` 每条记录新增可选字段 `pending_reward`（布尔，默认 `false`），
  向后兼容，旧存档缺省该字段按 `false` 处理。
- `Core.Rules.Skill.KnownSkillsPersistable.Load` 改为替换语义（快照未包含的永久技能会被撤销），不
  再是只增不减。
- `toolchain/get_framework.ps1` 新增 `-AllowVersionMismatch` 开关（默认关闭）；不带该开关时请求
  版本与本地归档版本不一致会直接 `throw`，不再仅 warning 后继续落地。
- `build.ps1 -Release`/`-Publish` 的 `git push` 改为推送当前所在分支 + 本次新建的单个标签，不再
  固定推送 `main` 分支与本机全部标签（`--tags`）；在 detached HEAD 下会报错拒绝执行。
- 私服 `toolchain/registry/config.yaml`：`auth.htpasswd.max_users` 由未设置（等价放开自注册）改为
  `-1`（禁止自注册）；`publish`/`unpublish` 权限从 `$authenticated`（任何已认证用户）改为限定显式
  用户名 `ws-game-publisher`。任何依赖"匿名自注册后即可发布"的私服接入脚本需要改用
  `toolchain/registry/init_publisher.ps1` 无人值守建号。

## [1.0.0] - 2026-09-07

首个正式基线版本。此前 `0.1.0`/`0.2.0` 均为落地过程中的里程碑快照（供消费方演练与打包流程自测
使用，未作为正式对外发布版本），`1.0.0` 是阶段 0～5 全部完成、经三轮内部审计与一轮外部深度审核
修复收口后的第一个"可供真实游戏接入"的稳定基线。

### 新增

- **阶段 0～5 全部完成**：环境与仓库骨架、L0 基础层（12 模块）、L1+L2 数值与规则层、L3+L4 载体
  与玩法层、Unity 适配层 + 表现层 + UI 套件、美术管线与资产规格；`Core.sln` 六个测试工程合计
  1550+ 例单测全过，Unity EditMode/PlayMode 测试全绿，独立版无人值守冒烟（连续/离散两种时间
  模型）通过，消费方演练（从零搭建独立于框架源码树的最小 Unity 工程，只以分发包为输入）通过。
- **离散时间模型**：与连续时间模型并列的第二套时间驱动方式（`TurnScheduler`、先攻策略、行动点
  移动预算、回合 HUD 等），玩家意图经 `WorldSim` 路由到调度器，回合结束统一推进计时器。
- **框架级数据目录分层**：`data/_framework/`（事件词汇登记表、输入动作声明等，随分发包交付）与
  `data/_sample/`（框架自测数据，不随分发包交付）分离；`DataRegistry` 支持多根合并加载（主键/
  schema 冲突阻断）。
- **新游戏模板** `games/_template/`：可运行的最小闭环骨架（`GameBootstrap`/`GameOptions`/
  `Editor/GameSceneBuilder`/`data/game/`/`validate.ps1`/PlayMode 冒烟测试），对照 13 号文档口味
  配置项清单逐行落地。
- **资产管线**：`toolchain/import_assets.py` 资产导入工具、`assets/_placeholder/` 通用占位资产
  包、方向档位/纸娃娃分层/序列帧图集等资产契约（14 号文档）。
- **一键门禁** `check.ps1`（22 步）与提交前钩子 `.githooks/pre-commit`（快速子集）、持续集成
  `.github/workflows/ci.yml`（非 Unity 门禁子集）。
- **版本管理方案**：语义化版本、`CHANGELOG.md`、`build.ps1 -Release`/`-DryRun`/`-Publish`、
  维护分支流程（`release/X.Y.x`）、发布工作流 `.github/workflows/release.yml`、游戏侧引用工具
  `toolchain/get_framework.ps1` 与锁文件 `ws-game.lock`。
- **私服交付通道**：与 zip 快照通道并存的第二条消费通道——私有包仓库（`toolchain/registry/`，
  Verdaccio，npm 兼容协议）+ 三个可发布包拆分（`com.gamefoundation.adapter.unity`/
  `com.gamefoundation.framework-data`/`com.gamefoundation.toolchain`）；`build.ps1 -Dist` 新增
  组装三个包 + `npm pack`，`-Release` 新增 `-PublishRegistry [-RegistryUrl]`；
  `toolchain/get_framework.ps1` 新增 `-FromRegistry`；`toolchain/sync_package_content.ps1`
  （新增）同步私服包内容到消费游戏工程；`check.ps1` 新增"包清单一致性"步骤。

### 修复

- **三轮内部文档代码一致性审计**（2026-09-05～2026-09-07）：逐轮核对 00～14 号架构文档与实现的
  一致性，修复审计发现的代码缺失（行动点、`SpellModDimension.Charges`、离散 GCD 接线、死亡复活
  三策略执行主体、种族被动光环应用、回合状态占位值等）与文档勘误，详见
  `architecture/落地计划/文档代码一致性审计_2026-09-05.md`、`_2026-09-06.md`、`_2026-09-07.md`。
- **外部深度审核 33 条发现根治**（分支 `codex/deep-review-b3b91ee-20260907`，报告见
  `architecture/落地计划/audit-b3b91ee-20260907/`）：FND-01～10、GP-01～10、RC-01～11、
  TOOL-01/02 共 33 条经三波并行核实全部成立并根治；排障过程中额外发现并修复 PlayMode 全量套件
  `VerticalSliceTests` 因跨夹具存档槽配额累积导致的隐性失败。
- **缺口收敛 G1/G2/G3**（2026-09-05）：16 条已知契约缺口中 13 条落地解决（`AnchorResolver`、
  `SpawnRequester`、`TeleportResolverDelegate`、`SaveRequesterDelegate`、回合状态显示等），3 条
  设计层判断维持"保留"（非拍板内容或本就只需单点承担的既定设计）。
- 修复：codex 第三轮深度审核 19 条（详见 audit-68c9bed-20260907/followup-2026-09-07c.md）。
- 修复：发布流程先提交后打包，lock/MANIFEST 的 `git_commit` 指向发布提交；写回覆盖
  `packages-lock.json`（`build.ps1 -Release` 首次实跑发现的时序与写回遗漏两处缺陷，根治后
  `1.0.0` 重新发布，详见根 `README.md`"版本与发布"一节）。

### 兼容性说明

- 存档格式：`save_version`（存档信封层字段，迁移链唯一依据）当前为初始版本，尚无历史存档需要
  迁移；后续存档结构不兼容变更须递增 `save_version` 并登记迁移函数（见
  `architecture/10_存档与持久化.md` 第 5 节）。
- 数据表：各表独立的 `schema_version`（见 `architecture/04_数据与内容管线.md`）随本版本一次性
  确定，表结构不兼容变更（字段删改）须递增 `schema_version` 并提供迁移路径。
- 构建产物公开契约：六个核心 DLL（`Core.Foundation`、`Core.Numbers`、`Core.Rules`、
  `Core.Carriers`、`Core.Gameplay`、`Presentation.Common`）随分发包 `dist/1.0.0/` 交付；对外公开
  的 L-1 接口签名（引擎适配层契约）、事件 key、数据表结构、存档结构变更均属 MAJOR 级变更范畴。
- 与 `0.2.0` 的差异：`1.0.0` 不改变任何公开契约或数据结构，只是把此前若干里程碑快照正式确立为
  第一个语义化版本基线，并新增本文件描述的版本管理方案本身（`build.ps1`/`check.ps1`/工作流/
  文档新增的发布相关能力）。

### 从 68c9bed 早期消费者迁移（早于 `0.1.0`/`0.2.0` 快照拉取过框架的消费方需核对）

`1.0.0` 基线包含 codex 第三轮深度审核（`audit-68c9bed-20260907/`）引入的以下破坏性/行为变更，若消费
方在提交 `68c9bed` 或更早时拉取过框架、并自行实现或依赖了下列契约，需要按下表核对：

| 契约/行为 | 变更内容 | 影响范围与迁移动作 |
|---|---|---|
| `Core.Gameplay.Common.IRewardDispatcher.Grant` | 签名由 `void Grant(...)` 改为 `bool Grant(...)`（破坏性签名变更） | 任何直接实现本接口的类型需要补返回值；调用方若忽略返回值仍可编译通过，但拿不到"是否实际发放成功"的信号，建议改为检查返回值以配合 `IQuestHost.TurnIn` 的原子化回滚（发放失败时任务不会被标记 `TurnedIn`，已消耗物品会回滚）。 |
| `Core.Rules.Common.ITargetHost` | 新增方法 `FilterExplicitTargets`（破坏性增补） | 任何直接实现本接口的类型需要补一个实现；仓库内唯一实现 `TargetHost` 已补齐。修复前显式指定的非法目标（如链式过滤要求 undead 但玩家显式指定了非 undead 目标）会被直接放行，修复后统一经该方法校验并按 `NoValidTarget` 失败码拒绝。 |
| `Presentation.FeedbackBinder.Contracts.IFeedbackSink` | 新增事件 `PendingPlaybackChanged`（破坏性增补） | 任何直接实现本接口的类型需要补一个实现（默认空 `add`/`remove` 亦可）。配套 `Presentation.VfxSfx.Contracts.IVfxPlayer`/`ISfxPlayer` 同批新增 `PendingSpawnCountChanged`/`PendingPlayCountChanged` 事件；`FeedbackBinder.TryPublishFinished` 改为"队列空 && 无 merger 待处理 && 无 sink 待处理"三者同时成立才发 `PlaybackFinishedEvent`，此前冷资源（首次加载中）的挂起播放会被误判为已完成。 |

以上三项详见 `architecture/落地计划/audit-68c9bed-20260907/followup-2026-09-07c.md`（N02/N10/N17）。

## [0.2.0]

里程碑快照（供内部打包流程与消费方演练自测使用）。收录工程收尾 K、加固波 J（契约一致性测试套件、
消费方演练脚本、PlayMode 隔离）、第三轮审计修复波（W1～W4）、第四方深度审核修复三波、离散时间
模型引擎侧接线、`found.time_model` 归属勘误、框架级数据目录与新游戏模板等一系列提交；详见
`architecture/落地计划/落地方案与分阶段计划.md`"落地进度记录"各小节与本仓库 `git log`。

## [0.1.0]

首个里程碑快照。收录阶段 0～5 全部完成、缺口收敛 G1/G2/G3、框架收官（离散时间模型初版落地、
文档/数据/门禁收尾）、收边波 I/J1 等提交；详见
`architecture/落地计划/落地方案与分阶段计划.md`"落地进度记录"各小节与本仓库 `git log`。
