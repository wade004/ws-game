# 表现与 Unity 验证记录

## 范围与证据基线

本记录针对冻结仓 `D:\workespace\ws-game-review-ac3b622` 的 `1.10.0`、HEAD
`ac3b622041c348e87a469959c09d8a541a7c1351`，原仓 `D:\workespace\ws-game` 保持只读。

Unity 验证工程为全新副本：
`C:\Users\1\.codex\visualizations\2026\09\08\01a0801a-5646-78c2-a68c-99bbc742bed9\audit-ac3b622-20260909\unity_probe_1\unity`。

副本初始同步自当前冻结仓的 `adapters/unity`、`adapters/conformance`、
`games/_template`、`data/_framework`、`data/_sample` 和 `assets`。

PlayMode 夹具源码按其真实 `Application.dataPath` 上溯三级规则，另同步至审计根
`adapters/unity/.../Tests/Runtime/TestData*`，没有从旧审计工程覆盖源文件。

Unity 版本为 `6000.3.23f1`。最终有效命令未与 `-quit` 同传，采用 test runner 自行结束；最初的
`navigation-probes.log` 误带 `-quit`，未实际运行测试，已排除其结果。

源文件、导航测试、fixture、占位音频和 DLL 的哈希记录见
[baseline-hashes.log](baseline-hashes.log)。

六个 DLL 的首轮副本版本与冻结仓 `bin/_check_artifacts` 当前构建产物不同。

首轮 PlayMode 失败后，已明确从冻结仓当前 `bin/_check_artifacts/bin/*/release` 复制六个 DLL
到独立副本；这是本执行步骤显式复制，非 Unity 自动修改。

因此 `editmode-full-v2` 在六个 DLL 切换前运行，使用初始冻结 `adapters/unity` 插件 DLL；
`playmode-full-v2/final` 使用切换后的当前构建 DLL，导航首轮和 A/B 最终探针也使用切换前
冻结 `adapters/unity` 插件 DLL。

两组 DLL 的来源、替换时点和最终哈希均已写入哈希日志，不把这次切换描述为无漂移。

## 1.9 掉落物 View 对账复核

当前 `ViewBinder` 构造函数在
[ViewBinder.cs:125](../../../../presentation/view_binding/core/ViewBinder.cs#L125)
订阅 `SaveEventKeys.SaveLoaded`。

`OnSaveLoaded` 在
[ViewBinder.cs:234](../../../../presentation/view_binding/core/ViewBinder.cs#L234)
先按 `ISimSnapshot.Exists` 销毁绑定表中的陈旧 View。

随后在
[ViewBinder.cs:244](../../../../presentation/view_binding/core/ViewBinder.cs#L244)
遍历 `GetAllEntityIds`，为缺 View 的存活实体复用 `OnEntityCreated` 规则。

补建前读取 `GetRawKind` 和 `GetDisplayId`；缺失数据走安全跳过分支，避免按空值创建。

`ISimSnapshot.GetRawKind` 与 `GetAllEntityIds` 当前是 C# 默认接口方法，分别在
[ISimSnapshot.cs:58](../../../../presentation/common/contracts/ISimSnapshot.cs#L58)
和 [ISimSnapshot.cs:74](../../../../presentation/common/contracts/ISimSnapshot.cs#L74)
返回 `null` 与空集合。

因此旧自定义快照实现可以继续编译，但未覆盖这两个成员时只保留陈旧 View 销毁半边对账。

唯一生产快照 `WorldSimSnapshot` 已覆盖这两个查询；完整补建能力依赖自定义实现方提供真实列表。

`PRES180_SaveLoadViewReconciliationTests` 记录了同图保存、清空、读档和重复读档的真实
`SaveSystem`/`LootHost`/`WorldSim`/`ViewBinder` 验收路径，见
[PRES180_SaveLoadViewReconciliationTests.cs:151](../../../../presentation/view_binding/tests/PRES180_SaveLoadViewReconciliationTests.cs#L151)。

本轮未把 `ViewBinder` 的同实体已有 View 重建列为新缺陷；其幂等跳过是源码明确行为，
本次验证关注的是被 `SuppressDispatch` 丢弃的实体变化是否能由 `save.loaded` 补账。

## 2.10 导航定向验证

导航实现当前为 [UnityNavigation2D.cs:45](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs#L45)。

### NAV-110-01（P2，已确认）

输入阻挡矩形 `[(0,0),(1.2,1)]`，`from=(1.21,0.5)`，`to=(1.8,0.5)`。

两端的 `IsWalkable` 都为 `true`，直接 `Raycast` 为 `null`，说明几何端点和直线均可通行。

`FindPath` 在 [UnityNavigation2D.cs:122](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs#L122)
先做端点可行判断，再在 [UnityNavigation2D.cs:150](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs#L150)
调用 A*。

该矩形使起点所在采样格中心约为 `(1.125,0.625)`，中心落在阻挡内部。

A* 在 [UnityNavigation2D.cs:438](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs#L438)
直接拒绝该格，返回 `null`；后续 `BuildWorldPath` 邻格接合逻辑没有机会执行。

审计探针日志为 `fromWalkable=True toWalkable=True directRaycast=null path=null`。

这使一个真实可行的精确端点请求被离散中心采样提前拒绝，属于 P2 导航正确性缺陷。

复现 XML：[navigation-probes-final.xml](navigation-probes-final.xml)。

源码副本：[UnityNavigation2D.ac3b622.cs](UnityNavigation2D.ac3b622.cs)。

### NAV-110-02（P2，已确认）

输入薄阻挡矩形 `[(0.1,-0.5),(0.15,0.5)]`，`from=(-1,0)`，`to=(1,0)`。

手工绕路 oracle 为 `(-1,0) -> (-0.2,-0.6) -> (0.2,-0.6) -> (1,0)`。

三段 oracle 的 `Raycast` 结果均为 `null`，且各点均不在阻挡内部，证明存在可行绕路。

由于薄墙宽度小于默认网格采样间距，A* 在 [UnityNavigation2D.cs:477](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs#L477)
至 [UnityNavigation2D.cs:500](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs#L500)
只检查节点可行性和邻接节点，不把矩形内部穿越作为边合法性约束。

A* 找到直穿候选后，最终防线 [UnityNavigation2D.cs:165](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs#L165)
至 [UnityNavigation2D.cs:169](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs#L169)
检测到阻挡便整体返回 `null`，不会继续搜索绕路。

审计探针日志为 `path=null oracleRaycasts=null,null,null`。

因此薄障碍存在绕路时会被错误判成无路可走，属于 P2 导航正确性缺陷。

复现 XML：[navigation-probes-final.xml](navigation-probes-final.xml)。

定向探针源码：[AuditAc3b622NavigationProbes.cs](AuditAc3b622NavigationProbes.cs)。

本两项为真实 `UnityNavigation2D` 调用结果，不是静态猜测；未扩展随机测试。

## 3 Unity 测试结果

完整 EditMode 在补齐占位音频路径后通过 `62/62`。

其中冻结套件既有用例为 `60/60`，另含本审计副本 A/B 两条探针，因此 XML 总数为 62。

结果：[editmode-full-v2.xml](editmode-full-v2.xml)，日志：[editmode-full-v2.log](editmode-full-v2.log)。

首轮 `60/62` 失败的两条 WAV 用例只因独立副本缺少 `assets/_placeholder/sfx`，
补同步冻结仓 assets 后重跑通过；首轮不作为产品失败计数。

PlayMode 首轮为 `256/263`，失败 7 条集中在离散战斗、共享离散引导和 QuestDay。

切换六个 DLL 后的 `playmode-full-v2` 仍为 `256/263`，说明 DLL 替换没有修复这 7 条失败。
随后补齐审计根 `adapters/unity/.../TestData`，重跑 `playmode-full-final` 为 `262/263`，
只剩 QuestDay overlay 查找位置缺失。

补齐审计根 `adapters/unity/.../TestDataQuestDayProvider` 后，单独重测该类为 `1/1`。

因此本轮 PlayMode 证据口径是完整批次 `262/263` 加唯一失败类补测 `1/1`，
不把分批结果伪写为一次单进程 `263/263`。

完整批次：[playmode-full-final.xml](playmode-full-final.xml)（原件未归档，归档时遗漏、候选产出目录已搜索确认找不回，结论以本节引用的 262/263 数值为准），日志：[playmode-full-final.log](playmode-full-final.log)（原件未归档，同上）。

补测：[playmode-questday-final.xml](playmode-questday-final.xml)，日志：[playmode-questday-final.log](playmode-questday-final.log)。

首轮与 v2 结果分别保留在 `playmode-full.xml` 与 `playmode-full-v2.xml`，用于说明副本准备过程。

## 4 结论与边界

### EquipmentWeaponStyleSource 的真实读档复核

core_180 已使用真实 `EquipmentHost`、`EquipmentWeaponStyleSource`、`SaveSystem`、合法
`PresentationSchemaCatalog.RegisterAll` 数据完成独立探针；本表现记录引用其
[followup-core-probe.log](../core/logs/followup-core-probe.log) 与
[FollowupCoreProbe.cs:354](../core/repro/FollowupCoreProbe.cs#L354)。

探针先在 A 状态调用 resolver 填充风格缓存，再保存武器 B，读档恢复 B；读档期间装备事件受
SaveSystem 抑制，resolver 仍返回缓存 A，而 EquipmentHost 的真实装备值已经是 B。

该证据确认为 `PRES-110-01` P2：读档后缓存未因真实装备变化失效，影响武器风格解析。

该结论只覆盖风格缓存与真实装备状态不一致，不外推为 Mesh 冷加载、HUD 刷新或 Unity 屏幕
外观缺陷；这些链路仍需各自端到端证据。

1.9 的掉落物 View 对账修复已在当前源码中落地，SaveLoaded 外部派发和 WorldSim 全量对账路径清晰。

当前证据没有证明所有自定义 `ISimSnapshot` 实现都覆盖新成员；未覆盖时补建半边按设计安全退化。

NAV-110-01 和 NAV-110-02 是本轮新增的两个已确认 P2，均有真实 Unity XML 与坐标化日志。

Unity 既有 EditMode 60/60 与 PlayMode 分批补测结果支持当前门禁，但 PlayMode 不是单进程一次 263/263。

本轮未执行独立版构建、IL2CPP、实际游戏屏幕验收、长时性能压测或用户验收。

`EquipmentWeaponStyleSource` 的真实 SaveSystem/EquipmentHost 换装读档探针由 core_180 单独执行，
本报告只保留 Unity `EquipmentVisual`/动画链路的正式数据装配和实际播放事件端到端边界。

缓存生命周期、资源卸载、多个 Animator state 共享 clip、异步加载和 override 匹配没有被本轮 XML
证明为缺陷；不能仅凭静态缓存代码扩大成泄漏结论。

导航源码与测试证据副本：[UnityNavigation2DTests.ac3b622.cs](UnityNavigation2DTests.ac3b622.cs)。

报告只记录本次验证和代码审阅结果，没有修改产品代码、原仓代码或冻结仓既有测试，也没有 commit。
