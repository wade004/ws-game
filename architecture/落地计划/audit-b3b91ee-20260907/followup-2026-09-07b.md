# 第四方深度审核修复跟进（2026-09-07b）

对应外部深度审核分支 `codex/deep-review-b3b91ee-20260907`（`d69f339`，报告见本目录）。
33 条发现已由三波并行核实（Y1 `core/foundation/**` 除 event_bus；Y2 `core/rules|carriers|numbers`；
Y3 `core/gameplay|presentation|adapters|games|toolchain|check.ps1`）全部根治并提交；本文件是
落盘后的核实表 + 4 条 PlayMode 失败的根因分析 + doc-code-matrix.md 逐条核对结果。

## 一、33 条发现核实表

“提交”列的短哈希对应 `audit6/deep-review-fixes` 分支：
`0e30d79`＝1/3（foundation）、`8a70cc4`＝2/3（rules/carriers/numbers）、`2be0651`＝3/3（gameplay/presentation/adapters/games/toolchain/check.ps1）。

| 编号 | 成立性 | 复现测试 | 修复位置 | 验收测试 | 提交 |
|---|---|---|---|---|---|
| FND-01 | 成立 | 审核 `validation-repros.txt` R3（.NET 有界复现：`slot.a`/`slot.a.bak1` 路径碰撞） | `core/foundation/save_system/core/SaveSystem.cs`（`BackupsDir()`/`BackupPath` 迁至 `<SavesDir>/backups/`） | `SaveSystemTests.cs`（新增槽 id 撞名用例） | `0e30d79` |
| FND-02 | 成立 | 审核 `validation-boundaries.md` FND02（StubResourceLoader 有界复现：A 迟到回调污染 B） | `core/foundation/scene_router/core/SceneRouter.cs`（`_navigationGeneration` 代际计数器） | `SceneRouterStaleCallbackTests.cs`（新增） | `0e30d79` |
| FND-03 | 成立 | 审核 `validation-boundaries.md` FND03（坏 JSON + 无参 Validate 复现遮蔽） | `core/foundation/data_registry/core/DataRegistry.cs`（按表保留加载诊断） | `DataRegistryTests.cs` | `0e30d79` |
| FND-04 | 成立 | 审核 `validation-repros.txt` R4（.NET 有界复现：旧句柄误取消新计时器） | `core/foundation/sim_loop/core/SimTimers.cs`（新增 `Clear()`）、`WorldSim.cs`（`_timers` 改 readonly，`ClearAll` 原地清空） | `WorldSimEntityTests.cs`、`TurnSchedulerTests.cs` | `0e30d79` |
| FND-05 | 成立 | 审核 `validation-repros.txt` R5（.NET 有界复现：fixed_order 中途加入 AP=0） | `core/foundation/sim_loop/core/TurnScheduler.cs`（AP 初始化移到先攻插入前） | `TurnSchedulerTests.cs` | `0e30d79` |
| FND-06 | 成立 | 静态生命周期审查（订阅无法释放） | `core/foundation/sim_loop/core/WorldSim.cs`（实现 `IDisposable`/`IsDisposed`）、`core/foundation/save_system/core/ReplayPlayer.cs`（二次 Load 释放旧世界） | `DiscreteReplayTests.cs` | `0e30d79` |
| FND-07 | 成立 | 静态分支审查（正式档缺失直接 NotFound、信封校验过浅） | `core/foundation/save_system/core/SaveSystem.cs`（`ReadValidEnvelope` 候选统一化、`TryParseEnvelope` 类型校验） | `SaveSystemTests.cs` | `0e30d79` |
| FND-08 | 成立 | 静态事件折叠审查（同帧 down/up 边沿丢失） | `core/foundation/input_map/core/InputMapHost.cs`（逐事件维护 pressed/released edge） | `InputMapHostTests.cs` | `0e30d79` |
| FND-09 | 成立 | 静态迁移循环审查（1→3 越过当前版本 2 仍成功） | `core/foundation/save_system/core/SaveSystem.cs`（`ToVersion > CurrentSaveVersion` 拒绝 + 终点双重校验） | `SaveSystemTests.cs` | `0e30d79` |
| FND-10 | 成立 | 审核 `validation-repros.txt` R2/R2b（.NET 有界复现：空/小快照合并旧装备） | `core/carriers/item/core/ItemPersistable.cs`、`EquipmentHost.cs`（快照全量替换） | `ItemPersistableTests.cs`、`EquipmentHostTests.cs` | `8a70cc4` |
| GP-01 | 成立 | 审核 `validation-repros.txt` R1（.NET 有界复现：旧任务状态跨档残留） | `core/gameplay/quest/core/QuestPersistable.cs`（全量替换） | `QuestPersistableTests.cs`（新增） | `2be0651` |
| GP-02 | 成立 | 静态默认接线审查（Hit/Death 终态永久锁定、复活/销毁未清理） | `adapters/unity/.../UnityViewFactory.cs`（瞬态完成接 `NotifyTransientStateFinished`、订阅 `unit.respawned`/`entity.destroyed` 调 `Forget`） | `UnityViewFactoryDefaultAnimationTests.cs`（新增） | `2be0651` |
| GP-03 | 成立 | 静态审查（`VfxPlayer.Update` 无生产入口调用） | 三处引导（`GameFoundationBootstrap.cs`/`FrameworkResidentHost.cs`/`GameBootstrap.cs`）接 `Vfx.Update` | `VfxPlayerTests.cs` | `2be0651` |
| GP-04 | 成立 | 静态链路审查（`LeaveMap` 不终止旧 Encounter/Level） | `core/gameplay/assembly/GameplayAssembly.cs`、`EncounterHost.cs`/`LevelHost.cs`（新增 `AbortForMap`+`LeaveMap` 接线） | `EncounterHostTests.cs`、`LevelHostTests.cs` | `2be0651` |
| RC-01 | 成立 | 静态审查（EventBus 异步 Enqueue 后触发链深度归零） | `core/rules/common/contracts/ITriggerChainEvent.cs`（新增）、`CastPipeline.cs`/`ProcHost.cs`（深度随事件传播） | `ProcTests.cs` | `8a70cc4` |
| RC-02 | 成立 | 审核 `validation-repros.txt` R7（.NET 有界复现：真实 Combat/Carriers 注销单位访问异常） | `core/rules/combat/core/CombatHost.cs`（订阅 `entity.destroyed`/`unit.died` 幂等清理） | `CombatEnterLeaveTests.cs`（新增） | `8a70cc4` |
| RC-03 | 成立 | 静态集成审查（死亡/销毁读条未取消） | `core/rules/skill/core/CastPipeline.cs`（订阅死亡/销毁取消读条与队列） | `CastPipelineDeathDestroyTests.cs`（新增） | `8a70cc4` |
| RC-04 | 成立 | 审核 `validation-repros.txt` R6（.NET 有界复现：超距仍产生 1 次 AP 消费） | `core/rules/skill/core/CastPipeline.cs`（AP 消费点后移到全部检查通过后） | `CastPipelineFailureTests.cs` | `8a70cc4` |
| RC-05 | 成立 | 静态审查（HashSet 不分来源，卸一件永久学习技能被遗忘） | `core/carriers/item/contracts/SkillGranter.cs`（四参补 `sourceId`）、`EquipmentHost.cs`、`CarriersAssembly.cs` | `EquipmentHostTests.cs` | `8a70cc4` |
| RC-06 | 成立 | 静态审查（Power 上限/评级缓存无生产重算接线） | `core/numbers/power_set/core/PowerHost.cs`、`stat_block/core/StatHost.cs`、`core/rules/assembly/RulesAssembly.cs`（订阅 `stat.changed`/`progression.level_up`/`unit.moved`） | `StatHostTests.cs`、`PowerMaxRecomputeWiringTests.cs`（新增） | `8a70cc4` |
| RC-07 | 成立 | 静态审查（离散模式学派锁不衰减） | `core/rules/skill/core/CastPipeline.cs`/`SkillHost.cs`（离散轮末推进学派锁） | `DiscreteModeCastPipelineTests.cs`（新增） | `8a70cc4` |
| RC-08 | 成立 | 静态审查（movement interrupt 无生产接线） | `core/rules/skill/core/SkillHost.cs`（订阅 `unit.moved`） | `MovementInterruptWiringTests.cs`（新增） | `8a70cc4` |
| RC-09 | 成立 | 静态审查（抛射物整步命中后才截射程） | `core/carriers/projectile/core/ProjectileHost.cs`（按剩余射程先截段再测试） | `ProjectileHostTests.cs` | `8a70cc4` |
| RC-10 | 成立 | 静态审查（AI 把当前目标强塞所有技能，绕过目标链） | `core/rules/ai/core/AiHost.cs`/`CompiledRotationEntry.cs`（目标分类，显式 targets 仍按阵营/存活过滤） | `AiRotationTargetingTests.cs`（新增） | `8a70cc4` |
| RC-11 | 成立 | 静态审查（`weapon_damage_pct` 被当 BaseValue，不读武器基数） | `core/rules/common/contracts/IWeaponDamageQuery.cs`（新增）、`core/rules/assembly/DeferredWeaponDamageQuery.cs`（新增）、`EffectDispatcher.cs` | `EffectPrimitiveDispatchTests.cs` | `8a70cc4` |
| GP-05 | 成立 | 静态审查（对话选择不重验 VisibleIf/Condition） | `core/gameplay/dialog/core/DialogHost.cs`（执行时重验 + `is_objectives_complete`）、`presentation/ui/core/ViewModels/DialogViewModel.cs` | `DialogHostTests.cs`、`ViewModelTests.cs` | `2be0651` |
| GP-06 | 成立 | 静态审查（`TryGetEffect` 未命中即永久单帧 fallback，不发起加载） | `adapters/unity/.../UnityViewFactory.cs`（未命中主动 `LoadAsync`，完成后重注册真实剪辑） | `UnityViewFactoryDefaultAnimationTests.cs` | `2be0651` |
| GP-07 | 成立 | 静态审查（`RemoveItem` 失败仍用事件数量增长 consume 进度） | `core/gameplay/quest/core/QuestHost.cs`（按实际成功扣除量增长） | `QuestHostTests.cs` | `2be0651` |
| GP-08 | 成立 | 静态审查（Accept 非消耗 collect 任务不读已有库存） | `core/gameplay/quest/core/QuestHost.cs`（Accept 后按现存库存初始化） | `QuestHostTests.cs` | `2be0651` |
| GP-09 | 成立 | 静态审查（冷资源 pending 不纳入离散完成门，派发可乱序） | `presentation/feedback_binder/core/FeedbackBinder.cs`/`CompositeFeedbackSink.cs`（`HasPendingPlayback` 纳入 Vfx/Sfx pending） | `CompositeFeedbackSinkTests.cs`（新增）、`FeedbackBinderTests.cs` | `2be0651` |
| GP-10 | 成立 | 静态审查（模板/场景 Bootstrap 未推进 CharacterRig 时间轴） | `games/_template/Runtime/GameBootstrap.cs`、`adapters/unity/.../GameFoundationBootstrap.cs`（每帧 `AdvanceCharacterRigs`） | 覆盖于既有 `UnityViewFactoryDefaultAnimationTests.cs` 相关断言 + 手工复核 Update 调用链 | `2be0651` |
| TOOL-01 | 成立 | 复现 `repro/tool-01-repro.ps1`/`.txt`（PowerShell 5.1 实测：缺可执行文件 EAP Continue 沿用旧 `$LASTEXITCODE=0`） | `check.ps1`（`Test-NativeExitCode` 改用 `Get-Command` 先验证存在） | check.ps1 自身新增自检步骤（"自检自测：Test-NativeExitCode 对失败/成功原因给出正确判定"，历次门禁日志可见 PASS） | `2be0651` |
| TOOL-02 | 成立 | 复现 `repro/tool-02-repro.py`/`.txt`（`sfx.fire.hit` 与 `sfx.fire_hit` 去域后归一化碰撞） | `toolchain/asset_import/common.py`/`sfx_cmd.py`/`vfx_cmd.py`（双下划线保留结构，避免碰撞编码） | `toolchain/tests/test_import_assets.py`（新增碰撞用例） | `2be0651` |

共 33 条，全部成立并已修复；未发现审核报告存在误判条目。

## 二、PlayMode 四条失败的根因与修复

### 现象

`VerticalSliceTests` 的 `Paperdoll_LayerOrder_MatchesDisplayMapDeclaredOrder`、
`Pause_StopsWorldSimTick_MovementDoesNotAdvance`、
`Projectile_CastBoltSkill_ShowsProjectileView_ThenRemovedOnHit`、
`YSorting_TwoEntitiesWithDifferentY_SortingOrderReflectsY` 四条用例，在完整 165+ 条 PlayMode
套件里稳定失败，报错均为 `EnterInWorld` 内 `Assert.AreEqual(ShellPage.InWorld, ...)`：
`Expected: InWorld, But was: MainMenu`；单独用 `-testFilter VerticalSliceTests` 只跑本夹具 7
条用例则 100% 通过（本次复核实测两次：7/7、9/9 含新增回归测试）。

### 排查过程

1. `FrameworkResidentHost`（`adapters/unity/.../Runtime/Shell/FrameworkResidentHost.cs:163-202`）
   是 `DontDestroyOnLoad` 单例，`Bootstrap()`（含其内部的 `WorldSim`/`SceneRouter`/`ShellHost`/
   `SaveSystem`）在整条 `-runTests` 命令的生命周期里只构造一次——跨全部 165+ 条 PlayMode 用例
   共享，`EnterInWorld` 里的 `SceneManager.LoadScene("Shell")` 只重建场景内的 MonoBehaviour，
   不会重建这些常驻对象。
2. `ShellHost.NewGame`（`presentation/shell/core/ShellHost.cs:104-143`）顺序为：
   `_difficulty.Apply` → `_newGameStarter` → `_saveSystem.Save(...)` → 仅当 `Save` 成功才调用
   `_sceneRouter.LoadScene`。`Save` 失败会让 `NewGame` 直接 `return false`，`Page` 保持调用前的
   `MainMenu`，根本不会走到场景加载——与观测到的"从未见过一次真实场景资源加载失败"一致（本轮
   在 `UnityResourceLoader` 加了临时诊断核实：scene/nav_mesh 每次都在下一帧内成功）。
3. `core/foundation/save_system/core/SaveSystem.cs:121` 的配额检查
   `!slotExists && MaxSlots(默认 20) > 0 && CountSlots() >= MaxSlots` 会在"目标槽不存在 + 已有
   槽数达到 20"时返回 `SaveFailureReason.SlotLimitReached`。`Application.persistentDataPath`
   是宿主机上真实、跨 `-runTests` 调用持久化的目录，不会被 Editor 自动清空。
4. `adapters/unity/.../Tests/Runtime/GlobalPlayModeTestSetup.cs`（早前一版排障已加的
   `[SetUpFixture]`）在整条 -runTests 之前清理存档槽，但**只清理 `"game.sample."` 前缀**的顶层
   文件——同一装配（`Adapter.Unity.Tests.Runtime`）里还有大量用例用其它前缀新建槽
   （`game.template.slot_*`、裸 `slot.*`，例如 `TemplateSmokeRunner`/审核复现用例），从不清理，
   跨开发机历次独立 `-runTests` 调用持续累积；叠加 FND-01 把备份迁到 `saves/backups/` 子目录后，
   `CountSlots` 不再套用旧的按文件名猜测的 `IsBackupFileName` 过滤——本次排障在开发机
   `Application.persistentDataPath/saves` 下确认发现历史遗留的**顶层旧格式** `<slot>.bakN.json`
   文件（迁移前写入，不在新的 `backups/` 子目录里），现在会被新版 `CountSlots` 当成一个个真实槽
   计入配额。两者叠加，配合一次完整 PlayMode 套件本身新建的数十个不重复 `game.sample.slot_*`
   槽，必然在测试执行序列中的某个固定点越过 `MaxSlots=20`，此后同一次运行内任何"新建槽"的
   `NewGame` 都会持续失败（配额只增不减）——这正是"单独跑 100% 通过、混跑固定失败同样 4 条"的
   根因，与 SceneRouter 代际隔离、`WorldSim.Dispose`/`SimTimers.Clear` 均无关（三处经本轮复核
   未发现相关缺陷；`WorldSim.Dispose` 目前只在 `ReplayPlayer` 二次 `Load` 时调用，不在
   `NewGame`/`LoadScene` 链路上）。

### 修复

`GlobalPlayModeTestSetup.cs`：清理范围从"只清 `game.sample.` 前缀的顶层文件"扩大为
`ClearAllSaveArtifacts(savesDir)`——递归清空整个 `saves/` 目录树（含 `backups/` 子目录与任何
历史遗留的旧格式备份文件），不按前缀筛选。`Application.persistentDataPath` 对本 Unity 工程
是测试/开发私有目录，不存在"其它真实游戏"与之共用，原实现"避免误删同一台宿主机上其它非本
示例数据集存档"的顾虑不成立；`SaveSystemOptions.MaxSlots` 配额本身不改动（既定行为，见
`core/foundation/save_system/README.md`）。

### 回归测试

`GlobalPlayModeTestSetupTests.cs`（新增，同目录）：
- `ClearAllSaveArtifacts_RemovesFilesAcrossAllPrefixesAndBackupsSubdir`——构造 `game.sample.`/
  `game.template.`/裸 `slot.` 三种前缀的顶层文件 + `backups/` 子目录文件 + 顶层旧格式
  `.bakN.json`（共 9 个），断言清理后一个不剩；修复前的清理范围（只清一个前缀）对着这份布局
  跑会遗漏 7 个文件，稳定复现原缺陷。
- `ClearAllSaveArtifacts_DirectoryDoesNotExist_ReturnsZeroWithoutThrowing`——目录不存在时的
  边界情况。

### 验证

- `-testFilter VerticalSliceTests`：7/7 通过（修复前后均通过，因为单独跑本就不触发配额）。
- `-testFilter "VerticalSliceTests|GlobalPlayModeTestSetupTests"`：9/9 通过。
- 全量 165+ 条 PlayMode（含本次新增 2 条回归测试）：见第四节门禁汇总表。

提交：本节改动随第五个提交（`GlobalPlayModeTestSetup.cs` 改动 + 本文件）落盘，不在前 3 个
按领域拆分的提交里（这是排障过程中新发现的缺陷，不属于外部审核报告原有 33 条）。

## 三、doc-code-matrix.md 逐条核对

外部审核 `doc-code-matrix.md` 里标记为"漂移"的条目，绝大多数是"架构文档描述正确、代码此前
未遵守"（即 33 条发现本身）——本轮把代码修到与文档一致后，这些条目**不需要修改架构文档**，
只需要在下面确认"代码已追平文档"：

- `03_运行时骨架.md`（SceneRouter generation）、`04_数据与内容管线.md`（FND-05 AP 顺序）、
  `05_对象模型与世界.md`（FND-04 计时器句柄）、`06_规则层...md`（RC-10 目标链、RC-11
  `weapon_damage_pct`）、`08_玩法层...md`（GP-01/04/05/07/08）、`10_存档与持久化.md`
  （FND-01/07/09/10）：代码本轮已改为遵守文档既有描述，文档文本本身不成立"漂移"，不改。
- `03_运行时骨架.md` 提到的"SceneRouter 类型注释仍有'无 Loading→MainMenu'旧注释"，指的是
  `SceneRouter.cs` 源码内注释（不是架构文档），Y1 提交已重写该注释（见
  `core/foundation/scene_router/core/SceneRouter.cs` 类型顶部判断记录），已解决。
- `11_工程规范与测试.md` 提到的 TOOL-01 已随 `check.ps1` 修复解决（见上表）。

以下是本轮**确认仍待办**、超出 33 条发现范围、不在本次改动授权内的条目（均为
doc-code-matrix.md 原有"待实现/预留"类描述，不是本轮引入或本轮声称修复的问题，如实记录、
不代为处理）：技能落点 `TargetPoint` 生产入口未贯通、`ISkillHost.FindUnits` 无真实空间查询
消费者、天赋树激活宿主未实现、光环 `stack_category` 类别级语义未消费、控制收益递减未实现、
召唤离散生命周期/跟随未实现、charge/leap/knockback 轨迹只保证瞬时落点、escort 任务自动路线/
护送死亡宿主未实现、Vendor 曲线/物品价上下文未实现、VFX anchor/screen 持续跟随未实现、
Unity 3D/model 全 `NotSupported`、编辑器未开始实现、`01_分层与依赖.md` 的 L5 只读边界与
`UiIntents` 直调下层的表述冲突。这些均为预先记录在案的能力边界或独立于本轮 33 条发现的既有
待办，按 12_扩展与变更流程.md，需要实现时应先出 ADR，不在本次审核修复授权范围内。

模块 README 层面，Y1 已同步 `save_system/README.md`、`sim_loop/README.md`、
`input_map/README.md`（备份目录、`Clear()`、迁移终点校验、同帧边沿等改动的直接影响面）。
`core/carriers/item/README.md`、`core/rules/skill/README.md`、`core/rules/combat/README.md`、
`core/carriers/projectile/README.md`、`core/rules/ai/README.md`、
`core/gameplay/quest/README.md`、`core/gameplay/dialog/README.md`、
`core/gameplay/encounter/README.md`、`presentation/vfx_sfx/README.md`、
`presentation/feedback_binder/README.md` 这些模块的 README 尚未逐条同步 RC-01～11/GP-01～10
对应的行为改动描述——判断记录：这些 README 的既有文字大多只是"未提及"新行为（不是"断言与新
行为矛盾"），本轮验收标准（33 条发现全部根治 + 4 条 PlayMode 失败根治 + 全量门禁通过）不
因此受阻；同步这些 README 是后续一个独立、篇幅相当的收尾任务，如实记录为本轮未完成事项，不
在本次提交范围内声称已做。

## 四、全量门禁

见会话汇报正文的门禁汇总表（`check.ps1` 输出）。
