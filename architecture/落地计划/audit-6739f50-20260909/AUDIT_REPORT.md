# ws-game 1.11.0 审计报告

审计基线为冻结工作树 `D:\workespace\ws-game-review-6739f50`，HEAD `6739f50e44ba39a023c6209af2673aaf6a1c1fdc`，版本 `1.11.0`。本审计未写原仓 `D:\workespace\ws-game`；结束检查发现原仓存在外部并发的未提交 `editor/docs/编辑器产品文档.md` 及对应 `.html` 改动，该改动不属于本审计证据。冻结仓 tracked diff 为空；本报告只汇总已留存证据，不修改产品、既有测试、registry 或提交。

## 结论总览

当前架构的分层、数据驱动、独立 Core 与 Unity adapter 边界清楚，工具链和公开 API 交付路径可复现。风险集中在生产装配生命周期、SaveLoaded 后运行时派生状态与界面缓存重建，以及几何边界处理。下面五项为主审确认的当前 P2；这属于审计判断，不是性能实测：

|编号|定级|代码定位与复现场景|影响|修复与验收|
|---|---|---|---|---|
|CORE-111-01|P2|失败读档切换不同 power 集合后回滚：`PowerHost` 集合恢复，但 [PlayerVitalsPersistable.cs:59-63](../../../core/gameplay/assembly/PlayerVitalsPersistable.cs#L59) 只覆盖 Health；派生重建/资源回滚入口还在 [RulesAssembly.cs:728](../../../core/rules/assembly/RulesAssembly.cs#L728)；真实 A 在 Mana=30、`InCombat=true`，B 读档失败后 Mana=0，`Advance(1)` 后为10，见 [followup-core-probe.log:44-46](core/logs/followup-core-probe.log#L44)|失败回滚后资源当前值和战斗状态丢失，后续 tick 继续产生分叉|对参与回滚的 power 保存/恢复当前值及必要运行态快照；验收 A/B 不同 power 集合失败后 Mana=30、`InCombat=true`，推进不改变为10，且不重复事件|
|UI-111-01|P2|同图成功 RestoreFromSlot(B) 后即时 `UiDataSource` 已是 B，但 [InventoryViewModel.cs:49](../../../presentation/ui/core/ViewModels/InventoryViewModel.cs#L49) 的缓存仍为 A，见 [followup-core-probe.log:58-63](core/logs/followup-core-probe.log#L58)|玩家看到旧背包/装备，手动 Refresh 才一致|让 VM 订阅统一 `SaveLoaded` 或由装配根显式刷新；验收同图 A→B 后无手动刷新即 entries/equipped 与 B，重复 Load 不重复订阅|
|NAV-111-01|P2|窄通道两端精确点可行走、直线 Raycast 清晰，但 [UnityNavigation2D.cs:166](../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs#L166) 的 `FindPath` 返回 null；[navigation-probes.log:5076](presentation/navigation-probes.log#L5076)（原件未归档，归档时遗漏、候选产出目录已搜索确认找不回，结论以本行引用行号及随附的 XML 为准），失败用例记录在 [navigation-probes.xml](presentation/navigation-probes.xml)|合法窄通道被判不可达，AI/移动请求无法生成路径|修正精确端点/细网格接合与窄通道判定，保持 Raycast 与 A* 同一通行规则；验收该用例返回非空路径且逐段 Raycast 清晰|
|SPATIAL-111-01|P2|查询中心 `(3.5,0)`、查询半径 `0.1`；实体中心 `(4.1,0)`、自身半径 `0.7`，中心距 `0.6 ≤ 0.8`，但 [UnitySpatialQuery.cs:80](../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnitySpatialQuery.cs#L80) 的跨 bucket 半径查询漏项（radiusCount=0、rect oracle=1），见 [spatial-probe-v2.log:561](presentation/spatial-probe-v2.log#L561)|范围技能/感知漏掉边界附近实体，目标选择与矩形 oracle 不一致|候选 bucket 范围按查询半径与实体自身半径扩张并去重；验收 `QueryRadius` 返回该实体且与 Rect oracle 一致，覆盖边界/移动/注销|
|TP-111-01|P2|公共 Dialog 连续选择传送 B 后仍处 Loading，期间选择 A 返回 true；[GameplayAssembly.cs:1431](../../../core/gameplay/assembly/GameplayAssembly.cs#L1431) 在场景请求前写入玩家字段，post-load 完成后 Router 在 B，但 WorldSim 玩家实体仍 Map A/pos 1,2，见 [teleport-loading-boundary.log](core/logs/teleport-loading-boundary.log) 与 [TeleportLoadingBoundaryProbe.cs](core/repro/TeleportLoadingBoundaryProbe.cs)|场景路由与玩家实体位置/地图字段分叉，后续规则和表现读取到不一致世界|在路由接受前不提交玩家地图/位置，或将拒绝/排队/失败恢复做成原子流程；验收策略任选但最终 Router、WorldSim 玩家实体 MapId/位置一致，重复选择不产生第二次未完成请求|

已有 View 装备同图读档目前只有静态链路边界：本轮未构造对应 Runtime 探针，未将其升级为当前 P2；它列入下一轮独立验收。

建议按以下顺序修复并验收：先收口回滚资源快照与 Loading 期间传送提交的原子性（CORE-111-01、TP-111-01），再重建 UI 缓存（UI-111-01），随后处理导航窄通道和空间查询跨 bucket 几何边界（NAV-111-01、SPATIAL-111-01），最后同步文档与能力计划。各项验收条件已列于上表，修复后应同时回归既有 SaveLoad、移动、目标查询和表现入口。

## 文档—代码结论

逐章矩阵见 [doc-code-matrix.md](docs-project/doc-code-matrix.md)，工程/文档发现见 [project-findings.md](docs-project/project-findings.md)。当前分类如下：

- 未实现或未覆盖：编辑器工具、`ISkillHost.FindUnits`、位移轨迹碰撞、VFX anchor 持续跟随、天赋激活/持久化、孤儿记录检测、nested teleport 元素/ref 完整性、全资源池当前值默认持久化、导航跨帧预算和空间查询完整索引化。
- 已实现未默认接线：`DisplayMapCoverageRule` sources、Spawn 查询、owner/day/vendor/time provider、回放及可选 model/weapon-style/VFX/SFX 消费。
- 游戏责任：TargetPoint 的具体地图点解析/点选、内容数据、完整新局重置、真实资产与 HUD/mesh 消费，以及 Health 之外资源池是否跨档保存。
- 明确非目标：ATB、`day_cycle`，以及 Power/Summon 离散步是否按游戏策略换算推进。Summon/Loot 的连续-only处理器边界不等于整个离散模型未实现。
- 文档更新项：10 §2.5 的 Power 当前值泛化；`ArchSchemas.cs`/archetype README 的 skill/aura 旧前提；PowerTickHandler/IPowerDiagnostics、UnityViewFactory 和 UnitySpatialQuery 过时或过度宣称的判断记录。

## 已关闭旧项

1.10 方向移动导航检查、同 tick 多 move 固定 dt、WorldMap 四字段类型登记、Save/Load 事件抑制及旧用例本身已修复；不能据此笼统宣称全部派生重建完成，当前 power/UI/导航/空间/传送 P2 仍按上表处理。06 当前 25、127、224 已统一为 `target_shape_ref` 只经 chain 使用 Shape；旧 direct Shape 结论排除。旧 API 兼容别名编译通过，归档项目使用 `FrameworkRoot` 参数化，不存在当前 API 断裂。

## 验证与发布证据

唯一 check 命令见 [validation.md](docs-project/validation.md)，`-SkipUnity` exit 0，14 PASS / 6 SKIP / 0 FAIL；.NET 六工程 2547 通过（685/107/354/404/498/499，含 Perf），Python pytest 92 passed / 2 skipped。原仓正式 ZIP/lock 六 DLL 两插件路径 12/12 匹配，哈希和整体 SHA-256 见 [release-zip-lock-hash.log](docs-project/release-zip-lock-hash.log)。API 兼容 probe 针对冻结仓本轮重建 DLL，最终编译 exit 0、仅 2 条预期 CS0618，见 [api-compat-current-rebuild-final.log](docs-project/api-compat/api-compat-current-rebuild-final.log)。 Core 三个独立 no-build 复跑均 exit 0，证据为 [reviewer-followup.log](core/logs/reviewer-followup.log)、[reviewer-movement.log](core/logs/reviewer-movement.log)、[reviewer-teleport.log](core/logs/reviewer-teleport.log) 及对应 `.exit.txt`。归档说明见 [EVIDENCE_README.md](EVIDENCE_README.md)，文件清单见 [evidence-manifest.txt](evidence-manifest.txt)，同级证据包为 [audit-6739f50-20260909-evidence.zip](../audit-6739f50-20260909-evidence.zip)（原件未归档，打包步骤未落地、候选产出目录已搜索确认找不回，交付以本目录内实际留存文件与 `evidence-manifest.txt` 记录的 SHA256 为准）。

Unity 证据需分阶段理解：EditMode 既有基线 62/62 通过；新增 5 个探针中 3 个通过、2 个断言失败，合计 67 项、65 通过、2 失败。最终 PlayMode XML 为 265/265 通过、0 失败、0 跳过，见 [playmode-full-final.xml](presentation/playmode-full-final.xml)（原件未归档，归档时遗漏、候选产出目录已搜索确认找不回，结论以本节引用的 265/265 数值为准），资源修正后的模板 4/4 也通过，完整过程见 [presentation-findings.md](presentation/presentation-findings.md)。有效 Unity 探针副本的六 DLL 均来自冻结仓 `bin/_check_artifacts/bin/<assembly>/release` 且与冻结仓匹配，来源核对见 [reviewer-unity-source-verification.txt](reviewer-unity-source-verification.txt)；另一次 adapter 目录构建产物哈希差异不代表测试途中换 DLL；EditMode 的两项新增失败仍按上表定级。

## 工程整体判断与边界

架构分层、数据驱动、独立 Core/Unity adapter 和版本锁为长期维护、替换引擎与发布复核提供了良好基础。当前风险聚集在生产组合根的生命周期收口、SaveLoaded 后派生状态/界面缓存的显式重建、以及路径/空间查询几何边界；测试总量虽大，仍需为这些跨模块场景建立针对性覆盖。本判断不声称发生了性能实测退化。

本轮未执行 standalone、IL2CPP、长时压力和完整真实美术验收；PlayMode 已包含既有竖切 SaveLoad，新增已有装备外观场景未构造独立 Runtime 探针，列为下一轮验收。Unity 全量门禁仅在 check.ps1 的 `-SkipUnity` 步骤中跳过，Unity 定向证据只按各自 XML/log 解释。






