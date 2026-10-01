#nullable enable
// DirectionPrewarmInvariantTests：ADR-0112 B 条（按实体预热全部方向）不变量用例——生产装配级，一个方法按场景逐条分支：
//   ① 进度单调不减、终值 = 总数（镜像对去重）、完成回调恰好一次、预热后转向任一方向都是 0 帧；
//   ② 选项默认 None 不多发任何加载（正对照：OnAttach 自动预热，全部方向的剪辑进缓存）；
//   ③ 让路：预热中有真实转向的方向准备在途时，档位之间暂停（已开始档位数不增长），提交后继续；
//   ④ 粘性：预热完成后换装，新层补预热，之后转向仍是 0 帧且是新装备的美术；
//   ⑤ 销毁：预热取消、回调 false 一次、记账清空，之后加载完成不泄漏。
// 让路/销毁等需要"加载分散到多帧"的分支把夹具加载器的主线程预算压到极小（每个 Tick 只做一个工作单元），
// 让先后发起的加载按顺序在不同帧完成，避免在微型美术下一帧全部做完而观察不到中间态。
using System.Collections;
using System.Collections.Generic;
using Adapter.Unity.Presentation;
using Core.Carriers.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:render")]
    public sealed class DirectionPrewarmInvariantTests : DirectionSwitchFixtureBase
    {
        private static readonly string[] Keys = { "idle" };

        private sealed class Callback
        {
            public int Calls;
            public bool LastOk;

            public void Invoke(Core.Foundation.Common.Id _, bool ok)
            {
                Calls++;
                LastOk = ok;
            }
        }

        private IEnumerator BuildShape(
            Box<Fx> box, string suffix, string mesh, System.Action<UnityViewFactory>? configure = null)
        {
            foreach (var dir in AllDirs)
            {
                WriteArtSet(suffix, mesh, Keys, dir);
            }
            yield return WarmArtSet(suffix, mesh, Keys, SideDir);
            yield return Build(box, suffix, mesh, Keys, configure);
        }

        private bool IsCached(string suffix, string? mesh, string dir)
        {
            var ok = Loader.TryGetEffect(LayerResourceId(null, ClipName(suffix, "idle"), dir, "body"), out _);
            if (mesh != null)
            {
                ok &= Loader.TryGetEffect(LayerResourceId(mesh, ClipName(suffix, "idle"), dir, "mainhand"), out _);
            }
            return ok;
        }

        [UnityTest]
        public IEnumerator Invariant_Prewarm_Progress_Default_Yield_Sticky_Destroy()
        {
            yield return ProgressAndImmediateTurns();
            yield return DefaultNoneVersusOnAttach();
            yield return YieldToTurnPreparation();
            yield return StickyTopUpAfterReequip();
            yield return DestroyCleansUp();
        }

        // ① 进度单调不减、终值 = 总数（镜像对去重）、回调恰好一次；预热后转向任一方向 0 帧。
        private IEnumerator ProgressAndImmediateTurns()
        {
            const string s = "ppr";
            const string mesh = "mesh.w63ppr";
            var box = new Box<Fx>();
            yield return BuildShape(box, s, mesh);
            var fx = box.Value;
            var cb = new Callback();

            Assert.IsTrue(fx.Factory.TryGetDirectionPrewarmProgress(fx.EntityId, out var ready0, out var total));
            Assert.AreEqual(fx.View.GetDistinctDirectionSlots().Count, total, "总数 = 镜像对去重后的方向档位数");
            Assert.AreEqual(AllDirs.Length, total, "8 方向、默认镜像表：五个不同档位（不是 8）");
            Assert.Less(ready0, total, "预热前不应全部就绪");

            Assert.IsTrue(fx.Factory.PrewarmDirections(fx.EntityId, cb.Invoke));
            var last = -1;
            var frames = 0;
            var log = new List<FrameReading>();
            while (cb.Calls == 0 && frames < 300)
            {
                yield return RunFrames(fx, InitialFacing, 1, log);
                frames++;
                Assert.IsTrue(fx.Factory.TryGetDirectionPrewarmProgress(fx.EntityId, out var ready, out var t2));
                Assert.AreEqual(total, t2, "总数不变");
                Assert.GreaterOrEqual(ready, last, "进度单调不减");
                Assert.LessOrEqual(ready, total);
                last = ready;
            }
            Assert.AreEqual(1, cb.Calls, "完成回调恰好一次");
            Assert.IsTrue(cb.LastOk, "完成回调 ok = true");
            Assert.IsTrue(fx.Factory.TryGetDirectionPrewarmProgress(fx.EntityId, out var readyEnd, out _));
            Assert.AreEqual(total, readyEnd, "终值 = 总数");
            Assert.IsTrue(fx.Factory.PrewarmDirections(fx.EntityId, cb.Invoke), "重复调用幂等，已完成时直接回调");
            Assert.AreEqual(2, cb.Calls, "对已完成的实体再调用：新回调立即触发一次");

            // 预热后首次转向任一方向都是 0 帧（且是该方向的剪辑）。
            foreach (var dir in new[] { FrontDir, FrontSideDir, BackSideDir, BackDir })
            {
                var l = new List<FrameReading>();
                yield return RunFrames(fx, Face(dir), 2, l);
                Assert.GreaterOrEqual(l[0].Readings.Count, 2, $"转向 {dir} 当帧可见渲染器不足\n{Dump(l)}");
                foreach (var r in l[0].Readings)
                {
                    Assert.AreEqual(dir, r.Dir, $"预热后首次转向 {dir} 应 0 帧：{r}\n{Dump(l)}");
                    Assert.IsFalse(r.Placeholder, "不应有占位方块\n" + Dump(l));
                }
            }
            Finish(fx);
        }

        // ② 默认 None 不多发任何加载；正对照 OnAttach 自动预热。
        private IEnumerator DefaultNoneVersusOnAttach()
        {
            {
                const string s = "pno";
                const string mesh = "mesh.w63pno";
                var box = new Box<Fx>();
                yield return BuildShape(box, s, mesh);
                var fx = box.Value;
                Assert.AreEqual(DirectionPrewarmPolicy.None, fx.Factory.DirectionPrewarm, "默认选项 None");
                var log = new List<FrameReading>();
                yield return RunFrames(fx, InitialFacing, 40, log);
                foreach (var dir in new[] { FrontDir, FrontSideDir, BackSideDir, BackDir })
                {
                    Assert.IsFalse(IsCached(s, mesh, dir), $"None：{dir} 不应被加载");
                    Assert.IsFalse(Loader.TryGetSprite(StaticLayerId(s, null, dir, "body"), out _), $"None：{dir} 静态层图不应被加载");
                }
                Assert.AreEqual(0, fx.Factory.DirectionPrewarmStartedSlotCountForTests(fx.EntityId), "None：没有预热记账");
                Assert.IsTrue(fx.Factory.TryGetDirectionPrewarmProgress(fx.EntityId, out var ready, out var total));
                Assert.Less(ready, total, "None：只读进度查询不发起任何加载");
                Finish(fx);
            }
            {
                const string s = "pat";
                const string mesh = "mesh.w63pat";
                var box = new Box<Fx>();
                yield return BuildShape(box, s, mesh, f => f.DirectionPrewarm = DirectionPrewarmPolicy.OnAttach);
                var fx = box.Value;
                var log = new List<FrameReading>();
                yield return RunUntil(fx, InitialFacing, () =>
                    fx.Factory.TryGetDirectionPrewarmProgress(fx.EntityId, out var r, out var t) && r == t, 300, log);
                Assert.IsTrue(fx.Factory.TryGetDirectionPrewarmProgress(fx.EntityId, out var ready, out var total));
                Assert.AreEqual(total, ready, "OnAttach（正对照）：自动预热完成");
                foreach (var dir in AllDirs)
                {
                    Assert.IsTrue(IsCached(s, mesh, dir), $"OnAttach（正对照）：{dir} 全部剪辑进缓存");
                    Assert.IsTrue(Loader.TryGetSprite(StaticLayerId(s, null, dir, "body"), out _), $"OnAttach（正对照）：{dir} 身体静态层图进缓存");
                    Assert.IsTrue(Loader.TryGetSprite(StaticLayerId(s, mesh, dir, "mainhand"), out _), $"OnAttach（正对照）：{dir} 装备静态层图进缓存");
                }
                Finish(fx);
            }
        }

        // ③ 让路：预热中出现真实转向的方向准备时，档位之间暂停；提交后继续到完成。
        private IEnumerator YieldToTurnPreparation()
        {
            const string s = "pyl";
            const string mesh = "mesh.w63pyl";
            var box = new Box<Fx>();
            yield return BuildShape(box, s, mesh);
            var fx = box.Value;
            var oldBudget = Loader.MainThreadBudgetMilliseconds;
            Loader.MainThreadBudgetMilliseconds = 0.0001;
            try
            {
                Assert.IsTrue(fx.Factory.PrewarmDirections(fx.EntityId));
                var started0 = fx.Factory.DirectionPrewarmStartedSlotCountForTests(fx.EntityId);
                Assert.AreEqual(2, started0, "前置条件：已显示档位同步完成，第二个档位已开始（加载在途）");

                // 不 Tick 的几帧让后台读盘先完成，然后发起真实转向（front，档位序列里排在第三）。
                var log = new List<FrameReading>();
                yield return RunFrames(fx, InitialFacing, 6, log, tickThisFrame: _ => false);
                yield return RunFrames(fx, Face(FrontDir), 1, log, tickThisFrame: _ => false);
                Assert.IsTrue(fx.View.HasPendingDirectionSwitch, "前置条件：转向 front 有待切换");

                var sawYield = false;
                var frames = 0;
                while (frames < 400 && fx.View.HasPendingDirectionSwitch)
                {
                    var startedBefore = fx.Factory.DirectionPrewarmStartedSlotCountForTests(fx.EntityId);
                    yield return RunFrames(fx, Face(FrontDir), 1, log);
                    frames++;
                    var startedAfter = fx.Factory.DirectionPrewarmStartedSlotCountForTests(fx.EntityId);
                    if (fx.View.HasPendingDirectionSwitch)
                    {
                        Assert.LessOrEqual(startedAfter, startedBefore, "让路：转向准备在途期间预热不得开始新档位\n" + Dump(log));
                        fx.Factory.TryGetDirectionPrewarmProgress(fx.EntityId, out var ready, out _);
                        if (ready >= 2 && startedAfter == 2)
                        {
                            sawYield = true;
                        }
                    }
                }
                Assert.IsFalse(fx.View.HasPendingDirectionSwitch, "转向应在有限帧内提交\n" + Dump(log));
                Assert.IsTrue(sawYield, "应观察到：第二档位已完成、但因转向在途而不开始第三档位（让路）\n" + Dump(log));

                // 提交后预热继续，直到全部完成。
                var frames2 = new List<FrameReading>();
                yield return RunUntil(fx, Face(FrontDir), () =>
                    fx.Factory.TryGetDirectionPrewarmProgress(fx.EntityId, out var r, out var t) && r == t, 400, frames2);
                fx.Factory.TryGetDirectionPrewarmProgress(fx.EntityId, out var readyEnd, out var totalEnd);
                Assert.AreEqual(totalEnd, readyEnd, "让路结束后预热继续并完成");
            }
            finally
            {
                Loader.MainThreadBudgetMilliseconds = oldBudget;
            }
            Finish(fx);
        }

        // ④ 粘性：预热完成后换装，对新增层补预热；之后转向任一方向仍是 0 帧，且是新装备的美术。
        private IEnumerator StickyTopUpAfterReequip()
        {
            const string s = "pst";
            const string meshA = "mesh.w63psta";
            const string meshB = "mesh.w63pstb";
            var box = new Box<Fx>();
            foreach (var dir in AllDirs)
            {
                WriteLayerArt(s, meshB, ClipName(s, "idle"), dir, "mainhand");
            }
            yield return BuildShape(box, s, meshA);
            var fx = box.Value;
            var cb = new Callback();
            Assert.IsTrue(fx.Factory.PrewarmDirections(fx.EntityId, cb.Invoke));
            var log = new List<FrameReading>();
            yield return RunUntil(fx, InitialFacing, () => cb.Calls > 0, 300, log);
            Assert.AreEqual(1, cb.Calls, "前置条件：首轮预热完成");

            EquipMesh(fx, s + "_w2", "slot.mainhand", meshB);
            fx.Factory.TryGetDirectionPrewarmProgress(fx.EntityId, out var readyAfterEquip, out var total);
            Assert.Less(readyAfterEquip, total, "换装后新层的方向还没预热完，进度回落（补预热一轮）");
            yield return RunUntil(fx, InitialFacing, () =>
                fx.Factory.TryGetDirectionPrewarmProgress(fx.EntityId, out var r, out var t) && r == t, 300, log);
            fx.Factory.TryGetDirectionPrewarmProgress(fx.EntityId, out var readyEnd, out _);
            Assert.AreEqual(total, readyEnd, "补预热完成");
            foreach (var dir in AllDirs)
            {
                Assert.IsTrue(Loader.TryGetEffect(LayerResourceId(meshB, ClipName(s, "idle"), dir, "mainhand"), out _), $"粘性：新装备 {dir} 剪辑进缓存");
            }

            foreach (var dir in new[] { FrontDir, BackDir, FrontSideDir })
            {
                var l = new List<FrameReading>();
                yield return RunFrames(fx, Face(dir), 2, l);
                var mainhand = l[0].Find("mainhand");
                Assert.IsNotNull(mainhand, $"转向 {dir} 当帧装备层应可见\n{Dump(l)}");
                Assert.AreEqual(LayerResourceId(meshB, ClipName(s, "idle"), dir, "mainhand").Value, mainhand!.Resource, $"补预热后转向 {dir} 0 帧且是新装备美术\n{Dump(l)}");
                Assert.AreEqual(dir, l[0].Find("body")!.Dir, "身体层同一帧到位\n" + Dump(l));
            }
            Finish(fx);
        }

        // ⑤ 销毁：预热取消，回调 false 一次，记账清空，之后加载完成不泄漏。
        private IEnumerator DestroyCleansUp()
        {
            const string s = "pdt";
            const string mesh = "mesh.w63pdt";
            var box = new Box<Fx>();
            yield return BuildShape(box, s, mesh);
            var fx = box.Value;
            var oldBudget = Loader.MainThreadBudgetMilliseconds;
            Loader.MainThreadBudgetMilliseconds = 0.0001;
            try
            {
                var cb = new Callback();
                Assert.IsTrue(fx.Factory.PrewarmDirections(fx.EntityId, cb.Invoke));
                var log = new List<FrameReading>();
                yield return RunFrames(fx, InitialFacing, 3, log);
                Assert.AreEqual(0, cb.Calls, "前置条件：预热还没完成");
                Assert.IsTrue(fx.Factory.HasDirectionPreparationStateForTests(fx.EntityId));

                fx.Bus.PublishImmediate(new EntityDestroyedEvent(fx.EntityId));
                fx.EquipSource.Dispose();
                fx.View.Destroy();

                Assert.AreEqual(1, cb.Calls, "销毁：未完成的预热回调恰好一次");
                Assert.IsFalse(cb.LastOk, "销毁：回调 ok = false");
                Assert.IsFalse(fx.Factory.HasDirectionPreparationStateForTests(fx.EntityId), "销毁：方向准备/预热记账清空");
                Assert.AreEqual(0, fx.Factory.DirectionPrewarmStartedSlotCountForTests(fx.EntityId));

                for (var i = 0; i < 40; i++)
                {
                    Loader.Tick();
                    yield return null;
                }
                Assert.AreEqual(1, cb.Calls, "销毁后加载完成不再触发任何回调");
                Assert.IsFalse(fx.Factory.HasDirectionPreparationStateForTests(fx.EntityId), "销毁后加载完成回调不得重建记账");
            }
            finally
            {
                Loader.MainThreadBudgetMilliseconds = oldBudget;
            }
        }
    }
}
