#nullable enable
// UiDragController：背包/装备格子的拖放（手感设计/08 第 3 节、ADR-0152）。
//
// 流程：格子上的 UiItemCell 在开始拖拽时交出 UiDragPayload；本控制器显示跟随指针的影子（皮肤包 drag/ghost.png 做底，物品图标叠在上面），
// 指针每移动一次就在已登记的落点（UiDropTarget）里找指针下面的那个，按落点自己的判断显示 target_ok（可放置）或 target_blocked（不可放置）叠层，
// 松手时对指针下的落点调用它的 Drop：可放置的穿戴/卸下，不可放置的拒绝并带出原因，装备状态不变。
// 命中判断用控制器自己的矩形检测（RectTransformUtility.RectangleContainsScreenPoint），不依赖 EventSystem 的射线，所以拖拽进行中指针下的落点也能被测试直接驱动。
//
// 判断记录（没有皮肤声明时）：影子与叠层取 UiSkinPack.DragState，没有声明皮肤时 UiSkinPack 取框架占位包的同名文件，占位包也缺时是程序生成的纯色框，
// 所以任何皮肤配置下都画得出来；切换皮肤后由 UiInteraction 调 RebuildVisuals 换成新包的图。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using UnityEngine;
using UnityEngine.UI;

namespace Adapter.Unity.Ui
{
    public enum UiDragOrigin
    {
        /// <summary>从背包格子拖出（目标是装备槽位：穿上）。</summary>
        Bag,

        /// <summary>从装备槽位拖出（目标是背包：卸下）。</summary>
        Equipment,
    }

    /// <summary>落点对被拖物品的判断。</summary>
    public enum UiDropState
    {
        /// <summary>这个落点与该拖拽无关（不显示叠层，松手什么也不发生）。</summary>
        None,

        /// <summary>可放置（target_ok）。</summary>
        Ok,

        /// <summary>不可放置（target_blocked）。</summary>
        Blocked,
    }

    /// <summary>被拖物品的描述。</summary>
    public sealed class UiDragPayload
    {
        public UiDragOrigin Origin { get; }

        public Id InstanceId { get; }

        public Id TemplateId { get; }

        /// <summary>从装备槽位拖出时的来源槽位 id；从背包拖出为 null。</summary>
        public Id? SourceSlot { get; }

        public Sprite? Icon { get; }

        public UiDragPayload(UiDragOrigin origin, Id instanceId, Id templateId, Id? sourceSlot, Sprite? icon)
        {
            Origin = origin;
            InstanceId = instanceId;
            TemplateId = templateId;
            SourceSlot = sourceSlot;
            Icon = icon;
        }
    }

    /// <summary>一次落下的结果。</summary>
    public readonly struct UiDropResult
    {
        /// <summary>是否真的改变了装备状态（穿上/卸下成功）。</summary>
        public bool Accepted { get; }

        /// <summary><c>equip</c> / <c>unequip</c> / 空（没有落在任何落点上）。</summary>
        public string Action { get; }

        /// <summary>失败原因（装备载体的 <c>EquipFailureReason</c> 名，或 <c>NoTarget</c>/<c>Blocked</c>）；成功为空。</summary>
        public string Reason { get; }

        public UiDropResult(bool accepted, string action, string reason)
        {
            Accepted = accepted;
            Action = action;
            Reason = reason;
        }

        public static UiDropResult Rejected(string action, string reason) => new UiDropResult(false, action, reason);
    }

    /// <summary>一个落点：矩形 + 对被拖物品的判断 + 落下时的动作。</summary>
    public sealed class UiDropTarget
    {
        public string Name { get; }

        public RectTransform Rect { get; }

        public Func<UiDragPayload, UiDropState> Evaluate { get; }

        public Func<UiDragPayload, UiDropResult> Drop { get; }

        /// <summary>落点上的叠层图（由控制器登记时创建，target_ok/target_blocked 的精灵在悬停时赋上）。</summary>
        public Image? Mark { get; internal set; }

        public UiDropTarget(string name, RectTransform rect, Func<UiDragPayload, UiDropState> evaluate, Func<UiDragPayload, UiDropResult> drop)
        {
            Name = name;
            Rect = rect;
            Evaluate = evaluate;
            Drop = drop;
        }
    }

    public sealed class UiDragController : IDisposable
    {
        private readonly UiInteraction _owner;
        private readonly List<UiDropTarget> _targets = new List<UiDropTarget>();
        private RectTransform? _ghost;
        private Image? _ghostFrame;
        private Image? _ghostIcon;

        public bool IsDragging => Payload != null;

        public UiDragPayload? Payload { get; private set; }

        /// <summary>指针下当前的落点（没有为 null）与它对被拖物品的判断。</summary>
        public UiDropTarget? Hover { get; private set; }

        public UiDropState State { get; private set; }

        /// <summary>最近一次落下的结果（还没落下过为 null）。</summary>
        public UiDropResult? LastDrop { get; private set; }

        /// <summary>拖拽影子根节点（拖拽中可见）。</summary>
        public RectTransform Ghost => _ghost ?? throw new InvalidOperationException("拖拽影子已销毁");

        public Image GhostFrame => _ghostFrame!;

        public Image GhostIcon => _ghostIcon!;

        public bool GhostVisible => _ghost != null && _ghost.gameObject.activeSelf;

        public IReadOnlyList<UiDropTarget> Targets => _targets;

        public UiDragController(UiInteraction owner)
        {
            _owner = owner;
            BuildGhost();
        }

        private void BuildGhost()
        {
            var go = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            _ghost = (RectTransform)go.transform;
            _ghost.SetParent(_owner.Content, false);
            _ghost.anchorMin = _ghost.anchorMax = _ghost.pivot = new Vector2(0.5f, 0.5f);
            go.GetComponent<CanvasGroup>().blocksRaycasts = false;
            _ghostFrame = go.GetComponent<Image>();
            _ghostFrame.sprite = _owner.Visuals.Pack.DragState("ghost");
            _ghostFrame.type = Image.Type.Simple;
            _ghostFrame.raycastTarget = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.SetParent(_ghost, false);
            UiWidgets.SetRect(iconRect, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            _ghostIcon = iconGo.GetComponent<Image>();
            _ghostIcon.preserveAspect = true;
            _ghostIcon.raycastTarget = false;
            go.SetActive(false);
        }

        /// <summary>登记一个落点，并在它的矩形里建一张默认关闭的叠层图。</summary>
        public void Register(UiDropTarget target)
        {
            if (_targets.Contains(target))
            {
                return;
            }

            var go = new GameObject("DropMark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(target.Rect, false);
            UiWidgets.SetRect(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var mark = go.GetComponent<Image>();
            mark.type = Image.Type.Simple;
            mark.raycastTarget = false;
            mark.enabled = false;
            target.Mark = mark;
            _targets.Add(target);
        }

        public void Unregister(UiDropTarget target)
        {
            _targets.Remove(target);
            if (Hover == target)
            {
                Hover = null;
                State = UiDropState.None;
            }

            if (target.Mark != null)
            {
                UnityEngine.Object.Destroy(target.Mark.gameObject);
                target.Mark = null;
            }
        }

        public void Begin(UiDragPayload payload, Vector2 screenPosition)
        {
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            LastDrop = null;
            _owner.Tooltip.Hide();
            var size = _targets.Count > 0 ? CellSizeHint() : 48f;
            _ghost!.sizeDelta = new Vector2(size, size);
            _ghostIcon!.sprite = payload.Icon;
            _ghostIcon.enabled = payload.Icon != null;
            _ghost.gameObject.SetActive(true);
            _ghost.SetAsLastSibling();
            Move(screenPosition);
        }

        private float CellSizeHint()
        {
            var size = 0f;
            foreach (var target in _targets)
            {
                // 取最小的落点边长作影子大小（落点里的格子比背包面板整体小得多）。
                var side = Mathf.Min(target.Rect.rect.width, target.Rect.rect.height);
                if (side > 8f && (size <= 0f || side < size))
                {
                    size = side;
                }
            }

            return size > 0f ? size : 48f;
        }

        public void Move(Vector2 screenPosition)
        {
            if (Payload == null)
            {
                return;
            }

            _ghost!.anchoredPosition = _owner.ScreenToLocal(screenPosition);
            var camera = _owner.Camera;
            UiDropTarget? hit = null;
            var hitArea = 0f;
            var state = UiDropState.None;
            foreach (var target in _targets)
            {
                if (!target.Rect.gameObject.activeInHierarchy || !RectTransformUtility.RectangleContainsScreenPoint(target.Rect, screenPosition, camera))
                {
                    continue;
                }

                var evaluated = target.Evaluate(Payload);
                if (evaluated == UiDropState.None)
                {
                    continue;
                }

                // 指针下有多个落点重叠时取面积最小的（槽位格比背包面板整体小，格子优先；与视觉上"最上层"一致）。
                var area = target.Rect.rect.width * target.Rect.rect.height;
                if (hit == null || area < hitArea)
                {
                    hit = target;
                    hitArea = area;
                    state = evaluated;
                }
            }

            Hover = hit;
            State = state;
            foreach (var target in _targets)
            {
                if (target.Mark == null)
                {
                    continue;
                }

                if (target == hit)
                {
                    target.Mark.sprite = _owner.Visuals.Pack.DragState(state == UiDropState.Ok ? "target_ok" : "target_blocked");
                    target.Mark.enabled = true;
                }
                else
                {
                    target.Mark.enabled = false;
                }
            }
        }

        /// <summary>松手：对指针下的落点执行落下动作，收起影子与叠层，返回结果。</summary>
        public UiDropResult End(Vector2 screenPosition)
        {
            if (Payload == null)
            {
                return UiDropResult.Rejected(string.Empty, "NoDrag");
            }

            Move(screenPosition);
            var payload = Payload;
            var result = Hover == null || State == UiDropState.None
                ? UiDropResult.Rejected(string.Empty, "NoTarget")
                : Hover.Drop(payload);
            LastDrop = result;
            Cancel();
            return result;
        }

        /// <summary>放弃拖拽（不触发任何落下动作）。</summary>
        public void Cancel()
        {
            Payload = null;
            Hover = null;
            State = UiDropState.None;
            foreach (var target in _targets)
            {
                if (target.Mark != null)
                {
                    target.Mark.enabled = false;
                }
            }

            if (_ghost != null)
            {
                _ghost.gameObject.SetActive(false);
            }
        }

        /// <summary>按当前皮肤包重建影子的外观（落点叠层的精灵在悬停时才取，天然是新包的）。</summary>
        public void RebuildVisuals()
        {
            var wasDragging = Payload != null;
            DestroyGhost();
            BuildGhost();
            if (wasDragging)
            {
                Cancel();
            }
        }

        private void DestroyGhost()
        {
            if (_ghost != null)
            {
                _ghost.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(_ghost.gameObject);
            }

            _ghost = null;
        }

        public void Dispose()
        {
            Cancel();
            DestroyGhost();
            _targets.Clear();
        }
    }
}
