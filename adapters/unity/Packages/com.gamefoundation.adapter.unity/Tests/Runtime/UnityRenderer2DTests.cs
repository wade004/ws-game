#nullable enable
using System.Collections;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
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

        /// <summary>W3b 审计发现补齐："EffectSequencePlayer.Play 是否被 UnityRenderer2DTests.EmitParticle_*
        /// 真正触发未知（vfx.sample_hit 是否解析到序列帧资源）"——既有用例的 <c>"vfx.sample_hit"</c>
        /// 不对应任何真实占位资源（不是 <c>UnityResourceLoader.ResolveEffectDir</c> 能解析到的目录，
        /// 见该方法命名规则），因此既有用例走的其实是回退到内建通用粒子的分支（<c>RentParticleSystem</c>，
        /// GameObject 名 <c>"ParticleEffect"</c>），从未真正触发过 <see cref="EffectSequencePlayer"/>。
        /// 本用例改用真实占位序列帧资源 <c>vfx.hit_spark</c>（assets/_placeholder/vfx/hit_spark/，
        /// 同 <c>UnityResourceLoaderTests.LoadAsync_PlaceholderHitSparkEffect_LoadsFramesSuccessfully</c>
        /// 已验证能加载成功），加载完成后再 EmitParticle，断言落地的是名为 <c>"EffectSequence"</c> 的
        /// <see cref="EffectSequencePlayer"/> 分支（而不是 <c>"ParticleEffect"</c> 回退分支），且其
        /// <see cref="SpriteRenderer"/> 已经切到第 0 帧真实贴图（不是占位洋红色方块）。</summary>
        [UnityTest]
        public IEnumerator EmitParticle_WithRealSequenceFrameResource_TriggersEffectSequencePlayer_NotFallback()
        {
            var effectId = new Id("vfx.hit_spark");
            bool? loadSuccess = null;
            _resourceLoader.LoadAsync(effectId, ResourceKind.Effect, (id, ok) => loadSuccess = ok);

            var timeout = 5f;
            while (loadSuccess == null && timeout > 0f)
            {
                _resourceLoader.Tick();
                yield return null;
                timeout -= Time.unscaledDeltaTime > 0 ? Time.unscaledDeltaTime : 0.02f;
            }
            Assert.IsTrue(loadSuccess == true, "占位 hit_spark 序列帧特效资源加载应当成功（先跑一次 build.ps1 -SyncContent）");

            var handle = _renderer.EmitParticle(effectId, new Vec2(1, 1), new System.Collections.Generic.Dictionary<string, double>());
            yield return null;

            var effectChild = _rootGo.transform.Find("EffectSequence");
            Assert.IsNotNull(effectChild, "解析到真实序列帧资源时应当落到 EffectSequencePlayer 分支（子物体名 \"EffectSequence\"），而不是回退到通用粒子");
            Assert.IsNull(_rootGo.transform.Find("ParticleEffect"), "不应当同时存在回退分支的 \"ParticleEffect\" 子物体");

            var player = effectChild!.GetComponent<EffectSequencePlayer>();
            Assert.IsNotNull(player, "EffectSequence 子物体应当挂有 EffectSequencePlayer 组件");
            var spriteRenderer = effectChild.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(spriteRenderer.sprite, "Play 被真正触发后 SpriteRenderer.sprite 应当已经切到第 0 帧真实贴图");

            Assert.DoesNotThrow(() => _renderer.StopParticle(handle));
        }
    }
}
