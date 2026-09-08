# Core 持久化、规则与 Gameplay 生命周期深度审计

审计基线：8160178b76fb51ae704a8f14b428decf228cc33e（v1.7.0）。

范围限定为 Core、生产入口和 Stub 适配器的真实 .NET 链路；未修改产品源码、既有测试或提交 Git。探针源码位于 [CorePersistenceProbe/Program.cs](repro/CorePersistenceProbe/Program.cs)，工程位于 [CorePersistenceProbe.csproj](repro/CorePersistenceProbe/CorePersistenceProbe.csproj)。所有复现均使用真实 GameplayAssembly、WorldSim、SaveSystem、EquipmentHost、ProgressionHost 和事件总线组合。

## 发现汇总

| 编号 | 级别 | 状态 | 结论 |
|---|---|---|---|
| CORE-170-01 | P2 | 已复现 | 进图先重放装备、再按 HasAura 重放种族被动；同一 auraDef 时种族来源没有记账，卸下装备会把种族被动一起移除。 |
| CORE-170-02 | P2 | 已复现 | 规则进度等级与实体 PlayerUnit.Level 分叉；升级后真实装备等级需求仍按旧实体等级拒绝。 |
| CORE-170-03 | P2 | 已复现 | 失败的 Equipment 存档段在验证 JSON 形状前清空旧装备，而 SaveSystem 只回滚此前成功段；该失败段自身不恢复。 |
| CORE-170-03 补充 | P2 | 已复现 | 成功段逆序回滚会向真实 EventBus 产生重放事件；AchievementHost 将回滚后的 ItemEquipped 再计数并错误解锁。 |

## CORE-170-01：共享 aura 的种族来源被装备卸下误删（P2，已复现）

**精确位置。** [RulesAssembly.cs:440-456](../../../../core/rules/assembly/RulesAssembly.cs:440) 的 ReapplyRacePassiveAuras 对每个种族 auraDefId 先调用 Skill.AuraQuery.HasAura，已有实例就直接 continue，否则才以 raceId 为来源应用。 [GameplayAssembly.cs:884-895](../../../../core/gameplay/assembly/GameplayAssembly.cs:884) 的 EnterMap 明确先调用 Equipment.ReapplyGrants，然后再调用种族重放。装备重放自身在 [EquipmentHost.cs:684-715](../../../../core/carriers/item/core/EquipmentHost.cs:684) 只按装备/套装台账和 HasAura 做幂等判断；卸装通过 [EquipmentHost.cs:594-625](../../../../core/carriers/item/core/EquipmentHost.cs:594) 的实例句柄引用计数释放装备来源。

**触发条件。** 配置一条非空 arch.race.passive_auras，其 auraDef 同时出现在装备模板 grants.auras；玩家有该种族并已装备物品。RaceId=null 时 EnterMap 按 [GameplayAssembly.cs:892-896](../../../../core/gameplay/assembly/GameplayAssembly.cs:892) 跳过，本发现不冒充无种族默认样例。

**复现步骤。** 探针创建真实玩家、种族被动和同一装备 aura；装备并派发事件；执行 World.ClearAll()、排空事件，再 World.AddEntity()、Gameplay.EnterMap()；最后真实 Equipment.Unequip() 并排空事件。

**预期 / 实际。** 进图后 aura 应有种族和装备两个来源的有效记录；卸下装备后仍应保留种族 aura。实际输出：

    equip_success=True
    after_clearall=hasAura=false,stacks=0,power=11
    after_enter_map=hasAura=true,stacks=1,power=61
    unequip_success=True
    after_unequip=hasAura=false,stacks=0,power=11

因此进图后的单一 aura 实例由装备重放先创建，种族重放因 HasAura=true 未建立种族来源；卸装的最后一个装备句柄释放时把实例删除。证据保存在 [core-persistence-probe.raw.log](repro/core-persistence-probe.raw.log)，探针退出码为 [core-persistence-probe.exit.txt](repro/core-persistence-probe.exit.txt) 中的 0。

**影响范围。** 任何可配置为“装备 grants 与种族 passive 共享同一 auraDef”的玩家，在跨图清空/重建实体后会出现。仅种族独占 aura、仅装备独占 aura、无种族玩家不触发。现有独立装备/套装共享源测试通过，不能覆盖跨载体（种族 + 装备）来源。

**修复与验收建议。** 让 aura 账本按来源保存种族句柄和装备句柄，跨来源共享时使用统一引用计数；ReapplyRacePassiveAuras 不应以“已有实例”推断“已有种族来源”。也可以在重放时先恢复来源账本，再按账本重建实例。验收必须包含：首次注册、ClearAll + Drain、重新 AddEntity + EnterMap、卸下装备、再次 Drain；断言 aura 仍存在且种族 stat modifier 保持，装备独占 modifier 消失。

## CORE-170-02：Progression 等级与 PlayerUnit.Level 分叉（P2，已复现）

**精确位置。** [WorldUnitAccess.cs:59](../../../../core/carriers/unit/core/WorldUnitAccess.cs:59) 的 GetLevel 直接读取 WorldSim 中 PlayerUnit.Level。[ProgressionHost.cs:179-182](../../../../core/numbers/progression/core/ProgressionHost.cs:179) 把等级保存在自己的 UnitState；[ProgressionHost.cs:192-228](../../../../core/numbers/progression/core/ProgressionHost.cs:192) 的 AddXp 只更新内部 unit.Level；[ProgressionHost.cs:292-304](../../../../core/numbers/progression/core/ProgressionHost.cs:292) 的 RestoreState 也只替换内部状态并发布 ProgressionRestoredEvent，没有写回 PlayerUnit.Level。真实入口在 [GameBootstrap.cs:239-249](../../../../games/_template/Runtime/GameBootstrap.cs:239)、[FrameworkResidentHost.cs:351-367](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Shell/FrameworkResidentHost.cs:351) 和 [GameFoundationBootstrap.cs:344-358](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Bootstrap/GameFoundationBootstrap.cs:344) 构造 PlayerUnit 时没有设置 Level，随后只把 PlayerLevel 传给 Rules.RegisterUnit。

**触发条件。** 生产式新游戏初始实体等级为构造默认值 1，Rules 以 level 1 注册；随后正常 Progression.AddXp 升到 2，或读档 Progression.RestoreState 恢复到更高等级。需要等级 2 的装备走真实 EquipmentHost.Equip。

**预期 / 实际。** 规则等级和单位访问等级应保持同一状态，等级 2 的装备应通过需求检查。探针使用真实 level curve（1 级 100 XP）和 requirements.level=2 的物品，输出：

    rules_progression_level=2;entity_level=1
    equip_result=False;equip_reason=RequirementNotMet

需求判断的生产位置为 [EquipmentHost.cs:262-265](../../../../core/carriers/item/core/EquipmentHost.cs:262)，其 _unitAccess.GetLevel 因而读到 1。证据同样在 [core-persistence-probe.raw.log](repro/core-persistence-probe.raw.log)，探针退出码为 0。

**影响范围。** 所有依赖 IUnitAccess.GetLevel 的装备需求及其它运行期消费者；内部 Progression UI/查询若直接读 ProgressionHost 仍会显示 2，形成同一玩家两种等级。读档恢复高等级会保留该分叉，直到有明确同步逻辑（本基线未找到）写实体字段。

**修复与验收建议。** 选定单一等级权威，并让 WorldUnitAccess 与 ProgressionHost 使用同一读路径；若保留 PlayerUnit.Level，则在 AddXp 升级和 RestoreState 成功后由明确适配层同步实体字段，并覆盖多级升级、读档、装备需求及 UI 查询。生产式验收应断言 Progression.GetLevel == WorldUnitAccess.GetLevel == PlayerUnit.Level，再执行等级需求装备。

## CORE-170-03：失败 Equipment 段自身不回滚（P2，已复现）

**精确位置。** [ItemPersistable.cs:126-143](../../../../core/carriers/item/core/ItemPersistable.cs:126) 的 EquipmentPersistable.Load 在确认 JsonObject 形状前，于第 132 行调用 ClearAllEquippedForLoad；非对象在第 139-143 行才抛 FormatException。[SaveSystem.cs:354-382](../../../../core/foundation/save_system/core/SaveSystem.cs:354) 只有 persistable.Load 成功后才把 key 加入 loadedKeysInOrder，失败时只对“此前已成功加载”的 key 调用回滚；逆序实现位于 [SaveSystem.cs:706-727](../../../../core/foundation/save_system/core/SaveSystem.cs:706)。

**触发条件。** 玩家已有真实装备，读档的 player.equipment 段存在但形状是 JSON 字符串等非法类型；该段是首先/唯一失败段，因此它没有进入成功列表。

**预期 / 实际。** 失败读档应保持读档前的装备和派生状态。探针先真实装备 max-health 物品，再载入坏形状段：

    equip_success=True
    before=True
    load_status=PersistableThrew
    after_load_before_dispatch=False
    after_dispatch=False
    events=StatChanged,ItemUnequipped

实际装备已在 Load 抛异常前被清空，且 SaveSystem 没有任何成功 key 可回滚；ItemUnequipped 也已进入真实 EventBus。证据为 [core-persistence-probe.raw.log](repro/core-persistence-probe.raw.log) 的 FAILED-EQUIPMENT-SEGMENT 段，退出码为 0。

**影响范围。** 坏存档、版本迁移后错误 shape、手工损坏段等触发；装备清空还会改变装备 stat/aura、背包联动和观察者看到的事件。与“此前成功段逆序回滚”的契约不是同一问题，不能由已有成功段回滚测试覆盖。

**修复与验收建议。** 在修改 live EquipmentHost 前完整验证 JSON shape、slot key、ItemInstance 和可恢复性，构造临时恢复计划后一次提交；或为失败段保存并恢复自身快照，同时抑制失败尝试产生的外部事件。验收需分别覆盖：坏 shape、非法 slot、坏 ItemInstance、恢复中途某槽失败；每项都断言装备、背包、stat/aura 与事件观察者状态回到读档前。

## CORE-170-03 补充：成功段回滚的事件副作用（P2，已复现）

在同一真实组合中，Equipment、PlayerVitals 和 Achievement 段成功加载后，后续自定义段抛异常；SaveSystem 逆序调用真实 Equipment/Vitals/Achievement Load。fixture 的完整 events 列表包含初始化装备事件；日志中的 queued_event_count_before_dispatch=3 实际来自观察列表 fx.Events.Count，不是 EventBus PendingCount。最终数值能恢复：equipped=True, health=200, max=200，所以本轮排除了“HP 被 maxHealth clamp 后永久丢失”的候选。回滚完成后，真实 AchievementHost 将重放的 ItemEquipped 再计数：

    StatChanged,ItemEquipped,PowerChanged,StatChanged,ItemUnequipped,
    PowerChanged,PowerChanged,StatChanged,ItemEquipped

证据为日志中的 SAVE-ROLLBACK-DERIVED-STATE 段：achievement_before=1，achievement_before_event_dispatch=1,unlocked=False，after_event_dispatch=2,unlocked=True。AchievementHost 的 custom_event 计数入口位于 [AchievementHost.cs:317-325](../../../../core/gameplay/achievement/core/AchievementHost.cs:317)，进度达标并解锁位于 [AchievementHost.cs:358-396](../../../../core/gameplay/achievement/core/AchievementHost.cs:358)。因此这是已由真实业务消费者确认的回滚副作用，应与 CORE-170-03 的失败段不回滚一并作为 SaveSystem 读档事务问题验收。修复方向是回滚期间缓冲/抑制领域事件，或让事件携带 rollback/replay 语义并使 Achievement 等消费者忽略回滚事件；不能依赖“最终数值相同”判定事务无副作用。

## 旧发现复核（不重报）

| 旧项 | 本基线复核 | 证据与结论 |
|---|---|---|
| AUD-01：旧 pending loot/gobj 字段兼容与 SaveSystem 基本成功段回滚 | 复核通过旧场景 | GobjPendingLootPersistable 当前兼容旧 gobjInstanceId 并安全丢弃；SaveSystemTests 定向 49/49 通过。当前新增 CORE-170-03 暴露的是失败段自身先变更及真实事件副作用，不宣称整个回滚契约已关闭。 |
| AUD-02：缺段按 JsonNull reset | 通过，关闭旧项 | 当前各持久化段保留/清空策略已按契约区分；SaveSystem 定向 49/49 通过，未重报缺段 reset。 |
| AUD-03：vendor timer 读档恢复 | 通过，关闭旧项 | VendorStockPersistable 保存/恢复 timer remaining；Gameplay 定向测试 7/7 通过，未重报 timer。 |
| AUD-04：公开 API 改名无旧别名 | 通过，关闭旧项 | GameObjectHost 已补回 PendingChestLootSnapshot / RestorePendingChestLoot 的 Obsolete 转发别名；GobjPendingLootPersistenceTests 定向 8/8 通过，未重报公开 API 编译破坏。 |
| AUD-02 后续：种族 aura 跨图恢复 | 原旧场景通过，关闭旧项 | RacePassiveAuraCrossMapTests 在本轮 Gameplay 定向测试中通过；新增 CORE-170-01 只针对“种族与装备共享同一 auraDef”的跨来源配置，不是旧项重复。 |
| 装备/set 共享源、same-map spawn timer | 通过，关闭旧项 | Carriers 定向 32/32、Gameplay 定向 7/7；这些测试不能覆盖种族来源账本或失败段自身回滚。 |

## 入口、注册顺序与状态同一性检查

GameplayAssembly.RegisterPersistables 在 [GameplayAssembly.cs:1194-1249](../../../../core/gameplay/assembly/GameplayAssembly.cs:1194) 注册 progression、unit/map/position、inventory、equipment、skills、economy、achievement、spawn、loot、difficulty、vitals、turn、RNG；SaveSystem 读档时按其 KnownOrder 处理并对成功段逆序回滚。该机制能覆盖“此前成功段”，不能覆盖 CORE-170-03 的失败段自身。

生产新游戏入口先创建 PlayerUnit，再注册 Rules/Economy，之后构造 Presentation；这样注册顺序能避免 UI 构造期查询未注册 unit，但没有把 PlayerLevel 写入实体，形成 CORE-170-02。读档后的模板入口最终调用 [GameBootstrap.cs:449-455](../../../../games/_template/Runtime/GameBootstrap.cs:449) 的 Gameplay.EnterMap，Unity Shell 入口调用 [FrameworkResidentHost.cs:712-717](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Shell/FrameworkResidentHost.cs:712) 的同一方法；因此 CORE-170-01 会稳定落在“实体重新加入世界后进图重放”的生产生命周期。

## 验证边界与日志

本轮只运行定向 .NET 验证，没有运行全量 dotnet test，没有运行 Unity。探针退出码 0 只表示诊断程序完成运行，不表示产品行为符合 EXPECTED；缺陷结论以日志中 EXPECTED/ACTUAL 的真实差异为依据。结果及退出码均保存在审计仓：

- 探针：[core-persistence-probe.raw.log](repro/core-persistence-probe.raw.log)，[core-persistence-probe.exit.txt](repro/core-persistence-probe.exit.txt)，退出码 0。
- Foundation SaveSystem：[foundation-save.log](repro/foundation-save.log)，[foundation-save.exit.txt](repro/foundation-save.exit.txt)，49/49，退出码 0。
- Gameplay 旧跨图/计时定向集：[gameplay-targeted.log](repro/gameplay-targeted.log)，[gameplay-targeted.exit.txt](repro/gameplay-targeted.exit.txt)，7/7，退出码 0。
- Carriers 装备/共享源定向集：[carriers-targeted.log](repro/carriers-targeted.log)，[carriers-targeted.exit.txt](repro/carriers-targeted.exit.txt)，32/32，退出码 0。
- Gobj 旧 API 兼容定向集：[gobj-compat-targeted.log](repro/gobj-compat-targeted.log)，[gobj-compat-targeted.exit.txt](repro/gobj-compat-targeted.exit.txt)，8/8，退出码 0。

