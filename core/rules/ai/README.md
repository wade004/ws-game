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
idle → patrol → chase ⇄ combat → return → flee
                                └→ dead（任意状态可进入）
```

`combat → chase`（ADR-0084 新增 `combat_to_chase`）：目标脱离攻击范围但仍在追击范围内时回追，
复用 `chase` 态既有的移动与 `leash_range` 拴绳判定，回追后仍可能再次 `chase → combat`，因此画作
`⇄`（详见下表 `combat_to_chase`/`chase_to_combat` 两行、判断记录 8）。

`AiHost.RegisterUnit(unitId, profileId, spawnPoint)` 的初始状态：`profile.patrol_path_ref` 非空则
`patrol`，否则 `idle`（06 §6.1"无巡逻路径时的默认态"）。

## 默认转移条件 ↔ Expr 覆盖对照表

`ai.behavior_profile.transitions` 可以用下表的转移名覆盖对应的默认判定；未列出的转移名沿用默认判定。
覆盖条件是一段 Expr 文本，经 `IExprHostFactory.CreateFor(unitId, targetId, null)` 取得的宿主求值为
`Bool`；`targetId` 传入当前状态机跟踪的 `Target`（部分转移求值时尚无 `Target`，见下表"target 参数"列）。

| 转移名 | 默认判定（代码内置） | target 参数 |
|---|---|---|
| `idle_to_chase` | 候选来源两级（ADR-0087，见判断记录 9）：① `ISpatialQuery` 在 `perception_radius` 内找到的最近敌对存活单位，找到即用；② 找不到时，若本单位 `ICombatHost.IsInCombat` 为真，退回取 `IThreatTable.GetTopThreat`——仍存活、仍敌对（`IFactionMatrix.IsHostile`）、且未超出 `leash_range` 才采用，否则视为无候选。找到候选即转移 | 找到的候选（可能为空） |
| `chase_to_combat` | 与 `Target` 距离 ≤ `AiOptions.AttackRange * AiOptions.CombatReentryRangeRatio`（ADR-0084：余量放在重进战一侧，见判断记录 8） | 当前 `Target` |
| `chase_to_return` | `Target` 不存在/已死亡，或距 `SpawnPoint` 距离 > `leash_range` | 当前 `Target` |
| `combat_to_chase` | `Target` 存在（存活）且与它的距离 > `AiOptions.AttackRange`（ADR-0084：不加余量，`combat` 期间目标只要仍在攻击距离内就不判定回追，不存在死区，见判断记录 8） | 当前 `Target` |
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
7. **`AiTickHandler` 跳过已标记销毁的单位（2026-09-23，消费方反馈第二十二批，
   architecture/adr/0079-销毁时序对齐与待销毁单位跳过处理.md）**：连续模式遍历
   `_host.RegisteredUnitIds`、离散模式处理当前行动者前，各自新增一次
   `world.IsPendingDestruction(unitId)` 判断，命中即本拍不再为该单位求新的 AI 决策（不调用
   `_host.Step`）。动机：`IWorldSim.Despawn` 标记销毁与真正移除（`Tick` 阶段 8）之间有窗口期，
   `AiHost` 对该单位的行为外壳登记要到同一阶段的 `EntityDestroyedEvent` 才清理——不加这层判断，
   窗口期内仍会继续为它生成移动意图，交给 `core/carriers/unit.MovementTickHandler` 处理（该
   处理器也做了同样的跳过，见其判断记录）。两处判断独立生效、互相印证：本处理器不生成新意图，
   移动处理器即便收到"历史遗留"的意图也不会再处理。
8. **ADR-0084：新增 `combat_to_chase`，`chase_to_combat` 默认阈值同步收紧（消费方反馈第二十九批，
   阻塞项）**——根治前 `HandleCombat` 只刷新 `Target`、判定 `combat_to_flee`/`combat_to_return`，
   从不产生 `move` 意图、也没有任何 transition 会在目标脱离攻击范围但仍存活/仍在追击范围内时把
   单位带出 `combat`：单位卡在原地反复 `Evaluate` 只会拿到 `OutOfRange`，永远不移动，直到目标彻底
   消失/死亡或（极端情况下自身产生的 `combat_to_return` 覆盖条件）才有机会脱离。新增的
   `combat_to_chase` 只做"判定 + 转移"，位移与 `leash_range` 拴绳判定完全复用 `chase` 态既有实现
   （`HandleChase`/`chase_to_return`），不在 `combat` 态另写一套。
   <br/>滞回余量放在重进战一侧、不放在退出一侧：`combat_to_chase` 固定用裸 `AttackRange`
   （`distance > AttackRange`，不加余量）——`combat` 期间目标只要仍在攻击距离内，判定就不会为真，
   不存在"处于 combat、目标却已经打不到"的死区。`chase_to_combat` 改用
   `AttackRange * AiOptions.CombatReentryRangeRatio`（新增 `AiOptions` 字段，默认 0.75，取值域
   `(0, 1]`，越界在 `AiHost` 构造期抛 `ArgumentOutOfRangeException`，不静默夹紧）——`chase` 态要
   贴近到比攻击距离更近一截才重新进入 `combat`，进出两个阈值之间天然隔着一段宽度为
   `AttackRange * (1 - CombatReentryRangeRatio)` 的缓冲区：目标停在 `[AttackRange *
   CombatReentryRangeRatio, AttackRange]` 之间做亚阈值抖动，两个方向的判定都不会翻转（`chase_to_combat`
   持续为真、`combat_to_chase` 持续为假），单位全程留在 `combat`、始终能打到目标。余量按
   `AttackRange` 的比例定义而非绝对距离：绝对余量在小攻击距离（近战）下会让重进战阈值逼近零甚至
   为负，比例定义不受攻击距离量级影响。默认 0.75（先追近到 75% 攻击距离才重新开打，留 25% 攻击
   距离宽度的缓冲），纯口味参数，不是架构层面的固定语义。回追转移评估不受 `decision_interval`
   节流（与 `combat_to_return`/`combat_to_flee` 等既有转移同一时机，每 tick 判定），保证目标一旦
   超距、下一个 `Step` 就开始朝它移动。
   <br/>**行为变更**：`chase_to_combat` 默认判定本身被收紧（`AttackRange` → `AttackRange *
   CombatReentryRangeRatio`），单位要追得比以前更近才会开打——已经用 `ai.behavior_profile.transitions`
   的 Expr 覆盖 `chase_to_combat` 的内容数据不受影响，覆盖表达式完全替代默认判定，本次改动只影响
   "没有覆盖时"的兜底计算。
   <br/>召唤物联动实测（`JoinCombat=true`）：`core/carriers/summon/core/SummonTickHandler.TryFollow`
   在召唤物 `ICombatHost.IsInCombat` 为真且 `JoinCombat=true` 时完全跳过跟随（战斗期间"打"和"跟"
   二选一，设计如此，本任务不改）——根治前召唤物在 `combat` 态卡死不动、`TryFollow` 又不跟随，
   表现为全程 0 输出、原地冻结；根治后召唤物自己的 `AiHost` 状态机与主人的 `AiHost` 状态机相互独立
   （各自登记、各自判定），目标脱离召唤物的攻击范围时召唤物按本转移回追，回追超出召唤物自身的
   `leash_range`（拴绳原点是 `RegisterUnit` 传入的 `spawnPoint`，即 `SummonHost.Summon` 调用时的
   召唤位置——一个固定点，不随主人当前位置变化）时按既有 `chase_to_return`/`TransitionTowardReturn`
   脱战，回到该固定点后转 `idle`（`combat_return_policy` 为默认 `return_to_spawn` 时）；`IsInCombat`
   与 `AiHost.BehaviorState` 是两条独立时间线，前者由 `CombatHost.NotifyCombatEvent` +
   `CombatOptions.LeaveCombatDelay` 超时判定（不感知召唤物 AI 是否已经脱战），召唤物回到固定点转
   `idle` 之后，只要不再发生新的战斗事件，`IsInCombat` 会在 `LeaveCombatDelay` 内自然转 false，
   `TryFollow` 随即恢复跟随并计算"当前"主人位置——最终会跟上主人，不会永久卡在旧位置，只是这段
   `LeaveCombatDelay` 窗口期内会先在固定点停留。见 `AiCombatRechaseTests.cs`（不变量 (a)/(b)/
   Expr 覆盖）、`core/carriers/assembly/tests/SummonCombatRechaseTests.cs`（召唤物联动实测，
   `CarriersAssembly` 真实装配 + `world.Tick` 驱动）。
   <br/>**本段结论已被 ADR-0087（判断记录 9）部分修订**：上面"`JoinCombat=true` 时 `TryFollow`
   完全跳过跟随，战斗期间'打'和'跟'二选一，设计如此"这一刀切规则在 `IsInCombat` 与召唤物自身
   `AiHost` 状态机脱节时会导致召唤物原地冻结——已改为按召唤物自己的行为态判定（`Chase`/`Combat`/
   `Return`/`Flee` 才跳过跟随，`Idle`/`Patrol` 放行），本段其余关于两条独立时间线、`leash_range`
   拴绳、`combat_return_policy` 回落点的描述不受影响、继续成立。

9. **ADR-0087（消费方第三十三批反馈1，idle/patrol 默认转移候选来源扩展）**：召唤物场景下
   `SummonTickHandler` 经 `SyncCombatState` 把召唤物同步进战（`IsInCombat=true`）、`ShareThreat`
   把召唤物累计的仇恨并入主人的仇恨表——但 `HandleIdleOrPatrol` 此前只按 `perception_radius`
   内的感知结果判定 `idle_to_chase`，完全不读威胁表；召唤物自身感知范围内没有敌对单位（主人在
   远处交战）时，即使 `IsInCombat` 为真，AI 仍判定"无事可做"留在 `idle`，加上（修订前）
   `TryFollow` 又因 `IsInCombat` 标志跳过跟随，召唤物表现为原地冻结、既不参战也不跟随，实测持续
   108 秒。根治口径：威胁表是"当前是否在战、该打谁"的权威来源，感知范围只负责发现*新*目标——
   `idle`/`patrol` 默认转移候选集新增第二来源：感知范围内无候选时，若 `ICombatHost.IsInCombat`
   为真，退回取 `IThreatTable.GetTopThreat`，验证仍存活、仍敌对（`IFactionMatrix.IsHostile`，
   阵营关系可能在两次判定之间变化，不缓存）、且未超出 `leash_range`（拴绳原点是
   `AiUnitState.SpawnPoint`，与 `chase_to_return`/`combat_to_return` 用的是同一个点）才采用，否则
   视为无候选、继续留在 `idle`/`patrol`（不会为一条已经无效的威胁表条目强行转移）。不新增转移名：
   `idle_to_chase` 的 Expr 覆盖语义不变，覆盖表达式返回真假的含义不变，覆盖时完全不读威胁表（覆盖
   即接管全部判定，与既有"覆盖只影响判定的布尔结果"原则一致，见判断记录 3）——本次改动只扩展了
   "没有覆盖时"默认候选的来源。`AiHost` 新增可选 `ICombatHost` 构造参数（ABI 安全新增重载，末尾
   追加、`RulesAssembly` 传入已装配好的 `Combat`）；未注入时（既有调用方、多数单元测试）
   `HandleIdleOrPatrol` 只用感知来源，行为与本次改动之前完全一致。跟随侧的联动修订（`TryFollow`
   不能再单看 `IsInCombat` 标志，否则本条修复对召唤物无效）见
   `core/carriers/summon/README.md` 判断记录 9；决定 2 未省略——`SummonTickHandler` 能低成本拿到
   `AiHost`（已在同一装配根 `CarriersAssembly` 内可用），未引入新的跨层耦合。

10. **ADR-0088（消费方第三十三批反馈2护栏，决定7）：`HandleCombat` 选目标跳过非敌对的顶端威胁
    来源**：目标运行期被改判为友方后，仇恨表的源头修复（见
    `core/rules/combat/README.md`/`core/carriers/unit/README.md` 对应判断记录）会同步清理相关
    条目，但这条护栏独立存在、不依赖源头修复——`HandleCombat` 不再无条件信任
    `IThreatTable.GetTopThreat`（该方法只看威胁值高低，不检查敌对性）：新增 `GetTopHostileThreat`
    复算一遍威胁表全部条目，跳过已死亡/已不存在/`IFactionMatrix.IsHostile` 为假的来源，在剩余
    仍敌对的条目里按原有"威胁值最高、同值取 `Id` 序数最小"规则（与 `ThreatTable.GetTopThreat`
    的平局打破算法一致）选一个；全部条目都非敌对时返回空，`state.Target` 保持未赋值，交给既有
    `combat_to_return` 判定接管（威胁表"为空"与"只剩非敌对来源"在这条护栏下等价）。不复用
    `IThreatTable.GetTopThreat` 加事后判空重取的写法，是因为威胁值次高但仍敌对的来源可能排在
    非敌对的最高来源之后，必须完整遍历一遍全部条目而不是只看第一名。
