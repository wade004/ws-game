#nullable enable
// EquipmentPanel：装备面板（手感设计/08 第 3 节、ADR-0149）——槽位网格 + 纸娃娃预览区（方向可切换）。
//
// 槽位清单与每件装备的图标、品质、纸娃娃图层全部来自 EquipmentViewModel（数据：槽位定义、物品模板、装备外观行、外形映射）；
// 槽位框、品质框、预览区背景来自皮肤包（UiVisuals.Pack）；图标与纸娃娃层来自适配器资源加载器；位置、列数、格子与预览尺寸来自
// ui_layout_definition 的 equipment 行（没有该行时取下面的缺省布局）。游戏接入时只替换这些资源与数据行，面板代码不改。
//
// 判断记录（缺省布局）：没有 equipment 行时锚点 left_center、2 列、格子取皮肤包槽位框原生尺寸、预览取预览区背景原生尺寸，保证只有框架数据也画得出来。
// 判断记录（点击槽位 = 卸下）：与背包"点击使用"对称，点击已装备的槽位经 UiIntents.Unequip 卸下；空槽点击无操作。
// 判断记录（交互与换皮肤，ADR-0152）：AttachInteraction 之后每个槽位有悬停提示框（已装备时）、可把已装备的物品拖回背包卸下，并是"从背包拖来穿上"的落点
// （槽位合法 = 物品模板的槽位等于本槽位，非法显示 target_blocked，落下被拒绝且装备不变）；UiVisuals.SwitchSkin 之后面板整体重建（保持当前预览方向），
// 交互自动重新挂上。重建后 Cells/Preview/Background 都是新对象，持有旧引用的调用方需重新取。
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Adapter.Unity.Ui.Panels
{
    public sealed class EquipmentPanel : UiPanelBehaviour
    {
        private const float Padding = 12f;
        private const float Spacing = 6f;
        private const float ButtonHeight = 26f;
        private const float ButtonWidth = 36f;

        /// <summary>缺省布局（没有 equipment 行时）。</summary>
        public static readonly UiPanelLayout DefaultLayout = new UiPanelLayout("left_center", 2, 0f, 1f, string.Empty, "front");

        /// <summary>一个槽位格子的控件引用。</summary>
        public sealed class SlotCell
        {
            public Id SlotId;
            public string SlotName = string.Empty;
            public RectTransform Root = null!;
            public Image Frame = null!;
            public Button Button = null!;
            public Image Quality = null!;
            public Image Icon = null!;
            public TextMeshProUGUI Label = null!;
        }

        private RectTransform _parent = null!;
        private EquipmentViewModel _vm = null!;
        private UiVisuals _visuals = null!;
        private UiIntents? _intents;
        private UiInteraction? _interaction;
        private readonly List<UiDropTarget> _targets = new List<UiDropTarget>();
        private readonly List<SlotCell> _cells = new List<SlotCell>();
        private TextMeshProUGUI _directionLabel = null!;

        public IReadOnlyList<SlotCell> Cells => _cells;

        /// <summary>点击任一槽位（空槽也触发）时发出，参数是槽位 id；宿主没有 <see cref="UiIntents"/> 时（实验室换装场景）用它自接线。</summary>
        public event System.Action<Id>? SlotClicked;

        public PaperdollPreview Preview { get; private set; } = null!;

        public UiPanelLayout Layout { get; private set; }

        /// <summary>格子边长（像素）：布局的 cell_size，为 0 时取皮肤包槽位框的原生宽度。</summary>
        public float CellSize { get; private set; }

        /// <summary>面板底板（九宫格背景）。</summary>
        public RectTransform Background { get; private set; } = null!;

        public void Construct(RectTransform parent, EquipmentViewModel vm, UiVisuals visuals, UiIntents? intents) =>
            Construct(parent, vm, visuals, intents, visuals.LayoutOf(UiPanel.Equipment, DefaultLayout));

        /// <summary>显式给布局的重载（宿主没有 equipment 数据行又想指定身体层精灵集等布局参数时用，如实验室换装场景）。</summary>
        public void Construct(RectTransform parent, EquipmentViewModel vm, UiVisuals visuals, UiIntents? intents, UiPanelLayout layout)
        {
            _parent = parent;
            _vm = vm;
            _visuals = visuals;
            _intents = intents;
            visuals.SkinChanged -= OnSkinChanged;
            visuals.SkinChanged += OnSkinChanged;
            Build(layout);
        }

        /// <summary>挂上悬停提示框与拖放（换皮肤重建后自动重新挂上）。</summary>
        public void AttachInteraction(UiInteraction interaction)
        {
            ClearTargets();
            _interaction = interaction;
            HookInteraction();
        }

        /// <summary>每个槽位的落点（槽位序同 <see cref="Cells"/>；没有挂交互时为空）。</summary>
        public IReadOnlyList<UiDropTarget> DropTargets => _targets;

        private void OnSkinChanged()
        {
            if (this == null)
            {
                return;
            }

            var old = Background;
            var direction = Preview != null ? Preview.Direction : Layout.PreviewDirection;
            ClearTargets();
            if (old != null)
            {
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }

            Build(new UiPanelLayout(Layout.Anchor, Layout.Columns, Layout.CellSize, Layout.PreviewScale, Layout.PreviewBodySet, direction));
            RefreshUi();
        }

        private void OnDestroy()
        {
            if (_visuals != null)
            {
                _visuals.SkinChanged -= OnSkinChanged;
            }

            ClearTargets();
        }

        private void ClearTargets()
        {
            if (_interaction != null)
            {
                foreach (var target in _targets)
                {
                    _interaction.Drag.Unregister(target);
                }
            }

            _targets.Clear();
        }

        private void HookInteraction()
        {
            if (_interaction == null)
            {
                return;
            }

            var interaction = _interaction;
            ClearTargets();
            var registry = _visuals.Registry;
            for (var i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                var handler = cell.Root.GetComponent<UiItemCell>() ?? cell.Root.gameObject.AddComponent<UiItemCell>();
                handler.Interaction = interaction;
                handler.Resolve = () =>
                {
                    var snapshot = FindSlot(cell.SlotId);
                    return snapshot is { Occupied: true } slot
                        ? new UiDragPayload(UiDragOrigin.Equipment, slot.InstanceId!.Value, slot.TemplateId!.Value, slot.SlotId, cell.Icon.enabled ? cell.Icon.sprite : null)
                        : null;
                };

                var slotId = cell.SlotId;
                var target = new UiDropTarget(
                    "slot:" + cell.SlotName,
                    cell.Root,
                    payload =>
                    {
                        if (payload.Origin == UiDragOrigin.Bag)
                        {
                            return registry != null && ItemTooltipBuilder.FitsSlot(registry, payload.TemplateId, slotId) ? UiDropState.Ok : UiDropState.Blocked;
                        }

                        return payload.SourceSlot.HasValue && payload.SourceSlot.Value.Equals(slotId) ? UiDropState.None : UiDropState.Blocked;
                    },
                    payload =>
                    {
                        if (payload.Origin != UiDragOrigin.Bag)
                        {
                            return UiDropResult.Rejected("equip", "Blocked");
                        }

                        if (registry == null || !ItemTooltipBuilder.FitsSlot(registry, payload.TemplateId, slotId))
                        {
                            return UiDropResult.Rejected("equip", Core.Carriers.Common.EquipFailureReason.SlotMismatch.ToString());
                        }

                        if (interaction.Actions == null)
                        {
                            return UiDropResult.Rejected("equip", "NoAction");
                        }

                        var result = interaction.Actions.Equip(payload.InstanceId, slotId);
                        return result.Success ? new UiDropResult(true, "equip", string.Empty) : UiDropResult.Rejected("equip", result.Reason.ToString());
                    });
                interaction.Drag.Register(target);
                _targets.Add(target);
            }
        }

        private EquipmentSlotSnapshot? FindSlot(Id slotId)
        {
            foreach (var slot in _vm.Slots)
            {
                if (slot.SlotId.Equals(slotId))
                {
                    return slot;
                }
            }

            return null;
        }

        private void Build(UiPanelLayout layout)
        {
            var visuals = _visuals;
            var parent = _parent;
            var vm = _vm;
            _cells.Clear();
            Layout = layout;

            var defaultFrame = visuals.Pack.SlotFrameDefault();
            CellSize = UiPanelLayout.ResolveCellSize(Layout.CellSize, defaultFrame);
            var columns = Mathf.Max(1, Layout.Columns);
            var rows = Mathf.Max(1, Mathf.CeilToInt(vm.Slots.Count / (float)columns));
            var gridW = columns * CellSize + (columns - 1) * Spacing;
            var gridH = rows * CellSize + (rows - 1) * Spacing;

            var previewBackground = visuals.Pack.PaperdollBackground();
            var previewW = previewBackground.rect.width * Layout.PreviewScale;
            var previewH = previewBackground.rect.height * Layout.PreviewScale;

            var width = Padding + gridW + Padding + previewW + Padding;
            var height = Padding + Mathf.Max(gridH, previewH + Spacing + ButtonHeight) + Padding;

            Background = UiWidgets.CreatePanelBackground("EquipmentPanel", parent, Vector2.zero, Vector2.zero, new Vector2(width, height), Vector2.zero);
            Layout.ApplyAnchor(Background, new Vector2(width, height));

            // 槽位网格（左）：从左上角起按行排布。
            for (var i = 0; i < vm.Slots.Count; i++)
            {
                var slot = vm.Slots[i];
                var cell = CreateCell(Background, slot.SlotId, slot.SlotName);
                var col = i % columns;
                var row = i / columns;
                var rect = cell.Root;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(CellSize, CellSize);
                rect.anchoredPosition = new Vector2(Padding + col * (CellSize + Spacing), -(Padding + row * (CellSize + Spacing)));
                _cells.Add(cell);
            }

            // 预览区（右上）+ 方向切换按钮（预览区下方）。
            Preview = new PaperdollPreview(Background, visuals, Layout.PreviewBodySet, Layout.PreviewScale, Layout.PreviewDirection);
            var previewRect = Preview.Root;
            previewRect.anchorMin = previewRect.anchorMax = previewRect.pivot = new Vector2(1f, 1f);
            previewRect.anchoredPosition = new Vector2(-Padding, -Padding);

            var buttonY = -(Padding + previewH + Spacing);
            var prev = UiWidgets.CreateButton("DirectionPrev", Background, "<", () => { Preview.CycleDirection(-1); RefreshUi(); });
            var next = UiWidgets.CreateButton("DirectionNext", Background, ">", () => { Preview.CycleDirection(1); RefreshUi(); });
            PlaceTopRight(prev.Root, new Vector2(-(Padding + previewW - ButtonWidth), buttonY), new Vector2(ButtonWidth, ButtonHeight));
            PlaceTopRight(next.Root, new Vector2(-Padding, buttonY), new Vector2(ButtonWidth, ButtonHeight));
            _directionLabel = UiWidgets.CreateLabel("DirectionLabel", Background, Preview.Direction, 14, TextAlignmentOptions.Center);
            PlaceTopRight(
                _directionLabel.rectTransform,
                new Vector2(-(Padding + ButtonWidth), buttonY),
                new Vector2(Mathf.Max(10f, previewW - 2f * ButtonWidth), ButtonHeight));
            if (_interaction != null)
            {
                HookInteraction();
            }
        }

        private static void PlaceTopRight(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        private SlotCell CreateCell(RectTransform parent, Id slotId, string slotName)
        {
            var root = new GameObject("Slot_" + slotName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            var frame = root.GetComponent<Image>();
            frame.sprite = _visuals.Pack.SlotFrame(slotName);
            frame.type = Image.Type.Simple;
            frame.preserveAspect = true;

            var icon = CreateChildImage("Icon", rect, 3f);
            var quality = CreateChildImage("Quality", rect, 0f);
            var label = UiWidgets.CreateLabel("Label", rect, slotName, 10, TextAlignmentOptions.Bottom);
            UiWidgets.SetRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(2f, 1f), new Vector2(-2f, -1f));
            label.overflowMode = TextOverflowModes.Ellipsis;

            // 槽位格状态由皮肤包的槽位框状态图切换：悬停 _highlight、按下 _pressed（缺省取悬停图）、选中 _selected（缺省取悬停图）、禁用 _disabled；常态是本槽位的框。
            var button = root.GetComponent<Button>();
            button.targetGraphic = frame;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = _visuals.Pack.SlotSpriteState();

            var cell = new SlotCell { SlotId = slotId, SlotName = slotName, Root = rect, Frame = frame, Button = button, Quality = quality, Icon = icon, Label = label };
            var captured = slotId;
            button.onClick.AddListener(() => OnSlotClicked(captured));
            return cell;
        }

        private static Image CreateChildImage(string name, RectTransform parent, float inset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            UiWidgets.SetRect(rect, Vector2.zero, Vector2.one, new Vector2(inset, inset), new Vector2(-inset, -inset));
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.enabled = false;
            return image;
        }

        private void OnSlotClicked(Id slotId)
        {
            SlotClicked?.Invoke(slotId);
            foreach (var slot in _vm.Slots)
            {
                if (slot.SlotId.Equals(slotId) && slot.Occupied)
                {
                    _intents?.Unequip(slotId);
                    return;
                }
            }
        }

        public override void RefreshUi()
        {
            for (var i = 0; i < _cells.Count && i < _vm.Slots.Count; i++)
            {
                var slot = _vm.Slots[i];
                var cell = _cells[i];
                if (slot.Occupied)
                {
                    var iconSprite = _visuals.Icon(slot.IconId);
                    cell.Icon.sprite = iconSprite;
                    cell.Icon.enabled = iconSprite != null;
                    cell.Quality.sprite = _visuals.Pack.QualityFrame(slot.QualityName);
                    cell.Quality.enabled = true;
                    cell.Label.text = string.Empty;
                }
                else
                {
                    cell.Icon.sprite = null;
                    cell.Icon.enabled = false;
                    cell.Quality.enabled = false;
                    cell.Label.text = slot.SlotName;
                }
            }

            Preview.Refresh(_vm.PaperdollLayers);
            _directionLabel.text = Preview.Direction;
        }
    }
}
