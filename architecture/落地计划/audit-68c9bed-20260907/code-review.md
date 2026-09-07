# 逐条代码审查

固定基线为 `68c9bedaeed3485ee845f68c95136f87476caf03`。路径均指向本审计工作树。证据等级分为静态、有界 .NET 复现和组件链复现；有界复现不等同 Unity/完整 Runtime 通过。共 19 条：P1 7 条（N01–N06、N17），P2 12 条（N07–N16、N18–N19）。

## P1

### N01 — 货币读档累加（P1）

`CurrencyPersistable.Load` 把快照值加到当前值而非替换。触发序列是保存 100、运行中变 80、Load 得 180、再 Load 得 280。源码：[CurrencyPersistable.cs:76](D:/workespace/ws-game-review-68c9bed/core/gameplay/economy/core/CurrencyPersistable.cs:76)、接线：[GameplayAssembly.cs:1116](D:/workespace/ws-game-review-68c9bed/core/gameplay/assembly/GameplayAssembly.cs:1116)。影响是跨槽/重复读档复制货币。修复应按快照替换并保证幂等；验收 100→80→Load=100，连续 Load 仍为 100。证据：静态。

### N02 — 满包奖励被吞且任务已完成（P1）

触发前提：背包容量有限，`InventoryFullPolicy.Reject`，且剩余容量不足以接收奖励。奖励发放调用 `AddItem` 后忽略失败，任务仍可置为 `TurnedIn`。源码：[RewardDispatcher.cs:77](D:/workespace/ws-game-review-68c9bed/core/gameplay/common/core/RewardDispatcher.cs:77)、[QuestHost.cs:223](D:/workespace/ws-game-review-68c9bed/core/gameplay/quest/core/QuestHost.cs:223)。满包时物品奖励丢失且玩家不能再次领取。修复应让交付结果参与提交，或保留待领取奖励；验收满包不丢奖励且状态可恢复。证据：静态。

### N03 — 同图读档未重建 Spawn 映射（P1）

同图 Restore 后 `SpawnHost.Load` 清空 `entityToSpawn` 而不恢复现有实体映射；原怪之后 `NotifyDespawn` 查不到映射。源码：[GameplayAssembly.cs:846](D:/workespace/ws-game-review-68c9bed/core/gameplay/assembly/GameplayAssembly.cs:846)、[SpawnHost.cs:419](D:/workespace/ws-game-review-68c9bed/core/gameplay/spawn/core/SpawnHost.cs:419)、[SpawnHost.cs:188](D:/workespace/ws-game-review-68c9bed/core/gameplay/spawn/core/SpawnHost.cs:188)。影响是读档后实体销毁/刷新/重挂失去一致性。应明确同图重绑或重建协议；验收 Restore 后旧实体可正确销毁、刷新且不重复生成。证据：静态。

### N04 — AuraRemoved Proc 循环缺少链深度预算（P1）

永久 Proc P 订阅 `aura.removed`，`procChance=1` 且无 ICD；S 先 Apply 临时 A 再 Dispel A，P 不属于 A 类别，于是 P 再处理移除事件。`AuraRemoved` 不实现 `ITriggerChainEvent`，`EventCorrelation` 取深度 0，绕过 `MaxTriggerDepth`。源码：[AuraHost.cs:266](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/AuraHost.cs:266)、[EventCorrelation.cs:62](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/EventCorrelation.cs:62)。应让 AuraRemoved 携带链身份/深度预算并在上限拒绝自循环；旧 `heal_done` 循环已经修复，不计为当前问题。证据：静态。

### N05 — 来源销毁后 DOT 访问已注销等级（P1）

来源 Despawn 后 DOT 光环保留；带 resist curve 时 [combat/Resolver.cs:380](D:/workespace/ws-game-review-68c9bed/core/rules/combat/core/Resolver.cs:380) 仍 `GetLevel(sourceId)`，周期 tick 可访问不存在来源。源码另见 [AuraHost.cs:118](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/AuraHost.cs:118)、[EffectDispatcher.cs:166](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/EffectDispatcher.cs:166)。影响是周期效果异常中断。应按定义移除/冻结剩余效果并让注销来源读取安全；验收 Despawn 后周期 tick 无异常且剩余效果策略明确。证据：静态。

### N06 — PlayMode 全局测试删除真实存档（P1）

全局 setup 使用 `Application.persistentDataPath/saves` 并递归 `File.Delete`；生产 UnityFileSystem 指向同一目录。源码：[GlobalPlayModeTestSetup.cs:84](D:/workespace/ws-game-review-68c9bed/adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/GlobalPlayModeTestSetup.cs:84)、[GlobalPlayModeTestSetup.cs:112](D:/workespace/ws-game-review-68c9bed/adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/GlobalPlayModeTestSetup.cs:112)、[UnityFileSystem.cs:64](D:/workespace/ws-game-review-68c9bed/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityFileSystem.cs:64)。影响是测试可删除手工进度与备份。应注入测试专属目录并启动时断言隔离；验收测试不访问真实玩家目录。证据：静态，本轮未启动 Unity。

### N17 — 冷 SFX pending 完成信号缺口（P1）

`FeedbackBinder.HasPendingPlayback` 已包含 sink，但 `PlaybackQueue.Finished` 只看 merger。Sequential 队列执行 `PlaySfx` 后 sink 仍 pending，仍发布 `PlaybackFinished`，门提前打开；Immediate 是显式 override，清 pending 后没有事件解门，调用方必须自行接管节奏。源码：[FeedbackBinder.cs:85](D:/workespace/ws-game-review-68c9bed/presentation/feedback_binder/core/FeedbackBinder.cs:85)、[PlaybackQueue.cs:80](D:/workespace/ws-game-review-68c9bed/presentation/feedback_binder/core/PlaybackQueue.cs:80)、[WaitForPlaybackPacingPolicy.cs:73](D:/workespace/ws-game-review-68c9bed/core/foundation/sim_loop/core/WaitForPlaybackPacingPolicy.cs:73)；默认 host 转发：[GameBootstrap.cs:274](D:/workespace/ws-game-review-68c9bed/games/_template/Runtime/GameBootstrap.cs:274)。复现器：[Program.cs](D:/workespace/ws-game-review-68c9bed/architecture/落地计划/audit-68c9bed-20260907/repro/Program.cs)，日志：[repro.log](D:/workespace/ws-game-review-68c9bed/architecture/落地计划/audit-68c9bed-20260907/repro/repro.log)。实际 Sequential `queue=0,sinkPending=True,gateOpen=True`；Immediate 清 pending 后 `gateOpen=False`，均 `REPRODUCED`。应将 sink 完成纳入有序 completion 协议；验收 cold SFX 未完成时后续动作不先播，完成后门只解一次。证据：有界 .NET 复现，不等同 Unity 资源加载器。

## P2

### N07 — KnownSkills 快照丢失来源（P2）

运行期来源集合已有区分，但存档只写技能全集 ID；Load 以无来源方式重学，装备 E 的临时技能因此变成永久技能，卸 E 后仍 `Knows`。源码：[KnownSkillsPersistable.cs:55](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/KnownSkillsPersistable.cs:55)、[KnownSkillsPersistable.cs:85](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/KnownSkillsPersistable.cs:85)、[SkillHost.cs:174](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/SkillHost.cs:174)。应持久化来源或只持久化永久授予并由装备快照重建临时来源；验收卸装不遗留临时技能。证据：静态。

### N08 — LevelUp 发布早于等级赋值（P2）

`ProgressionHost` 发布 LevelUp 前仍未提交等级；事件虽携 `NewLevel`，RulesAssembly 重算消费仍可能读取旧 Progression，普通 Get 保持旧缓存，只有后续相关属性写入才可能补救。源码：[ProgressionHost.cs:220](D:/workespace/ws-game-review-68c9bed/core/numbers/progression/core/ProgressionHost.cs:220)、[RulesAssembly.cs:227](D:/workespace/ws-game-review-68c9bed/core/rules/assembly/RulesAssembly.cs:227)。应先提交再发布，或让重算消费事件新值；验收不触碰 rating 的升级也立即得到新派生值。证据：静态。

### N09 — 多来源装备 Aura 卸载残留（P2）

`AllowMultiSourceTiming=true` 时每件装备独立 Apply 永久 aura；移除时发现其它引用便跳过，最后只删一个 handle，全部装备卸载后仍留首个临时 aura。源码：[EquipmentHost.cs:425](D:/workespace/ws-game-review-68c9bed/core/carriers/item/core/EquipmentHost.cs:425)、[EquipmentHost.cs:458](D:/workespace/ws-game-review-68c9bed/core/carriers/item/core/EquipmentHost.cs:458)。应按来源计数/句柄集合精确移除并保留真正其它来源；验收全部装备卸载后零临时 aura，永久来源仍保留。证据：静态。

### N10 — 显式 targets 绕过额外 target 条件（P2）

显式 targets 仍可绕过额外 tag/expr 条件：配置要求 `undead` 时，非 undead 的显式敌对单体 target 仍可成功。AI 自疗/友疗/AOE 分类链已修复，不在本条重复。源码：[CastPipeline.cs:216](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/CastPipeline.cs:216)、[AiHost.cs:216](D:/workespace/ws-game-review-68c9bed/core/rules/ai/core/AiHost.cs:216)。应继续执行技能配置条件，复活等例外按技能定义；验收非 undead 显式目标不能通过 undead 条件。证据：静态。

### N11 — 同物品多任务消耗依据缓存（P2）

确定触发序列：两个任务各需 3 件同种物品，库存只有 3 件；在 `item.removed` 事件派发前连续执行两次 `TurnIn`（例如同一次 gossip 执行两个交任务动作）。交付流程依据缓存完成并忽略 `RemoveCollectedItems` 的实际结果。源码：[QuestHost.cs:206](D:/workespace/ws-game-review-68c9bed/core/gameplay/quest/core/QuestHost.cs:206)、[QuestHost.cs:530](D:/workespace/ws-game-review-68c9bed/core/gameplay/quest/core/QuestHost.cs:530)。应按实际移除量提交并保证失败无奖励；验收总扣除量不超过库存。证据：静态。

### N12 — 跨堆叠事件只带最后实例（P2）

`InventoryHost` 跨堆叠合并为 `lastInstanceId + totalCount`，Quest 按单实例扣总量；stack1 一次加 2 时 consume2 进度为 0。源码：[InventoryHost.cs:146](D:/workespace/ws-game-review-68c9bed/core/carriers/item/core/InventoryHost.cs:146)、[QuestHost.cs:654](D:/workespace/ws-game-review-68c9bed/core/gameplay/quest/core/QuestHost.cs:654)。应携带实际扣除分解或提供批量消费 API；验收跨堆叠收集正确增长。证据：静态。

### N13 — Level 重开未终止旧 encounter（P2）

重开 Level 只删订阅，不 Abort 旧 encounter；两次 Start 留下两套活跃实例，之后可双奖励。源码：[LevelHost.cs:61](D:/workespace/ws-game-review-68c9bed/core/gameplay/encounter/core/LevelHost.cs:61)、[LevelHost.cs:69](D:/workespace/ws-game-review-68c9bed/core/gameplay/encounter/core/LevelHost.cs:69)。应 Start 前幂等终止旧实例并清理 tick；验收重复 Start 只有一套活跃运行。证据：静态。

### N14 — 跨图 teleport 只改 MapId（P2）

gossip teleport 只改 `MapId`，没有调用 SceneRouter 或重建世界资源。源码：[GameplayAssembly.cs:676](D:/workespace/ws-game-review-68c9bed/core/gameplay/assembly/GameplayAssembly.cs:676)、[GameplayAssembly.cs:1156](D:/workespace/ws-game-review-68c9bed/core/gameplay/assembly/GameplayAssembly.cs:1156)。影响是跨图后仍可见旧图怪物/资源。应走统一导航和 Leave/Enter 生命周期；验收地图、Spawn、Encounter 和导航上下文一致。证据：静态。

### N15 — SaveSystem 信封浅校验阻挡备份（P2）

`TryParseEnvelope` 的字段类型判断在 [SaveSystem.cs:791-792](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/SaveSystem.cs:791)；正式候选在 [ReadValidEnvelope:389-391](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/SaveSystem.cs:389) 被选中。顶层版本非整数在 [Load:260](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/SaveSystem.cs:260) 返回 Corrupted，meta 缺失在 [Load:288](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/SaveSystem.cs:288) 返回 Corrupted，均不继续尝试有效 bak1。A 使用真实 SaveSystem+StubFileSystem：[Program.cs](D:/workespace/ws-game-review-68c9bed/architecture/落地计划/audit-68c9bed-20260907/repro/Program.cs)，实际两次 Save 成功创建 bak1；正式 {"save_version":1,"sections":{}} 与顶层 1.5+完整 sections 均 Corrupted，期望 LoadedFromBackup，REPRODUCED。应完整校验候选或失败后继续回退；不等同 Unity 文件系统。

### N16 — 新 backup 目录不兼容旧布局（P2）

当前备份移至 `saves/backups/*.bakN.json`，没有旧顶层 `slot.a.bak1.json` 的读取/迁移；坏正式档无法从旧备份恢复，`ListSlots` 还会把旧 backup 当正式槽并计入 quota。路径选择见 [SaveSystem.cs:403](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/SaveSystem.cs:403)，列举/过滤见 [SaveSystem.cs:892](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/SaveSystem.cs:892)，当前选项见 [SaveSystemOptions.cs](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/contracts/SaveSystemOptions.cs)。应提供兼容迁移并验证碰撞隔离。证据：静态，本轮没有旧布局实际复现。

### N18 — UnityFrameAnimPlayer 热替换不刷新当前播放对象（P2）

`UnityFrameAnimPlayer.RegisterClip` 只替换字典，`FrameAnimPlayer.Play` 缓存当前 clip；虽有 [UnityViewFactory.cs:391](D:/workespace/ws-game-review-68c9bed/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:391) 的冷 clip 原地升级调用，正在播放的旧对象仍不刷新。单帧 loop 播放中注册同 ID 的八帧 12fps，当前仍停旧对象，手工重新 Play 才使用新帧。源码：[UnityFrameAnimPlayer.cs:93](D:/workespace/ws-game-review-68c9bed/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityFrameAnimPlayer.cs:93)、[FrameAnimPlayer.cs:56](D:/workespace/ws-game-review-68c9bed/presentation/render/core/FrameAnimPlayer.cs:56)。应明确刷新、版本化拒绝或显式重播策略；验收冷加载不永久停留 fallback。证据：静态组件链分析，未运行 Unity。

### N19 — 瞬发 Start→Success 同 dispatch 立即回 Idle（P2）

`CastPipeline` 在 [274](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/CastPipeline.cs:274) Start 后于 [282](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/CastPipeline.cs:282) Success；[AnimStateMachine.cs:105](D:/workespace/ws-game-review-68c9bed/presentation/render/core/AnimStateMachine.cs:105) 与 [AnimStateMachine.cs:197-202](D:/workespace/ws-game-review-68c9bed/presentation/render/core/AnimStateMachine.cs:197) 使 Attack 可能在下一帧前回落。默认 Attack clip 规则见 [AnimClipResolver.cs:142](D:/workespace/ws-game-review-68c9bed/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/AnimClipResolver.cs:142)，文档要求逻辑与动画时长独立见 [09_表现层.md:193](D:/workespace/ws-game-review-68c9bed/architecture/09_表现层.md:193)。应分离逻辑 Success 和表现瞬态完成；验收瞬发仍展示完整 Attack clip。证据：静态；不将 `NotifyTransientStateFinished` 接线等同全部动画链完成。

## 证据边界

N15 两个正式档输入、N17 两种队列模式共四个场景已运行，均 `REPRODUCED`；其余问题保持静态。基线自动测试通过不改变上述问题状态。
