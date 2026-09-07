# L2 规则层 · expr_host（Expr 宿主）

职责：落地方案 T2-11 集成任务"二、L2 Expr 宿主"——把 04_数据与内容管线.md 第 6.2 节九个 Expr
宿主引用分组（`self`/`target`/`event`/`world`/`quest`/`player`/`combat`/`enemies`/`time`）接到
L1/L2 的具体契约上，提供全架构（内容校验期 + 运行期）共用同一份的 `IExprSchema`/`IExprHostFactory`
默认实现。补齐 `core/rules/common/contracts/IExprHostFactory.cs` 判断记录里"具体实现属于集成任务，
本任务只声明接口"这一缺口。

依赖：`core/rules/common`（`IUnitAccess`/`IAuraQuery`/`ICombatHost`/`IThreatTable`/
`ISkillHost`/`IExprReadableEvent`）、`Core.Numbers`（`IStatHost`/`IPowerHost`/`IFactionMatrix`）、
`Core.Foundation`（`Expr`/`EngineAdapter.ISpatialQuery`/`EventBus.IEvent`）。不引用
`Core.Carriers`/`Core.Gameplay`，不使用 `UnityEngine`/`System.Threading`/`DateTime`/
`System.Random`/`System.Reflection`（同 01 第 6 节、11 第 2～4 节工程规范）。

## 目录

```
expr_host/
  README.md
  IExprGroupProvider.cs      world/quest/player 三个分组的扩展点（L4/游戏层实现）
  RulesExprSchema.cs         L2 基础登记表 RulesExprSchema.Base（严格模式，ADR-0015）+ Compose
  CompositeExprSchema.cs     通用 IExprSchema 合并帮助类型（阶段 3 整理，见下方"组合"一节）
  RulesExprHostFactory.cs    IExprHostFactory 默认实现，内部类 Host 是绑定上下文的 IExprHost
  tests/
    ExprHostTestSupport.cs   测试专用最小假实现（Unit/Stat/Power/Aura/Combat/Faction/SkillHost）
    RulesExprHostTests.cs    ≥14 条用例，覆盖六个分组的键、缺目标默认值、event 字段、extra group
```

## 分组与键（最小集合）

| 分组 | 键 | 说明 |
|---|---|---|
| `self`/`target` | `hp`/`hp_max`/`hp_pct` | 经 `WellKnownPowers.Health` 走 `IPowerHost` |
| | `power(powerTypeId)`/`power_pct(powerTypeId)` | 任意资源类型，参数为 Id |
| | `level`/`faction`/`is_alive` | 经 `IUnitAccess` |
| | `in_combat`/`is_casting` | 经 `ICombatHost`/`ISkillHost`（见下方判断记录） |
| | `has_aura(auraDefId)`/`aura_stacks(auraDefId)` | 经 `IAuraQuery` |
| | `stat(statId)` | 经 `IStatHost` |
| | `has_tag(tagId)` | 经 `IUnitAccess.GetTags` |
| | `position_x`/`position_y` | 经 `IUnitAccess.GetPosition` |
| | `distance_to_target`（**仅 self**）/`threat_top`（**仅 self**，Id） | target 分组下同名 key 落回"未知 key"分支 |
| `combat` | `in_combat`/`is_casting` | 语义等价于 `self.in_combat`/`self.is_casting`，供不想写 `self.` 前缀的条件文本使用 |
| `enemies` | `count_in_range(r)` | 经 `ISpatialQuery.QueryRadius` + `IFactionMatrix.IsHostile` |
| | `nearest_distance` | 无敌人时返回 `RulesExprHostFactory.NoEnemyDistance`（`1_000_000`，见判断记录） |
| `time` | `sim_time` | 经构造注入的 `simTimeProvider` |
| | `since_combat_start` | `simTimeProvider() - combatStartTimeProvider(selfId)` |
| | `turn_index`/`round_index`/`is_my_turn` | 离散模式接入后（ADR-0013）由组装根注入的可选委托驱动，取当前轮/回合序号与"是否轮到本单位行动"；未注入委托时（如未启用离散模式）恒返回 `0`/`0`/`false`，见判断记录 |
| | `day_cycle` | 恒返回 `0`——昼夜循环不在本模块任务范围内，没有任何委托/接线机制，是与前三个键不同的硬边界（不随任何组装配置变化），见判断记录 |
| `event` | `event.<field>` | 经 `IExprReadableEvent.TryGetField`（见 common 模块该接口）——运行期查询任意字段名都会尝试；解析期 `RulesExprSchema.Base` 不逐字段登记，见下方"严格模式"一节 |
| `world`/`quest`/`player` | 任意 | 委托给构造注入的 `extraGroups[group]`（`IExprGroupProvider`），未注入按默认值处理并警告一次；解析期同样不在 `RulesExprSchema.Base` 登记，需要经 `Compose` 叠加游戏层自己的登记表 |

## 判断记录

1. **构造参数清单比任务书多一个可选的 `ISkillHost? skillHost`**：`self/target.is_casting`、
   `combat.is_casting` 语义上必须读 `ISkillHost.IsCasting`（06 第 3.6 节"是否处于读条/引导"是
   skill 模块的运行期状态），但任务书给出的构造注入清单（`IUnitAccess`/`IStatHost`/`IPowerHost`/
   `IAuraQuery`/`ICombatHost`/`IThreatTable`/`ISpatialQuery`/`IFactionMatrix`/两个时间委托/
   `extraGroups`）没有列出 `ISkillHost`。作为清单之外的第 12 个可选构造参数补上；为 `null` 时
   `is_casting` 按默认值 `false` 处理并警告一次（不是编译期错误）。`core/rules/assembly` 的
   `RulesAssembly` 会传入真实的 `SkillHost`。

2. **缺目标时 Id 类型的占位值取 `new Id("none.none")`**：任务书原句已拍板，`RulesExprHostFactory`
   把它登记为 `public static readonly Id NoneId`。数值类型缺目标时返回 0，Bool 返回 false，
   String 返回空串——具体取哪个默认值由 `RulesExprHostFactory` 实例内部持有的 `IExprSchema`
   （`RulesExprSchema.Base`，或注入 `extraSchemas` 时经 `RulesExprSchema.Compose` 合并出的版本，
   见构造函数 `extraSchemas` 参数）的 `TryGetSignature` 登记的 `ReturnKind` 决定，未登记精确签名
   的 `group.key`（如 `target.distance_to_target` 这种"仅 self"的 key 出现在 target 分组下）
   统一退化为 `Bool(false)`。

3. **`enemies.nearest_distance` 无敌人时返回 `1_000_000`，不是 `double.MaxValue`**：`double.MaxValue`
   参与乘法等运算容易溢出到 `PositiveInfinity`，而 `1_000_000` 远超任何游戏口味配置的感知/攻击
   距离，`enemies.nearest_distance < N` 一类条件在无敌人时仍能正确判定为假，且不会在下游算式里
   产生 `Infinity`/`NaN`。`count_in_range`/`nearest_distance` 都通过 `ISpatialQuery.QueryRadius`
   实现——`nearest_distance` 用 `UnboundedSearchRadius`（同一个 `1_000_000`）近似"查询全部"（
   `ISpatialQuery` 没有无半径限制的查询方法）。

4. **`event.<field>` 缺失时统一返回 `Bool(false)`，不是按字段"应有类型"返回**：`event` 分组的
   具体字段类型随触发事件的类型而变，`RulesExprSchema.Base` 不逐字段登记类型（严格模式下这类
   分组要么靠运行期 `IExprReadableEvent.TryGetField` 动态查，要么在解析期落回 Id 字面量——见下方
   "严格模式"一节），既然无法在缺字段时确定"本应是什么类型"，本类型统一退化为 `Bool(false)`
   （与"未登记精确签名"分支复用同一条 `DefaultFor` 路径）。

5. **`turn_index`/`round_index`/`is_my_turn` 已随离散时间模型接入（ADR-0013）真正接线，`day_cycle`
   仍是纯占位**（2026-09-07 改写，此前四个键笼统写成"占位……本项目暂不启用"，与离散模式后续已经
   落地的事实脱节）：`RulesExprHostFactory` 构造函数新增三个可选委托
   `turnIndexProvider`/`roundIndexProvider`/`currentActorProvider`——`core/gameplay/assembly.GameplayAssembly`
   在装配了 `Core.Foundation.SimLoop.TurnScheduler` 时会传入真实实现（分别读
   `scheduler.CurrentTurnIndex`/`scheduler.RoundIndex`/`scheduler.GetCurrentActor()`），此时
   `time.turn_index`/`time.round_index`/`time.is_my_turn` 反映真实的离散回合状态；未注入这三个
   委托的组装场景（如未启用离散模式）仍按原占位语义恒返回 `0`/`0`/`false`，不破坏既有调用方。
   `day_cycle` 不属于这次接线范围——昼夜循环不在本模块任务范围内，没有任何委托/接线机制，恒返回
   `0`，是一条如实记录的边界，不代表任何真实语义，供已经在 Rotation/transitions 里写了这个引用
   的内容先解析通过。

6. **`world`/`quest`/`player` 分组缺失的警告"只记一次"，去重粒度是 `RulesExprHostFactory` 实例，
   不是每次 `CreateFor` 或每次 `Query`**：同一个工厂实例产出的全部 `Host`（不同 `selfId`/
   `targetId`/触发事件）共享一份 `_warnedMissingGroups`，避免同一局游戏跑下来对同一个未接入的
   分组反复刷屏警告。缺目标（`target.*`）与缺事件（`event.*`）两类警告不做这个去重——每次查询
   都记一条，因为"这次求值到底有没有目标/事件"本身就是每次都可能变化的上下文，不应该被静音。

## `RulesExprSchema` 严格模式（阶段 3 整理，ADR-0015）

**本类型早期版本对"已知分组但未登记的 key"一律放行为"合法引用，签名未知"，这违反 ADR-0015 的
决策——"未登记的 `group.key` 组合一律落回 Id 字面量"，会把 `quest.deliver_letter`、
`world.bridge.repaired` 这类内容 id 字面量误判成引用。阶段 3 整理已改为严格模式**：

1. 精确登记（见上表）：`group.key` 完全匹配 `RulesExprSchema.Base`（或 `Compose` 出的组合）时
   返回精确的 `ExprSignature`（`ReturnKind`/`ArgKinds` 均有意义，供 `RulesExprHostFactory` 的
   "缺目标默认值"按类型选取）。
2. 未登记一律拒绝：`TryGetSignature` 返回 `false`，交给 `ExprParser` 按 ADR-0015 消歧规则把该
   点分标识符解析成 Id 字面量——不再有"已知分组放行"这一中间状态。

`RulesExprSchema.Base` 只登记 `self`/`target`/`combat`/`enemies`/`time` 五个分组（`RulesExprHostFactory`
内置真正实现查询的分组）；`event`/`world`/`quest`/`player` 四个分组的具体 key 随事件类型或游戏层
内容而变，本类型构造期不可能穷举，需要这些分组的合法引用时经 `RulesExprSchema.Compose(params
IExprSchema[] extras)` 把游戏层自己的精确登记表与 `Base` 合并（`Compose` 内部用
`CompositeExprSchema` 按顺序尝试：先查 `extras`，再落到 `Base`）。`RulesExprHostFactory`/
`Core.Rules.Assembly.RulesAssembly` 均新增了可选的 `extraSchemas` 构造参数，自动完成这一步合并
（见 `core/rules/assembly/README.md`"阶段 3 整理"一节）。

`skill`/`ai`/`targeting` 三个模块原先各自的临时 `IExprSchema`（`PermissiveExprSchema`/
`AiExprSchema`/`TargetFilterExprSchema`——`targeting` 的这一个在早期版本因"允许改动范围不含
`core/rules/targeting`"而保留，本次整理已确认该目录在允许改动范围内一并处理）均已**删除**，
`SkillDefCache`/`SkillHost`/`AiHost`/`AiContentValidationRule`/`ChainDefValidationRule`/
`TargetHost` 统一默认注入 `RulesExprSchema.Base`（均保留可选的 `exprSchema`/`FilterSchema`
构造参数/字段，供需要复现旧行为或注入测试专用 schema 的调用方显式传入）——`RulesExprSchema.Base`
对 `self`/`target` 分组的精确登记（16 项，见上表）完整覆盖了这三个模块原临时类型实际用到的
全部引用，严格模式下还能额外捕获"拼写成不存在的 key"这类原本被放行分支掩盖的问题。
