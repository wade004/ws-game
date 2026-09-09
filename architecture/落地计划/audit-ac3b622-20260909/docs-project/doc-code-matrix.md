# 1.10.0 文档—代码对照矩阵

审计基线：冻结仓 `D:\workespace\ws-game-review-ac3b622`，HEAD `ac3b622041c348e87a469959c09d8a541a7c1351`，版本 `1.10.0`；原仓 `D:\workespace\ws-game` 只读。本表重新读取现行 `architecture/00`～`14`、`architecture/adr/0001`～`0017`、根 README、能力索引、模块 README 与工具链文档，再按当前源码核对；历史审计文档只作为线索，不作为现行规范。

分类含义：**默认已接线**表示至少有生产装配根直接调用；**已实现未默认接线**表示机制存在但需游戏层传入/驱动；**未实现**表示没有运行期逻辑；**游戏责任**表示框架提供参数或边界，具体内容由游戏层完成；**明确非目标**表示决策明确不在本版范围；**文档更新**表示代码边界成立但文字/注释需同步。

## 逐章矩阵

| 文档 | 当前代码对应 | 当前状态与分类 | 证据/维护动作 |
|---|---|---|---|
| 00 架构总则 | L0～L5、L-1 分层和单机/2.5D 定位均与目录及引用方向一致 | 默认已接线的框架原则；游戏专属内容仍为游戏责任 | 根 README 已把 TargetPoint 归为上层消费；编辑器、天赋等仍应单列能力缺口 |
| 01 分层与依赖 | `core`、`presentation`、`adapters`、`games` 目录及 assembly 组合根存在 | 默认已接线；assembly 豁免与模块边界可落地 | 以 `architecture/01_分层与依赖.md` 当前依赖矩阵为准，旧阶段计划中的目录估算不作现状结论 |
| 02 引擎适配层 | `INavigation2D` 含 BuildNavMesh/IsWalkable/FindPath/Raycast/SetBlocking/Clear/GetBlockingVersion；Stub 与 Unity 实现存在 | 核心接口与桩默认接线；Unity 真实运行由整体审计定向验证 | [02_引擎适配层.md](../../../../architecture/02_引擎适配层.md:148)、[INavigation2D.cs](../../../../core/foundation/engine_adapter/contracts/INavigation2D.cs:8)；Unity 文件头仍有“接口尚未出现 GetBlockingVersion”的过时措辞，需更新 |
| 03 运行时骨架 | `WorldSim` 固定步、连续/离散 `TurnScheduler`、意图路由、场景生命周期存在 | 连续/离散基础已实现；`atb` 扩展位非目标 | 不把旧计划中“暂不启用离散”的历史句当现行结论；当前模拟层 README 已说明离散已落地 |
| 04 数据与内容管线 | 合并根、框架根校验、Expr/引用登记、版本迁移与资源校验均在工具链中 | 校验主链默认已接线；`DisplayMapCoverageRule` 已实现未默认登记；孤儿记录检测未实现；部分跨表引用仅格式校验 | `WorldMapSchema` 尚未登记 `regions`/`teleport_points`/`music_ref`/`allowed_difficulties`；见能力清单 |
| 05 对象模型与世界 | Entity/地图/Spawn/WorldState/TargetPoint/移动契约均有对应类型；地图四字段仅结构声明 | TargetPoint 字段已实现，目标解析和地面点选属于游戏责任；四字段 schema 校验未实现；方向移动边界需更新文档 | [05_对象模型与世界.md](../../../../architecture/05_对象模型与世界.md:209)；`MovementTickHandler.ApplyDirectionalMove` 只检查 `IsBlockedByUnit` 后 `SetPosition`，方向移动不自动调用 `INavigation2D.IsWalkable/Raycast`，目标移动才走 FindPath |
| 06 规则层 | Stat/Power/Skill/Targeting/Combat/AI 组合根及生产链存在；`target_shape_ref` 经 `target.chain_def` 解析 | target.chain 形状查询默认已接线；`ISkillHost.FindUnits` 便利 API 未实现；视线依赖为空时为正常可选降级；天赋激活未实现 | [skill README](../../../../core/rules/skill/README.md:65) 明确只支持 chain，链内使用 Shape；`RulesSchemaCatalog.cs:172` 与 `CastPipeline.cs:254` 一致 |
| 07 载体层 | Item/Equipment/Creature/Gobj/Summon 及 AuraHandleLedger、归档字段实现 | 主体默认已接线；召唤处理器按现行注释只在连续步推进，局部离散行为边界需按需求决定；装备/外观扩展点是预留 | `SummonTickHandler.cs:53-61` 的“离散已接线、该处理器按设计跳过”文案已修；不将其扩大为整个离散模型非目标 |
| 08 玩法层 | Loot/Quest/Dialog/Difficulty/Achievement/Economy/Spawn 类型与组合根存在 | 基础流程默认已接线；`Quest.Update`、owner/day/vendor provider、采集时钟、ReplayPlayer 需游戏接入；escort 自动宿主未实现 | 空 provider/null 是可选扩展的正常默认，不单独定 bug；新局完整清理由游戏/模板负责 |
| 09 表现层 | ViewBinder、Feedback、Render、VFX/SFX、UI、model/sprite 路线与 ADR-0017 实现存在 | 基础表现默认接线；PRES-110-01 已确认同图 Load 后 weapon style 缓存残留；关键帧同步开关、FeedbackRuleValidator 等按能力清单区分 | 定向 Unity 结果与 DLL 来源见整体 [AUDIT_REPORT.md](../AUDIT_REPORT.md) 和 [presentation-findings.md](../presentation/presentation-findings.md)；全量 Unity 门禁在 check 中跳过，静态源码不替代画面证据 |
| 10 存档与持久化 | SaveSystem、KnownOrder、迁移、ReplayPlayer、`IEventBus.SuppressDispatch`、`IDerivedStateRebuilder` 存在 | 正常 Load 派生回调已实现；CORE-110-01/02 的回滚派生与跨职业残留已由当前真实 fixture 确认；CORE-180-01/02/03 已复核通过 | [IDerivedStateRebuilder.cs](../../../../core/foundation/save_system/contracts/IDerivedStateRebuilder.cs:35) 明确只在成功 Load 段回调，回滚不调用钩子；详见整体 [AUDIT_REPORT.md](../AUDIT_REPORT.md) 与 core 报告 |
| 11 工程规范与测试 | Core.sln、pytest、conformance、check.ps1、发布目录约定存在 | .NET/Python/静态门禁默认可执行；Unity 全量门禁本次由 `-SkipUnity` 跳过，定向 PlayMode 属整体审计范围 | [check-skipunity.log](check-skipunity.log)；本次实测见 validation |
| 12 扩展与变更流程 | ADR 目录、版本规则、原语审批和文档勘误流程存在 | 默认已接线的流程约束；历史条目需标清“已废止/历史” | ADR-0017 旧算法文字仅维护建议，不当作现行冲突；W9 导航新增接口已同步 CHANGELOG/02/05/模块 README |
| 13 新游戏接入指南 | 模板 GameOptions、组合根、包与数据接入步骤存在 | 接入骨架默认已接线；具体时钟、owner/day/vendor、Quest.Update、地面点选和新局 reset 是游戏责任/显式接入 | 导航策略不在模板口味字段中，而在 `MovementOptions` 装配根配置，符合 1.10 CHANGELOG |
| 14 资产规格书模板 | sprite/model、anim、VFX/SFX、UI 资产字段及导入关系存在 | 规格模板已实现；真实素材质量/游戏选型是游戏责任 | 与 ADR-0014/0017 的资产交付边界一致，不把占位资产当最终美术验收 |

## ADR 逐项复核

| ADR | 当前结论 |
|---|---|
| 0001 万物皆法术 | 事件/效果/光环注册与规则层实现存在，新增原语仍须审批。 |
| 0002 逻辑对象不依赖引擎 | `core` 未引用 Unity 运行时实现；适配层边界成立。 |
| 0003 固定步长模拟 | `WorldSim`/固定步与同平台确定性测试存在；跨平台逐位一致不作承诺。 |
| 0004 数据即内容 | DataRegistry、schema、合并校验存在；未登记 schema 字段仍按能力清单处理。 |
| 0005 一个条件语言 | ExprParser/ExprHost/校验器存在；未登记 group.key 按 ADR-0015 消歧。 |
| 0006 逻辑 ID 到 DisplayInfo | 映射与 ViewFactory 消费链存在，未映射项由数据/游戏层补齐。 |
| 0007 窄契约加事件总线 | 各模块 contracts 与 EventBus 存在；Save 抑制期内部派生改走显式 rebuilder。 |
| 0008 WorldState 替代 Phasing | WorldState/flag schema/运行期可选校验存在；具体内容标志由游戏提供。 |
| 0009 存档唯一持久化 | SaveSystem/迁移/回滚路径存在；成功 Load 回调与回滚边界以 [IDerivedStateRebuilder.cs](../../../../core/foundation/save_system/contracts/IDerivedStateRebuilder.cs:53) 为准；CORE-110-01/02 已由真实 fixture 确认回滚派生与跨职业残留。 |
| 0010 新增原语走审批 | ADR/注册表/校验/测试流程存在。 |
| 0011 引擎适配层隔离选型 | `adapters/stub`、Unity、conformance 分离；旧计划中的第三方导航方案是历史记录。 |
| 0012 双外形与固定镜头 | sprite/model 契约与固定镜头接口存在；Unity 实际画面由定向运行证据确认。 |
| 0013 时间模型可替换 | continuous/discrete 已实现，ATB 仍是明确预留扩展位。 |
| 0014 资产契约与 UI 套件交付 | toolchain、placeholder、UI 套件和包清单门禁存在。 |
| 0015 Expr 点分标识符消歧 | RulesExprSchema/DeclareReference/事件例外与代码一致。 |
| 0016 引擎适配契约联调 | SetBlocking/Clear、资源/空间查询与 conformance 已存在；W9 再补导航版本、端点和统一相交规则。 |
| 0017 model 路线与命中帧同步 | Unity model 路线、动画剪辑隔离、HitFrameSource 与默认装配开关存在；是否完整画面通过整体审计。 |

## 1.9 修复同步核对

- `TargetPoint` 已从“框架未实现”改为“字段由框架承载、目标解析由上层/游戏消费”，根 README、能力索引与 `SkillTickHandler` 当前口径一致。
- `target_shape_ref` 的 direct Shape 历史歧义已收窄为只引用 `target.chain_def`；`RulesSchemaCatalog`、`CastPipeline`、skill README 和 06 当前正文一致。
- 存档事件抑制后内部派生重算改用 `IDerivedStateRebuilder`；成功 Load 钩子与回滚不调用钩子的边界均写在接口注释中。当前 core fixture 已确认 CORE-110-01 回滚派生残留与 CORE-110-02 职业不同基础属性键/PowerTypes 残留。
- AuraHandleLedger、等级权威/同步、读档顺序与事件抑制在当前源码均有实现证据；1.9 的 CORE-180-01/02/03 已在当前 fixture 复核通过，但这些机制存在不等于每个职业/游戏组合已经验收。
- 当前 CORE-110-01/02/03 已由真实 core fixture 确认：回滚派生重建、跨职业旧键/旧 power 残留、同 tick 多 move 位移预算见 [总报告](../AUDIT_REPORT.md) 与 [core 报告](../core/core-findings.md)；当前均作为确认项。
- 旧 API 别名与参数化 API 兼容探针均按当前 DLL 复核；旧 e070 归档证据缺失单独记录为工程 P3。

## 当前开放能力清单

| 分类 | 能力与当前边界 | 代码/文档锚点 |
|---|---|---|
| 未实现 | 编辑器工具 | `editor/README.md:5` |
| 未实现 | 天赋运行时激活/点数消费/持久化 | `core/numbers/archetype/README.md:69,90`；`GameplayAssembly` 的 talentPointGranter 默认 null |
| 未实现 | `ISkillHost.FindUnits` 便利 API；不等同于 target.chain 范围查询 | `core/rules/skill/core/SkillHost.cs:169-175` |
| 未实现 | 召唤离散步 duration/跟随处理器；掉落离散步跳过清理调用 | `core/carriers/summon/core/SummonTickHandler.cs:53`；`core/gameplay/loot/core/LootExpiryTickHandler.cs:33` |
| 未实现 | 位移效果沿途碰撞 | `core/rules/skill/core/EffectDispatcher.cs` 位移分支直接写终点 |
| 未实现 | VFX anchor 持续跟随 | `presentation/vfx_sfx/core/VfxPlayer.cs` Spawn 解析一次，Update 不持续改锚点 |
| 未实现 | 孤儿记录检测 | 04 第 5 节明确仍是建议且无对应规则 |
| 游戏责任 | `TargetPoint` 可空字段已由框架承载；目标解析与地面点选消费由上层/游戏实现 | `core/rules/skill/core/SkillTickHandler.cs:13-16`；根 README 已同步 |
| 已实现未默认接线 | 采集 `SimTime` 注入、Quest.Update、owner/day/vendor provider、ReplayPlayer、FeedbackRuleValidator、DisplayMapCoverageRule | 各模块 README 与落地计划能力索引；null/未登记是正常默认边界 |
| 未接线/代码待补 | `world.map` 的 `regions`、`teleport_points`、`music_ref`、`allowed_difficulties` 未登记 `TableSchema`；部分运行时消费不代表校验已接线 | `architecture/05_对象模型与世界.md:209-218`、`WorldMapSchema.cs` |
| 明确非目标 | `time.day_cycle`、ATB；ATB 是合法预留值但 `TurnScheduler` 不支持 | `RulesExprHostFactory.cs:461-462`；`TimeModelSchema.cs`、`TurnScheduler.cs:77-81` |
| 边界待决/文档更新 | 方向移动当前只检查单位间阻挡后直接写位置，未调用导航 `IsWalkable/Raycast`；需明确补齐导航阻挡还是正式限制契约 | `MovementTickHandler.ApplyDirectionalMove:336-359`；05 第 6.1/6.4 节 |
| 游戏责任 | 新局完整 reset、最终素材/内容、可选 provider 的真实实现 | `SampleNewGameStarter.cs`；13 接入指南 |

## 需更新的文字

1. `core/carriers/unit/core/MovementHost.cs:39-42` 把 `PathFailurePolicy.Stop` 后的原因也写进 `MoveStopReason.BlockingChanged`；实际 `MovementTickHandler.HandlePathFailure:210-228` 发出 `PathFailed`，而 `BlockingChanged` 只对应阻挡策略直接 Stop，应修正文档注释。
2. `adapters/unity/.../UnityNavigation2D.cs` 文件头仍写“GetBlockingVersion 尚未出现在接口”，与当前接口默认成员及 W9 变更日志冲突，应更新历史措辞。
3. `architecture/05_对象模型与世界.md:298-300` 的“移动前单点/直线调用 IsWalkable”与方向移动实现不一致；应补方向导航检查，或经正式设计决策收窄契约，不能把当前实现描述成已有导航碰撞。
4. 05 的四个地图字段需继续明确“运行时可能已有消费，但 TableSchema 校验未登记”；不能把结构声明当作完整数据校验能力。
5. `core/gameplay/loot/core/LootExpiryTickHandler.cs:35` 注释仍写“离散时间模型本项目暂不启用”，应改为当前真实边界；`SummonTickHandler.cs:53-61` 已收窄为按设计只在连续步推进，不再列为旧文案缺陷。
