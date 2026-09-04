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
  core/
    AiHost.cs                IAiHost 默认实现：状态机 + Rotation 求值 + 位移
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

`AiHost.Evaluate(unitId)`：仅当 `GetBehaviorState(unitId) == Combat` 时求值，否则返回 `null`。按
`ai.rotation.entries` 的 `priority` 从高到低遍历（构造期已排序、`condition` 已解析为 `ExprNode`）：

1. 用 `IExprHostFactory.CreateFor(unitId, Target, null)` 取宿主，`ExprEvaluator.EvaluateBool` 求值
   `condition`；为假则看下一条。
2. 为真则调用 `ISkillHost.CastSkill(unitId, entry.skill_id, Target 非空则 [Target] 否则 [])`。
3. `CastResult.Success` 为真：`Enqueue AiDecisionMadeEvent{unitId, decisionId=skill_id}`，返回对应的
   `SkillCastRequest`，停止遍历。
4. 为假（如 `OnCooldown`）：继续看下一条。
5. 全部条目都不满足/都施法失败：返回 `null`，不发 `ai.decision_made`。

`AiHost.Step(unitId, dt)` 是"内部驱动版本"：combat 态下按 `DecisionAccumulator` 累加 `dt`，达到
`decision_interval` 才调用一次 `Evaluate`（不足一次不求值，超过一次只补一次，`accumulator -= interval`
而非清零，避免长期漂移）。离散模式的 `TurnScheduler`（06 §6.5"调用时机"）可以绕过 `Step` 直接调用
`Evaluate`，此时不受 `decision_interval` 节奏限制，由调用方自行决定何时求值一次。

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
