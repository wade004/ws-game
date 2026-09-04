# L2 规则层 · targeting 目标选择

职责：按数据驱动的"来源 + 过滤 + 排序 + 回退"链解析出目标列表（见
[06_规则层_属性技能战斗AI.md](../../../architecture/06_规则层_属性技能战斗AI.md) 第 5 节
`Targeting`、[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L2 `targeting` 行）。
技能定义本身不写死"选最近的敌人"，而是引用一条已登记的选择链，供多个技能、AI Rotation、玩家辅助
施法共用同一条链（见 06 第 3.7 节）。

依赖：`core/rules/common`（`IUnitAccess`/`ITargetHost`/`IThreatTable`/`IExprHostFactory`/
`WellKnownPowers`/`RulesEventKeys`/`TargetingResolvedEvent`）、L1 `Core.Numbers.Faction`
（`IFactionMatrix`/`Reaction`）、L1 `Core.Numbers.PowerSet`（`IPowerHost`）、L0
`Core.Foundation.EngineAdapter`（`ISpatialQuery`/`Shape`/`QueryFilter`）、L0
`Core.Foundation.Expr`（`ExprParser`/`ExprEvaluator`/`IExprHost`/`IExprSchema`）、L0
`Core.Foundation.DataRegistry`（`IDataRegistryView`/`DataRecord`/`TableSchema`/`IValidationRule`）、
L0 `Core.Foundation.EventBus`（`IEventBus`，仅 `TargetingOptions.EmitResolvedEvent = true` 时使用）。
不引用 `Core.Rules.Skill`/`Core.Rules.Combat`/`Core.Rules.Ai` 的具体类型（见 README"谁实现、谁调用"
矩阵：`ITargetHost` 由本模块实现，供 skill/ai 调用，不反向依赖）。

## 目录

```
targeting/
  README.md
  contracts/
    ITargetSourceStrategy.cs    ITargetSourceStrategy、TargetContext
    TargetStrategyRegistry.cs   策略注入点：Register/Get/TryGet/Names
    TargetSchemas.cs             target.chain_def 的 TableSchema
  core/
    TargetChainDef.cs            target.chain_def 记录的强类型视图（含 shape/sort_by 解析）
    BuiltinTargetStrategies.cs   六个内置来源策略 + RegisterAll
    TargetFilterExprSchema.cs    filters 里 Expr 文本解析用的宽松 IExprSchema（self/target 分组）
    ChainDefValidationRule.cs    source 已注册 / filters 可解析 / fallback 无环 三项校验
    TargetHost.cs                ITargetHost 默认实现 + TargetingOptions
  schema/
    README.md                    字段说明与判断记录
  tests/
    TargetHostTests.cs
    TargetStrategyRegistryTests.cs
    ChainDefValidationRuleTests.cs
```

## 设计要点与判断记录

1. **策略注入，不硬编码**：内置六种来源（`current_target`/`nearest_in_shape`/`self`/
   `party_lowest_hp_pct`/`threat_top`/`all_in_shape`）与游戏层自定义来源经同一个
   `TargetStrategyRegistry.Register` 入口登记，`TargetHost` 内部只有一处
   `_registry.Get(chain.Source).Collect(ctx)`，不出现任何策略名字面量的 switch/if 分支（见
   落地方案 T2-9 行"禁止目标链策略硬编码在 core/rules 内而不经策略注入机制"，对应 00 第 4 节
   原则 10、01 第 8 节第 3 种合法调用方式"策略注入回调"）。

2. **管线顺序固定为"来源收集 → 过滤(AND) → 排序 → 截断 → 空则回退"**：来源策略只负责按自己的
   语义给出一份"自然顺序"候选（如 `nearest_in_shape` 按距离升序、`party_lowest_hp_pct` 按血量
   百分比升序，均以 Id 升序决胜保证确定性）；链未显式声明 `sort_by` 时这个自然顺序原样保留，
   `max_targets` 默认 1 直接截取"来源认为最优先"的第一个——这样"自动选最近敌对目标"这条链
   （`nearest_in_shape` + `filters: [relation:hostile, alive]`）不需要来源策略了解任何过滤条件，
   过滤只是在保序前提下删元素，天然选出"最近的、且满足条件"的目标，不需要在 `TargetHost` 或
   策略内部为这一个场景单独处理。

3. **`filters` 数组取 AND，不是 OR**：与 `Core.Rules.Common.SkillFilter.Matches` 三维度取 OR 是
   不同场景——`SkillFilter` 的三个维度是"影响范围的并集式声明"，这里的 `filters` 是调用方在同一
   条链里显式列出的一串独立筛选条件（06 第 5 节示例"存活、阵营关系、免疫标志等"），语义上是
   "同时满足"。

4. **内置策略不做 alive/relation 过滤**：`party_lowest_hp_pct` 按"友方"关系收集候选是它名字
   本身的语义（不筛友方就不是这条策略了），但不会额外排除死亡单位——06 第 5 节把"存活、阵营
   关系、免疫标志等"明确列为 `filters` 字段的职责，内置策略只负责"来源"这一维度，避免同一件事
   在策略与过滤两处各做一半、行为不透明。

5. **Shape 模板与运行期锚定**：`target.chain_def.shape` 只登记 `kind`/`radius`/`angle`/`length`/
   `width`，不登记 `origin`/`direction`/`rotation`——链是可被多个施法者复用的模板，`TargetHost`
   在每次 `Resolve` 时用施法者当前坐标（`IUnitAccess.GetPosition`）与朝向（`GetFacing`）重新
   构造一个锚定后的 `Shape`（`RebaseShape`），与 common/README.md 判断记录 2
   "`ISkillHost.FindUnits` 的 `origin` 参数锚定可复用形状模板"同一模式。`rect` 缺少
   `halfExtents`/`rotation` 专属字段，复用 `length`/`width` 换算半宽高，`rotation` 同样用施法者
   朝向锚定，详见 `schema/README.md`。链未声明 `shape` 时退化为以施法者坐标为圆心、半径
   `TargetingOptions.DefaultRadius` 的 circle（任务书拍板）。

6. **`TargetContext.Shape` 类型忠实保留为 `Shape?`，但 `TargetHost` 保证调用策略前总有值**：
   任务书给出的字段签名是 `Shape?`，但按第 5 点的处理，传给 `ITargetSourceStrategy.Collect` 的
   `ctx.Shape` 在实践中永远非空（要么是链自己声明的、要么是退化出的默认 circle）；保留可空类型
   只是为了忠实签名，策略实现里直接 `ctx.Shape!.Value` 使用即可，不需要处理"确实为空"分支。

7. **契约缺口：`IExprHostFactory` 没有配套的 `IExprSchema`**：`ExprParser.Parse` 强制要求一个
   `IExprSchema` 才能判定"点分标识符是宿主引用还是 Id 字面量"（ADR-0015），但
   `common/contracts/IExprHostFactory.cs` 只声明了"怎么拿到一个绑定了上下文的 `IExprHost`"，没
   有配套声明"这个宿主开放哪些 `group.key` 签名"的静态登记表（具体把 self/target 分组接到哪些
   查询 key 属于集成任务，见该文件判断记录）。本模块的取舍：`TargetFilterExprSchema` 只要
   `group` 是 `self` 或 `target` 就一律判定为"合法引用"，不关心具体 key 或声明的返回类型——
   `ExprEvaluator` 求值时只看 `IExprHost.Query` 的实际返回值，从不读取这里登记的
   `ExprSignature.ReturnKind`，所以这个"宽松登记"不会放过真正的类型错误，只是把"分类判定"这一
   步交给运行期。若后续集成任务提供了真正的 `IExprSchema`，可以整体替换本类型而不影响其余契约。

8. **`ChainDefValidationRule` 的"Expr 可解析"检查不是零工作量**：任务书原句"Expr 可解析（数据
   注册表已做）"是针对 `FieldKind.Expr` 标量字段的一般表述；`filters` 是 `Array`，
   `DataRegistry` 内置的 `expr_parsable` 检查项不逐元素解析数组，因此这项检查由本规则自己跑一遍
   `ExprParser.Parse` 完成，详见 `schema/README.md`"校验规则"。

9. **`source` 不声明为 `FieldKind.Enum`**：`Enum` 要求取值集合在 `TableSchema` 构造期固定，与
   "游戏层可注册更多来源"矛盾；改用 `ChainDefValidationRule(strategyRegistry.Names)` 在数据校验
   期做等价检查，取值集合随注册表变化，不需要改 schema。

10. **`EmitResolvedEvent` 默认 false，为 true 时构造期要求提供 `eventBus`**：01 第 L2
    `targeting` 行登记了事件 `targeting.resolved`，与 06 第 5 节"目标选择本身不发事件"冲突，
    common/README.md 判断记录 4 已把这个冲突登记在案并把 `TargetingResolvedEvent` 定义为"建议"
    类型；本任务书进一步拍板"可选发布，默认 false"。`TargetHost` 构造期若 `EmitResolvedEvent`
    为 true 但未传 `eventBus` 直接抛 `ArgumentException`，不做"悄悄不发"的静默降级。事件只在
    最外层 `Resolve` 调用发布一次（`chainId`/`targetIds` 是最终结果，不是每次 fallback 内部
    递归都发一次）。

11. **回退环两道防线**：`ChainDefValidationRule` 在数据校验期检测 `fallback` 指针链成环
    （内容提交时即可拦下）；`TargetHost.ResolveChain` 额外维护
    `TargetingOptions.MaxFallbackDepth`（默认 8）独立防御——即使某个 `IDataRegistryView` 实现
    绕过了校验（如测试直接构造未跑校验规则的数据），运行期也不会无限递归。

12. **排序键 tie-break 统一按 Id 升序**：`distance`/`hp_pct`/`threat`/`level` 四个排序键同值时都
    退化为按 Id 升序，保证"同输入两次结果一致"这一确定性要求（见落地方案通篇的确定性拍板）。

## 不负责什么

- 不提供 `IExprHostFactory` 的默认实现——集成任务职责（见 common/README.md"谁实现、谁调用"）。
- 不实现施法管线（`skill`）、结算管线/仇恨表写入（`combat`）、Rotation/行为外壳状态机（`ai`）——
  本模块只回答"给一条链 id 和施法者 id，解析出哪些单位"，不关心解析结果被拿去做什么。
- 不做具体游戏内容的目标选择口味决策——"选最近的敌人"还是"选血量最低的友方"完全是
  `target.chain_def` 数据决定的，本模块只提供六个内置来源与一套注入机制。
