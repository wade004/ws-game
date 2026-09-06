# L4 玩法层 · spawn（刷新）

职责：落地 05_对象模型与世界.md 第 5 节"刷新与重生"——`spawn.table` 按 `respawn_policy` 四种策略
（`on_map_enter｜once｜never｜timer`）结合 `WorldState` 计算某地图当前应存在的刷新点实例并生成，
`content_ref` 指向 `creature.template`（经 `ICreatureFactory`）或 `gobj.template`（经
`SpawnOptions.GobjSpawner` 委托）。对应 01 第 L4 模块表"刷新"行（契约 `SpawnHost`、数据表
`spawn.table`、事件 `spawn.executed`、策略配置项"刷新策略（进图刷新/条件刷新）"）。字段表已在
05 第 5.1 节完整定义（08 第 8 节"Spawn 归属说明"明确"刷新表的字段与重生策略已在 05 中完整定义"），
本模块不额外拆分 `schema/*.md` 文档分片。

依赖（按实际 `using` 语句核实）：

- L0：`Core.Foundation.Common`、`Core.Foundation.Common.Json`、`Core.Foundation.DataRegistry`、
  `Core.Foundation.EventBus`、`Core.Foundation.Expr`、`Core.Foundation.SaveSystem`、
  `Core.Foundation.SimLoop`（`SpawnValidationRules.cs` 用，具体符号未在本次读码范围内展开）。
- L2：`Core.Rules.Common`（`IExprHostFactory`、`CarriersEventKeys`/`CreatureDespawnedEvent`，
  `condition` 求值与 `creature.despawned` 事件订阅用）、`Core.Rules.ExprHost`
  （`RulesExprSchema.Compose`，`condition` 解析 schema 用）。
- L3：`Core.Carriers.Common`（`ICreatureFactory`、`EntityKinds`，生物侧生成与 `content_ref` 域名
  判定用）、`Core.Carriers.Creature`（`ICreatureTemplateQuery`/`NpcFlag`，仅
  `SpawnSummonOnlyCreatureRule` 使用，校验 `summon_only` 生物不得出现在 `spawn.table`）。
- L4 姊妹模块（同一 `Core.Gameplay` 程序集内跨模块引用，不产生新 `ProjectReference`）：
  `Core.Gameplay.WorldState`（`IWorldState`，`once` 策略"是否已生成"标志的权威存储、`condition`
  引用 `world` 分组用）。

经 `Core.Gameplay.csproj` 既有的 `Core.Carriers` 项目引用传递可见，本模块不新增任何
`ProjectReference`。

## 目录

```
spawn/
  README.md
  contracts/
    Events.cs                    SpawnEventKeys + SpawnExecutedEvent
    ISpawnDiagnostics.cs         本模块最小诊断出口（Warn/Error 两级）
    ISpawnHost.cs                 刷新与重生契约（05 第 5.3 节四方法 + 任务书拍板补充的
                                  Update/TriggerNever/SpawnNow/UnloadMap）
    RespawnPolicy.cs             四种重生策略枚举 + wire 名互转
    SpawnOptions.cs               WriterId/PlayerUnitResolver/GobjSpawner 构造期策略配置
    SpawnRecord.cs                GetSpawnRecord 返回的只读快照
    SpawnSchemas.cs                spawn.table 的 TableSchema
  core/
    InMemorySpawnDiagnostics.cs  ISpawnDiagnostics 默认实现
    SpawnHost.cs                   ISpawnHost + IPersistable 唯一实现
    SpawnTableDef.cs               DataRecord -> SpawnTableDef 强类型视图（运行期与校验期共用）
  schema/
    SpawnValidationRules.cs      三条内容校验规则（respawn_timer 字段组一致性、content_ref
                                  域名与目标存在性、summon_only 生物排除）——校验规则代码，
                                  不属于本次新增文档范围
  tests/
    SpawnHostTests.cs
    SpawnPersistableTests.cs
    SpawnTableDefTests.cs
    SpawnValidationRuleTests.cs
    TestSupport.cs
```

## 判断记录

1. **`once` 策略"是否已生成"的权威存储是 `WorldState` 标志，不是本模块自身状态**：
   `SpawnHost.HasOnceDone`/`MarkOnceDone` 读写 `world.spawn.<name>.done`（`OnceFlagKey`），
   `SpawnRecord.OnceTriggered` 只是按需组装的只读快照，因此 `SpawnRecord` 本身是不可变值对象——
   这与 05 第 5.2 节"是否已生成经 `WorldState` 一个自动分配的标志记录"原文一致。

2. **`content_ref` 域名为 `creature` 与为 `gobj` 时走两条不同路径**：`creature` 域名直接依赖
   `ICreatureFactory`（L3 `core/carriers/common` 契约，该接口自身注释已点名"`core/gameplay/spawn`
   可复用本接口生成生物实体"）；`gobj` 域名改用 `SpawnOptions.GobjSpawner` 委托而不是直接引用
   `Core.Carriers.Gobj.GameObjectFactory`——该类型自身注释明确"不对外暴露为 common 契约接口"。
   判断记录：能复用既有契约的直接依赖，不能的改用委托注入，避免新增一条 `core/carriers/gobj` 的
   编译期依赖。

3. **"玩家可空"与 `IExprHostFactory.CreateFor` 的 `selfId` 不可空之间的契约缺口**：05 第 5.1 节
   `condition` 引用 `world` 分组时隐含"玩家可空"的期望，但 `IExprHostFactory.CreateFor(Id selfId,
   ...)` 的 `selfId` 是不可空的 `Id`（`core/rules/common` 现有签名的既有限制，本模块不允许改动）。
   `SpawnHost` 在 `SpawnOptions.PlayerUnitResolver()` 返回 `null` 时改用占位 `Id`
   （`NoPlayerSentinel = "spawn.no_player_sentinel"`）代替，不是真正的"无 self"求值——`self` 分组
   查询会按"缺失 → 默认值"处理，`world` 分组查询不受影响（不读 `selfId`）。这是一处记录在案、
   不由本模块修复的契约缺口。

4. **`SpawnNow` 是核对 08 号文档后补齐的方法**：`ISpawnHost` 接口注释称"核对 08 号文档发现'按 id
   立即生成'未列在本契约方法清单里"，补充 `SpawnNow(spawnId, mapId)`，签名与
   `Core.Gameplay.Encounter.SpawnRequester` 委托一致，供组装层把它接给 `EncounterHost` 的同名回调。
   行为等价于 `TriggerNever` 去掉"未知 id 抛异常"、加上一次 `mapId` 校验（与该条目 `map_id` 不一致
   时记诊断并跳过，防止调用方对地图归属做出错误假设）。

5. **存档段 `"spawn_state"` 只存 `timer` 剩余时间与累计生成次数，不存 `once` 标志**：`once` 标志
   的权威存储已经是 `WorldState`（见判断记录 1），会随 `WorldState` 自己的存档段持久化，不需要
   `spawn_state` 重复存一份；`spawn_state` 段 key 未登记进 `SaveSections`（`core/foundation/
   save_system` 不在本模块改动范围），直接使用字面量，惯例同 `core/gameplay/loot.
   DroppedLootPersistable`。

6. **`ApplyForMap`/`Update`/`SpawnNow`/`TriggerNever` 对 `SpawnTableDef.FromRecord` 解析失败的
   处理不一致**：`ApplyForMap`/`Update` 遍历全表时捕获 `DataFieldException` 记一条 Error 诊断后
   跳过该条继续处理其余记录（不因一条坏数据阻断整批刷新）；`TriggerNever` 对单条记录解析失败则
   不捕获、直接向上抛出（该方法本身在记录不存在时已经抛 `ArgumentException`，解析失败被视为同一
   类"调用方传入了有问题的 spawnId"）；`SpawnNow` 则捕获并转换成 Error 诊断 + 返回空列表（不抛
   异常）。三者对同一种失败采取不同的处理方式，是各方法各自独立实现时的取舍差异，未见任务书
   统一规定，此处如实记录供后续复核。

7. **判断记录 6 已解决（W2 收边补齐）**：`TriggerNever` 现在也对 `SpawnTableDef.FromRecord`
   解析失败（记录存在但字段非法）捕获 `DataFieldException`，记一条 Error 诊断后直接返回，不再
   向上抛出——与 `ApplyForMap`/`Update`/`SpawnNow` 三者对同一失败模式的处理方式统一为"记诊断并
   跳过该条，不抛异常"（11 第 4 节错误处理约定：数据内容问题按诊断降级，不假设调用方能处理
   异常）。回归测试 `SpawnHostTests.TriggerNever_RecordParseFailure_RecordsErrorAndSkips_DoesNotThrow`。
   **判断记录 6 里"记录不存在"（未知 `spawnId`）与"记录存在但字段非法"这两类失败的区分本身不变**：
   本条统一只覆盖后者——`TriggerNever` 对未知 `spawnId` 仍然抛 `ArgumentException`（调用方传参
   错误），`SpawnNow` 仍然记诊断并返回空列表（`ISpawnHost.SpawnNow` 文档注释明确以此与
   `TriggerNever` 对比，该对比继续成立）。

## 不负责什么

- 不实现难度倍率、遭遇内联生成等与刷新表并列的生成路径——08 第 8 节"Spawn 归属说明"明确
  `encounter.def.units`/`Level` 可以选择引用刷新表条目（`spawnRef`）或直接内联
  `creature.template` + 坐标（不经过持久刷新点，遭遇结束即销毁），二者的选择与实现属于
  `core/gameplay/encounter` 的职责。
- 不销毁实体本身——`UnloadMap` 只清理本模块对该地图各刷新点持有的运行期实例引用，`NotifyDespawn`
  只是被动接收外部（如 `creature.despawned` 事件）通知后更新自身运行期记录，不主动发起销毁。
- 不解决判断记录 3 描述的 `IExprHostFactory.CreateFor` selfId 不可空的契约缺口本身。
- 不校验 `content_ref` 目标行是否存在、`summon_only` 生物是否误登记——这两项由本模块自有的
  `IValidationRule`（`schema/SpawnValidationRules.cs` 的 `SpawnContentRefRule`/
  `SpawnSummonOnlyCreatureRule`）在内容管线阶段负责，不在 `SpawnHost` 运行期做重复检查；这两条
  校验规则与 `SpawnRespawnPolicyFieldGroupRule` 均不由 `data_registry` 自动注册，调用方需要显式
  `RegisterValidationRule`。
