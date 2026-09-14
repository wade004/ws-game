# L1 数值层 · stat_block 属性表

职责：单位的属性两轮拓扑序聚合（第一轮：无派生来源的属性三段式 `基础值 + Σflat` →
`× (1 + Σpct)` → `× Π(1 + Σmult_group)`；第二轮：`category=derived` 的属性基础值改为
Σ(来源属性最终值 × 系数)，再走同一套三段式，见 `architecture/06_规则层_属性技能战斗AI.md`
第 1.1 节、`architecture/adr/0030-属性系统派生换算与来源类别.md` 决策 2，T-N1-2 落地），
始终启用的换算层（第 1.1 节修订段、ADR-0030 决策 3；T-N1-3 起触发条件为
`category=="percent"`，不再有独立开关，见下文"换算层"一节）与可选抗性维度（`00` 第 3 节
"可选属性维度"；判定条件 T-N1-2 起改为 `category=="defense"`，见下文"抗性维度"一节）。
对应 `01_分层与依赖.md` L1 模块表 `stat_block` 行、契约接口名 `StatHost`。

依赖：L0（`Core.Foundation.Common` 的 `Id`；`Core.Foundation.DataRegistry` 的
`IDataRegistryView`/`TableSchema`/`FieldSchema`/`FieldKind`/`IValidationRule`；
`Core.Foundation.EventBus` 的 `IEventBus`/`IEvent`）。不引用 `Core.Rules`/`Core.Carriers`/
`Core.Gameplay`、不引用任何引擎适配层实现、不引用 `System.Threading`/`DateTime`/
`System.Random`/`System.Reflection`（见 `11_工程规范与测试.md` 第 4 节"确定性"）。

## 目录

```
stat_block/
  README.md
  contracts/   StatModifier.cs（StatModifierOp、StatModifier）
               IStatHost.cs
               StatHostOptions.cs
               StatChangedEvent.cs（StatBlockEventKeys、StatChangedEvent）
  core/        StatSchemas.cs（stat.definition / stat.rating_conversion / stat.weight 的
               TableSchema；stat.definition schema 版本 2，T-N1-1；group 字段 T-N1-2 起放宽为
               required:false；stat.rating_conversion 的 entries 字段 T-N1-3 起放宽为
               required:false，新增可选 saturation 字段，与 entries 二选一，schema 版本
               不变仍为 2；stat.weight 为 T-N1-5 新表，schema 版本 1，StatHost 不读取）
               StatDefinitionValidationRule.cs（IValidationRule：min<=max（T-N1-2 起
               同时检查嵌套 clamp.min<=clamp.max）、rating_conversion_ref 需要
               is_rating、derived_from 仅限 derived、conversion_ref 仅限 percent，
               T-N1-1 新增后两条）
               StatDefinitionDerivationCycleValidationRule.cs（T-N1-2 新增
               IValidationRule：stat.definition.derived_from 派生来源链无环，含自环；
               写法照抄 archetype 层 ArchTalentTreeCycleValidationRule）
               StatRatingConversionValidationRule.cs（T-N1-3 新增 IValidationRule：
               stat.rating_conversion 记录必须恰好登记 entries 或 saturation 之一）
               StatDefinitionConsumerValidationRule.cs（T-N1-9 新增 IValidationRule：
               检查名 stat_definition_no_consumer，Warning 级、NonEscalatable=true（04 第 5
               节"属性无消费者"整组警告登记为不可提升）；通用扫描全部已注册表的 Reference/
               SoftReference/Map 键引用字段是否命中 stat.definition，外加
               GetReferenceDeclarations() 动态声明与框架内置消费者属性名清单两条补充来源）
               StatHost.cs（IStatHost 默认实现；T-N1-2 起改读 category/derived_from/
               clamp（不再读 group/平级 min/max），两轮拓扑序聚合、派生失效传播、
               抗性维度判定改用 category=="defense"；T-N1-3 起 LoadRatingConversions
               无条件执行、换算触发条件改 category=="percent"、改读 v2 的
               conversion_ref（不再读已废弃的 is_rating/rating_conversion_ref）、
               ConvertRating 按 stat.rating_conversion 登记的形态（entries 断点表 /
               saturation 二元饱和）求值）
  schema/      README.md（字段表）
  tests/       StatHostTests.cs（T-N1-2：两轮聚合/clamp 夹取时机/失效传播/拓扑序
               稳定/派生成环防御/派生无环校验规则/抗性维度 v2 原生用例；T-N1-3：
               恒等/饱和两种曲线形态、"不存在关闭路径"行为与反射用例）
               StatDefinitionMigrationTests.cs（T-N1-1：1→2 迁移字段值回归）
               StatSchemaCoverageTests.cs（rating_conversion.entries、
               definition.derived_from/clamp 子结构覆盖；T-N1-3：
               rating_conversion.saturation 子结构与形态二选一正反例）
               StatWeightSchemaTests.cs（T-N1-5：stat.weight 合法记录/缺必填/引用不存在/
               class_overrides 子结构坏形状覆盖；"StatHost 不读该表"源码扫描 + 行为双重防御）
               StatDefinitionConsumerValidationRuleTests.cs（T-N1-9：无消费者警告正负例——
               负例/stat.weight 引用正例/derived_from 来源正例/框架内置豁免正例/
               NonEscalatable 在 WarningsBlock 下不阻断）
               RatingConversionMigrationTests.cs
               P2_05_StatHostReloadTests.cs
               CORE114_03_StatHostReloadCacheInvalidationTests.cs
```

## T-N1-1：stat.definition schema 版本 2（category/derived_from/clamp/conversion_ref/scope）

分阶段落地计划 T-N1-1（ADR-0030 决策 1；落地清单拍板 1/2/11）：`stat.definition` 新增
`category`/`derived_from`/`clamp`/`conversion_ref`/`scope` 五个字段，`group`/`min`/`max`/
`is_rating`/`rating_conversion_ref` 标废弃（保留一个版本周期不删），schema 版本 1→2 迁移链
自动补齐新字段。**本任务只改 schema 与校验规则，不改 `StatHost.cs`**——三段式聚合、评级换算、
抗性维度三节描述的仍是 T-N1-1 之前的行为，`StatHost` 仍读 `group`/`is_rating` 等旧字段；两轮
拓扑序聚合、换算层始终启用、`clamp` 真正生效等 ADR-0030 决策 2/3 的运行时改动留给 T-N1-2。

字段表与 `group→category` 映射细节见 `schema/README.md`"`stat.definition`"一节（含设计层裁定
（2026-09-14）采纳的推断映射：`secondary` 按 `is_rating` 分裂为 `percent`/`misc`）。

## T-N1-2：两轮拓扑序聚合、clamp 第二轮后夹取、失效传播、抗性维度独立策略项

分阶段落地计划 T-N1-2（ADR-0030 决策 2；落地清单拍板 1/2/11）：`StatHost` 从 T-N1-1 遗留的
"仍读 group/min/max"状态，改为无条件消费 `category`/`derived_from`/嵌套 `clamp`；
`is_rating`/`rating_conversion_ref` 读取逻辑本任务未改（换算层触发条件改
`category==percent` 留给 T-N1-3）。

- **加载期**：`StatHost.LoadDefinitions` 改读 `category`（`GetString`，required，不再无条件读
  `group`）；`derived_from` 仅当 `category=="derived"` 时纳入派生计算图；`clamp.min`/
  `clamp.max` 取代平级 `min`/`max`。随后 `BuildDerivationGraph` 建反向依赖表
  `_derivedDependents`（source → 直接依赖它的派生属性）与全部属性 id 的稳定拓扑序
  `_topoOrder`（Kahn 算法，候选零入度集合用 `SortedSet<Id>` 逐步取最小值出队，`Id` 按序数
  字符串序比较——不依赖字典/数组枚举顺序，见"拓扑序稳定"测试）；遇到环（含自环）抛
  `InvalidOperationException`，是内容校验之外的加载期防御（正常数据应已被下面的
  `StatDefinitionDerivationCycleValidationRule` 拦下）。
- **两轮聚合**：`ComputeFinal` 的"基础值"由 `ResolveBaseValue` 决定——显式 `SetBase` 过的值
  永远优先（设计层裁定（2026-09-14）：采纳，与既有 `DefaultBase` 回退规则同构的自然推广，见
  `ResolveBaseValue` 源码注释）；否则 `category=="derived"` 的属性走 `ComputeDerivedBase`
  （Σ 来源属性最终值 × 系数，来源经 `ResolveFinal` 取值——命中缓存直接读，未命中现算但不
  写缓存），其余属性沿用 `default_base`。flat/pct/mult 三段顺序与 `multGroup` 算法不变；
  `clamp` 仍是每个属性自己聚合的最后一步，对来源属性即"它自己完成三段式之后"（先于被下游
  派生属性读取），对派生属性即"第二轮聚合之后"（拍板 2）——两种情形共用同一段代码，不按轮次
  分叉。
- **失效传播**：`SetBase`/`AddModifier`/`RemoveModifiersBySource`/`ResetBase` 更新自己那个
  属性的缓存之后，经 `PropagateDerivedInvalidation` 按 `_topoOrder` 顺序重算全部（传递）
  依赖它、且此前已经被缓存过的派生属性（未被查询过的不主动补算，下次 `GetStat` 现算时自然
  读到最新状态——与 `RecomputeAllCachedStatsAfterReload` 同一判断记录口径）；`
  RecomputeRatingStats`（等级变化驱动的评级重算入口）同样接入这条传播路径（06 第 1.1 节
  修订段"与既有'等级变化驱动评级缓存重算'同一通知路径"）。`RecomputeAllCachedStatsAfterReload`
  （整表 reload 后的缓存重算）改为按 `_topoOrder` 过滤后处理，不再直接遍历
  `unit.Cache.Keys` 的原始顺序。
- **抗性维度独立策略项**：`StatHostOptions.EnableResistanceGroup` 属性本身在 T-N1-1 之前就
  已经是独立于内容数据的 `bool`（不是从 `group` 取值算出来的），本任务真正改变的只是它门控的
  判定条件——`ComputeFinal` 从 `def.Group == "resistance"` 改为 `def.Category == "defense"`。
  属性名保持 `EnableResistanceGroup` 不改名（G3 ABI 门禁禁止删除既有公开成员，改名等价于
  "删除旧成员"；新增一个同义属性又会造成"两个旗标谁为准"的歧义，没有收益），默认值维持
  `true`——判断记录与理由见 `contracts/StatHostOptions.cs` 该属性源码注释。
- **派生无环校验**：新增 `StatDefinitionDerivationCycleValidationRule`（检查名
  `stat_definition_derivation_cycle`，Error 级，写法照抄 `core/numbers/archetype` 的
  `ArchTalentTreeCycleValidationRule`），随 `RulesSchemaCatalog.RegisterL1Schemas` 与
  `L1SampleDataTests.BuildWorld` 注册。

## T-N1-3：换算层始终启用、触发条件改 category==percent、三形态复用通用曲线

分阶段落地计划 T-N1-3（ADR-0030 决策 3；落地清单）：换算层从"`StatHostOptions.
EnableRatingConversion` 门控的可选策略"改为"始终启用"，触发条件从"`is_rating` 字段"改为
"`category=="percent"`"，三种曲线形态（恒等、按等级除数、饱和）全部复用 04 第 3.6 节通用
曲线契约，不新增求值路径。

- **换算层始终启用**：`ReloadFromRegistry` 的 `LoadRatingConversions` 调用不再被
  `if (_options.EnableRatingConversion)` 包裹；`ComputeFinal` 的触发条件从
  `_options.EnableRatingConversion && def.IsRating` 改为单一条件 `def.Category ==
  "percent"`——`StatDefinition.IsRating` 字段已删除（内部实现细节，不是公开契约），`Category`
  本身已经能表达"是否经换算层"，不需要独立布尔值冗余记录同一件事。
- **`EnableRatingConversion` 保留但无效化（偏离计划原文"删除"）**：计划原文要求删除该属性，
  但落地计划第 1 节"每阶段对外契约变化必须控制在 MINOR"——删除既有公开属性是 ABI 破坏（G3
  门禁不允许）。设计层裁定：属性签名不变，标记 `[Obsolete("换算层自 1.31.0 起始终启用
  （ADR-0030 决策 3），本属性无任何作用，保留仅为二进制兼容")]`，`StatHost` 全部代码路径不再
  读取它（`grep -n "_options.EnableRatingConversion" core/numbers/stat_block/core/StatHost.cs`
  应为零命中）。"不存在关闭路径"由两条测试守护（`StatHostTests.cs`）：
  `EnableRatingConversion_HasNoEffect_PercentStatsStillConvert`（显式构造
  `EnableRatingConversion=false`，断言 `percent` 属性仍经曲线换算，不是直通原值）、
  `StatHostOptions_NoUnobsoleteConversionSwitch_Exists`（反射扫描 `StatHostOptions` 全部公开
  属性，断言不存在任何未标 `[Obsolete]` 的、名字含 `RatingConversion`/`Conversion` 的布尔
  属性——防止未来有人加一个新开关重新引入关闭路径而没人发现）。
- **`LoadDefinitions` 改读 `conversion_ref`**：不再读已废弃的 `is_rating`/
  `rating_conversion_ref`（两者的取值已由 T-N1-1 的 1→2 迁移链折算进 `category`/
  `conversion_ref`，到 `LoadDefinitions` 读到的记录已经过迁移，直接读新字段即可，同 T-N1-2
  对 `category`/`clamp` 的既有处理口径）。
- **三种曲线形态复用通用曲线契约**（`StatHost.ConvertRating`）：
  - **恒等**（保守版：恒等加 `clamp` 硬上限）：`stat.definition.conversion_ref` 缺省——直接
    返回 `base + Σflat` 原值；硬上限由 `ComputeFinal` 聚合末尾的 `clamp` 夹取承担，与换算层
    本身无关。
  - **按等级除数**（标准版：等级索引除数）：既有 `stat.rating_conversion.entries` 断点表形态
    （T-N0-4 起复用 `PiecewiseCurve`），公式与本任务之前完全相同——`percent = rawValue /
    Evaluate(单位等级)`，本任务未改动该路径的任何代码。
  - **饱和**（变态版：饱和曲线，除数随等级增长）：`stat.rating_conversion` 新增可选字段
    `saturation`（`CurveSchema.SaturationField`，`{k: Number(必填,>0), cap?:
    Number(可选,>0,缺省 1)}`），公式 `输出 = rawValue / (rawValue + k × 单位等级)`，非正分母
    与负结果降级为 0，再以 `cap` 封顶——与 `combat.resist_curve` 的
    `ResistCurveKind.Saturation` 分支（`ResistCurve.ComputeReduction`）逐运算相同公式，落实
    ADR-0030 决策 3"换算曲线契约固定为……与 `combat.resist_curve` 同形态"。`entries`/
    `saturation` 二选一由新增 `StatRatingConversionValidationRule`（检查名
    `stat_rating_conversion_requires_one_shape`）强制。
- **schema 版本判断**：`entries` 从 `required: true` 放宽为 `required: false`，新增
  `saturation` 可选字段——纯新增可选字段/放宽必填不改变已有合法数据的判定结论，不升级
  `stat.rating_conversion` 的 `currentSchemaVersion`（仍为 2），同 `StatSchemas.GroupValues`
  判断记录先例（T-N1-2"`group` 字段放宽为 `required:false` 不升版本"）。
- **回放/Perf 基线核查（本任务不是 ★ 任务，核查结论为零影响，未改动基线）**：
  `data/_sample/stat/stat.definition.json` 的 `crit_rating`/`dodge_rating` 两条属性
  `is_rating=true`，1→2 迁移后 `category=percent`——换算从"默认关闭"变为"始终启用"，属于
  行为变更；但核查 `data/_sample` 全库，两条属性均无 `default_base`（缺省 0）也没有任何
  装备/光环/职业模板赋值来源，`combat.hit_table_config` 直接把其原始值当 miss/crit 分支概率
  使用，`0` 经任意换算曲线（恒等或除数曲线）结果仍是 `0`——运行时数值零变化。`git status`
  确认 `core/gameplay/tests/Replay/replay_baseline.json`/`core/gameplay/tests/Perf/
  perf_baseline.json` 零改动，`--filter "FullyQualifiedName~Replay"` 全绿。
  `games/_template` 无 `percent`/`conversion_ref` 类属性，不受影响。

## T-N1-4：SetDerivationCoefficientOverrides / ClearDerivationCoefficientOverrides（职业派生系数覆盖）

分阶段落地计划 T-N1-4（ADR-0030 决策 2"职业模板可覆盖派生系数（`arch.class.derivation_overrides`，
可选）"）：`StatHost` 新增按单位的派生系数覆盖层，供 `Core.Numbers.Archetype.ArchetypeRegistry`
（经 `DerivationCoefficientOverrideWriter` 具名委托）与 `Core.Rules.Assembly.RulesAssembly.
ReloadArchetypeAndRace`（直接持有 `StatHost` 具体类型，同 `Stats.ResetBase`/`Stats.SetBase` 既有
用法）在单位首次应用职业/换职业/读档恢复时写入。

- **公开入口**（新增，未加入 `IStatHost` 接口——同 `ResetBase` 判断记录，`RulesAssembly.Stats`
  持有的是 `StatHost` 具体类型，加入具体类型即可满足接线需求，不扩大既有接口契约的语义范围）：
  - `void SetDerivationCoefficientOverrides(Id unitId, IReadOnlyList<(Id Stat, Id Source, double
    Coefficient)> overrides)`——**全量替换**语义：上一次调用登记过、这次不再出现的
    `(stat, source)` 自动失效，调用方不需要先调 `Clear` 再调 `Set`（天然实现"先清旧覆盖再写新
    覆盖"）。
  - `void ClearDerivationCoefficientOverrides(Id unitId)`——等价于传空数组，幂等 no-op（本就没有
    覆盖时不发 `StatChanged`，同 `ResetBase` 惯例）。
- **生效位置**：`ComputeDerivedBase`（第二轮聚合，见 T-N1-2）为每条来源查找 `unit.
  DerivationOverrides[目标属性][来源属性]`，命中则用覆盖系数取代 `stat.definition.derived_from`
  登记的默认系数，未命中（含整条属性没有任何覆盖、或覆盖指向一条不存在的来源边）回退默认——覆盖
  只改变"用哪个数"，不改变来源集合本身，也不在运行时校验"覆盖是否指向真实存在的边"（那是内容
  校验阶段 `ArchClassDerivationOverrideValidationRule` 的职责，见 `Core.Numbers.Archetype` 模块
  README）。
- **重算与事件**：只重算"此前已经被缓存过"的受影响属性（同 `PropagateDerivedInvalidation`/
  `RecomputeAllCachedStatsAfterReload` 一贯口径），受影响集合 = 覆盖表变化涉及的目标属性 ∪ 它们
  的全部传递依赖者，按拓扑序处理，与来源属性变化触发的失效传播是同一条通知路径。
- **换职业/读档恢复接线点**：`ArchetypeRegistry.ApplyTo`（单位首次注册）经新增构造重载注入的
  `DerivationCoefficientOverrideWriter` 转发（委托为 null 时向后兼容跳过）；`RulesAssembly.
  ReloadArchetypeAndRace`（换职业与 `IDerivedStateRebuilder.OnSectionLoaded` 对 `player.race_id`
  段的读档恢复共用的唯一路径）在写完新职业 `BaseStats` 之后无条件调用一次
  `Stats.SetDerivationCoefficientOverrides(unitId, cls.DerivationOverrides)`——不按 `classChanged`
  分叉，职业未变时重复调用是幂等的。

## T-N1-5：stat.weight 属性权重表 schema、注册、样例

分阶段落地计划 T-N1-5（ADR-0030 决策 7；04 第 1.1 节表清单 `stat.weight` 行）：新增
`stat.weight` 表——属性 id → 权重（一点主属性当量），可按职业覆盖；消费者是装备预算消耗
（ADR-0032）、技能控制/增益价值（ADR-0031）与装备评分/自动配装，三者均不在本任务范围内落地。
**本任务只登记 schema/注册/样例，`StatHost` 不读取 `stat.weight`**（06 第 1 节明文规定，
`core/StatHost.cs` 全文不出现字符串 `"stat.weight"`）。

- **字段**：`id`（`stat.weight.<name>`）、`stat`（Reference→`stat.definition`，目标属性）、
  `weight`（Number，`>= 0`，默认权重）、`class_overrides`（可选，`Array<{class:
  Reference(arch.class), weight: Number(>= 0)}>`，按职业覆盖）、`description`（可选）。字段表
  与两条设计层裁定（2026-09-14，均采纳：`class_overrides` 子结构形态选 `Array` 而非 `Map`；
  `weight` 范围约束 `>= 0` 而非放开负数）见 `schema/README.md`"`stat.weight`"一节。
- **注册**：`RulesSchemaCatalog.RegisterL1Schemas` 在 `StatSchemas.RatingConversion` 之后新增
  `registry.RegisterSchema(StatSchemas.Weight)`；`core/numbers/tests/L1SampleDataTests.cs`
  的 `BuildWorld`（L1 五模块联调世界的等价手工清单）同步补齐，随 `data/_sample/stat/
  stat.weight.json` 一起走完整字段级校验。
- **样例**：`data/_sample/stat/stat.weight.json` 为既有六条示例属性（strength/stamina/
  armor/crit_rating/dodge_rating/move_speed）各登记一条权重，`stat.weight.strength` 一条带
  `class_overrides`（覆盖 `arch.class.sample_a`）——数值只是样例，不是框架默认值。
  `games/_template/data/game/stat/stat.weight.json` 给空壳（`rows: []`，拍板 10），随附
  `.meta`（`TextScriptImporter`，guid 照 N0 `item.slot_definition.json.meta` 写法新生成一份
  唯一值）；`games/_template/data/README.md` 表清单同步拆行。
- **"StatHost 不读该表"防御**：`tests/StatWeightSchemaTests.cs` 双重防御——源码扫描
  （`[CallerFilePath]` 定位 `core/StatHost.cs` 磁盘路径、`File.ReadAllText` 断言不含
  `"stat.weight"` 字符串）+ 行为测试（注册 `stat.weight` 并加载含数据的记录后，正常构造
  `StatHost`、`GetStat` 不受影响、不抛异常）。

## T-N1-7：IStatHost.GetScope（供结算管线目标乘区/被暴击减免按显式清单消费）

分阶段落地计划 T-N1-7（[ADR-0030](../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
决策 5；06 第 4.1 节 2026-09-14 修订段"目标乘区"）：`core/rules/combat.Resolver` 需要对结算配置
显式给出的一批属性 id，按 `scope` 与结算上下文的来源类别匹配求和，但 `IStatHost` 到 T-N1-6 为止
只暴露"查询某个已知 id 的属性最终值"（`GetStat`），不支持"查询某属性的作用域"——**T-N1-1 已把
`scope` 字段登记进 schema，但当时判断记录明确写"本任务只改 schema 与校验规则，不改
`StatHost.cs`"，此后 T-N1-2～T-N1-6 均未消费过这个字段**（`StatHost.cs` 全文不出现 `scope`，
直到本任务）。本任务是第一个需要它的消费方，补齐：

- `IStatHost` 新增一个 C#8 默认接口成员（同 `Core.Rules.Common.IUnitAccess.GetSourceKind`/
  `GetMapId`——T-N1-6 先例——同一惯例："默认实现零成本退化，真正实现方显式 override"）：
  `GetScope(Id stat) -> string`：默认恒 `"any"`；`StatHost` 侧查内部
  `_definitions[stat].Scope`，属性未登记时同样返回 `"any"`（与 `stat.definition.scope` 字段
  本身缺省 `any` 同一语义，不是"未知属性"的特殊标记）。
- `StatHost.LoadDefinitions` 补上 `scope`（`TryGetString`，缺省 `"any"`）解析，私有
  `StatDefinition` 记录类型新增 `Scope` 字段——三段式聚合/两轮拓扑序/换算层三段既有逻辑完全不读
  这个字段，只是把它从"内容数据里的一个字段"变成"运行期可查询的元数据"，不改变任何既有
  `GetStat`/`SetBase`/聚合行为。
- `Tests.Presentation.Assembly.InterfaceDefaultMemberForwardingTests`（该通用门禁反射六个核心
  程序集，见该测试类型注释）已验证 `StatHost` 对该新默认成员显式转发（未静默吃掉默认值）。
- 本任务未新增测试文件到本模块（`core/numbers/stat_block/tests/`）——`GetScope` 的行为经
  `core/rules/combat/tests/ResolverScopedReductionTests.cs`（消费方）端到端覆盖：该测试类
  通过真实 `StatHost` + 含 `scope` 字段的 `stat.definition` 夹具数据驱动 `Resolver`，比直接
  单测 `StatHost` 该方法本身更贴近实际用法（同一属性经加载→查询→结算管线消费的完整链路），已在
  combat 模块的 G1 用例清单里列出。

**复核返工记录（2026-09-14）**：首版实现在本方法之外，还给 `IStatHost` 新增了
`GetDefinitionIdsByCategory(string category)`（按 `stat.definition.category` 批量列出属性 id），
供 `core/rules/combat.Resolver` 按类别扫描目标乘区/被暴击减免候选属性。复核裁定该设计有严重
缺陷并打回：ADR-0030 决策 9 明确把"护甲"归入 `defense` 推荐分类，一旦游戏内容按此分类登记护甲
属性，类别扫描会把护甲原始数值误当百分比计入目标乘区，真实内容一接入即错。返工改为
`core/rules/combat.CombatOptions` 显式属性 id 清单（`DamageTakenPctStats`/
`CritTakenReductionStats`）+ 本方法 `GetScope` 过滤，不再需要"按类别列出一批属性 id"这个能力，
`GetDefinitionIdsByCategory` 已撤回——**从未发布**（本模块 `IStatHost.cs` 未随任何已发布版本
对外，`toolchain/abi_probe.ps1` 对已发布基线核对无破坏），直接删除签名，不留废弃占位。详见
`core/rules/combat/README.md` 判断记录 18"偏离计划原文的说明"一节。

## T-N1-9：属性无消费者警告规则、样例按推荐分类补齐

分阶段落地计划 T-N1-9（ADR-0030 决策 8/9；04 第 5 节数值类校验项分级表"警告 | 属性无消费者 |
`stat.definition` 条目未被命中表配置、结算管线、资源池定义、派生规则或任一表达式引用"一行）：
新增 `StatDefinitionConsumerValidationRule`（检查名 `stat_definition_no_consumer`），并把
`data/_sample/stat/stat.definition.json` 按 ADR-0030 决策 9 的推荐分类升级为 v2 写法、补齐样例。

- **"消费者"定义与扫描机制**：04 原文四个例子（命中表配置/结算管线/资源池定义/派生规则/表达式
  引用）是例举而非封闭枚举（判断记录见规则源码类型注释）。实现不逐表硬编码消费者定义，而是通用
  扫描：(1) 全部已注册表的 `TableSchema`，任意深度递归 `Fields`/`Item`/`Variants`/`Map`，命中
  `ReferenceTable`/`SoftReferenceTable`/`MapSchema.KeyReferenceTable` 等于 `"stat.definition"`
  的字段，把已加载记录里该字段的实际取值计入"已消费属性 id"集合（一次遍历天然覆盖
  `stat.definition.derived_from[].stat`、`stat.weight.stat`、`arch.class.base_stats`/
  `derivation_overrides`、`arch.power_type.max_source.stat`、
  `combat.hit_table_config.*.stat`/`miss.hit_stat` 等全部既有引用点，新增一张引用表不需要改本
  规则代码）；(2) `IDataRegistryView.GetReferenceDeclarations()`（1.26.0）动态声明的引用；
  (3) 框架内置消费者属性名清单（`StatDefinitionConsumerValidationRule.
  FrameworkBuiltinConsumerStatIds`：`stat.armor`/`stat.damage_done_pct`/`stat.damage_taken_pct`/
  `stat.healing_done_pct`——`Core.Rules.Combat.CombatOptions` 构造期硬编码的默认属性 id，运行时
  选项引用不在数据里，规则拿不到，只能手抄登记，若 `CombatOptions` 未来改名/新增默认属性 id 需要
  人工同步，本模块不能反射跨层读取 L2 类型）。
- **级别与不可提升**：`DefaultSeverity = Warning`，`NonEscalatable = true`——04 第 5 节明文
  "警告级这一组登记为不可提升"（ADR-0030～0034 新增的数值类警告整组，不只本规则），即使
  `DataRegistryStrictness.WarningsBlock` 下也不阻断（`ValidationRuleMetadataTests` 同款语义，见
  `core/foundation/data_registry`）。`toolchain/validate_data.py --strict` 因此对该规则命中的
  Warning 不阻断退出码；示例数据仍要求每条属性都有消费者（本任务样例已逐条核对，见下），"无消费者"
  负例只出现在 `tests/StatDefinitionConsumerValidationRuleTests.cs` 的构造夹具里，不出现在
  `data/_sample`。
- **样例按 ADR-0030 决策 9 推荐分类补齐**（`data/_sample/stat/stat.definition.json` 升级为 v2
  写法，`schema_version: 2`，因为派生属性需要 `derived_from` 字段、v1 无此字段）：主属性四个
  （力量/敏捷/智力/体质）；派生三个（攻击强度=力量×2.0、法术强度=智力×1.5、资源回复速率=
  智力×0.1——决策 8/数值设计 01 第 3.7 节"派生属性列表中'资源上限'改为'资源回复速率'"，本样例
  据此**不**再把"生命上限"列入派生属性，与 06 第 1.3 节修订段仍保留"生命上限"字样这一处表述
  不一致，已如实记录为契约疑点，未改架构文档）；战斗百分比五个（暴击率/闪避/命中三个沿用既有
  评级属性迁移到 `category: percent`，新增暴击伤害与急速——急速引用新增
  `stat.rating.haste` 饱和曲线换算，示范"变态版"曲线形态，与既有 crit/dodge 的"标准版"断点表
  换算互补）；防御一个（护甲，`category` 从 v1 `derived` 改判为 `defense`——沿用 T-N1-7 复核
  返工记录"ADR-0030 决策 9 明确把护甲归入 defense 推荐分类"的既定裁定）；杂项四个（移动速度/
  仇恨系数/先攻/幸运）。每条新增属性都在 `data/_sample/stat/stat.weight.json` 补一条权重记录
  作为保底消费者（`stat.weight` 本身不影响 `StatHost` 运行时行为，见 T-N1-5，纯粹用于满足
  "有消费者"这条内容惯例），部分属性另有更具体的消费者（`arch.class.sample_a.base_stats`/
  `derivation_overrides`、`combat.hit_table_config` 各分支）。
- **`arch.power_type` 样例 health 上限改回固定值**：ADR-0030 决策 8"资源上限固定、回复速率
  派生"为推荐默认，`data/_sample/arch/arch.power_type.json` 的 `arch.power.health`
  （`"override": true` 行，见 `data/README.md`"arch.power_type"判断记录）从此前"演示改为随
  `stat.stamina` 成长"（`{kind: stat, stat: stat.stamina}`）撤回为另一个固定值
  `{kind: fixed, value: 120}`——继续演示行覆盖语义（覆盖后的值与框架级默认 100 不同，证明确实
  发生了覆盖），只是覆盖后不再引用属性；`data/_framework` 侧的框架级默认行本就是
  `{kind: fixed, value: 100}`，不受影响。`arch.power.sample_vigor`（`max_source` 引用
  `stat.stamina`）保留不变，继续承担"上限引用属性"这条路径的测试覆盖（同 `data/README.md`
  该节既有判断记录的分工）。`core/numbers/tests/L1SampleDataTests.cs` 两个断言健康池上限的用例
  （`ApplyTo_RegistersPowerTypes_HealthCapReferencesStamina` 改名为
  `ApplyTo_RegistersPowerTypes_HealthCapFixed`、`GrantFromSource_LevelUp_
  AppliesGrowth_StatsAndPowerCapChange`）同步改断言为固定值 120、不随属性成长。
- **契约疑点（如实上报，未改架构文档）**：(1) 检查名 `stat_definition_no_consumer`——04 第 5 节
  分级表"属性无消费者"一行未给出检查名（该表检查项列是中文短语），按 `stat_definition_*` 前缀
  命名，同本模块既有三条检查名的处理口径，设计层裁定（2026-09-14）：采纳；(2) ADR-0030 决策 9 原文与 06 第 1.3
  节修订段的推荐派生属性列表仍写"生命上限"，与决策 8/数值设计 01 第 3.7 节"改为资源回复速率"
  的表述冲突，本任务样例遵照后者（更具体、更晚的修订段落）；(3) "消费者"的操作化定义（本节
  第一条）比 04 原文四个例子更宽，把 `stat.weight`/`arch.power_type.max_source` 等任一登记为
  `Reference(stat.definition)` 的字段都算作消费者，不局限于 04 原文逐字列出的四类，属于对
  "或任一表达式引用"收尾措辞的从宽解释——设计层裁定（2026-09-14）：采纳，比 04 原文四例更宽是
  正确方向，不收紧。

## 用法

```csharp
var registry = new DataRegistry(source, bus);
registry.RegisterSchema(StatSchemas.Definition);
registry.RegisterSchema(StatSchemas.RatingConversion);          // 仅当数据里有这张表
registry.RegisterValidationRule(new StatDefinitionValidationRule());
var report = registry.LoadAll();

var statHost = new StatHost(registry, bus, new StatHostOptions
{
    // 换算层自 T-N1-3 起始终启用，不再有开关（EnableRatingConversion 已废弃、无任何效果，
    // 见"T-N1-3"一节）——这里不需要也不应该再设置它。
    EnableResistanceGroup = true,     // 00 第 3 节：可选维度，本项默认开启；T-N1-2 起判定
                                       // category=="defense"（属性名沿用旧名，见"T-N1-2"一节）
});

statHost.RegisterUnit(unitId);
statHost.SetBase(unitId, new Id("stat.strength"), 10);
statHost.AddModifier(unitId, new StatModifier(new Id("stat.strength"), StatModifierOp.Flat, 5, sourceId));
var final = statHost.GetStat(unitId, new Id("stat.strength"));
statHost.RemoveModifiersBySource(unitId, sourceId);
```

调用方（宿主）负责：把 `StatSchemas.Definition`/`StatSchemas.RatingConversion` 注册进
`IDataRegistry`；把 `StatDefinitionValidationRule` 注册为校验规则（可选，但建议）；在自己的
`IEventCatalog` 中登记 `StatBlockEventKeys.StatChanged`（`"stat.changed"`），或关闭
`EventBusOptions.StrictCatalog`——本模块不依赖 `event_bus/generated/EventKeys.g.cs`
是否已生成该常量（该文件由 `found.event_catalog.json` 驱动，登记时序不在本模块控制范围内）。

**`ResetBase`（CORE-110-02 根治，第十二轮外部审核，P2，architecture/落地计划/
audit-ac3b622-20260909）**：`StatHost.ResetBase(unitId, stat)` 清除某单位某属性此前显式
`SetBase` 过的值，恢复成"从未显式设置过"的状态——之后 `GetBase`/`GetStat` 重新退回
`stat.definition.default_base`。本就没有被显式 `SetBase` 过时是安全的幂等 no-op（不发
`StatChanged`）。供 `Core.Rules.Assembly.RulesAssembly.ReloadArchetypeAndRace` 在同图换职业时
清理"旧职业声明过、新职业未声明"的基础属性键，见该方法所在模块 README"CORE-110-02 根治"一节。
未加入 `IStatHost` 契约接口（`RulesAssembly.Stats` 持有的是 `StatHost` 具体类型，不是接口），
不影响任何既有 `IStatHost` 实现的源码兼容性。

## 三段式聚合与浮点确定性

- `flat`：按 `AddModifier` 调用顺序（`List<StatModifier>` 插入顺序）逐项累加。
- `pct`：同样按插入顺序累加后，整体作为 `(1 + Σpct)` 乘一次。
- `mult`：同一 `StatModifier.MultGroup` 内的多条 `Mult` 修正先按插入顺序相加，
  各乘区再按"该乘区名字第一次在这个属性的修正列表里出现"的顺序连乘（`Π(1+组内和)`）。
  未显式指定 `MultGroup` 的修正落入默认乘区 `StatModifier.DefaultMultGroup`（`"default"`）。
- 同一组插入顺序两次独立运行，聚合结果逐位相等（`double` 精确相等）；不同插入顺序不保证
  相等（`Mult` 各乘区之间是乘法，浮点乘法不满足结合律的舍入误差属于预期行为，架构文档只
  承诺"同种子同输入产生同结果"，不承诺不同插入顺序等价）。

## 换算层（始终启用，T-N1-3 起不再有开关）

`stat.definition.category == "percent"` 的属性，`base + Σflat` 一律先经换算层
（`StatHost.ConvertRating`），换算结果再进入 `pct`/`mult` 两段——不再有独立开关（此前的
`StatHostOptions.EnableRatingConversion` 已标 `[Obsolete]`、无任何效果，见"T-N1-3"一节）。
`conversion_ref` 缺省即**恒等**曲线（原样返回，保守版风格的硬上限改由 `stat.definition.clamp`
承担）；引用 `stat.rating_conversion` 时按该记录登记的形态求值：

- **按等级除数**（标准版）：`entries` 通用断点表 `{x: 等级, y: 每 1% 所需点数}`（T-N0-4 起经
  `PiecewiseCurve.Evaluate` 插值，越界取端点）——`percent = rawValue / pointsPerPercent`。曲线
  的自变量是**单位等级**（`x`，设计层 2026-09-05 拍板，取代此前"level 是评级原始值自己的
  插值断点"的判断），经 `StatHostOptions.LevelLookup(unitId)` 查询；为 `null` 时等级一律按
  1 处理。`StatHost` 不直接引用 `core/numbers/progression` 的任何类型——由调用方把真正的
  等级来源（如 `IProgressionHost.GetLevel`）适配成 `LevelLookup` 委托签名后注入。
- **饱和**（变态版，T-N1-3 新增）：`saturation` 字段 `{k, cap?}`——
  `percent = rawValue / (rawValue + k × 单位等级)`，以 `cap` 封顶（缺省 1），公式与
  `combat.resist_curve` 饱和分支同形态。

```csharp
var statHost = new StatHost(registry, bus, new StatHostOptions
{
    LevelLookup = unitId => progressionHost.GetLevel(unitId),  // 由调用方适配真正的等级来源
});
```

## 抗性维度（可选，默认开启）

`stat.definition.category == "defense"`（T-N1-2 起；此前是 `group == "resistance"`，`group`
已废弃）的属性始终可以正常 `SetBase`/`AddModifier`；`StatHostOptions.EnableResistanceGroup=false`
时 `GetStat` 对这类属性恒返回 `0` 并向 `StatHost.Warnings` 追加一条警告，不影响其它类别属性的
聚合。

## 不负责什么

- 不做技能/光环/施法管线（L2 `core/rules/skill` 的职责，见 06 第 3 节）。
- 不持有单位的其它状态（生命值、资源池等属于 `power_set` 与更高层）。
- 不做数据文件的读写/校验实现（复用 `core/foundation/data_registry`），本模块只提供
  `TableSchema`/`IValidationRule` 声明，注册时机由调用方掌控。
- 不直接依赖 `core/numbers/progression` 的任何具体类型——换算层曲线虽然以单位等级为自变量
  （见"换算层"一节，设计层 2026-09-05 拍板），但等级经 `StatHostOptions.LevelLookup`
  具名委托注入，不是直接引用 `IProgressionHost`，避免与 `progression` 产生同层跨模块的编译期
  耦合（`ConvertRating` 判断记录）。
