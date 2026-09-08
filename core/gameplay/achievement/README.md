# L4 玩法层 · achievement（成就）

职责：落地 08_玩法层_掉落任务对话关卡.md 第 6 节 Achievement——按 `achv.def.criteria`（六种类型：
`kill_count`/`collect_count`/`quest_complete`/`reach_area`/`cast_count`/`custom_event`）订阅事件
总线累计进度，全部 criteria 达标后一次性解锁并经 `Core.Gameplay.Common.IRewardDispatcher` 结算
`rewards`。对应 01 第 L4 模块表 `achievement` 行（契约 `AchievementHost.evaluate(event)`、数据表
`achv.def`、事件 `achievement.progressed`/`achievement.unlocked`）。

依赖：L0（`data_registry`/`event_bus`/`expr`/`save_system`）、L2（`core/rules/common.IUnitAccess`/
`IExprHostFactory`/`IExprReadableEvent`；`core/rules/expr_host.RulesExprSchema.Base` 默认，可由
调用方传入合并后的 schema 覆盖）、`core/gameplay/common`（`RewardBundle`/`IRewardDispatcher`）。
经 `Core.Gameplay.csproj` 既有的 `Core.Carriers` 项目引用传递可见。

## 目录

```
achievement/
  README.md
  contracts/
    CriterionType.cs               六种类型 + snake_case 互转
    AchievementCriterion.cs        单条 criterion 强类型视图 + FromRecord（filter 不在此解析）
    AchievementCriterionProgress.cs (Current, Target) 只读快照
    AchievementDefinition.cs       achv.def 一条记录的强类型视图
    AchievementOptions.cs          PlayerUnitResolver 策略配置
    Events.cs                      AchievementEventKeys + Progressed/Unlocked 两个事件
    IAchievementHost.cs            契约接口
  core/
    AchievementContentValidationRule.cs  type 合法 + observe_event 已登记
    AchievementHost.cs             IAchievementHost + IPersistable 唯一实现
  schema/
    AchievementSchemas.cs          achv.def 的 TableSchema
  tests/
    TestSupport.cs
    AchievementHostTests.cs        六种类型/filter/解锁一次性/持久化用例
```

## 判断记录

1. **`exprSchema` 默认值的契约缺口（`event.<field>` 過去无法被 `filter` 引用）**：并行开发中
   `core/rules/expr_host.RulesExprSchema` 经 ADR-0015 严格化后，`Base` 不再对 `event` 分组做"未
   登记 key 一律放行"。阶段 3 集成收尾"事项二"已经在 `core/foundation/expr`
   （`ExprParser`/`ExprValidator`）层面解决了这个缺口——`event.<任意 key>` 现在统一解析为合法
   引用（签名未知，类型检查跳过），不再需要调用方为每个具体事件字段名单独登记签名。本模块
   `AchievementHost` 构造函数默认仍用 `RulesExprSchema.Base`；游戏组装根
   （`core/gameplay/assembly.GameplaySchemaCatalog.FullExprSchema`）额外合并了 `quest`/`player`/
   `world` 三个分组，`CriterionType.CustomEvent` 的 `filter` 现在可以同时引用
   `event.<field>`/`world.*`/`quest.*`/`player.*`/`self`/`target` 等全部九个分组。

2. **`criteria[]` 的匹配规则一律经 `IExprReadableEvent.TryGetField` 按字段名读取，不依赖具体事件
   类型**：`TryMatch` 不假设 `quest.turned_in`/`area.trigger_entered` 对应的具体事件类型已经在
   本次编译中存在（这些事件由并行开发的 quest/area_trigger 模块定义），只依赖
   `found.event_catalog` 登记的字段名字与 `IExprReadableEvent` 这一通用读取协议，天然与事件的
   具体实现类型解耦，见 `AchievementHost.TryMatch` 注释。

3. **`FilterNode` 之外的五种类型也可以叠加 `filter`**：08 第 6.1 节 `custom_event` 行"由内容作者
   指定要观察的具体事件类型与匹配条件（Expr）"，本模块把这一约定推广为全部六种类型均可选携带一条
   补充过滤表达式——`CustomEvent` 之外的五种类型在各自的基础匹配规则通过之后再叠加求值 `filter`，
   均为真才计入一次进度。

4. **存档只落盘 `AchievementOptions.PlayerUnitResolver` 解析出的那一个单位**：运行期
   `_progress`/`_unlocked` 按任意 `(unitId, achievementId)` 维护（支持"非玩家单位触发的
   criterion"这一更一般场景），但 `Save`/`Load` 只处理当前玩家单位这一份，与 10 第 2.2 节
   `player.achievement_state` 字段定义一致；读档不重放 `achievement.progressed`/`unlocked`，也不
   重新调用 `IRewardDispatcher.Grant`（同 `WorldState`/`DifficultyHost`/`SpawnHost` 判断记录
   "读档不是一次业务事件"）。

5. **`AchievementProgressedEvent.Current`/`Target` 对应"触发本次变化的那一条 criterion"，不是跨
   criteria 汇总值**：`found.event_catalog` 该行字段表未明确区分，本模块按"每次某条 criterion
   计数变化各发一次"的最贴近字面理解实现（08 第 6.1 节"成就系统只订阅事件总线，累计计数"）。

6. **C04 收口（外部审核 7e63d66）：解锁改为"发奖成功后再提交 `_unlocked` 终态"，新增可重试待领奖
   状态**——原实现 `ApplyProgress` 达标时先把 key 写进 `_unlocked`、再调用
   `IRewardDispatcher.Grant` 却不检查返回值；发奖失败（如背包已满）时成就已经判定解锁、奖励却
   一件没发，`IsUnlocked` 仍报告 `true`，玩家没有任何补领入口。与 `EncounterHost.Evaluate`（被
   外部循环反复调用、天然可重试）不同，本模块只在事件驱动 `ApplyProgress` 时才会走到这一步——
   同一条 criterion 一旦计数打满，后续同类事件会在方法顶部提前 return（`counts[i] >= target`
   守卫），不会再次落到 Grant 重试这一步，因此不能靠"下次事件自动重来"收敛，需要一份显式、可
   持久化的 pending 状态。改法：Grant 失败时不写 `_unlocked`、不发 `AchievementUnlockedEvent`，
   改记入新增的 `_pendingReward` 集合（随 `player.achievement_state` 段一并持久化，新增
   `pending_reward` 布尔字段）；新增 `IAchievementHost.RetryPendingRewards(unitId)`，供调用方在
   推断发放前置条件已恢复（如清理背包空间）后主动调用——幂等，一旦某条成就 Grant 成功立即从
   `_pendingReward` 移出并入 `_unlocked`，不会被重复调用重复发放。见 `AchievementHost.cs`
   （`ApplyProgress`/`RetryPendingRewards`/`Save`/`Load`）、`IAchievementHost.cs`、
   `AchievementHostTests.cs`
   （`Unlock_RewardGrantFails_StaysLocked_RetryPendingRewardsGrantsExactlyOnceAfterRoomFreed`）。

## CORE-170-03 根治（第十轮外部审计，P2，architecture/落地计划/audit-8160178-20260908）

`AchievementHost.Load` 修复前开头无条件清空该玩家全部进度/解锁/待领奖记录，随后才校验 `data`
形状——坏 shape（`data` 本身不是 JSON 对象，或某个成就条目不是 JSON 对象）会在清空之后才抛
`FormatException`，此时该玩家的成就状态已经丢失；逐条目提交也不是原子的，排在坏条目之前的
成就已经写入本次读档的新值，排在坏条目之后的成就完全没处理，形成半新半旧的中间态——与
`Core.Carriers.Item.EquipmentPersistable.Load` 曾经的同一类缺陷成因相同（见
`core/carriers/item/README.md` 同编号判断记录）。根治后先完整遍历校验全部条目的形状（不触碰
`_progress`/`_unlocked`/`_pendingReward` 任何一个），只有整份数据校验通过才清空该玩家既有记录
并按解析结果一次性提交。

另外，`Core.Foundation.SaveSystem.SaveSystem.Load` 回滚失败读档时，会重新调用某些段真正的
运行时逻辑（如装备段为复用真实联动会调用真正的"装备"/"卸下"操作），这类操作本身会正常派发
领域事件；本类的 `custom_event` 观察条件（如观察 `item.equipped`）此前会把"读档/回滚期间的
重放"误当成一次真实玩家操作再计一次数（真实探针复现：进度从 1 被回滚重放的事件错误推高到 2
并触发解锁）。现在 `SaveSystem.Load` 把整段"逐段 `Load` + 失败回滚"逻辑包在 `IEventBus.
SuppressDispatch()` 抑制作用域内，回滚重放产生的事件在到达本类之前就已经被丢弃，本类不需要
（也不应该）自己判断"当前是不是在读档/回滚期间"，见 `core/foundation/event_bus/README.md`
"SuppressDispatch"一节。见 `Tests.Gameplay.Assembly.
CORE_170_03_SaveRollbackEventSuppressionTests`。

## 不负责什么

- 不实现"引用对象暂缺"之外的 Expr 求值细节——`filter` 的求值宿主（`self`/`target` 绑定谁）由
  `AchievementOptions.PlayerUnitResolver` 与 `evt` 触发者共同决定，见 `Evaluate` 实现。
- 不自动向任何 `ISaveSystem` 注册自身——组装层的事（惯例同 `core/gameplay/loot`/
  `core/gameplay/world_state`）。
- 不提供成就列表/进度的 UI 呈现——只暴露 `GetProgress`/`IsUnlocked` 两个查询方法。
