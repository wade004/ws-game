"""低多边形武器形体（手感落地 M4-D，04 §10 第 3 条）：长柄、弓、法杖、双持、盾各有可辨认的轮廓。

此前五个新族（polearm/bow/staff/dual/shield）的武器是方块占位。本模块用**凸多面体点集**组成低多边形部件（渲染器对每个部件
取投影凸包，所以部件只需给出凸点集）：叶形枪头、八面体宝珠、带弧度的多段弓臂、尖头剑刃、纹章盾形盾牌 + 盾心凸起。
几何由 sprite 版渲染（``skeleton._extra_weapon_parts`` 调用本模块）与 model 型武器预制体（规格 ``weapon_models``，
由 ``std_dummy_model_clips`` 把同一份部件换成局部坐标网格）共用，是两版武器形体的单一来源。

仍属占位：装备美术可按 ADR-0100 用自己的逐层剪辑 / 装备模型覆盖（本集不是装备资产，只在持械角色缺装备外观时顶替）。

纯向量运算，无随机数、无字典序依赖。点 = (x, y, z) 元组；frame 约定 ``(d, e1, e2)``：d 为武器方向，e1 为横向（取躯干 x 轴在垂直于 d 的
分量），e2 = d × e1（厚度方向）。
"""

from __future__ import annotations

import math

from . import config as C

Vec = tuple[float, float, float]

#: 手感落地 M4-D 重做形体的武器族。
FAMILIES = ("polearm", "bow", "staff", "dual", "shield")


def _add(a: Vec, b: Vec) -> Vec:
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def _mul(v: Vec, k: float) -> Vec:
    return (v[0] * k, v[1] * k, v[2] * k)


def _dot(a: Vec, b: Vec) -> float:
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _cross(a: Vec, b: Vec) -> Vec:
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _unit(v: Vec) -> Vec:
    n = math.sqrt(_dot(v, v))
    return (v[0] / n, v[1] / n, v[2] / n) if n > 1e-9 else (0.0, 0.0, 1.0)


def frame(d: Vec, ref: Vec) -> tuple[Vec, Vec, Vec]:
    """(d, e1, e2)：e1 = ref 去掉沿 d 的分量后归一（ref 与 d 近平行时退回世界 y 轴），e2 = d × e1。"""
    d = _unit(d)
    e1 = (ref[0] - _dot(ref, d) * d[0], ref[1] - _dot(ref, d) * d[1], ref[2] - _dot(ref, d) * d[2])
    if _dot(e1, e1) < 1e-6:
        e1 = (0.0, 1.0 - d[1] * d[1], -d[1] * d[2])
        if _dot(e1, e1) < 1e-6:
            e1 = (1.0, 0.0, 0.0)
    e1 = _unit(e1)
    return d, e1, _unit(_cross(d, e1))


def ring(o: Vec, d: Vec, e1: Vec, e2: Vec, z: float, w: float, t: float) -> list[Vec]:
    """沿 d 方向 z 处的矩形截面（半宽 w 沿 e1、半厚 t 沿 e2）四个角点；w、t 同为 0 时退化为一个点（尖）。"""
    c = _add(o, _mul(d, z))
    if w == 0.0 and t == 0.0:
        return [c]
    return [_add(_add(c, _mul(e1, sx * w)), _mul(e2, sz * t)) for sx in (-1, 1) for sz in (-1, 1)]


def taper(o: Vec, fr: tuple[Vec, Vec, Vec], rings: list[tuple[float, float, float]]) -> list[Vec]:
    """若干截面 (z, 半宽, 半厚) 的凸包点集：逐级收窄 = 尖头剑刃 / 锥形。"""
    d, e1, e2 = fr
    pts: list[Vec] = []
    for z, w, t in rings:
        pts += ring(o, d, e1, e2, z, w, t)
    return pts


def seg(a: Vec, b: Vec, w: float, ref: Vec) -> list[Vec]:
    """a→b 的细杆（方截面，半宽 w），截面朝向取 ref：弓臂分段、法杖叉角。"""
    d, e1, e2 = frame(_add(b, _mul(a, -1.0)), ref)
    pts: list[Vec] = []
    for c in (a, b):
        for sx in (-1, 1):
            for sz in (-1, 1):
                pts.append(_add(_add(c, _mul(e1, sx * w)), _mul(e2, sz * w)))
    return pts


def octahedron(c: Vec, fr: tuple[Vec, Vec, Vec], r_d: float, r_e: float) -> list[Vec]:
    d, e1, e2 = fr
    return [_add(c, _mul(d, r_d)), _add(c, _mul(d, -r_d)), _add(c, _mul(e1, r_e)), _add(c, _mul(e1, -r_e)),
            _add(c, _mul(e2, r_e)), _add(c, _mul(e2, -r_e))]


def polygon_slab(c: Vec, a: Vec, b: Vec, n: Vec, outline: list[tuple[float, float]], half: float) -> list[Vec]:
    """平面多边形厚板：outline 为 (沿 a, 沿 b) 的二维点，沿 n 前后各 half 厚。"""
    pts: list[Vec] = []
    for (u, v) in outline:
        base = _add(_add(c, _mul(a, u)), _mul(b, v))
        pts += [_add(base, _mul(n, half)), _add(base, _mul(n, -half))]
    return pts


Part = tuple[str, list[Vec], int]   # (部件名, 凸点集, 基础灰度)


def sword_parts(h: Vec, fr: tuple[Vec, Vec, Vec], length: float, sec: float, guard_w: float, prefix: str = "") -> list[Part]:
    """尖头短剑：握把 + 护手 + 渐尖的剑刃（85% 处收窄、剑尖为一点）。双持主手 / 盾族主手用。"""
    return [
        (prefix + "grip", taper(h, fr, [(-0.05, 0.02, 0.02), (0.035, 0.02, 0.02)]), 95),
        (prefix + "guard", taper(h, fr, [(0.032, guard_w, 0.02), (0.048, guard_w, 0.02)]), 125),
        (prefix + "blade", taper(h, fr, [(0.05, sec, sec), (0.05 + 0.85 * length, 0.8 * sec, 0.8 * sec),
                                          (0.05 + length, 0.0, 0.0)]), 225),
    ]


def polearm_parts(h: Vec, d: Vec, tx: Vec) -> list[Part]:
    """长柄：细杆 + 尾端锥帽 + 金属箍 + 叶形枪头 + 两侧小翼。"""
    fr = frame(d, tx)
    sec = C.WEAPON_SECTION["polearm"]
    # 枪尖前伸量不超过 M3-D 方块占位（0.68），保证 144x144 画布不裁切（既有帧的姿势不变，只换形体）
    return [
        ("shaft", taper(h, fr, [(-0.25, sec, sec), (0.47, sec, sec)]), 110),
        ("butt", taper(h, fr, [(-0.25, sec * 1.3, sec * 1.3), (-0.30, 0.0, 0.0)]), 150),
        ("collar", taper(h, fr, [(0.45, 0.03, 0.03), (0.50, 0.03, 0.03)]), 150),
        ("spearhead", taper(h, fr, [(0.50, 0.026, 0.012), (0.57, 0.052, 0.014), (0.68, 0.0, 0.0)]), 225),
        ("lugs", taper(h, fr, [(0.49, 0.075, 0.01), (0.52, 0.075, 0.01)]), 190),
    ]


def staff_parts(h: Vec, d: Vec, tx: Vec) -> list[Part]:
    """法杖：细杆 + 尾端锥帽 + 两根外弯的叉角 + 八面体宝珠 + 顶端尖饰。"""
    fr = frame(d, tx)
    dd, e1, _e2 = fr
    sec = C.WEAPON_SECTION["staff"]
    out: list[Part] = [
        ("shaft", taper(h, fr, [(-0.2, sec, sec), (0.42, sec, sec)]), 105),
        ("butt", taper(h, fr, [(-0.2, sec * 1.3, sec * 1.3), (-0.25, 0.0, 0.0)]), 150),
    ]
    orb_c = _add(h, _mul(dd, 0.5))
    for side, name in ((1, "prong_a"), (-1, "prong_b")):
        base = _add(h, _mul(dd, 0.4))
        mid = _add(_add(h, _mul(dd, 0.47)), _mul(e1, side * 0.07))
        top = _add(_add(h, _mul(dd, 0.57)), _mul(e1, side * 0.04))
        out.append((name + "_lo", seg(base, mid, 0.012, e1), 130))
        out.append((name + "_hi", seg(mid, top, 0.012, e1), 130))
    out.append(("orb", octahedron(orb_c, fr, 0.062, 0.05), 230))
    out.append(("finial", taper(h, fr, [(0.56, 0.014, 0.014), (0.64, 0.0, 0.0)]), 200))
    return out


def bow_parts(h: Vec, d: Vec, c: Vec, hand_o: Vec, tx: Vec) -> list[Part]:
    """弓：握把 + 每侧三段的弓臂（逐段向射手方向弯成弧）+ 尖端 + 弓弦（拉弦的副手靠近弦中点时弦被拉向副手）。

    ``c`` 为 d 的垂直方向、指向前（人物朝向）；弓臂尖端向 -c（射手方向）弯。"""
    fr = frame(d, tx)
    dd, e1, e2 = fr
    sec = C.WEAPON_SECTION["bow"]
    half = C.WEAPON_LENGTH["bow"]
    bend = 0.07
    n_seg = 3

    def arc(sign: int, s: float) -> Vec:
        return _add(_add(h, _mul(dd, sign * half * s)), _mul(c, -bend * s * s))

    out: list[Part] = [("riser", taper(h, fr, [(-0.05, 0.022, 0.022), (0.05, 0.022, 0.022)]), 95)]
    for sign, tag in ((1, "up"), (-1, "lo")):
        for i in range(n_seg):
            a, b = arc(sign, i / n_seg), arc(sign, (i + 1) / n_seg)
            out.append((f"limb_{tag}{i}", seg(a, b, sec * (1.2 - 0.25 * i), e1), 120))
    up_tip, lo_tip = arc(1, 1.0), arc(-1, 1.0)
    for tag, tip in (("up", up_tip), ("lo", lo_tip)):
        out.append((f"nock_{tag}", octahedron(tip, fr, 0.014, 0.011), 200))
    mid = _mul(_add(up_tip, lo_tip), 0.5)
    nock = hand_o if math.dist(hand_o, mid) < 0.25 else mid
    out.append(("string_up", seg(up_tip, nock, 0.006, e1), 215))
    out.append(("string_lo", seg(nock, lo_tip, 0.006, e1), 215))
    return out


def dual_parts(h: Vec, d: Vec, tx: Vec, ho: Vec, fo: Vec) -> list[Part]:
    """双持：主手尖头短剑 + 副手尖头匕首（沿副手前臂）。"""
    sec = C.WEAPON_SECTION["dual"]
    out = sword_parts(h, frame(d, tx), C.WEAPON_LENGTH["dual"], sec, 0.045)
    out += sword_parts(ho, frame(fo, tx), 0.24, sec * 0.8, 0.032, prefix="off_")
    return out


def shield_parts(h: Vec, d: Vec, tx: Vec, tz: Vec, up: Vec, ctr: Vec) -> list[Part]:
    """盾族：主手尖头短剑 + 副手纹章盾（上宽下尖的五边形厚板）+ 盾心凸起 + 上沿加厚。"""
    sec = C.WEAPON_SECTION["shield"]
    out = sword_parts(h, frame(d, tx), C.WEAPON_LENGTH["shield"], sec, 0.045)
    heater = [(-0.10, 0.14), (0.10, 0.14), (0.10, 0.03), (0.0, -0.16), (-0.10, 0.03)]
    out.append(("shield", polygon_slab(ctr, tx, up, tz, heater, 0.014), 150))
    fr = (tz, tx, up)
    out.append(("boss", octahedron(_add(ctr, _mul(tz, 0.032)), fr, 0.024, 0.036), 205))
    out.append(("rim_top", polygon_slab(_add(ctr, _mul(up, 0.13)), tx, up, tz, [(-0.105, -0.012), (0.105, -0.012),
                                                                                (0.105, 0.012), (-0.105, 0.012)], 0.02), 120))
    return out


def part_names(family: str) -> list[str]:
    """每个族的部件名清单（规格 / 自检用；用一个固定姿势实例化）。"""
    z = (0.0, 0.0, 0.0)
    d, tx, tz, up = (0.0, 0.0, 1.0), (1.0, 0.0, 0.0), (0.0, 0.0, 1.0), (0.0, 1.0, 0.0)
    return [p[0] for p in family_parts(family, z, d, tx, tz, up, z, z, d)]


def family_parts(family: str, h: Vec, d: Vec, tx: Vec, tz: Vec, up: Vec, ho: Vec, ctr: Vec, fo: Vec) -> list[Part]:
    """族 -> 部件。h 主手、ho 副手、fo 副手前臂方向、ctr 盾牌中心；其余为躯干基。"""
    if family == "polearm":
        return polearm_parts(h, d, tx)
    if family == "staff":
        return staff_parts(h, d, tx)
    if family == "bow":
        c = _unit(_add(tz, _mul(d, -_dot(tz, d))))
        return bow_parts(h, d, c, ho, tx)
    if family == "dual":
        return dual_parts(h, d, tx, ho, fo)
    if family == "shield":
        return shield_parts(h, d, tx, tz, up, ctr)
    raise KeyError(family)
