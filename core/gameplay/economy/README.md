# L4 玩法层 · economy（货币与商人）

职责：落地 08_玩法层_掉落任务对话关卡.md 第 7 节 Economy——货币（`econ.currency`）余额增减、商人
（`econ.vendor`）购买/出售、限量库存与两种补货策略（`on_map_enter`/`timer`）、价格公式（`econ.value_curve`/
`econ.gold_base_curve`，ADR-0034 决策 2，T-N4-6）、货币掉落条目的金币基数取值与入账方式策略项
（ADR-0034 决策 3/4，T-N4-7——落地实现见 `core/gameplay/loot`，本模块只提供
`IEconomyHost.TryGetGoldBaseAmount`/`DepositPolicy` 两个查询点与超 cap 的 `economy.currency_overflow`
事件）、带 `reason` 的原子扣费 `TryPay(unitId, currencyId, amount, reason)` 与 `economy.charged` 事件
（ADR-0034 决策 5，T-N4-8）、任务/遭遇奖励货币收口到 `IEconomyHost.Add`（`core/gameplay/common.
CurrencyGranters.ViaEconomyHost`，T-N4-8）。对应 01 第 L4 模块表 `economy` 行（契约
`EconomyHost.buy/sell(...)`、数据表 `econ.currency`/`econ.vendor`/`econ.value_curve`/
`econ.gold_base_curve`、事件
`economy.currency_changed`/`economy.charged`/`economy.item_purchased`/`economy.item_sold`/
`economy.vendor_restocked`/`economy.currency_overflow`）。

依赖：L0（`data_registry`/`event_bus`/`expr`）、L3（`Core.Carriers.Common.IInventoryHost`；T-N4-6 起
价格公式按表名字符串读取 `item.template`/`item.quality_definition`/`item.slot_definition` 三张
`Core.Carriers.Item` 表，不引用该模块的强类型 schema 常量，见 `EconomyPriceFormula` 判断记录）、L2
（`core/rules/common.IExprHostFactory`；`core/rules/expr_host.RulesExprSchema.Base`（默认，可由
调用方传入合并后的 schema 覆盖）用于解析 `buy_price_rule`）。经 `Core.Gameplay.csproj` 既有的
`Core.Carriers` 项目引用传递可见。T-N4-7 起被同一程序集内的 `core/gameplay/loot`（同层 L4）反向
依赖——`LootHost`/`CreatureDeathLootListener` 持有 `IEconomyHost` 引用用于货币掉落条目换算/入账，
见该模块 README 判断记录（同层 L4 互相依赖的先例是 `core/gameplay/loot` 对
`core/gameplay/difficulty.IDifficultyHost` 的既有依赖，T-N2-8b）。T-N4-8 起同样被
`core/gameplay/common`（同层 L4，`RewardDispatcher` 所在模块）反向依赖——
`RewardDispatchDelegates.CurrencyGranters.ViaEconomyHost` 引用 `IEconomyHost` 把货币奖励收口到
`Add`，见该文件判断记录。

## 目录

```
economy/
  README.md
  contracts/
    CurrencyDef.cs                econ.currency 强类型视图
    VendorDef.cs                  VendorRestockPolicy/VendorSellItem/VendorDef 强类型模型（T-N4-6：
                                   VendorSellItem 新增 HasPriceAmount + 对应构造重载）
    EconomySchemas.cs             econ.currency/econ.vendor/econ.value_curve/econ.gold_base_curve
                                   的 TableSchema（T-N4-6 新增后两张）
    IEconomyHost.cs                契约接口（T-N4-7：新增默认接口成员 TryGetGoldBaseAmount/DepositPolicy；
                                   T-N4-8：新增默认接口成员 TryPay(Id,Id,long,string) 带 reason 重载，
                                   默认转发旧签名、不发事件；T-N4-11：新增默认接口成员
                                   TryGetSellItemPrice(Id,Id): long?，默认返回 null）
    PurchaseResult.cs             Buy 返回值 + 失败原因枚举
    SellResult.cs                  Sell 返回值 + 失败原因枚举
    EconomyOptions.cs              售价比例（重定位）、价值曲线 id、偏离警告阈值等策略配置（T-N4-6）；
                                   T-N4-7 新增 GoldBaseCurveId、DepositPolicy（CurrencyDepositPolicy
                                   枚举，OnKill/GroundPickup，同文件顶层类型）
    Events.cs                      EconomyEventKeys + 六个事件类型（T-N4-7 新增
                                   economy.currency_overflow/CurrencyOverflowEvent；T-N4-8 新增
                                   economy.charged/EconomyChargedEvent，两条事件本次登记进
                                   found.event_catalog.json 并重生成 EventKeys.g.cs）
    EconomyExprSchemaEntries.cs    player.currency(currencyId): Int 登记
    ChainedExprGroupProvider.cs    player 分组"链式包装"合并帮助类型
  core/
    EconomyDataParser.cs           DataRecord -> CurrencyDef/VendorDef（运行期解析，ADR-0019/F1b 起校验期不再共用，见判断记录 10；T-N4-6：price_amount 可选解析）
    EconomyContentValidationRule.cs sell_items 内登记表达不了的业务判断（ADR-0019/F1b 收窄，见判断记录 10）
    EconomyPriceFormula.cs         T-N4-6 新增：econ.value_curve 价格公式（基准价值 = 曲线 ×
                                   品质价格倍率 × 槽位价格系数，value_override 优先），运行期与
                                   校验期共用；T-N4-7 新增 TryComputeGoldBaseAmount（econ.gold_base_curve
                                   按等级求值，供 EconomyHost.TryGetGoldBaseAmount 转发）
    EconomyPriceDeviatesFormulaRule.cs T-N4-6 新增："手填价格偏离公式"警告
                                   （检查名 econ_price_deviates_formula，设计层裁定
                                   （2026-09-16）：采纳）
    EconomyHost.cs                  IEconomyHost 唯一实现（T-N4-6：Buy/Sell 缺省走价格公式；T-N4-7：
                                   Add 超 cap 发 currency_overflow，SetBalance 不发；显式覆写
                                   TryGetGoldBaseAmount/DepositPolicy 两个默认接口成员；T-N4-8：显式
                                   覆写 TryPay(Id,Id,long,string) 为真正的原子扣费 + 发 charged 事件，
                                   旧 TryPay(Id,Id,long) 反过来转发新签名传占位 reason，见判断记录 14；
                                   T-N4-11：Buy 内联单价解析抽为私有 ResolveSellItemPrice，显式覆写
                                   TryGetSellItemPrice 复用同一方法，见判断记录 15）
    PlayerCurrencyExprGroupProvider.cs  player.currency 的 IExprGroupProvider 实现
    CurrencyPersistable.cs          player.currencies 段
    VendorStockPersistable.cs       world.vendor_stock 段（补录，可选）
  tests/
    EconomyTestSupport.cs           DataRegistry/EventBus/Fake 装配帮助
    EconomyHostTests.cs             Add/TryPay/Buy/Sell/补货/持久化/Expr 求值用例
    T_N4_6_EconomyPriceFormulaTests.cs 价格公式（含 value_override 优先）、售价比例、偏离警告
                                   正负例、NonEscalatable、新表 schema 覆盖；T-N4-11 新增
                                   TryGetSellItemPrice 三组（走公式/手填优先/商人或物品未知返回 null）
    T_N4_7_CurrencyOverflowTests.cs Add 超 cap 丢弃并发 currency_overflow（2 组：discards+emits、
                                   within-cap 不发）、SetBalance 夹取但不发该事件
    T_N4_8_TryPayReasonAndRewardCurrencyTests.cs TryPay(reason) 原子性 2 组（成功扣费发 charged、
                                   余额不足不扣不发）、旧签名转发新签名同样发事件 1 组、
                                   RewardDispatcher 经 CurrencyGranters.ViaEconomyHost 发放货币
                                   奖励真正经 IEconomyHost.Add 入账 1 组、工厂方法拒绝 null 1 组
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

2. **收购价的默认公式与货币解析（T-N4-6 起改写，ADR-0034 决策 2）**：没有 `buy_price_rule` 时，
   售价 = `EconomyPriceFormula.TryComputeBaseValue`（`econ.value_curve(item_level)` × 品质价格倍率 ×
   槽位价格系数，`item.template.value_override` 优先整体取代）算出的基准价值 ×
   `EconomyOptions.DefaultBuyPricePct`（重定位为"售价比例"，字段名/默认值 0.25 不变）；公式算不出
   （本 `IDataRegistryView` 未加载 `item.template`/`econ.value_curve`，如既有隔离测试场景、
   `CR130_01_BuyFailureTransactionTests` 把 `item.template` 登记进另一个独立 `DataRegistry` 实例）
   时回退 T-N4-6 之前的旧口径——`该物品在任一商人的手填售价`（"任一商人"按 `Id` 序数遍历全部已加载
   `econ.vendor`，取第一条命中该物品模板且 `VendorSellItem.HasPriceAmount` 为真的 `sell_items` 项）。
   货币种类解析口径不变：命中优先，找不到退化为"全局已加载货币里 `Id` 序数最靠前的一种"（一种货币都
   没有登记视为内容配置错误，抛异常）。`buy_price_rule` 存在时价格仍按 Expr 求值结果（硬性规则：
   禁止改该表达式语义），货币种类沿用同一套解析，不受价格公式介入影响。

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

10. **ADR-0019 / F1b：`sell_items[]` 的加载期校验改由 `EconomySchemas` 的子结构登记承担，
    `EconomyContentValidationRule` 相应收窄/退役**：`EconomySchemas.Vendor` 现把 `sell_items`
    登记为 `Item` 带 `Fields` 的 `FieldSchema`（`item_id`/`price_currency_id`/`price_amount`/
    `stock_limit`/`restock_policy`/`restock_timer`，见"子结构登记表"一节），`DataRegistry` 的递归
    结构校验（`required_field`/`field_type`/`reference_integrity`）覆盖了此前经
    `EconomyContentValidationRule` 委托 `EconomyDataParser.ParseVendor`/`ParseCurrency` 间接报出
    的全部结构性坏形状——这两个解析方法此前报出的问题其实早就与 `econ.currency`/`econ.vendor`
    顶层字段（`id`/`name_key`/`cap`/`display_ref`/`buy_price_rule`/`map_id`，F1b 之前就已登记）
    的结构校验重复，是本次一并收口的既存缺口，不是 F1b 新引入的问题。`price_currency_id` 改登记为
    `Reference(econ.currency)`（判断记录见 `EconomySchemas` 类型注释：`EconomyTestSupport.MakeRegistry`
    与生产装配都总是把两张表登记进同一个 `DataRegistry` 实例一起加载，`reference_integrity` 能可靠
    工作）后，此前手写的"核对 `price_currency_id` 是否命中已加载的 `econ.currency` 记录"业务判断
    随之整条退役。`EconomyContentValidationRule` 不再调用 `EconomyDataParser`，改为直接读取原始
    JSON 只保留登记表达不了的三类业务判断：`price_amount>=0`、`stock_limit>=0`（提供时）、
    `restock_policy=timer` 时 `restock_timer` 必须提供且 `>0`（条件必填 + 数值范围的复合约束，
    `VariantSchema` 要求判别字段必填而 `restock_policy` 本身可选、缺省即 `VendorRestockPolicy.None`，
    不适用）。`item_id` 保持 `Id`（未新增存在性校验，见"子结构登记表"一节判断记录）。详见
    `EconomySchemas.cs`/`EconomyContentValidationRule.cs` 判断记录、
    `tests/EconomySchemaCoverageTests.cs`。

11. **T-N4-6：`VendorSellItem.PriceAmount` 从必填改可选，走"新增属性 + 新增构造重载"而不是改
    既有属性类型（如 `long?`）**：硬性规则 5（ABI 只允许新增）——把既有 `long PriceAmount { get; }`
    属性的类型改成 `long?` 会同时破坏源码兼容（旧调用方 `long x = item.PriceAmount;` 编译失败）与
    二进制兼容（属性访问器方法签名变化）。改为新增 `HasPriceAmount`（bool）属性 + 一个新增
    七参数构造重载（显式接受 `hasPriceAmount`），旧六参数构造函数原样保留、内部转发并恒把
    `HasPriceAmount` 置真——旧调用方（含 `EconomyHostTests` 既有全部用例）不做任何改动，行为逐位
    不变。`price_amount` 未在 JSON 里出现时，`EconomyDataParser.ParseSellItem` 用新构造重载传入
    `priceAmount: 0`（占位，不参与任何计算）、`hasPriceAmount: false`。

12. **T-N4-6：价格公式算不出时的两套独立兜底口径，不是同一份"缺省值"**：`EconomyHost.Buy`
    （买价）与 `ComputeSellPrice`（售价）在 `EconomyPriceFormula.TryComputeBaseValue` 返回 `null`
    （本 `IDataRegistryView` 未加载 `item.template`/`econ.value_curve`——既有隔离测试场景的常态）
    时各自选了不同的兜底：`Buy` 直接按 0 处理（未填 `price_amount` 又算不出公式值，视为内容配置
    缺口，运行期不阻断只兜底）；`ComputeSellPrice` 回退 T-N4-6 之前的旧口径（"该物品在任一商人的
    手填售价 × 售价比例"），是刻意为之——保证 `EconomyHostTests.
    Sell_UsesDefaultBuyPricePercentOfVendorSellPrice_AndPublishesEvent` 等既有全部用例（其登记的
    最小测试 registry 从不加载 `item.template`）逐位不变，不需要为了新公式重写既有回归用例。
    `EconomyPriceDeviatesFormulaRule`（内容校验）则是第三套口径：算不出直接跳过该条记录、不产生
    告警，因为没有公式值就无从比较偏离——三处口径不同是"运行期要有个数、宁可粗糙也不抛异常"与
    "校验期没有依据就不下判断"两种场景的正常分歧，不是实现疏漏。

13. **T-N4-7（ADR-0034 决策 3/4；08 第 1.1/7.4 节修订段）：`IEconomyHost` 新增两个默认接口成员
    支撑 `core/gameplay/loot` 的货币掉落条目，`Add` 新增超 cap 丢弃事件，`SetBalance` 刻意不发**：
    - `TryGetGoldBaseAmount(int level): double?`——委托 `EconomyPriceFormula.
      TryComputeGoldBaseAmount`（`EconomySchemas.GoldBaseCurve` 按 `EconomyOptions.GoldBaseCurveId`
      取该等级金币基数），曲线未加载/该 id 找不到记录时返回 `null`。默认接口成员默认返回 `null`
      （"无曲线数据"的中性默认值），唯一实现 `EconomyHost` 显式覆写；`Core.Gameplay.Assembly.
      GameplayAssembly` 内部延迟绑定代理 `DeferredEconomyHost` 同样显式转发（不落回默认值），已过
      `Tests.Presentation.Assembly.InterfaceDefaultMemberForwardingTests` 门禁。
    - `DepositPolicy: CurrencyDepositPolicy`——转发 `EconomyOptions.DepositPolicy`（新增枚举
      `OnKill`/`GroundPickup`，默认 `OnKill`，与 ADR 原文"击杀即入账（默认）"一致）。供
      `core/gameplay/loot.CreatureDeathLootListener` 判断死亡结算时是否把货币产出直接入账给击杀者
      （`OnKill`）还是让货币随其它掉落物一并落地、拾取时才入账（`GroundPickup`）——本模块只登记
      策略取值与两个中性枚举成员，不实现"击杀"这个概念本身（那是 Loot 模块的事，见该模块 README
      判断记录）。
    - `Add` 使某单位某货币余额被夹到 `CurrencyDef.Cap` 之上而丢弃超出部分时，新增发 `economy.
      currency_overflow`（`CurrencyOverflowEvent{unitId, currencyId, discarded}`，`discarded = raw
      - cap`）；`SetBalance`（"读档等以快照为准场景，整体替换余额"语义）即便结果同样被夹到 cap，
      也**不**发——ADR/08 第 7.4 节原文明确区分（"`SetBalance` 不发"），读档不应该把存档快照记录
      时刻已经真实发生过的溢出事件在这里重放一次。判断依据 `raw`（未夹取前的余额和）而不是最终
      `newValue`：`raw > cap` 才是"真的顶到上限之上"，`newValue` 还可能因为下限夹取（扣款到负数
      再夹回 0）而与 `old` 不同，两者不是同一件事、不能混用同一个判断条件。
    - **设计层裁定（2026-09-16）：采纳**：08 原文"怪物掉钱 = 当量 ×
      `econ.gold_base_curve`(怪物等级) × 分档倍率 × `diff.tier.loot_multiplier`"里的"分档倍率"
      指 `creature.tier_definition` 的经验/金币倍率字段——核实该表（`core/carriers/creature/core/
      CreatureSchemas.cs`）当前只有 `stat_multiplier`/`control_immune` 等既有字段，没有任何"经验/
      金币倍率"字段（T-N4-4"分档与难度经验倍率"已落地但只补了 `xp_multiplier`，未新增任何"金币
      倍率"字段，本任务不依赖它），本模块与 `core/gameplay/loot` 均未新增这一乘数的读取，本阶段
      恒为 1，只保留 `RollContext.Multiplier`（`diff.tier.loot_multiplier`）一项——记为偏离首版
      基准的说明，留待阶段 N6 仿真核对锚点时补上该字段后再接入。
    - **登记记录（未同步 `found.event_catalog.json`/`EventKeys.g.cs`）**：核实
      `toolchain/gen_event_constants.py --check` 只比较该登记表与已提交的生成文件两者自身是否
      一致，不反查代码里手写的 `Id` 事件常量，本次不登记、不重生成不会让该门禁失败；分阶段落地
      计划把"两条新经济事件（`economy.charged`/`economy.currency_overflow`）登记 `found.
      event_catalog` 并重生成常量"整体列为 T-N4-8 的任务范围（T-N4-8 依赖 T-N4-7），本次不提前
      处理，避免与该任务重复改动同一份登记表产生合并冲突。
    - 测试：`tests/T_N4_7_CurrencyOverflowTests.cs`（`Add` 超 cap 丢弃+发事件、`Add` 未超 cap 不发、
      `SetBalance` 超 cap 夹取但不发，3 例）；`core/gameplay/loot/tests/T_N4_7_CurrencyLootTests.cs`/
      `T_N4_7_CreatureDeathCurrencyDepositTests.cs`（消费端集成用例，见该模块 README）。

14. **T-N4-8（ADR-0034 决策 5；08 第 7.4 节修订段"tryCharge/TryPay(unitId, currencyId, amount,
    reason)——余额足够时一次性扣除并发 economy.charged{unitId, currencyId, amount, reason}；不足时
    不扣、不发、返回 false"）：`TryPay` 带 reason 重载、旧签名转发决策、任务奖励货币收口、两条
    经济事件登记收口。**
    - `IEconomyHost` 新增默认接口成员 `TryPay(Id unitId, Id currencyId, long amount, string
      reason): bool`（ABI 硬性规则"只允许新增，禁止删除旧 TryPay"）。默认实现转发旧无 reason
      三参数签名、**不**发 `EconomyChargedEvent`（"调用方没有走带 reason 的新契约，也就不去凑一个
      假 reason 发一条新事件"，与 `TryGetGoldBaseAmount`/`DepositPolicy` 同一惯例）；唯一生产实现
      `EconomyHost` 显式覆写为真正的原子扣费（先只读校验余额是否足够，足够才一次性经 `Add` 扣除，
      不产生"扣了一部分"的中间态）+ 成功时发 `EconomyChargedEvent`。`core/gameplay/assembly.
      GameplayAssembly.DeferredEconomyHost` 显式转发该成员，已过
      `Tests.Presentation.Assembly.InterfaceDefaultMemberForwardingTests` 门禁。
    - **判断记录（旧无 reason 三参数签名要不要也发事件，本次的核心判断）**：`EconomyHost.
      TryPay(Id,Id,long)` 改为直接调用带 reason 的新签名、传入占位 `"unspecified"`——效果是旧签名
      从本次改动起也会在成功扣费时发 `economy.charged`。这不是"顺手带上"，是对照 ADR-0034 决策 5
      原文做出的判断：原文把"原子扣费"描述成唯一一种操作（"tryCharge……余额足够则扣并发
      economy.charged 事件返回真"），没有定义"发生扣费但不发事件"的另一分支；`Add` 早已是同样口径
      ——不管调用方传的 `sourceId` 是否携带业务语义，只要余额真的变化就发 `currency_changed`，"这次
      调用有没有额外标注意图"从不影响"账本事件该不该发"。旧签名与新签名在本类型里向来是同一份底层
      "检查余额、原子扣除"操作，只是历史上（T-N4-6 及更早）没有 reason 标签可发；补上"缺省 reason
      也发事件"后，返回值与对余额本身的副作用逐位不变（既有 `EconomyHostTests.TryPay_*` 用例不依赖
      "不发事件"，全部保持通过），只是让全部既有调用点——本类型 `Buy` 内部扣款（本次改为显式传
      `"vendor_buy"`，不吃占位默认值）、`GameplayAssembly.DeferredEconomyHost` 代理、任何外部直接
      持有 `IEconomyHost` 引用调用旧签名的调用方——从这次改动起也能在事件总线上观察到扣费发生。
      与"接口默认接口成员的默认值（转发旧签名、不发事件）"是两个不同层次的判断，互不矛盾：接口
      默认值面向"没有显式覆写的组合/包装实现该退化成什么"，`EconomyHost` 是唯一生产实现，可以且
      应该给出比中性默认值更正确的真实行为。
    - **任务奖励货币收口到 `IEconomyHost`**：`core/gameplay/common/contracts/
      RewardDispatchDelegates.cs` 新增 `CurrencyGranters.ViaEconomyHost(IEconomyHost)` 静态工厂，
      构造一个把货币奖励经 `IEconomyHost.Add` 入账的 `CurrencyGranter`。`CurrencyGranter` 委托类型
      本身未删除/未改写（ABI"只允许新增"，且 `RewardDispatcher` 既有构造参数与
      `GameplayAssembly` 现有 `currencyGranter` 闭包都依赖它继续存在）——`GameplayAssembly` 现有
      闭包本就直接调用 `EconomyHost.Add`（同一效果），未切换到本工厂：任务书要求 `GameplayAssembly.
      cs` 本次"只加行"，且切换纯属风格统一、不产生行为差异，留给后续任务收敛，不在 T-N4-8 范围内。
      验收用 `core/gameplay/economy/tests/T_N4_8_TryPayReasonAndRewardCurrencyTests.
      RewardDispatcher_GrantCurrency_ViaEconomyHost_DepositsThroughAdd` 验证：用本工厂构造的
      `CurrencyGranter` 注入 `RewardDispatcher`，`Grant` 后货币真正落地在 `EconomyHost` 的余额表里
      （经 `Add`，触发既有 `currency_changed` 账本事件），不是绕开经济宿主的旁路。
    - **两条经济事件登记收口**：`economy.charged`（`fields: [unitId, currencyId, amount, reason]`）
      与 T-N4-7 遗留的 `economy.currency_overflow`（`fields: [unitId, currencyId, discarded]`）一并
      登记进 `data/_framework/found/found.event_catalog.json`，跑 `python
      toolchain/gen_event_constants.py` 重生成 `core/foundation/event_bus/generated/
      EventKeys.g.cs`（新增 `EconomyCharged`/`EconomyCurrencyOverflow` 两个常量，`--check` 通过）。
      `EconomyEventKeys`（本类型手写的 `Id` 常量）与生成的 `EventKeys` 并存，值逐字相同——前者是
      本模块内部长期以来的既有惯例（订阅/发布都引用它），后者供不方便直接依赖
      `Core.Gameplay.Economy` 的外部消费方使用，改哪一套的引用来源属于超出本任务范围的重构。
      `core/foundation/data_registry/tests/DataRegistryTests.cs` 里硬编码的 `found.event_catalog`
      行数断言（`88 -> 90` 的既有注释）随之更新为 `90 -> 92`。
    - 测试：`tests/T_N4_8_TryPayReasonAndRewardCurrencyTests.cs`（原子性 2 组：成功扣费发
      charged、余额不足不扣不发；旧签名转发新签名同样发事件 1 组；`RewardDispatcher` 经
      `IEconomyHost.Add` 入账 1 组；工厂方法拒绝 null 1 组）。

15. **T-N4-11（阶段 N4 独立复核缺口修复；ADR-0034 决策 2 同一条价格解析路径）：展示层单价接入
    价格公式，补齐 T-N4-6 遗留的一处显示缺口。**
    - 缺口：T-N4-6 把 `sell_items[].price_amount` 改为可选、未填时 `EconomyHost.Buy` 按价格公式
      算买价，但 `Presentation.Ui.ShopViewModel.Refresh` 一直直接读 `VendorSellItem.PriceAmount`
      填充 `VendorSellItemSnapshot`——`HasPriceAmount=false` 时该字段恒为占位 0（见判断记录 11），
      T-N4-10 把 `data/_sample/econ/econ.vendor.json` 的 `item.sample_tonic` 条目摘掉
      `price_amount` 后，展示层（含 `adapters/unity` 的 `ShopPanel`）会显示错误的 0 价，实际扣款
      （`Buy`）却正确按公式算出非零单价——两者不一致。
    - 修法：`IEconomyHost` 新增默认接口成员 `TryGetSellItemPrice(Id vendorId, Id itemId): long?`
      （默认返回 `null`——接口自身没有暴露"某商人登记了哪些出售条目"这份静态清单，无法算出任何
      有意义的值，中性退化，同 `TryGetGoldBaseAmount`/`DepositPolicy` 惯例）；`EconomyHost` 显式
      覆写，内部把此前内联在 `Buy` 里的单价解析表达式（`HasPriceAmount ? PriceAmount : 按公式算，
      算不出按 0 处理`）抽成私有 `ResolveSellItemPrice(VendorSellItem)`，`Buy`（乘 `count`）与
      `TryGetSellItemPrice`（单价本身）共用该方法，保证"实际扣款单价"与"展示层读到的单价"永远
      出自同一条路径，不会再出现两者不一致的缺口。`GameplayAssembly.DeferredEconomyHost` 显式
      转发该成员，已过 `Tests.Presentation.Assembly.InterfaceDefaultMemberForwardingTests` 门禁。
    - `ShopViewModel.Refresh` 改为 `_economy.TryGetSellItemPrice(vendorId, sellItem.ItemId) ??
      sellItem.PriceAmount` 填充 `VendorSellItemSnapshot.PriceAmount`（`??` 兜底防御性写法，不假设
      具体实现细节——本视图模型持有的 `(vendorId, itemId)` 恒取自 `EconomyHost.GetVendorDef` 返回
      的 `def.SellItems` 本身，唯一生产实现理论上恒能命中，不会真的走到 `null` 分支）；`ShopPanel`
      （`adapters/unity`）本身不改，仍读 `VendorSellItemSnapshot.PriceAmount`，随上游修复自动生效。
    - 回归安全：改动前 `Buy` 的单价计算是 `(long)((公式值 ?? 0.0) * count)`（先乘 `count` 再取整），
      抽出的 `ResolveSellItemPrice` 是"先取整单价再乘 `count`"——两种顺序在 `count=1`（既有全部
      `Buy_UsesFormula_*`/`Buy_PrefersValueOverride_*`/`Buy_PrefersExplicitPriceAmount_*` 用例）与
      `HasPriceAmount=true`（既有全部 `count>1` 用例，整数乘法不受顺序影响）下逐位等价，唯一理论
      差异窗口（`count>1` 且未填 `price_amount` 且公式值非整数）在既有测试数据集里不存在实例，
      不产生任何既有断言的行为变化。
    - 测试：`tests/T_N4_6_EconomyPriceFormulaTests.cs` 新增 `TryGetSellItemPrice_*` 三组（未填
      price_amount 走公式、填了原样返回、商人/物品未知返回 null）；PlayMode
      `UiSuiteTests.Shop_OpenVendor_ShowsStockAndPrice_BuyChangesInventoryAndCurrency` 断言从旧的
      手填值 5 改为公式值 25（`econ.value_curve.default(1)`=25 × 品质 1.0 × 槽位 1.0），随
      T-N4-10 摘掉该条目 `price_amount` 之后的真实行为同步更新，不是放宽断言。

## 子结构登记表（ADR-0019 / F1b）

`econ.vendor.sell_items` 元素结构（对照 `EconomyDataParser.ParseSellItem` 运行时解析代码）：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `item_id` | Id | 是 | `item.<template>`；判断记录：层次上 `item.template`（L3）在 `econ.vendor`（L4）之下、可以 `Reference`，但本模块自身测试装配（`EconomyTestSupport.MakeRegistry`、`CR130_01_BuyFailureTransactionTests`）历来把 `item.template` 登记进另一个独立 `DataRegistry` 实例、与 `econ.vendor` 不在同一份加载结果里，登记为 `Reference` 会因"目标表在本 registry 里从未加载"对现有全部测试数据误报，因此退回 `Id`；此前也从未校验过其存在性，本次未新增（与 loot `ref` 的处理不同，是刻意保持行为不变，不是遗漏） |
| `price_currency_id` | Reference(`econ.currency`) | 是 | 同模块同层，两张表总是一起加载，交给 `reference_integrity` 检查项 |
| `price_amount` | Int | **否（T-N4-6 起，此前是）** | 分阶段落地计划 T-N4-6（ADR-0034 决策 2）：改为可选——未填时 `EconomyHost.Buy` 按价格公式算出买价，`VendorSellItem.HasPriceAmount` 为假、`PriceAmount` 占位为 0。填了则 `>=0` 是登记表达不了的数值范围约束，保留为 `EconomyContentValidationRule` 业务判断；与公式偏离超带宽报警告，见下方"手填价格偏离公式"一节 |
| `stock_limit` | Int | 否 | 缺省不限量；`>=0`（提供时）同上保留为业务判断 |
| `restock_policy` | Enum(`on_map_enter`\|`timer`) | 否 | 缺省 `VendorRestockPolicy.None`（无自动补货），对应运行时三态枚举中除 `None` 外的两个字符串字面量 |
| `restock_timer` | Number | 否 | `restock_policy=timer` 时必须提供且 `>0`——条件必填 + 数值范围的复合约束，登记表达不了，保留为业务判断 |

## 价格公式与"手填价格偏离公式"（分阶段落地计划 T-N4-6；ADR-0034 决策 2；08 第 7.4 节）

```
基准价值 = econ.value_curve(item_level) × item.quality_definition.price_multiplier × item.slot_definition.price_coefficient
          （item.template.value_override 存在时整体取代基准价值本身）
买价     = 基准价值（sell_items[].price_amount 手填时以手填为准）
售价     = 基准价值 × 售价比例（EconomyOptions.DefaultBuyPricePct，重定位，buy_price_rule 存在时以其为准）
```

新增 `econ.value_curve`（物品等级 → 基准价值）、`econ.gold_base_curve`（等级 → 金币基数，本任务只
登记 schema，消费留给 T-N4-7 掉落货币条目/任务金币）两张断点表（04 第 3.6 节通用曲线形态，
`curve_monotonic_finite` 自动覆盖）。共用算法见 `EconomyPriceFormula`（运行期 `EconomyHost.Buy`/
`ComputeSellPrice` 与内容校验 `EconomyPriceDeviatesFormulaRule` 共用同一份实现，不重复）。

新增校验规则 `EconomyPriceDeviatesFormulaRule`（检查名 `econ_price_deviates_formula`——04 第 5 节该行
原文未给出具体检查名，设计层裁定（2026-09-16）：采纳本任务暂按此拟定的检查名；`NonEscalatable = true`，同组既有警告"抓意图
不抓手滑"口径）：核对 `sell_items[].price_amount`（手填买价）与 `item.template.value_override`
（手填基准价值）各自与纯公式值（忽略 `value_override`）的偏离比例，超过 `EconomyOptions.
PriceDeviationWarningThreshold`（缺省 0.2，同 `ItemWeaponDamageDeviatesDpsCurveRule` 既定阈值类推）
报 Warning；未填 `price_amount` 的条目没有"手填值"可比较，不参与该分支检查。

判断记录：`item_id` 存在性未登记为新的业务检查（见上表），与 `core/gameplay/loot` 对 `ref` 新增
"目标表已加载才检查"的宽松存在性判断不同——这不是遗漏，而是"运行时解析代码为唯一依据"（04 第
3.2 节）的直接结果：`EconomyDataParser`/`EconomyHost` 从未检查过 `item_id` 是否存在，任务书对
economy 模块也未像 loot 那样明确要求补上这一判断，因此本次不新增超出既有行为的校验，避免范围
蔓延。若后续需要，可参照 loot `ref` 存在性检查的写法（"目标表已加载才判定存在性，未加载视为
无法判定、不报告"）原样添加。

本模块目前没有需要 `Variants` 的判别字段（`restock_policy` 是普通可选 `Enum`，不是子对象判别
字段），也没有 Map 型（键为任意字符串、值同构）字段——`sell_items` 是定长键的固定结构 Object 数组，
不适用 ADR-0019 首批范围外的 Map 型契约扩展条款。

## CORE-170-03 根治（第十轮外部审计，P2，architecture/落地计划/audit-8160178-20260908）

`CurrencyPersistable.Load`/`VendorStockPersistable.Load` 修复前都是边解析边直接调用
`EconomyHost.SetBalance`/`SetStock` 修改运行期状态——同一次 `Load` 调用里，排在后面的条目格式
非法时，排在前面的条目已经把新值写进了真实 `EconomyHost`，抛异常后这些已提交的写入不会回滚，
形成"部分是新存档值、部分还是读档前旧值"的半新半旧中间态，与 `Core.Carriers.Item.
EquipmentPersistable.Load` 曾经的同一类缺陷成因相同（见 `core/carriers/item/README.md` 同编号
判断记录）。根治后两者都先完整解析校验成临时恢复计划，只有整份数据校验通过才一次性提交。见
`Tests.Gameplay.Economy.CORE_170_03_CurrencyAndVendorStockPersistableLoadFailureTests`。

## 不负责什么

- 不解决判断记录 1 描述的 `self.item_level`/`self.quality` 契约缺口本身。
- 不自动向任何 `ISaveSystem` 注册 `CurrencyPersistable`/`VendorStockPersistable`——组装层的事
  （惯例同 `core/gameplay/loot`/`core/gameplay/world_state`）。
- 不实现"组合支付"（`ExtendedCost` 多货币/多物品组合支付，见 08 第 7.3 节对照表"留待后续 ADR"）。
- 不实现拍卖行/交易（08 第 7.3 节"直接裁剪"）。
- T-N4-7：不实现"击杀即入账"/"掉在地上"这两种入账方式本身的落地机制——本模块只登记
  `EconomyOptions.DepositPolicy` 策略取值与 `IEconomyHost.DepositPolicy`/`TryGetGoldBaseAmount`
  两个查询点供消费方读取，真正"死亡结算时直接入账"或"生成地面掉落物、拾取时入账"是
  `core/gameplay/loot`（`CreatureDeathLootListener`/`LootHost`）的事，见该模块 README。
- T-N4-7：不实现"分档倍率"（`creature.tier_definition` 的经验/金币倍率字段）——该字段尚未登记
  （见判断记录 13），本模块的金币基数曲线只按等级求值，不叠加任何分档乘数。
