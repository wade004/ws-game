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
| `skill_book_ref` | Optional\<Id\> | 否 | 引用 `skill.book.*`；L2 `skill` 模块尚未实现，暂不声明为 `Reference` |
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

- **`primary_stat`/`power_types`/`skill_book_ref`/`passive_auras` 暂不声明为
  `FieldKind.Reference`**：详见 README"设计要点与判断记录"第 2 条——这些字段指向的表分别属于
  并行开发的 `stat_block`/`power_set` 模块与尚未实现的 L2 `skill` 模块，本模块不静态耦合它们
  的表结构，待对应模块登记 schema 后由集成方经 `IDataRegistry.DeclareReference` 补上引用完整性
  检查。
- **`talent_tree_ref`/`level_curve_ref` 声明为 `Reference`**：两者指向的表都在本任务范围内
  （`arch.talent_tree` 是本模块自己的表，`prog.level_curve` 是同一任务 progression 模块的表），
  可以放心静态声明，也是任务书验收标准"`level_curve_ref` 引用完整性经 DataRegistry 报错"的
  直接实现依据。
