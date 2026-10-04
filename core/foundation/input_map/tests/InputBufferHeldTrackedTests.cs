using Core.Foundation.Common;
using Core.Foundation.InputMap;
using Xunit;
using static Tests.Foundation.InputMap.BufferRig;

namespace Tests.Foundation.InputMap
{
    /// <summary>
    /// 只追踪按住状态的动作（按钮型 move 类，ADR-0153，按住冲刺用）：复现——此前 move 类按钮被整个忽略，<c>IsHeld</c> 恒为 false；
    /// 不变量——它不入缓冲槽（没有记录/过期/丢弃事件），按住状态即时生效且不受缓冲清空影响，缓冲类动作与未声明动作的既有语义不变。
    /// </summary>
    public class InputBufferHeldTrackedTests
    {
        private static readonly Id Sprint = new Id("input.action.sprint");
        private static readonly Id Attack = new Id("input.action.attack");

        private static BufferRig NewRig() => new BufferRig(
            new[] { Def("input.action.sprint", ActionClass.Move), Def("input.action.attack", ActionClass.Attack) },
            "feel.preset.arpg_responsive");

        [Fact]
        public void MoveClassButton_IsHeldFollowsPressAndRelease_Immediately()
        {
            var rig = NewRig();
            Assert.True(rig.Buffer.IsHeldTracked(Sprint));
            Assert.False(rig.Buffer.IsBuffered(Sprint));
            Assert.False(rig.Buffer.IsHeld(Actor, Sprint));

            // 采样即生效：不必等 BeginTick（与移动轴采样同一口径，没有晚一 tick 的滞后）。
            rig.Buffer.Press(Actor, Sprint);
            Assert.True(rig.Buffer.IsHeld(Actor, Sprint));
            rig.Step();
            for (var i = 0; i < 5; i++) rig.Step();
            Assert.True(rig.Buffer.IsHeld(Actor, Sprint));

            rig.Buffer.Release(Actor, Sprint);
            Assert.False(rig.Buffer.IsHeld(Actor, Sprint));
        }

        [Fact]
        public void MoveClassButton_NeverEntersTheBuffer_AndSurvivesBufferClear()
        {
            var rig = NewRig();

            rig.Step(() => rig.Buffer.Press(Actor, Sprint));
            for (var i = 0; i < 10; i++) rig.Step();

            // 不变量：没有缓冲记录、没有丢弃事件；动作类的按住/缓冲照旧工作。
            Assert.False(rig.Buffer.TryPeek(Actor, out _));
            Assert.Empty(rig.Drops);

            rig.Step(() => rig.Buffer.Press(Actor, Attack));
            Assert.True(rig.Buffer.IsHeld(Actor, Attack));
            Assert.True(rig.Buffer.TryPeek(Actor, out var intent));
            Assert.Equal(Attack, intent.ActionId);

            // 缓冲清空只清缓冲内容与缓冲类按住；按键物理状态（sprint）不受影响。
            rig.Buffer.Clear(Actor);
            Assert.False(rig.Buffer.IsHeld(Actor, Attack));
            Assert.True(rig.Buffer.IsHeld(Actor, Sprint));

            // 角色销毁则一并释放。
            rig.Buffer.RemoveActor(Actor);
            Assert.False(rig.Buffer.IsHeld(Actor, Sprint));
        }

        [Fact]
        public void LocalInputEdges_ReachTheHeldSetThroughOnButtonEdge()
        {
            var rig = NewRig();
            rig.Buffer.BindLocalInput(new Core.Foundation.InputMap.InputMapHost(rig.Bus), Actor);

            rig.Buffer.OnButtonEdge(Sprint.Value, true);
            Assert.True(rig.Buffer.IsHeld(Actor, Sprint));
            rig.Buffer.OnButtonEdge(Sprint.Value, false);
            Assert.False(rig.Buffer.IsHeld(Actor, Sprint));
        }

        [Fact]
        public void UndeclaredAction_StaysIgnored_AsBefore()
        {
            var rig = NewRig();
            var other = new Id("input.action.unknown");

            rig.Buffer.Press(Actor, other);

            Assert.False(rig.Buffer.IsHeldTracked(other));
            Assert.False(rig.Buffer.IsHeld(Actor, other));
        }
    }
}
