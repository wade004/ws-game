# lab：手感实验室内核与无头宿主

手感实验室（`architecture/手感设计/06_手感实验室与验收.md`，ADR-0120/0122）的**引擎无关内核**加**无头宿主**命令行，以及用来把"当前移动与战斗行为"钉成指纹基线的标准脚本夹具。本切片的定位是回归网：先把现状量出来入库，之后手感机制落地时，每一处行为变化都会在基线比较里显形、可读、需要显式更新基线。

## 目录

| 路径 | 内容 |
|---|---|
| `lab/Lab.Kernel.csproj`、`lab/core/` | 内核（命名空间 `Lab`）：输入脚本、数据装载与类型化目录、无头宿主、录制、度量注册表与标准度量组、指纹与比较器、运行器与夹具套件 |
| `lab/tests/` | 内核测试工程 `Tests.Lab` |
| `lab/fixtures/scripts/` | 标准输入脚本（`<id>.script.json`，入库） |
| `lab/fixtures/baselines/` | 每个脚本一份基线（`<id>.baseline.json`，入库，内含六个格子） |
| `lab/out/` | 命令行默认输出（指纹、完整记录）；生成物，已被 `.gitignore` 忽略，不入库 |
| `toolchain/feellab/` | 命令行入口（形态同 `toolchain/simrunner`） |
| `data/_lab/` | 实验室数据集（框架级，占位命名，不含任何游戏内容） |
| `data/_lab_action/` | 实验室动作式数据（手感场景脚本经 `meta.extraDataRoots` 叠加：带 `timeline` 的技能、输入动作、靶子集、手感标定与覆盖行；见判断记录 22～24） |
| `core/sim/schema/LabSchemas.cs` | `lab.scenario`、`lab.arena`、`lab.dummy_set` 三张表的声明 |

## 命令行用法

在仓库根执行（构建产物也可直接 `dotnet FeelLab.dll <命令>`）：

```
dotnet run --project toolchain/feellab -- suite                       # 全部标准脚本 x 6 格子逐个与基线比较
dotnet run --project toolchain/feellab -- suite --script wall --cell 2d_targeted
dotnet run --project toolchain/feellab -- suite --update-baseline     # 有意重写基线（见下）
dotnet run --project toolchain/feellab -- run --script lab/fixtures/scripts/wall.script.json --cell 3d_action [--record] [--baseline lab/fixtures]
dotnet run --project toolchain/feellab -- export-test --script <脚本文件> [--fixtures lab/fixtures] [--cell <格子>] [--no-expect] [--expect-groups <组,组>] [--expect-realtime]
dotnet run --project toolchain/feellab -- list                        # 六个格子在无头宿主上的可运行状态
dotnet run --project toolchain/feellab -- invariants [--script <id>] # 跨格子不变量（ADR-0122 决定 4，见判断记录 25），不比较基线、不改文件
```

公共参数：`--framework-root`（默认 `data/_framework`）、`--data-root`（可重复，默认 `data/_lab`）；数据根可指向任何游戏自己的数据集（实验室场景的数据由 `_lab` 提供，游戏数据根可叠加或替换）。

退出码：`0` 全部通过；`1` 基线有差异（逐条可读 diff 打印在标准输出）；`2` 参数错误；`3` 缺基线；`4` 有格子不可运行（缺适配层能力，不静默跳过）；`5` 数据或脚本内容错误。同时出现多种情况时优先级为差异 > 缺基线 > 不可运行。

`suite` 末尾除中文汇总行外另输出一行纯 ASCII 的 `RESULT total=<n> pass=<n> diff=<n> missing=<n> not_runnable=<n>`，供门禁解析（门禁按系统代码页解码标准输出，中文行会失配）；有脚本带期望清单时，这一行之前另有一行 `RESULT expectations total=<n> pass=<n> fail=<n>`（期望按判定条数计，一条期望在多个格子上各判一次；`total` 是期望判定总数，不是脚本数）。

门禁：`feel_lab_suite` 步骤（`toolchain/_gate_line_heavy.ps1`）跑 `suite` 要求退出码 0；`validate_lab_data` 步骤（`check.ps1`）要求 `data/_lab` 校验 0 error 0 warning，`validate_lab_action_data` 步骤同样要求 `data/_lab_action`（叠加框架根、`data/_lab`、`data/_feel`）0 error 0 warning。跨格子不变量随 `Tests.Lab`（`CrossCellInvariantTests.AllInvariants_HoldForEveryScript`）进门禁；`invariants` 命令末行另输出纯 ASCII 的 `RESULT invariants total=<n> pass=<n> fail=<n>`，有不一致时退出码 1。

### 随发布产物分发（只消费发布产物的游戏怎么跑）

实验室随发布产物分发（手感设计/06 第 8 节第 2 步：游戏用实验室在自己的数据上校准手感）。三个形态，布局都与仓库同路径，命令行用法与上面一致（把 `dotnet run --project toolchain/feellab --` 换成 `dotnet <FeelLab.dll 路径>`）：

| 形态 | 实验室根（工作目录） | 命令行 |
|---|---|---|
| 发布 zip / `dist/<版本>/` | `dist/<版本>/` 本身（含 `data/_framework`、`data/_feel`、`data/_lab`、`data/_lab_action`、`data/_equip`、`lab/fixtures`） | `toolchain/feellab/bin/FeelLab.dll`（预编译，另带 `lib/` 与空 `Directory.Build.props`，同 simrunner） |
| 私服包 `com.gamefoundation.toolchain` | `Tools~/feellab/labroot/`（自包含：上面六棵树各一份） | `Tools~/feellab/bin/FeelLab.dll` |
| 自己的数据 | 在上面任一实验室根里加 `--data-root <你的数据根>`（叠加在 `_lab` 之后；`--framework-root` 指向你用的框架数据） | 同上 |

例（zip 解压后）：`cd ws-game-<版本>` 后 `dotnet toolchain/feellab/bin/FeelLab.dll suite`，应与框架基线一致（`RESULT total=258 pass=258 ...`；脚本带期望清单时，它前面另有一行 `RESULT expectations total=98 pass=98 fail=0`）；不一致说明你改了框架数据或标定。只需要 dotnet 运行时，不需要 Unity。消费方演练（`toolchain/consumer_smoke.ps1`）有一步在消费方工作目录里用 dist 里的预编译命令行跑 `suite` 与 `invariants`，门禁里随每次全量验证。

### 基线更新流程（有意提交，不允许静默通过）

1. 改了会改变行为的东西（移动、结算、数据）后，`suite` 会红并打印差异。
2. 确认差异是预期引入的，运行 `suite --update-baseline`，把脚本/基线改动与行为改动放同一提交，提交信息注明"更新手感基线及原因"。
3. 差异出乎意料时先定位，不要用重烘焙掩盖。
4. 标准脚本由 `export-test` 写入（脚本 + 基线 + 由指纹生成的期望清单，见下节）；脚本文件的 `scriptVersion` 变了，旧基线会被比较器拒绝（避免新脚本对着旧基线比较）。增删期望清单不属于行为变化，不改 `scriptVersion`，基线不受影响。
5. 数据集哈希只在基线比较有差异时才作为警告打印；改了实验室数据（`data/_lab`、`data/_lab_action`）而指纹度量没变时，基线文件里记的哈希会滞后，下一次有意重烘焙时一并刷新即可（不影响通过与否）。

### 脚本期望清单（`expectations`，手感设计 06 第 3.1、3.5 节）

输入脚本顶层可带 `expectations` 数组（脚本格式版本 4；版本 1～3 的旧脚本照常读取，没有期望的脚本序列化文本逐字不变）。基线守的是"整份指纹逐项不变"，期望清单守的是"人写下来的意图"：某度量在某些格子上的上下界、相等、近似、相对关系。期望随 `suite` 与 `run` 一起判定：任何一条没通过（失败或无法判定），该格子算"有差异"（`suite` 退出码 1、汇总行 `diff` 计数），逐条诊断紧跟在格子行下面；`suite` 另输出一行纯 ASCII 的 `RESULT expectations total=<n> pass=<n> fail=<n>`。

```
{"id": "freeze_longer", "cells": ["2d_action"], "metric": "hitstop.started_ticks_player", "op": "gt",
 "versus": {"cell": "2d_targeted", "metric": "hitstop.started_ticks_player", "scale": 1, "offset": 0}, "note": "..."}
```

| 字段 | 含义 |
|---|---|
| `id` | 期望标识，脚本内唯一；缺省按序号取 `#n`。`auto.` 开头的是"导出为测试"自动生成的 |
| `cell` / `cells` | 适用的格子（短名，如 `2d_action`）；缺省或 `*` 为脚本适用的全部格子 |
| `metric`、`at`、`agg` | 度量 `组.度量`；数组度量可取下标 `at`（负数从末尾数）或聚合 `agg`（`len`/`sum`/`min`/`max`/`first`/`last`），二者不同时给 |
| `op` | `eq`、`ne`、`lt`、`le`、`gt`、`ge`、`between`（`min`/`max` 闭区间，可只给一端）、`approx`（`value` ± `tolerance`，缺省 1e-6）、`in`（`value` 是候选数组）、`contains`（文本含子串 / 数组含元素） |
| `value` / `versus` | 比较的另一端：字面值，或另一个度量 `versus: {cell?, metric, at?, agg?, scale?, offset?}`，按 `other × scale + offset` 换算；`versus.cell` 缺省同格子，给另一个格子即跨格子相对关系（按需运行并缓存该格子） |

诊断（中文）：`[<id>] <格子> <度量>：要求 <运算> <对照>，实际 <值>（差 ±n）`；无法判定的（度量或度量组不存在、条件度量组在该运行里没有、类型不符、下标越界、对照格子不存在或不可运行）记为"期望无法判定"并给原因，不当作通过。引用了不存在的度量组、度量或格子（拼写错误）是脚本内容错误（退出码 5，列出该组已有的度量名），不让期望悄悄失效；未知字段与重复 id 在解析时就被拒绝。

**导出为测试产出期望**（`export-test`，`ExpectationExporter`）：逻辑类度量精确（数值 `eq`、文本与数组逐字 `eq`），表现类数值 `approx` 带度量声明的绝对允差，真实时间类（`--expect-realtime` 才导出）`le` 倍率上限（同比较器的规则）；空值（空文本、空数组、`-1` 哨兵）与超过 160 字符的长序列不导出（完整序列由基线逐字守着，期望只放人能编辑的断言）。各格子取值相同的度量合并成一条不带 `cells` 的期望，取值随格子不同的按取值分组写 `cells`。`--expect-groups attack,hitstop` 只导出指定组，`--no-expect` 不动脚本里的期望。再次导出只重生成 `auto.` 开头的，手写的期望原样保留在前。

### 新增度量组

实现 `IMetricGroup`（声明度量名、类别、允差；`Compute` 从录制里算值）并注册进 `MetricRegistry`，不改运行器、序列化与比较器。基线里缺这个组：比较给警告并提示重烘焙；基线里有而注册表已移除的组：算差异。已有度量的允差只许收紧、不许放宽。

## 内核结构与度量

- **输入脚本**（`InputScript`）：`meta`（脚本 id、`scriptVersion`、数据根、预设、标定版本、姿势集、`tickRate`、`frameRateCap`、时长、起点、靶子分组）加逐 tick 事件（`press`/`release`/`axis`，可带 `realTs`）；`formatVersion` 字段版本化，更高版本被拒绝。`ReplayImporter` 把带真实时间戳的事件换算成 tick（引擎宿主录制通道的占位入口）。
- **宿主**（`LabHost`）：用无头装配根（`HeadlessWorldBuilder`，离散时间模型开启以便 `Gameplay.Advance`）+ 桩时钟/输入/导航/空间查询装配世界，每 tick 按引擎侧引导代码同序执行：输入映射更新 → 移动请求 → 动作按下沿提交施放意图 → 推进一步；脚本事件经桩输入注入。
- **录制**（`LabRecording`）：三条时间线——逻辑（每 tick 位置、速度、运动模式、施放与伤害事件）、表现（假适配器视图的创建与可见位移）、真实时间（帧耗时与分配）。
- **度量**：逻辑类逐字节比较；表现类按绝对允差；真实时间类只做上限检查（基线按量级分桶存储，防止换机器后基线抖动）。当前实现四组：
  - 响应（`ResponseMetricGroup`）：输入到移动/施放的 tick 数，首次可见位移的帧数与毫秒；
  - 移动（`MovementMetricGroup`）：达速、停步、反转、斜向速度比、阈值以下 tick 数、受阻比例与受阻抖动；
  - 攻击（`AttackMetricGroup`）：施放提交/成功/失败（含原因）、命中与伤害、击杀、结算 tick、每实例命中数与去重违例；
  - 性能（`PerformanceMetricGroup`）：tick 数、事件总数、帧数、帧耗时 p50/p95、每帧分配 p95（后三项为真实时间类）。
- **手感条件度量组**（`FeelMetricGroups.cs`，七个，只在脚本 `meta.feel` 为真、记录带 `LabRecording.Feel` 时出现；既有脚本指纹不含它们，基线逐字不变；判断记录 26）：
  - 输入缓冲（`inputbuf`）：按下 / 入槽可见 / 消费 tick、输入到动作开始的延迟（先进先出配对）、过期与丢弃事件、槽占用；
  - 动作时间线（`actiontl`）：动作开始（含连招序号）、各相实际 tick 数、标记、取消（原因与下一技能）、结束、连招链、蓄力比例、目标辅助；
  - 顿帧（`hitstop`）：每次顿帧的作用实体集合与时长、两侧实际冻结 tick 数、嵌套次数、结束时未结束数与表现冻结登记残留；
  - 受击反应（`reaction`）：命中结局与反应分布、反应落地（受击者/反应/硬直 tick）、各单位硬直 tick 数、击杀、冲击等级序列；
  - 运动（`motion`）：玩家模式/来源游程序列、峰值速度、起步到全速与刹停 tick 数、转向到位 tick 数、终点位置与朝向、阻挡变更、会动的靶子的轨迹；
  - 空间命中（`spatialhit`）：命中确认数与回避数、每次施放每段的命中集合、重复确认数（恒 0）、有伤害无确认数（恒 0）、有确认无伤害数（恒 0）；
  - 表现时间线（`presentation`，**表现类**，数值按绝对允差 1e-6、id 与序列逐字）：反馈流水线出批到假 sink 的音效 id、镜头冲击（合并命中数、幅度、衰减）、顿帧冻结/释放指令、首次命中反馈相对命中确认的延迟。
- **指纹**：`(脚本 id 与版本, 数据集哈希, 格子)` 为键，按组存放度量；规范 JSON（键序固定，数值规整到 9 位小数）。数据集哈希变化只给警告（改数据必然改哈希，以度量差异为准）。

## 标准脚本与基线

十个脚本（`lab/fixtures/scripts/`），每个脚本在六个格子上各一条基线，共 60 条（全部标准脚本与基线的总数拆分见本节末尾"数量口径"）：`move_tap`、`move_small_axis`、`reverse_180`、`diagonal`、`wall`、`pillar_loop`、`attack_while_moving`、`attack_then_stop`、`group_hit`，以及换装场景脚本 `equip_cycle`（tickRate 60，格式版本 2，见判断记录 15～21）。另有 30/60/120 帧率上限测试（`meta.frameRateCap` 三档，逻辑组必须逐字节一致）。

**手感场景脚本**（`meta.feel = true`，格式版本 3，tickRate 60，S7b 新增十九个、S12 新增一个、M2-C 新增一个、M3-A 新增一个、M3-E2 新增一个，共二十三个，各六格共 138 条基线，与十个旧脚本合计 198 条；判断记录 22～40）：`feel_melee`、`feel_lunge`（近战与目标辅助/扑击位移）、`feel_dash`（冲刺）、`feel_projectile`（投射物：release 标记与只有 hit 标记两种形态）、`feel_projectile_miss`（S12：投射物没命中任何单位，被竞技场的直墙挡住，挥空提示出现在弹被挡住的那一刻）、`feel_combo3`（三连击）、`feel_dodge_cancel`（闪避取消）、`feel_buffer_lead`（输入缓冲提前量边界）、`feel_charge`（蓄力）、`feel_elite_armor`（精英霸体窗口内受击）、`feel_elite_flinch`（霸体窗口后按精英反应上限封顶）、`feel_interrupt`（普通靶被打断）、`feel_kill`（击杀放大顿帧）、`feel_group_hit`（同 tick 多目标顿帧取最大值并受上限约束）、`feel_patrol`（巡逻靶）、`feel_breakable`（可破坏障碍，动态阻挡）、`feel_motion_accel_decel`、`feel_motion_turn`、`feel_motion_wall`、`feel_motion_wall_slant`（起步刹停、转向、撞墙、斜撞墙贴墙滑行）、`feel_unit_block`（M2-C：单位间体积阻挡，走进木桩被挡在体积边界、闪避同样被挡；判断记录 34）、`feel_unit_separate`（M3-A，出生重叠的玩家与木桩按速率被推开，判断记录 36）、`feel_stake_poise`（M3-E2：靶子韧性，判断记录 40）。期望值全部在 `lab/tests/FeelSceneTests.cs` 里由预设字段 × 毫秒换算 × 受击裁决规则推出（读数辅助见 `FeelRules.cs`），用例里没有裸数。

六个格子（`lab.scenario.*`）：`2d_targeted`、`2d_action`、`2_5d_targeted`、`2_5d_action`、`3d_targeted`、`3d_action`，空间模型均为平面世界。`space` 字段另外接受 `side_2d`（横版二维）与 `volume`（体积空间），两者装配竖直轴、在无头宿主上可运行（判断记录 35）；它们的格子行 `side_2d_targeted/action`、`volume_targeted/action` 放在空间脚本的额外数据根 `lab/fixtures/data/space/`，只适用 `space.*` 脚本（脚本子集），不进基础数据集。

**数量口径**（2026-10-02 实测，`suite` 与 `invariants` 汇总行的拆分；增删脚本或格子时数字随之变，以命令实测为准，这里的算式用来对账）：标准脚本 39 个、基线文件 39 份 = 十个旧脚本 + 二十三个手感脚本 + 六个空间脚本。
- `suite` 258 格 = 33 个平面脚本（十个旧脚本 + 二十三个手感脚本）× 六个格子 198 + 六个空间脚本 × 十个格子 60；全部通过、无缺基线。
- 期望清单 98 条判定（`RESULT expectations total=98`）：带期望的脚本六个（`feel_breakable`、`feel_combo3`、`feel_elite_flinch`、`feel_kill`、`feel_motion_wall`、`feel_stake_poise`），按"期望 × 适用格子"逐格判定后计数。
- `invariants` 379 条 = `planar_combos_logic_equal` 156（39 个脚本 × 4）+ `action_stripped_equals_targeted` 117（39 × 3）+ `feel_assembly_transparent_under_classic` 78（26 × 3：二十九个 `meta.feel` 脚本里三个钉死了预设——`feel_unit_block`、`feel_unit_separate`、`space.launch`——换预设变体没有意义，不参与）+ `space_semantics_only` 28（6 × 4 + 4：每个竖直轴格子一次"按平面运行"，没有空间刺激的 `space.neutral` 再多一次）。

## 判断记录

1. **内核位置与分层**：独立顶层目录 `lab/`，登记为 `module_map.json` 的 `lab` 层（单模块，同 `sim` 层），位于 `sim` 与 `presentation` 之下游。依赖方向：`Lab.Kernel` → `Core.Sim`（装配根与桩适配层）、`Adapters.Stub`、`Presentation.Common`；任何运行时核心程序集（`Core.*`、`Presentation.*`）都不引用它——内核引擎无关、不被运行时核心程序集反向引用。不放进 `core/sim`：`sim` 是可被装配与分发的仿真骨架，实验室是验收设施，且需要表现层公共部分，放进去会让 `core/sim` 新增对表现层的依赖（两者现在是 `gameplay` 之下的并列下游）。不放进 `presentation/`：它依赖装配根与桩适配层，反过来会让表现层依赖 `sim`。因此 `sim` 与 `presentation` 的 `downstream` 都登记了 `lab`，上游公开面变化会选中 `Tests.Lab`。
2. **schema 声明放在 `core/sim/schema/LabSchemas.cs`**：与 `sim.*` 同属"仅无头宿主与内容工具读取"的工具表，沿用 `SimSchemaCatalog.RegisterAll` 这一个入口，校验器与独立发行包不用新增对内核的引用；`core/sim` 只知道表形状，不引用内核。
3. **相对 06 第 1.1 节结构的扩展字段**：`lab.scenario` 增加 `arena`（地形引用）、`settlement`（`targeted|action` 显式取值）、`skill_bindings`（输入动作到技能），无头宿主跑起来必需，不改既有字段语义。`default_preset`/`default_weapons` 指向手感数据域（`feel.preset`、`feel.weapon`），但表本身登记为自由 id（只作标签，不校验存在性）：标签与手感装配解耦，脚本实际用的预设与武器由脚本 meta 与数据根决定。
4. **`HeadlessWorldOptions.Navigation`**：`HeadlessWorldBuilder` 新增导航接口透传选项（缺省行为不变），让宿主能把竞技场阻挡登记进桩导航；只增不改。
5. **动作式格子 = 目标选择式 + `timeline` + 手感预设**：不带手感装配的旧标准脚本（十个）没有 `timeline` 数据，三个动作式格子行为与对应的目标选择式格子一致（测试 `ActionCells_BehaveLikeTargetedCells_ForScriptsWithoutFeelAssembly` 锁住这一点，十个旧脚本的六格基线逐字不变）。手感场景脚本（判断记录 22）里两者的差别正是 `timeline`（含连招/取消/蓄力/标记/位移）加预设（`default_preset`：动作式 `arpg_responsive`、目标选择式 `rpg_classic`）——跨格子不变量 2（判断记录 25）把这句话证明成运行期事实。
6. **移动绑定到摇杆**：宿主把 `input.action.move` 重绑到手柄左摇杆轴。输入映射层对摇杆不做死区、不单位化；因此被测的是宿主循环阈值（模长平方大于 0.0001 才提交移动请求）与移动层对输入的单位化。移动请求每 tick 重提交。
7. **空间查询位置同步**：桩空间查询的位置不随世界自动更新，宿主在每次推进后手工同步一遍（靶子位置同样）。
8. **视图插值 alpha 恒为 0**：宿主按整步推进，视图绑定器的插值因子为 0，表现层位置比逻辑滞后一个 tick（60 帧上限下首次可见位移帧数为 2）。这是当前如实量到的行为，不是实验室引入的偏差。
9. **真实时间度量分桶**：毫秒按 10 倍量级、分配字节按 2 倍量级分桶后才入基线，比较只做上限检查，基线不随机器抖动。
10. **可破坏障碍（真表达，M3-E2 取代此前的近似）**：靶子条目声明 `block_half_extent`（`lab.dummy_set` 的可选字段）即动态阻挡，`kind = breakable` 不声明时缺省 0.5，其它 kind 声明了同样生效、不声明就不挡路。宿主在地形阻挡之外追加以出生点为中心的占位矩形，被打死后经 `INavigation2D.SetBlocking` 批量替换去掉，导航阻挡版本随之递增（`GetBlockingVersion`；手感设计 06 第 10 节勘误 4：动态阻挡用既有导航接口，不另造机制，核心层不需要新增能力）。不再限手感场景：基础靶子集（`lab.dummy_set.lab_standard`，旧脚本用）与手感靶子集（`lab_action`）里的 `breakable` 都显式声明了 `block_half_extent: 0.5`；任何场景里阻挡变更都另记一条 `blocking_changed` 逻辑事件（`LabRecording.Events`，来源空、目标为障碍标签、数量为剩余阻挡数、`Detail` 为 `v<阻挡版本>`），手感场景仍由 `feel_breakable` 的 `motion.blocking_updates` 度量守（基线不变）。旧脚本里没有选 `breakable` 分组的，指纹与基线逐字不变。
11. **确定性**：命中表随机项在实验室数据里关闭；靶子默认不带 AI（`ai: true` 才保留）；不死木桩用 `power_floors` 的最低保留线实现"吃伤害不死"。
12. **随发布产物分发**（M2-D 取代此前"不进分发包"的决定，理由：06 第 8 节第 2 步要求游戏用实验室在自己的数据上校准手感，只消费发布产物的游戏不能依赖"框架一侧代跑"）：`toolchain/feellab` 与 SimRunner 同一治理方式——`FeelLab.csproj` 在源码树存在时 `ProjectReference` 内核，不存在时改引用 `lib/` 下 9 个预编译 DLL（内核 + Core.Sim + Adapters.Stub + 六个核心 DLL）；`build.ps1` 把预编译产物拷进 `dist/<版本>/toolchain/feellab/{bin,lib}`（含空 `Directory.Build.props`，MANIFEST 新增 `[feellab]` 段记 `FeelLab.dll`/`Lab.Kernel.dll` 的 sha256；lock 不扩字段，同 SimRunner），并把 `data/_lab`、`data/_lab_action`、`data/_equip`、`lab/fixtures` 按仓库同路径拷进 dist 根（`equip_cycle` 脚本声明了 `data/_equip` 作额外数据根，缺它 suite 抛"数据根目录不存在"，所以一并分发）。私服包落点是 `com.gamefoundation.toolchain`（命令行与预编译产物本来就在它的 `Tools~/` 里），另在 `Tools~/feellab/labroot/` 放一份自包含的实验室根（含 `data/_framework`、`data/_feel` 各一份，约 85 KB），让它不依赖同时装 framework-data 包；实验室数据不进 framework-data 包、不同步进游戏的 StreamingAssets（验收设施数据，不是运行期框架数据）。游戏仓库也仍可直接引用内核工程（内核是 netstandard2.1，与核心库同目标框架）。
13. **基线单文件多格子**：每个脚本一份基线文件，内含六个格子的 `{key, groups}`，比 54 个小文件更易评审且同脚本的跨格子差异在同一处可见。
14. **脚本再生**：标准脚本是手写数据，不由程序生成；改脚本即改 `scriptVersion` 并重烘焙该脚本的基线。

15. **换装场景（`equip_cycle`，手感设计/06 第 3.6 节）**：脚本 meta 的 `scene = equip` 让宿主额外装出换装链的全部生产部件（`EquipRig`：手感解析器 + `EquipmentFeelProvider`、`EquipmentFeelChain`、`WeaponActionBinding`、`PoseSelector` + `EquipmentPoseBridge`、外观/武器表现档案来源、装备面板视图模型），脚本里的 `equip`/`unequip` 事件经它执行；每次穿脱之后（该宿主固定步末尾）采集一份运行期事实快照，并记录换装后下一次普攻的相位 tick 实测值与由规则（时间线毫秒 × 解析出的相位倍率，经标定换算）算出的期望值。
16. **脚本格式版本 2 只在用到时才写**：新增 meta 字段（`scene`、`feelCalibrationId`、`extraDataRoots`、`extraDataExcludeTables`、`extraDataExcludeRows`、`weaponAttackSkills`、`unarmedAttackSkill`）与 `equip`/`unequip` 事件；`InputScript.FormatVersion` 仍是 1，既有九个脚本序列化文本与基线逐字不变，只有换装脚本写 `formatVersion: 2`。内核读取的最高版本升到 2。
17. **脚本级数据集**：占位装备集（`data/_equip`）与手感数据（`data/_feel`）不进实验室基础数据集（否则会改全部既有基线的数据集哈希与指纹键），而是由脚本 meta 声明 `extraDataRoots`，`LabRunner.DatasetFor(script)` 按脚本合并出独立数据集（按脚本 id 缓存）；基础数据集与既有 54 条基线不受影响。合并时与基础数据集主键重复的表/行由 `extraDataExcludeTables`/`extraDataExcludeRows` 显式剔除（目前是 `l10n.locale` 整表与 `l10n.power.health.name` 一行），不静默去重。实验室专用数据（六个换装普攻技能、自我靶链、显示映射行）放在 `data/_lab`（已有 `lab_dataset` 模块映射）。
18. **武器 → 普攻技能映射用内存覆盖注入**：占位 `feel.weapon` 行没有 `auto_attack_timeline_ref`，脚本 meta 的 `weaponAttackSkills` 在装载时对 `feel.weapon` 表做内存改写（不动磁盘上的共享占位数据），核心代码读的仍是数据里的真实字段 `auto_attack_timeline_ref`。这是已知局限，见下。
19. **度量组按需出现（`IConditionalMetricGroup`）**：`equip` 组只在记录带换装记录时计算；指纹比较器对"基线与实际都没有的条件组"跳过，因此既有指纹里没有这一组、既有基线逐字不变。该组度量里凡"缺失/不一致"类（`icon_missing`、`visual_missing`、`layer_fallbacks`、`ui_mismatch`、`pose_family_mismatch`、`unrefreshed_steps`、`stale_version_steps`、`attack_mismatches` 等）期望恒为 0，是"运行期检查与导入校验静态报告零差异"的运行期一侧（静态一侧见 `toolchain/tests/test_equip_runtime_zero_diff.py`）。
20. **tickRate 必须是 60**：动作时间线按 `SkillOptions.ActionStepSeconds`（缺省 1/60）把毫秒换成 tick，而 `GameplayAssembly` 目前不把宿主步长回填进去；换装场景在 tickRate 不是 60 时显式抛 `LabFormatException`，不静默按错的步长算。
21. **首次普攻不走 `IActionSkillBinding`**：该接口目前只在"取消进入"路径被动作管线使用，空闲状态下的第一下普攻由宿主在换装场景里经 `WeaponActionBinding.TryResolveAttackSkill` 解出技能 id，再提交 `cast` 意图。这只描述实验室换装装置；生产装配的输入缓冲入口已用武器优先的绑定（武器 `auto_attack_timeline_ref`，没有时回落槽位绑定），首次普攻也走它。
22. **手感场景脚本（`meta.feel`，格式版本 3）经生产装配运行**：`feel = true` 的脚本让宿主在 `HeadlessWorldOptions.FeelOptions`（`CarriersFeelOptions`：标定行 id、本地移动动作名）上走生产装配——输入缓冲、动作时钟、`HitFeelHost`、目标辅助、运动层全部是生产实现，不是替身；
本地输入经 `InputBuffer.BindLocalInput` 进缓冲，技能槽位经 `SkillBindings.Bind` 绑定（`found.input_action` 行声明动作类别、缓冲时长与槽位 id，脚本 `skillSlots` 声明槽位到技能，`learnSkills` 追加玩家要学的技能）。
预设取脚本 `presetId`，空则取格子缺省；标定行取 `feelCalibrationId`，空则按 `feel.calibration.lab_<预设短名>` 推出（`LabHost.CalibrationFor`）。没有声明 `feel` 的旧脚本走原路径，序列化与指纹逐字不变（版本 3 字段只在用到时才写，同判断记录 16）。
手感场景里旧的"按钮边沿直接提交施放意图"路径不再使用，所以 `casts_submitted`、`settle_ticks`、响应组的施放延迟在手感场景指纹里为空/-1（手感度量组承担这些事实）。

23. **动作式数据放 `data/_lab_action`，不进 `data/_lab`**：与判断记录 17 同理，改 `_lab` 会改既有 60 条基线的数据集哈希；新技能（`timeline`、`target_shape_ref` 用新建的扇形链 `target.chain.lab_a_arc`）、输入动作、两条标定行、`feel.action`/`feel.character` 覆盖行、创建物（含会被一击打死的低血量靶、带 `feel_ref` 的精英、无攻击轮换的巡逻靶）、靶子集 `lab.dummy_set.lab_action` 全在这里，由脚本 `meta.extraDataRoots = [data/_feel, data/_lab_action]` 叠加。
脚本 `meta.dummySet` 覆盖格子缺省靶子集；格子目录里的 `skill_bindings`（旧动作名到旧技能）在手感场景里被忽略。巡逻靶用 `ai.rotation.lab_a_passive`（空轮换）——否则它会反过来打玩家，脚本变成对打。

24. **目标选择式变体：运行时剥 `timeline`，不另存第二份数据**：同一批技能在目标选择式格子里要"瞬发、无时间线"。`LabDataSources.StripTimelines` 在装载时改写额外数据根的 `skill.def`（去掉 `timeline` 块并把 `cast_time` 置 0——带 `timeline` 的技能 `cast_time` 必须等于三相之和，剥块不置 0 会变成读条技能），
只对 `meta.feel` 的脚本生效；`LabRunVariant.StripTimelines` 为 `null` 时按格子结算模式取（目标选择式剥、动作式保留），也可显式指定（不变量 2 靠它）。数据集因此最多有"保留/剥掉"两份派生集（`LabRunner.DatasetFor(script, cell, variant)`，按脚本 id + 变体键缓存，装载一次），目标选择式格子的数据集哈希与动作式不同。

25. **跨格子不变量**（ADR-0122 决定 4；`LabInvariants`，命令 `feellab invariants`，测试 `CrossCellInvariantTests`）：
(1) `planar_combos_logic_equal`：同脚本、同结算模式，2D / 2.5D / 3D 三个平面组合的逻辑类度量逐项一致（每脚本 2 种结算 × 2 次比较）；
(2) `action_stripped_equals_targeted`：动作式格子在变体 `{剥 timeline, 预设换成目标选择式缺省}` 下与同组合目标选择式格子逻辑一致（每脚本 3 个组合；只换一半都不等，`ActionStrippedInvariant_IsNotVacuous_…` 锁住它不是空判）；
(3) `feel_assembly_transparent_under_classic`：目标选择式格子里手感装配开着与旧路径（变体 `FeelOff`，按钮边沿直接施放）在世界结局度量（移动、命中、伤害、击杀）上一致——经典预设下手感装配是透明的；提交/结算时序类度量按构造不同，不在比较范围（这是自拟的第三条，目的是防"手感装配本身在经典预设下改了行为"）。
(4) `space_semantics_only`（判断记录 35）：空间格子（`side_2d`/`volume`）按 `plane` 运行后逻辑组与同结算模式平面格子一致；无空间刺激的运行里除 `space` 组外一致——"同一脚本在不同 space 下逻辑组的差异只来自空间语义"。只对适用空间格子的脚本（`space.*`）出现。
**表现类度量组不参与跨格子比较**，每个格子各自一份基线（06 第 1.1 节不变量 3；`PresentationGroup_IsPerCell_…` 验证动作式与目标选择式的表现组确实不同）。
运行方式：不是 `suite` 的一部分（`suite` 只做基线比较），单独 `dotnet run --project toolchain/feellab -- invariants`，同时随 `Tests.Lab` 进门禁。

26. **手感度量组的口径**：约定写进各组 `MetricSpec` 描述。要点——入槽 tick = 脚本按下注入的 tick（按钮边沿同 tick 进缓冲），消费 tick = 该输入换来的动作开始 tick，二者按先进先出配对（丢弃事件按动作名摘最早待配对按下）；
动作各相 tick 数是相变事件之间的 tick 差，所以生效相里含命中顿帧冻结的 tick（动作时钟暂停）；空间命中的施放序号用"在命中确认里首次出现的先后序号"而不是引擎内部实例号（后者在两种结算模式间不同）；
条件组在基线与实际都没有时被比较器跳过（判断记录 19）。逻辑事件采样来自 `FeelRig`：订阅生产事件（`action.*`、`combat.hit_confirmed`、`unit.stagger_*`、顿帧起止、缓冲离开、阻挡变更）并在每个推进 tick 末采样缓冲槽、玩家运动层状态与靶子位置/非常态模式；实体一律用出场标签（`player`、靶子集条目名），不用实体 id。

27. **表现时间线用真实反馈包流水线 + 记录型假 sink**：`FeedbackBinder` + `ImpactPipeline` 是生产代码，`FeedbackRule(combat.hit_confirmed → PlayImpactAction)` 与实验室自带的冲击档案 `LabImpactProfile`（light/medium/heavy 三档、命中/击杀/回避/挥空四个结局、镜头增益 0.6/1.0/1.6、击杀强度倍率 2、音效层 swing/whiff/impact/sweetener 各三档 `sfx.lab_*` 行）装进去，
出批指令记进 `FeelRecording.Presentation`。档案放在内核里而不是数据里是为了不碰 `data/_lab`/`data/_feel`；指令数值按绝对允差比较。

28. **靶子出手：`cast` 事件**：精英起手重击、普通靶被打断这类场景需要靶子施法。脚本事件 `kind = cast`，`actor` 是靶子出场标签，`action` 是技能 id；宿主让该靶子学会技能后直接提交施放（`SkillHost.CastSkill`）。事件里的 `actor` 字段只在 `cast` 事件写出；行动者不在本次出场的靶子里时抛 `LabFormatException`（不静默忽略）。
精英场景里玩家用 `bolt`（投射物）远程命中——精英的扇形扫不到 3 个单位外的玩家，避免脚本变成互殴。

29. **实验室以 Walk 模式提交移动**：`MoveRequest.InDirection` 缺省 Walk，宿主沿用（旧脚本同）；预设的 `walk_speed_ratio` 因此直接进稳态速度：动作式格子稳态速度 = `walk_speed_ratio` × 标定基础移速（0.5 × 5 = 2.5），目标选择式 1 × 5 = 5。运动脚本的期望按这条推。
线性加速 `accel_ms` 按 tick 取整后每 tick 前进 1/N；线性制动以"当前速度与基础移速较大者"为参照（`MotionMath.ApproachSpeed`），所以半速起步的制动 tick 数 = ⌈速度/参照 × N⌉。

30. **蓄力比例原样记录**：蓄力键松开才出手（缓冲里的蓄力类按钮取按住时长），`chargeRatio = (按住 ms − min_ms) / (max_ms − min_ms)` 夹到 [0,1]；本切片不预判它对数值的影响（`timeline.charge` 目前只影响比例与校验，见 `core/rules/skill/README.md` T11），脚本只记录三种按住时长的比例与出手 tick。

31. **S11 缺口修复（时间线投射物命中确认）**：见 `core/rules/skill/README.md` T31。实验室侧的凭据是度量 `spatialhit.unconfirmed_damage`（有伤害无确认，恒 0）、`duplicate_confirmations`（恒 0）、`confirmed_without_damage`（恒 0）三个不变量计数，任何脚本违反都会让基线比较红；`feel_projectile` 覆盖 release 标记与只有 hit 标记两种形态。

32. **如实记录、不在本切片处理的观察**（见已知局限）：冲刺结束后的滑行、投射物挥空提示先于命中、非攻击技能的挥空提示、位移穿过靶子。其中前三条已在 S12 修复（判断记录 33），第四条仍在。
33. **S12 实验室修复（冲刺越程、非攻击动作挥空、投射物挥空时机）**：三条观察各先写复现用例（`lab/tests/FeelSceneTests.cs`，修复前失败），再改生产代码，实验室只是把问题测出来的凭据。(1) 冲刺越程：动作位移窗口结束后速度清零（运动档案新增可选字段 `keep_momentum_on_motion_end`，缺省假；手感设计/02 第 4 节），`feel_dash` 终点 x 由 2.577 变为声明的 2.0，`feel_lunge`、`feel_dodge_cancel`、`feel_buffer_lead` 同理；期望值 = 标定参考身高 × 动作声明的位移距离。(2) 非攻击动作不开挥空窗口：`action.started` 新增 `isAttack`，反馈流水线不为非攻击动作开窗（手感设计/07 第 6 节）。(3) 投射物动作的挥空等投射物结局：新增事件 `action.projectile_launched`/`action.projectile_ended`，判定相结束时弹还在飞则窗口留着，命中（含迟到命中）取消挥空，到期/被挡才补播（手感设计/03 第 2.5 节）；`feel_projectile_miss` 是未命中形态的基线，期望的挥空 tick = 弹被挡住的 tick。基线只更新了这三处修复正是意图的条目（逐条清单在 S12 汇报里），原有 60 条基线与 S7b 其余脚本的基线一字未动。

34. **M2-C 单位间体积阻挡脚本 `feel_unit_block`**：玩家（-1.5, 0）按住 +x 走向木桩（2, 0），第 90 tick 松手，第 100 tick 按闪避；预设 `feel.preset.unit_block`（继承 `arpg_responsive`，`unit_body_radius` 0.4 身高）与标定 `lab_unit_block`（参考身高 1.0）放在独立数据根 `lab/fixtures/data/unit_block/`，经该脚本 `meta.extraDataRoots` 叠加（不进 `data/_feel`/`data/_lab_action`，否则既有脚本的数据集哈希会变，同判断记录 17/23）。脚本钉死预设（`meta.presetId`），所以六格都按体积预设跑（目标选择式格子同样被挡）；`feelCalibrationId` 留空由 `lab_<预设短名>` 推出，运行变体换预设（如 `unit_block_pass`、`arpg_responsive`）时标定行随之换。实测：玩家停在 x = 1.1990740740740742（体积边界 1.2 减一个到达容差内），中心距在每个 tick 都不小于半径之和 0.8，闪避不改变位置；换成 `unit_block_pass`（`dodge_through_units`）闪避穿过木桩，换成 `arpg_responsive`（不声明体积）一路走进木桩。期望值在 `FeelSceneTests` 里由预设半径 × 标定参考身高算出。**`LabInvariants` 为钉死预设的脚本做了两处调整**（既有脚本不受影响，其条目数不变）：动作式剥离不变量的"目标选择式格子"比较对象的预设取脚本钉死的预设而不是格子缺省预设；手感装配透明性不变量（"换成 `rpg_classic` 后装配透明"）不适用于钉死了预设的脚本，跳过（它的预设是脚本自己的主张）；`CrossCellInvariantTests` 的条数断言相应改为只统计未钉预设的手感脚本。
35. **空间语义（M3-E1，手感设计/06 第 10 节勘误 9）**：`space` 三个取值在无头宿主上不再只有标签差异。核心层新增竖直轴能力（单位载体 `MovementOptions.Vertical` + `VerticalMotionHost`、目标链 `shape.height` + `TargetingOptions.VerticalHit/SpatialDistance`、受击裁决 `launch_height` → `ILaunchSink`），都是加法、缺省关闭，实验室只是按格子的 `space` 打开它们，没有自建平行机制。宿主口径：`plane` 不装配任何空间能力（跳跃请求被拒绝并计数、靶子声明的高度被忽略、击飞不生效，行为与此前一致）；`side_2d` 装配竖直轴 + 命中高度窗口，并把摇杆的竖直分量当"深度"丢掉计数；`volume` 装配竖直轴 + 命中高度窗口 + 三维最近距离，深度自由。格子行可选 `gravity`、`jump_height`（缺省 30、1.5；两个横版格子取缺省，两个体积格子取 20、2 以便区分），靶子条目可选 `height`（静态出生高度，不受重力）；脚本里 `input.action.lab_jump` 的按下沿是宿主级跳跃请求（不进输入映射与缓冲）。度量组 `space` 是条件组（`IConditionalMetricGroup`：格子带竖直轴、脚本含跳跃事件或靶子声明了高度时才出现），既有脚本在平面格子上没有它，既有基线逐字不变（suite 186 条原有基线全部通过）。命中高度差取伤害 tick 的前一个 tick 末尾的高度（核心 tick 里技能管线早于竖直积分）。数据放 `lab/fixtures/data/space/`（格子行、靶子集、两条目标链、两个技能、击飞预设 `space_launch` 与标定 `lab_space_launch`），由六个 `space.*` 脚本经 `meta.extraDataRoots` 叠加（与判断记录 17/23/34 同理）；脚本 id 带点是因为格子的 `script_subset` 是 Id 列表（点分 id 语法），基线文件名随之是 `space.<名>.baseline.json`。每个空间脚本十个格子各一份基线。**新增跨格子不变量** `space_semantics_only`（`LabInvariants.CheckSpaceSemantics`）：空间格子按 `plane` 运行（变体 `SpaceOverride`）后逻辑组与同结算模式平面格子逐字节一致；运行里空间语义从未被触发时（对照脚本 `space.neutral`）空间格子除 `space` 组外与平面格子一致。实测（`SpaceSemanticsTests` 里期望由重力/顶点/`launch_height`/倍率算出）：横版格子跳跃整段飞行 38 步（g=30、H=1.5：ceil(2·v0/(g·dt))）、体积格子 54 步（g=20、H=2）；命中高度窗口下平面格子每次挥击打中两个靶，竖直格子只打中高度差不超过形状高度 1 的那个；击飞木桩顶点 1.2（`launch_height`）、横版 34 步落地、体积 42 步落地；横版深度偏移 0（丢弃 2 个竖直输入）而体积 2（目标选择式）/ 0.926（动作式，动作式预设的走路速度比不同）；最近目标平面/横版选悬空的近靶，体积选地面的远靶。`LabTestSupport.FeelScripts()` 不含 `space.*` 脚本（它们是十个格子，由 `SpaceSemanticsTests` 验收），`SpaceScripts()` 取它们。

36. **M3-A 单位推开脚本 `feel_unit_separate`**：同判断记录 34 的钉死预设 `feel.preset.unit_block`，玩家出生在 (1.7, 0)，与木桩 (2, 0) 体积重叠（半径之和 0.8、中心距 0.3），全程没有输入。两个单位按 `unit_separation_speed_ratio` 缺省 0.5 分摊深度，玩家每 tick 位移不超过 比例 × 标定基础移速 × 步长，累计位移不超过分摊的一半深度；足够长之后中心距恰为半径之和（实测玩家停在 x = 1.45，木桩被推到 2.25）。对照预设 `feel.preset.unit_separate_off`（比例 0）放在独立数据根 `lab/fixtures/data/unit_separate/`（与 `unit_block` 数据根同理不进共享数据根，否则既有脚本数据集哈希会变），脚本通过 `meta.extraDataRoots` 叠加两个根；测试用运行变体换成该预设，玩家原地不动。路径跟随与追击的局部绕行、推人在实验室里没有可观测的玩家侧轨迹（记录里只有靶子的出生位置、脚本只有轴输入），只由 `MotionArbiterTests.UnitVolumeLimits` 覆盖。新增后 `suite` 共 192 格（原 186）、跨格子不变量 284 条（原 277），既有基线一字未改。

37. **脚本期望清单（M3-E2，手感设计 06 第 3.1 节）**：`InputScript.Expectations`，格式版本 4（`ExpectFormatVersion`），只有带期望的脚本才写版本 4，其余脚本的序列化文本与基线逐字不变，版本 1～3 照常读取；`MaxSupportedFormatVersion` 升到 4。实现在 `lab/core/Expectations.cs`：`Expectation`（格式、解析、规范写出）、`ExpectationEvaluator`（运行期判定与诊断）、`ExpectationExporter`（导出生成）。判断：期望不属于脚本行为身份——增删期望不改 `scriptVersion`、不改指纹键与度量；期望判定与基线比较各自独立，任一不通过该格子都算 `Diff`（沿用 `CellStatus.Diff`，不新增枚举成员，`suite` 汇总行 `RESULT total=… diff=…` 格式不变，门禁解析不动），期望自己的汇总另起一行 `RESULT expectations …`；未知字段、重复 id、拼错的度量组/度量/格子都不静默忽略（解析期或套件开跑前抛 `LabFormatException`，退出码 5），运行期取不到值的记为 `Error` 而非通过；`eq` 对数值用 1e-9（度量本身已规整到 9 位小数）、对文本与数组按规范文本逐字；跨格子相对关系按需运行对照格子并在同一脚本内缓存。
38. **导出为测试产出期望（M3-E2，06 第 3.5 节）**：`LabSuite.ExportAsTest(runner, script, dir, onlyCell, expect, writeExpectations)` 在写脚本与基线之外，由各格子指纹生成期望清单（规则与取舍见上文「脚本期望清单」一节）。判断：导出的期望自动 id 以 `auto.` 为前缀，再导出只替换这一类、手写的保留（否则一次重导出就会丢掉人写的断言）；合并规则按取值分桶（取值全一致不带 `cells`），这样清单短、可编辑；实时类默认不导出（随机器抖动，基线按量级分桶已守着）；不变量测试对每个入库脚本的每个适用格子证明"导出的期望在同一次运行上必然全部通过"。
39. **靶子数据声明（M3-E2，06 第 2 节）**：`lab.dummy_set` 条目新增两个可选字段——`block_half_extent`（判断记录 10）与 `poise`（判断记录 40），声明在 `core/sim/schema/LabSchemas.cs`，类型化读取在 `LabCatalog`（`LabDummy.DeclaredBlockHalfExtent`/`BlockHalfExtent`/`Poise`），旧构造保留（只增不改）。缺省不声明即与此前逐位一致。
40. **不死木桩可选韧性（M3-E2）**：靶子条目声明 `poise`（≥ 0），宿主在靶子出场后用 `StatHost.SetBase` 写进 `stat.poise`（受击裁决读取的缺省韧性属性，`HitFeelOptions.PoiseStat`），裁决规则不变：攻击的 `stagger_power` ≤ 韧性只 `Flinch`，否则按冲击等级映射再受 `reaction_cap` 封顶。`stat.poise` 的属性定义放在 `data/_lab_action/stat/stat.definition.json`（与判断记录 23 同理不碰 `data/_lab`），声明了 `poise` 而数据集里没有该属性定义时宿主抛 `LabFormatException` 并指明缺什么，不静默忽略；写属性会让总线多一条 `StatChanged` 事件，只影响声明了韧性的脚本的 `performance.event_count_total`。新增靶子 `stake_tough`（`lab_action` 靶子集，`poise = 1`，对应预设的普通击强度 1、终结击强度 2）与脚本 `feel_stake_poise`（三段连招打韧性木桩：前两击 Flinch、终结击 Knockback；目标选择式格子经典预设不装受击裁决，反应恒为 None，韧性不改变它）。
41. **引擎实验室宿主（M4-H，06 第 4 节引擎宿主）**：引擎宿主不是第二套循环，而是同一个 `LabHost.Run` 加一个扩展 `LabHostExtension`（`lab/core/LabHostExtension.cs`：挂接、包装视图工厂、反馈分流、就绪、移动轴转换、每固定步末尾、每表现帧、收尾七个钩子，`CompositeLabHostExtension` 可叠加）。判断：①扩展只能在内核已有的相同时序上观测与分流，不得改逻辑；引擎侧装配与驱动的任何异常都被吞进 `EngineRecording.Errors`（度量 `engine_errors`），逻辑组指纹因此在引擎宿主上与无头宿主逐字节一致，这是新增的跨宿主不变量（Unity PlayMode 用例 `FeelScripts_LogicFingerprint_IsByteIdentical_OnEngineAndHeadlessHosts` 对全部手感场景脚本逐字节比较）。②引擎侧度量是条件组 `engine`（`MetricRegistry.CreateWithEngine()` 才有，缺省注册表不含，所以无头 `suite`/`invariants` 的指纹与基线一字不变）：命中帧对齐（样本数、缺失数、最大绝对误差、平均误差）、镜头冲量曲线（与线性衰减的最大偏差、单调、残余）、顿帧期间 rig 与粒子的推进量（冻结目标应为 0，旁观 rig 与对照粒子应大于 0）、帧耗时 p50/p95/max（真实时钟，只进实时类）、相机相对输入误差、换装图层核对不一致数、引擎失败数。③命中帧对齐：逻辑侧取动作标记 `hit`/`hit_frame`，引擎侧取精灵 rig 帧动画关键帧 `hit_frame`（攻击剪辑）或 `release`（读条施法剪辑——手感场景的技能走 cast 剪辑，数据里它的关键帧是 `release`），模型 rig 取命中帧注册表；按"引擎事件配给不晚于它的最近一个逻辑命中"配对，不用 FIFO（丢事件时 FIFO 会把后面的事件错配给前面的命中，制造假的大误差），没配上的记缺失（例如读条剪辑在到达施放点前被下一次施法切走）。④镜头冲量曲线直接采样真实 `UnityCamera` 的冲量位移模长，触发时刻的第一个采样点是峰值；衰减结束前又来冲量的曲线标 `Truncated`，不进单调/残余/线性偏差统计。⑤顿帧：冻结目标的 rig 动画时钟取精灵帧动画播放器累计推进量、模型动画器"同一状态内"的归一化时间累计增量（状态切换与过渡只重设基线，避免把换状态当成动画在走）；旁观与对照由"该冻结窗口内从未被冻结"的单位担任，探针粒子由宿主自己挂在被冻结单位名下和旁观单位名下（实验室冻结档案 `particles=true`，宿主选项 `ProbeParticles` 可关，关掉不改逻辑）。⑥渲染隔离：舞台根物体放在专用层（缺省 30），舞台相机只渲染这一层，场内其它相机的剔除遮罩在存续期间清掉这一层、销毁时恢复，销毁后不留物体；组件自己的 `Update` 全部关掉，由舞台按模拟时间推进帧动画、特效序列、动画器、相机，所以快放、慢放、批处理下结果都可复现。⑦输入噪声模型 `InputNoiseModel`（延迟 tick 数、抖动、丢事件，种子化）与回放记录 `InputNoiseRecord`：噪声在脚本层改写事件，记录后可逐字节回放；纯延迟噪声使逻辑响应恰好晚延迟的 tick 数。⑧三个平面组合：宿主在 `2d_*`、`2_5d_*`、`3d_*` 格子上各跑一遍，精灵平面用精灵 rig、3d 用模型 rig，逻辑组指纹彼此逐字节一致（`ThreePlanes_...` 用例）。⑨`camera_relative`：框架没有相机相对输入语义，转换放在宿主层（`ConvertMoveAxis`）——设备轴按真实相机的右/上轴在世界平面上的投影换算，`ControlSpace` 给出偏航公式；检验三向：转换结果与偏航公式的夹角、世界方向经真实相机 `worldToCameraMatrix` 投回屏幕与摇杆方向的夹角（两者容差都是 0.01 度）、终点位置与"预先旋转摇杆的无头运行"一致（1e-3）；偏航 0 恒等。第三人称偏航只用适配器新增的可选开关 `UnityCamera.ApplyYawRotation`（缺省关，旧行为不变）。⑩换装场景核对渲染侧：图标按资源引用约定找到文件、用引擎解码，宽高大于 0 且与本次其余图标的众数尺寸一致；每个穿戴装备图层在姿势族的待机/攻击剪辑下、五个朝向各经真实资源加载器加载一次，帧数与帧尺寸必须与同剪辑同朝向的身体层一致；加载不出来、帧数或尺寸为 0、与参照不一致都记不一致。静态数据对账（`item_facts`）仍是 M4-L 与导入校验的事，这里只管"实际加载出来的是什么"。⑪覆盖存储 `OverrideStore`（`lab/core/OverrideStore.cs`，纯内核）：本地文件 JSON，一个覆盖集 = 名字 + 若干"手感字段 + 操作（set/multiply/add/remove）+ 值 + 作用单位（空为全局）"的写入，经 `OverrideExtension` 在就绪时写进手感装配的运行期调试覆盖层（`DebugOverrides`），源数据与磁盘上的档案文件一字不动（`Overrides_DoNotChangeSourceData_OnlyTheStoreFile` 证明）；写入按字段登记校验（字段未登记、字段不允许该操作、值种类不对都拒绝并返回问题清单，不碰存储文件）；`AbComparison.Compare(a, b)` 对同一脚本同一格子的两组覆盖逐度量求差（B − A，每条差异带度量类别，实时类随时钟抖动、由调用方过滤），交换 A、B 差值互为相反数。⑫面板：Editor 窗口 `GameFoundation/手感实验室`（`Editor/LabHost/FeelLabWindow.cs`，IMGUI，需 Play Mode）只做绘制，全部状态与逻辑在纯 C# 的 `LabPanelModel`（`Runtime/LabHost/LabPanelModel.cs`）；存储文件固定在 `<Unity 工程>/Library/FeelLab/overrides.json`（本地，不入库）。⑬引擎宿主是 Unity 包里的可选组件：独立程序集 `Adapter.Unity.LabHost`（不自动被引用，不进玩家构建），实验室内核与它依赖的核心库由 `build.ps1` 第 3b 步同步成 `Runtime/Plugins/Lab/` 下的预编译库（被 `.gitignore` 忽略，与第 3 步的核心库同一做法）。

## 已知局限


- 标准脚本集：06 第 3.1 节的短按、小幅轴、反转、斜向、贴墙、绕柱、边走边打、打完即停、群体命中、换装循环（十个旧脚本）加 S7b 的十九个、S12 的一个、M2-C 的一个（`feel_unit_block`）、M3-A 的一个（`feel_unit_separate`）与 M3-E2 的一个（`feel_stake_poise`）手感脚本（共二十三个），再加 M3-E1 的六个空间语义脚本（`space.*`，见判断记录 35）；"被精英打断"拆成了霸体窗口内（`feel_elite_armor`）、窗口后按反应上限封顶（`feel_elite_flinch`）与普通靶被打断（`feel_interrupt`）三种，因为精英的 `reaction_cap = flinch` 本身不会被打断。
- 度量组：响应、移动、攻击、性能四个常驻组，换装与七个手感组为条件组（装备解析、手感场景）。
- `expectations` 与"导出为测试"产出期望已实现（判断记录 37、38）；期望在 `suite`、`run` 与引擎宿主上都判定（引擎宿主上的判定与无头逐条一致，判断记录 41）。导出不导长序列文本与实时类（见「脚本期望清单」），生成的期望只是起点，由人编辑。
- 三个平面组合（2D / 2.5D / 3D）在无头宿主上只有标签与假适配器视图的差异，逻辑判定完全相同（这正是"平面格子间逻辑组指纹逐字节一致"不变量要证明的）；引擎宿主在三个平面组合上各跑一遍并证明逻辑指纹一致（判断记录 41）。竖直轴空间（`side_2d`/`volume`）有真实语义，见判断记录 35 与其已知局限。
- 靶子数据已能表达可破坏障碍（`block_half_extent`）与木桩韧性（`poise`）；韧性只读缺省的 `stat.poise` 属性（游戏改了 `HitFeelOptions.PoiseStat` 要在自己的数据里按同一属性写），伤害削减韧性、脱战回复这类韧性动态变化不在实验室数据表达范围内。动态阻挡是整批替换（导航契约语义），一次死亡重发全部阻挡矩形，靶子很多时开销随阻挡数线性增长；目前一张图只有一个障碍，不构成问题。
- 无头宿主的表现时间线是反馈流水线出批给假 sink 的指令，不含动画切换、真实音频与镜头平滑；这些由引擎宿主观测（判断记录 41）。
- **真机手测没做**：引擎宿主在批处理（无图形）下跑，证明的是适配器把逻辑指令画对的各项机制，不替代真人在真实设备上的试玩评分；真实输入设备的轴/按键抖动只有可注入的噪声模型（判断记录 41），没有真机采样。
- **引擎宿主帧耗时**是驱动器与适配器的 CPU 侧耗时（真实时钟、随机器抖动），不含 GPU 渲染，只做粗上限断言，不进基线。
- **命中帧对齐的现状数据**：手感场景的技能走读条剪辑 `cast`，数据里它的关键帧是 `release`（不是 `hit_frame`）；精灵平面上引擎事件时刻 = 施法起点 + 剪辑里关键帧的时刻，比逻辑命中标记晚（`feel_melee` 实测晚 150 毫秒），连续施法时前一次读条剪辑被切走会记缺失（`feel_combo3` 三次命中只有最后一次配上）；模型平面占位的 cast 骨骼剪辑不带 `release`/`hit_frame` 动画事件，命中对齐恒为缺失。这是数据与占位美术的事实，不是宿主缺口；指标把它如实量出来。
- **`camera_relative` 的宿主层换算**：框架没有相机相对输入语义，换算在宿主层完成并用真实相机三向检验（判断记录 41）；`UnityCamera` 没有俯仰与透视（`fixed_pitch` 未实现），第三人称偏航只靠可选开关 `ApplyYawRotation`。框架原生支持相机相对输入、俯仰与透视后，宿主层换算应拆除改用原生能力。
- 实验室冲击档案与 sfx 行是内核内置的（判断记录 27），游戏仓库跑自己的实验室时用自己的档案。
- **观察 1（冲刺滑行，S12 已修）**：原先冲刺位移窗口结束后玩家仍按"从末速度线性制动"滑行一小段（`feel_dash` 终点 x 2.577，声明 2.0）。现在位移窗口结束速度清零，终点恰为声明距离；需要滑行的动作在档案里声明 `keep_momentum_on_motion_end`（见判断记录 33）。
- **观察 2（投射物挥空提示先于命中，S12 已修）**：原先 `feel_projectile` 发射 tick + 3 出挥空提示音，+5 才命中。现在挥空等投射物结局：命中则不挥空，没命中到被挡/到期那一刻才挥空（`feel_projectile_miss`）。**已知限制**：实验室里"到期"与"清场"两种结局原因没有脚本级覆盖（竞技场的直墙让未命中的弹都以"被挡住"收场），这两种原因由 `core/carriers/projectile/tests/ProjectileHitHookTests.cs` 与 `presentation/feedback_binder/tests/ImpactPipelineTests.cs` 单元级覆盖；多段技能同一动作实例里先后发射的弹按动作实例合并等待（不按段拆）。
- **观察 3（非攻击技能的挥空提示，S12 已修）**：原先 `feel_dash` 里无命中标记的闪避结束时也出挥空提示音。现在非攻击动作（`action.started.isAttack` 为假）不开挥空窗口。
- **观察 4（位移穿过靶子，M2-C 已提供可选开启的体积阻挡）**：`feel_lunge` 的扑击位移 `blocking: stop` 只对地形阻挡生效，玩家会穿过木桩（靶子不是阻挡），终点超过木桩位置，该脚本与既有预设不变。需要的预设声明 `unit_body_radius` 即可开启单位间体积阻挡（脚本 `feel_unit_block`，判断记录 34）。
- 换装场景：`GameplayAssembly` 不回填 `SkillOptions.ActionStepSeconds`，所以换装脚本必须 tickRate 60（装置显式抛错，见判断记录 20）。
- 换装场景：实验室换装装置自带一套手感装配、换装链与姿势选择器，不经生产装配的手感装配（生产装配开启手感时由 `PresentationAssembly` 装出 `PoseSelector`/`EquipmentPoseBridge`，载体层手感装配装出换装链，见 `presentation/assembly/README.md`、`core/carriers/assembly/README.md`）；保持自带是为了既有换装基线逐字不变。生产链路由 `presentation/assembly/tests/FeelProductionWeaponChainTests.cs` 经真实装配单独验证。
- 换装场景：Unity 适配器没有图标加载路径（界面图标 id 不经 `UnityResourceLoader` 加载），引擎宿主按资源引用约定自己找文件并用引擎解码核对尺寸（判断记录 41）；静态对账（`item_facts` 两侧对账）是导入校验的事。
- 换装场景：武器 → `auto_attack_timeline_ref` 的映射由脚本 meta 的内存覆盖注入（占位 `feel.weapon` 行没有该字段，见判断记录 18）。
- 空间语义（判断记录 35）：施法射程检查仍是平面距离（技能管线判定，竖直方向不参与）；击飞中的目标不做空中控制、不被地形阻挡竖直运动（地面恒为 0，没有斜坡与台阶）；没有空中攻击/空中受击的专属反应与动画；击飞期间再次被击飞按"重新抛起（起点为当前高度）"处理；命中高度窗口用命中判定那一刻施法者的脚下高度，锚点恒在施法者脚下（没有向上/向下偏置）；横版深度锁只作用在移动输入，不限制被击退的方向；实验室里只有玩家会起跳，靶子只会被击飞，没有靶子主动跳跃的脚本事件；`volume` 格子的 `camera_relative` 控制空间与第三人称镜头不在无头宿主上证明（宿主不读呈现字段）。
