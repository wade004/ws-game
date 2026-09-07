# 旧审计结论适用范围

旧审计基线是 b3b91ee，不能替代本轮固定 HEAD 68c9bedaeed3485ee845f68c95136f87476caf03 的证据。旧结论只用于去重和确认历史修复；本轮报告以当前源码、当前命令日志和当前有界复现为准。本表“存在/闭合”均指对原问题修复代码的静态核验，不等同每项独立 Runtime 验收。

| 旧编号 | 当前状态与去重说明 |
|---|---|
| FND01 | 原修复源码存在；当前旧 backup 布局兼容另列 N16。 |
| FND02 | 原修复源码存在；本轮不重报 SceneRouter 代际问题。 |
| FND03 | 原修复源码存在；本轮不重报。 |
| FND04 | 原修复源码存在；仍需运行验收，不并入 N 编号。 |
| FND05 | 原修复源码存在；仍需运行验收，不并入 N 编号。 |
| FND06 | 原修复源码存在；仍需运行验收，不并入 N 编号。 |
| FND07 | 部分修复；当前 SaveSystem 候选浅校验形成 N15。 |
| FND08 | 原修复源码存在；不重报同帧输入旧结论。 |
| FND09 | 原修复源码存在；不重报迁移旧结论。 |
| FND10 | 旧装备快照替换源码存在；当前技能来源和装备 aura 的独立边界见 N07/N09。 |
| RC01 | 原 heal_done 事件循环已收口；当前精确 AuraRemoved 循环见 N04。 |
| RC02 | 当前只订阅 entity.destroyed 做清理；旧报告声称 unit.died 订阅不实，不把它写成当前缺陷。 |
| RC03 | 读条死亡/销毁中断接线已存在；不重报旧结论。 |
| RC04 | 失败扣 AP 问题已闭合；不重报。 |
| RC05 | 运行期来源处理部分存在；存档丢 source 的问题见 N07，装备多来源 aura 见 N09。 |
| RC06 | 派生值重算部分存在；等级事件顺序见 N08。 |
| RC07 | 学派锁闭合；不重报。 |
| RC08 | 移动接线存在于 RulesAssembly:310，不是 SkillHost 直接订阅；不把旧订阅位置当已证实。 |
| RC09 | 射程处理闭合；不重报。 |
| RC10 | AI 分类链已修；显式敌对单体绕过额外 tag/expr 条件见 N10。 |
| RC11 | 武器基数接入已存在；不重报。 |
| GP01 | 快照替换闭合；不重报旧的任务状态残留结论。 |
| GP02 | 瞬态结束与 Forget 接线存在；冷 clip 当前缓存见 N18，同批 Start/Success 见 N19。 |
| GP03 | 三宿主 Vfx.Update 已接；不重报。 |
| GP04 | LeaveMap abort 已接；Level 重开不终止旧实例是 N13。 |
| GP05 | Choose 时重验已闭合；不重报。 |
| GP06 | 冷 clip 原地升级调用存在，但当前播放对象缓存仍见 N18。 |
| GP07 | 原 RemoveItem 失败仍涨 consume 进度的问题已区分处理；当前跨堆叠集成回归见 N12。 |
| GP08 | 原有库存初始化接线存在；不重报。 |
| GP09 | 探针接线存在；pending 完成信号独立缺口见 N17。 |
| GP10 | 两个缺失 Rig.Update 已接；不重报。 |
| TOOL01 | 缺失 native 程序失败保护已接；不重报旧假阳性结论。 |
| TOOL02 | 双下划线与写前碰撞检查已接；不重报旧碰撞结论。 |

## 旧文案纠正

旧 followup 的当前工作树副本见 [followup-2026-09-07b.md](D:/workespace/ws-game-review-68c9bed/architecture/落地计划/audit-b3b91ee-20260907/followup-2026-09-07b.md)。其中 RC02 的 `unit.died`、RC08 的订阅位置、RC10 的“显式 target 已过滤”，以及 `PowerMaxRecomputeWiringTests` 尾注引用的 RC06 不存在测试名，都不能作为当前证据。另有 [skill/README.md:75](D:/workespace/ws-game-review-68c9bed/core/rules/skill/README.md:75) 的 FindUnits、`:86` 的 GCD 时点、[quest/README.md:99-102](D:/workespace/ws-game-review-68c9bed/core/gameplay/quest/README.md:99)、[presentation/common/README.md:94](D:/workespace/ws-game-review-68c9bed/presentation/common/README.md:94) 和 [presentation/assembly/README.md:52](D:/workespace/ws-game-review-68c9bed/presentation/assembly/README.md:52) 的漂移，已在矩阵标出。

[followup-2026-09-07b.md](D:/workespace/ws-game-review-68c9bed/architecture/落地计划/audit-b3b91ee-20260907/followup-2026-09-07b.md) 第四节的全量 Unity 门禁仅指向会话正文，本轮不将该声明计为已重跑；旧 b3 复现日志属于历史基线。当前命令、测试计数、TRX 位置和复现输出见 [validation.md](validation.md) 与 [evidence](evidence/)。
