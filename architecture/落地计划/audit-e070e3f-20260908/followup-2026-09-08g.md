# 第十三方深度审核核实跟进（codex 第十一轮，基线 `e070e3f`）

基线：`e070e3f`（main，v1.8.0）。审计报告本体见本目录 `AUDIT_REPORT.md`/`core/`/`docs-project/`/
`presentation/`（codex 原文，`git add` 归档，未改写）。

修复由三个并行 Sonnet 5 子 agent 按领域分工执行（核心侧 WA、表现侧 WB、工具链与文档 WC），判断记录
留存于 `C:\Users\1\AppData\Local\Temp\claude\D--workespace-ws-game\
f1d8941d-33da-4643-9759-6533f7c7ff79\scratchpad\audit11\{WA,WB,WC}.md`（未随仓库提交，属会话临时
材料）。主会话（本 agent）在三份判断记录基础上补齐"第 0 步"三处遗留（`RulesSchemaCatalog.cs`/
`core/rules/skill/README.md` 两处"直接指向 Shape"残留注释同步改为"只引用目标链"表述；
`06_规则层_属性技能战斗AI.md:211` 施法管线步骤 6 表格行"目标选择链（第 6 节）"笔误改为"第 5 节"，
并入 WC 已新增的同一条"跟进 audit-e070e3f-20260908 文档漂移"勘误记录，未单独加行；
`api-compat/old15-only/ApiCompatOld15Only.csproj` 绝对 HintPath 按 `current17-only` 同一做法参数化
为 `$(FrameworkRoot)`，补充同款 README），独立核实全部条目、统一分领域提交、执行全量门禁、撰写
本文档。提交：

| 提交 | hash | 范围 |
|---|---|---|
| A（核心侧） | `f9ee195` | `core/**`（CORE-180-01～03 + CAND-01 + summon 文案 + 第 0 步 core 注释）、`architecture/10` |
| B（表现侧） | `211e361` | `presentation/**`（PRES-180）、`adapters/unity/**`（Unity PlayMode 新增用例） |
| C（文档） | `49e414f` | `README.md`、`architecture/06`/`11`、ADR-0017、归档探针路径参数化（含第 0 步 06/api-compat 遗留） |
| D（归档） | `62b0777` | `architecture/落地计划/audit-e070e3f-20260908/` 整目录（codex 报告原文与证据，`core/logs/` 按 `.gitignore` `**/[Ll]ogs/` 规则排除，与既往轮次同一惯例） |

四次提交各自的 pre-commit 快速门禁（`-SkipUnity -Quick`，20 步全部 PASS/SKIP，0 FAIL）均一次通过，
`dotnet test`（六项目）在 A/B/C/D 四次提交时均为 Foundation 662/662、Numbers 107/107、
Carriers 329/329、Rules 404/404、PresentationCommon 495/495、Gameplay 496/496，全绿（提交 A 时
`presentation/**`/`adapters/**` 虽尚未 `git add`，但工作树已是 WA+WB+WC 全部改动叠加后的状态，
门禁针对真实磁盘文件构建/测试，故四次提交的六项目计数从一开始就已保持一致，非"逐步累加"）。提交
落地后 `git status --short` 为空。

## 核实方法说明

本文档"核实"列的判断基于：(1) 独立重读改动后的源码（非只读 WA/WB/WC 报告文本），逐一确认修复手法
与报告描述一致；(2) 独立重跑 `dotnet test`，核对通过数与报告一致（见上"提交"表下方说明）；
(3) 独立核对接口变更是否为 C#8 默认接口方法、是否构成源码级破坏性变更；(4) 对报告文字与实际文件
清单做交叉核对。

## 核实表：CORE-180-01～03 + CAND-01 + PRES-180

| 编号 | 判断 | 复现 | 修复位置 | 验收 | 判断记录 |
|---|---|---|---|---|---|
| CORE-180-01（成功 Load 后内部同步事件被抑制丢弃，评级/资源池上限未重算） | **成立（P1）** | `CORE_180_01_SuccessfulLoad_RestoresRatingConvertedStat_DirectSaveSystemLoad`、`CORE_180_01_SuccessfulLoad_RestoresPowerMaxAndHealth_DirectSaveSystemLoad` | `core/foundation/save_system/contracts/IDerivedStateRebuilder.cs`（新增契约）+ `ISaveSystem.cs`/`SaveSystem.cs`（`BeforeLoad`/`OnSectionLoaded` 钩子接线，完全绕开事件总线）+ `core/gameplay/assembly/GameplayAssembly.cs`（嵌套实现类，`RegisterPersistables` 末尾接线） | 独立重跑 Tests.Gameplay 496/496、Tests.Foundation 662/662 全绿；独立重读 `SaveSystem.Load` 源码确认 `BeforeLoad`/`OnSectionLoaded` 调用点位置（逐段成功 `Load()` 后立即调用，`player.equipment` 段完成时触发重算、早于 `player.vitals` 段读取） | 见下"CORE-180-01 判断记录：为何未采用『事件分类』方案" |
| CORE-180-02（后段失败逆序回滚与依赖顺序相反） | **成立（P2）** | `CORE_180_02_Rollback_RestoresEquipment_AfterLevelRolledBack` | `core/foundation/save_system/core/SaveSystem.cs`（`RollbackLoadedSections` 遍历方向由逆序改正向，与 `readOrder`/正常读档同一顺序） | 独立重跑 Tests.Foundation 662/662 全绿；独立重读源码确认遍历方向已改为 `foreach` 正向且 `rollbackKeysIncludingFailed` 构造逻辑未变（本就是正向序） | 不改用"先恢复全部快照、再统一重建"的二阶段设计——回滚正向遍历后本质等价于"再做一次读档，文档换成读档前快照"，复用既有依赖顺序（`SaveSections.KnownOrder`），不需要为回滚单独维护一套顺序规则；独立核对 AUD-01/CORE-170-03 既有回滚回归测试未依赖具体回滚顺序，只依赖"最终恢复到位"，改动后复跑全部通过 |
| CORE-180-03（同图读档只切种族字段，未清理/重应用属性修正与光环） | **成立（P2，已确认）** | `CORE_180_03_SameMapRestoreFromSlot_ReappliesRaceStatModsAndAuras` | `core/rules/assembly/RulesAssembly.cs`（新增 `ReloadArchetypeAndRace`）+ `GameplayAssembly.cs`（`DerivedStateRebuilder.OnSectionLoaded(PlayerRaceId)` 触发） | 独立重跑 Tests.Rules 404/404、Tests.Gameplay 496/496 全绿；独立核对新方法不复用 `ArchetypeRegistry.ApplyTo`（会对已注册单位重复调用 `PowerHost.RegisterUnit` 抛异常），改用 `Stats.RemoveModifiersBySource`/`AuraHandleLedger.Release` 精确移除旧种族来源、`Stats.AddModifier`/`ReapplyRacePassiveAuras` 应用新种族来源 | 用 `Release`（引用计数递减）而非 `Forget`（不递减，只用于跨图 `ClearAll` 后实例已不存在的场景）——本场景是同图、实例仍存活，独立核对与既有装备/套装门槛加成等共享来源的计数保持一致（用 `Forget` 会导致账目错乱） |
| CORE-180-CAND-01（候选转已确认：archetype 基础属性同图切换未重聚合） | **成立（候选转已确认，随 CORE-180-03 一并根治）** | `CORE_180_CAND_01_SameMapRestoreFromSlot_ReappliesArchetypeBaseStats` | 同 CORE-180-03（`ReloadArchetypeAndRace` 内 `cls.BaseStats` 的 `SetBase` 覆盖写入部分） | 独立重跑同上；独立核对 A/B 两个独立构造的有效存档 fixture 覆盖同图职业切换 | 已知收边范围（如实标注，未过度承诺）：`SetBase` 是覆盖写入，新旧职业若对同一属性键的声明集合不同（新职业未声明旧职业曾声明的某键），该属性会残留旧职业基础值；新旧职业若声明不同的 `power_types` 集合，因不再调用 `PowerRegistrar`，资源池类型集合不会跟着调整——这两点不在本次候选范围内（报告本身把 archetype 列为"候选，未定级"，只有种族部分是真实探针确认的运行时事实），如需补齐应另开条目 |
| PRES-180（同图读档抑制丢弃 `entity.created`/`entity.destroyed`，View 与逻辑实体不同步） | **成立（候选转已确认）** | `PRES180_01_SameMapLoad_SuppressedEntityCreated_ReconciledImmediately_WithoutExtraTick`、`PRES180_02_RepeatedSameMapLoad_DoesNotCreateDuplicateView`、`PRES180_03_EntityDestroyedSuppressedDuringLoad_StaleViewReconciledOnSaveLoaded`、`PRES180_04_CrossMapRestore_LootViewReconciliation_DoesNotRegress`；Unity PlayMode 新增 1 例（真实 `FrameworkResidentHost` 整套装配） | `presentation/common/contracts/ISimSnapshot.cs`（新增 `GetAllEntityIds()`/`GetRawKind(Id)`）+ `presentation/common/core/WorldSimSnapshot.cs`（实现）+ `presentation/view_binding/core/ViewBinder.cs`（订阅 `save.loaded`，`OnSaveLoaded` 双向全量对账） | 独立重跑 Tests.PresentationCommon 495/495 全绿（基线 491 + 新增 4）；独立核对 `ViewBinder` 对账复用既有 `OnEntityCreated`/`OnEntityDestroyed`（与正常事件路径共享 AreaTrigger 跳过/未映射诊断/幂等去重规则），未新造一套平行逻辑 | `ISimSnapshot` 新增两个成员**不是** C#8 默认接口方法（独立核对源码确认为纯抽象成员，无 `=>`/`{ }` 默认体），唯一生产实现 `WorldSimSnapshot` 已同步，自定义 `ISimSnapshot` 实现须自行新增才能编译通过——与本轮/上轮其余接口新增（均为默认接口方法）不同，已在 `CHANGELOG.md` 1.9.0 迁移说明单独标注；`entity.destroyed` 被抑制丢弃的具体触发路径当前仅为防御性场景（生产唯一 `ClearAll` 调用点在抑制作用域外），PRES180-03 用合成 `IPersistable` 覆盖，不代表已确认的第二个生产缺陷，如实标注 |

## 文档项核实

| 项 | 判断 | 修复位置 | 验收 |
|---|---|---|---|
| README.md 地面点选责任边界 | **成立（文档漂移）** | `README.md:9`：从"未实现"清单移除地面点选，改列编辑器/天赋/孤儿检查三项，补充"框架提供 `TargetPoint` 字段…不属于框架未实现项" | 独立核对现行正文与 `core/rules/skill/core/SkillTickHandler.cs` 类型注释口径一致（`point` 当前不被 `CastSkill` 消费，由更上层适配层处理） |
| 06 号文档 `target_shape_ref` 口径统一 | **成立（文档漂移）** | `:127`（原 126）字段表行、`:220`（原 223）3.7 节说明统一为"只引用第 5 节 `target.chain_def`，链内 `shape` 按需引用 05 的 Shape"；变更记录新增一行 | 独立核对 `RulesSchemaCatalog.cs`/`CastPipeline.cs` 现行代码确实只登记并按链解析，文档口径对齐既有实现，不改变代码 |
| ADR-0017"决定二"旧算法句标注废止 | **成立（工程证据）** | `adr/0017-模型型外形默认路线补齐与命中帧同步.md:79` 前插入标注（已被后续 2026-09-08 修订收窄替换），历史正文其余内容原样保留 | 独立核对标注不改写任何历史决定原文，符合 12 §5"历史正文不得改写，只能追加标注/新记录" |
| 归档探针可移植性（`current17-only`/`old15-only`） | **成立（工程证据）** | 两个 `.csproj` 的绝对 `HintPath` 均改为参数化 `$(FrameworkRoot)`，各补一份用法 README | 未实际构建（任务书明确不需要）；独立核对两个 `.csproj` 文本，`HintPath` 均为 `$(FrameworkRoot)\*.dll` 形式，不含机器绝对路径 |
| 11 号文档"本地重建 dist 不是正式发布包" | **成立（文档漂移）** | 第 7 节"发布不可变"段落后新增说明段，措辞已做技术名脱敏（未写具体脚本名/扩展名） | 独立核对新增段落不出现 `unity\|c#\|csharp\|dotnet\|.net\|python\|powershell` 等禁用技术名（immunity 例外未触发） |
| 能力索引"召唤/掉落跳过离散步""孤儿检测独立于空间查询"两处口径核对 | **核对无误，未改动** | 无（`落地方案与分阶段计划.md:1260`/`:1272` 现行文字已正确区分召唤物/掉落物、未把孤儿检测与空间查询混同） | 独立重读对应两行原文，确认与 WC 报告结论一致，未发现需要修正的漂移 |

## CORE-180-01 判断记录：为何未采用『事件分类』方案

报告给出两个方向：(a) 给"内部同步事件"分类，抑制作用域内仍照常派发；(b) 读档成功后由装配根按依赖
顺序显式执行派生状态重建。本次**只做 (b)，不做 (a)**，独立核实理由如下：

- `stat.changed` 这一事件 key 同时被 `RulesAssembly` 自身的内部重算订阅（`Powers.RecomputeMax`）
  与外部业务/测试订阅者共享；`EventBus.DispatchOne` 按 key 无差别派发给该 key 下全部订阅者，机制上
  做不到"只放行内部订阅者、外部订阅者继续抑制"——独立核对 `EventBus.cs` 源码确认这一分发机制属实，
  不存在按订阅者身份区分的钩子。
- 既有回归测试 `CORE_170_03_SaveRollbackEventSuppressionTests.
  Load_BadShapeEquipmentSection_RestoresEquipment_AndDoesNotLeakEventsToObservers` 显式断言排空
  事件队列后 `StatChanged` 一个都不应该出现在外部订阅者手里（`Assert.Empty(fx.Events)`）；独立核实
  若给 `stat.changed`/`PowerChanged` 开白名单会直接违反这条既有验收（任务书"不删测试不放宽断言"的
  硬约束），方案 (a) 不可行。
- 方案 (b) 新增的 `IDerivedStateRebuilder` 两个回调完全绕开事件总线，由装配根实现直接调用目标模块
  方法（`Stats.RecomputeRatingStats`/`Powers.RecomputeMax` 等），不产生任何新的可观察事件，因此
  不受 `SuppressDispatch` 影响也不会被任何事件订阅者观察到，与 CORE-170-03(b) 的既有事件零泄漏
  保证完全不冲突——独立重跑 `CORE_170_03_SaveRollbackEventSuppressionTests`（2 条）确认仍然通过。
- 独立核对触发时机选择的正确性：`OnSectionLoaded(SaveSections.PlayerEquipment)` 是
  `KnownOrder` 中会影响评级换算属性/资源池上限的最后一段（progression/archetype/race_id/
  inventory/equipment 排序中最后），且早于随后的 `player.vitals` 段——`PlayerVitalsPersistable.
  Load` 按"存档值与当前值差额"调用 `ModifyPower`，若此时上限仍是旧值会把差额 clamp 到旧上限；
  必须在读到 `player.vitals` 之前完成重算，这正是 CORE-180-01 装备/生命值那一半复现的直接根因，
  独立核对源码顺序确认该分析成立。

## 上轮三类修复复核

上轮（第十二方深度审核，基线 `8160178`，提交 `b6d7f4f`/`cce0370`/`46127ed`）按核心侧/表现侧/文档
三类分工修复，本轮独立复核三类均未被本次改动破坏：

- **核心侧**（`b6d7f4f`：CORE-170-01～03 + 8 处同类失败段缺陷）：`AuraHandleLedger` 跨来源引用计数
  账本本轮被 CORE-180-03 的 `ReloadArchetypeAndRace` 直接复用（`RemoveModifiersBySource`/`Release`
  调用同一账本实例），独立核对未新建平行账本、未绕开既有计数规则；`Progression` 等级唯一权威
  （`LevelSync` 委托）未被本轮触碰；`SaveSystem.Load` 事件抑制作用域（`SuppressDispatch`）本轮
  `IDerivedStateRebuilder` 回调设计为"完全绕开事件总线"正是为了不破坏这条既有保证（见上节判断
  记录）；独立重跑 `CORE_170_03_SaveRollbackEventSuppressionTests`（2 条）、`CORE_170_01_*`
  （3 条）、`CORE_170_02_*` 相关测试，均随 Tests.Rules 404/404、Tests.Carriers 329/329、
  Tests.Gameplay 496/496 一并全绿，无回归。
- **表现侧**（`cce0370`：PRES-170-01 共享 `AnimationClip` 隔离）：`UnityViewFactory.cs` 的进程级
  静态表（`s_authoredClipEvents`/`s_modelClipOverrides`）本轮未改动；独立核对 PRES-180 新增的
  `save.loaded` 对账逻辑位于 `ViewBinder`（不同类型），与 `UnityViewFactory` 的剪辑隔离机制无交叉
  依赖；Unity PlayMode 全量门禁见下"验收"节，247/247（基线 246 + 本轮新增 1）全绿，不含 PRES-170-01
  对应的 5 条新增用例回归。
- **文档**（`46127ed`：存档 best-effort 措辞、下载脚本依赖说明、能力索引事实修正、组合链探针分层
  约定）：独立核对 `architecture/10_存档与持久化.md` 第 3 节"段失败回滚合同"best-effort 措辞未被
  本轮改动推翻，本轮 CORE-180-02/03 的勘误是在该措辞基础上补充"失败回滚正向依赖顺序""同图/跨图
  统一重建"两条细节说明，不冲突；`toolchain/README.md`"依赖边界"段、能力索引表相关行本轮未触碰。

## 验收

前台实跑 `powershell -ExecutionPolicy Bypass -File check.ps1 -LogFile <scratchpad>\check_full_z13.log`
（不加 `-SkipUnity`/`-Quick`），执行前 `tasklist | findstr /i "Unity.exe"` 已确认 Unity 编辑器本体
未占用。一次实跑即全部通过，无需二次修复重跑：

| 步骤 | 结果 | 用时(s) | 详情 |
|---|---|---|---|
| 门禁自检：`Test-NativeExitCode` | PASS | 0.3 | — |
| `dotnet build Core.sln -c Release` | PASS | 6.1 | 0 警告 0 错误 |
| `dotnet test`（六项目） | PASS | 5.2 | Foundation 662/662、Numbers 107/107、Carriers 329/329、Rules 404/404、PresentationCommon 495/495、Gameplay 496/496（合计 2493 条全绿） |
| `validate_data.py`（合并根） | PASS | 4.9 | 60 tables/285 records/0 errors/0 warnings/1 override |
| `validate_data.py --data-root data/_framework` | PASS | 1.5 | 5 tables/124 records/0 errors/1 warning（缺 l10n 表，既有已知情况） |
| `gen_event_constants.py --check` | PASS | 0.1 | 90 个常量一致 |
| `gen_placeholder_assets.py --check` | PASS | 0.1 | 通过 92/92，失败 0 |
| `import_assets.py check --dataset _sample` | PASS | 0.1 | 0 问题 |
| `pytest toolchain/tests -q` | PASS | 11.3 | 82 passed |
| 禁用词扫描：全仓库不出现具体游戏代号 | PASS | 25.0 | 0 命中 |
| 禁用词扫描：architecture 正文技术名（immunity 例外） | PASS | 0.0 | 0 真实命中 |
| 版本一致性 | PASS | 0.1 | VERSION=1.8.0，两个 `package.json`、`packages-lock.json`、`CHANGELOG.md` 一致 |
| `build.ps1 -SkipTests`（同步 DLL） | PASS | 5.3 | 六个 DLL 哈希核对通过 |
| 包清单一致性 | PASS | 8.7 | 三个 npm 包 version=1.8.0 一致，`npm pack --dry-run` 清单不含排除项 |
| Unity 编译检查 | PASS | 16.8 | 退出码 0 |
| Unity EditMode 测试 | PASS | 8.9 | total=53 passed=53 failed=0 |
| Unity PlayMode 测试 | PASS | 67.3 | total=247 passed=247 failed=0（基线 246 + PRES-180 新增 1） |
| 独立版构建 + `-gf-smoke` 冒烟（连续模式） | PASS | 23.3 | — |
| 独立版 `-gf-smoke-discrete` 冒烟（离散模式） | PASS | 3.3 | — |
| IL2CPP 独立版构建/冒烟 ×3 | SKIP | 0 | 未传 `-Il2cpp`（任务未要求） |
| 消费方演练（`toolchain/consumer_smoke.ps1`） | PASS | 118.7 | — |

**汇总：23 步（20 PASS + 3 SKIP），0 FAIL，总用时 307s。**

## 自检复核

- 全仓库禁用具体游戏代号扫描（`check.ps1` 内置扫描步骤，同一禁用词）：**0 命中**（含 A/B/C/D 四次
  提交与本文档新增内容）。
- `architecture/00～14` 与 `architecture/adr/` 正文技术名 grep（`unity|c#|csharp|dotnet|\.net|
  nunit|python|newtonsoft|il2cpp|powershell|github|verdaccio|npm`）：仅 `immunity` 既有豁免误报，
  **0 真实命中**（含本轮 06/11 号文档新增段落、变更记录新增行、ADR-0017 修订标注、11 号文档新增
  "本地重建 dist 不是正式发布包"段的技术名脱敏措辞）。
- 四次提交（A/B/C/D）与 CHANGELOG/落地计划提交后 `git status --short` 均为空，工作树无残留改动；
  全量门禁实跑（不含 `-SkipUnity`/`-Quick`）未产生任何新的未提交改动（`build.ps1 -SkipTests` 同步
  DLL 哈希均"内容未变化"，非首次同步）。
