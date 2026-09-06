# L1 数值层 · archetype 职业与种族模板

职责：定义单位初始属性、技能、外形的模板（见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L1 模块表 `archetype` 行、
[00_架构总则.md](../../../architecture/00_架构总则.md) 第 3 节"职业与种族模板……原样保留"、
[07_载体层_物品生物物件.md](../../../architecture/07_载体层_物品生物物件.md) 第 5 节
"职业 = 主属性引用 + 资源类型引用 + 技能书 + 天赋树引用"）。

依赖：`core/foundation/common`（`Id`、`common/json`）、`core/foundation/event_bus`（`IEvent`、
`IEventBus`）、`core/foundation/data_registry`（`IDataRegistryView`、`DataRecord`、
`TableSchema`、`IValidationRule` 等）与 .NET 标准库；不引用任何引擎适配层实现、不使用系统
时间、不使用多线程、不使用系统级 `Random`、不使用反射。**并行开发期显式不引用**
`Core.Numbers.StatBlock`/`Core.Numbers.PowerSet` 的任何具体类型——写属性、注册资源改由
`StatBaseWriter`/`StatModifierWriter`/`PowerRegistrar` 具名委托注入（见
`contracts/ArchetypeWriters.cs`）。

**本模块及其测试内不出现任何具体游戏的职业/种族名称**（落地方案与分阶段计划.md T2-3 行显式
禁止），全部职业/种族的取名、数值完全由外部注入的 `arch.class`/`arch.race` 数据行决定，测试
只用 `arch.class.sample_a` 一类中性 id。

## 目录

```
archetype/
  README.md
  contracts/
    ArchSchemas.cs        arch.class / arch.race / arch.talent_tree 的 TableSchema
    Models.cs              ClassDefinition、RaceDefinition、TalentNode、TalentTree、AppliedArchetype
    Events.cs               ArchetypeEventKeys、ArchetypeAppliedEvent
    ArchetypeWriters.cs     StatBaseWriter / StatModifierWriter / PowerRegistrar 具名委托
    IArchetypeRegistry.cs   IArchetypeRegistry
  core/
    ArchetypeRegistry.cs                 IArchetypeRegistry 默认实现
    ArchTalentTreeCycleValidationRule.cs  天赋树前置存在性 + 无环校验规则
  schema/
    README.md               三张表的字段说明与判断记录
  tests/
    ArchetypeRegistryTests.cs
```

## 设计要点与判断记录

1. **三张表字段为实现期补录**：04 第 1.1 节只给出一句话描述，没有给出字段表；本模块按任务书
   T2-3 给出的最小字段集实现，取舍记录见 `schema/README.md`。

2. **跨模块引用字段暂不声明为 `Reference`**：`primary_stat`（→ `stat.definition`，
   `stat_block` 模块）、`power_types`（→ `arch.power.*`，`power_set` 模块）、`skill_book_ref`
   （→ `skill.book.*`，L2 `skill` 模块）、`passive_auras`（→ `skill.aura.*`）均指向本任务并行
   开发或尚未实现的模块表；本模块把它们声明为普通 `Id`/`IdList`（只做格式校验），不声明为
   `FieldKind.Reference`，避免测试期因为这些表不存在而被判定 `reference_integrity` 错误。待
   对应模块登记 schema 后，集成方可用 `IDataRegistry.DeclareReference` 动态补上引用完整性
   检查，不需要改动本模块的 `TableSchema`（`DeclareReference` 正是为这种场景设计的扩展点，见
   `IDataRegistry.cs` 文档）。`talent_tree_ref`/`level_curve_ref` 引用的表都在本任务范围内
   （`arch.talent_tree`、`prog.level_curve`），直接声明为 `Reference`，`level_curve_ref`
   指向不存在的曲线时会被 04 内置的 `reference_integrity` 检查拦截（验收标准原文）。

3. **`ApplyTo` 的调用顺序固定为 base → race flat → race passive_auras → powers → 事件**：
   任务书原文"按顺序 StatBaseWriter 写 base_stats → StatModifierWriter 写种族修正 →
   PowerRegistrar 注册资源 → 发 archetype.applied"；W1 收边补齐在种族修正之后、注册资源之前
   插入被动光环施加（见判断记录 7）。`raceId` 为 `null` 时完全跳过种族修正与被动光环这两步
   （不调用 `StatModifierWriter`/`AuraApplier`），不是"调用一次值为 0 的修正"。

4. **种族修正的 `sourceId` 取种族 id 本身**：见 `ArchetypeRegistry` 类文档判断记录——任务书
   没有指定这个来源 id 具体是什么，选用种族 id 本身语义最直接、也便于调用方按种族切换时
   知道该用哪个 sourceId 去移除旧修正。

5. **`ApplyTo` 不处理天赋树**：任务书对 `ApplyTo` 的描述只涉及 base_stats/种族修正/资源注册/
   事件，`AppliedArchetype` 只透传 `LevelCurveRef`；天赋点的分配、`arch.talent_tree` 节点的
   查询走 `GetTalentTree`，由上层（如未来的天赋系统）自行消费，不在本模块范围内。

6. **天赋树前置存在性 + 无环校验是数据校验层的扩展规则**：`ArchTalentTreeCycleValidationRule`
   （`IValidationRule` 扩展点，对应 04 第 5 节"循环引用检测：……如天赋前置……不得成环"）需要
   调用方 `RegisterValidationRule` 后随 `DataRegistry.LoadAll`/`Validate` 生效；
   `ArchetypeRegistry` 构造期解析 `nodes` 时只做"结构合法"的最小检查（节点必须是对象、必须有
   `id`），不重复做前置存在性/无环检查——那需要遍历同一张表的其它记录做跨行分析，属于
   `IValidationRule` 的职责边界，不适合放进单条记录的构造期解析。

7. **W1 收边补齐：`race.passive_auras` 经注入的 `AuraApplier` 委托施加**（A3 审计 #8）——
   此前"本模块只保存不应用"的前提是"L2 `skill` 模块尚未实现"，该前提已过期（`core/rules/skill`
   目前已完整实现光环系统）。构造函数新增可选参数 `AuraApplier? auraApplier = null`：非空时
   `ApplyTo` 对 `race.passive_auras` 逐个调用（`sourceId` 固定用种族 id 本身，与
   `StatModifierWriter` 同一选取理由，见判断记录 4）；为 null（未装配，向后兼容）时行为与本次
   改动之前完全一致，仍旧只保存不应用。真正的接线（把 `IEffectSink.ApplyAura` 转发进来）在
   `core/rules/assembly/RulesAssembly.cs` 完成，本模块本身不引用 `core/rules/skill` 任何类型。

## 不负责什么

- 不实现属性聚合/资源池的具体运算，只通过 `StatBaseWriter`/`StatModifierWriter`/
  `PowerRegistrar`/`AuraApplier` 四个具名委托与外界交互（见判断记录 2、7）。
- 不处理天赋点消耗/学习流程，只提供 `GetTalentTree` 只读查询（见判断记录 5）。
