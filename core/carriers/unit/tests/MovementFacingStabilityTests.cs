// MovementFacingStabilityTests：ADR-0099（消费方反馈第四十四批）验收——沿路径移动时，同一路点段内
// 逐 tick 推进不应该改用"当前插值位置"现算 Atan2（旧实现 ContinuePathCore/AdvanceChase 均如此），
// 非水平/竖直/45°整数倍角度的直线段上，位置逐 tick 浮点累加的最后一位舍入误差会让 Facing 在同一方向
// 档位内的相邻两个双精度值间来回抖动（消费方 32/32 段闪回逐帧 CSV 实测：闪回起始帧精确等于朝向原值
// 变化帧）。见 core/carriers/unit/core/MovementTickHandler.cs SegmentFacing 判断记录：改为只按路点
// 数组本身（建路那一刻就已经固定、不随插值位置变化）计算每一段的朝向。
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Unit
{
    public class MovementFacingStabilityTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id FactionId = new Id("fac.player");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");
        private static readonly Id HeroId = new Id("unit.hero");

        /// <summary>固定返回预先给定折线（忽略 from/to 参数）的 <see cref="INavigation2D"/> 测试替身
        /// ——本文件只需要一条"由几个固定路点组成的路径"，不需要真实寻路/阻挡判定（惯例同
        /// <c>UnitChaseTests.CountingNavigation2D</c>"只覆盖本文件测试实际用到的行为"）。</summary>
        private sealed class FixedPathNavigation2D : INavigation2D
        {
            private readonly IReadOnlyList<Vec2> _path;

            public FixedPathNavigation2D(IReadOnlyList<Vec2> path) => _path = path;

            public void BuildNavMesh(Id mapId)
            {
            }

            public bool IsWalkable(Id mapId, Vec2 point) => true;

            public IReadOnlyList<Vec2>? FindPath(Id mapId, Vec2 from, Vec2 to) => _path;

            public Vec2? Raycast(Id mapId, Vec2 from, Vec2 to) => null;

            public int GetBlockingVersion(Id mapId) => 0;

            public void SetBlocking(Id mapId, IReadOnlyList<Rect> rects)
            {
            }

            public void Clear(Id mapId)
            {
            }
        }

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public PlayerUnit Player = null!;
        }

        private static Fixture Build(double moveSpeed, INavigation2D? navigation = null)
        {
            // StrictCatalog=false：本文件不断言任何事件，惯例同 MovementTickHandlerTests.Build。
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var world = new WorldSim(bus);
            var player = new PlayerUnit(HeroId, MapId, FactionId, ArchetypeId) { Position = Vec2.Zero };
            world.AddEntity(player);

            var units = new WorldUnitAccess(world);
            var stats = new FakeStatHost();
            stats.SetBase(HeroId, new MovementOptions().MoveSpeedStat, moveSpeed);
            var auras = new FakeAuraQuery();
            var host = new MovementHost(world);
            var handler = new MovementTickHandler(units, stats, auras, host, bus, navigation);

            world.RegisterPhaseHandler(TickPhase.MovementAndNavigation, handler);

            return new Fixture { World = world, Units = units, Player = player };
        }

        private static JsonObject TargetArgs(double x, double y) =>
            new JsonObjectBuilder().Add("x", new JsonNumber(x)).Add("y", new JsonNumber(y)).Build();

        /// <summary>
        /// 单段路径（无导航时的直线兜底，见 <c>BeginPathTo</c>）：单位沿 (0,0) -&gt; (7,3) 移动
        /// 20 tick，moveSpeed/dt 刻意不能整除总距离（每个 tick 都落在"未到达，继续沿同一段插值"的
        /// 中间态，是旧实现逐 tick 现算 Atan2 的真正触发条件），全程只有 1 个路点段，<see cref="Unit.Facing"/>
        /// 理应全程只有 1 个不同取值。
        /// <para>
        /// 判断记录（为什么不是任务书原始建议的 (0,0)-&gt;(10,10)）：45°对角线从原点出发时，
        /// <c>dx</c>/<c>dy</c> 恒相等，<c>Vec2</c> 对 X/Y 两个分量的除法/乘加运算在同一 tick 内输入
        /// 完全相同，IEEE754 浮点运算对相同输入必然给出相同输出，<c>pos.X</c>/<c>pos.Y</c> 逐 tick
        /// 保持逐位相等，<c>Atan2(toWaypoint.Y, toWaypoint.X)</c> 退化成 <c>Atan2(v, v)</c>（<c>v</c>
        /// 相同），旧实现在这一特例下也不会抖动（实测验证：先用旧代码跑过 (10,10) 目标，20 tick 只有
        /// 1 个取值，不构成"先红后绿"）。改用 (7,3) 这一非对称、非 45°的目标复现：旧代码实测 20 tick
        /// 内出现 6 个不同取值（0.40489178628508343/34/35/33/316/327，均只在最后 1～2 位有效数字
        /// 抖动，与消费方"同档位内朝向末位抖动"描述完全吻合），本方法名/断言口径不变，只是坐标换成
        /// 真正能触发该缺陷的取值。
        /// </para>
        /// </summary>
        [Fact]
        public void SingleSegmentPath_TwentyTicks_FacingHasExactlyOneDistinctValue()
        {
            var fixture = Build(moveSpeed: 0.37);
            fixture.World.SubmitIntent(new Intent(HeroId, "move", TargetArgs(7, 3)));

            var facings = new List<double>();
            for (var i = 0; i < 20; i++)
            {
                fixture.World.Tick(SimStep.Continuous(1.0));
                facings.Add(fixture.Player.Facing);
            }

            var distinctValues = facings.Distinct().ToList();
            Assert.True(
                distinctValues.Count == 1,
                "单段路径 20 tick 内 Facing 应当只有 1 个不同取值（ADR-0099），实测 " +
                distinctValues.Count + " 个：" + string.Join(", ", distinctValues.Select(f => f.ToString("R"))));
        }

        /// <summary>
        /// 两段折线路径（0,0)-&gt;(6,2)-&gt;(10,10)，经 <see cref="FixedPathNavigation2D"/> 固定返回）：
        /// 单位跨两个路点段移动，每段各自应当有且只有一个 Facing 取值，全程恰好 2 个不同取值（不是 1
        /// 个——两段方向确实不同；也不是 &gt;2 个——同一段内不应该抖动）。
        /// </summary>
        [Fact]
        public void TwoSegmentPolyline_FacingHasExactlyTwoDistinctValues()
        {
            var polyline = new List<Vec2> { new Vec2(0, 0), new Vec2(6, 2), new Vec2(10, 10) };
            var nav = new FixedPathNavigation2D(polyline);
            var fixture = Build(moveSpeed: 0.9, navigation: nav);
            fixture.World.SubmitIntent(new Intent(HeroId, "move", TargetArgs(10, 10)));

            var facings = new List<double>();
            for (var i = 0; i < 30; i++)
            {
                fixture.World.Tick(SimStep.Continuous(1.0));
                facings.Add(fixture.Player.Facing);
            }

            var distinctValues = facings.Distinct().ToList();
            Assert.True(
                distinctValues.Count == 2,
                "两段折线路径应当恰好 2 个不同 Facing 取值（每段各一个、段内不抖动），实测 " +
                distinctValues.Count + " 个：" + string.Join(", ", distinctValues.Select(f => f.ToString("R"))));
        }
    }
}
