"""渲染配置（蒙皮预渲染的唯一输入声明）：读取、校验、缺省值。

配置是一份 JSON 文件；字段与缺省值见 README"渲染配置"一节。校验失败一律抛 :class:`ConfigError`
（命令行退出码 2，不启动引擎）；骨骼缺失等只有引擎能判定的拒绝由引擎侧返回，工具抛 :class:`SkinRefused`（退出码 3）。

单一来源：键清单、体量档、方向档、帧率、画布与像素密度的缺省值全部取自既有的假人姿势集配置
（``std_dummy_poses.config``）与方向档定义（``asset_import.directions``），本模块不持有第二份副本。
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path

from asset_import import directions
from asset_import.common import AssetImportError
from std_dummy_poses import config as SC

LIGHTING_MODES = ("unlit", "lit")
#: 逐层剪辑里"身体层"的固定层名（与既有序列帧假人集一致，见 std_dummy_poses.config.LAYER_BODY）。
BODY_LAYER = SC.LAYER_BODY
_ANIM_SET_ID_RE = re.compile(r"^display\.anim_set\.([a-z][a-z0-9_]*)$")
_LAYER_NAME_RE = re.compile(r"^[a-z][a-z0-9_]*$")

_KNOWN_FIELDS = {
    "anim_set_id", "stem_prefix", "skin", "direction_count", "slots", "keys", "mass_groups", "layers",
    "canvas", "pixels_per_unit", "pivot_px", "camera_pitch_deg", "lighting", "light_euler_deg", "light_color",
    "ambient_color", "fps", "position_scale", "pivot_tolerance_px", "min_opaque_pixels", "unity_layer",
    "pose_standard",
}


class ConfigError(ValueError):
    """渲染配置不合法（含未知方向档、未知键、未知体量档）；调用方不应启动引擎。"""


class SkinRefused(RuntimeError):
    """引擎侧拒绝渲染这套蒙皮（缺骨骼、骨名重复、层选择器找不到对象、缺剪辑资产等），``details`` 是逐项清单。"""

    def __init__(self, message: str, details: dict | None = None):
        super().__init__(message)
        self.details = details or {}


@dataclass(frozen=True)
class RenderConfig:
    anim_set_id: str                      # display.anim_set.<name>
    skin: str                             # 引擎工程内的资产路径（预制体或模型文件）
    stem_prefix: str                      # 资源名前缀；resource_ref = sprite_anim.<前缀><键点号换下划线>
    direction_count: int = SC.DEFAULT_DIRECTION_COUNT
    slots: tuple = ()                     # 方向档（canonical 右侧档位名）；缺省 = 该档数下全部 canonical 档
    keys: tuple = ()                      # 要渲染的键；缺省 = 标准姿势清单全部键
    mass_groups: tuple = ()               # 体量组（SC.MASS_TIERS 的档名子集）；中体量 = 主集
    layers: tuple = ()                    # ((层名, (选择器, ...)), ...)；身体层 body 恒有
    canvas: tuple = SC.CANVAS
    pixels_per_unit: float = float(SC.PIXELS_PER_UNIT)
    pivot_px: tuple = SC.ROOT_PX          # 脚点（地面点）像素坐标，原点在画布左上
    camera_pitch_deg: float = 0.0         # 相机俯角（度，0 = 水平侧视），整套固定
    lighting: str = "unlit"
    light_euler_deg: tuple = (50.0, -30.0, 0.0)
    light_color: tuple = (1.0, 1.0, 1.0)
    ambient_color: tuple = (0.55, 0.55, 0.55)
    fps: int = SC.FPS
    position_scale: float = 1.0           # 髋位置曲线的缩放（蒙皮身高 / 标准身高 2 世界单位）
    pivot_tolerance_px: int = 3           # 自检：待机时脚点相对 pivot 的允许漂移
    min_opaque_pixels: int = 16           # 自检：一帧不透明像素数下限（低于它算空帧）
    unity_layer: int = 30                 # 渲染隔离用的引擎层号
    pose_standard: bool | None = None     # None = 自动：键清单完整时为 true
    base_dir: str = ""                    # 配置文件所在目录（解析相对路径用，不参与渲染）

    @property
    def name(self) -> str:
        return _ANIM_SET_ID_RE.match(self.anim_set_id).group(1)

    @property
    def layer_names(self) -> list[str]:
        return [n for n, _s in self.layers]

    def effective_slots(self) -> list[str]:
        return list(self.slots) if self.slots else directions.canonical_slot_names(self.direction_count)

    def yaw_deg(self) -> dict[str, float]:
        """canonical 档位 -> 朝向偏航角（front = 0 面向镜头、side_r = 90 面向画面右、back = 180）。"""
        yaws = SC.slot_yaw_deg(self.direction_count)
        return {s: yaws[s] for s in self.effective_slots()}

    def to_dict(self) -> dict:
        """规范化的配置字典（写进规格文件，复现渲染用）。"""
        return {
            "anim_set_id": self.anim_set_id, "stem_prefix": self.stem_prefix, "skin": self.skin,
            "direction_count": self.direction_count, "slots": self.effective_slots(), "keys": list(self.keys),
            "mass_groups": list(self.mass_groups), "layers": {n: list(s) for n, s in self.layers},
            "canvas": list(self.canvas), "pixels_per_unit": self.pixels_per_unit, "pivot_px": list(self.pivot_px),
            "camera_pitch_deg": self.camera_pitch_deg, "lighting": self.lighting,
            "light_euler_deg": list(self.light_euler_deg), "light_color": list(self.light_color),
            "ambient_color": list(self.ambient_color), "fps": self.fps, "position_scale": self.position_scale,
            "pivot_tolerance_px": self.pivot_tolerance_px, "min_opaque_pixels": self.min_opaque_pixels,
            "unity_layer": self.unity_layer,
        }


def _num_list(d: dict, field_name: str, n: int, default: tuple) -> tuple:
    v = d.get(field_name, default)
    if not isinstance(v, (list, tuple)) or len(v) != n or not all(isinstance(x, (int, float)) and not isinstance(x, bool) for x in v):
        raise ConfigError(f"{field_name} 应为 {n} 个数字的数组，实际 {v!r}")
    return tuple(float(x) if field_name not in ("canvas", "pivot_px") else int(x) for x in v)


def parse_config(d: dict, base_dir: str = "") -> RenderConfig:
    """校验并构造 :class:`RenderConfig`。任何不合法的输入都在这里被拒绝（不启动引擎）。"""
    if not isinstance(d, dict):
        raise ConfigError("渲染配置的顶层必须是 JSON 对象")
    unknown = sorted(set(d) - _KNOWN_FIELDS)
    if unknown:
        raise ConfigError(f"未知的配置字段 {unknown}（可用字段：{sorted(_KNOWN_FIELDS)}）")

    anim_set_id = d.get("anim_set_id")
    if not isinstance(anim_set_id, str) or not _ANIM_SET_ID_RE.match(anim_set_id):
        raise ConfigError(f"anim_set_id 必须形如 display.anim_set.<小写名字>，实际 {anim_set_id!r}")
    name = _ANIM_SET_ID_RE.match(anim_set_id).group(1)
    if name.startswith("std_"):
        raise ConfigError(f"anim_set_id 的名字不能以 std_ 开头（{anim_set_id}）：std_ 前缀留给框架级姿势集（04 第 6.3 节）")
    skin = d.get("skin")
    if not isinstance(skin, str) or not skin.strip():
        raise ConfigError("skin 必填：引擎工程内的蒙皮资产路径（预制体或模型文件），如 Assets/Skins/hero.prefab")
    stem_prefix = d.get("stem_prefix", name + "_")
    if not isinstance(stem_prefix, str) or not re.fullmatch(r"[a-z][a-z0-9_]*", stem_prefix):
        raise ConfigError(f"stem_prefix 只能由小写字母、数字、下划线组成且以字母开头，实际 {stem_prefix!r}")
    if "__" in stem_prefix:
        raise ConfigError("stem_prefix 不能含连续两个下划线（'__' 是方向档/层名的分隔符）")

    direction_count = d.get("direction_count", SC.DEFAULT_DIRECTION_COUNT)
    try:
        canonical = directions.canonical_slot_names(direction_count)
    except (AssetImportError, TypeError) as exc:
        raise ConfigError(f"direction_count 非法：{exc}") from exc
    slots = d.get("slots", [])
    if not isinstance(slots, list) or not all(isinstance(s, str) for s in slots):
        raise ConfigError("slots 应为方向档名数组")
    if len(set(slots)) != len(slots):
        raise ConfigError(f"slots 有重复项：{slots}")
    unknown_slots = [s for s in slots if s not in canonical]
    if unknown_slots:
        mirrored = [s for s in unknown_slots if s in directions.all_slot_names(direction_count)]
        hint = f"（{mirrored} 是镜像档位，运行期由右侧档位水平翻转回填，不单独渲染）" if mirrored else ""
        raise ConfigError(f"未知的方向档 {unknown_slots}{hint}；direction_count={direction_count} 的可渲染档位是 {canonical}")
    if slots and directions.default_direction_slot(direction_count) not in slots:
        raise ConfigError(f"slots 必须包含默认方向档 {directions.default_direction_slot(direction_count)}（整身默认朝向剪辑取它）")

    fps = d.get("fps", SC.FPS)
    if not isinstance(fps, int) or isinstance(fps, bool) or not (1 <= fps <= 120):
        raise ConfigError(f"fps 应为 1..120 的整数，实际 {fps!r}")

    keys = d.get("keys", "all")
    all_keys = [c.key for c in SC.build_clip_defs()]
    if keys == "all":
        keys_t: tuple = tuple(all_keys)
        keys_explicit = False
    else:
        if not isinstance(keys, list) or not keys or not all(isinstance(k, str) for k in keys):
            raise ConfigError('keys 应为键名数组，或缺省/"all"（全部键）')
        if len(set(keys)) != len(keys):
            raise ConfigError(f"keys 有重复项：{sorted({k for k in keys if keys.count(k) > 1})}")
        bad = [k for k in keys if k not in set(all_keys)]
        if bad:
            raise ConfigError(f"未知的姿势键 {bad}（键清单见 toolchain/std_dummy_poses/README.md）")
        keys_t = tuple(keys)
        keys_explicit = True

    mass = d.get("mass_groups", [])
    if not isinstance(mass, list) or not all(isinstance(m, str) for m in mass):
        raise ConfigError("mass_groups 应为体量档名数组")
    bad_mass = [m for m in mass if m not in SC.MASS_TIERS]
    if bad_mass:
        raise ConfigError(f"未知的体量档 {bad_mass}（可用：{sorted(SC.MASS_TIERS)}；中体量就是主集，不单列）")
    if len(set(mass)) != len(mass):
        raise ConfigError(f"mass_groups 有重复项：{mass}")

    layers_raw = d.get("layers", {})
    if not isinstance(layers_raw, dict):
        raise ConfigError("layers 应为 {层名: [选择器, ...]} 对象")
    layers = []
    claimed: dict[str, str] = {}
    for lname, selectors in layers_raw.items():
        if not _LAYER_NAME_RE.match(lname) or lname == BODY_LAYER:
            raise ConfigError(f"装备层名 {lname!r} 非法（小写字母/数字/下划线，且不能叫 {BODY_LAYER}，它是身体层）")
        if "__" in lname:
            raise ConfigError(f"装备层名 {lname!r} 不能含连续两个下划线")
        if not isinstance(selectors, list) or not selectors or not all(isinstance(s, str) and s for s in selectors):
            raise ConfigError(f"装备层 {lname!r} 的选择器应为非空字符串数组")
        for s in selectors:
            if s in claimed:
                raise ConfigError(f"选择器 {s!r} 同时属于装备层 {claimed[s]!r} 与 {lname!r}")
            claimed[s] = lname
        layers.append((lname, tuple(selectors)))

    canvas = _num_list(d, "canvas", 2, SC.CANVAS)
    if not all(16 <= int(c) <= 2048 for c in canvas):
        raise ConfigError(f"canvas 每边应在 16..2048 像素，实际 {list(canvas)}")
    pivot = _num_list(d, "pivot_px", 2, SC.ROOT_PX)
    if not (0 <= pivot[0] < canvas[0] and 0 <= pivot[1] <= canvas[1]):
        raise ConfigError(f"pivot_px {list(pivot)} 必须落在画布 {list(canvas)} 内")
    ppu = d.get("pixels_per_unit", SC.PIXELS_PER_UNIT)
    if not isinstance(ppu, (int, float)) or isinstance(ppu, bool) or ppu <= 0:
        raise ConfigError(f"pixels_per_unit 应为正数，实际 {ppu!r}")
    pitch = d.get("camera_pitch_deg", 0.0)
    if not isinstance(pitch, (int, float)) or isinstance(pitch, bool) or not (0.0 <= pitch <= 80.0):
        raise ConfigError(f"camera_pitch_deg 应在 0..80 度（0 = 水平侧视），实际 {pitch!r}")
    lighting = d.get("lighting", "unlit")
    if lighting not in LIGHTING_MODES:
        raise ConfigError(f"lighting 只能是 {LIGHTING_MODES}，实际 {lighting!r}")
    pos_scale = d.get("position_scale", 1.0)
    if not isinstance(pos_scale, (int, float)) or isinstance(pos_scale, bool) or pos_scale <= 0:
        raise ConfigError(f"position_scale 应为正数，实际 {pos_scale!r}")
    tol = d.get("pivot_tolerance_px", 3)
    min_px = d.get("min_opaque_pixels", 16)
    layer_idx = d.get("unity_layer", 30)
    for fname, v, lo, hi in (("pivot_tolerance_px", tol, 0, 64), ("min_opaque_pixels", min_px, 1, 1_000_000),
                             ("unity_layer", layer_idx, 8, 31)):
        if not isinstance(v, int) or isinstance(v, bool) or not (lo <= v <= hi):
            raise ConfigError(f"{fname} 应为 {lo}..{hi} 的整数，实际 {v!r}")
    pose_standard = d.get("pose_standard", None)
    if pose_standard is not None and not isinstance(pose_standard, bool):
        raise ConfigError("pose_standard 应为布尔值或缺省")
    if pose_standard is None and not keys_explicit:
        pose_standard = True

    return RenderConfig(
        anim_set_id=anim_set_id, skin=skin.replace("\\", "/"), stem_prefix=stem_prefix,
        direction_count=direction_count, slots=tuple(slots), keys=keys_t, mass_groups=tuple(mass),
        layers=tuple(layers), canvas=(int(canvas[0]), int(canvas[1])), pixels_per_unit=float(ppu),
        pivot_px=(int(pivot[0]), int(pivot[1])), camera_pitch_deg=float(pitch), lighting=lighting,
        light_euler_deg=_num_list(d, "light_euler_deg", 3, (50.0, -30.0, 0.0)),
        light_color=_num_list(d, "light_color", 3, (1.0, 1.0, 1.0)),
        ambient_color=_num_list(d, "ambient_color", 3, (0.55, 0.55, 0.55)),
        fps=fps, position_scale=float(pos_scale), pivot_tolerance_px=tol, min_opaque_pixels=min_px,
        unity_layer=layer_idx, pose_standard=pose_standard, base_dir=base_dir,
    )


def load_config(path: Path | str) -> RenderConfig:
    p = Path(path)
    try:
        data = json.loads(p.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ConfigError(f"找不到渲染配置文件 {p}") from exc
    except json.JSONDecodeError as exc:
        raise ConfigError(f"渲染配置 {p} 不是合法 JSON：{exc}") from exc
    return parse_config(data, base_dir=str(p.resolve().parent))
