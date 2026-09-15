using Core.Foundation.Common;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>T-N6-4：<see cref="SimpleMoveModel"/> 的验收测试。</summary>
    public class SimpleMoveModelTests
    {
        [Fact]
        public void Step_WithinEngageRange_DoesNotMove()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 10);
            var playerId = SimTestWorldFactory.PlayerId;
            var before = world.Gameplay.Carriers.Units.GetPosition(playerId);

            var moved = SimpleMoveModel.Step(
                world.Gameplay.Carriers.Units, world.Spatial, playerId,
                targetPosition: new Vec2(2, 0), engageRange: 5, dtSeconds: 0.5, moveSpeed: 4);

            Assert.False(moved);
            Assert.Equal(before, world.Gameplay.Carriers.Units.GetPosition(playerId));
        }

        [Fact]
        public void Step_OutsideEngageRange_MovesTowardTarget_WithoutOvershooting()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 11);
            var playerId = SimTestWorldFactory.PlayerId; // starts at (0,0)

            var moved = SimpleMoveModel.Step(
                world.Gameplay.Carriers.Units, world.Spatial, playerId,
                targetPosition: new Vec2(10, 0), engageRange: 2, dtSeconds: 0.5, moveSpeed: 4);

            Assert.True(moved);
            var pos = world.Gameplay.Carriers.Units.GetPosition(playerId);
            Assert.Equal(2.0, pos.X, precision: 9);
            Assert.Equal(0.0, pos.Y, precision: 9);
        }

        [Fact]
        public void Step_StepLargerThanRemainingDistance_StopsExactlyAtTarget()
        {
            var world = SimTestWorldFactory.BuildFromEmbeddedDataset(seed: 12);
            var playerId = SimTestWorldFactory.PlayerId; // starts at (0,0)

            var moved = SimpleMoveModel.Step(
                world.Gameplay.Carriers.Units, world.Spatial, playerId,
                targetPosition: new Vec2(1, 0), engageRange: 0, dtSeconds: 10, moveSpeed: 4);

            Assert.True(moved);
            var pos = world.Gameplay.Carriers.Units.GetPosition(playerId);
            Assert.Equal(1.0, pos.X, precision: 9);
            Assert.Equal(0.0, pos.Y, precision: 9);
        }
    }
}
