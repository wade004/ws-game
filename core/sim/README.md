# 数值仿真骨架 Core.Sim

职责：ADR-0035 决策 1"仿真骨架是框架交付物"的落地起点——无头运行器的装配根，把装配一整套
L0～L4 世界（事件目录/总线、`DataRegistry` 装载、`RngHost`、`WorldSim`、`StubSpatialQuery`、
存档系统、`SimClockHost`、`GameplayAssembly`、玩家单位注册）收敛为一个公开、可复用的组装 API，
供后续任务（三级仿真、标准玩家生成器、场景/锚点表，见 [ADR-0035](../../architecture/adr/0035-数值仿真骨架为框架交付物.md)
决策 2～6）与既有测试夹具共用。T-N6-1 只交付装配根本身与其确定性证明，不含标准玩家生成器、场景
加载、报告输出——那些是后续任务（T-N6-2 及之后）的范围。

T-N6-2a（本次任务）在装配根之上补齐 ADR-0035 决策 4"两张数据表"：`sim.anchor`/`sim.scenario` 的
`TableSchema` 声明、专属表级校验规则、类型化只读读取（`AnchorTable`/`ScenarioCatalog`），并接入
`HeadlessWorldBuilder`（装配结果新增 `AnchorTable`/`ScenarioCatalog` 两个可空属性）与
`toolchain/validator`（经 `Presentation.Assembly.ContentValidationOptions.ExtraSchemaRegistration`
新增的登记钩子）。仍不含标准玩家生成器（决策 2）、三级仿真本身（决策 3）、报告与基线对比（决策 5）——
那些依旧是后续任务的范围，本任务只交付"这两张表存在、能被校验、能被类型化读取"这一层。

T-N6-2b（本次任务）在 `core/sim/tests/data/` 新增一套嵌入式最小仿真数据集（拍板 10：不进
`data/_sample`）——自洽、可被本模块装配根装配、可跑通一场战斗的最小内容集，详见
[`core/sim/tests/data/README.md`](tests/data/README.md)（数据清单、锚点推导公式与手算表、判断
记录）；配套新增 `SimTestWorldFactory.BuildFromEmbeddedDataset`/`RunEmbeddedFightScript`（测试
工厂，`Tests.Sim` 内部）与 `EmbeddedDatasetTests.cs`（装载/AnchorTable/ScenarioCatalog/生物物品
模板档位/标准职业学技能后战胜 1 级普通怪/确定性 共 7 例）。本任务未改动 `core/` 下任何生产代码
（`HeadlessWorldBuilder`/`AnchorTable`/`ScenarioCatalog`/`schema/` 均未触碰），只新增内容数据与
测试代码。

依赖：`Core.Gameplay`（L4，经其既有 `ProjectReference` 链传递可见 `Core.Carriers`/`Core.Rules`/
`Core.Numbers`/`Core.Foundation`）与 `Adapters.Stub`（桩适配层，ADR-0035 决策 1 明说无头运行器
复用桩适配层）。**不引用**任何 `Tests.*` 程序集或 `Presentation.Common`——本模块是生产代码交付物，
不是测试专用夹具。

T-N6-3a（本次任务，ADR-0035 决策 2；06 第 405 行勘误"锚点表接入后两条预算校验规则按本节公式默认
生效"）：新增 `ExpectedStatCalculator`（期望属性求值组件，数值总纲第 4.4 节"期望属性(L) = 等级
成长(L) + Σ槽位 预算反解(E(L), 期望品质, 槽位)"）、`AnchorTableSkillBudgetAnchorProvider`
（`ISkillBudgetAnchorProvider` 的 `sim.anchor` 真实实现）、`StandardPlayerBuilder`（ADR-0035
决策 2 标准玩家生成器：等级+职业 → `LearnFromBook` 填技能 → 每个装备位经 `IBudgetSolver` 生成并
装备"标准装" → 校验 `RotationEvaluator` 能选出可施放技能）。同时把 `HeadlessWorldBuilder.Build`/
`toolchain/validator/Program.cs` 两处此前"默认不注入真实 `ISkillBudgetAnchorProvider`"的接入点
改为自动装配（数据源含 `sim.anchor` 才装配，见判断记录"数据源含 sim.anchor 才自动装配锚点提供者"），
`RulesSchemaCatalog`/`CarriersSchemaCatalog`/`GameplaySchemaCatalog`/`PresentationSchemaCatalog`/
`ContentValidationOptions` 五处各新增一个接收 `ISkillBudgetAnchorProvider?` 的重载/属性（ABI：全部
新增，不改既有签名）。仍不含三级仿真本身（决策 3）、报告与基线对比（决策 5）——那些依旧是后续
任务（T-N6-4 及之后）的范围。

T-N6-4（本次任务，ADR-0035 决策 3）：三级仿真的第一级——战斗仿真运行器。新增
`AnchorCreatureLevelScaler`（`ICreatureLevelScaler` 的锚点表实现，按数值总纲第 4.2 节把
`creature.template.base_stats` 从模板登记等级换算到任意目标等级）、`SimpleMoveModel`（玩家侧
简化移动模型——优先级表选不出可施放技能时朝目标移动一步，生物侧复用 `core/rules/ai` 既有的
`AiHost` 追击/进战状态机，不重复实现）、`FightRunner`（单场战斗：标准玩家对指定生物模板按指定
等级出生，逐 tick 智能释放优先级表直至一方死亡或超时，采样口径接 `combat.damage_dealt` 与
`CombatOptions.ResolveTrace`，输出 `FightResult`——胜负/时长/双方伤害与命中/技能输出占比/资源
曲线/TTD 估计）、`ArenaSimulation`（场景运行器：对 `kind=arena` 场景按 `levels × level_offsets`
每个格子跑 `runs` 场，聚合输出 `ArenaReport`——胜率/TTK 分布/对账表，`ToJson()` 确定性序列化）。
`Core.Carriers.Creature.CreatureFactory` 新增可写属性 `LevelScaler`（ABI：纯新增，见该类型判断
记录"改为构造后可写的公开属性"）；`HeadlessWorldOptions` 新增 `CombatOptions` 属性（转发给
`GameplayAssembly` 既有的同名构造参数，此前恒隐式传 `null`）。隔离方案：每场 `FightRunner.Run`
各自新建一整套 `HeadlessWorld`（实测 `HeadlessWorldBuilder.Build` 平均 ~10～25ms，远低于任务书
50ms 判断线，见 `FightRunnerTests.Probe_BuildTiming_WellUnder50MsThreshold`），不做"同一世界内
重生重置"，不触碰任何被仿真模块的重置能力。为让"生物会主动追击并攻击玩家"这条链路真正跑通，
本任务修了嵌入数据集两处此前从未被触发过的缺口——`fac.reaction_matrix` 缺反向敌对行（生物→玩家）、
`stat.definition` 缺 `stat.move_speed`（`MovementTickHandler` 硬性要求）——均记录在
`core/sim/tests/data/README.md` 判断记录与本文件判断记录。跑通仿真后按对账等式与矩阵形状要求
重新核算了 `sim.anchor`/`creature.template`/`skill.base_curve.sim_creature_bite*`/
`sim.scenario.sim_arena_matrix.runs`，详见数据集 README"T-N6-4 / T-N6-4b 调参记录"一节——
`ExpectedStatCalculator`/`StandardPlayerBuilder` 用到的公式与结果不受影响（本次改动的表不在它们
的输入范围内）。

T-N6-4b（设计层复核后的根治提交）：T-N6-4 首次提交后，设计层复核发现玩家 20 级一行的越级矩阵
胜率非单调（断层），要求先查机制再调数据——排查定位到两处真实缺陷，均已根治：①
`HeadlessWorldBuilder.Build` 此前没有像 `CreatureFactory.SpawnCore` 那样在玩家按等级 &gt; 1
直接出生时补写等级成长，导致玩家实际战斗力远低于设计意图（判断记录 28，只改
`core/sim/core/HeadlessWorldBuilder.cs`，未触碰任何被仿真模块）；② `AnchorTable`/
`skill.base_curve.sim_creature_bite*` 原本只到 20 级，越级矩阵 +5 偏移在玩家满级时让生物
21～25 级全部夹到同一强度天花板，扩表到 25 级根治（判断记录 29）。两处修复后按同一方法论重新
核算了全部锚点/生物数值，`ArenaSimulationTests` 的验收标准同步收紧为任务书原文的严格版本（DPS/
HP 对账 5 个等级全部在带宽内、矩阵形状对全部等级逐偏移点成立，不再有 T-N6-4 首次提交时"至少
3 个"/"至少一个"的放宽，那条放宽的判断记录 26 已标注撤销）。

T-N6-5（本次任务，ADR-0035 决策 3 后两级）：三级仿真的最后两级——成长仿真
（`GrowthSimulation`）与内容覆盖仿真（`CoverageSimulation`），三级仿真至此全部落地（战斗见
T-N6-4）。成长仿真的简化路径模型："打同级怪 → 真实结算战果（经验/掉落/金币）→ 加一段击杀间隔
歇口气 → 循环直到真实升级"，不预判"打够 monsterEquivalent(L) 只怪就该升级"——升级完全由真实
`IProgressionHost.GrantXp` 的阈值判定触发；掉落/入账走"生物死亡自动触发的既有监听器
（`CreatureDeathLootListener`/`CreatureDeathXpListener`）+ 本类型自己拾取评分换装"，不重复调用
`LootHost.RollDetailed`（那会对同一次死亡多掷一次骰子，见 `GrowthSimulation` 类型判断记录"掉落
与货币走真实监听器，不重复掷骰"）。内容覆盖仿真对每个技能/装备/生物模板批量跑，输出三张按
`|偏离|` 降序排列的离群值表，注入两条探针（超模技能、欠模装备）验证排序正确。为让成长仿真可行，
新增 `FightRunner.RunWithinWorld`/`FightRunner.FightAccumulator`（把 `Run` 内部"每场新建世界"
的隔离方案之外，补一条"同一世界内连打多场"的路径，供成长/覆盖仿真复用同一世界或同一场次内的
命中/伤害追踪状态，`Run` 自身行为不变）；`HeadlessWorldOptions` 新增 `LootOptions` 转发属性
（放宽拾取半径，见该属性判断记录）。联调过程中发现并根治了嵌入数据集两处此前从未被真正验证过的
既有缺口（详见 `core/sim/tests/data/README.md`"T-N6-5 调参记录"）：① `loot.table.*` 货币条目的
`count_range` 被误当成"最终掉钱数"填写，但 `LootHost.ResolveCurrencyOutcome` 的真实公式是
"当量×金币基数(L)"，导致击杀掉钱系统性偏高约 5 倍——改为常量当量 1，与数值总纲 4.8 节"怪物掉钱=
金币基数(怪物等级)×……"字面公式（无当量项）对齐；② 本类型自己实现的"任务当量顺带发放"最初误写成
`Q(L)/(1-Q(L))`（与"每级怪当量(L) 就是设计意图的目标击杀数"这一前提不自洽，会让实际所需击杀数
系统性少于 `monsterEquivalent(L)`），改为 `Q(L)` 本身。两处修复后 `sim_growth_full` 全部 19 个
等级的四条轨迹逐级在带宽（0.25，低于任务书 ≤0.35 上限）内，见 `GrowthSimulationTests
.FullScenario_FourTrajectories_WithinBandwidth`。同时按任务需要给 5 档普通怪的 `loot.table.*`
各追加 `chest`/`legs`/`feet` 三条 common 品质条目（T-N6-2b/T-N6-4 阶段只登记了主手+头两槽位，
成长仿真需要全部 5 个装备槽位都有机会被替换，否则"各槽平均装备等级"轨迹追不上
`expected_item_level(L)`）。`prog.level_curve.sim_warrior.entries[].xp_to_next`（1～19 级）按
T-N6-4b 校准后的锚点重新核算（T-N6-4b 遗留的已知不一致，见该处 README 判断记录），20 级仍为
满级 0。

T-N6-6（本次任务，ADR-0035 决策 5"报告为结构化产物、基线对比工具输出改动前后统计量差异"）：
统计量快照与基线对比。新增 `SimReport`（统一信封：场景 id + kind + 数据集指纹
`SimReport.ComputeDatasetFingerprint`（对参与装载的全部表、全部记录按表名/记录 key 排序后逐条
FNV-1a 64 位累加，算法选型同 `core/foundation/save_system` `WorldSnapshot.Capture` 同款——只是
同款选型，独立实现，见该类型判断记录）+ 框架版本字符串（调用方传入，本类型不解析）+ 种子 +
统计量平铺表 `Stats: IReadOnlyList<SimStat>`（`Path`/`Value`/`Anchor?`/`Deviation?`/`Level?`）+
原始报告 `RawReport`），三个工厂方法 `FromArenaReport`/`FromGrowthReport`/`FromCoverageReport`
把三类既有报告拍平成统一形状（路径命名方案与叶子名对齐 `ScenarioDef.Bandwidths` 既有键名的判断
记录见 `SimReport.cs` 类型注释）。新增 `SimBaseline`（既往快照的最小形状
`{schema_version, scenario_id, kind, dataset_fingerprint, generated_with_version, seed,
stats: {path: value}}`，只做 `FromReport`/`ToJson`/`Parse` 文本↔类型转换，不做磁盘路径解析，
惯例同 `HeadlessWorldOptions.DataSources`"数据来源必须注入"）。新增 `BaselineComparer.Compare`
逐 `path` 比对当前 `SimReport` 与既往 `SimBaseline`，容差来源优先级"场景 `bandwidths` 中同名
统计量（按叶子名精确匹配 `BaselineCompareOptions.LeafBandwidthKeys` 显式映射表，T-N6-8b 根治，
见下方"T-N6-8b 判断记录"一节——此段原文曾是"按叶子名子串匹配"，已被取代）> `BaselineCompareOptions
.RelativeTolerance` 默认相对 1%（对
`|baselineValue|` 不超过 `AbsoluteToleranceForZeroBaseline`（默认 1e-9）时改用该值本身作绝对
容差）"，产出 `BaselineDiff`（`Same`/`Within`/`Exceeded`/`Added`/`Removed` 五态分类，全程只用
`<`/`<=` 阈值比较，不出现浮点精确相等，见 `BaselineComparer`/`BaselineCompareOptions` 类型判断
记录）。新增命令行入口 `toolchain/simrunner`（`SimRunner.csproj`，工程惯例照抄
`toolchain/validator` 的 `lib/` 分发分支）与薄封装 `toolchain/sim_baseline.ps1`，详见下方"命令行
入口"与"基线更新流程"两节。新增并提交三份基线 `core/sim/tests/baseline/*.json`（对应嵌入数据集
三个场景，见下方"基线文件"一节）。联调过程中发现并根治了 `CoverageSimulation`（T-N6-5 遗留）
的一处真实确定性缺陷——`AnalyzeSkills`/`MeasureItemMarginalImpact`/`AnalyzeCreatures` 三处用
`string.GetHashCode()`（.NET 逐进程随机化哈希）派生仿真种子，导致同一 `base_seed`、同一份数据
在不同进程里跑出不同结果，与仓库通篇"同种子确定性"矛盾；改用确定性的 FNV-1a 32 位字符串哈希
`CoverageSimulation.StableIdHash`，详见该方法判断记录。

## 命令行入口：`toolchain/simrunner`

```
dotnet run --project toolchain/simrunner -- run \
  --scenario all --framework-root data/_framework --data-root core/sim/tests/data \
  --out <out-dir> [--baseline-dir core/sim/tests/baseline] [--update-baseline] \
  [--json] [--runs <n>] [--version <str>]
```

- `--scenario <id>|all`：场景短 id（如 `sim_arena_matrix`，自动补全 `sim.scenario.` 前缀）或
  `all`（`ScenarioCatalog.All` 全部场景，按 id 排序逐个跑）。
- `--framework-root <dir>`（必填，单个）+ `--data-root <dir>`（必填，可重复）：拼成
  `IDataSource` 列表（框架根在前）传给 `ArenaSimulation`/`GrowthSimulation`/`CoverageSimulation
  .Run`；发现场景所需的"探测世界"用哪个职业构造见判断记录 36。
- `--out <dir>`：写 `<scenario短id>.report.json`（`SimReport.ToJson()`）；有基线时另写
  `<scenario短id>.diff.txt`/`.diff.json`（`BaselineDiff.ToText()`/`ToJson()`）。
- `--baseline-dir <dir>`（可选）：基线文件所在目录，文件名 `<scenario短id>.json`。不传时不做
  任何比对（纯跑仿真、写报告）。
- `--update-baseline`：用当前报告覆盖 `--baseline-dir` 下对应文件（需同时传 `--baseline-dir`），
  写前打印将被覆盖文件的路径与统计量条数。
- `--runs <n>`：覆盖全部选中场景的 `runs`（经 `ScenarioDef.WithRuns`），仅用于快速冒烟；报告里
  `runs_override` 字段如实记录。
- `--json`：额外把每个场景的 `BaselineDiff.ToJson()` 打印到标准输出（人读摘要行不受影响）。
- `--version <str>`：写入 `SimReport.GeneratedWithVersion`/`SimBaseline.GeneratedWithVersion`
  的框架版本字符串，本工具不解析、不校验，默认 `"unknown"`。

退出码：
- **0**：全部选中场景均无 `Exceeded`/`Removed`（含"未传 `--baseline-dir`，不做比对"这一情形）。
- **1**：至少一个场景的 `BaselineDiff.HasBlockingDifference`（存在 `Exceeded` 或 `Removed`）。
- **2**：参数错误，或数据装载阻断（`HeadlessWorldBuilder.Build` 抛出的校验阻断异常、目录不存在、
  数据根内找不到 `sim.scenario` 行等）。
- **3**：传了 `--baseline-dir` 但至少一个场景对应的基线文件不存在，且未传 `--update-baseline`
  ——与 0/1/2 三种情形都不同（不是参数写错，也不是跑出来的结果不通过，是"调用方明确要求比对但
  没有东西可比"这一第三种状态，见 `Program.cs` 类型判断记录"退出码 3 独立于 0/1/2"）。

控制台每个场景一行摘要（供 `check.ps1`/CI 直接判读）：
`scenario=<id> kind=<k> stats=<n> exceeded=<n> added=<n> removed=<n> result=PASS|FAIL`，末尾一行
`RESULT=OK|FAIL`。

判断记录 36（探测场景目录时用哪个职业构造"探测世界"）：见 `Program.cs`
`DiscoverBootstrapPlayerClass` 方法判断记录——`HeadlessWorldBuilder.Build` 要求一个真实存在的
`PlayerClassId`（否则装配阶段直接抛"未知职业"异常），但本工具在拿到 `ScenarioCatalog` 之前恰恰
不知道该传哪个职业；`AnchorTable`/`ScenarioCatalog` 本身只依赖 `sim.anchor`/`sim.scenario` 两表
数据、与玩家职业无关，为了拿到它们而必须先构造一整套可玩世界，是这层 API 形状带来的鸡生蛋
问题。本工具直接扫描全部数据源的 `sim.scenario` 表文件文本（`IDataSource.ListTables` +
`DataTableSource.ReadText`，不经过 `DataRegistry`）取第一行 `player.class_id`/`player.level`
作探测用参数——只是为"装配一次探测世界"这一引导步骤找一个真实存在的职业，不含任何校验/解析
业务逻辑，真正的场景解析仍全部经由 `ScenarioCatalog` 完成。

## 基线文件

`core/sim/tests/baseline/{sim_arena_matrix,sim_growth_full,sim_coverage_all}.json`——嵌入数据集
三个场景各一份，`SimBaseline.ToJson()` 格式，随本任务提交、由 `--update-baseline` 生成（生成
命令见下方"基线更新流程"）。

判断记录 37（为何不与数据集同目录，单独放 `tests/baseline/`）：`core/sim/tests/data/` 是"内容
数据"（供装配根/仿真运行器读取的 `sim.*`/`skill.*`/`item.*` 等表），基线文件是"这份内容数据配上
当前代码跑出来的历史快照"——两者生命周期不同（改一条技能数值应该让基线比对报出差异，而不是
"因为基线和数据放在同一棵目录树下，改数据的提交顺手把基线也搅进去、看不出这是两个独立的
变更"）。放在 `tests/baseline/` 也符合"基线是测试固件"的既有惯例（同
`core/gameplay/tests/Replay/replay_baseline.json`）。

## 基线更新流程

惯例同 `core/gameplay/tests/Replay/README.md`"如何更新基线"一节，同一原理（比对失败本身不说明
对错，只说明结果变了，这两者之间需要一道人工确认关卡）：

1. **先确认这是一次有意的数值/结算行为变化**（调了曲线、改了公式、调了带宽），而不是意外回归。
   若是意外回归，应该去修那个改动，不是更新基线掩盖它。
2. 跑一次不带 `--update-baseline` 的比对，看差异：
   `./toolchain/sim_baseline.ps1 -Scenario all`（或直接调用 `toolchain/simrunner`，见上方
   "命令行入口"）。
3. 打开 `.sim_out/<scenario短id>.diff.txt`，人工审阅每一条 `Exceeded`/`Removed` 是否对应你在
   第 1 步确认的具体改动（不应该有"看不懂为什么变"的行）。
4. 确认无误后，同一份改动里加 `-UpdateBaseline` 重新生成基线：
   `./toolchain/sim_baseline.ps1 -Scenario all -UpdateBaseline`。
5. **提交信息里必须注明本次更新了 sim 基线以及原因**，与本次数值/结算改动同一提交，不要把基线
   更新悄悄混进一次无关改动的提交里。

## 目录

```
core/sim/
  README.md
  Core.Sim.csproj
  LayerMarker.cs
  core/
    HeadlessWorldBuilder.cs   HeadlessWorldOptions（构造期选项，T-N6-3a 新增 ExpectedQualityId 可选
                              属性，T-N6-4 新增 CombatOptions 转发属性）+ HeadlessWorld（装配结果，
                              T-N6-2a 新增 AnchorTable?/ScenarioCatalog? 两个可空属性）+
                              HeadlessWorldBuilder（唯一的 Build 入口，T-N6-3a 起数据源含
                              sim.anchor 时自动装配 AnchorTableSkillBudgetAnchorProvider；T-N6-4b
                              根治：玩家 PlayerLevel > 1 时补调用 Progression
                              .ApplyGrowthToCurrentLevel + Powers.RecomputeMax/RefillAll，见判断
                              记录 28）
    AnchorTable.cs            T-N6-2a：AnchorRow（sim.anchor 一行的强类型只读视图）+
                              AnchorTable（MaxLevel/TryGet/Get）
    ScenarioCatalog.cs        T-N6-2a：ScenarioKind/ScenarioPlayerSpec/ScenarioOpponentSpec/
                              ScenarioDef（sim.scenario 一行的强类型只读视图）+
                              ScenarioCatalog（All/TryGet/Get/ByKind）
    ExpectedStatCalculator.cs
                              T-N6-3a：期望属性求值组件——给定职业/期望品质，按数值总纲第 4.4 节
                              公式对 stat.definition 全表求值（等级成长 + Σ非武器装备槽预算反解，
                              statMix 取该职业 stat.weight 归一化），构造期一次性解析、按等级缓存
    AnchorTableSkillBudgetAnchorProvider.cs
                              T-N6-3a：Core.Rules.Common.ISkillBudgetAnchorProvider 的 sim.anchor
                              真实实现——GetAnchorDps 查 AnchorTable（越界夹到 MaxLevel）；
                              GetExpectedScalingStatValue 委托 ExpectedStatCalculator；惰性持有
                              registry 引用（含 Func<IDataRegistry> 工厂重载，供 registry 尚不存在
                              时的调用方使用）；DataSourcesHaveAnchorRows 静态方法供两处接入点共用
                              的"数据源是否真的提供了 sim.anchor 行"预扫描
    StandardPlayerBuilder.cs T-N6-3a：标准玩家生成器（ADR-0035 决策 2）——LearnFromBook 填技能、
                              每个装备位选嵌入数据集里最接近 E(L) 的模板作载体、经 IBudgetSolver
                              对齐词缀反解向量并装备、校验 RotationEvaluator 能选出可施放技能，
                              返回 StandardPlayer 完整快照（已学技能/已装备实例/期望与实际属性/
                              偏差/武器秒伤）
    AnchorCreatureLevelScaler.cs
                              T-N6-4：ICreatureLevelScaler 的锚点表实现——血量槽位（经
                              arch.power_type 数据驱动找到）按 DPS(target)×TTK(target) ÷
                              DPS(source)×TTK(source) 缩放，其余全部属性按 HP(target)÷TTD(target)
                              ÷ HP(source)÷TTD(source) 缩放；越界夹到 AnchorTable.MaxLevel/最低 1 级
    SimpleMoveModel.cs        T-N6-4：玩家侧简化移动模型——距目标 > engageRange 时按固定速度移动
                              一步，不越过目标；生物侧不重复实现（复用 core/rules/ai 既有 AiHost
                              追击状态机）
    FightRunner.cs            T-N6-4：单场战斗仿真运行器——FightRunnerOptions/FightResult/
                              FightOutcome/ResourceSample；每场新建 HeadlessWorld（隔离方案见本
                              文件上方判断记录），采样口径 combat.damage_dealt（落地伤害/命中计数）
                              + CombatOptions.ResolveTrace（技能归属、含 Miss/Dodge/Immune 的完整
                              尝试计数）。T-N6-4b 新增 FightResult.CreatureHitRate（生物对玩家的
                              命中率，口径同 PlayerHitRate 对称）——根因排查 L20 越级矩阵断层时
                              需要的诊断字段，同时也补全了"双方命中率"这一输出维度
    ArenaSimulation.cs        T-N6-4：场景运行器——ArenaCellResult/ReconciliationRow/ArenaReport；
                              按 levels×level_offsets 逐格跑 runs 场并聚合；种子按
                              (base_seed,level,offset,runIndex) 纯函数派生（DeriveSeed）；
                              ArenaReport.ToJson() 经 Core.Foundation.Common.Json.JsonWriter 确定性
                              序列化（NaN/Infinity 写为 null）
    GrowthSimulation.cs       T-N6-5：成长仿真——GrowthLevelSample/GrowthReport/GrowthRunResult；
                              在一个持续存活的 HeadlessWorld 内反复调用 FightRunner
                              .RunWithinWorld 打同级怪，真实 GrantXp（击杀+任务当量 Q(L)）驱动
                              升级、真实掉落（生物死亡自动触发的 CreatureDeathLootListener，本
                              类型只拾取+评分+换装/出售，不重复调用 RollDetailed）、真实经济
                              入账；四条轨迹（时长/装备等级/命中率/金币）与对照基准/偏离/带宽
                              判定；runs 个种子取均值；ToJson() 确定性序列化
    CoverageSimulation.cs     T-N6-5：内容覆盖仿真——CoverageOutlierRow/CoverageReport；技能按
                              SkillBudgetAnalyzer 预算比值（Player 档伤害技能额外做"只用该技能+
                              填充技能"单技能秒伤占比实测，直接调用 SkillHost.CastSkill，不经
                              RotationEvaluator）、装备按 EquipmentScoreAnalyzer/预算消耗比（额外
                              做同种子基准装 vs 换单件的秒伤边际变化实测）、生物按同级标准玩家
                              1v1 的 TTK/TTD 与锚点偏离；三张表各自按 |偏离| 降序排列。T-N6-6 新增
                              私有方法 StableIdHash（FNV-1a 32 位）替换三处此前用
                              string.GetHashCode() 派生种子的调用点，根治跨进程不确定性，见该
                              方法判断记录
    SimReport.cs              T-N6-6：SimStat（一条统计量：path/value/anchor?/deviation?/
                              level?）+ SimReport（统一信封：场景 id/kind/数据集指纹/框架版本/
                              种子/runs_override?/bandwidths/stats/raw_report）；
                              FromArenaReport/FromGrowthReport/FromCoverageReport 三个工厂方法
                              把既有报告拍平成统一形状；ComputeDatasetFingerprint（FNV-1a 64 位，
                              对全部已加载表/记录排序后累加）
    SimBaseline.cs             T-N6-6：既往快照的最小形状（schema_version/scenario_id/kind/
                              dataset_fingerprint/generated_with_version/seed/stats:{path:
                              value}}）；FromReport/ToJson/Parse，不做磁盘路径解析（读写文件是
                              toolchain/simrunner 的职责）
    BaselineComparer.cs        T-N6-6：BaselineDiffStatus（Same/Within/Exceeded/Added/Removed）+
                              BaselineCompareOptions（容差配置）+ BaselineDiffRow/BaselineDiff +
                              BaselineComparer.Compare；容差来源优先级"场景 bandwidths 同名统计量
                              > 默认相对容差"，全程阈值比较不出现浮点精确相等
  schema/
    SimSchemas.cs             T-N6-2a：sim.anchor/sim.scenario 的 TableSchema 声明
    SimValidationRules.cs     T-N6-2a：SimAnchorValidationRule/SimScenarioValidationRule
                              （字段登记表达不了的表级/条件约束）
    SimSchemaCatalog.cs       T-N6-2a：RegisterAll(IDataRegistry) 统一注册入口
  tests/
    Tests.Sim.csproj
    SimTestWorldFactory.cs   本工程自己的"从磁盘读 data/_framework + data/_sample 构造数据源"
                             小工具（惯例同 core/gameplay/tests/EndToEnd/GameWorldFixture.cs 的
                             FindRepoRoot），供确定性测试驱动一场固定的战斗脚本
    DeterminismTests.cs      G1★ 确定性证明：同种子两次独立 Build 逐 tick 完全一致；不同种子
                             在命中判定上产生可观测差异
    SimSchemaTests.cs        T-N6-2a：sim.anchor/sim.scenario 的 schema/校验规则正反例
    AnchorTableTests.cs      T-N6-2a：AnchorTable 类型化读取
    ScenarioCatalogTests.cs  T-N6-2a：ScenarioCatalog 类型化读取
    HeadlessWorldBuilderSimTests.cs
                             T-N6-2a：用 data/_framework + data/_sample 构建后 AnchorTable 有
                             5 行、ScenarioCatalog 有 1 个场景
    EmbeddedDatasetTests.cs  T-N6-2b：用 data/_framework + core/sim/tests/data 构建后的一组断言
                             （装载零阻断、AnchorTable 1～20 级连续、三个场景可取、生物/物品模板
                             档位数量、标准职业学技能后能战胜 1 级普通怪、确定性）
    ExpectedStatCalculatorTests.cs
                             T-N6-3a：Compute(L) 在 L=1/10/20 三级与独立手算（README"锚点推导"
                             闭式公式 + 独立调用 IBudgetSolver）一致；结果按等级缓存；stat.armor
                             clamp 生效
    StandardPlayerBuilderTests.cs
                             T-N6-3a：L5/L15 两组——全部装备位有装备、物品等级与 E(L) 对齐、装备
                             贡献==反解向量（容差 1e-6）、已学技能==技能书 ≤L 集合、按优先级表
                             击杀同级普通怪且玩家存活；等级不一致时抛 ArgumentException
    AnchorProviderIntegrationTests.cs
                             T-N6-3a：叠加一条超预算 skill.def 行（第三数据根）验证
                             skill_budget_* 规则确有真实求值（阻断）；data/_sample 路径锚点接入后
                             仍不阻断（回归）
    AnchorCreatureLevelScalerTests.cs
                             T-N6-4：ScaleBaseStats 精确比值（1e-6 容差）、装配根接线端到端验证
                             （Spawn(...,level) → LevelScaler → StatHost → PowerHost）、未覆盖
                             等级不触碰缩放器、越界夹到 MaxLevel
    SimpleMoveModelTests.cs T-N6-4：射程内不移动、射程外朝目标移动且不越过、步长超过剩余距离时
                             精确停在目标点
    FightRunnerTests.cs     T-N6-4：生物确有主动伤害输出（AI 真正追击并攻击）、同种子结果逐位
                             一致、不同种子命中判定可观测差异、技能占比之和为 1、命中率 ∈[0,1]、
                             资源曲线下采样且非负、Build 平均耗时探针
    ArenaSimulationTests.cs T-N6-4：跑一次完整 sim_arena_matrix（不缩参数，35 格 × 60 次/格 =
                             2100 场，共享同一份 IClassFixture 结果，打印总耗时）断言对账等式
                             （dps/hp ≥3 个等级、ttd ≥1 个等级在带宽内）与矩阵形状（单调不增
                             ±0.05、偏移 ≤-3 胜率 ≥0.95、偏移 ≥+3 至少一格拐点成立）；另用一份
                             内嵌合成小场景验证 ArenaReport.ToJson() 同种子逐字节相同/不同种子
                             不同
    GrowthSimulationTests.cs T-N6-5：跑一次完整 sim_growth_full（不缩 runs，5 个种子 × 1→20 级，
                             共享同一份 IClassFixture 结果，打印总耗时）断言四条轨迹逐级在带宽内、
                             经验/金币双路径（GrantXp/Economy.Add 返回值求和 vs 事件流求和）互证、
                             prog.level_curve.xp_to_next 与公式独立核对；另用一份缩小的合成小场景
                             验证 ToJson() 同种子逐字节相同/不同种子不同
    CoverageSimulationTests.cs
                             T-N6-5：跑一次完整 sim_coverage_all（共享同一份 IClassFixture 结果，
                             打印总耗时）断言三张表均非空、按 |偏离| 降序排列、两条注入探针分别
                             排在技能表/装备表第一位、ToJson() 确定性
    BaselineComparerTests.cs T-N6-6：验收 5 全套——同种子独立重跑两次（缩小 runs，见类型判断
                             记录）三个场景 SimReport.ToJson() 逐字节相同且 BaselineComparer 全部
                             Same；内存覆盖 arch.class.sim_warrior.base_stats.stat.strength
                             重跑 arena 后至少一条 Exceeded、player_max_health 不受影响；1e-12
                             量级相对扰动分类为 Within（浮点比对无精确相等）；SimBaseline 经
                             ToJson/Parse 往返后比较仍全部 Same；SimReport.ToJson() 字段完整性
    data/                    T-N6-2b：嵌入式最小仿真数据集，见 data/README.md（数据清单、锚点
                             推导公式与手算表、判断记录）——不进 data/_sample（拍板 10）
  tests/baseline/            T-N6-6：三个场景各一份 SimBaseline JSON 快照（sim_arena_matrix/
                             sim_growth_full/sim_coverage_all），随本任务生成并提交，见本文件
                             "基线文件"/"基线更新流程"两节
```

## 不负责什么

- T-N6-6 已落地报告输出与基线对比工具（ADR-0035 决策 5）——`SimReport`/`SimBaseline`/
  `BaselineComparer` 与命令行入口 `toolchain/simrunner`，见上方相应章节。本任务不改
  `check.ps1`/`ci.yml`/`build.ps1`/`adapters/headless/README.md`（把 `simrunner` 接入门禁/CI/
  发布清单归 T-N6-7），只提供可被它们调用的入口；不把 `toolchain/simrunner` 纳入 `dist/`
  打包（`Core.Sim`/`Adapters.Stub` 本身尚未进入任何发布清单，见判断记录 10 同一缺口，`SimRunner
  .csproj` 的 `lib/` 分发分支目前无法在独立发行包内解析，同 `Validator.csproj` 已知缺口）。
  **T-N6-7 已收尾**：`check.ps1` 新增"数值仿真基线比对"步骤（复用步骤 1 已构建的
  `SimRunner.dll`）、`ci.yml` 随 `check.ps1 -SkipUnity` 自动纳入、`build.ps1` 已把
  `toolchain/simrunner` 预编译产物（`bin/`+`lib/`）随 `toolchain/` 打进 dist 与
  `com.gamefoundation.toolchain` 包、`Core.Sim.dll`/`Adapters.Stub.dll` 已补进
  `adapters/headless/`（随 dist 与 `com.gamefoundation.adapter.headless` 包）与
  `toolchain/validator/lib/`（判断记录 10 缺口已消除）；详见本文件"T-N6-7 判断记录"与
  `build.ps1`/`toolchain/abi_probe.ps1` 对应改动。
- 不把 `Core.Sim.dll` 同步进 Unity 工作台工程——`build.ps1` 的 `$CoreAssemblies` 是显式列出
  Foundation/Numbers/Rules/Carriers/Gameplay 五个程序集名的数组（不是通配符抓取
  `Core.*.dll`），本任务未改动这份清单，`Core.Sim` 因此天然不会被同步；`Core.Sim` 依赖
  `Adapters.Stub`，Unity 侧本就不需要它（T-N6-7 收尾同样保持这一点不变，只补 dist/UPM/
  headless 三条独立分发通道，不触碰 Unity 同步清单）。
- 不做仓库路径定位（`FindRepoRoot`/直接读 `data/_framework`、`data/_sample` 磁盘路径）——那是
  具体宿主（`GameWorldFixture`、本模块自己的 `SimTestWorldFactory`）的职责，装配根只接受调用方
  已经构造好的 `IDataSource` 列表，见判断记录"数据来源必须注入"。
- T-N6-2b 只交付"一套能被装配、能打通一场最小战斗的内容数据"这一层——`sim.anchor`/`sim.scenario`
  两表本任务同样只提供数据，接入消费逻辑是 T-N6-3a 的范围（见上）；`sim.anchor` 的 `dps`/`hp` 取值
  是"按标准玩家简化循环手算得出的自洽估计"，不是任何真实数值拍板，精调留给 T-N6-4（见
  `core/sim/tests/data/README.md` 判断记录 2）——T-N6-3a 接入锚点后嵌入数据集确实产生了 3 条
  `skill_budget_deviation` 警告，均是该数据集自己早已用 `budget_note` 确认过的"已知会超带宽"技能
  （见该目录 README 判断记录 6），不代表新的内容错误，不需要修数据（见本文件判断记录"锚点接入后
  嵌入数据集为何仍是 0 error"）。
- `StandardPlayerBuilder` 本身不生成战斗仿真报告——它只负责装配一个可玩的标准玩家单位（学技能、
  穿装备、给优先级表），"用它打一场仿真、统计胜率/时长"是 `FightRunner`/`ArenaSimulation`（T-N6-4）
  的职责，`FightRunner.Run` 内部调用 `StandardPlayerBuilder.Build` 一次。
- `FightRunner`/`ArenaSimulation` 不修改任何被仿真模块（`core/rules/combat`/`core/rules/skill`/
  `core/numbers/stat_block`/`core/rules/ai`/`core/gameplay/loot` 等）的行为——`CombatOptions.
  ResolveTrace` 是该模块早已存在的诊断回调（"消费方反馈 2026-09-11 编辑器第 31 条"落地时就已加入），
  本任务只是第一次真正使用它，不新增、不改变任何结算分支。生物追击/进战由 `core/rules/ai` 既有的
  `AiHost` 状态机驱动，本任务未改动该模块一行代码。

## 判断记录

1. **为何依赖桩适配层**：ADR-0035 决策 1 原文明说"无头运行器（复用桩适配层与世界模拟）"；
   11_工程规范与测试.md 第 6 节"数值仿真"行同样写明"无头运行器（复用桩适配层与世界模拟，目录
   建议 `core/sim`）"。这与 11 第 1 节"`core/` 下的模块不得出现任何 `adapters/` 或 `games/` 的
   引用"这条通用规则字面冲突——本次改动按 12 第 5 节"细节勘误"把这条例外写回 11 第 1 节正文
   （见该文件本次改动的勘误记录），不是本任务自行拍板：ADR-0035 已经是设计层拍板的决策，勘误
   只是把已拍板内容同步进第 1 节的目录约定原文，不新增结论。`core/sim` 因此是"生产代码"但同时
   是"无头测试/仿真专用宿主"的双重身份——它不像 `core/gameplay` 那样会被 `build.ps1` 同步进
   Unity 工程（见"不负责什么"一节），只随构建产物以 `Core.Sim.dll` 的形式服务于无头场景（CI、
   仿真批跑、内容编辑器等），这是它可以合法引用 `adapters/stub` 而不违反"游戏代码零引擎依赖"
   架构目标的原因——它本身不是会被引擎宿主加载的那一份代码。
2. **为何数据来源必须注入**：`HeadlessWorldOptions.DataSources` 要求调用方传入已经构造好的
   `IReadOnlyList<IDataSource>`，装配根内部不做任何 `Directory.GetFiles`/`FindRepoRoot` 之类的
   磁盘路径解析。这保证 `Core.Sim` 作为框架交付物可以脱离"必须存在一个名为 `<repoRoot>/data/
   _framework` 的目录结构"这一假设被复用——游戏层接入时的数据来源（`data/<game_name>/` 与
   `data/_framework/` 的具体路径拼法）完全由调用方决定，装配根只认 `IDataSource` 契约。
3. **为何本任务不进 dist/Unity 同步**：三级仿真、标准玩家生成器等后续任务落地之前，`Core.Sim`
   还不是一个"游戏层需要在自己仓库里跑仿真"就必须拿到的完整交付物；把 dist 打包、`ws-game.lock`
   记录、`check.ps1` 门禁步骤留到 T-N6-7 一次性做，避免中间态多次改动同一批发布脚本。
4. **为何 T-N6-1 阶段暂不拆 contracts/schema（已随 T-N6-2a 部分落地）**：11 第 2 节"模块范式"标准
   五段目录（`contracts/`/`core/`/`schema/`/`tests/`/`README.md`）面向"对外暴露契约、拥有自己数据
   表"的常规业务模块；T-N6-1 阶段 `Core.Sim` 既没有独立于 `HeadlessWorldBuilder` 本身的额外契约
   类型，也没有自己的数据表，拆出空的 `contracts/`/`schema/` 目录不会有实际内容，因此当时未拆分。
   T-N6-2a 落地 `sim.scenario`/`sim.anchor` 两张表后，`schema/` 目录随之补上（`SimSchemas.cs`/
   `SimValidationRules.cs`/`SimSchemaCatalog.cs`，惯例同 `core/rules/skill/schema/`）；`contracts/`
   仍未拆——本任务不引入独立于 `core/` 内类型（`AnchorRow`/`AnchorTable`/`ScenarioDef`/
   `ScenarioCatalog` 等）的额外对外契约接口，待后续任务（如标准玩家生成器）确有需要再拆分。

5. **`SchemaLayer.Sim`：为何新增枚举成员而不是复用某个既有层**：04 第 1.1 节表清单"层"列对
   `sim.scenario`/`sim.anchor` 的取值原文是"框架工具（无头仿真）"，01 第 4 节 2026-09-16 勘误也
   明确"`core/sim`……不对应 L0～L4 中的某一层"——`SchemaLayer` 枚举（`core/foundation/data_registry/
   contracts/SchemaLayer.cs`）此前只有 L-1～L5 七个取值，均对应 01 文档某一具体层，没有一个恰当
   代表这两张表；把它们错记成 `SchemaLayer.Gameplay` 会与 04 自身的登记矛盾。新增
   `SchemaLayer.Sim` 成员是纯粹的类型表面扩容（只增不删不改），经 abi_probe 验证不构成 ABI
   破坏；仓库内核实无任何对 `SchemaLayer` 的穷尽 `switch`，新增成员不会让既有代码出现未处理分支。

6. **`sim.anchor` 主键为何仍是 `id` 而非任务书字面提到的 `level`**：`TableSchema` 构造函数硬性
   要求 `primaryKey` 只能是 `"id"` 或 `"key"`，且 `id` 语义上必须是 `Core.Foundation.Common.Id`
   格式的字符串；`level` 是纯数值，不满足这个格式约束，无法直接充当框架意义上的主键。落地为：
   `id`（`sim.anchor.<name>`，如 `sim.anchor.l1`）仍是框架主键，`level` 是独立的必填 `Int` 字段，
   "每级一行、全表 `level` 从 1 起连续无缺口且不重复"这条任务书要求的约束改由
   `SimAnchorValidationRule` 在加载期做表级校验（详见该类型类头判断记录）。

7. **`sim.scenario.anchor_ref` 为何不登记为 `FieldKind.Reference`**：该字段是任务书拍板的"预留
   字段，面向未来多套锚点数据场景"——但本版本框架只登记了 `sim.anchor`唯一一张锚点表，没有第二张
   表可供它在多个候选之间选择；若登记为 `Reference(referenceTable: "sim.anchor")`，语义上会变成
   "指向 `sim.anchor` 的某一行"（单条记录），与"选择使用哪一整张锚点表"的预留意图不符，且会立刻
   要求内容作者为这个本该留空的字段填一个真实存在的 `sim.anchor.<name>` id。因此登记为普通
   `FieldKind.Id`，不登记 `Reference`/`SoftReference`（也刻意在 `Description` 里避开"引用"/"指向"
   字样，见字段声明处注释），本版本不解释非空取值的语义，留给后续任务在真正引入第二张锚点表时
   重新设计。

8. **`HeadlessWorld.AnchorTable`/`ScenarioCatalog` 何时构造**：`SimSchemaCatalog.RegisterAll` 在
   `HeadlessWorldBuilder.Build` 内总是无条件调用（两张表的 `TableSchema` 总是登记），但数据根完全
   可以不提供任何 `sim.anchor`/`sim.scenario` 行——`DataRegistry.GetAll` 对"schema 已注册但零行"
   返回空列表、不抛异常。`Build` 按"该表本次加载到的行数是否 > 0"决定是否构造对应的类型化读取，
   数据缺失时对应属性为 `null`（不是构造一个"空的" `AnchorTable`/`ScenarioCatalog`）——任务书原文
   "数据里无 sim 表时为 null 或空，不得抛"，两种表达都满足契约，选 `null` 是因为它能让调用方用
   `?.`/`??`/`is null` 一眼判断"这份数据根本没打算提供仿真锚点/场景"，比"拿到一个恒为空的对象、
   还要额外查 `MaxLevel==0`/`All.Count==0`"更直接。

9. **`Presentation.Assembly.ContentValidationOptions.ExtraSchemaRegistration`：为何是新增可设
   属性而不是新增方法重载**：`sim.scenario`/`sim.anchor` 仅无头仿真与内容工具读取，不进
   `PresentationSchemaCatalog`（否则每个运行期宿主都会背上两张自己永远不读的表，见
   `SimSchemaCatalog` 类型判断记录）——但 `toolchain/validator`、
   `NumericValidationRuleCatalogTests`（`presentation/assembly/tests/`）等"内容工具/校验测试"仍需要
   校验它们，尤其是当 `data/_sample`/`data/_framework` 里出现这两张表的数据行时（`FailOnUnknownTable`
   默认 `true`，未登记 schema 即报 `envelope` 错误）。`ContentValidationOptions` 是一个普通可变
   属性类（不是位置参数的不可变类型），新增一个默认 `null` 的 `Action<IDataRegistry>?` 属性，在
   `CreateRegistryCore` 内 `PresentationSchemaCatalog.RegisterAll` 之后、两条可选规则注册之后调用
   一次——不改变任何既有方法的物理签名，比新增 `CreateRegistryCore`/`Run` 重载更简单，调用点
   （`toolchain/validator/Program.cs` 两处、`NumericValidationRuleCatalogTests` 一处）也不需要改变
   既有传参方式，只需多设一个属性。`Tests.PresentationCommon.csproj`/`toolchain/validator/
   Validator.csproj` 因此新增对 `Core.Sim.csproj` 的引用（两者均已引用 `Adapters.Stub.csproj`，
   `Core.Sim` 同样依赖它，不新增额外的依赖边界）。

10. **`toolchain/validator/Validator.csproj` 的 `lib/` 分发分支：已知缺口，留给 T-N6-7（T-N6-7 已
    补齐，见本文件"T-N6-7 判断记录"）**：本任务
    照抄 `Presentation.Common` 既有的"源码树是否存在"二选一分支模式，为 `Core.Sim.csproj`/
    `Adapters.Stub.csproj` 补了一份平行分支（源码仓库内走 `ProjectReference`，独立发行包内走
    `lib\*.dll` 的 `Reference`）。但核实 `build.ps1` 后确认：打包 `dist\<ver>\toolchain\validator\
    lib\` 的清单是 `$CoreAssemblies`（六个固定名字，见该数组判断记录"不拷贝 Adapters.Stub"），不
    包含 `Core.Sim`/`Adapters.Stub`——这是 T-N6-1 README 判断记录 3"为何本任务不进 dist/Unity
    同步"的直接后果（`Core.Sim` 尚未进入任何发布清单）。本任务职责是"让 sim.* 两表能被
    `toolchain/validator` 校验、且不大改打包脚本"（任务书原文），因此不改 `build.ps1`：在它补齐
    `lib\Core.Sim.dll`/`lib\Adapters.Stub.dll` 之前，独立发行包（dist ZIP、UPM
    `com.gamefoundation.toolchain` 包）内对 `toolchain/validator` 的 `dotnet build`/`dotnet run`
    会因为这两个 `HintPath` 文件不存在而编译失败；源码仓库内构建（本仓库、CI、`check.ps1` 全量/
    `-Quick`）不受影响，永远走 `ProjectReference` 分支。这份缺口显式留给 T-N6-7（`Core.Sim` 的
    dist/Unity 打包收尾）一并处理。

11. **`class_id`/`creature_id`/`tier_id`/`quality_id`/`race_id` 为何登记为硬 `Reference` 而不是
    软引用**：`SimSchemaCatalog.RegisterAll` 总是紧跟在 `GameplaySchemaCatalog.RegisterAll`（经
    `ExtraSchemaRegistration` 或 `HeadlessWorldBuilder.Build` 内的调用顺序）之后登记进同一个
    `DataRegistry` 实例——`arch.class`/`creature.template`/`creature.tier_definition`/
    `item.quality_definition`/`arch.race` 因此总是与 `sim.*` 两表一起加载，`reference_integrity`
    检查天然可靠（不会像 `EconomySchemas` 判断记录里"跨 registry 装配"那种场景那样对目标表未加载
    误报）。登记为硬引用还能让"仿真场景引用了不存在的职业/生物模板"这类内容错误在校验期直接
    阻断，而不是留到仿真真正跑起来时才在运行期报错。

12. **`player.level`/顶层 `levels`/`level_from`～`level_to` 三者并存，如何理解**：`player.level`
    是标准玩家生成器（ADR-0035 决策 2）的基准输入——`sim.scenario` 一行始终对应"一个职业+一个
    基准等级"的标准玩家配置；顶层 `levels`（`kind=arena`/`coverage`）或 `level_from`/`level_to`
    （`kind=growth`）则是"这条场景要在哪些等级上各自独立跑一遍"的矩阵展开维度（战斗仿真按等级
    扫描出胜率/时长热图，成长仿真沿等级区间模拟整条成长曲线）。两者不是同一件事：前者是生成
    标准玩家这个"角色"要用的固定参数，后者是仿真运行器要展开的采样格集合；`SimScenarioValidationRule`
    只按 `kind` 校验后者的条件必填，不对两者的取值关系（如 `player.level` 是否落在 `levels`
    集合内）做任何约束——这属于仿真运行器（后续任务）的业务语义，不是数据 schema 层面的合法性
    问题。
5. **为何 `HeadlessWorldOptions` 比 `GameWorldFixture.Build` 此前的参数列表更宽**：新增
   `PlayerFactionId`/`PlayerClassId`/`PlayerRaceId`/`PlayerLevel`/`PlayerSpawnPosition`/
   `PlayerSpawnRadius`/`GameId`/`StepSeconds`/`MaxCatchUpSteps`/`FailOnUnknownTable` 等原先在
   `GameWorldFixture` 内硬编码的取值——装配根要同时服务"既有端到端测试夹具"（默认值必须与硬编码
   完全一致）与"后续标准玩家生成器"（需要按等级/职业生成不同玩家）两类调用方，把硬编码值开放成
   带默认值的选项是让第二类调用方不需要再改一遍装配根代码的最小代价；本任务本身只消费默认值，
   不引入任何行为变化。
6. **为何 `GameWorldFixture.Build` 里的死分支被删除**：`HeadlessWorldBuilder.Build` 在数据校验
   阻断时已经抛出 `InvalidOperationException`（调用不会正常返回），`GameWorldFixture.Build` 原样
   保留一份"检查 `LoadReport.IsBlocking` 再抛一次"的判断，是一段调用方永远到不了的死代码，本次
   改为一行注释说明，不再重复判断。

## T-N6-3a 判断记录

13. **期望属性求值组件为何最小化自实现，而不是复用某个既有组件**：见
    `core/sim/core/ExpectedStatCalculator.cs` 类型头判断记录——检索了 `core/rules/stat/`（该目录
    事实上不存在，属性模块是 `core/numbers/stat_block`）与 `SkillBudgetAnalyzer` 附近，唯一现成的
    "给定基础值集合按 `stat.definition` 派生规则算出最终值"逻辑是 `StatHost.ComputeFinal`/
    `ComputeDerivedBase`/`ResolveBaseValue`/`ConvertRating` 四个私有方法，要求先 `RegisterUnit`
    出一个"活体单位"才能求值，与"不依赖活体单位"的任务前提矛盾，且是私有方法不可调用。本类型按
    `StatHost.ComputeFinal` 同一套三段式聚合公式独立最小实现，只复用它已公开的两个纯函数工具——
    `RatingConversionEvaluator`（点数→百分比）与 `ItemBudgetCurve.BuildStatBudgetInfo(view, classId)`
    （按职业覆盖解析 `stat.weight`/换算曲线元信息，与装备预算校验共用同一份权重解析）。
14. **`Σ槽位` 的槽位范围与 `statMix` 来源**：只对 `item.slot_definition.is_weapon != true` 且
    `is_equipment != false` 的槽位求和（武器槽的"强度"由 `weapon_profile` 秒伤曲线口径承载，不占
    属性词条预算，见 `core/sim/tests/data/README.md` 判断记录 3）；`statMix` 取该职业在
    `stat.weight`（含 `class_overrides`）登记的**全部**属性，按权重归一化（任务书"statMix 来自该
    职业 stat.weight"）——本数据集 9 项权重均为 1.0，各占 1/9。真实装备-穿戴管线里护甲值走独立的
    `item.armor_curve` 曲线、不经 `stats[]`/词缀预算反解（`EquipmentHost.ApplyArmorValue`）；本组件
    与 `StandardPlayerBuilder` 是两条独立求值路径（见下一条），`stat.armor` 按与其它 8 项属性完全
    一致的方式计入 `statMix`，不额外复刻护甲曲线分支，避免同时维护两套求值路径。
15. **`ExpectedStatCalculator`（期望属性曲线）与 `StandardPlayerBuilder`（实际生成装备）互不要求
    数值相等**：前者是"标准玩家"这一简化抽象自己的期望值曲线（对全部非武器装备槽用统一 `statMix`
    反解求和），后者是"实际生成并穿戴一件可玩的装备实例"这一具体操作（受限于数据集里已有哪些
    模板/词缀，只能取最接近 E(L) 的一档，词缀池也是固定枚举而非连续可调）。两者数值一般不相等，
    `StandardPlayer.Deviation` 只是诊断信息，不是任何断言依据——04/07/ADR-0035 原文"期望值曲线用于
    内容平衡校验、标准玩家生成器用于仿真驱动"两个不同用途的定位一致，契约本就没有要求两者数值
    相等。
16. **`StandardPlayerBuilder` 装备实例方案：模板 + 词缀对齐反解向量，不新增实例级属性存储**：
    `ItemInstance`（`core/carriers/common/contracts/ItemInstance.cs`）在 T-N2-7（ADR-0032 决策 8
    "物品实例只存身份"）已经把"实例级属性表达"这条路关死——只有 `InstanceId`/`TemplateId`/
    `Count`/`Quality`/`Affixes` 五个身份字段，运行期由 `EquipmentHost.ApplyGrants` 按模板
    `stats[]`（字面值，逐条 `AddModifier`）+ `ApplyAffixValues`（对 `Affixes` 逐条经
    `IBudgetSolver.Solve` 反解）重新算出贡献，不接受调用方注入任意自定义数值。`StandardPlayerBuilder`
    因此选择"嵌入数据集中该槽位、该品质、物品等级 ≤ E(L) 最近一档的模板作载体"（任务书指定路径），
    并让"反解向量"的定义与 `EquipmentHost` 实际执行的运算完全同构——对每个选中的词缀调用与
    `ApplyAffixValues` 完全相同的 `IBudgetSolver.Solve` 归一化/份额换算公式，逐条累加模板自身
    `stats[]` 字面值，得到的"反解向量"与 `StatHost.GetModifiers(unitId, stat)` 里来源为该实例 id
    的全部 `Flat` 修正求和比对时必然逐位相等（同一份输入喂给同一份公式，不是近似对齐），满足任务
    书"装备贡献 == 反解向量"的验收断言，不是"直接用模板自带属性糊弄"。
17. **锚点表接入方案：规则执行时机核实结果——单遍惰性求值，不需要两遍机制**：核实
    `DataRegistry.LoadAllCore` 源码：全部表装载完毕（`_tables = loaded`）后才调用
    `RunValidationAndBuildReport` 跑 `IValidationRule.Validate`。`SkillBudgetValidationRule`/
    `ItemGrantValueExceedsShareRule` 的 `Validate` 因此总是在 `sim.anchor` 等全部表已装载完毕之后
    才被调用——`AnchorTableSkillBudgetAnchorProvider` 的构造函数可以在 `RegisterAll`（装载之前）就
    被安全构造并塞进两条规则的构造参数，真正触碰 `AnchorTable`/`sim.scenario`/
    `item.quality_definition` 等数据表的时机推迟到 `GetAnchorDps`/`GetExpectedScalingStatValue`
    首次被调用（届时数据必已装载完毕）。同 `RegistryCreatureTemplateQuery`"只持有 registry 引用，
    真正读取延迟到规则 Validate() 调用时"先例，不需要 `ContentValidationAssembly` 的两遍机制。
    `toolchain/validator/Program.cs` 场景（`registry` 实例要到 `ContentValidationAssembly.Run` 内部
    才构造出来，早于 `Program.cs` 能拿到引用）额外借用 `Func<IDataRegistry>` 工厂重载 + 既有的
    `ExtraSchemaRegistration` 钩子（该钩子由 `CreateRegistryCore` 在 `registry` 构造完成之后、
    `registry.LoadAll`——真正触发 `Validate`——之前同步调用）捕获 `registry` 引用，见该重载与
    `Program.cs` 接入点判断记录。
18. **数据源含 `sim.anchor` 才自动装配锚点提供者：按"行数 > 0"而非"表名/文件存在"判定**：
    `AnchorTableSkillBudgetAnchorProvider.DataSourcesHaveAnchorRows` 读取每个候选 `sim.anchor`
    文件的原始 JSON 文本、解析 `rows` 数组长度，只有真正含至少一行时才判定"数据源含 sim.anchor"。
    这不是可有可无的严谨——`games/_template`（游戏层空壳模板）已经登记了一份零行的
    `data/game/sim/sim.anchor.json`（供 `validate_data.py`/内容工具识别表结构），若只按"文件/表名
    是否存在"判断会被误判为"含 sim.anchor"，与任务书"validator --json 对 games/_template 为
    false"验收点矛盾；`HeadlessWorldBuilder.Build`/`toolchain/validator/Program.cs` 两处接入点共用
    同一个静态方法，判定口径不会跨两处漂移。
19. **`data/_sample` 本身也含 5 行 `sim.anchor` 演示数据——锚点接入后如何避免破坏既有端到端测试**：
    T-N6-2a 已经给 `data/_sample/sim/sim.anchor.json`/`sim.scenario.json` 各配了一份最小演示数据
    （供 schema/`ScenarioCatalog` 测试使用），任务书"数据源含 sim.anchor 则装配提供者"因此对
    `data/_sample`（经 `GameWorldFixture`/`SimTestWorldFactory.BuildWorld` 等既有端到端测试夹具）
    同样生效——这是任务书行文时未曾预见的既有事实（原文举例"无 sim.anchor 的 data/_sample+
    HeadlessWorldBuilder 路径"与仓库当前实际状态不符，已在汇报中如实上报）。接入后真实核算
    发现 `data/_sample` 的 `skill.sample_rest` 技能预算比值 5.08 超过硬上限 3.00（`HardCapExceeded`，
    阻断级）：核实 `SkillBudgetAnalyzer` 判定逻辑，"硬性规则：禁止阻断带说明的超模技能"——填写
    `budget_note` 后无论是否超硬上限都归"已确认"警告（不阻断，`DataRegistryStrictness.
    WarningsAllowed` 下 `report.IsBlocking` 只看 Error）。按此在 `data/_sample/skill/skill.def.json`
    给 `skill.sample_rest`（场外恢复技能，`use_condition: not combat.in_combat`，强度本就不该受
    战斗秒伤锚点约束）补了一句 `budget_note`（同时把 `skill.sample_burst` 早先"当前未接入……仅作
    字段样例"的说明文字更新为如实反映"已接入"的现状）——这是唯一对 `core/sim/` 目录之外文件的改动，
    只加了两处字符串字段，不改变任何既有技能的伤害/治疗数值，`dotnet test Core.sln` 全量回归
    （4188 基线用例）验证无副作用。`skill.sample_strike`（比值 1.30，超带宽未超硬上限）产生一条
    "待确认"警告，同样不阻断，未补 `budget_note`（该技能本身数值轻微越界，标"待确认"如实反映现状，
    不属于需要修的错误）。
20. **锚点接入后嵌入数据集为何仍是 0 error**：`core/sim/tests/data` 的
    `skill.sim_warrior_strike`/`rampage`/`execute` 三个技能本就已经在 T-N6-2b 阶段登记了
    `budget_note`（见该目录 README 判断记录 6"技能设计取舍"），锚点真实接入后它们产生 3 条
    `skill_budget_deviation` **警告**（比值 2.91/11.04/19.80，均超带宽，但均已有 `budget_note`
    归"已确认"分组，不阻断），符合该数据集设计之初的预期（`skill.book` 逐级解锁的高消耗/长冷却
    技能本就设计成不在"每 GCD 一次"的带宽内），不需要修改嵌入数据集本身的任何数值。

## T-N6-4 判断记录

21. **隔离方案：为何是"每场新建世界"而不是"同一世界内重生重置"**：任务书给了一条可测量的决策
    线——`HeadlessWorldBuilder.Build` 平均耗时 ≤50ms 就每场新建，否则要在同一世界内重生生物/重置
    玩家并说明 RNG 流如何按种子重置。实测（`FightRunnerTests.Probe_BuildTiming_WellUnder50MsThreshold`，
    20 次连续 `Build`）在本机环境稳定落在 10～25ms，远低于 50ms 这条线——`IRngHost.Reset(masterSeed)`
    虽然存在（理论上可以在不重建世界的前提下换种子），但"重生重置"方案还需要解决光环/仇恨/冷却/
    AI 行为状态如何清零这一整类问题，任务书明确"禁止给被仿真模块加重置功能"，选择更简单的"每场
    新建"直接绕开这整类风险，代价（吞吐量）在实测数据下完全可接受（完整 `sim_arena_matrix`
    2100 场 ≈8～9 秒）。
22. **采样口径：`combat.damage_dealt` 与 `CombatOptions.ResolveTrace` 分工，不是同一份数据重复
    采两遍**：`Resolver.Resolve` 只在结算真正"落地"（非 `Immune`、非 `Miss`/`Dodge`/`Parry`）时才
    `Enqueue` 一条 `combat.damage_dealt`（见该方法"terminal"分支源码），因此它天然适合直接累计
    `FightResult.PlayerTotalDamage`/`CreatureTotalDamage`（"确实造成了多少伤害"），但它既不携带
    `skillId`，也不覆盖"打空了"的尝试——这两样都只有 `CombatOptions.ResolveTrace`（对每一次
    `Resolver.Resolve` 调用无条件回调，含 `EffectContext.SkillId`）能提供，因此
    `FightResult.PlayerHitRate`（分子=落地事件计数、分母=`ResolveTrace` 总回调计数）与
    `PlayerSkillDamageShare`（`ResolveTrace` 按 `SkillId` 分组累加 `ResolveResult.FinalAmount`）
    走后者。两份数据在"确实落地"的交集上逐条一致，可以互相校验，但各自承担各自唯一能提供的那部分
    信息，详见 `FightRunner` 类型判断记录。
23. **`HeadlessWorldOptions.CombatOptions` 为何是新增属性而不是新增构造函数重载**：
    `GameplayAssembly` 的构造函数早已有 `CombatOptions? combatOptions = null` 这个可选参数（并非
    本任务新增），`HeadlessWorldBuilder.Build` 此前只是从未使用它、恒隐式传 `null`。本任务只需要
    "把这个早已存在的参数暴露给 `HeadlessWorldOptions` 调用方"，因此是给 `HeadlessWorldOptions`
    新增一个可选属性、并在 `Build` 内部转发，不涉及任何新增构造函数重载——比照
    `ExpectedQualityId`（T-N6-3a）同一惯例。
24. **`CreatureFactory.LevelScaler` 为何是构造后可写属性，不是第 11 个构造参数**：
    `Core.Sim.HeadlessWorldBuilder.Build` 需要在 `registry.LoadAll` 通过校验、`AnchorTable` 真正
    构造出来之后，才能判断"数据源是否真的含 `sim.anchor` 行"（决定要不要装配
    `AnchorCreatureLevelScaler`）——但 `CreatureFactory`（经 `CarriersAssembly`）在此之前就已经
    构造完成。与其给 `CarriersAssembly`/`GameplayAssembly` 各追加一份完整参数列表的新构造函数
    重载，选择把 T-N6-3b 已经就位的 `_levelScaler` 私有只读字段改成一个构造完成后仍可写的公开
    属性——两个既有构造函数（5 参/10 参）行为不变（默认 `null`），`HeadlessWorldBuilder.Build`
    在 `AnchorTable` 确定非空之后直接对 `gameplay.Carriers.Creatures.LevelScaler` 赋值即可。见
    `CreatureFactory.cs` 该属性判断记录。
25. **修了两处从未被触发过的既有数据缺口，均属"数据集配置问题，在数据集里修"**：任务书要求"先
    证明嵌入数据集里的生物会主动打玩家；若不会，是数据集配置问题"——实测发现真的不会，根因两处：
    ① `fac.reaction_matrix` 只登记了 `fac.player → fac.sim_hostile = hostile` 单向一行，
    `Core.Numbers.Faction.FactionMatrix.GetReaction(from,to)` 是方向性查找（不是对称矩阵），生物
    一侧查 `IsHostile(fac.sim_hostile, fac.player)` 落到 `fac.sim_hostile.default_reaction =
    "neutral"`，`AiHost.FindNearestHostile` 因此永远找不到玩家——`data/_sample` 的
    `fac.reaction_matrix.json` 同样只有单向一行，这不是本数据集独有的疏漏，是这一惯例此前从未被
    "生物需要主动还手"这个场景检验过。补了反向一行
    `fac.reaction.sim_hostile_vs_player`（`from: fac.sim_hostile, to: fac.player, reaction:
    hostile`）。② `stat.definition` 未登记 `stat.move_speed`，`Core.Carriers.Unit
    .MovementTickHandler.ResolveSpeed` 对"属性未在 stat.definition 登记"直接抛
    `ArgumentException`（`IStatHost.GetStat` 的既有行为，不是新问题）——只要生物真的产生一次
    `move` 意图（前提①修复后才会发生）就会触发。补了一行 `stat.move_speed`（`category: misc,
    default_base: 4`，与 `Core.Carriers.Unit.MovementOptions.DefaultSpeed` 的既有默认值一致）+
    对应 `l10n.text`。同时给 `Core.Numbers.StatBlock.StatDefinitionConsumerValidationRule
    .FrameworkBuiltinConsumerStatIds` 补了 `stat.move_speed` 一项（与既有的
    `CombatOptions.ArmorStat` 等四项同一性质——C# 代码里的默认值，数据层扫描天然拿不到，见该
    清单既有判断记录），否则会新增一条"属性无消费者"警告，破坏 G1"警告只允许既有 3 条
    budget_note 已确认项"这条门禁——这是本次唯一触碰 `core/sim/` 之外生产代码的改动，且只是给一份
    手抄字符串常量清单追加一项，不改变该规则的判定逻辑本身。
26. **（T-N6-4b 撤销）矩阵形状验收原按"该等级 ≥+3 的偏移点里至少一个满足"，复核后改回逐偏移点都
    要求**：T-N6-4 首次提交时以为 L1/+3 的边界摆动（85%～95%）是"线性插值+统计噪声"的正常现象，
    因此把验收放宽成"至少一个 ≥+3 的偏移点满足"。设计层复核指出：L20 行同时存在的非单调断层
    根本不是噪声，是两处真实缺陷（见判断记录 28/29）；缺陷修复、`AnchorTable` 扩到 25 级、
    重新核算全部锚点值之后，5 个等级的全部正偏移点（+1 除外，规则本就不要求 +1）均能稳定满足
    "≤0.5"，此前"至少一个"的放宽是在缺陷掩盖下得出的错误结论，不是真实的数据集边界特性——本条
    判断记录撤销，验收标准恢复为任务书原文"偏移 ≥+3 胜率 ≤0.5"逐点成立，见
    `ArenaSimulationTests.FullScenario_MatrixShape_WinRateDegradesWithPositiveOffset` 当前实现。
27. **`sim.scenario.sim_arena_matrix.runs` 从 20 调到 60**：见 `core/sim/tests/data/README.md`
    "T-N6-4 / T-N6-4b 调参记录"——20 次/格在低胜率格子（如 5%～15%）上采样噪声较大，容易出现
    "偏移 -1 比偏移 0 胜率更低"这类局部非单调（二项分布在小样本下的正常抖动，不代表仿真/数据有
    问题），60 次/格显著收敛、不再出现这类抖动，完整场景总耗时仍只有 ~8～9 秒，远在"`Tests.Sim`
    ≤90 秒"预算内，因此按 04/ADR-0035 判断记录 6 一贯口径直接调整了嵌入数据集本身（`runs` 属于
    "生物模板、装备/技能数值"之外，任务书"调整嵌入数据集……runs"未明确列举但"调整嵌入数据集"
    本就不是穷举清单，`runs` 是场景自身参数，调整它不影响"仿真骨架"契约面本身）。
28. **（T-N6-4b 根因 1）玩家按等级 &gt; 1 直接出生时，等级成长从未写入基础属性——装配根代码缺陷，
    已在 `core/sim` 内修复，未改动任何被仿真模块**：设计层复核要求先查清"L20 行胜率非单调"的
    机制再调数据。逐 tick 打印发现：L20 标准玩家在战斗中的 `IPowerHost.GetPowerMax(Health)` 只有
    150（1 级 `arch.class.base_stats` 原始值），远低于应有的 ~2000。根因：`Core.Rules.Assembly
    .RulesAssembly.RegisterUnit`（`HeadlessWorldBuilder.Build` 给玩家调用的那个重载）只调用
    `Progression.RegisterUnit(unitId, curveId, level)` 登记"当前在哪条曲线的第几级"这一记账
    状态，不像 `Core.Carriers.Creature.CreatureFactory.SpawnCore` 那样紧接着调用
    `Progression.ApplyGrowthToCurrentLevel` 把"2 级到出生等级"的曲线成长写成属性修正——这正是
    `Core.Numbers.Progression.IProgressionHost.RegisterUnit` 契约注释原文要求调用方自己做的
    第二步（"`startLevel > 1` 时……紧随其后显式调用 `ApplyGrowthToCurrentLevel`"），`RulesAssembly
    .RegisterUnit` 内部固定的调用顺序不允许重排（"被仿真模块"，本任务硬性规则不得修改），且仓库
    内此前从未有调用方以非默认值（1）使用过 `HeadlessWorldOptions.PlayerLevel`，这条"调用方自己
    负责第二步"的义务因此从未被触发、从未暴露。**修复**（`core/sim/core/HeadlessWorldBuilder.cs`，
    仅限该类登记了 `level_curve_ref` 时才执行，判据复刻 `RulesAssembly.RegisterUnit` 内部同一
    条件）：`RegisterUnit` 之后依次调用 `Progression.ApplyGrowthToCurrentLevel` →
    `Powers.RecomputeMax` → `Powers.RefillAll`（`sourceId` 复用
    `Core.Numbers.Progression.ProgressionEventKeys.LevelUp`，同 T-N4-5"升级回满"既有惯例的来源
    标记）——三个方法均是 `core/rules`/`core/numbers` 早已公开的既有成员，本次只是把
    `CreatureFactory.SpawnCore` 已经示范过的同一套调用顺序在玩家这一侧也照做一遍，不新增任何
    公开成员、不改动"被仿真模块"一行代码。为什么手动补 `RecomputeMax`/`RefillAll`、不能只指望
    既有的 `stat.changed → Powers.RecomputeMax` 事件订阅：`RulesAssembly.RegisterUnit` 内部
    `Powers.RegisterUnit`（经 `Archetypes.ApplyTo`）发生在成长写入**之前**，资源池按"成长前"的
    基础值把当前值/上限都定格为 `StartFull` 的那个（偏低的）数字；`Powers.RecomputeMax` 的既有
    判断记录原文只处理"上限下降时当前值随之夹取"，没有处理"上限上升后当前值该不该跟着涨"，必须
    显式 `RefillAll` 才能让当前值追上成长后的上限，且必须在首次 `world.Clock.Advance` 之前完成
    （不能依赖事件何时被 `DispatchPending` 处理——单纯指望事件订阅，"出生即成长"的玩家会在第一个
    tick 结算之前始终顶着注册时的偏低生命上限）。此修复影响面：任何调用方以 `PlayerLevel > 1`
    使用 `HeadlessWorldBuilder`/`StandardPlayerBuilder`/`FightRunner`/`ArenaSimulation` 都会受益
    （不限于 T-N6-4），`PlayerLevel == 1`（此前仓库内全部既有用法）不受影响（`ApplyGrowthToCurrentLevel`
    在等级 1 是空操作，`RecomputeMax`/`RefillAll` 幂等）——`dotnet test Core.sln` 全量回归
    （含既有 `Tests.Gameplay`/`Tests.Sim` 等全部 4233 例）验证无副作用。
29. **（T-N6-4b 根因 2）`AnchorTable` 原本只到 20 级，越级矩阵 +5 偏移在玩家满级时把生物 21～25
    级全部夹到 20 级等效强度——数据范围缺口，已扩表到 25 级**：`sim_arena_matrix.level_offsets`
    含 +5，玩家 20 级（场景定义的最高测试等级）时对手出生等级达到 25；`AnchorCreatureLevelScaler`
    对超出 `AnchorTable.MaxLevel` 的目标等级"夹到 MaxLevel"（该类型既有判断记录），
    `skill.base_curve` 曲线末端同样"夹到最后一个断点"（`PiecewiseCurve.Evaluate` 既有行为）——
    两者共同导致玩家 20 级这一行的偏移 +1/+3/+5 全部对上同一个强度天花板，胜率无法继续下降。这
    不是"生物模板选档"的问题（`sim_arena_matrix.opponent.creature_id` 全程固定为
    `creature.sim_wolf_l1`，从不切换模板），是锚点表覆盖范围不够越级矩阵实际会用到的生物等级
    范围——玩家等级测到 20、偏移测到 ±5，生物等级理论上限就是 25，`AnchorTable`/曲线理应覆盖到
    这里。**修复**：`sim.anchor` 扩到 25 级（21～25 为新增行，`dps`/`hp` 按 15→20 级斜率的 5 倍
    延长——按 1 倍/3 倍延长时越级矩阵仍测不出正偏移的胜率下降，见
    `core/sim/tests/data/README.md`"调参 3"判断记录；`level_duration_seconds`/
    `kill_interval_seconds`/`quest_share`/`expected_item_level` 延续既有公式或复用 20 级值，
    这几个字段本任务门禁不检验、玩家也不会真的到这些等级，纯粹满足 schema 必填），
    `skill.base_curve.sim_creature_bite{,_elite}` 新增 25 级断点，
    `EmbeddedDatasetTests.AnchorTable_HasAllTwentyFiveLevelsContinuous`（原
    `…TwentyLevelsContinuous`）与本模块 `AnchorCreatureLevelScalerTests
    .Spawn_AboveMaxAnchorLevel_ClampsToMaxLevel`（越界夹取边界从"20 vs 25"改为"25 vs 30"，否则
    测的就不再是真正越界）同步更新。

## T-N6-5 判断记录

30. **成长仿真为何不直接调用 `LootHost.RollDetailed`，而是消费自动监听器的产出**：
    `Core.Gameplay.Assembly.GameplayAssembly` 无条件装配 `CreatureDeathLootListener`（订阅
    `unit.died`）——任何 `HeadlessWorld` 都逃不开这一条既有接线，生物死亡时它已经真实调用过一次
    `RollDetailed` 并按 `CurrencyDepositPolicy.OnKill`（本装配根默认值）把货币入账给击杀者、把
    物品落地为地面 `DroppedLootEntity`。`GrowthSimulation` 若再自行调用一次 `RollDetailed`，
    等于对同一次死亡多掷一次骰子（重复消耗 `IRngHost` 序列、双倍入账/掉落），是明确的正确性
    缺陷。改为消费监听器已经产生的结果：货币经 `IEconomyHost.GetBalance` 前后差值/
    `economy.currency_changed` 事件确认到账，物品经 `WorldSim.QueryEntities(Kind=
    EntityKinds.Loot)` 找到掉落实体后调用真实的 `LootHost.PickUp` 拾取进背包——`RollDetailed`
    本身仍然是真正被调用的那份代码，只是调用方是监听器而不是 `GrowthSimulation`，链路上没有
    任何一步是"手算"。为保证拾取一定成功（默认 `LootOptions.PickupRange` 3.0 可能小于本数据集
    技能射程 5.0，生物死亡位置可能超出默认拾取半径），新增 `HeadlessWorldOptions.LootOptions`
    转发属性，成长仿真把 `PickupRange` 放宽到 50。
31. **击杀/任务经验为何直接调用 `GrantXp`，不依赖 `CreatureDeathXpListener`**：该监听器同样
    无条件装配，但它的默认击杀经验来源 id 是 `CreatureDeathXpListener.DefaultKillXpSourceId`
    （字面 `"prog.xp_source.kill"`），本数据集登记的却是 `prog.xp_source.sim_kill`（`sim_`
    前缀惯例）——两者不是同一个 id，监听器内部 `HasXpSource` 查不到会静默跳过，不会发放，也
    不会与 `GrowthSimulation` 的显式调用重复发放。`HeadlessWorldOptions` 本可以新增
    `ProgressionOptions` 转发属性去配置监听器的 `KillXpSourceId` 让监听器接管，但那样还是要
    解决"任务/探索当量经验监听器完全不发放（没有对应的击杀事件）"这一半的缺口，直接调用
    `GrantXp(unitId, sourceId, XpContext)` 两条来源都发（击杀＋任务当量）更简单、路径统一，且
    `GrantXp` 本身就是任务书原文指名的"真实 API"（只传入来源等级/当量，实际发放额度仍由
    `ProgressionHost` 内部公式计算，不是手算）。
32. **任务当量为何是 `Q(L)` 本身，不是 `Q(L)/(1-Q(L))`（联调排错记录）**：数值总纲 4.7 节
    "升级所需(L)=killBase(L)×monsterEquivalent(L)×(1+Q(L))"——`monsterEquivalent(L)` 本身就是
    "只看击杀"这一部分对应的目标击杀数：击杀 `monsterEquivalent(L)` 次、每次 `killBase(L)`
    经验，总击杀经验恰为 `killBase×monsterEquivalent`；要让这 `monsterEquivalent(L)` 次击杀
    正好把总需求（`killBase×monsterEquivalent×(1+Q)`）填满、不多不少，每次击杀还需额外发
    `killBase(L)×Q(L)` 的任务当量经验，即 `Equivalent=Q(L)`。初版实现误写成
    `Q(L)/(1-Q(L))`（把"任务份额相对击杀份额的比例"和"任务份额相对总需求的比例"搞混），会让
    每次击杀实际发放的经验变成 `killBase/(1-Q)`（比正确值 `killBase×(1+Q)` 更高），达标所需
    击杀次数因此变成 `monsterEquivalent×(1-Q²)`，比设计意图的 `monsterEquivalent` 次更少——
    完整场景联调测试第一次跑通后实测 L3/L4/L8 三个等级"实际击杀数明显少于 monsterEquivalent、
    每级时长系统性偏短"（偏离 26%～46%，超出 0.25 带宽），定位到此处后改用 `Q(L)`，修复后全部
    19 个等级四条轨迹逐级在带宽内。`ExpectedCumulativeGoldAt` 的金币期望公式同步改为
    `monsterEquivalent(l)×goldBase(l)×(1+Q(l))`（原为 `÷(1-Q(l))`），与经验联动同一口径。
33. **嵌入数据集货币掉落 `count_range` 的既有缺口（联调排错记录，与本任务代码逻辑无关的既有
    数据问题，首次被真正验证）**：`Core.Gameplay.Loot.LootHost.ResolveCurrencyOutcome` 的真实
    公式是"`equivalents`（从 `loot.table` 条目的 `count_range` 掷骰所得的整数当量）×
    `IEconomyHost.TryGetGoldBaseAmount(怪物等级)` × 分档倍率 × 难度倍率"——`count_range` 登记的
    是"当量"，不是"最终掉钱数"。T-N6-2b/T-N6-4 阶段登记 `loot.table.*` 时把 `count_range` 直接
    填成了 `econ.gold_base_curve` 断点 ±2（如 L1 的 `[3,7]`，均值 5，恰好等于
    `goldBase(1)=5`）——这是把"最终掉钱数应该是多少"当成了"当量取值范围"来填，实际效果是
    "当量(均值5) × goldBase(5) = 25"，掉钱系统性偏高约 5 倍。T-N6-4 阶段的仿真只关心战斗
    胜率/DPS/HP，从未真正验证过货币产出，这一缺口因此从未暴露，直到 T-N6-5 成长仿真第一次真正
    累计金币。**修复**：全部 `loot.table.*` 的货币条目 `count_range` 改为常量 `{"min":1,
    "max":1}`（当量恒为 1），使掉钱恰好等于 `goldBase(怪物等级)`，与数值总纲 4.8 节字面公式
    （无当量项）对齐。修复后 `sim_growth_full` 金币轨迹逐级偏离降到 0～12%（原 100%+）。
34. **`loot.table.*`（5 档普通怪）为何各追加 `chest`/`legs`/`feet` 三条掉落条目**：
    T-N6-2b/T-N6-4 阶段只登记了主手（0.3 概率）+ 头部（0.05 概率，quality_weights 常见/稀有）
    两个槽位——供最小烟雾测试与越级矩阵使用，两者都不关心装备等级轨迹。成长仿真需要"各槽平均
    装备等级"追上 `expected_item_level(L)`，若胸/腿/脚三槽永远没有掉落条目，三者会永远停留在
    1 级出生时 `StandardPlayerBuilder` 给的初始装备，均值必然被拖低、远低于期望曲线。按与既有
    主手条目同一惯例（common 品质，0.3 概率）各追加一条，不改变任何已有条目的取值。
35. **`skill.def.sim_probe_overbudget`/`item.template.sim_probe_underbudget` 两条覆盖仿真
    探针的设计取舍**：均不进入任何会被真实消费的接线（`skill.book`/`ai.rotation`/
    `loot.table`），只作为 `skill.def`/`item.template` 全表扫描时才会被看到的数据行，不影响
    任何既有测试断言、不参与任何真实战斗结算。探针技能未登记进任何 `skill.book`，
    `SkillDefCache.TryResolveBudgetAttribution` 因此把它归为 `SkillBudgetTier.Unattributed`
    （等级缺省取 1，按 Player 档带宽/硬上限判定——"宁可多报警告，不可放过手滑"，`SkillBudgetTier`
    既有判断记录），不需要为了让探针"有一个技能等级"而去改动 `skill.book`/`ai.rotation` 这类
    会被 `StandardPlayerBuilder`/`FightRunner` 真实消费的数据（避免影响既有断言"已学技能数"/
    "优先级表能选出可施放技能"）。探针物品选 `item.slot.sim_chest`/L20/common（预算上限随
    等级增长最大，1 点耐力相对这个上限的利用率最低，离群效果最明显），不带任何词缀。两条探针
    均已按各自规则要求的方式"确认"——技能填 `budget_note`（`skill_budget_deviation` 走
    "已确认"分组、不阻断）；装备触发的 `item_budget_utilization_low` 本身
    `NonEscalatable`（04 第 5 节原文即以"装备预算利用率过低"为该判定口径的示例——"抓意图不抓
    手滑"），没有类似 `budget_note` 的字段级确认机制，如实记录为"预期内的警告"，不做抑制。

## T-N6-6 判断记录

36. **`CoverageSimulation` 三处种子派生改用 `StableIdHash`（FNV-1a 32 位），根治跨进程不确定性
    ——T-N6-5 遗留、由本任务基线比对首次验证出的真实缺陷**：`AnalyzeSkills`/
    `MeasureItemMarginalImpact`/`AnalyzeCreatures` 三处此前用 `skillId.Value.GetHashCode()`/
    `templateId.Value.GetHashCode()`/`creatureTemplateId.Value.GetHashCode()` 派生独立仿真种子；
    `string.GetHashCode()` 在 .NET Core 默认对字符串哈希做逐进程随机化（防哈希 DoS 攻击的安全
    特性），同一个 id 字符串在不同进程里的返回值并不相同。T-N6-5 阶段的 `CoverageSimulationTests`
    确定性用例只在同一进程内重跑两次比较（xunit 单个测试方法内），从未跨进程验证过；T-N6-6 联调
    `toolchain/simrunner` 时第一次用两次独立的 `dotnet run` 进程跑同一 `base_seed`/同一份数据，
    发现 `coverage.creature.*`/`coverage.skill.*.secondary_measurement` 等统计量逐次运行不一致
    （`BaselineComparer` 报出 11 条 `Exceeded`，见联调过程记录），与仓库通篇"同种子确定性"的既有
    判断记录（如 `ArenaReport.ToJson` 判断记录"同一场景同 `base_seed` 两次调用逐字节相同"）直接
    矛盾。**修复**（`core/sim/core/CoverageSimulation.cs`，新增私有方法 `StableIdHash`）：改用
    FNV-1a 32 位对 id 字符串的 UTF-8 字节做确定性哈希，跨进程/跨机器同输入恒同输出，不改变三个
    调用方法的任何行为契约（返回值只是"一个由 id 派生的 int"，取值范围/用途不变）。修复后
    `toolchain/simrunner run --scenario all` 两次独立进程运行的 `*.report.json` 逐字节相同（含
    coverage 场景），`dotnet test Core.sln` 全量回归无副作用（`CoverageSimulationTests` 断言的是
    "分类排序第一位"这类相对关系，不依赖具体种子取值，未受影响）。这是本任务唯一触碰
    `core/sim/core/` 下 T-N6-5 既有生产代码的改动，且是"根治优先"（任务书硬性规则）要求的必要
    修复——本任务的验收 5-①（"同种子重跑，`BaselineComparer` 对三个场景全部 `Same`"）离不开这条
    真正成立的确定性不变量，绕开它（比如只用同进程内重跑）会让验收流于形式。
37. **`SimReport`/`SimBaseline`/`BaselineComparer` 为何是三个独立类型，不合并成一个**：
    `SimReport` 是"一次仿真运行的完整快照"（含原始报告、设计锚点偏离等丰富上下文，供人/工具排查
    问题）；`SimBaseline` 是"够用来做回归比对的最小信息"（只有 path→value，任务书原文指定的精简
    形状，供长期提交进 git 历史、diff 时噪声最小）；`BaselineComparer` 是"给定前两者产出第三种
    结果"的纯函数运算，不持有任何状态。三者职责边界清晰对应任务书章节结构（3.1 统计量快照/基线
    文件格式、3.1 比对器），合并成一个类型会让"运行时报告"与"持久化基线"这两种生命周期完全不同
    的数据混在一起，`SimBaseline` 也无法再保持"最小、diff 友好"这一设计目标。
38.（**T-N6-8b 已取代，见下方"T-N6-8b 判断记录"一节记录 45**：本记录保留作历史决策存档——
    当时选择子串匹配确有其理由，但审计发现它对语义不相关的叶子名存在误配隐患，T-N6-8b 改为精确
    匹配显式映射表，正文"容差来源优先级"一句已同步更新，不再按本记录描述的方式解析。）
    **容差解析为何按"叶子名子串匹配"而不是精确匹配整段路径**：`ScenarioDef.Bandwidths`
    （`sim.scenario.bandwidths`）登记的键是"统计量类别名"（如 `dps`/`hp`/`ttk`/`ttd`/
    `hit_rate`/`level_duration`/`item_level`/`gold`），不是某一条具体路径——同一个类别名要覆盖
    "该类别下全部等级/偏移格子"的容差（如 `dps` 要同时覆盖 `arena.reconciliation.L1.dps`、
    `arena.L1.off+0.player_dps_mean`、`arena.L5.off-3.player_dps_mean`……），精确匹配整段路径
    做不到这种"一对多"覆盖，子串匹配（取最长匹配 key，避免 `hp`/`hit_rate` 之类短 key 誤配长
    叶子名的歧义）是能让"同名统计量复用同一条设计带宽"这一任务书原文要求生效的最简单实现。
    `growth`/`coverage` 场景的叶子名特意对齐 `Bandwidths` 既有键名字面值（见 `SimReport`
    类型判断记录"叶子名对齐带宽键名"），这条子串匹配规则因此对它们总是精确命中；`arena` 场景
    的按格子统计量（`win_rate`/`ttk_mean_seconds` 等）叶子名与 `Bandwidths` 键名不完全一样，
    子串匹配能命中一部分（如 `ttk_mean_seconds` 命中 `ttk`），命中不到的退回默认相对容差 1%，
    这是有意的设计取舍（这些格子统计量本就是探索性观测，不是任务书要求锚定设计带宽的对账等式，
    见 `SimReport` 类型判断记录"`Anchor`/`Deviation` 与 `BaselineComparer` 的偏离是两件不同的
    事"）。
39. **`BaselineDiffStatus.Same` 的 `ExactMatchEpsilon` 阈值为何是可配置项，不是硬编码常量**：
    默认值 `1e-9`（绝对值）是为"同种子重跑、逐位相同"这一最常见场景挑的边界——真实的同种子重跑
    产生的差异恒为字面 `0.0`（同一段代码同一份输入，IEEE754 double 运算是确定性的，不存在"差了
    一点点噪声"这种中间状态），`1e-9` 因此有充足的安全边际。但 `BaselineComparerTests`
    "1e-12 扰动"用例需要故意构造一个"差异真实存在但小到接近浮点噪声量级"的场景来证明比较逻辑
    走的是阈值分支、不是被某个隐藏的精确相等判据吞掉——把 `ExactMatchEpsilon` 做成
    `BaselineCompareOptions` 上的可写属性，测试可以显式收紧到 `1e-15`，不需要依赖"被扰动的
    统计量数值恰好多大"这种脆弱的隐式耦合，见该测试判断记录。

## T-N6-7 判断记录

40. **第 0 步根治：`data/_sample/sim/sim.anchor.json` 的 `sim.anchor.l1.dps` 从 10 校准为 45**：
    详见 `data/README.md`"skill N3 示例数据"一节 T-N6-7 勘误与本文件下方摘要——T-N6-3a 接入真实
    锚点提供者后，`data/_sample` 的 `skill.sample_strike`/`sample_rest`/`sample_burst` 三条早于
    N6 就存在的示例技能（T-N3-11 落地）分别产生比值 1.30/5.08/3.53 的 `skill_budget_deviation`
    警告；`ConfirmedDeviation`（已确认，后两者已补 `budget_note`）与 `UnconfirmedDeviation`
    （待确认，`sample_strike`）在 `SkillBudgetAnalyzer.Classify` 里都仍会产出一条 `[warning]`
    （`budget_note` 只影响是否升级为阻断错误，不影响是否产生警告本身），3 条警告与
    `toolchain/tests/test_validate_data_summon_only_rule_default_wiring.py::
    test_unmodified_sample_data_still_zero_errors_zero_warnings` 断言的字面
    `"errors 0, warnings 0"` 直接冲突。根因是 `sim.anchor.l1.dps=10` 是 T-N6-2a 阶段"仅供
    schema/`AnchorTable` 测试使用"的占位值，从未与早于 N6 就存在的示例技能预算消耗做过校准。
    <br/>**修复**：只改 `sim.anchor.l1.dps`（10→45），不改任何示例技能、不改测试——45 是三者中
    最吃紧的 `skill.sample_rest`（原比值 5.08）按 `ratio_new = ratio_old × 10 / anchorDpsNew`
    换算需要的最小值（≈42.33）之上留出约 15% 安全边际；换算后三者新比值约
    0.29/1.13/0.78，均落入 `skill.budget_rule.default` 玩家档带宽 [0.80,1.20]
    （`Classify` 实际只判定上界 `ratio<=1+bandwidth`，"[0.80,1.20]"是校验消息的描述文本，不是
    代码里真的判定下界）。只改 L1 一行，L2～L5 与 `hp`/`ttk_seconds` 等其它字段未动——
    `SimAnchorValidationRule` 未对 `dps` 做跨行单调性约束（只约束 `expected_item_level`，见
    判断记录 6 附近），既有测试（`AnchorProviderIntegrationTests`/`HeadlessWorldBuilderSimTests`）
    只断言"不阻断"/"表有 5 行"这类结构性事实，未断言具体 `dps` 取值，不受影响。优先改锚点、不改
    示例技能：三条技能的数值早已服务"演示 `use_condition`/`budget_note`/`scaling`/
    `base_curve_ref` 字段写法"这一 T-N3-11 既定目的（见 `data/README.md`），`sim.anchor` 是
    T-N6-2a 才补的新表、从未真正校准，是影响面最小的根治点。
    <br/>**`budget_note` 未撤**：校准后比值已回落带宽内，`budget_note` 不再被 `Classify` 任何
    分支实际读取，但按任务书"若不再需要就撤掉，需要就保留并说明"的后一选项保留——它记录着
    T-N3-11 时"演示 `ConfirmedDeviation` 分支"的设计意图，删除会抹去这段历史说明；保留处于
    "存在但不被触发"的无害闲置状态，不影响 0 警告验收。`AnchorProviderIntegrationTests
    .SampleData_StillLoadsWithoutBlockingAfterAnchorWiring` 方法上方注释同步改为如实反映"校准后
    已不产生任何警告"的当前状态。
    <br/>**验证**：`python toolchain/validate_data.py --strict` → `errors 0, warnings 0`；
    `python -m pytest toolchain/tests -q` 全过（242 passed, 4 skipped）；`dotnet build
    Core.sln -c Debug`/`dotnet test Core.sln`（4249 例）无副作用——本次只改了一个 JSON 数据文件的
    一个数值字段与相关 Markdown/注释说明，未触碰任何生产代码。

41. **`check.ps1`"数值仿真基线比对"步骤的位置与"复用步骤 1 已构建产物"**：排在"6. toolchain
    自身 pytest 套件"之后、"7. 禁用词扫描"之前（即 Unity 四步与消费方演练之前，任务书原文
    要求）——同样属于"数据/内容一致性类"的校验步骤（数据校验、元数据门禁、pytest 都在附近），
    紧跟 pytest 顺序上自然；不需要 Unity，`-SkipUnity` 不影响它。命令行直接
    `dotnet <ArtifactsPath>\bin\SimRunner\<config小写>\SimRunner.dll run ...`——与
    `toolchain/abi_probe.ps1` 的 `Resolve-CurrentDll`/check.ps1 步骤 1 本身同一套
    "`--artifacts-path` 布局"约定，不再 `dotnet run --project` 触发一次重复编译（`SimRunner`
    是 `Core.sln` 的一个项目，步骤 1 早已构建过）。输出目录固定
    `<ArtifactsPath>\sim_out\`（每次运行前清空重建），失败时打印全部 `*.diff.txt` 全文（对齐
    ABI 探针步骤同一"自动分诊"风格）。`-Quick` 下 `Add-SkippedStep`（任务书硬性禁止事项"禁止把
    数值仿真列为 `-Quick` 步骤"——这里的"跳过"跟其它非秒级步骤一样，是"这一个子集不跑"，不是
    "认定它不重要"）。步骤总数：全量/`-SkipUnity`/`-Quick` 三种既有组合此前恰好都是 24 步（用同
    一份 `$script:Results` 计数机制——`-Quick`/`-SkipUnity` 各自跳过的步骤数量不同，但注册的
    步骤*调用点*总数不受运行参数影响），新增本步骤后三者一致变为 25 步（任务书原文"非 -Quick
    全量由 27 变 28（-Quick 仍 24 步）"与本仓库 `check.ps1` 实测行为不一致——已实测核对：
    `$script:Results.Add` 无论 `Invoke-CheckStep` 的结果是 PASS/FAIL 还是 `Add-SkippedStep` 的
    SKIP，都会让总数 +1，不存在"某种运行参数下新增步骤不计入总数"的机制；这一实测结论已如实
    上报设计层，未按任务书字面的"27/28"数字反向修改任何既有步骤的注册方式来凑数，避免为了凑
    一个数字而扭曲不相关步骤的分类）。

    补充（2026-09-17，`fix/sim-dataset-display-map`，反馈 46 后续 bug 修复：`check.ps1` 新增
    "6a. `core/sim/tests/data` 单独 `--strict` 校验"）：上面这条判断记录写于 `-Il2cpp` 开关
    尚未引入本脚本之前，当时"全量/`-SkipUnity`/`-Quick` 三者恰好一致"是真实的（IL2CPP 三步
    还不存在，不存在分叉）。`-Il2cpp` 开关引入后，`check.ps1` 实际存在两个不同的"总步骤数"
    族群——`if ($SkipUnity) { 6 个 Add-SkippedStep（Unity 四步 + 消费方演练折叠） } else { Unity
    四步 + 消费方演练分别注册 + `if (-not $Il2cpp) { 3 个 Add-SkippedStep } else { 3 个
    Invoke-CheckStep }`（IL2CPP 三步） }`——`-Quick`/`-SkipUnity` 族只注册 6 个 Unity 相关槽位，
    全量族（不传 `-SkipUnity`）注册 9 个（Unity 四步 + 消费方演练 + IL2CPP 三步，无论 PASS 还是
    SKIP 都算注册），两族相差固定 3。已用 `Grep` 实测数过全部 `Invoke-CheckStep`/
    `Add-SkippedStep` 调用点并按分支归类核对：非 Unity 部分（步骤 0～9，含本次新增 6a）共 19
    个，`-Quick`/`-SkipUnity` 族 19+6=25，全量族 19+9=28，与归档证据
    `architecture/落地计划/audit-24a11fe-20260910/followup-2026-09-10c.md:128`
    "门禁通过：全部 27 步（24 PASS + 3 SKIP，均为预期的 IL2CPP 分支）"（T-N6-7 新增"数值仿真
    基线比对"**之前**的全量族：24+3=27，之后变 25+3=28）互相印证一致；`build.ps1 -Release`
    默认不传 `-SkipUnity`/`-Il2cpp`（README.md"发布流程"一节；`build.ps1` 全文无 `-Il2cpp`
    调用点），命中的正是全量族。本次新增 6a 步骤不受 `-Quick`/`-SkipUnity`/`-Il2cpp` 任何一个
    开关门控，两个族群各自 +1：`-Quick`/`-SkipUnity` 族 25→26，全量族 28→29。上面那段"27/28
    与实际不一致"的历史陈述记录的是**更早一版**任务书数字（当时 `-Il2cpp` 尚不存在，全量族
    确实等于 `-Quick`/`-SkipUnity` 族，任务书"27→28"的说法与实测的"24→25"不一致）——与本次
    "28→29"是两个不同时间点的独立事实，不互相覆盖，均如实分别保留。）

42. **`build.ps1`：`adapters/headless/` 与 `toolchain/validator/lib/` 两处补 `Core.Sim.dll`/
    `Adapters.Stub.dll` 的取值来源为何不互相依赖（各自独立取源码仓库原始构建产物）**：
    `Core.Sim.dll` 分发到三处（`adapters/headless/`、`toolchain/validator/lib/`、
    `toolchain/simrunner/lib/`）+ 一份预编译产物随 `toolchain/simrunner/bin/`；若让后两处依赖
    前一处已经拷进 dist 的文件，会在 `build.ps1` 内部产生"哪一节必须先跑"的隐式顺序耦合（未来
    若任何一节被重排，另外两节会读到不存在的路径而失败，且报错信息只会说"找不到文件"，不会
    直接指向"顺序被换了"这一根因）。改为三处各自独立从 `core\sim\bin\$Configuration\
    netstandard2.1\Core.Sim.dll`/`adapters\stub\bin\$Configuration\netstandard2.1\
    Adapters.Stub.dll`（源码仓库的原始构建产物路径，`-SyncOnly` 场景下与六个核心 DLL、既有
    `Adapters.Stub.dll` 分发同一前提——要求之前至少完整构建过一次）取值，只是同一份文件内容被
    独立拷贝三次+预编译打包一次，代价是三行 `Copy-Item`/一份 `Get-ChildItem` 循环，换来的是三处
    互不依赖、可以任意重排验证（本任务开发过程中确实调整过一次相对顺序，未触发任何隐藏 bug）。
    `toolchain/simrunner/lib/` 里的六个核心 DLL 子集（5 个，不含 `Presentation.Common`）复用了
    `distAdapterPluginsCoreDir` 里已经同步好的文件（这五个此前就已经存在，不是本次新引入的
    "第三份"复制，取用既有产物比再拷一遍源码仓库构建目录更省重复代码）。

43. **`toolchain/abi_probe.ps1`：`Core.Sim.dll` 的表面 dump 为何还要额外把 `Adapters.Stub.dll`
    放进当前工作树 DLL 目录（`current-lib/`），却不把它加入 `$allDllNames`（不参与比对/不出现在
    报告里）**：`toolchain/abi_surface/SurfaceDumper.cs` 用
    `System.Reflection.MetadataLoadContext`（只读元数据反射）加载每个传入的 DLL 路径，反射任何
    公开/受保护成员的完整签名（含参数/返回类型）时，运行时必须能解析该签名引用到的*全部*
    程序集——`Core.Sim.dll` 的公开 API 表面直接用到 `Adapters.Stub.StubFileSystem`/
    `StubSpatialQuery` 两个类型（`HeadlessWorldOptions.FileSystem`/`HeadlessWorld.Spatial`），
    解析器（`PathAssemblyResolver`）按"每个传入 DLL 的同目录兄弟文件 + 当前运行时目录"两类
    候选路径构建，若 `Adapters.Stub.dll` 不在候选集合里就会在 dump 阶段直接抛
    `FileNotFoundException`（实测复现：修复前第一次跑 `abi_probe.ps1` 时命中）。但
    `Adapters.Stub.dll` 本身并不是本次新增比对的目标（它在基线 1.35.0 里也不存在，且不是
    "仿真骨架"这个交付概念本身要评估兼容性的对象——它是 `Adapters.Stub` 既有的、这次没有改动
    的既有交付物），因此只把它的当前构建产物复制进 `current-lib/` 目录（让解析器能找到它），不
    加入 `$allDllNames`/`$currentDllPaths`（不对它本身做 dump/compare，报告里不会出现任何
    `Adapters.Stub` 的类型行）。`Resolve-CurrentDll` 因此新增了 `Adapters.Stub.dll` 这一个
    `$projectNames`/`$classicRoots` 条目，纯粹是为了复用同一套"按 `-ArtifactsPath`/经典布局
    解析当前工作树 DLL 路径"的既有函数，不代表 `Adapters.Stub.dll` 被正式纳入 ABI 比对范围。

44. **`toolchain/abi_probe.ps1`：基线抽取环节改"先探测再抽取"而不是"抽取失败就整体判定为新增"
    ——两者的差异与为何选前者**：`Test-ZipEntryExists`（新增）与 `Get-ZipEntryTo`（既有）各自
    独立打开一次 zip 归档；若改成"直接调用 `Get-ZipEntryTo`，catch 到异常就当作新增"，会把
    "zip 里确实没有这个条目"（预期状态，如本次 `Core.Sim.dll`）与"zip 文件本身损坏/路径错误/
    磁盘 I/O 失败"（真实故障，理应让探针整体失败，不该被静默吞成"新增程序集"）两种性质完全
    不同的失败原因用同一个 `catch` 块混为一谈——这类"用异常控制流表达业务分支"的写法会让真正的
    基础设施故障（如误传了一个损坏的 zip 路径）被误诊断成"这是个新程序集，正常"，反而降低了
    探针本身的可信度。显式的存在性探测把"没有这个条目"当成一个可以被正常检查的布尔状态，与
    "打开 zip 本身失败"这一真正的异常路径分开，只有前者才会被本次改动新增的分支吞掉，后者仍会
    从 `Test-ZipEntryExists`/`Get-ZipEntryTo` 内部正常抛出、被 `Invoke-CheckStep`（经
    `check.ps1` 调用时）或 PowerShell 默认的 `$ErrorActionPreference = "Stop"`（独立调用时）
    如实中断。

## T-N6-8b 判断记录

45. **容差解析从"叶子名子串匹配"（记录 38）改为"叶子名精确匹配显式映射表"——复核建议根治**：
    设计层复核发现记录 38 的子串匹配（`leaf.IndexOf(key)`，取最长匹配 key）存在把语义不相关的
    叶子名误配到带宽键的隐患——审计（`git grep -n "new SimStat("` 核对 `SimReport.cs` 三个
    `Extract*Stats` 方法产出的全部固定叶子名）发现 `growth.summary.cumulative_gold_via_balance`/
    `cumulative_gold_via_events` 两行的叶子名恰好含子串 `"gold"`，在旧实现下会被误配到 `growth`
    场景登记的 `gold` 带宽（25%），而这两行的真实用途是"两条独立记账路径（API 调用 vs 事件流）
    是否彼此一致"这一代码正确性核对，不是设计带宽要覆盖的对象——用 25% 的宽松容差掩盖，会削弱这
    条核对本该有的敏感度（一次记账路径分叉的代码回归，只要幅度在 25% 以内就不会被基线比对发现）。
    `player_max_health`（`hp` 是否会误配）、技能 id 叶子含 `"_hp_"` 子串（如
    `skill_share.skill.sim_hp_regen`）两类风险经复核确认在当前仓库实际数据下不会命中（前者
    `"player_max_health"` 字面不含子串 `"hp"`；后者当前嵌入数据集/`data/_sample` 全部技能 id 均
    不含 `"_hp_"`，`git grep -n '"id"[[:space:]]*:[[:space:]]*"skill\.'` 核对过），但"当前数据集
    恰好没撞上"不是"这套匹配规则本身安全"的证明——任何后续新增的统计量字段名/技能 id 命名都可能
    意外撞上一个短带宽键（`hp`/`dps`/`ttk`/`ttd`/`gold` 全是 2～3 个字符的常见英文片段），子串
    匹配这条规则本身的风险面随数据集增长只会扩大，不会收敛。
    <br/>**修复**：`BaselineCompareOptions` 新增 `LeafBandwidthKeys`
    （`IReadOnlyDictionary<string leaf, string bandwidthKey>`，可整份覆盖）与默认表
    `DefaultLeafBandwidthKeys`——对上面审计出的全部固定叶子名逐一显式登记该不该映射、映射到哪个
    带宽键（逐项理由见该字段判断记录），`ResolveTolerance`（连带公开为 `public static`，供单元
    测试直接构造边界叶子名验证，不需要为每个边界情形另跑一整套仿真只为凑出一条真实统计量路径）
    改为查这张表的精确命中，不再做任何子串/前缀猜测；命中表但当前场景 `Bandwidths` 没有登记该键
    时，与查不到叶子名一样退回默认相对容差——两种"没有覆盖"情形处理方式统一，不再区分。
    `growth.summary.cumulative_gold_via_*` 两行因此从"借道 gold 带宽 25%"变为"默认相对容差
    1%"，这是有意收紧、不是需要修补的副作用（任务书原文"若映射修正导致某统计量容差变化，基线
    本身不变，只看比对结果"）。
    <br/>**验证**：新增 `BaselineComparerTests.ResolveTolerance_PlayerMaxHealth_DoesNotUseHpBandwidth`/
    `ResolveTolerance_SkillIdLeafContainingHpSubstring_DoesNotUseHpBandwidth`（直接构造边界叶子名，
    证明不再误配）、`ResolveTolerance_RegisteredLeaf_UsesScenarioBandwidth`（三类已登记叶子名仍
    按原有分类生效，证明"该配的还配得上"）、
    `Compare_AfterLeafBandwidthKeysFix_SameSeedRerun_AllThreeScenarios_StillZeroExceeded`（三个真实
    场景各自同种子独立重跑两次，容差表改过之后仍 `exceeded=0`——因为同种子重跑逐位相同，落在
    `ExactMatchEpsilon` 分支，容差收紧与否都不影响这条不变量，见该测试判断记录）。既有
    `Compare_SameSeedRerun_AllThreeScenarios_AllSame`/`Compare_MutatedStrengthBaseStat_*`/
    `Compare_TinyRelativePerturbation_*` 三条既有用例无改动、全绿（构造/断言均未依赖具体哪张容差
    映射表实现，只依赖 `Compare` 的行为契约）。
46. **`adapters/headless/README.md`/`toolchain/registry/manifests/adapter-headless/README.md`
    "数值仿真骨架"示例代码此前不可编译——复核建议根治**：两份文档"无头宿主怎么用"示例代码写的是
    `ArenaSimulation.Run(world.Options, scenarioDef)`（`HeadlessWorld` 根本没有 `Options` 属性，
    真实签名是 `Run(ScenarioDef scenario, AnchorTable anchors, IReadOnlyList<IDataSource>
    dataSources, bool failOnUnknownTable = false)`，见 `ArenaSimulation.cs` 第 246 行附近）与
    `SimReport.FromArenaReport(arenaReport, scenarioDef, frameworkVersion: "1.36.0")`（真实签名
    额外需要 `IDataRegistryView registry` 参数、且具名参数是 `generatedWithVersion` 不是
    `frameworkVersion`，见 `SimReport.cs` 第 157 行附近）——两处签名都是凭空杜撰，从未真正编译过。
    根因是这两份文档从 T-N6-7 起随分发产物新增该节时，作者按记忆/直觉写了示例代码，没有配套测试
    守护、也没有实际编译验证过。
    <br/>**修复**：按真实签名重写示例（构造数据源 → `HeadlessWorldBuilder.Build` 取
    `AnchorTable`/`Registry`/`ScenarioCatalog` → `ArenaSimulation.Run` → `SimReport
    .FromArenaReport` → `BaselineComparer.Compare`），新增
    `core/sim/tests/HeadlessReadmeExampleTests.cs`
    `ReadmeExample_HeadlessWorldBuilderThroughBaselineCompare_CompilesAndRuns`，逐字复刻这条修复
    后的代码路径并接入 `dotnet test Core.sln`——签名一旦再漂移，本测试先于任何人工核对之前编译
    失败，两份 README 顶部/对应小节新增"与本测试同步维护"的显式指引，不再依赖人工记得去翻文档
    比对签名。步骤 3a `FightRunner.Run(new FightRunnerOptions { /* ... */ })`
    本就是占位写法（字段全部省略），不含可编译的真实参数，不是本次要根治的对象，不在复刻范围
    （两份文档该行原样保留）。

## 深度复审领域 E 判断记录

47. **`FightRunner.FightAccumulator.IsLandedHit` 命中率统计未排除免疫吸收（E-M2 必须修）**：本类型
    判断记录"采样口径"一节称命中率数的是"多少次尝试里有多少次真正落地"，`IsLandedHit` 此前只读
    `ResolveResult.Hit`，未读 `ResolveResult.Immune`——一次被完全免疫吸收的攻击 `Hit` 仍可能是
    `Hit`/`Crit`/`Block`/`GlancingBlow`，`FinalAmount` 却恒为 0 且不触发 `combat.damage_dealt`
    事件，此前会被误计入 `PlayerLanded`/`CreatureLanded`，系统性高估命中率。嵌入数据集未配置任何
    伤害免疫光环，问题此前处于"正确但未被任何测试触达"的隐蔽状态。
    <br/>**修复**：`IsLandedHit` 改接收整个 `ResolveResult` 并在判定首位排除 `Immune=true`；
    `OnResolve` 两处调用点同步改传 `result`（而不是 `result.Hit`）。
    <br/>**验证**：新增 `FightRunnerTests.FightAccumulator_ImmuneResolve_CountsAttemptButNotLanded`
    ——直接构造 `FightRunner.FightAccumulator`，经其公开的 `CombatOptions.ResolveTrace` 回调手工喂入
    一条 `Immune=true` 的 `ResolveResult`，断言 `PlayerAttempts` 照常计数但 `PlayerLanded` 不递增；
    随后再喂一条 `Immune=false` 的正常命中，确认 `PlayerLanded` 恢复正常递增（防止修复反向回归成
    恒 false）。三份基线（`sim_baseline.ps1`/simrunner 全场景）不受影响——嵌入数据集本就不含免疫
    结算，修复前后命中率计数逐位相同。
48. **`CoverageSimulation` 三处离群值排序无并列 tie-break（E-S1 建议修，已采纳）**：`AnalyzeSkills`/
    `AnalyzeItems`/`AnalyzeCreatures` 此前用 `List<T>.Sort((a, b) => b.Deviation.CompareTo(a.Deviation))`
    ——不稳定排序且无次级排序键，与 `BaselineComparer.SortedRows()`"并列按 Path 用 `StringComparer
    .Ordinal` 兜底"的既有惯例不一致；技能/装备/生物里"完全合规、`Deviation` 恰好相等"（如多个装备
    模板 `Ratio` 都精确等于 1.0）的场景现实存在，并列项之间的相对顺序此前不保证跨机器/跨 .NET 版本
    可重现。
    <br/>**修复**：新增公开静态方法 `CoverageOutlierRow.SortByDeviationDescendingThenById`（按
    `Deviation` 降序、并列按 `Id.Value` 用 `StringComparer.Ordinal` 升序 tie-break），三处调用点
    统一改用它；连带把 `CoverageOutlierRow` 构造函数由 `internal` 放宽为 `public`（纯 ABI 新增，
    惯例同 `core/carriers/item/README.md`"选用公开重载而非 internal"判断记录——本仓库不对测试程序
    集声明 `InternalsVisibleTo`，`internal` 成员测试不可达）。
    <br/>**验证**：新增 `CoverageSimulationTests.SortByDeviationDescendingThenById_TiedDeviation
    _SortsByIdAscending`——手工构造含并列 `Deviation` 的行（含乱序/降序输入），断言排序结果按
    `Deviation` 降序、并列按 `Id` 升序稳定收敛，不依赖嵌入数据集是否恰好产生并列离群值这一脆弱
    前提。真实嵌入数据集的三份基线经本次修复后逐场景比对 `exceeded=0`（见门禁 G1）；若未来基线
    因真实并列项顺序变化需要 `--update-baseline` 更新，会在同一提交里说明，本次未发生。
49. **`sim.scenario.bandwidths` 键名拼写错误全链路静默失效（E-S2 建议修，已采纳）**：`bandwidths`
    在 schema 里登记为 `MapSchema.FreeKeyed`（自由字符串键，"本表不枚举合法键"），全链路（schema、
    `SimScenarioValidationRule`、`BaselineComparer.ResolveTolerance`、`GrowthSimulation`/
    `ArenaSimulation` 的 `ResolveBandwidths`）此前都只用 `TryGetValue` 静默回退默认值——任何一处
    拼错键名（如误把 `level_duration` 写成 `leve_duration`）都不会在任何环节报出诊断，内容作者
    永远不会知道自己配置的带宽从未生效。
    <br/>**修复**：新增 `SimBandwidthKeys.KnownKeys`（`BaselineComparer.cs`，与
    `BaselineCompareOptions.DefaultLeafBandwidthKeys` 的取值单一来源——直接取该映射表全部取值去重，
    不新开一张平行维护的键清单）；`SimScenarioValidationRule` 新增警告级检查
    `sim_scenario_bandwidth_key_unknown`（不可提升，本类型 `NonEscalatable` 因此由默认 `false`
    改为显式 `true`，同 `SimAnchorValidationRule` 判断记录同一处理口径——只影响本类产出的 Warning
    是否在 `WarningsBlock` 下阻断，不影响 `LevelCoverageCheck` 这条 Error 的阻断力）：`bandwidths`
    的键若不属于 `SimBandwidthKeys.KnownKeys`，报一条"未识别的带宽键，可能是拼写错误，本次不会
    生效"的诊断。已并入 `Presentation.Assembly.NumericValidationRuleCatalog`"仿真"分组（见
    `presentation/assembly/README.md` 对应判断记录），04 第 5 节分级表同步勘误一行，总数
    23→24（阻断 15，警告 9）。
    <br/>**验证**：新增 `SimSchemaTests.Scenario_MisspelledBandwidthKey_ReportsWarningNotBlocking`
    （拼写错误键名产出不阻断的警告，消息含具体键名）与
    `Scenario_AllKnownBandwidthKeys_NoUnknownBandwidthKeyWarning`（全部九个已知键的正例对照，确认
    修复本身不矫枉过正）。`data/_sample`/`games/_template`/嵌入数据集（`core/sim/tests/data`）已
    逐条核对，全部 `bandwidths` 键均落在已知集合内，`--strict` 下 warnings 仍为 0。
50. **深度复审 E-M1（presentation 层，非本模块直接改动，交叉记录）**：`Presentation.Assembly
    .NumericValidationRuleCatalog` 此前遗漏 `sim.anchor`/`sim.scenario` 三行检查（T-N6-8a 当时
    记录"目录不收录"），深度复审领域 E 发现该口径与 04 文档"23 行"不一致后已并入，详见
    `presentation/assembly/README.md` 对应判断记录与 `NumericValidationRuleCatalog.cs` 类型级
    判断记录——本模块（`Core.Sim`）自身除新增 `SimBandwidthKeys`（记录 49）外无需改动，三条 sim
    检查规则实现本身（`SimAnchorValidationRule`/`SimScenarioValidationRule`）不变。

## 消费方反馈第 45 条判断记录（2026-09-17）

51. **`ExpectedStatCalculator` 阻断态下不再抛异常**：构造函数此前一律用严格的 `view.Get`/
    `view.GetAll` 读取 `arch.class`/`stat.definition`/`stat.weight`/`item.slot_definition` 等表，
    `Compute` 内部经 `IBudgetSolver.Solve` 继续用同一个 `view`——registry 阻断态（全局级别、不按
    表/记录粒度）下即便触发阻断的记录与这些表完全无关，仍会抛 `InvalidOperationException`。
    <br/>**修复**：构造函数把入参 `view` 一律先包一层 `Core.Foundation.DataRegistry
    .TolerantRegistryView`，字段 `_view` 改持有包装后的引用，此后全程（包括 `Compute` 传给
    `IBudgetSolver.Solve` 的引用）只用它；新增只读属性 `IsDegraded`/`MissingTables`（实时读
    `_view` 的同名成员，不是构造期快照）。`arch.class` 记录本身"因阻断读不到"与"确实未登记"两种
    情形分开处理——前者不再抛 `ArgumentException`，改为职业相关字段全部按空/缺省处理；后者维持
    既有行为继续抛异常（调用方传了个不存在的职业 id，是用法错误）。`IBudgetSolver`/`BudgetSolver
    .Solve` 本身不改动（它同时服务 `EquipmentHost.ApplyAffixValues` 等运行期宿主，见
    `core/carriers/item/README.md` 消费方反馈第 45 条判断记录），本类型只是把自己私有持有的
    容错视图传给它，不影响运行期宿主用真实 view 直接调用的另一条路径。`AnchorTable` 本身不在本次
    改动范围（它是 `HeadlessWorldBuilder` 唯一的构造路径，调用方本就只在 `LoadAll` 通过校验之后
    才会构造它，不是本条反馈治理的"只读分析入口"）。
    <br/>**验证**：`ExpectedStatCalculatorTests` 新增用例——最小夹具（`arch.class`/
    `stat.definition`/`sim.anchor` 三张表 + 一条与之无关的坏引用触发阻断），断言构造与
    `Compute` 均不抛异常、结果与非阻断态逐位一致（`IsDegraded` 为 `false`），以及"职业 id 确实
    不存在"仍继续抛 `ArgumentException` 的回归对照。审计范围内 `core/sim/core
    /StandardPlayerBuilder.cs`（同样调用 `IBudgetSolver.Solve`、疑似同类只读分析场景）未纳入本次
    改动——超出反馈原文列出的入口清单，留待设计层确认是否需要一并处理。
