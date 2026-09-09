# v1.10.0 Core 定向复核报告

## 基线、范围与证据边界

- 冻结工作树：`D:\workespace\ws-game-review-ac3b622`，HEAD `ac3b622`，tag `v1.10.0`；1.9 基线为 `6b4b221`。原仓 `D:\workespace\ws-game` 未写入。
- 只写入本目录的独立探针、日志和报告；未修改产品源码、既有测试或提交。本独立 core 探针未执行全量 check；整体 check 2533 等结果见同批次 validation 记录。
- 本轮独立探针使用真实 `GameplayAssembly`、`WorldSim`、`RulesAssembly`、`SaveSystem`、`EventBus`，工程引用为相对冻结仓路径，运行入口见 [FollowupCoreProbe.csproj](repro/FollowupCoreProbe.csproj)。
- 最终当前基线日志为 [followup-core-probe.log](logs/followup-core-probe.log)，头部为 `baseline=ac3b622 version=1.10.0`，运行退出码为 0。此前复制后首次产生的 `baseline-followup-probe.log` 保留作过程记录，头部/解释仍是历史 `e070e3f` fixture，未作为本报告证据。
- 对继承的 CORE-180-01/02/03 场景，本报告只采用本次 `ac3b622` 运行的实际字段值；旧 fixture 中的 `EXPECTED`/`INTERPRETATION` 不作为新证据。

## 结论总览

| 编号 | 建议定级 | 当前结论 | 实际探针证据 |
|---|---|---|---|
| CORE-180-01 | 已修复，复核通过 | 成功 Load 的 Rating、Power max、Health 均经派生钩子恢复。 | `load_status=Loaded`；Rating=10；max/health=200；drain 后不变。 |
| CORE-180-02 | 已修复，复核通过 | 正向回滚先恢复 Progression 再恢复 Equipment，等级需求装备恢复。空 equipment 读档段场景未造成最终装备丢失。 | `PersistableThrew` 后 level/entity=2、equipped=true、inventory=0。 |
| CORE-180-03 | 已修复，复核通过 | 两个独立有效角色存档同图切换 race 时，字段、stat 修正和 aura 均重建。 | B oracle stat=91；加载后 RaceB、stat=91、AuraA=false、AuraB=true。 |
| CORE-110-01 | P2，失败回滚派生状态残留 | 失败回滚不调用派生重建钩子；race/stat/aura 与 equipment/max/health 两个子场景都能复现。 | `PersistableThrew` 后 race 字段回 A 但 B stat/aura 残留；known_skills 故障后装备回 A 但 max/health=100/100，均应为 A 的值。 |
| CORE-110-02 | P2，同图跨职业旧状态残留 | 同图换职业时，旧职业未声明的新 base stat 键残留，旧职业额外 power type 也不会按新职业集合重建。 | B oracle power=20/legacy=0/mana=false；加载到 A 宿主后 power=20 但 legacy=5、mana=true。 |
| CORE-110-03 | P2 | 同一连续 tick 提交多个目标 move 时，每条意图都会立即推进一次，公开 `MovementHost.Request` 也会重复消费固定 `dt`；意图裁决与每 tick 位移积分未分离。 | speed=10、dt=0.1：1/2/3 条 move 的实际 x=1/2/3；Stop 前 move 被丢弃，Stop 后的两条 move 实际 x=2。 |

## 1.9 修复的静态核对

`SaveSystem` 在 [core/foundation/save_system/core/SaveSystem.cs:36-51](../../../../core/foundation/save_system/core/SaveSystem.cs:36) 保存可选 `IDerivedStateRebuilder`；读档前快照与 `BeforeLoad` 位于 [350-372](../../../../core/foundation/save_system/core/SaveSystem.cs:350)，成功段之后的 `OnSectionLoaded` 位于 [409-420](../../../../core/foundation/save_system/core/SaveSystem.cs:409)。失败段会加入回滚列表并调用 [447-448](../../../../core/foundation/save_system/core/SaveSystem.cs:447)，回滚实现 [792-818](../../../../core/foundation/save_system/core/SaveSystem.cs:792) 按正向顺序遍历。

`GameplayAssembly.RegisterPersistables` 在 [core/gameplay/assembly/GameplayAssembly.cs:1257-1261](../../../../core/gameplay/assembly/GameplayAssembly.cs:1257) 注入生产重建器；`DerivedStateRebuilder.BeforeLoad`/`OnSectionLoaded` 在 [1292-1320](../../../../core/gameplay/assembly/GameplayAssembly.cs:1292)。它在 `player.race_id` 段重建职业/种族，在 `player.equipment` 段重算 Rating 和 Power max。`RulesAssembly.ReloadArchetypeAndRace` 在 [core/rules/assembly/RulesAssembly.cs:620-667](../../../../core/rules/assembly/RulesAssembly.cs:620) 移除旧 race 来源、设置新 class base、登记新 race modifier/aura，但 [622](../../../../core/rules/assembly/RulesAssembly.cs:622) 明确忽略 `previousClassId`，且没有处理旧 class base 键或 power type。`PowerHost`/`IPowerHost` 本身提供 `UnregisterUnit`（[core/numbers/power_set/contracts/IPowerHost.cs:25-30](../../../../core/numbers/power_set/contracts/IPowerHost.cs:25)）；本报告只指重建流程没有调用它或做资源集合对账，不声称该 API 不存在。

固定顺序中 Equipment、KnownSkills、Vitals 的关系由 [core/foundation/save_system/contracts/SaveSections.cs:135-150](../../../../core/foundation/save_system/contracts/SaveSections.cs:135) 给出：`player.equipment` 先于 `player.known_skills`，而 `player.vitals` 更晚。这是 CORE-110-01 子场景 B 的触发窗口。

## 上轮三项修复的本轮真实复核

独立工程初始调用仍使用真实模块装配，但报告不沿用旧日志解释。当前日志中的实际值为：

```text
SUCCESSFUL-LOAD-INTERNAL-EVENTS
save_success=True;load_status=Loaded
after_load_level=10;entity_level=10;rating_after_load_before_dispatch=10;rating_after_dispatch=10

SUCCESSFUL-LOAD-POWER-MAX-INTERNAL-EVENT
equip_success=True;save_success=True;load_status=Loaded
after_load_before_dispatch_max=200;after_load_before_dispatch_health=200
after_load_max=200;after_load_health=200;equipped_after=True

ROLLBACK-EQUIPMENT-BEFORE-PROGRESSION
initial_equip=True;baseline_inventory_count=0;save_success=True;load_status=PersistableThrew
after_level=2;entity_level=2;equipped_after=True;inventory_count_after=0

SAME-MAP-RACE-SAVE-LOAD
save_a_valid=True;save_b_valid=True;race_b_oracle_stat=91;load_status=Loaded
after_load_race=arch.race.followup_b;stat=91;auraA=False;auraB=True
```

这同时覆盖了空 `player.equipment` 文档的失败回滚场景，以及两个独立角色槽位的有效 A/B 存档；没有观察到上轮三项修复在本基线回归。

## 已确认的新问题

### CORE-110-01：失败回滚后派生状态残留（两个触发窗口）

合法 A 存档与合法 B 存档分别由真实 Gameplay fixture 生成。A 宿主额外注册后段抛异常的自定义段，加载 B 后在该段触发失败。forward 阶段的 B race hook 已执行；回滚随后恢复 A 的字段和快照，但 `IDerivedStateRebuilder` 契约明确只在成功段的正向读档调用，[core/foundation/save_system/contracts/IDerivedStateRebuilder.cs:35-57](../../../../core/foundation/save_system/contracts/IDerivedStateRebuilder.cs:35) 未要求回滚调用。

当前实际输出：

```text
ROLLBACK-DERIVED-STATE-RACE
save_a_initial=True;save_a_with_failure=True;save_b_valid=True;load_status=PersistableThrew
after_race=arch.race.followup_a;stat=91;auraA=False;auraB=True
EXPECTED=load_status=PersistableThrew;after_race=arch.race.followup_a;stat=61;auraA=True;auraB=False
```

这不是只观察事件队列：A/B stat 与 aura 都来自真实 `RulesAssembly`，失败后字段已回 A 而派生状态仍是 B。建议在回滚完成后按同一依赖顺序重建派生状态，或让回滚明确通知装配根；仅维持当前正向顺序不足以恢复派生副作用。

#### 子场景 B：Equipment 回滚后 max/health 仍是低装备状态

合法 A 前态为 level2、装备需求 level2 的物品、max=200、health=200；合法 B 存档由 level1 空装备角色真实保存。仅将 B 文档的 `player.known_skills` 段改为坏形状作为故障注入，故障发生在 Equipment 成功加载并执行派生 max 重算之后、Vitals 之前。`PlayerVitalsPersistable.Load` 的现有逻辑见 [core/gameplay/assembly/PlayerVitalsPersistable.cs:99-134](../../../../core/gameplay/assembly/PlayerVitalsPersistable.cs:99)，其未执行是本案例的一部分。

当前实际输出：

```text
ROLLBACK-DERIVED-STATE-KNOWN-SKILLS-BEFORE-VITALS
fault_section=player.known_skills;equip_success=True;saved_max=200;saved_health=200
save_a_valid=True;save_b_valid=True;load_status=PersistableThrew
after_level=2;equipped_after=True;max_after=100;health_after=100
EXPECTED=load_status=PersistableThrew;after_level=2;equipped_after=True;max_after=200;health_after=200
```

因此问题不只是“Equipment 是否恢复”：装备确实回来了，但由于回滚不调用派生钩子，Power max 保持 B 的 100；随后读档前的 Health=200 没有机会再次成功加载，当前值保持 100。建议回滚完成后对所有受 Equipment/Progression/Race/Class 影响的派生状态统一重算，并验证 Vitals 恢复时序；单独给回滚列表追加一个 Equipment hook 不能覆盖本案例。

### CORE-110-02：换职业后旧 base 键与旧 power type 残留

真实 A/B 职业存档使用不同形状：A 的 `base_stats` 含 `StatClassPower=10` 与 `StatClassLegacy=5`，`power_types` 含 Health+Mana；B 只声明 `StatClassPower=20` 与 Health。A/B 均由独立 `GameplayAssembly` 保存，A 宿主同图 `RestoreFromSlot` 加载 B。

```text
SAME-MAP-CLASS-SAVE-LOAD
save_a_valid=True;save_b_valid=True;a_stat=10;a_legacy=5;a_mana=True
b_oracle_stat=20;b_oracle_legacy=0;b_oracle_mana=False;load_status=Loaded
after_class=arch.class.followup_b;after_stat=20;after_legacy=5;after_mana=True
EXPECTED=...after_stat=20;after_legacy=0;after_mana=False
```

`ReloadArchetypeAndRace` 的 `SetBase` 覆盖了共同键 `StatClassPower`，但旧职业独有的 `StatClassLegacy=5` 没有被移除；`PowerHost` 虽有 `UnregisterUnit`，当前换职业流程没有调用它或做旧/新资源集合对账，因此 A 的 Mana 仍可查询。建议换职业时先按旧职业声明键移除 base/modifier，再按新职业完整聚合，并对资源类型集合采用明确的 unregister/re-register 或兼容迁移策略。

### CORE-110-03：同一连续 tick 多条目标 move 的位移按条数倍增

文档规定连续移动按 `speed × dt` 推进；[architecture/03_运行时骨架.md:117-128](../../../../architecture/03_运行时骨架.md:117) 和 [core/carriers/unit/README.md:88-92](../../../../core/carriers/unit/README.md:88) 将意图处理与位移推进列为移动阶段职责，但当前实现没有把同一单位同一 tick 的意图裁决与位移积分分离。实现的 [MovementTickHandler.cs:120-129](../../../../core/carriers/unit/core/MovementTickHandler.cs:120) 会逐条调用 `ApplyIntent`，而 `BeginPathTo` 在 [MovementTickHandler.cs:295-333](../../../../core/carriers/unit/core/MovementTickHandler.cs:295) 建路后立即调用 `ContinuePathCore(dt)`；因此同目标的每条存活意图都消费一次 `dt`。

独立探针复用 `MovementTickHandlerTests` 的真实 `WorldSim`/`WorldUnitAccess`/`MovementHost`/`MovementTickHandler` fixture 结构，且通过公共 `MovementHost.Request` 提交请求；相对冻结仓工程引用运行，日志为 [movement-boundary-probe.log](logs/movement-boundary-probe.log)：

```text
MOVEMENT-BOUNDARY-PROBE baseline=ac3b622 version=1.10.0
REPEATED-MOVE count=1;speed=10;dt=0.1;actual_x=1;path_active=True
REPEATED-MOVE count=2;speed=10;dt=0.1;actual_x=2;path_active=True
REPEATED-MOVE count=3;speed=10;dt=0.1;actual_x=3;path_active=True
STOP-AFTER-TWO-MOVES actual_x=0;stopped_callbacks=1;path_active=False;mode=Idle
TWO-MOVES-AFTER-STOP actual_x=2;stopped_callbacks=1;stop_reason=Replaced;path_active=True;mode=Run
```

`STOP-AFTER-TWO-MOVES` 与 README 的四步契约一致：Stop 之后更早的两个 move 被丢弃且不位移；`TWO-MOVES-AFTER-STOP` 也符合“Stop 后提交的 move 照常生效”，其中 `stopped_callbacks=1` 的原因是第二条 move 替换第一条路径（`Replaced`），不是 Stop 回调重复，因此 Stop 顺序语义不构成缺陷。真正的问题是两个 move 各自立即推进，导致固定 `dt` 按意图条数重复消费。修复应将同 tick 意图裁决与每 tick 单位级位移积分分离：先按明确策略合并/裁决目标意图，再对单位最多执行一次 `speed × dt` 推进。

## 钩子异常、空段与多角色边界

### 重建钩子异常：框架按契约继续读档，未判为独立产品缺陷

将真实 Gameplay 注入的重建器替换成只抛异常的探针实现，验证 `SaveSystem` 的边界行为：

```text
DERIVED-HOOK-EXCEPTION-BOUNDARY
save_success=True;load_status=Loaded;before_calls=1;section_calls=20;warning_count=21;rating_after=5
```

`SaveSystem` 对 `BeforeLoad` 和每次 `OnSectionLoaded` 都吞异常并记 warning；这是 [IDerivedStateRebuilder.cs:35-57](../../../../core/foundation/save_system/contracts/IDerivedStateRebuilder.cs:35) 与 [SaveSystem.cs:366-420](../../../../core/foundation/save_system/core/SaveSystem.cs:366) 写明的行为。由于本探针故意替换生产实现，`rating_after=5` 只证明“钩子失败时成功状态可能带陈旧派生值”，不能证明生产钩子当前会抛异常，故本项作为边界/诊断风险排除为已确认产品缺陷。

### 空段与多角色

- 空段：CORE-180-02 当前探针使用合法的空 `player.equipment` 段并在失败后验证装备恢复；本轮没有观察到空段导致的新增问题。坏形状 `player.known_skills` 是有意故障注入，不能归类为空段。
- 多角色/多槽：本轮使用独立 A/B 角色 fixture 生成两个有效存档并在 A 宿主切换 B，已覆盖多角色槽位数据；`GameplayAssembly` 的生产注册仍捕获单个 `PlayerUnit`，没有把该结果外推为多 live-player 支持或缺陷。

## 运行命令与产物

```powershell
dotnet run --project "D:\workespace\ws-game-review-ac3b622\architecture\落地计划\audit-ac3b622-20260909\core\repro\FollowupCoreProbe.csproj"
```

- 探针源码：[FollowupCoreProbe.cs](repro/FollowupCoreProbe.cs)
- 相对引用工程：[FollowupCoreProbe.csproj](repro/FollowupCoreProbe.csproj)
- 当前运行日志：[followup-core-probe.log](logs/followup-core-probe.log)
- 当前退出码：[followup-core-probe.exit.txt](logs/followup-core-probe.exit.txt)
- 多意图边界探针源码：[MovementBoundaryProbe.cs](repro/MovementBoundaryProbe.cs)
- 多意图相对引用工程：[MovementBoundaryProbe.csproj](repro/MovementBoundaryProbe.csproj)
- 多意图运行日志：[movement-boundary-probe.log](logs/movement-boundary-probe.log)
- 多意图退出码：[movement-boundary-probe.exit.txt](logs/movement-boundary-probe.exit.txt)

```powershell
dotnet run --project "D:\workespace\ws-game-review-ac3b622\architecture\落地计划\audit-ac3b622-20260909\core\repro\MovementBoundaryProbe.csproj"
```
