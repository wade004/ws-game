# 逐条代码审查

路径均为本审计工作树中的绝对路径。复现链接明确标注了 `.NET` 有界复现或组件链复现；其余条目证据等级为“静态”。任何有界复现都不等同 Unity/完整 Runtime 通过。

## P1：生命周期、存档与场景安全

### FND-01 — 存档槽与备份路径发生合法 ID 碰撞（P1）

- **触发**：合法槽 `slot.a.bak1` 的正式文件名与槽 `slot.a` 的备份 `slot.a.bak1.json` 相同。
- **影响**：保存/删除 `slot.a` 会覆盖或删除 `slot.a.bak1` 的正式档；`ListSlots` 还会按备份模式隐藏该槽，造成跨槽毁档与不可见槽。
- **源码**：[SaveSystem.cs:450](D:/workespace/ws-game-review-b3b91ee/core/foundation/save_system/core/SaveSystem.cs:450)、[SaveSystem.cs:489](D:/workespace/ws-game-review-b3b91ee/core/foundation/save_system/core/SaveSystem.cs:489)、[SaveSystem.cs:830](D:/workespace/ws-game-review-b3b91ee/core/foundation/save_system/core/SaveSystem.cs:830)。
- **建议**：把备份放入独立命名空间/目录，或使用不可与任意合法槽 ID 重合的编码；统一更新列举、容量、删除和迁移路径。
- **验收**：同时保存 `slot.a` 与 `slot.a.bak1`，两者独立列举、读取、删除；备份轮换不改动另一槽。
- **证据边界**：.NET 有界复现已触发，见 [validation-repros.txt](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/validation-repros.txt) R3；未升级为发布文件系统/消费方验收。

### FND-02 — SceneRouter 异步回调无代际隔离（P1）

- **触发**：加载 A 失败触发清理并开始 B；A 的迟到回调仍写入共享 `_pendingResources`。
- **影响**：A 的成功/失败结果可污染 B，导致 B 错误失败、错误回主菜单或资源状态混用。
- **源码**：[SceneRouter.cs:190](D:/workespace/ws-game-review-b3b91ee/core/foundation/scene_router/core/SceneRouter.cs:190)、[SceneRouter.cs:226](D:/workespace/ws-game-review-b3b91ee/core/foundation/scene_router/core/SceneRouter.cs:226)。
- **建议**：每次导航生成 generation/request token，回调携带并校验代际；终止请求时清理旧资源状态。
- **验收**：A 的迟到成功/失败对 B 无效；B 只由自身资源回调完成或失败；失败回退只发生一次。
- **证据边界**：StubResourceLoader 有界复现已触发，见 [validation-boundaries.md](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/validation-boundaries.md) FND02；未升级为 Unity 资源加载器。

## P2：数据、回合与输入边界

### FND-03 — DataRegistry 局部加载错误不持久保留（P2）

- **触发**：`LoadAll` 某表坏 JSON/坏信封后，调用无参数 `Validate()` 或重载无关的好表。
- **影响**：原始错误被新建空 `issues` 遮蔽，`_blocked` 可能解除，残缺表继续被读取。
- **源码**：[DataRegistry.cs:228](D:/workespace/ws-game-review-b3b91ee/core/foundation/data_registry/core/DataRegistry.cs:228)、[DataRegistry.cs:239](D:/workespace/ws-game-review-b3b91ee/core/foundation/data_registry/core/DataRegistry.cs:239)、[DataRegistry.cs:245](D:/workespace/ws-game-review-b3b91ee/core/foundation/data_registry/core/DataRegistry.cs:245)。
- **建议**：按表保留加载错误，直到该表成功重载并通过校验；全量验证应合并历史加载诊断。
- **验收**：坏表在无关 Validate/Reload 后仍阻断；仅修复并重载该表后解除；报告包含表名和原始错误。
- **证据边界**：坏 JSON/Validate 有界复现已触发，见 [validation-boundaries.md](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/validation-boundaries.md) FND03；未升级为生产数据运行。

### FND-04 — ClearAll 重置 SimTimers 后旧句柄可取消新句柄（P2）

- **触发**：`WorldSim.ClearAll` 替换 `SimTimers`；新实例从句柄 1 重新编号；调用方缓存旧 timers/handle。
- **影响**：旧 handle 与新 handle 值相等时可误取消新计时器；旧 timers 引用也不再推进，造成静默时序错误。
- **源码**：[WorldSim.cs:372](D:/workespace/ws-game-review-b3b91ee/core/foundation/sim_loop/core/WorldSim.cs:372)；[SimTimers.cs:26](D:/workespace/ws-game-review-b3b91ee/core/foundation/sim_loop/core/SimTimers.cs:26)。
- **建议**：稳定宿主对象并提供 Clear，或在句柄中编码 generation；所有跨场景引用在清理时失效。
- **验收**：Clear 前后句柄不可混用；旧句柄取消不影响新计时器；旧计时器集合为空且不再推进。
- **证据边界**：.NET 有界复现已触发，见 [validation-repros.txt](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/validation-repros.txt) R4；未升级为完整场景 Runtime。

### FND-05 — fixed_order 新参与者绕过 AP 初始化（P2）

- **触发**：`TurnScheduler` fixed_order 分支在 `AddParticipant` 先返回，未执行后续行动点初始化。
- **影响**：战斗中途加入单位本轮 AP 为 0，移动预算和离散行动行为不符合架构步骤。
- **源码**：[TurnScheduler.cs:302](D:/workespace/ws-game-review-b3b91ee/core/foundation/sim_loop/core/TurnScheduler.cs:302)；对应契约 [04_数据与内容管线.md:193](D:/workespace/ws-game-review-b3b91ee/architecture/04_数据与内容管线.md:193)。
- **建议**：先完成资源/AP 初始化，再按先攻策略插入顺序；明确先攻顺序与移动预算的独立性。
- **验收**：fixed_order、initiative_stat、action_points 三种策略中途加入均得到规定本轮预算，且顺序稳定。
- **证据边界**：.NET 有界复现已触发，见 [validation-repros.txt](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/validation-repros.txt) R5；未升级为完整离散战斗。

### FND-06 — ReplayPlayer 二次 Load 未清理审计状态（P2）

- **触发**：同一实例二次 `Load` 重置 world/tick，却不清空 `_audit`，且旧世界 EventBus 订阅仍可能存活。
- **影响**：相同录像第二次播放的事件日志会夹带前次记录，摘要不一致并受历史影响；旧世界订阅未释放是附加生命周期风险。
- **源码**：[ReplayPlayer.cs:53](D:/workespace/ws-game-review-b3b91ee/core/foundation/save_system/core/ReplayPlayer.cs:53)、[ReplayPlayer.cs:245](D:/workespace/ws-game-review-b3b91ee/core/foundation/save_system/core/ReplayPlayer.cs:245)。
- **建议**：每次 Load 清空 audit 并隔离/释放旧 world/bus，或明确播放器一次性使用并拒绝二次 Load。
- **验收**：同实例两次同数据的摘要/事件/tick 相同；旧订阅不会向第二次回放写入；Dispose 释放订阅。
- **证据边界**：静态生命周期；未执行二次回放。

### FND-07 — 正式档缺失时不尝试备份，信封校验过浅（P2）

- **触发**：正式文件不存在直接返回 `NotFound`；或 `sections:null` 等仅有字段的 JSON 被视为合法候选。
- **影响**：有效备份无法恢复；坏正式档可能阻止选择有效备份或在后续路径才失败。
- **源码**：[SaveSystem.cs:235](D:/workespace/ws-game-review-b3b91ee/core/foundation/save_system/core/SaveSystem.cs:235)、[SaveSystem.cs:366](D:/workespace/ws-game-review-b3b91ee/core/foundation/save_system/core/SaveSystem.cs:366)、[SaveSystem.cs:709](D:/workespace/ws-game-review-b3b91ee/core/foundation/save_system/core/SaveSystem.cs:709)；说明 [save_system/README.md:108](D:/workespace/ws-game-review-b3b91ee/core/foundation/save_system/README.md:108)。
- **建议**：正式档与全部备份统一作为候选，先做完整信封校验（版本类型、sections 对象）再按优先级选择。
- **验收**：主档缺失/损坏且 bak 有效时返回 `LoadedFromBackup`；全部候选无效才 `NotFound/Corrupted`；`sections:null` 不通过。
- **证据边界**：静态分支与契约；未执行损坏档夹具。

### FND-08 — 同帧按键 down/up 丢失 Button edge（P2）

- **触发**：一批输入事件内同一按键先 down 后 up；实现只在批次末按 `_keysDown` 等 held 集合求 rising。
- **影响**：点击/按下/释放都在一个批次完成时，整个 action 保持 up，触发事件丢失。
- **源码**：[InputMapHost.cs:189](D:/workespace/ws-game-review-b3b91ee/core/foundation/input_map/core/InputMapHost.cs:189)。
- **建议**：逐事件维护本帧 pressed/released edge，再以 held 状态计算持续动作；批次末清 edge。
- **验收**：同帧 down/up 触发一次按下；跨帧 held 不重复；up 后状态正确释放。
- **证据边界**：静态事件折叠；未运行输入桩。

### FND-09 — 迁移链允许超过当前运行时版本仍成功（P2）

- **触发**：当前运行时版本为 2，却登记迁移 1→3；`TryRunMigrationChain` 只以 `version < Current` 循环和字典键推进。
- **影响**：加载 v1 会执行 1→3，越过当前支持版本仍报告成功，并错误发出迁移到 2 的事件/交付未经当前版本验证的结构。
- **源码**：[SaveSystem.cs:389](D:/workespace/ws-game-review-b3b91ee/core/foundation/save_system/core/SaveSystem.cs:389)。
- **建议**：拒绝 `ToVersion > CurrentSaveVersion`，迁移循环结束必须恰好等于当前版本；允许当前版本为 3 时合法的 1→3。
- **验收**：当前为 2 时 1→3 失败且不加载段；当前为 3 时 1→3 成功，事件和最终版本准确。
- **证据边界**：静态迁移循环；未运行迁移夹具。

## P1：装备、玩法与战斗安全

### FND-10 — EquipmentPersistable.Load 合并旧装备而非替换（P1）

- **触发**：当前单位有装备，读取空/较小装备快照；`Load` 只 Inject/Equip，不清除旧槽位。
- **影响**：空档读档仍保留旧装备；同 ID 重复恢复可能返包/复制实例，跨槽污染装备与属性。
- **源码**：[ItemPersistable.cs:106](D:/workespace/ws-game-review-b3b91ee/core/carriers/item/core/ItemPersistable.cs:106)；换装返包路径 [EquipmentHost.cs:145](D:/workespace/ws-game-review-b3b91ee/core/carriers/item/core/EquipmentHost.cs:145)。
- **建议**：按快照替换单位完整装备状态，先清理旧装备和 grants，再原子恢复背包/装备；空对象也必须清空。
- **验收**：空快照清空装备；缺槽快照移除旧槽；重复 Load 幂等；跨槽恢复不复制物品、不遗留 grants。
- **证据边界**：.NET 有界复现已触发，见 [validation-repros.txt](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/validation-repros.txt) R2/R2b；未升级为 Unity/完整跨场景 Runtime。

### GP-01 — QuestPersistable.Load 不清除当前任务状态（P1）

- **触发**：先保存空任务档，再接取/完成任务，加载旧档；`Load` 只对快照键调用 RestoreProgress。
- **影响**：任务、完成次数和每日记录跨档残留，读档无法回滚到保存点。
- **源码**：[QuestPersistable.cs:64](D:/workespace/ws-game-review-b3b91ee/core/gameplay/quest/core/QuestPersistable.cs:64)；注册 [GameplayAssembly.cs:1092](D:/workespace/ws-game-review-b3b91ee/core/gameplay/assembly/GameplayAssembly.cs:1092)。
- **建议**：按玩家/快照替换完整任务状态，删除快照不存在的任务；用临时状态校验后一次提交。
- **验收**：空档回滚、任务删除、完成计数/每日记录和跨槽隔离均正确。
- **证据边界**：.NET 有界复现已触发，见 [validation-repros.txt](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/validation-repros.txt) R1；未升级为 Unity/完整跨场景 Runtime。

### GP-02 — 默认动画缺少瞬态完成、复活和销毁回收（P1）

- **触发**：默认工厂仅 StateChanged→Play；未将完成回调接到 `NotifyTransientStateFinished`，也未在 `unit.respawned/entity.destroyed` 调 `Forget`。
- **影响**：受击状态优先级锁住后永久拒绝移动/攻击/施法；死亡后同 ID 复活或重建 View 仍可能被旧终态阻断。
- **源码**：[UnityViewFactory.cs:265](D:/workespace/ws-game-review-b3b91ee/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:265)；[AnimStateMachine.cs:141](D:/workespace/ws-game-review-b3b91ee/presentation/render/core/AnimStateMachine.cs:141)。
- **建议**：非循环剪辑完成后通知状态机；订阅复活/销毁事件清理实体状态和播放器引用；保证同 ID 重建从 Idle 开始。
- **验收**：Hit/Death 结束后可进入 Move/Attack/Cast；复活恢复可动；销毁后无旧状态和回调。
- **证据边界**：静态默认接线；未执行 Unity。

### GP-04 — 切图未终止旧 Encounter/Level（P1）

- **触发**：`LeaveMap` 只卸载 AreaTrigger/Spawn，不终止 Encounter；`EvaluateAll` 继续扫描旧 `ActiveInstanceIds`。
- **影响**：A 图遭遇在 B 图继续求值，可能发波次、奖励或竞技场重置，且使用 B 图上下文。
- **源码**：[GameplayAssembly.cs:886](D:/workespace/ws-game-review-b3b91ee/core/gameplay/assembly/GameplayAssembly.cs:886)；[EncounterHost.cs:184](D:/workespace/ws-game-review-b3b91ee/core/gameplay/encounter/core/EncounterHost.cs:184)；[EncounterTickHandler.cs:75](D:/workespace/ws-game-review-b3b91ee/core/gameplay/encounter/core/EncounterTickHandler.cs:75)。
- **建议**：LeaveMap 以 map/instance 终止 Encounter/Level run、清理 override 和 tick 注册；重新进图建立新实例。
- **验收**：A→B 后 A 无波次/奖励/重置；重进 A 不复用旧运行实例；销毁事件与离图顺序稳定。
- **证据边界**：静态链；未执行完整跨图 Runtime。

### RC-01 — Proc 事件链深度预算失效（P1）

- **触发**：CastPipeline 的 `_triggerDepth` 只覆盖同步触发调用；Combat 事件经 EventBus 异步 Enqueue 后深度已归零。无 ICD 的 `heal_done → trigger_skill(heal)` 可跨派发 pass 持续循环。
- **影响**：不会由 MaxTriggerDepth 及时拒绝；EventBus 只到 `MaxDispatchPasses` 记录诊断并暂停，待处理事件留存，可能形成长期事件风暴/高 CPU/重复效果。
- **源码**：[CastPipeline.cs:426](D:/workespace/ws-game-review-b3b91ee/core/rules/skill/core/CastPipeline.cs:426)；[ProcHost.cs:121](D:/workespace/ws-game-review-b3b91ee/core/rules/skill/core/ProcHost.cs:121)；限流 [EventBus.cs:94](D:/workespace/ws-game-review-b3b91ee/core/foundation/event_bus/core/EventBus.cs:94)。
- **建议**：把触发链身份/深度或预算跨 EventBus 传播，按事件轮次限制；达到上限后丢弃自循环并允许下一 tick 正常工作。
- **验收**：真实 Combat+EventBus 循环到达链上限后排空/拒绝，不靠全局 pass 截断；下一 tick 新事件可执行。
- **证据边界**：静态；未运行异步事件链。

### RC-02 — 生物销毁后 CombatHost 残留访问已注销资源（P1）

- **触发**：进战生物 Despawn 注销 Stats/Powers，但 CombatHost `_inCombat` 未清理；脱战延迟后仍调用 Powers.SetInCombat。
- **影响**：访问不存在单位资源而抛异常，且战斗/仇恨/计时器残留。
- **源码**：[CombatHost.cs:128](D:/workespace/ws-game-review-b3b91ee/core/rules/combat/core/CombatHost.cs:128)；[CreatureFactory.cs:148](D:/workespace/ws-game-review-b3b91ee/core/carriers/creature/core/CreatureFactory.cs:148)。
- **建议**：订阅 entity.destroyed 做幂等战斗清理，取消脱战计时器并移除双向仇恨/所属关系。
- **验收**：进战→销毁→跨脱战周期无异常、无残留战斗条目和计时器。
- **证据边界**：.NET 有界复现已触发真实 Combat/Carriers 组件注销单位访问异常，见 [validation-repros.txt](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/validation-repros.txt) R7；未升级为 Unity/完整 Spawn 或切图 Runtime。

### RC-03 — 读条死亡/销毁未取消（P1）

- **触发**：CastPipeline 仅处理控制/受伤中断，不订阅死亡/销毁；AdvanceOne/FinishCast 不重验施法者存在性。
- **影响**：死亡者继续扣资源并结算；销毁后 FinishCast 访问已注销 Powers/Unit 而抛异常。
- **源码**：[CastPipeline.cs:84](D:/workespace/ws-game-review-b3b91ee/core/rules/skill/core/CastPipeline.cs:84)；[CastPipeline.cs:303](D:/workespace/ws-game-review-b3b91ee/core/rules/skill/core/CastPipeline.cs:303)。
- **建议**：死亡/销毁事件取消当前读条与队列，清冷却/锁定；完成前校验施法者和目标仍有效。
- **验收**：死亡、销毁×读条/引导/队列均无成功事件、额外效果或异常。
- **证据边界**：静态；未运行死亡/销毁读条集成夹具。

## P2：规则、玩法、表现与工具

### RC-04 — 无效技能提前扣 AP（P2）

**触发**：CastPipeline 在目标、射程、LOS 检查前调用 `TryConsumeActionPoints`。**影响**：AI 连续失败技能会耗尽预算。源码：[CastPipeline.cs:188](D:/workespace/ws-game-review-b3b91ee/core/rules/skill/core/CastPipeline.cs:188)，架构顺序 [06_规则层_属性技能战斗AI.md:201](D:/workespace/ws-game-review-b3b91ee/architecture/06_规则层_属性技能战斗AI.md:201)。**建议**：全部检查通过后才提交扣点或提供可回滚预留。**验收**：无目标、超距、LOS 三种失败各自 AP 不变。**证据边界**：.NET 有界复现已触发超距路径（R6：超距仍产生 1 次消费）；无目标与 LOS 分支仍为静态，见 [validation-repros.txt](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/validation-repros.txt)。

### RC-05 — 装备授予技能缺来源计数（P2）

**触发**：装备 Learn、卸下 Forget，HashSet 不记录永久学习与多个装备来源。**影响**：卸一件会遗忘永久技能，或移除共享光环。源码：[EquipmentHost.cs:329](D:/workespace/ws-game-review-b3b91ee/core/carriers/item/core/EquipmentHost.cs:329)、[CarriersAssembly.cs:254](D:/workespace/ws-game-review-b3b91ee/core/carriers/assembly/CarriersAssembly.cs:254)、[SkillHost.cs:162](D:/workespace/ws-game-review-b3b91ee/core/rules/skill/core/SkillHost.cs:162)。**建议**：永久、装备、天赋等来源分离并按引用计数。**验收**：永久+双装备、换装及存读档只撤最后临时来源。**证据边界**：静态。

### RC-06 — 派生上限/评级缓存缺生产重算接线（P2）

**触发**：Power 上限只在注册或显式 `RecomputeMax` 读取属性，等级查找结果缓存不随 LevelLookup 失效。**影响**：升级、装备、光环后上限/评级可旧。源码：[PowerHost.cs:123](D:/workespace/ws-game-review-b3b91ee/core/numbers/power_set/core/PowerHost.cs:123)、[StatHost.cs:204](D:/workespace/ws-game-review-b3b91ee/core/numbers/stat_block/core/StatHost.cs:204)、[RulesAssembly.cs:166](D:/workespace/ws-game-review-b3b91ee/core/rules/assembly/RulesAssembly.cs:166)。**建议**：装配层订阅属性/等级提交事件集中失效重算。**验收**：默认装配无需手工调用即可正确。**证据边界**：静态。

### RC-07 — 离散学派锁不衰减（P2）

**触发**：`AdvanceSchoolLocks` 只由连续 `Pipeline.Update` 调用，离散施法和轮末均不推进。**影响**：锁可永久存在。源码：[CastPipeline.cs:282](D:/workespace/ws-game-review-b3b91ee/core/rules/skill/core/CastPipeline.cs:282)、[SkillHost.cs:212](D:/workespace/ws-game-review-b3b91ee/core/rules/skill/core/SkillHost.cs:212)。**建议**：离散轮末推进且避免连续双扣。**验收**：锁两轮后按约定释放，连续模式仍按秒工作。**证据边界**：静态。

### RC-08 — movement 施法中断无生产接线（P2）

**触发**：`NotifyMoved` 只有定义，真实移动只发 UnitMovedEvent，Skill 未订阅。**影响**：声明 movement interrupt 的读条可边移动边完成。源码：[SkillHost.cs:140](D:/workespace/ws-game-review-b3b91ee/core/rules/skill/core/SkillHost.cs:140)、[CastPipeline.cs:377](D:/workespace/ws-game-review-b3b91ee/core/rules/skill/core/CastPipeline.cs:377)、[MovementTickHandler.cs:363](D:/workespace/ws-game-review-b3b91ee/core/carriers/unit/core/MovementTickHandler.cs:363)。**建议**：成功位移同步通知。**验收**：成功移动中断，阻挡未移动不取消。**证据边界**：静态。

### RC-09 — 抛射物整步命中后才截最大射程（P2）

**触发**：`newPos` 直接跨过剩余射程，先 `TryResolveUnitHits` 再检查 `MaxRange`。**影响**：大步更新可命中射程外目标，过射程爆炸中心也错误。源码：[ProjectileHost.cs:202](D:/workespace/ws-game-review-b3b91ee/core/carriers/projectile/core/ProjectileHost.cs:202)。**建议**：按剩余射程截段后再做墙/单位/终点测试。**验收**：大步与拆步一致，外部目标不命中。**证据边界**：静态。

### RC-10 — AI 把所有技能目标强塞当前敌人（P2）

**触发**：AiHost.Evaluate 对 Rotation 每个技能传入 `state.Target`，CastPipeline 见非空 targets 后跳过技能目标链。**影响**：自疗可作用于敌人，友疗/AOE 过滤失效，重复目标可能重复效果。源码：[AiHost.cs:216](D:/workespace/ws-game-review-b3b91ee/core/rules/ai/core/AiHost.cs:216)、[CastPipeline.cs:198](D:/workespace/ws-game-review-b3b91ee/core/rules/skill/core/CastPipeline.cs:198)。**建议**：当前选择目标与最终链解析分离；显式目标也按阵营、存活、数量过滤去重。**验收**：攻击、自疗、友疗、AOE 都遵循各自 chain。**证据边界**：静态。

### RC-11 — weapon_damage_pct 未消费武器基础伤害（P2）

**触发**：武器基数 100、`weapon_damage_pct=0.75` 时，EffectDispatcher 把 pct 本身当基础值。**影响**：当前结果为 0.75，更换武器不会改变该技能伤害；仅针对使用此字段的技能。源码：[EffectDispatcher.cs:145](D:/workespace/ws-game-review-b3b91ee/core/rules/skill/core/EffectDispatcher.cs:145)、[EquipmentHost.cs:254](D:/workespace/ws-game-review-b3b91ee/core/carriers/item/core/EquipmentHost.cs:254)。**建议**：注入明确武器伤害来源并计算 `weaponBase * pct`，定义无武器策略。**验收**：100/200 基数分别得 75/150。**证据边界**：静态。

### GP-03 — VfxPlayer.Update 未接入三条生产帧循环（P2）

**触发**：VFX 生命周期池和 pending 首载超时只在 `Update` 推进；生产入口未调用。**影响**：循环特效不按 lifetime 停，迟迟不回调的加载请求永久积压。源码：[VfxPlayer.cs:298](D:/workespace/ws-game-review-b3b91ee/presentation/vfx_sfx/core/VfxPlayer.cs:298)；入口 [FrameworkResidentHost.cs:669](D:/workespace/ws-game-review-b3b91ee/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Shell/FrameworkResidentHost.cs:669)、[GameFoundationBootstrap.cs:594](D:/workespace/ws-game-review-b3b91ee/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Bootstrap/GameFoundationBootstrap.cs:594)、[GameBootstrap.cs:392](D:/workespace/ws-game-review-b3b91ee/games/_template/Runtime/GameBootstrap.cs:392)。**建议**：每个生产帧循环推进并统一 Dispose/切图清理。**验收**：真实 lifetime 停止、pending 超时、无遗留。**证据边界**：静态。

### GP-05 — Dialog 执行不重验 VisibleIf/Condition（P2）

**触发**：View 构造时过滤，ChooseOption/AdvanceStory 只按原始 index 执行。**影响**：显示后世界状态变化仍可执行隐藏动作，DialogViewModel 也不随 world/quest 变化刷新。源码：[DialogHost.cs:132](D:/workespace/ws-game-review-b3b91ee/core/gameplay/dialog/core/DialogHost.cs:132)、[DialogHost.cs:203](D:/workespace/ws-game-review-b3b91ee/core/gameplay/dialog/core/DialogHost.cs:203)。**建议**：执行时用当前上下文重验，失效则拒绝并刷新。**验收**：初始隐藏及显示后变隐藏均无副作用，true 正常执行。**证据边界**：静态。

### GP-06 — Unity 默认动画冷启动只读缓存并永久 fallback（P2）

**触发**：`TryGetEffect` 未命中时立即登记单帧 fallback，即使 `display.anim_set` 和磁盘资源存在，也未 `LoadAsync` 或在加载完成后重注册。**影响**：冷启动真实剪辑永久退化为 fallback。源码：[UnityViewFactory.cs:215](D:/workespace/ws-game-review-b3b91ee/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:215)。**建议**：首次引用主动加载或预热；加载完成后注册真实多帧剪辑。**验收**：冷创建→加载完成后使用真实多帧，后续不重复加载。**证据边界**：静态；未断言纸娃娃被白点替换。

### GP-07 — 消耗型收集扣除失败仍增加任务进度（P2）

**触发**：`RemoveItem` 返回 false 时仍使用事件数量 `take` 调 `UpdateProgress`。**影响**：一件物品可同时完成多个 consume 任务。源码：[QuestHost.cs:576](D:/workespace/ws-game-review-b3b91ee/core/gameplay/quest/core/QuestHost.cs:576)。**建议**：按实际成功扣除量增长。**验收**：1 件物品最多产生 1 件总成功消耗进度，失败不加。**证据边界**：静态。

### GP-08 — 接取非消耗收集任务不从已有库存初始化（P2）

**触发**：Accept 初始化全零，库存同步只由后续物品事件驱动。**影响**：先持有足量物品再接非消耗 collect 任务不能即时完成。源码：[QuestHost.cs:146](D:/workespace/ws-game-review-b3b91ee/core/gameplay/quest/core/QuestHost.cs:146)，库存处理 [QuestHost.cs:558](D:/workespace/ws-game-review-b3b91ee/core/gameplay/quest/core/QuestHost.cs:558)。**建议**：Accept 后按现存库存初始化非消耗目标；消耗型目标另明确既有库存的消耗政策和扣除时点。**验收**：先持有 3 件再接需 3 件 nonconsume collect 立即完成；consume 按明确政策验收。**证据边界**：静态。

### GP-09 — 首次异步 VFX/SFX 不属于离散完成门（P2）

**触发**：冷资源播放排 pending 返回 null，PlaybackQueue 认为动作完成；`HasPendingPlayback` 只看 queue/merger。**影响**：资源回调可在下一步甚至 VFX/SFX 之间乱序派发。源码：[FeedbackBinder.cs:137](D:/workespace/ws-game-review-b3b91ee/presentation/feedback_binder/core/FeedbackBinder.cs:137)、[CompositeFeedbackSink.cs:55](D:/workespace/ws-game-review-b3b91ee/presentation/feedback_binder/core/CompositeFeedbackSink.cs:55)、[VfxPlayer.cs:176](D:/workespace/ws-game-review-b3b91ee/presentation/vfx_sfx/core/VfxPlayer.cs:176)、[SfxPlayer.cs:117](D:/workespace/ws-game-review-b3b91ee/presentation/vfx_sfx/core/SfxPlayer.cs:117)。**建议**：pending 首载纳入本步完成协议并按序派发。**验收**：不要求等待粒子完整 lifetime，但冷 A 未完成时 B 不先播。**证据边界**：静态。

### GP-10 — 模板与场景 Bootstrap 漏 CharacterRig 时间轴推进（P2）

**触发**：模板与场景 `OnFrameTick` 都更新 Feedback，却未调用 Rig/程序动画更新。**影响**：Move/Stagger/Fade/Scale 排入 sequencer 后不推进。源码：[GameBootstrap.cs:392](D:/workespace/ws-game-review-b3b91ee/games/_template/Runtime/GameBootstrap.cs:392)、[GameFoundationBootstrap.cs:594](D:/workespace/ws-game-review-b3b91ee/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Bootstrap/GameFoundationBootstrap.cs:594)。**建议**：两个 Bootstrap 每帧驱动所有已创建 CharacterRig。**验收**：原语在帧后改变位置/透明度并完成。Flash 走模板 `:301` 独立 FlashReceiver，不属于本条。**证据边界**：静态。

### TOOL-01 — check.ps1 缺失可执行文件误报成功（P2）

**触发**：`Test-NativeExitCode` 的 EAP Continue 分支在 `&` 找不到程序后沿用 `LASTEXITCODE=0`，返回 True。**影响**：本地门禁或依赖缺失环境可把未执行的检查记为 PASS；本轮未声称 CI 已发生该故障。源码：[check.ps1:259](D:/workespace/ws-game-review-b3b91ee/check.ps1:259)。**建议**：用 `Get-Command`/`Application` 先验证存在，只有成功启动才读取退出码。**验收**：缺失程序探针明确 FAIL；存在程序按真实退出码判定。复现：[tool-01-repro.txt](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/repro/tool-01-repro.txt)，PowerShell 5.1 实测。**证据边界**：该记录仅证明 helper。

### TOOL-02 — SFX/VFX 资源 ID 归一化发生碰撞（P2）

**触发**：`sfx.fire.hit` 与合法 `sfx.fire_hit` 去域后均归一化为 `sfx.fire_hit_v0`。**影响**：第二个输入覆盖第一个；VFX 同类实现静态确认。源码：[sfx_cmd.py:67](D:/workespace/ws-game-review-b3b91ee/toolchain/asset_import/sfx_cmd.py:67)、[vfx_cmd.py:72](D:/workespace/ws-game-review-b3b91ee/toolchain/asset_import/vfx_cmd.py:72)。**建议**：保留 ID 结构或使用无碰撞编码。**验收**：两个不同 ID 导入后有独立资源，重复同 ID 才按明确覆盖策略。复现：[tool-02-repro.txt](D:/workespace/ws-game-review-b3b91ee/architecture/落地计划/audit-b3b91ee-20260907/repro/tool-02-repro.txt) 显示同资源/一文件/第二源覆盖。**证据边界**：SFX 动态复现、VFX 静态。

## 去重与证据说明

FND-10 与 GP-01 分别覆盖装备和任务快照，不能合并为“读档残留”一句而丢失验收；GP-03 与 GP-09 分别覆盖 VFX 生命周期驱动和离散首载完成门；GP-02、GP-06、GP-10 分别覆盖状态机生命周期、剪辑冷加载和模板时间轴接线。历史已修的 dropped loot、Move 参数、Feedback merger 探针、ResourceLoader 传入、阴影、HUD AP/顺序条、死亡策略和默认 progression/archetype/RNG 注册未计入本表。
