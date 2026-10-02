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
    MovementState.cs        MovementState（05 §6.2）+ MoveMode 枚举（+ 运动学 Motion，手感设计/02）
    MotionKinematics.cs     运动学快照（速度/模式/来源）+ MotionMode/MotionSource（手感设计/02）
    MotionServices.cs       运动服务装配点 + 硬直/根运动/曲线/目标辅助的小接口（手感设计/02）
    KnockbackRequest.cs     击退请求（MovementHost.BeginKnockback）
    MoveRequest.cs           MoveRequest（05 §6.2）
    ISpatialIndexSync.cs    WorldUnitAccess 可选注入的空间索引同步小接口
    ISkillBindingHost.cs    缺口 4：技能槽位绑定契约 + KnownSkillQuery 具名委托
    HealthFractionSetter.cs W1 收边补齐：Revive 按生命值百分比恢复的具名委托（不引用
                             core/numbers/power_set 具体类型）
  core/
    WorldUnitAccess.cs       IUnitAccess 的真实实现 + Revive 窄契约（W1 收边补齐，拍板 3 前置）
    MovementHost.cs           MoveRequest → Intent 提交入口 + OnMoveFailed 回调
    MovementTickHandler.cs   TickPhase.MovementAndNavigation 的移动系统本体
    MovementTickHandler.Motion.cs  运动层：运动仲裁、速度积分、滑墙、动作位移、击退（partial，手感设计/02）
    MotionProfile.cs         运动档案读取（判定型视图 -> 值快照）
    MotionModeRuleSet.cs     运动模式规则表（feel.motion_mode_rules 的消费结果）
    MotionMath.cs            曲线求值/反求、速度趋近、朝向限速（纯函数）
    MotionSupport.cs         目标辅助、步态输入、击退距离的纯函数
    MotionHitFeelWiring.cs   受击裁决接进运动层：硬直查询适配 + 动作时钟/硬直/击退口接线（手感落地 S6）
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

12. **AUD-02 根治（外部审核第九轮，P2，architecture/落地计划/audit-85f1f4f-20260908）：
    `SkillBindingPersistable.Load` 对本段整体缺失（`JsonNull`）的处理，从 no-op（保留读档前的
    运行期绑定）改为先解绑该单位当前全部绑定、再按快照重建**——惯例同
    `core/carriers/item.EquipmentPersistable.Load`"先清空、再按快照重建"，修复前真实场景：同一
    宿主先后加载两个存档槽，缺本段的旧档不会清掉前一个槽留下的绑定，违反 10 第 3 节"缺失段语义"
    合同。见 `SkillBindingPersistableTests.Load_NullData_ClearsPreExistingBindings`。

13. **种族被动光环跨图丢失根治（外部审核第九轮，architecture/落地计划/audit-85f1f4f-20260908）：
    `PlayerUnit` 新增可选字段 `RaceId`，配套 `UnitPersistable.RaceId` 存档落点（`player.race_id`
    段）**——`UnitPersistable.ArchetypeId` 此前的判断记录"05 §1.2 字段表未列出独立种族引用字段，
    `Core.Numbers.Archetype.ArchetypeRegistry.ApplyTo` 的 `raceId` 参数只在应用当下使用，不会被
    任何 L3 类型记住"已不再成立：跨图 `World.ClearAll` 后需要重放种族被动光环（`Core.Rules.Assembly.RulesAssembly.
    ReapplyRacePassiveAuras`，见 `core/rules/assembly/README.md` 同编号条目），框架级的
    `GameplayAssembly.EnterMap` 需要知道"当前玩家是哪个种族"才能做这件事，不能再把这份信息完全
    丢给游戏层各自扩展。`RaceId` 可选（`Id?`）：调用方在 `RulesAssembly.RegisterUnit` 传入非空
    `raceId` 的同时，需要同步写入本字段（框架不自动同步——分层边界：`RulesAssembly`/L2 不知道
    `PlayerUnit`/L3 这个类型的存在，与 `ArchetypeId` 判断记录同款理由）。`RaceIdPersistable.Load`
    对本段整体缺失/为空按默认语义清空为 `null`，不像 `CurrentMapIdPersistable`/
    `ArchetypeIdPersistable`（见 `UnitPersistable.cs` 内两者的 `KeepStateWhenSectionMissing`
    判断记录——`Id` 没有合法的空值，缺段时保留调用前已确定的值优于覆盖成占位）那样声明"缺段即
    保留"例外——`Id?` 本身就有明确无歧义的空值，不存在"清空成什么才对"的两难。见
    `UnitPersistableTests`（`RaceId_*` 系列）、`RacePassiveAuraCrossMapTests`。

14. **C11-RELOAD 根治（2026-09-11，消费方反馈第 C11 项，基线 1.22.0，docs/消费方反馈/
    消费方反馈-2026-09-11-读档空间索引与复活生命周期.md）：`UnitPersistable.CurrentPosition`
    新增 `(PlayerUnit, IUnitAccess)` 重载，`Load` 经 `IUnitAccess.SetPosition` 写入位置**——
    此前唯一的工厂方法（`CurrentPosition(PlayerUnit)`）的 `Load` 直接改写 `_player.Position`
    字段，与判断记录 4 描述的"`WorldUnitAccess.SetPosition` 才会经 `ISpatialQuery.UpdatePosition`
    同步空间索引"这条既有规则正面冲突——`CurrentPositionPersistable` 本身不经过
    `WorldUnitAccess`，直接写字段完全绕开了判断记录 4 那套登记/同步机制。真实探针复现：保存
    `(5,5)` → 移动到 `(9,9)`（经 `WorldUnitAccess.SetPosition`，空间索引正确同步为 `(9,9)`）→
    致死 → 同图读档，读档后 `_player.Position` 正确回到 `(5,5)`，但空间索引仍登记在移动后的
    `(9,9)`（未同步的旧登记会一直留在那里），在存档位置附近查询查不到玩家。修复：新增重载接受
    `IUnitAccess`（生产装配传入 `Carriers.Units`，即判断记录 4 描述的 `WorldUnitAccess`），
    `Load` 改经其 `SetPosition` 写入，天然复用判断记录 4 的同步机制；`Save` 不受影响，仍直接读
    `PlayerUnit.Position`（两者指向同一个运行期实体，读到的值恒一致）。旧的单参数工厂方法保留
    （源码兼容，`Load` 仍是直接写字段的旧行为），本模块自身的 `GameplayAssembly.RegisterPersistables`
    已经改用新重载，见 `core/gameplay/assembly/README.md` 判断记录 14。`core/foundation/
    engine_adapter.ISpatialQuery` 同批新增 `ResyncPositions`（默认接口方法，逐个转发
    `UpdatePosition`）供派生状态重建阶段做一次全量兜底重同步，不属于本模块职责范围，接口定义与
    判断记录见该模块自身文档。见 `core/gameplay/assembly/tests/C11_LifecycleReloadTests.cs`
    （`SameMapLoad_RestoresSpatialIndexToSavedPosition`）。

15. **T-N1-6（[ADR-0030](../../../architecture/adr/0030-属性系统派生换算与来源类别.md) 决策 5；
    06 第 4.1 节 2026-09-14 修订段）：`WorldUnitAccess.GetSourceKind` 覆盖
    `IUnitAccess.GetSourceKind` 默认接口实现，复用 `Entity.Kind` 判定"单位是玩家还是生物"，
    不新造标记字段**——`PlayerUnit.Kind`/`CreatureUnit.Kind` 恒为 `EntityKinds.Player`/
    `EntityKinds.Creature`（判断记录 1 描述的既有字段，非本任务新增），供结算管线构造
    `Core.Rules.Common.EffectContext` 时填入 `SourceKind`（06 第 4.1 节"目标乘区"步骤据此按
    `stat.definition.scope` 匹配减免属性，消费方为 T-N1-7）。判断记录（未知/不存在的单位返回
    `SourceKind.Unknown` 而不是像 `GetPosition`/`GetFaction` 等既有访问器那样抛出）：本方法的
    生产调用方在构造 `EffectContext` 时查询来源单位，而来源单位在光环仍生效期间被销毁、周期效果
    仍继续结算是既有支持的边界情形（见 `core/rules/skill.EffectDispatcher.ApplyDamageOrHeal`
    判断记录 C02"来源销毁后……缩放贡献按 0 处理，效果本身继续正常结算/落地，不中断周期 tick
    循环、不抛异常"）——本方法对已销毁来源返回 `Unknown` 而非抛异常，是这条既有安全退化路径在
    本次改动后不至于反而崩溃的必要选择，与判断记录 3（`SetAlive` 不销毁实体）、上述 C02 判断
    记录同属"面对已知会发生的边界情形选择确定安全的退化路径"这一惯例。

- `Core.Rules.Common.IUnitAccess` 没有 `SetFacing`：`WorldUnitAccess` 未补这个方法（不修改
  `core/rules/common`），`MovementTickHandler` 需要写朝向时直接操作拿到的 `Unit`/`Entity` 实例的
  `Facing` 属性（`Entity.Facing` 本就是 `public get; set;`），绕过 `IUnitAccess` 完成，不影响
  L2 四模块（它们本来就不需要写朝向）。
- 其余用到的 L2 契约（`IUnitAccess`、`IStatHost`、`IAuraQuery`、`ControlFlags`）均按既有签名使用，
  未发现需要新增成员的缺口。

## CORE-170-02 根治（第十轮外部审计，P2，architecture/落地计划/audit-8160178-20260908）

新增 `WorldUnitAccess.SetLevel(unitId, level)`——不在 `IUnitAccess` 接口上（惯例同 `Revive`：
只供组合根装配期的委托闭包调用，不是 skill/combat/targeting/ai 四个模块经 `IUnitAccess` 契约会
用到的操作）。写 `Unit.Level` 实体字段，单位不存在于 `IWorldSim` 时安全 no-op、不抛异常——调用方
是 `Core.Numbers.Progression.LevelSync` 委托闭包（`CarriersAssembly` 注入），其调用时机由
`core/numbers/progression`（L1）单方面决定，本方法把"L1 调用一个跨层委托可能撞上未知单位"这种
边界情况自己吞掉，而不是让 L1 承担确认 L3 世界模拟状态的负担。根治的是 `GetLevel`（本模块既有
方法，未改动）读到的实体等级与 `Core.Numbers.Progression.ProgressionHost.GetLevel` 内部权威等级
不同步的问题（真实探针复现 `rules_progression_level=2;entity_level=1`），详见
`core/numbers/progression/README.md` 同编号判断记录。

## CORE-170-03 根治（第十轮外部审计，P2，architecture/落地计划/audit-8160178-20260908）

`SkillBindingPersistable.Load` 修复前开头无条件解绑该单位当前全部技能绑定，随后才校验 `data`
形状——坏 shape（既不是 JSON 对象，也不是每个值都合法的 `Id` 字符串）会在解绑之后才抛
`FormatException`，读档前的绑定因此丢失且不可恢复，与 `EquipmentPersistable.Load` 曾经的同一类
缺陷成因相同（见 `core/carriers/item/README.md` 同编号判断记录）。根治后先完整解析校验成临时
恢复计划（不触碰任何绑定），全部条目校验通过后才一次性解绑现有绑定、按计划重新绑定。见
`SkillBindingPersistableTests.Load_BadShape_ThrowsFormatException_AndLeavesExistingBindingsUntouched`/
`Load_NonIdSkillValue_ThrowsFormatException_AndLeavesExistingBindingsUntouched`。

## 游戏侧通用能力需求根治（移动/导航公共接口验证后提出，见 architecture 02 第 1.8 节、03 第 4.2 节、
05 第 6 节勘误）

游戏侧在 PlayMode 用已发布包验证导航/移动公共接口后提出 5 项通用能力需求：`MovementHost.Stop`/
`OnMoveStopped`、失败原因细分（`OnMoveFailedDetailed`）、`FindPath` 端点契约（含零长度目标）、
`INavigation2D.GetBlockingVersion` 与阻挡变化后的自动重验/重算策略、统一的"内部相交才受阻"可通行
规则。均是既有导航/移动契约之上的**新增能力**，不改变本任务之前任何既有行为的默认表现——新接口
成员一律走默认接口实现（`INavigation2D.GetBlockingVersion() => 0`），`MovementState`/`MoveRequest`
构造函数新增字段一律带默认值，`MovementOptions.PathFailurePolicy`/`BlockingChangePolicy` 默认值分别
是 `KeepOldPath`/`Replan`，其中 `KeepOldPath` 就是本任务之前唯一的行为（保留旧路径，不清空、不
回调）。

**生命周期与事件顺序**（`MovementTickHandler.Execute`，见该类型内部方法判断记录）：

1. 先处理本 tick 全部 `Kind == "move_stop"` 的意图（`MovementHost.Stop` 的落点）：清空对应单位的
   `CurrentPath`、状态收回 `Idle`，并计算同一 tick 内在它之前提交的该单位 `move` 意图是否因此被
   丢弃（丢弃判定：同一单位最后一条 `move_stop` 意图之前提交的 `move` 意图，视为从未提交，之后
   提交的照常生效）。确有路径被清空、或确有 `move` 意图被丢弃时触发一次 `OnMoveStopped`（原因
   `Requested`）；二者皆否时静默（幂等，见 `MovementHost.Stop` 判断记录）。不检查
   `MovementState.MovementLocked`/`ControlFlags.NoMove`——停止是取消操作，被控制效果禁止移动的
   单位仍然可以被停止。
2. 再处理本 tick 存活（未被步骤 1 丢弃）的 `move` 意图——**CORE-110-03 根治（第十二轮外部审核，
   P2，已确认，architecture/落地计划/audit-ac3b622-20260909）后**：同一单位本 tick 若存活多条
   `move` 意图，只取下标最大的**最后一条**处理，更早的视为被替换（不逐条执行），单位因此在本
   tick 至多进入一次下面的推进逻辑，固定的 `dt` 只积分一次（此前实现对每条存活意图都各自调用一
   次，导致同一单位同一 tick 提交 N 条意图会把 `speed × dt` 重复消费 N 次，真实探针复现：
   speed=10、dt=0.1 时 1/2/3 条相同目标意图分别得到 x=1/2/3，均应等于一条意图的结果 x=1）。目标类
   走 `BeginPathTo`（零长度目标——`|target - from| <= 1e-6`，与 `INavigation2D.FindPath` 端点契约
   同一常量——不建路径、不动、不回调；寻路失败触发 `OnMoveFailed` + `OnMoveFailedDetailed(NoPath)`，
   按 `MovementOptions.PathFailurePolicy` 处理；寻路成功且**进入本 tick 处理之前**（即上一 tick
   结束时）旧路径确实存在时触发 `OnMoveStopped`（原因 `Replaced`，本 tick 至多触发一次，因为本
   单位至多调用一次 `BeginPathTo`），随后立即推进本 tick 的位移）；方向类走 `ApplyDirectionalMove`
   ——**NAV-DOC-02 根治（方向移动导航阻挡契约，拍板已定，同一轮审核）后**：不再只查同帧其它单位
   阻挡就直接位移，改为对本 tick 算出的候选终点先调用一次 `INavigation2D.Raycast(mapId, from, to)`
   ——受阻则把位移截断到入射点前（按 `MovementOptions.ArrivalEpsilon` 往回收缩一小段，不直接落在
   入射点本身——`Raycast` 返回的入射点在 `IsWalkable` 的点包含判定下通常已经算"在阻挡区域内"）；
   截断后（或未受阻的原候选终点）若仍不可行走（`INavigation2D.IsWalkable` 为 false）则本次完全
   不位移；未装配导航时两项检查均跳过，与本任务之前完全一致。方向类与目标类由此遵守同一套可通行
   统一规则（02 第 1.8 节），不再只有目标类真正查询导航。
3. 再对本 tick 未被前两步处理、仍持有 `CurrentPath` 的单位（连续模式的"继续走旧路径"分支）：比较
   `INavigation2D.GetBlockingVersion(mapId)` 与建路时记录的 `MovementState.NavVersion`——相等
   （含都为 0）跳过；不同则按 `MovementOptions.BlockingChangePolicy` 处理（`Replan`：直接对当前
   位置到原目标重新整体寻路；`Revalidate`：先逐段 `Raycast` 剩余路段，无阻挡只更新
   `NavVersion`、有阻挡委托 `Replan` 的重算逻辑；`Stop`：直接清空路径、触发 `OnMoveStopped`
   （原因 `BlockingChanged`)，不尝试重算；`Ignore`：不处理，照常推进）。重算/重验失败触发
   `OnMoveFailed` + `OnMoveFailedDetailed(BlockingChanged)`，再按 `PathFailurePolicy`
   （`Stop` 分支触发的 `OnMoveStopped` 原因是 `PathFailed`，不是 `BlockingChanged`——用来区分
   "阻挡变化本身直接导致停止"与"阻挡变化触发的重算尝试失败后按失败策略停止"两种不同的因果链）。
4. 最后才真正推进本 tick 的位移（`ContinuePathCore`，逻辑不变）。

判断记录（失败回调重入安全）：`OnMoveFailed`/`OnMoveFailedDetailed`/`OnMoveStopped` 均由
`MovementHost` 的 `internal Raise*` 方法触发，`Stop`/`Request` 本身只是
`IWorldSim.SubmitIntent`（下一 tick 才生效）——游戏层在失败/停止回调内部同步调用
`Stop`/`Request` 不会在本次 `Execute` 内递归触发新的失败/停止回调，回调只会按上面四步各自的
判定条件各触发一次。

**兼容性说明**：

- `INavigation2D`：新增 `GetBlockingVersion` 为默认接口实现（`=> 0`），既有实现（含
  `Adapters.Stub.StubNavigation2D` 之外的任何游戏层/引擎适配层实现）不修改代码即可继续编译、
  继续工作，只是不参与阻挡变化自动重验（等价于 `BlockingChangePolicy.Ignore` 的效果，但更省一次
  版本比较）。
- `MovementState`/`MoveRequest` 构造函数新增的字段/参数均带默认值，源码兼容；`MovementState.
  WithLocked` 现在会保留 `NavVersion`（此前的实现在这类"仅改一个字段"的便捷方法里从未涉及
  `NavVersion`，属于新增字段后的自然补齐，不是行为变化）。
- `MovementHost.OnMoveFailed` 签名不变、既有订阅方不受影响，新增的 `OnMoveFailedDetailed`/
  `OnMoveStopped` 是并行的独立事件（不是替代关系）——两个失败事件在同一失败点同时触发。
- `MovementOptions.PathFailurePolicy`/`BlockingChangePolicy` 默认值即本任务之前的唯一行为
  （前者的默认值 `KeepOldPath` 字面意义就是"寻路失败保留旧路径继续，只回调不改状态"，后者的
  默认值 `Replan` 只有在导航实现支持版本追踪、且该地图确实发生过阻挡变化时才会被读取，未装配
  导航或使用不支持版本追踪的实现时完全不生效）。
- `StubNavigation2D`：`Raycast`/`FindPath` 共用的线段-矩形相交判定从"闭区间"改为"开区间"
  （line 与矩形边界/角点相切不再算受阻，见该类型 `ClipAxis` 判断记录）——这是对齐 02 第 1.8 节
  新勘误"统一可通行规则"的行为修正，唯一受影响的场景是线段恰好贴着阻挡矩形边界或只切过一个角点；
  真正穿过矩形内部的判定结果不变。`FindPath` 补齐端点契约（不可行走端点优先于零长度判断、零长度
  目标返回单元素路径），`GetBlockingVersion` 按地图独立计数。

## ADR-0026《技能位移的连续模式》：受控位移（Controlled Displacement）

消费方反馈"连续技能位移"（`docs/消费方反馈/消费方反馈-2026-09-11-技能位移连续模式.md`）：
`core/rules/skill` 的 `move` 效果原语此前只有一次性 `SetPosition` 的瞬移语义（合法墙前目标 leap
到墙对面时终点直接跳变穿墙）。本模块新增"受控位移"——与既有 `CurrentPath`（路径跟随）平级、互斥
的第二种由 `MovementTickHandler` 驱动的位移任务，由 `EffectDispatcher.ApplyMove`（L2）经依赖倒置
接口 `Core.Rules.Common.IControlledDisplacementSink` 发起（`MovementHost` 直接实现该接口，新增公开
入口 `BeginControlledDisplacement(ControlledDisplacementRequest)`，惯例同
`Core.Carriers.Projectile.ProjectileHost : IProjectileSpawner`）。

- **状态**：`MovementState` 新增 `ControlledDisplacementState? Displacement`（起点/终点/速度/阻挡
  策略/采样步长）与只读属性 `IsControlledDisplacementActive`；新增 6 参数构造函数重载（原 5 参数
  构造函数改为内部转发，物理签名不变，源码/二进制兼容）。
- **意图**：新增 `Intent.Kind == "move_displace"`，处理顺序排在既有 `move_stop`→`move` 之间（先
  `move_stop`→再 `move_displace`（本次新增）→再 `move`）——"最后一条生效"同一惯例（`lastDisplaceIndex`
  扫描）。`MovementTickHandler.ApplyIntent` 顶部新增判定：`Displacement.HasValue` 时拒绝本次
  `move` 意图（受控位移期间普通移动互斥，落地位置与既有"控制期间禁止移动"同一处）；既有
  `MovementHost.Stop`（`move_stop`）扩展为同时能取消受控位移（就地停止，不套用阻挡策略）。
- **推进**：`AdvanceDisplacement` 每次调用按"连续模式 `speed × dt`；离散模式一次性给出必然覆盖
  剩余全程的预算上界"计算本次可推进距离，按不超过 `ControlledDisplacementState.SampleStep` 的
  增量逐段推进，每段用 `INavigation2D.Raycast` 判定（与既有目标/方向移动同一"射线与路径段同源
  判定"）。判断记录（回退距离的下界）：受阻时的回退量（`MovementOptions.ArrivalEpsilon`）相对
  "本次 `AdvanceDisplacement` 调用开始时的位置"取下界，不是相对每个采样子步各自的临时位置——
  否则某个采样子步的候选终点恰好落在阻挡区域开区间边界上（`Raycast` 判定"不算受阻"，见
  `INavigation2D.Raycast` 判断记录"边界/角点相切不算受阻"）时，紧接着下一个采样子步的入射距离
  会是 0，回退量被 `Math.Min(0, ArrivalEpsilon)` 夹成 0，最终停在边界上而不是边界前——这是
  M-C10 反馈墙前场景在实现早期版本复现过的真实 bug（`x==1.5` 而非 `x<1.5`），已在
  `core/carriers/unit/tests/C10a_ControlledDisplacementTests.cs` 用精确坐标断言钉住。判断记录
  （即时终止而非延后一轮）：采样步恰好到达/越过终点时本轮循环内直接终止（`WriteDisplacementPosition`
  + `EndDisplacement`），不是把 `remaining` 减到 0 后指望下一次循环迭代的"到达"检查——`while`
  条件 `remaining > 0` 会在 `remaining` 恰好归零时提前退出循环，若不即时终止会导致"明明已经站在
  终点上，`Displacement` 却还留着非空"的状态泄漏（同一批开发中复现过、已修复）。
- **终止**：到达（`DisplacementArrived`）、受阻（`DisplacementBlocked`，`Stop`/`Revert` 两种子
  行为共用同一原因值）、控制打断（`DisplacementControlled`，判定同 `IsLocked`）、施法者死亡
  （`DisplacementCasterDead`，`Unit.Alive`）、显式 `Stop`（复用既有 `Requested`）——均触发既有
  `MovementHost.OnMoveStopped`，`MoveStopReason` 新增四个 `Displacement*` 枚举成员，不新增事件
  类型（决策：可观测性复用 `unit.moved`/`unit.state_changed`/`OnMoveStopped` 三个既有出口）。
- **BeginDisplacement 返回值**（判断记录）：`bool` 而非 `void`——`Execute` 只在
  `BeginDisplacement` 真正开始了一次新位移（返回 `true`）时才把该单位计入本 tick
  `processedThisTick`；忽略分支（已在位移中/被锁定/参数无效，返回 `false`）不计入，否则一条被
  忽略的多余 `move_displace` 意图会让第三遍循环（"本 tick 未收到新意图但仍在进行中"）误以为该
  单位本 tick 已处理过而跳过对其既有受控位移的续推——同一批开发中复现过、已修复（见
  `C10a_ControlledDisplacementTests.BeginDisplacement_WhileAlreadyDisplacing_IgnoresSecondRequest`）。
- **离散模式**：一次性完成整段位移，不按每回合移动预算拆分（判断记录见 ADR-0026 决策 4）——
  `AdvanceDisplacement` 的 `budget` 在 `isDiscrete` 为真时取"剩余直线距离 + 一个采样步长"的上界，
  保证 `while` 循环必然经"到达"或"受阻"分支之一返回，不会把 `Displacement` 非空状态带到下一个
  离散步。
- **兼容性**：`MovementOptions` 新增 `DefaultDisplacementSampleStep`（默认 0.5，`sample_step`
  未声明或非正值时的兜底，构造函数新增带默认值属性，源码/二进制兼容）；除上述新增成员外不改动
  任何既有公开签名。既有三种瞬移模式（`EffectDispatcher.ApplyMove` 的 `charge`/`leap`/`knockback`
  分支）完全不受影响——`motion: continuous` 分支在其之前短路返回，两段代码互不共享状态。

测试：`core/carriers/unit/tests/C10a_ControlledDisplacementTests.cs`（16 例，直接驱动
`MovementHost.BeginControlledDisplacement`：无阻挡终点/逐 tick 位置序列与瞬移一致、消费方反馈
最小场景 `stop`/`revert` 两种阻挡子行为、采样步长参数生效、控制打断/施法者死亡/显式 Stop 三类
中断就地停止、位移期间普通移动被拒绝、暂停不推进、离散模式一次性完成（含阻挡）、位移嵌套防御）；
`core/rules/skill/tests/C10a_ContinuousMoveDispatchTests.cs`、
`core/carriers/assembly/tests/C10a_ContinuousMoveEndToEndTests.cs` 见对应模块 README/测试文件头
注释。

## 不负责什么

- 不实现 `core/carriers/item`/`creature`/`gobj`/`summon` 四个并行模块的任何逻辑，只提供它们依赖的
  `Unit`/`PlayerUnit`/`CreatureUnit`/`WorldUnitAccess`。
- 不实现 `AreaTrigger`/`WorldState`/刷新表——那些属于 L4 玩法层（不在本任务范围）。
- 不提供 `PlayerUnit.QuestLog` 的具体结构或读写逻辑——只保留一个 `JsonObject` 占位存储位，具体由
  未来的 `core/gameplay/quest` 落地。

## 判断记录（`MovementTickHandler` 跳过已标记销毁的单位，2026-09-23，消费方反馈第二十二批，
architecture/adr/0079-销毁时序对齐与待销毁单位跳过处理.md）

`Execute` 内处理 `move_stop`/`move_displace`/`move` 三类意图、以及第二遍"本 tick 未收到新意图但仍
在走旧路径"的循环，各自在解出目标 `Unit` 之后新增一次 `world.IsPendingDestruction(unit.EntityId)`
判断，命中即跳过（不生成/不推进任何位移，效果同"这个单位本 tick 不存在"）——`IWorldSim.Despawn`
标记销毁与真正移除之间有一个窗口期（要等某次 `Tick` 阶段 8），窗口期内若该单位仍有已经排进
`CurrentIntents` 的 `move` 意图（典型来源：`core/rules/ai.AiTickHandler` 在更早阶段生成），
`ResolveSpeed` 会读该单位的属性——本次改动前该单位仍在 Stats 注册（见
`core/carriers/creature/README.md` 对应判断记录，销毁时序已对齐到同一时刻），不会再抛异常，但
"已经标记要消失的单位还在继续移动"本身是语义上的脏读，故一并跳过，不依赖 Stats 这一步侧面兜底。
不改变 `IWorldSim` 既有实现的行为（该方法是新增默认接口成员，未覆盖的实现恒返回 `false`，等价于
本次改动之前）。

## 判断记录（`WorldUnitAccess.SetFaction` 运行期改阵营入口，2026-09-25，[ADR-0088](../../../architecture/adr/0088-仇恨表跟随运行期阵营变化清理.md)）

消费方第三十三批反馈2：框架此前不提供任何运行期改变单位阵营的入口，消费方只能绕过直接写实体的
阵营字段，导致仇恨表/AI 目标选择等下游状态全部不知情。放在哪一层：`IUnitAccess` 是只读查询契约
（`GetFaction` 已在其上，但契约本身不含任何"改写单位状态"的方法——`SetLevel`/`Revive` 等既有
"改单位状态"入口同样只存在于具体实现 `WorldUnitAccess` 上，不上升为接口成员），故 `SetFaction`
同样只加在 `WorldUnitAccess` 这一具体类型上，与既有先例同层。新增一个带 `IEventBus` 参数的构造
函数重载（既有不带 `bus` 的构造原样保留，`_bus` 为 `null` 时调用 `SetFaction` 直接抛
`InvalidOperationException`，不静默丢事件）；`CarriersAssembly` 改用新重载装配 `Units`，是唯一
生产装配路径。`SetFaction` 读旧值、新旧相同直接返回（不产生空变更事件），不同则写入新阵营并用
`PublishImmediate`（同步派发，不进 `Enqueue` 的下一 tick 队列——参照 `FactionMatrix.SetReaction`
既有"一次性运行期状态迁移"先例，调用方能在同一行代码后立刻观察到下游清理已完成，不需要手动调用
`DispatchPending`）发布 `UnitFactionChangedEvent{unitId, oldFactionId, newFactionId}`
（`RulesEventKeys.UnitFactionChanged` / `unit.faction_changed`，登记进
`data/_framework/found/found.event_catalog.json` 并过 `gen_event_constants.py --check`）。仇恨表
一侧的订阅与清理见 `core/rules/combat/README.md` 对应判断记录。ABI：新增构造函数重载 + 新增
公开方法，不改动任何既有公开签名。

## ADR-0097《以单位为目标的追击移动请求》：追击（Chase）

消费方第四十三批反馈 1（阻塞）：既有 `MoveRequest` 只有 `ToTarget`（固定点）/`InDirection`（方向）
两种，均不随目标改变而更新，玩家侧要"追上一个单位并保持距离"只能自己逐 tick 重新提交请求。本模块
新增第三种由 `MovementTickHandler` 驱动的移动任务：`MoveRequest.ToUnit(unitId, targetUnitId,
stopRange, mode)`。

- **状态**：`MovementState` 新增 `UnitChaseState? Chase`（目标单位 id/停止距离/移动模式/上次规划
  路径时的目标位置快照），新增 7 参数构造函数重载（原 6 参数构造函数改为内部转发，物理签名不变）。
  与既有 `Displacement` 同一惯例互斥（受控位移期间 `move_to_unit` 意图被拒绝），但**不**与
  `CurrentPath`/`PathIndex`/`NavVersion` 互斥——追击复用这三个既有字段承载"当前正在跟随、终点会
  周期性重算"的路径，不是第四套独立位移字段。`MoveRequest` 新增 `TargetUnitId`/`StopRange` 两个
  只读字段与 `ToUnit` 静态工厂（新增内部构造函数重载，原 4 参数公开构造函数改为内部转发）。
- **意图**：新增 `Intent.Kind == "move_to_unit"`，与既有 `move` 共用同一组"本 tick 最后一条生效"
  的胜出下标扫描（二者是同一请求槽位的不同表现形式），处理顺序位置不变（仍在 `move_stop`/
  `move_displace` 之后）。
- **每 tick 语义**（`MovementTickHandler.AdvanceChase`）：① 目标不存在（含已标记销毁待移除）/
  已死亡/不在同一地图，经 `EndChase` 自动结束（清空 `Chase`、状态收回 `Idle`，触发既有
  `MovementHost.OnMoveStopped`，`MoveStopReason` 新增 `ChaseTargetLost`，不新增事件类型）；
  ② 朝目标更新 `Unit.Facing`；③ 滞回：用 `MovementState.Mode != Idle` 代理"当前是否在靠近"（不
  另开布尔字段）——已在靠近时距离 ≤ `StopRange` 才停，已停止时距离 &gt;
  `StopRange + MovementOptions.FollowResumeSlack` 才恢复，中间缓冲区维持现状，防止目标在
  `StopRange` 边界抖动时追击单位跟着每 tick 起停；④ 需要靠近时，仅当"尚无路径"或"目标相对上次
  规划时的位移 &gt; `MovementOptions.FollowRepathDistance` 时才重新规划，规划终点是"目标当前位置
  沿（本单位→目标）方向回退 `StopRange` 的一点"（不是目标本身——直接冲到与目标重合会在到达那一刻
  比声明的 `StopRange` 近得多，回退一个 `StopRange` 让既有的到达判定自然停在正确距离），随后复用与
  `ContinuePathCore` 相同的逐路点推进算法消费本 tick 的位移预算；到达路径终点（该次规划时的停止点
  快照）不收回 `Idle`、不清空 `Chase`，下一 tick 由①②③重新评估。
- **发起请求（`BeginChase`）**：先把 `MovementState.Mode` 重置为 `Idle` 再交给 `AdvanceChase`
  评估——"发起时是否需要立即靠近"只取决于与目标的实时距离，与发起前该单位在做什么无关；对外
  `unit.state_changed` 的"变化前"取值单独通过 `AdvanceChase` 的 `priorModeOverride` 参数传入发起
  前的真实 `Mode`，不会把这个内部占位值当成"变化前"上报出去。正在进行中的路径跟随或另一次追击，
  均算被新请求整体替换（触发既有 `OnMoveStopped(Replaced)`）；`ToTarget` 建立新路径时同样把
  "存在追击请求"计入"有旧的被替换"。既有的 `MovementHost.Stop`（`move_stop`）与方向类 `move`
  意图不必额外改动即可清空 `Chase`——它们本就通过既有的 4/6 参数 `MovementState` 构造函数整体
  重建状态，新增的 7 参数构造函数在这条转发链路末端把 `Chase` 恒置为 `null`。
- **兼容性**：`MovementOptions` 新增 `FollowResumeSlack`（默认 0.25）、`FollowRepathDistance`
  （默认 0.5），均为带默认值的可写属性，构造函数签名不变。除上述新增成员外不改动任何既有公开
  签名；`abi_probe` breaks=0。
- **重新规划路径失败**（消费方复核后拍板，修正本节最初版本"静默保留旧路径下次重试"的处理；
  [ADR-0102](../../../architecture/adr/0102-追击规划点不可达时采样候选站位点.md) 修订：判定
  "失败"之前先做一次候选站位点采样，见下方独立小节）：追击的目标在动，"移动到固定点"寻路失败时
  默认策略"保留旧路径原地不动、下次重试"这条既有语义在追击场景不成立——旧路径的终点是上一次的
  目标快照，目标下一 tick 大概率又移动了，继续沿用没有意义；若目标恰好站在一块永久不可达的区域，
  "什么都不做"会导致每个 tick 都重新调用一次 `INavigation2D.FindPath` 却每次都失败，寻路开销
  无界增长，且消费方永远收不到"到不了"的信号。改为按"移动到固定点"寻路失败的既有口径处理通知面：
  直接回退点与候选站位点全部寻路失败后（含首次规划失败）触发既有的
  `MovementHost.OnMoveFailed`/`OnMoveFailedDetailed`（复用既有的 `MoveFailReason.NoPath`，不
  新增原因值），随后直接结束本次追击（清空 `Chase`、状态收回 `Idle`）——不接入
  `MovementOptions.PathFailurePolicy` 的 `KeepOldPath`/`Stop` 二选一：那是"移动到固定点"场景的
  口味开关，追击场景的失败处理语义固定为"结束"，不做可配置。
- **已知限制**：① 移动请求（含追击）目前不参与存档序列化——本次改动之前就是如此，首次在此明确
  记录：读档后追击请求不自动恢复，需消费方读档完成后重新下达。② 追击单位靠近目标的移动预算足够
  大时，本 tick 可能一路走到与目标重合（比声明的 `StopRange` 更近），下一 tick 的距离判断才会把它
  收回停止状态，是逐 tick 结算的固有粒度问题。③ 离散模式下，目标死亡/消失只有在本单位下一次收到
  新的 `move_to_unit` 意图时才会被检测到并结束请求，与既有路径跟随/受控位移在离散模式下"仅在
  行动者自己回合处理"的惯例一致，不会在其它单位的回合中主动探测。④ 追击单位自身站在不可走格时
  （见下方独立小节"设计决定，见 ADR-0125"）不特殊处理，仍按"到不了"结束。

## ADR-0102《追击规划点不可达时采样候选站位点》：候选站位点采样（修订 ADR-0097 决策 5）

消费方第四十九批反馈 2（阻塞）：直接回退点（目标当前位置沿"单位→目标"方向回退 `StopRange` 的
一点）寻路失败时，此前立即判定为"到不了"；加了物件静态阻挡后，站在物件旁的目标从被挡一侧被追击，
这一个点常落在不可走格，即便目标自身所在格可走、停止距离圆上大多数方向也都可走。

- **采样规则**（`MovementTickHandler.TryFindStandoffCandidatePath`）：直接回退点失败且已装配
  `INavigation2D`、`MovementOptions.ChaseStandoffCandidates &gt; 1` 时才采样；以目标当前位置为
  圆心、`StopRange` 为半径，从直接回退点所在角度（"正对追击单位的方向"）起，按 `+δ, -δ, +2δ,
  -2δ, …`（`δ = 2π / ChaseStandoffCandidates`）依次生成候选点并调用 `FindPath`，取第一个成功的
  （角偏最小即绕路最短）；不重复尝试偏移 0（直接回退点已经单独试过），只补齐圆上其余
  `ChaseStandoffCandidates - 1` 个候选。全部候选（含直接回退点，最多共
  `ChaseStandoffCandidates` 次 `FindPath` 调用）都失败才真正判定为"到不了"，走既有的失败通知 +
  结束追击流程。
- **兼容性**：`MovementOptions` 新增可写属性 `ChaseStandoffCandidates`（默认 16，构造函数签名
  不变）；`≤1` 时不采样，行为与 1.82.0（本决策落地前）逐字一致；未装配 `INavigation2D` 时同样不
  采样（直接连线场景不存在"寻路失败"这一分支）。`abi_probe` breaks=0（仅新增该属性一处）。
- **设计决定，见 ADR-0125（D15）**：追击单位自身站在不可走格时，`INavigation2D.FindPath` 端点契约保证起点不可走则
  任何终点都返回 `null`，因此直接回退点与全部采样候选会一起失败，表现与"目标真的处于永久不可达
  区域"完全相同，按同一套"结束追击"处理——单位站进阻挡是放置/生成问题，不是追击系统的职责，本次
  不为此单独区分成因（见 ADR-0102"备选方案与为什么不选"）；`UnitChaseTests.ToUnit_ChaserStandsOnUnwalkableCell_*`
  钉住现行为（与目标不可达的可观测结果逐项相等）。

测试：`core/carriers/unit/tests/UnitChaseTests.cs`（10 例，在原 8 例基础上新增：①
`ToUnit_DirectStandoffPointBlocked_SamplesCandidate_SucceedsWithoutMoveFailed`——目标站在一块
阻挡矩形旁、直接回退点落在阻挡内、圆上其它点可走，验证采样找到第一个成功候选、不触发失败通知、
最终停在 `StopRange` 附近；② `ToUnit_ChaseStandoffCandidatesDisabled_DoesNotSample_BehavesLike1820`
——`ChaseStandoffCandidates = 1` 时不采样，直接回退点失败即结束追击，只调用一次 `FindPath`。原有
"目标进入永久不可达区域"用例改为按 `ChaseStandoffCandidates` 算出的公式断言 `FindPathCallCount`
（全部候选耗尽新增 `ChaseStandoffCandidates` 次调用），不再写死裸数；其余 7 例原样通过，作为
"直接点可走时行为与采样前完全一致"的阳性对照。原 8 例汇总：靠近—停止—目标走远后恢复追击的端到端
复现；停止区间滞回不动；目标死亡/不在同一地图两种自动结束路径，各自校验 `ChaseTargetLost` 触发
一次即不再重复；被 `ToTarget` 替换后停止追击；`stopRange` 非正数抛异常；目标位移低于
`FollowRepathDistance` 阈值不重新规划路径、超过阈值才触发下一次规划，并在同一用例里扩展验证目标
进入永久不可达区域时恰好触发一次寻路失败通知并结束追击、此后不再调用 `FindPath`）。

## ADR-0103《召唤物跟随点不可走时采样候选点与旧路径续推》：候选采样公式抽取 + KeepOldPath 续推缺陷根治

消费方第五十一批反馈：① 召唤物跟随（`core/carriers/summon`）的直接跟随点落进 owner 紧贴的阻挡
格时永久冻结（同 ADR-0102 的采样思路，见该模块 README）；② `HandlePathFailure` 走
`PathFailurePolicy.KeepOldPath`（默认策略）时只承诺"不清空状态"，但调用方 `Execute` 第一遍循环
已经无条件把该单位计入 `processedThisTick`，第二遍"沿旧路径续推"的循环因此 `continue` 跳过，
"保留旧路径"名不副实——旧路径本 tick 完全不推进，若调用方每 tick 都重发同一个失败的 `move` 意图
（召唤物跟随正是这种调用方式），旧路径永久冻结（消费方实测 532 tick 连续 `NoPath`）。

- **候选采样公式抽取为共享工具**：`TryFindStandoffCandidatePath` 里"以某点为圆心、按左右交替
  外扩的角度序列生成候选点"这套公式本身与目标（是给谁采样候选站位）无关，抽成本模块新增的
  `internal static` 工具 `StandoffCandidates.Enumerate(center, directionToApproacher, radius,
  count)`，供本方法与 `core/carriers/summon` 的 `SummonTickHandler.TryFollow`（ADR-0103 决定 1）
  共用同一套实现，不必各自维护一份等价逻辑。产出序列与抽取前逐位相同（`UnitChaseTests` 全部原样
  通过，证明追击行为未变）。两个模块同属 `Core.Carriers` 程序集，`internal` 直接跨模块命名空间
  可见，未新增 `InternalsVisibleTo`。
- **`HandlePathFailure` 返回值改造**：新增返回值——`KeepOldPath` 分支下是否确有可继续的旧状态
  （`MovementState.Displacement`/`Chase`/`CurrentPath` 任一非空）；`BeginPathTo`/`ApplyIntent`
  据此把该单位从本 tick 的 `processedThisTick` 里排除，让 `Execute` 第二遍循环把它当"本 tick 未
  收到新意图"的单位正常续推，本 tick 就能沿旧路径/追击/受控位移推进一次 `dt`，不必等到下一个没有
  新意图的 tick。`Stop` 策略或"本来就没有旧状态可继续"时返回 `false`，行为与本次改动之前一致。
  只影响 `move`/目标类意图（`ApplyChaseIntent`/`move_to_unit` 的失败走 `EndChase` 直接结束请求，
  不经过 `HandlePathFailure`/`PathFailurePolicy` 分支，不受影响）。三个私有方法的返回值类型改动
  （`void`→`bool`），不涉及任何公开签名，`abi_probe` 不受影响。
- **设计决定，见 ADR-0125（D14）**：候选只按 `IsWalkable` 过滤，不代表可达——这条限制与 ADR-0102 相同，见该节说明，
  两者共用同一套采样工具，限制也一并共用。

测试：既有 `PathFailurePolicy_KeepOldPath_Default_NewRequestFails_PreservesOldPath_ResumesNextTick`
改名为 `...ResumesSameTick` 并更新断言——修复前的断言"新目标寻路失败那个 tick 位置不变，要再等一
个 tick 才推进"其实是在给缺陷本体拍照，修复后同一 tick 就应该续推到底（默认
`BlockingChangePolicy.Replan` 对旧目标重算原地等价成功，随后正常推进，同
`BlockingChangePolicy_Revalidate_SegmentBlocked_ReplanAlsoFails_KeepOldPath_KeepsAdvancing` 已经
验证过的"第二遍循环续推"路径，本用例验证的是"第一遍循环失败的单位现在也能进入同一条续推路径"）。
其余 `PathFailurePolicy`/`BlockingChangePolicy` 系列用例原样通过。召唤物跟随侧的复现/不变量用例见
`core/carriers/summon/tests/SummonFollowNavigationTests.cs`（见该模块 README）。

## 判断记录（ADR-0103 决定 3 追加根治：同 tick 续推与阻挡重验重复触发失败通知，2026-09-27，
1.84.0 全量 PlayMode 回归发现）

上面"`HandlePathFailure` 返回值改造"让 `KeepOldPath` 下失败的单位不计入 `processedThisTick`，从而
进入 `Execute` 第二遍循环同 tick 续推——但第二遍循环自己也有一套`RevalidateBlocking`→`ReplanPath`
的阻挡重验逻辑：若新请求失败的目标与旧路径目标是同一个点（两者都被同一次阻挡事件挡住是常见场景），
旧路径的 `NavVersion` 仍是旧值，第二遍循环判定"阻挡已变化"，又发起一次独立的 `ReplanPath`，其内部
再次寻路失败又调用一次 `HandlePathFailure`——同一次寻路失败在同一 tick 触发两次
`OnMoveFailedDetailed`（Unity PlayMode 用例
`MovementStopAndBlockingPlayModeTests.ReRequestBlockedTarget_DefaultPolicy_KeepsAdvancingOldPath_FailsOnce`
与 `...StopInsideFailureCallback_OldPathNoLongerAdvances_NoRepeatFailure` 均实测 Expected 1 Actual 2）。

修法：把"失败后把 `NavVersion` 前移到当前阻挡版本"这条逻辑（原来只有 `ReplanPath` 内部一处）收拢进
`HandlePathFailure` 统一处理——只要 `KeepOldPath` 分支下旧路径仍存在且 `NavVersion` 与当前阻挡版本
不一致就前移。这样 `BeginPathTo` 触发的失败结束时旧路径的 `NavVersion` 已经等于当前版本，第二遍循环
的 `RevalidateBlocking` 检查版本相符直接跳过重验、走 `ContinuePathCore` 正常续推，不会再进
`ReplanPath` 重复寻路。`ReplanPath` 内原有的前移逻辑因此变成死代码一并删除。

结论：`OnMoveFailedDetailed` 恰好一次触发与 ADR-0103 决定 3"同 tick 续推旧路径"这两条要求之间，
不存在需要二选一的真实冲突——冲突只是本次改造引入的副作用，用上面的收拢修法即可同时满足，未改动
`MovementStopAndBlockingPlayModeTests` 两条既有用例的任何断言。核心回归：`dotnet test
core/carriers/tests/Tests.Carriers.csproj` 665/665 通过；Unity PlayMode 定向
`MovementStopAndBlockingPlayModeTests` 11/11 通过（含上述两条此前回归的用例）。

## ADR-0110《导航契约新增"最近可走点"与"可达最近点"》：点目标不可走/不可达时吸附到最近的可达点（2026-09-29，消费方反馈第六十批）

点目标（`MoveRequest.ToTarget` 一族）落在阻挡里，或可走却从单位处走不到（围栏另一侧、封闭院落里、地图外围阻挡带外侧）时，
`FindPath` 端点契约/连通性使建路必失败、单位原地不动。新增 `MovementOptions.UnwalkableTargetPolicy`（`Reject` 默认，
行为逐字节不变 / `SnapToNearestWalkable`）、`UnwalkableTargetSnapRadius`（8.0）、`UnwalkableTargetCandidates`（8，只服务第三方近似
实现的兜底）与 `MovementHost.OnMoveTargetAdjusted`（单位、原始请求目标、解析后目标）。取舍：

- **建路只经一个入口**：`MovementTickHandler.FindPointTargetPath`——先 `TryFindNearestReachable(单位位置, 原始目标, 半径)`
  取与单位连通的最近可走点（导航保证返回点必可 `FindPath`），再对它 `FindPath`；只有 `FindPath` 仍失败（第三方近似
  实现给出了走不到的点）才经 `FindNearestWalkableCandidates` 按几何排序依次试 `UnwalkableTargetCandidates` 个后续候选
  （跳过已试过的那个）。全部失败或半径内没有可达点返回 null 走既有 `HandlePathFailure`（失败目标仍是**原始请求目标**）。
  排序规则只在契约层 `NearestWalkableSearch` 一处实现，移动系统不重复排序。点目标的建路调用点只有两处，都接入：
  `BeginPathTo`（首次建路）与 `ReplanPath`（阻挡变化后重规划，`Replan` 直接进入、`Revalidate` 判定受阻后进入）。
  追击的两处 `FindPath`（`AdvanceChase` 直接回退点、`TryFindStandoffCandidatePath`）是 `ToUnit` 追击，不在范围。
  **为什么改用可达查询（首版是几何最近点 + 少量候选回退）**：点击点可走却不可达时，最近一批几何候选全在不可达一侧，
  候选数再大也是碰运气；由导航按连通性直接挑，一次成功。
- **重规划从原始点重新解析**：`MovementState` 新增只读属性 `RequestedTarget`（新增 8 参数构造重载，既有 4/5/6/7 参数
  构造转发、恒 `null`），仅 `SnapToNearestWalkable` 时由 `BeginPathTo`/`ReplanPath` 写入，`ContinuePathCore`/
  `HandlePathFailure`/`RevalidateRemainingSegments`/`WithLocked` 原样带过；`ReplanPath` 在该策略且属性非空时用它
  代替 `path[^1]`。`Reject` 下不写入、`ReplanPath` 仍用 `path[^1]`，与 1.87.0 逐字节一致。
- **"被调整"通知只在建路成功且解析结果 ≠ 原始点时触发**，每次解析一次（重规划再次解析各一次）；全部候选失败
  只有失败通知。零长度判断（`|target - from| <= 1e-6`）仍针对原始目标，解析后的终点若恰与当前位置重合，
  `FindPath` 返回单元素路径，随即按到达处理，不另设分支。
- **离散步**：解析发生在 `BeginPathTo`，早于既有的格子吸附（吸附作用于位置推进），因此"先解析再吸附"。
- **仍然成立的限制**：见 ADR-0110"负面"——精度一格、半径内没有可达点仍 `NoPath`（默认半径 8.0，外框很厚时点在外框外侧
  会落在半径之外，游戏可调大）、范围外的点不夹取、对召唤物跟随等同种意图一并生效、`Revalidate`/`Ignore`/`Stop` 下无实际
  重规划时不重新解析、单位自身站在阻挡里仍失败。

测试：`core/carriers/assembly/tests/ADR0110_UnwalkableTargetSnapTests.cs`（生产装配级：真实 `CarriersAssembly` +
带阻挡的桩导航；两条复现——点击厚墙正中、点击围住的房间外侧——加七支不变量：默认 Reject、外框带内、近似实现的候选回退
与耗尽、半径内无点、重规划从原始点重新解析、孤岛不试探直接选连通点并配阳性对照、缺口打开后可达点被选中）；
"两个导航实现结果一致"见 `adapters/conformance/Runtime/Navigation2DScenarios` 的最近可走点与可达最近点场景，桩与 Unity
实现共用同一组输入。

## 判断记录（意图参数 `mode`/`blocking` 只认枚举名，2026-10-01，行为收紧，[ADR-0125](../../../architecture/adr/0125-测试覆盖梳理第三批已知限制与行为语义拍板.md) 第三批探针缺陷修复）

`MovementTickHandler.ReadMode`（`mode`）与位移参数解码（`blocking`）用 `Enum.TryParse`，数字串（含未定义数字）会被采用，
例如 `blocking:"1"` 被当 `Revert`。现只认枚举名，其余走既有"无法解析 → 回退默认（mode 按意图类型的 fallback，
blocking 为 `Stop`）"分支。用例 `ADR0125_MovementIntentEnumArgsTests`（墙前受控位移：Revert 的数字串仍停在墙前）。
本次只改参数解码，未改移动/导航逻辑，未涉及需跑 `MovementStopAndBlockingPlayModeTests` 的路径（交互时序不变）。

## 判断记录（运动档案与运动仲裁器，2026-10-02，[手感设计/02](../../../architecture/手感设计/02_移动与运动仲裁.md)、ADR-0116，手感落地 S2）

`MovementTickHandler` 拆成两个 partial 文件：主文件保持既有流程，`MovementTickHandler.Motion.cs` 是运动层。入口是
`MovementHost.Motion`（`MotionServices`）：**不赋值或 `Feel` 为空即没有运动层，既有路径一字未动**；离散步与 `dt <= 0` 也不启用。

1. **缺省档案逐位等价**：档案取 `rpg_classic`（瞬时达速、瞬时转向、`walk_speed_ratio` 为 1、不滑墙）时，方向移动仍是
   `from + 单位方向 × (速率 × dt)`，速率恰为目标速度（`accel_ms`/`decel_ms` 为 0 时 `ApproachSpeed` 直接返回目标，不经运算），
   朝向速率为 0 时 `StepFacing` 直接返回目标朝向。用例 `DefaultProfile_PositionFacingAndMode_AreBitIdenticalToLegacyMovement`
   逐 tick 比较位置/朝向/模式的二进制位（方向、路径、追击、撞墙四类混合场景）。
2. **仲裁用"各来源入口处的放行判断"实现优先级**，每 tick 恰一个来源产生位移：`dead > frozen > forced > staggered > rooted >
   action|root_motion > regular`。forced（受控位移）不受 rooted/staggered 影响（优先级更高，被控制的目标仍会被击退），所以运动层启用时
   `AdvanceDisplacement`/`BeginDisplacement` 不再因 `IsLocked` 结束或拒绝位移；frozen 期间位移任务与速度原样保留，解冻后继续。
3. **运动学写回**：每 tick 末把速度、期望方向、模式、底层模式、来源、基础移速写入 `MovementState.Motion`（`MotionKinematics`）。
   `frozen` 是叠加态：`Mode` 为 Frozen、`BaseMode` 保留底层模式，速度保留。重建 `MovementState` 的既有代码路径会丢掉 `Motion`，
   但每 tick 末统一重写，所以对外可见值不丢。
4. **速度积分**：毫秒按步长折成"tick 数（实数，不取整）"，每 tick 沿曲线前进 `1/ticks`，进度到 1（容差 1e-9）吸附目标，所以
   `accel_ms = A` 的达速 tick 数恰为 `ceil(A / 步长)`，`6 × (1/60) / 0.1` 这类浮点误差不会多走一拍；曲线用反求（二分 48 次）
   从当前速度还原进度，速度是唯一积分状态，目标速度中途改变时自然续接。制动速率按"`decel_ms` 内从基础移速降到零"定，满速急停距离离散值为
   `v·dt·(N−1)/2`，与连续公式 `v·decel/2` 相差不超过半个 tick 的位移（位移按 tick 末速率计，与既有"速率 × dt"同构）。
   速度由向量长度还原，与标量速率差最后一位浮点，所以 `ApproachSpeed` 对相对 1e-9 之内的差按"已在目标速度"处理。
5. **目标速度 = 单位移动速度属性 × 倍率**（不是标定基础移速 × 倍率）：`walk_speed_ratio`/`action_move_speed_ratio` 取标定前的相对值，
   毫秒字段取绝对值，距离字段（击退、动作位移、停止距离）由 `FeelCalibration` 换算（身高倍数 × 参考身高）。移速属性为 5 的单位
   不会按标定基础移速 4 去走。`sprint_speed_ratio` 本轮不消费（没有 Sprint 移动模式）。
6. **反向策略**：`instant` 方向立即对齐期望方向、速率沿加速/制动曲线趋近目标（保留速率）；`through_zero` 反向（夹角大于 90°）先沿原方向
   制动到零，再沿新方向加速。无输入时沿原方向制动到零（`FinishMotionTick` 对没有意图的单位做减速滑行）。
7. **滑墙（`wall_slide`）**：`RaycastWithNormal` 截断后，把剩余位移沿阻挡面切向再裁决一次（最多一次、不递归）：切向 = `剩余位移 − (剩余位移·n)n`，
   `n` 是导航契约带回的命中面外法线（S2b 起；S2 原先靠轴向探测，只对轴对齐阻挡成立）。法线为零向量（实现不能确定，或起点已在阻挡内部）时不滑动、整体停下。
   滑动后速度的法向分量置零（无抖动），不滑墙则整体置零。细节与取舍见下文"S2b"判断记录。
8. **路径跟随与追击**：`apply_to_path_following` 为真时位移预算由速度积分器给出（`arrival_decel` 为真时到终点前按 `sqrt(2·a·L)` 限速，
   `a` 为基础移速/`decel` 秒数，到达时速度归零不再滑行越过终点）；为假时仍是既有的 `属性速度 × dt`。到达减速的限速按线性制动率估算，
   与制动曲线形状无关（只需要不越过终点）。追击的朝向走转向速率（`MotionFacing`）。
9. **动作位移（`action`/`root_motion` 来源）**：`MotionActionPass` 在 move 意图之前结算，胜出的 tick 压制该单位输入位移。读取
   `IActionStateQuery.Current(unit).Motion`（`ActionMotionState`）：窗口内逐 tick 位移 = `DistanceWorld × (f(p1) − f(p0))`，各 tick 之和恰为总距离；
   `charge` 额外按到目标身前 `StopDistanceWorld` 与累计已走距离（按 `CastInstanceId` 记）夹取；`blocking: slide` 沿用滑墙切向裁决。`root_motion` 驱动与
   代码驱动互斥，`IRootMotionSource.SupportsRootMotion` 为假时抛 `InvalidOperationException`，不降级。**契约新增**：`ActionState.Motion`
   与 `ActionMotion*` 类型落在 `core/rules/common/contracts/ActionMotion.cs`（`ActionState` 新增 6 参构造，5 参构造转发，ABI 只加不改），由动作时间线（S3a）填充。
10. **击退（forced 模式）**：`MovementHost.BeginKnockback(KnockbackRequest)` 提交带 `knockback`/`curve=ease_out`/`duration` 的 `move_displace`；
    曲线位移从当前位置按 `起点 + 向量 × 曲线(已过时间/总时长)` 逐 tick 取点并经导航裁决截断（起点取处理那一刻的位置，避免意图提交到处理之间位置回跳）。
    时长是游戏级选项 `MovementOptions.KnockbackDurationSeconds`（缺省 0.2，设计未给默认）；`KnockbackStack`（缺省 `Replace`，另有 `Ignore`）管叠加，
    被替换的位移以 `Replaced` 结束；`ResumePathAfterForced`（缺省 `Drop`，另有 `Resume`）管路径恢复：`Resume` 时记下被挂起路径的最终目标，位移自然结束
    （到达/受阻）后在同 tick 收尾处重新建路（`dt = 0`，下一 tick 才开始走）。追击请求不恢复。距离 = `knockback_distance`（身高倍数，标定换算）×
    (1 − 击退抗性) × 冲击等级倍率，`MotionKnockback` 只按传入的冲击倍率相乘（受击裁决切片提供）。forced 模式不转向（规则表）。
11. **受控位移期间持续的移动意图不再卡住位移（S2b 缺省路径根治）**：ADR-0026 落地时的缺陷——受控位移进行中的单位同 tick 收到 `move`/`move_to_unit` 意图，
    `ApplyIntent` 拒绝后返回 true，单位被记入 `processedThisTick`，第二遍循环跳过位移续推，每 tick 都提交输入的单位位移被卡住。S2 只在运动层启用时规避，
    现在 `DisplacementOutranksIntent`（主文件）不论运动层是否启用一律不把这种单位记入"已处理"。本 tick 刚开始的位移已在第一遍计入并推进过一次，不会推进两遍。
12. **模式规则表**：`MotionModeRuleSet.Default` 与框架数据 `feel.motion_mode_rules` 七行逐项一致（用例锁定）；`by_profile` 只对 `action` 有定义，
    `restore_previous_mode` 只对 `frozen` 有定义，别处写这两个值构造时抛异常；`staggered` 需要 `IStaggerStateQuery` 实现（受击裁决切片提供，没有实现时恒为否）。
13. **目标辅助与步态**：`TargetAssistEvaluator` 与 `ITargetAssistResolver` 只在运动侧给出纯函数与接口，缺省关闭（`MotionServices.TargetAssist` 为空）；
    步态只导出输入（`GaitInputs.From`：速度/基础移速比值 + 呈现型阈值），带滞回的 idle/walk/run/sprint 派生在表现层（S5）。
14. **装配**：本轮不接 `CarriersAssembly` 与实验室，游戏自己设置 `MovementHost.Motion`；PlayMode 侧受影响的交互时序组是
    `MovementStopAndBlockingPlayModeTests`（`MovementTickHandler.cs` 的 module_map 例外），运动层未启用时该组行为不变。

测试：`core/carriers/unit/tests/MotionArbiterTests.cs`（51 例：缺省逐位等价；`accel_ms` 达速 tick 数与速度曲线；减速与停止距离；反向策略；
转向速率；rooted/staggered/frozen/dead/forced 仲裁与优先级链；击退曲线、叠加与恢复策略；滑墙开关位移差；路径跟随与到达减速；动作位移、charge、
root_motion；规则表与档案读取；目标辅助、步态与击退距离的纯函数）。

## 判断记录（运动层遗留两项，2026-10-02，手感落地 S2b）

1. **滑墙改用带法线的射线查询**：`INavigation2D.RaycastWithNormal`（默认接口成员，ABI 只加不改；返回 `NavRayHit{Point, Normal}`，`Point` 与 `Raycast` 逐位相同，
   `Normal` 为指向自由空间一侧的单位外法线）。测试桩与 Unity 网格实现覆盖为精确法线（共用 `NavRaycastNormals.RectEntryNormal`：按被穿入的矩形面给出，轴向分量恰为 ±1/0；
   同点命中两块矩形——内角——按 `Merge` 合并，不取先登记者），其余实现走默认实现（轴向探测近似，内角/擦角给零向量即整体停下，不自己猜切向）。
   运动层只在 `wall_slide`（或动作位移 `blocking: slide`）为真时才调用带法线的查询，不滑墙的路径仍走 `Raycast`，行为一字未动。
2. **轴对齐阻挡逐位一致**：切向算式 `rem − (rem·n)n`、速度去法向 `v − (v·n)n` 在 `n = (±1,0)/(0,±1)` 下每一步都是精确运算，结果与 S2 逐轴处理逐位相同；
   `MotionArbiterTests.WallSlide_AxisAlignedBlockers_AreBitIdenticalToTheAxisProbeBaseline` 用 S2 提交上实测的 8 个场景轨迹散列（位置/朝向/速度的二进制位）锁定。
3. **有意的差异（只在 S2 的盲区）**：(a) 擦过墙角（命中点离面边缘不到一个到达容差）和斜面上，S2 探不到阻挡而整体停下，现在沿切向继续；
   (b) 内角（滑动那一段又撞上第二面墙）时，速度也去掉沿第二法线的分量，去完仍指向第一面墙（夹在内角里）则置零——S2 在非平局内角里把撞第二面墙后的速度原样留着，
   带加减速的档案下单位被钉在角里却保持着速度；位置轨迹与 S2 一致，只有速度不同（散列用例对该场景只比位置）。
4. **斜墙/墙角/擦角的运行时冒烟**（`ConvexNavigation` 测试替身：凸多边形半平面交集，Cyrus-Beck 裁剪）：45° 斜墙上每 tick 沿切向位移 = `速度·dt·(方向·切向)`，
   法向位移为零、贴墙距离恒定、速度法向分量为零；锐角内角停在角点、不进入阻挡、不抖动、速度归零；菱形（旋转 45° 的方块）擦角沿斜面爬到顶点后恢复直行；
   只有默认实现的导航在斜墙上整体停下。
5. **已知局限**：Tilemap/网格实现的阻挡是矩形并集，斜线墙在其上是阶梯形，法线逐级交替（轴向法线），不会比阶梯更平滑——需要平滑法线的游戏在阻挡数据侧处理；
   单位恰好落在斜墙的边界线上（边界相切不算受阻，上一 tick 终点正好落在线上）时，滑动目标点可能因浮点误差落入内部，被 `IsWalkable` 拒绝而该 tick 不位移，
   下一 tick 的位置仍在原处（不会陷进去）；一般情形下命中前总有到达容差的回退，不会落在线上。
6. **（缺省路径修复，记录 11）** 复现用例 `C10a_ControlledDisplacementTests.MoveIntentEveryTick_DoesNotStallAControlledDisplacement`（方向/目标点/追击三种意图各一例）：
   修复前第 2 个 tick 位移停在 1，修复后逐 tick 走 `速度·dt`、第 4 个 tick 到达并以 `DisplacementArrived` 结束。被控制（`IsLocked`）的单位同理：意图被忽略后位移交给
   `AdvanceDisplacement` 按它自己的控制规则处理，与"没有意图"时一致（此前持续输入会让它永远不结束）。

## 判断记录（受击裁决接进运动层，2026-10-02，[手感设计/03](../../../architecture/手感设计/03_攻击受击与命中.md)、ADR-0117，手感落地 S6）

补上运动层判断记录 12 里"`staggered` 需要 `IStaggerStateQuery` 实现（受击裁决切片提供）"的缺口：

1. **新增 `core/MotionHitFeelWiring.cs`**：`HitReactionStaggerQuery`（`IStaggerStateQuery` → rules 层 `IHitReactionQuery` 的适配，rules 不能引用 carriers，所以由 carriers 一侧适配）与
   `MotionHitFeelWiring.Connect(movement, clock, host)`——把行动者动作时钟接成 `frozen` 叠加态来源、把受击裁决的硬直接成 `staggered` 模式来源、把 `MovementHost` 设为受击裁决的击退口。`MotionServices.Feel`
   等其它服务仍由调用方设置（缺 `Feel` 运动层本身不开启）。**不接 `CarriersAssembly`**（并行切片也在改装配，由主会话合并后统一接）。
2. **`MovementHost` 实现 `IKnockbackSink`**：新增重载 `BeginKnockback(Id, Vec2, double, double)`（方向为零或距离非正时静默忽略），转成既有 `KnockbackRequest` 路径，既有签名不变（ABI 只加）。
3. **时序**：受击方顿帧 `t` 个 tick（仲裁器选 `frozen`，位置与受控位移原地挂住）→ 顿帧结束后的下一个 tick 起 `staggered` 共 `hit_stun_ms` 换算 tick 数（输入与转向被挡）→ 回到常规；
   击退（`forced` 优先级高于 `staggered`）在顿帧最后一个 tick 提交，解冻后以 `ease_out` 走完公式距离。用例 `tests/HitReactionMotionTests.cs`（6 例，真实 `MovementTickHandler` 与仲裁器）：
   模式序列、输入被挡、韧性未破只冻结、致死为 `Dead`、击退距离与起点、缺省档案接线前后位置/朝向/模式逐位一致。
4. **PlayMode**：改动涉及 `MovementHost.cs`（只加重载与接口声明，`MovementTickHandler.cs` 未改）；仍请主会话按 AGENTS.md 跑引擎侧 `MovementStopAndBlockingPlayModeTests` 一组。

## 判断记录（动作位移窗口结束时清零速度，2026-10-02，手感落地 S12，[手感设计/02](../../../architecture/手感设计/02_移动与运动仲裁.md) 第 4 节）

- **问题**：实验室冲刺脚本（`feel_dash`）声明位移 2.0，终点 x = 2.577：`motion_end` 之后动作模式里没有来源，仲裁走"常规来源 + `decel_ms` 制动"，把动作位移留下的末速度又滑出一段。设计没规定窗口末的速度处理（只有动作整体结束的 `keep_momentum_on_action_end`），按"位移距离 = 声明值"的直觉补条款。
- **做法**：`MotionProfile` 新增可选字段 `KeepMomentumOnMotionEnd`（档案字段 `keep_momentum_on_motion_end`，缺省假，可选——没写它的预设与数据不变）。仲裁器登记"本单位处于动作位移窗口"（`_actionMotionLive`）；窗口关闭的第一个 tick（`ActionMotionWindowOpen` 为假）把速度清零，字段为真则保留，由常规来源按 `decel_ms` 衰减。清零发生在 `GetMotionTick` 里，**不在**局部顿帧冻结的 tick 上吞掉——冻结解除后的第一个 tick 补做（`MotionArbiterTests` 里"冻结不吞掉清零"一条）。
- 影响面：只影响"带动作位移窗口且档案 `decel_ms > 0`"的动作；`decel_ms = 0` 的预设本来就瞬停，行为不变。受影响的实验室基线条目见 S12 汇报与 `lab/README.md` 判断记录 33。
- 复现/不变量：`tests/MotionArbiterTests.cs`（缺省窗口末清零、声明保留则滑行、冻结不吞清零三条）；实验室 `FeelSceneTests` 的冲刺与扑击期望按"参考身高 × 声明距离"推出。
- **需要主会话在有引擎的环境里补跑**：运动层核心逻辑改了，请按 AGENTS.md 跑引擎侧 `MovementStopAndBlockingPlayModeTests` 一组（工作树里没有引擎，没跑）。

## 判断记录（目标辅助载体与 `IUnitFacingWriter`，2026-10-02，手感落地 S3b，[手感设计/02](../../../architecture/手感设计/02_移动与运动仲裁.md) 第 5 节）

- `ActionTargetAssistAdapter`（实现规则层 `IActionTargetAssist`）与 `TargetChainAssistResolver`（实现 `ITargetAssistResolver`）：链候选里取第一个在 `max_distance`（世界单位，时间线已按标定换算）与 `max_angle_deg`
  内的存活目标；朝向修正与 `close_distance` 距离缩放复用运动侧纯函数 `TargetAssistEvaluator`（朝向修正不超过档案 `turn_assist_deg`，距离只缩不放大）。装配：
  `skillHost.AttachTimelineServices(new TimelineServices { TargetAssist = new ActionTargetAssistAdapter(new TargetChainAssistResolver(targets, units), units), ... })`，不装配即没有目标辅助（缺省关闭）。
- `WorldUnitAccess` 实现 `IUnitFacingWriter.SetFacing`：只写朝向（不经空间索引同步、不发事件），供时间线把目标辅助的朝向修正落地。该方法不在 `IUnitAccess` 上（与 `SetLevel`/`SetFaction` 同一约定：非契约的单位写操作走窄接口）。
- 复现/不变量：`tests/ActionTargetAssistTests.cs`（候选筛选、朝向修正 `min(方位角, 档案上限)` 不越界、`close_distance` 缩放、无候选静默、`SetFacing`）。

## 判断记录（单位间体积阻挡，2026-10-02，M2-C，[手感设计/02](../../../architecture/手感设计/02_移动与运动仲裁.md) 第 3.5 节、ADR-0116 勘误；手感设计未写清的规则由本切片拍板）

新增 `core/MovementTickHandler.UnitVolume.cs`（`MovementTickHandler` 的又一个 partial）与运动档案两个可选字段（`unit_body_radius`、`dodge_through_units`，`FeelFields.cs` / `MotionProfile.cs`，`MotionProfile` 加 18 参数构造函数，旧 15/16 参数构造函数保留并转发，ABI 只加）。

1. **开关**：只有运动层启用且**本单位**档案 `unit_body_radius > 0` 才进入任何体积分支；既有预设、`LegacyEquivalent`、未声明的单位逐位不变（`MotionArbiterTests.UnitVolume` 里"未声明 = 对方在别处"一条按逐位相等断言）。旧的全局 `MovementOptions.UnitBlocking`（终点判定、`unit_block` 标签、固定半径）一字未动，二者独立。
2. **成对语义**：半径取各自的档案；两方都 > 0 才互相阻挡，只有一方声明则穿过（一条测试钉住）。
3. **连续扫掠 + 回退**：线段对圆求首次进入，回退一个 `ArrivalEpsilon`（与墙体同约定，保证下一 tick 不从圆里起步，所以撞停后不蠕动）；撞停位置 = 对方位置 − 半径之和 − 回退量之内。起点已在体积内时只拦"让距离变近"，允许走开；末尾守卫保证结果不比起点更深。
4. **来源覆盖**：`regular`（输入位移，`wall_slide` 决定停下或沿切向滑一段）、`action`（动作位移，`blocking: slide` 滑开 / `stop` 停下；`dash`/`step_back` 在 `dodge_through_units` 为真时穿过，`lunge` 等永不穿过）、`forced`（击退：被挡即 `DisplacementBlocked` 收场，不滑）、方向/点/追击意图的路径跟随（停下，开 `wall_slide` 时滑开）、追击（有效停步距离取声明值与"半径之和 + 2 个到达容差"的较大者）。
5. **为什么穿过只给冲刺/后撤、不给扑击**：闪避的语义就是无敌位移穿过敌人，扑击是追击型位移，穿过木桩会让"扑到敌人面前"失去意义；用字段而非硬编码，是游戏仍可自己决定（只对 `dash`/`step_back`，其它种类要穿过需新增字段）。
6. **滑开细节**：切向那段仍过地形 `Raycast` 与 `IsWalkable`、再做一次扫掠（忽略刚撞的那个单位），最多一次不递归。

**已知限制（如实记录）**：
- 不做"推开重叠单位"：出生重叠或穿过式冲刺落进体积内时，只拦让距离变近的位移，允许走开，不强制推出。
- 路径跟随与追击不绕单位寻路：撞到单位体积就停（开 `wall_slide` 时沿切向滑一段），不重规划。
- 同一 tick 里先撞墙滑动又撞单位体积时，单位扫掠对切向段用直线近似。
- 受控位移（击退）不滑，被挡住的单位不会被推动。
- 单位按顺序处理，一个单位只看得到别人**当前**的位置（先走的单位先占位）。
- 死亡单位不阻挡；离散步（回合制）不受影响。
- 体积半径随运动档案走：全局预设下所有单位同半径，需要不同半径靠角色/单位覆盖行。
- 复现与不变量：`tests/MotionArbiterTests.UnitVolume.cs`（边界停止期望由半径与步长算出、6 个随机种子的"从不重叠"不变量并断言确实发生过碰撞、冲刺高速不隧穿、冲刺穿过、滑开、追击、击退、死亡不阻挡）；实验室脚本 `feel_unit_block`（`lab/README.md` 判断记录 34）。
- **需要在有引擎的环境里跑**：运动层核心逻辑改了，按 AGENTS.md 跑引擎侧 `MovementStopAndBlockingPlayModeTests` 一组。
