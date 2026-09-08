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

5. **存档段 `world.difficulty`（`DifficultyHost.SectionKeyValue`）现已登记进 `SaveSections`
   （W2 收边补齐，A4 审计 F2）**：10 文档给该段分配的固定位置是第 3 节步骤 7a（世界附属段），
   本模块自身仍直接用字面量 `"world.difficulty"` 作为 `IPersistable.SectionKey`（不新增对
   `core/foundation/save_system` 的编译期依赖），但 `SaveSections.KnownOrder` 现已把这个字符串
   值登记为 `SaveSections.WorldDifficulty` 常量，固定其相对于其余已知段的排序位置（不再按 key
   序数排在全部已知段之后）。落盘内容：`CurrentTier`/`CurrentScope`/`CurrentMapId` 三元组；
   读档不重放 `difficulty.applied`（同 `WorldState`/`SpawnHost` 判断记录"读档不是一次业务事件"）。

6. **`diff.tier.modifier_aura_refs`/`affix_pool_ref` 用 `FieldKind.IdList`/`FieldKind.Id` 而非
   `FieldKind.Reference`**：指向的表（`aura.def`/词缀池表）不在本任务数据集范围内，声明为
   `Reference` 会让 `reference_integrity` 校验因目标表未加载而恒报错（同
   `core/carriers/creature.CreatureSchemas` 同款判断记录）。

## CORE-170-03 根治（第十轮外部审计，P2，architecture/落地计划/audit-8160178-20260908）

`DifficultyHost.Load` 修复前开头无条件把 `CurrentTier`/`CurrentScope`/`CurrentMapId` 三个字段
重置为 `null`，随后才校验 `data` 形状——坏 shape（既不是 `JsonNull` 也不是 `JsonObject`）会在
重置之后才抛 `FormatException`，读档前已经生效的难度层级/范围/地图因此丢失，与 `Core.Carriers.
Item.EquipmentPersistable.Load` 曾经的同一类缺陷成因相同（见 `core/carriers/item/README.md`
同编号判断记录）。根治后把形状校验前移到任何状态变更之前。见
`DifficultyHostTests.Load_BadShape_ThrowsFormatException_AndLeavesCurrentDifficultyUntouched`。

## 不负责什么

- 不实现词缀池（`affix_pool_ref`）的具体生成规则——本版只登记挂载点，不解析、不应用。
- 不提供把 `IDataRegistryView` 里的 `diff.tier` 列表转换成 UI 选项（关卡入场难度选择界面）的代码，
  那是表现层/游戏层的事。
- 不做离散战斗模式（`combat_mode_override`）相关的任何难度联动——ADR-0013 离散时间模型现已接线
  （见 `core/gameplay/encounter/README.md`/`GameplayAssembly` 判断记录），但本模块（难度档位）
  与该覆盖字段没有交集，08/06 文档均未要求二者联动，不是遗留限制。
