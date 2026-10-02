"""姿势库：剪辑 + 时间点 -> 关节角姿势。全部是关键姿势插值或正弦循环，无随机数。

动作质量定位：几何人偶的"可读"占位（能区分走/跑/各武器的攻击/受击/倒地），不是美术。美术按 04 §4 逐键替换。
"""

from __future__ import annotations

import math

from . import config as C
from .skeleton import Pose, build_parts, make_pose, mix_pose, scale_pose, solve_ground


def smooth(u: float) -> float:
    u = max(0.0, min(1.0, u))
    return u * u * (3.0 - 2.0 * u)


def ease_in(u: float) -> float:
    u = max(0.0, min(1.0, u))
    return u ** 2


def ease_out(u: float) -> float:
    u = max(0.0, min(1.0, u))
    return 1.0 - (1.0 - u) ** 2


def _with(base: Pose, **kw) -> Pose:
    p = dict(base)
    for k, v in kw.items():
        if k not in p:
            raise KeyError(k)
        p[k] = float(v)
    return p


# --------------------------------------------------------------------------
# 持握与站姿
# --------------------------------------------------------------------------

#: 手感落地 M3-D 追加武器族的持握姿势（相对 peace/combat 的基础站姿的覆盖量；自拟，占位级）。
#: 长柄/法杖：主手握柄，和平姿态竖持在身侧（柄尖朝上），战斗姿态双手斜举向前；弓：主手持弓、副手自由，
#: 和平姿态弓下垂身侧，战斗姿态弓臂前伸；双持：双手各持一把，副手匕首沿前臂；盾族：主手短剑 + 副手前臂盾牌。
_EXTRA_PEACE = {
    "polearm": dict(m_sf=20, m_sa=8, m_el=40, wp=180),
    "staff": dict(m_sf=24, m_sa=8, m_el=34, wp=180),
    "bow": dict(m_sf=24, m_sa=8, m_el=42, wp=165),
    "dual": dict(m_sf=22, m_sa=8, m_el=55, wp=40, o_sf=22, o_sa=8, o_el=55),
    "shield": dict(m_sf=22, m_sa=8, m_el=55, wp=40, o_sf=40, o_sa=14, o_el=95),
}
_EXTRA_COMBAT = {
    "polearm": dict(m_sf=40, m_sa=-4, m_el=85, o_sf=62, o_sa=-14, o_el=70, wp=105),
    "staff": dict(m_sf=44, m_sa=-4, m_el=85, o_sf=60, o_sa=-14, o_el=72, wp=112),
    "bow": dict(m_sf=85, m_sa=-6, m_el=12, o_sf=52, o_sa=-4, o_el=118, wp=172),
    "dual": dict(m_sf=55, m_sa=6, m_el=75, o_sf=55, o_sa=6, o_el=75, wp=95),
    "shield": dict(m_sf=55, m_sa=6, m_el=75, o_sf=68, o_sa=2, o_el=100, wp=95),
}


def peace(fam: str | None) -> Pose:
    """非战斗站姿（腿直立）。"""
    p = make_pose(m_sf=4, m_sa=6, m_el=12, o_sf=-4, o_sa=6, o_el=12)
    if fam == "1h":
        p = _with(p, m_sf=22, m_sa=8, m_el=55, wp=40)
    elif fam == "2h":
        p = _with(p, m_sf=40, m_sa=-8, m_el=105, o_sf=32, o_sa=-28, o_el=100, wp=190)
    elif fam in _EXTRA_PEACE:
        p = _with(p, **_EXTRA_PEACE[fam])
    return p


def combat(fam: str | None) -> Pose:
    """战斗站姿：屈膝错步、前倾、持械前举。"""
    p = make_pose(m_hf=16, m_kn=26, o_hf=-14, o_kn=22, t_pitch=6,
                  m_sf=55, m_sa=8, m_el=105, o_sf=48, o_sa=10, o_el=112)
    if fam == "unarmed":
        fam = None
    if fam == "1h":
        p = _with(p, m_sf=55, m_sa=6, m_el=70, o_sf=35, o_sa=20, o_el=100, wp=100)
    elif fam == "2h":
        p = _with(p, m_sf=50, m_sa=-5, m_el=85, o_sf=48, o_sa=-20, o_el=92, wp=135)
    elif fam in _EXTRA_COMBAT:
        p = _with(p, **_EXTRA_COMBAT[fam])
    return p


def _base_for(clip: C.ClipDef) -> Pose:
    fam = clip.family
    return combat(fam) if clip.combat else peace(fam)


# --------------------------------------------------------------------------
# 循环：待机、走、跑
# --------------------------------------------------------------------------

def pose_idle(clip: C.ClipDef, t_ms: float) -> Pose:
    phi = (t_ms / clip.total_ms) % 1.0
    b = math.sin(2 * math.pi * phi)
    base = _base_for(clip)
    knee_extra = 3.0 * b
    p = dict(base)
    p["m_kn"] = base["m_kn"] + 5 + knee_extra
    p["o_kn"] = base["o_kn"] + 5 + knee_extra
    p["t_pitch"] = base["t_pitch"] + 1.2 * b
    p["h_pitch"] = -0.8 * b
    if clip.family not in C.FAMILIES_WITH_WEAPON:
        p["m_sf"] += 1.5 * b
        p["o_sf"] -= 1.5 * b
    if clip.variant == "wounded":
        _hunch(p, base, b)
    return p


def _hunch(p: Pose, base: Pose, b: float) -> None:
    """带伤变体（wounded）：躯干前弓、头低、膝更弯，副手按在腹部（原地修改）。"""
    p["t_pitch"] += 14.0
    p["h_pitch"] += 8.0
    p["m_kn"] += 8.0
    p["o_kn"] += 8.0
    p["o_sf"] = 30.0 + 2.0 * b
    p["o_sa"] = -6.0
    p["o_el"] = 105.0


def pose_loco(clip: C.ClipDef, t_ms: float) -> Pose:
    gait = clip.gait
    phi = (t_ms / clip.total_ms) % 1.0
    cs = math.cos(2 * math.pi * phi)
    sn = math.sin(2 * math.pi * phi)
    step = C.step_displacement_bh(gait, clip.stride_factor)
    amp = math.degrees(math.asin(step / (2.0 * C.LEG_LENGTH)))
    sprint = gait == "sprint"
    run = gait == "run" or sprint     # 冲刺沿用奔跑的姿势结构，幅度更大
    k_swing = (105.0 if sprint else 95.0) if run else 40.0
    k_stance = (14.0 if sprint else 12.0) if run else 4.0
    base = _base_for(clip)
    p = dict(base)
    p["m_hf"] = amp * cs
    p["o_hf"] = -amp * cs
    p["m_kn"] = k_stance + k_swing * max(0.0, -sn)
    p["o_kn"] = k_stance + k_swing * max(0.0, sn)
    p["t_pitch"] = ((16.0 if sprint else 10.0) if run else 3.0) + (4.0 if clip.combat else 0.0)
    p["t_yaw"] = ((11.0 if sprint else 9.0) if run else 5.0) * cs
    swing = (62.0 if sprint else 55.0) if run else 22.0
    held_main = clip.family in C.FAMILIES_WITH_WEAPON
    held_off = clip.family in C.OFFHAND_HELD_FAMILIES
    if clip.combat:
        swing *= 0.35
    if not held_main:
        p["m_sf"] = base["m_sf"] - swing * cs
        p["m_el"] = (85.0 if run else 15.0) if not clip.combat else base["m_el"]
    if not held_off:
        p["o_sf"] = base["o_sf"] + swing * cs
        p["o_el"] = (85.0 if run else 15.0) if not clip.combat else base["o_el"]
    if run:
        p["lift"] = (0.05 if sprint else 0.035) * abs(sn)
    if clip.variant == "wounded":
        _hunch(p, base, 0.0)
        p["t_pitch"] += 4.0 * sn
    return p


# --------------------------------------------------------------------------
# 攻击与技能动作：三相关键姿势
# --------------------------------------------------------------------------

def attack_keys(fam: str, seg: int) -> tuple[Pose, Pose, Pose]:
    s = combat(fam)
    if fam == "unarmed":
        if seg == 1:
            return s, _with(s, t_yaw=22, m_sf=25, m_el=125, t_pitch=4), \
                _with(s, t_yaw=-25, m_sf=88, m_el=6, m_sa=2, t_pitch=14, m_hf=28)
        if seg == 2:
            return s, _with(s, t_yaw=-22, o_sf=25, o_el=125, t_pitch=4), \
                _with(s, t_yaw=28, o_sf=88, o_el=6, o_sa=2, t_pitch=14, o_hf=-8)
        return s, _with(s, t_pitch=-8, m_hf=-25, m_kn=80, o_hf=-8, o_kn=30, m_sf=30, o_sf=70), \
            _with(s, t_pitch=-12, m_hf=88, m_kn=8, o_hf=-12, o_kn=40, m_sf=20, o_sf=75)
    if fam == "1h":
        if seg == 1:
            return s, _with(s, m_sf=155, m_el=65, wp=205, t_pitch=-4), \
                _with(s, m_sf=75, m_el=12, wp=65, t_pitch=22, m_hf=30, m_kn=20, o_hf=-22)
        if seg == 2:
            return s, _with(s, m_sf=85, m_sa=60, m_el=35, wp=90, wy=75, t_yaw=30), \
                _with(s, m_sf=80, m_sa=20, m_el=15, wp=90, wy=-55, t_yaw=-35, t_pitch=10)
        return s, _with(s, m_sf=35, m_el=140, wp=95, t_yaw=20, t_pitch=0, m_hf=8), \
            _with(s, m_sf=92, m_el=3, wp=92, t_yaw=-20, t_pitch=20, m_hf=42, m_kn=30, o_hf=-24, o_kn=10)
    if fam == "2h":
        if seg == 1:
            return s, _with(s, m_sf=165, o_sf=165, m_el=45, o_el=45, wp=215, t_pitch=-8), \
                _with(s, m_sf=62, o_sf=62, m_el=10, o_el=10, wp=82, t_pitch=18, m_hf=34, m_kn=28, o_hf=-20)
        return s, _with(s, m_sf=88, o_sf=88, m_sa=50, o_sa=-10, m_el=30, o_el=30, wp=90, wy=70, t_yaw=40), \
            _with(s, m_sf=88, o_sf=88, m_sa=15, o_sa=-15, m_el=15, o_el=15, wp=90, wy=-60, t_yaw=-40, t_pitch=8)
    return _extra_attack_keys(fam, seg, s)


def _extra_attack_keys(fam: str, seg: int, s: Pose) -> tuple[Pose, Pose, Pose]:
    """手感落地 M3-D 追加武器族的攻击关键姿势（前摇终点、判定相终点；自拟，占位级）。"""
    if fam == "polearm":
        if seg == 1:   # 直刺：双手收回腰侧 -> 向前突出，躯干前压
            return s, _with(s, m_sf=22, o_sf=30, m_el=105, o_el=95, wp=100, t_yaw=18, t_pitch=0), \
                _with(s, m_sf=86, o_sf=82, m_el=14, o_el=22, wp=90, t_yaw=-14, t_pitch=16, m_hf=32, m_kn=22, o_hf=-22)
        return s, _with(s, m_sf=80, o_sf=84, m_sa=48, o_sa=-8, m_el=30, o_el=36, wp=92, wy=70, t_yaw=38), \
            _with(s, m_sf=86, o_sf=84, m_sa=14, o_sa=-14, m_el=16, o_el=22, wp=92, wy=-58, t_yaw=-38, t_pitch=8)
    if fam == "staff":
        if seg == 1:   # 过顶下劈
            return s, _with(s, m_sf=150, o_sf=140, m_el=50, o_el=60, wp=200, t_pitch=-8), \
                _with(s, m_sf=64, o_sf=70, m_el=14, o_el=26, wp=68, t_pitch=18, m_hf=30, m_kn=26, o_hf=-20)
        return s, _with(s, m_sf=84, o_sf=86, m_sa=44, o_sa=-6, m_el=32, o_el=38, wp=88, wy=66, t_yaw=36), \
            _with(s, m_sf=84, o_sf=84, m_sa=14, o_sa=-14, m_el=18, o_el=26, wp=88, wy=-56, t_yaw=-36, t_pitch=8)
    if fam == "bow":   # 拉弦 -> 放箭：主手前伸持弓不动，副手拉到下颌再向后弹开
        return s, _with(s, o_sf=74, o_sa=-16, o_el=150, t_yaw=-8), \
            _with(s, o_sf=50, o_sa=-34, o_el=140, t_yaw=-12, t_pitch=4)
    if fam == "dual":
        if seg == 1:   # 右手斩
            return s, _with(s, m_sf=140, m_el=60, wp=160, t_yaw=22, t_pitch=0), \
                _with(s, m_sf=70, m_sa=22, m_el=14, wp=70, t_yaw=-26, t_pitch=18, m_hf=28, m_kn=22, o_hf=-20)
        if seg == 2:   # 左手斩
            return s, _with(s, o_sf=140, o_el=60, t_yaw=-22, t_pitch=0), \
                _with(s, o_sf=70, o_sa=22, o_el=14, t_yaw=26, t_pitch=18, m_hf=-18, o_hf=28, o_kn=22)
        return s, _with(s, m_sf=120, o_sf=120, m_sa=-6, o_sa=-6, m_el=60, o_el=60, wp=150, t_pitch=-6), \
            _with(s, m_sf=84, o_sf=84, m_sa=-14, o_sa=-14, m_el=12, o_el=12, wp=80, t_pitch=22, m_hf=40, m_kn=30, o_hf=-24)
    # shield：盾击——盾收回胸前 -> 盾牌向前顶出，主手短剑收在身后
    return s, _with(s, o_sf=42, o_sa=10, o_el=125, m_sf=40, m_el=100, wp=100, t_yaw=-16, t_pitch=0), \
        _with(s, o_sf=92, o_sa=0, o_el=18, m_sf=40, m_el=100, wp=100, t_yaw=12, t_pitch=18, m_hf=30, m_kn=20, o_hf=-22)


def three_phase(clip: C.ClipDef, t_ms: float, keys: tuple[Pose, Pose, Pose]) -> Pose:
    (_, w), (_, a), (_, r) = clip.phases
    stance, wind, strike = keys
    if t_ms < w:
        return mix_pose(stance, wind, smooth(t_ms / w))
    if t_ms < w + a:
        return mix_pose(wind, strike, ease_in((t_ms - w) / a) ** 0.75)
    return mix_pose(strike, stance, smooth((t_ms - w - a) / r))


def pose_attack(clip: C.ClipDef, t_ms: float) -> Pose:
    return three_phase(clip, t_ms, attack_keys(clip.family, clip.segment))


# --------------------------------------------------------------------------
# 受击、倒地、起身、死亡
# --------------------------------------------------------------------------

def _hit_pose(base: Pose, k: float) -> Pose:
    h = _with(base, t_pitch=-16, h_pitch=-18, m_sf=-20, o_sf=-24, m_el=30, o_el=30,
              m_hf=-12, m_kn=14, o_hf=8, o_kn=8, t_roll=6)
    return scale_pose(base, h, k)


#: 躺平姿势用整身俯仰 bp（M4-W5）：整个身体绕骨盆放平（bp = -88 仰面、+88 俯卧），躯干与大腿相对骨盆只剩人体范围内的小角度
#: （躯干俯仰 t_pitch 不再 ±88、髋前屈/后伸不再借"腿绕髋整体后摆"表达躺平）。肢体参数 = 此前躺姿的世界朝向减去 bp 后的残量，
#: 所以画面与此前的躺姿近似，区别是骨盆块随身体放平、关节角回到人体范围（model 型骨骼的躯干/大腿局部旋转随之只剩十几度）。
LYING_BACK = make_pose(bp=-88, t_pitch=0, h_pitch=-4, m_sf=-12, o_sf=-12, m_sa=20, o_sa=20, m_el=12, o_el=12,
                       m_hf=-2, o_hf=-6, m_kn=6, o_kn=10)
LYING_BACK_SETTLE = _with(LYING_BACK, m_sa=32, o_sa=34, m_hf=-8, o_hf=-12, m_kn=14, o_kn=12, h_pitch=-8)
CROUCH = make_pose(t_pitch=-30, m_hf=75, o_hf=70, m_kn=95, o_kn=95, m_sf=30, o_sf=30, m_el=40, o_el=40)
LYING_FRONT = make_pose(bp=88, t_pitch=0, h_pitch=-30, m_sf=170, o_sf=170, m_sa=14, o_sa=14, m_el=12, o_el=12,
                        m_hf=3, o_hf=6, m_kn=5, o_kn=8)
LYING_FRONT_SETTLE = _with(LYING_FRONT, m_sa=26, o_sa=26, h_pitch=-34, m_hf=8, o_hf=10)

#: 倒地/死亡/击飞起手的受击强度上限（base -> 受击姿势的外推倍数）：1.5 时肩后伸约 60 度、髋后伸约 26 度，不越人体范围；
#: 轻体量（反应倍率 > 1）也夹到它，重体量照倍率变小。主集的 hit.heavy/hit.knockback 另有更大的外推（字节不变，见 model 版 config.SOURCE_ANGLE_EXEMPT）。
FALL_REACT_CAP = 1.5
#: 击飞起手的骨盆后仰（度，随体量反应倍率缩放）：被击中的瞬间整个身体先向后仰，再在滞空段放平到躺姿。
LAUNCH_PELVIS_TILT = -30.0


def _react(clip: C.ClipDef, strength: float) -> float:
    """受击强度按体量档的反应倍率缩放（主集不变；上限 MASS_REACT_CAP，肩角不越 model 型的关节角限）。"""
    return min(strength * C.mass_react(clip), C.MASS_REACT_CAP) if clip.mass else strength


def _fall_start(clip: C.ClipDef, tilt: float = 0.0) -> Pose:
    """倒地类剪辑（knockdown/death/launch）的起手受击姿势：强度 FALL_REACT_CAP（按体量反应倍率缩放，但不越上限），可带骨盆后仰 tilt。"""
    k = min(FALL_REACT_CAP * (C.mass_react(clip) if clip.mass else 1.0), FALL_REACT_CAP)
    p = _hit_pose(combat(None), k)
    if tilt:
        p["bp"] = tilt * (C.mass_react(clip) if clip.mass else 1.0)
    return p


def pose_hit(clip: C.ClipDef, t_ms: float) -> Pose:
    base = combat(None)
    strength = _react(clip, {"hit": 1.0, "hit.light": 0.55, "hit.heavy": 1.5, "hit.knockback": 2.2}[clip.key])
    hp = _hit_pose(base, strength)
    (_, imp), (_, rec) = clip.phases
    if t_ms < imp:
        p = mix_pose(base, hp, ease_out(t_ms / imp))
    else:
        p = mix_pose(hp, base, smooth((t_ms - imp) / rec))
    if clip.key == "hit.knockback":
        hop = math.sin(math.pi * min(1.0, t_ms / (imp + 0.5 * rec))) if t_ms < imp + 0.5 * rec else 0.0
        p["lift"] = 0.05 * hop
    return p


def pose_knockdown(clip: C.ClipDef, t_ms: float) -> Pose:
    (_, fall), (_, lie) = clip.phases
    start = _fall_start(clip)
    if t_ms < fall:
        return mix_pose(start, LYING_BACK, ease_in(t_ms / fall))
    return mix_pose(LYING_BACK, LYING_BACK_SETTLE, smooth((t_ms - fall) / lie))


def pose_getup(clip: C.ClipDef, t_ms: float) -> Pose:
    u = t_ms / clip.total_ms
    if u < 0.5:
        return mix_pose(LYING_BACK_SETTLE, CROUCH, smooth(u / 0.5))
    return mix_pose(CROUCH, combat(None), smooth((u - 0.5) / 0.5))


def pose_death(clip: C.ClipDef, t_ms: float) -> Pose:
    (_, fall), (_, lie) = clip.phases
    start = _fall_start(clip)
    if t_ms < fall:
        return mix_pose(start, LYING_FRONT, ease_in(t_ms / fall))
    return mix_pose(LYING_FRONT, LYING_FRONT_SETTLE, smooth((t_ms - fall) / lie))


# --------------------------------------------------------------------------
# 跳跃、施法、闪避
# --------------------------------------------------------------------------

def pose_jump(clip: C.ClipDef, t_ms: float) -> Pose:
    (_, tk), (_, air), (_, ld) = clip.phases
    base = peace(None)
    crouch = _with(base, m_hf=40, m_kn=70, o_hf=35, o_kn=65, t_pitch=18, m_sf=-30, o_sf=-30, m_el=20, o_el=20)
    tuck = _with(base, m_hf=42, m_kn=72, o_hf=30, o_kn=58, t_pitch=6, m_sf=110, o_sf=110, m_sa=20, o_sa=20,
                 m_el=30, o_el=30)
    if t_ms < tk:
        return mix_pose(base, crouch, smooth(t_ms / tk))
    if t_ms < tk + air:
        u = (t_ms - tk) / air
        p = mix_pose(crouch, tuck, smooth(min(1.0, u * 2.0)))
        if u > 0.6:
            p = mix_pose(tuck, _with(base, m_hf=14, m_kn=20, o_hf=10, o_kn=16, m_sf=60, o_sf=60),
                         smooth((u - 0.6) / 0.4))
        p["lift"] = 0.34 * math.sin(math.pi * u) * C.mass_air(clip)
        return p
    u = (t_ms - tk - air) / ld
    land = _with(base, m_hf=40, m_kn=75, o_hf=35, o_kn=70, t_pitch=20, m_sf=20, o_sf=20)
    return mix_pose(land, base, smooth(u))


def pose_cast(clip: C.ClipDef, t_ms: float) -> Pose:
    (_, w), (_, rl), (_, rc) = clip.phases
    base = peace(None)
    wind = _with(base, m_sf=140, o_sf=140, m_el=50, o_el=50, m_sa=22, o_sa=22, t_pitch=-6)
    rel = _with(base, m_sf=95, o_sf=95, m_el=5, o_el=5, t_pitch=14, m_hf=24, m_kn=14, o_hf=-12)
    if t_ms < w:
        return mix_pose(base, wind, smooth(t_ms / w))
    if t_ms < w + rl:
        return mix_pose(wind, rel, ease_in((t_ms - w) / rl))
    return mix_pose(rel, base, smooth((t_ms - w - rl) / rc))


def pose_dodge(clip: C.ClipDef, t_ms: float) -> Pose:
    (_, st), (_, mo), (_, rc) = clip.phases
    base = peace(None)
    crouch = _with(base, t_pitch=25, m_hf=30, m_kn=45, o_hf=-20, o_kn=20, m_sf=-20, o_sf=-20, m_el=20, o_el=20)
    dash = _with(base, t_pitch=40, m_hf=48, m_kn=55, o_hf=-38, o_kn=15, m_sf=-55, o_sf=-55, m_el=25, o_el=25, h_pitch=-20)
    if t_ms < st:
        return mix_pose(base, crouch, smooth(t_ms / st))
    if t_ms < st + mo:
        return mix_pose(crouch, dash, smooth((t_ms - st) / mo))
    return mix_pose(dash, base, smooth((t_ms - st - mo) / rc))


# --------------------------------------------------------------------------
# 手感落地 M3-D 追加：启停过渡、抛飞、眩晕、格挡
# --------------------------------------------------------------------------

def _wsfx(clip: C.ClipDef) -> str:
    """启停过渡的相邻循环剪辑键后缀：带伤变体的过渡接带伤的待机/奔跑（首尾姿势与它们衔接）。"""
    return ".wounded" if clip.variant == "wounded" else ""


def pose_move_start(clip: C.ClipDef, t_ms: float) -> Pose:
    """起步：从待机（循环起点）前倾迈出第一步，终点 = move.run 循环起点（接触姿势），首尾与前后循环剪辑衔接。"""
    u = t_ms / clip.total_ms
    w = _wsfx(clip)
    a, b = pose_idle(C.clip_by_key("idle" + w), 0.0), pose_loco(C.clip_by_key("move.run" + w), 0.0)
    p = mix_pose(a, b, smooth(u))
    p["t_pitch"] += 8.0 * math.sin(math.pi * u)
    p["lift"] = 0.02 * math.sin(math.pi * u)
    return p


def pose_move_stop(clip: C.ClipDef, t_ms: float) -> Pose:
    """急停：从奔跑接触姿势刹车后仰，站稳回到待机起点。"""
    u = t_ms / clip.total_ms
    w = _wsfx(clip)
    a, b = pose_loco(C.clip_by_key("move.run" + w), 0.0), pose_idle(C.clip_by_key("idle" + w), 0.0)
    p = mix_pose(a, b, smooth(u))
    k = math.sin(math.pi * u)
    p["t_pitch"] -= 12.0 * k
    p["m_kn"] += 22.0 * k
    p["o_kn"] += 22.0 * k
    return p


def pose_move_pivot(clip: C.ClipDef, t_ms: float) -> Pose:
    """急转（方向反转）：奔跑接触姿势 -> 滑步屈膝后仰拧身 -> 回到奔跑接触姿势。"""
    u = t_ms / clip.total_ms
    run0 = pose_loco(C.clip_by_key("move.run" + _wsfx(clip)), 0.0)
    skid = _with(run0, m_hf=32, o_hf=-34, m_kn=48, o_kn=42, t_pitch=-10, t_yaw=0, h_yaw=-28, t_roll=-6,
                 m_sf=-10, o_sf=60, m_el=60, o_el=60)
    k = math.sin(math.pi * u)
    p = mix_pose(run0, skid, smooth(k))
    p["t_yaw"] = run0["t_yaw"] + 38.0 * k
    return p


def pose_launch(clip: C.ClipDef, t_ms: float) -> Pose:
    """抛飞：重击后仰 -> 滞空（抬升弧线，身体放平）-> 落地滑入躺姿（之后接 hit.getup 从躺姿起身）。"""
    (_, imp), (_, air), (_, land) = clip.phases
    base = combat(None)
    start = _fall_start(clip, LAUNCH_PELVIS_TILT)
    if t_ms < imp:
        return mix_pose(base, start, ease_out(t_ms / imp))
    if t_ms < imp + air:
        u = (t_ms - imp) / air
        p = mix_pose(start, LYING_BACK, smooth(u))
        p["lift"] = 0.45 * math.sin(math.pi * u) * C.mass_air(clip)
        return p
    return mix_pose(LYING_BACK, LYING_BACK_SETTLE, smooth((t_ms - imp - air) / land))


def pose_stunned(clip: C.ClipDef, t_ms: float) -> Pose:
    """眩晕：站立晃动（躯干侧倾与头部画圈，循环），双臂松垂。"""
    phi = (t_ms / clip.total_ms) % 1.0
    sn, cs = math.sin(2 * math.pi * phi), math.cos(2 * math.pi * phi)
    p = make_pose(m_sf=12, o_sf=10, m_sa=22, o_sa=22, m_el=28, o_el=24, m_hf=6, o_hf=-4,
                  m_kn=18 + 4 * sn, o_kn=16 - 4 * sn)
    p["t_roll"] = 7.0 * sn
    p["t_pitch"] = 9.0 + 3.0 * cs
    p["h_pitch"] = 12.0 + 6.0 * cs
    p["h_yaw"] = 16.0 * sn
    return p


def pose_block(clip: C.ClipDef, t_ms: float) -> Pose:
    """格挡：蹲稳，双前臂交叉举在身前（盾族：盾牌举在脸前、主手短剑收后），轻微受力颤动（循环）。"""
    phi = (t_ms / clip.total_ms) % 1.0
    sn = math.sin(2 * math.pi * phi)
    if clip.family == "shield":
        p = _with(combat("shield"), o_sf=84, o_sa=2, o_el=105, m_sf=40, m_el=100, wp=100, t_pitch=10, m_kn=34, o_kn=30)
    else:
        p = _with(combat(None), m_sf=76, m_sa=-14, m_el=128, o_sf=76, o_sa=-14, o_el=128, t_pitch=10, m_kn=34, o_kn=30)
    p["t_pitch"] += 0.8 * sn
    p["m_kn"] += 1.5 * sn
    p["o_kn"] += 1.5 * sn
    return p


# --------------------------------------------------------------------------
# 手感落地 M4-D 追加：空中姿势、格挡受击、眩晕摇晃、击飞翻滚与落地缓冲
# --------------------------------------------------------------------------

def _fall_pose(phi: float) -> Pose:
    """下落姿势（jump.fall 的循环取样，也是 hit.air / jump.land / attack.air 收尾的衔接点）：展臂、下肢自然下垂略晃。"""
    s, c = math.sin(2 * math.pi * phi), math.cos(2 * math.pi * phi)
    return _with(peace(None), m_hf=14 + 5 * s, m_kn=24 + 6 * c, o_hf=8 - 5 * s, o_kn=20 - 6 * c, m_sf=72 + 8 * s,
                 o_sf=76 - 8 * s, m_sa=46 + 5 * c, o_sa=48 - 5 * c, m_el=28 + 6 * c, o_el=30 - 6 * c,
                 t_pitch=-3 + 1.5 * s, h_pitch=-5)


def pose_jump_rise(clip: C.ClipDef, t_ms: float) -> Pose:
    """上升：下肢收起、双臂上扬，轻微起伏的循环保持姿势（滞空时长由逻辑竖直轴决定，剪辑不带抬升曲线）。"""
    phi = (t_ms / clip.total_ms) % 1.0
    s, c = math.sin(2 * math.pi * phi), math.cos(2 * math.pi * phi)
    return _with(peace(None), m_hf=40 + 4 * s, m_kn=68 + 5 * c, o_hf=20 - 4 * s, o_kn=50 - 5 * c, m_sf=122 + 7 * s,
                 o_sf=116 - 7 * s, m_sa=22 + 4 * c, o_sa=24 - 4 * c, m_el=34, o_el=40, t_pitch=4 + 1.2 * s, h_pitch=-4)


def pose_jump_fall(clip: C.ClipDef, t_ms: float) -> Pose:
    return _fall_pose((t_ms / clip.total_ms) % 1.0)


def pose_jump_land(clip: C.ClipDef, t_ms: float) -> Pose:
    """落地：从下落姿势吸收冲击（深蹲前倾）-> 起身回到站姿。起点 = jump.fall 起点姿势。"""
    (_, imp), (_, rec) = clip.phases
    base = peace(None)
    absorb = _with(base, m_hf=48, m_kn=88, o_hf=44, o_kn=84, t_pitch=26, m_sf=24, o_sf=24, m_el=40, o_el=40, h_pitch=-8)
    if t_ms < imp:
        return mix_pose(_fall_pose(0.0), absorb, ease_out(t_ms / imp))
    return mix_pose(absorb, base, smooth((t_ms - imp) / rec))


def pose_hit_air(clip: C.ClipDef, t_ms: float) -> Pose:
    """空中受击：下落姿势后仰、双臂甩开、下肢拖后（幅度随体量反应倍率），再回到下落姿势（接 jump.fall 循环）。"""
    (_, imp), (_, rec) = clip.phases
    start = _fall_pose(0.0)
    hit = _with(start, t_pitch=-24, h_pitch=-22, m_sf=-8, o_sf=-12, m_sa=54, o_sa=54, m_el=20, o_el=24,
                m_hf=-12, m_kn=40, o_hf=-20, o_kn=24, t_roll=8)
    target = scale_pose(start, hit, C.mass_react(clip))
    if t_ms < imp:
        return mix_pose(start, target, ease_out(t_ms / imp))
    return mix_pose(target, start, smooth((t_ms - imp) / rec))


_AIR_ATTACK_LEGS = (dict(m_hf=30, m_kn=58, o_hf=14, o_kn=42), dict(m_hf=36, m_kn=64, o_hf=20, o_kn=50),
                    dict(m_hf=48, m_kn=28, o_hf=-8, o_kn=34))


def pose_attack_air(clip: C.ClipDef, t_ms: float) -> Pose:
    """空中攻击：同族地面第一段的上身关键姿势，下肢换成收起（前摇收得更紧、判定相蹬出）。"""
    keys = tuple(_with(k, **legs) for k, legs in zip(attack_keys(clip.family, 1), _AIR_ATTACK_LEGS))
    return three_phase(clip, t_ms, keys)


def pose_hit_block(clip: C.ClipDef, t_ms: float) -> Pose:
    """格挡受击：从格挡持握后仰、屈膝撑住，再以阻尼余弦抖动（包络 (1-u)^2，两个半周期）收回持握——末帧 = block 循环起点。"""
    (_, imp), (_, rec) = clip.phases
    hold = pose_block(C.clip_by_key("block.shield" if clip.family == "shield" else "block"), 0.0)
    recoil = _with(hold, t_pitch=hold["t_pitch"] - 9, h_pitch=hold["h_pitch"] - 6, m_sf=hold["m_sf"] - 14,
                   o_sf=hold["o_sf"] - 10, m_kn=hold["m_kn"] + 10, o_kn=hold["o_kn"] + 8, m_hf=hold["m_hf"] - 4)
    target = scale_pose(hold, recoil, C.mass_react(clip))
    if t_ms < imp:
        return mix_pose(hold, target, ease_out(t_ms / imp))
    u = (t_ms - imp) / rec
    return mix_pose(hold, target, (1.0 - u) ** 2 * math.cos(2 * math.pi * 2.5 * u))


def pose_stunned_sway(clip: C.ClipDef, t_ms: float) -> Pose:
    """眩晕摇晃（循环）：躯干大幅侧倾、头部画圈、双膝交替屈伸（踉跄落脚）、双臂松垂反向摆动、重心左右漂移。"""
    phi = (t_ms / clip.total_ms) % 1.0
    sn, cs = math.sin(2 * math.pi * phi), math.cos(2 * math.pi * phi)
    sn2 = math.sin(4 * math.pi * phi)
    p = make_pose(m_hf=7 * sn, o_hf=-7 * sn, m_kn=22 + 12 * max(0.0, sn), o_kn=22 + 12 * max(0.0, -sn),
                  m_sf=14 + 18 * cs, o_sf=14 - 18 * cs, m_sa=26 + 6 * sn, o_sa=26 - 6 * sn, m_el=30, o_el=28)
    p["t_roll"] = 11.0 * sn
    p["t_pitch"] = 8.0 + 4.0 * sn2
    p["h_pitch"] = 14.0 + 7.0 * cs
    p["h_yaw"] = 26.0 * math.sin(2 * math.pi * phi + 0.8)
    p["px"] = 0.03 * sn
    return p


#: 击飞翻滚的蜷身姿势（抱膝）与骨盆抬升（绕骨盆翻转时身体最低点仍在地面之上）。
_TUMBLE_CURL = dict(t_pitch=34, h_pitch=22, m_hf=72, m_kn=104, o_hf=64, o_kn=98, m_sf=62, o_sf=58, m_sa=14, o_sa=14,
                    m_el=104, o_el=98)
TUMBLE_LIFT = 0.10


def _tumble_pose(phi: float) -> Pose:
    p = make_pose(**_TUMBLE_CURL)
    p["bp"] = -360.0 * phi
    p["ground"] = 0.0
    p["lift"] = TUMBLE_LIFT
    return p


def pose_launch_tumble(clip: C.ClipDef, t_ms: float) -> Pose:
    """击飞翻滚（循环）：蜷身向后翻转一整圈（整身俯仰 bp 0 -> -360 度，绕骨盆；不接地求解，骨盆定高）。"""
    return _tumble_pose((t_ms / clip.total_ms) % 1.0)


def _lying_contact_lift() -> float:
    """躺姿（LYING_BACK）着地求解的偏移 = 骨盆在躺姿触地时的抬升：翻滚落地的第一相用它从空中抬升收到触地，与第二相（着地求解）连续。"""
    parts, _j = build_parts(LYING_BACK, None)
    return solve_ground(LYING_BACK, parts)


def pose_launch_land(clip: C.ClipDef, t_ms: float) -> Pose:
    """击飞落地缓冲：蜷身下坠触地（impact，不接地求解，骨盆抬升收到触地值）-> 弹起一下（bounce）-> 滑入躺姿（settle，终点 = hit.getup 起点）。"""
    (_, imp), (_, bnc), (_, stl) = clip.phases
    if t_ms < imp:
        e = ease_in(t_ms / imp)
        p = mix_pose(_tumble_pose(0.0), LYING_BACK, e)
        p["ground"] = 0.0
        p["lift"] = TUMBLE_LIFT + (_lying_contact_lift() - TUMBLE_LIFT) * e
        return p
    if t_ms < imp + bnc:
        u = (t_ms - imp) / bnc
        p = dict(LYING_BACK)
        p["lift"] = 0.07 * math.sin(math.pi * u)
        p["m_sf"] = LYING_BACK["m_sf"] + 14.0 * math.sin(math.pi * u)
        p["o_sf"] = LYING_BACK["o_sf"] + 14.0 * math.sin(math.pi * u)
        return p
    return mix_pose(LYING_BACK, LYING_BACK_SETTLE, smooth((t_ms - imp - bnc) / stl))


_DISPATCH = {
    "move_start": pose_move_start, "move_stop": pose_move_stop, "move_pivot": pose_move_pivot,
    "hit.launch": pose_launch, "stunned": pose_stunned, "block": pose_block, "sprint": pose_loco,
    "idle": pose_idle, "walk": pose_loco, "run": pose_loco, "attack": pose_attack,
    "hit": pose_hit, "hit.light": pose_hit, "hit.heavy": pose_hit, "hit.knockback": pose_hit,
    "hit.knockdown": pose_knockdown, "hit.getup": pose_getup, "death": pose_death,
    "jump": pose_jump, "cast": pose_cast, "dodge": pose_dodge,
    "jump_rise": pose_jump_rise, "jump_fall": pose_jump_fall, "jump_land": pose_jump_land, "hit_air": pose_hit_air,
    "attack_air": pose_attack_air, "hit_block": pose_hit_block, "stunned_sway": pose_stunned_sway,
    "launch_tumble": pose_launch_tumble, "launch_land": pose_launch_land,
}


def apply_mass(clip: C.ClipDef, pose: Pose) -> Pose:
    """体量档的静态站姿偏移（config.MASS_TIERS）：躯干/头俯仰、肩/髋外展，左右同号。主集（mass=None）不调用，输出与此前逐位一致。"""
    prof = C.MASS_TIERS[clip.mass]
    return dict(pose, t_pitch=pose["t_pitch"] + prof["t_pitch"], h_pitch=pose["h_pitch"] + prof["h_pitch"],
                m_sa=pose["m_sa"] + prof["sa"], o_sa=pose["o_sa"] + prof["sa"],
                m_ha=pose["m_ha"] + prof["ha"], o_ha=pose["o_ha"] + prof["ha"])


def pose_at(clip: C.ClipDef, t_ms: float) -> Pose:
    p = _DISPATCH[clip.pose_id](clip, t_ms)
    return apply_mass(clip, p) if clip.mass else p
