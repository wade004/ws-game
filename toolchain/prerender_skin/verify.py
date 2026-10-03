"""自检：把磁盘上的预渲染产物对回渲染计划（计划来自 std_dummy_poses 的同一套时间规则）。有任何错误即失败。

检查项（对应 ADR-0140 的自检清单）：
  1 结构：每份资源 × 方向 × 层都有 atlas.png + frames.json；帧数、帧时长、帧率、循环标志、画布尺寸、图集尺寸与计划一致；
    数据行的键集合 = 配置的键集合，每个 resource_ref 都落到存在的资源，事件与计划逐项一致。
  2 非空：整身与身体层每帧不透明像素数 >= min_opaque_pixels；装备层每帧至少 1 个不透明像素。
  3 画布裁切：任何层的任何帧，不透明像素不得贴到画布四边（贴边 = 被画布切掉了）。
  4 枢轴不漂移：基础待机键（idle，主集）首帧，每个方向的轮廓底边与 pivot_px 的纵向距离、脚区（轮廓底部若干行）水平中心与 pivot_px 的横向距离
    不超过 pivot_tolerance_px（脚在地面点上）。没有渲染待机键时这一项不适用，记一条提示。
  5 层对齐：有装备层时，逐帧：身体层与各装备层的不透明像素并集 == 整身合成的不透明像素；
    没被任何装备层遮住的身体层像素，颜色与整身合成逐像素相同（同一相机、同一姿势，对不上说明层渲染不是同一帧）。
数据行过 ``validate_data --strict`` 与 ``import_assets check`` 由 cli 的 ``run_external_validation`` 另行执行。
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

from PIL import Image, ImageChops
from asset_import import directions
from std_dummy_poses import build as SB

from .assemble import clip_dir_name
from .plan import SPEC_SUFFIX, RenderPlan, anim_set_rows


@dataclass
class Report:
    errors: list = field(default_factory=list)
    warnings: list = field(default_factory=list)
    stats: dict = field(default_factory=dict)

    def err(self, msg: str) -> None:
        self.errors.append(msg)

    def warn(self, msg: str) -> None:
        self.warnings.append(msg)

    @property
    def ok(self) -> bool:
        return not self.errors


def load_clip(dir_path: Path, canvas: tuple):
    """读一份资源（atlas.png + frames.json）-> (frames.json 字典, [帧图像])；缺文件/坏文件抛 FileNotFoundError / ValueError。"""
    atlas_path, fj = dir_path / "atlas.png", dir_path / "frames.json"
    if not atlas_path.is_file() or not fj.is_file():
        raise FileNotFoundError(f"{dir_path} 缺 atlas.png 或 frames.json")
    doc = json.loads(fj.read_text(encoding="utf-8"))
    atlas = Image.open(atlas_path).convert("RGBA")
    frames = []
    for f in doc["frames"]:
        if f["w"] != canvas[0] or f["h"] != canvas[1]:
            raise ValueError(f"{fj} 的帧 {f['index']} 尺寸 {f['w']}×{f['h']} 不是画布 {canvas[0]}×{canvas[1]}")
        frames.append(atlas.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"])))
    return doc, atlas.size, frames


def opaque_count(im: Image.Image) -> int:
    return sum(im.getchannel("A").histogram()[1:])


def silhouette_mask(im: Image.Image) -> Image.Image:
    return im.getchannel("A").point(lambda v: 255 if v > 0 else 0)


def _touches_border(mask: Image.Image) -> bool:
    w, h = mask.size
    for box in ((0, 0, w, 1), (0, h - 1, w, h), (0, 0, 1, h), (w - 1, 0, w, h)):
        if mask.crop(box).getbbox() is not None:
            return True
    return False


def _expected_atlas_size(n: int, canvas: tuple) -> tuple:
    fw, fh = canvas
    cols = max(1, SB.ATLAS_MAX_WIDTH // fw)
    rows = (n + cols - 1) // cols
    return (min(n, cols) * fw, rows * fh)


def check_structure(plan: RenderPlan, assets_out: Path, data_out: Path, report: Report):
    """1 结构。返回 {资源目录名: [帧图像]} 供后续检查复用。"""
    cfg = plan.config
    loaded: dict[str, list] = {}
    default_slot = directions.default_direction_slot(cfg.direction_count)
    for cp in plan.clips:
        for slot in cfg.effective_slots():
            for variant in plan.variants():
                names = [clip_dir_name(cp.stem, slot)] if variant == "all" else [clip_dir_name(cp.stem, slot, variant)]
                if variant == "all" and slot == default_slot:
                    names.append(clip_dir_name(cp.stem))
                for name in names:
                    d = assets_out / "sprite_anim" / name
                    try:
                        doc, atlas_size, frames = load_clip(d, cfg.canvas)
                    except (FileNotFoundError, ValueError, KeyError) as exc:
                        report.err(f"资源 {name}：{exc}")
                        continue
                    loaded[name] = frames
                    if len(frames) != cp.frame_count:
                        report.err(f"资源 {name}：帧数 {len(frames)}，计划 {cp.frame_count}（键 {cp.key}）")
                    if doc.get("fps") != cfg.fps:
                        report.err(f"资源 {name}：fps {doc.get('fps')}，配置 {cfg.fps}")
                    if doc.get("loop") != cp.loop:
                        report.err(f"资源 {name}：loop {doc.get('loop')}，计划 {cp.loop}")
                    durs = [f["duration"] for f in doc["frames"]]
                    if len(durs) == len(cp.durations_s) and any(abs(a - b) > 1e-9 for a, b in zip(durs, cp.durations_s)):
                        report.err(f"资源 {name}：帧时长与计划不一致")
                    if abs(sum(durs) - cp.total_ms / 1000.0) > 1e-4 * max(1, len(durs)):
                        report.err(f"资源 {name}：帧时长之和 {sum(durs):.6f}s 与剪辑总长 {cp.total_ms}ms 不一致")
                    if atlas_size != _expected_atlas_size(len(frames), cfg.canvas):
                        report.err(f"资源 {name}：图集尺寸 {atlas_size} 与帧数不匹配")
    # 数据行
    path = data_out / "display" / "display.anim_set.json"
    if not path.is_file():
        report.err(f"缺数据行文件 {path}")
    else:
        rows = {r["id"]: r for r in json.loads(path.read_text(encoding="utf-8")).get("rows", [])}
        for exp in anim_set_rows(plan):
            row = rows.get(exp["id"])
            if row is None:
                report.err(f"数据行缺 {exp['id']}")
                continue
            if set(row.get("clips", {})) != set(exp["clips"]):
                diff = set(row.get("clips", {})) ^ set(exp["clips"])
                report.err(f"数据行 {exp['id']} 的键集合与配置不一致：{sorted(diff)}")
                continue
            for key, want in exp["clips"].items():
                got = row["clips"][key]
                if got.get("resource_ref") != want["resource_ref"] or got.get("events") != want["events"]:
                    report.err(f"数据行 {exp['id']} 键 {key}：resource_ref/events 与计划不一致")
            if bool(row.get("pose_standard", False)) != bool(exp.get("pose_standard", False)):
                report.err(f"数据行 {exp['id']}：pose_standard 与配置不一致")
        spec = assets_out / (cfg.name + SPEC_SUFFIX)
        if not spec.is_file():
            report.err(f"缺规格文件 {spec}")
        else:
            sc = json.loads(spec.read_text(encoding="utf-8")).get("clips", {})
            for cp in plan.clips:
                e = sc.get(cp.stem)
                if e is None or e.get("frame_count") != cp.frame_count or e.get("events") != [dict(x) for x in cp.events]:
                    report.err(f"规格文件里 {cp.stem} 的帧数/事件与计划不一致")
    report.stats["resource_dirs"] = len(loaded)
    return loaded


def check_content(plan: RenderPlan, loaded: dict, report: Report) -> None:
    """2 非空、3 画布裁切、5 层对齐。"""
    cfg = plan.config
    has_layers = bool(cfg.layers)
    n_frames = 0
    for cp in plan.clips:
        for slot in cfg.effective_slots():
            comp_name = clip_dir_name(cp.stem, slot)
            comp = loaded.get(comp_name)
            body = loaded.get(clip_dir_name(cp.stem, slot, "body")) if has_layers else None
            layer_frames = {n: loaded.get(clip_dir_name(cp.stem, slot, n)) for n in cfg.layer_names}
            groups = [("整身", comp, cfg.min_opaque_pixels)]
            if has_layers:
                groups.append(("body", body, cfg.min_opaque_pixels))
                groups += [(n, f, 1) for n, f in layer_frames.items()]
            for label, frames, minpx in groups:
                if frames is None:
                    continue
                for i, im in enumerate(frames):
                    n_frames += 1
                    cnt = opaque_count(im)
                    if cnt < minpx:
                        report.err(f"空帧：{comp_name}（{label}）第 {i} 帧不透明像素 {cnt} < {minpx}（键 {cp.key}）")
                    elif _touches_border(silhouette_mask(im)):
                        report.err(f"画布裁切：{comp_name}（{label}）第 {i} 帧的不透明像素贴到画布边缘（键 {cp.key}）")
            if has_layers and comp is not None and body is not None and all(v is not None for v in layer_frames.values()):
                for i in range(len(comp)):
                    union = silhouette_mask(body[i])
                    covered = Image.new("L", comp[i].size, 0)
                    for n in cfg.layer_names:
                        m = silhouette_mask(layer_frames[n][i])
                        union = ImageChops.lighter(union, m)
                        covered = ImageChops.lighter(covered, m)
                    if ImageChops.difference(union, silhouette_mask(comp[i])).getbbox() is not None:
                        report.err(f"层未对齐：{comp_name} 第 {i} 帧，身体层与装备层的轮廓并集 != 整身轮廓（键 {cp.key}）")
                        continue
                    owned = ImageChops.subtract(silhouette_mask(body[i]), covered)
                    diff = ImageChops.difference(comp[i].convert("RGB"), body[i].convert("RGB"))
                    masked = Image.new("RGB", diff.size, (0, 0, 0))
                    masked.paste(diff, mask=owned)
                    if masked.getbbox() is not None:
                        report.err(f"层未对齐：{comp_name} 第 {i} 帧，未被装备层遮住的身体层像素与整身合成颜色不同（键 {cp.key}）")
    report.stats["frames_checked"] = n_frames


FOOT_BAND_FRACTION = 0.06  # 脚区 = 轮廓底部这一比例的画布高度（至少 4 行）


def check_pivot(plan: RenderPlan, loaded: dict, report: Report) -> None:
    """4 枢轴不漂移：主集基础待机键（idle）首帧。持械/战斗站姿等待机变体的步幅与重心本来就不同，不作为枢轴基准。"""
    cfg = plan.config
    idle = [cp for cp in plan.clips if cp.mass is None and cp.key == "idle"]
    if not idle:
        report.warn("没有渲染基础待机键（idle）：枢轴不漂移检查不适用")
        return
    px, py = cfg.pivot_px
    tol = cfg.pivot_tolerance_px
    worst = 0.0
    for cp in idle:
        for slot in cfg.effective_slots():
            frames = loaded.get(clip_dir_name(cp.stem, slot))
            if not frames:
                continue
            mask = silhouette_mask(frames[0])
            bbox = mask.getbbox()
            if bbox is None:
                continue
            # 水平方向只看脚区（轮廓底部若干行）的中心：举臂、持械会把整身包围盒的水平中心带偏，但不代表枢轴漂移
            foot_rows = max(4, int(round(cfg.canvas[1] * FOOT_BAND_FRACTION)))
            foot_bbox = mask.crop((0, max(0, bbox[3] - foot_rows), mask.width, bbox[3])).getbbox()
            foot_center = (foot_bbox[0] + foot_bbox[2]) / 2.0
            bottom_gap = bbox[3] - py
            center_gap = foot_center - px
            worst = max(worst, abs(bottom_gap), abs(center_gap))
            if abs(bottom_gap) > tol:
                report.err(f"枢轴漂移：{cp.stem}__{slot} 首帧轮廓底边 {bbox[3]} 与 pivot_y {py} 相差 {bottom_gap} 像素 > {tol}")
            if abs(center_gap) > tol:
                report.err(f"枢轴漂移：{cp.stem}__{slot} 首帧脚区水平中心 {foot_center} 与 pivot_x {px} 相差 {center_gap:.1f} 像素 > {tol}")
    report.stats["pivot_worst_px"] = worst


def verify(plan: RenderPlan, assets_out: Path, data_out: Path) -> Report:
    report = Report()
    loaded = check_structure(plan, assets_out, data_out, report)
    check_content(plan, loaded, report)
    check_pivot(plan, loaded, report)
    report.stats["clips"] = len(plan.clips)
    report.stats["keys"] = sum(len(r) for r in plan.rows.values())
    return report


def print_report(report: Report, log=print) -> int:
    for w in report.warnings:
        log(f"提示：{w}")
    for e in report.errors[:60]:
        log(f"错误：{e}")
    if len(report.errors) > 60:
        log(f"……另有 {len(report.errors) - 60} 条错误未列出")
    log(f"自检{'通过' if report.ok else '失败'}：{report.stats}，错误 {len(report.errors)}，提示 {len(report.warnings)}")
    return 0 if report.ok else 1
