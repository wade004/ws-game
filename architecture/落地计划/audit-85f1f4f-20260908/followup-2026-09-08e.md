# 第十一方深度审核核实跟进（codex 第九轮，基线 `85f1f4f`）

基线：`85f1f4f`（main，v1.6.0）。审计报告本体见本目录 `AUDIT_REPORT.md`/`core/`/`docs-project/`/
`presentation/`（codex 原文，`git add` 归档，未改写）。

修复由三个并行 Sonnet 5 子 agent 按领域分工执行（核心侧 WA、表现/引擎侧 WB、工具链与文档 WC），
判断记录留存于 `C:\Users\1\AppData\Local\Temp\claude\D--workespace-ws-game\
f1d8941d-33da-4643-9759-6533f7c7ff79\scratchpad\audit9\{WA,WB,WC}.md`（未随仓库提交，属会话临时
材料）。主会话（本 agent）在三份判断记录基础上补齐"第 0 步"两处遗留（三处示例组合根透传
owner/day/vendor 回调 + 新增端到端验证测试；核实并更正 WC 对 `CHANGELOG.md` 1.6.0 迁移说明的一处
不准确改写），独立核实全部条目、统一分领域提交、执行全量门禁、撰写本文档。提交：

| 提交 | hash | 范围 |
|---|---|---|
| A（核心侧） | `ff427b7` | `core/**`（AUD-01～04、种族被动光环跨图、owner/day/vendor 装配扩展点）、`architecture/08、10` |
| B（表现/引擎侧） | `b32666b` | `adapters/unity/**`、`games/_template/**`（AUD-05、动画剪辑事件登记契约差异 + "第 0 步"owner/day/vendor 透传与验收测试）、`core/foundation/engine_adapter/contracts/IResourceLoader.cs`、`architecture/02、09、14`、ADR-0017 |
| C（工具链与文档） | `a486978` | `toolchain/**`、`build.ps1`、`check.ps1`、`.githooks/**`、`.github/workflows/**`、`README.md`、`CHANGELOG.md`（措辞）、`architecture/04、11、13`、落地计划 |
| D（归档） | `6fe900f` | `architecture/落地计划/audit-85f1f4f-20260908/` 整目录（codex 报告原文与证据） |
| fix（追加） | `8ea2106` | 全量门禁发现的一处测试隔离缺陷单独修复（见下"验收"节说明） |

## 核实方法说明

本文档"验收"列标注的测试文件均已在提交 A/B/C/D/fix 落地的工作树上重新执行：`dotnet build
Core.sln -c Release` 0 警告 0 错误，`dotnet test`（六项目）全绿——Foundation 659/659（基线 657 +
AUD-01 新增 2）、Numbers 107/107（基线 106 + 1）、Carriers 323/323（基线 314 + AUD-02/03/04 共
9）、Rules 404/404（不变）、PresentationCommon 491/491（不变）、Gameplay 476/476（基线 464 +
AUD-02/种族光环/owner-day-vendor 共 12），六项目合计 2460 条全绿。四次分领域提交（A/B/C/D）与一次
追加修复提交（fix）各自的 pre-commit 快速门禁（`-SkipUnity -Quick`，20 步全部 PASS/SKIP，0 FAIL）
全部通过；B/C/D/fix 一次通过，A 提交时"architecture 正文不出现引擎/语言/框架/工具名"这一步一次
因"第 0 步"补录到 08 号文档的段落误用了引擎适配层具体类型名（`Adapter.Unity.*`）而未通过，改写为
中立表述后重跑通过（详见下"文档更新与边界清单"节）。全部
提交落地后再跑一次全量门禁（不加 `-SkipUnity`/`-Quick`），首次因 Unity PlayMode 测试数据隔离缺陷
FAIL 1 步，单独提交 fix 后重跑到全 PASS（见下"验收"节，含 Unity EditMode/PlayMode 与独立版冒烟、
消费方演练）。

## 核实表（10 条：AUD-01～05、种族被动光环跨图、owner/day/vendor 装配扩展点、动画剪辑事件登记
契约差异、工具链两条）

| 编号 | 判断 | 复现 | 修复位置 | 验收 | 判断记录 |
|---|---|---|---|---|---|
| AUD-01（P1，SaveSystem 段失败不回滚 + 旧字段读档抛异常） | **成立** | `GobjPendingLootPersistenceTests.SaveSystemLoad_Legacy15GobjInstanceIdField_SafelyDropped_NotThrown`（手写 1.5.0 真实 on-disk 信封复现）、`SaveSystemTests.Load_LaterSectionThrows_RollsBackEarlierSuccessfullyLoadedSection_ToPreLoadState`/`...RollbackDoesNotMaskFailureStatus`（新增） | `core/carriers/gobj/core/GobjPendingLootPersistable.cs`（`Load` 新增 `TryResolveOriginKey`/`TryParseItems`，无可逆映射时安全丢弃并经 `ISaveDiagnostics` 记诊断，不抛异常）、`core/foundation/save_system/core/SaveSystem.cs`（`Load` 逐段读档前先对全部已注册段 `Save()` 取快照，某段 `Load` 抛异常时新增 `RollbackLoadedSections` 按逆序回滚已成功段） | `dotnet test` Tests.Foundation/Tests.Carriers 全绿（本项 3 条），既有 `GobjPendingLootPersistenceTests`/`SaveSystemTests` 无回归 | `core/foundation/save_system/README.md`"Save / Load 失败语义"节由"段失败不回滚"改写为"按逆序回滚"；`core/carriers/gobj/README.md` 判断记录 9 修订"本段未在任何已发布版本对外承诺过字段级兼容"这一不成立的旧结论。存档格式变更：`world.gobj_pending_loot` 段读取兼容旧字段 `gobjInstanceId`（只读兼容，`Save()` 输出不变） |
| AUD-02（P2，缺段不清空） | **成立**（真实探针复现 inventory/vendor 两例；逐一审查全仓 `IPersistable` 实现后确认还有 4 处同类缺陷） | 6 处修复各自新增测试：`ItemPersistableTests.InventoryPersistable_Load_NullData_ClearsPreExistingItems`、`SkillBindingPersistableTests.Load_NullData_ClearsPreExistingBindings`、`EconomyHostTests.VendorStockPersistable_Load_NullData_ResetsStockToContentDefinedFull`、`LootDropPickupTests.Load_NullData_ClearsAllTrackedDroppedLoot`、`PlayerVitalsPersistableAud02Tests.Load_NullData_ResetsToAliveAndFullHealth_EvenIfDeadWithPartialHealth`、`ProgressionPersistableTests.Load_NullData_ResetsRegisteredUnitToLevelOneAndZeroXp` | `ItemPersistable`/`SkillBindingPersistable`/`VendorStockPersistable`/`DroppedLootPersistable`/`PlayerVitalsPersistable`/`ProgressionPersistable` 六处 `Load` 补齐 `JsonNull` 分支清空逻辑；新增 `IPersistable.KeepStateWhenSectionMissing` 显式声明 2 处例外（`UnitPersistable` 的地图/坐标三段、`RngStreamsPersistable`，均写明理由） | `dotnet test` 六项目全绿（本项 6 条新增 + 例外 2 处已有覆盖），已确认 `GobjPendingLootPersistable`/`ItemPersistable.EquipmentPersistable`/`CurrencyPersistable`/`QuestPersistable`/`KnownSkillsPersistable`/`TurnScheduler`/`AchievementHost`/`DifficultyHost`/`SpawnHost`/`WorldState` 十处原本就正确，未见回归 | `core/carriers/item/README.md`13、`core/carriers/unit/README.md`12、`core/gameplay/economy/README.md`6a、`core/gameplay/loot/README.md`11、`core/gameplay/assembly/README.md`11、`core/numbers/progression/README.md`7、`core/foundation/save_system/README.md`（`RngStreamsPersistable` 一节）新增判断记录。存档格式无字段级变更，仅"缺段时的运行期结果"变化 |
| AUD-03（P2，商人补货计时器跨读档丢失） | **成立**（真实探针复现：t=2 存档剩 8 秒，原地推进 7 秒读档，再过 1 秒立即补货） | `EconomyHostTests.VendorStockPersistable_RoundTrip_RestoresTimerRemaining_OnSameHostReload_NotStaleValue`（严格复刻探针时序） | `core/gameplay/economy/core/EconomyHost.cs`（`SetStock` 新增可选参数 `timerRemaining`，为空时若为 `timer` 策略兜底重置为完整周期，新增 `GetStockTimerRemaining`）、`core/gameplay/economy/core/VendorStockPersistable.cs`（`timer` 策略物品条目形状扩展为可选对象 `{remaining, timer_remaining}`，向后兼容纯数字旧格式） | `dotnet test` Tests.Gameplay 全绿（本项 1 条 + `VendorStockPersistable_Load_LegacyNumberShape_ResetsTimerToFullPeriod` 旧格式兜底测试） | `core/gameplay/economy/README.md` 判断记录 6 改写（旧结论"不持久化倒计时"只在"重新构造宿主"场景成立，原地读档场景不成立）；10 号文档 §2.3 `vendor_stock` 行已勘误 |
| AUD-04（P2，公开 API 改名无别名） | **成立**（全仓 `git diff v1.5.0 v1.6.0 -- '*.cs'` 复扫，`GameObjectHost.PendingChestLootSnapshot`/`RestorePendingChestLoot` 是唯一一处纯改名） | `GobjPendingLootPersistenceTests.ObsoletePendingChestLootAliases_StillCompileAndForwardToNewNames`（"1.5 风格调用"编译测试） | `core/carriers/gobj/core/GameObjectHost.cs` 补回 `[Obsolete]` 转发方法 `PendingChestLootSnapshot()`/`RestorePendingChestLoot(snapshot)`，行为等价转发新名 | `dotnet test` Tests.Carriers 全绿（本项 1 条） | `core/carriers/gobj/README.md` 判断记录 9 新增 AUD-01/AUD-04 修订段落；`CHANGELOG.md` 1.7.0 迁移说明新增本条（见下"文档更新与边界清单"节——原 WC 改写误写成"1.6.0 已补回"，本轮已更正为随 1.7.0 发布，`[Obsolete]` 别名保留至下一个 MINOR 版本周期结束） |
| AUD-05（P2，样例 model 槽位换装把 prefab 引用当 Mesh 读取，槽位网格被清空） | **成立**（Unity 真实 PlayMode 已复现，已根治） | `SlotMeshResourceContractTests.AUD05_ApplyEquipVisual_SlotMesh_PrefabModelRef_ResolvesRealMesh_NotNull`（断言由"现状断言"改为"正确性断言"） | `UnityResourceLoader.cs`（新增 `TryGetOrLoadSlotMesh`/`TryExtractMeshFromPrefab`/`TryGetRendererMesh`/`FindDeep`）、`UnityRenderer3D.cs`（`ApplySlotMesh` 改经加载器解析，缺失时保留当前网格不清空，经 `RequestSlotMeshLoadAndSwap` 发起真实异步加载） | Unity PlayMode 全绿；`SlotMeshResourceContractTests.cs` 全 6 条（正确性、已缓存命中、缺失保留当前网格、缺失确实经 `LoadAsync` 发起真实请求、显式卸下+重复卸下幂等、销毁重建后仍可用） | `architecture/02` 第 1.7 节勘误（`mesh_ref` 资源合同）、`09` 第 4.1 节勘误、`14` 第 1.2/4.3.1 节勘误、ADR-0017 修订记录新增一条。**范围边界（如实保留，未扩张结论）**：codex 原报告已注明"样例没有 `item.template` 行，因此没有把它扩张为完整 inventory→View 生产入口已验证"——本轮验收测试证明的是资源加载合同层（`loader`+`renderer`）的正确性，未新增走真实背包→装备→View 生产入口的样例数据，08/09/14 文档也未做出超出这一范围的断言，与 codex 边界说明一致 |
| 种族被动光环跨图（静态候选 → 已确认） | **成立**（真实内容 `arch.race.passive_auras` 非空 + 真实 `World.ClearAll` 复现：种族属性修正不受影响，被动光环确实消失） | `RacePassiveAuraCrossMapTests`（3 条：属性修正与光环效果生命周期不一致的量化证明、`EnterMap` 重放后光环/属性/层数正确且幂等、未设置种族时静默跳过） | `core/carriers/unit/contracts/PlayerUnit.cs`（新增 `RaceId`）、`core/carriers/unit/core/UnitPersistable.cs`（`player.race_id` 段）、`core/foundation/save_system/contracts/SaveSections.cs`（新增 `PlayerRaceId`）、`core/rules/assembly/RulesAssembly.cs`（新增 `ReapplyRacePassiveAuras`，幂等）、`core/gameplay/assembly/GameplayAssembly.cs`（`EnterMap` 装备重放后跨图重放种族被动光环） | `dotnet test` Tests.Carriers/Tests.Gameplay 全绿（本项 3 条 + `UnitPersistableTests` 5 条 `RaceId` 覆盖） | `core/rules/assembly/README.md`、`core/gameplay/assembly/README.md` 判断记录 10、`core/carriers/unit/README.md` 判断记录 13（修订旧判断记录"框架不持有种族引用"的不成立结论）。存档格式变更：新增可选段 `player.race_id`，旧档缺失时清空为 `null`；调用方需在 `RulesAssembly.RegisterUnit` 传入非空 `raceId` 时同步写入 `PlayerUnit.RaceId`（框架不自动同步） |
| owner/day/vendor 装配扩展点 | **成立**（`GameplayAssembly` 构造 `QuestHost`/`DialogHost` 时对三者恒不提供，未在自己的构造函数开放对应参数；核实过程中进一步发现三处示例组合根同样未转发，见下"第 0 步补录"） | `GameplayAssemblyOwnerDayVendorExtensionPointTests`（4 条：真实 gossip `vendor` 动作触发回调、真实"宠物击杀记账给主人"任务进度场景含对照组、真实每日任务按转发的天数来源判定可再接）+ `GameFoundationBootstrapQuestDayProviderTests`（1 条，新增，见"第 0 步补录"） | `core/gameplay/assembly/GameplayAssembly.cs` 构造函数末尾新增 `questOwnerResolver`/`questDayProvider`/`vendorOpenRequested` 三个可选参数直接转发；**第 0 步补录**：`games/_template/Runtime/GameOptions.cs`（新增三个同名可选字段）、`GameBootstrap.cs`、`adapters/unity/.../GameFoundationBootstrap.cs`、`adapters/unity/.../FrameworkResidentHost.cs`（各新增三个同名可选公开属性）均已补齐透传，全部默认仍是 `null` | `dotnet test` Tests.Gameplay 全绿（核心侧 4 条）；Unity PlayMode 全绿（第 0 步新增 1 条，真实 `GameFoundationBootstrap` 组合根注入 `QuestDayProvider` 后每日任务当天不可再接/次日重新可接） | `core/gameplay/assembly/README.md` 判断记录 12（补录段落）+"回调接线矩阵"新增三行；`architecture/08` 第 9.1 节（含 2026-09-08 补录段）；`architecture/落地计划/落地方案与分阶段计划.md` 能力索引"owner/day/vendor 回调"行行号/措辞随本轮改动同步更正（详见下节） |
| 动画剪辑事件登记契约差异（`UnityViewFactory.RegisterModelClipEvents` 直接 `Resources.Load<AnimationClip>` + 整体覆盖 events） | **成立**（codex 原列为"静态契约差异，未计入确认缺陷"；本轮判定为真实契约违反，已根治） | `ModelClipEventIsolationTests`（3 条：首个 anim_set 合并不覆盖美术自带事件、不同签名 anim_set 经 `AnimatorOverrideController` 隔离互不影响、相同签名复用共享资产不产生多余覆盖） | `core/foundation/engine_adapter/contracts/IResourceLoader.cs`（`ResourceKind` 新增 `AnimationClip`）、`UnityResourceLoader.cs`（新增 `TryLoadAnimationClipSync`/`TryGetAnimationClip`）、`UnityRenderer3D.cs`（`ResolveLegacyClip` 改经加载器，新增 `ApplyAnimClipOverride`）、`UnityViewFactory.cs`（`RegisterModelClipEvents` 改为合并+按 anim_set 隔离） | Unity PlayMode 全绿；`ModelClipEventIsolationTests.cs` 全 3 条 | `architecture/02` 第 1.7 节勘误（`animation_clip` 种类）、`09` 第 4.3 节勘误、`14` 第 4.3.1 节勘误、ADR-0017 修订记录 |
| pytest 子进程编码崩溃 | **成立**（本机 `locale.getpreferredencoding()` 实测为 `cp936`/GBK，子进程 stdout/stderr 解码未指定编码；用最小 PowerShell 输出复现乱码/潜在 `UnicodeDecodeError`） | `test_get_framework_path_boundary.py` 四组合实测（默认编码+PS5.1、UTF-8+PS5.1 各 60 passed；pwsh 两组合本机未装，未能验证，机制已具备） | `toolchain/tests/test_get_framework_path_boundary.py`（`_run_script` 显式传 `encoding="utf-8", errors="replace"`，测试运行器改为 `_find_powershell_executables`）、`check.ps1`/`.githooks/pre-commit`（跑 Python 前设 `PYTHONUTF8=1`） | `pytest toolchain/tests -q` 60 passed（含真实 `dist/ws-game-1.6.0.zip`/`.lock` 回归） | `architecture/11` 新增提交门槛条目（技术无关措辞）；`.github/workflows/ci.yml` 补判断记录说明本地/CI 编码要求一致 |
| `Get-FileHash` 依赖（`get_framework.ps1` 等） | **成立**（本机原因未查明，怀疑 `PSModulePath`；用模拟脚本复现该 cmdlet 不可用场景并验证兜底路径可用） | 隔离脚本模拟 `Get-FileHash` 抛错，新增 `Get-Sha256FileHash`（.NET SHA256 流式计算）与移除模拟后真实 `Get-FileHash` 对同一份文件计算的哈希逐字节一致 | 新增 `toolchain/_hash.ps1`（`Get-FileHash` 可用时优先用，否则/调用失败时透明退化到 .NET SHA256 兜底）；`get_framework.ps1`/`sync_package_content.ps1`/`build.ps1` 三处哈希校验改用该共用函数 | `check.ps1 -SkipUnity -Quick` 20 步全部 PASS，用时 68.9s；`.githooks/pre-commit` 单独完整跑一遍 20/20 PASS | `architecture/04` 新增"校验覆盖矩阵"（5.1 节，与本条无直接关联，属同批 WC 文档核实产出，一并列入下节处理表） |

## 文档更新与边界清单（对照 codex `AUDIT_REPORT.md`"文档更新与边界清单"节逐条处理）

| codex 建议 | 处理 | 落点/提交 |
|---|---|---|
| 旧 pending 字段迁移承诺改为安全策略（无法映射时安全丢弃，只有明确映射时才迁移） | 已处理：`GobjPendingLootPersistable.Load` 按值映射能力分流（能映射则映射，否则安全丢弃）；`CHANGELOG.md` 对应迁移说明措辞同步改为"处理口径以 10 号文档当前记载为准，不承诺与早期草稿描述一致" | A（核心侧）、C（`CHANGELOG.md`） |
| `SaveSystem` 文档明确缺段默认清空，为"缺段即保留"能力显式声明例外 | 已处理：`save_system/README.md`"Save / Load 失败语义"节改写，`IPersistable.KeepStateWhenSectionMissing` 落地 2 处例外并各自写明理由（`UnitPersistable` 地图/坐标三段互相耦合、`RngStreamsPersistable` 无合法空默认状态） | A（核心侧） |
| API 改名列为 breaking change 或提供一个 MINOR 周期的 Obsolete alias | 已处理：`GameObjectHost` 补回 `[Obsolete]` 转发别名。**本轮核实并更正**：WC 在核实过程中把 `CHANGELOG.md` 1.6.0 段落改写为"本版本已补回旧名 Obsolete 转发"——核对 `GameObjectHost.cs` 实际改动时间线（该转发方法是 WA 在**本轮**加入，晚于 1.6.0 发布提交 `85f1f4f`），确认这一表述把本轮才落地的修复误记成已随 1.6.0 一并发布，属实际改写错误；已在提交 A 落地前撤回该条目，改为随本轮 1.7.0 变更记录如实登记（见下 step 3） | A（撤回 1.6.0 段错误条目）、待提交 1.7.0 变更记录（step 3） |
| 统一 `slot_mesh` 的资源种类、loader 和 renderer 合同 | 已处理：`mesh_ref` 复用 `ResourceKind.Model` 同一命名空间，`architecture/02` §1.7/`09` §4.1/`14` §1.2、§4.3.1 均已勘误，ADR-0017 补修订记录 | B（表现/引擎侧） |
| 文档中保留当前样例缺少完整 `item.template` 行的限制 | 已处理（原样保留边界，未扩张结论）：验收测试（`SlotMeshResourceContractTests.cs`）证明的是资源加载合同层（`loader`+`renderer`）的正确性，未新增让样例走完整背包→装备→View 生产入口的数据；`02`/`09`/`14` 文档改写均只勘误资源合同本身，未做出"完整 inventory→View 入口已验证"这一超出范围的断言，与 codex 边界说明一致 | B（无需新增改动，核实确认） |
| 未实现/未接入/非目标项应继续分栏，不能合并写成同一"框架缺失"（owner/day/vendor 装配参数、`SimTime` 生产接线、`Replay` 入口、`DisplayMapCoverageRule`、VFX anchor、武器 style resolver 等） | 已处理：`落地方案与分阶段计划.md`"能力边界与未默认接入能力索引"沿用四栏分类（未实现/已实现未默认接线/明确非目标/已归档），逐条列出上述各项各自的锚点与边界说明，未合并概括；owner/day/vendor 一行本轮随 `GameplayAssembly`/三处组合根改动同步更正行号与措辞（详见"核实表"owner/day/vendor 一行）；根 `README.md` 能力边界索引指针同步 | C（工具链与文档）+ 本轮追加（落地计划行号更正，待提交 step 3） |
| Unity、性能、真实游戏 Runtime 和用户验收不能由 Native 或静态检查替代 | 已遵守：本轮验收全部经真实 `dotnet test`/真实 Unity `-batchmode` 编译检查/EditMode/PlayMode/独立版冒烟/消费方演练完成，未用静态检查或 Native 断言替代任何一项，见下"验收"节 | 全部提交 |

## 四类能力状态核对（`落地方案与分阶段计划.md`"能力边界与未默认接入能力索引"）

- **owner/day/vendor 回调**：分类维持"已实现未默认接线"（本轮 `GameplayAssembly` 补齐参数并转发进
  `QuestHost`/`DialogHost`，三处示例组合根也补齐透传，但三处生产装配根默认仍不提供实现，游戏层
  仍需自行接入才能观察到效果，符合该分类定义，未升级为"已实现且默认接线"）；索引行的源码行号
  （`GameplayAssembly.cs:539-540/718`、`GameFoundationBootstrap.cs:330`、`FrameworkResidentHost.cs:337`）
  与措辞随本轮改动同步更正（原引用的 `518-519`/`694`/`310`/`305` 因本轮新增代码行数偏移已过期）。
- **日任务与自动 Quest 驱动（`Quest.Update`）**：分类不变（"已实现未默认接线"），本轮未改动
  `QuestHost.Update` 本体或任一装配根的调用关系，仅因 owner/day/vendor 改动导致三处组合根源码
  行号偏移，随附行号已一并更正（详见上一条同一表格行）。
- **`mesh_ref`/动画剪辑事件**：均为缺陷修复（AUD-05 与静态候选确认），不属于"能力边界索引"覆盖的
  "契约已就位、默认不接入"范畴，不在该索引表登记，本轮也未新增登记项——`02`/`09`/`14` 三份文档的
  勘误段落已完整记录这两条契约的现状（见"核实表"对应行）。
- **工具链两条**（pytest 编码、`Get-FileHash` 依赖）：均为工具链健壮性缺陷修复，不涉及游戏层
  能力边界，不在该索引表覆盖范围内，未新增/移除任何登记项。

## 验收

前台实跑 `powershell -ExecutionPolicy Bypass -File check.ps1`（不加 `-SkipUnity`/`-Quick`，执行前
`tasklist | findstr /i "Unity.exe"` 已确认 Unity 编辑器本体未占用）：

**第一次实跑**（提交 A/B/C/D 落地后）：23 步中 1 步 FAIL——Unity PlayMode 测试 7 例失败
（`DiscreteCombatTests` 3 例、新增的 `GameFoundationBootstrapQuestDayProviderTests` 1 例、
`SharedBootstrapDiscreteTests` 3 例），根因是新增的 `GameFoundationBootstrapQuestDayProviderTests`
把测试数据放在 `Tests/Runtime/TestData/QuestDayProviderOverlay/` 子目录下，`FileSystemDataSource`
按整个数据根递归扫描表文件、不把子目录当隔离命名空间——`DiscreteCombatTests`/
`SharedBootstrapDiscreteTests` 直接把 `Tests/Runtime/TestData` 整个目录当数据根，因此把新表一并
加载，被 `text_key_exists` 内容校验规则牵连报错；同时新表自身遗漏了 `l10n.text` 条目，本类自身
用例也因此失败。已根治并单独提交 `8ea2106`：测试数据改放到与 `TestData/` 完全平级的独立目录
`TestDataQuestDayProvider/`，补齐遗漏的 `l10n.text` 条目。

**第二次实跑**（fix 落地后）：全部 23 步通过：

| 步骤 | 结果 | 用时(s) | 详情 |
|---|---|---|---|
| 门禁自检：`Test-NativeExitCode` | PASS | 0.3 | — |
| `dotnet build Core.sln -c Release` | PASS | 6.0 | 0 警告 0 错误 |
| `dotnet test`（六项目） | PASS | 4.9 | Foundation 659/659、Numbers 107/107、Carriers 323/323、Rules 404/404、PresentationCommon 491/491、Gameplay 476/476 |
| `validate_data.py`（合并根） | PASS | 3.7 | 60 tables/285 records/0 errors/0 warnings/1 override |
| `validate_data.py --data-root data/_framework` | PASS | 1.4 | 5 tables/124 records/0 errors/1 warning（缺 l10n 表，既有已知情况） |
| `gen_event_constants.py --check` | PASS | 0.1 | 一致（90 个常量） |
| `gen_placeholder_assets.py --check` | PASS | 0.1 | 通过 92/92，失败 0 |
| `import_assets.py check --dataset _sample` | PASS | 0.1 | 0 问题 |
| `pytest toolchain/tests -q` | PASS | 7.3 | 60 passed |
| 禁用词扫描：全仓库不出现具体游戏代号 | PASS | 9.0 | 0 命中 |
| 禁用词扫描：architecture 正文技术名（immunity 例外） | PASS | 0.0 | 0 真实命中 |
| 版本一致性 | PASS | 0.0 | VERSION=1.6.0，两个 `package.json`、`packages-lock.json`、`CHANGELOG.md` 一致（本轮未提交版本号，见下"关于 VERSION"说明） |
| `build.ps1 -SkipTests`（同步 DLL） | PASS | 3.5 | 六个 DLL 哈希核对通过 |
| 包清单一致性 | PASS | 8.0 | 三个 npm 包 version=1.6.0 一致，`npm pack --dry-run` 清单不含排除项，adapter.unity 含 model/anim 占位资产与生成器 |
| Unity 编译检查 | PASS | 15.8 | 退出码 0 |
| Unity EditMode 测试 | PASS | 8.8 | total=53 passed=53 failed=0 |
| Unity PlayMode 测试 | PASS | 40.4 | total=241 passed=241 failed=0（基线 231 + AUD-05/剪辑事件共 9 + owner/day/vendor 组合根验收 1） |
| 独立版构建 + `-gf-smoke` 冒烟（连续模式） | PASS | 21.5 | — |
| 独立版 `-gf-smoke-discrete` 冒烟（离散模式） | PASS | 3.3 | — |
| IL2CPP 独立版构建/冒烟 ×3 | SKIP | 0 | 未传 `-Il2cpp`（任务未要求） |
| 消费方演练（`toolchain/consumer_smoke.ps1`） | PASS | 112.8 | — |

**汇总：23 步（20 PASS + 3 SKIP），0 FAIL，总用时 247.0s。** 门禁执行时工作树除本文档与 step 3
两处改动（`CHANGELOG.md` 1.7.0 段落、`落地方案与分阶段计划.md` 第十一方审核小节）待提交外全部
干净，门禁本身不产生任何额外改动；本文档与 step 3 提交落地后 `git status --short` 为空。

**关于 VERSION**：本轮任务范围明确"不跑 `build.ps1 -Release`"，因此不在本轮改动 `VERSION`
文件；`VERSION` 与 `CHANGELOG.md` 顶部即将新增的 `[1.7.0]` 条目暂不一致，与历次审核跟进（版本记录
先于实际发版落地）惯例一致——版本号写回、`git tag`、发布 zip/lock 均在后续独立的
`build.ps1 -Release` 流程中一次性完成，不属于本次审核修复与文档整合的范围。

自检复核：

- 全仓库禁用具体游戏代号扫描（`check.ps1` 内置扫描步骤，同一禁用词）：0 命中。
- `architecture/00～14` 与 `architecture/adr/` 正文技术名 grep（`unity|c#|csharp|dotnet|\.net|
  nunit|python|newtonsoft|il2cpp|powershell|github|verdaccio|npm`）：仅 `immunity` 既有豁免误报，
  0 真实命中（含本轮新增的 08 第 9.1 节补录段、02/09/14 勘误、ADR-0017 修订记录）。
