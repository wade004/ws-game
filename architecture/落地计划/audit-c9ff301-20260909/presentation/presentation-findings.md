# ws-game 1.13.0 Unity / 表现有界验证

验证对象是冻结提交 `c9ff30107413083188c597c0b65cf1691c9dfe9b`（`VERSION=1.13.0`）。原仓 `D:\workespace\ws-game` 未写入；Unity 运行只使用外部副本 `D:\workespace\ws-game-unity-audit-c9ff301\copyRoot`，其 `adapters/unity` 是真实 Unity 工程。Unity 版本为 `6000.3.23f1`，归档的最终基线证据包含 restore 后的 EditMode 与 PlayMode 各一次；本次责任 review 未重跑 Unity。未运行独立版或 IL2CPP，且未干扰其他 Unity 进程。

责任、owner、启用条件及框架/游戏边界的复核见 [scope-review.md](scope-review.md)。

## 证据与可复现入口

冻结仓源码、既有测试、`adapters/conformance`、`games/_template`、`data`/`assets` 与构建产物按副本布局同步。冻结仓 build 生成的完整 `Assets/StreamingAssets`（含 scene、nav_mesh、字体）由副本 `build.ps1 -SyncContent` 同步；最终同步清单为 framework 5、sample 57、template game 37、placeholder 97、sprites 95、audio 13、vfx 36，共 359 个 GameFoundation 文件。

六个 DLL 只从 docs agent 的 `docs-project/check-artifacts/bin` release 目录复制，来源、目标和 SHA-256 见 [six-dll-hashes.log](six-dll-hashes.log)。适配层逐文件校验见 [tracked-adapter-cs-hashes.log](tracked-adapter-cs-hashes.log)；扩展的 template/conformance 逐文件校验见 [tracked-template-cs-hashes.log](tracked-template-cs-hashes.log)、[tracked-conformance-cs-hashes.log](tracked-conformance-cs-hashes.log) 和汇总 [tracked-cs-hashes-all.log](tracked-cs-hashes-all.log)。当前汇总为 117 个冻结仓 tracked `.cs`，缺失 0、SHA mismatch 0；临时 probe 是新增文件。

真实 View probe 的合法样本 `item.sample_model_sword` 通过 [prepare-view-equipment-fixture.ps1](prepare-view-equipment-fixture.ps1) 追加到 `_sample/item/item.template.json`，脚本先保存独立原始字节备份并记录 SHA；[restore-view-equipment-fixture.ps1](restore-view-equipment-fixture.ps1) 直接还原备份、校验源文件与 StreamingAssets SHA，再执行 `-SyncContent`。已完成一次 prepare/restore 往返，结果见 [fixture-restore.log](fixture-restore.log)，原始 SHA 为 `e827331ab8d5728fea40f7ef624205f75c54c683d467e1cc1ec18ff2cffb6833`。便携 filtered runner 为 [run-view-equipment-filtered.ps1](run-view-equipment-filtered.ps1)，默认路径硬限制在外部副本。

## 全量 Unity 结果

EditMode 命令：

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe' -batchmode -nographics -projectPath 'D:\workespace\ws-game-unity-audit-c9ff301\copyRoot\adapters\unity' -runTests -testPlatform EditMode -testResults 'D:\workespace\ws-game-artifacts\audit-c9ff301-resumed\architecture\落地计划\audit-c9ff301-20260909\presentation\editmode-full-final.xml' -logFile 'D:\workespace\ws-game-artifacts\audit-c9ff301-resumed\architecture\落地计划\audit-c9ff301-20260909\presentation\editmode-full-final.log'
```

[editmode-full-final.xml](editmode-full-final.xml) 为 `Passed`, 70/70（69 个基线测试 + 1 个 Deleted overlay 诊断测试）。[editmode-full-final.log](editmode-full-final.log) 是对应 Unity log。

PlayMode 命令同样使用 `-testPlatform PlayMode`，结果输出为 `playmode-full-final.xml` 和 `playmode-full-final.log`（原件未随本次归档提交到本仓库，见 `toolchain/tests/.linkcheck-ignore` 说明）：`Passed`, 270/270，0 failed、0 skipped，duration 66.6525 秒。全量基线运行时 View probe 已临时以 `.cs.audit-disabled` 隔离，避免其行为正确性失败污染基线；其最终源码和单独 XML/log 仍保存在本目录。

## NAV / SPATIAL 修复复核

NAV 真实 Unity EditMode 回归在全量 70/70 中通过。当前实现 [UnityNavigation2D.cs](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs) 的 `FindPath` 为 127 行，网格失败后的窄通道直达兜底为 149–166 行，并由 `SegmentHasClearContact`（758 行）作独立接触判定。实际 oracle 是端点可行走、Raycast 无阻挡时返回首尾精确等于输入的直线路径；窄通道回归实际通过，未观察到 1.13 新缺陷。

SPATIAL 真实 Unity EditMode 回归同样通过。`QueryRadius` 为 85 行，半径扩张和候选查询为 100–101 行；`QueryCone` 为 109–115 行；候选 bucket 为 208 行，最大实体半径提示为 227 行。独立 oracle 是实体圆心跨 bucket 但实体半径与查询区域相交时必须命中；另有 UpdatePosition 跨 bucket 后仍命中的 oracle，以及 Unregister 后不再命中的 oracle，均在回归中通过。

## DataHotReload

生产接线在 [GameBootstrap.cs](../../../../games/_template/Runtime/GameBootstrap.cs) 220 行创建 `DataHotReload`。实际 changed-file probe [DataHotReloadProductionAuditTests.cs](DataHotReloadProductionAuditTests.cs) 启动真实 `GameTemplateShell`/`GameBootstrap`，改写已有合法 `stat.move_speed` 的 `default_base` 5→7，等待注册表变为 7，finally 恢复并等待回到 5；该测试包含在最终 PlayMode 270/270 中并通过，相关 `ReloadTable` 日志摘录见 [data-hotreload-play-evidence.log](data-hotreload-play-evidence.log)。

静态边界在 [DataHotReload.cs](../../../../games/_template/Runtime/DataHotReload.cs)：watcher 注册 `Changed`、`Created`、`Renamed` 为 110–112 行，主线程 `Update` 为 141 行，重载为 200 行。删除 overlay 的真实 EditMode probe [DataHotReloadDeletedOverlayAuditTests.cs](DataHotReloadDeletedOverlayAuditTests.cs) 记录 `before=2; after_delete=2; manual_reload_fallback=1`：正确 oracle 是删除 game override 后回到 framework 值 1；watcher 未订阅 `Deleted`，所以仍保留旧值 2。这是已复现的 P2 行为缺陷，断言用例的 1/1 通过表示捕获了缺陷，不能计作功能通过；对应摘录见 [data-hotreload-edit-evidence.log](data-hotreload-edit-evidence.log)。

SkillDefCache 永久缓存、生产 SkillHost 未订阅 `DataLoadCompleted` 已由 core 真实 host probe 取证，见 [skill-hot-reload-boundary.log](../core/logs/skill-hot-reload-boundary.log)；本 Unity 结果不把 README 的“运行中立刻效果”当成已证明。

## 同图已有 View 读档外观

最终 probe [ExistingViewEquipmentSaveLoadAuditTests.cs](ExistingViewEquipmentSaveLoadAuditTests.cs) 使用真实 `GameBootstrap`、`SaveSystem`、`InventoryHost`、`EquipmentHost`、`ViewBinder`、`UnityViewFactory` 和 renderer。路径为：真实新局 → 保存空装备 B → 真实 Add/Equip `item.sample_model_sword` 得到 A → 保存 A → 真实 Load B。Load 后重新通过 ViewBinder 取 View、重新取 model root/socket，并等待 socket 计数最多 8 秒。

对应 [view-equipment-filtered-final.xml](view-equipment-filtered-final.xml) 的行为正确性断言失败，实际日志 [view-equipment-filtered-final.log](view-equipment-filtered-final.log) 记录：`equipment_after_load=0; socket_childCount_before=1; socket_childCount_after=1; view_same=True; socket_same=True`，并有 `socket_wait expected=0;actual=1`。正确 oracle 是装备为空时同一个 View 的 `socket.main_hand` childCount 为 0；实际仍为 1，故确认 P2：同一 View 未因 SaveLoaded 清理装备外观。此证据保留正确 oracle 的失败结果；新 View 创建时的 replay 通过不等于已有 View 外观刷新通过。

相关静态行号为 [ViewBinder.cs](../../../../presentation/view_binding/core/ViewBinder.cs) 125 行订阅 `SaveLoaded`，234 行进入 `OnSaveLoaded`，248 行遇到已有 `_views` 直接 `continue`；[EquipmentVisualSource.cs](../../../../presentation/render/core/EquipmentVisualSource.cs) 68–70 行只订阅 ItemAdded/Equipped/Unequipped，91 行为新 View 的 `ReplayEquippedForUnit`。这些行解释了为何 View identity 保持而旧 socket child 未清理。

## 试错与边界分类

首轮新增 probe 曾因缺 `UnityEngine.TestTools` 引用、错误 fixture basename、向默认 `data/game` 注入 sample 依赖，以及非法 localization key 造成装配/fixture/污染失败；这些问题已在外部副本修复，不计为框架缺陷。最终报告只引用修复后的 probe、最终 XML/log 和可往返恢复脚本。Deleted overlay 的断言用例通过是因为它捕获了已确认的 watcher 缺陷；同图 Save/Load 则按正确行为 oracle 失败并确认 P2。两者均与 NAV、SPATIAL、生产 changed-file hot reload 和 full Edit/Play 的功能通过数分开记录。

## Runner 验证

fixture 原始字节 SHA-256 以 [fixture-restore.log](fixture-restore.log) 为准：`e827331ab8d5728fea40f7ef624205f75c54c683d467e1cc1ec18ff2cffb6833`。最终基线是在 restore 后各运行一次 EditMode 与 PlayMode；fixture prepare/restore 往返和 View filtered 是额外调试阶段。View filtered 保留正确行为 oracle（期待 0、实际 1）的失败；Deleted overlay 则以断言捕获 watcher 缺陷而通过，二者语义不同。

SPATIAL 的 oracle 分开核对：跨 bucket 移动后仍应命中；Unregister 后应不再命中，二者均通过。tracked `.cs` 汇总为 117 项（Unity adapter 90、template 11、conformance 16），缺失 0、SHA mismatch 0，完整清单见 [tracked-cs-hashes-all.log](tracked-cs-hashes-all.log)。SkillDefCache/SkillHost 边界已由 core 真实 host probe 取证，见 [skill-hot-reload-boundary.log](../core/logs/skill-hot-reload-boundary.log)。

最后执行的 [run-view-equipment-filtered.ps1](run-view-equipment-filtered.ps1) 会把归档 probe 源复制到外部副本的 Runtime Tests、执行 PlayMode filter、等待并解析 XML、校验 1 个用例及 `equipment_after_load=0`、`view_same=True`、`socket_same=True`、`socket_wait expected=0;actual=1`，最后恢复 fixture 与 probe 原状态。验证结果见 [view-equipment-runner-verification.log](view-equipment-runner-verification.log)：`runner=OK;unity_exit=2;cases=1;result=Failed;expected_socket_after=0;actual_socket_after=1;view_same=True;socket_same=True`。
