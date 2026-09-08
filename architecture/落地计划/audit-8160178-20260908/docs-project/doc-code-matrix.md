# ws-game 1.7.0 文档—代码深入矩阵

审计基线为冻结仓 `D:\workespace\ws-game-review-8160178`，`HEAD=8160178b76fb51ae704a8f14b428decf228cc33e`，`VERSION=1.7.0`。原 `D:\workespace\ws-game` 只读；`audit-85f1f4f-20260908` 与 `followup-2026-09-08e` 仅作为历史线索，本矩阵按当前 1.7 源码重新核对。判断分为：**未实现**、**已实现未默认接线**、**明确非目标**、**已实现且默认接线**。最后一类表示至少一处生产装配根默认走到该能力，但不代表完整游戏 Runtime 已验收。

## 00–14 逐章矩阵

|章|文档结论与当前代码|状态|文档需更新或边界|
|---|---|---|---|
|00 架构总则|`architecture/00_架构总则.md:31` 规定分层、固定步、数据化内容与存档边界；核心项目依赖仍按层组织。|已实现且默认接线（基础骨架）|静态依赖、Native 测试和文档一致性不能替代 Unity、真实游戏 Runtime 或用户验收；表现层仍可通过窄意图入口影响逻辑，须与“只读订阅”绝对表述统一。|
|01 分层与依赖|`GameplayAssembly` 构造参数已在 `core/gameplay/assembly/GameplayAssembly.cs:234-274` 公开，owner/day/vendor 在 `:272-274`；三处组合根分别在 `GameFoundationBootstrap.cs:330-341`、`FrameworkResidentHost.cs:337-348`、`games/_template/Runtime/GameBootstrap.cs:217-236` 透传。|已实现未默认接线|默认值仍为 `null`；游戏仍需提供实现并验证行为。旧审计中“装配根没有参数”的结论已历史化。|
|02 引擎适配层|Unity 适配器实现资源、时钟、窗口、输入、2D/3D、文件与导航契约；`UnityResourceLoader.TryGetOrLoadSlotMesh` 在 `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityResourceLoader.cs:713`，剪辑资源登记经统一加载器。|已实现且默认接线（适配契约）|本项目子任务使用 `-SkipUnity`，没有编译或运行 Unity；整体 Unity 专项结果见 `../presentation/presentation-findings.md`。资源合同不能由本子任务源码或 Native 结果扩大为全流程通过。|
|03 运行时骨架|固定步与连续/离散调度由 `SimClockHost`、`WorldSim`、`TurnScheduler` 实现；`TurnScheduler.cs:79` 对 ATB 抛 `NotSupportedException`。|已实现且默认接线（连续/基础离散）；ATB 明确非目标|离散下召唤过期处理仍是未实现项；所有时间模型与具体游戏入口的驱动责任要分开记录。|
|04 数据与内容管线|数据合并、schema 与 validator 可执行；`DisplayMapCoverageRule` 及若干引用校验需显式 sources/查询提供者。Unity 资源类型与模型/动画剪辑合同已在 `02`、`09`、`14` 勘误。|已实现未默认接线（部分覆盖规则）|不能把某个 catalog 的登记项数量当成全仓覆盖；`power_types`、`skill_book_ref`、`passive_auras` 的普通 Id/IdList 仍需按数据管线责任核对。孤儿记录检查未实现。|
|05 对象模型与世界|宝箱/采集余量通过 `GameObjectHost.PendingLootSnapshot`/`RestorePendingLoot`（`core/carriers/gobj/core/GameObjectHost.cs:104-152`）持久化；种族引用和 `player.race_id` 由 `UnitPersistable.cs:57-133` 实现。|已实现且默认接线（载体基础）；新局完整清空未实现|`SampleNewGameStarter.Start` 仅清有限玩家字段（`games/_template/Runtime/SampleNewGameStarter.cs:38` 起），库存、任务、货币、生命等全状态重置由游戏补齐。|
|06 规则层|`target.chain` 形状范围查询在生产装配中默认注册；`SkillHost.FindUnits`（`core/rules/skill/core/SkillHost.cs:169-175`）仍直接警告并返回空；地面 `TargetPoint` 参数由上层处理（`SkillTickHandler.cs:16`）。|已实现且默认接线（target.chain）；FindUnits/轨迹碰撞未实现；TargetPoint 属上层消费边界|不能把 `FindUnits` 的空实现扩大为“范围目标整体不可用”，也不能把 TargetPoint 上层责任写成框架缺失；位移效果在 `EffectDispatcher.cs:327-373` 直接到终点，没有轨迹碰撞。|
|07 载体层|Item、Equipment、Creature、Gobj、Summon 等宿主与持久化存在；`GobjOptions.SimTime` 默认 `() => 0`（`core/carriers/gobj/contracts/GobjOptions.cs:85-95`）。|已实现未默认接线（采集时钟）；离散召唤过期未实现|三处生产根均未把统一模拟时钟注入 Gobj 选项；`respawn_after_use>0` 时默认时间不前进。离散掉落清理和离散召唤 duration 必须分开描述。|
|08 玩法层|Quest/Dialog/Economy/Loot/Spawn 等机制存在；`QuestHost.Update` 在 `core/gameplay/quest/core/QuestHost.cs:386`，owner/day/vendor 已经由 `GameplayAssembly` 转发到内部宿主。|已实现未默认接线|全仓生产代码未发现 `Quest.Update` 调用；三处组合根只透传回调，默认不提供。日任务、owner 限定、vendor UI 仍需游戏层显式接线和 Runtime 验证。|
|09 表现层|sprite/model rig、装备重放、命中帧同步、动画剪辑事件合并、VFX/SFX/UI 机制存在；VFX anchor 在 `VfxPlayer.cs:144,373` 只在生成时解析。|已实现且默认接线（model/sprite 基础、命中帧可配置）；VFX anchor 持续跟随未实现；武器 style 消费未默认接线|`WeaponStyleResolver` 有解析器但无生产消费者；`FeedbackRuleValidator` 有实现但未由 catalog 默认调用。`-SkipUnity` 不证明适配器资源或表现 Runtime。|
|10 存档与持久化|缺段默认传 `JsonNull`、已成功段失败后按逆序尽力回滚的实现位于 `SaveSystem.cs:314-385,690-727`；`SaveSections.KnownOrder` 已固定 `world.dropped_loot`、`world.vendor_stock`、`world.difficulty`、`spawn_state`、`sim.turn_state` 与 `rng_stream_states`（`SaveSections.cs:122-145`）；`world.gobj_pending_loot` 已由装配根注册，但在 `GobjPendingLootPersistable.cs:53` 仍是未列入 KnownOrder 的自定义段。|已实现且默认注册|`IPersistable.cs:30-35` 仍写“其它段不会回滚”，与实现及 `architecture/10_存档与持久化.md:112` 的 best-effort 描述冲突；`CHANGELOG.md:84-85` 把“尽力”写成“中间态消失”，措辞过强。缺段清空与公开 API 兼容必须分开记录。|
|11 工程规范与测试|`check.ps1 -SkipUnity` 在本轮 14 步 PASS、6 步 SKIP；六个 .NET 项目分别 659/107/323/404/491/476，合计 2460。|已实现且默认接线（门禁脚本）|Unity 相关 6 步明确 SKIP；全量 `dotnet test` 已包含 .NET Perf 类别，但门禁通过只说明本机 Native/工具链检查，不是 CI、Unity、实际游戏性能/压测、游戏 Runtime 或用户验收证明。|
|12 扩展与变更流程|ADR 列表、版本迁移与可选契约流程存在；公开旧 API 别名由 `GameObjectHost.cs:146-152` 保留并标记 `[Obsolete]`。|已实现且默认接线（流程/兼容入口）|SaveSystem best-effort 回滚须修订契约说明；新增存档段、行为收紧和别名移除时仍需在 ADR、CHANGELOG、迁移说明中保持同一口径。|
|13 新游戏接入|模板构造 `GameplayAssembly` 时透传 owner/day/vendor（`GameBootstrap.cs:217-236`），但不驱动 Quest.Update，也不注入 Gobj SimTime。|已实现未默认接线|接入指南须明确游戏层责任：Quest 驱动、SimTime、TargetPoint、Replay、完整新局清理与真实内容/表现 provider。|
|14 资产规格书|`display.equip_visual.mesh_ref` 经 loader 解析 prefab 子对象/首个 renderer mesh；动画事件按资源加载和 anim_set 隔离。|已实现且默认接线（合同/工具）；孤儿检查未实现|本项目验证子任务只做静态与非 Unity 门禁；整体 Unity 专项验证见 `../presentation/presentation-findings.md`。资产导入器和真实消费方仍需独立 Unity 验收，不能把 hash/清单通过写成导入成功。|

## 四类能力状态

### 未实现

- GF 内容编辑器实现（`editor/README.md:5`）。
- 天赋运行时激活、撤销与持久化（`core/numbers/archetype/README.md:69,90`；装配根 `GameplayAssembly.cs:507` 仍传 `talentPointGranter=null`）。
- `ISkillHost.FindUnits` 便利 API（`SkillHost.cs:169-175`）。

- 离散模式召唤 duration 推进及掉落清理调用（`SummonTickHandler.cs:53`、`LootExpiryTickHandler.cs:33`；两者语义不同，不能合称“时钟不走”）。
- 位移/冲锋沿途轨迹碰撞（`EffectDispatcher.cs:327-373`）。
- 新局完整重置（`SampleNewGameStarter.cs:38` 起）。
- VFX anchor 持续跟随（`VfxPlayer.cs:315-373` 只做回收/pending 处理）。
- 孤儿记录检测（04 章第 5 节仍为建议，当前没有对应规则）。

### 已实现未默认接线

- Gobj 采集 `SimTime`：默认值见 `GobjOptions.cs:95`，三处生产根均未显式传递统一时间源。
- 地面点选 `TargetPoint`：参数由上层消费（`SkillTickHandler.cs:16`），属于接入责任边界，不列为框架内未实现。
- 日任务/自动 Quest：`QuestHost.Update` 已实现（`QuestHost.cs:386`），三处生产代码无调用。
- owner/day/vendor：构造参数 `GameplayAssembly.cs:272-274` 与内部转发 `:539-540,718` 已就位；三处组合根透传但默认仍为 null。
- Replay：`ReplayPlayer` 构造与测试存在，未发现游戏 UI、调试工具或命令行生产入口。
- 武器 Swing/Impact：`WeaponStyleResolver.cs:20,30` 有解析，当前无生产消费者。
- `DisplayMapCoverageRule`（04 章第 5.1 节）与 `FeedbackRuleValidator.cs:22`：实现存在，但不是默认 catalog 注册路径。

### 已实现且默认接线

- 连续/基础离散模拟调度（`core/foundation/sim_loop`）。
- `target.chain` 形状范围查询（`TargetHost.cs:139`、`BuiltinTargetStrategies.cs:102`、`RulesAssembly.cs:260`）。
- sprite/model View、装备外观、命中帧同步的可配置装配（`UnityViewFactory.cs`、`ModelCharacterRig.cs`、三处组合根）。
- 公开旧 pending API 的 `[Obsolete]` 转发（`GameObjectHost.cs:146-152`）；这只证明 1.7 API surface 兼容，不代表旧存档行为自动兼容。
- 数据、事件常量、占位资产和包清单门禁。

### 明确非目标

- `time.day_cycle` 恒返回 0（`RulesExprHostFactory.cs:461-462`）；离散的 `turn_index`/`round_index`/`is_my_turn` 是另一条已接线能力。
- ATB：schema 接受预留值，但 `TurnScheduler.cs:79-81` 明确抛 `NotSupportedException`，ADR-0013 已决定本版不展开。

## 文档需同步清单

|优先级|位置|事实|处理建议|
|---|---|---|---|
|P2|`core/foundation/save_system/contracts/IPersistable.cs:30-35`、`core/foundation/save_system/README.md:131-132,206-219`、`architecture/10_存档与持久化.md:112`、`CHANGELOG.md:84-85`|实现只承诺对已成功段做快照并按逆序**尽力**回滚；快照缺失、回滚再次抛错或段间联动都可能留下状态。接口注释仍声称完全不回滚，README 的部分历史段落及 CHANGELOG 则把结果写得过强（中间态消失/必然恢复）。|文档漂移单独列 P2：保留 `PersistableThrew`，把三处统一为 best-effort；实现残留风险的级别由 core 代理证据与主审单独判定。|
|P2|能力索引 `落地方案与分阶段计划.md:1232-1251`|当前四栏分类已经把旧已修项放入“已修复历史项”，但 `SimTime` 行仍写“两处生产装配根”的旧措辞；`Quest.Update` 与 owner/day/vendor 的三处组合根事实已核对。|只修正 SimTime 行及锚点；不要恢复旧“19 项”或“未实现/未接线统称”。|
|P2|`architecture/10_存档与持久化.md:113-127`、`SaveSections.cs:122-145`、`GobjPendingLootPersistable.cs:53`|`KnownOrder` 固定了 dropped/vendor/difficulty/spawn/sim/RNG；`world.gobj_pending_loot` 已由装配根注册，但未登记入 KnownOrder。10 章把它列在 7a、`sim.turn_state` 前，和当前排序事实不一致。|按当前代码明确自定义段排序，或将该自定义 key 登记进 KnownOrder 后再写恢复顺序；同时保留缺段语义。|
|P2|`toolchain/README.md:359`、`toolchain/get_framework.ps1:102`|接入说明写复制/引用 `get_framework.ps1`，但未提醒同步携带 helper，可能被误解为单脚本可独立使用；1.7 脚本已强依赖同目录 `_hash.ps1`。完整 toolchain 包含 helper，不是当前包缺件。|改为复制整套 toolchain，或明确同时携带 `_hash.ps1`；这是文档接入项，不评级为包损坏。|

## ADR 交叉核对

0001（万物皆法术）：规则机制存在，`FindUnits` 仍是明确缺口。  
0002（逻辑对象不依赖引擎对象）：Core 项目引用边界保持；Unity 适配证据需单独看。  
0003（固定步长）：sim_loop 与本轮 Native 测试通过。  
0004/0005（数据即内容/一个条件语言）：validator 与 Expr 存在；`day_cycle` 仍是非目标。  
0006（Id→DisplayInfo）：映射机制存在，coverage 需显式 sources。  
0007/0008（窄契约、世界状态）：事件与 WorldState 存在，生产消费者/游戏数据仍需接入。  
0009（存档唯一持久化）：Save/Load 与回滚机制存在；Replay 没有生产入口。  
0010（新原语审批）：新增扩展点仍按 ADR 与版本流程维护。  
0011/0012（适配隔离、双外形）：model/sprite 合同已实现；本项目子任务使用 `-SkipUnity`，整体 Unity 专项结果见 `../presentation/presentation-findings.md`。  
0013（可替换时间模型）：连续与基础离散可用，ATB 是本版非目标。  
0014（资产工具/UI）：工具与合同存在，不等于实机导入通过；本项目子任务使用 `-SkipUnity`，整体 Unity 专项结果见 `../presentation/presentation-findings.md`。  
0015/0016/0017：Expr 引用登记、适配联调、model/命中帧变更均应以当前源码和独立 Runtime 证据复核。

