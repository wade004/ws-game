# L4 玩法层 · dialog（对话与剧情）

职责：落地 08_玩法层_掉落任务对话关卡.md 第 3 节 Dialog——gossip 菜单（`dialog.gossip_menu`，十种
动作类型、按 `visible_if` 过滤可见选项）与剧情对话树（`dialog.story_tree`，节点/分支/演出钩子、按
`condition` 过滤可见分支、成环检测）、gossip↔story 统一会话与 Dialog 子状态的推入/弹出。对应 01
第 L4 模块表"对话与剧情"行（契约 `DialogHost`、数据表 `dialog.gossip_menu`/`dialog.story_tree`、
事件 `dialog.gossip_opened`/`dialog.gossip_action_executed`/`dialog.story_node_entered`/
`dialog.ended`）。字段表分片见 `schema/dialog.gossip_menu.md`、`schema/dialog.story_tree.md`。

依赖（按实际 `using` 语句核实）：

- L0：`Core.Foundation.Common`、`Core.Foundation.Common.Json`、`Core.Foundation.EventBus`、
  `Core.Foundation.Expr`、`Core.Foundation.DataRegistry`、`Core.Foundation.HookRegistry`
  （`IHookRegistry`/`HookArgs`，`performance_hook_ref`/`script` 动作用）、
  `Core.Foundation.AppLifecycle`（`IAppStateHost`/`SubStateId`，Dialog 子状态推入/弹出用）。
- L2：`Core.Rules.Common`（`ISkillHost`，`cast_skill` 动作用；`IExprHostFactory`/`IExprDiagnostics`）。
- L4 姊妹模块（同一 `Core.Gameplay` 程序集内跨模块引用，不产生新 `ProjectReference`）：
  `Core.Gameplay.Quest`（`IQuestHost`，`quest_accept`/`quest_turn_in` 两种动作直接调用；
  `QuestExprSchemaEntries.BuildParsingSchema` 复用为 `visible_if`/`condition` 的默认解析
  schema）、`Core.Gameplay.WorldState`（`IWorldState`，`set_flag` 动作用）、`Core.Gameplay.Common`
  （`ExprValueJson`，`set_flag` 动作 `params.value` 的解析复用）。

## 目录

```
dialog/
  README.md
  contracts/
    DialogActionKind.cs          十种 gossip 动作类型 + wire 名互转 + RequiresRef 收紧规则
    DialogCallbacks.cs           Vendor/Teleport/Save/EncounterStart 四个契约缺口委托
    Events.cs                     DialogEventKeys + 四个事件类型（GossipOpened/
                                  GossipActionExecuted/StoryNodeEntered/Ended）
    GossipMenuDefinition.cs      dialog.gossip_menu 一条记录的内存态表示 + FromRecord 解析
    IDialogDiagnostics.cs        诊断出口
    IDialogHost.cs                对话系统对外契约 + GossipView/StoryView 视图模型
    StoryTreeDefinition.cs       dialog.story_tree 一条记录的内存态表示 + FromRecord 解析 +
                                  HasCycle 成环检测（DFS）
  core/
    DialogContentValidationRule.cs  两张表的内容校验规则（悬空 next_node_id 引用 + 成环检测）
    DialogHost.cs                   IDialogHost 唯一实现
    DialogSchemas.cs                两张表的 TableSchema
    InMemoryDialogDiagnostics.cs   IDialogDiagnostics 默认实现
  tests/
    DialogHostTests.cs
    TestSupport.cs
```

## 判断记录

1. **Dialog 与 Quest 是一对双向依赖的 L4 姊妹模块**：本模块依赖 `IQuestHost`（`quest_accept`/
   `quest_turn_in` 动作直接调用），而 `Core.Gameplay.Quest.QuestHost` 反过来订阅本模块发出的
   `GossipOpenedEvent`/`StoryNodeEnteredEvent`（判定 `talk` 类任务目标是否推进，见
   `core/gameplay/quest/README.md` 判断记录 1）。二者同属 `Core.Gameplay` 单一程序集，不产生
   编译期循环 `ProjectReference` 问题，但确实是一处类型层面的双向耦合，两侧 README 对称记录
   同一事实。

2. **gossip 与 story 共用同一个"对话会话"概念**：一个单位同一时刻至多一个打开的对话会话（gossip
   菜单或剧情树二选一，可从 gossip 经 `start_story` 动作转为剧情而不离开会话）；只在"当前没有
   会话 → 打开会话"时 `IAppStateHost.PushSubState(Dialog)`，只在"会话结束"时 `PopSubState`，避免
   gossip→story 链路重复 Push、也不需要重复 Pop 才能真正退出 Dialog 子状态。

3. **`GossipActionExecutedEvent.ActionId` 的派生规则**：08 第 3.1 节 `GossipAction` 结构没有单独的
   `id` 字段，本模块取该动作的 `ref`（存在时）或 `dialog.action.<kind>`（`ref` 为空时，如 `save`）
   作为事件标识，供订阅方区分具体是哪一个动作被执行。

4. **`ref` 必需性相对 08 原文的收紧**：详见 `schema/dialog.gossip_menu.md`"代码相对 08 原文的收紧"
   一节——08 原文把 `ref` 整体标注为可选，代码按语义实际需要收紧为"八种动作必需、`save`/`vendor`
   两种不强制"，在内容解析期就能拦住缺失引用的坏数据。

5. **四个契约缺口全部改用委托绕过**：`vendor`（打开商店 UI）、`teleport`（传送执行）、`save`
   （发起存档流程）、`start_encounter`（启动遭遇）四种动作对应的实际执行都不属于本模块该直接
   依赖的职责（分别属于表现层 UI、场景路由、`ISaveSystem`、`core/gameplay/encounter`），本模块
   只发出请求信号（见 `contracts/DialogCallbacks.cs` 四个委托类型），未注入对应回调时只记一条
   诊断警告并跳过该动作，不抛异常、不阻断同一选项内其它动作的执行。

6. **`StoryBranchDef.NextNodeId` 的终止语义细化到分支粒度**：详见
   `schema/dialog.story_tree.md`"分支粒度的终止语义"一节。

7. **GP-05 收口（第四方深度审核）：`ChooseOption`/`AdvanceStory` 执行前重验 `VisibleIf`/
   `Condition`**：原实现只在"构造视图给 UI 显示"（`OpenGossip`/`GetGossipView` 等）那一刻按
   `VisibleIf`/`Condition` 过滤一次，玩家看到选项到真正点击执行之间世界状态可能已经变化（另一
   渠道推进了任务、物品被消耗、标志被翻转），执行时完全不管当下是否还成立，客户端若缓存了旧的
   选项索引就能强行执行一个"显示时已隐藏"的动作/分支。现在 `ChooseOption`/`AdvanceStory` 执行前
   用当前世界状态重新求值同一个表达式，不成立则拒绝（不执行任何 Action、不改变任何状态、返回
   false）；调用方（`DialogViewModel`）据此已订阅 quest/item/world 状态变化事件主动 `Refresh`，
   尽量让玩家在点击前就已经看到最新的可见性。配套在 `core/gameplay/quest`
   的 `QuestExprGroupProvider` 新增 `is_objectives_complete(questId)` 谓词——此前只有
   `is_active`/`is_available`/`is_completed` 三档状态查询，`Active → ObjectivesComplete →
   TurnedIn` 状态机里唯独 `ObjectivesComplete` 这一档没有对应查询，目标全部达标后 `is_active`
   已变 false，"任务可交付时显示交任务选项"这类内容写法会在真正该显示交任务选项的时候被隐藏
   （本条重验上线后，这个此前被"从不重验"掩盖的数据缺口才会真正表现为交互失败，`data/_sample/
   dialog/dialog.gossip_menu.json` 的 `option_turn_in` 一并勘误改用新谓词）。见
   `DialogHost.cs`、`presentation/ui/core/ViewModels/DialogViewModel.cs`、
   `DialogHostTests.cs`、`presentation/ui/tests/ViewModelTests.cs`。

## 不负责什么

- 不实现具体商店界面、传送执行、存档流程、遭遇启动的业务逻辑——四者全部经委托转发给调用方
  （见判断记录 5）。
- 不做具体对话文案与流程内容——08 第 9 节"基础架构提供 / 游戏层提供"表"Dialog 菜单/剧情树结构与
  动作类型集合"一行明确"结构由基础架构提供，具体对话文案与流程由游戏层提供"，本模块只提供结构、
  解析、状态机与动作分发机制。
- 不实现具体演出（切镜头、过场动画、BGM 控制等）——`performance_hook_ref` 是逻辑层与表现层之间
  关于"演出时机"的唯一约定接口，本模块只在进入节点时调用 `IHookRegistry.Invoke`，具体钩子做什么
  完全是表现层订阅实现，逻辑不感知。
- 不做地图标记/小地图渲染等表现层职责。
- 不自动向任何 `IDataRegistry`/`ISaveSystem` 注册 `DialogContentValidationRule`/两张表的
  `TableSchema`——调用方（组装层或本模块测试）需要显式注册（惯例同
  `core/gameplay/quest.QuestContentValidationRule`）。
