# 第十方深度审核核实跟进（codex 第八轮，基线 `3224ca1`）

基线：`3224ca1`（main，v1.5.0）。W8 提交（游戏侧复核 1.5.0 两处边界并根治，随本轮一并纳入审计范围
起点）：`8927398`。审计报告本体见本目录 `AUDIT_REPORT.md`/`doc-code-matrix.md`/`doc-evidence.md`/
`validation.md`/`unity-validation.md`/`evidence/`（codex 原文，已 `git add` 归档，未改写）。

修复由三个并行 Sonnet 5 子 agent 按领域分工执行（核心侧 WA、表现/引擎侧 WB、交付与文档 WC），
判断记录留存于 `C:\Users\1\AppData\Local\Temp\claude\D--workespace-ws-game\
f1d8941d-33da-4643-9759-6533f7c7ff79\scratchpad\audit8\{WA,WB,WC}.md`（未随仓库提交，属会话
临时材料）。主会话（本 agent）在三份判断记录基础上补齐 3 处遗留（ADR-0017 加载合同口径修订记录、
`.github/workflows/release.yml` 恢复附件措辞收紧、WB 范围偏差核对），独立核实全部条目、统一
分领域提交、执行全量门禁、撰写本文档。提交：

| 提交 | hash | 范围 |
|---|---|---|
| A（核心侧） | `e86be6a` | `core/carriers/gobj/**`、`core/carriers/item/**`、`core/foundation/save_system/**`、`core/gameplay/assembly/tests/CR150_01_...`、`architecture/05、07、10`（CR150-01～04） |
| B（表现/引擎侧） | `6871c3f` | `adapters/unity/**`（`AnimStateFinishRelay` 新增）、`core/rules/**`（`AttackInstanceId` 链路）、`data/_framework/found/found.event_catalog.json`、`core/foundation/event_bus/generated/EventKeys.g.cs`、`presentation/feedback_binder/**`、`architecture/09`（PR150-01～03 + 攻击实例 id 遗留） |
| C（交付与文档） | `930a363` | `toolchain/get_framework.ps1`（PJ150-01 + zip slip）、`toolchain/tests/test_get_framework_path_boundary.py`（新增）、`toolchain/README.md`、`.github/workflows/release.yml`（本文档说明的注释收紧）、`CHANGELOG.md`、`architecture/02`、`adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md`、`architecture/adr/0017`、落地计划、`followup-2026-09-08c.md`（计数更正） |
| D（归档） | `9ec43f6` | `architecture/落地计划/audit-3224ca1-20260908/` 整目录（codex 报告原文与证据） |

## 核实方法说明

本文档"验收"列标注的测试文件均已在提交 A/B/C/D 落地的工作树上重新执行：`dotnet build Core.sln
-c Release` 0 警告 0 错误，`dotnet test`（六项目）全绿——Foundation 657/657（基线 655 + CR150-03
新增 2）、Numbers 106/106（不变）、Carriers 314/314（基线 305 + CR150-02/03/04 共 3 + 边界测试
`GameObjectFactoryOriginKeyTests` 6）、Rules 404/404（基线 401 + `PR150_04_AttackInstanceIdTests`
3）、PresentationCommon 491/491（基线 486 + `HitFrameSyncPolicyTests`/`FeedbackBinderHitFrameSyncTests`
共 5）、Gameplay 464/464（基线 460 + `CR150_01_EquipmentSharedAuraCrossMapTests` 4）。四次提交各自
的 pre-commit 快速门禁（`-SkipUnity -Quick`，20 步全部 PASS/SKIP，0 FAIL）全部一次通过，无需
临时修复编译错误；提交全部落地后再跑一次全量门禁（见下"验收"节，含 Unity EditMode/PlayMode 与
两种独立版冒烟、消费方演练）。

## 核实表（8 条：CR150-01～04、PR150-01～03、PJ150-01 + PR140-04 遗留"攻击实例 id"）

| 编号 | 判断 | 复现 | 修复位置 | 验收 | 判断记录 |
|---|---|---|---|---|---|
| CR150-01 | 成立（P2） | `CR150_01_EquipmentSharedAuraCrossMapTests`（新增 4 条：两件装备共享光环、装备+套装门槛共享、`AllowMultiSourceTiming=true` 各自独立句柄、重复 `EnterMap` 幂等） | `core/carriers/item/core/EquipmentHost.cs`（`ReapplyGrants`/`ReapplyAuraGrants`/`ReapplySetBonuses` 改用调用开始前的惰性快照，不再在循环内实时查询 `HasAura`） | `dotnet test` Tests.Gameplay 全绿（本项 4 条）+ 原有 `CR140_02_EquipmentAuraMapClearTests` 2 条回归绿 | 两件装备共享同一 `aura_def` 时，跨图重放循环内实时查 `HasAura` 会被第一件重放的结果污染，第二件误判"从未失效"复用早已失效的旧句柄；改用调用前快照后，是否合并句柄完全交由 `IEffectSink.ApplyAura`/`AuraHost` 既有合并策略决定，与初次装备路径对称 |
| CR150-02 | 成立（P2） | `GobjPendingLootPersistenceTests::PartialChest_CrossMapReentry_NewEntityReattachesOldPending_DeliversRemainderExactlyOnce`（新增） | `core/carriers/gobj/contracts/GameObjectEntity.cs`（新增 `OriginKey`）、`core/carriers/gobj/core/GameObjectFactory.cs`（`Spawn` 新增可选 `originKey`，未传时按"地图+位置+模板"自动合成稳定键）、`GameObjectHost.cs`（余量台账改按 `PendingLootKey` 记账）、`GobjPendingLootPersistable.cs`（字段名 `gobjInstanceId`→`originKey`） | `dotnet test` Tests.Carriers 全绿（本项 1 条 + 自查边界测试 `GameObjectFactoryOriginKeyTests` 新增 6 条，覆盖负坐标/极端量级/幂等/位置区分）+ 原有 3 条回归 | Partial 余量此前按运行期实体 id 记账，`SpawnHost.UnloadMap`/`OnMapEnter` 重入分配新 id 后旧账目永久失联；改按稳定摆放位置键记账。自查发现坐标往返精度格式化在负坐标/科学计数法下会使合成键触发 `Id` 格式非法，已改用固定 6 位小数+符号前缀字母编码并补测试 |
| CR150-03 | 成立（P2） | `SaveSystemTests::Load_OldSaveMissingRegisteredSection_StillCallsLoadWithJsonNull_ClearingResidualState` + `GobjPendingLootPersistenceTests::SaveSystemLoad_OldSaveMissingPendingLootSection_ClearsExistingResidual`（新增，组合场景） | `core/foundation/save_system/contracts/IPersistable.cs`（新增默认接口方法 `KeepStateWhenSectionMissing`，默认 `false`）、`core/foundation/save_system/core/SaveSystem.cs`（`ComputeReadOrder`/`Load` 改按全部已注册段计算，缺失段默认调用一次 `Load(JsonNull.Instance)`） | `dotnet test` Tests.Foundation 全绿（本项 2 条）+ 全部既有 `SaveSystemTests` 与六项目回归绿；已审计全部 16 个既有 `IPersistable` 实现，均已在 `Load` 开头显式处理 `JsonNull`，无一需要覆盖为 `KeepStateWhenSectionMissing=true` | 正式推翻 `core/foundation/save_system/README.md` 原判断记录第 3 条"缺失段跳过不调用"；`ComputeReadOrder` 此前只对文档里实际存在的段安排 `Load`，已注册但旧档缺失的段永远不被调用，运行期残留状态（如未交付掉落余量）跨读档污染下一次存档。新增成员用默认接口实现，纯加法，既有实现无需改动即可编译（已核对，无需额外兼容层） |
| CR150-04 | 成立（P2） | `GobjPendingLootPersistenceTests::GatherNode_FullInventory_RejectDoesNotCommitCooldown_PartialCommitsAndPersistsRemainder`（新增） | `GameObjectHost.cs`（`GatherNode`/`GatherNodeReject`/`GatherNodePartial`，与 `OpenChest` 系列对称）、`GobjOptions.cs`（新增独立字段 `GatherNodeLootPolicy`，默认 `Partial`） | `dotnet test` Tests.Carriers 全绿（本项 1 条，覆盖完全失败/部分成功/成功交付/同冷却内重试补发四种场景）+ 原有回归 | 满包采集此前无条件先提交冷却再无视 `AddItem` 交付结果，满包时奖励整份丢失且冷却已提交，腾出空间后同冷却窗口内仍无法重试；改为只有真正交付了至少一部分才提交 `used_at`，未交付部分按 CR150-02 的稳定键记入 `_pendingLoot` |
| PR150-01 | 成立（P2） | `EquipmentVisualReplayTests::CreateView_SpriteKind_EquipmentAlreadyEquippedBeforeViewExisted_ReplaysAfterBind_RequestsOverrideLayerResource`（新增） | `UnityViewFactory.CreateView`（对新建"生物"分类 view 在装备快照重放前先调用一次 `Bind(entityId)`） | Unity PlayMode 全绿（本项计入 231 总数） | `UnitySpriteView`/`SpriteViewBase` 把 `EntityId` 赋值推迟到显式 `Bind`，重放发生在 `Bind` 之前时 `OnEvent` 按 `unitId.Equals(EntityId)` 过滤丢弃事件，帽子/武器类覆盖资源从未被请求加载；三个 `Bind` 实现（`SpriteViewBase`/`UnityModelView`/`NullView`）对同一 entityId 重复调用均幂等，不产生副作用 |
| PR150-02 | 成立（P2） | `ModelViewTests::PlayAutoExitClip_TransitionsBetweenManualUpdates_BeforeAnyRendererTick_StillRaisesFinishedExactlyOnce`（新增）+ `...RetriggeredWhileStillPlaying_DoesNotFireSpuriousFinishedOnRetrigger`（回归） | `UnityRenderer3D.cs` 新增 `AnimStateFinishRelay : StateMachineBehaviour` + `OnAnimStateEvent`，按 `normalizedTime>=1` 的 Exit 事件驱动完成检测，与既有采样判定取 OR；占位控制器预置到 idle/attack/cast/hit/test_autoexit 五个状态 | Unity PlayMode 全绿（本项计入 231 总数） | 完成检测原本完全依赖 `Tick()` 外部轮询采样，若自动过渡整个落在两次 `Tick()` 之间，采样永远看不到过渡本身，完成事件永久漏发。首版实现"必须先见配对 Enter 才采信 Exit"在 Enter/Exit 落在同一次 `Update` 内时会漏判，改用 `normalizedTime>=1` 判据后一并解决（同状态重触发打断产生的陈旧 Exit 天然被过滤） |
| PR150-03 | 成立（P2） | `UnityRenderer3DTests::AttachToSocket_MissingOnPlaceholder_PreservesIntent_ReattachesAfterSwapToModelWithSocket`（新增）+ `...StillMissingAfterSwap_KeepsIntentWithoutThrowing`（回归） | `UnityRenderer3D.AttachToSocket`：挂点不存在时也先登记挂接意图，只有物理挂接这一步依挂点是否可解析分支 | Unity PlayMode 全绿（本项计入 231 总数） | 查不到目标挂点时原逻辑直接 `return`，连挂接意图都不登记，后续换装/挂点补上后无记录可重放；改为无论挂点当前是否存在都先登记，挂点暂不可用时子实例保持在调用前父物体下、记一次去重诊断，不抛异常 |
| PJ150-01 | 成立（P0，安全） | `toolchain/tests/test_get_framework_path_boundary.py::test_malicious_lock_version_rejected_before_any_write`（新增，8 组恶意 `version` 参数化用例，修复前脚本上会失败——越界写/删除发生、exit 0） | `toolchain/get_framework.ps1`：新增 `$LockVersionFormatPattern` 对 `-AllowVersionMismatch` 放行的 `$lockObj.version` 做格式校验；新增 `Test-IsStrictSubPath` 校验落地目录必须是 `-Target` 严格子目录；`Expand-Archive` 改为逐条目 `ZipFile` 手动解压并同样过 `Test-IsStrictSubPath`（顺带根治 zip slip，并修正衍生 bug：本仓库 zip 目录条目 `FullName` 以 `\` 结尾，原按 `Name` 是否为空判目录条目的写法会漏判） | `pytest toolchain/tests/test_get_framework_path_boundary.py -q` 12 passed；`pytest toolchain/tests -q`（全量）60 passed（含原有 48 条）；覆盖正常路径回归、真实 `dist/ws-game-1.5.0.zip` 回归、合法版本不一致落地、8 组恶意 `lock.version`、zip slip 条目，均非 0 退出且不越界写/删除 | 锁文件 `version` 字段经 `-AllowVersionMismatch` 放行后未经格式校验即拼进落地目录路径，构造 `x/../../outside_sentinel` 可逃出 `-Target` 并触发 `Remove-Item -Recurse -Force`，六个 DLL 哈希校验不覆盖该字段，脚本 exit 0 收尾；`-Target`/`-LockPath` 本身来自可信调用方，未额外校验——真正攻击面是随 zip/lock 一起搬运、可能被篡改的数据字段（锁文件 `version`、zip 条目路径），已覆盖 |
| 攻击实例 id（PR140-04 遗留） | 成立（P2） | `PR150_04_AttackInstanceIdTests`（新增 3 条：AoE 共享实例 id、两次攻击各自不同实例 id、引导多跳各自不同实例 id）+ `HitFrameSyncPolicyTests` 新增 2 条 + `FeedbackBinderHitFrameSyncTests::HitFrameSyncRule_TwoDistinctAttacksSameWindow_EachReleasesOnlyItsOwnBatch`（新增） | `core/rules/common/contracts/EffectContext.cs`（新增 `AttackInstanceId`）→ `CastPipeline.ExecuteEffectsOnly`（每次调用分配新实例 id）→ `EffectDispatcher.ApplyDamageOrHeal`（转发，见下"范围偏差核对"）→ `Resolver.Resolve`→`CombatDamageDealtEvent`/`CombatHealDoneEvent`；`HitFrameSyncPolicy` 批次匹配由 `ReferenceEquals` 改为 `Equals`；`FeedbackBinder.ResolveHitFrameBatchToken` 优先用实例 id 装箱值当 token，无该字段时退回旧窗口合批兜底 | `dotnet test` Tests.Rules（本项 3 条）+ Tests.PresentationCommon（本项 5 条）全绿；`gen_event_constants.py --check` 通过（90 个常量，事件目录新增字段 doc 注释同步） | PR140-04 修复时"同一攻击者是否还有未释放批次"这一时序代理无法区分极短 GCD 内的两次独立攻击，会被误合并；`CastPipeline` 一次调用固定分配新实例 id 后按值合批彻底消除这一简化。**范围偏差**：任务书未列出 `EffectDispatcher.cs`，经代码追踪确认该文件 `ApplyDamageOrHeal` 重建 `outbound EffectContext` 时不转发会使整条修复形同虚设，已如实报告并核对——改动仅 1 行转发 + 6 行判断记录注释，未碰其它逻辑，判定合理，予以保留 |

## 上轮九项复核（对照 `followup-2026-09-08c.md` 核实表）

上轮（codex 第七轮，基线 `c86bfa9`）核实的 CR140-01～03、PR140-01～04、PJ140-01～02 共 9 项，
本轮六项目/Unity 全量回归（含各自当时新增的锁定测试）均保持全绿，未受本轮改动影响；本轮未修改
上述 9 项对应的核心逻辑文件之外的部分，逐项确认与上轮判断一致，不重复展开推导：

| 旧项 | 上轮结论 | 本轮跟进 |
|---|---|---|
| CR140-01 | 已根治（chest Reject/Partial 双协议 + 存读档持久化） | 本轮未涉及；`GobjPendingLootPersistenceTests` 既有回归全绿，CR150-02/04 在其基础上扩展余量记账键与 `GatherNode` 对称协议，未改变 chest 侧既有行为 |
| CR140-02 | 已根治（跨图 `EnterMap` 重放光环） | CR150-01 在同一 `ReapplyGrants` 路径内根治"共享光环重放期间实时查询污染判断"这一新发现的相邻缺陷，`CR140_02_EquipmentAuraMapClearTests` 2 条回归绿 |
| CR140-03 | 已根治（跨图传送携带已解析结果） | 本轮未涉及，无回归 |
| PR140-01 | 已根治（Blob 影子贴地） | 本轮未涉及，无回归 |
| PR140-02 | 已根治（socket 子实例/阴影模式跨模型替换保留） | 本轮未涉及，无回归 |
| PR140-03 | 已根治（自动过渡完成事件漏发，采样侧判据加固） | PR150-02 从"外部轮询采样"补一条"引擎动画状态机事件驱动"的并行判定路径，两者取 OR，不替代/不削弱 PR140-03 的采样侧修复 |
| PR140-04 | 已根治（同批多目标原子释放，已知局限：极短 GCD 连续攻击误合并） | 本轮"攻击实例 id"条目正是根治该已知局限，PR140-04 锁定的 4+1 条测试全绿未受影响 |
| PJ140-01 | 已根治（`HitFrameReached`/`ViewKind.GameObject` 源码兼容层） | 本轮未涉及，`Pj140_01LegacyConsumerCompatTests` 回归绿 |
| PJ140-02 | 已根治（Release 附件缺失分支阻断而非全量重建混批） | 本轮 PJ150-01 是同一脚本文件（`release.yml` 消费的 `get_framework.ps1`）里的另一处独立安全问题（锁文件版本越界路径），未触碰 PJ140-02 修复的 `release.yml` 判定逻辑本身；本文档"文档应怎样更新"一节额外收紧了 `release.yml` 一处与"确定性构建同批证明"相关的人工恢复指引措辞（见下） |

## 文档应怎样更新（5 项处理）

| 位置 | 原表述/缺口 | 处理 | 落点/处理人 |
|---|---|---|---|
| ADR-0017 决策 1 vs `architecture/02_引擎适配层.md` §1.7"禁止隐式加载" | 决策 1 原文字面上容易读成"任何首次引用触发的加载都禁止"，与具体引擎适配层资源加载器缓存未命中时同步完成一次加载表面冲突 | `architecture/02_引擎适配层.md` §1.7 正文与变更记录表已改写口径（消费方不得绕过 `IResourceLoader` 自行读取资源，`IResourceLoader` 自身同步/异步完成加载不违反本条款）；`adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md` 对应行同批修订；ADR-0017 追加一段修订记录（本轮由主会话补录，正文不出现引擎名，落地位置改用"具体引擎适配层"泛称） | WC（02/README 正文）+ 主会话（ADR-0017 修订记录），提交 C |
| `architecture/落地计划/落地方案与分阶段计划.md`"能力边界与未默认接入能力索引"表 | 原 21 行两栏（能力/说明），部分行内嵌真实换行符导致渲染断行，分类粒度不足 | 改为四栏（分类｜能力｜当前锚点｜边界说明），按"已实现且默认接线/已实现未默认接线/未实现/明确非目标"四类逐项归类，21 行按需拆分为 24 行，修复两处物理断行 | WC，提交 C |
| `CHANGELOG.md`"旧档缺段为空"表述（1.5.0 条目摘要 + `GobjPendingLootPersistable` 新增说明） | 原写死"旧存档没有该段视为空表/按空表处理"，与本轮 CR150-03（旧档缺段时台账根本不会被清空）矛盾 | 两处均改为中性措辞，指向 `architecture/10_存档与持久化.md` 当前记载为权威来源，不在 CHANGELOG 重复展开具体语义 | WC，提交 C |
| `architecture/落地计划/audit-c86bfa9-20260908/followup-2026-09-08c.md:84` PASS/SKIP 计数 | 原写"23 步（18 PASS + 5 SKIP）"，与下方逐行明细表（20 条 PASS + 1 条 IL2CPP×3 SKIP=3 步 SKIP）及末尾汇总"23 步（20 PASS + 3 SKIP）"自相矛盾 | 按原始明细表/末尾汇总更正为"23 步（20 PASS + 3 SKIP，SKIP 为 3 项 IL2CPP 未传 `-Il2cpp`）"，并注明更正依据 | WC，提交 C |
| `.github/workflows/release.yml` 人工恢复附件指引（"zip 缺失但 lock/tgz 仍在"阻断分支的判断记录注释与 Guidance 提示） | WC 检索确认"same HEAD + SyncOnly 可证明同批"字面表述本身未出现在 README/CHANGELOG/落地计划三个文件中，但注释里"本机用 `-SyncOnly -Dist <ver> -Zip` 针对这个 tag 重新打一份 zip 后手动补传"这条人工指引本身隐含"同一提交 + 跑了 SyncOnly 就足够手动补传"，与文件头部"确定性构建产物字节随本机 checkout 路径变化"这条既有判断记录的结论矛盾（同一提交在不同机器上重新构建，DLL 字节/哈希并不必然相同） | 判断记录注释追加约 5 行：明确"同一 tag 指向的提交 + 跑了 SyncOnly"本身不能当作"与既有附件同批"的证明，手动补传前必须核对新产出的 zip/lock/三个 `.tgz` 与仍保留的既有附件之间整套字节/哈希互相一致，核对不一致不得混用；只改 `#` 注释，未改 `Write-Host` 运行期输出文本与判定逻辑本身（`exit 1` 阻断行为不变） | 主会话，提交 C |

## 验收

前台实跑 `powershell -ExecutionPolicy Bypass -File check.ps1`（不加 `-SkipUnity`/`-Quick`，执行前
`tasklist | findstr /i "Unity.exe"` 已确认 Unity 编辑器本体未占用），提交 A/B/C/D 全部落地后执行：

| 步骤 | 结果 | 用时(s) | 详情 |
|---|---|---|---|
| 门禁自检：`Test-NativeExitCode` | PASS | 0.3 | — |
| `dotnet build Core.sln -c Release` | PASS | 2.2 | 0 警告 0 错误 |
| `dotnet test`（六项目） | PASS | 4.6 | Foundation 657/657、Numbers 106/106、Carriers 314/314、Rules 404/404、PresentationCommon 491/491、Gameplay 464/464 |
| `validate_data.py`（合并根） | PASS | 3.1 | 60 tables/285 records/0 errors/0 warnings/1 override |
| `validate_data.py --data-root data/_framework` | PASS | 1.3 | 5 tables/124 records/0 errors/1 warning（缺 l10n 表，既有已知情况） |
| `gen_event_constants.py --check` | PASS | 0.1 | 一致（90 个常量，含本轮 `attackInstanceId` doc 注释同步） |
| `gen_placeholder_assets.py --check` | PASS | 0.1 | 97 个占位文件，拷贝 0/跳过 97/删除 0，无漂移 |
| `import_assets.py check --dataset _sample` | PASS | 0.1 | 0 问题 |
| `pytest toolchain/tests -q` | PASS | 7.0 | 60 passed（含 PJ150-01 新增 12 条） |
| 禁用词扫描：全仓库不出现具体游戏代号 | PASS | 8.5 | 0 命中 |
| 禁用词扫描：architecture 正文技术名（immunity 例外） | PASS | 0.0 | 0 真实命中 |
| 版本一致性 | PASS | 0.1 | VERSION=1.5.0，两个 `package.json`、`packages-lock.json`、CHANGELOG.md 一致（本轮未提交版本号，见"关于 VERSION"说明） |
| `build.ps1 -SkipTests`（同步 DLL） | PASS | 3.3 | 六个 DLL 哈希核对通过 |
| 包清单一致性 | PASS | 8.1 | 三个 npm 包 version=1.5.0 一致，`npm pack --dry-run` 清单不含排除项，adapter.unity 含 model/anim 占位资产与生成器 |
| Unity 编译检查 | PASS | 18.0 | 退出码 0 |
| Unity EditMode 测试 | PASS | 8.8 | total=53 passed=53 failed=0 |
| Unity PlayMode 测试 | PASS | 40.0 | total=231 passed=231 failed=0（基线 226 + 本轮新增 5：PR150-01×1、PR150-02×2、PR150-03×2） |
| 独立版构建 + `-gf-smoke` 冒烟（连续模式） | PASS | 23.5 | — |
| 独立版 `-gf-smoke-discrete` 冒烟（离散模式） | PASS | 3.4 | — |
| IL2CPP 独立版构建/冒烟 ×3 | SKIP | 0 | 未传 `-Il2cpp`（任务未要求） |
| 消费方演练（`toolchain/consumer_smoke.ps1`） | PASS | 120.7 | — |

**汇总：23 步（20 PASS + 3 SKIP），0 FAIL，总用时 253.2s。** 门禁执行时工作树仅有本文档与
`CHANGELOG.md`/落地计划新增小节（step 3，见下）待提交，门禁本身不产生任何额外改动；本文档与
step 3 两处改动提交落地后 `git status --short` 为空。

**关于 VERSION**：本轮任务范围明确"不跑 `build.ps1 -Release`"，因此不在本轮改动 `VERSION`
文件；`VERSION` 与 `CHANGELOG.md` 顶部 `[1.6.0]` 条目暂不一致，与上一轮（1.5.0 变更记录合并
提交同样先于实际发版）惯例一致——版本号写回、`git tag`、发布 zip/lock 均在后续独立的
`build.ps1 -Release` 流程中一次性完成，不属于本次审核修复与文档整合的范围。

自检复核：

- 全仓库禁用具体游戏代号扫描（`check.ps1` 内置扫描步骤，同一禁用词）：0 命中。
- `architecture/00～14` 与 `architecture/adr/` 正文技术名 grep（`unity|c#|csharp|dotnet|\.net|
  nunit|python|newtonsoft|il2cpp|powershell|github|verdaccio|npm`）：仅 `immunity` 既有豁免误报，
  0 真实命中（含本轮新增的 ADR-0017 修订记录、`architecture/02` 口径修订）。
