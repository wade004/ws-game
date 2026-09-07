# 文档—代码矩阵（v1.3.0 基线）

基线：HEAD `5c444f1dc2f3729c8a03a68f78cacc28d24c2a2f` / VERSION `1.3.0`，代码证据取自本目录的 `source_zip_extract` 锁定归档。live 工作树存在来源未归因的并行未提交修改，本矩阵不纳入；本矩阵把架构规格、当前实现、默认装配、真实证据和需更新项分列；“实现”不代表“默认接线”，“测试存在”不代表 Unity/独立消费方已通过。源文件链接均指向只读归档。

`source_zip_extract` 同时含本审计追加的 probe、测试工程引用和验证辅助文件；生产源码证据取固定 HEAD 文件。总报告见 [AUDIT_REPORT.md](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/AUDIT_REPORT.md)，运行验证见 [validation.md](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/validation.md)，工作树快照见 [working-tree-drift.md](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/working-tree-drift.md)。

## 00–14 架构章节

| 章节 | 当前文档规格 | 当前代码/实现观察 | 默认接线与边界 | 真实 Unity/消费方证据 | 需更新或验收 |
|---|---|---|---|---|---|
| 00 总则 | 分层、解耦、固定步、数据化内容、存档唯一持久化、ADR 入口。 | 总则与 00–14 文件齐全；不能由文档存在推出全项目完成。 | 游戏 provider、内容和 model 资源仍取决于装配根。 | 本轮未运行 E3 Unity 纵切；只完成只读盘点。 | 补证据等级和“实现/默认/真实”三分法。 |
| 01 分层与依赖 | L0–L5 单向依赖；L5 只读查询、窄命令和事件。 | 正文窄命令已修；图解 01:201/208、03:113/155 仍有绝对只读/唯一反向事件表述。 | UI intent 等窄提交需按实现和图解一致表述。 | 本轮未运行 Unity/消费方意图闭环。 | 同步图解与正文，补窄命令例外。 |
| 02 引擎适配层 | `IResourceLoader` 含 model；`IRenderer3D` 选 model 时必需；资源首次引用异步加载，renderer 消费已加载/占位。 | `UnityRenderer3D` 有真实模型实例/动画/槽位/挂点实现，但冷缓存路径同步 `Resources.Load` 且失败抛异常；model 与 camera 平面约定不一致风险。 | 三处装配根传入 renderer3D，但资源、model handle、hit frame/provider 仍需核对。 | 本轮未运行对应 Unity 场景；已有测试文件不等于已执行，单个测试也不覆盖整条路线。 | 修资源缺失降级、统一坐标，更新 02 与包 README 的历史降级语句。 |
| 03 运行时骨架 | 生命周期、固定步、连续/离散时间模型、WorldSim、场景路由。 | CR130-03 覆盖 Cooldown/Proc ICD/school lock 混合时间折算；CR130-04 覆盖 channel 剩余时间与周期结算；其余运行时骨架机制存在。 | Template/Unity bootstrap 只提供最小内容，完整玩法 provider 不是默认事实。 | 本轮未运行完整 Shell/战斗/存档 Unity 纵切；E1/E2 结果以对应条目为准。 | 统一所有时间家族的 rescale 与 `Min(dt,Remaining)` 语义。 |
| 04 数据与内容管线 | schema、引用、Expr、DisplayInfo、anim_set/equip_visual、model 资源引用。 | `display.anim_set`/`equip_visual` 类型和校验已存在；默认资源登记与 model 资源加载链仍需对齐。 | `UnityViewFactory` 需按 display/weapon/equip provider 实际传入；不存在的 provider 必须显式记边界。 | 数据校验不能证明模型/动画实际播放。 | 补资源首次加载责任、剪辑登记键、默认装配和缺失降级示例。 |
| 05 对象模型与世界 | Entity、2.5D 平面、地图、Spawn、导航、AreaTrigger、WorldState。 | 对象/空间接口目录存在；model/camera 坐标风险影响点击/投影，但逻辑层仍应只使用 `Vec2`。CR130-05 覆盖 custom teleport 双消费。 | 地图/导航/传送完整链路取决于当前装配。 | 本轮未运行真实地图点击/场景路由纵切。 | 明确表现坐标转换不回流逻辑层，修复唯一传送消费。 |
| 06 规则层 | Stat/Power/Skill/Combat/Targeting/AI 契约与固定步骤。 | CR130-03/04 覆盖时间家族和引导周期；CR130-02 覆盖技能来源持久化边界；其余规则以当前源码重新核对，旧 1.1.0 FR 结论不直接沿用。 | `FindUnits` 等能力边界需准确写成当前实现行为，不能暗示仅 provider 未注入。 | 本轮未运行完整战斗 Unity 场景；机制证据与静态证据分列。 | 修复时间折算、channel 周期和技能来源持久化，并补规则消费方场景。 |
| 07 载体层 | Item/装备/Creature/GameObject/召唤与外形。 | `UnityModelView` 装备事件处理已实现，但 socket 清理使用事件 slot 而非 EquipVisualDef.SocketId，见 PR130-07。 | 默认 factory 未注入装备外观表；“model 装备已显示”不能写成默认能力。 | 本轮未运行对应 Unity 装备/替换/卸载场景；已有测试文件不等于已执行。 | 补可逆外观索引、provider 来源与默认接线。 |
| 08 玩法层 | Loot/Quest/Dialog/Encounter/Difficulty/Achievement/Economy/Spawn。 | CR130-01 覆盖 Economy→Quest 失败交易事件边界；CR130-05 覆盖 custom teleport listener 双消费；旧 1.1.0 发奖问题不直接继承。 | 自动 Quest、日历、商店/owner 等仍按当前装配边界列为能力项。 | 本轮未运行独立消费方完整玩法演练。 | 修复购买事务/传送唯一消费，并补失败与同图/跨图场景。 |
| 09 表现层 | sprite/model 混合渲染、CharacterRig、动画、命中帧、武器 style、VFX/SFX/UI。 | `ModelCharacterRig`/`IRenderer3D`/`HitFrameReached` 等机制存在；PR130-01～08 覆盖坐标、完成回落、剪辑登记、反馈批次、资源加载、装配 provider、socket 清理、影子高度。 | `HitFrameSync`、weapon/equip provider、renderer3D 是否同一实例必须按三处生产根核对。 | 本轮未运行对应 Unity 场景；U01–04 静态修复提示不等于完整表现路线已执行。 | 逐项修 09/ADR-0017 文案与测试，不能将 3D 整体列为未实现。 |
| 10 存档与持久化 | 版本/迁移、meta/player/world/rng、确定性与回放。 | CR130-02 暴露一次性奖励技能来源分类与恢复差异；Save schema 与 Replay 机制本体存在。 | Replay 仍未生产接入；测试构造不代表 Shell/游戏生产入口接入。 | 本轮未运行新局→存档→读档→退出重入 Unity 纵切。 | 修复来源持久化语义，补同宿主/新宿主恢复一致性。 |
| 11 工程规范与测试 | 测试分层、门禁、版本不可变、MAJOR/MINOR/PATCH 判据。 | `architecture/11:156` 已定义契约签名不兼容为 MAJOR；VERSION 1.3.0 却新增强制 `ICharacterRig.HitFrameReached`（PJ130-04）。 | check 的 `-SkipUnity` 只能证明非 Unity 子集；发布附件与 package 需同批次核验。 | SkipUnity 门禁 14 PASS/6 SKIP；实际 ZIP 的 get_framework 六 hash 通过；本轮未执行 Unity/consumer smoke。 | 修版本契约或提供可选接口迁移；同步 CHANGELOG/package/lock。 |
| 12 扩展与变更流程 | ADR、版本规则、实现不一致处理。 | ADR-0017 已登记 model/命中帧结构变化；历史落地计划仍有旧降级描述。 | 新增 provider/签名的兼容性和默认接线需在 ADR/README 同步。 | ADR 不替代运行证据。 | 把历史规划标历史，补本轮发现和验收。 |
| 13 新游戏接入 | 选引擎、组装、数据、表现、存档、E2E、资产验收。 | 文档宣称的消费流程依赖 dist 中资源、TMP 和 provider；PJ130-02 证明 model 占位生成器/资源未随当前 ZIP/UPM 交付。 | Template 是最小骨架，未默认提供所有玩法与 model 资源接线。 | 现有 ZIP 的离线 get_framework 与工具链校验通过；本轮未运行 Unity 消费方演练。 | 收窄“可接入”声明，补 model/动画资源与完整 E2E 门槛。 |
| 14 资产规格书 | sprite/model 挂点/槽位/命中帧命名、资源目录、导入工具。 | model 资产命名和 `display.anim_set` 字段已描述；实际 dist 打包遗漏 model/anim/editor generator，PJ130-02。 | 规格书默认展开路线为 sprite；选 model 还需要真实 `IRenderer3D` 与对应资产。 | 既有 ZIP/UPM 对模型资源和生成器精确匹配数为 0；本轮未执行 Unity model 场景。 | 更新资源交付清单、坐标/影子/动画完成时序与默认接线说明。 |

## ADR-0017 对照

| 决策 | 文档要求 | 源码状态 | 当前风险/验收 |
|---|---|---|---|
| 1 Model 资源 | 首次引用者 `loadAsync(Model)`；renderer 消费已加载/占位、不隐式加载、不抛。 | `UnityRenderer3D.CreateModelInstance` 冷缓存同步 `Resources.Load`，失败抛。 | PR130-05；做冷启动/缺失资源 E3。 |
| 2 model 默认路线 | 选 model 必须有真实 `IRenderer3D`。 | renderer/model rig 代码存在；不等于坐标、生命周期、默认 provider 完整。 | PR130-01/02/06/08；禁止把 3D 整体写成未实现。 |
| 3 统一命中帧 | `ICharacterRig.HitFrameReached`，sprite/model 均可触发。 | 成员与触发通道存在。 | 结合 PR130-04 批次释放和 E3 事件时序验收。 |
| 4 命中反馈批次 | 同一规则动作一起延迟，0.5 秒兜底。 | Binder 按规则建等待项，Policy 每次命中帧首项释放。 | PR130-04；双规则、多目标、超时逐批验收。 |
| 5 实体→武器 style | 装备事件失效缓存，解析主手 style。 | `IWeaponStyleSource`/resolver 代码存在；默认 factory provider 需核。 | PR130-03/06/07；装备换装后动画和特效来源验证。 |
| 6 StateChangedWithSkill | 新增带 skill 事件且保留旧事件。 | `AnimStateMachine` 已背靠背触发两事件。 | PR130-02/03 说明消费端完成时序、剪辑登记仍未闭合。 |

## 当前能力边界（与缺陷分开）

以下是文档/主审明确要求保留的“预留或未默认接入”条目，不应改写成运行时缺陷；每项列出当前源锚点和边界。新增 weapon swing/impactVFX 无生产消费者、默认 factory 未传 equipVisual 表也独立列为接线边界，不能与“ModelCharacterRig/IRenderer3D 已有实现”混淆。

| 能力边界 | 当前源锚点 | 当前状态与验收边界 |
|---|---|---|
| 编辑器工具 | [editor/README.md:5](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/editor/README.md:5) | 只有产品文档，实现尚未开始。 |
| 天赋激活/撤销/存档 | [archetype/README.md:69](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/numbers/archetype/README.md:69)、`:90` | 当前仅树查询，没有点数学习、激活、撤销或存档段。 |
| FindUnits | [SkillHost.cs:154](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/rules/skill/core/SkillHost.cs:154)–161 | 当前实现无条件返回空并记诊断，不能写成仅未注入 `ISpatialQuery`。 |
| TargetPoint 地面点选 | [SkillTickHandler.cs:16](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/rules/skill/core/SkillTickHandler.cs:16) | `cast.point` 不由 SkillHost 消费，需更上层转换为具体目标。 |
| 离散召唤/掉落过期 | [SummonTickHandler.cs:53](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/carriers/summon/core/SummonTickHandler.cs:53)、[LootExpiryTickHandler.cs:33](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/gameplay/loot/core/LootExpiryTickHandler.cs:33) | Discrete 步直接跳过，只有 Continuous 推进。 |
| 位移轨迹碰撞 | [EffectDispatcher.cs:327](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/rules/skill/core/EffectDispatcher.cs:327)、`:346`、`:358`、`:373` | 位移直接 SetPosition 到终点，不判沿途碰撞/遮挡。 |
| 采集时钟 | [GobjOptions.cs:64](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/carriers/gobj/contracts/GobjOptions.cs:64)–74 | 未注入 SimTime 时恒为 0，`respawn_after_use` 不刷新。 |
| 日任务/自动 Quest | [GameplayAssembly.cs:518](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/gameplay/assembly/GameplayAssembly.cs:518)–519、`:694` | dayProvider/ownerResolver 等默认 null，Quest.Update 无生产自动调用。 |
| 商店入口 | [GameplayAssembly.cs:694](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/gameplay/assembly/GameplayAssembly.cs:694) | vendorOpenRequested 未默认接线。 |
| 新局完整重置 | [SampleNewGameStarter.cs:38](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/games/_template/Runtime/SampleNewGameStarter.cs:38) | 只处理位置、地图、模板，库存/任务/货币/生命等需游戏层补齐。 |
| VFX 锚点跟随 | [VfxPlayer.cs:144](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/presentation/vfx_sfx/core/VfxPlayer.cs:144) | anchor 只在 Spawn 解析一次，移动后不跟随。 |
| day_cycle | [RulesExprHostFactory.cs:461](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/rules/expr_host/RulesExprHostFactory.cs:461)–463 | `time.day_cycle` 恒为 0，无接线机制。 |
| ATB | [TimeModelSchema.cs:23](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/foundation/sim_loop/schema/TimeModelSchema.cs:23)、[TurnScheduler.cs:77](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/foundation/sim_loop/core/TurnScheduler.cs:77)–81 | schema 接受 atb，但 Configure 抛 NotSupported，属于预留位。 |
| 孤儿/DisplayCoverage 检查 | [04_数据与内容管线.md:237](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/architecture/04_数据与内容管线.md:237)、`:242` | DisplayCoverage 需调用方登记，孤儿检测仍无实现。 |
| FeedbackRuleValidator 默认注册 | [presentation/assembly/README.md:122](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/presentation/assembly/README.md:122)、[FeedbackRuleValidator.cs:22](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/presentation/feedback_binder/core/FeedbackRuleValidator.cs:22) | 校验器存在，但 PresentationSchemaCatalog 未默认接入。 |
| Replay 生产接入 | [ReplayPlayer.cs:57](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/core/foundation/save_system/core/ReplayPlayer.cs:57) | 机制和单测存在，没有游戏 UI/调试/命令行生产入口。 |
| weapon swing/impactVFX 消费 | [WeaponStyleResolver.cs:20](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/presentation/vfx_sfx/core/WeaponStyleResolver.cs:20)–30 | 能力定义与测试存在；全仓生产调用方仍缺，不能写成武器动画本身未实现；与 PR130-03/06 分开记录。 |
| equipVisual 默认 provider | [UnityViewFactory.cs:166](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5c444f1-20260908/source_zip_extract/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:166)–176、`:263` | UnityViewFactory 构造函数没有 equipVisual 参数，new UnityModelView 未传映射；UnityModelView 本身支持可选映射，默认 factory 仍缺入口。 |

## 模块与链接漂移扫描

当前发现的文档问题包括：根 README 仍写 ADR 16 条和 19 项未默认能力；落地计划旧 `IRenderer3D` 降级段落未标历史；能力索引 `FindUnits` 描述不准确；图解 01/03 与正文窄命令/反向事件语义不一致；02/09/14/ADR-0017 与实际坐标、资源加载、完成回落、批次同步和装配 provider 需同步。另有 13 个相对文件链接失效（只做文件存在性扫描，未宣称锚点有效），典型是落地计划中的 `adr/0017` 层级应改为 `../adr/0017`；`core/foundation/display_info/schema`、`save_system/schema/save_slot_meta`、`scene_router/schema`、`sim_loop/schema` 的 `../../../architecture` 少一层，应按实际文件路径调整。

| 失效引用位置 | 当前错误写法 | 应改写为 | 扫描口径 |
|---|---|---|---|
| architecture/落地计划/落地方案与分阶段计划.md:26、1155、1171 | `adr/0017...` | `../adr/0017...` | 146 篇非历史审计 Markdown，按相对文件存在性扫描；忽略锚点、外部链接和绝对路径。 |
| core/foundation/display_info/schema/README.md:3、4 | `../../../architecture` | `../../../../architecture` | 同上。 |
| core/foundation/save_system/schema/save_slot_meta.md:3、4、5、29 | `../../../architecture` | `../../../../architecture` | 同上。 |
| core/foundation/scene_router/schema/README.md:3、4 | `../../../architecture` | `../../../../architecture` | 同上。 |
| core/foundation/sim_loop/schema/README.md:4、6 | `../../../architecture` | `../../../../architecture` | 同上。 |

## 证据与验收规则

E1 机制探针可证明局部触发/终态；E2 静态调用链可证明缺少接线或契约冲突；E3 才能证明 Unity/消费方实际行为。Unity 证据至少覆盖 model/sprite 共平面投影、冷启动/缺失资源、Attack 完成回落与重播、HitFrame 多规则/多目标、socket 装备替换卸载、height 影子和三处默认装配。发行证据至少覆盖同一构建批次的 zip/lock/tgz、完整占位 model 资源/生成器、离线消费方加载与 registry 无关进程保护。通过 .NET、数据校验或现有测试不能替代这些边界。
