"""姿势（sprite 版 Pose 关节角）→ 骨骼局部旋转 / 髋部位置，以及正向运动学（自检用）。

姿势函数与 sprite 版是**同一份**（``std_dummy_poses.poses.pose_at``）：同一个剪辑、同一个时刻，sprite 版把它投影成图，
本模块把它写成标准人形骨骼上的局部四元数曲线，所以两种外形的动作逐帧同源。

坐标系：Y 向上、Z 向前、X 指向主手侧，旋转矩阵约定与 sprite 版 ``skeleton._rx/_ry/_rz`` 逐式相同
（正向 X 旋转让朝前的向量俯下去；Unity 的 ``Quaternion`` 恰是这套约定，所以矩阵转出的四元数可直接作为引擎曲线值）。
全部是纯 Python 浮点运算，无随机数、无字典序依赖。
"""

from __future__ import annotations

import math

from std_dummy_poses import config as SC
from std_dummy_poses.skeleton import build_parts, solve_ground

from . import config as C

Mat = tuple[tuple[float, float, float], tuple[float, float, float], tuple[float, float, float]]
Quat = tuple[float, float, float, float]  # (x, y, z, w)

IDENTITY: Mat = ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0))


def rx(deg: float) -> Mat:
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    return ((1.0, 0.0, 0.0), (0.0, c, -s), (0.0, s, c))


def ry(deg: float) -> Mat:
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    return ((c, 0.0, s), (0.0, 1.0, 0.0), (-s, 0.0, c))


def rz(deg: float) -> Mat:
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    return ((c, -s, 0.0), (s, c, 0.0), (0.0, 0.0, 1.0))


def mul(a: Mat, b: Mat) -> Mat:
    return tuple(tuple(sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)) for i in range(3))  # type: ignore[return-value]


def mul3(a: Mat, b: Mat, c: Mat) -> Mat:
    return mul(mul(a, b), c)


def transpose(a: Mat) -> Mat:
    return tuple(tuple(a[j][i] for j in range(3)) for i in range(3))  # type: ignore[return-value]


def apply(a: Mat, v: tuple[float, float, float]) -> tuple[float, float, float]:
    return tuple(sum(a[i][k] * v[k] for k in range(3)) for i in range(3))  # type: ignore[return-value]


def mat_to_quat(m: Mat) -> Quat:
    """旋转矩阵 → 单位四元数 (x, y, z, w)，w ≥ 0 的分支（Shepperd 法，数值稳健）。"""
    t = m[0][0] + m[1][1] + m[2][2]
    if t > 0.0:
        s = math.sqrt(t + 1.0) * 2.0
        w = 0.25 * s
        x = (m[2][1] - m[1][2]) / s
        y = (m[0][2] - m[2][0]) / s
        z = (m[1][0] - m[0][1]) / s
    elif m[0][0] > m[1][1] and m[0][0] > m[2][2]:
        s = math.sqrt(1.0 + m[0][0] - m[1][1] - m[2][2]) * 2.0
        w = (m[2][1] - m[1][2]) / s
        x = 0.25 * s
        y = (m[0][1] + m[1][0]) / s
        z = (m[0][2] + m[2][0]) / s
    elif m[1][1] > m[2][2]:
        s = math.sqrt(1.0 + m[1][1] - m[0][0] - m[2][2]) * 2.0
        w = (m[0][2] - m[2][0]) / s
        x = (m[0][1] + m[1][0]) / s
        y = 0.25 * s
        z = (m[1][2] + m[2][1]) / s
    else:
        s = math.sqrt(1.0 + m[2][2] - m[0][0] - m[1][1]) * 2.0
        w = (m[1][0] - m[0][1]) / s
        x = (m[0][2] + m[2][0]) / s
        y = (m[1][2] + m[2][1]) / s
        z = 0.25 * s
    if w < 0.0:
        x, y, z, w = -x, -y, -z, -w
    n = math.sqrt(x * x + y * y + z * z + w * w)
    return (x / n, y / n, z / n, w / n)


def quat_to_mat(q: Quat) -> Mat:
    x, y, z, w = q
    n = math.sqrt(x * x + y * y + z * z + w * w)
    x, y, z, w = x / n, y / n, z / n, w / n
    return (
        (1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)),
        (2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)),
        (2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)),
    )


def quat_angle_deg(q: Quat) -> float:
    """四元数相对单位旋转的旋转角（度，0..180）。"""
    w = max(-1.0, min(1.0, abs(q[3])))
    return math.degrees(2.0 * math.acos(w))


def quat_dot(a: Quat, b: Quat) -> float:
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3]


# --------------------------------------------------------------------------
# 姿势 → 骨骼局部旋转
# --------------------------------------------------------------------------

def bone_matrices(g: dict) -> dict[str, Mat]:
    """sprite 版 Pose（度）→ 每根旋转骨骼相对父骨骼的局部旋转矩阵（关节链与 sprite 版 ``skeleton.build_parts`` 一一对应）。"""
    out: dict[str, Mat] = {}
    out["hips"] = ry(g["yaw"])
    # 躯干：sprite 版 Rt = Ry(yaw + t_yaw) · Rz(t_roll) · Rx(t_pitch)，相对髋（Ry(yaw)）的局部量
    out["spine"] = mul3(ry(g["t_yaw"]), rz(g["t_roll"]), rx(g["t_pitch"]))
    # 头：Rh = Rt · Ry(h_yaw) · Rx(h_pitch)
    out["head"] = mul(ry(g["h_yaw"]), rx(g["h_pitch"]))
    forearm_in_spine: dict[str, Mat] = {}
    for side, s in (("r", 1), ("l", -1)):
        pref = "m" if side == "r" else "o"
        ua = mul(rz(s * g[f"{pref}_sa"]), rx(-g[f"{pref}_sf"]))            # 上臂（躯干系）
        fa = mul(rx(-g[f"{pref}_el"]), ua)                                  # 前臂（躯干系）：肘绕躯干 X 轴屈
        out[f"upper_arm_{side}"] = ua
        out[f"forearm_{side}"] = mul(transpose(ua), fa)                     # 相对上臂的局部量
        forearm_in_spine[side] = fa
        th = mul(rz(s * g[f"{pref}_ha"]), rx(-g[f"{pref}_hf"]))            # 大腿（髋系）
        sh = mul(rx(g[f"{pref}_kn"]), th)                                   # 小腿（髋系）：膝绕髋系 X 轴屈
        out[f"thigh_{side}"] = th
        out[f"shin_{side}"] = mul(transpose(th), sh)
        out[f"foot_{side}"] = transpose(sh)                                 # 脚掌保持与髋系平行
    # 主手挂点：刀刃沿挂点 -Y；躯干系朝向 Ry(wy)·Rz(wz)·Rx(-wp)（sprite 版 d_local 的同一朝向），换算到前臂局部
    rw = mul3(ry(g["wy"]), rz(g["wz"]), rx(-g["wp"]))
    out["socket.main_hand"] = mul(transpose(forearm_in_spine["r"]), rw)
    return out


def hips_position_bh(pose: dict, parts_cache: tuple | None = None) -> tuple[float, float, float]:
    """髋（骨盆中心）世界位置，单位身高倍数：休息高度 + 姿势偏移 + 着地求解（最低点落在地面）+ 跳跃抬升。"""
    parts, _joints = parts_cache if parts_cache is not None else build_parts(pose, None)
    dy = solve_ground(pose, parts)
    return (pose["px"], SC.REST_HIP_Y + pose["py"] + dy, pose["pz"])


# --------------------------------------------------------------------------
# 正向运动学（自检：用写出的四元数把关节摆回去，与 sprite 版关节位置对账）
# --------------------------------------------------------------------------

def forward_kinematics(rot: dict[str, Quat], hips_pos: tuple[float, float, float]) -> dict[str, tuple[float, float, float]]:
    """按骨骼层级与休息位置，用局部四元数算每根骨骼的世界位置（身高倍数）。缺旋转的骨骼视为单位旋转。"""
    world_rot: dict[str, Mat] = {}
    world_pos: dict[str, tuple[float, float, float]] = {}
    for name, parent, rest in C.BONES:
        local = quat_to_mat(rot[name]) if name in rot else IDENTITY
        if parent is None:
            world_rot[name] = local
            world_pos[name] = hips_pos
        else:
            pr = world_rot[parent]
            wr = mul(pr, local)
            world_rot[name] = wr
            off = apply(pr, rest)
            pp = world_pos[parent]
            world_pos[name] = (pp[0] + off[0], pp[1] + off[1], pp[2] + off[2])
    return world_pos


def forward_kinematics_rot(rot: dict[str, Quat]) -> dict[str, Mat]:
    world_rot: dict[str, Mat] = {}
    for name, parent, _rest in C.BONES:
        local = quat_to_mat(rot[name]) if name in rot else IDENTITY
        world_rot[name] = local if parent is None else mul(world_rot[parent], local)
    return world_rot
