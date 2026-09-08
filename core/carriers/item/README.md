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
