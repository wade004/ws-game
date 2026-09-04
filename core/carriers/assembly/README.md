# L3 载体层 · assembly（组装根）

职责：阶段 3 整理"事项四"——`CarriersSchemaCatalog.RegisterAll` 一次性注册 L0～L3 全部
`TableSchema`/`IValidationRule`（先调 `Core.Rules.Assembly.RulesSchemaCatalog.RegisterAll` 注册
L0～L2，再补登记 item 六张表、creature 两张表、gobj 两张表）；`CarriersAssembly` 在
`Core.Rules.Assembly.RulesAssembly`（L0～L2）之上，按正确顺序装配 `core/carriers` 五个模块
（`unit`/`item`/`creature`/`summon`/`gobj`）的宿主，把契约缺口用具名委托接线，把
`create_item`/`open_lock`/`summon` 三类效果原语的真实实现组合后换入
`RulesAssembly.EffectExtension`，最终把全部宿主暴露为只读属性。

依赖：`Core.Rules.csproj`（经 `RulesAssembly`）、同程序集的 `core/carriers` 全部五个模块
（`common`/`unit`/`item`/`creature`/`summon`/`gobj`）。不引用 `Core.Gameplay`（L4），不使用
`UnityEngine`、`System.Threading`、`DateTime`、`System.Random`、`System.Reflection`。

## 目录

```
assembly/
  README.md
  CarriersSchemaCatalog.cs     注册 L0~L3 全部 TableSchema/IValidationRule
  CarriersAssembly.cs           组装根：构造 core/carriers 五模块宿主、处理装配顺序
  NullWorldFlags.cs             IWorldFlags 的空对象默认实现（L4 回调未注入时的兜底）
  CompositeEffectExtension.cs   IEffectExtension 组合帮助类型（item+gobj+summon 三份合一）
  tests/
    CarriersAssemblyTests.cs    烟雾测试：空数据构造不抛异常、tick 几次不抛异常
```

## 分层判断记录：不注册/不引用 L4

`CarriersSchemaCatalog.RegisterAll` **不**注册 `core/gameplay/world_state` 的
`world.flag_schema`——那是 L4 玩法层的表（01 第 3 节依赖矩阵"L3 允许依赖 L0～L2，禁止依赖
L4"）。同理，`CarriersAssembly` 的 `IWorldFlags`/`ILootRoller`/`DialogOpenerDelegate` 三个 L4 回调
一律是可选参数，未注入时分别退化为 `NullWorldFlags`、"不产出任何掉落"（`GameObjectHost` 的
`loot` 参数本就可空）、"`on_use: dialog` 分发失败并记警告"（`GobjOptions.DialogOpener` 本就可
空）——本类（L3）不持有任何 L4 具体实现，也不对 L4 程序集产生编译期依赖。

## 使用方式

```csharp
// 0. 调用方自己构造 DataRegistry。
var options = RulesSchemaCatalog.CreateOptions();       // ExprSchema = RulesExprSchema.Base
var registry = new DataRegistry(dataSource, bus, options);
CarriersSchemaCatalog.RegisterAll(registry);              // 注册 L0~L3 全部 schema/校验规则
var report = registry.LoadAll();

// 1. 调用方自己组装世界模拟与空间/导航查询。
var world = new WorldSim(bus);
var spatial = new StubSpatialQuery();                      // 或真实引擎适配

// 2. 一次性装配 L0~L3。
var carriers = new CarriersAssembly(bus, registry, rng, world, spatial);

// 3. 生成一个生物、给它穿上装备。
var unitId = carriers.Creatures.Spawn(templateId, mapId, position, facing: 0);
carriers.Inventory.AddItem(unitId, itemTemplateId, 1);
var instance = carriers.Inventory.ListItems(unitId)[0];
carriers.Equipment.Equip(unitId, instance.InstanceId, slotId);
```

## 装配顺序（`CarriersAssembly` 构造函数内编号注释）

| 步骤 | 创建 | 依赖谁 | 备注 |
|---|---|---|---|
| 1 | `WorldUnitAccess` | `IWorldSim`（+ 可选 `ISpatialIndexSync`） | `RulesAssembly` 需要调用方注入的 `IUnitAccess` |
| 2 | `CreatureImmunityProvider` + `InventoryHost` + `ItemEffectExtension` | `IWorldSim`／`IDataRegistryView`+`IEventBus` | 三者都不依赖 `RulesAssembly` 内部宿主，可以先造好 |
| 3 | `RulesAssembly`（`autoRegisterTickHandlers: false`） | 1、2 | `staticImmunity`=2 的 `CreatureImmunityProvider`；`effectExtension`=2 的 `itemExtension`（先只挂 create_item，见下） |
| 4 | `EquipmentHost` | 3（`Rules.Stats`/`Rules.Skill.EffectSink`） | `SkillGranter` 委托接线到 `Rules.Skill.LearnSkill`/`ForgetSkill` |
| 5 | `CreatureFactory` | 3（`Rules.Stats`/`Powers`/`Progression`/`Ai`） | `AiRegistrar` 委托接线到 `Rules.Ai.RegisterUnit`/`SetRotation` |
| 6 | `SummonHost` + `SummonEffectExtension` | 5 | |
| 7 | `GameObjectFactory` + `GameObjectHost` + `GobjEffectExtension` | 2（`Inventory`）、3（`Rules.Stats`/`Rules.Skill`） | `worldFlags`/`lootRoller` 未注入时退化为 `NullWorldFlags`/`null` |
| 8 | 换绑完整版 `IEffectExtension`（`CompositeEffectExtension(item, gobj, summon)`）→ `Rules.EffectExtension.Bind`；注册 `SummonTickHandler` 到 `AiDecision`（先于 `AiTickHandler`）→ `Rules.RegisterTickHandlers()`（补上 L2 四个，`AiTickHandler` 排在 `SummonTickHandler` 之后）→ `MovementHost` + `MovementTickHandler` 挂到 `MovementAndNavigation` | 全部 | 见下方"为什么 effectExtension/tick 顺序要分两段" |

## 为什么 `effectExtension`/tick 顺序要分两段

**`IEffectExtension` 的循环依赖**：`SkillHost`（L2，`RulesAssembly` 构造期内部创建）需要一份
`IEffectExtension` 才能正确处理 `create_item`/`open_lock`/`summon` 三类效果，但这三类的真实实现
（`ItemEffectExtension`/`GobjEffectExtension`/`SummonEffectExtension`，L3）恰恰需要
`RulesAssembly` 构造完成后才存在的 `Ai.RegisterUnit`/`Ai.SetRotation`（供 `AiRegistrar`）才能装配
出 `CreatureFactory`/`SummonHost`（`GameObjectHost` 同理需要 `Rules.Skill`）。`RulesAssembly` 用
`Core.Rules.Assembly.DeferredEffectExtension` 代理打破这个循环：先用代理构造 `SkillHost`（`create_item`
可以立即绑定，因为 `InventoryHost` 不依赖 `RulesAssembly`），L3 三个模块都装好之后，本类再调
`Rules.EffectExtension.Bind` 换上组合了三者的完整实现，不需要重新构造 `SkillHost`。

**tick 处理器的注册顺序**：`IWorldSim.RegisterPhaseHandler` 只能追加、不能插队。任务书拍板
`SummonTickHandler` 必须先于 `AiTickHandler` 挂到 `TickPhase.AiDecision`（跟随意图相对生物自身的
AI 决策意图优先，见 `SummonTickHandler` 判断记录），但 `AiTickHandler` 是 `RulesAssembly` 构造期
内部自动挂载的——若不干预，`RulesAssembly` 造完时 `AiTickHandler` 已经排好，本类再也插不到它
前面。解法：构造 `RulesAssembly` 时传 `autoRegisterTickHandlers: false` 跳过步骤 9 的自动挂载，
本类先注册 `SummonTickHandler`，再手动调用 `Rules.RegisterTickHandlers()` 补上 L2 的四个（此时
`AiTickHandler` 排在 `SummonTickHandler` 之后），最后注册 `MovementTickHandler`。

## tick 阶段挂载表（本类新增的两个，L2 四个见 `core/rules/assembly/README.md`）

| `TickPhase` | 处理器 | 顺序 |
|---|---|---|
| `AiDecision` | `SummonTickHandler` → `AiTickHandler`（L2） | `SummonTickHandler` 先注册 |
| `MovementAndNavigation` | `MovementTickHandler` | 唯一处理器 |

## 已知的契约补齐：`SkillHost.ForgetSkill`

`core/carriers/item.SkillGranter`/`EquipmentHost` 的既有判断记录早就写下"真实组装代码把
`SkillHost.LearnSkill`/`Forget` 适配成 `SkillGranter` 委托注入"，但当时 `SkillHost` 只有
`LearnSkill`，没有对称的遗忘方法——本次在允许改动的 `core/rules/skill/` 范围内给 `SkillHost` 补上
`ForgetSkill(unitId, skillId)`（从已知技能集合移除，单位未注册/技能本不在集合中均视为幂等成功），
`CarriersAssembly` 的 `SkillGranter` 委托才能真正接线完整。

## 测试

`tests/CarriersAssemblyTests.cs` 是烟雾测试，不是端到端玩法测试：验证"空数据也能正确装配出全部
宿主、tick 几次不抛异常"，重点覆盖本类接线量最大、编译期检查不到的构造期顺序问题。逐模块的行为
正确性（背包/装备/生物工厂/召唤/游戏对象/移动/免疫）由各自模块自己的测试覆盖，不在本类重复。
