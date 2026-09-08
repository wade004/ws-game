# L2 规则层 · assembly（组装根）

职责：落地方案 T2-11 集成任务"三、组装根"——`RulesSchemaCatalog.RegisterAll` 一次性注册 L0～L2
全部 `TableSchema`/`IValidationRule`（见该类型文件头注释）；`RulesAssembly` 按正确顺序把
`Core.Numbers` 五个 L1 宿主 + `core/rules` 四个 L2 宿主 + `core/rules/expr_host` 装配成一整套可用
的规则层世界，处理好其间的构造期循环依赖，把四个 tick 处理器接入 `IWorldSim`。

依赖：`Core.Numbers` 全部五个 L1 模块、`core/rules` 的 `common`/`skill`/`combat`/`targeting`/`ai`/
`expr_host`、`Core.Foundation`（`DataRegistry`/`EventBus`/`Rng`/`SimLoop`/`EngineAdapter`）。不引用
`Core.Carriers`/`Core.Gameplay`。

## 目录

```
assembly/
  README.md
  RulesSchemaCatalog.cs   注册全部 TableSchema/IValidationRule + 已知外键声明 + CreateOptions()
  RulesAssembly.cs        组装根：构造 10 个宿主、处理 2 处循环依赖、挂 4 个 tick 处理器
  DeferredEffectExtension.cs  IEffectExtension 延迟绑定代理（阶段 3 整理，见"新增可选构造参数"一节）
```

## 使用方式（调用顺序）

```csharp
// 0. 调用方自己构造 DataRegistry（本类不代为构造，见 RulesSchemaCatalog.CreateOptions 判断记录）。
var options = RulesSchemaCatalog.CreateOptions();      // ExprSchema = RulesExprSchema.Base
var registry = new DataRegistry(dataSource, bus, options);
RulesSchemaCatalog.RegisterAll(registry);               // 注册全部 schema/校验规则/已知外键
var report = registry.LoadAll();                        // 校验 + 加载

// 1. 调用方自己组装世界模拟与单位/空间查询（不属于本类职责，见 core/rules/tests/Integration）。
var world = new WorldSim(bus);
var units = new WorldUnitAccess(world);                  // 或任何其它 IUnitAccess 实现
var spatial = new StubSpatialQuery();

// 2. 组装规则层。
var rules = new RulesAssembly(bus, registry, rng, units, spatial, world);
rules.RegisterUnit(playerId, classId: new Id("arch.class.sample_a"), raceId: null, level: 1);
```

## 装配顺序图

`RulesAssembly` 构造函数按以下顺序创建内部组件（见 `RulesAssembly.cs` 构造函数内编号注释，
与下表一一对应）：

| 步骤 | 创建 | 依赖谁 | 备注 |
|---|---|---|---|
| 1 | `StatHost` | `IDataRegistryView`、`IEventBus` | `StatHostOptions.LevelLookup` 用闭包捕获尚未赋值的 `ProgressionHost` 局部变量，构造完成前不会被真正调用（惯例同 `SkillHost` 组合根） |
| 2 | `PowerHost` | `arch.power_type` 全部记录、`StatLookup`（接 1） | |
| 3 | `ProgressionHost` / `ArchetypeRegistry` | writers 接 1/2 | `ProgressionHost` 构造完成后立即回填第 1 步的闭包捕获 |
| 4 | `FactionMatrix` | `IDataRegistryView`、`IEventBus` | |
| 5 | `DeferredAuraQuery`（代理）→ `CombatHost` → `RulesExprHostFactory`（`skillHost=null`）→ `TargetHost` → `SkillHost` | 见下方"循环依赖处理" | |
| 6 | `DeferredAuraQuery.Bind(Skill.AuraQuery)` | 5 | 代理正式生效 |
| 7 | 重新构造一份 `RulesExprHostFactory`（`skillHost=Skill`） | 5、6 | 见下方"为什么重建一次" |
| 8 | `AiHost` | 1、2、4、5（`Skill`/`Combat` 的 `IThreatTable`）、7 | |
| 9 | 挂 4 个 tick 处理器到 `IWorldSim` | 全部 | 见下方"tick 阶段挂载表" |

## 循环依赖处理

**第一处（`CombatHost` ↔ `SkillHost`）**：`CombatHost` 构造函数要求一个非空 `IAuraQuery`（免疫/
吸收判定），而光环状态由 `SkillHost.AuraQuery` 提供；`SkillHost` 构造函数又要求一个非空
`ICombatHost`（效果落地的结算入口）。任务书给出两个可选方案——"先建 combat 时传入一个可延迟
绑定的 `IAuraQuery` 代理"或"在 combat 提供 `SetAuraQuery`"，本类选**前者**：`DeferredAuraQuery`
（`RulesAssembly` 内部私有类）是一个满足 `IAuraQuery` 契约的透明代理，`Bind` 之前调用任何查询
方法都抛 `InvalidOperationException`（不静默返回错误数据）。选它而不是"给 `CombatHost` 加
`SetAuraQuery`"的理由：后者要求修改 `core/rules/combat` 模块源码，超出本任务"允许改动"范围
（不含 `core/rules/combat`）；代理模式不需要触碰 `combat`/`skill` 任何一行代码就能解开循环。

**第二处（`RulesExprHostFactory` 的 `skillHost` 参数）**：`AiHost`/`TargetHost`/`SkillHost` 都要
在构造期注入 `IExprHostFactory`，但 `RulesExprHostFactory` 若要支持 `is_casting`（`self`/
`target`/`combat` 分组）就需要一个 `ISkillHost`——而 `SkillHost` 本身要到步骤 5 末尾才存在。
处理方式：步骤 5 先用 `skillHost: null` 构造一份 `ExprHostFactory` 喂给 `TargetHost`/`SkillHost`
（它们内部只用这份工厂求值 `filters`/`Proc` 条件，不涉及 `is_casting`，`null` 完全不影响正确性，
`RulesExprHostFactory` 对缺失 `ISkillHost` 的处理本就是"按默认值 `false` 处理并警告一次"而不是
抛异常）；`SkillHost` 造好之后，步骤 7 重新构造一份带 `skillHost: Skill` 的"完整版"工厂，只喂给
`AiHost`（Rotation/transitions 条件很可能引用 `is_casting`）。`RulesAssembly.ExprHostFactory`
属性对外暴露的是这份完整版。两份工厂共享其余全部依赖（`Units`/`Stats`/`Powers`/`deferredAuras`/
`Combat`/`Spatial`/`Factions`/两个时间委托），只有 `skillHost` 不同，重建成本可忽略。

**C03/C08 收口（外部审计 7e63d66 第四轮）：`DeferredAuraQuery` 新增成员必须显式转发，不能依赖
`IAuraQuery` 默认接口成员的隐式转发**——`IAuraQuery` 新增的
`ConsumeAbsorb(Id, Id, double, int triggerChainDepth)` 重载与 `InstanceReplaced` 事件都是 C#8
默认接口成员（分别默认转发到三参数重载、默认空 `add`/`remove`），目的是不强制其它测试假实现
连带改动；但 `DeferredAuraQuery` 本身也是 `IAuraQuery` 的一个具体实现——如果它不显式覆盖这两个
成员，`Real.ConsumeAbsorb(a,b,c,depth)`/`Real.InstanceReplaced += ...` 这类调用会被静态类型
`DeferredAuraQuery` 自己的默认接口实现接住，深度参数被默认转发悄悄丢回 0、事件订阅被默认空
`add` 悄悄吞掉，永远不会真正转发到 `Real`（真实 `AuraHost`）——这不是"未接入方行为不变"的安全
降级，而是"看似接入了、实际上悄悄失效"的陷阱。因此本类为这两个成员都写了显式转发（`add =>
Real.InstanceReplaced += value; remove => Real.InstanceReplaced -= value;` 同理），与既有全部
成员"逐一转发，不使用反射/动态代理"的惯例一致。

## tick 阶段挂载表

| `TickPhase` | 处理器 | 驱动 |
|---|---|---|
| `AiDecision` | `AiTickHandler` | `AiHost.Step`，AI 决策产出的 `move` 意图经 `IWorldSim.AppendCurrentIntent` 立即参与本 tick 剩余阶段（`AiHost.Evaluate` 内部直接调用 `ISkillHost.CastSkill`，技能类"决策"不经过 Intent，见 ai 模块 README 判断记录 4） |
| `SkillPipeline` | `SkillTickHandler` | 消费 `IWorldSim.CurrentIntents` 里 `Kind == "cast"` 的意图（`Args: {skill_id, targets?}`），随后 `SkillHost.Update` 推进读条/引导/冷却/光环/Proc |
| `CombatResolution` | `CombatTickHandler` | `CombatHost.Update`，只做"随时间推进"的脱战判定；效果结算本身（`ResolveEffect`）由 skill 在读条/引导完成的那一步即时调用，不经本处理器 |
| `TriggerEvaluation` | `PowerTickHandler` | `IPowerHost.AdvanceAll`（回复/衰减） |

`IntentCollection`/`MovementAndNavigation` 两个阶段本类不挂任何处理器——前者由
`IWorldSim.Tick` 自己在阶段 1 开头把 `SubmitIntent` 队列搬进 `CurrentIntents`（不需要额外处理器），
后者（把 `move` 意图真正应用成位置变化）是 L3 载体层/移动系统的职责，不在 L2 规则层范围内
（调用方需要自己挂一个消费 `move` 意图、调用 `IUnitAccess.SetPosition` 的处理器，见
`core/rules/tests/Integration` 集成测试里的最小示例）。

## 一站式单位注册

`RulesAssembly.RegisterUnit(unitId, classId, raceId?, level, factionId?, aiProfileId?, aiSpawnPoint)`：

1. `StatHost.RegisterUnit`。
2. `ArchetypeRegistry.ApplyTo`（写基础属性/种族修正、注册资源池）。
3. 若 `arch.class.level_curve_ref` 存在，`ProgressionHost.RegisterUnit`；否则跳过（"Progression
   应用"是条件性的，不是每个职业都必须挂等级曲线）。
4. 若 `aiProfileId` 非空，`AiHost.RegisterUnit`；否则跳过（"AI 可选"）。

判断记录：`factionId` 参数当前不生效——`IUnitAccess` 契约只暴露 `GetFaction`（只读查询），没有
配套的写入方法，单位的阵营归属由调用方经 `Units` 自己的注册通道决定（见 `RulesAssembly.cs`
`RegisterUnit` 参数文档）。保留这个参数位置是为了不破坏任务书给出的签名形状，但如实标注"当前
不生效"，不假装做了契约不支持的事情。

**种族被动光环跨图丢失根治（外部审核第九轮，architecture/落地计划/audit-85f1f4f-20260908）：新增
`ReapplyRacePassiveAuras(unitId, raceId)`。** `RegisterUnit` 经 `ArchetypeRegistry.ApplyTo` 施加
的 `race.PassiveAuras` 是运行时光环实例，与同一步写入的 `race.StatMods`（属性修正登记表）生命周期
不一致——跨图切换的既有实现 `World.ClearAll` 只清空前者（光环由 `AuraHost` 响应
`entity.destroyed` 清理），后者不受影响。真实内容复现：玩家实体 `ClearAll → 重新 AddEntity →
GameplayAssembly.EnterMap` 之后，种族属性加成还在，被动光环却已经消失，直到下一次显式换种族
（重新走一遍 `RegisterUnit`）才会被动补上。`ReapplyRacePassiveAuras` **不是**重新调用完整的
`ArchetypeRegistry.ApplyTo`（那会重复写基础属性/资源池注册，属性修正本就没丢，重新写一遍会产生
错误的双重叠加），只重放 `race.PassiveAuras` 这一项确实会丢失的状态。供
`core/gameplay/assembly.GameplayAssembly.EnterMap` 在装备 grants 重放之后一并调用，见
`core/gameplay/assembly/README.md`/`core/carriers/unit/README.md` 同编号条目。

**CORE-170-01 根治（第十轮外部审计，P2，architecture/落地计划/audit-8160178-20260908）：种族与
装备共享同一 `aura_def` 时卸装误删种族 aura。** 上面这版实现原本按 `Skill.AuraQuery.HasAura` 判断
"是否已生效、生效就跳过"——这个判断只问"这个 `aura_def` 在目标身上有没有活实例"，不问"生效的
这份实例，种族自己有没有登记过一份引用"。真实内容（装备 `grants.auras` 与种族 `passive_auras`
配置同一个 `aura_def`）复现：`EnterMap` 先重放装备（`EquipmentHost.ReapplyGrants`）创建了共享
光环实例并在跨来源引用计数账本上登记了一份引用，种族重放看到 `HasAura=true` 直接跳过、从未为
自己登记引用；随后卸下装备释放这唯一一份引用、计数归零，把种族仍然依赖的共享光环实例整个删除
（真实探针复现：卸装后预期 `hasAura=true,stacks=1,power=61`，实际 `hasAura=false,stacks=0,power=11`）。
根治方式：新增 `RulesAssembly.AuraHandles`（`Core.Rules.Common.AuraHandleLedger`，定义在
`core/rules/common/contracts` 而不是 `core/rules/skill`——只依赖 `IEffectSink`/`IAuraQuery` 两个
契约，方便跨层共享，见该类型判断记录），供装备/套装门槛加成（经 `CarriersAssembly` 注入
`EquipmentHost`）与种族被动（本类自己，经私有字典 `_raceAuraHandles` + `OnRaceAuraInstanceReplaced`
维护自己的句柄簿记）共享同一份跨来源引用计数——任一来源单独卸载只释放自己那一份，只有全部来源
都释放完毕才真正移除共享实例。`ReapplyRacePassiveAuras` 改为按 `_raceAuraHandles` 判断"种族这个
来源自己是否已经持有一份仍然有效的引用"，不再用 `HasAura`；需要为已存在的共享实例补登记引用时
用新增的 `IAuraQuery.TryGetInstanceRef` 取得句柄，不能重新调用 `ApplyAura`（会被 `AuraHost.
ReapplyExisting` 当作又一次独立施加而叠加层数）。验收：`Tests.Gameplay.Assembly.
CORE_170_01_RaceEquipmentSharedAuraTests`。

## 阶段 3 整理："事项一/三/四"新增的可选构造参数与延迟绑定属性

`RulesAssembly` 构造函数新增五个可选参数（均缺省保持原有行为不变）：

- `extraSchemas: IReadOnlyList<IExprSchema>?`——与 `RulesExprSchema.Base` 经
  `RulesExprSchema.Compose` 合并成本次装配实际使用的 Expr 登记表，暴露为 `ExprHostFactory`（内部
  用于 `DefaultFor` 选取默认值类型）与新增只读属性 `ExprSchema`（供调用方自己的 `DataRegistryOptions.ExprSchema`
  或另行调用 `ExprParser.Parse` 时复用同一份登记表）。典型用途：`CarriersAssembly`/游戏层组装根
  需要 `world.get`/`quest.is_active` 一类 L4 分组的精确签名时，构造一份 `ExprSchema` 传进来。
- `staticImmunity: IStaticImmunityProvider?`——透传给 `CombatHost`（→`Resolver`）与
  `SkillHost`（→`AuraHost`），在光环免疫/控制之外叠加内容驱动的静态免疫查询（如
  `core/carriers/creature.CreatureImmunityProvider`）。缺省 `NullStaticImmunityProvider`（一律
  不免疫），不改变既有行为。
- `effectExtension: IEffectExtension?`——`SkillHost` 内部实际持有的是新增只读属性
  `EffectExtension`（`DeferredEffectExtension` 代理，见该类型判断记录），本参数非空时在构造期就
  预先 `Bind` 一次；`create_item`/`open_lock`/`summon` 三类效果原语的真实组合实现（L3）要等
  `CarriersAssembly` 把 `Ai` 用完之后才能装配出来，调用方可以在 `RulesAssembly` 构造完成后随时
  再调 `rules.EffectExtension.Bind(compositeExtension)` 换上真实实现，不需要重新构造
  `RulesAssembly`/`SkillHost`。
- `autoRegisterTickHandlers: bool`（缺省 `true`）+ 新增公开方法 `RegisterTickHandlers()`——`IWorldSim.RegisterPhaseHandler`
  只能追加、不能插队，某些场景需要让另一个处理器排在 `AiTickHandler` 之前（如
  `CarriersAssembly` 的 `SummonTickHandler` 必须先于 `AiTickHandler` 挂到 `TickPhase.AiDecision`，
  见任务书拍板）。此时构造 `RulesAssembly` 时传 `autoRegisterTickHandlers: false` 跳过步骤 9，
  自己把需要排在前面的处理器注册完之后，再手动调用 `RegisterTickHandlers()` 补上 L2 的四个。

## 时间来源

`RulesExprHostFactory` 需要的 `simTimeProvider`/`combatStartTimeProvider` 不经任何专门契约
（`IWorldSim`本身不暴露"当前累计模拟秒数"）：`RulesAssembly` 订阅 `sim.tick_started`（`dt` 字段
累加）与 `combat.entered`（记录每个单位"最近一次进战"时刻的累计秒数）两个事件自行维护，详见
`RulesAssembly.TrackSimTime`/`TrackCombatStartTimes`。
