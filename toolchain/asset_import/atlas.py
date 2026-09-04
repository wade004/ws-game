"""简单图集打包：按行排布（超过最大宽度换行），不依赖任何第三方打包库。"""

from __future__ import annotations

from PIL import Image


def pack_atlas(
    frames: dict[str, Image.Image], *, max_width: int = 2048
) -> tuple[Image.Image, dict[str, dict[str, int]]]:
    """把一组已命名的帧图打包进一张图集。

    参数:
        frames: 有序字典，key 是帧名（如 ``front/body``），value 是 RGBA 图像。
        max_width: 单行最大像素宽度，超出后换行。

    返回:
        (atlas 图像, {帧名: {x, y, w, h}})，帧矩形坐标为图集内像素坐标。
    """
    if not frames:
        return Image.new("RGBA", (1, 1), (0, 0, 0, 0)), {}

    positions: dict[str, tuple[int, int, int, int]] = {}
    x = y = row_height = 0
    atlas_width = 0

    for key, img in frames.items():
        w, h = img.size
        if x > 0 and x + w > max_width:
            y += row_height
            x = 0
            row_height = 0
        positions[key] = (x, y, w, h)
        x += w
        row_height = max(row_height, h)
        atlas_width = max(atlas_width, x)

    atlas_height = y + row_height
    atlas = Image.new("RGBA", (max(atlas_width, 1), max(atlas_height, 1)), (0, 0, 0, 0))
    frame_rects: dict[str, dict[str, int]] = {}
    for key, img in frames.items():
        px, py, w, h = positions[key]
        atlas.paste(img, (px, py), img)
        frame_rects[key] = {"x": px, "y": py, "w": w, "h": h}

    return atlas, frame_rects
