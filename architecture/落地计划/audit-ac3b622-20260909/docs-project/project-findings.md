# 1.10.0 项目发现

基线为冻结仓 `ac3b622041c348e87a469959c09d8a541a7c1351` / `1.10.0`；原仓 `D:\workespace\ws-game` 只读。本报告只记录当前源码、现行文档和本轮工程证据得到的结论，不把可选回调为空、游戏内容责任或跳过的验证步骤定为框架缺陷。

## 需要维护的文档边界

### NAV-DOC-01：停止原因 XML 注释与实现分支不一致（文档更新）

`core/carriers/unit/core/MovementHost.cs:39-42` 将“重算/重验失败后 `PathFailurePolicy=Stop`”也描述为 `MoveStopReason.BlockingChanged`。当前 `MovementTickHandler.HandlePathFailure`（:210-228）明确调用 `ApplyStop(...PathFailed)`；阻挡变化直接按 `BlockingChangePolicy.Stop` 才使用 `BlockingChanged`（:470-478）。CHANGELOG 与 unit README 已写成 `PathFailed`，应以实现/CHANGELOG 统一 XML 注释，避免游戏层按原因分流时误判。

### NAV-DOC-02：方向移动导航边界与实现不一致（文档/契约待决，未定级）

05 第 6.1/6.4 节把“移动前单点/直线可达性”写成一般移动语义；实际 `MovementTickHandler.ApplyDirectionalMove:336-359` 只调用 `IsBlockedByUnit` 后 `SetPosition`，不会查询 `INavigation2D.IsWalkable` 或 `Raycast`。目标类移动才通过 `FindPath`。当前实现因此缺少方向移动的导航阻挡检查，需要主审决定补齐实现还是把公共契约正式限制为目标路径移动；本报告不擅自归为游戏责任。

### DATA-DOC-01：地图扩展字段 schema 尚未登记（未实现/代码待补）

05:209-218 明确 `regions`、`teleport_points`、`music_ref`、`allowed_difficulties` 只有结构声明；`WorldMapSchema` 目前只登记 `id`、`scene_ref`、`nav_ref`、`spawn_points`。部分运行时读取不等于数据校验接线，建议继续按四字段分别补 schema 与规则后再改分类。

### DATA-DOC-02：掉落过期处理器注释仍是旧版本口径（文档更新）

当前基础 Discrete 已由 `TurnScheduler` 和相关规则落地；`SummonTickHandler.cs:53-61` 已写明按设计只在连续步推进。实际残留旧文案在 `core/gameplay/loot/core/LootExpiryTickHandler.cs:35`，仍写“离散时间模型本项目暂不启用”并称不推进过期判定。应改为真实局部语义：离散步跳过本 tick 的清理调用，而 Loot 的绝对模拟时钟仍由 RulesAssembly 累加，不能将其表述成全局非目标。

## 已核实但不定为缺陷的工程边界

- `target_shape_ref` 当前只支持 `target.chain_def`；链内 `shape` 由 TargetHost 解析。`RulesSchemaCatalog.cs:161-172` 与 `CastPipeline.cs:247-255` 一致，06/skill README 也已收窄。`ISkillHost.FindUnits` 仍是另一条未实现的便利 API，不能合并成“范围查询整体不可用”。
- `IDerivedStateRebuilder` 只在成功 Load 段回调，回滚明确不调用钩子；这是当前窄契约。真实 fixture 已确认 CORE-110-01 的回滚派生残留与 CORE-110-02 的跨职业旧键/旧 power 残留，详见 [整体报告](../AUDIT_REPORT.md) 与 [core 报告](../core/core-findings.md)。
- `AuraHandleLedger`、等级权威/同步、存档事件抑制等机制在当前源码和模块 README 中均有对应实现；是否覆盖所有业务组合以 core 的定向证据为准。
- `TargetPoint` 是框架承载的可空输入字段，地面点选到具体目标属于上层 AI/玩家辅助施法责任；根 README 与能力索引已有该口径。编辑器、天赋运行时分配、孤儿检测等仍是独立未实现项。
- 采集时钟、Quest.Update、owner/day/vendor、ReplayPlayer、FeedbackRuleValidator、DisplayMapCoverageRule 属于已有机制但需显式驱动/登记；默认 null 或不登记符合可选设计。
- 新局完整清理是模板/游戏开局责任。方向移动当前只做单位间阻挡检查，未调用 `INavigation2D.IsWalkable/Raycast`；05 的一般移动文字与实现不一致，应补方向导航检查，或经正式设计决策收窄公共契约。

## 发布与归档工程证据

- 原仓正式 `D:\workespace\ws-game\dist\ws-game-1.10.0.zip` SHA-256 为 `60b98d28e33785b40744af1f21dff2b873b589c5810b81e37a7dc557a1c9fb8b`；对应 `ws-game-1.10.0.lock` SHA-256 为 `9c187c89f0baf5ca03e3127b7efd8c0cd6f2fbc7350e72309f34285bbde709dc`。ZIP 两个插件路径内六 DLL 的 hash 均与 lock 一致：Foundation `5de3ae406308f7b0abf74e2ed3baef7e68342dd077b000bb03b31039c029a03c`、Numbers `09034acaa0d7c6e3be79c938450aa1c93dade723647e4271dfd6f6c33beb18a8`、Rules `96bc2c4d9e108675b1296838d6b0e2befc2ffb6feead3019a1d5df1d946eccd6`、Carriers `deaf065a7b4374f35a54ce778bbb0d003d8754875e89a11d15f22b22176ad9dd`、Gameplay `5208849e1f7f4b8f31e542aa8ad84830227df5023b403960bde5840ab53ce5d8`、Presentation `7dd99d84a8c06e5ef713a62167ab8119a04be129f09d2f2e5a9f849f11dba288`。该核验只读原仓 ZIP/lock。
- 冻结仓 check 生成的 `dist/1.10.0` 是本轮重建快照，不是正式 ZIP；构建路径、`-SkipUnity`、审计 untracked 状态和 Unity 元数据差异均可能改变字节，不能用其 hash 反推发布缺陷。
- 旧 e070 归档的 Markdown 证据链接存在性扫描见 [archived-e070-link-audit.log](archived-e070-link-audit.log)：7 个 Markdown 共 9 个真实缺失引用；源码行号链接已剥离行号后确认文件存在，剩余缺失项集中在旧 core/presentation 日志和旧路径日志。这是证据归档不完整的工程 P3，不是当前运行时/API bug；本报告只引用当前审计实际留存的日志。

## API 兼容与结构风险

- 归档旧 API 探针 `ApiCompatProbe.cs` 以冻结仓本轮重建 DLL 为 `FrameworkRoot`，restore/build 成功，输出 [api-compat-current-rebuild.log](api-compat/api-compat-current-rebuild.log)。两个旧 `GameObjectHost.PendingChestLootSnapshot`/`RestorePendingChestLoot` 调用仅产生预期 CS0618，证明当前重建 DLL 保留兼容别名；这不是原始 ZIP 二进制复现。
- `current17-only/ApiCompatCurrent17Only.csproj` 当前已经使用 `$(FrameworkRoot)` 参数化 HintPath，README 也要求通过 `-p:FrameworkRoot=...` 指定 DLL。无参数构建的失败（MSB3245/CS0246）是缺少该必需参数的预期结果，见 [api-compat-current17-absolute-path.log](api-compat/api-compat-current17-absolute-path.log)；按 README 传入冻结仓重建 DLL 后 restore/build 成功，见 [api-compat-current17-parameterized.log](api-compat/api-compat-current17-parameterized.log)。1.9 旧的“绝对 HintPath 不可移植”结论已关闭。
- check 生成的 ignored `bin/_check_artifacts`、`dist/1.10.0` 以及本报告下 API compat obj/bin 仅为验证证据，未修改产品源码、既有测试、原仓或 registry；冻结仓 tracked diff 为空，新增均为审计/探针/忽略产物。

## 建议顺序

1. 先修 NAV-DOC-01～02 与 DATA-DOC-02，减少公共契约误用；补齐 05 四字段 schema 时为每字段加校验用例。
2. 由 core 依据真实 A/B 职业与 Save/Restore fixture 验收派生状态与回滚，不把 `IDerivedStateRebuilder` 的成功 Load 钩子当作回滚保证。
3. 游戏接入时明确方向移动阻挡、TargetPoint 消费、Quest/采集时钟和 provider 的 owner；这些选择不应被框架缺口清单混淆。
