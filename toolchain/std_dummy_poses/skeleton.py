"""几何人偶：关节角姿势 -> 三维块体 -> 按偏航角正交投影 -> 光栅化（Pillow 多边形）。

坐标（身高 = 1.0）：x 轴指向"主手侧"，y 向上，z 为人物前方。观察偏航角 yaw=0 时人物面向镜头
（主手侧在画面左侧，即人物自己的右手），yaw=90 时面向画面右侧（主手侧靠近镜头），yaw=180 为背向。
投影：屏幕 x = -x·cosθ + z·sinθ，深度 d = z·cosθ + x·sinθ（越大越靠近镜头）。

全部确定性：无随机数、无字典枚举顺序依赖（零件按固定列表顺序生成，排序键含序号）。
"""

from __future__ import annotations

import math
from dataclasses import dataclass

from PIL import Image, ImageDraw

from . import config as C

POSE_KEYS = (
    "px", "py", "pz", "lift", "ground",
    "yaw", "t_yaw", "t_pitch", "t_roll", "h_pitch", "h_yaw",
    "m_sf", "m_sa", "m_el", "o_sf", "o_sa", "o_el",
    "m_hf", "m_ha", "m_kn", "o_hf", "o_ha", "o_kn",
    "wp", "wz", "wy",
)
_DEFAULTS = {k: 0.0 for k in POSE_KEYS}
_DEFAULTS["ground"] = 1.0

Pose = dict


def make_pose(**kw) -> Pose:
    p = dict(_DEFAULTS)
    for k, v in kw.items():
        if k not in p:
            raise KeyError(f"未知姿势参数 {k}")
        p[k] = float(v)
    return p


def mix_pose(a: Pose, b: Pose, u: float) -> Pose:
    return {k: a[k] + (b[k] - a[k]) * u for k in POSE_KEYS}


def scale_pose(base: Pose, target: Pose, k: float) -> Pose:
    """从 base 向 target 偏移 k 倍（k>1 外推，用于受击强弱）。"""
    return {key: base[key] + (target[key] - base[key]) * k for key in POSE_KEYS}


# --------------------------------------------------------------------------
# 向量运算
# --------------------------------------------------------------------------

def _rx(deg, v):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    x, y, z = v
    return (x, y * c - z * s, y * s + z * c)


def _ry(deg, v):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    x, y, z = v
    return (x * c + z * s, y, -x * s + z * c)


def _rz(deg, v):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    x, y, z = v
    return (x * c - y * s, x * s + y * c, z)


def _add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def _mul(v, k):
    return (v[0] * k, v[1] * k, v[2] * k)


# --------------------------------------------------------------------------
# 零件
# --------------------------------------------------------------------------

@dataclass
class Part:
    name: str
    layer: str           # body / weapon
    corners: list        # 世界（人物）坐标下 8 个角点
    tone: int            # 基础灰度


def _box(center_a, center_b, half_x, half_z, frame_x, frame_z, half_y=0.0):
    """两端中心 + 横截面半宽/半深 -> 角点。frame_* 是已旋转到世界的单位轴。

    half_y > 0 时额外沿世界竖直方向加 ±half_y（用于肢体/武器：水平放置时侧视也保有厚度，
    否则横截面偏移落在视线方向，投影退化成一条线）。"""
    pts = []
    ys = (-1, 1) if half_y > 0 else (0,)
    for c in (center_a, center_b):
        for sx in (-1, 1):
            for sz in (-1, 1):
                for sy in ys:
                    pts.append(_add(_add(_add(c, _mul(frame_x, sx * half_x)), _mul(frame_z, sz * half_z)),
                                    (0.0, sy * half_y, 0.0)))
    return pts


def build_parts(pose: Pose, weapon: str | None) -> tuple[list[Part], tuple]:
    """返回 (零件列表, 关节字典 {ankle_m, ankle_o, hand_m, hand_o})。不含地面对齐（由 solve_ground 处理）。"""
    g = pose
    P = (g["px"], C.REST_HIP_Y + g["py"], g["pz"])

    def Rb(v):
        return _ry(g["yaw"], v)

    def Rt(v):
        return _ry(g["yaw"] + g["t_yaw"], _rz(g["t_roll"], _rx(g["t_pitch"], v)))

    def Rh(v):
        return Rt(_ry(g["h_yaw"], _rx(g["h_pitch"], v)))

    bx, bz = Rb((1, 0, 0)), Rb((0, 0, 1))
    tx, tz = Rt((1, 0, 0)), Rt((0, 0, 1))
    hx, hz = Rh((1, 0, 0)), Rh((0, 0, 1))

    parts: list[Part] = []

    # 骨盆与躯干
    parts.append(Part("pelvis", "body", _box(_add(P, (0, -0.045, 0)), _add(P, (0, 0.045, 0)), 0.09, 0.065, bx, bz), 120))
    chest_lo = _add(P, Rt((0, 0.03, 0)))
    chest_hi = _add(P, Rt((0, 0.31, 0)))
    torso = []
    for end, hw in ((chest_lo, 0.10), (chest_hi, 0.145)):
        for sx in (-1, 1):
            for sz in (-1, 1):
                torso.append(_add(_add(end, _mul(tx, sx * hw)), _mul(tz, sz * 0.07)))
    parts.append(Part("torso", "body", torso, 165))

    # 头与"鼻"（鼻是朝向标记，背向时被头遮住）
    head_c = _add(P, Rt((0, 0.395, 0)))
    parts.append(Part("head", "body", _box(_add(head_c, _mul(Rh((0, 1, 0)), -0.085)), _add(head_c, _mul(Rh((0, 1, 0)), 0.085)),
                                           0.078, 0.078, hx, hz), 205))
    nose_c = _add(head_c, _add(_mul(hz, 0.085), _mul(Rh((0, 1, 0)), -0.008)))
    parts.append(Part("nose", "body", _box(_add(nose_c, (0, -0.012, 0)), _add(nose_c, (0, 0.012, 0)), 0.018, 0.016, hx, hz), 240))

    # 腿
    hands = {}
    ankles = {}
    shoulders = {}
    for side, s in (("m", 1), ("o", -1)):
        hip = _add(P, Rb((s * 0.075, 0, 0)))
        th_local = _rz(s * g[f"{side}_ha"], (0, -math.cos(math.radians(g[f"{side}_hf"])), math.sin(math.radians(g[f"{side}_hf"]))))
        knee = _add(hip, _mul(Rb(th_local), C.THIGH))
        sh_local = _rx(g[f"{side}_kn"], th_local)
        ankle = _add(knee, _mul(Rb(sh_local), C.SHIN))
        ankles[side] = ankle
        tone_t, tone_s = (108, 92) if side == "m" else (96, 82)
        parts.append(Part(f"thigh_{side}", "body", _box(hip, knee, 0.048, 0.052, bx, bz, 0.04), tone_t))
        parts.append(Part(f"shin_{side}", "body", _box(knee, ankle, 0.036, 0.04, bx, bz, 0.03), tone_s))
        foot_c = _add(ankle, Rb((0, -0.02, 0.04)))
        parts.append(Part(f"foot_{side}", "body", _box(_add(foot_c, (0, -0.02, 0)), _add(foot_c, (0, 0.02, 0)), 0.036, 0.075, bx, bz), 70))

    # 臂
    forearm_dirs = {}
    elbows = {}
    for side, s in (("m", 1), ("o", -1)):
        sh = _add(P, Rt((s * 0.145, 0.27, 0)))
        shoulders[side] = sh
        sf, sa, el = g[f"{side}_sf"], g[f"{side}_sa"], g[f"{side}_el"]
        ua_local = _rz(s * sa, (0, -math.cos(math.radians(sf)), math.sin(math.radians(sf))))
        elbow = _add(sh, _mul(Rt(ua_local), C.UPPER_ARM))
        fa_local = _rx(-el, ua_local)
        forearm_dirs[side] = Rt(fa_local)
        elbows[side] = elbow
        wrist = _add(elbow, _mul(Rt(fa_local), C.FOREARM))
        tone_a, tone_f = (150, 175) if side == "m" else (135, 160)
        parts.append(Part(f"upper_arm_{side}", "body", _box(sh, elbow, 0.036, 0.036, tx, tz, 0.03), tone_a))
        parts.append(Part(f"forearm_{side}", "body", _box(elbow, wrist, 0.03, 0.03, tx, tz, 0.026), tone_f))
        hand = _add(wrist, _mul(Rt(fa_local), 0.025))
        parts.append(Part(f"hand_{side}", "body", _box(_add(hand, (0, -0.025, 0)), _add(hand, (0, 0.025, 0)), 0.03, 0.03, tx, tz), 190))
        hands[side] = hand

    # 手感落地 M3-D 追加的武器族（长柄/弓/法杖/双持/盾族）：几何见 _extra_weapon_parts。1h/2h 走下面既有分支，产物不变。
    if weapon in C.EXTRA_FAMILIES:
        d_local = _ry(g["wy"], _rz(g["wz"], (0, -math.cos(math.radians(g["wp"])), math.sin(math.radians(g["wp"])))))
        parts += _extra_weapon_parts(weapon, Rt(d_local), hands, forearm_dirs, elbows, tx, tz, Rt((0, 1, 0)))
    # 武器（挂主手）
    elif weapon:
        d_local = _ry(g["wy"], _rz(g["wz"], (0, -math.cos(math.radians(g["wp"])), math.sin(math.radians(g["wp"])))))
        d = Rt(d_local)
        h = hands["m"]
        length = C.WEAPON_LENGTH[weapon]
        sec = C.WEAPON_SECTION[weapon]
        grip_a, grip_b = _add(h, _mul(d, -0.05)), _add(h, _mul(d, 0.035))
        parts.append(Part("grip", "weapon", _box(grip_a, grip_b, 0.02, 0.02, tx, tz, 0.02), 95))
        guard_c = _add(h, _mul(d, 0.04))
        guard_w = 0.05 if weapon == "1h" else 0.075
        parts.append(Part("guard", "weapon", _box(_add(guard_c, _mul(d, -0.008)), _add(guard_c, _mul(d, 0.008)), guard_w, 0.02, tx, tz, 0.02), 125))
        blade_a, blade_b = _add(h, _mul(d, 0.05)), _add(h, _mul(d, 0.05 + length))
        parts.append(Part("blade", "weapon", _box(blade_a, blade_b, sec, sec, tx, tz, sec), 225))
    # 武器方向（主手武器沿它放置）与躯干/头部朝向基，供装备资产生成器按同一套姿势摆放装备零件
    # （toolchain/std_equip_set）。纯新增键，既有调用方只取 ankle_*/hand_*。
    wdir = Rt(_ry(g["wy"], _rz(g["wz"], (0, -math.cos(math.radians(g["wp"])), math.sin(math.radians(g["wp"]))))))
    return parts, {"ankle_m": ankles["m"], "ankle_o": ankles["o"], "hand_m": hands["m"], "hand_o": hands["o"],
                   "shoulder_m": shoulders["m"], "shoulder_o": shoulders["o"], "pelvis": P,
                   "chest_lo": chest_lo, "chest_hi": chest_hi, "tx": tx, "tz": tz, "hx": hx, "hz": hz,
                   "head_center": head_c, "weapon_dir": wdir}


def _unit(v):
    n = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])
    return (v[0] / n, v[1] / n, v[2] / n) if n > 1e-9 else (0.0, 0.0, 1.0)


def _extra_weapon_parts(weapon: str, d, hands, fdirs, elbows, tx, tz, up) -> list[Part]:
    """手感落地 M3-D：polearm/bow/staff/dual/shield 的占位几何（全部沿用 ``_box``，零件层名 weapon）。

    d 是主手武器方向（与 1h/2h 同一约定：pose 的 wp/wy/wz）；副手物品（双持匕首、盾牌）沿副手前臂放置，所以不新增姿势参数，
    model 型的 ``socket.off_hand`` 也恒挂在前臂末端，两版同一约定。"""
    h = hands["m"]
    sec = C.WEAPON_SECTION[weapon]
    out: list[Part] = []
    if weapon == "polearm":
        out.append(Part("shaft", "weapon", _box(_add(h, _mul(d, -0.25)), _add(h, _mul(d, 0.55)), sec, sec, tx, tz, sec), 110))
        out.append(Part("spearhead", "weapon", _box(_add(h, _mul(d, 0.55)), _add(h, _mul(d, 0.68)), 0.04, 0.014, tx, tz, 0.014), 225))
    elif weapon == "staff":
        out.append(Part("shaft", "weapon", _box(_add(h, _mul(d, -0.2)), _add(h, _mul(d, 0.55)), sec, sec, tx, tz, sec), 105))
        orb = _add(h, _mul(d, 0.6))
        out.append(Part("orb", "weapon", _box(_add(orb, _mul(d, -0.04)), _add(orb, _mul(d, 0.04)), 0.045, 0.045, tx, tz, 0.045), 230))
    elif weapon == "bow":
        # 弓臂对称于握把：沿 d 上下各半长，两端向射手方向（-c）弯，弓弦连两端，拉弦的副手靠近弦中点时弦被拉向副手。
        c = _unit(_add(tz, _mul(d, -(tz[0] * d[0] + tz[1] * d[1] + tz[2] * d[2]))))
        half = C.WEAPON_LENGTH["bow"]
        bend = 0.07
        up_tip = _add(_add(h, _mul(d, half)), _mul(c, -bend))
        lo_tip = _add(_add(h, _mul(d, -half)), _mul(c, -bend))
        out.append(Part("limb_up", "weapon", _box(h, up_tip, sec, sec, tx, tz, sec), 120))
        out.append(Part("limb_lo", "weapon", _box(h, lo_tip, sec, sec, tx, tz, sec), 120))
        mid = _mul(_add(up_tip, lo_tip), 0.5)
        nock = hands["o"] if math.dist(hands["o"], mid) < 0.25 else mid
        out.append(Part("string_up", "weapon", _box(up_tip, nock, 0.006, 0.006, tx, tz, 0.006), 215))
        out.append(Part("string_lo", "weapon", _box(nock, lo_tip, 0.006, 0.006, tx, tz, 0.006), 215))
    else:  # dual / shield：主手短剑（与 1h 同构，长度取该族）
        length = C.WEAPON_LENGTH[weapon]
        out.append(Part("grip", "weapon", _box(_add(h, _mul(d, -0.05)), _add(h, _mul(d, 0.035)), 0.02, 0.02, tx, tz, 0.02), 95))
        gc = _add(h, _mul(d, 0.04))
        out.append(Part("guard", "weapon", _box(_add(gc, _mul(d, -0.008)), _add(gc, _mul(d, 0.008)), 0.045, 0.02, tx, tz, 0.02), 125))
        out.append(Part("blade", "weapon", _box(_add(h, _mul(d, 0.05)), _add(h, _mul(d, 0.05 + length)), sec, sec, tx, tz, sec), 225))
        ho, fo = hands["o"], fdirs["o"]
        if weapon == "dual":
            # 副手匕首沿前臂向前（握把略后伸）
            out.append(Part("off_grip", "weapon", _box(_add(ho, _mul(fo, -0.04)), _add(ho, _mul(fo, 0.03)), 0.018, 0.018, tx, tz, 0.018), 95))
            out.append(Part("off_blade", "weapon", _box(_add(ho, _mul(fo, 0.04)), _add(ho, _mul(fo, 0.04 + 0.24)), sec, sec, tx, tz, sec), 215))
        else:
            # 盾牌：竖直板，贴在副手前臂外侧（朝人物前方），中心在肘与手的中点再向前偏 0.06
            ctr = _add(_mul(_add(elbows["o"], ho), 0.5), _mul(tz, 0.06))
            out.append(Part("shield", "weapon", _box(_add(ctr, _mul(up, -0.14)), _add(ctr, _mul(up, 0.14)), 0.10, 0.018, tx, tz), 150))
    return out


def solve_ground(pose: Pose, parts: list[Part]) -> float:
    """返回需要加到所有点 y 上的偏移：着地姿势让最低点落在 y=0，再叠加 lift。"""
    if pose["ground"] >= 0.5:
        lowest = min(c[1] for p in parts if p.layer == "body" for c in p.corners)
        return -lowest + pose["lift"]
    return pose["lift"]


# --------------------------------------------------------------------------
# 投影与光栅化
# --------------------------------------------------------------------------

def _project(pt, yaw_deg, dy):
    th = math.radians(yaw_deg)
    c, s = math.cos(th), math.sin(th)
    x, y, z = pt
    sx = -x * c + z * s
    d = z * c + x * s
    return (C.ROOT_PX[0] + sx * C.BODY_HEIGHT_PX, C.ROOT_PX[1] - (y + dy) * C.BODY_HEIGHT_PX), d


def _hull(points):
    pts = sorted(set(points))
    if len(pts) <= 2:
        return pts

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower = []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    upper = []
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


OUTLINE = (24, 24, 24, 255)


def render(pose: Pose, view_yaw: float, weapon: str | None, layers: str = "composite",
           clamp_weapon_to_ground: bool = False, item_fn=None) -> Image.Image:
    """layers: composite（全部）/ body（仅身体）/ weapon（仅武器）/ item（仅 item_fn 产出的装备零件）。
    clamp_weapon_to_ground：武器/装备零件低于地面（世界 y < 0）的角点压到地面高度（倒地姿势里剑躺在地上而不是
    扎出画布底边）。只给无族状态剪辑的武器层用，持械族剪辑的输出不受影响。
    item_fn(pose, joints) -> list[Part]：装备资产生成器（toolchain/std_equip_set）传入，产出 layer="item" 的零件，
    地面对齐只看身体零件（装备不影响落地高度）。"""
    parts, _hands = build_parts(pose, weapon)
    dy = solve_ground(pose, parts)
    if item_fn is not None:
        parts = parts + item_fn(pose, _hands)
    if clamp_weapon_to_ground:
        floor = -dy
        parts = [Part(p.name, p.layer, [(c[0], max(c[1], floor), c[2]) for c in p.corners], p.tone)
                 if p.layer in ("weapon", "item") else p for p in parts]
    img = Image.new("RGBA", C.CANVAS, (0, 0, 0, 0))
    dr = ImageDraw.Draw(img)
    ref = 0.0
    projected = []
    for idx, p in enumerate(parts):
        pp = [_project(c, view_yaw, dy) for c in p.corners]
        pts2 = [q for q, _d in pp]
        depth = sum(d for _q, d in pp) / len(pp)
        if p.name == "pelvis":
            ref = depth  # 明暗基准恒取骨盆深度，各层单独渲染与合成渲染的明暗一致
        if layers != "composite" and p.layer != layers:
            continue
        projected.append((depth, idx, p, pts2))
    projected.sort(key=lambda t: (t[0], t[1]))
    for depth, _idx, p, pts2 in projected:
        shade = 1.0 + 0.45 * (depth - ref)
        shade = max(0.72, min(1.18, shade))
        tone = max(0, min(255, int(p.tone * shade)))
        hull = _hull([(round(x), round(y)) for x, y in pts2])
        if len(hull) >= 3:
            dr.polygon(hull, fill=(tone, tone, tone, 255), outline=OUTLINE)
        else:
            dr.line(hull, fill=OUTLINE)
    return img
