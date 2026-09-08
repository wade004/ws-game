# L4 玩法层 · loot（掉落）

职责：落地 08_玩法层_掉落任务对话关卡.md 第 1 节 Loot——`loot.table` 抽取（`chance_each`/
`weighted_pick_one`、嵌套引用、条件、保底）、地面掉落物（`DroppedLoot`）的生成/拾取/过期、生物
死亡时自动结算掉落。对应 01 第 L4 模块表 `loot` 行（契约 `LootHost.roll(tableId, context):
List<ItemStack>`、数据表 `loot.table`、事件 `loot.rolled`/`loot.picked_up`）。

依赖：L0（`data_registry`/`event_bus`/`rng`/`expr`/`sim_loop`）、L3（`Core.Carriers.Common` 的
`ItemStack`/`IInventoryHost`/`ILootRoller`）、L2（`core/rules/common` 的 `IUnitAccess`/
`IExprHostFactory`；`core/rules/expr_host.RulesExprSchema.Base`（默认，可由调用方传入合并后的 schema
覆盖）用于解析 `condition` 文本，见
`LootTableParser` 判断记录）、L3 `core/carriers/creature`（`ICreatureTemplateQuery`，仅
`CreatureDeathLootListener` 使用）。经 `Core.Gameplay.csproj` 既有的 `Core.Carriers` 项目引用传递可见，
本模块不新增任何 `ProjectReference`。

## 目录

```
loot/
  README.md
  contracts/
    RollContext.cs             Roll 调用上下文
    ILootHost.cs                LootHost.Roll 契约
    LootOptions.cs              LootPickupPolicy 枚举 + 构造期策略配置
    LootTableDef.cs             LootRollMode/LootEntry/LootGroup/LootTableDef 强类型模型
    LootSchemas.cs               loot.table 的 TableSchema（顶层字段；groups 深层结构不在此声明）
    LootPickupResult.cs         PickUp 返回值 + 失败原因枚举
    Events.cs                    LootEventKeys + LootRolledEvent/LootPickedUpEvent
    LootExprSchemaEntries.cs     空登记表（本模块不新增 Expr 分组/键，见类型注释）
  core/
    LootTableParser.cs           DataRecord -> LootTableDef（运行期与校验期共用）
    LootContentValidationRule.cs 结构/范围校验 + 嵌套引用成环 DFS 检测
    DroppedLootEntity.cs         Entity 子类，Kind="loot"
    LootHost.cs                  ILootHost + ILootRoller 唯一实现（抽取核心 + Drop/PickUp/过期/存档重建）
    LootExpiryTickHandler.cs     挂 TickPhase.TriggerEvaluation，驱动 LootHost.PurgeExpired
    CreatureDeathLootListener.cs 订阅 unit.died，自动结算生物掉落
    DroppedLootPersistable.cs    world.dropped_loot 段（补录，见判断记录）
  tests/
    LootTestSupport.cs           DataRegistry/EventBus/WorldSim/RngHost/Fake 装配帮助
    LootHostRollTests.cs         Roll 核心算法用例
    LootDropPickupTests.cs       Drop/PickUp/过期/死亡联动/事件/持久化用例
```

## 判断记录

1. **随机确定性**：唯一随机源是 `LootOptions.RngStream`（默认 `"loot.roll"`）指定的 `IRngHost` 流，
   按分组/条目登记顺序依次消耗（`chance_each` 每条一次 `Next` + 命中一次 `NextInt`；
   `weighted_pick_one` 每次抽取一次 `Next` + 命中一次 `NextInt`），保证同一 `IRngHost` 内部状态下
   两次独立 `Roll` 调用产生完全相同的结果序列（对应落地方案与分阶段计划.md 第 13 节验收标准 2）。

2. **嵌套 `loot.*` 引用的 `count` 语义**：08 第 1.1 节 `LootEntry.countRange` 字段未说明 `ref` 是
   `loot.table` 时"数量"该如何解释。本模块拍板：`count`（在 `[countMin,countMax]` 内抽出的具体值）
   表示"把该嵌套表整体再抽取 `count` 次"，每次独立走一遍嵌套表自己的分组/保底逻辑，产出物品堆叠全部
   并入外层结果——不是"该嵌套表结果重复 `count` 份"。递归深度上限 `LootOptions.MaxNestedDepth`
   （默认 8），正常内容应已被 `LootContentValidationRule` 的成环检测拦截，运行期只是兜底静默停止。

3. **`guaranteed_min` 的补抽算法**：一张表先按各组正常规则跑完一遍，若产出条目数（每次成功的
   `chance_each` 命中或 `weighted_pick_one` 抽中各算一条，不是最终合并后的堆叠数）仍不足
   `guaranteed_min`，从全表全部条目（跨所有分组、按条件过滤后）组成候选池，按权重（`weight_or_chance`
   本身，无论其所属分组原本是 `chance_each` 还是 `weighted_pick_one`）不放回抽取，直到达标或候选池
   耗尽。

4. **伪随机（`LootOptions.PseudoRandom`）的具体曲线是本模块自行拍板**：08 第 1.1 节原文"（建议）"
   且"是否启用与具体曲线为策略配置项，架构只登记 `guaranteed_min` 作为保底挂载点"——本模块选择
   线性递增：连续 N 次未命中后，下一次有效概率 = `基础概率 × (1 + N × PseudoRandomStep)`（封顶 1）；
   命中后计数清零。计数 key 为 `(RollContext.ContextId, tableId, groupIndex, entryIndex)`，只在本
   `LootHost` 实例内存中累积，**不持久化**（见"不负责什么"）。仅对 `chance_each` 条目生效——
   `weighted_pick_one` 组内"抽中即出"的语义不存在"连续未中"概念。

5. **契约缺口——`IWorldSim.AllocateEntityId` 无法在读档后"设置/推进"计数器**：`AllocateEntityId`
   内部按 `kind` 维护一个只增计数器（见 `core/foundation/sim_loop/core/WorldSim.cs`），不提供任何
   把它推进到指定值的入口；`DroppedLootPersistable.Load` 用 `AddEntity` 直接把存档里的实体（携带
   原有 id）接回世界后，若不推进该计数器，后续真正的新 `Drop` 调用可能分配到与刚恢复实体相同的 id，
   触发 `AddEntity` 的"实体 id 重复"异常。本模块不允许改动 `core/foundation/sim_loop`（其他任务的
   目录），只能在本模块内规避：`LootHost.ReserveLootIdSequenceAtLeast` 反复调用
   `AllocateEntityId("loot")`（每次固定 +1，不产生其它副作用）把计数器推进到严格大于全部已恢复实体
   的最大序号，由 `DroppedLootPersistable.Load` 在恢复完全部实体后调用一次。这一问题不是本模块独有——
   任何"读档时用 `AddEntity` 恢复携带原 id 的实体"的持久化模块（如单位）都会遇到同样的问题，只是本
   模块是第一个在本次任务中显式记录并规避它的。

6. **`world.dropped_loot`/存档段 key 未出现在 `SaveSections`**：`core/foundation/save_system` 不在
   本任务允许改动的范围内。`IPersistable.SectionKey` 契约本身允许任意非空字符串（"自定义段"），
   `DroppedLootPersistable` 直接使用字面量 `"world.dropped_loot"`，不需要改动 `SaveSections`
   即满足任务书"补录"的意图。

7. **`PickUp` 的满包处理用 `IInventoryHost.CountOf` 前后差值判定实际加入数量，而不是只看
   `AddItem` 的布尔返回值**：`IInventoryHost.AddItem` 只返回"是否至少加入了一部分"（`Partial` 策略
   下具体加入了多少不透传），本模块因此在每次 `AddItem` 调用前后各查一次 `CountOf` 算出真实增量，
   据此实现 `LootOptions.FullPolicy=Reject` 的"整体回滚"（用 `IInventoryHost.RemoveItem` 撤销已加入
   的部分）与 `Partial` 的"剩余留在地面掉落物上"，不依赖调用方注入的 `IInventoryHost` 具体实现细节。

8. **`CreatureDeathLootListener` 取死亡单位的 `mapId` 改经 `IWorldSim.GetEntity(unitId)?.MapId`，
   不用 `IUnitAccess.GetMapId`**：`IUnitAccess.GetMapId` 是 C#8 默认接口方法，未 override 时恒返回
   null（见 `core/rules/common/contracts/IUnitAccess.cs` 判断记录），多数既有 `IUnitAccess` 假实现
   未 override 它；`IWorldSim.GetEntity` 是更可靠的取得权威 `MapId` 的方式（`Unit` 本就是 `Entity`
   子类）。若该单位在事件派发时已经从 `IWorldSim` 集合中移除（不应发生，但契约未绝对保证），本
   监听器放弃本次掉落生成而不抛异常，避免阻断死亡结算流程的其它订阅者。

9. **外部审核阻塞项 1 收口（2026-09-07）——`DroppedLootPersistable.Load` 读档前先清空陈旧记录，
   `LootHost.ReattachToWorld` 接进框架自身的 `EnterMap`**：此前 `Load` 只管往 `_dropped`/`_order`
   跟踪表里"增补"（经 `RestoreDropped`），从不清理"当前跟踪、但这次读档的存档快照里已经不再
   提及"的旧记录——复现路径"保存空掉落档 → 产生物品 B → 读取旧档（不含 B）→ B 仍出现"。新增
   `LootHost.ClearDroppedExcept(keepIds)`：`Load` 先解析出整份存档快照要恢复的全部 entityId 集合，
   再一次性清掉不在这个集合里的旧记录（复用既有 `DestroyDropped` 语义：标记世界待销毁 + 立即移出
   跟踪表），随后才逐条 `RestoreDropped`——只清理"不会被这次读档覆盖"的部分，同 id 记录留给
   `RestoreDropped` 既有的"原地覆写/重新 AddEntity"两个分支处理，避免"先销毁再恢复"两步之间出现
   `IWorldSim` 待销毁队列与跟踪表状态不一致的窗口。另外，`ReattachToWorld`（GP-PRES-01 收口新增）
   此前只接进了 `games/_template/Runtime/GameBootstrap.cs`，框架自身的 Shell 读档入口
   （`Presentation.Shell.ShellHost.LoadGame` → `ISceneRouter` → post_load 钩子）从未调用过；改为
   `core/gameplay/assembly.GameplayAssembly.EnterMap`（"一站式进入地图"的既定入口，全部生产宿主的
   post_load 钩子都会调用它）第一步统一调用 `Loot.ReattachToWorld(mapId)`，不再要求每个宿主各自
   记得接线。

10. **CR130-01 根治（外部审计 audit-5c444f1-20260908，P1）：`PickUpReject`（`FullPolicy=Reject`）
    的"整体回滚"改走 `IBatchableInventoryHost` 事务，不再是第 7 条描述的"先 `AddItem`、失败再逐项
    `RemoveItem` 撤销"这一种路径**：`AddItem` 成功那一刻已经把 `item.added` 排入事件总线待发队列
    （`IEventBus.Enqueue` 只入队不立即派发），第 7 条描述的补偿 `RemoveItem` 产生的是另一条独立的
    `item.removed` 事件——某一件放不下、需要回滚前面已经成功加入的堆叠时，二者在同一次
    `DispatchPending` 里先后派发，下游订阅者（如 `core/gameplay/quest.QuestHost.HandleItemAdded` 的
    `consumeOnProgress`）会把先到的 `item.added` 当真、立即消费玩家已有的同模板物品，后到的
    `item.removed` 抵消不了这个副作用——与 `core/gameplay/economy.EconomyHost.Buy` 同款缺口（见该
    模块 README 同编号条目）。`_inventory` 实现 `IBatchableInventoryHost`（`InventoryHost` 已实现）
    时，`PickUpReject` 把整趟拾取尝试（含可能的回滚）包进一次事务：失败时 `using` 块结束触发
    `Dispose`（未 `Commit` 即回滚）把库存状态与缓存事件一并撤销，不需要再调用第 7 条描述的逐项
    `RollbackAdd`；不支持事务的宿主（多数测试用的 Fake，包括本模块自己的 `FakeInventoryHost`——它
    甚至完全不产生事件，见判断记录）退回历史行为。`PickUpPartial` 本身不做任何回滚（"能放多少放
    多少"，保留已落地的部分，不存在"先落地再撤销"的窗口），不受影响。见 `LootHost.cs`
    （`PickUpReject`）、`core/gameplay/loot/tests/CR130_01_PickUpRejectTransactionTests.cs`
    （用真实 `InventoryHost` + 真实事件总线验证：失败拾取后一条 `item.added`/`item.removed` 都不
    应该派发）。

11. **AUD-02 根治（外部审核第九轮，P2，architecture/落地计划/audit-85f1f4f-20260908）：
    `DroppedLootPersistable.Load` 对本段整体缺失（`JsonNull`，如旧格式存档从未写过
    `world.dropped_loot` 段）的处理，从 no-op 改为同第 9 条一样调用 `ClearDroppedExcept`**：第 9
    条的"外部审核阻塞项 1 收口"只覆盖了"本段存在、但数组为空/不含某些 id"这一路径（`Load` 内
    `data is JsonArray` 分支），`data is JsonNull` 分支此前仍是 `return` 直接跳过，不调用
    `ClearDroppedExcept`——修复前真实场景：产生一件地面掉落物后，读一份没有 `world.dropped_loot`
    段的旧格式存档，这件掉落物原样保留，不是"读档=归零重建"应有的行为。现在 `JsonNull` 分支改为
    `ClearDroppedExcept(Array.Empty<Id>())`（空 keepIds，等价于"这次读档没有任何要保留的掉落物"），
    复用第 9 条已有的清理逻辑，不另写一份"清空"分支。见
    `LootDropPickupTests.Load_NullData_ClearsAllTrackedDroppedLoot`。

## 不负责什么

- 不实现难度倍率的具体计算——`CreatureDeathLootListener` 的 `Multiplier` 只是一个
  `Func<double> lootMultiplierProvider` 扩展点，由难度模块（不在本任务范围）注入。
- 不自动向任何 `ISaveSystem` 注册 `DroppedLootPersistable`——是否持久化地面掉落物是
  `LootOptions.PersistDropped` 描述的策略配置项，真正调用 `RegisterPersistable` 是组装层的事
  （惯例同 `core/gameplay/world_state`）。
- 不修复判断记录 5 描述的 `IWorldSim.AllocateEntityId` 契约缺口本身，只在本模块内规避。
- 伪随机计数（判断记录 4）不参与存档——同一局游戏内连续未中的"欠账"读档后清零，这是本模块拍板
  的取舍（架构原文只要求 `guaranteed_min` 本身作为保底挂载点，未要求伪随机状态可持久化）。
