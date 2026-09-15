# L1 数值层 · power_set 资源池

职责：定义单位可用资源类型集合（法力/怒气/连击点等，数量与种类完全由数据定义，见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L1 模块表 `power_set` 行、
[06_规则层_属性技能战斗AI.md](../../../architecture/06_规则层_属性技能战斗AI.md) 第 2 节）与其
再生/衰减规则；规则层（L2）只经 `IPowerHost` 消费，不知道具体实现。

依赖：只依赖 `core/foundation`（`Common`/`EventBus`/`DataRegistry`/`SimLoop`）与 .NET 标准库；
不引用任何引擎适配层实现；本模块**不引用 `core/numbers/stat_block` 的任何类型**——"上限引用
属性"经具名委托 `StatLookup` 注入，见 `contracts/StatLookup.cs` 与 `schema/README.md`"与
stat_block 的关系"一节（power_set 与 stat_block 同层、并行开发，避免编译期相互耦合）。

## 目录

```
power_set/
  README.md
  contracts/
    PowerSchemas.cs        arch.power_type 的 TableSchema（PowerSchemas.PowerType）
    PowerTypeDefinition.cs 一条 arch.power_type 记录的强类型视图
    StatLookup.cs           StatLookup 具名委托
    IPowerHost.cs            PowerHost 契约
    IPowerDiagnostics.cs     诊断出口
    Events.cs                PowerEventKeys、PowerChangedEvent、PowerDepletedEvent
  core/
    PowerHost.cs               IPowerHost 默认实现
    InMemoryPowerDiagnostics.cs IPowerDiagnostics 默认实现
    PowerTickHandler.cs         ITickPhaseHandler 实现，接入 sim_loop tick
  schema/
    README.md   arch.power_type 字段说明 + 表清单补录判断记录
  tests/
    PowerHostTests.cs
    PowerTickHandlerTests.cs
```

## 契约摘要

- `IPowerHost.RegisterUnit(unitId, powerTypes)` / `UnregisterUnit(unitId)`：单位与其持有资源
  类型集合的登记，`powerTypes` 数量与种类不限（禁止硬编码为 1，见落地方案 T2-2 行禁止事项）。
- `GetPower`/`GetPowerMax`/`HasPower`：只读查询。
- `ModifyPower(unitId, powerType, delta, sourceId)`：06 第 2.2 节 `modifyPower` 契约方法，
  夹取到 `[min, max]`（`allow_overflow` 时上限不夹取），变化发 `power.changed`，触底发
  `power.depleted`（只在从大于 min 变为等于 min 的那一次）。
- `SetInCombat(unitId, inCombat)`：脱战瞬间对 `refill_on_leave_combat` 的资源回满（见 06 第
  4.5 节"资源回复规则切换（如脱战自动回满）"）。
- `RefillAll(unitId, sourceId)`（T-N4-5 新增默认接口成员）：升级回满——把该单位全部
  `start_full=true` 的回复型资源回满到上限，积累型资源（`start_full=false`）不动，见下方判断
  记录"升级回满"。
- `Advance(unitId, timeUnits)` / `AdvanceAll(timeUnits)`：按注册顺序遍历单位与资源，每种资源
  先 regen 后 decay（decay 只在脱战生效），确定性推进。
- `RecomputeMax(unitId)`：`max_source.kind == "stat"` 的资源类型重新查询上限，上限下降时当前
  值随之夹取。

## 设计要点与判断记录

1. **`arch.power_type` 是表清单补录，不是新原语**——04 总索引没有单列这张表，详见
   `schema/README.md`"判断记录"一节，供设计层复核是否需要同步更新 04 第 1.1 节总索引。
2. **`PowerTickHandler` 挂在 `TickPhase.TriggerEvaluation`**——03 第 4.2 节固定的六个可注册
   阶段里，`TriggerEvaluation`（"触发评估"）在语义上最接近"随时间推进的被动结算"；06 原文未
   规定 PowerSet 应挂在哪个阶段，任务书拍板选定该阶段。离散步不推进，只记一条诊断警告，不抛
   异常——这是有意的一致取舍，不是"离散模式未启用"的遗留：ADR-0013 离散时间模型已在
   `core/gameplay/assembly` 真实接通（`ITurnScheduler`/`TimeModelSwitch`），但 PowerSet 的资源
   回复/衰减本身按连续时间语义设计（"随时间线性回复"），离散步（回合制的一步）不产生自然的
   "经过了多少秒"，所以本模块拍板离散步下按兵不动、只记警告，把"资源是否也该按回合推进"这一
   口味决策留给游戏层（如需要，可在游戏层按回合数×固定换算调用 `AdvanceAll`）。
3. **`power.depleted` 事件字段 `{unitId, powerType}`**——06 第 2.2 节只详细定义了
   `power.changed` 的字段，`power.depleted` 只在 01 模块表"主要事件"列出现了 key 名字，未给
   出字段。任务书拍板携带 `{unitId, powerType}`（触发条件已经隐含了"降到了 min"这一信息，
   不需要额外携带数值），与 sim_loop/hook_registry 处理"登记表未逐字段规定"的一贯做法一致。
4. **`ModifyPower` 的 `sourceId` 不写入 `power.changed`/`power.depleted` 事件**——06 原文
   `power.changed` 字段明确只有 `{unitId, powerType, oldValue, newValue}` 四个，`sourceId`
   只是契约方法签名里"标记本次修改来源"的入参（供未来扩展审计/日志），本任务不额外发挥。
5. **`StatLookup` 为 null 但资源类型确实是 `stat` 来源时，在"首次真正需要用到"（即
   `RegisterUnit`/`RecomputeMax` 触发 `ComputeMax`）时抛 `InvalidOperationException`，而不是
   构造 `PowerHost` 时立即抛**——因为一个 `PowerHost` 实例可能只承载全部 `fixed` 来源的资源
   类型（`StatLookup` 确实用不上），构造期强制要求非 null 会对这种合法场景造成不必要的约束；
   一旦真正遇到 `stat` 来源资源类型且没有 `StatLookup`，则立即报错，不静默降级（呼应 11 第 4
   节"运行时不做静默降级"）。
6. **`SetCurrentClamped` 是 `ModifyPower`/脱战回满/`RecomputeMax` 上限夹取三处共用的唯一"落值
   +发事件"入口**——保证"值变化才发 `power.changed`""触底只发一次 `power.depleted`"这两条
   规则在三个调用点上行为完全一致，不出现只有部分路径遵守规则的情形。

## T-N4-5：升级回满（`RefillAll`）

（ADR-0033 决策 7"升级回满：`progression.level_up` 触发生命与资源回满，由资源池订阅实现"；
分阶段落地计划 T-N4-5；06 第 2.5 节同条。）

7. **`RefillAll` 是新增的第四个共用 `SetCurrentClamped` 落值入口，只回满 `start_full=true` 的
   回复型资源**——与判断记录 6 的 `ModifyPower`/脱战回满/`RecomputeMax` 上限夹取三处同一惯例，
   `RefillAll` 不绕过事件（硬性规则），值确实变化才发一次 `power.changed`。**契约疑点上报**：
   ADR-0033 决策 7/06 第 2.5 节原文只说"生命与资源回满"，未区分资源类型是否"积累型"
   （`start_full=false`，如连击点一类"起始空、靠战斗行为累积"的资源）——本方法取"只回满
   `start_full=true` 的资源，积累型资源不动"这一保守判断，理由同 `RefillOnLeaveCombat`
   既有先例（同样只对显式登记为 true 的类型生效，不是无条件回满全部资源）。若设计层认定积累型
   资源也应升级回满，需要在 `arch.power_type` 补一个显式策略字段（同 `refill_on_leave_combat`
   写法），详见 `IPowerHost.RefillAll` 方法注释。
8. **接线点在 `RulesAssembly`，不在本模块内部**——本模块（L1）不知道、也不该知道 `progression`
   （同层 L1，但按判断记录 4 的一贯原则并行开发、互不引用），`RefillAll` 只是被动能力；真正订阅
   `progression.level_up` 并调用它、以及消费 `ProgressionOptions.RefillOnLevelUp` 开关，都在
   `core/rules/assembly.RulesAssembly` 构造函数完成（见该文件"T-N4-5"判断记录），同
   `progression.level_up`/`progression.state_restored` → `StatHost.RecomputeRatingStats` 两条
   既有订阅同一装配层惯例。

## 诊断

`IPowerDiagnostics`（默认实现 `InMemoryPowerDiagnostics`，内存列表）目前只记录一类警告：
`PowerTickHandler.Execute` 收到 `SimStepKind.Discrete` 步时。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| 资源池注册、修改、回复/衰减、脱战回满、上限重算的实现机制 | 是 | 具体资源类型集合、每种资源的具体参数取值 |
| `arch.power_type` 表字段定义 | 是 | 具体资源类型数据行 |
| `power.changed`/`power.depleted` 事件的产生时机与字段 | 是 | 订阅这两个事件做表现（如资源条动画） |
| `StatLookup` 委托签名 | 是 | 把具体 `IStatHost` 一类实现适配成该签名并注入 |
