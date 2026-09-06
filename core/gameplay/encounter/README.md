# L4 玩法层 · encounter（遭遇与关卡）

职责：落地 08_玩法层_掉落任务对话关卡.md 第 4 节 Encounter/Level——`encounter.def`（参战单位、波次、
阶段、竞技场规则、胜负条件、奖励）的运行期实例化与胜负评估，`encounter.level`（有序遭遇序列 + 可选
难度档位）的线性推进。对应 01 第 L4 模块表 `encounter` 行（契约 `EncounterHost.start/evaluate/abort`、
数据表 `encounter.def`/`encounter.level`、事件 `encounter.started`/`wave_spawned`/`phase_changed`/
`won`/`lost`）。

依赖：L0（`data_registry`/`event_bus`/`expr`/`hook_registry`/`engine_adapter`）、L2
（`core/rules/common.ICreatureFactory`/`IAiHost`/`IUnitAccess`/`IExprHostFactory`；
`core/rules/expr_host.RulesExprSchema.Base` 默认，可由调用方传入合并后的 schema 覆盖）、
`core/gameplay/common`（`RewardBundle`/`IRewardDispatcher`）。经 `Core.Gameplay.csproj` 既有的
`Core.Carriers` 项目引用传递可见。`SpawnRequester` 委托是本模块对 `core/gameplay/spawn` 的唯一
接触点（见判断记录 2），不直接引用该模块类型。

## 目录

```
encounter/
  README.md
  contracts/
    EncounterUnitSpec.cs          units[] 一条（spawnRef 或 templateRef 二选一 + 可选 position）
    EncounterWaveDefinition.cs    waves[] 一条
    EncounterPhaseDefinition.cs   phases[] 一条
    EncounterArenaRules.cs        arena_rules 强类型视图
    EncounterDefinition.cs        encounter.def 一条记录的强类型视图
    EncounterLevelDefinition.cs   encounter.level 一条记录的强类型视图
    EncounterShapeJson.cs         arena_rules.bounds_shape 的 JSON 形状解析
    EncounterState.cs             GetState 查询用的运行期快照
    Events.cs                     EncounterEventKeys + 五个事件类型
    IEncounterHost.cs / ILevelHost.cs  两个契约接口
    SpawnRequester.cs             按 spawn.table 条目生成参战单位的具名委托（见判断记录 2）
  core/
    EncounterContentValidationRule.cs  units 二选一 + spawnRefs 域名 + 嵌套 Expr 可解析
    EncounterHost.cs               IEncounterHost 唯一实现
    EncounterShapeMath.cs          竞技场边界几何判定
    EncounterTickHandler.cs        ITickPhaseHandler：逐 tick Evaluate 全部 ActiveInstanceIds
    LevelHost.cs                   ILevelHost 唯一实现（线性推进 encounter_sequence）
  schema/
    EncounterSchemas.cs            encounter.def/encounter.level 的 TableSchema
  tests/
    TestSupport.cs
    EncounterHostTests.cs          Start/Evaluate/胜负/波次/阶段用例
    LevelHostTests.cs              StartLevel 线性推进用例
```

## 判断记录

1. **胜负条件的求值 self 绑定"发起本次遭遇的玩家单位"**：`Start(encounterId, mapId,
   playerUnitId)` 记录 `PlayerUnitId`，`Evaluate` 用
   `_exprHostFactory.CreateFor(instance.PlayerUnitId, null, null)` 求值 `victory_condition`/
   `defeat_condition`——`self.hp_pct`/`enemies.count_in_range` 等分组均以这个单位为视角，不是
   "参战单位里的某一个"的抽象聚合。

2. **`SpawnRequester` 是本模块对 `core/gameplay/spawn` 的唯一接触面**（历史契约缺口已解决）：
   `EncounterUnitSpec.SpawnRef`/`EncounterWaveDefinition.SpawnRefs` 需要"给一个 `spawn.table`
   条目 id，生成对应单位、返回生成的实体 id 列表"这一能力，`core/gameplay/spawn` 现已补上
   `ISpawnHost.SpawnNow(spawnId, mapId)`（按单个 `spawnId` 立即生成一次），签名与
   `SpawnRequester` 委托一致——`core/gameplay/assembly.GameplayAssembly` 已把 `spawnRequester`
   接线为真实实现 `Spawn.SpawnNow`（不再是"恒返回空列表"），端到端测试
   `Encounter_Start_SpawnsUnit_ViaSpawnRequester_ReachingSpawnTableEntry` 验证 `spawn_ref` 分支
   真实可用。

3. **`waves`/`phases` 为空时的默认行为**：`units[]` 描述的参战单位在 `Start` 时一次性全部生成
   （无需等待任何波次触发），`victory_condition`/`defeat_condition` 从第一次 `Evaluate` 起就参与
   判定；`arena_rules` 未提供时不做任何竞技场边界限制（无重置、无边界）。

4. **`encounterId` 事件字段落地为运行期实例 id，不是 `encounter.def` 的定义 id**：
   `found.event_catalog` 只给出字段名 `encounterId`，未区分"定义 id"与"运行实例 id"；若取定义
   id，同一 `encounter.def` 被多次 `Start`（重复挑战，或多个关卡引用同一遭遇）时订阅方无法区分
   事件来自哪一次运行。`LevelHost` 需要按实例 id 精确匹配自己刚发起的那次运行才能正确推进
   `encounter_sequence`，因此全部五个事件统一落地为实例 id（`encounter.inst_<n>`）。

5. **`LevelHost.StartLevel` 重复调用先清理上一次运行遗留的 `encounter.won` 订阅**：同一玩家中途
   放弃重开时，旧订阅若不清理会在新一轮运行期间继续存活，造成事件串扰或订阅泄漏。

6. **`combat_mode_override`/`initiative_override` 已接入真实运行时行为**（历史判断记录，保留
   备查）：`GameplayAssembly` 第 12.5 步订阅 `encounter.started`/`won`/`lost`，经
   `EncounterHost.TryGetModeOverride` 读出这两个字段后转交
   `TimeModelSwitch.SetPendingOverride`——只在离散时间模型已装配（传入 `clockHost`）时生效，
   遭遇结束（won/lost）时清空，避免残留覆盖影响下一次不相关的战斗。纯连续模式的组装根不受
   影响，`EncounterHost` 本身仍然只负责登记与读取，不直接切换结算逻辑（执行方在
   `TimeModelSwitch`）。

## 不负责什么

- 不实现 `spawn_ref` 参战来源的真实生成（见判断记录 2 的已知契约缺口）。
- 不做任何存档——`EncounterState`/运行期实例是纯瞬时状态，不实现 `IPersistable`（08 文档未要求
  遭遇进度跨读档保留，一局遭遇中途读档视为该遭遇重新开始）。
- 不提供竞技场越界后的具体表现（闪现拉回/伤害惩罚等），只给出
  `EncounterArenaRules.ResetIfLeave` 这一策略位，具体效果留给游戏层。
