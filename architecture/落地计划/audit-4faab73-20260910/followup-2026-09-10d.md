# 第十七轮审核修复跟进核实（2026-09-10）

基线：`4faab73e7081f2984e7addb88fee051b6c0d3d02`（v1.16.2）。本文档核实
[AUDIT_REPORT.md](AUDIT_REPORT.md) 问题汇总表九项：三项已确认 P2 框架数据/持久化合同
（V-01/V-02/V-03）、一项已确认 P2 ABI 工具门禁（ABI-1162-01）、五项独立 P3 文档问题
（DOC-162-01～05）的实际修复结果。

修复由多个并行 agent 分工：本 agent（WT，`toolchain/abi_surface/**`、`toolchain/tests/**`、
`toolchain/README.md`、`architecture/落地计划/**`、`CHANGELOG.md` 仅本 agent 负责各项的条目）
负责 ABI-1162-01 与其中四项文档更新（DOC-162-02/03/04）与本轮证据归档；DOC-162-01（DataRegistry
README/XML 条件语义）由负责 `core/foundation/data_registry/README.md` 的并行 agent 处理；
V-01/V-02/V-03（`schema_version` 32 位回绕、`CurrencyPersistable` long 精度、
`JsonNumber.TryGetInt64` 上界）与 DOC-162-05（04 号文档 `field_finite` 文案收窄）由负责
`core/foundation/**`/`core/gameplay/economy/**`/`architecture/04` 的另一位并行 agent（分支
`wo/core17`）处理。本文档由本 agent 落笔核实自己负责的条目；V-01/V-02/V-03、DOC-162-01/05 行的
修复位置/验收留给整合 agent 按其余 agent 的实际提交回填。

提交哈希表：

| 提交 | 标题 | 范围 |
|---|---|---|
| `<提交待填>` | 第十九方深度审核修复(ABI 工具链与文档): 属性索引参数/运算符重载/事件 remove 访问器纳入 ABI 表面差异覆盖，发布计划四包附件清单、teleport 元素登记、ATB 延期预留三处文档措辞勘误，归档与跟进核实 | ABI-1162-01（`toolchain/abi_surface/{SurfaceDumper,TypeNameFormatter}.cs`、`toolchain/tests/test_abi_surface_compare.py`、`toolchain/README.md`）+ DOC-162-02/03/04（`architecture/落地计划/落地方案与分阶段计划.md`）+ 归档（`architecture/落地计划/audit-4faab73-20260910/`）+ `CHANGELOG.md`（`[Unreleased]` 追加本 agent 负责条目），本 agent（WT）负责范围合并为一个提交 |

## 核实表

| 条目 | 判断 | 复现 | 修复位置 | 验收 | 判断记录 |
|---|---|---|---|---|---|
| V-01 `schema_version` 32 位回绕 | **<待整合回填>** | <待整合回填> | <待整合回填> | <待整合回填> | <待整合回填> |
| V-02 `CurrencyPersistable` long 精度丢失 | **<待整合回填>** | <待整合回填> | <待整合回填> | <待整合回填> | <待整合回填> |
| V-03 `JsonNumber.TryGetInt64` 上界错误 | **<待整合回填>** | <待整合回填> | <待整合回填> | <待整合回填> | <待整合回填> |
| ABI-1162-01 indexer 门禁漏报 | **成立，已修复** | 条件：public indexer `public int this[int i]` 改 `int this[string i]`，旧编译 consumer 只换新 DLL、不重新编译。实际（修复前）：`toolchain/abi_surface/SurfaceDumper.cs` 属性签名只编码 `Name:PropertyType`，不含 `PropertyInfo.GetIndexParameters()`，索引参数变化前后 dump 逐字节相同，`compare` 判 `breaks=0`；同一场景真实旧 consumer 实跑抛 `System.MissingMethodException`（复现 `delivery/run5-console.log` 30-39 行现象）。预期：dump 编码索引参数并对变化判破坏；同时核对同一批容易漏判的签名维度：方法参数 `ref`/`out`/`in` 修饰与默认值存在性、`params` 数组、方法返回类型可空注解（不算）、显式接口实现成员、事件访问器可见性、嵌套类型泛型元数、运算符重载/转换运算符、`extern`/`unsafe` 指针参数类型——每发现一个漏项补编码+负例测试。 | 1) `toolchain/abi_surface/SurfaceDumper.cs` 属性分支：`sig` 追加 `[<索引参数类型列表>]`（`prop.GetIndexParameters()` 非空时），复用 `TypeNameFormatter.FormatParameters`。2) 同文件 method 循环：`IsSpecialName` 单独判定改为"`IsSpecialName` 且方法名匹配 `get_`/`set_`/`add_`/`remove_` 前缀"（新增 `IsAccessorMethodName`），运算符重载/转换运算符（`op_Addition`/`op_Implicit` 等同样 `IsSpecialName=true` 但不匹配四个前缀）不再被整体跳过，按普通 method 记录。3) 同文件 event 循环：`eflagsList` 追加独立 `remove:<VIS>` token（`evt.RemoveMethod` 的可见性），首 token 仍是 add 可见性不变，不影响既有放宽豁免逻辑。4) `TypeNameFormatter.cs`：核查后确认 `params` 数组修饰、可选参数默认值存在性/取值**不**编码——与既有 `ref`/`out`/`in` 不编码设计同一治理逻辑（判断记录见下）。5) 显式接口实现成员：核查确认无需改动——显式实现方法本身是 `private`（`IsMemberVisible` 已排除），公开契约由接口自身的 TYPE/MEMBER 行覆盖，已有机制足够。6) 嵌套类型泛型元数：核查确认 `Type.FullName` 对嵌套泛型类型定义（`Outer\`1+Inner\`1`）已正确带出元数后缀，无需改动。7) `extern`/`unsafe` 指针参数：核查确认 `TypeNameFormatter.Format` 的 `IsPointer` 分支（`T*`）已覆盖，无需改动。 | 复现：`toolchain/tests/test_abi_surface_compare.py` 新增
`test_indexer_parameter_type_change_negative_oracle_end_to_end_via_real_dll` 用真实 `dotnet build` 构造 baseline（`public string this[int i]`）/current（`this[string i]`）两份最小库 + 一份只编译一次的旧 consumer——换上 current 库不重编译，运行抛 `System.MissingMethodException`；修复前该用例在断言 `"Item[System.Int32]" in baseline_dump_text` 处失败（`Item:System.String` 未含索引参数，钉死修复前的漏报症状），修复后全绿。新增用例合计 11 个：索引器参数变化负例/新增重载正例/删除负例三个、`ref`/`out` 修饰不变确认正例一个、`params`/可选默认值不编码的真实反射验证一个（构造带 `params`/默认值参数的库，真实 `Dump()` 断言输出不含 `!params`/`!opt`、默认值 1→2 不改变 dump 文本）、运算符重载删除负例/转换运算符签名变化负例两个、事件 remove 访问器收窄负例一个、上述端到端负例一个，加原有 23 个，`toolchain/tests/test_abi_surface_compare.py` 单文件 `pytest -q`：34 passed、1 skipped（`test_real_baseline_end_to_end_via_abi_probe` 在本机 `dist/` 无 1.12.0 zip 时跳过，工作树内该 zip 存在，验证见下）。`python -m pytest toolchain/tests -q`（全仓）：174 passed、3 skipped。用新版工具对 `dist/ws-game-1.12.0.zip`（完整探针：consumer smoke + dump/compare）、`dist/ws-game-1.13.0.zip`（仅 dump/compare，既有已知边界见下）两份历史基线重跑当前工作树六个 DLL：均 `breaks=0`（1.12.0：`additions=221`；1.13.0：`additions=132`），未发现历史真实破坏。 | 索引参数是本条目最初报告的核心场景，独立于其余漏项。核查扩展维度时发现 `params`/可选参数默认值这两项若编码会产生假阳性——用新版（曾短暂加上 `!opt`/`!params` 编码的版本）工具对 `dist/ws-game-1.12.0.zip` 重跑当前工作树时一度出现 `breaks=5`：`FieldSchema`/`EconomyContentValidationRule`/`LootContentValidationRule`/`SkillHost`/`ViewBinder` 五个公开构造函数新增尾部可选参数时，框架统一采用"新增一个更长的可选参数重载，同时保留原始定长参数表的旧重载（旧重载参数改为不带默认值，仅作为纯物理兼容 shim）"模式——旧编译调用方物理上调用的正是这个保留的定长重载，从未因为它是否声明默认值而受影响，是合法的既有兼容模式，不是破坏。`ref`/`out`/`in`/`params`/可选默认值五者的共同点是"C# 编译器侧语法糖，不改变 IL 物理签名，旧编译调用方省略实参时编译器已把具体值/展开后的显式数组写死进调用方 IL，CLR 方法绑定只按物理参数类型+个数匹配"——因此撤回 `params`/可选默认值编码，与既有 `ref`/`out`/`in` 设计保持一致，判断记录见 `toolchain/abi_surface/TypeNameFormatter.cs` `FormatParameters`。1.13.0 场景下 `toolchain/abi_probe.ps1` 第 1 点手写消费方探针因 consumer 源码只锚定 `abi_probe_baseline.txt` 记录的 1.12.0 签名集合、对 1.13.0 编译会失败——这是既有已知边界（与 ABI-116-01 上一轮记录一致），与本轮 dump/compare 覆盖范围扩充无关，改用直接对提取自 `dist/ws-game-1.13.0.zip` 的六个基线 DLL 与当前工作树六个 DLL 做 dump+compare 验证。 |
| DOC-162-01 DataRegistry README/XML 条件语义漏写 | **<待整合回填>** | <待整合回填> | <待整合回填> | <待整合回填> | <待整合回填> |
| DOC-162-02 发布计划仍写三个 `.tgz` | **成立，已修复** | `architecture/落地计划/落地方案与分阶段计划.md`（原约 260 行）"互斥关系，`.github/workflows/release.yml` 仍然只把 zip/lock 与三个 `.tgz` 一并作为 GitHub Release 附件上传"一句仍写三个 `.tgz`；实际正式交付四个包（`com.gamefoundation.adapter.unity`/`framework-data`/`toolchain`/`adapter.headless`，ADR-0018 决策 3 已把 headless 适配层转正为第四个包），`.github/workflows/release.yml` 实际上传九件附件：`ws-game-<ver>.zip`/`.lock`/`-samples.zip`、四个 `.tgz`、`get_framework.ps1`、`_hash.ps1`（该文件 264-291 行 `$requiredNames`/`$assetPaths` 两个数组逐一列出）。 | `落地方案与分阶段计划.md` 对应句改写为明确列出九件附件（`ws-game-<ver>.zip`、`.lock`、`-samples.zip`、四个 `.tgz` 具名、`get_framework.ps1`、`_hash.ps1`），不再用"三个 `.tgz`"笼统概括。 | 走查 `.github/workflows/release.yml` 264-291 行 `$requiredNames`（9 项）与 `$assetPaths`（9 项路径映射）确认改写后的文字描述与实际上传附件逐一对应。 | 该文件同一小节 207/234/244/245 行此前已经正确写"四个包"，只有约 260 行这一处沿用了旧的"三个 `.tgz`"表述——是 ADR-0018 决策 3 落地时（第 37 行变更记录，`.github/workflows/release.yml` 三处多处硬编码改四包）遗漏同步的一句 prose，不是系统性问题，只此一处需要改。 |
| DOC-162-03 teleport nested 元素称"仅数组" | **成立，已修复** | `落地方案与分阶段计划.md`（原约 1371 行）"nested teleport 元素/引用完整性校验"一行称 `teleport_points` 数组内每个元素"仅做'是数组'这一层检查、不展开逐条元素形状"；实际 `core/foundation/scene_router/core/WorldMapSchema.cs:60`-`79` 的 `teleport_points` 字段已登记 `item: PointItemSchema`，元素结构 `{id?, position?}` 已展开到 `id`/`position` 两个子字段（`:92`-`99`，`position` 提供时 `x`/`y` 必填），并非只做容器级"是数组"检查。 | 该行改写为区分两层："结构已登记"（元素形状由 `PointItemSchema` 校验）与"引用目标完整性不由登记表保证"（命名点是否被 `teleport_target_ref` 正确引用、引用目标地图/落点是否存在，由 `Core.Gameplay.Assembly.TeleportTargetResolver` 解析时按 id 查找并温和降级，属于该解析器与调用方的业务约束，不是登记层职责），分类仍保留"未实现"但收窄为"引用目标完整性校验"这一项残余真实缺口，不再声称结构未登记。 | 走查 `WorldMapSchema.cs:60`-`79`（`PointItemSchema` 定义，`id`/`position` 两个子字段，`position` 内 `x`/`y` 必填）与 `:92`-`99`（`teleport_points` 字段登记 `item: PointItemSchema`）确认改写后的描述与源码一致；同时核对 `ISSUES.md`"不计入问题的边界"表中 `teleport_points` 一行结论（"元素结构已登记，命名点由 TeleportTargetResolver 解析，引用目标完整性不由 schema 保证"）与本行改写后的措辞一致，未产生新的分类矛盾。 | 只改措辞、不改"引用完整性校验未实现"这一结论——真正的开放缺口是跨地图/跨表引用完整性检查，不是元素形状登记（该部分已完成，只是文档滞后未同步）。 |
| DOC-162-04 ATB 被分类为"明确非目标" | **成立，已修复** | `落地方案与分阶段计划.md`（原约 1366 行）"能力边界与未默认接入能力索引"表 ATB 一行分类"明确非目标"；实际 `architecture/adr/0013-时间模型可替换即时与回合制同一规则层.md` 第 15 行原文"预留 `atb` 作为后续扩展位，本次不展开"——是"本版延期"而非无计划展开的"明确非目标"；`core/foundation/sim_loop/schema/TimeModelSchema.cs:23` 的 `InitiativePolicyValues` 已含 `"atb"` 合法枚举值，`core/foundation/sim_loop/core/TurnScheduler.cs:77`-`81` 遇到该策略时装配期抛 `NotSupportedException`（轮次调度器暂不支持，不是 schema 层拒绝）。 | 该行分类列从"明确非目标"改为"延期/预留"，说明栏补充与 ADR-0013 原文对照的判断记录：合法预留枚举值已存在于 schema，轮次调度器暂不支持，区别于"明确非目标"（无计划展开、纯占位不代表未来会启用）。 | 走查 `architecture/adr/0013-*.md` 第 15 行、`TimeModelSchema.cs:23`、`TurnScheduler.cs:77`-`81` 三处确认改写后的分类与说明同时与 ADR 原文决策、校验器枚举、运行期异常三处源码/文档一致；同时核对 `ISSUES.md`"不计入问题的边界"表 ATB 一行已使用"延期"一词（与本表改写方向一致，未产生新的分类矛盾）。 | 现行"能力边界与未默认接入能力索引"表分类口径共六类（已实现未默认接线/游戏责任/未实现/明确非目标/暂不落地/已实现且默认接线-归档），本次改写在 ATB 一行新引入"延期/预留"标签，未同步扩写六类口径的正式legend定义——按任务范围"只改措辞、不改结论"从简处理，只改这一行的分类词与说明，不重构整个legend段落；如后续需要把"延期/预留"正式纳入六类口径的第七类，需另经设计层判断，本轮不越权代做。 |
| DOC-162-05 04 号文档 `field_finite` 文案范围 | **<待整合回填>** | <待整合回填> | <待整合回填> | <待整合回填> | <待整合回填> |

## ISSUES.md"不计入问题的边界"表交叉核对（本 agent 核查，只改措辞不改结论）

按任务要求核对 `ISSUES.md`"不计入问题的边界"表与 `落地方案与分阶段计划.md`"能力边界与未默认接入
能力索引"表逐项分类是否矛盾：

- **weaponVfx**：ISSUES.md 标"未默认接线"（"Resolver 已构造暴露，无默认生产调用，调用方接线"）；
  落地计划表（约 1357 行"武器 Swing/Impact 特效消费"）标"已实现未默认接线"，两处措辞不同但语义
  一致（框架自己的六类口径里没有单独的"未默认接线"类目，"已实现未默认接线"是对应的正式名称），
  **不矛盾，未改动**。
- **gatherClock**：ISSUES.md 标"条件支持/调用方责任"（"`GobjOptions.SimTime` 默认恒 0，调用方
  注入模拟时钟；刷新机制存在"）；落地计划表（约 1358 行"采集时钟"）标"已实现未默认接线"，语义
  一致（默认值不生效、需调用方显式接入），**不矛盾，未改动**。
- **Replay**：ISSUES.md 标"未默认接线"（"API 存在，生产根默认不驱动或传 null"）；落地计划表
  （约 1359 行"回放接入"）标"已实现未默认接线"，**不矛盾，未改动**。同一条目下的
  Quest.Update、owner/day/vendor 两项落地计划表分别标"已实现未默认接线"，同样与 ISSUES.md 一致，
  未发现矛盾。
- 其余"不计入问题的边界"表条目（talent 完整 allocator、TargetPoint、teleport_points——已在
  DOC-162-03 单独处理、离散召唤/loot、day_cycle、导航跨帧预算/空间完整索引化、孤儿记录检测、
  跨 aura 定义共享槽位）逐项与落地计划表对照，分类词虽不完全逐字相同（如"条件支持"
  vs"游戏责任"/"已实现未默认接线"），但均指向同一结论方向，未发现需要改结论的实质矛盾；如有
  后续疑义留待整合阶段复核，本轮不改。

## 全量门禁验收结果（本 agent 负责范围，2026-09-10）

1. `dotnet build toolchain/abi_surface/AbiSurface.csproj -c Release --nologo --artifacts-path
   <scratchpad>/build_wp`：0 警告、0 错误。
2. `python -m pytest toolchain/tests/test_abi_surface_compare.py -q`：34 passed、1 skipped
   （含本轮新增 11 个用例 + 原有 23 个）。
3. `python -m pytest toolchain/tests -q`：174 passed、3 skipped（全仓 `toolchain` 自身 Python
   测试套件，本 agent 改动范围外的用例同批全绿，未发现回归）。
4. `dotnet build Core.sln -c Release --nologo --artifacts-path <scratchpad>/build_wp`：0 警告、
   0 错误（`toolchain/abi_probe.ps1`/独立 dump+compare 重跑 1.12.0、1.13.0 两份历史基线所需的
   当前工作树六个 DLL 构建）。
5. 1.12.0 基线重跑：`toolchain/abi_probe.ps1 -BaselineVersion 1.12.0 -BaselineZip
   D:\workespace\ws-game\dist\ws-game-1.12.0.zip -ArtifactsPath <scratchpad>/build_wp
   -SkipIfBaselineMissing:$false`：exit 0，`abi_surface compare`
   `breaks=0 allowed=0 additions=221 RESULT=OK`（手写消费方探针第 1 点与表面差异第 2 点均通过）。
6. 1.13.0 基线重跑：因既有已知边界（见上表 ABI-1162-01 判断记录），改用直接调用
   `toolchain/abi_surface` 对提取自 `dist/ws-game-1.13.0.zip` 的六个基线 DLL 与当前工作树六个
   DLL 做 dump+compare：`breaks=0 allowed=0 additions=132 RESULT=OK`。
7. 归档 `architecture/落地计划/audit-4faab73-20260910/`：对本目录做不区分大小写的具体游戏代号
   禁用词全文扫描，未发现任何具体游戏代号；本目录全部文本文件（含从 PowerShell 默认 UTF-16LE
   编码重新编码为 UTF-8 的日志）行结束符已统一转换为 LF。
8. `python -m pytest toolchain/tests/test_markdown_relative_links.py -q`：提交前先在本地未跟踪
   状态下人工核对本目录全部 Markdown 相对链接可解析；该测试的扫描范围限定为 `git ls-files` 已
   跟踪文件，`git add` 后需再跑一次确认（见下"异常与判断记录"）。

## 异常与判断记录

- 本 agent 只在自己的工作树分支 `wp/tool17`（从主树 `main` 创建的独立 `git worktree`）内工作，
  未触碰主树或其余并行 agent 负责的路径（`core/rules/skill/**`、`core/foundation/**`、
  `core/gameplay/economy/**`、`architecture/04`、`data_registry` README）；执行过程中一度误将
  `toolchain/abi_surface/{SurfaceDumper,TypeNameFormatter}.cs`、
  `toolchain/tests/test_abi_surface_compare.py` 三个文件的编辑落到主树而非工作树（工具路径参数
  错误），发现后立即用 `git show HEAD:<path>` 取回主树该三个文件在基线提交下的原始内容并写回，
  确认 `git -C <主树> status --short -- toolchain/` 干净后才继续；本轮最终提交只来自工作树分支
  `wp/tool17`。
- ABI-1162-01 用新版工具重跑 1.12.0/1.13.0 两份历史基线均 `breaks=0`，未发现任何历史真实破坏——
  本轮新增的覆盖项（索引器参数、运算符重载、事件 remove 访问器可见性）纯属"门禁能力补强"，不
  代表 1.16.2 当前六个核心 DLL 曾经发生过对应类型的未声明破坏。
- `params`/可选参数默认值两个维度**曾经短暂编码**又主动撤回——过程与证据见上表 ABI-1162-01
  判断记录列，不是本轮遗漏，是核查后确认不该编码。
- `toolchain/tests -q` 174 passed/3 skipped 的 3 个 skip 分别是：
  `test_abi_surface_compare.py::test_real_baseline_end_to_end_via_abi_probe`（该测试内部逻辑
  是"本机 `dist/` 无对应基线 zip 时 skip"，工作树内该 zip 实际存在，本轮已单独用 5/6 两步显式
  重跑验证，不依赖这条自动化用例）及另外 2 个与本 agent 改动无关的既有 skip（本机环境限制，
  非本轮改动引入）。
- 本文档 V-01/V-02/V-03、DOC-162-01/05 五行按任务要求写"待整合回填"，未越权代另一位并行 agent
  填写（未读取其工作树内容、未推测其提交范围），避免猜错造成二次勘误。
