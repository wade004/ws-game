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

**ADR-0050（技能宿主契约纳入技能簿查询与学习成员）收口：`DeferredSkillCastQuery` 新增成员同样
必须显式转发**——同上一条 `DeferredAuraQuery` 的判断记录同一种陷阱：`ISkillHost` 新增的
`Knows`/`GetKnownSkills`/`LearnSkill(Id,Id)`/`LearnFromBook` 四个 C#8 默认接口成员分别默认降级
为 `false`/空列表/抛 `NotSupportedException`（消费方反馈"这些能力只在具体类 `SkillHost` 上，面向
接口编程做不到"，见该 ADR），`DeferredSkillCastQuery` 本身也是 `ISkillHost` 的具体实现——不显式
覆盖就会被自己的默认接口实现接住，绑定完成后经代理调用这四个成员仍会分别得到"查不到任何已知
技能"/"学习恒失败"，不会真正转发到 `Real`（真实 `SkillHost`）。本类已为四者都写了显式转发，见其
源码判断记录。

**ADR-0058（技能宿主契约纳入光环查询与效果落地出口）收口：`DeferredSkillCastQuery` 新增
`AuraQuery`/`EffectSink` 两个成员同样必须显式转发**——同上一条判断记录同一种陷阱：`ISkillHost`
新增的 `AuraQuery`/`EffectSink` 两个 C#8 默认接口成员均默认降级为 `null`（消费方反馈第三批第 3
条"这两个已经接口化的出口只在具体类 `SkillHost` 上，面向接口编程做不到"，见该 ADR），
`DeferredSkillCastQuery` 本身也是 `ISkillHost` 的具体实现——不显式覆盖就会被自己的默认接口实现
接住，绑定完成后经代理读取这两个属性仍会恒得到 `null`，不会真正转发到 `Real`（真实
`SkillHost`）已经能提供的光环查询/效果落地出口。本类已为两者都写了显式转发，见其源码判断记录。

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

## CORE-110-02 根治（第十二轮外部审核，P2，已确认，architecture/落地计划/audit-ac3b622-20260909）

`ReloadArchetypeAndRace(unitId, classId, raceId, previousClassId, previousRaceId)`（供
`Core.Gameplay.Assembly.GameplayAssembly.DerivedStateRebuilder` 在读档后重新聚合职业/种族派生
状态时调用，见该类型判断记录）此前完全忽略 `previousClassId` 参数，只对新旧职业**共同**声明的
基础属性键调用 `StatHost.SetBase`（覆盖写入）——旧职业独有的基础属性键从未被任何调用触碰，永久
残留旧职业的基础值；`arch.class.power_types` 声明的资源类型集合同样从未随职业切换重新对账，旧
职业独有的资源类型（如法力）在新职业不再声明它之后仍可查询。真实探针复现：A 职业声明
`StatClassLegacy=5`/`Mana`，B 职业只声明共同键 `StatClassPower`；同图从 A 读到 B 后，
`StatClassPower` 正确变成 B 的值，但 `StatClassLegacy` 仍是 5、`Mana` 仍可查询（应分别为 0/不可
查询）。

根治：`ReloadArchetypeAndRace` 按"先旧种族修正（不变）→ 旧职业独有基础键 → 新职业完整基础键 →
新种族修正/光环（不变）→ 资源类型对账"的固定顺序处理：

- **旧职业独有基础键**：仅当职业确实变化（`previousClassId != classId`）且能查到旧职业定义时，
  对旧职业声明、新职业**未**声明的每个基础属性键调用新增的 `Core.Numbers.StatBlock.StatHost.
  ResetBase`（清除该单位这个属性的显式 `SetBase` 值，退回"从未显式设置过"，即
  `stat.definition.default_base`）；两边都声明的键仍只走后续"应用新职业完整基础键"的覆盖写入
  （不先清理再写入，避免多一次无意义的值变化事件）。
- **资源类型对账**：仅当职业确实变化、且新旧职业声明的资源类型集合（忽略顺序）确实不同时，才
  整体 `PowerHost.UnregisterUnit` + `RegisterUnit(cls.PowerTypes)` 替换——集合相同（含职业未变）
  时完全跳过，不重置任何资源池的当前值/上限（幂等）。放在方法最后一步，晚于全部基础属性/种族
  修正/光环重新聚合完毕之后：`PowerHost.RegisterUnit` 按注册那一刻的属性聚合值初始化资源池上限
  （`max_source.kind == "stat"` 时），若提前重新注册，初始上限会用到"还没换完"的中间态属性值。
  资源类型替换会把该单位全部资源池的当前值重置为各自的初始值（`start_full`），这是可接受的过渡
  态——本方法只在 `SaveSystem.Load` 逐段回放中被调用，随后 `player.equipment` 段会重算上限、
  `player.vitals` 段会用存档里的真实当前值覆盖这份初始值（含失败回滚路径，见
  `core/foundation/save_system/README.md`"CORE-110-01 根治"一节），最终结果不受这个过渡态影响。

验收：真实 A/B 职业 fixture 下，同图切换职业后共同基础键与 B oracle 一致、旧职业独有基础键归零、
旧职业独有资源类型不可再查询，重复读档不叠加/不报错。测试：
`core/gameplay/assembly/tests/CORE_110_FollowupAuditTests.cs`
`CORE_110_02_SameMapClassSwitch_ClearsLegacyBaseKeyAndPowerType`。

## T-N3-11：`RegisterAll` 新增 `SkillOptions` 重载，`MaxEffectsPerSkillRule` 上限接线

分阶段落地计划第 14 节 N3 任务表第十一行"`MaxEffectsPerSkillRule` 接 `SkillOptions`"：
`RegisterL2Schemas`（私有方法）此前把 `MaxEffectsPerSkillRule` 的构造参数硬编码为 8——该方法自己
的既有内联注释已指出"构造参数须与运行期实际生效的上限一致，否则数据校验期允许、运行期却又拒绝
（或反过来）"，但当时没有给调用方任何传入自定义上限的接口，只能"自己额外登记一条用同一上限构造
的 `MaxEffectsPerSkillRule`"这个变通办法（两条规则同时跑，多余但不冲突）。本任务补上这条接口：

- `RegisterAll(IDataRegistry, SkillOptions?)`：新重载，读取 `skillOptions.MaxEffectsPerSkill`
  （`SkillOptions` 该属性本身在阶段 3 整理时已随落地方案 T2-6 行登记，默认 8——本任务不新增字段，
  只补齐"注册期读取它"这一步）。`skillOptions` 为 `null` 时回退硬编码默认值 8，逐位不变。
- `RegisterAll(IDataRegistry)`（既有无参重载）：改为转发 `RegisterAll(registry, skillOptions:
  null)`，ABI 与行为均不变（回归）。
- `RegisterL2Schemas` 签名新增可选参数 `SkillOptions? skillOptions = null`（私有方法，不受 ABI
  探针约束）。

判断记录（为什么不直接改造用一份共享 `SkillOptions` 单例贯穿注册与运行期）：本类型（以及整个
`core/rules/assembly`）不持有任何跨调用的可变状态，`RegisterAll` 每次调用都是无状态的一次性注册
过程；调用方（游戏引导代码、集成测试）如果想让"数据校验期上限"与"运行期 `CastPipeline`/
`EffectDispatcher` 实际生效上限"保持一致，只需要把同一个 `SkillOptions` 实例分别传给
`RegisterAll(registry, options)` 与 `RulesAssembly` 构造函数的 `skillOptions` 参数（见该类型
第 166 行 `SkillOptions? skillOptions = null` 构造参数）——两处消费同一份数据，不需要本类型内部
再额外持有或转发一份。

ABI（G3）：只新增一个公开重载与一个私有方法的可选参数，未改动任何既有公开签名；探针基线
`ws-game-1.32.0.zip` 下 breaks=0。测试：
`core/rules/tests/Integration/T_N3_11_RulesSchemaCatalogSkillOptionsTests.cs`（3 例：无参重载回归、
显式传 `null` 与无参等价、注入自定义 `MaxEffectsPerSkill` 后加载期真的按注入值拦截——第三例特意
选在"超过注入上限但仍在硬编码默认上限 8 以内"的效果数上验证，若本任务只登记了参数却未真正接线，
这一例会假通过）。

## T-N4-4：`RulesAssembly` 新增带 `ProgressionOptions` 的构造重载（ABI 门禁 G3 约束下的写法变化）

本仓库已发布 1.33.0 基线（`toolchain/abi_probe.ps1` 按物理签名逐字节比对已发布版本）——`T-N3-11`
一节末尾"只新增一个公开重载与一个私有方法的可选参数"那种"直接在已发布构造函数末尾追加新可选参数"
的写法，在 1.33.0 发布之后不再是 ABI 安全的：物理参数列表变了，即便新参数是可选的，也会被判定为
"移除/变更"的破坏性变更（旧编译调用方按旧签名调用会找不到匹配的重载）。T-N4-4 需要给
`ProgressionHost`（经本类型第 3 步构造）接入 `ProgressionOptions?`（供
`core/gameplay/assembly.GameplayAssembly` 装配"分档 × 难度经验倍率"等策略配置项），改用"新增
重载"：

- 旧签名构造函数（20 个参数，`LevelSync? levelSync = null` 收尾）原样保留，转发到新增重载并传
  `progressionOptions: null`——不影响任何既有调用点（本类型十余处测试文件的直接构造、
  `CarriersAssembly` 若不需要 `ProgressionOptions` 时的调用），继续绑定这个未改动的物理签名。
- 新增重载（21 个参数，末尾追加 `ProgressionOptions? progressionOptions`）：C# 语法"可选参数必须
  在必选参数之后"——`progressionOptions` 若想放在参数列表最后又不带默认值（不带默认值是为了让
  "零新增参数"的调用只能匹配旧签名，不产生 CS0121 二义性），前面全部参数都必须同步改成不带默认
  值（否则违反该语法规则）。本仓库这批装配根构造函数参数量极大（20+），把新参数插进已有可选参数
  中间风险更高（唯一调用点大量使用位置参数，插入中间会让相邻位置参数静默错位，见
  `CarriersAssembly` 对本构造函数的调用），因此选择"新增重载的全部参数都不带默认值"这一更安全
  的写法：唯一调用点（`CarriersAssembly` 第 3 步）已同步显式列出全部 21 个参数。
- `core/carriers/assembly.CarriersAssembly`/`core/gameplay/assembly.GameplayAssembly` 两个上层
  装配根遇到同一约束，采用完全相同的写法（各自 README 同名小节）。
- `ProgressionHost` 本身（比本类型晚一层）沿用它自己在 T-N4-2 就确立的写法——`options` 参数插在
  既有可选参数**之前**（该类型参数量小，插入中间不影响任何位置参数调用点），两种写法的选择依据
  都是"怎么对现有调用点最安全"，不是哪种写法更"正确"。

ABI 探针复核：`toolchain/abi_probe.ps1 -BaselineZip ws-game-1.33.0.zip` breaks=0（新增重载计入
"新增"，不计入破坏）。

## 2026-09-16 深度复审 D-M2 判断记录：`LevelUpEvent → Powers.RefillAll` 订阅补 `RecomputeMax`

`LevelUpEvent` 订阅（T-N4-5，ADR-0033 决策 7"升级回满"）在调用 `Powers.RefillAll` 之前新增一行
`Powers.RecomputeMax(evt.UnitId)`——修复前只调用 `RefillAll`，而该方法读取的是 `PowerState.Max`
这一缓存字段；`ProgressionHost.AddXpCore` 里本次批量升级最后一步的属性成长写入触发的
`stat.changed` 是 `Enqueue`（不是 `PublishImmediate`），此刻还没派发，`Max` 缓存仍是"成长生效
前"的旧值，`RefillAll` 早已跑完，`Current` 永远追不上新上限（`max_source.kind=stat` 且该属性有
非零 `growth` 的资源池，这是最常见配置组合下必然触发的行为，不是边界场景）。与本模块第 341 行
`StatChangedEvent → Powers.RecomputeMax` 订阅之间不存在可依赖的顺序关系，必须在本订阅内部显式
同步调用一次——同构先例见 `core/sim/core/HeadlessWorldBuilder.cs`"出生等级 >1"场景的既有修复
判断记录。详见复审报告 D-M2 与 `core/numbers/progression/README.md` 同名小节；新增测试见
`core/rules/tests/Integration/T_N4_5_LevelUpRefillIntegrationTests.cs`。

## 判断记录（诊断契约统一转发机制，2026-09-19，architecture/adr/0042-诊断契约统一转发到宿主控制台.md）

新增 `PowerDiagnostics`（`Core.Numbers.PowerSet.IPowerDiagnostics?`）只读属性：`PowerTickHandler`
此前只在 `RegisterTickHandlers()` 内部构造并直接注册为 phase handler，构造出的实例从未被任何变量
持有，外部拿不到其默认诊断实例的引用。`RegisterTickHandlers()` 现在把构造出的 handler 先赋给局部
变量、回填本属性、再注册为 phase handler，构造顺序与注册结果不变。属性在 `autoRegisterTickHandlers`
默认值（`true`）下于构造函数返回前即已赋值；显式传 `false` 且从不手动调用
`RegisterTickHandlers()` 时保持 `null`（调用方按"该来源未提供"静默跳过，惯例同
`PresentationAssemblyDiagnosticsForwarder` 对可选来源的既有处理）。`Combat`/`Skill`/`Progression`
三个既有公开属性本身是具体类型（`CombatHost`/`SkillHost`/`ProgressionHost`），直接在各自类型上新增
`Diagnostics` 只读属性即可，不需要 `RulesAssembly` 转发（`CombatHost` 额外新增了 `_diagnostics`
字段接住此前只转发给内部 `Resolver`、自身不持有的诊断实例引用，见该类型判断记录）。
