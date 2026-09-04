# L0 基础层 · hook_registry 脚本钩子注册表

职责：供游戏层在架构声明的挂载点上注册自定义回调（见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L0 模块表 `hook_registry` 行、
[03_运行时骨架.md](../../../architecture/03_运行时骨架.md) 第 9 节 `HookRegistry` 签名）。
本模块提供三件事：挂载点声明（`DeclareHookPoint`）、回调注册与执行顺序控制
（`Register`，按 `order` 升序、同 `order` 按注册先后）、挂载点调用（`Invoke`，单个回调异常
被隔离，不中断其余回调、不向调用方抛出）。

**本任务（T1-7a）范围**：不依赖数据注册表。`found.hook` 表本任务只写字段说明
（`schema/found.hook.md`）并提供"从定义列表构造"的入口
（`IHookRegistry.DeclareFromDefinitions`），JSON 加载由数据注册表对接（T1-4，另一任务）。

依赖：只依赖 `core/foundation/common`（`Id`、`SubscriptionHandle`）、
`core/foundation/event_bus`（`IEvent`、`IEventBus`，仅在 `HookRegistryOptions.EmitInvokedEvent`
开启时用于发出 `hook.invoked` 调试事件）与 .NET 标准库；不引用任何引擎适配层实现、不使用
系统时间、不使用多线程、不使用反射、不使用系统级 `Random`（见架构确定性要求）。

不负责什么：

- 不知道任何具体挂载点应该在什么时机被 `Invoke`：挂载点的声明与调用时机由使用方
  （例如 `SceneRouter` 在场景卸载前调用 `found.hook.scene_pre_unload`）决定，本模块只提供
  登记与调用机制本身。
- 不读取 `found.hook` 数据文件：本模块拥有该表的字段说明（见 `schema/found.hook.md`），
  但只提供内存构造入口 `DeclareFromDefinitions`，JSON 读取属于数据注册表（T1-4）职责。
- 不做参数的强类型 / 签名校验：`HookPointDefinition.Signature` 只是文档化的说明文本，
  `HookArgs.Get<T>`/`TryGet<T>` 在取值时才做运行期类型检查，声明期不校验回调是否"符合签名"。

## 目录

```
hook_registry/
  README.md
  contracts/
    HookArgs.cs              HookArgs（只读参数包装，Get<T>/TryGet<T>/Empty）
    HookCallback.cs           HookCallback 委托
    HookPointDefinition.cs    HookPointDefinition
    HookRegistryOptions.cs    HookRegistryOptions
    IHookDiagnostics.cs       IHookDiagnostics
    IHookRegistry.cs          IHookRegistry
    WellKnownHooks.cs         WellKnownHooks（scene_pre_unload/scene_post_load 常量）
    Events.cs                 HookEventKeys、HookInvokedEvent
  core/
    HookRegistry.cs            IHookRegistry 默认实现
    InMemoryHookDiagnostics.cs IHookDiagnostics 默认实现
  schema/
    found.hook.md              found.hook 字段说明（本模块拥有，暂不加载 JSON）
  tests/
    HookArgsTests.cs
    HookRegistryTests.cs
```

## 设计要点与判断记录

以下几项是文档未逐字规定、执行期需要做出的具体选择，逐条记录判断依据供设计层复核：

1. **`hook.invoked` 事件字段：在登记表建议值之外补充 `callbackCount`**：
   `data/_sample/found/found.event_catalog.json` 里 `hook.invoked` 一行的 `fields` 只登记了
   `hookId`，但该行 description 明确标注"字段为建议值"；03 第 9 节 `HookRegistry` 接口签名
   完全没有规定 `invoke` 发出什么事件、携带什么字段。任务书显式拍板携带
   `{ hookId, callbackCount }`。与 sim_loop 处理 `sim.tick_started` 补充 `dt` 字段同一类
   "登记表标注建议值、03 未排他性限定字段 ⇒ 允许按需要补充"的处理，已在 `contracts/Events.cs`
   `HookInvokedEvent` 类型注释里详细记录，供设计层复核是否需要同步更新
   `found.event_catalog.json` 的 `hook.invoked` 行。

2. **本模块自己持有一份事件 key 常量（`HookEventKeys`），不依赖 event_bus 的
   `EventKeys.g.cs`**：与 sim_loop 模块的 `SimEventKeys` 同一惯例——`EventKeys.g.cs` 是从
   数据登记表批量生成、供跨模块引用"已知 key"字符串值的强类型入口，两者常量值恒等
   （都是 `new Id("hook.invoked")`），模块自身发出的事件仍在模块内部自持一份定义，
   不产生编译期依赖，也不会因生成物重新生成的时机差异而互相牵连。

3. **`Register` 抛异常而非返回失败结果**：`IHookRegistry.Register` 在挂载点未声明、或
   `AllowMultiple=false` 已有回调两种情况下抛 `InvalidOperationException`，而不是像
   `IAppStateHost.RequestTransition` 那样返回 `bool`。理由：这两种情况在正常运行时流程中
   都属于"组装期接线错误"（挂载点名字拼错、或错误地对一个单回调挂载点注册第二个回调），
   属于应当在开发期尽快暴露、而不是被静默吞掉继续运行的编程错误；这与
   `IHookRegistry.Invoke` 对"未声明挂载点"同样抛异常保持一致（两者都是契约误用），
   任务书原文对 `Register`/`Invoke` 的这两种情况也明确写"抛 InvalidOperationException"，
   不是本模块自行选择——只是在此记录以便与 `AppStateHost` 的"返回 bool"风格对照说明两者
   不矛盾：`RequestTransition`/`PushSubState`/`PopSubState` 处理的是"运行时游戏逻辑触发的、
   预期会时常发生的非法状态转移尝试"（例如 UI 层拦不住玩家在不该暂停时按了暂停键），
   属于正常业务分支，因此返回 `bool` 交调用方处理；而 `Register`/`Invoke` 的两种异常场景
   在架构设计上被定性为"接线错误"而非"业务分支"。

4. **`CallbackCount(hookId)` 对未声明的挂载点返回 0，而不是抛异常**：任务书只给出该方法
   签名与语义"某挂载点当前已注册的回调数量"，未明确对未声明 id 的行为。选择返回 0（而不是
   像 `Register`/`Invoke` 那样抛异常）：`CallbackCount` 是纯查询方法，语义上"一个不存在的
   挂载点，其已注册回调数为 0"是自洽且对调用方更友好的答案（例如测试或诊断代码想在声明前
   /声明后各查一次而不必先 try/catch），且不改变任何状态，与 `Register`/`Invoke` 这类
   "误用即报错"的写操作/触发操作性质不同。

5. **`InsertSorted` 用插入排序而非 `List.Sort`**：`Register` 频率低（组装期一次性居多）、
   每个挂载点的回调列表通常很短；插入排序按 `(Order, Sequence)` 复合键把新条目插入到正确
   位置，天然稳定（`List.Sort` 不保证稳定，需要额外用 `Sequence` 打破平局，两种写法效果
   一致，插入排序更直接地把"稳定"这件事写在算法本身而不是靠比较器兜底）。

6. **`HookRegistryOptions.EmitInvokedEvent=true` 但未传 `IEventBus` 时，在构造期直接抛
   `ArgumentException`，而不是运行期静默跳过发事件**：这是一处配置矛盾（"要求发事件"但
   "没给发事件的通道"），选择在装配阶段尽早暴露而不是让 `Invoke` 在运行时默默什么也不做，
   与判断记录 3 的"组装期接线错误应尽快暴露"是同一类考虑。

## 诊断

`IHookDiagnostics`（默认实现 `InMemoryHookDiagnostics`，内存列表，不依赖任何引擎适配层
接口）记录：`Invoke` 时单个回调抛出异常（Error，附带原始异常）。`Register`/`Invoke`
对未声明挂载点、`AllowMultiple` 冲突这两类"接线错误"直接抛异常（见判断记录 3），
不额外记诊断——异常本身已经是最直接的信号。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| 挂载点声明、注册、按序调用、异常隔离的实现机制 | 是 | 具体挂载点 id、参数签名、`AllowMultiple` 取值 |
| `found.hook` 表字段定义与内存构造入口 | 是 | 具体挂载点数据行（未来经数据注册表加载） |
| `pre_unload`/`post_load` 挂载点 id 常量（`WellKnownHooks`） | 是 | 在这些挂载点上注册的具体回调实现 |
| `hook.invoked` 调试事件开关 | 是 | 是否开启、订阅该事件做调试呈现 |
