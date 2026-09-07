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

## 加固任务补充：`DefaultSpatialSyncKinds` 接上 05 §3.6 碰撞层三常量

`EntitySpatialSyncHost.KindConfig` 新增可选的 `RadiusResolver`（`Func<Entity, double>`）——半径解析
委托非空时优先于固定 `Radius` 使用，只依赖 L0 的 `Entity` 基类，供真正认识具体 `Entity` 子类的
调用方（L4 `core/gameplay/assembly.GameplayAssembly`）注入闭包，本类不因此对 L4 产生编译期依赖（见
该字段判断记录）。`CarriersAssembly.DefaultSpatialSyncKinds` 相应新增：`creature`/`player` 补
`Core.Foundation.EngineAdapter.CollisionLayers.UnitBlock` 标签（供
`Core.Carriers.Unit.MovementOptions.UnitBlocking` 使用）；`EntityKinds.AreaTrigger` 加入白名单，打
`CollisionLayers.TriggerOnly` 单一标签，半径用固定近似值 0.5（本类是 L3，不能引用 L4 的
`AreaTriggerEntity` 按其 `Shape` 精确计算——`GameplayAssembly` 用上面的 `RadiusResolver` 换成精确值，
见其判断记录）。详见各自类型判断记录。

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

同目录下 `tests/CreatureDespawnPeriodicEffectTests.cs`（C02）、`core/carriers/item/tests/
EquipmentReplaceHandleTests.cs`（C08，见下）两个新增测试文件用的正是本类装配出的真实全链路
（`CreatureFactory`/`AuraHost`/`EquipmentHost`/`CombatHost` 等），不是任何模块内部的 fake unit
access——外部审计 7e63d66 第四轮明确要求这两条缺陷必须用真实组合复现，不能靠测试假实现规避。

## C08 收口（外部审计 7e63d66 第四轮，P2）：`EquipmentHost` 接入 `Rules.Skill.AuraQuery`

`StackOverflowPolicy.Replace` 换掉一个光环实例句柄时，`EquipmentHost` 需要同步收到通知才能正确
更新自己按装备实例记录的授予句柄（详见 `core/carriers/item/README.md`/`core/rules/skill/
README.md` 同编号条目）。本类第 4 步构造 `Equipment` 时新增传入 `auraQuery: Rules.Skill.AuraQuery`
（真实 `AuraHost`，不是 `RulesAssembly.DeferredAuraQuery` 那个延迟绑定代理——`SkillHost.AuraQuery`
在这里已经构造完成，直接返回真实实例，不存在判断记录 2 那种循环依赖，不需要延迟绑定）。

## CR130-02 收口（外部审计 audit-5c444f1-20260908，P1）：装备 `SkillGranter` 显式传 `permanent: false`

`core/rules/skill.SkillHost.LearnSkill(Id,Id,Id)`（不带 `permanent` 的三参重载）此前是这里"已知的
契约补齐"一节接线用的唯一带来源重载，语义含糊——既用来接装备联动，也被
`core/gameplay/assembly.GameplayAssembly` 的奖励/任务 `SkillGranter` 复用，两者对"这份授予是否应该
被存档快照"的期望截然相反（装备卸下即失效，不应持久化；一次性奖励技能应长期保留）。`SkillHost`
新增显式 `permanent` 参数的四参重载（见 `core/rules/skill/README.md` 同编号条目）后，本类第 4 步的
装备 `skillGranter` 接线改为显式调用 `Rules.Skill.LearnSkill(unitId, skillId, sourceId, permanent:
false)`——不依赖三参重载的默认值，装备授予的临时语义不会因为默认值将来改变而意外漂移成永久。
