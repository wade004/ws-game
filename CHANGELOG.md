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

## [Unreleased]

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
- **发布门禁固定 IL2CPP（I-6）**：IL2CPP 三步只进 `build.ps1 -Release`（固定 `-Il2cpp`），默认 `check.ps1`/pre-commit/CI 不跑；判断记录写入 `check.ps1` 头部。
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
