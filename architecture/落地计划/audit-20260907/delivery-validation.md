# 交付与实现有界审计（main `8d7057c`）

审计日期：2026-09-07
审计对象：`D:/workespace/ws-game-main-audit`，`main` 基线 `8d7057c049aa623ffd916d61540608550790b801`。
边界：本报告只代表该基线的实证；历史审计文件只作为线索，不能替代本次文件、代码、数据和命令结果。

## 结论

脚本报告为绿灯，但由于 F1 的门禁误判风险，不能把门禁本身当作充分验证证据；独立命令结果另列。交付材料仍有三类需要收口的事项：

1. `check.ps1` 存在 P1 级门禁误判：原生命令 stdout 与退出码布尔值混入同一输出数组后，非零命令可被 `[bool]` 数组真值判为 PASS。
2. 资产交叉检查与仓库现有 `_sample` 数据/`_placeholder` 资产的契约不一致，命令实跑 16 个问题；`check.ps1` 没有调用该检查，因此当前门禁不会发现它。
3. 落地计划 HTML 副本落后于 Markdown，缺少 J3/K 收尾及最新门禁数字；计划 Markdown 中的 J3/K 数字属于有日期的历史段，本次不改写历史，但需要补当前 main 快照，避免读者误作当前验收结论。

编辑器实现状态是明示状态，不构成隐藏缺陷：`editor/README.md:5` 明确“实现尚未开始”，产品文档是草案；`IRenderer3D` 全部 `NotSupportedException` 也由选型/落地计划明确为降级路径。回放摘要的 HP/伤害 payload 盲区属于验收覆盖缺口，见下文。

## 覆盖矩阵与规模计数

| 范围 | 库存/范围盘点与重点核验 |
|---|---:|
| 架构正文 `00_`～`14_` | 15 篇 Markdown（本次重点逐行核对 `04`、`11`～`14`） |
| ADR `0001`～`0016` | 16 篇正文 |
| 模块 README | 81 个：`core` 63、`adapters` 4、`presentation` 12、`games` 2 |
| schema Markdown | 21 个，其中 `schema/README.md` 16 个，命名 schema 文档 5 个 |
| HTML | 16 个：架构图 14（含 `index.html`）、落地计划 1、编辑器产品文档 1 |
| 数据 JSON | 59 个；`data/_framework` 5 张表文件，`data/_sample` 54 张表文件 |
| `assets/` 文件 | 96 个；`assets/_placeholder/MANIFEST.json` 存在 |
| 版本/文件规模（补充） | `git -c core.quotepath=false ls-files`：1648 tracked files、983 `.cs`、135 `.md`、16 `.html`；这是仓库规模盘点，不是逐文件语义审核 |

HTML 覆盖不是所有架构正文的镜像：只有落地计划和编辑器产品文档各有 HTML 副本，14 个架构图页面是独立静态页面。编辑器 Markdown/HTML 的版本、H2 数和关键状态词一致（Markdown 20 个 H2，HTML 20 个 H2；均含 `v1`、草案和 M5）。

## 门禁和独立验证

先检查 `check.ps1` 头部副作用说明及参数：`check.ps1:61-73` 的默认产物位于 `bin/`，生成器使用 `--check` 只读模式；`check.ps1:82-137` 的 `-LogFile` 会创建日志目录并启用 transcript。随后执行指定命令：

```powershell
check.ps1 -SkipUnity -LogFile bin/audit-20260907/check.log
```

结果：退出码 `0`，总计 17 步，其中 **11 PASS、6 SKIP**（Unity 编译、EditMode、PlayMode、Mono 独立版构建/连续冒烟、离散冒烟、消费方演练），没有 FAIL；总用时 23.9s。日志文件 `bin/audit-20260907/check.log` 已生成，命令未产生 tracked production 文件变更。当前本机探测到 Unity：`C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe`，本次遵守边界未启动 Unity 全量，因此 Unity、独立版、消费方和 IL2CPP 均未由本次审计验证。

独立命令结果：

| 命令 | 证据类型 | 结果 |
|---|---|---|
| `dotnet build Core.sln -c Release --artifacts-path ...`（门禁内） | 门禁输出 | PASS，0 警告/错误；受 F1 约束 |
| `dotnet test Core.sln -c Release --no-build` | 直接命令 | 退出 0；6 个测试程序集共 1846：Foundation 592、Numbers 84、Carriers 254、Rules 267、PresentationCommon 288、Gameplay 361，跳过 0 |
| `python toolchain/validate_data.py` | 门禁输出 | PASS；合并根由门禁记录为无阻断，受 F1 约束 |
| `python toolchain/validate_data.py --data-root data/_framework` | 直接命令 | 退出 0；5 张表、122 行、0 errors、1 warning（没有 `l10n.text` 时跳过文本键存在性检查） |
| `dotnet run --project toolchain/validator -- --data-root data/_framework --json` | 直接命令 | 退出 0；`tables=5, records=122, errors=0, warnings=1, blocking=false` |
| `python toolchain/gen_event_constants.py --check` | 门禁输出 | PASS，受 F1 约束 |
| `python toolchain/gen_placeholder_assets.py --check` | 门禁输出 | PASS，受 F1 约束 |
| `python -m pytest toolchain/tests -q` | 直接命令 | 退出 0，`19 passed` |
| 两道禁用词扫描、版本一致性、`build.ps1 -SkipTests` | 门禁输出 | PASS，受 F1 约束，未重复作为独立退出码证据 |
| `python toolchain/import_assets.py check --dataset _sample` | 直接命令 | 退出 1，16 个问题（见 F2） |
| `python toolchain/import_assets.py check --dataset _placeholder` | 直接命令 | 退出 0，但只检查到 0 条数据行，不能证明 `_sample`↔`_placeholder` 关联正确 |

上述独立命令的原始输出和退出码已分别保存为 [`direct-dotnet-test.log`](../../../bin/audit-20260907/direct-dotnet-test.log)、[`direct-pytest.log`](../../../bin/audit-20260907/direct-pytest.log)、[`direct-validator-framework.json.log`](../../../bin/audit-20260907/direct-validator-framework.json.log)；tracked 的短结果索引见 [`validation-summary.md`](validation-summary.md)。三份日志均含 `EXIT_CODE=0`；pytest 为 `19 passed`，framework validator 为 `tables=5, records=122, errors=0, warnings=1, blocking=false`。这些是独立命令证据，不能回填为门禁已可靠验证。

### F1（P1）`check.ps1` 可把“有输出且退出非零”的原生命令判为 PASS

证据：`Test-NativeExitCode` 在 `check.ps1:229-237` 直接执行 `& $Exe @ArgList`，随后 `return ($LASTEXITCODE -eq 0)`；`Invoke-CheckStep` 在 `check.ps1:162-174` 捕获整个脚本块输出，只有整个结果是裸 `[bool]` 才直接使用，否则对结果执行 `[bool]`。因此命令 stdout 的字符串和最终 `False` 会组成 `System.Object[]`，非空数组的布尔转换为 `True`。

独立复现（不改仓库）：

```powershell
Test-NativeExitCode 'cmd.exe' @('/c','echo probe-output & exit /b 1')
```

实际结果为 `[String] AUDIT_EXPECTED_FAILURE | [Boolean] False`，`RESULT_TYPE=System.Object[]`、`RESULT_COUNT=2`，按实际 `Invoke-CheckStep` 的转换逻辑得到 `ACTUAL_CHECKSTEP_RESULT=PASS`。可重跑附件为 [`repro-check-native-exit.ps1`](repro-check-native-exit.ps1)，其日志为 [`repro-check-native-exit.log`](../../../bin/audit-20260907/repro-check-native-exit.log)。该附件用 Parser.ParseFile 抽取当前 `check.ps1` 的三个函数定义后执行探针。

触发：任一被 `Test-NativeExitCode` 调用的 dotnet/python/generator/pytest 子命令打印至少一行 stdout 且退出码非 0。影响：门禁可能错误放行构建、测试、数据校验、生成器或 pytest 失败；本次绿灯只能证明实际命令返回了当前可见结果，不能消除该控制流缺陷。建议让 helper 只输出布尔值（将原生命令输出重定向到宿主或显式 `Out-Host`），或在 `Invoke-CheckStep` 只取结果集合的最后一个布尔项并拒绝其它输出。验收：失败探针输出一行并退出 7 时汇总为 FAIL、脚本退出 1；正常 dotnet/python 输出仍能完整显示/记录，真实退出 0 时 PASS。

### F2（P2）资产交叉校验无法验证仓库现有示例资产关系

证据：`toolchain/asset_import/check_cmd.py:42-49` 要求 `sprite_set_id` 必须为 `sprite.<category>.<name>`，并按同一 `dataset` 查找；`check_cmd.py:52-59` 要求 sprite `atlas.json`；`check_cmd.py:120-127` 对 VFX 也要求 `atlas.json`。而 `data/_sample/README.md:32-65` 明确 `_sample` 数据引用 `assets/_placeholder`，现有 `data/_sample/display/display.map.json:10-91` 使用 `sprite.placeholder_hero` 等两段 id，`assets/_placeholder` 只有 `anchors.json`，VFX 只有 `frames.json`（无 `atlas.json`）。

实跑 `python toolchain/import_assets.py check --dataset _sample`：8 条 `display.map` 全部被报 id 格式不符；3 条 VFX 全部被报 `assets/_sample/.../atlas.png` 和 `atlas.json` 缺失；`sfx.sample_hit` 两个变体被报 `assets/_sample` 文件缺失，共 16 个问题。`--dataset _placeholder` 退出 0 只是因为 `data/_placeholder` 没有表，检查了 0 行。`check.ps1` 只跑 `gen_placeholder_assets.py --check`，未跑 `import_assets.py check`（`check.ps1:409-439`），所以 CI 与指定门禁都不会发现关联断裂。

影响：资产导入工具自己的新数据集路径可能可用，但仓库交付的 `_sample` + `_placeholder` 参考集无法通过工具交叉检查；这是工具/参考样例契约断裂，不能据此断言 16 处运行时资产丢失，现有运行时占位路径仍可能可用。建议先统一契约：允许显式的 data/asset dataset 映射，或为占位包提供符合导入工具产物的 `atlas.json`/三段资源 id，再把一个明确的 sample/placeholder 交叉检查加入门禁。验收：仓库约定的参考检查命令退出 0，且每条 display/VFX/SFX 引用都有可解析的资产文件；导入工具生成的数据集仍保持现有测试全过。

### F3（P2）落地计划 HTML 副本明显落后于 Markdown

证据：`architecture/落地计划/落地方案与分阶段计划.md` 为 973 行，含 J3/K 章节及 IL2CPP/消费方演练；对应 HTML 为 1061 行，但不含 `J3`、`工程收尾 K`、`1827`、`125/125` 或“最终门禁计数”文本。Markdown 最新收尾数字位于 `落地方案与分阶段计划.md:881-935`，HTML 只保留早期阶段 0～5/初始任务拆分内容。两份文件的 H2 数也不同（Markdown 24，HTML 23）。

影响：读者从仓库入口打开 HTML 会看不到已实现的 J3/K 交付、CI、hook、IL2CPP 失败边界、消费方演练与最新测试证据，形成交付范围和验证状态的错误判断。建议由同一生成流程重建 HTML，并在 CI 加入 Markdown/HTML 版本或标题/关键章节一致性检查。验收：HTML 含 Markdown 当前的 J3/K 标题、最新门禁数字和未验证边界；Markdown 改动后副本检查失败而不是静默落后；重建后的副本与当前 Markdown 的关键标题、版本和验证边界一致。

### F4（P2）当前 main 快照缺失导致历史门禁数字容易被误读

证据：J3 历史段 `落地方案与分阶段计划.md:881-886` 声称 `dotnet test` 1827、`check.ps1` 17 步全 PASS；K 历史段 `:932-935` 又称 `check.ps1 -SkipUnity -Quick` 全 17 步 PASS、普通 `check.ps1 -SkipUnity` 全 17 步 PASS。这些段落有 2026-09-05/06 日期，历史数字本身不要求改写。当前 main 直接实测六程序集总计 1846（592+84+254+267+288+361），`-SkipUnity` 为 **11 PASS + 6 SKIP**；`-Quick` 为 **8 PASS + 9 SKIP**，共 17 行，不能称“全 PASS”。

影响：若没有当前 main 快照，历史记录容易被读成当前基线验收结论。建议保留有日期的历史快照，新增本次 main 实证段并引用日志；当前段将“17 步全 PASS”改为 PASS/SKIP 分解。验收：计划、README、CI 说明对同一命令给出同一 PASS/SKIP 和计数；历史数字与当前 main 明确分栏。

### F5（P2）`found.game_state`/`found.hook` 模块 README/schema 仍描述为未接入

证据：`core/foundation/app_lifecycle/README.md:12-14,29-31,131` 和 `schema/found.game_state.md:12-16,76-84` 仍称不依赖/不读取数据注册表、具体数据“未来加载”；`core/foundation/hook_registry/README.md:10-12,24-25,114` 与 `schema/found.hook.md:6-10,41-43` 有同样表述。当前代码已在 `core/foundation/app_lifecycle/contracts/AppStateMachineConfig.cs:161-205` 实现 `FromRegistry`，在 `core/foundation/hook_registry/schema/FoundHookSchema.cs:42-82` 实现 `LoadDefinitions`；`core/rules/assembly/RulesSchemaCatalog.cs:85-89` 注册两张 schema，`core/gameplay/assembly/GameplayAssembly.cs:442-456` 实际优先从 registry 读取，`data/_framework/found/` 有两张数据表。

影响：模块使用者会得到过时的“只可内存构造/未来再接入”指导，无法准确知道当前装配已经优先读取框架数据表。建议更新 README/schema 的范围、职责和默认退化行为；保留“DataRegistry 负责 JSON 解析，模块负责 registry→强类型转换”的边界说明。验收：模块文档同时说明 `FromRegistry`/`LoadDefinitions`、缺表回退和 `data/_framework` 默认行，且与代码测试名称一致。

### F6（P2）回放摘要对 HP/伤害 payload 不敏感

这是验收盲区，不是播放器完全失效。`core/foundation/save_system/contracts/Replay.cs:514-519` 明示摘要由事件 key 与存活实体 `(EntityId, Position, LayerDepth)` 构成；`Capture` 在 `:560-575` 只拼接这些字符串，事件 payload 和 HP/属性未进入摘要。`ReplayBaselineTests.cs:74-84,110-118,144-158` 只比较 EventLog、Digest、tick。因而两份同位置、同实体 id/layer、同事件 key，但伤害/HP payload 不同的状态可得到相同 Digest；现有测试仍能证明固定录像的 key 序列和几何实体状态重放一致，不能证明完整数值状态一致。

建议在回放契约确认后，把关键可持久化/战斗状态（至少 HP/资源/光环/库存等）纳入可稳定序列化的摘要，或另增 payload/state digest；补一个固定夹具证明 HP/伤害变化会改变摘要。验收：同位置同 key 不同 HP 的两个快照摘要不同；现有固定录像基线保持可迁移、可审阅，且事件序列比较仍保留。

## 明示未实现/预留范围（不计入隐藏缺陷）

- `editor/` 只有产品方案；`editor/README.md:5` 明确实现尚未开始，`editor/docs/编辑器产品文档.md:1-3` 状态为 v1 草案，HTML 副本关键章节与版本一致。
- `IRenderer3D`/`kind: model` 路径全部抛 `NotSupportedException`，落地计划 `:891-894` 和选型文档将其定义为未实现降级，属于可选能力未交付；不能把它当作隐藏违约。
- `world.map` 的 `regions`、`teleport_points`、`music_ref`、`allowed_difficulties` 只有结构声明，`WorldMapSchema` 未接入字段级校验；04/05 已明示代码待补。
- `DisplayMapCoverageRule` 默认不自动登记、孤儿记录检测尚无规则，04 第 5 节已明示调用方需显式登记/建议项。
- 其它跨模块表现/命令回放开放项由其它分组报告复核；本报告不把历史审计的待补列表直接当作本次结论。
- `item.affix`、`display.anim_set`、`display.equip_visual`、`world.flag_schema` 等“已登记但没有示例行”按 04 记录属于数据覆盖范围选择，不自动判成实现缺陷。

## 建议修复依赖与复验顺序

1. 先修 F1，新增 stdout+非零退出回归探针；否则后续门禁结果可能被误判。
2. 决定 F2 的 dataset/资源索引契约，修工具或参考资产，再把交叉检查纳入门禁。
3. 同一生成流程重建 F3 HTML，刷新 F4 当前 main 数字。
4. 同步 F5 两模块 README/schema 文档，避免文档继续指导错误接入方式。
5. 由回放契约负责人拍板 F6 的状态覆盖范围，再补稳定摘要和回归基线。
6. 具备独占 Unity 条件后再执行完整 `check.ps1`；本次审计只探测到 Unity 版本，Unity 编译、EditMode、PlayMode、独立版/消费方/IL2CPP 结果均应标为待补验证。

## 证据边界

本报告没有修改生产代码、原架构规范或既有数据；本组只新增审计报告与失败探针附件，门禁生成物留在被 `.gitignore` 忽略的 `bin/audit-20260907/`。`check.ps1 -SkipUnity` 的 PASS 不能证明 Unity 或发布形态链路；本报告只复核上列范围，其它历史开放项由相应分组报告处理；自动存档 ShouldAutoSave 等历史项未作为本报告结论。
