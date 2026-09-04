# stat_block 数据表字段说明

对应 `architecture/04_数据与内容管线.md` 第 1.1 节表清单 `stat.definition`/
`stat.rating_conversion` 两行；本文件是该总索引在本模块内的字段展开（04 第 1.1 节
"每份下游文档在讲对应模块时会重复给出该模块表的详细字段"）。`TableSchema` 实现见
`core/StatSchemas.cs`。

## `stat.definition`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `stat.<name>` |
| `name_key` | TextKey | 是 | 显示名文本键 |
| `group` | Enum | 是 | `primary`\|`secondary`\|`derived`\|`resistance` |
| `default_base` | Number | 否 | 未显式 `SetBase` 时的基础值，缺省 0 |
| `min` | Number | 否 | 最终值下限（可空，与 `max` 一起在聚合的最后一步夹取） |
| `max` | Number | 否 | 最终值上限（可空） |
| `is_rating` | Bool | 否 | 评级换算启用时，本属性是否先过曲线，缺省 false |
| `rating_conversion_ref` | Id（引用 `stat.rating_conversion`） | 否 | 曲线引用，仅 `is_rating=true` 时有意义 |
| `description` | String | 否 | 说明文字 |

与现有示例文件（`data/_sample/stat/stat.definition.json`，只含 `id`/`name_key`/`group`）
兼容：新增字段全部可选。

校验（`core/StatDefinitionValidationRule.cs`，`data_registry` 内建字段级校验之外的扩展项）：

- `stat_min_max_order`：`min` 与 `max` 同时提供时，`min <= max`。
- `stat_rating_ref_requires_is_rating`：提供了 `rating_conversion_ref` 时，`is_rating` 必须为
  `true`（反向不要求——`is_rating=true` 而没有 `rating_conversion_ref` 是合法的，此时评级换算
  对该属性直通不换算，见 `StatHost.ConvertRating`）。

## `stat.rating_conversion`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `stat.rating.<name>` |
| `entries` | Array | 是 | `[{level: Int, points_per_percent: Number}, ...]`，按 `level` 升序 |

`entries` 数组内层结构不由 `data_registry` 的 `field_type` 校验项深入检查（`FieldKind.Array`
只保证"是数组"），由 `StatHost` 构造期解析时做结构校验（缺字段/类型不对直接抛
`InvalidOperationException`，因为这属于内容数据的结构性错误，不是运行期可恢复的场景）。

判断记录：`entries[].level` 的含义是曲线自己的插值自变量断点，插值时的自变量取值是
"待换算的评级原始值本身"（`base + Σflat`），不是角色等级——详见
`core/StatHost.cs` 的 `ConvertRating` 方法注释与 `README.md`"评级换算"一节。
