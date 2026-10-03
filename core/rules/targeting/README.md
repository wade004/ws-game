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
    （阶段 3 整理：本模块原自带的临时 TargetFilterExprSchema 已删除，filters 解析改用
     core/rules/expr_host.RulesExprSchema.Base，见判断记录 7 与 ChainDefValidationRule/
     TargetHost 里 FilterSchema 字段注释）
    ChainDefValidationRule.cs    source 已注册 / filters 可解析 / fallback 无环 三项校验
    TargetHost.cs                ITargetHost 默认实现 + TargetingOptions
  schema/
    README.md                    字段说明与判断记录
  tests/
    TargetHostTests.cs
    TargetStrategyRegistryTests.cs
    ChainDefValidationRuleTests.cs
    T_N3_8_TargetOverflowPolicyTests.cs   overflow_policy 三态、新旧 Resolve 签名投影关系
```

T-N3-8（ADR-0031 决策 6、拍板 7）补充：`TargetOverflowPolicy` 枚举与 `ResolveWithCoefficients` 的
返回值类型 `TargetResolution` 定义在 `core/rules/common/contracts/TargetResolution.cs`（不是本模块
自己的 `contracts/`）——`ITargetHost.ResolveWithCoefficients` 是 `Core.Rules.Common` 命名空间下的
契约成员，本模块（`Core.Rules.Targeting`）依赖 `Core.Rules.Common`（见本文档顶部依赖清单），若把
这两个类型放在 `targeting/contracts/` 会导致 `common` 反向依赖 `targeting`，与既有分层方向冲突
（"谁实现、谁调用"矩阵：`ITargetHost` 由本模块实现，但契约签名/返回类型必须与接口本身同层）。

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

7. **契约缺口已由集成任务补齐（阶段 3 整理更新）**：`ExprParser.Parse` 强制要求一个
   `IExprSchema` 才能判定"点分标识符是宿主引用还是 Id 字面量"（ADR-0015），本模块最初自带一个
   临时的 `TargetFilterExprSchema`——只要 `group` 是 `self` 或 `target` 就一律判定为"合法引用"，
   不关心具体 key（当时的取舍：`ExprEvaluator` 求值只看 `IExprHost.Query` 的实际返回值，不读取
   `IExprSchema` 登记的 `ReturnKind`，所以"宽松登记"当时不会放过真正的类型错误，只是把"分类判定"
   交给运行期）。集成任务（`core/rules/expr_host`）落地 `RulesExprSchema` 后，该临时类型已删除，
   `ChainDefValidationRule`/`TargetHost` 的 `FilterSchema` 改用
   `core/rules/expr_host.RulesExprSchema.Base`——其 `self`/`target` 分组精确登记的 key 集合
   （`hp`/`hp_pct`/`faction`/`has_tag`/... 全部 16 项）覆盖本模块 `filters` 字段实际用到的引用，
   且严格模式（ADR-0015）下未登记的点分标识符会被正确判定为 Id 字面量而不是"签名未知的引用"，
   比原临时类型更准确。

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
    （内容提交时即可拦下）；`TargetHost.ResolveChainWithCoefficients`（T-N3-8 前名为
    `ResolveChain`，见判断记录 13）额外维护 `TargetingOptions.MaxFallbackDepth`（默认 8）独立
    防御——即使某个 `IDataRegistryView` 实现绕过了校验（如测试直接构造未跑校验规则的数据），
    运行期也不会无限递归。

12. **排序键 tie-break 统一按 Id 升序**：`distance`/`hp_pct`/`threat`/`level` 四个排序键同值时都
    退化为按 Id 升序，保证"同输入两次结果一致"这一确定性要求（见落地方案通篇的确定性拍板）。

13. **T-N3-8：`overflow_policy` 三态与 `ResolveWithCoefficients`，旧 `Resolve` 的"截断"并入统一
    分配系数管线**。目标形状新增 `max_targets`（既有字段）+ `overflow_policy`
    （`truncate`（默认）｜`split`｜`cap`，见 `schema/README.md`），`ITargetHost` 新增
    `ResolveWithCoefficients` 默认接口成员，返回 `TargetResolution`（`core/rules/common/contracts/
    TargetResolution.cs`：候选目标 + 各自分配系数 + 生效策略 + cap）。
    - **系数定义：设计层裁定（2026-09-15）：采纳**：ADR-0031 决策 6、06 第 3.7 节修订段原文只给出三个
      策略名字与默认值，未展开到"每个目标分配系数"这一精确公式。按字面含义裁定（见
      `TargetOverflowPolicy` 各成员 XML 文档）：`truncate` 与本字段引入之前的既有截断
      行为逐一对应（按既有排序取前 `max_targets` 个，多出的候选不命中，系数恒 1）；`split`
      （平摊）候选全部命中，总量守恒为"`max_targets` 个目标的满额值"，系数 = `max_targets` /
      命中数；`cap`（总量封顶）候选全部命中，总量硬封顶为"单个目标的满额值"，系数 = 1 / 命中数。
      三者均满足"未超出 cap（或 cap=0 不限）时策略不参与，系数恒为 1"。
    - **旧签名 `Resolve(Id, Id)`/`Resolve(Id, Id, Id?)` 保留、行为不变**（硬性规则）：`TargetHost`
      内部原 `ResolveChain`（"来源收集 → 过滤 → 排序 → 截断 → 空则回退"）改名/重构为
      `ResolveChainWithCoefficients`，返回 `TargetResolution`；"截断"不再是独立步骤，而是
      `ApplyOverflowPolicy` 内 `Truncate` 分支的其中一种结果——三条公开入口（`Resolve`/
      `ResolveWithCoefficients`/`ResolveAtPoint`）共享同一条管线，只是各自对结果做不同投影
      （`Resolve`/`ResolveAtPoint` 只取目标 Id 列表，丢弃系数）。`Truncate`（缺省，未声明
      `overflow_policy` 的既有链恒是这一策略）下投影结果与本字段引入之前的 `ApplyMaxTargets`
      逐字节一致，保证旧数据/旧调用方零行为变化（见 `T_N3_8_TargetOverflowPolicyTests.
      OldResolveSignature_DefaultTruncatePolicy_MatchesCoefficientProjection`）。`split`/`cap`
      策略下旧签名会返回全部候选（不做数量截断，因为这两种策略本身不丢弃候选，只稀释系数）——
      调用方需要系数时应改用 `ResolveWithCoefficients`。
    - **消费方**：`Core.Rules.Skill.CastPipeline` 步骤 6 链自行收集目标时改调
      `ResolveWithCoefficients`（显式目标路径经 `FilterExplicitTargets` 不受影响，系数恒 1），
      系数通过 `EffectContext.TargetCoefficient`（新增字段 + 第 18 参构造重载，ABI 只新增）一路
      带到 `EffectDispatcher.ApplyDamageOrHeal` 缩放群体效果值（`value × coefficient`）；地面坐标
      施法（`CastSkillAtGround`）与 `TriggerCast` 触发链两条入口本任务未接入系数（超出 T-N3-8
      范围，恒系数 1，与改动前行为一致），留给后续任务按需扩展。

14. **T-M14（测试覆盖第四批，2026-10-01）：`TargetChainDef` 的 `max_targets` 超 32 位范围改报
    `DataFieldException`**——复现：`max_targets: 4294967296` 旧实现 `checked((int)…)` 抛出裸
    `OverflowException`，既与同构造函数其它字段（负数、`overflow_policy`、`shape.kind`）的
    `DataFieldException` 不一致，又丢失表名/记录键/字段名，内容作者无法定位。改法：强转前判断
    `> int.MaxValue`，抛带 `max_targets` 字段路径的 `DataFieldException`；负数与缺省行为不变。见
    `TargetChainDefErrorPathTests`。

## 不负责什么

- 不提供 `IExprHostFactory` 的默认实现——集成任务职责（见 common/README.md"谁实现、谁调用"）。
- 不实现施法管线（`skill`）、结算管线/仇恨表写入（`combat`）、Rotation/行为外壳状态机（`ai`）——
  本模块只回答"给一条链 id 和施法者 id，解析出哪些单位"，不关心解析结果被拿去做什么。
- 不做具体游戏内容的目标选择口味决策——"选最近的敌人"还是"选血量最低的友方"完全是
  `target.chain_def` 数据决定的，本模块只提供六个内置来源与一套注入机制。

## 判断记录（`TryGetChainShape` 与 `ResolveAtPose`，2026-10-02，手感落地 S3b）

- `ITargetHost.TryGetChainShape(chainId, out Shape template)`：直接读链的 `shape` 字段（形状模板：`Origin` 为零、方向/旋转为 0 的未锚定形态），链没有声明 `shape` 返回 false。时间线据此决定命中路径
  （有形状走空间命中，没有保持 instant）并估算扫掠采样步长。默认接口成员恒返回 false，本类显式覆盖。
- `ITargetHost.ResolveAtPose(chainId, casterId, origin, facing, currentTarget)`：与 `ResolveWithCoefficients` 复用同一条解析管线（来源 → 过滤 → 排序 → 截断 → 系数 → 空则回退），只把形状锚点与排序距离基准换成给定位姿
  （`ResolveAtPoint` 的朝向固定为 0，本方法朝向可指定）。逐 tick 多次采样，**不发布** `targeting.resolved`。默认接口成员退化为 `ResolveWithCoefficients`（忽略给定位姿），本类显式覆盖。
- 复现/不变量：`tests/TargetHostResolveAtPoseTests.cs`（模板形态、位姿锚定与施法者自身位姿无关、矩形随朝向旋转、与 `ResolveWithCoefficients` 在自身位姿处一致、不发布事件）。

## 判断记录（命中形状高度窗口与三维距离，2026-10-02，M3-E1，[手感设计/06](../../../architecture/手感设计/06_手感实验室与验收.md) 第 10 节勘误 9）

1. **数据**：四种形状（circle/cone/line/rect）都可选 `height`（正数、有限，世界单位）；`TargetChainDef.ShapeHeight` 为空表示竖直方向不设限（无限高的柱体）。非正数/非数值抛 `DataFieldException`（字段 `shape.height`），schema 同步声明范围。
2. **两个选项，缺省都关**：`TargetingOptions.VerticalHit` 打开后，链声明了高度才过滤——候选脚下高度与锚点高度之差的绝对值 ≤ `height` 才保留（边缘含）；过滤发生在来源收集之后、过滤器之前，空间查询本身仍是平面的。`TargetingOptions.SpatialDistance` 打开后 `sort_by.distance` 与 `nearest_in_shape` 的"最近"按三维欧氏距离（`TargetContext.DistanceTo`）。两者都关时路径与改动之前逐位一致（不读高度）。
3. **锚点高度**：普通解析取施法者当前脚下高度；`ResolveAtPoint`（地面坐标施法）取地面 0，不取施法者高度。`ResolveAtPose` 同普通解析（施法者高度）。
4. **读口**：`IUnitAccess.GetHeightOffset`（默认接口成员，见 unit README）；既有测试假实现不必改。
5. **设计决定（M4 清扫，取代原"已知局限"两项）**：①`GridSnap` 吸附只按平面格子中心判定、不看高度——它是格子（二维网格）语义的吸附，竖直范围不是格子的属性；需要竖直限制的链用形状的 `height`/`height_offset`。②没有 `shape` 的链没有高度窗口——高度是形状的属性（`shape.height`），没有空间查询的链（自身、显式目标、全体友方等）不存在"锚点周围的窗口"，对它们套高度过滤没有意义。测试：`tests/TargetHostVerticalTests.cs`（窗口复现与施法者高度跟随、`ResolveAtPoint` 锚点、缺省关闭与链未声明高度的不变量、三维距离最近与排序、`shape.height` 解析与非法值）。

## 判断记录（形状竖直偏移，2026-10-03，M4-V，ADR-0130 追加决定）

1. **数据**：四种形状（circle/cone/line/rect）都可选 `height_offset`（有限数，可为负，世界单位；`TargetChainDef.ShapeHeightOffset`，缺省 0）；窗口中心 = 锚点高度 + 偏移，容差仍是 `height`，所以 `height_offset: 2`、`height: 1` 的窗口覆盖锚点之上高度差 1..3 的候选。非数值抛 `DataFieldException`，schema 同步声明。
2. **缺省不变**：偏移为 0 时路径与改动前逐位一致；只在 `VerticalHit` 打开且链声明了 `height` 时参与。
3. **取舍**：偏移同样作用于"施法者自己在候选里"的情形（窗口以施法者脚下为锚，偏移后施法者自己不一定在窗口内）。测试：`tests/TargetHostHeightOffsetTests.cs`（窗口上下偏移、负偏移、缺省 0 不变、与 `ResolveAtPoint` 锚点组合、解析与非法值）。

## 判断记录（目标命中半径，2026-10-03，M4 清扫，手感设计/03 第 2.4 节）

1. **选项**：`TargetingOptions.TargetRadius`（`Func<Id, double>?`，单位命中半径，世界单位，≤ 0 视为点）与 `MaxTargetRadius`（广相位上界）。缺省 null/0 时形状查询只按目标中心判定，路径与改动前逐位一致。
2. **口径**：`nearest_in_shape`/`all_in_shape` 在"中心落在形状内"的原始结果之后，追加"形状到目标中心的最近距离（`ShapeGeometry.ClosestPoint`）不超过该目标半径"的单位：先用外扩 `MaxTargetRadius` 的形状（`Shape.Expand`）取宁多勿少的候选，再按各自半径精确重判；追加项按 Id 序排在原始结果之后（确定性）；施法者自己不经半径追加。格子吸附（离散步 `grid_snap`）路径不参与（格子中心采样本来就以格为单位）。时间线空间命中的 marker 与 continuous 逐 tick 采样都经同一条形状查询。
3. **取舍**：半径来源由游戏供给（按体型数据，或把手感档案的 `unit_body_radius` 换算成世界单位），框架不替游戏选；`MaxTargetRadius` 必须不小于实际最大半径，否则半径更大的单位在广相位被漏掉（文档约束，测试里有反例）。接触点口径不变：marker/instant 取目标登记位置，continuous 取形状上离目标中心最近的点（命中时它必落在目标身体内）。测试：`tests/TargetHostBodyRadiusTests.cs`。

## 判断记录（受击半径的动态上界提供者，2026-10-04，M5-S2a，ADR-0144，[手感设计/03](../../../architecture/手感设计/03_攻击受击与命中.md) 第 2.2 节）

1. **`TargetingOptions.MaxTargetRadiusProvider`**（`Func<double>?`）：广相位上界的动态来源，每次解析时重新读取，优先于静态 `MaxTargetRadius`。理由：受击半径来自手感档案（`unit_body_radius × hurt_radius_scale`），换装与光环会改变它，装配时算一次的静态上界会漏判。只给提供者、不给静态上界也启用目标半径；提供者返回 0 等同未启用，行为与此前逐位一致。
2. **`ITargetHost.TargetHitRadius(Id)`**（默认接口成员，缺省 0）：`TargetHost` 覆盖为 `TargetRadius` 的当前值（选项未启用时 0），供命中接触几何（skill README M5-S2a 第 1 条）读取。
3. **复现与不变量**：`tests/TargetHostBodyRadiusTests.cs`（`MaxTargetRadiusProvider_*`：上界升高后同一宿主立刻命中；提供者为 0 与不配置逐位一致；`TargetHitRadius_*`）。
4. **已知限制**：打开后每次解析都由生产提供者对全部单位取一遍受击半径，单位很多时有成本（装配选项缺省关闭）；提供者的结果不缓存。
