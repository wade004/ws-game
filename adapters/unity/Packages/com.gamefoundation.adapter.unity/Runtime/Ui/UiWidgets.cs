#nullable enable
// UiWidgets：运行期 uGUI 控件构建的最小公共工具集（任务书"全部用 uGUI 运行期代码构建（不依赖
// 手工 prefab）"）。本文件只提供"造一个 XX 控件"这一层，不含任何面板专属布局/绑定逻辑——具体
// 面板见 Runtime/Ui/Panels/*.cs。
//
// 判断记录（皮肤跟随，ADR-0155）：本文件造出的每个控件都登记进 UiSkinBindings（记下它当时从 UiSkin 取的底图/按钮图/字体/配色）；
// 运行期换皮肤（UiVisuals.SwitchSkin）经 UiSkinBindings.ReapplyAll 把它们换成新皮肤的取值——面板不必自己订阅换皮肤事件，
// 也就不会有"漏网"的面板。面板建出控件后自己改过的属性（比如把标签染成别的颜色）不会被覆盖：只有"仍等于登记时从皮肤取的值"的属性才跟着换。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Adapter.Unity.Ui
{
    public static class UiWidgets
    {
        public static RectTransform CreateRoot(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>一块九宫格占位面板（<see cref="UiSkin.PanelSprite"/>），供各面板做背景。</summary>
        public static RectTransform CreatePanelBackground(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta, Vector2 anchoredPos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPos;

            var image = go.GetComponent<Image>();
            image.sprite = UiSkin.PanelSprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white; // 颜色已烘进九宫格纹理本身，Image.color 保持白色不叠加变色。
            UiSkinBindings.TrackPanel(image);
            return rect;
        }

        public static TextMeshProUGUI CreateLabel(string name, Transform parent, string text, int fontSize = 20, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = UiSkin.Font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = UiSkin.TextColor;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            UiSkinBindings.TrackLabel(tmp);
            return tmp;
        }

        public static (RectTransform Root, Button Button, TextMeshProUGUI Label) CreateButton(string name, Transform parent, string text, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.sprite = UiSkin.FlatSprite;
            image.type = Image.Type.Sliced;
            image.color = UiSkin.ButtonIdleColor;

            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.2f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.85f, 1f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            button.colors = colors;
            var skinned = UiSkin.ButtonSprites;
            if (skinned != null)
            {
                // 皮肤包提供了按钮九宫格状态图（ADR-0149）：换成精灵切换，其余状态缺省取常态图；没有提供时上面的着色外观逐位不变。
                image.sprite = skinned.Normal;
                image.type = Image.Type.Sliced;
                image.color = Color.white;
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = SkinnedSpriteState(skinned);
            }
            UiSkinBindings.TrackButton(image, button);
            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            var labelRect = CreateRoot("Label", rect);
            var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = UiSkin.Font;
            label.text = text;
            label.fontSize = 18;
            label.color = UiSkin.TextColor;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            UiSkinBindings.TrackLabel(label);

            return (rect, button, label);
        }

        /// <summary>按钮九宫格状态图 → <see cref="SpriteState"/>（其余状态缺省取常态图）；建按钮与换皮肤刷新共用。</summary>
        internal static SpriteState SkinnedSpriteState(UiButtonSprites skinned) => new SpriteState
        {
            highlightedSprite = skinned.Hover ?? skinned.Normal,
            pressedSprite = skinned.Pressed ?? skinned.Normal,
            selectedSprite = skinned.Selected ?? skinned.Normal,
            disabledSprite = skinned.Disabled ?? skinned.Normal,
        };

        /// <summary>一条水平进度条（血条/资源条/加载进度，纯 Image.fillAmount，不依赖 Slider）。</summary>
        public static (RectTransform Root, Image Fill) CreateProgressBar(string name, Transform parent, Color fillColor)
        {
            var backRect = CreatePanelBackground(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var backImage = backRect.GetComponent<Image>();
            backImage.color = Color.white;

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.SetParent(backRect, false);
            fillRect.anchorMin = new Vector2(0.03f, 0.15f);
            fillRect.anchorMax = new Vector2(0.97f, 0.85f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var fillImage = fillGo.GetComponent<Image>();
            fillImage.sprite = UiSkin.FlatSprite;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillAmount = 1f;
            fillImage.color = fillColor;
            UiSkinBindings.TrackFlat(fillImage);

            return (backRect, fillImage);
        }

        public static RectTransform CreateVerticalList(string name, Transform parent, float spacing = 4f)
        {
            var rect = CreateRoot(name, parent);
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;
            group.childControlHeight = false;
            group.childControlWidth = true;
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        public static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }

    /// <summary>
    /// 界面控件与 <see cref="UiSkin"/> 的绑定登记（ADR-0155）：<see cref="UiWidgets"/> 建的每个底板、按钮、标签、进度条填充都在这里记一笔——它当时从 UiSkin 取了什么。
    /// 换皮肤时 <see cref="ReapplyAll"/> 把"仍等于登记值"的属性换成 UiSkin 现在的取值（底图、按钮图与切换状态、字体、文字色、强调色）；面板自己改过的属性保持原样。
    /// 这是换皮肤时界面刷新的统一失效入口：HUD、动作条、任务日志、设置、存档、暂停、商店、对话、技能书、角色属性等不订阅 <see cref="UiVisuals.SkinChanged"/> 的面板，
    /// 主菜单与实验室控制条都因为用 <see cref="UiWidgets"/> 建控件而自动在内；不经 <see cref="UiWidgets"/> 自己造 Image 的面板要么自己订阅 SkinChanged，
    /// 要么调 <see cref="TrackPanel"/>/<see cref="TrackFlat"/>/<see cref="TrackAccentFill"/> 登记（用例 <c>SwitchSkin_…_EveryPanelRefreshes</c> 会抓出两样都没做的）。
    /// 登记表是进程级静态的（同 <see cref="UiSkin"/> 本身是全局覆盖）；已销毁的控件在登记超过阈值或刷新时清掉。
    /// </summary>
    public static class UiSkinBindings
    {
        private enum Role
        {
            Panel,          // Image：九宫格面板底图 UiSkin.PanelSprite
            Flat,           // Image：纯色矩形 UiSkin.FlatSprite（颜色由面板自己定）
            AccentFill,     // Image：纯色矩形 + 强调色
            Button,         // Image + Button：着色外观或九宫格状态图
            Label,          // TextMeshProUGUI：字体 + 文字色
        }

        private sealed class Binding
        {
            public Role Role;
            public Image? Image;
            public Button? Button;
            public TextMeshProUGUI? Text;
            public Sprite? Sprite;
            public Color Color;
            public TMP_FontAsset? Font;
            public bool SpriteSwap;

            public bool Alive => Role == Role.Label ? Text != null : Image != null;

            public Component Target => Role == Role.Label ? (Component)Text! : Image!;
        }

        private static readonly List<Binding> All = new List<Binding>();
        private static int _pruneAt = 512;

        /// <summary>登记表里还活着的控件数（诊断/用例用）。</summary>
        public static int Count
        {
            get
            {
                Prune();
                return All.Count;
            }
        }

        /// <summary>登记一块九宫格面板底图（<c>Image.sprite</c> 取自 <see cref="UiSkin.PanelSprite"/>）。</summary>
        public static void TrackPanel(Image image) => Add(new Binding { Role = Role.Panel, Image = image, Sprite = image.sprite });

        /// <summary>登记一块纯色矩形（<c>Image.sprite</c> 取自 <see cref="UiSkin.FlatSprite"/>，颜色由面板自己定）。</summary>
        public static void TrackFlat(Image image) => Add(new Binding { Role = Role.Flat, Image = image, Sprite = image.sprite });

        /// <summary>登记一块强调色纯色矩形（精灵取自 <see cref="UiSkin.FlatSprite"/>，颜色取自 <see cref="UiSkin.AccentColor"/>，如设置面板的音量条填充）。</summary>
        public static void TrackAccentFill(Image image) => Add(new Binding { Role = Role.AccentFill, Image = image, Sprite = image.sprite, Color = image.color });

        internal static void TrackButton(Image image, Button button) =>
            Add(new Binding { Role = Role.Button, Image = image, Button = button, Sprite = image.sprite, Color = image.color, SpriteSwap = button.transition == Selectable.Transition.SpriteSwap });

        /// <summary>登记一个文字标签（字体取自 <see cref="UiSkin.Font"/>，文字色取自 <see cref="UiSkin.TextColor"/>）。</summary>
        public static void TrackLabel(TextMeshProUGUI text) => Add(new Binding { Role = Role.Label, Text = text, Font = text.font, Color = text.color });

        private static void Add(Binding binding)
        {
            if (All.Count >= _pruneAt)
            {
                Prune();
                _pruneAt = Math.Max(512, All.Count * 2);
            }

            All.Add(binding);
        }

        private static void Prune() => All.RemoveAll(b => !b.Alive);

        /// <summary>
        /// 换皮肤后调用（<see cref="UiVisuals.SwitchSkin"/> 内部已经调了）：把所有已登记控件里"仍等于登记值"的皮肤属性换成 <see cref="UiSkin"/> 现在的取值。返回被换过的控件数。
        /// </summary>
        public static int ReapplyAll()
        {
            Prune();
            var changed = 0;
            foreach (var binding in All)
            {
                if (Sync(binding, apply: true))
                {
                    changed++;
                }
            }

            return changed;
        }

        /// <summary>
        /// 诊断/用例用：<paramref name="root"/> 子树里"还停在旧皮肤取值上"的已登记控件（路径 + 属性）——换皮肤之后应当为空。
        /// 面板自己改过的属性不算；没登记过的控件看不到（那是用例另一条基于精灵来源的检查要抓的）。
        /// </summary>
        public static IReadOnlyList<string> FindStale(Transform root)
        {
            var result = new List<string>();
            foreach (var binding in All)
            {
                if (binding.Alive && binding.Target.transform.IsChildOf(root) && Sync(binding, apply: false))
                {
                    result.Add(PathOf(binding.Target.transform, root) + " [" + binding.Role + "]");
                }
            }

            return result;
        }

        private static string PathOf(Transform t, Transform root)
        {
            var path = t.name;
            for (var p = t.parent; p != null && p != root.parent; p = p.parent)
            {
                path = p.name + "/" + path;
            }

            return path;
        }

        /// <summary>对比登记值与皮肤现值；<paramref name="apply"/> 为真时把落后的属性换掉。返回这个控件是否落后（apply 时即"是否换过"）。</summary>
        private static bool Sync(Binding b, bool apply)
        {
            var stale = false;
            switch (b.Role)
            {
                case Role.Panel:
                    stale |= SyncSprite(b, UiSkin.PanelSprite, apply);
                    break;
                case Role.Flat:
                    stale |= SyncSprite(b, UiSkin.FlatSprite, apply);
                    break;
                case Role.AccentFill:
                    stale |= SyncSprite(b, UiSkin.FlatSprite, apply);
                    stale |= SyncImageColor(b, UiSkin.AccentColor, apply);
                    break;
                case Role.Button:
                    stale |= SyncButton(b, apply);
                    break;
                case Role.Label:
                    stale |= SyncLabel(b, apply);
                    break;
            }

            return stale;
        }

        private static bool SyncSprite(Binding b, Sprite expected, bool apply)
        {
            var image = b.Image!;
            if (image.sprite != b.Sprite || b.Sprite == expected)
            {
                return false;       // 面板自己换过，或已经是现行皮肤的
            }

            if (apply)
            {
                image.sprite = expected;
                b.Sprite = expected;
            }

            return true;
        }

        private static bool SyncImageColor(Binding b, Color expected, bool apply)
        {
            var image = b.Image!;
            if (image.color != b.Color || b.Color == expected)
            {
                return false;
            }

            if (apply)
            {
                image.color = expected;
                b.Color = expected;
            }

            return true;
        }

        private static bool SyncButton(Binding b, bool apply)
        {
            var image = b.Image!;
            var button = b.Button!;
            var skinned = UiSkin.ButtonSprites;
            var expectedSprite = skinned != null ? skinned.Normal : UiSkin.FlatSprite;
            var expectedColor = skinned != null ? Color.white : UiSkin.ButtonIdleColor;
            var wantSwap = skinned != null;
            var stale = false;

            if (image.sprite == b.Sprite && b.Sprite != expectedSprite)
            {
                stale = true;
                if (apply)
                {
                    image.sprite = expectedSprite;
                    image.type = Image.Type.Sliced;
                    b.Sprite = expectedSprite;
                }
            }

            if (image.color == b.Color && b.Color != expectedColor)
            {
                stale = true;
                if (apply)
                {
                    image.color = expectedColor;
                    b.Color = expectedColor;
                }
            }

            var expectedState = skinned != null ? UiWidgets.SkinnedSpriteState(skinned) : default;
            var stateStale = wantSwap
                && (button.spriteState.highlightedSprite != expectedState.highlightedSprite
                    || button.spriteState.pressedSprite != expectedState.pressedSprite
                    || button.spriteState.selectedSprite != expectedState.selectedSprite
                    || button.spriteState.disabledSprite != expectedState.disabledSprite);
            if (b.SpriteSwap != wantSwap || stateStale)
            {
                stale = true;
                if (apply)
                {
                    if (wantSwap)
                    {
                        button.transition = Selectable.Transition.SpriteSwap;
                        button.spriteState = expectedState;
                    }
                    else
                    {
                        button.transition = Selectable.Transition.ColorTint;
                        button.spriteState = default;
                    }

                    b.SpriteSwap = wantSwap;
                }
            }

            return stale;
        }

        private static bool SyncLabel(Binding b, bool apply)
        {
            var text = b.Text!;
            var stale = false;
            var font = UiSkin.Font;
            if (text.font == b.Font && b.Font != font)
            {
                stale = true;
                if (apply)
                {
                    text.font = font;
                    b.Font = font;
                }
            }

            var color = UiSkin.TextColor;
            if (text.color == b.Color && b.Color != color)
            {
                stale = true;
                if (apply)
                {
                    text.color = color;
                    b.Color = color;
                }
            }

            return stale;
        }
    }
}
