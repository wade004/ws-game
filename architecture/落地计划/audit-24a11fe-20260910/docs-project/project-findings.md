# 当前工程 findings

本表只列当前冻结副本可复现、且属于文档或工具链合同的问题。P 级别表示审计整改优先级，不把游戏层选择或缺少 Unity 运行证据升级成框架缺陷。

| ID | 级别 | 位置 | 证据与判断 | 建议责任 |
|---|---|---|---|---|
| DOC-116-01 | P3 文档 | `architecture/README.md:83-92`；根 `README.md:16` | ADR 目录实际已有 0001–0022，但架构索引只列到 0019、根 README 仍称 19 条；ADR-0020/21/22 已存在且被落地计划引用 | 更新两处索引/计数；不改代码 |
| DOC-116-02 | P2 文档 | `core/rules/skill/README.md:80-86` | 文案仍称 `ISkillHost.FindUnits` 恒返回空、`ISpatialQuery` 未接线；`SkillHost.cs:274+` 在非 null query 时执行查询、过滤和排序，`RulesAssembly.cs:359-360` 默认传入 `Spatial` | 更新为“默认装配已接线；仅 null query 降级为空并记诊断” |
| DOC-116-03 | P3 文档 | `core/foundation/data_registry/README.md:65` | `reference_integrity` 表只写 `ReferenceTable/ReferenceDomain` 与 `DeclareReference`，漏记 ADR-0022 的 `IdList` 元素引用；检查项表也没有 ADR-0021 的 `field_range` 条目。代码和 validator 已具备这些元数据/校验能力 | 补充检查项说明；不要求 archetype README 改写已准确的 `DeclareReference` 分层解释 |
| DOC-116-04 | P3 文档 | `architecture/adr/0022-登记表补充导航与编辑元数据.md:68` | `validator --list-tables --json` 输出含 `tables_list`、`field_meta`；`validate_data.py --json` argparse 只支持 `--json`，wrapper 仅将 `--json` 传给 validator。wrapper JSON 顶层为 `skeleton/validator/exit_code`，没有 `tables_list` | 改写为区分两条命令的准确说明；不要求 wrapper 新增 `--list-tables` |
| TOOL-116-01 | P3 工程文档 | `README.md:119` 与 `check.ps1 -SkipUnity` 实测 | check transcript 的门禁成功，但 `build.ps1 -SkipTests` 步骤在冻结副本生成/更新 ignored `dist/1.16.1` 发布中间物；这与“只读”对工作树 tracked 文件成立，但不等于无副作用 | 文档说明 ignored release artifacts 例外，或把步骤副作用单独标注；本轮不清理/不改产品 |
| ABI-116-01 | P2 工具链 | `toolchain/abi_surface/SurfaceDumper.cs:104-110,133-151` | 独立最小 oracle 将 public `Api.Read()` 改为 protected：baseline/current surface dump 相同，compare `breaks=0 RESULT=OK`；旧 consumer 替换 DLL 后运行 `MethodAccessException`。说明 surface gate 未记录方法/字段/event visibility，不能单独证明 ABI | 增加 visibility 变化覆盖或与旧 consumer smoke 绑定；本轮不改工具实现 |
| F-01 | P2 框架数据合同 | `JsonReader.cs:319-381`、`DataRegistry.cs:981-1000`；真实 `skill.aura_def` | 合法 JSON `1e309` 读为 `Infinity`，真实 `ContentValidationAssembly` 报 `errors=0 blocking=False`，记录仍可读出 Infinity；这是 .NET 8 内容工具/框架加载路径证据，不是 Unity 崩溃断言 | JSON/DataRegistry 入口至少一处拒绝非有限 Number，保留正常 exponent |
| F-03 | P2 框架表达式合同 | `ExprLexer.cs:205-215`、`DataRegistry.cs:1362`；真实 `skill.proc_def.condition` | `9223372036854775808` 经公开 Tokenize 与真实 `LoadAll` 外抛 `OverflowException`；随后 `GetAll` 仍可读 count=1，说明异常逃出预期 Expr 诊断并留下部分加载状态 | 将整数溢出归一为带位置的 `ExprParseException`，并保证 LoadAll 的阻断/状态语义 |
| F-02 | P3 API 防御观察 | `FieldRange.cs:42-116` | 公开 Range API 接受 NaN/Infinity 边界，`Contains(0)` 可反常为 true；属于 F-01 相关 API 防御，不扩大为独立高优先级能力缺失 | 增加有限值不变量或统一 finite policy |

## 已核实边界（不列为 finding）

- ATB 是 ADR-0013 与落地计划明确的预留延期项；本版不执行，保留，不展开为能力缺陷。
- `PresentationAssembly` 调用未注入 `NewGameStarter` 时抛出，是游戏/宿主负责提供起始状态的合同结果。
- `NotSupportedException` 扫描命中的是显式降级/未绑定占位合同，不能作为能力缺口计数。
- `CarriersAssembly.cs:89-103` 明确默认不登记 `gobj`；游戏若把 `gobj` 放进自定义共用空间索引，宿主负责过滤后再交给 `FindUnits`/nearest 查询。当前没有证据支持“默认生产装配崩溃”或新增 P2。
- talent allocator、escort route、轨迹碰撞和具体游戏内容都属于游戏责任/选配机制；框架与游戏解耦要求保持。

## 已对齐与未提供证据

已对齐：00–14 架构正文与当前模块分层、ADR-0020/21/22 的代码入口、默认规则装配、正式包 lock/hash 关系、数据及工具链静态门禁。

未提供但不构成缺陷：本任务没有再次运行 Unity、独立版 smoke 或消费方演练；schema agent 的独立 Unity 证据应由总报告引用，不在此重复执行。
