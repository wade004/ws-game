// wall_slide 的滑动终点被可走性判定拒绝（NF1，取代 unit README 原"已知局限"里斜墙边界线相切的一半）的运行时冒烟：
// 复现（量从 X 变到 Y，期望值由撞击点与到达容差算出）加不变量（缺省导航下滑动行为不变、任何 tick 都不穿墙）。
using System;
using System.Collections.Generic;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Feel;
using Xunit;

namespace Tests.Carriers.Unit
{
    public partial class MotionArbiterTests
    {
        /// <summary>斜墙之外再声明"沿墙切向坐标 x−y 超过 <c>Limit</c> 的点不可走"：滑动终点落在这个区域里，撞击点（回退点）不在。</summary>
        private sealed class SlideDestinationRejectingNavigation : INavigation2D
        {
            private readonly INavigation2D _inner;
            public double Limit;

            public SlideDestinationRejectingNavigation(INavigation2D inner, double limit)
            {
                _inner = inner;
                Limit = limit;
            }

            public void BuildNavMesh(Id mapId) => _inner.BuildNavMesh(mapId);

            public bool IsWalkable(Id mapId, Vec2 point) => _inner.IsWalkable(mapId, point) && point.X - point.Y <= Limit;

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to) => _inner.FindPath(mapId, from, to);

            public Vec2? Raycast(Id mapId, Vec2 from, Vec2 to) => _inner.Raycast(mapId, from, to);

            public NavRayHit? RaycastWithNormal(Id mapId, Vec2 from, Vec2 to) => _inner.RaycastWithNormal(mapId, from, to);

            public void SetBlocking(Id mapId, IReadOnlyList<Rect> rects) => _inner.SetBlocking(mapId, rects);

            public void Clear(Id mapId) => _inner.Clear(mapId);
        }

        [Fact]
        public void WallSlide_WhenTheSlideDestinationIsRejected_StopsAtTheContactPointInsteadOfStallingTheWholeTick()
        {
            var options = new MovementOptions();
            var contactPoint = WallSum - options.ArrivalEpsilon; // 沿 +x 走向斜墙：撞击点回退一个到达容差，y 仍为 0。
            var nav = new SlideDestinationRejectingNavigation(DiagonalWall(), contactPoint + 0.1);
            var fx = Build(nav: nav, options: options);
            fx.Set(FeelFieldNames.WallSlide, true);

            Vec2? before = null;
            Vec2? after = null;
            for (var i = 0; i < 40 && !after.HasValue; i++)
            {
                var prev = fx.Pos;
                fx.Move(1, 0);
                fx.Tick();
                if (fx.Pos.X > prev.X && fx.Pos.X > WallSum - Speed * Dt) // 撞墙那一 tick：不是自由位移
                {
                    var free = Near2(fx.Pos - prev, new Vec2(Speed * Dt, 0));
                    if (!free) { before = prev; after = fx.Pos; }
                }
            }

            // 修复前：滑动终点被拒绝 → 整 tick 不位移（位置停在 before，after 永远取不到）；现在停在撞击点。
            Assert.True(after.HasValue, "撞墙那一 tick 应当有位移（停在撞击点），而不是整 tick 不位移");
            Near(contactPoint, after!.Value.X, 1e-9);
            Near(0.0, after.Value.Y, 1e-9);
            Assert.True(after.Value.X > before!.Value.X, "量应当从撞墙前的位置推进到撞击点");
            Near(0.0, fx.Mo.Velocity.Length, 1e-12); // 撞停：速度归零，下一 tick 不再顶墙
            Assert.True(after.Value.X + after.Value.Y < WallSum, "不穿墙");

            // 继续顶着走：每个 tick 都不穿墙、不抖动（位置稳定在撞击点）。
            for (var i = 0; i < 5; i++)
            {
                fx.Move(1, 0);
                fx.Tick();
                Assert.True(fx.Pos.X + fx.Pos.Y < WallSum);
                Near(contactPoint, fx.Pos.X, 1e-9);
            }
        }

        [Fact]
        public void WallSlide_WhenTheSlideDestinationIsWalkable_StillSlides_AndNeverCrossesTheWall()
        {
            // 不变量：滑动终点可走时行为与没有回退逻辑时一致（沿切向滑动，y 随之变为负）。
            var fx = Build(nav: new SlideDestinationRejectingNavigation(DiagonalWall(), limit: 100.0));
            fx.Set(FeelFieldNames.WallSlide, true);
            var slid = false;
            for (var i = 0; i < 40; i++)
            {
                fx.Move(1, 0);
                fx.Tick();
                Assert.True(fx.Pos.X + fx.Pos.Y < WallSum, $"tick {i} 穿墙：{fx.Pos}");
                if (fx.Pos.Y < -0.1) slid = true;
            }

            Assert.True(slid, "可走的滑动终点应当沿切向滑开");
        }
    }
}
