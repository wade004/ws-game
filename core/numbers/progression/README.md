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
    ProgressionPersistable.cs      player.progression 存档段（静态工厂，惯例同 core/carriers/unit
                                    的 UnitPersistable 写法，W1 收边补齐，A4 审计 F1）
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
   **N08 收边补齐（外部审计 68c9bed）：`while` 循环内先提交 `unit.Level = newLevel`，再
   `PublishImmediate(LevelUpEvent)`**——原实现先发布事件、后赋值，`PublishImmediate` 是同步
   派发，事件处理器内如果不读事件自带的 `NewLevel` 字段、而是反查 `GetLevel(unitId)`（如按等级
   重算派生属性的消费者），会读到升级前的旧等级，直到下一次别的属性写入才可能被动补救。事件
   携带的 `OldLevel`/`NewLevel` 字段值本身不变，只调整"字段赋值"与"发布事件"两个语句的先后
   顺序。见 `ProgressionHostTests.cs`
   （`AddXp_LevelUpHandler_ReadsGetLevel_SeesNewLevelAlready`/
   `AddXp_CrossesTwoLevels_EachHandlerInvocation_SeesLevelAtThatPointInTime`）。

4. **成长写入的来源 id 固定为 `prog.growth`，`RegisterUnit` 不隐式写入成长**：见
   `ProgressionHost` 类文档判断记录——`RegisterUnit` 定位是"全新单位从起始等级开始"，不应
   用曲线重算结果覆盖存档已有的属性值；只有真正经由 `AddXp`/`GrantFromSource` 触发的升级
   路径才会调用成长写入委托。
   **W1 收边补齐（A4 审计 F1）**：本条判断记录写下时"读档到已知等级"这一场景实际上从未真正
   落地过——10 第 2.5 节"属性快照默认不存"意味着读档后除非有人重新写入成长修正，否则等级
   越高的单位属性会缺失越多层成长（这正是 A4 审计发现的存档缺口本身）。现在
   `ProgressionPersistable`/`ProgressionHost.RestoreState` 补上了这条路径：读档专用，允许指定
   非零 `xp`，且**会**调用 `ApplyGrowth` 按 1..level 重新聚合成长——与 `RegisterUnit`（全新单位，
   `xp` 恒 0，不重算成长）是两个语义不同的入口，不冲突。
   **R08 收口（外部审计 5e779c6，P2，成立）**：`RestoreState` 补上成长修正重聚合后仍遗漏一环——
   它不经过 `AddXp` 的正常升级路径，不发布 `LevelUpEvent`，任何按 `progression.level_up` 才失效
   重算的下游缓存（当前唯一消费方：`core/numbers/stat_block.StatHost.RecomputeRatingStats`，见
   06 第 1.1 节评级换算）读档后会一直停留在读档前的等级算出的值（外部审计复现）。现在
   `RestoreState` 结尾额外发布一条新增的 `ProgressionRestoredEvent`（`progression.state_restored`）
   ——不复用 `LevelUpEvent`：读档不是"发生了一次升级"，没有真实的"旧等级"，且可能一次性跨越
   多级，复用会产生一串语义不自洽的虚假升级事件（见 `Events.cs` 判断记录）。
   `core/rules/assembly.RulesAssembly` 订阅本事件同样调用 `Stats.RecomputeRatingStats`（与订阅
   `LevelUpEvent` 那一条完全相同的方法，只是多一个触发来源）。见 `ProgressionHost.RestoreState`
   判断记录、`core/rules/tests/Integration/ProgressionRestoreRatingRecomputeTests.cs`。

5. **满级后 `AddXp` 整笔丢弃、记诊断、不发 `XpGainedEvent`**：任务书"到 max_level
   后经验不再累积（丢弃并记诊断）"。区分两种满级丢弃场景：调用时已在满级（整笔丢弃，不发
   事件）与本次调用内升到满级但仍有残余经验（残余部分丢弃，但本次调用已经发过
   `XpGainedEvent`/已跨的每一级 `LevelUpEvent`，不重复处理）。

6. **`entries` 连续性校验分两层**：数据校验层的 `ProgLevelCurveValidationRule`（`IValidationRule`
   扩展点，需调用方 `RegisterValidationRule` 后随 `DataRegistry.LoadAll`/`Validate` 生效，对应
   04 第 5 节"循环引用检测"一类由内容模块自行登记的检查项）与 `ProgressionHost` 构造期的防御性
   重复校验（与 `L10nHost.ValidateNoFallbackCycle` 同一惯例：即便调用方忘记注册校验规则，
   `ProgressionHost` 本身也不会把结构非法的曲线数据带入运行时）。

7. **AUD-02 根治（外部审核第九轮，P2，architecture/落地计划/audit-85f1f4f-20260908）：
   `ProgressionPersistable.Load` 对本段整体缺失（`JsonNull`）的处理，从 no-op（保留读档前的
   运行期等级/经验）改为重置到默认态（1 级、0 经验）**：修复前真实场景：同一宿主先后加载两个
   存档槽，缺本段的旧档不会清掉前一个槽留下的等级，违反 10 第 3 节"缺失段语义"合同。曲线 id
   （`curve_id`）本身不属于"存档数据"，是内容/装配阶段已经经 `RegisterUnit` 确定的职业/单位归属，
   缺段清空只清等级/经验这两个真正的存档字段，沿用 `Save()` 当前已登记的曲线（复用 `Save()`
   本身取回，不新增契约方法）。该单位从未经 `RegisterUnit` 注册（例如所属职业没有配置
   `level_curve_ref`，见判断记录 4 引用的 `RulesAssembly.RegisterUnit` 判断记录）时，`Save()`
   本身会抛 `ArgumentException`（判断记录见 `Save_UnregisteredUnit_Throws`）——本条根治捕获这一
   前提缺失的情形，直接保持未注册、不代为注册一个调用方从未选择的曲线，不抛异常。见
   `ProgressionPersistableTests.Load_NullData_ResetsRegisteredUnitToLevelOneAndZeroXp`（已注册
   单位分支）、`Load_NullData_LeavesUnitUnregistered`（未注册单位分支，既有测试）。

8. **消费方反馈第 36 条根治：新增 `ApplyGrowthToCurrentLevel`，成长统一只由本模块的修正承载**：
   `core/carriers/creature.CreatureFactory.ApplyStats` 此前自行重复解析 `prog.level_curve`，把
   "2 级到出生等级"的累计成长直接叠进 `IStatHost.SetBase` 写的基础值；该单位一旦经 `AddXp` 真实
   升级，`ApplyGrowth`（判断记录 3）又会把"2 级到新等级"整段成长重算并整体覆盖写入修正——两处各自
   独立维护"从 2 级累加"这同一段成长，`[2..出生等级]` 因此被基础值与修正各计了一次（真实探针：
   出生等级 2、出生 strength 7，升到 3 级实测 11，应为 9）。根治为在 `IProgressionHost` 上新增
   `ApplyGrowthToCurrentLevel(unitId)`——内部直接调用与 `AddXp`/`RestoreState` 末尾完全相同的
   `ApplyGrowth` 私有方法（先 `StatModifierRemover` 清空旧 `prog.growth` 修正、再按当前等级整体
   重算写入），不新写一份公式；`CreatureFactory.ApplyStats` 相应改为只写
   `base_stats × tier.stat_multiplier`，`Spawn` 在 `RegisterUnit` 之后紧接着调用
   `ApplyGrowthToCurrentLevel` 一次性补上"2 级到出生等级"的成长（出生等级 1、无曲线的既有示例
   数据下这一步是空操作，行为不变）。本方法刻意不发任何事件——语义上既不是"升级"（没有真实的
   等级提升，见判断记录 3）也不是"读档恢复"（`ProgressionRestoredEvent` 的语义是"状态被存档
   覆盖"，见判断记录 4/`Events.cs` 判断记录"语义诚实"，硬套给一次全新生成反而语义不诚实），典型
   调用方（`CreatureFactory.Spawn`）本身就在生成流程内，不需要额外的失效重算通知。见
   `core/numbers/progression/tests/ProgressionHostTests.cs`（`ApplyGrowthToCurrentLevel` 相关用例）、
   `core/carriers/creature/tests/CreatureFactoryTests.cs`（`E36_*`）。

## CORE-170-02 根治（第十轮外部审计，P2，architecture/落地计划/audit-8160178-20260908）

`ProgressionHost` 是单位等级的唯一权威，但修复前 `AddXp`/`RestoreState` 只更新本模块内部的
`UnitState.Level`，从不同步任何外部实体字段；生产装配构造玩家实体时也从未写入实体自身的
`Level` 字段（构造函数默认值 1），只把等级传给 `RulesAssembly.RegisterUnit`。`Core.Carriers.
Unit.WorldUnitAccess.GetLevel`（`IUnitAccess` 的真实实现，供装备需求判断等运行期消费者调用）
直接读实体字段，与本模块内部权威等级各自独立、互不同步——真实探针复现 `rules_progression_
level=2;entity_level=1`，等级 2 的装备需求判断因此读到过期的实体等级 1，返回
`RequirementNotMet`。

根治：新增具名委托 `LevelSync`（`core/numbers/progression/contracts/ProgressionWriters.cs`，
惯例同 `StatModifierWriter`——并行开发期不引用具体实体类型，通过具名委托注入），`ProgressionHost`
新增可选构造参数 `levelSync`，在三个等级会变化/确立的时机（`RegisterUnit` 首次注册、`AddXp`
升级、`RestoreState` 读档恢复）末尾统一调用；未注入时（`null`，多数测试用的最小假实现）行为与
本次改动之前完全一致。`RulesAssembly` 新增同名可选构造参数原样转发；`CarriersAssembly` 传入
`Core.Carriers.Unit.WorldUnitAccess.SetLevel`（新增方法，不在 `IUnitAccess` 接口上，惯例同
`Revive`——只供组合根装配期的委托闭包调用）——单位存在于 `IWorldSim` 时同步写入实体字段，单位
尚未注册到世界时安全 no-op。`WorldUnitAccess.GetLevel` 本身不改动，仍然直接读实体字段，只是
自此这个字段恒与 `ProgressionHost.GetLevel` 一致，不需要反查 `core/numbers/progression`（那会
要求 `core/carriers/unit` 反向依赖具体实现，且对没有配置进度曲线的单位——多数 NPC/怪物——
`GetLevel` 该读什么曲线本就无从谈起）。见 `Tests.Gameplay.Assembly.
CORE_170_02_ProgressionLevelSyncTests`（真实 `GameplayAssembly` 多级 `AddXp`、读档、
`WorldUnitAccess.GetLevel`、等级需求装备四者一致性）。

## 不负责什么

- 不实现"经验来源的触发条件"求值（`prog.xp_source.condition` 是 Expr 字段，本模块只登记类型）。
- 不知道、也不依赖具体的属性宿主/资源池实现，只通过 `StatModifierWriter`/
  `StatModifierRemover` 两个具名委托与外界交互（见判断记录 4）。
- 不做"死亡不掉经验""休息经验"等具体游戏口味机制——00 第 3 节已拍板"休息经验直接去掉"，
  经验获取速率完全由 `prog.level_curve`/`prog.xp_source` 数据与调用方传入的
  `amount`/`multiplier` 决定。
