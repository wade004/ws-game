#nullable enable
// LabPlaygroundSpawnPointTests：试玩宿主单只靶子出场点规则（FindSingleSpawnPoint）的不变量。
// 缺陷（复现）：此前每只单个靶子都出在"玩家正前方 2.2"，连出两只就共点。规则：正前方被占就沿弧取第一个空位，确定性。
// 期望值都由规则算出（间隔 = 两倍体半径、候选点的相对角 = 固定序 0、+1、-1、+2、-2 个步长），不写裸数。
using Adapter.Unity;
using System;
using System.Collections.Generic;
using FeelLab.Unity;
using Core.Foundation.Common;
using NUnit.Framework;

namespace FeelLab.Unity.Tests.Editor
{
    [Category("module:lab")]
    public sealed class LabPlaygroundSpawnPointTests
    {
        private static double Dist(Vec2 a, Vec2 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static Vec2 Forward(Vec2 player, double facing) =>
            new Vec2(player.X + Math.Cos(facing) * LabPlayground.SpawnDistance, player.Y + Math.Sin(facing) * LabPlayground.SpawnDistance);

        private static readonly KeyValuePair<Vec2, double>[] Poses =
        {
            new KeyValuePair<Vec2, double>(new Vec2(0.0, 0.0), 0.0),
            new KeyValuePair<Vec2, double>(new Vec2(3.5, -2.0), Math.PI / 2.0),
            new KeyValuePair<Vec2, double>(new Vec2(-7.25, 4.5), 2.4),
            new KeyValuePair<Vec2, double>(new Vec2(1.0, 1.0), -Math.PI * 0.8),
        };

        [Test]
        public void SeparationRule_IsTwiceTheBodyRadiusTheLogicRegistersDummiesWith()
        {
            Assert.AreEqual(2.0 * Lab.LabHost.DummyBodyRadius, LabPlayground.SpawnSeparation, 1e-12);
            // 弧步长在默认距离处的弦长不小于间隔，否则相邻弧位本身就重叠。
            Assert.GreaterOrEqual(2.0 * LabPlayground.SpawnDistance * Math.Sin(LabPlayground.SpawnArcStepRadians / 2.0), LabPlayground.SpawnSeparation);
        }

        [Test]
        public void EmptyField_SpawnsExactlyInFrontOfThePlayer()
        {
            foreach (var pose in Poses)
            {
                var p = LabPlayground.FindSingleSpawnPoint(pose.Key, pose.Value, new List<Vec2>(), out var crowded);
                Assert.IsFalse(crowded);
                Assert.AreEqual(0.0, Dist(p, Forward(pose.Key, pose.Value)), 1e-12, "没有别的靶子时仍出在正前方（既有行为不变）");
            }
        }

        [Test]
        public void ReproSecondSingleSpawn_WhenForwardIsTaken_DiffersByAtLeastTheSeparation()
        {
            foreach (var pose in Poses)
            {
                var first = LabPlayground.FindSingleSpawnPoint(pose.Key, pose.Value, new List<Vec2>(), out _);
                var second = LabPlayground.FindSingleSpawnPoint(pose.Key, pose.Value, new List<Vec2> { first }, out var crowded);
                Assert.IsFalse(crowded);
                Assert.GreaterOrEqual(Dist(first, second), LabPlayground.SpawnSeparation, "第二只不能与第一只共点或重叠");
                // 第二个候选位是弧上第一个空位：同一半径、相对正前方 +1 个步长。
                Assert.AreEqual(LabPlayground.SpawnDistance, Dist(pose.Key, second), 1e-9);
                var angle = Math.Atan2(second.Y - pose.Key.Y, second.X - pose.Key.X) - pose.Value;
                angle = Math.Atan2(Math.Sin(angle), Math.Cos(angle));
                Assert.AreEqual(LabPlayground.SpawnArcStepRadians, angle, 1e-9, "弧上的第一个空位是 +1 个步长");
            }
        }

        [Test]
        public void SlotOrder_IsFixed_PlusOne_MinusOne_PlusTwo_MinusTwo_ThenNextRing()
        {
            var player = new Vec2(0.0, 0.0);
            var occupied = new List<Vec2>();
            var expectedK = new[] { 0, 1, -1, 2, -2 };
            foreach (var k in expectedK)
            {
                var p = LabPlayground.FindSingleSpawnPoint(player, 0.0, occupied, out _);
                var angle = Math.Atan2(p.Y, p.X);
                Assert.AreEqual(k * LabPlayground.SpawnArcStepRadians, angle, 1e-9, "第 " + occupied.Count + " 只在弧上的相对角序号 " + k);
                Assert.AreEqual(LabPlayground.SpawnDistance, Dist(player, p), 1e-9);
                occupied.Add(p);
            }

            var sixth = LabPlayground.FindSingleSpawnPoint(player, 0.0, occupied, out var crowded);
            Assert.IsFalse(crowded);
            Assert.AreEqual(LabPlayground.SpawnDistance + LabPlayground.SpawnSeparation, Dist(player, sixth), 1e-9, "这一圈五个位置占满后换下一圈（半径加一个间隔）");
            Assert.AreEqual(0.0, Math.Atan2(sixth.Y, sixth.X), 1e-9, "下一圈同样从正前方开始");
        }

        [Test]
        public void Invariant_ManyConsecutiveSingleSpawns_NeverOverlap_AndAreDeterministic()
        {
            foreach (var pose in Poses)
            {
                var run1 = Place(pose.Key, pose.Value, 60);
                var run2 = Place(pose.Key, pose.Value, 60);
                Assert.AreEqual(run1.Count, run2.Count);
                for (var i = 0; i < run1.Count; i++)
                {
                    Assert.AreEqual(run1[i].X, run2[i].X, 0.0, "两次运行逐位一致");
                    Assert.AreEqual(run1[i].Y, run2[i].Y, 0.0, "两次运行逐位一致");
                    for (var j = 0; j < i; j++)
                    {
                        Assert.GreaterOrEqual(Dist(run1[i], run1[j]), LabPlayground.SpawnSeparation - 1e-9, "第 " + i + " 只与第 " + j + " 只重叠");
                    }
                }
            }
        }

        private static List<Vec2> Place(Vec2 player, double facing, int n)
        {
            var placed = new List<Vec2>();
            for (var i = 0; i < n; i++)
            {
                var p = LabPlayground.FindSingleSpawnPoint(player, facing, placed, out var crowded);
                Assert.IsFalse(crowded, "场上靶子上限 60 只以内不应走到占满");
                placed.Add(p);
            }

            return placed;
        }

        [Test]
        public void DeadOrMovedDummies_FreeTheirSlot_OccupancyIsByCurrentPositionList()
        {
            var player = new Vec2(0.0, 0.0);
            var forward = Forward(player, 0.0);
            // 占位清单只含"在场的"当前位置：正前方的靶子被击退到别处后，正前方又是空位。
            var moved = new Vec2(forward.X + 5.0, forward.Y);
            var p = LabPlayground.FindSingleSpawnPoint(player, 0.0, new List<Vec2> { moved }, out _);
            Assert.AreEqual(0.0, Dist(p, forward), 1e-12);
        }

        [Test]
        public void FullField_ReportsCrowded_AndFallsBackToForward()
        {
            var player = new Vec2(0.0, 0.0);
            var placed = new List<Vec2>();
            var crowded = false;
            var guard = 0;
            while (!crowded && guard++ < 500)
            {
                var p = LabPlayground.FindSingleSpawnPoint(player, 0.0, placed, out crowded);
                if (crowded)
                {
                    Assert.AreEqual(0.0, Dist(p, Forward(player, 0.0)), 1e-12, "占满时回到正前方");
                }
                else
                {
                    placed.Add(p);
                }
            }

            Assert.IsTrue(crowded, "候选位用完后应报告占满");
            Assert.Greater(placed.Count, 60, "候选位数量远大于场上靶子上限，正常使用走不到占满");
        }

        [Test]
        public void IsFreeSpawnPoint_BoundaryIsTheSeparationItself()
        {
            var o = new List<Vec2> { new Vec2(0.0, 0.0) };
            Assert.IsTrue(LabPlayground.IsFreeSpawnPoint(new Vec2(LabPlayground.SpawnSeparation, 0.0), o), "恰为间隔不算重叠");
            Assert.IsFalse(LabPlayground.IsFreeSpawnPoint(new Vec2(LabPlayground.SpawnSeparation - 0.01, 0.0), o));
            Assert.IsFalse(LabPlayground.IsFreeSpawnPoint(new Vec2(0.0, 0.0), o), "共点必然重叠");
        }
    }
}
