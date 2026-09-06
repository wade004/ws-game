# L0 基础层 · sim_loop 固定步长主循环

职责：按固定步长推进世界模拟（连续模式累积器 `SimClockHost`）、编排 `WorldSim` 每个 tick
固定的八步顺序、维护实体公共基类 `Entity` 与实体集合、提供挂在模拟时间轴上的通用计时器
`ISimTimers`、按先攻规则驱动离散（回合制）模拟步的 `TurnScheduler`、离散步节奏门
`PacingPolicy`（见 [03_运行时骨架.md](../../../architecture/03_运行时骨架.md) 第 3、4、8、9 节、
[ADR-0013](../../../architecture/adr/0013-时间模型可替换即时与回合制同一规则层.md)）。

**离散时间模型已落地**（ADR-0013 解除了此前"本项目暂不启用"的限制）：`TurnScheduler`
实现 `initiative_stat`/`action_points`/`fixed_order` 三种先攻策略（`atb` 仍是预留扩展位，
`Configure` 遇到时抛 `NotSupportedException`），额外提供 `AddParticipant`/`RemoveParticipant`
（中途加入/离场，见判断记录 2）与 `TryConsumeActionPoints`（供移动等系统按行动点计消耗共享
预算，见判断记录 5）；`ImmediatePacingPolicy`/`WaitForPlaybackPacingPolicy` 实现两种节奏策略；
`found.time_model` 表的加载/解释、模式切换（连续 ⇄ 离散）由
`core/gameplay/assembly.TimeModelSwitch`/`GameplayAssembly.Advance` 驱动（本模块自身仍不加载
数据表，只提供机制，见下方"基础架构提供/游戏层提供"）。`schema/TimeFieldConsistencyRule.cs`
提供 04 第 3.1/5 节"时间字段与时间模型一致"校验的通用机制（本模块不认识任何具体上层表/字段
名，由 `core/rules/assembly`/`core/gameplay/assembly` 各自登记声明，见该类型判断记录）。

依赖：只依赖 `core/foundation/common`（`Id`、`Vec2`）、`core/foundation/event_bus`
（`IEvent`、`IEventBus`）、`core/foundation/data_registry`（`TimeModelSchema`/
`TimeModelValidationRule` 的 `TableSchema`/`IValidationRule` 声明）与 .NET 标准库；不引用任何
引擎适配层实现、不使用系统时间（`DateTime`/`Stopwatch`/`Environment.TickCount`）、不使用多
线程、不使用反射、不使用系统级 `Random`（见落地方案与分阶段计划.md 第 4.1 节确定性要求）。

不负责什么：

- 不读取任何系统/引擎时间源：`SimClockHost.Advance` 的唯一输入是调用方传入的
  `realDeltaSeconds`（由引擎适配层 `IClock.onFrame` 回调提供），本模块不知道也不关心
  真实时钟怎么来的。
- 不实现 `Unit`/`GameObject`/`Projectile`/`AreaTrigger`/`DroppedLoot` 等具体实体子类
  （见 [05_对象模型与世界.md](../../../architecture/05_对象模型与世界.md) 第 1 节继承树），
  这些属于更上层模块（`core/carriers` 等）；本模块只提供公共基类 `Entity` 与集合管理。
- 不实现技能管线、AI 决策、战斗结算、触发评估的具体业务逻辑：`WorldSim` 只编排
  `TickPhase` 的固定顺序与处理器注册机制，各阶段"做什么"由外部模块经
  `RegisterPhaseHandler` 注入。
- 不判断"什么时候该从连续切到离散"：那是 `combat.entered`/`combat.left` 事件 + 数据集声明
  （`found.time_model`）共同决定的编排逻辑，属于 `core/gameplay/assembly.TimeModelSwitch`
  （L4）的职责，本模块（L0）只提供 `TurnScheduler`/`PacingPolicy`/`ISimClockHost.Mode` 三个
  被驱动的机制点。
- 不解释离散步下具体游戏规则如何结算（读条换算、移动预算数值等）：那些是数据集声明
  + 各自模块（skill/carriers.unit 等）的解释，见 04 第 3.1 节"时间字段语义"。

## 目录

```
sim_loop/
  README.md
  contracts/
    SimStep.cs           SimStepKind、StepPhase、SimStep
    ISimClockHost.cs      连续/离散模式主循环契约（含 Mode 属性）
    SimLoopOptions.cs     步长/最大补偿步数/默认时间缩放配置
    IWorldSim.cs           TickPhase、ITickPhaseHandler、IWorldSim
    Entity.cs              EntityLifecycle、Entity 公共基类
    EntityFilter.cs        EntityPredicate、EntityFilter
    ISimTimers.cs           TimerHandle、ISimTimers
    ITurnScheduler.cs       InitiativePolicy、ITurnScheduler
    IPacingPolicy.cs        PacingMode、IPacingPolicy
    TimeModelMode.cs        Continuous/Discrete
    TimeModelDefinition.cs  found.time_model 记录的强类型视图 + FromRecord
    Events.cs               SimEventKeys、SimTickStartedEvent、SimTickFinishedEvent、
                             EntityCreatedEvent、EntityDestroyedEvent、
                             SimTurnStartedEvent、SimTurnEndedEvent、SimRoundEndedEvent、
                             SimAwaitingInputEvent
  core/
    SimClockHost.cs              ISimClockHost 默认实现（含离散模式分支）
    WorldSim.cs                   IWorldSim 默认实现（离散步只收集当前行动者意图）
    SimTimers.cs                  ISimTimers 默认实现 + RescaleAll（模式切换时间单位换算）
    TurnScheduler.cs               ITurnScheduler 默认实现，同时实现 IPersistable
    ImmediatePacingPolicy.cs       IPacingPolicy："不等待回放"
    WaitForPlaybackPacingPolicy.cs IPacingPolicy："等待回放"
  schema/
    TimeModelSchema.cs      found.time_model 的 TableSchema + TimeModelValidationRule
    README.md               found.time_model 字段说明（备查，权威定义见 04 第 3.1 节）
  tests/
    SimClockHostTests.cs      含离散模式 Mode/Advance 分支
    WorldSimTickOrderTests.cs
    WorldSimEntityTests.cs
    SimTimersTests.cs
    NotEnabledTests.cs         历史文件名沿用，现覆盖 WorldSim 处理离散步的基本契约
    TurnSchedulerTests.cs
    PacingPolicyTests.cs
    TestEntity.cs            测试用 Entity 子类
    DelegatePhaseHandler.cs  测试用 ITickPhaseHandler 适配器
    SimLoopTestSupport.cs    测试共用的 IEventBus 构造帮助方法
```

## 设计要点与判断记录

以下几项是文档未逐字规定、执行期需要做出的具体选择，逐条记录判断依据供设计层复核：

1. **`SimStep.ActorId`/`Phase` 用可空类型**：03 第 9 节伪代码 `Discrete{actorId: Id,
   phase: ...}` 没有显式标注 Optional，但这两个字段只在 `Kind == Discrete` 时有意义。
   用 `Id?`/`StepPhase?` 显式表达"只在离散步下有值"，比用不可空类型 + 约定"连续步下
   忽略"更不容易被调用方误用，且与 `Entity` 里 `Optional<Id>` 字段用 `Id?` 表达的风格
   一致。

2. **`sim.tick_started` 用 `PublishImmediate` 立即派发，先于本 tick 全部阶段处理器
   执行**：03 第 4.2 节步骤 7"事件派发"指的是"把本 tick 累积的全部事件"（即步骤 1~6
   处理器产生的事件）批量派发；`sim.tick_started` 是"tick 开始"的边界标记事件，语义上
   发生在步骤 1 之前，不属于"本 tick 累积的事件"这一批。若也走 `Enqueue` + 批量派发，
   订阅者会在全部阶段处理器都执行完之后才收到"tick 开始"通知，这与直觉相悖，也不满足
   任务验收要求的"`sim.tick_started` 在处理器之前送达"。因此改用 `PublishImmediate`
   同步立即派发；`sim.tick_finished`、`entity.created`、`entity.destroyed` 仍然全部走
   `Enqueue` + 批量 `DispatchPending`，符合"同步派发 + tick 末批处理"（落地方案第 4.2
   节）。

3. **累积器补偿上限溢出丢弃策略：清零而非取模**：03 第 3.1 节步骤 4 只规定"超出部分的
   时间直接丢弃而不追赶"，未规定丢弃到什么精确程度；任务书给出"`acc` 置为 `acc % step`
   或直接清零，二选一"。选择清零：取模会把这部分本该被丢弃的"债务"保留为一段不足一步
   的余量，在长时间卡顿反复发生的场景下，这段余量会在下一次 `Advance` 里悄悄并入新的
   真实时间，让追赶行为在多次调用之间产生难以预期的耦合；触发补偿上限本身就意味着这段
   时间的推进已经是有损的（"死亡螺旋"保护，见 ADR-0003），清零给出一个干净、可预测的
   起点，代价只是多丢弃不到一个步长的模拟时间，不影响确定性（丢弃发生在 `SimClockHost`
   这一层，不改变 tick 内部的计算逻辑与随机数消耗）。

4. **计时器到期判定用 `1e-9` 浮点容差，而不是严格 `<= 0`**：`SimTimers` 按"逐 tick
   减法"累积剩余时长（而不是用"已推进 tick 数 × 步长"重新相乘），这样才能正确支持不同
   tick 之间 dt 不必相同的一般情形；代价是对于恰好整除的时长（如 duration=0.1 秒、
   步长=1/60 秒，理论上 6 步恰好归零），连续 6 次二进制浮点减法会残留一个量级 1e-17 的
   正数浮点噪声，而不是精确的 0。`IsExpired` 判断 `Remaining <= 1e-9`：这个容差远小于
   任何有意义的时间单位（不会把"确实还没到期"的计时器误判为到期），又足够吸收这类浮点
   噪声，使 `Create(0.1)` 在步长 1/60 下精确在第 6 个 tick 后判定到期（与任务验收标准
   逐字一致）。

5. **`sim.tick_started` 事件字段：在登记表建议值之外补充 `dt`**：
   `data/_sample/found/found.event_catalog.json` 里 `sim.tick_started` 一行的 `fields`
   只登记了 `tickIndex`，但该行 `description` 明确标注"字段为建议值"，03 原文本身也没有
   逐字段规定这个事件的载荷。`SimTickStartedEvent` 额外携带 `Dt`（本 tick 经过的秒数）：
   下游订阅者（尤其是表现层插值、性能采样）普遍需要知道本 tick 的步长，省得再反查
   `ISimClockHost.StepSeconds`。按任务书"若登记表字段与 03 原文不一致以 03 为准"处理为
   "03 未限定字段 ⇒ 允许在登记表建议值之外按需要补充"，在此记录，供设计层复核是否需要
   同步更新 `found.event_catalog.json` 的 `sim.tick_started` 行。`entity.created`、
   `entity.destroyed`、`sim.tick_finished` 三个事件的字段与登记表完全一致，无此问题。

6. **诊断出口：本模块自带简单列表，不复用 `event_bus` 的 `IEventDiagnostics`**：
   `IEventDiagnostics` 语义上是"事件总线派发过程"的诊断出口（未登记事件 key、类型化
   订阅类型不匹配、超过派发轮次上限等）；"离散步下计时器不推进"是 sim_loop 模块内部
   与事件派发无关的关注点，复用会让两类不同来源的诊断信息混进同一个通道。因此
   `WorldSim` 自带一个只读的 `DiagnosticsWarnings`（`IReadOnlyList<string>`，非
   `IWorldSim` 契约的一部分，只是具体类型上的便利成员），每次 `Tick` 收到 `Discrete`
   步时记一条警告。

7. **`ITurnScheduler.SubmitIntent` 已改用具体 `Intent` 类型**：03 第 9 节伪代码写的是
   `submitIntent(actorId: Id, intent: Intent)`——此前 `Intent` 数据结构尚未定义时曾用
   `object` 占位，现已在 `contracts/Intent.cs` 定义具体类型，`ITurnScheduler.SubmitIntent`
   签名为 `SubmitIntent(Id actorId, Intent intent)`，不再是占位签名（历史判断记录，保留
   备查）。

## `Entity` 与实体集合

- `Entity` 是抽象公共基类，字段见
  [05_对象模型与世界.md](../../../architecture/05_对象模型与世界.md) 第 1.1 节：
  `EntityId`（只读）、`TemplateId`、`Position`、`LayerDepth`、`Facing`、`MapId`、
  `SpawnedBy`、`WorldFlagsScope`、抽象只读 `Kind`。`Lifecycle`
  （`Created→Active→(Inactive 可选)→Destroyed`）由 `WorldSim` 管理（`internal set`），
  业务代码只读。
- `Unit`/`GameObject`/`Projectile`/`AreaTrigger`/`DroppedLoot` 等具体子类不属于本模块
  （见 05 第 1 节继承树），测试用一个内部的 `TestEntity : Entity` 子类。
- `EntityKinds`（本模块 `contracts/EntityKinds.cs`）登记已落地子类实际使用的 `Kind`
  取值常量：`Player`/`Creature`/`Gobj`/`Loot`/`Projectile`/`AreaTrigger`（分别对应
  `PlayerUnit`/`CreatureUnit`/`GameObjectEntity`/`DroppedLootEntity`/`ProjectileEntity`/
  `AreaTriggerEntity`）。`core/`/`presentation/` 内引用这些取值一律用该常量，不再手写
  字符串字面量；仅 `summon` 一个模块仍未落地对应 `Entity` 子类（按设计复用 creature
  模板，见 01 文档），暂不登记（见该类型判断记录"未使用的不发明"）。
- `WorldSim` 内部用 `SortedDictionary<Id, Entity>` 保存实体集合，天然按 `Id` 序数升序
  遍历，`QueryEntities` 据此保证结果确定性排序，不需要额外排序步骤。
- `AllocateEntityId(kind)` 按 `kind` 分别维护一个从 1 起的递增序号，产生
  `"<kind>.inst_<序号>"` 形式、满足 `Id` 格式校验的运行期 id，纯确定性、不依赖任何
  随机源或系统状态。

## `WorldSim.Tick` 的八步顺序与事件时序

见 03 第 4.2 节：`输入意图收集 → AI 决策 → 技能管线 → 移动与导航 → 战斗结算 → 触发评估
→ 事件派发 → 生命周期清理`，对应本模块的 `TickPhase` 枚举八个值。前六个可由外部经
`RegisterPhaseHandler` 注册处理器（同一阶段多个处理器按注册顺序执行）；后两个
（`EventDispatch`、`LifecycleCleanup`）由 `WorldSim` 自己执行，外部注册会抛
`ArgumentException`。

一次 `Tick` 内的事件送达顺序：`sim.tick_started`（立即，见判断记录 2）→ 六个阶段处理器
依次执行（期间产生的事件只入队，不派发）→ 阶段 7 批量派发步骤 1~6 产生的事件 → 阶段 8
对每个待销毁实体发出 `entity.destroyed` 并真正移除、`Lifecycle = Destroyed` → 入队
`sim.tick_finished` → 再批量派发一次，让 `entity.destroyed` 与 `sim.tick_finished`
在本 tick 内送达。

## 通用计时器 `ISimTimers`

挂在模拟时间轴上，供冷却、光环持续时间等一切"经过若干模拟时间后触发"的需求统一使用
（见 03 第 8 节）。`WorldSim` 在每个 `Continuous` tick 开头按 `step.Dt` 统一推进全部
存活计时器；`Discrete` 步下**不推进**（一个离散步只代表某一个行动者的一次行动，不是全体
单位共享的统一时间增量，见 `WorldSim.cs` 判断记录），并记一条诊断警告
（见 `WorldSim.DiagnosticsWarnings`）。到期的计时器保留"已到期"状态直到显式 `Cancel`，
计时器本身不发事件，由调用方轮询 `IsExpired`。模式切换时刻的时间单位换算见
`SimTimers.RescaleAll` 与 `TimeModelSwitch`。

## 离散时间模型（ADR-0013）判断记录

1. **`TurnScheduler` 一步一回合 vs 多次行动**：`initiative_stat`/`fixed_order` 两种策略下，
   每次 `NextStep()` 产生的离散步即该行动者本回合唯一一次行动，`NotifyStepConsumed` 之后
   立即推进到下一行动者；`action_points` 策略下 `NotifyStepConsumed` 按 1 点/步扣减行动点，
   未耗尽则继续同一行动者，直到耗尽或调用方显式 `EndTurn`。04 未规定
   `initiative_stat`/`fixed_order` 是否允许单回合多次行动，本实现按"一步一回合"处理——这是
   两种最简策略的通行做法（多行动预算是 `action_points` 策略专属的语义）。
2. **参与者名单：`BeginCombat` 一次性传入 + 中途加入/离场实时同步**：`ITurnScheduler` 契约
   （03 第 9 节）只有 `beginCombat(participants)` 这一个一次性传入整份名单的原语，`TimeModelSwitch`
   仍然只在"进战单位数从 0 变 1"这一刻解析一次完整参与者名单（空间查询半径 + 阵营过滤，见该
   类型判断记录"参与者解析"）；但战斗中途再有单位加入（`combat.entered`）或死亡
   （`unit.died`），本模块具体类型 `TurnScheduler`（不在 `ITurnScheduler` 契约上，属于该具体
   类型的便利成员，惯例同 `NotifyStepConsumed`）新增 `AddParticipant`/`RemoveParticipant` 两个
   方法，由 `TimeModelSwitch` 在离散模式中收到这两个事件时调用，实时同步进当前轮的行动顺序
   （`initiative_stat`/`action_points` 按先攻值插入尚未行动的序列，`fixed_order` 追加末尾；
   死亡/离场移除，处理"移除的正是当前行动者本人"等边界情形，见该方法判断记录）。`combat.left`
   （个体脱战，非死亡）不触发移除，是已知限制，见交付报告"做不了的事"。
3. **全局计时器换算 `SimTimers.RescaleAll` 不是 `ISimTimers` 契约的一部分**：03/09 未给"时间
   单位换算"定义独立接口原语，本方法是承载该文档要求行为（第 3.3 节步骤 2）的具体类型便利
   成员，惯例同 `WorldSim.DiagnosticsWarnings`。
4. **`TurnScheduler` 的存档段 key `sim.turn_state` 已登记进
   [10_存档与持久化.md](../../../architecture/10_存档与持久化.md) 第 3 节固定段序（步骤 7b，
   2026-09-05 勘误：只在离散模式有内容，连续模式为空段）**——W2 收边补齐（A4 审计 F2）后，
   `sim.turn_state` 已与 `world.dropped_loot` 等四个"世界附属段"（步骤 7a）一并登记进
   `SaveSections.KnownOrder` 这份全序数组（固定排在 7a 之后、`rng.stream_states` 之前），不再
   是按 key 序数排序的"自定义段"——此前按序数排序会让 `sim.turn_state` 实际排在全部 7a 段之前，
   与文档"7a 后 7b"的文字顺序不完全一致，现已消除该偏差（`GameplayAssembly.RegisterPersistables`
   接线本身不变，只是排序依据变了）。
5. **`found.time_model` 的 `movement_budget_rule: action_points`（以行动点计的移动预算）已
   落地**：`core/carriers/unit.MovementTickHandler` 离散分支在按 `distance` 规则（速度 ×
   `DiscreteTurnEquivalentSeconds`）算出本步会移动的距离之后，`action_points` 规则额外叠加一层
   "够不够行动点"的门槛——距离 × `found.time_model.movement_action_cost_per_unit` 算出所需
   行动点，向 `TurnScheduler.TryConsumeActionPoints`（与 `initiative_policy: action_points`
   共享同一份账本，见该方法判断记录）尝试扣减；不足时拒绝本次移动意图（不产生任何位移）并调用
   `TurnScheduler.EndTurn` 结束该行动者的回合。`action_points_per_turn`/
   `movement_action_cost_per_unit` 两个新字段已登记进 04 第 3.1 节（2026-09-05 勘误）。
6. **`WaitForPlaybackPacingPolicy` 零反馈离散步永久卡在 `playing_back` 已根治（W5c，第三轮
   审计"仍保留项"收口）**：`PlaybackQueue.Finished` 只在队列"由非空变空"的边沿触发，一个从未
   变过非空的队列（该离散步没有产生任何反馈动作）不会触发该边沿——此前 `WaitForPlaybackPacingPolicy`
   每个离散步都无条件进入等待，没有表现层主动通知就永久卡死。本类型现新增可选属性
   `HasPendingPlayback`（`Func<bool>?`，见该类型判断记录）："当前是否存在尚未回放完的表现动作"
   这项知识不属于本模块（本模块不知道播放队列的存在），由调用方注入；`BeginStep` 按探针结果
   判定——未接线或探针返回 `false` 时立即视为回放完成，不进入 `playing_back`；探针返回 `true`
   时才真正关闭节奏门。生产接线（探针如何接上真正的播放队列）落在 L4/L5：
   `core/gameplay/assembly.GameplayAssembly.SetPendingPlaybackProbe` 提供窄回填入口（本模块构造
   期拿不到表现层的播放队列，只能"先占位、后回填"），`presentation/assembly.PresentationAssembly`
   在装配好 `FeedbackBinder` 之后调用它接上 `() => Feedback.Queue.PendingCount > 0`；Unity 引导
   （`adapters/unity` 的 `GameFoundationBootstrap`/`FrameworkResidentHost`）不需要任何轮询兜底。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| 固定步长累积器、插值 alpha、暂停、慢动作、追赶上限的实现机制 | 是 | 具体步长、最大补偿步数、时间缩放数值配置 |
| `WorldSim` 八步 tick 固定顺序与阶段处理器注册机制 | 是 | 各阶段具体处理的业务逻辑（技能管线、AI 决策等实现） |
| `Entity` 公共基类与实体集合管理、确定性查询/id 分配 | 是 | 具体实体子类（`Unit`/`GameObject` 等，属于更上层模块） |
| 通用计时器原语 | 是 | 使用计时器的具体游戏内容（冷却时长等数值） |
| 离散时间模型：`TurnScheduler`（三种先攻策略）、`PacingPolicy`（两种节奏）、`ISimClockHost.Mode` | 是 | 探索/战斗各自选用哪种时间模型（`found.time_model`）、先攻/节奏策略的具体参数、何时触发模式切换（`core/gameplay/assembly.TimeModelSwitch`，L4） |
