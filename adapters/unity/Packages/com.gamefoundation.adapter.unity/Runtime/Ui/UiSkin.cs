#nullable enable
// UiSkin：U3-1 UI 套件默认皮肤（任务书"一个 UiSkin：字体 = Noto Sans CJK...、基础色板、九宫格占位
// 面板"）。选择"纯代码默认值"而不是 ScriptableObject 资产——本任务硬性规则 1 要求全程只用
// Unity.exe -batchmode 命令行、不开编辑器 GUI，ScriptableObject 资产通常需要在编辑器里手工创建/
// 赋值；纯代码静态默认值可以被本包内任何运行期代码直接引用，不依赖任何 .asset 文件是否已经在
// Unity 工程里创建好，与 UnityRenderer2D.GetPlaceholderSprite/UnityUISurface 的占位资源生成手法
// 同一思路（运行期用最小代码生成，不预先制作美术资产）。
//
// 判断记录（字体复用 U1 已有方案）：UnityUISurface 已经实现了"Resources.Load<Font>(占位字体) ->
// TMP_FontAsset.CreateFontAsset"这条路径（见该类型顶部"判断记录（TMP 运行期依赖）"），本类型对
// uGUI 套件复用完全相同的资源路径与生成方式（各自独立缓存一份 TMP_FontAsset 实例，互不共享——
// TMP_FontAsset.CreateFontAsset 本身是纯函数式生成，没有"全局唯一实例"的契约要求，两处各自持有
// 一份不产生状态不一致，只是多了一次可忽略的生成开销）。
using System;
using TMPro;
using UnityEngine;

namespace Adapter.Unity.Ui
{
    /// <summary>UI 套件默认皮肤：字体、基础色板、九宫格占位面板精灵。所有值均为运行期可用的
    /// 默认值，具体游戏接入时可整体替换（例如把 <see cref="PanelSprite"/> 换成美术定稿的九宫格
    /// 贴图），本类型不假设任何具体游戏的美术风格。</summary>
    public static class UiSkin
    {
        private const string PlaceholderFontResourcePath = "Fonts/noto_sans_cjk_sc";

        /// <summary>面板背景色（半透明深色，见任务书"基础色板"）。</summary>
        public static readonly Color PanelBackground = new Color(0.09f, 0.10f, 0.13f, 0.90f);

        /// <summary>面板边框/九宫格边缘色。</summary>
        public static readonly Color PanelBorder = new Color(0.42f, 0.46f, 0.55f, 1f);

        public static readonly Color TextColor = new Color(0.93f, 0.94f, 0.96f, 1f);

        public static readonly Color AccentColor = new Color(0.32f, 0.58f, 0.95f, 1f);

        public static readonly Color DisabledColor = new Color(0.42f, 0.42f, 0.45f, 1f);

        public static readonly Color DangerColor = new Color(0.85f, 0.30f, 0.28f, 1f);

        public static readonly Color ButtonIdleColor = new Color(0.20f, 0.22f, 0.27f, 0.95f);

        public static readonly Color ButtonHoverColor = new Color(0.27f, 0.30f, 0.37f, 0.95f);

        private static TMP_FontAsset? _font;

        /// <summary>套件默认字体：Noto Sans CJK（见包 README"已知契约缺口 4"——运行期只能用
        /// 已被 Unity 资产管线导入过的 <see cref="Font"/> 对象生成 TMP 字体资产，本类型复用
        /// <c>UnityUISurface</c> 已验证过的同一条路径；找不到占位字体或生成失败时回退
        /// <see cref="TMP_Settings.defaultFontAsset"/>。</summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (_font != null)
                {
                    return _font;
                }

                _font = TryCreateFontAsset();
                if (_font == null)
                {
                    var fallback = TMP_Settings.defaultFontAsset;
                    if (fallback == null)
                    {
                        throw new InvalidOperationException(
                            "[UiSkin] 找不到任何可用的 TMP 字体资产：占位字体生成失败且 TMP_Settings.defaultFontAsset 未配置");
                    }
                    _font = fallback;
                }
                return _font;
            }
        }

        private static TMP_FontAsset? TryCreateFontAsset()
        {
            var osFont = Resources.Load<UnityEngine.Font>(PlaceholderFontResourcePath);
            if (osFont == null)
            {
                Debug.LogWarning($"[UiSkin] 未找到占位字体资源 Resources/{PlaceholderFontResourcePath}，回退到 TMP 内置默认字体。");
                return null;
            }

            try
            {
                return TMP_FontAsset.CreateFontAsset(osFont);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UiSkin] TMP_FontAsset.CreateFontAsset 抛出异常：{ex.Message}，回退到 TMP 内置默认字体。");
                return null;
            }
        }

        private static Sprite? _panelSprite;

        /// <summary>九宫格占位面板精灵（任务书"九宫格占位面板：assets/_placeholder/ui 若有可用图，
        /// 否则纯色"）：<c>assets/_placeholder</c> 目前没有专用 UI 素材（见 toolchain/
        /// gen_placeholder_assets.py 产出清单，只有生物/图标/地图一类游戏内容占位），本类型运行期
        /// 生成一张 16x16、四周 5px 描边的纯色九宫格纹理并按 <see cref="Sprite.Create"/> 的
        /// <c>border</c> 参数声明九宫格切片，满足"面板有九宫格拉伸边框"这一形态要求，不依赖任何
        /// 预制美术文件。</summary>
        public static Sprite PanelSprite => _panelSprite ??= CreatePanelSprite();

        private static Sprite CreatePanelSprite()
        {
            const int size = 16;
            const int border = 5;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "UiSkinPanelTexture" };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var onEdge = x < border || y < border || x >= size - border || y >= size - border;
                    pixels[y * size + x] = onEdge ? (Color32)PanelBorder : (Color32)PanelBackground;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            var sprite = Sprite.Create(
                texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = "UiSkinPanelSprite";
            return sprite;
        }

        private static Sprite? _flatSprite;

        /// <summary>纯色矩形精灵（按钮/进度条底板等不需要九宫格拉伸的场合）。</summary>
        public static Sprite FlatSprite
        {
            get
            {
                if (_flatSprite == null)
                {
                    var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "UiSkinFlatTexture" };
                    var pixels = new Color32[16];
                    for (var i = 0; i < 16; i++) pixels[i] = Color.white;
                    texture.SetPixels32(pixels);
                    texture.Apply();
                    _flatSprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                    _flatSprite.name = "UiSkinFlatSprite";
                }
                return _flatSprite;
            }
        }
    }
}
