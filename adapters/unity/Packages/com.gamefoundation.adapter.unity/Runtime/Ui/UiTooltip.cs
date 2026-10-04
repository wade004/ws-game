#nullable enable
// UiTooltip：物品悬停提示框（手感设计/08 第 3 节、ADR-0152）。底板、分隔线、属性行底条来自皮肤包 tooltip/*（没有声明皮肤时取占位包的同名文件），
// 内容来自 ItemTooltipContent（名称、品质、属性行，全部由 item.template 数据算出）。提示框不接收射线（不挡住下面的格子），始终画在内容节点最上层，
// 位置跟随指针并夹在画布内。皮肤切换时由 UiInteraction 调 Rebuild 重建外观。
using System;
using System.Collections.Generic;
using Presentation.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Adapter.Unity.Ui
{
    public sealed class UiTooltip : IDisposable
    {
        private const float Padding = 10f;
        private const float TitleHeight = 24f;
        private const float QualityHeight = 18f;
        private const float RowHeight = 20f;
        private const float RowGap = 2f;
        private const float ValueGap = 16f;
        private const float MinWidth = 180f;
        private const float PointerOffset = 16f;

        private sealed class RowView
        {
            public RectTransform Root = null!;
            public Image Bar = null!;
            public TextMeshProUGUI Label = null!;
            public TextMeshProUGUI Value = null!;
        }

        private readonly UiInteraction _owner;
        private readonly List<RowView> _rows = new List<RowView>();
        private RectTransform? _root;
        private Image? _background;
        private Image? _divider;
        private TextMeshProUGUI? _title;
        private TextMeshProUGUI? _quality;

        /// <summary>当前显示的内容；隐藏时为 null。</summary>
        public ItemTooltipContent? Current { get; private set; }

        public bool IsVisible => Current != null && _root != null && _root.gameObject.activeSelf;

        /// <summary>提示框根节点（底板）。</summary>
        public RectTransform Root => _root ?? throw new InvalidOperationException("提示框已销毁");

        public Image Background => _background!;

        public Image Divider => _divider!;

        public string TitleText => _title != null ? _title.text : string.Empty;

        public string QualityText => _quality != null ? _quality.text : string.Empty;

        /// <summary>当前可见的属性行（标签，值）。</summary>
        public IReadOnlyList<(string Label, string Value)> VisibleRows
        {
            get
            {
                var list = new List<(string, string)>();
                foreach (var row in _rows)
                {
                    if (row.Root.gameObject.activeSelf)
                    {
                        list.Add((row.Label.text, row.Value.text));
                    }
                }

                return list;
            }
        }

        public int VisibleRowCount
        {
            get
            {
                var count = 0;
                foreach (var row in _rows)
                {
                    if (row.Root.gameObject.activeSelf)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public UiTooltip(UiInteraction owner)
        {
            _owner = owner;
            Build();
        }

        private void Build()
        {
            var pack = _owner.Visuals.Pack;
            _root = UiWidgets.CreatePanelBackground("ItemTooltip", _owner.Content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(MinWidth, 80f), Vector2.zero);
            _root.pivot = new Vector2(0f, 1f);
            _background = _root.GetComponent<Image>();
            _background.sprite = pack.TooltipBackground();
            _background.type = Image.Type.Sliced;
            _background.color = Color.white;
            _background.raycastTarget = false;
            var group = _root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            _title = Label("Title", 18, TextAlignmentOptions.MidlineLeft, UiSkin.TextColor);
            _quality = Label("Quality", 14, TextAlignmentOptions.MidlineLeft, pack.ThemeColor("highlight") ?? UiSkin.AccentColor);

            var dividerGo = new GameObject("Divider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var dividerRect = (RectTransform)dividerGo.transform;
            dividerRect.SetParent(_root, false);
            _divider = dividerGo.GetComponent<Image>();
            _divider.sprite = pack.TooltipDivider();
            _divider.type = Image.Type.Simple;
            _divider.raycastTarget = false;

            _rows.Clear();
            _root.gameObject.SetActive(false);
        }

        private TextMeshProUGUI Label(string name, int size, TextAlignmentOptions align, Color color)
        {
            var label = UiWidgets.CreateLabel(name, _root!, string.Empty, size, align);
            label.color = color;
            label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        private RowView EnsureRow(int index)
        {
            while (_rows.Count <= index)
            {
                var pack = _owner.Visuals.Pack;
                var go = new GameObject("Row" + _rows.Count, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rect = (RectTransform)go.transform;
                rect.SetParent(_root, false);
                var bar = go.GetComponent<Image>();
                bar.sprite = pack.TooltipRow();
                bar.type = Image.Type.Simple;
                bar.raycastTarget = false;
                var dim = pack.ThemeColor("text_dim") ?? Color.Lerp(UiSkin.TextColor, UiSkin.DisabledColor, 0.5f);
                var label = UiWidgets.CreateLabel("Label", rect, string.Empty, 14, TextAlignmentOptions.MidlineLeft);
                label.color = dim;
                label.raycastTarget = false;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                var value = UiWidgets.CreateLabel("Value", rect, string.Empty, 14, TextAlignmentOptions.MidlineRight);
                value.raycastTarget = false;
                value.textWrappingMode = TextWrappingModes.NoWrap;
                _rows.Add(new RowView { Root = rect, Bar = bar, Label = label, Value = value });
            }

            return _rows[index];
        }

        /// <summary>显示 <paramref name="content"/>，位置跟随屏幕坐标 <paramref name="screenPosition"/>。</summary>
        public void Show(ItemTooltipContent content, Vector2 screenPosition)
        {
            if (_root == null)
            {
                return;
            }

            Current = content;
            _title!.text = content.Name;
            _quality!.text = content.QualityText;
            _quality.gameObject.SetActive(content.QualityText.Length > 0);

            var titleWidth = _title.GetPreferredValues(content.Name).x;
            var qualityWidth = content.QualityText.Length > 0 ? _quality.GetPreferredValues(content.QualityText).x : 0f;
            var rowsWidth = 0f;
            for (var i = 0; i < content.Rows.Count; i++)
            {
                var row = EnsureRow(i);
                row.Label.text = content.Rows[i].Label;
                row.Value.text = content.Rows[i].Value;
                rowsWidth = Mathf.Max(rowsWidth, row.Label.GetPreferredValues(row.Label.text).x + ValueGap + row.Value.GetPreferredValues(row.Value.text).x);
            }

            var width = Mathf.Max(MinWidth, 2f * Padding + Mathf.Max(titleWidth, Mathf.Max(qualityWidth, rowsWidth)));
            var inner = width - 2f * Padding;

            var y = Padding;
            Place(_title.rectTransform, Padding, y, inner, TitleHeight);
            y += TitleHeight;
            if (content.QualityText.Length > 0)
            {
                Place(_quality.rectTransform, Padding, y, inner, QualityHeight);
                y += QualityHeight;
            }

            var dividerHeight = Mathf.Clamp(_divider!.sprite != null ? _divider.sprite.rect.height : 4f, 2f, 12f);
            y += 4f;
            Place(_divider.rectTransform, Padding, y, inner, dividerHeight);
            y += dividerHeight + 4f;

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var active = i < content.Rows.Count;
                row.Root.gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                Place(row.Root, Padding, y, inner, RowHeight);
                row.Bar.enabled = i % 2 == 0;
                UiWidgets.SetRect(row.Label.rectTransform, Vector2.zero, Vector2.one, new Vector2(4f, 0f), new Vector2(-4f, 0f));
                UiWidgets.SetRect(row.Value.rectTransform, Vector2.zero, Vector2.one, new Vector2(4f, 0f), new Vector2(-4f, 0f));
                y += RowHeight + RowGap;
            }

            var height = y + Padding - (content.Rows.Count > 0 ? RowGap : 0f);
            _root.sizeDelta = new Vector2(width, height);
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Move(screenPosition);
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        /// <summary>提示框跟随指针（右下方，超出画布时夹回画布内）。</summary>
        public void Move(Vector2 screenPosition)
        {
            if (_root == null || Current == null)
            {
                return;
            }

            var local = _owner.ScreenToLocal(screenPosition);
            var bounds = _owner.Content.rect;
            var size = _root.sizeDelta;
            var x = local.x + PointerOffset;
            var y = local.y - PointerOffset;
            if (x + size.x > bounds.xMax)
            {
                x = local.x - PointerOffset - size.x;     // 右边放不下：翻到指针左边
            }

            x = Mathf.Clamp(x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - size.x));
            y = Mathf.Clamp(y, Mathf.Min(bounds.yMax, bounds.yMin + size.y), bounds.yMax);
            _root.anchoredPosition = new Vector2(x, y);
        }

        public void Hide()
        {
            Current = null;
            if (_root != null)
            {
                _root.gameObject.SetActive(false);
            }
        }

        /// <summary>按当前皮肤包重建外观（<see cref="UiVisuals.SkinChanged"/> 之后）；正在显示的内容保持。</summary>
        public void Rebuild()
        {
            var shown = Current;
            var position = _root != null ? _root.anchoredPosition : Vector2.zero;
            Destroy();
            Build();
            if (shown != null)
            {
                Show(shown, _owner.ScreenOf(_owner.Content));
                _root!.anchoredPosition = position;
            }
        }

        private void Destroy()
        {
            if (_root != null)
            {
                _root.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(_root.gameObject);
            }

            _root = null;
            _rows.Clear();
        }

        public void Dispose() => Destroy();
    }
}
