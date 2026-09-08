# v1.8.0 Core 独立复核报告

## 范围与证据边界

- 基线：冻结仓 `D:\workespace\ws-game-review-e070e3f`，HEAD `e070e3f`，版本 `1.8.0`；对照变更范围 `8160178..HEAD`，并读取 `followup-2026-09-08f` 与新增回归测试。
- 本次只写入 `architecture\落地计划\audit-e070e3f-20260908\core`，未修改原仓 `D:\workespace\ws-game`、产品源码、既有测试或提交。
- 证据来自真实 `GameplayAssembly`、`WorldSim`、`SaveSystem`、`EventBus` 组装后的独立探针，日志见 [followup-core-probe.log](logs/followup-core-probe.log) 与种族 A/B 复跑 [followup-core-probe-race-final.log](logs/followup-core-probe-race-final.log)。仅执行必要定向构建与运行，未重复全量 check。
- 旧探针当前基线复跑日志见 [old-probe-current3.log](logs/old-probe-current3.log)。旧探针中的 `INTERPRETATION` 是历史硬编码文字，本报告只采用其 `EXPECTED`/`ACTUAL`，不把旧解释当作当前证据。

## 结论总览

| 编号 | 定级 | 结论 | 真实证据 |
|---|---|---|---|
| CORE-180-01 | P1 | 成功 `Load` 时，`SaveSystem` 的全局抑制会丢弃规则层依赖的内部同步事件。进度等级恢复，但 Rating 缓存不恢复；装备恢复，但 Power max/当前生命不恢复。 | `load_status=Loaded`；Rating `10 -> 5` 后加载仍 `5`，`DispatchPending` 后仍 `5`；装备 `equipped_after=true`，max/health `200 -> 100` 后加载及 drain 后仍 `100`。 |
| CORE-180-02 | P2 | 后段持久化失败的逆序回滚先恢复 Equipment，再恢复 Progression；低等级有效文档无法重新装备原先高等级装备，随后进度等级虽恢复，装备和库存状态仍丢失。 | 初始 level2/装备 true/库存 0；低等级 progression + 空 equipment 文档，后段真实 persistable 抛错；最终 level2、装备 false、库存 0。 |
| CORE-180-03 | P2，已确认 | 两个独立构造的有效角色存档之间，同图 `RestoreFromSlot` 只恢复 race 字段；种族 B 的 stat 修正和 aura 未应用，种族 A 的 stat/aura 仍残留。 | A/B 真实 fixture：B 档 oracle stat=91；A 同图加载 B 返回 `Loaded`，字段 B 但 stat `61`、AuraA=true、AuraB=false。 |
| CORE-180-CAND-01 | 候选，未定级 | `archetype` 字段是否也需要同样的运行时重应用，当前只有静态路径证据；本次真实确认范围限于 race。 | `UnitPersistable.ArchetypeId` 与同图恢复路径静态检查，未做 A/B archetype fixture。 |

## 已确认缺陷

### CORE-180-01：成功加载吞掉内部同步事件

`EventBus.Enqueue` 在 [core/foundation/event_bus/core/EventBus.cs:83-92](../../../../core/foundation/event_bus/core/EventBus.cs:83) 发现抑制深度后直接返回，`PublishImmediate` 在同文件 [127-142](../../../../core/foundation/event_bus/core/EventBus.cs:127) 也直接返回；契约 [core/foundation/event_bus/contracts/IEventBus.cs:60-78](../../../../core/foundation/event_bus/contracts/IEventBus.cs:60) 明确这是所有事件的丢弃语义。`SaveSystem.Load` 在 [core/foundation/save_system/core/SaveSystem.cs:356-365](../../../../core/foundation/save_system/core/SaveSystem.cs:356) 将整个加载和回滚包在 `SuppressDispatch()` 内，成功退出抑制后才在 [367-423](../../../../core/foundation/save_system/core/SaveSystem.cs:367) 发布 Save 结果事件。

规则层对这些事件有真实生产消费者：[core/rules/assembly/RulesAssembly.cs:261-269](../../../../core/rules/assembly/RulesAssembly.cs:261) 用 `StatChanged` 重算 Power max，用 `LevelUp`/`ProgressionRestored` 重算 Rating。进度恢复确实在 [core/numbers/progression/core/ProgressionHost.cs:311-326](../../../../core/numbers/progression/core/ProgressionHost.cs:311) 发布 `ProgressionRestoredEvent`，而生命恢复 [core/gameplay/assembly/PlayerVitalsPersistable.cs:129-134](../../../../core/gameplay/assembly/PlayerVitalsPersistable.cs:129) 只按保存值与当前 max 的差额调用 `ModifyPower`，自身不重算 max。

独立探针 `SUCCESSFUL-LOAD-INTERNAL-EVENTS` 的实际输出：

```text
save_success=True;load_status=Loaded
before_save_level=10;rating_at_10=10
mutated_level=1;rating_at_1=5
after_load_level=10;entity_level=10;rating_after_load_before_dispatch=5;rating_after_dispatch=5
EXPECTED=load_status=Loaded;after_load_level=10;rating_after_load=10
```

这不是失败回滚，也不是只观察队列未 drain：`Load` 已返回 `Loaded`，随后显式 `DispatchPending`，Rating 仍为 5；实体等级已为 10，说明丢失的是规则缓存同步。

装备/max 探针同样走成功加载：

```text
equip_success=True;save_success=True;load_status=Loaded
saved_max=200;saved_health=200;mutated_max=100
after_load_before_dispatch_max=100;after_load_before_dispatch_health=100
after_load_max=100;after_load_health=100;equipped_after=True
EXPECTED=load_status=Loaded;after_load_max=200;after_load_health=200;equipped_after=true
```

`Equipment.Load` 的 `StatChanged` 在抑制期间被丢弃，故 RulesAssembly 没有执行 `PowerHost.RecomputeMax`；随后 Vitals 只能在旧 max=100 上恢复。当前可确认的归因范围是 v1.8 的成功 Load 抑制影响。没有 v1.7 对照，不能把所有历史 max/health clamp 行为都归为 v1.8 新引入。

建议把内部失效/重算通知与外部业务事件分开处理，或在成功加载后按依赖显式执行规则重算；单纯对整个 Load 丢弃所有事件会继续丢掉上述生产同步。

### CORE-180-02：异常回滚的 Equipment/Progression 依赖顺序

保存段顺序在 [core/foundation/save_system/contracts/SaveSections.cs:135-158](../../../../core/foundation/save_system/contracts/SaveSections.cs:135) 为 progression、inventory、equipment；回滚在 [core/foundation/save_system/core/SaveSystem.cs:734-755](../../../../core/foundation/save_system/core/SaveSystem.cs:734) 逆序调用各段 `Load`。装备恢复实现 [core/carriers/item/core/ItemPersistable.cs:169-187](../../../../core/carriers/item/core/ItemPersistable.cs:169) 在当前等级不满足需求时无法 `Equip`，并将失败装备留在背包；该探针的低等级文档明确使用空 equipment，避免把输入不一致误判为框架错误。

`ROLLBACK-EQUIPMENT-BEFORE-PROGRESSION` 实际输出：

```text
initial_equip=True;baseline_inventory_count=0;save_success=True;load_status=PersistableThrew
after_level=2;entity_level=2;equipped_after=False;inventory_count_after=0
EXPECTED=load_status=PersistableThrew;after_level=2;entity_level=2;equipped_after=true;inventory_count_after=0
```

探针使用真实 progression/inventory/equipment/vitals 注册和真实 EventBus，并只在后段加入抛错 persistable。失败后等级与实体等级都回到 2，但装备没有恢复到 true，且库存仍为 0；这证明恢复过程丢失了原 live 装备，而不是仅仅因为最终等级错误。该问题位于异常恢复路径，建议按依赖恢复 progression 后再恢复 equipment，或先构造完整快照再一次性提交。

## 候选与排除

### CORE-180-03：同图成功加载只改种族字段，修正状态跨存档残留（P2，已确认）

静态检查显示 [core/carriers/unit/core/UnitPersistable.cs:106-134](../../../../core/carriers/unit/core/UnitPersistable.cs:106) 和 [137-169](../../../../core/carriers/unit/core/UnitPersistable.cs:137) 的 Load 只写 `PlayerUnit.RaceId`/`ArchetypeId` 字段。进入地图时 [core/gameplay/assembly/GameplayAssembly.cs:884-896](../../../../core/gameplay/assembly/GameplayAssembly.cs:884) 会重应用种族被动；但同图 `RestoreFromSlot` [932-947](../../../../core/gameplay/assembly/GameplayAssembly.cs:932) 在 SaveSystem.Load 后只有场景路由，未见 `ArchetypeRegistry.ApplyTo` 或旧修正清理调用。

真实探针分别构造 race A（base 1、stat +10、AuraA +50）和 race B（base 1、stat +20、AuraB +70）的两个有效角色存档，把 B 存档复制到 A 的内存文件系统，再在 A 上同图调用 `GameplayAssembly.RestoreFromSlot`。结果如下：

```text
save_a_valid=True;save_b_valid=True;race_b_oracle_stat=91;load_status=Loaded;map=world.followup_core
before_load_race=arch.race.followup_a;stat=61;auraA=True;auraB=False
after_load_race=arch.race.followup_b;stat=61;auraA=True;auraB=False
EXPECTED=save_a_valid=True;save_b_valid=True;race_b_oracle_stat=91;load_status=Loaded;after_load_race=arch.race.followup_b;stat=91;auraA=False;auraB=True
```

因此已确认“两个有效角色存档之间，字段变 B 而旧 stat/aura 保留”的运行时事实，定为 P2。B 档独立构造时的 stat=91 作为 oracle，排除了错误期望值。该探针同时覆盖 `RestoreFromSlot` 与其同图内部的 `SaveSystem.Load` 路径。

### CORE-180-CAND-01：archetype 字段与修正状态可能跨存档残留（候选，未确认）

静态检查显示 [core/carriers/unit/core/UnitPersistable.cs:137-169](../../../../core/carriers/unit/core/UnitPersistable.cs:137) 的 `ArchetypeId` Load 只写字段；同图恢复 [core/gameplay/assembly/GameplayAssembly.cs:932-947](../../../../core/gameplay/assembly/GameplayAssembly.cs:932) 未见 `ArchetypeRegistry.ApplyTo` 或旧修正清理调用。本次没有构造 archetype A/B 的独立有效存档，故只保留为候选，不把 race 的运行时结论外推到 archetype。

以下项目本轮排除为当前缺陷：事件抑制本身的嵌套深度和退出后 SaveLoaded/SaveMigrated 发布；旧三类问题的已修行为；以及没有真实业务消费者证据的事件副作用猜测。

## 上轮三类修复的复核结果

- 共享 aura 账本与来源移除：`core/rules/common/contracts/AuraHandleLedger.cs:52-114`，由 `RulesAssembly` 在 `core/rules/assembly/RulesAssembly.cs:75-104,281-295,349-354` 持有并注入；旧探针当前输出 `RACE-EQUIPMENT-SHARED-AURA` 的 `ACTUAL` 与 `EXPECTED` 一致，装备卸载后共享 aura 仍存在。
- 装备失败段原子性：`core/carriers/item/core/ItemPersistable.cs:126-188` 先解析再提交；旧探针 `FAILED-EQUIPMENT-SEGMENT` 的 `ACTUAL` 与 `EXPECTED` 一致，失败段后原装备状态保持，drain 后也一致。
- 回滚 derived state 与等级同步：`core/foundation/save_system/core/SaveSystem.cs:394-408` 将失败段纳入回滚，`core/numbers/progression/core/ProgressionHost.cs:178-193,253-262,311-326` 在注册/加经验/恢复时同步实体等级；旧探针 `SAVE-ROLLBACK-DERIVED-STATE`、`UNIT-PROGRESSION-LEVEL-DIVERGENCE` 的 `ACTUAL` 均与 `EXPECTED` 一致。CORE-180-02 是在该等级同步修复后暴露出的更深层恢复顺序问题，不否定上述修复。

## 可复核产物

- 独立探针工程：[FollowupCoreProbe.csproj](repro/FollowupCoreProbe.csproj)
- 独立探针源码：[FollowupCoreProbe.cs](repro/FollowupCoreProbe.cs)
- 运行日志：[followup-core-probe.log](logs/followup-core-probe.log)
- 运行退出码：[followup-core-probe.exit.txt](logs/followup-core-probe.exit.txt)
- 上轮探针当前基线复跑：[old-probe-current3.log](logs/old-probe-current3.log)
- 种族 A/B 真实复跑：[followup-core-probe-race-final.log](logs/followup-core-probe-race-final.log)
- 种族 A/B 复跑退出码：[followup-core-probe-race-final.exit.txt](logs/followup-core-probe-race-final.exit.txt)
