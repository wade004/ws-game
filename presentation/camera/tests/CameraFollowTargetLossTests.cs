using System;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Presentation.Camera;
using Presentation.Common;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.PresentationCamera
{
    /// <summary>
    /// ADR-0121 第 6 条（D6）：跟随目标丢失（被跟随实体销毁）时 <see cref="CameraHost.Update"/> 不抛、
    /// 保持最后位置；每次丢失只记一条诊断（多帧不刷）；目标重新出现则继续跟随。
    /// 期望位置由测试里的实体位置规则算出（不写裸数）。
    /// </summary>
    public class CameraFollowTargetLossTests
    {
        private static readonly Id TestMapId = new Id("map.test");
        private static readonly Id CameraProfileId = new Id("camera_profile.default");

        private sealed class LossTestEntity : Entity
        {
            public override string Kind => "test_entity";

            public LossTestEntity(Id entityId) : base(entityId, TestMapId)
            {
            }
        }

        private static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(Array.Empty<EventDefinition>());
            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false });
        }

        private static CameraProfile Profile(CameraBounds? bounds = null) =>
            new CameraProfile(CameraProfileId, pitchDegrees: 55, yawDegrees: 0, zoomMin: 5, zoomMax: 20, zoomDefault: 10, followLerp: 0.2, bounds: bounds);

        /// <summary>真实 <see cref="WorldSim"/> + <see cref="WorldSimSnapshot"/> 的跟随夹具。</summary>
        private sealed class Fixture
        {
            public IWorldSim World = null!;
            public StubCamera Camera = null!;
            public CameraHost Host = null!;
            public LossTestEntity Target = null!;

            /// <summary>目标重新出现：同 id 的新实体出现在给定位置。</summary>
            public void ReappearTarget(Vec2 position)
            {
                Target = new LossTestEntity(Target.EntityId) { Position = position };
                World.AddEntity(Target);
                World.Tick(SimStep.Continuous(0.016));
                Assert.NotNull(World.GetEntity(Target.EntityId));
            }

            /// <summary>把目标从世界中真正移除（销毁）。</summary>
            public void DestroyTarget()
            {
                World.MarkForDestruction(Target.EntityId);
                World.Tick(SimStep.Continuous(0.016));
                Assert.Null(World.GetEntity(Target.EntityId));
            }
        }

        private static Fixture Build(
            Func<ISimSnapshot, ICameraFollowTarget> targetFactory,
            CameraBounds? bounds = null,
            IPresentationDiagnostics? diagnostics = null)
        {
            var bus = CreateBus();
            var world = new WorldSim(bus);
            var target = new LossTestEntity(new Id("unit.follow_1")) { Position = new Vec2(3, 4) };
            world.AddEntity(target);
            world.Tick(SimStep.Continuous(0.016));

            var camera = new StubCamera();
            var host = new CameraHost(camera, targetFactory(new WorldSimSnapshot(world)), bus: null, options: null, diagnostics);
            host.Configure(Profile(bounds));
            host.Follow(target.EntityId);
            return new Fixture { World = world, Camera = camera, Host = host, Target = target };
        }

        private static ICameraFollowTarget SnapshotTarget(ISimSnapshot snapshot) => new SimSnapshotFollowTarget(snapshot);

        // ------------------------------------------------------------------
        // 复现：目标销毁后 Update 不抛、保持最后位置（修复前 InvalidOperationException 逐帧抛出）
        // ------------------------------------------------------------------

        [Fact]
        public void Update_FollowedEntityDestroyed_DoesNotThrowAndKeepsLastPosition()
        {
            var fx = Build(SnapshotTarget);
            fx.Host.Update(0.5);
            var lastPosition = fx.Camera.FollowTarget;
            Assert.Equal(fx.Target.Position, lastPosition); // 跟随中：相机目标 == 实体位置（规则算出）。

            fx.DestroyTarget();

            var caught = Record.Exception(() => fx.Host.Update(0.5));

            Assert.Null(caught);
            Assert.Equal(lastPosition, fx.Camera.FollowTarget); // 保持最后位置。
        }

        [Fact]
        public void Update_FollowedEntityDestroyed_DelegateFollowTarget_DoesNotThrowAndKeepsLastPosition()
        {
            // DelegateFollowTarget 包一层"查不到实体就抛"的解析函数（ViewBinder.GetInterpolatedPosition 同形）。
            var fx = Build(snapshot => new DelegateFollowTarget((id, alpha) => snapshot.GetPosition(id)));
            fx.Host.Update(0.5);
            var lastPosition = fx.Camera.FollowTarget;
            Assert.Equal(fx.Target.Position, lastPosition);

            fx.DestroyTarget();

            var caught = Record.Exception(() => fx.Host.Update(0.5));

            Assert.Null(caught);
            Assert.Equal(lastPosition, fx.Camera.FollowTarget);
        }

        // ------------------------------------------------------------------
        // 诊断：一次丢失一条，多帧不刷
        // ------------------------------------------------------------------

        [Fact]
        public void Update_TargetStaysLostForManyFrames_KeepsLastPositionAndRecordsSingleDiagnostic()
        {
            var fx = Build(SnapshotTarget);
            fx.Host.Update(0.5);
            var lastPosition = fx.Camera.FollowTarget;
            fx.DestroyTarget();

            const int frames = 30;
            for (var i = 0; i < frames; i++)
            {
                fx.Host.Update(i / (double)frames);
            }

            Assert.Equal(lastPosition, fx.Camera.FollowTarget);
            var diagnostic = Assert.Single(((PresentationDiagnosticsRecorder)fx.Host.Diagnostics).Warnings);
            Assert.Contains(fx.Target.EntityId.Value, diagnostic);
        }

        [Fact]
        public void Update_InjectedDiagnostics_ReceivesTheLostWarning()
        {
            var diagnostics = new PresentationDiagnosticsRecorder();
            var fx = Build(SnapshotTarget, diagnostics: diagnostics);
            fx.Host.Update(0.5);
            fx.DestroyTarget();

            fx.Host.Update(0.5);
            fx.Host.Update(0.5);

            Assert.Same(diagnostics, fx.Host.Diagnostics);
            Assert.Single(diagnostics.Warnings);
        }

        // ------------------------------------------------------------------
        // 目标重现：继续跟随；再次丢失是新的一次丢失事件
        // ------------------------------------------------------------------

        [Fact]
        public void Update_TargetReappears_ResumesFollowingAndSecondLossRecordsNewDiagnostic()
        {
            var fx = Build(SnapshotTarget);
            fx.Host.Update(0.5);
            var lastPosition = fx.Camera.FollowTarget;
            fx.DestroyTarget();
            fx.Host.Update(0.5);
            fx.Host.Update(0.5);
            var recorder = (PresentationDiagnosticsRecorder)fx.Host.Diagnostics;
            Assert.Single(recorder.Warnings);

            var reappearAt = lastPosition + new Vec2(7, -2);
            fx.ReappearTarget(reappearAt);
            fx.Host.Update(0.5);

            Assert.Equal(fx.Target.Position, fx.Camera.FollowTarget); // 跟随新出现的实体（位置由实体规则得出）。
            Assert.Equal(reappearAt, fx.Camera.FollowTarget);
            Assert.Single(recorder.Warnings); // 重现本身不新增诊断。

            // 继续移动仍被跟随。
            var moved = reappearAt + new Vec2(1, 1);
            fx.Target.Position = moved;
            fx.Host.Update(0.5);
            Assert.Equal(moved, fx.Camera.FollowTarget);

            // 再次丢失 = 新的一次丢失事件，再记一条（且仍保持第二次跟随到的最后位置）。
            fx.DestroyTarget();
            fx.Host.Update(0.5);
            fx.Host.Update(0.5);
            Assert.Equal(2, recorder.Warnings.Count);
            Assert.Equal(moved, fx.Camera.FollowTarget);
        }

        // ------------------------------------------------------------------
        // 边界与换目标
        // ------------------------------------------------------------------

        [Fact]
        public void Update_TargetLost_KeepsBoundsClampedLastPosition()
        {
            var bounds = new CameraBounds(new Vec2(-1, -1), new Vec2(2, 2));
            var fx = Build(SnapshotTarget, bounds);
            fx.Host.Update(0.5);
            var clamped = bounds.Clamp(fx.Target.Position);
            Assert.Equal(clamped, fx.Camera.FollowTarget);
            Assert.NotEqual(fx.Target.Position, clamped); // 夹取确实生效，用例才有区分度。

            fx.DestroyTarget();
            fx.Host.Update(0.5);

            Assert.Equal(clamped, fx.Camera.FollowTarget);
        }

        [Fact]
        public void Update_TargetNeverResolved_DoesNotThrowAndDoesNotMoveCamera()
        {
            var fx = Build(SnapshotTarget);
            fx.DestroyTarget(); // 从未成功 Update 过。
            var before = fx.Camera.FollowTarget;

            var caught = Record.Exception(() => fx.Host.Update(0.5));

            Assert.Null(caught);
            Assert.Equal(before, fx.Camera.FollowTarget);
            Assert.Single(((PresentationDiagnosticsRecorder)fx.Host.Diagnostics).Warnings);
        }

        [Fact]
        public void Follow_NewTargetAfterLoss_FollowsNewTargetAndLossFlagIsRearmed()
        {
            var fx = Build(SnapshotTarget);
            fx.Host.Update(0.5);
            fx.DestroyTarget();
            fx.Host.Update(0.5);
            var recorder = (PresentationDiagnosticsRecorder)fx.Host.Diagnostics;
            Assert.Single(recorder.Warnings);

            var other = new LossTestEntity(new Id("unit.follow_2")) { Position = fx.Target.Position + new Vec2(5, 5) };
            fx.World.AddEntity(other);
            fx.World.Tick(SimStep.Continuous(0.016));
            fx.Host.Follow(other.EntityId);
            fx.Host.Update(0.5);

            Assert.Equal(other.Position, fx.Camera.FollowTarget);
            Assert.Single(recorder.Warnings);

            // 新目标再丢失：去重标志已复位，记新的一条。
            fx.World.MarkForDestruction(other.EntityId);
            fx.World.Tick(SimStep.Continuous(0.016));
            fx.Host.Update(0.5);
            Assert.Equal(2, recorder.Warnings.Count);
        }

        // ------------------------------------------------------------------
        // 契约：TryGetPosition 默认接口成员与两个内置实现
        // ------------------------------------------------------------------

        private sealed class LegacyFollowTarget : ICameraFollowTarget
        {
            // 只实现旧成员 GetPosition：验证新增的默认接口成员不破坏既有实现的编译与行为。
            public Vec2 Position;

            public bool Throw;

            public Vec2 GetPosition(Id entityId, double alpha) =>
                Throw ? throw new InvalidOperationException("旧实现按旧惯例抛") : Position;
        }

        [Fact]
        public void TryGetPosition_DefaultInterfaceMember_WrapsGetPosition()
        {
            ICameraFollowTarget legacy = new LegacyFollowTarget { Position = new Vec2(8, 9) };

            var ok = legacy.TryGetPosition(new Id("unit.any"), 0.25, out var position);

            Assert.True(ok);
            Assert.Equal(new Vec2(8, 9), position);
        }

        [Fact]
        public void TryGetPosition_DefaultInterfaceMember_PropagatesGetPositionExceptionAsBefore()
        {
            ICameraFollowTarget legacy = new LegacyFollowTarget { Throw = true };

            Assert.Throws<InvalidOperationException>(() => legacy.TryGetPosition(new Id("unit.any"), 0.25, out _));
        }

        [Fact]
        public void TryGetPosition_BuiltInTargets_ReturnFalseWithoutThrowingForMissingEntity()
        {
            var bus = CreateBus();
            var world = new WorldSim(bus);
            var snapshot = new WorldSimSnapshot(world);
            var present = new LossTestEntity(new Id("unit.present")) { Position = new Vec2(1, 2) };
            world.AddEntity(present);
            world.Tick(SimStep.Continuous(0.016));
            var missing = new Id("unit.missing");

            ICameraFollowTarget[] targets =
            {
                new SimSnapshotFollowTarget(snapshot),
                new DelegateFollowTarget((id, alpha) => snapshot.GetPosition(id)),
            };

            foreach (var target in targets)
            {
                Assert.True(target.TryGetPosition(present.EntityId, 0.5, out var found));
                Assert.Equal(present.Position, found);

                var ok = true;
                var position = new Vec2(-1, -1);
                var caught = Record.Exception(() => ok = target.TryGetPosition(missing, 0.5, out position));
                Assert.Null(caught);
                Assert.False(ok);
                Assert.Equal(default(Vec2), position);
            }
        }
    }
}
