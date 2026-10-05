#nullable enable
// ShowcaseTests：真实美术手感演示场景（ADR-0154）的运行时验收。
// 期望值都由数据与规则算出（命中顿帧 tick 取预设行、伤害量取独立于呈现的逻辑记录），不写裸数。
// 约束：演示场景只改呈现——同一段输入在演示场景与原试玩场景上的逻辑指纹逐字节一致；原试玩场景不受影响。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.LabHost;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Lab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.LabHost
{
    [Category("module:lab")]
    public sealed class ShowcaseTests
    {
        private const double Frame = 1.0 / 60.0;
        private const string AttackKey = "j";
        private GameObject? _go;
        private LabPlayground? _pg;
        private Adapters.Stub.StubInput? _input;
        private string _saveDir = string.Empty;
        private readonly List<string> _warnings = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "lab_showcase_test_" + Guid.NewGuid().ToString("N"));
            _warnings.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            Dispose();
            if (Directory.Exists(_saveDir))
            {
                Directory.Delete(_saveDir, true);
            }
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
            {
                _warnings.Add(condition);
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

        private LabPlayground NewPlayground(bool showcase, string cell = "2d_action")
        {
            _go = new GameObject("ShowcaseTest");
            var pg = _go.AddComponent<LabPlayground>();
            pg.Configure(cell);
            pg.Showcase = showcase;
            pg.ManualDrive = true;
            _input = new Adapters.Stub.StubInput();
            pg.InputSource = _input;
            pg.PadReader = _ => false;
            pg.SaveDirectory = _saveDir;
            Assert.IsTrue(pg.Begin(), pg.Model.Status);
            _pg = pg;
            return pg;
        }

        /// <summary>推进 n 个 60 fps 帧；每 4 帧让出一帧，给资源加载器的异步读文件留出真实时间。</summary>
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

        private IEnumerator Tap(string key, int hold = 3)
        {
            _input!.Press(key);
            yield return Frames(hold);
            _input.Release(key);
        }

        private int ExpectedAttackerTicks(string preset)
        {
            var ctx = _pg!.Session!.Context!;
            var feel = ctx.World.Gameplay.Feel!.Feel;
            var row = feel.Profiles.GetPreset(preset)!;
            double Value(string field) => row.Values.First(w => w.Field == field).Value.AsNumber();
            var step = _pg.Session.StepSeconds;
            return Math.Min(
                FeelCalibration.MillisecondsToTicks(Value(FeelFieldNames.AttackerHitstopMs), step),
                FeelCalibration.MillisecondsToTicks(Value(FeelFieldNames.AttackerHitstopCapMs), step));
        }

        // ───────── 美术：全部精灵来自演示资源，0 回退占位 ─────────

        [UnityTest]
        public IEnumerator EveryUnitKind_RendersShowcaseSprites_ZeroPlaceholderFallback()
        {
            var pg = NewPlayground(true);
            Assert.IsNotNull(pg.Stage!.Showcase, "演示场景应有导演");
            var kinds = pg.Model.DummyKinds.Select(k => k.Id).Where(k => !k.StartsWith("pack_", StringComparison.Ordinal)).ToList();
            foreach (var kind in kinds)
            {
                pg.SpawnDummy(kind);
            }

            yield return Frames(90);
            var sprites = pg.Stage.RenderedSprites();
            Assert.GreaterOrEqual(sprites.Count, kinds.Count + 1, "玩家与每种靶子都应有渲染的精灵");
            var foreign = sprites.Where(s => !(s.Value.StartsWith("sprite_anim.show_", StringComparison.Ordinal) || s.Value.StartsWith("layer.creature_show_", StringComparison.Ordinal))).ToList();
            Assert.AreEqual(0, foreign.Count, "回退到占位的精灵：" + string.Join(", ", foreign.Select(f => f.Key.Value + "=" + f.Value)) + "\n警告：" + string.Join("\n", _warnings.Take(12)) + "\n切换：" + string.Join(" | ", pg.Stage.Record.ClipTransitions));

            var looks = new HashSet<string>(pg.Stage.ShowcaseDisplay!.Resolved.Values);
            foreach (var look in new[] { ShowcaseDisplayRegistry.Hero, ShowcaseDisplayRegistry.Grunt, ShowcaseDisplayRegistry.Brute, ShowcaseDisplayRegistry.Dummy })
            {
                Assert.IsTrue(looks.Contains(look), "外形登记应覆盖 " + look + "，实际 " + string.Join(",", looks));
            }

            var degraded = _warnings.Where(w => w.Contains("退化为单帧剪辑") && w.Contains("show_")).ToList();
            Assert.AreEqual(0, degraded.Count, "演示外形的动画状态退化为单帧：\n" + string.Join("\n", degraded));
            Assert.AreEqual(0, pg.Stage.Record.Errors.Count, string.Join(" | ", pg.Stage.Record.Errors));
        }

        // ───────── 菜单指路：占位美术场景的面板头部有第二行提示（指向同一视角的演示场景），演示场景没有 ─────────

        private static readonly string[] HintCells = { "2d_action", "2_5d_action", "3d_action" };

        [UnityTest]
        public IEnumerator PanelHint_ShownInPlaceholderScene_NamesTheShowcaseOfTheSameCombo_AbsentInEveryShowcase()
        {
            foreach (var cell in HintCells)
            {
                var placeholder = NewPlayground(false, cell);
                yield return Frames(4);
                var expectedName = LabPlayground.ShowcaseMenuNameOf(cell);
                Assert.IsNotNull(expectedName, cell + " 应有对应的演示场景菜单条目");
                Assert.AreEqual(LabPlayground.PlaceholderHintFor(cell), placeholder.PanelHint, cell + "：占位美术场景应有指向演示场景的提示");
                StringAssert.Contains(expectedName!, placeholder.PanelHint, cell + "：提示应点名同一视角的演示场景");
                foreach (var other in HintCells)
                {
                    if (other != cell)
                    {
                        StringAssert.DoesNotContain(LabPlayground.ShowcaseMenuNameOf(other)!, placeholder.PanelHint, cell + "：提示不应指向另一个视角（" + other + "）的演示场景");
                    }
                }

                Dispose();

                var showcase = NewPlayground(true, cell);
                yield return Frames(4);
                Assert.IsNull(showcase.PanelHint, cell + "：演示场景不应出现占位美术提示");
                Dispose();
            }
        }

        // ───────── 三连击打精英：受击动画、顿帧、闪白、伤害数字 ─────────

        [UnityTest]
        public IEnumerator ThreeHitCombo_OnElite_ShowsHitAnimation_Hitstop_Flash_AndDamageNumbers()
        {
            var pg = NewPlayground(true);
            pg.SpawnDummy("elite");
            yield return Frames(30);
            var stage = pg.Stage!;
            var director = stage.Showcase!;
            var flashBefore = stage.FlashesApplied;
            for (var i = 0; i < 3; i++)
            {
                yield return Tap(AttackKey);
                yield return Frames(14);
            }

            yield return Frames(30);
            var ctx = pg.Session!.Context!;
            var elite = ctx.Dummies.First(d => d.Key.StartsWith("elite", StringComparison.Ordinal)).Value;
            var hits = director.HitLog.Where(h => h.Target.Equals(elite) && h.Source.Equals(ctx.PlayerId)).ToList();
            Assert.GreaterOrEqual(hits.Count, 3, "三连击应打出至少三次命中，实际 " + hits.Count);

            // 伤害数字：数值与独立于呈现的逻辑记录（damage 事件）逐个相等；数字数 = 命中数。
            var damageEvents = pg.Session.Recording.Events.Where(e => e.Kind == "damage" && e.Source == "player").ToList();
            Assert.GreaterOrEqual(damageEvents.Count, hits.Count);
            Assert.AreEqual(director.HitLog.Count, director.Model.NumbersSpawned, "每次命中确认一个飘字");
            for (var i = 0; i < hits.Count; i++)
            {
                Assert.AreEqual(damageEvents[i].Amount, hits[i].Amount, 1e-9, "第 " + (i + 1) + " 个飘字数值 = 逻辑伤害");
            }

            // 受击动画：精英的动画切换里出现受击剪辑。
            var transitions = stage.Record.ClipTransitions.Where(t => t.Contains("show_brute") && t.Contains("hit")).ToList();
            Assert.Greater(transitions.Count, 0, "精英应切到受击剪辑；全部切换：" + string.Join(" | ", stage.Record.ClipTransitions.Take(40)));

            // 顿帧：命中事件里的攻击方顿帧 tick 等于预设行折算值；引擎侧确实开了冻结。
            var expected = ExpectedAttackerTicks(pg.Model.Preset);
            Assert.AreEqual(expected, hits[0].AttackerHitStopTicks, "攻击方顿帧 tick 由预设行算出");
            Assert.Greater(stage.Record.Freezes.Count, 0, "命中后引擎侧应有冻结记录");
            Assert.GreaterOrEqual(stage.Record.Freezes.Max(f => f.Ticks), Math.Min(hits[0].AttackerHitStopTicks, hits[0].TargetHitStopTicks), "冻结时长覆盖顿帧 tick");

            // 闪白与特效。
            Assert.Greater(stage.FlashesApplied, flashBefore, "命中应触发闪白");
            Assert.GreaterOrEqual(director.Model.SparksSpawned, hits.Count, "每次命中一个火花");
            Assert.GreaterOrEqual(director.Model.SlashesSpawned, 1, "出手关键帧生成挥砍拖影");
            Assert.AreEqual(0, stage.Record.Errors.Count, string.Join(" | ", stage.Record.Errors));
        }

        // ───────── 逻辑不变：与原试玩场景逐字节一致 ─────────

        private IEnumerator Scripted()
        {
            var pg = _pg!;
            pg.SpawnDummy("stake");
            pg.SpawnDummy("elite");
            yield return Frames(6);
            _input!.Press("d");
            yield return Frames(12);
            _input.Release("d");
            yield return Tap(AttackKey);
            yield return Frames(16);
            yield return Tap(AttackKey);
            yield return Frames(16);
            yield return Tap(AttackKey);
            yield return Frames(40);
            pg.EliteSwing();
            yield return Frames(50);
            _input.Press("k");
            yield return Frames(3);
            _input.Release("k");
            yield return Frames(30);
        }

        private static string LogicOf(LabPlayground pg)
        {
            var script = pg.Session!.Script;
            pg.End();
            var recording = pg.FinalRecording!;
            return pg.Host!.Runner.FingerprintOf(script, "2d_action", recording).Project(pg.Host.HeadlessRunner.Registry, MetricClass.Logic);
        }

        [UnityTest]
        public IEnumerator LogicFingerprint_IsByteIdentical_BetweenShowcaseAndOriginalPlayground()
        {
            NewPlayground(false);
            yield return Scripted();
            var original = LogicOf(_pg!);
            Dispose();

            NewPlayground(true);
            yield return Scripted();
            var showcase = LogicOf(_pg!);
            Assert.IsTrue(_pg!.Session!.Script.Meta.ExtraDataRoots.Contains(LabPlayground.ShowcaseDataRoot), "演示场景带上了演示数据根");
            Assert.Greater(original.Length, 200, "指纹不是空的");
            Assert.AreEqual(original, showcase, "同一段输入，演示场景与原试玩场景的逻辑指纹必须逐字节一致");
        }

        // ───────── 原试玩场景不受影响；调试面板缺省收起 ─────────

        [UnityTest]
        public IEnumerator OriginalPlayground_IsUnchanged_NoDirectorNoHud_PlaceholderSprites_PanelVisible()
        {
            var pg = NewPlayground(false);
            pg.SpawnDummy("stake");
            yield return Frames(40);
            Assert.IsNull(pg.Stage!.Showcase);
            Assert.IsNull(pg.Hud);
            Assert.IsTrue(pg.Model.PanelVisible, "原试玩场景面板缺省展开");
            var sprites = pg.Stage.RenderedSprites();
            Assert.Greater(sprites.Count, 0);
            Assert.IsTrue(sprites.All(s => !s.Value.Contains("show_")), "原试玩场景不画演示美术");
        }

        [UnityTest]
        public IEnumerator Showcase_PanelCollapsedByDefault_AndHudBuilt()
        {
            var pg = NewPlayground(true);
            yield return Frames(4);
            Assert.IsFalse(pg.Model.PanelVisible, "演示场景调试面板缺省收起");
            Assert.IsNotNull(pg.Hud);
            Assert.IsTrue(pg.Hud!.Built);
            var m = pg.Stage!.Showcase!.Model;
            Assert.AreEqual(4, m.Slots.Length);
            Assert.IsTrue(m.StaminaIsPresentationOnly);
            Assert.AreEqual(m.PlayerMaxHp, m.PlayerHp, 1e-9, "开局满血");
        }

        // ───────── 可选：演示场景截图（GF_LAB_SCREENSHOT_DIR 指定目录才跑；要有窗口的编辑器，批处理下不渲染屏幕；门禁不设）─────────

        private IEnumerator Shot(string dir, string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return null;
            yield return null;
        }

        /// <summary>按住 <paramref name="key"/> 逐帧推进，直到命中日志长度超过 <paramref name="count"/> 或 maxFrames 帧；返回是否等到。</summary>
        private IEnumerator UntilHits(ShowcaseDirector director, int count, int maxFrames, Action<bool> done)
        {
            for (var i = 0; i < maxFrames; i++)
            {
                if (director.HitLog.Count > count)
                {
                    done(true);
                    yield break;
                }

                yield return Frames(1);
            }

            done(director.HitLog.Count > count);
        }

        [UnityTest]
        public IEnumerator Screenshots_WhenRequested()
        {
            var dir = Environment.GetEnvironmentVariable("GF_LAB_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(dir) || Application.isBatchMode)
            {
                Assert.Pass("未设置 GF_LAB_SCREENSHOT_DIR（或批处理模式），跳过截图。");
            }

            Directory.CreateDirectory(dir!);
            var pg = NewPlayground(true);
            var director = pg.Stage!.Showcase!;

            // 一、精英单挑：全景、三连击第一下与第三下的命中瞬间。
            pg.SpawnDummy("elite");
            yield return Frames(60);
            yield return Shot(dir!, "01_overall_idle");
            var n0 = director.HitLog.Count;
            _input!.Press(AttackKey);
            var ok = false;
            yield return UntilHits(director, n0, 90, r => ok = r);
            _input.Release(AttackKey);
            Assert.IsTrue(ok, "第一下应命中");
            yield return Frames(3);
            yield return Shot(dir!, "02a_combo_hit1_impact");
            yield return Frames(24);
            yield return Tap(AttackKey);
            yield return UntilHits(director, n0 + 1, 40, r => ok = r);
            yield return Frames(6);
            yield return Tap(AttackKey);
            yield return UntilHits(director, n0 + 2, 40, r => ok = r);
            Assert.IsTrue(ok, "第三下应命中");
            yield return Frames(3);
            yield return Shot(dir!, "02b_combo_hit3_impact");
            yield return Frames(30);
            yield return Shot(dir!, "03_combat_aftermath");

            // 二、一群小怪 + 木桩：重击击退、蓄力击倒。
            pg.ClearDummies();
            pg.SetWeapon("feel.weapon.tpl_heavy_greatsword");
            pg.SetArchetype("feel.archetype.heavy");
            pg.SpawnDummy("mob", 3);
            yield return Frames(50);
            yield return Shot(dir!, "04_pack_overall");
            // 蓄力重击：等到击退/击倒命中，之后 +2 / +8 / +18 帧各一张。
            var seen = director.HitLog.Count;
            _input!.Press("u");
            yield return Frames(70);
            _input.Release("u");
            for (var i = 0; i < 150 && !director.HitLog.Skip(seen).Any(h => h.Reaction == "Knockback" || h.Reaction == "Knockdown"); i++)
            {
                yield return Frames(1);
            }

            var kd = director.HitLog.Skip(seen).FirstOrDefault(h => h.Reaction == "Knockback" || h.Reaction == "Knockdown");
            Debug.Log("[ShowcaseTests] knock hit: " + (kd == null ? "none" : kd.Reaction + "/" + kd.ImpactClass + " kill=" + kd.IsKill + " amount=" + kd.Amount));
            var lastHero = string.Empty;
            var lastSprite = string.Empty;
            var sampled = 0;
            foreach (var (frames, name) in new[] { (2, "05a_knock_plus2"), (6, "05b_knock_plus8"), (10, "05c_knock_plus18"), (12, "05d_knock_plus30"), (20, "05e_knock_plus50"), (30, "05f_knock_plus80"), (30, "05g_knock_plus110"), (40, "05h_knock_plus150") })
            {
                for (var f = 0; f < frames; f++)
                {
                    yield return Frames(1);
                    var heroNow = pg.Stage!.RenderedSprites().FirstOrDefault(r => r.Key.Equals(pg.Session!.Context!.PlayerId)).Value ?? "none";
                    if (heroNow != lastHero)
                    {
                        Debug.Log("[ShowcaseTests] hero sprite: " + heroNow);
                        lastHero = heroNow;
                    }

                    if (kd != null)
                    {
                        var cur = pg.Stage!.RenderedSprites().FirstOrDefault(r => r.Key.Equals(kd.Target)).Value ?? "none";
                        if (cur != lastSprite)
                        {
                            Debug.Log("[ShowcaseTests] sprite change at +" + (++sampled) + "f: " + cur);
                            lastSprite = cur;
                        }
                        else
                        {
                            sampled++;
                        }
                    }
                }

                yield return Shot(dir!, name);
                if (kd != null)
                {
                    var spr = string.Join(" + ", pg.Stage!.RenderedSprites().Where(r => r.Key.Equals(kd.Target)).Select(r => r.Value));
                    var at = director.PositionOf(kd.Target);
                    Debug.Log("[ShowcaseTests] " + name + " target sprite=" + spr + " pos=" + (at.HasValue ? at.Value.ToString() : "gone"));
                }
            }

            yield return Frames(30);
            yield return Shot(dir!, "07_hud_full");

            // F1 展开 + F12 调参页，叠在演示场景上。
            pg.Model.PanelVisible = true;
            pg.SetTab(LabTab.Tuning);
            yield return Frames(12);
            yield return Shot(dir!, "08_f12_tuning_over_showcase");
            pg.SetTab(LabTab.Scene);
            yield return Frames(12);
            yield return Shot(dir!, "09_f1_scene_panel_over_showcase");
            pg.Model.PanelVisible = false;
            Debug.Log("[ShowcaseTests] screenshots done: hits=" + director.HitLog.Count + " reactions=" + string.Join(",", director.HitLog.Select(h => h.Reaction + "/" + h.ImpactClass).Distinct()) + " weapons=" + string.Join(",", pg.Model.Weapons.Select(w => w.Id)) + " archetypes=" + string.Join(",", pg.Model.Archetypes.Select(a => a.Id)));
        }

        // ───────── 受击反应驱动动画：击退/击倒命中后目标播对应剪辑（修复前舞台没交付反应查询，只会播基础受击剪辑）─────────

        private IEnumerator HeavyScript_ThenCollectClips(bool showcase, Action<List<string>, string> done)
        {
            var pg = NewPlayground(showcase);
            pg.SetWeapon("feel.weapon.tpl_heavy_greatsword");
            pg.SetArchetype("feel.archetype.heavy");
            pg.SpawnDummy("mob", 3);
            yield return Frames(40);
            var seen = pg.Stage!.Showcase != null ? pg.Stage.Showcase.HitLog.Count : 0;
            _input!.Press("u");
            yield return Frames(70);
            _input.Release("u");
            yield return Frames(260);
            var hits = pg.Session!.Recording.Events.Where(e => e.Kind == "damage" && e.Source == "player").Count();
            done(pg.Stage.Record.ClipTransitions.ToList(), "命中（damage 事件）" + hits + " 次，起点 " + seen);
        }

        [UnityTest]
        public IEnumerator KnockReaction_Showcase_PlaysKnockbackOrKnockdownClip_NotJustBaseHit()
        {
            List<string> clips = new List<string>();
            var note = string.Empty;
            yield return HeavyScript_ThenCollectClips(true, (c, n) => { clips = c; note = n; });
            Assert.Greater(clips.Count(t => t.Contains("show_grunt.hit.knock")), 0, "演示场景目标应切到击退/击倒姿势剪辑（" + note + "）；全部切换：" + string.Join(" | ", clips.Take(60)));
        }

        [UnityTest]
        public IEnumerator KnockReaction_OriginalPlayground_AlsoSelectsKnockClip_SameAsProduction()
        {
            List<string> clips = new List<string>();
            var note = string.Empty;
            yield return HeavyScript_ThenCollectClips(false, (c, n) => { clips = c; note = n; });
            Assert.Greater(clips.Count(t => t.IndexOf("knock", StringComparison.OrdinalIgnoreCase) >= 0), 0, "原试玩场景在同一重击脚本下也应选到 knock* 类剪辑（" + note + "）；全部切换：" + string.Join(" | ", clips.Take(60)));
        }

        // ───────── 头顶名字走本地化显示名；连续命中的飘字分道错开 ─────────

        [UnityTest]
        public IEnumerator EnemyOverheadNames_UseLocalizedDisplayName_NotRawIds()
        {
            var pg = NewPlayground(true);
            var director = pg.Stage!.Showcase!;
            pg.SpawnDummy("elite");
            pg.SpawnDummy("stake");
            yield return Frames(30);
            var ctx = pg.Session!.Context!;
            var named = 0;
            foreach (var pair in ctx.Labels)
            {
                if (pair.Key.Equals(ctx.PlayerId))
                {
                    continue;
                }

                var name = director.NameOf(pair.Key);
                Assert.IsFalse(name.Contains("@"), "显示名不应是出场标签（含 @ 序号）：" + name);
                named++;
            }

            Assert.GreaterOrEqual(named, 2, "至少两只靶子取到了显示名");
        }

        [UnityTest]
        public IEnumerator DamageNumbers_OnSameTarget_AreSpreadAcrossLanes()
        {
            var pg = NewPlayground(true);
            var director = pg.Stage!.Showcase!;
            pg.SpawnDummy("elite");
            yield return Frames(40);
            var n0 = director.HitLog.Count;
            yield return Tap(AttackKey);
            yield return Frames(16);
            yield return Tap(AttackKey);
            yield return Frames(16);
            yield return Tap(AttackKey);
            for (var i = 0; i < 60 && director.HitLog.Count - n0 < 3; i++)
            {
                yield return Frames(1);
            }

            Assert.GreaterOrEqual(director.HitLog.Count - n0, 3, "三连击至少三次命中");
            var live = director.Model.Numbers.Where(n => n.Target.Equals(director.HitLog.Last().Target)).OrderBy(n => n.Tick).ToList();
            Assert.GreaterOrEqual(live.Count, 2, "三连击间隔内应有多条同目标飘字同时在场");
            Assert.AreEqual(live.Select(n => n.Lane).Distinct().Count(), live.Count, "同时在场的同目标飘字道次互不相同");
        }

        // ───────── 敌人面朝玩家（表现层）：玩家分别在敌人左/右/上/下时，引擎视图的朝向与镜像由位置算出 ─────────

        private void AssertFacesPlayer(LabPlayground pg, Id enemy, string where)
        {
            var ctx = pg.Session!.Context!;
            var director = pg.Stage!.Showcase!;
            var ep = ctx.World.World.GetEntity(enemy)!.Position;
            var pp = ctx.World.World.GetEntity(ctx.PlayerId)!.Position;
            var angle = Math.Atan2(pp.Y - ep.Y, pp.X - ep.X);
            Assert.IsTrue(director.TryGetForwardedFacing(enemy, out var facing), where + "：应有交给引擎视图的朝向");
            var count = facing.DirectionCount;
            // 期望：指向玩家的角度，加框架朝向约定的半圈偏移（见 ShowcaseDirector.FacingFor），按逻辑朝向的档位数量化。
            var expected = count > 0 ? global::Presentation.Common.Direction.FromQuantized(angle + Math.PI, count) : global::Presentation.Common.Direction.Continuous(angle + Math.PI);
            Assert.AreEqual(expected.Index, facing.Index, where + "：朝向档位 = 由敌人与玩家位置算出的档位（玩家在 " + pp + "，敌人在 " + ep + "）");
            Assert.AreEqual(expected.RawRadians, facing.RawRadians, 1e-6, where + "：朝向角度 = 指向玩家的角度（含约定偏移）");

            // 选用的方向视图与镜像：左右为侧面视图（玩家在敌人左侧时水平翻转，右侧不翻），上下为背面/正面（+Y 朝屏幕上方：玩家在上方 → 背面）。
            var shown = pg.Stage.DisplayedDirectionOf(enemy);
            Assert.IsTrue(shown.HasValue, where + "：敌人应是精灵视图");
            var dx = pp.X - ep.X;
            var dy = pp.Y - ep.Y;
            if (Math.Abs(dx) > 2.0 * Math.Abs(dy))
            {
                Assert.AreEqual("dir.side_r", shown!.Value.Slot, where + "：左右两侧取同一套侧面视图（镜像复用）");
                Assert.AreEqual(dx < 0, shown.Value.FlipX, where + "：玩家在左侧时水平翻转、在右侧时不翻");
                var views = pg.Stage.RenderedSpriteFlips().Where(v => v.Entity.Equals(enemy) && v.Sprite.StartsWith("sprite_anim.", StringComparison.Ordinal)).ToList();
                Assert.Greater(views.Count, 0, where + "：敌人应有动画剪辑精灵");
                Assert.IsTrue(views.All(v => v.FlipX == (dx < 0)), where + "：渲染出来的翻转应为 " + (dx < 0) + "；实际 " + string.Join(",", views.Select(v => v.Sprite + ":" + v.FlipX)));
                // 默认朝向（侧面）的剪辑走无方向入口（它是侧面视图的逐字节拷贝，框架只在方向槽位变化时才换入带方向后缀的剪辑），所以这里不要求名字带 __side_r。
            }
            else if (Math.Abs(dy) > 2.0 * Math.Abs(dx))
            {
                Assert.AreEqual(dy > 0 ? "dir.back" : "dir.front", shown!.Value.Slot, where + "：玩家在上方取背面、下方取正面");
            }
        }

        [UnityTest]
        public IEnumerator Enemies_FacePlayer_FromLeftRightUpAndDown_ViewDirectionAndMirrorFollowPositions()
        {
            var pg = NewPlayground(true);
            pg.SpawnDummy("frail");
            yield return Frames(120);
            var ctx = pg.Session!.Context!;
            var first = ctx.Dummies.First(d => d.Key.StartsWith("frail", StringComparison.Ordinal)).Value;
            // 玩家在敌人左边（初始布置：靶子在玩家前方右侧）。
            AssertFacesPlayer(pg, first, "玩家在敌人左侧");

            // 向上走：玩家移到敌人上方（y 方向）。
            _input!.Press("w");
            yield return Frames(60);
            _input.Release("w");
            yield return Frames(10);
            AssertFacesPlayer(pg, first, "玩家向上走后");
            var up = ctx.World.World.GetEntity(ctx.PlayerId)!.Position.Y;

            // 向下走过头：玩家移到敌人下方。
            _input.Press("s");
            yield return Frames(140);
            _input.Release("s");
            yield return Frames(10);
            AssertFacesPlayer(pg, first, "玩家向下走后");
            var down = ctx.World.World.GetEntity(ctx.PlayerId)!.Position.Y;
            Assert.AreNotEqual(up, down, "玩家确实上下移动过（上、下两次位置不同）");

            // 向左走并朝左：再在左边出一只靶子，则玩家在这只靶子的右边。
            _input.Press("a");
            yield return Frames(30);
            _input.Release("a");
            pg.SpawnDummy("frail");
            yield return Frames(40);
            var second = ctx.Dummies.Where(d => d.Key.StartsWith("frail", StringComparison.Ordinal)).Select(d => d.Value).First(v => !v.Equals(first));
            AssertFacesPlayer(pg, second, "玩家在敌人右侧");
        }

        [UnityTest]
        public IEnumerator HitEnemy_FacesAttacker_WhileBeingKnockedBack()
        {
            var pg = NewPlayground(true);
            pg.SetWeapon("feel.weapon.tpl_heavy_greatsword");
            pg.SetArchetype("feel.archetype.heavy");
            pg.SpawnDummy("mob", 3);
            yield return Frames(40);
            var director = pg.Stage!.Showcase!;
            var seen = director.HitLog.Count;
            _input!.Press("u");
            yield return Frames(70);
            _input.Release("u");
            for (var i = 0; i < 150 && !director.HitLog.Skip(seen).Any(h => h.Reaction == "Knockback" || h.Reaction == "Knockdown"); i++)
            {
                yield return Frames(1);
            }

            var knock = director.HitLog.Skip(seen).First(h => h.Reaction == "Knockback" || h.Reaction == "Knockdown");
            yield return Frames(20);
            AssertFacesPlayer(pg, knock.Target, "被击退/击倒的敌人");
            Assert.IsTrue(director.FacingFocusOf(knock.Target).HasValue, "被打的敌人有朝向焦点");
        }

        /// <summary>双影回归（ADR-0154 决策 10）：任何实体任何时刻只渲染一张整身动画图，不得再垫一张站姿静态层
        /// （此前 body 静态层垫在倒地剪辑下面，画面里一个站着、一个趴着）。</summary>
        [UnityTest]
        public IEnumerator EveryEntity_RendersExactlyOneBodySprite_AlsoWhileKnockedDown()
        {
            var pg = NewPlayground(true);
            pg.SetWeapon("feel.weapon.tpl_heavy_greatsword");
            pg.SetArchetype("feel.archetype.heavy");
            pg.SpawnDummy("mob", 3);
            pg.SpawnDummy("stake");
            yield return Frames(40);
            var director = pg.Stage!.Showcase!;
            var seen = director.HitLog.Count;
            _input!.Press("u");
            yield return Frames(70);
            _input.Release("u");
            var sawKnockClip = false;
            for (var i = 0; i < 220; i++)
            {
                yield return Frames(1);
                var perEntity = pg.Stage!.RenderedSprites().GroupBy(kv => kv.Key.Value).ToList();
                Assert.Greater(perEntity.Count, 0, "有可见实体");
                foreach (var g in perEntity)
                {
                    var names = string.Join(" + ", g.Select(kv => kv.Value));
                    Assert.AreEqual(1, g.Count(), "实体 " + g.Key + " 同时渲染了多张图（双影）: " + names);
                    Assert.IsFalse(names.StartsWith("layer."), "实体 " + g.Key + " 渲染了静态层而不是动画帧: " + names);
                    if (names.Contains("knock")) sawKnockClip = true;
                }
            }

            Assert.IsTrue(sawKnockClip || director.HitLog.Skip(seen).Any(), "重击后应出现击退/击倒剪辑或命中");
        }

        // ───────── 舞台相机不被宿主相机盖掉（顺带修复）─────────

        [UnityTest]
        public IEnumerator StageLayer_IsExcludedFromEveryOtherCamera_IncludingHostCamera()
        {
            foreach (var showcase in new[] { false, true })
            {
                var pg = NewPlayground(showcase);
                yield return Frames(4);
                var layer = new EngineLabOptions().IsolationLayer;
                var stageCamera = pg.Stage!.StageCamera!;
                var others = Camera.allCameras.Where(c => c != stageCamera).ToList();
                Assert.Greater(others.Count, 0, "宿主相机应存在");
                foreach (var c in others)
                {
                    Assert.AreEqual(0, c.cullingMask & (1 << layer), "相机 " + c.name + " 的剔除遮罩还带着舞台隔离层：它会把舞台相机的画面盖掉");
                }

                Assert.AreEqual(2.8, stageCamera.orthographicSize, 1e-6, "演示/试玩缺省取景半高");
                Dispose();
            }
        }

        [UnityTest]
        public IEnumerator Hud_SkillSlotSweeps_AndComboCounts()
        {
            var pg = NewPlayground(true);
            pg.SpawnDummy("stake");
            yield return Frames(30);
            var m = pg.Stage!.Showcase!.Model;
            yield return Tap(AttackKey);
            Assert.Greater(m.Slots[0].LockRemaining, 0.0, "出手后攻击格进入动作锁扫光");
            yield return Frames(14);
            yield return Tap(AttackKey);
            yield return Frames(14);
            Assert.GreaterOrEqual(m.Combo, 2, "连续命中累计连击数");
            yield return Frames(200);
            Assert.AreEqual(0, m.Combo, "超过连击窗口清零");
        }
    }
}
