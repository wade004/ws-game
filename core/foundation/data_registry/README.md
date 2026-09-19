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
  contracts/   FieldKind.cs FieldSchema.cs VariantSchema.cs CurveSchema.cs（含 CurveAxis/CurveShape）
               TableSchema.cs（含 TableMigration/MigrateDelegate）
               IDataSource.cs（DataTableSource/TextProvider）DataRecord.cs DataFieldException.cs
               ValidationReport.cs（ValidationSeverity/ValidationIssue）IValidationRule.cs
               IDataRegistry.cs（IDataRegistryView/IDataRegistry）DataRegistryOptions.cs Events.cs
  core/        DataRegistry.cs BuiltinSchemas.cs InMemoryDataSource.cs FileSystemDataSource.cs
               RecordExprHost.cs RecordExprSchema.cs CurveMonotonicFiniteRule.cs（T-N0-3）
  schema/      README.md（schema_registry 元表说明、信封/主键规则、判断记录）
  tests/       DataRegistryTests.cs SubstructureValidationTests.cs（ADR-0019 子结构递归校验）
               CurveSchemaTests.cs（T-N0-1 曲线形态登记）CurveMonotonicFiniteRuleTests.cs（T-N0-3）
               ValidationRuleMetadataTests.cs（T-N0-2）
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
| `field_type` | 字段 JSON 类型与声明的 `FieldKind` 不符（含 `Enum` 取值不在合法集合内）；`Id`/`Reference`/`IdList` 逐元素/`Map` 值种类为 `Id`/`Reference` 时的值，取值不是 JSON 字符串归本项——值确是字符串但不满足 Id 语法改报下一行 `field_id_format`（消费方反馈第 47 条改动前两者混用本检查名，见该行判断记录） |
| `field_id_format` | 消费方反馈第 47 条：`Id`/`Reference` 字段（含子结构内递归复用同一实现登记为这两种种类的子字段）与 `IdList` 逐元素、`Map` 值种类为 `Id`/`Reference` 时的值——取值是字符串但不满足 `Id.IsValidFormat`（全小写点分领域.名称）时报出，阻断级；与上一行 `field_type` 互斥，同一处至多命中其中一种。**判断记录**：此前这类值也报 `field_type`（笼统覆盖"类型不符"与"格式非法"两种情形），规则解析入口（如 `SkillDefCache.ParseSkillDef` 内部 `DataRecord.GetId`）对同一非法值再次解析时抛出的是 `DataFieldException`，而依赖该入口的规则（如 `SkillBudgetValidationRule`）原先只 `catch (ArgumentException)`，未被拦下、一路冒出中断整批 `LoadAll`——本检查名让这类坏形状在字段级就先被拦下并产出可定位诊断，配合规则侧同批把 `catch` 扩大为同时覆盖 `ArgumentException`/`DataFieldException`（且不再彻底静默，改产出规则自己的 Warning 诊断，见 `core/rules/skill/README.md`/`core/carriers/item/README.md`"判断记录（消费方反馈第 47 条根治）"），双层根治：字段级先拦、规则级兜底不中断批处理 |
| `reference_integrity` | `Reference` 字段（含 `ReferenceTable`/`ReferenceDomain`）与 `DeclareReference` 动态声明的引用目标不存在；ADR-0022 起同样覆盖登记了 `ReferenceTable`/`ReferenceDomain` 的 `IdList` 字段——按同款规则逐个校验其每个元素（元素路径形如 `field[2]`），不是只校验 `Id` 单值字段 |
| `field_range` | ADR-0021：字段登记了 `FieldSchema.Range`（数值下上界）时，`Number`/`Int` 字段的值超出该范围（`ValidateFieldRange`，见字段级校验小节判断记录——只在登记了范围时触发，未登记不检查） |
| `text_key_exists` | `TextKey` 字段的值在 `l10n.text` 表按 `DefaultLocale` 查不到；`l10n.text` 表未加载时降级为 Warning |
| `expr_parsable` | `Expr` 字段解析失败（Error）、`ExprValidator` 报出的 Error/Warning 级问题原样映射；`ExprSchema` 未配置时整体降级为一条 Warning，不解析 |
| `variant_discriminator` | ADR-0019：`Object` 字段登记了 `Variants` 时，判别字段缺失、非字符串、或取值不在 `Cases` 键集合内 |
| `substructure_depth` | ADR-0019：子结构递归深度超过 `MaxSubstructureDepth`（32），已停止对该子树继续校验（防御登记错误导致的无限递归，如把 `itemFactory` 误指向自身之外仍会成环的结构） |
| `unknown_subfield` | ADR-0019：已登记 `Fields`（或 `Variants` 命中分支的字段清单）之外出现的多余子字段；默认关闭（`DataRegistryOptions.UnknownSubfieldSeverity = None`），开启后为 Warning |
| `field_finite` | 第十八方深度审核 F-01：`Number` 字段的值是 NaN/±Infinity（不是有限浮点数）时报出；独立于该字段是否登记 `Range`——未登记上界的 `Range`（如 `Range(min: 0)`）本就放行 +Infinity，不能只靠 `field_range` 兜底。判断在类型检查之后、`field_range` 之前 |
| `curve_monotonic_finite` | 分阶段落地计划 T-N0-3（04 第 5 节数值类校验项分级表"曲线单调有限"）：`CurveMonotonicFiniteRule`——本模块唯一自带的 `IValidationRule`（框架级、不认具体表名，由装配根 `PresentationSchemaCatalog.RegisterAll` 注册）；对全部登记为断点表形态（`CurveSchema`）的字段逐条记录检查：断点非空、逐值有限、`x` 严格递增无重复、`y` 不递减（允许平台段），任一不满足报 Error，问题定位到曲线字段完整路径（含对象子字段/数组元素/映射值/变体分支）；元素形态不符（已由 `required_field`/`field_type` 报过）跳过不重复报；与 `field_finite` 层次不同不合并 |
| `expr_validation_error` | 第十八方深度审核 F-03：`Expr` 字段解析（`ExprParser.Parse`/`ExprLexer.Tokenize`）或静态校验（`ExprValidator.Validate`）内部抛出除 `ExprParseException` 之外的未预期异常时报出，Error 级、阻断；`ExprParseException` 仍归入既有 `expr_parsable`，不受影响 |
| `field_item_count` | 消费方反馈第 60 条：字段登记了 `FieldSchema.MinItems`/`MaxItems`（`FieldSchema.WithItemCount`，仅 `IdList`/`Array` 可登记）时，元素数不落在区间内报出（`ValidateFieldItemCount`，与元素结构 `Item` 是否登记正交，独立检查；未登记不检查，同 `field_range` 惯例）；消息含实际元素数与允许区间 |

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

## 曲线形态登记（分阶段落地计划 T-N0-1，04 第 3.6 节）

数值设计要求全部"横轴为等级/物品等级/数值"的曲线表共用一种登记形态与一份插值实现（数值总纲第 3 节
原则 1、落地清单 2.1 C1/C2）。`CurveSchema.cs` 提供：

- `FieldSchema.WithCurve(CurveSchema)`/`FieldSchema.Curve`：把"该字段是一条曲线、横轴是什么"作为
  元数据挂到字段上；不新增 `FieldKind`，曲线仍是 `Array`（断点表）或 `Object`（饱和）字段，子结构
  沿 ADR-0019 登记，加载期必填/类型/范围检查全部复用既有检查项（不新增平行检查名）。
- 两种形态：`CurveShape.Breakpoints`（一元断点表，元素固定为 `{x, y}`，横轴语义由 `CurveAxis`
  声明——`Level`/`ItemLevel` 的 `x` 登记为 `Int`，`Value` 的 `x` 为 `Number`）与
  `CurveShape.Saturation`（二元饱和 `{k, cap}`，输入为数值 × 等级；本模块只登记形态与参数字段，
  不提供求值，既有 `combat.resist_curve` 的 `saturation` 分支公式保持原样）。
- 工厂 `CurveSchema.BreakpointsField(...)`/`SaturationField(...)` 生成标准子结构；手工拼装的登记若
  形态与种类/子结构不符，由 `SchemaAudit` 的 `field_curve_shape` 检查项事后报告（同 `WithRange`/
  `WithMap`"挂载时不检查、门禁事后报告"的既有风格）。
- 解析入口 `CurveSchema.ReadBreakpoints(DataRecord, fieldName)`（形态不符抛 `DataFieldException`，
  路径定位到 `字段[下标]`）与 `TryReadBreakpoints(JsonArray, ...)`（供校验规则使用，不抛异常），
  返回 `Core.Foundation.Common.PiecewiseCurve`——解析放在本模块而不是 `common`，因为依赖方向是
  `data_registry → common`。

既有曲线表（`item.budget_curve`/`stat.rating_conversion`/`combat.resist_curve` 的 `table` 分支）迁移到
本形态与通用单调有限规则 `curve_monotonic_finite` 分别是分阶段落地计划 T-N0-4/T-N0-5 与 T-N0-3 的内容。

## 字段废弃元数据（消费方反馈第 46 条，2026-09-17）

判断记录：反馈原文——`FieldSchema` 此前没有任何结构化方式标记"该字段已废弃"，废弃事实只写在自由
文本的 `Description`（如 `"{items, xp（已废弃）, xp_equivalent, ...}"`）与升级指南附录 C 里，内容工具
（消费方原始案例：`Editor.Core.Validation.DeprecatedFieldHints`）只能手工维护一份与两处人工核对的
静态清单，框架升级后清单漂移没有编译期/加载期保证。

- `FieldSchema` 新增只读属性 `IsDeprecated`/`DeprecatedSince`（起始版本，`"X.Y.Z"`）/`ReplacedBy`
  （同一级字段清单里已登记的字段名，`null` 表示无同表替代字段）/`DeprecationNote`（可选补充说明）
  与修饰方法 `WithDeprecated(sinceVersion, replacedBy, note?)`（同 `WithCurve`/`WithSoftReference`
  "修饰方法追加可选元数据"惯例，不改动既有构造签名，只能设置一次）。
- **两段校验，时机不同**：`sinceVersion` 的格式合法性（须形如 `X.Y.Z`）只依赖它自己的字符串内容，
  在 `WithDeprecated` 挂载时直接拒绝非法值（同 `WithFreeIds` 的 `reason` 必填检查一样的既有风格）。
  `ReplacedBy` 的存在性依赖"同一级字段清单里有没有这个名字"，但 `WithDeprecated` 调用发生在单个
  `FieldSchema` 实例构造完成之后、被装进它所属的兄弟字段清单之前——此时看不到兄弟字段，无法在挂载
  时判断。推迟到"兄弟字段清单第一次被完整组装"的地方校验：`FieldSchema` 构造函数的 `fields` 参数
  分支（嵌套 `Object` 子结构）与 `TableSchema` 构造函数（表顶层 `Fields`）——两处都已拿到完整的兄弟
  清单，校验不合法直接抛 `ArgumentException`，不留到运行期或门禁才发现。
- `toolchain/validator --list-tables --json` 的 `field_meta` 新增 `deprecated`（`{since, replaced_by,
  note}`，未登记 `IsDeprecated` 时为 `null`），内容工具可直接从 schema 回吐读取废弃提示。
- `SchemaAudit`（`--schema-audit`）新增 `field_deprecated_metadata` 检查：`IsDeprecated` 为真时
  `DeprecatedSince` 必须非空、`ReplacedBy` 非空时须在（顶层）字段清单里存在——这两条在实践中已经被
  上面的构造期校验堵死，本检查是同 `field_group` 一样"理论上不可能触发、仍留一道可枚举软失败防线"
  的既有风格，不加白名单。
- 已知废弃字段（升级指南附录 C）与反馈原文点名的字段均已补登记：`stat.definition` 的
  `group`/`min`/`max`/`is_rating`/`rating_conversion_ref`（1.31.0）、`item.affix.effects`（1.32.0，
  由 `stat_mix`+`grants` 两个字段共同取代，`ReplacedBy` 登记为 `null`，取代关系写在
  `DeprecationNote`）、`prog.xp_source` 的 `base_xp`/`weight`（1.34.0→`base_curve_ref`）、
  `quest.def`/`encounter.def`/`achv.def` 共用的 `rewards.xp`（1.34.0→`xp_equivalent`，三张表共用同一份
  `QuestSchemas.RewardsFields` 子结构，登记一次即三表同步生效）、`skill.def`/`skill.aura_def` 效果参数
  `scaling_stat`/`coefficient`（1.33.0→`scaling` 列表，非周期 `school_damage`/`heal` 与周期
  `periodic_damage`/`periodic_heal` 四个变体分支各自登记）。

## 角度字段弧度制单一声明（消费方反馈第 61 条，2026-09-18）

`FieldUnit` 新增 `Radian` 成员（框架内角度量一律弧度制，逆时针为正、0 指向 +X，集中声明单一来源见
`architecture/05_对象模型与世界.md`"单位约定"一节，本枚举成员只是配合登记/导出用的结构化标记）。全仓库
角度语义字段（`area.trigger_def.shape.{rotation,angle}`、`encounter.def.arena_rules.bounds_shape.
{direction,angle,rotation}`、`spawn.table.facing`、`target.chain_def.shape.angle`）均已补
`FieldSchema.WithUnit(FieldUnit.Radian)` 登记。

- `toolchain/validator --list-tables --json` 的 `field_meta.unit` 此前已经是 `field.Unit.ToString()`
  的通用导出（不是按枚举值逐一分支的白名单），新增 `Radian` 成员后无需改动导出代码，`unit` 字段自动
  吐出 `"Radian"`——同 `"Time"`/`"Percent"`/`"None"` 既有取值一样，内容工具据此判断字段单位，不必解析
  `description` 自由文本猜测。
- 反射式回归测试 `presentation/assembly/tests/AngleFieldRadianUnitTests.cs`：遍历
  `SchemaAudit.EnumerateRegisteredSchemas()` 给出的全部已登记 `TableSchema`（含 `Fields`/`Item`/
  `Variants`/`Map` 递归子结构），断言字段名匹配 `angle|rotation|facing|heading`（大小写不敏感）的
  `FieldKind.Number` 字段均已登记 `Unit == FieldUnit.Radian`，未登记时报具体字段路径；允许一份显式例外
  清单（初始为空，任何新增例外须在测试文件内写明理由），防止后续新增角度字段遗漏该登记。

## 校验规则元数据与不可提升警告（分阶段落地计划 T-N0-2，04 第 5 节数值类校验项分级表）

`IValidationRule` 新增三个带默认实现的成员（11 第 7 节"默认实现"路线，既有规则一行不改）：

| 成员 | 默认值 | 用途 |
|---|---|---|
| `RuleId` | 具体类型名 | 报告 `Rules[]` 的键、`RegisterValidationRule` 去重的键、收集时补到每条 `ValidationIssue.RuleId`（规则自填不覆盖） |
| `DefaultSeverity` | `Error` | 本规则问题通常所处级别，只供报告/工具展示，不改变逐条 `Severity` |
| `NonEscalatable` | `false` | `true` 时本规则的 Warning 在 `WarningsBlock` 下也不计入阻断（04 第 5 节数值类分级表的警告级"抓意图不抓手滑"） |

配套改动：

1. **注册去重**：`RegisterValidationRule` 按 `RuleId` 去重——同一实例重复注册、或另一实例的 `RuleId`
   已注册，静默忽略并保留先注册的那份（多根装配/多个装配入口叠加注册同一条规则不再重复跑、重复报；
   不抛异常，因为重复注册不是编程错误的信号）。
2. **`ValidationIssue` 新增可选 `Group`/`Note`/`RuleId`**：经新增的九参数构造传入（全部必填参数，
   避免与既有六参数构造二义；既有构造物理签名不变，三者为 null）；`WithRuleId` 返回补了规则 id 的副本。
3. **`ValidationReport` 新增 `Rules`（`ValidationRuleSummary`：id/默认级别/不可提升/命中条数，按注册
   顺序、含零命中）与 `NonEscalatableWarningCount`**；三参数构造按 `Rules` 里不可提升规则的 id 匹配
   问题的 `RuleId` 把这部分 Warning 从阻断判定里排除：`IsBlocking = Error > 0 || (WarningsBlock &&
   (Warning − 不可提升 Warning) > 0)`。两参数构造行为不变（`Rules` 为空）。
4. **转发门禁**：`InterfaceDefaultMemberForwardingTests` 对非组合型规则（不持有/不委托另一份
   `IValidationRule`）逐条登记这三个默认成员的豁免（默认值即正确语义）；组合/转发型规则不在豁免之列，
   必须显式转发。

`toolchain/validator` 的文本/JSON 报告输出 `rules[]` 与 `issues[].group/note` 是 T-N0-6 的内容。

## 图诊断的节点定位与统一图分析（消费方反馈第 56/57 条，2026-09-18）

背景：`story_tree`/`quest_prerequisite`/`talent_tree` 三类"节点 + 有向边"图形数据的既有图结构
诊断（成环、悬空引用、id 重复等）只给出表/记录/静态字段名一类粗粒度定位，编辑器一类内容工具拿到
`ValidationIssue` 后无法直接高亮"具体是哪个节点/哪条边"，只能自己重新解析一遍数据反查——第 57 条
指出这一缺口；第 56 条另外指出三类图都缺"从起点不可达/孤立节点"一类展示性图结构信息，但孤立节点
在 `quest_prerequisite`/`talent_tree` 两类图里是合法内容形态（见
`core/gameplay/quest/README.md`/`core/numbers/archetype/README.md` 对应判断记录），不适合做成校验
警告。

- **`ValidationIssue` 新增只读属性 `AffectedNodeIds`**（`IReadOnlyList<string>`，默认空集合而非
  `null`）：跨节点路径类诊断（如各模块的 `*_cycle` 成环检查）按环上出现顺序填入环上全部节点 id；
  单节点类诊断（如 `*_duplicate_node_id`/`*_dangling_next_node`/`*_prerequisite_missing`/
  `quest_prerequisite_unknown`）填入该节点自身 id。经新增的十参数构造函数传入（ABI 只新增，同
  `Group`/`Note`/`RuleId` 三者当年新增九参数构造的既有惯例，不改既有构造物理签名）；新增
  `WithAffectedNodeIds` 返回补齐该字段的副本，同 `WithRuleId` 既有惯例。
- **单节点类诊断的 `Field` 补具体下标路径**（如 `nodes[3].id`/`nodes[3].branches[1].next_node_id`），
  与仓库内既有带下标诊断（如 `objectives[0].count`）的路径格式一致；具体改动见
  `DialogContentValidationRule`/`ArchTalentTreeCycleValidationRule` 类型判断记录。
  `quest_prerequisite_unknown` 的 `Field` 维持既有的 `"prerequisite"`（任务记录本身即最小定位单元，
  `prerequisite` 是标量字符串字段，没有数组下标可补）。
- **新增公开只读图分析入口 `ContentGraphAnalyzer`**（`contracts/ContentGraphAnalyzer.cs`）：对调用方
  给定的"节点 id 全集 + 有向邻接表"统一给出不可达（相对给定起点）/孤立（入度出度均为 0）/入度出度/
  环四类只读结果（`ContentGraphAnalysis`），本身不产出任何 `ValidationIssue`——是否、如何把分析结果
  转成校验问题由调用方决定。放在本模块（而不是 `Core.Gameplay`/`Core.Numbers` 任一具体模块）的理由：
  `story_tree`/`quest_prerequisite` 在 `Core.Gameplay`、`talent_tree` 在 `Core.Numbers`，两者互不
  依赖，唯一的共同祖先是 `Core.Foundation`（`ProjectReference` 链：`Core.Foundation` ←
  `Core.Numbers` ← `Core.Rules` ← `Core.Carriers` ← `Core.Gameplay`），本模块又已经是
  `ValidationIssue`/`IValidationRule` 的所在地，图诊断能力放在这里最贴近既有校验契约面。
  `story_tree` 新增的 `story_tree_node_unreachable` 警告级检查内部复用它（见
  `DialogContentValidationRule` 判断记录）；`quest_prerequisite`/`talent_tree` 按上面的判断记录不
  产出新校验问题，只把它作为公开分析能力暴露给内容工具直接调用。

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
该表已 `RegisterSchema`。**不是全部字段种类都登记**：`RecordExprMapping.ToExprValueKind`
只把能映射为 Expr 标量类型（`Bool`/`Int`/`Number`/`String`/`Id`）的字段种类登记为
`self.<field>`，`IdList`/`Vec2`/`Object`/`Array`/`Expr` 五种不登记（消费方反馈第四批第 25
条，2026-09-10：`Expr` 字段此前误按 `String` 登记，现改为不登记——该字段的值是待求值文本，
`self.<expr_field>` 没有明确的取值语义）；谓词文本引用到未登记字段名时按 ADR-0015 默认规则
整体解析为 `<id_literal>`，不抛异常。

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

## `FileSystemDataSource` 跳过非数据表 JSON 文件（`DataSourceOptions.SkipNonTableJsonFiles`）

判断记录（数据根非数据表 JSON 误判修复任务，2026-09-16）：`FileSystemDataSource.ListTables()`
默认（`DataSourceOptions.SkipNonTableJsonFiles = true`）不再把 `rootDir` 下递归找到的每个
`*.json` 文件都当成数据表候选——游戏侧仓库常见"数据目录旁边/上层还有 `package.json` 之类配置
文件"的布局（如 UPM 包根目录），一旦数据根把这些文件也递归进去，会被误判成表名 `"package"` 的
数据表，报出一堆"缺少顶层字段 table/schema_version/rows"的假错误（游戏运行期
`Adapter.Unity` 的 `GameFoundationBootstrap`/`FrameworkResidentHost` 构造 `FileSystemDataSource`
指向的目录同样可能混有这类文件，不止内容工具场景）。

判定规则（"非数据表 JSON 候选判定"，见 `FileSystemDataSource.cs` 类型注释）：

1. 文件名（去掉 `.json`）含至少一个 `.` 时，取第一个 `.` 之前的字符串作为"域"；域必须匹配
   `^[a-z][a-z0-9_]*$`——覆盖 `<域>.<表名>.json` 这一主流命名约定（如 `arch.class.json`）。
2. 文件名完全等于 `camera_profile`/`ui_layout_definition`/`shell_menu_definition` 这三张 04 第
   2.2 节勘误登记的单段名命名例外时，域取 `TableSchema.WithDomain` 登记的固定值（`camera`/`ui`/
   `shell`，见 `contracts/TableSchema.cs` 该判断记录）。
3. 以上两种方式都得不到域的文件（如 `package.json`）判定为非数据表，跳过。
4. 能得到域的文件还需"直接在数据根下，或所在的直接上级目录名等于该域"——对应
   `data/README.md` 记录的 `<domain>/<table>.json` 目录约定；"直接在数据根下"这一分支是为了
   兼容测试夹具常见的扁平布局。不满足则同样判定为非数据表，跳过（挡住"文件名凑巧带合法域前缀、
   但没放在对应域目录下"的配置文件，如误放的 `arch.config.json`）。

跳过的文件不参与加载/校验，累积在新增只读属性 `FileSystemDataSource.SkippedNonTableFiles`
里——本类型不做任何控制台/日志输出，是否、如何展示这份清单交给调用方决定（`toolchain/validator`
的用法见 `toolchain/README.md`"运行校验器"一节判断记录）。需要恢复"递归目录下全部 JSON 都当表"
旧行为的调用方，显式传入 `new FileSystemDataSource(fs, rootDir, new DataSourceOptions
{ SkipNonTableJsonFiles = false })` 即可（新增的三参数构造函数，原两参数构造函数签名不变，
ABI 只新增，见 `contracts/DataSourceOptions.cs` 判断记录）。

`toolchain/validate_data.py` 第一道骨架检查内的 `is_data_table_candidate`/`_resolve_table_domain`
是同一条规则的独立实现（Python/C# 两个工具链各自的运行时边界不共享代码），修改本规则时两处
必须同步更新；回归测试见 `core/foundation/data_registry/tests/FileSystemDataSourceSkipsNonTableJsonTests.cs`
与 `toolchain/tests/test_validate_data_skips_nontable_json.py`。

## `ValidationIssue.ToString()` 格式稳定性声明与运行期结构化出口现状（消费方反馈第 71 条，2026-09-19）

判断记录：反馈原文指出 `ValidationIssue.ToString()`（`contracts/ValidationReport.cs:142-166`）产出
形如 `[Error] required_field display.map[display.sample_map].sprite_set_id: ...` 的文本，全篇无
`[Stable]`/"稳定契约"一类标注，依赖该文本格式做日志反查跳转的消费方功能（如编辑器侧
`LogRecordLookup`）存在框架格式变化即静默失效的脆弱点。核实结论——**立场明确、范围收窄**：

1. **立场**：`ToString()` 是人类可读的一行诊断文本，供控制台/日志展示，**不承诺格式跨版本稳定**，
   不应被程序解析；已在该方法的 XML 文档注释里写明（见上方 `contracts/ValidationReport.cs`
   本次改动）。这与框架内其它明确标注"稳定契约"的公开 API 待遇一致——没有标注即视为不稳定。
2. **`ValidationIssue` 本身早已是结构化数据**：`Table`/`RecordKey`/`Field`/`Check`/`Message`/
   `Severity`/`Group`/`Note`/`RuleId`/`AffectedNodeIds` 全部是公开只读属性（本文件
   `contracts/ValidationReport.cs:27-66`），C# API 消费方不需要解析 `ToString()`，直接读属性即可。
3. **命令行层已有两条结构化出口**：`toolchain/validator`（`Program.cs:104-105` 定义 `--json` 开关，
   `:300` 常规校验路径、`:448` `--schema-audit` 路径分别输出）与
   `toolchain/asset_import/check_cmd.py check --json`（消费方反馈第 62 条，1.43.0 落地，开关定义见
   `check_cmd.py:235`）。两者 stdout 均只输出一份 JSON 文档，字段与 `ValidationIssue` 的公开属性
   对应。**真正需要程序化消费校验结果的消费方，应该走这两条命令行入口之一，或直接读 C# API 属性，
   不应该解析任一层的人类可读文本输出。**
4. **运行期（Unity Player/Editor 进程内）现状：确无结构化出口，是本条真正收窄后的缺口**——
   `games/_template/Runtime/GameBootstrap.cs:230`（`Debug.LogError("[GameBootstrap] 数据集校验
   未通过，已停止：" + string.Join("; ", report.Issues))`）与
   `games/_template/Runtime/DataHotReload.cs:386-388`（显式 `i.ToString()` 拼接后 `Debug.LogError`）
   是运行期唯二把校验问题往外传的路径，两处都只把 `ToString()` 文本写进 Unity 日志；随后经事件总线
   补发的 `DataValidationFailedEvent`（`core/foundation/data_registry/contracts/Events.cs:58-71`）
   只带 `ErrorCount`/`WarningCount` 两个汇总计数，**不携带任何单条 `ValidationIssue`**——运行期内
   没有任何路径能拿到结构化的问题清单，只能解析日志文本。
5. **是否补一个运行期结构化出口：评估后暂不动手，留给设计层**——已考虑过的方案与各自代价：
   - 方案 A：给 `DataValidationFailedEvent`/`DataLoadCompletedEvent` 新增一个携带
     `IReadOnlyList<ValidationIssue>` 的构造重载（ABI 只新增，代价小）。但这只服务"与 Unity 进程
     同进程内订阅事件总线"的 C# 消费方；本条反馈的场景（编辑器一类独立进程外部工具）拿不到进程内
     事件，这个方案不解决真实诉求。
   - 方案 B：运行期额外落一份结构化诊断（如日志旁再写一份 JSON 文件，或额外打一行专用前缀的结构化
     日志供外部按行解析）。这能真正解决"外部进程读运行期校验结果"的诉求，但等于**新开一个对外
     承诺格式稳定的运行期契约**（文件路径/日志前缀/字段形状一旦发布就要维持兼容），影响面和"要不要
     承诺、承诺成什么形状"都需要设计层先拍板，不是纯脚本/文档改动，本次不擅自实现。
   - 判断依据：在没有确认消费方实际怎么消费运行期校验结果（见下方"反问"草稿）之前，任何结构化
     方案都可能选错形状（比如消费方其实根本不需要运行期路径，只是没发现命令行 `--json` 出口）；
     先问清楚再定型，避免先建一个契约再发现选错了、造成二次破坏性变更。
6. 给消费方的反问见
   [`architecture/落地计划/消费方反馈-2026-09-19-编辑器-第67-72条.md`](../../../architecture/落地计划/消费方反馈-2026-09-19-编辑器-第67-72条.md)
   "待消费方答复"节"第 71 条反问（原文，供直接转发）"（原独立草稿文件
   `消费方反馈-2026-09-19-第67-72条回复草稿-71号反问.md` 已于提交 `d406af3` 并入本文档并删除，
   本节链接此前未同步更新，指向了一个已不存在的文件）。

## 不负责什么

- 不实现任何具体业务校验规则（效果数上限、预算、叠加冲突等），只提供 `IValidationRule` 扩展点。
- 不做资源加载（图片/音频/字体），那是 `IResourceLoader` 与表现层的职责；`IFileSystem` 只用于
  读取数据表 JSON 文本本身。
- 不做 JSON 序列化之外的任何文件系统写操作（本模块只读数据表，不回写）。
- 不提供 Unity/`IResourceLoader.loadAsync(kind: data_table)` 到 `IDataSource` 的桥接实现——
  `FileSystemDataSource` 只依赖 `IFileSystem`，游戏层/适配层若经 `IResourceLoader` 异步加载
  数据表文本，需要自行适配出一个 `IDataSource`（如先用 `IResourceLoader` 把文本读进内存，
  再包 `InMemoryDataSource`）。
