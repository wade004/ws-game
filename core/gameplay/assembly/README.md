# L4 玩法层 · assembly（组装根）

`GameplaySchemaCatalog`/`GameplayAssembly` 是 `core/gameplay` 十模块（`common`/`world_state`/
`loot`/`economy`/`quest`/`dialog`/`encounter`/`difficulty`/`achievement`/`area_trigger`/`spawn`）
在 `core/carriers/assembly.CarriersAssembly`（L0～L3）之上的最终组装根（阶段 3 集成收尾"事项一"）。
调用方（游戏层引导代码、集成测试、`toolchain/validator`）只需要：

1. `GameplaySchemaCatalog.CreateOptions()` 构造 `DataRegistryOptions`（`ExprSchema` 已设为完整
   九分组组合）→ 构造 `DataRegistry`。
2. `GameplaySchemaCatalog.RegisterAll(registry)` 一次性注册 L0～L4 全部表/校验规则。
3. `registry.LoadAll()`。
4. `new GameplayAssembly(bus, registry, rng, world, spatial, playerUnitProvider, playerFactionId, ...)`
   拿到全部十个 L4 宿主 + `AppState`/`Hooks`/`Reward`/`ExprHostFactory`。
5. 场景切换完成后调用一次 `GameplayAssembly.EnterMap(mapId, playerUnitId)`；旧场景卸载前
   （场景路由 `pre_unload` 钩子）调用一次 `GameplayAssembly.LeaveMap(mapId)`（ADR-0016 背景一节
   联动发现的既有缺口——此前只有"进图"入口，见该方法判断记录）。

## 目录

```
assembly/
  README.md
  GameplaySchemaCatalog.cs   L0～L4 全部 TableSchema/IValidationRule 的统一注册清单 + FullExprSchema
  GameplayAssembly.cs        L4 组装根
  tests/
    GameplayAssemblyTests.cs 烟雾测试（空数据装配、EnterMap 空图不抛异常）
    GameplayAssemblyMapReloadTests.cs 场景卸载级联清理（进图生成生物→LeaveMap→再进图 tick 数十次
      不抛异常，见 GameplayAssembly.LeaveMap 判断记录）
```

## 装配顺序（`GameplayAssembly` 构造函数内部步骤）

| # | 步骤 | 关键依赖来源 |
|---|---|---|
| 1 | `WorldState`（只依赖 `IEventBus`） | 无 |
| 2 | 两个延迟绑定代理：`DeferredLootRoller`、`DeferredExprGroupProvider`（quest/player 各一份） | 无（占位） |
| 3 | `CarriersAssembly`（L0～L3）：`worldFlags` 直接注入 `WorldState`；`lootRoller` 注入延迟代理；`extraSchemas` 传 `GameplaySchemaCatalog.FullExprSchema` | `WorldState` |
| 4 | 第二份 `RulesExprHostFactory`（带 `extraGroups`：world 已绑定、quest/player 延迟）暴露为 `ExprHostFactory` 属性；同时订阅 `combat.entered` 自行追踪战斗起始时间 | `CarriersAssembly.Rules.*` |
| 5 | `DifficultyHost`（先于 Loot，见判断记录 1） | `Carriers.Rules.Skill.EffectSink`/`Factions`/`Units` |
| 6 | `LootHost` + `CreatureDeathLootListener`；回填 `DeferredLootRoller` | `ExprHostFactory`、`Difficulty.LootMultiplier`（闭包） |
| 7 | `RewardDispatcher`（`currencyGranter` 用局部变量延迟闭包到第 8 步的 `EconomyHost`，惯例同 `RulesAssembly` 的 `progression` 变量写法） | `Carriers.Inventory`/`Progression`/`WorldState` |
| 8 | `EconomyHost`；回填第 7 步闭包；绑定 `DeferredExprGroupProvider`（player 分组，`ChainedExprGroupProvider` 合并 `PlayerExprGroupProvider` + `PlayerCurrencyExprGroupProvider`） | `ExprHostFactory` |
| 9 | `QuestHost`（解析 `quest.def`）；绑定 `DeferredExprGroupProvider`（quest 分组） | `ExprHostFactory`、`Reward` |
| 10 | `AppStateHost`、`HookRegistry` | `IEventBus` |
| 11 | `SpawnHost`（`GobjSpawner` 接 `GameObjectFactory.Spawn`） | `WorldState`、`Carriers.Creatures` |
| 12 | `EncounterHost` + `LevelHost`（`spawnRequester` 见判断记录 2） | `Carriers.Creatures`/`Ai`、`Hooks`、`Reward` |
| 13 | `AchievementHost` | `ExprHostFactory`、`Reward` |
| 14 | `DialogHost`（`teleportRequested`/`encounterStartRequested` 回调接线） | `AppState`、`Quest`、`Hooks`、`WorldState`、`Carriers.Rules.Skill` |
| 15 | `AreaTriggerHost`（`TrapTrigger`/`EncounterStartRequested`/`MapTransitionRequested`/`SceneRouter` 回调接线） | `WorldState`、`ExprHostFactory`、`Hooks` |
| 16 | 回填 `GobjOptions` 四个 L4 回调（`DialogOpener`/`TeleportResolver`/`SaveRequester`/`QuestActionDispatcher`，见判断记录 3） | `Dialog`、`Quest` |
| 17 | tick 处理器挂载（见下表） | `IWorldSim` |

## 回调接线矩阵（依赖倒置 / 契约缺口回接）

| 接口/委托 | 提供方 | 消费方 | 备注 |
|---|---|---|---|
| `IWorldFlags` | `WorldState`（显式接口实现） | `CarriersAssembly` → `GameObjectHost` | 构造期直接注入，无需延迟 |
| `ILootRoller` | `LootHost`（显式接口实现） | `CarriersAssembly` → `GameObjectHost` | 经 `DeferredLootRoller` 延迟绑定（步骤 2/6） |
| `SkillGranter` | `Carriers.Rules.Skill.LearnSkill/ForgetSkill` | `RewardDispatcher` | 具名委托闭包 |
| `CurrencyGranter` | `EconomyHost.Add` | `RewardDispatcher` | 局部变量延迟闭包（步骤 7/8） |
| `GobjSpawnerDelegate` | `Carriers.GameObjects.Spawn` | `SpawnHost`（`content_ref` 域名 `gobj` 时） | |
| `SpawnRequester` | `Spawn.SpawnNow`（G1 已接线，见判断记录 2） | `EncounterHost` | 已解决 |
| `DialogOpenerDelegate` | `Dialog.OpenGossip`（`dialogRef` 权宜当 `npcId` 使用，见判断记录 3） | `GobjOptions` → `GameObjectHost` | |
| `TeleportResolverDelegate` | `TeleportTargetResolver.Resolve`（G1 已接线，按 `teleport_target_ref` 解析 `world.map`/`spawn_points`/`teleport_points`，见判断记录 4） | `GobjOptions` → `GameObjectHost` | 已解决 |
| `SaveRequesterDelegate` | `RequestAutosave`（构造函数最前面的本地函数，`saveSystem.Save(autosaveSlotId)`；`DialogHost.saveRequested` 共用同一份，见 G1 遗留恢复判断记录） | `GobjOptions` → `GameObjectHost` | 已解决 |
| `QuestActionDispatcherDelegate` | `Quest.Accept` | `GobjOptions` → `GameObjectHost` | 语义存疑，见判断记录 3 |
| `TeleportRequestedCallback` | `GameplayAssembly.TeleportUnit`（只切 `MapId`，不落具体坐标） | `DialogHost` | |
| `EncounterStartRequestedCallback` | `Encounter.Start(ref, 当前玩家所在地图, 玩家单位)` | `DialogHost`/`AreaTriggerOptions` | |
| `TrapTriggerDelegate` | `Carriers.GameObjectInteractions.TriggerTrap` | `AreaTriggerOptions` → `AreaTriggerHost` | |
| `MapTransitionRequestedDelegate` | `GameplayAssembly.TeleportUnit` | `AreaTriggerOptions` → `AreaTriggerHost` | |
| `ISceneRouter`（可选） | 调用方注入的 `sceneRouter` 构造参数 | `AreaTriggerOptions` → `AreaTriggerHost` | 未注入时 `map_transition` 只记诊断 |

## tick 阶段挂载表

全部四个新增 tick 处理器统一挂在 `TickPhase.TriggerEvaluation`（`IWorldSim` 对外开放的最后一个
阶段——`EventDispatch`/`LifecycleCleanup` 由 `WorldSim` 自己执行，不可外部注册），按注册顺序执行：

| 顺序 | 处理器 | 依赖的"本 tick 已完成"前提 |
|---|---|---|
| 1 | `AreaTriggerTickHandler` | `MovementAndNavigation` 阶段已完成的位移结算 |
| 2 | `EncounterTickHandler` | `CombatResolution` 阶段已完成的战斗结算 |
| 3 | `LootExpiryTickHandler` | 无 |
| 4 | `EconomySpawnUpdateTickHandler`（本类私有适配器，转发 `EconomyHost.Update(dt)`/`SpawnHost.Update(dt)`，见判断记录 5） | 无 |

## 存档段顺序（`RegisterPersistables(ISaveSystem, PlayerUnit)`）

按 10_存档与持久化.md 第 3 节固定顺序：`WorldState`（`world_state_flags`）→
`UnitPersistable.CurrentMapId`/`CurrentPosition` → `InventoryPersistable`/`EquipmentPersistable`
→ `CurrencyPersistable`/`VendorStockPersistable` → `QuestPersistable` → `AchievementHost` →
`SpawnHost` → `DroppedLootPersistable` → `DifficultyHost`（自定义段 `world.difficulty`）→
`RngStreamsPersistable`（10 §3 步骤 8，全序最末——调用方需要自行额外注册，本方法不持有
`IRngHost`）。`player.progression` 段本方法不注册，见判断记录 6。

## 判断记录

1. **`DifficultyHost` 先于 `LootHost` 构造**：`CreatureDeathLootListener` 的
   `lootMultiplierProvider` 闭包读 `Difficulty.LootMultiplier`——若在 `Difficulty` 属性被赋值前
   就定义这个闭包，C# 可空引用分析会因"声明时尚未确定赋值"报 CS8602（本仓库把可空引用警告当
   错误处理）。调整构造顺序（而不是加 `!` 抑制）保证语义与编译期检查同时成立。

2. **`SpawnRequester` 已解决（G1）**：`core/gameplay/spawn` 补了
   `ISpawnHost.SpawnNow(spawnId, mapId): List<Id>`（按单个 `spawnId` 立即生成一次，见 05 第 5.3
   节勘误、`SpawnHost.SpawnNow` 源码），本类现直接 `SpawnRequester spawnRequester = Spawn.SpawnNow;`
   接线，不再退化为恒返回空列表；示例数据/端到端测试此前绕开该路径改用
   `encounter.def.units[].template_ref`（内联模板）的用法仍然有效，两条路径并存。

3. **`GobjOptions` 四个回调的接线时机与已知简化**：`GobjOptions` 必须在 `CarriersAssembly`
   构造时就传入一个非空实例（不能让 `CarriersAssembly` 自己 new 一份默认值）——`GameObjectHost`
   内部持有的是构造期传入实例的引用（不拷贝字段），本类先 `resolvedGobjOptions = gobjOptions ??
   new GobjOptions()` 传给 `CarriersAssembly`，等 `Dialog`/`Quest` 都构造完成后（步骤 16）再回填
   同一个实例上的四个委托属性才能生效。`DialogOpenerDelegate` 签名只有 `(unitId, dialogRef)`，
   不携带触发交互的 gobj 实例 id——`DialogHost.OpenGossip` 需要三元组
   `(unitId, npcId, menuId)`，本类权宜地把 `dialogRef` 同时当 `npcId` 使用（会话的 `NpcId` 只用作
   Expr `target` 分组与后续 vendor/quest 回调的定位标识，不要求是真正的生物单位 id）；
   `QuestActionDispatcherDelegate` 的语义 07/08 文档均未给出精确定义，本类按"最常见用例——一个
   `quest_object` 交互触发接取任务"权宜接到 `Quest.Accept`。两处都是记录在案的简化，不是最终
   方案，见 `GameplayAssembly.cs` 对应代码注释。

4. **`TeleportResolverDelegate` 已解决（G1）**：`TeleportTargetResolver`（本目录新文件）按
   `teleport_target_ref` 的 `'.'` 分段规则解析出精确落点（两段引用 `world.map` 取
   `spawn_points[0]`；三段引用按第三段与该地图 `teleport_points[]`/`spawn_points[]` 逐条比较末段
   id），不再是"恒把 ref 当目标地图、位置固定 `Vec2.Zero`"的简化实现；`resolvedGobjOptions.
   TeleportResolver ??= teleportTargetResolver.Resolve` 接线，解析失败（非法引用/地图或点位不存在）
   时返回 null，`GameObjectHost.DoTeleport` 记一条诊断，不抛异常。
   `AreaTriggerOptions.MapTransitionRequested`（区域触发型传送，与本条 `teleporter` 型物件传送是
   两条独立路径）判断记录仍然有效：`AreaTriggerHost` 触发的地图切换只经 `TeleportUnit` 切换
   `MapId`，精确 `spawn_point` 落位仍由 `EnterMap` 之后的调用方（游戏层）按 `spawnPoint` 查表调用
   `Carriers.Units.SetPosition` 完成，本类不越权代劳。

5. **`economy`/`spawn` 的 `Update(dt)` 没有自带 `ITickPhaseHandler`**：不像 `loot`/
   `area_trigger`/`encounter` 三个模块各自导出了一个 tick 处理器，`IEconomyHost.Update`（
   `restock_policy=timer` 补货倒计时）与 `ISpawnHost.Update`（`respawn_policy=timer` 刷新倒计时）
   需要调用方自己按秒推进。本类补了一个私有的 `EconomySpawnUpdateTickHandler` 最小适配器。

6. **`RegisterPersistables` 不注册 `player.progression` 段**：`core/numbers/progression.
   ProgressionHost` 未实现 `IPersistable`（勘察确认，不在本任务允许改动的目录范围内补），10 §2.2
   `player.progression` 段因此暂无持久化实现可挂——本方法如实跳过，不假装注册一个不存在的段。
