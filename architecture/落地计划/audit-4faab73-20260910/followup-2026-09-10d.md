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
| `f602c8c41981e05b399195ce44c93d223b83fcca` | 第十九方深度审核修复(ABI 工具链与文档): 属性索引参数/运算符重载/事件 remove 访问器纳入 ABI 表面差异覆盖，发布计划四包附件清单、teleport 元素登记、ATB 延期预留三处文档措辞勘误，归档与跟进核实 | ABI-1162-01（`toolchain/abi_surface/{SurfaceDumper,TypeNameFormatter}.cs`、`toolchain/tests/test_abi_surface_compare.py`、`toolchain/README.md`）+ DOC-162-02/03/04（`architecture/落地计划/落地方案与分阶段计划.md`）+ 归档（`architecture/落地计划/audit-4faab73-20260910/`）+ `CHANGELOG.md`（`[Unreleased]` 追加本 agent 负责条目），本 agent（WT）负责范围合并为一个提交 |
| `f6f8c159f42d7dd5c6339ea73744e28ae3f2e963` | 修复 V-01/V-02/V-03：schema_version 溢出、货币存档精度、JsonNumber.TryGetInt64 上界 | V-01/V-02/V-03（`core/foundation/data_registry/core/DataRegistry.cs`、`core/foundation/common/json/JsonValue.cs`、`core/gameplay/economy/core/CurrencyPersistable.cs` 等 7 处持久化点、`core/gameplay/common/contracts/ExprValueJson.cs`）+ 对应六工程测试新增，分支 `wo/core17`（整合阶段由整合 agent 核实并回填本文档） |
| `44f320d66eb598629823d012e4dfc333f18ccd38` | 勘误 04/data_registry README：数值有限适用范围、多根覆盖条件语义（DOC-162-05/01） | DOC-162-01（`core/foundation/data_registry/README.md`）+ DOC-162-05（`architecture/04_数据与内容管线.md`），分支 `wo/core17` |
| `d7c1c6c0f1bdaa283df12e41d0cf6a67d948d91b` | 合并 wo/core17：修复 V-01/V-02/V-03（schema_version 溢出、货币存档精度、TryGetInt64 上界） | 整合 agent 在主树上执行的 `git merge --no-ff wo/core17`，唯一冲突点 `architecture/04_数据与内容管线.md`"变更记录"表（两条独立行并存，无需取舍） |

## 核实表

| 条目 | 判断 | 复现 | 修复位置 | 验收 | 判断记录 |
|---|---|---|---|---|---|
| V-01 `schema_version` 32 位回绕 | **成立，已修复**（整合 agent 独立复现并核实） | 修复前后各写一个最小复现（独立 `dotnet run` 控制台程序，引用当前分支的 `Core.Foundation`/`Adapters.Stub` 工程，构造 `schema_version: 4294967297` 的表信封喂给真实 `DataRegistry.LoadAll`，不依赖仓库内既有单测断言）：对基线 `4faab73`（修复前）实测输出 `errors=0, blocking=False`，`GetAll` 成功返回 1 行——即审核报告所述"强转溢出回绕成 1、被当作低版本放行"确认属实；对当前合并结果（修复后）实测输出 `errors=1, blocking=True`，报出的 `issue.Message` 原文为`"表 \"test.version_boundary\" 的顶层字段 \"schema_version\" 取值 \"4294967297\" 非法（须为 [1, 2147483647] 范围内的整数）"`，`GetAll` 抛 `InvalidOperationException("数据校验未通过，禁止读取")`——修复属实生效。 | `core/foundation/data_registry/core/DataRegistry.cs`（`LoadOneTablePartial` 内 envelope 解析 `schema_version` 处，强转前先核对 `[1, int.MaxValue]` 闭区间，越界/非整数/负数统一走 `schema_version` 检查名报阻断错误，消息含实际取值；`LoadAll(sources)` 方法级 XML 注释同步补全 override/final 条件语义，即 DOC-162-01 的代码侧配套）。提交 `f6f8c15`。 | 复现脚本源码见本轮临时验证产物（未入库，整合阶段用 `dotnet run` 直接验证，不落盘到仓库）；仓库内固化回归见 `core/foundation/data_registry/tests/DataRegistryTests.cs` 的 `LoadAll_SchemaVersionOverflowsInt32_NoWrapAroundToOne_BlocksWithActualValueInMessage`（用例名与断言与整合 agent 独立复现结果一致）与同文件 `LoadAll_SchemaVersionOutOfRangeOrNonInteger_Blocks`（`2147483648`/`0`/`-1`/`1.5` 四个边界 `[Theory]` 用例）；三次合并后六工程 `dotnet test` 全绿（见"测试数量变化"）。 | 独立复现与仓库内固化测试结论一致，未发现审核报告或 wo/core17 提交说明有夸大或遗漏；`(int)` 强转前置范围检查是本条唯一必要的根治手段，未见更优替代方案，无需进一步整改。 |
| V-02 `CurrencyPersistable` long 精度丢失 | **成立，已修复**（整合 agent 独立复现并核实） | 独立 `dotnet run` 复现：对基线 `4faab73`（修复前，尚无 `JsonNumber.FromInt64` 工厂）用等价的 `new JsonNumber(9007199254740993d)`（当时 `CurrencyPersistable.Save` 等持久化点的实际写法就是把 `long` 隐式转 `double` 再构造 `JsonNumber`）往返，实测 `TryGetInt64` 输出 `value=9007199254740992`（确认精度丢失，与审核报告描述完全一致）；对当前合并结果（修复后）改用 `JsonNumber.FromInt64(9007199254740993L)`，实测 `RawNumberText="9007199254740993"`、`TryGetInt64` 往返输出 `value=9007199254740993`（精确无损）。 | `core/foundation/common/json/JsonValue.cs`（新增 `JsonNumber.FromInt64(long)` 精确整数工厂，`RawNumberText` 用不变文化十进制原文）；持久化调用点全仓扫描后改用该工厂，实际核对存在于以下 7 处（`git grep -l "JsonNumber.FromInt64"` 核实，均非测试文件）：`core/gameplay/economy/core/CurrencyPersistable.cs`（货币余额）、`core/numbers/progression/core/ProgressionHost.cs`（经验值 Xp）、`core/foundation/save_system/contracts/Replay.cs`（Tick/TickCount）、`core/foundation/save_system/core/SaveSystem.cs`（play_time_seconds）、`core/gameplay/world_state/core/WorldState.cs`（World State Expr Int 编码）、`core/gameplay/common/contracts/ExprValueJson.cs`（`ToJson` 的 Int 分支改用 `FromInt64`，`Parse` 侧配套改用 `TryGetInt64` 而非直接 `(long)n.Value` 截断，见判断记录）、`core/gameplay/quest/core/QuestPersistable.cs`（objective_counts/completion_count/last_completed_day）。提交 `f6f8c15`。 | `core/foundation/common/tests/JsonNumberTests.cs`（新增，含 `9007199254740993L` 精确往返用例）；`core/gameplay/economy/tests/EconomyHostTests.cs`（`[InlineData(9007199254740993L)]`，"审核报告 V-02 复现原文的确切输入"）；`CurrencyPersistable`/`ProgressionPersistable`/`Replay`/`SaveSystem`/`WorldState`/`ExprValueJson` 各测试文件均补一条精确往返用例（对照 wo/core17 提交说明逐项核实存在，未发现遗漏或夸大）。 | 独立复现确认修复前后行为差异属实；`ExprValueJson.Parse` 侧的配套修复（避免"写出精确、读回截断"的名存实亡问题）经代码走查确认真实存在（`ExprValueJson.cs` 第 25-30、97-127 行一带注释与实现均指向该判断记录），不是提交说明里的自我表述夸大。 |
| V-03 `JsonNumber.TryGetInt64` 上界错误 | **成立，已修复**（整合 agent 独立复现并核实） | 独立 `dotnet run` 复现：对基线 `4faab73`（修复前）用 `new JsonNumber(Math.Pow(2, 63))` 构造无 `RawNumberText` 的数字，实测 `TryGetInt64` 输出 `ok=True, value=-9223372036854775808`——即 `2^63` 本不在 `long` 范围内却被误判为可转换，`(long)Value` 溢出回绕成 `long.MinValue`，与审核报告描述完全一致；对当前合并结果（修复后）同一输入实测 `ok=False, value=0`，正确判定为不可转换。另按任务要求补测 `long.MinValue` 边界（`new JsonNumber((double)long.MinValue)`）：修复前后均输出 `ok=True, value=-9223372036854775808`——`long.MinValue`（即 `-2^63`）可被 `double` 精确表示，属于半开区间 `[-2^63, 2^63)` 的合法闭下界，修复未改变、也不应改变这一边界的行为，两次实测一致确认边界处理正确（不是遗漏）。 | `core/foundation/common/json/JsonValue.cs`（`JsonNumber.TryGetInt64` 无 `RawNumberText` 分支，改用显式双精度字面量 `[-9223372036854775808.0, 9223372036854775808.0)` 半开区间判断，替换原先的 `Value <= long.MaxValue` 隐式转换写法）。提交 `f6f8c15`。 | `core/foundation/common/tests/JsonNumberTests.cs` 新增边界用例（覆盖 `2^63`、`long.MinValue`、`long.MaxValue` 等临界值，与整合 agent 独立复现的两个输入一致）；`core/foundation/data_registry/tests/DataRegistryTests.cs` 新增"经 TableMigration 注入 Int 字段的 2^63 现在正确命中 field_type 阻断"一类用例。 | 独立复现的两个边界（`2^63` 误判、`long.MinValue` 正确处理）与提交说明描述完全吻合；"没有 `RawNumberText` 时才走这条判断路径、有原始文本时按文本精确解析不受影响"的分支逻辑经代码走查确认（`TryGetInt64` 方法体内 `if (RawNumberText != null)` 分支与本条判断路径互斥），未发现需要进一步修正之处。 |
| ABI-1162-01 indexer 门禁漏报 | **成立，已修复** | 条件：public indexer `public int this[int i]` 改 `int this[string i]`，旧编译 consumer 只换新 DLL、不重新编译。实际（修复前）：`toolchain/abi_surface/SurfaceDumper.cs` 属性签名只编码 `Name:PropertyType`，不含 `PropertyInfo.GetIndexParameters()`，索引参数变化前后 dump 逐字节相同，`compare` 判 `breaks=0`；同一场景真实旧 consumer 实跑抛 `System.MissingMethodException`（复现 `delivery/run5-console.log` 30-39 行现象）。预期：dump 编码索引参数并对变化判破坏；同时核对同一批容易漏判的签名维度：方法参数 `ref`/`out`/`in` 修饰与默认值存在性、`params` 数组、方法返回类型可空注解（不算）、显式接口实现成员、事件访问器可见性、嵌套类型泛型元数、运算符重载/转换运算符、`extern`/`unsafe` 指针参数类型——每发现一个漏项补编码+负例测试。 | 1) `toolchain/abi_surface/SurfaceDumper.cs` 属性分支：`sig` 追加 `[<索引参数类型列表>]`（`prop.GetIndexParameters()` 非空时），复用 `TypeNameFormatter.FormatParameters`。2) 同文件 method 循环：`IsSpecialName` 单独判定改为"`IsSpecialName` 且方法名匹配 `get_`/`set_`/`add_`/`remove_` 前缀"（新增 `IsAccessorMethodName`），运算符重载/转换运算符（`op_Addition`/`op_Implicit` 等同样 `IsSpecialName=true` 但不匹配四个前缀）不再被整体跳过，按普通 method 记录。3) 同文件 event 循环：`eflagsList` 追加独立 `remove:<VIS>` token（`evt.RemoveMethod` 的可见性），首 token 仍是 add 可见性不变，不影响既有放宽豁免逻辑。4) `TypeNameFormatter.cs`：核查后确认 `params` 数组修饰、可选参数默认值存在性/取值**不**编码——与既有 `ref`/`out`/`in` 不编码设计同一治理逻辑（判断记录见下）。5) 显式接口实现成员：核查确认无需改动——显式实现方法本身是 `private`（`IsMemberVisible` 已排除），公开契约由接口自身的 TYPE/MEMBER 行覆盖，已有机制足够。6) 嵌套类型泛型元数：核查确认 `Type.FullName` 对嵌套泛型类型定义（`Outer\`1+Inner\`1`）已正确带出元数后缀，无需改动。7) `extern`/`unsafe` 指针参数：核查确认 `TypeNameFormatter.Format` 的 `IsPointer` 分支（`T*`）已覆盖，无需改动。 | 复现：`toolchain/tests/test_abi_surface_compare.py` 新增
`test_indexer_parameter_type_change_negative_oracle_end_to_end_via_real_dll` 用真实 `dotnet build` 构造 baseline（`public string this[int i]`）/current（`this[string i]`）两份最小库 + 一份只编译一次的旧 consumer——换上 current 库不重编译，运行抛 `System.MissingMethodException`；修复前该用例在断言 `"Item[System.Int32]" in baseline_dump_text` 处失败（`Item:System.String` 未含索引参数，钉死修复前的漏报症状），修复后全绿。新增用例合计 11 个：索引器参数变化负例/新增重载正例/删除负例三个、`ref`/`out` 修饰不变确认正例一个、`params`/可选默认值不编码的真实反射验证一个（构造带 `params`/默认值参数的库，真实 `Dump()` 断言输出不含 `!params`/`!opt`、默认值 1→2 不改变 dump 文本）、运算符重载删除负例/转换运算符签名变化负例两个、事件 remove 访问器收窄负例一个、上述端到端负例一个，加原有 23 个，`toolchain/tests/test_abi_surface_compare.py` 单文件 `pytest -q`：34 passed、1 skipped（`test_real_baseline_end_to_end_via_abi_probe` 在本机 `dist/` 无 1.12.0 zip 时跳过，工作树内该 zip 存在，验证见下）。`python -m pytest toolchain/tests -q`（全仓）：174 passed、3 skipped。用新版工具对 `dist/ws-game-1.12.0.zip`（完整探针：consumer smoke + dump/compare）、`dist/ws-game-1.13.0.zip`（仅 dump/compare，既有已知边界见下）两份历史基线重跑当前工作树六个 DLL：均 `breaks=0`（1.12.0：`additions=221`；1.13.0：`additions=132`），未发现历史真实破坏。 | 索引参数是本条目最初报告的核心场景，独立于其余漏项。核查扩展维度时发现 `params`/可选参数默认值这两项若编码会产生假阳性——用新版（曾短暂加上 `!opt`/`!params` 编码的版本）工具对 `dist/ws-game-1.12.0.zip` 重跑当前工作树时一度出现 `breaks=5`：`FieldSchema`/`EconomyContentValidationRule`/`LootContentValidationRule`/`SkillHost`/`ViewBinder` 五个公开构造函数新增尾部可选参数时，框架统一采用"新增一个更长的可选参数重载，同时保留原始定长参数表的旧重载（旧重载参数改为不带默认值，仅作为纯物理兼容 shim）"模式——旧编译调用方物理上调用的正是这个保留的定长重载，从未因为它是否声明默认值而受影响，是合法的既有兼容模式，不是破坏。`ref`/`out`/`in`/`params`/可选默认值五者的共同点是"C# 编译器侧语法糖，不改变 IL 物理签名，旧编译调用方省略实参时编译器已把具体值/展开后的显式数组写死进调用方 IL，CLR 方法绑定只按物理参数类型+个数匹配"——因此撤回 `params`/可选默认值编码，与既有 `ref`/`out`/`in` 设计保持一致，判断记录见 `toolchain/abi_surface/TypeNameFormatter.cs` `FormatParameters`。1.13.0 场景下 `toolchain/abi_probe.ps1` 第 1 点手写消费方探针因 consumer 源码只锚定 `abi_probe_baseline.txt` 记录的 1.12.0 签名集合、对 1.13.0 编译会失败——这是既有已知边界（与 ABI-116-01 上一轮记录一致），与本轮 dump/compare 覆盖范围扩充无关，改用直接对提取自 `dist/ws-game-1.13.0.zip` 的六个基线 DLL 与当前工作树六个 DLL 做 dump+compare 验证。 |
| DOC-162-01 DataRegistry README/XML 条件语义漏写 | **成立，已修复**（整合 agent 核实） | `core/foundation/data_registry/README.md`"多根加载"一节此前只说"跨根主键重复默认仍判定为阻断错误"，未提 `AllowOverride`/行级 `override`/`final` 的例外；实际 `DataRegistry.cs` 早已实现该例外语义（`data/README.md`"多根加载与合并规则"一节已准确描述），是 README 单侧滞后。 | `core/foundation/data_registry/README.md`"多根加载（`LoadAll(IReadOnlyList<IDataSource>)`）"一节（第 46-61 行）补全 `AllowOverride` 默认 `true`、后层行 `override:true` 整行覆盖记诊断不报错、前层行 `final:true` 拒绝覆盖仍阻断、两字段仅多根同主键重复时生效（单根为真判 Warning 并忽略）的完整条件语义；`core/foundation/data_registry/core/DataRegistry.cs` 相关方法级 XML 注释同步补全（V-01 修复同批次一并完成，见该文件 `schema_version`/`override`/`final` 相关注释段）。提交 `44f320d`。 | 走查 `core/foundation/data_registry/README.md` 第 46-61 行确认与 `data/README.md`"多根加载与合并规则"、`DataRegistry.cs` 类型级判断记录"覆盖语义"三处描述一致，未发现新的分类矛盾；`core/foundation/data_registry/tests/DataRegistryTests.cs` 中既有的 override/final 相关用例（跨根主键重复、`AllowOverride=false`、单根 override 字段为真判 Warning 等场景）随三次合并后 `dotnet test` 全绿，行为与文档描述一致。 | 只改文档措辞，不改代码行为——`DataRegistry.cs` 的 override/final 判定逻辑本身在 wo/core17 分支未发生变化（仅 XML 注释补全），核实重点是"文档现在是否准确描述已有实现"，结论是准确。 |
| DOC-162-02 发布计划仍写三个 `.tgz` | **成立，已修复** | `architecture/落地计划/落地方案与分阶段计划.md`（原约 260 行）"互斥关系，`.github/workflows/release.yml` 仍然只把 zip/lock 与三个 `.tgz` 一并作为 GitHub Release 附件上传"一句仍写三个 `.tgz`；实际正式交付四个包（`com.gamefoundation.adapter.unity`/`framework-data`/`toolchain`/`adapter.headless`，ADR-0018 决策 3 已把 headless 适配层转正为第四个包），`.github/workflows/release.yml` 实际上传九件附件：`ws-game-<ver>.zip`/`.lock`/`-samples.zip`、四个 `.tgz`、`get_framework.ps1`、`_hash.ps1`（该文件 264-291 行 `$requiredNames`/`$assetPaths` 两个数组逐一列出）。 | `落地方案与分阶段计划.md` 对应句改写为明确列出九件附件（`ws-game-<ver>.zip`、`.lock`、`-samples.zip`、四个 `.tgz` 具名、`get_framework.ps1`、`_hash.ps1`），不再用"三个 `.tgz`"笼统概括。 | 走查 `.github/workflows/release.yml` 264-291 行 `$requiredNames`（9 项）与 `$assetPaths`（9 项路径映射）确认改写后的文字描述与实际上传附件逐一对应。 | 该文件同一小节 207/234/244/245 行此前已经正确写"四个包"，只有约 260 行这一处沿用了旧的"三个 `.tgz`"表述——是 ADR-0018 决策 3 落地时（第 37 行变更记录，`.github/workflows/release.yml` 三处多处硬编码改四包）遗漏同步的一句 prose，不是系统性问题，只此一处需要改。 |
| DOC-162-03 teleport nested 元素称"仅数组" | **成立，已修复** | `落地方案与分阶段计划.md`（原约 1371 行）"nested teleport 元素/引用完整性校验"一行称 `teleport_points` 数组内每个元素"仅做'是数组'这一层检查、不展开逐条元素形状"；实际 `core/foundation/scene_router/core/WorldMapSchema.cs:60`-`79` 的 `teleport_points` 字段已登记 `item: PointItemSchema`，元素结构 `{id?, position?}` 已展开到 `id`/`position` 两个子字段（`:92`-`99`，`position` 提供时 `x`/`y` 必填），并非只做容器级"是数组"检查。 | 该行改写为区分两层："结构已登记"（元素形状由 `PointItemSchema` 校验）与"引用目标完整性不由登记表保证"（命名点是否被 `teleport_target_ref` 正确引用、引用目标地图/落点是否存在，由 `Core.Gameplay.Assembly.TeleportTargetResolver` 解析时按 id 查找并温和降级，属于该解析器与调用方的业务约束，不是登记层职责），分类仍保留"未实现"但收窄为"引用目标完整性校验"这一项残余真实缺口，不再声称结构未登记。 | 走查 `WorldMapSchema.cs:60`-`79`（`PointItemSchema` 定义，`id`/`position` 两个子字段，`position` 内 `x`/`y` 必填）与 `:92`-`99`（`teleport_points` 字段登记 `item: PointItemSchema`）确认改写后的描述与源码一致；同时核对 `ISSUES.md`"不计入问题的边界"表中 `teleport_points` 一行结论（"元素结构已登记，命名点由 TeleportTargetResolver 解析，引用目标完整性不由 schema 保证"）与本行改写后的措辞一致，未产生新的分类矛盾。 | 只改措辞、不改"引用完整性校验未实现"这一结论——真正的开放缺口是跨地图/跨表引用完整性检查，不是元素形状登记（该部分已完成，只是文档滞后未同步）。 |
| DOC-162-04 ATB 被分类为"明确非目标" | **成立，已修复** | `落地方案与分阶段计划.md`（原约 1366 行）"能力边界与未默认接入能力索引"表 ATB 一行分类"明确非目标"；实际 `architecture/adr/0013-时间模型可替换即时与回合制同一规则层.md` 第 15 行原文"预留 `atb` 作为后续扩展位，本次不展开"——是"本版延期"而非无计划展开的"明确非目标"；`core/foundation/sim_loop/schema/TimeModelSchema.cs:23` 的 `InitiativePolicyValues` 已含 `"atb"` 合法枚举值，`core/foundation/sim_loop/core/TurnScheduler.cs:77`-`81` 遇到该策略时装配期抛 `NotSupportedException`（轮次调度器暂不支持，不是 schema 层拒绝）。 | 该行分类列从"明确非目标"改为"延期/预留"，说明栏补充与 ADR-0013 原文对照的判断记录：合法预留枚举值已存在于 schema，轮次调度器暂不支持，区别于"明确非目标"（无计划展开、纯占位不代表未来会启用）。 | 走查 `architecture/adr/0013-*.md` 第 15 行、`TimeModelSchema.cs:23`、`TurnScheduler.cs:77`-`81` 三处确认改写后的分类与说明同时与 ADR 原文决策、校验器枚举、运行期异常三处源码/文档一致；同时核对 `ISSUES.md`"不计入问题的边界"表 ATB 一行已使用"延期"一词（与本表改写方向一致，未产生新的分类矛盾）。 | 现行"能力边界与未默认接入能力索引"表分类口径共六类（已实现未默认接线/游戏责任/未实现/明确非目标/暂不落地/已实现且默认接线-归档），本次改写在 ATB 一行新引入"延期/预留"标签，未同步扩写六类口径的正式legend定义——按任务范围"只改措辞、不改结论"从简处理，只改这一行的分类词与说明，不重构整个legend段落；如后续需要把"延期/预留"正式纳入六类口径的第七类，需另经设计层判断，本轮不越权代做。 |
| DOC-162-05 04 号文档 `field_finite` 文案范围 | **成立，已修复**（整合 agent 核实） | `architecture/04_数据与内容管线.md`"变更记录"表 2026-09-10（消费方反馈"schema/expr 边界审计"）一行新增 `field_finite` 检查项时，措辞把 `Number`/`Int` 两种字段种类都写进适用范围；实际 `DataRegistry.ValidateFieldValue`（现 `core/foundation/data_registry/core/DataRegistry.cs` 第 1162-1196 行）里 `field_finite` 校验只出现在 `FieldKind.Number` 分支（第 1173-1196 行），`FieldKind.Int` 分支（第 1162-1171 行）走的是 `JsonNumber.TryGetInt64` 失败即报 `field_type`，两条检查名互斥，不存在"先类型错误优先、其次数值有限"的顺序关系。 | `architecture/04_数据与内容管线.md`"变更记录"表新增一行纯措辞收窄说明（见本文件合并后该表 2026-09-10 倒数第二行，"勘误（第十七方深度审核 DOC-162-05）"），不改变任何校验行为、不改变第 5 节校验项清单条目本身。提交 `44f320d`。 | 走查 `core/foundation/data_registry/core/DataRegistry.cs` 第 1162-1196 行源码确认 `field_finite` 检查名仅出现在 `case FieldKind.Number:` 分支内（`!double.IsFinite(nn.Value)` 判断），`case FieldKind.Int:` 分支（`!(raw is JsonNumber ni && ni.TryGetInt64(out var intValue))` 判断）不含 `field_finite` 字样，与勘误后的文档措辞一致；`architecture/04_数据与内容管线.md` 合并后该行完整保留（三次合并均未产生实质冲突，见"合并冲突点清单"）。 | 只改文档措辞、不改代码——代码本身（`ValidateFieldValue` 的两个分支）在 wo/core17 分支未发生变化，是纯粹的"上一条勘误新增检查项说明时写宽了范围"的自我纠正。 |

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

## 三方合并与 V-01/V-02/V-03 独立核实（整合 agent 补充，2026-09-10）

整合 agent 在主树 `main` 上依序 `git merge --no-ff wo/core17`（提交 `d7c1c6c`）→
`git merge --no-ff wp/tool17`（提交 `b344704`）→ `git merge --no-ff wq/editor3`（提交 `b797f03`），
每次合并后均以 `dotnet build Core.sln -c Release --artifacts-path <scratchpad>/build_wr` 验证通过；
三次合并全部完成后：

- 六工程 `dotnet test --artifacts-path <scratchpad>/build_wr` 全绿，测试数量相对合并前基线
  （提交 `e103f18`）变化：Tests.Foundation `852 → 910`（`+58`，其中 wo/core17 单独贡献 `+26`，
  精确隔离方法：临时 `git worktree add` 到 `e103f18` 与 `d7c1c6c`（仅 wo/core17 已合并）两个提交
  各自独立 `dotnet test`，比较结果后已 `git worktree remove` 清理，不留痕迹）；Tests.Numbers
  `127 → 128`（`+1`，wo/core17 单独贡献 `+1`）；Tests.Gameplay `650 → 656`（`+6`，wo/core17
  单独贡献 `+6`）；Tests.Rules/Tests.Carriers/Tests.PresentationCommon 三工程测试数量不变
  （`463`/`398`/`564`，wq/editor3 与 wp/tool17 对这三个工程无测试新增）。
- `python -m pytest toolchain/tests -q`：178 passed（三方合并后全量，含 wp/tool17 本轮新增的
  ABI 表面差异用例与 wq/editor3 新增的 `test_sample_table_empty.py`）。
- `python toolchain/validate_data.py --strict`：`tables 63, records 300, errors 0, warnings 0,
  overrides 1`。
- `powershell -File toolchain/abi_probe.ps1 -ArtifactsPath <scratchpad>/build_wr -Configuration
  Release`：`breaks=0 allowed=0 additions=253 RESULT=OK`——三方合并结果未发现任何索引器/运算符
  /事件访问器类可见性破坏，无需 façade 补救提交。

V-01/V-02/V-03 三项复现结果由整合 agent 独立验证（不复用/照抄 wo/core17 提交说明的文字表述），
方法：在 scratchpad 内临时 `git worktree add` 出 `4faab73`（修复前基线）与当前合并后主树（修复后）
两份代码，各自写一个独立的 `dotnet run` 控制台复现程序（引用对应工作树的 `Core.Foundation`/
`Adapters.Stub` 工程，不修改被测代码本身），对同一组边界输入（`schema_version=4294967297`、
`TryGetInt64(2^63)`、`TryGetInt64(long.MinValue)`、`FromInt64(9007199254740993)` 往返）分别在
修复前/修复后两份代码上实际运行，逐项核对输出是否与审核报告、wo/core17 提交说明描述的症状/修复
结果一致——三项复现结果详见上方核实表 V-01/V-02/V-03 行的"复现"列，结论：三项审核报告描述属实、
wo/core17 的修复均已生效、未发现夸大或遗漏。复现用的临时工程与临时 worktree 均已在验证后清理，
不落入仓库版本控制。

## 整合阶段全量门禁验收结果（步骤 3，2026-09-10）

三方合并 + 本文档回填提交（`3542b08`）落定后，在主树上执行全量 `check.ps1`（不带
`-SkipUnity`/`-Quick`，跑满全部步骤含 Unity EditMode/PlayMode）：

```
powershell -File check.ps1 -ArtifactsPath <scratchpad>/build_wr_check -LogFile <scratchpad>/check_full_wr.log
```

退出码 0，27 个步骤全部 `PASS`（3 个 IL2CPP 步骤 `SKIP`，原因"未传 `-Il2cpp`"，属预期行为，不计入
失败），总耗时 452s：

| 步骤 | 结果 | 关键计数/说明 |
|---|---|---|
| 门禁自检：Test-NativeExitCode | PASS | 对失败/成功原生命令判定均正确 |
| `dotnet build Core.sln -c Release` | PASS | 0 警告、0 错误 |
| `dotnet test Core.sln -c Release --no-build`（六工程） | PASS | Tests.Foundation 910、Tests.Numbers 128、Tests.Carriers 398、Tests.Rules 463、Tests.PresentationCommon 564、Tests.Gameplay 656，共计 3119 用例，0 失败、0 跳过 |
| ABI 探针（`toolchain/abi_probe.ps1`） | PASS | 独立单跑核实 `breaks=0 allowed=0 additions=253 RESULT=OK`（见上节），本次门禁内重跑同样退出码 0 |
| `validate_data.py --strict`（合并根） | PASS | tables 63, records 300, errors 0, warnings 0, overrides 1 |
| `validate_data.py --data-root data/_framework`（框架根单独） | PASS | tables 5, records 124, errors 0, warnings 1（`l10n.text` 未加载导致的文本键存在性检查跳过，非新问题） |
| 元数据门禁 `validator --schema-audit` | PASS | tables 63, fields 783, errors 0, warnings 0 |
| `gen_event_constants.py --check` | PASS | 40 个常量与生成文件一致 |
| `gen_placeholder_assets.py --check` | PASS | 通过 92/92，失败 0 |
| `import_assets.py check --dataset _sample` | PASS | 检查 10 条 display.map / 3 条 vfx.def / 3 条 sfx.def / 1 条 world.map，0 个问题 |
| `format_data.py --schema-order --check` | PASS | 共检查 64 个文件，需要重排 0 个 |
| `python -m pytest toolchain/tests -q` | PASS | 178 passed（含 wp/tool17 的 ABI 表面差异新增用例与 wq/editor3 的 `test_sample_table_empty.py`，后者为告警级门禁，本次未触发任何告警级失败） |
| 禁用词扫描：全仓库不出现具体游戏代号 | PASS | — |
| 禁用词扫描：architecture 正文不出现引擎/语言/框架/工具名 | PASS | — |
| 工作树文本文件无 CR | PASS | — |
| 版本一致性：VERSION/package.json/packages-lock.json/CHANGELOG.md | PASS | VERSION=1.16.2，四者一致（发布前应有状态） |
| `build.ps1 -SkipTests`（同步 DLL） | PASS | — |
| 清单一致性（四个 npm 包版本 + `npm pack --dry-run`） | PASS | 四个包 version=1.16.2 一致，排除清单符合预期（adapter.unity 排除 model/anim 占位资产，toolchain 排除预编译 validator） |
| Unity 编译期 | PASS | Unity 退出码 0 |
| Unity EditMode 测试 | PASS | total=70 passed=70 failed=0 |
| Unity PlayMode 测试 | PASS | total=272 passed=272 failed=0 |
| 播放器构建 + `-gf-smoke` 冒烟（连续模式默认路径） | PASS | — |
| 播放器 `-gf-smoke-discrete` 冒烟（离散模式路径） | PASS | — |
| IL2CPP 播放器构建 | SKIP | 未传 `-Il2cpp`（预期） |
| IL2CPP 播放器 `-gf-smoke` 冒烟 | SKIP | 未传 `-Il2cpp`（预期） |
| IL2CPP 播放器 `-gf-smoke-discrete` 冒烟 | SKIP | 未传 `-Il2cpp`（预期） |
| 消费方冒烟（`toolchain/consumer_smoke.ps1`） | PASS | — |

`sample_table_empty` 告警级门禁（wq/editor3 本轮新增，随 `python -m pytest toolchain/tests -q`
一并执行）本次未把整体判为 `FAIL`——走查结论：这是设计意图内的正常结果，不是门禁本身失效。
`item.affix`/`item.set`/`world.flag_schema` 三张此前无示例数据的表已在 wq/editor3 提交
`870884c` 里补齐 `data/_sample` 示例行（见"第 23 条"），本次合并后的 `_sample` 数据集里已不存在
空表，因此该告警级检查项本身无内容可警告，PASS 是正确结果，不需要额外处理。

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
- 本文档 V-01/V-02/V-03、DOC-162-01/05 五行原按任务要求写"待整合回填"（本 agent WT 未越权代
  另一位并行 agent 填写，未读取其工作树内容、未推测其提交范围，避免猜错造成二次勘误）；三方合并
  完成后，已由整合 agent 按上方"三方合并与 V-01/V-02/V-03 独立核实"一节所述方法独立复现验证并
  回填，不是照抄 wo/core17 提交说明的文字表述。
