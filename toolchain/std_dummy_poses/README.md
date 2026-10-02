# 假人姿势集生成器（`gen_std_dummy_poses.py`）

生成框架级**假人姿势集**（sprite 型）：用程序绘制的几何人偶序列帧 + `display.anim_set.std_dummy_biped`
数据行（含 04 第 5 节命名事件）+ 规格文件。依据
[手感设计/04](../../architecture/手感设计/04_姿势与动画契约.md) 第 2～6.2、8 节、
[ADR-0119](../../architecture/adr/0119-姿势维度模型与标准姿势库.md)；资产契约见
[14 资产规格书模板](../../architecture/14_资产规格书模板.md) 第 1.2、2.3 节。**不使用任何 AI 生图**。

同一姿势集的 model 型（骨骼剪辑）版见 [`../std_dummy_model_clips/README.md`](../std_dummy_model_clips/README.md)，
它引用本包的键清单、事件与姿势函数，本包是两版的单一来源。

用途（04 第 6.2 节）：新游戏第一天用假人跑手感与测试；美术之后按规格逐键替换；手感实验室的指纹基线
采集在假人集上，与美术无关。

## 用法

```
python toolchain/gen_std_dummy_poses.py [--clean] [--sheet <缩略拼图.png>]        # 生成并自检
python toolchain/gen_std_dummy_poses.py --check [--sheet <缩略拼图.png>]          # 只读自检
```

| 参数 | 缺省 | 说明 |
|---|---|---|
| `--assets-out` | `assets/_placeholder` | 资产根；帧资源落 `sprite_anim/std_dummy_*`，规格落 `std_dummy_poses.json` |
| `--data-out` | `data/_framework` | 数据根；数据行落 `display/display.anim_set.json` |
| `--direction-count` | 8 | 方向档数，4/8/16（14 第 2.1 节；canonical 档数 3/5/9，左侧档位靠镜像，不落盘） |
| `--fps` | 20 | 标定帧率；帧数按下节规则随它变化 |
| `--no-composite-dirs` | 关 | 不生成"整身合成的方向变体"，只留默认朝向整身 + 逐层剪辑 |
| `--clean` | 关 | 生成前删除 `sprite_anim/std_dummy_*` 与规格文件（只限这批，不碰别的占位资产） |
| `--sheet` | 无 | 输出每键一格的缩略拼图（04 第 9 节第 6 条）；**只留本地，不入库**（建议写到 `bin/_check_artifacts/`，已被忽略） |

所有可调数与来源都集中在 [`config.py`](config.py)（身高像素、画布、帧率、参考时长、三相、位移、方向档、事件规则），
改完重新生成即可，没有第二处副本。

## 键清单（128 个键，117 份资源）

键名按 04 第 2.1 节语法 `<状态>[.<步态>][.<姿态>][.<武器族>][.<变体>]`。

| 类别 | 键 |
|---|---|
| 待机 | `idle`、`idle.combat`、`idle.1h`、`idle.combat.1h`、`idle.2h`、`idle.combat.2h` |
| 走/跑 | `move.walk`、`move.run`、`move.run.combat`，及 `.1h`/`.2h` 族各一套（`move.walk.1h`、`move.run.1h`、`move.run.combat.1h`、`move.walk.2h`、`move.run.2h`、`move.run.combat.2h`） |
| 攻击 | 徒手 `attack.unarmed`、`.02`、`.03`；单手 `attack.1h`、`.02`、`.03`；双手 `attack.2h`、`.02`；基础键 `attack`（别名，复用 `attack.unarmed` 的资源） |
| 受击 | `hit`、`hit.light`、`hit.heavy`、`hit.knockback`、`hit.knockdown`、`hit.getup` |
| 其它 | `death`、`jump`、`cast`、`dodge` |
| 可选键 | 冲刺 `move.sprint`（及 `.combat`、各族）、启停过渡 `move.start`／`move.stop`／`move.pivot`、击飞 `hit.launch`、眩晕 `stunned`（循环）、格挡 `block`（循环）、带伤变体 `idle.wounded`／`idle.combat.wounded`／`move.walk.wounded`／`move.run.wounded` |
| 新武器族 | `polearm`、`bow`、`staff`、`dual`、`shield` 各一套待机（`idle.<族>`、`idle.combat.<族>`）、走/跑/战斗走/战斗跑/冲刺（`move.*.<族>`）与攻击（`attack.<族>`；长柄、法杖、双持有 `.02`，双持另有 `.03`；弓只一段并带 `release` 事件；盾牌另有 `block.shield`）；`1h`/`2h` 族补 `move.walk.combat.<族>`、`move.sprint.<族>`、`move.sprint.combat.<族>` |
| 空中键（M4-D） | `jump.rise`、`jump.fall`、`jump.land`、`hit.air`、`attack.air.<族>`（八族，别名 `attack.air` 到 `attack.air.unarmed`）；空中攻击不带离地量、不带连招与闪避取消窗口 |
| 细节键（M4-D） | 格挡受击抖动 `hit.block`、`hit.block.shield`；眩晕摇晃 `stunned.sway`（循环）；击飞翻滚 `hit.launch.tumble`（整个身体绕骨盆翻转）与落地缓冲 `hit.launch.land`（末姿势 = `hit.getup` 起姿势） |
| 带伤全覆盖（M4-D） | `move.sprint.wounded`、`move.sprint.combat.wounded`（别名）、`move.walk.combat.wounded`、`move.run.combat.wounded`、`move.start.wounded`、`move.stop.wounded`、`move.pivot.wounded` |
| 战斗/冲刺别名 | `move.walk.combat`（别名到 `move.walk`）、各族战斗冲刺 `move.sprint.combat.*`（别名到对应族冲刺）：键回落链先去变体、族、站姿、步态，最后冲刺才退回奔跑，所以每个族的冲刺键必须登记 |

04 第 3 节必备键（`idle`、`move.walk`、`move.run`、`attack`、`hit`、`death`，及每族一段攻击）与推荐键（上表其余键）全覆盖；
可选键全部出齐（见上表）；`wounded` 变体覆盖全部移动与战斗移动键（M4-D）。每个攻击剪辑的事件表末尾追加一条与 `hit` 同时刻的 `hit_frame` 事件（既有事件不动），运行期按事件时间百分比换算出关键帧下标，序列帧播放器据此触发 `hit_frame`，与 model 型的命中帧事件同名同时刻。
徒手是基础键本身（`idle`/`move.*` 无武器族后缀即徒手），不另出 `idle.unarmed`。

方向档：每个键 × 全部 canonical 档（8 方向 = `front`、`front_side_r`、`side_r`、`back_side_r`、`back`），
左三档（`front_side_l`、`side_l`、`back_side_l`）由镜像规则回填（规格里的 `mirror_pairs`）。

## 资源布局

```
assets/_placeholder/sprite_anim/
  std_dummy_<键>/                      整身合成，默认朝向 front；resource_ref = sprite_anim.std_dummy_<键>（点号换下划线）
  std_dummy_<键>__<方向档>/            整身合成的方向变体（ADR-0093）
  std_dummy_<键>__<方向档>__body/      身体层逐层剪辑（ADR-0072 第一级：方向 + 层名）
  std_dummy_<键>__<方向档>__hand_main/ 武器层逐层剪辑（1h/2h 族剪辑，加无族的 hit.*/death/jump/cast/dodge；层名沿用占位英雄的主手层 hand_main）
  std_dummy_<体量>_<键>/ 及其 __<方向档>__body、__<方向档>__hand_main  体量组（`light`、`heavy`）的同名剪辑，只写正面整身与身体/武器层，不写整身方向变体（见判断记录 11）
assets/_placeholder/std_dummy_poses.json   规格：参数、每键相位/帧数/事件/位移、方向档
data/_framework/display/display.anim_set.json   行 display.anim_set.std_dummy_biped 及体量行 _medium（空 extends）、_light、_heavy（extends 主集、覆盖全部键）
```

每个目录是固定的 `atlas.png` + `frames.json`（ADR-0054 结构：`frame_w/frame_h/fps/frame_duration/loop/frames[]`）。
画布 144×144、身高 64 像素（`pixels_per_unit` 32 下身高 2 世界单位）、脚点 `(72,140)`；
中性灰块体，鼻块标示朝向，明暗随深度。

## 帧数、时长与事件规则

- **帧数**：每相 `max(1, floor(相毫秒 × fps / 1000 + 0.5))`（四舍五入，0.5 进位）；相内帧时长均分，使三相分界与毫秒数精确对齐
  （`frames.json` 逐帧 `duration`）。20 fps 下：单手剑 110/80/190 ms → 2/2/4 帧（共 8），巨剑 170/100/290 ms → 3/2/6 帧（共 11），
  走 1000 ms → 20 帧，跑 600 ms → 12 帧。
- **时间标记**（写进 `events` 的 `{name, time_pct}`，`time_pct` = 毫秒偏移 ÷ 剪辑总时长，保留 4 位）：
  攻击键 `active_start`/`active_end`/`hit`（判定相正中）/`trail_start`/`trail_end`、非末段 `combo_open`/`combo_close`、
  `cancel_open:dodge`（单手/徒手 0.65、巨剑 0.75，05 第 9 节）；走/跑 `footstep`×2（0 与 0.5）；`dodge` 的
  `motion_start/end` 与 `invuln_start/end`；`cast` 的 `release`；`hit.*`（除 `getup`）的 `impact`。
- **三相**：单手剑 110/80/190 ms、巨剑 170/100/290 ms 取自 [05 第 9 节](../../architecture/手感设计/05_手感档案与解析.md) 试调起点；
  其余（徒手 = 单手 ×0.85 取整到 5 ms；收尾段 ×1.2/1.25/1.3；受击/倒地/跳跃/施法/闪避的相）**自拟**，均为 experimental 起点，
  在 [`config.py`](config.py) 标注，改动只需改那里。
- **走/跑每循环位移**：每步位移 = 参考基础移速 × 速度比 × 剪辑时长 ÷ 2。速度比取自 02 第 2 节（走 0.45，跑 1.0）；
  参考基础移速 2.0 身高/秒是**自拟参照**（02 没给绝对值，它属于 `feel.calibration`）。得走每循环 0.9 身高（每步 0.45）、
  跑每循环 1.2 身高（每步 0.6）；姿势的腿摆幅由每步位移反推，接触姿势两脚间距与它一致（自检断言，容差 5%）。
  换游戏标定后，播放速率按 02 第 7 节公式自动匹配，不需要重做假人。
  对应 `display.map.stride_distance`（ADR-0078）取每步位移 × 该游戏的参考身高（世界单位）。

## 自检（`--check`；同时是生成后的自动步骤，也被 `toolchain/tests/test_std_dummy_poses.py` 覆盖）

必备键 × 全方向档 × 各层文件齐全；键符合 04 第 2.1 节语法；数据行与规格逐键一致；攻击键有 `active_start/active_end` 与落在判定相内的 `hit`，
判定相标记与三相毫秒数一致；走/跑有 `footstep` 且每步位移×步数=每循环位移；`dodge` 有无敌窗口且落在 motion 窗口内；
帧数 = 参数推算、`frames.json` 的 fps/loop/帧尺寸/时长合计与规格一致；循环剪辑首尾姿势连续；任何帧都不触碰画布边缘（未被裁切）；
方向档命名与 `asset_import/directions.py`、资源目录与 `asset_import/ref_conventions.sprite_anim_dir` 一致；清单外残留目录给警告。
推荐键缺项为警告，其余为错误；退出码 1 表示有错误。

## 判断记录

1. **产物入库，跟随既有占位资产惯例**：`assets/_placeholder` 下的占位资产（精灵/特效/音效/地图）都是脚本确定性生成后**入库**的
   （构建把该目录整棵拷进发布包，不在构建期重新生成），假人姿势集同样是随版本发布的框架资产，故生成产物入库（约 980 个文件、2.8 MB；
   同样的输入产出逐字节一致，有测试）。唯一不入库的是缩略拼图（审阅证据，属"生成物不进 git"）。规格文件 `std_dummy_poses.json`
   与 MANIFEST 分开：`gen_placeholder_assets.py --check` 只核对它自己的清单，不会误认这批文件为残留。
2. **体量三档按数据声明、覆盖全部键**（M4-D 取代此前"只出一行"的判定）：`config.MASS_TIERS` 给出轻、重两档的关节角偏移与受击反应、腾空两个幅度系数
   （受击反应系数上限 `MASS_REACT_CAP`），中档 `MASS_MAIN_TIER` 就是主集，对应空 `extends` 行 `_medium`；两版都读这一张表。体量行 `_light`／`_heavy`
   用 `extends` 继承主集并覆盖主集的**全部键**（含别名），每个键指向 `std_dummy_<体量>_<键>` 自己的剪辑。体量偏移在姿势函数的出口按体量叠加（`poses.apply_mass`），
   主集（不带体量）的姿势计算路径不变，所以既有键字节不变。
3. **程序化伪三维而不是逐方向手绘**：关节角姿势 → 三维块体 → 按偏航角正交投影，方向档数（4/8/16）与任意键的方向一致性自动得到，
   参数改动（帧率、时长、位移）重新生成即可；代价是造型僵硬，只作"可读"的占位，美术按 04 第 4 节逐键替换。
4. **画布 144×144**：2h 巨剑侧面前伸（肩到剑尖约 0.95 身高）与上举要不被裁切，自检断言任何帧不触碰边缘；更小的画布（128）实测裁切了巨剑攻击帧。
5. **取样规则**：循环剪辑在帧起点取样（第 0 帧即接触姿势，与 `footstep`@0 对齐）；非循环在帧中点取样。
6. **武器层给持械族剪辑，也给无族的状态剪辑，层名 `hand_main`**：逐层剪辑按 ADR-0072 两级探测的第一级命名（方向 + 层名）；第二级（仅层名）不生成，
   因为所有剪辑都带全方向。无武器族的 `hit.*`、`death`、`jump`、`cast`、`dodge` 也出武器层剪辑（手感落地 S8a），使持械角色播这些键时武器随身体帧走，
   不再是静态图：武器画占位单手剑、俯仰角固定为 `STATE_CLIP_WEAPON_PITCH`（90 度，剑身沿躯干朝向水平前指；默认 0 度剑尖竖直向下，落地/倒地时扎出画布底边），
   倒地姿势里低于地面的武器角点压到地面高度（`skeleton.render(clamp_weapon_to_ground=True)`）。整身合成与身体层仍是徒手姿势，规格里每键多一个
   `weapon_layer_family`（持械族取自身族，状态剪辑取 `1h`，徒手 idle/move/attack.unarmed 为 null）。自检断言：这些键有非空的武器层图集，
   且武器层随身体帧变化（身体帧不同而武器层逐帧完全相同视为"没有随身体帧走"，报错）。
   ADR-0100 的"装备层候选"命名（带装备资源集引用段）仍不在这里生成：那是装备美术自己的命名空间，由 `toolchain/std_equip_set` 按物品生成。
7. **整身方向变体也生成**（ADR-0093）：没有声明纸娃娃层的外形用整身剪辑时也能随朝向换图；代价是多约 1/3 文件，可用 `--no-composite-dirs` 关闭。
   体量组同样生成（M4-W5，见判断记录 11）。
8. **别名键 `attack`**：04 第 3 节必备键 `attack` 复用 `attack.unarmed` 第一段的资源与事件（同一个 `resource_ref`），不重复出图。
9. **`hit` 的 `segment` 不写**：现有 `events` 条目只有 `name/time_pct`，没有 `segment`，每个剪辑只放一个 `hit`，多段攻击用多个键表达（见 14 第 2.3 节）。

10. **新武器族的武器层改画低多边形形体**（M4-D，`weapons.py`）：长柄（杆加枪头）、弓（弓身、弓弦）、法杖（杆加杖头宝珠）、双持（两把短刃）、盾（多边形盾面加盾边）都由凸块拼成，
    与骨骼版无关（骨骼版没有武器几何，武器是挂在挂点上的装备）。长柄与法杖尖端不超出旧方块占位的触及范围，否则 144 像素画布会裁切攻击帧。它们仍是占位，装备美术按 ADR-0100 覆盖。
    五个族既有键的武器层与整身合成图字节因此改变，身体层与元数据不变。
11. **体量组也写整身方向变体**（M4-W5，取代此前"体量组不写"的体积取舍）：体量组每个键同样按每个方向槽位写整身合成图（`composite_dirs`，规格 `mass_tiers.composite_direction_variants` 与 `params.composite_direction_variants` 同值，自检核对），
    用整身剪辑（没有纸娃娃层）的外形在轻/重体量下也随朝向换图。**体积判定**：先估算全部键的增量（轻、重各 117 份剪辑 × 每份 3 个方向槽位的整身目录），实测新增 2340 个文件、约 8.2 MB（轻约 4.0 MB、重约 4.2 MB），
    低于 20 MB 的上限，所以全部键都出，不退化为"只出移动/待机键"。装备集的占位剪辑仍只跟主集走。
12. **空中键与细节键用新键、不改既有键**：`hit.block`、`stunned.sway`、`hit.launch.tumble`、`hit.launch.land` 与空中键全是追加的新键，`block`、`stunned`、`hit.launch` 等既有键字节不变
    （既有键已被下游引用）。翻滚用整体俯仰关键 `bp`（骨盆为轴，度，缺省 0 走旧计算路径，逐位一致），整个身体绕骨盆翻转，翻滚全程不穿地。空中攻击不带离地量、不带连招与闪避取消窗口。
13. **不做布娃娃、不做根运动**：位移由逻辑驱动（ADR-0116），翻滚与落地缓冲是关键帧动画；动画不携带根位移（04 第 10 节第 10 条）。

14. **躺姿五键用整身俯仰 `bp` 重绘**（M4-W5，04 第 10 节第 3 条）：`hit.launch`、`hit.knockdown`、`hit.getup`、`death`、`hit.launch.land` 的躺平段此前靠"腿绕髋整体后摆"、肩/髋被迫超出人体范围；
    现在用 M4-D 已有的 `bp`（整身绕骨盆俯仰，仰卧 -88、俯卧 +88，后倒起手按体量反应缩放）放平，肩/髋/躯干回到人体范围（`LYING_BACK`/`LYING_FRONT` 及其落稳姿势在 `poses.py`）。
    `hit.launch.land` 与 `hit.getup` 共用躺姿（落地末姿势 = 起身起姿势），所以随之重绘：改的是五个键而不是四个。键名、时长、事件（名与时刻）、帧数不变，其余键（除 M4-W6 重绘的 `hit.heavy`、`hit.knockback`）的规格条目与图像字节不变
    （含体量组，缺省 `bp = 0` 走旧计算路径，逐位一致）。`hit.heavy`、`hit.knockback` 在 M4-W6 重绘到人体范围（判断记录 16），不再有任何角限豁免。
15. **`cast` 的 `release` 在 150 ms**（M4-W5）：`cast` 总时长 600 ms、12 帧不变，相位改为 windup 150 / release 100 / recovery 350（原 250/100/250），`release` 事件 `time_pct` 0.4167 → 0.25。
    理由：实验室技能（`data/_lab_action`）命中标记最常见是 150 ms（slash、combo3、slam、charge、bolt、projectile），改一个 `cast` 剪辑比改十多个技能时间线（技能时间线是 feellab 基线的输入）影响面小得多，
    且 `cast` 只被表现层读。起手 150 ms 的技能动画命中点与逻辑命中标记逐毫秒相等；起手 100 ms 与 300 ms 的技能由判断记录 17 的施放点变体对齐（武器风格 `cast_anim_override`）。
16. **重受击、击退的极值帧重绘到人体范围**（M4-W6）：`hit.heavy`、`hit.knockback` 此前用 2.2 倍外推表达强后仰（肩后伸 -125 度），靠骨骼版每键豁免表保留旧限。M4-W6 放开这两个键的字节锁：
    限肢外推倍率降为 1.3 / 1.4（`RECOIL_LIMB_STRENGTH`，并受 `FALL_REACT_CAP` 约束），后仰由骨盆整身俯仰 `bp` 补足（`RECOIL_PELVIS_TILT`：重受击 -12 度、击退 -24 度，乘体量反应倍率，轻体量更大、重体量更小）。
    键名、时长、事件、帧数不变；轻/重体量组、装备层与方向变体随生成器同步重出；豁免表与取限函数一并删除（骨骼版判断记录 18、21）。
17. **施放点变体 `cast.quick`、`cast.heavy`**（M4-W6）：总时长 600 ms、12 帧与 `cast` 相同，相位 windup 100 / release 100 / recovery 400 与 windup 300 / release 100 / recovery 200，`release` 事件落在前摇结束处
    （100 ms、300 ms），可选档（`optional`），带武器层（随装备族换层），轻/重体量组各带同名键。游戏把技能指到变体的方式是武器风格 `display.weapon_style.cast_anim_override`（技能 id 到剪辑资源引用），
    回退链 `cast.<变体>` → `cast`，没声明覆盖的游戏逐位不变；实验室用 `data/_lab_action/display/display.weapon_style.json` 把起手 100 ms 的 combo1/combo2/poise_chip/lunge/spx_jab 指向 `cast.quick`、起手 300 ms 的 elite_swing 指向 `cast.heavy`。
    自检检查两个变体的 `release` 时刻（容差 0.05 ms）、总时长与帧数同 `cast`。

## 范围与边界（设计决定，M4 清扫由"已知限制"改写）

- `frames.json` 没有原点/锚点字段：约定脚点 `(72,140)`（画布水平居中、距底边 4 像素）写在规格文件里，由本工具的一致性检查（`verify`）对账内容，运行期不校验。理由：加字段要改序列帧资产的格式契约，占位美术不为此改格式；04 第 8 节 `sprite` 型的原点校验面向游戏自己导入的美术，由游戏的美术管线提供原点。
- 无族状态剪辑（`hit.*`/`death`/`jump`/`cast`/`dodge`）的武器层统一画占位单手剑、固定持握俯仰角，不随 2h 等族改形。理由：这些键没有族维度；装备美术按 ADR-0100 用自己的逐层剪辑覆盖（`toolchain/std_equip_set` 给每件占位装备按其族出这些键的武器层）。
- 闪避是低身冲刺姿势，不是翻滚；受击/倒地/起身/死亡/击飞翻滚是关键姿势插值的占位，不含布娃娃物理。理由：位移由逻辑驱动（ADR-0116），动画不反向驱动单位位置（04 第 10 节第 10 条）。
- 假人的 `idle`/`move` 手臂与武器姿势是手调关键姿势，不保证"双手握持时副手恰好落在剑柄上"（2h 族副手只是靠近）。理由：占位只证明契约接线；握持精度属于装备美术（ADR-0100），框架不出最终美术。
- 每循环位移依赖自拟的参考基础移速 2.0 身高/秒（见上），不做"脚是否打滑"的像素级验证，只验证接触姿势两脚间距。理由：步幅是运行期参数，播放速率会按单位真实移速缩放，参考移速只是占位的出图约定。
- 左侧档位不落盘，依赖运行期水平翻转（14 第 2.1 节镜像规则）；主手因此在镜像档位出现在另一侧。理由：镜像规则是框架统一约定，落盘左侧档位会让资产体积翻倍，持握侧不对称由装备美术需要时自行覆盖。
- 步幅不出独立资产（运行期播放速率，同上）。
