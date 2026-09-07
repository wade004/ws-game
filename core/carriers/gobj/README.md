# L3 载体层 · gobj（游戏对象）

职责：落地 [07_载体层_物品生物物件.md](../../../architecture/07_载体层_物品生物物件.md) 第 3 节
`GameObject`——游戏对象模板（`gobj.template`）、类型数据（十种类型）、锁规则（`gobj.lock`，见 3.2
节）、交互分发（`interact`，二选一分发到技能/对话，见 3.3 节）、状态落 `WorldState`（见 3.4 节）。
对外契约 `IGameObjectHost`（`interact`/`tryUnlock`）已在 `core/carriers/common` 声明，本模块提供其
唯一实现 `GameObjectHost`。

依赖：`Core.Rules.csproj`（含其 `Core.Numbers`/`Core.Foundation` 传递引用）、同程序集的
`Core.Carriers.Common`（`core/carriers/common`）。不引用 `Core.Gameplay`，不引用
`Core.Carriers.Item`/`Creature`/`Summon`（背包只经 `IInventoryHost`），不使用 `UnityEngine`、
`System.Threading`、`DateTime`、`System.Random`、`System.Reflection`。

## 目录

```
gobj/
  README.md
  schema/
    GobjSchemas.cs             gobj.template / gobj.lock 的 TableSchema 声明
    GobjValidationRules.cs     type_data 字段组 / on_use 二选一 / lock.requirement 字段组三条规则
  contracts/
    GameObjectTemplate.cs      GobjKind 枚举 + 十种类型数据结构体 + GameObjectTemplate.FromRecord
    LockDef.cs                  gobj.lock 解析结果，requirement 复用 common 的 LockRequirement
    GameObjectEntity.cs         GameObject 运行期实体（Kind="gobj"，TemplateId 必填，LockId 可空）
    GobjStateKeys.cs            world.gobj.<实例id>.<字段名> key 拼接
    GobjOptions.cs               口味配置 + L4 回调委托（DialogOpener/TeleportResolver/...）
    IGobjDiagnostics.cs          最小诊断出口
  core/
    GameObjectFactory.cs         Spawn/Despawn（不读模板字段，只装配 Entity 外壳）
    GameObjectHost.cs             IGameObjectHost 实现：interact/tryUnlock + 补充能力
    GobjEffectExtension.cs       IEffectExtension 的 open_lock 分支
    InMemoryGobjDiagnostics.cs   IGobjDiagnostics 默认实现
  tests/
    ...
```

## 数据表字段（对应 `GobjSchemas.cs`）

### `gobj.template`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `gobj.<name>` |
| `name_key` | TextKey | 是 | 显示名文本键 |
| `kind` | Enum(10 值) | 是 | 见下"类型数据"表 |
| `type_data` | Object | 是 | 按 `kind` 解释，字段组完整性见 `GobjTypeDataFieldGroupRule` |
| `lock_id` | Id（Reference→`gobj.lock`） | 否 | 模板默认锁，运行期实际生效值是 `GameObjectEntity.LockId` |
| `on_use` | Object | 否 | `{kind: skill\|dialog, ref: Id}`，二选一，见 `GobjOnUseKindRule` |
| `display_ref` | Id | 是 | 指向 `display.map` |
| `tags` | List\<Id\> | 否 | 标签集合 |

`type_data` 按 `kind` 解释（见 07 第 3.1 节表格）：

| `kind` | `type_data` 形状 |
|---|---|
| `door` | `{lock_id?}`（冗余说明见下"判断记录"1） |
| `chest` | `{loot_table_ref, lock_id?}` |
| `quest_object` | `{quest_action_ref}` |
| `trap` | `{skill_id, trigger_shape}` |
| `spell_focus` | `{required_skill_tag}` |
| `gather_node` | `{loot_table_ref, respawn_after_use: Number}` |
| `teleporter` | `{teleport_target_ref}` |
| `save_point` | `{}` |
| `lever` | `{linked_object_ids: List<Id>}` |
| `sign` | `{text_key}` |

### `gobj.lock`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `gobj.lock.<name>` |
| `requirement` | Object | 是 | `{kind: item_key\|world_flag\|skill_check, ...}`，见 `GobjLockRequirementFieldGroupRule` |
| `consume_key` | Bool | 否 | `item_key` 时是否消耗钥匙，缺省 false |

`requirement` 按 `kind` 解释：

| `kind` | 专属字段 |
|---|---|
| `item_key` | `item_id: Id` |
| `world_flag` | `flag_key: Id`, `expected: Bool\|Int` |
| `skill_check` | `skill_tag: Id`, `min_value: Number` |

## 校验规则（`GobjValidationRules.cs`）

| 规则 | 检查项名 | 说明 |
|---|---|---|
| `GobjTypeDataFieldGroupRule` | `gobj_type_data_field_group` | `kind` 决定 `type_data` 内哪些字段必填（十种 kind 各一分支，见 07 第 3.1 节） |
| `GobjOnUseKindRule` | `gobj_on_use_kind` | `on_use.kind` 合法（`skill\|dialog`）且 `ref` 是合法 Id（见 07 第 3.3 节"二者二选一"） |
| `GobjLockRequirementFieldGroupRule` | `gobj_lock_requirement_field_group` | `requirement.kind` 合法（三选一）且专属字段齐全（见 07 第 3.2 节） |

以上规则通过 `IDataRegistry.RegisterValidationRule` 注册。

## 设计要点与判断记录

1. **`type_data` 的 `lock_id` 与顶层 `lock_id` 语义重叠，运行期只认顶层**：07 第 3.1 节表格把
   `door`/`chest` 的类型数据要点写成"`open_state`（经 WorldState 记录）、关联 `lock_id`"，任务拍板
   进一步把它落到 `type_data.lock_id`；但 05 第 1.3 节 `GameObject.lockId` 是顶层字段，
   `GameObjectHost.TryUnlock`/`Interact` 一律读 `GameObjectEntity.LockId`（初始通常取
   `GameObjectTemplate.LockId`，即顶层 `gobj.template.lock_id`，由生成实体的调用方决定是否照抄）。
   `type_data.lock_id` 只解析、不参与任何运行期判定——判断记录：与其发明一套"两个 lock_id 谁覆盖谁"
   的合并规则，不如保持顶层字段为唯一权威来源，`type_data.lock_id` 仅供内容作者/工具按类型数据表
   直观查看"这个门/箱子关联哪把锁"，两处应保持一致由内容评审保证，不属于本模块校验范围。

2. **不引入模板缓存层**：`core/rules/skill` 有 `SkillDefCache`，本模块的 `GameObjectHost` 每次
   `Interact`/`TryUnlock`/`TriggerTrap`/`HasSpellFocus` 都现查 `IDataRegistryView.Get`/`GetAll` 再
   `GameObjectTemplate.FromRecord` 解析。判断记录：`gobj.template` 记录数量级远小于 `skill.def`
   （场景物件而非战斗高频调用的技能），且 `Interact` 不在逐帧热路径上（由交互输入触发，不是每 tick
   都调用），省去缓存失效/一致性维护的复杂度换取实现简单，未来若性能需要可在不改变
   `IGameObjectHost` 契约的前提下补一层缓存。

3. **`InteractOutcome` 未新增取值处理"跨地图传送"**：07 第 3.6 节 `InteractResult` 契约在
   `core/carriers/common`（本任务禁止修改），只有 `Skill`/`Dialog`/`Locked`/`NoAction`/`Unknown`
   五种取值，没有"传送中"这一档。任务拍板"异图返回 DispatchedRef 供上层处理"——本模块的处理方式：
   `teleporter` 同图直接 `IUnitAccess.SetPosition`；异图（`TeleportResolverDelegate` 解析出的
   `MapId` 与游戏对象所在地图不同）不调用 `SetPosition`（`IUnitAccess` 没有"改变单位所属地图"的
   方法，切图属于场景路由/更上层职责），改为把 `teleport_target_ref` 通过 `InteractResult` 的
   `DispatchedRef` 字段带出去，`Outcome` 仍是 `NoAction`；`template.OnUse` 存在时优先按 `on_use`
   分发（`Outcome` 变为 `Skill`/`Dialog`），跨地图传送的 `DispatchedRef` 让位——这种情形下调用方
   需要自己在 `teleporter` 类型上避免同时声明 `on_use`，否则跨图引用会被 `on_use` 的分发结果覆盖，
   不再出现在 `InteractResult` 里（README 记录此限制，未在校验规则里强制禁止两者同时出现，因为
   07 文档未把这条列为强制约束）。

4. **`gather_node` 的刷新判定选择"读取时判断"而非 `Update(dt)`**：见 `GobjOptions.SimTime` 属性
   注释——不新增 `ITickPhaseHandler`，`respawn_after_use` 到期与否只在下一次 `Interact` 被调用时
   才需要知道结果，两种实现方式效果等价，前者更省。

5. **`skill_check` 的 `skill_tag` 直接作为 `IStatHost` 的属性 id**：07 第 3.2 节原文"技能相关数值
   达到门槛……单机简化为读一个数值属性比较"，任务拍板"skill_tag 形如 `stat.lockpicking` 直接作
   属性 id"——`LockRequirement.SkillCheck.SkillTag` 存的就是 `IStatHost.GetStat` 的 `stat` 参数本身，
   不做任何拼接/改写。

6. **`consume_key` 的物品移除按"背包中第一个匹配模板的实例"处理**：`IInventoryHost.RemoveItem`
   要求实例 id，而 `LockRequirement.ItemKey` 只携带模板 id（`CountOf`/`AddItem` 同样只认模板 id，
   见 07 第 1.3 节 `InventoryHost` 契约本身的设计），`GameObjectHost.ConsumeKeyItem` 遍历
   `ListItems` 找第一个 `TemplateId` 匹配的实例移除 1 个——多把同模板钥匙时"消耗哪一把"没有强制
   顺序保证（`ListItems` 顺序由 `item` 模块实现决定），本模块不对此做二次排序，因为消耗哪把钥匙
   在游戏语义上通常无差别。

7. **`GameObjectFactory` 不对外暴露为 `core/carriers/common` 契约接口**：07 第 9 节契约汇总表
   GameObject 行只登记 `GameObjectHost`（`interact`/`tryUnlock`）；"按模板生成实例"不是其它模块
   需要跨模块调用的能力（不同于 `Creature` 一侧 `ICreatureFactory` 被 `ISummonHost` 复用的场景），
   故只在本模块内部提供，不新增 common 契约（避免"没有消费者的契约"）。

8. **CR130-05 根治（外部审计 audit-5c444f1-20260908，P2）：`GobjInteractedEvent` 新增
   `TeleportTargetRef` 字段，携带"本次交互是否命中 teleporter 且需要跨地图"的唯一权威判定结果**：
   见判断记录 3 描述的双出口（`InteractResult.DispatchedRef`/事件）——`Interact` 此前只把
   `kindDispatchRef` 塞进 `InteractResult.DispatchedRef`（`InteractIntentTickHandler` 一类只读
   `Success` 的消费方看不到它），`gobj.interacted` 事件本身完全不带这份信息。外部审计发现更上层
   （`core/gameplay/assembly.GameplayAssembly`）为了在事件里也能拿到这份判定，自行订阅事件后又
   用另一个（可能是默认的）resolver 独立重新解析一遍同一个 `teleport_target_ref`——与
   `DoTeleport` 已经用（可能是调用方注入的自定义）`GobjOptions.TeleportResolver` 做出的判定形成
   双重消费，自定义同图结果可能被覆盖，resolver 显式拒绝时也可能被绕过重新传送。`Interact` 现在
   把"没有 `on_use` 可分发"（`InteractResult.DispatchedRef` 承载跨地图目标那唯一一种情形）算出的
   同一个引用一并放进 `GobjInteractedEvent.TeleportTargetRef`，`template.OnUse` 存在时固定为
   `null`（不与 skill/dialog 分发目标混同，见判断记录 3）——下游只需要读这一个字段就能知道
   "是否需要、需要传去哪"，不需要（也不应该）自己反查模板再解析一遍。消费方改动见
   `core/gameplay/assembly/README.md` 同编号条目。

## 契约缺口 / 未覆盖内容

- **`type_data.trigger_shape`（`trap`）不做结构校验**：05 第 3.5 节 `Shape` 的具体 JSON 形状由
  形状/目标相关模块定义（本模块未见其 JSON schema），本模块的 `GobjTypeDataFieldGroupRule`/
  `GameObjectTemplate.FromRecord` 只检查该字段"存在且是对象"，原样透传为 `JsonObject`，交给 L4
  区域触发装配时自行解释。
- **`type_data.sign.text_key` 不做文本键存在性校验**：`DataRecord`/`DataRegistry` 内建的
  `text_key_exists` 校验项只覆盖 `TableSchema.Fields` 声明的顶层字段，无法探进 `type_data` 这个
  `Object` 内部的 `text_key` 子字段；`GobjTypeDataFieldGroupRule` 只检查它"存在"，不检查
  `l10n.text` 里是否真的有这个键。
- **`type_data` 内的 `loot_table_ref`/`skill_id`/`quest_action_ref`/`teleport_target_ref` 等引用型
  子字段不做跨表引用完整性校验**：同上，`FieldKind.Reference` 只能声明在顶层 `FieldSchema` 上，
  本模块只检查这些子字段"是合法 Id 字符串"，不检查目标表中是否真的存在对应记录。
- **未提供 `gobj.spawned`/`gobj.despawned` 事件**：07 第 9 节契约汇总表 GameObject 行只登记
  `gobj.interacted`/`gobj.state_changed` 两个事件（不同于 `creature.spawned`/`creature.despawned`），
  `GameObjectFactory.Spawn`/`Despawn` 因此不发任何专属事件，只依赖 `IWorldSim` 自身的
  `entity.created`/`entity.destroyed`。

## 不负责什么

- 不实现 `core/carriers/item`/`creature`/`summon` 三个并行模块的任何逻辑，背包读写只经
  `Core.Carriers.Common.IInventoryHost`。
- 不实现 `WorldState`/`LootHost` 的具体存储——分别经 `IWorldFlags`/`ILootRoller` 两个依赖倒置接口
  由游戏组装根注入真实实现，本模块自身不持有任何 L4 类型的编译期引用。
- 不实现 `dialog.gossip_menu`/跨地图场景路由/存档系统/任务系统——均经 `GobjOptions` 的四个委托
  回调注入，未注入时对应交互记诊断并跳过，不阻断其余交互流程。
