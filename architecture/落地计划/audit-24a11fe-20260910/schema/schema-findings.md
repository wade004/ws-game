# ws-game 1.16.1 schema / expression / metadata findings

审计输入固定为 `D:\workespace\ws-game-artifacts\audit-24a11fe-frozen`，HEAD
`24a11fe28f9647cd532c41f56f7ab18c00fb8516`，版本 `1.16.1`。本目录是本轮唯一输出目录。
tracked 源码未改动（冻结树 `git status --short` 为空）。准备阶段的早期构建曾在冻结目录
生成 ignored `bin/obj`；最终 runner 已改为先镜像源码到本输出目录，再从镜像构建，避免继续
写入冻结目录。当前 `build/` DLL 是独立副本。

## 结论

发现 2 项确认的 P2 schema/consumer 问题，以及 1 项相关的 P3 API 防御观察；另有 ADR-0022
Domain 与 TimeModelRules 的边界观察，当前不计为缺陷。

### F-01 [P2] JSON 合法指数溢出成为 Infinity，真实框架表的 Number 范围可放行

**触发与实际行为。** `JsonReader.ParseNumber` 在
`core/foundation/common/json/JsonReader.cs:319-381` 用 `double.Parse` 接收 JSON 合法的
`1e309`，得到 `Infinity`，没有有限值检查。`DataRegistry` 的 Number 路径
(`core/foundation/data_registry/core/DataRegistry.cs:981-1000`) 随后只执行
`FieldRange.Contains`。合成 `test.number` 加载 `value: 1e309` 时报告
`errors=0 blocking=False actual=+Infinity`；真实框架表 `skill.aura_def` 输入
`duration: 1e309` 与合法 `periodic_damage.params.interval: 1e309`，通过正式
`ContentValidationAssembly` 也报告 `errors=0 blocking=False`，并从已加载记录读出
`duration_is_infinity=True`、`interval_is_infinity=True`。坏数据因此能穿过正式 schema/data
加载路径进入已加载记录。
同一 consumer 的 `-1e309` 被范围下界挡住，报告 `-Infinity` 超范围；这不是有限性契约，
因为 `+Infinity` 已经展示了可通过路径。直接 `JsonReader.Parse("1e309")` 也确认
`JsonNumber` 的值是 Infinity。`JsonWriter.Write` 可输出 `1e309`，所以读入后内存语义已
不是有限 JSON 数值的通常数值语义，形成 load/consumer 边界不一致。

**建议修复方向。** 在 JSON number 解析后拒绝 `!double.IsFinite(value)`，或在 DataRegistry
Number 验证入口统一拒绝非有限值；两者至少要有一个正式入口保障。应保留 exponent 语法，
只拒绝溢出结果。证据：`raw/presentation-consumer-rerun.log` 的
`REAL_FRAMEWORK_OVERFLOW`/`REAL_FRAMEWORK_VALUE` 行，以及 `raw/schema-consumer.log` 的
`JSON_NUMBER raw=1e309`、`JSON_READER_OVERFLOW`、`JSON_WRITER_OVERFLOW` 行。

### F-02 [P3] FieldRange 接受非有限边界，Contains(NaN) 反常为 true

`core/foundation/data_registry/contracts/FieldRange.cs:42-116` 的构造只检查 null、大小及
相等端点，没有排除 NaN/Infinity。独立 consumer 通过公开 API 构造：

* `Range(min: NaN)` 成功，描述 `[NaN, +∞)`，`Contains(0)=true`；
* `Range(max: NaN)` 成功，描述 `(-∞, NaN]`，`Contains(0)=true`；
* `Range(min: +Infinity)` 也成功。

这是公开 schema 元数据的有限值不变量缺口，属于 F-01 的 API 防御观察；它单独通过程序传入
无效 schema 参数触发，严重度不高于 F-01。F-01 进一步证明非有限 Number 能由正式 JSON
路径进入 DataRegistry；两项可分别修复，不能只依赖调用方不传无效 API 参数。

### F-03 [P2] Expr 字段整数溢出逃出正式内容校验，且公共 Tokenize 异常不符合词法错误契约

`core/foundation/expr/core/ExprLexer.cs:101-108` 暴露 `Tokenize`；同一实现由
`core/foundation/expr/core/ExprParser.cs:30-33` 调用，普通非法字符/未闭合字符串在 lexer
和 parser 均产生同位置 `ExprParseException`。但
`ExprLexer.cs:205-215` 对整数 token 直接 `long.Parse`：输入
`9223372036854775808` 从公开 `Tokenize` 抛出 `OverflowException`，没有 `Position`，而不是
统一的 `ExprParseException`。更强的正式路径证据是：独立 consumer 通过
`ContentValidationAssembly.CreateRegistry` 注册真实 `skill.proc_def.condition` Expr 字段，
输入该表达式后 `LoadAll()` 直接抛 `OverflowException`；随后对同一 registry 调
`GetAll("skill.proc_def")` 仍能读出 `count=1`，说明异常逃出 `DataRegistry.ValidateExprField`
（`DataRegistry.cs:1362` 只捕获 `ExprParseException`），且可能留下可读的部分加载状态。
这表明合法 JSON 中的非法超范围 Expr 内容没有形成预期的阻断诊断，而是以
`OverflowException` 外逃；属于框架内容校验契约问题，与具体游戏功能需求无关。

同一 probe 的字符串 token span 也记录了当前有效契约：`event.value` 为 `start=2 length=11`，
带转义字符串为 `start=17 length=6`（原文引号/反斜杠均计入 span），EOF 为 `start=39 length=0`；
parser 成功复用 lexer 结果，普通 lexer/parser 错误位置一致。500 位十进制 Number token
则返回 `Infinity`，这与 F-01 同属非有限数值边界，建议统一 finite policy，但 lexer 溢出
异常类型仍单列为 ADR-0020 consumer 契约问题。

## 不计缺陷的边界观察

### ADR-0022 Domain：当前是元数据/导航声明，不是 ID 命名空间重写

`TableSchema.Domain` 的文档（`core/foundation/data_registry/contracts/TableSchema.cs:75-109`）
和 ADR-0022 只允许 `camera_profile`、`ui_layout_definition`、`shell_menu_definition` 三个
单段表名用 `WithDomain` 显式描述域；现有 `camera_profile` 合法 ID 仍是
`camera_profile.default`。运行时主键检查在 `DataRegistry.cs:792-824` 使用表名首段，
ReferenceDomain 检查在 `DataRegistry.cs:1225-1315,1404-1417` 使用实际 ID 的首段。
独立构造 `camera_profile.WithDomain("camera")` 并令 `ReferenceDomain("camera")` 指向
`camera_profile.default`，得到 1 个 `reference_integrity` 错误，说明当前契约不会把显式
Domain 当作 ID 前缀。这个结果与 ADR-0022 的“表名保持不变”及现有样本一致，因此是清晰的
边界记录，不计“工具不得猜前缀”缺陷；只有未来决策把 Domain 改为运行时命名空间时才需改
主键/引用实现并迁移数据。

### TimeModelRules 与 TimeFieldConsistencyRule：存在两层登记，但本轮未找到具体不一致

`core/foundation/sim_loop/schema/TimeModelRules.cs:7-22` 公开读取 `TableSchema.TimeScope`
和 `FieldSchema.Unit`；实际整数校验仍由各装配 catalog 构造
`TimeFieldDeclaration`（`core/rules/assembly/RulesSchemaCatalog.cs:202-269`、
`core/gameplay/assembly/GameplaySchemaCatalog.cs:204-213`）后交给
`TimeFieldConsistencyRule.cs:93-174`。逐项核对生产 Unit=Time 字段与声明：
`skill.def` 的 cast/channel/cooldown/charges.recharge_time、`skill.aura_def.duration`、
`skill.aura_def.effects[].params.interval/tick_interval`、`skill.proc_def.internal_cooldown`、
`arch.power_type` 三个 regen/decay 字段、`spawn.table.respawn_timer` 均有对应声明；
`arch.power_type` 的 `TimeScope.Both` 与 combat/exploration 两个 declaration scope 相符，
其余表级 scope 也相符。`SchemaAudit` 对 Unit=Time 且 TimeScope=None 会报错
(`presentation/assembly/SchemaAudit.cs:483-489`)，allowlist 运行结果无此类问题。
这是双来源/实现组织观察，未发现某个具体 table/field 在 metadata 与运行规则间发生不一致，
故不计缺陷。需要未来防漂移时，可让 catalog 声明从 schema 元数据生成，但不作为本轮修复项。

## 其他验证结论

* `SchemaAudit` 对冻结注册表 63 tables / 783 fields：空 allowlist 为 20 个预期的 Map/opaque
  composite 问题；使用冻结 `toolchain/schema_audit_allowlist.json` 后 `errors=0 warnings=0 blocking=False`。
* `SchemaFieldRangeExport` 对合法 schema 可导出数值范围；对故意把 Range 绑到 String 的 schema
  报 `field_range_kind=1` 且 export_count=0，未出现把非法范围静默导出的情况。
* validator sample consumer：56 tables / 167 records / 0 errors / 1 warning（单根 override
  提示）；JSON `tables_list` 含 owner、domain、time_scope、field_meta、field_ranges。
* `DataRegistry.RecordCount` concrete override 在阻断结果下仍可取得加载记录计数；默认
  `IDataRegistryView.RecordCount` 仍按 `GetAll` 可能抛阻断异常，`TryGetRecordCount` 是明确的
  非抛出承诺。该差异符合当前接口文档，没有另报缺陷。
* `ViewBinder` 相对 v1.14.0 的当前改动仅是旧 7 参数构造器到新 8 参数构造器的 ABI facade；
  1.14 ViewBinder tests 未被删除或失配，静态上仍相关。本轮没有把旧 Unity 数字当作当前证据。

## 可复核入口

命令、输入输出和 SHA-256 见 `repro/README.md`；原始 stdout/build/validator 文件在 `raw/`。
