using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.Common;
using Presentation.FeedbackBinder.Core;
using Presentation.Render;
using Xunit;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary><see cref="CharacterRigHitFrameSource"/> 用例（ADR-0017 决策 d）。</summary>
    public class CharacterRigHitFrameSourceTests
    {
        /// <summary>最小 <see cref="ICharacterRig"/> 假实现：只暴露一个可从测试代码直接触发的
        /// <see cref="HitFrameReached"/> 事件，其余成员均为满足接口的最小占位，不被本测试用到。
        /// PJ130-04：命中帧不再是 <see cref="ICharacterRig"/> 强制成员，本假实现额外实现
        /// <see cref="IHitFrameEmitter"/> 以验证"支持命中帧同步的 rig"这一路径；见下方
        /// <see cref="FakeRigWithoutHitFrame"/> 覆盖"不支持"的另一路径。</summary>
        private sealed class FakeRig : ICharacterRig, IHitFrameEmitter
        {
            public event Action<Id>? HitFrameReached;

            public Id EntityId { get; } = new Id("unit.fake");

            public AnimState CurrentAnimState => AnimState.Idle;

            public IProceduralAnim ProceduralAnim => throw new NotSupportedException();

            public void SetAnimState(AnimState state)
            {
            }

            public void PlayClip(Id clipId, bool loop = false, double speed = 1.0)
            {
            }

            public Vec2? ResolveAnchorLocalOffset(Id anchorId, Direction facing) => null;

            public void ComposeAndApplyLayers(IReadOnlyList<string> layerNamesInOrder, Direction facing, Func<SpriteLayerPlacement, Id> resolveResourceId)
            {
            }

            public void ApplyLayers(IReadOnlyList<Id> resourceIds)
            {
            }

            public void Update(double dt)
            {
            }

            public void FireHitFrame() => HitFrameReached?.Invoke(EntityId);
        }

        /// <summary>PJ130-04 复现/回归用例：一个只实现 <see cref="ICharacterRig"/>（不实现
        /// <see cref="IHitFrameEmitter"/>）的 rig——模拟继续实现 1.2.0 及更早版本接口形状的外部实现，
        /// 升级到本仓库当前版本时不应因为 <c>ICharacterRig.HitFrameReached</c> 曾是强制成员而编译失败，
        /// 登记进 <see cref="CharacterRigHitFrameSource"/> 时也不应抛异常。</summary>
        private sealed class FakeRigWithoutHitFrame : ICharacterRig
        {
            public Id EntityId { get; } = new Id("unit.fake_no_hitframe");

            public AnimState CurrentAnimState => AnimState.Idle;

            public IProceduralAnim ProceduralAnim => throw new NotSupportedException();

            public void SetAnimState(AnimState state)
            {
            }

            public void PlayClip(Id clipId, bool loop = false, double speed = 1.0)
            {
            }

            public Vec2? ResolveAnchorLocalOffset(Id anchorId, Direction facing) => null;

            public void ComposeAndApplyLayers(IReadOnlyList<string> layerNamesInOrder, Direction facing, Func<SpriteLayerPlacement, Id> resolveResourceId)
            {
            }

            public void ApplyLayers(IReadOnlyList<Id> resourceIds)
            {
            }

            public void Update(double dt)
            {
            }
        }

        [Fact]
        public void RegisterRig_RigWithoutHitFrameEmitter_DoesNotThrow_AndHasRigStaysTrue()
        {
            var source = new CharacterRigHitFrameSource();
            var rig = new FakeRigWithoutHitFrame();

            var ex = Record.Exception(() => source.RegisterRig(rig.EntityId, rig));

            Assert.Null(ex);
            Assert.True(source.HasRig(rig.EntityId), "一个 rig 确实已经登记——即便它不支持命中帧同步。");
        }

        [Fact]
        public void UnregisterRig_RigWithoutHitFrameEmitter_DoesNotThrow()
        {
            var source = new CharacterRigHitFrameSource();
            var rig = new FakeRigWithoutHitFrame();
            source.RegisterRig(rig.EntityId, rig);

            var ex = Record.Exception(() => source.UnregisterRig(rig.EntityId));

            Assert.Null(ex);
            Assert.False(source.HasRig(rig.EntityId));
        }

        [Fact]
        public void RegisterRig_HasRig_ReturnsTrue()
        {
            var source = new CharacterRigHitFrameSource();
            var rig = new FakeRig();

            source.RegisterRig(rig.EntityId, rig);

            Assert.True(source.HasRig(rig.EntityId));
        }

        [Fact]
        public void UnregisteredEntity_HasRig_ReturnsFalse()
        {
            var source = new CharacterRigHitFrameSource();

            Assert.False(source.HasRig(new Id("unit.never_registered")));
        }

        [Fact]
        public void RigHitFrameReached_ForwardsAsAggregateEvent()
        {
            var source = new CharacterRigHitFrameSource();
            var rig = new FakeRig();
            source.RegisterRig(rig.EntityId, rig);
            Id? raisedFor = null;
            source.HitFrameReached += id => raisedFor = id;

            rig.FireHitFrame();

            Assert.Equal(rig.EntityId, raisedFor);
        }

        [Fact]
        public void UnregisterRig_StopsForwarding()
        {
            var source = new CharacterRigHitFrameSource();
            var rig = new FakeRig();
            source.RegisterRig(rig.EntityId, rig);
            var raised = false;
            source.HitFrameReached += _ => raised = true;

            source.UnregisterRig(rig.EntityId);
            rig.FireHitFrame();

            Assert.False(raised);
            Assert.False(source.HasRig(rig.EntityId));
        }

        [Fact]
        public void UnregisterRig_UnknownEntity_IsNoOp()
        {
            var source = new CharacterRigHitFrameSource();

            var ex = Record.Exception(() => source.UnregisterRig(new Id("unit.unknown")));

            Assert.Null(ex);
        }

        [Fact]
        public void RegisterRig_ReplacingExisting_DetachesOldRigSubscription()
        {
            var source = new CharacterRigHitFrameSource();
            var entityId = new Id("unit.shared");
            var firstRig = new FakeRig();
            var secondRig = new FakeRig();
            source.RegisterRig(entityId, firstRig);

            source.RegisterRig(entityId, secondRig);
            var raised = false;
            source.HitFrameReached += _ => raised = true;
            firstRig.FireHitFrame();

            Assert.False(raised);

            secondRig.FireHitFrame();
            Assert.True(raised);
        }
    }
}
