# 表现与 Unity 验证记录

## 范围与基线

本记录针对冻结仓 `D:\workespace\ws-game-review-6739f50` 的 `1.11.0`、HEAD
`6739f50e44ba39a023c6209af2673aaf6a1c1fdc`；原仓 `D:\workespace\ws-game` 未写入。

独立 Unity 工程为全新副本：
`C:\Users\1\.codex\visualizations\2026\09\08\01a0801a-5646-78c2-a68c-99bbc742bed9\audit-6739f50-20260909\unity_probe_1\unity`。

副本的 `adapters/unity`、`adapters/conformance`、`games/_template`、`data`、`assets`、
完整 `Tests/Runtime/TestData*` 均由本轮冻结仓复制；运行内容另按 `build.ps1` 同步到
`Assets/StreamingAssets/GameFoundation`，包括数据表、占位素材、字体和构建期生成的
`scene/sample_field.json`、`nav_mesh/sample_field.json`。

Unity 版本为 `6000.3.23f1`。最终有效命令均使用 `-runTests` 且不带 `-quit`；EditMode 使用
`-nographics`，PlayMode 未使用 `-nographics`。初始导航因缺少 `adapters/conformance`
目录而未生成测试 XML，随后日志被覆盖，原始准备失败没有留存；初始 Spatial 因审计 Id 含
非法连字符未进入查询。上述初始阶段不作为结果证据，修正后的日志和 XML 才作为证据。

可复制的重跑命令如下（按需替换结果文件名）。项目位于
`copyRoot/unity_probe_1/unity`；外部 fixture 放在
`copyRoot/adapters/unity/.../Tests/Runtime/TestData*`、`copyRoot/adapters/conformance`、
`copyRoot/games/_template`、`copyRoot/data` 和 `copyRoot/assets`，再从冻结仓按
`build.ps1` 同步完整 `Assets/StreamingAssets/GameFoundation`。两条命令均不带 `-quit`；
按当前代码重跑 EditMode 会保留两条新增失败断言。

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe' `
  -batchmode -nographics `
  -projectPath 'C:\Users\1\.codex\visualizations\2026\09\08\01a0801a-5646-78c2-a68c-99bbc742bed9\audit-6739f50-20260909\unity_probe_1\unity' `
  -runTests -testPlatform EditMode `
  -testResults 'D:\workespace\ws-game-review-6739f50\architecture\落地计划\audit-6739f50-20260909\presentation\editmode-rerun.xml' `
  -logFile 'D:\workespace\ws-game-review-6739f50\architecture\落地计划\audit-6739f50-20260909\presentation\editmode-rerun.log'
```

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe' `
  -batchmode `
  -projectPath 'C:\Users\1\.codex\visualizations\2026\09\08\01a0801a-5646-78c2-a68c-99bbc742bed9\audit-6739f50-20260909\unity_probe_1\unity' `
  -runTests -testPlatform PlayMode `
  -testResults 'D:\workespace\ws-game-review-6739f50\architecture\落地计划\audit-6739f50-20260909\presentation\playmode-rerun.xml' `
  -logFile 'D:\workespace\ws-game-review-6739f50\architecture\落地计划\audit-6739f50-20260909\presentation\playmode-rerun.log'
```

冻结仓 90 个已跟踪 adapter Unity C# 文件与独立副本逐项 SHA-256 全部匹配（90/90）。六个
Unity 插件 DLL 来自本轮冻结仓 `bin/_check_artifacts/bin/<assembly>/release`，与对应构建产物
匹配；它们不是冻结仓 `adapters/unity` 路径上的另一组六 DLL，也不是正式 ZIP。逐项哈希见
[baseline-hashes.log](baseline-hashes.log)。

## 导航回归与边界

实现副本见 [UnityNavigation2D.cs](UnityNavigation2D.cs)，原有测试副本见
[UnityNavigation2DTests.cs](UnityNavigation2DTests.cs)。

### NAV-110-01：端点格中心受阻

旧坐标：阻挡矩形 `[(0,0),(1.2,1)]`，`from=(1.21,0.5)`，`to=(1.8,0.5)`。

1.11 基线已有 Unity 测试期望路径非空，并检查首尾点精确相等、每段 `Raycast` 清晰。
真实结果通过；日志显示两端 `IsWalkable=true`、直线 `Raycast=null`、路径长度为 5。

源码 `FindPath` 在 [UnityNavigation2D.cs:127](UnityNavigation2D.cs#L127) 先检查真实端点，
[UnityNavigation2D.cs:166](UnityNavigation2D.cs#L166) 通过 `ResolveEntryCell` 选择接入格，
故旧的“端点格中心受阻即提前返回空路径”已被本次代码修复覆盖。

### NAV-110-02：薄墙细网格兜底

旧坐标：阻挡矩形 `[(0.1,-0.5),(0.15,0.5)]`，`from=(-1,0)`，`to=(1,0)`；手工绕路
`(-1,0) -> (-0.2,-0.6) -> (0.2,-0.6) -> (1,0)` 的每段 `Raycast` 均为 `null`。

1.11 基线已有 Unity 测试通过，返回非空路径且所有返回段均通过 `Raycast`。收尾防线在
[UnityNavigation2D.cs:197](UnityNavigation2D.cs#L197) 失败时进入 `FindPathWithFineGrid`，
该细网格入口在 [UnityNavigation2D.cs:217](UnityNavigation2D.cs#L217)。旧薄墙误判已被
本次实现覆盖。

### NAV-111-01：窄直达通道仍被拒绝（P2，已确认）

新增审计坐标使用两个矩形：`bottom=[(-1,-1),(1,0)]`、`top=[(-1,0.1),(1,1)]`，通道宽
0.1；`from=(-0.5,0.05)`、`to=(0.5,0.05)`。

两端逐点 `IsWalkable=true`，两端直线 `Raycast=null`，所以按当前点导航契约存在清晰直达段；
但 `FindPath` 返回 `null`。`ResolveEntryCell` 在 [UnityNavigation2D.cs:532](UnityNavigation2D.cs#L532)
只考察当前格和 8 邻格，窄通道的这些采样中心都落在上下阻挡内，无法接入 A*；该路径也不触发
薄墙收尾后的细网格兜底。

这是修复 NAV-110-01 后仍未覆盖的可达性边界，确认为 `NAV-111-01` P2。证据见
[navigation-probes.xml](navigation-probes.xml)、[navigation-probes.log](navigation-probes.log)
（原件未归档，归档时遗漏、候选产出目录已搜索确认找不回，结论以本节引用的 3 通过/1 失败数值及
随附的 XML 为准）和 [Audit6739f50NavigationProbes.cs](Audit6739f50NavigationProbes.cs)。该审计
探针一组为 3 通过、1 失败；失败是预期可达断言失败，不是测试资产错误。

### NAV-110-04：细网格较宽子格墙

新增边界使用宽度 0.2 的阻挡矩形 `[(0.1,-0.5),(0.3,0.5)]`，同样给出下方手工绕路
oracle。返回路径非空且逐段 `Raycast=null`，说明现有细网格兜底对该有限场景有效；不据此
宣称覆盖任意障碍尺寸。

## 空间查询边界

[UnitySpatialQuery.cs](UnitySpatialQuery.cs) 的 `QueryRadius` 在 [UnitySpatialQuery.cs:80](UnitySpatialQuery.cs#L80)
按查询半径先调用 `CandidatesNear`，而候选 bucket 计算在 [UnitySpatialQuery.cs:185](UnitySpatialQuery.cs#L185)。

### SPATIAL-111-01：跨 bucket 且实体自半径应扩大的查询（P2，已确认）

真实 Unity 审计探针登记实体 `audit.6739f50.spatial.bucket_edge` 于 `(4.1,0)`，实体半径
`0.7`；查询中心为 `(3.5,0)`、查询半径 `0.1`。几何距离为 `0.6`，按精确谓词应满足
`0.6 <= 0.1 + 0.7 = 0.8`。

`QueryRect` 小框 oracle 找到实体（`rectOracleCount=1`），但 `QueryRadius` 因候选范围只覆盖
bucket 0，未访问位置在 bucket 1 的实体（`radiusCount=0`）。因此确认为
`SPATIAL-111-01` P2。有效证据见 [spatial-probe-v2.xml](spatial-probe-v2.xml)、
[spatial-probe-v2.log](spatial-probe-v2.log) 和 [Audit6739f50SpatialQueryProbes.cs](Audit6739f50SpatialQueryProbes.cs)。
早期 `spatial-probe.xml` 只记录了非法 Id 的 fixture 错误，已明确排除。

## 装备表现与读档边界

`EquipmentVisualSource` 仍在 [EquipmentVisualSource.cs:68](EquipmentVisualSource.cs#L68) 至
[EquipmentVisualSource.cs:70](EquipmentVisualSource.cs#L70) 订阅 `item.added/equipped/unequipped`，
并在 [EquipmentVisualSource.cs:125](EquipmentVisualSource.cs#L125) 与
[EquipmentVisualSource.cs:134](EquipmentVisualSource.cs#L134) 维护实例到外观的活字典；它没有
`save.loaded` 订阅。`SpriteViewBase.OnEvent` 在 [SpriteViewBase.cs:176](SpriteViewBase.cs#L176)
只处理装备和卸装事件，[SpriteViewBase.cs:195](SpriteViewBase.cs#L195) 才更新纸娃娃覆盖。

`ViewBinder` 在 [ViewBinder.cs:125](ViewBinder.cs#L125) 订阅 `save.loaded`，但
[ViewBinder.cs:234](ViewBinder.cs#L234) 的处理范围是实体存在性对账：它销毁陈旧 View、为快照
中缺失的实体补建 View；已存在同一 entity id 的 View 会按源码幂等跳过，不负责把现有 View
重新发送装备事件。

因此静态链路仍有一个清晰边界：同图读档抑制装备事件时，逻辑 `EquipmentHost` 已换装，
现有 View 的装备层依赖后续 `item.equipped` 事件。`EquipmentVisualSource` 已提供
`ReplayEquippedForUnit`（[EquipmentVisualSource.cs:74-115](EquipmentVisualSource.cs#L74)），
但当前缺少 `save.loaded` 触发现有 View 重放的装配接线。
本轮没有再造 Shell 级真实 SaveSystem/EquipmentHost/既有 View 组合探针，未把这条静态边界
升级为已确认 P2，也没有把 core 代理已验证的 `EquipmentWeaponStyleSource` 结论外推到 Mesh、
HUD 或屏幕外观。该链路仍需独立真实读档和 View 层证据。

相邻修复 `EquipmentWeaponStyleSource` 已接入 `save.loaded` 清缓存，源码在
[EquipmentWeaponStyleSource.cs:75](../../../../presentation/vfx_sfx/core/EquipmentWeaponStyleSource.cs#L75)
至 [EquipmentWeaponStyleSource.cs:78](../../../../presentation/vfx_sfx/core/EquipmentWeaponStyleSource.cs#L78)；
本轮不重复 style probe。

## Unity 门禁结果

完整 EditMode XML 为 `67` 条：1.11 基线已有测试共 `62/62` 通过；新增审计探针共 5 项，
其中 3 项通过、NAV-111-01 与 SPATIAL-111-01 两条缺陷断言失败，批次计数为
`65 passed, 2 failed`。探针内的前置断言不另计测试。
证据见 [editmode-full.xml](editmode-full.xml) 与 [editmode-full.log](editmode-full.log)。

PlayMode 首次完整批次为 `197/265`，原因是副本尚未有 StreamingAssets 内容；补齐数据后
`playmode-full-v2.xml` 为 `227/265`，剩余 38 条依赖 build 生成的 scene/nav 资源。模板启动
补测 `playmode-template.xml` 为 `4/4`，确认补齐路径可进入场景；随后完整最终批次
[playmode-full-final.xml](playmode-full-final.xml)（原件未归档，归档时遗漏、候选产出目录已搜索
确认找不回，结论以本节引用的 `265/265` 数值为准）为 `265/265`，日志见
[playmode-full-final.log](playmode-full-final.log)（原件未归档，同上）。前两次批次保留作副本
准备诊断，不计产品缺陷。

## 结论与限制

本轮确认两项新增 P2：`NAV-111-01` 窄通道接入失败、`SPATIAL-111-01` 实体自半径跨 bucket
查询漏项。旧 NAV-110-01 与 NAV-110-02 坐标回归已通过，不能把它们重复上报为当前缺陷。

动画剪辑隔离、资源生命周期缓存、Destroy/Unload、多个 Animator state 共享 clip、异步加载、
实际 Animator override 匹配，以及装备层读档后的实际屏幕外观，本轮没有完整端到端证据；不以
静态线索凑缺陷数。

未执行完整独立版构建、IL2CPP、长时性能压测或用户验收；Unity 验证仅覆盖当前独立副本和
本记录列出的 EditMode/PlayMode 门禁。报告只写审计目录与独立副本，未修改产品源码、原仓
或冻结仓既有测试，也未提交 commit。
