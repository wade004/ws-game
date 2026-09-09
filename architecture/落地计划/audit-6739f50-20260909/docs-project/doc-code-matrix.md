# 1.11.0 文档—代码对照矩阵

基线：冻结工作树 `6739f50e44ba39a023c6209af2673aaf6a1c1fdc`，版本 `1.11.0`；原仓 `D:\workespace\ws-game` 只读。本矩阵重新读取 architecture/00-14、17 份 ADR、根/模块 README、能力索引、toolchain 与发布文档，并以当前源码为准。分类含义：**未实现**=当前没有框架实现；**已实现未默认接线**=代码可用但需调用方注入/声明；**游戏责任**=框架只给字段或窄契约，具体内容/消费由游戏提供；**非目标**=当前架构明确保留项。

## 00-14 逐章

|章节|当前代码对应|结论/分类|证据|
|---|---|---|---|
|00 架构总则|L0-L4 项目与事件边界、ADR 目录、当前检查门禁|总体一致；“未实现”只保留编辑器、天赋激活/持久化等开放项，不把 TargetPoint 字段归为框架未实现|[00](../../../../architecture/00_架构总则.md)、[能力索引](../../../../architecture/落地计划/落地方案与分阶段计划.md#L1343)|
|01 分层与依赖|六 Core 工程及 adapters/presentation 引用关系|已实现；跨层引用由项目文件和 check 编译约束，具体游戏数据仍属游戏责任|[01](../../../../architecture/01_分层与依赖.md#L207)、[check](check-skipunity.log#L26)|
|02 引擎适配层|UnityNavigation2D 同步 BuildGrid/AStar；MovementTickHandler 方向移动调用 Raycast/IsWalkable；UnitySpatialQuery 有 buckets 但部分查询遍历 `_entries.Values`|已有实现但 NAV/SPATIAL 定向探针仍有失败；性能契约存在两项差异：`findPath` 应支持跨帧预算而当前接口同步返回 `IReadOnlyList`，无请求队列；`QueryRect`、Line/Rect Shape、Nearest、MaxRadiusHint 仍有全扫描。分类为未实现/需正式收窄的性能实现，不静态定级性能缺陷|[02 §1.8-1.9](../../../../architecture/02_引擎适配层.md#L148)、[UnityNavigation2D](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityNavigation2D.cs#L92)、[UnitySpatialQuery](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnitySpatialQuery.cs#L113)|
|03 运行时骨架|SimLoop/TurnScheduler/GameplayAssembly 固定步与离散调度|连续与基础离散调度已实现；ATB/day_cycle 保留为非目标；PowerTickHandler 离散步按本模块设计不推进资源，SummonTickHandler/LootExpiryTickHandler 也只在连续步执行局部处理（Loot 的绝对模拟时钟仍累加），这些是处理器边界而非全局离散未启用；具体离散资源换算为游戏策略|[03](../../../../architecture/03_运行时骨架.md#L204)、[PowerTickHandler](../../../../core/numbers/power_set/core/PowerTickHandler.cs#L27)|
|04 数据与内容管线|DataRegistry、SchemaCatalog、validator；WorldMapSchema 注册 regions/teleport_points/music_ref/allowed_difficulties 类型|四字段类型登记已实现；nested teleport 元素结构和 refs 完整性仍未校验。DisplayMapCoverageRule/SpawnSummonOnlyCreatureRule 需显式来源或查询，孤儿记录检测仍只是建议、无对应规则|[04 §5.1](../../../../architecture/04_数据与内容管线.md#L246)、[WorldMapSchema](../../../../core/foundation/scene_router/core/WorldMapSchema.cs#L20)、[check](check-skipunity.log#L114)|
|05 对象模型与世界|Unit/WorldSim、TargetPoint、WorldMapSchema、MovementTickHandler|TargetPoint 字段已实现，具体 point 解析/点选由游戏/上层 AI 消费；方向移动当前已做导航阻挡检查。地图四字段仅完成字段类型登记，数组内元素/引用校验未完成|[05](../../../../architecture/05_对象模型与世界.md#L20)、[MovementTickHandler](../../../../core/carriers/unit/core/MovementTickHandler.cs#L368)|
|06 规则层|Stat/Power/Skill/Cast/TargetChain/AI；RulesSchemaCatalog 只登记 target.chain_def|基础规则已实现；06 当前正文已在 25、127、224 明确 `target_shape_ref` 只通过 chain 使用 Shape，现行 chain 入口已关闭旧直指 Shape 项，不能扩大为范围查询不可用。天赋树结构校验有实现，天赋激活/效果持久化未提供默认实现|[06](../../../../architecture/06_规则层_属性技能战斗AI.md#L120)、[RulesSchemaCatalog](../../../../core/rules/assembly/RulesSchemaCatalog.cs#L168)|
|07 载体层|Item/Creature/Gobj/Summon/Equipment/Aura；ArchetypeRegistry 注入后应用种族被动|载体与被动应用已实现；技能书/资源类型/被动 aura 字段仍只作 Id/IdList 格式校验，跨表登记受分层/接口形状限制，属于未校验，不是技能或被动未实现|[07](../../../../architecture/07_载体层_物品生物物件.md#L235)、[ArchetypeRegistry](../../../../core/numbers/archetype/core/ArchetypeRegistry.cs#L90)|
|08 玩法层|Loot/Quest/Dialog/Encounter 等 handler，GameplayAssembly provider 注入|玩法骨架已实现；owner/day/vendor、内容查询、时间提供器等可选依赖已实现但默认 null，属已实现未默认接线；具体掉落表、任务和完整新局重置流程属游戏责任|[08](../../../../architecture/08_玩法层_掉落任务对话关卡.md#L275)、[GameplayAssembly](../../../../core/gameplay/assembly/GameplayAssembly.cs#L260)|
|09 表现层|Presentation.Common、ViewBinder、Animation/Model、EquipmentVisual/WeaponStyle|表现框架与默认装配存在；model/动画、VFX/SFX 等资源/消费仍由游戏配置决定，未注入可按契约退化。UnityViewFactory 顶部旧判断记录仍声称 ViewBinder/CameraHost 不支持退订，实际 ViewBinder.Dispose 与 Presentation Dispose 已存在，需更新注释|[09](../../../../architecture/09_表现层.md#L200)、[ViewBinder](../../../../presentation/view_binding/core/ViewBinder.cs#L68)、[UnityViewFactory](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs#L5)|
|10 存档与持久化|SaveSystem、Persistable 注册、DerivedStateRebuilder、PlayerVitalsPersistable|存档段和成功/失败回滚重建合同已存在，但 CORE 定向复核仍发现运行态派生/资源状态恢复缺口；vitals 默认只保存 alive+Health。architecture/10 §2.5 将 Power 当前值泛化为默认存储，实际 mana/rage 等资源池没有通用默认 persistable，需按游戏扩展独立段|[10 §2.5](../../../../architecture/10_存档与持久化.md#L92)、[PlayerVitalsPersistable](../../../../core/gameplay/assembly/PlayerVitalsPersistable.cs#L85)|
|11 工程规范与测试|check.ps1、Core.sln、toolchain pytest、包清单|静态门禁与工具链可运行；Unity/独立版/消费方由本次 `-SkipUnity` 明确跳过，不能把跳过当未实现|[11](../../../../architecture/11_工程规范与测试.md#L120)、[check日志](check-skipunity.log#L232)|
|12 扩展与变更流程|ADR、版本与实现对齐流程|流程与 17 ADR 对齐；历史 ADR 算法废止记录仅作维护线索，不作为现行冲突|[12](../../../../architecture/12_扩展与变更流程.md#L180)、[ADR目录](../../../../architecture/adr)|
|13 新游戏接入指南|Bootstrap、数据根、provider、TargetPoint/Presentation 接入说明|公共入口、真实 DLL/包配置可复用；具体地图、目标解析、内容、可选 provider 与新局语义由游戏提供|[13](../../../../architecture/13_新游戏接入指南.md#L160)|
|14 资产规格书模板|display/model/animation/effect/audio 资产 schema 与导入器|模板、导入校验和占位资产已实现；真实资产、模型/动画选择和美术内容属游戏责任|[14](../../../../architecture/14_资产规格书模板.md#L80)|

## ADR 0001-0017

|ADR|当前结论|
|---|---|
|0001-0008|法术统一、逻辑对象不依赖引擎、固定步、数据驱动、Expr、DisplayInfo、窄契约事件总线、WorldState 均在现行模块和 check 编译中成立。|
|0009|存档是唯一持久化的现行规则；SaveSystem/注册段已实现，派生重建和回滚由当前 core 实现/探针另行验收；不可把“默认保存所有 Power 当前值”从 architecture/10 泛化。|
|0010-0012|新增原语审批、适配层隔离、2D/model 双外形与固定镜头现行实现一致。|
|0013|连续与基础离散调度已接通；ATB/day_cycle 仍是明确保留项，PowerTickHandler 对离散资源不推进是本模块边界。|
|0014|资产契约、导入工具、UI 套件为框架交付；具体内容仍由游戏提供。|
|0015|Expr 点分标识符以引用登记表消歧，当前 parser/validator 与 toolchain 对齐。|
|0016|公共导航、clock subscription、空间登记契约已实现；需补齐/收窄 02 §1.8 跨帧寻路和 1.9 查询性能语义。|
|0017|model 默认路线、动画剪辑隔离、命中帧同步接口现行；未注入可选渲染器/资源时的退化属于调用方配置边界。|

## 能力分类与当前开放项

- **未实现或未覆盖**：编辑器工具；`findPath` 跨帧预算/请求队列；空间查询所有方法的索引化（当前部分方法全扫描）；WorldMapSchema nested teleport 元素与 refs 完整性；孤儿记录检测；`ISkillHost.FindUnits` 便利 API；位移类效果沿途轨迹碰撞；VFX anchor 持续跟随；天赋激活/效果持久化；全资源池当前值的框架默认持久化；离散步召唤物处理器的推进（当前连续-only模块边界，需游戏策略决定，不升级为架构缺陷）。
- **已实现但未默认接线**：SpawnSummonOnlyCreatureRule 的查询、DisplayMapCoverageRule 的 sources、owner/day/vendor/time provider、model/weapon-style/VFX/SFX 的可选消费者、回放/反馈等按调用方注入。
- **游戏责任**：TargetPoint 从字段到具体地图点/上层目标的解析和点选；具体内容数据、完整新局/死亡重置策略；资源池除 health 外是否另存；真实渲染资产与 UI/HUD/mesh 消费。
- **非目标**：ATB 与 `day_cycle` 的完整模型，以及把资源离散推进规则固化为框架默认语义。
- **文档/注释更新**：`ArchSchemas.cs:17-18,42,58` 与 archetype schema README:29 的“skill 尚未实现/只保存不应用”旧前提；`PowerTickHandler.cs:13-14`、`IPowerDiagnostics.cs:7-8` 的“本项目未启用离散模型”表述；`UnityViewFactory.cs:5-14` 的退订旧判断；architecture/10:96 对 Power 当前值的泛化。SummonTickHandler 的连续-only诊断已修，不列为旧文案缺陷；LootExpiry 的历史修订不列现行缺陷。

## 已关闭的历史项

1. 1.10 方向移动导航阻挡、CORE-110-03 同 tick move 预算、WorldMap 四字段类型登记、Save/Load 事件抑制与派生回滚合同均已进入当前实现/文档，不能照抄旧审计重复定级。
2. API 归档工程路径需按当前 README 的 `FrameworkRoot` 参数使用；旧绝对路径结论已关闭。源码兼容别名由当前重建 DLL 编译验证，详见 validation。
3. 旧归档链接应以当前真实文件为准；本轮只记录 checker 的覆盖边界和当前存在性，未把缺失历史证据扩大成当前 API 断裂。



