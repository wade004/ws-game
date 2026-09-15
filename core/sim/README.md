# 数值仿真骨架 Core.Sim

职责：ADR-0035 决策 1"仿真骨架是框架交付物"的落地起点——无头运行器的装配根，把装配一整套
L0～L4 世界（事件目录/总线、`DataRegistry` 装载、`RngHost`、`WorldSim`、`StubSpatialQuery`、
存档系统、`SimClockHost`、`GameplayAssembly`、玩家单位注册）收敛为一个公开、可复用的组装 API，
供后续任务（三级仿真、标准玩家生成器、场景/锚点表，见 [ADR-0035](../../architecture/adr/0035-数值仿真骨架为框架交付物.md)
决策 2～6）与既有测试夹具共用。T-N6-1 只交付装配根本身与其确定性证明，不含标准玩家生成器、场景
加载、报告输出——那些是后续任务（T-N6-2 及之后）的范围。

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
    HeadlessWorldBuilder.cs   HeadlessWorldOptions（构造期选项）+ HeadlessWorld（装配结果）+
                              HeadlessWorldBuilder（唯一的 Build 入口）
  tests/
    Tests.Sim.csproj
    SimTestWorldFactory.cs   本工程自己的"从磁盘读 data/_framework + data/_sample 构造数据源"
                             小工具（惯例同 core/gameplay/tests/EndToEnd/GameWorldFixture.cs 的
                             FindRepoRoot），供确定性测试驱动一场固定的战斗脚本
    DeterminismTests.cs      G1★ 确定性证明：同种子两次独立 Build 逐 tick 完全一致；不同种子
                             在命中判定上产生可观测差异
```

本模块目前只有 `core/` 一段（无 `contracts/`/`schema/`）——判断记录见下"为何暂不拆
contracts/schema"。

## 不负责什么

- 不实现标准玩家生成器（ADR-0035 决策 2：按等级/职业经预算反解生成装备、按技能书填满、智能释放
  优先级表）——那是 T-N6 后续任务的范围，本任务的 `HeadlessWorldOptions` 已经为它预留了
  `PlayerLevel`/`PlayerRaceId` 等选项位，但本任务本身不消费非默认值。
- 不实现三级仿真（战斗/成长/内容覆盖）、不实现 `sim.scenario`/`sim.anchor` 两张数据表、不实现
  报告输出与基线对比工具——均为 ADR-0035 决策 3～5 的范围，本任务只交付它们共同需要的装配根。
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
4. **为何暂不拆 contracts/schema**：11 第 2 节"模块范式"标准五段目录（`contracts/`/`core/`/
   `schema/`/`tests/`/`README.md`）面向"对外暴露契约、拥有自己数据表"的常规业务模块；`Core.Sim`
   本任务阶段既没有独立于 `HeadlessWorldBuilder` 本身的额外契约类型，也没有自己的数据表（`sim.
   scenario`/`sim.anchor` 是后续任务的范围），拆出空的 `contracts/`/`schema/` 目录不会有实际内容。
   `HeadlessWorldOptions`/`HeadlessWorld` 两个公开类型与 `HeadlessWorldBuilder` 静态类放在同一个
   `core/` 目录下的同一个文件里，符合任务书"上提为 `core/sim/core/` 内可发布的公开装配根"的
   字面要求；待后续任务引入 `contracts/`（如标准玩家生成器契约）或 `schema/`（`sim.scenario`/
   `sim.anchor`）时再拆分，不在本任务范围内提前搭空架子。
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
