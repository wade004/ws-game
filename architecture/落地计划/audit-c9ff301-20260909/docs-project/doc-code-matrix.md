# ws-game 1.13.0 框架—游戏责任与文档—代码矩阵

基线为恢复仓 `c9ff30107413083188c597c0b65cf1691c9dfe9b`、`VERSION=1.13.0`；原仓 `D:\workespace\ws-game` 只读。本文逐章复核 architecture 00—14、19 条 ADR、editor/template/module README 和能力索引。责任分类的定义与证据限制见 [scope-and-evidence.md](scope-and-evidence.md)。本轮只复用已保存结果，不重新运行测试。

判定原则是先读架构责任，再看源码默认值和调用路径：框架已有通用机制不能因为具体游戏未接线而归为游戏缺陷；可选模块只有在启用条件成立时才适用；游戏内容、场景、资源和装配策略由游戏承担；没有运行逻辑且架构确实承诺的通用能力才列“未实现”。

## 00—14 章节矩阵

| 文档 | 框架/游戏责任与当前代码证据 | 分类 | 当前验收边界 |
|---|---|---|---|
| [00 架构总则](../../../../architecture/00_架构总则.md:18) | 明确技术无关、游戏无关；表现形态和时间模型由游戏选择，框架提供机制与配置位。 | 框架原则；游戏选择 | 不以某一游戏的技能、地图或控制方案推导框架契约。 |
| [01 分层与依赖](../../../../architecture/01_分层与依赖.md:45) | L-1 适配器隔离引擎，L0—L5 提供基础、数值、规则、载体、玩法、表现；L5 只读/订阅，状态改变走意图入口。 | 框架通用契约 | Core/adapter 依赖方向和状态边界可静态核验；具体装配由消费方完成。 |
| [02 引擎适配层](../../../../architecture/02_引擎适配层.md:23) | Unity/headless 实现隔离在 adapter；导航端点和碰撞语义是契约，跨帧预算与所有查询全索引化已收窄为实现方性能边界。 | 框架契约；可选实现 | NAV/SPATIAL 旧语义回归通过；不把同步实现或部分 bucket 索引列为现行 P2。 |
| [03 运行时骨架](../../../../architecture/03_运行时骨架.md:1) | `ContentValidationAssembly`、`GameplayAssembly`、Bootstrap loop、SceneRouter 是框架入口；状态/场景内容和选项由游戏装配。 | 框架机制；游戏接入 | 旧 teleport core oracle 通过；不要求所有游戏采用同一场景流程。 |
| [04 数据与内容管线](../../../../architecture/04_数据与内容管线.md:20) | DataRegistry、schema、校验装配和数据合并是框架；表行、引用和内容语义由消费方填写。孤儿检查在 04:310 明确为建议。 | 框架通用契约；建议 | schema/gobj/quest 的负例由框架门禁负责；孤儿检测不是本版必过门禁。 |
| [05 对象模型与世界](../../../../architecture/05_对象模型与世界.md:1) | 逻辑 Id、世界查询、Spawn/Scene/Teleport 契约由框架提供；地图内容、出生点用途和落点配置由游戏选择。 | 框架机制；游戏内容 | WorldMap 只检查已声明的结构和启用语义；不强迫所有点都有 id/position。 |
| [06 规则层](../../../../architecture/06_规则层_属性技能战斗AI.md:1) | Skill/Effect/Aura/Target/AI 通用规则由框架实现；`Script`、`SetWorldFlag` 等依赖 Hook/WorldState 的扩展由宿主注入。 | 框架机制；扩展点 | 19 effect/10 aura coverage 是框架门禁；`ISkillHost.FindUnits` 是未实现的框架便利 API，不因游戏未使用而消失。 |
| [07 载体层](../../../../architecture/07_载体层_物品生物物件.md:288) | Item/Creature/Gobj/Summon 容器、穿脱联动、召唤 follow/owner/联动、天赋被动光环复用均由框架提供；具体数据和策略值由游戏提供；affix effects 仍扩展位。 | 框架机制；游戏配置；扩展点 | 召唤连续机制不能泛化归游戏；Discrete expiration 是当前时间模型支持边界，需同步文案。 |
| [08 玩法层](../../../../architecture/08_玩法层_掉落任务对话关卡.md:328) | Quest/Dialogue/Loot/Encounter 状态机和目标枚举由框架；内容关系、owner/day/vendor 回调和玩法策略由游戏/宿主提供。相关责任见 08:338-350、391-402。 | 框架机制；扩展点；游戏内容 | `Quest.Update` 可被接入但不默认驱动；escort 自动执行仍无通用分支。 |
| [09 表现层](../../../../architecture/09_表现层.md:1) | View/反馈/VFX/SFX/Camera/纸娃娃和 model 路线机制由框架；具体资源、表现口味和可选装备/武器风格接线由游戏。 | 框架机制；可选模块 | ExistingView 只约束启用装备表现且已有 View 的 SaveLoaded 路径；不外推为所有游戏。 |
| [10 存档与持久化](../../../../architecture/10_存档与持久化.md:1) | SaveSystem、persistable 注册、失败回滚和已注册资源池持久化由框架；存档槽、地图内容和游戏专属状态由消费方定义。 | 框架机制；游戏内容 | CORE-111-01 与 VM/teleport core oracle 已保存；不推导游戏新局必须采用某种全量清理策略。 |
| [11 工程规范与测试](../../../../architecture/11_工程规范与测试.md:16) | Core build/test、数据/schema/package、可选 consumer/Unity/IL2CPP 门禁分层定义（另见 §8）；`-SkipUnity` 是显式子集。 | 框架门禁；消费方门禁 | 历史 check 为 15 PASS/6 SKIP/0 FAIL；本轮不重跑，不能称完整发布认证。 |
| [12 扩展与变更流程](../../../../architecture/12_扩展与变更流程.md:117) | ADR、新原语审批、框架/游戏提供表定义边界；游戏专属数据不应变成框架代码。 | 框架治理；游戏内容 | 文档更新只修正文案；能力扩展需按 ADR，不自动排期。 |
| [13 新游戏接入指南](../../../../architecture/13_新游戏接入指南.md:46) | 游戏层首要工作是组装；可选接入 owner/day/vendor、热重载、装备风格和 editor；模板只提供最小示例与扩展点。 | 游戏接入；可选模块 | 热重载仅 Editor/Development；authoring 文件须同步到 content root；consumer smoke 是框架基础设施的独立最小工程，不是具体游戏 E2E。 |
| [14 资产规格书模板](../../../../architecture/14_资产规格书模板.md:19) | 框架固定资产契约和 schema（另见 14:40-42）；游戏填写资源、命名、镜头、表现口味。 | 框架契约；游戏内容 | 资产占位/导入门禁通过不代表具体游戏最终艺术质量。 |

## ADR 0001—0019 矩阵

| ADR | 决策与代码/文档证据 | 分类与边界 |
|---|---|---|
| [0001 万物皆法术](../../../../architecture/adr/0001-万物皆法术.md:1) | 效果/光环/触发统一规则，Skill schema 和 dispatcher 落在 L2。 | 框架通用规则；具体效果数据由游戏填写。 |
| [0002 逻辑对象不依赖引擎对象](../../../../architecture/adr/0002-逻辑对象不依赖引擎对象.md:1) | Core contracts/hosts 不引用 Unity 对象，adapter 承载引擎句柄。 | 框架硬边界；独立 Core 证据可证明，具体引擎接线另验。 |
| [0003 固定步长模拟](../../../../architecture/adr/0003-固定步长模拟.md:1) | `WorldSim`/`IClock` 提供固定步；表现使用真实帧时间。 | 框架通用机制；游戏选择时间模型策略。 |
| [0004 数据即内容](../../../../architecture/adr/0004-数据即内容.md:1) | DataRegistry、schema、内容表和资源引用构成内容管线。 | 框架结构/门禁；游戏提供数据行和资源。 |
| [0005 一个条件语言](../../../../architecture/adr/0005-一个条件语言.md:1) | Expr host 与引用登记表统一条件求值。 | 框架契约；游戏提供合法表达式与登记引用。 |
| [0006 逻辑 id 到 DisplayInfo](../../../../architecture/adr/0006-逻辑id到DisplayInfo映射.md:1) | DisplayInfoRegistry/DisplayMap 负责逻辑到表现映射。 | 框架机制；具体 display 行和资源由游戏提供。 |
| [0007 窄契约加事件总线](../../../../architecture/adr/0007-窄契约加事件总线.md:1) | contracts 与 EventBus 解耦模块，L5 状态变更受意图入口约束。 | 框架硬边界；宿主通过公开窄契约接入。 |
| [0008 世界状态标志](../../../../architecture/adr/0008-世界状态标志替代Phasing.md:1) | WorldState/SetWorldFlag 由规则层提供，具体标志与 hook 需宿主装配。 | 框架机制+扩展点；不把默认未注入当普遍缺陷。 |
| [0009 存档唯一持久化](../../../../architecture/adr/0009-存档是唯一持久化.md:1) | SaveSystem/persistables 统一持久化路径。 | 框架通用契约；游戏定义专属 persistable 内容。 |
| [0010 新增原语走审批](../../../../architecture/adr/0010-新增原语走审批.md:1) | 新 effect/target/objective/action/AI 状态需 ADR、注册、校验和测试。 | 框架治理；内容新增优先走数据。 |
| [0011 引擎适配隔离](../../../../architecture/adr/0011-引擎适配层隔离技术选型.md:1) | L-1 接口隔离 Unity/headless/conformance。 | 框架硬边界；具体引擎实现归 adapter。 |
| [0012 双外形与固定镜头](../../../../architecture/adr/0012-表现形态双外形类型加固定镜头.md:1) | sprite 默认、model 可选；ModelCharacterRig 和 DisplayInfo 已有实现。 | 框架提供两条可选路线；游戏选路并提供资源。 |
| [0013 可替换时间模型](../../../../architecture/adr/0013-时间模型可替换即时与回合制同一规则层.md:13) | continuous/discrete 共用 tick；`atb` 是本版预留扩展位。 | 框架时间机制；ATB 明确非目标，Discrete 特定 handler 行为需文档准确。 |
| [0014 资产契约、工具与 UI 套件](../../../../architecture/adr/0014-资产契约导入工具与UI套件是框架交付物.md:1) | schema、导入工具、资产契约由框架定义，具体资产由游戏填。 | 框架交付+游戏内容；占位通过不证明成品质量。 |
| [0015 Expr 点分标识符](../../../../architecture/adr/0015-Expr点分标识符以引用登记表消歧.md:1) | Expr 引用表和点分解析避免字段歧义。 | 框架解析契约；引用目标由内容提供。 |
| [0016 适配层阶段 4 联调](../../../../architecture/adr/0016-引擎适配层契约阶段4联调补齐.md:1) | 文件根目录、空间登记、阻挡、动画/资源接口已补齐。 | 框架 adapter 契约；性能策略按 02 最新边界。 |
| [0017 model 路线与命中帧](../../../../architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md:1) | model rig、slot/socket、weapon style、可选 hit-frame source 已实现；武器风格和命中帧默认未接线。 | 框架可选表现模块；游戏启用并提供资源时才适用。 |
| [0018 编辑器随游戏走](../../../../architecture/adr/0018-编辑器随游戏走与框架为此提供的交付物.md:20) | editor 是外部消费项目；框架交付 headless adapter 与公开校验装配入口。 | 框架契约/交付；editor UI 本阶段用户暂缓，不列框架漏实现。 |
| [0019 复合字段机器可读 schema](../../../../architecture/adr/0019-复合字段子结构登记为机器可读schema.md:17) | FieldSchema 递归 Fields/Item/Variants；Skill、Gobj、Dialogue、Quest coverage tests 比较 runtime 集合，`SchemaAudit.WalkVariant` 只做结构检查。 | 框架 schema+CI 载体；当前 generic 命令不等于 runtime-set 证明，属于文档载体漂移。 |

## 能力索引逐项复核

| 能力 | 当前 source 证据 | 分类 | 责任与准确验收边界 |
|---|---|---|---|
| Talent 分配/激活/撤销/持久化 | `ArchetypeRegistry`、`ArchSchemas.cs:74-103`、`GameplayAssembly.cs:491-493`、`RewardDispatcher.cs:262-277` | 当前未提供完整管理契约；不是自动生成的本轮框架待办 | 已有字段、只读树查询、被动光环复用和奖励 granter 入口；游戏提供树/光环内容并决定是否需要点数管理。若未来承诺完整流程，先立通用契约/owner；`TalentPointGranter=null` 不能宣称到账。 |
| `ISkillHost.FindUnits` | `SkillHost.cs:169-175` | 未实现的框架便利 API | `TargetHost` 的 shape 查询已有独立路径；若承诺此 convenience API，最小 fake spatial consumer 需返回正确集合，不能以游戏未调用作为实现证明。 |
| `TargetPoint` 消费 | `SkillTickHandler.cs:16` | 已实现；可选宿主接线 | 框架承载落点字段和管线，玩家/AI/游戏适配层决定如何解析落点为目标。 |
| Summon follow/owner/联动 | `SummonHost`、07:291,303 | 框架既有通用机制；游戏提供策略值 | 连续模式 follow/联动是框架职责；owner、距离、仇恨策略是配置。 |
| Discrete summon expiration | `SummonTickHandler.cs:53-62` | 条件支持边界/文档澄清 | Discrete step 明示跳过该 handler；不能把未承诺的离散过期自动行为列为新框架 P2。若 ADR 扩大承诺，再增加通用 oracle。 |
| Discrete loot cleanup | `LootExpiryTickHandler.cs:33`、`RulesAssembly.cs:438` | 条件支持边界/文档澄清 | SimTime 继续累加但离散步跳过清理调用；分开记录判定时钟与 purge 行为，不归咎具体游戏。 |
| 武器 Swing/Impact VFX | `WeaponStyleResolver.cs:20,30`、`PresentationAssembly.cs:350` | 可选表现模块；默认未接线 | 框架解析器已存在；游戏选择接入挥击/命中反馈并提供 display 数据。 |
| gather respawn SimTime | `GobjOptions.cs:64-74` | 框架机制+宿主扩展点 | 框架提供委托；组合根/游戏注入统一时钟，默认 `() => 0` 是固定默认时钟，采集仍可执行但时间不会前进。 |
| ReplayPlayer | `ReplayPlayer.cs:57`、`DiscreteReplayTests` | 框架机制；可选调试/游戏接入 | 确定性回放本体已实现，游戏是否提供 UI/调试入口是消费方选择。 |
| 位移轨迹碰撞 | `EffectDispatcher.cs:337-341`、06:147 | 当前原语边界；未形成强制通用契约 | move 原语只保证最终逻辑落点，未证明必须沿途寻路/碰撞；若未来纳入轨迹/遮挡策略，需另立通用契约与 oracle，不能按具体游戏冲锋需求倒推默认框架缺陷。 |
| WorldMap 点元素/引用 | `WorldMapSchema.cs:20-57` | 部分框架结构校验；语义边界开放 | PointItemSchema 已是 item/Object，position 提供时 x/y 必填；是否要求 id/position 取决于首个出生点或命名引用用途，跨图/落点引用完整性需按 04/05 规则定义，不能强迫匿名点统一字段。 |
| 导航跨帧请求预算 | `UnityNavigation2D.cs:127-173`、02:23 | 现行契约不强制的性能边界 | 当前同步寻路；旧索引措辞过期，不列框架功能缺陷。 |
| 空间查询完整索引化 | `UnitySpatialQuery.cs:113-155`、02:24 | 性能建议/开放实现选择 | bucket 覆盖部分查询，其他路径可线性；不列现行 P2。 |
| `DisplayMapCoverageRule` | assembly options、04:5.1 | 框架可选 validator | source/表集合由 consumer 提供；disabled 不等于发布失败，启用后需独立 rule oracle。 |
| 孤儿记录检测 | 04:310 | 建议，非本版门禁 | 文档将其称建议；无 rule 不列框架缺陷。 |
| `FeedbackRuleValidator` | `FeedbackRuleValidator.cs:22` | 框架可选 validator | 规则本体存在但未接入统一 `IValidationRule`；启用方显式调用，不能称默认门禁。 |
| `SpawnSummonOnlyCreatureRule` | `SpawnValidationRules.cs:103-121`、`GameplaySchemaCatalog.cs:94,186-188` | 框架可选 validator | 需传入非 null creature query；默认 disabled 是装配选项，不是所有游戏必开。 |
| owner/day/vendor | `GameplayAssembly.cs:539-540,718`、三处组合根透传点 | 框架扩展点；游戏按需接入 | 框架提供签名和透传；游戏提供 resolver/provider/callback；默认未提供业务回调或使用固定默认值，需按所选语义注入。 |
| SampleNewGameStarter 新局 | `games/_template/Runtime/SampleNewGameStarter.cs:8-22` | 最小模板示例；游戏责任 | 只重置位置/地图/模板是可替换示例的设计，不是框架缺陷；游戏定义自己的新局策略和 persistable 清单。 |
| `day_cycle` | `RulesExprHostFactory.cs:461-462` | 明确非目标 | 本版恒 0；不与 `SimTime` 或离散 turn 字段混淆。 |
| ATB/先攻扩展 | `TimeModelSchema.cs:23`、`TurnScheduler.cs:77-81`、ADR0013:15 | 明确非目标 | initiative/fixed/action points 是框架已支持的离散策略；ATB 预留且本版不展开。 |
| Editor 工具 | `editor/README.md:5,7-9`、ADR0018 | 用户暂缓 | editor UI 属外部游戏项目；框架只承担契约、headless 和 assembly 交付。 |
| `Quest.Update` | `QuestHost.cs:386` | 框架机制；游戏选择驱动时机 | 框架已有 Update；游戏决定固定步/地图切换/触发协议并接入，不默认驱动不等于缺失。 |
| escort 自动执行 | `IQuestHost:25-39`、08:91,105,114；`QuestHost` 无 route provider | 框架提供进度/Fail API；路线自动化是游戏集成边界 | `UpdateProgress`/显式失败可供宿主驱动，通用 route provider 和自动路线没有形成契约；不因某游戏未接线列框架缺陷。 |
| VFX anchor 持续跟随 | 09:280,297-300；`VfxPlayer.cs:113-145,315-354` | 已有可选表现契约的静态差距；非本轮新 Runtime P2 | 09 明确 anchor/socket 是跟随语义，但实现只在 Spawn 解析，Update 只做回收/pending；框架表现模块若保留该契约应补持续更新或同步收窄描述，具体游戏未启用不构成通用 E2E 失败。 |

## 文档与 metadata 更新清单

| 文件/行 | 当前漂移 | 建议文案 | 责任 |
|---|---|---|---|
| `CHANGELOG.md:421-431` | “无任何公开签名删改”覆盖不了旧 ctor 与五个 public rule 删除 | 明确已编译 consumer 的 ABI 风险、源码重编译条件和迁移类型 | 框架发布文档 |
| `games/_template/README.md:159-170`、13:136 | 未把 authoring 文件与实际 watcher 根区分 | 写明同步到 `Application.streamingAssetsPath/GameFoundation/data/...` 后 reload；仅 Editor/Development | 模板/接入文档 |
| ADR0019、toolchain README | generic schema-audit 与 runtime-set coverage 混写 | 明确 coverage tests 是集合门禁，generic 命令是结构审计；若要命令兜底再另立实现 | 框架文档 |
| `ArchSchemas.cs:45-49,95-103` | “未登记”/grants 容易误读为字段缺失或奖励可分配 | 区分跨层引用不重复登记、树只读查询和未实现 talent runtime | metadata |
| `SkillSchemas.cs:231-239,67,327-333` | 扩展点/运行时消费与 Periodic metadata 不一致 | 说明 Hook/WorldState 由宿主注入；周期 params 登记 optional `Reference(stat.definition)` | metadata+schema |
| `QuestSchemas.cs:200-206`、quest README | union 约束延迟到 parser | 写明 Bool/Number/String/`{$id}` 联合；formal assembly 对 `[]` 必须 blocking/error | validator + 文档 |
| `WorldMapSchema.cs:14-30,43-57`、能力索引 1320-22 | 旧文案称只有数组容器 | 写明 PointItemSchema 当前部分结构校验，并以首个出生点/命名引用场景定义语义必填 | metadata/validator |
| `RewardSchemaFields.cs`、quest README | 旧文案称 common rewards 不能嵌套 | 说明 common helper 保持裸 Object，使用方可复用带 Fields 的 RewardsFields | metadata |
| `SceneRouter.cs:24-33`、`AppStateMachineConfig.cs:33,53` | 旧注释称 Default 不含 Loading→MainMenu | 更新为当前 Default 已允许；宿主仍可扩展其他状态 | metadata |
