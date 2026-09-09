# v1.8.0 冻结基线总审计报告

## 基线与范围

- 冻结仓：`D:\workespace\ws-game-review-e070e3f`。
- 版本：`1.8.0`；HEAD：`e070e3fc3b8ec992183590aab773d346fe9ab211`。
- 原仓 `D:\workespace\ws-game` 只读，仅用于核对原始发布 ZIP/lock；未修改产品、既有测试、registry 或提交。
- 本报告汇总 docs-project、core 与 presentation findings；主代理负责最终审核。当前 clean Unity 副本的定向 PlayMode 结果见 presentation 报告，旧混版副本结论已排除。

## 结论总览

当前确认三项 Core 缺陷：

1. **CORE-180-01，P1：成功 Load 的内部同步事件被抑制丢弃。** 实际 `load_status=Loaded`，等级/实体等级恢复到 10，但 Rating 保持 5（预期 10）；装备恢复为 true，但 Power max/health 保持 100（预期 200）。显式 `DispatchPending` 后仍未重算。
2. **CORE-180-02，P2：后段持久化失败时逆序回滚先恢复 Equipment、后恢复 Progression。** 低等级有效 progression 与空 equipment 文档触发后段失败后，等级恢复到 2，但原先高级装备未重新装备，库存仍为 0。
3. **CORE-180-03，P2：同图 `RestoreFromSlot` 只切换种族字段，未清理 A 的修正或应用 B 的修正。** 两个独立有效存档验证：B 基准 stat=91；A 加载 B 后 `RaceId=B`，但 stat 仍为 61，`auraA=true`、`auraB=false`。证据为 `core/evidence-logs/followup-core-probe-race-final.log`；不扩大到 Archetype/职业字段。

职业/Archetype 字段切换及其修正是否跨存档残留仍是静态候选，尚无真实 fixture，不定级。

## 文档与代码边界

| 章节 | 当前对照结论 |
|---|---|
| 00 架构总则 | 引擎无关、数据驱动、固定步与窄契约在 Core 结构中落地。 |
| 01 分层与依赖 | Core、Presentation.Common、adapter 的单向边界通过项目引用保持。 |
| 02 引擎适配层 | 契约和动画剪辑隔离实现存在；clean 副本定向隔离与生产入口通过，非完整 Unity 门禁。 |
| 03 运行时骨架 | Bootstrap、固定步和路由实现；采集时钟仍需宿主注入。 |
| 04 数据管线 | schema、引用登记、validator、导入工具已接线；孤儿记录检测仍未实现。 |
| 05 对象与世界 | Unit/Gobj/WorldSim 与空间登记实现；不把 `FindUnits` 空实现扩大为整个空间查询不可用。 |
| 06 规则层 | 属性、技能、战斗、AI 主干已实现；`FindUnits` 未实现，天赋运行时分配/激活/持久化未实现。 |
| 07 载体层 | 物品、生物、Gobj、召唤与持久化实现；内容选择由游戏负责。 |
| 08 玩法层 | loot/quest/dialog/encounter 等机制存在；Quest 回调为空属于游戏接线责任。 |
| 09 表现层 | 2D/3D、装备/武器动画、关键帧机制存在；clean 副本去重回归 58/58、隔离 8/8、生产入口 7/7 通过；这是定向 PlayMode，不是完整 Unity 门禁。VFX anchor 持续跟随仍未实现。 |
| 10 存档持久化 | 分段、备份、回滚和抑制机制存在；CORE-180-01/02/03 是真实跨段恢复缺陷。 |
| 11 工程测试 | Core/Python/静态门禁通过；Unity 与消费方证据按本轮状态单独记录。 |
| 12 变更流程 | ADR、版本、lock、旧 API 别名和发布脚本存在。 |
| 13 接入指南 | zip/registry 双通道与模板存在；原始 ZIP/lock 同源已核对。 |
| 14 资产模板 | model/equip_visual/anim_set 等合同和样例存在；编辑器未开始实现。 |

分类口径为：有生产装配根调用链即“默认已接线”；机制存在但需注入/登记即“已实现未默认接线”；无运行时逻辑即“未实现”；ADR 明确排除或属于消费方即“非目标/游戏责任”。

## 已确认文档漂移与工程项

- 根 `README.md:9` 仍将地面点选列为框架未实现。当前 `SkillCastRequest.TargetPoint` 已存在，point 到具体目标的消费是上层 AI/玩家辅助施法责任，应更新 README，并与编辑器/天赋真实未实现项分开。
- `architecture/06_规则层_属性技能战斗AI.md:126,223` 现行正文允许 `target_shape_ref` 直接指向 Shape；`RulesSchemaCatalog.cs:161-172` 只登记 `target.chain_def`，`CastPipeline.cs:254-255` 按 chain 调用 `TargetHost.Resolve`。应统一为 chain 内使用 Shape，或明确 direct Shape 尚未实现；这不等同于整个空间查询不可用。
- 旧存档顺序/helper 文档修订已纳入当前基线；不得沿用旧文字推翻当前实现。
- `current17-only/ApiCompatCurrent17Only.csproj` 的绝对 HintPath 指向不存在旧 worktree，归档项目不可移植；这属于工程证据问题，不是当前 API 断裂。旧 API 探针指向本轮重建 DLL 后成功编译。
- 原始 ZIP `ws-game-1.8.0.zip` 中六个 Core DLL 的 SHA-256 与 `ws-game-1.8.0.lock` 全部 MATCH：Foundation `e93028ea5463adb9ad9d6b1f9ef784ad8c29f2cd26439b0034994de057724dca`；Numbers `387592b20c6c98d2970cddec33a32e2dbcc6b06994f95a1ad3d7d82b66f9981a`；Rules `1d7a53c5d9d1530957a2797b9e1be1627f810a146e40c2a7cd1eaa95788ea6e5`；Carriers `a51c54cc8e728631a2bf1e22323f0463d618e25b2cba22dab235bd2bb91cf1db`；Gameplay `4e17a3f8d666e550d5b579450670eed1b2753268d3f9ab351d2cf567588b2294`；Presentation `5f0f49fea34c39087a443260117e3cdb12e42ceb89812524d49c63812224b401`。

## 验证结果

- 唯一一次 `check.ps1 -SkipUnity -LogFile ...\docs-project\check-skipunity.log`：**14 PASS / 6 SKIP / 0 FAIL**，退出码 0。
- .NET 六项目：Foundation 662、Numbers 107、Carriers 329、Rules 404、Presentation.Common 491、Gameplay 491；合计 **2484 passed，含 Perf，0 skipped**。
- Python：pytest **92 passed / 2 skipped**；合并数据 60 tables/285 records；框架数据 5 tables/124 records，1 条 l10n 文本键检查 warning 按设计通过。
- 事件常量 90、占位资产 92/92、sample 资产导入 0 issues，版本/package/lock/CHANGELOG 与三包 dry-run 均通过。
- 全量门禁命令使用 `-SkipUnity`；presentation clean 副本已完成定向 PlayMode 隔离 8/8、生产入口 7/7，合并去重回归 58/58。完整 Unity 门禁、游戏构建、consumer、IL2CPP、性能和表现层同图 Load 漏 `EntityCreated` 的 .NET 视图链仍不在本次定向结论内。旧 factory 覆盖副本及 v2/v3 混版测试不引用。

## 优先修复与验收标准

| 优先级 | 修复目标 | 验收标准 |
|---|---|---|
| P1 | 成功 Load 后完成内部 derived-state 同步 | 真实 `GameplayAssembly` + `SaveSystem.Load` 返回 Loaded 后，等级、Rating、Power max、health 与保存快照一致；显式 drain 不再改变结果；外部业务事件仍遵循抑制语义。 |
| P2 | 后段失败回滚按依赖恢复或事务化提交 | 注入后段 persistable 异常，progression/inventory/equipment/vitals 均恢复原 live 状态；高等级装备重新装备且库存无重复；原有失败段原子性回归继续通过。 |
| P2 | 同图 RestoreFromSlot 重建种族修正 | 清理 A 的 stat modifier/aura，再应用 B 的 stat modifier/aura；字段为 B、stat=91、A aura=false、B aura=true；重复加载不重复叠加。不得把 Archetype/职业字段纳入本项结论。 |
| 文档 | 修正 TargetPoint、direct Shape 与能力索引口径 | README/06/schema/API 三者统一；地面点选标为上层责任，direct Shape 明确支持状态。 |
| 工程 | 提高归档探针可移植性 | current17-only 不再含机器绝对 HintPath，换工作目录仅需参数化 FrameworkRoot 即可 restore/build。 |

## 上轮旧项复核

| 旧项 | 当前结果 |
|---|---|
| AuraHandleLedger 跨来源引用计数 | 已实现并由 RulesAssembly/EquipmentHost 共享，旧探针符合预期。 |
| 装备失败段原子性 | 已修；失败段后原装备状态保持。 |
| 回滚 derived state/等级同步 | 旧成就误计数原场景；普通等级字段同步已修，不代表全事务回滚已修。 |
| 动画事件共享资产污染 | 当前实现按 anim_set 隔离；ADR 旧算法文字仅作废止历史维护项。 |
| model/equip_visual/weapon animation/hit-frame | clean 副本定向 PlayMode 与去重回归通过；完整 Unity 门禁及真实游戏链路仍未宣称通过。 |
| 种族字段切换残留 | CORE-180-03 已确认 P2；Archetype/职业字段仍候选，未定级。 |

## 最终复核边界

- Unity clean 副本定向 PlayMode 已完成隔离 8/8、生产入口 7/7，合并去重回归 58/58；`UnityViewFactory.cs` 与六个 Core DLL hash 已与冻结仓一致，`UnityProcessCount=0`。完整 Unity 门禁及表现层同图 Load 视图链仍未覆盖，旧 factory 覆盖副本及 v2/v3 混版测试不引用。
- 表现层同图 Load 漏 `EntityCreated` 的 .NET 探针源码/日志已独立核对并 exit 0，输出 `status=Loaded entity_present=True binder_views=0 created_views=1`，候选证据到 ViewBinder；完整 `Gameplay.RestoreFromSlot` + Presentation + Unity 画面链尚未覆盖，当前不列为已确认缺陷。
- 召唤与掉落 handler 对 Discrete 步跳过是局部处理器支持状态；框架基础 Discrete 已存在，不应把它与 ATB/day_cycle 的明确非目标混为一类。
- 编辑器、天赋运行时分配/激活/持久化、孤儿记录检测、位移轨迹碰撞、VFX anchor 持续跟随按能力索引记录为未实现；新局完整 reset 与可选回调属于游戏/模板责任。
- 原始发布 ZIP 与 lock 的六个 DLL hash 已逐一核对；冻结仓 `-SkipUnity` 目录的 dirty/hash/meta 差异只作为验证边界，不据此认定发布缺陷。

## 证据索引

- 文档—代码逐章矩阵：[docs-project/doc-code-matrix.md](docs-project/doc-code-matrix.md)，覆盖 architecture 00–14 与 ADR-0001–0017。
- 工程发现：[docs-project/project-findings.md](docs-project/project-findings.md)，含 README TargetPoint、06 direct Shape、归档 API 路径及发布边界。
- 门禁日志：[docs-project/check-skipunity.log](docs-project/check-skipunity.log)，唯一全量 `check.ps1 -SkipUnity` transcript。
- 门禁明细：[docs-project/validation.md](docs-project/validation.md)，含 .NET/Python 分项、环境、原 ZIP/lock hash 核对与验证边界。
- Core 复现总证据：[core/core-findings.md](core/core-findings.md)。
- Core 成功 Load 及回滚日志：[core/evidence-logs/followup-core-probe.log](core/evidence-logs/followup-core-probe.log)。
- CORE-180-03 种族重建日志：[core/evidence-logs/followup-core-probe-race-final.log](core/evidence-logs/followup-core-probe-race-final.log)。
- 表现层当前复核：[presentation/presentation-findings.md](presentation/presentation-findings.md)；副本混版导致旧 v2/v3 结论暂不引用。

## 责任分界

- 框架确认的问题限于 CORE-180-01、CORE-180-02 与 CORE-180-03，修复必须保持事件外部语义、失败段原子性和已有等级同步回归。
- TargetPoint 地面点选、Quest 可选回调、采集时钟、Replay 接入和新局完整 reset 由消费方或模板接线；空回调不自动升级为框架缺陷。
- 编辑器、天赋运行时、孤儿检测、VFX 锚点持续跟随和位移轨迹碰撞按能力索引属于未实现范围。
- ATB 与 day_cycle 是 ADR 明确非目标；召唤/掉落跳过离散步则是局部 handler 支持状态，需单独决定是否补齐。
- 本报告的静态 PASS、Core 探针和发布 hash 证据不能替代 Unity、真实游戏消费方、性能或用户验收。

本报告与证据文件只写入冻结仓审计目录；未修改产品代码、既有测试或提交。
