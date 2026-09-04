# L3 载体层 · common 共享契约

职责：为 L3 载体层四个模块——物品 `core/carriers/item`、生物 `core/carriers/creature`、游戏对象
`core/carriers/gobj`、召唤与宠物 `core/carriers/summon`——以及本目录旁的 `core/carriers/unit`（对象
模型基类 + 移动系统）提供共用的契约接口与数据类型（见
[07_载体层_物品生物物件.md](../../../architecture/07_载体层_物品生物物件.md)、
[05_对象模型与世界.md](../../../architecture/05_对象模型与世界.md)、
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) 第 3 节"同层模块之间只经契约接口与事件
总线交互"）。**本目录只定义类型与接口，禁止任何业务逻辑**；四个模块随后并行实现，互相之间只依赖
本目录的类型，不直接引用对方模块内部的类型。

依赖：仅 `Core.Rules.csproj`（及其传递引用的 `Core.Numbers`/`Core.Foundation`）；不引用
`Core.Gameplay`，不使用 `UnityEngine`、`System.Threading`、`DateTime`、`System.Random`、
`System.Reflection`。

## 目录

```
common/
  README.md
  contracts/
    IWorldFlags.cs        依赖倒置接口：WorldState 最小只读+写子集（由 L4 实现，组装期注入）
    ILootRoller.cs         依赖倒置接口：按掉落表抽取物品（由 L4 loot 实现，组装期注入）
    ItemStack.cs            模板 id + 数量的堆叠
    ItemInstance.cs         运行期物品实例（实例 id + 模板 id + 数量 + 扩展字段）
    ItemInstanceRef.cs      物品实例的不透明引用句柄
    EquipResult.cs           穿戴结果（Ok/Fail 工厂）+ EquipFailureReason
    InteractResult.cs        GameObject 交互结果 + InteractOutcome
    LockRequirement.cs       锁的判定条件（item_key|world_flag|skill_check 判别联合）
    IInventoryHost.cs        背包契约（item 模块实现）
    IEquipmentHost.cs        装备栏契约（item 模块实现）
    IGameObjectHost.cs       GameObject 交互契约（gobj 模块实现）
    ISummonHost.cs            召唤与宠物契约（summon 模块实现）
    ICreatureFactory.cs      按模板生成/移除生物实体契约（creature 模块实现）
    Events.cs                 CarriersEventKeys + 07 契约汇总表对应的强类型事件
  tests/
    ...（类型与事件字段测试）
```

## 谁实现、谁调用

| 契约 | 由谁实现 | 谁调用 |
|---|---|---|
| `IInventoryHost`、`IEquipmentHost` | `core/carriers/item` | 装备/掉落拾取/商店交易等一切读写背包与装备栏的调用方 |
| `IGameObjectHost` | `core/carriers/gobj` | 交互系统、`open_lock` 效果原语 |
| `ISummonHost` | `core/carriers/summon` | `summon` 效果原语、AI/玩家对召唤物的管理界面 |
| `ICreatureFactory` | `core/carriers/creature` | `core/gameplay/spawn`（L4，不在本任务范围）、`ISummonHost` 的实现 |
| `IWorldFlags` | **L4**（`core/gameplay/world_state`）或游戏组装根 | `IGameObjectHost` 的实现（07 第 3.2、3.4 节：锁的 `world_flag` 判定、`GameObject.state` 落地为 WorldState 标志） |
| `ILootRoller` | **L4**（`core/gameplay/loot`）或游戏组装根 | `IGameObjectHost` 的实现（`chest`/`gather_node` 开箱/采集掉落）、`ICreatureFactory`/生物死亡结算的调用方（生物掉落） |

**L3 不依赖 L4，L4 通过 `IWorldFlags`/`ILootRoller` 反向注入**：这是 01 第 8 节"跨层调用的三种合法
方式"里的第 3 种（策略注入/依赖倒置）——07 文档要求 `GameObject` 状态落地为 `WorldState`、
`chest`/`gather_node` 产出掉落，但 `WorldState`/`LootHost` 都是 L4 玩法层模块（见 01 L4 模块表），
L3 载体层不得直接引用 L4（见 01 第 3 节依赖矩阵）。本目录把这两处依赖收窄成两个最小接口，
由游戏组装根在装配期把 L4 的真实实现（或测试假实现）注入给 `IGameObjectHost` 的具体实现，
L3 程序集本身对 L4 零编译期引用。

`ItemStack`/`ItemInstance`/`ItemInstanceRef`/`EquipResult`/`InteractResult`/`LockRequirement` 等纯
数据类型不属于上表——它们是四个模块共用的"词汇"，不由谁"实现"，按需直接引用。

## 设计要点与判断记录

1. **`IWorldFlags`/`ILootRoller` 是 05/07 对应接口的最小子集，不是完整搬运**：`IWorldFlags` 不含
   `onChanged` 订阅（05 第 8.2 节 `WorldState.onChanged`）——订阅需求属于表现层/玩法层自己的职责，
   本层用不到；`ILootRoller` 不含 `LootHost.roll` 完整签名里可能存在的上下文参数扩展，只取
   `07`/`05` 明确要求的"按掉落表 + 来源 + 击杀者抽取"这一最小能力。

2. **`ItemInstance`/`ItemStack` 均为不可变值类型**：呼应 `core/rules/common` README"结算管线均为
   不可变类型"的一贯做法，`ItemInstance.Extra` 作为 07 第 1.6 节"可选扩展点"（耐久/词缀/随机属性）
   的统一落地位置，默认空 `JsonObject`，避免未来加入某个扩展点时改动本类型签名。

3. **`ICreatureFactory.Despawn`/`CreatureDespawnedEvent.Reason` 用自由字符串而非强类型两值枚举**：
   05 第 5.3 节 `SpawnHost.notifyDespawn` 契约给出的是 `reason: died|despawned` 两个具体值，但
   `Despawn` 的调用方不止刷新表一处（`ISummonHost`/未来的 `core/gameplay/spawn` 都可能调用），
   为避免未来出现两值之外的分类时又要扩枚举，本次按 `core/rules/common` `AuraRemovedEvent.Reason`
   同款处理方式保留为自由字符串。

4. **`GobjStateChangedEvent.OldValue`/`NewValue` 用 `ExprValue` 承载**：07 第 3.4 节
   `GameObject.state` 落地为 `WorldState` 标志，其取值类型与 `IWorldFlags`/10 第 2.3 节
   `world_state_flags`（`Bool｜Int`）一致，复用同一套 `ExprValue` 判别联合，不另造一套值类型。

5. **`unit.state_changed` 在本目录的用法与 `core/rules/common` 的 `AiStateChangedEvent` 不冲突**：
   found.event_catalog 里 `unit.state_changed`/`ai.state_changed` 是两个不同的 key
   （分别对应"外显移动状态"与"AI 行为外壳状态"），本目录的 `UnitStateChangedEvent` 对应前者，
   由 `core/carriers/unit` 的 `MovementTickHandler` 在 `MoveMode` 变化时发出，`OldState`/`NewState`
   取 `MoveMode` 的 `ToString()`；本类型不绑定具体状态词汇表（用自由字符串），因为 `unit.moved`/
   `unit.state_changed` 二者在 found.event_catalog 中的字段描述本就是"字段为建议值"，留给具体调用
   方按需承载。

## 不负责什么

- 不实现背包/装备/交互/召唤的任何具体算法——那些是 item/gobj/summon/creature 各自模块在
  `core/carriers/<module>/core/` 下的职责。
- 不提供 `IWorldFlags`/`ILootRoller` 的默认实现——真正接 L4 的工作留给游戏组装根或后续 L4 任务。
- 不解析任何数据表（`item.template`/`creature.template`/`gobj.template` 等）——数据到强类型对象的
  解析由各模块自己完成。
