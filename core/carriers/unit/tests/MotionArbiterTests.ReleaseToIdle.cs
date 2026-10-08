// 方向移动松开后落回 Idle（消费方反馈 P2 缺口 8）。
// 缺陷：一次方向移动命令提交后，MovementState.Mode 永远停在 Walk/Run，unit.state_changed 的 Walk->Idle 一直不发，
// 动画状态机（只认这条事件）因此永远收不到"站住"，站着的角色原地踏步；运动层开着与关闭两条路径都要收口。
// 不变量：松开后 Idle 恰好发一次事件；状态在松开前保持原模式；运动层减速滑行期间（速度非零）不提前落到 Idle，速度归零的那个 tick 才落；
// 没有运动层时松开后的下一个 tick 落到 Idle；有路径的单位不受影响。
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Xunit;

namespace Tests.Carriers.Unit
{
    public partial class MotionArbiterTests
    {
        private static int IdleEvents(Fx fx) =>
            fx.StateChanged.Count(e => e.UnitId.Equals(HeroId) && e.NewState == MoveMode.Idle.ToString());

        [Fact]
        public void ReleaseDirection_WithoutMotionLayer_SettlesToIdleOnce()
        {
            var fx = Build(motion: false);
            for (var i = 0; i < 5; i++)
            {
                fx.Move(1, 0, "Walk");
                fx.Tick();
            }

            Assert.Equal(MoveMode.Walk, fx.Player.MovementState.Mode);
            Assert.Equal(0, IdleEvents(fx));

            fx.Tick(); // 没有按方向的一个 tick
            Assert.Equal(MoveMode.Idle, fx.Player.MovementState.Mode);
            Assert.Equal(1, IdleEvents(fx));
            var evt = fx.StateChanged.Last(e => e.NewState == MoveMode.Idle.ToString());
            Assert.Equal(MoveMode.Walk.ToString(), evt.OldState);

            for (var i = 0; i < 5; i++) fx.Tick(); // 之后保持 Idle，不重复发事件
            Assert.Equal(1, IdleEvents(fx));
        }

        [Fact]
        public void ReleaseDirection_WithMotionLayer_SettlesToIdleWhenVelocityReachesZero_NotBefore()
        {
            var fx = Build(motion: true);
            fx.Set(FeelFieldNames.DecelMs, 200.0);
            for (var i = 0; i < 20; i++)
            {
                fx.Move(1, 0, "Run");
                fx.Tick();
            }

            Assert.Equal(MoveMode.Run, fx.Player.MovementState.Mode);
            Assert.True(fx.Mo.Velocity.Length > 0.0, "判据：松开前在移动");
            Assert.Equal(0, IdleEvents(fx));

            var sliding = 0;
            for (var i = 0; i < 200 && fx.Player.MovementState.Mode != MoveMode.Idle; i++)
            {
                fx.Tick();
                if (fx.Player.MovementState.Mode != MoveMode.Idle)
                {
                    sliding++;
                    Assert.True(fx.Mo.Velocity.Length > 0.0, "减速滑行期间速度非零，不应提前落到 Idle");
                }
            }

            Assert.Equal(MoveMode.Idle, fx.Player.MovementState.Mode);
            Assert.True(sliding > 0, "decel_ms > 0 时松开应有至少一个 tick 的滑行");
            Assert.Equal(0.0, fx.Mo.Velocity.Length, 9);
            Assert.Equal(1, IdleEvents(fx));
            var evt = fx.StateChanged.Last(e => e.NewState == MoveMode.Idle.ToString());
            Assert.Equal(MoveMode.Run.ToString(), evt.OldState);
        }

        [Fact]
        public void ReleaseDirection_WithMotionLayer_InstantStop_SettlesNextTick()
        {
            var fx = Build(motion: true);
            fx.Set(FeelFieldNames.DecelMs, 0.0);
            for (var i = 0; i < 10; i++)
            {
                fx.Move(0, 1, "Walk");
                fx.Tick();
            }

            fx.Tick();
            Assert.Equal(MoveMode.Idle, fx.Player.MovementState.Mode);
            Assert.Equal(1, IdleEvents(fx));
        }

        [Fact]
        public void HoldingDirection_NeverSettlesToIdle()
        {
            var fx = Build(motion: true);
            for (var i = 0; i < 60; i++)
            {
                fx.Move(1, 0, "Run");
                fx.Tick();
                Assert.NotEqual(MoveMode.Idle, fx.Player.MovementState.Mode);
            }

            Assert.Equal(0, IdleEvents(fx));
        }

        [Fact]
        public void PathFollowingUnit_IsNotSettledByRelease()
        {
            var fx = Build(motion: true);
            fx.MoveTo(50, 0);
            fx.Tick();
            Assert.NotNull(fx.Player.MovementState.CurrentPath);
            fx.Tick();
            Assert.Equal(MoveMode.Run, fx.Player.MovementState.Mode);
            Assert.Equal(0, IdleEvents(fx));
        }
    }
}
