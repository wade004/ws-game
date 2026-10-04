#nullable enable
// UiSkinGallery：把界面资源契约清单里的每一个元素各画一遍（手感设计/08 第 3 节、ADR-0149）。
//
// 用途有二：1）美术/出图工具核对一整套皮肤包的实际观感（实验室里挂一个就能一眼看全：槽位框、品质框、拖拽态、提示框、预览区背景、面板底图、按钮各状态）；
// 2）完整性用例逐元素核对"换了皮肤包之后每个被渲染的元素都引用新资源"。九宫格元素按若干个不同尺寸各画一份（用来核对拉伸时四角不变形——
// 看精灵的 border 数据与 Image 的像素密度），可选元素缺失时记一条"未画"的条目（沿用框架默认外观，不是问题）。
//
// 判断记录（画廊只读皮肤包）：精灵一律经 UiSkinPack.ElementSprite 取（与面板用的类型化取用共享同一份缓存，所以同一元素在画廊与面板里是同一个精灵对象），
// 画廊不改任何皮肤状态；位置只做简单的从左到右折行，不依赖布局组件（尺寸可控，测试读到的就是设定值）。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Adapter.Unity.Ui
{
    public sealed class UiSkinGallery
    {
        /// <summary>画廊里的一个已渲染图。</summary>
        public sealed class Entry
        {
            public UiSkinFile File;
            public Image Image = null!;

            /// <summary>九宫格元素这一份的目标尺寸（其它元素为原生尺寸）。</summary>
            public Vector2 Size;
        }

        public RectTransform Root { get; }

        /// <summary>已渲染的图（九宫格元素每个尺寸一份）。</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>清单展开的全部图片文件（含没画的可选缺失项）。</summary>
        public IReadOnlyList<UiSkinFile> Files => _files;

        /// <summary>没有画（可选元素在本包与占位皮肤里都没有）的文件。</summary>
        public IReadOnlyList<UiSkinFile> Absent => _absent;

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<UiSkinFile> _files = new List<UiSkinFile>();
        private readonly List<UiSkinFile> _absent = new List<UiSkinFile>();

        private UiSkinGallery(RectTransform root) => Root = root;

        /// <summary>九宫格元素默认画的几种尺寸（像素）。</summary>
        public static readonly Vector2[] DefaultSliceSizes = { new Vector2(48f, 48f), new Vector2(160f, 96f), new Vector2(320f, 200f) };

        public static UiSkinGallery Build(
            RectTransform parent, UiSkinPack pack, UiSkinManifest manifest,
            IReadOnlyList<string> slots, IReadOnlyList<string> qualities, IReadOnlyList<Vector2>? sliceSizes = null)
        {
            var sizes = sliceSizes ?? DefaultSliceSizes;
            var rootGo = new GameObject("UiSkinGallery", typeof(RectTransform));
            var root = (RectTransform)rootGo.transform;
            root.SetParent(parent, false);
            UiWidgets.SetRect(root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var gallery = new UiSkinGallery(root);

            const float width = 1200f;
            var x = 8f;
            var y = -8f;
            var rowHeight = 0f;
            foreach (var file in manifest.Expand(slots, qualities))
            {
                if (!file.Element.IsImage)
                {
                    continue;
                }

                gallery._files.Add(file);
                var sprite = pack.ElementSprite(file);
                if (sprite == null)
                {
                    gallery._absent.Add(file);
                    continue;
                }

                var targets = new List<Vector2>();
                if (file.Element.IsNineSlice)
                {
                    targets.AddRange(sizes);
                }
                else
                {
                    targets.Add(new Vector2(sprite.rect.width, sprite.rect.height));
                }

                foreach (var size in targets)
                {
                    if (x + size.x + 8f > width)
                    {
                        x = 8f;
                        y -= rowHeight + 8f;
                        rowHeight = 0f;
                    }

                    var go = new GameObject("Skin_" + file.Path.Replace('/', '_'), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    var rect = (RectTransform)go.transform;
                    rect.SetParent(root, false);
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                    rect.sizeDelta = size;
                    rect.anchoredPosition = new Vector2(x, y);
                    var image = go.GetComponent<Image>();
                    image.sprite = sprite;
                    image.type = file.Element.IsNineSlice ? Image.Type.Sliced : Image.Type.Simple;
                    image.raycastTarget = false;
                    gallery._entries.Add(new Entry { File = file, Image = image, Size = size });
                    x += size.x + 8f;
                    rowHeight = Mathf.Max(rowHeight, size.y);
                }
            }

            return gallery;
        }
    }
}
