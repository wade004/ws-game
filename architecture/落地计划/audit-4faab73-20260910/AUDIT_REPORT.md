# ws-game 1.16.2 审核根报告

状态：审核完成；下列问题待修复，本轮未修改产品源码。归档完整性以 archive 内外 manifest 和校验结果为准。

本报告审核对象是冻结根 `D:\workespace\ws-game-artifacts\audit-4faab73-frozen`，detached HEAD `4faab73e7081f2984e7addb88fee051b6c0d3d02`，`VERSION=1.16.2`。文档—代码矩阵见 [doc-code-matrix.md](docs/doc-code-matrix.md)，文档问题逐条证据见 [docs-findings.md](docs/docs-findings.md)。

## 结论与排序

当前审定为 4 条 P2 实现/工具链问题、5 条独立 P3 文档问题。ABI 表面覆盖范围的文档更新与 ABI-1162-01 共用同一根因和修复，不另计一条 P3。

## 整体代码与工程结论

Core 到 Presentation 的依赖方向保持单向，核心代码未引入 UnityEngine、UnityEditor、Game 或 `Adapter.Unity`；引擎实现通过适配器窄契约接入。`GameFoundationBootstrap` 与 `FrameworkResidentHost`/`ShellRoot` 是 Unity 工作台的 sample 数据入口，`games/_template/Runtime/GameBootstrap.cs` 是新游戏的组合根；sample id 与 `data/_sample` 属于示例闭环，不应误判为 Core 反向耦合或新游戏必须入口。现有模块实现、测试集合和 1.16.2 版本交付证据已按矩阵与 delivery 记录核对，风险集中在整数边界与 ABI indexer 门禁覆盖。结论来自风险导向定点复核和保存的测试/包证据，不扩展为所有模块或所有 API 已完成验证。

| 排序 | ID | 级别 | 结论 |
|---|---|---|---|
| 1 | V-01 | P2 | `schema_version` 先转 `int` 时发生 32 位回绕，未来版本 `4294967297` 可被当成 1 放行。 |
| 2 | V-02 | P2 | `CurrencyPersistable` 用 `double` 承载 long 余额，2^53+1 保存后丢失 1。 |
| 3 | V-03 | P2 | `JsonNumber.TryGetInt64` 对无原始文本的 `2^63` double 错误返回 true/`long.MinValue`，可经迁移绕过 Int 校验。 |
| 4 | ABI-1162-01 | P2 | public indexer 参数变化造成旧 consumer `MissingMethodException`，surface dump/compare 未报破坏。 |
| 5 | DOC-162-01 | P3 | DataRegistry 多根重复注释漏掉 `AllowOverride`、`override`、`final` 条件。 |
| 6 | DOC-162-02 | P3 | 发布计划仍写三个 `.tgz`，当前交付为四个包。 |
| 7 | DOC-162-03 | P3 | 能力索引仍称 teleport nested 元素“仅数组”，落后于已登记的 item schema。 |
| 8 | DOC-162-04 | P3 | ATB 被分类为非目标，ADR 实际定义为本版延期预留。 |
| 9 | DOC-162-05 | P3 | 04 将 Int 非有限值统一写成 `field_finite`，与当前 Int 分支诊断名不一致。 |

## P2 问题证据

### V-01：schema_version 截断屏障

触发：最小合法 v1 表输入 `schema_version=4294967297`。

预期：1 接受；2 和所有大于 `Int32.MaxValue` 的未来版本报告 `schema_version` Error 并阻断，不能回绕为 1。

实际：验证记录 [validation.md](validation/validation.md) 的 V-01 表与成功 raw `presentation-consumer-rerun.log` 记录：v1 正常，v2 正确阻断，而 `4294967297` 为 `errors=0 blocking=False`。

源码：[DataRegistry.cs:737](D:/workespace/ws-game-artifacts/audit-4faab73-frozen/core/foundation/data_registry/core/DataRegistry.cs:737) 先取得 long，随后直接转换为 `int`，没有在转换前拒绝大于 `int.MaxValue` 的值。

责任：框架 DataRegistry 版本屏障。

修复验收：在转换前做无溢出边界检查或使用不回绕的整型策略；v1 仍可读，v2 与 `4294967297` 均出现 `schema_version` 阻断，且 `GetAll` 读屏障保持阻断。

### V-02：货币存档精度

触发：无 cap 的 `econ.currency`，调用 `SetBalance=9007199254740993`，经过 `CurrencyPersistable.Save → JsonWriter → JsonReader → Load`。

预期：long 余额精确往返，读回仍为 `9007199254740993`。

实际：验证记录 V-02 显示写出文本为 `9007199254740992`，读回同值，`exact=False`。

源码：[CurrencyPersistable.cs:44](D:/workespace/ws-game-artifacts/audit-4faab73-frozen/core/gameplay/economy/core/CurrencyPersistable.cs:44) 以 `new JsonNumber(balance)` 进入 double 表示，超过 2^53 后丢失精度。

责任：框架 Economy 持久化格式与 JsonNumber 边界。

修复验收：保留现有 Int64 余额合同，采用精确整数表示并完成 `Save → JsonWriter → JsonReader → Load` 往返；不以收窄到 safe double 范围作为本轮等价修复。任何破坏性格式迁移需另行决策。验收范围只覆盖该余额存档路径，不扩大为全存档扫描。

### V-03：Int 上界

触发：直接构造 `new JsonNumber(9223372036854775808d)`，再通过 `TableMigration` 放入真实 `FieldKind.Int` 字段。

预期：有限整数 double 只有在 `[-2^63, 2^63)` 内才可 `TryGetInt64`；2^63 应返回 false，DataRegistry 报 `field_type` 并阻断。

实际：验证记录 V-03 的 143–146 行显示带原始文本的 2^63 对照为 false，但无原始文本的直接值返回 `True value=-9223372036854775808`；迁移进入 Int 后 `errors=0 blocking=False`。

源码：[JsonValue.cs:108](D:/workespace/ws-game-artifacts/audit-4faab73-frozen/core/foundation/common/json/JsonValue.cs:108) 的上界判断在 double 提升后把 2^63 当作可接受；Int 校验调用点为 [DataRegistry.cs:1004](D:/workespace/ws-game-artifacts/audit-4faab73-frozen/core/foundation/data_registry/core/DataRegistry.cs:1004)。

责任：框架 JsonValue 整数转换与 DataRegistry Int 合同。

修复验收：无 RawNumberText 的有限整数 double 仅在 `[-2^63,2^63)` 返回 true；迁移后的 2^63 必须产生 `field_type` Error/阻断；long.MaxValue 精确原文对照仍接受。

### ABI-1162-01：indexer 门禁漏报

触发：最小 ABI fixture 将 `public int this[int i]` 改为 `int this[string i]`，旧编译 consumer 只替换为该 fixture 的新 DLL，不重新编译。

预期：旧 consumer 运行失败应被 ABI 门禁报告；surface dump 应将索引参数纳入属性签名并产生 break。

实际： [run5-console.log](delivery/run5-console.log) 第 30–39 行显示基线 consumer exit 0，换 DLL 后 `MissingMethodException=True`、consumer hash 不变；同时 `indexer_surface_exit=0_current_tool_misses_change`、`indexer_surface_dump_sha_equal=True`。这是门禁漏报，不是“正式 1.16.2 包已整体破坏 ABI”：同一日志第 23–29 行显示正式 1.16.2 zip 使用旧编译 1.12 consumer 的正式 DLL 验证通过。

源码：[SurfaceDumper.cs:126](D:/workespace/ws-game-artifacts/audit-4faab73-frozen/toolchain/abi_surface/SurfaceDumper.cs:126) 的属性签名只编码 `prop.Name:PropertyType`，不编码 `PropertyInfo.GetIndexParameters()`；文档覆盖声明见 `toolchain/README.md:555`。

责任：ABI surface dumper/compare 与其覆盖范围文档。

修复验收：dump 编码索引参数，compare 对 int→string 变化报告 break；保留旧 consumer 独立 smoke；正式 zip/lock 哈希、四包版本和现有兼容 consumer 结果继续单独记录。

## P3 文档问题

| ID | 触发/原文 | 当前事实 | 修复验收与责任 |
|---|---|---|---|
| DOC-162-01 | `core/foundation/data_registry/README.md:46-54` 与 `DataRegistry.cs:183-186` 将跨根重复写成无条件阻断。 | `DataRegistry.cs:623-665` 在 `AllowOverride` 且后层 `override:true` 时整行替换，前层 `final:true` 才阻断；`data/README.md:96-113` 已准确描述。 | 更新两处模块文档/XML 注释；与 data README 和代码条件一致。框架文档责任。 |
| DOC-162-02 | `architecture/落地计划/落地方案与分阶段计划.md:260` 写三个 `.tgz`。 | 根 README `:68-71,213-217`、formal 输出 `run5-console.log:17-20` 均为四包。 | 计划改为四包并同步附件清单。发布计划文档责任。 |
| DOC-162-03 | 计划 `:1368` 写 nested teleport 仅数组。 | `core/foundation/scene_router/core/WorldMapSchema.cs:60-79,92-99` 已登记共享 `{id?,position?}` item；`TeleportTargetResolver` 消费命名点。跨表目标完整性仍不由 schema 保证。 | 改写为“元素结构已登记，引用目标/业务必填另按边界处理”。计划文档责任。 |
| DOC-162-04 | 计划 `:1363` 将 ATB 列为明确非目标。 | `TimeModelSchema.cs:23` 接受 atb，`TurnScheduler.cs:77-81` 抛 NotSupported；ADR-0013 是本版不展开的延期预留。 | 改为延期/预留；不提出 ATB 算法修复。架构计划责任。 |
| DOC-162-05 | `architecture/04_数据与内容管线.md:362-363` 将 Number/Int 非有限统一写成 `field_finite`。 | Int 分支 `DataRegistry.cs:1004-1012` 失败报告 `field_type`；Number 分支 `:1015-1030` 才报告 `field_finite`。V-03 的 2^63 行为另列 P2。 | 明确诊断名范围，不用文档修订替代 V-03 修复。框架文档责任。 |

ABI surface “全部公开 API”覆盖声明见上方 ABI-1162-01；与该 P2 共用根因和修复，不重复计 P3。

## 旧问题关闭

旧审计 DOC-116-01～04、TOOL-116-01 当前均有对应修订：ADR 索引已为 23 条；`FindUnits` 已写明默认空间接线与 null 降级；DataRegistry README 已列 IdList 引用、`field_range`、`field_finite` 与 Expr 检查；ADR-0022 已区分元数据导出与校验 JSON；根 README 已说明 `check.ps1` 对 tracked 文件无改写但会写 ignored `bin/`/`dist/` 中间物。

| 旧项 | 输入与预期 | 当前结果与证据 |
|---|---|---|
| F-01 真实光环非有限值 | 同一 JSON fixture 的 `duration=1e309`、`effects[0].params.interval=1e309` 应报错并阻断读取 | 该 fixture 的 `duration` 先触发 `errors=1 blocking=True`、`+Infinity` 定位和读屏障；未将 `interval` 单独隔离验证；见 [validation.md](validation/validation.md) 与成功 raw。 |
| F-02 FieldRange 非有限端点 | NaN、`+Infinity`、`-Infinity` 端点应拒绝，`Contains(NaN)` 应为 false | validation 成功 raw 139–142 行直接 API 探针显示三个端点均 `ArgumentException`，`Contains(NaN)=False`；不归入 Python `176 passed`。 |
| F-03 Proc 表达式整数溢出与 Reload | `condition=9223372036854775808` 应报 `expr_parsable`、阻断；修复为 `true` 后应恢复 | 溢出报错并阻断，坏值 Reload 后恢复 `errors=0 blocking=False count=1`；见 [validation.md](validation/validation.md)。 |
| visibility 旧项 | public→protected 应由 surface/consumer E2E 捕获 | `test_abi_surface_compare.py:458` 覆盖，包含在 toolchain `176 passed`；不与 indexer P2 混计。 |

ADR-0023 已闭环：`stack_category` 只作加载期静态分组，运行时槽位为 `(target, aura_def, sourceKey)`。跨 aura 定义共享槽位是未提供能力，不是 AuraHost 缺陷。

## 能力边界与责任

| 分类 | 能力 | 当前结论 |
|---|---|---|
| 游戏责任 | talent 完整 allocator | 无既定通用分配器契约，技能树/点数策略由游戏实现。 |
| 条件支持/上层消费 | TargetPoint | cast 意图可携带落点；`SkillHost.CastSkill` 不消费，游戏/AI/辅助施法适配层自行转换目标；与 teleport_points 不同。 |
| 条件支持/上层消费 | `world.map.teleport_points` | 元素结构已登记；命名点由 `TeleportTargetResolver` 解析；引用目标完整性不由 schema 保证。 |
| 条件支持 | 离散召唤/loot | `SummonTickHandler` 在 Discrete 跳过且 duration 不推进；`LootExpiryTickHandler` 跳过清理但底层绝对时钟仍推进；扩大行为需另立 ADR。 |
| 未默认接线 | weaponVfx | Resolver 已构造暴露，`ResolveSwingVfx`/`ResolveImpactVfxOverride` 无默认生产调用，调用方接线。 |
| 条件支持/调用方责任 | gatherClock | `GobjOptions.SimTime` 默认恒 0，需注入模拟时钟；刷新机制已存在。 |
| 未默认接线 | Replay、Quest.Update、owner/day/vendor | API/实现存在，生产根默认不驱动/传 null，游戏选择入口和时机。 |
| 未默认接线 | DisplayMapCoverageRule、FeedbackRuleValidator、SpawnSummonOnlyCreatureRule | 本体存在，需要显式登记、调用或查询注入；delivery 输出记录 optional rules disabled。 |
| 延期 | ATB | 合法枚举与预留位存在，TurnScheduler 明确不支持；ADR-0013 本版不展开。 |
| 明确非目标 | day_cycle | 计划与规则宿主明确限定；不构成框架待办。 |
| 条件支持/实现方边界 | 导航跨帧预算、空间完整索引化 | 02 将性能要求收窄为实现方边界；具体适配器实现不代表 Core 契约承诺。 |
| 建议项/未承诺为本版门禁 | 孤儿记录检测 | 04 标为建议，不是当前门禁承诺。 |
| 未提供 | 跨 aura 定义共享槽位 | ADR-0023 已明确不提供，未来需求另立 ADR。 |

`prog.xp_source.condition` 只登记 Expr 类型、不求值；由宿主/游戏在授予前判断，或未来另行扩展。

## 验证与发布证据

- [check.log](delivery/output/raw/check.log) 第 64–107 行：六工程 .NET 测试共 3045 passed；第 149–153 行 Python toolchain 176 passed/4 skipped；第 252–280 行 24 步门禁 17 PASS/7 SKIP。ABI 探针因基线缺失在该受限门禁中显式 SKIP，不记 PASS。
- [run5-console.log](delivery/run5-console.log) 第 1–22 行：正式 `D:\workespace\ws-game\dist\ws-game-1.16.2.zip` SHA256 为 `544bedaf54bf6c97cd0ad7b5afd1b51f0e210c165ee756efcb2c34319683f454`；lock SHA256 为 `7a1f8f5a01e928d9262632e0c2c7b86f5af9a2cbb4305bc0f4b6da8af483b453`，版本和 git commit 匹配；六核心 DLL、Adapters.Stub、Validator、四包 manifest、manifest hash 均 PASS。
- 同一 formal 输出第 23–29 行：旧编译 1.12 consumer 使用正式新 DLL 运行通过且 consumer hash 不变。第 30–39 行为 indexer 负 oracle 与 surface 漏报证据。
- [validation.md](validation/validation.md) 与 [presentation-consumer-rerun.log](validation/raw/presentation-consumer-rerun.log) 覆盖 F-01/F-02/F-03 关闭和 V-01/V-02/V-03 定向复现；验证脚本使用冻结根镜像构建，冻结根保持只读。
- Unity EditMode [full-edit.xml](delivery/output/unity-evidence2/full-edit.xml) 文件头记录 70/70 Passed；当前 [full-play.xml](delivery/output/unity-evidence2/full-play.xml) 文件头记录 272/272 Passed，两个 Unity 运行日志 exit=0。该结果针对当前 check DLL 的 Unity 专项，不等同于正式 zip 直接消费认证。
- 本阶段没有独立版构建、IL2CPP、Standalone smoke 或独立 Unity 消费方完成证据；`-SkipUnity` 是部分门禁，不能泛化为完整 Runtime 发布认证。Unity PlayMode 结果已按当前 check DLL 证据记录，不能替代上述覆盖。

## 覆盖与未读范围

已读 architecture 00–14（15 文件/5076 行）、ADR 0001–0023（23 文件/1046 行）、指定模块 README 共 98 个/13589 行，并定点核对生产装配根、核心 API、适配器、模板、工具链、正式包和本轮 validation/delivery 证据。相对 1.16.1 基线，本冻结提交含 35 个非落地计划路径变化，其中包括核心修复，不能把 1.16.2 视为只改版本号。另见 [doc-code-matrix.md](docs/doc-code-matrix.md)。

这是风险导向深入抽查与既有矩阵增量核对，不是逐行全仓审计。本轮覆盖未确认“完全缺失且已承诺”的通用 API。未读范围包括非目标模块的全部实现细节、构建缓存/Unity `Library`/二进制内部、外部消费方仓库源码；没有据此声称全 API 无缺失或全发布认证。

`check.ps1 -SkipUnity` 对 tracked 源码不改写，但会写 `.gitignore` 覆盖的 `bin/`、`dist/` 中间物；这是验证过程副作用。
