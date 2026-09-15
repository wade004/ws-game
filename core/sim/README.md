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

## 目录

```
core/sim/
  README.md
  Core.Sim.csproj
  LayerMarker.cs
  core/
    HeadlessWorldBuilder.cs   HeadlessWorldOptions（构造期选项，T-N6-3a 新增 ExpectedQualityId 可选
                              属性）+ HeadlessWorld（装配结果，T-N6-2a 新增 AnchorTable?/
                              ScenarioCatalog? 两个可空属性）+ HeadlessWorldBuilder（唯一的 Build
                              入口，T-N6-3a 起数据源含 sim.anchor 时自动装配
                              AnchorTableSkillBudgetAnchorProvider）
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
    data/                    T-N6-2b：嵌入式最小仿真数据集，见 data/README.md（数据清单、锚点
                             推导公式与手算表、判断记录）——不进 data/_sample（拍板 10）
```

## 不负责什么

- 不实现三级仿真（战斗/成长/内容覆盖，ADR-0035 决策 3）、不实现报告输出与基线对比工具（决策 5）——
  均为 ADR-0035 后续任务（T-N6-4 及之后）的范围。T-N6-2a 已实现 `sim.scenario`/`sim.anchor` 两张
  数据表的 schema、校验规则、类型化读取（决策 4），T-N6-3a 起 `ExpectedStatCalculator`/
  `AnchorTableSkillBudgetAnchorProvider`/`StandardPlayerBuilder` 消费它们（决策 2 标准玩家生成器、
  锚点接入两条预算校验规则）——但三级仿真运行器本身（怎样用标准玩家跑一场战斗仿真并统计分布、越级
  矩阵、成长曲线、内容覆盖）仍未实现，`AnchorTable`/`ScenarioCatalog` 仍然只读、不做任何仿真计算。
- 不把 `Core.Sim.dll` 同步进 Unity 工作台工程——`build.ps1` 的 `$CoreAssemblies` 是显式列出
  Foundation/Numbers/Rules/Carriers/Gameplay 五个程序集名的数组（不是通配符抓取
  `Core.*.dll`），本任务未改动这份清单，`Core.Sim` 因此天然不会被同步；`Core.Sim` 依赖
  `Adapters.Stub`，Unity 侧本就不需要它。`dist/` 打包结构与 `check.ps1` 的对应步骤留给 T-N6-7。
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
- `StandardPlayerBuilder` 不生成"标准玩家生成器驱动的战斗仿真报告"——它只负责装配一个可玩的标准
  玩家单位（学技能、穿装备、给优先级表），真正"用它打一场仿真、统计胜率/时长"是三级仿真运行器
  （决策 3，T-N6-4 及之后）的职责，不在本任务范围。

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

10. **`toolchain/validator/Validator.csproj` 的 `lib/` 分发分支：已知缺口，留给 T-N6-7**：本任务
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
