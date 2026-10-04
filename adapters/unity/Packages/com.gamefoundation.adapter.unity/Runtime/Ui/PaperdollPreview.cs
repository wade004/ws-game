#nullable enable
// PaperdollPreview：装备面板的纸娃娃预览（手感设计/08 第 3 节、ADR-0149）——按装备图层叠放静态层精灵，方向可切换。
//
// 数据与资源全部由约定给出：身体层取布局行的 preview_body_set（<集>/<方向>/body.png），装备层取视图模型的纸娃娃图层清单
// （每件装备外观行的 mesh_ref → 层精灵集，<集>/<方向>/<层名>.png，层名取外观行 slot_id 去 slot. 前缀）；叠放次序 = 身体层在下，
// 装备层按视图模型顺序（槽位 sort_weight）依次在上。方向取五个已制作方向档（front/front_side_r/side_r/back_side_r/back，
// 与精灵集 authored_directions 同一组）；缺失的另一侧由渲染层镜像，本预览不镜像（只显示已制作方向）。
//
// 判断记录（图像对象数 = 图层数，与加载是否完成无关）：每个装备层固定对应一个 Image 子物体，精灵到位前它是隐藏的；
// 因此"预览里的装备层数 = 视图模型的纸娃娃图层数 = 有 sprite 外观的已装备槽位数"随时可核对，不依赖异步加载的进度。
// 判断记录（尺寸由资源算出）：预览框尺寸 = 皮肤包预览区背景精灵的原生尺寸 × preview_scale；层精灵按"身体层精灵高度 → 预览框高度 90%"的统一比例缩放
// （没有身体层时按各层自己的高度），不写死像素。预览框用矩形遮罩裁掉超出部分（占位装备层画布比身体层大）。
using System;
using System.Collections.Generic;
using Presentation.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Adapter.Unity.Ui
{
    public sealed class PaperdollPreview
    {
        /// <summary>五个已制作方向档，预览方向切换的循环顺序。</summary>
        public static readonly string[] Directions = { "front", "front_side_r", "side_r", "back_side_r", "back" };

        private readonly UiVisuals _visuals;
        private readonly string _bodySet;
        private readonly List<LayerImage> _layers = new List<LayerImage>();
        private Image? _body;
        private int _directionIndex;

        private sealed class LayerImage
        {
            public Image Image = null!;
            public string SpriteSet = string.Empty;
            public string Layer = string.Empty;
            public string AppliedDirection = string.Empty;
        }

        /// <summary>预览框根节点（背景 + 遮罩）。</summary>
        public RectTransform Root { get; }

        /// <summary>预览框背景图（皮肤包 paperdoll_preview/background.png）。</summary>
        public Image Background { get; }

        public string Direction => Directions[_directionIndex];

        /// <summary>装备层图像对象数（不含身体层）。</summary>
        public int EquipmentLayerCount => _layers.Count;

        /// <summary>装备层图像里已经拿到精灵（可见）的个数。</summary>
        public int EquipmentLayersWithSprite
        {
            get
            {
                var n = 0;
                foreach (var l in _layers)
                {
                    if (l.Image.sprite != null)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        public bool HasBody => _body != null;

        public bool BodyHasSprite => _body != null && _body.sprite != null;

        /// <summary>每个装备层图像：层名、精灵集目录名、图像（测试与实验室读取用）。</summary>
        public IReadOnlyList<(string Layer, string SpriteSet, Image Image)> EquipmentLayers
        {
            get
            {
                var list = new List<(string, string, Image)>();
                foreach (var l in _layers)
                {
                    list.Add((l.Layer, l.SpriteSet, l.Image));
                }

                return list;
            }
        }

        public Image? BodyImage => _body;

        public PaperdollPreview(Transform parent, UiVisuals visuals, string bodySet, float scale, string initialDirection)
        {
            _visuals = visuals;
            _bodySet = bodySet;
            var index = Array.IndexOf(Directions, initialDirection);
            _directionIndex = index >= 0 ? index : 0;

            var go = new GameObject("PaperdollPreview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
            Root = (RectTransform)go.transform;
            Root.SetParent(parent, false);
            var background = visuals.Pack.PaperdollBackground();
            Background = go.GetComponent<Image>();
            Background.sprite = background;
            Background.type = Image.Type.Simple;
            Background.raycastTarget = false;
            Root.sizeDelta = new Vector2(background.rect.width * scale, background.rect.height * scale);

            if (!string.IsNullOrEmpty(bodySet))
            {
                _body = CreateImage("Body");
            }
        }

        private Image CreateImage(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(Root, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.enabled = false;
            return image;
        }

        /// <summary>切到下一个/上一个方向档（<paramref name="step"/> 为 ±1）。</summary>
        public void CycleDirection(int step)
        {
            _directionIndex = ((_directionIndex + step) % Directions.Length + Directions.Length) % Directions.Length;
        }

        public void SetDirection(string direction)
        {
            var index = Array.IndexOf(Directions, direction);
            if (index >= 0)
            {
                _directionIndex = index;
            }
        }

        /// <summary>让图像对象清单与图层清单一致（数量与层名变化时才重建），并尝试给还没有精灵的图层补上（异步加载完成后调用即可）。</summary>
        public void Refresh(IReadOnlyList<EquipmentPaperdollLayer> layers)
        {
            var same = layers.Count == _layers.Count;
            for (var i = 0; same && i < layers.Count; i++)
            {
                same = string.Equals(_layers[i].Layer, layers[i].Layer, StringComparison.Ordinal)
                    && string.Equals(_layers[i].SpriteSet, UiVisuals.SetName(layers[i].MeshRef.Value), StringComparison.Ordinal);
            }

            if (!same)
            {
                foreach (var l in _layers)
                {
                    UnityEngine.Object.Destroy(l.Image.gameObject);
                }

                _layers.Clear();
                foreach (var layer in layers)
                {
                    _layers.Add(new LayerImage
                    {
                        Image = CreateImage("Layer_" + layer.Layer),
                        SpriteSet = UiVisuals.SetName(layer.MeshRef.Value),
                        Layer = layer.Layer,
                    });
                }
            }

            Apply();
        }

        private void Apply()
        {
            var direction = Direction;
            float k = 0f;
            if (_body != null)
            {
                var body = _visuals.Layer(_bodySet, direction, "body");
                _body.sprite = body;
                _body.enabled = body != null;
                if (body != null)
                {
                    k = Root.sizeDelta.y * 0.9f / body.rect.height;
                    SetSize(_body, body, k);
                }
            }

            foreach (var l in _layers)
            {
                var sprite = _visuals.Layer(l.SpriteSet, direction, l.Layer);
                l.Image.sprite = sprite;
                l.Image.enabled = sprite != null;
                l.AppliedDirection = direction;
                if (sprite != null)
                {
                    SetSize(l.Image, sprite, k > 0f ? k : Root.sizeDelta.y * 0.9f / sprite.rect.height);
                }
            }
        }

        private static void SetSize(Image image, Sprite sprite, float k)
        {
            ((RectTransform)image.transform).sizeDelta = new Vector2(sprite.rect.width * k, sprite.rect.height * k);
        }
    }
}
