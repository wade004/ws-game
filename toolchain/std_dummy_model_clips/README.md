# 假人姿势集生成器（model 型 / 骨骼剪辑，`gen_std_dummy_model_clips.py`）

生成框架级**假人姿势集**的 model 型版本：标准人形骨骼上的占位模型资源 `model.std_dummy_biped`、逐键骨骼剪辑，
和 `display.anim_set.std_dummy_biped_model` 数据行（与序列帧版 `display.anim_set.std_dummy_biped` 并列，同一个数据文件里的另一行）。
依据 [手感设计/04](../../architecture/手感设计/04_姿势与动画契约.md) 第 2～6.1、8 节与第 10 节勘误、
[ADR-0119](../../architecture/adr/0119-姿势维度模型与标准姿势库.md)、[ADR-0017](../../architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md)。
**不使用任何 AI 生成、不下载任何资产**：骨骼剪辑由姿势函数程序化产出。

## 与序列帧版的关系（单一来源）

键清单、时长、三相毫秒数、帧数规则、命名事件、每步位移、姿势函数全部是序列帧版 `toolchain/std_dummy_poses`
的同一份，本包**只引用不复制**（`config.py` 开头 `import std_dummy_poses.config`）。本包只声明 model 型独有的东西：
骨骼层级与比例、方块可视件、关节角限、引擎资产落点。`rig.py` 把序列帧版的关节角姿势换算成骨骼局部四元数
（旋转矩阵约定与序列帧版逐式相同，所以同一个剪辑同一个时刻两种外形动作逐帧同源）。

## 用法

```text
python toolchain/gen_std_dummy_model_clips.py                      # 生成规格 + 数据行，随后自检
python toolchain/gen_std_dummy_model_clips.py --check              # 只读自检（含引擎资产核对）
python toolchain/gen_std_dummy_model_clips.py --check --no-unity   # 只验规格与数据行
```

引擎侧资产由编辑器生成脚本 `adapters/unity/Assets/Editor/GenerateStdDummyModelAssets.cs` 按规格生成
（批处理：`Unity.exe -batchmode -nographics -quit -projectPath adapters\unity -executeMethod Adapter.Unity.EditorTools.GenerateStdDummyModelAssets.GenerateAndExit`，
或菜单 `GameFoundation/Generate Std Dummy Model Assets`；运行前先 `build.ps1 -SkipTests` 同步核心 DLL，同
`GeneratePlaceholderModelAssets` 的前置条件）。`.meta` 由 Unity 生成，与资产一起提交。

## 产物

| 文件 | 说明 |
|---|---|
| `assets/_placeholder/std_dummy_model_clips.json` | 规格（唯一机器可读来源）：骨架、方块可视件、逐剪辑关键帧（旋转四元数、髋位置）、数据行事件、烘进剪辑的事件 |
| `data/_framework/display/display.anim_set.json` 里 `display.anim_set.std_dummy_biped_model` 一行 | 128 个键 → `anim.std_dummy_<键点号换下划线>`，事件与序列帧版同源（别名键如 `attack`、各族战斗冲刺复用对应剪辑），逐键 `blend_ms` 与行级 `blends`（每对键）；另有 `_medium`（空 `extends` 行 = 主集）、`_light`、`_heavy` 三行体量组（`extends` 主集，轻/重覆盖全部键，不重复声明 `blend_ms`/`blends`） |
| `adapters/unity/Assets/Resources/GameFoundation/models/std_dummy_biped.{prefab,controller}` | 模型资源与动画控制器（每份剪辑一个状态，状态名 = 剪辑名，预置完成事件中继） |
| `adapters/unity/Assets/Resources/GameFoundation/anim_clips/std_dummy_*.anim` | 351 份剪辑资产（主集 117 份 + 轻/重体量组各 117 份，别名键不单独出） |

## 骨骼与约定

17 根骨骼：`hips`（根，髋位置曲线）→ `spine`（躯干，绕骨盆中心旋转）→ `head`、`upper_arm_{r,l}` → `forearm` → `hand` → `socket.{main,off}_hand`；
`hips` → `thigh` → `shin` → `foot`。Y 向上、Z 为人物前方、X 为主手侧；休息姿势（所有旋转为单位）= 双臂双腿自然下垂；
全部旋转曲线是相对父骨骼的局部四元数；脚掌骨骼反向旋转使脚底始终与髋系平行；主手挂点的 `-Y` 轴是刀刃方向；
头块体名 `slot.head`（槽位换网格的约定名）。预制体根在脚底原点，髋位置曲线 = 着地求解后的骨盆位置。
比例取自序列帧版骨架（身高 = 2 世界单位）。

## 关键帧与事件

关键帧取在序列帧版帧边界上（相内帧均分，相边界与三相毫秒数精确对齐），含终点：关键帧数 = 帧数 + 1；循环剪辑终点 = 起点。
引擎逐段线性插值。命名事件（04 第 5 节）逐键写进数据行，同时烘进剪辑资产（`functionName=OnAnimEvent`，字符串参数=事件名）；
烘进的事件里每个 `hit` 或 `release` 旁保证有一条同刻 `hit_frame`（数据行事件已带则不重复；角色外壳识别的命中帧事件名，见 04 第 10 节勘误）。

## 自检（`--check`；也是生成后的自动步骤，被 `toolchain/tests/test_std_dummy_model_clips.py` 覆盖，登记在门禁 `std_dummy_model_clips` 步骤）

- 规格文件与"当前代码重新生成的结果"逐字节对账（防手改、防改了配置没重新生成）；
- 04 第 3 节必备/推荐键（独立判据，不从配置推导）、键语法、键集合 = 序列帧版键集合；
- 事件、三相、时长、帧数、循环标志与序列帧版逐键一致；攻击类 `active_start/active_end/hit`、走跑 `footstep`、闪避无敌窗口、1h/2h 三相 = 05 第 9 节起点；
- 帧数规则、关键帧数 = 帧数 + 1、关键帧时刻与序列帧版帧划分一致、每个相边界有关键帧；
- 骨骼层级与路径；每条轨迹路径必须在骨架里且恰好是声明的旋转骨骼 + 髋位置；
- 四元数单位长、前后半球连续；每根骨骼每个关键帧的局部旋转角不超过 `BONE_ROT_LIMITS_DEG`；源关节角在 `SOURCE_ANGLE_LIMITS` 内；
- 循环首尾连续；走/跑接触姿势两脚间距 = 每步位移（5%）；
- 正向运动学对账：用写出的四元数把骨骼摆回去，脚、手、肩、头位置与序列帧版同一姿势的关节位置一致（1e-4 身高）、脚掌保持与髋系平行、髋高度等于着地求解；
- 数据行与规格一致（键集合、`resource_ref`、事件）；
- 引擎资产（解析 Unity YAML 文本，不启动引擎）：预制体层级路径 = 规格骨骼 + 可视件路径、控制器状态集合 = 剪辑集合、每份 `.anim` 的时长/循环标志/曲线路径/内嵌事件与规格一致、控制器与剪辑 `.meta` guid 引用一致。

## 判断记录

1. **新预制体 `std_dummy_biped`，不改 `placeholder_biped`**：占位胶囊体没有骨骼层级，既有占位用例绑定着它的结构；骨骼剪辑需要真实骨骼路径才能被 Animator 绑定。两者并列、互不影响。
2. **数据行 id 叫 `std_dummy_biped_model`**：沿用 `std_` 前缀（ADR-0119，自动套用姿势清单必备键检查）与序列帧版 `std_dummy_biped` 并列；资源引用类别用 `anim.`（ADR-0038：model 型走 `anim.<名字>`），名字取序列帧版同一个 stem。
3. **两版同源靠"引用不复制"**：键清单、事件、帧数规则都从 `std_dummy_poses` 取，自检里再逐键对账序列帧版规格；任何一边改动另一边的自检立刻红。
4. **规格文件作唯一来源，引擎资产由编辑器脚本按规格生成**：不手写 YAML 资产（Unity 的 .anim/.controller/.prefab 格式细节多、版本敏感）；`--check` 解析 YAML 文本核对资产与规格一致，不需要启动引擎，所以能进秒级门禁步骤。
5. **关键帧在序列帧版帧边界、线性插值**：与序列帧版逐帧一一对应；不另做曲线拟合。代价是关键帧数比手 K 动画密（20 帧/秒），换来可逐帧对账与确定性。
6. **铰链关节（肘、膝）取样时夹在 [0,150] 度**：序列帧版受击/倒地姿势的肘角会到负值（二维剪影里看不出），骨骼上就是手臂反折；只在骨骼版夹紧，不改序列帧版姿势函数（它已发布，改动会让已入库序列帧产物整体变化）。
7. **命中事件别名 `hit_frame`**：数据行事件只写 04 第 5 节的 `hit`（与序列帧版同源），角色外壳（ADR-0017）识别的是 `hit_frame`；在烘进剪辑的事件里同刻多放一条别名，使命中帧同步在 model 型单位上真实落在判定相中点。M4-W5 起 `release`（施法 `cast` 的释放点、弓的放箭点）同样别名成 `hit_frame`（配置 `ENGINE_EVENT_ALIASES = {hit, release}`，同刻去重：一个剪辑最多一条 `hit_frame`），见判断记录 19。
8. **体量三档数据声明、覆盖全部键，步幅轴参数化**：步幅就是运行期播放速率（02 第 7 节），不出独立资产；体量只改站姿与受击/腾空幅度，不改时长、帧数、事件、每步位移。体量表只有一份（序列帧版 `std_dummy_poses.config.MASS_TIERS`，`mass_profiles()` 取出），中档是主集（`_medium` 空 `extends` 行），轻/重两个数据行 `display.anim_set.std_dummy_biped_model_light`／`_heavy` 用 `extends` 继承主集并覆盖全部 128 个键（M4-D 取代此前"只覆盖 8 个站姿键"的做法）；体量偏移在序列帧版姿势函数出口叠加（`apply_mass`），本包不再自带体量表。独立资产全套使资产体积乘 3，换来每个键在每档都有自己的剪辑，是 M4-D 的显式取舍。
9. **四元数保 6 位小数、位置保 5 位**：规格体积（约 300 KB）与跨平台浮点末位差异的折中；自检用的容差（1e-4 身高）远大于舍入误差。
10. **控制器由生成脚本手写 YAML、`.meta` guid 稳定**：控制器的状态与中继行为的 fileID 取状态名的 FNV-1a 64 位散列（有符号），资产里的剪辑引用 guid 取自 `AssetPathToGUID`，控制器的 `.meta` 只写一次、之后原样保留；因此重复生成两次逐字节一致。自检校验每个状态的 fileID 等于公式值（回归：之前由引擎自己分配随机 fileID，每次生成都抖）。
11. **既有剪辑只追加不改**：既有全部 `.anim`（M4-D 前的 108 份）与预制体字节不变；预制体 fileID 是引擎随机分配的，所以只在结构（层级、组件、控制器引用）真的变化时才重写（`Equivalent` 判等），否则跳过。
12. **`genericBindings` 靠临时预制体烘进剪辑**：引擎只在"带 Animator 与控制器的预制体存盘"时把 `genericBindings` 写进剪辑，单独生成剪辑得到的是缺绑定的剪辑；生成脚本因此先把全部剪辑挂到一个临时预制体上存盘，再删除临时预制体（`_bake_scratch*` 不留）。
13. **冲刺键别名**：战斗站姿的冲刺键 `move.sprint.combat*` 别名到无站姿的冲刺剪辑（不另出资源），因为键回落链在冲刺键耗尽前不退回奔跑，每个族的冲刺键都得登记。
14. **弓的 `release`**：弓攻击在 `hit` 同刻另放 `release`（放箭点）；数据行事件里已有 `hit_frame`（序列帧版同样写），骨骼版烘 `hit_frame` 别名时按"同名同时刻已存在则不重复"去重。

15. **不做根运动、不做布娃娃**（M4-D，04 第 10 节第 10 条）：位移由逻辑层给出（ADR-0116），剪辑不带根骨位移曲线；击飞翻滚 `hit.launch.tumble` 与落地缓冲 `hit.launch.land` 是关键帧动画，
    只对这两个键放开髋的旋转上限（180 度；躺姿四键 100 度，见判断记录 18；其余键仍是 5 度），翻滚时整个身体绕骨盆翻转、不穿地。循环键首尾姿势的四元数允许整体差一个符号（同一旋转），自检按符号无关比较。
16. **过渡混合 `blend_ms`（加法字段）**：数据行每个键带 `blend_ms`（切入本剪辑的交叉淡入毫秒，按状态定一个值，如待机 120、移动 100、受击 30、格挡 80），行级 `blends`
    声明 22 对每对键值（如 `jump.land` 到 `idle` 160）；优先级每对键 > 逐键 > 默认 0.15 秒，显式 0 = 硬切。体量组行不重复声明，沿 `extends` 取主集的值。
    控制器无过渡连线、不含任何时长，是判定而非缺口，见判断记录 20。
17. **本包没有武器几何**：武器是挂在挂点 `socket.main_hand`、`socket.off_hand` 上的装备，占位模型只有身体方块；M4-D 的"五个新武器族低多边形形体"只作用在序列帧版的武器层（见序列帧版判断记录 10）。
18. **躺姿用骨盆整身俯仰 `bp`、肩/髋/躯干角限收紧到人体范围**（M4-W5，04 第 10 节第 3 条）：`hit.launch`、`hit.knockdown`、`hit.getup`、`death` 四个键（加上与 `hit.getup` 同一躺姿的 `hit.launch.land`，共五个键，轻/重体量组同步）
    此前靠"腿绕髋整体后摆"表达躺平，肩/髋被迫超出人体范围；现在用 M4-D 已有的 `bp`（整身绕骨盆俯仰，仰卧 -88、俯卧 88）放平，肩/髋/躯干回到人体范围：
    `SOURCE_ANGLE_LIMITS` 肩前屈 `[-65, 185]`（原 `[-130, 185]`）、髋前屈 `[-46, 100]`（原 `[-95, 100]`）、躯干俯仰 `[-50, 60]`（原 `[-95, 95]`），骨骼旋转角上限上臂 195 → 175、大腿 105 → 90、躯干 100 → 55、头 60 → 45；髋整体俯仰只对 `BP_KEYS`
    （四个躺姿键 + 翻滚两键）放开（躺姿 100 度、翻滚 180 度），其余键 `bp` 恒为 0 且自检会报错。键名、时长、事件（名与时刻）、帧数一字不动，其余键（除 M4-W6 重绘的 `hit.heavy`、`hit.knockback`）的规格条目与剪辑字节不变。
    此前 `hit.heavy`、`hit.knockback` 因验收锁定"字节不变"保留旧肩/髋角限（每键豁免表），M4-W6 已放开这两个键的字节锁并删除豁免表，见判断记录 21；现在任何键的肩/髋/躯干角限都是同一张全局表。
19. **cast 的 release 提前到 150 ms，`hit_frame` 别名扩到 `release`**（M4-W5，实验室命中对齐的数据根因）：M4-H 实测引擎命中点比逻辑命中标记晚 150 ms，根因是 `cast` 剪辑的 `release` 在 250 ms，而 `data/_lab_action` 技能的命中标记在 150 ms
    （slash、combo3、slam、charge、bolt、projectile）或 100 ms（combo1/2、poise_chip、lunge）。改 `cast` 剪辑的相位分配（windup 150 / release 100 / recovery 350，总时长 600 与帧数 12 不变，数据行 `time_pct` 0.4167 → 0.25），
    而不是改十多个技能的时间线，因为技能时间线是 feellab 基线的输入、改动会连带重写基线，而 `cast` 剪辑只被表现层读。模型 `cast` 此前没有任何 `hit_frame`（`release` 不在别名表里），宿主的命中对齐在 model 位面永远数不到；
    现在 `release` 别名成 `hit_frame`，与 sprite 位面（识别 `release` 或 `hit_frame`）等价。起手不是 150 ms 的技能（100 ms 档与 300 ms 档）由判断记录 22 的施放点变体对齐，不留残余。
20. **动画控制器无过渡连线（判定）**：控制器每个剪辑一个状态，状态之间不连线、不含任何时长。过渡由 `ModelCharacterRig.PlayClip` 在运行期按数据行 `blend_ms`／`blends`（每对键 > 逐键 > 默认 0.15 秒，04 第 10 节）交叉淡入。
    理由：（1）过渡时长是可数据声明、可按每对键覆写、可沿 `extends` 继承的运行期参数，写进控制器就成了第二份会漂移的真源；（2）控制器连线的数量与状态数平方相关（130 键 × 体量组），确定性生成与字节稳定的成本远高于收益；
    （3）完成事件靠每个状态预置的中继行为，不依赖过渡；（4）控制器与 guid 因此不随过渡数据变化，重复生成逐字节一致。
21. **重受击、击退的极值帧重绘到人体范围，后仰用整身俯仰 `bp` 表达**（M4-W6，04 第 10 节第 3 条）：此前 `hit.heavy`、`hit.knockback` 用 2.2 倍外推表达强后仰，肩后伸到 -125 度、髋后伸 -51 度，靠每键豁免表 `SOURCE_ANGLE_EXEMPT` 保留旧限，
    理由是这两个键被"字节不变"锁定。M4-W6 放开这两个键的字节锁：肩/髋/躯干每一帧回到全局角限内（外推倍率降为 `hit.heavy` 1.3、`hit.knockback` 1.4，并受 1.5 的上限约束），后仰由骨盆整身俯仰 `bp` 补足
    （`hit.heavy` 极值 -12 度、`hit.knockback` 极值 -24 度，乘体量反应倍率，所以轻体量后仰更大、重体量更小；只后仰，`bp` 在 `[-35, 0]`，髋旋转上限 `BP_RECOIL_LIMIT_DEG` = 35 度）。
    豁免表 `SOURCE_ANGLE_EXEMPT`、取限函数 `source_angle_limits` 与它的判定记录一并删除（豁免表空了就不留机制）。键名、时长、事件、帧数不变，由结构摘要逐字节锁定
    （`test_std_dummy_model_clips.py` 的 `LEGACY_POSTURE_STRUCT_SHA256` 取自放开字节锁之前的入库版本，重绘后逐位相同）；轻/重体量组同步重绘，精灵版与模型版同源同时改，装备层与方向变体随生成器重出。
    `BP_KEYS` 因此扩为躺姿 + 翻滚 + 重受击/击退三组，其余键 `bp` 仍恒为 0。
22. **施放点变体 `cast.quick`、`cast.heavy`**（M4-W6，与序列帧版判断记录 16 同一条规则）：骨骼版同名同时长同帧数同事件规则：总时长 600 ms、12 帧，`release` 在 100 ms 与 300 ms，烘进 `.anim` 的事件 `release` 与别名 `hit_frame` 同刻；轻/重体量组各带同名键。
    引擎侧 `.anim`、控制器状态由 `GenerateStdDummyModelAssets` 按规格生成，`.meta` 由 Unity 生成、随资产入库。模型位面的 Animator 事件在下一次动画求值时派发，命中时刻施放者的顿帧已把 rig 冻结，
    所以模型位面的事件滞后 = 一帧量化 + 施放者这次命中的顿帧时长；这是引擎侧的冻结语义，不是数据偏差，宿主命中对齐测试按这个口径断言。

## 已知限制

- 步幅轴靠运行期播放速率，不另出资产。
- 占位模型只有方块可视件，没有蒙皮、没有材质区分（左右、主副手同色）；块体厚度与序列帧版零件的厚度不完全相同，着地高度取自序列帧版零件的包络，不做像素级贴地验证。
- 格挡、眩晕、击飞翻滚是关键姿势插值的占位动作，不含物理（位移由逻辑驱动）。
