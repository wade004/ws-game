# 第六方深度审核（codex 第四轮，基于 7e63d66）核实跟进

基线 `7e63d66`；本轮外部深度审核只读工作树 `D:\workespace\ws-game-review-7e63d66`，报告提交
`6a42e02` 位于本地分支 `codex/project-review-7e63d66-20260907`，本目录（[README.md](README.md)、
[code-review.md](code-review.md)、[project-review.md](project-review.md)、
[doc-code-matrix.md](doc-code-matrix.md)、[previous-findings.md](previous-findings.md)、
[validation.md](validation.md)、[repro/](repro/)、[evidence/](evidence/)）经
`git cherry-pick 6a42e02`（提交 `2aeecc3`）原样归档进本分支。核实与修复提交为三个：

- `c66b557`（核心侧：`core/foundation`、`core/rules`、`core/carriers`）——对应 C01/C02/C03/C08/C09/C10。
- `910c5bb`（玩法/表现侧：`core/gameplay`、`presentation`、`adapters/unity`）——对应 C04/C05/C06/C07/C11/C12。
- `4c6572f`（交付侧：`build.ps1`、`check.ps1`、`.github/workflows/release.yml`、`toolchain/**`）——
  对应 P01～P07。

本文档只做核实结果的汇总归档，不重新展开调查——逐条证据、修改点、判断依据以对应提交的代码/测试/
README 为准。19 条全部**成立**，无"部分成立/不成立"。

## 19 条核实表

### 代码问题（C01～C12）

| 编号 | 判定 | 复现 | 修复位置 | 验收 |
|---|---|---|---|---|
| C01 | 成立 | `SaveSystemTests.Load_RequestedSlotHasNoOwnBackup_ButPathCollidesWithAnotherRealSlotsFormalFile_ReturnsNotFound_NoCrossSlotRead` | `core/foundation/save_system/core/SaveSystem.cs`：`TryReadLegacyBackup` 新增 `LegacyCandidateMatchesSlot` 核对 `meta.slot_id`，与请求槽名不一致即判定"属于另一个槽"跳过 | 同复现 + `Load_LegacyBackupWithoutSlotIdField_StillRecoversForRequestedSlot` |
| C02 | 成立 | `CreatureDespawnPeriodicEffectTests.PeriodicDotWithScalingStat_SourceDespawnedThenMultipleTicksElapse_DoesNotThrow_AndKeepsLandingDamage` | `core/rules/skill/core/EffectDispatcher.cs`（`ApplyDamageOrHeal` 缩放贡献未注册时降级为 0）+ `core/rules/combat/core/CombatHost.cs`（`NotifyCombatEvent` 对不存在单位静默跳过） | 同复现（真实 `CreatureFactory`+`AuraHost`+`CombatHost` 全链路） |
| C03 | 成立 | `ProcTests.TriggerChain_ViaAuraRemoved_AbsorbDepletedLoop_IsBoundedByMaxTriggerDepth` | `core/rules/common/contracts/IAuraQuery.cs`（新增带深度重载）+ `core/rules/skill/core/AuraHost.cs`（`ConsumeAbsorb` 传递深度）+ `core/rules/combat/core/Resolver.cs`（传入 `TriggerChainDepth`）+ `core/rules/assembly/RulesAssembly.cs`（`DeferredAuraQuery` 显式转发） | 同复现 |
| C08 | 成立 | `EquipmentReplaceHandleTests.ReplacePolicy_TwoItemsGrantSameAura_EitherUnequippedFirst_KeepsAuraUntilBothUnequipped`（+ `..._UnequipAFirst_...`） | `core/rules/common/contracts/IAuraQuery.cs`（新增 `InstanceReplaced` 事件）+ `core/rules/skill/core/AuraHost.cs`（Replace 分支同步触发）+ `core/rules/assembly/RulesAssembly.cs`（显式转发）+ `core/carriers/item/core/EquipmentHost.cs`（订阅并迁移句柄）+ `core/carriers/assembly/CarriersAssembly.cs`（接线） | 同复现 |
| C09 | 成立 | `KnownSkillsPersistableTests.Load_SameHost_ReplacesCurrentPermanentSkillSet_RemovingSkillsLearnedAfterSnapshot` | `core/rules/skill/core/KnownSkillsPersistable.cs`（`Load` 改为替换语义：先撤销"当前有、快照没有"的永久技能，再补回快照全部技能） | 同复现 + `Load_SameHost_ReplacePermanentSet_DoesNotBreakIndependentEquipmentGrantLifecycle` |
| C10 | 成立 | `SaveSystemTests.Load_FormalPassesShapeCheckButMetaMissingRequiredFields_FallsBackToHealthyBackup` | `core/foundation/save_system/core/SaveSystem.cs`：新增 `IsCandidateMetaUsable`，`ReadValidEnvelope`/`TryReadLegacyBackup` 选中候选前核对 `ParseMeta` 能否成功 | 同复现 + `Load_FormalMetaSemanticGap_NoHealthyCandidateAvailable_ReturnsCorrupted` |
| C04 | 成立 | 无独立复现测试（P1，直接根治：先写失败态断言再改代码）——验收测试即复现测试 | `core/gameplay/encounter/core/EncounterHost.cs`（`Evaluate` 步骤 4 发奖成功后才提交终态）、`core/gameplay/achievement/core/AchievementHost.cs`（`ApplyProgress`/新增 `RetryPendingRewards`，可重试待领奖持久化状态） | `EncounterHostTests.Evaluate_VictoryTrue_RewardGrantFails_KeepsActiveAndRetries_GrantsExactlyOnceAfterRoomFreed`、`AchievementHostTests.Unlock_RewardGrantFails_StaysLocked_RetryPendingRewardsGrantsExactlyOnceAfterRoomFreed` |
| C05 | 成立（真实 Core console 复现，REPRODUCED） | `RewardDispatcherTests.Grant_PartialPolicy_SecondItemFails_RollsBackOnlyActuallyAddedAmount_NotPreExistingStock` | `core/carriers/common/contracts/IInventoryHost.cs`（新增 `TryAddItem` 默认接口方法）、`core/carriers/item/core/InventoryHost.cs`（覆盖 `TryAddItem`）、`core/gameplay/common/core/RewardDispatcher.cs`（`GrantItems` 按实际落地量回滚） | 同复现 + `Grant_RejectPolicy_SecondItemFails_WholeBatchAtomicallyFails_WithRealInventoryHost`（回归）+ `InventoryHostTests` 两条新增用例 |
| C06 | 成立（两条路径均复现） | `QuestHostTests.HandleItemAdded_ConsumeOnProgress_TwoQuestsInsufficientForSecond_CreditedProgressMatchesActualConsumption`、`TurnIn_SingleQuestTwoObjectivesShareSameItem_InsufficientTotal_FailsWithoutLosingAnyItem` | `core/gameplay/quest/core/QuestHost.cs`（`RemoveCollectedItems` 改为先核验总量、不够不碰库存的原子操作） | 同两条复现 |
| C07 | 成立（VFX 超时不发信号 + SFX 无独立时钟入口，两点均复现） | `VfxPlayerTests.Update_TimesOut_TriggersPendingSpawnCountChanged_Once`、`SfxPlayerTests.Update_TimesOut_TriggersPendingPlayCountChanged_WithoutAnyFurtherPlayCall`、端到端 `FeedbackBinderTests.QueueMode_Sequential_RealSfxPlayerColdResourceTimesOut_FiresPlaybackFinishedExactlyOnce_ViaUpdateOnly` | `presentation/vfx_sfx/core/VfxPlayer.cs`（`Update` 超时分支补发事件）、`presentation/vfx_sfx/contracts/ISfxPlayer.cs`（新增 `Update`）、`presentation/vfx_sfx/core/SfxPlayer.cs`（实现）、`adapters/unity/.../Runtime/Shell/FrameworkResidentHost.cs`（新增 `Presentation.Sfx.Update(dt)` 生产接线） | 同三条复现 |
| C11 | 成立 | `GameplayAssemblySpawnTimerSameMapReloadTests.SavedRespawnTimer_SurvivesSameMapReload_EvenWhenCurrentSessionAlreadyRespawnedANewEntity` | `core/gameplay/spawn/core/SpawnHost.cs`（`Load`：快照记录了 `respawn_remaining` 时不再重绑 `previouslyAlive` 实体、不清空倒计时——存档倒计时优先于读档当下世界状态） | 同复现（经 `GameplayAssembly.RestoreFromSlot`） |
| C12 | 成立 | `DeathPolicyHostTests.ReloadSave_PlayerDies_LoadSucceeds_PublishesUnitRespawnedEvent_DeferredNotImmediate`、`ReloadSave_PlayerDies_RespawnedEventArrivesAfterAllUnitDiedSubscribersProcessed_NotOverwrittenByLateDeathHandler` | `core/gameplay/death/core/DeathPolicyHost.cs`（`OnUnitDied` 的 `ReloadSave` 成功分支 `Enqueue` 一次 `UnitRespawnedEvent`，避免与当前 `unit.died` 派发栈内的下游处理器发生覆盖） | 同两条复现 + Unity PlayMode `AuditBlockersPlayModeTests.PlayerDies_ReloadSave_PlayerRevivedAndViewExists`（已扩展断言） |

### 项目/交付问题（P01～P07）

| 编号 | 判定 | 复现 | 修复位置 | 验收 |
|---|---|---|---|---|
| P01 | 成立 | 干净 fixture 里 `build.ps1 -SyncOnly -Dist 1.0.0 -Zip` 在 `dotnet build --artifacts-path` 统一产物布局下找不到传统路径 DLL，退出 1 | `.github/workflows/release.yml`：`build.ps1 -SyncOnly -Dist <ver> -Zip` 之前新增一步 `build.ps1 -SkipTests`（完整 `dotnet build` 分支但跳过测试，产出传统路径 DLL） | 干净 fixture 实测：`check.ps1 -SkipUnity -Quick` PASS → 新增步骤退出 0 → `-SyncOnly -Dist -Zip` 退出 0，产出 `dist/ws-game-1.0.0.zip`（REPRODUCED→FIXED） |
| P02 | 成立 | `Validator.csproj` 无条件 `ProjectReference` 指向 `presentation/`/`adapters/stub/` 源码，ZIP 与 UPM `com.gamefoundation.toolchain` 包均不随包分发这两棵源码树；`validate_data.py` 的 `find_repo_root()` 在 UPM `Tools~/` 布局下拼出不存在的路径 | `toolchain/validator/Validator.csproj`（按源码是否存在二选一：源码内走 ProjectReference，独立包内引用编译好的 DLL）；`build.ps1` 打包新增拷贝六个核心 DLL 到 `dist/<ver>/toolchain/validator/lib/`；`toolchain/validate_data.py`（`validator_project` 改为相对自身文件位置）；新增回归测试 `toolchain/tests/test_validate_data_validator_project_path.py` | 真实 `dist` ZIP 与真实 `.tgz` 两条通道解压后独立 `validate_data.py`/`dotnet build Tools~/validator/Validator.csproj` 均退出 0；`pytest toolchain/tests -q` 47 passed（REPRODUCED→FIXED） |
| P03 | 成立 | `sync_package_content.ps1` 的 `Sync-Tree` 对目标目录整树镜像，会删除消费者自己混在公共目录（如 `Assets\TextMesh Pro`）里的文件 | `toolchain/sync_package_content.ps1`：`Sync-Tree` 改为清单制——记录"本脚本自己写入的文件"，下次只清理"上次清单里有、这次源目录已没有"的框架 stale 文件，从不删除未被记录过的消费者文件 | 真实 fixture 三轮实测（首次同步、幂等重跑、框架 stale 文件清理）均保留消费者 sentinel 文件（REPRODUCED→FIXED） |
| P04 | 成立 | `get_framework.ps1 -Version 1.0.1 -FromLocalDist <1.0.0 的 zip>` 此前只 warning，仍按请求版本命名目录、可能先删除该目录 | `toolchain/get_framework.ps1`：新增 `-AllowVersionMismatch` 开关（默认关闭）；不一致默认 `throw`，不落地/不删除/不覆盖；显式放行后落地目录名与提示信息一律改用锁文件的实际版本号 | 三种场景实测（不一致默认拒绝且哨兵文件无损、显式放行按实际版本落地、版本一致回归正常）（REPRODUCED→FIXED） |
| P05 | 成立 | `build.ps1` 硬编码 `git push origin main --tags`，与调用时实际所在分支无关，`--tags` 推送本机全部标签 | `build.ps1`：改为 `git rev-parse --abbrev-ref HEAD` 取当前分支，推送 `git push origin <当前分支> refs/tags/<本次新建的标签>`；detached HEAD 时报错拒绝 | 未做真实推送（超出允许操作范围）；只读方式验证分支探测机制在真实维护分支 `release/1.0.x` 与 `main` 上均正确取值 |
| P06 | 成立 | `config.yaml` 无 `auth.htpasswd.max_users`，publish/unpublish 匹配 `$authenticated`——任何自注册用户即可发布/删包 | `toolchain/registry/config.yaml`：`max_users: -1`（禁止自注册，`0` 因 JS 假值兜底反而放开，是本次澄清的陷阱）；`publish`/`unpublish` 从 `$authenticated` 改为显式用户名 `ws-game-publisher`；`toolchain/registry/init_publisher.ps1`：改为内嵌脚本直接写 htpasswd 哈希建号，绕开受 `max_users` 限制的注册接口 | 本机起停一次真实 Verdaccio，六项实测（自注册拒绝、无人值守建号成功、发布 dry-run 成功、匿名读放行、未认证 unpublish 拒绝、非授权账号 unpublish 拒绝）全部通过，服务已停（REPRODUCED→FIXED） |
| P07 | 成立 | `sync_package_content.ps1` 只把 `assets/_placeholder` 整体镜像，缺少 `build.ps1` 侧 sprites/audio/vfx 的改名/扁平化步骤，与 `UnityResourceLoader.ResolvePath` 实际查找路径不一致 | 新增 `toolchain/resource_layout_map.json`（`{sprites->sprites, sfx->audio, vfx->vfx}` 唯一权威映射）；`build.ps1` 三处同步改为读该 JSON 驱动；`toolchain/sync_package_content.ps1` 新增读取同一份 JSON 做对应同步（复用 P03 清单制 `Sync-Tree`） | 真实 fixture 同步后 `GameFoundation/audio/ui_click_01.wav` 等精确命中；`build.ps1 -SkipTests`/`check.ps1` 全量门禁正常跑通该步骤（REPRODUCED→FIXED） |

## doc-code-matrix 漂移处理

对照报告 `doc-code-matrix.md`"README 与源码漂移"与"架构章节"两节逐条标注处理方式：

### README 与源码漂移

| 条目 | 处理方式 |
|---|---|
| `core/gameplay/common/README.md:73` 原子发奖表述超出保证 | 已修（WB，提交 `910c5bb`）——README 判断记录改写，限定"回滚精确性"实际保证边界 |
| `core/gameplay/quest/README.md:118` GP07 事件锚点漂移 | 已修（WB，提交 `910c5bb`）——新增判断记录 11，`RemoveCollectedItems`/`HandleItemAdded` 原子化说明同步 |
| `core/gameplay/spawn/README.md:111` 未说明同图重载 timer 处理（对应 C11） | 已修（WB，提交 `910c5bb`）——新增判断记录 9 |
| `presentation/feedback_binder/README.md:105` 播放完成协议缺 timeout 终止路径（对应 C07） | 已修（WB，提交 `910c5bb`）——新增判断记录 13 |
| `presentation/assembly/README.md:122` Validator 未接入仍是当前事实 | 记录为已知边界——报告本身已确认这是当前事实描述，不是漂移，不需要改动 |
| `presentation/common/README.md:94`/`:77-81` 历史判断"留待后续"与当前已接通描述前后冲突 | 已修（WB，提交 `910c5bb`）——判断记录 5 末尾"留待后续"改为已完成表述 |
| `core/foundation/scene_router/README.md:116` 旧 void 询问已过时 | 已核实：现状与代码一致（WA 报告核对结论），全文档唯一"void"字样是解释"为什么偏离 03 原文的 void 签名"而非当前实现描述，无需改动 |
| `games/_template/README.md:41` Options 不是模块启停开关 | 本次处理（第 4 步）——已在 `games/_template/README.md` 表格下方新增边界说明段 |
| `core/rules/tests/Integration/PowerMaxRecomputeWiringTests.cs:49` 尾注引用不存在的历史测试名 | 已修（WA，提交 `c66b557`）——改为准确引用 `RecomputeRatingStats_AfterLevelLookupChanges_UpdatesCacheAndFiresStatChanged` |
| 根 `README.md:9`、`CHANGELOG.md:16`"阶段全部完成/稳定可接入"应附能力边界 | 本次处理（第 4 步）——根 `README.md` 落地状态声明附一句话 + 指向落地计划"能力边界与未默认接入能力索引"小节；`CHANGELOG.md` 新增 `[1.1.0]` 变更记录、接口变更与迁移说明，以及 `[1.0.0]` 下"从 68c9bed 早期消费者迁移"矩阵 |

### 架构章节

| 章节 | 处理方式 |
|---|---|
| 00 架构总则 | 记录为已知边界——报告本身即声明"就绪结论须依据缺口与运行验收"，不是文档错误，不需要改动 |
| 01 分层与依赖 :185 L5 改变下层状态口径冲突 | 本次处理（第 4 步）——第 6 节禁止事项清单第 2 条改写为与 09/`UiIntents.cs` 一致的口径："L5 只读订阅，改变状态只能通过提交意图（命令入口），业务判断留在下层" |
| 02 引擎适配层 IRenderer3D 边界 | 记录为已知边界——已收录进本文档"未实现或未默认接入能力索引"（Unity 3D 渲染） |
| 03 运行时骨架 SceneRouter 代际隔离/README void 签名 | 已核实：03 正文已用 `SubscriptionHandle`，仅 `scene_router/README.md` 历史措辞需核对（见上表，已核实无需改动） |
| 04 数据与内容管线 | 记录为已知边界——独立发行包 validator 依赖/路径边界即 P02，已修 |
| 05 对象模型与世界 | 已修（WA，提交 `c66b557`）——C02（来源销毁后周期效果与进战判定）语义补充，交叉引用 06 第 3.8 节 |
| 06 规则层 TargetPoint/FindUnits/离散 tick/位移效果/天赋点流程边界 | 记录为已知边界——均已收录进"未实现或未默认接入能力索引"；`SkillValidationRules` 最小 stack_category 冲突校验与运行期未实现跨 aura 聚合叠加上限的差距亦记录为已知边界，不在本轮修复范围 |
| 07 载体层 Equipment Replace/技能读档集合 | 已修（WA，提交 `c66b557`）——分别为 C08、C09 |
| 08 玩法层 Encounter/Achievement 发奖失败/奖励回滚/Quest 部分扣除/Quest.Update 未调用 | 前三项已修（WB，提交 `910c5bb`，C04/C05/C06）；`Quest.Update` 默认未调用记录为已知边界，收录进能力索引"日任务与自动 Quest 驱动" |
| 09 表现层 VFX/SFX timeout 完成链/reload_save 同图 Death 动画/默认 3D 边界 | 前两项已修（WB，提交 `910c5bb`，C07/C12）；默认 3D 路线 NotSupported 记录为已知边界，收录进能力索引 |
| 10 存档与持久化 stream_states/beginRecording 缺主种子字段、game_id 校验边界、ComputeReadOrder 边界 | 前两项已修（WA，提交 `c66b557`）——10 号文档补 `master_seed` 字段说明；`game_id`/`ComputeReadOrder` 边界记录为已知边界，不在本轮修复范围（报告未将其列为 C 编号问题） |
| 11 工程规范与测试 FeedbackRuleValidator 未注册/独立包 validator | 独立包 validator 已修（P02）；`FeedbackRuleValidator` 未注册记录为已知边界，不在本轮范围 |
| 12 扩展与变更流程 | 记录为已知边界——本轮全部文档改动均按 12 §5 细节勘误处理，无需改动 12 本身 |
| 13 新游戏接入指南 template options 边界 | 本次处理（第 4 步）——`games/_template/README.md` 边界说明段同步覆盖 |
| 14 资产规格书模板 sprite/VFX/importer 边界 | 记录为已知边界——均已收录进能力索引（VFX 锚点跟随） |

## 未实现或未默认接入能力索引

原样摘自审计报告 `doc-code-matrix.md`"未实现或未默认接入能力索引"一节，作为公开边界声明：

| 能力 | 当前锚点与边界 |
|---|---|
| Unity 3D 渲染 | `UnityRenderer3D.cs:21` 为 NotSupported。 |
| 编辑器工具 | `editor/README.md:5` 尚未开始。 |
| 天赋点激活 | `archetype/README.md:67`、:90 只有 `GetTalentTree`，没有点数学习/激活/撤销/存档流程。 |
| FindUnits | `SkillHost.cs:138` 无条件返回空数组。 |
| TargetPoint | `SkillTickHandler.cs:16` 声明由上层处理，无默认消费。 |
| 离散召唤/掉落过期 | `SummonTickHandler.cs:56`、`LootExpiryTickHandler.cs:37` 对非 Continuous 跳过。 |
| 位移轨迹碰撞 | `EffectDispatcher.cs:291`、:310、:322、:337 直接 `SetPosition`，无轨迹/沿途碰撞。 |
| 日任务与自动 Quest 驱动 | `GameplayAssembly.cs:519` 注入 `dayProvider=null`，`QuestHost.cs:83` 默认恒 0；`ownerResolver` 在 `GameplayAssembly.cs:518` 为 null，`vendorOpenRequested` 在 `GameplayAssembly.cs:691` 为 null；escort 有契约/schema 和手动进度入口，没有 `QuestHost` 自动执行分支，全仓没有 `Quest.Update` 调用。 |
| 新局完整重置 | `SampleNewGameStarter.cs:38` 仅处理位置、地图、模板，不清库存、任务、货币和生命。 |
| VFX 锚点跟随 | `VfxPlayer.cs:144` 只使用出生坐标。 |

## 验收

第 5 步全量门禁（`check.ps1`，本次三次修复提交 + 核实跟进提交之后重新执行）的实跑结果：见本次会话报告
（PASS/FAIL/SKIP 汇总、耗时、dotnet test 六项目计数、EditMode/PlayMode 计数），不在本文档重复贴全表。
