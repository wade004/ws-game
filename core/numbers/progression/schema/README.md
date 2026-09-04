# progression 数据表字段说明

对应 [04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 第 1.1 节表清单：
`prog.level_curve`"等级到所需经验、到属性成长系数的曲线表"、`prog.xp_source`"经验来源 id 到
经验值与限制规则"。**判断记录：04 未给出这两张表的字段表**，以下字段为实现期按任务书 T2-3
给出的最小字段集补录，待 04 正式登记时以 04 为准同步本文件。

## `prog.level_curve`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `prog.curve.<name>` |
| `max_level` | Int | 是 | 曲线最大等级，必须等于 `entries` 的元素个数（见下方校验规则） |
| `entries` | Array | 是 | `Array<{level:Int, xp_to_next:Int, growth:Object<stat_id,Number>}>`，`level` 从 1 连续到 `max_level`；最后一级的 `xp_to_next` 按约定为 0（无下一级） |

`entries` 声明为 `FieldKind.Array`（04 第 5 节"结构未知/由上层模块自行解释的 JSON 数组，本模块
只做存在且是数组检查"），元素内部结构由 `Core.Numbers.Progression.ProgressionHost` 与
`ProgLevelCurveValidationRule` 自行解析——04 的 `DataRecord`/`TableSchema` 没有"数组元素的
嵌套 schema"机制，这与 `arch.talent_tree.nodes`（见 archetype 模块 schema）是同一类处理方式。

`growth` 的 key 是属性 id（`stat.*`，供 `StatModifierWriter` 使用），value 是该级相对上一级的
成长增量；1 级（`level=1`）的 `growth` 即便存在也不参与累计（任务书原文"1 级无成长"）。

**校验规则**（`ProgLevelCurveValidationRule`，check 名 `level_curve_entries`/
`level_curve_continuity`）：`entries.Count == max_level`；`entries[i].level == i+1`（0 基下标）。

## `prog.xp_source`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `prog.xp.<name>` |
| `base_xp` | Int | 是 | 基础经验值 |
| `weight` | Number | 否 | 省略时按 1 处理 |
| `condition` | Expr | 否 | 触发条件；本任务只登记字段类型（供未来 `expr_parsable` 校验使用），`IProgressionHost` 不对其求值 |

## 判断记录

- **未把 `primary_stat`/`power_types` 一类跨模块引用声明为 `FieldKind.Reference`**：本表
  两张表都不涉及跨模块引用，此条不适用（对照 archetype 模块 schema/README.md 的同名判断记录，
  以免误读为遗漏）。
- **`condition` 只登记类型不求值**：见模块 README 判断记录 2；本模块不依赖 `Core.Foundation.Expr`
  的求值 API，只用 `FieldKind.Expr` 让 04 的 `field_type`/`expr_parsable` 校验项能识别该字段。
