# L2 规则层 · ai

职责：AI 行为外壳状态机 + 优先级表（Rotation）驱动的决策（见
[06_规则层_属性技能战斗AI.md](../../../architecture/06_规则层_属性技能战斗AI.md) 第 6 节、
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L2 `ai` 行、
[落地方案与分阶段计划.md](../../../architecture/落地计划/落地方案与分阶段计划.md) T2-10）。

依赖：`core/rules/common` 的共享契约（`IUnitAccess`/`ISkillHost`/`IThreatTable`/`IExprHostFactory`/
`BehaviorState`/`SkillCastRequest`/`WellKnownPowers`/事件类型）、`Core.Numbers`（`IFactionMatrix`、
`IPowerHost`）、`Core.Foundation`（`expr`、`rng`、`event_bus`、`data_registry`、`sim_loop`、
`engine_adapter` 的 `ISpatialQuery`/`INavigation2D`）。不引用 `core/rules/skill`/`combat`/`targeting`
的具体类型（本模块只经 `common` 的契约接口与它们协作）。

## 目录

```
ai/
  README.md
  contracts/
    AiSchemas.cs           ai.behavior_profile / ai.rotation / ai.patrol_path 的 TableSchema
    AiOptions.cs            口味配置项
    PatrolMode.cs            loop|pingpong
    CombatReturnPolicy.cs    return_to_spawn|stay|patrol
    IRotationEvaluator.cs    T-N3-10：优先级表求值组件契约（见下"Rotation 求值组件"一节）
  core/
    AiHost.cs                IAiHost 默认实现：状态机 + 位移 + 委托 RotationEvaluator 求值
    RotationEvaluator.cs     T-N3-10：IRotationEvaluator 默认实现，不依赖 profile/Combat 态
    AiTickHandler.cs          ITickPhaseHandler，挂 TickPhase.AiDecision
    AiContentValidationRule.cs  IValidationRule：priority 唯一性/阈值范围/路径点数量/Expr 可解析性
    （阶段 3 整理：本模块原自带的临时 AiExprSchema 已删除，默认改用
     core/rules/expr_host.RulesExprSchema.Base，见 AiHost/AiContentValidationRule 构造函数注释）
    AiBehaviorProfile.cs / CompiledRotationEntry.cs / AiPatrolPath.cs / AiUnitState.cs（内部数据模型）
  schema/
    README.md               三张表字段说明与判断记录
  tests/
    ...
```

## 状态机（06 §6.1）

```
idle → patrol → chase → combat → return → flee
                                └→ dead（任意状态可进入）
```

`AiHost.RegisterUnit(unitId, profileId, spawnPoint)` 的初始状态：`profile.patrol_path_ref` 非空则
`patrol`，否则 `idle`（06 §6.1"无巡逻路径时的默认态"）。

## 默认转移条件 ↔ Expr 覆盖对照表

`ai.behavior_profile.transitions` 可以用下表的转移名覆盖对应的默认判定；未列出的转移名沿用默认判定。
覆盖条件是一段 Expr 文本，经 `IExprHostFactory.CreateFor(unitId, targetId, null)` 取得的宿主求值为
`Bool`；`targetId` 传入当前状态机跟踪的 `Target`（部分转移求值时尚无 `Target`，见下表"target 参数"列）。

| 转移名 | 默认判定（代码内置） | target 参数 |
|---|---|---|
| `idle_to_chase` | `ISpatialQuery` 在 `perception_radius` 内找到最近的、`IFactionMatrix.IsHostile` 为真、存活的单位；找到即转移 | 找到的候选（可能为空） |
| `chase_to_combat` | 与 `Target` 距离 ≤ `AiOptions.AttackRange` | 当前 `Target` |
| `chase_to_return` | `Target` 不存在/已死亡，或距 `SpawnPoint` 距离 > `leash_range` | 当前 `Target` |
| `combat_to_return` | `IThreatTable.GetTopThreat` 为空，且感知范围内找不到新的敌对单位 | 当前 `Target` |
| `combat_to_flee` | `flee_hp_pct_threshold` 非空且 `hp_pct < threshold`（仅当阈值非空才会求值本转移） | 当前 `Target` |
| `return_to_idle` | 距 `SpawnPoint` 距离 < `AiOptions.ArrivalEpsilon`（`combat_return_policy` 非 `patrol` 时使用） | `null` |
| `return_to_patrol` | 距巡逻路径起点距离 < `AiOptions.ArrivalEpsilon`（`combat_return_policy` 为 `patrol` 时使用） | `null` |
| `flee_to_return` | 脱战追击上限距离（`leash_range`）内找不到敌对单位（即视为"距最近敌对 > leash_range"） | 找到的候选（可能为空） |
| `flee_to_combat` | 与最近敌对单位距离 ≤ `AttackRange`；仅当 `AiOptions.FleeReengage` 为 true 时才会求值/触发本转移 | 最近敌对单位 |
| 任意 → `dead` | `IUnitAccess.IsAlive == false`；不可覆盖（生死判定不下放给内容作者） | — |

`combat_return_policy = stay` 时，跳过 `return` 状态本身：脱战瞬间直接在原地转 `idle`，不触发
`return_to_idle`/`return_to_patrol` 判定（见 `contracts/CombatReturnPolicy.cs` 判断记录）。

## Rotation 求值与执行（06 §6.2）

`AiHost.Evaluate(unitId)`：仅当 `GetBehaviorState(unitId) == Combat` 时求值，否则返回 `null`；
Combat 态下委托给 `RotationEvaluator`（T-N3-10，算法细节、就绪判定、无状态保证见下方"Rotation
求值组件"一节），拿到结果非空即补发 `AiDecisionMadeEvent{unitId, decisionId=skill_id}`。

`AiHost.Step(unitId, dt)` 是"内部驱动版本"：combat 态下按 `DecisionAccumulator` 累加 `dt`，达到
`decision_interval` 才调用一次 `Evaluate`（不足一次不求值，超过一次只补一次，`accumulator -= interval`
而非清零，避免长期漂移）。离散模式的 `TurnScheduler`（06 §6.5"调用时机"）可以绕过 `Step` 直接调用
`Evaluate`，此时不受 `decision_interval` 节奏限制，由调用方自行决定何时求值一次。

## Rotation 求值组件（T-N3-10，ADR-0031 决策 12/ADR-0035 决策 2）

`IRotationEvaluator`/`RotationEvaluator`（`contracts/IRotationEvaluator.cs`、`core/RotationEvaluator.cs`）
把上一节"Rotation 求值与执行"的选择逻辑抽成一个独立组件：不依赖 `ai.behavior_profile`、不要求
`BehaviorState.Combat`，只要一个 `ai.rotation` 表 id 即可对任意单位求值一次——玩家单位不需要经
`RegisterUnit` 注册 AI 行为外壳，也不需要进入 `Combat` 态，即可复用同一份"按优先级挑第一个可施放
技能"的算法（ADR-0031 决策 12"一键智能释放"；ADR-0035 决策 2 要求仿真骨架的标准玩家生成器与 AI
共用同一求值组件，不各自重复实现）。

`AiHost.Evaluate(unitId)` 现在只做两件独属于 `AiHost` 自己的事，其余全部委托：

1. 门控：`GetBehaviorState(unitId) != Combat` 时直接返回 `null`（不越过组件白算一次）。
2. 委托：调用 `_rotationEvaluator.Evaluate(unitId, state.RotationId, state.Target)`。
3. 收尾：结果非空时补发 `AiDecisionMadeEvent`（组件本身不知道"决策事件"这个 AI 专属概念）。

`RotationEvaluator.Evaluate(unitId, rotationId, targetId)` 按 `entries` 的 `priority` 从高到低
遍历（构造期已排序、`condition` 已解析、"敌对单体"分类已算好，判据见下方"无状态保证"）：

1. `IExprHostFactory.CreateFor(unitId, targetId, null)` 求值 `condition`，为假看下一条。
2. 经 `ISkillHost.GetSkillReadiness(unitId, entry.SkillId).IsReady` 判定是否就绪，不就绪看下一条
   ——就绪判定统一走这一份只读查询（1.21.0 引入，T-N3-4 起覆盖 `ConditionNotMet`/`ActionLocked`），
   不自行用 `IsCasting`/`GetCooldown` 等零散状态推断，避免对已知不就绪的技能白发一次
   `CastSkill`（及其 `skill.cast_failed` 事件）。
3. 就绪则真的调用 `ISkillHost.CastSkill(unitId, entry.SkillId, targets)`，`Success` 为真返回对应
   请求，为假（如目标不合法/资源不足——`GetSkillReadiness` 不覆盖这些原因，见该方法契约文档）看
   下一条。
4. 全部条目都不满足：返回 `null`。

无状态保证（硬性规则"禁止求值组件持有单位状态"）：`RotationEvaluator` 只在构造期一次性从
`IDataRegistryView` 编译全部 `ai.rotation` 记录到一份私有只读字典（`priority` 排序、`condition`
解析、"敌对单体"分类均只算一次），此后只读——这是"内容编译缓存"，不随传入哪个 `unitId` 变化，
不是"单位状态"；`Evaluate` 本身不写任何实例字段，每次调用只读入参与该缓存。已知限制（非本任务
新引入，`AiHost` 迁移前同样如此）：构造完成后 `IDataRegistry.Reload`（开发期热重载）不会使这份
缓存失效，需要整体重建一个新实例。

## 位移

`chase`/`return`/`patrol`/`flee` 四个状态在 `Step` 里产生 `Intent(unitId, "move", {dx, dy})`：
`dx`/`dy` = 方向向量（归一化） × `AiOptions.MoveSpeed` × `dt`。方向计算：`AiOptions.MapId` 非空且
构造时传入了 `INavigation2D` 才调用 `FindPath` 取路径第二个点的方向，否则直线方向（详见"契约缺口"）。
`flee` 态的方向是"远离最近敌对单位"，不经过寻路（逃跑没有固定的"目的地点"概念，见判断记录）。
`combat` 态不产生位移意图（原地施法）。

`AiHost.Step` 本身不持有 `IWorldSim`，只返回本次产生的 `Intent` 列表；由 `AiTickHandler.Execute`
负责 `world.SubmitIntent`（详见"契约缺口"一节，这是本任务已知的一 tick 延迟来源）。

## 契约缺口

1. **`IWorldSim` 没有"追加进本 tick 当前意图列表"的方法**：只有 `SubmitIntent`（进入"下一 tick 待
   收集"队列）与只读的 `CurrentIntents`（本 tick 阶段 1 固定的快照）。03 第 4.2 节步骤 2 文字描述
   "AI 决策……追加进意图列表"暗示 AI 产生的意图应该在同一 tick 内继续走完步骤 3～8，但契约当前
   只能做到"下一 tick 生效"。`AiTickHandler` 按契约现状用 `SubmitIntent`，移动结算相对决策存在
   一 tick 延迟（详见 `AiTickHandler.cs` 顶部注释）。建议集成任务给 `IWorldSim` 补一个
   `AppendCurrentIntent(Intent)`（或等价物），语义为"追加进本 tick 的 `CurrentIntents`，仅在阶段
   1～7 之间可调用"。
2. **`IUnitAccess` 不暴露单位所属地图 id**：`MapId` 只存在于 `Core.Foundation.SimLoop.Entity`
   （L3 载体层把 `Unit` 接入 `IWorldSim` 时才会用到），而 `INavigation2D` 的全部方法都要求传入
   `mapId`。本模块用 `AiOptions.MapId` 表示"AI 使用的寻路地图"这一单地图场景的权宜之计
   （见 `AiOptions.MapId` 判断记录），多地图场景需要集成任务扩展 `IUnitAccess` 暴露 `GetMapId`。

## 判断记录

1. **`combat_return_policy = stay` 的语义**：06 第 6.1 节 `return` 状态原文只给出"到达后 → `idle`
   或 `patrol`"两个去向，任务书拍板的枚举多出第三档 `stay`，字面意为"原地留守"——本模块判定为
   "跳过 `return` 状态本身的行进过程，脱战瞬间直接在当前位置转 `idle`"，区别于 `return_to_spawn`
   仍需先走回出生点。见 `contracts/CombatReturnPolicy.cs`。
2. **`RandomTieBreak` 的适用场景**：`ai.rotation.entries` 的 `priority` 由
   `AiContentValidationRule` 校验为同表内不重复，因此 Rotation 候选技能之间不存在"平局"；
   `AiOptions.RandomTieBreak` 实际用于"最近敌对单位"选择出现等距离候选时的平局打破
   （`FindNearestHostile`），默认（false）按 `Id` 序数取第一个，为 true 时经
   `IRngHost.NextInt(RngStream, 0, 候选数-1)` 分流随机选取，满足"随机必须经 RngHost 分流"的
   禁止事项。
3. **`transitions` 覆盖只影响判定的布尔结果，不影响目标选择等副作用**：例如 `idle_to_chase` 被
   覆盖为真但 `ISpatialQuery` 感知范围内确实找不到任何敌对单位时，本模块选择"不转移"（没有可供
   `chase` 的目标，转移了也无意义），而不是转移到一个 `Target == null` 的 `chase` 态。
4. **`AiHost.Evaluate` 会直接调用 `ISkillHost.CastSkill`，不是只挑选候选**：06 §6.2"第一个满足
   且施法管线检查通过的条目被执行"要求实际尝试施法才能判断"检查通过"（如 `OnCooldown` 只有真的
   调一次 `CastSkill` 才知道），因此 `Evaluate` 本身是"求值 + 执行"合一，返回值是"已经生效的
   请求"，不是"建议稍后由调用方执行的请求"。这与 03 第 4.2 节"AI 决策产出意图，技能管线阶段再
   处理"的抽象描述不完全一致，任务书对 06 §6.2/6.5 的具体措辞（`CastSkill`+`ai.decision_made`）
   是更具体的拍板，本模块按任务书措辞实现；技能类意图是否也应该改走"提交 `cast` 类型 Intent 交给
   技能管线阶段处理"是集成期可能需要重新核对的点，本模块不擅自决定。
5. **RC-10 收口（第四方深度审核）：只有"敌对单体"技能才把当前追踪目标强塞给 `CastSkill`**——
   原实现对 Rotation 里的每一条都无条件把 `state.Target` 当成目标传给 `CastSkill`，`CastPipeline`
   见到非空 `targets` 就跳过技能自身的目标链解析，导致自疗可能作用到敌人、友疗/AOE 的目标过滤
   形同虚设。`LoadRotations` 现在按 `skill.def.target_shape_ref` 只读探测（`filters` 含
   `relation:hostile`、不含 `relation:friendly`、`max_targets` 为 1）把每条 Rotation 条目分类为
   `CompiledRotationEntry.IsHostileSingleTarget`；只有分类为真时才强塞 `state.Target`，其余一律
   传空数组，交由技能自己的 `target_shape_ref` 目标链解析（自疗/友疗/AOE 走各自 chain）。技能
   未登记/字段读取异常等任何"读不出"情形保守判 false（不强塞，比猜错更安全）。见
   `AiHost.cs`/`CompiledRotationEntry.cs`、`AiRotationTargetingTests.cs`。
6. **T-N3-10：`RotationEvaluator` 在条件为真之后、`CastSkill` 之前先查一次
   `ISkillHost.GetSkillReadiness`，不就绪直接跳过、不调用 `CastSkill`**——迁移前（`AiHost` 内联实现
   时期）没有这一步预筛，条件为真的每一条都会真的尝试一次 `CastSkill`，靠其返回值判断"不就绪"
   （如既有 `AiRotationTests.Evaluate_OnCooldownEntry_FallsBackToNextPriority` 用例）。两种写法对
   "最终选中哪一条"结果等价（`GetSkillReadiness.IsReady` 与随后 `CastSkill` 是否会因冷却/充能/
   公共冷却/节拍锁/使用条件而失败，按契约定义逐位对应），差异只在于：不就绪的候选不再产生一次
   `skill.cast_failed` 事件与相应的 `CastSkill` 调用记录。选择加这一步预筛而不是让 `AiHost` 既有
   "先斩后奏"写法原样保留，是任务书 T-N3-10 的显式要求（"就绪判定复用 `ISkillHost.
   GetSkillReadiness`，不自行推断"）；`AiHost` 既有测试用例的假实现 `FakeSkillHost` 未编程
   `GetSkillReadiness` 时走与 `ISkillHost` 默认接口实现同一"按 `GetCooldown` 推断"公式（恒判定
   就绪，因为 `FakeSkillHost.GetCooldown` 恒为 0），因此既有用例仍然会真的调用一次 `CastSkill`
   才发现失败，全部原样通过，未观察到任何行为差异。见 `RotationEvaluator.cs`、
   `RotationEvaluatorTests.cs`（首条不就绪跳过选下一条，冷却/`ConditionNotMet`/`ActionLocked`
   三态各一组）。
