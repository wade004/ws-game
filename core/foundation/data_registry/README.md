# L0 基础层 · data_registry 数据注册表

职责：加载全部数据表（`data/<dataset>/<domain>/<table>.json`，见 `data/README.md`）、管理
schema 版本迁移、做第 5 节列出的引用完整性等校验、提供只读查询、加载完成/校验失败时发事件
（见 `01_分层与依赖.md` L0 模块表 `data_registry` 行、`04_数据与内容管线.md` 第 4、5 节）。
是 `04` 定义的"L0 基础层暴露给全架构的唯一数据入口"的落地实现。

依赖：只依赖 `core/foundation/common`（`Id`、`Vec2`、`common/json`）、
`core/foundation/engine_adapter`（`IFileSystem`，供 `FileSystemDataSource`）、
`core/foundation/event_bus`（`IEventBus`、`IEvent`）、`core/foundation/expr`
（`ExprNode`、`ExprParser`、`ExprValidator`、`ExprEvaluator`、`IExprSchema`、`IExprHost`）与
.NET 标准库；不引用 `adapters/` 下任何具体实现，不引用任何 L1 以上模块。无反射、无 LINQ
热路径、无线程（呼应 `common/README.md` 的确定性约束）。

## 目录

```
data_registry/
  README.md
  contracts/   FieldKind.cs FieldSchema.cs VariantSchema.cs TableSchema.cs（含 TableMigration/MigrateDelegate）
               IDataSource.cs（DataTableSource/TextProvider）DataRecord.cs DataFieldException.cs
               ValidationReport.cs（ValidationSeverity/ValidationIssue）IValidationRule.cs
               IDataRegistry.cs（IDataRegistryView/IDataRegistry）DataRegistryOptions.cs Events.cs
  core/        DataRegistry.cs BuiltinSchemas.cs InMemoryDataSource.cs FileSystemDataSource.cs
               RecordExprHost.cs RecordExprSchema.cs
  schema/      README.md（schema_registry 元表说明、信封/主键规则、判断记录）
  tests/       DataRegistryTests.cs SubstructureValidationTests.cs（ADR-0019 子结构递归校验）
```

## 加载流程（`LoadAll`）

读取每张表文本 → JSON 解析（`common/json` 的 `JsonReader`）→ 信封检查（`table`/
`schema_version`/`rows`，`table` 必须等于文件名，见 `data/README.md`）→ 版本迁移（低于
`TableSchema.CurrentSchemaVersion` 依次跑迁移链，缺环节报 `schema_version` 错误；高于则直接
报错）→ 逐行建 `DataRecord`（主键重复报 `primary_key` 错误）→ 全部表进内存后跑第 5 节字段级
校验（`required_field`/`field_type`/`reference_integrity`/`text_key_exists`/`expr_parsable`）
+ 已注册 `IValidationRule` → 发 `data.load_completed`（`PublishImmediate`，无论报告是否阻断）；
报告为阻断态（有 Error，或严格级别为 `WarningsBlock` 且有 Warning）时再发
`data.validation_failed`，且此后 `Get`/`GetAll`/`Query` 一律抛
`InvalidOperationException("数据校验未通过，禁止读取")`（见 `11_工程规范与测试.md` 第 4 节
"不做静默降级"）。

`Validate()` 在已加载数据上重跑一遍同样的校验（不重新读取/解析文件）；`Reload(table)`
仅重新读取单张表、替换内存态记录后重跑一次全量 `Validate`（引用完整性等检查天然跨表）。

## 多根加载（`LoadAll(IReadOnlyList<IDataSource>)`）

数据目录框架/游戏分层任务新增：同一次加载可以传入多个 `IDataSource`（典型场景是"框架级数据表
`data/_framework` + 具体游戏的 `data/<game>`"并列加载，见 `data/README.md`"多根加载与合并规则"）。
同一张表出现在多个根时按主键并集合并；跨根信封 `schema_version` 不一致判定为阻断错误（消息里点出
两个根各自的 `Location`），不受下述覆盖语义影响。跨根主键重复默认仍判定为阻断错误，但这不是无条件
的（勘误，2026-09-10，第十七方深度审核 DOC-162-01）：`DataRegistryOptions.AllowOverride` 为 `true`
（默认）时，后层行（合并顺序更靠后的根，典型用法是游戏根在框架根之后）可显式声明 `"override": true`
整行替换前层同主键行（不做字段级合并），记一条覆盖诊断（见 `GetOverrideDiagnostics`）、不产生校验
问题；前层行可显式声明 `"final": true` 拒绝被覆盖，此时仍判定为阻断错误。`override`/`final` 只在
多根合并且确实发生同主键跨根重复时生效，单根加载中这两个字段为真判定为 Warning 并忽略。完整条件
与示例见 `data/README.md`"多根加载与合并规则"、`core/foundation/data_registry/core/DataRegistry.cs`
类型级判断记录"覆盖语义"。`LoadAll()`（无参，构造函数传入的单一 `IDataSource`）行为完全不变，等价
于单元素多根调用。只读诊断 `GetTableSourceLocations(table)`（声明在 `IDataRegistry`，见该接口判断
记录）返回某表本次加载实际来自哪些根的 `Location` 列表，供上层核对"这张表到底是从哪个数据根读上来
的"，不参与任何校验判定。

## 校验项检查名（04 第 5 节，`ValidationIssue.Check` 固定取值）

| Check | 触发点 |
|---|---|
| `envelope` | 顶层 `table`/`schema_version`/`rows` 缺失或类型不符、`table` 与文件名不一致、行不是对象 |
| `schema_version` | `schema_version` 超过当前代码期望版本、或低于当前版本但缺迁移环节 |
| `primary_key` | 主键字段缺失/格式非法、内容表 id 的 domain 前缀与表名首段不符、主键重复 |
| `required_field` | 必填字段缺失（或值为 JSON `null`，视为未提供） |
| `field_type` | 字段 JSON 类型与声明的 `FieldKind` 不符，含 `Id`/`IdList` 格式、`Enum` 取值 |
| `reference_integrity` | `Reference` 字段（含 `ReferenceTable`/`ReferenceDomain`）与 `DeclareReference` 动态声明的引用目标不存在；ADR-0022 起同样覆盖登记了 `ReferenceTable`/`ReferenceDomain` 的 `IdList` 字段——按同款规则逐个校验其每个元素（元素路径形如 `field[2]`），不是只校验 `Id` 单值字段 |
| `field_range` | ADR-0021：字段登记了 `FieldSchema.Range`（数值下上界）时，`Number`/`Int` 字段的值超出该范围（`ValidateFieldRange`，见字段级校验小节判断记录——只在登记了范围时触发，未登记不检查） |
| `text_key_exists` | `TextKey` 字段的值在 `l10n.text` 表按 `DefaultLocale` 查不到；`l10n.text` 表未加载时降级为 Warning |
| `expr_parsable` | `Expr` 字段解析失败（Error）、`ExprValidator` 报出的 Error/Warning 级问题原样映射；`ExprSchema` 未配置时整体降级为一条 Warning，不解析 |
| `variant_discriminator` | ADR-0019：`Object` 字段登记了 `Variants` 时，判别字段缺失、非字符串、或取值不在 `Cases` 键集合内 |
| `substructure_depth` | ADR-0019：子结构递归深度超过 `MaxSubstructureDepth`（32），已停止对该子树继续校验（防御登记错误导致的无限递归，如把 `itemFactory` 误指向自身之外仍会成环的结构） |
| `unknown_subfield` | ADR-0019：已登记 `Fields`（或 `Variants` 命中分支的字段清单）之外出现的多余子字段；默认关闭（`DataRegistryOptions.UnknownSubfieldSeverity = None`），开启后为 Warning |
| `field_finite` | 第十八方深度审核 F-01：`Number` 字段的值是 NaN/±Infinity（不是有限浮点数）时报出；独立于该字段是否登记 `Range`——未登记上界的 `Range`（如 `Range(min: 0)`）本就放行 +Infinity，不能只靠 `field_range` 兜底。判断在类型检查之后、`field_range` 之前 |
| `expr_validation_error` | 第十八方深度审核 F-03：`Expr` 字段解析（`ExprParser.Parse`/`ExprLexer.Tokenize`）或静态校验（`ExprValidator.Validate`）内部抛出除 `ExprParseException` 之外的未预期异常时报出，Error 级、阻断；`ExprParseException` 仍归入既有 `expr_parsable`，不受影响 |

其余检查项（效果数上限、预算超标、叠加类别冲突、外形映射存在、外形类型字段组完整、循环引用
检测、孤儿记录检测、时间字段与时间模型一致）由各内容模块以 `IValidationRule` 注册，本模块
不实现任何具体业务规则。

## 复合字段子结构递归校验（ADR-0019）

`FieldSchema` 对 `Object`/`Array` 两种字段种类新增可选的 `Fields`/`Item`/`Variants`（见
`FieldSchema.cs`、`VariantSchema.cs`；04 第 3.2 节）；`DataRegistry.ValidateFieldValue` 按登记
递归展开，子层的 `required_field`/`field_type`/`reference_integrity`/`text_key_exists`/
`expr_parsable` 与顶层字段共用同一份实现（`fieldPath` 换成完整路径如
`effects[2].params.base_value`，不新增平行的检查名/实现）。

判断记录：

1. **惰性求值支持自引用**：`FieldSchema` 构造函数额外接受 `itemFactory: Func<FieldSchema>`/
   `variantsFactory: Func<VariantSchema>`，首次访问 `Item`/`Variants` 属性时求值并缓存——用于
   `skill.def.effects` 的 `projectile.params.on_hit_effects` 这类"元素结构复用自身"的自引用
   场景（构造某个静态只读字段的初始化表达式内部不能直接读取该字段自身，此时仍是默认值/未赋值，
   必须延迟到真正使用时才读取，见 `SkillSchemas.EffectsItemSchema` 判断记录）。
2. **递归深度上限**：`MaxSubstructureDepth = 32`，超过报 `substructure_depth` 并停止对该子树
   继续递归（其余字段/记录不受影响），纯粹是防御登记错误导致的无限递归，不是对正常内容深度的
   限制（正常内容的嵌套深度由数据本身决定，远达不到这个上限）。
3. **未登记子结构行为不变**：`Object` 字段不设 `Fields`/`Variants`、`Array` 字段不设 `Item` 时，
   只检查"存在且类型匹配"，与登记子结构之前完全一致（向后兼容，属 MINOR 变更）。
4. **`unknown_subfield` 默认关闭**：首批登记（F1a）未必覆盖某个复合字段的全部实际用到的子
   字段，默认不报多余子字段，避免登记不全时产生噪音；游戏层也因此可以在已登记的复合字段上
   自由扩展字段而不触发校验，与"新增内容不改代码"的既有立场一致。需要更严格审查时可将
   `DataRegistryOptions.UnknownSubfieldSeverity` 设为 `Warning`。
5. **子结构暂不进入 Query 宿主**：`RecordExprSchema`/`RecordExprHost`（`Query(table,
   predicateText)` 用到的 Expr 宿主）只暴露记录的顶层字段为 `self.<field>`，不递归展开已登记的
   子结构——`Query` 的谓词场景（按字段筛选内容表）目前没有"按嵌套子字段筛选"的实际需求，贸然
   展开会显著扩大 `RecordExprSchema.For` 的实现复杂度（嵌套路径怎么表达成 Expr 引用语法、数组
   元素怎么索引），本批不做，待有真实场景再评估。

## 主键规则

- 内容表：主键字段 `id`，值须符合 04 第 2.1 节 id 格式，且 domain 前缀（`Id.Domain`，即第一段）
  必须等于表名首段——除非 `TableSchema.IsRegistryTable` 为 true（跨 domain 登记表，见
  `data/README.md`"记录主键"，本模块内置的 `found.event_catalog`、`l10n.text` 两张）。
- 登记表：主键字段 `key`，同样须符合 id 格式，但不做 domain 前缀检查。
- 复合主键登记表（目前只有 `l10n.text`，`TableSchema.HasLocaleCompositeKey`）：
  `DataRecord.Key` 取 `"{key字段值}@{locale字段值}"`；`text_key_exists` 校验按同样规则
  拼接 `"{文本键}@{DefaultLocale}"` 去查 `l10n.text` 的 `ByKey` 索引。

## Query 的宿主上下文

`Query(table, ExprNode predicate)` 用 `RecordExprHost`，只暴露 `self` 分组：`self.<field>`
返回记录字段（按 `TableSchema` 声明的 `FieldKind` 转换；未登记字段退化为按原始 JSON 类型
转换）；记录缺该字段返回 `Bool false` 并记一条 Expr 求值期警告，不抛异常（见 04 第 4 节
"宿主上下文只暴露记录自身字段"）。便捷重载 `Query(table, string predicateText)` 用
`RecordExprSchema.For(schema)` 把该表字段登记成 `self.<field>` 的零参引用后再解析——要求
该表已 `RegisterSchema`。

## `BuiltinSchemas`

登记 L0 自有表：目前只剩 `found.event_catalog`。**判断记录**：`l10n.locale`/`l10n.text` 两张表
的 schema 已搬到 `core/foundation/localization/contracts/L10nSchemas.cs`（`L10nSchemas.Locale`/
`.Text`），不再登记在本类；`stat.definition` 属 L1，不在 `BuiltinSchemas` 内，由调用方（如测试、
L1 模块自身）临时构造 `TableSchema` 并 `RegisterSchema`。

## `IFileSystem.ListFiles` 依赖的实现级约定

`FileSystemDataSource` 依赖 `ListFiles(dirPath)` 返回"`dirPath` 之下递归全部文件、相对
`dirPath`、`/` 分隔、按序数排序"（`02_引擎适配层.md` 第 1.6 节未限定该细节，本仓库拍板见
`core/foundation/engine_adapter/README.md`"IFileSystem"一节）；`adapters/stub/StubFileSystem.cs`
已按此约定实现。

## 不负责什么

- 不实现任何具体业务校验规则（效果数上限、预算、叠加冲突等），只提供 `IValidationRule` 扩展点。
- 不做资源加载（图片/音频/字体），那是 `IResourceLoader` 与表现层的职责；`IFileSystem` 只用于
  读取数据表 JSON 文本本身。
- 不做 JSON 序列化之外的任何文件系统写操作（本模块只读数据表，不回写）。
- 不提供 Unity/`IResourceLoader.loadAsync(kind: data_table)` 到 `IDataSource` 的桥接实现——
  `FileSystemDataSource` 只依赖 `IFileSystem`，游戏层/适配层若经 `IResourceLoader` 异步加载
  数据表文本，需要自行适配出一个 `IDataSource`（如先用 `IResourceLoader` 把文本读进内存，
  再包 `InMemoryDataSource`）。
