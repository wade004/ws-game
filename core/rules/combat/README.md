# L2 规则层 · combat 战斗系统

职责：结算管线（命中/闪避/招架/偏斜/格挡/暴击 → 基础值 → 暴击倍率 → 施法者乘区 → 护甲/抗性减免
→ 目标乘区 → 免疫吸收 → 落地 → 后置）、仇恨表、进出战斗判定（见
[06_规则层_属性技能战斗AI.md](../../../architecture/06_规则层_属性技能战斗AI.md) 第 4 节）。落地
计划 T2-7（`Resolver`）+ T2-8（`ThreatTable` 与进出战斗）。

依赖：`Core.Rules.Common`（`IUnitAccess`/`IAuraQuery`/`ICombatHost`/`IThreatTable`/
`IStaticImmunityProvider`/`IGearLevelOffsetProvider`/`EffectContext`/`ResolveResult`/`HitResult`/
`EffectKind`/`WellKnownPowers`/`RulesEventKeys` 及强类型事件）、L1 `Core.Numbers`（`IStatHost`/
`IPowerHost`/`IFactionMatrix`）、L0 `Core.Foundation`（`IRngHost`/`IEventBus`/`IDataRegistry`/
`ITickPhaseHandler`）。不引用 `core/rules/skill`/`targeting`/`ai` 的具体类型（`IAuraQuery` 用调用方
注入的实现，测试用 Fake）。

## 目录

```
combat/
  README.md
  contracts/
    CombatOptions.cs        构造期策略配置（命中表 id、属性 id 引用、系数、脱战时长、仇恨上限、死亡策略、结算追踪回调、T-N1-7 目标乘区/被暴击减免显式属性 id 清单、T-N1-8 LevelDiffTableId/EffectiveLevelIncludesGearOffset）
    ICombatDiagnostics.cs   最小诊断出口
  core/
    HitTableConfig.cs        combat.hit_table_config 强类型视图（T-N1-8：miss 分支新增 HitStat）
    ResistCurve.cs           combat.resist_curve 强类型视图 + 减免求值（table 分支自 T-N0-5 起委托 PiecewiseCurve 插值，saturation 公式不变）
    LevelDiffTable.cs        T-N1-8：combat.level_diff_table 强类型视图（四条曲线）
    CombatDataLoader.cs      从 IDataRegistryView 加载上面三张表（level_diff_table 可选，见判断记录 19）
    Resolver.cs               结算管线九步实现（T-N1-8：DetermineHit 接入 Δ 加成/压制）
    ThreatTable.cs            IThreatTable 默认实现
    CombatHost.cs             ICombatHost 默认实现
    CombatTickHandler.cs      接入 sim_loop TickPhase.CombatResolution
    InMemoryCombatDiagnostics.cs
  schema/
    CombatSchemas.cs          三张表的 TableSchema 声明
    CombatValidationRules.cs  概率范围/曲线单调性等校验规则
  tests/
    CombatTestSupport.cs      Fake IUnitAccess/IAuraQuery/IGearLevelOffsetProvider + 真实 Stat/Power/Rng/Faction/DataRegistry 夹具
    ResolverHitTableTests.cs  命中表六分支 + 手算全链 + 免疫 + 治疗 + 死亡
    ResolverScopedReductionTests.cs  T-N1-7：目标乘区 scope 匹配 sourceKind 遍历减免属性 + 被暴击减免介入暴击率下限夹取
    ResolverLevelDiffTests.cs  T-N1-8：Δ 矩阵（11 组）、命中/暴击双向生效与封顶、miss hit_stat、有效等级装备偏移策略项
    CombatLevelDiffTableSchemaTests.cs  T-N1-8：combat.level_diff_table schema 覆盖（合法/缺字段/负数纵轴/非单调）
    C10_ResolveTraceTests.cs  CombatOptions.ResolveTrace 三条返回路径 + 未设置零开销 + 回调抛异常不中断
    ThreatTableTests.cs       仇恨表增减/置顶/上限裁剪/清理/事件
    CombatEnterLeaveTests.cs  进出战斗、仇恨驱动脱战、治疗仇恨
    CombatDeterminismTests.cs 同种子重放一致性
    CombatValidationRuleTests.cs 数据校验规则正反例
```

## 设计要点与判断记录

1. **`ThreatTable` 是单个共享实例，不是"每单位一份"**：`IThreatTable`（`core/rules/common`）
   的全部方法都携带 `unitId` 参数，即使 `ICombatHost.GetThreatTable(unitId)` 已经"按单位"取得
   实例。本模块据此把 `ThreatTable` 实现为全局共享的一个对象，内部按 `unitId` 分桶存储；
   `CombatHost.GetThreatTable` 对任意 `unitId` 都返回同一个引用。外部可观察行为与"每单位一份"
   完全一致，只是少了 N 份对象分配。

2. **命中表六分支"向谁查询概率属性"**：06 只给出六项是什么、默认值、开关，未规定概率属性挂在
   攻击者还是防御者身上。本实现按 WoW 命中表的常见语义分配：`miss`/`crit` 查询攻击者
   （`SourceId`）；`dodge`/`parry`/`glancing_blow`/`block` 查询防御者（`TargetId`）。`block` 的
   固定减免量（`block_value_stat`）同样查询防御者。

3. **偏斜/格挡的数值应用时点**：06 第 4.1 节"判定"步骤本身产生不了数值（基础值要到下一步才
   确定），本实现把偏斜的伤害系数相乘、格挡的固定量相减挪到"基础值"确定后、"暴击"倍率相乘前
   应用，语义不变——它们仍然是"判定"阶段决出的结果，只是数值应用点顺延到有数值可用的第一时机。

4. **偏斜/格挡与暴击默认可叠加**（勘误，codex 第十八轮，audit-d6fda65-20260911，DOC-118-09：
   本条此前称"偏斜/格挡命中时不再参与暴击判定（`isCrit` 恒 false）"，与实现不符，已改写）：
   `HitResult` 是扁平枚举而非位标记，一次结算只报告一个分支标签（`DetermineHit` 的优先序是
   miss > dodge > parry > glancing_blow > block > (crit | hit)），但**暴击判定与伤害倍率是独立
   计算的**——`isCrit` 由单独一次掷骰决定（`DetermineHit` 内 `context.CanCrit && table.Crit.Enabled`
   分支），不受 `special` 是否已经是 glancing_blow/block 影响；调用方（`Resolve`）拿到 `isCrit`
   后，偏斜的伤害系数与暴击倍率依次相乘应用到同一笔伤害上（先 `amount *= glancingMult`，再
   `if (isCrit) amount *= critMultiplier`），两者可以同时生效。`CombatDamageDealtEvent.IsCrit`
   与 `ResolveResult.Hit`（扁平标签，此时会是 `GlancingBlow`/`Block`）因此**可以**同时表达"暴击的
   偏斜/格挡"这种组合——扁平标签只反映 `DetermineHit` 的优先序（决定报告哪个分支名），不代表
   伤害计算互斥。06 未规定偏斜/格挡与暴击是否必须互斥；框架把"可叠加"作为通用默认策略——是否需要
   互斥（例如某款游戏希望"偏斜的攻击不可能同时暴击"这类口味）由消费方在自己的战斗结算流程/口味
   配置层面决定，不属于本模块的通用契约，也不通过修改本模块算法本身实现。回归测试见
   `core/rules/combat/tests/ResolverHitTableTests.cs`（`Resolve_GlancingAndCritBothForced_StacksGlancingPercentAndCritMultiplier`/
   `Resolve_BlockAndCritBothForced_StacksFlatBlockReductionAndCritMultiplier`）。

5. **`context.BaseValue` 直接使用，不再乘 `context.Coefficient`**：06 第 4.1 节"基础值"一步
   原文"效果原语给出的基础数值"，任务书"设计"一节明确"`context.BaseValue`（skill 已含系数）"——
   `EffectContext.Coefficient` 由 skill 模块在计算 `BaseValue` 时已经消费过，Resolver 不重复
   应用。

6. **治疗仇恨的"拍板简化"落地方式**：06 第 4.4 节"治疗按（可配置系数的）数值增加仇恨值（记在
   被治疗方所在阵营的仇恨表里）"。任务书进一步拍板简化为：不新增"阵营仇恨表"概念，改为对每个
   当前已经把被治疗者或治疗者记在自己仇恨表里的敌对单位，追加
   `amount × CombatOptions.HealThreatCoefficient` 的仇恨（记在该敌对单位对"被治疗者"的仇恨上）。
   复用现有 `ThreatTable` 结构，不引入新数据形状；`ThreatTable.TrackedUnits`（本模块内部补充
   API，不在 `IThreatTable` 契约上）用于反查"谁已经在跟踪这场战斗"。

7. **对已死亡目标再结算的处理**：任务书"设计"一节拍板"返回 Miss 并记诊断"。`Resolver.Resolve`
   在最开头检查 `IUnitAccess.IsAlive(TargetId)`，为 false 时直接返回
   `ResolveResult(HitResult.Miss, 0, 0, 0, Immune=false, ...)` 并调用
   `ICombatDiagnostics.Warn`，不触碰任何 Stat/Power/仇恨/事件——不算"进战"，也不触发
   `NotifyCombatEvent`（与其它正常路径不同，判断记录：对尸体的操作不构成"新的战斗事件"）。

8. **免疫时完全跳过后置步骤**：`ResolveResult.Immune=true` 时不落地、不写仇恨、不发
   `combat.damage_dealt`/`combat.heal_done`，但仍然调用 `NotifyCombatEvent`（一次朝着活着的
   目标发起的攻击尝试，即使被免疫，仍然构成"战斗事件"，触发/刷新进战状态）。miss/dodge/parry
   （判定步骤终止分支）同样调用 `NotifyCombatEvent`——即便未命中，主动发起攻击这一行为本身已
   经满足 06 第 4.5 节"主动使用...技能"的进战条件。

9. **属性缺失按 0 处理**：`CombatOptions` 里引用的属性 id（`ArmorStat`/`DamageDonePctStat` 等）
   若未在 `stat.definition` 登记，或目标单位未在 `StatHost` 注册，`Resolver` 捕获
   `IStatHost.GetStat` 抛出的 `ArgumentException`/`InvalidOperationException`，按 0 处理并经
   `ICombatDiagnostics.Warn` 记一次警告（同一属性 id 只警告一次，避免刷屏）。

10. **`combat.resist_curve.school` 不声明为 `FieldKind.Reference`**：06 第 4.3 节只固定"输入
    护甲/抗性值、输出 0~1 减免百分比"这一契约，未规定 `school` 字段要指向哪张登记表；
    `core/rules/skill` 是并行开发的独立模块，本模块不预设它登记 school 定义表的表名，避免产生
    跨模块数据表依赖（呼应任务书"不引用 skill/targeting/ai 具体类型"，这里推广到不假设 skill
    的数据表结构）。`school` 只做 `Id` 格式检查。

11. **`combat.hit_table_config` 六个分支用 `FieldKind.Object` 声明**：`data_registry` 的
    `field_type` 检查不支持嵌套对象内部字段的类型校验，六个分支各自 `enabled`/`stat`/`base` 的
    合法性（概率落在 [0,1]）由本模块自己的 `CombatHitTableValidationRule` 负责，不复用
    `data_registry` 的通用校验。

12. **物理学派 id 做成可配置项（`CombatOptions.PhysicalSchool`）而非硬编码常量**：06 原文举例
    "school.physical 用护甲"，但架构总则要求框架不绑定具体游戏内容；把它做成可覆盖的默认值，
    游戏层改学派命名时不需要改本模块源码。

13. **RC-02 收口（第四方深度审核）：订阅 `entity.destroyed` 做幂等战斗清理**——原实现只有
    `Update` 里的脱战延迟到期才处理某单位的进出战状态，生物销毁（如 `CreatureFactory.Despawn`）
    先同步注销该单位在 `IPowerHost`/`IStatHost` 的注册，`CombatHost` 自己的 `_inCombat`/
    `_timeSinceLastEvent` 与 `ThreatTable` 双向仇恨条目完全不感知这一事件，等下一次 `Update` 因
    脱战延迟到期才尝试处理该单位时，`Powers.SetInCombat` 访问已注销单位直接抛异常。现在
    `CombatHost` 构造期订阅 `entity.destroyed`，收到后立即幂等清理 `_inCombat`/
    `_timeSinceLastEvent`（`Dictionary.Remove` 对不存在 key 安全）与 `ThreatTable` 里该单位的
    双向仇恨条目（`Clear`/`RemoveSourceEverywhere` 对空表/不存在来源安全），不触碰任何
    `IPowerHost`/`IStatHost` API。见 `CombatHost.cs`、`CombatEnterLeaveTests.cs`。

13. **`CombatTickHandler` 只负责脱战计时推进**：结算本身（`ICombatHost.ResolveEffect`）由
    skill 模块在效果原语落地的那一刻同步调用，不经 tick 编排；`CombatTickHandler` 把
    `ICombatHost.Update(dt)`（脱战判定）接到 `TickPhase.CombatResolution`，连续步按
    `step.Dt` 驱动。这与任务书"效果结算本身由 skill 在步骤 3 调用 ResolveEffect 即时完成"的
    说明一致。**离散模式（ADR-0013 补齐任务）**：`Discrete` 步恒 `Dt = 0`，不代表"经过了多少
    时间"（一个离散步只是某一个行动者的一次行动），本处理器改为构造期订阅 `sim.round_ended`，
    每轮结束调用一次 `Update(1.0)`；`CombatOptions.LeaveCombatDelay`（06 第 4.5 节脱战判定
    时长）由 `core/gameplay/assembly.TimeModelSwitch` 在切入离散模式时按 `seconds_per_turn`
    换算为等效轮数（向上取整，见该类型判断记录），切回连续模式时精确恢复——本处理器因此不需要
    另行知道 `seconds_per_turn`，`Update(1.0)` 恒代表"过了 1 轮"。未装配离散模式的调用方
    （构造 `CombatTickHandler` 时不传 `bus`）行为与本任务之前完全一致。

14. **阶段 3 整理"事项三"：`Resolver` 步骤 7 免疫判定叠加 `IStaticImmunityProvider`**（新增
    `core/rules/common` 契约，见该文件顶部注释）：`Resolve` 步骤 7 的 `immune` 判定改为
    `_auras.IsImmune(...) || _staticImmunity.IsImmune(...)`——`IAuraQuery.IsImmune` 反映当前生效
    的免疫类光环，`IStaticImmunityProvider.IsImmune` 反映生物模板/tier 一类内容驱动的固定免疫（如
    `creature.template.immunities` 声明的学派免疫），二者任一为真即视为本次结算免疫。构造参数
    `staticImmunity` 可选，缺省 `NullStaticImmunityProvider`（一律不免疫），不改变未接入方的既有
    行为；真实实现（`CreatureImmunityProvider`）在 `core/carriers/creature`，本模块不产生对 L3 的
    编译期依赖。

15. **C02 收口（外部审计 7e63d66 第四轮，P1，成立，跨模块——本模块负责的一半）：
    `NotifyCombatEvent` 对不存在单位静默跳过，不再抛异常**：`RC-02`（条目 13）当时只补齐了
    "`Update` 因脱战延迟到期"这一条路径的幂等清理，没有覆盖"来源已销毁但仍在产生新战斗事件"这
    一条——`Resolver.Resolve` 每次结算都对结算双方各调用一次 `NotifyCombatEvent(自己, 对方)`
    （见判断记录 7 上方 `Resolver.cs` 步骤 1/9 两处调用点），而真实 `CreatureFactory.Despawn`
    同步注销 `IPowerHost` 注册后，已施加到其它存活目标身上、来源正是这个被销毁单位的周期性
    效果（`periodic_damage`/`periodic_heal`，见 `core/rules/skill` 模块 `AuraHost.
    OnEntityDestroyed` 判断记录"来源销毁不移除已施加到其他目标身上的光环"）不会因来源销毁而
    停止结算，`NotifyCombatEvent(来源, ...)` 因此可能对着一个已注销的单位调用
    `IPowerHost.SetInCombat`，直接抛 `InvalidOperationException`。现在方法入口先查
    `IUnitAccess.Exists(unitId)`（惯例同本模块 `HasLivingHostileThreatSource`/`Resolver` 多处已有
    的防御性检查，不新增 `IPowerHost` 契约成员），不存在则静默跳过——不写入 `_inCombat`/
    `_timeSinceLastEvent`，不访问 `IPowerHost`，不发布 `combat.entered`；"进战"这件事对一个已经
    不存在于世界中的单位没有意义（既不会再被 AI/UI 观察到，也不会再脱战）。另一半
    （`core/rules/skill.EffectDispatcher` 周期效果缩放贡献降级为 0）见 `core/rules/skill/
    README.md` 同编号条目。验收（真实 `CreatureFactory`+`AuraHost`+`CombatHost` 全链路集成测试）：
    `Tests.Carriers.Assembly.CreatureDespawnPeriodicEffectTests.
    PeriodicDotWithScalingStat_SourceDespawnedThenMultipleTicksElapse_DoesNotThrow_
    AndKeepsLandingDamage`（`core/carriers/assembly/tests/`）。06/05 文档同步补充语义说明，见两者
    变更记录。

16. **`CombatOptions.ResolveTrace`：结算追踪回调（消费方反馈 2026-09-11 编辑器第 31 条，见
    `architecture/落地计划/消费方反馈-2026-09-11-编辑器-第31条.md`"方案 1"）**：消费方内容编辑器
    反馈"结算中间步骤经真实施法路径不可观测"——`ResolveResult.Steps` 总是被计算，但
    `CastPipeline.ExecuteEffectsOnly` 丢弃了 `ApplyEffect` 的返回值，落地事件
    （`CombatDamageDealtEvent`/`CombatHealDoneEvent`）也不携带分步明细，导致内容工具无法按 06
    第 4.1 节固定管线分步展示结算过程（沙盘回放同样只能看到落地总量）。三个候选方案里选**方案
    1**：不改事件契约、不给 `RulesAssembly` 加结算宿主注入点（消费方反馈原文另两个方案），只给
    `CombatOptions` 加一个可选回调 `ResolveTrace: Action<EffectContext, ResolveResult>?`。
    `Resolver.Resolve` 是全部伤害/治疗效果原语落地的唯一出口（见判断记录 7/8 与
    `core/rules/skill/core/EffectDispatcher.ApplyDamageOrHeal` 恒调用
    `ICombatHost.ResolveEffect`），本方法全部三条返回路径（"目标已死亡"短路、
    miss/dodge/parry 判定终止短路、完整九步落地）在各自 `return` 前统一经私有方法
    `InvokeResolveTrace` 调用一次，因此技能瞬发/读条完成/引导 tick、光环周期效果
    （`periodic_damage`/`periodic_heal`，经 `AuraHost.FirePeriodic` 直调
    `EffectSink.ApplyEffect`）、Proc 触发的嵌套施法（`CastPipeline.TriggerCast`）、免疫吸收
    （步骤 7 的 `ConsumeAbsorb` 发生在同一次 `Resolve` 调用内，天然覆盖，不需要单独接线）全部覆盖，
    不需要在各调用点分别接入。未设置（缺省 `null`）时只多一次 null 判断，不分配、不改变既有输出，
    零开销；回调本身抛出的异常被 `InvokeResolveTrace` 捕获后经 `ICombatDiagnostics.Warn` 记一次
    警告并继续，不向上传播——工具/诊断代码里的 bug 不应打断真实游戏结算。`RulesAssembly` 已有的
    `combatOptions` 构造参数原样透传，装配根本身不改。回归测试见
    `core/rules/combat/tests/C10_ResolveTraceTests.cs`（`Resolver.Resolve` 三条返回路径穷举）与
    `core/rules/skill/tests/C10_ResolveTraceTests.cs`（真实 `RulesAssembly` + `CastSkill`，覆盖
    瞬发伤害/光环周期伤害/Proc 嵌套施法/治疗四类路径，以及"未设置行为不变""回调抛异常不影响结算"
    两条跨路径不变量）。

17. **C11-RELOAD 根治新增 `ClearCombatState`/`RestoreCombatState`（2026-09-11，消费方反馈第 C11
    项，基线 1.22.0，architecture/落地计划/消费方反馈-2026-09-11-读档空间索引与复活生命周期.md）：
    `CombatHost` 自身的 `_inCombat` 字典是"是否在战"的唯一来源，读档场景需要一个绕开事件总线、
    直接把这份状态与 `IPowerHost` 同步的入口**——真实探针复现：`player.vitals` 段
    （`Core.Gameplay.Assembly.PlayerVitalsPersistable`）此前恢复 `in_combat` 时直接调用
    `IPowerHost.SetInCombat`，只改到了 `PowerHost` 自己的状态；`CombatHost` 本类的
    `_inCombat` 字典对此一无所知，仍停留在读档前的值（例如死亡前一刻进战、死亡结算未清空
    `_inCombat`）——读档后 `IsInCombat` 与 `PowerHost.IsInCombat` 出现分歧。新增两个公开方法：
    `ClearCombatState(unitId)` 清空该单位的 `_inCombat`/脱战计时器/双向仇恨表（同
    `OnEntityDestroyed` 同款清理惯例，见判断记录 13），不发事件、不碰 `IPowerHost`；
    `RestoreCombatState(unitId, inCombat)` 写入 `_inCombat` 并（单位存在时）同步调用
    `IPowerHost.SetInCombat`，同样不发事件（读档本就处于"不是一次业务事件"的抑制范围内，见 10
    第 3 节）。两者按"先清空、再恢复"的固定顺序由 `Core.Gameplay.Assembly.GameplayAssembly` 的
    `IDerivedStateRebuilder` 实现调用（`BeforeLoad` 清空、`player.vitals` 段 `Load` 恢复），见
    `core/gameplay/assembly/README.md` 判断记录 14；本类不知道、也不需要知道调用方是"读档"，
    只提供这两个窄操作。见
    `core/gameplay/assembly/tests/C11_LifecycleReloadTests.cs`
    （`SameMapLoad_CombatAndPowerHostAgreeOutOfCombat_AndStayConsistentNextTick`）。

18. **T-N1-7（[ADR-0030](../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
    决策 5；06 第 4.1 节 2026-09-14 修订段）：目标乘区按 `scope` 匹配 `sourceKind` 遍历减免属性、
    新增被暴击减免介入点、`CombatOptions.DamageTakenPctStat` 改按显式属性 id 清单筛选**——九步
    结算的"目标乘区"与"暴击"两步内部实现调整，**固定九步顺序本身不变**：
    - **偏离计划原文的说明（复核返工，2026-09-14）**：任务表原文写"`CombatOptions.
      DamageTakenPctStat` 改为按类别筛选"，首版实现据此按 `stat.definition.category` 批量扫描
      （`IStatHost.GetDefinitionIdsByCategory`，默认扫描类别 `"defense"`）。复核裁定该实现有
      严重缺陷并打回：ADR-0030 决策 9 明确把"护甲"归入 `defense` 推荐分类，一旦游戏内容按此
      分类登记护甲属性，类别扫描会把护甲的原始数值（如 300）当成"目标承伤 +300%"误计入目标
      乘区——不是"两个新扫描角色共享同一默认类别、可能重复计入"这种可以事后靠改配置规避的边界
      情形，而是默认配置本身与 ADR 推荐分类直接冲突，真实护甲数据一接入就错。返工改为**显式
      属性 id 清单**：`CombatOptions` 新增 `DamageTakenPctStats`/`CritTakenReductionStats`
      （均 `IReadOnlyList<Id>`，默认空列表），识别"哪些属性属于目标承伤减免/被暴击减免角色"由
      结算配置显式列出 id，不再由某个 `category` 取值批量圈定；`IStatHost.GetDefinitionIdsByCategory`
      已撤回（未发布，直接删除，不留废弃占位）。06 第 4.1 节修订段与 ADR-0030 决策 5 原文只规定
      "目标乘区步骤按 scope 匹配读取减免属性"，未规定用类别还是显式清单圈定候选集合——两种实现
      都不违反契约文字，但显式清单不会把内容作者按 ADR 推荐分类登记的既有属性（护甲、抗性等）
      误吸收进来，风险更低，因此改判显式清单为最终实现。设计层裁定（2026-09-14）：确认采纳显式
      清单方案，不再是候选之一。
    - **"目标乘区"步骤（步骤 6）**：此前只读取 `CombatOptions.DamageTakenPctStat` 一条属性、且
      不经 `scope` 过滤；现在实际读取的属性集合 = `{DamageTakenPctStat} ∪ DamageTakenPctStats`
      （`Resolver.BuildDamageTakenStatIds` 按首次出现顺序去重，`DamageTakenPctStat` 恒排最前，
      同一 id 出现在两处只计入一次），逐条经 `IStatHost.GetScope`（T-N1-7 新增 `IStatHost` 默认
      接口成员，`StatHost` 侧显式实现）按 `ScopeMatches` 与 `EffectContext.SourceKind` 匹配
      （`any` 恒匹配；`from_player` 仅 `SourceKind.Player`；`from_creature` 仅
      `SourceKind.Creature`）过滤，命中的对目标单位求最终值累加，求和结果一次性应用
      （`amount *= 1 + Σ / 100`）。既有内容数据从未给 `stat.damage_taken_pct` 登记过 `scope`
      字段（`GetScope` 对未登记属性缺省返回 `"any"`，恒匹配），`DamageTakenPctStats` 默认空
      列表，因此新逻辑对既有内容是恒等变换，回放基线/
      `ResolverHitTableTests.Resolve_FullChain_MatchesHandCalculatedValue`（184.8/134.8）
      逐位不变；`ResolverScopedReductionTests.TargetMultiplier_ArmorWithDefenseCategory_
      NotInList_NotAppliedToTargetMultiplier` 钉住"护甲即便是 `defense` 类别、数值很大，未被
      显式列进清单就不计入目标乘区"这一修复点。
    - **被暴击减免新介入点**：命中表"暴击"分支取样（`IRngHost.Next`）之前，遍历
      `CombatOptions.CritTakenReductionStats`（默认空列表）逐条经 `IStatHost.GetScope` 按
      `ScopeMatches` 过滤后求和，从 `ResolveChance(table.Crit, sourceId) + crit_chance_bonus`
      里扣减，`Math.Max(0.0, ...)` 下限夹取到 0——不改变 RNG 流 id（仍是
      `CombatOptions.RngStream`），不改变取样次数（只影响取样前 `chance` 的数值本身，disabled
      分支本就不取样，见 `Resolver.RollBranch`）。默认空列表，既有内容数据/回放基线不受影响。
    - **`IStatHost` 变更**：新增 `GetScope(Id stat)` 默认接口成员（恒 `"any"`，`StatHost` 侧
      显式实现）；首版新增的 `GetDefinitionIdsByCategory(string category)` 在本次返工已撤回
      （从未发布，直接删除签名，不留废弃占位——G3 ABI 门禁"只能新增"的约束只适用于已发布的公开
      契约，本任务在 `StatHost` 侧的实现细节改动不构成对外破坏性变更，`toolchain/abi_probe.ps1`
      对已发布基线 `breaks=0`）。理由同 `IUnitAccess.GetSourceKind`/`GetMapId`（T-N1-6
      先例）——本接口已有多个模块的测试假实现，默认值保证它们不必跟着改也能继续编译，默认实现
      下新介入点天然退化为空列表、零命中，等价于"新机制未生效"。仅 `StatHost`（生产实现）显式
      覆盖，`Tests.Presentation.Assembly.InterfaceDefaultMemberForwardingTests` 门禁已验证无
      遗漏转发。

19. **T-N1-8（[ADR-0030](../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
    决策 6；06 第 4.2 节 2026-09-14 修订段）：`combat.level_diff_table` 接入 `DetermineHit`，
    命中/暴击改加减式公式并接 Δ，双向生效；"有效等级是否计入装备等级偏移"策略项；`miss` 分支
    新增 `hit_stat`（攻击者命中属性）**：
    - **命中/暴击公式**：`未命中率 = 基础未命中 − 攻击者命中属性(hit_stat) + 未命中加成(Δ)`，
      `暴击率 = 攻击者暴击属性 − 暴击压制(Δ)`（在既有 T-N1-7 被暴击减免之外再减）；结果夹取到
      [0,1]（`Resolver.Clamp01`，crit 分支沿用既有 `Math.Max(0.0, ...)` 下限夹取，未设上限——同
      T-N1-7 既有实现，本任务不扩大范围改动）。Δ = 目标有效等级 − 攻击者有效等级，取自
      `Resolver.ResolveLevelDiffAdjustments`，无条件计算一次（不产生 RNG 消耗，只影响掷骰前的
      chance 数值本身），`CombatOptions.LevelDiffTableId` 缺省 `null` 时两项恒为 0，不写
      `level_diff:` 追踪日志——与 T-N1-8 之前逐位一致，回放基线不受影响（见
      `core/gameplay/tests/Replay/README.md`"如何更新基线"第 1 步核对结论：`ReplayWorldBuilder`
      不依赖 `data/_sample`、自带的 `combat.hit_table_config` 全分支禁用且不装配
      `LevelDiffTableId`，Δ 相关代码路径在回放场景里从未被触发，基线未变）。
    - **06 原文"未命中率 = 基础未命中 − 攻击者命中属性 + 目标闪避属性 + 未命中加成(Δ)"里的
      "+ 目标闪避属性"一项，本实现选择不叠加**（判断记录，契约存在歧义）：`dodge` 是六分支之一，
      已经是独立判定、单独消耗一次掷骰（见判断记录 2"dodge 查询防御者"），06 原文写在同一条公式里
      的"目标闪避属性"若再叠加进 `miss` 的 chance 计算，会与 `dodge` 分支重复表达"目标更容易躲开
      攻击"这同一件事——06 全文没有说明这两处是否应该同时生效还是二选一。本实现选择不叠加，只保留
      "基础未命中 − 攻击者命中属性 + 未命中加成(Δ)"，`miss`/`dodge`/`parry`/`glancing_blow`/`block`/
      `crit` 六分支既有优先序不变（禁止事项"不改九步顺序"同样约束六分支优先序）。若游戏口味需要
      "目标闪避属性"额外叠加进 miss 公式，需要回来在 `RollMissBranch` 里加一项，而不是在数据层
      绕过。
    - **`HitTableBranch` 新增 `HitStat`（`Id?`，仅 `miss` 分支消费，四参构造重载）**：不复用既有
      `Stat` 字段——`Stat` 在六分支里的既有语义是"提供时概率直接取该属性当前值，否则取 `Base`"
      （见判断记录 11），与"从 `base` 算出的概率上再额外减去一个命中属性"是不同的运算，复用会与
      既有语义冲突，因此单开 `hit_stat` 字段名，且只登记在 `miss` 分支的字段列表
      （`CombatSchemas.MissBranchFieldList`）——其余五分支即便数据里误填 `hit_stat` 也没有任何
      消费点。
    - **`CombatOptions.LevelDiffTableId`（`Id?`，默认 `null`）**：不接表时 `Resolver` 的行为与
      T-N1-8 之前逐位一致；配置了但该 id 在 `combat.level_diff_table` 里找不到（表未加载/拼错 id）
      同样退化为 Δ 加成/压制恒 0，不抛异常（同 `ArmorStat` 等既有属性缺失"按 0 处理"的防御姿态）。
      `combat.level_diff_table` 是**可选表**：`CombatDataLoader.LoadLevelDiffTables` 不像
      `LoadHitTables`/`LoadResistCurvesBySchool` 那样要求表必须存在，未注册 schema、或注册了但
      数据源没有对应文件（`IDataRegistryView.Tables` 判断记录，见 `CombatDataLoader.HasTable`）
      都返回空字典，不阻断构造——单机/既有测试夹具/T-N1-8 之前的既有装配都不受影响。
    - **"有效等级是否计入装备等级偏移"（`CombatOptions.EffectiveLevelIncludesGearOffset`，默认
      关闭）与 `IGearLevelOffsetProvider` 接口钩子（新契约，`core/rules/common`）**：关闭时有效
      等级恒等于 `IUnitAccess.GetLevel`（角色等级本身）；开启时叠加
      `IGearLevelOffsetProvider.GetGearLevelOffset` 的返回值。**契约缺口如实上报**：06 第 4.2 节
      原文"开启时有效等级 = 角色等级 + 偏移曲线(平均装备等级 − 期望装备等级)"——"期望装备等级曲线
      E(L)"归属 `sim.anchor` 表（阶段 N6 仿真骨架，尚未落地）、"平均装备等级"查询归属装备模块
      （`core/carriers/item.IEquipmentHost`，阶段 N2，尚未落地），两者均不是本模块（`combat`，L2）
      允许依赖的对象（不引用 L3 具体类型、不依赖尚不存在的仿真模块）。本任务因此只落地策略项与
      接口钩子（`IGearLevelOffsetProvider.GetGearLevelOffset(unitId)`——直接返回"已求值好的偏移量"
      这一个数，不暴露两个中间量），真实实现（把 `IEquipmentHost`/未来 `sim.anchor` 接到这个接口）
      留给阶段 N2/N6 落地时装配；测试用 `FakeGearLevelOffsetProvider` 验证策略项开关本身在
      `Resolver` 里正确生效（见 `core/rules/combat/tests/ResolverLevelDiffTests.cs`）。
    - **`combat.level_diff_table` 四条曲线的横轴与消费范围**：`miss_bonus`/`crit_suppression`
      横轴是 Δ（新增 `CurveAxis.LevelDiff`，见 `core/foundation/data_registry/schema/README.md`
      同名判断记录），本模块消费；`xp_factor`（经验系数）横轴同样是 Δ，`grey_line`（灰名界线）
      横轴是攻击者有效等级本身（不是 Δ——见 `CombatSchemas.LevelDiffTable` 判断记录 2"待设计层
      确认"），两者本任务只登记 schema 与样例，不在本模块内求值消费——分阶段落地计划阶段 N1 任务
      清单原文"经验系数与灰名界线两条曲线本任务只登记 schema 与样例，消费者在 N4（Progression）
      接入"。
    - **回放基线**：`data/_sample/combat/combat.hit_table_config.json` 的 `miss` 分支已真实接上
      `hit_stat: "stat.hit_rating"`（并新增 `data/_sample/stat/stat.definition.json` 的
      `stat.hit_rating` 一行），`data/_sample/combat/combat.level_diff_table.json` 已新增一条真实
      样例记录（四条曲线，`|Δ|<=2` 每级加得少、`>=3` 陡增并封顶到 ±0.30/±0.15，双向生效）——满足
      "示例数据必须真实接上"这一要求；但 `combat.hit_table_config`/`combat.level_diff_table` 均不
      在 `Replay` 场景（`core/gameplay/tests/Replay/ReplayWorldBuilder.cs`）的依赖范围内
      （该场景固定不读 `data/_sample`），且 `CombatOptions.LevelDiffTableId` 需要显式配置才会生效
      （默认 `null`），因此本次 `replay_baseline.json` **未发生变化**，已按 Replay README 五步的
      第 1 步"确认这是否是一次有意的战斗结算行为变化"核实（跑 `ReplayBaselineTests` 全绿、diff
      为空）——本次没有第 2～5 步的基线更新提交内容，这也是拍板 12"命中公式改写与回放基线更新
      同一提交"的另一种满足方式：改写发生了，但（如实核实后）不产生基线差异，因此不存在"分开
      提交"的风险。

## 契约缺口 / 未决问题

- `IThreatTable` 契约的方法签名对"是否每单位一份实例"没有强约束（见判断记录 1），如果后续
  ai/skill 模块假设"每次 `GetThreatTable` 返回的是互相独立的实例"，需要重新核对本模块的共享
  实现是否满足调用方预期（目前看外部可观察行为一致，无需假设内部是否共享）。
- 06 未规定命中表六分支的概率查询主体归属攻击者还是防御者（判断记录 2）、偏斜/格挡是否与暴击
  互斥（判断记录 4）——这两处若游戏口味清单有明确要求，需要回来调整 `Resolver` 而非在数据层
  绕过。
- **T-N1-8**：06 第 4.2 节与 04_经验.md 只定性描述"灰名界线随攻击者等级放宽"，未给出具体公式——
  `combat.level_diff_table.grey_line` 当前选取的形态（横轴攻击者有效等级、纵轴"允许的 Δ 下界
  绝对值"）是本任务选取的最简形态，消费公式（灰名门槛具体如何从这条曲线换算、目标名字五色的固定
  分档）留待阶段 N4 落地经验模块时确认（见 `CombatSchemas.LevelDiffTable` 判断记录 2、README
  判断记录 19）。
- **T-N1-8**：06 第 4.2 节原文"未命中率 = 基础未命中 − 攻击者命中属性 + 目标闪避属性 + 未命中
  加成(Δ)"里的"+ 目标闪避属性"一项，与独立的 `dodge` 分支是否应该同时生效，06 未给出明确规定
  （见判断记录 19）——本任务选择不在 `miss` 公式里叠加这一项，若游戏口味清单后续有明确要求，需要
  回来调整 `RollMissBranch`。
- **T-N1-8**："有效等级是否计入装备等级偏移"策略项开启时的真实装备等级偏移来源
  （`IGearLevelOffsetProvider` 的生产实现）留给阶段 N2（装备模块）/N6（仿真锚点表 `sim.anchor`）
  落地时装配，本阶段只有接口钩子与测试用 Fake 实现（见判断记录 19）。
- `combat.resist_curve` 没有"同一 school 多条曲线覆盖策略"的显式规定，本模块按"后加载覆盖先
  加载"处理（见 `CombatDataLoader` 注释），不阻断构造；如果需要阻断，应改为在
  `CombatResistCurveValidationRule` 里新增"同 school 重复"检查项。

## 不负责什么

- 不处理死亡后的复活流程——`Resolver` 落地到生命值 0 时只调用 `IUnitAccess.SetAlive(false)`
  并发 `unit.died`，`CombatOptions.DeathPolicy` 只是把 06 第 4.6 节三种策略的配置值透传给
  L3/L4，复活的具体流程（回到复活点/读档/结束存档周期）不在本模块实现。
- 不实现光环/免疫/吸收池的真实存储——`IAuraQuery` 由 `core/rules/skill` 实现，本模块只消费
  该接口；测试用 `FakeAuraQuery`。
- 不解析 `skill.def`/`skill.aura_def` 等技能相关数据表。
