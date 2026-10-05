#nullable enable
// LabPlaygroundSpawnPlayModeTests：试玩宿主连出单只靶子不再共点（所有试玩场景与演示场景）。
// 缺陷（复现）：SpawnDummy 把每只单个靶子都放在"玩家正前方 2.2"，连出两只（精英、木桩）圆心重合。
// 规则：正前方被占就沿玩家前方的弧取第一个空位（LabPlayground.FindSingleSpawnPoint，确定性）；间隔 = 两倍体半径。
// 期望值都由规则算出（出场点取会话记录的出场清单、间隔取 LabPlayground.SpawnSeparation），不写裸数。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.LabHost;
using Core.Foundation.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.LabHost
{
    [Category("module:lab")]
    public sealed class LabPlaygroundSpawnPlayModeTests
    {
        private const double Frame = 1.0 / 60.0;
        private static readonly string[] Cells = { "2d_action", "2_5d_action", "3d_action" };
        private static readonly string[] SingleKinds = { "elite", "stake", "frail", "elite", "stake", "frail", "elite", "stake", "frail", "elite" };

        private GameObject? _go;
        private LabPlayground? _pg;
        private Adapters.Stub.StubInput? _input;
        private string _saveDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "lab_spawn_test_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            Dispose();
            if (Directory.Exists(_saveDir))
            {
                Directory.Delete(_saveDir, true);
            }
        }

        private void Dispose()
        {
            _pg?.End();
            if (_go != null)
            {
                UnityEngine.Object.DestroyImmediate(_go);
            }

            _go = null;
            _pg = null;
        }

        private LabPlayground NewPlayground(string cell, bool showcase)
        {
            _go = new GameObject("SpawnTest");
            var pg = _go.AddComponent<LabPlayground>();
            pg.Configure(cell);
            pg.Showcase = showcase;
            pg.ManualDrive = true;
            pg.FlashIntensitySource = () => 1.0;
            _input = new Adapters.Stub.StubInput();
            pg.InputSource = _input;
            pg.PadReader = _ => false;
            pg.SaveDirectory = _saveDir;
            Assert.IsTrue(pg.Begin(), pg.Model.Status);
            _pg = pg;
            return pg;
        }

        private IEnumerator Frames(int n)
        {
            for (var i = 0; i < n; i++)
            {
                _pg!.Tick(Frame);
                _pg.FinishFrame();
                if (i % 4 == 3)
                {
                    yield return null;
                }
            }
        }

        private static double Dist(Vec2 a, Vec2 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>会话记录的出场清单（标签 -> 出场点），按出场顺序。</summary>
        private static List<KeyValuePair<string, Vec2>> Spawned(LabPlayground pg) => pg.Session!.Recording.Dummies.ToList();

        private static (Vec2 Player, double Facing) PlayerPose(LabPlayground pg)
        {
            var ticks = pg.Session!.Recording.Ticks;
            return (ticks[ticks.Count - 1].Position, ticks[ticks.Count - 1].Facing);
        }

        public static IEnumerable<object[]> Scenes()
        {
            foreach (var cell in Cells)
            {
                yield return new object[] { cell, false };
            }

            yield return new object[] { "2d_action", true };
        }

        [UnityTest]
        public IEnumerator ReproElitePlusStake_TwoConsecutiveSingleSpawns_DoNotShareAPoint_InEveryScene()
        {
            foreach (var scene in Scenes())
            {
                var cell = (string)scene[0];
                var showcase = (bool)scene[1];
                var pg = NewPlayground(cell, showcase);
                yield return Frames(4);
                var pose = PlayerPose(pg);
                pg.SpawnDummy("elite");
                pg.SpawnDummy("stake");   // 同一帧连出两只：第二只出场事件还没被会话处理，规则按出场点占位
                yield return Frames(6);
                var spawned = Spawned(pg);
                var label = cell + (showcase ? " showcase" : string.Empty);
                Assert.AreEqual(2, spawned.Count, label);
                var forward = new Vec2(pose.Player.X + Math.Cos(pose.Facing) * LabPlayground.SpawnDistance, pose.Player.Y + Math.Sin(pose.Facing) * LabPlayground.SpawnDistance);
                Assert.AreEqual(0.0, Dist(spawned[0].Value, forward), 1e-9, label + "：第一只仍出在玩家正前方（既有行为不变）");
                Assert.GreaterOrEqual(Dist(spawned[0].Value, spawned[1].Value), LabPlayground.SpawnSeparation, label + "：精英与木桩的出场点相距不小于体半径规则的间隔");

                // 逻辑里的两个实体也确实不在同一点（会话已建出实体，取实体当前位置）。
                var ctx = pg.Session!.Context!;
                var positions = ctx.Dummies.Select(d => ctx.World.World.GetEntity(d.Value)!.Position).ToList();
                Assert.AreEqual(2, positions.Count, label);
                Assert.Greater(Dist(positions[0], positions[1]), 0.0, label + "：两个实体不共点");
                Dispose();
            }
        }

        [UnityTest]
        public IEnumerator Invariant_ConsecutiveSingleSpawns_NeverOverlap_AndAreDeterministicAcrossRuns_InEveryScene()
        {
            foreach (var scene in Scenes())
            {
                var cell = (string)scene[0];
                var showcase = (bool)scene[1];
                var label = cell + (showcase ? " showcase" : string.Empty);
                var runs = new List<List<KeyValuePair<string, Vec2>>>();
                for (var run = 0; run < 2; run++)
                {
                    var pg = NewPlayground(cell, showcase);
                    yield return Frames(4);
                    foreach (var kind in SingleKinds)
                    {
                        pg.SpawnDummy(kind);
                    }

                    yield return Frames(8);
                    runs.Add(Spawned(pg));
                    Dispose();
                }

                Assert.AreEqual(SingleKinds.Length, runs[0].Count, label);
                Assert.AreEqual(runs[0].Count, runs[1].Count, label);
                for (var i = 0; i < runs[0].Count; i++)
                {
                    Assert.AreEqual(runs[0][i].Value.X, runs[1][i].Value.X, 0.0, label + "：两次运行第 " + i + " 只的出场点逐位一致");
                    Assert.AreEqual(runs[0][i].Value.Y, runs[1][i].Value.Y, 0.0, label + "：两次运行第 " + i + " 只的出场点逐位一致");
                    for (var j = 0; j < i; j++)
                    {
                        Assert.GreaterOrEqual(Dist(runs[0][i].Value, runs[0][j].Value), LabPlayground.SpawnSeparation, label + "：第 " + i + " 只与第 " + j + " 只重叠");
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator ClearingTheField_FreesTheForwardPoint_AgainForTheNextSingleSpawn()
        {
            var pg = NewPlayground("2d_action", false);
            yield return Frames(4);
            var pose = PlayerPose(pg);
            pg.SpawnDummy("elite");
            yield return Frames(4);
            pg.ClearDummies();
            yield return Frames(4);
            pg.SpawnDummy("stake");
            yield return Frames(4);
            var spawned = Spawned(pg);
            var forward = new Vec2(pose.Player.X + Math.Cos(pose.Facing) * LabPlayground.SpawnDistance, pose.Player.Y + Math.Sin(pose.Facing) * LabPlayground.SpawnDistance);
            var last = spawned[spawned.Count - 1].Value;
            Assert.AreEqual(0.0, Dist(last, forward), 1e-9, "清场之后正前方又是空位");
        }
    }
}
