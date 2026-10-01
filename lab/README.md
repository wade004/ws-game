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
```

公共参数：`--framework-root`（默认 `data/_framework`）、`--data-root`（可重复，默认 `data/_lab`）；数据根可指向任何游戏自己的数据集（实验室场景的数据由 `_lab` 提供，游戏数据根可叠加或替换）。

退出码：`0` 全部通过；`1` 基线有差异（逐条可读 diff 打印在标准输出）；`2` 参数错误；`3` 缺基线；`4` 有格子不可运行（预留空间模型或缺能力，不静默跳过）；`5` 数据或脚本内容错误。同时出现多种情况时优先级为差异 > 缺基线 > 不可运行。

`suite` 末尾除中文汇总行外另输出一行纯 ASCII 的 `RESULT total=<n> pass=<n> diff=<n> missing=<n> not_runnable=<n>`，供门禁解析（门禁按系统代码页解码标准输出，中文行会失配）。

门禁：`feel_lab_suite` 步骤（`toolchain/_gate_line_heavy.ps1`）跑 `suite` 要求退出码 0；`validate_lab_data` 步骤（`check.ps1`）要求 `data/_lab` 校验 0 error 0 warning。

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
- **指纹**：`(脚本 id 与版本, 数据集哈希, 格子)` 为键，按组存放度量；规范 JSON（键序固定，数值规整到 9 位小数）。数据集哈希变化只给警告（改数据必然改哈希，以度量差异为准）。

## 标准脚本与基线

十个脚本（`lab/fixtures/scripts/`），每个脚本在六个格子上各一条基线，共 60 条：`move_tap`、`move_small_axis`、`reverse_180`、`diagonal`、`wall`、`pillar_loop`、`attack_while_moving`、`attack_then_stop`、`group_hit`，以及换装场景脚本 `equip_cycle`（tickRate 60，格式版本 2，见判断记录 15～21）。另有 30/60/120 帧率上限测试（`meta.frameRateCap` 三档，逻辑组必须逐字节一致）。

六个格子（`lab.scenario.*`）：`2d_targeted`、`2d_action`、`2_5d_targeted`、`2_5d_action`、`3d_targeted`、`3d_action`，空间模型均为平面世界。`space` 字段另外接受 `volume`（体积空间能力包，预留）与 `side_2d`（横版二维，预留）；这两个取值的格子在宿主上标"不可运行"并给原因，不静默跳过。

## 判断记录

1. **内核位置与分层**：独立顶层目录 `lab/`，登记为 `module_map.json` 的 `lab` 层（单模块，同 `sim` 层），位于 `sim` 与 `presentation` 之下游。依赖方向：`Lab.Kernel` → `Core.Sim`（装配根与桩适配层）、`Adapters.Stub`、`Presentation.Common`；任何运行时核心程序集（`Core.*`、`Presentation.*`）都不引用它——内核引擎无关、不被运行时核心程序集反向引用。不放进 `core/sim`：`sim` 是可被装配与分发的仿真骨架，实验室是验收设施，且需要表现层公共部分，放进去会让 `core/sim` 新增对表现层的依赖（两者现在是 `gameplay` 之下的并列下游）。不放进 `presentation/`：它依赖装配根与桩适配层，反过来会让表现层依赖 `sim`。因此 `sim` 与 `presentation` 的 `downstream` 都登记了 `lab`，上游公开面变化会选中 `Tests.Lab`。
2. **schema 声明放在 `core/sim/schema/LabSchemas.cs`**：与 `sim.*` 同属"仅无头宿主与内容工具读取"的工具表，沿用 `SimSchemaCatalog.RegisterAll` 这一个入口，校验器与独立发行包不用新增对内核的引用；`core/sim` 只知道表形状，不引用内核。
3. **相对 06 第 1.1 节结构的扩展字段**：`lab.scenario` 增加 `arena`（地形引用）、`settlement`（`targeted|action` 显式取值）、`skill_bindings`（输入动作到技能），无头宿主跑起来必需，不改既有字段语义。`default_preset`/`default_weapons` 指向的 `feel.*` 域尚未落地，登记为自由 id（只作标签，不校验存在性）。
4. **`HeadlessWorldOptions.Navigation`**：`HeadlessWorldBuilder` 新增导航接口透传选项（缺省行为不变），让宿主能把竞技场阻挡登记进桩导航；只增不改。
5. **动作式格子在时间线机制落地前与目标选择式同样运行**：数据表没有 `timeline` 块，三个动作式格子目前行为与对应的目标选择式格子一致（测试 `ActionCells_BehaveLikeTargetedCells_UntilTimelineMechanismLands` 锁住这一点）。时间线机制落地后，动作式格子的基线是第一批预期变化。`default_preset` 现在只作标签。
6. **移动绑定到摇杆**：宿主把 `input.action.move` 重绑到手柄左摇杆轴。输入映射层对摇杆不做死区、不单位化；因此被测的是宿主循环阈值（模长平方大于 0.0001 才提交移动请求）与移动层对输入的单位化。移动请求每 tick 重提交。
7. **空间查询位置同步**：桩空间查询的位置不随世界自动更新，宿主在每次推进后手工同步一遍（靶子位置同样）。
8. **视图插值 alpha 恒为 0**：宿主按整步推进，视图绑定器的插值因子为 0，表现层位置比逻辑滞后一个 tick（60 帧上限下首次可见位移帧数为 2）。这是当前如实量到的行为，不是实验室引入的偏差。
9. **真实时间度量分桶**：毫秒按 10 倍量级、分配字节按 2 倍量级分桶后才入基线，比较只做上限检查，基线不随机器抖动。
10. **可破坏障碍近似**：数据表的靶子只能表达生物模板，没有"可破坏障碍"这种动态阻挡；`breakable` 近似为无 AI 的敌对低血量单位。真正的动态阻挡需要导航接口的运行期增删阻挡能力（上游缺口，见下）。
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
21. **首次普攻不走 `IActionSkillBinding`**：该接口目前只在"取消进入"路径被动作管线使用，空闲状态下的第一下普攻由宿主在换装场景里经 `WeaponActionBinding.TryResolveAttackSkill` 解出技能 id，再提交 `cast` 意图。

## 已知局限

- 只覆盖 06 第 3.1 节标准脚本集中十个（短按、小幅轴、反转、斜向、贴墙、绕柱、边走边打、打完即停、群体命中、换装循环）；三段连招、闪避取消、蓄力、被精英打断、击杀依赖尚未落地的机制，未做。
- 度量组只有响应、移动、攻击、性能四组；顿帧、受击、群体合并、装备解析等组随各自机制加入。
- 无引擎宿主、无面板、无覆盖存储与 A/B；`expectations`（导出为测试时由指纹生成的期望）未实现——当前"导出为测试"产出的是脚本加基线。
- 三种空间与呈现组合在无头宿主上只有标签与假适配器视图的差异，逻辑判定完全相同（这正是"平面格子间逻辑组指纹逐字节一致"不变量要证明的）；`camera_relative` 控制空间与体积空间格子标"不可运行"。
- 数据表不能表达的靶子特征：可破坏障碍（动态阻挡）、精英的 `reaction_cap`/霸体/抗硬直、不死木桩的"可选韧性值"（这些需要手感数据域与受击反应机制，见 06 第 2 节）；巡逻靶保留了 AI 但没有脚本覆盖它。
- 表现时间线只记录假适配器视图的创建与可见位移，不含动画切换、反馈动作、镜头、音效（对应机制未落地）。
- 换装场景：`GameplayAssembly` 不回填 `SkillOptions.ActionStepSeconds`，所以换装脚本必须 tickRate 60（装置显式抛错，见判断记录 20）。
- 换装场景：`PresentationAssembly` 没有装出 `PoseSelector`/`EquipmentPoseBridge`，姿势侧由实验室装置自己装（生产装配接线留给后续装配切片）。
- 换装场景：图标尺寸与逐层剪辑的帧数无法在无头宿主上检查（那是导入校验静态报告的职责，两侧用 `item_facts` 对账）。
- 换装场景：武器 → `auto_attack_timeline_ref` 的映射由脚本 meta 的内存覆盖注入（占位 `feel.weapon` 行没有该字段，见判断记录 18）。
