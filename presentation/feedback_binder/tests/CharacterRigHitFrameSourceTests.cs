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
        /// <see cref="HitFrameReached"/> 事件，其余成员均为满足接口的最小占位，不被本测试用到。</summary>
        private sealed class FakeRig : ICharacterRig
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
