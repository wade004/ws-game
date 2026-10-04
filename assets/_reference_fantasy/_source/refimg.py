# -*- coding: utf-8 -*-
"""参考皮肤包的后处理图像库（numpy + scipy + cv2；只在本地出包时用，门禁不跑）。

约定：图像一律是 float32 的 ``[H, W, 4]``（RGBA，0..255，直通 alpha）。
- alpha 边缘清理：``clean_alpha`` 去脏 alpha、``defringe`` 用最近的实心像素颜色覆盖半透明边缘（去紫边/黑边）；
- 缩放一律在预乘 alpha 空间做（``resize_premul``），避免透明像素的颜色渗进边缘；
- 对称化：``mirror_quad``（四向镜像，框用）、``mirror_lr``（左右镜像，图标/人物层用）；
- 九宫格：``nine_slice_from_art`` 从一张出图装配出"边框像素 = 令牌值、边是沿拉伸方向恒定的、中心是平滑色"的九宫格底图。
"""
from __future__ import annotations

from pathlib import Path

import cv2
import numpy as np
from PIL import Image
from scipy import ndimage as ndi


# ---------------------------------------------------------------------------
# 读写
# ---------------------------------------------------------------------------

def load_rgba(path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGBA"), dtype=np.float32).copy()


def save_png(img: np.ndarray, path) -> None:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    arr = np.clip(np.rint(img), 0, 255).astype(np.uint8)
    arr[arr[..., 3] == 0, :3] = 0                                # 全透明像素的 RGB 归零（不残留底色，再采样/打包不渗边）
    Image.fromarray(arr, "RGBA").save(path, format="PNG", optimize=True)


# ---------------------------------------------------------------------------
# alpha 与边缘
# ---------------------------------------------------------------------------

def clean_alpha(img: np.ndarray, lo: float = 10.0, hi: float = 245.0) -> np.ndarray:
    """alpha 低于 lo 当脏点清零、高于 hi 当实心拉满（出图模型会留 1~9 的底噪 alpha 与 250~254 的近实心，影响"透明像素/全透明"判定）。"""
    out = img.copy()
    a = out[..., 3]
    a[a < lo] = 0.0
    a[a > hi] = 255.0
    return out


def defringe(img: np.ndarray, solid: float = 250.0) -> np.ndarray:
    """半透明边缘像素的颜色改成最近的实心像素的颜色（保留 alpha）：去掉出图模型留在边缘里的底色（紫边/黑边）。"""
    a = img[..., 3]
    mask = a >= solid
    if not mask.any() or mask.all():
        return img.copy()
    idx = ndi.distance_transform_edt(~mask, return_distances=False, return_indices=True)
    out = img.copy()
    filled = img[idx[0], idx[1], :3]
    edge = ~mask
    out[edge, :3] = filled[edge]
    return out


def erode_alpha(img: np.ndarray, px: float = 1.0) -> np.ndarray:
    """alpha 向内收 px 像素（去掉最外圈半透明晕）。"""
    out = img.copy()
    a = out[..., 3] > 127
    d = ndi.distance_transform_edt(a)
    out[..., 3] = out[..., 3] * np.clip(d - px + 1.0, 0.0, 1.0)
    return out


def alpha_bbox(img: np.ndarray, thr: float = 16.0):
    ys, xs = np.where(img[..., 3] > thr)
    if len(xs) == 0:
        return None
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def autocrop(img: np.ndarray, thr: float = 16.0) -> np.ndarray:
    box = alpha_bbox(img, thr)
    if box is None:
        return img
    x0, y0, x1, y1 = box
    return img[y0:y1, x0:x1].copy()


def pad_to(img: np.ndarray, w: int, h: int) -> np.ndarray:
    """透明填充到 w x h（原图居中）。"""
    ih, iw = img.shape[:2]
    out = np.zeros((h, w, 4), dtype=np.float32)
    x0, y0 = (w - iw) // 2, (h - ih) // 2
    out[y0:y0 + ih, x0:x0 + iw] = img
    return out


def pad_square(img: np.ndarray) -> np.ndarray:
    h, w = img.shape[:2]
    s = max(w, h)
    return pad_to(img, s, s)


# ---------------------------------------------------------------------------
# 缩放（预乘 alpha）
# ---------------------------------------------------------------------------

def premultiply(img: np.ndarray) -> np.ndarray:
    out = img.copy()
    out[..., :3] *= (img[..., 3:4] / 255.0)
    return out


def unpremultiply(img: np.ndarray) -> np.ndarray:
    out = img.copy()
    a = img[..., 3:4]
    safe = np.where(a > 0.5, a, 1.0)
    out[..., :3] = np.where(a > 0.5, img[..., :3] * 255.0 / safe, 0.0)
    return out


def resize_premul(img: np.ndarray, w: int, h: int) -> np.ndarray:
    ih, iw = img.shape[:2]
    interp = cv2.INTER_AREA if (w < iw or h < ih) else cv2.INTER_LANCZOS4
    pm = premultiply(img)
    out = cv2.resize(pm, (w, h), interpolation=interp)
    out = unpremultiply(np.clip(out, 0, 255))
    return np.clip(out, 0, 255)


def fit_into(img: np.ndarray, w: int, h: int, max_w: int, max_h: int) -> np.ndarray:
    """按比例缩进 max_w x max_h，再居中放进 w x h 的透明画布。"""
    ih, iw = img.shape[:2]
    k = min(max_w / iw, max_h / ih)
    nw, nh = max(1, int(round(iw * k))), max(1, int(round(ih * k)))
    return pad_to(resize_premul(img, nw, nh), w, h)


def principal_angle_deg(img: np.ndarray, thr: float = 64.0) -> float:
    """主体主轴与"竖直向上"的夹角（度，逆时针为正）：取主轴朝上的那一端。"""
    ys, xs = np.nonzero(img[..., 3] > thr)
    x, y = xs - xs.mean(), -(ys - ys.mean())                  # y 向上
    cov = np.cov(np.stack([x, y]))
    w, v = np.linalg.eigh(cov)
    vx, vy = v[:, int(np.argmax(w))]
    if vy < 0:
        vx, vy = -vx, -vy
    return float(np.degrees(np.arctan2(vy, vx)) - 90.0) * -1.0   # 需要逆时针旋转多少度才能让主轴竖直（朝上）


def rotate_premul(img: np.ndarray, deg: float) -> np.ndarray:
    """逆时针旋转 deg 度，画布按旋转后的包围盒扩大，预乘 alpha 空间插值。"""
    h, w = img.shape[:2]
    c, s = abs(np.cos(np.radians(deg))), abs(np.sin(np.radians(deg)))
    nw, nh = int(np.ceil(w * c + h * s)), int(np.ceil(w * s + h * c))
    m = cv2.getRotationMatrix2D((w / 2, h / 2), deg, 1.0)
    m[0, 2] += nw / 2 - w / 2
    m[1, 2] += nh / 2 - h / 2
    out = cv2.warpAffine(premultiply(img), m, (nw, nh), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_CONSTANT, borderValue=(0, 0, 0, 0))
    return np.clip(unpremultiply(np.clip(out, 0, 255)), 0, 255)


# ---------------------------------------------------------------------------
# 对称
# ---------------------------------------------------------------------------

def mirror_quad(img: np.ndarray) -> np.ndarray:
    """取左上象限，镜像成四向对称（尺寸须为偶数，奇数则裁掉最后一行/列）。"""
    h, w = img.shape[:2]
    h, w = h - h % 2, w - w % 2
    q = img[:h // 2, :w // 2]
    top = np.concatenate([q, q[:, ::-1]], axis=1)
    return np.concatenate([top, top[::-1]], axis=0)


def mirror_diag(img: np.ndarray) -> np.ndarray:
    """沿主对角线对称（正方形图）：上边与左边、下边与右边的纹样一致（出图模型常把横竖边画成不同粗细，九宫格拉伸后会一边粗一边细）。"""
    h, w = img.shape[:2]
    n = min(h, w)
    out = img[:n, :n].copy()
    t = np.transpose(out, (1, 0, 2))
    upper = np.triu(np.ones((n, n), bool))[..., None]
    return np.where(upper, out, t)


def mirror_lr(img: np.ndarray) -> np.ndarray:
    h, w = img.shape[:2]
    w = w - w % 2
    half = img[:, :w // 2]
    return np.concatenate([half, half[:, ::-1]], axis=1)


def flip_lr(img: np.ndarray) -> np.ndarray:
    return img[:, ::-1].copy()


# ---------------------------------------------------------------------------
# 形状与色彩
# ---------------------------------------------------------------------------

def rounded_mask(w: int, h: int, radius: float, ss: int = 4) -> np.ndarray:
    """圆角矩形 alpha（0..1，超采样抗锯齿）。"""
    big = Image.new("L", (w * ss, h * ss), 0)
    from PIL import ImageDraw

    ImageDraw.Draw(big).rounded_rectangle([0, 0, w * ss - 1, h * ss - 1], radius=radius * ss, fill=255)
    return np.asarray(big.resize((w, h), Image.LANCZOS), dtype=np.float32) / 255.0


def apply_mask(img: np.ndarray, mask: np.ndarray) -> np.ndarray:
    out = img.copy()
    out[..., 3] = out[..., 3] * mask
    return out


def luminance(rgb: np.ndarray) -> np.ndarray:
    return rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114


def adjust(img: np.ndarray, *, brightness: float = 1.0, saturation: float = 1.0, tint=None, tint_amount: float = 0.0) -> np.ndarray:
    out = img.copy()
    rgb = out[..., :3]
    lum = luminance(rgb)[..., None]
    rgb = lum + (rgb - lum) * saturation
    rgb = rgb * brightness
    if tint is not None and tint_amount > 0:
        rgb = rgb * (1 - tint_amount) + np.array(tint, dtype=np.float32) * tint_amount
    out[..., :3] = np.clip(rgb, 0, 255)
    return out


def inner_rim(alpha_mask: np.ndarray, width: float, blur: float) -> np.ndarray:
    """沿 alpha 边缘向内的光晕强度（0..1）：边缘处最强，向内按高斯衰减。"""
    inside = (alpha_mask > 0.5)
    d = ndi.distance_transform_edt(inside)
    rim = np.exp(-(np.clip(d - 1.0, 0, None) ** 2) / (2.0 * max(width, 0.5) ** 2))
    rim = np.where(inside, rim, 0.0)
    if blur > 0:
        rim = ndi.gaussian_filter(rim, blur)
    return np.clip(rim, 0, 1).astype(np.float32)


def add_glow(img: np.ndarray, color, strength: float, width: float, blur: float = 0.8) -> np.ndarray:
    """沿框内缘叠一圈指定颜色的光（颜色按 strength 加色混合，alpha 不变）。"""
    rim = inner_rim(img[..., 3] / 255.0, width, blur)[..., None] * strength
    out = img.copy()
    out[..., :3] = np.clip(out[..., :3] * (1 - rim) + np.array(color, dtype=np.float32) * rim, 0, 255)
    return out


def reslice(img: np.ndarray, border: int) -> np.ndarray:
    """让九宫格底图的边带沿拉伸方向恒定：边带取正中间那一行/列的值铺满（状态派生的光晕在角附近有渐变，拉伸时会被放大）。"""
    out = img.copy()
    h, w = out.shape[:2]
    mx, my = w // 2, h // 2
    out[:border, border:w - border] = img[:border, mx:mx + 1]
    out[h - border:, border:w - border] = img[h - border:, mx:mx + 1]
    out[border:h - border, :border] = img[my:my + 1, :border]
    out[border:h - border, w - border:] = img[my:my + 1, w - border:]
    out[border:h - border, border:w - border] = img[my:my + 1, mx:mx + 1]
    return out


def alpha_over(base: np.ndarray, top: np.ndarray, opacity: float = 1.0) -> np.ndarray:
    """top 盖在 base 上（直通 alpha 合成），两者同尺寸。"""
    ta = (top[..., 3:4] / 255.0) * opacity
    ba = base[..., 3:4] / 255.0
    oa = ta + ba * (1 - ta)
    safe = np.where(oa > 1e-6, oa, 1.0)
    rgb = (top[..., :3] * ta + base[..., :3] * ba * (1 - ta)) / safe
    out = np.concatenate([rgb, oa * 255.0], axis=-1)
    out[..., :3] = np.where(oa > 1e-6, out[..., :3], 0.0)
    return np.clip(out, 0, 255)


def tint_mono(img: np.ndarray, color, opacity: float = 1.0) -> np.ndarray:
    """单色化：保留 alpha（乘 opacity），RGB 取 color（单色剪影图章用）。"""
    out = img.copy()
    out[..., :3] = np.array(color, dtype=np.float32)
    out[..., 3] = out[..., 3] * opacity
    return out


# ---------------------------------------------------------------------------
# 九宫格装配
# ---------------------------------------------------------------------------

def _median_profile(strip: np.ndarray, axis: int) -> np.ndarray:
    """strip 沿 axis 取中位数，得到一维剖面（通道在最后一维）。"""
    return np.median(strip, axis=axis)


def nine_slice_from_art(art: np.ndarray, out_w: int, out_h: int, border: int, *, corner_frac: float = 0.2,
                        edge_band: float = 0.04, center_alpha: float | None = None, center_blur: float = 0.0) -> np.ndarray:
    """从一张（已对称化的）面板/按钮出图装配九宫格底图。

    - 四个角：取出图左上 ``corner_frac`` 的方块缩到 ``border x border``，镜像出其余三角；
    - 四条边：取出图上边中段一个窄带，沿拉伸方向取中位数得到一维剖面（边在拉伸方向上恒定，拉伸不会糊开花纹），缩到 ``border`` 厚，镜像出其余三边；
    - 中心：取出图中心区域的中位数色（可选小幅高斯平滑的渐变），整幅平铺。
    结果的边框恰好 ``border`` 像素：theme 数值令牌与图一致；边与中心沿拉伸方向恒定，任意尺寸拉伸四角不变形。
    """
    ah, aw = art.shape[:2]
    cw = ch = max(2, int(min(aw, ah) * corner_frac))      # 角取正方形（按较短边算），缩到 border x border 不变形
    if cw * 2 > aw or ch * 2 > ah:
        raise ValueError("corner_frac 太大")
    corner = resize_premul(art[:ch, :cw], border, border)

    # 上边剖面：中段窄带，沿 x 取中位数 -> [ch, 4]，缩到 border 厚
    bx0 = int(aw * (0.5 - edge_band)); bx1 = max(bx0 + 1, int(aw * (0.5 + edge_band)))
    top_profile = _median_profile(art[:ch, bx0:bx1], axis=1)            # [ch, 4]
    top = cv2.resize(premultiply(top_profile[:, None, :]), (1, border), interpolation=cv2.INTER_AREA)[:, 0, :]
    top = unpremultiply(top[:, None, :])[:, 0, :]
    # 左边剖面：中段窄带，沿 y 取中位数 -> [cw, 4]，缩到 border 厚
    by0 = int(ah * (0.5 - edge_band)); by1 = max(by0 + 1, int(ah * (0.5 + edge_band)))
    left_profile = _median_profile(art[by0:by1, :cw], axis=0)           # [cw, 4]
    left = cv2.resize(premultiply(left_profile[None, :, :]), (border, 1), interpolation=cv2.INTER_AREA)[0, :, :]
    left = unpremultiply(left[None, :, :])[0, :, :]

    # 中心色
    cx0, cx1 = int(aw * 0.35), int(aw * 0.65)
    cy0, cy1 = int(ah * 0.35), int(ah * 0.65)
    center_patch = art[cy0:cy1, cx0:cx1]
    center_col = np.median(center_patch.reshape(-1, 4), axis=0)
    if center_alpha is not None:
        center_col[3] = center_alpha

    out = np.zeros((out_h, out_w, 4), dtype=np.float32)
    out[:, :] = center_col
    # 边
    out[0:border, border:out_w - border] = top[:, None, :].transpose(0, 1, 2)
    out[out_h - border:out_h, border:out_w - border] = top[::-1][:, None, :]
    out[border:out_h - border, 0:border] = left[None, :, :]
    out[border:out_h - border, out_w - border:out_w] = left[::-1][None, :, :]
    # 角
    out[0:border, 0:border] = corner
    out[0:border, out_w - border:out_w] = corner[:, ::-1]
    out[out_h - border:out_h, 0:border] = corner[::-1, :]
    out[out_h - border:out_h, out_w - border:out_w] = corner[::-1, ::-1]
    if center_blur > 0:
        inner = out[border:out_h - border, border:out_w - border]
        out[border:out_h - border, border:out_w - border] = ndi.gaussian_filter(inner, (center_blur, center_blur, 0))
    return out


def dominant_color(img: np.ndarray, box=None) -> tuple[int, int, int]:
    """区域内不透明像素的中位数颜色（box = (x0, y0, x1, y1)，缺省取中心 30%）。"""
    h, w = img.shape[:2]
    if box is None:
        box = (int(w * 0.35), int(h * 0.35), int(w * 0.65), int(h * 0.65))
    x0, y0, x1, y1 = box
    patch = img[y0:y1, x0:x1].reshape(-1, 4)
    patch = patch[patch[:, 3] > 128]
    if len(patch) == 0:
        return (0, 0, 0)
    med = np.median(patch[:, :3], axis=0)
    return int(med[0]), int(med[1]), int(med[2])
