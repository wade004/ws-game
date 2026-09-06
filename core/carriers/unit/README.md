# L3 载体层 · unit（对象模型基类 + 移动系统）

职责：落地 [05_对象模型与世界.md](../../../architecture/05_对象模型与世界.md) 第 1.2 节 `Unit`/
`PlayerUnit`/`CreatureUnit` 三个类型、第 6 节导航与移动（`MoveRequest`、`MovementState`、移动系统）、
第 3.2 节方向量化算法，以及把 `Core.Rules.Common.IUnitAccess` 接到真实的 `Unit` 实体上
（`WorldUnitAccess`）。物品 `core/carriers/item`、生物 `core/carriers/creature`、游戏对象
`core/carriers/gobj`、召唤与宠物 `core/carriers/summon` 四个模块随后并行开发，均依赖本模块的
`Unit`/`PlayerUnit`/`CreatureUnit`/`WorldUnitAccess`。

依赖：`Core.Rules.csproj`（及其传递引用的 `Core.Numbers`/`Core.Foundation`，`Core.Foundation` 内含
`engine_adapter`/`sim_loop`/`save_system`/`event_bus`/`expr` 等子模块）、同程序集的
`Core.Carriers.Common`（`core/carriers/common`）。不引用 `Core.Gameplay`，不使用 `UnityEngine`、
`System.Threading`、`DateTime`、`System.Random`、`System.Reflection`。

## 目录

```
unit/
  README.md
  contracts/
    Unit.cs               abstract Unit : Entity（05 §1.2 字段）+ UnitCombatState 枚举
    PlayerUnit.cs           PlayerUnit : Unit
    CreatureUnit.cs         CreatureUnit : Unit
    MovementState.cs        MovementState（05 §6.2）+ MoveMode 枚举
    MoveRequest.cs           MoveRequest（05 §6.2）
    ISpatialIndexSync.cs    WorldUnitAccess 可选注入的空间索引同步小接口
    ISkillBindingHost.cs    缺口 4：技能槽位绑定契约 + KnownSkillQuery 具名委托
    HealthFractionSetter.cs W1 收边补齐：Revive 按生命值百分比恢复的具名委托（不引用
                             core/numbers/power_set 具体类型）
  core/
    WorldUnitAccess.cs       IUnitAccess 的真实实现 + Revive 窄契约（W1 收边补齐，拍板 3 前置）
    MovementHost.cs           MoveRequest → Intent 提交入口 + OnMoveFailed 回调
    MovementTickHandler.cs   TickPhase.MovementAndNavigation 的移动系统本体
    MovementOptions.cs       移动系统口味配置项
    DirectionQuantizer.cs    05 §3.2 方向量化算法（纯函数）
    UnitPersistable.cs        world.current_map_id / world.current_position / player.archetype
                              三个存档段（player.archetype 为 W1 收边补齐，A4 审计 F1）
    SkillBindingHost.cs      ISkillBindingHost 默认实现（缺口 4）
    SkillBindingPersistable.cs  player.skill_bindings 存档段（惯例同 UnitPersistable 静态工厂写法）
  tests/
    ...
```

## 设计要点与判断记录

1. **`Unit`/`PlayerUnit`/`CreatureUnit` 不重复持有"引用"性质的字段**：05 §1.2 字段表里
   `statBlock`/`powerSet`/`auras`/`threatTable`（Unit）、`inventory`/`equipment`/`skillBook`
   （PlayerUnit）本质是"由其它宿主按单位 id 索引管理的状态"，不是 `Entity` 树自身该持有的数据——
   本模块只把 `EntityId` 作为这些宿主的 key，不在类型里重复放一份引用或数据副本，避免"两份真相"
   （同一状态既存在宿主里又存在 `Unit` 实例字段里，容易不同步）。`CombatState`/`Level`/`AiState`
   是例外：它们是明确声明为"快照"的字段（05 原文/任务书拍板），由权威模块（combat/progression/ai）
   写入，本模块只提供存储位置。详见 `Unit.cs`/`PlayerUnit.cs`/`CreatureUnit.cs` 顶部注释。

2. **`CreatureUnit.TemplateId` 复用 `Entity.TemplateId`，不新建同名字段**：`Entity.TemplateId` 基类
   语义是"手工放置对象可为空"，但 05 §1.2 明确 `CreatureUnit` 必然来自某个 `creature.template`；
   本模块选择在 `CreatureUnit` 构造函数里把它变成必填参数（内部仍写回 `Entity.TemplateId` 这同一
   存储位置），而不是新增一个不可空的同名字段——后者会造成"一个类型里有两个语义重叠的模板 id 字段"
   的混乱。

3. **`WorldUnitAccess.SetAlive` 天然不销毁实体**：`Entity.Lifecycle` 是 `internal set`（仅
   `Core.Foundation.SimLoop` 程序集可写），`Core.Carriers` 程序集从任何路径都无法修改它，`SetAlive`
   只能触碰 `Unit.Alive` 这一个字段——契约层面就保证了"死亡不销毁实体"，不需要额外防御代码。

4. **`ISpatialIndexSync` 已删除——已由 ADR-0016 解决**：此前 `ISpatialQuery`（见 02 第 1.9 节）
   只有查询方法、没有登记/更新方法，本模块曾自行拍板一个补充小接口 `ISpatialIndexSync` 让
   `WorldUnitAccess.SetPosition` 写入新位置后同步空间索引；ADR-0016 决策 7 直接给
   `ISpatialQuery` 增加了 `Register`/`UpdatePosition`/`Unregister`/`Clear` 四个契约方法，
   `ISpatialIndexSync` 因此完全冗余，已删除。`WorldUnitAccess` 现改为直接持有可选的
   `ISpatialQuery`，`SetPosition` 直接调用其 `UpdatePosition`（移动同步）；首次登记
   （`Register`，创建时机）与销毁移除（`Unregister`）不是 `WorldUnitAccess` 的职责，由
   `core/carriers/assembly/EntitySpatialSyncHost` 订阅 `entity.created`/`entity.destroyed`
   统一处理（按 `Entity.Kind` 决定标签，见该类型判断记录——`creature`/`player` 打 `unit` 标签、
   `gobj` 打 `gobj` 标签，避免范围查询把物件当成单位命中）。

5. **`MovementHost.Request` 与 `MovementTickHandler` 分工**：`MovementHost.Request` 只做"把
   `MoveRequest` 转译成一条 `Kind == "move"` 的 `Intent` 并 `SubmitIntent`"这一件事，不直接改变任何
   `MovementState`；真正的位移推进、寻路、控制效果判定、事件发出全部在 `MovementTickHandler.Execute`
   （挂在 `TickPhase.MovementAndNavigation`）完成。二者靠共享同一个 `MovementHost` 实例耦合：
   `MovementTickHandler` 持有它只为了在寻路失败时调用其 `internal RaiseMoveFailed` 触发
   `OnMoveFailed` 回调（见判断记录 6），调用方对外只应使用 `Request` 与订阅 `OnMoveFailed`。

6. **`unit.move_failed` 不新增事件，改用委托回调**：found.event_catalog（data/_sample/found/
   found.event_catalog.json）未登记 `unit.move_failed` 这个 key；01 第 6 节禁止事项 5"禁止游戏层
   修改架构核心目录内的契约接口签名或新增原语而不走 ADR 流程"同样约束本任务——寻路失败是一个需要
   通知调用方的场景，但不足以构成需要新增架构级事件词汇表条目的理由，改用 `MovementHost` 自身的
   `MoveFailedHandler` 委托事件（`OnMoveFailed`）覆盖这一需求，行为等价，不触碰事件词汇表。

7. **`MovementTickHandler` 区分"目标（x,y）"与"方向（dx,dy）"两类 move 意图的持续性**：目标类
   意图只需提交一次，`MovementState.CurrentPath`/`PathIndex` 让路径推进跨多个 tick 持续，直至到达
   或被打断（见 `MovementState` 类型注释判断记录：`PathIndex` 是本模块相对 05 原文字段表的补充字段，
   05 §6.2 原表没有列出但路径跟随离不开它）；方向类意图代表"这一 tick 的输入"，不建立持久路径，
   需要调用方每 tick 重新提交（典型场景：玩家持续按住移动键）。

8. **移动速度"缺失"判定是实用近似，不是精确契约**：`IStatHost.GetStat` 对"属性未注册"与"属性
   显式设为 0"返回相同的 0（见 `IStatHost.GetBase` 注释），本层无法从返回值精确区分二者；
   `MovementTickHandler.ResolveSpeed` 按"非正值一律回退到 `MovementOptions.DefaultSpeed`"处理——
   "移动速度为 0 或负数"本就不是有意义的配置，这一近似不会掩盖任何合法的口味设定。

9. **`unit.state_changed` 的 `oldState`/`newState` 用 `MoveMode` 的 `ToString()`**：见
   `Core.Carriers.Common.UnitStateChangedEvent` 顶部判断记录——该事件 key 在 found.event_catalog
   中的字段"为建议值"，不同调用方可以承载不同的状态词汇表，本模块的用法是承载移动模式名称
   （`"Idle"`/`"Walk"`/`"Run"`/`"Forced"`），与 `core/rules/common` 的 `AiStateChangedEvent`
   （承载 `BehaviorState`）是同一个事件 key 的两种不同调用方用法。

10. **`DirectionQuantizer` 是纯函数，不知道"该不该量化"**：05 第 3.2 节"量化只对 sprite 型外形
    在表现层进行"——是否调用本函数、传入哪个 `direction_count`（4/8/16）是表现层（`presentation/
    render`，L5，不在本任务范围）按 `display.map` 的 `direction_count` 字段决定的职责，本模块只
    提供算法本身，不判断"该不该量化"。

11. **加固任务补充：`MovementOptions.UnitBlocking` 落地 05 §3.6 `unit_block` 碰撞层**——默认
    `false`（允许单位重叠，行为不变）；为 `true` 时 `MovementTickHandler` 在每次应用位移前用新增的
    可选 `ISpatialQuery` 依赖查询目标落点附近携带
    `Core.Foundation.EngineAdapter.CollisionLayers.UnitBlock` 标签、非自身的对象，命中则本次不位移
    （停在原地，不做滑动/绕行，见 `MovementTickHandler.IsBlockedByUnit` 判断记录）。`spatial` 是
    构造函数新增的末位可选参数（不插在 `navigation` 之前，避免破坏既有按位置传参的调用点），
    `CarriersAssembly` 用它已持有的 `ISpatialQuery` 实例接线。

## L2 契约缺口清单（本次未新增/未修改 `core/rules/*`）

- `Core.Rules.Common.IUnitAccess` 没有 `SetFacing`：`WorldUnitAccess` 未补这个方法（不修改
  `core/rules/common`），`MovementTickHandler` 需要写朝向时直接操作拿到的 `Unit`/`Entity` 实例的
  `Facing` 属性（`Entity.Facing` 本就是 `public get; set;`），绕过 `IUnitAccess` 完成，不影响
  L2 四模块（它们本来就不需要写朝向）。
- 其余用到的 L2 契约（`IUnitAccess`、`IStatHost`、`IAuraQuery`、`ControlFlags`）均按既有签名使用，
  未发现需要新增成员的缺口。

## 不负责什么

- 不实现 `core/carriers/item`/`creature`/`gobj`/`summon` 四个并行模块的任何逻辑，只提供它们依赖的
  `Unit`/`PlayerUnit`/`CreatureUnit`/`WorldUnitAccess`。
- 不实现 `AreaTrigger`/`WorldState`/刷新表——那些属于 L4 玩法层（不在本任务范围）。
- 不提供 `PlayerUnit.QuestLog` 的具体结构或读写逻辑——只保留一个 `JsonObject` 占位存储位，具体由
  未来的 `core/gameplay/quest` 落地。
