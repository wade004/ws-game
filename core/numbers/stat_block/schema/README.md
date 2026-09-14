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
| `group`（废弃） | Enum | 是 | `primary`\|`secondary`\|`derived`\|`resistance`。v2 起由 `category` 取代；仍必填——`StatHost.LoadDefinitions`（T-N1-2 落地）目前仍无条件 `GetString("group")`，放宽为可选要等该处读取逻辑改为消费 `category` 之后 |
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
  `clamp` 新增而移除，`min`/`max` 本版本仍原样保留在数据里）。
- `stat_rating_ref_requires_is_rating`：提供了 `rating_conversion_ref` 时，`is_rating` 必须为
  `true`（反向不要求——`is_rating=true` 而没有 `rating_conversion_ref` 是合法的，此时评级换算
  对该属性直通不换算，见 `StatHost.ConvertRating`）。
- `stat_definition_derived_from_requires_derived`（T-N1-1 新增）：提供了 `derived_from` 时，
  `category` 必须是 `derived`。检查名——04 第 5 节数值类校验项分级表未逐条列出本项（只列了"派生
  无环"一条），按任务派发提示词"契约未给检查名，用 `stat_definition_*` 前缀"命名，**待设计层
  确认**。
- `stat_definition_conversion_ref_requires_percent`（T-N1-1 新增）：提供了 `conversion_ref` 时，
  `category` 必须是 `percent`。检查名同上一条判断记录，**待设计层确认**。

## `stat.rating_conversion`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `stat.rating.<name>` |
| `entries` | Array | 是 | 通用断点表 `[{x: Int, y: Number}, ...]`（04 第 3.6 节 `CurveSchema.BreakpointsField`，横轴 `Level`）：`x` 是**单位等级**（见下方判断记录），不是评级原始值；`y` 是该等级下每 1% 效果所需点数（登记 `> 0`）。schema 版本 2（T-N0-4）：v1 的 `{level, points_per_percent}` 由 1→2 迁移环节改名，旧数据文件无需手改即可加载；`curve_monotonic_finite` 规则要求非空、有限、`x` 无重复、`y` 不递减（除数随等级不递减，数值总纲原则 1） |

`entries` 数组内层结构由 ADR-0019 子结构登记在加载期检查（`required_field`/`field_type`/`field_range`，路径
形如 `entries[0].y`）；`StatHost` 构造期解析（`ParseCurve`）仍做一道结构兜底（缺字段/类型不对直接抛
`InvalidOperationException`，因为这属于内容数据的结构性错误，不是运行期可恢复的场景），并对未经迁移
直接构造的记录接受 v1 的 `{level, points_per_percent}` 元素名（T-N0-4 禁止删除旧字段读取路径）。

判断记录（2026-09-05，设计层裁定，取代原判断记录）：`entries[].level` 就是**单位等级**，插值
时的自变量是 `StatHostOptions.LevelLookup(unitId)` 查到的等级；`rawValue`（`base + Σflat`，
"待换算的评级原始值本身"）只在插值算出 `points_per_percent` 之后作被除数
（`percent = rawValue / pointsPerPercent`），不参与插值本身。`LevelLookup` 为 `null` 时按 1 级
处理。详见 `core/StatHost.cs` 的 `ConvertRating` 方法注释与 `README.md`"评级换算"一节。
