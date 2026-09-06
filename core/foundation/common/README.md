# L0 基础层 · common 公共原语

职责：提供全架构共用的最小原语类型——`Id`（领域.名称 逻辑标识）、`Vec2`（二维平面坐标）、
`SubscriptionHandle`（事件/回调订阅的取消句柄）、通用无参回调委托 `Callback`。这些类型不属于
任何具体系统，是 `00_架构总则.md` 第 4.1 节记法约定中 `Id`、`Vec2` 两种类型，以及
`02_引擎适配层.md` 等文档里反复出现的 `Callback`、`SubscriptionHandle` 概念在本仓库的落地。

依赖：不引用任何其他模块、任何引擎适配层实现；只依赖 .NET 标准库。

不负责什么：

- 不定义任何具体游戏系统的数据结构（技能、物品、任务等），那些是 L1~L4 各模块的职责。
- 不定义引擎适配层的 13 个接口本身，那是 `engine_adapter` 模块的职责；本模块只提供这些接口
  签名里会用到的最基础的值类型。
- 不做 `Id` 的引用完整性校验（某个 `Id` 指向的数据行是否存在），那是 `DataRegistry`
  （`core/foundation/data_registry`）的职责；本模块只保证 `Id` 的**格式**合法。
- 不出现任何引擎符号、任何读取系统挂钟时间的 API、任何系统提供的伪随机数生成器、任何
  基于线程池的异步任务库——全架构要求核心模拟可确定性复现，这些能力一律经引擎适配层
  （`IClock`、`Rng` 等）注入，不得在基础原语里悄悄引入。

## 目录

```
common/
  README.md
  contracts/   Id.cs Vec2.cs Callbacks.cs SubscriptionHandle.cs
  tests/       IdTests.cs Vec2Tests.cs
```

## 类型清单

| 类型 | 说明 | 对应文档 |
|---|---|---|
| `Id` | `readonly struct`，领域.名称 格式的逻辑标识，格式非法时构造期抛异常 | `00_架构总则.md` 4.1、`04_数据与内容管线.md` 2.1（`<domain>.<name>` 命名规范） |
| `Vec2` | `readonly struct`，二维平面坐标，仅提供纯数学运算，不做任何与引擎类型的转换 | `00_架构总则.md` 4.1 |
| `Callback` | 无参、无返回值的具名委托，供 `IWindow.onCloseRequested` 等只需要"通知一下"的回调点复用 | `02_引擎适配层.md` 1.1 |
| `SubscriptionHandle` | `sealed class`，`Dispose()` 幂等地调用构造时传入的取消订阅委托 | `02_引擎适配层.md` 1.12（`IRenderer3D.onAnimEvent` 返回值） |
