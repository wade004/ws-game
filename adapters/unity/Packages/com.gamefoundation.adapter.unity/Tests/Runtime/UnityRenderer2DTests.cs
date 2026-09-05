#nullable enable
using System.Collections;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UnityRenderer2DTests : PlayModeTestBase
    {
        private GameObject _rootGo = null!;
        private UnityResourceLoader _resourceLoader = null!;
        private UnityRenderer2D _renderer = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("RendererRoot");
            _resourceLoader = new UnityResourceLoader();
            _renderer = new UnityRenderer2D(_rootGo.transform, _resourceLoader);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_rootGo);
        }

        [UnityTest]
        public IEnumerator CreateSpriteInstance_ThenDestroy_RemovesGameObject()
        {
            var handle = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));
            Assert.AreEqual(1, _rootGo.transform.childCount);

            _renderer.DestroySpriteInstance(handle);
            yield return null; // UnityEngine.Object.Destroy 是延迟到本帧末尾才真正生效的。

            Assert.AreEqual(0, _rootGo.transform.childCount);
        }

        [Test]
        public void DestroySpriteInstance_ThenReuse_Throws()
        {
            var handle = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));
            _renderer.DestroySpriteInstance(handle);

            Assert.Throws<System.InvalidOperationException>(() => _renderer.SetTransform(handle, Vec2.Zero, 0, 0, 0, 0, 1, false));
        }

        [Test]
        public void SetTransform_SetsPositionAndComputesSortingOrderFromLayerAndSortY()
        {
            var handle = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));

            _renderer.SetTransform(handle, new Vec2(1.5, 2.5), height: 0, sortY: 2.5, layer: 1, rotation: 0, scale: 1, flipX: false);

            var child = _rootGo.transform.GetChild(0);
            Assert.AreEqual(1.5f, child.localPosition.x, 0.001f);
            Assert.AreEqual(2.5f, child.localPosition.y, 0.001f);

            var sortingGroup = child.GetComponent<SortingGroup>();
            Assert.AreEqual(1 * 1000 - 3, sortingGroup.sortingOrder); // round(2.5, AwayFromZero) = 3
        }

        [Test]
        public void SetTransform_Height_MovesLayersRootLocalY_ButNotSortYOrSortingOrder()
        {
            // ADR-0016 决策 2：height 经 SetTransform 正式参数传递（取代此前借用
            // SetShaderParam("height_offset_px") 的工作绕），只平移 LayersRoot，不参与 sortY 排序。
            var handle = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));

            _renderer.SetTransform(handle, new Vec2(1.5, 2.5), height: 100.0, sortY: 2.5, layer: 1, rotation: 0, scale: 1, flipX: false);

            var child = _rootGo.transform.GetChild(0);
            var layersRoot = child.Find("LayersRoot")!;
            Assert.AreEqual(100.0 / _renderer.PixelsPerUnit, layersRoot.localPosition.y, 0.001f);

            // 根物体的位置只反映 position（不含 height），sortingOrder 只反映 layer/sortY。
            Assert.AreEqual(2.5f, child.localPosition.y, 0.001f);
            var sortingGroup = child.GetComponent<SortingGroup>();
            Assert.AreEqual(1 * 1000 - 3, sortingGroup.sortingOrder);
        }

        [Test]
        public void SetLayers_CreatesChildSpriteRenderersInListOrder()
        {
            var handle = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));

            _renderer.SetLayers(handle, new[] { new Id("layer.sample_wolf__front__body"), new Id("layer.sample_wolf__front__head") });

            var layersRoot = _rootGo.transform.GetChild(0).Find("LayersRoot")!;
            Assert.AreEqual(2, layersRoot.childCount);
            Assert.AreEqual("Layer_0", layersRoot.GetChild(0).name);
            Assert.AreEqual("Layer_1", layersRoot.GetChild(1).name);

            var renderer0 = layersRoot.GetChild(0).GetComponent<SpriteRenderer>();
            Assert.IsNotNull(renderer0.sprite); // 资源不存在时应回退到占位精灵，而不是 null。
        }

        [Test]
        public void SetShaderParam_FlashIntensity_StillWorks_HeightOffsetNoLongerSpecialCased()
        {
            // ADR-0016 决策 2 落地后，SetShaderParam 不再特殊处理 "height_offset_px"（高度改经
            // SetTransform 的 height 参数），其它命名参数（如 flash_intensity）行为不变。
            var handle = _renderer.CreateSpriteInstance(new Id("sprite.creature.sample_wolf"));
            _renderer.SetLayers(handle, new[] { new Id("layer.sample_wolf__front__body") });

            Assert.DoesNotThrow(() => _renderer.SetShaderParam(handle, "flash_intensity", 1.0));

            var layersRoot = _rootGo.transform.GetChild(0).Find("LayersRoot")!;
            var renderer0 = layersRoot.GetChild(0).GetComponent<SpriteRenderer>();
            Assert.AreEqual(new Color(2f, 2f, 2f, 1f), renderer0.color);
        }

        [UnityTest]
        public IEnumerator EmitParticle_ThenStop_ReturnsToPoolWithoutError()
        {
            var handle = _renderer.EmitParticle(new Id("vfx.sample_hit"), new Vec2(1, 1), new System.Collections.Generic.Dictionary<string, double>());
            yield return null;

            Assert.DoesNotThrow(() => _renderer.StopParticle(handle));
        }
    }
}
