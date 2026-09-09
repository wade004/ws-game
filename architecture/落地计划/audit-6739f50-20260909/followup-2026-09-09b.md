# 第十五方深度审核核实跟进（codex 第十三轮，基线 `6739f50`）

基线：`6739f50`（main，v1.11.0）。审计报告本体见本目录 `AUDIT_REPORT.md`/`core/`/`docs-project/`/
`presentation/`（codex 原文，`git add` 归档，未改写）。

修复由三个并行 Sonnet 5 子 agent 按领域分工执行（核心侧 WA、表现/引擎侧 WB、文档/能力索引侧
WC），判断记录留存于 `C:\Users\1\AppData\Local\Temp\claude\D--workespace-ws-game\
f1d8941d-33da-4643-9759-6533f7c7ff79\scratchpad\audit13\{WA,WB,WC}.md`（未随仓库提交，属会话临时
材料）。主会话（本 agent）独立核实全部条目（重读改动后源码、重跑测试、交叉核对报告文字与实际
文件清单）、补齐第 0 步遗留（`PlayerVitalsPersistable` 源码兼容重载、能力索引"全资源池当前值默认
持久化"行改判）、统一分领域提交、执行全量门禁、撰写本文档。提交：

| 提交 | hash | 范围 |
|---|---|---|
| A（核心侧） | `f521753` | `core/**`（CORE-111-01、TP-111-01、`PlayerVitalsPersistable` 源码兼容重载、资源/职业文档注释现状化）、`architecture/10` |
| B（表现/引擎侧） | `cc3b69e` | `presentation/**`（UI-111-01，8 个视图模型）、`adapters/**`（NAV-111-01、SPATIAL-111-01）、`adapters/conformance/**`、`architecture/02`/`09` |
| C（文档） | `31c8d08` | `README.md`、`editor/README.md`、`editor/docs/编辑器产品文档.{md,html}`（写入范围外改动，按归属并入，见下"写入范围外改动"）、`architecture/11`、本目录之外的落地计划正文（能力索引新增分类/补齐遗漏行） |
| D（归档） | `ff7cc8a` | `architecture/落地计划/audit-6739f50-20260909/` 整目录（codex 报告原文与证据，不含本文档） |
| 版本/计划文档 | `9f84ad7` | `CHANGELOG.md`（新增 `[1.12.0]`）、`architecture/落地计划/落地方案与分阶段计划.md`（新增"第十五方深度审核修复"小节 + 能力索引"全资源池当前值默认持久化"行移入"已修复历史项"标注"已实现（1.12.0）"） |
| 本文档 | 本提交 | `audit-6739f50-20260909/followup-2026-09-09b.md`（新增） |

A/B/C/版本文档四次提交各自的 pre-commit 快速门禁（`-SkipUnity -Quick`，20 步 PASS/SKIP，0 FAIL）
均一次通过，`dotnet test`（六项目）在提交 A/B/C 时均为 Foundation 689/689、Numbers 107/107、
Carriers 354/354、Rules 404/404、PresentationCommon 507/507、Gameplay 503/503，全绿（提交 A 时
`adapters/**`/`presentation/**` 虽尚未 `git add`，但工作树已是 WA+WB+WC 全部改动叠加后的状态，
门禁针对真实磁盘文件构建/测试，三次提交计数从一开始就已保持一致，非"逐步累加"）。四次提交落地后
`git status --short` 只剩本文档待提交。

## 写入范围外改动

`git status` 初始快照里 `editor/docs/编辑器产品文档.md`/`.html` 两个文件已经处于修改状态，但
WA/WB/WC 三份报告均未提及（WC 报告写入清单明确只有 `README.md`、`editor/README.md`、
`architecture/11`、落地计划正文四个文件；`AUDIT_REPORT.md` 第一行也如实记录"结束检查发现原仓存在
外部并发的未提交 `editor/docs/编辑器产品文档.md` 及对应 `.html` 改动，该改动不属于本审计证据"）。
主会话核实：该改动是把编辑器产品文档对齐框架 1.11.0（消费通道改为 `ws-game.lock` + zip/私服两
通道、语义化版本兼容口径、校验覆盖矩阵等），文档内已自带 "2026-09-09 | v1.1" 变更记录行，内容
完整自洽、不依赖本轮任何未提交的其它改动，按归属并入提交 C（编辑器文档），随附本行说明来源。

## 核实方法说明

本文档"核实"列的判断基于：(1) 独立重读改动后的源码（`git show <commit> -- <path>`，非只读
WA/WB/WC 报告文本），逐一确认修复手法与报告描述一致；(2) 独立核对接口/方法签名变更是否构成源码
级破坏性变更（`PlayerVitalsPersistable` 构造函数收窄一项，额外核对了补回的兼容重载本身能否正确
编译、是否被任何生产装配点误用）；(3) 独立核对判断记录的推理链条（如 TP-111-01"默认拒绝策略"、
NAV-111-01 直线兜底不越权覆盖"禁止切角"）是否站得住；(4) 对报告文字与实际文件清单做交叉核对；
(5) 第 4 步全量门禁前台实跑复核最终测试计数与既有回归测试全绿。

## 核实表：CORE-111-01、TP-111-01、UI-111-01、NAV-111-01、SPATIAL-111-01、文档项、编辑器暂不落地拍板

| 编号 | 判断 | 复现 | 修复位置 | 验收 | 判断记录 |
|---|---|---|---|---|---|
| CORE-111-01（失败/成功读档回滚未覆盖 Health 之外资源池当前值与进出战斗运行态） | **成立（P2）** | `core/gameplay/assembly/tests/CORE_111_FollowupAuditTests.cs`：`CORE_111_01_RollbackRestoresPowerCurrentValuesAndCombatState`（真实 A/B 不同 power 集合，失败读档强制回滚，断言 Mana=30/`IsInCombat=true`、`Advance(1)` 后仍为 30）、`CORE_111_01_SuccessfulLoad_RestoresAllRegisteredPowerCurrentValuesAndCombatState`（正常读档同样恢复全部池当前值+战斗态）、`CORE_111_01_LegacyVitalsFormat_HealthOnly_StillLoadsWithoutTouchingOtherPools`（旧档只有 `health` 时向后兼容） | `core/gameplay/assembly/PlayerVitalsPersistable.cs`（`Save`/`Load` 泛化为全部已注册资源池当前值 + 进出战斗运行态，生产构造函数参数收窄为具体 `PowerHost`，补回旧签名 `[Obsolete]` 兼容重载）；`core/numbers/power_set/core/PowerHost.cs`（新增 `GetRegisteredPowerTypes`/`IsInCombat`，不进 `IPowerHost` 契约） | 独立重读 `PlayerVitalsPersistable.cs` diff 确认 `powers`/`in_combat` 字段与旧 `health` 字段并存写入、`Load` 优先用 `powers` 字段、`powers` 缺失才退回旧路径；三个复现/回归测试通过；`dotnet build`+`dotnet test`（六项目）全绿，见下"验收" | 独立核对收窄构造函数不破坏源码兼容：`git grep` 全仓库确认生产装配点（`GameplayAssembly.cs:1236`）与既有两处测试构造实参均是具体 `PowerHost`，收窄本身不影响真实调用；本仓库之外的消费方若曾以旧签名 `IPowerHost` 构造，补回的兼容重载允许其继续编译，运行期要求实际类型是 `PowerHost`（`IPowerHost` 当前唯一生产实现），不满足则在构造期抛 `ArgumentException`（而不是让 `GetRegisteredPowerTypes`/`IsInCombat` 调用处产生更难懂的 `InvalidCastException`），标记 `[Obsolete]` 引导新代码迁移、不强制 |
| TP-111-01（跨图传送先改字段再调用路由，Loading 期间第二个请求可污染玩家字段） | **成立（P2）** | `core/gameplay/assembly/tests/TP_111_FollowupAuditTests.cs :: TP_111_01_ConsecutiveGossipTeleportWhileLoading_KeepsSceneAndPlayerMapConsistent`（首个跨图请求接受后立即验证字段已提交；Loading 中的第二个请求验证字段保持不变，不被拒绝请求的目标污染；首个请求完成后验证 `Router.CurrentScene`/`fx.Player.MapId`/`WorldSim` 玩家实体 `MapId` 三者一致） | `core/gameplay/assembly/GameplayAssembly.cs` 的 `ApplyResolvedTeleport`：跨图分支改为先调用 `ISceneRouter.LoadScene`，只有路由未抛异常（确实接受本次导航请求）之后才提交 `entity.MapId`/位置；路由拒绝时整体不产生任何字段副作用 | 同上复现测试通过；独立重读 diff 确认 `try/catch` 分支不再"吞异常后仍提交字段"，而是路由拒绝直接 `return`，不改写任何字段 | **默认策略"拒绝"的判断**：对"未知地图"（`ArgumentException`）与"当前不允许转入 Loading"（`InvalidOperationException`，含 Loading 中收到的第二个跨图请求）两类路由拒绝，均采用默认"拒绝"策略——不排队、不重试，本次传送不产生任何字段副作用，交由上层（通常是发起传送的具体交互，如 Gossip 选项）按自己的重试/提示策略处理。独立核对该判断合理：(1) 与 `TeleportUnit` 类型注释既有惯例"解析失败时不产生任何副作用"一致，是把"路由拒绝"也纳入同一类"整体失败、原子回退"处理，不是新发明的语义；(2) 排队/重试需要额外状态机与超时策略，AUDIT_REPORT.md 验收条款本身也只要求"策略任选但最终字段与场景一致"，未强制排队；(3) 跨图请求被路由接受、但资源随后才在 `ISceneRouter.Update` 里异步失败（`HandleLoadFailure`）的情形不在本次修复范围——那种情形下字段已经按"路由已接受"正确提交，属于 `SceneRouter` 自己按 03 号文档"加载失败……State 回落 Idle"的既有失败恢复合同，不是本方法要重复处理的又一层 |
| UI-111-01（视图模型读档后不重建缓存，需等下一次手动 Refresh 才与宿主一致） | **成立（P2）** | `presentation/ui/tests/ViewModelTests.cs :: UI111_01_InventoryViewModel_RefreshesOnSaveLoaded_WithoutManualRefresh`（复现原 bug：同图读档后不手动 Refresh 即断言 VM 与宿主一致，修复前失败） | 8 个视图模型构造函数新增订阅 `SaveEventKeys.SaveLoaded`：`presentation/ui/core/ViewModels/InventoryViewModel.cs`、`ActionBarViewModel.cs`、`CharacterStatsViewModel.cs`、`DialogViewModel.cs`、`HudViewModel.cs`、`QuestLogViewModel.cs`、`ShopViewModel.cs`、`SkillBookViewModel.cs` | 同文件 `UI111_01_InventoryViewModel_RepeatedSaveLoaded_RefreshesEachTime_NoDuplicateSubscription`（重复读档不重复订阅、Dispose 后不再响应）+ 其余 7 个视图模型的同名回归 + `presentation/assembly/tests/PresentationAssemblyTests.cs :: Shop_SaveLoadedEvent_RefreshesOpenShelf_NoManualRefresh`；`dotnet test Tests.PresentationCommon`：507/507（基线 498 + 新增 9 条） | **覆盖的视图模型清单**：`InventoryViewModel`/`ActionBarViewModel`/`CharacterStatsViewModel`/`DialogViewModel`/`HudViewModel`/`QuestLogViewModel`/`ShopViewModel`/`SkillBookViewModel` 八个新增订阅；`SaveSlotsViewModel` 独立重读源码确认本就已订阅 `save.loaded`（无需改动）；`PauseMenuViewModel`（源自 `IAppStateHost`，与存档数据无关）、`SettingsViewModel`（本地化/按键绑定/音量是设备级用户偏好）独立核对 `core/foundation` 对应模块均未提供 `IPersistable`，确认与存档恢复无关、不需要订阅——`presentation/ui/core/ViewModels/` 目录下全部视图模型逐一核对完毕，无遗漏 |
| NAV-111-01（窄通道两端精确点可行走、直线畅通但网格寻路仍返回 null） | **成立（P2）** | `adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Editor/UnityNavigation2DTests.cs :: NAV111_01_NarrowCorridorThinnerThanBothGrids_ExactPointsAndDirectRaycastClear_ReturnsNonNullPath`（改写自审计归档 `Audit6739f50NavigationProbes` 的候选缺陷现象为正确性断言） | `UnityNavigation2D.cs`：`FindPath` 拆出私有 `FindPathViaGrid`（原逻辑不变），网格寻路彻底失败后新增"直线直达"兜底（新增私有 `SegmentHasClearContact`） | 同文件 `NAV111_01_StraightLineFallback_DoesNotOverrideDiagonalCornerCuttingBan`（复用既有"禁止切角"用例，断言直线兜底不越权放行）；`adapters/conformance/Runtime/Navigation2DScenarios.cs` 新增场景（桩、Unity 两侧均要求通过）；`dotnet test`（含 conformance 契约场景，走桩实现）：Tests.Foundation 689/689（基线 685 + 4） | 独立核对直线兜底判定比既有 `SegmentBlocked`（贴边/擦角放行）更严格——`SegmentHasClearContact` 要求线段与全部阻挡矩形完全无接触（含退化为单点的接触也不放行），否则会越权覆盖 A* 已有的"禁止切角"结论；根因确认：默认网格只在格子中心采样阻挡，通道宽度窄于两级采样格但直线本身畅通的场景此前完全没有兜底路径 |
| SPATIAL-111-01（跨 bucket 半径查询漏选大半径实体） | **成立（P2）** | `adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Editor/UnitySpatialQueryTests.cs :: SPATIAL111_01_QueryRadius_EntityAcrossBucketEdge_LargeSelfRadius_IsIncluded`（改写自审计归档 `Audit6739f50SpatialQueryProbes` 的候选缺陷现象为正确性断言，并与 `QueryRect` oracle 交叉核对） | `UnitySpatialQuery.cs`：`QueryRadius`/`QueryCone` 的候选桶范围改为"查询半径/范围 + `MaxRadiusHint()`"（索引内最大实体半径） | 同文件 `SPATIAL111_01_QueryCone_...`（同根因回归）、`SPATIAL111_01_QueryRadius_AfterUpdatePositionAcrossBucket_StillFound`（移动跨桶边界）、`SPATIAL111_01_QueryRadius_AfterUnregister_ExcludedEvenThoughWithinExpandedRange`（注销不应死灰复燃）；`adapters/conformance/Runtime/SpatialQueryScenarios.cs` 新增 3 条同语义场景（桩、Unity 两侧均要求通过） | 独立核对 `QueryRect`/`QueryShape`/`Nearest` 本就不经分桶（全量线性扫描 `_entries.Values`），不受本次改动影响、不存在同一漏选问题；`adapters/stub/StubSpatialQuery.cs` 核对后确认已与 Unity 实现对齐（全量线性扫描，天然不存在候选筛选问题），未作代码改动，独立复核该判断成立 |
| 文档/注释项（10 §2.5、`ArchSchemas.cs`/archetype README、`PowerTickHandler.cs`/`IPowerDiagnostics.cs`、`UnityViewFactory.cs`/`GameFoundationBootstrap.cs`、02 §1.8-1.9、09 §7.2、11 第 6 节） | **已完成** | 不适用（纯文档/注释，无源码行为变化） | 见 `architecture/10_存档与持久化.md`、`core/numbers/archetype/contracts/ArchSchemas.cs`、`core/numbers/archetype/schema/README.md`、`core/numbers/power_set/core/PowerTickHandler.cs`、`core/numbers/power_set/contracts/IPowerDiagnostics.cs`、`adapters/unity/.../UnityViewFactory.cs`、`GameFoundationBootstrap.cs`、`architecture/02_引擎适配层.md`、`architecture/09_表现层.md`、`architecture/11_工程规范与测试.md` | 独立通读改动后正文，确认 02/09 两处改写为"边界"表述后未新增/删除任何契约签名字样，版本号均未变（02 仍 v3、09 仍 v4），变更记录以"勘误："开头、ADR 列"—"；11 新增的跨模块场景测试约定为中立措辞，未点名具体引擎/文件 | 10 §2.5 "哪些不存"两行更新为"资源池当前值已泛化"引用 + "`vitals.in_combat` 默认持久化"；`ArchSchemas.cs`/README 的 `passive_auras`/`skill_book_ref` 说明改判为"分层边界/接口能力边界，不是对方模块尚未实现"；`PowerTickHandler.cs`/`IPowerDiagnostics.cs` 改判为"ADR-0013/sim_loop 已有基础离散调度，这只是本资源处理器自己的连续-only 边界"；`UnityViewFactory.cs` 改判为"`ViewBinder` 现已实现 `IDisposable`，`DestroyAllCreatedViews` 保留是两个独立职责"；02 §1.8/§1.9"性能约定"改写为"边界"（跨帧预算未实现、`QueryRect`/`Nearest`/桩实现均线性扫描），与已知参考实现一致；09 §7.2 补充读档场景视图模型缓存重建规则，与 §4.4 `IWeaponStyleSource` 缓存失效时机同一判断记录 |
| 编辑器暂不落地拍板（用户 2026-09-09 拍板） | **已核实并落地** | 不适用（产品决策，非缺陷） | `README.md`（能力边界三类举例句、顶层目录树 `editor/` 一行）、`editor/README.md`、`architecture/落地计划/落地方案与分阶段计划.md`（能力索引表新增第四类分类 + 编辑器工具一行改列、判断记录段新增说明该分类定义） | 独立核对四处表述前后一致：均明确"产品文档已完成，实现按用户拍板暂缓，待用户后续另行通知启动"，不声称"不是能力缺口"以外的其它结论 | 独立核对新增分类"暂不落地（用户拍板）"与既有"未实现"（框架当前无对应运行期逻辑，随时可能被排期补齐）、"明确非目标"（已有决策记录判定本版不展开）语义边界清晰：本类特指"技术上无障碍/方案已就绪，但用户主动暂缓"，不代表能力边界或架构结论变化，一旦用户后续通知启动即转入正常排期；核对未在 `architecture/00～13`/`adr/` 正文引入任何具体技术名 |

## 已关闭旧项

`AUDIT_REPORT.md`"已关闭旧项"一节记录：1.10 方向移动导航检查、同 tick 多 move 固定 dt、
WorldMap 四字段类型登记、Save/Load 事件抑制及旧用例本身已修复；06 号文档 `target_shape_ref` 只经
chain 使用 Shape 的旧 direct Shape 结论已排除；旧 API 兼容别名编译通过，归档项目使用
`FrameworkRoot` 参数化，不存在当前 API 断裂。主会话独立复核：本轮 A/B/C 三次提交均未改动
`core/carriers/unit/core/MovementTickHandler.cs`（方向移动/同 tick move 语义）、
`core/foundation/scene_router/core/WorldMapSchema.cs`（四字段登记）、
`core/foundation/save_system/core/SaveSystem.cs`（事件抑制作用域），独立重跑既有回归
（`MovementTickHandlerTests.cs`、`WorldMapSchemaTests` 一类骨架校验、`CORE_170_03_*`/
`CORE_180_*` 事件零泄漏回归）随本轮六项目全量测试一并全绿（见下"验收"），确认这些旧项未被本轮
任何改动破坏。

`AUDIT_REPORT.md` 另记录一项"已有 View 装备同图读档目前只有静态链路边界：本轮未构造对应 Runtime
探针，未将其升级为当前 P2；它列入下一轮独立验收"——本轮 A/B/C 均未涉及该边界，如实不处理，留待
后续以该模块自己的复现证据立项（与 followup-2026-09-09.md 记录的 PRES-110-01 判断记录"两种缓存
失效自愈机制不同，不能用同一次修复覆盖"是同一治理口径的延续）。

## 验收

前台实跑 `powershell -ExecutionPolicy Bypass -File check.ps1 -LogFile <scratchpad>\check_full_z16.log`
（不加 `-SkipUnity`/`-Quick`），执行前 `tasklist | findstr /i "Unity.exe"` 已确认 Unity 编辑器本体
未占用。

**首次实跑未一次通过**：`python -m pytest toolchain/tests -q` FAIL——`test_markdown_relative_links.py`
报出 6 处失效相对链接，均落在提交 D 归档的 `audit-6739f50-20260909/AUDIT_REPORT.md`/
`presentation/presentation-findings.md` 里（`navigation-probes.log`、`playmode-full-final.xml`/
`.log`、`audit-6739f50-20260909-evidence.zip`）——`evidence-manifest.txt` 登记了这些文件的 SHA256
（说明生成时确实存在过），但归档时未实际把它们写入仓库；候选产出目录
（`C:\Users\1\.codex\visualizations\2026\09\08\01a0801a-5646-78c2-a68c-99bbc742bed9\
audit-6739f50-20260909\` 及全量 `.codex` 目录）与本仓库全量搜索均确认找不回原件。与
`audit-ac3b622-20260909`（第十四方审核）遇到的同一类问题同一处理口径：`AUDIT_REPORT.md`/
`presentation-findings.md` 正文对应链接旁标注"原件未归档"及原因，结论以正文引用的
3 通过/1 失败（导航探针）、265/265（PlayMode 完整批次）数值为准；`toolchain/tests/
.linkcheck-ignore` 新增 6 条白名单条目。根治提交 `3ab485b`（标题"根治发布门禁失败:
audit-6739f50-20260909 三处失效证据链接补白名单与原因标注"，实际改动 3 个文件/6 处链接，标题
沿用既有惯例简述为"三处"指代三处引用位置——AUDIT_REPORT.md 两处 + presentation-findings.md
一处段落，与 `.linkcheck-ignore` 里逐条展开的 6 个具体 target 一一对应）。修复后本地独立重跑
`python -m pytest toolchain/tests -q`：82 passed，确认根治。

重跑（本次为最终成功版本，前台执行，无残留）：

| 步骤 | 结果 | 用时(s) | 详情 |
|---|---|---|---|
| 门禁自检：`Test-NativeExitCode` | PASS | 0.3 | — |
| `dotnet build Core.sln -c Release` | PASS | 3.7 | 0 警告 0 错误 |
| `dotnet test`（六项目） | PASS | 5.0 | Foundation 689/689、Numbers 107/107、Carriers 354/354、Rules 404/404、PresentationCommon 507/507、Gameplay 503/503（合计 2564 条全绿），与提交 A/B/C 三次提交时的计数一致 |
| `validate_data.py`（合并根） | PASS | 5.0 | 60 tables/285 records/0 errors/0 warnings/1 override |
| `validate_data.py --data-root data/_framework` | PASS | 1.4 | 5 tables/124 records/0 errors/1 warning（缺 l10n 表，既有已知情况） |
| `gen_event_constants.py --check` | PASS | 0.1 | — |
| `gen_placeholder_assets.py --check` | PASS | 0.1 | 92/92 通过 |
| `import_assets.py check --dataset _sample` | PASS | 0.1 | 0 问题 |
| `pytest toolchain/tests -q` | PASS | 10.8 | 82 passed（含本轮新增归档目录的链接检查，根治后确认） |
| 禁用词扫描：全仓库不出现具体游戏代号 | PASS | 26.6 | 0 命中 |
| 禁用词扫描：architecture 正文技术名（immunity 例外） | PASS | 0.1 | 0 真实命中 |
| 版本一致性 | PASS | 0.1 | VERSION=1.11.0，两个 `package.json`、`packages-lock.json`、`CHANGELOG.md` 一致（`CHANGELOG.md` 含 `## [1.11.0]` 条目，`## [1.12.0]` 为本轮新增的提前登记条目，不影响一致性判定——VERSION 尚未随本轮改动，等下一次 `build.ps1 -Release` 才会归档为 1.12.0） |
| `build.ps1 -SkipTests`（同步 DLL） | PASS | 5.0 | 六个 DLL 哈希核对通过（内容未变化，非首次同步） |
| 包清单一致性 | PASS | 8.5 | 三个 npm 包 version=1.11.0 一致，`npm pack --dry-run` 清单不含排除项 |
| Unity 编译检查 | PASS | 19.0 | 退出码 0 |
| Unity EditMode 测试 | PASS | 9.2 | total=68 passed=68 failed=0（基线 62 + NAV111_01 两条 + SPATIAL111_01 四条） |
| Unity PlayMode 测试 | PASS | 76.3 | total=269 passed=269 failed=0（基线 265 + 4 条契约一致性场景） |
| 独立版构建 + `-gf-smoke` 冒烟（连续模式） | PASS | 23.9 | — |
| 独立版 `-gf-smoke-discrete` 冒烟（离散模式） | PASS | 3.3 | — |
| IL2CPP 独立版构建/冒烟 ×3 | SKIP | 0 | 未传 `-Il2cpp`（任务未要求） |
| 消费方演练（`toolchain/consumer_smoke.ps1`） | PASS | 138.3 | — |

**汇总：23 步（20 PASS + 3 SKIP），0 FAIL，总用时 336.8s。**

## 自检复核

- 全仓库禁用具体游戏代号扫描：**0 命中**（含提交 A/B/C/D、版本文档提交、根治提交与本文档全部
  新增内容）。
- `architecture/00～14` 与 `architecture/adr/` 正文技术名 grep（`unity|c#|csharp|dotnet|\.net|
  nunit|python|newtonsoft|il2cpp|powershell|github|verdaccio|npm`）：仅 `immunity` 既有豁免
  误报，**0 真实命中**（含本轮 02/09/10/11 号文档新增/改写段落、变更记录新增行；独立重跑覆盖
  `architecture/` 下全部 15 个编号文档 + `adr/` 目录，非仅门禁脚本自身的排除范围）。
- A/B/C/D、版本文档、根治提交后 `git status --short` 均只剩下一步待落地的内容，本文档提交后应
  为空；全量门禁实跑（不含 `-SkipUnity`/`-Quick`）未产生任何新的未提交改动（`build.ps1
  -SkipTests` 同步 DLL 哈希均"内容未变化"，非首次同步；`build.ps1`（无 `-SkipTests`）打出的
  `dist/1.11.0/` 属既有 `.gitignore` 覆盖范围，不影响 `git status`）。
- `PlayerVitalsPersistable` 源码兼容重载独立编译核对：`dotnet build Core.sln -c Release` 0
  警告 0 错误（该重载标记 `[Obsolete]` 但本仓库内没有任何调用点触发编译警告，符合预期——只有
  外部消费方使用旧签名时才会在其自己的编译中看到过时警告）。
