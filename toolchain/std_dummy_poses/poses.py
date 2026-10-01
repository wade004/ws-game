"""姿势库：剪辑 + 时间点 -> 关节角姿势。全部是关键姿势插值或正弦循环，无随机数。

动作质量定位：几何人偶的"可读"占位（能区分走/跑/各武器的攻击/受击/倒地），不是美术。美术按 04 §4 逐键替换。
"""

from __future__ import annotations

import math

from . import config as C
from .skeleton import Pose, make_pose, mix_pose, scale_pose


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

def peace(fam: str | None) -> Pose:
    """非战斗站姿（腿直立）。"""
    p = make_pose(m_sf=4, m_sa=6, m_el=12, o_sf=-4, o_sa=6, o_el=12)
    if fam == "1h":
        p = _with(p, m_sf=22, m_sa=8, m_el=55, wp=40)
    elif fam == "2h":
        p = _with(p, m_sf=40, m_sa=-8, m_el=105, o_sf=32, o_sa=-28, o_el=100, wp=190)
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
    return p


def pose_loco(clip: C.ClipDef, t_ms: float) -> Pose:
    gait = clip.gait
    phi = (t_ms / clip.total_ms) % 1.0
    cs = math.cos(2 * math.pi * phi)
    sn = math.sin(2 * math.pi * phi)
    step = C.step_displacement_bh(gait)
    amp = math.degrees(math.asin(step / (2.0 * C.LEG_LENGTH)))
    run = gait == "run"
    k_swing = 95.0 if run else 40.0
    k_stance = 12.0 if run else 4.0
    base = _base_for(clip)
    p = dict(base)
    p["m_hf"] = amp * cs
    p["o_hf"] = -amp * cs
    p["m_kn"] = k_stance + k_swing * max(0.0, -sn)
    p["o_kn"] = k_stance + k_swing * max(0.0, sn)
    p["t_pitch"] = (10.0 if run else 3.0) + (4.0 if clip.combat else 0.0)
    p["t_yaw"] = (9.0 if run else 5.0) * cs
    swing = 55.0 if run else 22.0
    held_main = clip.family in C.FAMILIES_WITH_WEAPON
    held_off = clip.family == "2h"
    if clip.combat:
        swing *= 0.35
    if not held_main:
        p["m_sf"] = base["m_sf"] - swing * cs
        p["m_el"] = (85.0 if run else 15.0) if not clip.combat else base["m_el"]
    if not held_off:
        p["o_sf"] = base["o_sf"] + swing * cs
        p["o_el"] = (85.0 if run else 15.0) if not clip.combat else base["o_el"]
    if run:
        p["lift"] = 0.035 * abs(sn)
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
    # 2h
    if seg == 1:
        return s, _with(s, m_sf=165, o_sf=165, m_el=45, o_el=45, wp=215, t_pitch=-8), \
            _with(s, m_sf=62, o_sf=62, m_el=10, o_el=10, wp=82, t_pitch=18, m_hf=34, m_kn=28, o_hf=-20)
    return s, _with(s, m_sf=88, o_sf=88, m_sa=50, o_sa=-10, m_el=30, o_el=30, wp=90, wy=70, t_yaw=40), \
        _with(s, m_sf=88, o_sf=88, m_sa=15, o_sa=-15, m_el=15, o_el=15, wp=90, wy=-60, t_yaw=-40, t_pitch=8)


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


LYING_BACK = make_pose(t_pitch=-88, h_pitch=-4, m_sf=-12, o_sf=-12, m_sa=20, o_sa=20, m_el=12, o_el=12,
                       m_hf=86, o_hf=82, m_kn=6, o_kn=10)
LYING_BACK_SETTLE = _with(LYING_BACK, m_sa=32, o_sa=34, m_hf=80, o_hf=76, m_kn=14, o_kn=12, h_pitch=-8)
CROUCH = make_pose(t_pitch=-30, m_hf=75, o_hf=70, m_kn=95, o_kn=95, m_sf=30, o_sf=30, m_el=40, o_el=40)
LYING_FRONT = make_pose(t_pitch=88, h_pitch=-30, m_sf=170, o_sf=170, m_sa=14, o_sa=14, m_el=12, o_el=12,
                        m_hf=-85, o_hf=-82, m_kn=5, o_kn=8)
LYING_FRONT_SETTLE = _with(LYING_FRONT, m_sa=26, o_sa=26, h_pitch=-34, m_hf=-80, o_hf=-78)


def pose_hit(clip: C.ClipDef, t_ms: float) -> Pose:
    base = combat(None)
    strength = {"hit": 1.0, "hit.light": 0.55, "hit.heavy": 1.5, "hit.knockback": 2.2}[clip.key]
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
    start = _hit_pose(combat(None), 1.5)
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
    start = _hit_pose(combat(None), 1.5)
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
        p["lift"] = 0.34 * math.sin(math.pi * u)
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


_DISPATCH = {
    "idle": pose_idle, "walk": pose_loco, "run": pose_loco, "attack": pose_attack,
    "hit": pose_hit, "hit.light": pose_hit, "hit.heavy": pose_hit, "hit.knockback": pose_hit,
    "hit.knockdown": pose_knockdown, "hit.getup": pose_getup, "death": pose_death,
    "jump": pose_jump, "cast": pose_cast, "dodge": pose_dodge,
}


def pose_at(clip: C.ClipDef, t_ms: float) -> Pose:
    return _DISPATCH[clip.pose_id](clip, t_ms)
