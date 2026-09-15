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
  schema/README.md         九张 item.* 表的字段说明与判断记录（T-N2-1 起：六张既有表 + 三条新曲线表）
  contracts/
    ItemSchemas.cs          九张表的 TableSchema 声明
    ItemOptions.cs           InventoryOptions/InventoryFullPolicy/ItemOptions
    SkillGranter.cs           装备联动"学习/遗忘技能"的具名委托（ISkillHost 契约缺口绕过）
    WeaponProfile.cs          EquipmentHost.GetWeaponProfile 的返回值类型
    IItemDiagnostics.cs      本模块诊断出口
  core/
    InventoryHost.cs           IInventoryHost 实现
    EquipmentHost.cs            IEquipmentHost 实现（穿脱联动本体）
    ItemEffectExtension.cs      IEffectExtension 实现：create_item
    ItemBudgetCurve.cs          budget_curve 线性插值 + 预算消耗量计算
    ItemValidationRules.cs      预算超标/武器槽/套装归属/堆叠数/授权重复/品质倍率顺序/词缀份额之和/武器伤害偏离秒伤曲线八条 IValidationRule
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
`item.budget_curve`、`item.set`、`item.affix`（T-N2-2 起由留位转正为预算份额包，见下）；T-N2-1
（ADR-0032）新增 `item.armor_curve`/`item.weapon_dps_curve`/`item.req_level_curve` 三条曲线表（只
登记 schema 与 `curve_monotonic_finite` 校验，消费实现随后续任务落地）。

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

9. **R03 收口（外部审计 5e779c6，P2，成立）：套装门槛加成与装备 `grants.auras` 统一并入同一份
   `_auraHandleRefCount` 记账，不再各自为政**——N09/C08（上两条）解决的是"多件装备各自的
   `grants.auras`授予同一个 `aura_def`"这一半，`RecomputeSetBonuses`（套装门槛加成）此前完全不参与
   `_auraHandleRefCount`：`AllowMultiSourceTiming=false` 时装备 `grants.auras` 与套装门槛加成对同一个
   `aura_def` 施加，会在 `AuraHost` 内合并成同一份实例句柄，但只有 `ApplyGrants`/`RevertGrants`
   一侧登记引用计数，`RecomputeSetBonuses` 一侧直接无条件 `ApplyAura`/`RemoveAura`——卸下普通装备时
   `RevertGrants` 按自己那一份（未被套装分走）的计数归零，把共享的光环实例整个移除，即便套装仍
   满足件数门槛（外部审计复现："卸下装备后，仍满足条件的低门槛套装光环被删除"）。现在
   `RecomputeSetBonuses` 施加/降档套装门槛加成时同样调用 `RegisterAuraHandle`/`ReleaseAuraHandle`
   （新增的一对私有方法，封装原先散落在 `ApplyGrants`/`RevertGrants` 里的计数逻辑），且
   `OnAuraInstanceReplaced`（C08 的换句柄迁移回调）同步迁移 `_appliedSetBonuses` 里的句柄引用（此前
   只迁移 `_grantedAuras`）。另外同一件装备的 `grants.auras` 里重复登记两次同一个 `aura_def`（叠层
   溢出触发 `StackOverflowPolicy.Replace`，同一件装备内部换句柄）此前会被误判成"多了一个外部来源"
   重复计数、卸装备后光环反而卸不干净，改为在遍历 `grants.auras` 前就把待建列表登记进
   `_grantedAuras`（而不是遍历结束后一次性赋值），让 `OnAuraInstanceReplaced` 的迁移逻辑能在同一次
   循环内原地更新已有条目。见 `EquipmentHost.cs`（`RegisterAuraHandle`/`ReleaseAuraHandle`/
   `OnAuraInstanceReplaced`/`ApplyGrants` 判断记录）、`core/carriers/item/tests/
   EquipmentSetBonusSharedAuraTests.cs`（新增测试文件，真实 `CarriersAssembly` 全链路：
   `DuplicateAuraGrantsOnSameItem_Unequip_RemovesAuraCleanly`/
   `UnequipOrdinaryItem_WhileSetBonusStillMet_KeepsSharedAura`/
   `UnequipSetPieceFirst_ThenOrdinaryItem_RemovesAuraOnlyAfterAllSourcesGone`）。
10. **相邻缺口根治（第五轮外部审核 audit-5e779c6-20260907，WA 报告"需要说明的取舍"第 3 条）：
    `grants.auras` 同一物品内重复登记同一个 `aura_def` 新增数据校验提醒**——上一条（第 9 条）已经
    保证这种数据运行期不会再产生残留句柄/计数不一致，但重复引用本身此前完全没有任何数据层校验
    拦截，纯粹是"运行期凑巧不出错"，对内容作者而言仍是一处容易被忽略的冗余/误操作。新增
    `ItemGrantsAurasDuplicateRule`（Warning 级，check 名 `item_grants_auras_duplicate`）：同一
    `item.template.grants.auras` 内出现 2 次及以上同一个 `aura_def` 引用时提醒，不阻断合入（运行期
    已确认安全，拦截会让本就合法可加载的数据集突然过不了校验）。见
    `core/carriers/item/core/ItemValidationRules.cs`（`ItemGrantsAurasDuplicateRule`）、
    `core/carriers/assembly/CarriersSchemaCatalog.cs`（`RegisterItemSchemas` 注册）、
    `core/carriers/item/tests/ItemValidationRulesTests.cs`（新增 4 条用例）。

11. **CR140-02 根治（外部审计 audit-c86bfa9-20260908，P2）：新增公开方法
    `EquipmentHost.ReapplyGrants(unitId)`，供跨图 `World.ClearAll` 后重放装备/套装授予的
    Aura**——`ClearAll` 触发 `entity.destroyed`，`AuraHost`（`core/rules/skill`）响应该事件移除
    目标名下全部运行期 Aura 实例；但本模块的 `_equipped`/`_grantedAuras`/`_appliedSetBonuses` 全部
    按 `unitId`（不是 `entityId`）记账，与 `IWorldSim` 实体生命周期无关，`ClearAll` 完全不触碰——
    玩家实体重新登记回 `IWorldSim` 后，`_equipped` 仍然"记得"装备着哪些物品，装备本身的属性加成
    （`IStatHost.AddModifier`）与技能授予（`SkillGranter`）也完好（`IStatHost`/`core/rules/skill`
    的技能授予台账同样按 `unitId` 记账，不监听 `entity.destroyed`）——唯独 `_grantedAuras`/
    `_appliedSetBonuses` 里记录的 `AuraInstanceRef` 句柄全部失效，装备看起来"还穿着"、实际光环全部
    消失，直到重新装/卸一次才会被动刷新（外部审计探针 `equipment_aura_mapclear.log` 复现：
    `afterAura=False`、`afterEquipped=True`）。`ReapplyGrants` 按
    `instance→definition→grants` 重放每件已装备物品的 `grants.auras`（不重放 `stats`/`skills`——
    那两类从未真正丢失，重放会造成双重叠加），并对涉及到的每个套装重新走一遍
    `RecomputeSetBonuses`（先按 `IAuraQuery.HasAura` 核实 `_appliedSetBonuses` 里记录的档位是否
    真的还活着，清掉已经失效的记录——否则 `RecomputeSetBonuses` 只看这份记录判断 `isApplied`，
    会误以为不需要重新施加）。幂等：C08 收口时已经注入的 `IAuraQuery`（真实装配是 `AuraHost`）
    额外保留一份引用（新增字段 `_auraQuery`），逐条 `HasAura` 核实——已经生效的 `aura_def`（典型
    如本方法被意外连续调用两次）沿用已知句柄、不重新 `ApplyAura`（`AllowMultiSourceTiming=true`
    时重复施加会产生独立新叠层实例，不能靠"反正会合并"蒙混过去）；未注入 `IAuraQuery` 时（多数
    测试用的最小假实现）退化为"总是全部重新施加"，调用方需自行保证不会在 Aura 仍然存活时重复
    调用。调用方见 `core/gameplay/assembly/README.md` 同编号条目（`GameplayAssembly.EnterMap`）。

12. **CR150-01 根治（architecture/落地计划/audit-3224ca1-20260908，P2）：`ReapplyGrants` 判断
    "某个 `aura_def` 是否需要重新 `ApplyAura`"改用调用开始前的惰性快照，不再在逐件重放的循环
    过程中反复实时查询 `IAuraQuery.HasAura`**——两件装备共享同一个 `aura_def`（默认
    `AllowMultiSourceTiming=false`）时，旧实现会在重放第一件后把该 `aura_def` 的 `HasAura` 从
    false 变为 true，第二件因此误判"从来没有失效过"，转而复用自己名下那份早已随 `ClearAll` 失效
    的旧句柄——这份旧句柄既没有被重新计数，也不是 `AuraHost` 真正认得的活句柄，导致共享光环的
    引用计数只算上了第一件；卸下第一件时第二件仍装备着，光环却已经被误删（外部审计复现：
    `afterFirstUnequip` 实际 False，预期仍应为 True）。根治后 `ReapplyGrants` 内维护一份"按
    `aura_def` 惰性缓存、只在第一次被问到时真正查询一次 `IAuraQuery.HasAura`、此后同一次调用内
    全部复用同一个结果"的快照，逐件装备与逐个套装门槛判定（`ReapplySetBonuses`）都改用这份快照
    而不是实时查询；两件装备各自是否需要重新 `ApplyAura` 因此都反映"本次 `ReapplyGrants` 调用
    开始前"的真实状态，与彼此的重放顺序无关——是否合并成同一份实例、还是各自独立（
    `AllowMultiSourceTiming=true`）完全交给 `IEffectSink.ApplyAura`/`AuraHost` 自身的既有合并
    策略决定，本方法不在这一层揣测/复用其它来源的句柄。幂等场景（未发生 `ClearAll`，或
    `ReapplyGrants` 被意外连续调用）下，"调用前快照"与原实时查询的结果相同，不受影响。见
    `core/gameplay/assembly/tests/CR150_01_EquipmentSharedAuraCrossMapTests.cs`。

13. **AUD-02 根治（外部审核第九轮，P2，architecture/落地计划/audit-85f1f4f-20260908）：
    `InventoryPersistable.Load` 对本段整体缺失（`JsonNull`）的处理，从 no-op（保留读档前的
    运行期库存）改为清空背包**——修复前真实探针复现：先放入 1 件物品，再加载一份没有
    `player.inventory` 段的存档，返回 `Loaded` 但库存仍是 1 件，违反 10 第 3 节"缺失段语义"合同
    （缺段应清空到默认态）。见 `ItemPersistableTests.InventoryPersistable_Load_NullData_
    ClearsPreExistingItems`。
    > **勘误（CORE-170-03，见下方判断记录 15）：** 上一句话原来还写着"`EquipmentPersistable.Load`
    > 不受影响——它已经在检查 `JsonNull` 之前无条件调用 `ClearAllEquippedForLoad`，本就正确覆盖了
    > 这一路径"——这句话只对 `data is JsonNull` 这一个分支成立，对"`data` 既不是 `JsonNull` 也不是
    > 合法 `JsonObject`"这一分支是错的：无条件清空在校验形状之前执行，坏 shape 会在清空之后才
    > 抛异常，见判断记录 15。
14. **CORE-170-01 根治（第十轮外部审计，P2，architecture/落地计划/audit-8160178-20260908）：
    `_auraHandleRefCount` 从 `EquipmentHost` 私有字段上移为 `Core.Rules.Common.AuraHandleLedger`
    （定义在 `core/rules/common/contracts`），由 `RulesAssembly` 持有单一实例并经新增构造参数
    `auraHandleLedger` 注入本类**——上面第 7/8/9 条判断记录描述的引用计数机制（按实例句柄、随
    `InstanceReplaced` 迁移、装备与套装门槛加成共用）此前只覆盖装备/套装两类来源，种族/职业被动
    光环完全不参与，导致装备与种族共享同一 `aura_def` 时卸装会把种族仍依赖的共享实例一并删除
    （详见 `core/rules/assembly/README.md` 同编号判断记录）。本类原有的
    `RegisterAuraHandle`/`ReleaseAuraHandle` 方法名与调用点全部保留，只是内部改为转发到
    `_auraHandleLedger`；未注入时（`null`，多数测试用的最小假实现）自建一份私有账本，退化为
    此前"只在装备/套装两处之间共享计数"的行为，不影响不涉及种族共享 `aura_def` 的既有测试断言。
    `StackOverflowPolicy.Replace` 换句柄的计数迁移也从本类 `OnAuraInstanceReplaced` 里移出，由
    `AuraHandleLedger` 自己订阅同一个 `InstanceReplaced` 独立完成——本类该方法此后只保留
    `_grantedAuras`/`_appliedSetBonuses` 这两份"我自己记着哪个句柄"的簿记迁移。见
    `Core.Rules.Common.AuraHandleLedger` 类型判断记录、`Tests.Gameplay.Assembly.
    CORE_170_01_RaceEquipmentSharedAuraTests`。
15. **CORE-170-03 根治（第十轮外部审计，P2，architecture/落地计划/audit-8160178-20260908）：
    `EquipmentPersistable.Load` 改为"先解析校验成临时恢复计划、再一次性提交"，不再无条件先清空**
    ——上面第 5 条"FND-10 收口"确立的`ClearAllEquippedForLoad` 前置清空，此前对**任何** `data`（含
    既不是 `JsonNull` 也不是合法 `JsonObject` 的坏 shape）都无条件执行，随后才校验形状：坏 shape
    （`data` 本身不是对象、槽位键不是合法 `Id`、或某个槽位的物品实例存档数据格式非法）因此会在
    清空之后才抛 `FormatException`，此时该玩家读档前的全部装备（含属性修正/技能授予/光环施加/
    套装加成）已经丢失，且已经向真实事件总线发出 `StatChanged`/`ItemUnequipped`；`SaveSystem`
    只把"已成功加载"的段加入回滚列表，本段自身从未成功加载过，不会被回滚。真实探针复现：坏
    shape 抛错前装备存在，抛错后消失，排空事件队列后仍是消失状态。根治后 `Load` 先完整遍历
    `data` 校验全部槽位键与物品实例形状（`ItemInstanceJson.FromJson` 对坏 shape 抛异常），不触碰
    `EquipmentHost`/`InventoryHost` 任何运行期状态；只有整份数据校验通过，才调用
    `ClearAllEquippedForLoad` 并按解析结果一次性恢复——`JsonNull`（本段整体缺失）分支保持不变
    （不需要先解析，直接清空即是完整语义）。`SaveSystem` 侧另加一层兜底（回滚时把抛异常的段自身
    也纳入，见 `core/foundation/save_system/README.md` 同编号判断记录）与事件抑制（回滚重放的
    `ItemEquipped`/`ItemUnequipped` 不应该被 `AchievementHost` 一类计数消费者当作真实操作再计一
    次数，见 `core/foundation/event_bus/README.md`"SuppressDispatch"一节）——三层合起来才是完整
    的根治，本类自身的"先校验后提交"是第一层，不能只靠 `SaveSystem`/事件抑制兜底。见
    `Tests.Carriers.Item.CORE_170_03_EquipmentPersistableLoadFailureTests`、`Tests.Gameplay.
    Assembly.CORE_170_03_SaveRollbackEventSuppressionTests`。

16. **T-N2-1（分阶段落地计划、ADR-0032）：槽位/品质/模板新字段、`stat_roll_ref` 改兼容位、三条新
    曲线表登记，只登记 schema、不接消费实现**——本任务范围严格限定在"字段与表登记 + 品质倍率顺序
    阻断校验"，不改动 `ItemBudgetCurve.SumConsumed`/`EquipmentHost.GetWeaponBaseDamage`/
    `TryGetRequiredLevel` 等既有运行时公式（消耗侧权重/指数 k、护甲/武器秒伤求值、需求等级反推、
    授予预算校验分别留给 T-N2-4/T-N2-6/T-N2-9）。新增 `ItemQualityMultiplierOrderRule`
    （check 名 `item_quality_multiplier_order`）：按 `sort_weight` 升序分组比较
    `budget_multiplier`/`price_multiplier`，组内并列不比较、跨组不递减——04 第 5 节"品质倍率顺序"
    一行未给出具体检查名，本名称按任务书"04 §5 或 `item_quality_*` 前缀"取值——**设计层裁定
    （2026-09-15）：采纳**，见 04 第 5 节同一次改动的勘误记录。
    三条新曲线表（`item.armor_curve`/`item.weapon_dps_curve`/`item.req_level_curve`）复用 T-N0-1
    的 `CurveSchema.BreakpointsField`（横轴 `CurveAxis.ItemLevel`），新表无需迁移链，`entries` 自动
    受 T-N0-3 的 `curve_monotonic_finite` 通用规则约束，不为本模块单开曲线专属校验。
    `item.quality_definition.grant_budget_share`/`price_multiplier` 是否必填、缺省值取多少，
    ADR-0032 决策 2 原文未明确（只对 `affix_count` 标"可选"）——按既有 `budget_multiplier` 同类字段
    口径类推（`required: false`，倍率类缺省 1、占比类缺省 0）——**设计层裁定（2026-09-15）：
    采纳**，见
    `schema/README.md`"品质定义"小节判断记录与 `ItemSchemas.QualityDefinition` 类型顶部注释。见
    `core/carriers/item/tests/ItemSchemaCoverageTests.cs`（17 条新增用例：三条新曲线表命中/缺必填/
    范围越界、槽位/品质/模板新字段命中/范围越界）、`ItemValidationRulesTests.cs`（品质倍率顺序正例/
    负例/并列不比较 3 条）。

17. **T-N2-2（分阶段落地计划、ADR-0032 决策 7）：`item.affix` 由留位转正为预算份额包，`effects`
    留位废弃（保留一个版本周期）、`item.template.affixes` 语义改写**——四个新字段
    `budget_share`/`stat_mix`/`quality_pool`/`weight` 登记为必填（三项前提"留位期未被任何运行时代码
    解析、旧样例只有占位字段、`games/_template` 无该表数据"俱在，`currentSchemaVersion` 保持 1，
    不新增迁移链，见 `ItemSchemas.Affix` 类型顶部判断记录）；`grants` 可选，直接复用
    `item.template.grants` 同一个 `GrantsSchema` 静态字段实例（同结构、无需另开一份）。新增
    `ItemAffixStatMixRatioSumRule`（check 名 `item_affix_stat_mix_ratio_sum`）：单条
    `stat_mix[].ratio` 之和超过 `1 + 1e-9`（浮点容差）报 Error——校验对象是"单条词缀内部
    `stat_mix` 数组"而非"同一品质池跨词缀"，依据 07 第 1.6 节修订段与 04 第 5 节"词缀份额之和"行
    原文均把"之和不超过一"紧跟在 `stat_mix` 单字段后描述；04 第 5 节该行同样未给出具体检查名，
    按任务书"04 §5 或 `item_affix_*` 前缀"取值——**设计层裁定（2026-09-15）：采纳**（同 T-N2-1 的
    `item_quality_multiplier_order` 同一处理口径）。"模板加词缀最大份额超预算"与"预算利用率过低"
    两条校验本任务（T-N2-2）不实现，只登记字段；**勘误（见下方 T-N2-3 判断记录 18）**：后者只需
    消耗公式（消耗/上限比值），T-N2-3 已落地，不必等 T-N2-4 预算反解；前者仍需预算反解算出"可抽
    词缀最大份额对应的等效消耗"，确实要等 T-N2-4。
    `item.template.affixes` 语义由"词缀引用（扩展位）"改写为"该模板掉落时可抽取的词缀候选白名单"
    （落地改动点清单 E2 用语"可抽词缀池约束"，二选一"该模板可挂的词缀/或固定词缀"（任务书用语）
    与"可抽词缀池约束"（落地改动点清单用语）本任务按后者实现——**设计层裁定（2026-09-15）：
    采纳**（`item.template.affixes` 即"掉落时可抽词缀候选白名单"；"固定词缀"语义在现有契约里没有
    独立字段位，如需要须将来另开字段，不复用本字段）；缺省 `[]` 视为不收窄，消费实现（掉落三次掷骰第三骰的交集
    运算）随 T-N2-8 落地，本任务只改字段描述、不改运行时行为。`is_weapon` 附带核对：
    `item.slot_definition.is_weapon` 在 T-N2-1 之前（阶段 3 整理）已登记，缺省 `false`——ADR-0032
    决策 1 要求的字段已存在，本任务无需补登记，见 `ItemSchemas.SlotDefinition` 既有字段。见
    `core/carriers/item/tests/ItemSchemaCoverageTests.cs`（新增 7 条：命中、缺必填、`stat_mix`
    引用不存在的属性、`ratio` 越界、`quality_pool` 引用不存在的品质、`weight` 越界、`grants`
    命中）、`ItemValidationRulesTests.cs`（份额之和超一/未超各 1 条）。

18. **T-N2-3（分阶段落地计划、ADR-0032 决策 3/10）：预算消耗公式改加权 `(Σ(值×权重)^k)^(1/k)`，
    百分比属性经换算曲线折点，上限乘槽位系数；新增预算利用率过低 Warning**——新增
    `ItemBudgetCurve.ComputeConsumed`/`BuildStatBudgetInfo`（连同新增公开类型 `StatBudgetInfo`）
    取代 `ItemBudgetValidationRule` 内对旧式 `ItemBudgetCurve.SumConsumed`（`Σ|value|`，`pct`/`mult`
    ×100 折算）的调用——旧方法本身原样保留（硬性规则 5：ABI 只允许新增），只是不再是校验路径，供
    仍直接引用它的外部代码继续编译。判断记录见下（详细推导另见各自类型/方法 XML 注释，本节只列
    要点；以下各条此前标注为实现期判断、待裁定处均已由设计层裁定（2026-09-15）：采纳）：
    - **权重来源与缺省值**：`stat.weight.weight`（ADR-0030 决策 7 基础权重，不展开
      `class_overrides`——校验期核算"这件物品模板"本身，没有"当前职业"上下文，见
      `ItemBudgetCurve.BuildStatBudgetInfo` 判断记录）；**没有对应 `stat.weight` 记录时缺省权重取
      1（`ItemBudgetCurve.DefaultWeight`），不是该表类型注释里"显式登记 `weight:0`"的那个 0**——
      两者是不同场景，契约未明文规定"没有记录"这一情形的缺省值，按"避免过渡态下消耗公式整体退化
      为恒 0"选 1（设计层裁定：采纳）。
    - **指数 k 的登记位置**：契约三处（ADR-0032 决策 3、07 第 1.2 节修订段、数值总纲第 4.4 节）均
      只给"k 默认 1.5，数据配置"，未指明字段位置。本任务登记为 `item.budget_curve` 记录自身的可选
      字段 `exponent`（缺省 1.5）——设计层裁定：采纳，见 `ItemSchemas.BudgetCurve` 判断记录。
    - **"百分比属性折回点数"的操作化定义**：契约只给一句话，未展开到 op/category 组合。本任务依据
      `EquipmentHost.ApplyGrants`（`stats[]` 的权威消费者）与 `StatHost.ComputeFinal`（运行时聚合
      管线：`flat` 值贡献进换算前点数和，`pct`/`mult` 直接乘进换算后的百分比/乘区）反推：
      `category==percent` 且 `op=flat` 的值本就是点数，不折算；`op=pct`/`mult` 的值是作者按"最终
      百分比效果"填写的，经该属性引用曲线的反函数（`RatingConversionEvaluator.ToPoints`）折回点数
      （换算所需"单位等级"取物品自身 `item_level`，非 `requirements.level`——后者可能未填，且随
      T-N2-9 才由曲线反推，本任务不依赖它）；非 `percent` 属性沿用既有 `pct`/`mult` ×100 折算，
      未改动（设计层裁定：采纳，即"折算方向按运行时管线，percent 类别 op=pct/mult 视为最终百分比
      经 ToPoints 折点"），见 `ItemBudgetCurve.ComputeConsumed` 类型注释。
    - **反函数抽取为公开共享静态工具**：`StatHost.ConvertRating` 内部原本私有手写的"点数→百分比"
      断点/饱和两分支求值式子，提升为 `Core.Numbers.StatBlock.RatingConversionEvaluator`（正向
      `ToPercent` + 新增反向 `ToPoints`）；`StatHost` 自身同一私有嵌套枚举
      `RatingConversionShape` 也提升为该命名空间下的公开类型，两处删除重复定义，改为共用——满足
      硬性规则"禁止复制插值实现"，`core/numbers/stat_block` 既有测试（`RatingConversionMigrationTests`
      等 105 条）验证重构后结果逐位不变。饱和形态反函数在 `percent >= 1`（曲线永远达不到的百分比）
      时返回 `double.PositiveInfinity`，不抛异常，交给调用方（预算消耗求和）自然判定为超预算；
      `ComputeConsumed` 额外对 `weight == 0` 短路避免 `Infinity × 0 = NaN` 这一浮点陷阱。
    - **槽位系数接入预算上限**：`ItemBudgetValidationRule.Validate` 新增读取 `item.slot_definition.
      budget_coefficient`（缺省 1），预算上限 = 曲线值 × 品质倍率 × 槽位系数；T-N2-1 登记时留的
      "消费实现随 T-N2-4 落地"备注在 `item.slot_definition.budget_coefficient` 这一项上已提前由本
      任务接入（护甲/武器秒伤两条曲线的消费仍留给 T-N2-4/E7/E8，未受影响）。
    - **`ItemBudgetValidationRule` "改签名拿 registry 视图"的落地方式（新增构造重载）**：
      `Validate(IDataRegistryView view)` 本就以 view 为参数，新公式需要的四张表（`stat.weight`/
      `stat.definition`/`stat.rating_conversion`/`item.slot_definition`）均可经该既有参数查询，不
      需要在构造期（`RegisterAll` 阶段，此时数据尚未 `LoadAll`，注入了也是空视图）额外注入一份
      registry 视图。本任务把"改签名"具体落实为新增构造重载 `ItemBudgetValidationRule(Id, double)`
      ——落地 ADR-0032 决策 10"阈值默认七成，可配置"，旧的单参数构造函数保留并转发默认阈值；
      `CarriersSchemaCatalog.RegisterAll` 同步新增一个三参数重载（旧的两参数签名原样保留，硬性
      规则 5）——"规则用构造重载配阈值"这一落地方式，设计层裁定（2026-09-15）：采纳，见
      `ItemBudgetValidationRule` 类型判断记录。
    - **检查名**：新增 Warning `item_budget_utilization_low`（04 第 5 节"装备预算利用率过低"一行
      未给出具体检查名，同 `item_quality_multiplier_order`/`item_affix_stat_mix_ratio_sum` 一贯
      处理口径——设计层裁定（2026-09-15）：采纳）；`ItemBudgetValidationRule.NonEscalatable` 改为 `true`
      （`IValidationRule.NonEscalatable` 契约文档原文即以"装备预算利用率过低"为例——只影响本规则
      产出的 Warning 在 `WarningsBlock` 严格级别下是否计入阻断，既有 `item_budget_exceeded` 的
      Error 不受影响，见 `ValidationReport` 聚合逻辑）。
    - **示例数据调整**：`data/_sample/item/item.template.json` 的 `item.sample_blade`/
      `item.sample_model_sword` 两条 `stats[].value` 由 2 改为 15（原值在新公式下利用率仅 10%，
      触发新警告；样例非框架默认值，按任务书"不得调阈值迁就，调整示例数据"处理，调整后利用率
      75% ≥ 70% 阈值）；`item.sample_model_sword` 原先的 `budget_note`（"预算利用率偏低为演示
      数据，非超模说明"）随之删除——`budget_note` 语义（ADR-0032 决策 6"橙装独特技能凭
      `budget_note` 免检"）与预算利用率警告无关，不消费该字段，调高数值后这条说明也不再成立。
      `data/_sample/item/item.budget_curve.json` 补一条 `exponent: 1.5`（与缺省值相同，仅作为
      新字段的样例展示）。`games/_template` 目前没有 `item.template.json`（空壳表阶段，T-N2-10
      才补），本任务不涉及。见 `toolchain/validator --data-root data/_sample` 实测
      `ItemBudgetValidationRule: error (non-escalatable), hits 0`。
    - **测试**：`core/carriers/item/tests/ItemBudgetCurveComputeConsumedTests.cs`（新增 8 条：
      k=1 三组、k=1.5 三组——含断点表/饱和曲线换算折点各一组、缺省权重回退一组、NaN 防御回归一条）、
      `ItemValidationRulesTests.cs`（新增 4 条：槽位系数缩小上限致超预算、利用率警告正例、利用率
      不触发负例、自定义阈值构造重载生效）；`core/numbers/stat_block` 既有 105 条测试验证
      `RatingConversionEvaluator` 重构无回归，未新增/删除该模块测试用例数。

19. **T-N2-4（分阶段落地计划、ADR-0032 决策 3/9；07 第 1.2 节修订段）：`IBudgetSolver.Solve` 预算
    反解与 `EquipmentScoreAnalyzer` 装备评分契约面**——新增 `contracts/IBudgetSolver.cs`
    （接口 + `BudgetSolverResult`）、`contracts/EquipmentScoreResult.cs`、
    `core/BudgetSolver.cs`（实现类）、`core/EquipmentScoreAnalyzer.cs`（静态类）。
    - **`IBudgetSolver.Solve` 公开签名**：`BudgetSolverResult Solve(int itemLevel, Id qualityId, Id
      slotId, IReadOnlyList<(Id Stat, double Ratio)> statMix, Id budgetCurveId, double
      shareOfBudget, IDataRegistryView view)`，外加一个 `shareOfBudget` 缺省 1.0 的 6 参 C# 8
      默认接口方法重载（`BudgetSolver` 类按 `Tests.Presentation.Assembly.
      InterfaceDefaultMemberForwardingTests` 门禁要求显式转发，不悄悄落回接口默认实现）。07 第 1.2
      节原文签名 `BudgetSolver.solve(itemLevel, qualityId, slotId, statMix): Map<StatKey, Number>`
      只给 4 个概念参数——`budgetCurveId`（预算曲线 id 在本模块一贯是调用方配置项，同
      `ItemOptions.BudgetCurveId`/`ItemBudgetValidationRule` 构造参数惯例，不预设唯一默认曲线）与
      `shareOfBudget`（ADR-0032 决策 7 词缀落值需要"该件预算 × `budget_share`"这一缩小目标，若不
      独立建模只能让 `statMix` 同时承载"内部分配"与"总量占比"两种语义）是落地为 C# 时新增的两个
      参数——`budgetCurveId`/`shareOfBudget` 为显式参数，设计层裁定（2026-09-15）：采纳，见
      `IBudgetSolver` 类型判断记录。
    - **反解数学**：把 `statMix` 每项 `(stat_i, ratio_i)` 理解为"该属性加权贡献 `term_i = value_i ×
      weight_i` 占总加权贡献的比例"；令 `S` 为待定缩放常数，`term_i = ratio_i × S`，代入消耗公式
      `C = (Σ term_i^k)^(1/k) = S × (Σ ratio_i^k)^(1/k)`；令 `C` 等于目标预算
      `B = 曲线(itemLevel) × 品质预算倍率 × 槽位系数 × shareOfBudget`，解出
      `S = B / (Σ ratio_i^k)^(1/k)`，再对每一项 `value_i = term_i / weight_i`。`k=1` 时公式自然
      退化为线性（`Σ ratio_i = 1` 时 `S = B`），不需要单独分支。反解出的属性值统一按 `op=flat` 语义
      （即"点数"），`category=percent` 的属性同样是点数——调用方需要 `op=pct`/`mult` 的填写值时
      自行调用 `RatingConversionEvaluator.ToPercent`（`ToPoints` 的反函数）。
    - **`statMix` 比例语义判断记录（比例之和须严格为 1，不是"不超过一"）——设计层裁定
      （2026-09-15）：采纳（比例取"加权贡献 v·w 占比"、反解入参比例之和须为 1；词缀 `stat_mix`
      之和不足 1 时由调用方把 `Σratio` 折进 `shareOfBudget` 并归一化，T-N2-5 已如此实现，见下方
      对应条目）**：
      07 第 1.6 节 `item.affix.stat_mix` 原文"属性组合与内部分配比例，之和不超过一"、07 第 1.2 节
      `solve` 签名本身均未展开"分配比例"是分配"属性原始值"份额还是"加权贡献"份额。本接口按"各
      属性的加权贡献占比"实现（线性、作者填写时最直观），且比例之和必须恰为 1（±1e-9，否则抛
      `ArgumentException`）——`item.affix.stat_mix` 允许"之和小于一"是对存量数据的校验上界（宽松
      的合法数据形态，见 `ItemAffixStatMixRatioSumRule`），但 `Solve` 作为通用反解工具，"目标预算"
      与"份额"已经由 `shareOfBudget` 独立表达，`statMix` 只负责"这份已确定的目标预算如何在各属性
      间分配"，比例之和不为 1 时无法在"反解出的属性值"与"目标预算"之间建立直观对应关系。未来
      T-N2-8 调用方若需要表达"词缀 `stat_mix` 之和小于一"的存量数据，由调用方自行按
      `ratio_i / Σratio_i` 归一化后再传入（归一化不改变各项相对比例）。
    - **其余输入校验**：单项 `ratio` 须 > 0（否则抛 `ArgumentException`，0/负数在加权公式下无意义
      或产生未定义行为）；`statMix` 引用的属性若在 `stat.weight` 显式登记权重为 0，无法反解出有限
      值，抛 `ArgumentException`（点出属性 id）；`shareOfBudget` 须在 `(0,1]`（±1e-9 容差）；
      品质/槽位/预算曲线 id 在对应表中找不到，抛 `ArgumentException`（消息点出 id）——均为任务书
      硬性要求的落地。
    - **`EquipmentScoreAnalyzer.Score` 公开签名**：`static EquipmentScoreResult Score(Id
      templateId, Id? classId, IDataRegistryView view, IReadOnlyList<(Id Stat, double Value)>?
      additionalStats = null, double exponent = ItemBudgetCurve.DefaultExponent)`；`Compare
      (EquipmentScoreResult, EquipmentScoreResult): int` 提供"比较箭头"最小接口。评分定义
      = 用职业权重（`stat.weight.class_overrides` 命中 `classId` 时覆盖，未命中/`classId` 为
      `null` 时用顶层基础权重）代入同一条 `ItemBudgetCurve.ComputeConsumed` 公式算出的值——与预算
      消耗是同一公式，唯一区别是权重来源。`ItemBudgetCurve` 新增重载
      `BuildStatBudgetInfo(IDataRegistryView, Id classId)`（既有 `BuildStatBudgetInfo(view)` 不变，
      内部提取公共 `BuildCategoriesAndConversions`/`ComposeStatBudgetInfo` 两个私有辅助方法，避免
      复制换算曲线解析逻辑）供本类型消费。
    - **`exponent` 取显式参数、不取某条 `item.budget_curve` 记录（判断记录）**：评分本身不核算
      "消耗是否超过某条曲线上限"，只是复用同一条加权公式；07/ADR-0032 均未要求评分与某一条具体
      预算曲线绑定，改为显式参数（缺省 `ItemBudgetCurve.DefaultExponent`=1.5）避免"评分"这一纯
      展示概念意外依赖"预算校验用的是哪条曲线"这一内容配置细节。
    - **本任务只对模板 `stats` 评分，预留 `additionalStats`（"附加属性列表"）入参**：物品实例带
      词缀的评分依赖 `BudgetSolver` 反解出的数值，词缀反解值本身要等 T-N2-7/T-N2-8 落地才存在——
      `additionalStats` 语义同 `BudgetSolverResult.Values` 的 `Id → 点数` 形状，全部按 `op=flat`
      语义并入，供后续任务把词缀反解值接进来，不需要再改本方法签名。
    - **无状态核对**：`BudgetSolver`/`EquipmentScoreAnalyzer` 均不持有任何字段，`Solve`/`Score`
      每次调用都是纯函数（给定相同输入含相同 `view` 快照必然产出相同结果），不缓存、不记录调用
      历史——`EquipmentScoreAnalyzer` 额外照 `Core.Gameplay.Loot.LootTableAnalyzer` 的契约面形态
      （静态类、纯函数，任务书原文要求）；`Core.Carriers` 程序集按分层不引用 L4 `Core.Gameplay`，
      对该类型的引用一律用 `<c>` 而非 `<see cref>`（同 `RegistryCreatureTemplateQuery` 类型判断
      记录，避免 `TreatWarningsAsErrors` 下的 CS1574）。
    - **测试基建**：`core/carriers/item/tests/TestSupport.cs` 的 `BuildRegistry` 改为
      `FailOnUnknownTable=false`（同 `core/numbers/stat_block/tests/StatWeightSchemaTests
      .BuildRegistry` 既有手法）——`stat.weight.class_overrides[].class` 的 `reference_integrity`
      检查需要能在已加载数据里找到一条同 id 的 `arch.class` 记录，测试用例只需塞最小占位行（仅
      `id` 字段，走 `TableSchema.Unschematized`），不需要满足 `arch.class` 真实 schema 的
      `name_key`/`primary_stat`/`base_stats`/`power_types` 等必填字段；未提供该表数据的既有测试
      不受影响（538 条 `Tests.Carriers` 既有用例全绿）。
    - **测试**：`core/carriers/item/tests/BudgetSolverTests.cs`（新增 15 条：反解可逆 5 组——k=1
      单属性、k=1.5 双属性不同比例、k=1.5 percent 属性经断点表换算曲线折点并做
      `ToPercent`/`ToPoints` 往返一致性验证、`shareOfBudget<1` 词缀场景、槽位系数场景；输入校验
      9 条——比例之和不为一/单项比例非正/品质不存在/槽位不存在/曲线不存在/权重为零/
      `shareOfBudget` 越界/`statMix` 为空/默认接口方法重载一致性）、
      `core/carriers/item/tests/EquipmentScoreAnalyzerTests.cs`（新增 8 条：基础手算、职业覆盖
      权重生效、评分对预算单调 2 组——同槽位同职业预算上限更高评分更高、职业权重覆盖改变评分
      排序、`Compare` 符号一致性、`additionalStats` 预留入参、未知模板抛异常）。

20. **T-N2-5（分阶段落地计划、ADR-0032 决策 4/5/7/8；07 第 1.2/1.4 节修订段）：护甲值曲线 ×
    槽位系数写入、词缀反解值同 sourceId 随穿戴写入、`requirements.level` 缺省按曲线反推**——
    `EquipmentHost.cs` 内改动，无新文件。
    - **护甲值写入（ADR-0032 决策 4）**：新增私有方法 `ApplyArmorValue`，在 `ApplyGrants` 里
      模板 `stats` 之后、`grants` 之前调用；护甲值 = `item.armor_curve`（id 取
      `ItemOptions.ArmorCurveId`，缺省 `item.armor.default`）在 `item_level` 处求值 ×
      `item.slot_definition.budget_coefficient`（缺省 1），经 `IStatHost.AddModifier` 以
      `op=flat`、sourceId=该件装备实例 id 写入 `ItemOptions.ArmorStatId`（缺省 `stat.armor`）指向
      的属性；曲线在已加载数据里找不到对应记录时按"不写护甲"处理，不抛异常。
    - **护甲位判定——已被 T-N2-6 设计层裁定取代（见下方判断记录 21）**：
      `item.slot_definition`（见 `ItemSchemas.SlotDefinition`）当前没有独立的 `is_armor`/
      `armor_slot` 一类字段区分"护甲位"与其它非武器装备位（戒指/项链/饰品一类传统意义上不该有
      护甲值的槽位）；`item.template` 也没有 `kind`/`equip_slot` 一类模板分类字段可供二次判断。07
      第 1.2 节原文只给"仅护甲位"一句，未展开判定规则。本任务按任务书给出的候选兜底规则实现最简
      判断（`EquipmentHost.IsArmorSlot`）：非武器位（`is_weapon != true`）且是真正装备位
      （`is_equipment != false`，即既有 `IsEquipmentSlot`）即视为护甲位——代价是戒指/项链/饰品
      一类槽位同样会写入护甲修正，与魔兽世界"护甲仅头肩胸手腕手腰腿脚背盾"的更细分类不同。若设计
      层需要更精确区分，需要在 `item.slot_definition` 新增一个如 `is_armor` 的可选字段（ABI 允许
      新增），本任务"涉及文件"未列出该 schema 改动范围，且现有样例槽位（`item.slot.sample_main_
      hand`/`item.slot.sample_bag`）均不是护甲位、新增字段不会让既有样例立即受益，故本任务不新增
      该字段。**勘误（T-N2-6，见下方判断记录 21）：** 设计层就此上报项直接裁定——改为显式字段，
      不再是推断规则；本条保留仅作历史记录，`EquipmentHost.IsArmorSlot` 当前实现已不是这一段描述
      的样子。
    - **护甲属性 id 可配置——`ItemOptions` 新增三个可选属性**：`ArmorCurveId`（缺省
      `item.armor.default`）、`ReqLevelCurveId`（缺省 `item.req_level.default`）、`ArmorStatId`
      （缺省 `stat.armor`）。`ArmorStatId` 与 `Core.Rules.Combat.CombatOptions.ArmorStat` 默认值
      同名但不是同一常量引用——`core/carriers/item`（L3）不依赖 `core/rules/combat`（L2 具体子
      模块），两处各自维护一份手抄默认值字面量，游戏层若改挂护甲到其它属性需要同时改这两处配置。
    - **词缀反解值写入（ADR-0032 决策 7/8）——新增公开重载 `Equip(Id, Id, Id, Id?
      qualityId, IReadOnlyList<Id>? affixIds)`**：`ItemInstance` 目前不携带 `Quality`/`Affixes`
      字段（要到 T-N2-7 才落地，见该类型顶部判断记录"扩展字段……Extra"，本任务不改动
      `ItemInstance`）；任务书原文"把'词缀值写入'实现为接受 `(qualityId, IReadOnlyList<Id>
      affixIds)` 的内部/公开路径，并在既有装备路径里用'模板品质 + 空词缀'调用，T-N2-7 接上实例
      字段"。既有三参 `Equip(Id, Id, Id)` 转发本重载并传 `null`/`null`；`qualityId` 为 `null` 时
      按模板自身 `quality` 字段解析，`affixIds` 为 `null` 时按空列表处理。T-N2-7 落地后，把三参
      重载内部的转发调用改传 `instance.Quality`/`instance.Affixes` 即可接上，不需要改动本任务新增
      的其余逻辑（新增私有方法 `ApplyAffixValues`，在 `ApplyGrants` 里紧跟 `ApplyArmorValue` 之后
      调用）。选用**公开**重载而非 `internal`：`core/carriers/tests/Tests.Carriers.csproj` 以
      `ProjectReference` 引用 `Core.Carriers.csproj`（编译为独立程序集），本仓库当前未对该测试
      程序集声明 `InternalsVisibleTo`，`internal` 成员测试不可达；新增公开重载既满足"可测试"，又
      天然是 T-N2-7 想要的最终公开入口（掉落/背包装配代码后续可直接调用，不必等 `ItemInstance`
      补齐字段）。
    - **`shareOfBudget = affix.budget_share × Σratio`、`statMix` 归一化（判断记录）**：
      `IBudgetSolver.Solve` 要求 `statMix` 的 `ratio` 之和严格为 1（见该接口判断记录），但
      `item.affix.stat_mix` 的存量数据只保证"之和不超过一"（`ItemAffixStatMixRatioSumRule`）。
      `ApplyAffixValues` 按 `BudgetSolver` 类型判断记录给出的归一化方案：记原始比例之和为
      `Σratio`，传入 `Solve` 的 `statMix` 按 `ratio_i / Σratio` 归一化（之和恰为 1，不改变各属性
      间相对比例），同时把 `shareOfBudget` 由 `budget_share` 改传 `budget_share × Σratio`——这样
      "该条词缀内部未用满的份额"（`Σratio < 1`）会按比例折算进实际反解出的目标预算。任务书原文
      给出的正是这个乘积形式，本任务据此实现；`Σratio == 1`（正常数据的通常情形）时退化为
      `shareOfBudget = budget_share`，无特殊影响。反解结果（`op=flat` 点数）与模板 `stats`/护甲值
      共用同一 sourceId 写入。引用不到的词缀 id、`budget_share <= 0`、`stat_mix` 为空或全部比例
      非正——均按"这条词缀不贡献属性"静默跳过（前者额外记一条 `IItemDiagnostics.Warn` 诊断），
      不抛异常、不阻断整次穿戴。
    - **`requirements.level` 缺省按曲线反推（ADR-0032 决策 5）**：`TryGetRequiredLevel` 由
      `static` 改为实例方法（需要访问 `_registry`/`_options`）；模板显式填了 `requirements.level`
      时优先手填；未填时按 `ItemOptions.ReqLevelCurveId` 指向的 `item.req_level_curve` 在
      `item_level` 处求值。
    - **取整规则——设计层裁定（2026-09-15）：采纳**：ADR-0032 决策 5、07 第 1.1/1.2 节修订段均只给
      "由此反推"一句，未指明非整数需求等级如何取整。本方法按 `Math.Ceiling`（向上取整）——"需求
      等级"是穿戴门槛，宁可让门槛略严也不放宽，同预算/护甲/需求等级三条曲线"越界夹取到端点"这一
      既有口径里"选择更保守近似"的思路一致，设计层裁定维持 `Math.Ceiling` 不改。曲线
      找不到对应记录，或求值结果 `<= 0`，均按"无等级限制"处理（同未登记 `requirements` 字段时的
      既有行为），不抛异常。
    - **回放/Perf 基线核查**：`core/gameplay/tests/Replay/ReplayWorldBuilder.cs` 全文不引用
      `Core.Carriers.Item`/`EquipmentHost`（该回放场景不涉及装备），本任务改动的全部代码路径
      （`ApplyArmorValue`/`ApplyAffixValues`/`TryGetRequiredLevel` 曲线分支）在回放场景中不可能
      被触发；`core/gameplay/tests/EndToEndTests.cs`（经 `GameWorldFixture` 加载真实
      `data/_sample`）里唯一一次 `Equip` 调用装备的是武器槽（`item.slot.sample_main_hand`，
      `is_weapon: true`），既不是护甲位（不写护甲）也满足新增的需求等级反推（`item.sample_blade`
      的 `item_level=1`，`item.req_level_curve` 样例在 `x=1` 处取值 1，玩家注册等级为 1，
      `1 < 1` 为假，不阻断，与本任务改动前行为一致）——`dotnet test --filter
      "FullyQualifiedName~Replay"` 全绿，未触发任何基线更新流程。
    - **ABI（构造函数新增重载而不是给既有构造函数追加可选参数——判断记录）**：`toolchain/
      abi_probe.ps1` 把"给既有 `.ctor` 追加带默认值的新参数"判定为 BREAKING（C# 源码层面重编译
      调用方无感，但物理 IL 签名的参数个数变了，已编译、未重新编译的旧调用方按原签名调用会失败）
      ——原 11 参构造函数签名原样保留，新增一个 12 参重载（末尾追加 `IBudgetSolver? budgetSolver`），
      旧重载转发新重载并传 `null`。首次实现直接在原构造函数追加参数，`abi_probe.ps1` 报
      `breaks=1`（该 `.ctor` 被判定为"removed_or_changed"），改为新增重载后 `breaks=0`。
    - **测试**：`core/carriers/item/tests/T_N2_5_ArmorAffixReqLevelTests.cs`（新增 6 条，覆盖验收
      标准要求的"穿脱回退 1 组、护甲写入 1 组、需求等级反推 2 组"——穿脱回退 2 条：带词缀穿戴后
      模板 stats + 词缀反解值 + 护甲三者手算核对、卸下后三者同 sourceId 一次性全部回退；护甲写入
      2 条：护甲位模板装备后 `stat.armor` 增加 曲线×槽位系数、武器位模板即便护甲曲线存在也不写
      护甲；需求等级反推 2 条：未填时按曲线取值（`Math.Ceiling` 向上取整为 13 而非 12）并参与穿戴
      门槛判定（等级 12 拒绝、等级 13 通过）、填了以手填为准（曲线本会反推出 13，手填 3 优先）。

21. **T-N2-6（分阶段落地计划、ADR-0032 决策 4；拍板 6；07 第 1.2 节修订段）：武器秒伤查询
    `EquipmentHost.GetWeaponDps`、`damage_min/max` 偏离秒伤曲线警告
    （`ItemWeaponDamageDeviatesDpsCurveRule`）、`item.slot_definition.has_armor` 显式护甲位字段
    （设计层裁定，取代上一条判断记录 20 的推断规则）**——
    - **`IWeaponDamageQuery.GetWeaponDps(Id unitId): double` 新增（C# 8 默认接口成员，缺省
      `0.0`）**：武器秒伤 = `item.weapon_dps_curve`（id 取 `ItemOptions.WeaponDpsCurveId`，缺省
      `item.weapon_dps.default`）在该武器模板 `item_level` 处求值 × 品质预算倍率
      （`item.quality_definition.budget_multiplier`，找不到品质记录缺省 1）× 武器槽位系数（该
      武器所在槽位的 `item.slot_definition.budget_coefficient`，缺省 1——与预算系数同一个字段，
      ADR-0032 决策 4 原文明确"武器槽位系数"就是槽位定义已有的这一列，不新增字段）；武器槽的选取
      规则同既有 `GetWeaponBaseDamage`（按槽位 id 序数最先命中的武器槽，双持取第一个），两者共用
      新抽出的私有方法 `EquipmentHost.TryGetFirstWeaponSlot`（纯重构，`GetWeaponBaseDamage` 返回值
      逐位不变，硬性规则"禁止改既有签名"——本方法连内部选槽逻辑都未改变行为，只是把重复代码提取成
      共享私有方法）；未装备任何武器槽、或曲线找不到对应记录时返回 `0.0`，不抛异常（同
      `ApplyArmorValue`"曲线缺失按不写处理"既有口径）。`Core.Rules.Assembly.
      DeferredWeaponDamageQuery`（组合/代理实现）同步显式转发新成员（`Tests.Presentation.Assembly.
      InterfaceDefaultMemberForwardingTests` 门禁要求）。
    - **判断记录（品质取值来源——沿用既有限制，非本任务新引入）**：`ItemInstance` 要到 T-N2-7 才
      新增 `Quality` 字段，本方法与 `ApplyArmorValue`/`ApplyAffixValues` 同样只能取武器模板自身
      登记的 `quality` 字段，不是穿戴那一刻若显式传入的 `qualityId` 参数（那个参数不落地为可事后
      查询的状态）。T-N2-7 落地后若需要按实例真实品质求秒伤，只改内部实现，不改本方法签名。
    - **`ItemWeaponDamageDeviatesDpsCurveRule`（Warning，`NonEscalatable=true`，check
      `item_weapon_damage_deviates_dps_curve`——设计层裁定（2026-09-15）：采纳，同
      `ItemQualityMultiplierOrderRule` 一贯"04 §5 未给检查名，按 `item_weapon_*` 前缀取值"处理
      口径）**：核对武器槽模板手填
      `weapon_profile.damage_min`/`damage_max` 均值与理论期望值"武器秒伤 ×
      `weapon_profile.speed`"（`speed` 即 07 第 1.1 节"初始攻速"字段——ADR-0032 决策 4"伤害范围 =
      武器秒伤 × 初始攻速 × (1 ± 浮动)"原文，`weapon_profile.speed` 语义是"每次攻击的秒数"而非
      "每秒攻击次数"，故公式是秒伤 × speed 相乘而非相除，见 07 第 1.2 节公式原文与 `WeaponProfile`
      类型判断记录）的相对偏差；偏差超过阈值（构造参数，缺省 `±20%`——契约未给阈值，设计层裁定
      （2026-09-15）：采纳，同 `ItemBudgetValidationRule.DefaultUtilizationWarningThreshold` 一贯
      "契约给区间描述、没给具体数字"处理口径）报警告，不阻断合入。`damage_min`/`damage_max` 任一字段在 JSON 里
      不出现（而非"填了 0"，两者用 `JsonObject.TryGetValue` 区分，不能靠 `GetNumber` 的缺省值
      判断）时跳过（"未填不报"）；`item.weapon_dps_curve` 曲线找不到对应记录时本规则整体不产出
      任何问题（不同于 `ItemBudgetValidationRule` 缺曲线报 Error——武器秒伤曲线不是强制表）；只
      比较均值，不展开到 `item.weapon_dps_curve.variance`（`(1±浮动)` 的上下界）——该字段本任务
      只登记，尚无消费者。`CarriersSchemaCatalog.RegisterAll` 新增一个五参数重载（硬性规则 5：
      ABI 只允许新增，前两个既有重载签名不得改），额外接受本规则的曲线 id 与偏离阈值，惯例同
      `itemBudgetCurveId`/`itemBudgetUtilizationWarningThreshold` 两参数。
    - **`item.weapon_dps_curve.variance`（可选 Number，缺省 0.1）新增**：ADR-0032 决策 4"伤害范围
      = 武器秒伤 × 初始攻速 × (1 ± 浮动)"与拍板 6"一拍常数与浮动比例为数据项"的"浮动比例"落地
      位置——落地改动点清单第 10 节第 6 条给出候选"一拍常数与浮动比例放 `skill.budget_rule` 与
      `item.weapon_dps_curve` 旁"，设计层裁定（2026-09-15）：采纳，浮动比例登记在
      `item.weapon_dps_curve.variance`。一拍常数（另一半"数据项"）按同一候选
      登记在 `skill.budget_rule`——该表要到 T-N3-9 才创建（06 第 3.2/3.10 节修订段、落地改动点
      清单 S3/S12：`weapon_damage_pct` 原语改接"秒伤 × 一拍常数"是 N3 S3 的范围，一拍常数"只是
      记账单位，运行期不存在任何锁"），本任务不越权在 item 模块发明它的登记位置。`variance`
      字段本任务只登记 schema，无消费者（同预算利用率阈值一类"契约要求存在、具体用法留给后续
      任务"的字段）。**更新（T-N3-3 已落地，另一半"一拍常数"不再是"该表要到 T-N3-9 才创建"）**：
      `skill.budget_rule` 的最小骨架（只含 `id`/`beat_seconds`）随 T-N3-3 提前落地——
      `weapon_damage_pct` 原语在 T-N3-9 完整表落地之前就需要运行期读到一拍常数，任务书就此裁定
      分两步登记；`item.weapon_dps_curve.variance` 的消费者仍未落地（本条不受影响），完整
      `skill.budget_rule` 字段集（带宽/硬上限等）仍留给 T-N3-9，详见
      `core/rules/skill/README.md` 判断记录 50、`core/rules/skill/schema/README.md`
      "`skill.budget_rule`"一节。
    - **`item.slot_definition.has_armor`（可选 Bool，缺省 false）新增——设计层裁定**：取代
      判断记录 20 的"非武器位（`is_weapon != true`）且真正装备位（`is_equipment != false`）"
      推断规则（上一条已标记该判断记录的相应段落为历史记录）。`EquipmentHost.IsArmorSlot` 改为
      `_slotDefinitions.TryGetValue(slot, ...) && slotDef.TryGetBool("has_armor", ...) &&
      hasArmor`，不再读取 `is_weapon`/`is_equipment`——`IsEquipmentSlot` 仍用于 `Equip` 本身的
      可装备判定（未受影响）。`data/_sample/item/item.slot_definition.json` 现有两条槽位样例
      （`item.slot.sample_main_hand` 是武器位、`item.slot.sample_bag` 是非装备分类桶）均不是
      防具位，本任务未新增防具位样例（样例数据集当前没有头/胸/腿/手/脚一类槽位可供标注），
      `games/_template` 空壳表无需改动。
    - **回归测试更新**：`T_N2_5_ArmorAffixReqLevelTests.SlotJson` 的 `item.slot.t5_chest` 补
      `has_armor: true`（否则该文件全部护甲相关断言在新逻辑下失效，见该文件类型注释新增的
      "T-N2-6 更新"段）。
    - **回放/Perf 基线核查**：`ReplayWorldBuilder` 不引用 `Core.Carriers.Item`/`EquipmentHost`
      （同判断记录 20 既有核查结论），本任务改动的代码路径（`GetWeaponDps`/
      `ItemWeaponDamageDeviatesDpsCurveRule`/`IsArmorSlot`）在回放场景中不可能被触发；
      `EndToEndTests.EquipBlade_ChangesStrength_AndEmitsItemEquippedEvent` 装备的是武器位
      （`item.sample_blade`，不是护甲位），不受 `has_armor` 变化影响；`dotnet test --filter
      "FullyQualifiedName~Replay"` 全绿，未触发任何基线更新流程。
    - **示例数据调整**：`data/_sample/item/item.template.json` 的 `item.sample_blade`/
      `item.sample_model_sword` 两条 `weapon_profile.damage_min`/`damage_max` 由 `3`/`6` 改为
      `5`/`7`——原值在 `item.weapon_dps_curve`(item_level=1)=4 × 品质预算倍率 1.0 × 武器槽位系数
      1.0 = 4、× `speed` 1.5 = 期望均值 6 下，原均值 4.5 偏离 25%，超过默认阈值 20%，触发新警告；
      调整后均值 6.0，偏离 0%。样例非框架默认值，按既有惯例（同判断记录 18 调整 `stats[].value`
      的处理口径）调整示例数值而非放宽阈值。`data/_sample/item/item.weapon_dps_curve.json` 补一条
      `variance: 0.1`（与缺省值相同，仅作为新字段的样例展示，同判断记录 18 给 `item.budget_curve`
      补 `exponent` 样例的处理口径）。
    - **测试**：`core/carriers/item/tests/T_N2_6_WeaponDpsDeviationTests.cs`（新增 9 条：
      `GetWeaponDps` 3 条——装备武器手算秒伤、未装备武器返回 0、曲线 id 未注册返回 0；偏离警告
      4 条——均值远离期望值报警告、均值匹配期望值不报、`damage_min`/`damage_max` 未填不报、自定义
      阈值构造重载放宽接受范围；`has_armor` 字段 2 条——显式 `true` 的槽位写入护甲、非武器且未显式
      登记 `has_armor` 的槽位（如戒指位）不再写入护甲，是 T-N2-5 旧推断规则会误判、T-N2-6 显式
      字段规则下的核心回归用例）；`T_N2_5_ArmorAffixReqLevelTests.cs`（既有 6 条用例数据夹具同步
      更新，用例数不变）。

22. **T-N2-7（分阶段落地计划、ADR-0032 决策 8；10 第 2.5 节修订段"物品实例只存身份"）：
    `ItemInstance` 新增 `Quality`/`Affixes`、存档 `quality`/`affixes` 两个 key 与缺省/兼容读取、
    三参 `EquipmentHost.Equip` 改转发实例字段**——涉及 `core/carriers/common/contracts/
    ItemInstance.cs`、`core/carriers/item/core/ItemInstanceJson.cs`、
    `core/carriers/item/core/ItemPersistable.cs`、`core/carriers/item/core/InventoryHost.cs`、
    `core/carriers/item/core/EquipmentHost.cs`。
    - **`ItemInstance` 新成员（ABI：新增构造函数重载，不改既有 4 参构造函数——同判断记录 20
      "ABI（构造函数新增重载……）"一贯做法）**：`public Id Quality { get; }`（不是 `Id?`——见类型
      顶部判断记录"品质是恒定存在的身份字段，不是可选扩展点"）、`public IReadOnlyList<Id> Affixes
      { get; }`（不可变，默认空列表，不是 null）；新增构造函数 `ItemInstance(Id instanceId, Id
      templateId, int count, Id quality, IReadOnlyList<Id>? affixes, JsonObject? extra = null)`。
      旧 4 参构造函数原样保留，内部转发新构造函数并传 `default(Id)`/`null`（未指定品质时 `Quality.
      Value == null`，即"未解析"状态）。
    - **创建点判断记录（`ItemInstance` 构造时若未给品质则取模板品质，需要模板查询）**：`Add
      ItemCore`（`InventoryHost.cs`）是本仓库唯一的"新增物品实例"入口（不接受显式品质参数——带
      显式品质/词缀的掉落创建属于 T-N2-8），已同步改为在新开堆叠时用 `template.GetId("quality")`
      解析缺省品质（该方法此前已经查过 `template`，不需要额外一次表查询）；续填既有堆叠/部分移除
      两处原样保留该实例已有的 `Quality`/`Affixes`（不能只传旧 4 参构造函数——那会把品质/词缀身份
      悄悄重置为"未解析"/空，是本任务里一个真实存在、容易漏掉的坑，专门在 `InventoryHost.cs`
      对应位置留了判断记录）。`InventoryHost` 另新增 `internal Id ResolveTemplateQuality(Id
      templateId)`，与 `AddItemCore` 共用同一条"缺省取模板自身 `quality` 字段"口径，供
      `ItemInstanceJson.FromJson` 在存档兼容读取时调用（见下方存档判断记录）；模板在当前已加载
      数据里找不到时返回"未解析"的 `Id`（`Value == null`），不强行让整次读档失败（同 `AddItemCore`
      对"新增物品但模板未知"会抛异常不同——那是"新增"场景，这里是"读一份可能引用了已被数据更新
      移除的旧模板 id 的历史存档"场景）。
    - **存档兼容方式判断记录（不升 `save_version`、不登记 `ISaveMigration` 迁移函数——先例出处见
      `ItemInstanceJson.cs` 类型顶部判断记录）**：本仓库对"既有存档段条目形状新增可选字段"的既定
      先例是 `CHANGELOG.md` [1.7.0] 迁移说明的两条——① AUD-03 `world.vendor_stock` 段 `timer`
      物品条目新增可选字段，"`Load` 完全向后兼容纯数字旧格式，无需游戏侧改动"；② `player.
      achievement_state` 每条记录新增可选字段 `pending_reward`，"向后兼容，旧存档缺省该字段按
      `false` 处理"——两条都不升 `save_version`、不登记迁移函数，直接在字段读取处做缺省处理；
      `player.inventory`/`player.equipment` 段本身没有独立的段级版本号（10 第 5 节"迁移链读取/
      改写的是存档文档信封层（顶层）的 `save_version` 字段"），物品实例只是这两段内部数组/映射的
      条目，与"整段缺失"（10 第 2 节"缺失段语义"，走 `load(null)` 清空）是两回事——本次是"段仍
      在，条目形状新增两个可选 key"，与 AUD-03/achievement_state 场景一致，因此照抄同一先例：
      `ItemInstanceJson.FromJson` 直接在解析处对缺失的 `quality`/`affixes` key 做缺省。
    - **`quality` 缺省值判断记录——设计层裁定（2026-09-15）：采纳**：ADR-0032 决策 8"物品
      实例存……品质"未进一步说明旧存档（无 `quality` key）读档时该品质取什么值；07/10 与任务书
      原文给出的方向是"缺省取模板自身品质"（等价于"这件旧物品从它存在那一刻起就是模板默认品质"这
      一最保守假设，不会凭空让旧物品变得比原先更强/更弱）。本任务据此实现：`FromJson` 接受一个
      `Func<Id, Id> resolveTemplateQuality` 回调（自身不持有 `IDataRegistryView`，无法查表），由
      `InventoryPersistable`/`EquipmentPersistable` 传入 `InventoryHost.ResolveTemplateQuality`。
      `affixes` 缺省为空列表，语义明确（旧物品没有词缀身份数据可恢复，只能视为无词缀），不存在
      同等的待确认问题。
    - **坏值处理口径（与既有字段一致）**：`quality`/`affixes` key 存在但值非法（非字符串/非法
      `Id`/`affixes` 不是数组/数组元素非法）时，与 `instance_id`/`template_id`/`count` 坏值同一
      口径——抛 `System.FormatException`，不静默吞掉、不当成缺省处理（`ItemInstanceJson` 是纯
      JSON↔结构体转换层，不是数据表校验层，不用 `DataFieldException`——那是 `DataRecord`/schema
      校验层的类型，两者一贯分工不同）。
    - **`EquipmentHost` 三参 `Equip` 改转发实例字段**：`Equip(Id, Id, Id)` 先查一次背包里这件
      物品的当前实例，转发它自带的 `Quality`/`Affixes`（查不到实例时仍转发 `null`/`null`，五参
      重载内部会再查一次 `InventoryHost.FindInstance` 并统一返回 `NotInInventory`，行为与改动前
      一致）——按判断记录 20 原计划落地，多查一次背包是同一单位背包内的字典/列表查找，代价可
      忽略。
    - **五参 `Equip` 需要额外补的一处一致性修复（不在任务书"涉及文件"字面列出，但不修就无法满足
      验收标准"装备一件带词缀的实例后存档再读档，StatHost 属性与存档前一致"，判断记录）**：五参
      重载原本只用 `resolvedQuality`/`resolvedAffixes` 驱动 `ApplyGrants`（写入 `StatHost`），但
      存入 `unitSlots[slot]`/背包的 `taken` 仍是原样——若调用方显式传入的 `qualityId`/`affixIds`
      与 `taken` 自带的 `Quality`/`Affixes` 不一致（例如 T-N2-5/本任务测试沿用的"显式传参覆盖"
      调用方式），会出现"`StatHost` 上生效的品质/词缀"与"`GetAllEquippedInstances`/
      `EquipmentPersistable.Save` 序列化出的身份字段"两者不一致——存档/读档（`EquipmentPersistable.
      Load` 走三参 `Equip`，转发的是存档里 `instance.Quality`/`Affixes`）会用序列化出的（旧、不
      一致的）身份重新反解，读档后 `StatHost` 与存档前不再相等，直接违反 ADR-0032 决策 8"读档按
      数据重算……与存档前一致"这一不变量。本任务在五参重载内补了一行：解析出
      `resolvedQuality`/`resolvedAffixes` 后，立即用它们重建 `taken`（`new ItemInstance(taken.
      InstanceId, taken.TemplateId, taken.Count, resolvedQuality, resolvedAffixes, taken.
      Extra)`）再存入 `unitSlots`，保证"`StatHost` 上生效的身份"与"这件装备实例自己携带、会被
      存档序列化的身份"恒一致，不存在第二份影子状态。
    - **回放/Perf 基线核查**：`ReplayWorldBuilder` 不引用 `Core.Carriers.Item`/`InventoryHost`/
      `EquipmentHost`（同判断记录 20/21 既有核查结论），本任务改动的全部代码路径在回放场景中不
      可能被触发；`dotnet test --filter "FullyQualifiedName~Replay"` 全绿，未触发任何基线更新
      流程。
    - **测试**：`core/carriers/item/tests/T_N2_7_ItemInstanceIdentityPersistenceTests.cs`（新增
      5 条：旧存档兼容读取 2 条——`player.inventory` 段缺 `quality`/`affixes` key 按模板品质/空
      词缀解析、`player.equipment` 段同场景验证只生效模板 `stats`/护甲不生效词缀反解值；新存档
      往返 1 条——带显式品质（不同于模板自身品质）+ 词缀的实例，解析一次后再存→读一次，两次结果
      逐字段一致；坏值 1 条——`quality` 字段非法值抛 `FormatException`；集成 1 条——装备一件带
      词缀的实例（`StatHost` 属性手算 105/10，同 `T_N2_5_ArmorAffixReqLevelTests` 口径）后存档、
      用全新宿主读档，`StatHost` 属性与存档前一致，且读档后装备实例自身的 `Quality`/`Affixes`
      与存档前一致）。

23. **T-N2-9（分阶段落地计划；ADR-0034 决策 8"背包容量来源"；07 第 1.3 节 2026-09-14 修订段）：
    `InventoryOptions.MaxSlots` 来源二选一（固定值或引用属性）、`IInventoryHost.GetCapacity` 容量
    查询默认接口方法**——涉及 `core/carriers/item/contracts/ItemOptions.cs`、`core/carriers/common/
    contracts/IInventoryHost.cs`、`core/carriers/item/core/InventoryHost.cs`、
    `core/carriers/assembly/CarriersAssembly.cs`。
    - **来源二选一记法（不新增枚举字段）**：`InventoryOptions` 新增 `Id? MaxSlotsStat`；为 null
      （默认）时容量走既有 `MaxSlots` 固定值，非 null 时容量改由该属性 id 决定——沿用 `arch
      .power_type` 的 `PowerMaxSourceKind.Fixed`/`Stat` 二选一惯例（ADR-0034 决策 8 原文"与
      arch.power_type 上限来源同一写法"），但本类型是 C# 运行期配置对象（不像 `PowerTypeDefinition`
      从 `DataRecord` 解析），"字段是否为 null"本身已无歧义表达二选一，不必另加种类枚举字段。
    - **`IInventoryHost.GetCapacity(Id unitId): int` 新成员（默认接口方法）**：`int.MaxValue`
      表示不限；默认实现恒返回 `int.MaxValue`——判断记录：本接口新增该成员之前完全没有"容量"概念，
      未覆盖的既有实现（含各模块测试 Fake）对调用方而言此前就等价于"没有已知上限"，默认值原样
      表达这一历史现状，不引入新断言（备选方案"默认抛 `NotSupportedException`"被否决，会让既有
      Fake 从能用变成崩溃）。`InventoryHost` 显式覆盖本方法，不依赖默认值——生产程序集内唯一实现
      `IInventoryHost` 的类型只有 `InventoryHost`（已核对，`InterfaceDefaultMemberForwardingTests`
      不需要额外豁免登记）。
    - **`InventoryHost` 新构造重载（ABI：新增重载，不改既有 3 参构造函数）**：新增
      `InventoryHost(IDataRegistryView, IEventBus, InventoryOptions?, Func<Id, Id, double>?
      statLookup)`，`statLookup` 供 `MaxSlotsStat` 非 null 时解析容量使用（签名
      `(unitId, statId) => 当前值`）。判断记录（不复用 `Core.Numbers.PowerSet.StatLookup` 具名
      委托类型）：`core/carriers/item`（L3）依赖 `core/numbers/stat_block`（L1，`EquipmentHost`
      已直接引用 `IStatHost`）没有分层问题，但 `StatLookup` 定义在与背包容量无关的另一个 L1 模块
      `power_set`，只是恰好委托形状相同；为借用一个类型名引入跨模块依赖不值得，改用裸
      `Func<Id, Id, double>` 表达同一形状，两个模块各自独立解决同一个"构造期时序"问题、互不引用。
    - **`InventoryHost.GetCapacity` 判断记录（属性来源没有"不限"语义，floor 后夹取到下限 0——
      设计层裁定（2026-09-15）：采纳）**：固定值路径"≤0 表示不限"是历史既有行为，本任务不改变；
      但 07/ADR 原文
      对属性来源没有定义同等的"不限" sentinel——沿用该语义会让恰好取值 0（或被减益压低）的真实
      属性被误判为不限容量，与"容量不足时拒绝新增"的契约意图相反，因此属性来源解析结果 floor 后
      `< 0` 才夹到 0，`== 0` 就是容量已耗尽，不退化为不限。`MaxSlotsStat` 非 null 但构造期未提供
      `statLookup` 时，不在构造期报错，在首次调用 `GetCapacity` 解析容量的那一刻抛
      `InvalidOperationException`（同 `PowerHost` 对未注入 `StatLookup` 的既有处理时机）。
    - **容量判定统一收口**：`AddItemCore`/`TryPutBack`/`HasRoomForOne` 三处原先直接读
      `_options.MaxSlots` 的判定，改为统一调用 `GetCapacity(unitId)`——`int.MaxValue` 分支与既有
      "不限"语义原样保留，固定值路径三组既有测试断言不变。容量收缩到低于已有物品数时（属性被减益
      压低）的行为不需要额外特判代码：`AddItemCore` 判断记录 2 的原子失败分支
      （`newSlotsNeeded > availableSlots` 时 `availableSlots` 已被 `Math.Max(0, …)` 夹到 0）天然
      产生"不丢物品、只拒绝新增"的效果——已有物品仍在 `_bags` 里原样保留，只是后续 `AddItem`/
      `TryAddItem` 返回失败/`actualCount=0`，验证见下方测试第三、四条。
    - **`CarriersAssembly` 接线（不在任务书"涉及文件"字面列出，但不接线时 `MaxSlotsStat` 在真实
      装配下无法工作——判断记录）**：`InventoryHost` 构造（步骤 2）先于 `RulesAssembly`（步骤 3，
      真正的 `IStatHost` 由其内部构造）——与本文件第 1 步 `healthFractionSetter`/`powers` 同一种
      "两者互相需要对方，真正调用发生在构造完成之后即可安全提前绑定"的循环依赖处理手法：闭包捕获
      尚未赋值的 `statsRef` 局部变量，传入 `InventoryHost` 的新构造重载，`RulesAssembly` 构造完成
      后立即回填（`statsRef = Rules.Stats;`，紧邻既有 `powers = Rules.Powers;` 一行）。
      `MaxSlotsStat` 为 null（默认）时这份委托不会被调用，对既有行为无影响。
    - **回放/Perf 基线核查**：同判断记录 20/21/22 既有核查结论，`ReplayWorldBuilder` 不引用
      `Core.Carriers.Item`/`InventoryHost`；`dotnet test --filter "FullyQualifiedName~Replay"`
      全绿，未触发任何基线更新流程。
    - **测试**：`core/carriers/item/tests/T_N2_9_InventoryCapacitySourceTests.cs`（新增 7 条：
      固定值路径行为不变 2 条——`MaxSlots=0` 不限、`MaxSlots=2` 时 Reject 整批原子失败/成功各一次；
      属性来源随光环变化 1 条——`AddModifier`/`RemoveModifiersBySource` 前后 `GetCapacity` 现查
      结果分别为 1/3/1，不需要显式"重算容量"调用；容量收缩不丢物品只拒绝新增 1 条——先在容量 3
      时装满 3 件，光环撤销后容量回落到 1，`ListItems`/`CountOf` 仍是 3，Reject/Partial 两种策略
      的后续加入均被拒绝（`actualCount=0`）；属性来源下限夹取 1 条——减益把最终属性值压到 -4，
      `GetCapacity` 返回 0 而不是"不限"；未注入 `statLookup` 时抛异常 1 条；接口默认成员本身
      1 条——未覆盖 `GetCapacity` 的最小 Fake 恒返回 `int.MaxValue`）。

24. **T-N2-8b（T-N2-8 已知缺口收口，见判断记录 22 末段与 `core/gameplay/loot/README.md` 判断记录
    15 末段"拾取入包的品质/词缀传递留给后续任务补齐"；ADR-0032 决策 7/8）：`IInventoryHost` 新增
    带身份的 `AddItem`/`TryAddItem` 重载，`InventoryHost` 显式实现并接入非默认身份不堆叠规则**——
    涉及 `core/carriers/common/contracts/IInventoryHost.cs`、`core/carriers/item/core/
    InventoryHost.cs`。
    - **新成员签名（ABI：新增默认接口成员，照既有 `AddItem`/`TryAddItem` 的返回类型与参数顺序
      追加两个身份参数）**：`bool AddItem(Id unitId, Id templateId, int count, Id? qualityId,
      IReadOnlyList<Id>? affixes)`、`bool TryAddItem(Id unitId, Id templateId, int count, Id?
      qualityId, IReadOnlyList<Id>? affixes, out int actualCount)`；默认实现转发旧签名（丢弃身份，
      落地物品仍是模板缺省品质/无词缀）——同判断记录 23`GetCapacity` 一贯口径"默认值 = 历史行为
      原样保留，只有 `InventoryHost` 需要覆盖"，未覆盖的既有实现（各模块测试 Fake）不需要改动即可
      继续编译通过；`InterfaceDefaultMemberForwardingTests` 已确认生产程序集内唯一实现
      `IInventoryHost` 的类型只有 `InventoryHost`，已显式覆盖两个新成员，不需要额外豁免登记。
    - **堆叠规则判断（"默认身份"是能否续填/合并既有堆叠的唯一标准，与调用方是否显式传参无关）**：
      `InventoryHost.AddItemCore` 解析出 `resolvedQuality = qualityId ?? templateQuality`、
      `resolvedAffixes`（非空时取之，否则视为空），当且仅当 `resolvedQuality == templateQuality
      且 resolvedAffixes 为空` 时判定为"默认身份"——`Core.Gameplay.Loot.LootHost.
      ResolveDefaultOutcome` 一类"缺省照模板品质解析"的调用路径会显式传入一个等于模板品质的
      `Id`（不是 `null`），同样判定为默认身份，续填/合并行为与改造前逐字节一致（既有三组
      `AddItem_StacksSameTemplate_MergesUpToStackSize` 一类测试未改动、原样通过）。带词缀或非
      模板品质的物品视为与模板默认形态不同的身份，即使 `templateId` 相同、即使两次给的身份完全
      一样，也不与任何既有堆叠合并，总是新开格子——本任务范围内设计层已拍板的简化取舍：同一品质
      同一词缀组合的战利品反复掉落时不会自动堆叠成一条，代价是格子占用更多，换来的是不需要引入
      "词缀顺序无关的集合相等"这一更复杂的堆叠判定（多重词缀顺序不同但内容相同是否算"同一身份"，
      架构未给出判断依据）。见 `core/carriers/item/tests/InventoryHostTests.cs`
      （`AddItem_WithIdentity_StoresQualityAndAffixesOnNewInstance`/
      `AddItem_SameTemplateDifferentQuality_DoesNotStack`/
      `AddItem_NonDefaultIdentity_NeverStacksEvenWithIdenticalIdentity_OrExistingDefaultStack`）与
      `core/carriers/common/tests/ItemTypesTests.cs`（默认接口成员转发行为 2 条）。
    - **消费方——`Core.Gameplay.Loot.LootHost.PickUp` 改用带身份重载**：见
      `core/gameplay/loot/README.md` 判断记录 16。本模块自身不触碰 `LootHost`（L4 模块），只提供
      被消费的契约面。

25. **T-N2-11（分阶段落地计划第 8 节阶段 N2 验收标准 5；ADR-0032 决策 7/10；04 第 5 节数值类校验项
    分级表"模板加词缀最大份额超预算"行）：新增阻断校验 `ItemTemplateAffixShareExceedsBudgetRule`，
    补齐 T-N2-3/T-N2-4/T-N2-8/T-N2-10 均未落地的这一条**——`data/README.md`"item N2 示例数据"一节
    判断记录明确记录了这一缺口（T-N2-10 只人工核算样例合规、不实现规则），本任务补上。
    - **检查名——设计层裁定（2026-09-15）：采纳**：04 该行原文未给出具体检查名，按
      `item_template_affix_share_exceeds_budget`（`item_template_*` 前缀，同
      `item_quality_*`/`item_affix_*`/`item_weapon_*` 一贯"04 §5 未给检查名，按表名前缀取值"处理
      口径）登记，已同步补进 04 第 5 节该行的勘误记录。
    - **语义——设计层裁定（2026-09-15）：采纳**：`consumed = ItemBudgetCurve.ComputeConsumed(stats,
      statInfo, itemLevel, exponent)`（同 `ItemBudgetValidationRule` 既有消耗侧公式，基础权重、不
      按职业覆盖）；`B = 曲线(item_level) × 品质预算倍率 × 槽位系数`（同 `ItemBudgetValidationRule`
      既有算法）；候选词缀 = `item.affix` 中 `quality_pool == 本模板 quality`，且模板 `affixes`
      白名单非空时再与之取交集；`maxShare` = 候选按 `budget_share` 降序（同份额按 `Id` 升序稳定
      排序）取前 `affix_count`（本模板品质在 `item.quality_definition.affix_count` 登记的数量；
      该字段未登记时视为"不限"，取全部候选——这是校验期的保守上界口径，刻意区别于掉落三次掷骰
      运行期"`affix_count` 未登记按 0（不掷词缀骰）"的处理：词缀池将来扩容、`affix_count` 补登记
      都不应该让已经通过校验的旧数据突然超标）之和；`consumed + maxShare × B > B`（1e-9 浮点容差）
      报 Error，消息点出 `consumed`/`B`/`maxShare` 三个量。`B ≤ 0` 或预算曲线记录不存在时跳过——
      后者 `ItemBudgetValidationRule` 已经报出"预算曲线不存在"Error，本规则不重复报错。
    - **注册**：`CarriersSchemaCatalog.RegisterItemSchemas` 内随其余 item 规则一并注册，复用既有
      `budgetCurveId` 参数（与 `ItemBudgetValidationRule` 核算同一条预算曲线），不新增
      `RegisterAll` 重载、不新增独立配置参数。
    - **测试**：`core/carriers/item/tests/ItemValidationRulesTests.cs` 新增 5 组（consumed+maxShare
      超预算报错、消耗内不超预算不报错、两条候选无白名单按份额降序取到更大份额而超预算、模板
      `affixes` 白名单收窄到份额更小的候选后不再超预算、品质 `affix_count` 未登记按不限取全部
      候选之和）。
    - **示例数据核实**：`data/_sample/item/**`（T-N2-10 已充实的多品质多槽位模板）与
      `games/_template/data`（空壳表）两个数据根 `python toolchain/validate_data.py --strict` 均
      `ItemTemplateAffixShareExceedsBudgetRule: error, hits 0`——与 `data/README.md`"item N2 示例
      数据"一节的人工核算表结论一致，零改样例。

## 契约缺口清单（本次未新增/未修改 `core/rules/*`）

- `Core.Rules.Common.ISkillHost` 没有"学习/遗忘技能"方法（技能书能力目前只存在于
  `core/rules/skill` 内部实现，未提升到共享契约）：`EquipmentHost` 构造参数改用本模块新增的
  `SkillGranter` 具名委托绕过，由更上层组装代码把真实的 `SkillHost.LearnSkill`/`Forget` 适配成这个
  签名后注入，见 `contracts/SkillGranter.cs` 顶部注释。
- `Core.Rules.Common.IEffectSink` 没有"按来源整体撤销光环"的方法（只有
  `RemoveAura(unitId, AuraInstanceRef)` 按单个实例句柄撤销）：本模块自行维护
  `Dictionary<(unitId, instanceId), List<AuraInstanceRef>>`/`Dictionary<(unitId, setId),
  Dictionary<threshold, List<AuraInstanceRef>>>` 两份表分别记录"某件装备/某个套装门槛各自持有哪些
  句柄引用"，但这两份表不再各自独立决定"是否真的调用 `RemoveAura`"（R03 收口前是这样，见上方第 9
  条判断记录）——两者共同经 `RegisterAuraHandle`/`ReleaseAuraHandle` 把引用计数并入同一份
  `_auraHandleRefCount`（按实例句柄，不是按来源类型），只有全部来源（不论来自哪份表）都释放完毕、
  计数真正归零才调用 `RemoveAura`。不算契约缺口（`IEffectSink` 本就没有"按来源批量撤销光环"这一
  语义，`StatModifier` 的按来源撤销是 L1 `stat_block` 独有能力，两者不对称是既有设计，不是本次任务
  遗漏）。
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
