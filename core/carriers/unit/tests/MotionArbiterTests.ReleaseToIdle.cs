// �����ƶ��ɿ������ Idle�����ѷ����� P2 ȱ�� 8����
// ���֣�һ�η����ƶ������ύ��ͼ��MovementState.Mode ��Զͣ�� Walk/Run��unit.state_changed �� Walk->Idle һ����������
// ����״̬�������Զ�ղ���"վס"��վ�ŵĽ�ɫԭ��̤�������˶��㿪����ر�����·����Ҫ������
// �����������ɿ������ Idle ǡ�÷�һ���¼�����״̬���ɿ�ǰ��ģʽ�����˶����¼��ٻ����ڼ䣨�ٶȷ��㣩����ǰ��� Idle���ٶȹ���ĵ� tick ����أ�
// ����������� tick ����� Idle������·���ĵ�λ����Ӱ�졣
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

            fx.Tick(); // û������ͼ
            Assert.Equal(MoveMode.Idle, fx.Player.MovementState.Mode);
            Assert.Equal(1, IdleEvents(fx));
            var evt = fx.StateChanged.Last(e => e.NewState == MoveMode.Idle.ToString());
            Assert.Equal(MoveMode.Walk.ToString(), evt.OldState);

            for (var i = 0; i < 5; i++) fx.Tick(); // ֮�󱣳� Idle�����ظ����¼�
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
            Assert.True(fx.Mo.Velocity.Length > 0.0, "�оߣ��ɿ�ǰ����");
            Assert.Equal(0, IdleEvents(fx));

            var sliding = 0;
            for (var i = 0; i < 200 && fx.Player.MovementState.Mode != MoveMode.Idle; i++)
            {
                fx.Tick();
                if (fx.Player.MovementState.Mode != MoveMode.Idle)
                {
                    sliding++;
                    Assert.True(fx.Mo.Velocity.Length > 0.0, "���ٻ����ڼ��ٶȷ��㣬��Ӧ��ǰ��� Idle");
                }
            }

            Assert.Equal(MoveMode.Idle, fx.Player.MovementState.Mode);
            Assert.True(sliding > 0, "decel_ms > 0 ʱ�ɿ���Ӧ������ tick �Ļ���");
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
