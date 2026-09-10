# ws-game 1.14.0 表现与 Unity 验证

本轮基线是冻结仓 `D:\workespace\ws-game-artifacts\audit-76d16a5-frozen`，HEAD `76d16a54e54f0f11204d97d7460563c8a0dc8cd8`，VERSION `1.14.0`。Unity 工程是独立副本 `D:\workespace\ws-game-unity-audit-76d16a5\copyRoot\adapters\unity`，源码、产品与既有测试未改。check 产物来自 `D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\build\check-artifacts`；六 DLL 的来源、目标及 SHA-256 见 [six-dll-copy.txt](../hashes/six-dll-copy.txt)，6/6 一致。

## 当前运行证据

| 批次 | 命令/证据 | 结果 | 判断 |
|---|---|---:|---|
| 筛选 Play | `-testFilter EquipmentVisualSaveLoadResetTests\|VfxAnchorFollowTests`，[filtered-play.xml](filtered-play.xml) | 3/3 | View 读档和 VFX 真实适配器能力通过 |
| 筛选 Edit | `-testFilter DataHotReloadDeletedOverlayTests`，[filtered-edit-hotreload-v2.xml](filtered-edit-hotreload-v2.xml) | 1/1 | Deleted overlay 自动回落通过 |
| 最终完整 Edit | [editmode-full-1.14.xml](editmode-full-1.14.xml) | 70/70 | 当前副本 EditMode 全量通过 |
| 最终完整 Play | [playmode-full-1.14.xml](playmode-full-1.14.xml) | 272/272 | 当前副本 PlayMode 全量通过，含 `[GF-SMOKE] RESULT=OK` |

完整批次是本轮最终基线各跑一次；筛选批次是行为定位，不把旧轮重复测试数写成本轮新结果。日志中 Unity licensing 的 access-token 更新提示不影响测试结果，测试运行以 XML 的明确计数和退出完成记录为准。

## 1.14 新增行为

### 同图既有 View 的 Save/Load 外观重置

生产链在 `presentation/view_binding/core/ViewBinder.cs:89,115,128` 接受可选 `EquipmentVisualSource`；`OnSaveLoaded` 在 `:266` 保存触发前的既有 View id，处理实体增删后，在 `:308-314` 对仍存活且实现 `IEquipmentVisualResettable` 的 View 调用 `ReplayEquippedForUnit` 和 `ResetEquipmentVisuals`。接口语义在 `presentation/render/contracts/IEquipmentVisualResettable.cs:27-34`：先清全部已应用外观，再应用新快照，空快照必须清零且重复调用幂等。`UnityModelView` 在 `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityModelView.cs:29,179-186` 实现该能力。

模板生产根在 `games/_template/Runtime/GameBootstrap.cs:314,337,433-438` 创建装备外观源，并把同一实例传给新 View 工厂和 `ViewBinder`；`UnityViewFactory` 的 `equipmentVisualSource` 仍是可选参数（`:255-268`），因此未选择装备外观能力的消费方仍保持静默跳过。这是框架机制与具体 View 能力的组合条件，不要求没有装备外观概念的 gobj、掉落物或其他 View 实现接口。

独立真实探针为 `adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/EquipmentVisualSaveLoadResetTests.cs`。它使用真实 `GameplayAssembly`、`EquipmentHost`、`SaveSystem`、`ViewBinder`、`UnityViewFactory` 和 `UnityModelView`，保存空装备 B，实时装备 sample sword A，再 `Load(B)`；不是手工发布 `SaveLoaded` 或只检查样式缓存。其日志为：

```text
[EquipmentVisualSaveLoadResetTests] saveB=True;loadB=Loaded;equipment_after_load=0;socket_childCount_before=0;socket_childCount_after=0;view_same=True
```

`socket_childCount_before` 是日志写入时同一个 socket 已经被 LoadB 对账后的值；行为断言在测试 `:226` 先确认装备 A 时为 1，随后 `:235-251` 重新取得 LoadB 后的 View、ModelHandle、visual root 和 socket，确认 `equipment_after_load=0`、View identity 不变且 `socket_childCount_after=0`。因此 1.13 的历史正确性失败（状态已为空但同 View 外观残留）在本轮由真实 Save/Load 链路通过；历史失败本身仍保留在旧轮报告的修复矩阵中，不能改写成“从未发生”。

同目录 `EquipmentVisualReplayTests` 覆盖新建 View 的已有装备重放、无 resolver 的兼容退化和 sprite override；`EquipVisualSocketClearTests` 覆盖 socket/slot 装备、卸装与重复卸装；`ModelViewTests`/`UnityRenderer3DTests` 覆盖模型创建销毁、socket detach、缺挂点换模后的 intent 重挂和 shadow 恢复。完整 Play 通过这些现有回归，但没有把它们扩大解释为任意游戏资源的美术质量验收。

### VFX anchor/socket 跟随、停止与回收

这是表现通用契约。`architecture/09_表现层.md:31,283-287` 明确 `anchor` 和无模型句柄时降级为 world 的 `socket` 要持续跟随；真实 `socket` 由 `IRenderer3D.AttachToSocket` 父子关系跟随。`presentation/vfx_sfx/contracts/IParticleRepositioner.cs:21-35` 是不改变 `IRenderer2D` 最小契约的可选扩展，未实现时允许保留生成时位置；`presentation/vfx_sfx/core/VfxPlayer.cs:92-95,162,388-393,450-465` 探测并每帧重定位可跟随实例，解析不到目标时停止并摘除记录。Unity 默认适配器在 `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer2D.cs:50,464-483` 实现它。

真实 Play 探针 `VfxAnchorFollowTests.cs:40-65` 用 `UnityRenderer2D.EmitParticle` 产生真实 Transform，验证初始 `(0,0,0)` 经 `SetParticlePosition` 变为 `(5,7,0)`；`:67-81` 验证停止后再次重定位静默忽略。两项均通过。引擎无关的 `presentation/vfx_sfx/tests/VfxPlayerFollowTests.cs` 另覆盖 anchor、socket-to-world、目标销毁和未装配可选能力的退化逻辑。VFX 的具体资源、类别、播放时长、Swing/Impact 选择仍由游戏内容与战斗策略决定，不能用游戏没有选择某个特效定义来否定上述框架跟随机制。

### DataHotReload 删除覆盖回落

`games/_template/Runtime/DataHotReload.cs:95-113,126-159,225-256` 在 `UNITY_EDITOR || DEVELOPMENT_BUILD` 下创建 `FileSystemWatcher`，订阅 `Changed/Created/Deleted`，并把删除与其他变更送入相同的去抖、主线程 `ProcessPendingChanges` 和 `DataRegistry.Reload` 路径；删除后按当前仍存在的数据根重新合并，因此 game override 删除应回落 framework 行。`Renamed` 在 `:139-159` 同时登记旧名和新名。发布构建是文件头 `:10-12` 所述空壳，开发期承诺不外推到 Release/Shipping。

`games/_template/Tests/Editor/DataHotReloadDeletedOverlayTests.cs:32-101` 使用临时 framework/game 双根、自给 `stat.definition` 表和真实 watcher/registry，初始 override 值为 2，删除 game 文件后等待并手动驱动生产同一 `ProcessPendingChanges`；日志为 `before=2;after_delete=1`，XML 1/1 通过，之后手工 `Reload` 仍保持 1。这个 fixture 是通用机制测试输入，不依赖 ws-game-wow 或外部游戏数据。

## 表现/适配器旧修复矩阵与本轮覆盖

| 历史项 | 1.13 修复结论 | 1.14 本轮证据 | 当前判断 |
|---|---|---|---|
| P2-08 ExistingView 装备外观 SaveLoaded | 1.13 正确性 probe 失败，确认空装备状态与旧外观不一致；1.14 改为 `IEquipmentVisualResettable` 对账 | 当前真实 SaveSystem + EquipmentHost + UnityModelView Play probe 通过，`equipment_after_load=0`, `view_same=True`, `socket_after=0` | 已修复并通过真实外观 oracle |
| P2-09 删除双根 override 不回落 | 1.13 诊断 probe 复现删除后仍为 2；1.14 watcher 增加 Deleted | 当前真实 Editor probe `before=2;after_delete=1`，1/1 | 已修复并通过自动回落 oracle |
| 静态差距：VFX anchor 持续跟随 | 1.13 为待修静态差距；1.14 增加 `IParticleRepositioner` 与 Unity 实现 | 当前 `VfxAnchorFollowTests` 2/2，完整 Play 272/272 | 已实现并通过真实 Transform；可选能力未装配的引擎仍按允许退化解释 |
| NAV-111-01 窄通道接入 | 1.11 历史 P2；1.12 已修复窄通道接入路径 | 当前完整 Edit 70/70，包含 Unity navigation 回归；本轮未重复旧窄通道专用探针 | 按 `02:165-167` 几何契约保留；不把跨帧预算当要求 |
| SPATIAL-111-01 半径跨 bucket | 1.11 历史 P2；1.12 已修复半径 bucket 覆盖 | 当前完整 Edit 70/70，包含 Unity spatial query 回归；本轮未重复旧跨 bucket 专用探针 | 按 `02:189` 登记生命周期与查询语义保留；不把全索引当要求 |

历史 NAV/SPATIAL 回归只按当前适配器契约复核：`architecture/02_引擎适配层.md:165-167` 仍规定精确端点、统一 raycast/path 通行规则和不切角；`:189` 规定查询只针对已登记对象并由调用方负责 register/update/unregister/clear。其 `:168` 明确是否跨帧分摊由实现决定，`:190` 明确是否全部查询走空间索引由实现决定。因此本轮不能把跨帧预算、全索引或实现内部优化建议重新列成违约；完整 Edit/Play 回归通过也不宣称覆盖任意障碍几何或任意查询规模。ABI、Gobj schema、SkillHotReload/FindUnits、ExprValueJson 等核心项由 core review 负责，本页不替代其当前证据或结论。

## 证据边界

本轮没有运行独立版、IL2CPP、consumer smoke 或正式 package 发布验证；`-SkipUnity` check 中这些步骤按日志标为 SKIP。本轮也没有对 VFX 具体资源美术质量、TargetPoint 地图内容、Swing/Impact 事件选择做游戏级验收。运行与源码证据可从 [copy-layout.md](copy-layout.md)、[run-1.14-presentation.ps1](run-1.14-presentation.ps1) 和 [repro](repro) 复核；tracked adapter/conformance/template 共 120 个 C# 文件在冻结源与 copyRoot 逐项 SHA-256 一致，清单见 `../hashes/tracked-cs-copy.txt`。
