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

## `SuppressDispatch`（CORE-170-03 根治，第十轮外部审计，P2，architecture/落地计划/audit-8160178-20260908）

- `IEventBus.SuppressDispatch()` 返回一个 `IDisposable`：在这个作用域内，`Enqueue`/
  `PublishImmediate` 提交的事件被直接丢弃（不进队列、不校验目录、不派发给任何订阅者），
  释放（`Dispose`）后恢复正常派发；支持嵌套调用，按引用计数处理，只有最外层作用域释放才
  真正恢复。
- 动机：`Core.Foundation.SaveSystem.SaveSystem.Load` 回滚失败读档时，会重新调用某些
  持久化段真正的运行时逻辑（如装备段为复用真实联动逻辑会调用真正的"装备"/"卸下"操作），
  这些操作本身会正常派发领域事件（`ItemEquipped`/`StatChanged` 等）；成就系统一类按事件
  计数的消费者会把"读档/回滚期间的重放"误当成一次真实玩家操作再计一次数。"读档不是一次
  业务事件"是本仓库多个模块已经各自声明过的既定原则（`WorldState.Load`/
  `DifficultyHost.Load`/`AchievementHost.Load` 等），本方法把这条原则从"每个消费者各自
  识别是不是重放"下沉成"读档期间产生的事件从一开始就不会到达任何消费者"，不需要给事件
  额外加一个 `IsReplay` 标记、也不需要每个消费者各自过滤。
- 默认实现（`IEventBus` 上的 C#8 默认接口方法）返回一个空操作的 `IDisposable`、不做任何
  抑制：本接口只有 `EventBus` 一个生产实现，其它未来的实现方不因新增本成员而编译失败，
  只是丧失"读档期间抑制事件"这一项能力（退化为改动之前的行为）。
- 只应该在"这段代码内产生的事件确实不该被任何人看到"这类场景使用（当前唯一生产用法就是
  `SaveSystem.Load`）——不要把它当成一个通用的"临时静音"开关滥用到其它不相关的流程里，那
  会让"事件被谁、在哪、为什么丢弃了"变得难以追踪。
- **CORE-180-01 判断记录（第十一轮外部审计，P1，architecture/落地计划/audit-e070e3f-20260908）：
  抑制作用域内被丢弃的不只是业务事件，规则层依赖同一批事件做内部缓存重算（如
  `RulesAssembly` 订阅 `stat.changed` 重算资源池上限、订阅 `progression.state_restored` 重算
  评级换算属性）也会被无差别丢弃，导致读档成功后这些缓存停留在读档前的旧值。** 本模块没有
  为此新增"内部事件白名单"——`stat.changed` 这个 key 同时被内部重算订阅与外部业务/测试订阅者
  共享，`DispatchOne` 按 key 无差别派发，无法只放行前者、继续抑制后者（`core/gameplay/assembly/
  tests/CORE_170_03_SaveRollbackEventSuppressionTests.cs` 已经显式断言排空队列后
  `StatChanged` 不应出现在外部订阅者手里，开白名单会违反这条既有验收）。根治改在
  `core/foundation/save_system` 一侧新增完全绕开事件总线的 `IDerivedStateRebuilder` 回调
  （见该模块 README"CORE-180-01 根治"一节），本模块自身的 `SuppressDispatch` 语义、事件目录、
  派发规则均不受影响。

## 不负责什么

- 不定义任何具体业务事件类型（`combat.damage_dealt` 携带哪些字段、用什么 C# 类表示），
  那是各自发布方模块（L1~L4）的职责；本模块只提供最小契约 `IEvent` 与一个供测试/过渡期
  使用的弱类型 `GenericEvent`。
- 不做 JSON → `EventCatalog` 的加载（见上文"事件目录"一节），那是 `data_registry` 的职责。
- 不做跨线程/异步派发：全架构要求核心模拟可确定性复现，不引入 `System.Threading`、
  不读系统挂钟时间、不使用系统伪随机数生成器（见 `common/README.md` 同一条约束）。

## 事件 key 常量（`generated/EventKeys.g.cs`）

`generated/EventKeys.g.cs` 是**生成物，不可手改**：`toolchain/gen_event_constants.py`
读取 `data/_sample/found/found.event_catalog.json`，为登记表中每一行生成一个
`Core.Foundation.Common.Id` 强类型只读字段（如 `EventKeys.CombatDamageDealt`），外加
一个汇总数组 `EventKeys.All`，供发布方模块按强类型引用事件 key，避免手写字符串拼错。

- 新增/修改事件先按上文"新增事件"一节的流程改登记表，再运行
  `python toolchain/gen_event_constants.py` 重新生成本文件并提交；不要手工编辑
  `generated/EventKeys.g.cs`（脚本头部注释同样声明这一点）。
- 提交门槛可用 `python toolchain/gen_event_constants.py --check` 校验生成文件与登记表
  是否同步，不同步（含忘记重新生成）返回非 0，详见 `toolchain/README.md`。
- `EventKeys` 不是 `IEventCatalog`：它只是强类型的 key 常量表，不做登记/校验/派发；
  把 `found.event_catalog.json` 转成 `EventDefinition` 并接入 `EventCatalog` 仍是
  `data_registry`（T1-4）的职责，`EventKeys` 与之独立，二者的常量名/字段名分别来自
  同一份数据表，理应保持一致。
- 常量名由 key 按 `.`/`_` 切分后各单词首字母大写拼接得到（如
  `skill.cast_start` -> `SkillCastStart`）；若登记表出现两个不同 key 拼出同一个常量名，
  脚本会报错并拒绝生成，需要在登记表层面解决命名冲突。
