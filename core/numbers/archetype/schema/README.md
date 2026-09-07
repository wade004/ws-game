# archetype 数据表字段说明

对应 [04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 第 1.1 节表清单：
`arch.class`"职业模板：主属性、资源类型、技能书、天赋树引用"、`arch.race`"种族模板：被动光环
引用、基础属性修正"、`arch.talent_tree`"天赋树结构：节点、前置、天赋点消耗"。**判断记录：04
未给出这三张表的字段表**，以下字段为实现期按任务书 T2-3 给出的最小字段集补录，待 04 正式登记
时以 04 为准同步本文件。

## `arch.class`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `arch.class.<name>`；**不得是具体游戏的职业名**，测试/示例一律用 `sample_a` 一类中性名 |
| `name_key` | TextKey | 是 | 显示名文本键 |
| `primary_stat` | Id | 是 | 引用 `stat.*`；暂不声明为 `Reference`（见 README 判断记录 2） |
| `base_stats` | Object\<stat_id, Number\> | 是 | 初始基础属性绝对值，`ApplyTo` 经 `StatBaseWriter` 写入 |
| `power_types` | List\<Id\> | 是 | 引用 `arch.power.*` 资源类型定义（06 第 2.1 节），暂不声明为 `Reference` |
| `skill_book_ref` | Optional\<Id\> | 否 | 引用 `skill.book.*`；L2 `skill` 模块已实现（`core/rules/skill`），本字段仍不在模块内部声明为 `Reference`——避免 L1 `archetype` 反向静态耦合 L2 的表结构，是分层边界选择而不是对方模块不存在（见下方判断记录） |
| `talent_tree_ref` | Optional\<Id\>（Reference→`arch.talent_tree`） | 否 | 引用同表清单的 `arch.talent_tree` |
| `level_curve_ref` | Optional\<Id\>（Reference→`prog.level_curve`） | 否 | 引用 `progression` 模块的等级曲线 |

## `arch.race`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `arch.race.<name>` |
| `name_key` | TextKey | 是 | 显示名文本键 |
| `stat_mods` | Object\<stat_id, Number\> | 是 | 基础属性修正，`ApplyTo` 经 `StatModifierWriter` 以 `"flat"` 写入，来源为种族 id 本身 |
| `passive_auras` | Optional\<List\<Id\>\> | 否 | 引用 `skill.aura.*`；本模块只保存不应用（任务书原文），暂不声明为 `Reference` |

## `arch.talent_tree`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `arch.talent_tree.<name>` |
| `nodes` | Array | 是 | `Array<{id:String, prerequisites:[String], cost:Int, grants:Object}>`；结构由本模块自行解析（与 `prog.level_curve.entries` 同一处理方式，见 progression 模块 schema/README.md），前置存在性与无环由 `ArchTalentTreeCycleValidationRule` 校验 |

`nodes[].grants` 结构未知，由消费方（如未来的天赋效果系统）自行解释，本模块只透传。

**校验规则**（`ArchTalentTreeCycleValidationRule`，check 名 `talent_node_id`/
`talent_prerequisite_missing`/`talent_prerequisite_cycle`）：节点必须有合法的 `id`；
`prerequisites` 引用的节点必须存在于同一棵树；前置关系（含节点引用自身）不得成环。

## 判断记录

- **`primary_stat`/`power_types`/`skill_book_ref`/`passive_auras` 在本模块内部仍不声明为
  `FieldKind.Reference`**（2026-09-07 改写：此前称"指向并行开发的 stat_block/power_set 模块与
  尚未实现的 L2 skill 模块"已过时——四个模块此时都早已实现，本判断记录的真正理由是分层边界，
  不是对方模块还没写出来）：详见 README"设计要点与判断记录"第 2 条，本模块（L1）刻意不静态
  耦合 L2/L3 具体模块的表结构。当前实际的跨表注册/校验策略——`primary_stat`（标量 Id，指向
  `stat.definition`）已由集成方 `core/rules/assembly/RulesSchemaCatalog.cs`
  （`DeclareKnownReferences`）经 `IDataRegistry.DeclareReference` 补上引用完整性检查；
  `skill_book_ref`（标量 Id，指向 `skill.book`）结构上同样可以这样补，但当前尚未登记；
  `power_types`/`passive_auras` 是 `List<Id>` 字段，`IDataRegistry.DeclareReference` 契约本身
  只支持"某表某个标量 Id 字段整体指向另一张表"这一种形状（见
  `RulesSchemaCatalog.DeclareKnownReferences` 类型判断记录），数组/嵌套字段无法用这条通用机制
  声明引用完整性检查——这是接口能力边界，不是"尚未来得及补"。
- **`talent_tree_ref`/`level_curve_ref` 声明为 `Reference`**：两者指向的表都在本任务范围内
  （`arch.talent_tree` 是本模块自己的表，`prog.level_curve` 是同一任务 progression 模块的表），
  可以放心静态声明，也是任务书验收标准"`level_curve_ref` 引用完整性经 DataRegistry 报错"的
  直接实现依据。
