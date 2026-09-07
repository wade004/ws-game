# 代码审查结果（基线 7e63d66）

范围是 `7e63d6695644644e20f30e006b9efa7e531afa34` 的架构全章、主要运行链和本轮变更深审。C01、C05 有真实 console 复现；其余条目是当前源码静态证据，未声称 Runtime 或 Unity 已验收。C01–C07 为 P1，C08–C12 为 P2。

## C01（P1）正式槽名被 legacy 备份路径跨槽命中

**触发与实际行为。** 通过真实 `SaveSystem` 正常保存合法正式槽 `slot.a.bak1`，再请求不存在正式档且没有新布局备份的 `Load(slot.a)`。正确结果应是 `NotFound`；实际结果为 `LoadedFromBackup`，并加载了 `slot.a.bak1` 的 `meta.slot_id` 和 `repro.marker` 业务段，随后还生成 `backups/slot.a.bak1.json`。这是 `REPRODUCED`，不是伪造 legacy 文件。证据见 [CORE-A 输出](evidence/core-repros-final-run.stdout.log) 与 [复现源码](repro/Program.cs)。

**原因链与影响。** `TryReadLegacyBackup` 以请求槽名拼接 `slot.a.bak1.json`（[SaveSystem.cs:981](D:/workespace/ws-game-review-7e63d66/core/foundation/save_system/core/SaveSystem.cs:981)），只检查 envelope，不核对 `meta.slot_id`，通过后复制并返回 `LoadedFromBackup`（[SaveSystem.cs:990](D:/workespace/ws-game-review-7e63d66/core/foundation/save_system/core/SaveSystem.cs:990)、[SaveSystem.cs:1002](D:/workespace/ws-game-review-7e63d66/core/foundation/save_system/core/SaveSystem.cs:1002)、[SaveSystem.cs:1005](D:/workespace/ws-game-review-7e63d66/core/foundation/save_system/core/SaveSystem.cs:1005)）。独立合法正式槽因此可能被另一个槽的旧布局兜底路径误读。

**测试缺口、建议与验收。** 既有 legacy 用例覆盖同槽恢复，未覆盖允许点号和 `.bakN` 的合法槽名碰撞。应让备份命名与正式槽命名无歧义，或在候选中强制核对槽身份并建立迁移索引；验收 `Load(slot.a)=NotFound`，同时 `slot.a.bak1` 可独立列出、读取和删除。证据等级：Runtime console `REPRODUCED`。

## C02（P1）真实 Despawn 后周期效果仍读取来源状态

**触发与实际行为。** 施法者给存活目标施加带来源缩放的周期 DOT，随后用真实 `CreatureFactory.Despawn` 施法者，再由周期 tick 结算。当前源码仍允许效果读取已注销来源的 stats；战斗通知路径也把已销毁单位送入来源存在性要求的 power 状态。静态审查未将逻辑死亡或保留测试 fake 当作 Despawn Runtime 复现。

**原因链与影响。** `AuraHost.FirePeriodic` 继续以原 `SourceId` 构造效果，`EffectDispatcher` 在 scaling stat 存在时直接调用来源 stat（[EffectDispatcher.cs:166](D:/workespace/ws-game-review-7e63d66/core/rules/skill/core/EffectDispatcher.cs:166)）；`CreatureFactory.Despawn` 在 [CreatureFactory.cs:148](D:/workespace/ws-game-review-7e63d66/core/carriers/creature/core/CreatureFactory.cs:148)、[CreatureFactory.cs:154](D:/workespace/ws-game-review-7e63d66/core/carriers/creature/core/CreatureFactory.cs:154)、[CreatureFactory.cs:155](D:/workespace/ws-game-review-7e63d66/core/carriers/creature/core/CreatureFactory.cs:155) 注销实体及其运行期来源。战斗事件可经 `CombatHost.NotifyCombatEvent` 调用 `_powers.SetInCombat`（[CombatHost.cs:120](D:/workespace/ws-game-review-7e63d66/core/rules/combat/core/CombatHost.cs:120)），真实注销后周期或进战路径会抛错或无法完成。

**测试缺口、建议与验收。** 现有 fake unit access 删除本地单位但保留 stats/powers，不能代表真实注销语义；Resolver 的来源等级降级只覆盖其中一个 mitigation 分支。应定义来源销毁后的 DOT 生命周期、数值和战斗归属，并让所有周期/战斗来源查询遵守该策略；验收真实 Despawn 后多次周期 tick 无异常，效果按明确策略继续、冻结或移除。证据等级：当前源码与测试替身静态审阅，`NOT_EXECUTED`。

## C03（P1）吸收耗尽移除重置触发链深度

**触发与实际行为。** 永久 proc 光环 P 监听 `aura.removed`，chance=1、无 ICD，且 P 不会被 S 移除；S 创建吸收 Aura A，再造成恰好足以耗尽 A 且完全被 A 吸收的自身伤害。吸收耗尽进入移除事件，S 再次触发，形成跨 pass 的事件风暴。

**原因链与影响。** `ConsumeAbsorb` 没有 depth 参数，耗尽时直接调用 `RemoveInstanceInternal(instance, "absorb_depleted")`（[AuraHost.cs:538](D:/workespace/ws-game-review-7e63d66/core/rules/skill/core/AuraHost.cs:538)、[AuraHost.cs:557](D:/workespace/ws-game-review-7e63d66/core/rules/skill/core/AuraHost.cs:557)）；移除事件仍由内部队列发布（[AuraHost.cs:257](D:/workespace/ws-game-review-7e63d66/core/rules/skill/core/AuraHost.cs:257)、[AuraHost.cs:273](D:/workespace/ws-game-review-7e63d66/core/rules/skill/core/AuraHost.cs:273)），该入口落到默认 depth 0，绕开 `MaxTriggerDepth` 的收敛预算。EventBus 达到 MaxDispatchPasses 时只诊断并保留剩余队列，下一次调用仍可继续风暴；它与已修复的 Dispel depth 传播不是同一入口。

**测试缺口、建议与验收。** 现有用例覆盖 Dispel 或显式传 depth 的移除，没有覆盖 `absorb_depleted` 与真实 proc 配置组合。应从吸收结算传递触发深度，并让 AuraRemoved 事件参与统一深度/去重限制；验收循环在有限 pass 后停止，保留诊断而不持续增长队列。证据等级：静态，`NOT_EXECUTED`。

## C04（P1）Encounter/Achievement 忽略发奖失败

**触发与实际行为。** 触发前提是有限背包已满，奖励包含新物品、经验和货币；`RewardDispatcher.Grant` 因物品加入失败返回 `false`。Encounter 仍先结束并发布 Won，Achievement 仍先写入 unlocked 后发布解锁，玩家没有补领入口，且 RewardDispatcher 已跳过后续类别。

**原因链与影响。** Encounter 在 [EncounterHost.cs:248](D:/workespace/ws-game-review-7e63d66/core/gameplay/encounter/core/EncounterHost.cs:248) 先置 `IsActive=false`，在 [EncounterHost.cs:251](D:/workespace/ws-game-review-7e63d66/core/gameplay/encounter/core/EncounterHost.cs:251) 调用 Grant 却不检查 bool；Achievement 在 [AchievementHost.cs:341](D:/workespace/ws-game-review-7e63d66/core/gameplay/achievement/core/AchievementHost.cs:341) 先加入 `_unlocked`，在 [AchievementHost.cs:344](D:/workespace/ws-game-review-7e63d66/core/gameplay/achievement/core/AchievementHost.cs:344) 同样忽略失败。整批奖励可能永久丢失。

**测试缺口、建议与验收。** 相关测试 fake dispatcher 恒成功，未覆盖 `InventoryFullPolicy.Reject` 的满包输入。应在发奖成功后再提交终态，或持久化可重试的待领奖状态；验收 Reject 后领取权不会丢失，清理空间后整批奖励恰好发放一次；若采用待领奖终态，须验证其持久化和重复领取保护。证据等级：静态，`NOT_EXECUTED`。

## C05（P1）Partial 奖励回滚删除原有物品

**触发与实际行为。** 真实复现使用 `InventoryHost(MaxSlots=1, FullPolicy=Partial)`，已有 A5、堆叠上限 10，奖励为 `[A10,B1]`。A 实际只增加 5，B 加入失败；`Grant=false`，但最终 A 从 5 变成 0。证据见 [CORE-B 输出](evidence/core-repros-final-run.stdout.log) 与 [复现源码](repro/Program.cs)，标签为 `REPRODUCED`。

**原因链与影响。** `InventoryHost` 的 Partial 路径可以少量加入仍返回成功（[InventoryHost.cs:112](D:/workespace/ws-game-review-7e63d66/core/carriers/item/core/InventoryHost.cs:112)）；`RewardDispatcher` 记录的是请求 stack.Count（[RewardDispatcher.cs:95](D:/workespace/ws-game-review-7e63d66/core/gameplay/common/core/RewardDispatcher.cs:95)），失败时按请求量回滚（[RewardDispatcher.cs:102](D:/workespace/ws-game-review-7e63d66/core/gameplay/common/core/RewardDispatcher.cs:102)），并按模板从旧背包移除（[RewardDispatcher.cs:116](D:/workespace/ws-game-review-7e63d66/core/gameplay/common/core/RewardDispatcher.cs:116)）。因此补偿删除越过本次实际 delta。

**测试缺口、建议与验收。** 现有 common fake 只模拟一模板一堆叠，未模拟 Partial 分摊。应记录每次实际新增量并按实例回滚，或将 Partial 结果改为带实际数量的契约；验收本例失败后 A 仍为 A5，Reject 仍保持整批原子失败。证据等级：真实 Core console `REPRODUCED`。

补充静态旁证：Reject 补偿删除时，已入队的 ItemAdded 事件没有同步撤销，后续 Quest 消费可能把旧物品当作本批新增；本轮未运行该组合，不能标记为单独 Runtime 复现。

## C06（P1）任务部分扣除失败不回滚也不计实际进度

**触发与实际行为。** 路径一：按顺序接取 consume A1 与 consume A2 两任务，一次新增 A2；前者只能扣 1 并计 1，后者再扣 1 后返回 false，已消耗 2 但只记 1。路径二：一个 non-consume 任务有两个 A3 目标，库存 A4；第一目标扣 3，第二目标只扣 1 失败，仅补回第一项，最终 A3，TurnIn=false 仍丢 1。

**原因链与影响。** `RemoveCollectedItems` 边遍历边修改，最终只用 `remaining<=0` 返回整体 bool（[QuestHost.cs:577](D:/workespace/ws-game-review-7e63d66/core/gameplay/quest/core/QuestHost.cs:577)、[QuestHost.cs:590](D:/workespace/ws-game-review-7e63d66/core/gameplay/quest/core/QuestHost.cs:590)、[QuestHost.cs:597](D:/workespace/ws-game-review-7e63d66/core/gameplay/quest/core/QuestHost.cs:597)）；路径一是顺序执行 consume A1 任务和 consume A2 任务，一次 AddItem A2，前者只能扣 1 并计 1，后者再扣 1 后返回 false；consume 只有整体成功才更新进度（[QuestHost.cs:729](D:/workespace/ws-game-review-7e63d66/core/gameplay/quest/core/QuestHost.cs:729)），TurnIn 只回滚先前完整目标，未记录当前目标的部分移除。

**测试缺口、建议与验收。** 既有跨堆叠用例总量足够，未覆盖部分不足和同模板重复目标。应先规划实例级扣除事务，失败时恢复本批全部 delta，或明确按实际消费更新进度；验收上述两路径中失败不丢物品，consume 进度等于实际消费量。证据等级：静态，`NOT_EXECUTED`。

## C07（P1）VFX/SFX 超时没有完成 wait_for_playback 链

**触发与实际行为。** 默认 `FirstLoadTimeoutSeconds=5` 的冷 VFX 在 `wait_for_playback` 队列中等待资源；超过 5 秒，pending 项被删，但未通知 Binder 重新读取状态，节奏门可能永久关闭。SFX 没有 Update API，只在下一次 Play 开始时惰性扫描；若门已关闭且没有后续 Play，永不返回的加载请求不会被扫。

**原因链与影响。** VFX 超时分支只从 `_pendingSpawns` 移除并记录 warning（[VfxPlayer.cs:329](D:/workespace/ws-game-review-7e63d66/presentation/vfx_sfx/core/VfxPlayer.cs:329)、[VfxPlayer.cs:335](D:/workespace/ws-game-review-7e63d66/presentation/vfx_sfx/core/VfxPlayer.cs:335)），未触发 `PendingSpawnCountChanged`；SFX 的 `SweepTimedOutPendingPlays` 只在 `Play` 开头调用（[SfxPlayer.cs:97](D:/workespace/ws-game-review-7e63d66/presentation/vfx_sfx/core/SfxPlayer.cs:97)、[SfxPlayer.cs:99](D:/workespace/ws-game-review-7e63d66/presentation/vfx_sfx/core/SfxPlayer.cs:99)）。Binder 已正确把 sink pending 纳入状态，但超时完成事件链没有接上。

**测试缺口、建议与验收。** VFX 测试只断言 pending 清零，Binder 测试手动置 sink pending=false 并发事件，未覆盖真实 timeout→Composite→Binder→WaitForPlayback。应让超时也发 pending-change，或由统一时钟驱动 sink 清理；SFX 需有不会依赖下一次 Play 的时钟入口。验收冷 VFX/SFX 超时后门能收敛并恰好发布一次完成事件。证据等级：静态，`NOT_EXECUTED`。

## C08（P2）Equipment Replace 后按旧句柄撤销

**触发与实际行为。** 在 `AllowMultiSourceTiming=false`、maxStacks=1、StackOverflowPolicy.Replace 下，装备 A 授予 aura 得到 h1；装备 B 的 Replace 策略删除 h1 并创建 h2。A 仍记录 h1，B 记录 h2；卸 B 删除 h2，A 仍装备却没有应有光环。

**原因链与影响。** 卸装按保存的句柄计数并撤销（[EquipmentHost.cs:474](D:/workespace/ws-game-review-7e63d66/core/carriers/item/core/EquipmentHost.cs:474)、[EquipmentHost.cs:485](D:/workespace/ws-game-review-7e63d66/core/carriers/item/core/EquipmentHost.cs:485)），而 Replace 会先删旧实例再建立新句柄（[AuraHost.cs:193](D:/workespace/ws-game-review-7e63d66/core/rules/skill/core/AuraHost.cs:193)）。共享同句柄的普通多来源修复不能覆盖换句柄场景。

**测试缺口、建议与验收。** 现有测试覆盖共享句柄计数，没有覆盖 Replace 后的来源重绑定。应让装备来源记录随 Replace 原子更新，或在卸装时按当前来源重建撤销句柄；验收 A/B 任一卸装后仍保留另一件装备应有 aura，全部卸装后才清空。证据等级：静态，`NOT_EXECUTED`。

## C09（P2）KnownSkills 读档只增不减

**触发与实际行为。** 同一长期 `SkillHost` 先保存只有永久技能 A 的快照，之后学习永久技能 B，再读取旧快照；正确替换结果应只剩 A，实际 B 仍留在当前集合。

**原因链与影响。** `KnownSkillsPersistable.Load` 只遍历数组并调用 `LearnSkill`（[KnownSkillsPersistable.cs:82](D:/workespace/ws-game-review-7e63d66/core/rules/skill/core/KnownSkillsPersistable.cs:82)、[KnownSkillsPersistable.cs:89](D:/workespace/ws-game-review-7e63d66/core/rules/skill/core/KnownSkillsPersistable.cs:89)），不先替换或清理当前永久集合；默认 `GameplayAssembly.RestoreFromSlot` 复用当前 host（[GameplayAssembly.cs:841](D:/workespace/ws-game-review-7e63d66/core/gameplay/assembly/GameplayAssembly.cs:841)）。它不同于已修复的装备临时来源持久化问题。

**测试缺口、建议与验收。** 现有测试多在新 host 上 Load，未覆盖同 host 回档。应提供持久集合替换语义并保留运行期临时来源的独立生命周期；验收 A 快照覆盖当前 A+B 后只剩 A，之后卸装备不会误清永久 A。证据等级：静态，`NOT_EXECUTED`。

## C10（P2）meta 语义损坏在候选选择后才暴露

**触发与实际行为。** 当前 v1 formal 为 {"save_version":1,"sections":{"meta":{}}}，顶层结构筛选通过，另有健康 backup。正确结果应回退健康 backup；实际先选 formal，随后 `Load` 在 `ParseMeta` 因 meta.save_version、created_at 等必需语义字段缺失而返回 `Corrupted`，不再尝试 backup；meta.slot_id 可由请求槽名补足，并非本例前提。

**原因链与影响。** `TryParseEnvelope` 只检查可解析的整数 save_version 和 sections/meta 对象形状（[SaveSystem.cs:818](D:/workespace/ws-game-review-7e63d66/core/foundation/save_system/core/SaveSystem.cs:818)、[SaveSystem.cs:825](D:/workespace/ws-game-review-7e63d66/core/foundation/save_system/core/SaveSystem.cs:825)）；`ReadValidEnvelope` 因而选中 formal（[SaveSystem.cs:389](D:/workespace/ws-game-review-7e63d66/core/foundation/save_system/core/SaveSystem.cs:389)），真正读取 meta 时才在 [SaveSystem.cs:296](D:/workespace/ws-game-review-7e63d66/core/foundation/save_system/core/SaveSystem.cs:296) 失败并于 [SaveSystem.cs:300](D:/workespace/ws-game-review-7e63d66/core/foundation/save_system/core/SaveSystem.cs:300) 返回 `Corrupted`。

**测试缺口、建议与验收。** 已有防护覆盖顶层非整数版本和空 sections，不覆盖 meta 语义必填字段。应在候选筛选阶段使用与 Load 一致的可迁移 meta 校验，或失败后继续候选；验收缺 meta.save_version 或 created_at 等必填字段的 formal 会读取健康 backup，并保持错误诊断。证据等级：静态，`NOT_EXECUTED`。

## C11（P2）Shell 同图读档清除已保存刷新倒计时

**触发与实际行为。** 怪物死亡后剩余刷新时间为 15 秒时存档；等待其在当前世界复活，再经 `ShellHost.LoadGame` 读同图旧档。正确应恢复保存的倒计时策略；实际当前存活实体被重绑时把 `RespawnRemaining` 置空，随后同图卸载/重载可立即重生。

**原因链与影响。** `SpawnHost.Load` 先读取保存 timer（[SpawnHost.cs:467](D:/workespace/ws-game-review-7e63d66/core/gameplay/spawn/core/SpawnHost.cs:467)），重绑 previously-alive entity 时又无条件清空（[SpawnHost.cs:477](D:/workespace/ws-game-review-7e63d66/core/gameplay/spawn/core/SpawnHost.cs:477)、[SpawnHost.cs:481](D:/workespace/ws-game-review-7e63d66/core/gameplay/spawn/core/SpawnHost.cs:481)）；`ShellHost.LoadGame` 在 [ShellHost.cs:157](D:/workespace/ws-game-review-7e63d66/presentation/shell/core/ShellHost.cs:157) 随后无条件 LoadScene，进图恢复逻辑看不到正 timer。

**测试缺口、建议与验收。** Spawn 测试只在 Save 后立即 Load，没有经过 Shell 同图生命周期。应定义保存 timer 与当前存活实体映射冲突时的优先级并在场景重载中保持；验收同图 Shell.Load 后 15 秒计时仍有效，不提前刷新。证据等级：静态，`NOT_EXECUTED`。

## C12（P2）reload_save 同图恢复未清理 Death 动画终态

**触发与实际行为。** 玩家死亡后采用 `reload_save`，读档直接恢复 `Alive` 和生命；同图恢复不重载场景，也不发布 `UnitRespawnedEvent`。动画状态机已经由 `UnitDied` 进入 Death，后续移动或攻击仍被终态规则拒绝，表现停在 Death。若补发清理事件，必须在原 UnitDied 的派发完成后执行，否则原事件仍可能覆盖恢复状态。

**原因链与影响。** `DeathPolicyHost` 的成功 reload_save 分支只调用 Load（[DeathPolicyHost.cs:99](D:/workespace/ws-game-review-7e63d66/core/gameplay/death/core/DeathPolicyHost.cs:99)、[DeathPolicyHost.cs:104](D:/workespace/ws-game-review-7e63d66/core/gameplay/death/core/DeathPolicyHost.cs:104)），`PlayerVitalsPersistable` 直接恢复 Alive/health（[PlayerVitalsPersistable.cs:111](D:/workespace/ws-game-review-7e63d66/core/gameplay/assembly/PlayerVitalsPersistable.cs:111)、[PlayerVitalsPersistable.cs:120](D:/workespace/ws-game-review-7e63d66/core/gameplay/assembly/PlayerVitalsPersistable.cs:120)），同图分支不重载场景（[GameplayAssembly.cs:854](D:/workespace/ws-game-review-7e63d66/core/gameplay/assembly/GameplayAssembly.cs:854)）。`AnimStateMachine` 对 UnitDied 进入 Death（[AnimStateMachine.cs:114](D:/workespace/ws-game-review-7e63d66/presentation/render/core/AnimStateMachine.cs:114)），而 UnityViewFactory 仅用 EntityDestroyed/UnitRespawned 清理（[UnityViewFactory.cs:131](D:/workespace/ws-game-review-7e63d66/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:131)、[UnityViewFactory.cs:157](D:/workespace/ws-game-review-7e63d66/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:157)）。

**测试缺口、建议与验收。** 死亡测试只断言生命/位置，UnityViewFactory 测试手动发布 UnitRespawned，没有真实 reload_save 同图链。应让成功 reload_save 发布统一复活/动画清理事件，或由 Restore 完成回调清理状态；验收同图读档后状态机允许 Move、Attack，respawn_point 的既有路径继续保持。证据等级：静态，`NOT_EXECUTED`。

## 当前证据边界

只有 C01、C05 使用真实 Core 类的 console 运行并标记 `REPRODUCED`；C02–C04、C06–C12 均为静态核验并标记 `NOT_EXECUTED`。所有旧 68c9bed 案例中已修的货币累加、LevelUp 顺序、显式目标 tag/expr、旧 Dispel 循环、临时技能持久化、旧跨堆叠窄例均不属于本轮当前缺陷。
