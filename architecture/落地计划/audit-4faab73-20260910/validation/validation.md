# ws-game 1.16.2 有界验证记录

本记录只覆盖冻结基线 `4faab73e7081f2984e7addb88fee051b6c0d3d02`（`VERSION=1.16.2`）的 .NET 8 内容/框架路径验证，不是最终审核，也不包含整库 `check.ps1` 或 Unity。冻结源码 `D:\workespace\ws-game-artifacts\audit-4faab73-frozen` 全程只读；构建使用本目录的 `build\source` 镜像。成功 run 的完整原始输出在 `raw\presentation-consumer-rerun.log`，此前 fixture 调整过程保留在 `raw\presentation-consumer-attempt-1.log` 至 `attempt-5.log`，最终结论只引用成功 run。本轮主仓/冻结树、正式 ZIP/lock 与 Unity 进程快照见 `raw\final-baseline.txt`。

复现命令（脚本从自身 `$PSScriptRoot` 解析 consumer 工程与 source prepare；`OutputRoot` 可以是全新目录，不需要复制 `repro`）：

```powershell
$run = Join-Path D:\workespace\ws-game-artifacts\audit-4faab73-20260910 ("validation-rerun-" + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff'))
if (Test-Path -LiteralPath $run) { throw "OutputRoot already exists; choose a new timestamp/path: $run" }
& D:\workespace\ws-game-artifacts\audit-4faab73-20260910\validation\repro\run-presentation-consumer.ps1 `
  -FrozenRoot D:\workespace\ws-game-artifacts\audit-4faab73-frozen `
  -OutputRoot $run
```

已用同一脚本在独立的新 `OutputRoot=...\validation-rerun` 实测通过；再次运行必须选择不存在的新路径，避免 runner 的历史 raw 保护触发。脚本先校验冻结 HEAD/版本，再把 `core`、`presentation`、`toolchain`（排除 tests/bin/obj）复制到 `build\source`，在镜像上构建 consumer，最后把 allowlist 复制到 build 输出供 schema audit 使用。成功构建为 0 warning/0 error。

JSON_BUILD/证据 hash（SHA-256）：`build\pconsumer\PresentationConsumer.dll` 为 `8f6936d9b2096fced2eb0125dfe8f418b897b77e95c630b784e7ec32bbb2ba63`；使用的 `schema_audit_allowlist.json`（冻结源与 build 输出逐字节相同）为 `c3fdcf2adce8c54b6aa4dbeaeaa83f3f5056133357922aa52010961ebeba508e`；成功 raw `presentation-consumer-rerun.log` 为 `c55419c7f68abba9289a96f3c74ae1c81f404cabab48927a01a89768dd42ca08`。两次成功 run raw hash 相同。

## 原 F-01/F-02/F-03

| 条目 | input | expected | actual（`raw\presentation-consumer-rerun.log`） | 结论 |
|---|---|---|---|---|
| F-01 真实光环非有限值 | `skill.aura_def` 的同一 JSON 行中 `duration=1e309`、`effects[0].params.interval=1e309` | 合法 JSON 数值语法求值为非有限时拒绝；报告阻断；读屏障抛 `InvalidOperationException` | 126 行 `errors=1 blocking=True`；127 行为 `envelope` 且指出 `+Infinity`；128 行 `GetAll` 抛“数据校验未通过，禁止读取” | 已修复路径通过。解析在 `core/foundation/common/json/JsonReader.cs:383` 拒绝；`DataRegistry` 仍有绕过解析树的 `field_finite` 第二道防线（`core/foundation/data_registry/core/DataRegistry.cs:1028-1030`）。 |
| F-01 正常指数对照 | 真实 `skill.aura_def`：`duration=1e308`、`effects[0].params.interval=1e308`，其余字段合法 | 有限值接受，仍可读取，两个字段保持有限 | 132 行 `errors=0 blocking=False duration=1E+308 interval=1E+308` | 正常边界未被误伤。 |
| F-03 真实 Proc 整数溢出 | `skill.proc_def.condition="9223372036854775808"`，同时提供合法 `skill.def`/`target.chain_def` 控制记录 | `LoadAll` 不外抛；具体 `expr_parsable` Error；注册表阻断且读屏障 | 129-131 行：`errors=1 blocking=True`、`expr_parsable` 定位 `condition`/位置 0；读屏障抛 `InvalidOperationException` | 已修复路径通过。`ExprLexer.cs:224` 把 long 溢出转为带位置 `ExprParseException`；`DataRegistry.cs:1394-1422` 统一处理表达式校验异常。 |
| F-03 Reload 恢复契约 | 先 `condition=true` 加载，再 Reload 为超范围整数，再 Reload 回 `true` | 初始可读；坏 Reload 阻断和读屏障；修复同表 Reload 清除错误并恢复可读 | 133-137 行：初始 `errors=0 blocking=False count=1`；坏值 `errors=1 blocking=True` 且读屏障；修复后 `errors=0 blocking=False count=1` | 失败后恢复/同表 Reload 契约通过。 |
| F-03 表达式 double 溢出 | 公开 `ExprLexer.Tokenize(500位十进制+.1)` | 非有限表达式数字转为 `ExprParseException`，位置可见 | 138 行 `ExprParseException position=0` | 已修复同源边界通过（`ExprLexer.cs:213`）。 |
| F-02 Range API | `FieldRange.Range(min=NaN)`、`max=+Infinity`、`min=-Infinity`；`Range(0,10).Contains(NaN)` | 非有限端点抛 `ArgumentException`；NaN 值不属于任何范围 | 139-142 行三个端点均 `ArgumentException`，`Contains(NaN)=False` | 已修复。实现位于 `FieldRange.cs:64-69`、`:100`。 |

## 新增定向候选

| 候选 | input | expected | actual | 判断/修复验收准则 |
|---|---|---|---|---|
| V-03 `JsonNumber.TryGetInt64` 上界 | 直接 `new JsonNumber(9223372036854775808d)`；再通过 `TableMigration` 将它放入实际 `FieldKind.Int` 字段 | 对有限整数 double 输入，要求落在 `[-2^63, 2^63)` 才成功；2^63 不在 long 范围，`TryGetInt64=false`；DataRegistry 应报 `field_type` 并阻断 | 143-146 行：带原始文本的 `9223372036854775808` 对照为 false、long.MaxValue 对照为 true；但直接值 `actual=True value=-9223372036854775808`；迁移进入 Int 字段 `errors=0 blocking=False` | **候选缺陷确认**。`JsonValue.cs:108` 的 `Value <= long.MaxValue` 在 double 提升后把 2^63 当成可接受上界。修复验收：无 RawNumberText 的有限整数 double 仅在 `[-2^63, 2^63)` 内返回 true；同一迁移路径必须出现 `field_type` Error/阻断。raw text 的 long.MaxValue 精确接受对照保留。 |
| V-01 `schema_version` 截断屏障 | 最小合法 v1 表分别输入 `schema_version=1`、`2`、`4294967297` | 1 接受；2 和所有大于 Int32.MaxValue 的未来版本阻断，不应 wrap | 149 行 v1 接受；150 行 v2 正确阻断；151 行 `4294967297` 为 `errors=0 blocking=False` | **候选缺陷确认**。`DataRegistry.cs:737-744` 先读 long 后直接 `(int)svLong`，4294967297 wrap 成 1。修复验收：转换前显式检查 `svLong > int.MaxValue`（或使用无溢出整型策略）；大值报告 `schema_version` Error/阻断。 |
| V-02 Currency 存档精度 | 无 cap 的 `econ.currency`，`SetBalance=9007199254740993`（2^53+1），`CurrencyPersistable.Save → JsonWriter → JsonReader → CurrencyPersistable.Load` | long 余额应精确往返 | 152-154 行：写出文本为 `9007199254740992`，读回 `actual_balance=9007199254740992 exact=False` | **候选缺陷确认**，范围仅限该无 cap 大整数存档路径。`CurrencyPersistable.cs:44` 以 `new JsonNumber(balance)` 丢失 long 精度。普通修复验收：存档 JSON 保留精确整数原文/整数表示，往返仍为 9007199254740993；改窄为 safe double 范围属于需另行决定的破坏性迁移，不作为等价修复。未扩展为全存档扫描。 |

补充的非有限直接构造路径：通过迁移把 `new JsonNumber(double.PositiveInfinity)` 放入 `FieldKind.Number` 字段，147 行报告 `field_finite` Error 且阻断；这证明 F-01 的第二道防线有效。补充的未预期校验器异常路径：自定义 `IValidationRule` 在 `LoadAll` 抛 `InvalidOperationException`，148 行显示 Load 外抛但随后读操作仍抛“数据校验未通过”，对应 `DataRegistry.cs:347-366` 的 `completed/finally` 阻断兜底。

schema audit 的无 allowlist 诊断（开头 `SCHEMA_AUDIT` 的 20 项 `composite_without_substructure`）只是 consumer 中保留的空 allowlist 对照；同一 run 的 `SCHEMA_AUDIT_ALLOWLIST` 为 63 tables/783 fields、`errors=0 blocking=False`。它不参与上述 F/V 条目的判定。
