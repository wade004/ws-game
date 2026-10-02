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

## T-N4-4：`CarriersAssembly` 新增带 `ProgressionOptions` 的构造重载

同 `core/rules/assembly/README.md`"T-N4-4"一节判断记录（ABI 门禁 G3：本仓库已发布 1.33.0 基线，
直接在已发布构造函数末尾追加新参数不再安全）——本类型旧签名构造函数（25 个参数）原样保留、转发
新增重载（26 个参数，全部不带默认值）并传 `progressionOptions: null`；新增重载原样把
`progressionOptions` 转发给 `RulesAssembly` 同名新增重载。唯一调用点
（`core/gameplay/assembly.GameplayAssembly` 第 3 步）已同步显式列出全部 26 个参数（含此前从未
在该调用点出现过的 `projectileOptions`，此前该处省略即回退旧签名的默认值 `null`，新签名要求
显式传入）。

## T-N4-4：`CarriersAssembly` 新增带 `CreatureInteractOptions` 的构造重载（ADR-0051）

同上一节惯例——本类型当前"旧"签名构造函数（26 个参数，上一节新增的那个）原样保留、转发新增
重载（27 个参数，全部不带默认值）并传 `creatureInteractOptions: null`；新增重载内部把
`creatureInteractOptions` 转发给新增的 `Core.Carriers.Creature.CreatureInteractionHost`
构造函数。新增只读属性 `CreatureInteractions`（`CreatureInteractionHost`，ABI 只新增成员），
与既有 `GameObjectInteractions` 并列；装配体内新增
`world.RegisterPhaseHandler(TickPhase.TriggerEvaluation, new CreatureInteractIntentTickHandler(CreatureInteractions))`，
与既有 `InteractIntentTickHandler`（gobj）同一 tick 阶段并列注册，两者按 `"interact"` 意图
`Args` 是否携带 `creature_instance_id`/`gobj_instance_id` 分流，见
`core/carriers/creature/README.md` 判断记录 13、`core/carriers/gobj/README.md` 对应判断记录。
唯一调用点（`core/gameplay/assembly.GameplayAssembly`）已同步显式传入该参数。

## 判断记录（Bind 生物挥击间隔回退代理，2026-09-21，architecture/adr/0059-普通攻击的框架原生执行机制.md）

`CarriersAssembly` 构造函数体内、`Creatures = new CreatureFactory(...)` 之后追加一行：
`Rules.AttackIntervalFallback.Bind(new Core.Carriers.Creature.CreatureAttackIntervalProvider(world, Creatures))`
——把 `RulesAssembly` 构造期塞入的占位代理（`DeferredAttackIntervalProvider`，详见
`core/rules/assembly/README.md` 对应判断记录）换上真实实现，同既有
`deferredWeaponDamageQuery.Bind(Equipment)` 一类"L3 装配完成后回填 L2 延迟绑定代理"的既定接线
惯例。不新增/不改动任何公开构造签名，纯粹是既有构造函数体内多一行装配逻辑。

## 判断记录（`InteractionTargetRegistry`，2026-09-21，architecture/adr/0062-地面掉落物原生交互与统一最近可交互目标查询.md）

新增 `Core.Carriers.Assembly.InteractionTargetRegistry`（`core/carriers/common.IInteractionTargetRegistry`
的默认实现，见该目录 README 对应判断记录）。放在本目录而不是 gobj/creature/loot 任一具体模块：本类型
与本目录既有的 `EntitySpatialSyncHost`/`NullWorldFlags` 同一类"跨多个载体模块的组合期通用实现"，不
归属任何单一模块；只依赖 `IWorldSim`/`IUnitAccess`，在 `Units = new WorldUnitAccess(...)` 就绪后立即
构造（`CarriersAssembly` 新增只读属性 `InteractionTargets`），不需要等 gobj/creature 具体宿主构造
完成——本类型不持有它们的引用，只经 `EntityKinds` 字符串常量识别目标类型，`core/gameplay/loot`（L4）
构造出 `LootHost` 之后产生的掉落物实体，本类型透明覆盖（现场查 `IWorldSim`，不持有构造期快照）。
候选过滤显式排除查询发起者自身（`!entity.EntityId.Equals(unitId)`）——早期实现遗漏这一条，被
`Tests.PresentationCommon` 用真实 `CreatureFactory.Spawn` 出的玩家单位命中自身的用例发现（玩家单位
`Kind == EntityKinds.Creature`，未排除自身时会把查询发起者本身算作候选），修正为一处正确性修复，不
只是测试夹具绕行。不新增/不改动任何既有公开构造签名。

生物类候选仅存活且有可交互内容时成立（`IUnitAccess.Exists` × `IUnitAccess.IsAlive`，与表现层
`player.alive`/`target.alive` 同一权威口径，见 architecture/adr/0065-死亡生物不是最近可交互目标的
候选.md；内容判定见下方 ADR-0069 判断记录）——场景物件、地面掉落物不是"单位"概念的实例，不接入
这两条判定。`IsInteractionCandidate` 从 `static` 改为实例方法（读构造期已持有的 `_units` 字段判
存活），不新增构造参数、不新增依赖。

## 判断记录（生物候选补可交互内容核对，2026-09-22，architecture/adr/0069-最近可交互目标候选生物需有可交互内容.md）

`InteractionTargetRegistry` 新增构造重载，接受可选的 `Core.Carriers.Common.ICreatureInteractionHost`
（ABI 只加不改，旧的两参构造函数原样保留、转发新增重载并传 `null`）；`IsInteractionCandidate` 对
生物候选再叠加一条：`_creatureInteractions == null || _creatureInteractions.HasInteractableContent(id)`
——未注入时按"无法判定，按有内容处理"降级，不参与过滤，既有调用方（旧签名构造函数）行为逐位
不变。生产装配 `CarriersAssembly` 必须真的接上这条依赖：`InteractionTargets` 的构造点因此从
"`Units = new WorldUnitAccess(...)` 就绪后立即构造"（本类型历史版本，见上方"判断记录
（`InteractionTargetRegistry`...）"一节）后移到"`CreatureInteractions = new CreatureInteractionHost
(...)` 构造完成之后"——两次构造之间的既有代码没有任何一处读取 `InteractionTargets`，后移不影响
除本次新增内容过滤之外的任何既有行为；地面掉落物（L4，晚于 `CarriersAssembly` 构造）依旧能被
查到，不受构造点后移影响（见上方判断记录"直接查 `IWorldSim` 现场结果"）。

## 手感落地 S10：载体层手感接线（`CarriersFeelAssembly`，2026-10-02）

`CarriersAssembly` 新增末尾多一个 `CarriersFeelOptions? feelOptions` 参数的构造重载（最长的旧重载纯转发并传 `null`，物理签名不变）；传入非 null 即启用手感系统，`CarriersAssembly.Feel`（`CarriersFeelSystem`）持有：全装配唯一的解析器与动作时钟、输入缓冲 `InputBufferHost`、`ActionSlotSkillBinding`、`BufferedActionIntentSink`、可选的 `GraceTracker`、运动层 `MotionServices`。接线顺序：装配解析器（带生产提供者）→ 规则层接线（见 `core/rules/assembly/README.md`）→ 输入缓冲（声明 `found.input_action`、映射、出口、步骤 1 处理器、时间线拉取口）→ 失效与清理订阅 → 运动层（`MovementHost.Motion` + `MotionHitFeelWiring.Connect`）。数据里没有 `feel.*` 行时抛 `InvalidOperationException`，不静默降级；标定行有多行必须给 `CalibrationId`。

本节追加的判断记录（S10 本节编号）：

1. **单一动作时钟**：时钟只在 `HitFeelAssembly.Attach` 里创建一次，输入缓冲（过期按动作时钟计）、时间线、局部顿帧、运动层 `frozen` 叠加态读的都是它，顿帧时缓冲窗口随之暂停。
2. **输入动作 → 技能映射用 `found.input_action.skill_slot`（S10 加法字段）**：数据里原来没有"动作对应哪个技能"，新增可选字段 `skill_slot`（技能绑定槽位名），经 `SkillBindingHost` 取行动者当前绑定的技能；`ActionSlotSkillBinding` 同时是时间线取消进入用的 `IActionSkillBinding` 与缓冲出口的映射来源，两处用同一份。缺省不写即不映射。
3. **缓冲出口 `BufferedActionIntentSink` 在时间线动作进行中拒绝接受**：记录留给时间线自己在取消窗口/连招窗口里拉取（`CastPipeline` 每个推进 tick 末尾对缓冲 `TryConsume`）。若出口此时接受，同一 tick 内会被施法管线以 `ActionLocked` 拒绝并"撤销消费"，而缓冲对"本 tick 已取用过"的行动者不再提供候选，时间线的拉取被饿死。动作结束后的下一个步骤 1 出口接受它——这就是"在后摇结束前 X 毫秒按下，动作一结束就接上"（冒烟 `BufferedPress_BeforeRecoveryEnds_StartsNextActionOnFirstAcceptableTick`）。
4. **受击硬直中拒绝接受**：行动者处于 `IHitReactionQuery.IsStaggered` 时记录保留，硬直结束前按缓冲窗口（动作时钟，顿帧暂停）计时。
5. **施法被拒回报缓冲**：出口经 `skill.cast_failed` 回报 `InputBufferHost.ReportRejected`，只认"本 tick 由本出口提交的那次施法"的失败；时间可解 = `ActionLocked`/`GcdActive`/`Busy`，以及剩余冷却不超过记录剩余缓冲的 `OnCooldown`（同时间线取消进入口径），其余原因丢弃记录。
6. **接受时朝向对齐是瞬时对齐**：`FaceOnAccept` 且有按下瞬间方向快照时，接受瞬间 `Facing = Atan2(y, x)`；不经运动层转向速率（转向速率是行走惯性，攻击起手的方向修正才是 `face_on_accept` 语义）。
7. **主手/副手武器的定义**：数据里只有 `item.slot_definition.is_weapon`，没有主/副手概念。缺省取武器槽按槽位 id 序数排序的第 1 个为主手、第 2 个为副手（与 `EquipmentHost` 找"第一个武器槽"同一规则）；游戏槽位命名不符时用 `CarriersFeelOptions.MainHandSlot/OffhandSlot` 显式指定。
8. **光环临时手感条目键 = 光环定义 id，层数不放大**（M2-B 已改：见本文件"手感落地 M2-B"节，条目键改为光环实例 id、层数按次叠乘）：`AuraSnapshot` 不带光环实例 id，同一定义的多层叠加只算一条。
9. **失效订阅在步骤 7 才生效**：装备变化、光环施加/移除事件经事件总线在步骤 7 派发，所以这些变化对手感的影响从当 tick 派发之后才可见（同 tick 内更早步骤读到旧值），与既有事件驱动模块同一口径。
10. **运动层的 `MotionServices.Actions` 取技能宿主的 `ActionStateQuery`**：时间线动作进行中运动模式为 `Action`。
11. **`SkillOptions.ActionStepSeconds` 绑定**：启用手感时被写成手感步长（`CarriersFeelOptions.StepSeconds`，缺省取传入的 `SkillOptions` 值）；经 `GameplayAssembly` 装配时步长取时钟宿主 `StepSeconds`，显式给了不同值抛异常。

已知限制（逐条交代）：未映射 `skill_slot` 的已声明类别动作会留在缓冲里直到过期，若恰为最前候选会挡住优先级更低的候选；时间线自己在取消窗口里拉取的记录不经出口，不做接受时朝向对齐；没有生产的"剪辑标记来源"（clip marker），时间线标记只来自数据里 `timeline.markers`；本地玩家绑定在 `PresentationAssembly` 构造时固定。Unity 宿主引导（`GameFoundationBootstrap`/`FrameworkResidentHost`）已走 `GameplayAssembly` 的最长构造重载并透传 `FeelOptions`（缺省为空即不开手感，行为不变）；已被输入缓冲声明类别的动作不再由引擎直接提交 `cast` 意图、改经施法出口提交，未声明类别的动作仍直接提交（见 `adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md` "手感落地 M2-A：引擎侧手感生产接线"一节）。

## 手感落地 S11：时间线目标辅助接进生产装配（2026-10-02）

`CarriersFeelAssembly.Attach` 在时间线协作者上补 `TargetAssist = new ActionTargetAssistAdapter(options.TargetAssist ?? new TargetChainAssistResolver(rules.Targeting, carriers.Units), carriers.Units)`（`HitFeel` 与 `IsTimelineSkill` 的接线在规则层，见 `core/rules/assembly/README.md` S11 节）。本节编号为 S11 本节编号：

1. **候选解析缺省用目标选择链，`CarriersFeelOptions.TargetAssist` 可覆盖（游戏层软锁定）**：同一个选项同时是运动层 `MotionServices.TargetAssist` 与时间线 `target_assist` 的候选来源，覆盖时两处一致；没有覆盖时运动层仍为 null（缺省关闭、与 S10 一致），时间线侧装配缺省解析器——但只有技能数据声明了 `timeline.target_assist` 才生效，没声明的技能行为与未装配时逐位一致（用例 `TargetAssist_NotDeclaredBySkill_LeavesFacingAlone`）。
2. **`IUnitFacingWriter` 由生产 `WorldUnitAccess` 提供**：`CarriersAssembly` 构造 `RulesAssembly` 时传入的 `IUnitAccess` 就是 `WorldUnitAccess`（同时实现 `IUnitFacingWriter`），`CastPipeline` 持有的是同一个对象，朝向修正可落地；用例 `TargetAssist_DeclaredBySkill_TurnsTheActorByTheProfileCap_ThroughProductionAssembly` 断言朝向写入量等于 min(候选夹角, 档案 `turn_assist_deg`)。

## 手感落地 M1 收口：武器优先的普攻绑定与换装链接进生产装配（2026-10-02）

`CarriersFeelAssembly.Attach` 补两件此前只有实验室装置自己搭的东西：武器优先的动作绑定，与换装链 `EquipmentFeelChain`（发布 `feel.weapon_changed`，姿势桥订阅它）。

1. **普攻跟随武器，空手回落槽位绑定**：新增 `WeaponPreferredActionBinding`（`IActionSkillBinding`）。类别为 `attack` 的输入动作先取主手武器 `feel.weapon.auto_attack_timeline_ref` 对应的技能（`WeaponActionBinding.TryResolveAttackSkill`），武器没声明或空手时回落 `ActionSlotSkillBinding`（`skill_slot` 槽位绑定）。该绑定同时交给缓冲入口 `BufferedActionIntentSink` 与时间线协作者 `Binding`（取消进入路径），空闲首击与连段衔接口径一致。没有单独的"空手普攻技能"选项：空手普攻就是槽位绑定的那个技能。
2. **"普攻"的识别（设计层待确认）**：缺省所有类别为 `attack` 的动作都跟随武器；游戏另有自带 `skill_slot` 的攻击类动作（重击、蓄力等）时会被武器抢走，须用新增选项 `CarriersFeelOptions.AutoAttackActions` 点名真正的普攻动作，其余攻击类动作始终走槽位绑定。理由：框架无法从数据区分"普攻"与"另一个攻击键"，缺省取最常见情形（一个攻击键）并留出显式开关。
3. **换装链在生产装配里构造**：已知单位集合取世界全部实体（`save.loaded` 时按此对账）；`CarriersFeelSystem.WeaponChain` 暴露它，`Dispose` 一并释放。此前生产装配从不构造这条链，`feel.weapon_changed` 只在实验室里发布。
4. **向后兼容**：旧构造函数、`Binding`（槽位绑定）属性与 `BufferedActionIntentSink` 的旧构造重载均保留；不启用手感时一行不装配。
5. **已知限制**：同上文既有限制（手感数据热重载已在 M2-B 接线，标定热换在 M3-B 接入）。

用例见 `presentation/assembly/tests/FeelProductionWeaponChainTests.cs`（经真实 `PresentationAssembly` 与输入映射：单手剑、双手剑、空手的相位 tick 数与武器族均由数据与标定规则算出）。

## 手感落地 M2-B：手感核心侧缺口（2026-10-02）

六项缺口一次补齐，全部加法；缺省（不传 `feelOptions`、或传了但数据/动作不触及）与 1.93.0 逐位一致。本节编号为 M2-B 本节编号：

1. **宽限窗口被施法管线消费（`found.input_action.grace_conditions`，手感设计/01 第 2.4 节）**：`BufferedActionIntentSink` 把动作声明的条件名随 `cast` 意图（`grace_conditions` 数组）带给 `SkillTickHandler`，后者经 `SkillHost.CastSkillWithContext` 新增的 5 参数重载交给 `CastPipeline`；步骤 7（射程与视线）在"动作声明的条件全部满足、且至少一个正处于宽限窗口（条件刚失效、距上次为真不超过 `grace_ms` 换算的 tick 数）"时放行。**语义决定**：宽限只放宽射程/视线检查，不放宽冷却、资源、目标合法性、动作锁等其它步骤；时间线技能本来就不走步骤 6/7（空间命中由时间线自己判），不受影响。同时补了此前缺的生产接线：`InputBufferTickHandler` 现在对本地行动者与全部有缓冲的行动者登记并采样所有已声明的宽限条件名（`InputBufferHost.GraceConditionNames/LocalActorId` 为新增只读成员），`TimelineServices.Grace` 把追踪器交给管线，单位销毁时 `GraceTracker.Unregister`。
2. **数据热加载（ADR-0019，05 第 8 节）**：`CarriersFeelAssembly` 订阅 `data.load_completed`，调用 `FeelSystem.TryReload(registry)`（新增；重读 `feel.*`、过同一套 `FeelProfileChecker`，拒绝时保持当前档案并把原因写进 `CarriersFeelSystem.LastHotReload`），成功后：`FeelWeaponCatalog.Reload()`（动作绑定与换装链共用同一份目录）、未自带 `ModeRules` 时刷新运动模式规则、换装链 `ReconcileAll("data_reloaded")`（变化的单位照常发布 `feel.weapon_changed`）。**依赖约定**：宿主在 `DataRegistry.Reload(表)` 之后补发 `data.load_completed`（与其它宿主的开发期热加载同一惯例）；进行中动作的手感快照不变。
3. **光环层数叠乘**：见 `core/rules/assembly/README.md` M2-B 节（`AuraSnapshot.InstanceId` 与 `FeelTemporaryEntry.Stacks`）；`AuraFeelTemporaryProvider` 条目键改为光环实例 id，并订阅 `aura.stack_changed` 使缓存失效。
4. **换装链的单位状态口径**：`EquipmentFeelChain` 订阅 `entity.destroyed`（忘掉对账状态）与 `entity.created`（对账一次，没有武器的未跟踪单位不建记录也不发事件）。与表现侧 `EquipmentPoseBridge` 在销毁时清理姿势选择器配对：同 id 重建的单位（如换图重建的玩家）重新对账并重发 `feel.weapon_changed`，武器族补回；`save.loaded` 对账抽成公开的 `ReconcileAll(reason)`，路径不变。

已知限制（逐条交代）：热加载依赖宿主发布 `data.load_completed`（M2-B 起的约定，M3-B 后宽限条件表与标定行变化同样走这一条）。其余 M2-B 时代的限制已在 M3-B 全部解除，见下节。

## 手感落地 M3-B：宽限窗口与手感热重载的已知限制全部解除（2026-10-02）

五项一次补齐，全部加法；缺省（没有动作声明宽限条件、标定行不变、没有地面施法携带宽限条件）与 1.94.0 逐位一致（本切片落地当时的实测：feellab `suite` 186/186、`invariants` 277/277，基线未改；之后各切片陆续加了脚本，现行数字见 `lab/README.md` "数量口径"）。本节编号为 M3-B 本节编号：

1. **地面施法携带宽限条件**：`GroundCastRequest.WithGraceConditions(conditions)` 返回带条件名的副本（新增成员，既有构造不变）；`CastPipeline.CastSkillAtGround` 的步骤 7'（射程与视线）与对单位施法同一口径——条件全部满足且至少一个正处于宽限窗口时放行。**语义决定**：只放宽射程与视线，落点不可行走、冷却、资源等照常；条件"当前仍为真"时宽限不参与（条件为真是游戏对"现在可达"的声明，射程外的落点仍按落点自己的几何被拒），所以地面施法的宽限条件应表达"施法点/目标刚才还够得着"，而不是与落点无关的世界状态。请求被宽限放行后，效果落地（瞬发、读条完成、引导每个周期）的射程/视线再校验同样放行——只放宽"接受"，不因读条期间窗口过期把已接受的施法作废。
2. **排队中的施法保留宽限上下文**：对带宽限条件的请求，进入施法队列那一刻记宽限快照（`CastPipeline` 内部 `GraceSnapshot`：条件名、是否靠宽限覆盖步骤 7、窗口剩余 tick 按行动者动作时钟折成绝对到期读数）；出队执行时快照在窗口内即放行（与实时宽限查询取并集），过期则按原规则拒绝（`OutOfRange`）。**动作时钟计窗**：顿帧期间动作时钟不走，快照随之暂停——排队后顿帧的施法出队时仍在窗口内。`IGraceQuery.RemainingGraceTicks` 是为此新增的接口缺省成员（默认实现保守：不满足 -1、条件为真 `int.MaxValue`、窗口内返回 0），`GraceTracker` 覆盖给出精确值，旧第三方实现不必改。没有携带宽限条件的排队请求不记快照，行为不变。
3. **框架缺省的 Expr 宽限求值（游戏不写代码）**：新增 `ExprGraceConditionEvaluator`（`core/carriers/assembly/`）：`found.grace_condition` 每行 `expr` 在行动者上下文里求值（`self` 分组即行动者，经 `RulesAssembly.ExprHostFactory`/`ExprSchema`，`target` 分组绑定 `CarriersFeelOptions.GraceTargetResolver`，缺省取自动攻击的当前目标 `AutoAttackHost.GetTarget`，没有则不绑定）。`CarriersFeelOptions.GraceEvaluator` 仍可覆盖；不再需要提供也能用，`CarriersFeelSystem.Grace` 恒装配（没有动作声明宽限条件时不采样任何东西）。数据形态不变（`found.grace_condition` 的 `key`/`expr`/`description`，手感设计/01 第 2.4 节原定）——不需要新增字段。**注意**：没有绑定目标时 `self.distance_to_target` 取表达式宿主的缺省值 0（并记宿主诊断），"目标在射程内"一类条件在无目标时应写成 `enemies.nearest_distance <= N` 或让游戏提供目标解析委托。求值器热加载：`data.load_completed` 后重读 `found.grace_condition` 换入；新表达式写坏（数据校验不过）时保留旧条件并把原因记入 `ExprGraceConditionEvaluator.LastReloadError`。
4. **非本地行动者从登记起采样**：新增 `InputBufferHost.RegisterActor(actorId)`（幂等；建立空缓冲，`InputBufferTickHandler` 对全部有缓冲的行动者采样）与 `IsActorRegistered`。生产装配对世界里的 `player`/`creature` 实体（装配时已有的与之后出生的，订阅 `entity.created`）自动登记，第一次按键之前就有宽限历史；`CarriersFeelOptions.AutoRegisterGraceActors = false` 关闭（单位很多、只有本地玩家用宽限时），关闭后由游戏在按键前自行 `RegisterActor`。
5. **标定热更换**：`FeelSystem.TryReload` 在新数据的标定行取值变化时把档案与新标定一并换入（`FeelResolver.Reload(profiles, calibration)` 新增重载，旧重载转调）：此后每次解析与下一个动作的开始快照按新标定换算，进行中的动作快照保持旧标定下的结果（"进行中的动作沿用其开始时的快照"）；`FeelReloadResult.CalibrationChanged` 仍报告变化，但 `Applied` 为真、不再要求重启。新标定指定的基础预设不存在时整次热加载被拒绝、保持当前档案与标定。**换算范围**：标定只参与 `ToAbsolute`（参考身高、基础移速、参考镜头高度）与基础预设选择；毫秒到 tick 的换算只取决于模拟步长（不在标定行里，见 `data/_feel/feel/feel.calibration.json` 说明），所以标定变化不改变任何 tick 数。表现层消费方改读实时标定：`CameraHost.EnableFeel(source, Func<double>)` 与 `ImpactOptions.ReferenceHeightSource`（均为新增，旧固定值入口不变），`PresentationAssembly` 传入读 `feel.Calibration` 的实时委托。

用例见 `core/gameplay/assembly/tests/FeelGraceCompleteTests.cs`（五项各带"量从 X 变到 Y"的复现与不变量，边界由 `grace_ms` 换算规则算出）、`core/foundation/input_map/tests/GraceRemainingAndRegistrationTests.cs`、`core/foundation/feel/tests/FeelCalibrationHotSwapTests.cs`，以及 `presentation/camera/tests/CameraFeelTests.cs`、`presentation/feedback_binder/tests/ImpactPipelineTests.cs` 里的实时参考高度用例。

已知限制：地面施法的宽限条件须由游戏声明得能表达"落点/目标刚才还够得着"（框架不为地面坐标自动推导可达条件，可直接引用框架内置的 `input.grace.builtin_aim_*`，见 M4-G 节）。

## 手感落地 M3-E1：竖直轴装配与击飞接线（2026-10-02）

只做加法：`CarriersAssembly` 在 `MovementOptions.Vertical` 非空时创建 `VerticalMotionHost`、挂 `VerticalMotionTickHandler`（紧随 `MovementTickHandler`，同在 `MovementAndNavigation` 阶段）并暴露只读属性 `VerticalMotion`；`CarriersFeelAssembly` 在打通受击裁决与运动层之后，`VerticalMotion` 是 `ILaunchSink` 时把它接到 `HitFeelHost.Launch`（没有竖直轴时不接，档案里的 `launch_height` 被忽略）。`HeadlessWorldBuilder` 新增 `MovementOptions`/`TargetingOptions` 透传属性（缺省 null，原路径不变）。判断记录与已知局限见 unit README、targeting README、combat README 的 M3-E1 节。

## 手感落地 M4-G：宽限的施法瞄点、框架内置条件与惰性登记（2026-10-03）

1. **框架内置宽限条件**：`data/_framework/found/found.grace_condition.json` 三行（`input.grace.builtin_aim_in_range` / `..._line_of_sight` / `..._reachable`），表达式读 `event.aim_*`（`GraceAimContext`：`has_aim`、`aim_distance`、`aim_range`、`aim_in_range`、`aim_line_of_sight`、`aim_is_ground`，字段惰性计算）。游戏在动作的 `grace_conditions` 里直接引用；不引用则不采样，行为与引入前逐位一致。
2. **缺省求值器优先用施法携带的目标**：`target` 分组的来源优先级为：`CarriersFeelOptions.GraceTargetResolver`（给出非空即用，且游戏提供了覆盖后不再回落到自动攻击目标）-> 本次施法请求携带的单位目标 -> 自动攻击的当前目标。地面施法的瞄点是落点（`aim_is_ground`），`target` 不绑定。新增构造重载 `ExprGraceConditionEvaluator(registry, hosts, schema, targetResolver, fallbackTarget, aimServices)`，旧构造原样保留并转调。
3. **射程来源**：请求自己带的技能射程优先（`SkillHost.GetSkillRange` 新增）；输入动作路径下取引用该条件的动作绑定技能的射程，多个动作共用一条条件时取最小正射程。
4. **缓冲惰性分配**：没有任何动作声明宽限条件时，生产装配不给世界里的单位建缓冲；条件由空变非空（`InputBufferHost.GraceConditionsDeclared`，含数据热加载）时才补登记已有单位，之后出生的单位在 `entity.created`（下一个 tick 派发）时登记。`AutoRegisterGraceActors = false` 仍整体关闭。

判断记录（本节编号）：

1. **瞄点只改变"依赖目标"的条件的归属**：见 `core/foundation/input_map/README.md` M4-G 节。
2. **用 `event` 分组承载瞄点上下文而不新增分组**：Expr 分组固定为九个，`event` 本就是"触发事件字段"的分组，`FullExprSchema` 对 `event.*` 宽松放行，不改 schema、不增加接口。
3. **共用条件取最小正射程**：宁严勿松，避免一个射程短的动作借用另一个射程长的动作的历史。

用例见 `core/gameplay/assembly/tests/FeelGraceBuiltinTests.cs`（各项"量从 X 变到 Y"的复现与不变量）与 `core/foundation/input_map/tests/GraceAimTests.cs`。

已知限制：链式解析出来的目标（连锁技能的后续目标）不记为瞄点；瞄点只对携带宽限条件的施法请求记录；`InputBufferHost.RegisterActor` 被游戏显式调用时仍按调用分配缓冲；无头世界的视线查询恒为畅通（`StubSpatialQuery`），真实障碍物下的视线只能在带真实空间服务的世界里验证。
