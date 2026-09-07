# 文档—代码矩阵

本表按当前 `68c9bedaeed3485ee845f68c95136f87476caf03` 静态核对。文档声明、已实现能力、预留能力和本轮问题分开记录；测试通过不升级为 Runtime/Unity 证据。

| 文档 | 当前代码边界与漂移 | 处理/证据 |
|---|---|---|
| [00_架构总则](D:/workespace/ws-game-review-68c9bed/architecture/00_架构总则.md) | 通用框架定位成立，但机制复用不等于新游戏业务、内容和默认生产入口均完成；就绪结论须依据下列缺口与运行验收。 | 文档同步；不把架构边界当运行证明。 |
| [01_分层与依赖](D:/workespace/ws-game-review-68c9bed/architecture/01_分层与依赖.md) | `:185` 禁止 L5 改变下层状态，与 09/UI 及 [UiIntents.cs](D:/workespace/ws-game-review-68c9bed/presentation/ui/core/UiIntents.cs) 命令入口存在口径冲突；应明确只读查询与命令提交边界，业务判断保留下层。 | 静态。 |
| [02_引擎适配层](D:/workespace/ws-game-review-68c9bed/architecture/02_引擎适配层.md) | Unity 适配依赖需隔离测试目录和真实存档；Unity 3D/model 方法保持 `NotSupported` 边界。 | N06；本轮未启动 Unity。 |
| [03_运行时骨架](D:/workespace/ws-game-review-68c9bed/architecture/03_运行时骨架.md) | 时间字段需注明消费者；summon/LootExpiry 的 discrete 不推进，channel tick 有例外。SceneRouter 的 `SubscriptionHandle` 现已接入，未把旧代际问题重报为当前缺陷。 | N03/N14 按 code review 静态记录。 |
| [04_数据与内容管线](D:/workespace/ws-game-review-68c9bed/architecture/04_数据与内容管线.md) | 数据根合并、schema、资产映射和生成检查是当前管线边界；运行消费者、Unity 导入和内容实际可用性仍需独立验收。 | 基线数据/资产检查通过；行为问题按 code review。 |
| [05_对象模型与世界](D:/workespace/ws-game-review-68c9bed/architecture/05_对象模型与世界.md) | 世界实体生命周期与读档恢复仍有跨模块缺口：同图恢复丢 Spawn 实体映射（N03），来源销毁后持续效果仍访问来源（N05）；补齐生命周期与恢复契约。 | 静态。 |
| [06_规则层_属性技能战斗AI](D:/workespace/ws-game-review-68c9bed/architecture/06_规则层_属性技能战斗AI.md) | `TargetPoint` 生产意图未消费，`FindUnits` 当前恒空；talent 只有树解析，无点数/激活/撤销/存档；`stack_category` 只有字段；DR 无配置宿主；charge/leap/knockback 为瞬时位置；连续 summon 已实现，离散 summon 未实现。 | N07–N10 详见 code review。 |
| [07_载体层_物品生物物件](D:/workespace/ws-game-review-68c9bed/architecture/07_载体层_物品生物物件.md) | 连续召唤跟随存在，离散跟随未实现；affix/gems/enchant/durability/binding 是预留，不计为问题。 | 明示预留。 |
| [08_玩法层_掉落任务对话关卡](D:/workespace/ws-game-review-68c9bed/architecture/08_玩法层_掉落任务对话关卡.md) | 护送只有数据/手工推进，无 route/death 自动宿主；`Quest.Update` 默认无调用；daily provider null 恒 0；vendor 曲线与 item level/quality 上下文未提供。 | 明示待实现，不把内容数据当功能。 |
| [09_表现](D:/workespace/ws-game-review-68c9bed/architecture/09_表现层.md) | 动画时间与逻辑结算独立；默认瞬态结束、冷 clip、sink pending 完成协议和模板时间轴需要分开验收。 | N17 有界复现；N18/N19 静态，不声称 Unity。 |
| [10_存档与持久化](D:/workespace/ws-game-review-68c9bed/architecture/10_存档与持久化.md) | 文档 :78 仅列 `stream_states`，:172 的 beginRecording 漏写 masterSeed；代码 [RngStreamsPersistable.cs:47](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/RngStreamsPersistable.cs:47) 已持久化主种子，[ReplayRecorder.cs:37](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/ReplayRecorder.cs:37) 签名已含 masterSeed，[Replay.cs:341-346](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/contracts/Replay.cs:341) 写出 format_version/master_seed；另有货币/技能/刷新读档、旧 backup 兼容缺口。 | N01/N07/N15/N16；A 复现见 repro.log。 |
| [11_工程规范](D:/workespace/ws-game-review-68c9bed/architecture/11_工程规范与测试.md) | 基线 .NET/pytest/data/assets 检查通过；FeedbackRuleValidator 仍未接入，测试通过不等于完整可用。 | 命令与计数见 validation.md。 |
| [12_扩展](D:/workespace/ws-game-review-68c9bed/architecture/12_扩展与变更流程.md) | 新原语/新任务目标/新动作仍需 ADR、注册、代码、校验、测试；已有消费者的数据扩展不自动要求新 ADR。 | 按现有流程处理。 |
| [13_新游戏接入](D:/workespace/ws-game-review-68c9bed/architecture/13_新游戏接入指南.md) | template options 不是模块启停开关；Economy/Quest/Encounter 在 GameplayAssembly 无条件构造；SampleNewGameStarter 只初始化位置/地图/template。 | 文案需收窄；静态。 |
| [14_资产规格](D:/workespace/ws-game-review-68c9bed/architecture/14_资产规格书模板.md) | sprite 默认 animSet 的 displayId 末段约定需说明；VFX anchor/screen 当前只解析出生坐标，未持续跟随；map importer 只处理资源布局，不生成可玩导航。 | N18 与表现边界分开；静态。 |

## 模块 README 漂移与边界

| README 位置 | 当前代码锚点与应记录事实 |
|---|---|
| [skill/README.md:75](D:/workespace/ws-game-review-68c9bed/core/rules/skill/README.md:75)、[skill/README.md:86](D:/workespace/ws-game-review-68c9bed/core/rules/skill/README.md:86)、[skill/README.md:203](D:/workespace/ws-game-review-68c9bed/core/rules/skill/README.md:203) | `FindUnits` 当前恒空；GCD 不应统一写在开始时点；死亡处理需按 `CastPipeline` 实际中断边界写。对应 N10 及已修历史。 |
| [scene_router/README.md:116](D:/workespace/ws-game-review-68c9bed/core/foundation/scene_router/README.md:116)、[presentation/assembly/README.md:122](D:/workespace/ws-game-review-68c9bed/presentation/assembly/README.md:122) | SceneRouter hook 实际返回 `SubscriptionHandle`；`FeedbackRuleValidator` 仍未注册，不能写成已接入。 |
| [quest/README.md:99-102](D:/workespace/ws-game-review-68c9bed/core/gameplay/quest/README.md:99)、[QuestHost.cs:654](D:/workespace/ws-game-review-68c9bed/core/gameplay/quest/core/QuestHost.cs:654) | GP07 文案仍写 `item.removed/HandleItemRemoved`，实际路径是 `item.added/HandleItemAdded`；需按当前消费语义同步，不要以旧文案推导行为已实现。 |
| [presentation/common/README.md:94](D:/workespace/ws-game-review-68c9bed/presentation/common/README.md:94)、[presentation/assembly/README.md:52](D:/workespace/ws-game-review-68c9bed/presentation/assembly/README.md:52)、[presentation/assembly/README.md:140](D:/workespace/ws-game-review-68c9bed/presentation/assembly/README.md:140) | interpolation alpha 与 AnchorResolver 的“未接”描述过时；Validator 未接入（:122）仍是真实缺口。 |
| [games/_template/README.md:41](D:/workespace/ws-game-review-68c9bed/games/_template/README.md:41)、[GameplayAssembly.cs:492](D:/workespace/ws-game-review-68c9bed/core/gameplay/assembly/GameplayAssembly.cs:492)、[GameplayAssembly.cs:510](D:/workespace/ws-game-review-68c9bed/core/gameplay/assembly/GameplayAssembly.cs:510)、[GameplayAssembly.cs:625](D:/workespace/ws-game-review-68c9bed/core/gameplay/assembly/GameplayAssembly.cs:625) | template Options 不是模块启停开关，Economy/Quest/Encounter 由 Assembly 无条件构造；Sample starter 也不等于完整新局隔离。 |
| [save_system/README.md:108](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/README.md:108)、[SaveSystem.cs:389](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/SaveSystem.cs:389)、[SaveSystem.cs:403](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/SaveSystem.cs:403)、[SaveSystem.cs:892](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/SaveSystem.cs:892) | 备份/候选回退与旧布局迁移要按 N15/N16 和实际路径同步；不要把旧顶层 `.bak1` 当当前目录兼容。 |
| [SaveSystem.cs:738](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/SaveSystem.cs:738) | 仅校验 `meta.game_id` 是合法 Id，未与当前游戏 Id 匹配；接入文档须明确目录/游戏隔离由谁保障，不能把记录 `game_id` 当隔离校验。 |
| [SaveSystem.cs:619](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/SaveSystem.cs:619)、[SaveSystem.cs:325](D:/workespace/ws-game-review-68c9bed/core/foundation/save_system/core/SaveSystem.cs:325) | `ComputeReadOrder` 只枚举档内 sections；已注册但完全缺失的 key 对应警告路径不可达。文档须区分缺段保留/清空策略及实际诊断行为。 |
| [feedback_binder/README.md](D:/workespace/ws-game-review-68c9bed/presentation/feedback_binder/README.md)、[FeedbackBinder.cs:85](D:/workespace/ws-game-review-68c9bed/presentation/feedback_binder/core/FeedbackBinder.cs:85)、[WaitForPlaybackPacingPolicy.cs:73](D:/workespace/ws-game-review-68c9bed/core/foundation/sim_loop/core/WaitForPlaybackPacingPolicy.cs:73)、[GameBootstrap.cs:274](D:/workespace/ws-game-review-68c9bed/games/_template/Runtime/GameBootstrap.cs:274) | HasPendingPlayback、sink completion 和默认 host 转发需共同记录；N17 有界复现只覆盖真实组件链，不覆盖 Unity 资源。 |
| [vfx_sfx/README.md](D:/workespace/ws-game-review-68c9bed/presentation/vfx_sfx/README.md)、[UnityViewFactory.cs:391](D:/workespace/ws-game-review-68c9bed/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:391) | 冷 clip 具有原地升级调用，但不刷新 `FrameAnimPlayer` 当前缓存；N18 仍需运行时验收。 |

## 未实现或未默认接入能力源码索引

| 能力边界 | 当前源码锚点 |
|---|---|
| Unity 3D/model | [UnityRenderer3D.cs:21](D:/workespace/ws-game-review-68c9bed/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:21) 为 `NotSupported` 边界。 |
| 编辑器 | [editor/README.md:5](D:/workespace/ws-game-review-68c9bed/editor/README.md:5) 仍标明未开始。 |
| 天赋点激活 | [archetype/README.md:67](D:/workespace/ws-game-review-68c9bed/core/numbers/archetype/README.md:67)、[archetype/README.md:90](D:/workespace/ws-game-review-68c9bed/core/numbers/archetype/README.md:90) 只有 `GetTalentTree`。 |
| FindUnits | [SkillHost.cs:138](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/SkillHost.cs:138) 当前恒空。 |
| TargetPoint | [SkillTickHandler.cs:16](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/SkillTickHandler.cs:16) 只有输入形状，生产消费未贯通。 |
| 离散召唤 | [SummonTickHandler.cs:56](D:/workespace/ws-game-review-68c9bed/core/carriers/summon/core/SummonTickHandler.cs:56) 离散分支不推进。 |
| 离散掉落过期 | [LootExpiryTickHandler.cs:37](D:/workespace/ws-game-review-68c9bed/core/gameplay/loot/core/LootExpiryTickHandler.cs:37) 未接离散推进。 |
| 位移效果 | [EffectDispatcher.cs:291](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/EffectDispatcher.cs:291)、[EffectDispatcher.cs:310](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/EffectDispatcher.cs:310)、[EffectDispatcher.cs:322](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/EffectDispatcher.cs:322)、[EffectDispatcher.cs:337](D:/workespace/ws-game-review-68c9bed/core/rules/skill/core/EffectDispatcher.cs:337) 只写瞬时最终落点。 |
| 日任务默认日历 | [GameplayAssembly.cs:513](D:/workespace/ws-game-review-68c9bed/core/gameplay/assembly/GameplayAssembly.cs:513)、[QuestHost.cs:83](D:/workespace/ws-game-review-68c9bed/core/gameplay/quest/core/QuestHost.cs:83) 的 day provider 缺省为恒 0。 |
| 新局隔离 | [SampleNewGameStarter.cs:38](D:/workespace/ws-game-review-68c9bed/games/_template/Runtime/SampleNewGameStarter.cs:38) 只初始化最小位置/地图/template。 |
| Anchor/screen VFX | [VfxPlayer.cs:144](D:/workespace/ws-game-review-68c9bed/presentation/vfx_sfx/core/VfxPlayer.cs:144) 只在出生时解析坐标。 |

## 已修历史与本轮不重复计数

当前快照已看到 progression/archetype/RNG 默认注册、DroppedLoot 重挂、UiIntents Move 参数、ResourceLoader 三入口、Feedback merger 探针、sprite shadow、HUD 顺序/AP、DeathPolicy/死亡回档、Charges/action_cost/GCD/time provider、race passive aura、Target/Skill/AI provider、双向 Combat 仇恨、projectile spawner、连续 Summon ShareThreat 等历史收口。本轮只在 N 编号中记录仍有独立证据的问题；这些历史项仍需按 validation 的证据边界做运行验收。
