"""生成占位装备集：图标、静态层图、逐层剪辑、皮肤包、规格文件、数据表。

目录布局（与 ADR-0071/0072/0100 一致）：

  <assets>/icons/item/<物品名>.png                                       图标（icon.item.<名>）
  <assets>/sprites/item_<名>/<方向>/<层名>.png                           静态层图（ADR-0071，逐层剪辑缺失时的回落）
  <assets>/sprite_anim/item_<名>__<剪辑名>__<方向>__<层名>/               逐层剪辑（ADR-0100 候选，带方向一级）
  <assets>/ui/skin/default/…                                             占位皮肤包（skin.py）
  <assets>/std_equip_set.json                                            规格（每件装备的层、族、覆盖的剪辑）
  <data>/…                                                               数据表（data.py）

姿势与帧时序复用假人姿势集（``std_dummy_poses``）：装备层与身体层同一时间线，逐帧跟着身体走。
"""

from __future__ import annotations

import json
import shutil
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from std_dummy_poses import config as D  # noqa: E402
from std_dummy_poses.build import _write_clip, frame_times  # noqa: E402
from std_dummy_poses.poses import pose_at  # noqa: E402
from std_dummy_poses.skeleton import render  # noqa: E402

from . import config as C  # noqa: E402
from . import data as data_mod  # noqa: E402
from . import icons as icons_mod  # noqa: E402
from . import shapes  # noqa: E402
from . import skin as skin_mod  # noqa: E402

CLEAN_PREFIXES = ("item_std_",)


def clips_for_item(item: C.ItemDef) -> list[D.ClipDef]:
    """该装备层需要逐层剪辑的假人剪辑（别名键不单独出资源）。

    武器层：该武器族的族键，加上无族且假人本身给了武器层的状态剪辑（受击/死亡/跳跃/施法/闪避）；
    护甲（身体跟随层）：全部剪辑。——与 asset_import/equip_pack.needed_pose_keys 的规则一致，但这里从假人
    剪辑目录（ClipDef）独立推出，校验器从数据行推出，两边互为核对。"""
    out = []
    for c in D.build_clip_defs():
        if c.alias_of:
            continue
        if not item.is_weapon:
            out.append(c)
        elif c.family == item.family or (c.family is None and c.weapon_layer_family is not None):
            out.append(c)
    return out


def _item_image(item: C.ItemDef, clip: D.ClipDef, pose: dict, yaw: float) -> Image.Image:
    state_clip = item.is_weapon and clip.family is None
    if state_clip:
        pose = dict(pose, wp=D.STATE_CLIP_WEAPON_PITCH)
    return render(pose, yaw, None, layers="item", clamp_weapon_to_ground=state_clip,
                  item_fn=shapes.item_fn(item.kind))


def _static_clip(item: C.ItemDef) -> D.ClipDef:
    want = f"idle.{item.family}" if item.is_weapon else "idle"
    return next(c for c in D.build_clip_defs() if c.key == want)


def clip_stem(clip: D.ClipDef) -> str:
    return clip.resource_ref.partition(".")[2].replace(".", "_")


def generate_item(item: C.ItemDef, assets_out: Path, fps: int, log=print) -> dict:
    slots = D.canonical_slots(C.DIRECTION_COUNT)
    yaws = D.slot_yaw_deg(C.DIRECTION_COUNT)
    icon_path = assets_out / item.icon_file
    icon_path.parent.mkdir(parents=True, exist_ok=True)
    icons_mod.draw_icon(item.kind).save(icon_path, format="PNG", optimize=True)

    sclip = _static_clip(item)
    spose = pose_at(sclip, frame_times(sclip, fps)[0][2])
    for slot in slots:
        p = assets_out / "sprites" / item.mesh_stem / slot / f"{item.layer}.png"
        p.parent.mkdir(parents=True, exist_ok=True)
        _item_image(item, sclip, spose, yaws[slot]).save(p, format="PNG", optimize=True)

    n_dirs = 0
    refs = []
    for clip in clips_for_item(item):
        times = frame_times(clip, fps)
        durations = [round(dur / 1000.0, 6) for _t0, dur, _s in times]
        poses = [pose_at(clip, s) for _t0, _d, s in times]
        for slot in slots:
            frames = [_item_image(item, clip, p, yaws[slot]) for p in poses]
            out = assets_out / "sprite_anim" / f"{item.mesh_stem}__{clip_stem(clip)}__{slot}__{item.layer}"
            _write_clip(out, frames, durations, clip.loop, fps)
            n_dirs += 1
        refs.append(clip.resource_ref)
    log(f"  {item.name}: 图标 + {len(slots)} 张静态层图 + {len(refs)} 个剪辑 × {len(slots)} 方向档（{n_dirs} 个剪辑目录）")
    return {"item_id": item.item_id, "mesh_ref": item.mesh_ref, "layer": item.layer, "family": item.family,
            "kind": item.kind, "clips": sorted(refs)}


def clean_outputs(assets_out: Path) -> int:
    removed = 0
    for sub in ("sprites", "sprite_anim"):
        root = assets_out / sub
        if root.is_dir():
            for d in sorted(root.iterdir()):
                if d.is_dir() and d.name.startswith(CLEAN_PREFIXES):
                    shutil.rmtree(d)
                    removed += 1
    icons = assets_out / "icons" / "item"
    if icons.is_dir():
        for f in sorted(icons.glob("std_*.png")):
            f.unlink()
            removed += 1
        if not any(icons.iterdir()):
            icons.rmdir()
    skin = assets_out / "ui" / "skin" / C.SKIN_NAME
    if skin.is_dir():
        shutil.rmtree(skin)
        removed += 1
    spec = assets_out / C.SPEC_FILE
    if spec.is_file():
        spec.unlink()
        removed += 1
    return removed


def generate(assets_out: Path, data_out: Path, *, items: tuple[C.ItemDef, ...] = C.ITEMS, fps: int = D.FPS,
             clean: bool = False, with_skin: bool = True, with_data: bool = True, log=print) -> dict:
    if clean:
        log(f"--clean：删除 {clean_outputs(assets_out)} 项（仅 item_std_* 资源、std_* 图标、皮肤 {C.SKIN_NAME}、规格文件）")
    spec_items = [generate_item(it, assets_out, fps, log) for it in items]
    skin_files = skin_mod.generate_skin(assets_out) if with_skin else []
    if skin_files:
        log(f"  皮肤包 {C.SKIN_REF}: {len(skin_files)} 个文件")
    spec = {
        "generator": "toolchain/gen_std_equip_set.py",
        "doc": "architecture/手感设计/08_装备与UI资产契约.md",
        "anim_set_id": C.ANIM_SET_ID,
        "direction_count": C.DIRECTION_COUNT,
        "directions": D.canonical_slots(C.DIRECTION_COUNT),
        "fps": fps,
        "skin_ref": C.SKIN_REF,
        "skin_files": skin_files,
        "items": spec_items,
    }
    (assets_out / C.SPEC_FILE).write_text(json.dumps(spec, indent=2, ensure_ascii=False) + "\n", encoding="utf-8",
                                          newline="\n")
    if with_data:
        written = data_mod.write_all(data_out)
        log(f"  数据表: {len(written)} 张（{data_out.as_posix()}）")
    return spec
