# stat_block 数据表字段说明

对应 `architecture/04_数据与内容管线.md` 第 1.1 节表清单 `stat.definition`/
`stat.rating_conversion` 两行；本文件是该总索引在本模块内的字段展开（04 第 1.1 节
"每份下游文档在讲对应模块时会重复给出该模块表的详细字段"）。`TableSchema` 实现见
`core/StatSchemas.cs`。

## `stat.definition`

schema 版本 2（T-N1-1，ADR-0030 决策 1；落地清单拍板 1/2/11）：新增 `category`/`derived_from`/
`clamp`/`conversion_ref`/`scope` 五个字段；`group`/`min`/`max`/`is_rating`/`rating_conversion_ref`
标废弃（保留一个版本周期不删，迁移链自动把旧字段值转换到新字段，两侧同时存在）。

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `stat.<name>` |
| `name_key` | TextKey | 是 | 显示名文本键 |
| `category` | Enum | 是 | `primary`\|`derived`\|`percent`\|`defense`\|`misc`；决定聚合轮次（`derived` 第二轮）与是否经换算层（`percent`）。v1 数据经 1→2 迁移由 `group` 映射而来 |
| `derived_from` | Array\<{stat: Reference(stat.definition), coefficient: Number}\> | 否，仅 `category=derived` 有意义 | 派生来源与系数；`coefficient` 允许负数（拍板 11），只经内建 `field_finite` 检查有限性，不登记下界 |
| `clamp` | Object {min?: Number, max?: Number} | 否 | 最终值取值区间，两轮聚合的第二轮之后夹取（T-N1-2 落地，本表只登记 schema）；v1 平级 `min`/`max` 经迁移嵌套进本字段 |
| `conversion_ref` | Id（引用 `stat.rating_conversion`） | 否，仅 `category=percent` 有意义 | 换算曲线引用，缺省恒等曲线；v1 数据经迁移由 `is_rating=true` 且带 `rating_conversion_ref` 映射而来 |
| `scope` | Enum | 否，缺省 `any` | `any`\|`from_player`\|`from_creature`；结算管线"目标乘区"步骤按来源类别匹配作用域（ADR-0030 决策 5） |
| `group`（废弃） | Enum | 否（T-N1-2 起放宽） | `primary`\|`secondary`\|`derived`\|`resistance`。v2 起由 `category` 取代；`StatHost.LoadDefinitions` 已改为无条件消费 `category`，不再读取本字段，因此本字段不再需要强制必填——填了仍要满足枚举取值合法 |
| `default_base` | Number | 否 | 未显式 `SetBase` 时的基础值，缺省 0 |
| `min`（废弃） | Number | 否 | 最终值下限（可空）。v2 起由 `clamp.min` 取代 |
| `max`（废弃） | Number | 否 | 最终值上限（可空）。v2 起由 `clamp.max` 取代 |
| `is_rating`（废弃） | Bool | 否 | 评级换算启用时，本属性是否先过曲线，缺省 false。v2 起由 `category=percent` 取代 |
| `rating_conversion_ref`（废弃） | Id（引用 `stat.rating_conversion`） | 否 | 曲线引用，仅 `is_rating=true` 时有意义。v2 起由 `conversion_ref` 取代 |
| `description` | String | 否 | 说明文字 |

与现有示例文件（`data/_sample/stat/stat.definition.json`，v1 写法：`id`/`name_key`/`group` 等）
兼容：v1 数据经 1→2 迁移自动补齐 `category`/`clamp`/`conversion_ref`，`games/_template/data/game/stat/stat.definition.json`
已升级为 v2 写法示例（直接写 `category`，`group` 仍需同时提供，理由同上表）。

`group→category` 迁移映射（`StatSchemas.MigrateDefinitionV1ToV2` 判断记录）：`resistance→defense`
（拍板 1 明文规定）；`primary`/`derived` 恒等；`secondary` 契约未给出显式映射，按 `is_rating` 取值
二选一分裂——`is_rating=true→percent`（评级换算候选，如既有样例 `crit_rating`/`dodge_rating`），
否则 `→misc`（如既有样例 `move_speed`）。这条分裂规则是本迁移的推断，**非契约条文明文规定**，
已在 T-N1-1 任务汇报标注"待设计层确认"。

校验（`core/StatDefinitionValidationRule.cs`，`data_registry` 内建字段级校验之外的扩展项）：

- `stat_min_max_order`：`min` 与 `max` 同时提供时，`min <= max`（沿用废弃字段的既有检查，不因
  `clamp` 新增而移除，`min`/`max` 本版本仍原样保留在数据里）；T-N1-2 起同一检查名扩展到嵌套
  `clamp.min`/`clamp.max`（两者同时提供时同样要求 `clamp.min <= clamp.max`）——只填新字段、
  不带平级 `min`/`max` 的纯 v2 记录此前会绕过这条检查，属于同一条"下限不得大于上限"语义
  不变量在两种承载字段形态下的完整覆盖，不新增检查名。
- `stat_definition_derivation_cycle`（`core/StatDefinitionDerivationCycleValidationRule.cs`，
  T-N1-2 新增，独立文件/独立规则，照抄 `core/numbers/archetype` 的
  `ArchTalentTreeCycleValidationRule` 写法）：`category=derived` 属性的 `derived_from[].stat`
  来源链不得成环，含属性以自身为来源的自环。Error 级，04 第 5 节数值类校验项分级表"派生
  无环"一行。
- `stat_rating_ref_requires_is_rating`：提供了 `rating_conversion_ref` 时，`is_rating` 必须为
  `true`（反向不要求——`is_rating=true` 而没有 `rating_conversion_ref` 是合法的，此时评级换算
  对该属性直通不换算，见 `StatHost.ConvertRating`）。
- `stat_definition_derived_from_requires_derived`（T-N1-1 新增）：提供了 `derived_from` 时，
  `category` 必须是 `derived`。检查名——04 第 5 节数值类校验项分级表未逐条列出本项（只列了"派生
  无环"一条），按任务派发提示词"契约未给检查名，用 `stat_definition_*` 前缀"命名，**待设计层
  确认**。
- `stat_definition_conversion_ref_requires_percent`（T-N1-1 新增）：提供了 `conversion_ref` 时，
  `category` 必须是 `percent`。检查名同上一条判断记录，**待设计层确认**。

**T-N1-4 判断记录（职业派生系数覆盖不是 `stat.definition` 自己的字段）**：ADR-0030 决策 2"职业
模板可覆盖派生系数"落地为 `arch.class.derivation_overrides`（`Core.Numbers.Archetype` 模块自己的
表字段，见该模块 `schema/README.md`），不是本表新增字段——`stat.definition.derived_from` 只登记
"默认系数"，"某个职业把这条系数覆盖成多少"是职业模板的属性，不是属性定义本身的属性，因此不在
`stat.definition` schema 里加字段，而是在 `StatHost` 新增按单位的运行期覆盖层（`Set
DerivationCoefficientOverrides`/`ClearDerivationCoefficientOverrides`，见 README"T-N1-4"一节）。

## `stat.rating_conversion`

schema 版本 2（不变）。T-N1-3（ADR-0030 决策 3；04 第 3.6 节；数值设计 01 第 5 节"三种曲线
形态"）：新增可选 `saturation` 字段，与 `entries` 二选一——纯新增可选字段/放宽必填不改变已有
合法数据判定结论，不升级 schema 版本。

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `stat.rating.<name>` |
| `entries` | Array | 否（T-N1-3 起放宽，与 `saturation` 二选一） | 通用断点表 `[{x: Int, y: Number}, ...]`（04 第 3.6 节 `CurveSchema.BreakpointsField`，横轴 `Level`）："标准版：等级索引除数"形态：`x` 是**单位等级**（见下方判断记录），不是评级原始值；`y` 是该等级下每 1% 效果所需点数（登记 `> 0`）。schema 版本 2（T-N0-4）：v1 的 `{level, points_per_percent}` 由 1→2 迁移环节改名，旧数据文件无需手改即可加载；`curve_monotonic_finite` 规则要求非空、有限、`x` 无重复、`y` 不递减（除数随等级不递减，数值总纲原则 1） |
| `saturation` | Object | 否（T-N1-3 新增，与 `entries` 二选一） | `{k: Number(必填,>0), cap?: Number(可选,>0,缺省 1)}`（04 第 3.6 节 `CurveSchema.SaturationField`，形态 `Saturation`）："变态版：饱和曲线，除数随等级增长"形态：`输出 = rawValue / (rawValue + k × 单位等级)`，以 `cap` 封顶；与 `combat.resist_curve` 饱和分支（`ResistCurveKind.Saturation`）同形态（ADR-0030 决策 3） |

`entries`/`saturation` 数组/对象内层结构由 ADR-0019 子结构登记在加载期检查
（`required_field`/`field_type`/`field_range`，路径形如 `entries[0].y`/`saturation.k`）；
`StatHost` 构造期解析（`ParseConversion`/`ParseCurve`/`ParseSaturation`）仍做一道结构兜底
（缺字段/类型不对直接抛 `InvalidOperationException`，因为这属于内容数据的结构性错误，不是
运行期可恢复的场景），并对未经迁移直接构造的记录接受 v1 的 `{level, points_per_percent}`
元素名（T-N0-4 禁止删除旧字段读取路径）。一条记录必须**恰好**登记 `entries`/`saturation`
之一，由 `StatRatingConversionValidationRule`（检查名
`stat_rating_conversion_requires_one_shape`，Error 级，04 第 5 节分级表未逐条列出，按
`stat_*` 前缀命名，**待设计层确认**）强制；两者都缺或都填时 `ParseConversion` 仍有加载期防御
（都缺抛异常；都填时优先 `entries`），但正常数据不应触发这条防御路径。

判断记录（2026-09-05，设计层裁定，取代原判断记录）：`entries[].level` 就是**单位等级**，插值
时的自变量是 `StatHostOptions.LevelLookup(unitId)` 查到的等级；`rawValue`（`base + Σflat`，
"待换算的评级原始值本身"）只在插值算出 `points_per_percent` 之后作被除数
（`percent = rawValue / pointsPerPercent`），不参与插值本身。`LevelLookup` 为 `null` 时按 1 级
处理。详见 `core/StatHost.cs` 的 `ConvertRating` 方法注释与 `README.md`"换算层"一节。

判断记录（T-N1-3）：换算层触发条件与曲线形态本身现已互相独立——`stat.definition.category
== "percent"` 决定"是否经过换算层"，`conversion_ref` 缺省决定"经过的是恒等曲线"，
`conversion_ref` 指向的 `stat.rating_conversion` 记录登记了哪种形态（`entries`/
`saturation`）决定"用哪条公式"。三者组合覆盖 ADR-0030 决策 3 的三种曲线形态：`conversion_ref`
缺省 = 恒等（保守版）；引用 `entries` 记录 = 按等级除数（标准版）；引用 `saturation` 记录 =
饱和（变态版）。

## `stat.weight`

分阶段落地计划 T-N1-5（ADR-0030 决策 7；04 第 1.1 节表清单 `stat.weight` 行）：新表，schema
版本 1。属性权重表——属性 id → 权重（一点主属性当量），可按职业覆盖；消费者是装备预算消耗
（ADR-0032）、技能控制/增益价值（ADR-0031）与装备评分/自动配装，均不在本任务范围内落地。
**`StatHost` 不读取本表**（06 第 1 节明文规定，`core/StatHost.cs` 全文不出现字符串
`"stat.weight"`，由 `tests/StatWeightSchemaTests.cs` 源码扫描 + 行为双重测试防御）。

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `stat.weight.<name>`，惯例上与 `stat` 字段指向的属性同名（非强制） |
| `stat` | Id（引用 `stat.definition`） | 是 | 本条权重登记的目标属性 |
| `weight` | Number，`>= 0` | 是 | 一点该属性相当于多少点主属性当量；未被 `class_overrides` 命中的职业使用本值 |
| `class_overrides` | Array\<{class: Reference(arch.class), weight: Number（`>= 0`）}\> | 否 | 按职业覆盖权重，子结构见下方判断记录 |
| `description` | String | 否 | 说明文字 |

判断记录（一条记录对应一个属性，`stat` 字段显式登记目标，不靠解析 `id` 字符串）：与
`stat.rating_conversion`（每条记录是一条独立曲线，供 `conversion_ref` 按需引用）同一记法——
`derived_from[].stat`/`DerivedFromEntrySchema` 判断记录"字段名取自契约原文，不拿 id 的字符串
结构表达语义"的一贯做法在本表的延伸。

判断记录（`class_overrides` 子结构形态：`Array<{class, weight}>`，不是 `Map<arch.class id,
Number>`）：ADR-0030 决策 7、06 第 1 节、数值设计 01 第 128 行均只有一行描述，未展开子结构
形态——04 第 1.1 节总索引同样只有一行描述。任务派发提示词给出两个候选（`Map` 或 `Array`），
要求"与 T-N1-4 的 `derivation_overrides` 形态保持同一风格"；`derivation_overrides` 选
`Array` 的原始理由（一条覆盖要同时定位两个 `stat.definition` 引用，`Map` 键承载不下）在本字段
不成立（本字段只需单一职业维度，`Map<arch.class id, Number>` 同 `ArchSchemas.Class.base_stats`
的 `MapSchema.ReferenceKeyTable` 惯例其实同样可行）——选 `Array` 是遵照任务派发提示词"与
derivation_overrides 同一风格"的显式指示，统一子结构记法，不是本字段独立推导的必然结论。**已在
任务汇报标注"待设计层确认"**，与 `ArchSchemas.DerivationOverrideEntrySchema` 同一处理口径。

判断记录（`weight`/`class_overrides[].weight` 范围约束 `>= 0`，不像 `derived_from[].coefficient`
那样允许负数）：契约未明文规定范围（不像拍板 11 对 `coefficient` 明文"允许负数"）。但 07 第 1.2
节、ADR-0032 决策 3 已把消费公式写死为 `实际消耗 = (Σ(属性值_i × 权重_i)^k)^(1/k)`，`k` 默认
1.5（非整数）——负权重可能让括号内求和为负，对非整数指数在实数域无定义；`0` 是"该属性不计入
预算消耗"的合法表达。登记 `WithRange(FieldRange.Range(min: 0))`（含 0、不含负数），比照
`RatingConversion.entries[].y`"登记范围据下游公式推断、非契约条文明文规定"同一处理口径。
**已在任务汇报标注"待设计层确认"**——若设计层裁定允许负权重，去掉这条 `WithRange` 调用即可，
不影响其余 schema 结构。
