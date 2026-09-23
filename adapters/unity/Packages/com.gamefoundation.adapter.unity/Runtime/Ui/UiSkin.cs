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
    /// 贴图），本类型不假设任何具体游戏的美术风格。
    /// <para>
    /// 判断记录（ADR-0082，皮肤覆盖注入点开在本类型而不是 <see cref="UiWidgets"/> 的方法签名上）：
    /// 消费方反馈第二十七批（阻塞）——<c>Runtime/Ui/Panels/</c> 下的面板均为 <c>public sealed
    /// class</c>（不可继承覆写），其构造过程内部直接调用 <see cref="UiWidgets"/> 的
    /// <c>CreatePanelBackground</c>/<c>CreateButton</c>/<c>CreateLabel</c> 静态方法，消费方代码
    /// 从未参与这几次调用，给这些方法加"可选覆盖参数"这条路径消费方永远传不到——唯一能拦到"面板
    /// 建出来时用什么颜色/精灵/字体"这件事的位置，只有这几个方法内部读取的 <see cref="UiSkin"/>
    /// 本身。因此本类型新增一个可安装的"当前皮肤覆盖"（<see cref="UiSkinOverride"/>，逐项可空），
    /// 全部公开属性改为"覆盖值优先，否则退回原有默认值"，<see cref="UiWidgets"/> 与各面板文件不
    /// 需要任何改动即可自动生效。</para>
    /// <para>
    /// 判断记录（不缓存覆盖结果，且不影响默认值自身既有的 <c>??=</c> 惰性缓存）：<see cref="PanelSprite"/>/
    /// <see cref="FlatSprite"/>/<see cref="Font"/> 的默认值本就是运行期现造的对象，用 <c>??=</c>
    /// 惰性缓存避免重复生成——这份缓存只服务于"默认值"分支，<see cref="Install"/> 之后的读取
    /// 每次都先判 <see cref="_active"/> 是否非空，命中覆盖直接返回覆盖对象本身（覆盖对象由调用方
    /// 一次性构造好、本类型不做二次缓存），不会被默认值那份缓存吃掉；即便调用方在 <see cref="Install"/>
    /// 之前已经访问过一次触发默认值缓存生成，之后安装覆盖仍然对新建的控件生效（消费方反馈原文
    /// 场景）。已经建好的面板不会被回溯重建——这是与"运行期只读取，不做全局重刷"一致的显式取舍，
    /// 见 ADR-0082。</para>
    /// </summary>
    public static class UiSkin
    {
        private const string PlaceholderFontResourcePath = "Fonts/noto_sans_cjk_sc";

        private static readonly Color DefaultPanelBackground = new Color(0.09f, 0.10f, 0.13f, 0.90f);
        private static readonly Color DefaultPanelBorder = new Color(0.42f, 0.46f, 0.55f, 1f);
        private static readonly Color DefaultTextColor = new Color(0.93f, 0.94f, 0.96f, 1f);
        private static readonly Color DefaultAccentColor = new Color(0.32f, 0.58f, 0.95f, 1f);
        private static readonly Color DefaultDisabledColor = new Color(0.42f, 0.42f, 0.45f, 1f);
        private static readonly Color DefaultDangerColor = new Color(0.85f, 0.30f, 0.28f, 1f);
        private static readonly Color DefaultButtonIdleColor = new Color(0.20f, 0.22f, 0.27f, 0.95f);
        private static readonly Color DefaultButtonHoverColor = new Color(0.27f, 0.30f, 0.37f, 0.95f);

        /// <summary>当前安装的皮肤覆盖；<c>null</c> 表示未安装，全部属性回退默认值。</summary>
        private static UiSkinOverride? _active;

        /// <summary>安装一套皮肤覆盖：逐项可空，某项为空则该项继续用框架默认值（行为与未安装时
        /// 逐字节一致）。只影响安装之后新建的控件，已经建好的面板不回溯重建（见类型顶部判断
        /// 记录）。</summary>
        public static void Install(UiSkinOverride skin) => _active = skin ?? throw new ArgumentNullException(nameof(skin));

        /// <summary>卸载当前皮肤覆盖，确定性地恢复框架默认（供用例与消费方复位使用）。</summary>
        public static void Reset() => _active = null;

        /// <summary>当前是否已安装皮肤覆盖（诊断/测试用只读查询）。</summary>
        public static bool IsOverrideInstalled => _active != null;

        /// <summary>面板背景色（半透明深色，见任务书"基础色板"）。</summary>
        public static Color PanelBackground => _active?.PanelBackground ?? DefaultPanelBackground;

        /// <summary>面板边框/九宫格边缘色。</summary>
        public static Color PanelBorder => _active?.PanelBorder ?? DefaultPanelBorder;

        public static Color TextColor => _active?.TextColor ?? DefaultTextColor;

        public static Color AccentColor => _active?.AccentColor ?? DefaultAccentColor;

        public static Color DisabledColor => _active?.DisabledColor ?? DefaultDisabledColor;

        public static Color DangerColor => _active?.DangerColor ?? DefaultDangerColor;

        public static Color ButtonIdleColor => _active?.ButtonIdleColor ?? DefaultButtonIdleColor;

        public static Color ButtonHoverColor => _active?.ButtonHoverColor ?? DefaultButtonHoverColor;

        private static TMP_FontAsset? _defaultFont;

        /// <summary>套件默认字体：Noto Sans CJK（见包 README"已知契约缺口 4"——运行期只能用
        /// 已被 Unity 资产管线导入过的 <see cref="Font"/> 对象生成 TMP 字体资产，本类型复用
        /// <c>UnityUISurface</c> 已验证过的同一条路径；找不到占位字体或生成失败时回退
        /// <see cref="TMP_Settings.defaultFontAsset"/>。安装了皮肤覆盖且提供了
        /// <see cref="UiSkinOverride.Font"/> 时优先返回覆盖字体，不触发本类型自己的默认字体生成
        /// 逻辑。</summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (_active?.Font != null)
                {
                    return _active.Font;
                }

                if (_defaultFont != null)
                {
                    return _defaultFont;
                }

                _defaultFont = TryCreateFontAsset();
                if (_defaultFont == null)
                {
                    var fallback = TMP_Settings.defaultFontAsset;
                    if (fallback == null)
                    {
                        throw new InvalidOperationException(
                            "[UiSkin] 找不到任何可用的 TMP 字体资产：占位字体生成失败且 TMP_Settings.defaultFontAsset 未配置");
                    }
                    _defaultFont = fallback;
                }
                return _defaultFont;
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

        private static Sprite? _defaultPanelSprite;

        /// <summary>九宫格占位面板精灵（任务书"九宫格占位面板：assets/_placeholder/ui 若有可用图，
        /// 否则纯色"）：<c>assets/_placeholder</c> 目前没有专用 UI 素材（见 toolchain/
        /// gen_placeholder_assets.py 产出清单，只有生物/图标/地图一类游戏内容占位），本类型运行期
        /// 生成一张 16x16、四周 5px 描边的纯色九宫格纹理并按 <see cref="Sprite.Create"/> 的
        /// <c>border</c> 参数声明九宫格切片，满足"面板有九宫格拉伸边框"这一形态要求，不依赖任何
        /// 预制美术文件。安装了皮肤覆盖且提供了 <see cref="UiSkinOverride.PanelSprite"/> 时优先
        /// 返回覆盖精灵。</summary>
        public static Sprite PanelSprite => _active?.PanelSprite ?? (_defaultPanelSprite ??= CreateDefaultPanelSprite());

        /// <summary>判断记录：默认九宫格精灵固定烘入 <see cref="DefaultPanelBackground"/>/
        /// <see cref="DefaultPanelBorder"/> 两个原始默认色，不读可能已安装的颜色覆盖
        /// （<see cref="PanelBackground"/>/<see cref="PanelBorder"/> 属性）——"某一项未被覆盖时行为
        /// 与改动前逐字节一致"是本次改动的硬约束（ADR-0082），若这里改读覆盖后的颜色属性，会让
        /// "只覆盖颜色、不覆盖 <see cref="UiSkinOverride.PanelSprite"/>"这一常见的部分覆盖场景下，
        /// 默认精灵的生成结果随之漂移，不再是"未覆盖项＝今天的默认值"。</summary>
        private static Sprite CreateDefaultPanelSprite()
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
                    pixels[y * size + x] = onEdge ? (Color32)DefaultPanelBorder : (Color32)DefaultPanelBackground;
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

        private static Sprite? _defaultFlatSprite;

        /// <summary>纯色矩形精灵（按钮/进度条底板等不需要九宫格拉伸的场合）。安装了皮肤覆盖且
        /// 提供了 <see cref="UiSkinOverride.FlatSprite"/> 时优先返回覆盖精灵。</summary>
        public static Sprite FlatSprite
        {
            get
            {
                if (_active?.FlatSprite != null)
                {
                    return _active.FlatSprite;
                }

                if (_defaultFlatSprite == null)
                {
                    var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "UiSkinFlatTexture" };
                    var pixels = new Color32[16];
                    for (var i = 0; i < 16; i++) pixels[i] = Color.white;
                    texture.SetPixels32(pixels);
                    texture.Apply();
                    _defaultFlatSprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                    _defaultFlatSprite.name = "UiSkinFlatSprite";
                }
                return _defaultFlatSprite;
            }
        }
    }

    /// <summary>一套可安装的皮肤覆盖：逐项可空，未赋值的项由 <see cref="UiSkin"/> 继续提供框架
    /// 默认值（见 <see cref="UiSkin"/> 类型顶部判断记录，ADR-0082）。本类型只是一份纯数据持有者，
    /// 不做任何缓存/校验——调用方按需构造好整份覆盖后一次性 <see cref="UiSkin.Install"/>。</summary>
    public sealed class UiSkinOverride
    {
        public Color? PanelBackground { get; set; }
        public Color? PanelBorder { get; set; }
        public Color? TextColor { get; set; }
        public Color? AccentColor { get; set; }
        public Color? DisabledColor { get; set; }
        public Color? DangerColor { get; set; }
        public Color? ButtonIdleColor { get; set; }
        public Color? ButtonHoverColor { get; set; }

        /// <summary>覆盖九宫格面板精灵；未赋值时 <see cref="UiSkin.PanelSprite"/> 回退框架默认
        /// 生成的占位九宫格精灵。</summary>
        public Sprite? PanelSprite { get; set; }

        /// <summary>覆盖纯色矩形精灵；未赋值时 <see cref="UiSkin.FlatSprite"/> 回退框架默认生成的
        /// 占位精灵。</summary>
        public Sprite? FlatSprite { get; set; }

        /// <summary>覆盖套件字体；未赋值时 <see cref="UiSkin.Font"/> 回退框架默认字体生成/回退
        /// 逻辑（见该属性判断记录）。</summary>
        public TMP_FontAsset? Font { get; set; }
    }
}
