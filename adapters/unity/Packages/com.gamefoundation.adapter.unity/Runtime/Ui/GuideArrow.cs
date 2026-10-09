#nullable enable
// GuideArrow：任务目标指引的屏幕边缘箭头控件（ADR-0173）。
//
// 判断记录（缺省关闭）：本控件只是个被动的 uGUI 元素，不订阅、不轮询任何框架服务；游戏层每帧把
// "目标世界坐标"交给 Present，没有目标时调用 Hide。不接入的游戏不会出现任何箭头，行为与没有本控件时一致。
// 判断记录（数学与控件分离）：GuideArrowMath 是纯函数（只依赖 UnityEngine.Vector2/Rect），不碰 Camera/Canvas，
// 便于单测"目标在屏内/屏外/正中心时箭头落在哪、朝哪"；GuideArrow 只负责把世界坐标换成容器本地坐标并摆控件。
// 判断记录（美术）：框架不带美术资源，缺省箭头用代码画的抗锯齿三角形；游戏可通过 Sprite 属性换成自己的图
// （约定图朝向为 +X 即向右）。
using UnityEngine;
using UnityEngine.UI;

namespace Adapter.Unity.Ui
{
    /// <summary>箭头摆放结果：<see cref="Position"/> 是容器本地坐标（容器中心为原点），<see cref="AngleDegrees"/> 是箭头朝向
    /// （0 度朝右，逆时针为正）；<see cref="OnScreen"/> 为真表示目标就在可视范围内（此时箭头压在目标上方、朝下指着它）。</summary>
    public readonly struct GuideArrowPlacement
    {
        public bool OnScreen { get; }

        public Vector2 Position { get; }

        public float AngleDegrees { get; }

        public GuideArrowPlacement(bool onScreen, Vector2 position, float angleDegrees)
        {
            OnScreen = onScreen;
            Position = position;
            AngleDegrees = angleDegrees;
        }
    }

    public static class GuideArrowMath
    {
        /// <summary>目标在屏内（距边缘不少于 <paramref name="margin"/>）时箭头放在目标上方 <paramref name="onScreenLift"/> 处朝下；
        /// 否则从屏幕中心朝目标连线，落在向内收 <paramref name="margin"/> 的矩形边界上、朝向目标。</summary>
        public static GuideArrowPlacement Place(Vector2 targetLocal, Rect container, float margin, float onScreenLift)
        {
            var center = container.center;
            var half = new Vector2(Mathf.Max(0f, container.width * 0.5f - margin), Mathf.Max(0f, container.height * 0.5f - margin));
            var d = targetLocal - center;
            if (Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.y) <= half.y)
            {
                var lifted = targetLocal + new Vector2(0f, onScreenLift);
                lifted.y = Mathf.Min(lifted.y, center.y + half.y);
                return new GuideArrowPlacement(true, lifted, -90f);
            }

            var tx = Mathf.Abs(d.x) > 1e-4f ? half.x / Mathf.Abs(d.x) : float.PositiveInfinity;
            var ty = Mathf.Abs(d.y) > 1e-4f ? half.y / Mathf.Abs(d.y) : float.PositiveInfinity;
            var t = Mathf.Min(tx, ty);
            var pos = center + d * t;
            var angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            return new GuideArrowPlacement(false, pos, angle);
        }
    }

    /// <summary>屏幕边缘引导箭头：挂在一个铺满屏幕的 RectTransform（<see cref="Container"/>）下。</summary>
    public sealed class GuideArrow : MonoBehaviour
    {
        public const string ObjectName = "GuideArrow";

        private RectTransform? _container;
        private RectTransform? _arrow;
        private Image? _image;
        private Sprite? _sprite;

        /// <summary>箭头与屏幕边缘的最小留白（容器本地单位）。</summary>
        public float Margin { get; set; } = 56f;

        /// <summary>目标在屏内时，箭头抬在目标上方多少。</summary>
        public float OnScreenLift { get; set; } = 64f;

        public Color Color
        {
            get => _image != null ? _image.color : Color.white;
            set
            {
                if (_image != null) _image.color = value;
            }
        }

        /// <summary>自定义箭头图（朝向 +X）；为空时用内置三角形。</summary>
        public Sprite? Sprite
        {
            set
            {
                _sprite = value;
                if (_image != null) _image.sprite = value != null ? value : BuiltinSprite();
            }
        }

        public RectTransform? Container => _container;

        /// <summary>当前箭头是否可见（<see cref="PresentLocal"/>/<see cref="Present"/> 之后为真，<see cref="Hide"/> 之后为假）。</summary>
        public bool IsShown => _arrow != null && _arrow.gameObject.activeSelf;

        /// <summary>最近一次摆放结果；供测试与游戏读取。</summary>
        public GuideArrowPlacement LastPlacement { get; private set; }

        /// <summary>在 <paramref name="parent"/> 下建一个全屏容器和箭头。</summary>
        public static GuideArrow Create(Transform parent)
        {
            var root = UiWidgets.CreateRoot(ObjectName, parent);
            var arrow = root.gameObject.AddComponent<GuideArrow>();
            arrow.Build(root);
            return arrow;
        }

        private void Build(RectTransform container)
        {
            _container = container;
            var go = new GameObject("Arrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _arrow = (RectTransform)go.transform;
            _arrow.SetParent(container, false);
            _arrow.anchorMin = _arrow.anchorMax = new Vector2(0.5f, 0.5f);
            _arrow.sizeDelta = new Vector2(64f, 64f);
            _image = go.GetComponent<Image>();
            _image.sprite = _sprite != null ? _sprite : BuiltinSprite();
            _image.color = new Color(1f, 0.85f, 0.2f, 0.95f);
            _image.raycastTarget = false;
            go.SetActive(false);
        }

        /// <summary>把箭头指向世界坐标 <paramref name="worldPosition"/>：<paramref name="worldCamera"/> 把它投到屏幕，
        /// <paramref name="uiCamera"/> 是承载容器的画布所用相机（覆盖层画布传 null）。</summary>
        public void Present(Camera worldCamera, Vector3 worldPosition, Camera? uiCamera)
        {
            if (_container == null || _arrow == null)
            {
                return;
            }

            var screen = worldCamera.WorldToScreenPoint(worldPosition);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_container, screen, uiCamera, out var local))
            {
                return;
            }

            PresentLocal(local);
        }

        /// <summary>已知容器本地坐标时直接摆放（测试用，也供非相机投影的游戏使用）。</summary>
        public void PresentLocal(Vector2 targetLocal)
        {
            if (_container == null || _arrow == null)
            {
                return;
            }

            var placement = GuideArrowMath.Place(targetLocal, _container.rect, Margin, OnScreenLift);
            LastPlacement = placement;
            _arrow.anchoredPosition = placement.Position;
            _arrow.localRotation = Quaternion.Euler(0f, 0f, placement.AngleDegrees);
            if (!_arrow.gameObject.activeSelf)
            {
                _arrow.gameObject.SetActive(true);
            }
        }

        public void Hide()
        {
            if (_arrow != null && _arrow.gameObject.activeSelf)
            {
                _arrow.gameObject.SetActive(false);
            }
        }

        private static Sprite? _builtin;

        private static Sprite BuiltinSprite()
        {
            if (_builtin != null)
            {
                return _builtin;
            }

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "GuideArrowBuiltin",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[size * size];
            // 指向 +X 的实心三角形（外圈深色描边）：底边 x=8，尖端 x=60，半高从 24 线性收到 0。
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = (x - 8f) / 52f;
                    var inside = (u >= 0f && u <= 1f) ? (1f - u) * 24f - Mathf.Abs(y - 31.5f) : -1f;
                    var alpha = Mathf.Clamp01(inside + 0.5f);
                    if (alpha <= 0f)
                    {
                        px[y * size + x] = new Color32(0, 0, 0, 0);
                    }
                    else
                    {
                        var a = (byte)(alpha * 255f);
                        px[y * size + x] = inside < 3f ? new Color32(60, 40, 0, a) : new Color32(255, 255, 255, a);
                    }
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            _builtin = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _builtin.name = "GuideArrowBuiltin";
            return _builtin;
        }
    }
}
