# 第八方深度审核核实跟进（codex 第六轮，基线 `5c444f1`）

基线：`5c444f1`（main，v1.3.0）。审计报告本体见本目录 `AUDIT_REPORT.md`/`doc-code-matrix.md`/
`validation.md`/`working-tree-drift.md` 与 `evidence/`/`repro/`（codex 原文，已 `git add` 归档，
未改写）。此前 W7（同一基线上、审计交付前）已独立修复 model 路线动画完成回调等三项游戏侧复核问题，
提交 `9f5695d`/`360ff5f`，CHANGELOG 当时写作 `## [1.3.1]`（未发布，本轮改名为 `## [1.4.0]`，见下）。

修复由三个并行 Sonnet 5 子 agent 按领域分工执行（核心侧 WA、表现/Unity 侧 WB、交付与文档 WC），
主会话（本 agent）在三份判断记录报告基础上独立核实全部条目、统一分领域提交、执行全量门禁、
自检发现并根治一处架构文档技术名模糊误报、撰写本文档。提交：

| 提交 | hash | 范围 |
|---|---|---|
| A（核心侧） | `7b9c913` | `core/**`（economy/loot/skill/carriers/gobj/gameplay assembly 及其测试）、`architecture/06`、对应 README |
| B（表现/引擎侧） | `e98f6a6` | `presentation/**`、`adapters/unity/**`、`games/_template/**`、`data/_sample/display/**`、`architecture/02、09、adr/0017`、对应 README |
| C（交付与文档） | `14840f6` | `.github/workflows/release.yml`、`build.ps1`、`check.ps1`、`toolchain/**`、`README.md`、`architecture/01、03、11`、落地计划、`core/foundation/*/schema/README.md` |
| D（归档） | `0b9072e` | `architecture/落地计划/audit-5c444f1-20260908/` 整目录（codex 报告原文与证据） |
| E（主会话自检根治，本文档所在提交另计） | `88a0778` | `ViewKind.GameObject` 改名为 `ViewKind.Gobj`（见下"自检"节） |

## 核实方法说明

本文档"复现"列标注的测试文件均已在提交 A/B/C/D/E 落地的工作树上重新执行：`dotnet build Core.sln
-c Release` 0 警告 0 错误，`dotnet test`（六项目）全绿——Foundation 655/655、Numbers 106/106、
Carriers 297/297、Rules 401/401、PresentationCommon 478/478、Gameplay 457/457（`validation.md`
记录的 5c444f1 基线为 654/106/297/390/470/452，合计 2369；当前合计 2394，注：本仓库在
`5c444f1`→本轮起点 `360ff5f` 之间还有 W7 及若干与本轮审计无关的既有提交，测试数增量不能全部
归因于本轮 17 条修复，具体新增用例数以下表逐条"验收"列为准）。全量 `check.ps1`（不加
`-SkipUnity`/`-Quick`）执行两次（提交 A~D 落地后一次、提交 E 落地后一次），均 **23 步（20 PASS +
3 SKIP），0 FAIL**，详见下"验收"节。

## 核实表（17 条：PR130-01～08、PJ130-01～04、CR130-01～05）

| 编号 | 判断 | 复现 | 修复位置 | 验收 | 判断记录 |
|---|---|---|---|---|---|
| CR130-01 | 成立（P1） | `AuditEconomyProbeTests`（Economy→Quest）+ 本轮新增 Loot 侧同款复现 | `core/gameplay/economy/core/EconomyHost.cs`（`Buy`/`Sell`）、`core/gameplay/loot/core/LootHost.cs`（`PickUpReject`）改走 `IBatchableInventoryHost.BeginBatch()` 事务 | `CR130_01_BuyFailureTransactionTests`、`CR130_01_PickUpRejectTransactionTests`（真实 `InventoryHost`+`QuestHost`/`LootHost`+`EventBus`，验证失败后库存/任务进度/已派发事件数量均回到调用前） | 购买/拾取失败此前"先落地再补偿"，补偿窗口内已派发的 `item.added`/`item.removed` 事件不可撤销，可能被其它任务的 `consumeOnProgress` 目标误当新获得；改为事务，失败时 `Dispose` 整体回滚（数据与已缓存事件一并撤销） |
| CR130-02 | 成立（P1） | `AuditCoreMechanismProbeTests.RewardDispatcher_QuestSourceSkill_IsNotPersistedAsPermanent`（断言改为正确期望值后先复现失败） | `core/rules/skill/core/SkillHost.cs`（新增 `LearnSkill(Id,Id,Id,bool permanent)`/`ForgetAllPermanentGrants`/`GetPermanentlyKnownSkills` 改用逐来源分类）、`KnownSkillsPersistable.cs`、`core/carriers/assembly/CarriersAssembly.cs`（装备来源显式传 `permanent: false`） | `KnownSkillsPersistableTests.cs` 新增两条（新宿主读同一快照恢复奖励技能；同宿主 C09 替换语义正确覆盖奖励来源） | 一次性奖励技能来源此前只靠"来源是否等于哨兵"判断是否永久，导致读档结果依宿主生命周期分叉；改为逐来源记录"是否永久"。修复中途经回归测试实测暴露一处相邻缺口：`ReplacePermanentlyKnownSkills` 撤销步骤对非哨兵来源授予的永久技能是 no-op，新增 `ForgetAllPermanentGrants` 一并根治 |
| CR130-03 | 成立（P2） | `CooldownTracker_RescaledCharges_UsesUnscaledNextRecharge`（断言改造）+ 本轮新增 ICD/学派锁复现 | `core/rules/skill/core/CooldownTracker.cs`（`AdvanceCharges` 补折算）、`ProcHost.cs`（新增 `RescaleAll`，`OnEvent` 写入 ICD 前折算）、`CastPipeline.cs`（`Interrupt`/`RescaleAll` 补学派锁折算）、`SkillHost.OnTimeModelRescaled` 转发 | `CR130_03_TimeModelRescaleGapsTests.cs`（5 条：充能第二窗口 1 条、ICD 写入折算/既有存量换算各 1 条、学派锁写入折算/既有存量换算各 1 条） | 混合时间模式的"当下折算 + 切换换算"两层机制此前只覆盖冷却首颗，充能耗尽后开下一窗口、Proc ICD、施法学派锁三处未跟进，均按同款 `_currentFactor` 机制补齐 |
| CR130-04 | 成立（P2） | `CastPipeline_ChannelHalfSecond_IntervalOne_UpdateOneHasOneTick`（断言改为期望 0 次越界多算） | `core/rules/skill/core/CastPipeline.cs`（`AdvanceOne` 把 `channelDt` 截断为 `Min(dt, Remaining)` 再计入 `TickAccumulator`） | `CR130_04_ChannelBoundaryTests.cs`（4 条：单区间越界不多算、跨多 interval 恰好计数、被打断不结算、与 CR130-03 rescale 协同不多算） | 引导结束后的 `dt` 余量此前整段计入周期累加器，越界多结算一跳；改为与 `AuraHost` 周期效果尾跳同款的 `Min(dt,Remaining)` 处理 |
| CR130-05 | 成立（P2） | 报告描述复现（同图自定义结果被内置覆盖 / resolver 返回 null 仍传送），本轮新增可执行复现 | `core/carriers/common/contracts/Events.cs`（`GobjInteractedEvent` 新增 `TeleportTargetRef`）、`core/carriers/gobj/core/GameObjectHost.cs`（`Interact` 携带该引用）、`core/gameplay/assembly/GameplayAssembly.cs`（监听改为只消费该引用，删除 `HandleGobjTeleporterInteracted`） | `GameplayAssemblyGobjTeleportInteractionTests.cs` 新增 3 条（同图自定义结果不被覆盖、resolver 返回 null 不回退内置、未注入自定义 resolver 时跨图仍生效） | `GameObjectHost.DoTeleport` 是唯一权威解析并消费自定义 `TeleportResolver` 的地方；`GameplayAssembly` 此前反查模板 + 用自己的默认 resolver 独立重新解析一遍，构成双重消费，可能覆盖/绕过自定义结果 |
| PR130-01 | 成立（P1） | `UnityRenderer3DTests.SetPlacement_ModelWorldPosition_MatchesCameraWorldToScreen_ForVariousHeightsAndFacings`（新增） | `UnityRenderer3D.cs`（`ModelInstance` 拆分 Root/VisualRoot，`SetPlacement`/`CreateModelInstance`/`AttachVisual` 改造） | 同左 + `SetPlacement_ModelAndSpriteVisualPosition_ProjectToSameScreenPoint`（新增）+ `ModelViewTests.SyncPose_AppliesHeightToModelRoot`（改写） | model 放置与相机投影此前不共用同一 2.5D 平面；height 改只写 VisualRoot，与 sprite 路线的偏移语义对齐 |
| PR130-02 | 成立（已在本轮之前修复） | 不适用（`9f5695d`/`360ff5f` 已修，见 CHANGELOG 已废弃的 `[1.3.1]` 条目） | 不适用 | 不适用 | model Attack 无完成回调、状态机无法可靠回落与重播——该问题已由 W7（本轮之前、同基线上）独立发现并修复，本轮不重复处理，仅在此确认结论未变 |
| PR130-03 | 成立（P1） | `WeaponClipRegistrationTests.Attack_WeaponStyleClip_NotPreRegistered_DoesNotThrow_DegradesToSingleFrame`（新增，真实 `display.weapon_style.sample_sword` + 真实 `UnityFrameAnimPlayer`） | `UnityViewFactory.cs`（新增 `EnsureSpriteClipRegistered`/`RequestWeaponClipUpgrade`，`playClip` 委托调用 `Play` 前先保证已登记） | 同左 + `Cast_WeaponStyleCastOverrideClip_NotPreRegistered_DoesNotThrow`（新增） | 默认动画登记键与武器资源 id 不一致，sample swing 无法播放；改为按需登记后再播放 |
| PR130-04 | 成立（P2） | `FeedbackBinderHitFrameSyncTests.HitFrameSyncRule_TwoRulesMatchSameEvent_BothReleaseTogetherOnSingleHitFrame_NoTimeoutNeeded`（新增，双规则命中同一事件） | `FeedbackBinder.cs`（`OnEvent` 把同一次事件命中的全部 `sync: hit_frame` 规则动作合并成一个批次，只调用一次 `WaitForHitFrame`） | 同左；`HitFrameSyncPolicyTests.MultipleAttacks_SameEntity_DoNotCrossTalk` 保持绿且未放宽 | 同一命中批次的多个反馈规则此前各自拆开等待，第二条起可能错过命中帧、延迟到超时；不同来源（如 AoE 各目标）仍各自独立批次，不破坏既有"多次攻击不串扰"FIFO 语义 |
| PR130-05 | 成立（P2） | `UnityRenderer3DTests.CreateModelInstance_UnknownModelId_DoesNotThrow_CreatesPlaceholderAndLogsDiagnosticOnce`（改写自原 `..._ThrowsWithResolvedPathInMessage`） | `UnityRenderer3D.cs`（`CreateModelInstance` 缺资源不抛，落地占位 + 发起 `LoadAsync`，成功后 `AttachVisual` 原地替换） | 同左 + `CreateModelInstance_UnknownModelId_PlaceholderCanBePlacedAndDestroyed_LikeAnyOtherInstance`（新增） | model 缺资源路径违反 ADR-0017 加载与降级契约（此前直接抛异常，未走占位 + 异步替换这条既定路线） |
| PR130-06 | 成立（P2） | 无独立复现测试（静态链路缺口：三处装配根均只把 `renderer3D` 传给 `UnityViewFactory`，未传给 `PresentationAssembly`） | 三处装配根（`GameFoundationBootstrap`/`FrameworkResidentHost`/`games/_template.GameBootstrap`）的 `new PresentationAssembly(...)` 调用补 `renderer3D` 参数 | 全量 PlayMode 回归（218/218）+ 人工核对三处调用点参数列表 | `PresentationAssembly` 构造函数早已声明可选参数 `IRenderer3D? renderer3D`，纯粹是三处装配根忘记转发，属接线遗漏，非契约缺陷 |
| PR130-07 | 成立（P2） | `EquipVisualSocketClearTests.SocketAttach_Equip_AttachesChildModel_Unequip_DestroysIt_RepeatUnequip_IsIdempotent`（新增，真实 socket 挂点子节点计数） | `UnityModelView.cs`（`_appliedEquipVisualsByItemInstanceId` 可逆索引，按 `Mode` 精确清理）+ 新增 `EquipmentVisualSource`（`presentation/render/core`，订阅 `item.added`/`item.equipped`/`item.unequipped` 维护活字典）+ `UnityViewFactory` 新增 `equipVisualByItemInstanceId` 构造参数 + 三处装配根接线 | 同左 + `SlotMesh_Equip_ThenUnequip_DoesNotThrow_AndClearsSlot`（新增） | model 换装卸载此前按逻辑 slot 清 socket，可能残留挂件；改为按实例反查精确清理。同时补齐 `display.equip_visual` 默认 provider（此前该能力边界记录为"未实现"），新增示例数据 `data/_sample/display/display.equip_visual.json` |
| PR130-08 | 成立（P2） | `UnityRenderer3DTests.SetShadow_Blob_WorldPosition_DoesNotMoveWithHeight`（新增） | 与 PR130-01 同一次改动（height 只写 `VisualRoot`，`BlobShadow` 挂在不含 height 的锚点根下） | 同左 | model 影子此前挂在 root 下，height 会把影子一起抬离地面 |
| PJ130-01 | 成立（P2） | 本地实测：`dist/ws-game-1.3.0.zip` 内 DLL 与 `.lock` 哈希逐字段一致；用新增逻辑对本地 zip 复现"提取 DLL 生成 lock"与"抽取内嵌 tgz"两条路径，均与仓库真实产物逐字节/哈希一致，混批风险成立 | `.github/workflows/release.yml`（新增"Check for existing release assets"步骤，比对已有 zip 内 `MANIFEST.txt` 的 `git_commit` 与当前 checkout；新增"Repair missing assets from existing zip"步骤，zip 存在且 commit 校验通过时直接从 zip 提取补齐 lock/tgz；两个全量重建步骤的 `if:` 收紧为额外要求 `zip_missing == 'true'`） | YAML 解析通过；6 个新增/改动 `run:` 脚本体 ASCII 扫描 + PowerShell AST 解析通过；功能级复现两条路径均与真实产物一致；未执行远端 GitHub Actions 运行（超出本地权限范围，已注明） | Release fallback 此前一旦缺任意附件即整体重建全部五个文件，托管运行器确定性构建路径不同会产生不同字节，若只用于补全"已存在 zip 缺 lock/tgz"的情形就会把新构建批次的 lock/tgz 配到旧构建批次的 zip 上；改为优先从已验证 commit 一致的旧 zip 原地补齐，只有 zip 本身也缺失才全量重建 |
| PJ130-02 | 成立（P2） | `build.ps1` 原 Copy-DistDir 调用列表未包含 `adapters/unity/Assets/Resources/GameFoundation/{models,anim_clips}` 与 `Assets/Editor/GeneratePlaceholderModelAssets.cs`（提交在 Packages 目录之外，历史上从未随 dist/zip/tgz 分发） | `build.ps1` 新增"5.055"节，把这批资产复制进 dist 内适配层包副本的 `Runtime/Resources/GameFoundation/{models,anim_clips}/` 与 `Editor/`；`check.ps1` 包清单一致性步骤新增 7 个必需路径后缀核对 | 隔离脚本级验证 + 真实端到端（`dist\1.3.1-dryrun\...\com.gamefoundation.adapter.unity\` 由 194 files 变为 208 files，差值 14 精确匹配）+ `npm pack --dry-run`/`tar -tzf`/`ZipFile` 三层核对全部命中；验收完成后已清理 dryrun 产物 | dist 遗漏 model/anim 占位资源与生成器，导致消费方无法拿到这批默认占位资产与可重复运行的生成脚本 |
| PJ130-03 | 成立（P2） | 本机实测真实进程：正常起停、身份核验通过后停止、同端口无关进程（`python -m http.server 4873`）拒绝停止（退出码 1，进程未受影响）、幂等空转，四场景全部实测通过（非仅静态审查） | `toolchain/registry/start_registry.ps1` 新增 `Test-VerdaccioProcessIdentity`（可执行文件名必须是 `node`、`CommandLine` 含 `verdaccio` 与配置路径锚点、启动时间不早于 PID 文件写入时间三项核验）+ `Write-VerdaccioIdentityMeta`/`Get-VerdaccioIdentityAnchors`；`-Stop` 两条路径（PID 文件/端口兜底）均先过身份核验 | 见"复现"列，四场景真实进程实测全部通过 | registry stop 此前拿到 PID（尤其端口兜底路径）不核验身份直接强杀，可能误杀 PID 复用或无关监听进程；改为停止前核验，不通过则整体非零退出、不触碰该进程、不删除 PID 文件 |
| PJ130-04 | 成立（P2） | `CharacterRigHitFrameSourceTests.RegisterRig_RigWithoutHitFrameEmitter_DoesNotThrow_AndHasRigStaysTrue`（新增，模拟只实现旧版 `ICharacterRig` 的外部实现） | `ICharacterRig.cs` 移除 `HitFrameReached`；新增 `IHitFrameEmitter.cs`；`SpriteCharacterRig`/`ModelCharacterRig` 同时实现两接口；`CharacterRigHitFrameSource.RegisterRig` 按 `is IHitFrameEmitter` 探测 | 同左 + `UnregisterRig_RigWithoutHitFrameEmitter_DoesNotThrow`（新增）；`ICharacterRig` 全部既有实现/消费方编译通过（PresentationCommon 478/478、Unity EditMode 53/53、PlayMode 218/218） | 1.3.0（MINOR 发布号）新增了 `ICharacterRig` 强制事件成员，构成 [11_工程规范与测试.md](../../11_工程规范与测试.md) 定义的 MAJOR 级契约签名变化，发布号语义不一致；改为可选接口 `IHitFrameEmitter` 承载，`ICharacterRig` 恢复不含该强制成员，自定义实现不再被迫编译失败 |

## 旧 1.1.0 十三项复核表（对照 codex 报告表确认）

以下表格逐字取自 `AUDIT_REPORT.md`"旧 1.1.0 十三项复核表（不预判结论）"一节（编号沿用该报告——
实指第七方审核 `5e779c6` 基线的 13 项主发现，报告原标题如此，未改写）。本轮核实：该表给出的
"部分修复，遗漏见 CR130-0x"三行（FR-01→CR130-03、GP26-03→CR130-05、FR-03→CR130-04）均已随本轮
对应编号根治（见上表），其余"已闭合"判断与本轮改动范围不冲突（本轮未触碰对应机制），确认与报告
原判一致，不重复展开推导。

| 旧编号 | 1.3.0 复核状态 | 当前证据/结论 | 本轮跟进 |
|---|---|---|---|
| GP26-01 | 当前本体/Quest 事务边界已源码闭合；Economy 新链路见 CR130-01 | 不把旧奖励路径结论复制到当前；由 CR130-01 单独覆盖 Economy→Quest | 已根治（CR130-01） |
| GP26-02 | 已闭合 | 套装 Aura 来源计数修复成立，不重报 | 无需跟进 |
| GP26-03 | 默认路径已修；custom resolver 回归见 CR130-05 | 只对新双消费链路计当前发现 | 已根治（CR130-05） |
| FR-01 | 部分修复；混合家族遗漏见 CR130-03 | 旧"只换算通用计时器"不原样重报 | 已根治（CR130-03） |
| FR-02 | 已闭合 | 0 充能恢复已修，不重报 | 无需跟进 |
| FR-03 | 已闭合 | Aura 周期尾跳已修；引导周期仍见 CR130-04 | 已根治（CR130-04） |
| FR-04 | 已闭合 | 读档 rating cache 问题已修；技能来源恢复是 CR130-02 的新边界 | 无需跟进（CR130-02 单独覆盖） |
| FR-05 | 已闭合 | 技能嵌套坏字段校验已修，不重报 | 无需跟进 |
| U01 | 静态修复成立；本轮未执行对应 Unity 场景 | 音乐选源已改正，不重报当前缺陷 | 本轮全量门禁含 Unity PlayMode 218/218，间接覆盖，未见回归 |
| U02 | 静态修复成立；本轮未执行对应 Unity 场景 | SFX 自然结束回收已改正，不重报当前缺陷 | 同上 |
| U03 | 静态修复成立；本轮机制探针已执行并 PASS，Unity 场景未执行 | FrameAnim 跨帧事件已改正，不重报当前缺陷 | 同上 |
| U04 | 静态修复成立；本轮未执行对应 Unity 场景 | AnimRoot、renderer 高度/颜色链已接回；model 影子高度另见 PR130-08 | 已根治（PR130-08 覆盖 model 侧影子高度） |
| U05 | 旧缺附件检测已改为全 5 项；新混批 hash 风险见 PJ130-01 | 不重报旧"漏独立 tgz 完成判定" | 已根治（PJ130-01） |

## 文档漂移与链接处理

以下文档漂移与失效链接均已随提交 A/B/C 落地（架构正文 00～14/adr 采用 12 §5"勘误："格式，版本号
不变、ADR 列"—"；`architecture/落地计划/`、`toolchain/`、`README.md` 等允许出现技术名的文件不受
该格式约束）：

| 类别 | 位置 | 原表述 | 改后口径 |
|---|---|---|---|
| 能力索引 | `README.md:9` | "…共 19 项未默认接入能力…" | 删除硬编码"19 项"，改为"3D 渲染/装备外观/武器动画/关键帧反馈四项已实现、默认接线见口味配置开关（ADR-0017）；框架实现/默认接线/真实运行证据三分记录；其余条目数与源码锚点以落地计划该表为准" |
| ADR 计数 | `README.md:16/228` | "adr/（16 条 ADR）" | "adr/（17 条 ADR）" |
| IRenderer3D 历史表述 | 落地计划 :82（技术映射表） | "本项目 `IRenderer3D` 声明为未实现降级" | 勘误：W6/ADR-0017 起已改为真实实现；本项目参考游戏仍用 sprite 型是口味选择而非未实现 |
| IRenderer3D 历史表述 | 落地计划 :469/:478（阶段 4 计划/验收标准） | 同上 | 标注"历史记录，本阶段规划时的目标范围"，指向 W6/ADR-0017 现状 |
| 明确不做项 | 落地计划 :831-838、:957-960 | 整段"本轮收边不实现 IRenderer3D…"/"model 型 3D 渲染路径…不实现" | 段首加"（历史记录…以下三段文字保留为原貌，不代表当前状态）"，正文保留不改写 |
| FindUnits 表述 | 落地计划 :1152 | "未注入 `ISpatialQuery` 时恒返回空列表" | 勘误：当前实现无条件返回空（不判断是否注入），行号更正为 :169/:175 |
| L5 只读表述 | `architecture/01_分层与依赖.md` 图解 :206 | "L5 表现层 Presentation（只读/只订阅事件，经 L-1 执行绘制）" | "L5 表现层 Presentation（查询只读+窄命令例外，经 L-1 执行绘制）"，图例补一句意图路径说明 |
| L5 只读表述 | `architecture/03_运行时骨架.md` :137/:321 | "…这是'L5 对其余层只读、只订阅事件'规则…"/"…且明确 View 只读" | 改为"查询只读，改变状态经窄意图入口提交"，说明 `onPlaybackFinished` 只是窄命令例外之一，不是唯一反向通路 |
| 版本判据 | `architecture/11_工程规范与测试.md` :156 附近 | 无此说明 | 新增一句："框架自身实现的接口也算契约，新增强制成员即 MAJOR；可选能力用独立接口扩展"（对应本轮 PJ130-04 的修复依据） |
| Unity README 历史缺口 | `adapters/.../README.md:309/322` | 旧降级/退订缺口描述 | 已随 WB 报告新增三节（三维放置坐标换算/资源缺失降级/影子、生产装配根共享同一个 renderer3D、装备外观接线步骤）改写为现状 |
| gameObject 术语 | `architecture/09_表现层.md` :67 | ViewKind 建议枚举值含 `gameObject`（与具体引擎核心类型同名，模糊技术名误报） | 改为 `gobj`（本轮主会话自检根治，见下"自检"节，与 `EntityKinds.Gobj` 既有中立缩写一致）；提交 E |

失效链接（13 处，均已用文件系统实际存在性核对通过）：

| 文件 | 位置 | 修复 |
|---|---|---|
| 落地计划 | :26、:1160（原 :1155）、:1176（原 :1171） | `adr/0017-...` → `../adr/0017-...`（三处） |
| `core/foundation/display_info/schema/README.md` | :3、:4 | `../../../architecture` → `../../../../architecture`（两处） |
| `core/foundation/save_system/schema/save_slot_meta.md` | :3、:4、:5、:29 | 同上（四处） |
| `core/foundation/scene_router/schema/README.md` | :3、:4 | 同上（两处） |
| `core/foundation/sim_loop/schema/README.md` | :4、:6 | 同上（两处） |

新增自动化检查：`toolchain/tests/test_markdown_relative_links.py`——枚举 `git ls-files` 下全部被
跟踪 `*.md`（排除目录段精确匹配 `audit-*` 的路径），提取相对链接并做文件存在性校验，用
`git check-ignore --no-index` 排除指向 `.gitignore` 覆盖路径（一次性审计日志等）的链接；随
`python -m pytest toolchain/tests -q` 一并跑（无需改 `check.ps1`）。

## 自检：`ViewKind.GameObject` 改名为 `ViewKind.Gobj`（提交 `88a0778`）

按任务书自检要求对 `architecture/00～14` 与 `architecture/adr/` 正文做技术名 grep
（`unity|c#|csharp|dotnet|\.net|nunit|python|newtonsoft|il2cpp|powershell|github|verdaccio|npm`），
命中且需要判断的只有一处：`architecture/06` 第 168 行 `immunity`（游戏机制免疫字段名，含 `unity`
子串但不是该词本身，`check.ps1` 自身的门禁扫描已用左右非字母边界匹配排除这类误报，属既有已知
豁免，未改动）。

另按任务书特别提示复核了 `gameObject`（`architecture/09` 第 2 节 ViewKind 建议枚举值、
`Presentation.Common.ViewKind` 枚举成员）：该词本身不含 `unity` 子串，未被上述 grep 或
`check.ps1` 自身的门禁扫描捕获，但其拼写与具体引擎的核心类型完全同名，在技术无关的架构文档/
接口正文里构成模糊的技术名误报，不属于"grep 捕获不到就不算问题"的情形。核对生产代码后确认
改名影响面很小（仅 `presentation/common/contracts/EntityKindMapping.cs` 一处消费该枚举成员，
Unity 侧无任何代码引用 `ViewKind.GameObject`），选择直接改名而非登记豁免：枚举成员
`ViewKind.GameObject` → `ViewKind.Gobj`，与 `Core.Foundation.SimLoop.EntityKinds.Gobj`（常量值
`"gobj"`，`core/carriers/gobj` 模块既有的中立缩写）保持一致，逻辑分类语义不变。改动文件：
`presentation/common/contracts/ViewKind.cs`、`EntityKindMapping.cs`、
`presentation/common/tests/EntityKindMappingTests.cs`、
`core/foundation/sim_loop/contracts/EntityKinds.cs`（判断记录注释）、`architecture/09_表现层.md`
（正文 + 变更记录表新增一行）、`presentation/common/README.md`（判断记录新增第 6 条）。验收：
`dotnet build Core.sln -c Release` 0 警告 0 错误；`dotnet test presentation/tests/Tests.PresentationCommon.csproj`
478/478 通过；提交后重跑全量 `check.ps1`（见下）确认 Unity 编译/EditMode/PlayMode 计数不变
（53/53、218/218），DLL 同步后核对 `Runtime/Plugins/Core/Presentation.Common.dll` 与源码树构建
产物字节内容一致，二进制内已不含 `GameObject` 字面量、含 `Gobj` 字面量。

## 验收

全量 `powershell -ExecutionPolicy Bypass -File check.ps1`（不加 `-SkipUnity`/`-Quick`，前台实跑，
`tasklist` 确认 Unity 编辑器本体未占用许可）执行两次：提交 A/B/C/D 落地后一次、提交 E（自检根治）
落地后一次，两次结果一致，**23 步全部完成，0 FAIL**，取后一次（最终状态）逐步结果：

| 步骤 | 结果 | 用时(s) | 详情 |
|---|---|---|---|
| 门禁自检：`Test-NativeExitCode` | PASS | 0.3 | — |
| `dotnet build Core.sln -c Release` | PASS | 2.3 | 0 警告 0 错误 |
| `dotnet test`（六项目） | PASS | 4.5 | Foundation 655/655、Numbers 106/106、Carriers 297/297、Rules 401/401、PresentationCommon 478/478、Gameplay 457/457 |
| `validate_data.py`（合并根） | PASS | 3.1 | 60 tables/284 records/0 errors/0 warnings/1 override |
| `validate_data.py --data-root data/_framework` | PASS | 1.4 | 5 tables/124 records/0 errors/1 warning（缺 l10n 表，既有已知情况） |
| `gen_event_constants.py --check` | PASS | 0.1 | 一致 |
| `gen_placeholder_assets.py --check` | PASS | 0.1 | 92/92 |
| `import_assets.py check --dataset _sample` | PASS | 0.1 | 0 问题 |
| `pytest toolchain/tests -q` | PASS | 1.7 | 48 passed（含新增链接检查用例） |
| 禁用词扫描：全仓库不出现具体游戏代号 | PASS | 8.2 | 0 命中 |
| 禁用词扫描：architecture 正文技术名（immunity 例外） | PASS | 0.0 | 0 真实命中 |
| 版本一致性 | PASS | 0.0 | VERSION=1.3.0，两个 `package.json`、`packages-lock.json`、CHANGELOG.md 一致 |
| `build.ps1 -SkipTests`（同步 DLL） | PASS | 3.4 | 六个 DLL 哈希核对通过 |
| 包清单一致性 | PASS | 7.9 | 三个 npm 包 version=1.3.0 一致，`npm pack --dry-run` 清单不含排除项，adapter.unity 含 model/anim 占位资产与生成器 |
| Unity 编译检查 | PASS | 15.9 | 退出码 0 |
| Unity EditMode 测试 | PASS | 9.0 | total=53 passed=53 failed=0 |
| Unity PlayMode 测试 | PASS | 39.7 | total=218 passed=218 failed=0 |
| 独立版构建 + `-gf-smoke` 冒烟（连续模式） | PASS | 21.7 | — |
| 独立版 `-gf-smoke-discrete` 冒烟（离散模式） | PASS | 3.3 | — |
| IL2CPP 独立版构建/冒烟 ×3 | SKIP | 0 | 未传 `-Il2cpp`（任务未要求） |
| 消费方演练（`toolchain/consumer_smoke.ps1`） | PASS | 113.1 | — |

**汇总：23 步（20 PASS + 3 SKIP），0 FAIL，总用时 235.8s。** 门禁执行后 `git status --short` 为空。

- 全仓库禁用具体游戏代号扫描：0 命中（`git grep` 对该代号的独立核对同样 0 命中，命令本身不在本文档
  复述具体拼写，理由同 `check.ps1` 头注释——避免文档自身触发这条扫描规则）。
- `architecture/00～14` 与 `architecture/adr/` 正文技术名 grep（`unity|c#|csharp|dotnet|\.net|nunit|python|newtonsoft|il2cpp|powershell|github|verdaccio|npm`）：仅 `immunity` 一处既有豁免误报；`gameObject` 已根治（见上"自检"节），不再有模糊命中。
