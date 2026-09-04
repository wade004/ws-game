"""``icon`` 子命令：一批图标源图 -> 归一化尺寸后落到 assets/<dataset>/icons/。"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image

from .common import find_repo_root, log, resolve_root

CATEGORY_CHOICES = ["creature", "item", "skill", "aura", "gobj", "projectile", "misc"]


def add_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("src", nargs="+", help="图标源文件（.png），可传多个")
    parser.add_argument("--dataset", default="_sample", help="目标数据集名，默认 _sample")
    parser.add_argument("--category", default="misc", choices=CATEGORY_CHOICES, help="图标类别，默认 misc")
    parser.add_argument("--size", type=int, default=64, help="归一化后的正方形边长（像素），默认 64")
    parser.add_argument("--assets-root", default=None, help="资产根目录，默认仓库 assets/")
    parser.add_argument("--dry-run", action="store_true", help="只打印计划，不写任何文件")


def _fit_square(img: Image.Image, size: int) -> Image.Image:
    w, h = img.size
    scale = size / max(w, h)
    new_w, new_h = max(1, round(w * scale)), max(1, round(h * scale))
    resized = img.resize((new_w, new_h), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(resized, ((size - new_w) // 2, (size - new_h) // 2), resized)
    return canvas


def run(args: argparse.Namespace) -> int:
    repo_root = find_repo_root()
    assets_root = resolve_root(args.assets_root, repo_root, "assets")

    manifest: list[tuple[str, Path]] = []
    for src in args.src:
        src_path = Path(src)
        if not src_path.is_file():
            raise FileNotFoundError(f"图标源文件不存在: {src_path}")
        icon_id = f"icon.{args.category}.{src_path.stem}"
        out_path = assets_root / args.dataset / "icons" / args.category / f"{src_path.stem}.png"
        manifest.append((icon_id, out_path))

        if args.dry_run:
            log(f"计划写出图标: {out_path} (id={icon_id})", dry_run=True)
            continue

        img = Image.open(src_path).convert("RGBA")
        img = _fit_square(img, args.size)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        img.save(out_path)
        log(f"已写出图标: {out_path} (id={icon_id})")

    print(f"[icon] 处理 {len(manifest)} 个图标：")
    for icon_id, out_path in manifest:
        print(f"  {icon_id} -> {out_path}")
    return 0
