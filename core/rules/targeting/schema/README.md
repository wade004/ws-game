# `target.chain_def` 字段说明

对应 [06_规则层_属性技能战斗AI.md](../../../../architecture/06_规则层_属性技能战斗AI.md) 第 5
节 `TargetChainDef` 结构。`TableSchema` 定义见 `../contracts/TargetSchemas.cs`；强类型解析结果
见 `../core/TargetChainDef.cs`。

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | 形如 `target.chain.<name>`。 |
| `source` | String | 是 | 目标来源策略名。必须已在 `TargetStrategyRegistry` 登记，内置六种见 `BuiltinTargetStrategies`：`current_target`\|`nearest_in_shape`\|`self`\|`party_lowest_hp_pct`\|`threat_top`\|`all_in_shape`，游戏层可注册更多。**判断记录**：不声明为 `Enum`——`FieldKind.Enum` 要求取值集合在 schema 构造期就固定，与"游戏层可扩展注册"矛盾；改用运行期可扩展的 `ChainDefValidationRule(strategyRegistry.Names)` 做等价校验。 |
| `shape` | Object，可空 | 否 | `{kind: circle\|cone\|line\|rect, radius, angle, length, width}`。**判断记录**：05 第 3.5 节 `Shape` 联合类型的完整参数还包含 `origin`（circle 的 center、cone/line/rect 的 origin）与 `direction`/`rotation`，本表故意不登记这两组——链是可被多个施法者复用的模板，`origin`/`direction` 由 `TargetHost` 在每次 `Resolve` 时用施法者当前坐标/朝向重新锚定（与 common/README.md 判断记录 2 "`ISkillHost.FindUnits` 的 `origin` 参数锚定形状模板"同一模式）。`rect` 的 `halfExtents` 字段名也不在清单内，复用 `length`/`width`（与 `line` 共享字段名）换算为半宽高（各自除 2），见 `TargetChainDef.ParseShape` 内联判断记录。 |
| `filters` | Array of String，可空 | 否 | 每个元素是一段 Expr 文本，或以下内置简写之一：`relation:hostile`\|`relation:friendly`\|`relation:neutral`\|`relation:not_self`、`alive`、`tag:<id>`。**判断记录**：数组内全部元素取 AND（见 `TargetHost.PassesFilter` 上方注释）。Expr 文本按 `self`/`target` 两个分组求值（`self` = 施法者、`target` = 候选，经 `IExprHostFactory.CreateFor(caster, candidate, null)`）。 |
| `sort_by` | Object，可空 | 否 | `{key: distance\|hp_pct\|threat\|level, direction: asc\|desc}`；省略 `direction` 按 `asc`；省略整个字段时保留来源策略给出的"自然顺序"（不额外排序）。 |
| `max_targets` | Int，可空 | 否 | 默认 1；0 表示不限。 |
| `overflow_policy` | Enum，可空 | 否 | `truncate`（默认，截断）\|`split`（平摊）\|`cap`（总量封顶）——候选数超过 `max_targets` 时的处理策略（T-N3-8，ADR-0031 决策 6，06 第 3.7 节 2026-09-14 修订段）。**设计层裁定（2026-09-15）：采纳**：06 原文只给出三个策略名字与默认值，未展开到"每个目标分配系数"的精确公式；本字段三个取值与 `Core.Rules.Common.TargetOverflowPolicy` 枚举一一对应，系数定义见该类型 XML 文档——`truncate` 与本字段引入之前的既有截断行为逐一对应（按既有排序取前 `max_targets` 个，系数恒 1）；`split` 候选全部命中、总量守恒为"`max_targets` 个目标的满额值"（系数 = `max_targets` / 命中数）；`cap` 候选全部命中、总量硬封顶为"单个目标的满额值"（系数 = 1 / 命中数）。三态精确系数由 `ITargetHost.ResolveWithCoefficients`（`TargetHost.ApplyOverflowPolicy`）计算，本字段本身只是策略选择器；未超过 `max_targets`（或 `max_targets` 为 0 不限）时本字段不参与，全部候选命中、系数恒为 1。**判断记录（独立生效，不要求与 `max_targets` 成对声明）**：本字段可以只声明其中一个——只声明 `overflow_policy` 而不声明 `max_targets` 时，`max_targets` 沿用缺省值 1，等价于"最多 1 个目标不算超出"。 |
| `fallback` | Reference → `target.chain_def`，可空 | 否 | 候选为空时改用的另一条链；`ChainDefValidationRule` 在数据校验期检测成环，`TargetHost` 在运行期额外用 `TargetingOptions.MaxFallbackDepth`（默认 8）兜底防御。 |

## 校验规则

`ChainDefValidationRule`（`core/ChainDefValidationRule.cs`，需调用方显式
`registry.RegisterValidationRule(new ChainDefValidationRule(strategyRegistry.Names))` 才生效）：

1. `source` 必须在传入的已知来源名单里（`target_source_unknown`）。
2. `filters` 数组每个元素若不是内置简写，须能被 `ExprParser.Parse` 解析（`target_filter_expr_parsable`）。
   **判断记录**：任务书原句"Expr 可解析（数据注册表已做）"是针对 `FieldKind.Expr` 标量字段的一般
   表述；`filters` 是 `Array`，`DataRegistry` 内置的 `expr_parsable` 检查项不逐元素解析数组（见
   `DataRegistry.ValidateRecordField` 的 `FieldKind.Expr` 分支只处理标量），因此这项检查由本规则
   自己跑一遍 `ExprParser.Parse` 完成，不是零工作量。
3. `fallback` 链不得成环（`target_chain_fallback_cycle`，简单的"单出边"环检测，见 `HasFallbackCycle`）。

`fallback` 引用目标是否存在，由 `FieldSchema.ReferenceTable = "target.chain_def"` 触发
`DataRegistry` 内置的 `reference_integrity` 检查，不在本规则重复实现。
