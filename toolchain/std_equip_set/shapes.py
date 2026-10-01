"""装备几何：把装备零件摆进假人姿势（复用 ``std_dummy_poses.skeleton`` 的骨架、投影与光栅化）。

每种装备一个 ``item_fn(pose, joints) -> list[Part]``，零件 ``layer="item"``；``joints`` 来自
``skeleton.build_parts`` 的第二返回值（主手位置、武器方向、躯干基等）。武器沿 ``weapon_dir`` 放置——与假人
自带占位武器同一套持握角，因此装备层逐帧跟着身体层走（ADR-0072/0100 的共享时间轴）。
"""

from __future__ import annotations

from std_dummy_poses.skeleton import Part, _add, _box, _mul  # noqa: E402

LAYER = "item"


def _along(h, d, a, b, half_x, half_z, tx, tz, tone, name, half_y=None):
    """沿武器方向 d 从 h+d*a 到 h+d*b 的长条零件。"""
    hy = half_x if half_y is None else half_y
    return Part(name, LAYER, _box(_add(h, _mul(d, a)), _add(h, _mul(d, b)), half_x, half_z, tx, tz, hy), tone)


def sword(blade_len: float, blade_sec: float, guard_w: float):
    def fn(pose, j):
        h, d, tx, tz = j["hand_m"], j["weapon_dir"], j["tx"], j["tz"]
        return [
            _along(h, d, -0.05, 0.035, 0.02, 0.02, tx, tz, 95, "grip", 0.02),
            _along(h, d, 0.032, 0.048, guard_w, 0.02, tx, tz, 125, "guard", 0.02),
            _along(h, d, 0.05, 0.05 + blade_len, blade_sec, blade_sec, tx, tz, 225, "blade", blade_sec),
        ]
    return fn


def dagger():
    def fn(pose, j):
        h, d, tx, tz = j["hand_m"], j["weapon_dir"], j["tx"], j["tz"]
        return [
            _along(h, d, -0.04, 0.03, 0.018, 0.018, tx, tz, 95, "grip", 0.018),
            _along(h, d, 0.028, 0.04, 0.036, 0.016, tx, tz, 125, "guard", 0.016),
            _along(h, d, 0.04, 0.24, 0.018, 0.018, tx, tz, 215, "blade", 0.018),
        ]
    return fn


def bow():
    def fn(pose, j):
        h, d, tx, tz = j["hand_m"], j["weapon_dir"], j["tx"], j["tz"]
        stave = _along(h, d, -0.27, 0.27, 0.016, 0.016, tx, tz, 110, "stave", 0.016)
        grip = _along(h, d, -0.05, 0.05, 0.024, 0.024, tx, tz, 80, "grip", 0.024)
        off = _mul(tz, -0.06)
        string = Part("string", LAYER, _box(_add(_add(h, _mul(d, -0.27)), off), _add(_add(h, _mul(d, 0.27)), off),
                                            0.004, 0.004, tx, tz, 0.004), 200)
        return [string, stave, grip]
    return fn


def staff():
    def fn(pose, j):
        h, d, tx, tz = j["hand_m"], j["weapon_dir"], j["tx"], j["tz"]
        return [
            _along(h, d, -0.22, 0.52, 0.02, 0.02, tx, tz, 100, "pole", 0.02),
            _along(h, d, 0.5, 0.58, 0.032, 0.032, tx, tz, 125, "cradle", 0.032),
            _along(h, d, 0.56, 0.64, 0.04, 0.04, tx, tz, 230, "orb", 0.04),
        ]
    return fn


def chestplate():
    def fn(pose, j):
        tx, tz = j["tx"], j["tz"]
        parts = []
        torso = []
        for end, hw in ((j["chest_lo"], 0.118), (j["chest_hi"], 0.165)):
            for sx in (-1, 1):
                for sz in (-1, 1):
                    torso.append(_add(_add(end, _mul(tx, sx * hw)), _mul(tz, sz * 0.088)))
        parts.append(Part("plate", LAYER, torso, 105))
        belt_c = _add(j["pelvis"], (0, 0.03, 0))
        parts.append(Part("belt", LAYER, _box(_add(belt_c, (0, -0.025, 0)), _add(belt_c, (0, 0.025, 0)), 0.11, 0.08,
                                              tx, tz), 80))
        for side in ("m", "o"):
            sh = j["shoulder_" + side]
            parts.append(Part("pauldron_" + side, LAYER,
                              _box(_add(sh, (0, -0.035, 0)), _add(sh, (0, 0.04, 0)), 0.058, 0.058, tx, tz), 135))
        return parts
    return fn


SHAPES = {
    "sword_1h": lambda: sword(0.38, 0.024, 0.05),
    "greatsword": lambda: sword(0.58, 0.034, 0.075),
    "dagger": dagger,
    "bow": bow,
    "staff": staff,
    "chestplate": chestplate,
}


def item_fn(kind: str):
    return SHAPES[kind]()
