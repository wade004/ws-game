# 无头适配层（分发形态）

本目录只在分发产物（`dist/<ver>/adapters/headless/`）中存在，源码仓库里不放编译产物——本文件是
随 `build.ps1 -Dist`/`-Release` 一起拷进 dist 的说明文档源文件（`toolchain/registry/manifests/
adapter-headless/README.md` 是同一份信息面向私服 npm 包通道的独立版本，两条通道并存，见根
`README.md`"私服通道"一节）。

## 是什么

`Adapters.Stub.dll`——引擎适配层（L-1）的桩实现，确定性、无渲染、无输入（见
`architecture/02_引擎适配层.md`）。[ADR-0018](../../architecture/adr/0018-编辑器随游戏走与框架为此提供的交付物.md)
决策第 3 条起，本程序集由"仅测试用"转正为框架正式交付物，对外称"无头适配层"：供不接真实引擎、
不需要渲染/输入的宿主（自动化测试、CI、内容编辑器的数值沙盘等）使用，与框架自身测试用的是
同一份实现，结果一致性由此保证。

## 依赖哪几个核心 DLL

`Adapters.Stub.dll` 只引用 `Core.Foundation.dll`（六个核心 DLL 之一，见 `MANIFEST.txt`
`[core_assemblies]` 段），不依赖其余五个核心 DLL，也不依赖任何具体引擎的程序集。宿主进程按需要
额外引用 `Core.Numbers.dll`/`Core.Rules.dll`/`Core.Carriers.dll`/`Core.Gameplay.dll`/
`Presentation.Common.dll`（同目录 `adapters/unity/Packages/com.gamefoundation.adapter.unity/
Runtime/Plugins/Core/`，或本 zip 快照同一路径）。

## 怎么在无头宿主里使用

```csharp
using Adapters.Stub;

var engine = new StubEngine(); // 一次性构造全部 13 个引擎适配层接口的桩实例
engine.Clock.Advance(1.0 / 60.0);
engine.FileSystem.WriteTextAtomic("save/slot1.json", "{}");
```

`StubEngine` 暴露的各接口具体类型（`StubClock`/`StubFileSystem`/…）与桩清单见
`adapters/stub/README.md`（源码仓库内该目录是本程序集的源码与完整说明，本文件只是分发产物内的
精简指引）。

## 数值仿真骨架（T-N6-7，ADR-0035 决策 1/5）

`Core.Sim.dll`——无头运行器的装配根（`HeadlessWorldBuilder`）+ 三级数值仿真（战斗/成长/内容覆盖）
+ 报告输出与基线对比工具，本目录同一份 T-N6-7 起随分发产物提供，与 `Adapters.Stub.dll` 同目录（依赖
关系：`Core.Sim` 依赖 `Adapters.Stub`，见下方"依赖哪几个 DLL"）。措辞与
`architecture/11_工程规范与测试.md` 第 6 节"数值仿真"行一致：无头、固定种子、读 `sim.scenario`/
`sim.anchor` 两张数据表，输出结构化报告并与基线比对统计量差异（数值回归，与回放回归同一原理）；
是否把这一项列为提交门槛由游戏层决定。完整判断记录与类型清单见源码仓库 `core/sim/README.md`
（本文件只是分发产物内的精简指引，同上一节"无头适配层"惯例）。

### 依赖哪几个 DLL

`Core.Sim.dll` 依赖 `Adapters.Stub.dll`（同目录，见上一节）与六个核心 DLL 中的
`Core.Foundation.dll`/`Core.Numbers.dll`/`Core.Rules.dll`/`Core.Carriers.dll`/
`Core.Gameplay.dll` 五个（不需要 `Presentation.Common.dll`——不做内容编辑器/校验器那一层，只做
仿真装配与运行），取法与上一节相同：同目录 `adapters/unity/Packages/com.gamefoundation.adapter.unity/
Runtime/Plugins/Core/`，或本 zip 快照同一路径。

### 无头宿主怎么用：装配 + 三类仿真 + 基线比对

```csharp
using Core.Sim;

// 1. 装配一整套 L0～L4 世界（复用桩适配层，见 HeadlessWorldOptions 各字段）。
var world = HeadlessWorldBuilder.Build(new HeadlessWorldOptions
{
    DataSources = dataSources,           // IReadOnlyList<IDataSource>，框架根在前
    Seed = 20260916001UL,
    FileSystem = new Adapters.Stub.StubFileSystem(),
    MapId = mapId,
    PlayerClassId = playerClassId,
    PlayerLevel = 20,
});

// 2. 标准玩家生成器（等级+职业 -> 学技能 -> 按预算生成并装备"标准装"）。
var player = StandardPlayerBuilder.Build(world, playerClassId, level: 20, expectedQualityId);

// 3a. 战斗仿真：标准玩家对指定生物模板按指定等级出生，逐 tick 智能释放优先级表直至一方死亡/超时。
var fightResult = FightRunner.Run(new FightRunnerOptions { /* ... */ });
// 3b. 场景运行器：对 sim.scenario 里 kind=arena/growth/coverage 的场景按登记参数批量跑。
var arenaReport = ArenaSimulation.Run(world.Options, scenarioDef);
var growthReport = GrowthSimulation.Run(world.Options, scenarioDef);
var coverageReport = CoverageSimulation.Run(world.Options, scenarioDef);

// 4. 拍平成统一报告信封，与既往基线比对。
var report = SimReport.FromArenaReport(arenaReport, scenarioDef, frameworkVersion: "1.36.0");
var baseline = SimBaseline.Parse(File.ReadAllText("baseline/sim_arena_matrix.json"));
var diff = BaselineComparer.Compare(report, baseline, new BaselineCompareOptions());
if (diff.HasBlockingDifference) { /* Exceeded/Removed：需要人工确认是否为有意的数值改动 */ }
```

### 命令行入口：`simrunner`

```
dotnet <本目录同级 toolchain/simrunner/bin/SimRunner.dll> run \
  --scenario all --framework-root <framework-data 包解析路径>/Data~/data/_framework \
  --data-root <你的游戏>/data --out <out-dir> \
  [--baseline-dir <baseline-dir>] [--update-baseline] [--json] [--runs <n>] [--version <str>]
```

（预编译产物随 `com.gamefoundation.toolchain` 包的 `Tools~/simrunner/bin/` 分发，见该包
README.md；源码仓库内也可以 `dotnet run --project toolchain/simrunner -- run ...` 现场编译。）

退出码：**0**=全部选中场景均无 `Exceeded`/`Removed`（含未传 `--baseline-dir` 不做比对）；
**1**=至少一个场景 `BaselineDiff.HasBlockingDifference`；**2**=参数错误或数据装载阻断；
**3**=传了 `--baseline-dir` 但对应基线文件不存在且未传 `--update-baseline`。控制台每个场景
一行摘要：`scenario=<id> kind=<k> stats=<n> exceeded=<n> added=<n> removed=<n> result=PASS|FAIL`，
末尾一行 `RESULT=OK|FAIL`。完整参数说明见 `core/sim/README.md`"命令行入口"一节。

### 基线更新流程

惯例同 `core/gameplay/tests/Replay/README.md`"如何更新基线"一节（比对失败本身不说明对错，只说明
结果变了）：1）先确认这是一次有意的数值/结算行为变化；2）跑一次不带 `--update-baseline` 的比对，
看差异；3）人工审阅每一条 `Exceeded`/`Removed` 是否对应确认过的具体改动；4）确认无误后同一份改动里
加 `--update-baseline` 重新生成基线；5）提交信息里必须注明本次更新了基线以及原因。完整流程见
`core/sim/README.md`"基线更新流程"一节、`toolchain/sim_baseline.ps1`（源码仓库内的薄封装脚本，
不随本分发产物提供，源码仓库自用）。
