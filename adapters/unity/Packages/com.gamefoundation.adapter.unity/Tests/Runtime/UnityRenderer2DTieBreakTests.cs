#nullable enable
// UnityRenderer2DTieBreakTests：ADR-0104（消费方反馈第五十二批）验收——同层且 sortY 逐位相同的两个
// 精灵实例，前后绘制顺序须由 IRenderConventionHost.TieBreakComparer 确定且可经
// IRenderer2D.CompareDrawOrder 读回，不再是 URP 2D Renderer Transparency Sort Axis 兜底下"逐位相同
// 时次级排序同样是平局，结果由引擎内部实现决定"的不确定状态（消费方复现：近战贴身，玩家与敌人脚下
// sortY 逐位相同）。
//
// 实测记录（修前基线，2026-09-27，本机 Unity 6000.3.23f1）：临时把
// UnityRenderer2D.cs/GameFoundationBootstrap.cs/FrameworkResidentHost.cs 三个文件还原到本次改动之前
// （git checkout 到未改动版本），用一份不调用任何新增 API（不调 SetSortIdentity/CompareDrawOrder/
// TieBreakComparer）、只用既有 SetTransform + 手工挂纯色 SpriteRenderer + 相机渲染到 RenderTexture
// 读像素的临时探针复现下方同一场景，连续 3 次独立跑、每次 5 帧、每帧打乱两个精灵的 SetTransform
// 调用顺序，实测结果三次全部是"重叠区恒为后创建的精灵（unit.b）颜色，5 帧不变"——即修前在本次单进程
// 受控场景下是"碰巧全绿"（未观测到跨帧互换，与消费方在真实游戏里长时间运行、多单位混合排序观察到
// 的闪烁不矛盾——本探针只是单次短暂窗口，不能代表引擎内部平局规则在所有条件下都稳定），而不是稳定
// 复现闪烁；按任务书规则改用 CompareDrawOrder 是否返回非 0 作为红绿分界——修前 UnityRenderer2D 未
// 声明 SetSortIdentity/CompareDrawOrder/TieBreakComparer 三个成员，本文件调用 _renderer.
// CompareDrawOrder(...)（经具体类型 UnityRenderer2D 而非接口调用）在修前版本上直接编译不通过，是比
// "运行期返回 0" 更强的红信号；恢复本次改动后二者都能观察到：CompareDrawOrder 从编译不通过/恒定 0
// 变为返回与 TieBreakComparer 一致的非零符号，像素断言也从"巧合正确"变为"按规则确定正确"。
using System.Collections;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UnityRenderer2DTieBreakTests : PlayModeTestBase
    {
        private GameObject _rootGo = null!;
        private UnityResourceLoader _resourceLoader = null!;
        private UnityRenderer2D _renderer = null!;
        private GameObject _cameraGo = null!;
        private Camera _camera = null!;
        private RenderTexture _renderTexture = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("RendererRoot");
            _resourceLoader = new UnityResourceLoader();
            _renderer = new UnityRenderer2D(_rootGo.transform, _resourceLoader);
            _renderer.TieBreakComparer = new RenderConventionHost().TieBreakComparer;

            _cameraGo = new GameObject("TieBreakTestCamera");
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 1f;
            _camera.transform.position = new Vector3(0f, 0f, -10f);
            _camera.transform.rotation = Quaternion.identity;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.cullingMask = ~0;

            _renderTexture = new RenderTexture(8, 8, 16);
            _camera.targetTexture = _renderTexture;
        }

        [TearDown]
        public void TearDown()
        {
            _camera.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(_renderTexture);
            Object.DestroyImmediate(_cameraGo);
            Object.DestroyImmediate(_rootGo);
        }

        private static Sprite CreateSolidSprite(Color32 color)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color32[16];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels32(pixels);
            texture.Apply();
            // pixelsPerUnit=4：4x4 像素贴图覆盖 1x1 世界单位，配合下方相机 orthographicSize=1
            // （可视半高 1 世界单位）足够看清中心重叠区。
            return Sprite.Create(texture, new UnityEngine.Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }

        /// <summary>直接在 <see cref="UnityRenderer2D.GetLayersRoot"/> 下挂一个纯色 <see cref="SpriteRenderer"/>，
        /// 不经 <see cref="IRenderer2D.SetLayers"/>/资源加载——本测试只关心"两个精灵实例之间的前后
        /// 绘制顺序"，用可肉眼/像素区分的纯色比用真实资源解析路径（未注册资源会解析成占位方块，
        /// 两个实例的占位方块颜色相同、无法用像素区分）更直接、更少无关变量。</summary>
        private SpriteRenderer AttachSolidRenderer(SpriteHandle handle, Color32 color)
        {
            var layersRoot = _renderer.GetLayersRoot(handle)!;
            var child = new GameObject("SolidColorProbe");
            child.transform.SetParent(layersRoot, worldPositionStays: false);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateSolidSprite(color);
            return renderer;
        }

        private Color32 ReadCenterPixel()
        {
            _camera.Render();
            var previousActive = RenderTexture.active;
            RenderTexture.active = _renderTexture;
            var readback = new Texture2D(_renderTexture.width, _renderTexture.height, TextureFormat.RGBA32, false);
            readback.ReadPixels(new UnityEngine.Rect(0, 0, _renderTexture.width, _renderTexture.height), 0, 0);
            readback.Apply();
            var pixel = readback.GetPixel(_renderTexture.width / 2, _renderTexture.height / 2);
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(readback);
            return pixel;
        }

        /// <summary>
        /// 复现用例：两个不透明纯色精灵（蓝=unit.a，Id 较小；红=unit.b，Id 较大），同层、sortY 逐位
        /// 相同、位置完全重叠；连续 5 帧、每帧打乱两者 SetTransform 调用顺序；断言每帧重叠区都等于
        /// "Id 较大者"（红，见 <see cref="IRenderConventionHost.TieBreakComparer"/> 契约注释/
        /// ADR-0104 决策 1"Id 更大者在前"）的颜色，且 <see cref="IRenderer2D.CompareDrawOrder"/> 每帧
        /// 符号一致——a（Id 较小，语义上在后方/先绘制）与 b 比较应恒 &lt; 0。修前基线见类型顶部实测
        /// 记录。
        /// </summary>
        [UnityTest]
        public IEnumerator Reproduces_OverlappingSameSortY_DrawOrderStableAcrossFrames_RegardlessOfSetTransformCallOrder()
        {
            var handleA = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));
            var handleB = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));
            _renderer.SetSortIdentity(handleA, new Id("unit.a"));
            _renderer.SetSortIdentity(handleB, new Id("unit.b"));

            var blue = new Color32(0, 0, 255, 255);
            var red = new Color32(255, 0, 0, 255);
            AttachSolidRenderer(handleA, blue);
            AttachSolidRenderer(handleB, red);

            const double sharedSortY = 1.2345678901234; // 逐位相同的浮点值，模拟消费方"脚下 sortY 逐位相同"复现场景。
            for (var frame = 0; frame < 5; frame++)
            {
                if (frame % 2 == 0)
                {
                    _renderer.SetTransform(handleA, Vec2.Zero, 0, sharedSortY, 0, 0, 1, false);
                    _renderer.SetTransform(handleB, Vec2.Zero, 0, sharedSortY, 0, 0, 1, false);
                }
                else
                {
                    _renderer.SetTransform(handleB, Vec2.Zero, 0, sharedSortY, 0, 0, 1, false);
                    _renderer.SetTransform(handleA, Vec2.Zero, 0, sharedSortY, 0, 0, 1, false);
                }

                yield return null;

                var pixel = ReadCenterPixel();
                Assert.Greater(pixel.r, (byte)128, $"frame {frame}：重叠区红通道应占主导（Id 较大者 unit.b 在前）");
                Assert.Less(pixel.b, (byte)128, $"frame {frame}：重叠区蓝通道应被压制（Id 较小者 unit.a 在后）");

                var cmpAB = _renderer.CompareDrawOrder(handleA, handleB);
                var cmpBA = _renderer.CompareDrawOrder(handleB, handleA);
                Assert.Less(cmpAB, 0, $"frame {frame}：a（Id 较小）应先绘制/在后方，CompareDrawOrder(a, b) 应 < 0");
                Assert.Greater(cmpBA, 0, $"frame {frame}：CompareDrawOrder(b, a) 应与 (a, b) 反号");
            }
        }

        // -----------------------------------------------------------------
        // 不变量：sortY 不同的两精灵，sortingOrder 公式与 1.83.0 一致，不进入平局分组、偏移恒为 0。
        // -----------------------------------------------------------------
        [Test]
        public void Invariant_DifferentSortY_NoTieBreakGroup_SortingOrderUnchangedAndNoOffset()
        {
            var handleA = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));
            var handleB = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));
            _renderer.SetSortIdentity(handleA, new Id("unit.a"));
            _renderer.SetSortIdentity(handleB, new Id("unit.b"));

            _renderer.SetTransform(handleA, new Vec2(0, 2.5), 0, sortY: 2.5, layer: 1, rotation: 0, scale: 1, flipX: false);
            _renderer.SetTransform(handleB, new Vec2(0, 4.6), 0, sortY: 4.6, layer: 1, rotation: 0, scale: 1, flipX: false);

            var rootA = _renderer.GetSpriteRoot(handleA)!;
            var rootB = _renderer.GetSpriteRoot(handleB)!;
            // 与既有 UnityRenderer2DTests.SetTransform_SetsPositionAndComputesSortingOrderFromLayerAndSortY
            // 同一公式：layer * 1000 - round(sortY, AwayFromZero)，未叠加任何平局偏移。
            Assert.AreEqual(2.5f, rootA.transform.localPosition.y, 0.0001f, "sortY 不同时不应叠加平局偏移");
            Assert.AreEqual(4.6f, rootB.transform.localPosition.y, 0.0001f, "sortY 不同时不应叠加平局偏移");
            Assert.AreEqual(1 * 1000 - 3, rootA.GetComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder);
            Assert.AreEqual(1 * 1000 - 5, rootB.GetComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder);
        }

        // -----------------------------------------------------------------
        // 不变量：平局组中一方 sortY 变化离组后，另一方偏移归零。
        // -----------------------------------------------------------------
        [Test]
        public void Invariant_OneMemberLeavesGroup_RemainingMemberOffsetResetsToZero()
        {
            var handleA = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));
            var handleB = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));
            _renderer.SetSortIdentity(handleA, new Id("unit.a"));
            _renderer.SetSortIdentity(handleB, new Id("unit.b"));

            const double sharedSortY = 3.14159265;
            _renderer.SetTransform(handleA, new Vec2(0, 0), 0, sharedSortY, 0, 0, 1, false);
            _renderer.SetTransform(handleB, new Vec2(0, 0), 0, sharedSortY, 0, 0, 1, false);

            var rootA = _renderer.GetSpriteRoot(handleA)!;
            var rootB = _renderer.GetSpriteRoot(handleB)!;
            // 组内两成员：a（Id 较小，k=0，偏移 0）、b（Id 较大，k=1，偏移 -epsilon）——两者 Y 应不同。
            Assert.AreNotEqual(rootA.transform.localPosition.y, rootB.transform.localPosition.y,
                "同组两名成员分到的平局偏移应不同，Y 坐标不应恰好相等");
            Assert.AreEqual(0.0, (double)rootA.transform.localPosition.y - 0.0, 0.0001,
                "组内比较器最靠前（Id 最小）的成员偏移应为 0");

            // b 的 sortY 变化，离开与 a 共享的组。
            _renderer.SetTransform(handleB, new Vec2(0, 0), 0, sortY: sharedSortY + 1.0, layer: 0, rotation: 0, scale: 1, flipX: false);

            // a 此刻是组内唯一成员，偏移应归零，落回未叠加偏移的原始 Y（0）。
            Assert.AreEqual(0f, rootA.transform.localPosition.y, 0.0001f,
                "b 离组后，a 作为组内唯一剩余成员，偏移应归零");
        }

        // -----------------------------------------------------------------
        // 不变量：三个同 sortY 实例的绘制顺序 = 比较器全序，与创建顺序无关。
        // -----------------------------------------------------------------
        [Test]
        public void Invariant_ThreeInstancesSameSortY_OrderMatchesComparerFullOrder_RegardlessOfCreationOrder()
        {
            // 故意按与 Id 字典序相反的顺序创建（c 先建、a 最后建），验证组内顺序只看比较器，不看创建
            // 顺序/句柄分配顺序。
            var handleC = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));
            var handleB = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));
            var handleA = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));
            _renderer.SetSortIdentity(handleC, new Id("unit.c"));
            _renderer.SetSortIdentity(handleB, new Id("unit.b"));
            _renderer.SetSortIdentity(handleA, new Id("unit.a"));

            const double sharedSortY = 7.0;
            _renderer.SetTransform(handleC, Vec2.Zero, 0, sharedSortY, 0, 0, 1, false);
            _renderer.SetTransform(handleB, Vec2.Zero, 0, sharedSortY, 0, 0, 1, false);
            _renderer.SetTransform(handleA, Vec2.Zero, 0, sharedSortY, 0, 0, 1, false);

            // 全序应为 a < b < c（Id 升序，见 TieBreakComparer 契约），a 最先绘制（最靠后方）、
            // c 最后绘制（最靠前方）：CompareDrawOrder(a, b) < 0 < CompareDrawOrder(b, a)，
            // 且 a 与 c 之间差距应是 a 与 b 之间差距的整数倍方向一致（同一比较器全序）。
            Assert.Less(_renderer.CompareDrawOrder(handleA, handleB), 0);
            Assert.Less(_renderer.CompareDrawOrder(handleB, handleC), 0);
            Assert.Less(_renderer.CompareDrawOrder(handleA, handleC), 0);
            Assert.Greater(_renderer.CompareDrawOrder(handleC, handleA), 0);
            Assert.Greater(_renderer.CompareDrawOrder(handleC, handleB), 0);
            Assert.Greater(_renderer.CompareDrawOrder(handleB, handleA), 0);
        }
    }
}
