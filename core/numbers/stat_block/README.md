# L1 数值层 · stat_block 属性表

职责：单位的属性三段式聚合（`基础值 + Σflat` → `× (1 + Σpct)` → `× Π(1 + Σmult_group)`，
见 `architecture/06_规则层_属性技能战斗AI.md` 第 1.1 节），可选评级换算（第 1.1 节、
`00_架构总则.md` 第 3 节"默认关闭"）与可选抗性维度（`00` 第 3 节"可选属性维度"）。
对应 `01_分层与依赖.md` L1 模块表 `stat_block` 行、契约接口名 `StatHost`。

依赖：L0（`Core.Foundation.Common` 的 `Id`；`Core.Foundation.DataRegistry` 的
`IDataRegistryView`/`TableSchema`/`FieldSchema`/`FieldKind`/`IValidationRule`；
`Core.Foundation.EventBus` 的 `IEventBus`/`IEvent`）。不引用 `Core.Rules`/`Core.Carriers`/
`Core.Gameplay`、不引用任何引擎适配层实现、不引用 `System.Threading`/`DateTime`/
`System.Random`/`System.Reflection`（见 `11_工程规范与测试.md` 第 4 节"确定性"）。

## 目录

```
stat_block/
  README.md
  contracts/   StatModifier.cs（StatModifierOp、StatModifier）
               IStatHost.cs
               StatHostOptions.cs
               StatChangedEvent.cs（StatBlockEventKeys、StatChangedEvent）
  core/        StatSchemas.cs（stat.definition / stat.rating_conversion 的 TableSchema）
               StatDefinitionValidationRule.cs（IValidationRule：min<=max、
               rating_conversion_ref 需要 is_rating）
               StatHost.cs（IStatHost 默认实现）
  schema/      README.md（字段表）
  tests/       StatHostTests.cs
```

## 用法

```csharp
var registry = new DataRegistry(source, bus);
registry.RegisterSchema(StatSchemas.Definition);
registry.RegisterSchema(StatSchemas.RatingConversion);          // 仅当数据里有这张表
registry.RegisterValidationRule(new StatDefinitionValidationRule());
var report = registry.LoadAll();

var statHost = new StatHost(registry, bus, new StatHostOptions
{
    EnableRatingConversion = false,   // 00 第 3 节：默认关闭
    EnableResistanceGroup = true,     // 00 第 3 节：可选维度，本项默认开启
});

statHost.RegisterUnit(unitId);
statHost.SetBase(unitId, new Id("stat.strength"), 10);
statHost.AddModifier(unitId, new StatModifier(new Id("stat.strength"), StatModifierOp.Flat, 5, sourceId));
var final = statHost.GetStat(unitId, new Id("stat.strength"));
statHost.RemoveModifiersBySource(unitId, sourceId);
```

调用方（宿主）负责：把 `StatSchemas.Definition`/`StatSchemas.RatingConversion` 注册进
`IDataRegistry`；把 `StatDefinitionValidationRule` 注册为校验规则（可选，但建议）；在自己的
`IEventCatalog` 中登记 `StatBlockEventKeys.StatChanged`（`"stat.changed"`），或关闭
`EventBusOptions.StrictCatalog`——本模块不依赖 `event_bus/generated/EventKeys.g.cs`
是否已生成该常量（该文件由 `found.event_catalog.json` 驱动，登记时序不在本模块控制范围内）。

## 三段式聚合与浮点确定性

- `flat`：按 `AddModifier` 调用顺序（`List<StatModifier>` 插入顺序）逐项累加。
- `pct`：同样按插入顺序累加后，整体作为 `(1 + Σpct)` 乘一次。
- `mult`：同一 `StatModifier.MultGroup` 内的多条 `Mult` 修正先按插入顺序相加，
  各乘区再按"该乘区名字第一次在这个属性的修正列表里出现"的顺序连乘（`Π(1+组内和)`）。
  未显式指定 `MultGroup` 的修正落入默认乘区 `StatModifier.DefaultMultGroup`（`"default"`）。
- 同一组插入顺序两次独立运行，聚合结果逐位相等（`double` 精确相等）；不同插入顺序不保证
  相等（`Mult` 各乘区之间是乘法，浮点乘法不满足结合律的舍入误差属于预期行为，架构文档只
  承诺"同种子同输入产生同结果"，不承诺不同插入顺序等价）。

## 评级换算（可选，默认关闭）

`StatHostOptions.EnableRatingConversion=true` 且某属性 `is_rating=true` 时，
`base + Σflat` 先经该属性 `rating_conversion_ref` 指向的 `stat.rating_conversion` 曲线换算，
换算结果再进入 `pct`/`mult` 两段。曲线的自变量是**单位等级**（`entries[].level`，设计层
2026-09-05 拍板，取代此前"level 是评级原始值自己的插值断点"的判断）：按 `StatHostOptions.LevelLookup(unitId)`
查到的等级在 `entries`（按 `level` 升序排列）上线性插值取 `points_per_percent`（越界取端点），
再用 `percent = rawValue / pointsPerPercent` 算出换算结果；`LevelLookup` 为 `null` 时等级一律按
1 处理。`StatHost` 仍不直接引用 `core/numbers/progression` 的任何类型——由调用方把真正的等级
来源（如 `IProgressionHost.GetLevel`）适配成 `LevelLookup` 委托签名后注入构造期的
`StatHostOptions`。判断记录见 `StatHost.ConvertRating` 源码注释。

```csharp
var statHost = new StatHost(registry, bus, new StatHostOptions
{
    EnableRatingConversion = true,
    LevelLookup = unitId => progressionHost.GetLevel(unitId),  // 由调用方适配真正的等级来源
});
```

## 抗性维度（可选，默认开启）

`stat.definition.group == "resistance"` 的属性始终可以正常 `SetBase`/`AddModifier`；
`StatHostOptions.EnableResistanceGroup=false` 时 `GetStat` 对这类属性恒返回 `0` 并向
`StatHost.Warnings` 追加一条警告，不影响其它分组属性的聚合。

## 不负责什么

- 不做技能/光环/施法管线（L2 `core/rules/skill` 的职责，见 06 第 3 节）。
- 不持有单位的其它状态（生命值、资源池等属于 `power_set` 与更高层）。
- 不做数据文件的读写/校验实现（复用 `core/foundation/data_registry`），本模块只提供
  `TableSchema`/`IValidationRule` 声明，注册时机由调用方掌控。
- 不直接依赖 `core/numbers/progression` 的任何具体类型——评级曲线虽然以单位等级为自变量
  （见"评级换算"一节，设计层 2026-09-05 拍板），但等级经 `StatHostOptions.LevelLookup`
  具名委托注入，不是直接引用 `IProgressionHost`，避免与 `progression` 产生同层跨模块的编译期
  耦合（`ConvertRating` 判断记录）。
