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
    GobjValidationRules.cs     type_data 字段组条件必填规则（on_use/lock.requirement 两条纯结构
                                 规则已被 Variants 登记退役，ADR-0019 F1c）
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
| `type_data` | Object | 是 | 按 `kind` 解释，字段并集登记（`GobjSchemas.TypeDataSchema`），字段组完整性见 `GobjTypeDataFieldGroupRule` |
| `lock_id` | Id（Reference→`gobj.lock`） | 否 | 模板默认锁，运行期实际生效值是 `GameObjectEntity.LockId` |
| `on_use` | Object | 否 | `{kind: skill\|dialog, ref}`，二选一，ADR-0019 F1c 登记为 Variants（`GobjSchemas.OnUseSchema`） |
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
| `requirement` | Object | 是 | `{kind: item_key\|world_flag\|skill_check, ...}`，ADR-0019 F1c 登记为 Variants（`GobjSchemas.RequirementSchema`） |
| `consume_key` | Bool | 否 | `item_key` 时是否消耗钥匙，缺省 false |

`requirement` 按 `kind` 解释：

| `kind` | 专属字段 |
|---|---|
| `item_key` | `item_id: Reference(item.template)`（同层 `Core.Carriers` 程序集，登记为 Reference） |
| `world_flag` | `flag_key: Id`（`world.flag_schema` 属 L4，退回 Id）, `expected: Bool\|Int`（联合类型，不登记，见判断记录 11） |
| `skill_check` | `skill_tag: Id`, `min_value: Number` |

## 校验规则（`GobjValidationRules.cs`）

| 规则 | 检查项名 | 说明 |
|---|---|---|
| `GobjTypeDataFieldGroupRule` | `gobj_type_data_field_group` | `kind` 决定 `type_data` 内哪些字段必填（十种 kind 各一分支，见 07 第 3.1 节）。**不退役**：判别字段 `kind` 与 `type_data` 不同级，`Variants` 不适用（见判断记录 10），条件必填仍需独立规则表达 |
| `GobjLockWorldFlagExpectedRule` | `gobj_lock_world_flag_expected` | P2-03 根治（外部审计 audit-c9ff301-20260909）：`requirement.kind == "world_flag"` 时 `expected` 必填且类型必须是 Bool 或 Number（`LockDef.RequireExprValue` 实际接受集合）。`RequirementSchema` 的 `world_flag` 变体只登记了 `flag_key`（`expected` 是联合类型，`FieldKind` 无法表达），退役 `GobjLockRequirementFieldGroupRule` 时遗漏了这一条，此前缺失/错型 0 error、只在 `LockDef.FromRecord` 才抛异常 |

以上规则通过 `IDataRegistry.RegisterValidationRule` 注册（`CarriersSchemaCatalog.RegisterAll` 已默认注册全部三条）。

### ADR-0019 F1c 退役规则

`GobjOnUseKindRule`（检查名 `gobj_on_use_kind`）与 `GobjLockRequirementFieldGroupRule`（检查名
`gobj_lock_requirement_field_group`）两条规则**整条删除**：判别字段 `kind` 分别与 `on_use`/
`requirement` 本身同处一个 JsonObject 内，改用 `VariantSchema` 登记（`GobjSchemas.OnUseSchema`/
`RequirementSchema`）后，原两条规则的全部检查项——判别字段存在且是字符串、取值在合法集合内、
各分支专属字段必填——已被 `data_registry` 内置的 `variant_discriminator`/`required_field`/
`field_type` 三个检查项完全覆盖，不再需要手写规则；`gobj_on_use_kind`/`gobj_lock_requirement_field_group`
两个检查名从此消失，同一坏形状改由 `variant_discriminator`/`required_field` 报告（不双报，见
`GobjSchemaCoverageTests`）。测试从 `GobjValidationRuleTests` 迁移至 `GobjSchemaCoverageTests`。

`GobjTypeDataFieldGroupRule` 不退役（判断记录 10）：`type_data` 的判别字段是外层 `gobj.template.kind`，
不在 `type_data` 对象内部，`VariantSchema.Discriminator` 要求判别字段与被判别对象同处一个
JsonObject（同 `core/gameplay/area_trigger` 的 `ParamsSchema`/`trigger_type` 判断记录），因此
`type_data` 改用 `Fields` 登记十种 `kind` 分别用到的字段并集（全部非必填），"哪些字段必填视 `kind`
而定"这条条件必填业务判断继续留在 `GobjTypeDataFieldGroupRule`。本轮新增的是此前完全没有的
"存在时类型必须合法"校验（`skill_id` 的 `reference_integrity`、`text_key` 的 `text_key_exists`
等），与 `GobjTypeDataFieldGroupRule` 的必填性检查互不重叠，不会对同一缺陷双报。

### P2-01 ABI/API 兼容 façade（外部审计 audit-c9ff301-20260909）

`GobjOnUseKindRule`/`GobjLockRequirementFieldGroupRule` 两个 1.12 public 类型已按 [12_扩展与变更流程](../../../architecture/12_扩展与变更流程.md) 的兼容承诺补回（标 `[Obsolete]`，逻辑与 1.12 完全一致），供未重新编译的旧调用方/旧编译产物继续构造、注册、运行，不再 `MissingMethodException`。新代码应改用上一节的 `VariantSchema`/`GobjLockWorldFlagExpectedRule` 声明式登记；显式注册这两个 façade 会与内建结构校验（以及 `GobjLockWorldFlagExpectedRule`）对同一坏形状重复报告，是已知的、可接受的兼容代价，不是新缺陷。

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

   **CR140-03 根治（外部审计 audit-c86bfa9-20260908，P2）：上一段"下游……不需要（也不应该）自己
   反查模板再解析一遍"这句判断只对了一半**——`TeleportTargetRef` 携带的始终是原始未解析的
   `teleport_target_ref` 字符串（"值就是原始 `teleport_target_ref`"，本条上一段原文），下游拿到
   它之后，若不想重复反查模板，仍然只能拿这个原始 ref 去调用某个 resolver 才能落地到具体地图/
   位置——而"某个 resolver"实际是消费方自己的默认 resolver，不是 `DoTeleport` 内部真正用到的那
   个（可能是调用方注入的自定义）`GobjOptions.TeleportResolver`，构成两次独立解析同一个 ref（外部
   审计复现：跨图自定义解析结果被消费方默认 resolver 的解析结果覆盖）。根治：`GobjInteractedEvent`
   新增 `ResolvedTeleportTarget` 字段（`(Id MapId, Vec2 Position)?`），由 `DoTeleport`（本模块内
   唯一读取 `TeleportResolver` 的地方）在判定"确实需要跨地图"时，把已经解析出的具体
   `(MapId, Position)` 一并带出（经 `ExecuteKindBehavior` 的新增 `out` 参数），`Interact` 与
   `TeleportTargetRef` 同一个非空条件一起放进事件。下游现在只需要落地这个已解析的结果，不需要
   （也不应该）拿 `TeleportTargetRef` 反过去自己解析——`TeleportTargetRef` 保留只作为诊断/原始
   引用用途。消费方改动见 `core/gameplay/assembly/README.md` 同编号条目。

9. **CR150-02/04 根治（architecture/落地计划/audit-3224ca1-20260908，P2）：`chest`/`gather_node`
   的"能拿多少拿多少"（`GobjLootDeliveryPolicy.Partial`）未交付余量台账改按
   `GameObjectEntity.OriginKey`（稳定摆放位置键）记账，`gather_node` 补齐与 `chest` 同一套
   Reject/Partial 交付协议**：
   - **CR150-02（余量按瞬态实例 id 记账，实体重建后失联）**：`_pendingLoot`（原名
     `_pendingChestLoot`）此前按 `gobjInstanceId`（运行期实体 id）索引；同一刷新点/摆放位置的
     实体因 `World.ClearAll`/离图重进而重新生成时会分配一个全新的运行期 id（`GameObjectFactory`
     不复用旧 id），旧账目按旧 id 记的余量因此永久失联——外部审计复现：`oldId` 名下的余量仍在，
     新实体拿不到。根治：`GameObjectEntity` 新增可写属性 `OriginKey`（`Id?`），
     `GameObjectFactory.Spawn` 新增可选参数 `originKey`，未显式传入时按
     `"gobj.origin.<mapId>.<templateId>.<position>"` 自动合成一个"地图 + 位置 + 模板"稳定键
     （坐标用 `"G17"` 往返精度格式化，避免本地化/多次调用间的格式漂移）——刷新点数据登记的
     `spawn.table.position` 是固定值，同一刷新点历次重新生成天然拿到相同的合成键，不需要
     `SpawnHost` 额外传入刷新点 id 才能获得跨重建关联能力。`GameObjectHost` 的 `_pendingLoot`
     字典键改用 `PendingLootKey(gobj) => gobj.OriginKey ?? gobj.EntityId`（新构造的实体理论上
     `OriginKey` 恒非空，`?? gobj.EntityId` 只是防御性兜底）；`OpenChest`/`GatherNode` 交互时先按
     这个稳定键检查是否还欠着上一轮的余量——不管这个具体运行期实体自己的 `open_state`/`used_at`
     是什么（新实体天然是"未开过"/"未采集过"），只要账没结清就先补发、不重新 `Roll`，同时把这次
     补发标记为"这个新实体也用过一次"（`open_state=true`；`used_at` 仅在该新实体此前从未记录过
     冷却时才提交，避免推迟 `respawn_after_use` 应有的计时起点），防止紧接着的下一次交互因为新
     实体自己的状态从未被设置过而又整批重新发一遍同一份奖励。存档段 `GobjPendingLootPersistable`
     的方法改名为 `PendingLootSnapshot`/`RestorePendingLoot`，JSON 条目字段从 `gobjInstanceId`
     改名为 `originKey`（准确反映存的是稳定键，不是瞬态实体 id）。
     **AUD-01/AUD-04 修订（外部审核第九轮，architecture/落地计划/audit-85f1f4f-20260908）：上一句
     "本段未在任何已发布版本对外承诺过字段级兼容"的判断记录不成立——`GameObjectHost.
     PendingChestLootSnapshot`/`RestorePendingChestLoot` 与本段旧字段 `gobjInstanceId` 都是
     1.5.0（已发布版本）的公开形状，改名/改字段发生在同一次 1.6.0 变更里，不是"从未发布过"。**
     真实复现两个具体后果：(a) 存档兼容——1.5.0 非空旧格式存档经真实 `SaveSystem.Load` 直接索引
     `entryObj["originKey"]` 抛 `FormatException`，读档整体判 `PersistableThrew`；(b) API 兼容——
     仍调用旧方法名的 1.5 风格消费者在 1.6 上编译即 `CS1061`。根治两处：`GobjPendingLootPersistable.
     Load` 条目键既接受当前 `originKey`，也兼容旧 `gobjInstanceId`——旧运行期 id 本身不携带地图/
     模板/摆放位置信息，与稳定键之间不存在可逆映射，因此不做"猜测映射"，只安全丢弃该条（记诊断、
     不抛异常，不影响其它条目/其它段）；`GameObjectHost` 补回 `PendingChestLootSnapshot`/
     `RestorePendingChestLoot` 两个 `[Obsolete]` 转发方法（保留至少一个 MINOR 版本周期），行为
     与新名完全一致。验收：`GobjPendingLootPersistenceTests.
     SaveSystemLoad_Legacy15GobjInstanceIdField_SafelyDropped_NotThrown`、
     `ObsoletePendingChestLootAliases_StillCompileAndForwardToNewNames`。
   - **CR150-04（满包采集先提交冷却再忽略入包失败）**：`GatherNode` 修复前无条件先
     `SetState("used_at", ...)` 提交冷却，再把 `RollLootInto` 抽出的掉落无条件塞进背包、完全不
     处理 `IInventoryHost.AddItem` 交付结果——满包时奖励整份丢失，且冷却已提交，腾出空间后在同一
     冷却窗口内重试仍被挡住（外部审计复现：`used_at` 前后恒为同一时间戳、奖励数量恒为 0）。根治：
     新增 `GobjOptions.GatherNodeLootPolicy`（独立于 `ChestLootPolicy` 的口味配置项，默认同为
     `Partial`），`GatherNode` 改走与 `OpenChest` 完全同一套 Reject/Partial 协议
     （`GatherNodeReject`/`GatherNodePartial`）：只有真正交付了至少一部分才提交 `used_at`；
     `Reject` 下整批回滚且不提交，允许立即重试；`Partial` 下未交付部分记入 `_pendingLoot`（同
     CR150-02 的稳定键），一件都没能交付时同样不提交，交付了至少一部分才提交并可能同时留有余量。
   - 见 `core/carriers/gobj/tests/GobjPendingLootPersistenceTests.cs` 新增三条用例：
     `PartialChest_CrossMapReentry_NewEntityReattachesOldPending_DeliversRemainderExactlyOnce`
     （CR150-02，真实 `World.ClearAll` + `GameObjectFactory.Spawn` 重建）、
     `SaveSystemLoad_OldSaveMissingPendingLootSection_ClearsExistingResidual`（CR150-03 组合场景，
     真实 `SaveSystem.Save`/`Load`，见 `core/foundation/save_system/README.md` 同编号条目）、
     `GatherNode_FullInventory_RejectDoesNotCommitCooldown_PartialCommitsAndPersistsRemainder`
     （CR150-04）。

10. **ADR-0019 F1c：`type_data` 判别字段与被判别对象不同级，改用 `Fields` 并集登记，不用
    `Variants`**：`VariantSchema.Discriminator` 要求判别字段与被判别的子字段处在同一个
    JsonObject 内（`DataRegistry.ValidateVariantObject` 在 `type_data` 自身的 JsonObject 里找
    `kind`），但 `kind` 是 `gobj.template` 行内与 `type_data` 平级的字段，`type_data` 对象内部
    永远不会有这个键，若登记为 Variants 会恒报 `variant_discriminator` 缺失（同
    `core/gameplay/area_trigger` 的 `AreaTriggerSchemas.ParamsSchema`/`trigger_type` 判断记录，
    同一类结构边界）。改用 `Fields` 登记十种 `kind` 分别用到的字段并集、全部非必填，`kind` 决定
    哪些字段真正必填这条业务判断继续留在 `GobjTypeDataFieldGroupRule`（不退役）。`on_use`/
    `requirement` 的判别字段与专属字段同处一个对象内，Variants 适用，对应两条纯结构规则整条退役
    （见"校验规则"一节）。

11. **`requirement.world_flag.expected` 不登记类型**：`LockDef.RequireExprValue` 接受
    `JsonBool` 或 `JsonNumber`（`ExprValue` 的 `Bool｜Int` 联合，见 10 第 2.3 节
    `world_state_flags`），`FieldKind` 无法表达联合类型，登记任一具体类型都会把合法的另一种拒之
    门外。未登记子字段默认不报错（`DataRegistry.ReportUnknownSubfields` 只在
    `DataRegistryOptions.UnknownSubfieldSeverity == Warning` 时才提示，默认不开），不影响内容
    作者按 07 原文填写——同 `core/rules/skill` `SkillSchemas` 里 `set_world_flag.value` 的判断
    记录同一惯例。

## 契约缺口 / 未覆盖内容

- **`type_data.trigger_shape`（`trap`）不做结构校验**：05 第 3.5 节 `Shape` 的具体 JSON 形状由
  形状/目标相关模块定义（本模块未见其 JSON schema），本模块的 `GobjTypeDataFieldGroupRule`/
  `GameObjectTemplate.FromRecord` 只检查该字段"存在且是对象"，原样透传为 `JsonObject`，交给 L4
  区域触发装配时自行解释。
- ~~`type_data.sign.text_key` 不做文本键存在性校验~~（**ADR-0019 F1c 已关闭**）：`DataRegistry`
  的递归子结构校验（`Fields`/`Item`/`Variants`）与顶层字段共用同一份 `ValidateTextKeyField`/
  `ValidateReferenceField`/`ValidateExprField` 实现，`type_data.text_key` 登记为
  `FieldKind.TextKey` 后，嵌套字段的文本键存在性校验与顶层同等生效。
- **`type_data.trigger_shape`（`trap`）仍不做结构校验**（契约缺口未关闭）：05 第 3.5 节 `Shape`
  的具体 JSON 形状由形状/目标相关模块定义（本模块未见其 JSON schema），只检查该字段"存在且是
  对象"，原样透传为 `JsonObject`，交给 L4 区域触发装配时自行解释。
- **`type_data.loot_table_ref`/`quest_action_ref`/`teleport_target_ref` 仍不做跨表引用完整性
  校验**（契约缺口未关闭，依赖方向限制）：分别指向 `loot.table`/`quest.*`/运行期
  `TeleportResolver` 解析目标，均属 L4 或非静态数据表，本模块（L3）依赖方向不可 `Reference`，
  登记为 `Id` 只检查格式；`type_data.skill_id` **已关闭**——`skill.def` 属 L2，登记为
  `Reference(skill.def)`，本轮起有真实的跨表引用完整性校验。
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
