#nullable enable
// AvFeedbackPlayModeTests：镜头与音画反馈（ADR-0148，手感设计/07）的 Unity 侧落地验收——
// 缩放脉冲（UnityCamera.ZoomPunch）、手柄震动（UnityRumble，经注入的马达输出）、闪白玩家强度（FlashReceiver.IntensityScale）、
// 同名动画事件的重复关键帧（UnityViewFactory.ComputeKeyframes）、精灵残影（AfterimageEmitter）。
// 期望值都由规则算出：缩放脉冲 = 基准半高 x (1 - 幅度 x (1 - 已过时间/衰减时长))；残影数 = 时间/间隔，淡出 = 起始 x 线性余量。
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using NUnit.Framework;
using Presentation.Common;
using Presentation.Render;
using Presentation.ViewBinding;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:engine_adapter")]
    public sealed class AvFeedbackPlayModeTests : PlayModeTestBase
    {
        private GameObject _rootGo = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("AvFeedbackRoot");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_rootGo);
        }

        // ------------------------------------------------------------------ 缩放脉冲

        private UnityCamera MakeCamera(double zoom)
        {
            var go = new GameObject("AvCamera");
            go.transform.SetParent(_rootGo.transform);
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            var unity = new UnityCamera(camera);
            unity.Configure(45, 0, new ZoomRange(1, 20));
            unity.SetZoom(zoom);
            return unity;
        }

        [Test]
        public void ZoomPunch_NarrowsTheVisibleHalfHeightByTheMagnitude_ThenLinearlyReturnsToBase()
        {
            var camera = MakeCamera(10);
            const double magnitude = 0.1;
            const double decayMs = 200;

            camera.ZoomPunch(magnitude, decayMs);

            Assert.IsTrue(camera.SupportsCameraZoomPunch);
            Assert.AreEqual(10.0 * (1.0 - magnitude), camera.EffectiveZoom, 1e-9, "峰值：可视半高收窄 magnitude");
            Assert.AreEqual(10.0, camera.CurrentZoom, 1e-9, "基准缩放不变");

            camera.Tick(decayMs / 1000.0 / 2); // 过半：剩 50%
            Assert.AreEqual(10.0 * (1.0 - magnitude * 0.5), camera.EffectiveZoom, 1e-6);

            camera.Tick(decayMs / 1000.0); // 到期
            Assert.AreEqual(10.0, camera.EffectiveZoom, 1e-9, "脉冲结束后逐位回到基准缩放");
        }

        [Test]
        public void ZoomPunch_StackedPunches_AreCappedAtHalfTheView_AndInvalidOnesIgnored()
        {
            var camera = MakeCamera(10);
            for (var i = 0; i < 20; i++) camera.ZoomPunch(0.1, 200); // 合计 2.0，远超上限
            camera.ZoomPunch(0, 200);
            camera.ZoomPunch(0.1, 0);

            Assert.AreEqual(20, camera.ZoomPunchCount, "幅度或衰减非正的调用忽略、不计数");
            Assert.AreEqual(10.0 * 0.5, camera.EffectiveZoom, 1e-9, "叠加后合计收窄不超过 50%");
        }

        [Test]
        public void ZoomPunch_DoesNotChangeTheVisibleHalfHeightUsedByImpulseConversion()
        {
            var camera = MakeCamera(10);
            var before = camera.VisibleHalfHeight;
            camera.ZoomPunch(0.2, 200);
            Assert.AreEqual(before, camera.VisibleHalfHeight, 1e-9, "镜头冲击的画面比例按基准缩放换算，不含脉冲");
        }

        // ------------------------------------------------------------------ 手柄震动

        [Test]
        public void Rumble_DrivesBothMotors_AndTicksBackToZero()
        {
            var outputs = new List<(double Low, double High)>();
            var rumble = new UnityRumble((low, high) => outputs.Add((low, high)));

            rumble.Rumble(0.5, 100);

            Assert.IsTrue(rumble.SupportsRumble);
            Assert.AreEqual(1, outputs.Count);
            Assert.AreEqual(0.5, outputs[0].Low, 1e-9);
            Assert.AreEqual(0.5 * UnityRumble.HighFrequencyRatio, outputs[0].High, 1e-9);

            rumble.Tick(0.05);
            Assert.AreEqual(0.5, rumble.CurrentStrength, 1e-9, "未到期仍在震");
            Assert.AreEqual(0.05, rumble.RemainingSeconds, 1e-9);

            rumble.Tick(0.06);
            Assert.AreEqual(0.0, rumble.CurrentStrength, 1e-9);
            Assert.AreEqual((0.0, 0.0), outputs[outputs.Count - 1], "到期马达归零");
        }

        [Test]
        public void Rumble_OverlappingTakesTheLargerStrengthAndLongerRemaining_AndClampsToOne()
        {
            var rumble = new UnityRumble((_, __) => { });
            rumble.Rumble(0.3, 100);
            rumble.Rumble(0.8, 50);
            Assert.AreEqual(0.8, rumble.CurrentStrength, 1e-9);
            Assert.AreEqual(0.1, rumble.RemainingSeconds, 1e-9, "时长取剩余较长者，不累加");

            rumble.Rumble(5.0, 10);
            Assert.AreEqual(1.0, rumble.CurrentStrength, 1e-9);

            rumble.Rumble(0, 100);
            rumble.Rumble(0.5, 0);
            Assert.AreEqual(3, rumble.RumbleCount, "强度或时长非正的调用忽略");

            rumble.Stop();
            Assert.AreEqual(0.0, rumble.CurrentStrength, 1e-9);
        }

        // ------------------------------------------------------------------ 闪白玩家强度

        private sealed class OneEntitySnapshot : ISimSnapshot
        {
            public Vec2 GetPosition(Id entityId) => Vec2.Zero;
            public double GetFacing(Id entityId) => 0.0;
            public double GetHeight(Id entityId) => 0.0;
            public bool Exists(Id entityId) => true;
            public Id? GetDisplayId(Id entityId) => null;
            public ViewKind? GetKind(Id entityId) => ViewKind.Unit;
        }

        private sealed class FixedViewFactory : IViewFactory
        {
            private readonly IView _view;

            public FixedViewFactory(IView view)
            {
                _view = view;
            }

            public IView CreateView(ViewKind kind, Id displayId, Id entityId) => _view;
        }

        private static IEventBus NewBus()
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, System.Array.Empty<string>()));
            }

            return new EventBus(EventCatalog.FromDefinitions(definitions), new EventBusOptions { StrictCatalog = false, AuditLog = false });
        }

        private (FlashReceiver Receiver, UnitySpriteView View, SpriteRenderer Layer) MakeFlashFixture(System.Func<double>? scale)
        {
            var loader = new UnityResourceLoader();
            var renderer = new UnityRenderer2D(_rootGo.transform, loader);
            var info = DisplayInfoTestSupportForAnimTests.CreateSpriteInfo();
            var view = new UnitySpriteView(renderer, new RenderConventionHost(), info, loader);
            var registry = new FakeDisplayInfoRegistryForAnim();
            registry.Add(info);
            var binder = new ViewBinder(NewBus(), new FixedViewFactory(view), new OneEntitySnapshot(), registry);
            var entity = new Id("unit.av_flash");
            binder.OnEntityCreated(entity, Core.Foundation.SimLoop.EntityKinds.Creature, info.Id);
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), 0.0);
            var layer = renderer.GetSpriteRoot(view.EngineHandle)!.transform.Find("LayersRoot").GetChild(0).GetComponent<SpriteRenderer>();
            var receiver = new FlashReceiver(binder) { IntensityScale = scale };
            receiver.Show(entity, new Id("flash.test"));
            return (receiver, view, layer);
        }

        [Test]
        public void Flash_IntensityScaleHalf_ScalesTheOverexposure_ZeroTurnsItOff()
        {
            // 对照：缩放 1 时的过曝量（rgb 超出 1 的部分）。
            var (full, _, fullLayer) = MakeFlashFixture(() => 1.0);
            var fullOver = fullLayer.color.r - 1f;
            Assert.Greater(fullOver, 0f, "系数 1 时闪白过曝");
            Assert.AreEqual(1, full.TriggerCount);

            var (half, _, halfLayer) = MakeFlashFixture(() => 0.5);
            Assert.AreEqual(fullOver * 0.5f, halfLayer.color.r - 1f, 1e-4f, "强度系数 0.5：过曝量减半");

            var (off, _, offLayer) = MakeFlashFixture(() => 0.0);
            Assert.AreEqual(0, off.TriggerCount, "系数 0 关闭闪白，不计触发");
            Assert.LessOrEqual(offLayer.color.r, 1f + 1e-6f, "系数 0：不过曝");
        }

        // ------------------------------------------------------------------ 重复关键帧

        [Test]
        public void ComputeKeyframes_RepeatedEventNames_AreAllKept_WithRepeatKeys()
        {
            var events = new List<AnimClipEventSpec>
            {
                new AnimClipEventSpec("footstep", 0.0),
                new AnimClipEventSpec("footstep", 0.5),
                new AnimClipEventSpec("hit_frame", 1.0),
            };

            var keyframes = UnityViewFactory.ComputeKeyframes(events, 9)!;

            Assert.AreEqual(3, keyframes.Count, "此前同名事件互相覆盖，只剩 2 项");
            Assert.AreEqual(0, keyframes[AnimMarkerNames.RepeatKey("footstep", 0)]);
            Assert.AreEqual(4, keyframes[AnimMarkerNames.RepeatKey("footstep", 1)], "0.5 x (9-1) = 4");
            Assert.AreEqual(8, keyframes["hit_frame"]);
        }

        // ------------------------------------------------------------------ 残影

        private (UnitySpriteView View, AfterimageEmitter? Emitter, GameObject Root) MakeAfterimageFixture()
        {
            var loader = new UnityResourceLoader();
            var renderer = new UnityRenderer2D(_rootGo.transform, loader);
            var view = new UnitySpriteView(renderer, new RenderConventionHost(), DisplayInfoTestSupportForAnimTests.CreateSpriteInfo(), loader);
            view.Bind(new Id("unit.av_afterimage"));
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), 0.0);
            var root = renderer.GetSpriteRoot(view.EngineHandle)!;
            return (view, root.GetComponent<AfterimageEmitter>(), root);
        }

        [Test]
        public void Afterimage_OnLeavesFadingCopiesAtTheInterval_OffLetsThemFadeOut()
        {
            var (view, none, root) = MakeAfterimageFixture();
            Assert.IsTrue(none == null, "未开启前不挂残影组件");

            view.SetAfterimage(true);
            var emitter = root.GetComponent<AfterimageEmitter>();
            Assert.IsTrue(emitter != null);
            Assert.IsTrue(emitter.Active);

            // 每个间隔留一个：10 个间隔 = 10 个，其中存活数不超过 寿命/间隔。
            var steps = 10;
            for (var i = 0; i < steps; i++) emitter.Step(AfterimageEmitter.SpawnIntervalSeconds);
            Assert.AreEqual(steps, emitter.SpawnedTotal);
            var alive = Mathf.RoundToInt(AfterimageEmitter.LifetimeSeconds / AfterimageEmitter.SpawnIntervalSeconds);
            Assert.LessOrEqual(emitter.GhostCount, alive, "存活残影数不超过 寿命/间隔");
            Assert.Greater(emitter.GhostCount, 0);

            view.SetAfterimage(false);
            Assert.IsFalse(emitter.Active);
            var before = emitter.SpawnedTotal;
            for (var i = 0; i < 6; i++) emitter.Step(AfterimageEmitter.SpawnIntervalSeconds);
            Assert.AreEqual(before, emitter.SpawnedTotal, "关闭后不再生成");
            Assert.AreEqual(0, emitter.GhostCount, "关闭后已留下的残影在寿命内淡出销毁");
        }

        [Test]
        public void Afterimage_Off_WithoutAnEmitter_IsASilentNoOp()
        {
            var (view, _, root) = MakeAfterimageFixture();
            view.SetAfterimage(false);
            Assert.IsTrue(root.GetComponent<AfterimageEmitter>() == null);
        }
    }
}
