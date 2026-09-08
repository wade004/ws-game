# ws-game 1.7.0 总审计报告

审计基线为冻结仓 `D:\workespace\ws-game-review-8160178`，基线提交为 `8160178b76fb51ae704a8f14b428decf228cc33e`，版本为 `1.7.0`，审计分支为 `codex/audit-1.7.0-8160178`；原仓 `D:\workespace\ws-game` 在本轮只读，未修改产品代码、既有测试、原仓产物、网络服务或 registry。
本报告是本轮用户请求的总入口，结论以冻结仓源码、归档日志和真实链路探针为准。
五份分报告提供分域证据：[Core findings](core/core-findings.md)、[文档—代码矩阵](docs-project/doc-code-matrix.md)、[工程项目发现](docs-project/project-findings.md)、[验证记录](docs-project/validation.md)、[表现/Unity findings](presentation/presentation-findings.md)。

## 总体质量结论

1.7.0 的静态工程交付、版本封装、数据门禁、Native 测试和工具链检查达到可审计状态。
当前基线不能判定为完整游戏发布就绪，因为四项已由真实链路确认的 P2 缺陷仍在产品代码中。
本轮没有已确认的新 P1，不以静态候选或环境边界凑足缺陷数量。
四项 P2 覆盖跨图 aura 来源账本、进度等级状态同一性、存档失败事务边界和共享 AnimationClip 生命周期隔离。
CORE-170-03 同时包含失败段先清空装备，以及成功段逆序回滚向业务事件总线重放的两个已确认表现。
这些缺陷与门禁全绿可以同时成立；2460 个 .NET 测试通过不能覆盖真实 SaveSystem 组合链或 Unity 共享资源生命周期。
建议先完成四项 P2 的修复和生产式验收，再决定是否把版本提升为可发布候选。
Core 报告保留了真实 GameplayAssembly、WorldSim、SaveSystem、EquipmentHost 和 ProgressionHost 的组合探针及原始日志。
表现报告保留了真实 UnityEngineHost、UnityResourceLoader 和 UnityViewFactory 链路的有效 XML 与日志。
文档矩阵按 00–14 章逐项对照当前源码，避免把历史材料或能力索引误当成现状证明。
工程发现报告单独记录了 API surface、包 hash、工具链依赖和文档口径漂移。
验证报告记录了门禁命令、独立 TRX、退出码和明确跳过的 Unity 步骤。
所有“通过”均按证据类型解释，未把静态一致性、Native 测试或故障现状 probe 扩展成游戏正确性。
所有未执行项均作为验证边界记录，不单独升级为缺陷等级。
报告中的“已确认”仅指本轮已有可复核源码链路和归档结果支撑的现象。

## 四项已确认 P2

### CORE-170-01：跨图重放后卸装误删种族 aura

进图时装备 grants 先重放，种族被动随后只按 `HasAura` 判断是否已存在；共享同一 `auraDef` 时种族来源没有独立账本。
真实探针输出为：装备后 power 61，跨图清空后重进仍为 61，卸装后变为 power 11 且 aura 消失。
预期是卸下装备后保留种族 aura 及其属性修正，只移除装备来源的修正。
影响范围是配置了跨载体共享 aura 的有种族玩家；种族独占、装备独占和无种族场景不触发。
建议按来源保存种族句柄和装备/套装句柄，统一使用来源引用计数重放和释放。
验收覆盖首次注册、`ClearAll`、排空事件、重新 `AddEntity`、`EnterMap`、真实卸装和再次排空事件；断言种族 aura 和属性修正保留、装备独占修正消失。

### CORE-170-02：Progression 等级与实体等级分叉

`ProgressionHost.AddXp` 和 `RestoreState` 更新内部等级，但没有同步 `PlayerUnit.Level`。
`WorldUnitAccess.GetLevel` 继续读取实体字段，因此同一玩家出现规则等级 2 和实体等级 1。
真实探针输出为 `rules_progression_level=2;entity_level=1`，等级 2 装备返回 `RequirementNotMet`。
这会影响依赖 `IUnitAccess.GetLevel` 的装备需求及其它运行期消费者，也会让 UI 和规则查询出现分叉。
建议选定单一等级权威，或在升级和读档成功后由明确适配层同步实体字段。
验收覆盖多级真实 AddXp、Progression 读档、UI/查询、`WorldUnitAccess.GetLevel` 和等级需求装备，并断言 `Progression.GetLevel == WorldUnitAccess.GetLevel == PlayerUnit.Level`。

### CORE-170-03：Equipment 失败段不恢复且回滚事件污染业务

`EquipmentPersistable.Load` 在确认 JSON 对象形状前先清空当前装备，坏 shape 随后抛出 `FormatException`，由 SaveSystem 返回 `PersistableThrew`。
`SaveSystem` 只把已成功加载的段加入回滚列表，失败的 Equipment 段自身不进入列表。
真实探针显示坏段加载前装备存在，抛错后装备消失，并已产生 `StatChanged`、`ItemUnequipped` 等事件。
坏存档、迁移后错误 shape 或手工损坏段可以破坏读档前的装备、派生属性和观察者状态。
在 Equipment、PlayerVitals 和 Achievement 前段成功、后续自定义段失败时，真实 SaveSystem 逆序恢复数值，但会向 EventBus 重放 `ItemEquipped`；`AchievementHost` 将回滚事件再次计数，progress 由 1 变为 2 并错误解锁。
建议在修改 live host 前完成 shape、slot、ItemInstance 和可恢复性验证，使用临时恢复计划一次提交；或保存失败段自身快照并在回滚期间缓冲/抑制领域事件。
验收覆盖坏 shape、非法 slot、坏 ItemInstance、恢复中途槽位失败和跨段失败，断言装备、背包、生命/属性、aura、事件观察者和成就计数回到读档前。

### PRES-170-01：共享 AnimationClip 被空事件和跨 factory 污染

`UnityViewFactory` 对空 `events` 配置直接返回，不建立 authored 基线或独立 override。
首个非空配置从当前共享 `AnimationClip.events` 捕获 pristine 快照，再把数据事件写回全局共享剪辑。
pristine 快照和 override 状态是 factory 实例字段，新 factory 可能把旧 factory 写入事件误认为 authored 事件。
真实 Unity PlayMode 故障现状 probe 为 3/3，通过的是缺陷可稳定复现，不代表产品正确。
三种顺序均复现：非空后空配置、空配置后非空配置、factory A 写入后 factory B 写入；实际共享剪辑保留前一配置事件，空配置未获隔离 override。
建议把 authored 基线和每个 anim_set 结果移出可变共享剪辑写回路径；空配置也必须获得 authored-only 实例或 override，跨 factory 使用稳定快照或克隆资源。
本结论由真实 Unity 内部 hook 触发生产注册方法；尚未完成两套正式数据表装配及实际播放事件端到端验证，不能扩大为整条运行游戏链。
验收覆盖三种顺序、场景重建、实体销毁/重建，并以 Animator 播放中的事件集合为最终断言。

## 文档更新与能力分类

存档契约需要统一为 best-effort：失败结果仍为 `PersistableThrew`，只对已有成功快照的段尝试逆序恢复。
`IPersistable` 当前“之前成功 Load 的其它段不会被回滚”与实现冲突，应改为明确说明可能残留。
`CHANGELOG` 将 best-effort 写成“部分加载中间态消失”过强，应与 `architecture/10_存档与持久化.md` 的准确表述统一。
`world.gobj_pending_loot` 仍是自定义段且未列入 `SaveSections.KnownOrder`，第 10 章排序描述需按代码修订或登记该段。
能力索引应修正 SimTime 为三处组合根均未注入统一时钟，并保留 owner/day/vendor 三根透传、默认值为 null 的事实。
`toolchain/README.md` 应说明复制整套 toolchain，或明确 `get_framework.ps1` 必须连同同目录 `_hash.ps1` 携带。
公开 API 别名、旧存档字段迁移和 `PersistableThrew` 必须分开描述，不能用 API 编译兼容代替存档迁移证明。
未实现项包括内容编辑器、天赋运行时激活/撤销/持久化、`FindUnits`、离散召唤 duration 推进、离散掉落清理调用、轨迹碰撞、新局完整状态重置、VFX anchor 持续跟随和孤儿记录检测。
已实现但未默认接入项包括 Gobj `SimTime`、`QuestHost.Update`、owner/day/vendor 实际 provider、Replay 生产入口、武器 style 消费、DisplayMapCoverageRule 和 FeedbackRuleValidator 默认注册。
TargetPoint 参数由上层消费，owner/day/vendor 需要游戏提供回调，部分新局清理由游戏负责，不能统称为框架缺失。
明确非目标包括 `time.day_cycle` 恒为 0 和 ATB；离散 turn、round、is_my_turn 仍是另一条已接线能力。
独立 Mesh 异步 Model 合同等静态候选不计入确认缺陷。

## 旧 1.6 问题在 1.7 的复核

旧 pending loot/gobj 字段兼容和 SaveSystem 基本成功段回滚旧场景通过；新增 CORE-170-03 是失败段自身变更及真实事件副作用。
旧缺段按 `JsonNull` reset 已通过，旧 vendor timer 读档恢复已通过，均未重报。
旧种族 aura 跨图场景已通过；CORE-170-01 仅新增跨载体共享同一 auraDef 的来源账本场景。
旧装备/set 共享源和 same-map spawn timer 已通过，不能扩大为覆盖种族来源或失败段事务。
旧 AUD-04 公开 API 改名已通过，1.7 保留两个 `[Obsolete]` 转发别名，未重报公开 API 编译破坏。
旧 AUD-05 slot mesh 已修复并由 6/6 Unity 正确性测试确认，缺失时保留当前网格并发起加载，未重报。
1.7 原始 zip 的六个 DLL hash 与对应 lock 全部匹配；1.5 原始 zip 的六个 DLL hash 与对应 lock 也全部匹配。
两个独立 consumer 对原始 1.5 DLL 编译为 0 warning；对原始 1.7 DLL 编译为 0 error、2 条预期 CS0618 warning。
1.7 已保留旧 API 的 `[Obsolete]` 别名转发，证明公开 API 编译兼容窗口已修复，但不证明旧存档字段自动迁移。
SaveSystem 的基本成功段回滚修复不等于全面事务保证，不能用旧项关闭掩盖 CORE-170-03。

## 验证结果与边界

当前基线 `check.ps1 -SkipUnity` 为 14 PASS、6 SKIP、0 FAIL，退出码为 0。
六个 .NET 项目合计 2460 通过，组成是 659、107、323、404、491、476，包含现有 Perf 类别；Python 工具链为 92 passed、2 skipped。
Unity 专项正确性为 53/53，组成是旧回归 43、SlotMesh 6、ClipIsolation 3、QuestDay1 1。
另有共享剪辑故障现状 probe 3/3；这 3 条是证明缺陷存在的现状断言，不是产品正确性通过。
已验证证据支持静态依赖、版本/lock、包清单、数据校验、Native 测试、工具链检查和所列定向 Unity 链路。
本轮未执行完整 Unity 门禁（编译/EditMode/PlayMode 全套）；已执行的定向 PlayMode 测试经过其所需编译，但未执行连续或离散独立版冒烟、IL2CPP、完整 consumer 演练、实际游戏压测或用户验收。
本轮也没有网络 registry、Release 发布或网络消费验证，不能把 `-SkipUnity` 的通过扩张为完整 Unity、独立版、性能、发布或用户质量结论。

## 建议修复顺序与验收门槛

第一优先修复 CORE-170-03，建立失败段自身快照和回滚期间事件语义，避免坏存档造成装备丢失及成就误计数；门槛是所有失败阶段状态和外部观察者状态恢复。
第二优先修复 CORE-170-02，统一 Progression、实体和 `IUnitAccess` 的等级权威；门槛是升级、读档、UI 查询和等级需求装备在真实组合根中保持同一等级。
第三优先修复 CORE-170-01，完善跨来源 aura 账本和跨图重放顺序；门槛是重进地图后卸装只移除装备来源，种族属性和 aura 仍存在。
第四优先修复 PRES-170-01，隔离空事件、跨 factory 和场景重建的 AnimationClip 状态；门槛是三种顺序和 Animator 实际播放事件集合符合预期。
四项修复完成后，应重新执行 Native 门禁、对应真实链路探针、Unity 定向正确性集，并补齐本轮未执行的发布前环境验证。
截图诊断（非 `ws-game` 代码问题）：当前任务曾两次失败请求 `gpt-6-astra none`，而本轮已使用 `high`，全局 config 也是 `high`；主审已核对会话记录，无需再次修改配置。
本报告仅完成审计文档，没有改动产品代码，也没有 commit；原仓最终状态由主审核对。
