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
  RulesExprSchema.cs         唯一一份 IExprSchema：登记全部已知 group.key 签名 + 已知分组放行
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
| | `day_cycle`/`turn_index`/`round_index`/`is_my_turn` | 占位（分别对应昼夜循环、离散模式），恒返回 0/false，见判断记录 |
| `event` | `event.<field>` | 经 `IExprReadableEvent.TryGetField`（见 common 模块该接口） |
| `world`/`quest`/`player` | 任意 | 委托给构造注入的 `extraGroups[group]`（`IExprGroupProvider`），未注入按默认值处理并警告一次 |

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
   String 返回空串——具体取哪个默认值由 `RulesExprSchema.Instance.TryGetSignature` 登记的
   `ReturnKind` 决定，未登记精确签名的 `group.key`（如 `target.distance_to_target` 这种"仅
   self"的 key 出现在 target 分组下）统一退化为 `Bool(false)`。

3. **`enemies.nearest_distance` 无敌人时返回 `1_000_000`，不是 `double.MaxValue`**：`double.MaxValue`
   参与乘法等运算容易溢出到 `PositiveInfinity`，而 `1_000_000` 远超任何游戏口味配置的感知/攻击
   距离，`enemies.nearest_distance < N` 一类条件在无敌人时仍能正确判定为假，且不会在下游算式里
   产生 `Infinity`/`NaN`。`count_in_range`/`nearest_distance` 都通过 `ISpatialQuery.QueryRadius`
   实现——`nearest_distance` 用 `UnboundedSearchRadius`（同一个 `1_000_000`）近似"查询全部"（
   `ISpatialQuery` 没有无半径限制的查询方法）。

4. **`event.<field>` 缺失时统一返回 `Bool(false)`，不是按字段"应有类型"返回**：`event` 分组的
   具体字段类型随触发事件的类型而变（`RulesExprSchema` 对 `event` 分组只做"已知分组放行"，不
   逐字段登记类型），既然无法在缺字段时确定"本应是什么类型"，本类型统一退化为 `Bool(false)`
   （与"未登记精确签名"分支复用同一条 `DefaultFor` 路径）。

5. **`day_cycle`/`turn_index`/`round_index`/`is_my_turn` 是纯占位**：昼夜循环不在本任务范围内；
   离散（回合制）时间模型本项目暂不启用（ADR-0013），四个键分别恒返回 `0`/`0`/`0`/`false`，
   供已经在 Rotation/transitions 里写了这些引用的内容先解析通过，不代表任何真实语义。

6. **`world`/`quest`/`player` 分组缺失的警告"只记一次"，去重粒度是 `RulesExprHostFactory` 实例，
   不是每次 `CreateFor` 或每次 `Query`**：同一个工厂实例产出的全部 `Host`（不同 `selfId`/
   `targetId`/触发事件）共享一份 `_warnedMissingGroups`，避免同一局游戏跑下来对同一个未接入的
   分组反复刷屏警告。缺目标（`target.*`）与缺事件（`event.*`）两类警告不做这个去重——每次查询
   都记一条，因为"这次求值到底有没有目标/事件"本身就是每次都可能变化的上下文，不应该被静音。

## `RulesExprSchema` 与 `IExprSchema` 的两层放行策略

1. 精确登记（见上表）：`group.key` 完全匹配时返回精确的 `ExprSignature`（`ReturnKind`/
   `ArgKinds` 均有意义，供 `RulesExprHostFactory` 的"缺目标默认值"按类型选取）。
2. 已知分组放行：`group` 属于 `ExprGroups.All`（九个固定分组）但没有精确登记的 `key`（典型如
   `event.<动态字段名>`、`world`/`quest`/`player` 的任意 key），一律接受为"合法引用，签名未知"
   （`Permissive` 占位签名，`ExprEvaluator` 求值时从不读取这里的 `ReturnKind`）。

`skill`/`ai` 两个模块原先各自的临时 `IExprSchema`（`PermissiveExprSchema`/`AiExprSchema`）已改为
默认注入 `RulesExprSchema.Instance`（`SkillDefCache`/`SkillHost`/`AiHost`/
`AiContentValidationRule` 均新增了可选的 `exprSchema` 构造参数，不传时用
`RulesExprSchema.Instance`），两个临时类型本身仍保留在各自模块目录未删除（不再是默认路径，
供需要复现旧行为的调用方显式传入）。**`targeting` 模块的 `TargetFilterExprSchema` 未纳入本次
改动**——判断记录：落地方案本任务的"允许改动"范围明确列出
`core/rules/common`/`core/foundation/sim_loop`/`core/rules/skill`/`core/rules/ai`
（仅对接改动）/新建 `expr_host`/`assembly`/`tests`，未包含 `core/rules/targeting`；
`RulesAssembly` 组装 `TargetHost` 时仍使用该模块自带的默认 `IExprSchema`，不影响 `filters` 求值
本身的正确性（`TargetFilterExprSchema` 对 `self`/`target` 分组同样是全量放行），只是没有获得
`RulesExprSchema` 更完整的分组覆盖（`combat`/`enemies`/`time`/`event`/`world`/`quest`/`player`
在 `filters` 里目前不可用）。
