# L4 玩法层 · economy（货币与商人）

职责：落地 08_玩法层_掉落任务对话关卡.md 第 7 节 Economy——货币（`econ.currency`）余额增减、商人
（`econ.vendor`）购买/出售、限量库存与两种补货策略（`on_map_enter`/`timer`）。对应 01 第 L4 模块表
`economy` 行（契约 `EconomyHost.buy/sell(...)`、数据表 `econ.currency`/`econ.vendor`、事件
`economy.currency_changed`/`economy.item_purchased`/`economy.item_sold`/`economy.vendor_restocked`）。

依赖：L0（`data_registry`/`event_bus`/`expr`）、L3（`Core.Carriers.Common.IInventoryHost`）、L2
（`core/rules/common.IExprHostFactory`；`core/rules/expr_host.RulesExprSchema.Base`（默认，可由
调用方传入合并后的 schema 覆盖）用于解析 `buy_price_rule`）。经 `Core.Gameplay.csproj` 既有的
`Core.Carriers` 项目引用传递可见。

## 目录

```
economy/
  README.md
  contracts/
    CurrencyDef.cs                econ.currency 强类型视图
    VendorDef.cs                  VendorRestockPolicy/VendorSellItem/VendorDef 强类型模型
    EconomySchemas.cs             econ.currency/econ.vendor 的 TableSchema（顶层字段）
    IEconomyHost.cs                契约接口
    PurchaseResult.cs             Buy 返回值 + 失败原因枚举
    SellResult.cs                  Sell 返回值 + 失败原因枚举
    EconomyOptions.cs              收购价默认比例等策略配置
    Events.cs                      EconomyEventKeys + 四个事件类型
    EconomyExprSchemaEntries.cs    player.currency(currencyId): Int 登记
    ChainedExprGroupProvider.cs    player 分组"链式包装"合并帮助类型
  core/
    EconomyDataParser.cs           DataRecord -> CurrencyDef/VendorDef（运行期与校验期共用）
    EconomyContentValidationRule.cs 结构校验 + sell_items.price_currency_id 引用核对
    EconomyHost.cs                  IEconomyHost 唯一实现
    PlayerCurrencyExprGroupProvider.cs  player.currency 的 IExprGroupProvider 实现
    CurrencyPersistable.cs          player.currencies 段
    VendorStockPersistable.cs       world.vendor_stock 段（补录，可选）
  tests/
    EconomyTestSupport.cs           DataRegistry/EventBus/Fake 装配帮助
    EconomyHostTests.cs             Add/TryPay/Buy/Sell/补货/持久化/Expr 求值用例
```

## 判断记录

1. **`buy_price_rule` 的 `self.item_level`/`self.quality` 契约缺口**：08 第 7.2 节原文描述
   `buy_price_rule` 的 `self` 分组给出 `self.item_level`/`self.quality`，但
   `core/rules/expr_host.RulesExprSchema` 的 `self` 分组是"当前单位"视角（`IExprHostFactory.CreateFor`
   的 `selfId`），没有这两个键——它们是"正在出售的物品"的属性，不是单位属性，本任务没有获得任何把
   物品属性接入某个 Expr 分组的契约。本模块按任务书"本版只支持 Expr 返回数值的简单形式"原样落地：
   `buy_price_rule` 存在时经 `IExprHostFactory.CreateFor(unitId, null, null)` 求值取数值结果作为
   "每件收购价"，内容作者目前只能引用出售者自身的既有 `self.*` 字段（如 `self.level`），不能引用物品
   属性——**这是一处记录在案、未解决的契约缺口**，需要新增一个"物品属性"Expr 分组（或扩展
   `self`）才能真正支持，不在本任务修复范围。

2. **收购价的默认公式与货币解析**：没有 `buy_price_rule` 时，收购价 =
   `EconomyOptions.DefaultBuyPricePct（默认 0.25）× 该物品在任一商人的售价`——"任一商人"按 `Id` 序数
   遍历全部已加载 `econ.vendor`，取第一条命中该物品模板的 `sell_items` 项的价格与货币；找不到时收购价
   为 0，货币种类退化为"全局已加载货币里 `Id` 序数最靠前的一种"（一种货币都没有登记视为内容配置错误，
   抛异常）。`buy_price_rule` 存在但该物品在任何商人处都找不到售价时，价格仍按 Expr 求值结果，货币
   种类沿用同一套"任一商人/全局兜底"解析。

3. **`Buy` 的"满包回滚不扣款"顺序**：先只读校验（限量库存、余额），确认余额足够后才调用
   `IInventoryHost.AddItem`；用前后 `CountOf` 差值判定实际加入数量（同
   `core/gameplay/loot.LootHost` 判断记录 7），不足 `count` 时回滚已加入的部分并直接返回失败，**不
   调用** `TryPay`——"背包放不下"这一分支绝不产生货币/库存副作用。

4. **`player` 分组的合并方式选"链式包装"**：任务书"链式包装或说明由组装层用合成提供者，二选一"，
   本模块提供 `ChainedExprGroupProvider`（按顺序尝试多个 `IExprGroupProvider`，某个不认识的 key 约定
   抛 `KeyNotFoundException` 交给链继续尝试下一个）。若组装层更倾向自己写一个按 key 分发的 provider
   （另一个选项），效果等价，`PlayerCurrencyExprGroupProvider` 对不认识的 key 同样抛
   `KeyNotFoundException`，两种组合方式都兼容。

5. **`world.vendor_stock`/`player.currencies` 段 key**：`player.currencies` 已在
   `core/foundation/save_system.SaveSections.PlayerCurrencies` 登记，`CurrencyPersistable` 直接复用；
   `world.vendor_stock`（补录，任务书标注"可选"）未登记，`VendorStockPersistable` 直接用字面量段 key，
   同 `core/gameplay/loot.DroppedLootPersistable` 判断记录，不触碰 `SaveSections`。

6. **AUD-03 修订（外部审核第九轮，P2，architecture/落地计划/audit-85f1f4f-20260908）：
   `VendorStockPersistable` 现按需持久化 `restock_policy=timer` 的倒计时，取代此前"不持久化"的
   判断记录。** 旧判断记录"倒计时读档后从满值重新起算是可接受的简化"只在"读档同时重新构造全新
   `EconomyHost`"场景下成立——那种场景下倒计时反正会在构造函数里按 `restock_timer` 满值初始化，
   读档不读它确实等价于满值起算。但真实探针复现了另一种同样合法的调用方式："原地读档"——同一个
   已经在运行、倒计时已经跑了一段时间的 `EconomyHost` 实例被要求 `Load` 回某个更早的存档点，此时
   `SetStock` 只写 `Remaining`、从不触碰 `TimerRemaining`，读档后倒计时仍是读档前那个正在跑的旧值，
   会让补货比存档快照那一刻应有的时间提前触发（探针：t=2 存档，倒计时剩 8 秒；原地推进 7 秒，剩
   1 秒；读档；再过 1 秒立即补货，而不是应有的"剩 7 秒、不补货"）。现在的实现：`timer` 策略物品
   可选携带 `timer_remaining` 字段（`VendorSellItem.RestockPolicy == Timer` 时才写，其余物品继续
   只写纯数字，不改变旧格式已在用的形状）；`EconomyHost.SetStock` 新增可选参数
   `timerRemaining`——有值时按其写入，为空（旧格式存档没有该字段，或该物品并非 `timer` 策略）时若
   该物品确实是 `timer` 策略，兜底重置为完整周期（`RestockTimer`），不是"保持不动"。见
   `EconomyHost.cs`（`SetStock`/`GetStockTimerRemaining`）、`VendorStockPersistable.cs`、
   `EconomyHostTests.VendorStockPersistable_RoundTrip_RestoresTimerRemaining_OnSameHostReload_
   NotStaleValue`。

6a. **AUD-02 根治（同上轮，P2）：`VendorStockPersistable.Load` 对本段整体缺失（`JsonNull`，如
   只含 meta 的旧格式存档）的处理，从 no-op（保留读档前的运行期库存）改为按内容定义重置为满库存
   （限量物品的 `StockLimit`），`timer` 策略倒计时一并重置为完整周期**——修复前真实探针复现：
   先把库存从 5 改到 2，再加载一份只含 meta 的存档，返回 `Loaded` 但库存仍是 2，违反 10 第 3 节
   "缺失段语义"合同（缺段应清空到默认态，不是保留读档前的残留状态）。见
   `EconomyHostTests.VendorStockPersistable_Load_NullData_ResetsStockToContentDefinedFull`。

7. **`Add`/`TryPay` 的 `sourceId`**：`TryPay` 内部调用 `Add(unitId, currencyId, -amount, sourceId:
   unitId)`——"谁扣的款"就是单位自己（购买/出售场景由 `Buy`/`Sell` 分别决定，`Sell` 收入的
   `sourceId` 是 `vendorId`，`Buy` 扣款的 `sourceId` 是 `unitId` 自己），`IEconomyHost.Add` 本身不做
   任何权限检查（惯例同 `core/gameplay/world_state.IWorldState.Set`"谁能写"）。

8. **N01 收口（外部审核 68c9bed）：`CurrencyPersistable.Load` 是替换语义，不是叠加**——原实现
   假定调用时该单位在 `EconomyHost` 内尚无任何余额记录，用 `Add`（增量）达到"设置为存档值"的效果
   （`0 + savedValue = savedValue`），但这个假定在跨槽读档/运行中重新读同一存档等场景下不成立
   （当前余额可能已经不是 0），实际效果变成"叠加"：存 100、运行中变 80、读档得 180，再读一次得
   280。新增 `IEconomyHost.SetBalance(unitId, currencyId, amount)`（直接替换、按 `Cap` 夹取，不与
   当前余额相加），`CurrencyPersistable.Load` 对快照出现的货币调用 `SetBalance`，对
   `EconomyHost.CurrencyIds` 中快照未出现（`Save` 只写非零余额，缺省即为零）的货币显式置零——
   连续多次 `Load` 同一份快照结果幂等。见 `EconomyHost.cs`（`SetBalance`）、
   `CurrencyPersistable.cs`、`EconomyHostTests.cs`。

9. **CR130-01 根治（外部审计 audit-5c444f1-20260908，P1）：`Buy`/`Sell` 的库存变更改走
   `IBatchableInventoryHost` 事务，不再是"先 `AddItem`、失败再逐项 `RemoveItem` 补偿"**：`Buy` 在
   `added < count`（背包放不下）时此前直接调用 `RollbackAdd`（内部逐项 `RemoveItem`）——`AddItem`
   成功那一刻已经把 `item.added` 排入事件总线待发队列（`IEventBus.Enqueue` 只入队不立即派发），
   补偿的 `item.removed` 是另一条独立事件，二者在同一次 `DispatchPending` 里先后派发时，下游订阅者
   （如 `core/gameplay/quest.QuestHost.HandleItemAdded` 的 `consumeOnProgress`）会把先到的
   `item.added` 当真、立即消费玩家已有的同模板物品，后到的 `item.removed` 抵消不了这个副作用——
   购买失败后库存"数量"确实回滚了，但任务系统已经误判、多扣了一份玩家原有物品（外部审计复现：
   容量 1 的 `Partial` 背包已有 A5，购买失败回滚后变 A4，任务却记了一次消费进度）。`_inventory`
   实现 `IBatchableInventoryHost`（`InventoryHost` 已实现，见 `core/carriers/common/contracts/
   IInventoryTransaction.cs`、`core/gameplay/common/README.md` 同款判断记录——
   `RewardDispatcher.GrantItems` 已经用同一惯例）时，`Buy`/`Sell` 把各自的库存操作包进一次事务：
   失败时 `using` 块结束触发
   `Dispose`（未 `Commit` 即回滚）把库存状态与缓存事件一并撤销，下游完全观察不到这次失败发生过；
   不支持事务的宿主（多数测试用的 Fake）退回历史行为。`Sell` 目前只有一次 `RemoveItem`（成功即
   整份移除、失败不落地任何变化，本身不存在"先落地再补偿"的窗口），仍然包进事务是为了与"购买/
   出售/补偿全部走批量事务"这一统一口径保持一致。见 `EconomyHost.cs`（`Buy`/`Sell`）、
   `core/gameplay/economy/tests/CR130_01_BuyFailureTransactionTests.cs`。

## 不负责什么

- 不解决判断记录 1 描述的 `self.item_level`/`self.quality` 契约缺口本身。
- 不自动向任何 `ISaveSystem` 注册 `CurrencyPersistable`/`VendorStockPersistable`——组装层的事
  （惯例同 `core/gameplay/loot`/`core/gameplay/world_state`）。
- 不实现"组合支付"（`ExtendedCost` 多货币/多物品组合支付，见 08 第 7.3 节对照表"留待后续 ADR"）。
- 不实现拍卖行/交易（08 第 7.3 节"直接裁剪"）。
