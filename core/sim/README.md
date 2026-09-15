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

依赖：`Core.Gameplay`（L4，经其既有 `ProjectReference` 链传递可见 `Core.Carriers`/`Core.Rules`/
`Core.Numbers`/`Core.Foundation`）与 `Adapters.Stub`（桩适配层，ADR-0035 决策 1 明说无头运行器
复用桩适配层）。**不引用**任何 `Tests.*` 程序集或 `Presentation.Common`——本模块是生产代码交付物，
不是测试专用夹具。

## 目录

```
core/sim/
  README.md
  Core.Sim.csproj
  LayerMarker.cs
  core/
    HeadlessWorldBuilder.cs   HeadlessWorldOptions（构造期选项）+ HeadlessWorld（装配结果，
                              T-N6-2a 新增 AnchorTable?/ScenarioCatalog? 两个可空属性）+
                              HeadlessWorldBuilder（唯一的 Build 入口）
    AnchorTable.cs            T-N6-2a：AnchorRow（sim.anchor 一行的强类型只读视图）+
                              AnchorTable（MaxLevel/TryGet/Get）
    ScenarioCatalog.cs        T-N6-2a：ScenarioKind/ScenarioPlayerSpec/ScenarioOpponentSpec/
                              ScenarioDef（sim.scenario 一行的强类型只读视图）+
                              ScenarioCatalog（All/TryGet/Get/ByKind）
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
```

## 不负责什么

- 不实现标准玩家生成器（ADR-0035 决策 2：按等级/职业经预算反解生成装备、按技能书填满、智能释放
  优先级表）——那是 T-N6 后续任务的范围，本任务的 `HeadlessWorldOptions` 已经为它预留了
  `PlayerLevel`/`PlayerRaceId` 等选项位，但本任务本身不消费非默认值。
- 不实现三级仿真（战斗/成长/内容覆盖，ADR-0035 决策 3）、不实现标准玩家生成器（决策 2）、不实现
  报告输出与基线对比工具（决策 5）——均为 ADR-0035 后续任务的范围。T-N6-2a 已实现 `sim.scenario`/
  `sim.anchor` 两张数据表的 schema、校验规则、类型化读取（决策 4），但不消费它们——`AnchorTable`/
  `ScenarioCatalog` 只读、不做任何仿真计算，`HeadlessWorldBuilder.Build` 本身也不读取它们参与装配
  逻辑（只是顺带在数据存在时构造出来，见判断记录"AnchorTable/ScenarioCatalog 何时构造"）。
- 不把 `Core.Sim.dll` 同步进 Unity 工作台工程——`build.ps1` 的 `$CoreAssemblies` 是显式列出
  Foundation/Numbers/Rules/Carriers/Gameplay 五个程序集名的数组（不是通配符抓取
  `Core.*.dll`），本任务未改动这份清单，`Core.Sim` 因此天然不会被同步；`Core.Sim` 依赖
  `Adapters.Stub`，Unity 侧本就不需要它。`dist/` 打包结构与 `check.ps1` 的对应步骤留给 T-N6-7。
- 不做仓库路径定位（`FindRepoRoot`/直接读 `data/_framework`、`data/_sample` 磁盘路径）——那是
  具体宿主（`GameWorldFixture`、本模块自己的 `SimTestWorldFactory`）的职责，装配根只接受调用方
  已经构造好的 `IDataSource` 列表，见判断记录"数据来源必须注入"。

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
