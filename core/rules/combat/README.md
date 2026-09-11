# L2 规则层 · combat 战斗系统

职责：结算管线（命中/闪避/招架/偏斜/格挡/暴击 → 基础值 → 暴击倍率 → 施法者乘区 → 护甲/抗性减免
→ 目标乘区 → 免疫吸收 → 落地 → 后置）、仇恨表、进出战斗判定（见
[06_规则层_属性技能战斗AI.md](../../../architecture/06_规则层_属性技能战斗AI.md) 第 4 节）。落地
计划 T2-7（`Resolver`）+ T2-8（`ThreatTable` 与进出战斗）。

依赖：`Core.Rules.Common`（`IUnitAccess`/`IAuraQuery`/`ICombatHost`/`IThreatTable`/
`IStaticImmunityProvider`/`EffectContext`/`ResolveResult`/`HitResult`/`EffectKind`/
`WellKnownPowers`/`RulesEventKeys` 及强类型事件）、L1 `Core.Numbers`（`IStatHost`/`IPowerHost`/
`IFactionMatrix`）、L0 `Core.Foundation`（`IRngHost`/`IEventBus`/`IDataRegistry`/
`ITickPhaseHandler`）。不引用 `core/rules/skill`/`targeting`/`ai` 的具体类型（`IAuraQuery` 用调用方
注入的实现，测试用 Fake）。

## 目录

```
combat/
  README.md
  contracts/
    CombatOptions.cs        构造期策略配置（命中表 id、属性 id 引用、系数、脱战时长、仇恨上限、死亡策略、结算追踪回调）
    ICombatDiagnostics.cs   最小诊断出口
  core/
    HitTableConfig.cs        combat.hit_table_config 强类型视图
    ResistCurve.cs           combat.resist_curve 强类型视图 + 减免求值
    CombatDataLoader.cs      从 IDataRegistryView 加载上面两张表
    Resolver.cs               结算管线九步实现
    ThreatTable.cs            IThreatTable 默认实现
    CombatHost.cs             ICombatHost 默认实现
    CombatTickHandler.cs      接入 sim_loop TickPhase.CombatResolution
    InMemoryCombatDiagnostics.cs
  schema/
    CombatSchemas.cs          两张表的 TableSchema 声明
    CombatValidationRules.cs  概率范围/曲线单调性等校验规则
  tests/
    CombatTestSupport.cs      Fake IUnitAccess/IAuraQuery + 真实 Stat/Power/Rng/Faction/DataRegistry 夹具
    ResolverHitTableTests.cs  命中表六分支 + 手算全链 + 免疫 + 治疗 + 死亡
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

## 契约缺口 / 未决问题

- `IThreatTable` 契约的方法签名对"是否每单位一份实例"没有强约束（见判断记录 1），如果后续
  ai/skill 模块假设"每次 `GetThreatTable` 返回的是互相独立的实例"，需要重新核对本模块的共享
  实现是否满足调用方预期（目前看外部可观察行为一致，无需假设内部是否共享）。
- 06 未规定命中表六分支的概率查询主体归属攻击者还是防御者（判断记录 2）、偏斜/格挡是否与暴击
  互斥（判断记录 4）——这两处若游戏口味清单有明确要求，需要回来调整 `Resolver` 而非在数据层
  绕过。
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
