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

6. **消费方反馈第 52 条（2026-09-17）：`EquippedWeaponSummary` 是 `ItemInstance` 身份字段的一份
   只读重排，不是新的数值来源**：编辑器结算预览字段 `CasterWeapon(TemplateId, QualityId,
   AffixIds)` 与 `Core.Sim.StandardPlayer.EquippedInstances`（槽位→实例 id）字段形状不同，消费方
   此前各自实现一遍"`EquipmentHost.GetAllEquippedInstances` 取 `ItemInstance` 再手工搬运三字段"
   这层薄转换。本类型只把 `ItemInstance.TemplateId`/`Quality`/`Affixes` 三个既有身份字段原样
   重排成一个三元组（`FromInstance(ItemInstance)` 静态工厂），不计算、不读取任何属性数值——同本
   节判断记录 2"`ItemInstance` 均为不可变值类型"一贯口径，用 `readonly struct` 而不是 `class`。
   放在本目录（`Core.Carriers.Common`）而不是 `Core.Sim`：`ItemInstance` 本身就在本目录，本类型
   只是它的一个视图，`Core.Carriers.Item`（`EquipmentHost` 新增
   `GetEquippedWeaponSummary`/`GetAllEquippedWeaponSummaries`，见该模块 README 判断记录）与
   `Core.Sim`（`StandardPlayer.EquippedWeaponSummaries`，见 `core/sim/README.md` 判断记录）均已
   依赖本目录，不需要为此新增依赖边。

7. **消费方反馈第五批第 1 条（2026-09-21，ADR-0062）：新增 `IInteractionTargetRegistry`——统一
   "最近可交互目标"查询，覆盖 gobj/creature/loot 三类目标**：核对后确认该查询此前在本仓库任何一层
   都不存在（不是"已有、只差 loot 覆盖"）。放在本目录而不是某个具体载体模块：目标分类只依赖
   `Core.Foundation.SimLoop.Entity.Kind`（既有的 `EntityKinds.Gobj`/`Creature`/`Loot` 三个字符串
   常量），不需要引用 `core/gameplay/loot` 等 L4 模块的具体类型（`DroppedLootEntity` 等）——接口
   与其默认实现（`Core.Carriers.Assembly.InteractionTargetRegistry`，见该模块 README）因此可以
   完全留在 L3，不违反本目录"只定义类型与接口，禁止任何业务逻辑"的边界（接口体本身不含逻辑，
   实现放在装配层）。默认实现不另开登记表，直接对 `IWorldSim.QueryEntities` 现场求值，与
   `IWorldSim` 现有的确定性排序保持一致（等距离候选取 `EntityId` 序数最小者）。生物类候选仅存活
   且有可交互内容时成立，见 architecture/adr/0065-死亡生物不是最近可交互目标的候选.md、
   architecture/adr/0069-最近可交互目标候选生物需有可交互内容.md。

8. **消费方反馈——游戏接入方第五批第 2 条（2026-09-21，[ADR-0063](../../../architecture/adr/0063-装备宿主契约补模板id查询.md)）：
   `IEquipmentHost` 新增 `GetEquippedTemplateId`/`GetAllEquippedIdentities` 两个只读默认接口成员，
   新增元素类型 `EquippedItemIdentity`**：装备面板要显示"槽位名 + 已装备物品名"，需要模板 id，
   但既有 `GetEquipped`/`GetAllEquipped` 只返回不携带模板 id 的 `ItemInstanceRef`。不复用本节
   判断记录 6 的 `EquippedWeaponSummary`：那个类型没有实例 id（回答"挂的是什么"而不是"是哪一件
   具体实例"），定位不同。默认实现恒返回 `null`/空字典（只读查询允许显式降级，惯例同
   `core/rules/common.ISkillHost` 的 `Knows`/`GetSkillNameKey`），生产实现
   `Core.Carriers.Item.EquipmentHost` 用显式接口实现转发（理由与判断记录同 `core/carriers/item/
   README.md` 对应判断记录）。`ItemInstanceRef` 与既有四个方法签名一字不动。

9. **消费方反馈——游戏接入方第十批第 1 条（2026-09-22，[ADR-0067](../../../architecture/adr/0067-生物原生交互分流拒绝死亡目标与死亡发起者.md)）：
   `InteractOutcome` 新增 `TargetDead`/`ActorDead` 两个枚举成员**：只追加，不改既有取值的数值。
   本类型原本只服务 `GameObject` 交互（见本文件顶部目录"GameObject 交互结果"注释），
   `Core.Carriers.Creature.CreatureInteractionHost.Interact`（ADR-0051）复用同一枚举——两个新成员
   目前只由生物交互宿主产出，`GameObjectHost` 尚未接入对应存活核对（gobj 不是 `IUnitAccess` 概念
   的实例，没有存活状态，不适用）。

10. **消费方反馈——游戏接入方第十四批（2026-09-22，[ADR-0069](../../../architecture/adr/0069-最近可交互目标候选生物需有可交互内容.md)）：
    `ICreatureInteractionHost` 新增默认接口成员 `HasInteractableContent(creatureInstanceId)`**：
    只读、无副作用查询，不判距离/发起者/存活，与 `Interact` 共用同一份内容判定逻辑（见
    `core/carriers/creature/README.md` 对应判断记录），供 `IInteractionTargetRegistry` 的默认实现
    过滤"存在且存活但没有任何可交互内容"的生物候选（护送/跟随/闲逛一类生物离玩家最近时被误选中，
    交互却什么都不会发生）。默认实现恒返回 `true`（"有内容"，只读查询允许显式降级，惯例同
    `IEncounterHost.TryStart`），生产实现 `Core.Carriers.Creature.CreatureInteractionHost` 用显式
    接口实现转发。`Interact` 签名不变。

## 不负责什么

- 不实现背包/装备/交互/召唤的任何具体算法——那些是 item/gobj/summon/creature 各自模块在
  `core/carriers/<module>/core/` 下的职责。
- 不提供 `IWorldFlags`/`ILootRoller` 的默认实现——真正接 L4 的工作留给游戏组装根或后续 L4 任务。
- 不解析任何数据表（`item.template`/`creature.template`/`gobj.template` 等）——数据到强类型对象的
  解析由各模块自己完成。
