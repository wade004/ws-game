# L4 玩法层 · progression_bridge（经验发放的两个玩法侧接线点）

职责：落地分阶段落地计划 T-N4-3——把 L1 `core/numbers/progression`（`IProgressionHost.GrantXp`）
接到两个具体玩法事件上：击杀经验（订阅 `unit.died`，含召唤物击杀归主人）与探索经验（订阅
`area.trigger_entered`，一次性标志经 `WorldState` 保证）。对应
[ADR-0033](../../../architecture/adr/0033-等级经验模块正文与当量来源.md) 决策 3、
[06_规则层_属性技能战斗AI.md](../../../architecture/06_规则层_属性技能战斗AI.md) 第 2.5 节。

依赖：L0（`event_bus`/`data_registry`/`expr`）、L1 `core/numbers/progression`
（`IProgressionHost`/`XpContext`/`ProgressionOptions`）、L2 `core/rules/common`（`IUnitAccess`/
`SourceKind`/`RulesEventKeys`/`UnitDiedEvent`）、L3 `core/carriers/common`（`ISummonHost`）、
`core/carriers/creature`（`ICreatureTemplateQuery`）、本层 `core/gameplay/area_trigger`
（`AreaTriggerEnteredEvent`/`AreaTriggerEventKeys`）、`core/gameplay/world_state`（`IWorldState`）。
经 `Core.Gameplay.csproj` 既有的 `Core.Carriers` 项目引用传递可见（该引用链一路到
`Core.Numbers`），本模块不新增任何 `ProjectReference`。

## 目录

```
progression_bridge/
  README.md
  core/
    CreatureDeathXpListener.cs         订阅 unit.died，击杀经验（含召唤归属）
    AreaTriggerDiscoveryXpListener.cs  订阅 area.trigger_entered，探索经验一次性发放
  tests/
    CreatureDeathXpListenerTests.cs
    AreaTriggerDiscoveryXpListenerTests.cs
```

## 设计要点与判断记录

1. **禁止在监听器里读掉落结果**（任务书硬性规则）：本模块两个监听器都不持有
   `core/gameplay/loot` 的任何引用，不订阅 `loot.rolled`，只共用"死亡单位的模板/等级"这类与掉落
   无关的公共只读信息（各自独立经 `IUnitAccess`/`ICreatureTemplateQuery` 查询，不是同一份共享
   状态、也不是从 `CreatureDeathLootListener` 转发）。

2. **接线顺序"先掉落后经验"**：`IEventBus` 按订阅顺序同步派发同一事件给多个订阅者；
   `core/gameplay/assembly.GameplayAssembly` 把 `CreatureDeathXpListener` 接在
   `CreatureDeathLootListener` 构造之后（同一处 `unit.died` 订阅点），因此掉落结算先跑、经验结算
   后跑。两者之间没有任何数据依赖——纯粹是装配顺序的约定，即便调换顺序也不影响各自的计算结果，
   只是"同一个 tick 内谁先收到事件"这件事本身对外可观察（如两者各自可能发出的事件在事件队列里的
   相对顺序）。

3. **回放基线核查结论：零改动**。`core/gameplay/tests/Replay/ReplayWorldBuilder.cs`
   （`BuildCommon`/`BuildContinuousWorld`/`BuildDiscreteWorldWithScheduler`）只装配
   `Core.Rules.Assembly.RulesAssembly`（L2），从未构造 `core/gameplay/assembly.GameplayAssembly`
   （L4）——本模块两个监听器只在 `GameplayAssembly` 构造期接线，Replay 测试的世界里根本不存在
   这两个监听器的实例，`unit.died`/`area.trigger_entered` 即便在回放脚本里触发也不会被本模块
   处理。这与本任务派发书"此前多次确认回放不触达 progression"的提示一致，`--filter
   "FullyQualifiedName~Replay"` 全绿且 `replay_baseline.json` 零改动（见"T-N4-3 门禁记录"）。

4. **未登记的 `prog.xp_source` 来源 id 不阻断玩法流程**：某个游戏/测试夹具尚未配置
   `ProgressionOptions.KillXpSourceId`/`DiscoveryXpSourceId` 指向的 `prog.xp_source` 记录时，
   经验发放退化为"不发放"，不应该让 `unit.died`/`area.trigger_entered` 的其余处理（掉落、任务
   判定、场景切换……）跟着崩溃。同 `core/gameplay/loot.CreatureDeathLootListener.OnUnitDied` 对
   未登记模板 id 的处理惯例。**T-N4-4 附带任务（设计层裁定）机制变更**：T-N4-3 落地时两个监听器
   调用 `IProgressionHost.GrantXp` 都用 `try { … } catch (ArgumentException) { }` 包裹——本任务
   起改为先调用新增默认接口成员 `IProgressionHost.HasXpSource(sourceId)` 显式查询，查到才发，
   去掉 try/catch：来源未登记是正常场景，不应该依赖异常控制流表达，见该接口成员判断记录。
   行为对外不变（未登记仍是"静默跳过、不阻断"），只是不再依赖异常。

5. **来源 id 承接点：`ProgressionOptions.KillXpSourceId`/`DiscoveryXpSourceId`**（契约疑点上报，
   见该两个字段自身判断记录）：06 第 2.5 节/ADR-0033 均未给出"三种来源各用哪一条固定
   `prog.xp_source` 记录 id"的字面结论；三种来源的当量公式本身只依赖调用时传入的
   `XpContext`，与"来源 id 具体叫什么"无关（由调用方在 `GrantXp` 显式指定）——本模块的两个默认
   监听器需要一个"没有显式配置时用哪条 id"的兜底约定，因此在 `ProgressionOptions` 上新增这两个
   可空字段，未配置时分别退到 `CreatureDeathXpListener.DefaultKillXpSourceId`
   （`prog.xp_source.kill`）/`AreaTriggerDiscoveryXpListener.DefaultDiscoveryXpSourceId`
   （`prog.xp_source.discovery`）两个约定 id。`core/gameplay/assembly.GameplayAssembly` 当前接线
   两个监听器时未显式传入 `ProgressionOptions`（等价于用默认约定 id）——是否需要把某个真正配置好
   的 `ProgressionOptions` 实例统一接进 `RulesAssembly.Progression`/两个监听器的构造点，留待未来
   任务处理（不在本任务范围）。

6. **探索经验"区域"与"区域等级"的承接方式：`AreaDiscoveryLevelResolver` 委托，不改
   `area_trigger` schema**（契约疑点上报，见该委托类型注释）：05 第 7 节把 `quest_explore` 定死为
   "无需额外字段"，本任务不擅自改这条结论（架构文档改结论须先出 ADR，不在本任务允许范围）。本类
   把 `AreaTriggerEnteredEvent.TriggerId` 当作"区域 id"，"这个触发器是不是探索奖励区域、区域等级
   是多少"由调用方经 `AreaDiscoveryLevelResolver` 委托按需提供（惯例同
   `CreatureDeathLootListener` 的 `lootMultiplierProvider`）——框架本身不预设任何具体区域列表
   （CLAUDE.md"技术无关、游戏无关，面向后续所有游戏"）。`core/gameplay/assembly.GameplayAssembly`
   当前不注入具体委托（传 `null`，等价于"没有任何区域配置为探索奖励"，零成本退化，不发放、不写
   任何标志）；某个具体游戏要启用探索经验时，在自己的组合根里提供一个真正的委托即可。

7. **一次性标志键 = `world.<前缀>.<triggerId>`**：`prog.xp_source.once_key`
   （ADR-0033 决策 3）登记的是前缀本身，不是完整标志键——多个区域共用同一条全局 `discovery` 来源
   id（见判断记录 5），必须按触发器 id 再分叉，否则任意一个区域首次进入就会把全部区域的标志一并
   点亮。前缀优先级：该来源记录自身的 `once_key` 字段（非空时优先）＞ `ProgressionOptions.
   DefaultOnceKeyPrefix`（缺省 `"prog.explore"`）。`IWorldState.Set`/`Has` 要求 `flagKey` 必须以
   `"world."` 开头，前缀本身未带这个命名空间头时由本类补上，内容作者不需要在 `once_key` 里重复写
   `"world."`。

8. **不做召唤物→主人的探索经验归属**：ADR-0033 决策 3 只字面给出"召唤物击杀归主人"，未提及探索
   场景；`AreaTriggerDiscoveryXpListener` 只服务玩家单位本人直接触发 `AreaTriggerEnteredEvent` 的
   场景（`GetSourceKind(evt.UnitId) != Player` 直接跳过），不擅自把这条决策类比扩展到探索——按
   最小范围落地，真正需要时留待新任务/新 ADR 决定。

9. **一次性标志"处理过一次"与"是否真的发了经验"分离**：`GrantXp` 若因来源 id 未登记而抛
   `ArgumentException`（被本类捕获，见判断记录 4），标志仍然写入——语义上"这次进入已经处理过"比
   "这次进入到底发出了多少经验"更贴近"一次性"这个约定本身（同 `GetXpToNext` 满级归零"不发事件也
   算处理过一次"同一惯例），避免来源 id 配置补上之后同一个区域被"追发"一次。

10. **T-N4-4：`IProgressionHost.HasXpSource` 默认实现返回 `true`（不是 `false`）**：ABI 门禁
    "公开 API 只能新增"——接口已发布，本成员新增之前调用方对任何 `IProgressionHost` 实现都无
    条件尝试调用 `GrantXp`；默认值 `true` 保持这一既有行为对未覆盖本方法的旧实现方（测试假实现、
    未来第三方实现）透明，只有显式覆盖本方法的 `ProgressionHost` 才获得"真正按注册表判断"的精确
    能力，详见该接口成员判断记录。

11. **消费方反馈第 6 条根治（2026-09-20，fix/silent-degradation-diagnostics）：新增
    `IProgressionBridgeDiagnostics` 契约，两个监听器共用**：本模块此前（T-N4-3/T-N4-4）从未持有
    任何诊断契约实例——判断记录 4/9 描述的"来源未登记 → 静默跳过"这一退化路径因此完全不可观察，
    消费方反馈现象是"杀怪一直 0 经验且无任何线索"。复核结论：本模块的"数据漏配（`prog.xp_source`
    缺该来源）→ 持续性配置缺失告警"完全符合 [ADR-0042](../../../architecture/adr/0042-诊断契约统一转发到宿主控制台.md)
    的适用场景（区别于 ADR-0046 那次"离散事件被 hub 文本去重吞掉"的不适用场景），照该 ADR 的接入
    方式办理，不需要另出 ADR。落地：新增 `IProgressionBridgeDiagnostics`/默认实现
    `InMemoryProgressionBridgeDiagnostics`（惯例同 `core/gameplay/common` 的
    `IRewardDiagnostics`），两个监听器各自新增一个只带 `diagnostics` 追加参数、不带默认值的构造
    重载（ABI 门禁"只新增"——既有构造函数已发布，直接加参数是破坏性变更，惯例同
    `core/rules/combat.Resolver` 十四→十六参重载），未提供来源 id 已登记时不产生任何诊断消息
    （行为不变，只是"跳过"这件事从此可观察）。`CreatureDeathXpListener`/`AreaTriggerDiscoveryXpListener`
    两者共用同一份诊断实例（由 `GameplayAssembly` 构造并转发，见下方"接入"一节），不是各自独立
    一份——两个监听器逻辑上同属一个模块，合并成一个诊断来源更贴近"这是 progression_bridge 模块
    的问题"这一定位粒度，也不需要在 `DiagnosticsHubComposition` 里注册两条几乎同名的来源。
    **`AreaTriggerDiscoveryXpListener` 同构确认**：判断记录 9 描述的分支（来源未登记时跳过
    `GrantXp` 但仍写一次性标志）与 `CreatureDeathXpListener` 判断记录 4 是同一模式（数据漏配、
    静默返回"不发放"这个合法结果），一并修复，未发现语义差异需要区别对待。

## 接入 `core/gameplay/assembly.GameplayAssembly`

- `CreatureDeathXpListener` 接在 `CreatureDeathLootListener` 构造之后（同一个"6) LootHost（+
  CreatureDeathLootListener）"步骤尾部新增一行），依赖 `Carriers.Rules.Progression`/
  `Carriers.Units`/`Carriers.Creatures`/`Carriers.Summons`（均已在此之前构造完成）。
- `AreaTriggerDiscoveryXpListener` 接在 `AreaTriggerHost` 构造之后（"15) AreaTriggerHost"步骤尾部
  新增一行），依赖 `registry`/`WorldState`/`bus`/`Carriers.Rules.Progression`/`Carriers.Units`；
  `AreaDiscoveryLevelResolver` 当前传 `null`（见判断记录 6）。
- 两处接线均只新增行，不改动既有行（本任务与并行的 T-N4-7 共同修改
  `core/gameplay/assembly/GameplayAssembly.cs`，见分阶段落地计划任务派发说明）。
- **T-N4-4 变更**：两处接线新增传入 `options: resolvedProgressionOptions`（此前未显式传入，各自
  退到内部 `new ProgressionOptions()` 默认值）——`GameplayAssembly` 从本任务起持有一份共享的
  `ProgressionOptions` 实例（同时承载 `ExtraXpMultiplierProvider`/`QuestXpSourceId`），两个监听器
  与 `RewardDispatcher` 共用同一份配置，不再各自独立退到互不相干的默认实例，见
  `core/numbers/progression/README.md`"T-N4-4"一节判断记录 5。
- **消费方反馈第 6 条根治（见判断记录 11）**：`GameplayAssembly` 在构造第一个监听器
  （`CreatureDeathXpListener`）之前新建一份共享的 `InMemoryProgressionBridgeDiagnostics`，赋给
  新增只读属性 `ProgressionBridgeDiagnostics`；两处接线均改传这份共享实例（`diagnostics:` 具名
  参数），`adapters/unity` 侧 `DiagnosticsHubComposition.RegisterCoreSources` 登记为
  "Core.Gameplay.ProgressionBridge" 一个来源。此前两处均 `_ = new ...(...)` 弃元、从未对外暴露
  监听器实例本身——本次不改这一点（其余任何模块都不需要引用监听器对象），只转发共享的诊断出口。

## 2026-09-16 深度复审 D-M1 判断记录：`ResolveCreditUnit` 改为转发共享辅助

`CreatureDeathXpListener.ResolveCreditUnit`（私有方法，ADR-0033 决策 3"召唤物击杀归主人"）内部
逻辑抽到了 `core/gameplay/common/core/SummonCreditResolver.ResolveCreditUnit`（见
`core/gameplay/common/README.md` 同名小节）——`Core.Gameplay.Loot.CreatureDeathLootListener`
的 `OnKill` 货币入账分支复审发现完全没有做这一步归属解析（复审报告 D-M1），修复时让两处共用
同一份实现，避免各自维护一份容易漂移的同构逻辑。本方法名称/签名不变，只是内部改成一行转发，
不影响任何既有调用点，`CreatureDeathXpListener` 本身行为逐位不变。
