# L4 玩法层 · assembly（组装根）

`GameplaySchemaCatalog`/`GameplayAssembly` 是 `core/gameplay` 十模块（`common`/`world_state`/
`loot`/`economy`/`quest`/`dialog`/`encounter`/`difficulty`/`achievement`/`area_trigger`/`spawn`）
在 `core/carriers/assembly.CarriersAssembly`（L0～L3）之上的最终组装根（阶段 3 集成收尾"事项一"）。
调用方（游戏层引导代码、集成测试、`toolchain/validator`）只需要：

1. `GameplaySchemaCatalog.CreateOptions()` 构造 `DataRegistryOptions`（`ExprSchema` 已设为完整
   九分组组合）→ 构造 `DataRegistry`。
2. `GameplaySchemaCatalog.RegisterAll(registry)` 一次性注册 L0～L4 全部表/校验规则。
3. `registry.LoadAll()`。
4. `new GameplayAssembly(bus, registry, rng, world, spatial, saveSystem, playerUnitProvider, playerFactionId, ...)`
   （`saveSystem: ISaveSystem` 是必填的第 6 个位置参数，此前本行示例遗漏，2026-09-07 勘误补上）
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
  TimeModelSwitch.cs         ADR-0013 主循环模式切换（连续 ⇄ 离散），见下方"离散时间模型"一节
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
| 18 | `DeathPolicyHost`（W2 收边补齐，DECISIONS 拍板 3）：`ReviveUnit`/`ResolveDefaultSpawn` 两个委托 `??=` 接线（`Carriers.Units is WorldUnitAccess` 时接 `.Revive`；`teleportTargetResolver.Resolve`），同时作为 tick 处理器挂上 `TriggerEvaluation` | `AppState`、`SaveSystem`、`Carriers.Rules.CombatOptions.DeathPolicy`、第 16 步的 `teleportTargetResolver` |

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
| `SaveRequesterDelegate` | `RequestAutosave`（构造函数最前面的本地函数，先判 `saveSystem.ShouldAutoSave(AutoSaveTrigger.SavePoint)` 再 `saveSystem.Save(autosaveSlotId)`——加固任务补齐门控，`OnSavePoint=false` 时不再写盘；`DialogHost.saveRequested` 共用同一份，见 G1 遗留恢复判断记录） | `GobjOptions` → `GameObjectHost` | 已解决 |
| `QuestActionDispatcherDelegate` | `Quest.Accept` | `GobjOptions` → `GameObjectHost` | 语义存疑，见判断记录 3 |
| `TeleportRequestedCallback` | `GameplayAssembly.TeleportUnit`（经 `TeleportTargetResolver` 解析出目标 `MapId`+坐标并两者都落地——`entity.MapId` 与 `Carriers.Units.SetPosition` 一并写入；同图只挪点位不重载场景，跨图才调用 `ISceneRouter.LoadScene`；2026-09-07 勘误：此前"只切 MapId，不落具体坐标"的描述已过时，不是当前实现） | `DialogHost`（gossip `teleport` 动作）；`gobj.interacted` 事件订阅（`HandleGobjTeleporterInteracted`，外部审计 5e779c6 R04 收口，直接交互 `kind=teleporter` 的 gobj 不经 gossip 时补上同一条路径，见判断记录） | |
| `EncounterStartRequestedCallback` | `Encounter.Start(ref, 当前玩家所在地图, 玩家单位)` | `DialogHost`/`AreaTriggerOptions` | |
| `TrapTriggerDelegate` | `Carriers.GameObjectInteractions.TriggerTrap` | `AreaTriggerOptions` → `AreaTriggerHost` | |
| `MapTransitionRequestedDelegate` | `GameplayAssembly.TeleportUnit` | `AreaTriggerOptions` → `AreaTriggerHost` | |
| `ISceneRouter`（可选） | 调用方注入的 `sceneRouter` 构造参数 | `AreaTriggerOptions` → `AreaTriggerHost` | 未注入时 `map_transition` 只记诊断 |

## tick 阶段挂载表

全部五个新增 tick 处理器统一挂在 `TickPhase.TriggerEvaluation`（`IWorldSim` 对外开放的最后一个
阶段——`EventDispatch`/`LifecycleCleanup` 由 `WorldSim` 自己执行，不可外部注册），按注册顺序执行
（2026-09-07 勘误：此前称"四个"，`Death`/`DeathPolicyHost` 是 W2 收边补齐时作为第 5 个处理器
一并挂上的，下表本已列出五行，只是这句引子文字没有同步更新）：

| 顺序 | 处理器 | 依赖的"本 tick 已完成"前提 |
|---|---|---|
| 1 | `AreaTriggerTickHandler` | `MovementAndNavigation` 阶段已完成的位移结算 |
| 2 | `EncounterTickHandler` | `CombatResolution` 阶段已完成的战斗结算 |
| 3 | `LootExpiryTickHandler` | 无 |
| 4 | `EconomySpawnUpdateTickHandler`（本类私有适配器，转发 `EconomyHost.Update(dt)`/`SpawnHost.Update(dt)`，见判断记录 5） | 无 |
| 5 | `Death`（`Core.Gameplay.Death.DeathPolicyHost`，W2 收边补齐）：推进 `respawn_point` 策略的延迟复活队列，不区分 `Continuous`/`Discrete` 步（见该类型判断记录 3） | 无 |

## 存档段顺序（`RegisterPersistables(ISaveSystem, PlayerUnit)`）

按 10_存档与持久化.md 第 3 节固定顺序：`WorldState`（`world_state_flags`）→
`ProgressionPersistable.For`（`player.progression`）/`UnitPersistable.ArchetypeId`
（`player.archetype`，W2 收边补齐，见判断记录 6）→
`UnitPersistable.CurrentMapId`/`CurrentPosition` → `InventoryPersistable`/`EquipmentPersistable`
→ `CurrencyPersistable`/`VendorStockPersistable` → `QuestPersistable` → `AchievementHost` →
`SpawnHost` → `DroppedLootPersistable` → `DifficultyHost`（自定义段 `world.difficulty`）→
`TurnScheduler`（`sim.turn_state`，只在装配了离散模式时注册）→
`RngStreamsPersistable`（10 §3 步骤 8，全序最末——`RegisterPersistables` 内部用构造期传入的同一个
`IRngHost` 实例（`Rng` 属性）直接 `new RngStreamsPersistable(Rng)` 并注册，2026-09-07 勘误：此前
"调用方需要自行额外注册，本方法不持有 IRngHost"的描述已过时，P1-03 收口已把这一步收进本方法内部，
调用方不需要也不应该再自行重复注册一次）。实际读写顺序由 `SaveSections.KnownOrder` 决定（W2 收边补齐已把 7a 世界附属段与
7b `sim.turn_state` 一并登记进该表，顺序与 10 文档"7a 后 7b"一致，见该表判断记录），与本方法内
`RegisterPersistable` 调用顺序无关。

## 离散时间模型（ADR-0013）

`GameplayAssembly` 新增可选构造参数 `clockHost`（`ISimClockHost`）：不传时（默认）行为与本任务
之前完全一致——`TimeModelSwitch`/`TurnScheduler`/`Pacing`/`Advance` 均不装配，`Advance` 抛
`InvalidOperationException`。传入 `clockHost` 后：

- 第 3.5 步（`CarriersAssembly` 之后）提前构造 `Core.Foundation.SimLoop.TurnScheduler`（供第 4 步
  `ExprHostFactory` 接线 `time.turn_index`/`round_index`/`is_my_turn`）。
- 第 10 步 `AppStateHost` 额外登记两个自定义子状态 `AwaitingInput`/`PlayingBack`
  （`SubStateId` 扩展点，不改 `InWorldSubState` 枚举），并放行 `Combat ⇄ AwaitingInput`、
  `Combat ⇄ PlayingBack` 两组转移。
- 第 10.5 步构造 `TimeModelSwitch`（订阅 `combat.entered`/`combat.left`/`unit.died`，见该类型；
  已处于离散模式时收到 `combat.entered`/`unit.died` 改为调用
  `TurnScheduler.AddParticipant`/`RemoveParticipant` 实时同步参与者，见该类型判断记录"中途
  加入/离场"），随后把 `found.time_model.combat` 声明的移动预算规则/单位成本、以及行动点消耗/
  结束回合出口回填进 `Carriers.MovementOptions`（`movement_budget_rule: action_points` 落地，
  见 `MovementOptions.TryConsumeActionPoints`/`RequestEndTurn` 判断记录）。
- `GameplayAssembly.Advance(realDeltaSeconds)`：连续模式转发给 `clockHost.Advance`；离散模式驱动
  `TurnScheduler.NextStep()` 直到轮到玩家（`awaiting_input`）或需要等待回放（`playing_back`），
  期间维护 `AwaitingInputSubState`/`PlayingBackSubState` 的压栈/弹栈。`NotifyPlaybackFinished()`
  供表现层在 `presentation.playback_finished` 后转发调用，解除 `playing_back` 节奏门。每次
  `Advance` 调用后可读 `InterpolationAlpha`（`double`，只读）：连续模式下等于本次调用
  `ISimClockHost.Advance` 返回的插值系数（`[0,1)`）；离散模式、以及未传入 `clockHost` 时恒为
  `1.0`（离散步之间没有可插值的位置差，见该属性判断记录）——表现层（Unity 侧 `adapters/unity`）
  在每帧调用完 `Advance` 后应据此驱动 `ViewBinder.SyncAll(alpha)`。
- `RegisterPersistables` 在装配了离散模式时额外注册 `TurnScheduler` 的存档段（`sim.turn_state`，
  已登记进 10 号文档第 3 节固定段序步骤 7b，W2 收边补齐后已随四个世界附属段一并登记进
  `SaveSections.KnownOrder`，不再是按 key 序数排序的"自定义段"，见
  `core/foundation/sim_loop/README.md`"存档段 key"判断记录、`SaveSections.KnownOrder` 判断记录）。

现有调用方（未传 `clockHost` 的既有测试与游戏层引导代码）不受任何影响——这是一处纯加法扩展。

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

4. **`TeleportResolverDelegate` 已解决（G1）；N14 收口（外部审核 68c9bed）后 `TeleportUnit` 走
   统一导航**：`TeleportTargetResolver`（本目录新文件）按 `teleport_target_ref` 的 `'.'` 分段规则
   解析出精确落点（两段引用 `world.map` 取 `spawn_points[0]`；三段引用按第三段与该地图
   `teleport_points[]`/`spawn_points[]` 逐条比较末段 id），供 `resolvedGobjOptions.
   TeleportResolver ??= _teleportTargetResolver.Resolve`（`teleporter` 型物件传送）复用；解析
   失败（非法引用/地图或点位不存在）时返回 null，`GameObjectHost.DoTeleport` 记一条诊断，
   `TeleportUnit` 不产生任何副作用（不改 `MapId`、不发起场景切换），不抛异常。
   `AreaTriggerOptions.MapTransitionRequested`（区域触发型传送，与 `teleporter` 型物件传送是两条
   独立路径，已拆分好地图 id 与具体点位 id）改用 `TeleportTargetResolver.ResolveExplicit` 按精确
   id 匹配（不是 `Resolve` 那套"猜测段数"的模糊匹配）。**N14 之前**：`TeleportUnit` 只改
   `entity.MapId` 一个字段，不触碰 `Spawn`/`AreaTrigger`/`Encounter`/`Loot` 等按地图分片登记的
   状态、不切换场景资源——跨图传送后旧图刷新点/触发器/遭遇仍以为自己还装载着，旧图生成的实体仍
   存在于 `IWorldSim` 但玩家逻辑上已"离开"，新图对应状态从未 `EnterMap` 因此完全空白。**N14 之后**：
   `TeleportUnit` 把解析出的 `MapId`/位置先落到玩家实体上（同 `RestoreFromSlot` 判断记录"目标地图
   与当前地图相同时……已经把状态直接写回长期存活的 PlayerUnit 对象"这一惯例），目标地图与传送前
   不同时才经 `ISceneRouter.LoadScene` 发起统一导航（内部依次触发调用方已注册的 `pre_unload`→
   `LeaveMap`→`IWorldSim.ClearAll`→装载新场景→`post_load`→`EnterMap`，与 `RestoreFromSlot` 跨图
   分支完全一致）；同地图内传送（只挪点位）不发起场景重载。见 `GameplayAssembly.cs`
   （`TeleportUnit`、`TeleportTargetResolver.ResolveExplicit`）、
   `core/gameplay/assembly/tests/GameplayAssemblyTeleportTests.cs`。

5. **`economy`/`spawn` 的 `Update(dt)` 没有自带 `ITickPhaseHandler`**：不像 `loot`/
   `area_trigger`/`encounter` 三个模块各自导出了一个 tick 处理器，`IEconomyHost.Update`（
   `restock_policy=timer` 补货倒计时）与 `ISpawnHost.Update`（`respawn_policy=timer` 刷新倒计时）
   需要调用方自己按秒推进。本类补了一个私有的 `EconomySpawnUpdateTickHandler` 最小适配器。

6. **`RegisterPersistables` 已注册 `player.progression`/`player.archetype` 两段（历史缺口已解决，
   W2 收边补齐，见 A4 审计 F1）**：`core/numbers/progression.ProgressionHost` 已补
   `ProgressionPersistable` 静态工厂（段 `SaveSections.PlayerProgression`），`PlayerUnit` 已补
   `UnitPersistable.ArchetypeId`（段 `SaveSections.PlayerArchetype`）——`GameplayAssembly.
   RegisterPersistables` 现分别注册两者，玩家等级/经验/职业模板引用可正常跨读档保留，端到端
   回归见 `core/gameplay/tests/EndToEndTests.cs`
   `SaveThenLoad_AfterLevelUp_RestoresProgressionAndArchetype` 一类用例。

7. **R05 收口（外部审计 5e779c6，P2，成立，跨模块——本模块负责的一半）：`TimeModelSwitch.
   RescaleTimers` 除了换算 `SimTimers`，还同步广播一条 `Core.Rules.Common.TimeModelRescaledEvent`**：
   模式切换此前只换算了 `core/foundation/sim_loop.SimTimers` 的通用具名计时器，完全没有触及
   `core/rules/skill`（技能冷却/光环剩余时间）模块内部维护的倒计时状态，切换后计时按新模式的
   tick 单位重新解读会导致数值错位（外部审计复现）。本模块不能直接持有 `core/rules/skill` 的
   具体类型引用（跨越 L2/L4+ 层级边界，见 00 架构总则分层依赖方向），改用 `TimeModelSwitch` 已经
   持有、且与 `core/rules/skill.SkillHost` 构造期共享的同一个 `IEventBus`，
   `PublishImmediate`（不是 `Enqueue`，必须在方法返回前同步完成）一条 `TimeModelRescaledEvent`，
   `SkillHost` 订阅后自行换算——不需要本文件"装配顺序"新增任何构造参数或接线步骤。见
   `TimeModelSwitch.cs`（`RescaleTimers` 判断记录）、`core/rules/common/contracts/Events.cs`
   （`TimeModelRescaledEvent` 判断记录）；跨模块另一半（`SkillHost` 订阅后换算
   `CooldownTracker`/`AuraHost`）见 `core/rules/skill/README.md` 同编号条目。测试见
   `core/gameplay/tests/Discrete/TimeModelSwitchTests.cs`
   `SwitchToDiscrete_PublishesTimeModelRescaledEvent_WithReciprocalOfSecondsPerTurn`/
   `SwitchBackToContinuous_PublishesTimeModelRescaledEvent_WithSecondsPerTurn`（验证"确实发出了
   事件、系数正确"这一层跨模块接线；换算数值本身的正确性见
   `core/rules/skill/tests/TimeModelRescaleTests.cs`）。

8. **R04 收口（外部审计 5e779c6，P2，成立）：新增 `gobj.interacted` 事件订阅，直接交互
   `kind=teleporter` 的 gobj（不经 gossip）时补上跨图传送**：`GameObjectHost.DoTeleport`（
   `core/carriers/gobj`，不在本模块写入范围）对跨地图目标正确解析出结果，但只把它经
   `InteractResult.DispatchedRef` 原样返回；`InteractIntentTickHandler`（`CarriersAssembly`
   注册，同样不在本模块写入范围）消费 `"interact"` 意图时只检查 `Success`，从未读取
   `DispatchedRef`——直接交互一个 `kind=teleporter` 的跨地图 gobj（不经 gossip 的 `teleport`
   动作）时，玩家地图/位置完全不变（外部审计复现）。gossip 一侧的 `teleport` 动作（`DialogHost`
   → `teleportRequested` → 上方第 4 条的 `TeleportUnit`）本身没有问题，是不同代码路径。本模块
   写入范围不含 `core/carriers/gobj`，改为在允许改动的本文件里独立订阅 `gobj.interacted`
   （`_bus.Subscribe<GobjInteractedEvent>`）：该事件本身不携带 `DispatchedRef`，按
   `gobjInstanceId` 反查一遍模板，只有 `GobjKind.Teleporter` 才按其 `teleport_target_ref` 重新
   走一遍与 gossip teleport 完全相同的 `TeleportUnit`——同图时 `TeleportUnit` 内部
   `mapIdBeforeMove == resolvedMapId` 判定为真直接返回（幂等，`DoTeleport` 已经做过的
   `SetPosition` 结果一致，不产生第二次场景切换或位置偏移），不需要改动 gobj 模块内部实现。见
   `GameplayAssembly.cs`（`HandleGobjTeleporterInteracted`）、
   `core/gameplay/assembly/tests/GameplayAssemblyGobjTeleportInteractionTests.cs`（跨地图/同图
   两条用例）。
