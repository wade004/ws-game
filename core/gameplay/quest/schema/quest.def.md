# `quest.def` 字段表

对应 `01_分层与依赖.md` L4 模块表 `任务` 行（目录 `core/gameplay/quest`，契约 `QuestHost`）的主要
数据表、`08_玩法层_掉落任务对话关卡.md` 第 2.1 节 `quest.def` 字段表。本文件是该字段表在代码层面的
分片副本，逐字段核对代码实现（`core/gameplay/quest/core/QuestSchemas.cs` 的 `TableSchema` 声明 +
`core/gameplay/quest/contracts/QuestDefinition.cs` 的 `FromRecord` 解析逻辑）与 08 原文的一致性，并
如实记录二者的差异。

## 字段

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `quest.<name>`。 |
| `title_key` | TextKey（Id） | 是 | 任务标题文本键。**代码注释称"08 原文未列出，任务书拍板补录"——见下方"与 08 原文的差异"，实际当前版本 08 第 2.1 节字段表已收录该字段。** |
| `description_key` | TextKey（Id） | 否 | 任务描述文本键。代码注释同 `title_key` 的说法（"08 原文未列出"），实际当前版本 08 已收录，见下方说明。 |
| `objectives` | Array\<QuestObjective\> | 是 | 目标数组，至少一条（`QuestDefinition` 构造期强制非空），结构见下"QuestObjective 结构"。 |
| `prerequisite` | Expr | 否 | 前置条件（等级、已完成任务、世界标志等）；未声明时视为恒真（无前置）。 |
| `exclusive_group` | Id | 否 | 互斥组：同组任务同时只能激活/完成一个（`QuestHost.Accept` 校验）。 |
| `start_method` | Enum | 是 | `npc_gossip｜item_use｜area_trigger｜auto`，起始方式。 |
| `turn_in_method` | Enum | 是 | `npc_gossip｜auto`，交付方式。 |
| `rewards` | Object | 否 | `{items, xp, currency, skills, world_flags, talent_points}`；结构见 `core/gameplay/common/README.md` 与该模块 `RewardBundle` 类型（本表字段本身由 `Core.Gameplay.Common.RewardSchemaFields.Rewards(required: false)` 登记，非本模块自己重复声明）。 |
| `repeatable` | Enum | 是 | `none｜daily｜unlimited`，可重复性。 |

## `QuestObjective` 结构

`objectives[]` 的元素（08 第 2.1 节 `QuestObjective` 结构，字段名按 `QuestDefinition.ParseObjective`
的 JSON 键名）：

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `type` | Enum | 是 | `kill｜collect｜interact｜explore｜escort｜event｜cast｜talk`，见下"目标类型对照表"。 |
| `target_ref` | Id | 是 | 依 `type` 指向不同域名的目标，见下对照表。 |
| `count` | Int | 是 | 需求数量，必须为正数；`explore`/`escort`/`talk` 三种类型恒为 1（`QuestObjective` 构造期强制）。 |
| `param` | Object | 否 | 各类型的额外参数，见下对照表"param 要点"列。 |
| `description_key` | Id | 否 | 该条目标自身的描述文本键，供任务日志 UI 展示每条目标的文案。**代码注释称"08 原文 `QuestObjective` 结构未列出，任务书拍板补录"——见下方"与 08 原文的差异"，实际当前版本 08 第 2.1 节 `QuestObjective` 结构原文已含 `descriptionKey: Optional<Id>`。** |

## 目标类型对照表

（08 第 2.1 节表；`target_ref` 域名与 `count` 恒为 1 的判定见
`core/gameplay/quest/contracts/QuestObjectiveType.cs` 的 `QuestObjectiveTypes.RequiredTargetDomain`/
`RequiresCountOne`）

| `type` | `target_ref` 指向 | `count` 含义 | `param` 要点 |
|---|---|---|---|
| `kill` | `creature.template` | 需击杀数量 | 无 |
| `collect` | `item.template` | 需持有数量 | `consume_on_progress: Bool`（进度是否随拾取即时消耗物品） |
| `interact` | `gobj.template` | 需交互次数 | 无 |
| `explore` | `area.trigger_def`（`quest_explore` 类型） | 恒为 1 | 无 |
| `escort` | `creature.template`（被护送对象） | 恒为 1 | `escort_route_ref`（路径引用） |
| `event` | 任意事件类型 | 视事件语义 | `eventFilter: Expr` |
| `cast` | `skill.def` | 需施放次数 | 无 |
| `talk` | `dialog.gossip_menu` 或 `dialog.story_tree` 节点 | 恒为 1 | 无 |

判断记录（代码与 08 字面的一处偏差，见 `QuestObjectiveTypes.RequiredTargetDomain` 注释）：`talk`
类型的 `RequiredTargetDomain` 返回 `null`（不做域名强制校验），因为目标既可以是 `dialog.gossip_menu`
的 `id`，也可以是 `dialog.story_tree` 某个节点的 `id`（节点 id 由内容作者自行命名，不强制
domain 段等于 `"dialog"`）——是否真正匹配留给 `QuestHost` 运行期按实际触发的 gossip/story 事件字段
判定，不在内容校验期做域名检查。

## 与 08 原文的差异

代码内多处注释（`QuestSchemas.cs`、`QuestDefinition.cs`、`QuestObjective.cs`）称 `title_key`/
`description_key`（`quest.def` 级）与 `objectives[].description_key` 是"08 原文未列出，任务书拍板
补录"的字段。经核对当前仓库内 `architecture/08_玩法层_掉落任务对话关卡.md` 第 2.1 节，这三个字段
**均已经出现在正式字段表 / `QuestObjective` 结构原文中**（`title_key` 标注必填、`description_key`
标注选填，`QuestObjective` 结构块含 `descriptionKey: Optional<Id>`）。也就是说代码实现与 08 文档
当前版本在字段集合上是**一致的、没有实际差异**；代码注释里"08 原文未列出"的说法很可能是 08 文档
在该模块实现之后经历过一轮勘误补充、代码侧注释未同步更新所致——这是一处文档与代码注释之间的表述
不一致，不影响运行期行为，本文档如实记录，不代为修改 08 或 quest 模块源码（均不在本次任务范围）。

## 本模块不做什么

- 不校验 `exclusive_group` 引用的其它任务 id、`prerequisite` 引用的其它任务/世界标志是否真实存在——
  这类跨记录引用完整性检查留给通用的 `reference_integrity` 校验项（04 第 5 节），
  `QuestContentValidationRule` 只做解析期能发现的问题（字段格式、`type`/域名匹配、Expr 语法）。
- 不实现"多选一奖励"（08 第 2.4 节 `RewardChoiceItemId` 对照行"留待后续 ADR"）。
- `rewards` 字段内部结构（`items`/`xp`/`currency`/`skills`/`world_flags`/`talent_points`）的字段级
  说明不在本文件重复——见 `core/gameplay/common/README.md` 与 `RewardBundle` 类型注释，本模块只
  登记 `rewards` 整体为一个 `FieldKind.Object`（存在且是对象），不重复声明其内部字段。
