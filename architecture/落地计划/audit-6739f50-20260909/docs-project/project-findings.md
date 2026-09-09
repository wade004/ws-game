# 1.11.0 工程与文档发现

基线为冻结仓 `6739f50e44ba39a023c6209af2673aaf6a1c1fdc` / `1.11.0`。原仓 `D:\workespace\ws-game` 只读；本轮只在冻结仓审计目录写证据，未改产品源码、既有测试、registry 或发布源。

## 当前发现

### DOC-111-01：Power 持久化范围与实现不一致（文档更新，建议 P3）

`architecture/10_存档与持久化.md:96` 写成 Power 当前值默认存储，只排除 Stat 派生值；实际 [PlayerVitalsPersistable.cs:85-135](../../../../core/gameplay/assembly/PlayerVitalsPersistable.cs#L85) 只写入 `alive` 与 `WellKnownPowers.Health`。类型注释 [PlayerVitalsPersistable.cs:59-63](../../../../core/gameplay/assembly/PlayerVitalsPersistable.cs#L59) 明确其它 mana/rage 等资源需要游戏自行扩展独立段，且 `IPowerHost` 没有枚举全部资源的查询。应将 10 §2.5 改为“vitals 生命值实现；其它资源池当前值不由框架默认段覆盖”，不要把 core 派生状态问题与此条混为一谈。

### DOC-111-02：archetype schema 注释保留过期前提（文档/源码注释更新，P3）

[ArchSchemas.cs:17-22](../../../../core/numbers/archetype/contracts/ArchSchemas.cs#L17) 仍以“L2 skill 尚未实现”解释未声明 Reference，[ArchSchemas.cs:42](../../../../core/numbers/archetype/contracts/ArchSchemas.cs#L42) 重复该语句，[ArchSchemas.cs:58](../../../../core/numbers/archetype/contracts/ArchSchemas.cs#L58) 还写“只保存不应用”。当前 `skill` 已实现，`ArchetypeRegistry.ApplyTo` [90-115](../../../../core/numbers/archetype/core/ArchetypeRegistry.cs#L90) 会应用被动 aura（注入为 null 时才是兼容退化）；archetype README [18,29,46-57](../../../../core/numbers/archetype/schema/README.md#L18) 已部分说明分层边界，文件间表述需统一。该项不证明技能或种族被动未实现。

### DOC-111-03：离散资源处理器边界文字过时/易误导（文档更新，P3）

[PowerTickHandler.cs:12-14](../../../../core/numbers/power_set/core/PowerTickHandler.cs#L12) 与 [IPowerDiagnostics.cs:6-8](../../../../core/numbers/power_set/contracts/IPowerDiagnostics.cs#L6) 写“本项目暂不启用离散时间模型”，而当前 ADR-0013 和 sim_loop 已有基础离散调度。实际 `PowerTickHandler.Execute` 对离散步只记警告并不推进资源 [PowerTickHandler.cs:27-38](../../../../core/numbers/power_set/core/PowerTickHandler.cs#L27)，这是该资源处理器的连续-only边界；不应写成整个框架离散模型未实现，也不应把 ATB/day_cycle 与此混列。应改注释为“离散步由游戏策略决定是否换算调用 AdvanceAll”。

### DOC-111-04：UnityViewFactory 顶部判断记录过期（源码注释更新，P3）

[UnityViewFactory.cs:5-14](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs#L5) 仍声称 ViewBinder/CameraHost 不支持退订。当前 [ViewBinder.cs:68,90-145](../../../../presentation/view_binding/core/ViewBinder.cs#L68) 已实现 `IDisposable`、保存订阅句柄并退订，Bootstrap 也在 [GameFoundationBootstrap.cs:816-821](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Bootstrap/GameFoundationBootstrap.cs#L816) 先 Dispose Presentation 再清理 View。保留 DestroyAllCreatedViews 的生命周期职责即可，更新旧判断记录，不定运行时缺陷。

### DATA-111-01：地图数组字段的深层校验尚未提供（能力缺口，P3）

[WorldMapSchema.cs:20-45](../../../../core/foundation/scene_router/core/WorldMapSchema.cs#L20) 已登记 `regions`、`teleport_points`、`music_ref`、`allowed_difficulties` 的字段类型；当前 `Array` 只做容器级检查，nested teleport 元素形状和目标 refs 完整性未验证。architecture/05 已记录这个边界。部分运行时消费存在不等于 schema 校验完成，当前不把“四字段未实现”作为结论。

### PERF-DOC-111-01：导航/空间查询性能契约与实现层级不同（待设计决策，不定性能 bug）

02 §1.8 [167](../../../../architecture/02_引擎适配层.md#L167) 要求 `findPath` 支持跨帧分摊；[UnityNavigation2D.cs:127-173](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs#L127) 同步 `BuildGrid/AStar` 并返回 `IReadOnlyList`，没有请求队列或预算调度。02 §1.9 [189](../../../../architecture/02_引擎适配层.md#L189) 要求空间索引；[UnitySpatialQuery.cs:113-155](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnitySpatialQuery.cs#L113) 的 Rect、Line/Rect Shape、Nearest、MaxRadiusHint 仍访问 `_entries.Values`，虽有 buckets 且 Radius/Cone 使用局部候选。应实现分帧/完整索引，或经正式设计决策收窄公共性能契约；静态证据不足以认定实际性能不达标。


### CAP-111-01：能力索引中的三个真实未实现项需保留

当前能力索引 [1285](../../../../architecture/落地计划/落地方案与分阶段计划.md#L1285) 的 `ISkillHost.FindUnits` 对应 [SkillHost.cs:169-175](../../../../core/rules/skill/core/SkillHost.cs#L169)，仍直接告警并返回空列表；[1291](../../../../architecture/落地计划/落地方案与分阶段计划.md#L1291) 对应 [EffectDispatcher.cs:327-373](../../../../core/rules/skill/core/EffectDispatcher.cs#L327)，位移效果直接设置终点，不做沿途轨迹碰撞；[1295](../../../../architecture/落地计划/落地方案与分阶段计划.md#L1295) 对应 [VfxPlayer.cs:144-354](../../../../presentation/vfx_sfx/core/VfxPlayer.cs#L144)，anchor 只在 Spawn 解析一次，Update 不随实体移动更新。这三项是未实现能力，不能和已实现的 `target.chain` 形状查询合并，也不应由 `-SkipUnity` 推断运行时结果。

### DOC-111-05：空间查询索引注释过度宣称（源码注释更新，P3）

`UnitySpatialQuery.cs` 顶部判断记录声称已满足索引性能契约，但实际 Radius/Cone 以及 QueryLine 的候选阶段经 `CandidatesNear` 使用 buckets；Rect、Line/Rect Shape、Nearest、MaxRadiusHint 仍遍历 `_entries.Values`（见 [113-155](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnitySpatialQuery.cs#L113)）。应把注释改为“部分查询使用 buckets，剩余查询待索引化/性能契约待收窄”，不凭静态实现判定性能缺陷。
## 已实现但依赖接入的边界

- `SpawnSummonOnlyCreatureRule` 需要 `ICreatureTemplateQuery`，`DisplayMapCoverageRule` 需要显式 sources；默认 null 是装配边界。
- owner/day/vendor/time provider、model/weapon-style/VFX/SFX、回放与反馈消费者均有公共接口/实现，是否默认构造取决于游戏 Bootstrap。
- TargetPoint 字段已存在；具体地图点解析、点选和目标链消费由上层游戏负责，不能把字段存在写成完整地面点选实现。
- 新局完整重置、具体内容数据、真实资产和 HUD/mesh 等属于游戏责任；ATB/day_cycle 仍为非目标。

## 上轮项复核

- 1.10 方向移动导航检查、同 tick 多 move 单位固定 dt 预算、四字段类型登记、Save/Load 抑制/派生合同已在当前文档或实现中；本报告不重复定级历史项。
- 旧 `current17-only` 绝对 HintPath 结论已关闭：当前归档项目参数化 `$(FrameworkRoot)`，README 要求显式传入。无参数失败只是缺少参数的预期；本轮 API 编译见 [api-compat-current-rebuild-final.log](api-compat/api-compat-current-rebuild-final.log)。
- 旧归档材料的缺失日志若被发现属于证据归档完整性问题，不转化为当前 API/运行时缺陷。本轮报告只引用实际留存文件。

## 修复优先级与验收

1. 更新 10 §2.5 与 PlayerVitalsPersistable 的边界说明，并用不同资源池存档回归证明“health 默认段 + 游戏扩展段”的分界。
2. 修正 ArchSchemas、archetype README、PowerTickHandler/IPowerDiagnostics、UnityViewFactory 旧判断记录；要求源码注释与现行 ADR/模块 README 同义。
3. 对导航决定跨帧请求预算或正式收窄契约；对空间查询补齐 QueryRect/Line/Rect/Nearest 索引后，加入规模化基准再谈性能门槛。
4. 为 nested teleport 定义元素 schema、目标引用规则与孤儿记录检测；当前只登记容器类型的结果不得写成引用完整性通过。



