#nullable enable
// UiWidgets：运行期 uGUI 控件构建的最小公共工具集（任务书"全部用 uGUI 运行期代码构建（不依赖
// 手工 prefab）"）。本文件只提供"造一个 XX 控件"这一层，不含任何面板专属布局/绑定逻辑——具体
// 面板见 Runtime/Ui/Panels/*.cs。
using System;
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

            return (rect, button, label);
        }

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
}
