# L3 载体层 · summon（召唤与宠物）

职责：落地 [07_载体层_物品生物物件.md](../../../architecture/07_载体层_物品生物物件.md) 第 4 节
"召唤与宠物"——`ISummonHost` 的默认实现 `SummonHost`（复用 `core/carriers/creature` 的
`ICreatureFactory` 生成召唤物实体，额外维护"召唤物 → 拥有者 + 剩余时长"索引）、跟随/进出战斗
联动逻辑 `SummonTickHandler`、`summon` 效果原语的落地出口 `SummonEffectExtension`。

依赖：`Core.Rules.csproj`（及其传递引用的 `Core.Numbers`/`Core.Foundation`）、同程序集的
`Core.Carriers.Common`（`ICreatureFactory`/`ISummonHost`/事件）、`Core.Carriers.Unit`（`MoveMode`、
`Core.Carriers.Unit.Unit.FactionId`）。不引用 `Core.Gameplay`、`Core.Carriers.Item`、
`Core.Carriers.Gobj`，不使用 `UnityEngine`、`System.Threading`、`DateTime`、`System.Random`、
`System.Reflection`。

## 目录

```
summon/
  README.md
  contracts/
    SummonOptions.cs           口味配置项（跟随距离、是否参战、进出战斗联动、阵营继承…）
  core/
    SummonHost.cs                ISummonHost 默认实现 + 内部驱动版本扩展方法
    SummonTickHandler.cs        挂在 TickPhase.AiDecision 的跟随/联动/生命周期逻辑
    SummonEffectExtension.cs    IEffectExtension，落地 EffectKind.Summon
  tests/
    ...
```

W1 收边补齐：`SummonOptions.cs` 从 `core/` 移入新增的 `contracts/` 目录，与本仓库其余载体层模块
（`item`/`gobj`/`creature`）"公开契约/配置放 `contracts/`，实现放 `core/`"的既有布局惯例对齐；
命名空间（`Core.Carriers.Summon`）不变，纯物理位置调整，不影响任何调用方（`csproj` 用 SDK 风格
通配符引用，不需要同步改 `.csproj`）。

## 设计要点与判断记录

1. **`SummonHost` 只负责生成/登记/取消，跟随与联动逻辑放在 `SummonTickHandler`**：`Summon`/
   `Dismiss`/`GetOwner`/`GetSummons` 四个 `ISummonHost` 方法只涉及"生成实体 + 维护索引"，不触碰
   移动或战斗系统；`SummonHost` 的构造依赖因此收窄为 `ICreatureFactory`（生成/移除实体）、
   `IWorldSim`（读取拥有者所在地图、读写新生成实体的 `FactionId` 实现阵营继承）、`IUnitAccess`
   （读取拥有者朝向/阵营）、`IEventBus`（发 `summon.created`/`summon.expired`）。真正的跟随位移
   （需要 `IUnitAccess` 读位置 + `IWorldSim.AppendCurrentIntent` 提交意图）与进出战斗联动（需要
   `Core.Rules.Common.ICombatHost`）在 `SummonTickHandler` 里完成，其 `Execute(step, world)` 已经
   拿到 `IWorldSim` 参数，不需要额外持有 `MovementHost`。

2. **`ISummonHost.Dismiss(Id)` 不带 reason 参数，`SummonHost` 追加了一个内部驱动版本重载
   `Dismiss(Id, string reason)`**：`ICreatureFactory.Despawn` 的 `reason` 是自由字符串分类（调用方
   不止一处），但 `ISummonHost.Dismiss` 契约签名只有 `Dismiss(Id summonId)`（07/06 原文未要求携带
   分类）。`SummonTickHandler` 需要区分"到期"（`"expired"`）/"拥有者丢失"（`"owner_lost"`）/"主动
   取消"（`"dismissed"`，`ISummonHost.Dismiss(Id)` 固定走这一分类）三种场景各自的
   `creature.despawned` reason，因此 `SummonHost`（具体类）在契约之外追加了带 reason 的重载——
   惯例同 `core/rules/ai` 的 `AiHost` 在 `IAiHost` 契约之外追加 `RegisterUnit`/`Step` 等"内部驱动
   版本"方法的做法。

3. **`SummonTickHandler` 挂在 `TickPhase.AiDecision` 阶段，且需要先于 `AiTickHandler` 注册**：
   07 第 4 节"跟随规则...优先转入向 owner 靠拢的移动，而非按自身 patrol/chase 逻辑"——任务书
   拍板把召唤物的跟随意图放在 AI 决策阶段、且注册顺序上先于生物自身的 `core/rules/ai`
   `AiTickHandler`，使跟随意图相对生物自身的 AI 决策"优先"。同一 `TickPhase` 内多个处理器按
   `RegisterPhaseHandler` 调用顺序依次执行（见 `Core.Foundation.SimLoop.ITickPhaseHandler` 注释），
   具体的注册先后顺序由组装层（不在本任务范围）保证，本模块只负责"注册在这一阶段"这一半。

4. **跟随意图用 `IWorldSim.AppendCurrentIntent` 而非 `SubmitIntent`**：`SummonTickHandler` 挂在
   `TickPhase.AiDecision`（阶段 2），随后 `TickPhase.MovementAndNavigation`（阶段 4）的
   `MovementTickHandler` 要在**同一 tick** 内消费到这条跟随 `move` 意图，才能让召唤物与拥有者的
   位置在同一 tick 保持同步（而不是永远慢一拍）；`SubmitIntent` 提交的意图要到下一 tick 才进入
   `CurrentIntents`（见 `IWorldSim.SubmitIntent` 注释），因此改用
   `AppendCurrentIntent`（见该方法注释"让本 tick 内产生的意图立即参与本 tick 剩余阶段"）。

5. **跟随目标点选在"距 owner `FollowStopDistance` 处"而非 owner 的精确坐标**：避免召唤物移动到
   与 owner 完全重合的位置（视觉/碰撞上都不合理）；`FollowDistance`（超出才触发跟随）与
   `FollowStopDistance`（跟随时目标点离 owner 多远停下）是两个独立的策略配置项，07 第 4 节只说
   "跟随距离"是策略配置项，未展开"跟随后停在多远"这一细节，本模块按上述方式合理展开。

6. **`JoinCombat` 语义：为 true（默认）时召唤物在战斗中不被跟随逻辑打断，为 false 时召唤物
   永远视为"不参战"、恒可被跟随逻辑接管移动**：07 第 4 节"是否参战...是策略配置项"——
   `JoinCombat=true` 的召唤物战斗中应该由它自己的战斗/AI 系统控制移动（不被跟随覆盖）；
   `JoinCombat=false` 的召唤物是纯跟随型宠物，即使 `ICombatHost.IsInCombat` 因 `SyncCombatState`
   联动而返回 true，也不应该停止跟随——落地为"距离 > FollowDistance 且（召唤物不在战斗中 或
   JoinCombat=false）→ 跟随"这一条件。

7. **`ShareThreat`/`PlayerCanControl` 已驱动真实运行期行为（收边任务补齐，文档勘误）**：
   07 第 4 节把"是否共享仇恨表"/"玩家是否可操控"列为策略配置项——`ShareThreat=true` 时
   `SummonTickHandler.ShareThreatWithOwner` 每 tick 把召唤物累计到的仇恨并入 owner 的仇恨表
   （经注入的 `Core.Rules.Common.IThreatTable` 契约，非新增原语，只是把已有契约接进本模块）；
   `PlayerCanControl=true` 时 `SummonHost.IsControllableByOwner` 允许 owner 本人直接操控该召唤物
   （供输入系统在下发移动/技能意图前先做权限判断）。二者均默认 `false`，关闭时行为与"只是数据位"
   完全一致，不影响既有调用方。

8. **召唤物不进存档**：见 07 第 4 节"召唤物生命周期...不进存档（同 05 对 CreatureUnit 的默认
   结论），读档后由拥有者相关状态...驱动重建"——本模块（`SummonHost`）的 `_summons`/`_byOwner`
   索引是纯内存状态，本任务不新增任何存档段（`core/carriers/unit` 的 `UnitPersistable` 只处理
   `world.current_map_id`/`world.current_position` 两个通用段，与召唤物专属状态无关）；重建召唤物
   是拥有者相关状态驱动的 L4 职责，不在本任务范围。

## 契约缺口清单

- `Core.Rules.Common.IAuraQuery.IsImmune` 不读取 `CreatureUnit.Immunities`（见
  `core/carriers/creature` README 判断记录 2，本模块生成的召唤物同样受此限制）。
- `ISummonHost.Dismiss(Id)` 契约签名不带 reason，`SummonHost` 用重载方法补齐（见判断记录 2，
  不修改 `core/carriers/common` 的既有契约签名）。

## 不负责什么

- 不实现召唤物的存档重建——由拥有者相关状态驱动，属于 L4 职责。
- 不实现 `EffectKind.Summon` 之外任何原语的落地（`SummonEffectExtension.TryHandle` 对其余五类
  原语一律返回 false）。
