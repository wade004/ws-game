# ws-game 1.10.0 审计报告

## 1. 审计基线与结论口径

本报告基线为冻结工作树 `D:\workespace\ws-game-review-ac3b622`，HEAD
`ac3b622041c348e87a469959c09d8a541a7c1351`，版本 `1.10.0`。原仓
`D:\workespace\ws-game` 只读，仅用于读取正式 ZIP/lock；未启动 registry/release，未修改产品源码、既有测试或提交。

当前结论分为源码静态证据、真实 .NET 探针、Unity 定向测试和尚未完成的完整链路。旧审计文字不直接继承，只有当前路径中的源码、日志和 XML 才作为本轮证据。

## 2. 已确认问题

| 编号 | 定级 | 结论 | 证据 |
|---|---|---|---|
| CORE-110-01 | P2 | 失败读档回滚后派生状态不恢复。race 子案例中字段已回 A，但 stat/aura 仍为 B；equipment/known_skills 子案例中装备已回 A，但 max/health 仍为低状态 100/100，应为 200/200。两个子案例合并为同一回滚派生重建缺口。 | [core-findings.md](core/core-findings.md)、[followup-core-probe.log](core/logs/followup-core-probe.log) |
| CORE-110-02 | P2 | 同图跨职业 Load 只覆盖新旧共同 base 键；旧职业独有基础属性键残留，旧 power type 也未按新职业集合清理。探针实际为 legacy=5、mana=true，B 基准应为 legacy=0、mana=false。 | [core-findings.md](core/core-findings.md)、[followup-core-probe.log](core/logs/followup-core-probe.log) |
| CORE-110-03 | P2 | 同一连续 tick 提交多条目标 move 会逐条推进：speed=10、dt=0.1 时 1/2/3 条意图分别得到 x=1/2/3；Stop 后再提交两条得到 x=2。当前公共契约没有合并或每 tick 单意图限制。 | [movement-boundary-probe.log](core/logs/movement-boundary-probe.log) |
| NAV-110-01 | P2 | Unity 导航端点契约在端点精确接合场景拒绝可达路径：端点所在格中心被阻挡，但请求端点直线 Raycast 可通行，FindPath 返回 null。 | [navigation-probes-final.xml](presentation/navigation-probes-final.xml) 的 CandidateA |
| NAV-110-02 | P2 | Unity 网格 A* 薄墙场景返回 null，而手工绕路的各段 Raycast 均可通行，证明当前实现拒绝存在的可达绕路。 | [navigation-probes-final.xml](presentation/navigation-probes-final.xml) 的 CandidateB |
| PRES-110-01 | P2 | 同图真实 A/B 武器存档恢复后，Equipment 已为 B，但 `EquipmentWeaponStyleSource` 仍为 A；Save 抑制期间仅订阅 equip/unequip，未订阅 SaveLoaded，导致缓存失效未清理。该确认只覆盖 style 缓存，不外推模型 mesh/HUD 画面。 | [followup-core-probe.log](core/logs/followup-core-probe.log)、[FollowupCoreProbe.cs](core/repro/FollowupCoreProbe.cs) |

CORE-110-01 的共同根因是 `IDerivedStateRebuilder` 只在成功 Load 段回调，回滚不调用派生重建钩子；不能只修某个装备段。CORE-110-02 还需要对旧职业 base 键和 PowerTypes 做明确移除/重建策略。CORE-110-03 需要明确同 tick 多 move 的公共语义后再改实现或契约。

## 3. 1.9 修复复核与文档代码矩阵

1.9 的 CORE-180-01/02/03 在 1.10 当前真实 fixture 中复核通过：成功 Load 的 rating=10、power max/health=200；正向回滚顺序恢复等级与装备；A/B 独立 race 存档同图恢复后为 stat=91、AuraA=false、AuraB=true。证据见 [core-findings.md](core/core-findings.md) 的“上轮三项修复”及 [followup-core-probe.log](core/logs/followup-core-probe.log)。

现行文档—代码矩阵覆盖 00～14、ADR-0001～0017，并将能力分为默认已接线、已实现未默认接线、未实现、游戏责任、明确非目标和文档更新。完整矩阵见 [doc-code-matrix.md](docs-project/doc-code-matrix.md)。关键边界如下：

- `TargetPoint` 是框架提供的可空字段，地面点选到具体目标由上层消费；根 README 与能力索引已同步。
- `target_shape_ref` 只引用 `target.chain_def`，链内使用 Shape；RulesSchemaCatalog、CastPipeline、06 和 skill README 一致。
- 编辑器、天赋运行时激活/持久化、`ISkillHost.FindUnits`、位移轨迹碰撞、VFX 持续跟随、孤儿记录检测仍未实现。
- 采集时钟、Quest.Update、owner/day/vendor provider、ReplayPlayer、FeedbackRuleValidator、DisplayMapCoverageRule 属于已实现但需显式驱动/登记；null 或未登记不自动定 bug。
- `regions`、`teleport_points`、`music_ref`、`allowed_difficulties` 仍只有结构声明，未登记 WorldMap TableSchema；这是校验能力待补，不能据此否定运行时部分消费。
- `day_cycle` 与 ATB 是明确非目标/预留扩展。离散基础模型已实现；`SummonTickHandler` 的连续-only 局部边界文案已修，`LootExpiryTickHandler.cs:35` 仍留有旧“本项目暂不启用”注释，应勘误。
- 方向移动实现当前只检查单位间阻挡后写位置，未调用 `INavigation2D.IsWalkable/Raycast`；05 的文字需明确补齐导航阻挡还是正式限制契约，暂不归为游戏责任。

文档维护项还包括 `MoveStopReason.BlockingChanged` XML 注释把 `PathFailurePolicy.Stop` 失败混入 BlockingChanged；实际 `HandlePathFailure` 触发 `PathFailed`。旧 ADR 算法文字已经明确废止，只列低优先级维护建议，不列现行冲突。

## 4. 导航公共契约边界

1.10 已把 `INavigation2D` 的 SetBlocking/Clear/GetBlockingVersion、精确端点、零长度路径、内部相交规则和禁止切角写入 02，并在 Stub/Unity/conformance 中实现；MovementHost 处理顺序为停止、移动、阻挡重验、推进。

静态契约与核心单测通过不等于 Unity 端到端可达性成立。本轮导航定向 XML 已确认 NAV-110-01/02 两项 P2；具体修复验收应要求端点直线可通行时允许合法接合、薄墙可绕行且每段 Raycast 无阻挡，并补充端点中心被阻挡的边界测试。

## 5. 验证结果

唯一门禁命令为 `check.ps1 -SkipUnity`，日志见 [check-skipunity.log](docs-project/check-skipunity.log)，结果为 **14 PASS / 6 SKIP / 0 FAIL**，退出码 0。

- .NET Release build 0 warnings/0 errors；六工程测试 2533 passed、0 skipped（Foundation 683、Numbers 107、Carriers 347、Rules 404、Presentation.Common 496、Gameplay 496，含 Perf）。
- Python toolchain 92 passed、2 skipped；合并数据 60 tables/285 records/0 errors/0 warnings/1 override；framework 数据 5 tables/124 records/0 errors/1 warning。
- 事件常量 90、占位资产 92/92、sample 资产 0 issues、版本/包清单/三个 npm dry-run 均通过。
- Unity 编译、EditMode、PlayMode、standalone smoke、消费方演练在该门禁中全部按 `-SkipUnity` 跳过。

独立 Unity 证据中，[editmode-full-v2.xml](presentation/editmode-full-v2.xml) 为 **62/62**，包含 60 条既有测试与 2 条导航复现；该批次使用初始 adapter 插件 DLL。[navigation-probes-final.xml](presentation/navigation-probes-final.xml) 为 2/2 通过，但这两条测试的通过含义是复现并锁定缺陷行为，不是产品行为通过。PlayMode 分批结果为 **完整批次 262 通过、1 失败，独立补测 1/1 通过**：完整批次在检查重建 DLL 下唯一失败为隔离副本缺 `TestDataQuestDayProvider`，补齐后该项单测通过；此前 256 到 262 的变化来自 TestData 补齐，与 DLL 替换无关，也不是单轮 263 全绿。旧 `playmode-full.xml`/`playmode-discrete.xml` 的混合失败结果不作为当前最终结论。

## 6. 发布、兼容与证据边界

原仓正式 `ws-game-1.10.0.zip` SHA-256 为
`60b98d28e33785b40744af1f21dff2b873b589c5810b81e37a7dc557a1c9fb8b`，lock SHA-256 为
`9c187c89f0baf5ca03e3127b7efd8c0cd6f2fbc7350e72309f34285bbde709dc`；两插件路径中的六 DLL 均与 lock 匹配。逐条 hash 与命令边界见 [validation.md](docs-project/validation.md)。

冻结仓 `dist/1.10.0` 是 `-SkipUnity` 本轮重建快照，不能冒充原始发布 ZIP；构建路径、Unity 元数据、审计 untracked 状态可能使字节不同。旧 API 探针按当前重建 DLL 编译成功，current17-only 按 README 传 `FrameworkRoot` 后也成功；无参数失败是预期缺少参数，不再列工程缺陷。旧 e070 归档链接真实缺失 9 项，作为证据归档 P3，详见 [archived-e070-link-audit.log](docs-project/archived-e070-link-audit.log)。

## 7. 优先修复与验收标准

1. CORE-110-01：失败回滚完成后按与正常 Load 相同的依赖顺序重建 race/class、equipment 派生、Power max 和 vitals；验收字段、装备、stat/aura、max/health 全回到 A，且抑制期间不泄漏业务事件。
2. CORE-110-02：跨职业真实 A/B fixture 中移除旧职业独有 base 键、清理旧 power type，再应用新职业完整集合；验收 legacy=0、mana=false 且共同键与 B oracle 一致，无重复 aura/power 注册。
3. CORE-110-03：固定 dt 下每个单位每个 tick 只积分一次；策略可选合并或最后一条生效，但同目标 1/2/3 条意图应得到同一位移，且保留 Stop 语义。验收文档、事件和回归测试一致。
4. NAV-110-01/02：修正 Unity 端点接合和网格绕行；验收精确端点、直接 Raycast、薄墙合法绕路及各路径段 Raycast 全部一致，并在 clean Unity PlayMode 通过。
5. PRES-110-01：在 `SaveLoaded` 后清理或显式 reconcile `EquipmentWeaponStyleSource` 缓存；验收 A/B 真实存档恢复后 Equipment、style source 与 B oracle 一致，重复读档不复用 A，且不外推 mesh/HUD 画面。
6. 文档与工具链：修正 MovementHost/XML 与 LootExpiry 注释，补 05 四字段 schema；最终证据收齐后生成排除 bin*/obj* 的 manifest 和审计证据包。

## 8. 旧项复核与未验证边界

上轮已修项在当前 1.10 fixture 中保持关闭：成功 Load 的 rating/max-health 重算、正向回滚的等级与装备恢复、A/B race 存档和 AuraHandleLedger 释放均有 core 证据。旧“成就误计数原场景”与“普通等级字段同步”结论已按原场景复核，不外推为全部事务派生状态已修。

当前确认的三项 CORE-110 缺口是本轮新增真实 fixture 结果，与上述成功 Load/正向回滚修复并不矛盾。CORE-110-01 应覆盖 race、equipment、known_skills 依赖顺序；CORE-110-02 应覆盖跨职业独有 base 键及 power 集合；CORE-110-03 应覆盖连续 tick 的多意图预算。

EditMode clean XML [editmode-full-v2.xml](presentation/editmode-full-v2.xml) 为 62/62，包含 60 条既有测试和 2 条导航复现；[navigation-probes-final.xml](presentation/navigation-probes-final.xml) 的 2/2 是复现缺陷行为的测试通过，不能解释为导航产品行为已通过。全量 Unity 门禁仍由 `-SkipUnity` 跳过。

PlayMode 的 [playmode-full-final.xml](presentation/playmode-full-final.xml)（原件未归档，归档时遗漏、候选产出目录已搜索确认找不回，结论以本节引用的 262/263+1/1 数值为准）为完整批次 **262 通过、1 失败**，失败已定位为隔离副本缺少 `TestDataQuestDayProvider`；[playmode-questday-final.xml](presentation/playmode-questday-final.xml) 的独立 1/1 复测已通过，因此按分批口径记录 262+1 通过，不宣称单轮 263 全绿。EditMode v2 与导航最终探针使用初始 adapter 插件 DLL，PlayMode v2/final 使用冻结仓 `bin/_check_artifacts` 检查重建 DLL；此前 256 到 262 的变化来自 TestData 补齐，与 DLL 替换无关。带 `-quit` 且无有效 XML 的最初导航运行不纳入证据。PRES-110-01 的 style 缓存问题已由真实 A/B Save+RestoreFromSlot fixture 确认；修复应清理或显式 reconcile 缓存并回归既有单位，但不外推实际模型 mesh/HUD 画面。完整 Gameplay RestoreFromSlot、Presentation 与 Unity 画面链仍是未验证边界，本报告不预写通过。

## 9. 证据索引与限制

本报告的文档结论见 [doc-code-matrix.md](docs-project/doc-code-matrix.md) 和 [project-findings.md](docs-project/project-findings.md)；门禁、环境、ZIP/lock、兼容探针和旧归档存在性见 [validation.md](docs-project/validation.md)。Unity 分批证据与 DLL 来源见 [presentation-findings.md](presentation/presentation-findings.md)。所有链接以冻结审计目录内实际留存文件为准。

冻结仓的 ignored `bin*`/`obj*` 与 `dist/1.10.0` 只服务复现和构建，不代表正式发布产物；正式发布证据只采用原仓只读 ZIP/lock 及其六 DLL hash。交付文件清单见 [evidence-manifest.txt](evidence-manifest.txt)，审计证据包见 [audit-ac3b622-20260909-evidence.zip](../audit-ac3b622-20260909-evidence.zip)（原件未归档，打包步骤未落地、候选产出目录已搜索确认找不回，交付以本目录内实际留存文件与 `evidence-manifest.txt` 记录的 SHA256 为准）；清单排除了所有路径段以 `bin` 或 `obj` 开头的构建输出，且不对清单自身计算条目。

本报告保留完整批次与独立补测的真实口径；PRES-110-01 已确认但仅覆盖 style 缓存。完整 Gameplay RestoreFromSlot、Presentation 与实际游戏画面链仍未执行，不能以本报告替代这些验收；本报告不改变产品源码、既有测试、原仓或 registry。

## 10. 证据解释规则

- .NET 2533 passed 和 Python 92 passed 证明当前冻结仓门禁与工具链可执行，不证明 Unity 运行时或游戏内容已经完整接入。
- `-SkipUnity` 产生的 6 个 SKIP 是有意的门禁范围，不计为失败；定向 EditMode/XML 的 62/62 也不覆盖完整 PlayMode 画面链。
- CORE 探针日志保留输入、预期和实际字段；CORE-110-01/02/03 已由主审确认，探针通过表示复现了缺口，不能改写成产品行为通过。
- NAV XML 的两条测试通过表示复现稳定、测试断言成立；P2 定级来自真实拒绝路径及其源码/几何对照。
- 原始 ZIP/lock 的六 DLL hash 匹配是发布同源证据；冻结仓重建 DLL 仅用于兼容探针和本地验证。
- current17-only 无参数失败对应缺少 `FrameworkRoot`，参数化后成功；这不是旧的绝对 HintPath 工程缺陷。
- 旧 e070 链接缺失属于归档证据完整性 P3；源码行号链接经剥离行号后存在，不应误报代码文件缺失。
- 可选回调为空、TargetPoint 目标解析、Quest/provider/采集时钟和新局 reset 均按接入边界记录，不单独升级为框架缺陷。
- 地图四字段列为 schema 校验待补；运行时已有部分消费不能替代 TableSchema 登记与校验。
- 方向移动导航边界列为文档/契约待决，需正式选择补检查或收窄契约，当前不把它归为游戏责任。
- 交付时应以本报告引用的实际路径和最终 manifest 为准；任何未留存的口头结果不得进入最终定级。
