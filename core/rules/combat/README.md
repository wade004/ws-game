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
    CombatOptions.cs        构造期策略配置（命中表 id、属性 id 引用、系数、脱战时长、仇恨上限、死亡策略）
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

4. **偏斜/格挡命中时不再参与暴击判定**：`HitResult` 是扁平枚举而非位标记，一次结算只报告一个
   分支标签。本实现的优先序是 miss > dodge > parry > glancing_blow > block > (crit | hit)——
   偏斜或格挡一旦触发，直接跳过暴击判定（`isCrit` 恒 false）。这是一个未在 06 中明确规定的
   简化，符合 WoW"偏斜不能暴击"的常见设计直觉；`CombatDamageDealtEvent.IsCrit` 与
   `ResolveResult.Hit` 因此不会同时表达"暴击的偏斜"这种组合。

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

13. **`CombatTickHandler` 只负责脱战计时推进**：结算本身（`ICombatHost.ResolveEffect`）由
    skill 模块在效果原语落地的那一刻同步调用，不经 tick 编排；`CombatTickHandler` 只把
    `ICombatHost.Update(dt)`（脱战判定）接到 `TickPhase.CombatResolution`，离散步只记警告
    （本项目未启用离散时间模型，见 ADR-0013）。这与任务书"效果结算本身由 skill 在步骤 3 调用
    ResolveEffect 即时完成"的说明一致。

14. **阶段 3 整理"事项三"：`Resolver` 步骤 7 免疫判定叠加 `IStaticImmunityProvider`**（新增
    `core/rules/common` 契约，见该文件顶部注释）：`Resolve` 步骤 7 的 `immune` 判定改为
    `_auras.IsImmune(...) || _staticImmunity.IsImmune(...)`——`IAuraQuery.IsImmune` 反映当前生效
    的免疫类光环，`IStaticImmunityProvider.IsImmune` 反映生物模板/tier 一类内容驱动的固定免疫（如
    `creature.template.immunities` 声明的学派免疫），二者任一为真即视为本次结算免疫。构造参数
    `staticImmunity` 可选，缺省 `NullStaticImmunityProvider`（一律不免疫），不改变未接入方的既有
    行为；真实实现（`CreatureImmunityProvider`）在 `core/carriers/creature`，本模块不产生对 L3 的
    编译期依赖。

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
