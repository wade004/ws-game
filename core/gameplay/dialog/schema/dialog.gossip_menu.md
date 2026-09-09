# `dialog.gossip_menu` 字段表

对应 `01_分层与依赖.md` L4 模块表"对话与剧情"行（目录 `core/gameplay/dialog`，契约
`DialogHost`）的主要数据表之一、`08_玩法层_掉落任务对话关卡.md` 第 3.1 节"gossip 菜单"。本文件是该
字段表在代码层面的分片副本，字段名与结构逐一核对
`core/gameplay/dialog/core/DialogSchemas.cs`（`TableSchema` 声明）+
`core/gameplay/dialog/contracts/GossipMenuDefinition.cs`（`options` 数组内部结构解析）+
`core/gameplay/dialog/contracts/DialogActionKind.cs`（动作类型集合）。

## 字段

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `dialog.<name>` 或内容作者自定义命名。 |
| `options` | Array\<GossipOption\> | 是 | 菜单选项数组，结构见下。 |

## `GossipOption` 结构

`options[]` 的元素（08 第 3.1 节 `GossipOption {textKey, visibleIf?, actions}`，字段名按
`GossipMenuDefinition.ParseOption` 的 JSON 键名）：

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `text_key` | Id | 是 | 选项文案文本键。 |
| `visible_if` | Expr | 否 | 显隐条件；缺省视为恒可见。宿主分组复用 04 第 6.2 节（`quest`/`world`/`player` 等），不引入专门的"对话条件语言"（08 第 3.3 节）。 |
| `actions` | Array\<GossipAction\> | 否 | 动作数组；缺省或字段缺失时视为空数组（`GossipMenuDefinition.ParseOption` 未提供 `actions` 字段时不报错）。 |

## `GossipAction` 结构

`actions[]` 的元素（08 第 3.1 节 `GossipAction {kind, ref?, params?}`）：

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `kind` | Enum | 是 | 十种动作类型之一，见下"动作类型表"。 |
| `ref` | Id | 视 `kind` 而定 | 依 `kind` 指向不同目标（任务 id、传送点、世界标志键、遭遇 id、技能 id、剧情树 id、脚本钩子 id）。是否必需见下"动作类型表"`ref 是否必需`列——**这是代码相对 08 原文的一处收紧**，见下方说明。 |
| `params` | Object | 否 | 动作附加参数；目前只有 `set_flag` 使用（承载写入的值，键为 `value`，经 `Core.Gameplay.Common.ExprValueJson.Parse` 解析），其余动作类型该字段为 null。 |

## 动作类型表

（08 第 3.1 节十种 `Action` 类型语义表 + `DialogActionKinds.RequiresRef` 的 `ref` 必需性判定）

| `kind`（wire 值） | 语义 | `ref` 是否必需 |
|---|---|---|
| `vendor` | 打开该 NPC 的 `econ.vendor` 界面（逻辑侧只发出"打开商店"请求，经 `VendorOpenRequestedCallback` 委托转发） | 否——NPC 与商店可以是一对一关系，`ref` 为空时按当前 NPC 自身推断 |
| `quest_accept` | 接取指定 `quest.def` | 是 |
| `quest_turn_in` | 交付指定 `quest.def` | 是 |
| `teleport` | 传送到指定传送点 | 是 |
| `save` | 触发存档 | 否——发起存档不指向任何具体 id |
| `set_flag` | 写 `WorldState`（`ref` 是 flagKey，写入的值来自 `params.value`，缺失时默认 `Bool(true)`） | 是 |
| `start_encounter` | 启动指定 `encounter.def` | 是 |
| `cast_skill` | 以该 NPC 为施法者释放指定技能 | 是 |
| `start_story` | 打开指定的 `dialog.story_tree` | 是 |
| `script` | 调用脚本钩子，兜底自定义演出 | 是 |

**代码相对 08 原文的收紧**：08 第 3.1 节 `GossipAction` 结构原文把 `ref` 整体标注为
`Optional<Id>`（对全部 `kind` 一视同仁地"可选"），但代码（`GossipActionDef` 构造函数 +
`DialogActionKinds.RequiresRef`）按语义实际需要收紧为"八种动作离开具体引用目标就无法执行，构造期
直接抛 `ArgumentException`"，只有 `save`/`vendor` 两种真正不需要 `ref`。这不是与 08 相悖的字段
集合差异（08 也没有列出 `ref` 各自的必需性细节，只是笼统标注为整体可选），而是代码对 08 未细化
的部分做出的更严格实现选择，好处是在内容解析期就能拦住缺失引用的坏数据，比留到运行期触发空引用
异常更早发现问题。

## 子结构登记表（ADR-0019 / F1b）

`options`/`actions` 两层嵌套结构自本轮起登记为机器可读 `FieldSchema.Fields`/`Item`/`Variants`
（代码：`core/gameplay/dialog/core/DialogSchemas.cs` 的 `GossipOptionItemSchema`/
`GossipActionItemSchema`），`DataRegistry` 加载期据此逐字段递归校验必填/类型/引用/表达式可解析，
不再依赖 `DialogContentValidationRule` 整条委托 `GossipMenuDefinition.FromRecord` 兜底。

### `GossipOption`（`options[]`）

| 字段 | 类型 | 必填 | 引用/枚举 | 运行时代码位置 |
|---|---|---|---|---|
| `text_key` | TextKey | 是 | — | `GossipMenuDefinition.ParseOption` |
| `visible_if` | Expr | 否 | — | `GossipMenuDefinition.ParseOption`（`ExprParser.Parse`） |
| `actions` | Array\<GossipAction\> | 否（缺省空数组） | — | `GossipMenuDefinition.ParseOption` |

### `GossipAction`（`options[].actions[]`，按 `kind` 分派）

| `kind`（wire 值） | `ref` 必填 | `ref` 引用目标 | `params` | 运行时代码位置 |
|---|---|---|---|---|
| `vendor` | 否 | 退回 Id（无登记表，NPC 自身推断） | — | `DialogHost.ExecuteAction` |
| `quest_accept` | 是 | `Reference(quest.def)` | — | `DialogHost.ExecuteAction` |
| `quest_turn_in` | 是 | `Reference(quest.def)` | — | `DialogHost.ExecuteAction` |
| `teleport` | 是 | 退回 Id（`TeleportTargetResolver` 解析，非 DataRegistry 表） | — | `DialogHost.ExecuteAction`/`GameplayAssembly.TeleportUnit` |
| `save` | 否 | — | — | `DialogHost.ExecuteAction` |
| `set_flag` | 是 | 退回 Id（`world.flag_schema` 非运行态表） | `{value}` 未登记子结构（联合类型，见下方判断记录） | `DialogHost.ExecuteAction` |
| `start_encounter` | 是 | `Reference(encounter.def)` | — | `DialogHost.ExecuteAction`/`GameplayAssembly` |
| `cast_skill` | 是 | `Reference(skill.def)` | — | `DialogHost.ExecuteAction` |
| `start_story` | 是 | `Reference(dialog.story_tree)` | — | `DialogHost.ExecuteAction`/`DialogHost.StartStory` |
| `script` | 是 | 退回 Id（`found.hook` 无实现级 schema 登记） | — | `DialogHost.ExecuteAction` |

### 判断记录

1. **`ref` 必填性按 `DialogActionKinds.RequiresRef` 登记为 `Variants` 的逐 case `required`**：此前
   只在 `GossipActionDef` 构造函数里以异常形式体现（"代码相对 08 原文的收紧"，见上文），
   ADR-0019 起改为 `DataRegistry` 加载期的 `required_field` 结构校验，内容解析期之前即可拦下，
   不再需要构造一个 `GossipActionDef` 才能发现。
2. **`quest_accept`/`quest_turn_in`/`start_encounter`/`cast_skill`/`start_story` 五处升级为
   `Reference`**：此前"本模块不做什么"一节声明"不校验 ref 指向的具体目标是否真实存在"——ADR-0019
   起这五处目标表（`quest.def`/`encounter.def`/`skill.def`/`dialog.story_tree` 本身）分层上均不
   高于本模块（quest/encounter 同为 L4 且与 dialog 同属 `Core.Gameplay` 程序集、经
   `GameplaySchemaCatalog.RegisterAll` 同一次调用先后注册；skill.def 属 L2；dialog.story_tree 是
   本模块自身表），登记为 Reference 后交由 `reference_integrity` 通用校验项覆盖，此前的声明已过时。
3. **`vendor`/`teleport`/`set_flag`/`script` 四处仍退回 Id，不做存在性检查**：
   - `vendor`：`ref` 语义上是商店 id，但当前无独立"商店"登记表可指（`econ.vendor` 是运行态挂载
     关系，NPC 空 `ref` 时按自身推断，不是一个需要预先登记的目标集合）。
   - `teleport`：`ref` 经 `GameplayAssembly` 注入的 `TeleportTargetResolver` 委托解析成
     `(MapId, Position)`，不是任何一张 `DataRegistry` 表的主键（判断记录同
     `core/carriers/gobj` 的 `teleport_target_ref`）。
   - `set_flag`：`ref` 是 `WorldState` flagKey，`world.flag_schema`（04 第 1.1 节）虽然存在
     `TableSchema` 登记，但该表"非运行态数据，仅作文档化 schema"、不参与运行期加载（见
     `WorldStateSchemas.FlagSchema` 类型注释判断记录），登记为 Reference 会让该表未加载时的每条
     `set_flag` 动作都判为引用失效，判断记录同 `QuestSchemas.RewardsFields.world_flags.flagKey`。
   - `script`：`ref` 指向 `found.hook` 钩子 id，`found.hook` 当前无实现级 schema 登记（04 变更
     记录 2026-09-05"仍无对应实现级 schema 登记"），判断记录同 `SkillSchemas` 的 `script` 效果原语。
4. **`set_flag.params.value` 未登记子结构**：`value` 经 `Core.Gameplay.Common.ExprValueJson.Parse`
   解析，取值可以是 `Bool|Int|Number|String|{$id: Id}` 联合类型，04 记法的 `FieldKind` 是单一类型
   枚举，无法表达联合类型，因此 `params` 只登记"存在且是对象"，不登记 `value` 子字段（判断记录同
   `QuestSchemas.RewardsFields.world_flags[].value`）。
5. **Map 型对象**：本表无 Map 型字段，不适用（ADR-0019 首批范围外的记录留给 `AchievementSchemas`/
   `WorldStateSchemas` 等有 Map 型字段的模块）。

## 本模块不做什么

- 不实现具体商店/传送/存档/遭遇启动的执行细节——`vendor`/`teleport`/`save`/`start_encounter` 四种
  动作全部经委托（见 `core/gameplay/dialog/contracts/DialogCallbacks.cs`）转发给调用方，未注入对应
  委托时只记一条诊断警告并跳过，不抛异常。
