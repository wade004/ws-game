# `found.grace_condition` 字段说明

宽限条件登记表（手感设计/01 第 2.4 节，手感落地 S1）：声明"某个条件刚刚不再成立后，在宽限窗口内仍视为成立"所指的条件。
框架提供机制，并在 `data/_framework/found/found.grace_condition.json` 内置三条以施法瞄点为准的条件行（`input.grace.builtin_aim_in_range`、`input.grace.builtin_aim_line_of_sight`、`input.grace.builtin_aim_reachable`，见下节）；
不被任何动作引用就不采样、无任何效果。游戏自己的条件 id 由游戏数据声明、被 `found.input_action.grace_conditions` 引用。

## 判断记录：登记表，主键字段名 `key`

与 `found.input_action` 同理：表名 domain 前缀是 `found`，记录 id 域名是 `input`（如 `input.grace.target_in_range`），
按登记表处理（`IsRegistryTable = true`，主键字段名 `key`，不做 domain 前缀检查）。

## 字段

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `key` | Id | 是 | 条件 id，如 `input.grace.target_in_range` |
| `expr` | Expr | 是 | 条件表达式，在行动者上下文里求值：生产装配缺省用框架的 `ExprGraceConditionEvaluator`（`self` 分组即行动者，`target` 分组为这次求值的目标：游戏的 `GraceTargetResolver` 覆盖 -> 施法请求携带的目标 -> 自动攻击的当前目标；`event` 分组为施法瞄点上下文），游戏不写代码即可使用；也可经 `CarriersFeelOptions.GraceEvaluator` 覆盖 |
| `description` | String | 否 | 说明文字 |

宽限时长不在本表：取行动者手感档案输入组的 `grace_ms`（`FeelCalibration` 按模拟步长换算为 tick）。机制、时间基准与限制见
`core/foundation/input_map/README.md`"手感落地 S1"一节。

## 框架内置条件行（`data/_framework/found/found.grace_condition.json`，手感落地 M4-G）

| 条件 id | 表达式 | 含义 |
|---|---|---|
| `input.grace.builtin_aim_in_range` | `event.aim_in_range` | 施法瞄点（请求携带的目标或落点，没有时取缺省目标）在这次施法射程内 |
| `input.grace.builtin_aim_line_of_sight` | `event.aim_line_of_sight` | 行动者到瞄点之间视线畅通 |
| `input.grace.builtin_aim_reachable` | `event.aim_in_range and event.aim_line_of_sight` | 射程与视线同时满足，对应步骤 7 的两项 |

`event` 分组字段（`GraceAimContext`，snake_case，camelCase 等价）：`has_aim`、`aim_distance`、`aim_range`、`aim_in_range`、`aim_line_of_sight`、`aim_is_ground`。
游戏在动作的 `grace_conditions` 里直接引用即可；不引用则不采样，与引入之前逐位一致。游戏自己的条件表达式同样可以读这些字段。
