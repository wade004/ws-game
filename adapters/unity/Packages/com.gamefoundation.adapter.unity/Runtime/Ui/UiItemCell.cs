#nullable enable
// UiItemCell：挂在背包格子/装备槽位上的指针事件处理（悬停出提示框、开始/进行/结束拖拽），把事件转给 UiInteraction 的 UiTooltip 与 UiDragController。
// 格子自己不知道提示框与拖拽的细节：只通过 <see cref="Resolve"/> 说明"这个格子现在装着什么"（空格子返回 null：既不出提示框也不能拖）。
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Adapter.Unity.Ui
{
    public sealed class UiItemCell : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private bool _dragging;
        private bool _hovering;

        public UiInteraction? Interaction { get; set; }

        /// <summary>这个格子当前装着的物品（空格子为 null）。</summary>
        public Func<UiDragPayload?>? Resolve { get; set; }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovering = true;
            ShowTooltip(eventData.position);
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (_hovering && Interaction != null && !_dragging)
            {
                if (Interaction.Tooltip.IsVisible)
                {
                    Interaction.Tooltip.Move(eventData.position);
                }
                else
                {
                    ShowTooltip(eventData.position);
                }
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovering = false;
            Interaction?.Tooltip.Hide();
        }

        private void ShowTooltip(Vector2 position)
        {
            if (Interaction == null || _dragging || Interaction.Drag.IsDragging)
            {
                return;
            }

            var payload = Resolve?.Invoke();
            var content = payload != null ? Interaction.Visuals.TooltipOf(payload.TemplateId) : null;
            if (content == null)
            {
                Interaction.Tooltip.Hide();
                return;
            }

            Interaction.Tooltip.Show(content, position);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            var payload = Resolve?.Invoke();
            if (Interaction == null || payload == null)
            {
                _dragging = false;
                return;
            }

            _dragging = true;
            Interaction.Drag.Begin(payload, eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_dragging)
            {
                Interaction?.Drag.Move(eventData.position);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_dragging)
            {
                _dragging = false;
                Interaction?.Drag.End(eventData.position);
            }
        }

        private void OnDisable()
        {
            if (_dragging)
            {
                _dragging = false;
                Interaction?.Drag.Cancel();
            }

            _hovering = false;
        }
    }
}
