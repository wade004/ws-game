using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Xunit;

namespace Tests.Foundation.InputMap
{
    /// <summary>输入缓冲只读契约（S0 只定义契约）：记录快照、查询接口可被假实现、缓冲丢弃事件与登记一致。</summary>
    public class InputBufferContractsTests
    {
        private static readonly Id Actor = new Id("unit.actor");

        [Fact]
        public void BufferedIntent_CarriesEveryField()
        {
            var intent = new BufferedIntent(new Id("input.attack"), ActionClass.Attack, 100, 107, 5,
                new Vec2(1, 0), BufferHoldState.HoldReleased, 12, consumed: true);

            Assert.Equal(new Id("input.attack"), intent.ActionId);
            Assert.Equal(ActionClass.Attack, intent.Class);
            Assert.Equal(100, intent.SubmittedTick);
            Assert.Equal(107, intent.ExpiresAtActionTime);
            Assert.Equal(5, intent.Priority);
            Assert.Equal(new Vec2(1, 0), intent.DirectionSnapshot);
            Assert.Equal(BufferHoldState.HoldReleased, intent.HoldState);
            Assert.Equal(12, intent.HeldTicks);
            Assert.True(intent.Consumed);
        }

        [Fact]
        public void BufferedIntent_DirectionSnapshotIsNullWhenThereWasNoAxisInput()
        {
            var intent = new BufferedIntent(new Id("input.dodge"), ActionClass.Dodge, 1, 5, 0, null, BufferHoldState.Tap, 0, false);
            Assert.Null(intent.DirectionSnapshot);
            Assert.False(intent.Consumed);
        }

        [Fact]
        public void ActionClass_ValuesAreAppendedOnlyAndStable()
        {
            Assert.Equal(new[] { "Move", "Attack", "Skill", "Dodge", "Interact", "Item", "Menu" },
                System.Enum.GetValues(typeof(ActionClass)).Cast<ActionClass>().Select(c => c.ToString()));
            Assert.Equal(0, (int)ActionClass.Move);
            Assert.Equal(new[] { "Replaced", "Full", "Expired", "Cleared", "Rejected" },
                System.Enum.GetValues(typeof(BufferDropReason)).Cast<BufferDropReason>().Select(c => c.ToString()));
        }

        [Fact]
        public void InputBufferQuery_CanBeFaked_AndReturnsPerActorSnapshots()
        {
            IInputBufferQuery query = new FakeBuffer();
            Assert.Single(query.Snapshot(Actor));
            Assert.Empty(query.Snapshot(new Id("unit.other")));
        }

        [Fact]
        public void BufferDroppedEvent_UsesTheRegisteredKey_AndRejectedCarriesAReasonCode()
        {
            var rejected = new InputBufferDroppedEvent(Actor, new Id("input.skill"), BufferDropReason.Rejected, "NOT_ENOUGH_RESOURCE");
            Assert.Equal(InputMapEventKeys.BufferDropped, rejected.Key);
            Assert.Equal(EventKeys.InputBufferDropped, rejected.Key);
            Assert.Equal("NOT_ENOUGH_RESOURCE", rejected.ReasonCode);

            var expired = new InputBufferDroppedEvent(Actor, new Id("input.skill"), BufferDropReason.Expired);
            Assert.Null(expired.ReasonCode);
            Assert.Equal(BufferDropReason.Expired, expired.Reason);
        }

        private sealed class FakeBuffer : IInputBufferQuery
        {
            public IReadOnlyList<BufferedIntent> Snapshot(Id actorId) =>
                actorId == Actor
                    ? new[] { new BufferedIntent(new Id("input.attack"), ActionClass.Attack, 1, 8, 0, null, BufferHoldState.Tap, 0, false) }
                    : new BufferedIntent[0];
        }
    }
}
