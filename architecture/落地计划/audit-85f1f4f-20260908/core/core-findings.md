# Core 1.6.0 有界审计结果

基线：`85f1f4fbaff7aa3f01292f1b4b469a6a48bcc570`，版本 `1.6.0`。探针工程在 `core/repro/CorePersistenceProbe`，只链接当前产品项目和既有测试夹具，未修改产品代码或既有测试。

## 已确认：1.5 非空 `gobj_pending_loot` 无法被 1.6 读取（建议 P1）

历史 1.5.0 serializer 源码字段是 `gobjInstanceId`，当前 1.6 写入 `originKey`，读取器在 [GobjPendingLootPersistable.cs:96](../../../../core/carriers/gobj/core/GobjPendingLootPersistable.cs:96) 直接索引 `entryObj["originKey"]`。非空旧段因此抛缺键异常，`SaveSystem.Load` 返回 `PersistableThrew`。当前写入点见 [同文件:73](../../../../core/carriers/gobj/core/GobjPendingLootPersistable.cs:73)；历史字段可用 `git show 3224ca1:core/carriers/gobj/core/GobjPendingLootPersistable.cs` 检查，输出含 `.Add("gobjInstanceId", ...)`。

触发条件是 1.5 存档的 `world.gobj_pending_loot.pending_loot` 至少有一条记录。这里是依据 1.5 serializer 源码重建的等价 JSON 形状探针，不是从真实发布用户文件读取的原始旧档；探针使用合法旧实例形状 `gobj.inst_1`，并在同一信封中加入早于 gobj 段的 `player.inventory` 快照（运行期 1 件，快照 2 件）。历史检查完整命令为 `git show 3224ca1:core/carriers/gobj/core/GobjPendingLootPersistable.cs | rg -n "gobjInstanceId|originKey|pending_loot"`，原始输出保存在 [`legacy-1.5-gobj-fields.log`](repro/legacy-1.5-gobj-fields.log)，其中写入点为 `63:.Add("gobjInstanceId", ...)`、读取点为 `86:entryObj["gobjInstanceId"]`。最终探针日志为 [`core-persistence-probe-final.log`](repro/core-persistence-probe-final.log)。原始关键输出：`LEGACY-GOBJ-PENDING / on_disk_entry_key=gobjInstanceId / registered_reader=originKey / before_pending_entries=1 / load_status=PersistableThrew / after_pending_entries=1 / after_inventory_count=2 / EXPECTED=load_status=Loaded;after_inventory_count=2;pending restored / ACTUAL=load_status=PersistableThrew;after_inventory_count=2;pending_entries=1`。

预期是 `load_status=Loaded` 且 pending 按当前稳定键恢复；实际读档失败，但 inventory 已先按顺序恢复为 2，证明 SaveSystem 在异常段前已部分提交、不会回滚此前成功段。`gobjInstanceId` 到稳定摆放键并非总能无损映射；建议按 CHANGELOG 承诺对不可判定的旧格式段安全丢弃，或仅在存在明确映射时迁移，并补断言“旧段失败时前段提交、状态策略明确”的真实 `SaveSystem.Load` 回归测试。

## 已确认：缺 inventory/vendor 段未按合同清空，保留读档前状态（建议 P2）

`KeepStateWhenSectionMissing` 默认 `false`（[IPersistable.cs:65](../../../../core/foundation/save_system/contracts/IPersistable.cs:65)），SaveSystem 对缺段传 `JsonNull`（[SaveSystem.cs:347](../../../../core/foundation/save_system/core/SaveSystem.cs:347)），但 [ItemPersistable.cs:41](../../../../core/carriers/item/core/ItemPersistable.cs:41) 的 inventory `Load` 收到 `JsonNull` 直接返回。

inventory：预期是只含 meta 的旧档使 inventory 为空；实际探针先放入 1 件物品，再加载无 inventory 段的存档，返回 `Loaded` 但 `after_inventory_count=1`（完整原始输出在 `core-persistence-probe-final.log`：`registry_blocking=False / persistable_keep_state_default=False / before_inventory_count=1 / load_status=Loaded / after_inventory_count=1 / EXPECTED=...0 / ACTUAL=...1`）。

[VendorStockPersistable.cs:67](../../../../core/gameplay/economy/core/VendorStockPersistable.cs:67) 对 `JsonNull` 直接返回，且默认 `KeepStateWhenSectionMissing=false`。探针以内容定义初始库存 5，先将运行期库存改为 2，再加载只含 meta 的存档；原始关键输出：`MISSING-VENDOR-SECTION / registry_blocking=False / persistable_keep_state_default=False / before_vendor_remaining=2 / load_status=Loaded / after_vendor_remaining=2 / EXPECTED=...5 / ACTUAL=...2`。

两段属于同一缺段合同落差：缺段应视为空数据并清空到默认态，实际会造成跨槽旧状态残留。建议各实现的 `JsonNull` 分支清空/重建状态，或显式声明保留语义；当前实现与 architecture/10 缺段合同冲突。

## 静态候选：种族被动光环跨图可能丢失（未复现，建议 P2）

[RulesAssembly.cs:393](../../../../core/rules/assembly/RulesAssembly.cs:393) 将 `raceId` 只传给 [ArchetypeRegistry.cs:90](../../../../core/numbers/archetype/core/ArchetypeRegistry.cs:90)；`ApplyTo` 在 [同文件:112](../../../../core/numbers/archetype/core/ArchetypeRegistry.cs:112) 施加 `race.PassiveAuras`。AuraHost 在 [AuraHost.cs:136](../../../../core/rules/skill/core/AuraHost.cs:136) 响应 `entity.destroyed` 清理运行期光环。`GameplayAssembly.EnterMap` [GameplayAssembly.cs:836](../../../../core/gameplay/assembly/GameplayAssembly.cs:836) 目前只重放装备 grants，生产搜索未见 post-load 以已选 raceId 再次 `RegisterUnit`；`UnitPersistable` [UnitPersistable.cs:37](../../../../core/carriers/unit/core/UnitPersistable.cs:37) 也说明不持有独立 race 引用。

这构成 `ClearAll → 玩家实体重建 → EnterMap` 后种族被动消失的静态链路，但当前样例多传 `raceId:null`，本次没有 Runtime 探针，故不作为已确认问题。需要真实 `arch.race.passive_auras` 内容和 AuraHost/派生属性前后值验证。

## 已确认：同一宿主 vendor 读档继承旧补货计时器（建议 P2）

`VendorStockPersistable` 只保存剩余量（[VendorStockPersistable.cs:48](../../../../core/gameplay/economy/core/VendorStockPersistable.cs:48)），[EconomyHost.cs:424](../../../../core/gameplay/economy/core/EconomyHost.cs:424) 的 `SetStock` 只改 `Remaining`，不重置或恢复 `TimerRemaining`。在真实探针中，10 秒周期于 t=2 存档（快照剩余 8 秒），随后推进 7 秒再读档；读档后再推进 1 秒立即补满为 5。若按快照恢复或按代码注释所称“读档后从满值起算”，此时都应保持 2。

原始输出：`VENDOR-TIMER-SAME-HOST-LOAD / save_status=True / snapshot_timer_remaining=8 (not serialized) / load_status=Loaded / after_load_remaining=2 / after_one_second_remaining=5 / EXPECTED=after_one_second_remaining=2 (timer restored or reset to full) / ACTUAL=after_one_second_remaining=5 (stale pre-load timer restocked immediately)`。建议持久化并恢复 `TimerRemaining`，或让 `SetStock` 在 Load 时显式重置为定义的完整计时；同时修订 architecture/10 与实现注释的取舍。

## 边界、已排除与限制

已核对 gobj pending 的跨实体重建路径：稳定 origin key 的 pending 交付会给新实例设置 `open_state`/`used_at`，本轮未发现该重建路径的新异常。`open_state`/`used_at` 按实例 id 的重置符合 architecture/07 对实体重建和 spawn policy 的说明；没有把 on-map-enter 新 spawn 的预期新交互误报成读档缺陷。

本次未运行全量 check、Unity/引擎、性能或线上迁移，仅运行独立 .NET probe。原始日志：[core-persistence-probe.log](repro/core-persistence-probe.log)，最终日志：[core-persistence-probe-final.log](repro/core-persistence-probe-final.log)，最终退出码：[core-persistence-probe-final.exit.txt](repro/core-persistence-probe-final.exit.txt)（`0`）。缺段同类静态项还包括 Progression、SkillBinding、PlayerVitals、UnitPersistable 的若干 `JsonNull` 直接返回分支，未把它们冒充为独立 Runtime 确认。

## 原生基线与新增缺陷断言

原生 1.6 基线的稳定 pending 重建路径本轮未发现新异常；新增缺陷断言来自同一当前基线上的真实入口：跨版本旧字段读档应成功却抛 `PersistableThrew`，inventory/vendor 缺段应清空却保留旧态，vendor timer 应保持快照语义却立即补货。最终命令为 `dotnet run --project architecture/落地计划/audit-85f1f4f-20260908/core/repro/CorePersistenceProbe/CorePersistenceProbe.csproj | Tee-Object -FilePath architecture/落地计划/audit-85f1f4f-20260908/core/repro/core-persistence-probe-final.log`，进程退出码为 `0`；`EXPECTED`/`ACTUAL` 行已原样保存在最终日志并在各节列出。
