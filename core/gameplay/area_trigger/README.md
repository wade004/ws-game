# L4 玩法层 · area_trigger（区域触发）

职责：落地 05_对象模型与世界.md 第 7、7.1 节 `AreaTrigger`——`area.trigger_def` 数据驱动的四种触发
类型（`map_transition`/`quest_explore`/`encounter_start`/`script`）登记、单位进入/离开范围检测、
`condition` 附加条件求值、`one_shot` 已触发状态（经 `WorldState` 记录）；`RegisterTrap` 额外支持
`core/carriers/gobj` 陷阱物件动态登记的进入检测（见 07 第 3.1 节 `trap`）。对应 01 第 L4 模块表
`area_trigger` 行（契约 `AreaTriggerHost.register(def)`/`unregister(triggerId)`/`evaluate(unitId,
position)`、数据表 `area.trigger_def`、事件 `area.trigger_entered`/`area.trigger_left`）。ADR-0019 /
F1b（复合字段子结构登记）的子结构登记表、变体参数表、退役规则与判断记录见
[schema/README.md](schema/README.md)。

依赖：L0（`data_registry`/`event_bus`/`expr`/`hook_registry`/`scene_router`/`sim_loop`）、L4
`core/gameplay/world_state`（`IWorldState`，`one_shot` 标志）、L2 `core/rules/expr_host`
（`RulesExprSchema.Compose`，见 `AreaTriggerHost` 判断记录）。经 `Core.Gameplay.csproj` 既有引用传递
可见，本模块不新增任何 `ProjectReference`。

## 目录

```
area_trigger/
  README.md
  contracts/
    AreaTriggerEntity.cs          Entity 子类，Kind="area_trigger"（加固任务新增，见判断记录 1）；
                                   补 BoundingRadius（按 Shape 算外接半径，见判断记录 7）
    AreaTriggerType.cs             AreaTriggerType 枚举（四种数据驱动类型）+ 内部 AreaTriggerKind（含 Trap）
    AreaTriggerParams.cs           MapTransitionParams/EncounterStartParams/ScriptParams 强类型 params 视图
    AreaTriggerOptions.cs          回调委托 + WriterId 策略配置
    IAreaTriggerHost.cs            AreaTriggerHost 契约（含 05 原文之外任务书拍板补充的成员）
    AreaTriggerSchemas.cs          area.trigger_def 的 TableSchema
    AreaTriggerExprSchemaEntries.cs 空登记表（本模块不新增 Expr 分组/键）
    Events.cs                      AreaTriggerEventKeys + AreaTriggerEnteredEvent/AreaTriggerLeftEvent
    IAreaTriggerDiagnostics.cs     诊断出口契约
  core/
    AreaTriggerDef.cs               DataRecord -> AreaTriggerDef（运行期与校验期共用）
    AreaTriggerHost.cs               IAreaTriggerHost 唯一实现（登记表 + Evaluate + 实体创建/销毁）
    AreaTriggerShapeGeometry.cs      "点是否落在 Shape 内"纯几何判定
    AreaTriggerShapeJson.cs          shape 字段 JSON -> Shape
    AreaTriggerTickHandler.cs        挂 TickPhase.TriggerEvaluation，驱动 Evaluate
    InMemoryAreaTriggerDiagnostics.cs 默认诊断实现
  schema/
    README.md                       ADR-0019 / F1b 子结构登记表 + 变体参数表 + 退役规则 + 判断记录
    AreaTriggerValidationRules.cs   params 字段组完整性非阻断式校验（shape.kind 检查已退役，见
                                     schema/README.md"退役规则"一节）
  tests/
    TestSupport.cs                  DataRegistry/EventBus/Fake 装配帮助
    AreaTriggerHostTests.cs          Register/Unregister/Evaluate/LoadForMap/UnloadMap/RegisterTrap 用例
    AreaTriggerDefTests.cs           AreaTriggerDef.FromRecord 解析用例
    AreaTriggerSchemaCoverageTests.cs ADR-0019 / F1b：shape/params 子结构命中/坏形状 + 变体键集合一致性
    AreaTriggerShapeGeometryTests.cs Contains 几何判定用例
    AreaTriggerTickHandlerTests.cs   tick 挂载/位置变化检测用例
    AreaTriggerValidationRuleTests.cs 校验规则用例（params 字段组；shape.kind 用例已迁移到
                                       AreaTriggerSchemaCoverageTests）
```

## 判断记录

1. **实体化（加固任务）：从"纯数据记录 + 宿主字典"改为真正的 `Entity` 子类**——此前
   `AreaTriggerHost` 只在内部 `SortedDictionary<Id, RuntimeEntry>` 持有触发定义，不产生任何
   `Entity` 实例，不符合 05 第 1 节继承树"`AreaTrigger` 是 `Entity` 叶子类型之一"、第 1.5 节字段表
   的结论（任务拍板：改代码，不改文档结论）。新增 `AreaTriggerEntity : Entity`（`contracts/`），
   只持有 05 第 1.5 节明确列出的四个字段（`Shape`/`TriggerType`/`ConditionText`/`OneShot`）；基类
   `Position` 取 `Shape.Origin`（circle 是圆心，其余三种形状是几何原点，作为"形状中心"的统一近似）、
   `MapId` 取 `def.MapId`、`TemplateId` 取 `def.Id`（内容模板 id）。`AreaTriggerHost.Register`/
   `RegisterTrap` 改为经 `IWorldSim.AllocateEntityId`+`AddEntity` 创建对应实体；`Unregister`/
   `UnloadMap` 改为经 `IWorldSim.MarkForDestruction` 销毁。构造函数新增 `IWorldSim world` 依赖
   （见其判断记录顺序：置于 `IWorldState worldState` 之前）。

2. **字典键仍用 `TriggerId`，不改用 `EntityId`（二选一，见任务书）**：`IAreaTriggerHost.Register`/
   `Unregister`/`RegisterTrap` 的契约签名（入参/返回值都是 `triggerId`，见 05 第 7.1 节）不能变——
   调用方（`core/carriers/gobj` 的陷阱登记、既有测试）都按 `triggerId` 引用触发体。`RuntimeEntry`
   因此新增一个 `EntityId` 字段记录本次登记对应创建的 `AreaTriggerEntity` 运行期实例 id（由
   `IWorldSim.AllocateEntityId` 独立分配，语义上是"运行期唯一实例 id"，与 `TriggerId`——内容模板 id
   或陷阱内部生成 id——不是同一件事，见 05 第 1.1 节 `entityId`/`templateId` 字段表），`_entries`
   （`SortedDictionary<Id, RuntimeEntry>`，键仍是 `TriggerId`）保持"按 `TriggerId` 序数遍历"的既有
   确定性遍历顺序不变，`Evaluate` 不需要任何改动。

3. **运行期簿记留在宿主，不搬进实体**：`_inside`（当前处于"已成功进入"状态的 `(triggerId, unitId)`
   组合）、`ConditionNode`（`ConditionText` 解析出的表达式树缓存）、`MapTransition`/
   `EncounterStart`/`Script` 各类型具体 params、`_nextTrapSeq` 计数器等继续留在
   `AreaTriggerHost` 内部的 `RuntimeEntry`/字段（惯例同 `core/carriers/projectile`
   `ProjectileEntity`/`ProjectileHost` 判断记录："实体只放对象模型字段，运行期簿记留宿主"）——它们
   要么不是 05 第 1.5 节列出的对象模型字段，要么是可以从实体字段重新派生的缓存（`ConditionNode`
   由 `ConditionText` 解析得到），不重复搬到 `AreaTriggerEntity`。

4. **陷阱共用同一个 `Entity.Kind`、`TriggerType` 为 null**：07 第 3.1 节 `trap` 类型复用区域触发的
   进入检测机制（05 第 7 节变更记录 2026-09-05"陷阱类物件复用区域触发的进入检测机制……不在
   `trigger_type` 枚举中另列"），`RegisterTrap` 动态登记的触发体因此同样生成一个
   `Kind = EntityKinds.AreaTrigger` 的 `AreaTriggerEntity`，但 `TriggerType` 为 null（陷阱不是 05
   第 1.5 节 `triggerType` 四选一枚举的合法取值）、`TemplateId` 为 null（陷阱不是内容驱动，没有
   `area.trigger_def` 模板 id）；内部用哪一种调度种类区分——含 `Trap`——的完整信息仍在
   `AreaTriggerHost` 内部 `RuntimeEntry.Kind`（`AreaTriggerKind`，判断记录 2 之外的既有字段）持有。

5. **同一 `triggerId` 重复 `Register` 的防御性处理**：理论上不应发生（内容管线保证
   `area.trigger_def.id` 唯一），但此前的纯字典实现允许静默覆盖；实体化后若不先处理就覆盖
   `_entries`，会在 `IWorldSim` 里残留一个"孤儿" `AreaTriggerEntity`（`Unregister` 再也找不到它的
   `EntityId`）。`Register` 命中同 `TriggerId` 的既有登记时，先 `MarkForDestruction` 旧实体再创建
   新实体，保持"一个 `TriggerId` 对应至多一个存活实体"的不变量。

6. **不进存档**：见 05 第 1.5 节"进存档：手工放置的固定触发体不进存档（随地图数据加载）；`oneShot`
   已触发的状态经 `WorldState` 记录"——`AreaTriggerEntity`/`AreaTriggerHost` 均不实现
   `Core.Foundation.SaveSystem.IPersistable`（见 `AreaTriggerHostTests.
   AreaTriggerHostAndEntity_DoNotImplementIPersistable` 锁定用例，惯例同
   `ProjectileHostTests.ProjectileHostAndEntity_DoNotImplementIPersistable`）。场景卸载时
   `IWorldSim.ClearAll`/`AreaTriggerHost.UnloadMap` 销毁的实体，在下次
   `AreaTriggerHost.LoadForMap`（`GameplayAssembly.EnterMap` 调用的 `post_load` 路径）时按数据重新
   创建，不依赖任何存档恢复路径。

7. **参与 `ISpatialQuery` 空间索引，但打 `trigger_only` 单一标签、被各查询点显式排除**（加固任务，
   05 §3.6 碰撞层规划落地，原判断记录"不参与空间索引"已改变，见
   `architecture/落地计划/文档代码一致性审计_2026-09-06.md`"碰撞层"条目"已拍板：补实现"）：
   `Core.Carriers.Assembly.CarriersAssembly.DefaultSpatialSyncKinds` 现在把 `EntityKinds.AreaTrigger`
   加入白名单，登记时打 `Core.Foundation.EngineAdapter.CollisionLayers.TriggerOnly` 单一标签（不含
   `"unit"`），半径取 `AreaTriggerEntity.BoundingRadius`（本模块新增，按 `Shape` 计算外接半径，见
   该属性判断记录；`Core.Gameplay.Assembly.GameplayAssembly` 通过
   `EntitySpatialSyncHost.KindConfig.RadiusResolver` 委托接上，`CarriersAssembly` 自身因 L3 不能依赖
   L4 只能退回固定近似值 0.5）。刻意登记的目的是让"按 `trigger_only` 标签查询范围内触发体"一类
   未来查询成为可能；为避免区域触发体被现有"最近敌人"/"范围内单位"一类查询误当命中目标，
   `core/rules/targeting/core/BuiltinTargetStrategies.cs`（`nearest_in_shape`/`all_in_shape`）、
   `core/rules/ai/core/AiHost.cs`（`FindNearestHostile`）、
   `core/rules/expr_host/RulesExprHostFactory.cs`（`enemies.*`）、
   `core/gameplay/assembly/TimeModelSwitch.cs`（`ResolveParticipants`）四处不带 `RequiredTags` 的
   查询点已同一批任务补上 `ExcludedTags = [CollisionLayers.TriggerOnly]`（见各自判断记录）；带
   `RequiredTags: ["unit"]` 的查询（如 `core/carriers/projectile` 的 `ProjectileOptions.HitQueryTags`
   默认值）天然排除，未改动。

8. **表现层显式跳过，不查外形映射**：区域触发实体没有可见外观（05 第 7 节"只负责检测进入/离开+
   发事件，不内含具体业务逻辑"）；`Presentation.ViewBinding.ViewBinder.OnEntityCreated` 对
   `kind == EntityKinds.AreaTrigger` 显式提前返回——不创建 View、不查 `display.map`、不记入
   `UnmappedEntityKinds` 诊断列表（那份列表是给"确有渲染需求但内容漏配"场景用的，不是本情形）。
   `presentation/common/contracts/EntityKindMapping.cs` 的 `"area_trigger"` 字面量分支同步改用
   `EntityKinds.AreaTrigger` 常量（此前 `EntityKinds` 未登记该常量时的占位写法）。

## 不负责什么

- 不负责移动系统在位置变化后主动回调 `Evaluate`（05 原文如此描述）——`core/carriers/unit` 不在本
  模块允许改动范围内，实际由 `AreaTriggerTickHandler` 在 `TickPhase.TriggerEvaluation` 阶段对全部
  单位做位置差异检测后调用，见该类型判断记录。
- 不负责"陷阱是否启用/禁用"——由 `gobj.lock`/`gobj.template.state` 等 L3 机制控制，`RegisterTrap`
  只负责进入检测与委托触发。
- 不负责 `map_transition` 传送到具体出生点的落点计算——`spawn_point` 原样透传给
  `AreaTriggerOptions.MapTransitionRequested`，由组装层结合 `post_load` 钩子完成实际落点。
