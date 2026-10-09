#nullable enable
// SkillTooltipView：技能悬停详情的 uGUI 视图（ADR-0180）。内容（名称、类型、读条或动作时长、冷却、消耗、射程、效果数值、说明）由框架的
// SkillTooltipBuilder 按 skill.def 数据与当前属性算出（ADR-0175），本类只排版，不写任何文案或数值公式。
//
// 判断记录（为什么是独立控件而不是并进 UiTooltip）：UiTooltip 是物品提示框，内容模型（ItemTooltipContent）、皮肤包底板与指针跟随都是物品面板那一套；
// 技能栏的提示框要摆在被悬停的格子正上方、行数和文案不同，而且不属于背包面板的拖放交互。两者共享的只有 UiWidgets/UiSkin，所以各自一个控件。
// 判断记录（不接收射线）：底板与所有文字都关闭 raycastTarget，提示框永远不挡住它下面的格子，也不会让指针“离开格子”造成闪烁。
// 判断记录（排版）：宽度固定，行数与说明高度按内容算；摆在锚点格的正上方并夹在父节点矩形内，高度超出父节点上沿时翻到格子下方。
using System.Collections.Generic;
using Presentation.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Adapter.Unity.Ui
{
    public sealed class SkillTooltipView
    {
        /// <summary>外观参数；缺省取当前皮肤的文字色与强调色。</summary>
        public sealed class Style
        {
            public float Width = 600f;
            public float Padding = 24f;
            public float RowHeight = 34f;
            public int NameFontSize = 34;
            public int RowFontSize = 24;
            public int DescFontSize = 22;
            public float LabelColumnWidth = 240f;

            /// <summary>提示框底边到格子上沿的距离。</summary>
            public float Lift = 12f;

            public Color? NameColor;
            public Color? LabelColor;
            public Color? ValueColor;
            public Color? DescColor;
        }

        private readonly RectTransform _parent;
        private readonly Style _style;
        private readonly RectTransform _root;
        private readonly TextMeshProUGUI _name;
        private readonly TextMeshProUGUI _desc;
        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();
        private readonly List<TextMeshProUGUI> _values = new List<TextMeshProUGUI>();

        public SkillTooltipView(RectTransform parent, Style? style = null)
        {
            _parent = parent;
            _style = style ?? new Style();
            _root = UiWidgets.CreatePanelBackground("SkillTip", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(_style.Width, 200f), Vector2.zero);
            _root.pivot = new Vector2(0.5f, 0f);
            _root.GetComponent<Image>().raycastTarget = false;
            _name = UiWidgets.CreateLabel("Name", _root, string.Empty, _style.NameFontSize, TextAlignmentOptions.TopLeft);
            _name.fontStyle = FontStyles.Bold;
            _name.color = _style.NameColor ?? UiSkin.AccentColor;
            _name.raycastTarget = false;
            _desc = UiWidgets.CreateLabel("Desc", _root, string.Empty, _style.DescFontSize, TextAlignmentOptions.TopLeft);
            _desc.textWrappingMode = TextWrappingModes.Normal;
            _desc.color = _style.DescColor ?? _style.LabelColor ?? UiSkin.TextColor;
            _desc.raycastTarget = false;
            _root.gameObject.SetActive(false);
        }

        /// <summary>提示框根节点（底板）。</summary>
        public RectTransform Root => _root;

        public bool IsVisible => _root.gameObject.activeSelf;

        /// <summary>当前显示的内容（框架算出的数据）；隐藏时为 null。</summary>
        public SkillTooltipContent? Content { get; private set; }

        /// <summary>画面上实际显示的文字（测试与截图核对用）：名称、各行（标签，值）、说明。</summary>
        public string NameText => _name.text;

        public string DescriptionText => _desc.text;

        public IReadOnlyList<(string Label, string Value)> RowTexts
        {
            get
            {
                var rows = new List<(string, string)>();
                var count = Content != null ? Content.Rows.Count : 0;
                for (var i = 0; i < count && i < _labels.Count; i++)
                {
                    rows.Add((_labels[i].text, _values[i].text));
                }

                return rows;
            }
        }

        public void Hide()
        {
            Content = null;
            _root.gameObject.SetActive(false);
        }

        /// <summary>显示 <paramref name="content"/>，摆在 <paramref name="slot"/> 的正上方（放不下翻到下方）。</summary>
        public void Show(SkillTooltipContent content, RectTransform slot)
        {
            Content = content;
            var style = _style;
            var inner = style.Width - style.Padding * 2f;
            _name.text = content.Name;
            Place(_name, style.Padding, style.Padding - 6f, inner, style.NameFontSize + 12f);
            var y = style.Padding + style.NameFontSize + 16f;
            var labelColor = style.LabelColor ?? UiSkin.TextColor;
            var valueColor = style.ValueColor ?? UiSkin.AccentColor;
            for (var i = 0; i < content.Rows.Count; i++)
            {
                while (_labels.Count <= i)
                {
                    var label = UiWidgets.CreateLabel("RowLabel" + _labels.Count, _root, string.Empty, style.RowFontSize, TextAlignmentOptions.TopLeft);
                    label.color = labelColor;
                    label.raycastTarget = false;
                    var value = UiWidgets.CreateLabel("RowValue" + _values.Count, _root, string.Empty, style.RowFontSize, TextAlignmentOptions.TopRight);
                    value.color = valueColor;
                    value.fontStyle = FontStyles.Bold;
                    value.raycastTarget = false;
                    _labels.Add(label);
                    _values.Add(value);
                }

                _labels[i].text = content.Rows[i].Label;
                _values[i].text = content.Rows[i].Value;
                Place(_labels[i], style.Padding, y, style.LabelColumnWidth, style.RowHeight);
                Place(_values[i], style.Padding + style.LabelColumnWidth, y, inner - style.LabelColumnWidth, style.RowHeight);
                _labels[i].gameObject.SetActive(true);
                _values[i].gameObject.SetActive(true);
                y += style.RowHeight;
            }

            for (var i = content.Rows.Count; i < _labels.Count; i++)
            {
                _labels[i].gameObject.SetActive(false);
                _values[i].gameObject.SetActive(false);
            }

            var hasDesc = content.Description.Length > 0;
            _desc.gameObject.SetActive(hasDesc);
            if (hasDesc)
            {
                y += 10f;
                _desc.text = content.Description;
                var descHeight = _desc.GetPreferredValues(content.Description, inner, 0f).y;
                Place(_desc, style.Padding, y, inner, descHeight + 4f);
                y += descHeight + 4f;
            }

            var height = y + style.Padding;
            _root.sizeDelta = new Vector2(style.Width, height);
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            PlaceAbove(slot, height);
        }

        private void PlaceAbove(RectTransform slot, float height)
        {
            var corners = new Vector3[4];
            slot.GetWorldCorners(corners); // 0 左下 1 左上 2 右上 3 右下
            var topCenter = _parent.InverseTransformPoint((corners[1] + corners[2]) * 0.5f);
            var bottomCenter = _parent.InverseTransformPoint((corners[0] + corners[3]) * 0.5f);
            var area = _parent.rect;
            var center = (Vector2)area.center;
            var half = _style.Width * 0.5f;
            var x = Mathf.Clamp(topCenter.x, area.xMin + half, Mathf.Max(area.xMin + half, area.xMax - half));
            var above = topCenter.y + _style.Lift;
            if (above + height <= area.yMax)
            {
                _root.pivot = new Vector2(0.5f, 0f);
                _root.anchoredPosition = new Vector2(x, above) - center;
            }
            else
            {
                _root.pivot = new Vector2(0.5f, 1f);
                _root.anchoredPosition = new Vector2(x, bottomCenter.y - _style.Lift) - center;
            }
        }

        private static void Place(TextMeshProUGUI label, float x, float y, float w, float h)
        {
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(x, -y);
        }
    }
}
