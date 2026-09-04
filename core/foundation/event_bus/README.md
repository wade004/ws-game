# L0 基础层 · event_bus 事件总线

职责：提供全架构统一的发布/订阅事件通道，并管理事件词汇登记表（对应数据表
`found.event_catalog`，见 `04_数据与内容管线.md` 第 1.1 节）。承载全架构一切事件——
从 L2 规则层的 `combat.damage_dealt` 到 L0 自身的 `app.state_changed`——自身不发任何
业务事件（见 `01_分层与依赖.md` L0 模块表 `event_bus` 行）。是 `01` 第 8 节"跨层调用的
三种合法方式"里第二种"事件总线发布订阅"的落地。

依赖：只依赖 `core/foundation/common`（`Id`、`SubscriptionHandle`）与 .NET 标准库；
`core/PlatformEventDiagnostics.cs` 是唯一引用了 `Core.Foundation.EngineAdapter.IPlatform`
的文件，且是**可选**适配器（见下文"诊断"一节），`EventBus` 本身与 `IEventCatalog` 的
默认实现完全不依赖它。不引用 `adapters/` 下任何具体实现，不引用任何 L1 以上模块。

## 派发语义

采用落地方案与分阶段计划.md 第 4.2 节拍板的"同步派发 + tick 末批处理"：

- `Enqueue(evt)`：tick 中间产生的事件先入队，不立即通知订阅者。
- `DispatchPending()`：由 `WorldSim.tick` 第 7 步"事件派发"调用（见 `03_运行时骨架.md`
  第 4.2 节），按入队顺序同步批量派发本 tick 累积的全部事件；派发过程中订阅者又通过
  `Enqueue` 产生的新事件，会在同一次 `DispatchPending` 调用内继续被派发（下一"轮"），
  直到队列清空，或达到 `EventBusOptions.MaxDispatchPasses`（默认 16）——超过时停止本次
  调用并记一条诊断错误，未派发完的事件留在队列里，等待下一次 `DispatchPending` 调用，
  防止事件互相触发造成无限递归导致一次 tick 卡死。
- `PublishImmediate(evt)`：不经过队列，同步立即派发，供非 tick 上下文使用（应用状态机
  `app.state_changed`、场景路由 `scene.load_started` 等，见 `01` 模块表相应行）。

## 订阅

- `Subscribe(key, EventHandler)`：非泛型订阅，处理函数收到 `IEvent`，自行按需要向下转型。
- `Subscribe<T>(key, EventHandler<T>)`：类型化订阅；若某次派发给这个 key 的事件运行时
  类型不是 `T`（同一个 key 理论上不应该混用不同的具体事件类，但总线不做编译期强制），
  按策略跳过这次调用并记一条警告，不影响其它订阅者。
- 同一 key 下多个订阅者按 `Subscribe` 调用顺序被依次调用；某订阅者处理时抛出异常，
  异常会被 `EventBus` 捕获并记入诊断，不影响其它订阅者、也不影响队列——呼应
  `11_工程规范与测试.md` 第 4 节"表现层异常不得影响 WorldSim 的 tick 推进"（表现层订阅者
  多数走这条路径）。
- `SubscriptionHandle.Dispose()` 取消订阅，幂等；派发过程中取消订阅（包括订阅者取消自己）
  是安全的，不会抛异常、不会影响本次正在进行的派发快照。

## 事件目录（`IEventCatalog` / `found.event_catalog`）

- `IEventBus` 的实现要求一个 `IEventCatalog`：`EventBusOptions.StrictCatalog`（默认 true）
  开启时，`Enqueue`/`PublishImmediate` 遇到未登记的事件 key 直接抛
  `InvalidOperationException`，防止拼错的事件 key 悄悄流入运行时；关闭时改为记警告并照常
  处理。
- `EventCatalog.FromDefinitions(IEnumerable<EventDefinition>)` 是本模块提供的内存构造入口。
  **本模块不做 JSON → EventCatalog 的加载**：从 `data/_sample/found/found.event_catalog.json`
  （或具体游戏自己的 `data/<game>/found/found.event_catalog.json`）读取数据表并转换成
  `EventDefinition` 列表，是数据注册表（`core/foundation/data_registry`，T1-4）的职责；
  T1-4 应该调用 `EventCatalog.FromDefinitions` 对接本模块，不应该重新实现一套登记表。
- 字段规范见 `schema/found.event_catalog.md`，其中记录了本表主键字段命名为 `key`
  （而不是通用表的 `id`）的判断依据。

## 诊断（`IEventDiagnostics`）与审计（`IEventAudit`）

- `IEventDiagnostics`：模块内部最小诊断出口（`Warn`/`Error`），不要求 `EventBus` 依赖
  `IPlatform`（引擎适配层 L-1 接口）——事件总线是全架构最基础的通道之一，不应该强制调用方
  总要先备好一个引擎适配层实现才能用。默认实现 `InMemoryEventDiagnostics` 收集到内存列表，
  供测试断言、宿主自行读取展示。可选适配器 `PlatformEventDiagnostics` 把 `Error` 级诊断转发
  到 `IPlatform.ReportCrash`（`IPlatform` 没有区分级别的通用日志方法，只有面向不可恢复错误
  的 `ReportCrash`，因此该适配器里 `Warn` 是空操作，详见类型注释）；只有显式选择这个适配器
  时才会引入对 `IPlatform` 的依赖。
- `IEventAudit`：`EventBusOptions.AuditLog` 为 true 时，`EventBus` 每派发一个事件（无论
  `DispatchPending` 批处理还是 `PublishImmediate` 立即派发）都会调用一次
  `Record(key, sequence)`，序号从 0 起全局递增。默认实现 `InMemoryEventAudit` 收集到内存
  列表。

## 目录

```
event_bus/
  README.md
  contracts/   IEvent.cs EventHandlers.cs EventDefinition.cs IEventCatalog.cs
               IEventDiagnostics.cs IEventAudit.cs EventBusOptions.cs IEventBus.cs
  core/        EventBus.cs EventCatalog.cs GenericEvent.cs
               InMemoryEventDiagnostics.cs InMemoryEventAudit.cs PlatformEventDiagnostics.cs
  schema/      found.event_catalog.md
  tests/       EventBusTests.cs EventCatalogTests.cs
```

## 不负责什么

- 不定义任何具体业务事件类型（`combat.damage_dealt` 携带哪些字段、用什么 C# 类表示），
  那是各自发布方模块（L1~L4）的职责；本模块只提供最小契约 `IEvent` 与一个供测试/过渡期
  使用的弱类型 `GenericEvent`。
- 不做 JSON → `EventCatalog` 的加载（见上文"事件目录"一节），那是 `data_registry` 的职责。
- 不做跨线程/异步派发：全架构要求核心模拟可确定性复现，不引入 `System.Threading`、
  不读系统挂钟时间、不使用系统伪随机数生成器（见 `common/README.md` 同一条约束）。
