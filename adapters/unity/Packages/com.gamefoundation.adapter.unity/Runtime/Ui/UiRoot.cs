#nullable enable
// UiRoot：Screen Space Canvas + InputSystemUIInputModule（任务书 U3-1"UiRoot（MonoBehaviour，
// Screen Space Canvas + InputSystemUIInputModule）+ UiPanelHost：按 ui_layout_definition 表登记
// 面板、开关、层叠与焦点"）。本类型只负责"画布本体"，具体面板登记见 UiPanelHost。
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Adapter.Unity.Ui
{
    [RequireComponent(typeof(Canvas))]
    public sealed class UiRoot : MonoBehaviour
    {
        public Canvas Canvas { get; private set; } = null!;

        public RectTransform Content { get; private set; } = null!;

        /// <summary>创建一个挂好 <see cref="Canvas"/>（Screen Space - Overlay）+ <see cref="CanvasScaler"/>
        /// + <see cref="GraphicRaycaster"/> 的根物体；若场景内尚无 <see cref="EventSystem"/>，一并
        /// 创建一个挂 <see cref="InputSystemUIInputModule"/> 的（任务书"Screen Space Canvas +
        /// InputSystemUIInputModule"）。</summary>
        public static UiRoot Create(string name = "UiRoot")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var root = go.AddComponent<UiRoot>();
            root.Canvas = go.GetComponent<Canvas>();
            root.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            root.Content = (RectTransform)contentGo.transform;
            root.Content.SetParent(go.transform, false);
            root.Content.anchorMin = Vector2.zero;
            root.Content.anchorMax = Vector2.one;
            root.Content.offsetMin = Vector2.zero;
            root.Content.offsetMax = Vector2.zero;

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Object.DontDestroyOnLoad(esGo);
            }

            return root;
        }
    }
}
