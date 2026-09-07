# 文档—代码矩阵

基线：HEAD `5e779c600d7dd3c9ef995d34e844c48641f20a85` / VERSION `1.1.0`。矩阵按 00–14 架构章节、模块 README 和能力边界核对，引用实际源码锚点。它描述当前实现和文档可信度，不能把文档存在、静态校验或 .NET 全绿升级为 Unity Runtime 或完整游戏证明。

## 00–14 架构章节

| 章节 | 设计规格 | 已实现机制 | 默认接线 | 真实运行证据 |
|---|---|---|---|---|
| 00 架构总则 | 分层、复用和能力边界总则。 | 分层/契约目录存在；不能由“框架完成”推出内容、provider、表现或生命周期完成。 | 无统一游戏 provider/内容入口证明。 | 未作 Unity/完整游戏纵切；总则应链接本审计证据等级。 |
| 01 分层与依赖 | L5 查询只读、命令窄提交、业务裁决下沉。 | 矩阵存在，但 `:31`、`:67`、`:73` 与 `:185` 及 UiIntents 命令入口冲突。 | UiIntents 存在窄命令调用。 | 需统一正文并用 [UiIntents.cs:90](D:/workespace/ws-game/presentation/ui/core/UiIntents.cs:90)、`:92`、`:124` 静态验收。 |
| 02 引擎适配层 | 引擎隔离，2D 默认、model 可选。 | Unity 3D renderer 仍 NotSupported；2D 接口可用。 | 模板默认走 Unity 2D 路线。 | Unity PlayMode 尚 SKIP；见 [UnityRenderer3D.cs:21](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:21)。 |
| 03 运行时骨架 | 生命周期、模拟、场景和可替换时间模型。 | 通用 SimTimers 与 skill/Aura 私有计时器并存，文档概括过宽；FR-01 已复现。 | GameplayAssembly 接入基础 tick，但没有专用计时换算。 | 无 Unity 场景证据；需双向模式切换和往返不漂移验收。 |
| 04 数据与内容管线 | schema、引用完整性、Expr 消歧和提交门槛。 | 外层 schema 可校验，技能嵌套坏字段仍放行；孤儿检查未实现。 | 框架/合并数据 validator 默认可运行。 | 校验 PASS；FR-05 反例需 LoadAll 阻断，Expr 普通域/event 说明分开。 |
| 05 对象模型与世界 | World/Entity/Spawn/AreaTrigger 及任务状态归属。 | Quest 权威在 QuestHost；跨图 teleporter 返回引用但默认 interact 不消费。 | resolver 注入，默认 handler 未转发引用。 | GP26-03 尚静态；需 router 纵切确认旧图清理和目标坐标。 |
| 06 规则层 | 属性、技能、战斗、AI、目标及时间语义。 | FR-01—FR-05；FindUnits 当前无条件返回空，即使构造时传入 spatialQuery 也未消费；TargetPoint、ATB、位移轨迹为边界。 | RulesAssembly 接入基础 Skill/Combat，缺完整空间与时间边界。 | .NET 规则测试全绿但非完整场景；补时间、schema、目标与轨迹验收。 |
| 07 载体层 | 物品、装备、生物、物件的容器和来源语义。 | 普通 Aura Replace 有修复；套装门槛共享句柄仍有 GP26-02；采集 SimTime 恒 0。 | GameplayAssembly 注入部分回调，未注入采集 SimTime。 | 载体测试全绿；需套装顺序和默认采集时钟验收。 |
| 08 玩法层 | Loot/Quest/Dialog/Encounter/Difficulty/Achievement/Economy。 | 主体存在；Quest.Update、dayProvider、ownerResolver、vendorOpenRequested 默认缺接线；GP26-01 暴露奖励事件边界。 | 默认只装配显式回调，自动 Quest/日历/商店缺 provider。 | Gameplay .NET 444 全绿；不能代替失败后 DispatchPending 终态和自动玩法纵切。 |
| 09 表现层 | 2D/feedback/VFX/SFX/动画和统一表现重建。 | U01—U04；武器/装备外观/命中事件默认未接线。 | 模板接入 2D/反馈基础，Unity 音频/动画缺真实路径闭环。 | Unity 编译/PlayMode SKIP；Stub/机制探针不证明真实音频、渲染反馈。 |
| 10 存档与持久化 | 版本、master_seed、状态段和恢复。 | 默认段注册和 seed 已存在；FR-04 派生 rating cache 恢复缺失。 | GameplayAssembly 注册主要 persistables；完整世界恢复取决于调用方。 | 真实 dist 的 MANIFEST/lock commit 与 HEAD 一致，六个 DLL 实测 hash 与二者一致；未做完整 Shell/真实用户存档纵切。 |
| 11 工程规范与测试 | 自动化门禁、消费方和发布验证。 | .NET 2238、pytest 47、check 14 PASS/6 SKIP；SKIP 包含 Unity/消费方。 | check 默认由 `-SkipUnity` 明确跳过引擎链。 | ZIP 框架 validator 与 UPM 框架入口通过；整包 hash 和 Unity 仍需补齐。 |
| 12 扩展与变更流程 | ADR、新原语审批和变更记录。 | ADR/流程目录存在；append-only 修复尾注造成正文漂移。 | 新原语流程由维护者执行，非运行时默认接线。 | 需将当前规则移入正文、历史移索引，每个新原语保留 ADR 证据。 |
| 13 新游戏接入指南 | 七项数据、测试、确定性、表现、存档、E2E、资产验收。 | consumer smoke 描述超过模板实际覆盖；模板只含移动/存读档起步。 | 模板默认仅装配最小骨架，不含战斗/技能/任务内容。 | 需收窄 D02，独立消费方真正执行技能/战斗/任务后才可宣称第 6 项。 |
| 14 资产规格书模板 | 资源字段、映射和导入规范。 | 规格与 importer 可用；武器风格、装备视图、keyframes、VFX 锚点跟随非默认自动接线。 | 默认 view factory 未传 weaponStyles/equipment map/keyframes provider。 | 需补 provider 来源和默认装配路径，以 Unity 表现纵切验收。 |

## 模块 README 待改

| README | 漂移或缺口 | 代码锚点/验收 |
|---|---|---|
| `core/gameplay/assembly/README.md` | 构造例缺必填 saveSystem；TeleportUnit、RNG 注册、处理器数量与当前代码冲突。 | [GameplayAssembly.cs:732](D:/workespace/ws-game/core/gameplay/assembly/GameplayAssembly.cs:732)、`:1136`–`:1141`；按构造/注册静态检查更新。 |
| `core/gameplay/common/README.md` | “失败回滚到调用前”范围过宽，未说明 pending item.added 的提交边界。 | [RewardDispatcher.cs:101](D:/workespace/ws-game/core/gameplay/common/core/RewardDispatcher.cs:101)–`:113`；GP26-01 失败后库存和事件验收。 |
| `core/gameplay/quest/README.md` | 已要求调用方显式 Update，但“任一步失败不改状态”仍忽略事件队列。 | [QuestHost.cs:355](D:/workespace/ws-game/core/gameplay/quest/core/QuestHost.cs:355)、`:699`–`:745`；补事件边界说明。 |
| `core/rules/expr_host/README.md` | 离散 provider 已接入，旧文仍称 turn/round 恒 0；day_cycle 仍未实现。 | `RulesExprHostFactory.cs:461`–`:472`；区分已实现 provider 与 0 fallback。 |
| `core/numbers/archetype/schema/README.md` | “L2 skill 未实现”历史解释过时。 | `RulesSchemaCatalog`/SkillHost 跨表注册；重写引用校验边界。 |
| `core/rules/skill/README.md` | FindUnits 当前无条件为空，TargetPoint 是上层责任；即使构造 SkillHost 时传入 spatialQuery，FindUnits 也未消费它。 | [SkillHost.cs:138](D:/workespace/ws-game/core/rules/skill/core/SkillHost.cs:138)–`:144`、[SkillTickHandler.cs:16](D:/workespace/ws-game/core/rules/skill/core/SkillTickHandler.cs:16)。 |
| `core/carriers/gobj/README.md` | `respawn_after_use` 依赖调用方时间源；默认 `SimTime` 恒 0 的边界应写入正文。 | [GobjOptions.cs:74](D:/workespace/ws-game/core/carriers/gobj/contracts/GobjOptions.cs:74)；默认装配提供时间源后验证采集冷却往返。 |
| `core/carriers/item/README.md` | 普通 Aura grant 的 Replace 说明未覆盖套装门槛独立句柄；共享 Aura 的阈值降落需写明来源计数。 | [EquipmentHost.cs:578](D:/workespace/ws-game/core/carriers/item/core/EquipmentHost.cs:578)–`:590`；GP26-02 两种卸载顺序验收。 |
| `presentation/vfx_sfx/README.md` | append-only 段落仍保留旧的 “ISfx 无 Update” 语句；武器风格 resolver 无默认生产调用者。 | [SfxPlayer.cs:249](D:/workespace/ws-game/presentation/vfx_sfx/core/SfxPlayer.cs:249)、[WeaponStyleResolver.cs:11](D:/workespace/ws-game/presentation/vfx_sfx/core/WeaponStyleResolver.cs:11)。 |
| `games/_template/README.md` | `Options` 曾被描述为模块启停；consumer smoke 覆盖范围已由尾注收窄但正文表格仍易误读。 | `:40`–`:48`；复制模板后需补战斗/技能/任务/表现。 |
| `presentation/assembly/README.md` | FeedbackRuleValidator 未接入仍是事实；AnimClipResolver/装备视图 provider 也未默认传入。 | [UnityViewFactory.cs:430](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:430)–`:438`；默认装配探针。 |
| `presentation/render/README.md` | SpriteCharacterRig 的 HitFrameReached 有事件但无生产订阅；默认 parser 不读 keyframes。 | [SpriteCharacterRig.cs:55](D:/workespace/ws-game/presentation/render/core/SpriteCharacterRig.cs:55)、`:125`；关键帧事件纵切。 |
| 根 `README.md` / `CHANGELOG.md` | “阶段完成/稳定可接入”需附当前能力和 Unity/消费方 SKIP 边界。 | 链接本报告验证章节和 13 项发现。 |

## 未实现或未默认接入清单

| 能力 | 证据与边界 |
|---|---|
| Unity 3D renderer | [UnityRenderer3D.cs:21](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:21) NotSupported。 |
| Editor tooling | `editor/README.md:5` 尚未开始。 |
| Talent learn/activate/refund/save | `core/numbers/archetype/README.md:67`–`:90` 只有查询。 |
| FindUnits / TargetPoint | [SkillHost.cs:138](D:/workespace/ws-game/core/rules/skill/core/SkillHost.cs:138)–[SkillHost.cs:144](D:/workespace/ws-game/core/rules/skill/core/SkillHost.cs:144) 当前无条件返回空；即使构造 SkillHost 时传入 spatialQuery，本方法也未消费它；[SkillTickHandler.cs:16](D:/workespace/ws-game/core/rules/skill/core/SkillTickHandler.cs:16) 将 TargetPoint 留给上层。 |
| Discrete summon/loot expiry | `SummonTickHandler.cs:56`、`LootExpiryTickHandler.cs:37` 对非 continuous 跳过。 |
| Displacement trajectory collision | `EffectDispatcher.cs:291,310,322,337` 直接 SetPosition。 |
| Daily/automatic Quest/owner/shop | `GameplayAssembly.cs:518,519,691` 对应 provider/callback 为 null；Quest.Update 无默认调用。 |
| Gather respawn clock | `GobjOptions.cs:74` 默认 `SimTime=()=>0`；无 Gameplay/GameBootstrap 注入时正 `respawn_after_use` 只采一次。 |
| Complete new-game reset | `SampleNewGameStarter.cs:38` 未清 inventory/quest/currency/vitals。 |
| VFX anchor follow | `VfxPlayer.cs:144` 只用出生坐标。 |
| day_cycle | `RulesExprHostFactory.cs:461`–`:462` 恒 0。 |
| ATB | `TimeModelSchema.cs:22`–`:24`、TurnScheduler.Configure 预留并抛 NotSupported。 |
| Orphan/DisplayCoverage automatic checks | `architecture/04_数据与内容管线.md:241` 明示无孤儿检查；DisplayMapCoverageRule 要求消费者登记来源。 |
| FeedbackRuleValidator default registration | presentation assembly README 仍记录未接入。 |
| Replay 录制/回放 | [ReplayRecorder.cs:1](D:/workespace/ws-game/core/foundation/save_system/core/ReplayRecorder.cs:1)、[ReplayPlayer.cs:1](D:/workespace/ws-game/core/foundation/save_system/core/ReplayPlayer.cs:1) 机制存在；默认生产装配未创建它们，需游戏侧接入。 |
| Weapon style / equipment visuals / hit keyframes default path | `UnityViewFactory.cs:198,316,430-438` 未传/未消费 provider；`AnimClipResolver.cs:19-23` 明示技能覆盖不支持；`SpriteCharacterRig.cs:55,125` 无生产订阅；`SpriteViewBase.cs:184` 无装备映射直接返回。 |

## 当前发现索引

| 编号 | 级别 | 主题 | 证据 |
|---|---|---|---|
| GP26-01 | P1 | 发奖失败后 pending item.added 造成旧物品损失 | E1，console 复现 |
| GP26-02 | P2 | 套装共享 Aura 引用计数错误 | E1，console 复现 |
| GP26-03 | P2 | 跨图 teleporter 默认链丢引用 | E2，静态调用链 |
| FR-01 | P2 | 混合模式未换算技能/Aura 专用计时器 | E1，console 复现 |
| FR-02 | P2 | 0 充能恢复永久耗尽 | E1，console 复现 |
| FR-03 | P2 | Aura 寿命后多结算周期 | E1，console 复现 |
| FR-04 | P2 | 读档等级不失效 rating cache | E1，console 复现 |
| FR-05 | P2 | 技能嵌套坏字段未被校验阻断 | E1，console 复现 |
| U01 | P1 | 音乐 A/B 声源反选 | E1，源码+Unity Stub |
| U02 | P2 | SFX 自然结束池位不回收 | E1，源码+Unity Stub |
| U03 | P2 | 序列帧跨帧漏关键帧 | E1，非 Unity 机制复现 |
| U04 | P2 | root Renderer 未吃表现变换/反馈 | E2，静态调用链 |
| U05 | P2 | release fallback 漏独立 tgz 完成判定 | E2，静态 workflow |
