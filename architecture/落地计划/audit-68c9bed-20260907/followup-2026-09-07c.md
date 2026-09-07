# 第五方深度审核（codex 第三轮，基于 68c9bed）核实跟进

基线 `main` = `0146844`；本轮外部深度审核基于 `68c9bed`（`codex/deep-review-68c9bed-20260907`，只读
工作树 `D:\workespace\ws-game-review-68c9bed`，报告见本目录 [code-review.md](code-review.md)、
[doc-code-matrix.md](doc-code-matrix.md)、[previous-findings.md](previous-findings.md)、
[validation.md](validation.md)）；核实与修复提交为 `b0d43b5`（核心侧：`core/foundation`、
`core/numbers`、`core/rules`、`core/carriers`）与 `88ca6d3`（玩法/表现侧：`core/gameplay`、
`presentation`、`adapters/unity`）。本文档只做核实结果的汇总归档，不重新展开调查——逐条证据、
修改点、判断依据以对应提交的代码/测试/README 为准。

## 19 条核实表

| 编号 | 成立/部分成立/不成立 | 复现测试名 | 修复位置 | 验收测试名 |
|---|---|---|---|---|
| N01 | 成立 | `EconomyHostTests.CurrencyPersistable_Load_ReplacesRatherThanAdds_AndIsIdempotent` | `core/gameplay/economy/core/EconomyHost.cs`（新增 `SetBalance`，替换语义）、`core/gameplay/economy/contracts/IEconomyHost.cs`、`core/gameplay/economy/core/CurrencyPersistable.cs`（`Load` 改用 `SetBalance`，快照未出现的货币显式置零） | 同左 + `CurrencyPersistable_Load_ZeroesCurrenciesNotPresentInSnapshot` |
| N02 | 成立 | `RewardDispatcherTests.Grant_ItemAddFails_ReturnsFalse_AndSkipsOtherRewardCategories`、`Grant_SecondItemFails_RollsBackFirstItemAlreadyGranted` | `core/gameplay/common/contracts/IRewardDispatcher.cs`（`Grant` 由 void 改 bool）、`core/gameplay/common/core/RewardDispatcher.cs`（`GrantItems` 原子发放+失败回滚）、`core/gameplay/quest/contracts/QuestEnums.cs`（新增 `QuestTurnInFailure`）、`core/gameplay/quest/contracts/IQuestHost.cs`、`core/gameplay/quest/core/QuestHost.cs`（`TurnIn` 三步原子化：预检→实际移除→发放奖励，任一步失败整体回滚不置 `TurnedIn`） | `QuestHostTests.TurnIn_RewardGrantFails_RollsBackConsumedItems_KeepsObjectivesCompleteState` |
| N03 | 成立 | `SpawnPersistableTests.SameHost_SaveThenLoadWhileEntityStillAlive_...`（同图存活期间 Save 后 Load，验证映射不丢） | `core/gameplay/spawn/core/SpawnHost.cs`（`Load` 改为先记录当前存活实体映射，按快照重建 `_records` 后接回该映射，不再无条件清空 `_entityToSpawn`） | 同左 |
| N04 | 成立 | `ProcTests.TriggerChain_ViaAuraRemoved_ApplyThenDispelLoop_IsBoundedByMaxTriggerDepth`（修复前 `castCount` 顶到远超 `MaxTriggerDepth+2`） | `core/rules/common/contracts/Events.cs`（`AuraRemovedEvent`/`AuraStackChangedEvent` 实现 `ITriggerChainEvent`）、`core/rules/skill/core/AuraHost.cs`（`RemoveInstanceInternal`/`RemoveAura`/`Dispel`/`ReapplyExisting` 透传 `triggerChainDepth`）、`core/rules/skill/core/EffectDispatcher.cs`（`ApplyDispel` 传入 `context.TriggerChainDepth`） | 同左，已转绿 |
| N05 | 成立 | `MitigationSourceDespawnTests.Resolve_SourceDespawned_DoesNotThrow_MitigationDegradesToTargetLevel`（修复前直接抛 `InvalidOperationException`） | `core/rules/combat/core/Resolver.cs`（`ComputeMitigation`/`ResolveAttackerLevelForMitigation`：来源不存在时降级为目标等级，目标也不存在再降级为 1，不抛异常） | 同左 + `Resolve_SourceDespawned_TargetAlsoMissing_DoesNotThrow` |
| N06 | 成立 | `UnityFileSystemTests.GetUserDataDir_WithoutOverride_ReturnsRealPersistentDataPath`（EditMode） | `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityFileSystem.cs`（新增静态 `UserDataRootOverride`）、`Tests/Runtime/GlobalPlayModeTestSetup.cs`（`[OneTimeSetUp]` 改为把覆盖设为 `<persistentDataPath>/_playmode_tests/` 专属子目录，不再触碰真实 `saves/`，`[OneTimeTearDown]` 删除该子目录并复位） | `GetUserDataDir_WithOverride_RedirectsAwayFromRealPersistentDataPath_ForAllInstances`（EditMode）+ `GlobalPlayModeTestSetupTests.TestUserDataRoot_IsSubdirectoryOfRealPersistentDataPath_NotTheDirectoryItself`、`UserDataRootOverride_AlreadyRedirectedByAssemblyLevelOneTimeSetUp`（PlayMode） |
| N07 | 成立 | `KnownSkillsPersistableTests.EquipmentGrantedSkill_AfterSaveLoad_IsNotPermanent_ForgottenOnUnequip`（修复前 Load 后装备技能被误判永久） | `core/rules/skill/core/SkillHost.cs`（新增 `GetPermanentlyKnownSkills`）、`core/rules/skill/core/KnownSkillsPersistable.cs`（`Save` 改用它，只持久化永久授予） | 同左 + `Save_ExcludesSkillsGrantedOnlyByNonPermanentSource`、`Save_IncludesSkillGrantedByBothPermanentAndEquipmentSource` |
| N08 | 成立 | `ProgressionHostTests.AddXp_LevelUpHandler_ReadsGetLevel_SeesNewLevelAlready`（修复前处理器内 `GetLevel` 读到旧等级） | `core/numbers/progression/core/ProgressionHost.cs`（`AddXp` 循环内先 `unit.Level = newLevel` 再 `PublishImmediate`，事件顺序改为先提交后发布） | 同左 + `AddXp_CrossesTwoLevels_EachHandlerInvocation_SeesLevelAtThatPointInTime` |
| N09 | 成立 | `EquipmentHostTests.Unequip_OneOfTwoItemsGrantingSameAura_IndependentInstances_EachRemovedOnItsOwnUnequip`（修复前卸第一件装备时 `Removed` 为空，残留一份实例） | `core/carriers/item/core/EquipmentHost.cs`（`_auraGrantRefCount`→`_auraHandleRefCount`，改按实际 `AuraInstanceRef.AuraInstanceId` 计数，不再按 `auraDefId`） | 同左，原共享槽位回归测试改用 `FakeEffectSink.SharedSlotPerAuraDef=true` 显式声明前提后仍通过 |
| N10 | 成立 | `CastPipelineFailureTests.ExplicitTarget_FailingChainFilter_Fails_NotBypassed`（修复前显式非 undead 目标直接施法成功） | `core/rules/common/contracts/ITargetHost.cs`（新增 `FilterExplicitTargets`）、`core/rules/targeting/core/TargetHost.cs`（实现）、`core/rules/skill/core/CastPipeline.cs`（步骤 6 显式 targets 分支改走该方法） | 同左，失败码复用 `NoValidTarget`（06 文档未定义专门原因码，理由见下"顺带核实但判断不改"） |
| N11 | 成立 | `QuestHostTests.TurnIn_TwoQuestsShareSameItem_SecondTurnInFailsWhenInventoryExhaustedByFirst` | `core/gameplay/quest/core/QuestHost.cs`（`RemoveCollectedItems` 改返回 bool，`TurnIn` 交付前改按 `IInventoryHost.CountOf` 实际库存核验，不依赖 `ObjectiveCounts` 缓存） | 同左 |
| N12 | 成立 | `InventoryHostTests.AddItem_CrossStack_RaisesItemAddedEvent_WithPerInstanceRemovalsBreakdown`（核心侧，旧组合 `RemoveItem(unit, evt.ItemInstanceId, evt.Count)` 跨堆叠场景会失败） | 核心侧：`core/carriers/common/contracts/Events.cs`（`ItemAddedEvent` 新增 `Removals: IReadOnlyList<(Id InstanceId, int Count)>`，旧字段保留）、`core/carriers/item/core/InventoryHost.cs`（`AddItem` 逐项记录分摊）；消费侧：`core/gameplay/quest/core/QuestHost.cs`（`HandleItemAdded` 的 consume-on-progress 分支改用既有 `RemoveCollectedItems` 跨堆叠遍历，未依赖新增的 `Removals` 字段） | 核心侧同左 + 消费侧 `QuestHostTests.Objective_Collect_ConsumeOnProgress_CrossStackAdd_AdvancesProgressAcrossBothInstances` |
| N13 | 成立 | `LevelHostTests.StartLevel_CalledTwice_AbortsPreviousEncounterInstance_OnlyOneActiveInstance` | `core/gameplay/encounter/core/LevelHost.cs`（`LevelRunState` 新增 `ActiveInstanceId`，`StartLevel` 重开时对旧运行调用 `_encounterHost.Abort(existing.ActiveInstanceId)`，此前只 Dispose 订阅） | 同左 |
| N14 | 成立 | `GameplayAssemblyTeleportTests.GossipTeleport_CrossMap_ClearsOldMapEntities_EntersNewMap_PositionsAtTargetSpawnPoint` | `core/gameplay/assembly/GameplayAssembly.cs`（`TeleportUnit` 改为解析目标 (map,position) 后判断是否跨图，跨图才调用 `_sceneRouter.LoadScene`，内部走 pre_unload→LeaveMap→ClearAll→post_load→EnterMap 统一导航）、`TeleportTargetResolver.cs`（新增 `ResolveExplicit(mapId, pointId?)`） | 同左 + `GossipTeleport_SameMap_OnlyMovesPosition_DoesNotTriggerSceneReload` |
| N15 | 成立 | `SaveSystemTests.Load_FormalFileHasEmptySections_TreatedAsInvalidEnvelope_FallsBackToBackup`、`Load_FormalFileHasNonIntegerTopLevelVersion_TreatedAsInvalidEnvelope_FallsBackToBackup`（对应审核 repro N15-A/B，修复前判 `Corrupted`） | `core/foundation/save_system/core/SaveSystem.cs`（`TryParseEnvelope` 改为直接复用 `TryGetInt`/`TryGetSectionsMeta`，不再手写一份更浅的平行判断） | 同左 |
| N16 | 部分成立 | `SaveSystemTests.Load_FormalCorrupted_NoNewLayoutBackup_LegacyTopLevelBackupExists_RecoversFromIt` | `core/foundation/save_system/core/SaveSystem.cs`（新增 `TryReadLegacyBackup`：只读、按精确路径探测旧顶层备份，成功后复制到新布局路径，不删除/移动旧文件） | 同左 + 2 个补充用例；"配额不计旧备份"子项未实现，判断记录见下 |
| N17 | 成立 | `FeedbackBinderTests.QueueMode_Sequential_ColdSfxPending_DoesNotFirePlaybackFinished_UntilSinkResolves` | `presentation/feedback_binder/contracts/IFeedbackSink.cs`（新增 `PendingPlaybackChanged`）、`core/CompositeFeedbackSink.cs`（汇聚转发）、`core/FeedbackBinder.cs`（`TryPublishFinished` 重构：队列空 && !merger pending && !sink pending 三者同时成立才发 `PlaybackFinishedEvent`）、`presentation/vfx_sfx/contracts/IVfxPlayer.cs`/`ISfxPlayer.cs`（新增 `PendingSpawnCountChanged`/`PendingPlayCountChanged`）、`core/VfxPlayer.cs`/`SfxPlayer.cs` | 同左 + `QueueMode_Immediate_ColdSfxPending_FiresPlaybackFinished_WhenSinkResolves` |
| N18 | 成立 | `FrameAnimPlayerTests.Update_ClipReRegisteredWithSameIdWhilePlaying_UsesNewClipWithoutReplay` | `presentation/render/core/FrameAnimPlayer.cs`（`Update` 改为每帧按当前 clipId 重新从 `_clips` 字典查最新版本，不再只在 `Play()` 缓存一次引用；剪辑对象引用变化时强制重新触发 `FrameChanged`） | 同左 + `Update_ClipReRegisteredWithSameIdAndSameFrameIndex_StillForcesFrameChanged` + `AnimationLayerTests.FrameAnimPlayer_ReRegisterSameClipIdWhilePlaying_SwitchesToNewSpritesWithoutReplay`（Unity PlayMode） |
| N19 | 成立 | 核心侧：`EventsTests.SkillCastSuccessEvent_CarriesIsInstantAndCastTimeSeconds`；表现侧：`AnimStateMachineTests.SkillCastStart_ZeroCastTime_EntersAttack_DoesNotRevertOnSuccess_OnlyOnNotifyTransientStateFinished`（原 `..._RevertsOnSuccess`，修复前瞬发同批 Start→Success 立即回 Idle） | 核心侧：`core/rules/common/contracts/Events.cs`（`SkillCastSuccessEvent` 新增 `IsInstant`/`CastTimeSeconds`）、`core/rules/skill/core/CastPipeline.cs`（两处构造点传入正确值，`CastState` 新增 `CastTimeSeconds`）；表现侧：`presentation/render/core/AnimStateMachine.cs`（`OnSkillCastEnd` 改为只有 `Cast` 状态由施法收尾事件驱动回落，`Attack`（瞬发）改由已有的 `NotifyTransientStateFinished` 独家驱动） | 核心侧同左 + `CastPipelineFlowTests`（`InstantCast_.../QueueWindow_...` 补充断言）；表现侧同左 + `LocomotionUpdate_DuringTransientState_IsDeferred_AppliesAfterRevert` |

## N16 部分成立的判断记录（摘自 WA.md 判断记录第 4 条）

最初按"扫描存档目录顶层、按 `<x>.bakN.json` 文件名模式批量迁移/删除"实现，跑既有测试时在
`SaveSystemTests.SlotIdLooksLikeBackupFileName_IsIndependentFromRealBackup_ListedLoadedAndDeletedCorrectly`
（FND-01 收口回归测试）上失败——该测试断言"槽 id 长得像 `slot.a.bak1` 的合法正式槽必须完全独立、
不受任何备份相关逻辑影响"，是当前代码库已经锁定的硬约束（顶层目录里任何 `<x>.json` 都可能是货真价实
的正式槽，无法用文件名启发式区分"这是旧备份"还是"这是同名正式槽"）。按文件名批量迁移会把真实槽的
正式文件误判成旧备份并移动/删除，是真实的数据损坏风险，不是测试过严。因此改为按需、只读、精确路径
的恢复兜底（只在 `Load` 某个具体 slotId 的正式文件与全部新布局备份都失败后，才去探测这个 slotId
派生出的旧路径），不删除/不移动旧文件，`ListSlots`/`CountSlots`/`DeleteSlot` 完全不受影响——这意味着
"旧布局主档损坏可从旧备份恢复"（已验证）落地了，但"配额不计旧备份"在当前硬约束下无法安全达成，记录
为已知局限而非遗漏。该判断已过既有回归测试验证；如果需要达成"配额不计旧备份"，需要产品层面重新拍板
"槽 id 是否允许与备份命名模式撞名"这一更上游的问题，不是实现细节能单方面解决的。

## 顺带核实但判断不改

- **`power.changed` 未纳入触发链**：`aura.expired` 不是独立事件 key，只是 `AuraRemovedEvent.Reason="expired"`
  的一个取值，已随 N04 的 `AuraRemovedEvent` 一并修复。`power.changed`（`PowerChangedEvent`）位于
  `core/numbers/power_set`，其所在程序集 `Core.Numbers.csproj` 只引用 `Core.Foundation`（L1），不引用
  `Core.Rules`（L2）——若要实现定义在 `Core.Rules.Common` 的 `ITriggerChainEvent` 会形成 L1 反向依赖 L2
  的架构违规，故未修改，记录为已核实、判断不改。`AuraStackChangedEvent`（同样由 `AuraHost.ApplyAura`
  效果结算路径产生、同一文件、同一量级改动）顺带一并实现了 `ITriggerChainEvent`，避免同一类"事件驱动
  Proc 自循环"缺口以另一个事件类型重现。
- **N10 失败码复用 `NoValidTarget` 的理由**：`CastFailureReason` 被多处消费（AI Rotation 条件、UI 提示、
  `core/carriers/gobj`、`presentation/render` 测试等，覆盖到玩法/表现侧范围），新增枚举值有较高概率在
  某处未覆盖分支的 `switch` 表达式上因为"警告即错误"（`Directory.Build.props`）而编译失败，风险与收益
  不成比例；06 文档本身也没有为"显式目标不满足额外条件"定义专门原因码，复用语义最接近的 `NoValidTarget`
  （同属"步骤 6 目标合法性"失败）是合理选择。
- **N05 选"降级为目标等级"而非"快照来源等级"**：AuraHost 若要快照来源等级需要新注入 `IUnitAccess`
  （可行，已确认可选参数不破坏现有调用点），但 Resolver 的降级只需要局部改动、不牵扯 AuraHost/EffectContext
  的数据流扩展，风险更低；06 文档未规定光环来源消失后的精确数值语义（只讲了"来源单位被销毁不代表已施加
  的光环应当消失"这一存在性判断），两种方案都满足"不抛异常、光环继续生效"这一硬要求，选择成本更低的一种。
- **N19 表现侧最终判据用状态机当前状态而非新字段（字段保留）**：`AnimStateMachine.OnSkillCastEnd` 的修复
  未直接消费 WA 新增的 `SkillCastSuccessEvent.IsInstant`/`CastTimeSeconds`——判据改用状态机自身记录的
  Current 状态（只有 `Cast` 状态由收尾事件驱动回落，`Attack` 独家由 `NotifyTransientStateFinished` 驱动），
  比按事件字段判断更直接可靠。两个新字段仍保留在事件定义中，供其它消费方使用。
- **N12 消费侧未依赖 WA 新字段而用既有 `RemoveCollectedItems`**：`QuestHost.HandleItemAdded` 的
  consume-on-progress 分支未等待/依赖 `ItemAddedEvent.Removals`，而是改用已有的 `RemoveCollectedItems`
  （跨堆叠按模板 id 扣除，`TurnIn` 交付时的既有逻辑）取代按 `evt.ItemInstanceId` 单实例扣除，按 code-review
  给出的第二个选项"提供批量消费 API"自行根治，不依赖新字段。

## previous-findings 旧文案纠正

- **RC08**：`previous-findings.md` 原文认为"移动接线存在于 `RulesAssembly:310`，不是 `SkillHost` 直接
  订阅"。核对结果：`core/rules/skill/README.md` 原第 26 条（RC-08）原文写"现在 `SkillHost` 构造期订阅
  `unit.moved`"，与代码不符——`SkillHost.cs` 里只有 `NotifyMoved` 这个可调用方法，真正的
  `Bus.Subscribe(EventKeys.UnitMoved, ...)` 在 `core/rules/assembly/RulesAssembly.cs:310`（跨层装配根，
  转调 `Skill.NotifyMoved`）。原因是 `core/rules` 不依赖 `core/carriers`（L3），无法直接引用强类型
  `UnitMovedEvent`，只能由持有跨层依赖的装配根用弱类型 `Id` 事件 key 订阅后转调。`core/rules/skill/README.md`
  第 26 条已改写为注明订阅方是 `RulesAssembly`（跨层装配根），不是 `SkillHost` 直接订阅。
- **RC02**（"当前只订阅 `entity.destroyed` 做清理；旧报告声称 `unit.died` 订阅不实"）：核对属实，无需
  改动。`core/rules/skill/core/AuraHost.cs` 只 `Subscribe<EntityDestroyedEvent>`，`core/rules/combat/core/CombatHost.cs`
  同样只订阅 `entity.destroyed`；仓库内两处 README（`skill/README.md`、`combat/README.md`）均未出现
  "订阅 `unit.died`"这类不实描述，没有需要纠正的文案。
- **RC10**（"AI 分类链已修；显式敌对单体绕过额外 tag/expr 条件见 N10"，旧文案"显式 target 已过滤"不能
  作为证据）：核对属实，无需改动。`core/rules/ai/README.md` 第 5 条（RC-10）的描述准确限定在"AI 侧只
  对分类为敌对单体的 Rotation 条目强塞目标"，未声称"显式 target 已经被过滤"这类过宽结论，与 N10（`CastPipeline`
  对显式 targets 未执行链的 tag/expr 条件）是互补而非重叠/矛盾的两件事，不需要改动。
- **`PowerMaxRecomputeWiringTests` 尾注引用的 RC06**：核对为外部审核历史文档自身的引用问题，仓库内无
  对应缺陷。`core/rules/tests/Integration/PowerMaxRecomputeWiringTests.cs` 文件与其中的测试方法
  `StatChanged_AutomaticallyRecomputesStatSourcedPowerMax_WithoutManualRecomputeMaxCall` 均真实存在；
  仓库内没有任何 README/注释引用一个不存在的 RC-06 测试名。当前审核报告的 `previous-findings.md` 已把
  RC06 与 N08（`progression.level_up` 事件顺序）对应，N08 已修复闭合。

## 验收

摘自 WA.md/WB.md 最终验收记录：

- `dotnet test`（六个测试项目全量）：2214 项通过，0 失败，0 跳过——`Tests.Foundation` 638、
  `Tests.Numbers` 106、`Tests.Rules` 345、`Tests.Carriers` 283、`Tests.Gameplay` 435（基线 424 + 11）、
  `Tests.PresentationCommon` 407（基线 403 + 4）。
- Unity EditMode：54/54 通过（基线 52 + 2）。
- Unity PlayMode：170/170 通过（基线 167 + 3）。
- `python toolchain/validate_data.py`：tables 59、records 276、errors 0、warnings 0；`_framework`
  骨架 60 files/2 roots、0 errors。
- `python -m pytest toolchain/tests -q`：46 passed。

本次第 5 步全量门禁（`check.ps1`，本次核实跟进提交之后重新执行）的实跑结果见提交信息与会话记录，
不在本文档重复贴全表。
