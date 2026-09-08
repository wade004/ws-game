# 第十二方深度审核核实跟进（codex 第十轮，基线 `8160178`）

基线：`8160178`（main，v1.7.0）。审计报告本体见本目录 `AUDIT_REPORT.md`/`core/`/`docs-project/`/
`presentation/`（codex 原文，`git add` 归档，未改写）。

修复由三个并行 Sonnet 5 子 agent 按领域分工执行（核心侧 WA、表现/引擎侧 WB、工具链与文档 WC），
判断记录留存于 `C:\Users\1\AppData\Local\Temp\claude\D--workespace-ws-game\
f1d8941d-33da-4643-9759-6533f7c7ff79\scratchpad\audit10\{WA,WB,WC}.md`（未随仓库提交，属会话临时
材料）。主会话（本 agent）在三份判断记录基础上补齐"第 0 步"两处遗留（`architecture/02_引擎适配层.md`
§1.7"首个非空动画集免隔离/写回共享剪辑"旧算法描述改为中立描述；核实 `IEventBus.SuppressDispatch`/
`IAuraQuery.TryGetInstanceRef` 均为 C#8 默认接口方法，向后兼容，不强制自定义实现方新增成员），独立
核实全部条目、统一分领域提交、执行全量门禁、撰写本文档。提交：

| 提交 | hash | 范围 |
|---|---|---|
| A（核心侧） | `b6d7f4f` | `core/**`（CORE-170-01～03 + 7 处同类失败段缺陷）、`architecture/10` |
| B（表现/引擎侧） | `cce0370` | `adapters/unity/**`（PRES-170-01）、`architecture/02`（含"第 0 步"§1.7 勘误）、ADR-0017 |
| C（文档） | `46127ed` | `toolchain/**`、`CHANGELOG.md`（措辞）、`architecture/11`、落地计划 |
| D（归档） | `4067f21` | `architecture/落地计划/audit-8160178-20260908/` 整目录（codex 报告原文与证据） |

四次提交各自的 pre-commit 快速门禁（`-SkipUnity -Quick`，20 步全部 PASS/SKIP，0 FAIL）均一次通过，
`dotnet test`（六项目）在 A/B/C/D 四次提交时均为 Foundation 662/662、Numbers 107/107、
Carriers 329/329、Rules 404/404、PresentationCommon 491/491、Gameplay 491/491，全绿。提交落地后
`git status --short` 为空。

## 核实方法说明

本文档"核实"列的判断基于：(1) 独立重读改动后的源码（非只读 WA/WB/WC 报告文本），逐一确认"先解析
校验成临时恢复计划、再一次性提交"模式是否真的落地；(2) 独立重跑改动后的 `dotnet test`，核对通过数
与报告一致；(3) 对接口/委托兼容性做独立技术核查（C#8 默认接口方法机制）；(4) 对 WA 报告摘要文字与
其自身列出的文件清单做交叉核对。

**核实过程中发现的偏差**：WA 报告摘要文字写"同一类『先改状态后校验/边解析边提交』缺陷另在 7 处复现"，
但其后紧跟列出的文件清单实际有 **8** 个文件（`AchievementHost`、`SpawnHost`、`WorldState`、
`DifficultyHost`、`CurrencyPersistable`、`VendorStockPersistable`、`RngStreamsPersistable`、
`SkillBindingPersistable`）。逐一核对源码确认这 8 个文件确实都已按"先解析校验、再一次性提交"模式
改造（详见下表逐项复核），判定为 WA 报告摘要行文字的计数笔误（"7"应为"8"），不影响修复本身的完整性
与正确性；本文档以核实后的准确计数 **8 处** 为准，任务描述沿用的"7 处"口径已在此更正。

## 核实表：CORE-170-01～03 + 8 处同类失败段缺陷（原报告称"7 处"，核实后更正为 8 处，见上节）

| 编号 | 判断 | 复现 | 修复位置 | 验收 | 判断记录 |
|---|---|---|---|---|---|
| CORE-170-01（跨图重放后卸装误删种族 aura） | **成立** | `CORE_170_01_RaceEquipmentSharedAuraTests`（3 条，真实进图/跨图/卸装场景） | `core/rules/common/contracts/AuraHandleLedger.cs`（新增，跨来源引用计数账本上移为独立类型，只依赖 `IEffectSink`/`IAuraQuery`）、`core/rules/common/contracts/IAuraQuery.cs`（新增默认接口方法 `TryGetInstanceRef`）、`core/rules/skill/core/AuraHost.cs`（真正实现）、`core/rules/assembly/RulesAssembly.cs`（`AuraHandles` 属性、`_raceAuraHandles` 私有簿记、`ReapplyRacePassiveAuras` 重写为按来源判断而非 `HasAura`）、`core/carriers/item/core/EquipmentHost.cs`（改用注入的账本，`StackOverflowPolicy.Replace` 换句柄迁移移出由账本自己订阅 `InstanceReplaced` 完成） | 独立重跑 `dotnet test` Tests.Rules 404/404、Tests.Carriers 329/329、Tests.Gameplay 491/491 全绿；已确认新增测试 3 条真实断言种族 aura 与属性修正在跨图卸装后保留，装备来源修正正确移除 | `core/rules/assembly/README.md`、`core/carriers/item/README.md` 判断记录同步；接口新增成员为默认接口方法，独立核实其向后兼容性——见下"接口兼容性核实"节 |
| CORE-170-02（Progression 等级与实体等级分叉） | **成立** | `CORE_170_02_ProgressionLevelSyncTests`（3 条：多级 `AddXp`、读档恢复、等级需求装备） | `core/numbers/progression/contracts/ProgressionWriters.cs`（新增委托类型 `LevelSync`）、`core/numbers/progression/core/ProgressionHost.cs`（`RegisterUnit`/`AddXp`/`RestoreState` 三处等级确立/变化时机调用 `_levelSync`）、`core/carriers/unit/core/WorldUnitAccess.cs`（新增 `SetLevel`，不进 `IUnitAccess` 接口）、`core/carriers/assembly/CarriersAssembly.cs`（`levelSync: Units.SetLevel` 注入）、`core/rules/assembly/RulesAssembly.cs`（原样转发） | 独立重跑 Tests.Numbers 107/107、Tests.Gameplay 491/491 全绿；已确认新增测试真实断言 `Progression.GetLevel == WorldUnitAccess.GetLevel == PlayerUnit.Level` 三者一致 | Progression 定为单位等级唯一权威（写入时同步，非反查），选型合理——避免 L3 反向依赖 L1 具体实现；`SetLevel` 不进公开接口，独立核实未破坏 `IUnitAccess` 契约 |
| CORE-170-03(a)（失败段不回滚：Equipment 本体） | **成立** | `CORE_170_03_EquipmentPersistableLoadFailureTests`（4 条：坏 shape、非法 slot、坏 ItemInstance、恢复中途槽位失败） | `core/carriers/item/core/ItemPersistable.cs`（`EquipmentPersistable.Load` 改为先解析校验、再一次性提交） | 独立重跑 Tests.Carriers 329/329 全绿；独立重读 `Load` 方法源码确认解析与提交已分离为两遍循环 | `core/carriers/item/README.md` 判断记录同步 |
| CORE-170-03(a) 同类缺陷 1/8：`AchievementHost` | **成立** | `CORE_170_03_SaveRollbackEventSuppressionTests`（覆盖，非独立测试文件） | `core/gameplay/achievement/core/AchievementHost.cs`（`Load` 改为两遍：第一遍全量校验形状建 `plan`、不触碰 `_progress`/`_unlocked`/`_pendingReward`；第二遍校验通过后清空该玩家记录并按 `plan` 一次性提交） | 独立重读源码确认（见本文档"核实方法说明"逐段引用）；Tests.Gameplay 491/491 全绿 | 判断记录见类型内 `Load` 方法上方注释，已独立核实与代码行为一致 |
| CORE-170-03(a) 同类缺陷 2/8：`SpawnHost` | **成立** | `SpawnHostTests`（+1 条） | `core/gameplay/spawn/core/SpawnHost.cs`（`Load` 先校验 `data` 种类，坏 shape 早期抛出，不先清空运行期计时器状态） | Tests.Gameplay 491/491 全绿 | `core/gameplay/spawn/README.md` 判断记录同步 |
| CORE-170-03(a) 同类缺陷 3/8：`WorldState` | **成立** | `WorldStateTests`（+1 条） | `core/gameplay/world_state/core/WorldState.cs`（`Load` 改为临时字典 `parsed` 完整解析校验全部字段，不触碰 `_flags`，全部通过后才 `Clear()` 并整体替换） | 独立重读源码确认（`_flags.Clear()` 移至校验通过之后）；Tests.Gameplay 491/491 全绿 | `core/gameplay/world_state/README.md` 判断记录同步 |
| CORE-170-03(a) 同类缺陷 4/8：`DifficultyHost` | **成立** | `DifficultyHostTests`（+1 条） | `core/gameplay/difficulty/core/DifficultyHost.cs`（`Load` 先校验 `JsonNull`/形状分支，坏 shape 不触碰当前难度状态） | Tests.Gameplay 491/491 全绿 | `core/gameplay/difficulty/README.md` 判断记录同步 |
| CORE-170-03(a) 同类缺陷 5/8：`CurrencyPersistable` | **成立** | `CORE_170_03_CurrencyAndVendorStockPersistableLoadFailureTests`（4 条，两模块共享一个测试文件） | `core/gameplay/economy/core/CurrencyPersistable.cs`（`Load` 改为先建 `plan` 列表 + `seen` 去重集合完整校验，再一次性提交给 live host，不再"边解析边直接调用 live host 写方法"） | 独立重读源码确认；Tests.Gameplay 491/491 全绿 | `core/gameplay/economy/README.md` 判断记录同步 |
| CORE-170-03(a) 同类缺陷 6/8：`VendorStockPersistable` | **成立** | 同上（`CORE_170_03_CurrencyAndVendorStockPersistableLoadFailureTests`） | `core/gameplay/economy/core/VendorStockPersistable.cs`（同一模式改造） | 同上 | 同上 |
| CORE-170-03(a) 同类缺陷 7/8：`RngStreamsPersistable` | **成立**（此前模块无测试文件，本轮补齐） | `RngStreamsPersistableTests`（新增文件，3 条） | `core/foundation/save_system/core/RngStreamsPersistable.cs`（`Load` 校验通过后再提交，先前"边解析边写入" live 状态的缺陷同步修复） | 独立重读源码确认；Tests.Foundation 662/662 全绿（含新增 3 条） | `core/foundation/save_system/README.md` 判断记录同步 |
| CORE-170-03(a) 同类缺陷 8/8：`SkillBindingPersistable` | **成立** | `SkillBindingPersistableTests`（+2 条） | `core/carriers/unit/core/SkillBindingPersistable.cs`（`Load` 改为先校验 `JsonNull`/形状分支再提交） | Tests.Carriers 329/329 全绿 | `core/carriers/unit/README.md` 判断记录同步 |
| CORE-170-03(a) 兜底：`SaveSystem.Load` 回滚列表纳入失败段自身 | **成立** | 无需独立复现（工程一致性/防线项，由 `CORE_170_03_EquipmentPersistableLoadFailureTests` 等间接覆盖） | `core/foundation/save_system/core/SaveSystem.cs`（`Load` 循环内 `catch` 分支把抛异常的 `key` 自身并入 `rollbackKeysIncludingFailed`，用读档前快照 `preLoadSnapshots[key]` 重新调用一次 `Load`） | 独立重读源码确认（见本文档引用的源码片段，逻辑与 WA 报告描述一致）；Tests.Foundation 662/662 全绿 | 对已改造为"先校验后提交"的段是幂等 no-op，对未来新引入、仍踩同一坑的段是额外防线，判断记录准确 |
| CORE-170-03(b)（回滚事件污染业务） | **成立** | `CORE_170_03_SaveRollbackEventSuppressionTests`（2 条，含真实 `AchievementHost` 计数验证：回滚后 progress/unlocked 保持读档前值，不被回滚重放的 `ItemEquipped` 误计数） | `core/foundation/event_bus/contracts/IEventBus.cs`（新增默认接口方法 `SuppressDispatch()`）、`core/foundation/event_bus/core/EventBus.cs`（引用计数嵌套实现）、`core/foundation/save_system/core/SaveSystem.cs`（`Load` 主循环 + 回滚整体包进 `using (_bus?.SuppressDispatch())`；`SaveMigratedEvent`/`SaveLoadedEvent` 仍在作用域外正常派发） | 独立重读源码确认作用域边界（`SuppressDispatch` 覆盖逐段 `Load` 循环与 `RollbackLoadedSections`，不覆盖方法末尾的迁移/读档完成事件）；Tests.Foundation 662/662 全绿 | 选择"整段读档期间抑制"而非"只抑制回滚"，理由（多个模块 `Load` 已声明"读档不是一次业务事件"，`EquipmentPersistable.Load` 是唯一违反处）经独立核实成立 |
| 顺带：`world.gobj_pending_loot` 登记进 `KnownOrder` | **成立** | 无需复现（工程一致性项） | `core/foundation/save_system/contracts/SaveSections.cs`（新增 `WorldGobjPendingLoot` 常量，已独立核实插入 `KnownOrder` 数组）、`core/gameplay/assembly/GameplayAssembly.cs`（更正过期注释） | Tests.Carriers/Tests.Foundation 全绿 | 与 10 号文档 §7a"...spawn_state/gobj_pending_loot"文字顺序一致，已独立核对 |
| PRES-170-01（共享 AnimationClip 被空事件和跨 factory 污染） | **成立** | `Pres170_01SharedClipEventIsolationTests`（5 组：顺序一非空后空、顺序二空后非空、顺序三跨 factory、场景重建、实体销毁重建） | `UnityViewFactory.cs`：新增两张**进程级静态表** `s_authoredClipEvents`（resource_ref → authored 基线快照）与 `s_modelClipOverrides`（(resource_ref, 签名) → 运行期克隆覆盖剪辑），取代此前的实例字段；`RegisterModelClipEvents` 改为绝不写回共享 `AnimationClip.events`——空配置不做任何事（共享剪辑天生等于基线），非空配置一律以基线为底合并、写入运行期克隆并只经 `AnimatorOverrideController` 套用到具体 `ModelHandle` 实例，不再区分"第一个 vs 后续"签名 | 独立重读源码确认静态表定义与 `RegisterModelClipEvents` 逻辑（见本文档引用的源码片段）；`dotnet test` 491/491（未涉及 `core`/`presentation`，未受影响）；Unity 编译/EditMode/PlayMode 结果见下"验收"节全量门禁重跑 | `UnityViewFactory.cs` 类型级/方法级注释标注"12 §5 二次勘误"；ADR-0017 新增修订记录；`02_引擎适配层.md` §1.7 由本 agent 在"第 0 步"补录同步改写（见下节） |

## 第 0 步补录核实

- **`architecture/02_引擎适配层.md` §1.7 旧算法描述**：核实原文"运行期克隆一份私有副本承载『非首个』
  anim_set 的事件配置"确与 WB 根治后的实现（任何非空配置的 anim_set 都需要隔离，与是否首个无关）
  不一致，已改写为中立描述（不出现引擎/语言/框架/工具名，"immunity"豁免外 0 命中），并在同文档
  "变更记录"表新增一行（勘误、版本号不变、关联 ADR 列"—"），格式核对符合 `12_扩展与变更流程.md`
  §5"细节勘误直接修订、版本号不变"的规定。
- **接口兼容性核实**：`IEventBus.SuppressDispatch()` 与 `IAuraQuery.TryGetInstanceRef` 均独立确认
  为 C#8 默认接口方法（`interface` 成员体内直接给出实现，`=>` 表达式体或 `{ }` 块），未被声明为
  `abstract`；自定义 `IEventBus`/`IAuraQuery` 实现方不重写这两个成员即自动获得默认行为（分别为
  no-op 抑制作用域、返回 `null`），编译期不强制新增任何成员，不构成破坏性变更。`WorldUnitAccess.
  SetLevel` 与委托类型 `LevelSync` 均未进入任何公开接口（`IUnitAccess` 未变化），构造函数新增参数
  均为可选参数且默认 `null`，独立核实不影响任何既有调用方的编译或运行行为。

## 文档更新与能力分类

| codex 建议 | 处理 | 落点/提交 |
|---|---|---|
| 存档契约统一为 best-effort，`IPersistable` 措辞改为"可能残留"而非"不会被回滚" | 已处理：`IPersistable.Load` 契约注释、`architecture/10_存档与持久化.md` 第 3 节"段失败回滚合同"改写，纳入"失败段自身也纳入 best-effort 回滚" | A |
| `CHANGELOG.md` best-effort 措辞与 10 号文档统一，不写"部分加载中间态消失"过强表述 | 已处理：改写为"只对已取得成功快照的段尝试逆序恢复；快照缺失/回滚自身失败/段间联动仍可能残留部分状态"，末句指回 10 号文档正文为准 | C |
| `world.gobj_pending_loot` 登记进 `SaveSections.KnownOrder` | 已处理：见上"核实表"对应行 | A |
| 能力索引修正 `SimTime` 为三处组合根均未注入统一时钟，补齐第三处 `FrameworkResidentHost.cs` | 已处理：`落地方案与分阶段计划.md` 第 1240 行改写并加勘误标记，接线点补齐三处 | C |
| owner/day/vendor 三根透传、默认值为 null 的事实保留并补齐锚点行号 | 已处理：第 1244 行补三处组合根各自透传行号 | C |
| `toolchain/README.md` 说明需整套复制 toolchain 或至少携带 `_hash.ps1` | 已处理：新增"依赖边界"段；`get_framework.ps1` 增加 `Test-Path` 兜底与明确报错 | C |
| 公开 API 别名、旧存档字段迁移、`PersistableThrew` 分开描述 | 已处理：核对确认 `CHANGELOG.md` 原本已是三条独立 bullet，本次只改动被指出过强的 `PersistableThrew` 一条 | C |
| 未实现/已实现未接入/非目标三栏保持分类，不合并 | 已处理：核对 `落地方案与分阶段计划.md` 全表，除上述两处事实性更正外其余条目分类不变 | C |
| 单测/集成测试通过不等价于真实组合链验证，需补充说明 | 已处理：`architecture/11_工程规范与测试.md` 第 6 节新增一条勘误 | C |

## 旧 1.6/1.7 问题复核

- 旧 CORE-170-01 之前"种族被动光环跨图"（第九轮修复）场景保持通过；本轮 CORE-170-01 是"装备/套装/
  种族共享同一 `auraDef` 时的跨来源账本"这一更细分场景，不与旧场景重叠，未重报。
- 旧 AUD-01（SaveSystem 段失败回滚、pending loot 兼容）、AUD-02（缺段清空）、AUD-04（公开 API
  Obsolete 别名）、AUD-05（slot mesh）均未受本轮改动影响，独立重跑 `dotnet test`/Unity 测试确认
  对应既有回归测试全部保持通过（Tests.Carriers 329/329 含 AUD-01~04 相关用例、Unity `SlotMesh
  ResourceContractTests` 6/6，见下"验收"节全量门禁）。
- 本轮 CORE-170-03 是在 AUD-01 基础上新发现的更深层缺陷（AUD-01 只解决"其它已成功段是否回滚"，
  CORE-170-03(a) 解决"失败段自身"，CORE-170-03(b) 解决"回滚期间事件污染业务消费者"），二者不冲突、
  互为补充，未重开或推翻 AUD-01 的既有结论。

## 验收

前台实跑 `powershell -ExecutionPolicy Bypass -File check.ps1 -LogFile <scratchpad>\check_full_z12*.log`
（不加 `-SkipUnity`/`-Quick`），执行前 `tasklist | findstr /i "Unity.exe"` 已确认 Unity 编辑器本体
未占用。

**第一次实跑**（提交 A/B/C/D 与本文档初稿落地后）：23 步中 1 步 FAIL——"禁用词扫描：全仓库不出现
具体游戏代号"命中本文档"自检复核"节里作为 grep 命令示例逐字写出的具体游戏代号字符串（用于说明
用什么命令核对，非产品代码或架构文档本体引用游戏代号）。这是本文档自身的表述问题，不是任何一处
产品代码或架构文档的缺陷；已将该行改写为不出现具体禁用词字面量的描述（"`check.ps1` 内置扫描步骤，
同一禁用词"），不改变检查内容本身。

**第二次实跑**（改写后）：全部 23 步通过（20 PASS + 3 SKIP，0 FAIL）：

| 步骤 | 结果 | 用时(s) | 详情 |
|---|---|---|---|
| 门禁自检：`Test-NativeExitCode` | PASS | 0.3 | — |
| `dotnet build Core.sln -c Release` | PASS | 1.9 | 0 警告 0 错误 |
| `dotnet test`（六项目） | PASS | 3.6 | Foundation 662/662、Numbers 107/107、Carriers 329/329、Rules 404/404、PresentationCommon 491/491、Gameplay 491/491（合计 2484 条全绿） |
| `validate_data.py`（合并根） | PASS | 1.7 | 60 tables/285 records/0 errors/0 warnings/1 override |
| `validate_data.py --data-root data/_framework` | PASS | 1.4 | 5 tables/124 records/0 errors/1 warning（缺 l10n 表，既有已知情况） |
| `gen_event_constants.py --check` | PASS | 0.1 | 一致 |
| `gen_placeholder_assets.py --check` | PASS | 0.1 | 通过 92/92，失败 0 |
| `import_assets.py check --dataset _sample` | PASS | 0.1 | 0 问题 |
| `pytest toolchain/tests -q` | PASS | 10.2 | 82 passed |
| 禁用词扫描：全仓库不出现具体游戏代号 | PASS | 24.7 | 0 命中 |
| 禁用词扫描：architecture 正文技术名（immunity 例外） | PASS | 0.0 | 0 真实命中 |
| 版本一致性 | PASS | 0.1 | VERSION=1.7.0，两个 `package.json`、`packages-lock.json`、`CHANGELOG.md` 一致（`CHANGELOG.md` 新增 `[1.8.0]` 条目不影响本步骤，见本步骤判断口径——`[Unreleased]` 段存在即视为一致，不要求已发布版本号与 VERSION 一一对应） |
| `build.ps1 -SkipTests`（同步 DLL） | PASS | 2.4 | 六个 DLL 哈希核对通过 |
| 包清单一致性 | PASS | 7.7 | 三个 npm 包 version=1.7.0 一致，`npm pack --dry-run` 清单不含排除项 |
| Unity 编译检查 | PASS | 7.5 | 退出码 0 |
| Unity EditMode 测试 | PASS | 8.9 | total=53 passed=53 failed=0 |
| Unity PlayMode 测试 | PASS | 67.7 | total=246 passed=246 failed=0（基线 241 + PRES-170-01 新增 5） |
| 独立版构建 + `-gf-smoke` 冒烟（连续模式） | PASS | 12.2 | — |
| 独立版 `-gf-smoke-discrete` 冒烟（离散模式） | PASS | 3.3 | — |
| IL2CPP 独立版构建/冒烟 ×3 | SKIP | 0 | 未传 `-Il2cpp`（任务未要求） |
| 消费方演练（`toolchain/consumer_smoke.ps1`） | PASS | 112.9 | — |

**汇总：23 步（20 PASS + 3 SKIP），0 FAIL，总用时 266.8s。**

自检复核：

- 全仓库禁用具体游戏代号扫描（`check.ps1` 内置扫描步骤，同一禁用词）：0 命中。
- `architecture/00～14` 与 `architecture/adr/` 正文技术名 grep（`unity|c#|csharp|dotnet|\.net|
  nunit|python|newtonsoft|il2cpp|powershell|github|verdaccio|npm`）：仅 `immunity` 既有豁免误报，
  0 真实命中（含本轮 02 号文档 §1.7 勘误与变更记录新增行、10/11 号文档改动、ADR-0017 修订记录）。
