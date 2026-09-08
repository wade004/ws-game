# 第九方深度审核核实跟进（codex 第七轮，基线 `c86bfa9`）

基线：`c86bfa9`（main，v1.4.0）。审计报告本体见本目录 `AUDIT_REPORT.md`/`doc-code-matrix.md`/
`validation.md`/`evidence/`（codex 原文，已 `git add` 归档，未改写）。

修复由三个并行 Sonnet 5 子 agent 按领域分工执行（核心侧 WA、表现/Unity 侧 WB、交付与文档 WC），
判断记录留存于 `C:\Users\1\AppData\Local\Temp\claude\D--workespace-ws-game\
f1d8941d-33da-4643-9759-6533f7c7ff79\scratchpad\audit7\{WA,WB,WC}.md`（未随仓库提交，属会话
临时材料）。主会话（本 agent）在三份判断记录基础上补齐两处遗留（宝箱 Partial 余量存读档持久化、
新 View 初始装备外观重放，见下"第 0 步"两行），独立核实全部条目、统一分领域提交、执行全量门禁、
撰写本文档。提交：

| 提交 | hash | 范围 |
|---|---|---|
| A（核心侧） | `f4a6054` | `core/**`（gobj/item/gameplay assembly 及其测试，含新增 `GobjPendingLootPersistable`/`GobjPendingLootPersistenceTests`） |
| B（表现/引擎侧） | `5ea78b4` | `presentation/**`、`adapters/unity/**`、`games/_template/**`、`data/_sample/display/**`、`architecture/adr/0017`，含第 0 步"新 View 初始装备外观重放"（`EquipmentSnapshotResolver`/`EquipmentVisualSource.ReplayEquippedForUnit`/`UnityViewFactory.ReplayEquippedVisuals` 及三处装配根接线、`EquipmentVisualReplayTests`） |
| C（交付与文档） | `1e1ff58` | `.github/workflows/release.yml`、`CHANGELOG.md`（1.4.0 条目收窄）、`architecture/11`、落地计划 |
| D（归档） | `0c29f79` | `architecture/落地计划/audit-c86bfa9-20260908/` 整目录（codex 报告原文与证据） |

## 核实方法说明

本文档"验收"列标注的测试文件均已在提交 A/B/C/D 落地的工作树上重新执行：`dotnet build Core.sln
-c Release` 0 警告 0 错误，`dotnet test`（六项目）全绿——Foundation 655/655、Numbers 106/106、
Carriers 305/305（基线 302 + 本轮第 0 步新增 3）、Rules 401/401、PresentationCommon 486/486
（不变，第 0 步"新 View 重放"验证走 Unity PlayMode，不新增 dotnet 侧用例）、Gameplay 460/460
（不变）。全量 `check.ps1`（不加 `-SkipUnity`/`-Quick`）在提交前先跑通一次定位并修复了一处命名空间
编译错误（`UnityViewFactory.cs` 内 `Presentation.Render.EquipmentVisualSource` 在
`Adapter.Unity.Presentation` 命名空间下被解析成本文件自身命名空间的相对路径，改为依赖已有的
`using Presentation.Render;` 直接写 `EquipmentVisualSource` 解决），随后四次提交各自的
pre-commit 快速门禁（`-SkipUnity -Quick`）与提交后一次全量门禁均通过，详见下"验收"节。

## 核实表（9 条：CR140-01～03、PR140-01～04、PJ140-01～02 + 第 0 步 2 条遗留补齐）

| 编号 | 判断 | 复现 | 修复位置 | 验收 | 判断记录 |
|---|---|---|---|---|---|
| CR140-01 | 成立（P1） | `CR140_01_ChestOpenTransactionTests`（新增，真实 `InventoryHost`+`GameObjectHost`+stub loot，5 条用例：Reject 事务回滚、Reject 完全满包 Theory、Partial 保留/补发、Partial 完全满包） | `core/carriers/gobj/core/GameObjectHost.cs`（`OpenChest`/`OpenChestReject`/`OpenChestPartial`/`DeliverPendingChestLoot`/`RollbackAdd`）、`core/carriers/gobj/contracts/GobjOptions.cs`（新增 `GobjLootDeliveryPolicy`/`ChestLootPolicy`） | `dotnet test` Tests.Carriers 全绿（含本项 5 条） | chest 开箱改走仿 `LootHost.PickUp` 的 Reject/Partial 双协议；Reject 整批事务回滚不标记 `open_state`；Partial 逐堆按实际落地量交付，未交付部分记入 `_pendingChestLoot`，标记 `open_state=true` 防止重复 roll，下次交互只补发剩余 |
| CR140-02 | 成立（P2） | `CR140_02_EquipmentAuraMapClearTests`（新增，真实 `GameplayAssembly.Carriers` + `WorldSim.ClearAll` + 重加实体 + `EnterMap`，2 条：普通装备光环、套装门槛加成光环，均验证跨图丢失→恢复→重复调用不叠加→真正卸装光环仍消失） | `core/carriers/item/core/EquipmentHost.cs`（新增 `ReapplyGrants`/`ReapplyAuraGrants`/`ReapplySetBonuses`）、`core/gameplay/assembly/GameplayAssembly.cs`（`EnterMap` 新增调用 `Carriers.Equipment.ReapplyGrants`） | `dotnet test` Tests.Gameplay/Tests.Carriers 全绿；手工临时注释 `ReapplyGrants` 调用复测确认两条新测试会失败（证明是真实回归测试） | 跨图 `World.ClearAll` 后 `EquipmentHost._equipped` 台账不受影响但运行期 Aura 已随实体销毁清空；`EnterMap`（post-load 统一钩子）补齐重放步骤，幂等靠 `IAuraQuery.HasAura` 核实 |
| CR140-03 | 成立（P2） | `GameplayAssemblyGobjTeleportInteractionTests.InteractWithTeleporterGobj_CrossMap_CustomResolverPosition_IsPreserved`（新增） | `core/carriers/common/contracts/Events.cs`（`GobjInteractedEvent` 新增 `ResolvedTeleportTarget`）、`core/carriers/gobj/core/GameObjectHost.cs`（`DoTeleport`/`ExecuteKindBehavior`/`Interact` 携带已解析结果）、`core/gameplay/assembly/GameplayAssembly.cs`（拆出 `ApplyResolvedTeleport`，`gobj.interacted` 订阅改用已解析结果） | `dotnet test` Tests.Gameplay 全绿（含既有 4 条回归 + 本项新增 1 条） | 跨图自定义 resolver 结果此前只随事件携带原始 ref，`TeleportUnit` 用装配根内置 resolver 重新解析并覆盖；改为携带已解析结果，`gobj.interacted` 订阅不再二次解析 |
| PR140-01 | 成立（P2） | `ModelViewTests.SetShadow_Blob_FacesCameraOnGroundPlane_PositionUnaffectedByHeight`（新增） | `UnityRenderer3D.cs`：`SetShadow`/`SetPlacement`/新增 `ApplyBlobShadowTransform` | Unity PlayMode 全绿（本项计入 225 总数） | Blob 影子固定按 X 轴转 90° 是旧 XZ 地面平面约定遗留写法，当前 XY 地面约定下把 Quad 转成侧立薄片；改为世界旋转钉死为面向相机、位置只依赖锚点根世界位置 |
| PR140-02 | 成立（P2） | `UnityRenderer3DTests.CreateModelInstance_MissingThenAvailable_AttachVisual_RestoresSocketChildAfterSwap`/`...RestoresShadowModeAfterSwap`（新增） | `UnityRenderer3D.cs`：`AttachVisual`（socket 子实例暂存/挂回）、`AttachToSocket`/`Detach`/`DestroyModelInstance`（登记表维护）、新增 `ApplyShadowCastingMode` | Unity PlayMode 全绿 | 异步模型替换销毁旧 `VisualRoot` 时把挂在其下的 socket 子实例一并销毁、也不重放投影阴影状态；改为替换前暂存登记表，新内容就位后逐条挂回/重新应用 |
| PR140-03 | 成立（P2） | `ModelViewTests.PlayAutoExitClip_AnimatorTransitionsBeforeDetectionFrame_StillRaisesFinishedExactlyOnce`（新增，配套占位资产 `test_autoexit` 状态/剪辑） | `UnityRenderer3D.cs`：`IsAnimatorStateFinished` 加 `everEnteredTarget` 记账 | Unity PlayMode 全绿；EditMode/PlayMode 既有用例（`AnimReplayAndFinishEndToEndTests` 等）未受影响 | 自动过渡的开始与结束都发生在两次检测帧之间时，完成判断只看"当前状态"会被过渡后的新状态覆盖，完成事件永久漏发；新增"曾经确认进入过目标状态、现在稳定停留在别的状态"分支同样判定完成 |
| PR140-04 | 成立（P2） | `HitFrameSyncPolicyTests.WaitForHitFrame_SameBatchToken_HitFrameReleasesEntireBatchAtomically_InEnqueueOrder` 等 4 条（新增）；`FeedbackBinderHitFrameSyncTests.HitFrameSyncRule_AoeMultipleTargets_...`（改造） | `HitFrameSyncPolicy.cs`：新增 `batchToken` 重载 + `BatchReleased` 事件；`FeedbackBinder.cs`：`_hitFrameBatchTokenByAttacker` | `dotnet test` Tests.PresentationCommon 全绿（486，含本项新增 5 条）；旧测 `MultipleAttacks_SameEntity_DoNotCrossTalk` 逐字保留未改仍绿 | 同一攻击批次多目标此前按攻击者 FIFO 每次只释放一条，剩余目标错到下一命中帧或超时；改为按 `batchToken` 原子释放整批。已知局限：同一攻击者上一批未释放前发起第二次独立攻击会被误合并（核心事件契约补施法/攻击实例 id 前的已知简化，仍优于改动前） |
| PJ140-01 | 成立（P2） | `Pj140_01LegacyConsumerCompatTests`（新增，1.3 风格消费代码编译+运行） | `ICharacterRig.cs`：默认接口实现加回 `[Obsolete] HitFrameReached`（转发 `IHitFrameEmitter`）；`ViewKind.cs`：加回 `[Obsolete] GameObject = Gobj` | `dotnet test` Tests.PresentationCommon 全绿；Unity EditMode/PlayMode 全量编译通过 | 1.4.0 直接删除/改名两处公共契约成员，违反 11 号文档"改名/删除需保留过时别名/默认实现至少一个 MINOR 周期"判据（1.3 消费方对真实 1.4.0 DLL 编译复现 `CS0117`/`CS1061`）；恢复兼容层，计划下一个 MAJOR 随旧签名一并移除 |
| PJ140-02 | 成立（P2） | 四组实跑（1 组真实只读 v1.4.0 + 3 组构造场景，见 `scratchpad/audit7/test1~4_*.ps1`） | `.github/workflows/release.yml`："Check for existing release assets" 步骤新增 lock 段 `git_commit` 校验 + zip 缺失但其余附件存在时 `exit 1` 阻断 | PowerShell AST 语法验证 6 处 `run:` 块全部 `OK`；`pytest toolchain/tests -q` 48 passed；真实 v1.4.0 只读校验判定 `skip=true` 不触碰已发布附件 | zip 缺失、旧 lock/tgz 仍在时此前会全量重建只传新 zip，形成"新 zip + 旧附件"且无法证明同源；现只有五个附件全部缺失才继续全量重建，其余情形直接阻断并给出指引 |
| 第 0 步-1（宝箱 Partial 余量持久化） | 成立，本轮补齐 | `GobjPendingLootPersistenceTests`（新增 3 条：满包 Partial 开箱→Save→新宿主 Load→腾出空间→再交互恰好补发一次；`JsonNull` 旧存档兼容；无 pending 时空往返） | `core/carriers/gobj/core/GameObjectHost.cs`（新增 `PendingChestLootSnapshot`/`RestorePendingChestLoot`）、新增 `core/carriers/gobj/core/GobjPendingLootPersistable.cs`（`world.gobj_pending_loot` 段，字段名 `pending_loot`）、`core/gameplay/assembly/GameplayAssembly.cs`（`RegisterPersistables` 注册） | `dotnet test` Tests.Carriers 全绿（本项 3 条） | CR140-01 修复引入的 `_pendingChestLoot` 此前是纯进程内字典，存读档会丢；新增可选存档段（未注册进 `SaveSections.KnownOrder`，理由同 `DroppedLootPersistable` 既有惯例——本轮新引入的世界附属段，10 号文档尚未收录，改表需先出 ADR），旧存档无该字段（`JsonNull`）视为空表，不抛异常 |
| 第 0 步-2（新 View 初始装备外观重放） | 成立，本轮补齐 | `EquipmentVisualReplayTests`（新增 2 条，PlayMode：装备发生在 View 创建之前、全程不发布任何 `item.equipped` 事件仍能重放；未装配重放能力时行为与改动前一致） | 新增 `presentation/render/contracts/EquipmentSnapshotResolver.cs`（`EquippedItemRef` + 窄契约委托）、`presentation/render/core/EquipmentVisualSource.cs`（新增 `ReplayEquippedForUnit`）、`adapters/unity/.../UnityViewFactory.cs`（新增 `ReplayEquippedVisuals`，`CreateView` 对"生物"分类新 View 调用）、三处生产装配根（`GameFoundationBootstrap`/`FrameworkResidentHost`/`games/_template.GameBootstrap`）接入 `Carriers.Equipment.GetAllEquippedInstances` 作为查询来源 | Unity PlayMode 全绿（本项计入 225 总数，基线 223） | `EquipmentVisualSource` 此前只靠 `item.equipped` 事件累积外观表，跨图新 View（装备发生在别的地图/别的时间点）与 `InventoryHost.InjectInstance`（存档恢复，不发 `item.added`）两条路径都不重放；`ReplayEquippedForUnit` 直接从查询结果拿 `TemplateId`，不经过 `item.added` 这一跳，两条路径均被同一处修复覆盖——只要 View 创建晚于装备状态就位（两种场景均满足），即会在 `CreateView` 时合成一次 `ItemEquippedEvent` 调用 `view.OnEvent` 补齐外观。`InventoryHost.InjectInstance` 本身仍不发 `item.added`（这半个缺口影响的是其它依赖该事件的消费方，非本次修复范围，属 `core/carriers/item` 内部问题） |

## 旧 1.3.0 17 项复核（对照 `AUDIT_REPORT.md` 报告表确认）

以下表格逐字取自 `AUDIT_REPORT.md`"旧 1.3.0 17 项复核"一节（编号沿用该报告），本轮核实：报告
给出的"部分修复"两行（CR130-01→CR140-01、CR130-05→CR140-03）均已随本轮对应编号根治（见上表），
其余判断与本轮改动范围不冲突（本轮未触碰对应机制），确认与报告原判一致，不重复展开推导。

| 旧项 | 1.4 复核结论（报告原文） | 本轮跟进 |
|---|---|---|
| CR130-01 | 购买/任务事务按矩阵已有 batch/Partial 路径；CR140-01 的 GameObject chest 直发绕过该协议，不能把旧项的局部修复扩展到 chest。 | 已根治（CR140-01，含第 0 步存读档持久化补齐） |
| CR130-02 | RewardDispatcher/技能来源的局部保存路径已有实现；完整游戏磁盘存档纵切未由本轮证明。 | 本轮未涉及，无需跟进 |
| CR130-03/04 | charge、Proc ICD、school lock 与 channel 时间族按矩阵复核，未把局部时间族结论泛化为所有装配边界。 | 本轮未涉及，无需跟进 |
| CR130-05 | **部分修复**：sameMap/null 的二次传送路径已修，crossMap 仍把 ref 交回内置 resolver，形成 CR140-03。 | 已根治（CR140-03） |
| PR130-01～08 | 坐标、完成事件、动画登记、命中帧、资源降级、Vfx、装备反查和影子贴地均有局部实现；1.4 新路径差异分别形成 PR140-01～04，默认接线与 Unity 范围按矩阵保留。 | PR140-01～04 已根治（见上表） |
| PJ130-01～04 | 附件检测、占位资源交付、注册表 PID 防护和版本契约按矩阵复核；新 zip 混批与 1.4 公共契约破坏分别形成 PJ140-02/PJ140-01，正常包检查不替代发布分支验证。 | PJ140-01/02 已根治（见上表） |

## 文档漂移处理

| 位置 | 原表述/缺口 | 处理 | 落点 |
|---|---|---|---|
| 包 README（`adapters/unity/Packages/.../README.md`） | 顶部仍写旧 `(X,height,Y)`/XZ 平面约定与"缺资源 throw"，与 :603 附近"已降级"表述矛盾 | 按当前实现（XY 地面平面、`TryLoadModelSync`、占位降级）统一改写，新增 PR140-01/02/03 效果一节 | 提交 B |
| ADR-0017 决策 1 vs `UnityRenderer3D` 直接 `Resources.Load` | 决策规定 renderer 不隐式加载，代码里 `TryResolvePrefab` 与 `FinishModelLoad` 各自独立实现资源读取 | 新增 `UnityResourceLoader.TryLoadModelSync` 统一缓存/同步解析入口，`UnityRenderer3D` 改为委托；ADR-0017 追加两段修订记录 | 提交 B |
| `core/carriers/gobj/README.md`/`core/gameplay/assembly/README.md` | 传送文档"唯一权威结果／不再独立解析"表述超出实现（跨图仍被内置 resolver 覆盖） | 随 CR140-03 修复同步核对，两处模块 README 已与代码一致（`GobjInteractedEvent.ResolvedTeleportTarget` 携带已解析结果，不再二次解析） | 提交 A |
| `UnitySpriteView` 缺 `equipVisual` 入口 | `UnityViewFactory.CreateView` 的 model 分支透传 `equipVisualByItemInstanceId`，sprite 分支不传 | 构造函数新增 `equipVisualByItemInstanceId`，转发给 `SpriteViewBase`；`UnityViewFactory` sprite 分支接入同一份 `_equipVisuals`；新增 `data/_sample/display/display.equip_visual.json` 一行供回归测试 | 提交 B |
| `EquipmentVisualSource` 初始库存/跨图重放缺口 | AUDIT_REPORT 记录"声明初始库存限制…跨图新 View 也不重放既有装备事件" | 本轮第 0 步根治（见上表），已从"未实现"变为"已实现" | 提交 B |
| CHANGELOG.md 1.4.0 条目 | "17 项全部根治"/"不破坏既有编译"表述过满 | 收窄为"逐条核实并在当时验证范围内根治"，新增独立条目记录 `HitFrameReached`/`ViewKind.GameObject` 两处源码兼容性破坏与本版本恢复的兼容层 | 提交 C（1.4.0 条目文字），新增 1.5.0 条目见下节 |
| `architecture/11_工程规范与测试.md` §7 | 只讲"新增强制成员"一种不兼容变更，未覆盖"改名/删除"方向 | 追加一句：公开枚举成员改名/删除、接口成员删除同属不兼容变更，需保留过时别名/默认实现至少一个 MINOR 周期 | 提交 C |
| 落地计划 `ISkillHost.FindUnits` 能力索引行 | "基于形状的范围目标查询整体默认不可用" 过度泛化 | 改为"`FindUnits` 便利 API 未实现；`target.chain` 形状查询已默认接通"，区分两个不同入口 | 提交 C |
| 落地计划 离散模式过期处理行 | "离散模式下召唤物到期与掉落过期计时器不推进" 笼统表述 | 拆分：召唤物侧整条逻辑跳过（duration 冻结）；掉落物侧只是清理动作跳过，绝对时钟仍在推进（"已过期"判定仍生效） | 提交 C |
| 落地计划 | 缺武器 Swing/Impact 特效能力缺口记录 | 新增一行：能力已定义（`WeaponStyleResolver`/`PresentationAssembly`），但生产代码无调用，接线仍需游戏层自行完成（未修复，仅记录） | 提交 C |
| 落地计划 | 缺装备外观 sprite 路线 `equipVisual` 入口能力缺口记录 | WC 报告时"实测仍缺，未见改动"；本轮第 0 步随 PR140 文档漂移根治一并解决（见上表"第 0 步-2"），落地计划该行状态随之从"缺口"变为"已接通"，本文档在此确认 | 提交 B（代码）+ 本文档确认 |

## 验收

前台实跑 `powershell -ExecutionPolicy Bypass -File check.ps1`（不加 `-SkipUnity`/`-Quick`，
`tasklist` 已确认 Unity 编辑器本体未占用）：commit A/B/C/D 落地前先跑通一次定位并修复命名空间
编译错误（见上"核实方法说明"），随后四次提交各自的 pre-commit 快速门禁（`-SkipUnity -Quick`，
20 步全部 PASS/SKIP，0 FAIL）全部通过；提交全部落地后再跑一次全量门禁，**23 步（20 PASS + 3
SKIP，SKIP 为 3 项 IL2CPP 未传 `-Il2cpp`），0 FAIL**（与下表逐行计数及末尾汇总一致，按原始
执行日志更正——此前版本误写为"18 PASS + 5 SKIP"，与下表 20 条 PASS 行、末尾"23 步（20 PASS +
3 SKIP）"汇总自相矛盾，见 `architecture/落地计划/audit-3224ca1-20260908/doc-evidence.md`
"历史证据隔离"一节）：

| 步骤 | 结果 | 用时(s) | 详情 |
|---|---|---|---|
| 门禁自检：`Test-NativeExitCode` | PASS | 0.3 | — |
| `dotnet build Core.sln -c Release` | PASS | 1.1 | 0 警告 0 错误 |
| `dotnet test`（六项目） | PASS | 3.4 | Foundation 655/655、Numbers 106/106、Carriers 305/305、Rules 401/401、PresentationCommon 486/486、Gameplay 460/460 |
| `validate_data.py`（合并根） | PASS | 1.3 | 60 tables/285 records/0 errors/0 warnings/1 override |
| `validate_data.py --data-root data/_framework` | PASS | 1.2 | 5 tables/124 records/0 errors/1 warning（缺 l10n 表，既有已知情况） |
| `gen_event_constants.py --check` | PASS | 0.1 | 一致 |
| `gen_placeholder_assets.py --check` | PASS | 0.1 | 92/92 |
| `import_assets.py check --dataset _sample` | PASS | 0.1 | 0 问题 |
| `pytest toolchain/tests -q` | PASS | 1.6 | 48 passed |
| 禁用词扫描：全仓库不出现具体游戏代号 | PASS | 7.9 | 0 命中 |
| 禁用词扫描：architecture 正文技术名（immunity 例外） | PASS | 0.0 | 0 真实命中 |
| 版本一致性 | PASS | 0.0 | VERSION=1.4.0，两个 `package.json`、`packages-lock.json`、CHANGELOG.md 一致 |
| `build.ps1 -SkipTests`（同步 DLL） | PASS | 1.9 | 六个 DLL 哈希核对通过 |
| 包清单一致性 | PASS | 7.3 | 三个 npm 包 version=1.4.0 一致，`npm pack --dry-run` 清单不含排除项，adapter.unity 含 model/anim 占位资产与生成器 |
| Unity 编译检查 | PASS | 12.8 | 退出码 0 |
| Unity EditMode 测试 | PASS | 8.9 | total=53 passed=53 failed=0 |
| Unity PlayMode 测试 | PASS | 39.8 | total=225 passed=225 failed=0（基线 223 + 第 0 步-2 新增 2） |
| 独立版构建 + `-gf-smoke` 冒烟（连续模式） | PASS | 27.8 | — |
| 独立版 `-gf-smoke-discrete` 冒烟（离散模式） | PASS | 3.5 | — |
| IL2CPP 独立版构建/冒烟 ×3 | SKIP | 0 | 未传 `-Il2cpp`（任务未要求） |
| 消费方演练（`toolchain/consumer_smoke.ps1`） | PASS | 123.8 | — |

**汇总：23 步（20 PASS + 3 SKIP），0 FAIL，总用时 243.3s。** 门禁执行后 `git status --short` 为空。

- 全仓库禁用具体游戏代号扫描：0 命中。
- `architecture/00～14` 与 `architecture/adr/` 正文技术名 grep（`unity|c#|csharp|dotnet|\.net|nunit|python|newtonsoft|il2cpp|powershell|github|verdaccio|npm`）：仅 `immunity` 一处既有豁免误报，0 真实命中。
- `ViewKind.GameObject`（PJ140-01 恢复的过时别名）：`git grep` 命中代码文件 `presentation/common/contracts/ViewKind.cs`（`[Obsolete]` 标记）、`presentation/render/tests/Pj140_01LegacyConsumerCompatTests.cs`，以及模块 README（`presentation/common/README.md`）、`CHANGELOG.md`、`architecture/落地计划/**`（含本文档）——后三类允许出现具体技术名/过时成员名，不受约束；核对 `architecture/00～14` 与 `architecture/adr/` 正文，均未出现该词。
