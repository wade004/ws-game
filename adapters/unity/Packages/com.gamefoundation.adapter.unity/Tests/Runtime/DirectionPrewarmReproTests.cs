#nullable enable
// DirectionPrewarmReproTests：ADR-0112 B 条（按实体预热全部方向）复现用例——生产装配级。
//
// 复现：同一个外形（身体 + 一件装备层，五个方向档位的逐层剪辑与静态层图全部写盘），side_r 已显示；
//   - 没有预热的实体，冷转向 front：转向当帧仍显示 side_r，保持至少一帧才提交（保持帧数 > 0，玩家看到的"转身发硬"）；
//   - 调用 UnityViewFactory.PrewarmDirections 并等预热完成后，同一次转向 0 帧保持（转向当帧就是 front）；
//   - 预热进行期间显示不受影响（每帧全部可见渲染器仍是 side_r，已显示方向不变、没有重合成）。
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class DirectionPrewarmReproTests : DirectionSwitchFixtureBase
    {
        private static readonly string[] Keys = { "idle" };

        private IEnumerator BuildShape(Box<Fx> box, string suffix, string mesh)
        {
            foreach (var dir in AllDirs)
            {
                WriteArtSet(suffix, mesh, Keys, dir);
            }
            yield return WarmArtSet(suffix, mesh, Keys, SideDir);
            yield return Build(box, suffix, mesh, Keys);
        }

        [UnityTest]
        public IEnumerator Repro_ColdTurnHoldsWithoutPrewarm_ZeroHoldAfterPrewarm_DisplayUntouchedDuringPrewarm()
        {
            // ---- 1) 没有预热：冷转向 front 有保持帧 ----
            var box1 = new Box<Fx>();
            yield return BuildShape(box1, "pw1", "mesh.w63pw1");
            var fx1 = box1.Value;
            var log1 = new List<FrameReading>();
            yield return RunFrames(fx1, Face(FrontDir), 60, log1);
            var hold1 = FirstFrameAllIn(log1, FrontDir, 2);
            UnityEngine.Debug.Log("[dir63-trace] B repro no-prewarm hold frames = " + hold1 + "\n" + Dump(log1, 0, 4));
            Assert.Greater(hold1, 0, "没有预热的冷转向应当有保持帧（提交发生在转向当帧之后）\n" + Dump(log1));
            Finish(fx1);

            // ---- 2) 预热完成后：同一次转向 0 帧保持；预热期间显示不动 ----
            var box2 = new Box<Fx>();
            yield return BuildShape(box2, "pw2", "mesh.w63pw2");
            var fx2 = box2.Value;
            var recomposeBefore = fx2.View.PaperdollRecomposeCountForTests;
            var done = 0;
            Assert.IsTrue(fx2.Factory.PrewarmDirections(fx2.EntityId, (_, ok) => { if (ok) { done++; } }), "预热入口应接受该实体");

            var logPre = new List<FrameReading>();
            yield return RunUntil(fx2, InitialFacing, () => done > 0, 300, logPre);
            Assert.AreEqual(1, done, "预热应在有限帧内完成并回调一次\n" + Dump(logPre, 0, 10));
            Assert.Greater(logPre.Count, 0, "预热期间应至少经过一帧");
            AssertNoMixedDirection(logPre, "预热期间显示");
            foreach (var frame in logPre)
            {
                Assert.IsTrue(frame.Displayed.StartsWith(SideDir), "预热期间已显示方向不变\n" + Dump(logPre));
                Assert.AreEqual(SideDir, frame.Readings[0].Dir, "预热期间可见渲染器仍是 side_r\n" + Dump(logPre));
            }
            Assert.AreEqual(recomposeBefore, fx2.View.PaperdollRecomposeCountForTests, "预热不重合成层");

            var log2 = new List<FrameReading>();
            yield return RunFrames(fx2, Face(FrontDir), 4, log2);
            UnityEngine.Debug.Log("[dir63-trace] B repro after-prewarm turn:\n" + Dump(log2));
            AssertAllInDir2(log2[0], FrontDir);
            AssertNoMixedDirection(log2, "预热后转向");
            Finish(fx2);
        }

        private static void AssertAllInDir2(FrameReading frame, string dir)
        {
            Assert.GreaterOrEqual(frame.Readings.Count, 2, "预热后转向当帧：可见渲染器不足");
            foreach (var r in frame.Readings)
            {
                Assert.AreEqual(dir, r.Dir, $"预热后转向应 0 帧保持：{r}");
            }
        }
    }
}
