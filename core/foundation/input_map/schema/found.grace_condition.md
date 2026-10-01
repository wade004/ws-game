# `found.grace_condition` 字段说明

宽限条件登记表（手感设计/01 第 2.4 节，手感落地 S1）：声明"某个条件刚刚不再成立后，在宽限窗口内仍视为成立"所指的条件。
框架只提供机制，**不预置任何条件行**；条件 id 由游戏数据声明、被 `found.input_action.grace_conditions` 引用。

## 判断记录：登记表，主键字段名 `key`

与 `found.input_action` 同理：表名 domain 前缀是 `found`，记录 id 域名是 `input`（如 `input.grace.target_in_range`），
按登记表处理（`IsRegistryTable = true`，主键字段名 `key`，不做 domain 前缀检查）。

## 字段

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `key` | Id | 是 | 条件 id，如 `input.grace.target_in_range` |
| `expr` | Expr | 是 | 条件表达式，在行动者上下文里求值（求值由宿主经 `IGraceConditionEvaluator` 提供） |
| `description` | String | 否 | 说明文字 |

宽限时长不在本表：取行动者手感档案输入组的 `grace_ms`（`FeelCalibration` 按模拟步长换算为 tick）。机制、时间基准与限制见
`core/foundation/input_map/README.md`"手感落地 S1"一节。
