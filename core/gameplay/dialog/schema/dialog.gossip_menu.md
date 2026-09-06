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

## 本模块不做什么

- 不校验 `ref` 指向的具体目标（任务/传送点/遭遇/技能/剧情树/钩子 id）是否真实存在——这类跨表引用
  完整性检查留给通用的 `reference_integrity` 校验项（04 第 5 节），`DialogContentValidationRule`
  只捕获 `GossipMenuDefinition.FromRecord` 解析期能发现的问题（字段格式、`kind` 合法性、`ref` 按
  语义是否缺失、`visible_if` 的 Expr 语法）。
- 不实现具体商店/传送/存档/遭遇启动的执行细节——`vendor`/`teleport`/`save`/`start_encounter` 四种
  动作全部经委托（见 `core/gameplay/dialog/contracts/DialogCallbacks.cs`）转发给调用方，未注入对应
  委托时只记一条诊断警告并跳过，不抛异常。
