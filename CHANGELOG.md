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
  [消费方反馈-2026-09-10-编辑器.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器.md)）**：
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
  [消费方反馈-2026-09-10-技能效果参数范围.md](architecture/落地计划/消费方反馈-2026-09-10-技能效果参数范围.md)）**：
  `FieldSchema` 新增可选 `Range` 字段（Number/Int 字段的取值范围登记），`toolchain/validator
  --list-tables --json` 每张表新增 `field_ranges` 导出，编辑器可据此在数值输入控件上就地校验，
  不必等到一次完整加载校验才发现越界。
- **ADR-0022（1.16.0，消费方反馈处理，
  [消费方反馈-2026-09-10-编辑器-第二批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第二批.md)）**：
  `TableSchema` 新增 `Layer`/`Module`/`Domain`/`TimeScope`，`FieldSchema` 新增 `Group`/`Unit`，
  `IdList` 字段种类补齐 `ReferenceTable`/`ReferenceDomain`/`WithFreeIds` 登记，`reference_integrity`
  扩展覆盖 `IdList` 元素；元数据门禁新增五项自洽检查；`toolchain/validator --list-tables --json`
  每张表新增 `layer`/`module`/`domain`/`time_scope`，每个字段新增 `field_meta`
  （`group`/`unit`/`reference_table`/`reference_domain`/`free_ids`）。
- **第 17 条修复（1.16.1，消费方反馈处理，
  [消费方反馈-2026-09-10-编辑器-第二批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第二批.md)
  第 17 条）**：`DataRegistry.RecordCount` 阻断态不再抛异常（直接读内部按表合并去重后的记录快照）；
  `IDataRegistryView` 新增 `bool TryGetRecordCount(out int count)`（带默认实现，阻断态返回
  `false` 而不抛异常）；`ContentValidationAssembly.Run` 统一改用 `registry.RecordCount`，不再另开
  事件订阅旁路。`IDataRegistryView.RecordCount` 默认实现本身保持不变（仍可能抛异常，供无具体
  `DataRegistry` 实现的第三方替身兜底），XML 注释已更新建议改用新成员。
- **消费方反馈第三批 18/19/20/22（1.17.0，消费方反馈处理，
  [消费方反馈-2026-09-10-编辑器-第三批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md)）**：
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
  [消费方反馈-2026-09-11-编辑器-第27条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第27条.md)。
- **消费方反馈第 28/29 条（1.20.0，消费方反馈处理，
  [消费方反馈-2026-09-11-编辑器-第28-29条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第28-29条.md)）**：
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
  [消费方反馈-2026-09-11-编辑器-第31条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第31条.md)。
- **消费方反馈第 32 条（1.23.0）**：新增
  `Core.Foundation.EngineAdapter.AssetRefConventions`，把此前分散在引擎适配层、框架表现层、
  `toolchain/asset_import` 三处的 `sprite_set_id`/`icon_id` → 资产相对路径解析规则收口为公开
  契约（`SpriteSetDirectory`/`IconFile`/`TryParseSpriteSetId`/`TryParseIconId`），规则本身不变。
  详见 [ADR-0025](architecture/adr/0025-资源引用标识到资产相对路径的约定纳入公开契约.md)、
  [消费方反馈-2026-09-11-编辑器-第30-32-33-34条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第30-32-33-34条.md)。
- **消费方反馈第 34 条（1.23.0）**：`PresentationSchemaCatalog` 新增公开
  `DefaultDisplayMapCoverageSources`（五张表），`ContentValidationOptions
  .DisplayMapCoverageSources` 未指定时默认使用该清单，`DisplayMapCoverageRule` 从此默认启用
  （此前默认禁用）；`toolchain/validator` 的 `--display-map-sources` 改为可选覆盖参数。详见
  [消费方反馈-2026-09-11-编辑器-第30-32-33-34条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第30-32-33-34条.md)。
- **消费方反馈第 35 条（1.24.0）**：新增公开
  `Core.Gameplay.Loot.LootTableAnalyzer.ExpectedProbabilities(LootTableDef,
  LootAnalysisContext)`（掉落表期望概率分析），供编辑器"掉落与爆率编辑器"分组树右侧概率/期望
  数量列直接调用而不必复刻语义或做蒙特卡洛逼近；与真实抽取（`LootHost.Roll`）共用同一份条件
  筛选/权重归一实现，不改变任何抽取行为。详见
  [消费方反馈-2026-09-11-编辑器-第35条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第35条.md)。
- **消费方反馈第 37 条（1.26.0）**：`IDataRegistryView` 新增
  `GetReferenceDeclarations(): IReadOnlyList<ReferenceDeclaration>`（只读回吐经
  `IDataRegistry.DeclareReference` 声明的全部引用关系：源表/源字段/目标表/是否可选/登记来源），
  `IDataRegistry.DeclareReference` 新增带来源标注的重载；`toolchain/validator --list-tables
  --json` 新增 `reference_declarations` 导出；`SchemaAudit` 新增元数据门禁检查
  `declared_reference_unregistered`（告警级）。详见
  [消费方反馈-2026-09-12-编辑器-第37条.md](architecture/落地计划/消费方反馈-2026-09-12-编辑器-第37条.md)。
- **消费方反馈第 38/39 条（1.26.1）**：`QuestContentValidationRule` 新增前置链循环检测
  `quest_prerequisite_cycle`/`quest_prerequisite_unknown`（阻断级，不新增公开 API，编辑器第 38
  条）；`dialog.story_tree.nodes[].performance_hook_ref`/`dialog.gossip_menu.options[].
  actions[]{kind=script}.ref`/`encounter.def.phases[].on_enter_hook`/`skill.def.
  effects[]{kind=script}.params.hook_id`/`area.trigger_def.params{trigger_type=script}.hook_id`
  五处字段补登 `SoftReferenceTable("found.hook")`（纯新增可选元数据，编辑器第 39 条）。详见
  [消费方反馈-2026-09-13-编辑器-第38-39条.md](architecture/落地计划/消费方反馈-2026-09-13-编辑器-第38-39条.md)。
- **ADR-0029（1.27.0）**：`Core.Foundation.DataRegistry.SchemaMigrator` 公开静态类
  （`BuildChain`/`MigrateRow`/`MigrateEnvelope`），把此前只服务于加载期内部的迁移链串接逻辑收口
  为公开、单一来源的静态入口，并新增"整表迁移"（信封级，供内容工具写回磁盘前调用）；加载器新增
  信封级可选键 `migrated_from` 的形状校验；`toolchain/validator --list-tables --json` 每张表新增
  `schema_version`/`migrations` 导出。详见
  [ADR-0029](architecture/adr/0029-迁移链串接与整表迁移纳入公开契约.md)、
  [消费方反馈-2026-09-13-编辑器-第40条.md](architecture/落地计划/消费方反馈-2026-09-13-编辑器-第40条.md)。
- **消费方反馈第 41/42 条（1.28.0）**：`DataRegistryOptions` 新增 `WarnOnMissingTranslation`
  （默认 `true`）——`l10n.locale` 已登记的非默认语言下 `TextKey` 字段缺翻译，新增 Warning 级
  `text_key_exists`（消息附带回退链落点），默认语言缺失仍是 Error；`toolchain/validator` 新增
  `--no-missing-translation-warning` 开关（`--json` 新增 `warn_on_missing_translation` 字段），
  `toolchain/validate_data.py` 透传同名参数；`l10n.locale.fallback` 字段改登记为
  `FieldKind.Reference`（`--list-tables --json` 该字段 `kind` 由 `Id` 变为 `Reference`），指向
  未登记语言的坏数据从此在加载期即报 `reference_integrity` 错误。详见
  [消费方反馈-2026-09-14-编辑器-第41-42条.md](architecture/落地计划/消费方反馈-2026-09-14-编辑器-第41-42条.md)。
- **消费方反馈第 43/44 条（1.29.0）**：`Presentation.Assembly.ContentValidationAssembly` 新增
  `OptionalRules`/`OptionalRuleDescriptor`/`TryGetOptionalRuleByCheck`（可选规则"规则名 ↔ 检查名"
  关联单一来源），`SpawnSummonOnlyCreatureRule`/`DisplayMapCoverageRule` 的 `CheckName` 常量随之
  改为公开，`toolchain/validator --json` 新增 `optional_rules: [{rule, check, enabled}]`（第 43
  条）；新增 `Core.Carriers.Creature.RegistryCreatureTemplateQuery`，`ContentValidationAssembly`
  未提供 `CreatureTemplateQuery` 时默认改用它，`SpawnSummonOnlyCreatureRule` 从此默认启用（此前
  默认禁用，比照 1.23.0 `DisplayMapCoverageRule` 先例），`DisabledOptionalRules`/
  `disabled_optional_rules` 默认恒为空（第 44 条根治）；示例数据集新增 `summon_only` 生物模板
  `creature.sample_summon_totem`。详见
  [消费方反馈-2026-09-14-编辑器-第43-44条.md](architecture/落地计划/消费方反馈-2026-09-14-编辑器-第43-44条.md)。
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
  （`editor/docs/编辑器产品文档.md`/`.html`）第 4.1 节补齐 T-N3-9/T-N2-4 落地的两个 Analyzer
  契约面（`SkillBudgetAnalyzer`/`EquipmentScoreAnalyzer`）与 T-N5-2/T-N5-3 落地的数值规则集中
  登记清单/`ExprReferenceCollector`；第 5.8 节新增"内容覆盖仿真离群值列表"行，标注依赖阶段 N6
  （`core/sim`）产出，当前仅为契约意向。纯文档变更，不涉及代码。HTML 版同步新增内容；HTML 自
  v2.6 起累积的历史缺口（v2.7～v2.15 期间第 4.1 节新增的其余契约面尚未回填 HTML）不在本条改动
  范围，已在 HTML 头部说明如实标注；该缺口已由 T-N5-5（同版本）回填，详见下方正文"文档"小节。
- **消费方反馈第 45/46/47 条（1.38.0，[回复文档](architecture/落地计划/消费方反馈-2026-09-17-编辑器-第45-47条.md)）**：
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

## [1.62.0] - 2026-09-22

### 修复

- **对白面板复用选项按钮时未重绑点击回调，会话由闲聊切到剧情后点击卡死**（消费方反馈——游戏
  接入方第十六批，阻塞，框架缺陷）：`Adapter.Unity.Ui.Panels.DialogPanel.RebuildOptions` 此前
  只在"新建"按钮时给 `UiWidgets.CreateButton` 绑定 `onClick`（闭包捕获当次传入的点击委托），
  复用已有按钮（选项数量不变或减少）时不会重新绑定。闲聊选项动作含 `start_story`
  （如 `[quest_turn_in, start_story]`）、一次点击把会话从"1 个选项的闲聊菜单"切到"1 条分支的
  剧情节点"时，按钮被复用而不是重建，仍挂着上一次 Gossip 态 `RefreshUi` 绑定的旧闭包，其捕获的
  `DialogViewModel.Gossip` 此时已经为 `null`，点击直接 `NullReferenceException`，对话卡在第一
  节点。改为：`button.onClick` 只在按钮创建时永久绑定一层间接层（读取一个按位置索引的
  `Action?` 数组并调用），真正的点击语义在每次 `RebuildOptions` 的 `for` 循环里对全部按钮
  （新建的和复用的）无条件重新赋值，不存在"只在新建分支才重绑"的代码路径。判断记录见
  `adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md`
  "UI 套件（`Runtime/Ui/`）"一节。

## [1.61.0] - 2026-09-22

### 修复

- **原生对白面板无法推进剧情对话**（消费方反馈——游戏接入方第十五批，阻塞，框架缺陷）：
  `Presentation.Ui.UiIntents.ChooseDialogOption` 此前恒转发 `IDialogHost.ChooseOption`，而
  `IDialogHost.StartStory` 进入剧情会话时会清空会话的 `GossipMenuId`、`ChooseOption` 要求该字段
  非空——剧情会话里经框架自带对白面板（`DialogPanel.RefreshUi`）点任何剧情分支都会被拒绝，节点
  恒不推进，玩家用原生对白面板永远走不完剧情对话，只有绕开 UI 直调 `IDialogHost.AdvanceStory`
  才能推进。改为按当前会话类型分派：剧情会话（`IDialogHost.GetStoryView` 非空）转发
  `AdvanceStory`，闲聊会话（`IDialogHost.GetGossipView` 非空）转发 `ChooseOption`，判断记录见
  `presentation/ui/README.md`。`UiIntents.ChooseDialogOption(int)` 签名不变，不影响既有调用方。

## [1.60.0] - 2026-09-22

### 行为变更

- **"最近可交互目标"候选生物需有可交互内容**（消费方反馈——游戏接入方第十四批，
  [ADR-0069](architecture/adr/0069-最近可交互目标候选生物需有可交互内容.md)）：
  `Core.Carriers.Assembly.InteractionTargetRegistry.IsInteractionCandidate` 此前对生物候选只核对
  "存在且存活"（ADR-0065），不看它有没有任何可交互内容——护送/跟随/闲逛一类没有配置原生对话内容
  的生物离玩家最近时会被误选中，玩家按交互键什么也不会发生，旁边真正想交互的对象反而够不到。
  `Core.Carriers.Common.ICreatureInteractionHost` 新增默认接口成员 `HasInteractableContent
  (creatureInstanceId)`（只读、无副作用，不判距离/发起者/存活），与既有 `Interact` 共用同一份
  内容判定逻辑（生产实现 `Core.Carriers.Creature.CreatureInteractionHost` 抽出的私有方法），默认
  实现恒返回 `true`（未升级的既有实现方行为不变）；`InteractionTargetRegistry` 新增构造重载接受
  该接口（旧的两参构造函数保持不变、按"无法判定按有内容处理"降级，不参与过滤），生产装配
  `CarriersAssembly` 显式接上。生物候选判定收窄为"存在 且 存活 且 有可交互内容"三者合取。纯
  加法：两处签名都是新增（默认接口成员 + 构造函数重载），未跟改的既有调用方行为逐位不变。

## [1.59.0] - 2026-09-22

### 行为变更

- **遭遇开始幂等化：同一地图同一遭遇已有进行中实例时 `Start` 不再新建**（消费方反馈——游戏接入方
  第十一批，[ADR-0068](architecture/adr/0068-遭遇开始幂等化.md)）：
  `Core.Gameplay.Encounter.EncounterHost.Start(encounterId, mapId, playerUnitId)` 此前每次调用都
  新建一个实例、重新生成参战单位、重新发布 `EncounterStartedEvent`——`encounter_start` 类型区域
  触发设为 `one_shot: false`（为了死亡后可重打）时，玩家在战斗中来回穿越触发圈会反复触发 `Start`，
  同一遭遇出现多个并存的进行中实例。现在 `Start` 内部先查同一地图上该遭遇是否已有进行中
  （`IsActive`）实例，命中则直接返回该实例 id，不做任何其它副作用（幂等）；已结束（胜利/失败/
  `Abort`）的实例不算，不影响重新开始；不同地图上的同一遭遇定义互不影响。`Core.Gameplay.Encounter.
  IEncounterHost` 新增默认接口成员 `TryStart(encounterId, mapId, playerUnitId, out instanceId)`，
  返回新增枚举 `EncounterStartResult`（`Started`/`AlreadyActive`）显式区分本次调用是否真的新建了
  实例；默认实现委托给既有 `Start` 并恒返回 `Started`（老实现方行为不变），生产实现 `EncounterHost`
  用显式接口实现覆盖。区域触发默认的 `EncounterStartRequested` 转发逻辑本就是直接转调 `Start`，
  自动受益，无需改动——`encounter_start` + `one_shot: false` 现在是安全的组合。全仓排查未发现依赖
  "同一遭遇可并存多个进行中实例"的既有测试或流程。**这是一次行为变更**：本版本起同一地图同一遭遇
  任意时刻至多一个进行中实例。ABI 纯加法（新增一个默认接口成员 + 一个枚举类型），`breaks=0`。

### 修复

- **消费方反馈（游戏接入方第十二批）**：`CreatureDeathLootListener.OnUnitDied` 此前对任意死亡
  单位都去 `creature.template` 表查模板；玩家单位的模板 id 是职业原型（不在该表里），导致每次
  玩家死亡都写一条和"内容作者漏配生物模板"完全相同的 Loot 诊断告警——玩家死亡是每局必然发生的
  正常事件，不是内容缺口。现改用 `IUnitAccess.GetSourceKind` 先判别死亡单位是不是生物域实体，
  不是（含玩家单位、来源未知）就直接跳过、不写诊断；只有确实是生物、但查不到模板/掉落表这一
  真正的内容缺口才保留告警，文案不变。不新增构造重载（已有依赖足够判别）。详见
  `core/gameplay/loot/README.md` 判断记录 21。

- 消费方反馈第十三批（游戏接入方）：`GameplayAssembly` 第 11 步给 `SpawnOptions.GobjSpawner` 装的
  默认委托、示例 `adapters/unity/.../Bootstrap/GameFoundationBootstrap.cs`（样例箱子）此前只转发
  `GameObjectFactory.Spawn` 的 4 个位置参数，`lockId` 恒为 `null`——经 `spawn.table` 刷出来的
  `gobj` 无论 `gobj.template.lock_id` 登记了什么锁都不上锁。新增 `GameObjectFactory
  .SpawnFromTemplate`（ABI 纯新增，按 `templateId` 读 `gobj.template` 取 `LockId` 一并传给
  `Spawn`），框架默认生成路径与示例改调它；`GameObjectFactory.Spawn` 本身"调用方决定 `lockId`"的
  既有语义不变。详见 `core/carriers/gobj/README.md` 判断记录 13、`core/gameplay/assembly/README.md`
  判断记录 16。

## [1.58.0] - 2026-09-22

### 新增

- **区域触发宿主契约纳入当前所在区域查询**（消费方反馈——游戏接入方第九批，阻塞，
  [ADR-0066](architecture/adr/0066-区域触发宿主契约纳入当前所在区域查询.md)）：
  `Core.Gameplay.AreaTrigger.IAreaTriggerHost` 新增只读默认接口成员
  `GetActiveTriggerIds(unitId)`（某单位当前所在的全部触发区域 id，按进入先后排序，含
  `RegisterTrap` 登记的陷阱，未装配时降级为空集合），生产实现 `AreaTriggerHost` 用显式接口
  实现转发到内部账本（进入序号改用确定性单调计数，不用墙钟时间）。`Presentation.Ui.
  PlayerPathProvider` 新增携带 `IAreaTriggerHost`/`IDataRegistryView` 的十四参数构造重载，
  新增路径 `player.area.id`/`player.area.name_key`——"当前区域"取该单位所在区域中最近进入、
  尚未离开、且 `AreaTriggerDef.NameKey` 非空的那一个（没有显示名的触发不参与，重叠区域离开
  内层后回落到仍在其中的外层有名区域）。`Presentation.Ui.HudViewModel` 新增只读属性
  `CurrentAreaId`/`CurrentAreaNameKey`，经上述路径转发。`Presentation.Assembly.
  PresentationAssembly` 改用新构造重载装配（`gameplay.AreaTrigger` + 既有 `registry` 构造
  参数，不新增依赖边界）。纯加法，ABI `breaks=0`（新增 5 行：接口成员、`AreaTriggerHost` 公开
  方法、视图模型两个属性、`PlayerPathProvider` 新构造重载）。

### 行为变更

- **生物原生交互分流拒绝死亡目标与死亡发起者**（消费方反馈——游戏接入方第十批第 1 条，
  [ADR-0067](architecture/adr/0067-生物原生交互分流拒绝死亡目标与死亡发起者.md)）：
  `Core.Carriers.Creature.CreatureInteractionHost.Interact` 新增两条存活核对（复用构造期已持有
  的 `IUnitAccess`，不新增构造参数）——目标生物或交互发起者未存在且存活时拒绝交互，分别返回
  `Core.Carriers.Common.InteractOutcome` 新增的 `TargetDead`/`ActorDead`（枚举只追加，`breaks=0`）；
  两种情形均不记诊断（与既有"交互距离不足不记诊断"同一口径）。`"interact"` 意图分流
  （`CreatureInteractIntentTickHandler`）转调同一 `Interact`，同步生效。**这是一次行为变更**：
  此前对一个已知的死亡生物 id 直接发起交互会被受理（无对话配置判定为"无可交互内容"、有配置则
  打开对话），本版本起统一拒绝。

### 变更

- **`check.ps1` 门禁提速重排 + 分线并行**（工程收尾 gate-speed 任务，2026-09-22；判断记录见
  `check.ps1` 头部注释，唯一出处）：全量门禁此前 31 步严格串行、约 486 秒，且任一步失败仍会白跑
  完剩余全部步骤才报错。本次三件事：① 新增 `-FailFast` 开关——打开后任一步 FAIL 会让所在执行
  序列（前置快速检查阶段，或下面的两条并行线之一）后续步骤立即改判可见 SKIP，不再白跑；
  `build.ps1 -Release` 调用门禁时默认传本开关，日常直接跑 `check.ps1`（不传）仍保持"跑完全部、
  一次看全"的原行为。② 重排步骤顺序：秒级、不依赖 Unity/重构建的检查（门禁自检、两道禁用词
  扫描、版本一致性、几道数据/schema 校验、工作树 CR 检查、Unity `.meta` 完整性）挪到最前面串行
  跑完；真实依赖关系保持不变（`dotnet build` 仍在 `dotnet test`/ABI 探针/数值仿真基线比对之前；
  DLL 同步仍在全部 Unity 相关步骤之前）。③ 把不需要 Unity 但耗时的几步（`dotnet build`/
  `dotnet test`、ABI 探针、占位资产生成器检查、样例导入幂等性门禁、`toolchain` 自身 pytest、
  数值仿真基线比对）收拢进新增子脚本 `toolchain/_gate_line_heavy.ps1`，与收拢了 DLL 同步/包清单
  一致性/Unity 四步/IL2CPP 三步/消费方演练的 `toolchain/_gate_line_unity.ps1`（内部保持串行，
  Unity 不允许同一工程有两个批处理实例同时跑）用 `Start-Job`（PowerShell 5.1 内建后台作业机制）
  并行跑，两条线跑完后把各自 PASS/FAIL/耗时合并进同一张汇总表，另加一行"并行阶段墙钟"（不计入
  步骤总数）。另外，禁用词扫描（全仓库游戏代号一项）改用 `git grep`，只扫受版本管理的文件，不再
  靠 `Get-ChildItem -Recurse` 遍历全仓库物理目录树后按目录名黑名单过滤（原实现慢的根因是遍历
  `Library\` 等 Unity 缓存目录本身，不是匹配）。共享的步骤运行基础设施（`Invoke-CheckStep`/
  `Test-NativeExitCode`/`Invoke-NativeAndWait` 等）抽到新增文件
  `toolchain/_gate_step_runner.ps1`，供 `check.ps1` 与两条并行子线 dot-source 复用。`-Quick`/
  `-SkipUnity`/`-DocsOnly`/全量四种模式下的步骤总数不变（28/28/29/31，实测核对），既有
  `.githooks/pre-commit`/CI 调用点无需改动。纯工程内部重排 + 新增可选开关，不改变任何一步检查
  本身的判据。

## [1.57.0] - 2026-09-22

### 新增

- **任务宿主契约补标题与目标描述文本键查询**（消费方反馈——游戏接入方第六批，
  [ADR-0064](architecture/adr/0064-任务宿主契约补标题与目标描述文本键查询.md)）：
  `Core.Gameplay.Quest.IQuestHost` 新增两个只读默认接口成员 `GetQuestTitleKey(questId)`/
  `GetObjectiveDescriptionKey(questId, objectiveIndex)`（转发 `QuestDefinition.TitleKey`/
  `QuestObjective.DescriptionKey`，未知任务/越界下标/数据未填均返回 `null`），生产实现
  `Core.Gameplay.Quest.QuestHost` 用显式接口实现转发。`Presentation.Ui.QuestLogViewModel`
  新增同名两个只读转发方法。新增路径 `player.quest.<questId>.title_key`/
  `.objective_description_key[i]`（既有 `player.quest.<questId>.state`/`.objective[i]` 行为
  不变）。框架参考面板 `Adapter.Unity.Ui.Panels.QuestLogPanel` 新增携带 `IL10nHost` 的
  `Construct` 加性重载，有标题键时优先经本地化解析显示任务名（既有两参数签名保留未动）。
  纯加法，ABI `breaks=0`。

### 修复

- **死亡生物不是"最近可交互目标"的候选**（消费方反馈——游戏接入方第七批第 1 条，
  [ADR-0065](architecture/adr/0065-死亡生物不是最近可交互目标的候选.md)；缺陷由 1.56.0 引入的
  `IInteractionTargetRegistry` 统一最近可交互目标查询带入，本版本修复）：`Core.Carriers.Assembly.
  InteractionTargetRegistry` 的候选判定此前只按 `Entity.Kind` 分类、不看生物是否存活——框架在
  单位死亡时不会自动 `Despawn`，死亡生物的运行期实体与它死亡结算生成的地面掉落物同坐标、等距时
  按 `EntityId` 序数取小（`creature.*` 恒小于 `loot.*`），导致每次击杀后 `TryFindNearest`/表现层
  `interact.nearest.*` 恒返回尸体，捡不到战利品，尸体较近时还会挡住其它存活单位。现改为生物类
  候选仅存活时成立（`IUnitAccess.Exists` × `IUnitAccess.IsAlive`，与 `player.alive`/`target.alive`
  同一权威口径）。改动完全落在候选判定内部（`InteractionTargetRegistry` 已持有的 `IUnitAccess`
  依赖），不新增公开签名、不新增可选参数，ABI `breaks=0`。

### 变更

- **提交前钩子按暂存改动分级**（`.githooks/pre-commit`）：不再对每次提交一律跑 28 步
  `check.ps1 -SkipUnity -Quick`（约 100 秒），改为先按 `git diff --cached --name-only` 判定
  三档并打印一行说明——暂存改动全部是 `.md` 时跑新增的 `check.ps1 -DocsOnly`（门禁自检、两道
  禁用词扫描、版本一致性、`toolchain/tests` 里 markdown 链接与编辑器文档一致性两个用例，秒级）；
  `build.ps1 -Release` 全量门禁通过后仅改五个版本文件（`VERSION`/两个 `package.json`/
  `packages-lock.json`/`CHANGELOG.md`）的发布提交不重复跑 `check.ps1`（识别条件：该脚本在
  `git commit` 前设置的 `WS_GAME_RELEASE_COMMIT` 环境变量 + 暂存清单确实只含这五个文件，两者
  缺一仍照跑）；其余情况照旧跑完整 28 步。分级判断收拢为纯函数
  `Get-PreCommitCheckTier`（`toolchain/_precommit_tiering_guard.ps1`），
  `toolchain/tests/test_precommit_tiering_guard.py` 用 pytest 覆盖。

## [1.56.0] - 2026-09-21

### 新增

- **地面掉落物原生 `interact` 意图分流与统一"最近可交互目标"查询**（消费方反馈第五批第 1 条，
  [ADR-0062](architecture/adr/0062-地面掉落物原生交互与统一最近可交互目标查询.md)）：`interact`
  意图新增第三条原生分流 `Core.Gameplay.Loot.LootInteractIntentTickHandler`（Args 携带
  `loot_instance_id`），与既有 gobj/creature 两条分流并列注册在 `TickPhase.TriggerEvaluation`
  （`GameplayAssembly` 接线，因 `LootHost` 是 L4 类型）。`LootHost` 新增
  `Interact(Id unitId, Id lootInstanceId)` 方法（判距复用既有 `LootOptions.PickupRange`，成功后
  直接调用既有 `PickUp`，不重复实现拾取逻辑）、只读属性 `Diagnostics`（`ILootDiagnostics`）、
  14 参数构造函数重载（新增可选 `ILootDiagnostics? lootDiagnostics`）；`LootPickupFailureReason`
  新增 `PermissionDenied`；`LootOptions` 新增委托类型 `LootPickupPermissionDelegate` 与可选属性
  `PickupPermissionChecker`（默认 `null` = 默认放行，无归属系统，同 `GobjOptions`/
  `CreatureInteractOptions` 既有回调注入点惯例）。新增
  `Core.Carriers.Common.IInteractionTargetRegistry`/`InteractionTarget`/`InteractionTargetKind`
  （`core/carriers/common`）与默认实现 `Core.Carriers.Assembly.InteractionTargetRegistry`
  （`CarriersAssembly` 新增只读属性 `InteractionTargets`）：统一覆盖 gobj/creature/loot 三类目标的
  "按最近距离找可交互目标"查询，不另开登记表，直接对 `IWorldSim` 现场求值。表现层新增
  `Presentation.Ui.InteractPathProvider`，新增路径 `interact.nearest.id|kind|distance`
  （`PresentationAssembly` 接线）。纯加法，ABI `breaks=0`。

- **装备宿主契约补模板 id 查询**（消费方反馈——游戏接入方第五批第 2 条，
  [ADR-0063](architecture/adr/0063-装备宿主契约补模板id查询.md)）：`Core.Carriers.Common.
  IEquipmentHost` 新增两个只读默认接口成员 `GetEquippedTemplateId(unitId, slot)`/
  `GetAllEquippedIdentities(unitId)`，新增元素类型 `EquippedItemIdentity`（实例 id + 模板 id）；
  生产实现 `Core.Carriers.Item.EquipmentHost` 用显式接口实现转发。新增路径
  `player.equipment.<slot>.template`/`.instance`（既有裸路径 `player.equipment.<slot>` 行为不变），
  `InventoryViewModel` 新增只读属性 `EquippedSlotIdentities`。纯加法，ABI `breaks=0`，新增 13 个
  公开签名（见 ADR-0063 验收记录）。

## [1.55.0] - 2026-09-21

### 新增

- **表现层新增普通攻击状态与单位存活状态转发**（消费方反馈第四批第 1/2 条，
  [ADR-0061](architecture/adr/0061-表现层补普通攻击状态与存活状态转发.md)）：新增路径
  `player.auto_attack.state`/`target.auto_attack.state`（经 `Core.Rules.Combat.AutoAttackHost.
  GetState` 转发，新增 `AutoAttackStateNames` 静态类做枚举与固定小写文本互转）与
  `player.alive`/`target.alive`（经 `IUnitAccess.Exists`×`IsAlive` 转发，无目标时为"无"，同
  `target.id` 既有口径）。`HudViewModel` 新增只读属性 `AutoAttackState`/`TargetAutoAttackState`
  （`Core.Rules.Combat.AutoAttackState` 枚举）、`PlayerAlive`（`bool`）、`TargetAlive`
  （`bool?`）。`PlayerPathProvider`/`TargetPathProvider` 各新增一个构造函数重载，
  `PresentationAssembly` 改走新重载装配 `AutoAttackHost`。纯加法，ABI `breaks=0`。

### 修复

- **`HudViewModel.Auras`/`TargetAuras` 的 `Polarity`/`IconRef` 恒为默认值**（1.53.0
  [ADR-0060](architecture/adr/0060-光环极性与图标引用字段补全.md) 引入的缺陷，消费方反馈第四批
  第 3 条）：`presentation/ui/core/ViewModels/HudViewModel.cs` 的 `RefreshAuras` 从路径查询结果
  逐字段重新组装 `AuraSnapshot` 时漏读新增的 `polarity`/`icon_ref` 两条路径、仍调用旧的五参数
  构造函数，规则层与路径查询两端均已正确转发，唯独这一环重新组装逻辑没有同步补上——导致每条
  快照的 `Polarity` 恒为 `Undeclared`、`IconRef` 恒为 `null`，即使对应光环定义已声明这两个字段。
  已改为读取 `{root}[i].polarity`/`{root}[i].icon_ref` 并改走七参数构造函数。全仓复查确认这是
  唯一的生产代码缺陷点，详见 `presentation/ui/README.md` 对应判断记录（含 1.53.0 验收测试为何
  没有拦住本缺陷、及后续同类"字段穿线"改动的验收深度要求）。

- **`consumer_smoke.ps1` 的 `Wait-NoResidualUnityProcess` 等别的仓库正在跑的 Unity 进程导致误报超时**：
  等待范围从"系统里任何 Unity.exe"收窄为"可能与本次演练撞车的两类工程"——本仓库根目录下的、
  本脚本工作目录（`gf_consumer_smoke`）下的；拿不到命令行或没有 `-projectPath` 时按保守口径当作
  要等；明确落在这两处之外的判定不等，但每次检测到都会打印一行提示（PID + 工程路径），不静默忽略。
  判断归属的纯逻辑抽成 `toolchain/_unity_smoke_wait_scope_guard.ps1` 的
  `Get-UnitySmokeProcessWaitDecision` 函数（三态返回 `Wait`/`NoWait`/`Unknown`），由
  `toolchain/tests/test_unity_smoke_wait_scope_guard.py` 覆盖，含仓库根同名前缀目录（如
  `ws-game-wow`）不被误判为 `ws-game` 子目录这一关键回归用例。`check.ps1` 的
  `Test-NoResidualUnityProcess`（只管同一工程、发现即报错）语义不同，未改动。

## [1.54.0] - 2026-09-21

### 新增

- **`AssetRootConventions` 新增 `DatasetDataDirectory` 的逆运算 `TryGetDatasetName`**（消费方
  反馈第 79 条，[ADR-0054](architecture/adr/0054-资产数据根目录与目录内固定文件名纳入公开契约.md)
  "决策 1"纳入同一契约范围）：给定数据根目录与磁盘上已探测到的一个目录路径，尝试还原出该目录
  对应的数据集名——消费方（内容编辑器项目）从"游戏仓库根 + 已探测数据目录"定位资产文件时，中间
  "数据目录 → 数据集名"这一步此前只能自行切路径末段。`toolchain/asset_import/ref_conventions.py`
  同步新增等价函数 `try_get_dataset_name`，两侧各自独立实现、跨语言一致性测试对照。签名：
  `public static bool TryGetDatasetName(string dataRoot, string datasetDataDirectory, out string dataset)`
  / `def try_get_dataset_name(data_root: Path, dataset_data_directory: Path) -> str | None`。
  边界口径（不在数据根之下/是数据根本身/更深层子目录一律返回"不成功"而非编造数据集名；分隔符与
  结尾分隔符归一化；`.`/`..` 段不做语义解析；大小写不敏感匹配父目录、但还原出的数据集名保留输入
  原样大小写）见 `AssetRootConventions.cs` 该方法 XML 注释与 `toolchain/README.md` 对应判断记录。
  纯加法，ABI `breaks=0`。消费方交付后可以拆掉自己拆路径末段的那段绕行代码。

## [1.53.0] - 2026-09-21

### 勘误

- `[1.52.0]` "消费方反馈第三批第 1/4 条"一节对施法条三个只读方法的描述停在并行切片期间的中间
  状态（只提了 `Core.Rules.Skill.CastPipeline`/`SkillHost` 具体类与
  `presentation/ui.ISkillBookQuery` 窄接口）：`GetCastingSkillId`/`GetCastingRemaining`/
  `GetCastingTotal` 三个成员合并收口时已进一步提升为 `Core.Rules.Common.ISkillHost` 的 C#8
  默认接口成员（只读查询，默认降级为 `null`），生产实现显式接口转发，`RulesAssembly.
  DeferredSkillCastQuery` 同步补上显式转发；`presentation/ui.SkillHostSkillBookQuery` 已删除
  期间的临时向下转型，改为直接经 `ISkillHost` 接口引用调用。详见
  [ADR-0056](architecture/adr/0056-施法条与光环列表数据补全.md) 决策 1（已订正为最终形态）、
  `core/rules/common/README.md` 判断记录 15。不改动 `[1.52.0]` 已发布条目正文。

### 新增

- **`skill.aura_def` 新增极性与图标引用两个可选字段**（一个发现的交付缺口，见
  [ADR-0060](architecture/adr/0060-光环极性与图标引用字段补全.md)）：`polarity`
  （`FieldKind.Enum`，取值 `beneficial`/`harmful`，缺省未声明）、`icon_ref`（`FieldKind.Id`，
  类别前缀限定 `icon`，见 ADR-0038/0039）。`AuraDef`/`AuraSnapshot` 各新增一个构造函数重载
  携带这两个字段；`Core.Rules.Common.IAuraQuery.GetActiveAuraSnapshots` 生产实现
  `Core.Rules.Skill.AuraHost` 同步透传。表现层 `auras[i].polarity`/`auras[i].icon_ref` 路径
  与既有 `auras[i].name_key` 同一套惯例。均为纯加法，未声明字段的既有 `skill.aura_def` 行零
  改动仍合法，不提升 schema 版本。ABI 只新增，`breaks=0`。

## [1.52.0] - 2026-09-21

### 新增

- **`ISkillHost` 新增 `AuraQuery`/`EffectSink` 只读入口（消费方反馈第三批第 3 条，
  [ADR-0058](architecture/adr/0058-技能宿主契约纳入光环查询与效果落地出口.md)）**：生产实现
  `Core.Rules.Skill.SkillHost` 已经以公开属性形式暴露这两个已接口化的出口（`IAuraQuery`/
  `IEffectSink`），但契约接口此前未声明，只持有 `ISkillHost` 引用的调用方拿不到，须向下转型或
  直接持有具体类。新增两个 C#8 默认接口成员 `IAuraQuery? AuraQuery`/`IEffectSink? EffectSink`
  （均默认降级为 `null`，只读查询口径，与写路径"禁止静默降级"口径无关，详见该 ADR）；
  `SkillHost` 既有同名公开属性物理签名/行为不变，新增显式接口实现转发；
  `RulesAssembly.DeferredSkillCastQuery` 代理同步补上显式转发。ABI 只新增，`breaks=0`。
- **动作条槽位快照补冷却总时长、充能与结构化不可用原因（消费方反馈第三批第 2 条，
  [ADR-0057](architecture/adr/0057-动作条槽位补冷却总时长充能与结构化不可用原因.md)）**：
  `presentation/ui` 的技能簿窄查询接口（`ISkillBookQuery`）新增默认接口成员
  `GetSkillReadiness(Id unitId, Id skillId)`，转发规则层技能宿主契约既有的只读就绪查询
  （`ISkillHost.GetSkillReadiness`/`SkillReadiness`，2026-09-11 已有能力，本次未改规则层
  任何文件）；生产适配器 `SkillHostSkillBookQuery` 显式覆盖直接转发完整结果，未覆盖时按与
  规则层同名成员一致的降级算法（只看冷却剩余）就地计算。`ActionBarSlotSnapshot` 新增四个
  只读字段：`EffectiveCooldownDuration`（`double?`，与既有 `Cooldown` 同类型，`null` 表示
  取不到，不用 `0` 冒充）、`MaxCharges`/`CurrentCharges`（`int?`，直接转发规则层充能计数，
  技能未配置充能或数据不可用时为 `null`）、`BlockReason`（新枚举 `ActionBarSlotBlockReason`，
  取值集合与优先级见 ADR-0057 决策 3）；既有两个构造函数字节级不变，新增一个七参数构造函数。
  `ActionBarViewModel` 新增一个携带 `IUiDiagnostics` 的六参数构造函数重载：取不到完整就绪
  数据时经既有诊断出口告警一次，槽位标记为 `BlockReason.Unknown`，不当作"无阻塞"处理；
  `PresentationAssembly` 的生产装配同步改用该重载。全部改动均为纯加法，ABI 探针
  `breaks=0`。
- **消费方反馈第三批第 1/4 条**（[ADR-0056](architecture/adr/0056-施法条与光环列表数据补全.md)）：
  施法条与光环列表数据补全。
  - `Core.Rules.Skill.CastPipeline`/`SkillHost` 新增只读方法 `GetCastingSkillId(Id unitId)`/
    `GetCastingTotal(Id unitId)`（`GetCastingRemaining` 已存在）：当前正在读条/引导的技能 id 与
    总时长，未在读条/引导时为 `null`。
  - `Core.Rules.Common.IAuraQuery` 新增只读默认接口成员 `GetActiveAuraSnapshots(Id unitId)`，
    返回新增类型 `Core.Rules.Common.AuraSnapshot`（光环定义 id/层数/剩余时长/总时长/名称键），
    按光环创建顺序排列（确定性）；生产实现 `Core.Rules.Skill.AuraHost` 显式覆盖。
  - `skill.aura_def` 新增可选字段 `name_key`（`FieldKind.TextKey`），命名/类型沿用
    `skill.def.name_key`（ADR-0048）同一惯例；`AuraDef` 新增对应构造函数重载。
  - `presentation/ui.ISkillBookQuery` 新增只读默认接口成员 `GetCastingSkillId`/
    `GetCastingRemaining`/`GetCastingTotal`；`PlayerPathProvider`/`TargetPathProvider` 新增
    `casting.skill|remaining|total`、`auras.count`/`auras[i].def|stacks|remaining|total|
    name_key` 路径；`HudViewModel` 新增只读属性 `CastingSkillId`/`CastingRemaining`/
    `CastingTotal`/`TargetCastingSkillId`/`TargetCastingRemaining`/`TargetCastingTotal`/
    `Auras`/`TargetAuras`。均为纯加法，不改动任何既有公开签名。
- **行为变更**：新增普通攻击（武器驱动的自动重复攻击）的框架原生执行机制
  （[ADR-0059](architecture/adr/0059-普通攻击的框架原生执行机制.md)，答复消费方反馈第三批第 5
  条：普通攻击此前完全没有框架原生执行路径）。新增
  `Core.Rules.Combat.AutoAttackHost`（`SetEnabled`/`SetTarget`/`IsEnabled`/`GetTarget`/
  `GetState`/`Update`）与 `AutoAttackTickHandler`（新增 `TickPhase.CombatResolution` 阶段处理器，
  只驱动连续时间模式）；`RulesAssembly` 新增只读属性 `AutoAttack`/`AttackIntervalFallback`。
  普通攻击到点结算复用既有 `weapon_damage_pct` 效果原语与 `Resolver.Resolve` 结算管线（不新增
  伤害公式），`combat.damage_dealt`/`unit.died` 与技能击杀逐字段同构，既有 XP/掉落监听器无需
  特判。`IWeaponDamageQuery` 新增默认接口成员
  `GetWeaponAttackIntervalSeconds`/`GetWeaponSchool`（缺省 `null`，`EquipmentHost` 显式实现）；
  新增 `IAttackIntervalFallbackProvider` 契约与 `creature.template` 新增可选字段
  `attack_interval`（未装备武器时的固定挥击间隔回退，纯新增字段不提升 schema 版本）。均为
  ABI 纯新增（新类型/新只读属性/新默认接口成员），不改动任何既有公开签名的默认运行时行为——
  未显式调用 `AutoAttack.SetEnabled(...)` 的既有单位不受任何影响，标记为行为变更是因为该模块
  本身是首次落地的新能力，且其运行时结算会真实产生伤害/事件（一旦游戏侧开始调用）。

## [1.51.0] - 2026-09-21

### 新增

- **资产/数据根目录与目录内固定文件名纳入公开契约（消费方反馈第 76、77 条，
  [ADR-0054](architecture/adr/0054-资产数据根目录与目录内固定文件名纳入公开契约.md)，续
  ADR-0053 同一病灶）**：
  - 第 76 条：新增 `Core.Foundation.EngineAdapter.AssetRootConventions`（`ResolveAssetsRoot`/
    `ResolveDataRoot`/`DatasetAssetsDirectory`/`DatasetDataDirectory` 四个方法），收口"从仓库根 +
    数据集名推导资产根/数据根目录"这条此前只写在 `toolchain/asset_import/common.py`
    `resolve_root` 与各子命令文档字符串里的约定。`toolchain/asset_import/ref_conventions.py`
    新增等价函数（`resolve_assets_root`/`resolve_data_root`/`dataset_assets_directory`/
    `dataset_data_directory`），`common.py` 的 `resolve_root` 对 `"assets"`/`"data"` 两个已知
    取值改为委托它们；全仓排查另发现 `toolchain/import_sample_assets.py` 独立维护的第二处
    重复实现，已一并改为调用共享函数（附带修正了一处行为分歧：相对路径覆盖值此前按当前工作
    目录解析，现按仓库根解析，与六个子命令一致）。
  - 第 77 条：`Core.Foundation.EngineAdapter.AssetRefConventions` 新增
    `VfxAtlasFile`/`VfxFramesFile`/`SpriteAnimAtlasFile`/`SpriteAnimFramesFile`/
    `SpriteSetAtlasFile` 五个方法，补齐 `vfx`/`sprite_anim`/`sprite_set` 三类目录型资源目录下
    固定产出文件名的公开契约；`ref_conventions.py` 同步新增等价函数。新增
    `Core.Foundation.EngineAdapter.EffectFramesDocument`（含 `EffectFrameData`），把
    `frames.json` 的结构化只读解析从引擎适配层内部私有实现（`Adapter.Unity.EngineAdapter.
    UnityResourceLoader.TryDecodeEffect`）提到核心侧，该方法改为调用它，不再自行解析。
  - **消费方交付后可拆除的自建绕行**：`AssetsRootResolver`、`ImportDatasetRootsConvention`
    等消费方为绕开本条缺口自建的机制，待本条交付后可改为直接调用上述公开方法并删除。
- **`ValidationReportFileOutlet` 落盘信封新增可选字段 `table`（消费方反馈第 78 条，
  [ADR-0055](architecture/adr/0055-运行期校验报告落盘出口补表名字段.md)）**：`DataHotReload.
  ReloadTable` 此前只把持有的 `table` 局部变量用于 `Debug.Log`/`Debug.LogWarning` 文本插值，
  从未传给 `ValidationReportFileOutlet.WriteIfConfigured`，导致落盘 JSON（ADR-0047）顶层
  `sequence`/`source`/`timestamp_utc`/`issues` 四个字段里没有"哪张表"，独立进程消费方（如随
  游戏走的编辑器）只能解析日志文本判定表名。`WriteIfConfigured`/`Write`/`BuildJson` 各新增一组
  带 `string? table` 形参的重载（既有重载保留并委托，`table` 缺省 `null`，ABI 只新增）；落盘信封
  顶层新增可选字段 `table`：`hot_reload` 来源是被重载的确切表名，`startup` 来源固定输出 JSON
  `null`（启动全量校验没有单一确定的表，不编造空字符串/`"all"` 等占位值）。唯一改动的调用点是
  `games/_template/Runtime/DataHotReload.cs` 的 `ReloadTable`；三处启动全量校验调用点
  （`GameBootstrap.cs`/`GameFoundationBootstrap.cs`/`FrameworkResidentHost.cs`）原样不动。
  `ValidationIssueJsonWriter`（逐条问题层，`toolchain/validator --json` 与本出口共用）不改动
  ——新字段属于信封层，与逐条问题自带的 `table` 字段（"这一条问题出在哪张表"）语义不同，也不
  互相替代；`toolchain/validator --json` 的信封天生覆盖"一批表"，没有对应的单一值，故不新增。
  编辑器交付后可以删除原有解析 `[DataHotReload] 热重载 "<表名>" ...` 日志行判定表名的路径，
  改读落盘 JSON 的 `table` 字段。

### 行为变更

- **`toolchain/import_sample_assets.py` 的相对路径根目录覆盖值改为按仓库根解析**（随第 76 条
  收口）：该脚本此前独立维护了第二处根目录推导，相对路径的 `--assets-root`/`--data-root` 覆盖值
  按**当前工作目录**解析，与六个子命令按仓库根解析的口径不一致。收口到共享函数后统一为按仓库根
  解析。只影响「从非仓库根目录调用该脚本、且传了相对路径覆盖值」这一种用法；传绝对路径或不传
  覆盖值的调用不受影响。


## [1.50.0] - 2026-09-21

### 新增

- **`import_assets.py check --json` 顶层新增 `domain_counts` 字段（消费方反馈第 74 条）**：
  文本汇总行此前已给出各域实际加载的记录条数（`_load_rows` 读到的行数，与该行是否命中检查
  条件、是否报出问题无关），但 `--json` 文档不携带这份信息，消费方无法据此拆除自建的文本正则
  解析。纯加法新增 `domain_counts`（`dict[str, int]`，键为 `CheckIssue.table` 已用的表名风格，
  如 `"display.map"`/`"vfx.def"`），既有六个顶层键（`tool`/`dataset`/`domains`/`ok`/`counts`/
  `issues`）名字/类型/含义均未改动。域未被 `--only` 选中时，该域对应的表键整体不出现（区分
  "未跑该域"与"跑了但 0 条"）；跨表键按表名字符串排序输出，保持确定性。详见
  `toolchain/asset_import/check_cmd.py` 模块 docstring"判断记录（--json 顶层新增
  domain_counts 字段）"。
- **目标框补显示名/阵营（沿用 [ADR-0048](architecture/adr/0048-任务起始方式补与场景物件交互取值.md)
  口径，接续消费方反馈第 3 条）**：`HudViewModel.TargetId` 只转发目标原始 `Id`，接入方要做目标框
  （展示被选中目标的名字与阵营）仍取不到数据，只能自行查表硬编码。`TargetPathProvider` 新增两条
  叶子路径：`target.name`（经 `IUnitAccess.GetTemplateId` 取内容模板 id，再经
  `ICreatureTemplateQuery.Get(...).NameKey` 取显示名文本键，口径同 `skill.def.name_key`——文本键，
  不是已本地化文本，不回退占位文案）、`target.faction`（经 `IUnitAccess.GetFaction` 转发阵营原始
  `Id`，同 `target.id` 口径）；无目标时两者均为 `null`（与 `TargetId` 既有口径逐字一致）。目标存在
  但模板未登记（`GetTemplateId` 为空，或模板 id 未在 `ICreatureTemplateQuery` 登记）时，
  `target.name` 记一条诊断后返回 `null`，不静默降级（AGENTS.md §3）。`HudViewModel` 新增只读属性
  `TargetName`/`TargetFaction`，`TargetPathProvider` 新增携带 `IUnitAccess`/`ICreatureTemplateQuery`
  的构造函数重载（既有三参数构造函数原样保留），生产装配 `PresentationAssembly` 改走新重载
  （传入 `gameplay.Carriers.Units`/`gameplay.Carriers.Creatures`）。纯加法：既有公开签名与全部
  `creature.template` 数据行零改动仍合法。新增装配级测试
  `presentation/assembly/tests/PresentationAssemblyTests.cs`
  （`HudViewModel_TargetNameAndFaction_ReflectRegisteredTemplate_NullWhenNoTarget`/
  `HudViewModel_TargetName_TemplateNotRegistered_ReturnsNull_AndRecordsDiagnostic`）。
- **`Core.Foundation.EngineAdapter.AssetRefConventions` 补齐地图分层图路径约定**（消费方反馈第 75
  条，[ADR-0053](architecture/adr/0053-地图分层图路径约定纳入公开契约.md)）：既有 14 个公开方法
  覆盖 sprite/icon/vfx/sfx/anim/model/sprite_anim/paperdoll 各类资源引用路径约定，唯独 `world.map`
  分层图（ground/overlay/decal/nav_hint）此前唯一的权威出处是
  `toolchain/asset_import/map_cmd.py` 的模块文档字符串，从未以方法形式暴露。新增
  `MapDirectory(Id mapId)`（地图目录）与 `MapGroundFile`/`MapOverlayFile`/`MapDecalFile`/
  `MapNavHintFile`（四个固定层各自的文件路径）共五个静态方法，与既有方法同一形态（签名风格、
  参数类型、返回值语义、非法输入处理口径一致），输出与 `map_cmd.py` 改动前的格式逐字节对齐
  （已用改动前历史版本核对，不是"看起来一样"）。`toolchain/asset_import/ref_conventions.py`
  同步新增 `map_directory`/`map_ground_file`/`map_overlay_file`/`map_decal_file`/
  `map_nav_hint_file` 五个等价函数（独立实现，不互相调用），`map_cmd.py` 改为调用这组函数，
  不再自行拼接路径、模块文档字符串不再重复给出格式字符串。新增
  `toolchain/map_ref_probe`（不对外发行、不进 Core.sln）供跨语言一致性测试经子进程调用取得
  真实运行期计算结果。ABI 只新增，`breaks=0`，`additions=5`。内容编辑器项目待本条交付后，可以
  拆掉自建的地图分层图路径拼接绕行代码，改为依赖这组公开契约。落地跟进：`toolchain/asset_import/
  check_cmd.py` 的 `_check_world_row` 此前也自行拼接了一份地图路径（第三处独立实现），现已改为
  调用同一组共享函数，行为对现有数据逐字节不变。

### 行为变更

- **掉落条目随机子流隔离（消费方反馈第 4 条根治，[ADR-0052](architecture/adr/0052-掉落条目随机子流隔离.md)）**：
  `core/gameplay/loot/core/LootHost.cs` 的掉落判定不再共用一条按登记顺序顺序推进的
  `LootOptions.RngStream`，改为按 `(loot_table_id, entry_ref)` 派生独立随机子流——一个条目自己
  消耗几次随机数（判定骰/数量骰/品质骰/词缀骰）只影响它自己这条子流的推进位置，不再挪动同一张表
  里其它条目、或其它表的结果。`weighted_pick_one`/`guaranteed_min` 的"选哪条"仲裁抽取仍留在共享
  流上（这一步依赖当前候选池整体，不存在能对它负责的单一条目），仲裁选中之后该条目自己的消耗才
  下放到子流。
  - **这是一次有意的行为变更，不是缺陷修复的"结果不变、只是实现变了"**：同一个种子下，掉落判定
    的具体产出序列与本版本之前不同。接入方若把某个固定种子下的具体掉落结果（物品、数量、品质、
    词缀）写进了自己的测试用例，升级后需要重新生成这些期望值——这是本次改动的直接、必然后果，
    不提供兼容开关。往掉落表插入/删除/调整条目不再牵动其它条目的结果，这一属性从本版本起成立。
  - **数值仿真基线同批重新烘焙**（`core/sim/tests/baseline/*.json`）：仅内置成长曲线场景
    （`sim_growth_full`，其怪物掉落表含固定必掉货币条目 + 多件同权重装备条目 + 一件带品质骰的
    稀有装备条目——正是本次修复的目标配置）的统计量发生实质变化；竞技场/覆盖度场景
    （`sim_arena_matrix`/`sim_coverage_all`）使用的掉落表此前就只有单一有效条目，不受本缺陷
    影响，基线里的具体数值未变，只是重新盖了版本戳。三份基线均无 `added`/`removed`（未出现新增
    或消失的统计量，只有既有统计量的数值变化）。

## [1.49.0] - 2026-09-20

### 修复

- **发布产物不可变强制校验（消费方反馈第 8 条根治）**：`build.ps1` 打分发包 `dist/<版本>/`
  目录、打 `dist/ws-game-<版本>.zip`/`.lock` 两处此前均无版本存在性校验，会无条件覆盖已发布
  版本的产物；唯一的防线（`-Release` 第 7 步 `git tag -a`）在产物已被覆盖之后才执行，且
  `-Dist`/`-Zip` 两条独立于 `-Release` 的打包路径完全不经过这道防线（复现：发布过 vX 后单独
  执行 `build.ps1 -Dist X -Zip`，三个产物被静默覆盖，同一 zip 两次打包出的哈希不同）。新增
  `toolchain/_dist_immutability_guard.ps1`（`Assert-DistVersionNotAlreadyReleased`），在三个
  产物真正写出之前校验目标版本号是否已存在对应 `v<版本>` 标签，已发布则直接报错终止并给出
  正确做法（发布新版本号）；新增 `-AllowOverwriteDist` 开关作为显式例外通道（默认关闭，用于
  重跑一次失败/半途的发布），启用时打印醒目警告。覆盖 `-Release`/`-Dist`/`-Zip` 三条入口的
  任意组合。
- **消费方反馈第 5/6 条根治：运行时静默降级补诊断（不改变任何既有数值/行为）**——
  `core/rules/combat.Resolver.ComputeMitigation` 对未登记 `combat.resist_curve` 的学派、
  `core/gameplay/progression_bridge.CreatureDeathXpListener`/`AreaTriggerDiscoveryXpListener`
  对未登记的 `prog.xp_source` 来源，此前均静默返回一个合法值（减免=0 / 不发放经验），接入方
  无法区分"数据漏配"与"设计如此"。`Resolver` 补一处遗漏的 `ICombatDiagnostics.Warn` 调用
  （按学派 id 去重，惯例同既有 `WarnMissingStatOnce`）；`progression_bridge` 新增
  `IProgressionBridgeDiagnostics`/`InMemoryProgressionBridgeDiagnostics` 契约（两个监听器
  共用同一份实例，`GameplayAssembly` 新增只读属性 `ProgressionBridgeDiagnostics` 转发，
  `adapters/unity` 侧登记为诊断来源 "Core.Gameplay.ProgressionBridge"），两个监听器各自新增
  一个带 `diagnostics` 参数、不带默认值的构造重载（ABI 只新增）。详见
  `core/rules/combat/README.md`/`core/gameplay/progression_bridge/README.md` 判断记录。
- **消费方反馈同构问题第三处根治：`core/gameplay/loot.CreatureDeathLootListener` 补诊断（不改变
  任何既有行为）**——`OnUnitDied` 用 `catch (ArgumentException)` 静默跳过死亡单位模板查询失败的
  分支（未登记的模板 id/记录存在但字段非法），与上一条 `progression_bridge` 那两处监听器同一病灶
  （用户侧表现"杀怪不掉东西且无任何线索"）。新增最小诊断契约 `ILootDiagnostics`/
  `InMemoryLootDiagnostics`；`CreatureDeathLootListener` 新增一个带 `diagnostics` 参数、不带
  默认值的构造重载（ABI 只新增，9 对 10 参）；`GameplayAssembly` 新增只读属性 `LootDiagnostics`
  转发，`adapters/unity` 侧登记为诊断来源 "Core.Gameplay.Loot"。顺带核实 `catch (ArgumentException)`
  的覆盖面：区分"未登记"与"字段非法"（含 `DataFieldException` 内层异常）两种成因给出不同诊断
  消息，不改变"跳过、不外抛"的控制流。详见 `core/gameplay/loot/README.md` 判断记录 19。
- **复审发现并根治 `sim_coverage_all` 基线漏烘焙 + `check.ps1` 补上 `Added` 阻断**：2026-09-16
  新增测试技能 `skill.sim_review_b_weapon_pct_strike`（提交 `17a66703`）扩大了 coverage 场景的
  统计面，产生了一条从未记录进基线的新增统计量（`coverage.skill.skill
  .sim_review_b_weapon_pct_strike.budget_ratio`），但当次未同提交重新烘焙基线；`check.ps1`
  "6b. 数值仿真基线比对"步骤此前只看 `simrunner` 退出码，而该工具契约"0=无 Exceeded/Removed"
  不含 `Added`，导致这条漂移在 31/31 全 PASS 下潜伏 4 天、跨两次发布未被发现。修复两处：①
  `check.ps1` 6b 步骤额外解析场景摘要行的 `added=<n>` 字段，任一场景非零即判本步骤失败（不改
  `simrunner` 退出码语义，理由见 `core/sim/README.md`"T-N6-7 判断记录"57）；② 用
  `toolchain/sim_baseline.ps1 -Scenario all -UpdateBaseline` 重新烘焙三份基线，补齐这一条统计量
  （已核对：仅此一个键新增，其余统计量/浮点值逐条比对无变化）。`AGENTS.md` §4 同步补充
  "`Added` 也算差异"。
### 新增

- **`combat.resist_curve` 饱和公式新增可选截距字段 `k0`**（消费方反馈第 7 条，
  [ADR-0049](architecture/adr/0049-抗性曲线饱和公式新增截距项.md)）：`ResistCurve.ComputeReduction`
  饱和分支公式由 `reduction = value / (value + k × attackerLevel)` 改为
  `reduction = value / (value + k × attackerLevel + k0)`；新增只读属性 `ResistCurve.K0`。
  `k0` 缺省 0，未登记时与改动前的公式逐位相同——已用
  `core/rules/combat/tests/ResistCurveInterceptBackCompatTests.cs` 对仓库随附的三处
  `combat.resist_curve` 数据集（默认示例数据根、`<game>` 模板数据根、内置数值仿真测试数据根）
  里全部既有 `kind=saturation` 记录逐位对比验证，不需要任何数据迁移；三份仿真基线
  （`core/sim/tests/baseline/*.json`）比对零增量差异。`data/_sample/combat/combat.resist_curve.json`
  新增探针样例 `combat.resist.physical_with_floor`（`k0=200`）演示新能力：修正饱和曲线在低等级
  内容里"任意正护甲值都换算出接近封顶减免"的退化区间。ABI 只新增，`breaks=0`。
- **`ISkillHost` 契约新增已知技能查询/学习成员，根治消费方反馈"契约缺口"（
  [ADR-0050](architecture/adr/0050-技能宿主契约纳入技能簿查询与学习成员.md)）**：`Knows`/
  `GetKnownSkills`/`LearnSkill(Id,Id)`/`LearnFromBook` 四个成员此前只在具体实现类
  `Core.Rules.Skill.SkillHost` 上，面向接口编程做不到（`core/carriers/item/contracts/
  SkillGranter.cs` 注释此前也自认这是契约缺口）；现以带默认实现的接口成员新增到 `ISkillHost`
  （只读查询默认降级为 `false`/空列表，写路径 `LearnSkill`/`LearnFromBook` 默认抛
  `NotSupportedException`，不静默降级），`SkillHost` 既有同名公开方法签名/行为不变，新增四个
  **显式接口实现**转发到既有方法（不用隐式实现——隐式实现会把既有方法物理 IL 属性改写成
  `virtual sealed`，被 ABI 探针静态签名比对误判为破坏，即便运行期实测完全兼容）；
  `core/rules/assembly.RulesAssembly` 内部代理 `DeferredSkillCastQuery` 新增四个成员的
  显式转发；表现层 `Presentation.Ui.SkillHostSkillBookQuery` 新增一个接受 `ISkillHost` 的构造
  函数重载（原具体类型重载保留）。与 ADR-0048 分支合并时另暴露一处同类缺口，一并按同一口径
  收口：`GetSkillNameKey`（ADR-0048 消费方反馈第 3 条新增的 `SkillHost` 公开方法）同样只在
  具体类上、表现层已面向接口装配却调用不到，因此一并提升为 `ISkillHost` 默认接口成员（默认
  返回 `null`），`SkillHost`/`DeferredSkillCastQuery` 同步补显式接口实现转发。ABI 只新增，
  `breaks=0`。新增测试
  `core/rules/skill/tests/ISkillHostSkillBookContractTests.cs` 钉住"仅持有 `ISkillHost` 接口
  引用即可完成学技能→查询已知→从技能书学"这条运行时链路。详见 `core/rules/common/README.md`
  判断记录 11、12，`core/rules/skill/README.md` 判断记录 58、59，`core/rules/assembly/README.md`
  同判断记录。
- **[ADR-0048](architecture/adr/0048-任务起始方式补与场景物件交互取值.md)：任务起始方式补
  `gobj_interact` 取值，技能/目标身份补显示字段，合并根治消费方第 9/3 条反馈**——
  - **第 9 条**：`quest.def.start_method` 枚举追加第五个取值 `gobj_interact`（`QuestStartMethod
    .GobjInteract`，追加在既有四值之后，序数/线上文本均不改动），描述框架早已存在的"交互一个
    `gobj.template.kind: quest_object` 场景物件触发任务接取"链路（`GameObjectHost.Interact` →
    `GobjOptions.QuestActionDispatcher` → `IQuestHost.Accept`，链路本身零改动，`IQuestHost.Accept`
    不因此新增任何门禁分支）。新增装配级集成测试
    `core/gameplay/assembly/tests/GameplayAssemblyGobjQuestStartTests.cs`：构造一条声明该取值的
    真实 `quest.def` 与一个指向它的 `gobj.template`，走真实"交互"接口验证任务状态从
    `Available` 转移到 `Active`；`core/gameplay/quest/tests/QuestStartMethodGobjInteractTests.cs`
    新增 `TryParseStartMethod` 往返测试覆盖新取值与全部既有取值不受影响。样例数据新增
    `quest.sample_gobj_start`/`gobj.sample_quest_marker` 一对互相引用的记录演示新取值。
  - **第 3 条**：`skill.def` 新增可选字段 `name_key`（`FieldKind.TextKey`，缺省不渲染不回退
    占位文案，命名/类型沿用 ADR-0043 `greeting_key` 惯例）；`SkillDef`/`SkillDefCache`/
    `SkillHost.GetSkillNameKey` 均以 ABI 纯新增方式（新构造函数重载/新只读方法）解析并转发。
    `HudViewModel` 新增只读属性 `TargetId`（经 `TargetPathProvider` 新增的 `target.id` 叶子路径，
    转发原始 `Id`，不解析显示文本，无目标时为 `null`）。`ActionBarViewModel`/
    `ActionBarSlotSnapshot` 新增 `NameKey`（经新增的携带 `ISkillBookQuery` 的构造函数重载解析，
    `ISkillBookQuery` 新增默认接口成员 `GetNameKey`）。消费端（`adapters/unity` 参考界面）
    同步更新：`HudPanel` 目标框补身份短串展示，`ActionBarPanel.Construct` **新增**携带
    `IL10nHost` 的构造函数重载（既有三参数签名原样保留、行为不变，生产调用方
    `UiPanelHost.Initialize` 改走新重载），槽位标签在新重载下改为优先展示技能真名，取不到
    `name_key` 或走旧签名时才回退既有的技能引用短串展示（不是编造占位文案）。样例数据
    `skill.sample_strike` 新增 `name_key` 演示。
  两条反馈均为纯加法：全部既有 `quest.def`/`skill.def`/`gobj.template` 数据行零改动仍合法，
  全部既有公开签名（含 `ActionBarPanel.Construct`）保持不变，新增能力一律走新重载/新增
  成员落地。
- **生物原生交互路径（消费方反馈第 2 条根治，
  [ADR-0051](architecture/adr/0051-生物原生交互路径.md)）**：此前"交互"意图唯一的消费者只认
  场景物件（gobj），接入方要让 NPC 可被直接交互（打开对话），只能把生物伪装成场景物件模板。
  新增 `Core.Carriers.Common.ICreatureInteractionHost`（`Interact(Id unitId, Id
  creatureInstanceId)`，复用既有 `InteractResult`/`InteractOutcome`）与默认实现
  `Core.Carriers.Creature.CreatureInteractionHost`；`CreatureTemplate` 新增可选字段
  `GossipMenuRef`（指向 `dialog.gossip_menu`，缺省表示该生物当前没有原生可交互内容，纯新增
  可选字段，既有数据行行为不变）；新增 `Core.Carriers.Creature.CreatureInteractIntentTickHandler`
  挂载 `TickPhase.TriggerEvaluation`，与既有 `Core.Carriers.Gobj.InteractIntentTickHandler`
  共用同一个 `"interact"` 意图种类，按 `Args` 是否携带 `creature_instance_id`/
  `gobj_instance_id` 分流，互不影响。新增回调类型
  `CreatureGossipOpenerDelegate(Id unitId, Id creatureInstanceId, Id dialogRef)`（命名对齐
  ADR-0044 的 `DialogOpenerWithSourceDelegate` 形状，如实反映携带的是生物实例身份），经新增
  `CreatureInteractOptions.GossipOpener` 注入；`CarriersAssembly`/`GameplayAssembly` 各新增
  一个构造重载（ABI 只新增，既有签名原样保留转发），新增只读属性
  `CarriersAssembly.CreatureInteractions`。目标生物未登记/未配置 `GossipMenuRef`/已配置但
  未注入 `GossipOpener` 三种情形均经新增的 `ICreatureDiagnostics` 留痕（交互距离不足维持
  既有静默惯例）。详见 `core/carriers/creature/README.md` 判断记录 13。

### 文档

- **审计证据生成物出库**：`architecture/落地计划/audit-*/` 下历次审计留存的一次性构建/测试
  日志、命令原始输出、测试结果 xml、哈希/lock 快照、exit 码、diff patch 等生成物 `git rm`
  720 个文件（约 20.74MB），配套 `.gitignore` 新增规则拦截同类文件今后再次入库；结论性 `.md`
  文档与复现工程输入（`.cs`/`.csproj`/`.ps1`/`.py`/`.asmdef`/`.props`/`.meta`）不受影响。
- **`AGENTS.md` §4/§7 补回归分级唯一口径**：§4 用"回归分级"替换此前"每次改动都全量"的旧
  条款——日常切片只跑自身运行时冒烟 + 直接波及模块的定向重跑，全量回归（G1 的 `dotnet test`/
  `pytest`/三套数据根全量、G2 的 `check.ps1 -Quick` 全量）只在里程碑收口、升级框架/依赖版本、
  改生产装配入口或多模块共享数据三种时刻跑，执行 agent 不得为"再确认一次"自行追加；§7 明确
  独立验收 agent 只在里程碑收口时派一次，文档类、登记类提交不派验收。
- **新增 `REGRESSION_LOG.md` 记录全量回归结果**：只记录全量回归（对应 §4 三种时刻）的
  `run_id`/通过或失败/对应提交 sha/日期，每轮只追加一行不改历史行；日常切片的定向重跑不记录
  在此，落在各自提交信息与模块 README"判断记录"里。

## [1.48.0] - 2026-09-20

### 新增

- **[ADR-0046](architecture/adr/0046-运行期校验报告结构化转发出口.md)：运行期校验报告结构化
  转发出口，根治消费方第 71 条反馈**——核实"接入 ADR-0042 统一诊断转发机制"在结构上不成立（该
  机制只承诺文本消息列表、按精确文本去重、面向持续增长列表轮询，与校验报告离散触发、逐条结构化
  字段、每次触发都独立成立三点结构性冲突，见 ADR-0046 决策 2）后，改为对 `data_registry` 自身
  已有的结构化广播事件做加性扩展：`DataValidationFailedEvent`（`core/foundation/data_registry/
  contracts/Events.cs`）新增三参数构造函数，携带 `IReadOnlyList<ValidationIssue> Issues`（ABI
  只新增，既有两参数构造函数保留、内部委派，`Issues` 默认空集合、不是 `null`）；`DataRegistry.
  LoadAllCore` 与 `games/_template/Runtime/DataHotReload.cs` 两处既有发布点均已改用新构造函数
  传入 `report.Issues`。`ValidationIssue` 公开只读字段与 `toolchain/validator --json` 的
  `issues[]` 元素字段一一对应，两条通道天然同形状。同时修正 `games/_template/Runtime/
  GameBootstrap.cs`/`DataHotReload.cs` 两处校验失败日志此前用 `Debug.LogError`（与 ADR-0042
  决策 4"诊断消息一律不产生 Error 级、避免触发宿主自动化测试框架失败判定"的既有硬约束冲突，是
  框架侧遗漏，不是消费方问题）改为 `Debug.LogWarning`，人类可读文本内容不变。**局限（如实
  标注，未解决）**：本次只服务与运行期宿主同进程内的结构化消费方；若消费方是完全独立的外部进程
  （如"编辑器随游戏走"独立工程），本次改动不解决其读取运行期校验结果的诉求，需要另一种跨进程
  送达的结构化出口，属于需要设计层单独拍板的新对外契约，ADR-0046 明确留白、未擅自实现。
  `core/foundation/data_registry/README.md`"`ValidationIssue.ToString()` 格式稳定性声明与运行期
  结构化出口现状"一节已同步更新第 71 条状态。测试：`core/foundation/data_registry/tests/
  DataRegistryTests.cs` 新增 4 例（阻断态事件携带的 `Issues` 与报告逐字段一致、非阻断态不发出该
  事件、两参数/三参数构造函数的 `Issues` 默认空集合语义）。
- **Unity 相关门禁步骤新增 Windows MAX_PATH 快速失败守卫**（`toolchain/_unity_path_length_guard.
  ps1` 的 `Test-UnityWorkingTreePathLength`，`check.ps1` 在 Unity 相关四步 + 消费方演练入口调用，
  不经 `Invoke-CheckStep` 包裹）：深层 scratchpad 工作树里跑 Unity PlayMode 测试会踩 Windows 260
  字符 MAX_PATH（`GameTemplateResidentTests.ResidentRunner_DatasetRootOverride_
  LoadsProbeTable_FromOverrideRootOnly` 用例运行期把 `data/game` 整棵目录树复制到一个带 32 位
  十六进制 GUID 的新目录，工作树根路径一深，复制出来的文件绝对路径就可能超限），且 Mono/.NET
  旧式路径 API 此时抛的是 `DirectoryNotFoundException` 而不是 `PathTooLongException`，症状会
  伪装成"目录没建出来"、容易被误判为产品缺陷。新增校验在真正调用任何 Unity 批处理之前，实际
  扫一遍 `data/game` 下最长的相对路径（不硬编码具体字符数），代入覆盖目录名模板估算最长生成
  路径，超阈值（260 - 10 字符余量）直接报错终止，错误信息含当前根路径长度、预估最长路径长度、
  260 上限与"怎么办"指引（换到主检出或路径足够短的工作树）。`AGENTS.md` §4 同步补充"不要在
  深层 scratchpad 工作树里跑 Unity 测试步骤"条目。**同日复审修复一处会让守卫在头号场景失效的
  缺口**：最初实现只扫 `adapters/unity/.../data/game`——这是 `.gitignore` 忽略的生成目录（由
  `build.ps1 -SyncOnly` 同步生成），新建的工作树在跑过一次同步之前该目录不存在，旧实现遇到这
  种情况会静默放行，而"深层 scratchpad 新建工作树后立刻跑 Unity 步骤"恰恰是本守卫要防的头号
  场景。改为同时扫两个候选根（随仓库提交的源目录 `games/_template/data/game` + 生成目录），
  存在的根取最长相对路径的 max；两个候选根都不存在时也不再静默放行，按 AGENTS.md §3"只读分析
  类入口在遇到阻断态时降级要显式标记"改为打印醒目警告后继续（不阻断门禁，理由见该函数判断
  记录）。测试：`toolchain/tests/test_unity_path_length_guard.py`（7 例：短根路径放行 ×3
  场景【只有源目录/只有生成目录/两者都在】、深根路径终止 ×3 同样场景【含此前会被漏判的"只有
  源目录存在"场景】、两个候选根都缺失时打印警告但不阻断）。
- **[ADR-0047](architecture/adr/0047-运行期校验报告落盘出口.md)：运行期校验报告落盘出口，补齐
  ADR-0046 决策 6 留白的跨进程消费诉求**——运行期宿主把每次校验结果（不论通过/阻断）原子落盘为
  结构化文档，供与宿主完全独立的外部进程（如编辑器）监视/轮询读取，不需要解析任何日志文本。落盘
  路径由新增的运行期选项显式指定（命令行参数 `-gfValidationReportPath` 或环境变量
  `GF_VALIDATION_REPORT_PATH`，命令行优先），未设置时不产生任何文件（不设默认路径，见 ADR-0047
  决策 3）；文档带单调递增序号与触发来源标识（`startup`/`hot_reload`）。新增
  `Core.Foundation.DataRegistry.ValidationIssueJsonWriter`（`core/foundation/data_registry/
  contracts/ValidationIssueJsonWriter.cs`，ABI 只新增：新增公开静态类 + 两个公开静态方法）——
  把此前只在 `toolchain/validator/Program.cs` 内部私有的 `ValidationIssue` JSON 序列化逻辑
  （`AppendIssueJson`/`JsonEscape`）搬到本模块公开，`Program.cs` 改为委托调用，落盘出口与命令行
  `--json` 出口从此共用同一份序列化实现，不会漂移。新增
  `Adapter.Unity.Diagnostics.ValidationReportFileOutlet`
  （`adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Diagnostics/
  ValidationReportFileOutlet.cs`）承载选项解析、信封字段组装与原子写（临时文件 + 改名），已接入
  四个已知校验点：`games/_template/Runtime/GameBootstrap.cs`（启动校验）、`games/_template/
  Runtime/DataHotReload.cs`（热重载校验，`Initialize` 新增四参数重载携带落盘路径，ABI 只新增，
  既有三参数重载保留）、`Adapter.Unity.Bootstrap.GameFoundationBootstrap`/`Adapter.Unity.Shell.
  FrameworkResidentHost`（启动校验）。**顺带修复**：后两处校验失败日志此前仍用 `Debug.LogError`
  （违反 ADR-0042 决策 4，`GameBootstrap.cs`/`DataHotReload.cs` 两处同类问题已在 ADR-0046 修正，
  这两处当时不在其范围内），本次一并改为 `Debug.LogWarning`，人类可读文本内容不变。四处校验点的
  接线一致性由新增的 `toolchain/tests/test_validation_report_file_outlet_wiring.py` 守护（11
  例）。测试：`adapters/unity/DiagnosticsForwarding/tests/ValidationReportFileOutletTests.cs`
  新增 11 例（选项解析、落盘 JSON 形状与命令行 `--json` 逐字段一致、校验通过时写出空 `issues`、
  序号按路径分别单调递增、原子写失败时清理临时文件且不产生半成品文件）。

### 文档

- **消费方问询文档改写为通知文档**：原 `消费方问询-2026-09-20-第71条反问答复要求.md`
  发出后消费方一直未答复，设计层已通过 ADR-0046/ADR-0047 直接把契约定下来并落地，原问询的三个
  问题因此不再需要作答，继续留着会让消费方误以为还欠一份回复。已 `git mv` 改名为
  [消费方通知-2026-09-20-校验报告消费契约.md](architecture/落地计划/消费方通知-2026-09-20-校验报告消费契约.md)
  并整篇改写为通知：一句话复述原诉求、逐条列出已定契约（日志文本不是契约的硬边界、同进程走
  `DataValidationFailedEvent.Issues`、跨进程走落盘出口、两条通道的 `issues[]` 字段形状与命令行
  `--json` 共用同一份序列化实现）、给消费方按"是否与宿主同进程"给出迁移选择建议，并明确原三个
  问题不需要回答。`core/foundation/data_registry/README.md` 两处引用旧文件名/旧节标题的链接与
  正文同步更新，不留死链或过期的节名引用。
- **AGENTS.md §1 补一条 Unity 导入范围新文件的提交时机规则**：派单若会在 `adapters/unity/Assets`、
  `adapters/unity/Packages/com.gamefoundation.adapter.unity`、`adapters/conformance`、
  `games/_template` 四个根内新建文件，执行 agent 不得自己提交——新文件的 `.meta` 须由真实 Unity
  导入生成，执行 agent 不开 Unity，自行提交会被 meta 门禁拦下（1.46.0、1.48.0 各踩过一次同类
  问题）。规则明确正确做法（`git add` 暂存后停下汇报，由主会话搬到主检出跑一次含 Unity 的门禁
  补齐 `.meta` 再提交）与禁止项（不得 `--no-verify` 绕过）。

## [1.47.0] - 2026-09-20

### 新增

- [ADR-0043](architecture/adr/0043-gossip菜单新增可选开场白正文字段.md)（消费方反馈第 3a/3b 条，
  `ws-game-wow` 2026-09-20 反馈文档）：`dialog.gossip_menu` 新增可选字段 `greeting_key`
  （`FieldKind.TextKey`，兼容变更，旧数据零改动即合法）；`GossipMenuDefinition`/`GossipView`
  各新增一个构造函数重载承载该字段（ABI 只新增，原有构造函数不变）；`DialogHost.OpenGossip`/
  `GetGossipView` 透传 `menu.GreetingKey` 到 `GossipView.GreetingKey`。
- `adapters/unity` 的 `DialogPanel.RefreshUi`：gossip 分支不再硬编码占位文案"（NPC 对话选项）"，
  改为有 `GreetingKey` 时经 `_l10n.Text(...)` 渲染，缺省时隐藏正文标签、不渲染正文区（不回落任何
  占位文案，取舍见 ADR-0043）。
- 测试：`core/gameplay/dialog/tests/ADR0043_GossipGreetingKeyTests.cs`（3 例：字段登记为可选
  `TextKey`、`GossipMenuDefinition.FromRecord` 有/无 `greeting_key` 两种解析情形）；
  `DialogHostTests.cs` 新增 2 例（`GreetingKey` 经 `OpenGossip`/`GetGossipView` 正确透传、缺省时
  为 `null` 且不抛异常）。
- `data/_sample/dialog/dialog.gossip_menu.json` 的 `dialog.sample_hunter` 补 `greeting_key`
  样例；`data/_sample/l10n/l10n.text.json` 补对应的中/英两条文本。
- **ADR-0044：gobj 对话打开回调新增交互者身份透传路径**（消费方反馈-2026-09-20，`ws-game-wow`
  复审报告"反馈 6"）：`Core.Carriers.Gobj.GobjOptions` 新增可选属性
  `DialogOpenerWithSource`（新增委托类型 `DialogOpenerWithSourceDelegate(Id unitId, Id
  gobjInstanceId, Id dialogRef)`），`GameObjectHost.Interact` 分发 `on_use: dialog` 时优先调用
  该回调，携带触发交互的 gobj 实例自身 id；未设置时退回既有 `DialogOpenerDelegate`
  （`DialogOpener` 属性），两者都未设置时行为不变。生产装配根（`GameplayAssembly`）改接新回调，
  `DialogHost.OpenGossip` 的 `npcId` 参数改传 gobj 实例 id，不再用对话菜单引用 id 顶替——
  vendor 等动作按"当前交互对象自身"推断的下游能力从此能拿到正确的交互对象身份。ABI 只新增，
  不改动任何既有公开签名。详见 [ADR-0044](architecture/adr/0044-gobj对话打开回调新增交互者身份透传路径.md)、
  `core/carriers/gobj/README.md` 判断记录 12、`core/gameplay/assembly/README.md` 判断记录 3。
- [ADR-0045](architecture/adr/0045-任务给予者指示器只读查询.md)（消费方反馈第 4 条）：新增只读
  聚合查询 `Core.Gameplay.Quest.QuestGiverIndicatorQuery.Evaluate(IQuestHost, Id, IReadOnlyCollection<Id>)`
  与枚举 `QuestGiverIndicatorState`（`None`/`Available`/`InProgress`/`Completable`），聚合"某个任务
  给予者对当前玩家处于何种任务指示器状态"，完全基于既有 `IQuestHost.GetState` 求值，不新增持久化
  状态、不改 `quest.def` schema、不修改 `IQuestHost` 既有成员（纯新增静态类型）。给予者身份沿用现有
  单位/NPC 身份表示，给予者关联的任务集合由调用方给出；多态并存时按 `Completable` > `Available` >
  `InProgress` > `None` 固定优先级裁决。不覆盖"等级不足预览"等中间态（见 ADR-0045"边界"一节）。
  具体呈现（图标/颜色/动画）不属于框架职责，一律归游戏侧决定。
- **消费方反馈第 2 条根治：`QuestLogPanel` 补全目标进度展示**——参考 UI 任务日志面板此前只展示
  任务 id 与状态，不展示每条目标的进度（数据链路本身是通的，`QuestProgress.ObjectiveCounts` 早已
  可读，缺口纯粹是参考 UI 未读取，不涉及契约变更、不需要 ADR）。`IQuestHost` 新增只读查询方法
  `GetObjectiveRequiredCounts(Id questId)`（默认接口成员，ABI 只新增，不改动任何既有公开签名，
  未登记/已删除的任务定义降级返回空列表、不抛异常）；`QuestLogViewModel` 新增同名转发方法；
  `QuestLogPanel.RefreshUi` 据此拼"当前/需求"文案，边界处理覆盖空任务日志、需求数量缺失、
  计数超过需求、任务已完成四种情形，均不抛异常，数字格式化固定 `InvariantCulture`。测试：
  `core/gameplay/quest/tests/QuestHostTests.cs` 新增 2 例、`presentation/ui/tests/
  ViewModelTests.cs` 新增 1 例、`adapters/unity/.../Tests/Runtime/UiSuiteTests.cs` 新增 1 例
  PlayMode 测试。详见 `core/gameplay/quest/README.md` 判断记录 19、`presentation/ui/README.md`
  同名判断记录、`adapters/unity/.../README.md`"判断记录索引"对应条目。

### 文档

- **消费方反馈处理记录（wow，交互/gossip/任务日志/NpcFlag/持久化/掉落RNG 七项核实，
  2026-09-20）**：新增
  [消费方反馈-2026-09-20-wow-交互gossip任务日志掉落RNG.md](architecture/落地计划/消费方反馈-2026-09-20-wow-交互gossip任务日志掉落RNG.md)，
  逐条核实消费方（`ws-game-wow`）2026-09-20 提交的反馈文档《交互/gossip/任务日志/NpcFlag/
  持久化注册策略/掉落RNG 共七项核实》：7 条候选反馈中 7 条成立或部分成立、1 条（候选 5（原稿）：
  持久化注册策略"两种并存"）经框架侧复核确认确实不成立，与消费方自查结论一致；已成立的 7 条中
  4 条本轮已落地（即上文 ADR-0043、ADR-0044、ADR-0045 与任务日志目标进度条目）、2 条方向已定但
  本轮未落地（反馈 1、反馈 7，需单独一份 ADR 才能推进）、1 条（反馈 5）本轮不改代码，仅在文档中
  澄清口径差异。

## [1.46.0] - 2026-09-20

### 破坏性变更

[ADR-0041](architecture/adr/0041-数据注册表单记录容错查询语义修正.md)（2026-09-19，已拍板并
落地）：`IDataRegistryView.TryGet(string, string, out DataRecord)`/`TryGet(string, CommonId,
out DataRecord)`（消费方反馈第 45 条，1.38.0 引入）返回值语义修正——发布后实测证明其恒返回
`true`（哪怕 `record` 为 `null`），只要注册表未处于阻断态，与同类查询方法"返回值即是否找到"的
通用调用惯例相悖；已确认消费方两条回归用例（长驻可交互运行入口的数据集根覆盖生效验收）因此写出
永远通过的断言，直到本次任务实跑门禁才暴露。方法名与参数签名不变，只改变"记录不存在但未阻断"
这一分支的返回值。

**before/after**（三种情形逐一对照，两处物理实现——接口默认实现、`DataRegistry` 显式覆盖——
口径一致）：

| 情形 | 修正前 | 修正后 |
|---|---|---|
| 记录存在 | 返回 `true`，`record` 为该记录 | 不变 |
| 记录不存在（表可正常访问，未阻断） | 返回 `true`，`record` 为 `null` | 返回 `false`，`record` 为 `null` |
| 整体阻断态（数据校验未通过） | 返回 `false`，`record` 为 `null` | 不变 |

`TolerantRegistryView.Get`/`TryGet`（同批消费方反馈第 45 条引入的包装类型）同步调整内部判定：
此前直接用 `_inner.TryGet` 的布尔返回值反推"是否阻断"，该返回值修正后不能再用于此目的（阻断与
"记录不存在"现在都会让 `TryGet` 返回 `false`），改为"找不到时另外探测 `_inner.TryGetAll` 确认
是否阻断"的两步判定，`IsDegraded`/`MissingTables`/`WasMissing` 对外语义与诊断准确度不变。

全仓扫描确认的调用点：`core/foundation/data_registry/tests/GameDatasetRootOverrideEquivalenceTests.cs`
两处、`core/foundation/scene_router/tests/SceneDescriptorTests.cs` 一处——均配合断言记录字段值
或阻断态直读通道，不单独依赖返回值表达"找到与否"，修正后行为不变（前者的 `Assert.True(found)`
断言反而从此前的"恒真摆设"变为真正有效）；`games/_template/Tests/Runtime/
GameTemplateResidentTests.cs` 两条 PlayMode 用例已在 1.45.0 随二次修复改用 `Get(...)` 返回值
判空，不再依赖 `TryGet` 布尔值，不受本次修正影响。消费方通知文档：
[消费方通知-2026-09-19-TryGet契约行为修正.md](architecture/落地计划/消费方通知-2026-09-19-TryGet契约行为修正.md)。

### 新增

- 三种情形（记录存在/不存在/整体阻断）的回归测试：`core/foundation/data_registry/tests/
  DataRegistryTests.cs`（`TryGet_DefaultInterfaceImplementation_*`/`TryGet_OnDataRegistry_*`
  共 7 例）、`core/foundation/data_registry/tests/TolerantRegistryViewTests.cs`
  （`Get_PartiallyPopulatedInnerView_*`/`TryGet_PartiallyPopulatedInnerView_*` 共 4 例）。

- **`check.ps1` 新增"Unity `.meta` 完整性检查"步骤（不依赖 Unity 本体）**：本会话内先后多次
  出现新增 `.cs` 漏提交对应 `.meta`（1.45.0 发版前 `games/_template/` 下 6 个文件、随后
  `Runtime/Diagnostics/` 下 2 个文件），根因是执行 agent 一律不跑 Unity、而 `.meta` 恰恰是
  Unity 首次导入新文件时才在本机生成的产物，纯 .NET/Python 门禁此前完全查不出这类问题。新增
  `toolchain/check_unity_meta.py`：只读 git 索引 + `.gitignore` 规则判断 Unity 实际导入范围
  （`<工程>/Assets/`、`Packages/` 下本地包、`manifest.json` 里 `"file:"` 声明的内嵌本地包）内
  每个已跟踪文件/目录是否都有对应 `.meta`（含目录本身，见下方判断记录）、每个 `.meta` 是否都还
  对应一个被跟踪的文件/目录（含"目录被 `.gitignore` 整体排除、但目录级 meta 为保留稳定 GUID
  仍提交"的既有例外），不需要启动 Unity，`check.ps1` 新增步骤放在"工作树文本文件无 CR"之后、
  "版本一致性"之前，`-SkipUnity`/`-Quick` 下同样跑。回归测试：`toolchain/tests/
  test_check_unity_meta.py`（独立临时 git 仓库，11 个场景 + 1 条真实仓库现状回归，共 12 例）。

  落地当次实测在 `main` 上先后照单验出三处此前无人发现的真实缺口，均已修复，最终状态零缺失、
  零孤儿：① `Runtime/Diagnostics/` 下两个新文件漏提交 `.meta`（本检查立项起因，由真实 Unity
  导入补齐，提交 `90691cc3`）；② 修复①时 Unity 额外生成了目录级的 `Runtime/Diagnostics.meta`
  ——促成本检查补齐"目录本身也要有 `.meta`"的判定能力（`git ls-files` 只返回文件路径，第一版
  实现漏了这一层）；③ `adapters/unity/Assets/Resources/GameFoundation/...` 下已有 17 个永久
  提交的占位资产，但 `.gitignore` 仍把父目录 `Resources.meta` 整体忽略（该条规则写于"目录此前
  不存在、由测试框架自动创建"之时，前提早已过时，与仓库现状自相矛盾）——修复方式是撤销这条过期
  忽略规则（`PerformanceTestRun*` 一条运行时文件忽略保留），磁盘上已有的 `Resources.meta`
  正常纳入跟踪，不是手写、也不是放宽检查（提交 `29d7e5bd`）。三处均属于"纯 .NET/Python 门禁
  完全看不见、只有对照 Unity 实际导入范围才能发现"的一类问题，详见 `toolchain/README.md`
  "Unity `.meta` 完整性检查"一节判断记录。

### 修复

- **`toolchain/tests` 下 PowerShell 子进程用例不再受"pytest 从哪个 shell 启动"影响**：
  `test_real_baseline_end_to_end_via_abi_probe`（`test_abi_surface_compare.py`）在部分开发机
  上稳定复现失败（`abi_probe.ps1` 报 `The term 'Get-FileHash' is not recognized ...`）——根因
  是从 PowerShell 7（pwsh，MSIX 打包安装）里拉起 Windows PowerShell 5.1 子进程时，Python
  `subprocess.run` 会把 pwsh 7 自己的 `PSModulePath`（含一条 PowerShell 7 专属的 MSIX 模块
  目录）原样继承给 5.1 子进程，5.1 在该外来模块目录下做命令自动发现时内部命中非终止性异常，
  被调用脚本设置的 `$ErrorActionPreference = "Stop"` 把它提升为终止性异常，整个自动发现流程
  中止，连内置 `Get-FileHash` 都解析不到；`check.ps1` 的 ABI 探针步骤用 pwsh 的 `&` 调用运算符
  起子进程不受影响，是"同一份 `abi_probe.ps1`，`check.ps1` 跑得过、pytest 跑不过"的原因，已用
  最小复现锁定并反向确认（详见 `toolchain/README.md`"`toolchain/tests` 里 PowerShell 子进程
  用例的确定性环境"一节）。新增 `toolchain/tests/_ps_subprocess_env.py` 的
  `clean_powershell_env()`，给 Windows PowerShell 5.1 目标显式设置一份干净的原生
  `PSModulePath`（不覆盖 pwsh 目标），并接入 10 个测试文件的全部 13 处启动 PowerShell 子进程
  的 `subprocess.run` 调用点，不只修最先暴露的那一个。

- 消费方反馈第 73 条：`DataRegistry.RunValidationAndBuildReport` 单条 `IValidationRule.Validate`
  执行期抛出的异常此前未做任何隔离，一路冒出 `LoadAll` 使调用方进程以 `0xE0434352`（未捕获托管
  异常标准终止码）崩溃退出、不产出任何报告（消费方两条独立复现路径——`item.budget_curve` 记录
  `item.budget.default` 的 `entries` 字段元素类型错配/整字段类型错配——经本仓库 `toolchain
  /validator` 实测复现，退出码均为 `-532462766`）。现改为逐条规则捕获枚举期异常，转成新检查名
  `rule_execution_failed`（Error 级，携带规则名/异常类型名/异常消息，`DataFieldException` 类型的
  异常额外带上其自带的表/记录/字段定位），异常前该规则已产出的问题保留、其余规则照常跑完，最终
  产出完整报告、退出码回到正常的"校验失败"语义（1），不再是 CLR 崩溃码。详见
  [消费方反馈-2026-09-19-编辑器-第73条.md](architecture/落地计划/消费方反馈-2026-09-19-编辑器-第73条.md)。

- **`SchemaMigrator.MigrateRow` 单行迁移执行期异常隔离（同源同构缺口根治，紧接上一条）**：
  第 73 条根治时扫出的同类未隔离点——`DataRegistry.LoadOneTablePartial` 逐行调用
  `SchemaMigrator.MigrateRow` 同样没有 try/catch，某个内容模块的迁移函数本身有缺陷时会同样击穿
  整个 `LoadAll`，当时判定"需要单独设计拍板"未修、只留档。现已处理：与"规则跑挂只是报告缺一部分
  结论"不同，迁移跑挂意味着这一行数据根本没能迁到目标 `schema_version`，处理方式是**整行剔除**
  （不是像 `rule_execution_failed` 那样记一条 issue 后仍照常收录）——新增检查名
  `row_migration_failed`（Error 级、阻断，消息含表名/记录定位/源目标 `schema_version`/异常类型名/
  异常消息，并点明"该行已剔除，下游 `reference_integrity` 等错误可能系连带产生"），该行不会以
  任何半迁移状态进入注册表，其余行/其余表照常处理。回归测试：`DataRegistryTests.
  LoadAll_RowMigrationThrowsUnexpectedException_DoesNotThrow_ReportsRowMigrationFailedAndExcludesRow`
  （已反向确认：去掉隔离后必然改为断言异常冒出，验证后已还原）；另用真实数据根
  （`data/_framework` + `data/_sample`）对 `found.migration_sample` 演示表临时注入迁移异常，
  实测 `toolchain/validator` 隔离生效前 `Unhandled exception` 崩溃、生效后正常退出码 `1`，验证后
  已还原全部临时注入。顺带排查同类未隔离点：`LoadAllCore`/`Reload` 内 `IDataSource.ListTables()`/
  `.Root` 调用同样没有 try/catch，但影响面更大（一次失败牵连全部数据根），且修法涉及多根合并语义
  拍板，本次不修、已留档（见 `core/foundation/data_registry/README.md`"单行迁移执行期异常隔离"
  一节）。

- **数据源枚举执行期异常隔离（框架调用外部实现不做隔离系列第三条，紧接上两条）**：上一条留档的
  同类缺口——`DataRegistry.LoadAllCore`/`Reload` 内对 `IDataSource.Root`/`IDataSource.ListTables`
  的调用没有 try/catch，某个自定义 `IDataSource` 实现枚举失败会击穿整个 `LoadAll`/`Reload`（影响
  面比"单行"/"单条规则"更大：一次失败牵连该数据源本应提供的**全部表**）。现已处理：新增
  `TryEnumerateSource` 把两次调用各自单独隔离，某个数据源失败即跳过（隔离粒度=单个数据源），其余
  数据源照常加载；新增检查名 `data_source_unavailable`（Error 级、阻断，`Table` 字段为该数据源标识
  ——能取到 `Root` 时用它，否则退化为类型名，不冒充任何具体表名，消息含数据源标识/异常类型名/异常
  消息，并点明"其余数据源照常加载"与"下游 `reference_integrity` 等错误可能系连带产生"）。**多根
  合并语义拍板**（上一条标注待设计层决定的事项）：失败的数据源如同从未出现在本次 `sources` 列表里
  一样，其贡献的表若也来自其它数据源则照常合并，若唯一来源就是它则该表本次不出现在 `Tables` 里（与
  "没有数据源提供该表"是同一种可观察状态，不伪造"表存在但缺内容"的记录，也不与"表本来就不存在"混为
  一谈——报告固定在数据源粒度而不是表粒度）；诚实说明"无法确定具体缺了哪些表"（枚举本身失败，框架
  没有独立途径倒推该数据源原本会提供哪些表名）。新增只读诊断 `IDataRegistry.IsDegraded`/
  `GetUnavailableSources()`（`DataSourceDiagnostic` 结构化快照，命名风格同既有
  `TolerantRegistryView.IsDegraded`/`MissingTables`，但落在 `DataRegistry` 本体、反映加载/重载期间
  的数据源级退化）——供 `IDataRegistryView.TryGetAll` 等绕开阻断直读的内容工具通道显式核对"当前是否
  有数据源被跳过"，不能仅凭"读到空/表不存在"就断定内容本身如此。回归测试：`DataRegistryTests.
  LoadAll_OneSourceListTablesThrowsUnexpectedException_DoesNotThrow_ReportsDataSourceUnavailableAndIsolatesOtherSources`/
  `LoadAll_OneSourceRootThrowsUnexpectedException_DoesNotThrow_ReportsDataSourceUnavailableWithTypeNameIdentifier`/
  `Reload_SourceStartsThrowingAfterInitialLoad_DoesNotThrow_ReportsDataSourceUnavailableAndOtherTableReloads`
  （已反向确认：去掉隔离后三例全部改为断言异常冒出，验证后已还原）；另用真实数据根（`data/_framework`
  + `data/_sample`）临时注入 `_sample` 根枚举异常，实测 `toolchain/validator` 隔离生效前
  `Unhandled exception` 崩溃（退出码 `-532462766`/`0xE0434352`）、生效后正常退出码 `1`（报告含预期的
  `data_source_unavailable`，`data/_framework` 的表照常加载、`data/_sample` 整批表缺席），验证后已
  还原全部临时注入。系列收尾扫描（`core/foundation/data_registry/README.md`"数据源枚举执行期异常
  隔离"一节列出全部调用点及隔离状态）额外发现一处物理位置在 `core/sim/` 的同类未隔离点
  （`AnchorTableSkillBudgetAnchorProvider.DataSourcesHaveAnchorRows` 在 `DataRegistry.LoadAll` 之前
  预扫描 `sim.anchor` 时同样直接调用 `IDataSource.ListTables()`），超出本次改动范围，留档建议另行
  派单。

- **锚点预扫描异常隔离（框架调用外部实现不做隔离系列第四条，紧接上一条留档条目）**：
  `AnchorTableSkillBudgetAnchorProvider.DataSourcesHaveAnchorRows`（`toolchain/validator
  /Program.cs`/`HeadlessWorldBuilder.Build` 两处接入点在 `DataRegistry.LoadAll` **之前**预扫描
  `sim.anchor` 是否含数据行）内部对 `IDataSource.ListTables()` 的调用同样没有 try/catch，某个数据源
  枚举失败会先于第三条已隔离的 `DataRegistry.LoadAllCore` 自身崩溃。本方法固定发生在有任何
  `ValidationReport`/issue 收集器之前，不能像 `TryEnumerateSource` 那样产出问题项——设计层拍板：
  捕获异常后**保守立即返回 `true`**（假定"可能含锚点行"，照常装配提供者），而不是当作"没有锚点行"
  返回 `false`（那会让 `SkillBudgetValidationRule`/`ItemGrantValueExceedsShareRule` 因
  `anchorProvider==null` 整条跳过、预算求解在未经确认的"锚点缺失"前提下悄悄运行且毫无提示，属于
  AGENTS.md 第 3 节禁止的静默降级）。安全性论证：两处调用方随后都会用同一份数据源列表调用
  `DataRegistry.LoadAll`，其内部既有的 `TryEnumerateSource`（第三条）会独立重新枚举同一个失败源并
  再次失败，产出 `data_source_unavailable`、令报告阻断，两处调用方均已对阻断报告做拒绝继续处理
  （`HeadlessWorldBuilder.Build` 抛 `InvalidOperationException`；`toolchain/validator` 按
  `IsBlocking` 返回退出码 1）——失败信息经既有隔离通道到达调用方/报告；即使误判为"过度保守"，
  `AnchorTable.MaxLevel<=0` 时 `GetAnchorDps`/`GetExpectedScalingStatValue` 首次调用即抛异常的既有
  防线也不会让错误数值悄悄产出。回归测试（`core/sim/tests/AnchorTableSkillBudgetAnchorProviderTests.cs`
  新增 3 例）：`DataSourcesHaveAnchorRows_SourceListTablesThrowsUnexpectedException_DoesNotThrow_
  ReturnsTrueNotFalse`/`DataSourcesHaveAnchorRows_GoodSourceWithoutAnchorThenThrowingSource_
  DoesNotThrow_ReturnsTrue`/`HeadlessWorldBuilderBuild_OneExtraSourceListTablesThrows_DoesNotCrash_
  ThrowsWithDataSourceUnavailableDetail`（已反向确认：去掉 try/catch 后三例全部改为失败，还原后
  7/7 通过）；真实数据根（`data/_framework` + `data/_sample`）临时注入 `_sample` 根首次枚举抛出，
  实测 `toolchain/validator` 隔离生效前 `Unhandled exception` 崩溃（退出码 `0xE0434352`）、生效后
  正常退出码 `0`（`errors 0, warnings 0`，`requires_anchor` 显示已接入），验证后已还原全部临时注入。
  **顺带同类修复**：`toolchain/simrunner/Program.cs` 内 `DiscoverBootstrapPlayerClass`（`Headless
  WorldBuilder.Build` 之前另一处探测 `sim.scenario` 的预扫描）同样直接调用 `IDataSource.ListTables()`
  /`DataTableSource.ReadText()`，均无 try/catch，一并根治——本工具可直接把失败打到标准错误，捕获后
  打印 `[警告]` 并跳过该候选继续扫描，不让它击穿整个探测流程；真正装载仍由随后的 `HeadlessWorldBuilder
  .Build` 完成，同上一条推理不会静默降级。**系统性收尾扫描扩到全仓**（`core/sim/README.md`"框架调用
  外部实现不做隔离系列第四条判断记录"一节"全仓外部调用点清单"）：确认 `IEventBus`/`IHookRegistry`
  的批处理回调分发、`ISaveMigration`（`SaveSystem`/`SettingsStore`）迁移链调用均早于本系列即已隔离；
  引擎适配层契约、约 20 个诊断旁路契约、约 40 个业务域默认实现契约按问题形状分类排除（诊断旁路类
  未逐个源码核实，已如实标注扫描深度），本系列四条覆盖的"数据/内容驱动型可插拔实现"范围内未发现
  其它未隔离点。判断记录详见 `core/sim/README.md`、`core/foundation/data_registry/README.md`
  "顺带发现"一节更新。

- **表现层诊断转发到引擎控制台——补上 Vfx/Sfx/Feedback/ViewBinder 四条链路（1.45.0 遗留缺口收口）**：
  1.45.0 只接了 `SpriteCharacterRig.Diagnostics` 一条链路，`VfxPlayer`/`SfxPlayer`/`FeedbackBinder`
  （含 `HitFrameSyncPolicy`，两者共享同一诊断实例）/`ViewBinder` 虽然都已接受可选构造参数
  `diagnostics`，却都没有公开出口能读到它，`presentation/assembly/PresentationAssembly` 装配时也
  从未对外暴露，adapters/unity 拿不到引用、无法转发。四个类型各自补上
  `public IPresentationDiagnostics Diagnostics { get; }`（ABI 只新增只读属性），
  `PresentationAssembly` 新增 `VfxDiagnostics`/`SfxDiagnostics` 转发属性（`Feedback`/`ViewBinder`
  本身是具体类型，直接读其 `.Diagnostics`）。设计取舍（判断记录见
  `presentation/assembly/README.md` 判断记录 10）：四条链路**各自独立** `PresentationDiagnosticsRecorder`
  （不共享实例，保留"某子系统的 `Warnings` 只含自己产生的消息"这一属性），本装配根不改变对这四个
  类型的构造调用。适配层新增 `PresentationAssemblyDiagnosticsForwarder`
  （`Runtime/Presentation/PresentationDiagnosticsConsoleForwarding.cs`）接住这四个来源，复用既有
  `PresentationDiagnosticsConsoleGate`/`SpriteRigDiagnosticsPump`，持有一个独立于
  `UnityViewFactory._diagnosticsGate` 的另一个 gate 实例（与 rig 消息量互不挤占）；级别映射/去重/
  开关语义与既有 `SpriteCharacterRig` 链路一致（恒 Warning，不用 Error）。三处生产装配入口
  （`GameFoundationBootstrap`/`FrameworkResidentHost`/`games/_template` `GameBootstrap`）的
  `AdvanceCharacterRigs` 均已接入（紧跟 `ViewFactory.PumpDiagnostics()` 之后调用
  `_presentationDiagnosticsForwarder?.Pump()`），
  `toolchain/tests/test_diagnostics_forwarding_advance_character_rigs_wiring.py` 新增第二组
  参数化断言同步守住这一行。新增测试：`VfxPlayerTests`/`SfxPlayerTests`/`FeedbackBinderTests`/
  `ViewBinderTests` 各自的 `Diagnostics_*` 用例（`dotnet test` 侧覆盖新属性）、
  `PresentationAssemblyTests.VfxSfxFeedbackViewBinderDiagnostics_AreExposed_AndEachIndependent`
  （覆盖转发属性接线与"四份互不相同"设计取舍）、
  `adapters/unity/DiagnosticsForwarding/tests/PresentationDiagnosticsConsoleForwardingTests.cs`
  新增 `PresentationAssemblyDiagnosticsForwarderTests`（7 例）。均已对"四类型 `Diagnostics` 属性
  返回错误实例""`PresentationAssembly` 转发属性接错实例""轮询循环漏掉一个来源""三处生产入口漏接
  `Pump()` 调用"四类改动分别做过反向确认（临时破坏、确认对应测试必然失败、已还原）。

- **表现层诊断转发到引擎控制台——补上第五条链路 `CompositeFeedbackSink`（上一批扫漏，第三批
  收口）**：上一批只覆盖了 `VfxPlayer`/`SfxPlayer`/`FeedbackBinder`（含 `HitFrameSyncPolicy`）/
  `ViewBinder` 四条链路，`presentation/assembly` 第 3 步构造的
  `Presentation.FeedbackBinder.Core.CompositeFeedbackSink`（`play_vfx`/`play_sfx` 找不到可用
  坐标/锚点时记警告）同样持有一份独立诊断、此前既无公开出口也未被装配根转发——是与上一批完全
  同类的遗漏。`CompositeFeedbackSink` 新增 `public IPresentationDiagnostics Diagnostics { get; }`
  （ABI 只新增只读属性），`PresentationAssembly` 新增 `FeedbackSinkDiagnostics` 转发属性（同
  `VfxDiagnostics`/`SfxDiagnostics` 惯例，不改变构造期不传 `diagnostics` 参数的既有行为）。
  `PresentationAssemblyDiagnosticsForwarder` 新增一个五源构造函数重载接住第五个来源（不改动既有
  四源构造函数签名，ABI 只新增重载，旧签名内部委托给新重载并传 `null`），三处生产装配入口
  （`GameFoundationBootstrap`/`FrameworkResidentHost`/`games/_template` `GameBootstrap`）的构造
  调用同步补上 `presentation.FeedbackSinkDiagnostics` 实参，
  `toolchain/tests/test_diagnostics_forwarding_advance_character_rigs_wiring.py` 新增一组参数化
  断言同步守住三处构造调用都传入了这个实参。新增测试：`CompositeFeedbackSinkTests.Diagnostics_*`
  （2 例）、`PresentationAssemblyTests.FeedbackSinkDiagnostics_IsExposed_SameInstanceAsCompositeFeedbackSink_AndIndependentOfOtherFour`、
  `adapters/unity/DiagnosticsForwarding/tests/PresentationDiagnosticsConsoleForwardingTests.cs`
  新增 5 例 `FiveSourceOverload_*`/`FourSourceConstructor_StillWorks_*`。均已对"轮询循环漏掉第五
  个来源""`PresentationAssembly` 转发属性接错实例""三处生产入口漏传第五个实参"三类改动分别做过
  反向确认（临时破坏、确认对应测试必然失败、已还原）。逐一复核仓库内全部
  `Presentation.VfxSfx.Contracts.IPresentationDiagnostics` 持有者后确认没有第六条未覆盖链路，见
  `presentation/assembly/README.md` 判断记录 10b"扫描结论"。

- **诊断契约统一转发到引擎控制台（[ADR-0042](architecture/adr/0042-诊断契约统一转发到宿主控制台.md)，
  全仓收口第四批）**：此前三批只覆盖了表现层 `IPresentationDiagnostics` 一套契约（六条链路）；
  本批对全仓做了一次完整普查，另发现 20+ 个同惯例的 `I*Diagnostics` 契约（`event_bus`/
  `hook_registry`/`save_system`/`scene_router`/`app_lifecycle`/`input_map`/`localization` 等
  L0 基础模块，`area_trigger`/`dialog`/`spawn`/`death`/`common`（奖励）/`world_state` 等 L4
  玩法宿主，`power_set`/`progression` 等数值模块，`combat`/`skill` 等规则模块，`presentation/ui`
  独立契约）同样只记内存、从未转发到引擎控制台。确立架构结论：框架内所有诊断契约产生的信息都
  必须有到达宿主控制台的通路，并新增一套与具体诊断契约解耦的统一转发机制——
  `Adapter.Unity.Diagnostics.DiagnosticsHub`（注册制轮询集线器，登记"来源名 + 只增不减的消息
  列表引用"，每帧轮询新增消息，复用既有 `PresentationDiagnosticsConsoleGate`/
  `IPresentationDiagnosticsConsoleSink` 的去重/容量上限/开关语义，恒映射为控制台 Warning、
  Error 级另加 `[error]` 文本标记不改变实际级别，消息统一带来源名前缀避免跨模块去重误撞）与
  `ProjectedReadOnlyList<TSource>`（把非字符串消息列表惰性投影为 `IReadOnlyList<string>`，供
  消息记录类型不统一的契约复用同一登记入口）。上述全部 21 个可达契约的宿主类型新增
  `public I<X>Diagnostics Diagnostics { get; }` 只读属性（ABI 只新增属性，不改构造签名），
  `GameplayAssembly`/`RulesAssembly`/`PresentationAssembly` 新增对应转发属性；三处生产装配入口
  （`GameFoundationBootstrap`/`FrameworkResidentHost`/`games/_template` `GameBootstrap`）新增
  `DiagnosticsHubComposition.RegisterCoreSources(...)` 统一登记调用，`AdvanceCharacterRigs`
  内紧跟既有转发调用之后新增 `_coreDiagnosticsHub?.Pump()`。结构性排除：`IExprDiagnostics`
  未接入——其绝大多数使用点是单次调用内临时构造、调用结束即丢弃的实例，没有可跨帧持续读取的
  稳定消息列表，不适用本轮的轮询式机制，需要另一种"异常分支处直接同步转发"的机制，留待后续
  单独立项（详细清单与逐项理由见 `adapters/unity/Packages/com.gamefoundation.adapter.unity/
  Runtime/Diagnostics/DiagnosticsHubComposition.cs` 判断记录）。新增测试：
  `adapters/unity/DiagnosticsForwarding/tests/DiagnosticsHubTests.cs`
  （`DiagnosticsHubTests` 8 例 + `ProjectedReadOnlyListTests` 3 例），
  `toolchain/tests/test_diagnostics_forwarding_advance_character_rigs_wiring.py` 新增两组
  参数化断言守住三处生产入口的登记调用与 `Pump()` 调用。已对"转发逻辑本身失效"
  "某一生产入口漏调用 `Pump()`"两类改动分别做过反向确认（临时破坏、确认对应测试必然失败、
  已还原）。24 个模块 README 补充判断记录，详见各自"判断记录（诊断契约统一转发机制，
  2026-09-19，architecture/adr/0042-诊断契约统一转发到宿主控制台.md）"一节。

- **ABI 破坏修正：`IDataRegistry.IsDegraded`/`GetUnavailableSources` 补默认接口实现（数据源枚举
  执行期异常隔离单遗留问题）**：这两个成员发布时是不带默认实现的抽象接口成员，`toolchain/
  abi_probe.ps1` 正确报出 `interface_new_abstract_member` 破坏（`breaks=2`）——AGENTS.md 第 3 节
  "ABI 只新增：……默认接口成员……"这条规则本身不区分"仓库内是否已知有第三方实现"，当时的判断记录
  "全仓库唯一实现完整 `IDataRegistry` 的类型是 `DataRegistry` 本身，新增本成员不破坏任何第三方
  实现"不成立。现补默认实现：`IsDegraded => false`、`GetUnavailableSources()` 默认返回空集合
  （语义"未退化/无不可用数据源"，与本接口既有的 `GetOverrideDiagnostics`/
  `GetReferenceDeclarations` 默认值风格一致）；`DataRegistry` 本体保留自己的真实覆盖，不受影响。
  全仓调用点扫描确认现有读取方均经具体 `DataRegistry` 实例访问，未发现依赖裸接口引用读取这两个
  成员的调用点，补默认值不影响任何既有行为。新增测试：`core/foundation/data_registry/tests/
  DataRegistryTests.cs` 的 `MinimalDataRegistry`（故意不覆盖这两个成员的测试替身）+
  `IsDegraded_DefaultInterfaceImplementation_ReturnsFalse`/
  `GetUnavailableSources_DefaultInterfaceImplementation_ReturnsEmpty`/
  `MinimalDataRegistry_CompilesWithoutOverridingDegradedMembers_ProvingDefaultInterfaceImplementationExists`。
  反向确认：改回不带默认实现的纯抽象成员，`Tests.Foundation.csproj` 编译报 `CS0535`，已验证并
  还原。重跑 ABI 探针：`breaks=0 allowed=0 additions=46 RESULT=OK`（基线
  `dist/ws-game-1.45.0.zip`）。详见 `core/foundation/data_registry/README.md` 判断记录。

- **Unity 编译错误修正：`DiagnosticsHubComposition.cs` 的 `presentation/ui` 契约接线 CS0234
  （ADR-0042 诊断契约统一转发单未跑 Unity 暴露的遗留问题）**：`presentation.UiDiagnostics is
  Presentation.Ui.InMemoryUiDiagnostics uiDiag` 这一模式匹配在 Unity 批处理编译时报
  `error CS0234: The type or namespace name 'Ui' does not exist in the namespace
  'Adapter.Unity.Presentation'`——`DiagnosticsHubComposition.cs` 所在命名空间
  `Adapter.Unity.Diagnostics` 与同一 asmdef 内已存在的兄弟命名空间 `Adapter.Unity.Presentation`
  （`Runtime/Presentation/` 下 `AnimClipResolver` 等）同前缀冲突，编译器把未限定的 `Presentation`
  标识符解析到了后者、再在其下找 `Ui` 子命名空间失败报错，不会回退到全局的 `Presentation.Ui`
  （`presentation/ui` 模块真正所在的命名空间，已编译进 `Presentation.Common.dll`，
  `Adapter.Unity.asmdef` 的 `precompiledReferences` 已含此程序集，不是缺引用）。不是"接线漏了"，
  是引用点未限定命名空间作用域。**修法**：类型引用加 `global::` 前缀
  （`global::Presentation.Ui.InMemoryUiDiagnostics`）——本程序集内 `GameFoundationBootstrap.cs`/
  `FrameworkResidentHost.cs` 已有同名冲突场景下用 `global::Presentation.Xxx` 的既有先例，沿用
  同一惯例。本次任务新增的 xUnit 测试文件 `PresentationAssemblyTests.cs`（命名空间
  `Tests.Presentation.Assembly`）首次编译时独立复现了同一个 CS0234，证实这是命名空间冲突的通性
  问题。新增/新增测试：`toolchain/tests/test_diagnostics_forwarding_advance_character_rigs_
  wiring.py` 新增
  `test_diagnostics_hub_composition_registers_presentation_ui_source`/
  `test_diagnostics_hub_composition_ui_diagnostics_type_pattern_uses_global_qualifier`（静态
  解析源码文本确认接线仍存在、且确实带 `global::` 前缀）；
  `presentation/assembly/tests/PresentationAssemblyTests.cs` 新增
  `UiDiagnostics_ProductionWiring_IsInMemoryUiDiagnostics`/
  `UiDiagnostics_AfterUnknownPathQuery_RecordsWarning_ObservableThroughDiagnosticsProperty`
  （纯 C# 侧确认生产装配下 `presentation.UiDiagnostics` 确实是 `InMemoryUiDiagnostics`、且真实
  查询会产生可观察警告）。反向确认：去掉 `global::` 前缀，新增的 python 测试失败，已验证并还原。
  `DiagnosticsHubComposition.cs` 本身是 Unity-only 胶水、不进 `dotnet test` 编译范围，本次未起
  Unity，编译正确性靠静态命名空间分析 + asmdef 引用关系核对自证，待主会话跑真实 Unity 批处理
  门禁复核。详见 `presentation/assembly/README.md` 判断记录（诊断契约统一转发机制）跟进段。

- **规范化 `Runtime/Diagnostics.meta` 为 Unity 完整形式**：上一条 Unity meta 完整性检查修复
  （`90691cc3`）落盘的目录级 `Runtime/Diagnostics.meta` 是当时磁盘上的两行精简版，随后一次
  Unity 导入将其展开为仓库惯例的完整形式（`folderAsset: yes` + `DefaultImporter` 块，GUID
  不变）；不收进来的话每跑一次 Unity 工作树就会因此变脏，干扰发版时的 `-dirty` 核对，提交
  `c744cf61`。

## [1.45.0] - 2026-09-19

本版落地消费方反馈第 67～72 条（ADR-0040 运行期宿主命令行能力契约；常驻运行入口、参数化内容
同步入口、内容根覆盖、Development Build 开关；第 67/71 条为文档澄清与反问），并随本分支一并
落地表现层诊断转发到引擎控制台、资源加载完成回填缺陷根治、`build.ps1` 构建产物陈旧检测、资产
导入数值字面量形式漂移修复。完整门禁结论与本版暴露的质量问题披露见下方"验证结论"/"本版暴露的
质量问题"两段。

[ADR-0040](architecture/adr/0040-运行期宿主命令行能力契约.md)（2026-09-19，已拍板并落地）：
运行期宿主的命令行能力契约——新增一个"进入并保持可交互运行状态"的长驻运行入口（第 68 条）与一个
"任意源内容目录 → 任意目标运行期工程"的参数化内容同步入口（第 69 条），均为面向任意外部工具的
通用命令行能力，不是面向某个具体消费方的专属联调协议；参数化同步入口与既有两个同步入口（私服包
内容落地、框架自身工作台同步）并列新增，不取代、不包装。**两项能力本次随本分支一并落地**（决策
2/3，详见下方"新增"小节）；决策 5 另补了命令行开关命名约定（运行期宿主自身解析的开关走短横线
全小写族，批处理构建工具解析的开关走驼峰族，禁止跨类别近似名）。消费方反馈第 67～72 条统一处理
记录见
[消费方反馈-2026-09-19-编辑器-第67-72条.md](architecture/落地计划/消费方反馈-2026-09-19-编辑器-第67-72条.md)。

本版还随本分支一并落地三项非反馈单驱动的修复/增强：表现层诊断（`IPresentationDiagnostics`）
转发到引擎控制台（含模板侧补线，详见下方"新增"小节）；纸娃娃层/方向档位资源加载完成后未回填
已渲染画面的结构性缺陷根治，以及 `build.ps1` 新增构建产物陈旧检测 `Test-CoreAssemblyDllStale`
（详见下方"修复"小节）；资产导入写盘层数值字面量形式漂移修复，根治 `world.map` 等表重复导入
产生无意义 diff、破坏幂等性的问题（详见下方"修复"小节）。

**验证结论**：完整 `check.ps1` 全部 30 步通过（总用时 442.1s）——Unity 编译 PASS、EditMode
86/86、PlayMode 293/293、独立版连续/离散两种冒烟 PASS、消费方演练 PASS。

**本版暴露的质量问题（如实披露）**：本轮所有执行 agent 均被要求不跑 Unity 以省 reimport 时间，
导致三个同源问题直到发版前完整门禁才暴露——① `GameBootstrap.RuntimeOptions` 跨程序集不可见致
Unity 编译失败；② 第 68 条两条新增 PlayMode 用例覆盖根只放探针表、拖垮世界装配，原设计不成立；
③ 第 70/72 条新增的 6 个 `.cs` 漏提交对应 `.meta`（本仓库惯例随源文件一起提交 `.meta`，模板下
已跟踪 62 个，漏提交会让消费方克隆后 GUID 重新生成）。三项均已在本版内修复，详见下方"修复"小节，
完整门禁复核通过。

### 新增

- **第 68 条**：`games/_template/Runtime/ResidentRunner.cs`（`-gf-resident`）——长驻可交互运行
  入口，与既有 `-gf-smoke-template`（跑完即退）并列，不取代。进入可交互状态（主菜单可见）后写
  就绪文件（`-gf-ready-file`，默认 `<persistentDataPath>/gf_resident_ready.txt`）并保持运行，
  轮询停止文件（`-gf-stop-file`，默认同目录 `gf_resident_stop.txt`）实现非强杀受控退出；退出码
  复用 `-gf-smoke-template` 既有分级（`0` 成功/`2` 失败，不新造语义）。新增 `-gf-dataset-root`
  覆盖游戏数据根（`GameBootstrap.GameDatasetRootOverride`，与 `sync_content.ps1` 的目标参数面
  对齐）。**判断记录（命名）**：落地时初名把"数据集根"错写成了"内容根"（字面上与"内容根"更接近
  的原先旧名），与第 70 条另一个并行开发、语义完全不同的开关 `-gfContentRoot`（物理内容根，用于
  热重载监视）拼写几乎相同，是两条分支并行开发的偶然撞车；已在本分支改名为语义更贴切的
  `-gf-dataset-root` 消除歧义，`-gfContentRoot` 不受影响，命名约定详见 ADR-0040 决策 5。新增
  `games/_template/Tests/Runtime/GameTemplateResidentTests.cs`（编辑器内验证就绪信号/受控退出
  链路，以及 `ResidentRunner_DatasetRootOverride_LoadsProbeTable_FromOverrideRootOnly`/
  `..._Absent_DefaultRootNeverSeesOverrideProbeTable` 一对用例——把此前"覆盖值=默认值，通过与
  失败无法区分"的假测试替换为写入只存在于覆盖根的探针表、断言只有传了覆盖参数才能读到的有区分
  力用例）；另在纯 .NET 侧新增 `core/foundation/data_registry/tests/
  GameDatasetRootOverrideEquivalenceTests.cs`（3 例，镜像同一段选根逻辑，已做反向确认：临时让
  解析恒返回默认值，1 例按预期失败）。真实独立版下的进程退出码/长驻行为，以及上述 Unity
  PlayMode 用例的反向确认（去掉覆盖应让断言失败），仍**待整合时统一跑引擎门禁确认**，不预先
  宣称通过；见 `games/_template/README.md`"长驻可交互运行"一节。
- **第 69 条**：`toolchain/sync_content.ps1`——通用参数化内容同步入口，接受任意
  `-SourceDir`/`-TargetDir`、`-OverridePolicy`（`Additive`/`Mirror`）、`-DryRun`，与既有
  `sync_package_content.ps1`/`build.ps1 -SyncContent` 并列，不取代、不包装（`toolchain/README.md`
  新增三入口对照表说明各自适用场景）。新增 `toolchain/tests/test_sync_content.py`（9 例：参数
  校验、Additive/Mirror 两态、干跑、幂等性）。
- **第 70 条**：`games/_template/Runtime/ContentSourceRootOverride.cs` 新增——命令行参数
  `-gfContentRoot <路径>` 或环境变量 `GF_CONTENT_ROOT` 可以把 `GameBootstrap` 的内容根整体指向
  外部内容源目录，数据加载与 `Runtime/DataHotReload.cs` 监视都会改用该目录，不再局限于
  StreamingAssets 部署副本。未指定时行为与之前完全一致；指定了不存在的目录会让装配显式失败
  （不静默回退）。`games/_template/README.md`"开发期数据热重载"一节同步补充说明。
- **第 72 条**：`games/_template/Editor/WindowsPlayerBuilder.cs` 新增自定义 `-executeMethod`
  构建入口，暴露 `-gfDevelopmentBuild` 命令行标志，批处理构建独立版时可选勾选
  `BuildOptions.Development`（内置 `-buildWindows64Player` 开关本身不支持）；
  `toolchain/consumer_smoke.ps1` 新增 `-DevelopmentBuild` 开关贯通到该入口，默认不传时构建行为与
  之前完全一致。`games/_template/README.md` 新增"构建独立版"一节说明两种命令行用法。
  独立验收发现该条落地后全分支零自动化测试，补测试单据此把参数解析纯逻辑抽到新增
  `games/_template/Editor/WindowsPlayerBuilderArgs.cs`（`WindowsPlayerBuilder.BuildWindows64Player`
  公开签名与构建行为不变），新增 `games/_template/Tests/Editor/WindowsPlayerBuilderArgsTests.cs`
  （8 个 EditMode 单测，覆盖 Development 标志存在性判定、输出路径命令行/环境变量优先级、两者都未
  指定时的显式报错），`Game.Template.EditorTests.asmdef` 新增对 `Game.Template.Editor` 的引用。
- **表现层诊断转发到引擎控制台**：`Presentation.VfxSfx.Contracts.IPresentationDiagnostics` 此前
  只记进内存，从不外发（真实游戏缺资源时控制台一行输出都没有，是本次 PlayMode 排查的观测盲区）。
  改动只落在 `adapters/unity/`：新增
  `Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/PresentationDiagnosticsConsoleForwarding.cs`
  （纯逻辑，不引用 UnityEngine——`PresentationDiagnosticsConsoleGate` 去重 + LRU 上限淘汰（默认
  500 条）+ 开关，`PresentationDiagnosticsConsoleForwarder`/`SpriteRigDiagnosticsPump` 两种接入
  方式）与薄胶水 `UnityPresentationDiagnosticsConsoleSink.cs`（`Debug.LogWarning`，恒为控制台
  Warning 级，不使用 Error 级——`IPresentationDiagnostics` 契约目前只有 Warn 一个级别，也是硬约束
  要求"避免 Unity Test Framework 因未预期 LogError 误判 PlayMode 用例失败"的落实）。
  `UnityViewFactory` 新增 `PumpDiagnostics()`/`DiagnosticsConsoleForwardingEnabled`
  （默认开启，可运行期关闭），接入现有 `GameFoundationBootstrap`/`FrameworkResidentHost` 的
  `AdvanceCharacterRigs` 每帧维护步骤，转发 `SpriteCharacterRig.Diagnostics` 新增的警告（对应
  09 表现层"资源加载失败保留占位方块"场景）。新增 `adapters/unity/DiagnosticsForwarding/`
  独立 dotnet 项目（跨目录引用同一份源文件，不复制）+ 13 例 xUnit 单测，均已用临时破坏转发/去重/
  开关逻辑做过反向确认（确认对应测试必然失败）后还原。**待设计层确认**：`VfxPlayer`/`SfxPlayer`/
  `CompositeFeedbackSink`/`FeedbackBinder`/`ViewBinder`/`HitFrameSyncPolicy` 各自的
  `IPresentationDiagnostics` 由 `presentation/assembly/PresentationAssembly` 内部各自默认构造、
  未对外暴露注入点，adapters/unity 暂无法转发，需要 presentation/assembly 新增可选注入点才能覆盖，
  超出本次"只落在引擎适配层"的范围；**待主会话跑 Unity 引擎门禁确认**：`UnityPresentationDiagnosticsConsoleSink`
  与三处生产接线点引用 UnityEngine，无法用 `dotnet test` 验证，只能在真实 Unity 批处理测试里确认。
- **表现层诊断转发到引擎控制台——补齐游戏模板侧接线（上一批遗留缺口收口）**：上一批只接了
  `adapters/unity/` 里 `GameFoundationBootstrap`/`FrameworkResidentHost` 两处
  `AdvanceCharacterRigs`，漏了 `games/_template/Runtime/GameBootstrap.cs` 的同款方法——按模板起的
  真实游戏（该类型正是模板的生产入口）此前从未获得这份转发，是三处近似重复代码"改了两处、漏了
  第三处"的具体案例，比两个工作台宿主类型本身更该覆盖。已按既有写法补上同一行
  `ViewFactory.PumpDiagnostics()`，开关语义（`DiagnosticsConsoleForwardingEnabled` 默认开启）不变。
  新增 `toolchain/tests/test_diagnostics_forwarding_advance_character_rigs_wiring.py`（3 例，按
  文件参数化）：花括号配对提取三处 `AdvanceCharacterRigs()` 方法体全文，断言均含
  `ViewFactory.PumpDiagnostics()` 调用——这段接线本身是 Unity 胶水，无法在不起 Unity 的前提下用
  `dotnet test` 验证运行期行为，但"三处近似重复方法是否保持同步"这件事本身可以脱离 Unity 用纯文本
  解析验证，把这类回归当场拦下。已做反向确认：临时删掉模板侧那一行，仅
  `games/_template/Runtime/GameBootstrap.cs` 对应用例按预期失败（另两处仍通过），报出的失败信息
  准确指向缺失调用的文件路径；确认后已还原。**仍待主会话跑 Unity 引擎门禁确认**：
  `UnityViewFactory.PumpDiagnostics` 是否真的经 `Debug.LogWarning` 写入控制台，本次新增测试只保证
  三处调用点存在且一致，不替代真实 Unity 批处理验证。`VfxPlayer`/`SfxPlayer`/`FeedbackBinder`/
  `ViewBinder`/`HitFrameSyncPolicy` 的注入点缺口仍待设计层决定，本批未处理。

### 文档

- 消费方反馈第 67 条（部分失实，补文档指引）：主 zip 不含 `data/_sample`/`assets/_sample` 属既有
  设计（自测数据不随分发产物），但样例数据获取通道（`ws-game-<ver>-samples.zip` +
  `get_framework.ps1 -WithSamples`，1.15.0 起已具备）此前只在根 `README.md`/`toolchain/README.md`
  写明，`games/_template/README.md` 未提及；补齐"获取验收样例数据集"小节，并补一条此前未文档化的
  限制——`-WithSamples` 只对 zip 通道有效，纯私服（`-FromRegistry`）通道消费框架的游戏没有等价的
  样例数据获取路径。
- 消费方反馈第 71 条（部分失实/需澄清，补稳定性声明）：`ValidationIssue.ToString()` 确无稳定性
  承诺，但结构化属性与两条命令行 `--json` 入口（`toolchain/validator`、
  `toolchain/asset_import/check_cmd.py check`）早已存在；`ToString()` 新增 XML 文档注释明确"人类
  可读、不承诺格式稳定、程序消费请改读结构化属性/CLI --json"的立场，`core/foundation/data_registry/
  README.md` 新增对应说明章节。核实确认运行期（Unity 进程内）唯二输出校验问题的两处
  （`GameBootstrap.cs:230`、`DataHotReload.cs:386-388`）仍只写 `ToString()` 文本、事件总线
  `DataValidationFailedEvent` 不携带结构化问题列表——评估两种补结构化出口的方案后认为均需要新的
  对外契约承诺，未实现，反问草稿已并入下方统一回复文档。
- 新增消费方反馈第 67～72 条统一回复文档
  `architecture/落地计划/消费方反馈-2026-09-19-编辑器-第67-72条.md`：合并
  `design/0040-tooling-contract`（ADR-0040）、`feat/68-69-tooling-entries`（第 68、69 条）、
  `feat/70-72-tooling`/`feat/72-build-tests`（第 70、72 条及补测试）、`feat/67-71-contract-docs`
  （第 67、71 条）四条源分支的处理记录，并如实记录了独立验收发现的问题及后续处理结果——四条
  源分支各自独立开发时互不可见，其中两条源分支合并时确认存在真实缺口：第 68 条数据集根覆盖开关
  当时仍用着原先的旧名（改名建议尚未落地）、"数据集根覆盖生效"测试当时仍是无区分力的假测试；
  **两项缺口均已在整合阶段修复**（改名为 `-gf-dataset-root`，测试重做为有区分力的用例，见上方
  "第 68 条"改动说明），文档正文与"待设计层确认"清单已同步更新为已处理状态。71 号
  反问草稿文件随本次提交删除（内容已并入统一回复文档）。

### 修复

- 根治两处 PlayMode 用例隐性执行顺序依赖（`GreyBoxTests.
  Move_Right_IncreasesPlayerX_AndTurnsSideways`/`VerticalSliceTests.
  Paperdoll_LayerOrder_MatchesDisplayMapDeclaredOrder`）：两者断言依赖纸娃娃层/方向档位精灵
  资源已经异步加载完成，但用例自身既不触发加载也不等待，全量门禁里靠同批处理进程里更早跑过的
  其它用例把资源预热进跨用例共享的 `UnityResourceLoader` 缓存才侥幸通过，单独用 `-testFilter`
  跑二者均失败（各 total=1 passed=0 failed=1）。改为轮询等待资源真正加载完成再断言，超时给出
  说明等待对象与时长的失败信息，不放宽/不删除原有断言。已扫查全部 37 个 PlayMode/EditMode 测试
  文件中涉及资源加载状态查询的用例，未发现其它同类隐性顺序依赖（其余用例均已用私有
  `UnityResourceLoader` 实例、专属资源引用字面量或已有轮询等待，详见
  `architecture/落地计划/排查复盘-2026-09-19-PlayMode-全局缓存清理反例.md`"教训三/正面做法"
  两节与包 README"PlayMode 用例写法约定"一节）。
- **纸娃娃层/方向档位资源异步加载完成后未回填已渲染画面**（诊断记录
  `architecture/落地计划/排查复盘-2026-09-19-PlayMode-全局缓存清理反例.md`"教训四"）：上面那条
  "改轮询等待"本身没有错，但等多久都通不过——真因是
  `Presentation.Common.ResourceReferenceTracker.EnsureLoading` 此前固定给
  `IResourceLoader.LoadAsync` 传空操作完成回调，`UnityRenderer2D.SetLayers` 只在调用当下同步
  解析一次精灵（未加载完成时落地占位方块），此后除非层集合/朝向再次变化，没有任何机制在资源
  真正加载完成后回头刷新已渲染的占位方块——隔离跑 PlayMode 用例证实这不是竞态，是结构性缺口：
  全量门禁能通过纯属"更早用例碰巧把同一资源预热进跨用例共享缓存"的假通过。新增
  `EnsureLoading(Id, ResourceKind, LoadCallback?)` 重载（ABI 只新增，原双参数重载保留不变）；
  `SpriteCharacterRig`/`SpriteViewBase` 把该回调统一接到 `SpriteCharacterRig.
  HandleResourceLoadCompleted`：加载成功时重新应用最近一次已知的完整层列表，让占位方块有机会被
  真实资源替换（幂等，多个层引用同一资源只触发一次重新应用）；加载失败时不重新应用，改记一条
  `IPresentationDiagnostics` 警告；已销毁的渲染实例（`SpriteCharacterRig.MarkDestroyed`）直接
  跳过迟到的回调，不触碰失效句柄。新增 12 例测试（`presentation/common/tests/
  ResourceReferenceTrackerTests.cs` 全新文件 6 例；`SpriteCharacterRigTests.cs`/
  `SpriteViewBaseTests.cs` 各新增覆盖成功回填/失败诊断/多层共享资源去重/销毁后不触发的用例）。
- **`build.ps1` 静默同步陈旧核心 DLL 到 Unity 适配层包**（诊断记录见
  `architecture/落地计划/排查复盘-2026-09-19-PlayMode-全局缓存清理反例.md`"教训五"）：DLL
  同步步骤固定读取默认构建输出路径（`<程序集目录>\bin\$Configuration\netstandard2.1\`），若按
  AGENTS.md §4 旧规则用 `--artifacts-path` 构建后再 `-SyncOnly`，同步的是默认路径下更早一次
  构建遗留的陈旧产物，此前只在源文件缺失时报错、陈旧但存在时无任何提示，曾造成连续三轮
  "修复看似无效"的假失败。新增 `Test-CoreAssemblyDllStale` 陈旧检测：比较每个核心 DLL 的
  修改时间与其对应源码目录下最新 `.cs` 文件（排除 `tests/`/`bin`/`obj`，与各 `Core.*.csproj`/
  `Presentation.Common.csproj` 的 `<Compile Remove>` 规则一致）的修改时间，DLL 落后超过 2 秒
  容差即直接报错终止，错误信息包含程序集名、陈旧秒数、最新的源文件路径与正确做法。AGENTS.md
  §4 同步补充了例外条款：涉及会编译进这六个 DLL 的改动、且要验证 Unity 侧行为时，必须用
  默认路径构建再 `-SyncOnly`，不带 `--artifacts-path`。
- **`core/foundation/data_registry/README.md` 一处失效相对链接**（`python -m pytest
  toolchain/tests -q` 既有失败用例，与本批诊断转发功能无关，主检出同样复现）：第 71 条反问的
  链接指向 `消费方反馈-2026-09-19-第67-72条回复草稿-71号反问.md`，该草稿文件已在提交 `d406af3`
  并入统一回复文档 `消费方反馈-2026-09-19-编辑器-第67-72条.md` 并删除，链接本身此后未同步更新。
  改指向合并后的统一回复文档（"待消费方答复"节"第 71 条反问（原文，供直接转发）"），并在正文
  注明原草稿文件已被删除与并入的提交，不是简单删链接掩盖问题。`python -m pytest
  toolchain/tests -q` 现全过。
- **`GameBootstrap.RuntimeOptions` 跨程序集不可见导致 Unity 编译检查失败**：第 70 条新增的
  `games/_template/Tests/Runtime/ContentSourceRootOverridePlayModeTests.cs` 读取
  `GameBootstrap.RuntimeOptions`（原声明为 `internal`），但该文件所在的 `Game.Template.Tests` 是
  独立于 `Game.Template` 的程序集，`internal` 跨程序集不可见，报
  `CS1061: 'GameBootstrap' does not contain a definition for 'RuntimeOptions'`——此前所有执行
  agent 均被要求不跑 Unity，这个漏网编译错误从未在真实引擎里编译过，一直未暴露。改为 `public`
  （照搬本模板 `Runtime/DataHotReload.cs`/`Runtime/TemplateSmokeRunner.cs` 已有的既有惯例：本模板
  会被复制改名为具体游戏，改名后程序集名跟着变化，硬编码旧程序集名的 `InternalsVisibleTo` 会失效，
  故不采用 `Adapter.Unity`（程序集名固定）那条 `InternalsVisibleTo` 路线）。已静态核查
  `games/_template/Tests/`、`adapters/unity` 测试程序集，未发现其它同类跨程序集引用 internal 成员
  却尚未编译暴露的隐患。详见 `games/_template/README.md` 对应判断记录。
- **`GameTemplateResidentTests.cs` 两条"数据根覆盖生效"用例二次修复（第 68 条测试真正跑通引擎门禁
  后暴露的两处独立问题）**：上一版把覆盖根设成只放探针表的空目录，实机一跑就把
  `GameBootstrap.Bootstrap()` 装配拖垮（`GameplayAssembly` 需要的 `stat.definition` 等表也在被
  覆盖的 `data/game` 树下，装配在 `StatHost` 处直接抛异常，被 `BootstrapFailed` 接住，走不到
  断言）；改为覆盖根 = 默认数据集 `data/game` 的完整运行期副本（跳过 `.meta` 避免 GUID 冲突）+
  叠加一张只存在于该副本的探针表。修完又暴露第二处问题：对照用例
  `ResidentRunner_DatasetRootOverride_Absent_DefaultRootNeverSeesOverrideProbeTable` 稳定失败
  （"Expected: False But was: True"）——根因是两条用例都误用了
  `IDataRegistryView.TryGet(string, string, out DataRecord)` 默认实现的语义（只要 registry 未
  阻断就恒返回 `true`，不代表记录真的存在），改用 `IDataRegistryView.Get(string, string)`
  （不阻断时表/记录不存在直接返回 `null`）配合 `Assert.IsNotNull`/`Assert.IsNull`。已实跑验证：
  `-testFilter` 单独跑这两条通过、全量 PlayMode（293 例）零回归；反向确认（临时让
  `GameDatasetRootOverride` 不生效）证实 `LoadsProbeTable_FromOverrideRootOnly` 按预期失败、
  `Absent_...` 仍通过（符合其"对照组"定位），已还原。详见
  `games/_template/README.md`/`Tests/Runtime/GameTemplateResidentTests.cs` 对应判断记录。
- **资产导入写盘层数值字面量形式漂移，破坏 `world.map` 等表的幂等性**：重跑
  `toolchain/import_assets.py map`（`import_sample_assets.py` 第 5 步）会给
  `data/_sample/world/world.map.json` 产生无意义 diff——`image_transform.pixels_per_unit` 从
  `32` 变成 `32.0`、`origin_px.x`/`y` 从 `0`/`1024` 变成 `0.0`/`1024.0`。根因：`map_cmd.py` 的
  `--pixels-per-unit`/`--origin-px` 声明为 `type=float`，经 `merge_write_row -> write_envelope
  -> _row_json` 按 Python 运行时类型直接 `json.dumps`，未做任何 int/float 字面量归一化；schema
  侧 `WorldMapSchema.cs` 这几个字段均为 `FieldKind.Number`（不区分 Int/Float），`32.0` 不违反
  校验，问题性质是字面量形式漂移而非校验问题，因此不改任何 C# 文件、不改 schema 声明、不出
  ADR。修法：新增 `toolchain/asset_import/common.py` 的 `_normalize_json_literals`，在
  `_row_json`（`merge_write_row`/`write_envelope` 唯一的 `json.dumps` 落点）序列化前把数值上是
  整数的 float 归一化为整数字面量，非整数 float 原样保留；map/sprite/vfx/sfx 四个子命令统一
  受益。`sprite_cmd.py`/`vfx_cmd.py` 的旁路写 JSON 路径（`atlas.json`/`anchors.json`/
  `frames.json`）因已提交数据里存在大量既有整数值 float 字面量（如 `"fps": 20.0`），规范化会
  改动这批已提交样例数据，按任务口径未纳入本次改动，留待设计层另行拍板。首批新增的 4 个"字节级
  幂等性"用例经验收反证实验证实无效（同一版本代码连跑两次天然字节一致，防不住"新写出的字面量
  形式与历史提交不一致"这个真实症状）；补一层通用文本扫描工具
  `find_int_valued_float_literals`/`assert_no_int_valued_float_literal_drift`（正则直接在原始
  文本上找漂移字面量，先掩蔽 JSON 字符串内容避免误判子串），配 map/sprite/vfx 三个子命令各自的
  显式整数值参数回归用例（已用反证实验确认修复前失败、修复后通过），原 4 个幂等性用例改名并
  订正 docstring（不删测试）。新增 `check.ps1`"样例导入幂等性门禁"步骤：重跑
  `import_sample_assets.py` 后断言 `data/_sample`/`assets/_sample` 零 git diff。`python -m
  pytest toolchain/tests -q` 全过（334 例，5 skipped）。
- **第 70/72 条新增源文件漏提交 Unity `.meta`**：`games/_template/Editor/
  WindowsPlayerBuilder.cs`/`WindowsPlayerBuilderArgs.cs`、`games/_template/Runtime/
  ContentSourceRootOverride.cs`、`games/_template/Tests/Editor/
  ContentSourceRootOverrideTests.cs`/`WindowsPlayerBuilderArgsTests.cs`、`games/_template/
  Tests/Runtime/ContentSourceRootOverridePlayModeTests.cs` 共 6 个 `.cs` 在
  `feat/70-72-tooling`/`feat/72-build-tests` 提交时未带对应 `.meta`——这些文件此前从未在真实
  引擎里导入过（执行 agent 一律被要求不跑 Unity），`.meta` 是本次完整门禁首次导入时才在本地
  生成的。本仓库惯例是 `.meta` 随源文件一起提交（模板下已跟踪 62 个），漏提交会让消费方克隆后
  由 Unity 重新生成 GUID，破坏跨仓库的资源引用稳定性。已补齐全部 6 个 `.meta` 并随本版一并
  提交。

## [1.44.0] - 2026-09-19

[ADR-0038](architecture/adr/0038-资源引用类别前缀唯一决定路径空间.md)/
[ADR-0039](architecture/adr/0039-内容数据schema破坏性变更政策.md) 落地——契约面/校验/工具链、
样例数据迁移、**引擎适配层接线**均已完成；sprite 型隐式接线改显式仍是后续任务（见下方
"未完成/后续任务"）。**样例数据迁移与引擎适配层接线两部分已于 2026-09-19 在真实引擎环境完成
验证**：`check.ps1`（不带 `-SkipUnity`）前台全量跑通，全部 29 步 PASS/SKIP，总用时 425s，含
Unity 编译检查 PASS（19.2s）、Unity EditMode PASS（70/70）、Unity PlayMode PASS（288/288，
含数据迁移批改动断言的 `EquipmentVisualReplayTests.cs`/`VerticalSliceTests.cs` 两处测试与适配层
接线批改动的 `UnityResourceLoader.cs` 配套测试）；过程中发现并根治了 3 处 PlayMode 用例失败
（均为测试侧问题，非产品缺陷，详见下方"修复"小节）。逐条验证结果见验证清单文档；其中"武器风格
动画剪辑真实加载"/"纸娃娃层装备覆盖真实加载"两项手工验证仍未覆盖（自动化用例只断言"发起了
加载请求"，不断言"确实加载成功"，二者不等价），如实标注，详见下方"引擎适配层接线"小节与
验证清单文档。**

### 破坏性变更

**本条目按 ADR-0039 决策 2 第 3 类"新增无条件 Error 级校验规则视同破坏性变更"、以及决策 2 第 2 类
"改资源引用取值约定"，均属破坏性变更；按决策 3 当前期政策，材料现已齐备——ADR、本节标注、下方
逐字段迁移说明、全部数据根同步迁移、消费方通知文档均已完成，见文末对照清单。**

- 资源引用类别前缀集合变化（ADR-0038 决策 2/3/4）：`anim` 前缀此后只表示 model 型引擎侧逻辑路径，
  不再承载 sprite 型语义；sprite 型动画帧资源改用新前缀 `sprite_anim`；`display.equip_visual.
  mesh_ref` 的 sprite 型取值改用新前缀 `paperdoll`（落地期核实确认与 `sprite_set_id` 语义不等价，
  见 ADR-0038 决策 4 附带条款、`architecture/14_资产规格书模板.md` 第 1.2 节新增表格）。

  **逐字段 before/after 迁移说明**（每一行的消费型均按实际消费路径逐行核实——`display.map` 对应
  行的 `kind`/`anim_set_ref` 显式引用/`weapon_style_ref` 挂接行的 `kind`，而不是望文生义；`sample_
  model_sword` 一行的判定与最初按"挂在 kind=sprite 行下"这一浅层线索得出的猜测**相反**，见下方
  说明）：

  | 表.字段（行 id） | 消费型判定依据 | 旧取值 | 新取值 |
  |---|---|---|---|
  | `display.anim_set.sample_hero.clips.{idle,move,attack,cast,hit,death}.resource_ref` | sprite 型：无 `anim_set_ref` 显式引用，按 `display.map.sample_hero`（`kind=sprite`）id 末段隐式接线（`UnityViewFactory.TryResolveAnimSet`） | `anim.sample_hero_{idle,move,attack,cast,hit,death}` | `sprite_anim.sample_hero_{idle,move,attack,cast,hit,death}`（共 6 个字段） |
  | `display.anim_set.placeholder_biped.clips.{idle,attack,cast,hit}.resource_ref` | model 型：被 `display.map.sample_model_hero`（`kind=model`）的 `anim_set_ref` 显式引用 | `anim.{idle,attack,cast,hit}` | 不变（保持 `anim.*`） |
  | `display.weapon_style.sample_sword.auto_attack_anim` | sprite 型：挂接行 `display.map.sample_blade`（`weapon_style_ref` 指向本行）`kind=sprite`；`adapters/unity/.../Tests/Runtime/WeaponClipRegistrationTests.cs` 用 `creature.sample_hero`（sprite 型）实测经 `UnityFrameAnimPlayer.Play` 落地 | `anim.sample_sword_swing` | `sprite_anim.sample_sword_swing` |
  | `display.weapon_style.sample_staff.auto_attack_anim` | sprite 型：同上机制实测（`WeaponClipRegistrationTests.Cast_WeaponStyleCastOverrideClip_NotPreRegistered_DoesNotThrow` 用 `creature.sample_hero` 实测） | `anim.sample_staff_jab` | `sprite_anim.sample_staff_jab` |
  | `display.weapon_style.sample_staff.cast_anim_override.skill.sample_fireball` | sprite 型：同上 | `anim.sample_staff_cast` | `sprite_anim.sample_staff_cast` |
  | `display.weapon_style.sample_model_sword.auto_attack_anim` | **model 型**（与初步线索"挂在 kind=sprite 行下"相反）：`adapters/unity/.../Tests/Runtime/ModelIntegrationTests.cs EquippedWeapon_AttackState_PlaysAutoAttackAnimOnRealAnimator` 用 `creature.sample_model_hero`（model 型）实测经 `IRenderer3D.PlayAnim` 驱动真实 Animator 进入 `attack` 状态；`display.equip_visual.sample_model_sword` 的 `mode: socket_attach`+`model_ref` 也只有 `ModelCharacterRig.ApplyEquipVisual` 消费（`SpriteCharacterRig` 无此方法） | `anim.attack` | **不变**（保持 `anim.attack`） |
  | `display.weapon_style.sample_model_sword.cast_anim_override.skill.sample_burn` | model 型：同上（未单独实测 cast 分支，但与 `auto_attack_anim` 同属一行、同一挂接关系，判定一致） | `anim.cast` | **不变**（保持 `anim.cast`） |
  | `display.equip_visual.sample_hero_hat.mesh_ref` | sprite 型：`presentation/render/core/SpriteViewBase.cs HandleItemEquipped`（195～207 行）对 `mode: slot_mesh` 行直接把 `mesh_ref` 当纸娃娃层资源 id 使用，不经 `SpriteSetDirectory`；语义与 `sprite_set_id`（目录）不等价（单文件 vs 目录），按 ADR-0038 决策 4 附带条款新增独立前缀 | `sprite.item.sample_hero_hat_test` | `paperdoll.item.sample_hero_hat_test` |
  | `display.equip_visual.sample_model_helmet.mesh_ref` | model 型：`mode: slot_mesh`，取值已是 `model.*`（`ModelLogicalPath` 路径空间），`ModelCharacterRig.ApplyEquipVisual` 消费 | `model.placeholder_biped` | 不变 |

  **受影响样例行数**：4 行发生了取值迁移（`display.anim_set.sample_hero`/`display.weapon_style.
  sample_sword`/`display.weapon_style.sample_staff`/`display.equip_visual.sample_hero_hat`），
  共 10 个字段值（6 + 1 + 2 + 1）；另有 4 行经核实确认消费型不受影响、无需迁移（`display.anim_set.
  placeholder_biped`、`display.weapon_style.sample_model_sword`、`display.equip_visual.
  sample_model_sword`、`display.equip_visual.sample_model_helmet`）。全仓排查确认没有"无法判定
  消费型"的行。

  **数据根同步情况（ADR-0039 决策 3 已满足）**：`data/_sample` 已完成上表迁移；`data/_framework`
  当前 `display/` 目录为空、`games/_template/data/game` 无 `display/` 目录、`core/sim/tests/
  data/display` 只有不含这四个字段的 `display.map.json`——均核对确认不持有受影响字段，不需要
  同步改动，如实记录该核对结论而非假设。

  **配套测试同步（2026-09-19 已在真实引擎环境重新验证，见下方"引擎适配层接线"小节"验证结果"与
  验证清单文档）**：`adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/
  EquipmentVisualReplayTests.cs`（`HatMeshRef` 常量）/`VerticalSliceTests.cs`（两处
  `resourceRef` 局部变量）直接硬编码了 `display.equip_visual.sample_hero_hat.mesh_ref`/
  `display.anim_set.sample_hero.clips.*.resource_ref` 的取值用于断言，随上表迁移同步改为新前缀；
  `AnimReplayAndFinishEndToEndTests.cs`/`UnityViewFactoryDefaultAnimationTests.cs` 也提到了这两个
  字段但只是判断记录注释文字更新，不涉及断言取值。真实引擎环境跑通后，`VerticalSliceTests.cs`
  两处用例的断言写法有调整（原 `LogAssert.Expect` 写法过期，改为正面断言，见下方"修复"小节），
  `EquipmentVisualReplayTests.cs` 断言本身未变、随 PlayMode 288/288 通过。
- 新增无条件校验规则视同破坏性变更（ADR-0039 决策 2 第 3 类）：`RefCategoryFieldRule`
  （检查名 `field_ref_category`）已从此前默认关闭的可选规则**转正为无条件注册**（与
  `DisplayKindFieldGroupRule`/`EquipVisualModeFieldGroupRule` 同等地位）——上一条数据迁移完成后，
  该规则唯一的默认关闭理由（`display.equip_visual.sample_hero_hat` 遗留 `sprite.*` 前缀会立刻
  报错）已消除；`ContentValidationOptions.EnableRefCategoryCheck`/`toolchain/validator
  --enable-ref-category-check` 一并删除。

### 新增

- `Core.Foundation.EngineAdapter.AssetRefConventions`/`toolchain/asset_import/ref_conventions.py`
  新增方法/函数：`SpriteAnimDir`/`sprite_anim_dir`（`sprite_anim.<name>` →
  `sprite_anim/<name>`，结构同 `vfx`）、`PaperdollLayerFile`/`paperdoll_layer_file`
  （`paperdoll.<...>` → `paperdoll/<...>.png`，单个扁平文件）。
- 新增框架契约面公开路由入口 `AssetRefConventions.ResolvePathSpace`/
  `ref_conventions.resolve_path_space`：输入资源引用标识，输出 `(路径空间, 相对路径)`；路径空间为
  显式枚举 `AssetRefPathSpace { AssetRootRelative, EngineLogicalPath }`（C#）/
  `AssetRefPathSpace(AssetRootRelative, EngineLogicalPath)`（Python `enum`）。按类别前缀分派到
  `KnownCategories`/`KNOWN_CATEGORIES` 登记的方法表（`sprite`/`icon`/`vfx`/`sfx`/`sprite_anim`/
  `paperdoll` → 资产根相对；`anim`/`model` → 引擎侧逻辑路径）；类别前缀缺失或不在集合内均报错
  （消息含收到的前缀与合法集合），不做静默兜底。两侧各自独立实现、互相不调用，靠同一组输入/
  期望字符串的对照测试捕获漂移（同 ADR-0037 决策 1）。
- `FieldSchema` 新增 `WithAllowedRefCategories(params string[] categories)`（`Id`/`IdList`
  种类专用，设置一次、`Kind` 不匹配或重复设置均抛异常，同既有 `With...` 系列惯例）；已在
  `display.anim_set.clips.<clip>.resource_ref`（`anim`/`sprite_anim`）、
  `display.weapon_style.auto_attack_anim`/`cast_anim_override.<value>`（同上）、
  `display.equip_visual.mesh_ref`（`model`/`paperdoll`）、`vfx.def.resource_ref`（仅 `vfx`）、
  `sfx.def.resource_ref`/`variants`（仅 `sfx`）六处登记。
- 新增校验规则 `Core.Foundation.DataRegistry.RefCategoryFieldRule`（检查名
  `field_ref_category`）：递归校验已登记 `WithAllowedRefCategories` 的字段，取值类别前缀必须
  合法且落在该字段允许子集内；只检查前缀合法性，不检查资源文件是否存在。**无条件注册**（见上方
  "破坏性变更"小节），与仓库其它 `*FieldGroupRule` 同等地位。
- `toolchain/asset_import/check_cmd.py` 新增检查域 `display_anim`（**已纳入默认 `DEFAULT_DOMAINS`**，
  省略 `--only` 时随其余四项一并跑）：核对 `display.anim_set.clips.resource_ref`/`display.
  weapon_style.auto_attack_anim`/`cast_anim_override`/`display.equip_visual.mesh_ref` 四个
  字段的资源存在性。新增 5 个检查名：`display_anim_ref_category_invalid`、
  `display_anim_sprite_anim_atlas_missing`、`display_anim_sprite_anim_frames_json_missing`、
  `display_anim_paperdoll_file_missing`、`display_anim_asset_missing`（遗留前缀兜底）。
- `assets/_sample/sprite_anim/`/`assets/_sample/paperdoll/` 新增占位资产：9 个 `sprite_anim/
  <name>/{atlas.png,frames.json}` 目录（`sample_hero_{idle,move,attack,cast,hit,death}`/
  `sample_sword_swing`/`sample_staff_{jab,cast}`）+ 2 个 `paperdoll/
  item_sample_hero_hat_test.png`/`item_sample_hero_hat_wiring_test.png` 扁平文件（后者为下方
  "修复"小节 `SpriteEquipVisualWiringTests` 专属夹具新增，2026-09-19 补）；结构与既有
  `assets/_sample/vfx/*` 样例资产同构，尺寸取最小（2x2 像素单帧），总计约 2.8KB（实测 2845
  字节）。

### 迁移说明

见上方"破坏性变更"小节的逐字段 before/after 表格；消费方通知文档：
[消费方通知-2026-09-19-资源引用类别前缀契约变更.md](architecture/落地计划/消费方通知-2026-09-19-资源引用类别前缀契约变更.md)。

### 引擎适配层接线（ADR-0038 决策 8 第二项，本批提前落地）

**判断记录（为何提前）**：ADR-0038 决策 8 原把这一项列为不在该决策范围的后续项——当时样例数据
尚未迁移，`UnityResourceLoader` 既有实现（`ResolveEffectDir` 固定拼 `vfx` 子目录、`ResolvePath`
对非 `layer` 类别的 `Image` 种类固定退化为 `sprites/<name>.png`）与彼时数据取值仍然吻合，不转发
只是"多一份未来需要同步维护的拷贝"的低等级风险。上一版本样例数据迁移完成后（sprite 型动画剪辑
`anim.*` -> `sprite_anim.*`，纸娃娃层 `mesh_ref` 的 `sprite.*` -> `paperdoll.*`），不转发就会
变成"运行期解析规则与已迁移数据不匹配"的正确性缺陷，因此本批把决策 8 第二项提前到本次落地；决策
8 第一项（sprite 型隐式接线改显式）与本次数据迁移无因果关系，仍按 ADR 原意留给后续任务。

- `UnityResourceLoader.ResolveModelResourcesPath`/`ResolveAnimClipResourcesPath` 改为直接转发
  `AssetRefConventions.ModelLogicalPath`/`AnimClipLogicalPath`，不再各自维护一份拼接算法。
- `UnityResourceLoader.ResolveEffectDir` 改为经 `AssetRefConventions.ResolvePathSpace` 按类别
  前缀分派到 `vfx`/`sprite_anim` 两个子目录（此前固定拼 `vfx/`，会让 `sprite_anim.*` 找不到
  文件）。
- `UnityResourceLoader.ResolvePath` 对 `ResourceKind.Image` 新增 `paperdoll` 类别分支，转发
  `AssetRefConventions.PaperdollLayerFile`（此前落进通用回退分支被误当 `sprites/<name>.png`
  扁平文件解析）。
- `toolchain/resource_layout_map.json` 新增 `sprite_anim`/`paperdoll` 两条目标子目录映射（否则
  `build.ps1`/`toolchain/sync_package_content.ps1` 都不会把这两棵新目录同步进
  `StreamingAssets/GameFoundation/`，代码改对了也找不到文件）。
- 更新 `adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/
  UnityResourceLoaderTests.cs` 新增 4 个用例（`sprite_anim` 分派、`paperdoll` 分派、与
  `AssetRefConventions` 的转发一致性对照）；`SpriteEquipVisualWiringTests.cs`/
  `WeaponClipRegistrationTests.cs` 的过期取值/注释按已迁移数据勘误（`sprite.item.
  sample_hero_hat_test` -> `paperdoll.item.sample_hero_hat_test` 等，不改断言逻辑）。

**验证结果（2026-09-19，真实引擎环境，如实标注）**：Unity 许可恢复后，以上全部改动点已在
真实引擎环境重新验证——`check.ps1`（不带 `-SkipUnity`）前台全量跑通，全部 29 步 PASS/SKIP，
总用时 425s，Unity 编译检查 PASS（19.2s）、Unity EditMode PASS（70/70）、Unity PlayMode PASS
（288/288）。同一批门禁下，上方"破坏性变更"小节"配套测试同步"提到的
`EquipmentVisualReplayTests.cs`/`VerticalSliceTests.cs` 两处**真正改了断言硬编码常量**（而非仅
注释）的测试同样已重新验证通过。验证过程中发现并根治了 3 处 PlayMode 用例失败（均为测试侧问题，
非产品缺陷，详见下方"修复"小节），二次全量门禁 288/288 全绿。逐条验证结果见：
[待引擎环境验证清单-2026-09-19-资源引用类别前缀适配层接线.md](architecture/落地计划/待引擎环境验证清单-2026-09-19-资源引用类别前缀适配层接线.md)
（已按实测结果更新，标题现为"验证记录"）。**如实说明未覆盖项**：清单中"手工验证：sprite 型
武器风格动画剪辑真实加载"/"手工验证：sprite 型纸娃娃层装备覆盖真实加载"两项是灰盒场景/专门脚本
的人工验证，本次全量门禁（自动化用例 + 冒烟测试）未覆盖这两项——既有自动化用例只断言"发起了
加载请求"，不断言"资源确实加载成功"，二者不等价，仍待专人补验，不视为已验证。

### 未完成/后续任务

- sprite 型"按消费实体行 id 末段命名"隐式接线约定改为显式引用字段（ADR-0038 决策 8 第一项，不在
  本次范围）。
- "引擎适配层接线"小节列出的全部改动点，以及"破坏性变更"小节"配套测试同步"提到的
  `EquipmentVisualReplayTests.cs`/`VerticalSliceTests.cs` 断言取值改动：均已于 2026-09-19 在
  真实引擎环境验证通过（见"引擎适配层接线"小节"验证结果"与验证清单文档）。**仍未覆盖**：验证
  清单中两项手工验证（sprite 型武器风格动画剪辑真实加载/sprite 型纸娃娃层装备覆盖真实加载）
  尚待专人用灰盒场景或脚本补验，见清单文档"如实处理原则"。

### 修复（PlayMode 全量门禁分诊后补修，2026-09-19，真实引擎环境实测）

按分诊报告（`toolchain/unity_test_triage.py` 实测产物、`bin/_check_artifacts/unity/`）逐条根治
三处 PlayMode 用例失败，均为上一条目"引擎适配层接线"落地后首次真实在 Unity 批处理环境跑通两批
改动叠加效果暴露出的测试侧问题，非产品缺陷：

- `SpriteEquipVisualWiringTests`/`EquipmentVisualReplayTests` 共享同一资源引用字面量
  （`paperdoll.item.sample_hero_hat_test`）时的跨用例顺序依赖失败：**第一版方案（提交
  0a07c0c）新增 `UnityResourceLoader.UnloadAllImages`，由 `Tests/Runtime/
  PlayModeIsolation.cs` `TearDownAfterTest` 每条 PlayMode 用例结束后清空
  `ResourceKind.Image` 类别的"已加载"缓存——真实引擎门禁实测（288 条，285 通过，3 失败）证伪
  了该方案：`GreyBoxTests.Move_Right_IncreasesPlayerX_AndTurnsSideways`/`GreyBoxTests.
  PlayerView_ExistsAndLayerResourcesLoaded_NotPlaceholder`/`VerticalSliceTests.
  Paperdoll_LayerOrder_MatchesDisplayMapDeclaredOrder` 三处新失败，均为"精灵/纸娃娃层资源
  加载不到"，已撤销该方案（方法与调用点均已删除，不改为按 id/按用例限定清理的变体，跨用例
  清空共享资源缓存这条路整体放弃，见 `PlayModeIsolation.TearDownAfterTest` 判断记录）。
  改为局部修复：给 `SpriteEquipVisualWiringTests` 专属测试用一个它独有的资源引用字面量
  `paperdoll.item.sample_hero_hat_wiring_test`（新增 `display.equip_visual.
  sample_hero_hat_wiring_test` 一行 + 配套占位资产，并入 `toolchain/import_sample_assets.py`
  既有生成流水线的 `PAPERDOLL_FILES` 列表），"未被加载过"这一前提由夹具本身独占保证，
  不再依赖执行顺序或跨用例缓存清理，`EquipmentVisualReplayTests` 未改动。**
- `VerticalSliceTests` 两处 `FullVerticalSlice_...`/`PRES180_...` 用例：`sprite_anim.
  sample_hero_*` 六个状态的动画资源经本批适配层路由修复 + 占位帧集数据迁移叠加后已能真实
  加载成功，原先预期"加载失败"的 `LogAssert.Expect` 写法过期，改为新增
  `AssertAnimResourcesLoadedSuccessfully`：轮询 `UnityResourceLoader.TryGetEffect` 确认六个
  状态均已从真实占位资源文件加载成功，正面验证新的正确行为（不是删除断言）。经复核，这两处
  断言检查的是 `ResourceKind.Effect` 类别（`sprite_anim.*`），与上一条被撤销的
  `UnloadAllImages`（只清 `ResourceKind.Image` 类别）无关，不受本次撤销影响。

## [1.43.0] - 2026-09-18

消费方反馈第 62/63/64/65/66 条（见
[消费方反馈-2026-09-18-编辑器-第62-66条.md](architecture/落地计划/消费方反馈-2026-09-18-编辑器-第62-66条.md)）。

### 新增

- **第 62 条**：`toolchain/asset_import/check_cmd.py` 新增 `--json`（stdout 只输出一份 JSON 文档，
  其余人类可读输出改走 stderr；不加本参数时文本输出与改造前逐字节一致，两种模式退出码语义相同）。
  顶层结构 `{tool, dataset, domains, ok, counts:{error, warning}, issues:[...]}`，每条 `issue`
  为 `{severity, table, record_key, field_path, check, message, path}`——与
  `toolchain/validator --json` 的 `issues[]` 字段同名同语义对齐（`field_path` 为本工具新增字段名，
  与 validator 的 `field` 语义相同、键名有意不同，因本工具是独立新契约，不强求同名）。新增 26 个
  稳定 snake_case 检查名常量（`check_cmd.CHECK_NAMES`，清单见 `toolchain/README.md`）。
- **第 63 条**：新增 `Core.Foundation.DisplayInfo.EquipVisualModeFieldGroupRule`（检查名
  `equip_visual_mode_field_group`，同 `DisplayKindFieldGroupRule` 惯例），登记进
  `PresentationSchemaCatalog.RegisterAll`：`display.equip_visual.mode` 为 `slot_mesh` 时要求
  `slot_id`/`mesh_ref` 必填，为 `socket_attach` 时要求 `socket_id`/`model_ref` 必填。判断记录：
  本规则只检查"按自己 mode 取值对应的字段组是否填齐"，不检查"是否同时携带另一模式的字段"（04 与
  schema README 均未写"另一模式必须留空"，任务口径取保守解释，是否收紧留待设计层确认）。
  **兼容性**：本规则无条件注册（Error 级，不可提升），既有 `display.equip_visual` 数据若
  `mode=slot_mesh` 但缺 `slot_id`/`mesh_ref`，或 `mode=socket_attach` 但缺 `socket_id`/
  `model_ref`，升级后加载/`validate_data.py --strict`/`toolchain/validator` 会首次报出该检查名
  的阻断错误；处理方式是给对应记录补齐所缺字段，没有开关可以豁免。
- **第 65 条**：`Core.Foundation.EngineAdapter.AssetRefConventions`/
  `toolchain/asset_import/ref_conventions.py` 新增四个字段的路径推导方法/函数：
  `VfxResourceDir`/`vfx_resource_dir`（`vfx.def.resource_ref` → 相对资产根目录的目录）、
  `SfxResourceFile`/`sfx_resource_file`（`sfx.def.resource_ref`/`variants` → 相对资产根目录的
  `.wav` 文件）、`AnimClipLogicalPath`/`anim_clip_logical_path`（model 型
  `display.anim_set.clips.resource_ref` → 引擎侧已导入逻辑资源路径）、
  `ModelLogicalPath`/`model_logical_path`（`display.map.model_ref`、model 型
  `display.equip_visual.mesh_ref`/`model_ref` → 引擎侧已导入逻辑资源路径）。后两者命名刻意用
  `LogicalPath` 后缀，与前两者的 `Dir`/`File` 后缀区分"引擎侧已导入逻辑资源路径"与"资产根目录相对
  路径"两种不能互换使用的路径空间（设计层裁决，见 ADR-0037 决策 1）。仅收口 model 型消费规则，
  sprite 型的并存加载规则本次不纳入，见 ADR-0037 与本轮回复文档"待设计层确认"。新增
  [ADR-0037](architecture/adr/0037-资源引用路径推导契约扩展到vfx-sfx-model-anim_set.md)。

### 变更

- **第 62 条**：`check_cmd.py` 全部 `_check_*` 函数改为先产出结构化 `CheckIssue` 对象，文本模式与
  `--json` 模式共享同一份诊断来源；`sfx`/`vfx` 两个检查域改用 `ref_conventions.py` 共享函数推导
  资源路径，不再各自内联拼接（同第 65 条改动共用同一批共享函数）。

### 移除

- **第 64 条**：删除 `check_cmd.py` 内 `_check_world_row` 的第二份重复定义（第 300～322 行，与第
  275～297 行逐字节相同，此前按 Python 语义实际执行的是第二份；删除后行为不变，仅消除阅读/维护
  困惑）。

### 文档

- **第 66 条**（如实说明，非"待办"）：核实 `display.anim_set.clips.resource_ref`/
  `display.equip_visual.mesh_ref`/`display.weapon_style.auto_attack_anim`/`cast_anim_override`
  四个字段因 sprite/model 并存加载规则与"model 型解析路径不在资产根目录检查域内"两个已知架构性
  阻塞，本轮不新增 `check` 检查域；`display.weapon_style.swing_vfx`/`impact_vfx_override` 是指向
  `vfx.def` 的表引用（软引用），非直接资源文件引用，引用完整性属 `toolchain/validator` 软引用校验
  域（该校验器当前未对软引用做存在性检查，是已知、超出本次任务范围的缺口）；`camera_profile`/
  `ui_layout_definition`/`shell_menu_definition` 三张表 schema 经核实均不含任何资源引用字段，不
  构成同类缺口。`architecture/14_资产规格书模板.md` 第 1.2 节命名规则补充段落。

## [1.42.0] - 2026-09-18

消费方反馈第 59/60/61 条与第 56 条追问（见
[消费方反馈-2026-09-18-编辑器-第59-61条.md](architecture/落地计划/消费方反馈-2026-09-18-编辑器-第59-61条.md)）。

### 新增

- **第 59 条**：`world.map` 新增可选字段 `image_transform`（`pixels_per_unit`/`origin_px`/可选
  `image_size_px`），`core/foundation/scene_router/core/WorldMapSchema.cs`。新增只读纯函数类型
  `Core.Foundation.SceneRouter.MapImageTransform`（`core/foundation/scene_router/contracts/
  MapImageTransform.cs`）：`PixelToWorld`/`WorldToPixel`/`WorldBounds`/静态 `Default`
  （ppu=32、原点图片左上角）/`FromRecord`（字段缺失返回 `null`，不隐含套用默认值）。新增校验规则
  `WorldMapPointOutsideImageValidationRule`（检查名 `world_map_point_outside_image`，Warning、
  不可提升，`core/rules/assembly/RulesSchemaCatalog.cs` 默认注册）：地图声明 `image_size_px` 时，
  `spawn_points`/`teleport_points` 世界坐标落在换算包围盒之外即告警。`toolchain/asset_import/
  map_cmd.py` 新增 `--pixels-per-unit`（默认 32）、`--origin-px`（默认世界原点位于 `ground.png`
  左下角）两个参数，`image_size_px` 从 `ground.png` 的 PNG `IHDR` 块直接读出（不新增第三方依赖）。
- **第 60 条**：`FieldSchema` 新增可选 `MinItems`/`MaxItems`（仅 `IdList`/`Array` 字段可登记，其它
  种类调用 `WithItemCount` 直接抛 `ArgumentException`）与修饰方法
  `WithItemCount(int min, int? max = null)`（`min` 须 ≥ 0，`max` 若提供须 ≥ `min`；只能设置一次）。
  `DataRegistry` 字段级校验新增检查名 `field_item_count`（Error）：`IdList`/`Array` 字段元素数不
  落在登记区间内即报出，消息含实际元素数与允许区间，独立于元素结构（`Item`）是否登记，覆盖顶层
  与任意深度嵌套字段。`presentation/assembly/SchemaFieldItemCountExport`
  （`FieldItemCountInfo`/`Collect`）——与 `SchemaFieldRangeExport`/`SchemaFieldDeprecationExport`
  同一套递归记法，收集全表已登记的元素数量约束；`toolchain/validator --list-tables --json`
  每张表新增 `field_item_counts` 导出（`[{field_path, kind, min_items, max_items}]`），编辑器可
  据此在内容作者增删数组/IdList 元素时就地提示。
- **第 61 条**：`Core.Foundation.DataRegistry.FieldUnit` 新增 `Radian` 成员——框架内角度量一律弧度制，
  逆时针为正、0 指向 +X，集中声明单一来源见 `architecture/05_对象模型与世界.md`"单位约定"新增
  第 9 节（02 文档接口清单前言段角度约定同步改为引用该节）。全仓库全部角度语义字段（10 个字段，4 张
  表）均补 `FieldSchema.WithUnit(FieldUnit.Radian)` 登记并在 `Description` 补"弧度"字样：
  `area.trigger_def.shape{kind=cone}.rotation/angle`、`area.trigger_def.shape{kind=line}.rotation`、
  `area.trigger_def.shape{kind=rect}.rotation`、
  `encounter.def.arena_rules.bounds_shape{kind=cone}.direction/angle`、
  `encounter.def.arena_rules.bounds_shape{kind=line}.direction`、
  `encounter.def.arena_rules.bounds_shape{kind=rect}.rotation`、`spawn.table.facing`、
  `target.chain_def.shape{kind=cone}.angle`。`toolchain/validator --list-tables --json` 的
  `field_meta.unit` 此前已是 `FieldUnit.ToString()` 的通用导出，新增成员后自动吐出 `"Radian"`，
  无需改动导出代码。新增反射式回归测试 `presentation/assembly/tests/AngleFieldRadianUnitTests.cs`：
  遍历全部已登记 `TableSchema`（含嵌套子结构），断言字段名匹配 `angle|rotation|facing|heading` 的
  `Number` 字段均已登记 `Unit == Radian`（允许一份显式例外清单，当前为空）。
- **第 56 条追问**：新增两条默认关闭的可选诊断规则，回应"是否为 `quest_prerequisite`/`talent_tree`
  孤立节点提供可选诊断"的追问——`ContentValidationOptions` 新增显式布尔开关
  `EnableGraphIsolationDiagnostics`（默认 `false`），`toolchain/validator` 新增对应命令行开关
  `--enable-graph-isolation`（首个"命令行式可选规则开关"，与既有两条"提供依赖即启用"的可选规则
  不同）。开启后注册：`Core.Gameplay.Quest.QuestPrerequisiteIsolationRule`（检查名
  `quest_prerequisite_node_isolated`，Warning，`NonEscalatable`）——提示 `quest.def` 里既无前置、
  也未被任何其它任务引用为前置的孤立任务，仅当表内任务总数 ≥2 时报；
  `Core.Numbers.Archetype.TalentTreeIsolationRule`（检查名 `talent_node_isolated`，Warning，
  `NonEscalatable`）——提示每棵 `arch.talent_tree` 里既无前置也未被引用的孤立节点，仅当树内节点数
  ≥2 时报。两条规则均登记进 `ContentValidationAssembly.OptionalRules`（`OptionalRuleNames` 由两项
  扩为四项）。孤立节点在这两类图里仍是合法内容形态（见 1.41.0 `ContentGraphAnalyzer`/
  `story_tree_node_unreachable` 判断记录），本次不改变这一判定，只是把该判断结果按需暴露为一条
  可选的展示性提示，默认关闭、不影响任何既有校验行为。三套官方数据根默认（未开启开关）零行为变化；
  实测三套官方数据根在开关**开启**时均为 0 条新增 Warning：`data/_sample`/
  `games/_template/data/game` 均含 `quest.def`/`arch.talent_tree`，两条规则均已注册运行但命中数为
  0（示例内容前置/引用关系已全连通，无孤立节点，不是规则未生效）；`core/sim/tests/data` 只含
  `arch.talent_tree`（无 `quest.def`），`TalentTreeIsolationRule` 同样运行且命中数为 0，
  `QuestPrerequisiteIsolationRule` 因表不存在而跳过。

### 变更

- **第 60 条**：`world.map.spawn_points`（≥1）、`dialog.story_tree.nodes`（≥1）、
  `quest.def.objectives`（≥1）、`ai.patrol_path.points`（≥2）四处字段改为在 schema 上声明
  `WithItemCount`，对应业务校验规则里原有的"元素数不足"判断分支同步删除，避免同一缺陷经通用字段
  校验与业务规则双报；四处业务规则对空数组/元素数不足的情形改为显式提前 `continue`/`yield break`
  （而不是继续按"已通过数量校验"的假设往下访问首个元素），因为通用字段校验与 `IValidationRule`
  在同一次 `LoadAll`/`Validate` 遍历同一份已加载数据，业务规则不能假设"数量不足"已经阻止了自己
  被调用。
- **第 60 条**：全仓库审计（grep 描述含"至少/至多 N 项/个"但未声明 `WithItemCount` 的
  `Array`/`IdList` 字段）额外发现两处同类缺口，一并补齐：
  - `achv.def.criteria`（≥1）：`AchievementContentValidationRule` 原有的 `achv_criteria_min_count`
    判断分支同步删除（同上四处的迁移方式）。
  - `found.input_action.default_bindings`（≥1）：此前完全没有 `IValidationRule` 覆盖，只在
    `ActionDefinition.FromRecord` 构造期抛异常（不经过加载期批量诊断）；该构造函数检查予以保留，
    作为绕过 `DataRegistry` 加载路径时的最后一道防线，不算重复诊断。
  两处补登后逐一验证三份示例数据 `validate_data.py --strict`：默认数据根（`data/_framework` +
  `data/_sample`）与 `games/_template/data/game` 均 0 error 0 warning；`core/sim/tests/data` 报
  8 条既有已确认的探针/预算偏离 warning（`skill_budget_deviation`×6、
  `item_budget_utilization_low`×1、`override` 忽略提示×1，均带 `budget_note`/T-N6 系列判断记录
  确认，与本次改动无关），未新增任何 `field_item_count` warning——三处均未发现现存示例数据违反
  新登记的数量约束。

### 移除

- **第 60 条**：退役检查名（原有的"元素数不足"诊断分支改由通用 `field_item_count` 覆盖，检查名/
  规则本身若还承担其它职责则保留）：

  | 旧检查名（退役的诊断分支） | 归属 | 现检查名 |
  |---|---|---|
  | `world_map_spawn_points_first_position` 的"spawn_points 至少需要一个出生点"分支 | `WorldMapSpawnPointsValidationRule`（该检查名"首条须含合法 position"分支保留） | `field_item_count` |
  | `story_tree_min_nodes` | `DialogContentValidationRule`（检查名整体退役） | `field_item_count` |
  | `objectives_min_count` | `QuestContentValidationRule`（检查名整体退役） | `field_item_count` |
  | `ai_content` 的"points 至少需要 2 个点"分支 | `AiContentValidationRule`（该检查名其余职责保留） | `field_item_count` |
  | `achv_criteria_min_count` | `AchievementContentValidationRule`（检查名整体退役） | `field_item_count` |

### 文档

- **第 59 条**：`architecture/05_对象模型与世界.md` 新增第 3.1.1 节"坐标约定"——世界坐标 Y 轴
  正方向向上、地图背景图片像素坐标左上角为原点/行向下为正、换算公式；第 4.1 节 `world.map`
  字段表补 `image_transform`。`architecture/14_资产规格书模板.md` 第 2.2 节 `pixels_per_unit`
  行由 `<待填>` 改为"框架默认 32；每张地图可在 `world.map.image_transform` 覆盖"。新增
  [ADR-0036](architecture/adr/0036-地图图片像素与世界坐标换算约定.md)。
- **第 61 条**：`architecture/05_对象模型与世界.md` 新增第 9 节"单位约定"（弧度制角度字段清单），
  第 2 节改为引用该节，不再各处分散重复声明；第 3.5 节 `Shape` 字段表 `direction`/`angle`/
  `rotation` 参数补"（弧度）"标注；`toolchain/README.md`、
  `core/foundation/data_registry/README.md`、`core/gameplay/quest/README.md`、
  `core/numbers/archetype/README.md`、`presentation/assembly/README.md` 补判断记录。

## [1.41.0] - 2026-09-18

编辑器上游反馈第 54～58 条（见
[消费方反馈-2026-09-18-编辑器-第54-58条.md](architecture/落地计划/消费方反馈-2026-09-18-编辑器-第54-58条.md)，
第 55 条原分片文档已并入本文件同一份回复文档的第 55 条小节）。

### 新增

- **第 54 条**：`Core.Gameplay.Quest.QuestReferenceExtractor.ExtractReferencedQuestIds(string
  prerequisiteExprText, IExprSchema? schema = null) : IReadOnlyList<Id>`——提取
  `quest.def.prerequisite` 里引用的其它任务 id，已去重、按首次出现顺序排列；语法解析失败返回空
  列表，不抛新的未处理异常。`QuestContentValidationRule` 原私有的 AST 遍历方法迁移为本类型的
  `internal` 方法并被前者复用，不再各自维护一份遍历逻辑。
- **第 56 条**：新增 `Core.Foundation.DataRegistry.ContentGraphAnalyzer`（+
  `ContentGraphAnalysis`）——对调用方给定的"节点 id 全集 + 有向邻接表"统一给出不可达/孤立/入度
  出度/环四类只读分析结果，供 `story_tree`/`quest_prerequisite`/`talent_tree` 三类图共用。
  `dialog.story_tree` 内部复用它新增校验项 `story_tree_node_unreachable`（Warning，
  `NonEscalatable`）：从运行时实际入口 `nodes[0]` 出发不可达的节点。`quest_prerequisite`/
  `talent_tree` 两类图的孤立节点是合法内容形态（一条独立支线任务、天赋树并列根节点），按判断记录
  不新增等价校验警告，只把 `ContentGraphAnalyzer` 作为公开分析能力暴露给内容工具。
- **第 57 条**：`ValidationIssue` 新增只读属性 `AffectedNodeIds`（`IReadOnlyList<string>`，默认空
  集合非 `null`）与 `WithAffectedNodeIds` 方法（经新增的十参数构造传入，ABI 只新增，不改既有构造
  签名）——跨节点路径类图诊断（`story_tree_cycle`/`quest_prerequisite_cycle`/
  `talent_prerequisite_cycle`）按环上出现顺序填入环上全部节点 id；单节点类图诊断
  （`story_tree_duplicate_node_id`/`story_tree_dangling_next_node`/`quest_prerequisite_unknown`/
  `talent_prerequisite_missing`）填入该节点自身 id。`story_tree_duplicate_node_id`/
  `story_tree_dangling_next_node`/`talent_node_id`/`talent_prerequisite_missing` 四项单节点类
  诊断的 `Field` 补上具体下标路径（如 `nodes[3].id`/`nodes[3].branches[1].next_node_id`）。
  `toolchain/validator --json` 的 `issues[]` 每项新增 `affected_node_ids` 字段（空时省略，不同于
  `group`/`note`/`rule_id` 恒为 `null` 的既有约定）。
- **第 55 条**：`IDialogHost`/`IQuestHost` 均是有状态运行期契约，无法在不持有单位/会话的前提下
  "模拟走一遍"剧情树/任务前置链（编辑器"试走"面板类诉求）。新增两个独立只读无状态入口，均不改动
  既有接口成员：`Core.Gameplay.Dialog.DialogStoryPreview.Preview(StoryTreeDefinition tree,
  Func<string, bool?>? evaluateCondition = null, int maxPaths = 200, int maxDepth = 64)`——给出
  剧情树节点列表、每节点出边（目标节点/分支文本键/条件规范化文本）、起点（`FirstNode`）、终止节点；
  提供可选条件判定回调时额外给出可达节点集合与路径枚举（含环/悬空引用/深度或条数上限的安全截断，
  显式标记 `PathsTruncated`）。`Core.Gameplay.Quest.QuestPrerequisitePreview.Preview(Id questId,
  IEnumerable<QuestDefinition> definitions, IReadOnlyCollection<Id>? completedQuestIds = null, int
  maxNodes = 10000)`——给出任务 `prerequisite` 里 `quest.*` 引用构成的直接前置/传递闭包/拓扑序，
  成环时显式报告 `HasCycle`/`CyclePath`（不给出误导性的"空拓扑序=无前置"）；提供已完成任务集合时
  额外给出可接性判定与阻塞任务列表。均为纯新增类型，不涉及 `QuestObjective` 语义，不对
  `prerequisite` 做真正的布尔求值（只看引用了哪些任务，不看 `and`/`or`/`not` 组合方式，口径与既有
  `quest_prerequisite_cycle` 校验一致）。整合时把前置引用提取统一到
  `QuestReferenceExtractor`（见下"勘误"）与 `QuestPrerequisitePreview` 共用同一份实现，消除并行
  分支各自实现的重复。

### 文档

- **第 58 条**：`arch.talent_tree.nodes[].id` 有意登记为 `FieldKind.String` 而非
  `FieldKind.Id`（不受 `field_id_format` 点分格式约束），与 `dialog.story_tree.nodes[].id`
  （`FieldKind.Id`）不同——原因（天赋树节点 id 只在同一棵树内以字符串比较，运行时
  `ArchetypeRegistry.ParseTalentTree` 也只检查非空字符串）此前只写在代码注释，本次补进
  `core/numbers/archetype/README.md`/两处 `FieldSchema.Description`/
  `editor/docs/编辑器产品文档.md`（`.html` 同步）。纯文档新增，不改 `FieldKind`、不改数据。

### 勘误

- `ArchTalentTreeCycleValidationRule` 的成环检测算法改为与 `story_tree_cycle`/
  `quest_prerequisite_cycle` 同款的单次共享状态三色标记 DFS，候选起点改按节点在原始 JSON 数组里
  的出现顺序遍历，不再依赖 `Dictionary.Keys` 枚举顺序（AGENTS.md"保持确定性"）；
  `talent_prerequisite_cycle` 消息文本顺带补上完整环路链（原消息只报告 DFS 起点），无既有测试
  断言该文本，判定为无风险勘误。
- 整合验收发现 `QuestPrerequisitePreview`（第 55 条）与 `QuestReferenceExtractor`（第 54 条）在
  并行分支上各自实现了一份"从 `prerequisite` 表达式提取 `quest.*` 引用"的 AST 遍历逻辑：前者手写
  and/or/not/比较/引用节点的完整 switch 递归，与既有公开的 `Core.Foundation.Expr
  .ExprReferenceCollector.Collect` 语义等价（T-N5-2 已提供的通用引用遍历入口）。整合时把
  `QuestReferenceExtractor.CollectQuestIdLiteralArgs` 改为基于 `ExprReferenceCollector.Collect`
  实现（不再自行手写树遍历），并新增一个接受已解析 `ExprNode` 的内部重载供
  `QuestPrerequisitePreview` 直接复用（避免其重新解析 `prerequisite` 文本），消除重复实现；
  两者原有对外行为/测试断言不变（新增测试覆盖等价性，见下）。

### 迁移说明

- 本版本全部为新增 API（`QuestReferenceExtractor`、`ContentGraphAnalyzer`、`ValidationIssue
  .AffectedNodeIds`/`WithAffectedNodeIds`、`DialogStoryPreview`、`QuestPrerequisitePreview`），
  旧签名与默认行为均未变化，**无必做迁移项**，直接升级即可。
- **唯一实质行为变更：`dialog.story_tree` 新增 Warning 级校验 `story_tree_node_unreachable`**——
  在 `--strict` 模式下（Warning 也阻断），若接入方数据里存在从运行时实际入口 `nodes[0]` 出发
  不可达的节点，校验会新增一条该 Warning 并因 `--strict` 而阻断。处理方式：先确认该节点是否为
  遗留死节点——若确认是数据缺陷，补上缺失的 `next_node_id` 连接使其可达，或删除该孤立节点；若
  该节点是刻意保留的草稿/未接入内容，需按内容组织规范另行安排（`ContentGraphAnalyzer` 只做
  只读分析，本规则不提供 suppress 机制）。框架自带三套官方数据根（默认合并根、
  `data/_framework` 单独、`games/_template/data/game`、`core/sim/tests/data`）均已逐一验证
  `--strict` 下 0 新增警告，无需改动框架自带示例数据；接入方数据若命中新规则，属于该数据自身的
  既有孤岛节点被首次发现，不是本版本引入的新缺陷。
- 图诊断 `Field` 更具体：`story_tree_duplicate_node_id`/`story_tree_dangling_next_node`/
  `talent_node_id`/`talent_prerequisite_missing` 四项诊断的 `Field` 从静态字符串（如
  `"nodes"`）改为具体数组下标路径（如 `nodes[3].id`）；若消费方代码对 `Field` 做精确字符串匹配，
  需要改为前缀匹配，或改读新增的结构化字段 `AffectedNodeIds`——诊断本身是否触发的判定逻辑不变。
- 诊断文本变化：`talent_prerequisite_cycle` 消息文本从"报告 DFS 起点"改为"报告完整环路链"
  （成环算法勘误的副产物，见上"勘误"一节），无既有测试断言该文本；若消费方对该消息做过精确
  字符串匹配，需要更新匹配逻辑，不影响该诊断是否触发。
- `toolchain/validator --json` 的 `issues[]` 每项新增 `affected_node_ids` 字段（仅非空时输出，
  不同于 `group`/`note`/`rule_id` 恒为 `null` 的既有约定）：消费方若用禁止未知字段的严格 JSON
  schema 解析，需要放开允许该新字段。

## [1.40.0] - 2026-09-17

编辑器上游反馈第 49～53 条（
[消费方反馈-2026-09-17-编辑器-第49-51条.md](architecture/落地计划/消费方反馈-2026-09-17-编辑器-第49-51条.md)、
[消费方反馈-2026-09-17-编辑器-第52-53条.md](architecture/落地计划/消费方反馈-2026-09-17-编辑器-第52-53条.md)）。
第 50 条（高优先级）：`FightRunner`/`ArenaSimulation`/`GrowthSimulation`/`CoverageSimulation`
四个 `Run` 入口新增接受 `CancellationToken`/`IProgress<Core.Sim.SimProgress>` 的重载（新只读
结构体 `SimProgress`：`Stage`/`Completed`/`Total`/`Detail`），取消抛 `OperationCanceledException`
且绝不返回半成品报告，检查点覆盖外层迭代边界与（`FightRunner`/`ArenaSimulation`）单场战斗内部
逐 tick；旧签名转发到新重载，行为不变。`toolchain/simrunner` 新增 `--progress`（进度行写
`stderr`）与 Ctrl+C 取消、新退出码 `4`="cancelled"（`0`/`1`/`2`/`3` 不变）。第 49 条：新增
`FightRunnerOptions.CaptureEvents`/`MaxCapturedEvents` 与 `FightResult.CapturedEvents`
（`IReadOnlyList<FightLogEntry>`，关闭时为空集合非 `null`）/`CapturedEventsTruncated`，新类型
`FightLogEntry`/`FightLogEventCategory` 覆盖伤害/治疗/施法成功失败/增益施加移除/死亡/资源变化；
聚合结果不受开关影响（已测试）；`toolchain/simrunner` 无单场战斗 CLI 模式，本条能力暂为
API-only。第 51 条：`core/sim/README.md` 新增"线程安全与并发"章节，承诺四个 `Run` 入口
side-effect-free、可在调用方不并发修改共享输入的前提下安全并发调用，新增并发回归测试
`core/sim/tests/ConcurrencyTests.cs`；落地前复审静态可变状态额外发现并修复一处真实数据竞争
——`Core.Foundation.DataRegistry.FileSystemDataSource.ListTables()` 此前在共享字段
`_skippedNonTableFiles` 上做 `Clear()`+逐条 `Add()`，同一实例被多线程并发复用时产生数据竞争，
已改为局部构建后一次性整体替换只读引用，新增并发回归测试。均为纯新增重载/可选属性/文档章节，
不改变既有签名与默认行为。

### 新增

- `Core.Carriers.Common.EquippedWeaponSummary`（新只读结构体，反馈第 52 条）：把一个已装备
  `ItemInstance` 的身份字段（模板/品质/词缀）原样重排为"武器摘要三元组"，纯搬运、不计算数值；
  `EquipmentHost` 新增 `GetEquippedWeaponSummary(Id unitId, Id slot)`/
  `GetAllEquippedWeaponSummaries(Id unitId)` 两个方法；`Core.Sim.StandardPlayer` 新增只读属性
  `EquippedWeaponSummaries` 与建议签名一致的便捷方法 `GetEquippedWeaponSummary(weaponSlotId)`
  （`StandardPlayerBuilder.Build` 内部一次性调用新方法填充），消费方不必再各自实现一遍"取
  `ItemInstance` 再手工搬运三字段"的薄转换层。
- `Core.Sim.GrowthReport` 新增只读集合 `OpponentAmbiguities`（`GrowthOpponentAmbiguity` 类型：
  `TierId`/`Level`/`ChosenTemplateId`/`DiscardedTemplateIds`，反馈第 53 条）：同 `tier`+`level`
  存在多条对玩家阵营敌对的 `creature.template` 候选时如实记录被丢弃的候选，集合非空时
  `GrowthReport.ToJson()` 才输出 `opponent_ambiguities` 字段（默认不出现，不影响既有基线比较）。
- 新增校验规则 `Core.Sim.SimGrowthOpponentAmbiguityValidationRule`（检查名
  `sim_growth_opponent_ambiguous`，Warning 级、不可提升，反馈第 53 条）：离线静态检查同
  `tier`+`level` 下是否存在多条对玩家阵营敌对的 `creature.template` 记录；`NumericValidationRuleCatalog`
  同步登记为第 25 行（阻断 15 + 警告 10，"仿真"分组扩到五行）。

### 修复

- `Core.Sim.GrowthSimulation.ResolveCreatureFamily` 按 `tier`+`level` 定位对手候选时不按
  `faction_id` 过滤的既有缺陷（反馈第 53 条）：新增按玩家阵营敌对关系过滤候选（`IFactionMatrix
  .IsHostile(playerFactionId, candidateFactionId)`），过滤后一个候选都不剩时显式抛
  `InvalidOperationException`（不静默回退到未过滤集合）；过滤后同档位仍有多条候选时维持"数据
  登记顺序第一条生效"的既有基线行为不变（改用 `OrderBy` 稳定排序，不再依赖 `List.Sort`），但
  会记入上面新增的 `OpponentAmbiguities` 诊断集合。
- `data/_sample`：`creature.sample_summon_totem`（`summon_only` 占位生物）与 `creature.sample_beast`
  此前恰好同 `tier=creature.tier.sample_normal`、`level=1`，会被新增校验规则判定为歧义——挪到
  新增的独立 tier `creature.tier.sample_summon`（`xp_multiplier`/`gold_multiplier` 均为 0，符合它
  从未打算作战斗对手的定位），`validate_data.py --strict` 恢复 0 error 0 warning 基线。

### 迁移说明

- 本版本全部为新增 API（`CancellationToken`/`IProgress<SimProgress>` 重载、`CaptureEvents` 相关
  类型、`EquippedWeaponSummary` 及其读取方法、`GrowthReport.OpponentAmbiguities`、
  `sim_growth_opponent_ambiguous` 校验规则），旧签名与默认行为均未变化，**无必做迁移项**，直接
  升级即可。
- 唯一行为变更：`GrowthSimulation` 的对手选择现在会按玩家阵营排除非敌对阵营的 `creature.template`
  候选；若某 `tier`+`level` 下过滤后一个候选都不剩，会抛 `InvalidOperationException`
  （此前会静默选中非敌对候选）——如自有数据在同档位只登记了非敌对阵营生物，需要补登记至少一个
  敌对阵营候选，否则升级后跑成长期仿真会在该档位报错。
- `data/_sample` 中 `creature.sample_summon_totem` 的 `tier` 由 `creature.tier.sample_normal`
  改为新 `creature.tier.sample_summon`：仅示例数据调整，自有数据不受影响。
- `toolchain/simrunner` 新增退出码 `4`="cancelled"：如有脚本按退出码分支处理，需要补上对 `4`
  的识别（不影响既有 `0`/`1`/`2`/`3` 语义）。

## [1.39.1] - 2026-09-17

消费方反馈-2026-09-17：存档/回滚恢复战斗态误判为真实脱战导致资源当前值被回满覆盖，纯缺陷修复，
不涉及数据/存档格式变更。

### 修复

- **存档/回滚恢复战斗态误判为真实脱战、把刚恢复的资源当前值回满为上限**（消费方反馈-2026-09-17，
  影响版本范围约 v1.33.0～v1.39.0）：`Core.Rules.Combat.CombatHost.RestoreCombatState`（存档读档/
  回滚恢复战斗态的唯一入口，C11-RELOAD 于 2026-09-11 引入）与
  `Core.Gameplay.Assembly.PlayerVitalsPersistable` 旧两参构造函数兜底分支此前都直接调用
  `IPowerHost.SetInCombat` 同步 `PowerHost` 自身的进出战状态——`SetInCombat` 从诞生起就同时承担
  "设置进出战布尔状态"与"true→false 转换时对 `refill_on_leave_combat=true` 的资源类型立即回满"
  两件事（真实玩法语义：脱战自动回满）。存档/回滚恢复不是一次真实脱战：若恢复发生时运行期状态
  恰好是 `true`（如读档前玩家正在战斗）、而存档快照/回滚快照写的是 `false`，就会被误判为一次
  真实脱战，把 `player.vitals` 段刚用存档值恢复好的资源当前值（如生命值）覆盖为资源上限——真实
  探针复现：存档 `health=37`，读档前运行期 `in_combat=true`，存档 `in_combat=false`，读档后
  `health` 被回满成 `100`。框架默认数据 `arch.power.health` 自 v1.33.0（ADR-0031，`arch.power_type`
  默认打开 `refill_on_leave_combat`）起即可复现，此前（`refill_on_leave_combat` 默认关闭）该缺陷
  潜伏但不可见。修复：新增 `IPowerHost.RestoreInCombat(unitId, inCombat)`（C#8 默认接口方法，
  ABI 只新增；`PowerHost` 显式覆盖为纯赋值实现，不遍历资源类型、不触发回满、不发事件），
  `CombatHost.RestoreCombatState` 与 `PlayerVitalsPersistable` 旧两参构造函数兜底分支均改调用
  该方法；`SetInCombat` 本体逻辑与既有"真实脱战仍回满"行为不变。

### 迁移说明

- 无需改动数据与存档：`player.vitals` 段字段名/结构、`IPersistable` 契约均未变化，修复完全在
  运行时恢复逻辑内部；升级到本版本即可，旧存档读档后不会再出现"读档瞬间生命值被回满"的现象。
- 影响版本范围约 v1.33.0（`arch.power.health` 默认打开 `refill_on_leave_combat`，ADR-0031）～
  v1.39.0（修复前的 main HEAD）；`core`/`Core.Rules.dll`/`Core.Numbers.dll` 修复后需要重新构建
  并随发布把新 DLL 带进 `adapter.unity` 包，接入方升级依赖后按各自"读档触发脱战回满"复现场景
  重跑读档相关用例复验。

## [1.39.0] - 2026-09-18

编辑器上游反馈第 48 条：`LootTableAnalyzer.ExpectedProbabilities` 补品质/词缀/货币三段期望分布
维度；`core/sim/tests/data` 嵌入仿真数据集补 `display.map` 缺行；`check.ps1` 新增嵌入数据集校验步骤；
验收顺带修复 `InclusionProbabilities` 不放回多抽 `k>=n` 快速路径对零权重条目误报入选概率的既有
缺陷（反馈 35/1.24.0 遗留）。

### 新增

- `Core.Gameplay.Loot.LootTableAnalyzer` 新增三个只读分析入口（均只新增，`ExpectedProbabilities`
  既有签名与结果不变，消费方反馈第 48 条）：
  - `ExpectedQualityDistribution(LootTableDef, LootAnalysisContext, IDataRegistryView):
    IReadOnlyList<LootQualityOutcome>`——每个叶子 `item.*` 的期望品质分布（条件概率），与
    `LootHost.RollQuality` 同一套判定；同一叶子经多条不同 `LootEntry`（不同分组/嵌套表分支/
    `guaranteed_min` 补抽）贡献时按期望产出数量加权混合。
  - `ExpectedAffixInclusion(Id templateId, Id qualityId, IDataRegistryView, int exactMaxEntries =
    16): LootAffixInclusionResult`——给定模板与品质骰结果，算出词缀骰候选池逐条入选概率（位掩码
    动态规划精确解，复用 `weighted_pick_one` 不放回多抽同一份算法）；候选池超过阈值时返回 `null`
    并显式标记降级（不退化为近似值）。
  - `ExpectedCurrency(LootTableDef, LootAnalysisContext, IEconomyHost, int? sourceLevel = null,
    Id? tierId = null, LootGoldMultiplierProvider? goldMultiplierProvider = null):
    IReadOnlyList<LootExpectedCurrencyOutcome>`——每种货币（`econ.*` 叶子）的期望产出数量，与
    `LootHost.ResolveCurrencyOutcome` 逐项对齐（当量 × 金币基数 × 分档倍率 × 难度倍率）。
- `core/gameplay/loot/contracts/LootDistributionAnalysis.cs` 新增三个纯数据结果类型
  `LootQualityOutcome`/`LootAffixInclusionResult`/`LootExpectedCurrencyOutcome`，均带
  `IsDegraded`/`Reason` 字段（只读分析入口遇 registry 阻断态或数据缺失时显式标记，不抛异常）。
- `core/sim/tests/data` 嵌入仿真数据集补齐 `skill.sim_review_b_weapon_pct_strike` 的
  `display.map` 映射行（复审整合项 3 `17a6670` 遗留缺口，`DisplayMapCoverageRule` 此前报 1 条
  阻断 error）；`toolchain/validate_data.py --strict --data-root core/sim/tests/data` 对该根
  校验回到 `errors 0`。

### 变更

- `check.ps1` 新增一步（"6a"，排在 pytest 之后、数值仿真基线比对之前）单独对
  `core/sim/tests/data` 跑 `validate_data.py --strict`（`-Quick` 下也跑）——此前只有"数值仿真
  基线比对"用 `SimRunner` 装载该数据集跑仿真场景，不等价于跑一遍声明式规则引擎校验，同类遗漏
  （如本次的 `display.map` 缺行）会被放过；新步骤堵住这条门禁空档。

### 修复

- `Core.Gameplay.Loot.LootTableAnalyzer.ExpectedProbabilities` 反馈 48 验收发现的既有缺陷
  （反馈 35/1.24.0 遗留）：`weighted_pick_one` 不放回多抽 `pick_count` 等于候选池条目数
  （`k>=n` 快速路径）时，权重 <=0 的条目也被无条件报入选概率 1.0——但该条目在
  `LootHost.PickWeighted` 语义下永远选不中（剩余权重合计归零即整体提前停止），理论值与真实抽取
  不符。修复为快速路径按各条目权重区分：权重 >0 报 1.0，否则报 0（下游 `ExpectedProbabilities`
  因不命中条目一律不入账，表现为该条目整条不出现在结果里）；`ExpectedAffixInclusion` 本身在构建
  候选池阶段就过滤掉零权重词缀，不受影响，一并补一条断言留痕。

### 文档

- 新建
  [消费方反馈-2026-09-18-编辑器-第48条.md](architecture/落地计划/消费方反馈-2026-09-18-编辑器-第48条.md)。
- `core/gameplay/loot/README.md` 新增"消费方反馈第 48 条判断记录"一节，并补一条"本次收口顺带修复
  既有缺陷"说明（`InclusionProbabilities` k>=n 快速路径零权重条目误报）。
- `editor/docs/编辑器产品文档.md`/`.html`（v2.18）第 4.1 节新增一行契约面——"掉落品质/词缀/货币
  期望分布分析入口"（消费方反馈第 48 条）；变更记录表追加 v2.18 一行。

### 迁移说明

- 无阻断性变化——三个新增分析入口与新增结果类型均为纯新增（`ExpectedProbabilities` 既有签名/
  结果、`LootHost`/`RollContext` 等运行期路径逐位不变），消费方不需要跟改既有调用。
- **理论 vs 观测面板**：编辑器"理论 vs 观测"比对面板此前品质/词缀/货币三段只能展示观测值
  （蒙特卡洛），现在三段都能取到理论值——词缀候选池 > 16 条时理论列仍只能显示"—（仅模拟）"（见
  `LootAffixInclusionResult.IsDegraded`/`Reason`）。

## [1.38.0] - 2026-09-17

编辑器上游反馈第 45/46/47 条 + 数据校验器非数据表 JSON 跳过。

### 新增

- `IDataRegistryView` 新增带默认实现的 `TryGet(string table, string key, out DataRecord?
  record)`（及 `CommonId` 重载），语义与既有 `TryGetAll` 对称——阻断态返回 `false`，非阻断态与
  `Get` 一致；`DataRegistry` 显式覆盖为直接读内部快照（消费方反馈第 45 条）。
- `Core.Foundation.DataRegistry.TolerantRegistryView`（`sealed class`，实现
  `IDataRegistryView`，静态工厂 `Wrap(view)` 对已是该类型的入参直接复用）：把任意
  `IDataRegistryView` 包装成"读不到就退化为空/null，不向外抛阻断异常"的只读视图，新增
  `IsDegraded`/`MissingTables`/`WasMissing(table)`（消费方反馈第 45 条）。
- `ItemBudgetCurve.BuildStatBudgetInfo` 新增两个带 `out TolerantReadDiagnostics diagnostics`
  出参的重载（纯新增）；`EquipmentScoreResult`/`SkillBudgetResult` 各新增一个带
  `isDegraded`/`missingTables` 两参的构造函数重载（纯新增，ABI 只新增）与对应
  `IsDegraded`/`MissingTables` 只读属性；`ExpectedStatCalculator` 新增同名属性（消费方反馈第
  45 条）。
- `FieldSchema` 新增 `WithDeprecated(string sinceVersion, string? replacedBy, string? note =
  null)` 修饰方法与 `IsDeprecated`/`DeprecatedSince`/`ReplacedBy`/`DeprecationNote` 四个只读
  属性（同 `WithCurve`/`WithSoftReference` 既有惯例，不破坏既有构造签名）；`toolchain/validator
  --list-tables --json` 的 `field_meta` 新增 `deprecated`（`{since, replaced_by, note}`，未
  登记为 `null`）；`--schema-audit` 新增检查名 `field_deprecated_metadata`（消费方反馈第 46
  条）。
- `DataRegistry` 字段级校验新增检查名 `field_id_format`（阻断级，见"变更"一节行为说明）；
  `SkillBudgetValidationRule`/`ItemGrantValueExceedsShareRule` 新增检查名
  `skill_budget_record_unparseable`/`item_grant_value_unparseable`（均 Warning，不可提升，
  消费方反馈第 47 条）。
- `FileSystemDataSource` 新增 `DataSourceOptions`（可选构造项，默认开启跳过非数据表 JSON，ABI
  只新增）。
- `toolchain/validator --list-tables --json` 每张表条目新增 `deprecated_paths`
  （`[{path, since, replaced_by, note}]`，`presentation/assembly/SchemaFieldDeprecationExport`）：
  按点路径记法（同 `field_ranges` 的 `FieldPath` 记法）递归全量导出本表全部（含顶层与任意深度
  嵌套）已登记废弃元数据的字段——既有 `field_meta.deprecated` 只覆盖顶层字段，`quest.def
  .rewards.xp`、`skill.def`/`skill.aura_def` 效果参数 `scaling_stat`/`coefficient` 等嵌套字段
  此前这条导出路径读不到（独立验收反馈第 46 条落地遗留的必须修项收口）。

### 变更

- **行为变更（消费方反馈第 47 条）**：字段级"取值确是字符串但不满足 Id 语法"（如空字符串）的
  诊断，此前笼统归入 `field_type`，现拆出改报专用检查名 `field_id_format`——`Id`/`Reference`
  （含子结构递归）、`IdList` 逐元素、`Map` 值种类为 `Id`/`Reference` 时的值均受影响；取值本身
  不是字符串仍归 `field_type`，`Map` 键格式校验不受影响（仍归 `reference_integrity`）。**按检查
  名过滤/映射诊断的消费方需跟进**，见"迁移说明"。
- `SkillBudgetValidationRule.Validate` 的 `catch` 由仅 `ArgumentException` 扩大为同时捕获
  `Core.Foundation.DataRegistry.DataFieldException`，遇到结构非法记录时不再彻底静默，改产出
  `skill_budget_record_unparseable` 诊断（消费方反馈第 47 条）。
- `ItemGrantValueExceedsShareRule` 原本对 `SkillBudgetAnalyzer.ComputeGrantValue` 的调用零
  try/catch 兜底，补齐异常兜底，改产出 `item_grant_value_unparseable` 诊断（消费方反馈第 47
  条）。
- `ItemBudgetCurve.BuildStatBudgetInfo`/`EquipmentScoreAnalyzer.Score`/`SkillBudgetAnalyzer
  .Analyze`/`ComputeGrantValue`/`ExpectedStatCalculator` 内部改用 `TolerantRegistryView`
  包装：registry 阻断态下不再抛 `InvalidOperationException`，改按容错语义返回降级结果（非阻断态
  行为逐位不变，消费方反馈第 45 条）。`IBudgetSolver.Solve`/`SkillDefCache` 等运行期入口不变，
  仍遵守"运行时不做静默降级"。
- `item.template.stat_roll_ref` 补登 `.WithDeprecated("1.32.0", "affixes", ...)`（整合反馈
  45/46/47 条同批收口，Description 早已用自由文本写明该事实，本次补为结构化标记）。
- `Core.Gameplay.Common.RewardSchemaFields.Rewards()`（无调用方的历史遗留公开方法）标
  `[Obsolete]`——ABI 规则禁止删除公开成员，不物理删除；Description 里的"xp（已废弃）"改写为指向
  `QuestSchemas.RewardsFields` 的 `xp` 字段结构化标记（整合反馈 45/46/47 条同批收口）。

### 修复

- `toolchain/validate_data.py`/`toolchain/validator`（`core/foundation/data_registry
  .FileSystemDataSource`）数据根目录里非数据表 JSON 文件（如游戏侧 UPM 包根目录的
  `package.json`）不再被误判成数据表、报出假的"缺少顶层字段 table/schema_version/rows"错误。

### 文档

- 新建 [消费方反馈-2026-09-17-编辑器-第45-47条.md](architecture/落地计划/消费方反馈-2026-09-17-编辑器-第45-47条.md)。
- `editor/docs/编辑器产品文档.md`/`.html`（v2.17）第 4.1 节新增两行契约面——"只读分析入口阻断态
  容错"（消费方反馈第 45 条）与"字段级 Id 语法与规则解析容错"（消费方反馈第 47 条）；变更记录表
  追加 v2.17（反馈 45）/v2.17（反馈 47）两行；现有 v2.17 行补充 `item.template.stat_roll_ref`
  收口说明；`TableSchema`/`FieldSchema` 一行描述追加 `deprecated_paths` 说明，变更记录表再追加
  v2.17（反馈 46 CLI 收口）一行。
- `docs/升级指南/1.29.0到1.37.0-数值设计专项.md` 附录 C"废弃与替代 API 总表"补一行
  `item.template.stat_roll_ref → affixes（1.32.0）`。

### 迁移说明

- **`field_type` → `field_id_format` 过滤跟进**：消费方若按检查名过滤/分类诊断（如问题面板按
  `Check` 归类、消费方反馈第 43 条的 `TryGetOptionalRuleByCheck` 一类按检查名索引的逻辑），需要
  把此前隐含在 `field_type` 里的"Id 语法非法"部分改指向新检查名 `field_id_format`；`field_type`
  本身语义不变（仍表示"取值类型根本不对"）。
- **废弃字段元数据可替代手工清单**：`Editor.Core.Validation.DeprecatedFieldHints` 一类自建静态
  清单可退役，改从 `FieldSchema.IsDeprecated`/`DeprecatedSince`/`ReplacedBy`/`DeprecationNote`
  读取；走命令行路径的消费方——顶层字段读 `--list-tables --json` 的 `field_meta.deprecated`，
  嵌套字段（`field_meta` 不覆盖）改读新增的 `deprecated_paths`（点路径，全量导出含顶层与任意
  深度嵌套）。
- **只读分析入口容错后包装可退役**：`Editor.Core.Validation.TolerantRegistryView` 一类自建结构性
  包装可退役，改用框架原生 `Core.Foundation.DataRegistry.TolerantRegistryView`。
- **`stat_roll_ref` 标废弃**：`item.template.stat_roll_ref` 现已结构化标记为废弃（`ReplacedBy:
  "affixes"`），消费方若仍在读取该字段，应尽快迁移到 `affixes`。

### 编辑器接入建议

- 问题面板按检查名过滤/分类诊断的逻辑，需要跟进 `field_id_format` 这一新检查名（见"迁移说明"）。
- `skill_budget_record_unparseable`/`item_grant_value_unparseable` 两个新增诊断出现时不需要
  重复排查——对应记录的结构问题已由字段级检查项单独报出。
- `IBudgetSolver.Solve` 仍是唯一的运行期预算求解权威入口，阻断态行为未变；只读分析/展示场景请用
  本版本新增的容错入口，不要对运行期入口做额外包装。
- 详见回复文档"对编辑器的使用建议"一节。

## [1.37.0] - 2026-09-16

N0～N6 深度代码复审（五个领域）修复版：必须修 7 项、建议修 11 项、测试覆盖缺口 19 条；契约只
新增。

### 修复

- `StatHost.RecomputeRatingStats`/`RemoveModifiersBySource` 改为批量传播派生失效，修复多来源
  派生属性在单次调用内广播错误中间值（A-M1）。
- `SkillBudgetAnalyzer.Classify` 补齐技能预算带宽下界判定，修复严重弱于预算的技能永远判"通过"
  （C-M1）；`data/_sample` 与嵌入数据集相应校正/补 `budget_note`。
- `EquipmentHost.GetWeaponDps` 改用装备实例真实品质（B-M1）。
- `CreatureDeathLootListener.OnKill` 货币入账核对击杀者身份并解析召唤物归属，非玩家击杀退回
  落地（D-M1）。
- 升级回满时序：`ProgressionHost.AddXpCore` 在最后一级发布 `LevelUpEvent` 前先写入聚合成长，
  `RulesAssembly` 订阅先 `RecomputeMax` 再 `RefillAll`（D-M2）。
- 数值仿真命中率统计排除被免疫全额吸收的结算（E-M2）。
- `toolchain/abi_probe.ps1` 基线侧补齐 `Core.Sim.dll` 依赖抽取（整合项 1）。
- `ArchClassDerivationOverrideValidationRule` 引用不存在时跳过（整合项 4）。

### 变更

- `SkillHost.GetSkillReadiness` 的 `ActionLocked` 判定补齐排队窗口（C-S1）。
- `EffectDispatcher` 周期效果冻结缓存键加入效果下标（C-S2）。
- `Resolver.BuildDamageTakenStatIds` 去重结果缓存（A-S1）。
- `creature.tier_definition.xp_multiplier`/`gold_multiplier` 补非负范围校验（B-S2）。
- `LootContentValidationRule` 成环检测报错文本可重现（B-S3）。
- `EconomyHost.FindAnyVendorSellPrice` 改索引查询（D-S2）。
- `NumericValidationRuleCatalog` 并入 `sim.anchor`/`sim.scenario` 三项检查，新增"仿真"分组
  （E-M1，推翻 N6 收尾"目录不收录"裁定）；`SimScenarioValidationRule` 新增
  `sim_scenario_bandwidth_key_unknown` 警告（E-S2）；目录总数 20→24（阻断 15 + 警告 9）。
- `skill.budget_rule.beat_seconds`/`periodic_time_discount` 明确为记账常数，离散模式不要求整数
  （C-S3，文档）。
- `prog.level_curve.talent_points` 明确框架只登记累计、运行期无消费入口（D-S1，文档）。
- 仿真侧调用点（`CoverageSimulation`/`GrowthSimulation`/`StandardPlayerBuilder`）改用修复单 2
  新增的接受预构建 `StatBudgetInfo` 的重载，循环外构建一次，避免装备预算相关热路径重复扫描
  `stat.definition`/`stat.weight`/`stat.rating_conversion` 三张表（整合项 2，数值零差异）。

### 新增

- `EffectContext.EffectEntryIndex` 与配套构造重载；`CastPipeline.GetCastingRemaining`。
- `IBudgetSolver.Solve`/`EquipmentScoreAnalyzer.Score` 接受预构建 `StatBudgetInfo` 的重载。
- `CreatureDeathLootListener` 接受 `ISummonHost?` 的构造重载；共享辅助 `SummonCreditResolver`。
- `CoverageOutlierRow.SortByDeviationDescendingThenById`；
  `Core.Sim.SimBandwidthKeys.KnownKeys`；
  `AnchorTableSkillBudgetAnchorProvider.ResolvedStandardPlayer`。
- `core/sim/tests` 补品质骰结果偏离模板默认品质对武器秒伤/一场战斗累计伤害的对照测试（B 报告
  测试覆盖缺口 2，整合项 3）——嵌入数据集新增测试专用技能/优先级表，仅供该用例使用。

### 文档

- 新增 `docs/升级指南/`（`README.md` 索引 + `1.29.0到1.37.0-数值设计专项.md`）——覆盖数值
  设计专项 N0～N6 七个版本（1.30.0～1.36.0）的跨版本升级路径与检查表，与 `CHANGELOG.md` 各版本
  "迁移说明"（记单版增量）互补；根 `README.md`、`architecture/13_新游戏接入指南.md` 已加一句
  指向。
- 性能基线用例机器归一化口径（不改本机门槛）：`core/gameplay/tests/Perf/PerfBaselineTests.cs`
  四条性能基线用例的阈值比较改为按运行机器的参考负载耗时归一化——新增
  `Tests.Gameplay.Perf.PerfMachineCalibration`（确定性、与被测代码无关的固定工作量，取 5 次运行
  中位数，进程内只测一次并缓存）；机器系数 = 本机参考负载耗时 ÷ `perf_baseline.json` 新增字段
  `reference_workload_ms`（记录基线时基线机的同一参考负载耗时），限幅 [1, `calibration_factor_max`
  （当前 8）]；四条用例的有效阈值 = 原阈值 × 机器系数，基线机上系数 ≈ 1，与改动前等价。四条用例
  无论成败都通过 `ITestOutputHelper` 输出一行诊断（用例名/中位数/阈值/系数/归一化后阈值/参考负载
  耗时）；`check.ps1` 的 `dotnet test Core.sln` 步骤改用 trx logger 落盘结果、跑完后从 trx 中挑出
  这四行显式回显到 CI transcript，不改变该步骤对其余全部用例的输出量。背景：1.36.0 发布工作流的
  `TickCost_FullPipeline_MedianOfSampledTicks_WithinBaselineThreshold` 曾在共享 runner 上因机器
  繁忙连续两次误报"超阈值"（实测中位数约为基线阈值 1.43～1.44 倍，第三次重跑通过，本机对比确认无
  真实回归），本次改动让阈值判定本身吸收这一类机器速度差异，不再单纯依赖重跑。架构文档勘误：
  `architecture/11_工程规范与测试.md` 第 6 节"性能基线"行补充归一化口径说明（版本号不变，见该
  文档变更记录）。详见 `core/gameplay/tests/Perf/README.md`"机器归一化口径"节判断记录。托管运行
  器实测见 Perf README。
- `architecture/04_数据与内容管线.md`/`architecture/06_规则层_属性技能战斗AI.md` 正文勘误：
  `prog.level_curve.talent_points` 补一句"框架只登记与加载期校验，运行期无消费入口，天赋点
  余额/发放/存档由接入方自行实现"（D-S1，此前只在 `core/numbers/progression/README.md` 一处
  判断记录里说明，正文未同步）。

### 迁移说明

- 技能预算下界生效后，既有内容可能新增警告（处理方式：校正数值，或补 `budget_note` 说明设计
  意图）。
- 击杀掉钱归属变化：`CreatureDeathLootListener` 现核对击杀者身份并解析召唤物归属，非玩家击杀
  （或无法归属的召唤物击杀）不再入账落地。
- 升级回满行为修正：逐级升级路径的最后一级现在先聚合成长再发布 `LevelUpEvent`，回满时机可能
  与此前版本存在细微差异（结果更准确，不是回退）。
- 武器秒伤按装备实例真实品质计算（此前按模板默认品质）——掉落三次掷骰产出的常规装备，武器
  秒伤与一场战斗内 `weapon_damage_pct` 类技能的伤害数值可能因此变化（数值更准确，不是回退）。
- 数值类校验目录计数由 20 行变为 24 行（新增仿真分组三项 + `sim_scenario_bandwidth_key_unknown`
  一项警告），若编辑器一侧按目录行数或分组名单做了硬编码假设，需要同步更新。

### 编辑器接入建议

- 若编辑器内置了数值类校验目录的本地缓存/展示（如按分组渲染检查项列表），需要跟进本版新增
  的"仿真"分组三项检查与 `sim_scenario_bandwidth_key_unknown` 一项警告，目录总数由 20 行改为
  24 行（阻断 15 + 警告 9）。
- 若编辑器展示武器秒伤/技能伤害预览，建议改用装备实例真实品质（`ItemInstance.Quality`）而不是
  模板默认品质，避免预览值与运行期实际结算不一致（B-M1 修复前两者恒等，修复后可能出现偏差）。

## [1.36.0] - 2026-09-16

MINOR 版本：数值设计落地阶段 N6"数值仿真"（[数值设计分阶段落地计划](architecture/落地计划/数值设计分阶段落地计划.md)第 12/14 节，T-N6-1～T-N6-8b）——落地 [ADR-0035](architecture/adr/0035-数值仿真骨架为框架交付物.md)：新增 `core/sim`（无头运行器装配根从测试程序集上提为可发布模块、`sim.scenario`/`sim.anchor` 两表、标准玩家生成器、三级仿真——战斗仿真/成长仿真/内容覆盖仿真、统计量快照与带带宽比对器的基线比对、命令行入口 `toolchain/simrunner`）；`core/sim/tests/data` 嵌入式最小仿真数据集（拍板 10）跑通对账等式、越级矩阵形状、成长四条轨迹、内容覆盖离群值探针四类验收；接入 `check.ps1`/CI/发布分发清单；文档收尾（本计划末节"落地进度记录"新增 N6 记录、06/05 移动速度属性 id 勘误、遗留待确认判断记录改写为明确结论、编辑器产品文档 sim 域口径说明）。

回放/Perf 基线：零改动，原因——`core/gameplay/tests/Replay`/`Perf` 只装配 L2 `RulesAssembly`，本阶段全部改动（新模块 `core/sim`、`toolchain/simrunner`、`check.ps1`/`.github/workflows/ci.yml`/`build.ps1` 分发清单、文档）均落在新模块与工具链/文档层，不触达该装配路径；`dotnet test Core.sln` 全量回归（4249 例）与 `Tests.Gameplay` 的 `ReplayBaselineTests` 全绿，各任务提交时逐一复核，见下方各条目与该计划末节"落地进度记录"N6 一节。

提交链：`d6b4bd0`（T-N6-1）、`1bb18b5`（T-N6-2a）、`209970b`（T-N6-2b）、`aa6d96a`（T-N6-3a）、`7d4ca34`（T-N6-3b，`n6/creature-level` 并行分支）、合并 `9517e9c`（`n6/creature-level`：T-N6-3b `CreatureFactory.Spawn` 等级覆盖与 `creature.tier_definition.gold_multiplier` 接入掉钱公式，`--no-ff` 合入本分支）、`235a120`（T-N6-4）、`c7aee6c`（T-N6-4b，设计层复核根治两处根因）、`27f5260`（T-N6-5）、`db34812`（T-N6-6）、`29315f7`（T-N6-7a，根治示例数据零警告不变量）、`522df64`（T-N6-7）、`96ee4cb`（T-N6-8a：落地进度记录、06/05 勘误、遗留待确认判断记录改写为明确结论、编辑器产品文档口径说明、1.36.0 变更记录）、本笔提交（T-N6-8b：复核建议修复——`adapters/headless/README.md`/`toolchain/registry/manifests/adapter-headless/README.md`"数值仿真骨架"示例代码按真实签名重写并配套测试守护、`BaselineComparer` 容差解析改为叶子名精确匹配显式映射表），分支 `n6/sim`，合并提交 `2939a36`（`--no-ff` 合入 `main`），发布提交由 `build.ps1 -Release 1.36.0` 产生，见标签 `v1.36.0`。

### 新增

- **数值设计落地阶段 N6 · T-N6-1**（[ADR-0035](architecture/adr/0035-数值仿真骨架为框架交付物.md)
  决策 1）：新增 `core/sim`（`Core.Sim`/`Tests.Sim`，已加入 `Core.sln`）——数值仿真骨架无头运行器的
  装配根 `Core.Sim.HeadlessWorldBuilder.Build(HeadlessWorldOptions): HeadlessWorld`，把此前分散在
  `Tests.Gameplay.EndToEnd.GameWorldFixture.Build` 里"组装一整套 L0～L4 世界"的逻辑上提为框架交付物
  （事件目录/总线、`DataRegistry` 装载、`RngHost`、`WorldSim`、`StubSpatialQuery`、存档系统、
  `SimClockHost`、`GameplayAssembly`、玩家单位注册）；数据来源经 `HeadlessWorldOptions.DataSources`
  注入，装配根本身不含任何读仓库磁盘路径的逻辑。`GameWorldFixture.Build` 已改为调用该装配根，
  `Fixture` 类型与全部公开字段/方法签名不变，`Tests.Gameplay` 既有用例无改动（仅 `Tests.Gameplay.
  csproj` 新增对 `Core.Sim.csproj` 的引用）。`Core.Sim` 依赖 `Core.Gameplay` 与 `Adapters.Stub`，
  不依赖任何 `Tests.*` 程序集或 `Presentation.Common`。新增确定性证明测试
  `Tests.Sim.DeterminismTests`（同一数据源、同种子两次独立 `Build` 各跑一遍固定战斗脚本，逐 tick
  比对事件流与玩家/目标关键状态完全一致；另有不同种子在命中判定上产生可观测差异的对照用例）。
  `build.ps1` 同步进引擎工程的核心程序集清单是显式列出的五个层，不含 `Core.Sim`，未受影响；
  `dist/` 打包与对应 `check.ps1` 门禁步骤留给 T-N6-7。架构文档勘误（均为把 ADR-0035 决策 1 已拍板
  结论同步进目录约定正文，不新增结论）：`architecture/11_工程规范与测试.md` 第 1 节目录树补
  `core/sim/` 一行，并为"`core/` 下的模块不得引用 `adapters/`"这条规则补充唯一例外；
  `architecture/01_分层与依赖.md` 第 4 节"实现级共享目录"同步补 `core/sim` 一行。
- **数值设计落地阶段 N6 · T-N6-2a**（[ADR-0035](architecture/adr/0035-数值仿真骨架为框架交付物.md)
  决策 4）：`core/sim` 新增 `schema/`（`SimSchemas.cs`/`SimValidationRules.cs`/
  `SimSchemaCatalog.cs`）——`sim.anchor`（每级期望 HP/DPS/TTK/TTD/期望装备等级 E(L)/目标每级时长
  T(L)/期望击杀间隔 G(L)/任务探索占比 Q(L)，数值总纲第 4.1 节）与 `sim.scenario`（仿真场景：双方、
  等级、分档、次数、种子、对照锚点与带宽）两张表的 `TableSchema` 声明、专属表级校验规则
  `SimAnchorValidationRule`（等级从 1 连续无缺口无重复 [阻断]、`expected_item_level` 沿等级单调
  不减 [警告，不可提升]）与 `SimScenarioValidationRule`（`kind` 专属等级覆盖字段条件必填、
  `opponent.level`/`level_offsets` 二选一）、统一注册入口 `SimSchemaCatalog.RegisterAll`；`core/`
  新增类型化只读读取 `AnchorTable`（`MaxLevel`/`TryGet`/`Get`）与
  `ScenarioCatalog`（`All`/`TryGet`/`Get`/`ByKind`）。`SchemaLayer` 新增 `Sim` 成员（04 第 1.1 节
  "层"列原文"框架工具（无头仿真）"，不对应 01 文档任一层，纯新增不删改，非破坏性变更）。
  `HeadlessWorldBuilder.Build` 在 `GameplaySchemaCatalog.RegisterAll` 之后追加
  `SimSchemaCatalog.RegisterAll`，`HeadlessWorld` 新增 `AnchorTable?`/`ScenarioCatalog?` 两个可空
  属性（数据根未提供对应数据时为 `null`，不抛异常）——两表仍"仅无头仿真与内容工具读取，运行期
  宿主不读"，不进 `GameplaySchemaCatalog`/`PresentationSchemaCatalog`。`Presentation.Assembly.
  ContentValidationOptions` 新增可选属性 `ExtraSchemaRegistration`（`Action<IDataRegistry>?`，ABI
  兼容的纯新增属性），供 `toolchain/validator`（`--data-root` 校验与 `--schema-audit` 两条路径）与
  `Tests.PresentationCommon`（`NumericValidationRuleCatalogTests`，因 `data/_sample` 新增 `sim/`
  域而需要接入）接入 `SimSchemaCatalog.RegisterAll`；`toolchain/validator/Validator.csproj`、
  `Tests.PresentationCommon.csproj` 新增对 `Core.Sim.csproj`（含 `Adapters.Stub.csproj`）的引用
  （源码树/独立发行包二选一分支，同 `Presentation.Common` 既有惯例；独立发行包分支当前缺
  `lib/Core.Sim.dll`/`lib/Adapters.Stub.dll`，`build.ps1` 未打包，已知缺口留给 T-N6-7）。新增示例
  数据 `data/_sample/sim/{sim.anchor,sim.scenario}.json`（五级锚点 + 一个 arena 场景）与
  `games/_template/data/game/sim/{sim.anchor,sim.scenario}.json` 空壳表（`rows: []`）。新增测试
  `Tests.Sim.{SimSchemaTests,AnchorTableTests,ScenarioCatalogTests,HeadlessWorldBuilderSimTests}`
  （25 例：schema 正反例、类型化读取、`HeadlessWorldBuilder` 用 `data/_framework`+`data/_sample`
  构建后 `AnchorTable` 5 行/`ScenarioCatalog` 1 个场景）。架构文档勘误：`architecture/
  04_数据与内容管线.md` 第 5 节数值类校验项分级表新增三行检查登记
  （`sim_anchor_level_continuity`/`sim_anchor_expected_item_level_monotonic`/
  `sim_scenario_level_coverage_required`），不改变既有检查项判定逻辑。
- **数值设计落地阶段 N6 · T-N6-2b**（[ADR-0035](architecture/adr/0035-数值仿真骨架为框架交付物.md)
  决策 2～6；拍板 10）：新增嵌入式最小仿真数据集 `core/sim/tests/data/`（45 张表、321 行，清单/
  锚点推导公式与手算表/判断记录见该目录 `README.md`）——一套自洽、可被 `Core.Sim.HeadlessWorldBuilder`
  装配、可跑通一场最小战斗的内容数据（等级 1～20 连续、四主属性+派生+评级换算、单职业
  `arch.class.sim_warrior`、5 个技能覆盖主要攻击/高伤终结技/自身增益/消耗资源四类角色、5 档普通怪
  + 1 档精英、5 档物品等级×2 品质×5 槽位共 50 件装备模板、掉落/经济/世界/阵营/本地化配套表、
  `sim.anchor` 1～20 级连续、`sim.scenario` 三条场景各覆盖 arena/growth/coverage 一种）——拍板 10：
  只嵌入 `core/sim/tests/data/`，不放进 `data/_sample`（该处仍只保留两张表的 schema 覆盖样例）。
  `Tests.Sim.SimTestWorldFactory` 新增 `BuildFromEmbeddedDataset(ulong seed, int playerLevel = 1)`
  （数据根 = `data/_framework` + `core/sim/tests/data`）与 `RunEmbeddedFightScript`（学技能→生成
  1 级普通怪→连续施放直至一方死亡的固定脚本，供功能性与确定性双重验证）；新增测试
  `Tests.Sim.EmbeddedDatasetTests`（7 例：装载零阻断、`AnchorTable` 1～20 级连续、三个场景可取、
  生物/物品模板档位数量、标准职业 1 级学技能后战胜 1 级普通怪且玩家获胜、同种子两次逐 tick 完全
  一致）。本任务未改动 `core/` 下任何生产代码（`HeadlessWorldBuilder`/`AnchorTable`/
  `ScenarioCatalog`/`schema/` 均未触碰，无 C# 公开面变化），只新增内容数据与测试代码。
- **数值设计落地阶段 N6 · T-N6-3a**（[ADR-0035](architecture/adr/0035-数值仿真骨架为框架交付物.md)
  决策 2；06 第 405 行勘误"锚点表接入后两条预算校验规则按本节公式默认生效"）：`core/sim` 新增
  `ExpectedStatCalculator`（期望属性求值组件，数值总纲第 4.4 节公式：等级成长 + Σ非武器装备槽
  `IBudgetSolver` 预算反解，`statMix` 取该职业 `stat.weight` 归一化，对 `stat.definition` 全表求值、
  构造期缓存）、`AnchorTableSkillBudgetAnchorProvider`（`Core.Rules.Common
  .ISkillBudgetAnchorProvider` 的 `sim.anchor` 真实实现，惰性持有 `IDataRegistry` 引用/含
  `Func<IDataRegistry>` 工厂重载，`DataSourcesHaveAnchorRows` 静态预扫描按"行数 > 0"而非"表名存在"
  判定）、`StandardPlayerBuilder`（标准玩家生成器：`LearnFromBook` 填技能、每个装备位选最接近
  E(L) 的模板作载体并经 `IBudgetSolver` 对齐词缀反解向量后装备、校验 `RotationEvaluator` 能选出
  可施放技能，返回已学技能/已装备实例/期望与实际属性快照/偏差/武器秒伤的完整结果）。
  `Core.Rules.Assembly.RulesSchemaCatalog`/`Core.Carriers.Assembly.CarriersSchemaCatalog`/
  `Core.Gameplay.Assembly.GameplaySchemaCatalog`/`Presentation.Assembly.PresentationSchemaCatalog`
  各新增一个接收 `ISkillBudgetAnchorProvider?` 的 `RegisterAll` 重载（ABI：纯新增，既有重载逐位
  不变、均转发新重载并传 `null`）；`Presentation.Assembly.ContentValidationOptions` 新增可选属性
  `SkillBudgetAnchorProvider`。`Core.Sim.HeadlessWorldBuilder.Build`/`toolchain/validator/
  Program.cs` 两处接入点改为按"数据源是否真的含至少一行 `sim.anchor`"自动装配真实提供者（新增
  `HeadlessWorldOptions.ExpectedQualityId` 可选属性）；`toolchain/validator --json` 的
  `rules[].enabled` 字段不再对 `requires_anchor` 规则恒为 `false`，改为如实反映本次是否真的装配了
  提供者。副作用修复（不属于本任务功能范围，但锚点真实接入后触发）：`data/_sample` 自身也含
  `sim.anchor` 演示数据，接入后 `skill.sample_rest` 技能预算比值超硬上限，按框架既有"禁止阻断带
  说明的超模技能"规则给该技能补了一句 `budget_note`（唯一一处 `core/sim/` 之外的改动，只新增
  字符串字段，不改变任何技能数值，`dotnet test Core.sln` 4188 基线用例回归无副作用）。新增测试
  `Tests.Sim.{ExpectedStatCalculatorTests,StandardPlayerBuilderTests,AnchorProviderIntegrationTests}`
  （10 例：期望属性 L=1/10/20 与独立手算一致且按等级缓存、标准玩家生成器 L5/L15 全部装备位对齐
  反解向量/已学技能匹配技能书/按优先级表击杀同级普通怪、锚点接入后 `skill_budget_*` 规则确有真实
  求值、`data/_sample` 路径锚点接入后仍不阻断）。

- **数值设计落地阶段 N6 · T-N6-3b**（ADR-0035 决策 3；N4 遗留第 7 项，ADR-0034 决策 3 延伸）：
  `ICreatureFactory` 新增默认接口成员 `Spawn(templateId, mapId, position, facing, ownerId, int level)`
  （按指定等级出生，未覆盖时委托 5 参重载）；新增窄接口 `ICreatureLevelScaler.ScaleBaseStats`
  （core/carriers/creature 声明，供上层 core/sim 反向注入，未注入时"等级改、属性不变"）；
  `CreatureFactory` 新增对应构造重载与实现。`creature.tier_definition` 新增可选字段
  `gold_multiplier`（缺省 1），`CreatureFactory.TryGetGoldMultiplier` 查询；`RollContext` 新增
  `TierId`；新增委托 `Core.Gameplay.Loot.LootGoldMultiplierProvider`；`LootHost`/
  `CreatureDeathLootListener` 新增对应重载，`GameplayAssembly` 默认接线——08 第 7.4 节"怪物掉钱"
  公式的"分档倍率"自本版本起真正生效，不再恒为 1。
- **数值设计落地阶段 N6 · T-N6-4（含 T-N6-4b 根治，提交 `235a120`/`c7aee6c`）**
  （[ADR-0035](architecture/adr/0035-数值仿真骨架为框架交付物.md)
  决策 3）：三级仿真的第一级——战斗仿真运行器。`core/sim` 新增 `AnchorCreatureLevelScaler`
  （`ICreatureLevelScaler` 的锚点表实现，数值总纲第 4.2 节：血量槽位按 `DPS(L)×TTK(L)` 比值缩放、
  其余属性按 `HP(L)÷TTD(L)` 比值缩放，越界夹到 `AnchorTable.MaxLevel`/最低 1 级）、
  `SimpleMoveModel`（玩家侧简化移动模型；生物侧复用 `core/rules/ai` 既有 `AiHost` 追击状态机，
  未改动该模块）、`FightRunner`（单场战斗：标准玩家对生物模板按指定等级出生，逐 tick 智能释放
  优先级表直至一方死亡或超时；采样口径接 `combat.damage_dealt`（落地伤害/命中计数）与
  `Core.Rules.Combat.CombatOptions.ResolveTrace`（技能归属、含 Miss/Dodge/Immune 的完整尝试计数，
  该回调本就是既有诊断通道，本任务是第一次真正使用它，未改变结算逻辑）；输出 `FightResult`：
  胜负、时长、双方伤害与秒伤、命中率、技能输出占比、资源曲线（下采样 ≤64 点）、TTD 估计）、
  `ArenaSimulation`（场景运行器：对 `kind=arena` 场景按 `levels×level_offsets` 逐格跑 `runs`
  场并聚合为 `ArenaReport`——胜率/TTK 分布/对账表；种子按 `(base_seed,level,offset,runIndex)`
  纯函数确定性派生；`ArenaReport.ToJson()` 经 `Core.Foundation.Common.Json.JsonWriter` 确定性
  序列化）。隔离方案：每场 `FightRunner.Run` 各自新建一整套 `HeadlessWorld`（实测
  `HeadlessWorldBuilder.Build` 平均 10～25ms，远低于 50ms 判断线，不做"同一世界内重生重置"，
  不触碰任何被仿真模块的重置能力）。ABI 新增：`Core.Carriers.Creature.CreatureFactory` 的
  `_levelScaler` 私有只读字段改为公开可写属性 `LevelScaler`（两个既有构造函数行为不变）；
  `Core.Sim.HeadlessWorldOptions` 新增 `CombatOptions` 属性（转发给 `GameplayAssembly` 早已存在
  的同名构造参数，此前恒隐式传 `null`）。副作用修复（唯一触及 `core/sim/` 之外生产代码的改动）：
  `Core.Numbers.StatBlock.StatDefinitionConsumerValidationRule.FrameworkBuiltinConsumerStatIds`
  新增 `stat.move_speed`（与既有 `CombatOptions.ArmorStat` 等四项同一性质——C# 代码默认值消费，
  数据层扫描天然拿不到，只追加一份手抄清单里的一项，不改变规则判定逻辑）。数据集修复
  （`core/sim/tests/data/`，"生物会主动追击并攻击玩家"此前从未被验证过、实测确实不会，均为数据
  缺口不是框架缺陷）：`fac.reaction_matrix` 补 `fac.sim_hostile → fac.player = hostile` 反向行
  （原表只有玩家→生物单向，`Core.Numbers.Faction.FactionMatrix.GetReaction` 是方向性查找非对称
  矩阵）；`stat.definition` 新增 `stat.move_speed`（`Core.Carriers.Unit.MovementTickHandler
  .ResolveSpeed` 硬性要求该属性已登记，否则生物一旦产生位移意图就抛异常）+ 对应 `l10n.text`。
  数值调参（真跑 `sim.scenario.sim_arena_matrix` 后按数值总纲第 5 节对账等式与矩阵形状核算，详见
  `core/sim/tests/data/README.md`"T-N6-4 调参记录"）：`sim.anchor.dps`/`hp`/`ttk_seconds` 五个
  仿真等级（1/5/10/15/20）改取真实仿真实测值（此前 T-N6-2b 的简化手算忽略了 `rampage` 等技能，
  系统性低估约 1.5～2.3 倍），中间等级按相邻真实锚点线性插值；`sim.anchor.ttd_seconds` 改取真实
  仿真观测值（与驱动怪物伤害的内部设计常数解耦，两者定义不同，见该 README 判断记录）；
  `creature.template` 血量与 `skill.base_curve.sim_creature_bite{,_elite}` 伤害曲线按新锚点重算；
  `sim.scenario.sim_arena_matrix.runs` 从 20 提到 60（降低越级矩阵低胜率格子的抽样噪声）；带宽
  维持 0.25，未触达"≥0.5"上限。新增测试
  `Tests.Sim.{AnchorCreatureLevelScalerTests,SimpleMoveModelTests,FightRunnerTests,
  ArenaSimulationTests}`（21 例：等级缩放公式精确性与装配根接线、简化移动模型、单场战斗确定性/
  命中率/技能占比之和/资源曲线、完整 `sim_arena_matrix`（2100 场，~8～9 秒）的对账等式
  （dps/hp ≥3 个等级、ttd ≥1 个等级在带宽内）与矩阵形状（单调不增、越级胜率梯度、拐点存在）、
  `ArenaReport.ToJson()` 确定性）。
  同一功能验收过程中，设计层复核 T-N6-4 首次提交后要求根治两处根因（提交 `c7aee6c`），而不是
  放宽验收标准：① `Core.Sim.HeadlessWorldBuilder.Build` 此前给玩家按 `PlayerLevel > 1` 直接
  出生时，只调用 `Progression.RegisterUnit` 登记等级记账状态，未像
  `Core.Carriers.Creature.CreatureFactory.SpawnCore` 那样紧接着调用
  `Progression.ApplyGrowthToCurrentLevel` 补写"2 级到出生等级"的曲线成长——`IProgressionHost
  .RegisterUnit` 契约注释原文明确这第二步须由调用方自己完成；此前仓库内从未有调用方以非默认值
  （1）使用过 `HeadlessWorldOptions.PlayerLevel`，缺口因此从未暴露，直到 T-N6-4 的
  `ArenaSimulation` 第一次让玩家在等级 5/10/15/20 直接出生。现象：L20 标准玩家战斗中的
  `IPowerHost.GetPowerMax(Health)` 只有 150（1 级基础值），远低于应有的 ~2000，越级矩阵最高
  一行的胜率因此在错误的低生命基线上剧烈失真（非单调断层）。修复：`HeadlessWorldBuilder.Build`
  在 `RegisterUnit` 之后补调用 `Progression.ApplyGrowthToCurrentLevel` → `Powers.RecomputeMax`
  → `Powers.RefillAll`（均为 `core/rules`/`core/numbers` 已公开的既有成员，不新增任何公开
  成员、不改动"被仿真模块"一行代码），惠及任何以 `PlayerLevel > 1` 使用本装配根的调用方（不限于
  T-N6-4），`PlayerLevel == 1` 不受影响。② `sim.anchor`/`skill.base_curve.sim_creature_bite*`
  此前只到 20 级，`sim_arena_matrix` 允许玩家满级（20）时对手偏移 +5，生物出生等级达到 25，
  越界夹到 `AnchorTable.MaxLevel`（此前 20）导致玩家 20 级这一行的正偏移 +1/+3/+5 全部对上同一
  强度天花板——扩表到 25 级根治（`sim.anchor` 新增 21～25 五行，`dps`/`hp` 按 15→20 级斜率的
  5 倍延长；`skill.base_curve.sim_creature_bite{,_elite}` 新增 25 级断点）。两处修复后按数值
  总纲第 5 节方法论重新核算全部锚点/生物数值（`sim.anchor.dps`/`hp`/`ttk_seconds`/`ttd_seconds`、
  `creature.template` 血量、`skill.base_curve.sim_creature_bite*` 伤害曲线，详见
  `core/sim/tests/data/README.md`"T-N6-4 / T-N6-4b 调参记录"），`ArenaSimulationTests` 验收标准
  收紧为任务书原文的严格版本：DPS/HP 对账改为 5 个等级全部在带宽内（不再是 ≥3）；矩阵形状改为
  对场景定义的全部等级、每一个 ≥+3 的偏移点都要求胜率 ≤0.5（不再有"至少一个满足"的例外，
  T-N6-4 首次提交时的判断记录 26 已标注撤销）。`FightResult` 新增 `CreatureHitRate`（生物对
  玩家的命中率，口径同既有 `PlayerHitRate` 对称，根因排查需要的诊断字段，纯新增属性）。
  `skill.sim_creature_bite_elite` 新增 `budget_note`（精英分档伤害刻意高于 Monster 档预算带宽，
  是分档系统 1.5 倍强度的既定意图，不是数值手滑）。`EmbeddedDatasetTests
  .AnchorTable_HasAllTwentyFiveLevelsContinuous`（原 `…TwentyLevelsContinuous`）、
  `AnchorCreatureLevelScalerTests.Spawn_AboveMaxAnchorLevel_ClampsToMaxLevel`（越界夹取边界从
  "20 vs 25"改为"25 vs 30"）随锚点表扩容同步更新，`Core.Numbers.StatBlock
  .StatDefinitionConsumerValidationRule.FrameworkBuiltinConsumerStatIds` 补充判断记录说明
  `stat.move_speed` 的真正消费者是 `Core.Carriers.Unit.MovementTickHandler.ResolveSpeed`
  （`core/sim` 自己的 `SimpleMoveModel` 不读取该属性，此前汇报的归因不够精确，已在
  `core/numbers/stat_block/README.md` 更正）。`dotnet test Core.sln` 全量回归（4233 例）验证
  两处修复对既有全部用例无副作用。
- **数值设计落地阶段 N6 · T-N6-5**（[ADR-0035](architecture/adr/0035-数值仿真骨架为框架交付物.md)
  决策 3 后两级：成长仿真、内容覆盖仿真）：新增 `Core.Sim.GrowthSimulation.Run(ScenarioDef,
  AnchorTable, IReadOnlyList<IDataSource>, bool failOnUnknownTable = false): GrowthReport`——以
  `FightRunner`（经新增 `FightRunner.RunWithinWorld`/`FightRunner.FightAccumulator` 复用同一世界
  连打多场，见下）为输入，模拟标准玩家从 `level_from` 到 `level_to` 的整条成长曲线：真实战斗、
  真实击杀/任务当量经验（`IProgressionHost.GrantXp`，任务当量取 `Q(L)` 本身）、真实掉落（生物死亡
  自动触发 `CreatureDeathLootListener` 内部的 `LootHost.RollDetailed`，不重复掷骰）、真实经济
  入账/拾取/换装/出售（`IEconomyHost`/`EquipmentHost`/`EquipmentScoreAnalyzer` 评分决策）。输出
  每级实际时长/装备等级/命中率/金币累积四条轨迹与对照基准（时长/装备等级对
  `sim.anchor.level_duration_seconds`/`expected_item_level`；命中率对同等级独立战斗的 5 场均值；
  金币对 4.8 节公式解析推算——含出售掉落装备的期望收入估计，经 `EconomyPriceFormula
  .TryComputeBaseValue`）与偏离/带宽内判定。新增 `Core.Sim.CoverageSimulation.Run(ScenarioDef,
  AnchorTable, IReadOnlyList<IDataSource>, bool failOnUnknownTable = false): CoverageReport`——对
  数据集每个技能（`SkillBudgetAnalyzer` 预算比值，`Tier=Player` 且含伤害效果的额外做"只用该技能 +
  填充技能"单技能秒伤占比实测）、每件非武器装备（`EquipmentScoreAnalyzer`/预算消耗比，额外做同种子
  基准装 vs 换单件的秒伤边际变化实测）、每个生物模板（同级标准玩家 1v1 的 TTK/TTD 与锚点偏离）
  输出三张按 `|偏离|` 降序排列的离群值表（技能表第一、装备表第一，不做跨类别合并总表，见类型
  判断记录"探针位次验收口径"）。`FightRunner` 新增公开类型 `FightRunner.FightAccumulator`（把原
  `Run` 内部的命中/伤害计数闭包上提为可复用状态）与公开方法 `FightRunner.RunWithinWorld`（在已
  装配好的 `HeadlessWorld` 内针对已生成的生物打一场，供成长/覆盖仿真在同一世界连打多场；`Run` 本身
  改为"建世界+生成标准玩家+生成生物"后转发调用，行为不变，ABI 纯新增）。`StandardPlayerBuilder
  .ComputeExpectedContribution` 拆出 `ComputeTemplateStatsLiteral`/`ComputeAffixContribution`
  两个 `internal` 子步骤（供 `GrowthSimulation`/`CoverageSimulation` 复用词缀反解口径，行为不变，
  仅可见性从 `private` 放宽到 `internal`，不构成 ABI 变化）。`HeadlessWorldOptions` 新增属性
  `LootOptions`（转发 `GameplayAssembly` 既有的 `lootOptions` 构造参数，成长仿真借此把
  `PickupRange` 放宽到覆盖战斗交手距离，ABI 纯新增，默认 `null` 时行为不变）。
  联动重算（本任务遗留自 T-N6-4b 的已知不一致，见该处 README 判断记录）：`prog.level_curve
  .sim_warrior.entries[].xp_to_next`（1～19 级）按 T-N6-4b 校准后的 `sim.anchor`
  （`ttk_seconds`/`level_duration_seconds`/`kill_interval_seconds`/`quest_share`）与数值总纲 4.7
  节公式重新核算（417/477/540/606/673/752/834/919/1008/1101/1190/1282/1376/1473/1572/1673/
  1776/1882/1989，20 级仍为满级 0）；`prog.xp_base_curve.sim_default` 本身经复核后确认与公式一致，
  未改动。数据补齐（供成长仿真使用，均为新增/追加，不改既有条目取值）：`loot.table.*`（5 档普通怪）
  各追加 `chest`/`legs`/`feet` 三条 common 品质条目（此前只有主手+头两槊位掉落，成长仿真需要全部 5
  个装备槊位都有机会被替换）；同一批 `loot.table.*` 里全部货币条目的 `count_range` 由
  `{min,max}≈{金币基数(L)-2, 金币基数(L)+2}` 改为常量 `{1,1}`（判断记录——发现 `LootHost
  .ResolveCurrencyOutcome` 的真实公式是"当量（count_range 掷骰所得）× 金币基数(L)"，此前的
  `count_range` 取值把"金币基数(L)"本身当成了当量的取值范围，导致击杀掉钱是设计意图的约 5 倍，
  联调测试实测到该问题后按 4.8 节字面公式"无当量项"改为常量 1，即掉钱 = 金币基数(L)）。新增
  两条覆盖仿真探针数据（不进任何掉落表/技能书/优先级表，只作为全表扫描的数据行存在，验收覆盖仿真
  离群值排序）：`skill.def.sim_probe_overbudget`（`Tier=Unattributed`，预算比值 85.23，已填
  `budget_note`）、`item.template.sim_probe_underbudget`（L20 common 胸甲，仅 1 点耐力，预算
  消耗比 0.3%，触发 `item_budget_utilization_low` 警告——该检查项 `NonEscalatable`、无
  `budget_note` 式确认字段，04 第 5 节原文即以此项为"抓意图不抓手滑"的示例，本条属于刻意的
  设计意图，如实记录不做抑制）。新增测试 `Tests.Sim.{GrowthSimulationTests,
  CoverageSimulationTests}`（11 例：完整 `sim_growth_full`/`sim_coverage_all` 场景运行+计时、
  四条轨迹逐级带宽内、经验/金币双路径（`GrantXp`/`Economy.Add` 返回值求和 vs 事件流求和）互证、
  同种子 `ToJson` 确定性、不同种子产出不同、探针分类第一位、联动重算与公式独立核对）；
  `EmbeddedDatasetTests.ItemTemplate_CoversAllLevelTiersQualitiesAndSlots` 排除新增探针后保持
  既有"档位×品质×槽位"网格断言不变。`dotnet test Core.sln` 全量回归（4244 例，4233 基线 + 11
  新增）验证无副作用；`toolchain/abi_probe.ps1` 针对基线 1.35.0 确认 `breaks=0`（`Core.Sim` 不在
  `dist/` 打包清单内，本任务全部改动实际不影响可探测的 ABI 表面）。
- **数值设计落地阶段 N6 · T-N6-6**（[ADR-0035](architecture/adr/0035-数值仿真骨架为框架交付物.md)
  决策 5：统计量快照、基线 JSON、带带宽比对器、差异报告、命令行入口）：`core/sim` 新增
  `SimReport`（统一信封：场景 id/kind/`ComputeDatasetFingerprint`——对全部已加载表/记录排序后
  FNV-1a 64 位累加，算法选型同 `WorldSnapshot.Capture` 同款、独立实现/框架版本字符串/种子/
  `runs_override?`/`bandwidths`/`stats: IReadOnlyList<SimStat>`——`path`/`value`/`anchor?`/
  `deviation?`/`level?`/`raw_report`，`ToJson()` 确定性序列化；三个工厂方法
  `FromArenaReport`/`FromGrowthReport`/`FromCoverageReport` 把既有三类报告拍平为统一形状）、
  `SimBaseline`（既往快照最小形状 `{schema_version, scenario_id, kind, dataset_fingerprint,
  generated_with_version, seed, stats:{path:value}}`，只做 `FromReport`/`ToJson`/`Parse`，不做
  磁盘路径解析）、`BaselineComparer`（`BaselineDiffStatus`
  `Same`/`Within`/`Exceeded`/`Added`/`Removed` 五态；容差来源优先级"场景 `bandwidths` 同名统计量
  （叶子名子串匹配）> 默认相对容差 1%（`BaselineCompareOptions.RelativeTolerance`，基线值接近 0
  时改用绝对容差）"；全程 `<`/`<=` 阈值比较，不出现浮点精确相等，`ExactMatchEpsilon` 可配置）。
  `ScenarioDef` 新增公开方法 `WithRuns(int): ScenarioDef`（ABI 纯新增，仅替换 `Runs`，供命令行
  `--runs` 快速冒烟覆盖使用）。新增命令行入口 `toolchain/simrunner`（`SimRunner.csproj`，已加入
  `Core.sln`，工程惯例照抄 `toolchain/validator` 的 `lib/` 分发分支）：`run --scenario <id>|all
  --framework-root <dir> --data-root <dir>... --out <dir> [--baseline-dir <dir>]
  [--update-baseline] [--json] [--runs <n>] [--version <str>]`，退出码 `0`=无
  `Exceeded`/`Removed`、`1`=存在、`2`=参数错误/数据装载阻断、`3`=传了 `--baseline-dir` 但基线
  文件不存在且未传 `--update-baseline`（独立于其余三种退出码，见 `Program.cs` 判断记录）；控制台
  每场景一行摘要 `scenario=<id> kind=<k> stats=<n> exceeded=<n> added=<n> removed=<n>
  result=PASS|FAIL`，末尾 `RESULT=OK|FAIL`，供 `check.ps1`/CI 直接判读。新增薄封装
  `toolchain/sim_baseline.ps1`（`-Scenario`/`-UpdateBaseline`/`-Out`/`-BaselineDir`/`-Runs`/
  `-Version`/`-ArtifactsPath`）。新增并提交三份基线
  `core/sim/tests/baseline/{sim_arena_matrix,sim_growth_full,sim_coverage_all}.json`（对应嵌入
  数据集三个场景，`SimBaseline.ToJson()` 格式）。**根治缺陷（T-N6-5 遗留，本任务基线比对首次
  验证出）**：`CoverageSimulation` 三处（`AnalyzeSkills`/`MeasureItemMarginalImpact`/
  `AnalyzeCreatures`）此前用 `string.GetHashCode()`（.NET 逐进程随机化哈希）派生仿真种子，导致
  同一 `base_seed`、同一份数据在不同进程里跑出不同结果，与仓库通篇"同种子确定性"矛盾；改用
  确定性的 FNV-1a 32 位字符串哈希 `CoverageSimulation.StableIdHash`。新增测试
  `Tests.Sim.BaselineComparerTests`（5 例：三场景同种子独立重跑两次 `ToJson` 逐字节相同且
  `BaselineComparer` 全部 `Same`；内存覆盖 `arch.class.sim_warrior.base_stats.stat.strength`
  重跑 arena 后至少一条 `Exceeded`、`player_max_health` 不受影响；1e-12 量级相对扰动分类为
  `Within`——浮点比对无精确相等；`SimBaseline` 经 JSON 往返后比较仍全部 `Same`；`SimReport.ToJson()`
  字段完整性）。`dotnet test Core.sln` 全量回归（4249 例，4244 基线 + 5 新增）验证无副作用；
  `toolchain/abi_probe.ps1` 针对基线 1.35.0 确认 `breaks=0`。本任务不改
  `check.ps1`/`ci.yml`/`build.ps1`/`adapters/headless/README.md`，把 `simrunner` 接入门禁/CI/
  发布清单归 T-N6-7。

### 变更

- **数值设计落地阶段 N6 · T-N6-7（含 T-N6-7a 根治，提交 `29315f7`/`522df64`）**：先根治"示例数据
  `--strict` 零错误零警告"这条既有不变量与 T-N6-3a 锚点真实接入之间的回归冲突（T-N6-7a）——
  `data/_sample` 三条早于 N6 就存在的示例技能
  （`skill.sample_strike`/`sample_rest`/`sample_burst`）在锚点真实接入后产生 3 条
  `skill_budget_deviation` 警告，与
  `toolchain/tests/test_validate_data_summon_only_rule_default_wiring.py` 断言的字面
  `"errors 0, warnings 0"` 冲突。只把 `data/_sample/sim/sim.anchor.json` 的
  `sim.anchor.l1.dps` 从占位值 10 校准为 45（三者换算后新比值约 0.29/1.13/0.78，均落入
  `skill.budget_rule.default` 玩家档带宽 [0.80,1.20]），不改任何示例技能、不改测试；
  `skill.sample_rest`/`sample_burst` 的 `budget_note` 按"保留并说明"选项保留（记录历史设计
  意图，当前不被任何规则分支实际读取）。`python toolchain/validate_data.py --strict` →
  `errors 0, warnings 0`；`python -m pytest toolchain/tests -q` 全过；`dotnet test Core.sln`
  全过（4249 例）无副作用。详见 `core/sim/README.md`"T-N6-7 判断记录"40、`data/README.md` 勘误。
  随后（T-N6-7）：把 T-N6-6 落地的数值仿真报告与基线比对工具接入提交门禁、CI 与发布分发清单。
  `check.ps1` 新增"数值仿真基线比对"步骤（排在 `toolchain` 自身 pytest 之后、Unity 四步之前；
  直接执行步骤 1 已构建的 `SimRunner.dll`，不 `dotnet run` 重复编译；`-Quick` 下 SKIP——任务书
  硬性规则"禁止把数值仿真列为 `-Quick` 步骤"；`-SkipUnity` 不影响本步骤），全量步骤总数由 24
  变 25（`-Quick`/`-SkipUnity` 同样由 24 变 25——两者此前恰好都是 24 步，新增步骤对两者都生效，
  非只影响其中一种模式）；失败时打印全部 `*.diff.txt` 全文。`.github/workflows/ci.yml` 固定调用
  `check.ps1 -SkipUnity`，新步骤随之自动纳入，本文件本身不需要任何改动（不引入新的外部下载）。
  `build.ps1` 分发补齐：`Core.Sim.dll` 随 `Adapters.Stub.dll` 一起补进
  `dist/<ver>/adapters/headless/` 与私服包 `com.gamefoundation.adapter.headless` 的 `Lib~/`；
  `toolchain/simrunner` 预编译产物（`bin/`+`lib/`）随 `toolchain/` 打进 dist 与私服包
  `com.gamefoundation.toolchain` 的 `Tools~/simrunner/`；`toolchain/validator/lib/`（含对应
  UPM 包位置）补齐 `Core.Sim.dll`/`Adapters.Stub.dll`（T-N6-2a 判断记录 10 缺口消除）；
  `MANIFEST.txt` 新增 `[simrunner]` 段与 `[headless_assemblies]` 段 `Core.Sim.dll` 条目；
  `ws-game.lock` 的 `headless_dlls` 字段同步含 `Core.Sim.dll`（`New-WsGameLockObjectFromZip`
  一并更新，避免 release.yml 缺附件修复路径漏收）；"包清单一致性"步骤新增对应断言。
  `SimRunner.csproj` 的 `lib/` 回退分支根治：此前只列 3 个 `<Reference>`（编译期够用），缺
  Core.Sim.dll 运行期真正需要的 4 个传递依赖（Core.Numbers/Core.Rules/Core.Carriers/
  Core.Gameplay），独立发行包内编译能通过但运行会抛 `FileNotFoundException`，补全为完整 7 个。
  `toolchain/sim_baseline.ps1` 的 `--project` 路径改为相对脚本自身目录解析（原硬编码
  `"toolchain/simrunner"` 假设脚本恒常驻仓库根 `toolchain/` 下，随本包分发到 `Tools~/` 后会
  解析到不存在的目录）。`toolchain/abi_probe.ps1`/`toolchain/abi_surface` 新增 `Core.Sim.dll`
  加入比对程序集清单（归入表面差异比对，consumer 探针不涉及）；基线 1.35.0 无该程序集时按
  "新增程序集"处理（baseline 抽取环节容忍条目不存在、只记录说明并跳过，不 throw），全部类型
  行落入 `Additions` 而非 `Breaks`（`SurfaceCompareLogic.Compare` 既有语义，未新增特判分支）；
  实测 `toolchain\abi_probe.ps1 -BaselineZip dist\ws-game-1.35.0.zip` → `breaks=0 allowed=0
  additions=390`，`RESULT=OK`。文档同步：`adapters/headless/README.md`、
  `toolchain/registry/manifests/adapter-headless/README.md`（+`package.json` description）、
  `toolchain/registry/manifests/toolchain/README.md`（+`package.json` description）各补"数值
  仿真骨架"一节；根 `README.md` 门禁步骤清单、`toolchain/README.md`"`toolchain/simrunner`"
  一节、`core/sim/README.md`"T-N6-7 判断记录"、`architecture/11_工程规范与测试.md` 第 8 节
  提交门槛清单补一条（数值仿真基线比对是否列为强制门槛仍由游戏层决定，不改变第 6 节已有定位）。
  `dotnet build Core.sln -c Debug` 0 警告 0 错误；`dotnet test Core.sln` 全量回归（4249 例）无
  副作用（本任务未新增/修改任何生产代码的行为逻辑，只新增/扩容脚本与分发清单）。

### 修复

- **T-N6-4b（设计层复核根治，提交 `c7aee6c`）**：`Core.Sim.HeadlessWorldBuilder.Build` 此前给
  `PlayerLevel > 1` 的玩家出生时漏调 `Progression.ApplyGrowthToCurrentLevel`（等级成长曲线未
  补写），L20 标准玩家战斗中的生命上限只有 150（1 级基础值），越级矩阵最高一行胜率因此在错误的
  低生命基线上剧烈失真（非单调断层）；同批把 `sim.anchor`/`skill.base_curve.sim_creature_bite*`
  从 20 级扩表到 25 级，消除玩家满级对手偏移 +5 时越界夹取到同一强度天花板的假象。详见上方
  "新增"小节 T-N6-4 条目与 `core/sim/tests/data/README.md`"T-N6-4 / T-N6-4b 调参记录"。
- **T-N6-6（提交 `db34812`）**：`CoverageSimulation` 三处求值（技能/装备边际/生物）此前用
  `string.GetHashCode()`（.NET 逐进程随机化哈希）派生仿真种子，导致同一 `base_seed`、同一份
  数据在不同进程里跑出不同结果，与仓库通篇"同种子确定性"矛盾；改用确定性 FNV-1a 32 位字符串
  哈希 `CoverageSimulation.StableIdHash`。
- **T-N6-7a（提交 `29315f7`）**：`data/_sample/sim/sim.anchor.json` 的 `sim.anchor.l1.dps` 从
  占位值 10 校准为 45，消除锚点真实接入后三条早于 N6 就存在的示例技能触发的
  `skill_budget_deviation` 警告与既有"示例数据 `--strict` 零错误零警告"不变量的冲突，详见上方
  "变更"小节 T-N6-7 条目。
- **T-N6-8b（复核建议，本笔提交）**：`adapters/headless/README.md`/`toolchain/registry/
  manifests/adapter-headless/README.md`"数值仿真骨架"一节示例代码此前不可编译——
  `ArenaSimulation.Run(world.Options, scenarioDef)` 系凭空杜撰（`HeadlessWorld` 没有 `Options`
  属性，真实签名是 `Run(ScenarioDef, AnchorTable, IReadOnlyList<IDataSource>, bool)`）、
  `SimReport.FromArenaReport(arenaReport, scenarioDef, frameworkVersion: "1.36.0")` 缺
  `IDataRegistryView registry` 参数且具名参数拼错（应为 `generatedWithVersion`）；按真实签名
  重写示例，新增 `core/sim/tests/HeadlessReadmeExampleTests.cs` 逐字复刻该代码路径并接入
  `dotnet test Core.sln` 守护。`Core.Sim.BaselineComparer.ResolveTolerance` 容差解析此前按
  "叶子名子串匹配"（`leaf.IndexOf(bandwidthKey)`）关联场景带宽，审计发现该实现会把语义不相关的
  叶子名误配到带宽键（`growth.summary.cumulative_gold_via_balance`/`via_events` 两行因叶子名含
  子串 `"gold"` 被误配到 `growth` 场景 25% 带宽，削弱了这两行"两条记账路径应彼此一致"这一代码
  正确性核对本该有的敏感度）；改为 `BaselineCompareOptions.LeafBandwidthKeys` 显式精确映射表
  （默认表 `DefaultLeafBandwidthKeys` 覆盖全部现存叶子名，可整份覆盖），新增
  `BaselineComparerTests` 四条用例验证不再误配（`player_max_health`/含 `"_hp_"` 的技能 id 叶子）、
  已登记叶子名仍生效、三个场景同种子重跑仍 `exceeded=0`。详见 `core/sim/README.md`"T-N6-8b
  判断记录"记录 45/46。

### 文档

- **`architecture/落地计划/数值设计分阶段落地计划.md`**：末节"落地进度记录"新增
  "### 阶段 N6 · 2026-09-16 · 1.36.0"记录——小任务提交链、验收标准 1～7 逐项打勾（对账等式/
  越级矩阵形状/成长四条轨迹/内容覆盖离群值/基线差异报告/`check.ps1` 步骤/`adapters/headless/
  README.md` 口径一致）、阶段门禁 P1～P5、偏离首版基准的说明（逐条设计层裁定：采纳）。
- **`architecture/06_规则层_属性技能战斗AI.md`**：勘误——第 1 节"单机推荐属性分类"补一句，移动
  速度这一杂项属性的登记 id 为 `stat.move_speed`（框架默认消费者是移动系统，具体数值仍由游戏层
  决定）。版本号不变，ADR 列"—"。
- **`core/numbers/stat_block/README.md`**："属性无消费者"判断记录里遗留的一条疑难判断——是否
  要把 `stat.move_speed` 正式写进 06 文档——设计层裁定（2026-09-16）：采纳，已在本任务把
  `stat.move_speed` 写入 06 第 1 节，判断记录同步改写为明确结论。
- **`architecture/04_数据与内容管线.md`**：核对 §1.1 表清单 `sim.scenario`/`sim.anchor` 两行与
  §5 三条 sim 检查行——均已在 T-N6-2a 完整登记且与 `core/sim/schema/SimValidationRules.cs` 的
  检查名常量逐字一致，本任务核对后确认不需要改动。
- **编辑器产品文档**（`editor/docs/编辑器产品文档.md`/`.html`）：第 4.1 节"数值规则集中登记清单
  与校验报告分组"一行补一句说明——04 第 5 节分级表当前共 23 行（20 行数值域 + 3 行 T-N6-2a 新增
  的 `sim.anchor`/`sim.scenario` 表结构检查），后者按既定口径（04 表登记、`NumericValidationRule
  Catalog` 目录不收录）不计入本行所述的 20 行只读登记，编辑器如需展示 sim 域检查项目前需直接读
  04 或等待后续单独契约面。细节勘误，文档版本号不变（v2.16），变更记录表新增一行标注"v2.16
  （勘误）"。

### 迁移说明

- **无阻断性变化**：`core/sim`、`sim.anchor`/`sim.scenario`、`toolchain/simrunner` 均是"不使用即
  不受影响"的纯增量交付——不填这两张表、不跑 `simrunner` 的游戏，升级后现有内容、存档、既有 API
  行为逐位不变。
- **`sim.anchor`/`sim.scenario` 两表仅供无头仿真与内容工具读取**：运行期宿主不读，`games/_template`
  按约定留空壳（`rows: []`）；游戏层若不打算接入数值仿真，两表可以一直留空。
- **锚点表一旦提供真实数据（行数 > 0），三条此前被短路的预算校验立即生效**：`skill_budget_hard_
  cap_exceeded`（阻断）、`skill_budget_deviation`/`item_grant_value_exceeds_share`（警告，不可
  提升）此前因装配根 `anchorProvider` 恒为 `null` 而"整条规则不产生任何问题"（04"预算类校验的
  锚点依赖"段），示例数据零告警；填了 `sim.anchor` 后这三项会用真实数据重新核算，游戏层已有的
  技能/装备预算若从未被真正检查过，升级并填表后可能第一次冒出真实问题，需要评审并补 `budget_note`
  或调整数值，不是回归缺陷。
- **`creature.tier_definition.gold_multiplier`（缺省 1）/`ICreatureFactory.Spawn(...,int level)`/
  `ICreatureLevelScaler` 新增**：默认不改变现有掉钱与生成行为；游戏层需要"分档怪掉钱倍率"生效，
  需显式给对应分档记录填该字段。
- **`Core.Sim`/`toolchain/simrunner` 随构建产物分发**：`dist/<ver>/adapters/headless/`、
  `toolchain/simrunner/{bin,lib}/`、私服包 `com.gamefoundation.adapter.headless`/
  `com.gamefoundation.toolchain` 均新增对应文件；`MANIFEST.txt` 新增 `[simrunner]` 段与
  `[headless_assemblies]` 段 `Core.Sim.dll` 条目。
- **`check.ps1` 步骤数由 24 变 25**：新增"数值仿真基线比对"步骤（`-Quick` 下按硬性规则整体
  SKIP，全量/`-SkipUnity` 均真跑）；游戏层若复制了本仓库 `check.ps1` 骨架或对步骤总数/顺序有
  硬编码断言，需要同步更新。
- **`stat.move_speed` 正式写入 06 文档**：确认为框架默认移动速度属性 id（消费者是移动系统），
  是文档确认而非行为变化；游戏层若此前已用别的属性 id 承载移动速度，需要经
  `MovementOptions.MoveSpeedStat` 显式配置（此前即需要，本次不新增约束）。

跨版本升级见 [docs/升级指南/1.29.0到1.37.0-数值设计专项.md](docs/升级指南/1.29.0到1.37.0-数值设计专项.md)。

## [1.35.0] - 2026-09-16

MINOR 版本：数值设计落地阶段 N5"校验全集与编辑器契约面"（[数值设计分阶段落地计划](architecture/落地计划/数值设计分阶段落地计划.md)第 11/14 节，T-N5-1～T-N5-5）——落地 04 第 5 节数值类校验项分级表的全集核对与登记：补齐"属性无消费者"规则缺失的 Expr 表达式引用扫描（补缺规则实现）；新增数值规则集中登记清单与 `validator --json` 规则分组/锚点依赖标记；编辑器产品文档第 4.1/5.8 节契约面补齐并把 HTML 版落后 md 版九次更新的历史缺口一并回填，新增 md/HTML 一致性 pytest 门禁；核对表并入计划文档、04 第 5 节细节勘误、阶段 N5 落地进度记录归档。

回放/Perf 基线：零改动，原因——本阶段全部改动（校验规则补缺、集中登记清单、校验装配入口注册、`validator --json` 输出字段、编辑器文档、`toolchain` 测试）均落在校验/文档/工具链层，不触达 `core/gameplay/tests/Replay`/`Perf` 装配的 L2 `RulesAssembly` 运行期路径（校验期改动不触达）。各小任务门禁与逐项验收记录见该计划末节"落地进度记录"N5 一节。

提交链：`451991f`（T-N5-1）、`5167490`（T-N5-2）、`07e4d84`（T-N5-3）、`cbb6f1e`（T-N5-4）、`0448101`（T-N5-5 前半：编辑器产品文档 HTML 回填至 v2.16 与 md/HTML 一致性测试）、本笔提交（T-N5-5 后半：核对表并入计划文档、04 第 5 节勘误、落地进度记录、1.35.0 变更记录）、`6a2af80`（T-N5-5 复核维持：清理遗留待确认措辞），分支 `n5/validation`，`--no-ff` 合入 `main`（哈希 `b339943`）。

### 新增

- **数值设计落地阶段 N5 · T-N5-1**：新增 `architecture/落地计划/数值规则核对表-N5.md`——对照 04
  第 5 节数值类校验项分级表逐条核对 N1～N4 已落地规则，不涉及代码改动。契约疑点：04 当前文本实际
  为阻断 11 + 警告 7 = 18 概念条目（对应 20 行技术登记），多于计划文档估算的"十三条"，详见该核对
  表第 0 节；T-N5-5 已把该表内容并入 `数值设计分阶段落地计划.md` 末节"落地进度记录"N5 记录，原
  文件保留作工作产物。
- **数值设计落地阶段 N5 · T-N5-2**：`core/foundation/expr` 新增只读遍历入口
  `ExprReferenceCollector.Collect(ExprNode) -> IReadOnlyList<ExprReferenceNode>`（深度优先收集
  语法树里全部引用节点，含函数调用参数里嵌套的引用；不求值）与兜底 `PermissiveExprSchema`
  （`TryGetSignature` 对任意 `group.key` 恒真，只用于只读遍历场景，不用于内容加载或运行期求值）；
  均为纯新增公开类型，供编辑器等工具枚举一段 Expr 文本里出现过的全部 `group.key` 引用。
- **数值设计落地阶段 N5 · T-N5-3**：新增 `Presentation.Assembly.NumericValidationRuleCatalog`/
  `NumericValidationRuleDescriptor`（04 第 5 节数值类校验项分级表的只读集中登记，逐字段直接引用
  各规则类自己的 `RuleId`/`CheckName` 常量）与 `ContentValidationAssembly.NumericRules`（单一来源
  转发）；`toolchain/validator --json` 的 `rules[]` 新增 `category`/`group`/`check_names`/
  `requires_anchor`/`enabled` 五个字段（详见 `toolchain/README.md`）；新增两个此前只以字面量嵌入
  调用点、未公开的 `CheckName` 常量——`Core.Numbers.Progression.ProgLevelCurveValidationRule.
  CheckName`（`"level_curve_xp_monotonic"`）与 `Core.Rules.Combat.CombatResistCurveValidationRule.
  CheckEntriesMonotonic`（`"resist_curve_entries_monotonic"`），供集中登记清单按常量引用而非重复
  字面量。均为纯新增公开类型/成员，`toolchain/abi_probe.ps1` 核对 breaks=0。
- **数值设计落地阶段 N5 · T-N5-5**：新增 `toolchain/tests/test_editor_doc_consistency.py`——用
  标准库 `html.parser`（无第三方依赖）解析编辑器产品文档 md/HTML 两版的"契约面清单"表、"变更记录"
  表与文档头部版本号字符串，断言两版行标识集合与版本号一致，防止 HTML 版再次落后 md 版（随
  `toolchain` pytest 套件一并跑）。

### 变更（行为变更）

- **数值设计落地阶段 N5 · T-N5-2**：`stat_definition_no_consumer`（"属性无消费者"警告）的消费者
  来源判定从三条扩展为四条——`skill.def.use_condition`/`ai.rotation.condition`/
  `quest.def.prerequisite` 等任意表的 `Expr` 字段里 `self.stat(<属性 id>)`/`target.stat(<属性
  id>)` 引用现在也计入"已消费属性 id"（此前只覆盖跨表 Reference/SoftReference/Map 键引用与框架
  内置消费者清单）。行为变更方向单一：此前只靠表达式引用消费的属性会从"报告警告"变为"不再报告
  警告"，不会反过来产生新的警告。

### 文档

- **`architecture/04_数据与内容管线.md`**：第 5 节数值类校验项分级表引言补一句——本表落地为 20
  条独立规则（阻断 13 + 警告 7），其中"曲线单调有限"一个概念条目按表族拆成三条实现，技能预算三项
  依赖仿真锚点表（N6）接入后生效；"属性无消费者"一行的定义补上第四条已实现的消费者来源——任一
  表达式（Expr）字段中 `self.stat(<id>)`/`target.stat(<id>)` 引用。细节勘误，版本号不变。
- **`architecture/落地计划/数值规则核对表-N5.md`**：内容已并入 `数值设计分阶段落地计划.md` 末节
  "落地进度记录"N5 记录；原文件保留作工作产物，头部加说明指向该节。
- **编辑器产品文档**（`editor/docs/编辑器产品文档.md`/`.html`）：
  - T-N5-4：第 4.1 节契约面清单新增三行——技能预算分析 `Core.Rules.Skill.SkillBudgetAnalyzer.
    Analyze`/`ComputeGrantValue`（[ADR-0031](architecture/adr/0031-技能数值契约与预算.md) 决策
    2，T-N3-9）、装备评分 `Core.Carriers.Item.EquipmentScoreAnalyzer.Score`/`Compare`
    （[ADR-0032](architecture/adr/0032-装备预算消耗与词缀份额.md) 决策 9，T-N2-4）、数值规则集中
    登记与引用查找 `Presentation.Assembly.NumericValidationRuleCatalog`/`ContentValidationAssembly.
    NumericRules`/`toolchain/validator --json rules[]` 新字段/`Core.Foundation.Expr.
    ExprReferenceCollector`（T-N5-2/T-N5-3）；第 5.8 节"数值沙盘"表新增第六行"内容覆盖仿真离群值
    列表"并补一段契约意向说明（[ADR-0035](architecture/adr/0035-数值仿真骨架为框架交付物.md) 决策
    3/6，标注"待 N6"，仅先固定契约面意向，具体字段形态待阶段 N6 落地 `core/sim` 后回填）。文档
    版本 v2.15 → v2.16。均为纯文档新增，不改变第 4 章既有契约面签名，不涉及代码改动。
  - T-N5-5：HTML 版此前只同步到 md 版 v2.6（第 4.1 节仍是 7 行的旧表，"资源引用标识"/"掉落表期望
    概率分析"两行与 v2.7～v2.15 期间新增的其余契约面均未回填）——本任务把这段历史缺口连同 T-N5-4
    的三行一并回填：第 4.1 节契约面清单表（全部 12 行）、"变更记录"表（v1～v2.16 全部 19 行）与
    5.5.2（右侧结算预览 `ResolveTrace`）/5.5.4（掉落模拟 `LootTableAnalyzer.
    ExpectedProbabilities`）/5.5.8（本地化编辑器 `WarnOnMissingTranslation` 框架契约更新段）/
    5.7.5（schema 迁移改用 `SchemaMigrator.MigrateEnvelope`）/5.7.6（可选规则默认注册情况 +
    `OptionalRules`/`TryGetOptionalRuleByCheck`）/5.8（`ResolveTrace` 相关依据文案）六处 md 已
    更新但 HTML 未跟上的正文段，逐条对照 md 版对应版本的变更记录描述定位；`toolchain/tests/
    test_editor_doc_consistency.py`（3 例）随之全绿。`editor/README.md` 版本号与说明同步更新。
  - `CHANGELOG.md` 文首"编辑器相关契约"索引里 T-N5-2/3/4 三条已从 `（Unreleased）` 改标
    `（1.35.0）`，内容不变。

### 迁移说明

- **`stat_definition_no_consumer` 消费者判定扩展 Expr 扫描**：见上方"变更（行为变更）"——此前只靠
  表达式引用的属性会从"警告"变"不警告"，反向不会发生；按检查名/命中数断言"属性无消费者"告警数量
  的消费方需要重新核对基线。
- **`rules[]` 新字段向后兼容**：`toolchain/validator --json` 的 `rules[]` 新增
  `category`/`group`/`check_names`/`requires_anchor`/`enabled` 五个字段均为纯新增，既有字段不变；
  消费方若按 `rules[]` 数量或字段集合做精确断言（而非只读取已知字段）需要更新预期。

### 编辑器接入建议

- 编辑器"数值校验清单"一类面板可直接读 `ContentValidationAssembly.NumericRules`/
  `Presentation.Assembly.NumericValidationRuleCatalog.Entries`，按 `rules[].group` 增加"所属
  数值域"筛选维度，`requires_anchor`/`enabled` 供标注"该规则依赖 N6 锚点接入、当前不产出问题"，
  不必自行硬编码 04 第 5 节分级表这份清单。
- `ExprReferenceCollector` 供编辑器"引用图"与 Expr 编辑器"查找此字段全部引用"功能复用同一套只读
  遍历逻辑，不必自行重新解析语法树摘取引用节点。
- 编辑器产品文档第 4.1/5.8 节与 HTML 版已完整同步至 v2.16，`toolchain/tests/
  test_editor_doc_consistency.py` 作为长期门禁防止两版再次漂移；编辑器项目消费文档内容时可直接
  读任一版本，不必再核对哪个版本更新。

## [1.34.0] - 2026-09-16

MINOR 版本：数值设计落地阶段 N4"经验与经济"（[数值设计分阶段落地计划](architecture/落地计划/数值设计分阶段落地计划.md)第 10/14 节，T-N4-1～T-N4-11）——落地 ADR-0033 与 ADR-0034：Progression 契约与当量来源、击杀/探索经验发放主体、等级差经验系数、分档与难度倍率、升级回满、满级归零；价值曲线与价格公式、金币基数与货币掉落条目、货币入账与溢出事件、原子扣费、复活费不阻断、坐骑样例、进战下马策略项（拍板 9）。
回放/Perf 基线：零改动，原因——`core/gameplay/tests/Replay`/`Perf` 只装配 L2 `RulesAssembly`，本阶段全部改动（`ProgressionHost`/`progression_bridge`/`EconomyHost`/`LootHost`/`DeathPolicyHost`/`CombatOptions` 进战下马）均落在 L1/L4 模块与装配根，回放/Perf 场景不触达。各小任务门禁与逐项验收记录见该计划末节"落地进度记录"N4 一节。

提交链：`55515e0`（T-N4-1）、`1920f21`（T-N4-2）、`ef1f8d2`（T-N4-3）、`3feb0f9`（T-N4-6，`n4/econ` 并行分支）、`f20ebcc`（T-N4-7）、`0272883`（T-N4-8）、合并 `dbcf411`（`n4/econ`：T-N4-6/7/8 经济曲线与价格公式、掉落货币入账与溢出事件、`TryPay(reason)` 与事件登记，`--no-ff` 合入本分支）、`eb3d3fc`（T-N4-4）、`90550ea`（T-N4-9，`n4/econ` 并行分支）、合并 `04397ad`（`n4/econ`：T-N4-9 复活费策略与进战下马，`--no-ff` 合入本分支）、`f5680f8`（T-N4-5）、`97ba86a`（T-N4-10），分支 `n4/progression`，`--no-ff` 合入 `main`（合并提交哈希 `1d4d0ef`），本笔提交（T-N4-11 前半：示例经验来源 id 统一为监听器约定 id）与后续提交（T-N4-11 后半：阶段 N4 文档收尾——裁定落地、04/06/08 勘误、落地进度记录、1.34.0 变更记录），以及独立复核缺口修复提交 `46c9b2e`（T-N4-11 补充：商店展示层接入价格公式，`IEconomyHost.TryGetSellItemPrice`）。

### 新增

- **数值设计落地阶段 N4 · T-N4-1**：`prog.level_curve.talent_points`（每级天赋点数，缺省 0）；
  `prog.xp_source` 新字段 `kind`/`base_curve_ref`/`level_diff_ref`/`once_key`
  （[ADR-0033](architecture/adr/0033-等级经验模块正文与当量来源.md) 决策 3），旧字段
  `base_xp`/`weight` 标废弃（`base_curve_ref` 存在时优先，保留一个版本周期，拍板 4）；新表
  `prog.xp_base_curve`（击杀基数曲线：怪物/任务/区域等级 → 一只同级普通怪的基础经验值，落地
  改动点清单第 10 节拍板 5）；新增契约壳 `Core.Numbers.Progression.ProgressionOptions`
  （构造期口味配置，本版本只登记字段、`ProgressionHost` 尚未消费，消费实现留后续任务）。全部
  改动均为纯新增，两张既有表 `schema_version` 不递增，旧数据/旧存档不受影响；`prog.xp_base_curve` 在 04 第 1.1 节表清单的登记行此前缺失，已由 T-N4-11 补齐（见 04 同日
  勘误）；`kind` 登记为可选（非必填）、`base_xp`/`weight` 保留必填至下个版本周期到期——设计层
  裁定（2026-09-16）：采纳。`ProgressionOptions` 部分字段的最终消费方式留待后续任务。

- **数值设计落地阶段 N4 · T-N4-2**（[ADR-0033](architecture/adr/0033-等级经验模块正文与当量来源.md)
  决策 1/3/9；[06 第 2.5 节](architecture/06_规则层_属性技能战斗AI.md)，编辑器/游戏侧接入契约）：
  `Core.Numbers.Progression.IProgressionHost` 新增默认接口成员
  `GrantXp(unitId, sourceId, context: XpContext) -> long`（三种来源——`kind=kill|quest|discovery`
  ——的统一折算入口，返回实际入账值）；新增契约类型 `XpContext`（`sourceLevel`/`tierId`/
  `equivalent`）；`ProgressionHost` 新增接受 `ProgressionOptions?` 的构造重载（旧构造函数转发
  `null`，行为不变）。折算公式：击杀 = 基数曲线(怪物等级) × 分档/难度倍率钩子（缺省 1，真正接入
  留 T-N4-4）× 等级差系数(Δ)；任务 = 当量 × 基数曲线(任务等级) × 等级差系数(Δ)；探索 = 当量
  （缺省 1）× 基数曲线(区域等级)，刻意不接等级差系数。`GetXpToNext`/`AddXp`/`GrantXp`/
  `GrantFromSource` 统一按"有效满级"判定（`ProgressionOptions.MaxLevel` 为 0 时等于曲线自身
  `max_level`，为正值时收紧到该值与曲线 `max_level` 的较小者），满级时 `GetXpToNext` 返回 0、
  `grantXp`/`AddXp` 整笔丢弃不发 `progression.xp_gained`。`ProgressionOptions.MaxLevel` 默认值
  由 T-N4-1 的 `1` 改为 `0`（该字段此前是纯登记壳、默认值不影响任何判定；本任务开始真正消费，
  语义与默认值必须同时修正）。旧字段兼容：来源没有 `base_curve_ref` 时 `GrantFromSource` 逐位
  保留旧算法（`base_xp × weight × multiplier`，回归锁死）；存在 `base_curve_ref` 时无论走
  `GrantXp` 或旧入口 `GrantFromSource` 均以曲线为准。`GrantFromSource` 未删除。`kind` 未登记时兜底按 `kill` 处理——设计层裁定（2026-09-16）：采纳；`GrantFromSource` 走曲线
  分支时的隐式 `sourceLevel`、`prog.level_curve.talent_points` 消费不在本任务范围内，留待后续
  任务，详见 `core/numbers/progression/contracts/IProgressionHost.cs`/`XpContext.cs`/
  `ProgressionOptions.cs` 类型注释与 `core/numbers/progression/schema/README.md`"T-N4-2 补记"。

- **数值设计落地阶段 N4 · T-N4-3**（[ADR-0033](architecture/adr/0033-等级经验模块正文与当量来源.md)
  决策 3；[06 第 2.5 节](architecture/06_规则层_属性技能战斗AI.md)）：新模块
  `core/gameplay/progression_bridge`——击杀经验监听器 `CreatureDeathXpListener`（订阅
  `unit.died`，击杀者为玩家单位或其召唤物且主人是玩家时按 `prog.xp_source(kind=kill)` 经
  `IProgressionHost.GrantXp` 发放，来源等级取死亡单位等级，分档 id 从生物模板取出经
  `XpContext.TierId` 传递供 T-N4-4 消费）；探索经验监听器
  `AreaTriggerDiscoveryXpListener`（订阅 `area.trigger_entered`，区域/区域等级由调用方经新增
  委托 `AreaDiscoveryLevelResolver` 提供，一次性标志经 `IWorldState` 保证，键为
  `world.<once_key 前缀>.<触发器 id>`）。`Core.Numbers.Progression.ProgressionOptions` 新增
  `KillXpSourceId`/`DiscoveryXpSourceId`（两个可空 `Id?`，未配置时两个监听器分别退到约定 id
  `prog.xp_source.kill`/`prog.xp_source.discovery`）。`core/gameplay/assembly/GameplayAssembly.cs`
  接线两个监听器（只新增行）：击杀监听器接在既有 `CreatureDeathLootListener` 构造之后（先掉落后
  经验）；探索监听器接在 `AreaTriggerHost` 构造之后，`AreaDiscoveryLevelResolver` 当前传 `null`
  （框架不预设任何具体区域，零成本退化）。两个监听器调用 `GrantXp` 均以
  `try/catch (ArgumentException)` 包裹未登记来源 id 的情形，不阻断玩法流程。回放基线核查：
  `core/gameplay/tests/Replay` 只装配 L2 `RulesAssembly`、从未构造 L4 `GameplayAssembly`，本次
  改动零影响，基线未变。契约疑点（"区域"由触发器 id 承担、区域等级由委托而非新 schema 字段提供、
  两个来源 id 承接点的最终消费方式）详见
  `core/numbers/progression/contracts/ProgressionOptions.cs`/
  `core/gameplay/progression_bridge/README.md` 类型/模块注释"契约疑点上报"。

- **数值设计落地阶段 N4 · T-N4-4**（[ADR-0033](architecture/adr/0033-等级经验模块正文与当量来源.md)
  决策 4；[08 第 2.1 节](architecture/08_玩法层_掉落任务对话关卡.md) 2026-09-14 修订段；编辑器/
  游戏侧接入契约）：`creature.tier_definition`/`diff.tier` 各新增可选字段 `xp_multiplier`
  （经验倍率，缺省 1，仅 `kind=kill` 经验来源生效）；`Core.Numbers.Progression.
  ProgressionXpMultiplierProvider` 委托签名扩展为 `(unitId, sourceId, tierId) -> double`（T-N4-2
  落地时只有 `(unitId, sourceId)`，本任务补上第三个参数供分档倍率查询，见判断记录）；
  `Core.Gameplay.Assembly.GameplayAssembly` 从本任务起把
  `ProgressionOptions.ExtraXpMultiplierProvider`（未显式配置时）接为
  "`Core.Carriers.Creature.CreatureFactory.TryGetXpMultiplier` 结果 ×
  `Core.Gameplay.Difficulty.IDifficultyHost.XpMultiplier`"；`RulesAssembly`/`CarriersAssembly`/
  `GameplayAssembly` 三个装配根新增接受 `ProgressionOptions?` 的构造重载（ABI 门禁 G3：本仓库
  已发布 1.33.0 基线，三者旧签名构造函数均原样保留、转发新增重载并传 `null`，新增重载因 C# 语法
  "可选参数必须在必选参数之后"把此前全部可选参数一并改为必选，唯一调用点均已同步显式列出全部
  参数，见三者各自 README"T-N4-4"一节）。任务/遭遇奖励改当量与等级：`Core.Gameplay.Common.
  RewardBundle` 新增 `XpEquivalent`/`RewardLevel`（`rewards.xp_equivalent`/`rewards.level`，
  `QuestSchemas.RewardsFields` 同步登记，`quest.def`/`encounter.def`/`achv.def` 三表共用同一份
  声明自动生效），旧字段 `xp`（`rewards.xp`）标废弃但保留一个版本周期供兼容读取；
  `Core.Gameplay.Common.RewardDispatcher` 新增接受 `ProgressionOptions?` 的构造重载（供
  `ProgressionOptions.QuestXpSourceId` 解析，未配置落到约定 id `prog.xp_source.quest`），
  `GrantXp`（私有方法）：`XpEquivalent` 非空时改经 `IProgressionHost.GrantXp(unitId,
  questXpSourceId, new XpContext(rewardLevel ?? 1, equivalent: xpEquivalent))` 折算发放（硬性
  规则"禁止奖励直发绝对数"），为空时逐位保留旧字段 `Xp` 经 `AddXp` 绝对数直发的路径（回归锁死）。
  `Core.Numbers.Progression.IProgressionHost` 新增默认接口成员 `HasXpSource(sourceId): bool`
  （设计层裁定，附带任务）：`core/gameplay/progression_bridge` 两个监听器与 `RewardDispatcher`
  均改为先显式查询、查到才发，取代 `try/catch (ArgumentException)` 兜未登记来源的写法（行为对外
  不变，仍是"未登记静默跳过、不阻断"，只是不再依赖异常控制流）。回放基线核查：`core/gameplay/
  tests/Replay` 只装配 L2 `RulesAssembly`、从未构造 L4 `GameplayAssembly`，本次改动（分档/难度
  倍率接线、监听器改用 `HasXpSource`）均落在 L4 装配根与经验发放路径，回放场景不触达，基线未变、
  `--filter "FullyQualifiedName~Replay"` 全绿。契约疑点上报（"任务等级"折算输入的字面挂载点，
  08 未给出结论，本任务落在 `rewards.level`）详见 `core/gameplay/common/contracts/RewardBundle.cs`
  `FromRecord` 判断记录"reward_level 挂载点"。

- **数值设计落地阶段 N4 · T-N4-5**（[ADR-0033](architecture/adr/0033-等级经验模块正文与当量来源.md)
  决策 6/7；[06 第 2.5 节](architecture/06_规则层_属性技能战斗AI.md)）：`Core.Numbers.PowerSet.
  IPowerHost` 新增默认接口成员 `RefillAll(unitId, sourceId)`（升级回满：把该单位全部
  `start_full=true` 的回复型资源池回满到上限，积累型资源——`start_full=false`，如连击点一类
  ——不动，经既有 `SetCurrentClamped` 落值 + 发事件入口，不绕过 `power.changed`，唯一生产实现
  `PowerHost` 显式覆盖）；`Core.Rules.Assembly.RulesAssembly` 新增订阅：`progression.level_up`
  触发时，`ProgressionOptions.RefillOnLevelUp`（T-N4-1 登记、缺省 `true`）为真且该单位已在
  `PowerHost` 注册则调用 `RefillAll`，为假则跳过——`ProgressionOptions.RefillOnLevelUp` 从本
  任务起真正被消费（消费方在装配根，不在 `core/numbers/progression` 模块内部）。同一任务锁死
  ADR-0033 决策 6"从不扣经验"：`core/gameplay/death` 三种死亡复活策略（`RespawnPoint`/
  `ReloadSave`/`Permadeath`）按模块依赖清单本就不引用 `IProgressionHost` 任何类型，新增回归
  用例 `core/gameplay/death/tests/T_N4_5_RespawnPolicyDoesNotAffectXpTests.cs` 把这条模块
  边界钉成可执行回归锁。契约疑点上报（积累型资源是否也应升级回满，ADR/06 原文未区分）详见
  `IPowerHost.RefillAll` 方法注释"契约疑点上报"。全部改动均为纯新增 C# API + 装配接线，不涉及
  任何数据表字段，回放基线核查：`core/gameplay/tests/Replay` 只装配 L2 `RulesAssembly`，回放
  夹具的等级曲线 `max_level=1` 从不触发真实升级，本次改动零影响，基线未变。

数值设计落地阶段 N4 · T-N4-6（ADR-0034 决策 2；08 第 7.4 节；04 第 1.1 节表清单）：新增表
`econ.value_curve`（物品等级 → 基准价值，断点表，横轴 `CurveAxis.ItemLevel`）与
`econ.gold_base_curve`（等级 → 金币基数，断点表，横轴 `CurveAxis.Level`，本任务只登记 schema，
消费留给 T-N4-7）；新增价格公式共用类型 `Core.Gameplay.Economy.EconomyPriceFormula`（基准价值 =
`econ.value_curve(item_level)` × `item.quality_definition.price_multiplier` ×
`item.slot_definition.price_coefficient`，`item.template.value_override` 存在时整体取代基准价值）；
`econ.vendor.sell_items[].price_amount` 改为可选（`VendorSellItem` 新增 `HasPriceAmount` 属性与
对应七参数构造重载，ABI 只增不改，旧六参数构造函数与 `PriceAmount` 属性语义不变）——未填时
`EconomyHost.Buy` 按价格公式算出买价，填了则手填优先（回归安全，逐位不变）；`EconomyOptions.
DefaultBuyPricePct` 重新定位为"售价比例"策略项（字段名与默认值 0.25 不变，语义由"任一商人手填
售价的百分之几"改为"基准价值的百分之几"）——`EconomyHost.Sell` 无 `buy_price_rule` 时的缺省售价
公式同步改用价值公式，`buy_price_rule` 存在时既有 Expr 求值语义不变（硬性规则：禁止改该表达式
语义）；`EconomyOptions` 新增 `ValueCurveId`（缺省 `econ.value.default`）与
`PriceDeviationWarningThreshold`（缺省 0.2）。新增校验规则 `EconomyPriceDeviatesFormulaRule`
（检查名 `econ_price_deviates_formula`——04 第 5 节该行原文未给出具体检查名，设计层裁定
（2026-09-16）：采纳本任务暂按此拟定的检查名，已补登记进 04 第 5 节；`NonEscalatable = true`）：`sell_items[].price_amount`/`item.template.value_override`
与纯公式值偏离超过阈值报 Warning，未填 `price_amount` 的条目不参与该分支检查。
`Core.Gameplay.Assembly.GameplaySchemaCatalog.RegisterAll` 新增五参数重载
（`economyValueCurveId`/`economyPriceDeviationThreshold`），既有 1/3 参数重载保留、转发默认值不变
（回归）。数据侧：`data/_sample/econ/` 新增 `econ.value_curve.json`/`econ.gold_base_curve.json`
两条曲线样例；既有 `econ.vendor.json` 的 `item.sample_tonic` 条目去掉 `price_amount`，作为"缺省
走公式"的样例（未填 `price_amount` 的条目不参与偏离检查分支，见 `EconomyPriceFormula`/曲线样例
数值判断）。

数值设计落地阶段 N4 · T-N4-11（阶段 N4 独立复核缺口修复；ADR-0034 决策 2 同一条价格解析路径）：
`IEconomyHost` 新增默认接口成员 `TryGetSellItemPrice(Id vendorId, Id itemId): long?`（默认返回
`null`，唯一生产实现 `EconomyHost` 显式覆写、`GameplayAssembly.DeferredEconomyHost` 显式转发）——
`EconomyHost.Buy` 内部单价解析抽出为私有 `ResolveSellItemPrice`，供该新成员复用，保证"实际扣款
单价"与"展示层读到的单价"出自同一条路径；展示层 `Presentation.Ui.ShopViewModel.Refresh` 改用该
成员填充 `VendorSellItemSnapshot.PriceAmount`（此前直接读 `VendorSellItem.PriceAmount`，
`HasPriceAmount=false` 时该字段恒为占位 0，T-N4-10 摘掉 `item.sample_tonic` 的 `price_amount`
后商店 UI 会显示错误的 0 价，未随 T-N4-6 一并接入价格公式的缺口，本任务补齐）。

数值设计落地阶段 N4 · T-N4-7（ADR-0034 决策 3/4；08 第 1.1/7.4 节修订段）：`loot.table.groups[].
entries[].ref` 放行 `econ` 域（`LootTableParser`/`LootContentValidationRule` 同步放行，报错文案
由"必须是 item 或 loot"改为"必须是 item、loot 或 econ"），货币掉落条目的 `count_range` 解释为
当量区间，实际数量 = 当量 × `econ.gold_base_curve`（来源等级，`RollContext.SourceLevel` 为空时
回退等级 1）× `RollContext.Multiplier`（既有难度倍率挂载点，落地为 `diff.tier.loot_multiplier`）；
`creature.tier_definition` 的"分档金币倍率"字段尚未登记（不依赖 T-N4-4），该乘数本阶段恒为
1，记为偏离首版基准的说明，留待阶段 N6 仿真核对锚点时补上（设计层裁定（2026-09-16）：采纳）。货币不进背包、不占格子：`LootHost.PickUp`（`Reject`/`Partial`
两种满包策略）对货币堆叠改经新增的 `IEconomyHost.Add` 入账，不再走 `IInventoryHost.AddItem`，
背包容量为 0/已满时货币仍能全额入账，不影响同一次拾取里非货币条目的既有满包语义；`LootHost`
新增 12 参数构造重载（末尾 `IEconomyHost? economyHost`，旧 11 参数构造函数不变）。入账方式新增
策略项 `EconomyOptions.DepositPolicy`（`Core.Gameplay.Economy.CurrencyDepositPolicy`，
`OnKill`/`GroundPickup`，默认 `OnKill`）：`OnKill` 下 `CreatureDeathLootListener` 在死亡结算那一刻
把货币产出直接入账给击杀者、不生成地面掉落物；找不到明确击杀者时退回 `GroundPickup` 语义
（设计层裁定（2026-09-16）：采纳）；`CreatureDeathLootListener` 新增 8 参数构造重载（末尾 `IEconomyHost? economyHost`）。
`IEconomyHost` 新增两个默认接口成员：`TryGetGoldBaseAmount(int level): double?`（按
`EconomyOptions.GoldBaseCurveId` 指定的 `econ.gold_base_curve` 曲线求值，唯一实现 `EconomyHost`
显式覆写）、`DepositPolicy: CurrencyDepositPolicy`（转发 `EconomyOptions.DepositPolicy`）；
`EconomyOptions` 新增 `GoldBaseCurveId`（缺省 `econ.gold_base.default`）。新增事件
`economy.currency_overflow`（`Core.Gameplay.Economy.CurrencyOverflowEvent`，字段
`unitId`/`currencyId`/`discarded`）：`EconomyHost.Add` 使某单位货币余额被夹到
`econ.currency.cap` 之上而丢弃超出部分时触发；`EconomyHost.SetBalance`（读档"以快照为准"语义）
即便结果同样被夹到 cap 也不触发。判断记录：本次未登记 `found.event_catalog.json`/重生成
`EventKeys.g.cs`——`gen_event_constants.py --check` 只比较登记表与已提交生成文件两者自身是否
一致，不反查代码里手写的 `Id` 事件常量，因此不登记也不影响该门禁；两条新经济事件（含 T-N4-8 的
`economy.charged`）的登记与常量重生成整体留给 T-N4-8（依赖 T-N4-7），避免与其重复改动同一份登记
表。数据侧：`data/_sample/econ/econ.currency.json` 补 `cap: 99999`；`data/_sample/loot/
loot.table.json` 的 `loot.sample_beast` 新增一条 `econ.currency.sample_coin` 货币条目
（`count_range: {min:1,max:3}`）。

数值设计落地阶段 N4 · T-N4-8（ADR-0034 决策 5；08 第 7.4 节修订段；分阶段落地计划 M5/M7）：
`IEconomyHost` 新增默认接口成员 `TryPay(Id unitId, Id currencyId, long amount, string reason):
bool`（原子扣费带 reason 重载；ABI 只增，旧无 reason 三参数签名保留不变；默认实现转发旧签名、不发
事件，唯一实现 `EconomyHost` 显式覆写为真实原子扣费 + 发事件逻辑，`GameplayAssembly.
DeferredEconomyHost` 显式转发）。新增事件 `economy.charged`（`Core.Gameplay.Economy.
EconomyChargedEvent`，字段 `unitId`/`currencyId`/`amount`/`reason`）：`TryPay(...,reason)` 原子
扣费成功时触发。判断记录：`EconomyHost.TryPay(Id,Id,long)`（旧无 reason 签名）改为转发带 reason
的新签名、传入占位 `reason="unspecified"`——旧调用路径（`Buy` 内部扣款改传显式
`"vendor_buy"`，其余调用方维持占位）从本次起同样会发 `economy.charged`，与 ADR-0034 决策 5"原子
扣费即发事件"口径一致（详见 `EconomyHost.TryPay(Id,Id,long)` 判断记录，编辑器等下游若依赖"旧签名
不发扣费事件"的既有行为需要注意此变化）。任务/遭遇奖励货币入账收口：`core/gameplay/common.
RewardDispatchDelegates` 新增 `CurrencyGranters.ViaEconomyHost(IEconomyHost)` 静态工厂，构造经
`IEconomyHost.Add` 入账的 `CurrencyGranter`；`CurrencyGranter`/`RewardDispatcher` 既有签名不变，
`GameplayAssembly` 现有等价闭包未切换（风格统一留待后续）。两条新经济事件登记与常量重生成收口：
`economy.charged`、T-N4-7 遗留的 `economy.currency_overflow` 一并登记进
`data/_framework/found/found.event_catalog.json`，`toolchain/gen_event_constants.py` 重生成
`EventKeys.g.cs`（新增 `EconomyCharged`/`EconomyCurrencyOverflow` 两个常量，`--check` 通过）。

数值设计落地阶段 N4 · T-N4-9（ADR-0034 决策 6/7；06 第 4.6 节 2026-09-14 修订段"复活费"；
分阶段落地计划拍板 9）：`Core.Gameplay.Death.DeathPolicyOptions` 新增复活费策略项
`RespawnFee`（枚举 `RespawnFeePolicy`：`None`/`PctOfBalance`/`FixedByLevel`，默认 `None`）、
`RespawnFeePercentage`、`RespawnFeeCurrencyId`，以及三个窄委托
`RespawnFeeFixedAmount`/`RespawnFeeBalance`/`RespawnFeeCharge`（签名分别对应"按等级取原始费用"/
"读余额"/"扣费"，均不直接依赖 `IEconomyHost`——硬性规则）；`DeathPolicyHost` 的 `respawn_point`
分支在 `ReviveUnit` 调用之后新增一次复活费结算：`实际费用 = min(计算值, 当前余额)`，无条件调用
扣费委托（余额为零时同样调用、传入 `0`——"扣零"，不是跳过），复活永远不被阻断；三项委托任一未
接线都退化为不收费，逐位保持既有行为。`GameplayAssembly` 新增两行接线（`RespawnFeeBalance ??=
Economy.GetBalance`、`RespawnFeeCharge ??= (u,c,amt) => Economy.TryPay(u,c,amt,"respawn_fee")`），
默认策略 `None` 下这两行接线不产生任何行为差异。`Core.Rules.Combat.CombatOptions` 新增进战下马
策略项 `DismountOnEnterCombat`（默认 `true`，08/13 号文档既定默认值）、坐骑光环识别字段
`MountAuraDispelType`（`Id?`，默认 `null`，复用 `aura_def.dispel_type` 既有分类机制——设计层
裁定（2026-09-16）：采纳）与窄委托 `DismountMountAurasDelegate`/`DismountMountAuras`；`CombatHost.NotifyCombatEvent`
在"不在战 -> 在战"这一次转换上，三项条件全部满足时调用一次该委托移除坐骑光环。
`core/rules/assembly.RulesAssembly` 新增接线：`Skill.AuraQuery` 运行期确实是 `AuraHost` 时把
`DismountMountAuras` 接到 `AuraHost.Dispel(unitId, dispelType, int.MaxValue)`。回放基线未变化
（默认场景不装配复活费委托与坐骑光环，两处新逻辑均未触发）。

- **数值设计落地阶段 N4 · T-N4-10**（清单 X10/M10；[ADR-0034](architecture/adr/0034-单一货币与价格挂物品等级.md)
  决策 7；纯数据/文档改动，不改代码契约）：把 T-N4-1～T-N4-9 落地的字段/表充实为可仿真的样例数据集。
  `data/_sample/prog/prog.level_curve.json` 的 `prog.curve.sample` 抬档到 20 级（`max_level` 由 3
  改为 20，`xp_to_next` 沿等级单调递增、`talent_points` 每级 1，1/2 级既有取值不变，兼容既有断言）；
  `prog.xp_source.json` 补齐三种来源各带 `base_curve_ref` 的样例——新增 `prog.xp_source.quest`
  （真正的 `RewardDispatcher.DefaultQuestXpSourceId` 约定 id，使任务/遭遇当量奖励在样例数据集
  上真实可发放）；T-N4-11 把既有 `prog.xp.kill_curve_sample`/`prog.xp.discovery_sample` 两条
  样例改名为约定 id `prog.xp_source.kill`（补 `level_diff_ref: combat.level_diff.default`）/
  `prog.xp_source.discovery`，使击杀/探索经验监听器在样例数据集上开箱即真实发放（此前因 id 不
  匹配约定值而静默跳过）；`prog.xp.kill_sample`（只有旧字段 `base_xp`/`weight` 的兼容示例）
  保留原 id 不动。`quest.def`/`encounter.def` 的样例奖励改用 `xp_equivalent`
  + `level`，不再直发绝对经验（硬性规则）。`creature.tier_definition`/`diff.tier` 两档样例各补
  `xp_multiplier`（1.0/2.0、1.0/1.25）。`econ.vendor.sample_hunter` 新增一条填了 `price_amount`
  （27，与价格公式偏离 8%，在带宽内不触发 `econ_price_deviates_formula`）的 `sell_items` 条目，
  与既有不填价格的条目并存；`econ.currency`/`loot.table` 货币掉落条目在此前任务已具备，本任务未改。
  坐骑与骑术样例：`skill.def` 新增 `skill.sample_mount`（`use_condition: not combat.in_combat`，
  `apply_aura` 挂坐骑光环）、`skill.aura_def` 新增 `skill.aura_def.sample_mount_speed`
  （`dispel_type: skill.dispel.mount`，含 `stat.move_speed` 百分比加成）——`dispel_type` 取值即
  游戏层需要配置给 `CombatOptions.MountAuraDispelType` 的约定值，已记入 `data/README.md`；
  `skill.book.sample_a` 新增 `{level: 10, skill_id: skill.sample_mount}` 作为"骑术"学习门槛
  （骑术本身不是独立契约面，落地为技能书学习等级门槛，见 T-N4-9 判断记录 20）。配套新增两条
  `display.map` 覆盖行满足 `DisplayMapCoverageRule`，`toolchain/tests/test_import_sample_assets.py`
  的 `EXPECTED_DISPLAY_ROWS` 清单同步补齐。`validate_data.py --strict`/`SchemaAudit`/
  `format_data.py --schema-order --check` 均 0 问题；`games/_template/data/game/prog/` 空壳表按
  既定约定不动。

### 变更（行为变更）

数值设计落地阶段 N4 · T-N4-2：`ProgressionOptions.MaxLevel` 默认值由 T-N4-1 落地时的 `1` 改为 `0`（该字段在 T-N4-1 只是纯登记壳，默认值不影响任何判定；T-N4-2 开始真正消费"有效满级"判定，语义与默认值必须同时修正，`0` 表示"以曲线自身 `max_level` 为准"）。

数值设计落地阶段 N4 · T-N4-5：`ProgressionOptions.RefillOnLevelUp`（缺省 `true`）从本任务起真正被消费——任何已装配 `RulesAssembly` 且单位在 `PowerHost` 注册的游戏，从本版本起单位升级会自动把全部回复型资源池（`start_full=true`）回满到上限，此前该字段只是登记壳、不产生任何效果；不显式关闭该项的游戏会获得这一新行为。

数值设计落地阶段 N4 · T-N4-6：`EconomyOptions.DefaultBuyPricePct`（字段名与默认值 `0.25` 不变）语义由"任一商人手填售价的百分之几"改写为"基准价值（价格公式算出的值）的百分之几"；`EconomyHost.Sell` 无 `buy_price_rule` 时的缺省售价公式同步改用价值公式（此前的缺省售价公式与本次不同，具体数值可能变化；`buy_price_rule` 存在时既有 Expr 求值语义不变）。

数值设计落地阶段 N4 · T-N4-7：掉落表货币条目从本版本起改经 `IEconomyHost.Add` 直接入账，不再走 `IInventoryHost.AddItem` 占用背包格子——此前货币条目作为普通 `ItemStack` 走背包，背包满时可能拾取失败；现货币恒能全额入账，不受背包容量影响。

数值设计落地阶段 N4 · T-N4-8：`IEconomyHost.TryPay(Id,Id,long)`（旧无 reason 三参数签名）从本版本起改为转发带 reason 的新签名、传入占位 `reason="unspecified"`，因此也会在原子扣费成功时发 `economy.charged`——此前该签名不发任何"扣费"事件，只有 `Add` 间接发 `currency_changed`；依赖"旧签名不发扣费事件"这一既有行为的下游逻辑需要重新核对。

### 文档

- **`architecture/04_数据与内容管线.md`**：第 1.1 节表清单补一行此前拍板已定但正文遗漏登记的 `prog.xp_base_curve`；第 5 节数值类校验项分级表"手填价格偏离公式"一行补检查名 `econ_price_deviates_formula`。细节勘误，版本号不变。
- **`architecture/06_规则层_属性技能战斗AI.md`**：第 2.5 节补一句——击杀/任务/探索三种来源各自实际消费的 `prog.xp_source` 记录 id 由策略配置项指定，未显式配置时退到约定 id，来源未登记时静默跳过、不阻断玩法流程。细节勘误，版本号不变。
- **`architecture/08_玩法层_掉落任务对话关卡.md`**：第 2.1 节 `rewards` 字段落地对齐——任务/遭遇奖励经验由"字段名不变仍叫 `xp`"改写为落地形态"新增 `xp_equivalent`/`level` 两个可选字段，旧字段 `xp` 标废弃保留一个版本周期"；第 7.4 节"怪物掉钱"公式的"分档倍率"补注本阶段恒为 1、留待阶段 N6 补齐。细节勘误，版本号不变。
- 全部 T-N4-* 判断记录里的"待设计层确认"字样已按设计层裁定（2026-09-16）改写为明确结论——`core/gameplay/death/README.md`、`core/gameplay/economy/README.md`/`core/gameplay/economy/core/EconomyPriceDeviatesFormulaRule.cs`、`core/gameplay/loot/README.md`/`core/gameplay/loot/core/CreatureDeathLootListener.cs`/`core/gameplay/loot/core/LootHost.cs`/`core/gameplay/loot/tests/T_N4_7_CreatureDeathCurrencyDepositTests.cs`、`core/numbers/progression/README.md`/`core/numbers/progression/schema/README.md`、`core/rules/combat/README.md`/`core/rules/combat/contracts/CombatOptions.cs` 共十余处，逐处结论见各文件判断记录本身与本计划下方"落地进度记录"N4 一节。
- `architecture/落地计划/数值设计分阶段落地计划.md` 末节"落地进度记录"追加"阶段 N4"一节（小任务门禁、阶段验收标准 1～7 逐项、阶段门禁 P1～P5、回放/Perf 基线、偏离首版基准的说明）。

### 迁移说明

- **`prog.xp_source` 旧字段废弃**：`base_xp`/`weight` 保留一个版本周期供兼容读取，`base_curve_ref` 存在时优先；示例数据集里既有 `prog.xp.kill_curve_sample`/`prog.xp.discovery_sample` 两条样例已改名为约定 id `prog.xp_source.kill`/`prog.xp_source.discovery`（`prog.xp.kill_sample` 保留原 id）。
- **任务/遭遇/成就奖励 `xp`→`xp_equivalent`+`level`**：旧字段 `xp` 标废弃保留一个版本周期，两者同时填写时以 `xp_equivalent` 为准；`quest.def`/`encounter.def`/`achv.def` 三表共用同一份声明同步生效。
- **旧 `TryPay(Id,Id,long)` 从本版本起也发 `economy.charged`**：见上方"变更（行为变更）"T-N4-8。
- **货币掉落条目不进背包**：见上方"变更（行为变更）"T-N4-7；地面掉落物身份存档段不受影响（货币条目不生成携带身份的地面实体）。
- **`RefillOnLevelUp` 缺省 `true` 的行为变化**：见上方"变更（行为变更）"T-N4-5。
- **新事件登记与 `EventKeys.g.cs` 重生成**：`economy.charged`（字段 `unitId`/`currencyId`/`amount`/`reason`）、`economy.currency_overflow`（字段 `unitId`/`currencyId`/`discarded`）已登记进 `found.event_catalog.json` 并生成 `EventKeys.EconomyCharged`/`EventKeys.EconomyCurrencyOverflow`。
- **新检查名**：警告（不可提升）——`econ_price_deviates_formula`。按检查名过滤诊断的既有逻辑需要认识这个新检查名。
- **样例 id 改名**：见上一条"`prog.xp_source` 旧字段废弃"。

### 编辑器接入建议

- 文首"编辑器相关契约"索引里 T-N4-1/4/6/8 四条已从 `（Unreleased）` 改标 `（1.34.0）`，内容不变——编辑器项目按该索引即可判断新版本需要跟改的契约面。
- `prog.level_curve.talent_points`/`prog.xp_source` 新字段（T-N4-1）：等级曲线编辑界面补"天赋点数"输入框；经验来源编辑界面补 `kind` 三选一下拉、`base_curve_ref`/`level_diff_ref` 引用选择控件、`once_key` 文本输入框，并对 `base_xp`/`weight` 加"已废弃"标注。
- `creature.tier_definition`/`diff.tier.xp_multiplier`、任务/遭遇/成就奖励 `xp_equivalent`/`level`（T-N4-4）：生物分档/难度档编辑界面各补"经验倍率"输入框；奖励编辑界面补"经验当量"/"等级"两个输入框，并对 `xp` 输入框加"已废弃"标注。
- `econ.value_curve`/`econ.gold_base_curve`/`sell_items[].price_amount` 可选（T-N4-6）：见上方"编辑器相关契约"T-N4-6 条目。
- `economy.charged`/`economy.currency_overflow`（T-N4-8）：新增事件出现在事件词汇表，事件流查看器/日志面板若按事件名过滤需要认识这两个新事件；依赖"旧 `TryPay` 不发扣费事件"的编辑器侧逻辑需要重新核对（见上方"变更（行为变更）"）。
- `DeathPolicyOptions` 复活费策略项/`CombatOptions.DismountOnEnterCombat`/`MountAuraDispelType`（T-N4-9）：死亡策略编辑界面若展示 `respawn_point` 分支参数，需要补"复活费策略"三选一（`None`/`PctOfBalance`/`FixedByLevel`）与对应的百分比/货币引用输入框；坐骑相关光环编辑界面需要提示"进战自动移除"依赖 `dispel_type` 取值与 `CombatOptions.MountAuraDispelType` 配置一致。

## [1.33.0] - 2026-09-15

MINOR 版本：数值设计落地阶段 N3"技能"（[数值设计分阶段落地计划](architecture/落地计划/数值设计分阶段落地计划.md)第 9/14 节，T-N3-1～T-N3-11）——落地 ADR-0031 全部决策：效果值缩放契约、使用条件、节拍锁、来源缺失冻结、瘟疫刷新、控制类别、群体超出策略（拍板 7）、武器百分比基于秒伤乘一拍、`skill.budget_rule` 与技能预算 Analyzer、结算类原语集合、独立的优先级表求值组件——不新增任何效果原语。
回放/Perf 基线未变（回放场景不触达本阶段新增的任一分支，详见落地进度记录 N3 一节"回放/Perf 基线"）。各小任务门禁与逐项验收记录见该计划末节"落地进度记录"。

提交链：`53e4f1a`（T-N3-1）、`591fdfc`（T-N3-2）、`43f505f`（T-N3-3）、`185d68e`（T-N3-4，`n3/cast` 并行分支，`--no-ff` 合入本分支的合并提交 `cccdbe3`）、`d19995f`（T-N3-5）、`941ecb2`（T-N3-6）、`5276fef`（T-N3-7）、`f38679c`（T-N3-7 补：周期效果冻结缓存清理）、`4a7afe9`（T-N3-8，`n3/target` 并行分支，`--no-ff` 合入本分支的合并提交 `4d1f7a7`）、`424854a`（T-N3-9）、`65a6446`（T-N3-10）、`a8aa340`（T-N3-11），分支 `n3/skill`，`--no-ff` 合入 `main`（合并提交 `cdc8ab4`），本笔提交（T-N3-12：阶段 N3 文档收尾——裁定落地、04/06 勘误、落地进度记录、1.33.0 变更记录）。

### 新增

数值设计落地阶段 N3 · T-N3-1（ADR-0031 决策 1/9/10；06 第 3.1/3.2 节修订段）：`skill.def` 新增 `use_condition: Optional<Expr>`（可选，宿主为施法者上下文，self/combat/target 分组，为假时施法返回 `ConditionNotMet`（T-N3-4 落地）、就绪查询同步反映）、`budget_note: Optional<String>`（超模说明，`SkillBudgetAnalyzer` T-N3-9 按此归入已确认组）两个字段；`cast_time`/`respects_gcd`/`cost` 三个既有字段改写描述（动作时长/节拍锁/固定消耗语义，不改字段种类/必填性）；新增 `Core.Rules.Common.SettlementEffectKinds`（结算类原语集合常量：`school_damage`/`weapon_damage_pct`/`heal`/`projectile`/`apply_aura`，供 T-N3-9 预算校验适用范围与 T-N3-10 智能释放候选集共用）。本任务只登记 schema 层，运行时消费留给 T-N3-4/T-N3-9。

数值设计落地阶段 N3 · T-N3-2（ADR-0031 决策 1；06 第 3.2 节修订段）：`school_damage`/`heal`（`skill.def.effects`）与 `periodic_damage`/`periodic_heal`（`skill.aura_def.effects`）效果参数新增 `scaling: [{stat: Reference(stat.definition), coefficient: Number}]`（可选，权威写法，允许多条求和，效果值 = 基础值 + Σ(coefficient × stat 最终值)）与 `base_curve_ref: Optional<Reference(skill.base_curve)>`（可选，存在时取代 `base_value`，按施法者当前等级在曲线上取值）；新增表 `skill.base_curve`（施法者等级到效果基础值的断点曲线，横轴 `CurveAxis.Level`）。`EffectDispatcher.ApplyDamageOrHeal` 求和优先级：`scaling` 列表非空时权威，缺省时回退旧字段；来源单位未注册/已销毁时缩放贡献按 0 处理（C02 判断记录），曲线引用解析不到时回退 `base_value`。

数值设计落地阶段 N3 · T-N3-3（ADR-0031 决策 1/2/10；ADR-0032 决策 4；06 第 3.2/3.10 节修订段）：新增表 `skill.budget_rule`（最小骨架：本任务只登记 `id`/`beat_seconds: Optional<Number> > 0`，缺省 1.0，`FieldUnit.Time`/`TimeScope.Combat`；完整字段留给 T-N3-9 在同一张表上继续登记）；`SkillOptions` 新增 `BudgetRuleId: Id`（缺省 `skill.budget_rule.default`），`EffectDispatcher` 新增可选构造参数 `skillOptions`（ABI 安全：新增十六参数主构造函数，原十五参数构造函数标注 `[Obsolete]` 纯转发保留）。一拍常数解析（`EffectDispatcher.ResolveBeatSeconds`，经新增 `SkillDefCache.TryGetBeatSeconds`）：`skill.budget_rule` 表未注册或该 id 无对应记录时按缺省 1.0 处理并记一条警告，不阻断结算。

数值设计落地阶段 N3 · T-N3-4（ADR-0031 决策 9/10；06 第 3.1/3.6 节修订段）：施法管线新插入步骤 1.5"使用条件"（求值 `skill.def.use_condition`，为假返回新增失败原因码 `CastFailureReason.ConditionNotMet`）；步骤 4"公共冷却"泛化为"节拍锁"，`SkillReadinessBlockers` 新增同名两位（`ConditionNotMet`/`ActionLocked`），`GetSkillReadiness` 只读查询裁决口径同步纳入这两项。

数值设计落地阶段 N3 · T-N3-5（ADR-0031 决策 10；06 第 3.1/3.6 节修订段）：`CastPipeline.ComputeCastTime` 接入急速策略项与动作时长下限；04 第 5 节"无时间成本"新增警告规则。`SkillOptions` 新增 `HasteAffectsActionTime`（bool，缺省 `false`）、`HasteStat`（`Id?`，缺省 `null`）、`MinActionSeconds`（double，缺省 0）、`MaxHastePct`（double，缺省 100）；三者（含 `CastPipeline` 是否经新增十五参数构造函数重载接到了 `IStatHost`）均满足时，`ComputeCastTime` 读取急速属性最终值折算动作时长。新增 `SkillNoTimeCostWarningRule`（检查名 `skill_no_time_cost`，`NonEscalatable = true`）：主动技能 `cast_time` 为零且 `respects_gcd` 为真（未声明为反应类）即警告。

数值设计落地阶段 N3 · T-N3-6（ADR-0031 决策 8；06 第 3.3 节修订段）：`control` 光环效果新增可选 `category`（`skill.aura_def.effects[kind=control].params.category`，`FieldKind.Enum`，取值集合新增 `Core.Rules.Common.ControlCategoryValues.All`——固定六值 `stun|root|silence|disarm|fear|polymorph`）；免疫按类别判，`AuraHost.ApplyStaticEffects` 对声明了 `category` 的 `control` 效果条目额外查询新增默认接口成员 `IStaticImmunityProvider.IsControlCategoryImmune`，命中则该条目对目标完全不生效。`creature.tier_definition` 新增可选并存字段 `control_immune_categories`（`Array<Enum>`，同一取值集合）——与既有 `control_immune`（全部类别）并存、互不覆盖。

数值设计落地阶段 N3 · T-N3-8（ADR-0031 决策 6、拍板 7；06 第 3.7 节修订段）：`target.chain_def` 新增可选字段 `overflow_policy`（`truncate`（默认）｜`split`｜`cap`，与既有 `max_targets` 配合，候选数超过上限时的处理策略）；`ITargetHost` 新增默认接口成员 `ResolveWithCoefficients(chainId, casterId, currentTarget?) -> TargetResolution`（新类型，`core/rules/common/contracts/TargetResolution.cs`：候选目标 + 各自分配系数 + 生效策略 + cap），旧签名 `Resolve(Id, Id)`/`Resolve(Id, Id, Id?)` 保留、行为不变；`EffectContext` 新增 `TargetCoefficient` 字段（第 18 参构造重载，ABI 只新增，经既有构造函数得到的实例恒为 1.0）；`Core.Rules.Skill.CastPipeline` 步骤 6 链自行收集目标时改调 `ResolveWithCoefficients`，`EffectDispatcher.ApplyDamageOrHeal` 按系数缩放群体效果值。地面坐标施法与 `TriggerCast` 触发链两条入口未接入分配系数（恒系数 1）。

数值设计落地阶段 N3 · T-N3-9（ADR-0031 决策 2；ADR-0032 决策 6；06 第 3.10 节；04 第 5 节数值类校验项分级表）：`skill.budget_rule` 表在 T-N3-3 最小骨架基础上新增九个可选字段——`periodic_time_discount`（Number）、`cooldown_premium_curve`/`range_discount_curve`/`cost_premium_curve`（三条通用断点表）、`player_bandwidth`/`monster_bandwidth`/`player_hard_cap`/`monster_hard_cap`（Number）、`control_category_weights`（Object，六个具名可选 Number 子字段）；新增 `Core.Rules.Skill.SkillBudgetAnalyzer`（静态类，`Analyze(skillId, view, options?, anchorProvider?)` 返回不可变 `SkillBudgetResult`，四态 `SkillBudgetVerdict`：`NotApplicable`/`Pass`/`ConfirmedDeviation`/`UnconfirmedDeviation`/`HardCapExceeded`）与新增校验规则 `SkillBudgetValidationRule`（检查名 `skill_budget_deviation`/`skill_budget_hard_cap_exceeded`）；`SkillDefCache` 新增 `TryResolveBudgetAttribution`（技能等级/档位反查）；新增接口钩子 `Core.Rules.Common.ISkillBudgetAnchorProvider`（`sim.anchor` 归阶段 N6，本任务改接受调用方注入锚点秒伤/期望缩放属性）。新增 `Core.Carriers.Item.ItemGrantValueExceedsShareRule`（检查名 `item_grant_value_exceeds_share`）与 `SkillBudgetAnalyzer.ComputeGrantValue` 公开静态方法：装备授予内容的预算价值合计超过份额报 Warning，`item.template.budget_note` 非空时整条豁免。三条新规则默认注册但因未接入锚点表整体不产生任何问题。

数值设计落地阶段 N3 · T-N3-10（ADR-0031 决策 12"一键智能释放"；ADR-0035 决策 2；06 第 6.2 节）：新增独立的优先级表（`ai.rotation`）求值组件 `Core.Rules.Ai.IRotationEvaluator`/`RotationEvaluator`——不依赖 `ai.behavior_profile`、不要求 `BehaviorState.Combat`，只需一个 `ai.rotation` 表 id 即可给任意单位（含未注册 AI 行为外壳的玩家单位）选出并经 `ISkillHost.CastSkill` 施放一次技能。`AiHost.Evaluate`/`AiHost.LoadRotations`/`AiHost.ClassifyIsHostileSingleTarget` 原样搬迁进新组件，`AiHost` 改为只保留"仅 `Combat` 态才求值"的行为档门控与选中后发事件两件自己的事，其余委托 `RotationEvaluator`。求值算法在委托前后逐位不变（`AiHost` 既有全部测试用例未改动、全绿），唯一新增行为是候选条件为真后先查一次 `ISkillHost.GetSkillReadiness`、不就绪直接跳过。

数值设计落地阶段 N3 · T-N3-11（ADR-0031 决策 3/11）：`Core.Rules.Assembly.RulesSchemaCatalog` 新增重载 `RegisterAll(IDataRegistry, SkillOptions?)`：读取 `SkillOptions.MaxEffectsPerSkill`（缺省 8）作为 `MaxEffectsPerSkillRule` 的构造上限，取代此前硬编码 8；既有无参 `RegisterAll(IDataRegistry)` 改为转发本重载并传 `skillOptions: null`，行为逐位不变（回归）。数据侧：`data/_sample/skill/skill.def.json` 新增 4 条技能（`sample_rest`/`sample_lockpick`/`sample_burst`/`sample_parry`）；新增 `data/_sample/skill/skill.base_curve.json`；`data/_sample/target/target.chain_def.json` 新增 `overflow_policy` 三态各一条真实链；`data/_sample/arch/arch.power_type.json` 新增积累型资源示例 `arch.power.sample_fury`；`data/_framework/arch/arch.power_type.json` 的 `arch.power.health` 补 `refill_on_leave_combat: true`（早于本阶段存在的字段与运行时消费，只补框架默认数据）。

### 变更（行为变更）

数值设计落地阶段 N3 · T-N3-3：`weapon_damage_pct` 效果原语改为"武器秒伤 × 一拍常数 × 百分比"——`EffectDispatcher.ApplyDamageOrHeal` 的 `WeaponDamagePct` 分支不再调用 `IWeaponDamageQuery.GetWeaponBaseDamage`（武器单次基础伤害均值），改调 T-N2-6 新增的 `GetWeaponDps`（武器秒伤）再乘以一拍常数；`GetWeaponBaseDamage` 方法本身保留，签名与既有语义不变。

数值设计落地阶段 N3 · T-N3-4：`GcdEnabled=false` 分支新增判定——`respects_gcd=true` 的技能在施法者他技能动作时长内返回新增失败原因码 `CastFailureReason.ActionLocked`；`respects_gcd=false` 的瞬发反应类技能（打断/格挡/保命）可在动作中插入执行、不占用法术队列、不打断/不覆盖原读条状态；`respects_gcd=false` 但非瞬发时结构性无法安全插入，回退 `Busy`。`respects_gcd=false` 技能在 `GcdEnabled=false` 的游戏里语义变化明显（由"读条中一律拒绝/排队"变为"可插入立即执行"）。

数值设计落地阶段 N3 · T-N3-7（ADR-0031 决策 4/5；06 第 3.3/3.8 节修订段）：周期效果（`periodic_damage`/`periodic_heal`）来源单位在光环仍生效期间被销毁（`Despawn`）后，此前的行为是"缩放贡献按 0 处理，每跳只剩 base_value"，现改为"冻结为最后一次来源仍存在时算出的每跳值"；不设快照策略项，无开关，行为恒生效。`SkillOptions` 新增 `PlagueRefreshRatio`（double，**默认 0.3**，ADR-0031 决策 5 原文"默认三成"）：同来源同光环再次施加（刷新）时，新持续时间 = 定义持续时间 + min(剩余时长, 定义持续时间 × 比例)，取代此前"直接重置为定义持续时间"的行为；`PlagueRefreshRatio = 0` 时精确退化为旧行为（回归）。

### 修复

数值设计落地阶段 N3 · T-N3-7 补（复核发现）：`EffectDispatcher._lastPeriodicEffectValue` 缓存此前无清理路径，长会话内随光环实例产生/移除无界增长；`IEffectSink` 新增默认接口成员 `ForgetPeriodicCache(Id)`/`ClearPeriodicCache()`（ABI 安全，空实现，不影响其它实现方），`AuraHost.RemoveInstanceInternal`（全部移除路径的唯一收口：到期/`RemoveAura`/`Dispel`/吸收耗尽/叠加溢出`Replace`/目标销毁）统一调用 `ForgetPeriodicCache`，随光环实例移除同步清理，不再无界增长。

### 文档

- **`architecture/04_数据与内容管线.md`**：第 1.1 节表清单新增 `skill.base_curve`；第 5 节数值类校验项分级表为阶段 N3 新增检查登记补全——"技能预算硬上限"一行补检查名 `skill_budget_hard_cap_exceeded`；"技能预算偏离"一行补检查名 `skill_budget_deviation`（不可提升）；"授予价值超特效占比"一行补检查名 `item_grant_value_exceeds_share`（不可提升）并把注记改为"已接入"；"无时间成本"一行补检查名 `skill_no_time_cost`（不可提升）；新增"预算类校验的锚点依赖"说明。细节勘误，版本号不变。
- **`architecture/06_规则层_属性技能战斗AI.md`**：第 3.2 节"结算类原语集合"修订段补一句——`apply_aura` 是否算结算类的精确判定由消费方消费时解析落实；第 3.7 节修订段补一句——三种超出策略的精确分配系数定义（`truncate` 系数恒 1、`split` 系数=`max_targets`/命中数、`cap` 系数=1/命中数）；第 3.10 节补一句——预算校验用到的锚点秒伤与期望缩放属性来自数值仿真锚点表（归阶段 N6），接入前带宽/硬上限两条校验规则整体跳过，接入后默认生效。细节勘误，版本号不变。
- 全部 T-N3-* 判断记录里的"待设计层确认"字样已按设计层裁定（2026-09-15）改写为明确结论——`core/rules/common/`（`README.md`、`contracts/SettlementEffectKinds.cs`、`contracts/ISkillBudgetAnchorProvider.cs`、`contracts/TargetResolution.cs`）、`core/rules/skill/`（`README.md`、`schema/README.md`、`schema/SkillSchemas.cs`、`schema/SkillValidationRules.cs`、`contracts/SkillBudgetTier.cs`、`contracts/SkillOptions.cs`、`core/CastPipeline.cs`、`core/EffectDispatcher.cs`、`core/SkillBudgetAnalyzer.cs`、`core/SkillDefCache.cs`）、`core/rules/targeting/`（`README.md`、`schema/README.md`、`contracts/TargetSchemas.cs`、`tests/T_N3_8_TargetOverflowPolicyTests.cs`）、`core/carriers/item/core/ItemValidationRules.cs` 共三十余处，逐处结论见各文件判断记录本身与本计划下方"落地进度记录"N3 一节。
- `architecture/落地计划/数值设计分阶段落地计划.md` 末节"落地进度记录"追加"阶段 N3"一节（小任务门禁、阶段验收标准 1～7 逐项、阶段门禁 P1～P5、回放/Perf 基线、偏离首版基准的说明）。

### 迁移说明

- **`skill.def`/`skill.aura_def` schema_version 1→2**：迁移自动把旧单字段 `scaling_stat`/`coefficient` 补出等价 `scaling` 列表，旧字段原样保留（读取路径不删除）；无真实旧数据的 `data/_sample` 迁移为空操作。
- **`weapon_damage_pct` 语义变化**：旧语义"武器单次伤害 × 百分比"→新语义"武器秒伤 × 一拍常数 × 百分比"，数值会变，游戏侧已有的 `weapon_damage_pct` 技能百分比需要重新校准；新公式不受武器攻速影响。
- **周期效果来源缺失处理变化**：来源单位在光环仍生效期间被销毁后，每跳值由"缩放贡献按 0 处理"改为"冻结为最后一次来源仍存在时算出的每跳值"，恒生效、无开关；数值上通常显著更高。
- **`PlagueRefreshRatio` 缺省 0.3**：装配本模块且不显式设置该项的游戏，默认即获得"刷新最多额外保留 30% 定义时长"这一新行为（光环刷新后持续时间比旧版本更长），需要重新评估战斗节奏/DPS 曲线，或显式设置 `PlagueRefreshRatio = 0` 保持旧行为。
- **`GcdEnabled=false` 下节拍锁新原因码**：新引入的 `CastFailureReason.ConditionNotMet`/`ActionLocked` 与 `SkillReadinessBlockers` 同名两位需要 UI/AI 消费方按需处理（未处理时按"未知失败原因"通用兜底展示，不会崩溃）；已声明 `respects_gcd=false` 的既有内容作者需要确认新的"可插入执行"行为符合设计意图。
- **`split`/`cap` 策略下旧 `Resolve` 返回全部候选**：声明了 `split`/`cap` 策略的目标链，旧 `Resolve(Id, Id)`/`Resolve(Id, Id, Id?)` 签名不做数量截断（因这两种策略本身不丢弃候选、只稀释系数），调用方需要精确分配系数时应改用 `ResolveWithCoefficients`。
- **新检查名清单**：阻断——`skill_budget_hard_cap_exceeded`；警告（不可提升）——`skill_no_time_cost`/`skill_budget_deviation`/`item_grant_value_exceeds_share`。按检查名过滤诊断的既有逻辑需要认识这四个新检查名；`skill_budget_deviation`/`item_grant_value_exceeds_share` 两条规则待阶段 N6 接入锚点表后才会真正产生问题，接入前示例数据零告警。
- **`MaxEffectsPerSkillRule` 改读选项**：`RulesSchemaCatalog.RegisterAll(IDataRegistry, SkillOptions?)` 新增重载，`MaxEffectsPerSkillRule` 的构造上限改读 `SkillOptions.MaxEffectsPerSkill`（缺省 8，与硬编码此前的默认值相同），既有无参重载行为不变（回归）。

### 编辑器接入建议

- 文首"编辑器相关契约"索引里 T-N3-1/2/3/5/6/8/9 七条已从 `（Unreleased）` 改标 `（1.33.0）`，内容不变——编辑器项目按该索引即可判断新版本需要跟改的契约面。
- `skill.def.use_condition`（T-N3-1）：技能编辑界面渲染 Expr 输入控件时可复用与 `ai.rotation.entries[].condition` 同一套 `self`/`combat`/`target` 分组补全。
- `scaling` 列表（T-N3-2）：技能效果编辑界面若已支持 `scaling_stat` 单选下拉，需要升级为可增删的缩放条目列表；`base_curve_ref` 可复用曲线表通用编辑控件。
- `skill.budget_rule`（T-N3-3/T-N3-9）：编辑器新曲线/规则表编辑界面应当收录的新表，T-N3-9 落地后共十一个字段，三条曲线复用既有断点表编辑控件，`control_category_weights` 可复用 `ControlCategoryValues.All` 六值渲染六个滑块/输入框。
- `control.category`/`control_immune_categories`（T-N3-6）：技能效果编辑界面的 `control` 效果 `flags` 多选控件旁需要为 `category` 补一个六值单选下拉；生物分档编辑界面的 `control_immune` 开关旁需要新增一个可多选的"免疫控制类别"控件。
- `target.chain_def.overflow_policy`（T-N3-8）：目标链编辑界面的 `max_targets` 输入框旁需要新增一个三选一下拉。
- `skill_budget_deviation`/`skill_budget_hard_cap_exceeded`/`item_grant_value_exceeds_share`（T-N3-9）：新增检查名出现在 `toolchain/validator --json` 的 `rules[]` 里；接入锚点表前不会真正产生问题，编辑器暂不需要为此新增交互，接入后校验报告面板需要能展示这三个新检查名。

## [1.32.0] - 2026-09-15

MINOR 版本：数值设计落地阶段 N2"装备与掉落"（[数值设计分阶段落地计划](architecture/落地计划/数值设计分阶段落地计划.md)第 8/14 节，T-N2-1～T-N2-11）——落地 ADR-0032 全部决策：预算消耗公式、槽位与品质倍率、护甲与武器秒伤曲线、预算反解、装备评分、词缀转正为预算份额包、掉落三次掷骰、物品实例携带品质与词缀身份、背包容量来源、模板加词缀最大份额超预算阻断校验。
回放/Perf 基线未变（回放场景本就不触达 Loot 模块任何代码路径，详见落地进度记录 N2 一节验收标准第 7 项）。各小任务门禁与逐项验收记录见该计划末节"落地进度记录"。

提交链：`34248a8`（T-N2-1）、`8c596aa`（T-N2-2）、`b298e5a`（T-N2-3）、`a926442`（T-N2-4）、`09bf0ef`（T-N2-5）、`15d483d`（T-N2-6）、`ae139a4`（T-N2-7）、`6c1741d`（T-N2-8 ★确定性敏感）、`cf566a0`（T-N2-9，`n2/inventory` 并行分支，`--no-ff` 合入本分支的合并提交 `6123406`）、`8330ec8`（T-N2-8b）、`1f82dbd`（T-N2-10）、`e714a81`（T-N2-11 前半：补阻断校验），分支 `n2/equipment`，`--no-ff` 合入 `main`（合并提交哈希 `6e81549`），本笔提交（T-N2-11 后半：阶段 N2 文档收尾——裁定落地、04/07 勘误、落地进度记录、1.32.0 变更记录）。

### 新增

数值设计落地阶段 N2 · T-N2-1（ADR-0032 决策 1/2/4/5）：`item.slot_definition` 新增 `budget_coefficient`/`price_coefficient`；`item.quality_definition` 新增 `affix_count`（可选）/`grant_budget_share`/`price_multiplier`，`budget_multiplier`/`price_multiplier` 大小顺序须与 `sort_weight` 一致（新校验规则 `ItemQualityMultiplierOrderRule`，检查名 `item_quality_multiplier_order`）；`item.template` 新增 `value_override`/`budget_note`，`stat_roll_ref` 描述改为"由 `item.affix` 取代的兼容位"；新表 `item.armor_curve`/`item.weapon_dps_curve`/`item.req_level_curve`（物品等级→护甲值/武器秒伤/需求等级的断点表曲线，复用 `CurveSchema.BreakpointsField`，横轴 `CurveAxis.ItemLevel`，自动受 `curve_monotonic_finite` 约束）。本任务只登记 schema/注册/品质倍率顺序校验，消耗公式/护甲武器秒伤求值/需求等级反推等运行时消费实现留给后续任务（T-N2-4/6/9）。

数值设计落地阶段 N2 · T-N2-2（ADR-0032 决策 7）：`item.affix` 由留位转正为预算份额包——新增必填 `budget_share`（占该件预算的比例）/`stat_mix`（`Array<{stat:Reference(stat.definition), ratio:Number(0,1]}>`，属性组合与内部分配比例）/`quality_pool`（`Reference(item.quality_definition)`，所属品质池）/`weight`（池内权重）与可选 `grants`（复用 `item.template.grants` 同一 `GrantsSchema`）；旧占位字段 `effects` 标记废弃，保留一个版本周期只作读取兼容，不再解析。新校验规则 `ItemAffixStatMixRatioSumRule`（检查名 `item_affix_stat_mix_ratio_sum`）：单条词缀 `stat_mix[].ratio` 之和超过一（1e-9 浮点容差）报 Error。`item.template.affixes` 语义由"词缀引用"改写为"该模板掉落时可抽取的词缀候选白名单"，缺省 `[]` 视为不收窄。`currentSchemaVersion` 保持 1，不新增迁移链（留位期无真实旧数据需要兼容，判断记录见 `ItemSchemas.Affix`）。本任务只登记字段与份额之和校验，预算反解/"模板加词缀最大份额超预算"/掉落三次掷骰等运行时消费实现留给后续任务（T-N2-3/4/6/8）。

数值设计落地阶段 N2 · T-N2-4（ADR-0032 决策 3/9；07 第 1.2 节修订段）：新增 `IBudgetSolver`/`BudgetSolver`——预算消耗公式 `(Σ(值×权重)^k)^(1/k)` 的反函数，给定物品等级、品质、槽位与属性组合比例（各属性"加权贡献"占比，之和须为 1）反解各属性值，供词缀落值（T-N2-8）与数值仿真标准玩家生成器（ADR-0035）共用；新增 `budgetCurveId`/`shareOfBudget` 两个 07 原文签名未列出的参数（设计层裁定（2026-09-15）：采纳，见 `core/carriers/item/README.md` 判断记录 19）。新增 `EquipmentScoreAnalyzer`（静态类，`Score`/`Compare`）——同一条消耗公式换成职业权重（`stat.weight.class_overrides`）即为装备评分，本任务只对模板 `stats` 评分，预留 `additionalStats` 入参供后续任务接词缀反解值。`ItemBudgetCurve` 新增重载 `BuildStatBudgetInfo(IDataRegistryView, Id classId)`。两个新类型均不持有可变状态（纯函数/静态类）。`core/carriers/item/tests/TestSupport.cs` 的 `BuildRegistry` 改为 `FailOnUnknownTable=false`（同 `stat_block` 模块既有测试手法），供 `stat.weight.class_overrides[].class` 引用完整性检查用最小占位 `arch.class` 行满足，不影响既有用例。

数值设计落地阶段 N2 · T-N2-7（ADR-0032 决策 8；10 第 2.5 节修订段"物品实例只存身份"）：`Core.Carriers.Common.ItemInstance` 新增 `Quality`（`Id`，非 `Id?`——品质是恒定身份字段）/`Affixes`（`IReadOnlyList<Id>`，不可变、缺省空列表）两个字段，新增构造函数重载 `(Id instanceId, Id templateId, int count, Id quality, IReadOnlyList<Id>? affixes, JsonObject? extra = null)`（旧 4 参构造函数原样保留并转发，见判断记录）；`InventoryHost.AddItemCore` 新建物品时按模板自身 `quality` 字段解析缺省品质，新增 `internal Id ResolveTemplateQuality(Id templateId)` 供存档兼容读取复用同一口径。`player.inventory`/`player.equipment` 两个存档段的物品实例 JSON 新增两个可选 key：`quality`（字符串 id）、`affixes`（字符串 id 数组）——**兼容方式判断记录（不升 `save_version`、不登记 `ISaveMigration` 迁移函数）**：比照本仓库既定先例（1.7.0 AUD-03 `world.vendor_stock` 段 `timer` 条目新增可选字段"`Load` 完全向后兼容纯数字旧格式"、`player.achievement_state` 新增可选字段 `pending_reward`"向后兼容，旧存档缺省按 `false` 处理"），物品实例条目形状新增可选 key 直接在 `ItemInstanceJson.FromJson` 解析处做缺省，不视为"整段缺失"（10 第 2 节"缺失段语义"）。`quality` 缺省取模板自身 `quality` 字段（设计层裁定（2026-09-15）：采纳，见 `core/carriers/item/README.md` 判断记录 22），`affixes` 缺省空列表；key 存在但值非法与既有 `instance_id`/`template_id`/`count` 字段同一口径，抛 `FormatException`。`EquipmentHost` 三参 `Equip(Id,Id,Id)` 由"转发 `null`/`null`"改为"先查一次背包里这件物品的当前实例，转发它自带的 `Quality`/`Affixes`"（查不到实例仍转发 `null`/`null`，行为与改动前一致，接上判断记录 20 原计划）；五参重载额外补一处一致性修复：解析出 `resolvedQuality`/`resolvedAffixes` 后用其重建存入 `unitSlots`/背包的实例（不这样做会导致"`StatHost` 上生效的品质/词缀"与"存档序列化出的身份字段"不一致，读档后属性值与存档前不同，违反 ADR-0032 决策 8"读档按数据重算……与存档前一致"这一不变量）。**迁移说明**：无游戏层需要主动跟改的行为——旧存档缺 `quality`/`affixes` key 时自动按模板品质/空词缀兼容读取，不影响读档；`save_version` 未变。编辑器装备/背包查看工具若展示物品实例，可新增展示 `quality`/`affixes` 两个只读字段（数据来自存档，框架不提供编辑能力——ADR-0032 决策 8"不存任何算出的属性数值，读档按数据重算"，编辑存档中的品质/词缀不在本次契约范围）。回放/Perf 基线未变（`ReplayWorldBuilder` 不涉及 `Core.Carriers.Item`）。

数值设计落地阶段 N2 · T-N2-10（ADR-0032；分阶段落地计划第 14 节）：充实 `data/_sample/item/**`
样例数据，覆盖 T-N2-1～T-N2-9 落地的全部 N2 新字段/新表——`item.slot_definition` 新增 8 条槽位
（副手 `is_weapon:true`；头/胸/腿/手/脚五个防具位 `has_armor:true`，各自不同 `budget_coefficient`/
`price_coefficient`；戒指/项链两个饰品位 `has_armor:false`）；`item.quality_definition` 新增第三档
`item.quality.sample_epic`，三档 `affix_count` 呈 0/1/2、`grant_budget_share` 呈 0.0/0.2/0.3
递增；`item.affix` 新增 `item.affix.sample_of_the_titan`（epic 池），连同既有 4 条覆盖全部三档
`quality_pool`；`item.budget_curve`/`item.armor_curve`/`item.weapon_dps_curve`/`item.req_level_
curve` 四张曲线断点由 3 个扩到 6 个（覆盖物品等级 1～60），全部保留原有断点值不变；`item.template`
新增 4 条（头/胸/脚/戒指，等级 10/30/60/20，品质 rare/epic/epic/rare），连同既有两条武器共 6 条
覆盖多等级×多品质×多槽位，`stats` 均按预算手算反解、`budget_note` 记录推导过程，利用率统一
75%；`loot.table.sample_beast` 追加第二条 `quality_weights` 条目；`diff.tier.sample_story` 补显式
`item_level_offset: 0`。`python toolchain/validate_data.py --strict` 0 error 0 warning（含
`item_budget_exceeded`/`item_budget_utilization_low`/`item_quality_multiplier_order`/
`item_affix_stat_mix_ratio_sum`/`item_weapon_damage_deviates_dps_curve`/`curve_monotonic_finite`
六条相关规则均 0 命中）；`--schema-audit`/`format_data.py --schema-order --check` 均 0。
**T-N2-10 当时尚未落地、已由下方 T-N2-11 补齐**：分阶段落地计划阶段 N2 验收标准 5 要求的"模板加
词缀最大份额超预算"阻断校验（Error）经核对 `core/carriers/item/core/ItemValidationRules.cs` 当时
现有八条 `IValidationRule` 尚未实现（T-N2-4/T-N2-8 均未补，`core/carriers/item/README.md` 判断记录
17 末段"勘误"已预告"确实要等 T-N2-4"但该任务实际只交付了 `IBudgetSolver` 契约面），T-N2-10 本任务
按该规则既定语义人工核算全部样例合规（见 `data/README.md`"item N2 示例数据"一节核算表），供该规则
将来落地时零改样例；**T-N2-11 已落地该规则（新校验规则 `ItemTemplateAffixShareExceedsBudgetRule`，检查名 `item_template_affix_share_exceeds_budget`），`data/_sample`/`games/_template` 两个数据根 `--strict` 校验 0 命中，与本节人工核算结论一致，验证零改样例，见下方 T-N2-11 条目**。
`games/_template/data/game/item/**`/`diff/diff.tier.json` 按 `games/_template/data/README.md`
既定约定保持空壳/原有示例行不动（模板本身尚无 `item.template` 内容，见 `data/README.md` 同一节
判断记录）。

数值设计落地阶段 N2 · T-N2-11（ADR-0032 决策 7/10；04 第 5 节数值类校验项分级表"模板加词缀最大份额超预算"行；分阶段落地计划第 8 节阶段 N2 验收标准 5）：新增阻断校验规则 `ItemTemplateAffixShareExceedsBudgetRule`，补齐 T-N2-3/T-N2-4/T-N2-8/T-N2-10 均未落地的这一条——检查名 04 该行原文未给出，按 `item_template_affix_share_exceeds_budget` 登记（设计层裁定（2026-09-15）：采纳）。语义：`consumed = ItemBudgetCurve.ComputeConsumed(stats, ...)`（同 `ItemBudgetValidationRule` 既有消耗侧公式，基础权重，不按职业覆盖）；`B` = 预算上限（曲线 × 品质预算倍率 × 槽位系数，同 `ItemBudgetValidationRule` 既有算法）；候选词缀 = `item.affix` 中 `quality_pool == 模板品质`，模板 `affixes` 白名单非空时再与之取交集；`maxShare` = 候选按 `budget_share` 降序（同份额按 `Id` 升序稳定排序）取前 `affix_count`（该品质 `affix_count` 未登记视为不限，取全部候选——校验期的保守上界口径，刻意区别于掉落三次掷骰运行期"`affix_count` 未登记按 0（不掷词缀骰）"的处理）之和；`consumed + maxShare × B > B`（1e-9 浮点容差）报 Error，消息点出 `consumed`/`B`/`maxShare` 三个量。`B ≤ 0` 或预算曲线记录不存在时跳过（后者 `ItemBudgetValidationRule` 已报错，不重复）。`CarriersSchemaCatalog.RegisterItemSchemas` 随其余 item 规则一并注册，复用既有 `budgetCurveId` 参数，不新增 `RegisterAll` 重载。新增 5 组正负例（含"白名单收窄后不超预算"、"`affix_count` 未登记按不限取全部候选"两组）。`data/_sample`/`games/_template` 两个数据根 `python toolchain/validate_data.py --strict` 均 0 命中，与 T-N2-10 人工核算结论一致，验证零改样例。见 `core/carriers/item/README.md` 判断记录 25。

### 变更（行为变更）

数值设计落地阶段 N2 · T-N2-3（ADR-0032 决策 3/10）：装备预算消耗公式改为加权 `(Σ(属性值×权重)^k)^(1/k)`（`ItemBudgetCurve.ComputeConsumed`，权重取自 `stat.weight`，没有对应记录时缺省 1；`category=percent` 属性的 `pct`/`mult` 词条先经 `stat.rating_conversion` 换算曲线反函数折回点数，`flat` 词条本就是点数不折算；非 `percent` 属性沿用既有 `pct`/`mult` ×100 折算），指数 `k` 登记在 `item.budget_curve` 新增可选字段 `exponent`（缺省 1.5）；预算上限新增乘 `item.slot_definition.budget_coefficient`（T-N2-1 登记的字段本任务起接入消费）。`ItemBudgetValidationRule` 新增预算利用率过低 Warning（检查名 `item_budget_utilization_low`，不可提升，默认阈值七成，新增构造重载 `(Id, double)` 可配置）；`CarriersSchemaCatalog.RegisterAll` 同步新增重载透传阈值。`Core.Numbers.StatBlock` 新增公开 `RatingConversionShape`/`RatingConversionEvaluator`（`StatHost.ConvertRating` 换算式子提升为共享静态工具，供本任务反函数折算复用，避免复制插值实现）。示例数据 `data/_sample/item/item.template.json` 两条武器模板的 `stats[].value` 由 2 调整为 15（新公式下原值利用率仅一成，触发新警告，样例非框架默认值）。契约未展开到"k 登记位置"/"没有 `stat.weight` 记录时缺省权重"/"op 与 percent 折算方向"/"'registry 视图'具体所指" 四处操作化细节，均按证据链推导实现，设计层裁定（2026-09-15）：采纳，见 `core/carriers/item/README.md` 判断记录 18。

数值设计落地阶段 N2 · T-N2-5（ADR-0032 决策 4/5/7/8；07 第 1.2/1.4 节修订段）：`EquipmentHost` 装备联动新增两类非 `stats` 来源，与模板 `stats` 共用同一 sourceId——护甲值（`item.armor_curve`(item_level) × `item.slot_definition.budget_coefficient`，仅"护甲位"——该护甲位判定规则已被下方 T-N2-6 设计层裁定取代（改为显式字段 `has_armor`），见 `core/carriers/item/README.md` 判断记录 20/21）写入 `ItemOptions.ArmorStatId`（新增可选属性，缺省 `stat.armor`）；词缀反解值经新增公开重载 `Equip(Id,Id,Id,Id? qualityId,IReadOnlyList<Id>? affixIds)` 按 `IBudgetSolver.Solve` 随穿戴重算（`shareOfBudget = affix.budget_share × Σratio`，`statMix` 归一化，判断记录见 README），既有三参 `Equip` 转发并传"模板自身品质 + 空词缀"，`ItemInstance.Quality`/`Affixes` 待 T-N2-7 接入后改接实例字段。`requirements.level` 未填时按 `item.req_level_curve` 反推（`ItemOptions.ReqLevelCurveId` 新增可选属性，缺省 `item.req_level.default`；取整用 `Math.Ceiling`，设计层裁定（2026-09-15）：采纳），填了以手填为准。**迁移说明**：装备防具类模板（非武器位）后，`stat.armor`（或游戏层配置的 `ArmorStatId`）会额外获得曲线值，游戏层若已经手工在装备属性/光环里配置过护甲，需要核对是否与新的自动护甲来源叠加；`requirements.level` 未填的装备现在可能出现此前不存在的等级门槛（若游戏层登记了 `item.req_level_curve`），需要核对现有装备穿戴测试与内容是否受影响。`EquipmentHost` 构造函数新增重载（末尾追加 `IBudgetSolver? budgetSolver`），原 11 参构造函数签名不变、转发新重载。回放/Perf 基线未变（`ReplayWorldBuilder` 不涉及 `EquipmentHost`；`EndToEndTests` 唯一 `Equip` 调用装备武器槽，不触发新增护甲/需求等级分支）。

数值设计落地阶段 N2 · T-N2-6（ADR-0032 决策 4；拍板 6；07 第 1.2 节修订段；设计层裁定）：`IWeaponDamageQuery` 新增默认接口成员 `GetWeaponDps(Id unitId): double`（`EquipmentHost` 实现——武器秒伤 = `item.weapon_dps_curve`(item_level，id 取新增 `ItemOptions.WeaponDpsCurveId`，缺省 `item.weapon_dps.default`) × 品质预算倍率 × 武器槽位系数（即既有 `item.slot_definition.budget_coefficient`）；未装备武器/曲线缺失返回 0；`Core.Rules.Assembly.DeferredWeaponDamageQuery` 同步转发），既有 `GetWeaponBaseDamage`（`(damage_min+damage_max)/2`）签名与行为不变——`weapon_damage_pct` 原语改接秒伤 × 一拍常数是 N3 S3 的范围。新增 `item.weapon_dps_curve` 可选字段 `variance`（伤害范围浮动比例，缺省 0.1，设计层裁定（2026-09-15）：采纳该登记位置，本任务只登记无消费者）。新校验规则 `ItemWeaponDamageDeviatesDpsCurveRule`（检查名 `item_weapon_damage_deviates_dps_curve`，Warning，不可提升，阈值默认 ±20%，构造重载可配置，`CarriersSchemaCatalog.RegisterAll` 新增五参数重载透传曲线 id 与阈值）：武器槽模板手填 `weapon_profile.damage_min`/`damage_max` 均值偏离"秒伤 × `weapon_profile.speed`"超阈值报警告，未填 `damage_min`/`damage_max`、或曲线缺失不报。**设计层裁定**：`item.slot_definition` 新增可选字段 `has_armor`（缺省 false，描述"该槽位的装备是否提供护甲值"），`EquipmentHost.IsArmorSlot` 改为只看本字段，取代 T-N2-5 的"非武器位且真正装备位"推断（该推断会把戒指/饰品一类槽位误判成护甲位）——**迁移说明**：游戏层若已登记防具位（头/胸/腿/手/脚等），需要给对应 `item.slot_definition` 记录补 `has_armor: true`，否则升级后这些槽位不再写入护甲值（此前是自动推断，现在需要显式登记）；样例数据 `item.sample_blade`/`item.sample_model_sword` 的 `weapon_profile.damage_min`/`damage_max` 由 `3`/`6` 调整为 `5`/`7`（原值在新警告下偏离阈值，样例非框架默认值）。编辑器装备编辑界面若渲染槽位定义表单，需要为 `has_armor` 补一个布尔勾选控件；若渲染武器伤害区间输入，可据同一比值（均值 / 期望值）与阈值提示偏离警告，惯例同 T-N2-3 的"预算利用率"进度条。

数值设计落地阶段 N2 · T-N2-8 ★（确定性敏感；ADR-0032 决策 7/8；08 第 1.1/5.1 节修订段；拍板 12）：掉落改为三次独立掷骰——`loot.table.groups[].entries[]` 新增可选 `quality_weights`（ADR-0024 动态键 Map，键引用 `item.quality_definition`、值经 `field_range` 非负校验，见 `LootSchemas.Table` 判断记录，不再退回不透明 `FieldKind.Object` 占位）；`RollContext` 新增构造重载接受 `sourceLevel`/`itemLevelOffset`（旧 4 参构造函数签名不变）；`Core.Carriers.Common.LootRollOutcome`（新公开 struct，登记在 L3 而非 L4——同 `ItemStack` 判断记录，供 `ILootRoller` 依赖倒置接口使用，避免 L3 反向引用 L4）带 `TemplateId`/`Count`/`QualityId`（`Id?`）/`Affixes`（`IReadOnlyList<Id>`）/`ItemLevel`（`int?`，均为 `null` 表示"未额外指定，回退模板"）；`ILootRoller`/`Core.Gameplay.Loot.ILootHost` 各自新增默认接口成员 `RollDetailed(...)`（默认实现转发旧 `Roll` 并投影为"模板品质 + 空词缀 + 模板物品等级"），`LootHost` 对两者均显式覆写为真实抽取实现，旧 `Roll` 反过来调用 `RollDetailed` 再按模板 id 合并投影（两条路径共用同一份 `IRngHost` 序列，不重复抽取）；`GameplayAssembly.DeferredLootRoller` 显式转发 `RollDetailed` 到真实宿主（不落回默认实现，否则会丢失品质/词缀信息），均已过 `InterfaceDefaultMemberForwardingTests` 门禁。掷骰顺序：既有"掉哪条"掷骰（`Next`/`NextInt`）不变，紧接着按 `entry.QualityWeights` 是否配置决定是否掷品质骰（未配置不掷、不消耗随机数）、再按（品质骰结果或缺省的模板品质对应的）`item.quality_definition.affix_count` 决定是否掷词缀骰（从 `quality_pool` 匹配且与模板 `affixes` 白名单取交集的候选池按 `weight` 加权、不放回抽取，候选耗尽提前停止）。物品等级不参与掷骰，是 `RollContext.SourceLevel + ItemLevelOffset` 的确定性折算（无来源等级则为 `null`，回退模板 `item_level`）——ADR-0032 决策 8"物品实例只存……品质、词缀引用"不含物品等级，折算结果因此只出现在 `LootRollOutcome.ItemLevel`，不写入 `ItemInstance`。`DroppedLootEntity` 新增 `Outcomes`（与 `Items` 按下标一一对应的身份，新构造函数重载，旧签名不变）；`LootHost.Drop` 新增 `IReadOnlyList<LootRollOutcome>` 重载（旧 `IReadOnlyList<ItemStack>` 签名不变，经新增 `internal ResolveDefaultOutcome` 按模板缺省解析）；`DroppedLootPersistable` 的 `items[]` 新增可选 `qualityId`/`affixes`/`itemLevel` 三个 key（均只在有值时才写），旧存档缺这三个 key 时缺省为"未额外指定"（`null`/空/`null`），同 T-N2-7"旧存档缺 key → 缺省"先例但落点不同（本场景 `LootRollOutcome` 字段本就允许可空，不需要提前回填模板缺省）。`diff.tier` 新增可选 `item_level_offset`（Int，缺省 0）；`DifficultyTierDefinition`/`IDifficultyHost`/`DifficultyHost` 新增 `ItemLevelOffset`（`IDifficultyHost` 侧按 ABI 规则登记为默认接口成员，缺省 0，唯一实现显式转发，同 `LootMultiplier` 惯例）——难度模块不反向依赖 Loot，调用方自行从该属性取值传入 `RollContext`。**已知缺口（已由同阶段新增的 T-N2-8b 收口，见下一条）**：本任务未把地面掉落物身份接进拾取入包路径（`PickUp` 仍用 `IInventoryHost.AddItem` 无品质入参，`IInventoryHost`/`InventoryHost.cs` 属并行分支 T-N2-9 范围未触碰）；未把 `RollContext.SourceLevel`/`ItemLevelOffset` 接进任何现有掉落发起点（如 `CreatureDeathLootListener`），只新增契约与消费逻辑，接线留给游戏层组装。**迁移说明**：见"编辑器相关契约"小节同一条目末尾"随机数消耗"细节。**回放基线**：`replay_baseline.json` 相对本任务起点零改动——`core/gameplay/tests/Replay/ReplayWorldBuilder.cs`/两份 `*.replay.json` 全文不含 `loot`/`item.template`/`item.quality_definition` 关键字，回放场景本就不触达 Loot 模块的任何代码路径（不是本次改写恰好不影响随机序列，是回放场景压根不掉落），按拍板 12"若回放场景根本不掉落导致基线不变，如实说明原因"处理，`Tests.Gameplay` 全量测试（720 例，含本任务新增 16 例）与 `ReplayBaselineTests` 全绿。

数值设计落地阶段 N2 · T-N2-8b（T-N2-8 已知缺口收口；ADR-0032 决策 5/7/8）：拾取入包携带品质与词缀、死亡掉落接入来源等级与难度层偏移——`IInventoryHost` 新增默认接口成员 `AddItem(Id,Id,int,Id?,IReadOnlyList<Id>?)`/`TryAddItem(...,out int)`（默认实现转发旧签名、丢弃身份，`InventoryHost` 显式覆盖为真实语义）；`InventoryHost.AddItemCore` 按"默认身份"（品质等于模板自身品质且无词缀）判定是否续填/合并既有堆叠——带词缀或非模板品质的物品视为不同身份，即使 `templateId`/身份完全相同也不与任何既有堆叠合并，总是新开格子（默认身份行为与改造前逐字节一致）。`LootHost.PickUpReject`/`PickUpPartial` 改用带身份重载，逐条 `DroppedLootEntity.Outcomes` 按其品质/词缀入包，`PickUpPartial` 额外同步"留在地面上的剩余部分"的 `Outcomes`（沿用同一条 outcome、只改 `Count`，不是重新掷骰）。`CreatureDeathLootListener` 新增构造重载（末尾追加可选 `IDifficultyHost? difficultyHost`），`OnUnitDied` 构造 `RollContext` 时 `sourceLevel` 取死亡单位当前等级（`IUnitAccess.GetLevel`）、`itemLevelOffset` 取 `IDifficultyHost.ItemLevelOffset`（未注入难度宿主时恒 0），并改用 `RollDetailed`/`Drop(outcomes)` 路径保证品质骰/词缀骰结果真正落到地面掉落物身份上（两条路径共用同一份 `IRngHost` 消耗，不增加随机数调用）；`GameplayAssembly` 同步接线 `difficultyHost: Difficulty`。回放/Perf 基线未变（`--filter "FullyQualifiedName~Replay"` 全绿，本任务改动的代码路径不在 `ReplayWorldBuilder` 触达范围）。见 `core/gameplay/loot/README.md` 判断记录 16、`core/carriers/item/README.md` 判断记录 24。

数值设计落地阶段 N2 · T-N2-9（ADR-0034 决策 8"背包容量来源"；07 第 1.3 节 2026-09-14 修订段）：`InventoryOptions` 容量来源二选一——新增可选 `Id? MaxSlotsStat`，为 null（默认）时容量走既有 `MaxSlots` 固定值，非 null 时容量改由该属性 id 决定（沿用 `arch.power_type` 的 `PowerMaxSourceKind.Fixed`/`Stat` 二选一惯例，本类型是 C# 运行期配置对象而非表数据，不另加种类枚举字段）。`IInventoryHost` 新增默认接口成员 `GetCapacity(Id unitId): int`（`int.MaxValue` 表示不限；默认实现恒返回 `int.MaxValue`——本接口新增该成员前完全没有"容量"概念，未覆盖的既有实现视为"无已知上限"，判断记录见 `core/carriers/item/README.md` 判断记录 23），`InventoryHost` 显式覆盖为真实容量语义，`AddItemCore`/`TryPutBack`/`HasRoomForOne` 三处判定统一改读该方法。`InventoryHost` 新增构造函数重载（末尾追加 `Func<Id, Id, double>? statLookup`，供 `MaxSlotsStat` 非 null 时查询属性当前值；原 3 参构造函数签名不变、转发新重载并传 null），未提供 `statLookup` 时若 `MaxSlotsStat` 非 null，在首次解析容量时抛 `InvalidOperationException`（同 `PowerHost` 未注入 `StatLookup` 的既有处理时机）；属性来源解析结果向下取整并夹取到下限 0——没有与固定值路径同等的"≤0 表示不限"语义（设计层裁定（2026-09-15）：采纳，判断记录同上）。`CarriersAssembly` 同步接线：`InventoryHost` 构造先于 `RulesAssembly`（真正的 `IStatHost` 由其内部构造），与该文件既有 `healthFractionSetter`/`powers` 同一种"闭包捕获尚未赋值的局部变量、构造完成后回填"手法解决循环依赖。固定值路径行为与既有完全一致（`MaxSlots≤0` 仍表示不限），本任务不实现任何扩容功能本身——容量随属性变化需游戏层自行用光环/物品修改该属性。回放/Perf 基线未变（`ReplayWorldBuilder` 不涉及 `Core.Carriers.Item`）。

### 修复

- **EditMode 热重载去抖用例偶发失败根治**：`games/_template/Tests/Editor/DataHotReloadEditModeTests.cs`
  的 `FileChange_AfterDebounceWindow_ReloadsTableAndUpdatesRegistry` 偶发 `Expected: 2, But was: 1`。
  用带诊断打点的仪表化版本反复跑全量 EditMode 门禁定位：`System.IO.FileSystemWatcher` 的事件投递
  延迟在观测中就有约 350ms~950ms 的波动，且官方文档明确它在系统繁忙/内部缓冲区来不及消费时会静默
  丢事件、不重投递、不报错——单靠它触发 300ms 去抖窗口，在偶发的高延迟/丢事件场景下会导致"表文件
  已经改了，但热重载永远不会触发"，且不留任何 Error/Exception 日志（与该用例实际失败现场用
  `toolchain/unity_test_triage.py` 分诊出的"断言失败窗口内没有任何 `[DataHotReload]` 日志行"完全
  吻合）。`games/_template/Runtime/DataHotReload.cs` 新增轮询兜底：`ProcessPendingChanges` 每次
  调用时额外做一轮节流轮询（250ms 间隔），对监视根下的 `*.json` 文件比较"上次记录的 mtime+长度"
  与"当前 mtime+长度"，检测到变化/新增/消失都走与 `FileSystemWatcher` 事件同一条
  `MarkPending`→300ms 去抖→`Reload` 路径登记，不新开重载分支、不依赖单一事件源。

### 工具与测试基建

新增 `toolchain/unity_test_triage.py`：Unity EditMode/PlayMode 测试结果分诊脚本，从 NUnit3 结果 XML 与 Unity 日志里抽出失败用例的执行窗口、窗口内首个异常/断言（并提醒 NUnit message 只是最后一次异常）与 Warning/Error 摘要；已接入 `check.ps1` 失败分支自动调用（排查复盘 2026-09-15-PlayMode-PRES180）。
新增 Unity 测试程序集内的 `[TestFirstChance]` 首个异常记录回调：测试运行期把每个断言/未预期日志异常的第一现场打进 Unity 日志，与上条分诊脚本配套，弥补 Unity 批处理日志不打印用例边界与异常发生时机的已知限制。

### 文档

- **`architecture/04_数据与内容管线.md`**：第 5 节数值类校验项分级表为阶段 N2 新增检查登记补全——"模板加词缀最大份额超预算"一行补检查名 `item_template_affix_share_exceeds_budget`（护甲/秒伤/需求等级曲线本身是否单调已由"曲线单调有限"一行的 `curve_monotonic_finite` 覆盖，不属于本项范围）；"词缀份额之和"一行补检查名 `item_affix_stat_mix_ratio_sum`；"品质倍率顺序"一行补检查名 `item_quality_multiplier_order`；"装备预算利用率过低"一行补检查名 `item_budget_utilization_low`；新增警告行"武器伤害偏离秒伤曲线"（检查名 `item_weapon_damage_deviates_dps_curve`）；"授予价值超特效占比"一行补注"N3 落地"（需先接入 `skill.budget_rule` 技能预算才具备判定依据，本阶段未实现）。细节勘误，版本号不变。
- **`architecture/07_载体层_物品生物物件.md`**：第 1.1 节补充 `item.slot_definition.has_armor`（护甲位由显式字段标识，取代 T-N2-5 首版"非武器位且真正装备位"的推断规则）；第 1.2 节预算公式代码块补充"浮动比例登记在 `item.weapon_dps_curve.variance`"一句。细节勘误，版本号不变。
- 全部 T-N2-* 判断记录里的"待设计层确认"字样已按设计层裁定（2026-09-15）改写为明确结论——`core/carriers/item/README.md`/`schema/README.md`、`core/gameplay/loot/README.md`、`core/carriers/item/contracts/`（`IBudgetSolver.cs`/`ItemOptions.cs`/`ItemSchemas.cs`）、`core/carriers/item/core/`（`EquipmentHost.cs`/`InventoryHost.cs`/`ItemBudgetCurve.cs`/`ItemInstanceJson.cs`/`ItemValidationRules.cs`）、`core/carriers/assembly/CarriersSchemaCatalog.cs`、`data/README.md` 共数十处，逐处结论见各文件判断记录本身与本计划下方"落地进度记录"N2 一节。
- `architecture/落地计划/数值设计分阶段落地计划.md` 末节"落地进度记录"追加"阶段 N2"一节（小任务门禁、阶段验收标准 1～7 逐项、阶段门禁 P1～P5、偏离首版基准的说明）。

### 迁移说明

- **`item.affix` 转正**：`budget_share`/`stat_mix`/`quality_pool`/`weight` 四个新增字段为必填（留位期无真实旧数据需要兼容，不新增迁移链，`currentSchemaVersion` 保持 1）；旧占位字段 `effects` 标记废弃，保留一个版本周期只作读取兼容。
- **槽位/品质新字段缺省**：`item.slot_definition.budget_coefficient`/`price_coefficient` 缺省 1、`has_armor` 缺省 `false`；`item.quality_definition.affix_count` 缺省不限、`grant_budget_share` 缺省 0、`price_multiplier` 缺省 1；均为可选字段，旧数据无需手改。
- **护甲写入来源变化**：装备 `has_armor: true` 槽位的模板后，`stat.armor`（或游戏层配置的 `ItemOptions.ArmorStatId`）会额外获得 `item.armor_curve` 曲线值；游戏层若此前手工在装备属性/光环里配置过护甲，需要核对是否与新的自动护甲来源叠加。升级前用"非武器位且真正装备位"推断护甲位的游戏层，需要给对应槽位显式补 `has_armor: true`，否则升级后这些槽位不再自动获得护甲值。
- **`requirements.level` 缺省反推**：未填时按 `item.req_level_curve` 反推，装备可能出现此前不存在的等级门槛（若游戏层登记了该曲线），需要核对现有装备穿戴测试与内容是否受影响。
- **`Roll`/`ILootRoller.Roll` 随机数序列变化**：条目配置了 `quality_weights` 时新增品质骰；不论条目是否配置 `quality_weights`，只要该条目对应品质的 `item.quality_definition.affix_count` 登记了大于零的值、且 `item.affix` 里有该品质池的候选词缀，就会新增词缀骰——游戏层若已经按 T-N2-1/T-N2-2 给这两张表配置了真实数据，全部装备类掉落的随机数序列都会变化，不只是新增 `quality_weights` 的那一条。
- **存档实例新增可选 key**：`player.inventory`/`player.equipment` 段物品实例新增可选 `quality`/`affixes`；`DroppedLootPersistable.items[]` 新增可选 `qualityId`/`affixes`/`itemLevel`；均不升 `save_version`，旧存档缺 key 时自动兼容读取（品质缺省取模板品质、词缀缺省空列表）。
- **`InventoryOptions.MaxSlotsStat`**：容量来源新增"引用属性"二选一，为 `null`（默认）时行为与既有完全一致；非 `null` 时需要构造 `InventoryHost` 时提供 `statLookup` 委托，否则首次查询容量时抛 `InvalidOperationException`。
- **新检查名清单**：阻断——`item_quality_multiplier_order`/`item_affix_stat_mix_ratio_sum`/`item_template_affix_share_exceeds_budget`；警告（不可提升）——`item_budget_utilization_low`/`item_weapon_damage_deviates_dps_curve`。按检查名过滤诊断的既有逻辑需要认识这五个新检查名。

### 编辑器接入建议

- 文首"编辑器相关契约"索引里 T-N2-1/2/3/4/6/8 六条已从 `（Unreleased）` 改标 `（1.32.0）`，内容不变——编辑器项目按该索引即可判断新版本需要跟改的契约面。
- `item.slot_definition.has_armor`（T-N2-6）：槽位定义编辑界面需要为它补一个布尔勾选控件；游戏层已登记的防具位需要显式补勾选，否则升级后不再自动获得护甲。
- `weapon_profile.damage_min`/`damage_max`（T-N2-6）：武器伤害区间输入控件可据"均值 / 秒伤曲线期望值"比值与阈值（默认 ±20%）着色提示偏离警告，惯例同 T-N2-3 的"预算利用率"进度条。
- `loot.table.groups[].entries[].quality_weights`（T-N2-8）：掉落表编辑界面可为该字段渲染"品质 → 权重"键值对表格，键的候选下拉从已加载 `item.quality_definition` 取。
- `item.template.affixes`（T-N2-2）：编辑界面若渲染词缀候选白名单选择器，候选应从 `item.affix` 按 `quality_pool == 该模板 quality` 过滤。
- `item_template_affix_share_exceeds_budget`（T-N2-11）：新增阻断规则出现在 `toolchain/validator --json`/`--list-tables --json` 的 `rules[]` 里；编辑器物品编辑界面若展示"预算利用率"进度条，可同时展示"模板消耗 + 可抽词缀最大份额"这一上界，帮助内容作者在填词缀池前预判是否会超预算。

## [1.31.0] - 2026-09-14

MINOR 版本：数值设计落地阶段 N1"属性"（[数值设计分阶段落地计划](architecture/落地计划/数值设计分阶段落地计划.md)第 7/14 节，T-N1-1～T-N1-10）——落地 ADR-0030 全部决策：属性类别与派生两轮聚合、
换算层始终启用、来源类别进结算上下文、等级差规则表、属性权重表、职业派生系数覆盖。
回放基线未变（回放场景不依赖 `data/_sample`、自带命中表全分支禁用且未装配
`LevelDiffTableId`，Δ 相关代码路径与 T-N1-3 评级换算路径均未在回放场景中触发，已按
Replay README 五步核实）；Perf 基线未变。各小任务门禁与逐项验收记录见该计划末节
"落地进度记录"。

提交链：`c2a63c7`（T-N1-1）、`7d70adb`（T-N1-2）、`f4890f2`（T-N1-3）、`60a18fe`（T-N1-4）、
`686576c`（T-N1-5）、`5cddee1`（T-N1-6）、`0a3fd95`（T-N1-7）、`d987294`（T-N1-7 返工）、
`ea0f6de`（T-N1-8 ★确定性敏感）、`4e7a650`（T-N1-9），分支 `n1/attributes`，`--no-ff` 合入
`main`（合并提交 `2821912`），本笔提交（T-N1-10：文档收尾——裁定落地、04/06/ADR-0030 勘误、落地进度
记录、1.31.0 变更记录）。

### 新增

- **数值设计落地阶段 N1 · T-N1-1（`stat.definition` 新字段 category/derived_from/clamp/
  conversion_ref/scope 与 1→2 迁移，[数值设计分阶段落地计划.md](architecture/落地计划/数值设计分阶段落地计划.md)
  第 14 节）**：`core/numbers/stat_block` 的 `stat.definition` schema 版本 1→2，新增
  `category`（`primary|derived|percent|defense|misc`，决定聚合轮次与是否经换算层）、
  `derived_from`（`Array<{stat:Reference(stat.definition), coefficient:Number}>`，仅
  `category=derived` 有意义，`coefficient` 允许负数只登记有限性不登记下界，拍板 11）、`clamp`
  （`{min?, max?}`，取代平级 `min`/`max`，夹取时机随 T-N1-2 落地）、`conversion_ref`（引用
  `stat.rating_conversion`，仅 `category=percent` 有意义）、`scope`（`any|from_player|from_creature`，
  缺省 `any`）五个字段；`group`/`min`/`max`/`is_rating`/`rating_conversion_ref` 标废弃，保留一个
  版本周期不删（拍板 1/2），迁移链自动补算新字段而不移除旧字段。`group` 本版本仍必填——
  `StatHost.LoadDefinitions`（T-N1-1 不改动 `StatHost.cs`）目前仍无条件读取该字段，放宽为可选
  留给 T-N1-2 改读 `category` 之后。`StatDefinitionValidationRule` 新增两条 Error 检查：
  `derived_from` 出现在非 `derived` 类别、`conversion_ref` 出现在非 `percent` 类别（检查名
  `stat_definition_derived_from_requires_derived`/`stat_definition_conversion_ref_requires_percent`，
  04 第 5 节分级表未逐条列出这两项检查名，按 `stat_definition_*` 前缀命名，设计层裁定
  （2026-09-14）：采纳）。
  **迁移说明**：`group→category` 映射——`resistance→defense`（拍板 1 明文规定）、`primary`/
  `derived` 恒等、`secondary` 按 `is_rating` 取值二选一分裂为 `percent`/`misc`（推断，非契约
  条文明文规定，设计层裁定（2026-09-14）：采纳，理由与既有样例数据 `crit_rating`/`dodge_rating`/`move_speed`
  的既定用法一致）；`min`/`max` 同时或部分存在时嵌套进 `clamp`；`is_rating=true` 且带
  `rating_conversion_ref` 时映射出 `conversion_ref`。`games/_template/data/game/stat/stat.definition.json`
  升级为 v2 写法示例（直接写 `category`，`group` 仍需同时提供）；`data/_sample/stat/stat.definition.json`
  保留 v1 写法练迁移链。三处既有测试夹具（`core/numbers/stat_block/tests/StatHostTests.cs`、
  `core/rules/tests/Integration/ProgressionRestoreRatingRecomputeTests.cs`、
  `core/gameplay/assembly/tests/CORE_180_FollowupAuditTests.cs`）把 `is_rating=true` 属性的
  `group` 由 `primary` 改为 `secondary`，避免迁移后 `category=primary` 与同时迁移出的
  `conversion_ref` 冲突新增校验（`group` 本版本对 `StatHost` 行为无差异，见 `GroupValues`
  判断记录）。测试：`StatDefinitionMigrationTests` 8 例、`StatSchemaCoverageTests` 新增 4 例、
  `StatHostTests` 新增 4 例（两条新规则正负例）。

- **数值设计落地阶段 N1 · T-N1-4（`arch.class.derivation_overrides` 子结构、`ArchetypeRegistry`
  写入派生系数覆盖、换职业/读档恢复触发派生重算，[数值设计分阶段落地计划.md](architecture/落地计划/数值设计分阶段落地计划.md)
  第 14 节）**：`core/numbers/stat_block/core/StatHost.cs` 新增按单位的派生系数覆盖层——公开方法
  `SetDerivationCoefficientOverrides(Id unitId, IReadOnlyList<(Id Stat, Id Source, double
  Coefficient)> overrides)`（全量替换语义）与 `ClearDerivationCoefficientOverrides(Id unitId)`
  （未加入 `IStatHost` 接口，同既有 `ResetBase` 判断记录）；`ComputeDerivedBase` 为每条
  `derived_from` 来源先查该单位的覆盖表，命中则用覆盖系数取代默认系数，未命中（含指向不存在的
  边）静默回退默认，覆盖表变化后按拓扑序重算受影响的已缓存派生属性并广播 `stat.changed`（与
  `PropagateDerivedInvalidation` 同一通知路径）。`core/numbers/archetype`：`ArchSchemas.Class`
  新增可选字段 `derivation_overrides`（见上方编辑器契约条目）；`ArchetypeWriters.cs` 新增具名
  委托 `DerivationCoefficientOverrideWriter`；`Models.ClassDefinition` 新增只读属性
  `DerivationOverrides` 与配套构造重载（既有八参构造委托新九参构造，签名不变）；
  `ArchetypeRegistry` 新增构造重载（在既有六参构造之上追加
  `DerivationCoefficientOverrideWriter?`，既有六参构造改为委托新重载并传 `null`，签名与行为
  完全不变），`ApplyTo` 在写完 `base_stats` 之后、种族修正之前用当前职业的完整覆盖列表调用一次
  该委托（为 `null` 时向后兼容跳过）；新增校验规则 `ArchClassDerivationOverrideValidationRule`
  （见上方编辑器契约条目）。`core/rules/assembly/RulesAssembly.cs`：`ArchetypeRegistry` 构造
  接入新委托（转发到 `Stats.SetDerivationCoefficientOverrides`）；`ReloadArchetypeAndRace`（换
  职业与 `IDerivedStateRebuilder.OnSectionLoaded` 对 `player.race_id` 段读档恢复共用的唯一路径）
  新增一步：写完新职业 `BaseStats` 之后无条件调用
  `Stats.SetDerivationCoefficientOverrides(unitId, cls.DerivationOverrides)`，不按 `classChanged`
  分叉（全量替换语义天然实现"先清旧覆盖再写新覆盖"，职业未变时重复调用是幂等的）。**子结构形态
  判断记录（设计层裁定 2026-09-14：采纳）**：ADR-0030 决策 2 只给出表名"可选"，06 第 1.3 节字段表未展开子结构；
  选 `Array<{stat, source, coefficient}>` 而非 `Map`——一条覆盖需要同时定位"目标派生属性"与
  "被覆盖的来源边"两个 `stat.definition` 引用，`Map` 形态的键只能承载单个属性 id，无法表达这一对
  复合键，理由与命名详见 `core/numbers/archetype/contracts/ArchSchemas.cs` 的
  `DerivationOverrideEntrySchema` 注释。**样例说明**：`data/_sample/stat/stat.definition.json`
  当前没有 `category=derived` 的属性（分类样例留给 T-N1-9），本任务只登记 schema、规则与测试内嵌
  数据，`data/_sample`/`games/_template` 均未新增 `derivation_overrides` 数据行。**禁止事项核对**：
  `ArchetypeRegistry` 全程未出现任何具体职业名，测试沿用 `sample_a` 一类中性 id 惯例。测试：
  `StatHostTests` 新增 5 例（覆盖单条来源/其余来源走默认系数 1、覆盖不存在边静默忽略 1、清空覆盖
  1、全量替换语义 1、覆盖表变化广播 `stat.changed` 1）；`ArchSchemaCoverageTests` 新增 2 例
  （`arch_class_derivation_override_requires_existing_edge` 正反例）；`ArchetypeRegistryTests` 新增
  3 例（`ApplyTo` 转发覆盖列表、职业未登记覆盖时转发空列表、未注入委托时向后兼容跳过）；
  `core/rules/tests/Integration/ArchetypeDerivationOverrideTests.cs` 新增 3 例（战士/法师覆盖不同
  系数得到不同派生值、一个职业覆盖另一个走默认系数得到不同派生值、`ReloadArchetypeAndRace`
  模拟读档恢复后派生值按该职业系数重算）。

- **数值设计落地阶段 N1 · T-N1-5（`stat.weight` 属性权重表 schema、注册、样例，
  [数值设计分阶段落地计划.md](architecture/落地计划/数值设计分阶段落地计划.md)
  第 14 节）**：`core/numbers/stat_block/core/StatSchemas.cs` 新增 `stat.weight` 表（schema
  版本 1）——字段 `id`（`stat.weight.<name>`）、`stat`（`Reference`→`stat.definition`，目标
  属性）、`weight`（`Number`，`WithRange(min: 0)`，一点该属性相当于多少点主属性当量）、
  `class_overrides`（可选，`Array<{class:Reference(arch.class), weight:Number(WithRange(min:
  0))}>`，按职业覆盖）、`description`（可选）；`RulesSchemaCatalog.RegisterL1Schemas` 在
  `StatSchemas.RatingConversion` 之后新增注册，`core/numbers/tests/L1SampleDataTests.cs`
  `BuildWorld` 同步补齐。**本任务只登记 schema/注册/样例，`StatHost` 不读取本表**（06 第 1 节
  明文规定；`core/StatHost.cs` 全文不出现字符串 `"stat.weight"`，由新增
  `tests/StatWeightSchemaTests.cs` 源码扫描 + 行为双重测试防御，见下方测试小节）。**子结构形态
  判断记录（设计层裁定 2026-09-14：采纳）**：ADR-0030 决策 7、06 第 1 节、数值设计 01 第 128 行均只有一行
  描述，未展开 `class_overrides` 子结构形态；任务派发提示词给出两个候选（`Map<arch.class id,
  Number>` 或 `Array<{class, weight}>`），要求"与 T-N1-4 的 `derivation_overrides` 形态保持
  同一风格"——`derivation_overrides` 选 `Array` 的原始理由（一条覆盖需要同时定位两个
  `stat.definition` 引用，`Map` 键承载不下）在本字段不成立（本字段只需单一职业维度，`Map`
  形态同样可行），选 `Array` 是遵照任务派发提示词显式指示统一子结构记法，不是本字段独立推导
  的必然结论，理由详见 `StatSchemas.WeightClassOverrideEntrySchema` 注释。**范围约束判断记录
  （设计层裁定 2026-09-14：采纳）**：`weight`/`class_overrides[].weight` 登记 `>= 0`（不像
  `derived_from[].coefficient` 那样允许负数）——契约未明文规定范围，但 07 第 1.2 节/ADR-0032
  决策 3 的消费公式 `实际消耗 = (Σ(属性值_i × 权重_i)^k)^(1/k)`（`k` 默认 1.5，非整数）在负权重
  下于实数域无定义，登记范围据下游公式推断，若设计层裁定允许负权重，去掉对应 `WithRange`
  调用即可。**样例**：`data/_sample/stat/stat.weight.json` 为既有六条示例属性
  （strength/stamina/armor/crit_rating/dodge_rating/move_speed）各登记一条权重（数值仅为
  样例，非框架默认值），`stat.weight.strength` 一条带 `class_overrides`（覆盖
  `arch.class.sample_a`）；`games/_template/data/game/stat/stat.weight.json` 给空壳
  （`rows: []`，拍板 10）并配 `.meta`（guid 新生成），`games/_template/data/README.md` 表清单
  拆行（`stat.weight` 单列一行，`combat.level_diff_table`/`skill.budget_rule` 仍留待后续任务）。
  测试：`StatWeightSchemaTests` 新增 7 例（合法记录、缺必填 `weight`、`stat` 引用不存在、
  `class_overrides` 子结构缺必填、`class_overrides[].class` 引用不存在、`weight` 负数越界各
  1 例；"`StatHost` 不读该表"源码扫描 1 例、行为测试 1 例）。

- **数值设计落地阶段 N1 · T-N1-6（`EffectContext.sourceKind` 构造重载、`IUnitAccess` 载体类型
  查询默认成员、`WorldUnitAccess` 实现、全部生产构造点补入参，
  [数值设计分阶段落地计划.md](architecture/落地计划/数值设计分阶段落地计划.md)
  第 14 节；[ADR-0030](architecture/adr/0030-属性系统派生换算与来源类别.md) 决策 5）**：新增
  `Core.Rules.Common.SourceKind` 枚举（`Unknown|Player|Creature`）；`EffectContext` 新增只读
  属性 `SourceKind` 与承载它的第十七参数构造函数重载（既有十五/十六参数构造函数物理签名不变，
  经它们构造的实例 `SourceKind` 恒为 `Unknown`）；`IUnitAccess` 新增默认接口成员
  `GetSourceKind(Id unitId) => SourceKind.Unknown`（同 `GetMapId` 既有惯例，不强制既有测试假
  实现改动）；`core/carriers/unit.WorldUnitAccess` 覆盖该成员，按 `Entity.Kind`
  （`EntityKinds.Player`/`EntityKinds.Creature`，即 `PlayerUnit`/`CreatureUnit` 既有的"单位是
  玩家还是生物"判定依据）返回真实值，未知/不存在的单位返回 `Unknown`（不抛异常，供来源单位已
  销毁这一既有支持的边界情形安全退化）。全部生产构造点（`CastPipeline.ExecuteEffectsOnly`、
  `EffectDispatcher.ApplyDamageOrHeal` 重建 outbound 上下文、`AuraHost.FirePeriodic`——经新增
  可写属性 `AuraHost.Units` 注入、`ProjectileHost.ApplyOnHitEffects`）均已补齐 `sourceKind`
  入参；测试夹具未改动的按需继续使用旧构造函数。本任务只透传字段，06 第 4.1 节"目标乘区"步骤尚
  未消费它（T-N1-7 落地），回放与 Perf 基线零变化。游戏侧若自行实现 `IUnitAccess`（教程/
  测试替身之外的场景）建议按同一判定依据覆盖 `GetSourceKind`，否则 `scope: from_player`/
  `scope: from_creature` 两类属性对该实现的单位一律不生效（`scope: any` 不受影响）。

- **数值设计落地阶段 N1 · T-N1-8 ★确定性敏感（`combat.level_diff_table` 接入命中/暴击公式、
  "有效等级是否计入装备等级偏移"策略项，
  [数值设计分阶段落地计划.md](architecture/落地计划/数值设计分阶段落地计划.md) 第 14 节；
  [ADR-0030](architecture/adr/0030-属性系统派生换算与来源类别.md) 决策 6；06 第 4.2 节
  2026-09-14 修订段；拍板 12）**：新增数据表 `combat.level_diff_table`（三条断点表曲线
  `miss_bonus`/`crit_suppression`/`xp_factor`，横轴 Δ = 目标有效等级 − 攻击者有效等级，新增
  `Core.Foundation.DataRegistry.CurveAxis.LevelDiff` 枚举成员承接这一横轴语义；一条 `grey_line`，
  横轴攻击者有效等级本身，`CurveAxis.Level`）。`core/rules/combat.Resolver.DetermineHit` 命中/
  暴击改加减式公式并接入 Δ：`未命中率 = 基础未命中 − 攻击者命中属性(hit_stat) + 未命中加成(Δ)`、
  `暴击率 = 攻击者暴击属性 − 暴击压制(Δ)`（在既有 T-N1-7 被暴击减免之外再减），结果夹取到 [0,1]，
  双向生效（Δ 可负，miss_bonus/crit_suppression 随之可负）。`CombatOptions` 新增
  `LevelDiffTableId`（`Id?`，默认 `null`，不接表时 Δ 加成/压制恒 0，行为与 T-N1-8 之前逐位一致）、
  `EffectiveLevelIncludesGearOffset`（`bool`，默认 `false`）；新增契约
  `Core.Rules.Common.IGearLevelOffsetProvider`（装备等级偏移量查询钩子，缺省
  `NullGearLevelOffsetProvider` 恒返回 0）——期望装备等级曲线（`sim.anchor`，阶段 N6）与平均装备
  等级查询（`core/carriers/item`，阶段 N2）均晚于本阶段落地，本任务只落地策略项与接口钩子，真实
  实现留给后续阶段装配（如实上报）。`combat.hit_table_config` 的 `miss` 分支新增可选字段
  `hit_stat`（攻击者命中属性，`HitTableBranch` 四参构造重载，仅 `miss` 分支消费）。ABI 新增（不
  改动既有签名）：`HitTableBranch`/`Resolver`/`CombatHost` 均以新增构造重载承载新增参数，`Resolver`
  十六参数新重载、`CombatHost` 十二参数新重载。回放基线：`data/_sample/combat/combat.hit_table_config.json`
  的 `miss` 分支已真实接上 `hit_stat: "stat.hit_rating"`（新增 `data/_sample/stat/stat.definition.json`
  同名一行），并新增 `data/_sample/combat/combat.level_diff_table.json` 一条真实样例记录；`Replay`
  场景（`core/gameplay/tests/Replay/ReplayWorldBuilder.cs`）不依赖 `data/_sample`、自带命中表全
  分支禁用且未装配 `LevelDiffTableId`，Δ 相关代码路径未被触发，`replay_baseline.json` **本次未
  变化**（已按 Replay README 五步的第 1 步核实，`ReplayBaselineTests` 全绿）。
  `games/_template/data/game/combat/combat.level_diff_table.json` 补空壳（`rows: []`）+
  `.meta`，`games/_template/data/README.md` 表清单同步更新。设计层裁定（2026-09-14）：采纳，06 原文"未命中率 =
  基础未命中 − 攻击者命中属性 + 目标闪避属性 + 未命中加成(Δ)"里的"+ 目标闪避属性"一项由独立 `dodge`
  分支承担、本实现不在 miss 公式里叠加（06 同日已做勘误，见下方"文档"小节）；`grey_line` 的具体
  消费公式（灰名门槛换算、目标名字五色固定分档）留待阶段 N4 确认。详见
  `core/rules/combat/README.md` 判断记录 19。

- **数值设计落地阶段 N1 · T-N1-9（属性无消费者校验、样例按推荐分类补齐、`arch.power_type` 样例
  health 上限改回固定值，
  [数值设计分阶段落地计划.md](architecture/落地计划/数值设计分阶段落地计划.md) 第 14 节；
  [ADR-0030](architecture/adr/0030-属性系统派生换算与来源类别.md) 决策 8/9；04 第 5 节数值类
  校验项分级表"属性无消费者"一行）**：新增 `core/numbers/stat_block.
  StatDefinitionConsumerValidationRule`（检查名 `stat_definition_no_consumer`，Warning 级，
  `NonEscalatable=true`——04"警告级这一组登记为不可提升"，`WarningsBlock` 下也不阻断）：通用
  扫描全部已注册表的 `Reference`/`SoftReference`/`Map` 键引用字段与
  `IDataRegistryView.GetReferenceDeclarations()` 动态声明是否命中某条 `stat.definition` 记录，
  外加手抄的"框架内置消费者属性名清单"（`CombatOptions` 构造期硬编码默认值：`stat.armor`/
  `stat.damage_done_pct`/`stat.damage_taken_pct`/`stat.healing_done_pct`）兜底运行时选项引用。
  `data/_sample/stat/stat.definition.json` 升级为纯 v2 写法（`schema_version: 2`，不再提供已
  废弃的 `group`/`min`/`max`/`is_rating`/`rating_conversion_ref`），按 ADR-0030 决策 9 推荐分类
  补齐：主属性四个（力量/敏捷/智力/体质）、派生三个（攻击强度/法术强度/资源回复速率，均带
  `derived_from` 系数样例）、战斗百分比五个（暴击率/闪避/命中/暴击伤害/急速，急速新增引用
  `stat.rating.haste` 饱和曲线，示范"变态版"换算形态）、防御一个（护甲，`category` 由 v1
  `derived` 改判为 `defense`）、杂项四个（移动速度/仇恨系数/先攻/幸运）；`data/_sample/stat/
  stat.weight.json`/`data/_sample/arch/arch.class.json`（新增 `base_stats.stat.agility`/
  `stat.intellect` 与一条 `derivation_overrides` 样例）/`data/_sample/l10n/l10n.text.json`
  （新属性两语言文本）配套更新，逐条属性均核对有消费者。`data/_sample/arch/arch.power_type.json`
  的 `arch.power.health`（`"override": true` 行）撤回此前"改为随 `stat.stamina` 成长"的示例，
  改回另一个固定值 `{kind: fixed, value: 120}`（ADR-0030 决策 8"资源上限固定、回复速率派生"为
  推荐默认；`data/_framework` 侧框架级默认行仍是 `{kind: fixed, value: 100}`，不受影响）；
  `core/numbers/tests/L1SampleDataTests.cs` 两个健康池上限用例同步改断言（其一改名
  `ApplyTo_RegistersPowerTypes_HealthCapFixed`）。新增测试
  `core/numbers/stat_block/tests/StatDefinitionConsumerValidationRuleTests.cs`（无消费者警告
  正负例、`stat.weight`/`derived_from` 来源引用正例、框架内置豁免正例、`NonEscalatable` 在
  `WarningsBlock` 下不阻断，共 5 例）。**契约疑点（T-N1-10 已勘误）**：ADR-0030 决策 9
  原文与 06 第 1.3 节修订段的推荐派生属性列表此前仍写"生命上限"，与决策 8/数值设计 01 第 3.7 节
  "改为资源回复速率"表述冲突，本任务样例遵照后者；T-N1-10 已在 ADR-0030"修订记录"节补一条
  （2026-09-14）把决策 9 该处的派生属性列表改写为只保留"资源回复速率"。检查名
  `stat_definition_no_consumer` 与"消费者"的操作化定义（比 04 原文四个例子更宽）均经设计层
  裁定（2026-09-14）：采纳，详见
  `core/numbers/stat_block/README.md`"T-N1-9"一节、`schema/README.md`"`stat_definition_no_
  consumer`"判断记录。

### 变更（行为变更）

- **数值设计落地阶段 N1 · T-N1-2（`StatHost` 两轮拓扑序聚合、派生失效传播、clamp 第二轮后夹取、
  抗性维度独立策略项、派生无环校验，[数值设计分阶段落地计划.md](architecture/落地计划/数值设计分阶段落地计划.md)
  第 14 节）**：`core/numbers/stat_block/core/StatHost.cs` 改读 `stat.definition.category`（不再
  读已废弃的 `group`）与嵌套 `clamp.min`/`clamp.max`（不再读平级 `min`/`max`）；`is_rating`/
  `rating_conversion_ref` 读取逻辑本任务未改（换算层触发条件改 `category==percent` 留给
  T-N1-3）。加载期新增 `BuildDerivationGraph`：按 `derived_from` 建反向依赖表与全部属性 id 的
  稳定拓扑序（候选零入度集合用 `SortedSet<Id>` 逐步取最小值出队，不依赖字典/数组枚举顺序），
  遇到环（含自环）抛 `InvalidOperationException`（加载期防御，正常数据应已被下方新校验规则
  拦下）。`ComputeFinal` 的"基础值"改由 `ResolveBaseValue` 决定：显式 `SetBase` 值永远优先
  （设计层裁定（2026-09-14）：采纳，与既有 `DefaultBase` 回退规则同构的自然推广，ADR-0030 决策 2 原文未明确
  与显式覆盖的优先级关系）；否则 `category=="derived"` 的属性走 `ComputeDerivedBase`——基础值
  = Σ(`derived_from` 来源属性最终值 × 系数，系数允许负数，拍板 11)，再走同一套三段式；其余
  属性沿用既有 `default_base` 规则。`clamp` 仍是每个属性自己聚合的最后一步（拍板 2：来源属性
  先在自己的聚合里被夹取，派生属性拿到的是夹取之后的来源值；派生属性自身的 `clamp` 在它自己
  的第二轮聚合之后生效）。`SetBase`/`AddModifier`/`RemoveModifiersBySource`/`ResetBase` 新增
  `PropagateDerivedInvalidation`：按拓扑序把失效传播给全部（传递）依赖变化属性、且此前已被
  缓存过的派生属性（未查询过的不主动补算，与既有 `RecomputeAllCachedStatsAfterReload` 判断
  记录同一口径），`RecomputeRatingStats`（等级变化驱动的评级重算）同样接入这条路径。抗性维度
  判定条件从 `group == "resistance"` 改为 `category == "defense"`；`StatHostOptions.
  EnableResistanceGroup` 属性名不改（已经是独立于内容数据的 `bool`，改名等价于删除既有公开
  成员，违反 ABI 门禁），默认值不变。新增 `StatDefinitionDerivationCycleValidationRule`（检查
  名 `stat_definition_derivation_cycle`，Error 级，随 `RulesSchemaCatalog` 注册，写法照抄
  `ArchTalentTreeCycleValidationRule`）；`StatDefinitionValidationRule.CheckMinMaxOrder`
  扩展到同时检查嵌套 `clamp.min`/`clamp.max`（此前只查已废弃的平级 `min`/`max`，纯 v2 记录会
  绕过检查）；`StatSchemas.cs` 的 `group` 字段 `required` 放宽为 `false`。`core/carriers/item/
  tests/TestSupport.cs` 原先手写一份独立于真实 `StatSchemas.Definition` 的最小 `stat.definition`
  schema（无 `category` 字段、无迁移链），随 `StatHost` 改读 `category` 而在加载期报错，改为
  直接注册真实 `StatSchemas.Definition`。测试：`StatHostTests` 新增 15 例（两轮聚合 6、失效
  传播 2、派生成环加载期防御 1、显式覆盖 1、派生无环校验规则正反例 3、`clamp` 嵌套校验 1、
  抗性维度 v2 原生用例 1）。

- **数值设计落地阶段 N1 · T-N1-3（换算层始终启用、触发条件改 `category==percent`、恒等/按等级
  除数/饱和三形态复用通用曲线，[数值设计分阶段落地计划.md](architecture/落地计划/数值设计分阶段落地计划.md)
  第 14 节）**：`core/numbers/stat_block/core/StatHost.cs` 的 `LoadRatingConversions` 改为无条件
  执行（不再由 `StatHostOptions.EnableRatingConversion` 门控）；`ComputeFinal` 的换算触发条件从
  "`EnableRatingConversion` 开关 && `is_rating` 字段"改为单一条件 `category=="percent"`；
  `LoadDefinitions` 改读 v2 字段 `conversion_ref`（不再读已废弃的 `is_rating`/
  `rating_conversion_ref`，两者的取值已由 T-N1-1 迁移链折算进 `category`/`conversion_ref`）。
  **偏离计划原文的说明**：计划原文要求"删除 `EnableRatingConversion`"，但落地计划第 1 节"每阶段
  对外契约变化必须控制在 MINOR"——删除既有公开属性是 ABI 破坏，G3 门禁不允许。设计层裁定改为
  **保留但无效化**：`StatHostOptions.EnableRatingConversion` 标记 `[Obsolete("换算层自 1.31.0
  起始终启用（ADR-0030 决策 3），本属性无任何作用，保留仅为二进制兼容")]`，`StatHost` 不再有
  任何代码读取它；"不存在关闭路径"由两条测试保证——`EnableRatingConversion_HasNoEffect_
  PercentStatsStillConvert`（显式构造 `EnableRatingConversion=false`，断言 `percent` 属性仍经
  曲线换算）与 `StatHostOptions_NoUnobsoleteConversionSwitch_Exists`（反射断言 `StatHostOptions`
  上不存在任何未标 `[Obsolete]` 的、名字含 `RatingConversion`/`Conversion` 的布尔属性）。**三种
  曲线形态**（ADR-0030 决策 3；数值设计 01 第 5 节）复用 04 第 3.6 节通用曲线契约，不新增求值
  路径：恒等（保守版：`conversion_ref` 缺省，直通原值，硬上限由 `stat.definition.clamp` 在聚合
  末尾夹取，与换算层本身无关）；按等级除数（标准版：既有 `entries` 断点表形态，T-N0-4 起已复用
  `PiecewiseCurve`，本任务未改公式）；饱和（变态版：`stat.rating_conversion` 新增可选字段
  `saturation`（`CurveSchema.SaturationField`，子字段 `k`（必填，> 0）/`cap`（可选，> 0，缺省
  1）），公式 `输出 = rawValue / (rawValue + k × 单位等级)`，以 `cap` 封顶，与 `combat.resist_curve`
  饱和分支同形态，ADR-0030 决策 3"换算曲线契约……与 `combat.resist_curve` 同形态"）。`entries`
  放宽为 `required: false`，与 `saturation` 二选一，由新增
  `StatRatingConversionValidationRule`（检查名 `stat_rating_conversion_requires_one_shape`，
  Error 级，04 第 5 节分级表未逐条列出，按 `stat_*` 前缀命名，设计层裁定（2026-09-14）：采纳）强制"恰好二选
  一"；纯新增可选字段不升级 `stat.rating_conversion` 的 schema 版本（仍为 2，同
  `StatSchemas.GroupValues` 判断记录先例：放宽必填/新增可选字段不是破坏性变更）。**行为变更
  （不是放宽断言）**：原测试 `RatingConversion_DisabledPassesRawValueThrough`（断言
  `EnableRatingConversion=false` 时直通原值）语义已不成立，改写为
  `EnableRatingConversion_HasNoEffect_PercentStatsStillConvert`（同一输入现在断言经曲线换算，
  不再直通）；生产装配测试（`core/gameplay/assembly/tests/CORE_110_FollowupAuditTests.cs`/
  `CORE_180_FollowupAuditTests.cs`、`core/rules/tests/Integration/
  ProgressionRestoreRatingRecomputeTests.cs`、`core/numbers/stat_block/tests/
  RatingConversionMigrationTests.cs`、`core/numbers/tests/L1SampleDataTests.cs`）里此前
  显式 `EnableRatingConversion=true`/`=false` 的初始化项全部去掉（换算层已始终启用，无需
  也不能再引用已废弃属性）。**回放/Perf 基线核查**：`data/_sample/stat/stat.definition.json`
  的 `crit_rating`/`dodge_rating` 两条属性 `is_rating=true`，迁移后 `category=percent`——换算
  从"默认关闭"变为"始终启用"，但两者在 `data/_sample` 全库无任何 `default_base`/装备/光环
  赋值来源（`default_base` 缺省 0），`combat.hit_table_config` 直接引用其原始值作为 miss/crit
  分支概率，`0` 经任意换算曲线（恒等或除数）结果仍是 `0`——核查结论：**运行时数值零变化**，
  `Replay`/`Perf` 基线保持零改动（已跑 `--filter "FullyQualifiedName~Replay"` 确认，`git status`
  显示两个基线文件零改动）。`games/_template` 无 `percent`/`conversion_ref` 类属性，不受影响。
  测试：`StatHostTests` 新增 6 例（恒等 2、饱和 2、不存在关闭路径行为/反射各 1）、
  `StatSchemaCoverageTests` 新增 5 例（`saturation` 子结构正反例 2、形态二选一正反例 3）。

- **数值设计落地阶段 N1 · T-N1-7（目标乘区按 `scope` 匹配 `sourceKind` 遍历减免属性、被暴击减免
  新介入点、`CombatOptions.DamageTakenPctStat` 改按显式属性 id 清单筛选，
  [数值设计分阶段落地计划.md](architecture/落地计划/数值设计分阶段落地计划.md) 第 14 节；
  [ADR-0030](architecture/adr/0030-属性系统派生换算与来源类别.md) 决策 5；06 第 4.1 节
  2026-09-14 修订段）**：`Core.Numbers.StatBlock.IStatHost` 新增默认接口成员
  `GetScope(Id stat)`（恒 `"any"`），`StatHost` 侧显式实现（按加载期解析的
  `stat.definition.scope` 返回真实值，`LoadDefinitions` 补上此前一直未消费的 `scope` 字段
  解析）。`core/rules/combat.CombatOptions` 新增 `DamageTakenPctStats`/`CritTakenReductionStats`
  （均 `IReadOnlyList<Id>`，默认空列表）。`core/rules/combat.Resolver`"目标乘区"步骤（九步结算
  步骤 6，顺序不变）改为：实际读取的属性集合 = `{DamageTakenPctStat} ∪ DamageTakenPctStats`
  （去重，同一 id 只计入一次），逐条经 `scope` 与 `sourceKind` 匹配后求和，一次性应用；命中表
  "暴击"分支取样前新增介入点——遍历 `CritTakenReductionStats`，同样按 `scope` 过滤后求和，从
  暴击率里扣减，下限 0，不改变 RNG 流 id/取样次数。两个新清单默认均为空列表，既有内容数据/
  回放/Perf 基线零变化。
  **偏离计划原文的说明（复核返工，同一批次内完成，未发布）**：任务表原文"改按类别筛选"的首版
  实现按 `stat.definition.category` 批量扫描（新增 `IStatHost.GetDefinitionIdsByCategory`，
  默认扫描类别 `"defense"`）；复核指出该实现与 ADR-0030 决策 9 的推荐分类冲突——护甲被推荐归入
  `defense` 类别，类别扫描会把护甲原始数值误当百分比计入目标乘区，真实内容一接入即错，比"两个
  新扫描角色共享默认类别可能重复计入"更严重，判定打回返工。现改为本条目描述的"显式 id 清单 +
  `scope` 过滤"，`GetDefinitionIdsByCategory` 已撤回（从未发布，直接删除签名）。详见
  `core/rules/combat/README.md` 判断记录 18、`core/numbers/stat_block/README.md`"T-N1-7"一节。

### 修复

- **PlayMode 全量门禁 `VerticalSliceTests` 283/284 稳定失败根治**：真正根因是
  `FrameworkResidentHost` 的玩家对象（`Core.Numbers.PowerSet.PowerHost` 名下 `arch.power.mana`
  等资源池）跨整个 `-runTests` 批处理进程复用，`SampleNewGameStarter`（示例"新游戏"入口）只重置
  `Position`/`MapId`、不回满资源池——上一条用例（如 `FullVerticalSlice_...`）把玩家法力值打到
  接近 0 后，下一条用例（`PRES180_...`）的"新游戏"直接继承这份未回满的法力，其攻击循环在固定
  预算内换算不出足够的普攻次数杀死示例生物，`Assert.IsTrue(died, ...)` 因此真的失败；但 Unity
  Test Framework 的 `UnityLogCheckDelegatingCommand` 在协程异常终止后仍会继续做日志核对，两条
  `LogAssert.Expect`（死亡飘字缺字警告）因为死亡从未发生自然也不会被满足，NUnit 记录的
  "最后一次"异常覆盖了更早的死亡断言失败，最终报告只剩"Expected log did not appear"——这是此前
  两轮排查被"TMP 缺字警告的顺序依赖"这一表面症状带偏的根本原因（判断记录详见
  `adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/PlayModeIsolation.cs`
  `TearDownAfterTest` 新增小节）。对策落在测试基建（不改生产代码）：`PlayModeIsolation.
  TearDownAfterTest` 新增"跨用例回满玩家资源池"步骤，仿照 `PlayerVitalsPersistable` 已有的
  `GetRegisteredPowerTypes` 遍历模式把当前值补到当前上限；另外用同一份判断记录独立确认的次要
  问题——死亡飘字复用的 `TextMeshPro` 对象池实例在文本值与上次相同时会跳过 TMP 重新生成网格，导致
  缺字警告在同一实例第二次显示同一文案时不再触发——`VerticalSliceTests.
  ResetFloatingTextBeforeDeathWarning`（`FullVerticalSlice_.../PRES180_...` 死亡飘字前各调用
  一次）在死亡飘字发生前把场景内全部 `TextMeshPro`（含对象池中已停用实例）的 `.text` 清空，使
  两条缺字警告与对象池复用历史、执行顺序均无关，必然触发。验证：工作树内连续两次全量 PlayMode
  284/284，单独 `-testFilter` 跑 `PRES180_...` 同样通过，EditMode 70/70。

### 文档

- **`architecture/04_数据与内容管线.md`**：第 5 节数值类校验项分级表为阶段 N1 新增检查登记——
  "派生无环"一行补检查名 `stat_definition_derivation_cycle`；阻断组新增四行
  （`stat_definition_derived_from_requires_derived`/`stat_definition_conversion_ref_requires_percent`/
  `stat_rating_conversion_requires_one_shape`/`arch_class_derivation_override_requires_existing_edge`）；
  "属性无消费者"一行补检查名 `stat_definition_no_consumer` 并把"消费者"定义改写为"被任一登记
  为引用 `stat.definition` 的字段/引用声明或框架内置消费者清单引用"。细节勘误，版本号不变。
- **`architecture/06_规则层_属性技能战斗AI.md`**：第 4.2 节等级差规则表公式之后补一句勘误——
  "+ 目标闪避属性"一项由六分支命中表里独立的 `dodge` 分支承担，`miss` 分支概率只取"基础
  未命中 − 攻击者命中属性 + 未命中加成(Δ)"，不重复计入闪避；第 1.3 节"策略配置项：评级换算是否
  启用"字样改写为"换算层始终启用（ADR-0030 决策 3），不再是策略配置项"，与本节既有修订段
  对齐。细节勘误，版本号不变。
- **`architecture/adr/0030-属性系统派生换算与来源类别.md`**："修订记录"节新增一条
  （2026-09-14）——决策 9 派生属性列表原文同时列出"生命上限"与"资源回复速率"两项，与决策 8
  "资源上限固定、回复速率派生"冲突，以决策 8 为准，决策 9 正文该处改写为只保留"资源回复
  速率"。
- 全部 T-N1-* 判断记录里的"待设计层确认"字样已按设计层裁定（2026-09-14）改写为明确结论——
  `core/numbers/stat_block/README.md`/`schema/README.md`、`core/numbers/archetype/README.md`/
  `schema/README.md`、`core/rules/combat/README.md`、若干 `.cs` 源码注释，共约 39 处，逐处结论
  见各文件判断记录本身与本文件下方"落地进度记录"N1 一节。
- `architecture/落地计划/数值设计分阶段落地计划.md` 末节"落地进度记录"追加"阶段 N1"一节
  （小任务门禁、阶段验收标准 1～7 逐项、阶段门禁 P1～P5、偏离首版基准的说明）。

### 迁移说明

- **`stat.definition` 1→2 迁移**：`group`/`min`/`max`/`is_rating`/`rating_conversion_ref`
  标废弃，保留一个版本周期不删；`group` 自本版本起改为可选（`StatHost.LoadDefinitions` 已不再
  读取该字段，只在加载期做枚举合法性检查）。旧数据文件无需手改，迁移链自动补齐新字段。
- **换算层始终启用**：`StatHostOptions.EnableRatingConversion` 标 `[Obsolete]` 无效化——不删除
  签名（ABI 不允许），但不再有任何代码读取它，不存在"关闭换算层"的路径；显式设置该属性的调用
  方应移除设置（设置与否不再影响任何行为）。
- **`arch.power_type` 样例**：`data/_sample/arch/arch.power_type.json` 的 `arch.power.health`
  上限示例由"随 `stat.stamina` 成长"改回固定值 120（仅示例数据变化，不影响框架默认值
  `{kind: fixed, value: 100}`）。
- **`combat.hit_table_config.miss` 新增可选字段 `hit_stat`**（攻击者命中属性引用）——未提供时
  行为与本版本之前逐位一致。
- **`CombatOptions.DamageTakenPctStat` 现只是目标承伤减免读取集合里的一项**：新增
  `DamageTakenPctStats`/`CritTakenReductionStats`（均默认空列表），依赖"目标乘区只读取
  `DamageTakenPctStat` 单一属性"这一假设的调用方需要了解读取集合已可扩展；默认空列表对既有
  内容数据是恒等变换。
- **新增检查名清单**：阻断——`stat_definition_derived_from_requires_derived`/
  `stat_definition_conversion_ref_requires_percent`/`stat_definition_derivation_cycle`/
  `stat_rating_conversion_requires_one_shape`/`arch_class_derivation_override_requires_existing_edge`；
  警告（不可提升）——`stat_definition_no_consumer`。按检查名过滤诊断的既有逻辑需要认识这六个
  新检查名。

### 编辑器接入建议

- 文首"编辑器相关契约"索引里 T-N1-3/4/5/8/9 五条已从 `（Unreleased）` 改标 `（1.31.0）`，
  内容不变——编辑器项目按该索引即可判断新版本需要跟改的契约面，不必重读本节全部正文。
- `stat.rating_conversion.saturation` 字段（T-N1-3）：曲线编辑控件遇到
  `field_meta.curve.shape="saturation"` 应渲染为"数值 × 等级"两参数输入（`k`/`cap`），不是
  断点表。
- `arch.class.derivation_overrides`（T-N1-4）：职业模板编辑界面若展示派生系数覆盖控件，建议
  渲染为"按来源覆盖系数"子表单，`stat`/`source` 两个引用字段候选建议限定到目标属性的
  `derived_from` 列表。
- `stat.weight`（T-N1-5）：权重编辑控件的 `class_overrides` 建议渲染为"按职业覆盖"子表单，
  `class` 候选取 `arch.class` 全表。
- `combat.level_diff_table`（T-N1-8）：`miss_bonus`/`crit_suppression`/`xp_factor` 三列
  `field_meta.curve.axis="level_diff"`，允许负值输入，不同于既有 `level`/`item_level` 轴默认
  非负；`combat.hit_table_config.miss` 分支新增 `hit_stat` 列，其余五分支不出现该字段。
- `stat_definition_no_consumer`（T-N1-9）：Warning 级、`NonEscalatable=true`，落入既有"警告级
  这一组登记为不可提升"的渲染分组，不需要新增前端分支；若编辑器给"属性详情"面板加"谁在引用
  我"反查视图，可直接复用本规则的扫描逻辑（全部已注册表的 `Reference`/`SoftReference`/`Map`
  键引用字段 + `GetReferenceDeclarations()`）。

## [1.30.0] - 2026-09-14

MINOR 版本：数值设计落地阶段 N0"横切前置"（[数值设计分阶段落地计划](architecture/落地计划/数值设计分阶段落地计划.md)
第 6 节，T-N0-1～T-N0-8）——建立通用曲线契约（04 第 3.6 节曲线形态登记 + 公共插值工具）、校验规则元数据
与报告（规则 id/默认级别/不可提升、注册去重、`rules[]`、`issues[].group/note/rule_id`）、通用"曲线单调
有限"阻断规则，并把既有四张曲线表接到通用插值上（`item.budget_curve`/`stat.rating_conversion` schema
版本 1→2 走迁移链，`combat.resist_curve` 的 `table` 分支复用插值、`prog.level_curve` 只接单调校验）。
回放与 Perf 基线零变化。**迁移说明**：两张迁移表的旧数据文件无需手改（1→2 迁移环节自动改名
`{item_level, budget}`/`{level, points_per_percent}` → `{x, y}`），内容工具执行整表迁移仍走 ADR-0029
`SchemaMigrator.MigrateEnvelope`；直接读这两张表原始 JSON 的消费方改读 `x`/`y`；编辑器问题面板可按
`rule_id`/`group` 分组；`data/_sample` 的 `stat.rating_conversion` 示例值改为随等级不递减（示例数据集未
启用评级换算，无行为影响）；新增阻断检查名 `curve_monotonic_finite`/`level_curve_xp_monotonic` 与元数据
门禁 `field_curve_shape`。各小任务门禁与逐项验收记录见该计划末节"落地进度记录"。

### 新增

- **数值设计落地阶段 N0 · T-N0-1（通用曲线形态与公共插值工具，
  [数值设计分阶段落地计划.md](architecture/落地计划/数值设计分阶段落地计划.md) 第 6 节）**：
  `core/foundation/common` 新增 `CurvePoint`/`PiecewiseCurve`（按 `x` 稳定排序、分段线性插值、
  两端夹取、空表为 0，插值式子与迁移前 `ItemBudgetCurve`/`StatHost` 手写式子逐运算相同，无超越
  函数路径；`IsNonDecreasing()`/`IsFinite()` 只读查询）；`core/foundation/data_registry` 新增
  `CurveSchema`（`CurveAxis` 等级/物品等级/数值三种横轴语义、`CurveShape` 断点表/二元饱和两种
  形态、`BreakpointsField`/`SaturationField` 标准子结构工厂、`ReadBreakpoints`/`TryReadBreakpoints`
  解析入口）与 `FieldSchema.Curve`/`WithCurve`；`SchemaAudit` 新增 `field_curve_shape` 自洽检查
  （形态标记与字段种类/子结构相符）。04 新增第 3.6 节"曲线形态登记"与元数据门禁一行。既有曲线表
  一律未改动（T-N0-4/T-N0-5 迁移），加载期检查全部复用既有检查名；`toolchain/validator --list-tables
  --json` 的 `field_meta` 新增 `curve`（阶段任务线 ②）。测试：`PiecewiseCurveTests`
  12 例、`CurveSchemaTests` 11 例、`SchemaAuditTests` 新增 5 例。
- **数值设计落地阶段 N0 · T-N0-2（校验规则元数据、注册去重、问题分组/说明）**：
  `IValidationRule` 新增带默认实现的 `RuleId`（默认类型名）/`DefaultSeverity`（默认 Error）/
  `NonEscalatable`（默认 false）；`DataRegistry.RegisterValidationRule` 按 `RuleId` 去重（同一实例或
  同 id 的另一实例静默忽略，保留先注册者），校验时给每条规则问题补 `RuleId`（规则自填不覆盖）并按
  注册顺序生成规则摘要；`ValidationIssue` 新增 `Group`/`Note`/`RuleId` 与九参数构造、`WithRuleId`；
  `ValidationReport` 新增 `Rules`/`NonEscalatableWarningCount`，`WarningsBlock` 下不可提升规则的
  Warning 不计入阻断（04 第 5 节数值类校验项分级表警告级"抓意图不抓手滑"）。
  `InterfaceDefaultMemberForwardingTests` 对非组合型规则逐条登记这三个默认成员的豁免（默认值即
  正确语义），组合/转发型规则仍须显式转发。既有规则与检查名一律未改。测试：
  `ValidationRuleMetadataTests` 11 例。
- **数值设计落地阶段 N0 · T-N0-3（通用曲线单调有限阻断规则）**：`core/foundation/data_registry` 新增
  `CurveMonotonicFiniteRule`（检查名 `curve_monotonic_finite`，Error 级）：对全部登记为断点表形态
  （`CurveSchema`）的字段逐条记录检查断点非空、逐值有限、`x` 严格递增无重复、`y` 不递减（允许平台段），
  嵌在对象子字段/数组元素/映射值/变体分支里的曲线一并覆盖，问题定位到完整字段路径；元素形态不符由
  `required_field`/`field_type` 报，本规则不重复；与 `field_finite` 层次不同不合并。由
  `PresentationSchemaCatalog.RegisterAll` 注册（`ContentValidationAssembly`/`toolchain/validator` 与
  运行期组装根一处登记两边生效）。04 第 5 节分级表"曲线单调有限"一行补检查名与判定口径。测试：
  `CurveMonotonicFiniteRuleTests` 12 例、`ContentValidationAssemblyTests` 新增 1 例。
- **数值设计落地阶段 N0 · T-N0-4（既有两张曲线表迁移到通用形态）**：`item.budget_curve`、
  `stat.rating_conversion` 的 `entries` 改用 `CurveSchema.BreakpointsField` 登记（元素 `{x, y}`，横轴分别为
  物品等级/单位等级；后者 `y > 0` 范围登记沿用），schema 版本 1→2，迁移环节 `CurveSchema.MigrateBreakpointsFieldNames`
  把 v1 的 `{item_level, budget}`/`{level, points_per_percent}` 逐元素改名（其余键保留），旧数据文件不改即可
  加载；`ItemBudgetCurve` 新增 `ParseCurve`/`Interpolate(PiecewiseCurve, int)`，既有 `ParseEntries`/元组版
  `Interpolate` 保留为兼容 façade，`StatHost.ConvertRating` 改为 `PiecewiseCurve.Evaluate`（插值式子逐运算相同，
  迁移前后结果逐位一致）；两处解析对未经迁移直接构造的记录仍接受 v1 元素名。`data/_sample`、
  `games/_template` 的两张表数据迁移到 v2；`data/_sample` 的 `stat.rating_conversion` 示例值由随等级递减
  （50→20）改为不递减（50→80）以符合数值总纲原则 1 与新规则 `curve_monotonic_finite`（示例数据集未启用
  评级换算，回放/Perf 基线不受影响；`L1SampleDataTests` 一处按示例值断言的期望同步）。两处覆盖测试的
  `required_field` 路径断言由 `entries[0].budget`/`entries[0].points_per_percent` 改为 `entries[0].y`。
  测试：`ItemBudgetCurveMigrationTests` 12 例、`RatingConversionMigrationTests` 7 例。
- **数值设计落地阶段 N0 · T-N0-5（`combat.resist_curve` table 分支复用插值、`prog.level_curve` 接入单调校验）**：
  `ResistCurve` 的 `table` 分支求值改为委托 `PiecewiseCurve.Evaluate`（式子逐运算相同，`Entries` 公开面不变），
  `saturation` 分支公式原样不动；`ProgLevelCurveValidationRule` 新增检查名 `level_curve_xp_monotonic`
  （`xp_to_next` 沿等级不递减、允许相等，末级条目按约定为 0 不参与比较），表本身保持逐级密集枚举不迁移
  （拍板 3）。既有 `resist_curve_entries_monotonic`/`level_curve_entries`/`level_curve_continuity` 不变。
  测试：`ResistCurveTableInterpolationTests` 14 例、`ProgLevelCurveXpMonotonicTests` 4 例。
- **数值设计落地阶段 N0 · T-N0-6（校验器报告：规则清单与命中统计、问题分组/说明）**：
  `toolchain/validator --json` 新增 `rules[]`（每项 `{id, severity, non_escalatable, hits}`，按注册
  顺序、含命中 0 条的规则）与 `issues[].group/note/rule_id`（未填为 `null`）；文本模式追加末尾
  `rules (N):` 段，问题行尾追加 ` [group: …]`/` [note: …]` 后缀（未填时逐字节不变）；
  `validate_data.py --json` 原样透传。既有字段名与语义一律不变。测试：
  `test_validate_data_json_output.py` 新增 3 例（规则清单、分组/说明、无规则时空数组）。
- **数值设计落地阶段 N0 · T-N0-7（游戏模板空壳表与示例数据核对）**：`games/_template/data/game/item/`
  新增 `item.slot_definition.json`/`item.quality_definition.json` 空壳（空 `rows`，不含任何具体系数，
  拍板 10）；`stat.weight`/`combat.level_diff_table`/`skill.budget_rule` 三张表尚未登记（分别随 N1/N3
  落地），登记前放文件会触发 `FailOnUnknownTable`，本阶段只在模板 `data/README.md` 表清单预留其位置。
  `data/_sample` 迁移后数据（`item.budget_curve`/`stat.rating_conversion` v2）经 `validate_data.py
  --strict` 与迁移回归测试核对一致。模板数据经 `validate_data.py --framework-root data/_framework
  --data-root games/_template/data/game --strict` 零错误零警告（等价于 `validate.ps1 -Strict`）。

### 迁移说明

- **`item.budget_curve`/`stat.rating_conversion` schema 1→2**：`entries` 元素改名
  `{item_level, budget}`/`{level, points_per_percent}` → `{x, y}`；旧数据文件无需手改，加载期
  经迁移链自动补齐；游戏层若有工具绕过框架加载直接读原始 JSON，需要改读 `x`/`y`。
- **整表迁移仍走 `SchemaMigrator.MigrateEnvelope`**（ADR-0029）：内容工具批量迁移这两张表并
  写回磁盘应调用该权威入口，不要自行拼接迁移逻辑。
- **`data/_sample` 的 `stat.rating_conversion` 示例值改为随等级不递减**：示例数据集本身未启用
  评级换算，不影响任何运行时数值；游戏层若复制过这份示例数据且已启用评级换算，需要核对递减方向
  是否符合新规则 `curve_monotonic_finite`。
- **新增阻断检查名 `curve_monotonic_finite`**：对全部登记为断点表形态的曲线字段统一生效（预算/
  护甲/秒伤/需求等级/价值/金币基数/换算/减免等一切以等级或物品等级为横轴、按断点表登记的曲线），
  要求断点非空、逐值有限、横轴严格递增无重复、纵轴不递减（允许平台段）；游戏层若有自定义曲线数据
  违反此约束，升级后加载会报错。
- **新增阻断检查名 `level_curve_xp_monotonic`**：`prog.level_curve.xp_to_next` 沿等级不递减
  （末级条目按约定为 0 不参与比较）；游戏层若有类似"高等级所需经验反而更少"的自定义曲线需要修正。
- **新增元数据门禁 `field_curve_shape`**：只在框架/游戏层自行新增登记曲线形态字段时触发，检查
  字段种类/子结构是否与登记的曲线形态（断点表/饱和）相符；游戏层通常不直接触碰这项。
- **`toolchain/validator --json` 新增 `rules[]` 与 `issues[].group/note/rule_id`**：均为纯新增
  字段，既有字段不变；若消费方按字段集合数量做精确断言（而非只读已知字段）需要更新预期。
- **不可提升警告新语义**：校验规则元数据新增 `NonEscalatable` 标记，这组警告在
  `Strictness=WarningsBlock` 下不会被提升为阻断；若游戏层此前依赖"全部警告在 WarningsBlock 下
  都会阻断"这一假设，需要重新核对。

跨版本升级见 [docs/升级指南/1.29.0到1.37.0-数值设计专项.md](docs/升级指南/1.29.0到1.37.0-数值设计专项.md)。

## [1.29.0] - 2026-09-14

MINOR 版本：消费方反馈处理——问题面板"本条来自可选规则"标注此前只能靠编辑器自行硬编码一份
"规则类型名（PascalCase）→检查项名（snake_case）"映射表，框架新增/改名可选规则时该映射会静默
失效（编辑器第 43 条）；示例数据集 `creature.template` 没有一条 `summon_only` 生物模板，
`SpawnSummonOnlyCreatureRule` 的正例分支无法用真实示例数据验证（编辑器第 44 条）。独立复核"有
条件通过"过程中，主会话进一步认定"`SpawnSummonOnlyCreatureRule` 需要显式提供
`ICreatureTemplateQuery` 才注册、`toolchain/validator` 按既有设计从不接线该依赖、示例数据门禁
因此从未真正跑过这条规则"是真实缺口而非可搁置的边界，决定比照 1.23.0 `DisplayMapCoverageRule`
默认启用的先例一并根治。核实、逐条回复、根治追记见
[消费方反馈-2026-09-14-编辑器-第43-44条.md](architecture/落地计划/消费方反馈-2026-09-14-编辑器-第43-44条.md)。
提交链：`d1787b9`（第 43 条：可选规则描述符）、`64f20d7`（第 44 条：示例数据补 summon_only 生物
正例演示）、`ccd7c78`（回复文档 + 编辑器产品文档回填）、`e51d2d6`（第 44 条根治：
`SpawnSummonOnlyCreatureRule` 改为默认接线）、`5a9dda6`（独立复核修正：两份 README 同步默认接线
行为），分支 `wbg/editor43-44`，`--no-ff` 合入 `main` `1d0c2ed`，本笔提交（1.29.0 变更记录与迁移
说明）。

### 新增

- `Presentation.Assembly.ContentValidationAssembly.OptionalRules`/`OptionalRuleDescriptor`/
  `TryGetOptionalRuleByCheck`——可选规则的"规则名 ↔ 检查名"关联单一来源：`OptionalRuleDescriptor`
  （`RuleName`/`CheckName`/`Description` 三只读属性）的 `CheckName` 直接引用各规则类型自己公开的
  `CheckName` 常量（`SpawnSummonOnlyCreatureRule.CheckName`/`DisplayMapCoverageRule.CheckName` 随
  之由 `private` 改为 `public const string`），不是另行抄写的字面量；`OptionalRuleNames` 保留不变
  （既有签名不变），改由 `OptionalRules.Select(d => d.RuleName)` 投影得到。`TryGetOptionalRuleByCheck
  (string checkName, out OptionalRuleDescriptor descriptor)` 按检查名反查描述符。`ContentValidationRun`
  新增 `EnabledOptionalRuleChecks`/`DisabledOptionalRuleChecks`（与 `EnabledOptionalRules`/
  `DisabledOptionalRules` 一一对应，经 `OptionalRules` 投影得到）。`toolchain/validator --json` 在
  既有 `disabled_optional_rules`/`enabled_optional_rules` 之后追加
  `optional_rules: [{rule, check, enabled}]`（`rule`/`check` 直接来自 `OptionalRules`）。详见消费方
  反馈第 43 条，
  [消费方反馈-2026-09-14-编辑器-第43-44条.md](architecture/落地计划/消费方反馈-2026-09-14-编辑器-第43-44条.md)。【编辑器相关契约】
- `Core.Carriers.Creature.RegistryCreatureTemplateQuery`（新增公开类型，实现
  `ICreatureTemplateQuery`）：构造参数 `IDataRegistryView view`，`Get`/`HasFlag` 直接从已加载的
  `view` 现读现解析 `creature.template` 记录（不像 `CreatureFactory` 那样在构造期预先解析出完整
  强类型索引），因此不要求 `view` 提前加载完成；`Get` 未命中或字段非法时统一抛 `ArgumentException`
  （`DataFieldException` 被捕获并包成 `ArgumentException`，`InnerException` 保留原始异常，不让本
  查询的异常把整次 `ContentValidationAssembly.Run` 变成进程级未处理异常）；不做跨次缓存。
- 示例数据集 `data/_sample/creature` 新增 `summon_only` 生物模板 `creature.sample_summon_totem`
  （`npc_flags` 含 `npc_flag.summon_only`，不进 `spawn.table`，保持示例数据集 0 error 0 warning
  基线），配套 `display.map`/`l10n.text` 补齐行；新增
  `core/gameplay/spawn/tests/SpawnSummonOnlyCreatureRealSampleDataTests.cs`，用真实
  `data/_framework` + `data/_sample` 正式装配加载后内存追加一条违规 `spawn.table` 行，断言
  `SpawnSummonOnlyCreatureRule` 的 Error 分支触发。详见消费方反馈第 44 条，同上文档。

### 变更（行为变更）

- `SpawnSummonOnlyCreatureRule` 经 `ContentValidationAssembly` 装配入口默认启用（此前默认禁用）：
  `CreateRegistryCore` 未提供 `ContentValidationOptions.CreatureTemplateQuery` 时，改为
  `options.CreatureTemplateQuery ?? new RegistryCreatureTemplateQuery(registry)`（比照 1.23.0
  `DisplayMapCoverageRule` 默认启用的先例，`DisplayMapCoverageSources` 未提供时改用
  `PresentationSchemaCatalog.DefaultDisplayMapCoverageSources` 的机制不变）；显式提供
  `CreatureTemplateQuery` 的调用方仍以传入值为准（覆盖默认）。`toolchain/validator` 不需要任何
  代码改动即自动生效（该工具从未显式设置过 `CreatureTemplateQuery`）。`ContentValidationRun.
  DisabledOptionalRules`/`toolchain/validator --json` 的 `disabled_optional_rules` 在默认装配下
  现恒为空数组——两个可选规则接线参数"未提供即禁用"的机制本身未删除，只是当前两条规则均已升级为
  "未提供则用默认接线"，机制预留给未来新增的、确实需要默认禁用的可选规则。详见消费方反馈第 44
  条追记（二次根治），同上文档。【编辑器相关契约】

### 文档

- **`presentation/assembly/README.md`**：校验装配入口"判断记录"整段按新行为改写（两条可选规则
  现均默认启用，`DisabledOptionalRules` 恒为空），补充 `OptionalRules`/`TryGetOptionalRuleByCheck`
  用途说明；测试用例表同步改名后的用例。
- **`toolchain/README.md`**：`--json` 字段清单补 `optional_rules`；`--display-map-sources`/
  `SpawnSummonOnlyCreatureRule` 相关段落改写为默认接线行为。
- **`data/README.md`**："creature 示例数据（summon_only 正例演示）"一节改写：`toolchain/validator`
  侧现同样会拦下（此前"无法演示"的既有边界已根治），补充新增 pytest 用例引用与行为变更提醒。
- **`editor/docs/编辑器产品文档.md`**（v2.13 → v2.15）：第 4.4 节"校验装配入口"（签名示例注释、
  正文段落）、第 5.7.6 节"校验覆盖矩阵与可选规则装配"表格行、第 5.9 节"游戏专属定制清单"附近
  "可选规则标注"一句，均按新行为改写；变更记录新增两行（v2.14 第 43/44 条落地、v2.15 第 44 条
  追加根治）。
- 新增
  [消费方反馈-2026-09-14-编辑器-第43-44条.md](architecture/落地计划/消费方反馈-2026-09-14-编辑器-第43-44条.md)：
  反馈复现、决策依据、处理方式、验证结果、对编辑器的使用建议，及追记（二次根治）说明。

### 迁移说明

- **`SpawnSummonOnlyCreatureRule` 默认启用**：消费方若自己的 `spawn.table`（或游戏数据目录同名表）
  引用了任何 `npc_flags` 含 `npc_flag.summon_only` 的生物模板，升级到本版本后 `dotnet test`/
  `python toolchain/validate_data.py`（含未加 `--strict` 的默认严格级别，因为该检查项是 Error
  级、不受严格级别影响）会报 `spawn_summon_only_creature` 错误，需要先清理这类引用（或改用其它非
  summon_only 生物模板）再升级；若确实需要保留旧行为（不做该项检查），可显式传入
  `ContentValidationOptions.CreatureTemplateQuery` 一个"永远返回 false"的自定义
  `ICreatureTemplateQuery` 实现覆盖默认接线。
- 依赖 `ContentValidationRun.DisabledOptionalRules`/`toolchain/validator --json` 的
  `disabled_optional_rules` 字段判断"`SpawnSummonOnlyCreatureRule` 是否启用"的既有逻辑需要更新：
  该字段在默认装配下现恒为空数组，不能再用它反推该规则是否启用，应改读
  `EnabledOptionalRules`/`enabled_optional_rules`（或新字段 `optional_rules[].enabled`）。

### 编辑器接入建议

- **问题面板"本条来自可选规则"标注改用 `ContentValidationAssembly.TryGetOptionalRuleByCheck`/
  `OptionalRules`，不再硬编码映射表**——对每条诊断的 `Check` 调用
  `TryGetOptionalRuleByCheck(issue.Check, out var descriptor)`，命中即标注"来自可选规则：
  `descriptor.Description`"；框架未来新增第三个可选规则或调整检查名拼写时，这段标注逻辑不需要
  跟着改一行代码。
- **"校验设置"面板展示可选规则全量选项建议改用 `OptionalRules` 而不是 `OptionalRuleNames`**——
  每项自带 `Description`，面板可直接展示，不必自行维护规则名到说明文案的映射。
- **`SpawnSummonOnlyCreatureRule`/`DisplayMapCoverageRule` 现均有框架内置默认接线，默认装配下均
  显示"已启用"**——基础套件若走的是框架正式装配入口（`ContentValidationAssembly`），无需任何额外
  配置即自动获得默认查询/默认覆盖清单；若已有更高效的强类型索引（如运行时装配根已构造好的
  `CreatureFactory`），仍可显式提供 `ICreatureTemplateQuery`/`DisplayMapCoverageSources` 覆盖默认
  实现。
- 示例数据 `data/_sample/creature/creature.template.json` 新增的 `creature.sample_summon_totem`
  可直接用于编辑器侧"可选规则/summon_only 校验"UI 交互的手工验证或自动化测试固件，不必再自行改写
  `creature.sample_beast` 等既有记录的 `npc_flags`。

## [1.28.0] - 2026-09-14

MINOR 版本：消费方反馈处理——`text_key_exists` 此前只校验默认语言，非默认语言缺翻译对内容团队
完全静默，编辑器本地化矩阵的 `HasMissing` 判定只能自行扫描 `l10n.text` 重新推断（编辑器第 42
条）；框架示例数据集只有一种语言，编辑器无法用真实数据验证多语言/回退链/变量缺失路径（编辑器第
41 条）。核实与逐条回复见
[消费方反馈-2026-09-14-编辑器-第41-42条.md](architecture/落地计划/消费方反馈-2026-09-14-编辑器-第41-42条.md)。
独立复核通过后，整合 agent 另发现一处既有缺口一并收口：`l10n.locale.fallback` 登记为
`FieldKind.Id`，指向未登记语言的坏数据加载期不拦，要到 `L10nHost` 构造期才抛异常；详见该文档末尾
"复核附带收口"小节。
提交链：`72a6632`（第 42 条：非默认语言缺翻译报 Warning 级 `text_key_exists`）、`a3f0443`（第 41
条：示例数据补第二语言 `en_us` 与回退链/变量演示键）、`1d8ccf1`（回复文档 + 编辑器产品文档回填）、
`902cacb`（复核附带收口：`l10n.locale.fallback` 改为引用登记），分支 `wbd/editor41-42`，`--no-ff`
合入 `main` `93ae55a`，本笔提交（1.28.0 变更记录与迁移说明）。

### 新增

- `DataRegistryOptions.WarnOnMissingTranslation`（默认 `true`）——`l10n.locale` 表已登记、且不等于
  `DefaultLocale` 的每个语言，若某 `TextKey` 字段引用的键在该语言下没有对应 `l10n.text` 行，新增
  一条 Warning 级 `text_key_exists`（检查名与默认语言缺失时相同），消息附带该语言经回退链会落到
  哪个语言取文本；默认语言下缺失该键仍始终是 Error，不受本开关影响；`l10n.locale` 表未加载时天然
  跳过，不额外告警。`ContentValidationOptions` 新增同名 `WarnOnMissingTranslation`（默认
  `true`）透传给 `DataRegistryOptions`。`toolchain/validator` 新增命令行开关
  `--no-missing-translation-warning`（关闭该 Warning，恢复只查默认语言的旧行为），非 `--json`
  模式追加一行 `missing translation warning: enabled|disabled`，`--json` 模式在
  `display_map_coverage_sources` 字段之后新增布尔字段 `warn_on_missing_translation`；
  `toolchain/validate_data.py` 新增同名 `--no-missing-translation-warning` 透传参数。【编辑器相关契约】
- `l10n.locale.fallback` 字段改登记为 `FieldKind.Reference`（`referenceTable: "l10n.locale"`，
  仍 `required: false`）——`fallback` 指向本表未登记的语言 id 现在在 `DataRegistry.LoadAll`
  加载期即报 `reference_integrity` 错误（此前登记为 `FieldKind.Id`，只校验格式合法，坏数据要等到
  `L10nHost` 构造期 `ValidateNoFallbackCycle` 才抛异常）；`fallback: null`、指向已登记语言（含
  自引用）均不受影响。`toolchain/validator --list-tables --json` 里 `l10n.locale.fallback` 的
  `field_meta.kind` 由 `"Id"` 变为 `"Reference"`，新增 `reference_table: "l10n.locale"`，属登记
  元数据修正。【编辑器相关契约】
- 示例数据集 `data/_sample/l10n` 新增第二语言 `l10n.locale.en_us`（`fallback` 指向 `zh_cn`）与
  原有 56 条 `zh_cn` 文本对应的英文译文，另加两个不被任何表字段引用的纯演示键：
  `l10n.ui.sample_fallback_demo.text`（只登记 `zh_cn`，演示 `en_us` 查询经回退链取到中文正文）、
  `l10n.ui.sample_variable_demo.text`（`zh_cn`/`en_us` 均登记且含 `{amount}` 占位，演示变量缺失
  路径）。

### 文档

- **04 数据与内容管线**：第 5 节校验项清单"文本键存在"一行改写，变更记录表新增一行（本条为既有
  检查项覆盖面补齐，不改变结论，不出 ADR）。
- **`data/README.md`**：新增"l10n 示例数据（多语言/回退链/变量缺失演示）"一节，说明第二语言与
  两个演示键的用途。
- **`editor/docs/编辑器产品文档.md`**（v2.12 → v2.13）：变更记录新增一行；第 5.5.8 节"本地化
  编辑器"补充"框架契约更新（消费方反馈第 42 条）"说明，建议矩阵 `HasMissing` 复用框架诊断。
- 新增
  [消费方反馈-2026-09-14-编辑器-第41-42条.md](architecture/落地计划/消费方反馈-2026-09-14-编辑器-第41-42条.md)：
  反馈复现、决策依据、处理方式、验证结果、对编辑器的使用建议，及复核附带收口说明。

### 迁移说明

- **`WarnOnMissingTranslation` 默认开启**：消费方若翻译不全（非默认语言存在缺失键）且 CI 用
  `python toolchain/validate_data.py --strict`（把 Warning 当阻断），升级后会因新增的
  `text_key_exists` Warning 而失败——升级前建议先补齐缺失译文，或按需用
  `--no-missing-translation-warning`（`toolchain/validate_data.py`/`toolchain/validator` 均支持）
  或 `DataRegistryOptions.WarnOnMissingTranslation = false`/`ContentValidationOptions
  .WarnOnMissingTranslation = false` 关闭该项。非严格模式（`--strict` 未传）不受影响，只是多打印
  一条 Warning，不阻断。
- **`l10n.locale.fallback` 新增加载期引用完整性校验**：升级前建议先用本版
  `python toolchain/validate_data.py --strict` 排查既有 `l10n.locale` 数据——若某语言的 `fallback`
  指向一个当前未登记的语言 id，此前加载期静默接受（要到运行时 `L10nHost` 构造才报错），升级后会在
  加载期即报 `reference_integrity` 阻断错误。核实当前框架示例数据不受影响。

### 编辑器接入建议

- **本地化矩阵 `HasMissing` 可改为复用框架 `text_key_exists` Warning**——对同一份数据跑一次
  `DataRegistry.LoadAll`/`toolchain/validator --json`，按 `check == "text_key_exists" &&
  severity == "warning"` 过滤即得到与框架诊断同源的缺失清单（`field`/`record_key`/`table`
  可直接定位到具体记录字段），不必再自行扫描 `l10n.text` 重新判定一遍；消息文本里已带回退落点，
  矩阵若要展示"回退后实际显示哪个语言"，也可以直接解析复用。
- 用示例数据 `data/_sample/l10n` 的 `l10n.locale.en_us` 与两个演示键
  （`l10n.ui.sample_fallback_demo.text`/`l10n.ui.sample_variable_demo.text`）做多语言/回退链/
  变量缺失路径的端到端验证或自动化测试固件，不必再自行注入内存态第二语言。

## [1.27.0] - 2026-09-13

MINOR 版本：消费方反馈处理——数据注册表加载期内部私有的"迁移链串接"逻辑此前从未对外公开，内容
工具（编辑器"执行迁移并写回磁盘"功能）被迫照着文字描述与框架源码自行复刻链选取语义与信封改写
逻辑，且没有真实迁移用例可供端到端比对（编辑器第 40 条）；核实与逐条回复见
[消费方反馈-2026-09-13-编辑器-第40条.md](architecture/落地计划/消费方反馈-2026-09-13-编辑器-第40条.md)、
[ADR-0029](architecture/adr/0029-迁移链串接与整表迁移纳入公开契约.md)。
提交链：`4f11244`（公开 SchemaMigrator：迁移链串接 + 整表迁移）、`51955ca`（迁移链演示表 +
`--list-tables --json` 导出 + `format_data.py` 根治）、`507eaa2`（ADR-0029 + 04/README/编辑器
产品文档回填）、`c4590e2`（复核修正：链接路径、04 变更记录 ADR 列、`MigrateEnvelope` 注释），分支
`wba/editor40`，`--no-ff` 合入 `main` `7a03f55`，本笔提交（1.27.0 变更记录与迁移说明）。

### 新增

- `Core.Foundation.DataRegistry.SchemaMigrator` 公开静态类——`BuildChain`/`MigrateRow`/
  `MigrateEnvelope` 三个方法，把此前只服务于加载期内部的"迁移链串接"逻辑（从起始版本起，每一步
  取该表登记的迁移环节集合中第一条"起点等于当前版本"的环节，直到精确到达目标版本；途中缺少衔接
  某个版本的环节判定为"缺少迁移环节"）收口为公开、单一来源的静态入口，语义与原私有实现逐字一致；
  新增"整表迁移"（信封级，供内容工具写回磁盘前调用，权威处理 `schema_version`/`migrated_from`
  改写，含多次增量迁移时保留最早原始版本这条细节，`migrated_from` 存在但不合法时按未提供处理，
  不校验、不抛异常）。`DataRegistry` 加载器改为委托该类，行为不变。【编辑器相关契约】
- 数据注册表加载器新增信封级可选键 `migrated_from` 的形状校验（须为
  `[1, schema_version - 1]` 范围内的整数，否则报阻断错误）；04 号文档第 3 节勘误，澄清
  `migrated_from` 与 `schema_version` 同级，是**信封级**（表级统一）可选键，不是逐行字段。
  【编辑器相关契约】
- `toolchain/validator --list-tables --json` 每张表新增导出 `schema_version`（当前登记版本）与
  `migrations`（迁移环节清单，按登记顺序，是否可从任意起点串通仍须调用 `SchemaMigrator.BuildChain`
  确认）；`toolchain/format_data.py --schema-order` 据此根治此前的一处误判——文件信封
  `schema_version` 低于表当前版本时整体跳过字段顺序检查（旧版本文件的字段名与当前版本登记不同，
  按当前顺序重排没有意义，升版本应先调用 `SchemaMigrator.MigrateEnvelope`）。【编辑器相关契约】
- 示例数据集新增迁移链演示表 `found.migration_sample`（1→2 迁移：`label` 字段改名为
  `display_name`，无运行时消费方），随 `BuiltinSchemas.All` 自动登记、样例包自动包含，供内容
  工具用真实数据端到端验证"迁移链串接 + 整表迁移"复刻实现是否与框架行为一致，不必再自行合成
  测试专用 schema。

### 文档

- 新增 [ADR-0029](architecture/adr/0029-迁移链串接与整表迁移纳入公开契约.md)：迁移链串接、单行
  迁移、整表迁移三个入口纳入公开契约的决策记录与备选方案取舍。
- **04 数据与内容管线**：第 1.1 节总索引新增 `found.migration_sample` 一行；第 3 节字段表之后
  补充 `migrated_from` 的信封级语义说明；变更记录表 2026-09-13 第 40 条一行补上 ADR-0029 链接。
- **`data/README.md`**：补充迁移链演示表的归属说明（示例数据目录，不随主分发包发布）。
- **`editor/docs/编辑器产品文档.md`**（v2.11 → v2.12）：变更记录新增一行。
- 新增
  [消费方反馈-2026-09-13-编辑器-第40条.md](architecture/落地计划/消费方反馈-2026-09-13-编辑器-第40条.md)：
  反馈复现、决策依据、处理方式、验证结果与对编辑器的使用建议。

### 迁移说明

- **信封 `migrated_from` 键新增形状校验**：升级前建议先用本版
  `python toolchain/validate_data.py --strict` 排查既有数据文件——若某表信封携带
  `migrated_from` 键且取值非法（非整数，或不在 `[1, schema_version - 1]` 范围内），此前加载期
  静默接受，升级后会报阻断错误并中止加载。核实当前框架全部已登记表的框架/示例数据文件均不含
  这个键，不受影响，这条新校验只影响未来新写入该键的文件。
- `SchemaMigrator`/`--list-tables --json` 新增字段/`format_data.py` 新行为均为纯新增，不改变
  任何既有公开方法签名或既有字段的既有输出，无需迁移动作。

### 编辑器接入建议

- **"执行迁移并写回磁盘"直接调用 `SchemaMigrator.MigrateEnvelope(schema, envelope)`**，不要自行
  拼装 `BuildChain` + 手写信封改写逻辑；用 `toolchain/validator --list-tables --json` 的
  `schema_version`/`migrations` 字段判断哪些表存在待迁移的可能（权威答案仍须调用
  `SchemaMigrator.BuildChain` 确认链是否可用，不要自行用 `migrations` 数组拼接推断）。
- 用 `found.migration_sample` 做端到端验证：加载该表 v1 示例数据 → 调用 `MigrateEnvelope` →
  断言产出信封与直接用 `DataRegistry` 加载同一份 v1 数据得到的记录字段值一致。这张表没有任何
  运行时消费方，可放心用真实数据反复练习迁移流程。

## [1.26.1] - 2026-09-13

PATCH 版本：消费方反馈处理——`quest.def.prerequisite` 前置链循环检测缺失（编辑器第 38 条）+
`found.hook` 消费方字段引用元数据缺失（编辑器第 39 条），核实与逐条回复见
[消费方反馈-2026-09-13-编辑器-第38-39条.md](architecture/落地计划/消费方反馈-2026-09-13-编辑器-第38-39条.md)。
提交链：`5e028fb`（第 38 条实现 + 测试）、`69ea8c6`（第 39 条实现 + 测试）、`d1fa3d2`（编辑器
产品文档 + 回复文档），分支 `wax/editor38-39`，`--no-ff` 合入 `main` `6baa22c`、`13ef2f4`（合入后
CHANGELOG/落地方案/回复文档回填 + `speaker_ref` 说明更正），本笔提交（变更记录与迁移说明）。

### 修复

- **消费方反馈第 38 条（`quest.def.prerequisite` 前置链循环检测缺失）**：
  `QuestContentValidationRule` 此前只校验 `prerequisite` 表达式语法本身是否合法，从未解析表达式
  里引用的其它任务、也不检测是否成环——两个任务互相以对方为前置时，加载期 0 错误，运行期永远
  互相卡死（真实复现，见回复文档）。新增 `ValidatePrerequisiteGraph`：重新解析每条 `prerequisite`
  文本收集 `quest.*` 引用构图，跑三色标记 DFS（算法同既有 `story_tree_cycle`），新增两项阻断级
  检查——`quest_prerequisite_cycle`（成环，含自环）与 `quest_prerequisite_unknown`（前置引用了
  不存在的任务）。不新增公开 API（构造函数签名不变，新增方法均为 `private`）。
- **消费方反馈第 39 条（`found.hook` 消费方字段引用元数据缺失）**：全仓排查核实 `found.hook`
  （脚本钩子注册表）早已登记 `TableSchema`（收边 I1 落地），反馈原文"从未被登记过 schema"的
  复现 grep 只扫描了 `core/foundation/app_lifecycle/`，未覆盖真实登记所在的
  `core/foundation/hook_registry/`，是复现步骤的目录遗漏，不是表未注册；真正的空白是
  `dialog.story_tree.nodes[].performance_hook_ref`/`dialog.gossip_menu.options[].
  actions[]{kind=script}.ref`/`encounter.def.phases[].on_enter_hook`/`skill.def.
  effects[]{kind=script}.params.hook_id`/`area.trigger_def.params{trigger_type=script}.
  hook_id` 五处消费方字段的判断记录/引用元数据一直没跟着补齐。本版本五处补登
  `SoftReferenceTable("found.hook")`（不升级为 `Reference`，理由同 29/30/37 号反馈同类修复路径）；
  `data/_sample` 新增 `found/found.hook.json` 示例数据与四处消费方示例引用。纯新增可选元数据，
  不破坏既有兼容调用。【编辑器相关契约】

### 文档

- **04 第 8 节**"新任务"行勘误：补齐"循环引用检测（前置链）"对应的实现检查名
  `quest_prerequisite_cycle`/`quest_prerequisite_unknown`；新增 2026-09-13 变更记录一行。
- **`core/gameplay/quest/README.md`**：新增判断记录 14。
- **五份 schema README**（`core/gameplay/area_trigger/schema/README.md`、`core/gameplay/dialog/
  schema/dialog.gossip_menu.md`、`core/gameplay/dialog/schema/dialog.story_tree.md`、
  `core/gameplay/encounter/schema/README.md`、`core/rules/skill/schema/README.md`）：五处字段
  描述改写为如实反映"`found.hook` 已登记 `TableSchema`，本字段补软引用"，不再出现过时表述。
- **`editor/docs/编辑器产品文档.md`**（v2.9 → v2.11）：变更记录新增两行，4.1 节补充五处字段清单。
- **`dialog.story_tree.md` 判断记录 1**（合入后勘误，非本轮反馈范围）：`speaker_ref` 字段代码
  实际早已登记 `.WithSoftReference(table: "creature.template")`（消费方反馈第 30 条落地），文档
  仍写"不登记 Reference"、未提软引用，改为如实描述；只改文档不改代码。
- 新增 [消费方反馈-2026-09-13-编辑器-第38-39条.md](architecture/落地计划/消费方反馈-2026-09-13-编辑器-第38-39条.md)：
  两条反馈的复现、根因、处理方式、验证结果。

### 迁移说明

- **任务前置成环或引用未知任务的内容从本版起在加载期阻断**：升级前建议先用本版
  `python toolchain/validate_data.py --strict` 排查既有 `quest.def` 内容，确认没有任务互为前置
  （或更长的环）、也没有 `prerequisite` 引用不存在的任务 id；此前这类内容加载期 0 错误、运行期
  卡死，升级后会在加载期直接报 `quest_prerequisite_cycle`/`quest_prerequisite_unknown` 并阻断。
- 五处 `found.hook` 消费方字段新增软引用元数据是纯新增、不参与加载期引用完整性校验，不改变任何
  既有加载行为，无需迁移动作。

### 兼容声明

本版本对 1.12.0～1.26.0 期间编译的旧消费方二进制保持兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的验证——1.12.0 基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，且
独立的公开 API 表面差异比对（`toolchain/abi_surface`）输出 `breaks=0`（本次两条反馈均未新增
公开 API：38 条新增方法均为 `private`，39 条只是给既有 `FieldSchema` 补调用既有的
`.WithSoftReference(...)`）。

## [1.26.0] - 2026-09-12

MINOR 版本：消费方反馈处理——`declareReference` 登记可读回（编辑器第 37 条），核实与逐条回复见
[消费方反馈-2026-09-12-编辑器-第37条.md](architecture/落地计划/消费方反馈-2026-09-12-编辑器-第37条.md)。
提交链：`6d0ecbd`（实现 + 测试）、`baca53a`（文档 + 回复），分支 `wau/editor37`，`--no-ff` 合入
`main` `95a7f79`、`84b3e46`（合入后 CHANGELOG/落地方案文档回填），本笔提交（1.26.0 变更记录与
迁移说明）。

### 新增

- `IDataRegistryView` 新增 `GetReferenceDeclarations(): IReadOnlyList<ReferenceDeclaration>`
  （只读回吐经 `IDataRegistry.DeclareReference` 声明的全部引用关系：源表/源字段/目标表/是否
  可选/登记来源），`IDataRegistry.DeclareReference` 新增带来源标注的重载；`toolchain/validator
  --list-tables --json` 新增 `reference_declarations` 导出（见架构文档 04 第 4 节勘误、
  `architecture/落地计划/消费方反馈-2026-09-12-编辑器-第37条.md`）——`declareReference` 登记
  此前只驱动加载期硬校验、没有公开读回方式，内容工具（引用图/影响分析）只能按值弱推断某字段
  是否是引用；新契约面均为带默认实现的接口成员/新增重载/新增只读导出字段，不破坏既有兼容
  调用。【编辑器相关契约】
- `Presentation.Assembly.SchemaAudit` 新增元数据门禁检查 `declared_reference_unregistered`
  （告警级）：凡经 `declareReference` 声明的 (表, 字段) 若字段元数据既无 `ReferenceTable`/
  `ReferenceDomain`、也无 `SoftReferenceTable`/`SoftReferenceDomain`、也无 `AllowedValues`，
  即报告——防止"`declareReference` 登记了引用但字段元数据未同步"的遗漏（消费方反馈第 37 条原始
  案例 `skill.proc_def.trigger_skill` 即属此类，已补登 `SoftReferenceTable("skill.def")`）。
  【编辑器相关契约】

### 迁移说明

- **`GetReferenceDeclarations`/带来源 `DeclareReference` 重载均为带默认实现的新增接口成员**：
  不要求任何既有 `IDataRegistryView`/`IDataRegistry` 实现类型改动即可编译通过；未覆盖
  `GetReferenceDeclarations()` 的第三方实现（如测试替身）调用该方法恒返回空集合，不抛异常。
  `toolchain/validator --list-tables --json` 新增的 `reference_declarations` 字段追加在既有
  `display_map_coverage_sources` 之后、闭合大括号之前，不改动任何既有字段，按字段名读取 JSON 的
  既有消费方不受影响。
- **新增告警级检查 `declared_reference_unregistered` 可能在消费方自定义 catalog 上出告警**：若
  消费方在自己的 schema 目录（未纳入本仓库 `toolchain/schema_audit_allowlist.json`）里调用了
  `registry.DeclareReference(...)`，但对应字段元数据未同步登记 `ReferenceTable`/
  `SoftReferenceTable`/`SoftReferenceDomain`/`AllowedValues` 之一，升级后跑
  `Presentation.Assembly.SchemaAudit`/`toolchain/validator --schema-audit` 会新增一条告警（不是
  错误，不阻断加载）；如确认该遗漏是有意为之，可在自己的 allowlist 中豁免，或补登对应的引用
  元数据（推荐，参照本条 `skill.proc_def.trigger_skill` 补登 `SoftReferenceTable` 的写法）。

### 兼容声明

本版本对 1.12.0～1.25.1 期间编译的旧消费方二进制保持兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的验证——1.12.0 基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，且
独立的公开 API 表面差异比对（`toolchain/abi_surface`）输出 `breaks=0`（新增 478 行，均为纯新增
公开成员，未删改任何既有公开签名）。

## [1.25.1] - 2026-09-11

PATCH 版本：消费方反馈处理——进出战斗死亡复活通用能力（M-C11）——同图读档未同步空间索引、
读档后 `CombatHost`/`PowerHost` 战斗态分歧、延迟复活 pending 队列未随读档/实体销毁/
`WorldSim.ClearAll`/宿主释放失效，三项均已根治并补测试回归。核实与逐条回复见
[消费方反馈-2026-09-11-读档空间索引与复活生命周期.md](architecture/落地计划/消费方反馈-2026-09-11-读档空间索引与复活生命周期.md)。
提交链：`d2e95d0`（实现 + `C11_LifecycleReloadTests.cs` 7 例）、`7aad3ec`（架构文档回填 +
回复文档），分支 `was/c11-lifecycle`，`--no-ff` 合入 `main` `36453e4`、`0f4995c`（合入后
CHANGELOG/落地方案/回复文档回填）、本笔提交（变更记录与迁移说明）。

### 修复

- **C11-RELOAD（读档空间索引未同步）**：`world.current_position` 段 `Load` 此前直接改写
  `PlayerUnit.Position` 字段，绕开 `Core.Carriers.Unit.WorldUnitAccess.SetPosition`，空间索引
  不会同步（同图读档后玩家位置正确但查询不到）。`Core.Carriers.Unit.UnitPersistable` 新增
  `CurrentPosition(PlayerUnit, IUnitAccess)` 重载，`Load` 改经 `IUnitAccess.SetPosition` 写入；
  `Core.Foundation.EngineAdapter.ISpatialQuery` 新增默认接口方法
  `ResyncPositions(IEnumerable<(Id, Vec2)>)` 供派生状态重建阶段做一次全量兜底重同步。
- **C11-RELOAD（读档战斗态不一致）**：读档恢复 `in_combat` 此前只直接调用
  `IPowerHost.SetInCombat`，未触碰 `Core.Rules.Combat.CombatHost` 自身的进出战斗登记表（唯一
  来源），读档后两者分歧。`CombatHost` 新增 `ClearCombatState(Id)`/`RestoreCombatState(Id, bool)`；
  `Core.Gameplay.Assembly.PlayerVitalsPersistable` 新增 `(PlayerUnit, PowerHost, CombatHost?)`
  三参构造重载，`Load` 改经 `RestoreCombatState` 写入；读档恢复顺序固定为"先清空运行期战斗态、
  再按存档值恢复"。
- **C11-PENDING-LOAD/C11-CLEANUP（延迟复活生命周期）**：`Core.Gameplay.Death.DeathPolicyHost`
  的延迟复活队列此前没有任何失效机制——读档前遗留的记录会在读档完成后到期覆盖刚恢复好的状态；
  `WorldSim.ClearAll` 后下一 tick 对已销毁单位复活会抛异常。新增公开方法 `ClearPending()`（读档
  前清空，供 `GameplayAssembly` 的 `IDerivedStateRebuilder.BeforeLoad` 调用）；新增
  `IDisposable` 实现（宿主释放时取消订阅并清空队列）；新增对 `entity.destroyed` 事件的订阅，
  单位销毁后立即摘除对应记录；`Execute` 在调用 `ReviveUnit` 前新增目标单位存在性校验，不存在时
  丢弃记录并记诊断，不抛异常。

### 文档

- architecture/05：对象模型/单位访问层文档补充"位置写入统一经 IUnitAccess.SetPosition"的
  约束说明。
- architecture/06：规则层战斗文档补充 CombatHost 作为进出战斗状态唯一来源的说明。
- architecture/10：存档与持久化文档新增第 3 节"读档恢复顺序与一致性契约"小节（先清空派生
  运行期状态、再按段恢复）。
- 四个模块 README（`core/carriers/unit`、`core/gameplay/assembly`、`core/gameplay/death`、
  `core/rules/combat`）补充本次新增公开成员的判断记录索引。
- 新增 `architecture/落地计划/消费方反馈-2026-09-11-读档空间索引与复活生命周期.md`：三项
  问题的复现（含真实探针 FAIL 输出）、根因、处理方式、修复位置、复现前后对比、测试清单、
  新增公开成员清单（ABI 影响）、给消费方的兼容性提示。

### 迁移说明

- 读档后战斗态以战斗宿主（`CombatHost`）为准：若消费方此前依赖"读档后 `IPowerHost.IsInCombat`
  可能与 `CombatHost.IsInCombat` 不一致"这一未定义行为，需要复核相关逻辑——修复后两者恒一致。
- 延迟复活队列在读档（同图/跨图，不限于 `reload_save` 策略）、对应单位销毁（含
  `WorldSim.ClearAll`）、`DeathPolicyHost.Dispose()` 后均会失效：若消费方此前依赖"读档不影响
  死亡前遗留的延迟复活记录"这一未定义行为（例如刻意利用旧记录在读档后延迟复活玩家），需要
  复核相关配置——该行为建立在一个未定义、且与"读档后玩家状态完全由存档决定"契约相反的副作用
  之上，不建议继续依赖。
- 消费方若自行构造过 `PlayerVitalsPersistable(player, powers)`（两参）或
  `UnitPersistable.CurrentPosition(player)`（单参），这两条路径仍然可用（源码兼容），但不会
  得到本次修复——需要改用新增的三参/双参重载才能获得读档后空间索引/战斗态一致的保证。
- `DeathPolicyHost` 新实现了 `IDisposable`：若消费方自己持有该类型实例并在某个时机（如切换到
  主菜单、卸载游戏世界）主动释放，建议调用 `Dispose()`。
- 除此之外无需任何改动：公开签名未删改，调用频率、调用时机均不受限制。

### 兼容声明

本版本对 1.12.0～1.25.0 期间编译的旧消费方二进制保持兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的验证——1.12.0 基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，且
独立的公开 API 表面差异比对（`toolchain/abi_surface`）输出 `breaks=0`；本次新增的公开成员全部
是"新增方法/新增构造函数重载/新增默认接口方法"，未删改任何既有公开签名。

## [1.25.0] - 2026-09-11

MINOR 版本：消费方反馈"投射物与技能位移通用能力反馈"（M-C10）第 1/2/3/4 节处理，核实与逐条回复见
[消费方反馈-2026-09-11-技能位移连续模式.md](architecture/落地计划/消费方反馈-2026-09-11-技能位移连续模式.md)、
[消费方反馈-2026-09-11-地面坐标施法.md](architecture/落地计划/消费方反馈-2026-09-11-地面坐标施法.md)、
[消费方反馈-2026-09-11-投射物敌友策略与ActiveCount.md](architecture/落地计划/消费方反馈-2026-09-11-投射物敌友策略与ActiveCount.md)。
提交链：连续位移 `015e882`（ADR-0026 + 契约/schema）/`b65af95`（实现 + 46 例测试）/`2115ecc`
（文档 + 回复），分支 `wao/continuous-move`，`--no-ff` 合入 `main` `f38dd14`；地面坐标施法
`bc59d9a`（ADR-0027 + 契约/schema）/`d63dca3`（实现 + 验收测试）/`917452f`（文档 + 回复），
分支 `wap/ground-cast`，`--no-ff` 合入 `main` `ae4996c`；投射物敌友策略 `334c260`（ADR-0028 +
契约/schema）/`4ff9ae1`（实现 + 测试）/`33ec751`（文档 + 回复），分支 `waq/projectile-policy`，
`--no-ff` 合入 `main` `dbd6ad6`；`09aabe7`（本版本变更记录与计划文档回填，含合并期间根治
`SkillTestSupport.cs` 一处构造函数重载选择漂移）。

### 新增

- `move` 效果原语新增可选参数 `motion: instant|continuous`（缺省 `instant`，既有三种子类型
  `charge`/`leap`/`knockback` 的既有语义不变）。`continuous` 时新增 `speed`/`duration`/
  `blocking`/`sample_step` 参数，转为逐 tick 推进并做导航裁决（阻挡后按策略停在阻挡前最后可
  通行点或回到起点），经新增依赖倒置接口 `IControlledDisplacementSink`（`SkillHost.
  DisplacementSink`/`EffectDispatcher.DisplacementSink` 可写属性，由 `CarriersAssembly` 装配期
  接入 `core/carriers/unit` 的 `MovementHost`/`MovementTickHandler`）交给 L3 落地逐 tick 推进/
  阻挡裁决/终止条件（到达/受阻/控制打断/施法者死亡/显式 Stop）。详见
  [ADR-0026](architecture/adr/0026-技能位移的连续模式.md)、
  [消费方反馈-2026-09-11-技能位移连续模式.md](architecture/落地计划/消费方反馈-2026-09-11-技能位移连续模式.md)。
- 新增独立施法入口 `ISkillHost.CastSkillAtGround`/`CastPipeline.CastSkillAtGround`，以显式世界
  坐标点（而非选中的单位列表）为落点，与既有 `CastSkill` 结构性互斥。新增 `skill.def.
  ground_target` 字段（缺省 `false`，`SkillDef.AllowGroundTarget`）门禁；射程/视线仅当
  `range > 0` 时校验（受阻返回新增的 `GroundTargetNoLineOfSight`）；可行走校验新增依赖
  `INavigation2D`（可选注入），不可行走返回新增的 `GroundTargetUnreachable`；技能未声明返回新增
  的 `GroundTargetUnsupported`。请求（`GroundCastRequest`）携带坐标快照策略
  （`AtRequest`/`AtRelease`），效果落地时改用新增的 `ITargetHost.ResolveAtPoint` 解析命中单位。
  `EffectContext`/`SkillCastStartEvent`/`SkillCastSuccessEvent` 各新增一个 `GroundPoint` 属性。
  详见 [ADR-0027](architecture/adr/0027-地面坐标施法请求.md)、
  [消费方反馈-2026-09-11-地面坐标施法.md](architecture/落地计划/消费方反馈-2026-09-11-地面坐标施法.md)。
- `projectile` 效果原语新增可选的敌友关系策略参数 `relation_policy`
  （`default|hostile_only|friendly_only|locked_target_only`，缺省 `default`）与 `pierce_order`
  （`nearest|hostile_first`，缺省 `nearest`），裁决优先级为"施法者排除 → 目标锁定 → 关系筛选 →
  标签筛选 → 穿透计数 → 命中效果回灌"；二者均为可选字段，缺省组合下候选筛选、命中顺序与事件序列
  逐字节不变。`ProjectileHost` 新增公开契约 `Factions`（`IFactionMatrix?`，由
  `CarriersAssembly` 装配期自动回填为与 `Rules.Factions` 同一实例）、`ActiveCount`（存活投射物
  数）、`IsQuiescent`（`ActiveCount == 0` 的别名）与 `ClearAll()`（经 `GameplayAssembly.
  LeaveMap` 既有出图收尾入口自动接线）。详见
  [ADR-0028](architecture/adr/0028-投射物碰撞的敌友关系策略.md)、
  `core/carriers/projectile/README.md`"敌友关系策略"一节、
  [消费方反馈-2026-09-11-投射物敌友策略与ActiveCount.md](architecture/落地计划/消费方反馈-2026-09-11-投射物敌友策略与ActiveCount.md)。

### 迁移说明

- **`motion` 缺省瞬移不变**：`move` 效果原语不声明 `motion` 字段时行为与 1.25.0 之前逐字节
  一致（`charge`/`leap`/`knockback` 三种子类型仍是一次性 `SetPosition`），仅显式声明
  `motion: continuous` 才转为逐 tick 推进；未装配 `core/carriers`（未接入
  `IControlledDisplacementSink`）的纯 L2 测试/集成场景下，`continuous` 会降级为直接按算出的
  目标点 `SetPosition`（记一条警告，不抛异常），不需要任何一次性迁移。
- **`ground_target` 缺省不允许**：技能不声明 `skill.def.ground_target: true` 时，
  `CastSkillAtGround` 一律返回 `GroundTargetUnsupported`，既有技能内容与既有 `CastSkill` 单位
  目标入口不受影响，不需要任何数据迁移；需要开放地面坐标施法的技能需显式在内容表打开该字段。
- **`relation_policy` 缺省兼容**：`projectile` 效果原语不声明 `relation_policy`/`pierce_order`
  时按 `default`/`nearest` 既有裁决口径运行，候选筛选、命中顺序与事件序列逐字节不变；若消费方
  自行手工构造 `ProjectileHost`（不经 `CarriersAssembly` 装配根），`Factions` 属性不会自动注入，
  `hostile_only`/`friendly_only`/`hostile_first` 三项取值静默退化为兼容默认行为并记一条诊断
  警告（不是报错），需要该能力时须自行设置该属性。
- **`ActiveCount` 语义与 `ClearAll()` 接入**：`ProjectileHost.ActiveCount`/`IsQuiescent` 反映的
  是"截至上一次 `Advance`/`ClearAll` 调用后的存活投射物数"，经本仓库
  `Core.Gameplay.Assembly.GameplayAssembly.LeaveMap`（既有出图收尾入口）触发的
  `IWorldSim.ClearAll()` 已自动接线 `ProjectileHost.ClearAll()`；若消费方在该入口之外的路径
  直接调用 `IWorldSim.ClearAll()`（如脱离场景路由的手工重置/测试），需要自行一并调用
  `ProjectileHost.ClearAll()` 才能让 `ActiveCount`/`IsQuiescent` 立即归零反映"已清空"，否则会
  保持清空前的数值直到下一次正 `dt` 的 `Advance`（不产生幽灵伤害，纯粹是可观测性滞后）。

### 兼容声明

本版本对 1.12.0～1.24.0 期间编译的旧消费方二进制保持兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的验证——1.12.0 基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，且
独立的公开 API 表面差异比对（`toolchain/abi_surface`）输出 `breaks=0`（`allowed=0`,
`additions=456`）；带默认实现的接口新增成员（`ISkillHost.CastSkillAtGround`/
`ITargetHost.ResolveAtPoint`）与新增可写属性（`SkillHost.DisplacementSink`/
`EffectDispatcher.DisplacementSink`、`ProjectileHost.Factions`）均不改变任何既有构造函数的
物理签名；`CastPipeline`/`SkillHost` 新增的 `navigation` 尾参数各自补回一个物理签名不变、全部
参数必填的 `[Obsolete]` façade 构造函数（同既有 `factions` 参数处理方式）。经同一探针验证旧
编译 consumer 无需重新编译即可继续运行（含接口默认成员转发门禁
`InterfaceDefaultMemberForwardingTests`，覆盖 `ISkillHost`/`ITargetHost` 全部默认成员）。

## [1.24.0] - 2026-09-11

MINOR 版本：消费方反馈第 35/36 条处理，核实与逐条回复见
[消费方反馈-2026-09-11-编辑器-第35条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第35条.md)、
[消费方反馈-2026-09-11-编辑器-第36条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第36条.md)。
提交链：第 35 条 `cc59037`（纯函数收口）/`2e9bb6f`（分析器 + 测试）/`09a25fb`（文档 + 回复），
分支 `wam/loot-analyzer`，`--no-ff` 合入 `main` `52bb607`；第 36 条 `17b9846`（根治代码 +
测试）/`5b75876`（文档 + 回复），分支 `wal/growth-fix`，`--no-ff` 合入 `main` `15d0bcc`；
`e87d716`（第 36 条回复文档数学求和记号被误判为失效相对链接的修复）；`74489dc`（本版本变更
记录与计划文档回填）。

### 新增

- `Core.Numbers.Progression.IProgressionHost` 新增可选覆盖的 `ApplyGrowthToCurrentLevel(Id
  unitId)`（C#8 默认接口方法，默认空操作，既有实现方零改动仍可编译）——按当前已注册等级重新
  聚合"2 级到当前等级"的曲线成长量并整体写回属性宿主，供调用方在把单位注册到某个 > 1 的起始
  等级之后一次性补写成长，与升级（`AddXp`）/读档恢复（`RestoreState`）共用同一份聚合逻辑。详见
  [消费方反馈-2026-09-11-编辑器-第36条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第36条.md)。
- `Core.Gameplay.Loot` 新增公开 `LootTableAnalyzer.ExpectedProbabilities(LootTableDef,
  LootAnalysisContext): IReadOnlyList<LootExpectedOutcome>`（掉落表期望概率分析），按叶子
  （嵌套 `loot.*` 展开后的 `item.*` 引用）聚合给出至少掉落一次的概率、期望数量、来源路径，与
  真实抽取（`LootHost.Roll`）共用同一份条件筛选/权重归一实现；`chance_each`/`weighted_pick_one`
  单抽、嵌套展开、`guaranteed_min` 保底对直接条目的组合均为解析式精确值，不放回多抽候选池超过
  阈值（`LootAnalysisContext.ExactWithoutReplacementMaxEntries`，默认 12）或嵌套引用落在保底
  候选池时标注近似（`LootExpectedOutcome.IsApproximate`）。条件求值支持三种模式
  （`AssumeTrue`/`AssumeFalse`/按给定 `IExprHost` 求值）。不改变 `LootHost`/`LootTableDef`
  既有契约，不改变任何抽取行为。【编辑器相关契约】详见
  [消费方反馈-2026-09-11-编辑器-第35条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第35条.md)。

### 修复

- 修复出生等级 > 1 且配置了成长曲线（`stat_growth_ref`）的生物再升级时，"出生态已经计入的一段
  成长"被再次叠加进属性修正，导致数值偏高的问题——`CreatureFactory.ApplyStats` 不再把曲线成长
  叠进基础值，出生态成长统一经 `IProgressionHost.ApplyGrowthToCurrentLevel`（与正常升级、读档
  恢复共用同一份聚合实现）以属性修正形式写入。出生等级 1（既有示例数据现状）的生物不受影响；
  已有存档不需要任何迁移（生物本身不逐个持久化，玩家出生等级恒为 1，历史存档从未落入本条缺陷
  窗口）。详见
  [消费方反馈-2026-09-11-编辑器-第36条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第36条.md)。

### 迁移说明

- **出生等级 > 1 且配成长曲线的生物升级数值变化为正确值**：此前该类生物再升级时会重复计入
  "出生段"的成长增量，属性偏高；本版本根治后升级得到的数值是正确值（偏低于修复前）。生物本身
  不逐个持久化到存档，玩家出生等级恒为 1 不受影响，因此不需要任何一次性存档迁移；若消费方代码/
  测试对旧的（偏高的）数值做过硬编码断言，需要按新的正确值更新。
- **分析 API 不改变抽取**：`LootTableAnalyzer.ExpectedProbabilities` 是纯只读分析入口，与
  `LootHost.Roll` 共用同一份条件筛选/权重归一实现但互不影响；不调用该 API 的既有消费方无需
  任何改动。

### 兼容声明

本版本对 1.12.0～1.23.0 期间编译的旧消费方二进制保持兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的验证——1.12.0 基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，且
独立的公开 API 表面差异比对（`toolchain/abi_surface`）输出 `breaks=0`（`allowed=0`,
`additions=386`）；带默认实现的接口新增成员（`IProgressionHost.ApplyGrowthToCurrentLevel`）
经同一探针验证旧编译 consumer 无需重新编译即可继续运行（含接口默认成员转发门禁
`InterfaceDefaultMemberForwardingTests`）。

## [1.23.0] - 2026-09-11

MINOR 版本：消费方反馈第 30/32/33/34 条处理，核实与逐条回复见
[消费方反馈-2026-09-11-编辑器-第30-32-33-34条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第30-32-33-34条.md)。
提交链：`e11a55b`（第 30 条）、`8331310`（第 32 条，ADR-0025）、`7127bd5`（第 33 条）、
`bbae580`（第 34 条）、`81f40c2`（回复文档），分支 `waj/editor5`，`--no-ff` 合入 `main`
`a0623b2`；`e1fbeb2`（本版本变更记录与计划文档回填）。

### 新增

- `Core.Foundation.EngineAdapter.AssetRefConventions`：`sprite_set_id`/`icon_id` 资源引用标识
  到资产相对路径的公开约定（`SpriteSetDirectory`/`IconFile`/`TryParseSpriteSetId`/
  `TryParseIconId`），收口此前分散的多处私有实现（[ADR-0025](architecture/adr/0025-资源引用标识到资产相对路径的约定纳入公开契约.md)）。【编辑器相关契约】
- `Core.Numbers.PowerSet.IPowerHost.TryGetPower(Id, Id, out double)`：容错查询资源池当前值，
  未注册/未持有时返回 `false` 而不抛异常（带默认实现，不影响既有实现）。
- `Presentation.Assembly.PresentationSchemaCatalog.DefaultDisplayMapCoverageSources`：外形映射
  覆盖检查的默认内容表清单（`skill.def`/`skill.aura_def`/`item.template`/`creature.template`/
  `gobj.template`，均以 `id` 比对）。【编辑器相关契约】
- `SchemaAudit` 元数据门禁新增检查 `id_description_reference_hint`（告警级）：`Id`/`IdList`
  字段 `Description` 提到"引用"/"指向"却未登记任何引用元数据时告警，防止第 30 条同类遗漏。

### 变更（编辑器相关契约）

- `Core.Carriers.Creature.CreatureOptions.DefaultPowerTypes` 类型改为 `IReadOnlyList<Id>?`，
  默认值由 `[Health]` 改为 `null`；`CreatureFactory.Spawn` 未显式覆盖时改为按模板/数据集实际
  定义的资源类型注册（详见消费方反馈第 33 条）——生成单位默认注册的资源类型范围随之变化，若
  消费方曾依赖"不设置该选项时只注册 Health"的隐式行为需复核，详见文档"对消费方提示"一节。
- `ContentValidationOptions.DisplayMapCoverageSources` 未指定时默认使用
  `PresentationSchemaCatalog.DefaultDisplayMapCoverageSources`，`DisplayMapCoverageRule` 默认
  启用（此前默认禁用）——外形映射覆盖检查现默认启用，消费方数据若有外形域引用缺失将报错；
  `validator --list-tables --json` 新增 `display_map_coverage_sources` 导出字段（详见消费方
  反馈第 34 条）。
- `arch.class.primary_stat` 等 21 处字段补登 `SoftReferenceTable`/`SoftReferenceDomain`，另有
  22 处字段描述改写去除"引用"/"指向"误导性措辞（目标不确定，不登记软引用）；资源路径约定
  （`sprite_set_id`/`icon_id`）纳入公开契约面（详见消费方反馈第 30/32 条）。

### 修复

- 示例数据集补齐 `skill.def`/`skill.aura_def`/`item.template` 共 8 条记录的外形映射行，配合
  `DisplayMapCoverageRule` 默认启用后仍保持 `validate_data.py --strict` 0 error。

### 迁移说明

- **生成单位默认注册的资源类型范围变化**：`CreatureFactory.Spawn` 未显式设置
  `CreatureOptions.DefaultPowerTypes` 时，此前只注册 `Health` 一种，现在改为注册当前数据集里
  全部已登记的 `arch.power_type` 定义。若消费方代码曾依赖"不设置该选项时只注册 Health"的隐式
  行为（如断言某单位没有除 Health 外的资源池），升级后需复核；如仍需旧行为，显式设置
  `DefaultPowerTypes = new[] { WellKnownPowers.Health }`。
- **外形映射覆盖检查现默认启用**：`DisplayMapCoverageRule` 此前默认禁用（需调用方显式接线
  `(table, idField)` 列表才生效），本版本起默认对 `skill.def`/`skill.aura_def`/`item.template`/
  `creature.template`/`gobj.template` 五张表启用。消费方数据集若有上述表的记录尚未配齐
  `display.map` 外形映射行，升级后 `validate_data.py --strict`/`validator` 会新增
  `display_map_coverage` 错误；如需暂时保留旧行为，显式传入
  `DisplayMapCoverageSources = Array.Empty<(string, string)>()`（`ContentValidationOptions`）。
- **资源路径约定纳入契约**：`sprite_set_id`/`icon_id` → 资产相对路径的推导规则此前分散在多处
  私有实现、行为不变，现收口为 `Core.Foundation.EngineAdapter.AssetRefConventions` 公开契约
  （`toolchain/asset_import/ref_conventions.py` 为脚本侧对等实现）；建议消费方改为直接调用，不要
  再照抄样例反推，往后调整须走 ADR。
- 其余改动（`IPowerHost.TryGetPower`、`PresentationSchemaCatalog.DefaultDisplayMapCoverageSources`、
  `SchemaAudit.id_description_reference_hint`、21 处字段补登软引用）均为纯新增或元数据补充，既有
  消费方无需任何改动即可继续编译运行。

### 兼容声明

本版本对 1.12.0～1.22.0 期间编译的旧消费方二进制保持兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的验证——1.12.0 基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，且
独立的公开 API 表面差异比对（`toolchain/abi_surface`）输出 `breaks=0`（`allowed=0`,
`additions=349`）；带默认实现的接口新增成员（`IPowerHost.TryGetPower`）经同一探针验证旧编译
consumer 无需重新编译即可继续运行。

## [1.22.0] - 2026-09-11

MINOR 版本：消费方反馈处理——结算中间步骤经真实施法路径不可观测——`CombatOptions` 新增可选
`ResolveTrace: Action<EffectContext, ResolveResult>?`（结算追踪回调），`Resolver.Resolve` 三条
返回路径统一接入，供编辑器"右侧结算预览"/"简易战斗回放"直接订阅真实结算的分步中间值，不必自行
复刻结算公式脱离真实施法路径单独计算。核实与逐条回复见
[消费方反馈-2026-09-11-编辑器-第31条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第31条.md)。
提交链：`d5da983`（契约 + 实现 + 测试）、`bba9474`（06 勘误 + 判断记录 + 回复文档 + 编辑器
产品文档 v2.7）、`49fabe5`（`--no-ff` 合入 `main`）。

### 新增

- `CombatOptions` 新增可选 `ResolveTrace: Action<EffectContext, ResolveResult>?`（结算追踪
  回调，见架构文档 06 第 4.1 节勘误、`architecture/落地计划/消费方反馈-2026-09-11-编辑器-
  第31条.md`）——供内容工具/诊断消费方在结算管线（`Resolver.Resolve`）每次真实返回前（含
  判定步骤即终止的短路分支）获取本次 `EffectContext` 与完整 `ResolveResult`（含各步骤中间值
  `Steps`），覆盖技能瞬发/读条完成/引导 tick、光环周期效果、Proc 触发的嵌套施法、弹道命中后
  效果全部落地路径；未设置零开销，不改变既有行为；回调抛出的异常被捕获吞掉，不影响真实结算。
  【编辑器相关契约】

### 文档

- 06 第 4.1 节勘误：补充说明结算管线可选接入一个结算追踪回调（`CombatOptions.ResolveTrace`），
  仅供内容工具/诊断消费，管线每一次返回前（含判定步骤即终止的短路分支）都会把本次输入与完整
  输出（含 `Steps`）交给该回调恰好一次，不改变固定步骤本身，未接入时零开销；变更记录新增一行。
- `core/rules/combat/README.md`：判断记录 16——记录方案选型（三选一选方案 1 的理由）、落地路径
  覆盖清单、异常吞掉的诊断通道约定；目录树同步补充新增测试文件说明。
- `editor/docs/编辑器产品文档.md`（v2.6 → v2.7）：第 4.1 节契约面清单补充 `ResolveTrace`
  签名与语义；第 5.5.2 节"右侧结算预览"、第 5.8 节"简易战斗回放"补充其实现直接订阅
  `ResolveTrace`、不必自行复刻结算公式的说明；变更记录新增 v2.7 条目。

### 迁移说明

- 纯新增可选属性，`CombatOptions` 既有（隐式）构造签名不受影响，既有消费方无需任何改动即可
  继续编译。未设置（缺省 `null`）时只多一次 null 判断，零开销，不改变既有输出。
- 回调本身抛出的异常被 `Resolver` 捕获后经 `ICombatDiagnostics.Warn` 记一次警告并继续，不向上
  传播、不影响结算与事件——消费方若依赖回调异常中断结算这一未定义行为，需要复核；正常场景下
  无需任何改动。

### 兼容声明

本版本对 1.12.0～1.21.1 期间编译的旧消费方二进制保持兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的验证——1.12.0 基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，且
独立的公开 API 表面差异比对（`toolchain/abi_surface`）输出 `breaks=0`；本次改动只新增一个可选
属性与私有实现细节，未删改任何既有公开签名。

## [1.21.1] - 2026-09-11

PATCH 版本：消费方反馈处理——只读就绪查询影响后续充能状态——`CooldownTracker` 只读充能查询
（`GetCharges`/`GetChargeRechargeRemaining`/`GetEffectiveChargesMax`/`GetEffectiveRechargeTimeScaled`/
`IsSkillReady`/`GetCooldown`）不再惰性创建或修改充能账本；充能上限发生变化（`charges` 维度
SpellMod 生效/失效）时，已存在账本的当前充能数按守恒规则跟随调整（提高同步增、降低夹取并按需清零
恢复窗口），不再因"查没查询过一次"而产生分叉。核实与逐条回复见
[消费方反馈-2026-09-11-充能查询副作用.md](architecture/落地计划/消费方反馈-2026-09-11-充能查询副作用.md)。
提交链：`2c16551`（`CooldownTracker` 修复 + 单元/集成测试）、`656588c`（06 第 3.5 节勘误 +
判断记录 + 回复文档 + 落地方案登记）、本笔提交（变更记录与迁移说明）。

### 修复

- `core/rules/skill/core/CooldownTracker.cs`：只读方法改经新增私有方法 `ComputeReadOnlySnapshot`
  计算快照，不再触达惰性创建路径 `GetOrCreateChargeState`；`GetCooldown(Id, SkillDef)` 此前经
  `GetCharges(Id, SkillDef)` 间接触发创建的同源独立读路径缺口一并修复。
- `ChargeState` 新增字段 `KnownEffectiveMax`（记录"上一次对账时的有效上限"），新增共用纯函数
  `ReconcileForMaxChange` 实现充能上限变化的守恒规则：提高 Δ → 当前充能数 `+= Δ`（恢复窗口不变）；
  降低 → 当前充能数夹取到不超过新上限，夹取后若恰好满充能则距下次恢复剩余清零。写路径
  （`GetOrCreateChargeState`/`AddCharge`/`StartCooldown`）与推进路径（`AdvanceCharges`）在真正
  触达/推进账本前先对账，只读路径（`ComputeReadOnlySnapshot`）计算同一结果但不写回，保证查询
  是否发生过不影响后续原生施法的实际充能数与连续可施放次数。
  （消费方反馈处理，
  [消费方反馈-2026-09-11-充能查询副作用.md](architecture/落地计划/消费方反馈-2026-09-11-充能查询副作用.md)，
  提交 `2c16551`）。

### 文档

- 06 第 3.5 节勘误：补充"充能只读查询不得产生状态、`charges` 维度上限变化的守恒规则"说明，并同步
  更新变更记录。
- `core/rules/skill/README.md` 新增判断记录条目 47，完整记录现象/根因/两条根治规则/测试清单。

### 迁移说明

- 若消费方此前的数值/技能设计**依赖**"查询后充能上限提高不生效"这一修复前的旧行为（例如刻意利用
  该路径营造"充能暂不到账"的效果），需要复核相关配置——该行为建立在一个未定义、且与只读查询契约
  相反的副作用之上，不建议继续依赖。除此之外无需任何改动：查询频率、调用时机均不受限制，公开
  签名未发生任何变化。

### 兼容声明

本版本对 1.12.0～1.21.0 期间编译的旧消费方二进制保持兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的验证——1.12.0 基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，且
独立的公开 API 表面差异比对（`toolchain/abi_surface`）输出 `breaks=0`；本次改动全部落在私有字段
与私有方法层面，未新增、未删改任何公开签名。

## [1.21.0] - 2026-09-11

MINOR 版本：消费方反馈处理——冷却充能与公共冷却缺少统一只读查询接口——新增
`ISkillHost.GetSkillReadiness(unitId, skillId)` 统一只读就绪快照（充能上限/当前充能数/下次充能
恢复剩余、技能冷却/分类冷却/公共冷却剩余、修饰后的有效值、按位标记的阻塞来源），裁决口径与
`CastSkill` 施法管线步骤 3（冷却/充能）、步骤 4（公共冷却）一致；`CastSkill` 仍是唯一最终裁决。
核实与逐条回复见
[消费方反馈-2026-09-11-冷却充能只读查询.md](architecture/落地计划/消费方反馈-2026-09-11-冷却充能只读查询.md)。
提交链：`d4ed9cd`（契约 + 实现 + 验收测试）、`0e87e1a`（06 勘误 + 判断记录 + 回复文档）、
`2b601b4`（`--no-ff` 合入 `main`）。

### 新增

- `Core.Rules.Skill.SkillReadiness`（不可变值类型，`SkillId`/`IsReady`/`BlockingSources`/
  `SkillCooldownRemaining`/`CategoryCooldownRemaining`/`GlobalCooldownRemaining`/`MaxCharges`/
  `CurrentCharges`/`NextChargeRemaining`/`EffectiveCooldownDuration` 十个只读字段）、
  `[Flags] SkillReadinessBlockers`（`None`/`SkillCooldown`/`CategoryCooldown`/`GlobalCooldown`/
  `NoCharges`，可按位或组合）、`CategoryCooldownStatus`（分类 id + 该分类剩余的不可变结构体）。
- `ISkillHost` 新增带默认实现的 `GetSkillReadiness(Id unitId, Id skillId): SkillReadiness`
  （C# 8 default interface member，公开 API 只新增，既有实现方零改动仍可编译）；默认实现只能借
  `GetCooldown` 拼一个降级快照，公共冷却不参与判定，其余字段为 `null`。生产实现
  `Core.Rules.Skill.SkillHost.GetSkillReadiness` 显式覆盖，直接从 `CooldownTracker`/
  `SpellModResolver`/`SkillOptions` 读取精确字段，与 `CastSkill`（经 `CastPipeline`）裁决逐字
  对齐；`Core.Rules.Assembly.RulesAssembly` 内代理 `DeferredSkillCastQuery` 显式转发到内层实现。
- `CooldownTracker` 补充四个只读公开出口：`CurrentTimeFactor`、`GetChargeRechargeRemaining`、
  `GetEffectiveChargesMax`、`GetEffectiveRechargeTimeScaled`（均为既有私有状态/私有方法的只读
  转发，不新增账本、不改变既有写路径）。

### 文档

- 06 第 3.1/3.6/7 节同批勘误：`SkillHost` 契约补充只读查询接口说明。
- `core/rules/skill/README.md` 新增本次接口的"谁实现、谁调用"说明。

### 迁移说明

- 纯新增，既有 `ISkillHost` 实现方（含消费方自定义实现/测试替身）无需任何改动即可继续编译；未
  显式覆盖 `GetSkillReadiness` 时落回默认接口成员提供的降级快照（`IsReady`/`BlockingSources` 只能
  从 `GetCooldown` 粗略推断，公共冷却不参与判定，其余字段为 `null`）——这是刻意设计的兼容兜底。
- `GetSkillReadiness` 只覆盖施法管线步骤 3（冷却/充能）、步骤 4（公共冷却）两项拒绝原因，不含
  存活/控制、学派锁定、资源、目标合法性、距离与视线等其它步骤；`IsReady=true` 不代表此刻
  `CastSkill` 一定成功，仍需以 `CastSkill` 返回值为唯一最终裁决。消费方可用本快照替代自建计时器
  做 UI 呈现（冷却圈、充能图标、GCD 转圈），不再需要在客户端侧另行维护一套冷却/充能镜像状态。

### 兼容声明

本版本对 1.12.0～1.20.0 期间编译的旧消费方二进制保持兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的验证——历史基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，独立的
公开 API 表面差异比对（`toolchain/abi_surface`）输出 `breaks=0`，未发现任何破坏性变更；本版本
新增的公开契约面（`SkillReadiness`/`SkillReadinessBlockers`/`CategoryCooldownStatus`/
`ISkillHost.GetSkillReadiness`/`CooldownTracker` 四个只读出口等）均为新增类型、新增成员或带
默认实现的接口新增成员，不删改任何既有公开签名。

## [1.20.0] - 2026-09-11

MINOR 版本：消费方反馈第 28/29 条（编辑器内容校验）落地——`FieldSchema` 新增固定取值集合登记
（`AllowedValues`/`WithAllowedValues`）与软引用元数据（`SoftReferenceTable`/`SoftReferenceDomain`/
`WithSoftReference`），`NpcFlagIds` 新增 `All`；07/06/04 文档同步 + 06 事件表字段语义勘误。核实与
逐条回复见
[消费方反馈-2026-09-11-编辑器-第28-29条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第28-29条.md)。
提交链：`92228d1`（`FieldSchema` 固定取值登记与软引用元数据）、`f940e47`（文档同步第 28/29 条）、
`689d0e6`/`efafa91`（06 事件表字段语义勘误分支与 `--no-ff` 合入 `main`）、`4975e40`（变更记录）。

### 新增

- `Core.Foundation.DataRegistry.FieldSchema` 新增可选 `AllowedValues`（`IReadOnlyList<Id>`）+
  `WithAllowedValues(...)`：`IdList`/`Id` 两种字段种类的固定合法取值集合登记，`DataRegistry.LoadAll`
  据此新增运行期检查项 `field_allowed_value`（`IdList` 逐元素、`Id` 单值均覆盖）；与 `FreeIds`/
  `ReferenceTable` 语义上互斥，元数据门禁（`toolchain/validator --schema-audit`）新增自洽检查
  `idlist_allowed_values_conflict`；既有 `idlist_reference_target` 检查扩展接受 `AllowedValues`
  作为合法登记之一。
- `FieldSchema` 新增可选 `SoftReferenceTable`/`SoftReferenceDomain` + `WithSoftReference(table:,
  domain:)`：`Id`/`IdList` 字段"软引用"元数据，只传达内容工具（编辑器自动补全/跳转）可用的目标表
  提示，**不参与**任何加载期引用完整性校验，不改变既定分层边界；登记种类限制新增元数据门禁
  `soft_reference_kind`。
- `toolchain/validator --list-tables --json` 的 `field_meta` 数组新增 `allowed_values`/
  `soft_reference_table`/`soft_reference_domain` 三个字段，与既有键并列，不改动既有输出。
- `Core.Carriers.Creature.NpcFlagIds` 新增 `All`（六个合法职能标志 Id 的有序集合）；
  `CreatureSchemas.Template` 的 `npc_flags` 字段改用 `WithAllowedValues(NpcFlagIds.All)` 登记，
  取代 `CreatureContentValidationRule` 此前手写的重复校验（该规则收口为空实现，保留类型与
  `IValidationRule` 注册点供未来扩展）——非法职能标志现由 `field_allowed_value` 单独报告一次，
  不再与旧 `creature_content` 检查项重复报告同一处问题。
- `creature.template` 的 `ai_rotation_ref`（→`ai.rotation`）、`ai_behavior_ref`
  （→`ai.behavior_profile`）、`loot_table_ref`（→`loot.table`）、`display_ref`（→`display.map`）
  补登软引用；全仓走查另有 21 处 `_ref`/`target_ref` 字段（`skill.def`/`item.template`/
  `gobj.template`/`encounter.def`/`encounter.level`/`econ.currency`/`area_trigger.params`/
  `quest.def`/`achv.def`/`arch.class` 等模块）按已确认目标表补登软引用，完整清单见回复文档。
  `creature.template.on_hit_reaction_ref`/`on_death_reaction_ref` 核实为预留字段（全仓无消费方、
  无目标表），标注 `Description` 并说明，不登记软引用。

### 文档

- 07 文档 `npc_flags` 改写为正式 `npc_flag.<name>` 写法（此前裸词如 `vendor`），补充运行期额外
  映射为单位标签 `tag.npc.<name>` 的消费方说明（`CreatureFactory.Spawn` → `unit.Tags`）；
  `on_hit_reaction_ref`/`on_death_reaction_ref` 标注预留。
- 04 文档 3.4/5 节同步补充两项新登记（`AllowedValues`/软引用）与三个新检查名
  （`field_allowed_value`/`idlist_allowed_values_conflict`/`soft_reference_kind`）。
- 06 事件表 `combat.damage_dealt` 行补充字段语义说明——`triggerChainDepth`（产生本次结算的
  `EffectContext.triggerChainDepth` 原样戳入）、`attackInstanceId`（同一次结算批次共用的攻击
  实例 id）两字段的含义解释，不改变已登记的字段列表与事件契约（`689d0e6`）。
- `editor/docs/编辑器产品文档.md`（v2.6）契约面清单同步补充。

### 迁移说明

- `npc_flags` 字段非法值现由登记表（`NpcFlagIds.All` 经 `WithAllowedValues` 登记）在加载期阻断
  （`field_allowed_value` 检查项）；此前该字段仅由 `CreatureContentValidationRule` 运行期校验，
  现由统一的登记表校验路径接管，报告口径可能与旧实现存在措辞差异，校验覆盖范围不变。
- 软引用元数据（`SoftReferenceTable`/`SoftReferenceDomain`）仅供内容工具读取使用（自动补全/
  跳转提示），**不参与**任何加载期引用完整性校验；已登记软引用的字段若填写了目标表中不存在的
  id，加载期不会报错（与登记前行为一致）。
- `creature.template.on_hit_reaction_ref`/`on_death_reaction_ref` 核实为预留字段（当前全仓无
  消费方、无目标表），本版本仅补充 `Description` 说明，不登记软引用、不引入任何新校验；消费方
  若已自行填充这两个字段的值，行为不受影响。

### 兼容声明

本版本对 1.12.0～1.19.0 期间编译的旧消费方二进制保持兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的验证——历史基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，独立的
公开 API 表面差异比对（`toolchain/abi_surface`）输出 `breaks=0`，未发现任何破坏性变更；本版本
新增的公开契约面（`FieldSchema.AllowedValues`/`WithAllowedValues`/`SoftReferenceTable`/
`SoftReferenceDomain`/`WithSoftReference`/`NpcFlagIds.All` 等）均为新增类型、新增成员或新增
链式登记方法，不删改任何既有公开签名。

## [1.19.0] - 2026-09-11

MINOR 版本：五个并行分支整合——新增格子吸附（`grid_snap`）通用能力、接口默认成员转发门禁、ABI
属性/索引器/事件形态覆盖、发布锁重建共用脚本；修复七项 P2（PRES-118-CAMERA/CORE-118-QUEST/
CORE-118-CAST/PRES-118-SFX/PRES-118-VIEW/TOOL-118-ABI/TOOL-118-LOCK）与消费方反馈第 27 条
【编辑器相关契约】；九类文档更新（DOC-118-01～09）。第二十方深度审核修复（codex 第十八轮，
基线 `d6fda65`）五个并行分支整合：`wy/tool18`（TOOL-118-ABI/TOOL-118-LOCK 两项 P2 工具链
缺陷 + 九类文档更新中除 `grid_snap` 外的全部行）、`ww/core18`（CORE-118-QUEST/CORE-118-CAST
两项 P2 运行时缺陷）、`wx/pres18`（PRES-118-CAMERA/SFX/VIEW 三项 P2 表现层缺陷）、
`wz/gridsnap`（`grid_snap` 通用能力实现）、`w27/expr-forward`（消费方反馈第 27 条，接口默认
成员转发门禁）。归档与核实表见
[audit-d6fda65-20260911/](architecture/落地计划/audit-d6fda65-20260911/)、
[followup-2026-09-11.md](architecture/落地计划/audit-d6fda65-20260911/followup-2026-09-11.md)、
[消费方反馈-2026-09-11-编辑器-第27条.md](architecture/落地计划/消费方反馈-2026-09-11-编辑器-第27条.md)。
提交链：`907c15d`/`d40938c`（`ww/core18`：CORE-118-QUEST/CAST）、`003f73d`/`82cbad1`/`6a9b1cf`
（`wx/pres18`：PRES-118-CAMERA/SFX/VIEW）、`349d484`（`wy/tool18`：TOOL-118-ABI/LOCK + 九类
文档其余行）、`638d62e`/`b436226`/`aba64cd`（`wz/gridsnap`：格子吸附通用实现）、
`bba98ae`/`9d696cf`（`w27/expr-forward`：消费方反馈第 27 条）、
`96db5a6`/`f5fb6a3`/`b84af22`/`932c349`/`2fa600b`（五分支依次 `--no-ff` 合入 `main`）、
`88aba80`/`3d7caec`（整合阶段回填核实表、变更记录、能力索引与全量门禁验收）。

### 新增

- **格子吸附（`grid_snap`）通用能力落地**：`Core.Foundation.Common.IGridSnapPolicy` + 默认实现
  `GridSnapPolicy`（连续坐标换算成所属格子中心点，可替换策略）；`Core.Foundation.EngineAdapter.
  ShapeGeometry.Contains`（形状几何包含判定，从 `core/gameplay/encounter` 私有实现提升为 L1
  可复用）、`Shape.Expand`（外扩边距）、`GridSnapShapeQuery.QueryShapeAtCellCenters`（两阶段
  查询：外扩广相位 + 候选格子中心点精确重判）；`TimeModelDefinition.GridSnapCellSize` +
  `found.time_model.grid_snap` 子结构登记（`cell_size: Number`，`> 0`）。运行时接线：离散模式
  下移动在每个模拟步结束吸附到格子中心（`core/carriers/unit.MovementTickHandler`）；
  `nearest_in_shape`/`all_in_shape`/`ISkillHost.FindUnits` 三处范围形状查询按候选所属格子中心
  点判定；连续模式或未声明 `grid_snap` 时行为逐字节不变。此前该字段只有登记、无运行时消费，
  `toolchain/schema_audit_allowlist.json` 对应豁免条目随之移除。
- **接口默认成员转发门禁**：新增反射测试 `InterfaceDefaultMemberForwardingTests`（`presentation/
  assembly/tests/`），自动扫描组合/包装某个接口的实现类型，校验其是否显式转发了该接口全部带
  默认实现的成员（未转发即回落到"什么都不做"的默认实现，属常见疏漏模式，第 27 条即一例）；
  当前 18 处经审查确认"有意不转发"的实现登记豁免并附理由。
- **ABI 属性/索引器/事件形态覆盖**：`toolchain/abi_surface` dump/compare 新增编码属性、索引器、
  事件访问器的 `static`/`virtual`/`abstract`/`sealed-override` 修饰组合（此前只有 method 行
  覆盖这些维度），并配套修复 `SplitPropertyIdentity` 的"身份"判定范围，使"可见性放宽 + static
  化"复合变化不再被误判为纯可见性放宽而漏判。
- **发布锁重建共用脚本**：新增 `toolchain/_lock_writeback.ps1`（`New-WsGameLockObject`/
  `Write-WsGameLockFile`/`New-WsGameLockObjectFromZip`），`build.ps1` 正常路径与
  `.github/workflows/release.yml`"缺附件修复"分支统一改为调用同一份实现，三个 DLL 哈希映射
  参数均为 PowerShell `Mandatory`，结构上杜绝遗漏；`toolchain/get_framework.ps1` 新增版本阈值
  判定（锁文件 `version >= 1.15.0` 时缺 `headless_dlls`/`validator_dlls` 直接判定损坏）。

### 修复

- **CORE-118-QUEST**：任务定义热重载删除该定义后，`GetActiveObjectives(unit)` 等单位级枚举/
  事件消费路径直接按 `questId` 索引已删除的定义，抛 `KeyNotFoundException`；随后以不同目标数
  恢复同一 `questId` 时 `UpdateProgress` 抛 `IndexOutOfRangeException`（迁移锚点依赖 `Reload`
  临时快照，中间隔一次整体删除就找不到旧形状）。改用新增 `TryGetDefinition`（定义缺失时该条
  进度本次跳过，不抛异常、进度保留）与常驻于 `QuestRuntimeState` 的 `LastKnownObjectives` 作为
  迁移锚点（不再依赖 `Reload` 临时快照），不受中间隔了多少次删除/恢复影响。新增 9 个回归测试。
- **CORE-118-CAST**：施法者死亡/销毁已生效、但对应 `unit.died`/`entity.destroyed` 事件延后到
  本次推进调用结束后才派发时，`CastPipeline` 的防御性重验分支此前只摘除施法状态、不发任何
  终结事件，当前读条与排队请求收不到 `Interrupted`/`QueueCleared` 通知。新增私有方法
  `TerminateCast` 把全部会摘除施法状态的路径（正常打断、施法者死亡/销毁防御性重验）收敛到同一
  条终结路径，摘除操作天然幂等，不重复发送终结事件。新增 7 个回归测试。
- **PRES-118-CAMERA**：三个生产装配入口未在场景加载完成后重新 `Follow`，进图/切图/读档重进后
  镜头静止不再跟随。`CameraHostOptions` 新增 `FollowTargetResolverOnReset`（新增构造重载，不
  改已发布双参构造签名），`PresentationAssembly` 默认补"继续跟随同一玩家单位"的解析函数，三个
  生产入口全部自动获得该接线。
- **PRES-118-SFX**：两个生产装配入口的逐帧维护漏调用 `Sfx.Update`，连续两次播放同一个缺失音效
  资源时第二次请求永久卡在 pending（没有回调可等、也没有 `Update` 驱动超时清理）。新增
  `PresentationAssembly.UpdatePlaybackMaintenance` 把 `Feedback`/`Vfx`/`Sfx` 三步逐帧维护收敛
  为一处，三个生产装配入口统一改为调用该方法，不再各自罗列播放器清单。
- **PRES-118-VIEW**：同图直接读档（不经场景卸载重建）时，继续存活的既有角色 View 的插值快照
  不会刷新，`GetInterpolatedPosition` 会继续插值出读档前的旧坐标直到下一次无关 tick 才"顺带"
  刷新，画面与刚恢复的逻辑位置不同步。`ViewBinder.OnSaveLoaded` 补充对存活 View 直接用恢复后
  位置重设插值起点（`prev=curr`），不发起任何模拟 tick、不改变任何逻辑结算。

- **TOOL-118-ABI**：`toolchain/abi_surface/SurfaceDumper.cs` 的属性/索引器/事件行此前不编码
  访问器的 `static`/`virtual`/`abstract`/`sealed-override`——public 实例属性改成同名同类型
  static 属性（或反过来）两次 dump 输出逐字节相同，ABI 门禁判 `breaks=0`，而旧编译消费方运行期
  `System.MissingMethodException`。按与 method 行同一套修饰编码补齐三类成员；配套修复
  `SurfaceCompareLogic.SplitPropertyIdentity`（可见性放宽豁免判定用的"身份"此前不含这些新 token，
  会让"可见性放宽 + static 化"复合变化被误判成纯放宽而漏判）。新增 8 个测试用例（含真实
  `dotnet build` 端到端负例）；用新版工具对 1.12.0/1.13.0 两份历史基线重跑当前工作树六个 DLL
  均 `breaks=0`，未发现历史真实破坏。
- **TOOL-118-LOCK**：`.github/workflows/release.yml`"缺附件修复"分支（zip 已存在、lock 缺失时从
  已验证 zip 重建 lock）此前手写的锁对象构造逻辑漏写 `headless_dlls`/`validator_dlls` 两个字段，
  使 `toolchain/get_framework.ps1` 对缺字段一律按"旧版本、跳过校验"处理，`Adapters.Stub.dll`/
  `Validator.dll` 被篡改也检测不出来。新增共用脚本 `toolchain/_lock_writeback.ps1`
  （`New-WsGameLockObject`/`Write-WsGameLockFile`/`New-WsGameLockObjectFromZip`），`build.ps1`
  正常路径与 `release.yml` 修复分支均改为调用同一份实现，三个 DLL 哈希映射参数均为 PowerShell
  `Mandatory`、结构上杜绝再漏传；`get_framework.ps1` 新增版本阈值（锁文件 `version >= 1.15.0`
  时缺 `headless_dlls`/`validator_dlls` 直接判定损坏拒绝，`< 1.15.0` 仍走既有兼容跳过）。新增
  3 个测试用例，含篡改副本场景下"正常路径风格锁"与"修复分支风格锁"同等阻断的专项回归。
- **消费方反馈第 27 条【编辑器相关契约】**：具体游戏/工具实际使用的正式装配入口是多个模块
  `IExprSchema` 登记表组合/包装出的门面（如 `PresentationSchemaCatalog.FullExprSchema`），
  其 `KnownKeys`/`KnownGroups` 此前逐级落回接口默认实现，九个分组全部返回空集合，未反映任何
  成员登记表的真实内容。四处组合/包装实现补齐显式转发（并集聚合全部成员登记表结果），新增
  `InterfaceDefaultMemberForwardingTests` 反射门禁防止同类遗漏再次发生。

### 文档

- ADR 总清单两处"23 条"更正为"24 条"（`architecture/README.md`、根 `README.md`），补
  `adr/0024` 一行。
- 根 `README.md` 落地状态摘要：ATB 从"明确非目标"改列"延期/预留"；teleport 一行改为
  "`teleport_points` 引用目标完整性校验"（元素结构已登记，未提供的是跨表引用完整性检查）。
- `editor/docs/编辑器产品文档.html` 版本头/变更记录表/第 4.1 节契约面清单表按 md 版（v2.5）同步
  补齐 `Range`/`field_meta`（`group`/`unit`/`reference_table`/`reference_domain`/`free_ids`/
  `map_key`/`map_value`）/`TryGetAll`/`TryQuery`/`OverrideDiagnostic` 新字段/`ExprNode`/
  `ExprIssue` 区间/`KnownKeys`/`KnownGroups` 等新契约；`editor/README.md` 版本号同步至 v2.5 并
  注明 HTML 由 md 手工同步生成（仓库内无自动转换脚本）。
- `architecture/14_资产规格书模板.md`"基础架构提供 / 游戏层提供"表导入工具责任行改为"通用实现是
  框架交付物，游戏负责具体资源/参数/阈值/专属扩展"，按 ADR-0014 与当前 `toolchain/` 落地状态更正
  此前写反的责任归属。
- `architecture/13_新游戏接入指南.md` 第 6 步"命中帧同步与武器风格默认关闭/未接线"一句拆成三条：
  命中帧开关（默认关闭）、武器风格数据来源接线（三处生产装配根均已默认构造，非"未接线"）、
  Swing/Impact 特效消费（已实现未默认调用）。
- `core/rules/combat/README.md` 判断记录 4、`architecture/06_规则层_属性技能战斗AI.md` 第 4.1
  节：偏斜/格挡与暴击默认可叠加（此前称暴击判定被跳过，与 `Resolver` 实际实现不符），是否互斥
  由消费方策略决定，不改算法本身；`core/rules/combat/tests/{CombatTestSupport,ResolverHitTableTests}.cs`
  新增两条回归测试钉住该行为。
- `toolchain/README.md` 新增"`abi_surface` dump/compare 覆盖范围补齐（TOOL-118-ABI 根治）"一节。
- `architecture/04_数据与内容管线.md`、`architecture/13_新游戏接入指南.md` 变更记录补充
  `grid_snap` 已落地运行时消费；`core/foundation/sim_loop/README.md` 第 7 条从"F1c 判断记录
  （不登记子结构）"改写为"已落地"。
- `core/gameplay/quest/README.md`、`core/rules/skill/README.md` 判断记录同步更新，纠正
  CORE-118-QUEST/CORE-118-CAST 两处已被复现推翻的过期假设；`architecture/06_规则层_属性技能
  战斗AI.md` 第 3.6 节补充"施法终结事件保证"勘误段。
- `presentation/camera`、`presentation/assembly`、`presentation/vfx_sfx`、`presentation/
  view_binding` 四份 README 补 PRES-118-CAMERA/SFX/VIEW 三项修复的判断记录；`architecture/09_
  表现层.md`、`architecture/13_新游戏接入指南.md` 补对应勘误行。
- `architecture/04_数据与内容管线.md` 第 6.2 节补充"按分组枚举已登记 key/枚举全部已登记分组"
  的组合登记表并集口径说明（消费方反馈第 27 条）；`architecture/11_工程规范与测试.md` 第 7/8
  节新增接口默认成员转发门禁的说明与豁免登记口径。

### 迁移说明

- 离散模式下声明 `found.time_model.grid_snap`（`{cell_size: Number}`，`> 0`）后，移动会在每个
  模拟步结束吸附到格子中心，`nearest_in_shape`/`all_in_shape`/`ISkillHost.FindUnits` 三处范围
  形状查询也会按候选所属格子中心点判定包含关系——若现有内容已声明该字段但依赖"只登记、不生效"
  的旧行为（例如手工在数据侧做过等效的格子对齐处理），升级后需要复核是否与新的运行时吸附叠加；
  未声明该字段或使用连续模式的既有内容行为逐字节不变。
- `toolchain/get_framework.ps1` 对 `version >= 1.15.0` 的锁文件新增强制字段校验：缺
  `headless_dlls`/`validator_dlls` 任一字段即判定锁文件损坏、拒绝落地（此前一律按"旧版本、跳过
  该项校验"处理）。仅影响手工编辑或由非官方工具生成、缺失这两个字段的 1.15.0 及以上版本锁文件；
  由 `build.ps1`/`release.yml` 正常发布流程生成的锁文件不受影响。`version < 1.15.0` 的锁文件仍
  走既有兼容跳过分支。
- 施法者死亡/销毁后若相应的 `unit.died`/`entity.destroyed` 事件延后到本次推进调用结束后才派发，
  当前读条/引导与排队请求现在保证收到对应的终结事件（`skill.cast_interrupted` 或携带
  `QUEUE_CLEARED` 的 `skill.cast_failed`）——若消费方此前依赖"这种时序下不会收到终结事件"这一
  缺陷行为（例如自行补发过等效通知），升级后需要复核并移除这类补偿逻辑；同一次施法/排队请求不会
  重复收到终结事件。
- `games/_template` 等生产装配入口的镜头选项（`GameOptions.CameraResetFollowOnSceneLoadFinished`）
  与逐帧播放维护现在被真正透传/驱动到底层（此前声明了但未接入/未调用）——若消费方此前依赖旧的
  "配置了也不生效"行为，升级后需要复核对应模板配置的实际取值是否符合预期。

### 兼容声明

本版本对 1.12.0～1.18.0 期间编译的旧消费方二进制保持兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的验证——历史基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，独立的
公开 API 表面差异比对（`toolchain/abi_surface`）输出 `breaks=0`，未发现任何破坏性变更；本版本
新增的公开契约面（`IGridSnapPolicy`/`GridSnapShapeQuery`/`ShapeGeometry`/`CameraHostOptions`
新构造重载/`PresentationAssembly.UpdatePlaybackMaintenance`/`QuestHost.TryGetDefinition` 等）均
为新增类型、新增成员或新增构造重载，不删改任何既有公开签名。

## [1.18.0] - 2026-09-11

MINOR 版本：编辑器相关契约新增（ADR-0024 动态键映射登记、消费方反馈第三批第 21 条 + 第四批第
24/25/26 条）+ 施法生命周期事件新增实例标识与失败原因码 + 读条完成当帧新冷却被提前推进的时序
修复 + 06 §8 事件词汇表勘误。核实与逐条回复见
[消费方反馈-2026-09-10-编辑器-第三批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md)
第 21 条、
[消费方反馈-2026-09-10-编辑器-第四批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第四批.md)、
[消费方反馈-2026-09-10-施法时序与实例标识.md](architecture/落地计划/消费方反馈-2026-09-10-施法时序与实例标识.md)。
提交链：`dd2fdec`/`9a57361`/`f8ffc3d`（ws/adr24：ADR-0024 新增 + 第二批 18 字段登记 + 文档
对齐）、`9b3fcbd`/`6d76569`（wt/editor4：第四批第 25/26 条根治 + 文档同步）、`f6d99dd`/
`75b3f41`/`37ca21b`（wu/cast17：读条完成新冷却提前推进根治 + `CastInstanceId` + 文档）、
`d6822b0`/`7e87741`/`3fb88d7`（三分支合并）、`f519a2f`（06 §8 `combat.damage_dealt` 事件表
勘误）、`4bb70fe`（变更记录与提交哈希回填）。

### 新增

- **ADR-0024（编辑器相关契约，消费方反馈第三批第 21 条）**：`FieldSchema` 新增 `Map`（动态键
  映射子结构登记，与 `Fields`/`Variants` 并列，新增 `MapSchema` 契约类型，键约束"引用表/引用域/
  自由字符串"三选一）；`DataRegistry` 按登记做加载期键值校验；`SchemaAudit` 新增 `Map` 相关
  自洽检查；`toolchain/validator --list-tables --json` 每个顶层字段的 `field_meta` 新增
  `map_key`（键约束）/`map_value`（值种类）。18 个此前退化为 JSON 子编辑的字段中 12 个改用
  `Map` 登记、1 个改用既有 `Item` 登记（`Array<String>`），元数据门禁白名单从 20 条收敛到 7
  条。详见
  [消费方反馈-2026-09-10-编辑器-第三批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md)
  第 21 条。
- **消费方反馈第四批第 26 条**：`RecordExprMapping`/`RecordExprMapping.ToExprValueKind(FieldKind)`
  （字段种类 → Expr 标量类型的映射函数，驱动 `RecordExprSchema.For` 的 `self.<field>` 自动
  登记）由框架内部实现放宽为 `public static`，供编辑器等消费方直接复用同一份映射规则。详见
  [消费方反馈-2026-09-10-编辑器-第四批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第四批.md)。
- **施法生命周期事件实例标识（消费方反馈"施法时序与实例标识"）**：`SkillCastStartEvent`/
  `SkillCastSuccessEvent`/`SkillCastFailedEvent`/`SkillCastInterruptedEvent` 新增只读属性
  `CastInstanceId`（与 `CastResult.CastInstanceId` 同一枚 id）及对应构造重载，用于跨事件关联
  同一次施法请求的完整生命周期；新增失败原因码 `CastFailureReason.QueueCleared`（法术队列请求
  被新请求覆盖、或所在读条/引导被打断清空时发出）。详见
  [消费方反馈-2026-09-10-施法时序与实例标识.md](architecture/落地计划/消费方反馈-2026-09-10-施法时序与实例标识.md)。

### 修复

- **消费方反馈第四批第 25 条**：`DataRegistry.Query(string, string)` 便捷重载（及底层
  `RecordExprSchema.For(TableSchema)`）不再把 `FieldKind.Expr` 字段注册为 `self.<field>` 引用
  ——该字段种类的值是待求值的表达式文本，没有明确取值语义，此前误按 `String` 注册；改为与
  `IdList`/`Vec2`/`Object`/`Array` 同等对待，不登记。全仓 grep 确认无真实使用者受影响。详见
  [消费方反馈-2026-09-10-编辑器-第四批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第四批.md)。
- **读条/引导完成当帧新冷却被提前推进**：`SkillHost.Update`（连续模式）此前"结算触发"与"冷却
  等既有计时器统一推进"共用同一次调用、且结算触发在前，导致同一完成时刻新创建的计时状态
  （冷却/公共冷却/充能恢复窗口/光环持续时间与周期累加器）被同一个 `dt` 二次扣减；改为先推进
  既有计时器、再处理结算触发，GCD/充能/光环同类问题一并修复。详见
  [消费方反馈-2026-09-10-施法时序与实例标识.md](architecture/落地计划/消费方反馈-2026-09-10-施法时序与实例标识.md)。

### 文档与门禁

- 04 第 6.3/6.4 节、`QuestExprSchemaEntries.cs` 注释纠正"`event` 分组未登记 → 不报"的措辞
  （消费方反馈第四批第 24 条）：明确该分组根本不做静态签名校验、`TryGetSignature` 恒放行，
  不存在"已登记/未登记"两态；运行时行为本身未变。
- 04 §3.5 补充 `Map` 动态键映射登记形态说明（ADR-0024）；06 §3.6/§8 勘误（读条完成当帧新冷却
  提前推进的起算时刻规则、四个施法生命周期事件补充 `castInstanceId`）；06 §8 事件词汇表
  `combat.damage_dealt` 行补登记既已实现的 `triggerChainDepth`/`attackInstanceId` 两个字段。

### 迁移说明

- 元数据门禁白名单收敛后，本版内容若使用了 12 个已改用 `Map` 登记的字段（`arch.class.
  base_stats`/`arch.race.stat_mods`/`creature.template.base_stats`/`prog.level_curve.
  entries[].growth`/`display.map.{anchor_points,default_slot_meshes,material_params}`/
  `display.anim_set.clips`/`display.weapon_style.{cast_anim_override,impact_vfx_override}`/
  `encounter.def.phases[].ai_rotation_override`/`ai.behavior_profile.transitions`），键约束
  为"引用表"的字段（如 `arch.class.base_stats` 的键须引用 `stat.definition`）从本版起在加载
  期做键值校验，此前完全不校验；请在升级前用本版 `toolchain/validate_data.py --strict` 排查
  现有内容里这些字段的键是否为合法引用。
- `DataRegistry.Query(string, string)` 谓词文本中若曾写过 `self.<Expr 字段名>` 引用，从本版
  起不再登记为 `self.<field>` 引用（回退为 `<id_literal>` 解析）——全仓 grep 确认框架自身与
  `data/` 现有内容均无此用法，属安全收紧；若消费方自有内容存在类似写法，需要相应调整。
- 四个施法生命周期事件（`skill.cast_start`/`skill.cast_success`/`skill.cast_failed`/
  `skill.cast_interrupted`）新增 `CastInstanceId` 属性与对应构造重载，均为纯新增（既有构造
  物理签名不变，见下方"兼容声明"）；`skill.cast_failed` 新增失败原因码
  `CastFailureReason.QueueCleared`，若消费方订阅该事件时对 `reasonCode` 做了穷举分支（如
  `switch` 语句），升级后需要补上这个新分支或提供默认兜底分支。
- 读条/引导完成当帧新创建的计时状态（冷却/公共冷却/充能恢复窗口/光环持续时间与周期累加器）
  从本版起不再被同一个 `dt` 二次扣减；若现有内容或消费方测试断言依赖了"提前推进"这一此前的
  缺陷行为（如手动补偿过冷却剩余时长），升级后需要复核并移除这类补偿逻辑。

### 兼容声明

本版本对 1.12.0～1.17.0 期间编译的旧消费方二进制保持兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的验证——历史基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，独立的
公开 API 表面差异比对（`toolchain/abi_surface`）输出 `breaks=0 allowed=0 additions=275`，
未发现任何破坏性变更；`Map`/`CastInstanceId`/`RecordExprMapping` 公开可见性放宽等新增契约面
均为新增类型/新增成员/新增重载，不删改任何既有公开签名。

## [1.17.0] - 2026-09-10

MINOR 版本：编辑器相关契约新增（消费方反馈第三批 18/19/20/22/23 条）+ 框架数据/持久化合同修复
（外部深度审核第十九方 V-01/V-02/V-03 + 消费方反馈"同一光环多个 Proc 触发器"）+ ABI 工具门禁补强
（ABI-1162-01）+ 五处文档勘误（DOC-162-01～05）。核实与逐条回复见
[消费方反馈-2026-09-10-多Proc触发器.md](architecture/落地计划/消费方反馈-2026-09-10-多Proc触发器.md)、
[消费方反馈-2026-09-10-编辑器-第三批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md)、
[architecture/落地计划/audit-4faab73-20260910/followup-2026-09-10d.md](architecture/落地计划/audit-4faab73-20260910/followup-2026-09-10d.md)。
提交链：`5ef4128`/`e103f18`（多 Proc 触发器修复与文档）、`f6f8c15`/`44f320d`（wo/core17：
V-01/V-02/V-03 + DOC-162-01/05）、`f602c8c`（wp/tool17：ABI-1162-01 + DOC-162-02/03/04 + 归档）、
`870884c`/`2297766`/`15d38e7`（wq/editor3：消费方反馈第三批 18/19/20/22/23）、`d7c1c6c`/`b344704`/
`b797f03`（三分支合并）、`3542b08`/`e4970d2`（跟进核实与全量门禁回填）。

### 新增

- 编辑器相关契约新增 API（消费方反馈第三批 18/19/20/22，详情见文首"编辑器相关契约"索引小节
  1.17.0 一行）：`IExprSchema.KnownKeys`/`KnownGroups`；`ExprIssue`/`ExprNode` 全系子类新增
  `Start`/`Length` 源文本位置区间；`IDataRegistryView.TryGetAll`/`TryQuery`；`IDataSource.Root`；
  `OverrideDiagnostic` 新增 `OverridingRootIndex`/`OverriddenRootIndex`/
  `OverridingRelativePath`/`OverriddenRelativePath`。
- **ADR-0024 + 第 21/25/26 条（1.18.0，消费方反馈处理，
  [消费方反馈-2026-09-10-编辑器-第三批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md)
  第 21 条、
  [消费方反馈-2026-09-10-编辑器-第四批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第四批.md)）**：
  `FieldSchema` 新增 `Map`（动态键映射子结构登记，与 `Fields`/`Variants` 并列，`MapSchema`
  契约类型）；`toolchain/validator --list-tables --json` 每个顶层字段的 `field_meta` 新增
  `map_key`/`map_value`；18 个此前退化为 JSON 子编辑的字段中 12 个改用 `Map` 登记、1 个改用
  既有 `Item` 登记，元数据门禁白名单从 20 条收敛到 7 条（第 21 条）。`RecordExprMapping`/
  `RecordExprMapping.ToExprValueKind(FieldKind)` 由框架内部实现放宽为 `public static`（第 26
  条）；`DataRegistry.Query(string, string)` 便捷重载不再把 `FieldKind.Expr` 字段注册为
  `self.<field>` 引用，改为与 `IdList`/`Vec2`/`Object`/`Array` 同等对待、不登记（第 25 条）。
- `JsonNumber.FromInt64(long)`：精确整数工厂，`RawNumberText` 用不变文化十进制原文写出，覆盖
  `long` 全值域的精确往返（含 `double` 无法精确表示的 2^53 以上整数）。
- `ProcHost.Detach(Id instanceId, Id procDefId)`：按单个触发器精细摘除的新增重载，既有
  `Attach(Id, Id, ProcDef)`/`Detach(Id)`/`Update(double)`/`RescaleAll(double)` 签名不变。
- `sample_table_empty` 告警级门禁（`toolchain/tests/test_sample_table_empty.py`）：找出已注册
  schema 但 `data/_sample` 与 `data/_framework` 两根并集里都没有任何行的表，提醒补充示例数据；
  本测试恒 PASS，只在命中时 `warnings.warn` 提醒，不阻断整体 `pytest` 退出码。
- `toolchain/validator --schema-audit --json` 新增 `table_names` 字段（全架构已注册表名数组）。
- `data/_sample` 补齐 `item.affix`/`item.set`/`world.flag_schema` 三张此前无任何示例数据的表。

**已知未完成事项（如实登记，供后续版本跟进）**：ADR-0019 子结构登记第二批（18 个字段）本轮未
落地，其中至少 3 个字段（`arch.class.base_stats`/`arch.race.stat_mods`/
`creature.template.base_stats`）需要先给 `FieldSchema` 设计一种当前不支持的"动态键 Map"登记
形态，建议单独立项，见
[消费方反馈-2026-09-10-编辑器-第三批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md)
第 21 条。

### 修复

- 同一光环多个 `proc_trigger` 此前只有最后登记的一个真正生效（`AuraInstanceState.ProcDefRef`
  单值字段被后一条覆盖，`ProcHost._attachments` 同样以光环实例 id 为单值键、第二次挂载直接覆盖
  第一次），前面的触发器加载校验通过却在运行期被静默丢弃。改为 `AuraInstanceState.ProcDefRefs`
  有序集合 + `ProcHost` 按实例 id 分桶的多槽列表，单光环登记的多个 `proc_trigger` 各自独立挂载、
  独立结算条件/概率/内部冷却，光环整体移除（到期、`RemoveAura`、驱散、吸收耗尽、叠加溢出替换、
  目标销毁）时全部一并注销，不残留孤儿订阅。同一光环内重复引用同一个 `proc_def` 现在在加载期直接
  拒绝（新增校验规则 `AuraProcTriggerDuplicateRule`，检查名 `aura_proc_trigger_duplicate`，定位到
  `effects[index].params.proc_ref`，已在 `RulesSchemaCatalog` 默认登记）。对消费方：升级后可把
  此前拆成多个原生光环的多个被动合并回一个光环共用同一批 `proc_trigger`；若内容中已存在同一光环内
  重复引用同一个 `proc_def` 的写法，升级后加载会报错，需要改为引用不同的 `proc_def` 记录，或拆成
  独立光环（代码/校验规则/测试见提交 `5ef4128`）。
- `schema_version` 越界（超过 int 上界的整数，如 4294967297）此前强转溢出被误判为低版本放行，
  现阻断；`JsonNumber.TryGetInt64` 对 2^63 边界误判已修正；存档/事件序列化中的整数（货币余额、
  经验、tick 计数、游玩时长等）以精确十进制原文写出（`JsonNumber.FromInt64`），大于 2^53 的整数
  不再丢精度，旧存档仍可读（外部深度审核第十九方 V-01/V-02/V-03，分支 `wo/core17`，提交
  `f6f8c15`；整合阶段独立复现核实见 followup-2026-09-10d.md）。
- **ABI-1162-01（工具链）**：`toolchain/abi_surface` 属性签名此前只编码 `Name:PropertyType`，不含
  索引参数——public indexer（`this[int]`）的索引参数类型变化（如改成 `this[string]`）两次 dump
  输出逐字节相同，`compare` 判 `breaks=0`，旧编译消费方运行期 `System.MissingMethodException`。
  现属性 sig 编码索引参数类型列表；同批核查另确认运算符重载/转换运算符（此前被 `IsSpecialName`
  整体跳过、完全不进 dump）、事件 `remove` 访问器可见性（此前只记 `add` 半边）两处也是真实缺口，
  一并补齐；`params` 数组修饰、可选参数默认值存在性/取值两个维度核查后确认不编码（与既有
  `ref`/`out`/`in` 不编码设计同一治理逻辑，曾短暂编码又因产生历史基线假破坏而撤回，判断记录见
  `toolchain/abi_surface/TypeNameFormatter.cs` `FormatParameters`）。新版工具对
  `dist/ws-game-1.12.0.zip`/`dist/ws-game-1.13.0.zip` 两份历史基线重跑当前工作树六个 DLL 均
  `breaks=0`，未发现历史真实破坏（提交 `f602c8c`）。三方分支合并进 `main` 后，整合阶段重新对
  三方合并结果整体重跑 `abi_probe.ps1`，同样 `breaks=0 allowed=0 additions=253`，未发现任何
  索引器/运算符/事件访问器类可见性合并回归。

### 文档与门禁

- **DOC-162-01（`core/foundation/data_registry/README.md`）**：补全 `AllowOverride`/行级
  `override`/`final` 的条件语义——跨根主键重复默认仍阻断，但 `AllowOverride=true`（默认）时
  后层行可声明 `override:true` 整行覆盖前层不报错，前层行可声明 `final:true` 拒绝被覆盖仍阻断；
  此前只提"无条件阻断"，与 `data/README.md` 既有准确描述不一致（分支 `wo/core17`，提交
  `44f320d`）。
- **DOC-162-02（`architecture/落地计划/落地方案与分阶段计划.md`）**：一处仍写"三个 `.tgz`"，
  落后于 ADR-0018 决策 3 已转正的第四个包（`com.gamefoundation.adapter.headless`）；改为明确
  列出九件 GitHub Release 附件（zip、lock、samples zip、四个 `.tgz`、`get_framework.ps1`、
  `_hash.ps1`）（提交 `f602c8c`）。
- **DOC-162-03（同文件）**：`teleport_points` 一行称元素"仅做是数组这一层检查"，落后于已登记的
  `WorldMapSchema.PointItemSchema`（`{id?, position?}` 元素结构已展开登记）；改为区分"结构已
  登记"与"引用目标完整性不由登记表保证，由 `TeleportTargetResolver`/业务约束负责"（提交
  `f602c8c`）。
- **DOC-162-04（同文件）**：ATB（先攻策略预留位）一行分类"明确非目标"，与 ADR-0013 决策第 3 条
  原文"本版延期、预留扩展位"不符；改列"延期/预留"（提交 `f602c8c`）。
- **DOC-162-05（`architecture/04_数据与内容管线.md`）**：收窄"数值有限"（`field_finite`）校验项
  适用范围，明确只覆盖 `Number` 字段；`Int` 字段取值须先精确转换为 `long`
  （`JsonNumber.TryGetInt64`），非有限值本身不是合法的 `Int` 编码，落入既有 `field_type` 检查
  （分支 `wo/core17`，提交 `44f320d`）。
- 归档 `architecture/落地计划/audit-4faab73-20260910/`（第十九方深度审核报告、核实表、证据）；
  `followup-2026-09-10d.md` 回填 V-01/V-02/V-03、DOC-162-01/05 核实结果、三分支合并冲突处理、
  整合阶段全量门禁验收结果（提交 `f602c8c`/`3542b08`/`e4970d2`）。

### 迁移说明

- 同一光环内登记多个不同的 `proc_trigger`，从本版起会**全部合并生效**，不再只有最后登记的一个
  生效——若此前依赖"只有最后一个生效"这一缺陷行为拆分过内容（如刻意用多个 `proc_trigger` 条目
  表达互斥关系），升级后需要重新核对该内容的实际生效效果是否符合预期。
- 同一光环内重复引用同一个 `proc_def` 的内容，从本版起在加载期直接报错阻断（`AuraProcTrigger
  DuplicateRule`），此前不会阻断；若现有内容存在这种写法，需要改为引用不同的 `proc_def` 记录或
  拆成独立光环。
- `schema_version` 超过 `int` 上界的整数、或非 `[1, int.MaxValue]` 范围内的取值，从本版起在加载期
  直接阻断，此前会因强转溢出被误判为低版本而放行；请在升级前用本版 `toolchain/validate_data.py
  --strict` 排查现有内容。
- 存档/事件序列化中的整数（货币余额、经验、tick 计数、游玩时长等）从本版起以精确十进制原文写出
  （不再经 `double` 中转），大于 2^53 的大整数存档文件可能出现字节级 diff（数字的文本表示变了），
  但语义完全等价，旧版本写出的存档文件本版仍可正常读取。
- `IDataRegistryView.TryGetAll`/`TryQuery` 仅供内容工具（编辑器、校验命令行等）在阻断态下读取
  当前已合并的记录视图使用，**不建议游戏运行时代码依赖**——运行期宿主仍须使用 `GetAll`/`Query`，
  阻断态即禁止读取这一既有结论不变。
- `OverrideDiagnostic` 新增了 `OverridingRootIndex`/`OverriddenRootIndex`/
  `OverridingRelativePath`/`OverriddenRelativePath` 四个字段；已读取该类型既有字段
  （`OverridingLocation`/`OverriddenLocation` 等绝对路径字段）的既有调用方不受影响，新字段是
  纯增量。

### 兼容声明

本版本对 1.12.0～1.16.2 期间编译的旧消费方二进制保持兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的验证——1.12.0/1.13.0 两份历史基线 consumer 程序集不重新编译、直接换上本版本正式
DLL 实跑通过，独立的公开 API 表面差异比对（`toolchain/abi_surface`）输出均 `breaks=0`；本版本
表面差异工具自身已修复 ABI-1162-01，新增覆盖属性索引参数、运算符/转换运算符、事件 `remove`
访问器可见性三类此前会被漏报的破坏性信号，即本版本发布时使用的门禁工具本身已具备检出这几类
破坏的能力（不是"没检出=没发生"的弱依据）。

如实记录一段真实开发过程（消费方反馈第三批第 19 条实现期间）：最初实现直接给 `ExprNode` 基类及
其 6 个子类（`ExprLiteralNode`/`ExprReferenceNode`/`ExprNotNode`/`ExprCompareNode`/
`ExprAndNode`/`ExprOrNode`）已有的构造函数各自加了可选参数（如
`ExprLiteralNode(ExprValue value, int start = -1, int length = 0)`）来携带新增的源文本位置
区间，源码层面完全向后兼容（既有调用点不用改一行），但可选参数只是编译期语法糖，物理上仍是同一个
构造函数、只是参数数量变多——已编译的旧消费方对"少参数版本"的调用指令在新 DLL 里找不到对应物理
签名，运行期会抛 `System.MissingMethodException`，属于二进制破坏性变更。`toolchain/abi_probe.ps1`
对照 1.12.0 基线运行时真实拦下了这 7 处（7 个构造函数）破坏，本版本据此没有直接发布这个实现，
而是改为给每个类新增一个真正独立的重载构造（既有构造物理签名完全不动）——`abi_probe.ps1` 复跑
`breaks=0` 后才采用这个形态发布，见
[消费方反馈-2026-09-10-编辑器-第三批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第三批.md)
第 19 条"踩坑记录"。

## [1.16.2] - 2026-09-10

外部深度审核（codex 第十六轮，基线 `24a11fe28f9647cd532c41f56f7ab18c00fb8516`/v1.16.1 自身，报告见
`architecture/落地计划/audit-24a11fe-20260910/`）ABI 工具门禁、框架数据/表达式合同修复共两项，另加
五处文档勘误与 ADR-0023 光环叠加类别澄清；核实与逐条回复见
`architecture/落地计划/audit-24a11fe-20260910/followup-2026-09-10c.md`。提交 `af22b4b`（核心侧
F-01/F-02/F-03）、`a6702c0`（工具链与文档：ABI-116-01 + DOC-116-01～04/TOOL-116-01 + 归档）、
`9c7d533`（followup 回填）、`d7a300f`/`8dc7008`/`7886c1d`（ADR-0023）。

### 修复

- ABI-116-01（外部审计 audit-24a11fe-20260910，codex 第十六轮）：`toolchain/abi_surface`
  （公开 API 表面差异门禁）dump/compare 补齐可见性覆盖——此前方法/构造/字段/事件/属性访问器的
  可见性（`public`/`protected`/`protected-internal`）不记录具体档位，`public → protected` 这类
  收窄前后两次 dump 输出相同、`compare` 判 `breaks=0`，而真实旧编译消费方运行会抛
  `System.MethodAccessException`（最小 oracle 见
  `toolchain/tests/test_abi_surface_compare.py::test_public_to_protected_negative_oracle_end_to_end_via_real_dll`）。
  现在 TYPE/MEMBER 行统一记录可见性（类型另覆盖 `public`/`nested-public`/`nested-protected`/
  `nested-protected-internal`），`compare` 对可见性收窄判破坏、放宽豁免；同批补齐泛型约束变化、
  非枚举 `const` 字段内联值变化两处此前同样未被记录的信号（均判破坏）。用新版工具对
  `dist/ws-game-1.12.0.zip`、`dist/ws-game-1.13.0.zip` 两份历史基线重跑当前工作树六个 DLL，
  均 `breaks=0`，未发现历史真实破坏——本条属门禁能力补强，不代表已发布版本存在未声明的破坏性变更
  （提交 `a6702c0`）。
- F-01/F-02/F-03（外部审计 audit-24a11fe-20260910，codex 第十六轮）：`JsonReader`/`ExprLexer` 拒绝
  语法合法但求值非有限（Infinity/NaN）的数字与超出可表示范围的整数字面量，统一为带位置的解析错误；
  `FieldRange` 拒绝 NaN/Infinity 端点（无界用 null）；`DataRegistry` 新增 `field_finite` 检查项；
  表达式字段校验器内部异常统一转为阻断级 `expr_validation_error`，校验期任何未预期异常下注册中心
  保证阻断态、不留残留可读数据。对消费方：此前携带 Infinity/超范围整数仍能加载的内容从本版起在
  加载期阻断，请检查现有内容（提交 `af22b4b`）。

### 文档

- DOC-116-02（外部审计 audit-24a11fe-20260910）：`core/rules/skill/README.md`
  `ISkillHost.FindUnits` 一节由"当前恒返回空列表"更正为"已实现查询/过滤/排序，生产装配默认注入
  `Spatial`，仅未注入 `ISpatialQuery` 时降级为空列表+诊断"（提交 `a6702c0`）。
- DOC-116-01（外部审计 audit-24a11fe-20260910）：`architecture/README.md` 文件清单与根
  `README.md` 两处 ADR 计数由 17～19 条更正为实际 23 条，补 `adr/0020`～`adr/0023` 四行索引
  （提交 `a6702c0`）。
- DOC-116-03（外部审计 audit-24a11fe-20260910）：`core/foundation/data_registry/README.md`
  校验项检查名表补 `field_range`（ADR-0021）一行，`reference_integrity` 一行补充 ADR-0022
  的 `IdList` 元素引用覆盖说明；检查项表新增 `field_finite`/`expr_validation_error` 两行说明
  （提交 `a6702c0`）。
- DOC-116-04（外部审计 audit-24a11fe-20260910）：[ADR-0022](architecture/adr/0022-登记表补充导航与编辑元数据.md)
  第 5 节"导出"改写为分别说明 `toolchain/validator --list-tables --json`（元数据导出，顶层
  `tables_list` 含 `field_meta`/`field_ranges`）与 `toolchain/validate_data.py --json`（校验结果
  输出，顶层 `skeleton/validator/exit_code`，不含表清单）两条命令的不同输出结构，避免此前措辞被
  误读为两者输出等价（提交 `a6702c0`）。
- TOOL-116-01（外部审计 audit-24a11fe-20260910）：根 `README.md` 说明 `check.ps1`（含
  `-SkipUnity`）的 `-ArtifactsPath` 与"包清单一致性"步骤仍会往 `.gitignore` 覆盖的 `bin/`/`dist/`
  写入构建产物/打包中间物，"不修改已跟踪文件"不等于"运行过程零副作用"（提交 `a6702c0`）。
- 归档 `architecture/落地计划/audit-24a11fe-20260910/`（提交 `a6702c0`）；`followup-2026-09-10c.md`
  回填 F-01/F-02/F-03 核实结果与整合 agent 全量门禁验收（提交 `9c7d533`）。

- [ADR-0023](architecture/adr/0023-光环叠加类别为静态校验分组.md)：明确 `skill.aura_def.
  stack_category` 是跨定义的静态叠加校验分组（仅供加载期内容校验使用），不是运行时叠加槽位维度——
  运行时叠加与溢出严格按 `(target, aura_def, sourceKey)` 分槽，不同 `aura_def` 即使 `stack_category`
  相同也各自独立叠加、互不影响；`sourceKey` 在默认共享计时模式（`AllowMultiSourceTiming=false`）下
  恒为 `null`，仅开启独立来源计时时才按来源区分。06 第 3.3/3.8 节、04 第 5 节"叠加类别冲突"行随之
  改写措辞，`AuraHost.cs`/`SkillOptions.cs`/`SkillValidationRules.cs`/两份模块 README 同步对齐；
  跨定义共享叠加槽位列入落地方案"能力边界与未默认接入能力索引"表"未提供"分类。不改变任何运行时
  算法与公开签名，消费方复核见
  [消费方反馈-2026-09-10-光环叠加类别.md](architecture/落地计划/消费方反馈-2026-09-10-光环叠加类别.md)（提交 `d7a300f`/`8dc7008`）。

### 迁移说明

- 此前携带 Infinity/NaN 数值或超出 `long` 可表示范围的整数字面量的数据内容，从本版起在加载期
  （`JsonReader`/`ExprLexer` 解析阶段，或 `DataRegistry` 的 `field_finite`/`expr_validation_error`
  校验阶段）直接阻断，请在升级前用本版 `toolchain/validate_data.py --strict` 排查现有内容。
- `FieldRange.Range` 不再接受 NaN/Infinity 作为端点，若代码里存在类似写法请改用 `null` 表示该侧
  无界。

### 兼容声明

本版本对 1.12.0～1.16.1 旧编译消费方保持二进制兼容：依据是 `toolchain/abi_probe.ps1` 严格模式下的
验证——1.12.0 基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，且独立的公开 API
表面差异比对输出 `breaks=0`；本版本表面差异工具（`toolchain/abi_surface`）自身已修复 ABI-116-01，
新增覆盖可见性收窄、泛型约束变化、非枚举 `const` 字段值变化三类此前会被漏报的破坏性信号。

## [1.16.1] - 2026-09-10

消费方（内容编辑器项目）反馈第二批第 17 条根治，另含 ADR-0022 文档事实勘误；核实与逐条回复见
`architecture/落地计划/消费方反馈-2026-09-10-编辑器-第二批.md` 第 17 条。提交 `0364cce`（代码与
测试）、`2de351d`（文档与变更记录）、`0ee0bbe`（ADR-0022 勘误）。

### 修复

- `DataRegistry.RecordCount` 阻断态不再抛异常（直接读内部按表合并去重后的记录快照，不经
  `EnsureReadable`）；`IDataRegistryView` 新增默认成员 `TryGetRecordCount(out int count)`
  （阻断态返回 `false` 而不抛异常）；`ContentValidationAssembly.Run` 去掉靠事件订阅拿计数的旁路，
  统一改用 `registry.RecordCount`，与 `CreateRegistry`+`Reload` 路径口径一致（消费方反馈处理，
  [消费方反馈-2026-09-10-编辑器-第二批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第二批.md)
  第 17 条，提交 `0364cce`/`2de351d`）：【编辑器相关契约】见上方索引"第 17 条修复（1.16.1）"一行。

### 文档

- [ADR-0022](architecture/adr/0022-登记表补充导航与编辑元数据.md) 事实勘误：第 5 节把
  `validate_data.py --json` 是否透传给 `validator` 的表述由"消费方反馈第一批第 08 条待办，不在本次
  ADR 范围内"更正为陈述已随 1.15.0 落地的事实（提交 `0ee0bbe`）。

### 迁移说明

- 编辑器等消费方可用 `DataRegistry.RecordCount`/`IDataRegistryView.TryGetRecordCount` 替换自行
  遍历求和的记录计数逻辑；口径与 `DataLoadCompletedEvent.RecordCount` 一致。

### 兼容声明

本版本对 1.12.0～1.16.0 旧编译消费方保持二进制兼容：依据是 `toolchain/abi_probe.ps1` 严格模式下的
验证——1.12.0 基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，且独立的公开 API
表面差异比对输出 `breaks=0`。

## [1.16.0] - 2026-09-10

消费方（内容编辑器项目）反馈处理，[ADR-0022](architecture/adr/0022-登记表补充导航与编辑元数据.md)
（登记表补充导航与编辑元数据）与 XML 文档注释修复均在本版本收口；核实与逐条回复见
`architecture/落地计划/消费方反馈-2026-09-10-编辑器-第二批.md`。提交 `53b2e90`/`ad84a2f`/
`74cff87`/`5688c4b`（ADR-0022 落地）、`ab4d42c`（XML 文档注释修复）。

### 新增

- 登记表补充导航与编辑元数据（[ADR-0022](architecture/adr/0022-登记表补充导航与编辑元数据.md)，
  消费方反馈处理，
  [消费方反馈-2026-09-10-编辑器-第二批.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器-第二批.md)
  第 12～16 条，提交 `53b2e90`/`ad84a2f`）：【编辑器相关契约】
  - `TableSchema` 新增只读属性 `Layer`/`Module`/`Domain`/`TimeScope` 与 `WithOwnership`/
    `WithDomain`/`WithTimeScope` 链式登记方法；全部 63 张已登记表填齐 `Layer`/`Module`，
    三张单段名命名例外表（`camera_profile`/`ui_layout_definition`/`shell_menu_definition`）
    显式登记 `Domain`；5 张含时间字段的表（`skill.def`/`skill.aura_def`/`skill.proc_def`/
    `arch.power_type`/`spawn.table`）登记 `TimeScope`。
  - `FieldSchema` 新增只读属性 `Group`（未显式登记时按种类/命名计算默认值）、`Unit`（标记时间模型
    单位字段）、`FreeIds`/`FreeIdsReason`，新增 `WithGroup`/`WithUnit`/`WithFreeIds` 链式登记方法；
    全部 783 个已登记字段的 `Group` 生效（计算默认值 + 少量显式覆盖）；10 个已知时间字段登记
    `Unit=Time`；22 个 `IdList` 字段逐一登记 `ReferenceTable`/`ReferenceDomain` 或 `WithFreeIds`。
  - `DataRegistry` 的 `reference_integrity` 检查扩展为覆盖 `IdList` 字段的每个数组元素。
  - 新增公开静态工具类 `Core.Foundation.SimLoop.TimeModelRules`（`GetTimeScope`/`IsTimeField`），
    暴露"时间字段与时间模型一致"校验规则此前封装在实现内部的判定入口。
  - 元数据门禁（`SchemaAudit`）新增五项自洽检查：`table_ownership`/`field_group`/
    `time_scope_declared`/`time_unit_missing`/`idlist_reference_target`。
  - `toolchain/validator --list-tables --json` 每张表新增 `layer`/`module`/`domain`/
    `time_scope`；每个顶层字段新增 `field_meta`（`group`/`unit`/`reference_table`/
    `reference_domain`/`free_ids`）。

### 修复

- 修复 `core/foundation` XML 文档注释：WorldMapSchema 未闭合标签与 cref/paramref 漂移（`ab4d42c`）。

### 文档与门禁

- 新增文档：[ADR-0022](architecture/adr/0022-登记表补充导航与编辑元数据.md)、
  `architecture/落地计划/消费方反馈-2026-09-10-编辑器-第二批.md`（消费方反馈第 12～16 条逐条回复，
  提交 `74cff87`）。
- 顶部"编辑器相关契约"索引：ADR-0022 条目版本标注由"下一版本"改为"1.16.0"（详见文首索引小节，
  与本版本正式发布对齐）。
- 新增/更新回归测试：`core/foundation/data_registry/tests/SchemaMetadataTests.cs`、
  `presentation/assembly/tests/SchemaAuditTests.cs`。

### 迁移说明

面向消费方（游戏侧工程、内容编辑器等无头宿主），逐条可执行：

a. **`reference_integrity` 现在覆盖 `IdList` 元素**：消费方数据里 `item.template.affixes`、
   `item.slot_definition.accepts`、`item.set.pieces`、`world.map.allowed_difficulties` 等
   `IdList` 字段若含悬空 id，将在加载期被阻断（此前 `reference_integrity` 只校验单值引用字段，
   不遍历 `IdList` 数组元素）。
b. **消费方自定义 catalog 注册的表需声明归属元数据**：若表未声明 `Layer`/`Module`（含时间字段的
   表还需声明 `TimeScope`），元数据门禁（`SchemaAudit`）会报 `table_ownership`/
   `time_scope_declared` 等检查项；声明方式为在表登记处链式调用 `TableSchema.WithOwnership(...)`/
   `WithDomain(...)`/`WithTimeScope(...)`。
c. **三张单段名表不改名**：`camera_profile`/`ui_layout_definition`/`shell_menu_definition`
   （命名本身不含 `.` 分段）继续沿用原表名，工具改为按显式登记的 `Domain` 取域，不依赖表名分段。
d. **`Group` 为计算默认值 + 可覆盖**：字段未显式登记 `Group` 时按种类/命名规则计算得到默认值；
   编辑器等下游工具应读取 `toolchain/validator --list-tables --json` 导出的 `field_meta.group`
   （即计算/覆盖后的最终值），不要自行重新推导。

### 兼容声明

本版本对 1.12.0～1.15.0 旧编译消费方保持二进制兼容：依据是 `toolchain/abi_probe.ps1` 严格模式下的
验证——1.12.0 基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑通过，且独立的公开 API
表面差异比对输出 `breaks=0`；`build.ps1 -Release` 发布门禁本次固定传 `-AbiStrict`。

### 版本判据说明

新增能力（`TableSchema`/`FieldSchema` 归属与分组元数据、`IdList` 引用登记、`TimeModelRules` 公开
入口、元数据门禁五项新检查、`--list-tables --json` 导出扩展）与修复（XML 文档注释）均不改变任何
现有公开签名的必填参数个数（新增只读属性与链式登记方法均可选）、不删改任何数据表既有字段、不改变
存档格式；`reference_integrity` 覆盖 `IdList` 元素属于新增校验范围而非签名变化，按 SemVer 判定为
MINOR。

## [1.15.0] - 2026-09-10

第十五轮外部审核（codex，基线 `76d16a5`/v1.14.0 自身，`architecture/落地计划/audit-76d16a5-20260910/`）
修复条目、消费方（内容编辑器项目）反馈 E1～E11、[ADR-0021](architecture/adr/0021-字段登记表纳入数值范围约束.md)
（字段登记表数值范围约束）、[ADR-0020](architecture/adr/0020-表达式词法器纳入公开契约.md)（表达式词法器
公开，随 E5 落地）、打断免疫统一门（消费方反馈 C06-PRE-01）、私服脚本 `start_registry.ps1` 的 `-Stop`
误拒根治均在本版本收口；核实表见 `architecture/落地计划/audit-76d16a5-20260910/followup-2026-09-10b.md`。

### 新增

- 字段登记表新增数值范围约束（[ADR-0021](architecture/adr/0021-字段登记表纳入数值范围约束.md)）：
  `FieldSchema` 对 Number/Int 字段支持可选范围登记（`FieldRange`），加载期新增 `field_range` 检查项；
  元数据门禁新增 `field_range_kind` 自洽检查；`toolchain/validator --list-tables --json` 新增
  `field_ranges` 导出。【编辑器相关契约】
- 18 处字段登记数值范围（依据见各字段旁 ADR-0021 判断记录注释），修复消费方反馈的"内容错误在加载期
  未被拦截"问题：`item.template.stack_size`、`econ.currency.cap`、`econ.vendor` 售卖条目
  `price_amount`/`stock_limit`、`loot.table` 的 `count_range.min`/`pick_count`/`guaranteed_min`、
  `stat.rating_conversion.points_per_percent`、`combat.hit_table_config` 各判定分支
  `base`/`crit_multiplier_base`/`glancing_damage_pct`、`combat.resist_curve` 的
  `reduction`/`max_reduction`、`skill.def` 的 `apply_aura` 效果 `params.duration_override` 与
  `charges.max`、`skill.aura_def` 周期效果（`periodic_damage`/`periodic_heal`）`params.interval`、
  `skill.proc_def.proc_chance`、`target.chain_def.max_targets`。
- 消费方反馈处理新增能力（逐条见下方"修复"小节"消费方（内容编辑器项目）反馈处理"）：**E4** 新增
  验收数据集附件 `ws-game-<ver>-samples.zip` 与 `get_framework.ps1 -WithSamples`；**E5**
  （[ADR-0020](architecture/adr/0020-表达式词法器纳入公开契约.md)）`ExprLexer`/`ExprToken`/
  `ExprTokenKind` 由 `internal` 改为 `public`，新增 `ExprLexer.Tokenize(string)` 公开入口；**E8**
  `toolchain/validate_data.py` 新增 `--json`；**E10** `IDataRegistryView` 新增只读属性
  `RecordCount`（带默认实现，不构成 ABI 破坏）；**E11** 新增
  `toolchain/format_data.py --schema-order[--check]`。

### 修复

- 根治 `toolchain/registry/start_registry.ps1` 的 `-Stop` 误拒缺陷：`-Detach` 就绪后按端口核实
  发现真正监听端口的 PID 与 `Start-Process` 记录的不一致而重写 `-PidFile`/`.meta.json` 时，改为
  写入真正监听端口的那个进程自身的启动时间（`Get-Process` 查得），不再使用任何"本次调用当下
  时刻"的代理值——此前会导致该进程仍在正常服务却被身份核验条件 (c) 误判为"PID 被系统复用给了
  另一个更早启动的无关进程"而拒绝停止。新增 `toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py`
  静态 + 行为级回归。
- 修正外部打断（`interrupt` 效果原语）未查询目标免疫的问题：效果分发入口新增统一免疫门，
  `dispel`/`energize`/`teleport`/`move`/`trigger_spell`/`modify_cooldown`/`add_charge`/
  `learn_skill`/`projectile` 及扩展类效果原语同样补齐了此前只有伤害/治疗分支才有的免疫判定；
  打断免疫（动态光环或静态 `effect.interrupt` 标记）现在会正确保留目标读条/引导，免疫拦截时
  施法者一侧仍正常消耗资源与进入冷却；`apply_aura` 本身的免疫语义未拍板，暂不纳入（控制类光环
  免疫仍由光环宿主单独处理）。（消费方反馈 C06-PRE-01，`73cb55e`/`9cc4442`）
- 第十五轮外部审核（codex，基线 `76d16a5`/v1.14.0 自身，
  `architecture/落地计划/audit-76d16a5-20260910/`）修复条目：
  - **PJ114-01**：恢复 `SkillHost` 17 参数构造签名的兼容入口——1.14.0 新增 `IFactionMatrix?
    factions` 使公开构造签名从 17 参数增至 18 参数时未保留旧参数个数的转发入口，导致 1.13.0 正式
    consumer 只换 1.14 正式 DLL、不重新编译即抛 `MissingMethodException`，与本文件 1.14.0 迁移
    说明"无需重编译即可运行"矛盾；本次恢复后旧编译 consumer 换 DLL 重新可用，提交 `c35543d`。
    同批额外发现并修复了同一根因的 `Presentation.ViewBinder` 7 参数构造 façade 缺失（新增
    `EquipmentVisualSource` 可选参数导致的同类破坏），提交同为 `c35543d`。
  - **PJ114-02**：ABI 门禁在基线发行包缺失时不再记为 `PASS`——`toolchain/abi_probe.ps1` 缺基线时
    改为在门禁汇总中显式输出可见的 `SKIP`；面向正式发布的门禁路径新增严格模式（缺基线即判定门禁
    失败）；新增独立于"旧编译消费方换新库实跑"之外的"公开 API 表面差异"门禁（以最后已知兼容基线
    为准，比对公开类型/成员删除、签名变化、接口新增无默认实现成员）；探针落盘保存基线包/库文件
    校验值、消费方程序集校验值与两次运行结果（提交 `f0e5cd8` 新增 `toolchain/abi_surface`
    工具本身；提交 `b3c898e` 接线 `check.ps1` 可见 SKIP/`-AbiStrict`/持续集成环境下载基线包）。
  - **PJ114-03**：`toolchain/abi_probe.ps1` 的显式已有输出目录不再被无边界递归删除——改为默认生成
    唯一目录，显式传入的已有目录直接拒绝或经专用临时根与唯一前缀验证，提交 `f0e5cd8`。
  - **CORE114-01**：`QuestHost` reload 后活跃任务目标数组不再可能越界——旧定义目标数减少/增加/
    重排时迁移已有进度计数，结构不兼容时原子拒绝该次 reload，提交 `89b1b69`。
  - **CORE114-02**：`EconomyHost` 既有 vendor/item 从 `none` reload 为 `timer` 补货策略时，正确
    初始化/重采样计时器（此前遗留 `null` 计时器导致库存永久停留在 0），并守恒切换前已消耗的库存
    进度，提交 `89b1b69`。
  - **CORE114-03**：`StatHost` 派生值缓存随 reload 一致失效并按新定义重算，不再出现同一新定义下
    因访问顺序不同而返回不同历史遗留值，提交 `89b1b69`。
  - **CORE114-04**：`ExprValueJson.IsValid` 捕获非法 `$id`（不再冒泡 `ArgumentException`），正式
    `ContentValidationAssembly` 对 Quest/Dialog world flag value 的非法形状输出可定位的 blocking
    报告项，提交 `89b1b69`。
  - **P3**：`core/gameplay/common/contracts/RewardSchemaFields.cs:9-13` 过时注释（仍称
    `FieldSchema` 不能表达嵌套）更正为描述当前 Object/复合 schema 登记能力的实际范围，提交
    `89b1b69`。
  - 套装缓存（Equipment set cache）候选排查：补充 resident equip→reload→unequip 独立负例测试，
    确认存在与 CORE114-03 同构的派生缓存分歧（`EquipmentHost.RecomputeSetBonuses` 未释放"孤儿"
    套装门槛光环句柄），已按排查结论修复，提交 `89b1b69`。
  - 归档、核实表与逐项判断/修复位置/验收见
    `architecture/落地计划/audit-76d16a5-20260910/followup-2026-09-10b.md`。
- 消费方（内容编辑器项目）反馈处理，逐条现象/根因/处理方式/验证结果见
  [消费方反馈-2026-09-10-编辑器.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器.md)：
  - **E1**：`toolchain/validator` 现场编译受消费方 `Directory.Build.props` 影响导致校验失败——
    Release 现附带预编译的 `validator/bin/`（附隔离用的空 `Directory.Build.props`），
    `validate_data.py` 改为优先执行预编译产物，找不到才退回现场编译，提交 `dc6e2e5`。
  - **E2**：`get_framework.ps1` 依赖同目录 `_hash.ps1` 造成"先有鸡还是先有蛋"式引导死锁——改为
    内联 `Get-Sha256FileHash` 函数体，脚本自包含、可单独下载使用，提交 `dc6e2e5`。
  - **E3**：`ws-game.lock` 的 `source.local_path` 写入本机绝对路径导致换机器出现无意义
    diff——改写为相对的 `zip_file_name`，判定"是否需要改写锁文件"改为只比较引用内容字段，
    提交 `dc6e2e5`。
  - **E4**：验收数据集 `data/_sample`/`assets/_sample` 不随 zip 分发——新增独立附件
    `ws-game-<ver>-samples.zip` 与 `get_framework.ps1 -WithSamples` 下载合并落地，提交
    `dc6e2e5`。
  - **E5**：Expr 词法器不在公开契约内——新增 [ADR-0020](architecture/adr/0020-表达式词法器纳入公开契约.md)，
    `ExprLexer`/`ExprToken`/`ExprTokenKind` 改为公开，新增 `ExprLexer.Tokenize(string)` 入口，
    提交 `396afbc`。
  - **E6**：示例数据 `quest.sample_hunt` 触发"疑似引用拼写错误"误报——根治
    `ExprValidator` 规则本身（实参位置期望类型恰为 `Id` 时不再检查拼写），不是改数据迁就，
    提交 `3deba37`。
  - **E7**：`toolchain/gen_placeholder_assets.py` 在 Windows 上写出 CRLF——6 处 `write_text`
    补齐 `newline="\n"`，新增静态扫描测试防回归，`check.ps1` 新增"工作树文本文件无 CR"步骤，
    提交 `3deba37`。
  - **E8**：`validate_data.py` 未透传 `--json`——新增 `--json`，骨架检查与
    `toolchain/validator --json` 输出合并为一份结构化 JSON，提交 `3deba37`。
  - **E9**：`--data-root` 相对路径按脚本安装位置解析而非调用方当前工作目录——改为按
    `Path.cwd()` 解析，提交 `3deba37`。
  - **E10**：`ContentValidationAssembly.CreateRegistry` 路径拿不到记录计数——`IDataRegistryView`
    新增带默认实现的 `RecordCount` 只读属性（不破坏 ABI），提交 `3deba37`。
  - **E11**：示例数据字段顺序与 schema 声明顺序不一致——新增
    `toolchain/format_data.py --schema-order[--check]`，对 `data/_sample` 6 个文件重排并纳入
    `check.ps1` 门禁，提交 `3deba37`。
  - 逐条复现、根因、验证结果与迁移提示见
    `architecture/落地计划/消费方反馈-2026-09-10-编辑器.md`（该文档本身提交 `f3f2d20`）。

### 文档与门禁

- 新增文档：`architecture/落地计划/消费方反馈-2026-09-10-编辑器.md`（E1～E11 逐条归档，提交
  `f3f2d20`）、`architecture/落地计划/消费方反馈-2026-09-10-技能效果参数范围.md`（ADR-0021 消费方
  反馈归档）、[ADR-0020](architecture/adr/0020-表达式词法器纳入公开契约.md)、
  [ADR-0021](architecture/adr/0021-字段登记表纳入数值范围约束.md)、
  `architecture/落地计划/audit-76d16a5-20260910/followup-2026-09-10b.md`（第十五轮审核核实表）。
- 顶部"编辑器相关契约"索引：E1～E4、E5、E8、E10、ADR-0021 各条版本标注由"Unreleased"改为
  "1.15.0"（详见文首索引小节，与本版本正式发布对齐）。
- 门禁新增：`check.ps1`"工作树文本文件无 CR"步骤（消费方反馈 E7）、`data/_sample
  --schema-order --check` 步骤（消费方反馈 E11）；`build.ps1 -Release` 第 5 步固定传
  `-AbiStrict`（PJ114-02 根治，见下方"兼容声明"）。新增回归测试
  `toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py`、
  `core/foundation/data_registry/tests/FieldRangeValidationTests.cs`、
  `core/rules/skill/tests/SkillEffectParamRangeTests.cs`。

### 迁移说明

面向消费方（游戏侧工程、内容编辑器等无头宿主），逐条可执行：

a. **Release 附件集合由 6 件变为 9 件**：`ws-game-<ver>.zip`、`ws-game-<ver>.lock`、
   `ws-game-<ver>-samples.zip`、四个私服包 `.tgz`
   （`com.gamefoundation.adapter.unity`/`com.gamefoundation.framework-data`/
   `com.gamefoundation.toolchain`/`com.gamefoundation.adapter.headless`）、自包含的
   `get_framework.ps1`、`_hash.ps1`（兼容旧还原脚本）。消费方可以直接从 Release 页面下载
   `get_framework.ps1` 单个文件使用，不必先解压主 zip 再取脚本。
b. **锁文件 `ws-game.lock` 的 `source` 字段不再含绝对路径**（`local_path` 改为相对的
   `zip_file_name`），新增 `samples.sha256`（`-WithSamples` 校验用）与 `validator_dlls`/
   `headless_dlls` 预编译产物哈希字段；判定"是否需要改写本机锁文件"改为只比较这些引用内容字段，
   不再比较路径本身；不含这些新字段的旧锁文件仍可读（跳过对应校验并提示，向后兼容）。
c. **新增 `get_framework.ps1 -WithSamples`**：下载并按锁文件 `samples.sha256` 校验、解压合并
   `ws-game-<ver>-samples.zip`（含 `data/_sample`/`assets/_sample` 验收数据集）到本地落地目录。
d. **`validate_data.py` 优先运行预编译 validator**（找不到才退回现场编译，规避消费方自带
   `Directory.Build.props` 干扰）；新增 `--json`（骨架检查与 `toolchain/validator --json` 输出
   合并为一份结构化 JSON 打印到标准输出，人类可读诊断改走标准错误）；`--data-root` 相对路径改为
   按**调用方当前工作目录**解析（此前按脚本安装位置解析）——旧用法若依赖"相对脚本目录"这一行为，
   需要相应调整调用处的相对路径或改传绝对路径。
e. **`FieldSchema` 新增范围登记与 `field_range` 检查**：已登记范围的字段若消费方数据取值越界，
   会在加载期被阻断。本版本登记范围的字段共 18 处，分布在 11 张表：`item.template`
   （`stack_size`）、`econ.currency`（`cap`）、`econ.vendor`（售卖条目 `price_amount`/
   `stock_limit`）、`loot.table`（`count_range.min`/`pick_count`/`guaranteed_min`）、
   `stat.rating_conversion`（`points_per_percent`）、`combat.hit_table_config`（判定分支
   `base`/`crit_multiplier_base`/`glancing_damage_pct`）、`combat.resist_curve`
   （`reduction`/`max_reduction`）、`skill.def`（`apply_aura` 效果 `params.duration_override`/
   `charges.max`）、`skill.aura_def`（周期效果 `params.interval`）、`skill.proc_def`
   （`proc_chance`）、`target.chain_def`（`max_targets`）；每处范围的依据见对应 schema 文件里
   紧邻 `.WithRange(...)` 调用的 ADR-0021 判断记录注释。`toolchain/validator --list-tables
   --json` 每张表新增 `field_ranges` 导出，供编辑器就地校验数值输入控件。
f. **`ExprLexer.Tokenize(string)` 公开**：`core/foundation/expr` 的 `ExprLexer`/`ExprToken`/
   `ExprTokenKind` 由 `internal` 改为 `public`，编辑器等工具做语法高亮应改用该入口，不再自行
   正则切词。
g. **`IDataRegistryView.RecordCount` 新增默认实现**：`CreateRegistry` 路径（调用方自行持有
   registry、自行调用 `Reload`）现在也能拿到精确记录计数，不必自行遍历求和；不要求已有实现类
   改动，不构成 ABI 破坏。
h. **打断免疫门扩展到全部效果原语**（`apply_aura` 除外）：`dispel`/`energize`/`teleport`/
   `move`/`trigger_spell`/`modify_cooldown`/`add_charge`/`learn_skill`/`projectile` 及扩展类
   效果原语现在同样受打断免疫判定约束——依赖"免疫不拦打断/驱散/位移"这一旧行为（即这些效果
   此前对免疫目标同样生效）的内容需要复核。
i. **定义热重载语义补强**：`QuestHost` reload 后活跃任务目标数组迁移已有进度计数（结构不兼容时
   原子拒绝该次 reload）；`EconomyHost` 补货策略从 `none` 改为 `timer` 时正确初始化计时器；
   `StatHost`/`EquipmentHost` 派生值与套装门槛缓存随 reload 一致失效重算。均不需要调用方改动
   既有调用点。
j. **`SkillHost`/`Presentation.ViewBinder` 旧构造签名重新恢复为 `[Obsolete]` 兼容重载**：
   1.14.0 曾意外破坏这两处的旧构造签名（见下方"兼容声明"），本版本恢复；旧编译 consumer 换
   1.15.0 正式 DLL 不需要重新编译。

### 兼容声明

本版本对 1.12.0/1.13.0/1.14.0 旧编译消费方保持二进制兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的两项独立验证——① 1.12.0 基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑
通过；② 独立的公开 API 表面差异比对（`toolchain/abi_surface`，以最后已知兼容基线为准）输出
`breaks=0`；`build.ps1 -Release` 发布门禁固定传 `-AbiStrict`（PJ114-02 根治），缺基线或探针未
真正跑起来会使门禁判定失败而不是静默放行。

**1.14.0 的兼容声明曾不成立**：该版本"迁移说明"所述"旧编译的 1.12 consumer 换正式 1.14 DLL、不
重新编译即可继续运行"这一结论，对 `SkillHost` 构造签名不成立——`IFactionMatrix? factions` 参数的
加入使旧有的 17 参数构造签名一并消失，同批还漏了 `Presentation.ViewBinder` 7 参数构造 façade
（`EquipmentVisualSource` 可选参数加入导致同类破坏）；第十五轮外部审核以独立 ABI 探针实跑复现
（1.13.0 正式 consumer 只换 1.14 正式 DLL、不重新编译，退出码由 0 变为 11，报告
`MissingMethodException`）。按 [12_扩展与变更流程.md](architecture/12_扩展与变更流程.md) 第 4 节
"发布不可变"约定，已发布的 `[1.14.0]` 小节正文不回头修改；本版本（PJ114-01）恢复两处兼容入口并以
独立探针实跑证明，上方"迁移说明"j 项记录修复结果。

### 版本判据说明

新增能力（字段数值范围登记与 `field_range`/`field_ranges` 导出、打断免疫门扩展、定义热重载语义
补强、若干消费方交付面改进）与修复（`SkillHost`/`ViewBinder` 兼容 façade 恢复、ABI 门禁
SKIP/严格模式、热重载缓存/库存/目标数组等第十五轮审核问题、`-Stop` 误拒、消费方反馈
E1～E11）——均不改变任何现有公开签名的必填参数个数（新增字段登记与只读属性均可选/带默认实现）、
不删改任何数据表既有字段、不改变存档格式，按 SemVer 判定为 MINOR。

## [1.14.0] - 2026-09-10

第十四轮（修订版）审核修复（codex，基线 `c9ff301`/v1.13.0，
`architecture/落地计划/audit-c9ff301-20260909/`）：6 项 P2 + 2 项静态差距（`ISkillHost.FindUnits`、
VFX 锚点持续跟随）+ 3 项 P3 + 文档/能力索引均已核实并修复，核实结论见该目录
`followup-2026-09-10.md`。核心侧修复提交 `c65258e`，表现/模板侧修复提交 `775de54`，文档与门禁提交
`4a60ca2`，审计归档提交 `acbdf99`。

### 新增/恢复

- **ABI 兼容 façade**（`[Obsolete]`，内部委托给现有实现，行为与 1.12 完全一致）：
  `Core.Foundation.DataRegistry.FieldSchema` 七参数构造函数；六个已删除公开规则类型
  `Core.Carriers.Gobj.GobjOnUseKindRule`/`GobjLockRequirementFieldGroupRule`、`Core.Rules.Skill.
  EffectKindRegisteredRule`/`CostEntryShapeRule`/`ChargesShapeRule`、`Core.Gameplay.AreaTrigger.
  AreaTriggerShapeKindRule`；两处构造签名收窄的兼容重载 `Core.Gameplay.Economy.
  EconomyContentValidationRule(IExprSchema?)`/`Core.Gameplay.Loot.LootContentValidationRule
  (IExprSchema?)`。
- `Presentation.Render.IEquipmentVisualResettable`（新接口，`SpriteViewBase`/
  `Adapter.Unity.Presentation.UnityModelView` 实现）：装备表现 SaveLoaded 读档对账先清旧外观再
  应用快照。
- `Presentation.VfxSfx.Contracts.IParticleRepositioner`（新可选能力接口，`Adapter.Unity.
  EngineAdapter.UnityRenderer2D` 实现）：VFX anchor/socket 降级场景的锚点持续跟随；`VfxPlayer.
  Update` 默认调用，未实现该接口的引擎适配层行为与改动前一致。
- `ISkillHost.FindUnits` 真实现（此前恒返回空列表）：委托 `ISpatialQuery.QueryShape` + 新增
  `Shape.WithOrigin`，按 `UnitFilter` 全维度过滤，生产装配 `RulesAssembly` 默认注入 `Spatial`/
  `Factions`。
- 定义缓存热重载失效（十处，"构造期一次性读 registry 建索引、之后只读"模式统一订阅
  `DataLoadCompletedEvent`）：`SkillDefCache`（新增 `InvalidateAll`）、`InventoryHost`/
  `EquipmentHost`、`QuestHost`/`DialogHost`（均新增 `Reload` 方法 + `GameplayAssembly` 转发）、
  `ArchetypeRegistry`、`StatHost`、`LootHost`、`EconomyHost`（新增 `Reload` 方法 +
  `RulesAssembly` 转发）；均不触碰运行期状态，`EconomyHost` 的限量库存额外保留已消耗进度不重置。
- schema 新增字段：`GobjLockWorldFlagExpectedRule`（`world_flag.expected` 缺失/错型 report 阶段
  拒绝）、`ExprValueJson.IsValid`（Quest `rewards.world_flags[].value`/Dialog
  `set_flag.params.value` 联合形状校验）、`SkillSchemas.PeriodicParamsCase` 补
  `scaling_stat: Reference(stat.definition)`、`WorldMapSpawnPointsValidationRule`（出生点/传送点
  元素语义边界）、`games/_template/Runtime/DataHotReload.cs` 新增订阅 `Deleted` 文件事件（删除
  override 自动回落）。
- `toolchain/abi_probe.ps1` + `toolchain/abi_probe/`（消费方探针工程）+
  `toolchain/abi_probe_baseline.txt`：发布前 ABI 探针，纳入 `check.ps1` 全量步骤（`-Quick` 跳过）。

### 修复

第十四轮（修订版）审核 6 项 P2（ABI/API 兼容性、Gobj `world_flag.expected`、SkillHost 热重载缓存、
Quest world flag reward 联合校验、装备表现 SaveLoaded、开发期热重载删除覆盖回落）+ 2 项静态差距
（`ISkillHost.FindUnits`、VFX 锚点持续跟随）+ 3 项 P3（ADR-0019 门禁载体文案、周期效果
`scaling_stat` metadata、WorldMap 点元素语义边界）+ 额外发现（第六个退役类型、两处构造签名收窄、
四处同类缓存候选）——逐条判断、复现、修复位置、验收结果见
`architecture/落地计划/audit-c9ff301-20260909/followup-2026-09-10.md` 核实表，不在本条目重复展开。

### 文档

- 能力索引（`architecture/落地计划/落地方案与分阶段计划.md`"能力边界与未默认接入能力索引"一节）
  分类口径由四类扩到六类，新增**游戏责任**；天赋点数管理、位移轨迹碰撞、新局完整重置、escort
  自动路线等条目按责任重分类；导航跨帧预算、空间查询完整索引化改归**明确非目标**；
  `ISkillHost.FindUnits`、VFX 锚点持续跟随两行随本版本实现完成，从索引表本体移出、归档进"已修复
  历史项"小节；根 `README.md` 能力边界段同步。
- `architecture/05_对象模型与世界.md`（WorldMap 点元素语义勘误）、`architecture/09_表现层.md`
  （装备外观对账、VFX 跟随实现现状）、`architecture/13_新游戏接入指南.md`（热重载删除语义）、
  `architecture/adr/0019-复合字段子结构登记为机器可读schema.md`（追加修订记录）、
  `architecture/11_工程规范与测试.md`（ABI 探针现状更正为已实现）均按 12 §5"细节勘误直接修订、
  版本号不变"处理。

### 迁移说明

- 旧编译的 1.12 consumer 换正式 1.14 DLL、**不重新编译**即可继续运行，不再出现
  `MissingMethodException`（已随 `toolchain/abi_probe.ps1` 纳入发布前检查，基线 1.12.0 本机实跑
  通过）。
- 使用上方"新增/恢复"小节列出的退役类型/兼容重载的**源码**在重新编译时会收到 `[Obsolete]` 编译期
  警告，提示改用对应的声明式 schema 登记（`VariantSchema`/`Fields`）或无参构造函数；这些 façade
  至少保留一个 MINOR 发布周期，下一个 MINOR 周期后可能移除（届时另行走
  [12_扩展与变更流程.md](architecture/12_扩展与变更流程.md) 走 MAJOR 流程）。显式使用兼容重载可能
  与内建结构校验/新增业务规则对同一处坏数据重复报告（check 名不同，不掩盖问题，只是冗余）。
- 定义缓存热重载失效对调用方透明，无需改动任何既有调用点；`DataLoadCompletedEvent` 触发后
  resident host 下一次访问即可见新定义，运行期状态（背包物品、任务进度、对话会话、已应用光环/
  属性、已注册单位的当前资源值、`EconomyHost` 未变化商品的限量库存剩余）不受影响。
- VFX 跟随对自定义 `IRenderer2D` 引擎适配层是**可选**接口（`IParticleRepositioner`）：未实现该
  接口的引擎适配层行为与改动前完全一致，不需要任何改动即可继续编译运行；需要跟随效果的引擎适配层
  实现方可自行实现该接口接入。

### 版本判据说明

新增能力（ABI 探针、两个新接口、`FindUnits` 真实现、十处热重载缓存刷新、四个 schema 新字段）与
恢复向后兼容（ABI façade 恢复对已编译 1.12 consumer 的二进制兼容）——均不改变任何现有公开签名、
不删改任何数据表字段、不改变存档格式，按 SemVer 判定为 MINOR。

## [1.13.0] - 2026-09-09

ADR-0018（编辑器随游戏走：基础套件按版本消费框架，无头适配层与校验装配入口列为框架交付物）、
ADR-0019（复合字段子结构登记为机器可读 schema，含按 kind 分派的变体）拍板落库（`88f31f8`）；
ADR-0019 F1 复合字段子结构登记与递归校验分三段收口——F1a 技能域首批登记与 `FieldSchema`/
`DataRegistry` 契约扩展（`e2fc928`）、F1b L4 玩法层九个模块登记（`cba913b`）、F1c L3/L1/L2 其余/
L0/L5 登记，首批登记范围收口（`e50339a`）；ADR-0018 决策 3 F2 无头适配层交付与核心库校验装配入口
`ContentValidationAssembly`（`7ddbeac`）；F3 元数据门禁 `SchemaAudit`、开发期数据热重载标准实现、
`.github/workflows/release.yml` 四包修正（`3456e94`）。均属向后兼容的新增能力与契约扩展，无数据表
字段删改、无存档格式变更（判据见下"版本判据说明"）。

### 文档

- ADR-0018（编辑器随游戏走：基础套件按版本消费框架，无头适配层与校验装配入口列为框架交付物）、
  ADR-0019（复合字段子结构登记为机器可读 schema，含按 kind 分派的变体）拍板落库，收入
  `architecture/adr/`，索引见 `architecture/adr/README.md`。
- `architecture/04_数据与内容管线.md` 升级 v5 → v6：新增 3.2 节"复合字段子结构登记"（`Object`
  字段可登记 `fields`、`Array` 字段可登记 `item`、按判别字段分派的复合字段登记为 `variants`，
  以 `skill.def.effects` 的 `school_damage`/`apply_aura`/`projectile` 三个变体为记法示例），
  第 5 节校验项清单新增"复合字段子结构合法"与"变体表与原语注册集合一致"两项。
- `architecture/13_新游戏接入指南.md`、`architecture/12_扩展与变更流程.md` 补充勘误（版本号不
  变）：13 第 1 节前置动作后补一步可选项——若该游戏需要内容编辑器，复制编辑器项目提供的编辑器
  模板到游戏仓库；12 第 2 节新增原语审批流程"再改注册表"一步补充同时登记该原语的变体子结构。
- `architecture/落地计划/落地方案与分阶段计划.md` 3.5 节"四条消费通道"表新增"无头适配层与校
  验装配入口"一行，3.5.1 节补一句"三个包"归属未决说明。
- `editor/docs/编辑器产品文档.md` 升级 v2 → v2.1：全文 ADR-0018/0019 引用由"草案，待拍板"改为
  正式链接，附录 F 等处措辞同步改为已拍板。
- `architecture/04_数据与内容管线.md` 勘误（F1a 落地对齐，版本号不变）：3.2 节示例改正为真实
  `{kind, params: {...}}` 形状，补充 `CommonFields`、惰性递归中立记法、递归深度上限与"分层边界"
  说明；第 5 节两行说明补齐三个新增检查名（`variant_discriminator`/`substructure_depth`/
  `unknown_subfield`）。
- ADR-0018 决策 3 落地（F2）文档同步：`architecture/04_数据与内容管线.md` 勘误（版本号不变）——
  5.1 节"校验覆盖矩阵" `SpawnSummonOnlyCreatureRule`/`DisplayMapCoverageRule` 两行补充"经校验
  装配入口显式声明"；`architecture/11_工程规范与测试.md` 勘误（版本号不变）——目录树
  `adapters/stub/` 一行、第 6 节"桩适配层专供测试与 CI"一句均改为"同时是框架交付物（ADR-0018）"；
  `architecture/落地计划/落地方案与分阶段计划.md` 3.5.1 节"三个包"改"四个包"，补第四行，去掉
  F1 时补的"归属未决"说明改为已决定新增包。`adapters/stub/README.md` 首段改写为框架交付物说明；
  新增 `adapters/headless/README.md`（zip 通道说明文档源文件）。根 `README.md`、
  `toolchain/README.md`（validator 一节）、`data/README.md`（"与校验器的关系"一节）、
  `toolchain/registry/README.md`、`presentation/assembly/README.md`（新增"校验装配入口"一节）
  同步更新。`editor/docs/编辑器产品文档.md` 第 4.3 节状态改"已落地（1.13.0 起）"、第 4.4 节状态
  改"已落地"（仅改状态句，html 版本本轮未重生成，待下次文档转换一并同步）。

### 新增

- ADR-0019 F1a：`FieldSchema` 新增可选子结构登记（`Fields`/`Item`/`Variants`，仅对
  `Object`/`Array` 两种字段种类生效），新增 `VariantSchema` 契约类型（按判别字段分派的变体：
  `Discriminator`+`Cases`+可选 `CommonFields`）；`DataRegistry` 按登记递归校验必填/类型/枚举/
  引用/表达式可解析，新增三个检查名 `variant_discriminator`/`substructure_depth`/
  `unknown_subfield`（后者默认关闭，`DataRegistryOptions.UnknownSubfieldSeverity` 可开启为
  Warning）；`FieldSchema` 支持 `itemFactory`/`variantsFactory` 惰性求值，支撑
  `projectile.params.on_hit_effects` 一类自引用结构。
- 技能域（`skill.*`）首批登记：`skill.def.effects`（19 种效果原语）、`skill.aura_def.effects`
  （10 种光环效果类型）均登记为按 `kind` 分派的 `Variants`，逐个原语的 `params` 结构对照运行时
  解析代码核对（`EffectDispatcher`/`ProjectileHost`/`AuraHost`/三个 `IEffectExtension` 实现）；
  `skill.def.cost`/`charges`、`skill.spell_mod_def.affects`、`skill.book.entries` 均登记子结构。
  详见 `core/rules/skill/schema/README.md`"效果原语参数表"/"光环效果参数表"。
- `core/foundation/data_registry/tests/SubstructureValidationTests.cs`（32 条新测试，覆盖递归
  必填/类型、Variants 判别、子层 Reference/Expr、自引用递归与深度上限、`unknown_subfield`、
  路径格式、构造期非法组合）、`core/rules/skill/tests/SkillSchemaCoverageTests.cs`（锁定变体键
  集合与 `EffectKindNames`/`AuraEffectKindNames` 全集一致）。
- ADR-0019 F1b：L4 玩法层（`core/gameplay/`）九个模块的复合字段子结构登记（延续 F1a 的技能域，
  收口"首批登记"范围）：
  - `loot.table.groups` 登记为 `Item`（`roll_mode: Enum(chance_each|weighted_pick_one)`、
    `pick_count: Int?`、`entries: [{ref: Id, weight_or_chance: Number, condition: Expr?,
    count_range: {min: Int, max: Int}}]`，`ref` 跨类型指向 `item.template`/`loot.table` 退回
    `Id`）；`econ.vendor.sell_items` 登记为 `Item`（`item_id: Id`、
    `price_currency_id: Reference(econ.currency)`、`price_amount: Int`、`stock_limit: Int?`、
    `restock_policy: Enum(on_map_enter|timer)?`、`restock_timer: Number?`）；
    `world.flag_schema.allowed_values` 因元素类型随同记录 `kind` 字段动态变化（判别字段与被
    判别数组不同层，`Item`/`Variants` 均无法表达）且无运行时解析代码可作依据，本轮不登记。
  - `quest.def.objectives` 按 `type` 登记为 `Variants`（kill/collect/interact/explore/escort/
    event/cast/talk 八种目标类型，`collect`/`interact`/`explore`/`cast` 四处 `target_ref` 升级
    为 `Reference`）；新增公开可复用结构 `Core.Gameplay.Quest.QuestSchemas.RewardsFields`
    （`items`/`xp`/`currency`/`skills`/`world_flags`/`talent_points`），`quest.def.rewards`
    改为直接带 `Fields` 登记。
  - `dialog.gossip_menu.options`（`{text_key: TextKey, visible_if: Expr?, actions: Array?}`）、
    `options[].actions` 按 `kind` 登记为 `Variants`（十种动作，`quest_accept`/`quest_turn_in`/
    `start_encounter`/`cast_skill`/`start_story` 五处升级为 `Reference`）、
    `dialog.story_tree.nodes`/`nodes[].branches` 均登记子结构；`achv.def.criteria` 按 `type`
    登记为 `Variants`（六种条件类型），`achv.def.rewards` 直接复用
    `QuestSchemas.RewardsFields`（同一静态实例）。
  - `encounter.def.units`/`waves`/`phases`/`arena_rules`/`initiative_override` 登记子结构——
    `arena_rules.bounds_shape` 按 `kind` 登记为 `Variants`（circle/cone/line/rect 四种形状）；
    `units[].template_ref` 升级为 `Reference(creature.template)`，
    `initiative_override.params.initiative_stat` 升级为 `Reference(stat.definition)`。
  - `area.trigger_def.shape` 按 `kind` 登记为 `Variants`（circle/cone/line/rect）；`params`
    因判别字段 `trigger_type` 与 `params` 不同层（`Variants` 机制表达不了），改登记为 `Fields`
    （`target_map`/`spawn_point`/`encounter_ref`/`hook_id`，均退回 `Id`）。`spawn.table` 勘察
    确认全部字段为标量，无可登记的复合字段（`respawn_policy`/`respawn_timer` 同属判别字段与被
    判别字段不同层，`SpawnRespawnPolicyFieldGroupRule` 保留不退役）。
  - 上述九个模块共九个子结构登记表文档：`core/gameplay/loot/README.md`、
    `core/gameplay/economy/README.md`、`core/gameplay/world_state/schema/README.md`、
    `core/gameplay/quest/schema/quest.def.md`、
    `core/gameplay/dialog/schema/dialog.gossip_menu.md`/`dialog.story_tree.md`、
    `core/gameplay/achievement/schema/README.md`、`core/gameplay/encounter/schema/README.md`、
    `core/gameplay/spawn/schema/README.md`、`core/gameplay/area_trigger/schema/README.md`。
- ADR-0019 F1b 新增测试：`core/gameplay/loot/tests/LootSchemaCoverageTests.cs`（10 条）、
  `core/gameplay/economy/tests/EconomySchemaCoverageTests.cs`（8 条）、
  `core/gameplay/world_state/tests/WorldStateSchemaCoverageTests.cs`（2 条）、
  `core/gameplay/quest/tests/QuestSchemaCoverageTests.cs`（19 条）、
  `core/gameplay/dialog/tests/DialogSchemaCoverageTests.cs`（17 条）、
  `core/gameplay/achievement/tests/AchievementSchemaCoverageTests.cs`（15 条）、
  `core/gameplay/encounter/tests/EncounterSchemaCoverageTests.cs`（13 条）、
  `core/gameplay/spawn/tests/SpawnSchemaCoverageTests.cs`（2 条）、
  `core/gameplay/area_trigger/tests/AreaTriggerSchemaCoverageTests.cs`（12 条，含 2 条从
  `AreaTriggerValidationRuleTests.cs` 迁移），覆盖变体键集合与运行时枚举/注册集合一致性、子
  结构命中/坏形状、退役规则不双报的回归。

- ADR-0019 F1c：L3 载体层（`core/carriers/`）、L1 数值层（`core/numbers/`）、L2 其余模块
  （`core/rules/ai|targeting|combat`）、L0（`core/foundation/display_info|input_map|
  scene_router`）、L5 表现层（`presentation/`）复合字段子结构登记，收口 ADR-0019 首批登记范围：
  - L3：`item.template.stats`（`{stat: Reference(stat.definition), op: Enum(flat|pct|mult),
    value: Number}`，`op` 枚举以 `EquipmentHost.ParseOp` 为准，与 07 原文示例 `flat|pct` 不同）、
    `grants`（`skills`/`auras` 分别登记为 `Reference(skill.def)`/`Reference(skill.aura_def)`，
    L3 引用 L2 程序集合法）、`weapon_profile`、`requirements`；`item.set.bonuses`（`aura_ref`
    升级为 `Reference(skill.aura_def)`）、`item.budget_curve.entries`；`gobj.template.type_data`
    因判别字段 `kind` 与被判别对象不同层，登记为 `Fields`（十种 kind 字段并集，`GobjTypeData
    FieldGroupRule` 不退役）而非 `Variants`；`on_use`/`gobj.lock.requirement` 判别字段与被判别
    对象同层，登记为 `Variants`（`item_id` 升级为同层 `Reference(item.template)`）；
    `creature.template.base_stats` 为 `Map<stat_id, Number>`，按通用规则不登记。
  - L1：`stat.rating_conversion.entries`、`prog.level_curve.entries`（`growth` 为 Map 不登记）、
    `arch.talent_tree.nodes`（`grants` 未被任何运行时代码进一步解析，不登记子结构）、
    `arch.power_type.max_source` 登记为 `Variants`（`fixed`/`stat`，`stat` 升级为同程序集
    `Reference(stat.definition)`）；`arch.class.base_stats`/`arch.race.stat_mods` 为 Map，不登记。
  - L2：`ai.rotation.entries`（`priority`/`condition`/`skill_id` 均按运行时直接强转判断记录为
    必填，偏离方案"priority?"示例；`skill_id` 升级为 `Reference(skill.def)`）、
    `ai.patrol_path.points`（`FieldKind.Vec2` 元素）、`ai.behavior_profile.transitions` 为
    Map，不登记；`target.chain_def.shape` 登记为 `Variants`（circle/cone/line/rect）、`filters`
    登记 `Item`（`FieldKind.String`，不登记为 `Expr`——内置简写如 `relation:hostile` 不是合法
    Expr 语法）、`sort_by` 登记 `Fields`；`combat.hit_table_config` 六个分支登记共用 `Fields`
    （`stat` 升级为 `Reference(stat.definition)`）、`combat.resist_curve.entries`。
  - L0：`display.map.mirror_pairs`/`paperdoll_layers` 登记 `Item`（`anchor_points`/
    `default_slot_meshes`/`material_params` 为 Map，不登记）；`found.input_action.
    default_bindings` 登记 `Item`；`world.map.spawn_points`/`teleport_points` 共用同一份
    `{id?:String, position?:{x,y}}` 元素结构（`facing` 全仓库无运行时读取代码，不登记）；
    `found.time_model.grid_snap` 全仓库无运行时读取代码，本轮不登记（偏离方案建议）。
  - L5：`camera_profile.bounds`/`shake_presets` 登记 `Fields`/`Item`；
    `feedback.binding.actions` 登记为 `Variants`（六种动作，`style_id` 升级为同表
    `Reference(feedback.floating_text_style)`；`vfx_id`/`sfx_id`/`profile_id` 退回 `Id`）；
    `shell_menu_definition.entries` 登记 `Item`（`target_panel` 升级为同层
    `Reference(ui_layout_definition)`，`text_key` 升级为 `FieldKind.TextKey`）；
    `ui_layout_definition.fields`（非同构 Map，是引擎适配层解释的不透明 blob）、
    `display.weapon_style.cast_anim_override`/`impact_vfx_override`（`Id→Id` Map）按通用规则
    均不登记。
  - `item.affix.effects`（全仓库搜索无任何运行时解析代码，07 原文标注"本版不实现具体效果"）本轮
    不登记，偏离任务书"若要复用 `EffectsItemSchema`"的示例（该示例以"字段已被实际解析"为前提，
    与运行时现状不符）。
  - 新增测试：`core/carriers/gobj/tests/GobjSchemaCoverageTests.cs`（11 条）、
    `core/carriers/item/tests/ItemSchemaCoverageTests.cs`（13 条）、
    `core/numbers/stat_block/tests/StatSchemaCoverageTests.cs`（2 条）、
    `core/numbers/progression/tests/ProgSchemaCoverageTests.cs`（2 条）、
    `core/numbers/archetype/tests/ArchSchemaCoverageTests.cs`（2 条）、
    `core/numbers/power_set/tests/PowerSchemaCoverageTests.cs`（4 条）、
    `core/rules/targeting/tests/TargetSchemaCoverageTests.cs`（5 条）、
    `core/rules/combat/tests/CombatSchemaCoverageTests.cs`（5 条）、
    `core/foundation/display_info/tests/DisplaySchemaCoverageTests.cs`（3 条）、
    `core/foundation/input_map/tests/InputActionSchemaCoverageTests.cs`（2 条）、
    `core/foundation/scene_router/tests/WorldMapSchemaCoverageTests.cs`（2 条）、
    `presentation/camera/tests/CameraSchemaCoverageTests.cs`（3 条）、
    `presentation/feedback_binder/tests/FeedbackActionsSchemaCoverageTests.cs`（4 条）、
    `presentation/shell/tests/ShellMenuSchemaCoverageTests.cs`（3 条），另有 `core/rules/ai`
    既有 `AiContentValidationRuleTests.cs` 补齐 `skill.def` 假数据依赖，覆盖变体键集合一致性、
    子结构命中/坏形状、退役规则不双报的回归。

- ADR-0018 决策 3（编辑器随游戏走：框架侧交付物）——无头适配层交付：`Adapters.Stub`（对外称
  "无头适配层"）由此前"仅测试用、不对外发布"转正为框架正式交付物：
  - zip 通道：`build.ps1 -Dist`/`-Release` 新增拷贝 `Adapters.Stub.dll` + 说明文档到
    `dist/<ver>/adapters/headless/{Adapters.Stub.dll, README.md}`；`MANIFEST.txt` 新增
    `[headless_assemblies]` 段（sha256）；`dist/ws-game-<ver>.lock` 新增 `headless_dlls`
    字段（`{"Adapters.Stub.dll": <sha256>}`）。`toolchain/get_framework.ps1` 校验该字段存在
    时的哈希（老锁文件没有该字段时跳过并提示，向后兼容）。
  - 私服通道：新增第四个可发布包 `com.gamefoundation.adapter.headless`
    （`toolchain/registry/manifests/adapter-headless/`），内容放包内 `Lib~/`；
    `toolchain/registry/registry.json`/`build.ps1` 5.15 节/`check.ps1`"包清单一致性"步骤同步
    为四个包；该包不是 Unity 依赖，`get_framework.ps1 -FromRegistry` 不写入游戏工程
    `Packages/manifest.json`，只登记进 `ws-game.lock` 的 `source.optional_packages` 字段并
    在输出中提示对应 `npm install` 命令。
  - `toolchain/tests/test_package_name_consistency.py`（新增）：断言四个包名在
    `registry.json`/`build.ps1`/`check.ps1`/`get_framework.ps1` 四处一致。
- ADR-0018 决策 3——校验装配入口：核心库 `presentation/assembly` 新增单一公开入口
  `ContentValidationAssembly`（`ContentValidationOptions`/`ContentValidationRun`），把此前只
  内联在 `toolchain/validator/Program.cs` 里的"汇总注册目录 + 可选规则接线参数 + 装配选项"
  逻辑抽出，供 `toolchain/validator` 与编辑器基础套件（独立消费方项目）共用同一份装配代码：
  - `ContentValidationAssembly.Run(sources, options?)`：一次性完成建 registry、注册全部
    L0～L5 `TableSchema`/`IValidationRule`（含可选规则 `SpawnSummonOnlyCreatureRule`/
    `DisplayMapCoverageRule` 按 `options` 是否提供接线参数决定是否注册）、`LoadAll`、汇总结果
    （含 `DisabledOptionalRules`/`EnabledOptionalRules` 如实汇报未启用的可选规则，不静默跳过）。
  - `ContentValidationAssembly.CreateRegistry(primary, options, out disabledOptionalRules)`：
    只做到"注册完成、不加载"，供需要先持有 registry 的宿主（如编辑器）使用。
  - `toolchain/validator` 改为只做参数解析 + 调用该入口 + 打印；新增可选参数
    `--display-map-sources <table:idField,...>`；`--json` 输出新增 `disabled_optional_rules`/
    `enabled_optional_rules` 数组，文本输出末尾追加一行 `optional rules disabled: ...`
    （既有输出的其余部分逐字节兼容）。
  - 新增测试 `presentation/assembly/tests/ContentValidationAssemblyTests.cs`（5 条）：固定
    可选规则清单、默认选项下两条均未启用、接线后均启用且真实生效（`DisplayMapCoverageRule`
    构造真实触发场景）、`WarningsBlock` 下警告置 `IsBlocking`、与直接调用
    `PresentationSchemaCatalog.RegisterAll` 得到的问题集合一致。

### 行为变更与迁移说明

- ADR-0019 F1c：以下手写结构校验按登记覆盖情况退役/收窄（检查名集合变化，均属 MINOR 级，无数据
  表字段删改）：
  - `core/carriers/gobj/schema/GobjValidationRules.cs`：`GobjOnUseKindRule`（检查名
    `gobj_on_use_kind`）/`GobjLockRequirementFieldGroupRule`（检查名
    `gobj_lock_requirement_field_group`）两条纯结构规则整条删除，改由 `on_use`/`requirement`
    的 `Variants` 登记（`variant_discriminator`/`required_field`/`field_type`）完全覆盖；
    `GobjTypeDataFieldGroupRule`（判别字段 `kind` 与 `type_data` 不同层）不退役。
  - `core/rules/ai/core/AiContentValidationRule.cs`：`ValidateRotations` 的元素非对象/
    `priority`/`condition`/`skill_id` 缺失或类型不符四类结构检查、`condition` 的 Expr 解析
    （检查名 `expr_parsable`）整段删除——`condition` 登记为 `FieldKind.Expr` 后与顶层字段共用
    同一份 `DataRegistry.ValidateExprField` 实现，继续跑自定义域 Schema 解析会与内置校验对同一
    条坏数据双报；仅保留 `priority` 同表内不重复这条纯业务判断（数组内多元素互相比较，登记层
    表达不了）。`ValidateBehaviorProfiles`（`transitions` 是 Map，未登记子结构）/
    `ValidatePatrolPaths`（`points` 数量 `>= 2`）未改动。
  - `core/rules/targeting/core/ChainDefValidationRule.cs`：`filters` 元素非字符串的检查
    （检查名 `target_filter_type`）整条删除，改由 `filters` 的 `Item`（`field_type`）覆盖；
    内置简写识别 + Expr 解析（`target_filter_expr_parsable`）、`source` 已注册、`fallback`
    成环三项业务判断未改动。
  - `core/rules/combat/schema/CombatValidationRules.cs`：`CombatResistCurveValidationRule` 的
    `entries[]` 元素非对象/`value`/`reduction` 缺失或类型不符检查（检查名
    `resist_curve_entries_shape`）整条删除，改由 `entries` 的 `Item`
    （`required_field`/`field_type`）覆盖；单调性、`kind=table` 时非空、`kind=saturation` 时
    `k` 必填且为正、`max_reduction` 区间四项业务判断（判别字段 `kind` 与 `entries`/`k` 是表
    顶层平级字段，`Variants` 不适用）未改动。`CombatHitTableValidationRule` 未改动（六个分支
    此前无对应结构检查，本轮子结构登记是纯新增覆盖）。
  - 已通过既有内容数据（`data/_sample`、`data/_framework`）0 错误验证，`data/_sample/stat/
    stat.definition.json`/`stat.rating_conversion.json`/`l10n/l10n.text.json` 补充此前缺失的
    `stat.dodge_rating`（连同其 `stat.rating.dodge` 评级曲线与 `l10n.stat.dodge_rating.name`
    文本键）——`combat.hit_table_config.dodge.stat` 一直引用这个未登记过的属性 id，此前因
    `stat` 字段未登记子结构、这条引用完整性检查从未真正跑过，本轮修复样例数据而非放宽登记。
  - 大量既有单元测试夹具（`core/carriers/item|gobj`、`core/rules/ai`、
    `core/gameplay/assembly` 等）内联的假 `skill_id`/`item_id`/`stat`/`style_id` 等 id 此前
    不受跨表引用完整性约束，本轮子结构登记升级为 `Reference` 后需要夹具补充匹配的最小合法数据
    （`skill.def`/`item.template`/`stat.definition`/`feedback.floating_text_style`/
    `target.chain_def` 等），测试断言的业务行为本身未变化。
- 嵌套 `Expr` 字段现经登记递归校验（`DataRegistry.ValidateFieldValue` 对子层 `FieldKind.Expr`
  与顶层共用同一份 `ValidateExprField` 实现，见 F1a），ADR-0015"疑似引用拼写错误"警告会在嵌套
  表达式上出现：`data/_sample` 的 `dialog.gossip_menu` 现报 3 条此类警告（`dialog.sample_hunter`
  的 `options[].visible_if` 对 `"quest.sample_hunt"` 的引用），均为对 `quest.*` 内容 id 的合法
  引用，属规范内的警告级噪音，不阻断（`toolchain/validate_data.py`/`check.ps1 -Quick` 均已确认
  0 错误通过）；`games/_template/validate.ps1 -Strict` 或 `validate_data.py --strict` 下警告会
  阻断，游戏侧启用严格模式前需核对并按需给 `quest.sample_hunt` 补一条真实引用或调整措辞消除
  误报。
- `core/rules/skill/schema/SkillValidationRules.cs`：`EffectKindRegisteredRule`/
  `CostEntryShapeRule` 两条手写结构校验规则整条退役，`ChargesShapeRule` 的结构部分退役、唯一
  业务判断（`charges.max >= 1`）收窄保留为新规则 `ChargesMaxAtLeastOneRule`——三者要检查的坏
  形状全部由 `SkillSchemas.Def`/`AuraDef` 的子结构登记 + `DataRegistry` 递归校验覆盖，不再需要
  单独注册（`core/rules/assembly/RulesSchemaCatalog.cs` 同步调整注册列表）。已通过既有内容数据
  （`data/_sample`、`data/_framework`）0 错误 0 警告验证，无需修改任何样例数据行。
- 若游戏层曾自行 `RegisterValidationRule(new EffectKindRegisteredRule())` 等注册上述已删除的
  规则类型，需要移除对应调用（改由 `SkillSchemas` 的登记自动覆盖同等检查）；曾依赖
  `unknown_effect_kind`/`effect_kind_missing`/`effect_entry_not_object`/
  `cost_entry_not_object`/`cost_power_type_invalid`/`cost_amount_invalid`/`charges_max_missing`/
  `charges_recharge_time_missing`/`charges_recharge_time_not_number` 这些检查名做过滤/展示的
  下游工具（如内容编辑器/CI 报告），改为识别 `required_field`/`field_type`/
  `variant_discriminator` 三个检查名（属 MINOR 级：新增能力、既有检查名未删改，但检查名集合
  的组成发生了变化）。
- `skill.def.effects[].params` 内以下字段的引用严格度按判断记录做了收窄（未按 04 §3.2 示例
  登记为强 `Reference`/必填，理由见 `core/rules/skill/schema/SkillSchemas.cs`/`README.md`
  对应判断记录）：`trigger_spell`/`modify_cooldown`/`add_charge`/`learn_skill` 的 `skill_id`
  按 `Id` 登记（不做跨表存在性校验）；`summon.creature_template`/`create_item.item_template`/
  `set_world_flag.flag_key`/`script.hook_id` 按 `Id` 登记且非必填。
- ADR-0019 F1b：以下手写结构校验按登记覆盖情况退役/收窄（检查名集合变化，均属 MINOR 级，无数据
  表字段删改）：
  - `core/gameplay/loot/core/LootContentValidationRule.cs`：不再委托 `LootTableParser.Parse`
    报告结构性坏形状，收窄为五类登记表达不了的业务判断（`ref` 领域+存在性、`weight_or_chance`
    区间、`count_range` 区间、`pick_count`、`guaranteed_min`）+ 嵌套引用成环检查。
  - `core/gameplay/economy/core/EconomyContentValidationRule.cs`：不再委托
    `EconomyDataParser` 报告结构性坏形状；`sell_items[].price_currency_id` 手写的货币存在性
    核对整条退役，改由 `reference_integrity` 覆盖；收窄为三类业务判断（`price_amount`/
    `stock_limit` 非负、`restock_policy=timer` 时 `restock_timer` 必填且为正）。
  - `core/gameplay/quest/core/QuestContentValidationRule.cs`：不再整条委托
    `QuestDefinition.FromRecord` 把任意解析异常打包报告，收窄为七类业务判断（`objectives`
    数量/`count` 约束、kill/escort 的 `target_ref` domain 弱校验、`rewards` 数值范围/存在性）；
    structural 类问题改由 `DataRegistry` 独立报告。
  - `core/gameplay/dialog/core/DialogContentValidationRule.cs`：gossip_menu 侧整条委托解析异常
    的检查退役；story_tree 侧检查名从笼统的 `dialog_content` 拆分为
    `story_tree_min_nodes`/`story_tree_duplicate_node_id`/`story_tree_dangling_next_node`/
    `story_tree_cycle` 四个更具体的检查名（图结构业务判断保留，未退役）。
  - `core/gameplay/achievement/core/AchievementContentValidationRule.cs`：`criteria[].type`
    合法性、`observe_event`/`count` 缺失/格式检查退役（改由结构登记覆盖）；新增两项此前完全无
    校验覆盖、只在运行期构造时才崩溃的检查 `achv_criteria_min_count`/
    `achv_criterion_count_positive`；`observe_event` 成员资格检查收窄为
    `achv_observe_event_unregistered`。
  - `core/gameplay/encounter/core/EncounterContentValidationRule.cs`：`waves[].trigger_condition`/
    `phases[].enter_condition` 两处手写 Expr 可解析检查整条删除，改由结构登记的
    `FieldKind.Expr` 子字段经 `DataRegistry` 递归校验覆盖（且更严格，额外跑
    `ExprValidator.Validate` 静态校验）；`units[]` 二选一、`spawn_ref`/`spawn_refs[]` 的
    domain 校验（跨字段/跨表业务判断）保留。
  - `core/gameplay/area_trigger/schema/AreaTriggerValidationRules.cs`：`AreaTriggerShapeKindRule`
    （`shape.kind` 合法性检查，检查名 `area_trigger_shape_kind`）整条退役，由 `Variants` 内置的
    `variant_discriminator` 检查完全覆盖；`AreaTriggerParamsFieldGroupRule`（按 `trigger_type`
    决定 `params` 哪些字段必填的业务判断，判别字段与被判别对象不同层、登记层无法覆盖）保留。
    `core/gameplay/assembly/GameplaySchemaCatalog.cs` 的 `RegisterAreaTriggerSchemas` 同步删除
    对应注册行。
  - `core/gameplay/spawn`：勘察确认 `spawn.table` 全部字段为标量（判别字段
    `respawn_policy`/`respawn_timer` 与其它模块的 shape/params 同构、不同层），
    `SpawnRespawnPolicyFieldGroupRule`/`SpawnContentRefRule`/`SpawnSummonOnlyCreatureRule` 均
    因结构上无法登记覆盖或属跨表业务判断而原样保留，本轮不退役任何规则。
  - 已通过既有内容数据（`data/_sample`、`data/_framework`、`games/_template/data`）0 错误验证，
    未修改任何样例数据文件。
- 曾依赖旧的单一检查名（`loot_content`/`economy_content`/`quest_content`/`dialog_content`/
  `achievement_content`/`encounter_content`/`area_trigger_shape_kind`）做过滤/展示的下游工具，
  改为识别上述新检查名，以及结构层既有检查名
  `required_field`/`field_type`/`variant_discriminator`/`reference_integrity`/`expr_parsable`。
- `quest.def.objectives[].target_ref`：`kill`/`escort` 两处按判断记录退回 `Id`（不做跨表存在性
  检查——既有测试用不落 `creature.template` 内容表的 id 驱动 kill 目标）；
  `collect`/`interact`/`explore`/`cast` 四处升级为 `Reference`，分别指向
  `item.template`/`gobj.template`/`area.trigger_def`/`skill.def`。
- `dialog.gossip_menu.options[].actions[]`：`vendor`/`teleport`/`set_flag`/`script` 四处 `ref`
  退回 `Id`（分别因无登记表/非 DataRegistry 表/`world.flag_schema` 非运行态/`found.hook` 无实现
  级 schema）；`achv.def.criteria[].observe_event`/`target_ref` 均未登记为 `Reference`（既有测试
  用最小 registry 驱动大量用例，登记会误报 `reference_integrity`）。
- `encounter.def.units[].template_ref` 升级为 `Reference(creature.template)`；
  `units[].spawn_ref`、`waves[].spawn_refs[]`、`phases[].on_enter_hook` 保持 `Id`（前二者刻意
  经 `SpawnRequester` 与 `core/gameplay/spawn` 决耦，后者 `found.hook` 无实现级 schema）。
- ADR-0018 决策 3：`ws-game.lock`（zip 通道）新增可选字段 `headless_dlls`（老锁文件没有该字段
  时 `toolchain/get_framework.ps1` 跳过对应哈希校验并提示，不报错、不阻断，向后兼容）；
  `-FromRegistry` 通道写入的 `ws-game.lock` 新增 `source.optional_packages` 字段（可选包清单，
  当前只含 `com.gamefoundation.adapter.headless`），不影响既有 `source.packages` 字段语义
  （仍是写入游戏工程 `Packages/manifest.json` 的三个 Unity 依赖）。
- `toolchain/validator --json` 输出新增 `disabled_optional_rules`/`enabled_optional_rules`
  两个数组字段（追加在既有字段之后）；文本输出末尾新增一行
  `optional rules disabled: <逗号分隔的规则名，或 none>`；新增可选命令行参数
  `--display-map-sources <table:idField,...>`。既有字段/行的内容与含义不变。
- `check.ps1`"包清单一致性"步骤核对的包数量由三个改为四个（新增
  `com.gamefoundation.adapter.headless`），`build.ps1 -Dist`/`-Release` 组装出的 `packages/`
  目录与 `.tgz` 数量同步由三个改为四个。`.github/workflows/release.yml` 涉及三个 `.tgz` 的
  多处硬编码已于 F3 补齐（见下"F3：元数据门禁、开发期数据热重载、release.yml 四包修正"小节），
  此前"已知限制，留待后续单独跟进"的说明不再适用。
- F3（ADR-0018 决策 3/5、ADR-0019 决策 4）：
  - **元数据门禁**：新增 `presentation/assembly/SchemaAudit.cs`（`Presentation.Assembly.SchemaAudit`），
    对全部已登记 `TableSchema`/`FieldSchema` 结构声明本身做静态审计（不加载任何数据），六项检查
    ——`missing_description`/`composite_without_substructure`/`allowlist_entry_unused`/
    `reference_target_unknown`/`variant_shape`/`unschematized_table`；`DataRegistry` 新增
    只读属性 `RegisteredSchemas`（具体类，不改 `IDataRegistry`/`IDataRegistryView` 接口签名）。
    `toolchain/validator` 新增 `--schema-audit [--allowlist <path>] [--json]` 命令行模式；
    `check.ps1` 新增步骤"元数据门禁：validator --schema-audit"，`-Quick` 下也跑。新增白名单文件
    `toolchain/schema_audit_allowlist.json`（20 条，均为动态键 Map 型对象或模块显式声明"暂不
    解析"的扩展位，逐条写明 reason）；首次审计跑出的全部 `missing_description`（452 处原始命中，
    经修复"自引用 schema 环检测"缺陷后去重为 227 处真实字段）已在对应 `*Schemas.cs` 文件里真正
    补齐中文描述，不是加白名单绕过。`encounter.def.rewards` 改为复用 `quest.def`/`achv.def` 已有
    的 `QuestSchemas.RewardsFields`（此前是裸 `Object`，无 `Fields` 子结构）。
  - **开发期数据热重载标准实现**：新增 `games/_template/Runtime/DataHotReload.cs`（仅
    `UNITY_EDITOR || DEVELOPMENT_BUILD` 下编译为有效实现，其余情况是空壳，发布构建零开销）；
    `GameOptions` 新增口味开关 `EnableDataHotReload`（默认 `true`）；`GameBootstrap` 在数据加载
    成功后按该开关挂载，监视框架数据根与游戏数据根下 `*.json`，去抖 300ms 后调用
    `DataRegistry.Reload`，成功/失败分别补发 `data.load_completed`/`data.validation_failed`
    （`Reload` 本身不发这两个事件，只有 `LoadAll` 会发）。
  - **`.github/workflows/release.yml` 四包修正**：`Check for existing release assets`/
    `Repair missing assets from existing zip` 两步的必需附件集合由"三个 `.tgz`/五个附件"补齐为
    "四个 `.tgz`/六个附件"（新增 `com.gamefoundation.adapter.headless-<ver>.tgz`）。
    `toolchain/tests/test_package_name_consistency.py` 新增一条测试，把 `release.yml` 的
    `$requiredNames`/`$requiredPaths` 纳入四处一致性核对（此前只核对
    `registry.json`/`build.ps1`/`check.ps1`/`get_framework.ps1` 四处，`release.yml` 曾是漏网
    的第五处）。
- **迁移说明（元数据门禁，F3）**：`check.ps1` 新增的"元数据门禁"步骤从此会阻断提交——任一已登记
  `FieldSchema` 缺描述（`missing_description`）、或 `Object`/`Array` 字段未登记子结构且未在白
  名单声明（`composite_without_substructure`）都会让该步骤以非零退出码失败，`-Quick` 子集同样
  跑这一步（秒级，不需要 Unity/构建产物）。游戏侧若有自定义 `TableSchema` 经
  `IDataRegistry.RegisterSchema` 登记进与框架共用的同一个 `registry`（例如
  `PresentationSchemaCatalog.RegisterAll` 之后再补注册游戏专属表），这些自定义表同样会被本仓库
  的 `SchemaAudit`/`check.ps1` 步骤审计到（若游戏侧直接复用本仓库的 `check.ps1`）——升级后首次
  遇到阻断，按检查名分两种处理：`missing_description` 必须给对应 `FieldSchema` 补一句中文描述，
  不能靠白名单绕过；`composite_without_substructure` 若确认是动态键 Map 型对象/暂不支持结构化
  登记的字段，在 `toolchain/schema_audit_allowlist.json`（或游戏侧自己维护的等价白名单文件，
  经 `--allowlist <path>` 指定）新增一条 `{"table", "field", "reason"}`，`reason` 必填、必须
  写清楚为什么这个字段暂时/永久不适合登记子结构，白名单条目不再对应任何真实命中时
  `allowlist_entry_unused`（Warning，不阻断）会提醒清理。
- `games/_template/`"复制为新游戏：改哪几处"新增第四个 asmdef
  `Tests/Editor/Game.Template.EditorTests.asmdef`（F3 随 `DataHotReloadEditModeTests.cs` 一并新增，
  此前遗漏在改名清单外）：复制模板改名时需一并处理该文件的文件名、`"name"` 与 `references` 数组，
  否则改名后的消费方工程编译报 `DataHotReload` 找不到（`toolchain/consumer_smoke.ps1` 同步补齐，
  见该脚本 `$asmdefRenames`）；`games/_template/README.md` 目录清单与改名步骤已同步更新。
- `DataHotReload.ProcessPendingChangesForTests`（原 `internal` + 程序集级
  `InternalsVisibleTo("Game.Template.EditorTests")`）改为公开方法
  `DataHotReload.ProcessPendingChanges()`：模板改名后程序集名变化会让硬编码旧程序集名的
  `InternalsVisibleTo` 失效，改为公开方法即可在改名后继续被测试/编辑器/无头宿主调用，与
  `TemplateSmokeRunner` 公开而非 internal 的既有判断一致；`Update()` 内部改为调用同一个公开方法，
  行为未变。若游戏层曾直接调用 `ProcessPendingChangesForTests`，改调 `ProcessPendingChanges`。

### 已知问题：ABI/API 兼容性（第十四轮修订版审核，2026-09-09，基线 `c9ff301`；已在 1.14.0 收口）

外部审核（`architecture/落地计划/audit-c9ff301-20260909/`，独立 consumer probe，见
`docs-project/api-compat/api-compat.log`）核实：本版本对已编译旧 consumer **不是**二进制兼容——
`FieldSchema` 的 1.12 七参数公开构造函数
`FieldSchema(string, FieldKind, bool, IReadOnlyList<string>, string, string, string)` 在 1.13
正式 `Core.Foundation.dll` 中已不存在，旧编译产物只替换正式 DLL、不重新编译，运行期抛
`System.MissingMethodException`；`core/carriers/gobj/schema/GobjValidationRules.cs`/
`core/rules/skill/schema/SkillValidationRules.cs` 一并整条删除的
`GobjOnUseKindRule`/`GobjLockRequirementFieldGroupRule`/`EffectKindRegisteredRule`/
`CostEntryShapeRule`/`ChargesShapeRule` 五个公开规则类型同属已删除的公开签名（源码重编译可绕开
`FieldSchema` 新增的可选参数、但不能让旧签名重新出现，也不能恢复已删除的公开类型）。上一条
"版本判据说明"曾表述为"无任何公开签名删改"，与上述探针结果不一致，此处更正为准确口径：**本版本
按 SemVer 声明为 MINOR，但对已编译的旧 1.12 consumer 存在已知二进制不兼容**，源码级消费方按上方
"行为变更与迁移说明"逐条改造仍可正常编译通过。

**核实补充（第十四轮修订版审核收口，2026-09-10）**：核心侧全仓复查（`git diff v1.12.0 v1.13.0 --
'*.cs'` 扫公开签名）另发现审计报告未点名的两处：（1）`core/gameplay/area_trigger/schema/
AreaTriggerValidationRules.cs` 的 `AreaTriggerShapeKindRule` 同一批 ADR-0019 退役、同属整条删除的
公开规则类型（第六个，五个之外的遗漏）；（2）`core/gameplay/economy/core/
EconomyContentValidationRule.cs`/`core/gameplay/loot/core/LootContentValidationRule.cs` 的 1.12
构造签名 `(IExprSchema? conditionSchema = null)` 均被收窄为隐式无参构造——不是整条类型删除，但同样
属于物理签名不兼容（可选参数默认值在旧调用点编译期内联进 IL，换 DLL 不重编译同样
`MissingMethodException`）。以上"六个已删除类型 + 两处构造签名收窄"已在 **1.14.0** 全部恢复为
`[Obsolete]` 兼容 façade，详见下方 `[1.14.0]` 正式条目；本小节保留作为 1.13.0 版本本身的已知状态
记录，不因后续版本修复而删除。

### 版本判据说明

新增能力与向后兼容的契约扩展——`FieldSchema` 新增可选子结构登记（`Fields`/`Item`/`Variants`，
仅对既有 `Object`/`Array` 字段种类新增可选行为，未登记的字段不受影响）、新增
`ContentValidationAssembly`/`SchemaAudit` 两个核心库公开入口、新增第四个私服包
`com.gamefoundation.adapter.headless`、`ws-game.lock` 新增可选字段
`headless_dlls`/`source.optional_packages`、`DataRegistry` 新增只读属性
`RegisteredSchemas`（具体类新增成员，不改 `IDataRegistry`/`IDataRegistryView` 接口签名）、
`games/_template` 新增模板热重载标准实现（`DataHotReload.cs`，仅编辑期/开发构建生效，发布构建
零开销空壳）。退役的手写结构校验规则（`GobjOnUseKindRule`/`EffectKindRegisteredRule` 等，见上
"行为变更与迁移说明"）改由登记层递归校验完全覆盖同等检查，属检查名集合变化，不影响校验结论；
无任何数据表字段删改，无任何存档格式变更。**"无任何公开签名删改"一句已按上方"已知问题：ABI/API
兼容性"小节更正**——`PlayerVitalsPersistable` 构造函数收窄发生在 1.12.0、本版未涉及是准确的，但
`FieldSchema` 七参数构造函数、六个手写规则类型（含核实补充发现的 `AreaTriggerShapeKindRule`）与
两处构造签名收窄（`EconomyContentValidationRule`/`LootContentValidationRule`）确在本版本删除/改签，
属已知的二进制不兼容，不属于"公开签名零删改"。按 SemVer 判定为 MINOR（新增能力占多数；已知的二进制
兼容缺口已在 1.14.0 通过恢复 façade 收口，见上方小节与 `[1.14.0]` 正式条目）。

## [1.12.0] - 2026-09-09

第十五方深度审核（codex 第十三轮，基线 `6739f50`，即 1.11.0 发布提交）2 项核心侧确认缺陷
（CORE-111-01、TP-111-01）+ 3 项表现/引擎侧确认缺陷（UI-111-01、NAV-111-01、SPATIAL-111-01）
逐条核实并根治，另处理若干核心侧文档/注释勘误、02/09 性能约定改写为准确边界、能力索引新增
"暂不落地（用户拍板）"分类并归入编辑器工具、补齐此前遗漏的能力索引行。逐条核实表、判断记录、
验收结果见
[audit-6739f50-20260909/followup-2026-09-09b.md](architecture/落地计划/audit-6739f50-20260909/followup-2026-09-09b.md)。
均属核心存档/传送缺陷修复、表现层缓存缺陷修复、引擎适配层寻路/空间查询缺陷修复与文档口径修正，
无数据表字段删改；存档格式有向后兼容的新增字段（见下"行为变更与迁移说明"）。

### 新增

- **`Core.Numbers.PowerSet.PowerHost.GetRegisteredPowerTypes(Id unitId)`/`IsInCombat(Id unitId)`**
  （新增公开方法，CORE-111-01 根治）：枚举某单位已注册的全部资源类型 id、查询当前进出战斗运行态；
  均只加在具体类 `PowerHost` 上，不进 `IPowerHost` 契约，不影响本仓库其它独立实现该接口的测试假
  类型。
- **`player.vitals` 存档段新增 `in_combat`（`Bool`）/`powers`（`Map<Id, Number>`）两个字段**
  （CORE-111-01 根治）：覆盖该单位读档前那一刻已注册的全部资源池当前值与进出战斗运行态，不再只
  挑生命值一种资源；旧字段 `health` 继续保留写入（供仍直接读取旧字段名的外部工具过渡使用）。
- **`Core.Gameplay.Assembly.PlayerVitalsPersistable(PlayerUnit, IPowerHost)`**（源码兼容重载，
  标记 `[Obsolete]`）：CORE-111-01 把生产构造函数第二参数类型从 `IPowerHost` 收窄为具体
  `PowerHost` 后，为保持本仓库之外可能存在的消费方源码兼容而补回的旧签名重载，仅做类型收窄转发
  （传入的 `IPowerHost` 若实际不是 `PowerHost` 实例则抛出明确的 `ArgumentException`）；框架自身
  生产装配点已经改用具体类型重载，不受影响。
- **"能力边界与未默认接入能力索引"表新增第四类分类"暂不落地（用户拍板）"**：区别于"未实现"
  （框架当前无对应运行期逻辑）与"明确非目标"（已有决策记录判定本版不展开）——本类特指"框架侧
  机制/产品方案已就绪或不构成技术障碍，但用户已明确拍板本阶段不安排落地"；编辑器工具（GF 内容
  编辑器）一行由"未实现"改列此类（产品文档已完成，实现按用户 2026-09-09 拍板暂缓）。索引表另
  按第十三轮审计报告补齐此前遗漏的五行——`ISkillHost.FindUnits`、位移轨迹碰撞、VFX 锚点持续
  跟随、nested teleport 元素/引用完整性校验、导航跨帧请求预算、空间查询完整索引化（均"未实现"）、
  `SpawnSummonOnlyCreatureRule` 查询接入（"已实现未默认接线"）。

### 修复

- **失败/成功读档回滚快照未覆盖 Health 之外的资源池当前值与进出战斗运行态（CORE-111-01，P2）**：
  见上"新增"两项。`PlayerVitalsPersistable.Save`/`Load` 泛化为覆盖全部已注册资源池当前值与
  `in_combat`，正向读档与失败回滚重放路径共用同一份 `Load` 实现；旧存档只有 `health` 字段时仍可
  正常读取（其余资源池按各自 `start_full`/默认规则初始化，不受影响）。
- **跨图传送先改写玩家字段再调用场景路由，路由拒绝（含 Loading 期间收到的第二个跨图请求）时异常
  被吞掉、字段已提交、场景与玩家 map/位置永久分叉（TP-111-01，P2）**：
  `GameplayAssembly.ApplyResolvedTeleport` 改为先尝试 `ISceneRouter.LoadScene`，只有路由未抛异常
  （确实接受本次导航请求）之后才提交 `entity.MapId`/位置；对"未知地图"与"当前不允许转入
  Loading"（含 Loading 中的第二次跨图请求）两类路由拒绝，采用默认"拒绝"策略——不排队、不重试，
  本次传送不产生任何字段副作用，交由上层按自己的重试/提示策略处理。同图内传送（不切地图）与未
  装配场景路由的场景不受影响。
- **视图模型读档后不重建缓存，需等待下一次手动 `Refresh` 才与宿主数据一致（UI-111-01，P2）**：
  `InventoryViewModel`/`ActionBarViewModel`/`CharacterStatsViewModel`/`DialogViewModel`/
  `HudViewModel`/`QuestLogViewModel`/`ShopViewModel`/`SkillBookViewModel` 八个视图模型的构造函数
  新增订阅 `SaveEventKeys.SaveLoaded`；`SaveSlotsViewModel` 本就已订阅；`PauseMenuViewModel`/
  `SettingsViewModel` 核对后确认与存档数据无关，不需要订阅。
- **Unity 导航窄通道：通道宽度窄于两级采样网格但直线本身畅通时 `FindPath` 仍返回 `null`
  （NAV-111-01，P2）**：`UnityNavigation2D.FindPath` 拆出私有 `FindPathViaGrid`（原逻辑不变），
  网格寻路彻底失败后新增"直线直达"兜底（新增私有 `SegmentHasClearContact`，要求线段与全部阻挡
  矩形完全无接触，比既有 `SegmentBlocked` 更严格，不越权覆盖 A* 已有的"禁止切角"结论）。
- **Unity 空间范围/锥形查询：候选桶范围未按被查询实体自身半径扩张，跨桶边界的大半径实体被漏选
  （SPATIAL-111-01，P2）**：`UnitySpatialQuery.QueryRadius`/`QueryCone` 的候选桶范围改为"查询
  半径/范围 + `MaxRadiusHint()`"（索引内最大实体半径）；`QueryRect`/`QueryShape`/`Nearest` 本就
  不经分桶（全量线性扫描），不受影响。
- **`architecture/02` §1.8 `findPath`/§1.9 `ISpatialQuery` 的"性能约定"过度承诺**："运行时应
  支持跨帧分摊、不得要求单帧内同步返回"与"查询应基于空间索引而非线性扫描"均与已知参考实现不符
  （跨帧预算未实现；`QueryRect`/`Nearest`/桩实现均为线性扫描），改写为准确的"边界"表述，不改变
  契约签名，版本号不变。
- **`architecture/09` §7.2 补充读档场景的视图模型缓存重建规则**：与 §4.4
  `IWeaponStyleSource` 缓存失效时机同一判断记录，版本号不变。
- 若干核心侧文档/注释现状化，均不改变任何运行期行为：`ArchSchemas.cs`/archetype `README.md`
  的 `passive_auras`/`skill_book_ref` 说明（`stat_block`/`power_set`/L2 `skill` 均早已实现，
  "不声明 Reference"的真正理由是分层边界与接口能力边界，不是对方模块尚未实现）；
  `PowerTickHandler.cs`/`IPowerDiagnostics.cs` 的过期判断记录（"本项目暂不启用离散时间模型"
  改判为"ADR-0013/sim_loop 已有基础离散调度，这只是本资源处理器自己的连续-only 边界"）；
  `UnityViewFactory.cs`/`GameFoundationBootstrap.cs` 的过期判断记录（"ViewBinder/CameraHost
  不支持退订"，`ViewBinder` 现已实现 `IDisposable`）。

### 行为变更与迁移说明

- **`player.vitals` 存档段格式扩展（向后兼容）**：新增 `in_combat`/`powers` 字段；读档时优先用
  `powers` 字段（存在即覆盖同一集合，缺失的当前值项静默跳过），只有 `powers` 字段完全缺失（旧
  格式存档）才退回旧的仅 `health` 路径。无需离线迁移脚本，读档即完成透明升级；旧档写回后自动
  变为新格式（不会自动降级回旧格式）。
- **`PlayerVitalsPersistable` 构造函数收窄**：生产构造函数第二参数类型从 `IPowerHost` 收窄为
  具体 `PowerHost`。本仓库之外若有消费方直接以旧签名 `new PlayerVitalsPersistable(player,
  someIPowerHost)` 构造，源码仍可编译（改走新增的 `[Obsolete]` 兼容重载），但要求传入的实例实际
  是 `PowerHost`（`IPowerHost` 目前唯一的生产实现）；传入其它实现会在构造期抛出
  `ArgumentException`（此前能编译通过，但也无法正常调用 CORE-111-01 新增的
  `GetRegisteredPowerTypes`/`IsInCombat`）。
- **跨图传送加载期间不再"静默接受第二次请求并覆盖玩家字段"**：升级前 Loading 中收到第二个跨图
  传送请求会让玩家 `MapId`/位置被改写成第二个请求的目标，即便该请求本身被路由拒绝、场景最终仍
  停在第一个请求的目标地图（字段与场景永久分叉）；升级后第二个请求整体不产生任何字段副作用。
  **依赖旧行为（Loading 期间发起的传送请求仍会生效于玩家字段）的调用方需要重新评估**——正确用法
  是等待首个请求完成（或失败）后再发起下一次传送。
- **自定义 `ISpatialQuery` 实现**：若游戏层提供了自己的 `ISpatialQuery` 实现（而非使用
  `UnitySpatialQuery`/`StubSpatialQuery`），且该实现也做了按桶（网格分区）加速的候选筛选，建议
  对照 SPATIAL-111-01 的根治手法核对候选桶范围是否已按被查询实体自身半径扩张，避免同一漏选问题。
- **`architecture/02`/`09` 两处"性能约定"改写为"边界"表述**：不改变任何契约签名或运行期行为，
  仅文档措辞更准确地反映当前参考实现的真实能力边界（跨帧分摊寻路预算、空间索引完整覆盖仍是
  "未实现"能力索引项，见上"新增"能力索引表更新）。

### 版本判据说明

新增公开成员（`PowerHost.GetRegisteredPowerTypes`/`IsInCombat`、`PlayerVitalsPersistable` 源码
兼容重载）与存档段向后兼容的新增字段（`player.vitals` 的 `in_combat`/`powers`），以及已记录在案
的行为修正（传送提交时机、视图模型缓存重建、导航/空间查询候选筛选）——按 SemVer 判定为 MINOR。

## [1.11.0] - 2026-09-09

第十四方深度审核（codex 第十二轮，基线 `ac3b622`，即 1.10.0 发布提交）3 项核心侧确认缺陷
（CORE-110-01～03）+ 2 项引擎侧确认缺陷（NAV-110-01/02）+ 1 项表现侧确认缺陷（PRES-110-01）逐条
核实并根治，另拍板落地方向移动导航阻挡契约、补齐 WorldMap 四字段 schema 登记、找回被
`.gitignore` 误伤的审计证据并收紧相对链接检查覆盖范围。逐条核实表、判断记录、验收结果见
[audit-ac3b622-20260909/followup-2026-09-09.md](architecture/落地计划/audit-ac3b622-20260909/followup-2026-09-09.md)。
均属核心存档/规则/载体层缺陷修复、引擎适配层寻路缺陷修复与工具链/归档完整性补强，无数据表字段
删改，无存档格式变更。

### 新增

- **`Core.Numbers.StatBlock.StatHost.ResetBase(Id unitId, Id stat)`**（新增公开方法，CORE-110-02
  根治）：清除某单位某属性此前显式 `SetBase` 过的值，恢复成"从未显式设置过"（即
  `stat.definition.default_base`）；不是 `IStatHost` 接口成员，不影响任何既有 `IStatHost` 假实现。
- **`WorldMapSchema`（`world.map`）四字段补齐类型校验**：`regions`（`IdList`）、
  `teleport_points`（`Array`）、`music_ref`（`String`）、`allowed_difficulties`（`IdList`），均为
  可选字段，按 `05_对象模型与世界.md` 第 4.1 节原文类型/必填性登记，只做类型校验、不做引用完整性
  校验（无独立登记表可查）；`data/_sample`/`data/_framework` 现有 `world.map` 记录均未使用这四个
  字段，登记不影响既有数据。
- **`UnityNavigation2D` 端点接合/薄障碍兜底**：`FindPath` 起止格量化后若格中心恰好落在阻挡区域
  内部，新增私有 `ResolveEntryCell` 在其 8 邻居里找一个可行走且与精确端点直连不受阻的格子接合
  （NAV-110-01 根治）；`FindPath` 收尾复核（逐段 `Raycast`）未通过时，新增私有
  `FindPathWithFineGrid` 用半格尺寸 + 精确占用判定的独立细网格重新完整寻路一次作为兜底，仍不通过
  才是真正无路可走（NAV-110-02 根治）——均为类型内部私有实现细节，`INavigation2D` 契约签名不变。
- **`EquipmentWeaponStyleSource` 订阅 `save.loaded`**（PRES-110-01 根治）：构造函数新增订阅，收到
  后整表清空内部风格缓存，下一次查询对全部实体重新解析真实 `EquipmentHost` 状态。
- **方向移动导航阻挡契约拍板落地（NAV-DOC-02）**：`MovementTickHandler.ApplyDirectionalMove` 新增
  对候选终点的 `Raycast`/`IsWalkable` 检查，与目标类移动共用同一套统一可通行规则（受阻按
  `MovementOptions.ArrivalEpsilon` 截断到入射点前，截断后仍不可行走则不动）；`architecture/05` 第
  6.1 节同步勘误。

### 修复

- **失败读档回滚后派生状态（评级换算属性/光环/资源池上限-当前值）不恢复（CORE-110-01，P2）**：
  `SaveSystem.RollbackLoadedSections` 此前只覆盖字段本身（`IPersistable.Load(快照)`），不重新调用
  `IDerivedStateRebuilder.OnSectionLoaded`——回滚只恢复字段不等于恢复由字段推导出的派生状态。改为
  对每个成功回滚的段按与正常读档相同的正向顺序重放一次 `OnSectionLoaded`；失败分支的回滚集合额外
  总是尝试把 `player.vitals`（资源池当前值唯一权威段）纳入，即便它本不在这次失败读档实际触碰过的
  段列表里。
- **同图跨职业读档只覆盖新旧共同基础属性键，旧职业独有键与资源类型集合未清理（CORE-110-02，P2）**：
  `RulesAssembly.ReloadArchetypeAndRace` 此前完全忽略 `previousClassId` 参数。改为按"旧种族修正 →
  旧职业独有基础键（`StatHost.ResetBase`）→ 新职业完整基础键 → 新种族修正/光环 → 资源类型对账"
  顺序根治（资源类型对账必须放最后，因为 `PowerHost.RegisterUnit` 按注册时刻的属性聚合值初始化
  上限）。
- **同一 tick 内多条 move 意图逐条重复推进位移（CORE-110-03，P2）**：`MovementTickHandler.Execute`
  此前对本 tick 存活的每一条 move 意图各自调用一次 `ApplyIntent`，同一单位同一 tick 提交 N 条意图
  会把固定的 dt 重复消费 N 次。落地公共语义"每单位每 tick 只积分一次，同 tick 多条 move 意图最后
  一条生效"：先扫描每单位本 tick 最后一条存活意图的下标，第二趟遍历只在命中该下标时调用一次
  `ApplyIntent`（用两趟遍历、不依赖 `Dictionary` 迭代顺序，保证单位间处理顺序与修复前一致）。
- **Unity 导航端点所在采样格中心被阻挡时 `FindPath` 拒绝可达路径（NAV-110-01，P2）**：见上"新增"
  `ResolveEntryCell`。
- **Unity 网格 A* 对窄于采样间距的薄墙视而不见，收尾防线命中即直接返回 `null`、不尝试绕路
  （NAV-110-02，P2）**：见上"新增"`FindPathWithFineGrid`。
- **读档后 `EquipmentWeaponStyleSource` 缓存未随 `SaveSystem.Load` 抑制作用域内重放的装备事件失效
  （PRES-110-01，P2）**：见上"新增"`save.loaded` 订阅。核对同类缓存来源
  `presentation/render/core/EquipmentVisualSource.cs`：理论上受同一抑制作用域问题影响，但不是同一
  种缺陷形状（无状态查询缓存 vs. 已应用到具体 View 实例的状态日志），不外推入本次修复范围，留待
  该模块自己的复现证据立项。
- **归档证据被 `.gitignore` `**/[Ll]ogs/` 规则误伤**：该规则未加路径前缀，连带忽略了
  `architecture/落地计划/audit-*/**/logs/` 这类要长期留存的审计证据目录；收窄为
  `adapters/unity/**/[Ll]ogs/`（只限定 Unity 工程自身日志缓存）。找回 e070e3f/3224ca1/8160178/
  c86bfa9 四个既往归档目录下共 30 处因此漏收录的证据文件；2 处原件已随会话清理彻底丢失（presentation
  动画回归证据）的链接如实标注"原件未归档"并登记进新增的 `toolchain/tests/.linkcheck-ignore` 白
  名单。
- **相对链接检查此前对 `audit-*` 目录整段排除，从未扫描审计报告内的证据链接**：
  `toolchain/tests/test_markdown_relative_links.py` 取消该排除，改为逐条判断（`.gitignore` 覆盖
  路径照旧跳过；新增识别 `path.cs:123` 源码行号引用记法并剥离后缀再判存在性；显式豁免走新增的
  `.linkcheck-ignore` 白名单）。
- **`MoveStopReason.BlockingChanged` XML 注释错误地把 `PathFailurePolicy.Stop` 失败归为该原因**（
  实际触发 `PathFailed`）、`LootExpiryTickHandler.cs:35` 旧注释与当前真实局部语义不符：均已改正，
  不改变任何运行期行为。

### 行为变更与迁移说明

- **同 tick 多条 move 意图的位移语义变更**：升级前同一单位同一 tick 提交 N 条 move 意图会把固定
  dt 重复消费 N 次（等价于"叠加位移"）；升级后固定为"最后一条生效，dt 只消费一次"。**依赖旧行为
  （连续提交多条 move 意图来叠加本 tick 位移量）的调用方需要改为一次性提交携带最终目标/方向的单条
  意图**——这不是框架推荐或文档化过的用法，是此前实现的副作用；`OnMoveStopped(Replaced)` 触发
  语义不变（至多一次，取决于上一 tick 是否已有路径，与本 tick 内提交过几条被丢弃的意图无关）。
- **方向移动现在遵守导航阻挡**：升级前 `ApplyDirectionalMove` 只检查同帧其它单位阻挡，从不查询
  `INavigation2D.IsWalkable`/`Raycast`，可以穿过地形阻挡；升级后与目标类移动共用统一可通行规则，
  受阻会被截断或完全不位移。**依赖旧行为（方向移动可以穿越导航阻挡区域）的调用方需要重新评估**——
  未装配 `INavigation2D` 的场景（`_navigation == null`）两项检查全部跳过，行为与升级前完全一致。
- **失败读档回滚后派生状态重建**：升级前失败读档回滚只恢复字段本身，评级换算属性/光环/资源池
  上限可能停留在读档失败前的陈旧值；升级后回滚会重放 `IDerivedStateRebuilder.OnSectionLoaded`。
  自定义 `IDerivedStateRebuilder` 实现需保证该回调幂等/可重入（此前只在成功路径被调用，现在也会
  在失败回滚路径被调用）。
- **跨职业读档清理旧职业基础键与资源类型**：升级前同图切换职业只覆盖新旧共同基础属性键，旧职业
  独有的基础属性键与资源类型（如法力）残留；升级后会清理干净。依赖"残留旧职业独有属性/资源类型"
  这一此前未文档化行为的调用方需要重新评估。
- **`.gitignore` `**/[Ll]ogs/` 规则收窄为 `adapters/unity/**/[Ll]ogs/`**：仓库根或
  `architecture/`/`core/` 等位置下字面叫 `logs` 的目录不再被自动忽略；若本机有依赖旧忽略范围
  存放临时文件的习惯，需要自行清理或改用其它已忽略目录（如 `bin/`）。

### 版本判据说明

- MINOR：`StatHost.ResetBase` 是新增公开方法（非接口成员）；`WorldMapSchema` 四字段均为可选字段
  的类型校验补齐，不影响既有数据；`UnityNavigation2D`/`EquipmentWeaponStyleSource` 新增成员均为
  类型内部实现细节，契约接口签名不变；三处"行为变更"均是缺陷修复（此前行为未被文档化为预期契约、
  且与既有文档/契约表述矛盾），按仓库既有版本判据惯例（见 1.9.0/1.10.0 条目）计入 MINOR 而非
  MAJOR。无删改既有公开签名，无存档格式变更，无数据表字段删改。

## [1.10.0] - 2026-09-09

导航与移动公共接口补齐（W9，响应游戏侧 5 项需求：停止/取消接口、端点契约与精确接合、寻路与
Raycast 拐角判定统一、寻路失败公共处理契约、动态阻挡后现有路径处理），逐项落地见下"新增"/
"生命周期与事件顺序"/"迁移说明"三节；核心侧与引擎侧改动、`core` 六工程 2533/2533 与 Unity
EditMode 60/60、PlayMode 263/263 验收过程中另发现并根治两处真实缺陷（非本次新增功能，见"修复"
一节）。均属核心载体层/引擎适配层能力补齐，无数据表字段删改，无存档格式变更。

### 新增

- **`Core.Carriers.Unit.MovementHost.Stop(Id unitId)`**（需求 1 落地）：经 `IWorldSim.SubmitIntent`
  提交一条 `Kind == "move_stop"` 的意图，下一次移动与导航阶段生效；不检查 `MovementLocked`/
  `NoMove`（停止不受控制效果限制）；同一 tick 内幂等（重复 `Stop` 只触发一次 `OnMoveStopped`）。
- **`Core.Carriers.Unit.MovementHost.OnMoveStopped`**（需求 1 落地）：
  `delegate void MoveStoppedHandler(Id unitId, Vec2 position, MoveStopReason reason);`，
  `enum MoveStopReason { Requested, PathFailed, BlockingChanged, Replaced }`——分别对应主动
  `Stop`、按 `PathFailurePolicy.Stop` 因寻路/重算失败停止、按 `BlockingChangePolicy.Stop` 因阻挡
  变化停止、新 `Request` 替换仍存在的旧路径。
- **`Core.Carriers.Unit.MovementHost.OnMoveFailedDetailed`**（需求 4 落地）：
  `delegate void MoveFailedDetailedHandler(Id unitId, Vec2 from, Vec2 to, MoveFailReason reason);`，
  `enum MoveFailReason { NoPath, BlockingChanged }`；与既有 `OnMoveFailed`（签名/触发时机不变）在
  同一失败点同时触发，是补充而非替代。
- **`Core.Carriers.Unit.MovementOptions.PathFailurePolicy`**（需求 4 落地，新增顶层枚举
  `{ KeepOldPath（默认）, Stop }` + 同名属性）：寻路失败或阻挡重算失败时的公共处理策略。
- **`Core.Carriers.Unit.MovementOptions.BlockingChangePolicy`**（需求 5 落地，新增顶层枚举
  `{ Replan（默认）, Revalidate, Stop, Ignore }` + 同名属性）：导航阻挡发生变化后，对单位已持有
  的现存路径的处理策略。
- **`Core.Carriers.Unit.MovementState.NavVersion`**（需求 5 落地，`int`，构造函数追加末位可选
  参数 `navVersion = 0`，源码兼容）：记录该单位当前路径建立时所依据的导航阻挡版本号，供阻挡重验
  比对。
- **`Core.Foundation.EngineAdapter.INavigation2D.GetBlockingVersion(Id mapId) => 0`**（需求 5
  落地，默认接口成员）：每次 `SetBlocking`/`Clear`/`BuildNavMesh` 使某地图可行走判定结果变化时
  递增（各 `mapId` 独立计数）；返回 0 表示不支持版本追踪，调用方对 0 视为"不做自动重验"——未
  重写本成员的既有实现（含未来新增的引擎适配层实现）源码/二进制兼容，行为等价于"不支持版本
  追踪"。`Adapters.Stub.StubNavigation2D`、Unity `UnityNavigation2D` 均已实现真实的按地图计数。
- **`INavigation2D.FindPath` 端点契约精确化**（需求 2 落地，契约文档 + `StubNavigation2D`/
  `UnityNavigation2D` 网格实现同步）：`from`/`to` 任一不可行走返回 `null`（优先于零长度判断）；
  `|from-to| <= 1e-6` 返回单元素路径 `[from]`；成功路径 `path[0]` 精确等于 `from`、
  `path[^1]` 精确等于 `to`（网格路径与精确端点的"接合段"复用与 `Raycast` 相同的判定，接合失败
  退化到相邻可行走格或返回 `null`）。
- **`Raycast`/`FindPath` 统一可通行规则**（需求 3 落地，契约文档 + `StubNavigation2D`/
  `UnityNavigation2D` 同步）：线段与阻挡区域**内部**相交才受阻，仅边界/角点相切不算受阻；
  `FindPath` 返回路径的每一段 `Raycast` 必为 `null`；网格实现对角相邻格仅当两个正交邻居都可
  行走时才允许联通（禁止切角）。
- **`adapters/conformance` 一致性场景**：`Navigation2DScenarios` 新增 5 个场景（端点精确接合、
  零长度路径、不可行走端点返回 null、双矩形拐角每段 `Raycast` 不受阻、`GetBlockingVersion`
  随三类变更操作递增——该场景对返回恒为 0 的实现走 `assert.Skip`，视为合法退化），
  `INavigation2D` 场景数由 4 增至 9，跨桩实现与 Unity 实现同源驱动。

### 生命周期与事件顺序

`Core.Carriers.Unit.MovementTickHandler.Execute` 按固定四步推进（原三步基础上插入阻挡重验一
步，未改变既有推进逻辑本身）：

1. **停止**：处理本 tick 全部 `move_stop` 意图——清空 `CurrentPath`、状态收回 `Idle`，丢弃同一
   tick 内在它之前提交的该单位 `move` 意图（之后提交的照常生效）；确有路径或被丢弃的意图时触发
   `OnMoveStopped(Requested)` 一次，否则静默（幂等）。
2. **移动**：处理存活的 `move` 意图——零长度目标不建路径、不动、不回调；寻路失败触发
   `OnMoveFailed` + `OnMoveFailedDetailed(NoPath)`，按 `PathFailurePolicy` 处理；新路径替换仍
   存在的旧路径时触发 `OnMoveStopped(Replaced)`，随后立即推进本 tick 位移。
3. **阻挡重验**（仅未被前两步处理、仍持有路径的单位）：比较 `GetBlockingVersion` 与
   `MovementState.NavVersion`，不同则按 `BlockingChangePolicy` 处理（`Replan` 直接重算；
   `Revalidate` 先逐段 `Raycast`、受阻再委托重算；`Stop` 直接清空并触发
   `OnMoveStopped(BlockingChanged)`；`Ignore` 不处理）；重算/重验失败触发
   `OnMoveFailed` + `OnMoveFailedDetailed(BlockingChanged)`，再按 `PathFailurePolicy`（`Stop`
   分支触发 `OnMoveStopped(PathFailed)`，与"直接因阻挡变化而停止"的 `BlockingChanged` 原因区分
   开）。
4. **推进**：`ContinuePathCore`（逻辑不变）。

重入安全：`Stop`/`Request` 都只是 `SubmitIntent`（下一 tick 生效），回调内同步调用二者不会在本次
`Execute` 内递归触发新的失败/停止回调。

### 修复

- **`ReplanPath` 重算失败时未推进 `NavVersion`，导致同一次阻挡变化在后续每个 tick 都重复触发一
  次 `OnMoveFailedDetailed`**：默认 `PathFailurePolicy.KeepOldPath`（旧路径原样保留）分支下，
  `ReplanPath` 重算失败只触发了失败回调，没有把 `MovementState.NavVersion` 前移到本次读到的
  `currentVersion`，下一个 tick 阻挡重验比较仍判定"版本已变化"，对同一次阻挡变化重新调用一次
  `ReplanPath`——再次失败、再次回调，此后每个 tick 都重复，直到阻挡状况本身改变。改为该分支下
  显式把 `NavVersion` 前移到 `currentVersion`（语义与"未受阻分支只更新版本号"一致，标记"已经按
  这个版本处理过，虽然重算失败"）；`Stop` 分支路径已被清空，不受影响。新增回归测试
  `MovementTickHandlerTests.BlockingChangePolicy_ReplanFails_DefaultKeepOldPathPolicy_
  DoesNotRepeatFailureEachTick`（默认策略下重算失败后再跑 10 个 tick，失败计数仍为 1，修复前会
  变成 11）。
- **Unity PlayMode 新增测试夹具耗尽跨批次共享的存档槽配额，连带导致同一批次里无关用例静默失败**：
  `SaveSystemOptions.MaxSlots`（默认 20）跨整个 `-runTests` 单次批处理进程共享、只增不减；新增的
  `MovementStopAndBlockingPlayModeTests` 最初每条用例各建一个独一无二的新槽，把既有用例累计已接近
  上限的运行推过 20，导致按夹具名排在更后面的 `VerticalSliceTests` 5 条用例在尝试新建槽时命中
  `SaveFailureReason.SlotLimitReached`，`ShellHost.NewGame` 因 `_saveSystem.Save(...).Success` 为
  `false` 直接 `return false`，不会走到 `_sceneRouter.LoadScene`（单独跑各夹具都各自全绿，只有混
  在完整套件里跑才复现，与仓库既有 `GlobalPlayModeTestSetup.cs` 描述的历史根因同一模式）。改为
  本套件全体用例改用同一个共享存档槽 id——第一次调用消耗 1 份新建配额，此后每条用例的 `NewGame`
  对同一个已存在的槽只是覆盖重写，不再消耗新配额；`PlayModeIsolation.TearDownAfterTest` 已在每条
  用例结束时 `World.ClearAll`，复用同一槽 id 不影响各用例世界状态隔离。

### 迁移说明

- **默认口味下行为逐位一致**：`PathFailurePolicy.KeepOldPath` + `BlockingChangePolicy.Replan`
  是默认值，且未显式实现 `GetBlockingVersion` 的导航实现恒返回 0（阻挡重验整体不生效）——升级
  前后在默认配置下行为逐位一致，回归测试覆盖全部既有用例（`core` 六工程与 Unity EditMode/
  PlayMode 既有用例原样通过，未修改任何既有断言）。
- **既有 `OnMoveFailed` 保留，不是替代关系**：签名与触发时机均不变，新增的 `OnMoveFailedDetailed`
  在同一失败点额外触发，只订阅旧事件的调用方无需任何改动。
- **自定义 `INavigation2D` 实现若要启用自动阻挡重验，需要显式实现 `GetBlockingVersion`**：该成员
  是默认接口方法，不实现不影响编译，但也不会得到自动重验能力（等价于"未支持"，不是"选择
  `BlockingChangePolicy.Ignore`"）——需要在自身的 `SetBlocking`/`Clear`/`BuildNavMesh` 落地方法
  内对相应 `mapId` 递增一个私有版本号计数器并在本方法中返回。
- **`FindPath` 端点精确化对依赖"格子中心"输出的调用方有影响**：升级前部分网格实现可能返回贴近
  网格中心而非精确等于传入 `from`/`to` 的路径端点；升级后 `path[0]`/`path[^1]` 精确等于调用方
  传入的浮点坐标。若调用方此前对首/末点做过"对齐到格子中心"之类的后处理补偿，该后处理现在是
  多余的（不会再有偏差需要补），可以安全移除，不移除也不会出错（幂等对齐同一点）。
- **`Raycast`/`FindPath` 边界相切语义修正（`StubNavigation2D.ClipAxis` 由闭区间改为开区间）**：
  升级前贴边/擦角（线段与阻挡矩形边界或角点相切、不进入内部）会被判定为"受阻"；升级后改为"内部
  相交才受阻，边界/角点相切不算受阻"，与"网格路径的每一段 `Raycast` 必为 `null`"这一契约保持
  一致（此前贴边场景下二者可能矛盾）。依赖旧行为（把贴边当受阻）的调用方需要重新评估——
  `IsWalkable`（点包含判定）未改动，仍是闭区间，本次统一可通行规则的范围限定于线段判定
  （`Raycast`/`FindPath`），不涉及单点判定。
- **`games/_template`/`architecture/13` 未新增对应口味配置项**：`GameOptions.BuildMovementOptions()`
  目前只接了 `DiscreteTurnEquivalentSeconds`/`UnitBlocking` 两项，未暴露
  `PathFailurePolicy`/`BlockingChangePolicy` 作为口味配置项；两个策略经 `MovementOptions` 在
  装配根（`GameBootstrap`/组合根构造 `MovementOptions` 处）直接配置，默认值即为框架推荐值，游戏
  层如需覆盖自行在装配根按需传入，不是缺失能力。

### 版本判据说明

- MINOR：`MovementHost.Stop`/`OnMoveStopped`/`OnMoveFailedDetailed` 均为新增公开成员；
  `MovementOptions`/`MovementState` 均只新增属性/带默认值的可选构造参数；
  `INavigation2D.GetBlockingVersion` 是默认接口方法；`FindPath`/`Raycast` 的契约精确化与边界
  语义修正均不改变方法签名，且默认口味 + `GetBlockingVersion` 恒为 0 时行为与升级前逐位一致。
  无删改既有公开签名，无存档格式变更。

## [1.9.0] - 2026-09-09

第十三方深度审核（codex 第十一轮，基线 `e070e3f`，即 1.8.0 发布提交）3 项确认缺陷
（CORE-180-01～03）+ 1 项候选转已确认（CORE-180-CAND-01）+ 1 项表现层候选转已确认（PRES-180）逐条
核实并根治，另处理 5 项文档漂移/工程证据勘误。逐条核实表、文档更新与能力分类处理、验收结果见
[audit-e070e3f-20260908/followup-2026-09-08g.md](architecture/落地计划/audit-e070e3f-20260908/followup-2026-09-08g.md)。
均属核心存档/规则层缺陷修复与表现层读档对账补强，无数据表字段删改，无存档格式变更。

### 新增

- **`Core.Foundation.SaveSystem.IDerivedStateRebuilder`**（新增契约，`BeforeLoad()`/
  `OnSectionLoaded(string sectionKey)`，CORE-180-01 根治）：可选注入到 `SaveSystem` 的回调，完全
  绕开事件总线（不产生任何可观察事件，不受 `SuppressDispatch` 影响）；`SaveSystem.Load` 在逐段读档
  前调用一次 `BeforeLoad()`，每段成功 `Load()` 后立即调用一次 `OnSectionLoaded(sectionKey)`，供
  装配根在读档期间就地重算评级换算属性、资源池上限等派生缓存，不等读档全部完成、不依赖事件重放。
- **`Core.Foundation.SaveSystem.ISaveSystem.SetDerivedStateRebuilder`**（默认接口方法，默认为
  no-op，CORE-180-01 根治）：注入上述重建器；自定义 `ISaveSystem` 实现方无需新增任何代码即可编译
  通过。
- **`Core.Rules.Assembly.RulesAssembly.ReloadArchetypeAndRace`**（新增公开方法，CORE-180-03 +
  CORE-180-CAND-01 根治）：同图读档后重新聚合单位的种族属性修正/被动光环与职业基础属性，不重复
  调用 `PowerHost.RegisterUnit`（避免对已注册单位抛"不能重复注册"异常）；种族切换时精确移除旧种族
  来源的属性修正、按引用计数递减释放旧种族被动光环，覆盖写入职业基础属性。
- **`Presentation.Common.ISimSnapshot.GetAllEntityIds`/`GetRawKind`**（新增只读成员，均为默认接口
  方法，PRES-180 根治，2026-09-09 版本判据勘误后改写）：`GetAllEntityIds()` 返回当前存活实体 id
  全量列表（默认返回空集合），`GetRawKind(Id)` 返回映射前的原始 `Entity.Kind` 字符串（默认返回
  `null`）；供 `ViewBinder` 在 `save.loaded` 后与已绑定 View 表做全量对账。唯一生产实现
  `WorldSimSnapshot` 已同步覆盖为真实实现；自定义 `ISimSnapshot` 实现方无需新增任何代码即可编译
  通过——未覆盖这两个成员时 `ViewBinder.OnSaveLoaded` 的对账安全退化为"只销毁已不存在实体的
  View，跳过按存活实体补建 View"，详见下"迁移说明"与 `ISimSnapshot`/`ViewBinder.OnSaveLoaded`
  源码判断记录。
- **`ViewBinder` 读档后视图对账**（PRES-180 根治）：构造函数新增订阅 `save.loaded`
  （`SaveEventKeys.SaveLoaded`），收到后立即（同步，不等下一次 `sim.tick_finished`）做一次双向全量
  对账——`ISimSnapshot.Exists` 为假但仍持有绑定的按 `OnEntityDestroyed` 销毁，`GetAllEntityIds()`
  中存在但未绑定的按 `OnEntityCreated` 补建，补齐"读档期间 `SuppressDispatch` 抑制丢弃
  `entity.created`/`entity.destroyed`导致 View 与逻辑实体不一致"这一缺口，幂等（重复读档不重复
  创建/销毁）。

### 修复

- **成功读档后评级换算属性/资源池上限未重算（CORE-180-01，P1）**：`SaveSystem.Load` 整段包在
  `IEventBus.SuppressDispatch` 抑制作用域内，`stat.changed`/`PowerChanged` 等事件被抑制丢弃，导致
  依赖这些事件重算的评级换算属性、资源池上限恢复到读档前的旧值，即便等级/装备等原始字段已正确
  恢复。`player.vitals` 段按"存档值与当前值差额"调用 `ModifyPower` 时若上限仍是旧值，差额被 clamp
  到旧上限，`RecomputeMax` 只在 `Current > newMax` 时下调、不会在 `newMax` 变大时补回 `Current`，
  当前值即便后续再重算上限也不会跟着回升。改为经 `IDerivedStateRebuilder.OnSectionLoaded
  (PlayerEquipment)` 在读到 `player.vitals` 段之前完成一次重算，完全绕开事件总线，不影响既有"读档
  期间业务事件零泄漏"回归。
- **后段读档失败时逆序回滚顺序与依赖方向相反（CORE-180-02，P2）**：`RollbackLoadedSections` 此前
  从后往前遍历，装备重新装备（复用真实 `EquipmentHost.Equip`，内部校验等级需求）先于等级本身被
  回滚恢复，导致等级需求校验用的是本次失败读档写入的低等级值，装备重新装备失败、物品被迫留在
  背包。改为与正常读档同一顺序（从前往后）遍历，回滚本质上变成"再做一次读档，只是文档换成读档前
  的快照"，不需要为回滚单独维护一套依赖顺序规则。
- **同图读档只切换种族/职业字段，未重新聚合对应的属性修正与光环（CORE-180-03 + CAND-01，P2）**：
  `GameplayAssembly.RestoreFromSlot` 判定目标地图与当前地图相同时不触发 `EnterMap`，此前读到
  `player.race_id`/`player.archetype` 段只覆盖字段本身，不会重新聚合旧种族属性修正的移除、新种族
  属性修正/光环的应用，也不会覆盖写入新职业的基础属性。改为经 `IDerivedStateRebuilder.
  OnSectionLoaded(PlayerRaceId)` 触发 `RulesAssembly.ReloadArchetypeAndRace`，同图与跨图路径统一
  覆盖。已知收边范围：新旧职业基础属性键集合不同、或 `power_types` 集合不同时的联动不在本次范围内
  （详见 `RulesAssembly.ReloadArchetypeAndRace` 源码注释与 followup 文档判断记录）。
- **同图读档期间被抑制丢弃的 `entity.created`/`entity.destroyed` 导致 View 与逻辑实体不同步
  （PRES-180，候选转已确认）**：`SaveSystem.Load` 抑制作用域内若某段 `Load`（如
  `DroppedLootPersistable.Load`）往 `WorldSim` 加实体，产生的 `entity.created` 永久丢失，逻辑实体
  已恢复但对应 View 未绑定；`ViewBinder` 此前只在构造期订阅事件，没有读档完成后的补扫入口。改为
  订阅 `save.loaded` 并做全量对账（见上"新增"一节）。

### 迁移说明

- **自定义装配根需注册派生状态重建器**（CORE-180-01/03）：若具体游戏/适配层提供了自定义
  `ISaveSystem`/装配根替代框架默认的 `GameplayAssembly`，且存在依赖事件重算的派生缓存（评级换算
  属性、资源池上限、种族/职业属性修正与光环等），需要实现 `IDerivedStateRebuilder` 并调用
  `ISaveSystem.SetDerivedStateRebuilder` 注册，否则读档后这些派生缓存不会重算，行为等同于本次修复
  之前的框架默认实现。不注册不影响编译（`SetDerivedStateRebuilder` 是默认接口方法），只影响读档后
  派生缓存的正确性。
- **自定义 `ISimSnapshot` 实现可选覆盖两个新成员**（PRES-180，2026-09-09 版本判据勘误后改写）：
  `GetAllEntityIds`/`GetRawKind` 是 C#8 默认接口方法（默认分别返回空集合/`null`），与本版本其它
  新增接口成员同一惯例——任何自定义 `ISimSnapshot` 实现无需新增任何代码即可继续编译通过。
  `GetAllEntityIds()` 语义为"当前存活实体 id 全量列表"，`GetRawKind(Id)` 语义为"映射前的原始
  `Entity.Kind` 字符串，实体不存在返回 null"，均为只读查询、不持有 `Entity` 引用本身（铁律 P1）；
  框架内唯一生产实现 `WorldSimSnapshot` 已同步覆盖为真实实现。若不覆盖，`ViewBinder` 的
  `save.loaded` 全量对账会安全退化：销毁"绑定表里指向已不存在实体"的陈旧 View 这一半不受影响
  （只依赖原有强制成员 `Exists`），"按当前存活实体补建 View"这一半因 `GetAllEntityIds()` 返回
  空集合而天然是空操作——即读档期间被 `SuppressDispatch` 抑制丢弃的 `entity.created` 不会被这类
  自定义实现补扫到，需要完整对账能力的自定义 `ISimSnapshot` 实现方应显式覆盖这两个成员。
- **自定义 `IViewFactory` 实现须保证创建幂等**（PRES-180）：`ViewBinder` 的读档后对账在
  `GetAllEntityIds()` 中存在但未绑定的实体上复用既有 `OnEntityCreated` 路径调用
  `IViewFactory.Create`；若某具体游戏/适配层的 `IViewFactory` 实现对同一实体重复调用 `Create` 会
  产生副作用（如资源重复分配），需要确认其自身具备"同一实体已存在 View 时安全跳过或替换"的幂等
  处理——框架侧 `ViewBinder` 已经用绑定表去重、不会对同一实体重复调用 `Create`，此处针对的是
  `IViewFactory` 实现自身在异常路径下被多次调用时的健壮性。
- **版本判据说明**：本次为 MINOR（`1.8.0` → `1.9.0`）。"新增"一节列出的成员均为新增（新增
  类型/默认接口方法/公开方法/接口成员），无删改既有公开签名；`ISimSnapshot` 新增的
  `GetAllEntityIds`/`GetRawKind` 两个成员（2026-09-09 版本判据勘误后改为）同样是默认接口方法，
  第三方 `ISimSnapshot` 实现方无需新增代码即可继续编译，不构成源码级破坏，按 MINOR 处理不需要
  任何例外说明。（此前一版曾把这两个成员定义为普通接口方法并按"源码级破坏但按 MINOR 处理"的
  例外记录在案，属于版本判据误判——已改为默认接口方法根治，不再需要该例外，具体见上方"新增"/
  "迁移说明"两处改写内容。）

## [1.8.0] - 2026-09-08

第十二方深度审核（codex 第十轮，基线 `8160178`，即本版本发布前的最新提交）4 项发现（CORE-170-01～03、
PRES-170-01）逐条核实并根治，CORE-170-03(a) 审查全仓 `IPersistable` 实现后另发现 8 处同类"先改状态
后校验/边解析边提交"缺陷一并根治。逐条核实表、文档更新与能力分类处理、验收结果见
[audit-8160178-20260908/followup-2026-09-08f.md](architecture/落地计划/audit-8160178-20260908/followup-2026-09-08f.md)。
均属核心规则/存档/玩法层缺陷修复与表现层/引擎适配层缺陷修复，无数据表字段删改，无存档格式变更。

### 新增

- **`Core.Rules.Common.AuraHandleLedger`**（跨来源光环句柄账本，CORE-170-01 根治）：把装备/套装门槛
  加成原有的"跨来源引用计数账本"上移为独立类型，由 `RulesAssembly` 持有单一实例，`CarriersAssembly`
  注入给 `EquipmentHost`，`RulesAssembly` 自身（种族被动）也用同一实例，装备/套装/种族三类来源共享
  同一条账本，互不覆盖对方持有的引用。
- **`Core.Rules.Common.IAuraQuery.TryGetInstanceRef`**（默认接口方法，默认返回 `null`，CORE-170-01
  根治）：按单位与光环定义查询该单位当前是否持有一份有效引用；自定义 `IAuraQuery` 实现方无需新增
  任何代码即可编译通过，默认实现对不参与跨来源账本的测试假实现是安全的等价空实现。
- **`Core.Numbers.Progression.LevelSync`**（委托类型）与 **`Core.Carriers.Unit.WorldUnitAccess.
  SetLevel`**（CORE-170-02 根治）：`ProgressionHost` 在 `RegisterUnit`/`AddXp`/`RestoreState` 三个
  等级确立/变化的时机调用该委托，`CarriersAssembly` 接到 `WorldUnitAccess.SetLevel`（不进
  `IUnitAccess` 接口，仅供组装期委托闭包使用），确立 Progression 为单位等级唯一权威并同步实体
  字段与查询结果。
- **`Core.Foundation.EventBus.IEventBus.SuppressDispatch`**（默认接口方法，默认返回一个 no-op
  `IDisposable`，CORE-170-03(b) 根治）：调用方在 `using` 作用域内产生的领域事件（`Enqueue`/
  `PublishImmediate`）直接丢弃，不派发给订阅者；`SaveSystem.Load` 用它把逐段读档 + 失败回滚整体
  包进抑制作用域，避免回滚期间重放的领域事件被业务消费者（如 `AchievementHost`）误计数。自定义
  `IEventBus` 实现方无需新增任何代码即可编译通过。
- **`Core.Foundation.SaveSystem.SaveSections.KnownOrder` 纳入 `world.gobj_pending_loot`**：此前该
  段未登记进 `KnownOrder`，落入"自定义段"分支按 key 序数排序；现按 10 号文档"7a.
  .../spawn_state/gobj_pending_loot"既有文字顺序固定登记，只影响读档时的段处理顺序，不改变段的
  存在性或字段形状。

### 修复

- **跨图重放后卸装误删种族 aura（CORE-170-01，P2）**：装备/套装/种族共享同一 `auraDef` 时，种族
  被动此前只按 `HasAura` 判断是否需要重放，没有独立的来源账本；卸下装备会连带清除种族来源的同一份
  光环及其属性修正。改为三类来源各自维护"我持有哪个句柄"的簿记，互不代劳。
- **Progression 等级与实体等级分叉（CORE-170-02，P2）**：`ProgressionHost.AddXp`/`RestoreState`
  更新内部等级后未同步 `PlayerUnit.Level`，导致 `WorldUnitAccess.GetLevel` 与规则层查询到的等级
  不一致，等级需求装备可能因此误判 `RequirementNotMet`。改为写入时同步，Progression 是唯一权威。
- **存档失败段自身不回滚，且回滚期间产生的领域事件污染业务消费者（CORE-170-03，P2，两个已确认
  表现）**：(a) `EquipmentPersistable.Load` 及审查全仓 `IPersistable` 实现后另发现的 8 处同类
  缺陷（`AchievementHost`/`SpawnHost`/`WorldState`/`DifficultyHost`/`CurrencyPersistable`/
  `VendorStockPersistable`/`RngStreamsPersistable`/`SkillBindingPersistable`）此前均"先改变运行期
  状态、后校验数据形状"或"边解析边直接调用 live host 写方法"，坏存档会在状态已被部分或全部改动后
  才抛异常，且 `SaveSystem.Load` 此前只把"此前已成功加载"的段纳入回滚列表，抛异常的段自身不在
  其中；现全部改为"先解析校验成临时恢复计划、再一次性提交"，`SaveSystem.Load` 额外把失败段自身
  纳入回滚列表兜底。(b) `SaveSystem.Load` 逆序回滚时会调用 live host 的真实写方法（如
  `EquipmentPersistable.Load` 复用真实 Equip/Unequip 逻辑），产生的真实领域事件被业务消费者
  （`AchievementHost`）当成真实玩家操作再次计数，导致成就进度被错误推高甚至误解锁；现读档与回滚
  期间整体抑制领域事件派发，`SaveMigratedEvent`/`SaveLoadedEvent` 仍在读档完成后正常派发。
- **共享 `AnimationClip` 被空事件配置和跨 factory 状态污染（PRES-170-01，P2）**：`UnityViewFactory.
  RegisterModelClipEvents` 此前对空 `events` 配置直接跳过、不建立任何基线或隔离；首个非空配置从
  当前共享剪辑资产捕获"pristine"快照后把合并结果写回该**共享**资产；承载基线/签名/覆盖状态的三张
  表此前是 factory 实例字段。三者叠加导致同一 factory 内空配置 anim_set 会看到另一个非空配置写入
  的数据事件，新建 factory（典型触发：场景重进）会把旧 factory 写入共享资产的事件误当成美术自带
  基线保留。现改为进程级静态表缓存美术自带基线，任何非空配置都以基线为底合并出一份运行期私有
  副本，只经 `AnimatorOverrideController` 套用到具体 `ModelHandle` 实例，共享剪辑资产自始至终
  不被写入。

### 迁移说明

- **单位等级唯一权威改为 `Core.Numbers.Progression.ProgressionHost`**（CORE-170-02）：直接改写
  `PlayerUnit.Level` 字段而不经 `ProgressionHost.RegisterUnit`/`AddXp`/`RestoreState` 的具体游戏
  代码，其改动会在下一次上述三个方法被调用时被 `LevelSync` 覆盖同步；需要设置单位等级的具体游戏
  代码应统一改走 `ProgressionHost` 相应方法，不要再直接写 `PlayerUnit.Level` 字段。
- **读档与回滚期间领域事件被抑制**（CORE-170-03(b)）：依赖"读档期间正常成功加载某段会让该段产生
  的事件到达外部订阅者"这一行为的具体游戏代码（例如监听 `ItemEquipped` 来更新 UI）需要改为在
  `SaveLoadedEvent`（读档完成后正常派发）到达后按当前状态重建一次，不能再假设读档过程中会收到
  逐段变化事件；`SaveMigratedEvent`/`SaveLoadedEvent` 本身不受影响，仍会正常派发。
- **自定义 `IPersistable` 实现须遵循"先解析校验，再一次性提交"**（CORE-170-03(a)）：`IPersistable.
  Load` 契约注释已更新为要求实现方在触碰任何运行期状态之前完整校验数据形状，只有整份数据校验
  通过才提交；`SaveSystem.Load` 新增的失败段自身回滚只对遵循该约定的实现是安全的幂等 no-op，不
  遵循该约定的自定义实现在读档失败时仍可能残留部分改动的状态，建议对照框架自身 8 处修复的模式
  （见上"修复"一节）同步改造。
- **自定义 `IEventBus`/`IAuraQuery` 实现的新增成员**（CORE-170-03(b)、CORE-170-01）：
  `IEventBus.SuppressDispatch`/`IAuraQuery.TryGetInstanceRef` 均为 C#8 默认接口方法，自定义实现
  方不重写这两个成员即自动获得默认行为（分别为 no-op 抑制作用域、返回 `null`），不需要任何代码
  改动即可继续编译通过；如果自定义 `IEventBus` 实现有自己的事件派发路径且希望"读档抑制"语义生效，
  需要显式实现 `SuppressDispatch` 并让派发路径检查抑制状态。
- **版本判据说明**：本次为 MINOR（`1.7.0` → `1.8.0`）。"新增"一节列出的全部成员均为新增（新增
  类型/默认接口方法/委托/公开方法/`KnownOrder` 登记项），无删改既有公开签名；构造函数新增参数均为
  可选参数且默认值保持既有行为；`SaveSystem.Load` 失败段自身回滚、读档期间事件抑制、单位等级权威
  改为 Progression 是行为契约变更但不改变任何公开类型签名，不构成 MAJOR。

## [1.7.0] - 2026-09-08

第十一方深度审核（codex 第九轮，基线 `85f1f4f`，即本版本发布前的最新提交）10 项发现（AUD-01～05、
种族被动光环跨图、owner/day/vendor 装配扩展点、动画剪辑事件登记契约差异、工具链两条）逐条核实并
根治。逐条核实表、文档更新与边界清单处理、验收结果见
[audit-85f1f4f-20260908/followup-2026-09-08e.md](architecture/落地计划/audit-85f1f4f-20260908/followup-2026-09-08e.md)。
均属核心存档/规则/玩法层缺陷修复、引擎适配层资源合同修复、工具链健壮性加固与文档口径统一；
存档格式变更见下"迁移说明"。

### 新增

- **`Core.Foundation.SaveSystem.SaveSystem.Load` 新增按逆序回滚已成功加载段的机制**（AUD-01
  根治）：某个已注册段 `Load()` 抛异常时，对此前已成功 `Load()` 的段按逆序重新 `Load` 读档前
  快照，尽力恢复到读档前状态；最终 `LoadStatus` 仍是 `PersistableThrew`，回滚不改变这一结果；
  回滚自身失败也只记诊断，继续处理其它段。
- **6 处既有 `IPersistable` 实现补齐缺段清空覆盖面**（AUD-02 根治）：`ItemPersistable.
  InventoryPersistable`/`SkillBindingPersistable`/`VendorStockPersistable`/`DroppedLootPersistable`/
  `PlayerVitalsPersistable`/`ProgressionPersistable` 六处此前缺段（`JsonNull`）时直接返回、不清空
  既有运行期状态，现均已补齐清空逻辑；`UnitPersistable`（`CurrentMapId`/`ArchetypeId`/
  `CurrentPosition` 三段）与 `RngStreamsPersistable` 显式声明 1.6.0 新增的
  `IPersistable.KeepStateWhenSectionMissing => true` 例外并写明理由。
- **`Core.Gameplay.Economy.EconomyHost.SetStock` 新增可选参数 `timerRemaining`；新增
  `GetStockTimerRemaining`**（AUD-03 根治）：商人补货倒计时剩余时间可持久化，原地读档不再继承
  旧计时器；`world.vendor_stock` 段内 `timer` 策略物品条目形状扩展为可选对象
  `{remaining, timer_remaining}`，向后兼容纯数字旧格式。
- **`Core.Carriers.Gobj.GameObjectHost.PendingChestLootSnapshot`/`RestorePendingChestLoot`
  过时别名**（AUD-04 根治）：`[Obsolete]` 转发到 1.6.0 改名后的 `PendingLootSnapshot`/
  `RestorePendingLoot`，1.5.0 风格调用点本版本仍可编译通过（带过时警告），计划下一个 MINOR 版本
  随该窗口期结束一并移除。
- **`Core.Carriers.Unit.PlayerUnit.RaceId`/`Core.Carriers.Unit.UnitPersistable.RaceId`**（新增
  可选存档段 `player.race_id`）与 **`Core.Rules.Assembly.RulesAssembly.ReapplyRacePassiveAuras`**
  （种族被动光环跨图重放根治）：`GameplayAssembly.EnterMap` 已接入调用，此前 `World.ClearAll`
  后种族被动光环消失但种族属性修正不受影响，两者生命周期不一致的缺陷已根治。
- **`Core.Gameplay.Assembly.GameplayAssembly` 构造函数新增 `questOwnerResolver`/
  `questDayProvider`/`vendorOpenRequested` 三个可选参数**（owner/day/vendor 装配扩展点根治）：
  直接转发进内部装配的 `QuestHost`/`DialogHost`（此前恒传 `null`）；`games/_template.GameOptions`
  新增三个同名可选字段、两处引擎适配层随附的示例组合根新增三个同名可选公开属性，均已补齐透传，
  全部默认仍是 `null`，不改变未显式提供时的既有行为。
- **`Core.Foundation.EngineAdapter.IResourceLoader.ResourceKind` 新增 `AnimationClip`**（动画剪辑
  事件登记契约差异根治）：动画剪辑资源经加载器统一登记，`UnityViewFactory.RegisterModelClipEvents`
  不再整体覆盖美术自带事件，改为合并、并按 `anim_set` 的事件配置签名隔离生效范围。
- **`UnityResourceLoader.TryGetOrLoadSlotMesh`**（AUD-05 根治）：`display.equip_visual.mesh_ref`
  资源合同——预制体按槽位子对象/首个网格渲染组件提取网格，独立网格资产可直接使用；缺失/未加载时
  保留当前槽位网格不清空为不可见。
- **`toolchain/_hash.ps1`**（工具链健壮性加固）：共用哈希函数 `Get-Sha256FileHash`，
  `Get-FileHash` 可用时优先用，否则透明退化到 .NET SHA256 流式计算；`get_framework.ps1`/
  `sync_package_content.ps1`/`build.ps1` 三处哈希校验改用该函数。

### 修复

- **旧格式非空 `world.gobj_pending_loot` 读档抛异常且失败段不回滚（AUD-01，P1）**、**六处
  `IPersistable` 实现缺段不清空（AUD-02，P2）**、**商人补货计时器原地读档丢失（AUD-03，P2）**、
  **公开 API 改名无过时别名（AUD-04，P2）**、**样例 model 槽位换装把 prefab 引用当 Mesh 读取、
  槽位网格被清空（AUD-05，P2）**、**种族被动光环跨图重放丢失**、**owner/day/vendor 装配扩展点
  三处示例组合根未透传**、**动画剪辑事件登记整体覆盖美术自带事件、未按 anim_set 隔离**、
  **`toolchain` pytest 子进程按宿主默认编码解码可能崩溃**、**`get_framework.ps1` 等三处直接依赖
  `Get-FileHash` 无兜底**：10 项均为第十一方深度审核（codex 第九轮）发现，逐条根因、复现测试、
  修复位置、验收结果见上文引用的 followup 文档，不在此重复展开。
- **`toolchain/_hash.ps1` 缺少 UTF-8 BOM 导致在部分执行宿主上按系统默认代码页误读脚本源码、触发
  语法解析错误**：发布前 CI 复核发现（本机开发代码页下未复现，另一台系统默认代码页不同的宿主上
  可稳定复现），已补回 BOM（内容不变）并新增门禁测试
  `toolchain/tests/test_powershell_scripts_ansi_safe.py`，把"脚本含非 ASCII 字符必须带 BOM"
  固化为门禁校验项，见 `architecture/11_工程规范与测试.md` 第 8 节对应勘误行与
  `toolchain/README.md` 判断记录。

### 迁移说明

- **`world.gobj_pending_loot` 段读档兼容旧字段名**（AUD-01）：1.5.0 期间产生、字段名与当前版本
  不一致的旧 `pending` 记录，能按值映射的字段会被迁移，无法映射时安全丢弃单条（不影响其它条目或
  其它段），不再抛异常；`Save()` 输出格式不变，仍写 `originKey`。
- **`SaveSystem.Load` 段失败时对此前已成功加载的段按逆序尽力（best-effort）回滚**（AUD-01，行为
  契约变更）：`Load` 开始逐段读取前先对全部已注册段各取一份读档前状态快照；某段 `load()` 抛异常
  后，只对已经取得成功快照的此前成功段按逆序重新 `load(快照)`，尝试恢复到读档前状态；快照缺失、
  回滚自身再次失败、或段间存在联动时仍可能残留部分状态，不保证消除"部分加载"中间态；最终
  `LoadStatus` 枚举值不变，仍是 `PersistableThrew`。回滚不覆盖失败段自身，也不改变
  `PersistableThrew` 这一最终结果。准确合同以 `architecture/10_存档与持久化.md` 当前正文为准。
- **6 处既有 `IPersistable` 实现的缺段行为收紧**（AUD-02）：`ItemPersistable.InventoryPersistable`/
  `SkillBindingPersistable`/`VendorStockPersistable`/`DroppedLootPersistable`/
  `PlayerVitalsPersistable`/`ProgressionPersistable` 六处此前缺段时保留运行期状态不动，现改为清空
  到内容默认态；自行实现 `IPersistable` 且依赖"缺段时保留当前状态不动"这一行为的具体游戏代码，
  需要显式覆盖 1.6.0 新增的 `KeepStateWhenSectionMissing => true`（该新增接口默认方法本身不要求
  任何既有实现新增代码即可通过编译，本条只影响上述 6 处框架自身实现的默认行为）。
- **`world.vendor_stock` 段 timer 物品条目新增可选字段**（AUD-03）：`timer` 策略物品条目形状从
  纯数字变为可选对象 `{remaining, timer_remaining}`；`Load` 完全向后兼容纯数字旧格式，无需游戏侧
  改动。
- **`GameObjectHost.PendingChestLootSnapshot`/`RestorePendingChestLoot` 改名别名（AUD-04）**：本
  版本已补回旧名的 `[Obsolete]` 转发方法（行为与新名完全一致），1.5.0 风格调用点本版本仍可编译
  通过（带过时警告），计划下一个 MINOR 版本随该窗口期结束一并移除；升级到下一个 MINOR 版本前请把
  仍在用的旧名调用点改为新名。
- **`player.race_id` 新存档段**（种族被动光环跨图重放）：新增可选段，旧档缺失时清空为 `null`，
  不影响读档；调用方（游戏引导代码）需要在调用 `RulesAssembly.RegisterUnit` 传入非空 `raceId` 时
  同步写入 `PlayerUnit.RaceId`——框架不自动同步（分层边界，`RulesAssembly`/L2 不知道
  `PlayerUnit`/L3 类型存在）。
- **`display.equip_visual.mesh_ref` 资源合同首次明文**（AUD-05）：引用与 `model_ref` 同一条
  `model` 种类资源；预制体按槽位子对象/首个网格渲染组件提取，独立网格资产可直接使用；缺失/未加载
  须保留当前槽位网格，不得清空为不可见。字段数据形状（04 第 7.1.2 节字段表）不变。
- **`display.anim_set.clips[*].events` 的引擎侧登记行为契约首次明文**（动画剪辑事件登记）：必须
  与剪辑资产原有事件合并、不得整体覆盖；同一 `resource_ref` 被不同事件配置的 `display.anim_set`
  共同引用时必须按 anim_set 隔离生效范围。字段数据形状不变。
- **版本判据说明**：本次为 MINOR（`1.6.0` → `1.7.0`）。"新增"一节列出的全部成员均为新增（新增
  类型/字段/段/可选构造参数/可选公开属性/共用脚本函数），无删改既有公开签名；6 处 `IPersistable`
  实现的缺段行为收紧、AUD-01 的段失败回滚是行为契约变更但不改变任何公开类型签名，不构成 MAJOR。

## [1.6.0] - 2026-09-08

游戏侧复核 1.5.0 发现两处边界并根治：① 框架常驻壳（`FrameworkResidentHost`）命中帧同步开关此前是
私有编译期常量，是三处装配根（另两处 `GameFoundationBootstrap`/`games/_template.GameBootstrap`）
里唯一不支持"具体游戏配置开启"的一处，与包 README"命中帧同步接线步骤"一节对三处装配根一视同仁
的既有描述不符；② 命中帧同步端到端测试（`Tests/Runtime/HitFrameSyncEndToEndTests.cs`）只用一个
远大于默认超时（0.5s）的等待死线判断"VFX 是否终于入队"，未排除"命中帧链路完全断线、只是超时兜底
先一步释放，让断言恰好也通过"这一假通过可能性。均属表现层/引擎适配层缺陷修复与测试加固，无数据
表字段删改，无存档格式变更。

第十方深度审核（codex 第八轮，基线 `3224ca1`，即本版本发布前的最新提交）8 项发现（CR150-01～04、
PR150-01～03、PJ150-01）逐条核实并根治，另根治 1.5.0 遗留的一处已知局限（PR140-04 的"极短 GCD
内连续独立攻击被误合并为同一命中帧同步批次"，本版本补齐 `AttackInstanceId` 链路后消除）。逐条
核实表、上轮九项复核对照、文档处理清单见
[audit-3224ca1-20260908/followup-2026-09-08d.md](architecture/落地计划/audit-3224ca1-20260908/followup-2026-09-08d.md)。
均属核心规则/表现层/引擎适配层缺陷修复、下载脚本安全加固与文档口径统一，无数据表字段删改；
存档格式变更见下"迁移说明"。

### 新增

- **`Presentation.FeedbackBinder.Core.HitFrameSyncReleaseReason`**（枚举，`HitFrame`/`Timeout`）与
  只读诊断属性 `HitFrameSyncPolicy.LastReleaseReason`/`FeedbackBinder.LastHitFrameSyncReleaseReason`：
  命中帧同步等待队列最近一次批次释放究竟是命中帧事件真正到达，还是超时兜底，供测试与诊断直接断言
  区分两条释放路径，不再只能靠"某个副作用计数是否增加"间接判断。
- **`Core.Carriers.Gobj.GameObjectEntity.OriginKey`**（CR150-02，可空 `Id` 属性）：经
  `GameObjectFactory.Spawn` 按"地图+位置+模板"自动合成的稳定摆放位置键，跨运行期实体重建
  （地图卸载/重入分配新实体 id）保持不变；`Spawn` 新增同名可选参数，未显式传入时自动合成，既有
  调用方无需改动。
- **`Core.Foundation.SaveSystem.IPersistable.KeepStateWhenSectionMissing`**（CR150-03，默认接口
  方法，默认返回 `false`）：已注册的存档段在读取的文档里整段缺失时，`SaveSystem.Load` 默认仍会
  调用一次该段 `Load(JsonNull.Instance)`（清空既有运行期状态）；需要保留旧行为（缺段视为"不动
  当前状态"）的具体实现可显式覆盖为 `true`。纯加法，既有 `IPersistable` 实现无需改动即可编译。
- **`Core.Carriers.Gobj.GobjOptions.GatherNodeLootPolicy`**（CR150-04，`GobjLootDeliveryPolicy`
  枚举，默认 `Partial`）：独立于既有 `ChestLootPolicy` 的口味配置项，控制采集节点满包时的交付
  协议（`Reject` 整批回滚可立即重试；`Partial` 部分交付+余量记账）。
- **`Core.Rules.Common.EffectContext.AttackInstanceId`**（可空 `Id`，攻击实例 id 遗留根治）与
  `CombatDamageDealtEvent`/`CombatHealDoneEvent` 同名新字段：`CastPipeline.ExecuteEffectsOnly`
  每次调用固定分配一个全新实例 id，经 `Resolver` 转发到落地伤害/治疗事件，供 `FeedbackBinder`
  按值精确区分同一攻击者的多次独立攻击各自的命中帧同步批次，不再依赖"是否还有未释放批次"这一
  时序代理。均为新增可空字段/新增可选构造参数，默认 `null`，不改变既有调用点在缺省参数下的
  观测行为。
- **`Adapter.Unity.EngineAdapter.AnimStateFinishRelay`**（PR150-02，新增
  `StateMachineBehaviour` 子类，不属于 `IRenderer3D` 契约）：挂到具体引擎适配层动画状态机的
  目标状态上后，把动画系统同步触发的进入/退出事件转发回渲染器，作为既有"外部轮询采样"判断动画
  播放完成的并行判定路径（两者取 OR），修复自动过渡整个落在两次采样之间导致完成事件永久漏发的
  问题；未挂接时完全不影响既有行为，占位资产已随框架预置到五个内建状态。
- **`toolchain/get_framework.ps1` 下载脚本落点边界校验**（PJ150-01，安全加固）：新增锁文件
  `version` 字段格式校验（`-AllowVersionMismatch` 放行分支）与落地目录必须是 `-Target` 严格
  子目录的校验，恶意/畸形值直接 `throw` 不做任何写入/删除；`Expand-Archive` 改为逐条目手动解压
  并同样校验每个条目落点（顺带根治 zip slip）。

### 修复

- **`Adapter.Unity.Shell.FrameworkResidentHost` 命中帧同步开关改为口味配置**：私有编译期常量
  `HitFrameSyncEnabled` 改为与 `GameFoundationBootstrap` 同名同型的
  `[SerializeField] private bool _hitFrameSyncEnabled`——具体游戏可以像摆放 `GameFoundationBootstrap`
  一样在自己的场景里预先放置一个 `FrameworkResidentHost` 组件并在 Inspector 里开启该字段，
  `Ensure()` 会优先复用这个已配置好的实例（`FindFirstObjectByType` 既有查找逻辑）而不是另建一个
  默认值实例；默认仍是 `false`（`LogicDriven`），未预放置时行为与改动前完全一致。
- **命中帧同步端到端测试加固，排除超时兜底假通过**：`Tests/Runtime/HitFrameSyncEndToEndTests.cs`
  既有用例新增断言真实释放原因必须是 `HitFrame`；新增镜像反例
  `ModelAttacker_HitFrameSync_NeverFires_TimesOutWithTimeoutReason`（刻意不播放攻击动画，验证超时
  兜底确实只在命中帧从未到达时才触发、且诊断如实报告 `Timeout`）。
  `presentation/feedback_binder/tests/HitFrameSyncPolicyTests.cs`/`FeedbackBinderHitFrameSyncTests.cs`
  的等价单元测试同步补上释放原因断言，覆盖同一缺口的单元测试层面。
- **跨图重放共享光环误删（CR150-01，P2）**、**宝箱/采集节点余量按运行期实体 id 记账跨重建失联
  （CR150-02，P2）**、**旧档缺失可选段不清空当前台账（CR150-03，P2）**、**满包采集先提交冷却
  再忽略入包失败（CR150-04，P2）**、**新精灵视图创建早于绑定导致初始装备重放被过滤（PR150-01，
  P2）**、**动画完成检测依赖外部轮询采样、自动过渡落在两次采样之间会漏发完成事件（PR150-02，
  P2）**、**挂点缺失时不登记挂接意图、挂点补上后无法重放（PR150-03，P2）**、**下载脚本锁文件
  `version` 字段未经校验即拼入落地路径、可越界写删（PJ150-01，P0 安全）**：8 项均为第十方深度
  审核（codex 第八轮）发现，逐条根因、复现测试、修复位置、验收结果见上文引用的 followup 文档，
  不在此重复展开。

### 迁移说明

- **旧存档缺失可选段时默认清空该段运行期状态**（CR150-03，行为收紧）：`SaveSystem.Load` 此前对
  "已注册但当次读取的文档里整段缺失"的段直接跳过、不调用 `Load`；本版本改为默认仍调用一次
  `Load(JsonNull.Instance)`。已审计全部既有 `IPersistable` 实现（16 个），均已在 `Load` 开头
  显式处理 `JsonNull`（清空重置或无副作用 no-op），当前无一需要调整；自行实现 `IPersistable` 且
  依赖"旧档缺段时保留当前运行期状态不动"这一（未在架构文档承诺过的）旧行为的具体游戏代码，需要
  显式覆盖新增的默认接口方法 `KeepStateWhenSectionMissing => true`。该新增成员是默认接口实现，
  不要求任何既有实现新增代码即可通过编译。
- **`world.gobj_pending_loot` 段 JSON 字段名与值语义变更**（CR150-02，非公开承诺字段的一次性
  调整）：数组元素字段名 `gobjInstanceId`（运行期实体 id）改为 `originKey`（稳定摆放位置键）。
  本段是 1.5.0 新增的可选附加段，此前未在 `architecture/10_存档与持久化.md` 正式登记、未对外
  承诺过字段级兼容；产生于 1.5.0 期间、字段名与当前版本不一致的旧 `pending` 记录，读取时的实际
  处理口径（能按值映射的字段是否迁移、无法映射时如何安全丢弃、单段失败是否回滚）以
  `architecture/10_存档与持久化.md` 当前记载为准，不在本条目重复展开、也不承诺与早期草稿描述
  一致；建议对极少数处于这一窗口期、且确实存在未交付宝箱/采集节点掉落余量的存档，升级前先进入
  相关地图把余量交互清空一次，规避任何处理口径下都可能出现的"旧余量丢失"结果。
- **自定义 `IRenderer3D` 实现建议接入动画状态机的状态退出回调**（PR150-02，非强制）：本版本
  新增的 `AnimStateFinishRelay` 只是既有"外部轮询采样"判断动画完成的一个并行判定路径，不替代
  也不要求废弃采样路径；具体引擎适配层若已经能通过采样正确判断完成（未撞见"自动过渡整个落在两次
  采样之间"这一边界），无需任何改动。若自定义实现同样依赖外部轮询采样判断完成，建议参考本版本
  接入方式补一条由动画系统事件驱动的完成检测路径，避免同一类漏发问题。
- **版本判据说明**：本次为 MINOR（`1.5.0` → `1.6.0`）。"新增"一节列出的全部成员均为新增
  类型/新增可选构造参数/新增可空字段/新增默认接口方法，均不改变既有调用点在缺省参数下的观测
  行为，不删除、不改名任何已有公开签名，不破坏既有调用方编译；`world.gobj_pending_loot` 段的
  字段改名属于上述"非公开承诺字段"的例外说明，不计入 MAJOR 判据（该段本身从未进入过
  `SaveSections.KnownOrder`/架构文档正式登记）。

## [1.5.0] - 2026-09-08

第九方深度审核（codex 第七轮，基线 `c86bfa9`，即 1.4.0 自身）9 项发现（CR140-01～03、
PR140-01～04、PJ140-01～02）逐条核实并根治，另补齐审计未覆盖的两处遗留（宝箱 Partial 余量存读档
持久化、新 View 初始装备外观重放）。逐条核实表、旧 17 项复核对照、文档漂移处理见
[audit-c86bfa9-20260908/followup-2026-09-08c.md](architecture/落地计划/audit-c86bfa9-20260908/followup-2026-09-08c.md)。
均属核心规则/表现层/引擎适配层缺陷修复与能力补齐，无数据表字段删改，无存档格式不兼容变更（新增
`world.gobj_pending_loot` 段是可选附加段；缺段时的实际读取/清空语义以
`architecture/10_存档与持久化.md` 当前记载为准，不在本条目重复展开）。

### 新增

- **`Core.Carriers.Gobj.GobjLootDeliveryPolicy`**（枚举，`Reject`/`Partial`，CR140-01）：
  `GobjOptions.ChestLootPolicy`（默认 `Partial`）决定 `chest` 一次性开箱的交付协议——`Reject`
  经 `IBatchableInventoryHost` 事务整批交付，任一堆放不下即整体回滚，不标记 `open_state`；
  `Partial` 逐堆按实际落地量交付，未交付部分记入进程内台账供下次交互补发。
- **`Core.Carriers.Gobj.GameObjectHost.PendingChestLootSnapshot`/`RestorePendingChestLoot`**
  + 新增 `GobjPendingLootPersistable`（CR140-01 存读档收口）：`Partial` 策略下未交付的宝箱余量
  补齐可选存档段 `world.gobj_pending_loot`（字段名 `pending_loot`）；缺段时的实际读取/清空语义
  以 `architecture/10_存档与持久化.md` 当前记载为准；`GameplayAssembly.RegisterPersistables`
  已注册。
- **`Core.Carriers.Item.EquipmentHost.ReapplyGrants(Id unitId)`**（CR140-02）：按当前 `_equipped`
  记录重放每件装备的 `grants.auras` 并重新核实/施加套装门槛加成，幂等经 `IAuraQuery.HasAura`
  核实；`GameplayAssembly.EnterMap`（post-load 统一钩子）已接入调用。
- **`Core.Carriers.Common.GobjInteractedEvent.ResolvedTeleportTarget`**（`(Id MapId, Vec2
  Position)?`，CR140-03）：跨图传送在 `GameObjectHost.DoTeleport` 判定确实需要跨地图时，随原始
  `TeleportTargetRef` 一并携带已解析结果；`GameplayAssembly` 新增 `ApplyResolvedTeleport` 只负责
  落地，`gobj.interacted` 订阅不再反查模板独立重新解析。
- **`Adapter.Unity.EngineAdapter.UnityResourceLoader.TryLoadModelSync`**（ADR-0017 决策 1 收紧）：
  统一的模型资源缓存优先/未命中同步解析入口，`UnityRenderer3D` 不再直接调用引擎资源读取接口，
  `FinishModelLoad` 异步路径复用同一方法，渲染器只消费已加载资源、不再自行决定加载责任边界。
- **`Adapter.Unity.Presentation.UnitySpriteView` 的 `equipVisualByItemInstanceId` 构造参数**
  （PR140 文档漂移根治）：sprite 外形路线补齐装备外观入口，`UnityViewFactory` sprite 分支已接入
  同一份表；新增示例数据 `data/_sample/display/display.equip_visual.json` 一行。
- **`Presentation.Render.EquipmentSnapshotResolver`/`EquippedItemRef`（窄契约委托）+
  `EquipmentVisualSource.ReplayEquippedForUnit`**（第 0 步补齐，新 View 初始装备外观重放）：按
  单位 id 查询当前全部已装备物品（生产装配根通常包一层
  `EquipmentHost.GetAllEquippedInstances`），供跨图新 View / 存档恢复后创建的 View 在 `CreateView`
  时合成一次 `ItemEquippedEvent` 调用 `OnEvent`，不再要求先等到一次真正的装备/卸装事件才能看见
  已有装备的外观；三处生产装配根（`GameFoundationBootstrap`/`FrameworkResidentHost`/
  `games/_template.GameBootstrap`）已接线。
- **兼容层（源码兼容，`[Obsolete]`，恢复 PJ140-01）**：`Presentation.Render.ICharacterRig.
  HitFrameReached`（默认接口实现，转发到 `IHitFrameEmitter`，探测不到时静默 no-op）；
  `Presentation.Common.ViewKind.GameObject`（与 `Gobj` 数值相同的过时别名）。两者均不要求任何
  既有 `ICharacterRig` 实现/`ViewKind` 消费方改动代码即可继续编译。
- **`HitFrameSyncPolicy.WaitForHitFrame(Id, object batchToken, Action)` 重载 + `event
  Action<Id>? BatchReleased`**（PR140-04）：同一 `batchToken` 的多条等待项视为一个不可拆分批次，
  命中帧或超时都原子释放整批；`FeedbackBinder` 按攻击者维护当前批次 token。

### 修复

9 条逐条判断记录、复现测试、修复位置、验收测试见
[audit-c86bfa9-20260908/followup-2026-09-08c.md](architecture/落地计划/audit-c86bfa9-20260908/followup-2026-09-08c.md)
核实表，概要：
- **核心侧**（CR140-01～03）：宝箱一次性开箱不再在发奖前就永久标记已开、真实满包时不再吞掉/重复
  发放奖励；跨图 `World.ClearAll` 清场后按装备台账重建光环，不再出现"持久装备集合与运行期光环
  脱节"；跨图传送携带已解析结果，不再被内置默认 resolver 二次解析覆盖自定义结果。
- **表现/引擎侧**（PR140-01～04）：Blob 影子固定绕 X 轴转 90° 的旧 XZ 地面约定遗留写法改为随
  当前 XY 地面平面动态朝向相机；异步模型替换恢复 socket 子实例与投影阴影状态，不再销毁挂点子模型
  /阴影状态回退到默认值；Animator 自动过渡在两次检测帧之间完成时不再永久漏发完成事件；同一次
  攻击命中多个目标的命中帧反馈按批次原子释放，不再按 FIFO 逐条错帧/超时。
- **契约兼容性**（PJ140-01）：恢复 `ICharacterRig.HitFrameReached`/`ViewKind.GameObject` 两处
  1.4.0 内直接改名/删除造成的 1.3 消费方源码兼容性破坏。
- **交付流程**（PJ140-02）：Release 附件检测新增 lock 段 `git_commit` 一致性校验，zip 缺失但其余
  必需附件仍存在时直接阻断（不再全量重建后只上传缺失文件，消除"新 zip + 旧附件"混批且无法证明
  同源的窗口）。

### 迁移说明

- **1.3 消费方源码现可直接对 1.4.0 之后的 DLL 编译，无需改动**（PJ140-01 兼容层恢复）：见上
  "新增"一节两处 `[Obsolete]` 兼容成员；两处均计划在下一个 MAJOR 发布中随旧签名一并移除，
  过时成员按 [11_工程规范与测试.md 第 7 节](architecture/11_工程规范与测试.md) 判据至少保留一个
  MINOR 发布周期，本版本是该周期的第一个 MINOR。
- **自定义 `IRenderer3D` 实现的完成事件语义收紧**（PR140-03 收口）：非循环剪辑自然播放完成时必须
  恰好发出一次完成事件（`anim_event.finished`），即便动画状态机在两次 `Tick` 检测帧之间已经自动
  过渡离开目标状态——自实现方若只在"当前状态精确等于目标状态"时才判定完成，会漏发这一窗口内的
  完成事件，导致瞬态状态锁永久残留；`UnityRenderer3D.IsAnimatorStateFinished` 的
  `everEnteredTarget` 记账机制可作为参考实现。
- **gobj 存档新增可选段 `world.gobj_pending_loot`**：字段名 `pending_loot`，数组元素
  `{gobjInstanceId, items:[{templateId, count}]}`；只有使用 `GobjLootDeliveryPolicy.Partial`
  策略且确实产生过未交付余量时才有内容，未注册该段的既有装配根/未升级读取该段的旧存档均不受
  影响（读到 `JsonNull` 视为空表）。
- **版本判据说明**：本次为 MINOR（`1.4.0` → `1.5.0`），"新增"一节列出的全部成员均为新增
  类型/新增可选构造参数/新增枚举/恢复的过时别名或默认实现，不删除、不改名任何已有公开签名，
  不破坏既有调用方编译。

## [1.4.0] - 2026-09-08

两轮修复合并发布：① 游戏侧复核 1.3.0 发现并根治三项问题——model 型动画状态机永久卡死、瞬态状态
同状态重入不重播、框架常驻壳（`FrameworkResidentHost`）未接通 model 型外形（提交 `9f5695d`/
`360ff5f`）。② 第八方深度审核（codex 第六轮，基线 `5c444f1`）17 项发现（PR130-01～08、
PJ130-01～04、CR130-01～05）逐条核实并在当时验证范围内根治，逐条核实表、旧 13 项复核对照、
文档漂移与链接处理见
[audit-5c444f1-20260908/followup-2026-09-08b.md](architecture/落地计划/audit-5c444f1-20260908/followup-2026-09-08b.md)。
均属表现层/引擎适配层/核心规则/交付工具链缺陷修复与能力补齐，无数据表字段删改，无存档格式不兼容
变更（CR130-02 只改变运行期判定逻辑，`player.known_skills` 段 JSON 形状不变）。**收窄说明**：
第七轮外部审核（codex，基线 `c86bfa9`，即本版本自身）复核这 17 项时发现其中两项当时的根治
范围未覆盖全部子路径——CR130-01（购买/拾取事务化）未覆盖一次性宝箱直发路径（新记为
CR140-01，P1）、CR130-05（跨图传送 resolver）只修了同图/null 分支、跨图分支仍被内置 resolver
覆盖（新记为 CR140-03）；这两项不因"17 项全部根治"这句话被视为已闭合，实际状态与验收标准见
[audit-c86bfa9-20260908/AUDIT_REPORT.md](architecture/落地计划/audit-c86bfa9-20260908/AUDIT_REPORT.md)。

### 新增

- **`Presentation.Render.IHitFrameEmitter`**（可选接口，PJ130-04）：承载 `HitFrameReached: Event<Id>`
  命中帧到达事件，`SpriteCharacterRig`/`ModelCharacterRig` 均实现；`ICharacterRig` 本身不再强制要求
  该成员（迁移说明见下）。
- **`IBatchableInventoryHost` 事务覆盖购买/拾取**（CR130-01）：`EconomyHost.Buy`/`Sell`、
  `LootHost.PickUpReject` 涉及"先落地再补偿"的路径改用既有 `IBatchableInventoryHost.BeginBatch()`
  事务，与 `RewardDispatcher.GrantItems`/`QuestHost.TurnIn` 此前已用的惯例统一，失败时连已缓存的
  `item.added`/`item.removed` 事件一并回滚。
- **`SkillHost.LearnSkill` 永久来源参数与 `ForgetAllPermanentGrants`**（CR130-02）：新增
  `LearnSkill(Id unitId, Id skillId, Id sourceId, bool permanent)`；来源分类从"是否等于哨兵"改为
  逐来源记录是否永久；新增 `ForgetAllPermanentGrants(unitId, skillId)` 一次性撤销某技能全部永久
  来源，供 C09 存档替换语义正确覆盖奖励来源技能。
- **`Core.Rules.Skill.ProcHost.RescaleAll(double factor)`**（CR130-03）：混合时间模式切换时同步
  折算 Proc 的 ICD 存量，与 `CooldownTracker`/`AuraHost`/`CastPipeline` 各自的 `RescaleAll` 判断
  记录同款。
- **`Core.Carriers.Common.GobjInteractedEvent.TeleportTargetRef`**（`Id?`，CR130-05）：`on_use` 不
  可分发时 `GameObjectHost.Interact` 算出的传送目标引用随事件一并携带，`GameplayAssembly` 改为直接
  消费该引用，不再反查模板独立重新解析。
- **`Presentation.Assembly.PresentationAssembly` 的 `renderer3D` 参数完整接线**（PR130-06）：该
  构造参数早已声明为可选，三处生产装配根（`GameFoundationBootstrap`/`FrameworkResidentHost`/
  `games/_template.GameBootstrap`）此前只转发给 `UnityViewFactory`、未转发给 `PresentationAssembly`
  的接线遗漏本次补齐。
- **`Presentation.Render.EquipmentVisualSource`**（PR130-07）：新增默认的"实体 → 装备外观"来源
  实现，订阅 `item.added`/`item.equipped`/`item.unequipped` 维护"物品实例 id → 装备外观引用"活
  字典，按 `display.equip_visual.item_id` 索引；`UnityViewFactory` 新增
  `equipVisualByItemInstanceId` 构造参数，三处装配根已接线；新增示例数据
  `data/_sample/display/display.equip_visual.json`。
- **`Core.Foundation.EngineAdapter.UnityRenderer3D.Tick()`/`AnimStateMachine.StateRetriggered`
  事件/`anim_event.finished` 完成事件**（游戏侧复核收口，见上"② 概述"提交 `9f5695d`/`360ff5f`）：
  model 路线动画状态机的完成回调与同状态重入重播通道，详见下"修复"与"迁移说明"。
- **dist 纳入模型占位资源与生成器**（PJ130-02）：`build.ps1` 新增"5.055"节，把
  `adapters/unity/Assets/Resources/GameFoundation/{models,anim_clips}` 与
  `Assets/Editor/GeneratePlaceholderModelAssets.cs` 补进 dist 内适配层包副本；`check.ps1` 包清单
  一致性步骤新增对应必需路径核对。
- **私服停止身份核验**（PJ130-03）：`toolchain/registry/start_registry.ps1` 新增
  `Test-VerdaccioProcessIdentity`（可执行文件名/`CommandLine` 锚点/启动时间三项核验），`-Stop`
  两条路径（PID 文件/端口兜底）均先核验身份，不通过即拒绝停止并非零退出；`-Status` 同步显示核验
  结果。
- **文档相对链接检查测试**（`toolchain/tests/test_markdown_relative_links.py`，交付侧文档漂移
  处理附带产出）：枚举被跟踪 `*.md` 的相对链接并做文件存在性校验，随 `pytest toolchain/tests`
  一并执行。

### 修复

17 条逐条判断记录、复现测试、修复位置、验收测试见
[audit-5c444f1-20260908/followup-2026-09-08b.md](architecture/落地计划/audit-5c444f1-20260908/followup-2026-09-08b.md)
核实表，概要：
- **表现/Unity 侧**（PR130-01/03/04/05/06/07/08）：model 放置与相机投影现共用同一 2.5D 平面、
  影子不随高度抬离地面；武器/技能覆盖剪辑按需登记后再播放，不再因未预注册而无法播放；同一命中帧
  的多条反馈规则原子合批释放；model 缺资源路径落地占位并异步替换，不再直接抛异常；三处生产装配根
  一致转发 `renderer3D` 给 `PresentationAssembly`；model 换装卸载按实例反查精确清理槽位/挂点，不
  再残留挂件。
- **交付与工具链**（PJ130-01/02/03）：Release 缺附件时优先从已验证 commit 一致的旧 zip 原地补齐
  lock/tgz，不再整体重建混入新构建批次；dist 补齐模型占位资源与生成器；registry 停止前核验进程
  身份，不再误杀端口复用的无关进程。
- **契约版本语义**（PJ130-04）：`ICharacterRig.HitFrameReached` 改由可选接口 `IHitFrameEmitter`
  承载，恢复 1.3.0 MINOR 发布号与"不新增强制成员"语义一致。
- **核心规则/玩法**（CR130-01～05）：购买/拾取失败的补偿路径原子化，已派发事件一并回滚；一次性
  奖励技能按来源正确分类为永久/临时并可正确遗忘；混合时间模式折算补齐充能第二窗口、Proc ICD、
  施法学派锁三处遗漏；引导结束后的时间余量不再多结算一跳周期效果；自定义传送 resolver 结果不再
  被内置默认传送或独立重新解析覆盖。

### 迁移说明

- **`ICharacterRig.HitFrameReached` 不再是接口本身的强制成员**（PJ130-04，恢复 1.3.0 引入前的
  兼容性）：任何自定义 `ICharacterRig` 实现无需再实现该事件即可编译；需要命中帧同步的代码改为
  `(rig as IHitFrameEmitter)?.HitFrameReached`，或改持有具体类型（`SpriteCharacterRig`/
  `ModelCharacterRig` 仍直接声明该事件）。`CharacterRigHitFrameSource.RegisterRig` 对不支持
  `IHitFrameEmitter` 的 rig 仍登记成功（`HasRig` 为 true），只是不转发任何事件。
- **`IRenderer3D` 实现新增契约义务**（02 第 1.12 节勘误，游戏侧复核收口引入）：`PlayAnim` 播放的
  非循环剪辑（`loop=false`）自然播放完成时，必须经既有 `OnAnimEvent` 通道额外发出一次约定的
  "播放完成"事件（循环剪辑不发）；具体事件 id 由消费方（表现层装配代码）约定，本仓库 Unity 实现
  固定用 `anim_event.finished`（与 `Presentation.Render.ModelCharacterRig.AnimFinishedEventId`
  逐字相等）。自行实现 `IRenderer3D`（迁移到其它引擎，见 02 第 4 节迁移步骤）的具体游戏，必须在
  自己的实现里补齐这条完成事件，否则 model 型外形的 Attack/Hit/Cast 等瞬态动画状态会永久卡死，
  无法回落。`adapters/conformance/Runtime/Renderer3DScenarios.cs`/`adapters/stub/StubRenderer3D.cs`
  新增对应契约一致性场景与测试专用完成钩子（`CompleteAnimForTest`），供自实现方按同一套场景验证。
- **自定义库存实现建议实现 `IBatchableInventoryHost`**（CR130-01）：未实现该接口的库存宿主，
  `EconomyHost.Buy`/`Sell`、`LootHost.PickUpReject` 会退回历史行为（逐项补偿，不保证已派发事件的
  原子回滚）；建议按 `InventoryHost` 既有实现补齐，获得失败路径的完整原子性。
- **`SkillHost.LearnSkill` 三参调用语义**（CR130-02）：不带 `permanent` 的三参重载
  `LearnSkill(Id,Id,Id)` 现在等价于 `permanent: true`（对绝大多数既有调用方是无感知的行为收紧）；
  生产代码中唯一"曾经依赖三参重载被当作临时来源"的调用方（`Core.Carriers.Assembly.CarriersAssembly`
  装备 `SkillGranter`）已同步改为显式四参调用（`permanent: false`）；自行组装 `SkillHost` 的调用方
  若依赖三参重载表示临时来源，需要同步改为显式四参调用。
- **版本判据说明**：本次为 MINOR（`1.3.0` → `1.4.0`），新增成员（`IHitFrameEmitter`、
  `ProcHost.RescaleAll`、`GobjInteractedEvent.TeleportTargetRef`、`SkillHost` 新重载/新方法、
  `EquipmentVisualSource`、`PresentationAssembly`/`UnityViewFactory` 新构造参数）均为可选/附加成员，
  不破坏既有调用方编译。**收窄**：上一句"不破坏既有调用方编译"仅覆盖上述新增成员，不覆盖下面
  "已知源码兼容性破坏"一条列出的两处改名/删除——那两处已经过独立编译验证证实会破坏未迁移的
  1.3 消费方源码，不属于本条"新增成员均可选"的范围，不应被本条带过。
- **已知源码兼容性破坏与本版恢复的兼容层**（PJ140-01，第七轮外部审核，基线 `c86bfa9` 发现）：
  1.3.0 引入的强制成员 `ICharacterRig.HitFrameReached` 在本版本内被直接从接口移除（迁移到新增的
  可选接口 `IHitFrameEmitter`），以及 `Presentation.Common.Contracts.ViewKind` 的枚举成员
  `GameObject` 被直接改名为 `Gobj`（消除与具体引擎核心类型同名造成的技术名模糊误报，见提交
  `88a0778`）——这两处都不是"新增可选/附加成员"，而是对已发布公开签名的改名/删除，用独立的 1.3
  消费方工程针对真实 1.4.0 DLL 编译可复现失败：`ViewKind.GameObject` 报 `CS0117`、
  `ICharacterRig.HitFrameReached` 报 `CS1061`（复现日志见
  [audit-c86bfa9-20260908/AUDIT_REPORT.md PJ140-01](architecture/落地计划/audit-c86bfa9-20260908/AUDIT_REPORT.md)）。
  按 [11_工程规范与测试.md 第 7 节](architecture/11_工程规范与测试.md) 的判据，公开枚举成员改名/
  删除、接口成员删除同属不兼容变更，本应按 MAJOR 处理或至少保留过时别名/默认实现一个 MINOR
  周期，而不是在同一个 MINOR 发布内直接改名/删除且不留兼容路径。为把当前状态收回到"MINOR 内不
  破坏既有编译"的既有承诺内，本版本恢复了对应的兼容层：`ViewKind` 补回过时别名
  `GameObject`（与 `Gobj` 同值，标记为已过时，建议新代码改用 `Gobj`）；`ICharacterRig` 恢复
  `HitFrameReached` 作为带默认实现的成员（标记为已过时，建议改用 `IHitFrameEmitter`），未覆写该
  默认实现的既有实现类型无需改动即可继续编译。两处兼容层均计划在下一个 MAJOR 发布中随旧签名一并
  移除；具体实现类型与提交见后续修订版本记录。
- 占位模型资产新增 `hit` 状态与 `anim_clips/hit.anim`（`adapters/unity/Assets/Editor/GeneratePlaceholderModelAssets.cs`
  可重复运行生成）；`data/_sample/display/display.anim_set.json` 的
  `display.anim_set.placeholder_biped` 补 `hit` 剪辑声明，供"受击后继续攻击"端到端验收使用。

## [1.3.0] - 2026-09-08

W6 表现能力补齐：补齐"能力边界与未默认接入能力索引"表中长期标记"未接入"的三项表现能力——装备
外观（`model` 型）、武器动画（`auto_attack_anim`/`cast_anim_override`）、关键帧反馈
（`anim_keyframe_driven`）——的引擎无关部分（`presentation/**`）与引擎适配层真实实现
（`adapters/unity/**`），并收口三项能力共同依赖的命中帧同步链路最后一段接线缺口，使其在生产装配根
"默认可接线（开关）"。决策见 [ADR-0017](architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md)
（模型型外形默认路线补齐与命中帧同步）。另附工具链两项修正。

### 新增能力

- **装备外观（`model` 型外形）**：`ModelCharacterRig`（`presentation/render/core/ModelCharacterRig.cs`）
  从占位收口为真实实现——`ApplyEquipVisual`/`ClearSlot`/`ClearSocket` 驱动装备外观替换、
  `SyncPlacement` 落实八原语基准姿态；`Adapter.Unity.EngineAdapter.UnityRenderer3D` 提供
  `IRenderer3D` 真实实现（模型实例化/骨骼动画播放/动画事件/挂点槽位/材质参数/阴影），资源路径约定
  `Resources/GameFoundation/models/<资源引用 id 去类别前缀>`；占位模型资产
  `Assets/Resources/GameFoundation/models/placeholder_biped.*` 与可重复运行的生成脚本
  `Assets/Editor/GeneratePlaceholderModelAssets.cs` 一并提供。
- **武器动画（`auto_attack_anim`/`cast_anim_override`）**：新增 `IWeaponStyleSource`/
  `EquipmentWeaponStyleSource`（`presentation/vfx_sfx/**`，按实体查其当前装备的武器风格引用，复用
  既有 `item.template.display_ref → display.map.weapon_style_ref` 关联链路，未新增任何数据表字段）；
  `Adapter.Unity.Presentation.AnimClipResolver` 接入该来源与新增的
  `AnimStateMachine.StateChangedWithSkill` 事件，Attack 状态用 `AutoAttackAnim`、Cast 状态按触发
  技能 id 命中 `CastAnimOverride` 时用覆盖剪辑，sprite/model 两条路线共用同一份决策逻辑。
- **关键帧反馈（`anim_keyframe_driven`）**：命中帧统一为 `ICharacterRig.HitFrameReached`（sprite
  经序列帧关键帧、model 经 `IRenderer3D.OnAnimEvent` 命中固定事件 id
  `ModelCharacterRig.HitFrameEventId`）；`feedback.binding` 新增 `sync: "hit_frame"` 字段（默认对
  `combat.damage_dealt` 开启）；`presentation/feedback_binder` 新增 `IHitFrameSource`/
  `CharacterRigHitFrameSource`/`HitFrameSyncPolicy`（等待队列，0.5 秒超时兜底，逻辑结算不受影响，
  只调节呈现时机）。**本次收口**：`PresentationAssembly`/`UnityViewFactory` 均补齐接线参数（见下
  "接口变更"），三处引擎侧装配根默认可用一个口味配置项一键切换，不再需要游戏层手工绕过
  `PresentationAssembly` 自行接线。

### 接口变更

- **新增枚举值** `Core.Foundation.EngineAdapter.ResourceKind.Model`。
- **新增契约成员** `Presentation.Render.ICharacterRig.HitFrameReached`（`event Action<Id>?`，已从
  `SpriteCharacterRig` 专属成员提升进接口本身）。**迁移（破坏性，需自定义实现方补齐）**：任何自定义
  `ICharacterRig` 实现（`SpriteCharacterRig`/`ModelCharacterRig` 两个框架内置实现已补齐）必须新增
  实现本事件成员，否则无法通过编译；不打算支持命中帧同步的实现可以让该事件永不触发（等价于
  `LogicDriven` 策略下的既有行为）。
- **新增事件** `Presentation.Render.AnimStateMachine.StateChangedWithSkill`（携带触发技能 id，与既有
  `StateChanged` 三元组事件并存、`StateChanged` 签名不变）。
- **新增数据字段** `feedback.binding.sync`（可选枚举，当前仅 `"hit_frame"`；未提供时按 `event` 是否
  为 `combat.damage_dealt` 决定默认值，见 `FeedbackRule.Sync` 判断记录）。
- **新增可选构造参数**：
  - `Presentation.FeedbackBinder.Core.FeedbackBinder` 新增 `IHitFrameSource? hitFrameSource = null`；
  - `Presentation.Assembly.PresentationAssembly` 新增 `IHitFrameSource? hitFrameSource = null`（原样
    转发给内部 `FeedbackBinderCore` 同名参数——**本次收口新增**，此前该类型完全没有暴露这个参数）；
  - `Adapter.Unity.Presentation.UnityViewFactory` 新增 `IRenderer3D? renderer3D`、
    `IHitFrameSource? hitFrameSource`、`IWeaponStyleSource? weaponStyleSource`、
    `RenderOptions? renderOptions`（最后一项**本次收口新增**——此前即便别处已把
    `RenderOptions.HitFrameSync` 切到 `AnimKeyframeDriven`，`UnityViewFactory` 构造
    `UnitySpriteView`/`UnityModelView` 时仍从不传这份 `RenderOptions`，rig 构造期实际拿到的永远是
    默认 `LogicDriven`，是比"`PresentationAssembly` 未暴露 `hitFrameSource`"更深一层、本次才发现的
    接线缺口，一并收口）。
  以上均为可选参数，不传时行为与改动前完全一致，不影响既有调用方编译或运行期行为。
- **新增字段** `Presentation.Render.RenderOptions.HitFrameSync`（`HitFrameSyncStrategy`，默认
  `LogicDriven`）、`Presentation.FeedbackBinder.Contracts.FeedbackOptions.HitFrameSync`/
  `HitFrameSyncTimeoutSeconds`（默认 `LogicDriven`/0.5 秒）——两者是同一个口味配置项在渲染侧/反馈
  绑定侧的两个落点，装配层需要保持一致（见下"游戏侧接入步骤"第 6 条）。
- **诊断/测试专用新增成员（非契约）**：`Adapter.Unity.EngineAdapter.UnityRenderer2D.EmitParticleCallCount`
  （累计 `EmitParticle` 调用次数，同 `UnityAudio.PlaySfxCallCount` 一类既有诊断计数惯例）。

### 游戏侧接入步骤（新游戏若要使用以上三项能力）

1. **model 型外形**：`display.map` 填一行 `kind: "model"`，`model_ref` 指向三维模型资源引用 id，
   `anim_set_ref` 指向 `display.anim_set` 表一行（`clips[*]` 声明动画剪辑，`events[*]` 声明关键帧，
   固定名字 `"hit_frame"` 是框架约定的命中帧标记，见 `AnimSetEventsShapeRule` 校验）；`sockets`/
   `slots` 数组声明挂点/换装槽位 id。
2. **模型预制体放置约定路径**：`Resources/GameFoundation/models/<资源引用 id 去类别前缀>`（模型）、
   `Resources/GameFoundation/anim_clips/<资源引用 id 去类别前缀>`（动画剪辑）；挂点/槽位对象命名须
   与 `display.map.sockets`/`slots` 逐字一致（含域前缀）。
3. **武器风格**：`display.weapon_style` 表填一行（`auto_attack_anim`/`cast_anim_override`），经
   `item.template.display_ref → display.map.logical_id → weapon_style_ref` 关联；装配根构造一个
   `Presentation.VfxSfx.Core.EquipmentWeaponStyleSource`（需提供
   `MainHandWeaponTemplateResolver` 委托）传给 `UnityViewFactory` 的 `weaponStyleSource` 参数。
4. **主手槽位 id**：09/04 未定义全局槽位登记表，本次在三处装配根（灰盒 `GameFoundationBootstrap`
   的 `_mainHandSlotId`、模板 `GameOptions.MainHandSlotId`）各暴露一个口味配置项承载，具体游戏按
   自己的装备槽位登记表填入。
5. **命中帧同步开关**：装配根构造一个 `CharacterRigHitFrameSource`，同一个实例分别传给
   `UnityViewFactory` 的 `hitFrameSource` 参数与 `PresentationAssembly` 的 `hitFrameSource` 参数；
   构造**同一个** `RenderOptions` 实例（`HitFrameSync = AnimKeyframeDriven`）分别传给
   `UnityViewFactory` 的 `renderOptions` 参数与 `PresentationAssemblyOptions.RenderOptions`，并把
   `PresentationAssemblyOptions.FeedbackOptions.HitFrameSync` 同步切到 `AnimKeyframeDriven`——五处
   必须两两取同一实例/同一策略值，任一处遗漏或不一致都会让命中帧同步整体或部分失效（完整步骤见
   `adapters/unity` 包 README"命中帧同步接线步骤"一节）。三处框架自带装配根
   （`GameFoundationBootstrap`/`Adapter.Unity.Shell.FrameworkResidentHost`/
   `games/_template.GameBootstrap`）均已按上述步骤接线，各暴露一个布尔口味配置项
   （`_hitFrameSyncEnabled`/`GameOptions.HitFrameSyncEnabled`）一键切换，默认 `false`
   （`LogicDriven`，行为与本次收口前完全一致）。

### 工具链修正

- `toolchain/registry/start_registry.ps1`：私服停止逻辑改为以端口监听进程为准（不再依赖可能已经
  漂移的 pid 文件/进程句柄），新增 `-Status` 查询当前私服运行状态。
- `toolchain/consumer_smoke.ps1`：消费方演练在启动 Unity 前先等待同名残留进程退出，避免与新启动的
  实例互相冲突；冒烟结果落盘为 `consumer_smoke.log`。

### 迁移说明

- **自定义 `ICharacterRig` 实现**必须新增实现 `HitFrameReached` 事件成员（破坏性接口变更，详见上
  "接口变更"）；不需要命中帧同步的实现可以让该事件永不触发。
- 使用框架自带三处装配根（`GameFoundationBootstrap`/`FrameworkResidentHost`/`games/_template.
  GameBootstrap`）的游戏无需任何改动即可编译运行，命中帧同步默认关闭（`LogicDriven`），行为与
  1.2.0 完全一致；需要启用时按上面"游戏侧接入步骤"第 5 条打开对应口味配置项即可。
- 自行组装 `PresentationAssembly`/`UnityViewFactory` 的游戏（未使用框架自带装配根）：新增参数均为
  可选、默认 `null`，不传不影响现有行为，可按需选择性升级到新能力。

## [1.2.0] - 2026-09-08

第七方深度审核（codex 第五轮，基线 `1.1.0`/`5e779c6`，报告见
`architecture/落地计划/audit-5e779c6-20260907/`）13 项主发现（GP26-01～03、FR-01～05、U01～05）+
2 项验证阶段追加复现（同图读档孤儿实体、`skill.def.charges`/`cost[]` 嵌套形状校验缺口）+ WA 报告
记录的 3 条相邻缺口（施放当下按当前时间模式折算冷却/充能/光环 duration、周期累加器同步换算、
`grants.auras` 重复引用数据提醒）+ 本轮补齐的 2 条相邻缺口（`QuestHost.TurnIn` 回滚事务化、
`CastPipeline` 施放当下折算 `cast_time`/`channel_time`/`modify_cooldown` delta）全部核实并根治，
20 条核实表、判断记录、文档漂移处理逐条见
[audit-5e779c6-20260907/followup-2026-09-08.md](architecture/落地计划/audit-5e779c6-20260907/followup-2026-09-08.md)。
本条目记录变更内容与迁移说明。

### 修复（概要，逐条详见 followup 文档）

- 玩法：奖励发放失败回滚现在连已入队的 `item.added`/`item.removed` 事件一并撤销，不再被其它任务
  的 `consumeOnProgress` 目标误当新获得而错误推进进度（GP26-01）；`QuestHost.TurnIn` 步骤 2/3 失败
  的回滚同样改走事务，不再靠"移除又放回"产生虚假 `item.added`（STEP0-1）；直接交互（非 gossip）
  跨地图 teleporter 类物件现在能正确触发场景切换（GP26-03）；同图读档若命中"快照仍在倒计时、
  当下已有孤儿实体"会主动清理孤儿实体，不再与倒计时到期新生实体重复（附加-R14）。
- 规则/技能：套装门槛加成与普通装备 `grants.auras` 现在共享同一份光环来源引用计数，卸装备不再
  误删仍满足门槛的套装光环（GP26-02）；连续/离散时间模式切换现在同步换算技能冷却、光环剩余时间
  （FR-01），且施放/施加**当下**（不只是切换那一刻）就会按当前生效模式正确折算 `cooldown_duration`/
  `charges.recharge_time`/光环 `duration`/周期 `interval`/`cast_time`/`channel_time`/引导
  `tick_interval`/`modify_cooldown` 的 `delta`（WA-GAP-1/2、STEP0-2；`add_charge` 的 `amount` 是
  离散计数不受影响）；`charges.recharge_time<=0` 时充能耗尽后不再永久卡死，改为即时恢复（FR-02）；
  大步长推进不再让光环到期后的时间余量多算周期结算次数（FR-03）；读档恢复等级现在会失效重算评级
  属性缓存（FR-04）；技能 `effects[]`/`charges`/`cost[]` 的嵌套坏字段现在在数据校验阶段就会被拦下
  阻断合入，不再拖到首次施法才崩溃（FR-05 + 附加-Shape）；`item.template.grants.auras` 内重复引用
  同一光环新增数据校验 Warning 提醒（WA-GAP-3）。
- 表现：修正音乐交叉淡入/停止选错音源导致新旧音源互换（U01）；SFX 自然播放结束现在会回收对象池
  位，不再无限增长（U02）；序列帧动画大步长跨帧现在按序补发每一个跨过的关键帧，不再丢失中途命中
  特效（U03）；默认序列帧渲染器（`UnityFrameAnimPlayer` 挂载点）现在正确接入高度偏移/淡出/闪色，
  与纸娃娃层表现一致（U04，此前 WB 初判"无法复现"，WD 复核推翻，见 followup 文档 U04 行"过程"）。
- 交付：Release 工作流附件存在性检查现在核对完整五件套（zip/lock/三个 UPM tgz），部分缺失时只
  补传缺失的文件，不再因为 zip 已存在就整体跳过、遗漏其余附件（U05）。
- 文档：01/03/04/06/07/08/10/13 共 8 份架构文档按 12 §5 格式勘误（L5 查询/命令边界口径统一、
  固定步/计时器描述统一、时间字段"施放当下折算"补充说明、`grants.auras` 契约缺口清单更新等）；
  `core/rules/skill`、`core/carriers/item`、`core/gameplay/{common,quest,assembly}`、
  `core/numbers/progression`、`core/rules/expr_host`、`core/numbers/archetype/schema`、
  `presentation/vfx_sfx`、`core/foundation/sim_loop` 等模块 README 判断记录同步更新；"能力边界与
  未默认接入能力索引"补齐 `day_cycle`/ATB/孤儿检查(`DisplayMapCoverageRule`)/`FeedbackRuleValidator`
  四项，现收录两份审计报告表格给出的全部条目。

### 接口 / 事件 / 数据变更与迁移说明

- **新增事件** `sim.time_model_rescaled`（`Core.Rules.Common.TimeModelRescaledEvent{double Factor}`，
  `PublishImmediate`）：连续/离散模式切换时发出，驱动 `CooldownTracker`/`AuraHost`/`CastPipeline`
  各自的 `RescaleAll`。**迁移**：使用标准 `TimeModelSwitch`/`SkillHost` 装配（`GameplayAssembly`
  默认路径）的游戏无需任何改动，事件已自动接线。若游戏层自行实现了不经过 `TimeModelSwitch` 的
  模式切换逻辑，需要自己在切换点 `PublishImmediate` 这个事件才能让技能冷却/光环/读条正确折算。
- **新增事件** `progression.state_restored`（`Core.Numbers.Progression.ProgressionRestoredEvent
  {Id UnitId, int Level}`，`PublishImmediate`）：读档恢复等级时发出。**迁移**：标准装配无需改动，
  `RulesAssembly` 已默认订阅并转发到 `Stats.RecomputeRatingStats`；若游戏层维护了自己的等级相关
  缓存且未监听 `progression.level_up`，可能需要额外订阅这个新事件。
- **新增接口** `Core.Carriers.Common.IInventoryTransaction`（`Commit()` + `IDisposable`）、
  `IBatchableInventoryHost`（`BeginBatch(): IInventoryTransaction`）：`InventoryHost` 已实现。
  **迁移（需自定义实现方补实现）**：自定义 `IInventoryHost` 实现若不实现 `IBatchableInventoryHost`，
  `RewardDispatcher.GrantItems`/`QuestHost.TurnIn` 会自动回退到旧的"逐项精确量回滚"历史行为，
  行为不变、无需改动；若自定义实现选择实现该接口以获得"失败时事件也一并撤销"的完整保证，
  **必须支持嵌套调用**——`BeginBatch()` 在自身已处于一个未提交/未回滚的事务中时，应返回一个
  "加入外层事务"的透传句柄（其 `Commit`/`Dispose` 均为 no-op，不影响外层事务状态），而不是抛异常：
  `QuestHost.TurnIn` 会持有一个未提交的事务再调用 `RewardDispatcher.Grant`，后者若也需要发放物品
  会再次调用 `BeginBatch()`，两者必须能安全组合，见 `IInventoryTransaction.cs`
  `IBatchableInventoryHost.BeginBatch` 判断记录、`InventoryHost.BeginBatch`/`Transaction` 实现。
- **新增委托/配置** `core/gameplay/spawn/contracts/SpawnOptions.cs` 的 `GobjDespawnerDelegate`/
  `SpawnOptions.GobjDespawner`（可选）：供 `SpawnHost.Load` 在命中"同图读档孤儿实体"场景时移除
  `gobj` 域的孤儿实体（生物域经已持有的 `ICreatureFactory` 处理，无需额外配置）。**迁移**：使用
  `GameplayAssembly` 默认装配的游戏无需改动，已默认注入；自行组装 `SpawnHost` 的游戏若希望获得
  这一修复的完整效果，需要自己注入 `GobjDespawner`，否则保持旧行为（孤儿实体脱离追踪但不移除）
  并记一条诊断，不强制。
- **新增校验规则**（均已在 `RulesSchemaCatalog`/`CarriersSchemaCatalog` 的 `RegisterAll` 注册，
  `toolchain/validator` 自动继承，无需游戏层改动装配代码）：
  - `ChargesRechargeTimeZeroWarningRule`（Warning，check 名 `charges_recharge_time_zero`）
  - `ChargesShapeRule`（**Error**，check 名 `charges_max_missing`/`charges_max_invalid`/
    `charges_recharge_time_missing`/`charges_recharge_time_not_number`）
  - `CostEntryShapeRule`（**Error**，check 名 `cost_entry_not_object`/`cost_power_type_invalid`/
    `cost_amount_invalid`）
  - `ItemGrantsAurasDuplicateRule`（Warning，check 名 `item_grants_auras_duplicate`）
  - `EffectKindRegisteredRule` 行为变更（非新增）：新增 check 名 `effect_entry_not_object`/
    `effect_kind_missing`/`effect_kind_not_string`
  **迁移（需要内容作者关注）**：`ChargesShapeRule`/`CostEntryShapeRule`/`EffectKindRegisteredRule`
  的新增检查项是 **Error 级**——此前能以 0 error 通过 `DataRegistry.LoadAll()`、只在首次施法才
  崩溃的坏数据（`effects[]` 缺 `kind`、`charges`/`cost[]` 内部子字段缺失或类型错误），现在会在
  数据校验阶段直接阻断合入。已存在类似坏数据的内容仓库升级后首次跑 `validate_data.py`/
  `toolchain/validator` 会新增报错，需要修正数据（这些数据即便不修，本来也会在运行时抛异常，
  阻断合入是提前暴露问题，不是收紧了原本合法的用法）。
- **`CooldownTracker.ModifyCooldown` 行为变更**（签名不变）：`delta` 参数现按当前生效时间模式的
  `_currentFactor` 折算后再应用，与 `cooldown_duration` 同一口径。**迁移**：若游戏内容的
  `modify_cooldown` 效果原语按"与该技能 `cooldown_duration` 同一份连续秒 authoring"的惯例填写
  `delta`（本仓库默认假设，多数内容应该已经是这样），无需改动数据，离散模式下的实际效果会比
  修复前更符合直觉；若有内容特意依赖"离散模式下 `delta` 按当前轮数直接解释、不折算"的旧（有缺陷
  的）行为，需要重新核对该效果在离散战斗中的数值表现。`add_charge` 的 `amount` **不**受本次改动
  影响（离散充能计数，不是时间量）。
- **`CooldownTracker`/`AuraHost`/`CastPipeline` 新增公开方法** `RescaleAll(double factor)`：三者
  均由 `SkillHost` 构造期统一订阅 `sim.time_model_rescaled` 并转发，标准装配下不需要游戏层直接
  调用。
- **`UnityRenderer2D` 新增具体类型方法** `GetLayersRoot(SpriteHandle)`、
  `RegisterAnimRootRenderer(SpriteHandle, SpriteRenderer)`（均不进入 `IRenderer2D` 契约）；
  **`UnityFrameAnimPlayer` 新增公开属性** `SpriteRenderer`（只读，转发既有私有访问器）。
  **迁移**：仅供 `UnityViewFactory.AttachDefaultAnimation` 内部使用，游戏层通常无需直接调用；
  自定义 `IRenderer2D` 实现若也想让默认序列帧动画正确响应 height/flash/fade，可参考这一实现模式
  （把序列帧渲染器纳入与纸娃娃层同一套变换/颜色遍历）。
- **`IInventoryHost`/`IAudio`/`IFrameAnimPlayer` 等既有公开契约签名均未变化**（`UnityAudio` 新增
  的 `ActiveMusicSource`/`SfxPoolSize` 等均为 `internal` 测试专用访问器，不进入跨模块契约）。
- **无存档格式变更**：本轮全部修复均不改动任何 `IPersistable.Save()`/`Load()` 段的 JSON 结构。
- **`.github/workflows/release.yml` 行为变更**（CI 逻辑，非代码契约）：附件存在性判定改为核对
  完整五件套，游戏侧消费方无需改动，只影响本仓库自己的发布 CI 行为。

## [1.1.0] - 2026-09-07

第六方深度审核（codex 第四轮，基线 `1.0.0`/`7e63d66`，报告见
`architecture/落地计划/audit-7e63d66-20260907/`）19 条发现（C01～C12 共 12 条代码问题、P01～P07
共 7 条项目/交付问题）全部核实成立并根治，详见
`architecture/落地计划/audit-7e63d66-20260907/followup-2026-09-07d.md`。本条目记录变更内容。

### 修复（概要，逐条详见 followup 文档）

- 存档读档：旧备份候选核对 `meta.slot_id` 归属，避免跨槽误读（C01）；候选筛选核对 meta 必填字段，
  避免语义损坏文件挡住健康备份（C10）。
- 规则/技能：施法来源被销毁后周期效果缩放属性降级为 0、进战判定静默跳过而非抛异常（C02）；吸收
  耗尽的连锁移除正确传递触发深度，纳入 `MaxTriggerDepth` 收敛预算（C03）；装备 Replace 换句柄后
  另一件装备同步迁移引用，不再误清光环（C08）；技能读档改为替换语义而非只增不减（C09）。
- 玩法：Encounter/Achievement 发奖失败后保留可重试状态，不提前提交终态（C04）；`RewardDispatcher`
  按实际落地量回滚，不假设"请求量=落地量"（C05）；任务扣除物品改为先核验总量、不够不碰库存的原子
  操作（C06）；刷新点存档倒计时在同图读档时优先于当下世界状态（C11）；`reload_save` 现在也发布
  复活事件，动画状态机不再卡在死亡态（C12）。
- 表现：VFX/SFX 资源冷加载超时也会完整走完播放完成信号链，`ISfxPlayer` 新增独立时钟入口接入
  Unity 生产帧循环（C07）。
- 交付：Release 工作流在打包前补一步默认构建，干净 checkout 也能出传统路径 DLL（P01）；发行 ZIP
  与 UPM 工具链包的 validator 自包含（编译好的 DLL 或源码引用二选一），不再依赖包内不存在的源码树
  （P02）；同步脚本改清单制，只清理框架自己上次写入的文件，不再误删消费者文件（P03）；
  `get_framework.ps1` 默认严格校验请求版本与本地归档版本一致，不一致需显式 `-AllowVersionMismatch`
  （P04）；发布只推当前分支与本次新建的单个标签，不再固定推 `main` 与全部标签（P05）；私服禁止
  自注册取得发布权限，发布/删包限定到显式发布账号（P06）；sprite/audio/vfx 三类资源的同步路径与
  Unity loader 实际查找路径统一到共享映射表 `toolchain/resource_layout_map.json`（P07）。

### 接口变更与迁移说明

非破坏性增补（C# 默认接口方法，未覆盖的既有实现自动获得历史行为，无需改动）：

- `Core.Carriers.Common.IInventoryHost` 新增 `bool TryAddItem(Id unitId, Id templateId, int count, out int actualCount)`。
- `Core.Rules.Common.IAuraQuery` 新增带触发深度参数的移除重载，以及 `InstanceReplaced` 事件（默认空
  `add`/`remove`）。

破坏性增补（自定义实现方需补实现；仓库内既有实现均已补齐）：

- `Core.Gameplay.Achievement.IAchievementHost` 新增 `IReadOnlyList<Id> RetryPendingRewards(Id unitId)`
  ——仓库内唯一实现 `AchievementHost` 已补齐。
- `Presentation.VfxSfx.Contracts.ISfxPlayer` 新增 `void Update(double dt)`——仓库内 `SfxPlayer` 与
  测试用 `RecordingSfxPlayer`（`presentation/vfx_sfx/tests/AudioLayerVolumeHostTests.cs`）已补齐。

行为变更（签名不变，语义/时序变化，下游若按旧假设编写逻辑需要重新核对）：

- `Core.Gameplay.Death.RespawnPolicy.ReloadSave` 读档成功时现在也经 `IEventBus.Enqueue` 补发一次
  `Core.Rules.Common.UnitRespawnedEvent`（此前只有 `RespawnPoint` 策略发布该事件）。
- 存档段 `player.achievement_state` 每条记录新增可选字段 `pending_reward`（布尔，默认 `false`），
  向后兼容，旧存档缺省该字段按 `false` 处理。
- `Core.Rules.Skill.KnownSkillsPersistable.Load` 改为替换语义（快照未包含的永久技能会被撤销），不
  再是只增不减。
- `toolchain/get_framework.ps1` 新增 `-AllowVersionMismatch` 开关（默认关闭）；不带该开关时请求
  版本与本地归档版本不一致会直接 `throw`，不再仅 warning 后继续落地。
- `build.ps1 -Release`/`-Publish` 的 `git push` 改为推送当前所在分支 + 本次新建的单个标签，不再
  固定推送 `main` 分支与本机全部标签（`--tags`）；在 detached HEAD 下会报错拒绝执行。
- 私服 `toolchain/registry/config.yaml`：`auth.htpasswd.max_users` 由未设置（等价放开自注册）改为
  `-1`（禁止自注册）；`publish`/`unpublish` 权限从 `$authenticated`（任何已认证用户）改为限定显式
  用户名 `ws-game-publisher`。任何依赖"匿名自注册后即可发布"的私服接入脚本需要改用
  `toolchain/registry/init_publisher.ps1` 无人值守建号。

## [1.0.0] - 2026-09-07

首个正式基线版本。此前 `0.1.0`/`0.2.0` 均为落地过程中的里程碑快照（供消费方演练与打包流程自测
使用，未作为正式对外发布版本），`1.0.0` 是阶段 0～5 全部完成、经三轮内部审计与一轮外部深度审核
修复收口后的第一个"可供真实游戏接入"的稳定基线。

### 新增

- **阶段 0～5 全部完成**：环境与仓库骨架、L0 基础层（12 模块）、L1+L2 数值与规则层、L3+L4 载体
  与玩法层、Unity 适配层 + 表现层 + UI 套件、美术管线与资产规格；`Core.sln` 六个测试工程合计
  1550+ 例单测全过，Unity EditMode/PlayMode 测试全绿，独立版无人值守冒烟（连续/离散两种时间
  模型）通过，消费方演练（从零搭建独立于框架源码树的最小 Unity 工程，只以分发包为输入）通过。
- **离散时间模型**：与连续时间模型并列的第二套时间驱动方式（`TurnScheduler`、先攻策略、行动点
  移动预算、回合 HUD 等），玩家意图经 `WorldSim` 路由到调度器，回合结束统一推进计时器。
- **框架级数据目录分层**：`data/_framework/`（事件词汇登记表、输入动作声明等，随分发包交付）与
  `data/_sample/`（框架自测数据，不随分发包交付）分离；`DataRegistry` 支持多根合并加载（主键/
  schema 冲突阻断）。
- **新游戏模板** `games/_template/`：可运行的最小闭环骨架（`GameBootstrap`/`GameOptions`/
  `Editor/GameSceneBuilder`/`data/game/`/`validate.ps1`/PlayMode 冒烟测试），对照 13 号文档口味
  配置项清单逐行落地。
- **资产管线**：`toolchain/import_assets.py` 资产导入工具、`assets/_placeholder/` 通用占位资产
  包、方向档位/纸娃娃分层/序列帧图集等资产契约（14 号文档）。
- **一键门禁** `check.ps1`（22 步）与提交前钩子 `.githooks/pre-commit`（快速子集）、持续集成
  `.github/workflows/ci.yml`（非 Unity 门禁子集）。
- **版本管理方案**：语义化版本、`CHANGELOG.md`、`build.ps1 -Release`/`-DryRun`/`-Publish`、
  维护分支流程（`release/X.Y.x`）、发布工作流 `.github/workflows/release.yml`、游戏侧引用工具
  `toolchain/get_framework.ps1` 与锁文件 `ws-game.lock`。
- **私服交付通道**：与 zip 快照通道并存的第二条消费通道——私有包仓库（`toolchain/registry/`，
  Verdaccio，npm 兼容协议）+ 三个可发布包拆分（`com.gamefoundation.adapter.unity`/
  `com.gamefoundation.framework-data`/`com.gamefoundation.toolchain`）；`build.ps1 -Dist` 新增
  组装三个包 + `npm pack`，`-Release` 新增 `-PublishRegistry [-RegistryUrl]`；
  `toolchain/get_framework.ps1` 新增 `-FromRegistry`；`toolchain/sync_package_content.ps1`
  （新增）同步私服包内容到消费游戏工程；`check.ps1` 新增"包清单一致性"步骤。

### 修复

- **三轮内部文档代码一致性审计**（2026-09-05～2026-09-07）：逐轮核对 00～14 号架构文档与实现的
  一致性，修复审计发现的代码缺失（行动点、`SpellModDimension.Charges`、离散 GCD 接线、死亡复活
  三策略执行主体、种族被动光环应用、回合状态占位值等）与文档勘误，详见
  `architecture/落地计划/文档代码一致性审计_2026-09-05.md`、`_2026-09-06.md`、`_2026-09-07.md`。
- **外部深度审核 33 条发现根治**（分支 `codex/deep-review-b3b91ee-20260907`，报告见
  `architecture/落地计划/audit-b3b91ee-20260907/`）：FND-01～10、GP-01～10、RC-01～11、
  TOOL-01/02 共 33 条经三波并行核实全部成立并根治；排障过程中额外发现并修复 PlayMode 全量套件
  `VerticalSliceTests` 因跨夹具存档槽配额累积导致的隐性失败。
- **缺口收敛 G1/G2/G3**（2026-09-05）：16 条已知契约缺口中 13 条落地解决（`AnchorResolver`、
  `SpawnRequester`、`TeleportResolverDelegate`、`SaveRequesterDelegate`、回合状态显示等），3 条
  设计层判断维持"保留"（非拍板内容或本就只需单点承担的既定设计）。
- 修复：codex 第三轮深度审核 19 条（详见 audit-68c9bed-20260907/followup-2026-09-07c.md）。
- 修复：发布流程先提交后打包，lock/MANIFEST 的 `git_commit` 指向发布提交；写回覆盖
  `packages-lock.json`（`build.ps1 -Release` 首次实跑发现的时序与写回遗漏两处缺陷，根治后
  `1.0.0` 重新发布，详见根 `README.md`"版本与发布"一节）。

### 兼容性说明

- 存档格式：`save_version`（存档信封层字段，迁移链唯一依据）当前为初始版本，尚无历史存档需要
  迁移；后续存档结构不兼容变更须递增 `save_version` 并登记迁移函数（见
  `architecture/10_存档与持久化.md` 第 5 节）。
- 数据表：各表独立的 `schema_version`（见 `architecture/04_数据与内容管线.md`）随本版本一次性
  确定，表结构不兼容变更（字段删改）须递增 `schema_version` 并提供迁移路径。
- 构建产物公开契约：六个核心 DLL（`Core.Foundation`、`Core.Numbers`、`Core.Rules`、
  `Core.Carriers`、`Core.Gameplay`、`Presentation.Common`）随分发包 `dist/1.0.0/` 交付；对外公开
  的 L-1 接口签名（引擎适配层契约）、事件 key、数据表结构、存档结构变更均属 MAJOR 级变更范畴。
- 与 `0.2.0` 的差异：`1.0.0` 不改变任何公开契约或数据结构，只是把此前若干里程碑快照正式确立为
  第一个语义化版本基线，并新增本文件描述的版本管理方案本身（`build.ps1`/`check.ps1`/工作流/
  文档新增的发布相关能力）。

### 从 68c9bed 早期消费者迁移（早于 `0.1.0`/`0.2.0` 快照拉取过框架的消费方需核对）

`1.0.0` 基线包含 codex 第三轮深度审核（`audit-68c9bed-20260907/`）引入的以下破坏性/行为变更，若消费
方在提交 `68c9bed` 或更早时拉取过框架、并自行实现或依赖了下列契约，需要按下表核对：

| 契约/行为 | 变更内容 | 影响范围与迁移动作 |
|---|---|---|
| `Core.Gameplay.Common.IRewardDispatcher.Grant` | 签名由 `void Grant(...)` 改为 `bool Grant(...)`（破坏性签名变更） | 任何直接实现本接口的类型需要补返回值；调用方若忽略返回值仍可编译通过，但拿不到"是否实际发放成功"的信号，建议改为检查返回值以配合 `IQuestHost.TurnIn` 的原子化回滚（发放失败时任务不会被标记 `TurnedIn`，已消耗物品会回滚）。 |
| `Core.Rules.Common.ITargetHost` | 新增方法 `FilterExplicitTargets`（破坏性增补） | 任何直接实现本接口的类型需要补一个实现；仓库内唯一实现 `TargetHost` 已补齐。修复前显式指定的非法目标（如链式过滤要求 undead 但玩家显式指定了非 undead 目标）会被直接放行，修复后统一经该方法校验并按 `NoValidTarget` 失败码拒绝。 |
| `Presentation.FeedbackBinder.Contracts.IFeedbackSink` | 新增事件 `PendingPlaybackChanged`（破坏性增补） | 任何直接实现本接口的类型需要补一个实现（默认空 `add`/`remove` 亦可）。配套 `Presentation.VfxSfx.Contracts.IVfxPlayer`/`ISfxPlayer` 同批新增 `PendingSpawnCountChanged`/`PendingPlayCountChanged` 事件；`FeedbackBinder.TryPublishFinished` 改为"队列空 && 无 merger 待处理 && 无 sink 待处理"三者同时成立才发 `PlaybackFinishedEvent`，此前冷资源（首次加载中）的挂起播放会被误判为已完成。 |

以上三项详见 `architecture/落地计划/audit-68c9bed-20260907/followup-2026-09-07c.md`（N02/N10/N17）。

## [0.2.0]

里程碑快照（供内部打包流程与消费方演练自测使用）。收录工程收尾 K、加固波 J（契约一致性测试套件、
消费方演练脚本、PlayMode 隔离）、第三轮审计修复波（W1～W4）、第四方深度审核修复三波、离散时间
模型引擎侧接线、`found.time_model` 归属勘误、框架级数据目录与新游戏模板等一系列提交；详见
`architecture/落地计划/落地方案与分阶段计划.md`"落地进度记录"各小节与本仓库 `git log`。

## [0.1.0]

首个里程碑快照。收录阶段 0～5 全部完成、缺口收敛 G1/G2/G3、框架收官（离散时间模型初版落地、
文档/数据/门禁收尾）、收边波 I/J1 等提交；详见
`architecture/落地计划/落地方案与分阶段计划.md`"落地进度记录"各小节与本仓库 `git log`。
