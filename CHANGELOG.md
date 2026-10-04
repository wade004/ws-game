# 变更日志

本文件记录 ws-game（游戏技术基础架构框架仓库）各构建产物版本号之间的变更，格式遵循
[Keep a Changelog](https://keepachangelog.com/) 惯例；版本号遵循语义化版本
（[SemVer](https://semver.org/)）：`MAJOR.MINOR.PATCH`——MAJOR 表示不兼容变更（走 ADR 审批的
契约签名变化、存档格式不兼容、数据表字段删改）；MINOR 表示向后兼容的新增能力；PATCH 表示缺陷
修复与文档勘误。单一版本源见仓库根 `VERSION` 文件；版本号与发布流程见根 `README.md`"版本与发布"
一节。

### 编辑器相关契约

ADR-0018 决策 4 要求：本仓库对"编辑器项目（独立仓库，随具体游戏走，见
[ADR-0018](architecture/adr/0018-编辑器随游戏走与框架为此提供的交付物.md)）依赖的契约面"发生的
变更，单独标注、汇总索引在此小节，供编辑器项目维护者只看这一类条目即可判断新版本是否需要跟改
（不需要通读全部版本条目）——每条给出条目所在的版本区间与简述，详情见对应版本正文。

- **F1（1.13.0）**：`FieldSchema` 新增可选子结构登记
  （`Fields`/`Item`/`Variants`，`Object`/`Array` 两种字段种类）与 `VariantSchema` 契约类型；
  `DataRegistry` 按登记递归校验，新增检查名 `variant_discriminator`/`substructure_depth`/
  `unknown_subfield`；`toolchain/validator --json` 输出新增
  `disabled_optional_rules`/`enabled_optional_rules` 字段。
- **F2（1.13.0）**：新增核心库校验装配入口
  `Presentation.Assembly.ContentValidationAssembly.Run`/`CreateRegistry`（`toolchain/validator`
  与编辑器基础套件共用同一份装配代码）；新增第四个私服包 `com.gamefoundation.adapter.headless`
  （无头适配层，随构建产物分发，供内容编辑器等无头宿主使用）。
- **F3（1.13.0）**：新增元数据门禁
  `Presentation.Assembly.SchemaAudit`/`toolchain/validator --schema-audit`（六项检查，见下方
  `[1.13.0]` 正文"F3"小节）；`DataRegistry` 新增只读属性 `RegisteredSchemas`；开发期数据
  热重载标准实现 `games/_template/Runtime/DataHotReload.cs`（`Reload` 成功/失败经事件总线补发
  `data.load_completed`/`data.validation_failed`）；`.github/workflows/release.yml` 四包修正
  （编辑器项目若参照本仓库发布工作流的必需附件集合，需同步补齐第四个 `.tgz`）。
- **E1～E4 交付面变化（1.15.0，消费方反馈处理，
  [消费方反馈-2026-09-10-编辑器.md](docs/消费方反馈/消费方反馈-2026-09-10-编辑器.md)）**：
  Release 新增预编译 `toolchain/validator`（附隔离用的空 `Directory.Build.props`）作为发布附件，
  编辑器项目不再需要自行现场编译 validator；`toolchain/get_framework.ps1` 改为自包含（不再
  dot-source 同目录 `_hash.ps1`），可单独下载使用；`ws-game.lock` 的 `source` 字段不再写本机
  绝对路径（`local_path` 改为 `zip_file_name`）；新增可选附件 `ws-game-<ver>-samples.zip` 与
  `get_framework.ps1 -WithSamples` 参数，用于下载并合并落地验收数据集样例。
- **E5（1.15.0）**：[ADR-0020](architecture/adr/0020-表达式词法器纳入公开契约.md)——
  `core/foundation/expr` 的 `ExprLexer`/`ExprToken`/`ExprTokenKind` 由 `internal` 改为
  `public`，`ExprLexer.Tokenize(string)` 成为公开的词法切分入口，编辑器等工具做语法高亮应改用
  该入口，不再自行正则切词。
- **E8（1.15.0）**：`toolchain/validate_data.py` 新增 `--json`，把骨架检查结果与
  `toolchain/validator --json` 输出合并为一份结构化 JSON 打印到标准输出（人类可读诊断改走标准
  错误），结构见脚本头部 docstring 与 `data/README.md`。
- **E10（1.15.0）**：`IDataRegistryView` 新增只读属性 `RecordCount`（带默认实现，按
  `Tables`/`GetAll` 求和，不要求已有实现类改动，不构成 ABI 破坏），`CreateRegistry` 路径（调用方
  自行持有 registry、自行调用 `Reload`）现在也能拿到精确记录计数，不必自行遍历求和。
- **ADR-0021（1.15.0，消费方反馈处理，
  [消费方反馈-2026-09-10-技能效果参数范围.md](docs/消费方反馈/消费方反馈-2026-09-10-技能效果参数范围.md)）**：
  `FieldSchema` 新增可选 `Range` 字段（Number/Int 字段的取值范围登记），`toolchain/validator
  --list-tables --json` 每张表新增 `field_ranges` 导出，编辑器可据此在数值输入控件上就地校验，
  不必等到一次完整加载校验才发现越界。
- **ADR-0022（1.16.0，消费方反馈处理，
  [消费方反馈-2026-09-10-编辑器-第二批.md](docs/消费方反馈/消费方反馈-2026-09-10-编辑器-第二批.md)）**：
  `TableSchema` 新增 `Layer`/`Module`/`Domain`/`TimeScope`，`FieldSchema` 新增 `Group`/`Unit`，
  `IdList` 字段种类补齐 `ReferenceTable`/`ReferenceDomain`/`WithFreeIds` 登记，`reference_integrity`
  扩展覆盖 `IdList` 元素；元数据门禁新增五项自洽检查；`toolchain/validator --list-tables --json`
  每张表新增 `layer`/`module`/`domain`/`time_scope`，每个字段新增 `field_meta`
  （`group`/`unit`/`reference_table`/`reference_domain`/`free_ids`）。
- **第 17 条修复（1.16.1，消费方反馈处理，
  [消费方反馈-2026-09-10-编辑器-第二批.md](docs/消费方反馈/消费方反馈-2026-09-10-编辑器-第二批.md)
  第 17 条）**：`DataRegistry.RecordCount` 阻断态不再抛异常（直接读内部按表合并去重后的记录快照）；
  `IDataRegistryView` 新增 `bool TryGetRecordCount(out int count)`（带默认实现，阻断态返回
  `false` 而不抛异常）；`ContentValidationAssembly.Run` 统一改用 `registry.RecordCount`，不再另开
  事件订阅旁路。`IDataRegistryView.RecordCount` 默认实现本身保持不变（仍可能抛异常，供无具体
  `DataRegistry` 实现的第三方替身兜底），XML 注释已更新建议改用新成员。
- **消费方反馈第三批 18/19/20/22（1.17.0，消费方反馈处理，
  [消费方反馈-2026-09-10-编辑器-第三批.md](docs/消费方反馈/消费方反馈-2026-09-10-编辑器-第三批.md)）**：
  `IExprSchema` 新增 `KnownKeys(string group)`/`KnownGroups`（带默认实现）：按分组枚举已登记的
  宿主引用 key，供编辑器自动补全；`ExprIssue`/`ExprNode`（及全部子类）新增源文本位置区间
  `Start`/`Length`（新增重载构造，既有构造不变）：`ExprParser.Parse` 产出的语法树与
  `ExprValidator` 产出的校验问题项均携带精确区间，供编辑器把问题定位/高亮到源文本对应片段；
  `IDataRegistryView` 新增 `TryGetAll`/`TryQuery`（带默认实现）：阻断态下仅供内容工具使用的只读
  通道，运行期宿主仍须使用 `GetAll`/`Query`，阻断态即禁止读取的既有结论不变；`IDataSource` 新增
  `Root`（带默认实现），`OverrideDiagnostic` 新增
  `OverridingRootIndex`/`OverriddenRootIndex`/`OverridingRelativePath`/`OverriddenRelativePath`：
  多根合并覆盖诊断新增"根序号 + 相对路径"，`toolchain/validator` 文本/JSON 输出同步，绝对路径
  字段保留。**第 21 条（ADR-0019 子结构登记第二批，18 个字段）本轮未完成，单独立项**，见该文档
  第 21 条"如实说明"。
- **消费方反馈第 27 条（Unreleased）**：具体游戏/工具实际使用的正式装配入口是多个模块
  `IExprSchema` 登记表组合/包装出的门面（如 `PresentationSchemaCatalog.FullExprSchema`），
  其 `KnownKeys`/`KnownGroups` 此前逐级落回接口默认实现（返回空集合），九个分组全部返回空——
  已在四处组合/包装实现补齐显式转发（并集聚合全部成员登记表的结果，不再只反映某一个成员登记
  表），新增反射门禁 `InterfaceDefaultMemberForwardingTests` 防止同类遗漏再次发生。详见
  [消费方反馈-2026-09-11-编辑器-第27条.md](docs/消费方反馈/消费方反馈-2026-09-11-编辑器-第27条.md)。
- **消费方反馈第 28/29 条（1.20.0，消费方反馈处理，
  [消费方反馈-2026-09-11-编辑器-第28-29条.md](docs/消费方反馈/消费方反馈-2026-09-11-编辑器-第28-29条.md)）**：
  `FieldSchema` 新增可选 `AllowedValues`（`IdList`/`Id` 固定取值集合登记，与 `FreeIds`/
  `ReferenceTable` 互斥，新增运行期检查名 `field_allowed_value`，元数据门禁新增
  `idlist_allowed_values_conflict`）与可选 `SoftReferenceTable`/`SoftReferenceDomain`
  （软引用元数据，仅 `Id`/`IdList` 可设、不参与加载期引用完整性校验，元数据门禁新增
  `soft_reference_kind`）；`toolchain/validator --list-tables --json` 每个字段的 `field_meta`
  新增 `allowed_values`/`soft_reference_table`/`soft_reference_domain`。
  `creature.template.npc_flags` 改用 `AllowedValues` 登记（收口 `CreatureContentValidationRule`
  此前手写的重复校验）；`ai_rotation_ref`/`ai_behavior_ref`/`loot_table_ref`/`display_ref` 等
  一批已确认目标表的字段补登软引用。
- **消费方反馈第 31 条（1.22.0）**：`CombatOptions` 新增可选 `ResolveTrace: Action<EffectContext,
  ResolveResult>?`（结算追踪回调），供编辑器"右侧结算预览"/"简易战斗回放"直接订阅
  `Resolver.Resolve` 每次真实返回前的完整输入输出（含分步中间值 `Steps`），不必自行复刻结算
  公式。详见
  [消费方反馈-2026-09-11-编辑器-第31条.md](docs/消费方反馈/消费方反馈-2026-09-11-编辑器-第31条.md)。
- **消费方反馈第 32 条（1.23.0）**：新增
  `Core.Foundation.EngineAdapter.AssetRefConventions`，把此前分散在引擎适配层、框架表现层、
  `toolchain/asset_import` 三处的 `sprite_set_id`/`icon_id` → 资产相对路径解析规则收口为公开
  契约（`SpriteSetDirectory`/`IconFile`/`TryParseSpriteSetId`/`TryParseIconId`），规则本身不变。
  详见 [ADR-0025](architecture/adr/0025-资源引用标识到资产相对路径的约定纳入公开契约.md)、
  [消费方反馈-2026-09-11-编辑器-第30-32-33-34条.md](docs/消费方反馈/消费方反馈-2026-09-11-编辑器-第30-32-33-34条.md)。
- **消费方反馈第 34 条（1.23.0）**：`PresentationSchemaCatalog` 新增公开
  `DefaultDisplayMapCoverageSources`（五张表），`ContentValidationOptions
  .DisplayMapCoverageSources` 未指定时默认使用该清单，`DisplayMapCoverageRule` 从此默认启用
  （此前默认禁用）；`toolchain/validator` 的 `--display-map-sources` 改为可选覆盖参数。详见
  [消费方反馈-2026-09-11-编辑器-第30-32-33-34条.md](docs/消费方反馈/消费方反馈-2026-09-11-编辑器-第30-32-33-34条.md)。
- **消费方反馈第 35 条（1.24.0）**：新增公开
  `Core.Gameplay.Loot.LootTableAnalyzer.ExpectedProbabilities(LootTableDef,
  LootAnalysisContext)`（掉落表期望概率分析），供编辑器"掉落与爆率编辑器"分组树右侧概率/期望
  数量列直接调用而不必复刻语义或做蒙特卡洛逼近；与真实抽取（`LootHost.Roll`）共用同一份条件
  筛选/权重归一实现，不改变任何抽取行为。详见
  [消费方反馈-2026-09-11-编辑器-第35条.md](docs/消费方反馈/消费方反馈-2026-09-11-编辑器-第35条.md)。
- **消费方反馈第 37 条（1.26.0）**：`IDataRegistryView` 新增
  `GetReferenceDeclarations(): IReadOnlyList<ReferenceDeclaration>`（只读回吐经
  `IDataRegistry.DeclareReference` 声明的全部引用关系：源表/源字段/目标表/是否可选/登记来源），
  `IDataRegistry.DeclareReference` 新增带来源标注的重载；`toolchain/validator --list-tables
  --json` 新增 `reference_declarations` 导出；`SchemaAudit` 新增元数据门禁检查
  `declared_reference_unregistered`（告警级）。详见
  [消费方反馈-2026-09-12-编辑器-第37条.md](docs/消费方反馈/消费方反馈-2026-09-12-编辑器-第37条.md)。
- **消费方反馈第 38/39 条（1.26.1）**：`QuestContentValidationRule` 新增前置链循环检测
  `quest_prerequisite_cycle`/`quest_prerequisite_unknown`（阻断级，不新增公开 API，编辑器第 38
  条）；`dialog.story_tree.nodes[].performance_hook_ref`/`dialog.gossip_menu.options[].
  actions[]{kind=script}.ref`/`encounter.def.phases[].on_enter_hook`/`skill.def.
  effects[]{kind=script}.params.hook_id`/`area.trigger_def.params{trigger_type=script}.hook_id`
  五处字段补登 `SoftReferenceTable("found.hook")`（纯新增可选元数据，编辑器第 39 条）。详见
  [消费方反馈-2026-09-13-编辑器-第38-39条.md](docs/消费方反馈/消费方反馈-2026-09-13-编辑器-第38-39条.md)。
- **ADR-0029（1.27.0）**：`Core.Foundation.DataRegistry.SchemaMigrator` 公开静态类
  （`BuildChain`/`MigrateRow`/`MigrateEnvelope`），把此前只服务于加载期内部的迁移链串接逻辑收口
  为公开、单一来源的静态入口，并新增"整表迁移"（信封级，供内容工具写回磁盘前调用）；加载器新增
  信封级可选键 `migrated_from` 的形状校验；`toolchain/validator --list-tables --json` 每张表新增
  `schema_version`/`migrations` 导出。详见
  [ADR-0029](architecture/adr/0029-迁移链串接与整表迁移纳入公开契约.md)、
  [消费方反馈-2026-09-13-编辑器-第40条.md](docs/消费方反馈/消费方反馈-2026-09-13-编辑器-第40条.md)。
- **消费方反馈第 41/42 条（1.28.0）**：`DataRegistryOptions` 新增 `WarnOnMissingTranslation`
  （默认 `true`）——`l10n.locale` 已登记的非默认语言下 `TextKey` 字段缺翻译，新增 Warning 级
  `text_key_exists`（消息附带回退链落点），默认语言缺失仍是 Error；`toolchain/validator` 新增
  `--no-missing-translation-warning` 开关（`--json` 新增 `warn_on_missing_translation` 字段），
  `toolchain/validate_data.py` 透传同名参数；`l10n.locale.fallback` 字段改登记为
  `FieldKind.Reference`（`--list-tables --json` 该字段 `kind` 由 `Id` 变为 `Reference`），指向
  未登记语言的坏数据从此在加载期即报 `reference_integrity` 错误。详见
  [消费方反馈-2026-09-14-编辑器-第41-42条.md](docs/消费方反馈/消费方反馈-2026-09-14-编辑器-第41-42条.md)。
- **消费方反馈第 43/44 条（1.29.0）**：`Presentation.Assembly.ContentValidationAssembly` 新增
  `OptionalRules`/`OptionalRuleDescriptor`/`TryGetOptionalRuleByCheck`（可选规则"规则名 ↔ 检查名"
  关联单一来源），`SpawnSummonOnlyCreatureRule`/`DisplayMapCoverageRule` 的 `CheckName` 常量随之
  改为公开，`toolchain/validator --json` 新增 `optional_rules: [{rule, check, enabled}]`（第 43
  条）；新增 `Core.Carriers.Creature.RegistryCreatureTemplateQuery`，`ContentValidationAssembly`
  未提供 `CreatureTemplateQuery` 时默认改用它，`SpawnSummonOnlyCreatureRule` 从此默认启用（此前
  默认禁用，比照 1.23.0 `DisplayMapCoverageRule` 先例），`DisabledOptionalRules`/
  `disabled_optional_rules` 默认恒为空（第 44 条根治）；示例数据集新增 `summon_only` 生物模板
  `creature.sample_summon_totem`。详见
  [消费方反馈-2026-09-14-编辑器-第43-44条.md](docs/消费方反馈/消费方反馈-2026-09-14-编辑器-第43-44条.md)。
- **数值设计落地 T-N0-1（1.30.0）**：`FieldSchema` 新增可选 `Curve`/`WithCurve(CurveSchema)`
  （曲线形态标记：断点表 `{x, y}` + 横轴语义 / 二元饱和 `{k, cap}`，见 04 第 3.6 节），新增契约
  类型 `CurveSchema`/`CurveAxis`/`CurveShape` 与工厂 `CurveSchema.BreakpointsField`/`SaturationField`；
  元数据门禁新增 `field_curve_shape`。编辑器若按字段元数据渲染曲线编辑控件，可据 `Curve` 识别
  曲线字段；`toolchain/validator --list-tables --json` 每个字段的 `field_meta` 新增 `curve`
  （`null` 或 `{shape: breakpoints|saturation, axis: level|item_level|value}`）。
- **数值设计落地 T-N0-2（1.30.0）**：`IValidationRule` 新增三个带默认实现的成员 `RuleId`/
  `DefaultSeverity`/`NonEscalatable`（既有实现无需改动）；`ValidationIssue` 新增可选 `Group`/`Note`/
  `RuleId`（新增九参数构造与 `WithRuleId`，既有六参数构造签名不变）；`ValidationReport` 新增
  `Rules`（`ValidationRuleSummary` 列表）与 `NonEscalatableWarningCount`，`WarningsBlock` 下不可提升
  规则的 Warning 不再计入 `IsBlocking`；`RegisterValidationRule` 按 `RuleId` 去重。编辑器问题面板
  可据 `RuleId`/`Group`/`Note` 分组展示；`toolchain/validator` 报告字段随 T-N0-6 补齐。
- **数值设计落地 T-N0-3（1.30.0）**：新增框架级校验规则 `CurveMonotonicFiniteRule`（检查名
  `curve_monotonic_finite`，Error 级），随 `PresentationSchemaCatalog.RegisterAll` 默认注册；编辑器
  问题面板会出现这一新检查名，按检查名过滤的既有逻辑不受影响。
- **数值设计落地 T-N0-4（1.30.0）**：`item.budget_curve`/`stat.rating_conversion` schema 版本 1→2，
  `entries` 元素改为通用断点表 `{x, y}`（`FieldSchema.Curve` 标记横轴语义），v1 字段名经迁移链自动改名；
  编辑器曲线编辑控件（物品编辑器 5.5.3 曲线图）应按 `x`/`y` 读写并据 `Curve.Axis` 标注横轴，
  执行整表迁移仍走 ADR-0029 的 `SchemaMigrator.MigrateEnvelope`。
- **数值设计落地 T-N0-5（1.30.0）**：`ProgLevelCurveValidationRule` 新增检查名 `level_curve_xp_monotonic`；
  `combat.resist_curve`/`prog.level_curve` 的字段与版本均不变。
- **数值设计落地 T-N0-6（1.30.0）**：`toolchain/validator --json` 新增 `rules[]` 与
  `issues[].group/note/rule_id`，编辑器问题面板可据此按规则分组并展示作者说明原文；既有字段不变。
- **数值设计落地阶段 N1 · T-N1-3（1.31.0）**：`stat.rating_conversion` 新增可选字段
  `saturation`（`CurveSchema.SaturationField`，`Curve.Shape=Saturation`，子字段 `k`/`cap`），与
  既有 `entries`（`Curve.Shape=Breakpoints`）二选一，由新增校验规则
  `StatRatingConversionValidationRule`（检查名 `stat_rating_conversion_requires_one_shape`）
  强制恰好二选一；`entries` 从 `required: true` 放宽为 `required: false`，schema 版本不变
  （仍为 2）。编辑器曲线编辑控件遇到 `Curve.Shape=Saturation` 的字段应渲染为"数值 × 等级"
  两参数输入（`k`/`cap`），不是断点表；物品编辑器 5.5.3 曲线图一节若复用本表的编辑控件，需
  按 `field_meta.curve.shape` 分流两种编辑器 UI。`toolchain/validator --list-tables --json`
  的 `field_meta.curve` 对本字段输出 `{shape: "saturation", axis: "value"}`（复用 T-N0-1 已有
  的 `field_meta.curve` 导出结构，不新增导出字段）。
- **数值设计落地阶段 N1 · T-N1-4（1.31.0）**：`arch.class` 新增可选字段 `derivation_overrides`
  （`Array<{stat:Reference(stat.definition), source:Reference(stat.definition), coefficient:Number}>`，
  ADR-0030 决策 2"职业模板可覆盖派生系数"），纯新增可选字段不升级 `currentSchemaVersion`（仍为
  1）；新增校验规则 `ArchClassDerivationOverrideValidationRule`（检查名
  `arch_class_derivation_override_requires_existing_edge`，Error 级，04 第 5 节分级表未列出，
  按 `arch_class_*` 前缀命名，设计层裁定（2026-09-14）：采纳）：每条覆盖的 `stat` 必须是 `stat.definition`
  里 `category=derived` 的属性，且 `source` 必须出现在该属性 `derived_from[].stat` 登记的来源
  列表中。编辑器职业模板编辑界面若展示派生属性系数编辑控件，可据本字段渲染"按来源覆盖系数"的
  子表单，`stat`/`source` 两个引用字段的下拉候选建议限定为目标属性的 `derived_from` 列表（避免
  用户在界面上就构造出会被本规则拦下的非法组合）。`toolchain/validator --list-tables --json`
  对 `arch.class` 表新增该字段的常规字段元数据导出（复用既有 `Reference`/`Array` 字段种类，不
  新增导出结构）。
- **数值设计落地阶段 N1 · T-N1-5（1.31.0）**：新表 `stat.weight`（属性 id → 权重当量，可按
  职业覆盖，ADR-0030 决策 7），字段 `id`/`stat`（Reference→`stat.definition`）/`weight`
  （Number，`>= 0`）/`class_overrides`（可选，`Array<{class:Reference(arch.class),
  weight:Number(>= 0)}>`）/`description`；`StatHost` 不读取本表。编辑器属性/装备编辑界面若展示
  权重编辑控件，`class_overrides` 建议渲染为"按职业覆盖"子表单，`class` 引用字段下拉候选取
  `arch.class` 全表（不像 `arch.class.derivation_overrides` 那样需要限定到某个属性的
  `derived_from` 子集）。`toolchain/validator --list-tables --json` 新增本表的常规字段元数据
  导出（复用既有 `Reference`/`Array`/`Number` 字段种类，不新增导出结构）。
- **数值设计落地阶段 N1 · T-N1-8（1.31.0）**：新表 `combat.level_diff_table`（等级差规则表，
  ADR-0030 决策 6），字段 `id`/`miss_bonus`/`crit_suppression`/`xp_factor`（三条断点表曲线，
  横轴新增 `CurveAxis.LevelDiff`——等级差 Δ，可负，编辑器曲线编辑控件遇到该轴应渲染为允许负值
  的横轴输入，不像既有 `CurveAxis.Level`/`ItemLevel` 那样默认非负）/`grey_line`（断点表曲线，
  横轴 `CurveAxis.Level`，攻击者有效等级本身，不是 Δ）。`combat.hit_table_config` 的 `miss` 分支
  新增可选字段 `hit_stat`（`Reference(stat.definition)`，攻击者命中属性）——编辑器命中表编辑界面
  若渲染六分支的 `stat`/`base` 输入，`miss` 分支需额外渲染这一列，其余五分支不出现该字段。
  `toolchain/validator --list-tables --json` 的 `field_meta.curve` 对 `miss_bonus`/
  `crit_suppression`/`xp_factor` 三列输出 `{shape: "breakpoints", axis: "level_diff"}`（新增轴
  取值字符串，复用既有 `field_meta.curve` 导出结构，编辑器需要按新字符串区分"这是差值轴不是等级
  轴"，不识别的客户端可退化为按普通数值轴渲染，不阻断）。
- **数值设计落地阶段 N1 · T-N1-9（1.31.0）**：新校验规则 `stat_definition_no_consumer`（04
  第 5 节"属性无消费者"，Warning 级，`NonEscalatable=true`）随 `RulesSchemaCatalog.RegisterAll`
  一起注册，出现在 `toolchain/validator --json`/`--list-tables --json` 的 `rules[]` 与命中
  记录的 `issues[]` 里——编辑器问题面板若已按 04"警告级这一组登记为不可提升"渲染既有规则（同
  T-N0-2 起的 `rules[].non_escalatable` 字段），本规则天然落入同一渲染分组，不需要新增前端
  分支；若编辑器给"属性详情"面板加"谁在引用我"反查视图，本规则的扫描逻辑（全部已注册表的
  `Reference`/`SoftReference`/`Map` 键引用字段）可直接复用同一份实现思路。
- **数值设计落地阶段 N2 · T-N2-1（1.32.0）**：`item.slot_definition`/`item.quality_definition`/
  `item.template` 三张既有表新增字段（`budget_coefficient`/`price_coefficient`；`affix_count`/
  `grant_budget_share`/`price_multiplier`；`value_override`/`budget_note`），`stat_roll_ref`
  描述改为兼容位；新表 `item.armor_curve`/`item.weapon_dps_curve`/`item.req_level_curve`（断点表
  曲线，横轴 `CurveAxis.ItemLevel`，形态与既有 `item.budget_curve` 一致）。新校验规则
  `ItemQualityMultiplierOrderRule`（检查名 `item_quality_multiplier_order`）出现在
  `toolchain/validator --json`/`--list-tables --json` 的 `rules[]` 里；编辑器品质编辑界面若提供
  `budget_multiplier`/`price_multiplier` 输入控件，应据 `sort_weight` 实时提示顺序冲突（同校验
  逻辑：按排序权重分组，跨组不得递减）。`toolchain/validator --list-tables --json` 对三条新曲线
  表的 `field_meta.curve` 输出 `{shape: "breakpoints", axis: "item_level"}`（复用既有导出结构，
  不新增字段）。
- **数值设计落地阶段 N2 · T-N2-2（1.32.0）**：`item.affix` 由留位（仅 `id`/`name_key`/
  `effects` 三个占位字段）转正为预算份额包正式表：新增必填 `budget_share`/`stat_mix`/
  `quality_pool`/`weight` 与可选 `grants`；`effects` 改为已废弃占位字段（保留一个版本周期只作
  读取兼容，不再解析）。`item.template.affixes` 语义由"词缀引用"改写为"该模板掉落时可抽取的
  词缀候选白名单"，缺省 `[]` 视为不收窄。新校验规则 `ItemAffixStatMixRatioSumRule`（检查名
  `item_affix_stat_mix_ratio_sum`）出现在 `toolchain/validator --json`/`--list-tables --json`
  的 `rules[]` 里：单条 `item.affix.stat_mix[].ratio` 之和超过一报 Error；编辑器词缀编辑界面若
  提供 `stat_mix` 多行属性比例输入，应实时汇总校验"之和不超过一"（同校验逻辑，1e-9 浮点容差）。
  `stat_mix[]` 子结构（`stat`/`ratio`）与 `grants` 子结构经 `FieldSchema.Fields`/`Item` 登记，
  出现在 `toolchain/validator --list-tables --json` 的 `field_meta`/子结构导出里，与
  `item.template.stats[]`/`grants` 同一套导出机制，编辑器无需额外适配。
- **数值设计落地阶段 N2 · T-N2-3（1.32.0）**：`item.budget_curve` 新增可选字段 `exponent`
  （消耗公式指数 `k`，缺省 `1.5`）。`ItemBudgetValidationRule` 预算超标校验（检查名不变，
  `item_budget_exceeded`）消耗公式改为加权 `(Σ(属性值×权重)^k)^(1/k)`（权重来自 `stat.weight`，
  百分比属性经 `stat.rating_conversion` 换算曲线折回点数）、上限乘 `item.slot_definition.
  budget_coefficient`；新增检查名 `item_budget_utilization_low`（Warning，不可提升，预算利用率
  低于阈值默认七成，出现在 `toolchain/validator --json`/`--list-tables --json` 的 `rules[]`
  里）。`ItemBudgetValidationRule` 新增构造重载 `(Id, double)` 可配置该阈值，`CarriersSchemaCatalog
  .RegisterAll` 同步新增重载透传；编辑器物品编辑界面若展示"预算利用率"进度条，应据同一比值
  （消耗/上限）与阈值着色。`Core.Numbers.StatBlock` 新增公开类型 `RatingConversionShape`/
  `RatingConversionEvaluator`（`StatHost.ConvertRating` 内部换算式子提升为共享静态工具，纯内部
  重构，不改变既有属性换算的对外可见行为）。
- **数值设计落地阶段 N2 · T-N2-4（1.32.0）**：新增 `IBudgetSolver`（预算反解契约，
  `Solve(itemLevel, qualityId, slotId, statMix, budgetCurveId, shareOfBudget, view)`，外加
  `shareOfBudget` 缺省 1.0 的重载）与实现类 `BudgetSolver`——给定物品等级、品质、槽位与属性
  组合比例，反解各属性值，供编辑器"按预算生成属性"（例如给词缀/标准装备一键落值）使用；返回
  `BudgetSolverResult`（各属性值、目标预算、实际消耗、使用的 k/权重）。新增 `EquipmentScoreAnalyzer`
  （静态类，`Score`/`Compare`）——把预算消耗公式换成职业权重即为装备评分，编辑器可用它渲染"评分
  对比箭头"/"一键换装最优"一类界面元素；两者均出现在 `toolchain/validator --list-tables --json`
  之外（不是数据表，是运行时契约面），编辑器项目需要按新公开类型直接调用。`ItemBudgetCurve`
  新增重载 `BuildStatBudgetInfo(IDataRegistryView, Id classId)`（按职业覆盖权重）。
- **数值设计落地阶段 N2 · T-N2-6（1.32.0）**：`item.slot_definition` 新增可选字段 `has_armor`
  （Bool，缺省 `false`，设计层裁定——取代 T-N2-5"非武器位且真正装备位"的护甲位推断规则）：编辑器
  槽位定义编辑界面需要为它补一个布尔勾选控件，并且游戏层已登记的防具位（头/胸/腿/手/脚等）需要
  显式补勾选，否则升级后不再自动获得护甲。`item.weapon_dps_curve` 新增可选字段 `variance`（伤害
  范围浮动比例，缺省 `0.1`，本任务只登记无消费者）。新校验规则
  `ItemWeaponDamageDeviatesDpsCurveRule`（检查名 `item_weapon_damage_deviates_dps_curve`，
  Warning，不可提升）出现在 `toolchain/validator --json`/`--list-tables --json` 的 `rules[]` 里：
  编辑器武器伤害区间编辑控件若展示 `damage_min`/`damage_max`，可据"均值 / 秒伤曲线期望值"比值与
  阈值（默认 ±20%）着色提示，惯例同 T-N2-3 的"预算利用率"进度条。`Core.Rules.Common
  .IWeaponDamageQuery` 新增默认接口成员 `GetWeaponDps(Id unitId): double`——不是数据表，是运行时
  契约面，编辑器/内容工具若需要展示"当前武器秒伤"，可直接调用该成员，不需要自行重算曲线×倍率×
  系数。
- **数值设计落地阶段 N2 · T-N2-8（1.32.0）**：`loot.table.groups[].entries[]` 新增可选字段
  `quality_weights`（`Map<Reference(item.quality_definition), Number>=0>`，装备类条目的品质权重；
  编辑器掉落表编辑界面可为该字段渲染"品质 → 权重"键值对表格，键的候选下拉从已加载
  `item.quality_definition` 取，键的引用完整性与值的非负已由 `toolchain/validator
  --schema-audit`/加载期校验原生覆盖，不需要编辑器自行校验）。`diff.tier` 新增可选字段
  `item_level_offset`（Int，缺省 `0`，该难度下掉落/商店的物品等级偏移）。运行时契约面新增
  `Core.Carriers.Common.LootRollOutcome`（带身份的掉落结果：模板 id、数量、品质、词缀引用、物品
  等级）与 `ILootRoller`/`Core.Gameplay.Loot.ILootHost` 各自新增的默认接口成员
  `RollDetailed(...)`——编辑器/内容工具若需要预览"某次掉落的完整身份"（不只是模板 id + 数量），
  应改调用 `RollDetailed` 而不是既有 `Roll`；`Core.Gameplay.Loot.RollContext` 新增构造重载，
  额外接受 `sourceLevel`/`itemLevelOffset`。`Core.Gameplay.Difficulty.IDifficultyHost` 新增默认
  接口成员 `ItemLevelOffset: int`（缺省 `0`），与既有 `LootMultiplier` 同一读取口径。**迁移说明**：
  旧 `Roll`/`ILootRoller.Roll` 的随机数消耗会在两种情况下增加（均追加在既有"掉哪条"掷骰之后，不
  改变"掉出哪个模板/多少个"这一层的既有结果，只影响该次调用之后的随机数序列）——(1) 条目配置了
  `quality_weights` 时新增品质骰；(2) **不论条目是否配置 `quality_weights`**，只要该条目 `item.*`
  模板（掷出的或缺省取模板自身）的品质在 `item.quality_definition.affix_count` 登记了大于零的值、
  且 `item.affix` 里有该品质池的候选词缀，就会新增词缀骰——`affix_count` 是品质本身的属性，不受
  掉落表 `quality_weights` 是否配置的影响；游戏层若已经按 T-N2-1/T-N2-2 给 `item.quality_definition
  .affix_count`/`item.affix` 配置了真实数据（`data/_sample` 即如此，见本任务改动的
  `data/_sample/loot/loot.table.json` 判断记录），全部装备类掉落的随机数序列都会变化，不只是新增
  `quality_weights` 的那一条。真正"逐随机数字节不变"的旧数据只有两种：`item.quality_definition`/
  `item.affix` 表本身未加载（如既有单元测试的最小夹具），或已加载但目标品质的 `affix_count`
  未登记/为零。
- **数值设计落地阶段 N3 · T-N3-1（1.33.0）**：`skill.def` 新增 `use_condition`
  （`FieldKind.Expr`，可选）/`budget_note`（`FieldKind.String`，可选）两个字段，`cast_time`/
  `respects_gcd`/`cost` 三个既有字段改写描述（不改字段种类/必填性）；新增
  `Core.Rules.Common.SettlementEffectKinds`（结算类原语集合常量，`All`/`IsSettlement(string)`）。
  编辑器技能编辑界面若需要为 `use_condition` 渲染 Expr 输入控件，可复用与 `ai.rotation.entries[]
  .condition` 同一套 `self`/`combat`/`target` 分组补全；`budget_note` 是普通多行文本输入。两个
  新字段均只是登记 schema，运行时消费（施法管线使用条件检查、`SkillBudgetAnalyzer`）留给后续
  任务（T-N3-4/T-N3-9）。
- **数值设计落地阶段 N3 · T-N3-2（1.33.0）**：`school_damage`/`heal`/`periodic_damage`/
  `periodic_heal` 效果参数新增 `scaling: [{stat, coefficient}]`（可选，权威写法，取代旧单字段
  `scaling_stat`/`coefficient`，允许多条求和）与 `base_curve_ref`（可选，引用新表
  `skill.base_curve`，取代 `base_value`）；`skill.def`/`skill.aura_def` `schema_version` 1→2，
  迁移自动把旧 `scaling_stat`/`coefficient` 补出等价 `scaling` 列表（旧字段原样保留）。编辑器
  技能效果编辑界面若已支持 `scaling_stat` 单选下拉，需要升级为可增删的缩放条目列表；
  `base_curve_ref` 可复用曲线表通用编辑控件（同 `item.armor_curve` 等既有曲线表）。
- **数值设计落地阶段 N3 · T-N3-3（1.33.0）**：`weapon_damage_pct` 效果原语改为"武器秒伤 ×
  一拍常数 × 百分比"，不再读取单次武器伤害（`IWeaponDamageQuery.GetWeaponBaseDamage`）；新增表
  `skill.budget_rule`（最小骨架，只含 `id`/`beat_seconds`，完整字段留给 T-N3-9）；`SkillOptions`
  新增 `BudgetRuleId`（缺省 `skill.budget_rule.default`）。**行为变化（迁移说明）**：旧语义"武器
  单次伤害（`(damage_min+damage_max)/2`）× 百分比"→新语义"武器秒伤（`item.weapon_dps_curve`）×
  一拍常数（`skill.budget_rule.beat_seconds`，未声明该表/记录时缺省 1.0）× 百分比"——数值会变，
  游戏侧已有的 `weapon_damage_pct` 技能百分比需要重新校准；新公式不再受武器速度影响单次挥击强度
  （秒伤已经是速度无关量）。编辑器技能效果编辑界面若展示 `weapon_damage_pct` 的 `pct` 输入控件，
  文案建议改为"武器秒伤 × 一拍常数的百分比"；`skill.budget_rule` 是编辑器新曲线/规则表编辑界面
  应当收录的新表之一（当前只有 `id`/`beat_seconds` 两个字段，T-N3-9 落地后字段会继续增加）。
- **数值设计落地阶段 N3 · T-N3-5（1.33.0）**：新增校验检查名 `skill_no_time_cost`（04 第 5
  节"无时间成本"警告，Warning 级、不可提升）——主动技能 `cast_time` 为零且 `respects_gcd` 为真
  （未声明为反应类）即命中，编辑器技能编辑界面/校验报告面板需要能展示这一新检查名（消息文案见
  `Core.Rules.Skill.SkillNoTimeCostWarningRule`）。`SkillOptions` 新增的
  `HasteAffectsActionTime`/`HasteStat`/`MinActionSeconds`/`MaxHastePct` 四个字段属运行时口味配置
  （C# 构造期参数），不是数据表 `FieldSchema`，不进编辑器数据编辑界面范围。
- **数值设计落地阶段 N3 · T-N3-6（1.33.0）**：`skill.aura_def` 的 `control` 光环效果新增可选
  `category`（`FieldKind.Enum`，取值 `stun|root|silence|disarm|fear|polymorph`，缺省不分类）；
  `creature.tier_definition` 新增可选 `control_immune_categories`（`FieldKind.Array<Enum>`，同一
  取值集合，与既有 `control_immune` 并存、互不覆盖）。编辑器技能效果编辑界面若展示 `control`
  效果的 `flags` 多选控件，需要为新增的 `category` 单选下拉补一个选项（六值，可复用与
  `interrupt_flags` 同款枚举控件）；生物分档编辑界面的 `control_immune` 开关旁需要新增一个可多选
  的"免疫控制类别"控件（同一份六值枚举，来源
  `Core.Rules.Common.ControlCategoryValues.All`）。两个字段均为纯新增可选字段，不升
  `schema_version`、不需要迁移函数，旧数据/旧存档不受影响。
- **数值设计落地阶段 N3 · T-N3-8（1.33.0）**：`target.chain_def` 新增可选字段
  `overflow_policy`（`FieldKind.Enum`，取值 `truncate|split|cap`，缺省 `truncate`）——编辑器目标链
  编辑界面的 `max_targets` 输入框旁需要新增一个三选一下拉（复用现有 Enum 单选控件即可，来源
  `Core.Rules.Targeting.TargetSchemas.OverflowPolicyValues`）；纯新增可选字段，不升
  `target.chain_def` 的 `schema_version`、不需要迁移函数，旧数据/旧存档不受影响。
- **数值设计落地阶段 N3 · T-N3-9（1.33.0）**：`skill.budget_rule` 表在 T-N3-3 最小骨架
  （`id`/`beat_seconds`）基础上新增九个可选字段——`periodic_time_discount`（Number）、
  `cooldown_premium_curve`/`range_discount_curve`/`cost_premium_curve`（三条 04 第 3.6 节通用
  断点表，`CurveAxis.Value`）、`player_bandwidth`/`monster_bandwidth`/`player_hard_cap`/
  `monster_hard_cap`（Number）、`control_category_weights`（Object，六个具名可选 Number 子字段）；
  编辑器技能预算规则编辑界面需要为这九个字段补对应控件（三条曲线复用既有断点表编辑控件，其余为
  数值输入框，`control_category_weights` 可复用 `ControlCategoryValues.All` 六值渲染六个滑块/
  输入框）。新增两条检查名：`skill_budget_deviation`（Warning，按 `budget_note` 有无分组
  "已确认"/"待确认"）、`skill_budget_hard_cap_exceeded`（Error）、`item_grant_value_exceeds_share`
  （Warning，落在 `item.template`）——均为 04 第 5 节数值类校验项分级表既有登记行的检查名补录，
  非新增校验维度。全部新增字段均为可选、`schema_version` 不递增，旧数据/旧存档不受影响；三条
  新检查规则默认注册但因未接入 `sim.anchor`（阶段 N6）而整体不产生任何问题，见
  `core/rules/skill/README.md` 判断记录 55、`core/carriers/item/README.md` 判断记录 26。
- **数值设计落地阶段 N4 · T-N4-1（1.34.0）**：`prog.level_curve` 新增可选字段
  `talent_points`（`FieldKind.Int`，缺省 0，`>= 0`）；`prog.xp_source` 新增四个可选字段
  `kind`（`FieldKind.Enum`，取值 `kill|quest|discovery`）、`base_curve_ref`（引用新表
  `prog.xp_base_curve`）、`level_diff_ref`（引用既有 `combat.level_diff_table`）、
  `once_key`（`FieldKind.String`）；旧字段 `base_xp`/`weight` 标废弃（保留一个版本周期，不删除）。
  编辑器等级曲线编辑界面需要为每级条目补一个"天赋点数"输入框；经验来源编辑界面需要为 `kind`
  补三选一下拉、为 `base_curve_ref`/`level_diff_ref` 补引用选择控件（分别指向新表
  `prog.xp_base_curve`/既有 `combat.level_diff_table`）、为 `once_key` 补文本输入框，并对
  `base_xp`/`weight` 两个输入框加"已废弃"视觉标注。全部新增字段均为纯新增可选字段，两张表
  `schema_version` 均不递增，旧数据/旧存档不受影响。
- **数值设计落地阶段 N4 · T-N4-4（1.34.0）**：`creature.tier_definition`/`diff.tier` 各新增
  可选字段 `xp_multiplier`（`FieldKind.Number`，缺省 1）；任务/遭遇/成就奖励 `rewards` 子结构
  新增可选字段 `xp_equivalent`（`FieldKind.Number`）/`level`（`FieldKind.Int`），旧字段 `xp`
  标废弃（保留一个版本周期，不删除）。编辑器生物分档/难度档编辑界面需要各补一个"经验倍率"输入框
  （缺省 1）；任务/遭遇/成就奖励编辑界面需要补"经验当量"/"等级"两个输入框，并对 `xp` 输入框加
  "已废弃"视觉标注（两者同时填写时以 `xp_equivalent` 为准）。全部新增字段均为纯新增可选字段，
  各表 `schema_version` 均不递增，旧数据/旧存档不受影响。
- **数值设计落地阶段 N4 · T-N4-6（1.34.0）**：新增两张断点表 `econ.value_curve`（物品等级 →
  基准价值）与 `econ.gold_base_curve`（等级 → 金币基数）；`econ.vendor.sell_items[].price_amount`
  由必填改为可选（未填按价格公式计算，填了手填优先）。编辑器需要为两张新表提供曲线编辑界面
  （复用既有断点表曲线编辑控件）；`sell_items` 出售条目编辑界面需要把 `price_amount` 输入框改为
  可选（留空提示"缺省走价格公式"），并对手填值与公式值偏离超带宽的条目提示 `econ_price_deviates_
  formula` 警告。全部改动均为纯新增表/纯新增可选字段语义放宽，既有表 `schema_version` 不递增，
  旧数据/旧存档不受影响。
- **数值设计落地阶段 N4 · T-N4-8（1.34.0）**：新增事件 `economy.charged`（字段
  `unitId`/`currencyId`/`amount`/`reason`），已登记进 `found.event_catalog.json` 并生成
  `EventKeys.EconomyCharged`；T-N4-7 新增的 `economy.currency_overflow`（字段
  `unitId`/`currencyId`/`discarded`）同时补登记并生成 `EventKeys.EconomyCurrencyOverflow`。
  `IEconomyHost.TryPay` 旧无 reason 三参数签名从本版本起也会在原子扣费成功时发
  `economy.charged`（此前该签名不发任何"扣费"事件，只有 `Add` 间接发 `currency_changed`）——
  依赖"该签名不发事件"这一旧行为的编辑器侧逻辑需要重新核对。新增带 reason 的
  `TryPay(Id,Id,long,string)` 重载与 `CurrencyGranters.ViaEconomyHost` 静态工厂均为纯新增 C#
  API，不涉及数据表/编辑器控件改动。
- **数值设计落地阶段 N5 · T-N5-2（1.35.0）**：`core/foundation/expr` 新增只读遍历入口
  `ExprReferenceCollector.Collect(ExprNode)`（收集语法树里全部引用节点）与兜底 schema
  `PermissiveExprSchema`——编辑器等工具若需要枚举一段 Expr 文本里出现过的全部 `group.key` 引用
  （不关心是否已在自己的登记表里注册），可直接复用这两个类型，不必再各自实现一套遍历/兜底逻辑
  （同 [ADR-0020](architecture/adr/0020-表达式词法器纳入公开契约.md)"不允许派生出第二套"的
  一贯取舍）。均为纯新增公开类型/成员。
- **数值设计落地阶段 N5 · T-N5-3（1.35.0）**：新增 `Presentation.Assembly.
  NumericValidationRuleCatalog`/`NumericValidationRuleDescriptor`（04 第 5 节数值类校验项分级表
  的只读集中登记清单）与 `ContentValidationAssembly.NumericRules`——编辑器"校验设置"/"数值规则"
  一类面板可直接读取本清单展示全部数值规则的检查名、级别、是否不可提升、所属数值域分组、是否依赖
  尚未接入的仿真锚点，不必自行硬编码这份清单。`toolchain/validator --json` 的 `rules[]` 每项新增
  `category`/`group`/`check_names`/`requires_anchor`/`enabled` 五个字段（详见
  `toolchain/README.md`），供消费方按数值域分组展示、按 `enabled` 判断"这条规则当前是否真的会产出
  问题"。均为纯新增公开类型/成员与纯新增 JSON 字段。
- **数值设计落地阶段 N5 · T-N5-4（1.35.0，文档版本 v2.16）**：编辑器产品文档
  （`docs/编辑器/编辑器产品文档.md`/`.html`）第 4.1 节补齐 T-N3-9/T-N2-4 落地的两个 Analyzer
  契约面（`SkillBudgetAnalyzer`/`EquipmentScoreAnalyzer`）与 T-N5-2/T-N5-3 落地的数值规则集中
  登记清单/`ExprReferenceCollector`；第 5.8 节新增"内容覆盖仿真离群值列表"行，标注依赖阶段 N6
  （`core/sim`）产出，当前仅为契约意向。纯文档变更，不涉及代码。HTML 版同步新增内容；HTML 自
  v2.6 起累积的历史缺口（v2.7～v2.15 期间第 4.1 节新增的其余契约面尚未回填 HTML）不在本条改动
  范围，已在 HTML 头部说明如实标注；该缺口已由 T-N5-5（同版本）回填，详见下方正文"文档"小节。
- **消费方反馈第 45/46/47 条（1.38.0，[回复文档](docs/消费方反馈/消费方反馈-2026-09-17-编辑器-第45-47条.md)）**：
  第 45 条——`IDataRegistryView.TryGet`（带默认实现）、`Core.Foundation.DataRegistry
  .TolerantRegistryView`（`Wrap`/`IsDegraded`/`MissingTables`/`WasMissing`），
  `ItemBudgetCurve.BuildStatBudgetInfo`/`EquipmentScoreAnalyzer.Score`/`SkillBudgetAnalyzer
  .Analyze`/`ComputeGrantValue`/`ExpectedStatCalculator` 等只读分析入口阻断态下不再抛异常，
  `EquipmentScoreResult`/`SkillBudgetResult`/`ExpectedStatCalculator` 新增
  `IsDegraded`/`MissingTables`；`IBudgetSolver.Solve`/`SkillDefCache` 等运行期入口不变。第 46
  条——`FieldSchema.WithDeprecated`/`IsDeprecated`/`DeprecatedSince`/`ReplacedBy`/
  `DeprecationNote`，新增检查名 `field_deprecated_metadata`，`--list-tables --json` 的
  `field_meta` 新增 `deprecated` 键。第 47 条——**行为变更**：字段级"Id 语法非法"诊断从
  `field_type` 拆出，改报新检查名 `field_id_format`；`SkillBudgetValidationRule`/
  `ItemGrantValueExceedsShareRule` 补齐异常兜底，新增检查名
  `skill_budget_record_unparseable`/`item_grant_value_unparseable`。编辑器产品文档 v2.17 同批
  补齐第 4.1 节两行契约面，`Editor.Core.Validation.TolerantRegistryView`/
  `DeprecatedFieldHints` 两个自建包装/清单均可退役。
- **ADR-0041（1.46.0，破坏性变更）**：第 45 条新增的 `IDataRegistryView.TryGet`（含
  `Core.Foundation.DataRegistry.DataRegistry` 的显式覆盖）实测证明恒返回 `true`（哪怕记录本就
  不存在），返回值语义修正为"是否找到记录"；`TolerantRegistryView.Get`/`TryGet` 同步调整内部
  判定算法。方法名/签名不变，编辑器若直接使用这两个入口的返回值判断"是否找到"，需按下方
  "[1.46.0]"正文"破坏性变更"小节自查。
- **ADR-0047（[Unreleased]，新增跨进程能力，续第 71 条反问）**：第 71 条反问原本想问清楚编辑器
  是否需要跨进程读取运行期校验结果——设计层本次不再等答复直接落地一个新的可选出口：运行期宿主
  支持通过命令行参数 `-gfValidationReportPath <文件路径>` 或环境变量
  `GF_VALIDATION_REPORT_PATH` 让编辑器指定一个落盘文件路径，宿主每次校验（启动/热重载，不论
  通过/阻断）都会原子写一份结构化文档到该路径，字段形状（逐条问题）与 `toolchain/validator
  --json` 的 `issues[]` 完全一致，外层另带单调递增序号与触发来源（`startup`/`hot_reload`）。
  编辑器若确实需要跨进程读取运行期校验结果，可以启动游戏进程时传入该参数/环境变量，之后监视/
  轮询该文件；若编辑器其实不需要（第 71 条反问的答案是"走的是命令行出口"），本条不产生任何影响
  ——不设置选项时宿主行为与本条之前完全一致，不产生任何文件。详见下方"[Unreleased]"正文与
  [ADR-0047](architecture/adr/0047-运行期校验报告落盘出口.md)。
- **ADR-0055（[Unreleased]，消费方反馈第 78 条根治）**：第 78 条指出 ADR-0047 的落盘信封缺"这次
  校验确切针对哪一张表"——热重载入口本就持有确切的表名，却只用于日志文本插值，编辑器只能退回去
  解析 `[DataHotReload] 热重载 "<表名>" ...` 这行日志文本才能知道确认提示对应哪张表，与该出口
  自身"日志文本永远不是契约"的既有立场矛盾。本次落盘信封顶层新增可选字段 `table`：`hot_reload`
  来源固定是被重载的确切表名，`startup`来源固定为 JSON `null`（不是空字符串/`"all"`）。**纯
  加法**：既有四个字段一个不改，`WriteIfConfigured`/`Write`/`BuildJson` 均以新增重载方式提供，
  未设置落盘路径时依然不产生任何文件。编辑器交付后可以删除原有解析该行日志文本判定表名的路径，
  改读落盘 JSON 的 `table` 字段。详见下方"[Unreleased]"正文与
  [ADR-0055](architecture/adr/0055-运行期校验报告落盘出口补表名字段.md)。
- **手感落地 M4（1.96.0）**：数据表字段只增不改，编辑器按需跟进——手感档案新增 `poise_damage`、`poise_recover_per_s`、`poise_recover_delay_ms`、`poise_recover_mode`、`poise_break_reset_ms`、`air_reaction_cap`、`launch_height_cap`、`launch_body_scale`、`air_stun_until_land`、`land_hold_ms`；`found.input_action` 新增 `control_space`；`found.grace_condition` 新增三行框架内置条件；`display.anim_set` 新增 `blend_ms`/`blends`；`world.map` 新增 `terrain`（矩形、凸多边形、高度场）；目标链形状新增 `height_offset`；新事件 `combat.poise_changed`、`combat.poise_recovered`、`unit.landed`；表达式新增 `event.aim_*` 上下文；技能时间线新增 `charge.value_scale{min, max}`（蓄力效果值倍率）与 `is_attack`（显式声明动作是否带攻击）；标准假人姿势集新增可选键 `cast.quick`、`cast.heavy`（施放点变体，经 `display.weapon_style.cast_anim_override` 指到）；新增默认接口成员 `IBufferedIntentSink.CanHandle`、`IInputBufferQuery.TryPeek`/`TryConsume` 的跳过谓词重载、`IProjectileHitHook.ValueScale`，以及目标选项 `TargetingOptions.TargetRadius`/`MaxTargetRadius`（缺省关闭）。详见下方 `[1.96.0]` 正文。
- **非手感已知限制清扫（1.96.0，[ADR-0139](architecture/adr/0139-非手感已知限制清扫的契约与行为决定.md)）**：数据与契约只增不改，编辑器按需跟进——`item.slot_definition` 新增可选布尔字段 `has_appearance`（缺省 `true`，写 `false` 的槽位物品不要求 `display.equip_visual` 行）；表达式可读事件 `combat.attack_avoided`、`combat.hit_confirmed` 的 `attackInstanceId` 现在可经表达式读取（为空时查不到）；`LootExpectedCurrencyOutcome.ExpectedRoundedAmount`（含取整与非正值跳过的精确货币期望，线性的 `ExpectedAmount` 保留，"理论与观测"面板应读前者）与 `LootTableAnalyzer.ExpectedAffixInclusion` 的 5 参数重载（大词缀池精确解，计算量预算 `exactMaxWork`）；`item.set` 数据重载后套装门槛立即对账（热重载后不必再等装备变化才见效果）；`simrunner fight` 子命令与 `--fight-log` 日志文件（`{schema_version, truncated, entries}`）；游戏模板 `GameOptions.PathFailurePolicy`/`BlockingChangePolicy`。详见下方 `[1.96.0]`"非手感已知限制清扫"。

## [Unreleased]

### 新增

- **参考界面皮肤包与真实素材回归（[ADR-0152](architecture/adr/0152-参考界面皮肤包与真实素材回归.md)，手感落地 M5-S6 后续）**：全部可选、缺省皮肤与基线逐位不变。①新增样例包 `assets/_reference_fantasy/`（通用奇幻风、本地出图 + 可复现后处理脚本 `_source/`；皮肤包清单全元素含可选项、样例装备图标与纸娃娃静态层），不替换占位皮肤、不进内容同步与发布打包；回归 `toolchain/tests/test_reference_skin_pack.py`（零错误零警告、可选缺失为空、真实美术不变量与负例）与 PlayMode `ReferenceSkinPlayModeTests`。②走真实美术发现并修复五处缺陷：背包面板物品格不认 `cell_size`（曾按 100 x 100 画出；带皮肤的重载现在显式设定行高与格子尺寸）；图标无透明像素不再被报成"主体贴边"（贴边报包围盒）；品质框中心区报最大 alpha 与像素位置；布局 `cell_size`=0 在背包面板里也取槽位框原生宽度（新增公开 `UiPanelLayout.ResolveCellSize`，此前背包当成 32 px）；`EquipWardrobeScene` 新增可选 `SkinRef`（缺省占位皮肤，行为不变）。适配器包新增对 `Adapter.Unity.Tests.LabHost` 的测试可见性。
- **参考界面皮肤包第二轮：层序、锚点对位、背包面板、换肤缓存、提示框与拖放（[ADR-0152](architecture/adr/0152-参考界面皮肤包与真实素材回归.md) 决策 7～11）**：全部可选，缺省行为逐位不变。①`display.equip_visual` 新增可选 `behind_directions`（方向槽位 id 列表），命中方向的装备层画在身体之后（运行期合成与预览区同一份声明，镜像方向跟随），参考武器声明背面两档；纯新增可选字段，不升 schema 版本。②纸娃娃静态层可选声明 `anchors.json` 的 `directions.<方向>.grip`，预览区按它与身体同名挂接点对齐（对位规则拍板：预览读锚点；运行期合成仍居中叠放，是已知限制）；导入校验新增 `equip_anchor_out_of_bounds`/`equip_anchor_invalid`/`equip_behind_direction_invalid`（错误）与 `equip_opaque_coverage_low`（警告，阈值在 `skin_manifest.json` 的 `icons.coverage`/`paperdoll.static_layer_coverage`）；参考包的弓图标重出（原图几乎透明）。③背包面板宽高随内容（缺省仍 260 x 260），行高/按钮宽显式设定（修复无皮肤重载行高与按钮宽为 100），行标签显示 `name_key` 本地化名（`UiVisuals.L10n`/`ItemName`，取不到退回 id 短名）。④新增 `UiVisuals.SwitchSkin`/`SkinChanged` 与 `UnityResourceLoader.InvalidateSpriteSetAnchorsCache`：运行期换皮肤时图标/层图/精灵集锚点缓存同步失效（含在途加载），背包与装备面板整体重建，`EquipWardrobeScene.SwitchSkin`。⑤新增 `ItemTooltipBuilder`（presentation）与 `UiInteraction`/`UiTooltip`/`UiDragController`/`UiItemCell`：悬停提示框、从背包拖到装备槽位穿上（`target_ok`/`target_blocked`）、把已装备槽位拖回背包卸下；`WardrobeStage` 新增 `Bag`/`L10n`/`AddToBag`/`EquipInstance`。（第 2 轮收口：物品名/槽位名统一走 `ItemTooltipBuilder.ItemName`/`SlotText`、占位数据集背包格子 `cell_size: 48`）
- **装备面板与界面资源契约清单（[ADR-0149](architecture/adr/0149-装备面板与界面资源契约清单.md)，手感落地 M5-S6，手感设计/08）**：全部可选、缺省皮肤逐位不变，既有基线与数据集哈希不动。①**皮肤包有运行期消费方**：适配器新增 `UiSkinPack`/`UiVisuals`/`UiSkinManifest`/`UiSkinGallery`（槽位框与状态、品质框、预览区背景、面板九宫格底图、按钮九宫格状态图、主题颜色与字体；回落链与回落记录）；`UiSkinOverride.ButtonSprites`（新增属性）与 `UiSkin.ButtonSprites`；`skin.default` 不安装任何覆盖。`toolchain/resource_layout_map.json` 新增 `ui` 映射，皮肤包随内容同步进 `StreamingAssets`。②**装备面板**：`UiPanel.Equipment`（`ui_layout_definition.panel` 新增枚举值 `equipment`，行可选，缺省内置布局）、生产类 `EquipmentViewModel`（`PresentationAssembly.Equipment`）、适配器 `EquipmentPanel`（槽位网格 + 纸娃娃预览区，方向可切换）与 `PaperdollPreview`；背包面板新增带皮肤与资源的 `Construct` 重载（物品格：槽位框、图标、品质框、数量），既有签名不变；`UnityResourceLoader` 无公开面变化。③**一份机器可读的界面资源契约清单** `toolchain/asset_import/skin_manifest.json`：皮肤元素（路径模板、必备/可选/仅占位必备、尺寸与比例、同尺寸组、透明度、九宫格边框令牌、状态变体）、主题令牌、图标类别与规格、纸娃娃静态层/逐层剪辑/身体剪辑与帧对齐规则；导入校验、占位皮肤生成器、人读清单（`import_assets.py skin-checklist`）与引擎侧完整性用例读同一份。④**导入校验补全具名诊断**（每个一个反例）：皮肤包 `equip_skin_state_missing`/`_theme_token_missing`/`_theme_color_invalid`/`_image_unreadable`/`_size_invalid`/`_size_group_mismatch`/`_nineslice_invalid`/`_alpha_invalid`，图标 `equip_icon_alpha_invalid`（从 `equip_icon_size_invalid` 拆出透明度/贴边），纸娃娃 `equip_layer_image_invalid`/`_frame_count_mismatch`/`_frame_size_mismatch`/`_atlas_mismatch`；皮肤图内容缺陷为错误级，缺失仍是警告。⑤**实验室换装场景引擎形态**：`EquipWardrobe`（内核，数据生成的衣橱脚本与报告）、`WardrobeStage`、`EquipStepRecord` 面板快照（不进度量组）；适配器 `EquipWardrobeScene`/`EquipWardrobeRunner`/`WardrobeCarouselModel`（本地报告与拼图，只留本地）。`data/_sample` 的 `ui_layout_definition` 新增一行 `equipment`。遗留：穿脱音效 `equip_sfx_ref` 运行期播放、占位装备集的副手与挂点型外观夹具（见 ADR-0149）。
- **玩家的手感入口与成熟度验证记录（[ADR-0146](architecture/adr/0146-玩家手感入口与成熟度验证记录.md)，手感落地 M5 档案与成熟度）**：①`arch.class` 新增可选 `feel_archetype_ref`/`feel_ref`，玩家按职业行取体型原型与角色手感（解析路径与生物模板同一条；未声明时行为与此前逐位一致；读档后在有职业行声明手感引用时玩家缓存重算；`CarriersFeelOptions.Fields` 新增可选属性）。②新增数据表 `feel.validation`（游戏自己的成熟度验证记录，框架不提供任何行）与加载期规则 `FeelMaturityRule`：档案行标 `validated` 而没有覆盖它的记录（指向该行、`profile_version` 一致、四个评分维度各不低于 4、格子非空）报错；新增 `FeelMaturity`/`FeelValidationRecord`/`FeelValidationLedger`（按格子的已验证状态）；框架自带档案行永远是 `experimental`。③字段登记新增落地状态 `FeelFieldStatus`（`active`/`planned`）与扫描测试：8 个无消费方的字段（`sprint_speed_ratio`、`stride_scale`、`start_blend_ms`、`stop_blend_ms`、`lean_deg_per_accel`、`trail_enabled`、`afterimage_enabled`、`trail_ref`）标 `planned`，被实现后由实现者摘掉。④字段登记表由登记生成：`FeelFieldCatalogDoc` 与 `feellab fields [--check]` 生成 `architecture/手感设计/05a_字段登记表.md`，测试逐字节比较。⑤游戏自有手感字段的扩展位：`FeelFields.Extend`（`game.` 前缀）与 `FeelSchemas.RegisterAll(registry, fields)`/`FeelSchemas.BuildAll`，注册顺序无关。均为纯加法，数据缺省行为、既有基线与数据集哈希逐位不变。
- **命中几何与时序（[ADR-0144](architecture/adr/0144-命中几何与时序.md)，手感设计/03 第 2.2～2.7 节，M5-S2a）**：全部可选、未声明时逐位不变，schema 版本不动。①**受击半径**：受击组新增可选字段 `hurt_radius_scale`（目标受击半径 = `unit_body_radius` × 它，命中形状与该圆相交即命中，接触点落在目标圆面上不再在体内）；装配选项 `CarriersFeelOptions.HitRadiusFromFeel`（缺省关闭，打开会改变既有命中结果）；`TargetingOptions.MaxTargetRadiusProvider`（动态上界）与默认接口成员 `ITargetHost.TargetHitRadius`。②**无敌窗口前置检查上移为结算第 0 步**：`Resolver.InvulnerabilityGate`/`CombatHost.InvulnerabilityGate`（生产装配根接上技能模块的 `SkillHost.BlocksHitByInvulnerability`），目标选择式、范围效果、投射物与时间线路径统一经过；技能行新增可选 `ignores_invulnerability` 豁免；实验室既有基线逐字不变。③**手感来源**：技能行新增可选 `feel_ref`（优先级 技能 > 武器 > 角色，落在 05 第 6 层，法术等非时间线技能命中时取用，`HitFeelOptions.SkillFeelRef`）；`hit`/`release` 标记 `args.feel_ref` 分段覆盖；`timeline.charge.feel_scale` 蓄力对顿帧/击退/击飞的缩放（`HitFeelInput` 新增 9 参数构造重载与 `HitFeelScale`；`IFeelJudgingSource.ResolveJudgingWithAction` 默认接口成员）。④**落点技能**：`timeline.hit_anchor`（`caster`｜`ground_point`）让 `ground_target` 技能经地面坐标施法请求进入时间线；不声明保持既有警告行为。⑤新增加载期校验 `skill_feel_ref_conflict`（错误）、`timeline_hit_anchor_without_ground_target`、`timeline_marker_feel_ref_ignored`（警告）。⑥文档整理：03 更正 2.2/2.3 节无敌矛盾、写明"不设 `supportsSweep`、一律子采样"、补命中路径与采样步长与空战字段索引。实验室新增脚本 `feel_hit_geometry`、`feel_hit_charge_scale`（独立数据根，判断记录 61）。
- **输入层补全（M5-S1，ADR-0143，手感设计/01、00 第 7 节）**：全部可选、缺省逐位不变，除下条"变更"外既有基线不动。①摇杆处理有了消费方：`InputMapHost` 按 `found.input_action` 的 `dead_zone`（径向死区重标度）、`response_curve`（`linear|expo|custom:<id>`）、`smoothing_ms`（只作用于下降沿，一次按下至少一个 tick 满幅）处理模拟绑定，玩家覆盖随设置文件持久化。②新输入类别 `jump`（缺省优先级 35）：跳跃缓冲就是输入缓冲；土狼时间走宽限机制，新增内置条件 `input.grace.builtin_grounded`，`IVerticalMotion` 新增默认成员 `IsLedgeFall`/`JumpFromLedge`（走出平台边缘的下落按地面起跳、不占空中跳跃次数）；可变跳高 `jump_cut_ratio` 与 `IVerticalMotion.CutAscent`。③蓄力补全：按住达到 `charge.max_ms` 自动释放并发 `input.charge_ready`（事件，不是时间线标记）；`charge.below_min: release|cancel`，取消时发 `input.buffer_dropped{reason: charge_below_min}`（`BufferDropReason.ChargeBelowMin`）。④点按/按住变体：`found.input_action.hold_skill_slot`。⑤取消窗口与连招窗口新增 `requires: any|hit|whiff`，取消窗口新增 `into` 目标白名单。⑥按住维持：`timeline.active_until_release{max_ms}`（`sustain_start`/`sustain_end` 标记，`IActionStateQuery.IsSustained`），格挡的判定仍归受击裁决。⑦AI 经缓冲提交：`CarriersFeelOptions.AiIntentsThroughBuffer`（缺省关闭，开启带一个 tick 延迟）与 `IAiCastRouter`/`AiHost.CastRouter`。契约一律加法：新的构造重载、默认接口成员、枚举值追加在末尾。新增事件登记 `input.charge_ready`。设计文档 `手感设计/01_输入与动作.md` 字段表与时间线块对齐实现，00 第 7 节土狼时间改为输入层原生支持。
- **默认手感模板（[ADR-0142](architecture/adr/0142-默认手感模板.md)，纯加法）**：新增可选框架数据根 `data/_feel_templates/`，出厂五套完整预设模板 `feel.preset.tpl_classic`（经典目标选择）、`tpl_agile`（敏捷动作）、`tpl_heavy`（厚重动作）、`tpl_horde`（爽快割草）、`tpl_precise`（精准硬核），成熟度 `experimental`、带 `description`，数值取自 05 第 9 节试调起点与字段范围；每套配反馈档案 `feedback.impact_profile.tpl_*`（`light/medium/heavy/massive` 四档 × 命中/击杀，加回避与挥空）、镜头档案 `camera_profile.tpl_*`，资产全是占位引用（`vfx.placeholder_hit_spark`），游戏只换美术/界面/音频；武器原型补齐长柄、投射、法器三个类行（`feel.weapon.polearm/ranged/catalyst`）与每套模板对匕首/单手剑/巨剑/长柄/投射/法器的 30 行 `feel.weapon.tpl_*` 及显示档案。既有预设、武器行、基线与数据集哈希不变；该根随 dist、framework-data 包与实验室根发出，不同步进游戏的 StreamingAssets，游戏需显式装载。新增门禁步骤 `validate_feel_templates_data`；实验室新增五个标准脚本 `feel_tpl_*`（各六格基线，`suite` 530 → 560 格，`invariants` 741 → 776），台架改为按玩家手感表的 `impact_profile_ref` 取反馈包（既有预设无该字段，行为不变）。已知限制见 ADR-0142。
- **手感实验室人手试玩宿主（[ADR-0141](architecture/adr/0141-手感实验室人手试玩宿主.md)，手感设计/06 第 1、4 节）**：真人可以在 Unity 里直接玩实验室的三个平面组合（`2d_action` 俯视精灵、`2_5d_action` 固定俯仰精灵、`3d_action` 固定俯仰模型各一个薄场景，`Assets/Framework/Scenes/LabPlayground_*.unity`，不进场景清单）。内核新增可单步推进的 `LabSession`（`LabHost.Start`/`LabRunner.StartLive`，脚本回放是同一份循环的特例）；输入脚本格式版本升到 5，新增事件种类 `spawn`、`clear_dummies`、`preset`、`loadout`、`override`、`clear_overrides`、`marker`（旧脚本逐字节不变）；面板每个影响逻辑的操作都落成事件，整局录成本地脚本（`lab/out/playground/`，被忽略规则覆盖），无头 `feellab run` 重放的逻辑组指纹逐字节一致（内核与引擎两侧各有用例）。引擎侧新增 `EngineLabStage` 试玩模式（缺省关）、`LabPlayground` 控制器与屏上面板（F1 开关：场景控制、预设与 A/B、三项响应指标与帧耗时、录制）、`LabLiveInput`/`LabLiveModel`/`LabEffectFilter`、`LabPlaygroundSceneBuilder`；`UnityViewFactory.PlayLocomotionClip`（只增，生产装配入口不调用）。指南见 `lab/README.md`「人手试玩指南」与判断记录 64；调参面板、帧数据时间轴、轨迹叠层、评分不在本期。
- **受击反应扩展（[ADR-0145](architecture/adr/0145-受击反应的硬直保护期时长公式命中类别与倒地起身阶段.md)，手感落地 M5-S2b）**：全部可选、缺省逐位等价此前行为。①手感字段新增 20 个（受击方 `stagger_grace_ms`/`stagger_grace_cap`/`getup_ms`/`getup_invuln_ms`/`guard_arc_deg`/`guard_damage_scale`/`guard_parry_window_ms`/`block_*`/`glancing_*`/`parry_*`，攻击方 `hit_stun_scale`/`knockback_duration_ms`/`crit_*`），均有生产消费方；②`HitFeelOptions.HitStunReactionMultipliers`（反应类型 → 硬直倍率，缺省空表）；③新增事件 `unit.knocked_down`/`unit.getup_started`/`unit.getup_finished`（事件目录 117 → 120），`combat.reaction_applied` 追加 `stunTicks`/`downedTicks`/`getupTicks`，`combat.hit_confirmed` 追加 `reactionDetail`/`attackerReaction`（构造函数新增重载，旧重载保留）；④契约新增 `IDefenseArbiter`/`DefenseVerdict`、`IGuardStateQuery`/`GuardState`、`HitReactionDetail`，`IHitReactionQuery` 与 `IActionStateQuery` 追加带缺省实现的成员，`CombatOptions.DefenseArbiter`；⑤时间线标记 `guard_start`/`guard_end`（成对校验 `timeline_guard_*`）；⑥实验室：独立数据根 `lab/fixtures/data/reaction/`、脚本 `feel_react_knockdown`/`feel_react_grace`/`feel_react_guard`、条件度量组 `reactionext`，既有基线不变。
- **默认手感模板接进人手试玩（ADR-0141 x ADR-0142）**：试玩宿主与三个试玩场景的数据根带上模板根，预设列表出现五套 `tpl_*` 预设（缺省预设不变）；切换预设时反馈包随玩家手感表的 `impact_profile_ref` 切换（`FeelRig` 逐次取包，没有该字段的预设逐位不变）；四套模板的反馈档案变体补 `camera.shake_profile` 引用，震屏在试玩里可见（四份 `feel_tpl_*` 基线只在镜头提示一项重录），闪白因模板没有闪白特效行仍只计数。内核与引擎各有一条"重型与敏捷的攻击方顿帧取自模板数据且不同"的验收。见 `lab/README.md` 判断记录 66。
- **按住维持的格挡动作（M5 合并后接缝，ADR-0143 × ADR-0145）**：带 `skill.tag.guard` 标签且声明了 `timeline.active_until_release` 的技能，按住维持期间算在格挡（`SkillHost.GuardStateQuery`，装配接给受击裁决宿主）；弹反窗口从进入维持起算；没有这类技能时与此前逐位一致。
- **镜头与音画反馈（[ADR-0148](architecture/adr/0148-镜头与音画反馈的合成上限玩家强度脚步材质与动画表现标记.md)，手感落地 M5-S5）**：①框架缺省规则 `combat.hit_confirmed → PlayImpact(from_feel)`（游戏没有任何 `PlayImpact` 规则时自动补，游戏规则优先，装配选项 `DefaultImpactRule` 可关）；②`camera_shake_cap` 改为相机侧合成上限（所有仍在衰减的冲击与震屏的合成幅度，方向冲击按向量合成）；③玩家强度四个全局系数 `feel.intensity.shake/impulse/flash/rumble`（0..1，缺省 1），在出口统一生效，规则驱动的震屏也受控；④`camera_distance_attenuation` 新增 `none`（缺省）与真线性 `linear:<跨度>`，旧数据的裸 `linear` 行为保持不衰减并提示迁移；⑤脚步单一来源（剪辑声明 `footstep` 标记则以标记为准，几何步幅事件对该单位抑制）与地图可选字段 `world.map.surface_materials` 提供材质；⑥动画表现标记消费方：`footstep`、`trail_start/trail_end`（拖尾特效与残影）、`fx:<id>`、`impact`（闪白对齐，变体 `flash.sync = impact_marker`），同名重复标记各自触发；⑦两个可选适配层能力 `ICameraZoomPunch`（变体 `camera.zoom_punch`）与 `IRumble`（变体 `rumble{strength, duration_ms}`），引擎端实现 `UnityCamera`/`UnityRumble` 与精灵残影；⑧实验室脚本 `feel_av_feedback`（独立数据根，既有脚本与基线不变）；`trail_enabled`/`afterimage_enabled`/`trail_ref` 三个字段落地状态改为 `active`。手感设计/07 正文按实现重写，02 第 1.13 节补两个可选能力。
- **姿势与动画契约落地（[ADR-0147](architecture/adr/0147-姿势与动画契约落地.md)，手感落地 M5-S4）**：全部可选、缺省逐位等价此前行为。①**受击反应驱动姿势**：世界装配了手感受击裁决（动作式）时，`AnimStateMachine`（新增带 `IHitReactionQuery` 的构造重载）由 `combat.reaction_applied`/`unit.knocked_down`/`unit.getup_started`/`combat.hit_confirmed`（格挡）驱动 `hit.light/heavy/knockback/knockdown/getup/block` 子键（`PoseRequest.Sub`、`PoseKeys.HitSub*`、`PoseContext.ToRequest(…, sub)`），霸体（反应为 none）不播受击，有硬直的反应保持到硬直结束；没装配时仍由伤害落地驱动。②**重映射动作的动画播放速率**：`action.started` 追加 `startupRate/activeRate/recoveryRate`（`ActionStartedEvent` 旧构造保留，事件目录字段表同步），`ClipPlaybackRates` 汇总各状态的剪辑播放速率，`IFrameAnimPlayer.SetSpeed`（默认接口成员）与骨骼剪辑播放器接收。③**步态呈现参数**：`stride_scale`（步幅速率，夹 0.5～2.0、取整 0.05）、`start_blend_ms`/`stop_blend_ms`（待机 ↔ 移动的交叉淡入时长）、`lean_deg_per_accel`（身体前倾，夹 45 度，`IRenderer3D.SetLean` 默认接口成员，只骨骼模型外形）有了生产消费方，字段登记里摘掉 `planned`。④**冲刺**：`MoveMode.Sprint`（枚举值追加在末尾），目标速度 = 属性速度 × `sprint_speed_ratio`（没写按 1），`move.sprint` 缺键先退 `move.run`。⑤**剪辑标记对外发布**：序列帧与骨骼角色外壳的剪辑标记对外发布（与 ADR-0148 同一机制，统一为 `IAnimMarkerEmitter.AnimMarker(entityId, 标记名)`，S4 自带的 `IClipMarkerEmitter` 在合并时并入）。⑥**技能时间线与剪辑标记一致性有了生产来源**：`display.anim_set` 剪辑条目新增可选加法字段 `duration_ms`，`DisplayClipMarkerSource`/`SkillClipConsistencyRule`（规则组装层，登记进校验目录），技能到剪辑的对应来自 `display.weapon_style.cast_anim_override` 与普攻映射。⑦**导入工具**：`import_assets check` 新增剪辑标记齐全（只对按清单发布的姿势集）、剪辑总时长与资源一致、技能时间线与剪辑一致三类检查；新增子命令 `bake-motion`（剪辑根位移采样 → `skill.motion_curve` 行）。⑧新数据表 `skill.motion_curve`（动作位移曲线，`curve: custom:<id>` 引用，运行期按固定步求值）。实验室新增条件度量组 `poseext`（表现类）、脚本元信息 `poseExt`、三个脚本 `feel_pose_react`/`feel_pose_armor`/`feel_pose_motion` 与独立数据根 `lab/fixtures/data/pose/`，既有基线与数据集哈希不变。
- **装备面板与界面资源契约清单（[ADR-0149](architecture/adr/0149-装备面板与界面资源契约清单.md)，手感落地 M5-S6，手感设计/08）**：全部可选、缺省皮肤逐位不变，既有基线与数据集哈希不动。①**皮肤包有运行期消费方**：适配器新增 `UiSkinPack`/`UiVisuals`/`UiSkinManifest`/`UiSkinGallery`（槽位框与状态、品质框、预览区背景、面板九宫格底图、按钮九宫格状态图、主题颜色与字体；回落链与回落记录）；`UiSkinOverride.ButtonSprites`（新增属性）与 `UiSkin.ButtonSprites`；`skin.default` 不安装任何覆盖。`toolchain/resource_layout_map.json` 新增 `ui` 映射，皮肤包随内容同步进 `StreamingAssets`。②**装备面板**：`UiPanel.Equipment`（`ui_layout_definition.panel` 新增枚举值 `equipment`，行可选，缺省内置布局）、生产类 `EquipmentViewModel`（`PresentationAssembly.Equipment`）、适配器 `EquipmentPanel`（槽位网格 + 纸娃娃预览区，方向可切换）与 `PaperdollPreview`；背包面板新增带皮肤与资源的 `Construct` 重载（物品格：槽位框、图标、品质框、数量），既有签名不变；`UnityResourceLoader` 无公开面变化。③**一份机器可读的界面资源契约清单** `toolchain/asset_import/skin_manifest.json`：皮肤元素（路径模板、必备/可选/仅占位必备、尺寸与比例、同尺寸组、透明度、九宫格边框令牌、状态变体）、主题令牌、图标类别与规格、纸娃娃静态层/逐层剪辑/身体剪辑与帧对齐规则；导入校验、占位皮肤生成器、人读清单（`import_assets.py skin-checklist`）与引擎侧完整性用例读同一份。④**导入校验补全具名诊断**（每个一个反例）：皮肤包 `equip_skin_state_missing`/`_theme_token_missing`/`_theme_color_invalid`/`_image_unreadable`/`_size_invalid`/`_size_group_mismatch`/`_nineslice_invalid`/`_alpha_invalid`，图标 `equip_icon_alpha_invalid`（从 `equip_icon_size_invalid` 拆出透明度/贴边），纸娃娃 `equip_layer_image_invalid`/`_frame_count_mismatch`/`_frame_size_mismatch`/`_atlas_mismatch`；皮肤图内容缺陷为错误级，缺失仍是警告。⑤**实验室换装场景引擎形态**：`EquipWardrobe`（内核，数据生成的衣橱脚本与报告）、`WardrobeStage`、`EquipStepRecord` 面板快照（不进度量组）；适配器 `EquipWardrobeScene`/`EquipWardrobeRunner`/`WardrobeCarouselModel`（本地报告与拼图，只留本地）。`data/_sample` 的 `ui_layout_definition` 新增一行 `equipment`。遗留：穿脱音效 `equip_sfx_ref` 运行期播放、占位装备集的副手与挂点型外观夹具（见 ADR-0149）。
- **实验室面板：调参、帧数据时间轴、轨迹叠层、评分（[ADR-0150](architecture/adr/0150-实验室调参面板与时间轴轨迹评分.md)，手感设计/06 第 3.4、4 节）**：纯加法，缺省逐位不变。①内核新增四个纯数据视图模型 `TuningPanel`/`TimelineModel`/`TrajectoryModel`/`RatingModel`（无头可测），人手试玩宿主新增分页 场景｜调参｜时间轴｜轨迹｜评分（F12 循环）。②调参每次改值落成一条覆盖事件：`ScriptEvent` 新增可选文本载荷（非空才序列化，旧脚本逐字节不变），覆盖枚举、文本、引用、布尔与列表字段；`clear_overrides` 的动作名是已登记字段名时只清该字段；A/B 两个槽位各持有自己的预设与覆盖组；作用域为全局、玩家或某只靶子。③"保存为预设"与"写回数据表"只写本地文件（`lab/out/playground/panels/`，不入库）：保存的预设下次开局自动可选，写回输出统一差异文件并经重新解析验证，算不出的项列人工清单。④录制新增接触点与法线、判定形状位姿（不进指纹）；轨迹叠层与时间轴只读录制；评分带预设版本、指纹哈希与设备条件存本地，可导出为 `feel.validation` 汇总行。⑤修两处已知限制：试玩舞台相机取模板 `camera_profile.tpl_*`（缩放取相对值、跟随平滑折成时间常数）；五个模板的打击反馈包补闪白（新增占位特效行 `vfx.placeholder_hit_flash`），`feel_tpl_*` 五份基线有意重录（只在表现组的其它操作序列多出闪白条目，逻辑组不变）。⑥`FeelRig` 的靶子清单改为宿主在场靶子表本身，交互式试玩运行中出的靶子也进位置样本，`MotionMetricGroup.target_track` 对靶子清单长度变化容错（既有脚本输出逐位不变）。口径与已知局限见 `lab/README.md` 判断记录 67 与「人手试玩指南」。
- **实验室补全：体型 × 武器矩阵、首次可见延迟、表现慢放、可选度量组（[ADR-0151](architecture/adr/0151-实验室补全矩阵延迟与慢放.md)，手感设计/06 第 3.3、3.5、4 节，M5-S7）**：全部可选、缺省逐位不变，既有 85 个脚本的基线 0 差异（判断记录见 `lab/README.md` 71）。①体型 × 武器矩阵脚本：`feellab matrix` 从数据枚举全部体型原型与武器原型（不含模板变体行），每个组合生成一个脚本与六格基线（当前 3 × 5 = 15 个）；体型经职业行 `feel_archetype_ref`（脚本 `meta.playerClass`），武器经第 0 tick 的 `loadout` 事件；`--check` 核对夹具与枚举一致。②输入脚本 `meta` 新增可选 `playerClass` 与 `extraMetrics`（未用到不序列化，格式版本不变）；五个可选度量组按声明出现——首次可见延迟 `latency`（逻辑 tick 差与表现毫秒，帧量化多出的一截落在 `[0, 表现帧步长)`）、攻击扩展 `attackx`（取消残留命中、连招接续成功率）、命中扩展 `hitx`（击退距离）、同时反馈 `crowd`（同 tick 发声与指令峰值、总数）、朝向量化 `facing`；另有六个 `feel_x_*` 脚本带规则算出的期望。③表现慢放：`feellab run --time-scale`、运行变体 `LabRunVariant.TimeScale` 与试玩宿主新增的"脚本回放"模式（可视回放夹具脚本，可切时间尺度含 0.25 倍、暂停、单步，不录制、不收真实输入），只缩放表现时钟，逻辑组逐字节不变。④引擎宿主度量新增剪辑切换序列与"输入到首次可见响应"毫秒。⑤`LabInvariants` 的手感装配透明性不变量不含经职业行取体型或用 `loadout` 叠加武器的脚本。⑥手感设计 06 的"首版落地勘误"一节并回正文后删除（见变更）。
- **M5 收尾遗留（[ADR-0153](architecture/adr/0153-m5收尾遗留穿脱音效副手与挂点夹具按住冲刺意图解析与异步加载驱动.md)，手感设计/08、01、02，M5-S8）**：全部可选、缺省逐位不变。①穿脱音效 `item.template.equip_sfx_ref` 有了运行期播放：新增表现层 `EquipSfxDirector`，订阅 `item.equipped`/`item.unequipped`，经与脚步、打击同一条出声通路（`IFeedbackSink.PlaySfx`，ADR-0148）在持有者位置各播一次；只有目录里某个模板声明了该字段时才装配，未声明零订阅零调用。②框架玩家移动意图解析器 `PlayerMoveIntentResolver`（`Core.Carriers.Unit`）：轴 + 可选的数据声明冲刺输入动作（`IInputBufferQuery.IsHeld`）→ `MoveRequest` + `MoveMode`；输入层 `button` 型且 `class` 为 `move` 的动作只记按住（`ActionDefinition.IsHeldTracked`、`InputBufferHost.IsHeldTracked`，不入缓冲、不写动作事件）；引擎侧三个宿主（`GameFoundationBootstrap`、`FrameworkResidentHost`、游戏模板 `GameBootstrap`）与实验室试玩宿主经它出请求；未声明冲刺动作时请求与此前逐位相同（对照用例）。③实验室新增副手与挂点装备夹具（独立数据根 `lab/fixtures/data/equip_ext`，`data/_equip` 不动）、换装脚本 `equip_ext_cycle`、条件度量组 `equip_offhand` 与按住冲刺脚本 `feel_pose_sprint_hold`，配导入校验、无头与引擎侧装备面板/纸娃娃/挂点用例；既有脚本基线与数据集哈希零差异。④修复异步加载换向原子性用例的偶发失败：根因是测试按帧数推进而后台解码按墙钟完成，修测试驱动（推进前先等在途加载落定），不放松断言；加载器只新增两个仅供测试用的钩子。口径与已知局限见 `lab/README.md` 判断记录 72 与 ADR-0153。

### 变更（破坏性，ADR-0039 授权）

- **【破坏性】手感档案输入组的摇杆处理三字段 `dead_zone`、`response_curve`、`smoothing_ms` 删除，改登记在 `found.input_action`（[ADR-0143](architecture/adr/0143-输入层补全.md)）**：这三个字段此前在档案输入组登记，却没有任何读取点（消费方是输入映射，读不到档案），写了也不生效，所以迁移不会让行为变化，只需要删数据行。**迁移**：改前 `feel.preset`（及其它 `feel.*` 表）里写 `"dead_zone": 0.15` 之类 → 改后删去这些键；需要摇杆处理的游戏改在对应轴动作的 `found.input_action` 行上写 `dead_zone`/`response_curve`/`smoothing_ms`，玩家可经 `InputMapHost.SetAxisProcessing` 覆盖（设置文件键 `input_axis_settings`）。仍带这三个键的旧数据表照常加载（容错，值被忽略）；`FeelFieldNames.DeadZone`/`ResponseCurve`/`SmoothingMs` 常量保留并标 `[Obsolete]`。
- **删除根运动（ADR-0147，破坏性变更，授权来自手感设计评审）**：`skill.def.timeline.motion.driver: root_motion`、运动仲裁的 `root_motion` 来源与适配层能力声明 `supportsRootMotion` 一并删除——根运动由表现帧累加剪辑根位移，模拟结果依赖表现帧率与引擎动画求值，无法确定性重放，也从未有适配层实现。**迁移**：旧数据写 `root_motion` 在加载期报错（校验检查名 `timeline_motion_driver_removed`，附迁移说明）；把剪辑根位移曲线用 `import_assets bake-motion` 烘焙成 `skill.motion_curve` 行，`motion.curve` 改为 `custom:<行 id>`、`motion.distance` 取烘焙报告给出的位移（身高倍数），并删掉 `motion.driver`（字段废弃，缺省且唯一取值 `code`）。公开类型（`ActionMotion.RootMotion` 枚举成员、`IRootMotionSource`、`MotionServices.RootMotion`、`CarriersFeelOptions.RootMotion`）为程序集接口兼容保留 `[Obsolete]`，行为已删除（赋值被忽略、遇到该值直接抛错）。同批删除从未实现的能力声明 `supportsSweep`（高速形状一律子步采样）、`supportsFootIK`，适配层可选能力只剩 `supportsCameraImpulse`。

### 变更

- **硬直不再是"内部控制光环"（ADR-0145）**：手感设计/03 与 ADR-0117 决策 6 写的"硬直落地为内部控制光环、受控制递减约束"从未实现，已订正为裁决宿主的硬直窗口，并以硬直保护期 `stagger_grace_ms` 取代递减作为防无限硬直锁的机制。05 第 9 节"硬直 150/230 ms"明确为受击方基础值的参考取值。
- **`camera_follow_lag_ms` 弃用，阻尼是唯一权威（[ADR-0148](architecture/adr/0148-镜头与音画反馈的合成上限玩家强度脚步材质与动画表现标记.md)）**：此前 `follow_lag` 换算成 `ICamera.Follow` 的平滑，与分轴阻尼叠成两级滞后；现在分轴阻尼为 0 的轴取 `follow_lag` 作为阻尼，不再另走平滑（字段保留，数据不破坏；依赖旧双级滞后的手感需要重新调阻尼）。
- **标定的基础移速只用于绝对值视图（ADR-0146）**：运动层的目标速度本来就是"相对倍数 × 单位移动速度属性"，标定的 `base_speed` 从不参与移动；文档、字段说明与 `FeelCalibration` 改写为"参考基础移速"，`FeelUnit.BaseSpeedSeconds` 标不推荐（当前无字段使用）。不改任何行为与数据。
- **手感设计文档整理**：05 删去没有消费方的 `units`/`requires` 行字段规定、手写字段清单改为引用生成的字段登记表、操作语义与层号按实现订正；06 第 6 节把成熟度升级（各项不低于 4）与真机手测及格线（3）分开写；README 与 ADR-0113 范围边界改为现状；05_对象模型与世界 第 6.2 节 `MovementState` 补四个运动字段。
- **【缺省行为变化，单列】蓄力动作按住超过上限时在上限处自动释放（ADR-0143 决策 5）**：此前按住超过 `charge.max_ms` 要等到抬起才开始动作，现在在按下后 `ceil(max_ms / 步长)` 个 tick 就自动释放；只影响"按住时长超过蓄力上限"的输入，未超过上限的蓄力、没有蓄力声明的动作不受影响。唯一入库基线变化：`lab/fixtures/baselines/feel_charge.baseline.json`（三个动作式格，第三次按住的动作开始 tick 200 → 191，延迟 60 → 51，标记与顿帧随之平移）。
- **手感设计 02/04 文档订正（ADR-0147）**：04 第 10 节去掉"勘误"措辞并入正文，键数不写死，第 6.1 节步幅是运行期参数，第 8 节导入检查表按 `import_assets check` 的实际落点重写，第 9.1 节参考值改为 `feel.weapon.timeline_reference` 偏差不超过一帧，补受击反应驱动、播放速率与脚步声单一来源；02 第 7 节改为步态与呈现参数契约，第 3.5 节与第 8 节第 11 条的求解机制收为契约级。
- **手感设计 06 文档订正（ADR-0151）**：第 10 节"首版落地勘误"并回正文各节后删除，指标表按注册表重写并标明逻辑/表现/实时三种比较口径，不再枚举脚本数量；删去无落点的"标定表可收紧"；接入指南第 2 步拆成"不改数据跑框架套件应零差异"与"自己的数据根对自己导出的夹具比较"；03、00、13 与 ADR-0130 对 06 第 10 节勘误的引用改指现章节。

## [1.97.0] - 2026-10-04

### 工具链

- **`build.ps1 -Release <版本> -Resume`：发布提交之后失败可续跑，不再重跑全量门禁**：1.96.1 发布三次失败（全量门禁已通过，打包阶段 `dotnet test` 偶发红、docfx 崩溃），当时唯一的恢复路径是 `git reset --soft`、手工还原五个版本文件、整条 `-Release` 重跑，约 50 分钟的全量门禁白跑。现在第 5 步通过时写门禁通过记录 `dist/release-<版本>.state.json`（被 `.gitignore` 覆盖，`prune_dist.ps1` 保留），第 6 步写入发布提交，其后每个阶段（打包、自检、标签、私服 ×4、推送、GitHub Release）各写完成标记；发布提交之后任一阶段失败（`throw` 与 `exit` 两条路径）都打印 `-Resume` 续跑命令，`git reset --soft` 只保留为"放弃本次发布"的回退路径。`-Resume` 只在六项前置校验全部满足时执行（状态文件在且版本一致、`HEAD` 即记录的发布提交、其父提交即记录的发布前提交、发布提交只含五个版本文件、工作树干净、`VERSION` 等于目标版本；拒绝码 `NoState`/`StateUnreadable`/`VersionMismatch`/`NoReleaseCommit`/`HeadMismatch`/`ParentMismatch`/`CommitFiles`/`DirtyTree`/`LocalVersionMismatch`，逐项给出下一步），跳过第 1～6 步，从第一个未完成的阶段起按序执行，各阶段幂等：标签已在 `HEAD` 则跳过（指向别处则拒绝）；私服已有同版本包则比对 `integrity`（一致跳过、不一致拒绝、绝不覆盖，非 404 的查询错误中止）；远端已有标签且分支在 `HEAD` 则跳过推送；GitHub Release 已存在则只补传缺失附件（不带 `--clobber`，同名附件大小不符则拒绝）；打包重跑跳过 `dotnet test`（门禁已对同一棵代码树跑过），允许覆盖未打标签的产物（标签已存在由"发布不可变"守卫拒绝）。第 5 步及更早的失败不在续跑范围（没有门禁通过记录），修好后重跑 `-Release`。判断记录见 `toolchain/_release_resume.ps1` 文件头、`build.ps1` `.PARAMETER Resume`、`toolchain/README.md`"发布续跑"一节；测试 `toolchain/tests/test_release_resume.py`（库函数单元 + 真实 `build.ps1` 跑在最小仓库骨架里，外部命令用桩，不触及真实私服/GitHub）。`AGENTS.md` §5 的半途恢复条款同步改为"首选续跑"。

### 新增

- **标准骨骼蒙皮预渲染为序列帧（[ADR-0140](architecture/adr/0140-标准骨骼蒙皮预渲染为序列帧.md)，手感设计/04 第 6.2 节"后续项"落地）**：离线工具链能力，运行期契约不变。`toolchain/run_prerender_skin.py` + `toolchain/prerender_skin/` 驱动器读一份渲染配置（姿势集 id、方向档、分辨率与脚点、固定相机俯角、光照模式、体量组、装备层选择器），经引擎批处理（包内新增编辑器程序集 `Adapter.Unity.SkinPrerender.Editor`）把"骨名符合标准骨骼的蒙皮"逐键逐方向渲染成透明背景图像，组装成序列帧资源与 `display.anim_set` 数据行；动画复用标准骨骼剪辑库（含体量组），帧数/帧时长/事件时刻取自假人姿势集同一套函数，释放点与命中对齐保持。自检（键与帧数、非空、枢轴不漂移、层对齐、画布裁切）失败即错误；缺骨骼、未知方向档、找不到装备层对象的选择器一律拒绝并列出清单。同机同输入逐字节一致。预渲染产物是生成物不入库。`toolchain/std_dummy_poses/build.py` 的数据行写入器加法支持行级 `pose_standard`，打包函数加可选画布参数，假人姿势集产物逐字节不变。测试：`toolchain/tests/test_prerender_skin.py`（驱动器全链路，桩后端）、引擎侧 `SkinPrerenderPlayModeTests`（真实渲染、对齐、确定性、拒绝路径、现有序列帧播放路径下 release/hit 时刻与假人姿势集逐位相同）。

## [1.96.1] - 2026-10-03

### 修复

- **`toolchain/get_framework.ps1` 解压提速（zip 通道）**：1.96.0 发布 zip 已有约 3.3 万个条目、解压后约 413 MB，逐条目的 PowerShell 命令调用开销成了主要耗时。`Expand-ZipEntriesSafely` 改为循环外一次性算好根目录规范化路径、循环内只用 .NET 静态方法并缓存已建目录；zip slip 边界校验语义不变（仍在写入任何后续条目前整体拒绝）。本机实测解压并落地 1.96.0 的 zip：PowerShell 7 约 60 秒降到约 24 秒，Windows PowerShell 5.1 约 44 秒降到约 29 秒。对外参数与行为不变，消费方无需改动。

## [1.96.0] - 2026-10-03

本段汇集手感落地 M4（1.95.0 之后）：体积阻挡精确化与三期、宽限瞄点与冻结兜底、实验室无头补完与动态韧性、竖直轴能力包补完与二期（地形、导航、空中战斗）、实验室引擎宿主与引擎侧残余、框架级假人姿势集三期与残余收尾（躺姿重绘、施放点变体与命中对位）、手感相关已知限制的最终清扫。**不开手感、不声明 `unit_body_radius`、不配置 `MovementOptions.Vertical`、不声明本段新增的任何可选字段时，行为与 1.95.0 逐位一致**；有行为变化的几处见下方"变更"与"接入与迁移说明（M4）"。公开签名只增不改（新增的是默认接口成员、可选属性、重载与新类型，旧签名物理保留）。

**1.95.0 条目与 `lab/README.md` 里登记的手感相关已知限制，到本段为止已全部解除或转为判断记录**：真能力缺口在 M4 各切片与最终清扫里落地为能力（见下方新增与变更），合理的边界改写为各模块 README 与 [ADR-0138](architecture/adr/0138-手感已知限制清扫的数据与契约决定.md) 里的设计决定（决定 + 理由）；历史版本条目按惯例原样保留、不回头改写。

**非手感模块（核心、表现、适配、工具链、文档）里登记的已知限制，到本段为止同样已全部解除或转为判断记录**（见下方"非手感已知限制清扫"与 [ADR-0139](architecture/adr/0139-非手感已知限制清扫的契约与行为决定.md)）；历史版本条目保持原样。

### 新增

- **动态韧性池（M4-L，M4-W3 扩展，[ADR-0131](architecture/adr/0131-动态韧性池与回复扩展.md)，手感设计/03 第 4 节）**：攻击方档案可选字段 `poise_damage`（每击韧性伤害），目标侧可选字段 `poise_recover_per_s`、`poise_recover_delay_ms`（回复速率与延迟）、`poise_recover_mode`（`delay`｜`out_of_combat`，缺省 `delay`；后者脱战才回复，复用战斗状态）、`poise_break_reset_ms`（破韧后定时回满，0..60000，缺省不重置），`HitFeelOptions.PoiseDamageImpactMultipliers`（`poise_damage` 按冲击等级缩放）；新事件 `combat.poise_changed`、`combat.poise_recovered`。不声明 `poise_damage` 时逐位保持原有静态韧性规则。
- **导航增量阻挡（M4-L，[ADR-0132](architecture/adr/0132-导航增量阻挡.md)）**：`INavigation2D` 新增默认接口成员 `AddBlocking`、`RemoveBlocking`、`GetBlocking`，阻挡版本号每次有效变化恰好加一；没有覆盖这些成员也不暴露当前集合的实现在调用增量成员时抛 `NotSupportedException`（不静默丢阻挡）。桩导航与 Unity 导航都已覆盖，可破坏障碍由此增量替换而不是整批重设。
- **宽限窗口的施法瞄点与框架内置条件（M4-G，M4-W3 收口，[ADR-0133](architecture/adr/0133-宽限窗口的施法瞄点.md)，手感设计/01 第 2.4 节）**：新增 `GraceAim`、`IGraceAimSink`；`IGraceConditionEvaluator` 新增接口缺省成员（带瞄点的三参 `Evaluate`、`UsesAim`、`DefaultAimTarget`），旧的第三方求值器不必改。缺省 Expr 求值器的目标来源优先级为游戏覆盖、施法请求携带的目标或落点、自动攻击目标；表达式新增 `event.aim_*` 上下文（`has_aim`、`aim_distance`、`aim_range`、`aim_in_range`、`aim_line_of_sight`、`aim_is_ground`，惰性计算）。依赖瞄点的条件按瞄点归属历史，瞄点换了历史清零，杜绝"对 A 的宽限放行对 B"。目标链解析出的目标同样记为瞄点；瞄点只对携带宽限条件的请求记录。框架数据新增 `found.grace_condition` 三行内置条件（`input.grace.builtin_aim_in_range`、`builtin_aim_line_of_sight`、`builtin_aim_reachable`），不被引用则没有任何效果。生产装配对单位的输入缓冲惰性分配（没有动作声明宽限条件就不建缓冲，之后声明时补登记），`InputBufferHost.RegisterActor` 同样惰性。无头世界新增可选 `HeadlessWorldOptions.NavigationLineOfSight`（缺省关）。
- **顿帧冻结的通用兜底（M4-G、M4-W3，[ADR-0129](architecture/adr/0129-顿帧冻结特效与粒子的可选能力接口.md) M4 增补，手感设计/07 第 5 节）**：`freeze_layers.particles` 为真时被冻结单位名下特效的存活时长恒停住，不再取决于适配层是否实现 `IParticleFreezer`；新增 `ParticleFreezeFallbackRenderer2D`（二维渲染契约的装饰器，给没有粒子暂停原语的适配层通用兜底，宿主给时间缩放口时粒子停在原处，否则暂停等于停止、恢复等于按原参数重发；无头桩经 `Wrap` 获得）。
- **单位体积阻挡精确化与三期（M4-B、M4-W2，[ADR-0128](architecture/adr/0128-单位间体积阻挡.md) M4 增补，手感设计/02 第 3.5 节）**：1.95.0 留下的四条限制解除。①跟随者不再滞后一个 tick，会动的单位之间在 tick 末按最终位置求解；②成对撞停按单位实际走的折线逐段求接触；③位移到达与受阻事件对有体积的单位延后到成对裁决之后、带最终位置发出；④追击与朝目标的动作位移读目标的 tick 起点快照。三期：求解改为"可重放的求解遍"，按实际是否移动分类并迭代到收敛（上限 12 遍），预判只是加速提示，结果与预判无关；接触时刻按速度剖面求解（匀速仍走解析式，逐位不变）；穿过式位移的终点不落在别人体积里（24 个圆周采样点、最多 4 轮），别的单位避让它的最终位置；被拉回的单位保留沿接触面的切向速度、路径进度不倒退；受控位移推人同 tick 生效（连锁最多 8 轮）；位移扫掠与近邻对收集改用均匀网格宽相，与暴力两两比较逐位一致。全部只在 `unit_body_radius > 0` 时生效。
- **竖直轴能力包补完（M4-V，[ADR-0130](architecture/adr/0130-竖直轴能力包.md) M4 增补，手感设计/06 第 10 节勘误 9）**：全部可选、缺省关闭。①空中水平控制比例（`VerticalAxisOptions.AirControl`，缺省 null = 不限制）与空中跳跃次数（`MaxAirJumps`）；②地形：`ITerrainHeight2D` 与 `world.map.terrain` 数据，地面与天花板高度、落地高度、天花板清零上升速度、斜坡贴地、`StepHeight` 台阶阻挡（经共享阻挡出口，停止原因 `MoveStopReason.TerrainBlocked`）；③空中受击反应 `air_hit_reaction` 与空中姿势键（`jump.rise`、`jump.fall`、`jump.land`、`hit.air`、`attack.air[.<族>]` 及固定回落链，`AirPoseFeeder` 派生空中阶段）；④击飞叠加 `launch_stack`（`restart`｜`add`）与 `launch_stack_cap`；⑤目标链形状 `height_offset`；⑥可选三维施法射程 `SkillOptions.SpatialRange`。
- **地形与导航二期（M4-W1a，ADR-0130 M4 增补）**：台阶规则改为滑窗（阻挡点与停点二分到 1e-10，停点精确）；`INavigation2D.FindPath`、`TryFindNearestReachable` 新增带 `ITerrainStepConstraint` 的默认接口重载，共享规划器 `TerrainStepPathPlanner` 由桩导航、Unity 导航与开阔场地导航共用，目标被台阶整个隔开时报 `NoPath`；路径校验看地形，路径分段按折线精确扫掠；单向平台落地条件加竖直速度不大于 0（上升中穿过更高平台不落地）；走出台地边缘下落由 `VerticalAxisOptions.FallHeight` 决定；地形形状支持矩形、凸多边形与高度场同表（后声明覆盖先声明，`WorldMapTerrainValidationRule` 校验）；`AiHost.TerrainStepConstraint`（缺省 null）让 AI 转向感知台阶；Unity 适配新增 `UnityTerrainHeight2D`（物理射线实现，按物理场景与地图根隔离，`BindMap`、`BindMapRoot`、`StrictMaps`）与地形感知的 `UnityNavigation2D`。
- **空中战斗与空中姿势维度（M4-W1b，ADR-0130 M4 增补）**：手感档案新增可选字段 `air_reaction_cap`（空中反应上限）、`launch_height_cap`（击飞绝对高度上限）、`launch_body_scale`（按体型缩放击飞）、`air_stun_until_land`（空中硬直撑到落地）、`land_hold_ms`（落地保持毫秒）；新事件 `unit.landed`（`VerticalAxisOptions.EmitLandedEvent`，缺省关）；`MovementOptions.DepthLockControlledMotion`（受控位移也受深度锁，缺省关）；`SkillOptions.SpatialRangeHitWindow`（动作式结算命中窗口的射程门，缺省关）；地面坐标施法的落点高度读地形（`IUnitAccess.GetGroundHeightAt`）；`ILaunchSink.BeginLaunch` 新增带高度上限的默认接口重载（既有实现不必改）；空中姿势键可带姿态、武器族、变体维度并按维度前缀逐段回落。
- **实验室引擎宿主（M4-H、M4-W4，[ADR-0134](architecture/adr/0134-实验室引擎宿主.md)，手感设计/06 第 4 节）**：同一个运行入口加扩展钩子 `LabHostExtension`（不是第二套循环），逻辑组指纹与无头宿主逐字节一致（新增跨宿主不变量）；引擎侧失败隔离进记录；条件度量组 `engine`（命中帧对齐、镜头冲量曲线、顿帧期间 rig 与粒子推进量、CPU 与 GPU 帧耗时、相机相对输入误差、换装图层核对、引擎失败数）；覆盖存储 `OverrideStore` 与 A/B 对比（源数据一字不动）；输入噪声模型与回放；编辑器面板 `GameFoundation/手感实验室`（面板逻辑在纯逻辑的 `LabPanelModel`）；Unity 包新增可选程序集 `Adapter.Unity.LabHost`（不自动引用、不进玩家构建）。GPU 帧耗时只在有图形设备的环境取得，取不到时度量照样在场、状态标 `unavailable`、数值取哨兵 -1。
- **相机相对控制空间与相机俯仰/透视（M4-W4，[ADR-0135](architecture/adr/0135-相机相对控制空间与可选相机俯仰透视.md)）**：`found.input_action` 新增可选字段 `control_space`（`world` 缺省｜`camera_relative`），新增可选能力接口 `ICameraOrientation`（只有偏航角），`InputMapOptions.CameraOrientation` 提供朝向来源；声明了 `camera_relative` 却没有朝向来源是声明期错误。`UnityCamera` 新增可选能力 `ApplyPitch`（固定俯仰，[0, 89] 度）与 `Perspective`/`FieldOfViewDegrees`（透视，缺省 40 度），显式声明才生效，缺省保持正交俯视。
- **可注入帧时间源（M4-W4，[ADR-0136](architecture/adr/0136-可注入帧时间源.md)）**：新增 `IFrameTimeSource` 与 `ManualFrameTimeSource`；`UnityFrameAnimPlayer`、`EffectSequencePlayer` 新增公开可选属性 `TimeSource` 与公开 `Step()`，`UnityRenderer2D.EffectTimeSource`/`StepEffects()` 对全部序列帧特效生效；不设置则每帧取引擎帧间隔，行为逐位不变。M4-H 的内部推进入口已删除。
- **图标加载路径（M4-W4）**：`UnityResourceLoader` 对 `icon` 类别资源引用走 `icons/<类别>/<名>.png` 约定；`toolchain/resource_layout_map.json` 新增 `icons` 映射，占位美术与样例美术的图标同步到 `StreamingAssets/GameFoundation/icons`。
- **姿势切换交叉淡入时长数据化（M4-D，[ADR-0137](architecture/adr/0137-姿势切换交叉淡入时长数据化.md)，手感设计/04 第 10 节第 10 条）**：`display.anim_set` 加法字段 `blend_ms`（每个剪辑条目，0..2000）与行级 `blends`（`[{from, to, blend_ms}]`）；优先级每对键 > 逐键 > 缺省 0.15 秒，显式 0 = 硬切；只对 `model` 型骨骼剪辑生效；沿 `extends` 合并。
- **框架级假人姿势集三期（M4-D，手感设计/04 第 10 节第 7、10 条）**：键清单由 103 键增至 128 键（117 份独立剪辑）：空中键（`jump.rise`、`jump.fall`、`jump.land`、`hit.air`、`attack.air` 与八个武器族）、带伤变体覆盖全部移动与战斗移动键、细节键（`hit.block`、`hit.block.shield`、`stunned.sway`、`hit.launch.tumble`、`hit.launch.land`）；体量三档由数据声明（中档 = 主集），轻、重体量组覆盖主集全部键，序列帧版同样有体量组；五个新武器族改为程序化低多边形形体。**除下一条 M4-W5/W6 重绘的躺姿与重受击键外，既有键的骨骼剪辑与身体层字节不变，只追加。**不做根运动与布娃娃（位移权威在逻辑层）。

- **假人姿势集残余收尾（M4-W5、M4-W6，手感设计/04 第 10 节第 3、4、7、10 条）**：①躺姿五键（`hit.launch`、`hit.knockdown`、`hit.getup`、`death`，加与起身同躺姿的 `hit.launch.land`）改用骨盆整身俯仰重绘，肩、髋、躯干角限收紧到人体范围；M4-W6 把 `hit.heavy`、`hit.knockback` 也从"字节锁"里放开并重绘到人体范围（后仰用整身俯仰表达），骨骼版的角限例外表（`SOURCE_ANGLE_EXEMPT`）随之删除；键名、时长、事件、帧数不变，其余键的规格与图像字节不变；两版、体量组、装备集同步。②体量组（轻、重）补整身方向变体，全部键都出（不退化为只出移动与待机键）：新增 2340 个序列帧文件、约 8.2 MB（轻约 4.0 MB、重约 4.2 MB），低于 20 MB 的上限；用整身剪辑的外形在体量组下因此也随朝向换图。③动画控制器没有过渡连线，交叉淡入由运行期按数据（[ADR-0137](architecture/adr/0137-姿势切换交叉淡入时长数据化.md)）驱动，控制器与资源标识不变，生成仍确定性、重复生成逐字节一致。④`cast` 的释放点由 250 毫秒移到 150 毫秒（相位 150/100/350，总时长 600 毫秒与帧数 12 不变），`release` 同时作 `hit_frame` 别名事件（模型版 `cast` 此前没有 `hit_frame`），与实验室多数技能的命中标记（150 毫秒）逐毫秒对齐；不改技能时间线，实验室基线不变。
- **施放点变体 `cast.quick` / `cast.heavy`（M4-W6，手感设计/04 第 10 节第 10 条）**：可选姿势键 `cast.quick`（`release` 在 100 毫秒）与 `cast.heavy`（`release` 在 300 毫秒），总时长与帧数同 `cast`，两版与体量组同键同事件；游戏用武器风格 `display.weapon_style` 的既有字段 `cast_anim_override`（技能 id 到剪辑资源引用）把技能指到变体，回退链为 `cast.<变体>` 到 `cast`，没有声明覆盖的游戏不变。实验室数据 `data/_lab_action/display/display.weapon_style.json` 用它把起手 100 毫秒（combo1、combo2、poise_chip、lunge、空间扩展的 jab）与 300 毫秒（精英重击）的技能指到变体，结果每个实验室技能的命中标记都等于它所播放施放剪辑的 `release` 时刻。W6 新增文件共 325 个、约 1.16 MB（序列帧图像 312 个，含主集、体量组与装备集派生的变体剪辑；骨骼剪辑 6 个及其 6 个 `.meta`；数据行文件 1 个）。
- **蓄力效果值倍率（M4 清扫，[ADR-0138](architecture/adr/0138-手感已知限制清扫的数据与契约决定.md) 决策 1、2，手感设计/01 第 3.3 节）**：`timeline.charge` 新增可选 `value_scale: {min, max}`（均不小于 0），伤害与治疗类效果值乘倍率 `min + (max − min) × 蓄力比例`（蓄力比例夹到 0～1），经既有的 `EffectContext.TargetCoefficient` 生效；`release` 标记发射的投射物经新增的默认接口成员 `IProjectileHitHook.ValueScale`（缺省 1）把倍率带到命中后效果。不声明不缩放。蓄力相就是动作开始之前输入缓冲里 `hold_pending` 记录存续的那段，动作在抬起或到上限时才开始，不发 `charge` 相位事件（枚举成员保留）。
- **`timeline.is_attack`（M4 清扫）**：技能时间线新增可选布尔字段，显式声明动作是否带攻击（`action.started.isAttack`），缺省按技能内容推断；对友方的治疗类投射物写 `false`，反馈侧就不为它开挥空窗口。
- **缓冲出口"能否处理"（M4 清扫，ADR-0138 决策 3，核心输入映射判断记录）**：`IBufferedIntentSink` 新增默认接口成员 `CanHandle(actorId, record)`（缺省真），`IInputBufferQuery` 新增带跳过谓词的 `TryPeek`/`TryConsume` 重载（默认实现忽略谓词）；只有出口声明"永远接不了"（例如输入动作没有技能映射）的记录被剔出候选，它们留在缓冲里直到自己的窗口过期，不再在过期前挡住优先级更低的可接受记录；"此刻不能接受"（冷却、动作锁）仍按优先级等待。生产装配的 `BufferedActionIntentSink` 与时间线取消窗口的拉取都按此剔除未映射记录。
- **取消进入时的朝向对齐（M4 清扫）**：时间线在取消窗口里拉取的记录被接受时，若记录要求对齐（`FaceOnAccept`）且带按下瞬间的方向快照，就在取消进入被接受的那一刻把朝向对齐到该方向（需要 `IUnitFacingWriter`，没有时记诊断警告），与缓冲出口同一口径。
- **目标命中半径（M4 清扫，ADR-0138 决策 4，手感设计/03 第 2.4 节）**：`TargetingOptions.TargetRadius`（`Func<Id, double>?`，单位命中半径，世界单位）与 `MaxTargetRadius`（广相位上界，必须不小于实际最大半径），缺省关闭。开启后 `nearest_in_shape`/`all_in_shape` 在"中心落在形状内"的结果之后，追加"形状到目标中心的最近点距离不超过该目标半径"的单位（先按上界外扩取候选，再逐个精确重判，追加项按 Id 序）；时间线空间命中（标记与持续命中的逐 tick 采样）经同一条形状查询，自动按半径判定。半径来源由游戏给（体型数据，或手感档案 `unit_body_radius` 的换算）。

### 变更

- **体积阻挡的三处行为变化（只影响声明了 `unit_body_radius` 的单位）**：①`unit.moved` 与位移到达、受阻事件现在延后到成对裁决之后、带最终位置发出（M4-B）；②穿过式位移（冲刺、后撤等）的窗口最后一个 tick，终点若落在别人体积里会被修正到就近可行位置，此前允许在窗口后才被推出（M4-W2）；③受控位移（击退、技能位移）推人由"下一 tick 起生效"改为同 tick 生效，被拉回的单位保留切向速度（M4-W2）。
- **顿帧冻结特效的存活时长**：`freeze_layers.particles` 为真且适配层没有实现 `IParticleFreezer` 时，此前粒子既不暂停也不停表，现在存活时长倒计时恒停住（解冻后从冻结点继续）；层开关为假（缺省）时一切不变。
- **宽限的目标来源**：缺省 Expr 求值器在施法请求带单位目标时取该目标（此前只取自动攻击目标）；声明了依赖 `target` 的宽限条件并发出带不同目标的请求，行为按新优先级。没有动作声明 `grace_conditions` 的游戏不受影响。
- **单向平台落地条件**：装配了竖直轴时落地要求竖直速度不大于 0；上升中穿过更高平台的单位不再提前落地。停点精确化使台阶前停点从 9.000000003 变为 9（3e-9 量级），只影响装配了台阶阻挡的世界。
- **动作时间步长回填**：生产装配与无头世界按宿主步长回填 `SkillOptions.ActionStepSeconds`（M4-L），非 60 Hz 宿主上动作时间线的相位 tick 数现在随宿主步长换算；60 Hz 宿主不变。
- **装配对穿脱装备的失效**：去掉装配对穿脱装备的无差别 `equipment_changed` 失效（M4-L，换装场景改走生产装配后暴露的重复失效），换装后不再多做一次无谓重算；结果不变。
- **实验室**：武器到普攻技能映射改为数据覆盖行，移除内存覆盖 `WeaponAttackSkills`；冲击档案与音效行从内核搬进实验室数据集（`data/_lab_action`）。`feellab run` 支持竖直格子。
- **镜头阻尼按真实帧间隔（M4 清扫，ADR-0138 决策 10）**：三个生产引导（Unity 适配层的 `GameFoundationBootstrap`、`FrameworkResidentHost`，以及游戏模板 `GameBootstrap`）改调 `Update(alpha, unscaledDelta)`，镜头的阻尼、前瞻与死区按真实（不受时间缩放影响的）帧间隔计算，收敛速度不再随帧率变化。`ICameraHost.Update(alpha)` 既有签名原样保留（按固定 1/60 秒推进，对固定步长宿主就是准确值）。不开镜头手感档案（缺省）时旁路档案计算，不受影响；自己直接调旧入口的外部宿主改用带帧间隔的重载。
- **冷加载特效的发射位置（M4 清扫）**：冷加载排队的特效在加载完成那一刻重新取锚点/挂点宿主的当前位置发射，与资源已缓存的热路径同口径（此前用 `Spawn` 时刻位置，加载越慢差越大）；宿主已不在时回落到 `Spawn` 时刻位置；排队期间 `Stop(占位句柄)` 取消排队、从不发射。
- **未映射记录不再挡路、取消进入对齐朝向**：见上方"缓冲出口能否处理"与"取消进入时的朝向对齐"。只影响"动作声明了输入类别却没有技能映射"，或记录要求对齐朝向且带方向快照的游戏；其余行为与 1.95.0 逐位一致。
- **重受击与击退的骨骼剪辑字节变化（M4-W6）**：`hit.heavy`、`hit.knockback` 的极值帧放开字节锁并重绘，引用框架标准假人姿势集的游戏会看到这两个键的剪辑资源与序列帧图像字节变化（键名、时长、事件、帧数不变）。躺姿五键同样重绘（M4-W5）。

### 修复

- **覆盖剪辑的事件缺口（M4-W6，Unity 适配层）**：`UnityViewFactory` 登记武器风格覆盖剪辑时一律传空事件表，覆盖剪辑播放时序列帧位面没有 `release`/`hit_frame`，宿主命中对齐数不到。新增 `OverrideClipEvents`：在实体所用 `display.anim_set` 合并继承链后的剪辑里按资源引用反查事件，探测、重探测、缓存命中重登记、整身方向变体（热路径与冷加载）与非方向路径都经它。
- **序列帧播放器的帧边界浮点（M4-W6，`FrameAnimPlayer`）**：求当前帧改为 `FrameIndexAt = floor(已播秒数 × 帧率 + 1e-9)`，累加多个浮点步长恰好落在帧边界时不再因舍入落在边界之下、使关键帧晚一个宿主帧触发；逻辑指纹不受影响（播放器只在表现层）。
- **实验室整份基线比较不再依赖墙钟（M4-W6）**：`FingerprintComparer.Compare` 与 `LabSuite.Check` 新增 `includeRealTime` 重载，测试辅助 `LabTestSupport.CheckDeterministic` 排除实时类度量（帧耗时与每帧分配的倍率上限），此前满载机器上间歇变红；实时类上限仍由命令行 `suite` 强制。
- **光环霸体补测（M4 清扫）**：`SuperArmorAuraDef` 经光环查询的霸体补了独立用例（光环在 → 不进硬直但照常吃目标顿帧，光环移除 → 恢复正常裁决；选项没声明光环定义时光环存在被忽略），原"已知局限"解除。

### 接入与迁移说明（M4）

- **不开手感、不声明新字段：什么都不用做，行为与 1.95.0 逐位一致。**旧编译的消费方不必重编。实验室既有基线随各切片逐步扩充，既有脚本除 `space.terrain` 外基线逐位不变（该脚本因停点精确化重写，仅此一个）。
- **声明了 `unit_body_radius` 的游戏**：留意上面"变更"里的三处行为变化；依赖"位移事件在 tick 中途按处理顺序发出"或"穿过式位移可以停在别人体积里"的订阅方与脚本需要同步。
- **自写 `IGraceConditionEvaluator`、`ILaunchSink`、`INavigation2D`、`ICamera` 的游戏**：这些接口只增加了默认成员或独立的可选接口，既有实现不必改；要用增量阻挡、带瞄点的宽限、空中击飞上限或相机相对控制，再按需覆盖对应成员或实现可选接口（`ICameraOrientation`）。自写 `INavigation2D` 若要调增量阻挡，须覆盖 `AddBlocking`/`RemoveBlocking` 或暴露 `GetBlocking`，否则调用抛 `NotSupportedException`。
- **开启竖直轴的可选能力**：地形数据写在 `world.map.terrain`，空中控制与空中跳跃经 `VerticalAxisOptions`；空中姿势键在姿势集里没有对应剪辑时沿回落链落到通用键，不报错。
- **使用相机相对控制**：在二维轴动作上声明 `control_space: camera_relative`，并确保相机实现了 `ICameraOrientation`（Unity 适配的 `UnityCamera` 已实现）；无头宿主不提供朝向，该格按 `world` 运行。
- **使用框架假人姿势集的游戏**：键清单只追加、既有键不变；`display.anim_set` 的新字段 `blend_ms`/`blends` 没声明时缺省仍是 0.15 秒交叉淡入。轻、重体量组覆盖主集全部键，序列帧版体量组同样写整身方向变体（M4-W5），用整身剪辑的外形在体量组下也随朝向换图；`hit.heavy`、`hit.knockback` 与躺姿五键的剪辑字节有变（见上方"变更"），`cast` 的释放点移到 150 毫秒；`cast.quick`/`cast.heavy` 是可选键，没声明 `cast_anim_override` 的游戏不受影响。
- **依赖实验室内部的工具**：`LabHostExtension` 的 `ConvertMoveAxis` 已由 `CameraOrientation`、`ControlSpaceOverride`、`OnMoveAxis` 取代；引擎宿主的内部推进入口已删除，改用公开的时间源与 `Step()`。

- **自写 `IBufferedIntentSink`、`IInputBufferQuery`、`IProjectileHitHook` 的游戏**：这三个接口只新增了默认成员（`CanHandle`、带跳过谓词的 `TryPeek`/`TryConsume`、`ValueScale`），既有实现原样工作、不必重编；自写缓冲出口若要让"永远接不了"的记录不挡路，覆盖 `CanHandle`，自写输入缓冲查询要支持跳过则覆盖带谓词的重载。不声明 `charge.value_scale`、不配置 `TargetingOptions.TargetRadius`、不写 `is_attack` 时这几条都与 1.95.0 逐位一致。
- **自己驱动 `ICameraHost.Update(alpha)` 的外部宿主**：旧入口按固定 1/60 秒推进，变帧率宿主上阻尼收敛速度随帧率变化；改调 `Update(alpha, dt)` 并传真实（未缩放）帧间隔即可。三个生产引导已改。
- **使用蓄力的游戏**：效果值倍率写在时间线的 `charge.value_scale`（旧设计文稿里"蓄力比例暴露给表达式"的写法不被承认）；蓄力期间没有动作实例，表现层读输入缓冲的只读快照取按住时长。

### 资产与实验室规模（M4）

- **资产体积**（按 M4 收口后的文件系统重新统计，`assets/` 与 Unity 适配内 `anim_clips/` 均为入库文件）：框架 `assets/` 合计由 5165 个文件、30.85 MB 增至 13831 个文件、58.40 MB（序列帧占位美术 `assets/_placeholder/sprite_anim/` 由 4898 个文件、12.45 MB 增至 13564 个文件、37.21 MB；Unity 适配内的骨骼剪辑资源 `anim_clips/` 由 226 个文件、9.37 MB 增至 724 个文件、30.56 MB）。序列帧占位美术净增 8666 个文件：M4-D 新增 6014 个、约 15.78 MB（轻体量组 6.33 MB、重体量组 6.59 MB、主集新键 1.68 MB、装备集派生约 1.17 MB），M4-W5 体量组整身方向变体新增 2340 个、约 8.2 MB（轻约 4.0 MB、重约 4.2 MB），M4-W6 施放点变体新增 312 个；M4-D 既有文件改写 495 个、约 2.13 MB（五个新武器族的武器层与合成图），M4-W5/W6 另改写躺姿与重受击、击退键的图像字节（文件数不变）；骨骼剪辑净增 6 个剪辑（W6 的 `cast.quick`/`cast.heavy` 在主集、轻、重三组下各一，文件数含 `.meta` 为 12 个）。发布包体积随之增加。
- **实验室**（本段合并后 `feellab` 实测，数字随脚本增删变化，以命令输出为准）：标准脚本 69 个（十一个旧脚本、二十九个手感脚本、二十九个空间脚本）；`suite` 530 格、`RESULT total=530 pass=530 diff=0 missing=0`；期望清单 `RESULT expectations total=484 pass=484 fail=0`；`invariants` `RESULT invariants total=741 pass=741 fail=0`。相比 1.95.0 的 258 格、98 条期望、379 条不变量，增量全部来自新增脚本（动态韧性、投射物到期与清场、空间扩展、空中战斗、地形与导航等）。发布包随附的实验室数据与夹具同步更新，消费方按 `dotnet toolchain/feellab/bin/FeelLab.dll suite` 应得与上面同口径的汇总行。

### 非手感已知限制清扫

核心层、表现层、适配层、工具链与文档里非手感模块登记过的已知限制，到本段为止同样已全部解除或转为判断记录：真能力缺口落地为能力并带复现用例，合理边界改写为各模块 README 的"设计决定"（决定 + 理由），汇总取舍见 [ADR-0139](architecture/adr/0139-非手感已知限制清扫的契约与行为决定.md)。公开签名只增不改；不声明新能力时行为与清扫前逐位一致，唯一的行为改进见下方"迁移说明"。

**核心层（NF1）**

- **攻击实例标识经表达式暴露**：`combat.attack_avoided` 与 `combat.hit_confirmed` 事件的 `attackInstanceId` 现在可被表达式读取（`IExprReadableEvent.TryGetField("attackInstanceId")`，为空即不经施法管线的结算时查不到），`feedback.binding` 等条件过滤可按攻击实例关联多个事件；事件目录描述同步，`EventKeys.g.cs` 重新生成。
- **套装门槛在 `item.set` 数据重载后立即对账**：`EquipmentHost` 在 `data.load_completed` 之后对"有装备件的单位 × 所属套装"与已记录施加过门槛的 (单位, 套装) 全部重算（遍历按单位 id、套装 id 的序数序，确定）。重载删除或改高的门槛当场释放，新增或改低且已满足件数的门槛当场施加；定义没变时不动已施加的句柄。取代此前"孤儿门槛要等下一次装备变化才清理"。
- **滑动终点被拒绝时停在撞击点**：`wall_slide` 的滑动终点被可走性判定拒绝（单位恰好落在斜墙边界线上、终点因浮点误差落入内部）时，位移退回撞击点前的回退点并速度归零，不再整个 tick 不位移。
- **货币期望新增精确取整口径**：`LootExpectedCurrencyOutcome.ExpectedRoundedAmount` 含运行期同样的四舍五入与非正值跳过，经嵌套掉落与保底补抽路径线性叠加；`ExpectedAmount`（线性期望）语义不变，两个字段并存。编辑器"理论与观测"面板应读 `ExpectedRoundedAmount`。
- **词缀入选概率的大池精确解**：新增 5 参数重载 `LootTableAnalyzer.ExpectedAffixInclusion(templateId, qualityId, registry, exactMaxEntries, exactMaxWork)`，以抽取次数有界的精确算法求解，`exactMaxWork` 是转移次数预算；既有三参、四参入口不变（预算缺省 0 = 不启用，池超过阈值仍降级并标记）。抽取次数大于 5、池大于 4096 或估算超预算才如实降级。
- **增长仿真可在种子中途取消**：`GrowthSimulation` 的取消检查点下探到每一场击杀战斗之前，取消延迟从"一个种子的完整成长轨迹"缩短到"一场战斗"；单场战斗内部不检查，不取消时结果逐位不变。
- **改写为设计决定**：移动请求不入存档、追击过冲上界为重规划距离、离散模式追击目标丢失检测时机、网格斜墙阶梯法线（要平滑斜墙提供带真实法线的导航实现）、旧布局备份计入配额；已解决的陈旧条目删除。

**表现层、适配层与游戏模板（NF2）**

- **`SfxPlayer` 新增 7 参数构造**（末位 `EntityPositionResolver?`）：挂在实体上的循环音效在实体被销毁后于下一次更新停止；原 6 参数构造保留并转调，装配接入同一解析器。
- **总线音量作用于已在播放的声音**：`UnityAudio.SetBusVolume(Sfx, v)` 对正在播放的音效立即按"基础音量 × 总线音量"重设，不碰其它总线。
- **图片解码完整性校验**：`ManagedPngDecoder` 校验 IHDR/IDAT/tRNS/IEND 的 CRC-32 与 zlib 尾部 Adler-32，不符整份资源判为损坏，回落引擎解码并记警告。
- **`MapLayers` 背景解码**：地图分层（地面/覆盖/贴花）的图片在后台托管解码，主线程每层一个工作单元；某层是内置解码器不支持的变体时整份资源回退主线程解码。
- **加载器并入在途请求**：`UnityResourceLoader` 对同一标识、同一种类的后到请求并入在途请求，只读取与解码一次，各回调恰好一次；种类不同或带精灵集提示的重载仍各自独立。
- **无后缀 `jump` 跳转与初始朝向整身变体**：外形声明了无后缀 `jump` 时默认登记它，空中姿势回退链第二级对只有 `jump` 的外形生效；没有纸娃娃层的外形在挂接时探测初始方向的整身变体。
- **渲染插值系数按引擎时间计算**：新增适配层纯函数 `RenderInterpolationClock`，连续模式下按引擎时间逐渲染帧计算插值系数（宿主整步喂入使核心系数恒近零），两处生产帧循环接入；核心语义不变。
- **`GameOptions.PathFailurePolicy` / `GameOptions.BlockingChangePolicy`**：游戏模板暴露移动选项里的两个策略，默认值即框架默认（`KeepOldPath`、`Replan`）。
- **`lab/README.md` 真机手测清单补第 6 项**：高刷屏插值平滑、分层图加载卡顿、音量滑条即时生效、销毁时循环音效停止四项引擎侧专项观察。

**工具链与文档（NF3）**

- **`item.slot_definition.has_appearance`**：可选布尔字段，缺省 `true`；写 `false`（戒指、项链等不上身可见的槽位）时，装备资产包校验不要求该槽位物品有 `display.equip_visual` 行、不报 `equip_visual_missing`。运行期核心不读取它。
- **`simrunner fight` 子命令**：直接指定标准玩家（职业、等级、品质）对一只生物打一场，`--fight-log <file>` 把逐条战斗日志写成 `{schema_version, truncated, entries}` 的 UTF-8 JSON，`--max-log-entries` 限制条数；退出码 0（跑完，不论胜负）、2（参数或数据装载错误）、4（取消），与 `run` 的退出码语义互不相干。补上消费方反馈第 49 条当时"命令行没有单场战斗入口"的缺口。
- **`gate_floors.json` 下限上调**：dotnet、pytest、EditMode、PlayMode 四个套件的 `min_passed` 按 full-20261001-33 实测上调为 8150、1170、160、400。
- **门禁中途被中断时补写耗时记录**：`check.ps1` 被未接住的异常中断时，已完成的步骤行照写，`_total` 行 `result=FAIL`、`note` 以 `aborted:` 开头。
- **`prune_dist` 清理发布标签说明文件**：`dist\tag-message-<ver>.txt` 在标签 `v<ver>` 已存在时识别并清理，标签不存在时仍按"未识别，已保留"处理。
- **UPM 证据覆盖超时分支**：`consumer_smoke.ps1` 的五个 Editor 步骤在超时被强杀时同样按签名抓取包管理器证据。
- **版本控制配置守卫豁免分支与远端小节**：测试会话期间别的会话正当修改 `[branch …]`、`[remote …]` 小节不再导致整个会话误报；其余小节仍受守卫。
- **改写为设计决定**：引擎适配层包不进 API 手册、持续集成依赖发布产物（缺失即红灯，有意）、开发期热重载改坏一张表时整个数据集阻断、`playmode` 日志无边界时诚实退化到 `not_found` 等；Release 工作流的线上验证登记为 `lab/README.md` 验收清单第 8 项。

**迁移说明**

- **不声明新能力：什么都不用做，行为与清扫前逐位一致。**新增的都是构造重载（`SfxPlayer` 7 参数）、方法重载（`ExpectedAffixInclusion` 5 参数）、只读属性（`ExpectedRoundedAmount`）、可选数据字段（`has_appearance`）、`GameOptions` 的默认成员与新的 `simrunner` 子命令；旧签名物理保留，旧编译的消费方不必重编。
- **唯一的行为改进**：`wall_slide` 的滑动终点被可走性判定拒绝时，单位现在停在撞击点，此前整个 tick 不位移。只在原本就会卡住的情形生效，其余路径不变。
- **自写 `SfxPlayer` 装配的游戏**：要让实体销毁后循环音效停止，改用 7 参数构造并传入实体位置解析器；生产装配已接入。
- **依赖线性货币期望的编辑器面板**：`ExpectedAmount` 不变；要与 `RollDetailed` 观测均值对得上的小额货币，改读 `ExpectedRoundedAmount`。
- **槽位不上身可见的游戏**：在 `item.slot_definition` 里写 `has_appearance: false`，不再需要补空外观行。

## [1.95.0] - 2026-10-02

本段汇集手感落地 M3（1.94.0 之后，手感机制的已知限制补完与实验室扩充）与 Release 工作流修复。**不开手感（`FeelOptions`/`feelOptions` 为空）、不声明 `unit_body_radius`、不声明 `launch_height`/`shape.height`、不配置 `MovementOptions.Vertical` 时，行为与 1.94.0 逐位一致**：实验室既有的 186 条 `suite` 基线与 277 条不变量在本段全部落地后原样通过，已有基线文件无一改动（本段实验室共 258 格、379 条不变量，增量全部来自新增脚本）。开手感或声明了体积半径的游戏有三处缺省行为变化，见下方"接入与迁移说明（M3）"。

### 新增

- **竖直轴能力包（可选开启，M3-E1，[ADR-0130](architecture/adr/0130-竖直轴能力包.md)，手感设计/06 第 10 节勘误 9，05 第 3.3 节随之修订为 v3）**：`volume`（体积空间）与 `side_2d`（横版二维）两个空间取值不再是预留标签，核心层提供真实语义，全部缺省关闭。①单位载体：`MovementOptions.Vertical`（`VerticalAxisOptions`：`Gravity` 缺省 30、`JumpHeight` 缺省 1.5、`AllowAirJump` 缺省假，世界单位，不经手感标定）非空时装配 `VerticalMotionHost`（实现 `IVerticalMotion` 与 `ILaunchSink`，`CarriersAssembly.VerticalMotion` 暴露）：脚下高度 `Unit.HeightOffset` 按解析式抛体 `h0 + v0·t − g·t²/2` 积分，`h ≤ 0` 落地写 0（地面恒为 0，没有斜坡与台阶），只积分被抛起的单位（悬空靶、飘浮怪是静态高度、不受重力），`Jump` 在空中默认被拒绝（返回假）。②目标选择：目标链形状（圆/扇形/线/矩形）新增可选 `height`（正数，命中高度窗口半宽），`TargetingOptions.VerticalHit` 打开才生效——候选脚下高度与施法者（地面坐标施法取地面 0）之差的绝对值不超过 `height` 才保留；`TargetingOptions.SpatialDistance` 打开后 `sort_by.distance` 与"最近"按含高度差的三维距离；两个选项缺省关，施法射程仍是平面距离。③击飞：手感档案新增可选判定型字段 `launch_height`（体型倍数，0～10，缺省无值 = 不击飞，`FeelFieldNames.LaunchHeight`），反应达到击退/击倒时顶点高度 = `launch_height`（标定后）×(1 − 击退抗性)×冲击等级倍率，经新接口 `ILaunchSink`（`HitFeelHost.Launch`，生产装配在世界装了竖直轴时自动接上）提交，与击退互相独立；没有竖直轴的世界里忽略。④读口：`IUnitAccess.GetHeightOffset`（默认接口成员，恒 0）、`TargetContext.OriginHeight`/`SpatialDistance`/`DistanceTo`、`HeadlessWorldOptions.MovementOptions`/`TargetingOptions`（透传，缺省 null）。公开签名只增不改。实验室：格子行可选 `gravity`/`jump_height`，靶子条目可选 `height`，度量组 `space`（条件组），新增六个 `space.*` 脚本（跳跃、命中高度窗口、击飞、深度锁、三维距离最近、无空间刺激对照）各十个格子（空间格子 `side_2d_targeted/action`、`volume_targeted/action` 的数据放 `lab/fixtures/data/space/`，不进基础数据集），新增跨格子不变量 `space_semantics_only`。已知限制：不做空中控制，竖直运动不被地形阻挡（没有斜坡、台阶、天花板），没有空中攻击/受击的专属反应，击飞中再次击飞按"从当前高度重新抛起"处理、不叠加速度，命中高度窗口用命中那一刻施法者的脚下高度、不考虑形状自身的竖直偏移。
- **单位体积阻挡补完（M3-A，[ADR-0128](architecture/adr/0128-单位间体积阻挡.md) 追加决定，手感设计/02 第 3.5 节）**：1.94.0 的五条限制逐项解除，全部只在本单位 `unit_body_radius > 0` 时生效。运动档案新增可选字段：`unit_separation_speed_ratio`（缺省 0.5）、`path_avoid_units`（缺省真）、`forced_push_units`（缺省假）、`forced_push_ratio`（缺省 0.5）、`pass_through_motion_kinds`（逗号分隔，取值 `lunge|dash|step_back|charge`，缺省 `dash,step_back`；`dodge_through_units` 保留为总开关，名字不认识在读档案时报错并带字段名）。①推开重叠：tick 末按"比例 × 基础移速"把重叠单位分摊着推开（每 tick 不超过权重 × 步长，先过地形再过成对接触；冻结/定身/死亡/受控位移中的单位不被推）。②路径跟随与追击局部绕行：沿目标方向去掉法向分量的切向走完剩余预算，偏向的一侧被挡死就停、不换侧（窄道不摆动）。③受控位移推人：剩余位移 × 转移比例 ×（1 − 被推单位击退抗性）转给挡路单位，下一 tick 起生效。④同 tick 顺序无关：tick 开头按 id 拍快照、成对相对运动求接触比例（迭代到收敛）、`unit.moved` 对有体积的单位延后到 tick 末按 id 发出，打乱创建与意图提交顺序结果逐位一致。⑤先撞墙滑动再撞体积改用折线逐段精确扫掠。新类型/成员：`MotionProfile` 23 参数构造（18/16/15 参数构造保留并转发）与对应只读属性、`FeelFieldNames` 五个新常量。实验室新增脚本 `feel_unit_separate`。已知限制（原样保留）：成对阻挡看到的是别人 tick 起点位置（跟随者最多滞后一个 tick）；对滑动或折线位移的成对检测用起终点的弦（保守，可能略多缩短一点，并经地形校验）；位移与到达事件在同 tick 又被成对裁决拉回时，事件里的位置是裁决之前的位置；追击与扑向目标读目标当前位置的既有顺序依赖不变。
- **顿帧同时冻结粒子/特效（M3-C，[ADR-0129](architecture/adr/0129-顿帧冻结特效与粒子的可选能力接口.md)，手感设计/07 第 5 节）**：新增两个可选能力接口（ABI 只加法，必需接口不变，自写实现不必改）：`IVfxFreezable`（`SetOwnerFrozen(ownerEntityId, frozen)`/`IsOwnerFrozen`，框架自带 `VfxPlayer` 实现）与 `IParticleFreezer`（`SetParticlePaused(handle, paused)`，引擎适配层的 `IRenderer2D` 实现可选实现，Unity 适配的 `UnityRenderer2D` 已实现，`EffectSequencePlayer` 新增 `SetPaused`/`IsPaused`/`PlayedSeconds`）。"谁是冻结宿主"的判据：以 `anchor`/`socket` 挂接到实体的特效属于该实体、随它冻结，`world`/`screen` 挂接的没有宿主、永不冻（命中闪光天然不冻）；是否冻结由挂接方式决定，不新增特效表字段；整层开关仍是反馈包 `freeze_layers.particles`（缺省假）。冻结是状态型：冻结期间新播放的该单位特效（含资源冷加载补发）同样从暂停起播，存活时长（`lifetime`）随暂停停住；没有 `IParticleFreezer` 的适配层上粒子既不暂停也不停表。装配根在 `OnFreezePresentation` 里除冻结 rig 外按 `freeze_layers.particles` 冻结名单单位名下的特效。
- **包内两个引导的开发期数据热重载（M3-C）**：Unity 适配新增 `BootstrapDataHotReload`；`GameFoundationBootstrap` 与 `FrameworkResidentHost` 各新增公开开关 `EnableDataHotReload`（缺省 `false` = 不挂组件，行为与此前逐位一致）与只读属性 `HotReload`。开启后轮询（250 ms）各数据根下 `*.json` 的写入时刻与长度，变更去抖 300 ms 后 `DataRegistry.Reload(表名)`，成功补发 `data.load_completed`、失败补发 `data.validation_failed` 并落校验报告；只在 `UNITY_EDITOR || DEVELOPMENT_BUILD` 下有实际行为。
- **宽限窗口与手感热重载的已知限制全部解除（M3-B，手感设计/01 第 2.4 节、05 第 8 节）**：①地面坐标施法携带宽限条件：新增 `GroundCastRequest.WithGraceConditions(conditions)`，`CastSkillAtGround` 的射程/视线检查与对单位施法同一口径（只放宽射程与视线，落点不可行走、冷却、资源照常；请求被放行后落地再校验同样放行）。②排队中的施法保留宽限上下文：进队列时记快照，出队按行动者动作时钟判窗，过期按原规则拒绝；新增 `IGraceQuery.RemainingGraceTicks`（接口缺省成员，旧第三方实现不必改）。③框架缺省的 Expr 宽限求值：新增 `ExprGraceConditionEvaluator`，`found.grace_condition` 每行 `expr` 在行动者上下文求值，游戏不再必须写求值代码；`CarriersFeelOptions.GraceEvaluator` 仍可覆盖，新增 `GraceTargetResolver`（缺省取自动攻击的当前目标）；`CarriersFeelSystem.Grace` 现在恒装配，新增只读 `GraceEvaluator`。④非本地行动者从登记起采样：新增 `InputBufferHost.RegisterActor`/`IsActorRegistered`，生产装配自动登记世界里的 `player`/`creature`，`CarriersFeelOptions.AutoRegisterGraceActors`（缺省真）可关。⑤标定热更换：新增 `FeelResolver.Reload(profiles, calibration)` 重载（旧重载转调），标定行变化不再要求重启，进行中的动作沿用开始时的快照、下一个动作按新标定；`CameraHost.EnableFeel(source, Func<double>)` 与 `ImpactOptions.ReferenceHeightSource` 新增实时参考高度入口（旧固定值入口不变）。已知限制（原样保留）：地面施法的宽限条件须由游戏声明得能表达"落点/目标刚才还够得着"；缺省 Expr 求值器的目标来源取自动攻击目标；单位很多时自动登记会给每个单位建一份空缓冲（可关）。
- **实验室脚本期望清单与导出生成期望（M3-E2，手感设计/06 第 3.1、3.5 节）**：输入脚本顶层可带 `expectations`（脚本格式版本 4，版本 1～3 的旧脚本照常读取，没有期望的脚本序列化文本与基线逐字不变）：每条指定度量（数组度量可取下标或聚合）、适用格子与比较（`eq`/`ne`/`lt`/`le`/`gt`/`ge`/`between`/`approx`/`in`/`contains`），比较的另一端可以是字面值或另一个度量（可来自另一个格子，按倍率与偏移换算，用来写"动作式比目标选择式顿帧更久"这类跨格子关系）。期望随 `suite`/`run` 判定，任一条失败或无法判定（度量/格子不存在、类型不符）该格子算有差异，诊断写明要求与实测；引用不存在的度量组、度量或格子是脚本内容错误；期望不进指纹与基线身份，增删期望不改脚本版本、不使基线失效。`suite` 在有期望时另输出一行 `RESULT expectations total=<n> pass=<n> fail=<n>`。`export-test` 现在由指纹生成可编辑的期望（逻辑组精确、表现组带容差、各格取值相同的合并成一条，空值与长序列不导出，实时类默认不导出；新增参数 `--no-expect`、`--expect-groups`、`--expect-realtime`），再次导出只重生成自动生成的、保留手写的。六个脚本带示范期望（`feel_breakable`、`feel_combo3`、`feel_elite_flinch`、`feel_kill`、`feel_motion_wall`、`feel_stake_poise`）。靶子条目（`lab.dummy_set`）新增可选字段 `block_half_extent`（动态阻挡，被打死后移除，各手感场景通用，取代基础集里的近似）与 `poise`（经 `stat.poise` 出场写入，缺省行为不变），新增脚本 `feel_stake_poise`。
- **框架级假人姿势集补齐（M3-D，手感设计/04 第 10 节勘误 7、8，13 第 9 节）**：两版假人姿势集（序列帧版 `display.anim_set.std_dummy_biped`、骨骼剪辑版 `display.anim_set.std_dummy_biped_model`）键清单由 34 键增至 103 键（94 份独立剪辑，其余为别名键），**既有 34 键的资源与剪辑字节不变，只追加**。①可选键全部出齐：`move.sprint`（含 `.combat` 与各族）、启停过渡 `move.start`/`move.stop`/`move.pivot`、击飞 `hit.launch`、眩晕 `stunned`、格挡 `block`、带伤变体 `idle.wounded`/`idle.combat.wounded`/`move.walk.wounded`/`move.run.wounded`；运行期不再对这些键回落。②新增五个武器族 `polearm`/`bow`/`staff`/`dual`/`shield`（各一套待机、战斗待机、走/跑/战斗走/战斗跑/冲刺与攻击；长柄、法杖、双持有 `.02`，双持另有 `.03`；弓只一段攻击并在 `hit` 同刻另放 `release`；盾牌另有 `block.shield`），`1h`/`2h` 族补战斗走与冲刺；武器层是方块占位。③体量轴落轻/重两组骨骼版偏移姿势集：数据行 `display.anim_set.std_dummy_biped_model_light`/`_heavy`（`extends` 主集，各 8 个键，只改待机、战斗待机与移动站姿，攻击/受击等沿主集），步幅不出独立资产、仍由运行期播放速率表达。④序列帧版攻击剪辑的数据行事件表末尾追加一条与 `hit` 同时刻的 `hit_frame`（既有事件内容与顺序不变），序列帧播放器据此触发 `ICharacterRig.HitFrameReached`，与 model 型同名同时刻；骨骼版烘入时数据行已有同名同时刻事件则不重复烘。⑤骨骼控制器改由生成脚本确定性写出（状态与中继行为的 fileID 取状态名的 FNV-1a 散列，控制器 `.meta` guid 稳定），重复生成逐字节一致，不再因重新生成换 guid；临时预制体烘入 `genericBindings` 后即删。⑥序列帧版自检 `gen_std_dummy_poses.py --check` 接入门禁（`module_map.json` 与 `_gate_line_heavy.ps1`，`-Quick` 跳过，缺图像库时按快速档跳过），与骨骼版自检并列；装备集 `std_equip_set` 由全部假人剪辑派生，新键与新族自动级联，`block` 并入派生的族状态。资产增量约 15 MB（发布包体积相应增加）。已知限制（原样保留）：体量轴只有轻、重两组、只改待机与移动站姿；`wounded` 变体只覆盖待机与走/跑；新增五个族的武器块是方块占位，格挡、眩晕、击飞是关键姿势插值的占位动作、不含物理；序列帧版没有体量组（体量差异靠换姿势集）；骨骼剪辑不带根运动、控制器状态间没有过渡连线（沿用既有）。
- **实验室规模**：`feellab suite` 由 186 格增至 258 格（十个旧脚本与二十三个手感脚本各六格 198 + 六个空间脚本各十格 60），期望清单 98 条判定，`invariants` 由 277 条增至 379 条（拆分算式见 `lab/README.md` "数量口径"）。发布包里随附的实验室数据与夹具同步更新，消费方按 `dotnet toolchain/feellab/bin/FeelLab.dll suite` 应得 `RESULT total=258 pass=258`。

### 变更

- **手感装配的已知限制再关闭一批**：1.94.0"接入与迁移说明"与各模块 README 里列出的"宽限条件不能用于地面施法与排队施法""宽限求值必须游戏自写""非本地行动者的第一次按键可能早于采样""标定变化需要重启""粒子宿主不随顿帧冻结""包内引导没有热重载"六条，在本段起均已解除（见上）。假人姿势集方面，1.94.0 "框架级假人姿势集骨骼剪辑版"条目里的"骨骼剪辑只出一组中等体量""可选键与五个武器族不出""序列帧版没有命中帧关键帧""控制器重新生成会换 guid""序列帧版自检未进门禁"几条限制同样解除（M3-D，见上）。

### 接入与迁移说明（M3）

- **不开手感、不声明新字段：什么都不用做，行为与 1.94.0 逐位一致。** 公开签名只增不改（新增的是重载、新类型、接口缺省成员、可选属性，旧签名物理保留），旧编译的消费方不必重编；`feellab` 既有的 186 条基线与 277 条不变量原样通过。
- **声明了 `unit_body_radius` 的游戏（只影响它们）：体积阻挡的两个新缺省值会改变行为。** `unit_separation_speed_ratio` 缺省 0.5（重叠的单位现在会被推开；1.94.0 里重叠只会拦"让距离变近"的位移）、`path_avoid_units` 缺省真（路径跟随与追击现在会局部绕行；1.94.0 里撞到即停）。想保持 1.94.0 行为，在运动档案（预设或覆盖行）里显式写 `unit_separation_speed_ratio: 0` 与 `path_avoid_units: false`；`forced_push_units` 缺省仍是假，`pass_through_motion_kinds` 缺省 `dash,step_back` 与此前写死的行为一致，不需要改。另外对有体积的单位，`unit.moved` 现在延后到 tick 末按单位 id 顺序发出（带最终位置），依赖"移动事件在 tick 中途按处理顺序发出"的订阅方要留意。
- **开手感且动作声明了 `grace_conditions` 的游戏：宽限现在可能开始生效。** 1.94.0 里没有提供宽限求值器就不会有任何宽限（施法管线步骤 7 照常拒绝）；现在缺省由框架的 Expr 求值器读 `found.grace_condition` 求值，数据里有对应条件行就会放行。不想要宽限就删掉动作上的 `grace_conditions` 声明；仍想用自己的求值方式则照旧给 `CarriersFeelOptions.GraceEvaluator`（优先于缺省）。同时生产装配现在为世界里的 `player`/`creature` 自动登记输入缓冲，没有动作声明宽限条件时不采样、无可观察行为差异；单位很多且只有本地玩家用宽限时可设 `AutoRegisterGraceActors = false`。
- **声明了 `freeze_layers` 的反馈包（只影响它们）：层声明现在真正生效。** 见下方"修复"：1.94.0 里反馈包的 `freeze_layers.particles`/`trail` 在生产链路里从不生效（恒为缺省假），现在按声明生效；没声明的反馈包（含框架全部缺省数据）不变。不想冻结就把对应层写成假或删掉声明。
- **粒子随顿帧冻结要两样都具备**：反馈包 `freeze_layers.particles` 为真，且特效播放器实现 `IVfxFreezable`（框架自带的 `VfxPlayer` 已实现；自写特效播放器不实现则静默不冻），2D 粒子还要求引擎适配层的渲染实现同时实现 `IParticleFreezer`（Unity 适配已实现；自写适配层不实现则粒子不暂停，行为与此前相同）。
- **开启竖直轴**：装配时给 `MovementOptions.Vertical` 赋值（世界单位的重力与跳跃高度）；要让近战区分高度，目标选项打开 `VerticalHit` 并在目标链形状里写 `height`，体积空间再开 `SpatialDistance`；要击飞，给攻击方预设写 `launch_height`。三者彼此独立，缺省都关。
- **使用框架假人姿势集的游戏（只影响它们）**：键清单只追加、既有 34 键不变，旧数据与旧引用不必改；新增的键与族只有在单位的状态、装备族或体量选择命中时才被取到。唯一的缺省行为变化来自序列帧版数据行在每个攻击剪辑的事件末尾追加了 `hit_frame`：事件表里既有事件的内容、顺序与个数不变（只多一条），除非自己的代码按"攻击剪辑事件数"断言；可观察影响只在两处——订阅 `ICharacterRig.HitFrameReached` 的代码，在 sprite 型假人单位的攻击里现在会收到一次（此前收不到）；把 `HitFrameSync` 设为 `AnimKeyframeDriven` 的游戏，对假人攻击的命中同步反馈现在落在命中帧而不是等 0.5 秒超时兜底。`HitFrameSync` 缺省 `LogicDriven` 时没有任何变化。要保持旧行为，用自己的 `display.anim_set` 行而不是假人行。装备集（`std_equip_set`）资产随之重新派生。
- **包内引导热重载**：自带引导的游戏要在开发期使用，设置 `EnableDataHotReload = true`（在 `Awake`/`Ensure` 之前赋值或在 Inspector 勾选）；缺省关闭。

### 修复

- **顿帧的反馈包层声明（`freeze_layers`）在生产链路里从不生效（M3-C）**：`feel.hitstop_started` 在 tick 末排队发出，到达反馈流水线时命中自己的打击计划通常已在前一次出批里下发，原实现只在同批计划里找相关命中，`freeze_layers.particles/trail` 恒为缺省（单测把命中与顿帧塞进同一批才没暴露）。现在流水线保留最近 3 次出批的命中（更老的丢弃，不把很久以前的反馈包套到无关的顿帧上）。行为收紧：此前声明了 `freeze_layers` 的反馈包在生产里不起作用，现在起作用，见迁移说明。
- **Release 工作流不再在 tag 触发的运行里重建附件**：`.github/workflows/release.yml` 的"全部附件缺失 -> 托管运行器全量重建"路径在 tag 触发的运行里必被 `build.ps1` 的发布不可变守卫拒绝（v1.85.0～v1.91.0 的红灯），重建出的也是同一版本号的第二套字节。现在改为附件核对步骤里提前明确失败并打印人工指引；删除 `build.ps1 -SkipTests`、`dotnet tool restore`、`build.ps1 -SyncOnly -Dist -Zip` 三个构建步骤；守卫一行未改。新增 `toolchain/_release_assets_plan.ps1`（附件处置纯函数 `Get-WsGameReleaseAssetsPlan`：Skip/Repair/Blocked）与 `toolchain/tests/test_release_assets_plan.py`（含全部 512 种已有附件子集的不变量与把工作流步骤正文抽出、以桩 `gh` 模拟 tag 触发运行的用例）。

## [1.94.0] - 2026-10-02

本段汇集手感落地 M2（1.93.0 之后的手感生产接线与缺口补齐）与 CI/Release 工作流修复。**所有 M2 机制缺省不开启**：不开手感（`FeelOptions`/`feelOptions` 为空）、不在运动档案里写 `unit_body_radius`，行为与 1.93.0 逐位一致（`feellab suite` 与 `invariants` 基线未改既有条目）。

### 新增

- **单位间体积阻挡（可选开启，M2-C，[ADR-0128](architecture/adr/0128-单位间体积阻挡.md)，手感设计/02 第 3.5 节）**：运动档案新增可选字段 `unit_body_radius`（单位体积半径，身高倍数，缺省不声明即无体积）与 `dodge_through_units`（缺省假；`dash`/`step_back` 类动作位移是否穿过别的单位的体积）。只有本单位 `unit_body_radius > 0` 才进入体积分支，成对语义（双方都声明才互相阻挡）；位移当作线段做连续扫掠，高速位移不隧穿；`regular` 来源按 `wall_slide` 停下或沿切向滑开，`action` 按动作 `blocking` 声明停下或滑开、`dash`/`step_back` 在 `dodge_through_units` 为真时穿过（扑击等永不穿过），`forced`（击退）被挡即以"被阻挡"收场、不推人；追击停步距离取"声明值"与"半径之和加两个到达容差"的较大者。新类型/成员：`MotionProfile` 18 参数构造（旧 15/16 参数构造保留并转发）、`FeelFieldNames.UnitBodyRadius`/`DodgeThroughUnits`、`MovementTickHandler.UnitVolume.cs`。旧的全局 `MovementOptions.UnitBlocking` 一字未动，二者独立。实验室新增标准脚本 `feel_unit_block`（六格各一条基线，suite 180 -> 186）与对应不变量（invariants 270 -> 277）。已知限制：不推开重叠单位（出生重叠只拦"让距离变近"的位移）、路径跟随与追击不绕单位寻路、受控位移不滑、单位按顺序处理只看别人当前位置。
- **手感实验室随发布产物分发（M2-D，手感设计/06 第 8 节第 2 步、13 第 9 节第 6 步）**：只消费发布产物的游戏现在可以自己跑实验室。发布包 `dist/<版本>/` 新增预编译命令行 `toolchain/feellab/bin/FeelLab.dll`（另带 `lib/` 9 个预编译 DLL 与空 `Directory.Build.props`，与 `simrunner` 同一治理；`FeelLab.csproj` 在源码树存在时引用内核工程、不存在时引用 `lib/`）、实验室数据集 `data/_lab`、`data/_lab_action`、占位装备集 `data/_equip` 与标准脚本/基线夹具 `lab/fixtures`（与仓库同路径，dist 根即实验室根）；`MANIFEST.txt` 新增 `[feellab]` 段（`FeelLab.dll`/`Lab.Kernel.dll` 的 sha256）；工具链私服包 `com.gamefoundation.toolchain` 新增 `Tools~/feellab/{bin,lib}` 与自包含的 `Tools~/feellab/labroot/`。命令：`dotnet toolchain/feellab/bin/FeelLab.dll suite`（应与框架基线一致，`RESULT total=186 pass=186`），自己的数据根用 `--data-root` 叠加；只需要 dotnet 运行时，不需要引擎。消费方演练 `toolchain/consumer_smoke.ps1` 新增一步用 dist 里的预编译命令行跑 `suite` 与 `invariants`；`check.ps1` 的 pkg_manifest 步骤要求 toolchain 包含代表文件。这一条取代 1.93.0"接入与迁移说明"里"实验室不随发布产物分发"的限制。
- **输入宽限窗口被施法管线消费（M2-B，手感设计/01 第 2.4 节）**：`found.input_action.grace_conditions` 此前只装配追踪、不被消费，现在生效：缓冲出口把动作声明的条件名随 `cast` 意图（可选字符串数组 `grace_conditions`）带给施法；步骤 7（射程与视线）在"声明的条件全部满足且至少一个正处于宽限窗口"时放行，只放宽射程/视线，冷却、资源、目标合法性、动作锁不放宽；时间线技能不走步骤 6/7、不受影响。新增：`SkillHost.CastSkillWithContext` 与 `CastPipeline.CastSkillWithContext` 的 5 参数重载（末位 `IReadOnlyList<Id>? graceConditions`，旧 4 参数重载原样保留并转发 null）、`TimelineServices.Grace`（`IGraceQuery?`）、`InputBufferHost.GraceConditionNames`/`LocalActorId`（只读）；`InputBufferTickHandler` 对本地行动者与全部有缓冲的行动者登记并采样宽限条件（此前生产里 `GraceTracker` 从不登记行动者，宽限恒为"无记录"）。已知限制：地面施法请求不带宽限条件；排队中的施法丢失宽限上下文；框架不提供基于 Expr 的 `IGraceConditionEvaluator`，游戏需经 `CarriersFeelOptions.GraceEvaluator` 提供；非本地行动者的第一次按键可能早于采样。
- **手感数据热加载（M2-B，ADR-0019 / 05 第 8 节）**：开发期改 `feel.*` 表并 `DataRegistry.Reload` 后，宿主补发 `data.load_completed`，核心装配订阅它并换入新档案，无需重启。新增 `FeelSystem.TryReload(IDataRegistryView)` -> `FeelReloadResult`（重读 `feel.*`、过同一套 `FeelProfileChecker`，校验不过则拒绝、保持当前档案、原因写进结果与 `CarriersFeelSystem.LastHotReload`，不抛异常、不换入半份数据）与 `FeelWeaponCatalog.Reload()`；成功后同步刷新动作绑定、运动模式规则，换装链对全部单位 `ReconcileAll("data_reloaded")`（变化的单位照常发布 `feel.weapon_changed`）。进行中动作的手感快照不变；**标定不热换**（`CalibrationChanged` 为真时重启生效）。游戏模板 `DataHotReload` 已在 `Reload` 之后补发该事件并监视开手感时自动加入的框架手感根；引擎侧用例改框架 `feel.preset` 基础档的 `buffer_ms` 并热重载，单位读到新值。
- **步态喂入与单位销毁清理（M2-B）**：新增 `PoseGaitFeeder`（`PresentationAssembly` 在手感启用时装配）：按单位运动速度比喂 `PoseSelector.Observe`，移动姿势按 idle/walk/run/sprint 解析（姿势集缺步态剪辑时仍沿回落链回落）；`GaitDeriver` 修正完全静止时停在 walk 的边界（缺省阈值下 `idle_max_ratio` 与滞回同为 0.05）。`EquipmentPoseBridge` 订阅 `entity.destroyed` 清理选择器记账；`EquipmentFeelChain` 订阅 `entity.destroyed`（忘掉对账状态）与 `entity.created`（对账一次），同 id 重建的单位重新对账并重发 `feel.weapon_changed`，`save.loaded` 对账抽成公开的 `ReconcileAll(reason)`。
- **光环层数叠乘（M2-B，手感设计/05 第 6 节）**：`AuraSnapshot` 新增只读 `InstanceId` 与 8 参数构造重载（既有 5、7 参数构造原样保留；查询实现不填则为 `null`，回落到按定义 id 区分）；`FeelTemporaryEntry` 新增 `Stacks`（缺省 1，3 参数构造重载，旧 2 参数构造委托）。光环临时手感条目键由定义 id 改为光环实例 id，第 7 层数值 `multiply`/`add` 按层数逐次叠乘/累加（不用 `Math.Pow`，保证确定性），`set` 与列表操作幂等；`AuraFeelTemporaryProvider` 订阅 `aura.stack_changed` 使缓存失效。
- **顿帧冻结落到渲染 rig（M2-A，手感设计/07 第 5 节）**：新增可选接口 `IPresentationFreezable`（`IsPresentationFrozen`、`FreezePresentation(freezeTrail)`、`UnfreezePresentation()`），`SpriteCharacterRig`/`ModelCharacterRig` 实现；`IFrameAnimPlayer` 新增带缺省实现的成员 `IsPaused`/`SetPaused`（旧实现不实现也能编译，等价于不暂停）；`ICharacterRig` 一个成员都没加。冻结是幂等的布尔开关：序列帧暂停、程序动画时间轴停住（闪白不冻，拖尾按冻结层 `Trail`）、model rig 动画速率写 0 并在解冻时写回最近一次请求值；冻结期间才附上的播放器与资源后到的原地替换也保持冻结（冷路径同热路径）。`PresentationAssembly` 默认把 `OnFreezePresentation`/`OnReleasePresentation` 落到 `feel.hitstop_started/ended` 名单里各单位视图的 rig，调用方显式回调仍在其后照常调用。已知限制：粒子宿主与引擎物理/粒子的暂停不在本接口范围，仍靠显式回调自行接入。
- **引擎侧镜头冲量与引导接线（M2-A，手感设计/07 第 2 节）**：Unity 适配层 `UnityCamera` 实现 `ICameraImpulse`（恒声明 `SupportsCameraImpulse`，`CameraHost.Impulse` 直接转发，不再退化为 `Shake`）：位移峰值 = 幅度 × 画面高度（正交相机 `2 × orthographicSize`），沿命中方向推开并线性衰减回零，历时 `decayMs`，零方向取各向同性；冲击位移独立于震屏与跟随基准维护。`GameFoundationBootstrap` 与 `FrameworkResidentHost` 新增公开属性 `FeelOptions`（缺省 `null` = 不启用）与常量 `FeelDatasetRoot`，开启时数据源顺序为框架根 -> 手感根 -> 游戏根，`GameplayAssembly` 改走最长构造重载并透传；开手感时，被输入缓冲声明了类别的动作不再由引擎直接提交施法意图、改经缓冲出口（避免一次按键施法两次），未声明类别的动作仍直接提交。`UnityRenderer3D` 新增 `AnimSpeedOverride`（冻结期间后到资源保持冻结速率），`UnityFrameAnimPlayer` 暂停标志存在组件上。
- **框架级假人姿势集骨骼剪辑版（M2-E，手感设计/04 第 10 节勘误）**：新增数据行 `display.anim_set.std_dummy_biped_model`（与序列帧版 `std_dummy_biped` 并列、命名事件同源），带 17 骨骼占位人形模型 `model.std_dummy_biped` 与 33 份骨骼剪辑，覆盖全部必备与推荐姿势键及单手/双手武器族变体；剪辑由规格文件 `assets/_placeholder/std_dummy_model_clips.json` 经 `toolchain/gen_std_dummy_model_clips.py` 确定性生成，门禁新增 `--check` 一致性步骤（缺键、缺事件、帧数不一致、骨骼路径不匹配、关节角超限均拦截）。引擎侧 `UnityRenderer3D.RaiseAnimEvent` 支持带参数事件名（`cancel_open:dodge` → `anim_event.cancel_open.dodge`），非法名丢弃不抛；model 剪辑在每个 `hit` 旁另带同刻 `hit_frame` 别名。限制：只出一组中等体量与标称步幅；占位模型为方块无蒙皮；剪辑不带根运动。

### 变更

- **手感装配的已知限制关闭**：1.93.0"接入与迁移说明"里列出的"生产装配不喂步态""手感数据热重载未接线""姿势选择器在单位销毁时不清理""实验室不随发布产物分发"四条，在本段起均已解除（见上面对应条目）；光环临时手感条目键与层数语义随 M2-B 改为"实例 id + 层数叠乘"（只影响开手感且使用光环临时手感条目的游戏）。

### 接入与迁移说明（M2）

- **不开手感：什么都不用做，行为与 1.93.0 逐位一致。** 不传 `FeelOptions`/`feelOptions`、不在运动档案写 `unit_body_radius`/`dodge_through_units`、不写 `found.input_action.grace_conditions`，装配、事件序列与表现输出都不变；本段所有新增字段均为可选。
- **公开签名只增不改，旧编译的消费方不必重编**：新增的是重载（`CastSkillWithContext` 5 参、`AuraSnapshot` 8 参构造、`MotionProfile` 18 参构造、`FeelTemporaryEntry` 3 参构造，旧签名物理保留）、默认接口成员（`IFrameAnimPlayer.IsPaused`/`SetPaused`）、可选接口（`IPresentationFreezable`）与新类型/属性。
- **自定义 rig 与顿帧**：开手感后 `PresentationAssembly` 会自动冻结实现了 `IPresentationFreezable` 的 rig。自定义 rig 若**不想**被顿帧自动冻结（要完全自管冻结），不要实现 `IPresentationFreezable`，经 `OnFreezePresentation`/`OnReleasePresentation` 回调自行处理即可；未实现该接口的 rig 被静默跳过。自定义的 `IFrameAnimPlayer` 不实现新成员也能编译，但不会随顿帧暂停。
- **热重载**：自家宿主若要让手感数据热重载生效，在 `DataRegistry.Reload(表)` 之后补发 `data.load_completed`（游戏模板的 `DataHotReload` 已这样做）；标定行变化仍需重启。
- **引擎侧**：自带引导的游戏改用 `GameFoundationBootstrap`/`FrameworkResidentHost` 时，设置它们的 `FeelOptions` 即可开启；开手感后普攻要走缓冲，数据里需给对应动作写 `class` 与 `skill_slot`（见 `architecture/13_新游戏接入指南.md` 第 9 节）。
- **单位体积**：要开启单位间体积阻挡，给对应预设/覆盖行写 `unit_body_radius`（身高倍数，大于 0）；双方都声明才互相阻挡；要冲刺/后撤穿过敌人另写 `dodge_through_units: true`。

### 修复

- **Release 工作流四个 `.tgz` 一直没补上 GitHub Release（两个不同根因）**：`.github/workflows/release.yml`"Check for existing release assets"一步在 v1.92.0、v1.93.0 都 exit 1。v1.93.0：zip/lock 的 `git_commit` 是发布机 `git rev-parse --short` 的 8 位（`b7ac4dc3`），托管运行器浅克隆的 `--short` 只有 7 位（`b7ac4dc`），字符串全等比较误判"提交不一致"；改为对完整 sha 做前缀比对（`toolchain/_lock_writeback.ps1` 新增 `Test-WsGameCommitMatch`，记录值至少 7 位十六进制且是完整 sha 的前缀）。v1.92.0：1.92.0 发布包的 zip 条目分隔符是正斜杠、1.93.0 是反斜杠，workflow 用反斜杠字面量精确匹配 `MANIFEST.txt`/`packages/*.tgz` 条目，报"no MANIFEST.txt at expected path"；改为分隔符无关的 `Get-WsGameZipEntry`。新增 `toolchain/tests/test_release_commit_match.py`。
- **CI 工作流（`check.ps1 -SkipUnity`）pytest 阶段 7 失败 + 5 跳过**：托管运行器的环境与本机不同、而用例按本机写——`ci.yml` 的 checkout 改 `fetch-depth: 0` + `fetch-tags: true`（补齐 `test_ref_conventions.py` 三条要 `git show` 历史提交的用例，以及 `test_dist_immutability_guard.py` 两条要真实 `vX.Y.Z` 标签的用例）；新增"下载最新已发布 zip+lock 到 `dist/`"三步（带版本号缓存，补齐 `test_lock_writeback_repair_parity.py`/`test_get_framework_path_boundary.py` 的三条真实产物用例）；`test_unity_path_length_guard.py` 的"短根路径"用例不再用 pytest `tmp_path`（托管运行器上 108 字符）；`test_gate_step_runner.py::test_two_lines_run_concurrently` 改读汇总表"并行阶段墙钟"而不是含运行器固定开销的外层总墙钟。不放宽 `gate_floors.json` 的 skip 上限、不批量 skip。

## [1.93.0] - 2026-10-02

本版主题是手感体系落地（设计见 `architecture/手感设计/` 与 ADR-0113～0123，上一版已入库设计文档集）：输入缓冲、动作时间线、空间命中、运动仲裁、局部顿帧与受击裁决、打击反馈、姿势维度、换装链路与手感实验室。**所有机制默认不开启**：不写任何新增字段、不给装配入口传 `CarriersFeelOptions`（`GameplayAssembly` 的 `feelOptions`、`HeadlessWorldOptions.FeelOptions`、`PresentationAssemblyOptions.FeelResolver` 均缺省 null）时，既有游戏的移动、结算、事件序列、表现输出与此前逐位一致（运行时凭据：关闭手感与"启用且 `rpg_classic` 预设、无时间线技能、无带类别输入动作"两个世界逐 tick 位置/运动模式/事件序列逐位一致；`feellab suite` 180/180 与既有各模块用例为证）。公开签名只新增（新增重载、默认接口成员、可选字段、新类型），旧签名原样保留。接入与迁移说明见本版末尾"接入与迁移说明"小节。

### 新增

- **手感档案与分层解析（ADR-0113/0118）**：新增数据域 `feel`（八张表：`feel.preset`/`feel.archetype`/`feel.weapon`/`feel.character`/`feel.action`/`feel.calibration`/`feel.motion_mode_rules`/`feel.tag_map`），新模块 `core/foundation/feel`：字段登记 `FeelFields.Default`（名字、类型、范围、判定型/呈现型半属、分组、允许的覆盖操作、合成来源、单位）、八层覆盖解析器 `FeelResolver`（预设 → 体型原型 → 标签映射 → 武器 → 角色 → 动作 → 临时状态 → 调试；层内 `set → multiply → add → remove`，限幅记录、按单位缓存、版本号、字段溯源、标定换算与动作开始快照）、判定型/呈现型两个只读视图（读另一半抛 `FeelHalfViolationException`）、九项静态检查与三条注册表规则、装配（没有 `feel.*` 数据即不装配；有数据而无标定装配失败）。`data/_feel` 是框架自带手感数据（两个预设、体型原型、武器原型、运动模式规则，实验性试调起点，含缺省标定行 `feel.calibration.framework_default`，可单独校验），随 `dist/<ver>/data/_feel`、框架数据包与 `StreamingAssets` 同步一起发出，与 `data/_framework` 并列作为框架根；真实游戏写自己的标定行并在装配时用 `CalibrationId` 指定。`FieldSchema` 新增可选 `Feel` 元数据（只设一次）。新增可选数据字段（各表 schema 版本不升，旧数据零改动合法）：`creature.template.feel_archetype_ref`/`feel_ref`、`item.template.feel_weapon_ref`、`skill.aura_def.feel_modifiers`。
- **输入缓冲与宽限（ADR-0115，手感设计/01）**：`found.input_action` 新增可选字段 `class`/`buffer_ms`/`priority`/`hold_threshold_ms`/`repeat_policy`/`face_on_accept`/`grace_conditions`/`skill_slot`，新增登记表 `found.grace_condition`；`InputBufferHost`（每行动者缓冲槽：同类别覆盖、高优先级替换、点按/按住、重复策略、过期、清空）、`GraceTracker`（宽限窗口）、`InputBufferTickHandler`（步骤 1）、拉取接口 `IInputBufferQuery`（带"能否接受"谓词的 `TryConsume`/`ReportRejected`，默认接口成员）与反向接口 `IBufferedIntentSink`。缓冲过期按行动者动作时钟计（顿帧期间不流逝），宽限按模拟 tick。`class` 缺省为空即不缓冲，旧输入行为不变；离散步只清空缓冲。`ActionDefinition` 旧 5 参构造保留。新增事件 `input.buffer_dropped`（原因 `Replaced`/`Full`/`Expired`/`Cleared`/`Rejected`）。
- **动作时间线与空间命中（ADR-0114/0115，手感设计/01、03）**：`skill.def` 新增可选块 `timeline`（`startup_ms`/`active_ms`/`recovery_ms` 三相，`markers`、`cancel_windows`、`combo`、`charge`、`cost_at`/`cooldown_at`、`motion`、`feel_ref`、`hit_policy: marker|continuous`、`hit_mode: auto|spatial|instant`、`sample_step_ms`、`rehit_interval_ms`、`target_assist`；`cast_time` 须等于三相之和），带 `SkillTimelineRule` 校验。时间线是第三种施法形态：相位按行动者动作时钟推进，速率重映射复用既有急速/SpellMod，取消窗口与连招经输入缓冲拉取（先验证后取消），`invuln_*`/`armor_*` 窗口，`release` 标记发射投射物。命中：目标选择链声明了 `shape` 即走空间命中（`marker` 按攻击方当时位姿解析一次，`continuous` 逐 tick 沿位姿插值采样，保证不跳过目标），攻击实例按 (目标, 段) 去重，无敌窗口前置回避，目标辅助（朝向修正不超过档案 `turn_assist_deg`、距离缩放）。新增事件 `action.started`/`action.phase_changed`/`action.marker`/`action.cancelled`/`action.finished`/`action.target_assisted`/`action.projectile_launched`/`action.projectile_ended` 与 `combat.hit_confirmed`（命中完整结论，含接触点/法线/冲击类别/两侧顿帧/反应与发起动作实例 id `castInstanceId`，时间线命中与 instant 命中统一发出）；`HitResult` 末尾追加 `Invulnerable`，`combat.attack_avoided.hitResult` 取值集合相应扩充。公开契约只加不改：`ITargetHost.TryGetChainShape`/`ResolveAtPose`、`IProjectileSpawner.Spawn(context, sink, IProjectileHitHook)`、`IUnitFacingWriter`、`ShapeGeometry.RebaseAt`/`ClosestPoint`、`ActionStartedEvent.IsAttack`（旧构造保留、缺省真）、`SkillCastInterruptedEvent.Reason`。未声明 `timeline` 的技能走原路径；离散步时间线折叠为瞬发；地面坐标施法不进入时间线。
- **运动档案与运动仲裁（ADR-0116，手感设计/02）**：新增运动档案字段（`accel_ms`/`decel_ms`、转向速率、反向策略 `instant|through_zero`、`wall_slide`、`apply_to_path_following`、`arrival_decel`、动作位移与击退参数、可选 `keep_momentum_on_motion_end`）与运动仲裁器：每 tick 恰一个位移来源，优先级 `dead > frozen > forced > staggered > rooted > action|root_motion > regular`；速度曲线积分、动作位移（窗口内逐 tick 位移之和恰为声明距离，窗口结束速度清零）、击退（`MovementHost.BeginKnockback(KnockbackRequest)`，`MovementOptions.KnockbackDurationSeconds`/`KnockbackStack`/`ResumePathAfterForced`）、沿墙滑动、步态输入导出。入口 `MovementHost.Motion`（`MotionServices`），不赋值即没有运动层，既有路径一字未动；缺省档案 `rpg_classic` 与旧移动逐位一致。`INavigation2D` 新增默认接口成员 `RaycastWithNormal`（带碰撞法线的射线查询，命中点与 `Raycast` 逐位相同；桩与 Unity 导航实现覆盖为精确法线，其余实现走轴向探测近似；一致性场景 `Navigation2DScenarios` 新增对应用例）。
- **局部顿帧与受击裁决（ADR-0117，手感设计/03）**：新增行动者动作时钟 `ActorActionClock`（`IActorActionClockQuery`/`IActorActionClockControl`：暂停窗口取大不相加、到期自动解除、场景卸载无条件释放）与受击裁决宿主 `HitFeelHost`（纯函数 `Evaluate` + 事件适配）：顿帧只冻结所列行动者的动作时钟（输入缓冲、时间线、运动层），冷却、光环、寻路不受影响；反应按 死亡 > 霸体 > 韧性 > 冲击等级映射 > `reaction_cap` 裁决，硬直在顿帧结束后起算，击退经 `IKnockbackSink` 提交，硬直打断时间线动作（`action.cancelled{stagger}`）；离散时间模型下不生效。新增事件 `feel.hitstop_started`/`feel.hitstop_ended`/`combat.reaction_applied`，`IActionStateQuery`（含无敌、霸体窗口）与 `IHitReactionQuery` 只读契约。
- **打击反馈、镜头与音效（ADR-0113/0117，手感设计/07）**：新增表 `feedback.impact_profile`（按 (冲击类别, 结局) 选变体：闪白、粒子、音效层、镜头冲击、拖影）与反馈动作 `play_impact`（`FeedbackActionKind.PlayImpact` 追加到枚举末尾），`ImpactPipeline`：同 tick 多目标命中先逐个解析、镜头冲击幅度取最大值并受镜头拥有者上限截断，音效按 `sfx_max_concurrent` 限数；`sfx.def` 新增可选 `feel_layer`/`feel_tier`/`feel_material`（`SfxLayerIndex` 缺材质先降材质后降档，不向高档回落）；挥空反馈（只对带攻击的动作开窗，投射物动作等到投射物结局再定）；顿帧表现经 `IFeedbackSink.FreezePresentation`/`ReleasePresentation` 通知。镜头：`ICameraImpulse` 可选能力（不支持时退化为 `ICamera.Shake`）与镜头手感档案（十四个 `camera_*` 字段：跟随滞后/前瞻/死区/阻尼/战斗缩放/冲击），`CameraHost` 新增 `Update(alpha, dt)`/`EnableFeel` 等；`IFeedbackSink` 四个新成员均为默认接口成员，`FeedbackBinder` 新增十三参构造重载。`PresentationAssemblyOptions.FeelResolver` 为 null（缺省）时全部手感呈现关闭，输出与改动前逐位一致；呈现型反馈与镜头档案不改判定型视图与逻辑指纹。
- **姿势维度与假人姿势集（ADR-0119，手感设计/04）**：姿势键语法 `<状态>[.<步态>][.<姿态>][.<武器族>][.<变体>]`（旧键 `combat_<状态>` 恒等于 `<状态>.combat`），回落链解析 `PoseResolver`（去变体 → 去武器族 → 去姿态 → 去步态 → 基础键，`sprint` 先按 `run` 重走），`display.anim_set` 新增可选字段 `extends`（沿链继承合并，成环抛异常）与 `pose_standard`（选入制：必备键缺失为错误、推荐键缺失为警告并指明回落到哪个键，`display.anim_set.std_` 前缀的框架级姿势集默认选入），标准姿势清单与 `import_assets.py check` 镜像；表现层 `GaitDeriver`（带滞回的 idle/walk/run/sprint 派生，只读呈现型视图）、`PoseSelector`、`IPoseContextSource`；Unity 适配层 `AnimClipResolver` 新增带姿势上下文来源的构造重载，`UnityViewFactory` 把维度键纳入登记并按 `extends` 合并，没有来源与维度键的外形回落链恰为旧的两级查表，逐位一致。新增框架级假人姿势集生成器 `toolchain/gen_std_dummy_poses.py`（程序绘制的几何人偶，34 个键、33 份资源、方向档逐向变体与武器层逐层剪辑，带只读自检）与数据行 `display.anim_set.std_dummy_biped`；第三方美术只需按规格逐键替换。
- **装备资产包与换装链路（ADR-0123，手感设计/08）**：`display.map` 新增可选 `preview_direction`；`toolchain/import_assets.py equip`（及显式 opt-in 的 `check --only equip`）校验装备资产包与界面皮肤包并输出装备完整性报告（错误级物品不得进入 validated 数据集，`EquipReport.is_validated`/`filter_validated`）；新增框架级占位装备集生成器 `toolchain/gen_std_equip_set.py`（单手剑/双手巨剑/匕首/弓/法杖/胸甲 + 占位皮肤包 `skin.default`，数据根 `data/_equip`）。换装链：`EquipmentFeelProvider`（主手/副手取武器槽）、`EquipmentFeelChain`（订阅 `item.equipped`/`item.unequipped`/`save.loaded`，武器引用或族变了才失效手感缓存并发新事件 `feel.weapon_changed`，读档冷路径与热路径同一出口）、`WeaponActionBinding`（武器的 `auto_attack_timeline_ref` 映射普攻）、表现层 `EquipmentPoseBridge`（把武器族设进 `PoseSelector`）；假人姿势集同步生成武器层逐层剪辑。
- **生产装配开关（手感机制接进生产装配）**：`CarriersAssembly` 与 `GameplayAssembly` 各新增末尾多一个 `CarriersFeelOptions? feelOptions` 参数的构造重载（既有最长重载纯转发并传 null，物理签名不变；可选参数重载的调用方要启用手感需改走最长重载），启用即装配全装配唯一的解析器与动作时钟、输入缓冲、局部顿帧与受击裁决、时间线协作者（命中解析接 `HitFeelHost`、目标辅助）、运动层、换装/光环失效订阅；启用而数据里没有 `feel.*` 行时抛 `InvalidOperationException`，不静默降级；`HeadlessWorldOptions` 新增 `FeelOptions` 与 `Navigation` 透传；`games/_template` 的 `GameOptions.FeelOptions`（属性，默认 null）经 `GameBootstrap` 转发，启用时数据根须含 `feel.*` 行（如 `data/_feel`）。启用后换图、读档、离开地图清空输入缓冲，`SkillOptions.ActionStepSeconds` 绑定装配步长（须与模拟固定步长一致）。
- **手感接入补缺（新游戏直接用，手感落地 M1 收口）**：`GameOptions` 新增 `ExtraFrameworkDatasetRoots` 与常量 `FeelDatasetRoot`，开启 `FeelOptions` 时模板自动把分发包 `data/_feel` 接进数据加载顺序（框架根 → 手感根 → 额外根 → 游戏根），热重载监视根与 `validate.ps1`（新增 `-FeelRoot`/`-NoFeel`，默认带上手感根）同步；生产装配的普攻绑定改为武器优先（`WeaponPreferredActionBinding`，选项 `CarriersFeelOptions.AutoAttackActions`），换装链 `EquipmentFeelChain` 在载体层手感装配里构造（`CarriersFeelSystem.WeaponChain`/`ActionBinding`），`PresentationAssembly` 装出 `PoseSelector`（`Pose`）与 `EquipmentPoseBridge`，新增可选接口 `IPoseContextReceiver`，`UnityViewFactory` 据此把姿势上下文交给 `AnimClipResolver`（只增不改，ABI 探针 `RESULT=OK`）；`architecture/13_新游戏接入指南.md` 新增第 9 节"手感接入"；`check.ps1` 的 `ban_arch_terms` 扩展到扫描 `architecture/手感设计/` 与 `architecture/数值设计/`。`toolchain/consumer_smoke.ps1` 同步内容数据集一步补同步 `data/_feel`（模板 PlayMode 新增的手感装配用例暴露：消费方工程缺手感根时开启手感装配会抛异常）。已知限制：生产装配不喂步态（移动恒为 `walk`，姿势集缺该键时回落 `move`）、手感数据热重载未接线。
- **手感实验室六格场景与不变量（ADR-0120/0122，手感设计/06）**：新增顶层模块 `lab/`（引擎无关内核 + 无头宿主）、命令行 `toolchain/feellab`（`suite`/`run`/`export-test`/`list`/`invariants`，不进分发包）与数据集 `data/_lab`、`data/_lab_action`（表 `lab.scenario`/`lab.arena`/`lab.dummy_set` 声明在 `core/sim/schema`）：六个格子（二维/二点五维/三维 × 目标选择式/动作式），30 个标准脚本（十个基础脚本：九个移动与攻击脚本加换装场景脚本；二十个手感场景脚本，覆盖近战、扑击、冲刺、投射物、三连击、闪避取消、缓冲边界、蓄力、精英霸体、打断、击杀、群体命中、巡逻靶、可破坏障碍与运动加减速/转向/撞墙），每个脚本每格一份指纹基线（共 180 条），度量组 4 个基础组 + 7 个手感条件组（输入缓冲、动作时间线、顿帧、受击反应、运动、空间命中、表现时间线）+ 换装组；跨格子不变量 270 条随 `Tests.Lab` 进门禁。`HeadlessWorldOptions` 的导航透传使无头世界可装配导航。

### 门禁

- **定向门禁：四级影响集（ADR-0126）**：新增 `toolchain/module_map.json`（子模块表：路径前缀、层、测试命名空间、引擎侧分类、责任人、交互例外；步骤触发规则）、`toolchain/gen_module_map.py`（生成与 `--check` 自检，新增门禁步骤"模块表覆盖所有子模块目录"）、`toolchain/change_impact.py`（改动 → T0～T3 影响集，输出 JSON）。`check.ps1` 新增 `-Changed <基线>`（默认 main）、`-Staged`、`-Modules a,b`、`-DryRun`：先打印"本次判定"块，只跑被触发的步骤，其余标 SKIP 与"T? 未触发"，全文日志落盘、控制台只留每步一行 + 汇总 + 首个失败的最后 30 行；不带这些参数时行为不变。T2 切片级只向下游多看一层，其余风险由里程碑全量兜底。
- **提交前钩子改走定向判级**：钩子对非发布提交调用 `check.ps1 -Staged -SkipUnity`，原 DocsOnly 档即 T0；T3 额外带 `-Quick`（与旧 Full 档等价）；暂存为空或判级失败退回旧 Full 档。
- **引擎侧 PlayMode 用例带模块分类**：`adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/` 下所有用例类加 `[Category("module:<模块名>")]`（归属不明的为 `module:shared`），移动阻挡用例另带交互例外分类；只改既有文件。已在引擎侧实测对账：`module:shared` 单独 46 例（11 个类）；`module:ui` 27 + `module:shared` 46 合跑 73（多分类分号串是并集）；`interaction:movement_stop_blocking` 恰好命中 `MovementStopAndBlockingPlayModeTests` 11 例，与 `module:unit` 合跑仍 11（并集去重，不是求和）。
- **定向模式的用例数下限**：子集运行不适用全量下限，改为失败为 0、通过至少 1、跳过数不超过上限。
- **数值仿真基线比对挪出 T1**：模块表步骤新增 `min_level`，仿真基线只在 T2 及以上触发；仿真自己的输入数据与基线文件（`core/sim/tests/data/**`、`core/sim/tests/baseline/**`）经 `always_triggers` 不受限制。
- **定向模式不再把「T? 未触发」误报为环境性 SKIP**：汇总段的环境性 SKIP 判定正则把定向模式的未触发原因归入开关类；主线新增的环境矩阵 6c/6d 与 IL2CPP 三步补步骤编号并登记模块表。
- **pytest 用例数下限上调**：`toolchain/gate_floors.json` 的 pytest `min_passed` 780 → 1000（先按合并前全量实测 934 取 840；并入版本标签、耗时记录与定向门禁覆盖率三个切片的新用例后，定向门禁 `-Changed main -SkipUnity` 内 toolchain_pytest 实测 1036，取 930；再并入待领耗时记录与 upm 证据/陈旧 pid 两个切片后实测 1082，按 90% 向下取整到十位取 970；再并入测试 git 环境隔离修复后收集 1115，取 1000）。
- **引擎步骤包管理器子进程消失的失败现场自动抓取**：门禁引擎步骤（`_gate_line_unity.ps1` 的编译检查/EditMode/PlayMode/独立版构建/IL2CPP 构建）与 `consumer_smoke.ps1` 的各 Editor 子步骤，引擎退出码非零且日志含 `IPC stream failed to read` 时，立即把 `upm.log`（`%LOCALAPPDATA%\Unity\Editor\upm.log`）、引擎日志相关片段、Unity 相关进程快照存进 `<ArtifactsPath>\upm_evidence\<时间>_<步骤>\`（`bin/` 下，不入库），并在该步骤 Detail 追加一行 `包管理器子进程退出码=…（-1/1 疑似外部结束，101 疑似自身崩溃）；现场已存 <路径>`；退出码取自引擎日志 `Server process stopped with exit code` 行（Unity 以无符号 32 位打印，折回有符号值）。判定与抓取在新增 `toolchain/_upm_evidence.ps1`（纯函数、可单测），抓取失败不改步骤结论；`AGENTS.md` §5 加一条判定规则。
- **消费方演练默认工作目录按检出隔离**：`consumer_smoke.ps1` 默认 WorkDir 由 `<TEMP>\gf_consumer_smoke`（所有检出共用、互相清空）改为 `<TEMP>\gf_consumer_smoke_<仓库根路径 SHA-256 前 8 位>`（`Get-ConsumerSmokeDefaultWorkDir`），显式 `-WorkDir` 仍优先；`check.ps1` Unity 线调用演练时一并传入自己的 `-ArtifactsPath`，演练失败时把各子步骤的包管理器现场摘要并进该步骤 Detail。
- **注册表测试不再误杀 PID 复用的无关进程、不再覆盖私服日志**：`test_registry_stop_pidfile_rewrite_timestamp.py` 的清理由"按裸 PID `Stop-Process -Force`"改为按进程身份（映像名 + 启动时间，`tests/_pid_identity.py`）核对后才结束，已退出或身份不符一律不动；`start_registry.ps1` 新增 `-LogDir`，测试把 verdaccio 日志写进临时目录，不再覆盖 `toolchain/registry/verdaccio.out.log`。
- **门禁耗时自动记录**：`check.ps1` 每次运行结束（通过或失败都写）把每个步骤追加到 `timing/<年月日>_<分支名去 feature/ bugfix/ 前缀>.jsonl`（main 上为 `<年月日>_main.jsonl`），字段与 `AGENTS.md` §1c 一致（task/branch/phase/step/start/end/seconds/result/note）；`phase` 定向模式记 `定向门禁`、否则 `全量门禁`，`step` 用步骤稳定 `-Id`（所有步骤调用点补齐 `-Id`，含被开关跳过的步骤），start/end 是每步真实起止时间（并行线在各自子进程里取、经结果 JSON 带回），另加一行 `_total` 记脚本总墙钟。新增 `-NoTiming`（预提交钩子与 `build.ps1 -Release` 调用时带，免得弄脏工作树）与 `-TimingTask "<一句话>"`（缺省 `check.ps1 <参数串>`）；写入失败不影响门禁结论，只在汇总末尾打一行警告。新增 `toolchain/timing_report.py`：读 `timing/*.jsonl` 按 phase、按 step 输出次数/合计/中位数/P90/最大值，支持 `--since`、`--branch`、`--phase`、`--json`，只写 stdout。
- **main 上的门禁耗时改写待领目录，不再弄脏主检出**：`check.ps1` 在 `main`（与 `version_label.py` 同一分支判定）或游离 HEAD 上运行时，耗时写到被 `.gitignore` 覆盖的 `timing/_pending/<年月日>_main_<时分秒>.jsonl`（一次运行一个文件，绝不追加到已跟踪文件），分支上运行与 `-NoTiming` 语义不变。此前当天的 `timing/<日期>_main.jsonl` 一入库，之后在 main 上跑门禁就会往已跟踪文件追加，主检出变脏，挡住 `build.ps1 -Release` 与 `git merge --ff-only`。新增 `toolchain/claim_pending_records.py`：在 feature/bugfix 分支的工作树里运行，把主检出 `timing/_pending/` 下全部文件按 `timing/<日期>_main.jsonl` 追加合并进当前工作树（同日按时间顺序拼接、去逐字节重复行）并删除主检出里被领走的待领文件，幂等；在 main 上运行拒绝；`--from` 缺省取 `git worktree list` 里检出 main 的工作树。`toolchain/timing_report.py` 默认也统计待领目录，`--no-pending` 排除。`AGENTS.md` §1b「合并后记录怎么入库」同步改写。
- **分支版本标签由工具自动推导（ADR-0127）**：落地 `AGENTS.md` §1b 的版本号格式。新增 `toolchain/version_label.py`：feature/bugfix 分支标签为 `<VERSION>_<名>`，main 为 `<VERSION>_release`，其它分支把 `/` 换成 `-`，游离 HEAD 为 `<VERSION>_detached-<短提交号>`；`--check-branch-name` 检查分支名规范。`check.ps1` 开头与汇总末尾打印"版本标签：…"，新增门禁步骤"分支名规范"（只判 feature/bugfix 分支，已登记模块表）。`Directory.Build.props` 把程序集信息版本设为标签（`check.ps1`/`build.ps1` 经环境变量传入，未传时回退 `VERSION` 内容；程序集版本与文件版本不动，ABI 不变，没有新增公开接口）。`build.ps1 -Release` 的发布说明首行写 `<新版本>_release`（`toolchain/_release_notes.ps1`）。`VERSION`、包版本、发布标签、发布包名、变更日志标题与 ABI 基线仍是纯语义化版本，版本校验逻辑不变。
- **定向门禁：层级范围与适配层判级，提高 T1/T2 命中率（ADR-0126 决定 9～12）**：层内共享面（`core/*/common`、`core/*/assembly`、`presentation/common|assembly`）与层根文件（`core/numbers/NumericGuard.cs`、各层 `LayerMarker.cs`）由 T3 改记 T2（层级范围：本层测试工程 + 下游一层 + ABI 探针 + 本层所有模块的引擎侧分类）；各层测试工程文件按所在层的层级范围记 T2（诊断转发测试工程文件只跑它自己）；`toolchain/tests/**` 与 `toolchain/gate_floors.json` 记 T1（只跑 `toolchain_pytest` 与秒级自检步骤）；生产工程文件/解决方案/全局构建配置/`data/**`/桩生产代码/`check.ps1`/`build.ps1`/`toolchain/` 下其它路径/`.gitattributes` 仍是 T3。`AGENTS.md` §4 全量时刻收敛为里程碑收口、升级框架/依赖、合并到 `main` 前后各一次，装配入口改动按层级范围 T2 判级。模块表新增 `tier_rules.path_rules` 登记适配层与其它路径（判级依据是 csproj 的编译包含/工程引用与 asmdef 引用）：`adapters/conformance/**` T2（`Tests.Foundation` + 分类 `module:engine_adapter` + 引擎编译/EditMode）；`adapters/stub/tests/**` T1（`Tests.Foundation`）；引擎侧 `Tests/Runtime/**` T1（只跑该文件自己标注的分类，无 dotnet 步骤、不同步 DLL；文件无分类标注则保守跑全部 PlayMode）、`Tests/Editor/**` T1（只跑 EditMode）；包内 `Runtime/**`、`Editor/**` T2（引擎编译 + 相关分类 + 消费方演练 + 同步 DLL，被 `Adapters.Unity.DiagnosticsForwarding` 按引用编译的三个源文件另跑其 dotnet 测试）；`adapters/unity/DiagnosticsForwarding/**` T1；`games/_template/**` T1（模板数据校验 + 模板冒烟）；`assets/**` T1（占位资产检查 + 样例导入幂等 + 导入检查）；`.github/**` T0；`.gitignore`、`.githooks/**` T1（门禁自检 + 新步骤 `hooks_pytest`）。模块表步骤新增字段 `needs`（基础步骤只在确有测试工程/确有层级公开面时才跑）、`needs_unity`、`dll_sync_gated`；判定输出新增 `rules`、`engine.dll_sync`、`engine.steps`。门禁步骤"模块表覆盖所有子模块目录"扩展：校验路径规则自身合法、每条规则至少匹配一个受版本管理的文件、覆盖根目录下无文件落到"未被任何规则覆盖"。新增不变量用例按 csproj 的真实引用关系逐文件核对"被判 T1/T2 的改动，所列测试工程包含把该文件编进自己的全部测试工程"，并据此补登 `Tests.Sim.csproj` 用 Link 直接编译的 `core/gameplay/tests/Perf/PerfMachineCalibration.cs`（该文件改动同时跑 `Tests.Gameplay` 与 `Tests.Sim`）。
- **测试不再把 git 命令打到真实仓库（2026-10-01 事故根治）**：预提交钩子里跑完整 `toolchain_pytest` 时，git 注入的 `GIT_DIR`/`GIT_INDEX_FILE`/`GIT_PREFIX`/`GIT_CONFIG_PARAMETERS` 被测试在临时目录里起的 `git init`/`config`/`add`/`commit`/`tag` 继承，命令实际作用到真实仓库（共享 `.git/config` 被写入 `core.bare=true`、`user.*`、`commit.gpgsign`，暂存区、分支、标签也被写）。三道防线：`toolchain/tests/conftest.py` 会话开始即移除 `os.environ` 里全部 `GIT_*`；新增 `toolchain/tests/_git_env.py`（`git_env`/`run_git`/`init_temp_repo`：干净环境 + `GIT_CONFIG_NOSYSTEM=1` + 空 `GIT_CONFIG_GLOBAL`，用户名/邮箱只写进临时仓库自己的配置，建完核对落点），`test_version_label`/`test_check_unity_meta`/`test_dist_immutability_guard`/`test_gate_step_runner` 的 git grep 用例/`test_change_impact` 两条用例/`test_gate_timing_log` 全部改走它，`_ps_subprocess_env.clean_powershell_env` 同样剥 `GIT_*`；conftest 会话级守卫记录真实仓库共享配置（`git rev-parse --git-common-dir` 下的 `config`）的 SHA-256，会话结束不一致即让整个会话失败并打印差异（只报告、不还原）。新增 `test_git_env_isolation.py`（模拟钩子环境的复现用例 + 不变量）；`AGENTS.md` §3 加一条硬约束。
- **`build.ps1` 发布不可变校验提前到一切写盘动作之前**：此前被拦截的 `-Dist <已发布版本>`（尤其带 `-SyncContent`）会先重写 `StreamingAssets/GameFoundation/{scene,nav_mesh}/*.json` 四个共享占位文件、先跑 `dotnet build`，再报"已发布版本不可覆盖"；并行门禁里这些文件被占用时 `Set-Content` 抛 `GetContentWriterIOError` 抢在校验之前、提示丢失。现校验紧跟版本号解析、先于任何写盘；新增静态顺序用例 `test_dist_guard_runs_before_any_shared_file_write`。
- **工具链：`dist/` 瘦身、ABI 基线回落、发版后自动瘦身**：`dist/`（`.gitignore`，本机构建缓存）此前只增不减，主检出累积到约 12.4 GB，并行会话还因工作树里找不到 ABI 基线而把整个 `dist` 复制进每个工作树（每份约 12.5 GB）。新增 `toolchain/prune_dist.ps1`（`-RepoRoot`、`-KeepVersions` 默认 2、`-Apply`；不传 `-Apply` 只列清单）：保留当前 `VERSION` 与紧邻其下的正式版本的 `<ver>/`、主 zip、samples zip，全部 `.lock` 与 `release-notes-*.txt`，ABI 基线版本（`abi_probe_baseline.txt`）的主 zip，高于当前版本的条目与认不出形态的条目；其余历史产物与全部 `-dryrun` 产物删除；`dist` 或待删条目含 junction/symlink 时拒绝；输出每类条目数与释放字节数。`toolchain/abi_probe.ps1` 默认基线解析在本工作树 `dist` 缺基线且本目录是链接工作树时回落到主工作树的 `dist`（新增 `toolchain/_abi_baseline_resolve.ps1`，输出标"来自主工作树"，显式 `-BaselineZip` 与缺基线的 SKIP/FAIL 行为不变），pytest 的真实 dist 产物用例（`_latest_dist.resolve_dist_dir`/`locate_dist_file`）同口径回落。`build.ps1 -Release`（非 `-DryRun`）打完标签后尽力而为地调用 `prune_dist.ps1 -Apply`，失败只警告、不改变发布结果与退出码。`AGENTS.md` §1 工作树改为 `D:\wt\<name>` 并禁止复制 `dist`，G3 基线口径同步。新增 `test_prune_dist.py`、`test_abi_baseline_fallback.py`。
- **手感相关门禁步骤**：新增 `validate_feel_data`（`data/_feel` 单独校验，须零错误零警告）、`validate_lab_data`、`validate_lab_action_data`、`validate_equip_data`（实验室与占位装备集数据根，叠加框架根与 `data/_feel`，须零警告）、`equip_pack_check`（`import_assets.py equip --strict-warnings`，占位装备集与皮肤包完整性）、`feel_lab_suite`（`toolchain/feellab suite` 全部脚本 × 六格与基线比对，`-Quick` 跳过）；`lab` 登记为模块表的独立层，跨格子不变量随 `Tests.Lab` 进门禁。

### 修复

- **持续的移动意图不再卡住受控位移**（ADR-0026 遗留缺陷，缺省路径，不依赖手感）：受控位移（`charge`/`leap`/`knockback`）进行中，若同一单位每个 tick 都提交 `move`/`move_to_unit` 意图（方向、目标点、追击），意图被位移拒绝后该单位被记入"本 tick 已处理"，位移续推被跳过，位移每次只走一步便停住。现在这种单位不再被记入已处理，位移逐 tick 走完；被控制（`IsLocked`）的单位仍按位移自己的控制规则处理。行为变化仅对"位移期间持续提交移动意图"的调用方可见（此前位移被卡住即缺陷）。复现用例 `C10a_ControlledDisplacementTests.MoveIntentEveryTick_DoesNotStallAControlledDisplacement`。

### 接入与迁移说明

- **不开手感：什么都不用做，行为与 1.92.0 逐位一致。** 不在数据里写任何手感新增字段（`found.input_action` 的 `class` 等、`skill.def.timeline`、`creature.template.feel_*`、`item.template.feel_weapon_ref`、`display.map.preview_direction` 等均为可选字段）、不给装配入口传 `CarriersFeelOptions`（`GameOptions.FeelOptions` 缺省 `null`），移动、结算、事件序列与表现输出都不变；各表 schema 版本不升，旧数据零改动合法。唯一对缺省路径可见的行为变化是上面"修复"小节的"持续的移动意图不再卡住受控位移"（此前位移被卡住即缺陷）。
- **公开签名只增不改，旧编译的消费方不必重编。** 新增的都是重载、默认接口成员（`INavigation2D.RaycastWithNormal`、`IFeedbackSink` 四个新成员、`IInputBufferQuery` 等）、可选字段与新类型；`CarriersAssembly`/`GameplayAssembly` 的新最长重载以外，既有构造签名物理不变。需要留意两处追加：`HitResult` 末尾追加枚举成员 `Invulnerable`（`combat.attack_avoided.hitResult` 取值集合相应扩充，对该枚举做穷举 `switch` 的代码补一个分支即可）；`FeedbackActionKind` 末尾追加 `PlayImpact`。
- **开启手感**：按 [`architecture/13_新游戏接入指南.md`](architecture/13_新游戏接入指南.md) 第 9 节"手感接入"的六步走——① 给 `GameOptions.FeelOptions` 赋值（模板装配根自动接上分发包的框架手感数据根 `data/_feel`）；② 写自己的 `feel.calibration` 标定行并经 `CalibrationId` 指定（游戏自带标定行后数据里有两行，必须显式填，否则装配报错而不是悄悄选一行）；③ 在 `found.input_action` 上声明 `class`/`buffer_ms`/`skill_slot`；④ 给技能写 `timeline` 块（命中形状写在技能的 `target_shape_ref` 指向的目标选择链上）；⑤ 补装备资产包并写 `feel.weapon` 行；⑥ 验收与调参。启用而数据里没有 `feel.*` 行会抛 `InvalidOperationException`，不静默降级；`validate.ps1` 默认带上手感数据根（`-NoFeel` 可关闭）。自己直接构造 `CarriersAssembly`/`GameplayAssembly` 的调用方需改走带 `feelOptions` 的最长重载（可选参数重载不会自动带上它）；`SkillOptions.ActionStepSeconds` 须与模拟固定步长一致。
- **已知限制（不影响缺省关闭的行为）**：生产装配不喂步态，移动恒为 `walk`（姿势集缺该键时回落到基础键 `move`）；手感数据热重载未接线（开发期改手感数据后需重启）；姿势选择器按单位记下的武器族在单位销毁时不清理（与换装链自身的单位状态口径一致）；手感实验室内核与实验室数据集（`lab/`、`data/_lab*`、`toolchain/feellab`）只在框架源码仓库提供、不随发布产物分发，只消费发布产物的游戏暂时无法自己跑第 6 步的实验室验收；`INavigation2D.RaycastWithNormal` 默认实现在桩与 Unity 导航实现之外的导航实现上用轴向探测近似法线。
- **框架侧工具链变化（消费方通常无需操作）**：`dist/` 发版后自动瘦身（`build.ps1 -Release` 成功打标签后调用 `toolchain/prune_dist.ps1 -Apply`，保留当前与上一版本、ABI 基线 zip、全部 lock/release-notes；历史版本按标签 `build.ps1 -Dist <ver>` 重建）；ABI 探针在链接工作树里缺基线时回落主检出的 `dist`，工作树里不得复制 `dist`；新增门禁步骤见"门禁"小节。
- **发布前全量凭据**：main 合并后 `check.ps1` 全量含 Unity 43 步全过，`dotnet test` 8804/8804、`pytest toolchain/tests` 1217/1217、EditMode 188/188、PlayMode 377/377、`feellab suite` 180/180、数值仿真三份基线零差异、ABI 探针 `RESULT=OK`、消费方演练 PASS（详见 `REGRESSION_LOG.md`）。

## [1.92.0] - 2026-10-01

本版主题是全仓库测试覆盖梳理（四批）与门禁加固：覆盖清单 99 条缺口全部关闭，约 60 处探针缺陷已修复，其中多处属于行为收紧（此前被静默接受的非法输入现在抛带定位的异常，详见下方"修复"小节中标注"行为收紧/行为变化"的条目），签名变化一律只增不改。门禁新增测试用例数下限、`games/_template` 数据根严格校验、环境矩阵两步，以及发布前回归记录守卫（`build.ps1 -Release` 的 `-ReleaseSkipUnity` 开关随之删除）。同批纳入 API 参考手册（ADR-0124，随 `dist/<version>/manual/` 分发）与手感设计文档集（ADR-0113～0120、0122、0123，设计层文档，无代码变化）。消费方升级时请重点核对"修复"小节的行为收紧条目与种子字段 `seed` 的数字/字符串双写法。

### 新增

- **手感设计文档集与 ADR-0113～0120**（设计层，文档类变更，无代码与签名变化）：新增 `architecture/手感设计/`（README + 00～07 共九份：手感总纲、输入与动作、移动与运动仲裁、攻击受击与命中、姿势与动画契约、手感档案与解析、手感实验室与验收、镜头与音画反馈）与八条 ADR——[ADR-0113](architecture/adr/0113-手感体系纳入框架判定型与呈现型两半拆分.md)（判定型/呈现型两半拆分）、[ADR-0114](architecture/adr/0114-技能结算新增时间线模式.md)（技能结算新增时间线模式，与目标选择式并存）、[ADR-0115](architecture/adr/0115-输入缓冲与动作时间线.md)（输入缓冲与动作时间线）、[ADR-0116](architecture/adr/0116-运动仲裁器与运动档案.md)（运动仲裁器与运动档案）、[ADR-0117](architecture/adr/0117-局部顿帧作为判定型手感.md)（局部顿帧）、[ADR-0118](architecture/adr/0118-手感档案分层解析与字段登记.md)（手感档案分层解析与字段登记）、[ADR-0119](architecture/adr/0119-姿势维度模型与标准姿势库.md)（姿势维度模型与标准姿势库）、[ADR-0120](architecture/adr/0120-手感实验室为框架交付物.md)（手感实验室为框架交付物）。受影响的 02/03/04/05/06/09/11/13/14 号文档的变更记录已加指针行，正文随落地切片同步。
- **手感设计补装备与 UI 资产契约及实验室场景矩阵**（设计层，文档类变更，无代码与签名变化）：新增 `architecture/手感设计/08_装备与UI资产契约.md`（换装链路、装备资产包、界面皮肤包、导入校验与完整性报告），`06_手感实验室与验收.md` 补场景矩阵（2D/2.5D/3D × 目标选择式/动作式）与换装场景，并新增两条 ADR——[ADR-0122](architecture/adr/0122-手感实验室场景矩阵与跨场景不变量.md)（手感实验室场景矩阵与跨场景不变量）、[ADR-0123](architecture/adr/0123-装备资产包与界面皮肤包契约.md)（装备资产包与界面皮肤包契约）。受影响的 04/09/13/14 号文档的变更记录已加指针行，正文随落地切片同步。
- **API 参考手册（ADR-0124）**：新增 `docs/manual/`（DocFX 站点，入口 `docs/manual/build.ps1`）——从 `Core.sln` 类库的 `///` 注释生成按命名空间 → 类型 → 成员逐条列出的 API 参考（67 个命名空间、约 1360 个页面），并把全部 114 份已入库 `README.md`（概念文档）与 `architecture/` 文档、ADR 并入同一站点；`build.ps1 -Dist`/`-Release`/`-Zip` 默认生成并放进 `dist/<version>/manual/`（`MANIFEST.txt` 新增 `manual:` 文件数行），手册生成失败即打包失败；新增 `-SkipManual` 开关（直接传 `-Dist X.Y.Z-dryrun` 时默认跳过）。新增 `.config/dotnet-tools.json`（固定 docfx 版本，`dotnet tool restore` 可复现）、`toolchain/gen_manual_toc.py`（生成概念文档导航目录）；`.github/workflows/release.yml` 打包前补 `dotnet tool restore` 一步。
- **已知缺口**：Unity 适配层 UPM 包由 Unity 编译、不在 `Core.sln`，未纳入 API 参考；README/架构文档里指向非 markdown 文件的链接在站点内是断的。

### 变更

- `Directory.Build.props`：`Core.*`、`Presentation.*`、`Adapters.*` 类库开启 `GenerateDocumentationFile`（测试/工具工程不开），并对这些工程关闭 CS1591/CS1573/CS1574/CS0419/CS1580/CS1734；`TreatWarningsAsErrors` 仍为 true。不改任何公开签名与运行时行为，DLL 内容不变。
- 文档注释勘误：修复 4 处注释 XML 格式错误（`IDataRegistry.TryGet` 缺 `</para>`、`StatHost`/`GobjSchemas`/`DiagnosticsHub` 各一处标签未闭合），此前这些注释会整段丢失；给 18 个没有任何 `<summary>` 的非测试 `.cs` 文件、以及 API 范围内另外 28 个无注释的公开类型补一句话用途说明。

### 测试

- **`CarriersAssembly` 真实组合根探针**（测试覆盖第二批，9 例，`core/carriers/assembly/tests/CarriersCompositionRootProbeTests.cs`）：经真实 `CarriersAssembly` + `Rules.Skill.CastSkill` 施放 `create_item`/`open_lock`/`summon` 三类效果并断言运行时结果（物品入包、锁解开并消耗钥匙、召唤物出现且归属施法者；无钥匙等失败分支），真实组合根下 `set_world_flag` 无扩展处理时断言 Warn 诊断且无副作用，`CompositeEffectExtension` 的先到先得/全不处理/空参数语义；`CarriersAssemblyTests` 空世界空跑用例补真实断言。无生产代码变化。
- **表现层生产装配接线探针**（测试覆盖第二批，`presentation/assembly/tests/`）：`PresentationAssemblyOptions` 每个可注入选项经真实 `PresentationAssembly` 注入后断言运行时结果（`PresentationAssemblyOptionsWiringTests`，30 例）；`PresentationAssembly.Dispose` 完整性——表现层全部总线订阅与场景钩子退订、`IDisposable` 公开子系统可观测、二次 `Dispose` 幂等（`PresentationAssemblyDisposeTests`，6 例）；`SchemaFieldRangeExport`/`SchemaFieldItemCountExport` 正常路径、路径记法、自引用/深度上限、空与非法输入（各 12 例）。探针暴露的真缺陷见下方"修复"。
- **`SimRunner`/`Validator` 命令行进程级契约与基线比对契约**（测试覆盖第二批 T-H7）：`toolchain/tests/test_simrunner_cli.py`（36 例：退出码 0/1/2/3、`--update-baseline`/`--runs`/`--progress`/`--json`、摘要行格式、基线文件损坏）、`toolchain/tests/test_validator_cli.py`（34 例：参数错误、`--strict`、`--list-tables`、`--display-map-sources`、`--enable-graph-isolation`、`--schema-audit` + `--allowlist`）、`toolchain/tests/test_gate_sim_added_guard.py`（11 例）、`core/sim/tests/BaselineContractTests.cs`（42 例：`Added`/`Removed` 判定、`ToText` 首行 `added=<n>`、`ToJson`/`SortedRows` 顺序确定性、`DatasetChanged`、`SimBaseline.Parse` 异常类型、种子往返、指纹与加载顺序无关）。
- **存档/时间模型/回放的零测试入口补测**（测试覆盖第三批 T-H1/T-H2/T-H4/D19，121 例，`core/foundation/sim_loop/tests/TimeModelValidationRuleTests.cs` 35 例、`core/foundation/save_system/tests/SaveSystemLoadCallbackTests.cs` 24 例、`SettingsStoreTests.cs` 45 例、`ReplayEndTurnTests.cs` 17 例）：`TimeModelValidationRule` 八条检查各一反例且只命中该条；读档回调（`BeforeLoad`/`OnSectionLoaded`）成功与回滚路径的次数与顺序、`deferLoadedNotification`；`SettingsStore` 迁移链多跳/断链/抛异常、文件损坏退化、非法登记、写失败；`ReplayRecorder.RecordEndTurn` 往返（[ADR-0125](architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) D19：保留并补测试，不标 Obsolete）。只加测试，不改产品代码。
- **事件总线重入钉住与非不变文化守护**（测试覆盖第三批 T-H3/T-H13）：`EventBusSuppressDispatchTests`（11 例）钉住 `SuppressDispatch` 嵌套/重复 Dispose/作用域内丢弃与 `DispatchPending` 订阅者内重入的插队顺序（现行为）；de-DE/tr-TR/sv-SE 三种文化下输出与不变文化逐字节相同的守护用例共约 48 例，覆盖 JSON 读写、表达式词法/解析/求值/`ToString`、`RngStreamState`、`GameObjectFactory` origin key、`ExprValueJson` 与 `WorldState` 存档段、`AuraHost` 诊断文本、`SimReport`/`SimBaseline`/`BaselineDiff` 的 `ToJson`/`ToText`（各测试工程各带一份 `CultureScope`，校验文化确实生效，防空测试）；表现层补 `Direction.ToString`（3 例，见修复）。
- **gameplay 错误路径与几何边界覆盖**（测试覆盖第三批 T-H10/T-H11/T-H12，224 例）：`EconomyHost` 买卖失败路径（13 例，货币/背包/商人库存逐项不变且无事件）；Quest/Achievement/DroppedLoot/PlayerVitals/GobjPendingLoot 五个存档段 `Load` 坏形状（88 例，含"合法条目在前、坏条目在后不得部分提交"的原子性）；`ShapeGeometry`/`AreaTriggerShapeGeometry`/`EncounterShapeMath`（经反射）/`StubSpatialQuery` 三份几何实现边界一致性（123 例，期望值由形状参数算出）。Achievement/PlayerVitals/GobjPendingLoot 的宽松读取现状以 Characterization 用例钉住（其中 GobjPendingLoot 已随后续修复收紧，见修复）。
- **rules/carriers 错误路径与已知限制钉住**（测试覆盖第三批 T-M16/T-M18 与 D11/D12/D14/D15）：`AiHost` 七个 throw 点各一例并断言抛出后宿主状态未变（`AiHostErrorPathTests`，18 例）；`AuraHandleLedger` 的 Release 未登记键/Forget/InstanceReplaced 迁移与合并分支（`AuraHandleLedgerTests`，10 例）；[ADR-0125](architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) 接受的四项设计限制各补用例钉住现行为（`RotationEvaluatorHotReloadTests`、`ADR0107_ThreatSourceRangeTests`、`SummonFollowNavigationTests`、`UnitChaseTests`）。
- **numbers 非法入参路径**（测试覆盖第三批 T-H5）：重复 `RegisterUnit`、`Advance`/`AdvanceAll` 负时长、`PowerHost` 构造期重复/空/null 资源类型、`AddXp` 负数、`Grant` 负结果钳 0、升满级残余经验诊断、long 上界、`ProgressionPersistable.Load` 失败后单位状态不变、`IStatHost.GetScope` 等；与 D17/D18 修复同提交。
- **表现层错误路径与空参守卫**（测试覆盖第三批 T-M14/T-M15/T-M31）：空参守卫反射批量覆盖公共构造器 + 手写覆盖方法/工厂/受保护构造器共 72 个类型（`NullGuardTests.cs` 反射 Theory 现 267 例、`NullGuardMethodTests.cs` 20 例）；camera 值对象与 `CameraProfile`、`ShellMenuDefinition`、`EquipVisualDef`、全部 feedback 动作/规则、vfx.def/sfx.def/weapon_style 的 `FromRecord` 错误路径；ui 的 `ShopViewModel` 真实 `EconomyHost` 直接用例（11 例）、11 个视图模型 `Dispose` 退订（13 例）、`UiLayoutSchema` 正反例（16 例）、`UiIntents` 失败路径（13 例）、`InMemoryUiDiagnostics`。
- **第三批探针缺陷的复现与回归用例**（与下列修复同提交，新增测试文件 9 个共 58 例，另有既有测试文件追加）：`ProgressionBatch3DefectTests`（14 例）、`CreatureFactorySpawnAtomicityTests`、`ADR0125_StubSpatialQueryLineBandTests`、`ADR0125_QuestPersistableStateEnumTests`、`ADR0125_DroppedLootPersistableAtomicLoadTests`、`ADR0125_DifficultyLoadScopeEnumTests`、`ADR0125_AppStateNameParsingTests`、`ADR0125_MovementIntentEnumArgsTests`、`SettingsStoreVersionOverflowReproTests`。
- **测试覆盖第四批：core/foundation、core/numbers、core/sim**（约 150 例，`core/foundation/sim_loop/tests/`、`localization`/`data_registry`/`app_lifecycle`/`event_bus`/`display_info`/`rng`/`scene_router` 各 tests、`core/numbers/*/tests`、`core/sim/tests/`）：sim_loop 的 `Intent`/`SimStep`/`SimClockHost`/`SimTimers`/`WorldSim` 边界与异常路径（T-M2/T-M3/T-M4）；faction schema 与重载、评级换算、archetype 契约、progression 选项与等级同步、power_set 内存诊断（T-M6/T-M9）；`core/sim` 四个 Run 入口守卫、`ScenarioCatalog`/`AnchorTable`、`HeadlessWorldBuilderOptions` 各字段、`FightOutcome` 全部取值、成长安全阀、基线/报告类型空参守卫（T-M5/T-M8/T-M15/T-L6）。
- **测试覆盖第四批：core/rules、core/carriers、core/gameplay**（约 300 例，含 T-M14 core 半 150 例）：`AutoAttackHost`/`Events`/`RulesAssembly` 延迟提供器/`TargetHost.ResolveAtPoint`/`ThreatTable`/`ProcHost`；投射物飞行边界、`WorldUnitAccess` 变更方法、`ItemBudgetCurve.SumConsumed`、`EquipmentHost.GetWeaponProfile`、`GameObjectFactory.Despawn`；`DialogHost` 错误路径、`QuestHost` NotReady、`LootHost.PurgeExpired`、`SpawnHost.NotifyDespawn`、`DeathPolicyHost.ClearPending`；gobj 模板/锁、遭遇、成就、经济、区域触发、目标链、难度档解析器的 `DataFieldException` 错误路径（T-M12/M13/M14/M17/M19/M21/M22/M23/M25、T-L9～L14）。期望值均由规则推出，无墙钟/区域性依赖。
- **测试覆盖第四批：presentation 表现侧**（约 300 例）：`VfxPool`（淘汰/并列/永久/到期/Untrack/容量）、`SfxPlayer` 变体选择走 `options.RngStream`、`VfxPlayer` Socket/Screen 冷加载与加载后跟随登记及排队期位置冻结、诊断记录器；`CompositeFeedbackSink` 挂接、`FeedbackBinder` 派发边缘、`FeedbackRuleValidator`、`FloatingTextMerger`、`PlaybackQueue`；`StrideEmitter`、`ViewBinder` 边缘与装备外观对账（ADR-0112 锚点按已显示方向）；render 的 `EquipmentVisualSource`/`SpriteViewDirectionCommit`/边界与默认值（T-H14 余项/T-M26/T-M27/T-M30/T-M33/T-M34/T-M35/T-M42/T-M45/T-M46/T-L16）。
- **测试覆盖第四批：presentation 外壳侧**（约 300 例）：`WorldSimSnapshot` 查询缺口与 `Direction` 边界；`CameraHost` 边缘（未注册档、缩放夹取、解析器返回空/抛出、释放后更新）；`UiIntents` 18 个带 `panelId` 重载与失败路径、11 个视图模型订阅键集合与逐键刷新、`UiPath` 解析边缘；`ShellHost` 新游戏/读档/返回主菜单/设置存取的失败与边界路径、`ShellViewModel` 逐键刷新；`PresentationAssembly` 真实宿主驱动的视图模型事件刷新、构造期必填参数为空与缺表退化（T-M29/T-M32/T-M37/T-M38/T-M39/T-M40/T-M41/T-M43/T-L19）。
- **测试覆盖第四批：adapters 与 Unity 适配层、模板**（约 270 例）：`ConformanceStubTests` 桩侧跳过集合与已知两项相等；`adapters/stub/tests` 七个桩与 `CollisionLayers`/`NavGridLayout` 的 xunit 用例（随 `Tests.Foundation` 编译）；`DiagnosticsHubComposition` 行为级 PlayMode 用例（24 来源普查、条数/前缀/异常文本、开关、反射遍历防漏登记）与 `UnityPresentationDiagnosticsConsoleSink` EditMode 用例；`games/_template` 热重载失败/新文件/去抖窗口/恢复与 `GameOptions` 默认口味；`FreezeFrameReceiver`/`ManagedPngDecoder`/`UiRoot`/`ShopPanel`/`ProjectSetup`/`EnsureAdditiveShaderAlwaysIncluded` 直接用例（T-M10/M11/M47/M48/L15）。
- **测试覆盖第四批：门禁与工具链脚本**（pytest 204 例）：发布前回归记录守卫（临时 git 仓库 + 伪造记录行）、Perf 诊断行缺失判定（伪造 trx 夹具）、Unity 结果 XML/冒烟日志/包清单判定、`Resolve-UnityExe`/`Invoke-NativeAndWait`/`Test-NoResidualUnityProcess`、`sync_package_content.ps1`（子进程 + 临时工程目录）、`unity_test_triage.py` 与 `format_data.py` 内部纯逻辑、两个 PowerShell 宿主重跑的 conftest 开关。
- **测试覆盖第四批收口：遗留修复的复现与不变量用例**（新增约 45 例、改写约 10 例，改写的是原先钉住"现状待确认"的特征化用例）：`VfxPlayerSocketColdLoadTests`、`PlaybackQueueEdgeTests`、`FeedbackBinderDispatchEdgeTests`、`FloatingTextMergerTests`、`ShellHostCoverageTests`、`SimLoopBoundaryTests`、`AppStateHostDiagnosticsAndReentryTests`、`RngReferenceVectorTests`、`ScenarioCatalogErrorPathTests`（放宽的测试 schema 触达 `DataFieldException` 分支）、`PresentationAssemblyTests`（T-M13 构造用例补副作用断言）。

### 门禁

- **模板数据根纳入严格校验**：`check.ps1` 新增 `games/_template/data/game` 的 `validate_data.py --strict` 步骤（秒级，`-Quick`/`-SkipUnity` 也跑），要求 warnings 为 0。
- **测试用例数下限**：新增 `toolchain/gate_floors.json` 与 `toolchain/_gate_test_floors.ps1`，`dotnet test`（trx）、`pytest`（junitxml）、Unity EditMode/PlayMode（NUnit XML）四个测试步骤解析结果文件的 passed/skipped/failed/inconclusive，`passed` 低于下限、`skipped + inconclusive` 超过上限、`failed > 0` 均 FAIL，四个计数恒写进步骤 Detail；防止"整批用例被静默排除/跳过仍全绿"。`dotnet test` 步骤名里的测试工程数改为从 `Core.sln` 数出（此前写死"六工程"，实际早已是八个）。
- **`consumer_smoke` 私服冷启动**：私服不在运行时改用 `Start-Process` 起 `start_registry.ps1 -Detach` 并带超时等待就绪，修复 `| Out-Null` 被后台 node 进程占住管道而卡死。
- **`added>0` 拦截判定抽函数**：门禁线的 `Added` 差异拦截从 `_gate_line_heavy.ps1` 内联正则抽成 `toolchain/_sim_added_guard.ps1` 的 `Get-SimRunnerAddedVerdict` 并单测；同时有意收紧一处——`simrunner` 退出码 0 却解析不出任何场景摘要行、或出现 `scenario=` 开头但格式不匹配的行时 FAIL（此前静默放行）。
- **全量门禁新增两步（复盘 I-5 缩减版）**：6c 不设 `PYTHONUTF8` 重跑 `toolchain` pytest；6d 脚本类 pytest 在 Windows PowerShell 5.1 与 PowerShell 7 各跑一遍（`toolchain/tests/conftest.py` 的 `WS_GAME_PS_HOST` 开关重定向宿主，宿主缺失/版本不符直接退出，不静默降级），skipped 必须为 0。`-Quick`/`-SkipUnity` 下这两步显示为 SKIP 行。
- **IL2CPP 保持显式开关（I-6，发布门禁不强制）**：IL2CPP 三步仍只由 `check.ps1 -Il2cpp` 开启，`build.ps1 -Release` 不强制传（构建机缺 Visual Studio C++ 工作负载与 Windows SDK，IL2CPP 构建必败；装好后再启用）；判断记录写入 `check.ps1` 头部。
- **发布前回归记录守卫，删除 `-ReleaseSkipUnity`（I-13）**：`build.ps1 -Release` 写回版本号前校验 `REGRESSION_LOG.md` 有对应 HEAD（或其祖先且其后只改 `docs/`、`architecture/`、`*.md`）的含 Unity 全量通过记录，否则拒绝并打印原因；`-DryRun` 只警告。逻辑抽成 `toolchain/_release_regression_guard.ps1`。**`build.ps1 -Release` 的 `-ReleaseSkipUnity` 开关删除**，发布时不能再整段跳过 Unity。
- **Perf 诊断行缺失判 FAIL（I-2）**：性能基线诊断行缺失此前只是黄色警告，现改为步骤 FAIL（`Get-PerfDiagnosticLines`，伪造 trx 夹具测试）。
- **门禁判定逻辑抽成函数并补测试（I-8/I-12）**：Unity 结果 XML 判定、冒烟日志判定、包清单必需文件与排除项抽成 `toolchain/_gate_unity_verdicts.ps1`，经 PowerShell 子进程测试；`Resolve-UnityExe`/`Invoke-NativeAndWait`/`Test-NoResidualUnityProcess` 同补用例。
- **环境性 SKIP 单独计数（I-8）**：汇总段新增"环境性 SKIP"单独计数并逐条打印（`Get-EnvironmentalSkipRows`），不再与"被参数跳过"混在一起。
- **`sync_package_content.ps1` 与两个生成器的测试（I-9/I-10）**：`sync_package_content.ps1` 首次有 pytest（清单制镜像/扁平化映射/TMP 与字体例外目标/幂等）；`unity_test_triage.py`、`format_data.py` 内部纯逻辑直接 import 的单元测试。
- **门禁汇总段修复**：`@($script:Results)` 传给 `[object[]]` 形参抛 "Argument types do not match"，改为直接传 `List` 并加静态断言。`check.ps1 -SkipUnity -Quick` 现为 32 步（原 30 步，新增 6c/6d 两个 -Quick SKIP 行）。

### 修复

- **区域触发内的实体被销毁时补发离开事件**（[ADR-0121](architecture/adr/0121-测试覆盖梳理第一批十条行为语义拍板.md) 第 9 条，覆盖梳理 D9）：`AreaTriggerHost` 订阅 `entity.destroyed`，区内实体被销毁（含 `IWorldSim.ClearAll`）时对每个所在触发体补发 `reason=despawned` 的 `area.trigger_left` 并清"已进入"记录，此前 `AreaTriggerLeaveReason.Despawned` 从未发出、记录残留，`GetActiveTriggerIds` 持续报告已销毁实体，同 Id 再生成后进入还可能被残留记录吞掉 `area.trigger_entered`。死亡但实体仍存在不算离开；与卸载补发（`unloaded`）共存不重复。无签名变化。
- **`ArchetypeRegistry.ApplyTo` 先全部校验再写**（[ADR-0121](architecture/adr/0121-测试覆盖梳理第一批十条行为语义拍板.md) 第 7 条，覆盖梳理 D7）：职业、种族、职业声明的天赋树全部解析成功后才调用任何 writer，未知职业/种族/天赋树在写入前抛 `ArgumentException`、writers 零调用（此前未知种族在职业基础属性与派生系数覆盖写出之后才抛，留下半应用状态）。同一单位重复 `ApplyTo` 在写入前抛 `InvalidOperationException`（仓库内唯一生产调用方 `RulesAssembly.RegisterUnit` 本就拒绝重复登记）。无签名变化。
- **`InputMapHost.ImportBindings` 全量解析后再落地**（ADR-0121 第 8 条，覆盖梳理 D8）：任一条非法（未知动作、值非数组、元素非字符串、绑定格式非法）整批拒绝，全部动作的绑定与脏标记保持导入前状态（此前非法项之前的合法项已经生效）。异常类型不变。无签名变化。
- **`VfxPlayer` 冷加载排队期间可 `Stop`**（[ADR-0121](architecture/adr/0121-测试覆盖梳理第一批十条行为语义拍板.md) 第 1 条，覆盖梳理 D1）：`Spawn` 在资源未就绪而排队时也返回可用句柄（占位句柄，与后端正值句柄不重叠），`Stop(占位句柄)` 取消排队使资源到达后不再补发、补发之后仍能停到那个粒子；加载失败/超时/已取消后再 `Stop` 安全忽略。`CompositeFeedbackSink` 因此冷热都登记句柄，`stop_vfx` 在加载完成前到达也生效（此前冷加载 `Spawn` 返回 null，停不掉）。冷加载 `Spawn` 由返回 null 改为返回占位句柄是行为变化；无签名变化。
- **`SfxPlayer` 首次加载超时改按 `Update(dt)` 累计判定**（[ADR-0121](architecture/adr/0121-测试覆盖梳理第一批十条行为语义拍板.md) 第 2 条，覆盖梳理 D2）：超时截止由墙钟时间改为表现时钟（只由 `Update(dt)` 推进，与 `VfxPlayer` 同口径），累计 dt 等于阈值即到期，`FirstLoadTimeoutSeconds=0` 语义不变；暂停/慢帧/重负载下不再因真实时间流逝误判超时。无签名变化。
- **`SfxPlayer` 层满时拒绝优先级更低的新音**（[ADR-0121](architecture/adr/0121-测试覆盖梳理第一批十条行为语义拍板.md) 第 3 条，覆盖梳理 D3）：新来者优先级严格低于该层全部在播实例时不淘汰任何实例——不播、返回空句柄、记一条诊断、计入丢弃计数；相等或更高仍淘汰"最低优先级中最老"；冷加载补播放走同一出口。此前低优先级新音会顶掉高优先级在播音。无签名变化。
- **震屏缺失 preset 不再抛，记诊断并跳过**（[ADR-0121](architecture/adr/0121-测试覆盖梳理第一批十条行为语义拍板.md) 第 4 条，覆盖梳理 D4）：`shake_camera` 的 `profile_id` 语义定为"当前相机档 `shake_presets` 里的 preset id"（写入 `feedback.binding` 字段登记表描述，字段名不改）；`PresentationAssembly` 在无生效档或当前档无该 preset（典型：相位切档后）时向 `FeedbackSinkDiagnostics` 记一条警告并跳过本次震屏，同事件其它动作与后续派发照常，不再抛 `ArgumentException` 被吞成一条不带上下文的诊断。补事件→规则→震屏→相机、事件→顿帧两条端到端用例。无签名变化（`CameraHost.Shake` 抛出契约不变）。
- **`ViewBinder` 逐视图异常隔离**（[ADR-0121](architecture/adr/0121-测试覆盖梳理第一批十条行为语义拍板.md) 第 5 条，覆盖梳理 D5）：`SyncAll`/`OnForwardableEvent`/`OnEntityDestroyed`/`OnSaveLoaded` 每个视图各自 `try/catch`，一个视图抛异常只记诊断、其余视图照常；同一实体连续逐帧失败只在每轮首帧记一条；`view.Destroy()` 失败仍从绑定表移除该实体并清位置缓存（此前残留）。无签名变化。
- **相机跟随目标丢失时保持最后位置**（[ADR-0121](architecture/adr/0121-测试覆盖梳理第一批十条行为语义拍板.md) 第 6 条，覆盖梳理 D6）：被跟随实体销毁后，`CameraHost.Update` 不再每帧抛异常——继续向最后一次有效位置跟随并停在那里，每次丢失只记一条诊断，目标重现后自动继续跟随。有签名变化（仅新增）：`ICameraFollowTarget` 新增默认接口成员 `TryGetPosition(entityId, alpha, out position)`（旧实现不改也能编译；内置 `SimSnapshotFollowTarget`/`DelegateFollowTarget` 覆写为不抛）；`CameraHost` 新增五参构造重载注入诊断出口，旧四参构造原样保留。
- **读档场景路由失败显式暴露**（[ADR-0121](architecture/adr/0121-测试覆盖梳理第一批十条行为语义拍板.md) 第 10 条，覆盖梳理 D10）：`ShellHost.LoadGame` 读档成功但场景路由被拒绝（`ArgumentException` 未知场景 / `InvalidOperationException` 状态不允许）时，读档仍算成功（`Status` 不变），但不再静默吞掉——`LoadResult` 新增只增字段 `SceneRouteFailed`/`SceneRouteError`（含目标地图 id 与异常消息）并向 `ShellHost.Diagnostics` 记一条警告。有签名变化（仅新增）：`LoadResult.SceneRouteFailed`/`SceneRouteError`/`WithSceneRouteFailure(string)`，`ShellHost` 新增带 `IPresentationDiagnostics` 的十一参构造重载，旧构造签名保留。
- **相机与外壳的诊断接入装配层共享诊断**（[ADR-0121](architecture/adr/0121-测试覆盖梳理第一批十条行为语义拍板.md) 第 6、10 条的接线收口）：`PresentationAssembly` 把同一份 `PresentationDiagnosticsRecorder` 传给 `CameraHost`（五参重载）、`CompositeFeedbackSink` 与 `ShellHost`（十一参重载），`Camera.Diagnostics`/`Shell.Diagnostics`/`FeedbackSinkDiagnostics` 是同一实例；`adapters/unity` 既有的 ADR-0042 轮询转发（`PresentationAssemblyDiagnosticsForwarder` 已轮询 `FeedbackSinkDiagnostics`）无需改动即可把"相机跟随目标丢失""读档场景路由失败"转发到引擎控制台。三处消息共处一个 `Warnings` 列表。无签名变化。
- **`PresentationAssemblyOptions.SettingsActionNames` 注入非空清单不再使构造期抛异常**（测试覆盖第二批缺陷 1，位置 `presentation/ui/core/ViewModels/SettingsViewModel.cs`、`presentation/assembly/PresentationAssembly.cs`、`core/foundation/input_map`）：此前注入任何非空清单，`PresentationAssembly` 构造期 `SettingsViewModel.Refresh()` 对未声明的动作盲调 `IInputMapHost.GetBindings`，抛 `InvalidOperationException`，选项事实上不可用。现在设置面板的按键绑定行只列**当前已声明**的动作（清单里未声明的跳过，之后声明并 `Refresh()` 即出现，行顺序按清单顺序）；`SettingsActionNames = null`（默认）改为"当前已声明的全部动作"（声明顺序），与字段注释统一（此前默认恒为空清单，即不设该选项时设置面板恒无按键绑定行，现在会列出所有已声明动作）。**签名只新增**：`IInputMapHost.GetDeclaredActionNames()` 默认接口成员（默认返回 `null` = 不支持枚举，`InputMapHost` 覆盖为声明顺序快照）、`SettingsViewModel` 不带动作清单的四参构造重载；既有五参构造不变。
- **`simrunner` 基线文件损坏不再崩溃**（测试覆盖第二批缺陷 2，位置 `toolchain/simrunner/Program.cs`）：基线文件是非法 JSON、缺必填字段或字段类型不符时，此前 `JsonParseException`/`KeyNotFoundException` 未捕获，进程以 Unhandled exception 崩溃；现按"数据装载阻断"返回退出码 2 并打印"基线文件损坏：<路径>：<原因>"。无公开签名变化。
- **`validator --schema-audit` 白名单文件不是合法 JSON 不再崩溃**（测试覆盖第二批缺陷 3，位置 `toolchain/validator/Program.cs` 的 `RunSchemaAudit`）：此前只捕获 `FormatException`，`JsonParseException` 未捕获致进程崩溃；现与结构非法同属"白名单文件格式非法"，退出码 2。无公开签名变化。
- **`SimBaseline`/`SimReport` 种子超过 2^53 不再丢精度**（测试覆盖第二批缺陷 4，位置 `core/sim/core/SimBaseline.cs`、`SimReport.cs`）：种子此前经 `JsonNumber`（double）往返，2^53+1 读回成 2^53、`ulong.MaxValue` 读回成 0。现 `ToJson` 把 `seed` 写成十进制字符串，`SimBaseline.Parse` 同时接受字符串与数字（旧基线写法）；仓库内已入库的基线不重写，`--update-baseline` 自然重写时才变成字符串。消费方若自行解析基线/报告 JSON 的 `seed` 字段，需同时接受数字与字符串。无公开签名变化。
- **`Direction.ToString` 固定不变文化**（测试覆盖第三批收口，位置 `presentation/common/contracts/Direction.cs`）：弧度插值此前随当前线程文化变（de-DE 输出 `raw=-1,5`，sv-SE 输出 U+2212 负号）；现用 `FormattableString.Invariant`。修前 `DirectionCultureTests` 三种文化 3/3 红，修后绿。无签名变化；行为收紧（非不变文化下输出变为与不变文化一致）。
- **表现层 8 个构造器形参补空参守卫**（同上，`ResourceReferenceTracker(loader)`、`InteractPathProvider(registry)`、`UiActionInvokedEvent(actionName)`、`UiLayoutDefinition(fields)`、`NumericValidationRuleDescriptor` 的 `ruleId`/`checkName`/`gradingItemName`/`group`）：传 null 现在在构造期抛 `ArgumentNullException`（此前静默接受，到使用处才抛 `NullReferenceException`）。无签名变化；行为收紧。`NullGuardTests` 的"已知无守卫"豁免清单已清空，这 8 项由反射 Theory 直接覆盖（修前 8 例红）。
- **`CreatureFactory.SpawnCore` 在注册实体之前校验 `PowerFloors`**（[ADR-0125](architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) D13，位置 `core/carriers/creature/core/CreatureFactory.cs`）：`template.PowerFloors` 的键不在 `ResolvePowerTypes` 实际注册集合内时，在 `AllocateEntityId`/`AddEntity` 之前抛 `InvalidOperationException`；此前冲突在 `RegisterUnit` 之后才由 `SetMinOverride` 抛出，世界实体与 Stats/Powers 登记已落地无人回滚（`EntityCount` 残留 1）。无签名变化；行为收紧（异常发生点提前，抛出后世界与登记零残留）。
- **数值写入口拒绝 NaN/Infinity**（[ADR-0125](architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) D17，位置 `core/numbers/NumericGuard.cs` 及 `StatHost`/`PowerHost`/`RegenModifier`/`ProgressionHost`）：`StatHost.SetBase`/`AddModifier`/`SetDerivationCoefficientOverrides`、`PowerHost.ModifyPower`/`Advance`/`AdvanceAll`/`SetMinOverride`、`RegenModifier` 构造、`ProgressionHost.GrantFromSource(multiplier)` 对 NaN/±Infinity 抛 `ArgumentOutOfRangeException`，在任何状态变更前拒绝、不发事件（此前 `SetBase(NaN)` 每次发 `stat.changed`，`ModifyPower`/`Advance(NaN)` 把当前值写成 NaN）。无签名变化；行为收紧。
- **`FactionMatrix.SetReaction(from==to)` 抛出**（[ADR-0125](architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) D18，位置 `core/numbers/faction/core/FactionMatrix.cs`）：自指写入抛 `ArgumentException`，不写入、不发事件（自身反应固定 Friendly）。无签名变化；行为收紧。
- **`ToString` 固定 `InvariantCulture`**（[ADR-0125](architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) D20，位置 `core/foundation/common/contracts/Vec2.cs`、`Rect.cs`，以及排查时发现的同类 `core/foundation/sim_loop/contracts/SimStep.cs`、`core/foundation/display_info/contracts/AnimSetDef.cs` 的 `AnimClipEventSpec`）：此前 de-DE 下 `Vec2`/`Rect` 输出 `(-1234,5, 0,25)`、`SimStep` 输出 `Continuous(dt=0,0166667)`。无签名变化；行为收紧（非不变文化下输出变为与不变文化一致）。
- **音量入口校验与持久化失败可见**（[ADR-0125](architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) D21，位置 `presentation/vfx_sfx/core/AudioLayerVolumeHost.cs`、`SfxPlayer.cs`）：`SetVolume`/`SetLayerVolume` 对 NaN/Infinity 抛 `ArgumentOutOfRangeException`，有限值裁剪到 [0,1] 且持久化裁剪后的值，未知层名抛 `ArgumentException` 且不写新键；`ISettingsStore.Save` 返回 false 时记一条诊断。有签名变化（仅新增）：`AudioLayerVolumeHost` 新增五参构造与 `Diagnostics` 属性，旧构造转调。行为收紧。
- **`StrideEmitter` 实体销毁时清状态**（[ADR-0125](architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) D23，位置 `presentation/view_binding/core/StrideEmitter.cs`）：订阅 `entity.destroyed`，销毁时清该实体的位置与累计记录；此前同 id 在远处重生会产生一次虚假位移。无签名变化；非收紧（修复虚假输出）。
- **`FeedbackRule.OptionalEnum`/`OptionalId` 非法字符串抛出**（[ADR-0125](architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) D24，位置 `presentation/feedback_binder/contracts/FeedbackRule.cs`）：非法枚举/标识字符串抛带字段名的 `DataFieldException`，不再静默变 null（与其余 `FromRecord` 一致）。无签名变化；行为收紧。
- **`SettingsStore.Load` 版本号超 int 范围按文件损坏处理**（测试覆盖第三批探针缺陷，位置 `core/foundation/save_system/core/SettingsStore.cs`）：`settings_version` 超 int 范围此前被 `(int)` 截断，可能误触发迁移；现按损坏退化为空设置。无签名变化；行为收紧。
- **`CreatureFactory.SpawnCore` 原子回滚**（同上，位置 `core/carriers/creature/core/CreatureFactory.cs`）：`AddEntity` 之后任一登记步骤抛出，回滚本次已完成的步骤（Powers/Progression/Stats/世界实体），异常原样上抛。有签名变化（仅新增）：`IWorldSim.RemoveEntityImmediately`、`IProgressionHost.UnregisterUnit`（均为默认接口成员，旧实现不改也能编译）。行为收紧（抛出后零残留）。
- **`ProgressionHost` 溢出/截断/xp 校验**（同上，位置 `core/numbers/progression/core/ProgressionHost.cs`、`ProgressionPersistable.cs`）：`AddXp` 溢出抛 `ArgumentOutOfRangeException`；`Load` 的 level 超 int 抛 `FormatException`；`RoundXp` 超 long 范围显式抛；`RestoreState`/`Load` 校验 xp（负数、达到本级升级门槛）抛 `FormatException`；负结果钳 0 的既有行为保留。无签名变化（`UnregisterUnit` 见上条）；行为收紧。
- **`PowerHost.Reload` 原子**（同上，位置 `core/numbers/power_set/core/PowerHost.cs`）：先解析进新表、全部成功后才替换，中途失败旧表不变。无签名变化；行为收紧。
- **枚举解析只认枚举名**（同上，位置 `core/gameplay/quest/core/QuestPersistable.cs`、`core/gameplay/difficulty/core/DifficultyHost.cs`、`core/carriers/unit/core/MovementTickHandler.cs`（mode/blocking）、`core/foundation/app_lifecycle/contracts/AppStateMachineConfig.cs`）：按名字精确匹配（`Enum.IsDefined`），拒绝数字串与组合串。无签名变化；行为收紧。
- **`DroppedLootPersistable.Load` 原子**（同上，位置 `core/gameplay/loot/core/DroppedLootPersistable.cs`）：快照 `entityId` 与世界里非掉落物实体冲突时，校验先于提交，既有掉落不被销毁。无签名变化；行为收紧。
- **`StubSpatialQuery.QueryShape(Line)` 改矩形带口径**（同上，位置 `adapters/stub/StubSpatialQuery.cs`）：复用 `ShapeGeometry.Contains` + 圆与带相交，端面外不命中，与其余几何实现一致。无签名变化；行为变化（端面外的实体此前可被线形命中）。
- **`GobjPendingLootPersistable.Load` 坏形状**（同上，位置 `core/carriers/gobj/core/GobjPendingLootPersistable.cs`）：段本身坏形状时保持既有台账不变并记 Warn；`count` 非法丢弃该条并记 Warn，不再抛。无签名变化；行为变化（由抛出改为记 Warn 丢弃）。
- **`WorldSim.Tick` 重入守卫**（测试覆盖第四批收口，位置 `core/foundation/sim_loop/core/WorldSim.cs`）：阶段处理器/事件订阅者里嵌套调用 `Tick` 现在抛 `InvalidOperationException`（此前内层整拍完整执行，两次 `sim.tick_started` 携带相同 `tickIndex`、意图队列被内层搬走）；外层 tick 被捕获后照常完整完成。无签名变化；行为收紧。
- **`AppStateHost` 回调异常隔离与重入迁移按发生顺序送达**（同上，位置 `core/foundation/app_lifecycle/core/AppStateHost.cs`）：`OnStateChanged`/`OnSubStateChanged` 回调抛异常与 `EventBus` 同口径——隔离、经 `IAppLifecycleDiagnostics.Error` 记一条、其余回调照常、不再穿透给 `RequestTransition`/`PushSubState`/`PopSubState` 调用方；回调内重入迁移时状态立即落定，通知排队按发生顺序送达（此前嵌套迁移的通知先于外层送达后续订阅者）。无签名变化；行为收紧。
- **`RngStreamState` 拒绝全零状态**（同上，位置 `core/foundation/rng/contracts/RngStreamState.cs`、`core/foundation/rng/core/RngHost.cs`、`SeedDerivation.cs`）：构造函数对四个字全零抛 `ArgumentException`，`TryParse`/`Parse` 把全零文本当非法格式（存档里全零的 `rng.stream_states` 条目现在整体读档失败而不是让该流静默恒产出 0），`RngHost.SetStreamState` 拦截 `default(RngStreamState)`；`SeedDerivation` 数学上不可达的全零回退分支删除并在注释里写明证明。无签名变化；行为收紧。
- **`VfxPlayer` 真 3D socket 挂接路径补冷加载排队**（同上，位置 `presentation/vfx_sfx/core/VfxPlayer.cs` 的 `TrySpawnAttachedToSocket`）：资源未就绪时与其它路径"冷 = 热"一致——返回占位句柄、`Stop` 可取消、加载完成后才创建子模型并挂到 socket（宿主按实体 id 重新解析，解析不到记诊断丢弃）；此前直接对尚未加载完成的资源 `CreateModelInstance`。无签名变化；行为收紧。
- **`PlaybackQueue` 步骤抛异常不再吞掉 `Finished`**（同上，位置 `presentation/feedback_binder/core/PlaybackQueue.cs`、`FeedbackBinder.cs`）：`Sequential` 下某步抛异常现记诊断后继续后续步骤、队列清空时照常触发 `Finished`（此前最后一步抛异常使 `PlaybackFinishedEvent` 永不发出、节奏门卡死；非最后一步抛异常也不再中断本次 `Update`/`Skip`）。有签名变化（仅新增）：构造重载 `PlaybackQueue(double, IPresentationDiagnostics?)` 与只读属性 `Diagnostics`，旧构造签名保留并转调。行为收紧。
- **`FeedbackBinder` 的 `flash` 缺 `targetId` 警告文本补事件键**（同上，位置 `presentation/feedback_binder/core/FeedbackBinder.cs`）：与其余四个动作一致。无签名变化。
- **`FloatingTextMerger.Update` 多个到期窗口按插入顺序派发**（同上，位置 `presentation/feedback_binder/core/FloatingTextMerger.cs`）：与 `FlushAll` 一致，此前同一次调用里倒序派发。无签名变化；行为收紧。
- **`ShellHost.NewGame` 存档写失败时回滚难度与 starter**（同上，位置 `presentation/shell/core/ShellHost.cs`）：返回 `false` 后难度宿主三项状态（经其 `IPersistable` 快照）恢复到调用前，并调用可选的 `NewGameRollback` 撤销 `NewGameStarter` 的效果（未注入则记诊断说明游戏层状态未回滚）；此前难度已切、起始状态已建却无存档也不进图。有签名变化（仅新增）：`NewGameRollback` 委托类型、`ShellHost` 十二参构造重载、`PresentationAssemblyOptions.NewGameRollback`。行为收紧。
- **`StubResourceLoader.DeferCallbacks` 注释订正**（同上，位置 `adapters/stub/StubResourceLoader.cs`）：注释原称延迟模式下仍由 `Register`/`Unregister` 决定成败，与实现不符（实际由 `CompletePending`/`FailPending` 显式裁决），已改成与排队语义一致。只改注释，无行为与签名变化。
- **sim_loop 四处缺陷**（测试覆盖第四批，位置 `core/foundation/sim_loop/`）：① `Intent.Kind` 校验正则 `$` 放过结尾换行（`"move\n"` 被当合法 Kind），改 `\z`（`contracts/Intent.cs`；无签名变化，行为收紧）；② `dt`/`scale`/`duration`/`factor` 的 `NaN` 绕过"不能为负"守卫，改 `!(x >= 0)` 习语（`contracts/SimStep.cs`、`core/SimClockHost.cs`、`core/SimTimers.cs`；无签名变化，行为收紧——此前 NaN 会污染时钟累积与计时器）；③ `SimClockHost` 构造函数未校验 `DefaultTimeScale`，与 `SetTimeScale` 共用校验（无签名变化，行为收紧）；④ `WorldSim.Tick` 阶段处理器抛异常后 `_isTicking` 卡死为 true，改 `try/finally` 复位（无签名变化，非收紧，异常仍原样传播）。
- **`RulesAssembly.ReloadArchetypeAndRace` 先解析再写**（测试覆盖第四批，位置 `core/rules/assembly/RulesAssembly.cs`）：校验失败时此前已改动属性（非原子），现先解析职业/种族再写入，失败时零写入。无签名变化；行为收紧。
- **`ProjectileHost.Spawn` 速度非正时告警并跳过**（同上，位置 `core/carriers/projectile/core/ProjectileHost.cs`）：此前速度 <= 0 生成永不过期的僵尸/反向投射物。无签名变化；行为收紧。
- **四处字段 int 回绕改抛 `DataFieldException`**（同上，位置 `AchievementCriterion.count`、`EconomyDataParser.stock_limit`、`DifficultyTierDefinition.item_level_offset`、`TargetChainDef.max_targets`）：超 32 位整数范围时 `(int)` 强转静默回绕（`max_targets` 是抛无定位的裸 `OverflowException`），现统一抛带字段路径的 `DataFieldException`。无签名变化；行为收紧。
- **`UiPathParser` 三处**（同上，位置 `presentation/ui/contracts/UiPath.cs`）：下标超 int 范围抛 `OverflowException`、非 ASCII 数字抛 `FormatException`、结尾 `$` 吞尾随换行，现均按"无法解析"返回 false。无签名变化；行为收紧。
- **`PresentationAssembly.Dispose` 逐子系统隔离**（同上，T-M41 拍板，位置 `presentation/assembly/PresentationAssembly.cs`）：单个子系统释放抛异常只记诊断、继续释放其余、最后重抛第一个异常（此前第一个抛出即中断，其余子系统泄漏）。无签名变化；行为收紧。
- **`DiagnosticsHub` 关闭开关时的伪重复行**（同上，位置 `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Diagnostics/DiagnosticsHub.cs`）：`Enabled=false` 时异常感知通道仍输出"累计出现 0 次"的伪重复行，现不输出。无签名变化。
- **`UiPanelHost` 面板 `Hide()` 从未真正隐藏**（同上，位置 `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Ui/UiPanelHost.cs`）：此前只停用空壳，背景/控件仍在画面上；面板物体改为拉伸节点并作为 `Construct` 的父节点，`UiSuiteTests` 三处硬编码路径同步加一层 `Dialog/`。无签名变化；行为变化（隐藏现在真正隐藏）。
- **删除 6 个 `Assert.True(true)` 占位测试**（T-M12，`PlaceholderTests.cs`，core 五层 + presentation）：它们不验证任何行为。
- **`FightRunnerTests` 墙钟阈值用例改 Perf 基线**（T-M8，`core/sim/tests`）：绝对 `< 100ms` 的墙钟断言在并行执行下误报，改为 `SimPerfBaselineTests.HeadlessBuild_MinTiming_WithinBaselineThreshold`（`[Trait("Category","Perf")]`，读 `perf_baseline.json`，阈值按本机参考负载校准），输出诊断行进门禁日志。

### 设计决定

- [ADR-0125](architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) 接受的设计决定（补用例钉住现行为，源码注释与模块 README 的"已知限制"改为"设计决定，见 ADR-0125"，无行为变化）：
  - D11 `RotationEvaluator` 热重载后不自动重编译，需重建实例。
  - D12 `ThreatSourceRange` 超范围不修剪仇恨表。
  - D14 召唤物跟随候选"可走"不等于"可达"（ADR-0103 已知负面）。
  - D15 追击单位自身站在不可走格不处理。
  - D16 `UnitPathProvider` 按字面 `power`/`stat` 段切分 unitId 的歧义，unitId 命名约束写入 `presentation/ui/README.md`。
  - D22 `ResourceReferenceTracker` 加载失败后同 id 不重试（重试属上层策略）、同步抛异常契约。

## [1.91.0] - 2026-09-30

### 修复

- **精灵单位冷转向时新旧方向混搭、闪占位图**（消费方反馈第六十三批 A，[ADR-0112](architecture/adr/0112-方向切换原子化与按实体预热全部方向.md)）：视图区分期望方向与已显示方向，新方向"当前显示所需"的资源（当前层集合的静态图 + 运动态 idle/move 与当前状态的剪辑）全部有结论（缺美术算结论，不无限等待）后才当帧整体提交，其余状态键提交后后台补加载，准备期间整个实体保持旧方向；整身兜底渲染器可见时内容方向必须等于已显示方向；仅镜像变化与热转向当帧提交。
  **行为变化**：`SpriteViewBase.OnDirectionSlotChanged` / `UnitySpriteView.DirectionSlotChanged` 改为**提交时**触发（此前是期望方向变化的当帧）；冷转向保持时间 = 上述所需资源加载完成的时间（真实外形实测 4 ms 预算 240～350 ms、8 ms 预算 155～240 ms；提交后首次进入尚未就绪的状态时该状态各层先显示新方向静态图，剪辑就绪后接上）；合成层身份改为 (层名, 装备资源集引用)，纯方向变化不再整体重探测。
  新增只读查询 `DesiredDirection`/`DisplayedDirection`/`HasPendingDirectionSwitch`（及 `HasDisplayedDirection`/`DisplayedFacing`）、纯计算 `ComposeLayersForDirection`、受保护虚方法 `PrepareDirection`/`OnDirectionCommitted`。默认开启，无开关。纯新增 ABI。

### 新增

- **按实体预热全部方向**（消费方反馈第六十三批 B，ADR-0112）：`UnityViewFactory.PrewarmDirections(entityId, onCompleted)`（镜像对去重后逐档位顺序执行，转向准备优先、档位之间让路，换装后补预热新增层，销毁取消并以 `false` 回调）与只读 `TryGetDirectionPrewarmProgress`；选项 `UnityViewFactory.DirectionPrewarm`（`DirectionPrewarmPolicy.None` 默认 / `OnAttach`）。
  预热后首次转向任一方向 0 帧；代价是该外形全部方向的贴图常驻内存（真实 16 方向外形实测约 1.4 GB，预热耗时 4 ms 预算约 3.6 s / 8 ms 预算约 2.3 s），默认不预热。走加载器既有分帧预算。纯新增 ABI。

## [1.90.1] - 2026-09-30

### 修复

- **复活时已脱战、或外形没有任何 `combat_*` 键的单位，复活后停在死亡剪辑末帧**（消费方反馈第六十二批，ADR-0111 相邻缺陷）：`UnityViewFactory.OnUnitRespawnedForAnim` 复活时调用 `AnimClipResolver.Refresh`，其"无记录时基线取普通键剪辑"假设视图静态显示的是普通待机，复活那一刻不成立，解析结果等于基线时什么都不播，直到下一次状态事件。复活改走内部的显示复位入口（按当前状态与姿态无条件播放一次运动态剪辑；变体没就绪先播普通待机，就绪后补切）；`Refresh` 语义不变。1.89.0 及更早同样受影响（那时复活只清记账不重播）；复活时在战且变体已就绪的单位本来就正确。

## [1.90.0] - 2026-09-30

### 新增

- **战斗姿态动画变体（战斗待机）**（消费方反馈第六十一批，[ADR-0111](architecture/adr/0111-战斗姿态动画变体.md)）：`display.anim_set.clips` 可声明 `combat_<状态键>`（七个状态键），单位在战斗中优先播它、没有就回落基础键（覆盖剪辑优先级更高）；`AnimStateMachine` 新增与 `AnimState` 正交的战斗姿态记账（`IsInCombatStance`/`CombatStanceChanged`/带初始姿态探针的构造重载），`AnimClipResolver` 新增 `Refresh`，运动态即时切换、瞬态不打断、冷加载补切、装备层经逐层剪辑跟随；没有变体键的外形行为不变。
  `UnityViewFactory.CombatProbe` 需接 `ICombatHost.IsInCombat`（三个框架装配入口已接，自写装配入口需照抄）。纯新增 ABI。

## [1.89.0] - 2026-09-29

### 新增

- **点目标不可走/不可达时吸附到最近的可达点**（消费方反馈第六十批，[ADR-0110](architecture/adr/0110-导航契约新增最近可走点.md)）：`INavigation2D` 新增默认接口成员 `TryFindNearestWalkable`/`FindNearestWalkableCandidates`（几何）与 `TryFindNearestReachable`（与单位连通、返回点必可 `FindPath`；桩与网格实现精确，网格按阻挡版本缓存连通标号），排序共用 `NearestWalkableSearch`/`NavGridLayout`。
  `MovementOptions` 新增 `UnwalkableTargetPolicy`（默认 `Reject`，行为不变；`SnapToNearestWalkable` 对全部点目标 `move` 意图生效，含召唤物跟随，首次建路与重规划都从原始点解析，点可走却不可达也吸附）、`UnwalkableTargetSnapRadius`（8.0）、`UnwalkableTargetCandidates`（8，仅第三方近似实现兜底）；
  `MovementHost.OnMoveTargetAdjusted` 通知目标被调整，失败通知目标仍为原始点；`MovementState.RequestedTarget` 记录原始目标。纯新增 ABI。

## [1.88.0] - 2026-09-29

### 变更

- **资源解码分帧与后台化**（消费方反馈第五十八批"首次换向长帧"，[ADR-0109](architecture/adr/0109-资源解码分帧与后台化.md)）：
  `UnityResourceLoader` 的 `Image`/`Effect` 位图解码与逐帧动画按帧切块移到后台线程（托管解码器不支持的变体整体回退主线程并记 Warn），
  主线程 `Tick` 按新增公开属性 `MainThreadBudgetMilliseconds`（默认 `4.0` ms，`<= 0` 不限）分帧，并新增只读诊断（`LastTickDecodeMilliseconds` 等，见 ADR）。
  运行期解码的精灵改用整矩形网格（`rect`/`pivot`/`bounds`/渲染像素实测不变，`textureRect` 与顶点数据变为整矩形）。
  **行为变更**：同一帧发起的多个冷加载不再保证在下一帧全部完成。

## [1.87.0] - 2026-09-28

### 新增

- **脱战判定补上交战范围**（消费方反馈第五十六批，[ADR-0107](architecture/adr/0107-脱战判定的交战范围.md)）：
  `CombatOptions` 新增 `ThreatSourceRange`（默认 30，`<= 0` 表示不限范围），`CombatHost` 脱战判定的
  两个方向（自己仇恨表里的记录、作为攻击来源挂在某个存活敌对单位仇恨表里）新增同一条距离过滤——
  超出该范围的存活敌对来源不再计入"仍在交战"，修复此前全图范围导致的"打伤一个敌人不杀、跑开任意
  距离也永远脱不了战"。仇恨表本身不因此修剪，AI 目标选择等其它用途不受影响。
- **单位级资源回复速率修饰器与周期形态通用资源恢复光环效果**（消费方反馈第五十七批"静息回复"，
  [ADR-0108](architecture/adr/0108-静息回复.md)）：`IPowerHost` 新增默认接口成员
  `AddRegenModifier(unitId, powerType, key, modifier)`/`RemoveRegenModifier(unitId, powerType,
  key)`，新增值类型 `RegenModifier`（生效范围 `脱战`/`战斗内`/`两者都`、乘算系数、加算增量，
  同一 `key` 重复登记为替换不叠加）；`PowerHost` 唯一回复速率读取点改为
  `(定义速率 + 全部匹配修饰器加算之和) × 全部匹配修饰器乘算之积`（结果为负夹到零）。光环效果
  新增 `mod_power_regen`（施加/刷新时登记、移除/到期时撤销）与周期形态资源恢复效果
  `periodic_energize`（每跳复用既有瞬时资源恢复原语的落地逻辑，不经战斗结算、不触发进战）。

### 变更

- **治疗结算不再无条件把双方标记为进入战斗**（消费方反馈第五十七批，
  [ADR-0108](architecture/adr/0108-静息回复.md)）：只有当这次治疗确实让至少一个存活敌对单位的
  仇恨表发生实际变化时，才把治疗来源一方标记为进入战斗；被治疗一方不因这次治疗单独改变进出战
  状态。伤害结算路径不变；治疗仇恨记账对象不变（仍记在被治疗者已有的仇恨条目上）。此前"两个不在
  战的单位之间互相治疗（含自我治疗、周期治疗光环）会把双方拉进战"判定为缺陷而非既有玩法语义。

## [1.86.0] - 2026-09-28

### 新增

- **单位级资源下限覆盖**（消费方反馈第五十五批"单一模板受伤但不死"，[ADR-0106](architecture/adr/0106-单位级资源下限覆盖.md)）：
  `IPowerHost` 新增默认接口成员 `SetMinOverride(unitId, powerType, min)`，`PowerHost` 全部夹取出口
  统一改读"覆盖 ?? 资源类型定义的 min"；`creature.template` 新增可选字段 `power_floors`
  （`Map<PowerTypeId, Number>`），`CreatureFactory` 在 `RegisterUnit` 之后逐条落地为覆盖，
  `CreatureContentValidationRule` 新增 `creature_power_floor_min` 检查项校验覆盖值不低于该资源
  类型的 `min`。战斗结算与伤害事件不改——落地数值仍是结算前的原始量，生命值被夹在覆盖下限之上，
  死亡判定自然不触发。

## [1.85.0] - 2026-09-27

### 修复

- **同槽位换装（同层名、不同 mesh_ref）未触发逐层剪辑重探测**（消费方反馈第五十三批）：
  `UnityViewFactory` 判断"合成层是否变化"的依据从只投影 `LayerName` 改为投影
  `(LayerName, ResourceId)` 二元组——同槽位换装（如 `mainhand` 层从装备 A 换成装备 B）前后
  层名集合不变但 `ResourceId` 随 `EquipMeshRef` 变化，此前被误判为"没有变化"而不触发重探测，
  该层会一直播放旧装备的帧集直至下一次方向切换才被纠正；现同层名换资源同样触发
  `ReprobeForCompositionChange`，冷加载路径同一出口，行为一致。
- **同层低优先级音效被紧随其后的高优先级音效当场停掉**（消费方反馈第五十四批，[ADR-0105](architecture/adr/0105-一次性音效播完即释放同层并发名额.md)）：
  `SfxPlayer` 层并发记账从不释放已自然播完的一次性音效，层累计播满上限后"永远满员"，挥击声被 2ms 后的命中声抢占停止。
  `IAudio` 新增默认接口成员 `bool? IsSfxPlaying(SfxHandle)`（默认 null），`UnityAudio`/`StubAudio` 已实现；`SfxPlayer` 按回报 false 即释放名额（只摘记账、不停止），
  回报 null 时才退回新增可选属性 `SfxOptions.OneShotLayerSlotHoldSeconds`（默认 2 秒，按 `Update(dt)` 累计）；循环音效、超限抢占、ADR-0083 计数语义不变。

## [1.84.0] - 2026-09-27

### 修复

- **召唤物跟随点不可走时采样候选点；移动系统"保留旧路径"策略下旧路径本 tick 真正续推**（消费方
  反馈第五十一批，阻塞，[ADR-0103](architecture/adr/0103-召唤物跟随点不可走时采样候选点与旧路径续推.md)）：
  `SummonTickHandler.TryFollow` 直接跟随点不可行走时改为在 owner 周围采样候选点（新增可选属性
  `SummonOptions.FollowCandidates`，默认 16，`≤1` 不采样），根治召唤物从阻挡一侧接近 owner 时永久
  冻结；`MovementTickHandler` 修正"寻路失败保留旧路径"策略下旧路径本 tick 不推进的缺陷。
- **切到无逐层剪辑的状态时写回该层静态层图**（消费方反馈第五十批，修订 ADR-0072 决策 2 一处已不
  再成立的判断记录表述）：`UnityViewFactory` 按实体跟踪"当前被逐层帧覆盖过的层名"集合，状态切换
  后集合中不再命中当前状态的层，经新增的 `UnityRenderer2D.RestoreLayerSprite`（复用 `SetLayers`
  同一条解析路径）写回该层最近一次合成的静态层图，不再停留在旧状态的最后一帧；`SetLayers` 触发的
  统一出口处清空该集合。
- **接入平局比较器，同 sortY 精灵绘制顺序确定且可读回**（消费方反馈第五十二批，
  [ADR-0104](architecture/adr/0104-渲染平局规则接入与绘制顺序可读回.md)）：`IRenderer2D` 新增
  `SetSortIdentity`/`CompareDrawOrder` 两个默认接口成员；`SpriteViewBase.Bind` 登记稳定 id；
  `UnityRenderer2D` 按 `(Layer, SortY)` 精确建组，组内按 `IRenderConventionHost.TieBreakComparer`
  全序给一个肉眼不可见的排序轴方向位置偏移，根治近战贴身、脚下 `sortY` 逐位相同时前后遮挡逐帧
  互换的画面闪烁；生产装配点接入比较器。

## [1.83.0] - 2026-09-27

### 修复

- **追击规划点不可达时在目标周围采样候选站位点**（消费方反馈第四十九批·反馈 2，阻塞，修订
  [ADR-0097](architecture/adr/0097-以单位为目标的追击移动请求.md) 决策 5，
  [ADR-0102](architecture/adr/0102-追击规划点不可达时采样候选站位点.md)）：`MovementTickHandler`
  重新规划追击路径时，直接回退点寻路失败不再直接判定为"到不了"，改为在目标周围按停止距离采样若干
  候选站位点，取第一个寻路成功的；新增可选属性 `MovementOptions.ChaseStandoffCandidates`（默认
  16，`≤1` 不采样）。
- **逐层剪辑按方向缓存命中时重新登记帧到播放器**（消费方反馈第四十九批·反馈 1）：
  `UnityViewFactory` 的逐层/覆盖剪辑逐层探测命中 (实体, 方向) 缓存时改为重新调用
  `RegisterClipFromEffect`，不再只换映射表不换播放器内容——根治逐层 clipId 跨方向复用同一 id
  导致换向两次后帧内容停在上一个方向的缺陷（含 attack 起手第一帧方向段）。

## [1.82.0] - 2026-09-27

### 修复

- **网格寻路结果做视线剪枝**（消费方反馈第四十六批，阻塞，
  [ADR-0101](architecture/adr/0101-寻路结果做视线剪枝.md)）：`UnityNavigation2D.FindPath` 返回前
  裁剪掉视线通畅的中间网格路点，根治开阔地起步回退、停步朝向错、任意角度目标折线来回切向三类
  失真；新增可选属性 `SmoothPaths`（默认开启）用于显式关闭。

## [1.81.0] - 2026-09-27

### 修复

- **逐层剪辑覆盖装备层与覆盖剪辑**（消费方反馈第四十五批，阻塞，
  [ADR-0100](architecture/adr/0100-逐层剪辑覆盖装备层与覆盖剪辑.md)）：装备新增/覆盖的纸娃娃层
  纳入逐层剪辑探测（按其 `mesh_ref` 取候选前缀），武器风格/技能覆盖剪辑改走与默认状态相同的
  逐层 + 方向探测，取代此前"整身唯一路径"。

## [1.80.0] - 2026-09-26

### 修复

- **纸娃娃层直线段移动途中静态图闪回**（消费方反馈第四十四批，阻塞，
  [ADR-0099](architecture/adr/0099-纸娃娃层只在方向槽位变化时重合成.md)）：移动结算模块按当前
  路点段固定两端点计算朝向，不再逐 tick 用漂移的插值位置现算；表现层重合成判据改按方向槽位而不是
  连续弧度比较，合法重合成后逐层动画当前帧立即回填，不必等下一次自然推进。

更早版本（0.1.0 至 1.79.0）的条目见 [docs/CHANGELOG-归档-0.1.0至1.79.x.md](docs/CHANGELOG-归档-0.1.0至1.79.x.md)。
