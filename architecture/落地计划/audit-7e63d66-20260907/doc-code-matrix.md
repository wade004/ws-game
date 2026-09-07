# 文档—代码矩阵（基线 7e63d66）

本矩阵以当前 00–14 架构文档、主要模块 README 和实际源码锚点核对。它描述能力边界和文档准确性，不把文档存在或测试通过升级为可用游戏证明。

## 架构章节

| 章节 | 当前核对结论与源码/文档锚点 |
|---|---|
| 00 架构总则 | 通用框架定位成立，但机制复用不等于新游戏业务、内容和默认生产入口均完成；就绪结论须依据下列缺口与运行验收。见 [00_架构总则.md](D:/workespace/ws-game-review-7e63d66/architecture/00_架构总则.md:1)。 |
| 01 分层与依赖 | :185 禁止 L5 改变下层状态，与 09/UI 及 presentation/ui/core/UiIntents.cs 命令入口存在口径冲突；只读查询与命令提交边界需要统一，业务判断应保留下层。见 [01_分层与依赖.md](D:/workespace/ws-game-review-7e63d66/architecture/01_分层与依赖.md:185)。 |
| 02 引擎适配层 | 文档说明 IRenderer3D 在需要 model 时必需、纯 sprite 可不实现；当前 UnityRenderer3D 全部 NotSupported（[UnityRenderer3D.cs:21](D:/workespace/ws-game-review-7e63d66/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:21)），因此默认仅 sprite 路线，model 需补适配。见 [02_引擎适配层.md](D:/workespace/ws-game-review-7e63d66/architecture/02_引擎适配层.md:230)。 |
| 03 运行时骨架 | SceneRouter 代际隔离修复存在；03 第 9 节已经采用 SubscriptionHandle，模块 README 中仍要求同步 void 签名的历史说明应更新。默认运行仍需真实场景生命周期验收。 |
| 04 数据与内容管线 | 数据 schema 与源码校验入口存在；独立发行包 validator 的依赖和路径边界见 P02。 |
| 05 对象模型与世界 | 世界实体生命周期与读档恢复仍有跨模块缺口：真实 Despawn 后来源状态访问是 C02，同图 Shell 重载刷新 timer 是 C11；补齐生命周期与恢复契约。见 [05_对象模型与世界.md](D:/workespace/ws-game-review-7e63d66/architecture/05_对象模型与世界.md:1)。 |
| 06 规则层 | TargetPoint、FindUnits、离散 tick、位移效果和天赋点流程均有默认边界，见下方能力索引；SkillValidationRules 在 [SkillValidationRules.cs:80](D:/workespace/ws-game-review-7e63d66/core/rules/skill/schema/SkillValidationRules.cs:80)、[SkillValidationRules.cs:107](D:/workespace/ws-game-review-7e63d66/core/rules/skill/schema/SkillValidationRules.cs:107) 做最小 stack_category 冲突校验，但运行期 AuraHost 仍按 defId/sourceKey 分槽，未实现 06:221 所述同 category 跨 aura 聚合叠加上限。 |
| 07 载体层 | 物品、装备、生物载体有可复用接口；Equipment Replace 句柄撤销是 C08，技能读档集合是 C09。章节文件为 [07_载体层_物品生物物件.md](D:/workespace/ws-game-review-7e63d66/architecture/07_载体层_物品生物物件.md:1)。 |
| 08 玩法层 | Encounter/Achievement 发奖失败是 C04，奖励回滚是 C05，Quest 部分扣除是 C06；Quest.Update 有 API 但默认没有调用。章节文件为 [08_玩法层_掉落任务对话关卡.md](D:/workespace/ws-game-review-7e63d66/architecture/08_玩法层_掉落任务对话关卡.md:1)。 |
| 09 表现层 | VFX/SFX timeout 完成链是 C07；reload_save 同图 Death 动画是 C12。默认 3D 路线仍是 NotSupported 边界，不以 2D 测试替代。 |
| 10 存档与持久化 | 文档 :78 只列 stream_states，:172 的 beginRecording 缺主种子；代码 [RngStreamsPersistable.cs:47](D:/workespace/ws-game-review-7e63d66/core/foundation/save_system/core/RngStreamsPersistable.cs:47) 持久化 master_seed，[ReplayRecorder.cs:37](D:/workespace/ws-game-review-7e63d66/core/foundation/save_system/core/ReplayRecorder.cs:37) 接收 masterSeed，[Replay.cs:341](D:/workespace/ws-game-review-7e63d66/core/foundation/save_system/contracts/Replay.cs:341) 和 [Replay.cs:346](D:/workespace/ws-game-review-7e63d66/core/foundation/save_system/contracts/Replay.cs:346) 写 format_version/master_seed；文档应同步这两个字段。game_id 只是格式/命名空间，SaveSystem :751 只校验 Id 合法性，未与当前 options game id 比对。另 :632 ComputeReadOrder 只枚举档内 sections，:325 已注册但完全缺失的 key 不可达，应区分缺段保留/清空策略和实际诊断。 |
| 11 工程规范与测试 | 六个 .NET 测试程序集和 pytest 通过只证明源码入口可执行；FeedbackRuleValidator 仍未注册，独立包 validator 见 P02。 |
| 12 扩展与变更流程 | 细节勘误直接修；拍板变更走 ADR；仅修复引入新原语时另走新增原语流程，见 :113。 |
| 13 新游戏接入指南 | template options 不是模块启停开关；默认构造仍需游戏侧 provider、内容、UI 和生命周期装配。 |
| 14 资产规格书模板 | sprite 默认 animSet 依 displayId 末段约定需写清；VFX anchor/screen 目前只记录出生坐标；importer 只处理资源布局，不自动生成可玩导航。 |

## README 与源码漂移

| 文档 | 校准项 |
|---|---|
| core/gameplay/common/README.md:73 | 原子发奖/失败无变化的表述超出保证；应限定原例已修，C04–C06 的组合事务仍未闭合。 |
| core/gameplay/quest/README.md:118 | 同上；GP07 应描述 item.added/HandleItemAdded，实际源码锚为 QuestHost:654。 |
| core/gameplay/spawn/README.md:111 | 已说明保留活实体映射，但未说明 Shell 同图重载如何处理保存 timer，对应 C11。 |
| presentation/feedback_binder/README.md:105 | 播放完成协议缺少 timeout 终止路径，对应 C07。 |
| presentation/assembly/README.md:122 | Validator 未接入仍是当前事实；:52 AnchorResolver 已同步为接 ViewBinder.GetAnchorWorldPosition，不列待更新。 |
| presentation/common/README.md:94 | 此处已正确说明 alpha 接通；同文件 :77–81 的历史判断仍说接线留待后续，应标记为已完成，避免前后冲突。 |
| core/foundation/scene_router/README.md:116 | 旧 void 询问已过时，当前使用 SubscriptionHandle。 |
| games/_template/README.md:41 | Options 不是模块启停开关；模板构造默认仍无完整游戏内容。 |
| core/rules/tests/Integration/PowerMaxRecomputeWiringTests.cs:49 | 尾注引用不存在的历史测试方法名；属于文档/覆盖说明漂移，不增加运行时编号。 |
| 根 README.md:9、CHANGELOG.md:16 | “阶段全部完成/稳定可接入”应附适用能力和当前问题边界；版本升级说明应按实际旧 lock/commit 提供迁移矩阵。当前磁盘 0.2.0 manifest 为 6b07bae，且与 1.0.0 的 core/presentation 无 diff，不能直接断言当前 0.2.0 产物存在接口不兼容；早期 68c9bed 消费者的接口迁移仍需记录。 |

## 未实现或未默认接入能力索引

| 能力 | 当前锚点与边界 |
|---|---|
| Unity 3D 渲染 | [UnityRenderer3D.cs:21](D:/workespace/ws-game-review-7e63d66/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:21) 为 NotSupported。 |
| 编辑器工具 | [editor/README.md:5](D:/workespace/ws-game-review-7e63d66/editor/README.md:5) 尚未开始。 |
| 天赋点激活 | [archetype/README.md:67](D:/workespace/ws-game-review-7e63d66/core/numbers/archetype/README.md:67)、:90 只有 GetTalentTree，没有点数学习/激活/撤销/存档流程。 |
| FindUnits | [SkillHost.cs:138](D:/workespace/ws-game-review-7e63d66/core/rules/skill/core/SkillHost.cs:138) 无条件返回空数组。 |
| TargetPoint | [SkillTickHandler.cs:16](D:/workespace/ws-game-review-7e63d66/core/rules/skill/core/SkillTickHandler.cs:16) 声明由上层处理，无默认消费。 |
| 离散召唤/掉落过期 | [SummonTickHandler.cs:56](D:/workespace/ws-game-review-7e63d66/core/carriers/summon/core/SummonTickHandler.cs:56)、[LootExpiryTickHandler.cs:37](D:/workespace/ws-game-review-7e63d66/core/gameplay/loot/core/LootExpiryTickHandler.cs:37) 对非 Continuous 跳过。 |
| 位移轨迹碰撞 | [EffectDispatcher.cs:291](D:/workespace/ws-game-review-7e63d66/core/rules/skill/core/EffectDispatcher.cs:291)、:310、:322、:337 直接 SetPosition，无轨迹/沿途碰撞。 |
| 日任务与自动 Quest 驱动 | [GameplayAssembly.cs:519](D:/workespace/ws-game-review-7e63d66/core/gameplay/assembly/GameplayAssembly.cs:519) 注入 dayProvider=null，[QuestHost.cs:83](D:/workespace/ws-game-review-7e63d66/core/gameplay/quest/core/QuestHost.cs:83) 默认恒 0；ownerResolver 在 [GameplayAssembly.cs:518](D:/workespace/ws-game-review-7e63d66/core/gameplay/assembly/GameplayAssembly.cs:518) 为 null，vendorOpenRequested 在 [GameplayAssembly.cs:691](D:/workespace/ws-game-review-7e63d66/core/gameplay/assembly/GameplayAssembly.cs:691) 为 null；escort 有契约/schema 和手动进度入口，没有 QuestHost 自动执行分支，全仓没有 Quest.Update 调用。 |
| 新局完整重置 | [SampleNewGameStarter.cs:38](D:/workespace/ws-game-review-7e63d66/games/_template/Runtime/SampleNewGameStarter.cs:38) 仅处理位置、地图、模板，不清库存、任务、货币和生命。 |
| VFX 锚点跟随 | [VfxPlayer.cs:144](D:/workespace/ws-game-review-7e63d66/presentation/vfx_sfx/core/VfxPlayer.cs:144) 只使用出生坐标。 |
