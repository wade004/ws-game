# L4 玩法层 · quest（任务）

职责：落地 08_玩法层_掉落任务对话关卡.md 第 2 节 Quest——`quest.def` 定义任务目标/前置条件/奖励、
`unavailable → available → active → objectives_complete → turned_in`（及 `failed`）任务日志状态机、
八种目标类型（`kill｜collect｜interact｜explore｜escort｜event｜cast｜talk`）的事件驱动进度、任务
交付时经 `IRewardDispatcher` 结算奖励。对应 01 第 L4 模块表"任务"行（契约 `QuestHost`、数据表
`quest.def`、事件 `quest.accepted`/`quest.objective_progress`/`quest.completed`/`quest.turned_in`/
`quest.failed`、策略配置项"任务目标类型集合"）。字段表分片见 `schema/quest.def.md`。

依赖（按实际 `using` 语句核实）：

- L0：`Core.Foundation.Common`、`Core.Foundation.Common.Json`、`Core.Foundation.EventBus`、
  `Core.Foundation.Expr`、`Core.Foundation.DataRegistry`、`Core.Foundation.SaveSystem`。
- L1：`Core.Numbers.Progression`（`IProgressionHost`，`PlayerExprGroupProvider` 的 `player.level` 用）。
- L2：`Core.Rules.Common`（`IUnitAccess`、`IExprHostFactory`、`IExprDiagnostics`、
  `RulesEventKeys`/`CarriersEventKeys` 及对应事件类型 `UnitDiedEvent`/`ItemAddedEvent`/
  `ItemRemovedEvent`/`GobjInteractedEvent`/`SkillCastSuccessEvent`，事件驱动进度的订阅源）、
  `Core.Rules.ExprHost`（`IExprGroupProvider` 基接口）。
- L3：`Core.Carriers.Common`（`IInventoryHost`，`collect` 目标计数与交付扣物品用）。
- L4 姊妹模块（同一 `Core.Gameplay` 程序集内跨模块引用，不产生新 `ProjectReference`）：
  `Core.Gameplay.Common`（`IRewardDispatcher`/`RewardBundle`）、`Core.Gameplay.Dialog`
  （`GossipOpenedEvent`/`StoryNodeEnteredEvent`/`DialogEventKeys`，`talk` 目标进度判定用，见判断
  记录 1）、`Core.Gameplay.WorldState`（`WorldExprSchemaEntries`，`QuestExprSchemaEntries.
  BuildParsingSchema` 合并 `world` 分组用）。

## 目录

```
quest/
  README.md
  contracts/
    Events.cs                    QuestEventKeys + 五个事件类型（Accepted/ObjectiveProgress/
                                  Completed/TurnedIn/Failed）
    IQuestHost.cs                 任务系统对外契约（GetState/Accept/UpdateProgress/TurnIn/Fail/
                                  GetLog/GetActiveObjectives/Update）
    QuestDefinition.cs            quest.def 一条记录的内存态表示 + FromRecord 解析（运行期与
                                  校验期共用）
    QuestEnums.cs                  StartMethod/TurnInMethod/Repeatable/State 四枚举 + wire 名互转
    QuestExprSchemaEntries.cs     quest/player 分组的精确签名登记表 + BuildParsingSchema（额外合并
                                  world_state 三键、放宽 event 分组）
    QuestObjective.cs             单条任务目标强类型模型（type 按属性拆解 param）
    QuestObjectiveType.cs         八种目标类型 + targetRef 域名/count==1 规则
    QuestOptions.cs               AllowFail 等构造期策略配置
    QuestProgress.cs              单位对单条任务的持久化进度快照
  core/
    PlayerExprGroupProvider.cs    player 分组的 IExprGroupProvider 实现（level/has_item/item_count）
    QuestContentValidationRule.cs quest.def 内容校验规则（捕获 FromRecord 解析异常）
    QuestExprGroupProvider.cs     quest 分组的 IExprGroupProvider 实现（is_active/is_completed/
                                  is_available/objective_progress）
    QuestHost.cs                   IQuestHost 唯一实现：状态机 + 固定订阅事件驱动进度
    QuestPersistable.cs           player.quest_state 段
    QuestSchemas.cs                quest.def 的 TableSchema
  schema/
    quest.def.md                  quest.def 字段表（本次新增，见"与 08 原文的差异"一节）
  tests/
    QuestHostTests.cs
    TestSupport.cs
```

## 判断记录

1. **Quest 与 Dialog 是一对双向依赖的 L4 姊妹模块**：`DialogHost` 依赖 `IQuestHost`（gossip 菜单的
   `quest_accept`/`quest_turn_in` 两种动作直接调用），而 `QuestHost` 反过来订阅 `Dialog` 模块发出的
   `GossipOpenedEvent`/`StoryNodeEnteredEvent`（判定 `talk` 类目标是否推进）——两个模块互相持有对方
   的类型引用。因为二者同属 `Core.Gameplay` 单一程序集，不产生编译期循环 `ProjectReference` 的问题，
   但确实是一处类型层面的双向耦合，记录在案（`core/gameplay/dialog/README.md` 对称记录同一事实）。

2. **`area.trigger_entered` 事件按非泛型订阅 + `IExprReadableEvent` 反射式读字段处理**：该事件由
   `core/gameplay/area_trigger` 模块发布（与本模块同批次由另一 agent 并行建设，本任务不允许改动该
   目录），本模块不对其具体事件类做编译期依赖，改用 `IEventBus.Subscribe(Id, EventHandler)` +
   按字段名读取 `triggerId`/`unitId`，不要求编译期已知具体事件类型。

3. **事件驱动进度采用"构造期固定订阅一次、每次事件到达时按当前 Active 快照过滤"，而不是
   "接取时动态订阅、交付/失败时取消订阅"**：后者需要为"daily 可重复任务多次接取/交付"这类场景反复
   订阅/取消订阅同一组事件，且要正确处理"同一 unitId 同时持有多条引用同一 target_ref 的不同任务"的
   句柄归属，复杂度明显高于固定订阅方案，且两者性能特征在单机场景下差异可忽略（活跃任务数量级小）。

4. **`ObjectivesComplete → Active` 的反向同步**：`collect` 目标计数变化后按当前实际是否全部达标
   双向同步任务状态——不仅 `Active → ObjectivesComplete`（达标）需要处理，此前已达标的 `collect`
   目标若因物品被移除/卖出而回落，`ObjectivesComplete → Active` 同样需要处理，否则"已达标"会与
   "背包里其实已经没有足够物品"这一实际情况脱节。回落时不发送专门的"取消完成"事件——08 第 9 节
   事件词汇表没有定义这一事件，`quest.objective_progress` 已经足够让订阅方感知数值变化。

5. **`IQuestHost.Update`（驱动 `auto` 起始/交付）不会被 `UpdateProgress` 自动触发**：避免"进度更新"
   这一相对高频操作里隐式发生任务交付这类有较重副作用（结算奖励）的转移；调用方需要按需（如每次
   场景加载、每次事件驱动进度更新之后）显式调用 `Update`。

6. **`quest.def` 的 `title_key`/`description_key`、`QuestObjective.description_key` 三个字段代码
   注释与当前 08 文档表述不一致**：详见 `schema/quest.def.md`"与 08 原文的差异"一节——代码注释称
   这三个字段是"08 原文未列出，任务书拍板补录"，但当前版本 08 第 2.1 节字段表与 `QuestObjective`
   结构原文实际已经收录它们，二者字段集合本身一致，只是代码注释的说法有点滞后（推测是 08 文档在
   本模块实现后又经历过一轮勘误补充）。
7. **GP-01 收口（第四方深度审核）：`QuestPersistable.Load` 是完整替换，不是合并**——原实现只对
   快照里出现的 questId 调用 `RestoreProgress`，从不清理调用前已经 Accept/推进的、快照里没提到的
   任务，空档/旧档读档因此不会清空/回滚任务日志、完成次数、每日记录。现在 `Load` 按快照全量替换
   单位的任务运行期状态（先清空再按快照重建），空快照回到"无任何任务"、快照里没有的任务被移除、
   重复 `Load` 幂等、跨槽读档不残留。见 `QuestPersistable.cs`、`QuestPersistableTests.cs`。
8. **GP-07/N12 收口（第四方深度审核，N12 见 architecture/落地计划/audit-68c9bed-20260907/
   code-review.md）：consume 型 collect 目标按实际成功扣除量推进，不按事件携带的请求量推进；
   跨堆叠扣除按模板 id 而不是单个实例 id**——勘误：consume 型目标的事件驱动路径是
   `item.added`/`HandleItemAdded`（不是 `item.removed`/`HandleItemRemoved`，后者只处理非消耗型
   collect 目标的 `SetObjectiveAbsolute` 重算，见 `HandleItemRemoved`）。原实现无条件按事件携带的
   `take` 推进进度，即使扣除失败也一样，会让同一件物品同时喂饱多个 consume 型目标（GP-07）；且
   原实现只对事件携带的单个 `ItemInstanceId` 调用 `IInventoryHost.RemoveItem`——但
   `InventoryHost.AddItem` 跨堆叠合并新增时，`ItemAddedEvent` 只携带最后一个被触碰的实例 id 与
   合计新增数，若这次新增分散在多个独立堆叠实例上，单实例扣除会因为该实例数量不足而整体失败，
   consume 进度永远推进不了（N12）。现在改用与 `TurnIn` 交付时同款的"按模板 id 跨堆叠扣除"
   （`RemoveCollectedItems`），只有真正扣够完整数量才 `UpdateProgress`，失败（含跨堆叠扣除不足）
   本次不计入任何进度。见 `QuestHost.cs`（`HandleItemAdded` 的 `ConsumeOnProgress` 分支）、
   `QuestHostTests.cs`。
9. **GP-08 收口（第四方深度审核）：非消耗型（`consumeOnProgress == false`）collect 目标接取瞬间
   按当前库存初始化进度**——原实现无条件从 0 起算，只靠后续 `item.added`/`item.removed` 事件被动
   推进；若玩家在接取任务前就已经持有足量目标物品（先攒够材料再接任务是常见玩法顺序），任务会
   错误显示"尚未收集"，要再触发一次物品增减事件才被动纠正。`Accept` 现在对每个非消耗型 collect
   目标立即调用 `SetObjectiveAbsolute(..., _inventoryHost.CountOf(unitId, targetRef))`，可能直接
   达成 `ObjectivesComplete`；消耗型目标语义是"接取后主动上交"，不倒扣已有库存，不受本条影响。
   见 `QuestHost.cs`（`Accept` 方法）、`QuestHostTests.cs`。
10. **N01/N02/N11 收口（外部审核 68c9bed）：`TurnIn` 交付三步原子化，新增 `QuestTurnInFailure`
    失败码**——原实现①货币读档（`CurrencyPersistable.Load`）是叠加不是替换，跨槽/重复读档货币
    翻倍；②满背包（`InventoryFullPolicy.Reject`）时奖励物品发放失败被忽略、任务仍置 `TurnedIn`，
    奖励永久丢失；③同一物品被两个 collect 目标同时依赖时，交付只看 `ObjectiveCounts` 缓存进度、
    不检查实际库存，可能让第二个交付"成功"却没有真实物品可扣。现在 `IQuestHost.TurnIn` 新增
    `TurnIn(unitId, questId, out QuestTurnInFailure failure)` 重载，交付分三步——按实际库存预检
    （不足则 `InsufficientItems`）→ 实际移除（核验返回值，失败回滚）→
    经 `IRewardDispatcher.Grant`（本身也已改为物品奖励原子发放，失败回滚已发放部分并返回
    `false`，见 `core/gameplay/common/README.md`）发放奖励（失败则 `InventoryFull`，回滚本次已
    移除的 collect 物品）——任一步失败都不改变任何状态、任务保持 `ObjectivesComplete`，可以在
    玩家清出背包空间/物品补齐后重试；`CurrencyPersistable.Load` 改为按快照完全替换（含把快照未
    出现的货币显式置零），连续 `Load` 幂等。见 `QuestHost.cs`（`TurnIn`/`RemoveCollectedItems`）、
    `QuestEnums.cs`（`QuestTurnInFailure`）、`core/gameplay/economy/core/EconomyHost.cs`
    （`SetBalance`）、对应测试文件。
    <br>**事件提交边界补充（2026-09-07，外部审计 5e779c6 R01 收口）**："任一步失败都不改变任何
    状态"覆盖的是"数据状态"（背包物品实例/堆叠、货币余额），本条本身不新增任何事件层面的保证。
    步骤 3（`IRewardDispatcher.Grant`）内部现已是事件层面同样原子（见
    `core/gameplay/common/README.md` N02 收口条目"事件提交边界补充"）；但本方法自己在步骤 2/3
    失败时执行的回滚（`foreach (var prior in removed) _inventoryHost.AddItem(...)` 把已移除的
    collect 物品放回背包）是直接调用，不经过同一份批量事务——这次"放回"本身会产生一条新的
    `item.added` 事件，与步骤 2 原始移除产生的 `item.removed` 事件一起进入同一次
    `DispatchPending()` 批次，若玩家对同一物品模板还有另一个 `consumeOnProgress` 目标在监听，
    理论上可能被这条"放回"事件误当作"新获得"而推进进度——这是本次任务书范围之外新发现的一处
    可能的相邻缺口，未改动，留待后续单独复现/修复。
11. **C06 收口（外部审核 7e63d66）：`RemoveCollectedItems` 改为"先核验总量、不够就不碰库存"的原子
    操作**——原实现边遍历边改：数量不足以凑满请求总量时，已经扫到的那部分仍然会被真正移除，只是
    最终返回值是 `false`。两条调用路径各自暴露一种净丢失：路径一，`HandleItemAdded` 的
    `ConsumeOnProgress` 分支里两个目标共享同一模板、一次新增的数量不足以两个目标都拿满时，先处理
    的目标扣满记满，后处理的目标扣到"实际能找到的那部分"却因为凑不满整体失败而不计入任何进度——
    物品被静默消耗但进度没有对应增加。路径二，`TurnIn` 步骤 2 里单个任务的两个目标共享同一模板、
    合计需求超出库存时，第一个目标顺利扣除，第二个目标只够扣到部分就因不足而整体失败，`TurnIn`
    的失败回滚只按 `removed` 列表（"已确认完整移除成功"的目标）放回，第二个目标那部分移除从未被
    记录、永远回不来——"失败了却还丢东西"。现在 `RemoveCollectedItems` 先用 `CountOf` 核验总量是否
    足够，不够直接返回 `false`、不触碰背包任何状态；核验通过后再真正移除，单线程调用下背包状态与
    刚才核验时一致，移除必然能凑满整数量，两条路径都不再出现"部分移除后失败"的中间态——路径一记录
    的总进度精确等于实际消耗量，路径二失败后精确回滚到交付前状态。见 `QuestHost.cs`
    （`RemoveCollectedItems`）、`QuestHostTests.cs`
    （`TurnIn_SingleQuestTwoObjectivesShareSameItem_InsufficientTotal_FailsWithoutLosingAnyItem`、
    `HandleItemAdded_ConsumeOnProgress_TwoQuestsInsufficientForSecond_CreditedProgressMatchesActualConsumption`）。

## 不负责什么

- 不实现"多选一奖励"（08 第 2.4 节"留待后续 ADR"）。
- 不做地图标记/追踪的具体渲染——`GetActiveObjectives` 只给出 `(questId, objectiveIndex, targetRef)`
  三元组，具体位置由调用方按 `targetRef` 对应实体查询，本模块不涉及空间查询（08 第 2.3 节"逻辑层
  只暴露当前激活目标的位置查询接口"）。
- 不做任务分类显示（08 第 2.4 节"属于表现层/UI 归类，不在本表定义"）。
- 不自动向任何 `ISaveSystem` 注册 `QuestPersistable`——是组装层的事（惯例同
  `core/gameplay/loot`/`core/gameplay/world_state`）。
- 不解决 `Core.Gameplay.Common` 判断记录 2 描述的 `CurrencyGranter`/`TalentPointGranter` 契约缺口
  本身——奖励结算全部委托给 `IRewardDispatcher`，本模块只负责在 `TurnIn` 时机调用它。
