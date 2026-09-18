# L1 数值层 · archetype 职业与种族模板

职责：定义单位初始属性、技能、外形的模板（见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L1 模块表 `archetype` 行、
[00_架构总则.md](../../../architecture/00_架构总则.md) 第 3 节"职业与种族模板……原样保留"、
[07_载体层_物品生物物件.md](../../../architecture/07_载体层_物品生物物件.md) 第 5 节
"职业 = 主属性引用 + 资源类型引用 + 技能书 + 天赋树引用"）。

依赖：`core/foundation/common`（`Id`、`common/json`）、`core/foundation/event_bus`（`IEvent`、
`IEventBus`）、`core/foundation/data_registry`（`IDataRegistryView`、`DataRecord`、
`TableSchema`、`IValidationRule` 等）与 .NET 标准库；不引用任何引擎适配层实现、不使用系统
时间、不使用多线程、不使用系统级 `Random`、不使用反射。**并行开发期显式不引用**
`Core.Numbers.StatBlock`/`Core.Numbers.PowerSet` 的任何具体类型——写属性、注册资源、写派生系数
覆盖改由 `StatBaseWriter`/`StatModifierWriter`/`PowerRegistrar`/`DerivationCoefficientOverrideWriter`
具名委托注入（见 `contracts/ArchetypeWriters.cs`）。

**本模块及其测试内不出现任何具体游戏的职业/种族名称**（落地方案与分阶段计划.md T2-3 行显式
禁止），全部职业/种族的取名、数值完全由外部注入的 `arch.class`/`arch.race` 数据行决定，测试
只用 `arch.class.sample_a` 一类中性 id。

## 目录

```
archetype/
  README.md
  contracts/
    ArchSchemas.cs        arch.class / arch.race / arch.talent_tree 的 TableSchema（T-N1-4：
                           arch.class 新增可选 derivation_overrides 字段）
    Models.cs              ClassDefinition（T-N1-4 新增 DerivationOverrides 属性与配套构造重载）、
                            RaceDefinition、TalentNode、TalentTree、AppliedArchetype
    Events.cs               ArchetypeEventKeys、ArchetypeAppliedEvent
    ArchetypeWriters.cs     StatBaseWriter / StatModifierWriter / PowerRegistrar /
                            DerivationCoefficientOverrideWriter（T-N1-4 新增）具名委托
    IArchetypeRegistry.cs   IArchetypeRegistry
  core/
    ArchetypeRegistry.cs                        IArchetypeRegistry 默认实现（T-N1-4 新增构造重载与
                                                 ApplyTo 派生系数覆盖转发，见判断记录 8）
    ArchTalentTreeCycleValidationRule.cs         天赋树前置存在性 + 无环校验规则
    ArchClassDerivationOverrideValidationRule.cs T-N1-4 新增：derivation_overrides 边存在性校验规则
  schema/
    README.md               三张表的字段说明与判断记录
  tests/
    ArchetypeRegistryTests.cs
    ArchSchemaCoverageTests.cs
```

## 设计要点与判断记录

1. **三张表字段为实现期补录**：04 第 1.1 节只给出一句话描述，没有给出字段表；本模块按任务书
   T2-3 给出的最小字段集实现，取舍记录见 `schema/README.md`。

2. **跨模块引用字段暂不声明为 `Reference`**：`primary_stat`（→ `stat.definition`，
   `stat_block` 模块）、`power_types`（→ `arch.power.*`，`power_set` 模块）、`skill_book_ref`
   （→ `skill.book.*`，L2 `skill` 模块）、`passive_auras`（→ `skill.aura.*`）均指向本任务并行
   开发或尚未实现的模块表；本模块把它们声明为普通 `Id`/`IdList`（只做格式校验），不声明为
   `FieldKind.Reference`，避免测试期因为这些表不存在而被判定 `reference_integrity` 错误。待
   对应模块登记 schema 后，集成方可用 `IDataRegistry.DeclareReference` 动态补上引用完整性
   检查，不需要改动本模块的 `TableSchema`（`DeclareReference` 正是为这种场景设计的扩展点，见
   `IDataRegistry.cs` 文档）。`talent_tree_ref`/`level_curve_ref` 引用的表都在本任务范围内
   （`arch.talent_tree`、`prog.level_curve`），直接声明为 `Reference`，`level_curve_ref`
   指向不存在的曲线时会被 04 内置的 `reference_integrity` 检查拦截（验收标准原文）。

3. **`ApplyTo` 的调用顺序固定为 base → race flat → race passive_auras → powers → 事件**：
   任务书原文"按顺序 StatBaseWriter 写 base_stats → StatModifierWriter 写种族修正 →
   PowerRegistrar 注册资源 → 发 archetype.applied"；W1 收边补齐在种族修正之后、注册资源之前
   插入被动光环施加（见判断记录 7）。`raceId` 为 `null` 时完全跳过种族修正与被动光环这两步
   （不调用 `StatModifierWriter`/`AuraApplier`），不是"调用一次值为 0 的修正"。

4. **种族修正的 `sourceId` 取种族 id 本身**：见 `ArchetypeRegistry` 类文档判断记录——任务书
   没有指定这个来源 id 具体是什么，选用种族 id 本身语义最直接、也便于调用方按种族切换时
   知道该用哪个 sourceId 去移除旧修正。

5. **`ApplyTo` 不处理天赋树**：任务书对 `ApplyTo` 的描述只涉及 base_stats/种族修正/资源注册/
   事件，`AppliedArchetype` 只透传 `LevelCurveRef`；天赋点的分配、`arch.talent_tree` 节点的
   查询走 `GetTalentTree`，由上层（如未来的天赋系统）自行消费，不在本模块范围内。

6. **天赋树前置存在性 + 无环校验是数据校验层的扩展规则**：`ArchTalentTreeCycleValidationRule`
   （`IValidationRule` 扩展点，对应 04 第 5 节"循环引用检测：……如天赋前置……不得成环"）需要
   调用方 `RegisterValidationRule` 后随 `DataRegistry.LoadAll`/`Validate` 生效；
   `ArchetypeRegistry` 构造期解析 `nodes` 时只做"结构合法"的最小检查（节点必须是对象、必须有
   `id`），不重复做前置存在性/无环检查——那需要遍历同一张表的其它记录做跨行分析，属于
   `IValidationRule` 的职责边界，不适合放进单条记录的构造期解析。

7. **W1 收边补齐：`race.passive_auras` 经注入的 `AuraApplier` 委托施加**（A3 审计 #8）——
   此前"本模块只保存不应用"的前提是"L2 `skill` 模块尚未实现"，该前提已过期（`core/rules/skill`
   目前已完整实现光环系统）。构造函数新增可选参数 `AuraApplier? auraApplier = null`：非空时
   `ApplyTo` 对 `race.passive_auras` 逐个调用（`sourceId` 固定用种族 id 本身，与
   `StatModifierWriter` 同一选取理由，见判断记录 4）；为 null（未装配，向后兼容）时行为与本次
   改动之前完全一致，仍旧只保存不应用。真正的接线（把 `IEffectSink.ApplyAura` 转发进来）在
   `core/rules/assembly/RulesAssembly.cs` 完成，本模块本身不引用 `core/rules/skill` 任何类型。

8. **T-N1-4：`arch.class.derivation_overrides` 经 `DerivationCoefficientOverrideWriter` 转发，
   在 `ApplyTo` 的 base_stats 写入之后、种族修正/被动光环/资源注册之前**（ADR-0030 决策 2"职业
   模板可覆盖派生系数"）——子结构选 `Array<{stat, source, coefficient}>` 而不是 `Map` 的判断记录
   见 `contracts/ArchSchemas.cs` 的 `DerivationOverrideEntrySchema` 注释；`stat` 指"被覆盖系数的
   目标派生属性"、`source` 指"该属性 `derived_from` 里被覆盖的那一条来源"，两个字段名均经设计层
   裁定（2026-09-14）：采纳。委托为 null（既有六参构造，向后兼容）时跳过这一步，同
   `AuraApplier`/判断记录 7 的既有取舍；非 null 时每次 `ApplyTo` 都会用当前职业的完整覆盖列表
   （可能为空）调用一次——**全量替换语义**，由接收方（`StatHost.SetDerivationCoefficientOverrides`）
   负责"先清旧覆盖再写新覆盖"。换职业/读档恢复不走 `ApplyTo`，走
   `Core.Rules.Assembly.RulesAssembly.ReloadArchetypeAndRace`——该方法直接持有 `StatHost` 具体
   类型，不经本委托，直接调用 `Stats.SetDerivationCoefficientOverrides`（同该方法对
   `Stats.ResetBase`/`Stats.SetBase` 的既有用法），详见该方法源码判断记录。覆盖是否指向
   `stat.definition` 中真实存在的 `(stat, source)` 派生边，由内容校验阶段的
   `ArchClassDerivationOverrideValidationRule` 保证，不是 `ArchetypeRegistry`/`StatHost` 运行时的
   职责——两者对不存在的边都是静默安全忽略（见 `StatHost.ComputeDerivedBase` 判断记录）。禁止事项
   核对：本任务全程未在 `ArchetypeRegistry` 出现任何具体职业名，测试用
   `arch.class.n1_4_warrior_like`/`arch.class.n1_4_mage_like` 一类中性 id（同判断记录顶部
   "sample_a" 惯例，`warrior_like`/`mage_like` 只是让测试断言的意图更直观，不是真实游戏职业名）。

9. **消费方反馈第 54～58 条（2026-09-18）回复要点**（详见
   `architecture/落地计划/消费方反馈-2026-09-18-编辑器-第54-58条.md`）：
   - **第 58 条：`arch.talent_tree.nodes[].id` 有意登记为 `FieldKind.String` 而非
     `FieldKind.Id`**，与 `dialog.story_tree.nodes[].id`（`FieldKind.Id`，受 `field_id_format`
     点分格式约束）不同——天赋树节点 id 只在同一棵树内以字符串比较，不要求全局 `Id` 格式，运行时
     `ArchetypeRegistry.ParseTalentTree` 也只检查非空字符串，不做 `CommonId.TryParse` 校验；把
     `FieldKind` 改成 `Id` 是一次真正的行为收紧（对既有数据新增格式约束），`core/sim/tests/data`
     现有节点 id（如 `"node_a"`）不含点分格式，会直接击穿 `validate_data.py --strict` 零警告
     基线，本次不做，只在 `contracts/ArchSchemas.cs` 该字段 `description` 与本条判断记录里补齐
     取舍说明（纯文档，不改 `FieldKind`/不改数据）。
   - **第 56 条：`arch.talent_tree` 孤立节点（无前置也未被任何其它节点引用）不新增校验警告**——
     天赋树允许多个并列根节点（`prerequisites=[]` 可以有多个，是合法的并列分支设计），孤立节点是
     正常内容形态，不是遗漏；需要"孤立/入度出度"一类图结构信息的内容工具改用
     `Core.Foundation.DataRegistry.ContentGraphAnalyzer`（新增的公开只读图分析入口，`story_tree`/
     `quest_prerequisite`/`talent_tree` 三类图统一提供不可达/孤立/入度出度/环结果），本模块不
     重复实现、也不产出等价 `story_tree_node_unreachable` 的警告级检查。
   - **第 57 条：`talent_node_id`/`talent_prerequisite_missing`/`talent_prerequisite_cycle` 三项
     图诊断补齐结构化定位**——前两项 `Field` 补上具体下标路径（`nodes[i].id`/
     `nodes[i].prerequisites[j]`），后者按环上出现顺序把节点 id 填进新增的
     `ValidationIssue.AffectedNodeIds`（ABI 只新增，见该属性判断记录）；成环检测算法一并勘误为
     与 `story_tree_cycle`/`quest_prerequisite_cycle` 同款的单次共享状态三色标记 DFS + 显式路径
     记录（原实现每个候选起点各自新开一套 `visiting`/`visited` 集合、且按 `Dictionary.Keys`
     顺序选取候选起点，不满足 AGENTS.md"不依赖字典枚举顺序"），详见
     `ArchTalentTreeCycleValidationRule` 类型判断记录；`talent_prerequisite_cycle` 消息文本顺带
     补上完整链路（原消息只报告 DFS 起点），无既有测试断言该文本，判定为无风险勘误。
10. **消费方反馈第 56 条追问（2026-09-18）：新增默认关闭的可选诊断 `TalentTreeIsolationRule`**
    （`core/numbers/archetype/core/TalentTreeIsolationRule.cs`，检查名 `talent_node_isolated`，
    Warning，`NonEscalatable`）——判断记录 9 第 56 条已定性"孤立节点（含并列根节点）是合法内容形态、
    不产出等价 `story_tree_node_unreachable` 的警告级检查"，本条不推翻该结论，只是回应"既然合法，
    能否仍提供一个可选的展示性提示"的追问：以
    `Presentation.Assembly.ContentValidationOptions.EnableGraphIsolationDiagnostics`（默认 `false`）
    为唯一开关，显式打开时才注册，默认路径（三套官方数据根、`toolchain/validator` 未传
    `--enable-graph-isolation`）零行为变化。对每棵 `arch.talent_tree` 独立取 `nodes[].id`/
    `nodes[].prerequisites[]` 构图（同 `ArchTalentTreeCycleValidationRule` 的手工 JSON 解析手法，不
    依赖 `FieldKind.Id`），复用 `ContentGraphAnalyzer.Analyze` 求孤立节点，仅当单棵树节点数 ≥2 时才报
    （单节点树没有"孤立"这一概念）；`Field` 填 `nodes[i]` 下标路径，`AffectedNodeIds` 填孤立节点自身
    id。**测试放置的架构分层考量**：`core/numbers` 的测试工程（`Tests.Numbers.csproj`）不引用
    `presentation/Presentation.Common.csproj`（与 `core/gameplay` 的测试工程不同，后者经 CORE114-04
    先例已建立该引用），本规则自身触发/不触发逻辑测试
    （`tests/TalentTreeIsolationRuleTests.cs`）改用手工构造裁剪版 `DataRegistry`（同
    `ArchTalentTreeCycleValidationRuleTests.cs` 既有手法，只登记 `ArchSchemas.TalentTree` 一张表），
    不新增跨层引用；"开关关闭时不注册"这一装配层行为覆盖在
    `presentation/assembly/tests/ContentValidationAssemblyTests.cs`。

## 不负责什么

- 不实现属性聚合/资源池/派生系数覆盖的具体运算，只通过 `StatBaseWriter`/`StatModifierWriter`/
  `PowerRegistrar`/`AuraApplier`/`DerivationCoefficientOverrideWriter` 五个具名委托与外界交互
  （见判断记录 2、7、8）。
- 不处理天赋点消耗/学习流程，只提供 `GetTalentTree` 只读查询（见判断记录 5）。
