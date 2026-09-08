#nullable enable
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    /// <summary>独立审计 probe，仅存在于本次 Unity 副本，用于验证共享 AnimationClip 状态。</summary>
    public sealed class PresentationClipSharedStateProbe : PlayModeTestBase
    {
        private static readonly Id AttackClipRef = new Id("anim.attack");
        private UnityEngineHost _host = null!;
        private AnimationEvent[] _originalEvents = null!;

        [SetUp]
        public void SetUp()
        {
            _host = UnityEngineHost.Ensure();
            _originalEvents = LoadClip().events;
        }

        [TearDown]
        public void TearDown()
        {
            LoadClip().events = _originalEvents;
        }

        private AnimationClip LoadClip()
        {
            Assert.IsTrue(_host.ResourceLoader.TryLoadAnimationClipSync(AttackClipRef, out var clip));
            return clip;
        }

        private static bool HasEvent(AnimationClip clip, string name)
        {
            foreach (var evt in clip.events)
            {
                if (evt.functionName == UnityRenderer3D.AnimEventFunctionName && evt.stringParameter == name)
                {
                    return true;
                }
            }

            return false;
        }

        private (ModelHandle Handle, Animator Animator) CreateModel()
        {
            var handle = _host.Renderer3D.CreateModelInstance(new Id("model.placeholder_biped"));
            var animator = _host.Renderer3D.GetModelVisualRoot(handle)!.GetComponentInChildren<Animator>();
            Assert.IsNotNull(animator);
            return (handle, animator!);
        }

        private static UnityViewFactory NewFactory(UnityEngineHost host) =>
            new UnityViewFactory(host.Renderer2D, new RenderConventionHost(), new NullDisplayInfoRegistry(),
                host.ResourceLoader, renderer3D: host.Renderer3D);

        private static void AssertNoOverride(Animator animator) =>
            Assert.IsNull(animator.runtimeAnimatorController as AnimatorOverrideController);

        [Test]
        public void EmptyAnimSetAfterConfiguredAnimSet_InheritsEarlierDataEventFromSharedClip()
        {
            var clip = LoadClip();
            var factory = NewFactory(_host);
            var a = CreateModel();
            factory.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("probe_a", 0.2) }), a.Handle);
            var b = CreateModel();
            factory.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, System.Array.Empty<AnimClipEventSpec>()), b.Handle);
            AssertNoOverride(b.Animator);
            var actual = HasEvent(clip, "probe_a");
            Debug.Log($"PRESENTATION17 CLIP_EMPTY_AFTER_A expected_B_probe_a=false actual_shared_probe_a={actual} b_override={b.Animator.runtimeAnimatorController is AnimatorOverrideController}");
            Assert.IsTrue(actual, "probe: empty B shares A's data event through base AnimationClip.events");
            _host.Renderer3D.DestroyModelInstance(a.Handle);
            _host.Renderer3D.DestroyModelInstance(b.Handle);
        }

        [Test]
        public void EmptyAnimSetBeforeConfiguredAnimSet_IsLaterPollutedBySharedClipWrite()
        {
            var clip = LoadClip();
            var factory = NewFactory(_host);
            var b = CreateModel();
            factory.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, System.Array.Empty<AnimClipEventSpec>()), b.Handle);
            var a = CreateModel();
            factory.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("probe_a", 0.2) }), a.Handle);
            var actual = HasEvent(clip, "probe_a");
            Debug.Log($"PRESENTATION17 CLIP_EMPTY_BEFORE_A expected_B_probe_a=false actual_shared_probe_a={actual} b_override={b.Animator.runtimeAnimatorController is AnimatorOverrideController}");
            AssertNoOverride(b.Animator);
            Assert.IsTrue(actual, "probe: B's shared playback sees A's later base clip write");
            _host.Renderer3D.DestroyModelInstance(a.Handle);
            _host.Renderer3D.DestroyModelInstance(b.Handle);
        }

        [Test]
        public void DifferentUnityViewFactory_CapturesPreviousFactoryDataAsAuthoredBaseline()
        {
            var clip = LoadClip();
            var factoryA = NewFactory(_host);
            var a = CreateModel();
            factoryA.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("probe_a", 0.2) }), a.Handle);
            var factoryB = NewFactory(_host);
            var b = CreateModel();
            factoryB.RegisterModelClipEventsForTest(
                new AnimClipDef(AttackClipRef, new[] { new AnimClipEventSpec("probe_b", 0.8) }), b.Handle);
            var hasA = HasEvent(clip, "probe_a");
            var hasB = HasEvent(clip, "probe_b");
            Debug.Log($"PRESENTATION17 CLIP_CROSS_FACTORY expected_factoryB_probe_a=false actual_shared_probe_a={hasA} actual_shared_probe_b={hasB} b_override={b.Animator.runtimeAnimatorController is AnimatorOverrideController}");
            AssertNoOverride(b.Animator);
            Assert.IsTrue(hasA, "probe: factory B treats factory A data as authored baseline");
            Assert.IsTrue(hasB);
            _host.Renderer3D.DestroyModelInstance(a.Handle);
            _host.Renderer3D.DestroyModelInstance(b.Handle);
        }

        private sealed class NullDisplayInfoRegistry : IDisplayInfoRegistry
        {
            public Core.Foundation.DisplayInfo.DisplayInfo? Lookup(Id logicalId) => null;
            public System.Collections.Generic.IReadOnlyList<Core.Foundation.DisplayInfo.DisplayInfo> LookupByCategory(DisplayCategory category) => System.Array.Empty<Core.Foundation.DisplayInfo.DisplayInfo>();
            public System.Collections.Generic.IReadOnlyList<Core.Foundation.DisplayInfo.DisplayInfo> All => System.Array.Empty<Core.Foundation.DisplayInfo.DisplayInfo>();
            public void Reload() { }
        }
    }
}
