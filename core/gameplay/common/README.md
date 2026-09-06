# L4 玩法层 · common（奖励包与奖励分发契约）

职责：本目录是 01_分层与依赖.md 第 4 节"实现级共享目录"登记的一条（`core/gameplay/common`（奖励包与
奖励分发契约）），不对应 01 第 L4 模块表里的某一行——它没有自己的数据表、没有自己的事件，只提供一份
被多个 L4 玩法模块共同复用的"奖励包"结构与结算契约：`RewardBundle`（08_玩法层_掉落任务对话关卡.md
第 2.1 节 `quest.def.rewards` 字段 `{items, xp, currency, skills, world_flags, talent_points}` 结构，
第 4.1、6.1 节分别注明 `encounter.def.rewards`、`achv.def.rewards`"同 `quest.def.rewards` 结构"）与
`IRewardDispatcher`（把一份 `RewardBundle` 结算到具体单位身上，供 `quest`/`encounter`/`achievement`
在各自的完成/达成时机调用，避免三个模块各自重写一遍"按字段逐项发放"的样板逻辑）。此外还提供
`ExprValueJson`（`ExprValue` 与 `JsonValue` 之间的编码约定）与 `RewardSchemaFields`（`rewards` 字段的
`FieldSchema` 帮助方法），供 `rewards` 结构的三个使用方内容表登记与 JSON 编解码复用。

依赖（按实际 `using` 语句核实）：

- L0：`Core.Foundation.Common`（`Id`）、`Core.Foundation.Common.Json`（`JsonObject`/`JsonArray`/
  `JsonBool`/`JsonNumber`/`JsonString`）、`Core.Foundation.Expr`（`ExprValue`/`ExprValueKind`）、
  `Core.Foundation.DataRegistry`（`FieldSchema`/`FieldKind`，`RewardSchemaFields` 用）。
- L1：`Core.Numbers.Progression`（`IProgressionHost`，`RewardDispatcher` 的经验发放依赖）。
- L3：`Core.Carriers.Common`（`IInventoryHost`/`ItemStack`，物品奖励发放/解析依赖）、
  `Core.Carriers.Item`（`SkillGranter` 委托类型，技能奖励发放依赖）。
- L4 姊妹模块：`Core.Gameplay.WorldState`（`IWorldState`，`world_flags` 奖励项的发放依赖）——同一个
  `Core.Gameplay` 程序集内的跨模块引用，不产生新的 `ProjectReference`。

经 `Core.Gameplay.csproj` 既有的 `Core.Carriers` 项目引用传递可见，本模块不新增任何 `ProjectReference`。

## 目录

```
common/
  README.md
  contracts/
    ExprValueJson.cs            ExprValue<->JsonValue 编码约定（惯例照抄 world_state.WorldState.ToJson/
                                 FromJson，抽出供 RewardBundle 的 world_flags 奖励项与
                                 core/gameplay/dialog 的 set_flag 动作 params.value 共同复用）
    IRewardDiagnostics.cs       RewardDispatcher 的诊断出口（某类奖励依赖未注入时警告并跳过）
    IRewardDispatcher.cs        奖励分发契约：Grant(unitId, bundle, sourceId)
    RewardBundle.cs             奖励包强类型模型 + FromRecord 解析（items/xp/currency/skills/
                                 world_flags/talent_points 六类奖励）
    RewardDispatchDelegates.cs  CurrencyGranter/TalentPointGranter 两个契约缺口委托
    RewardSchemaFields.cs       rewards 字段的 FieldSchema 帮助方法，供 quest.def/encounter.def/
                                 achv.def 三处内容表登记复用，避免各自重复拼写
  core/
    InMemoryRewardDiagnostics.cs  IRewardDiagnostics 默认实现（警告收进内存列表，供测试断言）
    RewardDispatcher.cs         IRewardDispatcher 唯一实现：六类奖励分别转发给各自的宿主依赖
  tests/
    RewardDispatcherTests.cs    Grant 六类奖励发放/依赖缺失跳过用例
```

## 判断记录

1. **`RewardBundle` 放在 `core/gameplay/common` 而不是 `quest` 模块内部**：08 第 2.1、4.1、6.1 节三处
   分别描述 `quest.def.rewards`/`encounter.def.rewards`/`achv.def.rewards`，结构完全相同（"同
   `quest.def.rewards` 结构"），01 第 4 节也把本目录明确登记为"奖励包与奖励分发契约"的共享落点——
   三个使用方模块只依赖本模块的强类型结构与解析逻辑，不需要互相依赖或重复实现一遍同样的
   JSON 解析/校验代码。

2. **`CurrencyGranter`/`TalentPointGranter` 是契约缺口的委托绕过**：架构未给货币系统（08 第 7 节
   Economy，与本任务并行由另一 agent 建设）与天赋点发放定义共享契约接口，`RewardDispatcher`
   按委托签名绕过（惯例同 `core/carriers/item` 的 `SkillGranter`"契约缺口用模块内委托绕过并
   汇报"）——这两个契约缺口本身不由本模块解决，见"不负责什么"。

3. **`RewardDispatcher` 的六类依赖全部是可选构造参数，任一为 null 时对应奖励记诊断跳过、不抛
   异常**：调用方可以只注入自己关心的子集（例如只测试物品发放时不必构造一个真实
   `IProgressionHost`）；`RewardBundle` 里某类奖励本就为空时，即便对应依赖也是 null，也不产生
   警告——只有"确实有这类奖励要发但发不出去"才值得警告。

4. **`ExprValueJson` 的 `Id` 用 `{"$id": "..."}` 包装以区别于 `String`**：与
   `core/gameplay/world_state.WorldState.ToJson/FromJson` 使用同一套编码约定（Bool/String 原生映射，
   `Int` 用不带小数点的原始文本、`Number` 用必带小数点的原始文本区分），本类型把这套约定单独抽出
   成独立工具类，避免 `RewardBundle.WorldFlags` 与 `core/gameplay/dialog` 的 `set_flag` 动作
   `params.value` 各自手抄一遍同样的编解码逻辑（第三处复用点）。

## 不负责什么

- 不实现"组合支付"（`ExtendedCost` 多货币/多物品组合支付）与"多选一奖励"（08 第 2.4 节
  `rewards.items` 扩展为"多选一"分组结构，原文"留待后续 ADR"）——本模块只落地当前单一固定结构。
- 不解决判断记录 2 描述的 `CurrencyGranter`/`TalentPointGranter` 契约缺口本身（架构未给货币/
  天赋点系统定义共享接口），只提供委托绕过。
- 不知道"何时该发奖励"——不订阅任何事件、不持有任何触发时机的判断逻辑，`IRewardDispatcher.Grant`
  由 `quest`/`encounter`/`achievement` 各自在完成/达成时机主动调用。
- 不对外暴露任何数据表（`quest.def`/`encounter.def`/`achv.def` 各自的 `TableSchema` 由使用方模块
  自己声明），本模块的 `RewardSchemaFields.Rewards` 只是登记 `rewards` 字段本身的帮助方法，`rewards`
  内部结构的校验在解析期由 `RewardBundle.FromRecord` 完成，不是 `TableSchema`/`FieldSchema` 能表达
  的嵌套形状。
