# L0 基础层 · app_lifecycle 应用生命周期与状态机

职责：驱动应用级主状态（启动/菜单/加载/世界内/暂停）流转与 InWorld 内部子状态栈（见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L0 模块表 `app_lifecycle` 行、
[03_运行时骨架.md](../../../architecture/03_运行时骨架.md) 第 1、2、9 节
`AppStateHost` 签名）。本模块提供三件事：主状态转移合法性判定与切换
（`RequestTransition`，非法拒绝并记诊断，合法则切换、发 `app.state_changed`、再回调
`OnStateChanged` 订阅者）、InWorld 子状态栈管理（`PushSubState`/`PopSubState`，仅
InWorld 下可操作，进入/离开 InWorld 分别自动置顶/清空栈）、"请求退出应用"标志位
（`RequestExit`，只允许在 MainMenu）。

**本任务（T1-7a）范围**：不依赖数据注册表。`found.game_state` 表本任务只写字段说明
（`schema/found.game_state.md`）并提供"从定义列表构造"的入口
（`AppStateMachineConfig.FromDefinitions`），JSON 加载由数据注册表对接（T1-4，另一任务）。

依赖：只依赖 `core/foundation/common`（`Id`、`SubscriptionHandle`）、
`core/foundation/event_bus`（`IEvent`、`IEventBus`，用于发出 `app.state_changed`）与
.NET 标准库；不引用任何引擎适配层实现、不使用系统时间、不使用多线程、不使用反射、不使用
系统级 `Random`（见架构确定性要求）。

不负责什么：

- 不定义 InWorld 之外的其它复合状态：主状态只有 `Boot`/`MainMenu`/`Loading`/`InWorld`/
  `Pause` 五个，`InWorld` 内的 `awaiting_input`/`playing_back` 两个仅离散时间模型下出现的
  附加子态属于 `sim_loop` 模块管辖（该模块 T1-5 已声明本项目暂不启用离散模式），本模块不
  建模它们（见 `contracts/InWorldSubState.cs` 注释）。
- 不实现"退出应用"的具体行为（关闭窗口、保存设置等）：`RequestExit` 只记录
  `IsExitRequested` 标志位，触发后续动作属于游戏外壳 Shell（L5）的职责。
- 不读取 `found.game_state` 数据文件：本模块拥有该表的字段说明（见
  `schema/found.game_state.md`），但只提供内存构造入口 `AppStateMachineConfig.Default()`/
  `FromDefinitions`，JSON 读取属于数据注册表（T1-4）职责。

## 目录

```
app_lifecycle/
  README.md
  contracts/
    AppState.cs                    AppState 枚举
    InWorldSubState.cs              InWorldSubState 枚举
    SubStateId.cs                   SubStateId（readonly struct，内置枚举 + 自定义名统一表达）
    AppStateMachineConfig.cs        AppStateMachineConfig（Default()、FromDefinitions()、扩展方法）
    GameStateTransitionDefinition.cs GameStateTransitionKind、GameStateTransitionDefinition
    Callbacks.cs                    StateChangedCallback、SubStateChangedCallback
    IAppLifecycleDiagnostics.cs     IAppLifecycleDiagnostics
    IAppStateHost.cs                IAppStateHost
    Events.cs                       AppEventKeys、AppStateChangedEvent
  core/
    AppStateHost.cs                  IAppStateHost 默认实现
    InMemoryAppLifecycleDiagnostics.cs IAppLifecycleDiagnostics 默认实现
  schema/
    found.game_state.md              found.game_state 字段说明（本模块拥有，暂不加载 JSON）
  tests/
    SubStateIdTests.cs
    AppStateMachineConfigTests.cs
    AppStateHostTests.cs
```

## 设计要点与判断记录

以下几项是文档未逐字规定、执行期需要做出的具体选择，逐条记录判断依据供设计层复核：

1. **`PushSubState`/`PopSubState` 返回 `bool`，覆盖 03 第 9 节伪代码的 `void` 签名**：
   03 原文 `pushSubState(sub): void`/`popSubState(): void` 没有返回值，但任务书明确拍板
   "PushSubState 检查'当前子状态→目标子状态'在允许集合内，否则……拍板：返回 bool，非法
   返回 false 并记诊断（与 RequestTransition 一致）""栈底 Explore 不可弹出（返回
   false）"。本接口按任务书拍板实现为 `bool` 返回值——这是任务书对 03 伪代码的显式覆盖
   指示，不是本模块自行推测的结果，已在 `contracts/IAppStateHost.cs` 类型注释里同样记录，
   供设计层复核是否需要同步更新 03 第 9 节伪代码，使其与 `RequestTransition` 的
   `bool` 风格保持一致。

2. **默认子状态转移表额外放行 `Combat → MenuOverlay`**：03 第 2 节状态机表"子状态之间
   允许有限的叠加"一段原文"叠加规则由游戏层通过状态栈机制配置，例如 Combat 中打开
   MenuOverlay"、MenuOverlay 一行"回落到触发前的子状态（Explore 或 Combat）"两处合并
   读，MenuOverlay 明确可以叠加在 Combat 之上。若 `Default()` 只放行
   `Explore → MenuOverlay`（对应任务书"子状态：Explore→Combat/Dialog/MenuOverlay/
   Cutscene"这一句字面枚举），任务验收要求的场景"Explore→Combat→（Push
   MenuOverlay）→Pop 回 Combat→Pop 回 Explore"会在 Push 一步直接因未登记的子转移被拒绝。
   因此 `Default()` 额外放行 `Combat → MenuOverlay`；`Dialog`/`Cutscene → MenuOverlay`
   03 原文未举例提及，默认表不主动放开，游戏层可用 `AllowSubTransition` 按需扩展。
   详见 `contracts/AppStateMachineConfig.cs` `Default()` 内联注释。

3. **`AllowTransition`/`AllowSubTransition` 是写入（扩展）方法，另配
   `IsTransitionAllowed`/`IsSubTransitionAllowed` 作为查询方法**：任务书把前两者列在
   "配置可由游戏层扩展"一节，语义是"追加一条允许的转移"，因此实现为返回
   `AppStateMachineConfig` 自身的链式修改方法（`Default()` 内部也用它们搭建默认表）；
   `AppStateHost` 做合法性检查时改用另外命名的查询方法，避免"检查"与"改配置"共用同一个
   方法名带来的误用风险。这是本模块在任务书给定的方法名之外，为查询用途新增的方法，
   不改变任务书要求的公开扩展方法名字与语义。

4. **`SubStateId` 相等性只看名字字符串，不区分"来自内置枚举"还是"来自自定义字符串"**：
   `SubStateId.Combat`（由 `InWorldSubState.Combat` 构造）与 `new SubStateId("Combat")`
   相等。好处：`AppStateMachineConfig.FromDefinitions` 处理 `kind: sub` 行时，`from`/`to`
   字符串统一走 `new SubStateId(value)` 构造，不需要先尝试按内置枚举名解析、失败再退回
   自定义字符串这一分支逻辑，行为与直接使用 `SubStateId.Combat` 常量完全一致。

5. **`AppStateMachineConfig.FromDefinitions` 从空配置起步，不隐式叠加 `Default()`**：
   与 `EventCatalog.FromDefinitions`、`HookRegistry.DeclareFromDefinitions` 的"从传入的
   定义列表构造，不偷偷合并其它来源"是同一惯例——若隐式叠加默认表，调用方传入一份"完整
   替换默认表"的数据（例如游戏层完全重新设计状态机）时会意外得到默认表与自定义表的并集，
   而不是调用方期望的替换结果。需要"默认表 + 扩展"效果时，调用方应显式合并两组定义
   （或直接对 `Default()` 返回值调用 `AllowTransition`/`AllowSubTransition`），已在
   `contracts/AppStateMachineConfig.cs` `FromDefinitions` 注释中说明。

6. **`RequestTransition` 触发子状态栈变化不额外调用 `OnSubStateChanged`**：进入 InWorld
   自动压入 `Explore`、离开 InWorld 清空栈，这两处栈变化是"主状态转移的副作用"，任务书
   "子状态变化也发 app.state_changed？——拍板：不发……提供单独的 OnSubStateChanged 回调
   订阅"这句话讨论的是 `PushSubState`/`PopSubState` 触发的子状态变化；本模块进一步把这条
   原则延伸到"进入/离开 InWorld 导致的栈初始化/清空"场景，同样不触发 `OnSubStateChanged`
   ——这类变化已经被 `RequestTransition` 自身的 `OnStateChanged`/`app.state_changed`
   完整表达（"主状态变成了 InWorld"这一件事本身就蕴含"子状态被置为 Explore"，是同一因果
   链的一部分，不需要拆成两次通知）。

## 诊断

`IAppLifecycleDiagnostics`（默认实现 `InMemoryAppLifecycleDiagnostics`，内存列表，不依赖
任何引擎适配层接口）记录：`RequestTransition`/`PushSubState`/`PopSubState`/`RequestExit`
遇到非法调用时的一条警告（不中止流程，方法本身返回 `false` 表达失败）。

## 事件时序

一次成功的 `RequestTransition(target)`：切换 `_state` → 按需更新子状态栈 →
`PublishImmediate(AppStateChangedEvent)`（key `app.state_changed`）→ 依次调用
`OnStateChanged` 订阅者——事件先于回调送达，满足任务验收"事件字段正确且顺序在回调之前"。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| 主状态机与 InWorld 子状态栈的定义与转移规则实现机制 | 是 | 需要叠加的自定义子状态、自定义转移（经 `AllowTransition`/`AllowSubTransition`/`AddCustomSubState` 扩展） |
| `found.game_state` 表字段定义与内存构造入口 | 是 | 具体转移数据行（未来经数据注册表加载） |
| `app.state_changed` 事件与 `OnStateChanged`/`OnSubStateChanged` 回调通道 | 是 | 具体订阅者与响应逻辑 |
| "请求退出应用"标志位机制 | 是 | 退出应用的具体行为（游戏外壳 Shell） |
