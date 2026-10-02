"""假人姿势集生成器的全部可调参数与剪辑目录（键、时长、相位、事件）。

所有"数"在这里集中声明并写明来源；生成、数据行、自检三处只读本模块，不各自持有副本。

来源标注约定：
- 04 §N：architecture/手感设计/04_姿势与动画契约.md 第 N 节。
- 05 §9：architecture/手感设计/05_手感档案与解析.md 第 9 节"试调起点"。
- 02 §N：architecture/手感设计/02_移动与运动仲裁.md 第 N 节。
- 自拟：文档没有给数，生成器按"与相邻已拍板数同量级、先能用"拍的起点，全部标 experimental，
  由手感实验室（06）试玩后决定是否改动（改动只需改本文件并重新生成）。
"""

from __future__ import annotations

import dataclasses
import math
from dataclasses import dataclass

# --------------------------------------------------------------------------
# 画布与标定（sprite 型：中性像素密度）
# --------------------------------------------------------------------------

#: 帧画布像素尺寸。取 144x144 的理由：2h 巨剑水平前伸（肩到剑尖约 0.95 倍身高）时侧面方向不被裁切，
#: 头顶留 ~1.9 倍身高净空供跳跃与上举；自检会断言任何帧的不透明像素都不触碰画布边缘。
CANVAS = (144, 144)
#: 身高对应的像素数（"参考身高"）。64 像素/身高，与框架占位资产 pixels_per_unit=32 搭配即身高 2 世界单位。
BODY_HEIGHT_PX = 64
#: 脚点（地面点）像素坐标：水平居中，距画布底边 4 像素（与 placeholder_hero 的 root=[32,92]/96 同一留白惯例）。
ROOT_PX = (72, 140)
PIXELS_PER_UNIT = 32

#: 标定帧率。来源：feel.calibration 无缺省值（05 §2 只列字段），取与框架占位特效（assets/_placeholder/vfx，
#: fps=20）相同的 20 作为框架级假人集的标定值；每帧时长 = 1000/FPS = 50 ms。
FPS = 20

#: 默认方向档数（14 §2.1：默认 8，允许 4/16）。
DEFAULT_DIRECTION_COUNT = 8

#: 纸娃娃层名：身体层固定 body；武器层沿用占位英雄的主手层名 hand_main（assets/_placeholder/sprites/
#: placeholder_hero 的 layers），使 display.map.paperdoll_layers 直接复用。
LAYER_BODY = "body"
LAYER_WEAPON = "hand_main"

#: sprite_anim 资源名前缀（resource_ref = sprite_anim.<STEM_PREFIX><键点号换下划线>）。
STEM_PREFIX = "std_dummy_"
#: 框架级姿势集 id（display.anim_set.<ANIM_SET_NAME>）。判断记录见 README：05 §9 写"假人集一组"，故一行。
ANIM_SET_NAME = "std_dummy_biped"

#: 假人比例（身高 = 1.0）。
REST_HIP_Y = 0.52
THIGH = 0.24
SHIN = 0.24
UPPER_ARM = 0.16
FOREARM = 0.15
LEG_LENGTH = THIGH + SHIN

# --------------------------------------------------------------------------
# 位移与步幅（走/跑每循环位移）
# --------------------------------------------------------------------------

#: 参考基础移速（身高/秒）。02 没有给基础移速绝对值（它属于 feel.calibration），这里自拟 2.0 作为
#: 推算每循环位移的参照；换游戏的标定后，播放速率按 02 §7 公式（实际速度/(每循环位移÷时长)）自动匹配，
#: 所以这个数只影响"标称速度下脚是否打滑"，不影响正确性。
REFERENCE_BASE_SPEED_BH_PER_S = 2.0
#: 来源：02 §2 运动档案 walk_speed_ratio 缺省 0.45；run 以基础移速（比值 1.0）。
WALK_SPEED_RATIO = 0.45
RUN_SPEED_RATIO = 1.0
#: 冲刺（可选步态 move.sprint，04 §3）：02 §2 的 sprint_speed_ratio 没有缺省值（可选倍率，合法范围 1～4），自拟 1.5（experimental）。
SPRINT_SPEED_RATIO = 1.5

# 参考时长（ms）。04 §4 举例：走 1000、跑 600；其余自拟。
DUR_IDLE = 1200
DUR_IDLE_COMBAT = 1000
DUR_WALK = 1000
DUR_RUN = 600
#: 冲刺循环时长（自拟）：每步位移 = 2.0 × 1.5 × 0.45 / 2 = 0.675 身高，腿摆幅 ≈ 44.7 度。
DUR_SPRINT = 450
#: 受击后的状态与可选键时长（自拟，experimental）。
DUR_STUNNED = 1200
DUR_BLOCK = 800
DUR_WOUNDED_IDLE = 1400
DUR_WOUNDED_IDLE_COMBAT = 1200
#: 带伤变体（wounded）的每步位移相对标称值的倍率（自拟）：蹒跚的步幅更短，腿摆幅按此反推。
WOUNDED_STRIDE_FACTOR = 0.7
#: 手感落地 M4-D 追加（自拟，experimental）：空中姿势（jump.rise/fall/land、hit.air）、格挡受击、眩晕摇晃、击飞翻滚与落地缓冲。
DUR_JUMP_RISE = 500
DUR_JUMP_FALL = 600
DUR_STUNNED_SWAY = 1600
DUR_LAUNCH_TUMBLE = 800

#: 三相（windup, active, recovery）起点。1h/2h 第一段取自 05 §9（110/80/190 与 170/100/290）；
#: 无 05 来源的取值全部自拟（unarmed = 1h × 0.85 取整到 5 ms；后续段见 attack_phases）。
PHASES_SEG1 = {
    "unarmed": (95, 70, 160),   # 自拟：1h × 0.85
    "1h": (110, 80, 190),       # 05 §9 单手剑
    "2h": (170, 100, 290),      # 05 §9 巨剑
    # 手感落地 M3-D 追加的武器族（05 §9 没有给数，全部自拟 experimental：与相邻族同量级，按"越重越慢"排序）。
    "polearm": (130, 90, 250),  # 自拟：介于 1h 与 2h 之间，直刺前摇短、后摇长
    "staff": (150, 90, 260),    # 自拟：略慢于长柄
    "bow": (300, 60, 180),      # 自拟：拉弓前摇长、放箭判定相极短（20 fps 下 1 帧）
    "dual": (80, 60, 140),      # 自拟：双持最快，约 1h × 0.75
    "shield": (140, 80, 240),   # 自拟：盾击，前摇与后摇都偏长
}
#: 第 3 段（收尾）相对第 1 段的倍率：windup、active、recovery（自拟）。
FINISHER_SCALE = (1.2, 1.25, 1.3)
#: 闪避取消起点（动作进度）。来源：05 §9（单手剑 0.65 / 巨剑 0.75）；unarmed 自拟取 0.65。
CANCEL_DODGE_PROGRESS = {"unarmed": 0.65, "1h": 0.65, "2h": 0.75,
                         "polearm": 0.7, "staff": 0.7, "bow": 0.6, "dual": 0.6, "shield": 0.75}  # 新增族自拟
#: 各武器族的攻击段数（04 §3：每个武器族至少一段；任务书要求各 1~3 段）。
ATTACK_SEGMENTS = {"unarmed": 3, "1h": 3, "2h": 2,
                   "polearm": 2, "staff": 2, "bow": 1, "dual": 3, "shield": 1}  # 新增族自拟：重武器两段，远程与盾击一段

#: 非循环剪辑相位（名称, ms）。自拟（04 §4 只给了轻攻击 450 ms 的举例量级）。
PHASES_OTHER = {
    "hit": (("impact", 80), ("recover", 220)),
    "hit.light": (("impact", 60), ("recover", 190)),
    "hit.heavy": (("impact", 100), ("recover", 300)),
    "hit.knockback": (("impact", 100), ("recover", 400)),
    "hit.knockdown": (("fall", 300), ("lie", 400)),
    "hit.getup": (("rise", 600),),
    "death": (("fall", 400), ("lie", 500)),
    "jump": (("takeoff", 120), ("air", 360), ("land", 120)),
    # 施法：前摇取 150 ms（M4-W5 起；此前 250）。理由：手感实验室的动作式技能经读条剪辑 cast 播放，其时间线 hit 标记典型值为 150 ms
    # （100 ms 的连段技能也在 150 之前出手），引擎侧命中帧事件不能早于逻辑命中，所以施放点 release 取 150 ms 而不是 250；
    # 总时长 600 ms 与帧数 12 不变（前摇/后摇的帧数 3/2/7 与此前 5/2/5 之和相同）。
    "cast": (("windup", 150), ("release", 100), ("recovery", 350)),
    "dodge": (("start", 60), ("motion", 240), ("recover", 100)),
    # 手感落地 M3-D 追加（自拟）：抛飞 = 冲击 + 滞空（抬升弧线）+ 落地（滑入躺姿，之后接 hit.getup）。
    "hit.launch": (("impact", 100), ("air", 450), ("land", 250)),
    # 手感落地 M4-D 追加（自拟）：落地瞬态 = 吸收冲击 + 起身回到站姿；空中受击 = 冲击 + 回到下落姿势；
    # 格挡受击 = 冲击后仰 + 阻尼抖动回到格挡持握；击飞落地缓冲 = 触地 + 弹起 + 滑入躺姿。
    "jump.land": (("impact", 90), ("recover", 210)),
    "hit.air": (("impact", 100), ("recover", 300)),
    "hit.block": (("impact", 80), ("recover", 320)),
    "hit.launch.land": (("impact", 120), ("bounce", 180), ("settle", 300)),
}
#: 启停过渡剪辑（04 §3 可选）的相（自拟）：起步 = 前倾 + 迈出第一步，进入 move.run 的循环起点；急停 = 刹车 + 站稳，
#: 回到 idle 起点；急转 = 滑步 + 拧身，回到 move.run 的循环起点。
TRANSITION_PHASES = {
    "move.start": (("lean", 100), ("step", 200)),
    "move.stop": (("brake", 150), ("settle", 150)),
    "move.pivot": (("skid", 150), ("turn", 150)),
}
#: 闪避无敌窗口：motion 相起点起 180 ms（自拟）。
DODGE_INVULN_MS = 180

#: 手感落地 M3-D 补齐的武器族（04 §2 的族清单里除 unarmed/1h/2h 之外的全部）。
EXTRA_FAMILIES = ("polearm", "bow", "staff", "dual", "shield")
FAMILIES_WITH_WEAPON = ("1h", "2h") + EXTRA_FAMILIES
#: 双手（或副手也持物）的族：走/跑时主手与副手的手臂姿势都固定在持握姿势上（不随步摆臂）；弓只有主手持弓，副手自由。
OFFHAND_HELD_FAMILIES = ("2h", "polearm", "staff", "dual", "shield")
#: 无武器族的状态剪辑（hit.*/death/jump/cast/dodge）也要给武器层（hand_main）出逐层剪辑，使持械角色播这些键时
#: 武器随身体帧走（ADR-0072：缺逐层剪辑的层维持静态图）。这些剪辑不分族，武器层统一画占位单手剑（假人只提供
#: 一把占位武器；游戏装备美术按 ADR-0100 候选命名覆盖）。整身合成与身体层仍是徒手姿势。
WEAPON_LAYER_DEFAULT_FAMILY = "1h"
#: 状态剪辑里武器相对躯干的俯仰角（度）：90 = 剑身沿躯干朝向水平前指（与战斗站姿 wp=100 接近）。默认 0 会让
#: 剑尖竖直向下，落地蹲姿/倒地时扎出画布底边（自检会报裁切）；wp=40（和平持握）在落地蹲姿时仍擦到底边。
STATE_CLIP_WEAPON_PITCH = 90.0
STATE_CLIPS_WITH_WEAPON_LAYER = ("hit", "death", "jump", "cast", "dodge", "stunned", "block")
WEAPON_LENGTH = {"1h": 0.38, "2h": 0.58,
                 # 新增族（自拟）：长柄向前最远 0.65（手到尖），向后留 0.25 的柄尾，使侧视直刺不裁切画布；
                 # 法杖同理；弓是上下对称的弓臂（半长）；双持主手短剑、副手匕首沿前臂；盾族主手短剑 + 前臂盾牌。
                 "polearm": 0.93, "staff": 0.75, "bow": 0.30, "dual": 0.34, "shield": 0.32}
WEAPON_SECTION = {"1h": 0.024, "2h": 0.034, "polearm": 0.02, "staff": 0.022, "bow": 0.015, "dual": 0.022, "shield": 0.022}

# --------------------------------------------------------------------------
# 方向档
# --------------------------------------------------------------------------

# 与 toolchain/asset_import/directions.py 同一命名（14 §2.1 + 16 方向延伸）；这里不 import 它是为了让本包
# 可单独运行，自检里有一条断言与该模块逐项一致。
CANONICAL_SLOTS = {
    4: ["front", "side_r", "back"],
    8: ["front", "front_side_r", "side_r", "back_side_r", "back"],
    16: [
        "front", "front_side_r_a", "front_side_r", "front_side_r_b", "side_r",
        "back_side_r_b", "back_side_r", "back_side_r_a", "back",
    ],
}


def canonical_slots(direction_count: int) -> list[str]:
    if direction_count not in CANONICAL_SLOTS:
        raise ValueError(f"不支持的方向档数 {direction_count}（仅 4/8/16）")
    return list(CANONICAL_SLOTS[direction_count])


def slot_yaw_deg(direction_count: int) -> dict[str, float]:
    """canonical 档位 → 朝向偏航角：front=0（面向镜头）、side_r=90（面向画面右）、back=180，等分。"""
    slots = canonical_slots(direction_count)
    n = len(slots) - 1
    return {name: 180.0 * i / n for i, name in enumerate(slots)}


def mirror_pairs(direction_count: int) -> list[dict]:
    """左侧档位由右侧 canonical 水平翻转回填（14 §2.1），不单独落盘。"""
    slots = canonical_slots(direction_count)
    out = []
    for name in slots[1:-1]:
        parts = name.split("_")
        for i in range(len(parts) - 1, -1, -1):
            if parts[i] == "r":
                parts[i] = "l"
                break
        out.append({"direction_slot": "_".join(parts), "mirror_of": name, "flip_x": True})
    return out


# --------------------------------------------------------------------------
# 剪辑目录
# --------------------------------------------------------------------------

@dataclass(frozen=True)
class ClipDef:
    key: str                     # 04 §2.1 语法的剪辑键
    state: str                   # idle/move/attack/cast/hit/death/jump 或 dodge
    pose_id: str                 # 姿势函数 id（poses.py）
    family: str | None           # None（徒手基础）/unarmed/1h/2h
    loop: bool
    phases: tuple                # ((相位名, ms), ...)
    tier: str                    # required / recommended
    segment: int = 0             # 攻击段号（1 起），非攻击为 0
    gait: str | None = None
    combat: bool = False
    alias_of: str | None = None  # 非 None：不单独出资源，复用目标键的资源（如 attack -> attack.unarmed）
    # 手感落地 M3-D 追加字段（缺省值 = 既有剪辑的取值，既有剪辑的行为与产物不变）
    variant: str | None = None       # 04 §2 变体维度（wounded 等）；攻击段号与 hit 后缀不算变体
    transition: str | None = None    # 启停过渡剪辑（start/stop/pivot，04 §3），非循环、不是状态
    stride_factor: float = 1.0       # 每步位移相对该步态标称值的倍率（带伤蹒跚 < 1）
    mass: str | None = None          # 体量档（MASS_TIERS 的档名；主集恒为 None。手感落地 M4-D 起 sprite 与 model 两版共用）
    # 手感落地 M4-D 追加字段
    air: bool = False                # 空中动作（attack.air.* 等）：不带连招/闪避取消事件

    @property
    def stem(self) -> str:
        return STEM_PREFIX + (f"{self.mass}_" if self.mass else "") + self.key.replace(".", "_")

    @property
    def resource_ref(self) -> str:
        target = self.alias_of or self.key
        # 体量组（手感落地 M4-D）：在 std_dummy_ 后插 <档>_（与 model 版 clip_state_name 同规则）；主集不带。
        return "sprite_anim." + STEM_PREFIX + (f"{self.mass}_" if self.mass else "") + target.replace(".", "_")

    @property
    def has_weapon(self) -> bool:
        return self.family in FAMILIES_WITH_WEAPON

    @property
    def weapon_layer_family(self) -> str | None:
        """武器层（hand_main）逐层剪辑里画的武器族；None 表示该剪辑没有武器层剪辑。
        持械族剪辑取自身族；无族的状态剪辑（hit.*/death/jump/cast/dodge）取占位单手剑，其余（徒手 idle/move/
        attack.unarmed 与别名 attack）无武器层。"""
        if self.has_weapon:
            return self.family
        if self.key.split(".")[0] in STATE_CLIPS_WITH_WEAPON_LAYER:
            return WEAPON_LAYER_DEFAULT_FAMILY
        return None

    @property
    def total_ms(self) -> int:
        return sum(ms for _, ms in self.phases)


def _round5(x: float) -> int:
    return int(math.floor(x / 5.0 + 0.5) * 5)


def attack_phases(family: str, segment: int) -> tuple:
    w, a, r = PHASES_SEG1[family]
    if segment == ATTACK_SEGMENTS[family] and segment >= 3:
        sw, sa, sr = FINISHER_SCALE
        w, a, r = _round5(w * sw), _round5(a * sa), _round5(r * sr)
    return (("windup", w), ("active", a), ("recovery", r))


def attack_key(family: str, segment: int) -> str:
    return f"attack.{family}" if segment == 1 else f"attack.{family}.{segment:02d}"


def air_attack_key(family: str) -> str:
    """空中攻击键（手感落地 M4-D）：``attack.air.<族>``，回落链 attack.air.<族> -> attack.<族>；徒手族 ``unarmed`` 另有别名 ``attack.air``。"""
    return f"attack.air.{family}"


def build_clip_defs() -> list[ClipDef]:
    clips: list[ClipDef] = []
    for fam in (None, "1h", "2h"):
        sfx = f".{fam}" if fam else ""
        clips.append(ClipDef(f"idle{sfx}", "idle", "idle", fam, True, (("loop", DUR_IDLE),),
                             "required" if fam is None else "recommended"))
        clips.append(ClipDef(f"idle.combat{sfx}", "idle", "idle", fam, True, (("loop", DUR_IDLE_COMBAT),),
                             "recommended", combat=True))
        clips.append(ClipDef(f"move.walk{sfx}", "move", "walk", fam, True, (("loop", DUR_WALK),),
                             "required" if fam is None else "recommended", gait="walk"))
        clips.append(ClipDef(f"move.run{sfx}", "move", "run", fam, True, (("loop", DUR_RUN),),
                             "required" if fam is None else "recommended", gait="run"))
        clips.append(ClipDef(f"move.run.combat{sfx}", "move", "run", fam, True, (("loop", DUR_RUN),),
                             "recommended", gait="run", combat=True))
    for fam in ("unarmed", "1h", "2h"):
        for seg in range(1, ATTACK_SEGMENTS[fam] + 1):
            clips.append(ClipDef(attack_key(fam, seg), "attack", "attack", fam, False,
                                 attack_phases(fam, seg), "required" if seg == 1 else "recommended",
                                 segment=seg))
    # 基础 attack 键：回落链终点，复用 unarmed 第一段的资源（04 §3 必备：attack）。
    clips.append(ClipDef("attack", "attack", "attack", "unarmed", False, attack_phases("unarmed", 1),
                         "required", segment=1, alias_of="attack.unarmed"))
    for key in ("hit", "hit.light", "hit.heavy", "hit.knockback", "hit.knockdown", "hit.getup"):
        state = "hit"
        tier = "required" if key == "hit" else "recommended"
        clips.append(ClipDef(key, state, key, None, False, PHASES_OTHER[key], tier))
    clips.append(ClipDef("death", "death", "death", None, False, PHASES_OTHER["death"], "required"))
    clips.append(ClipDef("jump", "jump", "jump", None, False, PHASES_OTHER["jump"], "recommended"))
    clips.append(ClipDef("cast", "cast", "cast", None, False, PHASES_OTHER["cast"], "recommended"))
    clips.append(ClipDef("dodge", "attack", "dodge", None, False, PHASES_OTHER["dodge"], "recommended"))
    # ---- 手感落地 M3-D 追加：以下条目只追加在末尾，既有键的位置与内容不变 ----
    clips += _m3d_clips()
    # ---- 手感落地 M4-D 追加：同样只追加在末尾 ----
    clips += _m4d_clips()
    return clips


_CLIPS_CACHE: dict[str, ClipDef] = {}


def clip_by_key(key: str) -> ClipDef:
    """按键取剪辑定义（姿势函数里引用基础剪辑 idle/move.run 的循环起点姿势用，结果缓存）。"""
    if not _CLIPS_CACHE:
        _CLIPS_CACHE.update({c.key: c for c in build_clip_defs()})
    return _CLIPS_CACHE[key]


def _alias(target: ClipDef, key: str, combat: bool = False) -> ClipDef:
    """别名键：不单独出资源，复用目标键的资源（数据行里指向同一个 resource_ref），其余字段照抄目标。"""
    return dataclasses.replace(target, key=key, alias_of=target.key, combat=combat)


def _m3d_clips() -> list[ClipDef]:
    """04 §3 可选键补齐 + 04 §2 武器族补齐（polearm/bow/staff/dual/shield）。

    冲刺的战斗姿态版（move.sprint[.combat].<族>）取别名：冲刺是全速奔跑，战斗与和平姿态不分（自拟，省一半资源）；
    和平姿态版是真实剪辑。判据：运行期回落链先走完带 sprint 段的候选再用 run 代替（04 §2.2 判断记录），所以只要声明
    了 move.sprint 就必须同时声明各族与战斗姿态的 sprint 键，否则持械冲刺会先落到徒手冲刺剪辑。"""
    out: list[ClipDef] = []
    walk_combat = ClipDef("move.walk.combat", "move", "walk", None, True, (("loop", DUR_WALK),), "optional",
                          gait="walk", combat=True)
    sprint = ClipDef("move.sprint", "move", "sprint", None, True, (("loop", DUR_SPRINT),), "optional", gait="sprint")
    out += [walk_combat, sprint, _alias(sprint, "move.sprint.combat", combat=True)]
    for name, transition in (("move.start", "start"), ("move.stop", "stop"), ("move.pivot", "pivot")):
        out.append(ClipDef(name, "move", "move_" + transition, None, False, TRANSITION_PHASES[name], "optional",
                           transition=transition))
    out.append(ClipDef("hit.launch", "hit", "hit.launch", None, False, PHASES_OTHER["hit.launch"], "optional"))
    out.append(ClipDef("stunned", "stunned", "stunned", None, True, (("loop", DUR_STUNNED),), "optional"))
    out.append(ClipDef("block", "block", "block", None, True, (("loop", DUR_BLOCK),), "optional"))
    # 带伤变体（04 §3 可选"wounded 变体"）：变体是回落链最先被去掉的维度，所以只有"请求带变体且键表里有同族同姿态的
    # 变体键"才会命中；这里出徒手基础族的待机/走/跑，持械族的带伤变体由游戏按需用 extends 补（README 已知限制）。
    out.append(ClipDef("idle.wounded", "idle", "idle", None, True, (("loop", DUR_WOUNDED_IDLE),), "optional",
                       variant="wounded"))
    out.append(ClipDef("idle.combat.wounded", "idle", "idle", None, True, (("loop", DUR_WOUNDED_IDLE_COMBAT),),
                       "optional", combat=True, variant="wounded"))
    out.append(ClipDef("move.walk.wounded", "move", "walk", None, True, (("loop", DUR_WALK),), "optional",
                       gait="walk", variant="wounded", stride_factor=WOUNDED_STRIDE_FACTOR))
    out.append(ClipDef("move.run.wounded", "move", "run", None, True, (("loop", DUR_RUN),), "optional",
                       gait="run", variant="wounded", stride_factor=WOUNDED_STRIDE_FACTOR))
    # 既有族 1h/2h：补战斗姿态的走（move.walk.combat 声明后，战斗中持械走路不能回落到徒手的 move.walk.combat）与冲刺。
    for fam in ("1h", "2h"):
        out.append(ClipDef(f"move.walk.combat.{fam}", "move", "walk", fam, True, (("loop", DUR_WALK),), "optional",
                           gait="walk", combat=True))
        spf = ClipDef(f"move.sprint.{fam}", "move", "sprint", fam, True, (("loop", DUR_SPRINT),), "optional", gait="sprint")
        out += [spf, _alias(spf, f"move.sprint.combat.{fam}", combat=True)]
    # 新增武器族：与 1h/2h 同口径（待机、战斗待机、走、跑、战斗跑、攻击各段）+ 战斗走与冲刺。
    for fam in EXTRA_FAMILIES:
        out.append(ClipDef(f"idle.{fam}", "idle", "idle", fam, True, (("loop", DUR_IDLE),), "recommended"))
        out.append(ClipDef(f"idle.combat.{fam}", "idle", "idle", fam, True, (("loop", DUR_IDLE_COMBAT),),
                           "recommended", combat=True))
        out.append(ClipDef(f"move.walk.{fam}", "move", "walk", fam, True, (("loop", DUR_WALK),), "recommended", gait="walk"))
        out.append(ClipDef(f"move.run.{fam}", "move", "run", fam, True, (("loop", DUR_RUN),), "recommended", gait="run"))
        out.append(ClipDef(f"move.run.combat.{fam}", "move", "run", fam, True, (("loop", DUR_RUN),), "recommended",
                           gait="run", combat=True))
        out.append(ClipDef(f"move.walk.combat.{fam}", "move", "walk", fam, True, (("loop", DUR_WALK),), "optional",
                           gait="walk", combat=True))
        spf = ClipDef(f"move.sprint.{fam}", "move", "sprint", fam, True, (("loop", DUR_SPRINT),), "optional", gait="sprint")
        out += [spf, _alias(spf, f"move.sprint.combat.{fam}", combat=True)]
        for seg in range(1, ATTACK_SEGMENTS[fam] + 1):
            out.append(ClipDef(attack_key(fam, seg), "attack", "attack", fam, False, attack_phases(fam, seg),
                               "required" if seg == 1 else "recommended", segment=seg))
    out.append(ClipDef("block.shield", "block", "block", "shield", True, (("loop", DUR_BLOCK),), "optional"))
    return out


def _m4d_clips() -> list[ClipDef]:
    """手感落地 M4-D 追加的键（全部 optional 档，自拟 experimental）。

    - 空中键（键名与回落链见 04 §2.2 / M4 共用约定）：``jump.rise``、``jump.fall``（循环保持姿势，滞空时长由逻辑竖直轴决定）、
      ``jump.land``（瞬态）、``hit.air``（空中受击，瞬态，结束姿势 = jump.fall 起点）、``attack.air.<族>``（每族一段，下肢收起；
      徒手另有别名 ``attack.air``）。这些剪辑**不带抬升曲线**（lift）：离地高度归逻辑竖直轴，剪辑只表达姿势。
    - 带伤变体补全：冲刺、战斗走/跑、启停过渡的 wounded 版（徒手基础族；持械族的带伤变体仍由游戏按需用 extends 补）。
    - 格挡受击 ``hit.block``（盾族 ``hit.block.shield``）：从格挡持握后仰、阻尼抖动回到持握；
      眩晕摇晃循环 ``stunned.sway``；击飞翻滚循环 ``hit.launch.tumble`` 与落地缓冲 ``hit.launch.land``（触地、弹起、滑入躺姿，之后接 hit.getup）。
    既有键一个字节都不动：这些细节全部以新键追加（04 §10 第 9 条）。"""
    out: list[ClipDef] = []
    out.append(ClipDef("jump.rise", "jump", "jump_rise", None, True, (("loop", DUR_JUMP_RISE),), "optional"))
    out.append(ClipDef("jump.fall", "jump", "jump_fall", None, True, (("loop", DUR_JUMP_FALL),), "optional"))
    out.append(ClipDef("jump.land", "jump", "jump_land", None, False, PHASES_OTHER["jump.land"], "optional"))
    out.append(ClipDef("hit.air", "hit", "hit_air", None, False, PHASES_OTHER["hit.air"], "optional"))
    for fam in ("unarmed", "1h", "2h") + EXTRA_FAMILIES:
        w, a, r = PHASES_SEG1[fam]
        out.append(ClipDef(air_attack_key(fam), "attack", "attack_air", fam, False,
                           (("windup", w), ("active", a), ("recovery", r)), "optional", segment=1, air=True))
    out.append(_alias(next(c for c in out if c.key == "attack.air.unarmed"), "attack.air"))
    # 带伤变体补全（徒手基础族）
    spr_w = ClipDef("move.sprint.wounded", "move", "sprint", None, True, (("loop", DUR_SPRINT),), "optional",
                    gait="sprint", variant="wounded", stride_factor=WOUNDED_STRIDE_FACTOR)
    out.append(spr_w)
    out.append(_alias(spr_w, "move.sprint.combat.wounded", combat=True))
    out.append(ClipDef("move.walk.combat.wounded", "move", "walk", None, True, (("loop", DUR_WALK),), "optional",
                       gait="walk", combat=True, variant="wounded", stride_factor=WOUNDED_STRIDE_FACTOR))
    out.append(ClipDef("move.run.combat.wounded", "move", "run", None, True, (("loop", DUR_RUN),), "optional",
                       gait="run", combat=True, variant="wounded", stride_factor=WOUNDED_STRIDE_FACTOR))
    for name, transition in (("move.start", "start"), ("move.stop", "stop"), ("move.pivot", "pivot")):
        out.append(ClipDef(name + ".wounded", "move", "move_" + transition, None, False, TRANSITION_PHASES[name], "optional",
                           transition=transition, variant="wounded"))
    out.append(ClipDef("hit.block", "hit", "hit_block", None, False, PHASES_OTHER["hit.block"], "optional"))
    out.append(ClipDef("hit.block.shield", "hit", "hit_block", "shield", False, PHASES_OTHER["hit.block"], "optional"))
    out.append(ClipDef("stunned.sway", "stunned", "stunned_sway", None, True, (("loop", DUR_STUNNED_SWAY),), "optional"))
    out.append(ClipDef("hit.launch.tumble", "hit", "launch_tumble", None, True, (("loop", DUR_LAUNCH_TUMBLE),), "optional"))
    out.append(ClipDef("hit.launch.land", "hit", "launch_land", None, False, PHASES_OTHER["hit.launch.land"], "optional"))
    return out


# --------------------------------------------------------------------------
# 时间标记（04 §5）：返回 [(事件名, 毫秒偏移)]
# --------------------------------------------------------------------------

def events_for(clip: ClipDef) -> list[tuple[str, float]]:
    total = clip.total_ms
    ev: list[tuple[str, float]] = []
    if clip.transition:
        # 启停过渡：各一次脚触地（起步第二段中点迈出的脚、急停刹车末、急转滑步末），不是循环剪辑，不要求每循环位移。
        ev.append(("footstep", {"start": 0.65, "stop": 0.5, "pivot": 0.5}[clip.transition] * total))
    elif clip.state == "move":
        # 走/跑/冲刺：两次脚触地，接触姿势是循环起点，另一脚在半周期。
        ev += [("footstep", 0.0), ("footstep", total / 2.0)]
    elif clip.pose_id in ("attack", "attack_air"):
        w, a, _r = (ms for _, ms in clip.phases)
        ev += [("active_start", float(w)), ("hit", w + a / 2.0), ("active_end", float(w + a)),
               ("trail_start", float(w)), ("trail_end", float(w + a))]
        if not clip.air and clip.segment < ATTACK_SEGMENTS[clip.family]:
            r = clip.phases[2][1]
            ev += [("combo_open", float(w + a)), ("combo_close", w + a + 0.8 * r)]
        if not clip.air:
            # 空中攻击每族只有一段，也不接闪避取消（空中没有地面闪避）：不带 combo_* 与 cancel_open:dodge
            ev.append(("cancel_open:dodge", CANCEL_DODGE_PROGRESS[clip.family] * total))
        if clip.family == "bow":
            # 弓的放箭点（04 §5 `release`：远程类投射物发射）= 判定相中点，与 hit 同刻（弓的命中点就是放箭点）。
            ev.append(("release", w + a / 2.0))
        # 命中帧别名（手感落地 M3-D）：角色外壳（sprite 型与 model 型）识别的命中帧事件名是 hit_frame（ADR-0017），
        # 04 §5 的 hit 是判定侧标记名；两者同刻同源，追加在该剪辑事件表末尾（既有事件的内容与顺序不变）。
        ev.append(("hit_frame", w + a / 2.0))
    elif clip.key == "cast":
        ev.append(("release", float(clip.phases[0][1])))
    elif clip.key == "dodge":
        start = clip.phases[0][1]
        motion = clip.phases[1][1]
        ev += [("motion_start", float(start)), ("invuln_start", float(start)),
               ("invuln_end", float(start + DODGE_INVULN_MS)), ("motion_end", float(start + motion))]
    elif clip.key == "jump.land":
        ev.append(("footstep", 0.0))        # 落地触地
    elif clip.key == "stunned.sway":
        ev += [("footstep", 0.0), ("footstep", total / 2.0)]   # 眩晕踉跄的两次落脚
    elif clip.state == "hit" and clip.key != "hit.getup" and not clip.loop:
        ev.append(("impact", 0.0))          # 循环的击飞翻滚（hit.launch.tumble）没有冲击点
    return ev


def frames_per_phase(ms: float, fps: int = FPS) -> int:
    """帧数规则：每相 max(1, floor(ms × fps / 1000 + 0.5))（四舍五入，0.5 进位；不用银行家舍入）。"""
    return max(1, int(math.floor(ms * fps / 1000.0 + 0.5)))


def phase_frame_plan(clip: ClipDef, fps: int = FPS) -> list[tuple[str, int, float]]:
    """[(相位名, 帧数, 每帧毫秒)]；相位内帧时长均分，使各相边界与三相毫秒数精确对齐。"""
    return [(name, frames_per_phase(ms, fps), ms / frames_per_phase(ms, fps)) for name, ms in clip.phases]


GAIT_SPEED_RATIO = {"walk": WALK_SPEED_RATIO, "run": RUN_SPEED_RATIO, "sprint": SPRINT_SPEED_RATIO}
GAIT_DURATION_MS = {"walk": DUR_WALK, "run": DUR_RUN, "sprint": DUR_SPRINT}


def step_displacement_bh(gait: str, factor: float = 1.0) -> float:
    """每步位移（身高倍数）= 标称速度 × 剪辑时长 / 2（每循环两步）× 该剪辑的步幅倍率（缺省 1，带伤变体 < 1）。"""
    ratio = GAIT_SPEED_RATIO[gait]
    dur = GAIT_DURATION_MS[gait] / 1000.0
    return REFERENCE_BASE_SPEED_BH_PER_S * ratio * dur / 2.0 * factor


# --------------------------------------------------------------------------
# 体量档（04 §6.1 / §7、05 §9"标准骨骼三组（对应三体量）"）——手感落地 M4-D：数据声明的多档，覆盖全部键
# --------------------------------------------------------------------------

#: 主集（无偏移）就是"中"体量：数据行 display.anim_set.<主集>（及 _medium 空覆盖行）。其余档逐档声明偏移，加一档 = 在
#: MASS_TIERS 里加一项并重新生成（两版生成器、自检、数据行都读这一张表，没有第二份副本）。
MASS_MAIN_TIER = "medium"
#: 档 -> 偏移（度，自拟 experimental）。静态站姿偏移叠加在主集同一姿势上（任何键）：躯干前倾/后仰（t_pitch）、头反向补偿
#: 保持视线（h_pitch）、肩/髋外展（sa/ha，正 = 向外，左右同号即对称）。重：前倾、宽站姿、手臂离身；轻：微后仰、窄站姿、贴身。
#: react = 受击类反应幅度倍率（hit*/death/击飞的受击姿势强度）：轻的更夸张、重的更稳；air = 跳跃/击飞抬升高度倍率。
#: 时长、帧数、事件、每步位移不随体量变（步幅轴是运行期播放速率，02 §7）。
MASS_TIERS: dict[str, dict[str, float]] = {
    "light": {"t_pitch": -2.0, "h_pitch": 1.5, "sa": -3.0, "ha": -1.5, "react": 1.15, "air": 1.12},
    "heavy": {"t_pitch": 6.0, "h_pitch": -4.0, "sa": 8.0, "ha": 5.0, "react": 0.8, "air": 0.88},
}
#: 受击强度上限（倍率 × 档倍率后夹到它；主集最大值 2.4 = 击飞，肩角在 SOURCE_ANGLE_LIMITS 内的上界）。
MASS_REACT_CAP = 2.4
#: 体量组资源与数据行命名：sprite/model 两版同一规则。
MASS_SET_SUFFIX_ID = "_"


def mass_react(clip: "ClipDef") -> float:
    """受击强度倍率；主集 1.0。"""
    return MASS_TIERS[clip.mass]["react"] if clip.mass else 1.0


def mass_air(clip: "ClipDef") -> float:
    return MASS_TIERS[clip.mass]["air"] if clip.mass else 1.0


def mass_clip_defs(mass: str) -> list["ClipDef"]:
    """体量档的剪辑定义：主集**全部**键（含别名键，别名目标指向组内同键）各加体量标记。顺序 = 主集顺序。"""
    return [dataclasses.replace(c, mass=mass) for c in build_clip_defs()]


def mass_anim_set_id(base_id: str, mass: str) -> str:
    return f"{base_id}_{mass}"


# --------------------------------------------------------------------------
# 过渡混合时长（手感落地 M4-D，04 §10 第 10 条）：model 型数据行 display.anim_set 的可选 blend_ms / blends
# --------------------------------------------------------------------------

def blend_ms_for(clip: "ClipDef") -> int:
    """切入该键时的交叉淡入时长（ms，自拟 experimental）。按状态/步态/过渡种类定，别名键与目标同值（只看 state/pose）。"""
    key = clip.key
    if clip.transition:
        return 60                      # 启停过渡
    if key == "hit.getup":
        return 120
    if key == "hit.air":
        return 30
    if key in ("hit.launch.tumble", "hit.launch.land", "hit.launch"):
        return 40
    if key.startswith("hit.block"):
        return 20
    if key.startswith("hit"):
        return 30                      # 受击反应要"打到身上"，淡入极短
    if key == "jump.rise":
        return 60
    if key == "jump.land":
        return 30
    if clip.state == "jump":
        return 100
    if clip.state == "attack":
        return 30 if clip.air else 40  # 含 dodge
    return {"idle": 120, "move": 100, "cast": 60, "death": 80, "stunned": 100, "block": 80}[clip.state]


#: 每对（从某键切到某键）覆盖：(from, to, ms)。键都在主集里；体量组行继承。族相关的空中攻击逐族展开。
BLEND_PAIRS: tuple[tuple[str, str, int], ...] = (
    ("jump.rise", "jump.fall", 60),
    ("jump.fall", "jump.land", 20),
    ("jump.land", "idle", 160),
    ("hit.air", "jump.fall", 60),
    ("hit.launch", "hit.air", 40),
    ("hit.launch.tumble", "hit.launch.land", 40),
    ("hit.launch.land", "hit.getup", 120),
    ("hit.knockdown", "hit.getup", 150),
    ("hit.getup", "idle", 180),
    ("hit.block", "block", 30),
    ("block", "hit.block", 15),
    ("move.stop", "idle", 80),
    ("move.start", "move.run", 70),
    ("move.pivot", "move.run", 70),
) + tuple((air_attack_key(f), "jump.fall", 60) for f in ("unarmed", "1h", "2h") + EXTRA_FAMILIES)
