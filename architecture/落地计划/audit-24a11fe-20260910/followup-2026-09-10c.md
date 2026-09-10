# 第十六轮审核修复跟进核实（2026-09-10）

基线：`24a11fe28f9647cd532c41f56f7ab18c00fb8516`（v1.16.1）。本文档核实
[AUDIT_REPORT.md](AUDIT_REPORT.md) 问题汇总表七项：一项已确认 P2 框架数据合同（F-01）、一项已确认
P2 框架表达式合同（F-03）、一项已确认 P2 ABI 工具门禁（ABI-116-01）、一项已确认 P2 文档问题
（DOC-116-02）、三项 P3 文档/工程说明（DOC-116-01/03/04、TOOL-116-01 合记为一条线索）、一项 P3
相关 API 观察（F-02）的实际修复结果。

修复由并行执行的两个 agent 分工：本 agent（WL）负责 `toolchain/**`、各 README、
`architecture/README.md`、`architecture/adr/0022*.md`、`architecture/落地计划/**`、
`CHANGELOG.md`——即 ABI-116-01 与五项文档更新（DOC-116-01～04、TOOL-116-01）；另一位并行 agent
负责 `core/**` 源码与测试、`architecture/04_数据与内容管线.md`——即 F-01/F-02/F-03 三项框架数据/
表达式合同问题。本文档由本 agent 落笔核实自己负责的条目；F-01～03 行的修复位置/验收留给整合 agent
按另一位 agent 的实际提交回填。

提交哈希表：

| 提交 | 标题 | 范围 |
|---|---|---|
| `<提交待填>` | 根治 ABI-116-01：abi_surface dump/compare 补记可见性等覆盖项 | `toolchain/abi_surface/SurfaceDumper.cs`、`toolchain/abi_surface/SurfaceCompare.cs`、`toolchain/abi_surface/Program.cs`、`toolchain/tests/test_abi_surface_compare.py`、`toolchain/README.md` |
| `<提交待填>` | 五项文档更新：ADR 索引/Skill README/DataRegistry README/ADR-0022 措辞/check.ps1 副作用说明 | `architecture/README.md`、`README.md`、`core/rules/skill/README.md`、`core/foundation/data_registry/README.md`、`architecture/adr/0022-登记表补充导航与编辑元数据.md` |
| `<提交待填>` | 归档 audit-24a11fe-20260910 + 落地方案新增小节 + CHANGELOG | `architecture/落地计划/audit-24a11fe-20260910/`（新增）、`architecture/落地计划/落地方案与分阶段计划.md`、`CHANGELOG.md` |
| `<提交待填>` | F-01/F-02/F-03（另一位 agent） | `core/**`、`architecture/04_数据与内容管线.md` |

## 核实表

| 条目 | 判断 | 复现 | 修复位置 | 验收 | 判断记录 |
|---|---|---|---|---|---|
| ABI-116-01 ABI 工具门禁漏报可见性收窄 | **成立，已修复** | 条件：一份公开签名从 `public` 收窄为 `protected`，旧编译 consumer 不重编译运行。实际（修复前）：`toolchain/abi_surface/SurfaceDumper.cs` dump 不记 method/ctor/field/event/属性访问器的可见性档位（`IsMemberVisible` 只判"是否可见"，不记是哪一档），两次 dump 输出逐字节相同，`compare` 判 `breaks=0`；同一场景下真实旧 consumer 实跑抛 `System.MethodAccessException`。预期：dump 记录可见性并对"收窄"判破坏、对"放宽"豁免；同时核对虚方法变 sealed、实例↔静态、类型种类变化、泛型约束变化、const 字段值变化等相邻覆盖项。 | 1) `toolchain/abi_surface/SurfaceDumper.cs`：TYPE 行 flags 首 token 固定为类型可见性（`public`/`nested-public`/`nested-protected`/`nested-protected-internal`，`TypeVisibility` 方法）；ctor/method/field/event 的 MEMBER 行 flags 首 token 固定为成员可见性（`public`/`protected`/`protected-internal`，复用/新增 `Visibility(MethodAttributes)`/`Visibility(FieldAttributes)`）；属性沿用既有 `get:VIS,set:VIS` 不变；新增 `FormatGenericConstraints`（泛型类型/方法定义的约束编入 flags 的 `constraints:` token）；非枚举 const 字段的内联值编入 sig（`FormatConstValue`）。2) `toolchain/abi_surface/SurfaceCompare.cs`：新增 `CompareResult.Widened`、`SplitVisibility`/`SplitPropertyIdentity`/`VisRank` 与"可见性放宽豁免"逻辑——baseline 行消失时先按"去掉可见性 token 后的身份"匹配 current-only 行，若可见性变宽（放宽）则计入 `Widened`、不计破坏，否则（收窄或身份本身变化，如参数/返回值/virtual/静态/约束/常量值变化）保持规则 1 原有判破坏逻辑不变。3) `toolchain/abi_surface/Program.cs`：`BuildReport` 新增"可见性放宽"小节展示。 | 复现：`<scratchpad>/wl_repro/` 用真实 `dotnet build` 构造 baseline（`public void Bar()`）/current（`protected void Bar()`）两份最小库 + 一份只编译一次的旧 consumer——换上 current 库不重编译，运行抛 `System.MethodAccessException`（`run-current.log` 已保留）；修复前 `abi_surface dump`+`compare` 对同一对 DLL 判 `breaks=0`，修复后判 `breaks=1`（RESULT=BREAKING，`removed_or_changed`），且反向（protected→public）判 `breaks=0`+"可见性放宽"1 行、不计破坏。新增 `toolchain/tests/test_abi_surface_compare.py` 15 个用例（可见性收窄/放宽覆盖 method/ctor/field/event/property/TYPE 六类、virtual→sealed-override、实例↔静态、class→struct、泛型约束变化、非枚举 const 值变化的正负例，外加一个用真实 `dotnet build` 复现 `MethodAccessException` 并断言 `compare breaks>0` 的端到端负例 `test_public_to_protected_negative_oracle_end_to_end_via_real_dll`），随文件原有 10 个用例一并 `python -m pytest toolchain/tests/test_abi_surface_compare.py -q`：25 passed。用新版工具对 `dist/ws-game-1.12.0.zip`、`dist/ws-game-1.13.0.zip` 两份历史基线重跑当前工作树六个 DLL：均 `breaks=0`，未发现历史真实破坏（1.13.0 场景下 `toolchain/abi_probe.ps1` 第 1 点手写消费方探针因 consumer 源码只锚定 `abi_probe_baseline.txt` 记录的 1.12.0 签名集合、对 1.13.0 编译会失败——这是既有已知边界，与本次改造 `toolchain/abi_surface` dump/compare 覆盖范围无关，未在本轮改动范围内，改用直接 dump+compare 两份基线六 DLL 的方式验证）。 | 修复采用"把可见性编进 dump 行"而非新增比较维度的做法——一旦可见性成为行内容的一部分，"收窄"天然落入既有规则 1（行消失即判破坏），只需额外补一条"放宽豁免"逻辑排除误伤；这样改动范围小、不影响既有规则 2（接口新增 abstract 成员）与 allowlist 机制。virtual→sealed-override/实例↔静态/class→struct 三项在修复前就已经因为 flags/kind 差异触发规则 1 判破坏（本轮未改这部分逻辑，只是新增负例把这一行为钉成回归测试）；真正的新增覆盖项是可见性（六类）、泛型约束变化、非枚举 const 字段值变化三项。 |
| DOC-116-02 Skill README `FindUnits` 描述过时 | **成立，已修复** | `core/rules/skill/README.md:80-86` 仍称 `ISkillHost.FindUnits` 当前恒返回空列表、无条件记诊断；实际 `SkillHost.FindUnits`（`core/rules/skill/core/SkillHost.cs:274` 起）已实现委托 `ISpatialQuery.QueryShape` 的形状查询、按 `UnitFilter` 全维度过滤（`AliveOnly`/`Exclude`/`RequiredTags`/`ExcludedTags`/`Relation`）、结果排序，且生产装配 `RulesAssembly.cs:359` 起默认把 `Spatial` 传给 `SkillHost` 构造函数；只有未注入 `ISpatialQuery` 时才降级为空列表+诊断。 | `core/rules/skill/README.md` 判断记录 3 改写：先描述已实现的查询/过滤/排序能力（附 `SkillHost.FindUnits` 方法文档索引），再说明"未注入 `ISpatialQuery`"这一降级分支的边界，与判断记录 2（射程/视线检查同款降级惯例）对齐。 | 走查 `core/rules/skill/core/SkillHost.cs:274-320`（`FindUnits` 实现）与 `core/rules/assembly/RulesAssembly.cs:359-361`（`Skill = new SkillHost(..., Spatial, ...)`）确认改写后的 README 描述与源码一致。 | 根文档（`README.md`"能力边界"一节）此前已经用"已实现"口径描述 `ISkillHost.FindUnits`（第 9 行"`target.chain` 形状范围目标查询"一句提及"已委托 `ISpatialQuery.QueryShape` 并按 `UnitFilter` 全维度过滤、生产装配默认注入 `Spatial`/`Factions`"），本次只是把 `core/rules/skill/README.md` 模块级文档的过时表述同步过去，不是新发现的能力差异。 |
| DOC-116-01 ADR 索引计数漂移 | **成立，已修复** | `architecture/README.md:83-92` 文件清单只列到 `adr/0019`、结尾写"共 19 篇"；仓库实际已有 `adr/0020`～`adr/0023` 四篇（`architecture/adr/README.md` 索引表本身已经是最新的 23 条，只有 `architecture/README.md` 的文件清单一节与根 `README.md` 的两处计数漂移）。 | `architecture/README.md`：变更记录追加一行（2026-09-10，DOC-116-01），文件清单补 `adr/0020`～`adr/0023` 四行，结尾改"共 23 篇"。根 `README.md`：第 16 行"19 条 ADR"改"23 条 ADR"，第 228 行"17 条架构决策记录"改"23 条架构决策记录"。 | `ls architecture/adr/*.md` 与 `architecture/adr/README.md` 索引表交叉核对，确认 0001～0023 共 23 篇、无缺号。 | 根 `README.md` 存在两处独立计数（第 16 行文件清单摘要、第 228 行 ADR 索引入口说明），均已漂移，一并修正；`architecture/adr/README.md` 自身索引表在提交 ADR-0023 时已经同步维护，未发现该文件需要改动。 |
| DOC-116-03 DataRegistry README 检查项表漏项 | **成立，已修复** | `core/foundation/data_registry/README.md:65` 检查项表 `reference_integrity` 一行未提及 ADR-0022 新增的 `IdList` 元素引用检查（`DataRegistry.cs` 的 `ValidateIdList` 方法，约 1269 行起，按元素路径逐个做与标量 `Reference` 字段相同的存在性检查）；检查项表也缺 `field_range`（ADR-0021，`ValidateFieldRange`，约 1251 行起）整行。 | `core/foundation/data_registry/README.md` 检查项表：`reference_integrity` 一行补充 `IdList` 元素引用说明（ADR-0022）；新增 `field_range` 一行（ADR-0021）；按任务书要求在表后留一行占位注释 `<!-- 整合时补：数值有限检查项 -->`，供整合阶段按另一位 agent 本轮新增的"数值有限"检查实际命名回填。 | 走查 `core/foundation/data_registry/core/DataRegistry.cs` 确认 `ValidateIdList`（1269 行起，含判断记录引用 ADR-0022）与 `ValidateFieldRange`（1251 行起）两个方法的检查名分别是 `reference_integrity`（复用）与 `field_range`（独立），与改写后的 README 描述一致。 | 占位注释按任务书要求原样保留，未替另一位 agent 猜测检查名——避免猜错名称造成二次勘误。 |
| DOC-116-04 ADR-0022 命令边界表述混淆 | **成立，已修复** | `architecture/adr/0022-登记表补充导航与编辑元数据.md:68`"`validate_data.py --json` 已把 `--json` 透传给 `validator`，会自动带上这些新字段"一句容易被读成"`validate_data.py --json` 的输出也含 `tables_list`/`field_meta`/`field_ranges`"；实际 `validate_data.py --json` 跑校验（不传 `--list-tables`），顶层固定 `{skeleton, validator, exit_code}`，`validator` 子对象是校验结果（`tables`/`records`/`errors`/`warnings`/`blocking`/`issues`/`overrides` 等），不含表清单三个字段——这三个字段只在显式 `--list-tables --json` 组合、`tablesForListing` 非空时才写出（`toolchain/validator/Program.cs` `PrintJson`）。 | `architecture/adr/0022-登记表补充导航与编辑元数据.md` 第 5 节"导出"改写为分别说明两条命令：`validator --list-tables --json`（元数据导出，`tables_list` 数组）与 `validate_data.py --json`（校验结果输出，顶层 `skeleton/validator/exit_code`，不含表清单），并追加一条修订记录说明本次澄清不改变任何决策。 | 走查 `toolchain/validator/Program.cs` `PrintJson` 方法（`tables_list`/`field_meta`/`field_ranges` 三段代码块均嵌在 `if (tablesForListing != null)` 分支内）与 `toolchain/validate_data.py`（`_run_validator`/主流程调用 `cmd.append("--json")` 但不追加 `--list-tables`）确认改写后的 ADR 描述与源码行为一致。 | 措辞技术无关但按任务要求引用了仓库工具路径（`toolchain/validator`、`toolchain/validate_data.py`），符合"架构文档禁止具体工具/框架名，但 ADR 正文可引用仓库工具路径"的既有惯例（本 ADR 此前已多处引用同类路径）。 |
| TOOL-116-01 `check.ps1` 副作用说明不完整 | **成立，已修复** | `README.md:119`"`check.ps1` 自身只读跑校验/测试/构建、不写仓库文件"一句只讲了"不改写已跟踪文件"，未说明 `-ArtifactsPath`（默认 `bin\_check_artifacts`）与"包清单一致性"步骤内部调用的 `build.ps1 -SyncOnly -Dist auto` 会往 `.gitignore` 覆盖的 `bin/`、`dist/` 目录写入构建产物/打包中间物——即使 `-SkipUnity` 也一样会写（这两步不属于 Unity 相关步骤）。 | `README.md` 该段改写：明确"不写仓库文件"仅指不改写已跟踪的源码/文档，说明 `-SkipUnity` 下仍会写入的两类 `.gitignore` 覆盖目录，并把 Unity 编辑器进程重写已跟踪文件（ProjectSettings.asset 等）这一件事与本脚本自身的中间物写入分开表述，避免读者混为一谈。 | 走查 `check.ps1` 步骤列表确认"包清单一致性"步骤依赖`build.ps1 -SyncOnly -Dist auto`（该脚本自身文档 68 行"额外把...打成一份版本快照 `dist/<version>/`"），且该步骤排在 Unity 相关步骤之前、`-SkipUnity` 不会跳过它；`$ArtifactsPath` 默认值 `bin\_check_artifacts` 已被 `.gitignore` 的 `bin/` 规则忽略（`README.md` 原文本身已提及这一点，本次只是把它与"不写仓库文件"的整体论断放在一起重新表述，纠正后者过于绝对的措辞）。 | 本条与 DOC-116-01/02/03/04 一并作为"文档措辞/口径"类修复，不涉及代码行为变化，只是让说明更准确、避免误导消费方认为"跑一遍全量 `check.ps1 -SkipUnity` 绝对零副作用"。 |
| F-01 非有限 Number 放行 | **成立，已修复** | 条件：数据表 JSON 文件中某数值字段写作语法合法但求值溢出的指数记法（如 `"1e309"`）。实际（修复前）：`DataRegistry.LoadAll` 对该文件正常加载，校验报告 `errors=0 blocking=False`，该字段实际值 `actual=+Infinity`（`JsonReader.ParseNumber` 对合法语法的 `double.Parse` 溢出静默饱和为 `+Infinity`，不抛异常，也未在校验期被任何检查项拦下）。预期：非有限数值应在加载期被拒绝或至少在校验期报错阻断。 | `JsonReader.ParseNumber`（`core/foundation/common/json/JsonReader.cs:319-393`，`double.Parse` 之后立即 `double.IsFinite` 检查，抛带位置的 `JsonParseException`）+ `JsonWriter.FormatNumber`（`core/foundation/common/json/JsonWriter.cs:123-146`，把非有限检查挪到 `RawNumberText` 短路分支之前，独立守住写出口）+ `DataRegistry.ValidateFieldValue` Number 分支（`core/foundation/data_registry/core/DataRegistry.cs:996-1033`，第二道防线，覆盖绕过 `JsonReader` 直接构造 `JsonValue` 树的自定义 `IDataSource`，新检查名 `field_finite`）。 | `dotnet test Core.sln -c Release` 六工程全绿（Tests.Foundation 852 通过，本轮净增 28 个 `[Fact]` + 4 个 `[Theory]`/11 组 `[InlineData]`，合计 +39）；修复后同一份含 `"1e309"` 字段的数据文件经 `JsonReader.Parse` 直接抛 `JsonParseException`，该表加载在 `LoadOneTablePartial` 的 `catch (JsonParseException ex)` 分支（`DataRegistry.cs:713`）归入 `envelope` 级 Error，`errors=1 blocking=True`（整表判阻断）；`JsonWriter` 对携带 `+Infinity`/`NaN` 的 `JsonNumber` 写出统一抛 `InvalidOperationException`。 | 独立走查确认：`JsonReader.ParseNumber` 383 行 `double.Parse` 之后立即做 `IsFinite` 检查，抛出的 `JsonParseException` 在 `DataRegistry.cs:713` 归为 `envelope` 级错误——不是 `field_finite`；`field_finite`（`DataRegistry.cs:1030`）是绕过 `JsonReader` 直接构造 `JsonValue` 树时才会命中的第二道防线，两条路径不冲突、覆盖面互补。`JsonWriter.FormatNumber` 的检查顺序调整（先判有限性、后判 `RawNumberText` 短路）是必要的，否则 `RawNumberText` 非空时会绕过检查原样吐出非法文本。 |
| F-02 FieldRange 接受 NaN/Infinity 边界 | **成立，已修复** | 条件：调用 `FieldRange.Range(min: double.NaN)` 构造范围端点。实际（修复前）：构造成功不抛异常，且 `Range(min: double.NaN).Contains(0)` 返回 `true`（NaN 与任何值比较恒为 false，导致 `value < Min.Value` 判断永假，端点检查形同虚设）。预期：NaN/Infinity 不该是合法端点，`Contains` 对任何输入都应给出符合直觉的判断。 | `FieldRange.Range`（`core/foundation/data_registry/contracts/FieldRange.cs:52-77`，构造入口对 `min`/`max` 分别做 `double.IsFinite` 检查，非有限端点抛 `ArgumentException`，无界统一改用 `null`）+ `FieldRange.Contains`（同文件 94 行起，对 `value` 为 `NaN` 时独立短路返回 `false`，不依赖端点比较）。 | `dotnet test` 全绿（含 `FieldRangeValidationTests` 新增用例，计入上一行 +39 合计）；修复后 `FieldRange.Range(min: double.NaN)` 抛 `ArgumentException`（"Min 不能是 NaN/Infinity；表示该侧无界请传 null"）；`Range(min: 0).Contains(double.NaN)` 返回 `false`。 | 独立走查确认：`Contains` 里 NaN 短路检查必须放在 `Min`/`Max` 比较之前——否则同样的"NaN 比较恒假"问题会在 `value` 侧重演，即便端点本身已不可能是 NaN；构造入口拒绝 Infinity 端点的理由是"Infinity 端点与该侧无界（null）语义完全等价，只会造成同一件事两种表达"，与"无界应统一用 null 表达"的既有设计意图一致。 |
| F-03 Expr 整数溢出外抛 OverflowException | **成立，已修复** | 条件：Expr 字段值是一个超出 `long` 可表示范围的整数字面量（如 `99999999999999999999`）。实际（修复前）：`ExprLexer.Tokenize` 内部 `long.Parse` 对超范围输入抛出不带位置信息的 `OverflowException`，该异常从 `DataRegistry.ValidateExprField` 原 `try` 块（只 catch `ExprParseException`）外逃、继续从 `LoadAll` 外抛；此时 `_tables` 已被替换为本轮加载结果但 `_blocked` 仍停留在方法开头设的 `false`，调用方捕获异常后若继续调用 `GetAll`，仍能读到 `count=1`（本该判定校验未完成、阻断读取的一份数据）。预期：任何词法/校验期异常都不应外逃，且外逃时也不能把 `_blocked` 停留在 `false`。 | `ExprLexer.Tokenize`（`core/foundation/expr/core/ExprLexer.cs:108` 起，整数分支改用 `long.TryParse`，超范围时抛带位置的 `ExprParseException`；浮点分支为 F-01 同源问题，`double.Parse` 溢出后加 `IsFinite` 检查同样抛带位置 `ExprParseException`）+ `DataRegistry.ValidateExprField`（`core/foundation/data_registry/core/DataRegistry.cs:1379` 起，`try` 块收窄为只对 `ExprParseException` 走既有 `expr_parsable`，新增 `catch (Exception ex)` 把其余任何未预期异常转为阻断级新检查项 `expr_validation_error`，不再外抛）+ `RunValidationAndBuildReport`（同文件 331 行起，`try/finally` 兜底：`completed` 标志只在正常走完全部校验后置 `true`，`finally` 里只要 `!completed` 就强制把 `_blocked` 回填 `true`，覆盖"校验期间真的有异常逃出本方法"这种理论边界，不让 `GetAll` 读到一份从未真正通过校验的 `_tables`）。 | `dotnet test` 全绿（含 `ExprLexerTests`/`ExprParserTests`/`DataRegistryTests` 新增用例，计入上述 +39 合计）；修复后同一份数据经 `LoadAll` 加载，超范围整数字面量在词法阶段即被 `ExprLexer.Tokenize` 拒绝、抛带位置的 `ExprParseException`，`DataRegistry.ValidateExprField` 捕获后归入既有 `expr_parsable` 检查项、位置指向字面量起始偏移 0（该表达式整体就是这一个字面量）；随后 `GetAll` 抛 `InvalidOperationException("数据校验未通过，禁止读取")`。 | 独立走查确认：`RunValidationAndBuildReport` 的 `try/finally` 兜底与 `ValidateExprField` 的 `catch (Exception ex)` 是两道独立防线，不是重复——`ValidateExprField` 防的是"表达式校验器自身、或其依赖的宿主回调（如 `IExprSchema.TryGetSignature`）抛出未预期异常"这一具体场景，转成阻断级问题项、不影响同批校验的其余记录继续跑完；`RunValidationAndBuildReport` 的 `try/finally` 防的是"万一某个校验入口（如自定义 `IValidationRule.Validate`）没有同等兜底、异常真的逃出本方法"这种理论边界，确保即使异常逃逸也不会把 `_blocked` 错误地留在 `false`。两者叠加才能同时保证"尽量不发生"与"万一发生也不留残留可读数据"两个层次。 |

## 全量门禁验收结果（本 agent 负责范围，2026-09-10）

1. `dotnet build toolchain/abi_surface/AbiSurface.csproj -c Release --nologo`：0 警告、0 错误
   （历经三次增量重建，逐次确认新增可见性/泛型约束/const 值代码可编译）。
2. `python -m pytest toolchain/tests/test_abi_surface_compare.py -q`：25 passed（含 15 个
   ABI-116-01 新增用例 + 原有 10 个用例）。
3. `python -m pytest toolchain/tests -q`：168 passed（全仓 `toolchain` 自身 Python 测试套件，
   本 agent 改动范围外的用例同批全绿，未发现回归）。
4. `dotnet build Core.sln -c Release --nologo --artifacts-path <scratchpad>/build_wl`：0 警告、
   0 错误（`toolchain/abi_probe.ps1`/独立 dump+compare 重跑 1.12.0、1.13.0 两份历史基线所需的
   当前工作树六个 DLL 构建）。
5. 1.12.0 基线重跑：`powershell -File toolchain\abi_probe.ps1 -BaselineVersion 1.12.0 -ArtifactsPath <scratchpad>/build_wl`：
   exit 0，`abi_surface compare` `breaks=0 allowed=0 additions=221 RESULT=OK`（手写消费方探针第 1
   点与表面差异第 2 点均通过）。
6. 1.13.0 基线重跑：因 `toolchain/abi_probe/Program.cs` 手写消费方探针源码只锚定
   `abi_probe_baseline.txt` 记录的 1.12.0 签名集合，直接传 `-BaselineVersion 1.13.0` 会在"编译 consumer
   （针对基线 1.13.0 DLL）"这一步失败（`CS0246`/`CS1729` 等，缺 1.13.0 之后才新增的规则类型）——这是
   `abi_probe.ps1` 既有已知边界（见该脚本 `.PARAMETER BaselineVersion` 判断记录："日常 MINOR/PATCH
   发布不应推进"该锚点文件），与本轮改造 `toolchain/abi_surface` dump/compare 覆盖范围无关。改用
   直接调用 `toolchain/abi_surface` 对提取自 `dist/ws-game-1.13.0.zip` 的六个基线 DLL 与当前工作树
   六个 DLL 做 dump+compare：`breaks=0 allowed=0 additions=132 RESULT=OK`。
7. 归档 `architecture/落地计划/audit-24a11fe-20260910/`：按仓库禁用词规则对本目录做不区分大小写的
   全文扫描，未发现任何具体游戏代号；
   自建 Python 脚本复核归档内全部 Markdown 相对链接均可解析（`git ls-files` 尚未纳入本目录，
   `toolchain/tests/test_markdown_relative_links.py -q` 当前对未跟踪文件不生效，通过是因为该
   pytest 用例的扫描范围本身限定为 `git ls-files` 已跟踪的 `*.md`——整合 agent `git add` 后应
   重跑该测试再次确认）。

## 异常与判断记录

- 本 agent 未执行任何 git 写操作（不 add/commit/stash/checkout），`git status --short` 中不属于
  本 agent 的改动（`core/**`、`architecture/04_数据与内容管线.md` 等，由另一位并行 agent 负责）
  未触碰；提交与最终统一验收由整合 agent 负责。
- ABI-116-01 用新版工具重跑 1.12.0/1.13.0 两份历史基线均 `breaks=0`，未发现任何历史真实破坏——
  本轮新增的覆盖项（可见性、泛型约束、非枚举 const 值）纯属"门禁能力补强"，不代表 1.16.1 当前
  六个核心 DLL 曾经发生过对应类型的未声明破坏。
- `toolchain/tests -q` 当前 168 passed、0 skipped，与 `AUDIT_REPORT.md` 记录的审计基线快照
  "161 passed、4 skipped"（166→ 应为 165 总数，取自审计当时那次 transcript）不是同一批可直接相减
  对账的数字——两次统计相隔的不只是本 agent 的改动：`git log` 显示审计基线提交 `24a11fe` 之后，
  同一会话内已经先落地了三个与本任务无关的 ADR-0023 提交（`d7a300f`/`8dc7008`/`7886c1d`），且
  `dist/` 目录在本轮任务执行前就已经存在 1.11.0～1.16.1 全部历史版本 zip（构建时间戳早于本 agent
  开始工作），会让原本依赖"本机是否有历史 zip"的 skip 条件转为通过。本 agent 自己确认的、可精确
  对账的唯一数字是 `toolchain/tests/test_abi_surface_compare.py` 一个文件：改动前 10 个测试函数、
  改动后 25 个（新增 15 个，见上表"复现"列列举的用例名），该文件单独 `pytest -q` 结果
  25 passed、0 failed、0 skipped，改动前后无回归。
