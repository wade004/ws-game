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
| `objectives` | Array\<QuestObjective\> | 是 | 目标数组，至少一条（`QuestDefinition` 构造期强制非空，`FieldSchema` 无法表达最小长度，业务判断保留见下"子结构登记表"）。元素结构 ADR-0019/F1b 起登记为 `QuestSchemas.ObjectiveItemSchema`（按 `type` 分派的 `Variants`），见下"QuestObjective 结构"与"子结构登记表"。 |
| `prerequisite` | Expr | 否 | 前置条件（等级、已完成任务、世界标志等）；未声明时视为恒真（无前置）。 |
| `exclusive_group` | Id | 否 | 互斥组：同组任务同时只能激活/完成一个（`QuestHost.Accept` 校验）。 |
| `start_method` | Enum | 是 | `npc_gossip｜item_use｜area_trigger｜auto`，起始方式。 |
| `turn_in_method` | Enum | 是 | `npc_gossip｜auto`，交付方式。 |
| `rewards` | Object | 否 | `{items, xp, currency, skills, world_flags, talent_points}`；结构见 `core/gameplay/common/README.md` 与该模块 `RewardBundle` 类型。ADR-0019/F1b 起本表字段改为 `QuestSchemas.RewardsFields` 直接构造（带 `Fields`），不再调用裸的 `Core.Gameplay.Common.RewardSchemaFields.Rewards(required: false)`——后者目前仍只登记裸 `Object`，见下"子结构登记表"判断记录。 |
| `repeatable` | Enum | 是 | `none｜daily｜unlimited`，可重复性。 |

## `QuestObjective` 结构

`objectives[]` 的元素（08 第 2.1 节 `QuestObjective` 结构，字段名按 `QuestDefinition.ParseObjective`
的 JSON 键名）：

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `type` | Enum（判别字段） | 是 | `kill｜collect｜interact｜explore｜escort｜event｜cast｜talk`，见下"目标类型对照表"；ADR-0019/F1b 起是 `QuestSchemas.ObjectiveItemSchema` 的 `VariantSchema.Discriminator`，取值集合由 `variant_discriminator` 校验项检查，与 Enum 语义等价。 |
| `target_ref` | Id 或 Reference（依 `type`） | 是 | 依 `type` 指向不同域名的目标，见下对照表"登记方式"列。 |
| `count` | Int | 是 | 需求数量，必须为正数；`explore`/`escort`/`talk` 三种类型恒为 1（`QuestObjective` 构造期强制；`FieldSchema` 无法表达数值范围/等值约束，业务判断保留在 `QuestContentValidationRule`，见下"子结构登记表"）。 |
| `param` | Object | 否 | 各类型的额外参数，见下对照表"param 要点"列；ADR-0019/F1b 起每种 `type` 各自登记 `param` 的接受子字段（无参数的类型登记为"存在则不接受任何子字段"，做法同 `SkillSchemas` 的 `open_lock`/`flag`）。 |
| `description_key` | Id | 否 | 该条目标自身的描述文本键，供任务日志 UI 展示每条目标的文案。**代码注释称"08 原文 `QuestObjective` 结构未列出，任务书拍板补录"——见下方"与 08 原文的差异"，实际当前版本 08 第 2.1 节 `QuestObjective` 结构原文已含 `descriptionKey: Optional<Id>`。** |

## 目标类型对照表

（08 第 2.1 节表；`target_ref` 域名与 `count` 恒为 1 的判定见
`core/gameplay/quest/contracts/QuestObjectiveType.cs` 的 `QuestObjectiveTypes.RequiredTargetDomain`/
`RequiresCountOne`）

| `type` | `target_ref` 指向 | `count` 含义 | `param` 要点 | ADR-0019/F1b 登记方式 |
|---|---|---|---|---|
| `kill` | `creature.template` | 需击杀数量 | 无 | `target_ref`: **Id**（不是 Reference，见下判断记录 2） |
| `collect` | `item.template` | 需持有数量 | `consume_on_progress: Bool`（进度是否随拾取即时消耗物品） | `target_ref`: `Reference(item.template)` |
| `interact` | `gobj.template` | 需交互次数 | 无 | `target_ref`: `Reference(gobj.template)` |
| `explore` | `area.trigger_def`（`quest_explore` 类型） | 恒为 1 | 无 | `target_ref`: `Reference(area.trigger_def)` |
| `escort` | `creature.template`（被护送对象） | 恒为 1 | `escort_route_ref`（路径引用） | `target_ref`: **Id**（同 `kill`，见判断记录 2）；`escort_route_ref`: Id（04 §2.2 无对应 domain，无法 Reference） |
| `event` | 任意事件类型 | 视事件语义 | `eventFilter: Expr` | `target_ref`: Id（不做域名/存在性强校验，见判断记录 1） |
| `cast` | `skill.def` | 需施放次数 | 无 | `target_ref`: `Reference(skill.def)` |
| `talk` | `dialog.gossip_menu` 或 `dialog.story_tree` 节点 | 恒为 1 | 无 | `target_ref`: Id（不做域名/存在性强校验，见判断记录 1） |

判断记录 1（代码与 08 字面的一处偏差，见 `QuestObjectiveTypes.RequiredTargetDomain` 注释）：`talk`
类型的 `RequiredTargetDomain` 返回 `null`（不做域名强制校验），因为目标既可以是 `dialog.gossip_menu`
的 `id`，也可以是 `dialog.story_tree` 某个节点的 `id`（节点 id 由内容作者自行命名，不强制
domain 段等于 `"dialog"`）——是否真正匹配留给 `QuestHost` 运行期按实际触发的 gossip/story 事件字段
判定，不在内容校验期做域名检查。`event` 同理（`RequiredTargetDomain` 对 `event` 也返回 `null`，
指向 06/08 事件词汇表中的具体事件 key，事件 key 的 domain 不固定）。

判断记录 2（ADR-0019/F1b，`kill`/`escort` 退回 Id 而不是 Reference）：这两处 `target_ref` 概念上
指向 `creature.template`（域名 `creature`，属 L3，低于本模块 L4，分层上允许登记为
`Reference("creature.template")`）。但既有测试
`Tests.Gameplay.Assembly.GameplayAssemblyOwnerDayVendorExtensionPointTests`
（`core/gameplay/assembly/tests/GameplayAssemblyOwnerDayVendorExtensionPointTests.cs`，不在本次任务
改动范围内）用一个只存在于运行期世界实体、从未登记进 `creature.template` 内容表的 creature id
（`creature.owner_day_vendor_target`）驱动 `kill` 目标，断言 `DataRegistry.LoadAll()` 的
`report.IsBlocking` 为 `false`——若登记为 `Reference`（隐含存在性检查），会让该测试判为阻断错误。
因此这两处退回 `Id`（只查格式，不查存在性），domain 必须是 `"creature"` 这条比 Reference 弱一档的
检查改由 `QuestContentValidationRule` 手写兜底（检查项 `objective_target_domain_mismatch`），不是
完全放弃这条判断，只是不做跨表存在性检查。

## 与 08 原文的差异

代码内多处注释（`QuestSchemas.cs`、`QuestDefinition.cs`、`QuestObjective.cs`）称 `title_key`/
`description_key`（`quest.def` 级）与 `objectives[].description_key` 是"08 原文未列出，任务书拍板
补录"的字段。经核对当前仓库内 `architecture/08_玩法层_掉落任务对话关卡.md` 第 2.1 节，这三个字段
**均已经出现在正式字段表 / `QuestObjective` 结构原文中**（`title_key` 标注必填、`description_key`
标注选填，`QuestObjective` 结构块含 `descriptionKey: Optional<Id>`）。也就是说代码实现与 08 文档
当前版本在字段集合上是**一致的、没有实际差异**；代码注释里"08 原文未列出"的说法很可能是 08 文档
在该模块实现之后经历过一轮勘误补充、代码侧注释未同步更新所致——这是一处文档与代码注释之间的表述
不一致，不影响运行期行为，本文档如实记录，不代为修改 08 或 quest 模块源码（均不在本次任务范围）。

## 子结构登记表（ADR-0019 / F1b）

对应 `core/gameplay/quest/core/QuestSchemas.cs` 的 `ObjectiveItemSchema`/`RewardsFields`。以
`core/gameplay/quest/contracts/QuestDefinition.cs`（`ParseObjective`）与
`core/gameplay/common/contracts/RewardBundle.cs`（`FromRecord`）解析代码为唯一依据；`QuestObjective`
结构的字段清单/目标类型对照表见上两节，本节只记录登记本身的判断（哪些收窄为 Id、哪些数值范围/等值
业务判断保留在 `QuestContentValidationRule`、Map 型字段的处理）。

### `rewards` 参数表（`QuestSchemas.RewardsFields`）

| 字段 | 类型 | 必填 | 引用目标 | 判断记录 |
|---|---|---|---|---|
| `items` | Array\<Object\> | 否 | `items[].itemId`: `Reference(item.template)` | `items[].count`: Int/必填，必须为正数（`ItemStack` 构造期硬约束，业务判断保留在 `QuestContentValidationRule` 的 `reward_item_count_positive`） |
| `xp` | Number | 否 | 无 | 缺省 0；不能为负数（`RewardBundle` 构造期硬约束，业务判断 `reward_xp_non_negative`） |
| `currency` | Array\<Object\> | 否 | `currency[].currencyId`: `Reference(econ.currency)` | `currency[].amount`: Int/必填；`RewardBundle.ParseCurrency` 本身不检查符号，未登记额外约束 |
| `skills` | Array\<Reference\> | 否 | 元素 `Reference(skill.def)` | 元素本身即引用，无额外子字段 |
| `world_flags` | Array\<Object\> | 否 | 无（`flagKey` 是 Id，非 Reference） | `world_flags[].flagKey`: Id/必填，判断记录同下；`world_flags[].value` 是 Bool｜Number｜Id 联合类型，`FieldKind` 无法表达联合类型，**未登记该子字段**——"value 必须存在"这条纯必填判断改由 `QuestContentValidationRule` 手写兜底（`reward_world_flag_value_required`），做法同 `SkillSchemas` 的 `set_world_flag.flag_key` 先例 |
| `talent_points` | Int | 否 | 无 | 缺省 0；不能为负数（`RewardBundle` 构造期硬约束，业务判断 `reward_talent_points_non_negative`） |

判断记录（登记落点）：`Core.Gameplay.Common.RewardSchemaFields.Rewards()`（`core/gameplay/common/`，
`quest.def`/`encounter.def`/`achv.def` 三表共用的登记入口）目前仍只登记裸 `FieldKind.Object`（不带
`Fields`）——本次任务范围限定在 `quest` 模块，不改动 `core/gameplay/common`。`QuestSchemas.RewardsFields`
因此作为本模块的公开静态成员单独登记，`quest.def` 的 `rewards` 字段直接用它构造（不再调用
`RewardSchemaFields.Rewards()`）。`RewardsFields` 与同为 `Core.Gameplay` 单一程序集内的 achievement/
dialog 等模块共享同一个 `RewardBundle` 结构，可以直接引用 `Core.Gameplay.Quest.QuestSchemas.RewardsFields`
复用（无需新增 `ProjectReference`）；上游也可以考虑后续把它搬进 `RewardSchemaFields.Rewards()` 本身。

### `objectives[]` 变体（`QuestSchemas.ObjectiveItemSchema`，判别字段 `type`）

变体键集合与 `QuestObjectiveTypes.EnumValues` 全集一致（`QuestSchemaCoverageTests` 用测试锁死），每
个取值的 `target_ref`/`param` 登记见上"目标类型对照表"的"ADR-0019/F1b 登记方式"列；`count`/
`description_key` 是全部取值共有的 `CommonFields`（`count` 必填 Int，`description_key` 可选 Id）。

### Map 型字段

`quest.def`/`objectives[]`/`rewards` 范围内**没有**发现键为任意字符串、值同构的 Map 型对象——
`param`（各类型固定键清单）、`rewards` 各子字段（固定键清单）均可以用 `Fields`/`Variants` 精确表达，
不涉及"键本身是内容 id 或任意字符串"的动态键场景。本节如实记录：本轮未发现需要"Map 型待后续契约
扩展（ADR-0019 首批范围外）"的字段。

## 本模块不做什么

- 不校验 `exclusive_group` 引用的其它任务 id、`prerequisite` 引用的其它任务/世界标志是否真实存在——
  这类跨记录引用完整性检查留给通用的 `reference_integrity` 校验项（04 第 5 节）。
- 不实现"多选一奖励"（08 第 2.4 节 `RewardChoiceItemId` 对照行"留待后续 ADR"）。
- ADR-0019/F1b 起 `objectives`/`rewards` 的内部结构已登记为机器可读 `Fields`/`Variants`/`Reference`
  （见上"子结构登记表"），`QuestContentValidationRule` 收窄为只保留登记表达不了的纯业务判断（数值
  范围/等值约束、`kill`/`escort` 的 domain 弱校验），不再整条委托 `QuestDefinition.FromRecord` 把
  任意解析异常打包成一条泛化消息——避免同一缺陷被结构校验与本规则双重报告，详见
  `QuestContentValidationRule.cs` 类型顶部判断记录。
