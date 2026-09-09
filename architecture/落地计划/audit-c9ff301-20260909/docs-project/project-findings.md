# ws-game 1.13.0 条件契约发现

基线为 `c9ff30107413083188c597c0b65cf1691c9dfe9b` / `VERSION=1.13.0`。本文件只把源码与已保存证据映射到契约责任；严重度不代表每个具体游戏都必须启用该能力。责任词典、证据限制和旧口径映射见 [scope-and-evidence.md](scope-and-evidence.md)。本轮责任重审复用历史 log/XML/probe，未重新运行测试。

## P2-01：1.13 public ABI/API 迁移说明与实际兼容范围不一致

- **责任/启用条件**：框架发布与兼容策略；只有已有编译 consumer 或源码仍引用旧 public rule 时触发。具体游戏是否升级由消费方决定。
- **Trigger**：旧 consumer 调用 1.12 `FieldSchema` 七参数 metadata，或源码注册已退役的五个 public 类型：`GobjOnUseKindRule`、`GobjLockRequirementFieldGroupRule`、`EffectKindRegisteredRule`、`CostEntryShapeRule`、`ChargesShapeRule`。
- **Actual**：正式 1.12 DLL 编译/运行 exit 0；只替换正式 1.13 `Core.Foundation.dll`、不重编译，运行 `MissingMethodException`。1.13 删除五个 public rule 类型；可选参数只支持源码重编译，不保留旧 metadata 签名。
- **Expected**：版本说明必须准确声明 ABI 与源码迁移边界；若宣称 MINOR 二进制兼容，必须保留 façade/旧签名。
- **Evidence**：[api-compat.log](api-compat/api-compat.log)、[api-compat-repro.log](api-compat/api-compat-repro.log)、[Program.cs](api-compat/Program.cs)、[CHANGELOG.md](../../../../CHANGELOG.md:421)、[SkillValidationRules.cs](../../../../core/rules/skill/schema/SkillValidationRules.cs:172)、[RulesSchemaCatalog.cs](../../../../core/rules/assembly/RulesSchemaCatalog.cs:137)。
- **最小通用验收（二选一）**：A. 继续宣称 MINOR 二进制兼容：保留旧 ctor 和旧 rule façade，old/new formal DLL probe 在不重编译替换后均 exit 0，并让旧源码迁移样例编译通过。B. 接受破坏性变更：按 11 的版本契约升级/记录迁移，迁移后的 consumer 和五个旧类型替代样例通过；当前旧 binary 的失败保留为已知破坏，不能只改 changelog 就称 ABI 修复。源码重编译可过可选参数不等于 ABI 修复。

## P2-03：Gobj `world_flag.expected` 未在 validation report 阶段拒绝

- **责任/启用条件**：框架 schema/业务 validator；仅当内容启用 `gobj.lock.requirement.kind=world_flag` 时触发，具体 flag 值由游戏内容提供。
- **Trigger**：缺失或错类型的 `expected` 进入正式校验装配。
- **Actual**：`GobjSchemas.cs:128-158` 的 variant 只登记 `flag_key`；[GobjLockBoundaryProbe.cs](../core/repro/GobjLockBoundaryProbe.cs) 记录 formal report `blocking=False/error_count=0`，随后 `LockDef` 在 `LockDef.cs:42-53,98-106` 抛 `DataFieldException`。合法 `expected=true` 通过。
- **Expected**：内容门禁在 report 阶段拒绝缺失/错型，消费方不依赖运行时解析异常。
- **Evidence**：[core-findings.md](../core/core-findings.md)、[gobj-lock-boundary.log](../core/logs/gobj-lock-boundary.log)、[GobjSchemas.cs](../../../../core/carriers/gobj/schema/GobjSchemas.cs:128)、[LockDef.cs](../../../../core/carriers/gobj/contracts/LockDef.cs:42)。
- **最小通用验收**：missing/错型 fixture 返回 blocking error 且不进入 `LockDef`；Bool/整数/Number 合法 fixture 继续通过。不能用改文档或接受 `DataFieldException` 作为修复。

## P2-05：启用开发期 DataHotReload 时 resident SkillHost 缓存不刷新

- **责任/启用条件**：框架 SkillHost/registry 的可选热重载契约；只适用于 Editor/Development、`EnableDataHotReload=true` 且 resident host 在 reload 后继续使用的路径。游戏负责将 authoring 文件同步到 content root。
- **Trigger**：修改数据并收到 reload 完成通知后，既有 SkillHost 继续 cast。
- **Actual**：resident host 仍观测 `EffectContext.BaseValue=7`、cooldown `0`，registry/fresh host 已是 `99/5`；这是 fake combat host 的效果 base value，不是实际 HP damage。`SkillDefCache.cs:38-55` 无失效，`SkillHost.cs:83-114,186,419` 无 `DataLoadCompleted` 订阅。
- **Expected**：成功 reload 后 resident host 要么刷新 cache，要么按契约强制重建；失败语义按 envelope/field 类别区分，不能笼统称永远保留快照。
- **Evidence**：[core-findings.md](../core/core-findings.md)、[skill-hot-reload-boundary.log](../core/logs/skill-hot-reload-boundary.log)、[presentation-findings.md](../presentation/presentation-findings.md)、[games/_template/README.md](../../../../games/_template/README.md:159)。
- **最小通用验收**：同一 resident host 在 reload 后得到 base `99`、cooldown `5`，与 fresh host 一致；同时保留成功/信封错误/字段错误的记录和 global blocked 语义。不能要求所有 host 或所有游戏都证明此可选路径。

## P2-06：启用 Quest world flag reward 时 union 负例延迟到 parser

- **责任/启用条件**：框架 `ContentValidationAssembly` 与 Quest 业务 validator；仅在消费方使用 `rewards.world_flags[].value` 时触发。
- **Trigger**：value 为空数组或超出 `ExprValueJson` 联合的值。
- **Actual**：`QuestContentValidationRule.cs:194-206` 只检查存在性，`QuestSchemas.cs:188-206` 未登记联合子结构；formal assembly 对 `value=[]` 为 0 error/warning/issue，后续 `RewardBundle.cs:164-182` / `ExprValueJson.cs:18-33` 抛 `FormatException`。合法 `true` 正向解析通过。
- **Expected**：默认 formal assembly 在 report 阶段 blocking/error；合法 Bool/Number/String/`{$id}` 通过。
- **Evidence**：[quest-world-flag-value-boundary.log](../core/logs/quest-world-flag-value-boundary.log)、[core-findings.md](../core/core-findings.md)、[ExprValueJson.cs](../../../../core/gameplay/common/contracts/ExprValueJson.cs:18)。
- **最小通用验收**：`[]` 不得到达 parser；四类合法值分别通过，sample/framework 原有结果不回归。不能以“union 可选”掩盖必须的业务 validator。

## P2-08：启用装备表现且已有 View 时 SaveLoaded 不清理旧外观

- **责任/启用条件**：框架可选装备表现的 View/SaveLoaded 消费契约；只有游戏选择该表现路线、场景已有 View 且从有装备 A 读无装备 B 时触发。游戏仍需提供 DisplayInfo 和资源。
- **Trigger**：同图 A→B 读档，B 的 live equipment 为空。
- **Actual**：正确 fixture 的 `equipment_after_load=0`，但既有 view/socket 保持，`Expected 0 / Actual 1`、`view_same=True`、`socket_same=True`；`ViewBinder.cs:125,234,248` 遇已有 view 直接保留，`EquipmentVisualSource.cs:68-70,91` 的 replay 主要覆盖新 View。
- **Expected**：同一 View 在 SaveLoaded 按真实装备快照先清旧再应用新快照，空装备也要清零；不得通过重发全局业务装备事件替代。
- **Evidence**：[view-equipment-filtered-final.xml](../presentation/view-equipment-filtered-final.xml)、[view-equipment-filtered-final.log](../presentation/view-equipment-filtered-final.log)、[presentation-findings.md](../presentation/presentation-findings.md)、[ExistingViewEquipmentSaveLoadAuditTests.cs](../presentation/ExistingViewEquipmentSaveLoadAuditTests.cs:180)。
- **最小通用验收**：A→空 B 断言 live equipment=0、同一 View/socket child=0；有装备 B 与快照一致，重复读档幂等且不重复触发业务副作用。full PlayMode 通过不覆盖这个隔离 optional path。

## P2-09：启用双根覆盖热重载时删除 override 不回落

- **责任/启用条件**：框架模板 watcher/registry 的可选热重载契约；仅适用于 Editor/Development 双根 framework/game 覆盖并删除 game 文件。
- **Trigger**：删除 game override，仍有 framework 同表记录。
- **Actual**：EditMode 诊断记录 `before=2; after_delete=2; manual_reload_fallback=1`；watcher 注册 Changed/Created/Renamed，未注册 Deleted。
- **Expected**：删除事件自动 reload，结果回到仍存在的 framework 值；诊断 test 的 1/1 Passed 只代表它捕获了缺陷。
- **Evidence**：[data-hotreload-edit-evidence.log](../presentation/data-hotreload-edit-evidence.log)、[editmode-full-final.xml](../presentation/editmode-full-final.xml)、[DataHotReloadDeletedOverlayAuditTests.cs](../presentation/DataHotReloadDeletedOverlayAuditTests.cs:43)、[presentation-findings.md](../presentation/presentation-findings.md)。
- **最小通用验收**：同一 fixture 的 changed/created/deleted 均自动刷新；删除后无需手工 Reload 得到 framework 值 1，并保留错误/blocked 语义。

## P3 与静态边界

### P3-02：ADR0019 门禁载体文案漂移

`SchemaAudit.WalkVariant`（[SchemaAudit.cs](../../../../presentation/assembly/SchemaAudit.cs:377)）查空键、common/discriminator 冲突和递归结构；Skill 19 effect/10 aura、Gobj、Dialogue、Quest 的 coverage tests 才比较 runtime 集合（[SkillSchemaCoverageTests.cs](../../../../core/rules/skill/tests/SkillSchemaCoverageTests.cs:20)、[GobjSchemaCoverageTests.cs](../../../../core/carriers/gobj/tests/GobjSchemaCoverageTests.cs:40)）。因此当前是文档载体 P3，不是 runtime 门禁缺失；修正文档并固定 CI coverage，未来若要求命令兜底再另立实现。

### P3-04：Periodic `scaling_stat` metadata 漂移

`AuraHost` 把 params 传到 dispatcher，dispatcher 消费 `scaling_stat`；`SkillSchemas.cs:327-333` 未声明，而非周期字段在 `:67` 使用 `Reference(stat.definition)`。这是 schema/编辑器可见性 P3，建议补 optional 同类型字段和 coverage，不把当前 warning 升为运行时缺陷。

### P3-07：WorldMap 点元素语义边界与旧索引漂移

`WorldMapSchema.cs:20-57` 已登记 `PointItemSchema` 的 item/Object，position 提供时 x/y 必填；匿名点和命名点是否需要 id/position 取决于其用途（如首个出生点或被引用目标），不强迫所有点统一字段。旧索引把它概括为“只有数组类型”不准确；跨图/落点引用完整性应由 04/05 的具体规则决定。本轮只保留静态边界，没有扩大成新增 Runtime P2。

## 按责任撤出主动缺陷列表的条目

| 条目 | 当前判断 | 证据 |
|---|---|---|
| Talent 完整点数管理 | 当前没有完整管理契约；已有字段、只读查询、被动光环复用和 granter 入口。是否形成通用分配/激活/撤销/持久化能力需另行契约决策，不能自动生成框架任务 | 07:294-304；`ArchetypeRegistry`；`RewardDispatcher.cs:262-277` |
| Summon follow/owner | 框架已有连续 follow/owner/联动机制；Discrete handler 明示跳过，是条件支持边界和文档问题，不新增 P2 | 07:291,303；`SummonTickHandler.cs:53-62` |
| SampleNewGameStarter | 可替换最小示例只重置位置/地图/模板；新局状态策略归游戏 starter，不能要求框架硬编码所有状态清理 | `SampleNewGameStarter.cs:8-22` |
| 孤儿记录 | 04:310 标为建议，不是当前门禁契约 | 04:310 |
| 导航跨帧/全索引空间查询 | 02:168-190 已收窄为实现方性能边界；旧索引措辞过期 | 02:168-190；`UnityNavigation2D.cs:127-173` |
| 位移轨迹碰撞 | `EffectDispatcher.cs:337-341` 只保证最终逻辑落点，06:147 未形成轨迹/遮挡强制契约；具体游戏需求不能倒推默认框架缺陷 | 06:147；`EffectDispatcher.cs:337-341` |
| owner/day/vendor | 已有透传扩展点；默认未提供业务回调或使用固定默认值，游戏按所选语义注入 | 13:63-66；`GameplayAssembly.cs:539-540,718` |
| escort | `IQuestHost` 的 UpdateProgress/Fail 是宿主可驱动 API；route provider/自动路线不是既定通用契约，属于游戏集成边界 | `IQuestHost:25-39`；08:91,105,114 |
| editor | ADR0018 规定外部消费项目；本阶段用户暂缓 | ADR0018:20-25；`editor/README.md:5,7-9` |

## 证据状态

旧五项 CORE/UI/TP/NAV/SPATIAL 复测结果、70 EditMode/270 PlayMode、正式 ZIP/lock、ABI 和六项 P2 的原始 logs/XML 均沿用已保存证据；本轮只修正责任解释和文档链接。基线门禁的 `15 PASS / 6 SKIP / 0 FAIL`、Core `2774 passed` 等数字见 [validation.md](validation.md)，不因本轮不重跑而改变。
