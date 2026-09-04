# ai 数据表字段说明

对应 [04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 第 1.1 节表清单：
`ai.rotation`"优先级表：条件到候选技能的有序列表"、`ai.behavior_profile`"行为外壳参数：感知半径、
巡逻路径引用、脱战规则"、`ai.patrol_path`"巡逻路径：有序 Vec2 列表 + `loop`\|`pingpong` 模式"，以及
[06_规则层_属性技能战斗AI.md](../../../../architecture/06_规则层_属性技能战斗AI.md) 第 6.1～6.3 节。

**判断记录：04/06 均未给出这三张表的完整字段表**（04 只有一句话用途描述；06 §6.1 只列出
`ai.behavior_profile` 的字段名，未给类型/必填性；`ai.rotation`/`ai.patrol_path` 的字段结构只在
06 §6.2/6.3 以伪代码/散文形式出现）。以下字段为实现期按任务书 T2-10 给出的最小字段集补录，待 04/06
正式登记完整字段表时以其为准同步本文件。

## `ai.behavior_profile`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `ai.profile.<name>` |
| `perception_radius` | Number | 是 | 感知半径，`idle`/`patrol` → `chase` 判定用 |
| `patrol_path_ref` | Optional&lt;Reference→`ai.patrol_path`&gt; | 否 | 巡逻路径引用；缺省表示该单位无巡逻路径，默认态为 `idle` |
| `leash_range` | Number | 是 | 脱战追击上限距离，`chase`/`flee` → `return` 判定用 |
| `flee_hp_pct_threshold` | Optional&lt;Number&gt;（0~1） | 否 | 血量低于该比例且策略允许时 `combat` → `flee`；缺省表示不启用 `flee` |
| `combat_return_policy` | Enum（`return_to_spawn`\|`stay`\|`patrol`） | 是 | `return` 状态到达后的去向，`stay` 见架构 README 判断记录 |
| `rotation_ref` | Reference→`ai.rotation` | 是 | `combat` 态执行的优先级表；可经 `IAiHost.SetRotation` 运行期替换（Boss 阶段换表，06 §6.4） |
| `decision_interval` | Number | 否，缺省 `0.5` | `combat` 态求值优先级表的节奏（模拟时间单位） |
| `transitions` | Optional&lt;Object&gt; | 否 | 覆盖默认转移条件：键为转移名，值为 Expr 文本；见架构 README"默认转移条件 ↔ Expr 覆盖对照表" |

## `ai.rotation`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `ai.rotation.<name>` |
| `entries` | Array of `{priority: Int, condition: String(Expr), skill_id: Id}` | 是 | 有序 `RotationEntry` 列表；`priority` 在同一张表内不得重复（`AiContentValidationRule` 校验），执行时按 `priority` 从高到低遍历；`condition` 用 `self`/`target`/`combat`/`enemies` 分组（06 §6.2） |

## `ai.patrol_path`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `ai.path.<name>` |
| `points` | Array of `{x: Number, y: Number}` | 是 | 有序路径点列表，至少 2 个点（`AiContentValidationRule` 校验） |
| `mode` | Enum（`loop`\|`pingpong`） | 是 | `loop`：到终点跳回起点；`pingpong`：到端点折返方向 |

## 校验

`AiSchemas` 声明的 `entries`/`points`/`transitions` 均为 `FieldKind.Array`/`FieldKind.Object`，
DataRegistry 通用校验只检查"存在且是数组/对象"，不深入检查元素结构。`AiContentValidationRule`
（`core/AiContentValidationRule.cs`）补充以下模块专属检查（需要调用方显式
`registry.RegisterValidationRule(new AiContentValidationRule())` 注册）：

- `ai.rotation.entries`：`priority` 同表内唯一；每个元素含数值型 `priority`、字符串型 `condition`
  （且可被 `ExprParser` 解析、`ExprValidator` 静态校验通过）、字符串型 `skill_id`。
- `ai.behavior_profile.flee_hp_pct_threshold`：若提供，必须落在 `[0,1]` 区间。
- `ai.behavior_profile.transitions`：各值必须是字符串且可被解析、静态校验通过。
- `ai.patrol_path.points`：至少 2 个点。

## Expr 词汇表覆盖面

`ai.rotation.entries[].condition` 与 `ai.behavior_profile.transitions` 里的 Expr 文本用本模块自带的
`AiExprSchema`（`core/AiExprSchema.cs`）解析——只登记 04 第 6.2 节"宿主引用分组"表格里与 AI 直接
相关、且文档已给出示例引用的最小词汇集合（`self.hp_pct`、`target.hp_pct`、`target.faction`、
`combat.in_combat`、`combat.cast_school`、`enemies.count_in_range(Int)`、`time.since_combat_start`、
`time.day_cycle`、`time.turn_index`、`time.round_index`、`time.is_my_turn`）。`IExprHostFactory`
的默认实现（把这些 key 接到具体宿主契约上）属于集成任务，不在本任务范围（见
`core/rules/common/README.md`"不负责什么"）；内容作者若使用了本表未登记的 `group.key` 组合，解析
会失败（详见 `AiExprSchema.cs` 判断记录），后续集成任务可直接在该文件追加 `Register` 调用扩展词汇表。
