# `dialog.story_tree` 字段表

对应 `01_分层与依赖.md` L4 模块表"对话与剧情"行（目录 `core/gameplay/dialog`，契约
`DialogHost`）的主要数据表之一、`08_玩法层_掉落任务对话关卡.md` 第 3.2 节"剧情对话树"。本文件是该
字段表在代码层面的分片副本，字段名与结构逐一核对
`core/gameplay/dialog/core/DialogSchemas.cs`（`TableSchema` 声明）+
`core/gameplay/dialog/contracts/StoryTreeDefinition.cs`（`nodes` 数组内部结构解析 + 成环检测）+
`core/gameplay/dialog/contracts/DialogActionKind.cs`（本表节点不直接引用动作类型，此处仅作交叉参考）。

## 字段

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `dialog.<name>` 或内容作者自定义命名。 |
| `nodes` | Array\<StoryNode\> | 是 | 剧情节点数组，至少一个元素（`StoryTreeDefinition` 构造期强制非空）；起始节点固定为数组第一个元素（08 第 3.2 节、`StoryTreeDefinition.FirstNode`）。 |

## `StoryNode` 结构

`nodes[]` 的元素（08 第 3.2 节 `StoryNode` 结构，字段名按 `StoryTreeDefinition.ParseNode` 的 JSON
键名）：

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | 节点 id；同一棵树内必须唯一（`StoryTreeDefinition` 构造期检测重复，重复即抛 `ArgumentException`）。 |
| `text_key` | Id | 是 | 该节点的对话文本键。 |
| `speaker_ref` | Id | 否 | 指向 `creature.template` 或占位角色 id，供表现层选头像/立绘。 |
| `branches` | Array\<StoryBranch\> | 否 | 分支数组，结构见下；缺省或字段缺失时视为空数组。分支为空的节点是终止节点（08 第 3.2 节）。 |
| `performance_hook_ref` | Id | 否 | 进入该节点时调用的 `found.hook`，用于过场/运镜等演出；逻辑层只在进入节点时调用钩子，具体钩子做什么完全是表现层订阅实现，逻辑不感知（08 第 3.2 节）。 |

## `StoryBranch` 结构

`branches[]` 的元素（08 第 3.2 节 `{textKey, condition?, nextNodeId?}`）：

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `text_key` | Id | 是 | 该条分支选项的文案文本键。 |
| `condition` | Expr | 否 | 显隐条件；宿主分组复用 04 第 6.2 节，同 `dialog.gossip_menu.visible_if`（08 第 3.3 节）。 |
| `next_node_id` | Id | 否 | 选择该分支后进入的下一节点 id；为空表示该分支是终止分支，见下方"分支粒度的终止语义"。 |

## 分支粒度的终止语义（代码对 08 原文的细化）

08 第 3.2 节原文"分支为空时该节点是终止节点"，字面上以"整个节点没有任何分支"作为终止判定单位。
代码（`StoryBranchDef.NextNodeId` 类型注释、`DialogHost.AdvanceStory`）把这一语义细化到分支粒度：
**某一条分支的 `next_node_id` 为空即该分支本身终止对话**（`AdvanceStory` 遇到 `next_node_id` 为空
的分支直接调用 `Close`），允许同一节点的一部分分支继续推进、另一部分分支结束对话——这是比"整个
节点没有任何分支才终止"更细粒度的实现选择，不影响"分支为空的节点必然是终止节点"这一 08 原文
描述的特例（该节点的 `branches` 本就是空数组，自然没有分支可选）。

## 校验规则（由 `DialogContentValidationRule` 实施，不在解析期强制）

- `next_node_id` 引用的节点必须存在于同一棵树内——解析期不检查（`Id` 本身不携带"是否命中已知
  节点"的信息），由校验规则在解析成功后二次核对树内节点索引（`story_tree_dangling_next_node`）。
- 树不得成环——三色标记的迭代式 DFS 检测（算法与曾经的 `StoryTreeDefinition.HasCycle` 一致，
  ADR-0019 起改在原始 JSON 派生的邻接表上直接跑，不要求先成功解析出强类型的
  `StoryTreeDefinition`），成环时给出环上的节点 id 序列（`story_tree_cycle`）。
- 节点 id 在同一棵树内必须唯一（`story_tree_duplicate_node_id`）、`nodes` 数组至少一个元素
  （`story_tree_min_nodes`）——两项均为跨元素/最小长度的业务判断，`FieldSchema` 不表达，见下方
  "子结构登记表"判断记录。
- "起始节点 = `nodes[0]`"不需要额外校验——这是 `StoryTreeDefinition.FirstNode` 的既定语义（数组
  第一个元素恒是起始节点），不存在"取错"的可能。

## 子结构登记表（ADR-0019 / F1b）

`nodes`/`branches` 两层嵌套结构自本轮起登记为机器可读 `FieldSchema.Fields`/`Item`（代码：
`core/gameplay/dialog/core/DialogSchemas.cs` 的 `StoryNodeItemSchema`/`StoryBranchItemSchema`），
`DataRegistry` 加载期据此逐字段递归校验必填/类型/表达式可解析。

### `StoryNode`（`nodes[]`）

| 字段 | 类型 | 必填 | 引用/枚举 | 运行时代码位置 |
|---|---|---|---|---|
| `id` | Id | 是 | — | `StoryTreeDefinition.ParseNode` |
| `text_key` | TextKey | 是 | — | `StoryTreeDefinition.ParseNode` |
| `speaker_ref` | Id | 否 | 退回 Id（见下方判断记录） | `StoryTreeDefinition.ParseNode` |
| `branches` | Array\<StoryBranch\> | 否（缺省空数组） | — | `StoryTreeDefinition.ParseNode` |
| `performance_hook_ref` | Id | 否 | 退回 Id（`found.hook` 无实现级 schema 登记） | `StoryTreeDefinition.ParseNode` |

### `StoryBranch`（`nodes[].branches[]`）

| 字段 | 类型 | 必填 | 引用/枚举 | 运行时代码位置 |
|---|---|---|---|---|
| `text_key` | TextKey | 是 | — | `StoryTreeDefinition.ParseBranch` |
| `condition` | Expr | 否 | — | `StoryTreeDefinition.ParseBranch`（`ExprParser.Parse`） |
| `next_node_id` | Id | 否 | 退回 Id（同棵树内存在性属跨元素一致性检查，见上方"校验规则"） | `StoryTreeDefinition.ParseBranch` |

### 判断记录

1. **`speaker_ref` 退回 Id，不登记 `Reference(creature.template)`**：`creature.template` 属 L3
   `Core.Carriers.Creature`，`dialog` 模块（`Core.Gameplay.csproj`）只经 `Core.Carriers.csproj`
   传递可见、README 依赖清单未把它列为本模块直接依赖，登记为 Reference 前应先在 README 补一条
   依赖声明——本轮不在任务范围内新增模块依赖，先退回 Id，待 `speaker_ref` 真正需要存在性校验时
   再补依赖声明并升级。
2. **`performance_hook_ref` 退回 Id**：同 `dialog.gossip_menu.md` "`script.ref`" 判断记录，
   `found.hook` 当前无实现级 schema 登记。
3. **`next_node_id` 未登记为 Reference（甚至没有登记为跨表引用）**：它引用的是"同一条记录内、
   `nodes` 数组的其它元素"，不是另一张表的主键——`FieldKind.Reference` 表达的是跨表引用完整性，
   VariantSchema/Fields 递归也没有"引用同一数组其它元素"的记法，因此这项检查天然只能是业务判断
   （见上方"校验规则"），不属于 ADR-0019 首批"复合字段子结构登记"能表达的范围。
4. **节点 id 重复/`nodes` 最小长度/成环三项保留在 `DialogContentValidationRule`**：均为跨元素一致
   性或数组最小长度判断，`FieldSchema`/`VariantSchema` 只表达单个字段/单个数组元素内部的结构，
   不表达"同一数组内多个元素之间的关系"，理由同 `QuestContentValidationRule` 的
   `objectives_min_count`。
5. **Map 型对象**：本表无 Map 型字段，不适用。

## 本模块不做什么

- 不实现具体演出（切镜头、播放过场、静音 BGM 等）——`performance_hook_ref` 是逻辑层与表现层之间
  关于"演出时机"的唯一约定接口，本模块只在进入节点时调用 `IHookRegistry.Invoke`。
