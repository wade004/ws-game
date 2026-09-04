# L4 玩法层 · difficulty（难度档位）

职责：落地 08_玩法层_掉落任务对话关卡.md 第 5 节 Difficulty——按 `diff.tier` 定义的档位（修正光环、
词缀池挂载点、掉落倍率）应用到全局或单张地图，对新生成的敌对（或全体）单位施加修正光环，向
`core/gameplay/loot` 暴露 `LootMultiplier` 供 `lootMultiplierProvider` 引用。对应 01 第 L4 模块表
`difficulty` 行（契约 `DifficultyHost.apply(tierId, scope): Bool`、数据表 `diff.tier`、事件
`difficulty.applied`、策略配置项"分档集合、是否允许中途切换"）。

依赖：L0（`data_registry`/`event_bus`）、L2（`core/rules/common.IEffectSink`/`IFactionMatrix`/
`IUnitAccess`）。经 `Core.Gameplay.csproj` 既有的 `Core.Carriers`（传递到 `Core.Rules`）项目引用
可见，本模块自身不新增任何 `ProjectReference`。不引用 `core/gameplay/loot`（`LootMultiplier` 是
只读属性，由 loot 侧的组装层自行读取，见判断记录 1"依赖方向"）。

## 目录

```
difficulty/
  README.md
  contracts/
    DifficultyScope.cs        Global/Map 两值
    DifficultyTierDefinition.cs diff.tier 强类型视图 + FromRecord
    DifficultyOptions.cs      PlayerFactionId/ApplyToAll/AllowMidSwitch 策略配置
    Events.cs                 DifficultyEventKeys + DifficultyAppliedEvent
    IDifficultyHost.cs        契约接口
  core/
    DifficultyHost.cs         IDifficultyHost + IPersistable 唯一实现
  schema/
    DifficultySchemas.cs      diff.tier 的 TableSchema
  tests/
    TestSupport.cs
    DifficultyHostTests.cs    Apply/中途切换/creature.spawned 订阅/持久化用例
```

## 判断记录

1. **依赖方向：`difficulty` 不引用 `loot`，`loot` 才引用 `difficulty` 的读值**——
   `IDifficultyHost.LootMultiplier` 是本模块暴露给外部读取的只读属性，`core/gameplay/loot` 的
   `CreatureDeathLootListener`/`LootHost` 通过组装层注入的 `Func<double> lootMultiplierProvider`
   委托读取它（见 `core/gameplay/loot/core/CreatureDeathLootListener.cs` 判断记录"难度模块提供；
   默认 1"），本模块不持有、不引用 `Core.Gameplay.Loot` 任何类型——依赖单向，避免两个 L4 模块之间
   出现互相引用。

2. **`Apply` 返回 `Bool`，不是 08 第 9 节字面签名的 `Void`**：`DifficultyOptions.AllowMidSwitch`
   为 false 且已应用过某一档位时，`Apply` 不做任何改动、不发事件，直接返回 `false`；其余情况按
   请求应用并返回 `true`。未登记的 `tierId` 仍抛 `ArgumentException`（不属于"允许中途切换"判定
   覆盖的范围，是"未知 id"这一更基础的错误）。

3. **修正光环只施加给"新生成"的单位，不回溯已存在单位**：`DifficultyHost` 订阅
   `creature.spawned`（`CreatureSpawnedEvent`），`Apply` 切换难度时不遍历、不重算任何已经在场的
   单位——08 第 5.2 节"已进行中的遭遇不因难度切换而重算，只影响后续新生成的内容"。判定"是否敌对"
   经 `IFactionMatrix.IsHostile(unit, DifficultyOptions.PlayerFactionId)`；`ApplyToAll=true` 时
   跳过该判定，对全部新生成单位（含玩家阵营/中立阵营）都施加。

4. **`DifficultyAppliedEvent.ScopeId` 的取值约定**：`found.event_catalog` 只给出字段名
   `scopeId`，未规定 `DifficultyScope.Global` 时它应该是什么——本模块用固定哨兵
   `DifficultyHost.GlobalScopeId`（`"diff.scope.global"`）表示全局作用域，`DifficultyScope.Map`
   时取实际 `mapId`，二者统一用 `Id` 承载（Expr 无联合类型）。

5. **存档段 `world.difficulty`（`DifficultyHost.SectionKeyValue`）不在 `SaveSections` 登记表**：
   10 文档未给难度状态分配固定段 key，`IPersistable.SectionKey` 允许任意非空字符串（惯例同
   `core/gameplay/loot.DroppedLootPersistable`/`economy.VendorStockPersistable`），本模块直接用
   字面量，不触碰 `SaveSections`。落盘内容：`CurrentTier`/`CurrentScope`/`CurrentMapId` 三元组；
   读档不重放 `difficulty.applied`（同 `WorldState`/`SpawnHost` 判断记录"读档不是一次业务事件"）。

6. **`diff.tier.modifier_aura_refs`/`affix_pool_ref` 用 `FieldKind.IdList`/`FieldKind.Id` 而非
   `FieldKind.Reference`**：指向的表（`aura.def`/词缀池表）不在本任务数据集范围内，声明为
   `Reference` 会让 `reference_integrity` 校验因目标表未加载而恒报错（同
   `core/carriers/creature.CreatureSchemas` 同款判断记录）。

## 不负责什么

- 不实现词缀池（`affix_pool_ref`）的具体生成规则——本版只登记挂载点，不解析、不应用。
- 不提供把 `IDataRegistryView` 里的 `diff.tier` 列表转换成 UI 选项（关卡入场难度选择界面）的代码，
  那是表现层/游戏层的事。
- 不做离散战斗模式（`combat_mode_override`）相关的任何难度联动——本项目未启用离散模式（ADR-0013）。
