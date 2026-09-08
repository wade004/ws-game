# ws-game 1.6.0 文档—代码矩阵

基线：85f1f4fbaff7aa3f01292f1b4b469a6a48bcc570 / v1.6.0。源码锚点按冻结基线复核。四类判断为：已实现且默认接线、已实现未默认接线、框架未实现、明确非目标。

## 00–14 逐章矩阵

|章|文档主题|源码锚点与实际|判断/需更新|
|---|---|---|---|
|00|总则与框架/游戏分界|README.md:221-223；00章目标/非目标|分界一致；补证据边界：静态门禁不等于 Unity/Runtime/用户验收。|
|01|七层与装配|GameplayAssembly.cs:223-260、516-520、692-695|没有 ownerResolver/dayProvider/vendorOpenRequested 构造参数；文档不能让游戏传不存在参数，应改为扩展 options/adapter。|
|02|引擎适配|adapters/unity/.../UnityRenderer3D.cs:929-967、1320；../presentation/presentation-findings.md:PRES-85-01|适配源码存在，但 sample slot_mesh 把 prefab 型 model_ref 当 Mesh 读取，当前有 P2 缺陷；表现审计已有 43 个回归通过和 1 个故障探针，不能把 model 整体写成已收口。|
|03|生命周期/固定步/连续离散|TurnScheduler.cs:77-81；GameplayAssembly.cs:1004|基础调度有 Native 测试；ATB schema 接受但调度器抛 NotSupportedException，是 ADR-0013 非目标。|
|04|数据/schema/引用/coverage|GameplaySchemaCatalog.cs:171-188；RulesSchemaCatalog.cs:154-174；ArchSchemas.cs:35-59；DisplayMapCoverageRule.cs:15-21|SpawnSummonOnlyCreatureRule 仅 query 非 null 时登记；RulesSchemaCatalog 在此处显式登记 3 条引用规则；不能推断整个框架所有 catalog 只有 3 条。power_types/skill_book_ref/passive_auras 为普通 Id/IdList；coverage 需显式 sources。须登记默认未验证范围。|
|05|对象模型与世界|GameObjectHost.cs:104-118；GameplayAssembly.cs:513-520|待领取掉落 API/持久化存在；owner/day 仍 null。模板新局完整清空不由框架保证，SampleNewGameStarter.cs:38。|
|06|属性/技能/战斗/AI|SkillHost.cs:169-175；SkillTickHandler.cs:13-16；GameplayAssembly.cs:491-493|target.chain 形状查询默认接通；FindUnits 返回空；TargetPoint 由上层处理；天赋分配无运行时流程且 granter=null。分别分类，不写成范围目标整体不可用。|
|07|物品/生物/物件|GobjOptions.cs:95,113；SummonTickHandler.cs:56|SimTime 默认 () => 0，采集刷新未默认接真实时钟；Discrete summon 明确跳过。CR150-04 旧满包缺陷不作为当前缺陷。|
|08|掉落/任务/对话/关卡|QuestHost.cs:64-83,386,722-726；GameplayAssembly.cs:516-520,692-695|下层回调契约存在但装配根传 null；Quest.Update 存在但未发现生产推进入口。须先扩展装配层再由游戏接入。|
|09|表现、VFX 与反馈|VfxPlayer.cs:113-145、315-373；FeedbackRuleValidator.cs:22；PresentationSchemaCatalog.cs:115、130；../presentation/presentation-findings.md|VFX 播放机制存在，但 anchor 持续跟随尚未实现；反馈校验器不是 IValidationRule 且未由 catalog 调用。旧 PR150-03 已修，新 slot_mesh 合同缺陷仍在。|
|10|存档、恢复与 Replay|ReplayPlayer.cs:23、57、225；GameObjectHost.cs:104-118；SaveSections.cs:112-132|Replay 没有生产入口。10_存档与持久化.md:119 步骤 7a 含 gobj_pending_loot，但 SaveSections.KnownOrder 没有该段，实际按自定义段排序到 rng 之后，属于文档顺序漂移；API 改名与字段迁移分开。|
|11|工程/测试/门禁|check.ps1；test_get_framework_path_boundary.py:67-117；Core.sln 六测试项目|六 Native 项 2436 通过；SkipUnity 门禁 13 PASS/1 FAIL/6 SKIP。pytest 是编码和 PowerShell 工具链问题。|
|12|扩展/ADR/变更流程|architecture/adr/README.md；ADR-0010；12章|流程总体一致；owner/day/vendor 扩展应先补 ADR/接口。|
|13|新游戏接入|games/_template/Runtime/GameBootstrap.cs；13章:119-125；get_framework.ps1:437|原始 1.6 zip 离线消费在 pwsh 成功；指南须写明游戏负责 owner/day/vendor、SimTime、TargetPoint 接线。|
|14|资产规格/导入/display.map|DisplayMapCoverageRule.cs:15-21；Unity Editor 导入工具|规则与工具存在但 coverage 要显式 sources；未做 Unity importer/Runtime 验证。|

## ADR 交叉核对

0001 万物皆法术：规则存在，FindUnits 未实现。  
0002 逻辑对象不依赖引擎：Core 引擎隔离按静态依赖核对；另有表现定向 Unity 证据，详见 ../presentation/presentation-findings.md。  
0003 固定步长：sim_loop 与测试通过。  
0004 数据即内容：catalog 可运行但引用覆盖有限。  
0005 一个条件语言：统一 Expr，day_cycle 恒 0。  
0006 Id 到 DisplayInfo：coverage 需显式 sources。  
0007 窄契约与事件：装配存在，外部消费需接线。  
0008 世界状态替代 Phasing：WorldState 存在，不代替游戏内容验证。  
0009 存档唯一持久化：Save/Replay 存在，Replay 生产入口缺失。  
0010 新原语审批：owner/day/vendor 扩展应走此流程。  
0011 引擎适配层隔离：package/Core 边界存在；Unity 回归与 43 个通过、1 个 slot_mesh 故障探针见 ../presentation/presentation-findings.md；本子任务未执行全量 Unity 门禁。  
0012 双外形与固定镜头：model/sprite 路径存在，需 Unity 验收。  
0013 可替换时间模型：ATB 明确抛异常，是本版非目标。  
0014 资产工具/UI 交付：工具存在，不等于导入器实机通过。  
0015 Expr 引用登记：RulesSchemaCatalog 在该处注册 3 条引用规则，不能代表所有 catalog 的总覆盖。  
0016 适配契约联调：静态源码与门禁存在，消费方未运行。  
0017 model 外观与命中帧：PR150-03 历史问题已修；新 slot_mesh 缺陷及 43 个相关 Unity 回归见表现报告。

## 四类能力漏项

**未实现**：天赋点激活、撤销与持久化；`ISkillHost.FindUnits`；位移轨迹碰撞；VFX anchor 持续跟随；新局全状态重置（模板仅重置有限玩家项，完整流程须由游戏负责）；编辑器实现（editor/README.md:5 明确尚未开始，当前仅有产品 md/html）。

**已实现但未默认接线**：Gobj `SimTime`、`Quest.Update` 生产驱动、Replay 生产入口、下层 owner/day/vendor 回调（`QuestHost.cs:64-66` 存在，但 `GameplayAssembly.cs:223-260` 未暴露参数，内部传 null）、`DisplayMapCoverageRule`/`FeedbackRuleValidator`、武器 style 消费。`TargetPoint` 是已有参数但当前 `SkillHost` 不消费，须由游戏上层处理；这属于边界责任，不等同完整机制未实现。

**已实现且默认接线**：`target.chain`、连续/基础离散调度、model/sprite 视图装备重放、命中帧事件（配置可关闭）。已有 43 个 Unity 相关回归，但 slot_mesh 新缺陷仍在；这些证据不等于完整游戏通过。

**明确非目标**：`day_cycle`（`RulesExprHostFactory.cs:461-462` 恒 0）、ATB（`TurnScheduler.cs:77-81` 抛 `NotSupportedException`）。

## 公共 API 兼容性

只读原始 dist/ws-game-1.5.0.zip 与 lock 的六 DLL hash 全匹配；1.5 反射为 PendingChestLootSnapshot()/RestorePendingChestLoot(...)。同一 Library consumer 对 1.5 编译成功，对原始 1.6 旧调用各报 CS1061；1.6 仅有 PendingLootSnapshot()/RestorePendingLoot(...)，无 alias。因此 CHANGELOG 的“不删除/改名已有公开签名”表述与实际不符。建议添加 Obsolete 转发 alias，或明确 breaking change/升级迁移；与存档字段迁移风险分开。

## 文档更新 checklist（交给主文档 owner）

- README/能力索引：把 PR150-03、CR150-04 明确写为历史已修；不要把“框架未实现”与“已实现未默认接线”统称契约就位。
- 能力索引：ownerResolver/dayProvider/vendorOpenRequested 不在 GameplayAssembly 构造参数；应记录装配层扩展点缺失及游戏责任。
- CHANGELOG：核对旧 PendingChestLoot 方法名；当前 1.6 无 alias，旧 consumer 会抛编译 CS1061。与存档字段迁移分开说明。
- 10 章：10_存档与持久化.md:119 步骤 7a 含 gobj_pending_loot；SaveSections.KnownOrder:112-132 没有该段，实际自定义排序在 rng 之后，需修正文档顺序。
- 08/13 章：说明 vendorOpenRequested 仍 null，且 owner/day provider 需装配层先暴露；SimTime 不是 vendor 时钟的替代。
- 09/14 章：补 slot_mesh 资源合同：UnityRenderer3D.cs:941-966 按 Mesh 读取，sample model_ref 是 prefab；引用表现审计 PRES-85-01。补 VFX anchor 跟随边界（VfxPlayer.cs:315-373）。
- 07/03 章：补离散模式掉落/召唤清理边界，依据 LootExpiryTickHandler 与 SummonTickHandler 的明确 Discrete 路径，不扩写为连续模式缺陷。
- 09 章：武器 style resolver 有机制但无生产消费者，和 model 外观“已接线”分开。


- CHANGELOG.md:100-102 声称 1.5 非空 pending 存档旧字段会被安全忽略、不报错且不影响其它段；实际依据旧 serializer 重建的等价存档经 SaveSystem.Load 返回 PersistableThrew，且 inventory 前段已经变更。需修订旧档兼容说明并落实安全跳过或明确迁移；这是 AUD-01，独立于 AUD-04 的公开 API 编译破坏。
