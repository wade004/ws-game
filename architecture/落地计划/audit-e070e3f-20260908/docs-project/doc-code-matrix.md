# v1.8.0 文档—代码对照矩阵（e070e3f）

审计对象是冻结仓 `D:\workespace\ws-game-review-e070e3f` 的 `v1.8.0/e070e3f`。本表重新读取 `architecture/00`～`14`、`architecture/adr` 的 17 条 ADR、架构 README、根 README、`architecture/落地计划/落地方案与分阶段计划.md`、`toolchain/README.md` 及工具链代码后建立；不把旧审计结论直接当作当前事实。行中的“已实现且默认接线”要求存在至少一条生产装配根调用链；“已实现未默认接线”表示 API/机制已有但消费方必须显式注入；“未实现”表示当前框架没有该运行时逻辑；“明确非目标/游戏责任”不作为框架缺陷。

## 逐章对照

| 文档 | 当前规范要点 | 代码/配置证据 | 当前判定 |
|---|---|---|---|
| 00 架构总则 | 引擎无关、数据驱动、固定步模拟、窄契约与事件 | `core/*` 六个核心程序集不引用 Unity；`core/foundation/sim_loop`、各装配根 | 已实现且默认接线（静态结构）；全量 Unity 门禁本次 SKIP，定向 PlayMode 去重回归 58/58，真实游戏运行仍未覆盖 |
| 01 分层与依赖 | L0～L5 单向依赖，适配层隔离引擎 | `Core.sln` 项目引用；`presentation/Presentation.Common.csproj`；Unity 只引用包 | 已实现且默认接线（构建验证通过） |
| 02 引擎适配层 | `IResourceLoader`、空间查询、渲染/音频/输入/场景契约；`animation_clip` 事件合并与按 anim_set 隔离 | `adapters/unity/.../Runtime`；`AnimClipResolver.cs`、`UnityViewFactory.cs`；文档正文已写“任何非空配置克隆、不写共享资产”（第 1.7 节） | 契约/实现已实现；默认根配置存在。整体审核定向 PlayMode 去重回归 58/58；全量 Unity 门禁仍属 SKIP；ADR-0017 修订记录的旧算法只作历史维护项 |
| 03 运行时骨架 | Bootstrap 顺序、固定步循环、输入/场景路由 | `GameFoundationBootstrap.cs`、`FrameworkResidentHost.cs`、`games/_template/Runtime/GameBootstrap.cs`、`core/foundation/sim_loop` | 已实现且默认接线；`GobjOptions.SimTime` 在三根默认值为冻结 `() => 0`，采集时钟列为已实现未默认接线 |
| 04 数据与内容管线 | schema、引用登记、校验、资产导入 | `toolchain/validate_data.py`、`import_assets.py`、`RulesSchemaCatalog.cs`、`data/_framework`/`data/_sample` | 已实现且默认接线（check 的数据、常量、占位资产、导入检查均通过）；编辑器实现仍未开始 |
| 05 对象模型与世界 | Unit/Gobj、地图/位置、空间登记、生命周期 | `WorldSim`、`WorldUnitAccess`、`GameObjectHost`、`ISpatialQuery` 登记调用 | 已实现且默认接线；空间查询委托对 `ISkillHost.FindUnits` 尚未接通 |
| 06 规则层：属性、技能、战斗、AI | 属性/等级/技能/目标链/战斗事件/AI；`TargetPoint` 施法请求字段 | `architecture/06_规则层_属性技能战斗AI.md:126,223` 现行文字仍说 `target_shape_ref` 可直接指向 Shape；`RulesSchemaCatalog.cs:161-172` 只登记 `target.chain_def`，`CastPipeline.cs:255` 按链 id 调 `TargetHost.Resolve` | 主干已实现且默认接线；`FindUnits` 恒返回空（未实现）；技能管线的 `TargetPoint` 消费仍需上层辅助施法适配，根 README 当前措辞需更新；文档直接 Shape 分支与 schema/管线当前实现不一致 |
| 07 载体层：物品、生物、物件 | 装备/掉落载体、Gobj、召唤、持久化分段 | `EquipmentHost`、`CreatureHost`、`GameObjectHost`、`SummonHost` | 已实现且默认接线；武器外观/动画能力机制已有，具体游戏数据与消费方选择属游戏责任 |
| 08 玩法层 | loot、quest、dialog、encounter、world state 等玩法服务 | `GameplayAssembly.cs`、`LootHost`、`QuestHost`、`DialogHost`、`EncounterHost` | 已实现且默认接线；`Quest.Update` 没有框架生产 tick 调用，owner/day/vendor 回调默认为 null 属游戏接线责任，不据此报框架 bug |
| 09 表现层 | 2D/3D 外形、装备合成、动画状态、VFX/SFX、命中帧 | `PresentationAssembly.cs`、`UnityRenderer3D.cs`、`UnityModelView.cs`、`AnimClipResolver.cs`、`VfxPlayer.cs` | 2D/3D、装备/武器动画、关键帧机制已实现；整体审核定向 PlayMode 去重回归 58/58，非完整 Unity 门禁；VFX anchor 持续跟随仍未实现 |
| 10 存档与持久化 | 分段快照、版本迁移、备份回退、失败回滚、读档期间事件抑制 | `SaveSystem.cs`、`SaveSections.cs`、`IPersistable.cs`、`ProgressionPersistable.cs`、`EventBus.SuppressDispatch` | 存档主流程、回滚与抑制已实现且默认接线；抑制期间 `ProgressionRestoredEvent`/`StatChangedEvent` 会被丢弃，真实 Load 已确认 CORE-180-01/02/03（见 [core findings](../core/core-findings.md)） |
| 11 工程规范与测试 | Core 构建/测试、Python 校验、Unity 分层验证、证据边界 | `check.ps1`、`build.ps1`、`toolchain/tests`、六个 Core.Tests 项目 | 静态门禁已通过；全量 Unity/standalone smoke 本轮明确 SKIP，整体审核另有定向 PlayMode 去重回归 58/58，不宣称完整 Runtime 门禁 |
| 12 扩展与变更流程 | ADR、版本、兼容性、变更审批与迁移 | `CHANGELOG.md`、`VERSION`、`build.ps1`、`toolchain/get_framework.ps1`、旧 API 别名 | 已实现；旧 API 别名仍可编译（带过时警告），current17-only 归档项目的绝对 HintPath 不可移植，属于审计证据/工程清理项 |
| 13 新游戏接入指南 | zip/registry 两通道、锁文件、模板 Bootstrap、接入步骤 | `games/_template`、`toolchain/get_framework.ps1`、`toolchain/registry`、模板 README | 接入机制与模板已实现；全量门禁 `-SkipUnity`，消费方 rehearsal 未覆盖；`-SkipUnity` 检查目录与原发布快照存在构建路径/Unity 元文件差异，属于验证边界，原始 ZIP 与 lock 同源已核对 |
| 14 资产规格书模板 | model/equip_visual/anim_set/VFX/SFX 等资产合同与验收字段 | `data/_framework` schema、`data/_sample/display`、Unity 资源解析器、占位资产脚本 | 资产合同、示例和工具链校验已实现；编辑器仍未实现；真实美术资源与游戏内容不属于框架验证 |

## ADR 对照

| ADR | 决策与代码证据 | 判定 |
|---|---|---|
| 0001 万物皆法术 | 技能/效果/Aura/Proc 统一由 `SkillHost`/`EffectDispatcher` 驱动 | 已实现且默认接线 |
| 0002 逻辑对象不依赖引擎对象 | Core 程序集无 Unity 引用，Unity 逻辑通过适配契约 | 已实现且默认接线 |
| 0003 固定步长模拟 | `SimLoop`/`SimTime`/计时器按固定步推进 | 已实现；全量 Unity 门禁 SKIP，定向 PlayMode 去重回归 58/58 |
| 0004 数据即内容 | JSON/表格注册、schema validator、数据引用表 | 已实现且默认接线 |
| 0005 一个条件语言 | `Expr` parser/evaluator 与引用登记表 | 已实现且默认接线 |
| 0006 逻辑 id 到 DisplayInfo | `DisplayInfoRegistry`、ViewFactory/Renderer 解析 | 已实现；真实资源运行未验证 |
| 0007 窄契约加事件总线 | `IEventBus`、契约接口、生产装配订阅 | 已实现且默认接线 |
| 0008 世界状态标志替代 Phasing | `WorldState`/flag 与玩法查询 | 已实现；内容登记由游戏负责 |
| 0009 存档是唯一持久化 | `SaveSystem` 分段、备份、回滚；无额外框架持久化 | 已实现；真实 Load 已确认 CORE-180-01/02/03，详见 [core findings](../core/core-findings.md) |
| 0010 新增原语走审批 | ADR/架构落地计划记录，接口按模块契约增加 | 流程已实现，无法由本次静态 check 证明组织审批 |
| 0011 引擎适配层隔离技术选型 | Unity 包承载 adapter，Core/Presentation.Common 引擎无关 | 已实现且默认接线 |
| 0012 双外形类型加固定镜头 | sprite/model 双路、`UnityRenderer3D`、固定二维逻辑平面 | 机制已实现；全量 Unity 门禁 SKIP，定向 PlayMode 去重回归 58/58 |
| 0013 可替换时间模型 | 即时模型已实现；`TurnScheduler` 对 ATB 明确 `NotSupportedException` | 明确非目标（ATB）；即时路径已实现 |
| 0014 资产契约、导入工具与 UI 套件 | validator/importer/placeholder/template 包 | 工具链已实现；editor UI 实现未开始 |
| 0015 Expr 点分标识符以引用登记表消歧 | `RulesSchemaCatalog`/reference registry 对点分 id 分类 | 已实现且默认接线 |
| 0016 适配层契约阶段 4 联调 | `ISpatialQuery`、导航、资源、场景契约及 Unity adapter | 契约/接线已实现；整体审核定向 PlayMode 去重回归 58/58，全量 Unity 门禁仍 SKIP；`FindUnits` 仍是未实现能力 |
| 0017 模型型外形与命中帧同步 | model 默认路线、weapon style、anim set 事件合并/隔离、hit frame | 代码与 02 正文已按当前算法实现；ADR 修订记录中仍有旧“首个免克隆”文字，需维护时统一历史口径 |

## 当前能力边界（以代码为准）

| 分类 | 能力 | 证据 | 结论 |
|---|---|---|---|
| 未实现 | 编辑器 | `editor/README.md:5` | 明确未实现 |
| 未实现 | 天赋运行时分配/激活/持久化 | `core/numbers/archetype/README.md:69,90`；`GameplayAssembly.cs:507` | 明确未实现 |
| 未实现 | `ISkillHost.FindUnits` 空间查找 | `core/rules/skill/core/SkillHost.cs:169-176` | 明确未实现；不等同于 `ISpatialQuery` 整体未实现 |
| 未实现 | 孤儿记录检测 | `architecture/04_数据与内容管线.md` 仅列建议；当前 validator/规则注册未提供 orphan detector | 当前未实现，需独立工具/规则设计 |
| 游戏责任/上层适配 | `SkillCastRequest.TargetPoint` 的地面点选转换 | `SkillCastRequest.cs:21-33`；`SkillTickHandler.cs:13-16` | 框架承载字段；消费点/目标单位解析由 AI/玩家辅助施法层完成。根 README 的“框架未实现地面点选”措辞需改为此边界 |
| 处理器未支持/跳过离散步 | 召唤/掉落离散步过期推进 | `core/carriers/summon/core/SummonTickHandler.cs:53`；`core/gameplay/loot/core/LootExpiryTickHandler.cs:36` | 当前框架已有基础 Discrete；这两个 handler 仍直接跳过 Discrete，不能外推为整个离散模型非目标；旧“本项目未启用离散模型”诊断文案应更新 |
| 已实现未默认接线 | weapon style / impact VFX | `WeaponStyleResolver.cs:20,30`；`PresentationAssembly.cs:350` | 机制存在，需生产调用/数据 |
| 已实现未默认接线 | gather clock | `GobjOptions.SimTime` 默认 `() => 0`；三装配根默认不注入 | 游戏/宿主需注入时钟 |
| 已实现未默认接线 | ReplayPlayer | 仅测试构造，未见生产根调用 | 需游戏接线 |
| 未实现 | displacement trajectory collision | `EffectDispatcher.cs:327,346,358,373` | 明确未实现 |
| 已实现未默认接线 | Quest.Update、owner/day/vendor 回调 | `GameplayAssembly.cs:539-540,718` 及各根可空回调 | 游戏责任；null 是正常可选依赖 |
| 游戏责任/模板未提供 | new-game full reset | `SampleNewGameStarter.cs:38` | 模板启动器未提供完整 reset；新游戏流程由消费方负责，不作为框架缺陷 |
| 未实现 | VFX anchor continuous follow | `VfxPlayer.cs:144,315-354` | 明确未实现 |
| 明确非目标 | `day_cycle`、ATB | `RulesExprHostFactory.cs:461-462`；`TimeModelSchema.cs:23`、`TurnScheduler.cs:77-81` | 当前版本不展开 |
| 已实现未默认接线 | `DisplayMapCoverageRule`、`FeedbackRuleValidator` | `PresentationSchemaCatalog` 未默认登记 | 机制存在，需显式登记 |
