# L1 数值层 · progression 等级与经验

职责：定义等级曲线、经验获取与升级判定（见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L1 模块表 `progression` 行、
[00_架构总则.md](../../../architecture/00_架构总则.md) 第 3 节"等级与经验……原样保留"、
[04_数据与内容管线.md](../../../architecture/04_数据与内容管线.md) 第 1.1 节表清单）。

依赖：`core/foundation/common`（`Id`、`common/json`）、`core/foundation/event_bus`（`IEvent`、
`IEventBus`）、`core/foundation/data_registry`（`IDataRegistryView`、`DataRecord`、
`TableSchema`、`IValidationRule` 等）与 .NET 标准库；不引用任何引擎适配层实现、不使用系统
时间、不使用多线程、不使用系统级 `Random`、不使用反射。**并行开发期显式不引用**
`Core.Numbers.StatBlock`/`Core.Numbers.PowerSet` 的任何具体类型——升级带来的属性成长改由
`StatModifierWriter`/`StatModifierRemover` 具名委托注入（见 `contracts/ProgressionWriters.cs`），
由上层（游戏初始化代码）把真正的 `IStatHost` 实现接进来。

## 目录

```
progression/
  README.md
  contracts/
    ProgSchemas.cs             prog.level_curve / prog.xp_source / prog.xp_base_curve 的 TableSchema
    ProgressionOptions.cs      构造期口味配置契约壳（T-N4-1 新增契约壳，T-N4-2 起真正消费 MaxLevel/
                                ExtraXpMultiplierProvider；T-N4-3 新增 KillXpSourceId/
                                DiscoveryXpSourceId，本模块不消费，见"T-N4-3"一节；T-N4-4 新增
                                QuestXpSourceId，ExtraXpMultiplierProvider 委托签名扩展为
                                (unitId, sourceId, tierId)，见"T-N4-4"一节）
    XpContext.cs               grantXp 的当量上下文（T-N4-2 新增：sourceLevel/tierId/equivalent）
    Events.cs                  ProgressionEventKeys、LevelUpEvent、XpGainedEvent
    ProgressionWriters.cs      StatModifierWriter / StatModifierRemover 具名委托
    IProgressionDiagnostics.cs 诊断出口
    IProgressionHost.cs        IProgressionHost（T-N4-2 新增 GrantXp 默认接口成员；T-N4-4 新增
                                HasXpSource 默认接口成员）
  core/
    ProgressionHost.cs             IProgressionHost 默认实现
    ProgressionPersistable.cs      player.progression 存档段（静态工厂，惯例同 core/carriers/unit
                                    的 UnitPersistable 写法，W1 收边补齐，A4 审计 F1）
    ProgLevelCurveValidationRule.cs prog.level_curve 的 entries 连续性校验规则
    InMemoryProgressionDiagnostics.cs
  schema/
    README.md                  两张表的字段说明与判断记录
  tests/
    ProgressionHostTests.cs
```

## 设计要点与判断记录

1. **`prog.level_curve`/`prog.xp_source` 字段为实现期补录**：04 第 1.1 节只给出这两张表的
   一句话描述，没有给出字段表；本模块按任务书 T2-3 给出的最小字段集实现，取舍记录见
   `schema/README.md`。

2. **`AddXp` 直接收最终经验数值，`GrantFromSource` 才按 `prog.xp_source` 换算**：任务书显式
   拍板"`AddXp` 直接收最终数值；另提供 `GrantFromSource(unitId, xpSourceId,
   multiplier=1)` 按表计算"——`GrantFromSource` 算出 `base_xp × weight × multiplier`
   （四舍五入到 `long`，负值钳为 0）后转发给 `AddXp`。`prog.xp_source.condition`
   （Expr 字段）本任务只登记字段类型供未来内容校验使用，`IProgressionHost` 不对它求值
   （任务书原文"本任务只登记字段类型，不求值"）。

3. **一次 `AddXp` 可连跨多级，每跨一级发一次 `LevelUpEvent`，成长只在跨级全部处理完后写入一次**：
   `ProgressionHost.AddXp` 用 `while` 循环逐级消耗经验并逐级发事件，循环结束后才调用一次
   `StatModifierRemover` + 若干次 `StatModifierWriter`，写入"从 2 级到最终等级"的累计成长
   （1 级无成长，任务书原文）。这样"写入一次"与"每级发一次事件"是两件独立的事情，不会出现
   跨两级却把成长值分两次写、后一次覆盖前一次只剩最后一级增量的错误。
   **N08 收边补齐（外部审计 68c9bed）：`while` 循环内先提交 `unit.Level = newLevel`，再
   `PublishImmediate(LevelUpEvent)`**——原实现先发布事件、后赋值，`PublishImmediate` 是同步
   派发，事件处理器内如果不读事件自带的 `NewLevel` 字段、而是反查 `GetLevel(unitId)`（如按等级
   重算派生属性的消费者），会读到升级前的旧等级，直到下一次别的属性写入才可能被动补救。事件
   携带的 `OldLevel`/`NewLevel` 字段值本身不变，只调整"字段赋值"与"发布事件"两个语句的先后
   顺序。见 `ProgressionHostTests.cs`
   （`AddXp_LevelUpHandler_ReadsGetLevel_SeesNewLevelAlready`/
   `AddXp_CrossesTwoLevels_EachHandlerInvocation_SeesLevelAtThatPointInTime`）。

4. **成长写入的来源 id 固定为 `prog.growth`，`RegisterUnit` 不隐式写入成长**：见
   `ProgressionHost` 类文档判断记录——`RegisterUnit` 定位是"全新单位从起始等级开始"，不应
   用曲线重算结果覆盖存档已有的属性值；只有真正经由 `AddXp`/`GrantFromSource` 触发的升级
   路径才会调用成长写入委托。
   **W1 收边补齐（A4 审计 F1）**：本条判断记录写下时"读档到已知等级"这一场景实际上从未真正
   落地过——10 第 2.5 节"属性快照默认不存"意味着读档后除非有人重新写入成长修正，否则等级
   越高的单位属性会缺失越多层成长（这正是 A4 审计发现的存档缺口本身）。现在
   `ProgressionPersistable`/`ProgressionHost.RestoreState` 补上了这条路径：读档专用，允许指定
   非零 `xp`，且**会**调用 `ApplyGrowth` 按 1..level 重新聚合成长——与 `RegisterUnit`（全新单位，
   `xp` 恒 0，不重算成长）是两个语义不同的入口，不冲突。
   **R08 收口（外部审计 5e779c6，P2，成立）**：`RestoreState` 补上成长修正重聚合后仍遗漏一环——
   它不经过 `AddXp` 的正常升级路径，不发布 `LevelUpEvent`，任何按 `progression.level_up` 才失效
   重算的下游缓存（当前唯一消费方：`core/numbers/stat_block.StatHost.RecomputeRatingStats`，见
   06 第 1.1 节评级换算）读档后会一直停留在读档前的等级算出的值（外部审计复现）。现在
   `RestoreState` 结尾额外发布一条新增的 `ProgressionRestoredEvent`（`progression.state_restored`）
   ——不复用 `LevelUpEvent`：读档不是"发生了一次升级"，没有真实的"旧等级"，且可能一次性跨越
   多级，复用会产生一串语义不自洽的虚假升级事件（见 `Events.cs` 判断记录）。
   `core/rules/assembly.RulesAssembly` 订阅本事件同样调用 `Stats.RecomputeRatingStats`（与订阅
   `LevelUpEvent` 那一条完全相同的方法，只是多一个触发来源）。见 `ProgressionHost.RestoreState`
   判断记录、`core/rules/tests/Integration/ProgressionRestoreRatingRecomputeTests.cs`。

5. **满级后 `AddXp` 整笔丢弃、记诊断、不发 `XpGainedEvent`**：任务书"到 max_level
   后经验不再累积（丢弃并记诊断）"。区分两种满级丢弃场景：调用时已在满级（整笔丢弃，不发
   事件）与本次调用内升到满级但仍有残余经验（残余部分丢弃，但本次调用已经发过
   `XpGainedEvent`/已跨的每一级 `LevelUpEvent`，不重复处理）。

6. **`entries` 连续性校验分两层**：数据校验层的 `ProgLevelCurveValidationRule`（`IValidationRule`
   扩展点，需调用方 `RegisterValidationRule` 后随 `DataRegistry.LoadAll`/`Validate` 生效，对应
   04 第 5 节"循环引用检测"一类由内容模块自行登记的检查项）与 `ProgressionHost` 构造期的防御性
   重复校验（与 `L10nHost.ValidateNoFallbackCycle` 同一惯例：即便调用方忘记注册校验规则，
   `ProgressionHost` 本身也不会把结构非法的曲线数据带入运行时）。

7. **AUD-02 根治（外部审核第九轮，P2，architecture/落地计划/audit-85f1f4f-20260908）：
   `ProgressionPersistable.Load` 对本段整体缺失（`JsonNull`）的处理，从 no-op（保留读档前的
   运行期等级/经验）改为重置到默认态（1 级、0 经验）**：修复前真实场景：同一宿主先后加载两个
   存档槽，缺本段的旧档不会清掉前一个槽留下的等级，违反 10 第 3 节"缺失段语义"合同。曲线 id
   （`curve_id`）本身不属于"存档数据"，是内容/装配阶段已经经 `RegisterUnit` 确定的职业/单位归属，
   缺段清空只清等级/经验这两个真正的存档字段，沿用 `Save()` 当前已登记的曲线（复用 `Save()`
   本身取回，不新增契约方法）。该单位从未经 `RegisterUnit` 注册（例如所属职业没有配置
   `level_curve_ref`，见判断记录 4 引用的 `RulesAssembly.RegisterUnit` 判断记录）时，`Save()`
   本身会抛 `ArgumentException`（判断记录见 `Save_UnregisteredUnit_Throws`）——本条根治捕获这一
   前提缺失的情形，直接保持未注册、不代为注册一个调用方从未选择的曲线，不抛异常。见
   `ProgressionPersistableTests.Load_NullData_ResetsRegisteredUnitToLevelOneAndZeroXp`（已注册
   单位分支）、`Load_NullData_LeavesUnitUnregistered`（未注册单位分支，既有测试）。

8. **消费方反馈第 36 条根治：新增 `ApplyGrowthToCurrentLevel`，成长统一只由本模块的修正承载**：
   `core/carriers/creature.CreatureFactory.ApplyStats` 此前自行重复解析 `prog.level_curve`，把
   "2 级到出生等级"的累计成长直接叠进 `IStatHost.SetBase` 写的基础值；该单位一旦经 `AddXp` 真实
   升级，`ApplyGrowth`（判断记录 3）又会把"2 级到新等级"整段成长重算并整体覆盖写入修正——两处各自
   独立维护"从 2 级累加"这同一段成长，`[2..出生等级]` 因此被基础值与修正各计了一次（真实探针：
   出生等级 2、出生 strength 7，升到 3 级实测 11，应为 9）。根治为在 `IProgressionHost` 上新增
   `ApplyGrowthToCurrentLevel(unitId)`——内部直接调用与 `AddXp`/`RestoreState` 末尾完全相同的
   `ApplyGrowth` 私有方法（先 `StatModifierRemover` 清空旧 `prog.growth` 修正、再按当前等级整体
   重算写入），不新写一份公式；`CreatureFactory.ApplyStats` 相应改为只写
   `base_stats × tier.stat_multiplier`，`Spawn` 在 `RegisterUnit` 之后紧接着调用
   `ApplyGrowthToCurrentLevel` 一次性补上"2 级到出生等级"的成长（出生等级 1、无曲线的既有示例
   数据下这一步是空操作，行为不变）。本方法刻意不发任何事件——语义上既不是"升级"（没有真实的
   等级提升，见判断记录 3）也不是"读档恢复"（`ProgressionRestoredEvent` 的语义是"状态被存档
   覆盖"，见判断记录 4/`Events.cs` 判断记录"语义诚实"，硬套给一次全新生成反而语义不诚实），典型
   调用方（`CreatureFactory.Spawn`）本身就在生成流程内，不需要额外的失效重算通知。见
   `core/numbers/progression/tests/ProgressionHostTests.cs`（`ApplyGrowthToCurrentLevel` 相关用例）、
   `core/carriers/creature/tests/CreatureFactoryTests.cs`（`E36_*`）。

## CORE-170-02 根治（第十轮外部审计，P2，architecture/落地计划/audit-8160178-20260908）

`ProgressionHost` 是单位等级的唯一权威，但修复前 `AddXp`/`RestoreState` 只更新本模块内部的
`UnitState.Level`，从不同步任何外部实体字段；生产装配构造玩家实体时也从未写入实体自身的
`Level` 字段（构造函数默认值 1），只把等级传给 `RulesAssembly.RegisterUnit`。`Core.Carriers.
Unit.WorldUnitAccess.GetLevel`（`IUnitAccess` 的真实实现，供装备需求判断等运行期消费者调用）
直接读实体字段，与本模块内部权威等级各自独立、互不同步——真实探针复现 `rules_progression_
level=2;entity_level=1`，等级 2 的装备需求判断因此读到过期的实体等级 1，返回
`RequirementNotMet`。

根治：新增具名委托 `LevelSync`（`core/numbers/progression/contracts/ProgressionWriters.cs`，
惯例同 `StatModifierWriter`——并行开发期不引用具体实体类型，通过具名委托注入），`ProgressionHost`
新增可选构造参数 `levelSync`，在三个等级会变化/确立的时机（`RegisterUnit` 首次注册、`AddXp`
升级、`RestoreState` 读档恢复）末尾统一调用；未注入时（`null`，多数测试用的最小假实现）行为与
本次改动之前完全一致。`RulesAssembly` 新增同名可选构造参数原样转发；`CarriersAssembly` 传入
`Core.Carriers.Unit.WorldUnitAccess.SetLevel`（新增方法，不在 `IUnitAccess` 接口上，惯例同
`Revive`——只供组合根装配期的委托闭包调用）——单位存在于 `IWorldSim` 时同步写入实体字段，单位
尚未注册到世界时安全 no-op。`WorldUnitAccess.GetLevel` 本身不改动，仍然直接读实体字段，只是
自此这个字段恒与 `ProgressionHost.GetLevel` 一致，不需要反查 `core/numbers/progression`（那会
要求 `core/carriers/unit` 反向依赖具体实现，且对没有配置进度曲线的单位——多数 NPC/怪物——
`GetLevel` 该读什么曲线本就无从谈起）。见 `Tests.Gameplay.Assembly.
CORE_170_02_ProgressionLevelSyncTests`（真实 `GameplayAssembly` 多级 `AddXp`、读档、
`WorldUnitAccess.GetLevel`、等级需求装备四者一致性）。

## T-N4-1（分阶段落地计划；ADR-0033 决策 2/3/4；06 第 2.5 节）：新字段、新表、`ProgressionOptions` 契约壳

1. **`prog.level_curve.talent_points`（可选，缺省 0）**：该级获得的天赋点数（ADR-0033 决策 2）。
   纯新增可选字段，`currentSchemaVersion` 不递增。消费实现（写入天赋点余额）留 T-N4-2，本任务
   只登记数据形状。

2. **`prog.xp_source` 新增 `kind`/`base_curve_ref`/`level_diff_ref`/`once_key` 四个可选字段**
   （ADR-0033 决策 3）：`base_curve_ref` 引用新表 `prog.xp_base_curve`（存在时优先于
   `base_xp`/`weight`）；`level_diff_ref` 引用既有 `combat.level_diff_table`；`once_key` 供
   `kind=discovery` 的探索来源使用。`base_xp`/`weight` 标**废弃**（拍板 4："保留一个版本周期"，
   本任务不删除、只在字段描述追加废弃说明）。`kind` 登记为可选（非必填）——判断记录见
   `schema/README.md`"判断记录（`kind` 登记为可选而非必填）"，本任务验收标准显式要求"只有
   `base_xp`/`weight` 的来源加载 0 error"，若 `kind` 必填会与该标准冲突，标"待设计层确认"。

3. **新表 `prog.xp_base_curve`**（落地改动点清单第 10 节拍板 5"表名 `prog.xp_base_curve`，归 L1
   progression"）：击杀基数曲线，04 第 3.6 节通用断点表形态（横轴 `Level`），单调有限阻断校验
   自动生效，不需要专属校验规则。**契约疑点**：04 第 1.1 节表清单当前尚未有本表的登记行（拍板
   已定，04 正文落后一步），本任务不改架构文档，留阶段收尾文档回填批次同步，详见
   `contracts/ProgSchemas.cs` 类型注释"契约疑点上报"。

4. **`ProgressionOptions`（`contracts/ProgressionOptions.cs`，新增）**：本模块构造期口味配置的
   契约壳——`MaxLevel`、`RefillOnLevelUp`、`DefaultOnceKeyPrefix`、
   `ExtraXpMultiplierProvider`（`ProgressionXpMultiplierProvider` 委托）四个字段，均只登记默认值
   与判断记录，**本任务不消费**：`ProgressionHost` 尚未提供接受本类型的构造重载。**契约疑点**：
   分阶段落地计划 T-N4-4/T-N4-5 任务行的"涉及文件"清单均未列出本文件，但本任务书原文明确要求
   先登记"XpMultiplier 注入（T-N4-4）"与"升级回满开关（T-N4-5）"两项字段——已按要求落地，若
   后续任务发现字段设计与实际消费方式不符，以彼时任务书与设计层裁定为准调整，详见该类型注释
   "契约疑点上报"。

## T-N4-2（分阶段落地计划；ADR-0033 决策 1/3/9；06 第 2.5 节）：`grantXp(XpContext)`、满级归零、`ProgressionOptions` 接入

1. **新增 `XpContext`（`contracts/XpContext.cs`）与 `IProgressionHost.GrantXp(unitId, sourceId,
   context)`**：06 第 2.5 节契约原文的三种来源统一入口，返回实际入账值（`long`）。默认接口方法
   （惯例同 `ApplyGrowthToCurrentLevel`），默认体固定返回 0，`ProgressionHost` 显式覆盖为真正实现；
   既有测试假实现（`FakeProgressionHost` 一类）不因新增本成员而编译失败。**禁止事项对照**：
   `GrantFromSource` 未被删除，仍是公开方法，只是内部改为与 `GrantXp` 共用同一条"应用倍率 → 加
   经验 → 升级循环 → 事件"路径（`ProgressionHost.AddXpCore`）。

2. **三种来源公式**（ADR-0033 决策 3）：`kind=kill`——`baseAmount × (ExtraXpMultiplierProvider ?? 1)
   × ΔFactor`；`kind=quest`——`(equivalent ?? 1) × baseAmount × ΔFactor`；`kind=discovery`——
   `(equivalent ?? 1) × baseAmount`（刻意不接 `ΔFactor`，即便该来源同时登记了 `level_diff_ref` 也
   不生效）。`baseAmount = prog.xp_base_curve[base_curve_ref].Evaluate(context.SourceLevel)`；
   `ΔFactor`：`level_diff_ref` 未登记时恒 1，否则查 `combat.level_diff_table[level_diff_ref]
   .xp_factor.Evaluate(Δ)`，`Δ = context.SourceLevel − 领取者当前有效等级`。**判断记录（不引用
   `Core.Rules.Combat.LevelDiffTable` 类型）**：01 把 progression 登记为 L1、combat 登记为 L2，
   L1 反向依赖 L2 具体类型会倒置分层；`ProgressionHost` 改用
   `Core.Foundation.DataRegistry.CurveSchema.ReadBreakpoints` 直接从原始 `DataRecord` 读取
   `xp_factor` 字段，不经 `Core.Rules.Combat` 程序集（同 `Tests.Numbers.csproj` 不引用
   `Core.Rules` 的既有约束）。该表与 `prog.xp_base_curve` 都是可选表（未装配 combat 模块/未接数据
   时留空字典，退化为"Δ 系数恒 1"，不抛异常）。**待设计层确认**：`kind` 未登记时兜底按 `kill`
   处理（06/ADR 均未给出字面结论，见 `schema/README.md`"T-N4-2 补记"）。

3. **`GetXpToNext`/`AddXp`/`GrantXp` 共用"有效满级"判定（`ProgressionHost.GetEffectiveMaxLevel`）**：
   `ProgressionOptions.MaxLevel` 为 0（新缺省）时等于单位绑定曲线自身的 `max_level`；为正值时取
   该值与曲线 `max_level` 的较小者（只能收紧，不能放宽到超出曲线数据）。`GetXpToNext` 在有效满级
   显式返回 0（不再只依赖"曲线最后一条记录的 `xp_to_next` 恰好是 0"这条数据约定——收紧场景下
   有效满级可能落在曲线中间某一级，那一级的 `xp_to_next` 通常非零，仍必须返回 0）；`AddXp`/
   `GrantXp`/`GrantFromSource` 共用的核心实现（`AddXpCore`）在有效满级整笔丢弃、记诊断、不发
   `XpGainedEvent`（ADR-0033 决策 9）。

4. **`ProgressionHost` 新增接受 `ProgressionOptions?` 的构造重载**：旧构造函数（不带该参数）转发
   `null` 到新重载，新重载内部 `options ?? new ProgressionOptions()`——两者共用同一份初始化逻辑，
   不是并行路径。`ProgressionOptions.MaxLevel` 默认值同步由 T-N4-1 的 `1` 改为 `0`（该字段此前只是
   登记壳、不驱动任何判定；本任务开始真正消费，默认值必须同时修正，否则任何未显式配置的调用方会
   被意外收紧到 1 级封顶，见 `ProgressionOptions.cs` 该字段"变更记录"）。

5. **`GrantFromSource` 旧字段兼容（拍板 4）**：来源没有 `base_curve_ref` 时逐位保留 T-N4-2 之前的
   旧算法 `base_xp × weight × multiplier`（回归测试
   `GrantFromSource_ComputesAmountFromBaseXpWeightAndMultiplier` 锁死）；存在 `base_curve_ref` 时
   改走曲线折算（新增测试
   `GrantFromSource_SourceHasBothLegacyFieldsAndBaseCurveRef_UsesCurveNotLegacyFormula` 验证"以
   曲线为准"）。**契约疑点上报/临时判断**：`GrantFromSource` 旧签名没有"来源等级"参数，本任务
   走曲线分支时按"该来源与领取者当前等级相同"处理（Δ 恒从 0 起算），`multiplier` 直接相乘、不经
   `ExtraXpMultiplierProvider` 钩子；真正需要按怪物/任务/区域等级折算的场景应改走新
   `GrantXp` 显式传入 `XpContext.SourceLevel`（T-N4-3/T-N4-4 的击杀/任务/探索监听器按此收敛），详
   见 `IProgressionHost.GrantFromSource` 判断记录。

6. **契约疑点上报：`talent_points` 消费不在本任务范围内**：T-N4-1 落地时在 `schema/README.md`/
   `ProgSchemas.cs` 留了"消费实现（写入天赋点余额）留 T-N4-2"的前瞻记录，但分阶段落地计划正式
   的 T-N4-2 任务行与本任务实际收到的派发任务书均只列 `grantXp(XpContext)`/`GetXpToNext` 满级
   归零/`XpContext` 结构三项，未提及天赋点，也没有给出"天赋点余额"应挂在哪个契约面的字面结论
   （新查询方法？新事件？游戏层自行订阅 `progression.level_up` 累加？）。本任务不擅自新增这类
   未声明的契约面，留待设计层重新拆分派发，见 `schema/README.md` 同名小节。

## T-N4-3（分阶段落地计划；ADR-0033 决策 3；06 第 2.5 节）：`ProgressionOptions` 新增
`KillXpSourceId`/`DiscoveryXpSourceId`

`ProgressionOptions.cs` 新增两个可空 `Id?` 字段——`KillXpSourceId`/`DiscoveryXpSourceId`（默认
`null`）。**契约疑点上报**：06 第 2.5 节/ADR-0033 均未给出"三种来源各用哪一条固定
`prog.xp_source` 记录 id"的字面结论，本模块本身不消费这两个字段（三种来源公式只依赖调用时传入
的 `XpContext`，与"来源 id 叫什么"无关，见 `IProgressionHost.GrantXp` 判断记录）——真正的消费方
是 T-N4-3 新增的 `core/gameplay/progression_bridge` 模块两个监听器（`CreatureDeathXpListener`/
`AreaTriggerDiscoveryXpListener`）：未显式传入 `ProgressionOptions`（或传入但字段为 `null`）时，
两个监听器各自退到自己的约定 id（`prog.xp_source.kill`/`prog.xp_source.discovery`，见各自类型
`DefaultKillXpSourceId`/`DefaultDiscoveryXpSourceId` 静态字段）。本模块只负责登记字段与判断记录，
详见 `core/gameplay/progression_bridge/README.md` 判断记录 5。

## T-N4-4（分阶段落地计划；ADR-0033 决策 4；附带任务设计层裁定）：`ExtraXpMultiplierProvider` 签名扩展、`QuestXpSourceId`、`HasXpSource`

1. **`ProgressionXpMultiplierProvider` 委托签名扩展为 `(unitId, sourceId, tierId) → 倍率`**：
   T-N4-1/T-N4-2 落地时签名只有 `(unitId, sourceId)`，`XpContext.TierId` 判断记录当时已把"若
   T-N4-4 落地时发现真的需要按 `TierId` 查倍率，委托签名或注入方式需要那时再按需扩展"登记为
   契约疑点上报——本任务落地"分档经验倍率"（读 `creature.tier_definition.xp_multiplier`）确实
   需要知道死亡单位的分档 id，`ProgressionHost.GrantXp` 的 `kind=kill` 分支调用本委托时手上正好
   有 `XpContext.TierId`（`CreatureDeathXpListener` 传入），因此新增第三个参数直传。ABI 判断
   记录：本委托是阶段 N4 内多个任务共同完善、随 1.34.0 一次性发布的全新类型，本阶段内的签名
   调整不构成"已发布 ABI"的破坏性变更。
2. **真正接入分档 × 难度倍率**：`core/gameplay/assembly.GameplayAssembly` 从本任务起把
   `ExtraXpMultiplierProvider`（未显式配置时）默认接为
   `(tierId 对应的 CreatureFactory.TryGetXpMultiplier 结果 ?? 1) ×
   Core.Gameplay.Difficulty.IDifficultyHost.XpMultiplier`——真正读
   `creature.tier_definition.xp_multiplier`/`diff.tier.xp_multiplier` 两张新增字段（各自模块
   README 判断记录见 `core/carriers/creature/README.md`/`core/gameplay/difficulty/README.md`）。
   调用方显式设置本字段时装配根不覆盖。
3. **新增 `ProgressionOptions.QuestXpSourceId`**：`null`（默认）时消费方（`core/gameplay/common.
   RewardDispatcher`）落到约定 id `prog.xp_source.quest`（同 T-N4-3 `KillXpSourceId`/
   `DiscoveryXpSourceId` 一贯约定）——任务/遭遇奖励包新增 `xp_equivalent`/`level` 字段非空时，
   `RewardDispatcher` 改经 `IProgressionHost.GrantXp(unit, questXpSourceId, new
   XpContext(level, equivalent: eq))` 发放，取代旧字段 `xp` 绝对数直发路径（硬性规则"禁止奖励
   直发绝对数"），详见 `core/gameplay/common/README.md`。
4. **新增默认接口成员 `IProgressionHost.HasXpSource(sourceId)`（设计层裁定，附带任务）**：
   `core/gameplay/progression_bridge` 的两个监听器（`CreatureDeathXpListener`/
   `AreaTriggerDiscoveryXpListener`）此前用 `try/catch (ArgumentException)` 兜"来源未登记"这一
   正常场景，改为先显式查询、查到才发，不再依赖异常控制流——`ProgressionHost` 显式覆盖为按内部
   `prog.xp_source` 索引精确判断；默认实现返回 `true`（不是更"保守"的 `false`）：本成员新增之前
   调用方对任何 `IProgressionHost` 实现都无条件尝试调用 `GrantXp`，默认值 `true` 保持这一既有
   行为对未覆盖本方法的旧实现方透明，只有 `ProgressionHost` 才获得精确判断能力，详见该接口成员
   判断记录。
5. **ABI 门禁 G3（已发布基线约束）**：本仓库已发布 1.33.0，`RulesAssembly`/`CarriersAssembly`/
   `GameplayAssembly` 三个装配根构造函数均已随该版本发布，本任务需要给它们接入
   `ProgressionOptions?` 时不能再直接在已发布签名末尾追加新参数（会被 `toolchain/abi_probe.ps1`
   判定为破坏性变更），改用"新增重载"——三者各自的旧签名构造函数原样保留、转发新增的带
   `ProgressionOptions` 参数的重载（新重载因 C# 语法"可选参数必须在必选参数之后"，把此前全部
   可选参数一并改为必选，仅有的调用点已同步显式列出全部参数），详见三者各自构造函数注释。

## T-N4-5（分阶段落地计划；ADR-0033 决策 7；06 第 2.5 节"升级回满"）：`ProgressionOptions.RefillOnLevelUp` 消费方接线

`ProgressionOptions.RefillOnLevelUp`（T-N4-1 落地时只是登记壳，缺省 `true`）从本任务起真正被
消费——消费方不在本模块内部，而是 `core/rules/assembly.RulesAssembly` 构造函数：本字段为 `true`
且该单位已在 `Core.Numbers.PowerSet.PowerHost` 注册时，`progression.level_up` 触发一次
`IPowerHost.RefillAll`（新增默认接口成员，见 `core/numbers/power_set/README.md`"T-N4-5"一节）；
为 `false` 时整条订阅回调直接跳过。本模块自身不改动任何代码（`LevelUpEvent` 早已存在并在
`ProgressionHost.AddXp` 内正确发布，见判断记录 3），只是原先"契约疑点上报"的占位字段现在有了
真正的消费方。

同一任务另锁死 ADR-0033 决策 6"从不扣经验"——`core/gameplay/death`（三种死亡复活策略
`RespawnPolicy.RespawnPoint`/`ReloadSave`/`Permadeath` 的执行主体）按其模块 README"依赖"一节
只依赖 L0 + L2 `core/rules/common`，从未引用、也不依赖 `IProgressionHost`/`ProgressionHost` 任何
类型，三种策略的结算逻辑本身不触达经验/等级状态；回归用例见
`core/gameplay/death/tests/T_N4_5_RespawnPolicyDoesNotAffectXpTests.cs`（三种策略各一组，独立
装配的 `ProgressionHost` 在完整死亡结算流程前后 `GetXp`/`GetLevel` 逐字节不变）。

## 不负责什么

- 不实现"经验来源的触发条件"求值（`prog.xp_source.condition` 是 Expr 字段，本模块只登记类型）。
- 不知道、也不依赖具体的属性宿主/资源池实现，只通过 `StatModifierWriter`/
  `StatModifierRemover` 两个具名委托与外界交互（见判断记录 4）。
- 不做"死亡不掉经验""休息经验"等具体游戏口味机制——00 第 3 节已拍板"休息经验直接去掉"，
  经验获取速率完全由 `prog.level_curve`/`prog.xp_source` 数据与调用方传入的
  `amount`/`multiplier` 决定。
