#nullable enable
// DirectionSwitchAtomicInvariantTests：ADR-0112 A 条（方向切换原子化）不变量用例——生产装配级，一个方法按场景矩阵
// 逐条分支（每个分支自己造夹具、自己的资源名后缀，互不串缓存）。每条分支都逐帧采样全部可见渲染器（每个纸娃娃层 +
// 整身兜底渲染器）此刻应用的资源与方向，断言的都是"屏幕上实际应用了什么"，不看加载器进度、不看内部回调标志。
//
// 分支：热转向 / 仅镜像 / A->B 未就绪再转 C / A->B 再转回 A / 等待期间状态切换 / 新方向缺美术 / 准备中换装 /
// 准备中进战出战与复活 / 准备中销毁 / 整身兜底渲染器方向不变量（含正对照）。
using System;
using System.Collections;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Foundation.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class DirectionSwitchAtomicInvariantTests : DirectionSwitchFixtureBase
    {
        private static void AssertAllInDir(FrameReading frame, string dir, int minVisible, string what, List<FrameReading> log)
        {
            Assert.GreaterOrEqual(frame.Readings.Count, minVisible, $"{what}：可见渲染器不足\n{Dump(log)}");
            foreach (var r in frame.Readings)
            {
                Assert.AreEqual(dir, r.Dir, $"{what}：{r}\n{Dump(log)}");
            }
        }

        [UnityTest]
        public IEnumerator Invariant_DirectionSwitch_Atomic_ScenarioMatrix()
        {
            yield return HotTurn();
            yield return MirrorOnly();
            yield return SupersedeAtoBtoC();
            yield return ReturnToDisplayedDirection();
            yield return StateChangeDuringHold();
            yield return MissingArtInNewDirection();
            yield return EquipmentChangeDuringPreparation();
            yield return CombatAndRespawnDuringPreparation();
            yield return DestroyDuringPreparation();
            yield return FallbackRendererDirectionInvariant();
        }

        // ① 热转向：新方向全部资源已在缓存 -> 0 帧保持，转向当帧就是新方向。
        private IEnumerator HotTurn()
        {
            const string s = "hot";
            const string mesh = "mesh.w63hot";
            var keys = new[] { "idle" };
            WriteArtSet(s, mesh, keys, SideDir);
            WriteArtSet(s, mesh, keys, FrontDir);
            yield return WarmArtSet(s, mesh, keys, SideDir);
            yield return WarmArtSet(s, mesh, keys, FrontDir);
            var box = new Box<Fx>();
            yield return Build(box, s, mesh, keys);
            var fx = box.Value;
            var commits = 0;
            fx.View.DirectionSlotChanged += _ => commits++;

            var log = new List<FrameReading>();
            yield return RunFrames(fx, Face(FrontDir), 3, log);
            AssertAllInDir(log[0], FrontDir, 2, "①热转向应 0 帧保持", log);
            AssertNoMixedDirection(log, "①热转向");
            Assert.AreEqual(1, commits, "①热转向恰好提交一次方向档位变化");
            Assert.IsFalse(fx.View.HasPendingDirectionSwitch, "①提交后不应还有待切换");
            Finish(fx);
        }

        // ② 仅镜像变化（同槽位，翻转不同）：立即提交，不走准备。
        private IEnumerator MirrorOnly()
        {
            const string s = "mir";
            const string mesh = "mesh.w63mir";
            var keys = new[] { "idle" };
            WriteArtSet(s, mesh, keys, SideDir);
            yield return WarmArtSet(s, mesh, keys, SideDir);
            var box = new Box<Fx>();
            yield return Build(box, s, mesh, keys);
            var fx = box.Value;
            var commits = 0;
            fx.View.DirectionSlotChanged += _ => commits++;
            Assert.IsTrue(fx.View.DisplayedDirection.FlipX, "前置条件：起始朝向 0.0 是镜像档位（翻转）");

            var log = new List<FrameReading>();
            yield return RunFrames(fx, Face(SideDir), 2, log);
            Assert.IsFalse(fx.View.DisplayedDirection.FlipX, "②仅镜像变化应当当帧提交（翻转取消）");
            Assert.IsFalse(fx.View.HasPendingDirectionSwitch, "②仅镜像变化没有待切换");
            Assert.AreEqual(0, commits, "②同槽位不触发 DirectionSlotChanged");
            AssertAllInDir(log[0], SideDir, 2, "②镜像变化资源仍是同一档位", log);
            AssertNoMixedDirection(log, "②镜像");
            Finish(fx);
        }

        // ③ A->B（未就绪）再转 C：显示始终停在 A，从不显示 B；B 的加载自然收尾进缓存，之后转 B 是热转向。
        private IEnumerator SupersedeAtoBtoC()
        {
            const string s = "sup";
            const string mesh = "mesh.w63sup";
            var keys = new[] { "idle" };
            WriteArtSet(s, mesh, keys, SideDir);
            WriteArtSet(s, mesh, keys, FrontDir);
            WriteArtSet(s, mesh, keys, BackDir);
            yield return WarmArtSet(s, mesh, keys, SideDir);
            var box = new Box<Fx>();
            yield return Build(box, s, mesh, keys);
            var fx = box.Value;

            var log = new List<FrameReading>();
            yield return RunFrames(fx, Face(FrontDir), 1, log, tickThisFrame: _ => false);
            yield return RunFrames(fx, Face(BackDir), 1, log, tickThisFrame: _ => false);
            Assert.IsTrue(log[1].Desired.StartsWith(BackDir), "③期望方向已改成 C\n" + Dump(log));
            Assert.IsTrue(log[1].Displayed.StartsWith(SideDir), "③显示方向仍是 A\n" + Dump(log));
            yield return RunFrames(fx, Face(BackDir), 60, log);

            Assert.AreEqual(-1, FirstFrameAnyIn(log, FrontDir), "③任何一帧都不许显示被放弃的 B\n" + Dump(log));
            Assert.GreaterOrEqual(FirstFrameAllIn(log, BackDir, 2), 2, "③最终应提交 C\n" + Dump(log));
            AssertNoMixedDirection(log, "③A->B->C");

            // B 的加载自然收尾进缓存：现在转 B 是热转向（0 帧）。
            var log2 = new List<FrameReading>();
            yield return RunFrames(fx, Face(FrontDir), 2, log2);
            AssertAllInDir(log2[0], FrontDir, 2, "③被放弃的 B 已进缓存，之后转 B 应 0 帧", log2);
            Finish(fx);
        }

        // ④ A->B 再转回 A：待切换被取消，始终 A，没有任何提交/重合成。
        private IEnumerator ReturnToDisplayedDirection()
        {
            const string s = "ret";
            const string mesh = "mesh.w63ret";
            var keys = new[] { "idle" };
            WriteArtSet(s, mesh, keys, SideDir);
            WriteArtSet(s, mesh, keys, FrontDir);
            yield return WarmArtSet(s, mesh, keys, SideDir);
            var box = new Box<Fx>();
            yield return Build(box, s, mesh, keys);
            var fx = box.Value;
            var commits = 0;
            fx.View.DirectionSlotChanged += _ => commits++;
            var recomposeBefore = fx.View.PaperdollRecomposeCountForTests;

            var log = new List<FrameReading>();
            yield return RunFrames(fx, Face(FrontDir), 2, log, tickThisFrame: _ => false);
            Assert.IsTrue(fx.View.HasPendingDirectionSwitch, "前置条件：转向 B 后应有待切换\n" + Dump(log));
            yield return RunFrames(fx, InitialFacing, 40, log);

            Assert.IsFalse(fx.View.HasPendingDirectionSwitch, "④转回 A 应取消待切换\n" + Dump(log));
            Assert.AreEqual(0, commits, "④没有提交过任何档位变化\n" + Dump(log));
            Assert.AreEqual(recomposeBefore, fx.View.PaperdollRecomposeCountForTests, "④没有重合成（无切换/重播）");
            Assert.AreEqual(-1, FirstFrameAnyIn(log, FrontDir), "④始终显示 A\n" + Dump(log));
            AssertNoMixedDirection(log, "④A->B->A");
            Finish(fx);
        }

        // ⑤ 等待期间状态变化（idle->walk(move 剪辑)->attack）：旧方向对应状态的剪辑照常播，身体/装备层同步，提交后新方向同状态。
        private IEnumerator StateChangeDuringHold()
        {
            const string s = "sta";
            const string mesh = "mesh.w63sta";
            var keys = new[] { "idle", "move", "attack" };
            WriteArtSet(s, mesh, keys, SideDir);
            WriteArtSet(s, mesh, keys, FrontDir);
            yield return WarmArtSet(s, mesh, keys, SideDir);
            var box = new Box<Fx>();
            yield return Build(box, s, mesh, keys);
            var fx = box.Value;

            const int hold = 9;
            var log = new List<FrameReading>();
            yield return RunFrames(fx, Face(FrontDir), 60, log, tickThisFrame: i => i >= hold, beforeFrame: i =>
            {
                if (i == 1)
                {
                    fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
                }
                if (i == 5)
                {
                    fx.Bus.PublishImmediate(new AutoAttackSwingEvent(fx.EntityId, new Id("unit.test_dummy_63")));
                }
            });

            var commit = FirstFrameAllIn(log, FrontDir, 2);
            Assert.GreaterOrEqual(commit, hold, "⑤前 hold 帧加载器无进展，不许提交\n" + Dump(log));
            for (var i = 3; i < hold; i++)
            {
                AssertAllInDir(log[i], SideDir, 2, $"⑤等待期间第 {i} 帧应仍是旧方向", log);
                Assert.AreEqual(ClipOf(log[i].Find("body")!), ClipOf(log[i].Find("mainhand")!), $"⑤第 {i} 帧身体层与装备层应同步（同一状态剪辑）\n{Dump(log)}");
            }
            Assert.AreEqual(ClipName(s, "move"), ClipOf(log[4].Find("body")!), "⑤等待期间状态切换到 Walk（move 剪辑）后播旧方向 move 剪辑\n" + Dump(log));
            Assert.AreEqual(ClipName(s, "attack"), ClipOf(log[hold - 1].Find("body")!), "⑤等待期间挥击后播旧方向 attack 剪辑\n" + Dump(log));
            Assert.AreEqual(ClipName(s, "attack"), ClipOf(log[commit].Find("body")!), "⑤提交后新方向仍是同一状态\n" + Dump(log));
            Assert.AreEqual(ClipOf(log[commit].Find("body")!), ClipOf(log[commit].Find("mainhand")!), "⑤提交帧身体/装备同步\n" + Dump(log));
            AssertNoMixedDirection(log, "⑤等待期间状态变化");
            Finish(fx);
        }

        // ⑥ 新方向缺美术：算"有结论"，提交后回落（装备层无剪辑 -> 静态层图；整层没有任何美术 -> 也照样提交，不无限等待）。
        private IEnumerator MissingArtInNewDirection()
        {
            // (a) 装备层的 front 逐层剪辑缺失，身体层 front 剪辑与两层静态图都在。
            {
                const string s = "mis";
                const string mesh = "mesh.w63mis";
                var keys = new[] { "idle" };
                WriteArtSet(s, mesh, keys, SideDir);
                WriteArtSet(s, mesh, keys, FrontDir, equipClips: false);
                yield return WarmArtSet(s, mesh, keys, SideDir);
                var box = new Box<Fx>();
                yield return Build(box, s, mesh, keys);
                var fx = box.Value;

                var log = new List<FrameReading>();
                yield return RunFrames(fx, Face(FrontDir), 90, log, tickThisFrame: i => i >= 2);
                var commit = FirstFrameAllIn(log, FrontDir, 2);
                Assert.GreaterOrEqual(commit, 2, "⑥(a)缺美术也应有结论并提交\n" + Dump(log));
                AssertNoMixedDirection(log, "⑥(a)装备层缺 front 剪辑");
                var last = log[log.Count - 1];
                Assert.AreEqual(LayerResourceId(null, ClipName(s, "idle"), FrontDir, "body").Value, last.Find("body")!.Resource, "⑥(a)身体层播 front 剪辑");
                Assert.AreEqual(StaticLayerId(s, mesh, FrontDir, "mainhand").Value, last.Find("mainhand")!.Resource, "⑥(a)装备层没有 front 剪辑，提交后回落到 front 静态层图");
                Finish(fx);
            }

            // (b) front 方向一份美术都没有（剪辑、静态图全缺）：不能无限等待，有限帧内提交。
            {
                const string s = "mis2";
                const string mesh = "mesh.w63mis2";
                var keys = new[] { "idle" };
                WriteArtSet(s, mesh, keys, SideDir);
                yield return WarmArtSet(s, mesh, keys, SideDir);
                var box = new Box<Fx>();
                yield return Build(box, s, mesh, keys);
                var fx = box.Value;

                var log = new List<FrameReading>();
                yield return RunUntil(fx, Face(FrontDir), () => !fx.View.HasPendingDirectionSwitch && fx.View.DisplayedDirection.SlotId.Value.EndsWith(FrontDir), 90, log);
                Assert.IsFalse(fx.View.HasPendingDirectionSwitch, "⑥(b)全缺美术也必须在有限帧内提交（不无限等待）\n" + Dump(log));
                Assert.IsTrue(fx.View.DisplayedDirection.SlotId.Value.EndsWith(FrontDir), "⑥(b)提交后显示方向是 front\n" + Dump(log));
                for (var i = 0; i < log.Count; i++)
                {
                    if (log[i].Displayed.StartsWith(SideDir))
                    {
                        Assert.IsFalse(log[i].AnyPlaceholder(), $"⑥(b)提交前第 {i} 帧不许出现占位方块\n{Dump(log)}");
                        Assert.IsTrue(log[i].Dirs().Count <= 1, $"⑥(b)提交前第 {i} 帧不许混合\n{Dump(log)}");
                    }
                }
                Finish(fx);
            }
        }

        // ⑦ 准备期间换装：重新计算完成条件，提交时不混（身体 front + 新装备 front）。
        private IEnumerator EquipmentChangeDuringPreparation()
        {
            const string s = "eqp";
            const string meshA = "mesh.w63eqa";
            const string meshB = "mesh.w63eqb";
            var keys = new[] { "idle" };
            WriteArtSet(s, meshA, keys, SideDir);
            WriteArtSet(s, meshA, keys, FrontDir);
            // meshB 的 side_r 热、front 冷。
            WriteLayerArt(s, meshB, ClipName(s, "idle"), SideDir, "mainhand");
            WriteLayerArt(s, meshB, ClipName(s, "idle"), FrontDir, "mainhand");
            yield return WarmArtSet(s, meshA, keys, SideDir);
            yield return WarmEffectCache(LayerResourceId(meshB, ClipName(s, "idle"), SideDir, "mainhand"));
            yield return WarmImage(StaticLayerId(s, meshB, SideDir, "mainhand"));
            var box = new Box<Fx>();
            yield return Build(box, s, meshA, keys);
            var fx = box.Value;

            var log = new List<FrameReading>();
            yield return RunFrames(fx, Face(FrontDir), 60, log, tickThisFrame: i => i >= 5, beforeFrame: i =>
            {
                if (i == 2)
                {
                    EquipMesh(fx, s + "_w2", "slot.mainhand", meshB);
                }
            });

            AssertNoMixedDirection(log, "⑦准备期间换装");
            var commit = FirstFrameAllIn(log, FrontDir, 2);
            Assert.GreaterOrEqual(commit, 5, "⑦应提交且不早于加载器有进展\n" + Dump(log));
            var last = log[log.Count - 1];
            Assert.AreEqual(LayerResourceId(meshB, ClipName(s, "idle"), FrontDir, "mainhand").Value, last.Find("mainhand")!.Resource, "⑦提交后装备层是新装备的 front 剪辑\n" + Dump(log));
            Assert.AreEqual(LayerResourceId(null, ClipName(s, "idle"), FrontDir, "body").Value, last.Find("body")!.Resource, "⑦提交后身体层是 front 剪辑\n" + Dump(log));
            Finish(fx);
        }

        // ⑧ 准备期间进战/出战/复活：姿态变体与显示复位都对，且落在已显示方向上，提交后新方向同一套。
        private IEnumerator CombatAndRespawnDuringPreparation()
        {
            const string s = "cmb";
            const string mesh = "mesh.w63cmb";
            var keys = new[] { "idle", "combat_idle" };
            WriteArtSet(s, mesh, keys, SideDir);
            WriteArtSet(s, mesh, keys, FrontDir);
            WriteArtSet(s, mesh, keys, BackDir);
            yield return WarmArtSet(s, mesh, keys, SideDir);
            var inCombat = false;
            var box = new Box<Fx>();
            yield return Build(box, s, mesh, keys, f => f.CombatProbe = id => inCombat);
            var fx = box.Value;
            var idle = ClipName(s, "idle");
            var combat = ClipName(s, "combat_idle");

            // (i) 等待期间进战 -> 旧方向 combat_idle；提交后新方向 combat_idle；出战 -> 新方向 idle。
            var log = new List<FrameReading>();
            yield return RunFrames(fx, Face(FrontDir), 40, log, tickThisFrame: i => i >= 6, beforeFrame: i =>
            {
                if (i == 1)
                {
                    inCombat = true;
                    fx.Bus.PublishImmediate(new CombatEnteredEvent(fx.EntityId));
                }
            });
            var commit = FirstFrameAllIn(log, FrontDir, 2);
            Assert.GreaterOrEqual(commit, 6, "⑧(i)提交不早于加载器有进展\n" + Dump(log));
            for (var i = 3; i < 6; i++)
            {
                AssertAllInDir(log[i], SideDir, 2, $"⑧(i)等待期间第 {i} 帧应仍是旧方向", log);
                Assert.AreEqual(combat, ClipOf(log[i].Find("body")!), $"⑧(i)等待期间进战后旧方向播 combat_idle（身体）\n{Dump(log)}");
                Assert.AreEqual(combat, ClipOf(log[i].Find("mainhand")!), $"⑧(i)等待期间进战后旧方向播 combat_idle（装备）\n{Dump(log)}");
            }
            Assert.AreEqual(combat, ClipOf(log[log.Count - 1].Find("body")!), "⑧(i)提交后新方向 combat_idle（身体）\n" + Dump(log));
            Assert.AreEqual(combat, ClipOf(log[log.Count - 1].Find("mainhand")!), "⑧(i)提交后新方向 combat_idle（装备）\n" + Dump(log));
            AssertNoMixedDirection(log, "⑧(i)准备期间进战");

            inCombat = false;
            fx.Bus.PublishImmediate(new CombatLeftEvent(fx.EntityId));
            var logLeft = new List<FrameReading>();
            yield return RunFrames(fx, Face(FrontDir), 6, logLeft);
            AssertAllInDir(logLeft[logLeft.Count - 1], FrontDir, 2, "⑧(i)出战后仍是 front", logLeft);
            Assert.AreEqual(idle, ClipOf(logLeft[logLeft.Count - 1].Find("body")!), "⑧(i)出战后切回 idle\n" + Dump(logLeft));

            // (ii) 在战 -> 转向 back 的等待期间复活（规则层已脱战）：显示复位到 idle，落在已显示方向（front）上。
            inCombat = true;
            fx.Bus.PublishImmediate(new CombatEnteredEvent(fx.EntityId));
            var logIn = new List<FrameReading>();
            yield return RunFrames(fx, Face(FrontDir), 4, logIn);
            Assert.AreEqual(combat, ClipOf(logIn[logIn.Count - 1].Find("body")!), "前置条件：转 back 前在战\n" + Dump(logIn));
            var logRespawn = new List<FrameReading>();
            yield return RunFrames(fx, Face(BackDir), 40, logRespawn, tickThisFrame: i => i >= 6, beforeFrame: i =>
            {
                if (i == 1)
                {
                    inCombat = false;
                    fx.Bus.PublishImmediate(new UnitRespawnedEvent(fx.EntityId, RespawnPolicy.RespawnPoint));
                }
            });
            var commit2 = FirstFrameAllIn(logRespawn, BackDir, 2);
            Assert.GreaterOrEqual(commit2, 6, "⑧(ii)提交不早于加载器有进展\n" + Dump(logRespawn));
            for (var i = 3; i < 6; i++)
            {
                AssertAllInDir(logRespawn[i], FrontDir, 2, $"⑧(ii)复活后等待期间第 {i} 帧应仍是已显示方向 front", logRespawn);
                Assert.AreEqual(idle, ClipOf(logRespawn[i].Find("body")!), $"⑧(ii)复活复位到 idle（身体）\n{Dump(logRespawn)}");
                Assert.AreEqual(idle, ClipOf(logRespawn[i].Find("mainhand")!), $"⑧(ii)复活复位到 idle（装备）\n{Dump(logRespawn)}");
            }
            Assert.AreEqual(idle, ClipOf(logRespawn[logRespawn.Count - 1].Find("body")!), "⑧(ii)提交后新方向 idle\n" + Dump(logRespawn));
            AssertNoMixedDirection(logRespawn, "⑧(ii)准备期间复活");
            Finish(fx);
        }

        // ⑨ 准备期间销毁：不抛异常，方向准备记账清空，之后加载完成回调不泄漏。
        private IEnumerator DestroyDuringPreparation()
        {
            const string s = "des";
            const string mesh = "mesh.w63des";
            var keys = new[] { "idle" };
            WriteArtSet(s, mesh, keys, SideDir);
            WriteArtSet(s, mesh, keys, FrontDir);
            yield return WarmArtSet(s, mesh, keys, SideDir);
            var box = new Box<Fx>();
            yield return Build(box, s, mesh, keys);
            var fx = box.Value;

            var log = new List<FrameReading>();
            yield return RunFrames(fx, Face(FrontDir), 2, log, tickThisFrame: _ => false);
            Assert.IsTrue(fx.View.HasPendingDirectionSwitch, "前置条件：准备中\n" + Dump(log));
            Assert.IsTrue(fx.Factory.HasDirectionPreparationStateForTests(fx.EntityId), "前置条件：有方向准备记账");

            fx.Bus.PublishImmediate(new EntityDestroyedEvent(fx.EntityId));
            fx.EquipSource.Dispose();
            fx.View.Destroy();
            Assert.IsFalse(fx.Factory.HasDirectionPreparationStateForTests(fx.EntityId), "⑨销毁后方向准备记账应清空");

            // 之后加载器把在途资源加载完成：回调不得抛异常（Unity 测试框架会把异常日志判失败）、不得重新建立记账。
            for (var i = 0; i < 30; i++)
            {
                Loader.Tick();
                yield return null;
            }
            Assert.IsFalse(fx.Factory.HasDirectionPreparationStateForTests(fx.EntityId), "⑨销毁后的加载完成回调不得重建记账");
        }

        // ⑩ 整身兜底渲染器方向不变量（A8）：兜底渲染器可见时其内容方向必须等于已显示方向，否则隐藏、显示静态层。
        //    外形只有整身方向美术（没有逐层剪辑）。先热转向到 front（整身美术在缓存里）——兜底渲染器显示 front 内容；
        //    再冷转向 back：
        //      负分支：back 没有整身美术（两档都缺）-> 提交后兜底渲染器必须隐藏，不许继续显示 front 内容，静态层显示 back；
        //      正对照：back 有整身美术 -> 提交后兜底渲染器显示 back 内容（证明负分支的"隐藏"不是因为兜底渲染器本来就不出现）。
        private IEnumerator FallbackRendererDirectionInvariant()
        {
            foreach (var withBackWholeBody in new[] { false, true })
            {
                var s = withBackWholeBody ? "fbp" : "fbn";
                var clip = ClipName(s, "idle");
                Id WholeBody(string dir) => new Id($"sprite_anim.{clip}__{dir}");
                foreach (var dir in new[] { SideDir, FrontDir, BackDir })
                {
                    WriteStaticImage(StaticLayerId(s, null, dir, "body"), Color.gray);
                }
                WriteEffectResource(WholeBody(FrontDir), Color.red, Color.green);
                if (withBackWholeBody)
                {
                    WriteEffectResource(WholeBody(BackDir), Color.red, Color.green);
                }
                yield return WarmEffectCache(WholeBody(FrontDir));
                yield return WarmImage(StaticLayerId(s, null, SideDir, "body"));
                yield return WarmImage(StaticLayerId(s, null, FrontDir, "body"));

                var box = new Box<Fx>();
                yield return Build(box, s, null, new[] { "idle" });
                var fx = box.Value;

                var pre = new List<FrameReading>();
                yield return RunFrames(fx, Face(FrontDir), 4, pre);
                var fallbackFront = pre[pre.Count - 1].Find("(fallback)");
                Assert.IsNotNull(fallbackFront, $"前置条件：该外形只有整身美术，兜底渲染器应可见\n{Dump(pre)}");
                Assert.AreEqual(FrontDir, fallbackFront!.Dir, $"前置条件：转到 front 后兜底渲染器显示 front 整身剪辑\n{Dump(pre)}");

                var log = new List<FrameReading>();
                yield return RunFrames(fx, Face(BackDir), 60, log, tickThisFrame: i => i >= 3);
                var tag = $"⑩兜底渲染器方向不变量（back 整身美术{(withBackWholeBody ? "有" : "无")}）";
                AssertNoMixedDirection(log, tag);
                Assert.IsFalse(fx.View.HasPendingDirectionSwitch, "⑩应已提交\n" + Dump(log));
                var last = log[log.Count - 1];
                var fallbackAfter = last.Find("(fallback)");
                if (withBackWholeBody)
                {
                    Assert.IsNotNull(fallbackAfter, "⑩正对照：back 有整身美术，提交后兜底渲染器应可见\n" + Dump(log));
                    Assert.AreEqual(BackDir, fallbackAfter!.Dir, "⑩正对照：兜底渲染器显示 back 整身剪辑\n" + Dump(log));
                }
                else
                {
                    Assert.IsNull(fallbackAfter, "⑩负分支：back 没有整身美术，兜底渲染器必须隐藏（不许继续显示 front 内容）\n" + Dump(log));
                    var body = last.Find("body");
                    Assert.IsNotNull(body, "⑩负分支：兜底渲染器隐藏后应显示静态层\n" + Dump(log));
                    Assert.AreEqual(StaticLayerId(s, null, BackDir, "body").Value, body!.Resource, "⑩负分支：静态层图是 back\n" + Dump(log));
                }
                Finish(fx);
            }
        }
    }
}
