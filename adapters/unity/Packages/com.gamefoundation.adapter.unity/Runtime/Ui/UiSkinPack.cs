#nullable enable
// UiSkinPack：界面皮肤包的运行期读取（手感设计/08 第 3/4/6 节、ADR-0123 的文件布局、ADR-0149 的运行期消费）。
//
// 皮肤包是资产目录 assets/<数据集>/ui/skin/<名>/，同步进 StreamingAssets/GameFoundation/ui/skin/<名>/（toolchain/resource_layout_map.json
// 的 ui 映射）；ui_layout_definition.skin_ref = skin.<名>，缺省 skin.default（框架占位皮肤）。文件布局的权威是 toolchain/asset_import/skin_pack.py，
// 本类只按同一份布局读：槽位框 slot_frame/<槽位名>.png、品质框 quality_frame/<品质名>.png、槽位框三态 slot_frame/_highlight|_disabled|_drag_hover.png、
// 纸娃娃预览区 paperdoll_preview/background.png、可选的面板底图 panel/background.png、主题 theme.json。游戏换美术只换这些文件，不写界面代码。
//
// 判断记录（回落链与导入校验一致）：皮肤包缺项不阻断——槽位框/品质框取"本包同名 → 占位皮肤同名 → 占位皮肤 _default.png"，其余项取"本包 → 占位皮肤同名"；
// 占位皮肤自己也缺时用程序生成的纯色框兜底（界面永远画得出来，且 <see cref="Fallbacks"/> 记下每一次回落与原因，导入校验的静态报告对应同一批项）。
// 判断记录（缺省皮肤逐位不变）：skin.default 不安装任何 UiSkin 覆盖——面板底图、按钮、字体、配色仍是 UiSkin 的原有默认值；占位包里的 theme.json 只是
// 占位配色的文档，不参与运行期配色。非缺省皮肤才把 theme.json 的 colors 映射成 UiSkinOverride 的配色（panel→面板底色、panel_edge→边框色、text、highlight→强调色、
// disabled、blocked→危险色），并在有 panel/background.png 时换九宫格面板精灵（border 取 theme.json 的 panel_border，缺省 5 像素）。
// 判断记录（同步读文件）：皮肤包的图都很小（几十像素的框与一张预览底图），按需首次取用时同步读文件解码并缓存；图标与纸娃娃层才是量大的资源，走适配器自己的
// 资源加载器（异步、按帧预算，见 UiVisuals）。
using System;
using System.Collections.Generic;
using System.IO;
using Core.Foundation.Common.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Adapter.Unity.Ui
{
    /// <summary>皮肤包的一次回落记录：哪一项没在本包里找到、落到了哪里。</summary>
    public readonly struct UiSkinFallback
    {
        /// <summary>包内相对路径（如 <c>slot_frame/std_chest.png</c>）。</summary>
        public string Item { get; }

        /// <summary>回落目标：<c>placeholder:&lt;相对路径&gt;</c> 或 <c>procedural</c>。</summary>
        public string Target { get; }

        public UiSkinFallback(string item, string target)
        {
            Item = item;
            Target = target;
        }
    }

    public sealed class UiSkinPack : IDisposable
    {
        public const string PlaceholderSkinRef = "skin.default";
        public const string SkinRootRelative = "ui/skin";
        public const string ThemeFile = "theme.json";

        public static readonly string[] SlotStates = { "highlight", "disabled", "drag_hover" };

        /// <summary>槽位框的可选状态变体（清单里 <c>optional</c>）：缺省时按下/选中都用 highlight 图。</summary>
        public static readonly string[] SlotOptionalStates = { "pressed", "selected" };

        private readonly string _packDir;
        private readonly string _placeholderDir;
        private readonly Dictionary<string, Sprite?> _sprites = new Dictionary<string, Sprite?>(StringComparer.Ordinal);
        private readonly List<UiSkinFallback> _fallbacks = new List<UiSkinFallback>();
        private readonly HashSet<string> _fallbackSeen = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _optionalAbsent = new List<string>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private JsonObject? _theme;
        private bool _themeLoaded;

        /// <summary>皮肤包引用（<c>skin.&lt;名&gt;</c>）；传入空或格式不对时取占位皮肤。</summary>
        public string SkinRef { get; }

        /// <summary>是否框架占位皮肤。</summary>
        public bool IsPlaceholder { get; }

        /// <summary>本包目录（可能不存在）。</summary>
        public string Directory => _packDir;

        /// <summary>读到的皮肤引用格式不对（回落占位皮肤）时为 true。</summary>
        public bool RefInvalid { get; }

        /// <summary>本包目录是否存在。</summary>
        public bool PackDirectoryExists => System.IO.Directory.Exists(_packDir);

        /// <summary>至今发生的回落（去重，按发生顺序）：与导入校验报告 <c>equip_skin_item_missing</c> 同一批项。</summary>
        public IReadOnlyList<UiSkinFallback> Fallbacks => _fallbacks;

        /// <summary>至今取用过、本包与占位皮肤都没有的可选元素（相对路径，去重）：不是问题，该项沿用框架默认外观。</summary>
        public IReadOnlyList<string> OptionalAbsent => _optionalAbsent;

        private UiSkinPack(string contentRoot, string skinRef, bool refInvalid)
        {
            RefInvalid = refInvalid;
            var name = DirNameOf(skinRef);
            IsPlaceholder = string.Equals(name, DirNameOf(PlaceholderSkinRef), StringComparison.Ordinal);
            SkinRef = skinRef;
            _packDir = Path.Combine(contentRoot, SkinRootRelative.Replace('/', Path.DirectorySeparatorChar), name!);
            _placeholderDir = Path.Combine(contentRoot, SkinRootRelative.Replace('/', Path.DirectorySeparatorChar), DirNameOf(PlaceholderSkinRef)!);
        }

        /// <summary><c>skin.&lt;名&gt;</c> → 目录名（名里的点换下划线，同 skin_pack.py）；格式不对返回 null。</summary>
        public static string? DirNameOf(string skinRef)
        {
            const string prefix = "skin.";
            if (string.IsNullOrEmpty(skinRef) || !skinRef.StartsWith(prefix, StringComparison.Ordinal) || skinRef.Length == prefix.Length)
            {
                return null;
            }

            return skinRef.Substring(prefix.Length).Replace('.', '_');
        }

        /// <summary>
        /// 装载皮肤包。<paramref name="skinRef"/> 为空取占位皮肤；格式不对记警告并取占位皮肤（<see cref="RefInvalid"/>）。
        /// <paramref name="contentRoot"/> 缺省取适配器资源根（<c>StreamingAssets/GameFoundation</c>）。
        /// </summary>
        public static UiSkinPack Load(string? skinRef, string? contentRoot = null)
        {
            var root = contentRoot ?? EngineAdapter.UnityResourceLoader.ContentRoot;
            if (string.IsNullOrEmpty(skinRef))
            {
                return new UiSkinPack(root, PlaceholderSkinRef, false);
            }

            if (DirNameOf(skinRef!) == null)
            {
                Debug.LogWarning($"[UiSkinPack] skin_ref \"{skinRef}\" 不是 skin.<名> 形式，改用框架占位皮肤。");
                return new UiSkinPack(root, PlaceholderSkinRef, true);
            }

            return new UiSkinPack(root, skinRef!, false);
        }

        // ---------------------------------------------------------------- 取用

        /// <summary>槽位框（<c>slot_frame/&lt;槽位名&gt;.png</c>，槽位名见 <c>EquipmentViewModel.SlotShortName</c>）。</summary>
        public Sprite SlotFrame(string slotName) =>
            Resolve("slot_frame/" + slotName + ".png", "slot_frame/_default.png", new Color(0.30f, 0.33f, 0.40f, 1f));

        /// <summary>兜底槽位框（<c>slot_frame/_default.png</c>）。</summary>
        public Sprite SlotFrameDefault() =>
            Resolve("slot_frame/_default.png", null, new Color(0.30f, 0.33f, 0.40f, 1f));

        /// <summary>槽位框三态（<see cref="SlotStates"/>）。</summary>
        public Sprite SlotState(string state) =>
            Resolve("slot_frame/_" + state + ".png", null, new Color(0.85f, 0.75f, 0.35f, 1f));

        /// <summary>品质框（<c>quality_frame/&lt;品质名&gt;.png</c>）；品质名为空取 <c>_default.png</c>。</summary>
        public Sprite QualityFrame(string qualityName) =>
            string.IsNullOrEmpty(qualityName)
                ? Resolve("quality_frame/_default.png", null, new Color(0.45f, 0.48f, 0.55f, 1f))
                : Resolve("quality_frame/" + qualityName + ".png", "quality_frame/_default.png", new Color(0.45f, 0.48f, 0.55f, 1f));

        /// <summary>纸娃娃预览区背景（<c>paperdoll_preview/background.png</c>）。</summary>
        public Sprite PaperdollBackground() =>
            Resolve("paperdoll_preview/background.png", null, new Color(0.14f, 0.15f, 0.19f, 1f));

        /// <summary>面板九宫格底图（可选项 <c>panel/background.png</c>，边框取 theme 的 <c>panel_border</c>，缺省 5）；本包与占位皮肤都没有时为 null（面板沿用 <see cref="UiSkin.PanelSprite"/>）。</summary>
        public Sprite? PanelBackground() => Optional("panel/background.png", "panel_border", 5);

        /// <summary>提示框九宫格底图（<c>tooltip/background.png</c>，边框取 theme 的 <c>tooltip_border</c>，缺省 2）。</summary>
        public Sprite TooltipBackground() =>
            Resolve("tooltip/background.png", null, new Color(0.10f, 0.11f, 0.14f, 1f), ThemeInt("tooltip_border", 2));

        /// <summary>提示框分隔线（<c>tooltip/divider.png</c>）。</summary>
        public Sprite TooltipDivider() => Resolve("tooltip/divider.png", null, new Color(0.43f, 0.45f, 0.52f, 1f));

        /// <summary>提示框属性行底条（<c>tooltip/row.png</c>）。</summary>
        public Sprite TooltipRow() => Resolve("tooltip/row.png", null, new Color(1f, 1f, 1f, 0.06f));

        /// <summary>拖拽态（<c>drag/ghost|target_ok|target_blocked.png</c>）。</summary>
        public Sprite DragState(string name) => Resolve("drag/" + name + ".png", null, new Color(0.9f, 0.9f, 0.9f, 1f));

        /// <summary>
        /// 槽位格的状态精灵（给 <see cref="Selectable.spriteState"/> 用）：悬停 = <c>_highlight</c>；按下 = <c>_pressed</c>（可选，缺省取悬停图）；
        /// 选中 = <c>_selected</c>（可选，缺省取悬停图）；禁用 = <c>_disabled</c>。常态是各槽位自己的 <see cref="SlotFrame"/>。
        /// </summary>
        public SpriteState SlotSpriteState()
        {
            var hover = SlotState("highlight");
            return new SpriteState
            {
                highlightedSprite = hover,
                pressedSprite = Optional("slot_frame/_pressed.png", null, 0) ?? hover,
                selectedSprite = Optional("slot_frame/_selected.png", null, 0) ?? hover,
                disabledSprite = SlotState("disabled"),
            };
        }

        /// <summary>
        /// 按钮九宫格状态图（可选元素 <c>button/normal|hover|pressed|disabled|selected.png</c>，边框取 theme 的 <c>button_border</c>，缺省 4）。
        /// 本包与占位皮肤都没有 <c>normal</c> 时返回 null（按钮沿用框架默认的纯色着色外观）；其它状态缺省取 normal。
        /// </summary>
        public UiButtonSprites? Button()
        {
            var normal = Optional("button/normal.png", "button_border", 4);
            if (normal == null)
            {
                return null;
            }

            return new UiButtonSprites
            {
                Normal = normal,
                Hover = Optional("button/hover.png", "button_border", 4),
                Pressed = Optional("button/pressed.png", "button_border", 4),
                Disabled = Optional("button/disabled.png", "button_border", 4),
                Selected = Optional("button/selected.png", "button_border", 4),
            };
        }

        /// <summary>可选元素：本包 → 占位皮肤；都没有返回 null（记进 <see cref="OptionalAbsent"/>，不记回落）。<paramref name="borderToken"/> 非空时按九宫格装载。</summary>
        public Sprite? Optional(string relative, string? borderToken, int borderDefault)
        {
            var border = borderToken != null ? ThemeInt(borderToken, borderDefault) : 0;
            var native = relative.Replace('/', Path.DirectorySeparatorChar);
            var file = Path.Combine(_packDir, native);
            if (!File.Exists(file) && !IsPlaceholder)
            {
                file = Path.Combine(_placeholderDir, native);
            }

            if (!File.Exists(file))
            {
                if (!_optionalAbsent.Contains(relative))
                {
                    _optionalAbsent.Add(relative);
                }

                return null;
            }

            var key = file + "|b" + border;
            if (_sprites.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var sprite = LoadFile(file, border);
            _sprites[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// 清单里任一展开文件的精灵（皮肤画廊与完整性用例用）：必备/占位必备元素走"本包 → 占位同名 → 清单声明的兜底 → 程序生成"的回落链，
        /// 可选元素走 <see cref="Optional"/>（缺则 null）；九宫格元素按清单声明的边框令牌装载。
        /// </summary>
        public Sprite? ElementSprite(UiSkinFile file)
        {
            var e = file.Element;
            var border = e.IsNineSlice ? ThemeInt(e.NineSliceToken!, e.NineSliceDefault) : 0;
            if (e.Requirement == "optional")
            {
                return Optional(file.Path, e.NineSliceToken, e.NineSliceDefault);
            }

            return Resolve(file.Path, e.Fallback, new Color(0.5f, 0.5f, 0.5f, 1f), border);
        }

        /// <summary>主题数值令牌（theme.json 顶层的正整数，如 <c>panel_border</c>），缺省取 <paramref name="fallback"/>。</summary>
        public int NineSliceBorder(string token, int fallback) => ThemeInt(token, fallback);

        // ---------------------------------------------------------------- 主题

        private JsonObject? Theme
        {
            get
            {
                if (_themeLoaded)
                {
                    return _theme;
                }

                _themeLoaded = true;
                var path = Path.Combine(_packDir, ThemeFile);
                if (!File.Exists(path))
                {
                    path = Path.Combine(_placeholderDir, ThemeFile);
                    if (File.Exists(path))
                    {
                        Record(ThemeFile, "placeholder:" + ThemeFile);
                    }
                }

                if (File.Exists(path))
                {
                    try
                    {
                        _theme = JsonReader.Parse(File.ReadAllText(path)) as JsonObject;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[UiSkinPack] {path} 解析失败：{ex.Message}");
                    }
                }

                return _theme;
            }
        }

        /// <summary>theme.json 的 <c>colors.&lt;键&gt;</c>（<c>#rrggbb</c> 或 <c>#rrggbbaa</c>）；缺省为 null。</summary>
        public Color? ThemeColor(string key)
        {
            if (Theme != null && Theme.TryGetValue("colors", out var colors) && colors is JsonObject obj
                && obj.TryGetValue(key, out var value) && value is JsonString text
                && ColorUtility.TryParseHtmlString(text.Value, out var color))
            {
                return color;
            }

            return null;
        }

        /// <summary>theme.json 的 <c>font</c>（如 <c>fonts/noto_sans_cjk_sc.otf</c>）；缺省为 null。</summary>
        public string? ThemeFont =>
            Theme != null && Theme.TryGetValue("font", out var value) && value is JsonString text ? text.Value : null;

        private int ThemeInt(string key, int fallback)
        {
            if (Theme != null && Theme.TryGetValue(key, out var value) && value is JsonNumber n && n.TryGetInt64(out var l) && l > 0)
            {
                return (int)l;
            }

            return fallback;
        }

        /// <summary>
        /// 由本包生成要安装的皮肤覆盖：占位皮肤返回 null（缺省皮肤逐位不变，见类型注释）；其它皮肤把 theme.json 配色、panel/background.png、
        /// 字体（theme.json 的 font 能在 Resources/Fonts 下找到时）映射到 <see cref="UiSkinOverride"/>，本包没有的项不覆盖。
        /// </summary>
        public UiSkinOverride? CreateOverride()
        {
            if (IsPlaceholder)
            {
                return null;
            }

            var skin = new UiSkinOverride
            {
                PanelBackground = ThemeColor("panel"),
                PanelBorder = ThemeColor("panel_edge"),
                TextColor = ThemeColor("text"),
                AccentColor = ThemeColor("highlight"),
                DisabledColor = ThemeColor("disabled"),
                DangerColor = ThemeColor("blocked"),
                PanelSprite = PanelBackground(),
                ButtonSprites = Button(),
            };

            var font = ThemeFont;
            if (!string.IsNullOrEmpty(font))
            {
                var stem = Path.GetFileNameWithoutExtension(font);
                if (!string.Equals(stem, "noto_sans_cjk_sc", StringComparison.Ordinal))
                {
                    var osFont = Resources.Load<Font>("Fonts/" + stem);
                    if (osFont != null)
                    {
                        try
                        {
                            var asset = TMP_FontAsset.CreateFontAsset(osFont);
                            _owned.Add(asset);
                            skin.Font = asset;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[UiSkinPack] 字体 {stem} 生成 TMP 字体资产失败：{ex.Message}");
                        }
                    }
                }
            }

            return skin;
        }

        // ---------------------------------------------------------------- 解析与装载

        /// <summary>该相对路径在本包（或回落链）里实际落到的文件；全部缺失为 null（由程序生成的兜底框顶上）。</summary>
        public string? ResolveFile(string relative, string? defaultRelative)
        {
            var native = relative.Replace('/', Path.DirectorySeparatorChar);
            var own = Path.Combine(_packDir, native);
            if (File.Exists(own))
            {
                return own;
            }

            if (!IsPlaceholder)
            {
                var placeholder = Path.Combine(_placeholderDir, native);
                if (File.Exists(placeholder))
                {
                    Record(relative, "placeholder:" + relative);
                    return placeholder;
                }
            }

            if (defaultRelative != null)
            {
                var defNative = defaultRelative.Replace('/', Path.DirectorySeparatorChar);
                var ownDefault = Path.Combine(_packDir, defNative);
                if (IsPlaceholder && File.Exists(ownDefault))
                {
                    Record(relative, "placeholder:" + defaultRelative);
                    return ownDefault;
                }

                var placeholderDefault = Path.Combine(_placeholderDir, defNative);
                if (File.Exists(placeholderDefault))
                {
                    Record(relative, "placeholder:" + defaultRelative);
                    return placeholderDefault;
                }
            }

            Record(relative, "procedural");
            return null;
        }

        private Sprite Resolve(string relative, string? defaultRelative, Color proceduralColor, int border = 0)
        {
            var file = ResolveFile(relative, defaultRelative);
            var key = file != null ? file + "|b" + border : "procedural:" + relative;
            if (_sprites.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var sprite = file != null ? LoadFile(file, border) : null;
            if (sprite == null)
            {
                if (file != null)
                {
                    Record(relative, "procedural");
                }

                sprite = CreateProceduralFrame(proceduralColor);
            }

            _sprites[key] = sprite;
            return sprite;
        }

        private Sprite? LoadFile(string path, int border)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "UiSkinPack:" + Path.GetFileName(path) };
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                texture.wrapMode = TextureWrapMode.Clamp;
                _owned.Add(texture);
                var b = Mathf.Clamp(border, 0, Mathf.Min(texture.width, texture.height) / 2);
                var sprite = Sprite.Create(
                    texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f,
                    0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
                sprite.name = "UiSkinPack:" + Path.GetFileName(path);
                _owned.Add(sprite);
                return sprite;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UiSkinPack] 读取 {path} 失败：{ex.Message}");
                return null;
            }
        }

        private Sprite CreateProceduralFrame(Color color)
        {
            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "UiSkinPack:procedural" };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var edge = x < 2 || y < 2 || x >= size - 2 || y >= size - 2;
                    pixels[y * size + x] = edge ? (Color32)color : new Color32(0, 0, 0, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            _owned.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "UiSkinPack:procedural";
            _owned.Add(sprite);
            return sprite;
        }

        private void Record(string item, string target)
        {
            if (_fallbackSeen.Add(item))
            {
                _fallbacks.Add(new UiSkinFallback(item, target));
            }
        }

        /// <summary>该对象（精灵/纹理/字体资产）是否由本包装载并持有（<see cref="UiVisuals.IsRetiredSkinSprite"/> 判断精灵是否属于被换下的旧包用）。</summary>
        public bool Owns(UnityEngine.Object? obj) => obj != null && _owned.Contains(obj);

        public void Dispose()
        {
            foreach (var o in _owned)
            {
                if (o != null)
                {
                    UnityEngine.Object.Destroy(o);
                }
            }

            _owned.Clear();
            _sprites.Clear();
        }
    }
}
