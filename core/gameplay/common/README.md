# L4 玩法层 · common（奖励包与奖励分发契约）

职责：本目录是 01_分层与依赖.md 第 4 节"实现级共享目录"登记的一条（`core/gameplay/common`（奖励包与
奖励分发契约）），不对应 01 第 L4 模块表里的某一行——它没有自己的数据表、没有自己的事件，只提供一份
被多个 L4 玩法模块共同复用的"奖励包"结构与结算契约：`RewardBundle`（08_玩法层_掉落任务对话关卡.md
第 2.1 节 `quest.def.rewards` 字段 `{items, xp, currency, skills, world_flags, talent_points}` 结构，
第 4.1、6.1 节分别注明 `encounter.def.rewards`、`achv.def.rewards`"同 `quest.def.rewards` 结构"）与
`IRewardDispatcher`（把一份 `RewardBundle` 结算到具体单位身上，供 `quest`/`encounter`/`achievement`
在各自的完成/达成时机调用，避免三个模块各自重写一遍"按字段逐项发放"的样板逻辑）。此外还提供
`ExprValueJson`（`ExprValue` 与 `JsonValue` 之间的编码约定）与 `RewardSchemaFields`（`rewards` 字段的
`FieldSchema` 帮助方法），供 `rewards` 结构的三个使用方内容表登记与 JSON 编解码复用。

依赖（按实际 `using` 语句核实）：

- L0：`Core.Foundation.Common`（`Id`）、`Core.Foundation.Common.Json`（`JsonObject`/`JsonArray`/
  `JsonBool`/`JsonNumber`/`JsonString`）、`Core.Foundation.Expr`（`ExprValue`/`ExprValueKind`）、
  `Core.Foundation.DataRegistry`（`FieldSchema`/`FieldKind`，`RewardSchemaFields` 用）。
- L1：`Core.Numbers.Progression`（`IProgressionHost`，`RewardDispatcher` 的经验发放依赖）。
- L3：`Core.Carriers.Common`（`IInventoryHost`/`ItemStack`，物品奖励发放/解析依赖）、
  `Core.Carriers.Item`（`SkillGranter` 委托类型，技能奖励发放依赖）。
- L4 姊妹模块：`Core.Gameplay.WorldState`（`IWorldState`，`world_flags` 奖励项的发放依赖）——同一个
  `Core.Gameplay` 程序集内的跨模块引用，不产生新的 `ProjectReference`。

经 `Core.Gameplay.csproj` 既有的 `Core.Carriers` 项目引用传递可见，本模块不新增任何 `ProjectReference`。

## 目录

```
common/
  README.md
  contracts/
    ExprValueJson.cs            ExprValue<->JsonValue 编码约定（惯例照抄 world_state.WorldState.ToJson/
                                 FromJson，抽出供 RewardBundle 的 world_flags 奖励项与
                                 core/gameplay/dialog 的 set_flag 动作 params.value 共同复用）
    IRewardDiagnostics.cs       RewardDispatcher 的诊断出口（某类奖励依赖未注入时警告并跳过）
    IRewardDispatcher.cs        奖励分发契约：Grant(unitId, bundle, sourceId)
    RewardBundle.cs             奖励包强类型模型 + FromRecord 解析（items/xp/currency/skills/
                                 world_flags/talent_points 六类奖励）
    RewardDispatchDelegates.cs  CurrencyGranter/TalentPointGranter 两个契约缺口委托
    RewardSchemaFields.cs       rewards 字段的 FieldSchema 帮助方法，供 quest.def/encounter.def/
                                 achv.def 三处内容表登记复用，避免各自重复拼写
  core/
    InMemoryRewardDiagnostics.cs  IRewardDiagnostics 默认实现（警告收进内存列表，供测试断言）
    RewardDispatcher.cs         IRewardDispatcher 唯一实现：六类奖励分别转发给各自的宿主依赖
  tests/
    RewardDispatcherTests.cs    Grant 六类奖励发放/依赖缺失跳过用例
```

## 判断记录

1. **`RewardBundle` 放在 `core/gameplay/common` 而不是 `quest` 模块内部**：08 第 2.1、4.1、6.1 节三处
   分别描述 `quest.def.rewards`/`encounter.def.rewards`/`achv.def.rewards`，结构完全相同（"同
   `quest.def.rewards` 结构"），01 第 4 节也把本目录明确登记为"奖励包与奖励分发契约"的共享落点——
   三个使用方模块只依赖本模块的强类型结构与解析逻辑，不需要互相依赖或重复实现一遍同样的
   JSON 解析/校验代码。

2. **`CurrencyGranter`/`TalentPointGranter` 是契约缺口的委托绕过**：架构未给货币系统（08 第 7 节
   Economy，与本任务并行由另一 agent 建设）与天赋点发放定义共享契约接口，`RewardDispatcher`
   按委托签名绕过（惯例同 `core/carriers/item` 的 `SkillGranter`"契约缺口用模块内委托绕过并
   汇报"）——这两个契约缺口本身不由本模块解决，见"不负责什么"。

3. **`RewardDispatcher` 的六类依赖全部是可选构造参数，任一为 null 时对应奖励记诊断跳过、不抛
   异常**：调用方可以只注入自己关心的子集（例如只测试物品发放时不必构造一个真实
   `IProgressionHost`）；`RewardBundle` 里某类奖励本就为空时，即便对应依赖也是 null，也不产生
   警告——只有"确实有这类奖励要发但发不出去"才值得警告。

4. **`ExprValueJson` 的 `Id` 用 `{"$id": "..."}` 包装以区别于 `String`**：与
   `core/gameplay/world_state.WorldState.ToJson/FromJson` 使用同一套编码约定（Bool/String 原生映射，
   `Int` 用不带小数点的原始文本、`Number` 用必带小数点的原始文本区分），本类型把这套约定单独抽出
   成独立工具类，避免 `RewardBundle.WorldFlags` 与 `core/gameplay/dialog` 的 `set_flag` 动作
   `params.value` 各自手抄一遍同样的编解码逻辑（第三处复用点）。

5. **N02 收口（外部审核 68c9bed）：`IRewardDispatcher.Grant` 返回 `bool`，物品奖励原子发放**——
   原实现无论 `IInventoryHost.AddItem` 是否成功都视为发放完成（`void` 返回），背包已满
   （`InventoryFullPolicy.Reject`）时物品奖励静默丢失，`QuestHost.TurnIn` 仍照常把任务置为
   `TurnedIn`，玩家永久拿不到这份奖励也无法重新交付。现在 `GrantItems` 最先执行，某一项 `AddItem`
   失败时回滚已经成功发放的物品项，`Grant` 返回 `false` 且不再继续发放其余类别（xp/货币/技能/
   世界标志/天赋点没有"容量不足"这类失败模式，物品先行发放、失败即整体中止已经覆盖"全部生效或
   全部不生效"）。`QuestHost.TurnIn` 据此在 `Grant` 返回 `false` 时把本次交付已经扣除的 collect
   物品放回背包、任务保持 `ObjectivesComplete`，不误置 `TurnedIn`（见
   `core/gameplay/quest/README.md` 对应条目）。见 `IRewardDispatcher.cs`、`RewardDispatcher.cs`
   （`GrantItems`/`RemoveByTemplate`）、`RewardDispatcherTests.cs`。
   <br>**C05 收口（外部审核 7e63d66）限定回滚精确性的实际保证范围**——上一段"回滚可以精确进行"
   此前的表述基于"`AddItem` 本就要么整份加入、要么完全不变"，这只对 `InventoryFullPolicy.Reject`
   成立；`InventoryFullPolicy.Partial` 下 `AddItem` 可能只加入一部分仍返回成功（该策略本身的既有
   语义），若回滚仍按"请求量"移除，会越过实际落地量、多删到这批发放之前就已经存在的同模板堆叠
   （即"发奖失败却把玩家原有物品也删了"）。现改用 `IInventoryHost.TryAddItem`（`out actualCount`，
   见 `core/carriers/common/contracts/IInventoryHost.cs`）取得每一项的实际落地量，回滚按实际量
   而不是请求量移除。**实际保证边界**：`Reject` 策略下失败=整批原子不生效（背包状态与调用前完全
   相同）；`Partial` 策略下失败=整批回滚到"这次 `Grant` 调用之前"的状态（不多不少，但不代表
   `Grant` 调用中途任何一步的中间态都不可观察——回滚发生在 `Grant` 内部同一次调用返回之前，调用方
   看不到中间态）。见 `RewardDispatcherTests.cs` 里 `Grant_PartialPolicy_...`/`Grant_RejectPolicy_...`
   两条真实 `InventoryHost` 用例。
   <br>**事件提交边界补充（2026-09-07，外部审计 5e779c6 R01 收口，见变更记录）**：上一段"背包状态
   与调用前完全相同"此前只覆盖 `IInventoryHost` 的物品实例/堆叠这份"数据状态"，不覆盖
   `item.added`/`item.removed` 这两个事件本身——`InventoryHost.AddItemCore`/`RemoveItem` 早已把
   事件 `Enqueue` 进总线待处理队列（`Enqueue` 只入队不立即派发），失败回滚只是按实际落地量再
   `RemoveItem` 补一条独立的 `item.removed`，两条事件仍在同一次 `DispatchPending()` 里被当作两个
   真实事件先后派发——`QuestHost.HandleItemAdded`（`consumeOnProgress`）会在先到的 `item.added` 上
   立即消费玩家已有的同模板物品并推进任务进度，后到的 `item.removed` 抵消不了这个副作用（外部审计
   GP26-01 复现："失败发奖的已排队 item.added 仍会消耗旧任务物品"）。现在 `InventoryHost` 实现
   `IBatchableInventoryHost.BeginBatch()`：`GrantItems` 若判定失败则把整批发放期间产生的事件一并
   丢弃（不只是回滚数据状态），失败时下游不会收到任何 `item.added`/`item.removed`；只有整批成功
   才会按序补发全部事件。"背包状态与调用前完全相同"现在应理解为"数据状态与已派发事件两者都与调用
   前完全相同"。见 `core/carriers/common/contracts/IInventoryTransaction.cs`（新增
   `IInventoryTransaction`/`IBatchableInventoryHost`）、`core/carriers/item/core/InventoryHost.cs`
   （事务实现）、`RewardDispatcher.cs`（`GrantItems` 经检测能力接口套用事务）、
   `RewardDispatcherTests.cs`（`Grant_RejectPolicy_SecondItemFails_RollsBackQueuedEventsToo_R01`）。

## 不负责什么

- 不实现"组合支付"（`ExtendedCost` 多货币/多物品组合支付）与"多选一奖励"（08 第 2.4 节
  `rewards.items` 扩展为"多选一"分组结构，原文"留待后续 ADR"）——本模块只落地当前单一固定结构。
- 不解决判断记录 2 描述的 `CurrencyGranter`/`TalentPointGranter` 契约缺口本身（架构未给货币/
  天赋点系统定义共享接口），只提供委托绕过。
- 不知道"何时该发奖励"——不订阅任何事件、不持有任何触发时机的判断逻辑，`IRewardDispatcher.Grant`
  由 `quest`/`encounter`/`achievement` 各自在完成/达成时机主动调用。
- 不对外暴露任何数据表（`quest.def`/`encounter.def`/`achv.def` 各自的 `TableSchema` 由使用方模块
  自己声明），本模块的 `RewardSchemaFields.Rewards` 只是登记 `rewards` 字段本身的帮助方法，`rewards`
  内部结构的校验在解析期由 `RewardBundle.FromRecord` 完成，不是 `TableSchema`/`FieldSchema` 能表达
  的嵌套形状。
