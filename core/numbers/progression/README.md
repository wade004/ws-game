# L1 数值层 · progression 等级与经验

职责：定义等级曲线、经验获取与升级判定（见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L1 模块表 `progression` 行、
[00_架构总则.md](../../../architecture/00_架构总则.md) 第 3 节"等级与经验……原样保留"、
[04_数据与内容管线.md](../../../architecture/04_数据与内容管线.md) 第 1.1 节表清单）。

依赖：`core/foundation/common`（`Id`、`common/json`）、`core/foundation/event_bus`（`IEvent`、
`IEventBus`）、`core/foundation/data_registry`（`IDataRegistryView`、`DataRecord`、
`TableSchema`、`IValidationRule` 等）与 .NET 标准库；不引用任何引擎适配层实现、不使用系统
时间、不使用多线程、不使用系统级 `Random`、不使用反射。**并行开发期显式不引用**
`Core.Numbers.StatBlock`/`Core.Numbers.PowerSet` 的任何具体类型——升级带来的属性成长改由
`StatModifierWriter`/`StatModifierRemover` 具名委托注入（见 `contracts/ProgressionWriters.cs`），
由上层（游戏初始化代码）把真正的 `IStatHost` 实现接进来。

## 目录

```
progression/
  README.md
  contracts/
    ProgSchemas.cs             prog.level_curve / prog.xp_source 的 TableSchema
    Events.cs                  ProgressionEventKeys、LevelUpEvent、XpGainedEvent
    ProgressionWriters.cs      StatModifierWriter / StatModifierRemover 具名委托
    IProgressionDiagnostics.cs 诊断出口
    IProgressionHost.cs        IProgressionHost
  core/
    ProgressionHost.cs             IProgressionHost 默认实现
    ProgLevelCurveValidationRule.cs prog.level_curve 的 entries 连续性校验规则
    InMemoryProgressionDiagnostics.cs
  schema/
    README.md                  两张表的字段说明与判断记录
  tests/
    ProgressionHostTests.cs
```

## 设计要点与判断记录

1. **`prog.level_curve`/`prog.xp_source` 字段为实现期补录**：04 第 1.1 节只给出这两张表的
   一句话描述，没有给出字段表；本模块按任务书 T2-3 给出的最小字段集实现，取舍记录见
   `schema/README.md`。

2. **`AddXp` 直接收最终经验数值，`GrantFromSource` 才按 `prog.xp_source` 换算**：任务书显式
   拍板"`AddXp` 直接收最终数值；另提供 `GrantFromSource(unitId, xpSourceId,
   multiplier=1)` 按表计算"——`GrantFromSource` 算出 `base_xp × weight × multiplier`
   （四舍五入到 `long`，负值钳为 0）后转发给 `AddXp`。`prog.xp_source.condition`
   （Expr 字段）本任务只登记字段类型供未来内容校验使用，`IProgressionHost` 不对它求值
   （任务书原文"本任务只登记字段类型，不求值"）。

3. **一次 `AddXp` 可连跨多级，每跨一级发一次 `LevelUpEvent`，成长只在跨级全部处理完后写入一次**：
   `ProgressionHost.AddXp` 用 `while` 循环逐级消耗经验并逐级发事件，循环结束后才调用一次
   `StatModifierRemover` + 若干次 `StatModifierWriter`，写入"从 2 级到最终等级"的累计成长
   （1 级无成长，任务书原文）。这样"写入一次"与"每级发一次事件"是两件独立的事情，不会出现
   跨两级却把成长值分两次写、后一次覆盖前一次只剩最后一级增量的错误。

4. **成长写入的来源 id 固定为 `prog.growth`，`RegisterUnit` 不隐式写入成长**：见
   `ProgressionHost` 类文档判断记录——`RegisterUnit` 定位是"初始化/读档到已知等级"，不应
   用曲线重算结果覆盖存档已有的属性值；只有真正经由 `AddXp`/`GrantFromSource` 触发的升级
   路径才会调用成长写入委托。

5. **满级后 `AddXp` 整笔丢弃、记诊断、不发 `XpGainedEvent`**：任务书"到 max_level
   后经验不再累积（丢弃并记诊断）"。区分两种满级丢弃场景：调用时已在满级（整笔丢弃，不发
   事件）与本次调用内升到满级但仍有残余经验（残余部分丢弃，但本次调用已经发过
   `XpGainedEvent`/已跨的每一级 `LevelUpEvent`，不重复处理）。

6. **`entries` 连续性校验分两层**：数据校验层的 `ProgLevelCurveValidationRule`（`IValidationRule`
   扩展点，需调用方 `RegisterValidationRule` 后随 `DataRegistry.LoadAll`/`Validate` 生效，对应
   04 第 5 节"循环引用检测"一类由内容模块自行登记的检查项）与 `ProgressionHost` 构造期的防御性
   重复校验（与 `L10nHost.ValidateNoFallbackCycle` 同一惯例：即便调用方忘记注册校验规则，
   `ProgressionHost` 本身也不会把结构非法的曲线数据带入运行时）。

## 不负责什么

- 不实现"经验来源的触发条件"求值（`prog.xp_source.condition` 是 Expr 字段，本模块只登记类型）。
- 不知道、也不依赖具体的属性宿主/资源池实现，只通过 `StatModifierWriter`/
  `StatModifierRemover` 两个具名委托与外界交互（见判断记录 4）。
- 不做"死亡不掉经验""休息经验"等具体游戏口味机制——00 第 3 节已拍板"休息经验直接去掉"，
  经验获取速率完全由 `prog.level_curve`/`prog.xp_source` 数据与调用方传入的
  `amount`/`multiplier` 决定。
