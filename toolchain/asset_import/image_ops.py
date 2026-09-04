"""图像处理：抠图（matting）与裁边（trim）。只用 Pillow，不依赖其他第三方图像库。"""

from __future__ import annotations

from pathlib import Path

from PIL import Image

from .common import AssetImportError, parse_hex_color


def parse_matting_spec(spec: str) -> tuple[str, str | None]:
    """解析 ``--matting`` 取值，返回 (mode, extra)：

    - ``none`` -> ("none", None)
    - ``rembg`` -> ("rembg", None)
    - ``colorkey:#RRGGBB`` -> ("colorkey", "#RRGGBB")
    """
    if spec == "none":
        return "none", None
    if spec == "rembg":
        return "rembg", None
    if spec.startswith("colorkey:"):
        return "colorkey", spec.split(":", 1)[1]
    raise AssetImportError(f"未知 --matting 取值 '{spec}'，应为 none|rembg|colorkey:#RRGGBB")


def _rembg_weights_available() -> bool:
    """检测本地是否已有 rembg (u2net) 权重，避免首次运行触发联网下载。"""
    weights_dir = Path.home() / ".u2net"
    if not weights_dir.is_dir():
        return False
    return any(p.is_file() and p.stat().st_size > 0 for p in weights_dir.glob("*.onnx"))


def apply_matting(img: Image.Image, mode: str, extra: str | None, *, tolerance: int = 32) -> Image.Image:
    """对图像做抠图，返回带透明通道的新图（不修改入参）。"""
    img = img.convert("RGBA")

    if mode == "none":
        return img

    if mode == "colorkey":
        if not extra:
            raise AssetImportError("colorkey 抠图需要 #RRGGBB 颜色值，如 --matting colorkey:#00FF00")
        r, g, b = parse_hex_color(extra)
        pixels = img.load()
        w, h = img.size
        for y in range(h):
            for x in range(w):
                pr, pg, pb, pa = pixels[x, y]
                if abs(pr - r) <= tolerance and abs(pg - g) <= tolerance and abs(pb - b) <= tolerance:
                    pixels[x, y] = (pr, pg, pb, 0)
        return img

    if mode == "rembg":
        if not _rembg_weights_available():
            raise AssetImportError(
                "--matting rembg 需要本地已有 u2net 权重（~/.u2net/*.onnx），本工具不会自动联网下载模型；"
                "请手动准备好权重后重试，或改用 --matting none / --matting colorkey:#RRGGBB。"
            )
        import io

        import rembg

        buf = io.BytesIO()
        img.save(buf, format="PNG")
        result = rembg.remove(buf.getvalue())
        return Image.open(io.BytesIO(result)).convert("RGBA")

    raise AssetImportError(f"未知抠图模式 '{mode}'")


def trim_transparent(img: Image.Image) -> tuple[Image.Image, tuple[int, int]]:
    """裁掉四周完全透明的边，返回 (裁剪后的图, (left, top) 偏移)。

    若图像整体全透明（bbox 为空），原样返回、偏移 (0, 0)。
    """
    img = img.convert("RGBA")
    alpha = img.split()[-1]
    bbox = alpha.getbbox()
    if bbox is None:
        return img, (0, 0)
    left, top, right, bottom = bbox
    cropped = img.crop(bbox)
    return cropped, (left, top)


def flip_horizontal(img: Image.Image) -> Image.Image:
    return img.transpose(Image.FLIP_LEFT_RIGHT)
