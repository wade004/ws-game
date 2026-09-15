# L4 玩法层 · death（死亡复活）

职责：落地 06_规则层_属性技能战斗AI.md 第 4.6 节死亡与复活三策略（`respawn_point`/`reload_save`/
`permadeath`）的**执行主体**。`core/rules/combat.CombatOptions.DeathPolicy` 只是一个孤立的配置项
（06 §4.6 只声明策略取值，不声明谁来执行），本模块订阅 `unit.died`（仅玩家单位，AI/生物死亡不
触发），按 `DeathPolicyHost.EffectivePolicy`（`DeathPolicyOptions.Policy` 覆盖，或回退
`CombatOptions.DeathPolicy`）执行对应策略——这是 DECISIONS（2026 深夜设计层拍板）拍板 3 的落地：
"死亡复活三策略执行主体应落在 L4 新模块 `core/gameplay/death`"。

依赖：L0（`event_bus`/`app_lifecycle`/`save_system`/`sim_loop`）、L2（`core/rules/common.RespawnPolicy`/
`RulesEventKeys`/`UnitDiedEvent`/`UnitRespawnedEvent`）。不直接依赖 `core/carriers/unit`
（L3 具体类型 `WorldUnitAccess`）——经 `DeathPolicyOptions.ReviveUnit` 窄契约委托接线（见判断记录 1），
也不直接依赖同层的 `core/gameplay/assembly`（经 `DeathPolicyOptions.ResolveDefaultSpawn` 窄契约委托
接线，同一份判断记录）。T-N4-9（[ADR-0034](../../../architecture/adr/0034-单一货币与价格挂物品等级.md)
决策 6）起同样不直接依赖 `core/gameplay/economy.IEconomyHost`（硬性规则）——经
`DeathPolicyOptions.RespawnFeeBalance`/`RespawnFeeCharge` 两个窄契约委托接线，见判断记录 8。

## 目录

```
death/
  README.md
  contracts/
    IDeathPolicyHost.cs       契约接口（只暴露只读 EffectivePolicy，无触发方法——事件驱动）
    DeathPolicyOptions.cs     策略覆盖/复活生命值比例/延迟 tick 数/存档槽 id/ReviveUnit/
                              ResolveDefaultSpawn 两个 L4↔L3、L4↔L4 边界委托；T-N4-9：
                              RespawnFee/RespawnFeePercentage/RespawnFeeCurrencyId/
                              RespawnFeeFixedAmount/RespawnFeeBalance/RespawnFeeCharge——
                              复活费策略项与三个 L4↔L4 边界委托
    IDeathPolicyDiagnostics.cs 最小诊断出口
  core/
    DeathPolicyHost.cs        IDeathPolicyHost + ITickPhaseHandler 唯一实现（T-N4-9：
                              Execute 里 ReviveUnit 调用之后新增 ChargeRespawnFee 私有方法）
    InMemoryDeathPolicyDiagnostics.cs
  tests/
    TestSupport.cs
    DeathPolicyHostTests.cs   三策略各 ≥2 条 + AI 死亡不触发 + EffectivePolicy 覆盖
    T_N4_9_RespawnFeeTests.cs 复活费：余额为零仍照常复活且扣零、fixed_by_level 曲线值超余额扣至零、
                              pct_of_balance 按百分比、None 不收费、扣费委托未接线不阻断复活
```

## 判断记录

1. **`ReviveUnit`/`ResolveDefaultSpawn` 两个窄契约委托，而不是直接引用 `WorldUnitAccess`/
   `TeleportTargetResolver`**：`WorldUnitAccess.Revive`（L3 `core/carriers/unit`）是本模块
   `respawn_point` 策略需要的"复活单位"能力，但该方法不在 `IUnitAccess` 契约上（只是具体类型的
   窄契约，见其判断记录），本模块不为此新增一条对 `core/carriers/unit` 具体类型的编译期依赖；
   `TeleportTargetResolver`（L4 `core/gameplay/assembly`）能把一个地图 id 解析成该地图默认出生点，
   但 `assembly` 是组装根，同层 L4 模块不应反向依赖组装根（会造成"部件依赖装配它的容器"这种
   倒置）。两者都改为 `GameplayAssembly` 装配期用 `is` 模式/直接持有的方式接线的委托——
   `ReviveUnit` 只在 `Carriers.Units` 运行期确实是 `WorldUnitAccess` 时才接线（防御性判断，惯例
   同 `GameplayAssembly` 第 10.5 步 `world is WorldSim` 的一贯做法），`ResolveDefaultSpawn` 直接
   接同一个 `teleportTargetResolver.Resolve` 方法组（传入地图 id 本身即命中其"两段式地图 id"解析
   路径，见该类型判断记录）。任一委托未接线（如测试直接构造 `DeathPolicyHost` 不提供）时
   `respawn_point` 策略退化为"只记诊断、不复活"，不抛异常（同本仓库一贯的未接线退化惯例）。

2. **`RespawnDelayTicks` 默认 1，且不接受非正数**：死亡结算（`unit.died` 发布）发生在
   `WorldSim.Tick` 的 `EventDispatch` 阶段（第 7 步），晚于本模块 tick 处理器挂载的
   `TriggerEvaluation` 阶段（第 6 步）——因此"死亡当次 tick 立即复活"在时序上不可行，最早只能是
   下一次 tick 的 `TriggerEvaluation`。`DeathPolicyHost` 构造函数对 `RespawnDelayTicks < 1` 直接
   抛 `ArgumentException`（构造期快速失败，不是运行期悄悄降级为 1）。

3. **`respawn_point` 的延迟队列不区分 `Continuous`/`Discrete` 步**：两种时间模型下死亡都可能
   发生，复活延迟按"tick 数"（`ITickPhaseHandler.Execute` 被调用的次数）计，不按秒数（`Discrete`
   步没有秒数概念，`SimStep.Dt` 恒为 0）——同一份倒计时逻辑天然对两种模式都成立，不需要为离散
   模式单独分支。

4. **`permadeath` 删除的"当前槽"默认等于 `AutosaveSlotId`**：06 第 4.6 节"死亡即结束当前存档
   周期"没有定义"当前存档周期"对应哪个槽 id 这一框架层概念（多存档槽 vs 单存档槽是游戏层决策）。
   `DeathPolicyOptions.CurrentSlotIdProvider` 为 `null`（默认）时退化为删除
   `AutosaveSlotId`——单存档槽的游戏这一默认值已经足够；多存档槽的游戏（有独立于自动存档槽的
   "当前正在进行的存档周期"概念）应显式提供该委托。

5. **`reload_save` 不做任何"复活单位"的专门处理，但经注入的 `ReloadSaveDelegate` 读档**（外部
   审核阻塞项 2 收口，2026-09-07）：不直接调用 `ISaveSystem.Load(AutosaveSlotId)`，改经
   `DeathPolicyOptions.ReloadSave`（未装配时退化为直接调用 `ISaveSystem.Load`，行为与收口前完全
   一致）——`core/gameplay/assembly.GameplayAssembly` 默认把 `RestoreFromSlot` 接给这个委托，读档
   本身按 10 号文档固定顺序恢复全部已注册段（含玩家位置、存活状态与生命值当前值——见
   `core/gameplay/assembly.PlayerVitalsPersistable`"player.vitals" 段——、目标地图与当前地图不同
   时经 `ISceneRouter` 切场景），本模块仍然不需要额外调用 `ReviveUnit` 或做任何单位状态改写，
   "回退到最近一次存档"这句话本身就是"整体读档"的准确技术含义；只是"整体读档"现在真的包含了
   存活状态/生命值/场景切换这几步，此前这几步在框架层面是缺失的（`Unit.Alive`/资源池当前值此前
   完全没有 `IPersistable` 落点）。读档失败（如自动存档槽从未写入过，`LoadStatus.NotFound`）不再
   只记一条诊断错误就此了事——回退到 `respawn_point` 策略同一套默认复活点逻辑（`EnqueueRespawn`），
   避免玩家永久停留在"已死亡"状态；`DeathPolicyHost` 本身仍不直接持有 `ISceneRouter`（构造函数
   依赖清单未变），场景切换这一步完全在注入的委托实现里完成，同 `ReviveUnit`/`ResolveDefaultSpawn`
   两个既有 L4↔L3/L0 边界委托一贯的接线手法。

5b. **C12 收口（外部审核 7e63d66），限定判断记录 5"不需要额外调用 ReviveUnit 或做任何单位状态
   改写"的适用范围**——上一条只覆盖"逻辑层状态"（读档本身已经把存活/生命值/位置恢复到位，确实
   不需要再手工改写），但遗漏了"表现层需要一个明确信号才能清理死亡终态锁"这一点：读档成功
   （跨地图会触发场景切换，`ClearAll` 销毁重建过程本身会清理旧 View 的动画状态机记账，不受
   影响；但同图读档不触发场景切换，不会销毁重建任何 View）时，若不发一个复活类事件，表现层的
   动画状态机会一直停在死亡姿态，即便逻辑层早已恢复存活、可以正常行动。现在 `reload_save`
   读档成功分支额外发布一次 `UnitRespawnedEvent(unitId, RespawnPolicy.ReloadSave)`（同
   `respawn_point` 策略延迟复活队列既有的发布惯例，见判断记录 3 附近 `Execute` 实现）——用
   `IEventBus.Enqueue` 而不是 `PublishImmediate`：本方法正处于 `unit.died` 自己的订阅回调内，
   仍在那一次 `unit.died` 派发的调用栈中，若同步发出会被订阅顺序晚于本处理器的下游（如表现层
   的死亡处理）随后原地覆盖回死亡状态；`Enqueue` 保证复活信号严格晚于当前这一批 `unit.died`
   全部订阅方处理完毕之后才真正派发。见 `DeathPolicyHost.cs`（`OnUnitDied` 的 `ReloadSave`
   分支）、`DeathPolicyHostTests.cs`
   （`ReloadSave_PlayerDies_LoadSucceeds_PublishesUnitRespawnedEvent_DeferredNotImmediate`、
   `ReloadSave_PlayerDies_RespawnedEventArrivesAfterAllUnitDiedSubscribersProcessed_NotOverwrittenByLateDeathHandler`）、
   Unity 侧 PlayMode 用例 `AuditBlockersPlayModeTests.PlayerDies_ReloadSave_PlayerRevivedAndViewExists`
   （已扩展断言同图读档后动画终态锁被清理、Move/Attack 恢复正常）。

6. **`EffectivePolicy` 在构造期一次性解析，运行期不重新读取 `CombatOptions.DeathPolicy`**：
   `CombatOptions.DeathPolicy` 是构造期口味配置（同 `SkillOptions`/`CombatOptions` 其余字段），
   本仓库没有任何"运行时热切换战斗口味配置"的先例，`DeathPolicyHost` 与其它 L4 宿主一样，在
   构造函数里把最终生效值算好存成只读属性，不在每次死亡事件到来时重新计算。

7. **C11-CLEANUP/C11-PENDING-LOAD 根治新增（2026-09-11，消费方反馈第 C11 项，基线 1.22.0）：
   延迟复活队列的四个失效时机与执行前的存在性校验**——`respawn_point` 延迟复活队列（`_pending`）
   依赖"目标单位到期时仍然存在"这一前提，本次收口前完全没有任何失效机制，撞上两类真实探针复现
   的问题：(a) 见上方"不负责什么"一节已改判的判断记录——读档前遗留的 pending 覆盖读档恢复好的
   状态；(b) `WorldSim.ClearAll` 后下一 tick，`Execute`（挂在 `TickPhase.TriggerEvaluation`，见
   判断记录 3）先于对应的 `entity.destroyed`（`ClearAll` 只是把它排入待处理队列，不立即派发，
   要等同一次 `Tick` 更晚的 `EventDispatch` 阶段——见 `WorldSim.ClearAll`/`Tick` 判断记录）被执行，
   此时倒计时恰好到期会直接调用 `ReviveUnit`（生产装配即 `WorldUnitAccess.Revive`）尝试复活一个
   `IWorldSim.GetEntity` 已经查不到的单位，抛 `InvalidOperationException`。修复分两层，互补而非
   互斥：
   - `ClearPending()`（新增公开方法，清空 `_pending`，不发布任何事件）——由
     `core/gameplay/assembly.GameplayAssembly` 经其 `IDerivedStateRebuilder.BeforeLoad`
     实现（早于任何存档段真正 `Load`，见该接口类型判断记录）在读档开始前调用一次；同时新增
     `IDisposable.Dispose()` 同样清空 `_pending`（并取消两个事件订阅，幂等）——供调用方在释放
     本宿主时主动失效，不依赖后续任何 tick。
   - 新增对 `SimEventKeys.EntityDestroyed` 的订阅（`OnEntityDestroyedForPending`）：某单位被销毁、
     对应的 `entity.destroyed` **真正派发**之后，立即摘除 `_pending` 里匹配该单位 id 的记录——
     覆盖 `ClearAll`/`MarkForDestruction` 两种销毁路径里"事件已经派发"之后的全部后续 tick。
   - `Execute` 本身在真正调用 `ReviveUnit` 之前，新增一道防御性存在性校验（`IWorldSim.GetEntity`
     是否返回 `Unit`）——覆盖上述订阅覆盖不到的那个时序窗口（`ClearAll` 到对应
     `entity.destroyed` 真正派发之间，早于派发的那次 `Execute`）：目标单位不存在时丢弃这条
     `_pending` 记录、只记一条诊断（`IDeathPolicyDiagnostics.Warn`），不抛异常——同
     `core/rules/combat.CombatHost.NotifyCombatEvent` 判断记录"对不存在单位静默跳过"同一惯例。
   三者合起来才是完整修复：`ClearPending`/`Dispose` 处理"读档"与"宿主释放"两个非"单位销毁"触发
   的失效场景，事件订阅处理"单位销毁后、事件已派发"的后续 tick，`Execute` 自身的存在性校验兜底
   "事件尚未派发"的时序窗口，任一层单独存在都不足以覆盖全部真实探针复现的路径。

8. **T-N4-9（[ADR-0034](../../../architecture/adr/0034-单一货币与价格挂物品等级.md) 决策 6；
   06 第 4.6 节 2026-09-14 修订段"复活费"）：`respawn_point` 分支复活费——策略项、三个窄委托、
   "min(计算值, 余额)"夹取、"扣零"语义。**
   - **策略枚举 `DeathPolicyOptions.RespawnFeePolicy`（`None`/`PctOfBalance`/`FixedByLevel`，
     默认 `None`）**：字面照抄 ADR 原文"计算方式为 `pct_of_balance` 或 `fixed_by_level`（引用
     曲线）二选一"，不是本模块自己发明的措辞。默认 `None` 保证本任务之前"不收复活费"的既有行为
     逐位不变——`ChargeRespawnFee` 私有方法开头即短路返回，不读取任何余额/委托。
   - **三个窄委托（`RespawnFeeBalance`/`RespawnFeeCharge`/`RespawnFeeFixedAmount`），而不是
     `DeathPolicyHost` 新增 `IUnitAccess`/`IEconomyHost` 构造依赖**：硬性规则"禁止
     `DeathPolicyHost` 直接依赖 `IEconomyHost`"——`RespawnFeeBalance`（签名同
     `IEconomyHost.GetBalance`）/`RespawnFeeCharge`（签名同
     `IEconomyHost.TryPay(unitId,currencyId,amount,reason)`，`reason` 由注入方闭包固定为
     `"respawn_fee"`，本模块不关心具体传什么 reason）供 `GameplayAssembly` 接线；
     `RespawnFeeFixedAmount`（签名只接 `unitId`，不接 `level`）供"`fixed_by_level` 策略如何从
     等级/曲线算出原始费用"这件事完全交给注入方——本模块不新增对任何曲线表结构、也不新增对
     `IUnitAccess.GetLevel`（读取等级）的编译期依赖，同 `ReviveUnit`/`ResolveDefaultSpawn` 两个
     既有窄委托"只关心签名，不关心背后怎么实现"的一贯判断记录。三者任一未接线都退化为"该分支
     费用按 0 处理"，不阻断复活（同本模块一贯的"未接线不阻断"惯例）。
   - **"哪种货币"——设计层裁定（2026-09-16）：采纳**：ADR-0034 决策 6 原文未规定复活费扣的是
     "唯一那种货币"还是可配置——`core/gameplay/economy` 框架层本身只有一种货币（ADR-0034 决策 1
     "一种货币，一个整数"），理论上不需要配置项。本模块仍新增
     `DeathPolicyOptions.RespawnFeeCurrencyId`（`Id?`，默认 `null`）显式要求配置——一是保持本
     模块不预设"经济模块只有一种货币"这条属于 `core/gameplay/economy` 的框架决策（本模块不引用
     该模块的任何强类型），二是 `null` 时不收费的退化路径本身就是"未配置=关闭"的既有惯例，不
     额外新增特殊逻辑。
   - **判断记录（"实际费用 = min(计算值, 当前余额)"两种策略统一夹取，且余额为零时仍无条件调用
     扣费委托——扣零，不是跳过整个扣费流程）**：ADR-0034 决策 6 原文"实际费用 = min(计算值, 当前
     余额)……余额为零扣零"——"扣零"这个措辞本身表明"扣费"这个动作发生了（只是发生的量是零），
     不是"检测到余额为零就跳过扣费"。本模块因此不对 `fee == 0` 做提前短路，无条件调用
     `RespawnFeeCharge(unitId, currencyId, fee)`——这与
     `core/gameplay/economy.EconomyHost.TryPay` 自身对 `amount == 0` 的既有行为完全一致（该方法
     的余额校验对 `amount=0` 必然通过，`Add(amount:0)` 因增量为零不产生 `currency_changed`，但
     仍无条件 `Enqueue` 一次 `EconomyChargedEvent{amount:0}`，见该方法判断记录），本模块是对齐
     经济模块已有的"零金额也算一次扣费"语义，不是新引入的特例。
   - **扣费发生在 `ReviveUnit` 调用之后，不是之前**：扣费与"是否放行复活"完全解耦——复活已经
     发生，扣费只是复活的一个后续副作用；即便注入方的 `RespawnFeeCharge` 内部有 bug 返回
     `false`，本方法也只记一条诊断，不撤销已经发生的复活、不重试、不抛异常（见
     `ChargeRespawnFee` 判断记录"理论上不应发生"）。
   - 测试：`tests/T_N4_9_RespawnFeeTests.cs`（余额为零复活 1 组、`fixed_by_level` 超余额扣至零
     1 组、`pct_of_balance` 按百分比 1 组、`None` 不收费 1 组、扣费委托未接线不阻断复活 1 组）。

7c. **T-N4-5 回归锁定（ADR-0033 决策 6"从不扣经验：无论 13 的死亡策略选哪种，经验不减"；06 第
   2.5 节同条）**：本模块从设计上不持有、也不依赖 `Core.Numbers.Progression.IProgressionHost`/
   `ProgressionHost` 任何类型（见本文档顶部"依赖"一节——只依赖 L0 + L2 `core/rules/common`），
   三种策略（`RespawnPoint`/`ReloadSave`/`Permadeath`）的结算逻辑（延迟复活、读档、删当前槽）
   本身不触达经验/等级状态，因此"死亡不扣经验"是模块边界的自然结果，不需要任何专门代码。回归
   用例见 `core/gameplay/death/tests/T_N4_5_RespawnPolicyDoesNotAffectXpTests.cs`（三种策略各
   一组：把一个与 `DeathPolicyHost` 完全独立装配的 `ProgressionHost` 挂在同一个玩家单位 id 上，
   走完整死亡结算流程后断言 `GetXp`/`GetLevel` 逐字节不变），把这条模块边界钉成一条可执行的
   回归锁，防止未来有人误在死亡路径上"顺手"接一条扣经验/降级的分支。

## 不负责什么

- 不解析/查找具体的复活点坐标算法本身（如"取最近出生点"而非固定 `spawn_points[0]`的距离比较）
  ——这属于 `ResolveDefaultSpawn` 委托实现方（`GameplayAssembly` 装配的
  `TeleportTargetResolver.Resolve`）的职责，本模块只负责"死亡后调用这个委托、延迟 N tick 后调用
  复活委托"这条编排逻辑。
- 不做尸体清理、掉落物生成、复活无敌帧一类周边表现/规则——06/08 文档未把它们划给死亡复活策略，
  分别属于既有的 `core/gameplay/loot`（死亡掉落）/表现层（复活特效）职责。
- 不持久化"待复活队列"：`respawn_point` 策略的延迟复活队列是纯运行期瞬时状态，不实现
  `IPersistable`。**C11-PENDING-LOAD 勘误（2026-09-11，消费方反馈第 C11 项，基线 1.22.0，见
  `architecture/落地计划/消费方反馈-2026-09-11-读档空间索引与复活生命周期.md`）：本条原判断记录
  "待复活的记录会丢失（复活永远不会发生），判定为可接受的简化"已不成立**——真实探针复现的不是
  "丢失"而是更严重的"残留"：死亡后留有一条尚未执行的延迟复活记录时若发生一次读档（同图或跨图，
  不限于死亡→`reload_save`那一条内部触发路径，玩家从菜单手工读档同样会撞上），读档本身已经把
  存活状态/生命值/位置按存档内容恢复到位，但这条读档前遗留的旧延迟复活记录不会被读档感知，会在
  它原定的 tick 到期时用死亡地图的默认复活点/复活血量把刚恢复好的读档状态覆盖掉。现由
  `ClearPending`（见判断记录 7）在读档开始前清空，不再是"可接受的简化"，而是"读档必须先失效
  它"的确定行为——存档语义因此简化为：若存档时刻已处于死亡未复活状态，读档后玩家按存档段
  （`player.vitals` 的 `alive`/资源池当前值）原样停留在死亡态，不会凭空复活（本模块当前仍不为
  这个队列本身新增一个存档段，语义上等价于"没有需要恢复的 pending"）。
- 不管理 AI/生物单位的死亡后续（复活、移除、刷新计时）——那是 `core/gameplay/spawn`
  （`respawn_policy`）与 `core/carriers/creature`（`Despawn`/`creature.despawned`）既有职责，
  本模块对非玩家单位的 `unit.died` 直接忽略（见判断记录、08 归属说明惯例）。
- T-N4-9：不实现 `fixed_by_level` 策略"按等级查哪条曲线"这件事本身——`RespawnFeeFixedAmount`
  只是一个签名窄委托，曲线表结构/来源（`econ.value_curve`、专用复活费曲线，或任何其它形态）完全
  交给注入方（游戏层/`GameplayAssembly`）决定，见判断记录 8。
