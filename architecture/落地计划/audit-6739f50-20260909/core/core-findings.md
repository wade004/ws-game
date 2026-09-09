# v1.11.0 Core 定向复核报告

## 基线、范围与证据边界

- 冻结工作树：`D:\workespace\ws-game-review-6739f50`，HEAD `6739f50e44ba39a023c6209af2673aaf6a1c1fdc`，tag `v1.11.0`；原仓 `D:\workespace\ws-game` 未写入。
- 本目录只保存独立探针、日志与本报告；未修改产品源码或既有测试，未提交。
- 本独立 core 探针未执行全量 check；整体 check 结果由同批次 validation 记录负责。
- 证据采用本轮 `baseline=6739f50 version=1.11.0` 日志中的实际字段与当前版本 correctness oracle；归档 1.10 探针的旧 `EXPECTED`/`INTERPRETATION` 不作为本轮结论。Console 探针退出码为0只表示程序执行完成，正确性由实际字段与独立 oracle 判断。
- `FollowupCoreProbe` 使用真实 `GameplayAssembly`、`WorldSim`、`RulesAssembly`、`SaveSystem`、`EventBus`、`EquipmentHost`、`DisplayInfoRegistry` 与 `EquipmentWeaponStyleSource`；`TeleportLoadingBoundaryProbe` 另使用真实 `GameplayAssembly`、公共 Dialog、`SceneRouter`、`StubResourceLoader` 与生产形状的 post-load reattach；工程引用均为相对冻结仓路径。

## 结论总览

| 编号 | 建议定级 | 当前结论 | 实际证据 |
|---|---|---|---|
| CORE-180-01/02/03 | 已修复，复核通过 | 成功 Load 的 rating/max/health、正向回滚装备、同图 race 重建均正确。 | `FollowupCoreProbe` 对应场景均符合当前 oracle。 |
| CORE-110-01 | 已修复，复核通过 | 失败回滚 race/aura 与 equipment/max/health 派生状态已恢复。 | 两个 `PersistableThrew` 场景实际值均回到 A oracle。 |
| CORE-110-02 | 已修复，复核通过 | 跨职业旧 base 键与 power type 残留已清理。 | B oracle 与同图 Load 后 `stat/legacy/mana` 一致。 |
| CORE-110-03 | 已修复，复核通过 | 同 tick 多条目标 move 只消费一次固定 `dt`；Stop 顺序语义保持正确。 | 1/2/3 条请求实际 x 均为 1；Stop 前实际 x=0，Stop 后两条实际 x=1。 |
| CORE-111-01 | P2，主审已确认 | 不同 power 集合读档失败后，资源类型集合恢复，但资源当前值与 `InCombat` 运行态没有快照恢复。 | A 的 Mana=30、战斗内；失败回滚后 Mana=0，`Advance(1)` 后变为10，应保持30。 |
| UI-111-01 | P2，主审已确认 | 同图成功读档后，真实 InventoryViewModel 保留旧背包/装备快照；即时 UiDataSource 已是新档。 | `INVENTORY-VM-SAME-MAP-LOAD`：B 真实存档加载成功后 direct entries 为两件 B 文档物品、B 武器实例，VM 仍为 A 的一件物品、A 武器实例；手动 Refresh 后才变 B。 |
| TP-111-01 | P2，主审已确认 | 公共 Gossip 入口可在首个跨图请求 Loading 时接受第二个请求；第二次失败被吞掉但已写玩家字段，最终场景与玩家 MapId/位置分叉。 | `TELEPORT-GOSSIP-CONSECUTIVE-WHILE-LOADING`：首 B 请求进入 Loading，第二 A 请求 `ChooseOption=True`，完成 B 后 `final_scene=B` 而 `final_map=A`, `final_position=1,2`。 |

## 1.11 修复静态核对

- `SaveSystem.Load` 在 [core/foundation/save_system/core/SaveSystem.cs:379-388](../../../../core/foundation/save_system/core/SaveSystem.cs:379) 将逐段 Load 与回滚置于 `SuppressDispatch`；成功段派生钩子在 [409-424](../../../../core/foundation/save_system/core/SaveSystem.cs:409)，失败段含失败段自身的回滚列表在 [427-476](../../../../core/foundation/save_system/core/SaveSystem.cs:427)。
- 回滚后派生重建已在 [core/foundation/save_system/core/SaveSystem.cs:817-865](../../../../core/foundation/save_system/core/SaveSystem.cs:817) 对每个成功回滚段调用 `OnSectionLoaded`，覆盖 1.10 的 race/max/health 残留。
- `PlayerVitalsPersistable` 当前只序列化 Health（[core/gameplay/assembly/PlayerVitalsPersistable.cs:72-133](../../../../core/gameplay/assembly/PlayerVitalsPersistable.cs:72)）；`PowerHost.RegisterUnit` 新建 `UnitState`（含 `InCombat`）并以定义的 start_full 初始化资源（[core/numbers/power_set/core/PowerHost.cs:27-84](../../../../core/numbers/power_set/core/PowerHost.cs:27)），`SetInCombat` 是独立运行态写入（[core/numbers/power_set/core/PowerHost.cs:143-149](../../../../core/numbers/power_set/core/PowerHost.cs:143)）。
- `GameplayAssembly` 生产注入调用位于 [core/gameplay/assembly/GameplayAssembly.cs:1261](../../../../core/gameplay/assembly/GameplayAssembly.cs:1261)，派生重建器实际实现位于 [1292-1309](../../../../core/gameplay/assembly/GameplayAssembly.cs:1292) 与 [1334-1355](../../../../core/gameplay/assembly/GameplayAssembly.cs:1334)。
- `RulesAssembly.ReloadArchetypeAndRace` 当前在 [core/rules/assembly/RulesAssembly.cs:665-731](../../../../core/rules/assembly/RulesAssembly.cs:665) 处理旧 race、旧 class base 与 power 集合重建，其中 power 集合替换实际为 [725-729](../../../../core/rules/assembly/RulesAssembly.cs:725)；`PowerHost` 的注册/注销契约在 [core/numbers/power_set/contracts/IPowerHost.cs:25-30](../../../../core/numbers/power_set/contracts/IPowerHost.cs:25)。
- `EquipmentWeaponStyleSource` 的装备事件订阅、缓存与失效逻辑在 [presentation/vfx_sfx/core/EquipmentWeaponStyleSource.cs:38-81](../../../../presentation/vfx_sfx/core/EquipmentWeaponStyleSource.cs:38)；1.11 额外成功 Load 清缓存的接线需结合后续实际值核对。
- 表现层生产装配在 [presentation/assembly/PresentationAssembly.cs:453-465](../../../../presentation/assembly/PresentationAssembly.cs:453)，真实 UI 路由由 `PlayerPathProvider` 的即时 `ListItems`/`GetEquipped` 查询实现（[presentation/ui/core/PathProviders/PlayerPathProvider.cs:110-161](../../../../presentation/ui/core/PathProviders/PlayerPathProvider.cs:110)）。`InventoryViewModel` 仅在 [presentation/ui/core/ViewModels/InventoryViewModel.cs:45-60](../../../../presentation/ui/core/ViewModels/InventoryViewModel.cs:45) 订阅四类业务 item 事件并刷新；`SaveLoaded` 虽在 [core/foundation/save_system/core/SaveSystem.cs:486](../../../../core/foundation/save_system/core/SaveSystem.cs:486) 发布，但该 VM 未订阅。
- `GameplayAssembly.ApplyResolvedTeleport` 在 [core/gameplay/assembly/GameplayAssembly.cs:1419-1462](../../../../core/gameplay/assembly/GameplayAssembly.cs:1419) 先写 `entity.MapId`/位置再调用 `SceneRouter.LoadScene`，并吞掉 Loading 时的 `InvalidOperationException`；`SceneRouter.LoadScene` 在 [core/foundation/scene_router/core/SceneRouter.cs:122-128](../../../../core/foundation/scene_router/core/SceneRouter.cs:122) 明确拒绝 Loading 中的第二次请求。公共 `DialogHost.ChooseOption` 执行动作的路径在 [core/gameplay/dialog/core/DialogHost.cs:132-170](../../../../core/gameplay/dialog/core/DialogHost.cs:132)。

## 真实复核结果

`FollowupCoreProbe` 当前日志 [followup-core-probe.log](logs/followup-core-probe.log)，退出码为 0：

```text
ROLLBACK-DERIVED-STATE-RACE
load_status=PersistableThrew;after_race=arch.race.followup_a;stat=61;auraA=True;auraB=False

ROLLBACK-DERIVED-STATE-KNOWN-SKILLS-BEFORE-VITALS
load_status=PersistableThrew;after_level=2;equipped_after=True;max_after=200;health_after=200

SAME-MAP-CLASS-SAVE-LOAD
load_status=Loaded;after_class=arch.class.followup_b;after_stat=20;after_legacy=0;after_mana=False

WEAPON-STYLE-CACHE-SAME-MAP-LOAD
load_status=Loaded;equipped_template_after=item.weapon.followup_b;style_after_before_dispatch=display.weapon_style.followup_b;style_after=display.weapon_style.followup_b;oracle_b_style=display.weapon_style.followup_b
```

`MovementBoundaryProbe` 当前日志 [movement-boundary-probe.log](logs/movement-boundary-probe.log)，退出码为 0：

```text
REPEATED-MOVE count=1;actual_x=1
REPEATED-MOVE count=2;actual_x=1
REPEATED-MOVE count=3;actual_x=1
STOP-AFTER-TWO-MOVES actual_x=0;stopped_callbacks=1;path_active=False;mode=Idle
TWO-MOVES-AFTER-STOP actual_x=1;stopped_callbacks=0;path_active=True;mode=Run
```

## CORE-111-01：失败回滚后 power 当前值与战斗状态丢失

合法 A 存档使用 Class A、Race A；A 的 Mana 资源设置为 `start_full=false`、脱战回复10，先真实设置 `InCombat=true`，再用运行时 `Modify` 将 Mana 设为30后保存。合法 B 存档使用相同 Race A、不同 Class B，B 不声明 Mana。A 注册后段故障，加载 B 使 Class B 先成功写入并移除 Mana，后段失败后进入真实正向回滚。

```text
ROLLBACK-CLASS-POWER-CURRENT-AND-COMBAT-STATE
save_a_initial=True;save_a_with_failure=True;save_b_valid=True
race_before=arch.race.followup_a;race_after=arch.race.followup_a
class_after=arch.class.followup_a;mana_before=30;load_status=PersistableThrew
mana_present_after=True;mana_after_rollback=0;mana_after_advance_1=10
```

当前 1.11 回滚已恢复 Class A 的资源集合，但 `PlayerVitalsPersistable` 只保存 Health，`PowerHost.RegisterUnit` 重建资源时将 Mana 初始化为0并将 `InCombat` 重置为 false；随后 `Advance(1)` 的脱战回复把 Mana 推到10。这里要求保留的是失败加载前的运行时快照（A 在加载前 Mana=30、`InCombat=true`），不是把 `InCombat` 当作正式存档字段；实际当前值和战斗 flag 均未恢复。该项由主审确认 P2。修复需要为全部可持久化 power 当前值及失败前 `InCombat` 运行态建立明确快照/恢复路径，不能只依赖 class/power 集合重建。

## UI-111-01：InventoryViewModel 同图读档缓存不刷新

探针构造真实 `PlayerPathProvider`、`UiDataSource`、`InventoryViewModel`，先建立 A：背包一件 `item.followup_level2:1`、主手 `item.weapon.followup_a`，实例化 VM 使其缓存 A。再由独立 Gameplay 实例建立合法 B：背包两件 `item.followup_level2:1`、主手 `item.weapon.followup_b`，`Save.Save` 返回成功；把 B 文档放进 A 的内存文件系统后调用真实 `Gameplay.RestoreFromSlot(B)` 并 `EventBus.DispatchPending()`。

实际日志如下（`direct_*` 经 UiDataSource 即时 Query，`vm_*` 是未经手动刷新时的 VM 快照）：

```text
INVENTORY-VM-SAME-MAP-LOAD
a_inventory_count=1;...;a_equipped_instance=item.inst_2;...;a_vm_equipped_instance=item.inst_2
b_entries_before_save=item.followup_level2:1,item.followup_level2:1;save_b_valid=True;load_status=Loaded
direct_count_after=2;direct_entries_after=item.followup_level2:1,item.followup_level2:1
direct_equipped_instance_after=item.inst_3;direct_equipped_template_after=item.weapon.followup_b
vm_entries_after=item.followup_level2:1;vm_equipped_instance_after=item.inst_2
vm_after_manual_refresh_entries=item.followup_level2:1,item.followup_level2:1;vm_after_manual_refresh_equipped_instance=item.inst_3
```

本轮 probe 随后记录的 correctness oracle 要求 VM 在成功读档后等于 live B direct query；实际 bug signature 是 `vm_entries_after=item.followup_level2:1;vm_equipped_instance_after=item.inst_2`，而 live B 是两件物品、实例 `item.inst_3`、模板 `item.weapon.followup_b`。这确认 P2：成功读档的真实装备/背包状态已经改变，但 VM 没有收到刷新事件，继续向 UI 消费 A 快照；手动 `Refresh` 后才与即时查询一致。结论限于该 VM 缓存契约与同图成功读档，未宣称 Unity 画面通过。

## TP-111-01：Loading 中重复 Gossip 传送造成场景/玩家字段分叉

探针使用合法 `world.map` A/B、真实 `GameplayAssembly`、真实 `DialogHost.OpenGossip/ChooseOption`、真实 `SceneRouter`、`StubResourceLoader.DeferCallbacks=true` 与生产形状的 post-load hook（`WorldSim` 被 ClearAll 后重新 AddEntity，再调用 `Gameplay.EnterMap`）。A 首次加载完成后打开 B gossip 并选择：`state_after_first=Loading`、`map_after_first=B`、B 资源仍 pending。随后在不调用 Router.Update 的情况下打开 A gossip 并选择，公共入口返回 `second_chosen=True`、玩家先被写成 A/出生点。完成首 B 资源并 Update 后：

```text
TELEPORT-GOSSIP-CONSECUTIVE-WHILE-LOADING
initial_scene=world.teleport_loading_a;first_chosen=True;state_after_first=Loading
scene_after_first=world.teleport_loading_a;map_after_first=world.teleport_loading_b;first_pending_B=True
second_chosen=True;state_after_second=Loading;map_after_second=world.teleport_loading_a;position_after_second=1,2
final_scene=world.teleport_loading_b;final_map=world.teleport_loading_a;final_position=1,2;world_player_present=True;world_player_map=world.teleport_loading_a;world_player_position=1,2
```

这是 P2：`SceneRouter` 正确拒绝 Loading 中的第二次 `LoadScene`，但 `ApplyResolvedTeleport` 在调用前已写玩家 `MapId`/位置，异常只被吞掉；首请求完成后生产形状 post-load 虽重新把玩家实体加入世界，仍没有把玩家字段纠正回场景 B，留下 `Router.CurrentScene=B` 与 `WorldSim` 中玩家 `MapId=A` 的分叉。合法 oracle 是场景与玩家 map/位置保持一致；第二请求可以拒绝、排队或替换，只要保持该不变量。当前实现未满足任何一种一致结果。探针的 `ORACLE-CURRENT` 已明确记录这一不变量，错误状态只取上面的 actual。建议在路由接受前不提交玩家字段，或在 Loading 拒绝时回滚/排队。该证据来自公共 Dialog 入口，不涉及反射或 Unity 画面。

## 运行命令与产物

```powershell
dotnet run --project "D:\workespace\ws-game-review-6739f50\architecture\落地计划\audit-6739f50-20260909\core\repro\FollowupCoreProbe.csproj"
dotnet run --project "D:\workespace\ws-game-review-6739f50\architecture\落地计划\audit-6739f50-20260909\core\repro\MovementBoundaryProbe.csproj"
dotnet run --project "D:\workespace\ws-game-review-6739f50\architecture\落地计划\audit-6739f50-20260909\core\repro\TeleportLoadingBoundaryProbe.csproj"
```

- [FollowupCoreProbe.cs](repro/FollowupCoreProbe.cs)
- [FollowupCoreProbe.csproj](repro/FollowupCoreProbe.csproj)
- [followup-core-probe.log](logs/followup-core-probe.log)
- [followup-core-probe.exit.txt](logs/followup-core-probe.exit.txt)
- [MovementBoundaryProbe.cs](repro/MovementBoundaryProbe.cs)
- [MovementBoundaryProbe.csproj](repro/MovementBoundaryProbe.csproj)
- [movement-boundary-probe.log](logs/movement-boundary-probe.log)
- [movement-boundary-probe.exit.txt](logs/movement-boundary-probe.exit.txt)
- [TeleportLoadingBoundaryProbe.cs](repro/TeleportLoadingBoundaryProbe.cs)
- [TeleportLoadingBoundaryProbe.csproj](repro/TeleportLoadingBoundaryProbe.csproj)
- [teleport-loading-boundary.log](logs/teleport-loading-boundary.log)
- [teleport-loading-boundary.exit.txt](logs/teleport-loading-boundary.exit.txt)
