# L3 载体层 · item（物品模板、背包、装备）

职责：落地 [07_载体层_物品生物物件.md](../../../architecture/07_载体层_物品生物物件.md) 第 1 节
Item——物品模板字段（`item.template` 及五张配套表）、背包 `InventoryHost`（实现
`Core.Carriers.Common.IInventoryHost`）、装备栏 `EquipmentHost`（实现
`Core.Carriers.Common.IEquipmentHost`）、穿脱流程对属性/技能/光环/外形事件的联动（第 1.4 节）、
物品等级预算校验（第 1.2 节）、`create_item` 效果原语落地（`ItemEffectExtension`）、`player.inventory`/
`player.equipment` 两个存档段。

依赖：`Core.Rules.csproj`（及其传递引用的 `Core.Numbers`/`Core.Foundation`）、同程序集的
`Core.Carriers.Common`（`core/carriers/common`，`ItemInstance`/`ItemStack`/`ItemInstanceRef`/
`EquipResult`/`IInventoryHost`/`IEquipmentHost`/`CarriersEventKeys`/四个物品事件类型均来自那里，本
模块不重复定义）。不引用 `core/carriers/unit`/`creature`/`summon`/`gobj`（并行开发的兄弟模块）、
`Core.Gameplay`，不使用 `UnityEngine`、`System.Threading`、`DateTime`、`System.Random`、
`System.Reflection`。

## 目录

```
item/
  README.md
  schema/README.md         六张 item.* 表的字段说明与判断记录
  contracts/
    ItemSchemas.cs          六张表的 TableSchema 声明
    ItemOptions.cs           InventoryOptions/InventoryFullPolicy/ItemOptions
    SkillGranter.cs           装备联动"学习/遗忘技能"的具名委托（ISkillHost 契约缺口绕过）
    WeaponProfile.cs          EquipmentHost.GetWeaponProfile 的返回值类型
    IItemDiagnostics.cs      本模块诊断出口
  core/
    InventoryHost.cs           IInventoryHost 实现
    EquipmentHost.cs            IEquipmentHost 实现（穿脱联动本体）
    ItemEffectExtension.cs      IEffectExtension 实现：create_item
    ItemBudgetCurve.cs          budget_curve 线性插值 + 预算消耗量计算
    ItemValidationRules.cs      预算超标/武器槽/套装归属/堆叠数四条 IValidationRule
    ItemInstanceJson.cs         ItemInstance ↔ JSON（两个存档段共用）
    ItemPersistable.cs           InventoryPersistable + EquipmentPersistable
    InMemoryItemDiagnostics.cs
  tests/
    ...
```

## 谁实现、谁调用

沿用 `core/carriers/common/README.md`"谁实现、谁调用"表：`IInventoryHost`/`IEquipmentHost` 由本模块
实现，供装备/掉落拾取/商店交易等一切读写背包与装备栏的调用方使用；本模块自身额外对外暴露
`InventoryHost`/`EquipmentHost` 两个具体类（而非只暴露接口）——`EquipmentHost` 的构造需要直接持有
`InventoryHost` 实例（穿脱要移动物品所有权，接口层面的 `IInventoryHost` 不够用，见 `EquipmentHost`
构造参数），`ItemPersistable.cs` 两个存档段同理需要具体类而非接口。

## 数据表清单

见 `schema/README.md`：`item.template`、`item.slot_definition`、`item.quality_definition`、
`item.budget_curve`、`item.set`、`item.affix`（扩展位，只登记 schema 不实现）。

## 设计要点与判断记录

1. **"格子"的定义、换装顺序、Unequip 背包已满处理、套装光环来源标记、ISkillHost 契约缺口**——
   见 `InventoryHost`/`EquipmentHost` 类型顶部注释里的判断记录 1～4，不在本文件重复。
2. **`item.template.slot`/`quality`/`set_id` 的存在性校验复用 `data_registry` 内置
   `reference_integrity` 检查**（`FieldSchema.Kind = Reference`），不为此单独写
   `IValidationRule`——`IdList` 字段（`affixes`/`pieces`）不享受这一内置检查（`data_registry` 只对
   单值 `Reference` 字段做引用完整性检查，`IdList` 只做格式检查），`item.set.pieces` 反向包含
   `item.template.set_id` 的双向一致性因此仍需要 `ItemSetMembershipRule` 手写。
3. **"装备类"的判定拍板**：任务书原文提到"is_weapon 或 `ItemOptions.EquipmentSlots` 判断"两种
   候选，最终拍板简化为"`slot` 指向已登记的 `item.slot_definition` 即视为装备类"——不再需要
   `ItemOptions` 额外携带一份槽位集合，`item.slot_definition` 本身已经是"哪些槽位存在"的唯一权威
   来源，见 `ItemStackSizeRule`。**阶段 3 整理修正**：这条拍板把"分类桶"（如消耗品/材料，同样需要
   一个 `item.slot_definition` 记录才能满足 `item.template.slot` 的 `Reference` 校验）也误判成装备
   类，导致这类物品被迫 `stack_size == 1`。改为新增 `item.slot_definition.is_equipment`（Bool，缺省
   `true`）：只有 `is_equipment` 不为 false 的槽位才是"装备类"（`ItemStackSizeRule` 的唯一堆叠约束、
   `EquipmentHost.Equip` 的可装备判定均以此为准），`is_equipment: false` 的槽位是纯粹的分类桶，允许
   任意 `stack_size`，且 `EquipmentHost.Equip` 对这类槽位一律返回 `SlotMismatch`（不可装备）。
4. **`ItemOptions` 只保留 `BudgetCurveId`/`EnforceRequirements` 两项**：前者供
   `ItemBudgetValidationRule` 构造时读取（内容校验阶段使用），后者供 `EquipmentHost.Equip` 的等级
   需求判定使用（运行期使用）；两者虽然生命周期不同（一个是数据校验期，一个是运行期），仍放进
   同一个配置对象，方便宿主一次性配置该游戏的"物品口味"，同 `MovementOptions`/`InventoryOptions`
   等模块"一个配置类装下本模块全部策略配置项"的既有惯例。
5. **FND-10 收口（第四方深度审核）：`EquipmentPersistable.Load` 是完整替换，不是合并**——原实现
   只对快照里出现的槽位调用 Inject/Equip，从不清理调用前已经装备着、快照里没提到的旧物品，空档/
   缺槽档读档因此不会清空/收窄装备。现在 `Load` 开头先调用
   `EquipmentHost.ClearAllEquippedForLoad`（撤销全部联动、物品不放回背包）把单位重置到"无装备"，
   再按快照从零重新 Inject/Equip；空快照清空装备、缺槽快照对应槽位归空、重复 `Load` 幂等、跨槽
   恢复不再依赖 `Equip` 内部"换装放回背包"分支的副作用（那是为正常运行时换装设计的语义，与"读档
   =回到快照那一刻"不同）。见 `ItemPersistable.cs` 类型顶部判断记录、`ItemPersistableTests.cs`。
6. **RC-05 收口（第四方深度审核）：`SkillGranter` 按来源分离，不再是一个不分来源的 `HashSet`**——
   原实现装备 Learn/卸下 Forget 共用一个 `HashSet<Id>`，卸一件装备会连带遗忘"永久学习"的技能，或
   撤销另一件装备/来源共同授予的同一技能（共享光环/技能被误撤）。`SkillGranter` 现补第四个参数
   `sourceId`（装备实例/来源标识），`EquipmentHost`/`CarriersAssembly` 按 `(unitId, skillId,
   sourceId)` 分离追踪，只有最后一个引用该技能的来源被撤销时才真正 Forget；`SkillHost` 侧同理按
   来源计数。见 `EquipmentHost.cs`/`CarriersAssembly.cs`、`EquipmentHostTests.cs`。
7. **N09 收边补齐（外部审计 68c9bed）：装备授予光环的引用计数改按实例句柄，不再按 `aura_def`
   id**——RC-05 只解决了"技能"这一半（见上一条），光环那一半（`_grantedAuras`/原
   `_auraGrantRefCount`）仍按 `(unitId, auraDefId)` 聚合计数，隐含假设"同一 `aura_def` 被多件
   装备授予时它们拿到的 `AuraInstanceRef` 一定指向同一个共享实例"——这只在
   `SkillOptions.AllowMultiSourceTiming == false`（默认）时成立；`== true` 时 `AuraHost.ApplyAura`
   按来源各开一份独立实例，两件装备会拿到两个不同的 `AuraInstanceRef`，旧计数把它们误记成
   "同一份、还有其它引用"，卸下第一件装备时因为计数未归零而被跳过移除，全部装备卸载后仍残留
   一份临时 aura。现按实际落地的 `AuraInstanceRef.AuraInstanceId` 计数（改名
   `_auraHandleRefCount`），两种时序模式下都能精确判断"这份具体实例是否还有其它引用"，不需要
   区分是哪种模式。见 `EquipmentHost.cs`（`_auraHandleRefCount` 判断记录）、
   `EquipmentHostTests.cs`（`Unequip_OneOfTwoItemsGrantingSameAura_IndependentInstances_
   EachRemovedOnItsOwnUnequip`）。

8. **C08 收口（外部审计 7e63d66 第四轮，P2，成立）：装备来源记录随 `StackOverflowPolicy.Replace`
   换句柄原子更新**——N09（上一条）解决了"两件不同来源装备共享同一实例句柄"的正常计数问题，
   但没有覆盖`AllowMultiSourceTiming=false`、`maxStacks=1`、`StackOverflowPolicy.Replace` 下的
   换句柄场景：装备 A 授予 aura 得到句柄 h1，装备 B（不同槽位，授予同一个 `aura_def`）触发
   `Replace` 策略，`AuraHost` 内部删除 h1、创建全新句柄 h2——A 的授予记录（`_grantedAuras`）与
   `_auraHandleRefCount` 仍停留在已经失效的 h1，B 只知道 h2；任一件先卸下都会按自己记录的
   （可能已失效的）句柄错误判断"是否还有其它来源"，导致另一件仍装备着却没有了应有光环。现在
   `Core.Rules.Common.IAuraQuery` 新增 `InstanceReplaced` 事件（见 `core/rules/skill/README.md`
   同编号条目，C#8 默认接口成员、`AuraHost` 提供真正实现），`EquipmentHost` 新增可选构造参数
   `IAuraQuery? auraQuery`——注入后订阅该事件，把 `_grantedAuras` 里全部仍引用旧句柄的授予记录
   原子迁移到新句柄，并把 `_auraHandleRefCount` 上旧句柄名下的计数原样搬到新句柄名下（与新句柄
   自己已有的计数相加，不是覆盖）；未注入时（`null`，默认）行为与本次改动之前完全一致，只是
   重新暴露这个缺口，不抛异常、不改变既有测试断言。真实生产装配见
   `core/carriers/assembly/README.md` 同编号条目。判断记录（为什么不改成"原地复用同一个实例
   id"）见 `EquipmentHost.OnAuraInstanceReplaced` 类型注释——`AuraHost.ReapplyExisting` 的 Replace
   分支本就会发布一对 `AuraRemovedEvent("overwritten")`+`AuraAppliedEvent`，改成原地复用会
   连带改变这一对事件的既有发布契约（例如监听 `aura.removed` 的 proc），风险与收益不对称，选择
   新增一条独立的同步通知而不改动既有的换实例机制本身。测试假实现放在本模块新增的
   `core/carriers/item/tests/EquipmentReplaceHandleTests.cs`（不改动既有
   `EquipmentHostTests.cs`/`TestSupport.cs`），用真实 `CreatureFactory`+`AuraHost`+
   `EquipmentHost` 全链路组合验证：`ReplacePolicy_TwoItemsGrantSameAura_
   EitherUnequippedFirst_KeepsAuraUntilBothUnequipped`/
   `ReplacePolicy_TwoItemsGrantSameAura_UnequipAFirst_KeepsAuraUntilBothUnequipped`
   （两种卸载顺序都验证"任一件仍装备着，光环就还在，全卸才清空"）。

## 契约缺口清单（本次未新增/未修改 `core/rules/*`）

- `Core.Rules.Common.ISkillHost` 没有"学习/遗忘技能"方法（技能书能力目前只存在于
  `core/rules/skill` 内部实现，未提升到共享契约）：`EquipmentHost` 构造参数改用本模块新增的
  `SkillGranter` 具名委托绕过，由更上层组装代码把真实的 `SkillHost.LearnSkill`/`Forget` 适配成这个
  签名后注入，见 `contracts/SkillGranter.cs` 顶部注释。
- `Core.Rules.Common.IEffectSink` 没有"按来源整体撤销光环"的方法（只有
  `RemoveAura(unitId, AuraInstanceRef)` 按单个实例句柄撤销）：本模块自行维护
  `Dictionary<(unitId, instanceId), List<AuraInstanceRef>>`/`Dictionary<(unitId, setId),
  Dictionary<threshold, List<AuraInstanceRef>>>` 两份句柄表分别追踪"某件装备授予的光环"与"某个套装
  某个门槛施加的光环"，卸下/降档时按句柄逐一 `RemoveAura`，不算契约缺口（`IEffectSink` 本就没有
  "按来源批量撤销光环"这一语义，`StatModifier` 的按来源撤销是 L1 `stat_block` 独有能力，两者不
  对称是既有设计，不是本次任务遗漏）。
- 07 第 6 节"武器决定普通攻击动作……属于表现层职责"：`EquipmentHost.GetWeaponProfile` 只提供
  `weapon_profile` 的原始数值（伤害区间、攻速、学派），不提供"当前武器外形分类"——外形分类经
  `display_ref` 关联 `display.map` 查询，属于表现层（09，不在本模块范围）的职责，本模块不越权
  实现。

## 不负责什么

- 不实现 `core/carriers/creature`/`gobj`/`summon`（并行开发的兄弟模块）。
- 不解析/校验 `display.map`（外形映射）——`display_ref` 只作为不透明 `Id` 透传，供表现层订阅
  `item.equipped`/`item.unequipped` 事件后自行查询。
- 不实现附魔/宝石镶嵌/耐久/绑定/随机属性的具体规则——07 第 1.6 节扩展位只留字段位，见
  `schema/README.md`。
