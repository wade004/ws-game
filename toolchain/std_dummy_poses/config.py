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

# 参考时长（ms）。04 §4 举例：走 1000、跑 600；其余自拟。
DUR_IDLE = 1200
DUR_IDLE_COMBAT = 1000
DUR_WALK = 1000
DUR_RUN = 600

#: 三相（windup, active, recovery）起点。1h/2h 第一段取自 05 §9（110/80/190 与 170/100/290）；
#: 无 05 来源的取值全部自拟（unarmed = 1h × 0.85 取整到 5 ms；后续段见 attack_phases）。
PHASES_SEG1 = {
    "unarmed": (95, 70, 160),   # 自拟：1h × 0.85
    "1h": (110, 80, 190),       # 05 §9 单手剑
    "2h": (170, 100, 290),      # 05 §9 巨剑
}
#: 第 3 段（收尾）相对第 1 段的倍率：windup、active、recovery（自拟）。
FINISHER_SCALE = (1.2, 1.25, 1.3)
#: 闪避取消起点（动作进度）。来源：05 §9（单手剑 0.65 / 巨剑 0.75）；unarmed 自拟取 0.65。
CANCEL_DODGE_PROGRESS = {"unarmed": 0.65, "1h": 0.65, "2h": 0.75}
#: 各武器族的攻击段数（04 §3：每个武器族至少一段；任务书要求各 1~3 段）。
ATTACK_SEGMENTS = {"unarmed": 3, "1h": 3, "2h": 2}

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
    "cast": (("windup", 250), ("release", 100), ("recovery", 250)),
    "dodge": (("start", 60), ("motion", 240), ("recover", 100)),
}
#: 闪避无敌窗口：motion 相起点起 180 ms（自拟）。
DODGE_INVULN_MS = 180

FAMILIES_WITH_WEAPON = ("1h", "2h")
#: 无武器族的状态剪辑（hit.*/death/jump/cast/dodge）也要给武器层（hand_main）出逐层剪辑，使持械角色播这些键时
#: 武器随身体帧走（ADR-0072：缺逐层剪辑的层维持静态图）。这些剪辑不分族，武器层统一画占位单手剑（假人只提供
#: 一把占位武器；游戏装备美术按 ADR-0100 候选命名覆盖）。整身合成与身体层仍是徒手姿势。
WEAPON_LAYER_DEFAULT_FAMILY = "1h"
#: 状态剪辑里武器相对躯干的俯仰角（度）：90 = 剑身沿躯干朝向水平前指（与战斗站姿 wp=100 接近）。默认 0 会让
#: 剑尖竖直向下，落地蹲姿/倒地时扎出画布底边（自检会报裁切）；wp=40（和平持握）在落地蹲姿时仍擦到底边。
STATE_CLIP_WEAPON_PITCH = 90.0
STATE_CLIPS_WITH_WEAPON_LAYER = ("hit", "death", "jump", "cast", "dodge")
WEAPON_LENGTH = {"1h": 0.38, "2h": 0.58}
WEAPON_SECTION = {"1h": 0.024, "2h": 0.034}

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

    @property
    def stem(self) -> str:
        return STEM_PREFIX + self.key.replace(".", "_")

    @property
    def resource_ref(self) -> str:
        target = self.alias_of or self.key
        return "sprite_anim." + STEM_PREFIX + target.replace(".", "_")

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
    return clips


# --------------------------------------------------------------------------
# 时间标记（04 §5）：返回 [(事件名, 毫秒偏移)]
# --------------------------------------------------------------------------

def events_for(clip: ClipDef) -> list[tuple[str, float]]:
    total = clip.total_ms
    ev: list[tuple[str, float]] = []
    if clip.state == "move":
        # 走/跑：两次脚触地，接触姿势是循环起点，另一脚在半周期。
        ev += [("footstep", 0.0), ("footstep", total / 2.0)]
    elif clip.pose_id == "attack":
        w, a, _r = (ms for _, ms in clip.phases)
        ev += [("active_start", float(w)), ("hit", w + a / 2.0), ("active_end", float(w + a)),
               ("trail_start", float(w)), ("trail_end", float(w + a))]
        if clip.segment < ATTACK_SEGMENTS[clip.family]:
            r = clip.phases[2][1]
            ev += [("combo_open", float(w + a)), ("combo_close", w + a + 0.8 * r)]
        ev.append(("cancel_open:dodge", CANCEL_DODGE_PROGRESS[clip.family] * total))
    elif clip.key == "cast":
        ev.append(("release", float(clip.phases[0][1])))
    elif clip.key == "dodge":
        start = clip.phases[0][1]
        motion = clip.phases[1][1]
        ev += [("motion_start", float(start)), ("invuln_start", float(start)),
               ("invuln_end", float(start + DODGE_INVULN_MS)), ("motion_end", float(start + motion))]
    elif clip.state == "hit" and clip.key != "hit.getup":
        ev.append(("impact", 0.0))
    return ev


def frames_per_phase(ms: float, fps: int = FPS) -> int:
    """帧数规则：每相 max(1, floor(ms × fps / 1000 + 0.5))（四舍五入，0.5 进位；不用银行家舍入）。"""
    return max(1, int(math.floor(ms * fps / 1000.0 + 0.5)))


def phase_frame_plan(clip: ClipDef, fps: int = FPS) -> list[tuple[str, int, float]]:
    """[(相位名, 帧数, 每帧毫秒)]；相位内帧时长均分，使各相边界与三相毫秒数精确对齐。"""
    return [(name, frames_per_phase(ms, fps), ms / frames_per_phase(ms, fps)) for name, ms in clip.phases]


def step_displacement_bh(gait: str) -> float:
    """每步位移（身高倍数）= 标称速度 × 剪辑时长 / 2（每循环两步）。"""
    ratio = WALK_SPEED_RATIO if gait == "walk" else RUN_SPEED_RATIO
    dur = (DUR_WALK if gait == "walk" else DUR_RUN) / 1000.0
    return REFERENCE_BASE_SPEED_BH_PER_S * ratio * dur / 2.0
