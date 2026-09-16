# L4 玩法层 · loot（掉落）

职责：落地 08_玩法层_掉落任务对话关卡.md 第 1 节 Loot——`loot.table` 抽取（`chance_each`/
`weighted_pick_one`、嵌套引用、条件、保底）、地面掉落物（`DroppedLoot`）的生成/拾取/过期、生物
死亡时自动结算掉落；T-N4-7（ADR-0034 决策 3/4）起新增货币掉落条目（`ref` 可为
`econ.currency.*`）与货币入账（拾取不进背包直接入账/击杀即入账）。对应 01 第 L4 模块表 `loot` 行
（契约 `LootHost.roll(tableId, context): List<ItemStack>`、数据表 `loot.table`、事件
`loot.rolled`/`loot.picked_up`）。

依赖：L0（`data_registry`/`event_bus`/`rng`/`expr`/`sim_loop`）、L3（`Core.Carriers.Common` 的
`ItemStack`/`IInventoryHost`/`ILootRoller`）、L2（`core/rules/common` 的 `IUnitAccess`/
`IExprHostFactory`；`core/rules/expr_host.RulesExprSchema.Base`（默认，可由调用方传入合并后的 schema
覆盖）用于解析 `condition` 文本，见
`LootTableParser` 判断记录）、L3 `core/carriers/creature`（`ICreatureTemplateQuery`，仅
`CreatureDeathLootListener` 使用）、L4 `core/gameplay/difficulty`（`IDifficultyHost`，仅
`CreatureDeathLootListener` 使用，T-N2-8b）、L4 `core/gameplay/economy`（`IEconomyHost`，T-N4-7
新增：`LootHost`/`CreatureDeathLootListener` 可选注入，用于货币掉落条目换算数量与入账——同层 L4
互相依赖，先例见对 `IDifficultyHost` 的既有依赖）。经 `Core.Gameplay.csproj` 既有的
`Core.Carriers` 项目引用传递可见，本模块不新增任何 `ProjectReference`（`Core.Gameplay` 是单一
程序集，`Core.Gameplay.Economy`/`Core.Gameplay.Difficulty` 与本模块同在其中，不需要额外的项目
引用即可互相看到）。

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
    LootAnalysisContext.cs       LootTableAnalyzer 求值上下文 + LootExpectedOutcome/LootPseudoRandomKey（见判断记录 14）
  core/
    LootTableParser.cs           DataRecord -> LootTableDef（运行期解析，ADR-0019/F1b 起校验期不再共用，见判断记录 13；
                                  T-N4-7：ref 领域校验放行 econ）
    LootContentValidationRule.cs groups 内登记表达不了的业务判断 + 嵌套引用成环 DFS 检测（ADR-0019/F1b 收窄，见判断记录 13；
                                  T-N4-7：ref 领域校验放行 econ，econ.currency 存在性检查同 item 域惯例）
    DroppedLootEntity.cs         Entity 子类，Kind="loot"
    LootRollCore.cs              抽取核心的纯步骤（条件筛选/权重归一/按阈值选中一条），LootHost 与 LootTableAnalyzer 共用（见判断记录 14）
    LootHost.cs                  ILootHost + ILootRoller 唯一实现（抽取核心 + Drop/PickUp/过期/存档重建；
                                  T-N4-7：新增 12 参数构造重载注入 IEconomyHost，货币掉落条目换算/
                                  拾取直接入账，见判断记录 17）
    LootTableAnalyzer.cs         期望概率分析入口（消费方反馈第 35 条，见判断记录 14）
    LootExpiryTickHandler.cs     挂 TickPhase.TriggerEvaluation，驱动 LootHost.PurgeExpired
    CreatureDeathLootListener.cs 订阅 unit.died，自动结算生物掉落；T-N4-7：新增 8 参数构造重载注入
                                  IEconomyHost，OnKill 策略下击杀即入账，见判断记录 17
    DroppedLootPersistable.cs    world.dropped_loot 段（补录，见判断记录）
  tests/
    LootTestSupport.cs           DataRegistry/EventBus/WorldSim/RngHost/Fake 装配帮助
    LootHostRollTests.cs         Roll 核心算法用例
    LootDropPickupTests.cs       Drop/PickUp/过期/死亡联动/事件/持久化用例
    E35_LootTableAnalyzerTests.cs 期望概率分析对照用例（解析式 vs 蒙特卡洛，见判断记录 14）
    T_N4_7_CurrencyLootTests.cs   货币掉落条目数量公式（2 组手算）、背包满仍入账（2 组，见判断记录 17）
    T_N4_7_CreatureDeathCurrencyDepositTests.cs 死亡结算 OnKill/GroundPickup 两种入账方式策略项
                                  （3 组，见判断记录 17）
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

12. **DATA-DOC-02 文档勘误（第十二轮外部审核，architecture/落地计划/audit-ac3b622-20260909）：
    `LootExpiryTickHandler.Execute` 收到 `SimStepKind.Discrete` 步时跳过本次清理调用的旧注释
    "离散时间模型本项目暂不启用（ADR-0013）"是过期文案，不是当前的真实局部语义**：ADR-0013 离散
    时间模型早已接线（`GameplayAssembly`、`TurnScheduler`）；本处理器只是按设计把"过期销毁"这一
    具体动作收窄到只在连续步推进（惯例同 `core/carriers/summon.SummonTickHandler`），离散步下
    跳过的只是 `LootHost.PurgeExpired` 这一次调用本身，用于判断"是否过期"的绝对模拟时钟
    （`_simTimeProvider`，见 `RulesAssembly.TrackSimTime` 订阅 `sim.tick_started` 累加）在离散步下
    照常累加，不受这一步跳过影响。注释已改正为真实局部语义，不再泛化成"离散时间模型整体不启用"。

13. **ADR-0019 / F1b：`groups[]` 的加载期校验改由 `LootSchemas` 的子结构登记承担，
    `LootContentValidationRule` 相应收窄/退役**：`LootSchemas.Table` 现把 `groups` 登记为 `Item`
    带 `Fields` 的 `FieldSchema`（`roll_mode`/`pick_count`/`entries[]`，`entries[]` 元素再登记
    `ref`/`weight_or_chance`/`condition`/`count_range`，见"子结构登记表"一节），`DataRegistry` 的
    递归结构校验（`required_field`/`field_type`/`expr_parsable`）覆盖了此前经 `LootContentValidationRule`
    委托 `LootTableParser.Parse` 间接报出的全部结构性坏形状——分组/条目不是对象、字段缺失、类型
    不对、`roll_mode` 非法取值、`condition` 解析失败。`LootContentValidationRule` 不再调用
    `LootTableParser.Parse`（运行期 `LootHost` 仍需要它对任意来源做完整解析，不受影响），改为直接
    读取原始 JSON 只保留登记表达不了的五类业务判断：(1) `ref` 领域段必须是 `item`/`loot` 且目标
    记录存在（跨域引用退回 `Id`，见"子结构登记表"一节；存在性检查仿照
    `core/gameplay/spawn.SpawnContentRefRule`"目标表已加载才检查"的宽松惯例，保证只装配 `loot.table`
    单表的既有测试不会因为 `item.template` 未加载而误报——这是本次**新增**的存在性判断，此前
    `ref` 字段既未登记为 `Reference` 也未被任何代码显式核对是否存在，只做过领域段检查，见
    `LootContentValidationRule.cs` 判断记录）；(2) `weight_or_chance` 的合法区间随同一分组的
    `roll_mode` 变化；(3) `count_range.min>=1` 且 `max>=min`；(4) `pick_count>=1`；(5)
    `guaranteed_min>=0`。嵌套 `loot.*` 引用成环检测（DFS）不变，只是改为直接从原始 JSON 容错构建
    `loot->loot` 邻接表（不再依赖 `LootTableDef`）。详见 `LootSchemas.cs`/
    `LootContentValidationRule.cs` 判断记录、`tests/LootSchemaCoverageTests.cs`。

14. **消费方反馈第 35 条收口：`LootHost` 抽取核心的纯步骤收拢到 `LootRollCore`，新增公开
    `LootTableAnalyzer.ExpectedProbabilities` 期望概率分析入口**：反馈原文——`LootHost` 只提供
    "抽一次给我结果"的黑箱接口，`LootTableDef` 只是结构模型；嵌套表、条件、多抽不放回、保底等
    情形的期望概率此前只能复制 `LootHost` 私有抽取语义或蒙特卡洛逼近。处理分两步：(1) 把
    `LootHost` 私有的"条件筛选（`ConditionPasses`）/权重归一（`PickWeighted` 的 `totalWeight`
    求和）/按阈值累加权重选中一条"这几个与随机数无关的纯计算步骤原样（不改变任何一步浮点运算
    顺序）搬到新文件 `core/gameplay/loot/core/LootRollCore.cs`（`internal`，不引用
    `IRngHost`），`LootHost` 改为调用它、只在真正采样处（`PickWeighted` 消耗一次
    `IRngHost.Next` 得到 [0,1) 阈值分数、`RollChanceEachGroup` 消耗一次 `IRngHost.Next` 判定
    命中）注入随机数——`IRngHost` 调用的顺序与次数、每一步的浮点运算逐字节不变，见既有
    `LootHostRollTests`/`LootDropPickupTests` 等 50 条固定种子/精确期望值用例重构前后全部
    不改动断言、原样通过；(2) 新增公开 `core/gameplay/loot/core/LootTableAnalyzer.cs`
    （`LootTableAnalyzer.ExpectedProbabilities(LootTableDef def, LootAnalysisContext context):
    IReadOnlyList<LootExpectedOutcome>`）与 `contracts/LootAnalysisContext.cs`
    （`LootAnalysisContext`/`LootConditionEvaluationMode`/`LootPseudoRandomKey`/
    `LootExpectedOutcome`），与 `LootHost` 共用 `LootRollCore` 的条件筛选/权重归一步骤，解析式
    计算"至少掉落一次的概率"与"期望数量"，按叶子（嵌套 `loot.*` 展开后的 `item.*` 引用）聚合。
    关键判断记录（详见 `LootTableAnalyzer.cs` 类型注释"抽取语义清单"/"精确 / 近似边界"两节）：
    - `chance_each`/`weighted_pick_one` 单抽都是解析式精确值；`weighted_pick_one` 多抽不放回在
      候选池条目数不超过 `LootAnalysisContext.ExactWithoutReplacementMaxEntries`（默认 12）时
      用位掩码动态规划精确枚举全部抽取顺序分支（与 `LootHost.PickWeighted` 逐步"按剩余候选池
      归一权重抽一条、移出、重复"完全同构，含"剩余权重合计 &lt;=0 时整个分支提前停止"这一行为），
      超过阈值退化为"视作放回抽样"的近似估计并标注 `IsApproximate`。
    - `guaranteed_min` 保底：自然产出数（`chance_each` 命中数之和 + `weighted_pick_one` 各组
      确定性选中数）本身是随机变量，用泊松二项分布精确建模；"该条目自然命中"与"该条目被保底
      补抽命中"两件事的合并按条目类型分两种情形，不是笼统的独立近似合并——`chance_each` 直接
      条目自身是否命中直接是"自然产出数"的一个加数、与补抽规模天然相关，用条件概率精确展开
      （"自然命中概率 + 自然未命中概率 × 排除该条目自身贡献后的分布下的补抽命中概率"）；
      `weighted_pick_one` 条目一次抽取固定选出的数量本身不随机、与具体选中哪几条无关，因此该
      条目是否被自然选中与"自然产出数"的分布无关，用边际分布独立合并同样是精确值；只有
      `loot.*` 嵌套条目落在保底候选池时（"自然命中与补抽命中若同时发生，等价于两次独立的嵌套
      子抽取"这一层未展开建模）与"补抽候选池条目数超过精确阈值"两种情形才标注近似。手算校验见
      `E35_LootTableAnalyzerTests.GuaranteedMin_DirectItems_ExactMatchesMonteCarlo` 注释（三
      条目候选池的完整推导过程）。
    - 条件（`LootEntry.Condition`）三种求值模式：`AssumeTrue`/`AssumeFalse`（不需要
      `IExprHost`，全体条件统一视为真/假）、`Evaluate`（需要调用方提供已装配好的
      `IExprHost`，语义与 `LootHost.Roll` 完全一致，复用同一份 `LootRollCore.ConditionPasses`）。
    - 嵌套 `loot.*` 引用解析源由 `LootAnalysisContext.Tables` 提供（调用方传入，通常是
      `IDataRegistryView.GetAll(LootSchemas.Table.Name)` 解析后的结果）；引用了字典里不存在的
      表或递归深度超过 `MaxNestedDepth` 时静默跳过该分支（不贡献概率/期望数量），语义与
      `LootHost.ResolveEntryAtDepth`/`RollTableInto` 运行期兜底完全一致，不抛异常。
    - 分析本身不消耗随机数、不修改传入的 `LootTableDef`/`LootAnalysisContext`、不触碰任何
      `LootHost` 实例状态（`LootTableAnalyzer` 只接受 `LootTableDef` 值对象，从不持有或引用
      `LootHost`），可在任意时刻、任意次数重复调用，见
      `E35_LootTableAnalyzerTests.ExpectedProbabilities_DoesNotAffectUnrelatedLootHostRollSequence`
      （交叉调用不影响无关 `LootHost` 的 `Roll` 序列）。

15. **T-N2-8（ADR-0032 决策 7/8；08 第 1.1 节修订段"三次独立掷骰"；拍板 12）：掉落三次掷骰、带身份的
    掉落结果类型、地面掉落物存读档带身份、`diff.tier.item_level_offset` 转正**：

    - **掷骰顺序与随机数消耗**（唯一随机源仍是 `LootOptions.RngStream`）：既有"掉哪条"掷骰
      （`chance_each` 每条一次 `Next` + 命中一次 `NextInt`；`weighted_pick_one` 每次抽取一次 `Next`
      + 命中后一次 `NextInt`）**原样不变、顺序不变**；`ResolveEntryAtDepth` 把一条候选解析成具体
      产出时，若 `entry.Ref` 是 `item.*`，紧接着（在把这一条结果追加进输出列表之前，早于处理下一条
      候选）按 `LootHost.ResolveItemOutcome` 追加两步：(1) 品质骰——`LootEntry.QualityWeights` 非空
      时消耗一次 `Next` 按权重选中一个品质；为空/未配置时**不掷骰、不消耗随机数**，直接取模板自身
      `item.template.quality`（保证未配置该字段的旧数据行为完全不变，见 `LootHostRollTests`/
      `LootDropPickupTests`/`E35_*`/`CR130_01_*`/`P2_*` 等既有全部固定种子/精确期望值用例——它们的
      `loot.table` 样例均未配置 `quality_weights`，重构前后随机数序列/断言逐字节不变，原样通过）。
      (2) 词缀骰——按**品质骰结果**（不管是掷出来的还是缺省取模板品质）查 `item.quality_definition
      .affix_count`：未登记/`<=0` 时不掷词缀骰、不消耗随机数；否则逐个消耗一次 `Next`，从"
      `item.affix.quality_pool == 本次品质骰结果` 且（模板 `affixes` 白名单非空时与其取交集，缺省
      `[]` 视为不收窄）"的候选池按 `weight` 加权、不放回抽取，候选耗尽提前停止（不是错误）；候选池
      在抽取前按 `Id`（序数字符串）排序，不依赖 `IDataRegistryView.GetAll` 的既有顺序是否稳定（同
      T-N1-2 判断记录"禁止把拓扑序依赖字典枚举顺序，须稳定排序"）。物品等级**不参与掷骰**：
      `RollContext.SourceLevel` 非空时 = `SourceLevel + RollContext.ItemLevelOffset` 的确定性折算，
      为空时为 `null`（消费方回退模板 `item_level`）——"三次独立掷骰"里真正消耗随机数的只有品质骰、
      词缀骰两步，物品等级是来源折算，不是掷骰（见 `LootRollOutcome` 类型注释判断记录）。
    - **投影规则**：新增 `LootHost.RollDetailed(tableId, context): IReadOnlyList<LootRollOutcome>`
      是真正的抽取实现（`RollTableInto` 树遍历产出，不按模板合并，一条候选一条 `LootRollOutcome`）；
      旧 `Roll(tableId, context): IReadOnlyList<ItemStack>` 改为调用 `RollDetailed` 再按模板 id 合并
      投影（`MergeOutcomes`，丢弃品质/词缀，只保留 `TemplateId`/`Count` 求和，惯例与改写前的
      `Merge` 完全一致）——两条路径共用同一份 `RngHost` 消耗（不重复抽取，见
      `T_N2_8_LootRollIdentityTests.Roll_And_RollDetailed_ConsumeSameRngSequence_NotDouble`），旧
      `Roll` 因此在配置了 `quality_weights`/词缀池非空的表上随机数消耗会变化（多了品质骰/词缀骰）——
      这正是拍板 12"回放基线更新"的来源；`ILootRoller`/`ILootHost` 各自新增 `RollDetailed` 作为
      **默认接口成员**，默认实现转发旧 `Roll` 并把每条 `ItemStack` 投影为"未额外指定"的
      `LootRollOutcome`（`QualityId=null`/`Affixes=空`/`ItemLevel=null`，即"回退模板"）——`LootHost`
      对两个接口各自的 `RollDetailed` 均显式覆写（不落回默认值），延迟绑定代理
      `GameplayAssembly.DeferredLootRoller` 同样显式转发到 `_real.RollDetailed`（不经默认实现的
      "转发 Roll 再投影"间接路径，否则会丢失品质/词缀信息），均已过
      `Tests.Presentation.Assembly.InterfaceDefaultMemberForwardingTests` 门禁。
    - **物品等级与实例身份**：ADR-0032 决策 8"物品实例只存……实例 id、模板 id、堆叠数、品质、词缀
      引用"不含物品等级——`ItemInstance`（T-N2-7）与 `EquipmentHost.ApplyAffixValues`/
      `ApplyArmorValue`（T-N2-5）均只认模板自身 `item_level`，不认"来源折算的物品等级"；`RollContext
      .SourceLevel`/`ItemLevelOffset` 折算出的物品等级因此**只出现在 `LootRollOutcome.ItemLevel`
      这一掉落中间结果里**，不写入 `ItemInstance`、也不改变穿戴时的属性重算口径（未改 T-N2-5/T-N2-7
      的任何实现，只是本任务新增的字段不消费到那两处）——设计层裁定（2026-09-15）：采纳（物品等级
      不进实例身份，只在 `LootRollOutcome.ItemLevel`）。
    - **地面掉落物身份**：`DroppedLootEntity` 新增 `Outcomes: IReadOnlyList<LootRollOutcome>`
      （与 `Items` 按下标一一对应，新构造函数重载接受，旧 4/5 参构造函数不变、`Outcomes` 缺省空
      列表）；`LootHost.Drop` 两个重载（旧 `IReadOnlyList<ItemStack>` 签名不变 + 新
      `IReadOnlyList<LootRollOutcome>` 重载）内部都会让 `Outcomes` 与 `Items` 等长——旧签名经新增
      `internal LootHost.ResolveDefaultOutcome` 按模板缺省（品质=模板 `quality`、无词缀、物品等级=
      模板 `item_level`）逐条解析。`DroppedLootPersistable` 存读档：`items[]` 每个元素新增可选
      `qualityId`/`affixes`/`itemLevel` 三个 key（均只在非空/有值时才写）；**旧存档缺这三个 key**
      （T-N2-8 之前的存档格式）按同一先例缺省为`未额外指定`（`QualityId=null`/`Affixes=空`/
      `ItemLevel=null`，不是报错、也不凭空回填模板缺省——消费方需要具体数值时自行按"品质=模板品质、
      物品等级=模板 item_level"回退，见 `LootRollOutcome` 判断记录），与 T-N2-7`ItemInstanceJson`
      "旧存档缺 key → 缺省"是同一惯例但不同落点（`ItemInstance` 场景下确实需要回填模板缺省因为
      `Quality` 字段非空约束；本场景 `Outcomes` 允许字段级可空，因此选择"保持未解析"而不是"提前
      回填"，两种落点均合法，取决于目标类型是否允许可空）。已知缺口：`LootHost.PickUp` 仍按
      `ItemStack` 走 `IInventoryHost.AddItem`（无品质/词缀入参重载），拾取入背包后 `ItemInstance`
      仍是默认品质——地面掉落物的身份只在"落地-存读档"这一段保真，尚未接到拾取入包；`IInventoryHost`
      是并行分支 T-N2-9 的范围，本任务未触碰。**该缺口已由 T-N2-8b 收口**（`IInventoryHost` 新增
      带身份重载 `AddItem(Id,Id,int,Id?,IReadOnlyList<Id>?)`/`TryAddItem(...)`，`LootHost.
      PickUpReject`/`PickUpPartial` 改用带身份重载按 `Outcomes` 入包，见 `core/carriers/item/
      README.md` 判断记录 24、本文件判断记录 16）。
    - **`diff.tier.item_level_offset` 转正**：`DifficultyTierDefinition`/`DifficultySchemas`/
      `IDifficultyHost`/`DifficultyHost` 新增 `ItemLevelOffset`（int，缺省 0），与既有
      `LootMultiplier` 同一取值/消费口径——难度模块只登记与暴露该值，不反向依赖 Loot 模块；调用方
      （如 `CreatureDeathLootListener` 一类掉落发起点）需要自行从 `IDifficultyHost.ItemLevelOffset`
      取值后传入 `RollContext` 的新构造重载，本任务未改动任何现有掉落发起点的接线（不在"涉及文件"
      范围），是留给后续任务/游戏层组装的消费点。`IDifficultyHost.ItemLevelOffset` 按 ABI 硬性规则
      登记为**默认接口成员**（默认值 0，语义同 `LootMultiplier` 默认值 1.0"无难度修正"），唯一实现
      `DifficultyHost` 显式转发真实值。

16. **T-N2-8b（T-N2-8 已知缺口收口，见判断记录 15 末两段；ADR-0032 决策 5/7/8）：`PickUp` 改用
    `IInventoryHost` 带身份重载、`CreatureDeathLootListener` 接线来源等级与难度层物品等级偏移**：
    - **拾取入包携带身份**：`LootHost.PickUpReject`/`PickUpPartial` 新增私有辅助
      `ResolveOutcomesFor`——按下标把 `entity.Items`（`ItemStack`）与 `entity.Outcomes`
      （`LootRollOutcome`）配对（本类自身的 `DropCore`/`RestoreDropped` 两条唯一入口总是让二者
      等长，见 `DroppedLootEntity.Outcomes` 判断记录；仍按下标越界防御性回退到
      `ResolveDefaultOutcome`，覆盖"理论允许但本类从不这样做"的边界情况）——两个方法原先调用
      `_inventory.AddItem(unitId, stack.TemplateId, stack.Count)` 的三处，改为逐条传入
      `outcome.QualityId`/`outcome.Affixes` 调用带身份重载（见 `core/carriers/item/README.md`
      判断记录 24）。`PickUpPartial` 额外把"留在地面上的剩余部分"的 `Outcomes` 同步更新
      （`DroppedLootEntity.ReplaceOutcomes`）——剩余的那部分沿用同一条 outcome、只把 `Count` 改成
      剩余数量（品质骰/词缀骰结果已在 `Drop` 那一刻定型，部分拾取不是重新掷骰）；此前的实现只同步
      `Items`、从不改动 `Outcomes`，若不修会导致"部分拾取后再次查询 `Outcomes`"与实际留在地面的
      `Items` 数量对不上（本次改造前 `Outcomes` 从未在 `PickUp` 内被读取，这个不一致此前不可观测，
      本次改用它之后必须一并修）。
    - **来源等级接线（`CreatureDeathLootListener`）**：新增构造重载（ABI：新增重载，不改既有 6
      参构造函数），末尾追加可选 `IDifficultyHost? difficultyHost`。`OnUnitDied` 改为：
      `sourceLevel = _units.GetLevel(evt.UnitId)`（`IUnitAccess.GetLevel` 是既有必须实现的抽象
      成员，本类已持有 `_units` 引用，未新增依赖）；`itemLevelOffset =
      _difficultyHost?.ItemLevelOffset ?? 0`（未注入难度宿主时恒 0，同该成员默认接口方法语义，
      不抛异常也不跳过掉落生成）。两者都是确定性折算，不消耗任何随机数（见 `RollContext.
      SourceLevel`/`ItemLevelOffset` 判断记录）。`GameplayAssembly` 接线：`Difficulty`
      （`DifficultyHost`）已在 `CreatureDeathLootListener` 构造前一步（第 5 步）构造完成，直接
      传入 `difficultyHost: Difficulty`，不需要像 `healthFractionSetter`/`powers`/
      T-N2-9 的 `statsRef` 那样用闭包延迟回填。
    - **顺带改用 `RollDetailed`/`Drop(outcomes)` 路径（不在任务书字面"只改 sourceLevel/offset"，
      但不改就无法满足"拾取带词缀掉落物后背包实例身份一致"的验收标准，判断记录）**：
      `CreatureDeathLootListener.OnUnitDied` 原先调用旧 `Roll(tableId, context)`（返回
      `ItemStack`，不带身份）+ `Drop(mapId, position, IReadOnlyList<ItemStack>, ownerHint)`（旧
      签名重载，内部经 `ResolveDefaultOutcome` 把每条 `ItemStack` 解析成"模板缺省品质、无词缀"的
      `LootRollOutcome`）——即使 `loot.table` 配置了 `quality_weights`/词缀池，这条路径产出的
      `DroppedLootEntity.Outcomes` 也只会是模板缺省值，真正掷出的品质/词缀在合并成 `ItemStack`
      那一步已经丢失，与上面"拾取入包携带身份"的改动组合起来仍然等价于没做。改为调用
      `RollDetailed(tableId, context)`（返回 `IReadOnlyList<LootRollOutcome>`）+
      `Drop(mapId, position, IReadOnlyList<LootRollOutcome>, ownerHint)`（新签名重载，`Outcomes`
      原样保留调用方给出的品质/词缀）——两条路径共用同一份 `IRngHost` 消耗（见判断记录 15
      "两条路径共用同一份 RngHost 消耗，不重复抽取"），改用 `RollDetailed` 不增加/减少随机数调用
      次数，回放基线不受影响（本类不在 `ReplayWorldBuilder` 触达范围，同判断记录 15 既有核查
      结论）。
    - **测试**：`core/gameplay/loot/tests/T_N2_8b_PickUpIdentityTests.cs`（新增 3 条：拾取带词缀
      掉落物后背包实例 `Quality`/`Affixes` 与掉落时一致、同模板不同品质拾取后不堆叠、Partial 策略
      放不下时留在地面的 `Outcomes` 身份与剩余数量一致）、`core/gameplay/loot/tests/
      T_N2_8b_CreatureDeathSourceLevelTests.cs`（新增 2 条：`RollContext.SourceLevel`/
      `ItemLevelOffset` 正确接线进 `DroppedLootEntity.Outcomes[].ItemLevel`、未注入难度宿主时
      偏移恒为 0）。
    - **回放/Perf 基线核查**：`--filter "FullyQualifiedName~Replay"` 全绿，基线零改动——本任务
      改动的全部代码路径（`LootHost.PickUp`/`CreatureDeathLootListener`）均不在
      `ReplayWorldBuilder`/两份 `*.replay.json` 触达范围（同判断记录 15 既有核查结论：全文不含
      `loot`/`item.template`/`item.quality_definition` 关键字）。

17. **T-N4-7（ADR-0034 决策 3/4；08 第 1.1/7.4 节修订段）：货币掉落条目、当量数量公式、拾取不进
    背包直接入账、入账方式策略项**：
    - **`ref` 放行 `econ` 域**：`LootTableParser.ParseEntry`（运行期，硬性抛异常口径）与
      `LootContentValidationRule.ValidateEntry`（内容校验，同惯例）同步把域名允许集合从
      `{item, loot}` 扩为 `{item, loot, econ}`；报错文案由"必须是 item 或 loot"改为"必须是
      item、loot 或 econ"（`LootSchemaCoverageTests.RefWrongDomain_ReportsLootContentBusinessError`
      同步更新断言文本，行为本身——`creature` 等其它域仍被拒绝——不变）。`econ` 域存在性检查固定
      指向 `econ.currency` 表，惯例同 `item` 域"目标表已加载才检查、未加载视为无法判定"。
    - **当量数量公式**：`count_range` 对货币条目解释为"当量区间"（复用既有 `RollCount` 机制，不
      新增字段）；实际数量 = 当量 × `IEconomyHost.TryGetGoldBaseAmount`（来源等级）×
      `RollContext.Multiplier`（既有难度倍率挂载点，见判断记录 15/`CreatureDeathLootListener`
      既有接线，落地为 `diff.tier.loot_multiplier`），四舍五入（`MidpointRounding.AwayFromZero`）
      到整数，见 `LootHost.ResolveCurrencyOutcome`。
      - **设计层裁定（2026-09-16）：采纳**：08 原文"分档倍率"（`creature.tier_definition` 的
        经验/金币倍率字段）当前不存在（核实见 `core/gameplay/economy/README.md` 同编号判断记录），
        本方法没有可读取的数据源来实现这一乘数，本阶段恒为 1——只保留 `RollContext.Multiplier`
        一项，记为偏离首版基准的说明，留待阶段 N6 仿真核对锚点时补上该字段后由调用方通过某个新的
        `RollContext` 字段/本方法新增可选参数接入（ABI 只允许新增，届时可平滑扩展）。
      - **T-N6-3b 变更记录（补齐）**：`creature.tier_definition` 新增 `gold_multiplier`（缺省
        1）；`RollContext` 新增 7 参构造重载携带 `TierId`（掉落来源单位的分档 id，由
        `CreatureDeathLootListener.OnUnitDied` 与 `lootTableRef` 一并从
        `ICreatureTemplateQuery.Get` 取出后传入）；`LootHost` 新增 14 参构造重载接受
        `LootGoldMultiplierProvider? goldMultiplierProvider`（`(Id? tierId) => double` 窄委托，
        未注入时恒 1，取法与 `Core.Numbers.Progression.ProgressionXpMultiplierProvider` 分档经验
        倍率窄委托注入同一惯例）；`ResolveCurrencyOutcome` 换算数量时在原有基础上再乘一次
        `_goldMultiplierProvider?.Invoke(context.TierId) ?? 1.0`。`core/gameplay/assembly.
        GameplayAssembly` 默认按 `Core.Carriers.Creature.CreatureFactory.TryGetGoldMultiplier`
        接线，未显式配置时对全部分档生效；调用方显式传入自己的委托时装配根不覆盖。至此"分档倍率"
        这一乘数真正接入，不再恒为 1，上一条判断记录留作历史沿革说明。
      - **来源等级为空时回退等级 1——设计层裁定（2026-09-16）：采纳**：08 原文未说明没有来源等级
        （如非生物来源的货币掉落）时该按什么等级取金币基数；本任务选择回退等级 1（曲线定义域的
        合理下界），不是"跳过该条目"（那会让没有来源等级信息的货币掉落表整体失效）。
      - **未注入 `IEconomyHost` 或曲线不可解析时静默跳过该条目**：不产出一条 `LootRollOutcome`
        （不是"数量为 0"——该类型构造函数本就要求 `Count` 为正数），同嵌套 `loot.*` 引用未加载时
        的兜底惯例一致，不抛异常。
    - **货币不进背包，`LootHost.PickUp` 改经 `IEconomyHost.Add` 入账**：`PickUpReject`/
      `PickUpPartial` 把 `entity.Items` 里 `TemplateId.Domain == "econ"` 的堆叠从"需要走
      `IInventoryHost.AddItem` 的堆叠"里剔除，逐条改经新增的 `LootHost.DepositCurrencyStacks`
      调用 `IEconomyHost.Add`（`sourceId` 传本次拾取的地面掉落物实例 id，"谁给的钱"，惯例同
      `EconomyHost.Sell` 的 `sourceId: vendorId`）——货币入账没有"放不下"这个失败分支（`Add` 恒
      返回 `true`），因此不参与 `PickUpReject` 的"全部拿到才算数"事务/回滚判断（`Reject` 策略下，
      若同一次拾取里存在非货币物品且放不下，整次拾取（含货币部分）一并失败——货币此时也不入账，
      是"全部拿到才算数"这一既有语义的自然延伸，不是新缺口；`Partial` 策略下货币恒全额计入
      `taken`，不受非货币物品能否放下影响）；`PickUp` 距离/存在性等既有前置校验不变。
      货币堆叠身份（`QualityId`/`Affixes`）恒为空（`ResolveCurrencyOutcome` 产出），不参与
      `ResolveOutcomesFor` 的品质/词缀分派逻辑，只借用 `LootRollOutcome`/`ItemStack` 现有形状携带
      `TemplateId`（货币 id）与 `Count`（数量）。
    - **入账方式策略项（`EconomyOptions.DepositPolicy`，`CurrencyDepositPolicy.OnKill`/
      `GroundPickup`，默认 `OnKill`）在 `CreatureDeathLootListener` 落地，不在 `LootHost`**：
      "击杀即入账"只在"死亡结算"这个事件点上有意义（`LootHost.Drop`/`PickUp` 本身可能被非死亡
      来源——如箱子、`GobjPendingLoot`——调用，那些场景没有"击杀者"概念），因此把策略判断放在
      `CreatureDeathLootListener.OnUnitDied`：`OnKill` 且 `evt.KillerId` 有值时，先把
      `RollDetailed` 产出的 `outcomes` 按 `TemplateId.Domain` 拆成货币/非货币两份，货币部分立即
      `IEconomyHost.Add(evt.KillerId.Value, ..., sourceId: evt.UnitId)`（"谁给的钱"=死亡的生物
      自己），非货币部分才调用既有 `Drop`；货币部分全部入账、非货币部分为空时不生成地面掉落物
      实体（`Drop` 契约本身总是返回一个真实创建的实体 id，本任务不改变这一契约，直接不调用，
      不是"生成一个空实体"）。**找不到明确击杀者（`evt.KillerId` 为 null，如环境死亡）时的处理，
      设计层裁定（2026-09-16）：采纳**：ADR/08 原文只给出"击杀即入账（默认）"一句，未说明这种边界情形；
      本任务选择整条退回 `GroundPickup` 语义（货币随其它掉落物一并落地，不静默丢弃），不是"归属
      死亡单位自己"或"直接丢弃"——理由是没有击杀者就没有 `OnKill` 策略要求的入账对象，落地待拾取
      是唯一不丢钱的选择。`GroundPickup` 策略下（或未注入 `IEconomyHost`）恒走既有 `Drop` 路径，
      货币混在 `outcomes` 里，落地后由 `LootHost.PickUp` 按上一条处理。
      `CreatureDeathLootListener` 新增 8 参数构造重载（末尾追加 `IEconomyHost? economyHost`，
      同既有 `difficultyHost` 参数一贯做法：新增重载而不是给既有构造函数追加带默认值的参数，见
      判断记录 16"T-N2-8b 新增重载"同款 ABI 判断）。
    - **组装接线（`GameplayAssembly`）**：`EconomyHost` 构造在第 8 步，晚于第 6 步的
      `LootHost`/`CreatureDeathLootListener`——同 `deferredLootRoller` 一贯"延迟绑定代理"惯例，
      新增 `DeferredEconomyHost`（实现 `IEconomyHost`，未绑定期间全部成员静默退化：查询类返回
      中性默认值，写入类返回 `false`，`Buy`/`Sell` 返回"未知商人"失败），第 2 步先占位注入
      `LootHost`/`CreatureDeathLootListener`，第 8 步真正的 `EconomyHost` 构造完成后 `Bind`——
      不改变六个宿主既有的构造/事件订阅顺序（判断记录：本任务未把 `EconomyHost` 构造提前到
      `LootHost` 之前，尽管两者互不依赖、理论上可以重排——重排会牵动 `RewardDispatcher` 的
      `currencyGranter` 延迟闭包写法与全部宿主的事件订阅先后顺序，超出本任务最小改动范围）。
      `DeferredEconomyHost` 对两个新增默认接口成员（`TryGetGoldBaseAmount`/`DepositPolicy`）显式
      转发，已过 `InterfaceDefaultMemberForwardingTests` 门禁。
    - **测试**：`core/gameplay/loot/tests/T_N4_7_CurrencyLootTests.cs`（数量公式 2 组手算；背包
      容量 0 时货币仍入账、混合货币+非货币条目下非货币按既有语义留在地面 1 组；货币单条目全部
      拾完销毁地面实体 1 组）、`T_N4_7_CreatureDeathCurrencyDepositTests.cs`（`OnKill` 有击杀者
      直接入账不落地、`OnKill` 无击杀者退回落地、`GroundPickup` 恒落地，3 组）；
      `core/gameplay/economy/tests/T_N4_7_CurrencyOverflowTests.cs`（见该模块 README）。
    - **回放/Perf 基线核查**：`--filter "FullyQualifiedName~Replay"` 全绿，基线零改动——回放场景
      不触达本任务改动的任一路径（同判断记录 15/16 既有核查结论）。

18. **2026-09-16 深度复审 D-M1/B-S3 判断记录**：
    - **D-M1（必须修）**：`CreatureDeathLootListener.OnUnitDied` 的 `OnKill` 货币入账分支此前只要
      `evt.KillerId.HasValue` 就无条件 `Add` 进 `evt.KillerId` 自己的钱包——不核对击杀者是否为
      玩家、也不做"召唤物击杀归主人"的归属解析（判断记录 17 同批新增的 `CreatureDeathXpListener`
      有这一步，本类当时没有）。生物互殺（击杀者是怪物）时金币静默存入一个通常几秒后就销毁的
      运行期单位钱包，永久遗失；玩家召唤物击杀目标时金币进了召唤物自己的钱包，玩家收不到。新增
      构造重载接受可选 `ISummonHost? summons`（ABI 只增不改，同判断记录 17"T-N4-7 新增重载"款
      ABI 判断）；`OnUnitDied` 在货币入账前先经
      `Core.Gameplay.Common.SummonCreditResolver.ResolveCreditUnit`（与
      `CreatureDeathXpListener.ResolveCreditUnit` 共用的同一份静态辅助，见
      `core/gameplay/common/core/SummonCreditResolver.cs`，避免两处各自维护一份同构逻辑）把
      `evt.KillerId` 解析为记账单位，再核对 `IUnitAccess.GetSourceKind` 是否为
      `SourceKind.Player`——不是玩家（且不归属任何玩家召唤物）时，整条产出（不只是货币条目）
      原样退回 `GroundPickup` 语义，与"击杀者为空"的既有处理口径一致，不静默丢弃。
      `GameplayAssembly.cs` 第 6 步的生产装配调用点同步追加 `summons: Carriers.Summons`（对齐
      `CreatureDeathXpListener` 一贯就有的装配点）。新增测试：
      `T_N4_7_CreatureDeathCurrencyDepositTests
      .OnUnitDied_OnKillPolicy_KillerIsNonPlayerCreature_FallsBackToGroundLoot_
      NotDepositedIntoCreatureWallet`/
      `.OnUnitDied_OnKillPolicy_KillerIsPlayerSummon_CreditsOwnerWallet_NotSummonWallet`；既有
      `OnUnitDied_OnKillPolicy_WithKiller_DepositsCurrencyImmediately_NoGroundLoot` 的夹具补上
      "击杀者显式注册为世界里的 `PlayerUnit`"这一步（此前只是一个裸 `Id`，修复前不核对身份也能
      通过，掩盖了这个缺口）。
    - **B-S3（建议修，已采纳）**：`LootContentValidationRule` 的成环检测 DFS 起点改为按 `Id`
      序数排序后的稳定顺序，不再直接依赖 `Dictionary<Id, List<Id>>` 的枚举顺序（未定义，不同
      进程/哈希种子下可能不同）——数据里同时存在多个独立成环组件时，报出的 `cyclePath` 文本现在
      可重现，同本文件"随机确定性"（判断记录 1）"不依赖 `GetAll` 既有顺序是否稳定"一贯口径。
      新增测试：`LootHostRollTests
      .TwoDisjointCycles_CyclePathText_IsDeterministic_StartsFromSmallestIdInEachComponent`。

## 消费方反馈第 45 条判断记录（2026-09-17）

审计结论：`LootTableAnalyzer.ExpectedProbabilities` 不适用（N/A）——反馈原文点名的问题是"只读
分析入口内部对 `IDataRegistryView` 用严格 `Get`/`GetAll`，registry 阻断态下抛异常"，但
`ExpectedProbabilities` 签名是 `(LootTableDef def, LootAnalysisContext context)`，全文不接受
`IDataRegistryView` 参数、不读 registry（`def`/`context` 都是调用方已经解析好的强类型对象，嵌套
`loot.*` 展开也只查 `context.Tables`——一个调用方自带的内存字典，不是 registry），因此不会有
`EnsureReadable` 抛异常的风险，本条反馈不涉及本模块改动。

## 子结构登记表（ADR-0019 / F1b）

`loot.table.groups` 元素结构（对照 `LootTableParser.ParseGroup`/`ParseEntry` 运行时解析代码）：

**`groups[]`（LootGroup）**

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `roll_mode` | Enum(`chance_each`\|`weighted_pick_one`) | 是 | 对应 `LootRollMode` |
| `pick_count` | Int | 否 | `weighted_pick_one` 下可选多次抽取；`>=1` 是登记表达不了的数值范围约束，保留为业务判断 |
| `entries` | Array\<Object\> | 是 | 见下 |

**`entries[]`（LootEntry）**

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `ref` | Id | 是 | `item.<template>`、`loot.<table>` 或 `econ.currency.<name>`（T-N4-7：ADR-0034 决策 3 货币掉落条目，此时 `count_range` 解释为当量区间，见判断记录 17）；判断记录（退回 `Id`）：目标表随 `Id.Domain` 动态变化（`item` 域→`item.template`，`loot` 域→同一张 `loot.table` 自引用，`econ` 域→`econ.currency`），`FieldSchema.Reference` 只能声明单一目标表/域，表达不了"按值切换目标表"，域名 + 存在性校验保留为 `LootContentValidationRule` 业务判断（惯例同 `core/gameplay/spawn.SpawnContentRefRule`） |
| `weight_or_chance` | Number | 是 | `chance_each`: [0,1]；`weighted_pick_one`: `>=0`——区间随父级 `roll_mode` 变化，登记表达不了，保留为业务判断 |
| `condition` | Expr | 否 | 缺省/未提供表示恒真；判断记录：present 但为空字符串 `""` 时 `LootTableParser` 视同"未提供"（不解析），但登记后 `DataRegistry` 的 `expr_parsable` 校验会对空字符串尝试解析并报错（`ExprParser.Parse("")` 失败）——现有样例数据与测试均未使用空字符串 `condition`，本次不改 `LootTableParser` 迁就这一边缘用法，视为收紧（空字符串本就不是有意义的条件），如后续需要放宽再另行处理 |
| `count_range` | Object | 是 | `{min: Int 必填, max: Int 必填}`；`1<=min<=max` 是登记表达不了的数值范围约束，保留为业务判断 |
| `quality_weights` | Object（Map） | 否 | T-N2-8：`Map<Reference(item.quality_definition), Number>=0>`；键经 `WithMap(MapSchema.ReferenceKeyTable("item.quality_definition", ...))` 做 `reference_integrity` 存在性校验，值经嵌套 `FieldSchema.WithRange(min:0)` 做 `field_range` 非负校验，均是 DataRegistry 原生递归校验（ADR-0024），不再需要 `LootContentValidationRule` 手写重复判断；缺省/空表示不掷品质骰，直接取模板自身品质 |

`guaranteed_min`（顶层，已有）：`>=0` 同属数值范围约束，保留为业务判断。

本模块目前没有需要 `Variants` 的判别字段（`roll_mode` 是分组自身的普通 `Enum`，不分派子字段结构）。
`quality_weights`（T-N2-8 起）是本模块第一个 Map 型（动态键）字段，键引用 `item.quality_definition`。

## 不负责什么

- 不实现难度倍率的具体计算——`CreatureDeathLootListener` 的 `Multiplier` 只是一个
  `Func<double> lootMultiplierProvider` 扩展点，由难度模块（不在本任务范围）注入。
- 不自动向任何 `ISaveSystem` 注册 `DroppedLootPersistable`——是否持久化地面掉落物是
  `LootOptions.PersistDropped` 描述的策略配置项，真正调用 `RegisterPersistable` 是组装层的事
  （惯例同 `core/gameplay/world_state`）。
- 不修复判断记录 5 描述的 `IWorldSim.AllocateEntityId` 契约缺口本身，只在本模块内规避。
- 伪随机计数（判断记录 4）不参与存档——同一局游戏内连续未中的"欠账"读档后清零，这是本模块拍板
  的取舍（架构原文只要求 `guaranteed_min` 本身作为保底挂载点，未要求伪随机状态可持久化）。
- T-N2-8：不把地面掉落物的品质/词缀身份（`DroppedLootEntity.Outcomes`）接进拾取入包路径——
  `PickUp` 仍按 `ItemStack` 走 `IInventoryHost.AddItem`（无品质/词缀入参重载，且该文件属并行分支
  T-N2-9 范围，本任务不得触碰），拾取后 `ItemInstance` 落回默认品质；身份只在"掉落-地面存读档"这一段
  保真（见判断记录 15），拾取入包的品质/词缀传递留给后续任务补齐 `IInventoryHost` 的品质感知重载。
  **（T-N2-8b 已补齐，见判断记录 16"拾取入包携带身份"）**
- T-N2-8：不把 `RollContext.SourceLevel`/`ItemLevelOffset` 接进任何现有掉落发起点（如
  `CreatureDeathLootListener`）——只新增契约字段与 `LootHost` 内部消费逻辑，"谁在死亡结算时传入怪物
  等级/难度偏移"是游戏层组装的事，不在本任务"涉及文件"范围内。
  **（T-N2-8b 已补齐，见判断记录 16"来源等级接线"）**
- ~~T-N4-7：不实现"分档倍率"~~——**T-N6-3b 已补齐**（`creature.tier_definition.gold_multiplier`
  经 `LootGoldMultiplierProvider` 接进 `ResolveCurrencyOutcome`），见判断记录 17"T-N6-3b 变更记录"。
- T-N4-7：不实现"击杀即入账时找不到明确击杀者"以外的任何其它兜底策略（如"归属死亡单位自己"）——
  只实现"退回落地待拾取"这一种，见判断记录 17（设计层裁定（2026-09-16）：采纳）。
- T-N4-7：不改变旧 `Roll(Id, RollContext): IReadOnlyList<ItemStack>`/
  `Drop(Id, Vec2, IReadOnlyList<ItemStack>, Id?)` 两条不带身份的旧签名路径对货币条目的处理——
  经这两条路径产出的货币"堆叠"仍是普通 `ItemStack`（`TemplateId` 落在 `econ` 域），若调用方绕开
  `PickUp`、自行处理这些堆叠（如直接 `IInventoryHost.AddItem`），不会被本任务的货币拦截逻辑覆盖；
  本任务只保证经 `LootHost.PickUp` 这一条唯一拾取入口的货币条目不进背包。
