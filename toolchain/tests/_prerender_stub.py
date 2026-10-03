"""蒙皮预渲染驱动器的测试桩后端：不启动引擎，用 std_dummy_poses 的几何人偶渲染"同形"的裸 RGBA 文件。

用途：在 pytest 里覆盖驱动器全链路（计划 -> 组装 -> 自检 -> 数据校验），引擎侧的真实渲染由 PlayMode 用例
（SkinPrerenderPlayModeTests）与命令行端到端实跑覆盖。装备层在桩里用棋盘格把整身切成两半（逐像素互补），
所以层对齐检查"按构造成立"；要测对齐检查能抓错，由用例事后改写某层的像素。
"""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

from std_dummy_model_clips import config as MC  # noqa: E402
from std_dummy_poses import config as SC  # noqa: E402
from std_dummy_poses.poses import pose_at  # noqa: E402
from std_dummy_poses.skeleton import render  # noqa: E402


def _clip_by_source_name() -> dict:
    out = {}
    for mass in (None, *SC.MASS_TIERS):
        for c in (SC.build_clip_defs() if mass is None else SC.mass_clip_defs(mass)):
            if not c.alias_of:
                out[MC.clip_state_name(c.key, c.mass)] = c
    return out


def _checker(size: tuple, parity: int) -> Image.Image:
    w, h = size
    m = Image.new("L", size, 0)
    px = m.load()
    for y in range(h):
        for x in range(w):
            if (x + y) % 2 == parity:
                px[x, y] = 255
    return m


def make_stub_backend(refuse_bones=None, calls=None):
    """返回后端函数。``refuse_bones``：给定则返回"缺骨骼"拒绝结果；``calls``：列表，记录每次收到的作业。"""
    by_name = _clip_by_source_name()

    def backend(job: dict, work_dir: Path) -> dict:
        if calls is not None:
            calls.append(job)
        if refuse_bones is not None:
            return {"status": "refused", "message": "蒙皮不符合标准骨骼命名", "missing_bones": list(refuse_bones),
                    "duplicate_bones": [], "bad_selectors": [], "overlapping_renderers": [], "missing_clips": []}
        size = (job["canvas_w"], job["canvas_h"])
        out_dir = Path(job["out_dir"])
        out_dir.mkdir(parents=True, exist_ok=True)
        checker0, checker1 = _checker(size, 0), _checker(size, 1)
        layer_names = [l["name"] for l in job["layers"]]
        variants = ["all"] + (["body"] + layer_names if layer_names else [])
        files = []
        for cj in job["clips"]:
            c = by_name[cj["stem"]]
            weapon = c.family if c.has_weapon else None
            for slot in job["slots"]:
                images = {v: [] for v in variants}
                for ms in cj["sample_ms"]:
                    comp = render(pose_at(c, ms), slot["yaw_deg"], weapon)
                    images["all"].append(comp)
                    if layer_names:
                        body = Image.new("RGBA", size, (0, 0, 0, 0))
                        body.paste(comp, mask=checker1)
                        images["body"].append(body)
                        for i, n in enumerate(layer_names):
                            lay = Image.new("RGBA", size, (0, 0, 0, 0))
                            # 第一个装备层取棋盘格偶数位；多于一个装备层时其余层取空（桩只模拟一个装备层）
                            if i == 0:
                                lay.paste(comp, mask=checker0)
                            images[n].append(lay)
                for v, frames in images.items():
                    name = f"{cj['stem']}__{slot['name']}__{v}.rgba"
                    (out_dir / name).write_bytes(b"".join(f.tobytes() for f in frames))
                    files.append({"file": name, "stem": cj["stem"], "slot": slot["name"], "layer": v, "frames": len(frames)})
        return {"status": "ok", "message": "", "files": files, "unity_version": "stub", "graphics_device": "stub"}

    return backend
