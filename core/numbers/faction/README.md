# L1 数值层 · faction 阵营矩阵

职责：定义阵营间敌对/中立/友好关系（见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L1 模块表 `faction` 行、
[00_架构总则.md](../../../architecture/00_架构总则.md) 第 3 节"阵营矩阵……缩到小矩阵
（敌对/中立/友好等有限枚举）"）。

依赖：`core/foundation/common`（`Id`）、`core/foundation/event_bus`（`IEvent`、`IEventBus`）、
`core/foundation/data_registry`（`IDataRegistryView`、`DataRecord`、`TableSchema` 等）与 .NET
标准库；不引用任何引擎适配层实现、不使用系统时间、不使用多线程、不使用系统级 `Random`、不使用
反射；不引用 `Core.Numbers.StatBlock`/`Core.Numbers.PowerSet`（阵营关系与属性/资源池无关，
本模块不需要任何具名委托注入点）。

## 目录

```
faction/
  README.md
  contracts/
    FacSchemas.cs      fac.faction / fac.reaction_matrix 的 TableSchema
    Reaction.cs         Reaction 枚举（Hostile/Neutral/Friendly）
    Events.cs            FactionEventKeys、FactionRelationChangedEvent
    IFactionMatrix.cs    IFactionMatrix
  core/
    FactionMatrix.cs    IFactionMatrix 默认实现
  schema/
    README.md           两张表的字段说明与判断记录
  tests/
    FactionMatrixTests.cs
```

## 设计要点与判断记录

1. **两张表字段为实现期补录**：04 第 1.1 节只给出一句话描述，没有给出字段表；本模块按任务书
   T2-3 给出的最小字段集实现，取舍记录见 `schema/README.md`。

2. **`GetReaction` 的解析优先级：同阵营 → 运行期覆盖 → 显式登记行 → `from` 的
   `default_reaction`**：同阵营恒为 `Friendly`，优先级最高，不受任何覆盖/登记行影响（两个
   阵营 id 相同这件事本身已经回答了"是不是自己人"）。矩阵不要求对称——`(from,to)` 与
   `(to,from)` 是两个独立的键，`fac.reaction_matrix` 只登记 `(A,B)` 时，`GetReaction(B,A)`
   查不到显式行，回退到 `B` 的 `default_reaction`，两个方向可以给出不同结果。

3. **`SetReaction` 只覆盖 `(from,to)` 单一方向，未变化时不发事件**：与 localization 的
   `SetLocale`（"未变化不发事件"）同一惯例；覆盖不是"就地改数据"，`ResetOverrides` 之后立刻
   恢复到数据加载时的状态（显式登记行 + 默认反应回退），不会把运行期覆盖误当成新的"数据事实"。

4. **未登记阵营一律抛异常**：`GetReaction`/`SetReaction`/`IsHostile` 传入未在 `fac.faction`
   登记的阵营 id 时抛 `ArgumentException`——与 `L10nHost.SetLocale` 对未声明语言的处理同一
   惯例，视为调用方拼错 id 的编程错误，不是需要静默兜底的正常业务分支。

## 不负责什么

- 不实现"进出战斗""仇恨表"一类由阵营关系驱动的战斗判定——那是 L2 `combat`（`ThreatTable`）的
  职责，本模块只回答"两个阵营之间是什么关系"。
- 不做声望网络、跨阵营任务链一类魔兽式阵营系统——00 第 3 节已拍板"缩到小矩阵"，本模块的
  `Reaction` 是固定的三值枚举，不可扩展出更多档位。
