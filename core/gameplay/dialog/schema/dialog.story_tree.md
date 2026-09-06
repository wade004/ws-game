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
  节点"的信息），由校验规则在解析成功后二次核对树内节点索引。
- 树不得成环——`StoryTreeDefinition.HasCycle` 用三色标记的迭代式 DFS 检测，成环时给出环上的
  节点 id 序列。
- "起始节点 = `nodes[0]`"不需要额外校验——这是 `StoryTreeDefinition.FirstNode` 的既定语义（数组
  第一个元素恒是起始节点），不存在"取错"的可能。

## 本模块不做什么

- 不校验 `speaker_ref` 指向的 `creature.template`、`performance_hook_ref` 指向的 `found.hook` 是否
  真实存在——留给通用的 `reference_integrity` 校验项（04 第 5 节）。
- 不实现具体演出（切镜头、播放过场、静音 BGM 等）——`performance_hook_ref` 是逻辑层与表现层之间
  关于"演出时机"的唯一约定接口，本模块只在进入节点时调用 `IHookRegistry.Invoke`。
