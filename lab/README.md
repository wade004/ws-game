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
dotnet run --project toolchain/feellab -- export-test --script <脚本文件> [--fixtures lab/fixtures] [--cell <格子>]
dotnet run --project toolchain/feellab -- list                        # 六个格子在无头宿主上的可运行状态
dotnet run --project toolchain/feellab -- invariants [--script <id>] # 跨格子不变量（ADR-0122 决定 4，见判断记录 25），不比较基线、不改文件
```

公共参数：`--framework-root`（默认 `data/_framework`）、`--data-root`（可重复，默认 `data/_lab`）；数据根可指向任何游戏自己的数据集（实验室场景的数据由 `_lab` 提供，游戏数据根可叠加或替换）。

退出码：`0` 全部通过；`1` 基线有差异（逐条可读 diff 打印在标准输出）；`2` 参数错误；`3` 缺基线；`4` 有格子不可运行（预留空间模型或缺能力，不静默跳过）；`5` 数据或脚本内容错误。同时出现多种情况时优先级为差异 > 缺基线 > 不可运行。

`suite` 末尾除中文汇总行外另输出一行纯 ASCII 的 `RESULT total=<n> pass=<n> diff=<n> missing=<n> not_runnable=<n>`，供门禁解析（门禁按系统代码页解码标准输出，中文行会失配）。

门禁：`feel_lab_suite` 步骤（`toolchain/_gate_line_heavy.ps1`）跑 `suite` 要求退出码 0；`validate_lab_data` 步骤（`check.ps1`）要求 `data/_lab` 校验 0 error 0 warning，`validate_lab_action_data` 步骤同样要求 `data/_lab_action`（叠加框架根、`data/_lab`、`data/_feel`）0 error 0 warning。跨格子不变量随 `Tests.Lab`（`CrossCellInvariantTests.AllInvariants_HoldForEveryScript`）进门禁；`invariants` 命令末行另输出纯 ASCII 的 `RESULT invariants total=<n> pass=<n> fail=<n>`，有不一致时退出码 1。

### 基线更新流程（有意提交，不允许静默通过）

1. 改了会改变行为的东西（移动、结算、数据）后，`suite` 会红并打印差异。
2. 确认差异是预期引入的，运行 `suite --update-baseline`，把脚本/基线改动与行为改动放同一提交，提交信息注明"更新手感基线及原因"。
3. 差异出乎意料时先定位，不要用重烘焙掩盖。
4. 标准脚本由 `export-test` 写入（脚本 + 基线）；脚本文件的 `scriptVersion` 变了，旧基线会被比较器拒绝（避免新脚本对着旧基线比较）。

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

十个脚本（`lab/fixtures/scripts/`），每个脚本在六个格子上各一条基线，共 60 条：`move_tap`、`move_small_axis`、`reverse_180`、`diagonal`、`wall`、`pillar_loop`、`attack_while_moving`、`attack_then_stop`、`group_hit`，以及换装场景脚本 `equip_cycle`（tickRate 60，格式版本 2，见判断记录 15～21）。另有 30/60/120 帧率上限测试（`meta.frameRateCap` 三档，逻辑组必须逐字节一致）。

**手感场景脚本**（`meta.feel = true`，格式版本 3，tickRate 60，S7b 新增十九个、S12 新增一个、M2-C 新增一个，各六格共 126 条基线，合计 186 条；判断记录 22～33）：`feel_melee`、`feel_lunge`（近战与目标辅助/扑击位移）、`feel_dash`（冲刺）、`feel_projectile`（投射物：release 标记与只有 hit 标记两种形态）、`feel_projectile_miss`（S12：投射物没命中任何单位，被竞技场的直墙挡住，挥空提示出现在弹被挡住的那一刻）、`feel_combo3`（三连击）、`feel_dodge_cancel`（闪避取消）、`feel_buffer_lead`（输入缓冲提前量边界）、`feel_charge`（蓄力）、`feel_elite_armor`（精英霸体窗口内受击）、`feel_elite_flinch`（霸体窗口后按精英反应上限封顶）、`feel_interrupt`（普通靶被打断）、`feel_kill`（击杀放大顿帧）、`feel_group_hit`（同 tick 多目标顿帧取最大值并受上限约束）、`feel_patrol`（巡逻靶）、`feel_breakable`（可破坏障碍，动态阻挡）、`feel_motion_accel_decel`、`feel_motion_turn`、`feel_motion_wall`、`feel_motion_wall_slant`（起步刹停、转向、撞墙、斜撞墙贴墙滑行）、`feel_unit_block`（M2-C：单位间体积阻挡，走进木桩被挡在体积边界、闪避同样被挡；判断记录 34）。期望值全部在 `lab/tests/FeelSceneTests.cs` 里由预设字段 × 毫秒换算 × 受击裁决规则推出（读数辅助见 `FeelRules.cs`），用例里没有裸数。

六个格子（`lab.scenario.*`）：`2d_targeted`、`2d_action`、`2_5d_targeted`、`2_5d_action`、`3d_targeted`、`3d_action`，空间模型均为平面世界。`space` 字段另外接受 `volume`（体积空间能力包，预留）与 `side_2d`（横版二维，预留）；这两个取值的格子在宿主上标"不可运行"并给原因，不静默跳过。

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
10. **可破坏障碍**：基础靶子集（`lab.dummy_set.lab_standard`，旧脚本用）里的 `breakable` 仍近似为无 AI 的敌对低血量单位（没有动态阻挡）。手感场景用自己的靶子集（`lab.dummy_set.lab_action`）里的 `breakable`：靶子 `kind = breakable`，出场时在地形阻挡之外追加其占位矩形（半边长 0.5），被打死后经 `INavigation2D.SetBlocking` 批量替换去掉，导航阻挡版本随之递增（`GetBlockingVersion`，手感设计 06 第 10 节勘误 4：动态阻挡用既有导航接口，不另造机制）；`feel_breakable` 的 `motion.blocking_updates` 记下变更 tick。
11. **确定性**：命中表随机项在实验室数据里关闭；靶子默认不带 AI（`ai: true` 才保留）；不死木桩用 `power_floors` 的最低保留线实现"吃伤害不死"。
12. **不进分发包**：`toolchain/feellab` 不提供 `lib/` 预编译回退，也不进 dist/UPM 发行包；实验室是框架仓库内的验收设施，游戏仓库跑自己的实验室时直接引用内核工程。
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

## 已知局限


- 标准脚本集：06 第 3.1 节的短按、小幅轴、反转、斜向、贴墙、绕柱、边走边打、打完即停、群体命中、换装循环（十个旧脚本）加 S7b 的十九个与 S12 的一个手感脚本；"被精英打断"拆成了霸体窗口内（`feel_elite_armor`）、窗口后按反应上限封顶（`feel_elite_flinch`）与普通靶被打断（`feel_interrupt`）三种，因为精英的 `reaction_cap = flinch` 本身不会被打断。
- 度量组：响应、移动、攻击、性能四个常驻组，换装与七个手感组为条件组（装备解析、手感场景）。
- 无引擎宿主、无面板、无覆盖存储与 A/B；`expectations`（导出为测试时由指纹生成的期望）未实现——当前"导出为测试"产出的是脚本加基线。
- 三种空间与呈现组合在无头宿主上只有标签与假适配器视图的差异，逻辑判定完全相同（这正是"平面格子间逻辑组指纹逐字节一致"不变量要证明的）；`camera_relative` 控制空间与体积空间格子标"不可运行"。
- 数据表不能表达的靶子特征：不死木桩的"可选韧性值"（韧性 `poise` 按受击裁决读取，木桩没有声明）；旧靶子集的可破坏障碍仍是近似（判断记录 10）。
- 表现时间线是反馈流水线出批给假 sink 的指令，不含动画切换、真实音频与镜头平滑（那是引擎适配器的职责）；是否需要引擎 PlayMode 见下一条。
- **无头宿主不能证明的东西**：真实渲染帧（动画事件对齐、镜头插值、粒子）、真实输入设备的轴/按键抖动与延迟、真实时钟下的帧耗时；这些仍需要引擎 PlayMode 的实验室宿主（06 第 4 节引擎宿主，本仓库未提供）。M1 竖切前核心逻辑侧的手感行为（输入缓冲、动作时间线、顿帧、受击反应、运动、空间命中、反馈指令）已被指纹钉住，引擎 PlayMode 只需要验证"适配器把逻辑指令画对"。
- 实验室冲击档案与 sfx 行是内核内置的（判断记录 27），游戏仓库跑自己的实验室时用自己的档案。
- **观察 1（冲刺滑行，S12 已修）**：原先冲刺位移窗口结束后玩家仍按"从末速度线性制动"滑行一小段（`feel_dash` 终点 x 2.577，声明 2.0）。现在位移窗口结束速度清零，终点恰为声明距离；需要滑行的动作在档案里声明 `keep_momentum_on_motion_end`（见判断记录 33）。
- **观察 2（投射物挥空提示先于命中，S12 已修）**：原先 `feel_projectile` 发射 tick + 3 出挥空提示音，+5 才命中。现在挥空等投射物结局：命中则不挥空，没命中到被挡/到期那一刻才挥空（`feel_projectile_miss`）。**已知限制**：实验室里"到期"与"清场"两种结局原因没有脚本级覆盖（竞技场的直墙让未命中的弹都以"被挡住"收场），这两种原因由 `core/carriers/projectile/tests/ProjectileHitHookTests.cs` 与 `presentation/feedback_binder/tests/ImpactPipelineTests.cs` 单元级覆盖；多段技能同一动作实例里先后发射的弹按动作实例合并等待（不按段拆）。
- **观察 3（非攻击技能的挥空提示，S12 已修）**：原先 `feel_dash` 里无命中标记的闪避结束时也出挥空提示音。现在非攻击动作（`action.started.isAttack` 为假）不开挥空窗口。
- **观察 4（位移穿过靶子，M2-C 已提供可选开启的体积阻挡）**：`feel_lunge` 的扑击位移 `blocking: stop` 只对地形阻挡生效，玩家会穿过木桩（靶子不是阻挡），终点超过木桩位置，该脚本与既有预设不变。需要的预设声明 `unit_body_radius` 即可开启单位间体积阻挡（脚本 `feel_unit_block`，判断记录 34）。
- 换装场景：`GameplayAssembly` 不回填 `SkillOptions.ActionStepSeconds`，所以换装脚本必须 tickRate 60（装置显式抛错，见判断记录 20）。
- 换装场景：实验室换装装置自带一套手感装配、换装链与姿势选择器，不经生产装配的手感装配（生产装配开启手感时由 `PresentationAssembly` 装出 `PoseSelector`/`EquipmentPoseBridge`，载体层手感装配装出换装链，见 `presentation/assembly/README.md`、`core/carriers/assembly/README.md`）；保持自带是为了既有换装基线逐字不变。生产链路由 `presentation/assembly/tests/FeelProductionWeaponChainTests.cs` 经真实装配单独验证。
- 换装场景：图标尺寸与逐层剪辑的帧数无法在无头宿主上检查（那是导入校验静态报告的职责，两侧用 `item_facts` 对账）。
- 换装场景：武器 → `auto_attack_timeline_ref` 的映射由脚本 meta 的内存覆盖注入（占位 `feel.weapon` 行没有该字段，见判断记录 18）。
