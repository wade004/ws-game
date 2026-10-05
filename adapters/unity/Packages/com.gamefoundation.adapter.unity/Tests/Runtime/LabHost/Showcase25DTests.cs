#nullable enable
// Showcase25DTests：2.5D 真实美术手感演示场景（ADR-0157）的运行时验收。
// 约束与 ShowcaseTests 相同——演示场景只改呈现：同一段输入在 2.5D 演示场景与原 2.5D 试玩场景上逻辑指纹逐字节一致；
// 这里另外守住 2.5D 才有的东西：直立广告牌（朝向随俯角相机）、地面/阴影仍躺在地上、脚底落在地面点、角色与每种敌人都画出演示美术（按加载器解出的资源引用核对）、
// 以及两个演示场景（2D、2.5D）的 F1 面板与原试玩场景提供同一套调参功能（武器/体型/模板预设/场景控制/各页）。
// 期望值都由数据与规则算出，不写裸数。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.LabHost;
using Core.Foundation.Common;
using Lab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rect = UnityEngine.Rect;

namespace Adapter.Unity.Tests.LabHost
{
    [Category("module:lab")]
    public sealed class Showcase25DTests
    {
        private const double Frame = 1.0 / 60.0;
        private const string AttackKey = "j";
        private const string Cell25 = "2_5d_action";
        private const string Cell2D = "2d_action";
        private GameObject? _go;
        private LabPlayground? _pg;
        private Adapters.Stub.StubInput? _input;
        private string _saveDir = string.Empty;
        private readonly List<string> _warnings = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "lab_showcase25_test_" + Guid.NewGuid().ToString("N"));
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

        private LabPlayground NewPlayground(string cell, bool showcase)
        {
            _go = new GameObject("Showcase25Test");
            var pg = _go.AddComponent<LabPlayground>();
            pg.Configure(cell);
            pg.Showcase = showcase;
            pg.ManualDrive = true;
            pg.FlashIntensitySource = () => 1.0;   // 不读本机设置文件：用例给固定的玩家闪白强度
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

        // ───────── 逻辑不变：与原 2.5D 试玩场景逐字节一致 ─────────

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

        private static string LogicOf(LabPlayground pg, string cell)
        {
            var script = pg.Session!.Script;
            pg.End();
            var recording = pg.FinalRecording!;
            return pg.Host!.Runner.FingerprintOf(script, cell, recording).Project(pg.Host.HeadlessRunner.Registry, MetricClass.Logic);
        }

        [UnityTest]
        public IEnumerator LogicFingerprint_IsByteIdentical_BetweenShowcase25DAndOriginalPlayground25D()
        {
            NewPlayground(Cell25, false);
            yield return Scripted();
            var original = LogicOf(_pg!, Cell25);
            Dispose();

            NewPlayground(Cell25, true);
            yield return Scripted();
            var showcase = LogicOf(_pg!, Cell25);
            Assert.IsTrue(_pg!.Session!.Script.Meta.ExtraDataRoots.Contains(LabPlayground.ShowcaseDataRoot), "演示场景带上了演示数据根");
            Assert.Greater(original.Length, 200, "指纹不是空的");
            Assert.AreEqual(original, showcase, "同一段输入，2.5D 演示场景与原 2.5D 试玩场景的逻辑指纹必须逐字节一致");
        }

        // ───────── 2.5D 管线：固定俯角透视相机 + 直立广告牌 ─────────

        [UnityTest]
        public IEnumerator Showcase25D_UsesFixedPitchPerspectiveCamera_AndUprightBillboards()
        {
            var pg = NewPlayground(Cell25, true);
            pg.SpawnDummy("elite");
            pg.SpawnDummy("stake");
            yield return Frames(60);
            var stage = pg.Stage!;
            var director = stage.Showcase!;
            var cam = stage.StageCamera!;
            var unityCam = stage.StageUnityCamera!;
            Assert.IsTrue(director.Upright, "2.5D 演示场景是直立广告牌模式");
            Assert.IsFalse(cam.orthographic, "固定俯角透视相机");
            Assert.Greater(unityCam.EffectivePitchDegrees, 1.0, "相机带俯角");

            var ctx = pg.Session!.Context!;
            AssertUnitsUpright(pg, ctx);

            Assert.Greater(director.UprightPropCount, 0, "场景道具是直立广告牌");
            foreach (var prop in director.UprightProps)
            {
                Assert.Less(Quaternion.Angle(prop.rotation, cam.transform.rotation), 0.01f, "道具与相机平面平行");
            }

            // 地面砖躺在地上（不随相机立起），用三线性过滤（透视下近大远小）。
            var ground = cam.transform.parent.Find("ShowcaseGround");
            Assert.IsNotNull(ground, "地面砖存在");
            Assert.Less(Quaternion.Angle(ground!.rotation, Quaternion.identity), 0.01f, "地面砖躺在世界平面上");
            var floor = ground.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(floor);
            Assert.IsNotNull(floor.sprite, "地面砖美术已加载");
            Assert.AreEqual(FilterMode.Trilinear, floor.sprite.texture.filterMode, "2.5D 地面砖用三线性过滤");
            Assert.AreEqual(0, stage.Record.Errors.Count, string.Join(" | ", stage.Record.Errors));
        }

        /// <summary>每个精灵单位：整身渲染根与相机平行（直立）、枢轴落在单位的地面点上（脚底锚定）、影子仍躺在地面。</summary>
        private void AssertUnitsUpright(LabPlayground pg, LabHostContext ctx)
        {
            var stage = pg.Stage!;
            var cam = stage.StageCamera!;
            var checkedUnits = 0;
            foreach (var pair in ctx.Labels)
            {
                var entity = pair.Key;
                var e = ctx.World.World.GetEntity(entity);
                var layers = stage.LayersRootOf(entity);
                if (e == null || layers == null)
                {
                    continue;
                }

                checkedUnits++;
                Assert.Less(Quaternion.Angle(layers.rotation, cam.transform.rotation), 0.01f, pair.Value + "：渲染根与相机平面平行（直立广告牌）");
                var ground = new Vector3((float)e.Position.X, (float)e.Position.Y, 0f);
                var screenGround = cam.WorldToScreenPoint(ground);
                var screenPivot = cam.WorldToScreenPoint(layers.position);
                Assert.AreEqual(screenGround.x, screenPivot.x, 1.5f, pair.Value + "：枢轴在地面点的屏幕位置（x）");
                Assert.AreEqual(screenGround.y, screenPivot.y, 1.5f, pair.Value + "：枢轴在地面点的屏幕位置（y）：脚底落在地面");

                var shadow = layers.parent.Find("Shadow");
                if (shadow != null)
                {
                    Assert.Less(Quaternion.Angle(shadow.rotation, Quaternion.identity), 0.01f, pair.Value + "：影子躺在地面");
                }

                // 精灵本体：枢轴在脚底一侧（不是画布中心）、画面在地面点上方，不陷进地里。
                var body = layers.GetComponentsInChildren<SpriteRenderer>(false).FirstOrDefault(r => r.enabled && r.sprite != null);
                if (body != null)
                {
                    var sprite = body.sprite;
                    Assert.Less(sprite.pivot.y / sprite.rect.height, 0.45f, pair.Value + "：枢轴靠近脚底，不是画布中心：" + sprite.name);
                    var topScreen = cam.WorldToScreenPoint(layers.TransformPoint(new Vector3(0f, sprite.bounds.max.y, 0f)));
                    Assert.Greater(topScreen.y, screenPivot.y + 8f, pair.Value + "：精灵画面立在地面点上方");
                }
            }

            Assert.GreaterOrEqual(checkedUnits, 2, "至少玩家与一只靶子被核对");
        }

        [UnityTest]
        public IEnumerator Showcase2D_StaysFlat_NoBillboards_UnchangedPipeline()
        {
            var pg = NewPlayground(Cell2D, true);
            pg.SpawnDummy("elite");
            yield return Frames(40);
            var stage = pg.Stage!;
            Assert.IsFalse(stage.Showcase!.Upright, "2D 演示场景不做广告牌");
            Assert.IsTrue(stage.StageCamera!.orthographic, "2D 演示场景仍是正交俯视");
            Assert.AreEqual(0, stage.Showcase.UprightPropCount);
            Assert.IsNull(stage.LayersRootOf(pg.Session!.Context!.PlayerId), "2D 不改渲染根朝向");
        }

        // ───────── 美术：英雄与每种敌人都画出演示美术，不是占位 ─────────

        [UnityTest]
        public IEnumerator Hero_AndEveryEnemyType_RenderShowcaseArt_ByLoadedResourceIds_NotPlaceholder()
        {
            var pg = NewPlayground(Cell25, true);
            var kinds = pg.Model.DummyKinds.Select(k => k.Id).Where(k => !k.StartsWith("pack_", StringComparison.Ordinal)).ToList();
            foreach (var kind in kinds)
            {
                pg.SpawnDummy(kind);
            }

            yield return Frames(90);
            var stage = pg.Stage!;
            var ctx = pg.Session!.Context!;
            var sprites = stage.RenderedSprites();
            var resolved = stage.ShowcaseDisplay!.Resolved;

            // 玩家：英雄美术。
            var heroSprites = sprites.Where(s => s.Key.Equals(ctx.PlayerId)).Select(s => s.Value).ToList();
            Assert.Greater(heroSprites.Count, 0, "玩家应有渲染的精灵");
            Assert.IsTrue(heroSprites.All(n => n.StartsWith("sprite_anim.show_hero", StringComparison.Ordinal)),
                "玩家画的不是英雄演示美术：" + string.Join(", ", heroSprites) + "\n警告：" + string.Join("\n", _warnings.Take(12)));

            // 每只靶子：按种类词（与导演/外形登记同一条规则）取对应演示美术。
            var seenLooks = new HashSet<string>(StringComparer.Ordinal) { ShowcaseDisplayRegistry.Hero };
            foreach (var pair in ctx.Dummies)
            {
                var entity = pair.Value;
                var look = ShowcaseDisplayRegistry.LookOf(pair.Key);
                seenLooks.Add(look);
                var names = sprites.Where(s => s.Key.Equals(entity)).Select(s => s.Value).ToList();
                Assert.Greater(names.Count, 0, "靶子 " + pair.Key + " 应有渲染的精灵");
                Assert.IsTrue(names.All(n => n.StartsWith("sprite_anim.show_" + look, StringComparison.Ordinal)),
                    "靶子 " + pair.Key + "（" + look + "）画的不是对应演示美术：" + string.Join(", ", names));
            }

            var resolvedLooks = new HashSet<string>(resolved.Values);
            foreach (var look in seenLooks)
            {
                Assert.IsTrue(resolvedLooks.Contains(look), "外形登记应解析出 " + look + "，实际 " + string.Join(",", resolvedLooks));
            }

            foreach (var look in new[] { ShowcaseDisplayRegistry.Hero, ShowcaseDisplayRegistry.Grunt, ShowcaseDisplayRegistry.Brute, ShowcaseDisplayRegistry.Dummy })
            {
                Assert.IsTrue(seenLooks.Contains(look), "场上应出现 " + look + "，实际 " + string.Join(",", seenLooks));
            }

            // 没有任何一张是占位图（占位美术的资源引用不带 show_）。
            var foreign = sprites.Where(s => !s.Value.StartsWith("sprite_anim.show_", StringComparison.Ordinal)).ToList();
            Assert.AreEqual(0, foreign.Count, "回退到占位的精灵：" + string.Join(", ", foreign.Select(f => f.Key.Value + "=" + f.Value)));
            Assert.AreEqual(0, stage.Record.Errors.Count, string.Join(" | ", stage.Record.Errors));
        }

        // ───────── 双影：任何实体任何时刻只渲染一张整身图（含击倒） ─────────

        [UnityTest]
        public IEnumerator Showcase25D_EveryEntity_RendersExactlyOneBodySprite_AlsoWhileKnockedDown()
        {
            var pg = NewPlayground(Cell25, true);
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
            Assert.AreEqual(0, pg.Stage!.Record.Errors.Count, string.Join(" | ", pg.Stage.Record.Errors));
        }

        // ───────── 敌人面朝玩家（2.5D）：玩家分别在敌人左/右/上/下 ─────────

        private void AssertFacesPlayer(LabPlayground pg, Id enemy, string where)
        {
            var ctx = pg.Session!.Context!;
            var director = pg.Stage!.Showcase!;
            var ep = ctx.World.World.GetEntity(enemy)!.Position;
            var pp = ctx.World.World.GetEntity(ctx.PlayerId)!.Position;
            var angle = Math.Atan2(pp.Y - ep.Y, pp.X - ep.X);
            Assert.IsTrue(director.TryGetForwardedFacing(enemy, out var facing), where + "：应有交给引擎视图的朝向");
            var count = facing.DirectionCount;
            var expected = count > 0 ? global::Presentation.Common.Direction.FromQuantized(angle + Math.PI, count) : global::Presentation.Common.Direction.Continuous(angle + Math.PI);
            Assert.AreEqual(expected.Index, facing.Index, where + "：朝向档位 = 由敌人与玩家位置算出的档位（玩家在 " + pp + "，敌人在 " + ep + "）");

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
                Assert.IsTrue(views.All(v => v.FlipX == (dx < 0)), where + "：渲染出来的翻转应为 " + (dx < 0));
            }
            else if (Math.Abs(dy) > 2.0 * Math.Abs(dx))
            {
                Assert.AreEqual(dy > 0 ? "dir.back" : "dir.front", shown!.Value.Slot, where + "：玩家在上方取背面、下方取正面");
            }
        }

        [UnityTest]
        public IEnumerator Showcase25D_Enemies_FacePlayer_FromLeftRightUpAndDown()
        {
            var pg = NewPlayground(Cell25, true);
            pg.SpawnDummy("frail");
            yield return Frames(120);
            var ctx = pg.Session!.Context!;
            var first = ctx.Dummies.First(d => d.Key.StartsWith("frail", StringComparison.Ordinal)).Value;
            AssertFacesPlayer(pg, first, "玩家在敌人左侧");

            _input!.Press("w");
            yield return Frames(60);
            _input.Release("w");
            yield return Frames(10);
            AssertFacesPlayer(pg, first, "玩家向上走后");
            var up = ctx.World.World.GetEntity(ctx.PlayerId)!.Position.Y;

            _input.Press("s");
            yield return Frames(140);
            _input.Release("s");
            yield return Frames(10);
            AssertFacesPlayer(pg, first, "玩家向下走后");
            var down = ctx.World.World.GetEntity(ctx.PlayerId)!.Position.Y;
            Assert.AreNotEqual(up, down, "玩家确实上下移动过");

            _input.Press("a");
            yield return Frames(30);
            _input.Release("a");
            pg.SpawnDummy("frail");
            yield return Frames(40);
            var second = ctx.Dummies.Where(d => d.Key.StartsWith("frail", StringComparison.Ordinal)).Select(d => d.Value).First(v => !v.Equals(first));
            AssertFacesPlayer(pg, second, "玩家在敌人右侧");
        }

        // ───────── 界面：参考皮肤、飘字与血条经俯角相机投影到头顶 ─────────

        [UnityTest]
        public IEnumerator Showcase25D_Hud_UsesReferenceSkin_AndNumbersAndBarsSitAboveTheEnemy()
        {
            var pg = NewPlayground(Cell25, true);
            pg.SpawnDummy("elite");
            yield return Frames(40);
            Assert.IsNotNull(pg.Hud);
            Assert.IsTrue(pg.Hud!.Built);
            Assert.AreEqual(ShowcaseHud.SkinRef, pg.Hud.LoadedSkinRef, "HUD 皮肤引用是参考皮肤");
            Assert.AreEqual("skin.reference_fantasy", pg.Hud.LoadedSkinRef);
            Assert.IsFalse(pg.Hud.UsesPlaceholderSkin, "构建同步应把参考皮肤包放进工作台资源根（缺同步时 HUD 会悄悄退回占位皮肤，这里必须红）");
            Assert.IsFalse(pg.Model.PanelVisible, "调试面板缺省收起");

            var director = pg.Stage!.Showcase!;
            var ctx = pg.Session!.Context!;
            var elite = ctx.Dummies.First(d => d.Key.StartsWith("elite", StringComparison.Ordinal)).Value;
            var n0 = director.HitLog.Count;
            yield return Tap(AttackKey);
            for (var i = 0; i < 60 && director.HitLog.Count <= n0; i++)
            {
                yield return Frames(1);
            }

            Assert.Greater(director.HitLog.Count, n0, "第一下应命中");
            yield return Frames(3);
            Assert.GreaterOrEqual(director.Model.Combo, 1, "连击计数在涨");
            Assert.Greater(director.Model.Numbers.Count, 0, "命中后有飘字");
            Assert.GreaterOrEqual(director.Model.NumbersSpawned, 1);
            Assert.GreaterOrEqual(pg.Hud.VisibleNumbers, 1, "飘字可见");
            Assert.GreaterOrEqual(pg.Hud.VisibleEnemyBars, 1, "被打的敌人头顶血条可见");

            // 2.5D 的头顶点沿相机上轴抬：飘字起点与血条在敌人脚下地面点的屏幕位置之上。
            var cam = pg.Stage.StageCamera!;
            var pos = director.PositionOf(elite)!.Value;
            var feet = cam.WorldToScreenPoint(new Vector3(pos.x, pos.y, 0f));
            var head = cam.WorldToScreenPoint(director.Lift(pos, director.HeadHeightOf(elite)));
            Assert.Greater(head.y, feet.y + 20f, "头顶点在脚下地面点的屏幕位置之上");
            var number = director.Model.Numbers.Last();
            Assert.Greater(number.Height, 0f, "2.5D 飘字的头高放在 Height 里，由界面沿相机上轴抬");
            Assert.AreEqual(director.HeadHeightOf(elite), number.Height, 1e-6, "飘字起点高度 = 目标头高");
            Assert.AreEqual(0, pg.Stage.Record.Errors.Count, string.Join(" | ", pg.Stage.Record.Errors));
        }

        // ───────── F1 面板：两个演示场景与原试玩场景提供同一套调参功能 ─────────

        private static void AssertSameFeatures(LabPlayground reference, LabPlayground other, string where)
        {
            CollectionAssert.AreEqual(reference.Model.Weapons.Select(w => w.Id).ToList(), other.Model.Weapons.Select(w => w.Id).ToList(), where + "：武器（F3）");
            CollectionAssert.AreEqual(reference.Model.Archetypes.Select(a => a.Id).ToList(), other.Model.Archetypes.Select(a => a.Id).ToList(), where + "：体型（F4）");
            CollectionAssert.AreEqual(reference.Model.Presets.Select(p => p.Id).ToList(), other.Model.Presets.Select(p => p.Id).ToList(), where + "：预设与模板");
            CollectionAssert.AreEqual(reference.Model.DummyKinds.Select(k => k.Id).ToList(), other.Model.DummyKinds.Select(k => k.Id).ToList(), where + "：场景控制的靶子种类");
            CollectionAssert.AreEqual(reference.Model.EffectChannels.ToList(), other.Model.EffectChannels.ToList(), where + "：效果开关通道");
            Assert.IsNotNull(reference.Tuning);
            Assert.IsNotNull(other.Tuning, where + "：带调参面板");
            Assert.AreEqual(reference.Tuning!.Fields.Count, other.Tuning!.Fields.Count, where + "：调参字段数");
            Assert.AreEqual(reference.Model.Preset, other.Model.Preset, where + "：缺省预设");
        }

        [UnityTest]
        public IEnumerator Showcase_F1Panel_OffersSamePlaygroundFeatures_InBothCells()
        {
            foreach (var cell in new[] { Cell2D, Cell25 })
            {
                var reference = NewPlayground(cell, false);
                var refSnapshot = Snapshot(reference);
                Dispose();

                var pg = NewPlayground(cell, true);
                yield return Frames(4);
                Assert.IsFalse(pg.Model.PanelVisible, cell + "：演示场景面板缺省收起");
                pg.Model.PanelVisible = true;
                Assert.IsTrue(pg.Model.PanelVisible, cell + "：F1 可展开");
                var snapshot = Snapshot(pg);
                CollectionAssert.AreEqual(refSnapshot, snapshot, cell + "：F1 面板提供的功能与原试玩场景一致");
                Assert.IsTrue(pg.Model.Presets.Any(p => p.Id.StartsWith("feel.preset.tpl_", StringComparison.Ordinal)), cell + "：有 tpl_* 手感模板");
                Assert.IsTrue(pg.Model.Weapons.Count > 1 && pg.Model.Archetypes.Count > 1, cell + "：有武器与体型可选");

                // 五页都能切、画得出来（模型层）。
                foreach (var tab in new[] { LabTab.Scene, LabTab.Tuning, LabTab.Timeline, LabTab.Trajectory, LabTab.Rating })
                {
                    pg.SetTab(tab);
                    yield return Frames(2);
                    Assert.AreEqual(tab, pg.Model.Tab, cell + "：切到 " + tab);
                }

                pg.SetTab(LabTab.Scene);
                Dispose();
            }
        }

        private static List<string> Snapshot(LabPlayground pg)
        {
            var list = new List<string>();
            list.Add("weapons:" + string.Join(",", pg.Model.Weapons.Select(w => w.Id)));
            list.Add("archetypes:" + string.Join(",", pg.Model.Archetypes.Select(a => a.Id)));
            list.Add("presets:" + string.Join(",", pg.Model.Presets.Select(p => p.Id)));
            list.Add("kinds:" + string.Join(",", pg.Model.DummyKinds.Select(k => k.Id)));
            list.Add("channels:" + string.Join(",", pg.Model.EffectChannels));
            list.Add("tuning_fields:" + pg.Tuning!.Fields.Count);
            list.Add("default_preset:" + pg.Model.Preset);
            return list;
        }

        // ───────── 换武器/换模板：真实美术不丢、广告牌随模板俯角转 ─────────

        private IEnumerator SwitchAll(string cell, bool expectUpright)
        {
            var pg = NewPlayground(cell, true);
            pg.SpawnDummy("elite");
            pg.SpawnDummy("frail");
            yield return Frames(60);
            var stage = pg.Stage!;
            var ctx = pg.Session!.Context!;
            var cam = stage.StageCamera!;
            var framed = 0;

            IEnumerator Settle(string what)
            {
                yield return Tap(AttackKey);
                yield return Frames(24);
                var sprites = stage.RenderedSprites();
                var hero = sprites.Where(s => s.Key.Equals(ctx.PlayerId)).Select(s => s.Value).ToList();
                Assert.Greater(hero.Count, 0, what + "：玩家有渲染的精灵");
                Assert.IsTrue(hero.All(n => n.StartsWith("sprite_anim.show_hero", StringComparison.Ordinal)), what + "：玩家仍是英雄演示美术：" + string.Join(", ", hero));
                var foreign = sprites.Where(s => !s.Value.StartsWith("sprite_anim.show_", StringComparison.Ordinal)).ToList();
                Assert.AreEqual(0, foreign.Count, what + "：回退到占位的精灵：" + string.Join(", ", foreign.Select(f => f.Key.Value + "=" + f.Value)));
                Assert.AreEqual(0, stage.Record.Errors.Count, what + "：" + string.Join(" | ", stage.Record.Errors));
                if (expectUpright)
                {
                    var layers = stage.LayersRootOf(ctx.PlayerId);
                    Assert.IsNotNull(layers, what);
                    Assert.Less(Quaternion.Angle(layers!.rotation, cam.transform.rotation), 0.01f, what + "：广告牌与相机平行");
                    foreach (var prop in stage.Showcase!.UprightProps)
                    {
                        Assert.Less(Quaternion.Angle(prop.rotation, cam.transform.rotation), 0.01f, what + "：道具与相机平行");
                    }
                }
            }

            foreach (var weapon in pg.Model.Weapons.Select(w => w.Id).ToList())
            {
                pg.SetWeapon(weapon);
                yield return Settle("武器 " + (weapon.Length == 0 ? "无" : weapon));
            }

            pg.SetWeapon(string.Empty);
            foreach (var archetype in pg.Model.Archetypes.Select(a => a.Id).ToList())
            {
                pg.SetArchetype(archetype);
                yield return Settle("体型 " + (archetype.Length == 0 ? "无" : archetype));
            }

            pg.SetArchetype(string.Empty);
            var pitches = new HashSet<int>();
            foreach (var preset in pg.Model.Presets.Select(p => p.Id).ToList())
            {
                pg.SetPreset(preset);
                yield return Settle("预设 " + preset);
                var framing = stage.TemplateFraming;
                if (preset.StartsWith("feel.preset.tpl_", StringComparison.Ordinal))
                {
                    Assert.IsTrue(framing.Found, preset + "：模板有取景配置");
                    framed++;
                    if (expectUpright)
                    {
                        Assert.AreEqual(framing.PitchDegrees, stage.StageUnityCamera!.EffectivePitchDegrees, 1e-6, preset + "：相机俯角取模板值");
                        pitches.Add((int)Math.Round(framing.PitchDegrees));
                    }
                }
            }

            Assert.Greater(framed, 0, "至少切过一个 tpl_* 模板");
            if (expectUpright)
            {
                Assert.Greater(pitches.Count, 1, "模板的俯角不全相同，广告牌确实跟着转过：" + string.Join(",", pitches));
            }
        }

        [UnityTest]
        public IEnumerator Showcase25D_SwitchWeaponArchetypeAndTemplate_KeepsRealArt_BillboardsFollowPitch()
        {
            yield return SwitchAll(Cell25, true);
        }

        [UnityTest]
        public IEnumerator Showcase2D_SwitchWeaponArchetypeAndTemplate_KeepsRealArt()
        {
            yield return SwitchAll(Cell2D, false);
        }

        // ───────── 待机时没有两只单位的身体精灵叠在一起 ─────────

        // 判断记录：试玩宿主的出靶子规则是"玩家前方固定距离"，连着出两只单个靶子（精英、木桩）会出在同一个逻辑坐标——
        // 那是两个实体共点（每个实体仍只画一个身体），不是一个实体画了两个身体；规则属于逻辑（改它会改逻辑指纹），
        // 所以演示布局的修法放在"怎么出靶子"：出下一只前先让玩家转向另一个方向（走几步）。截图与本类用例都用这个布局。

        private IEnumerator SpawnEliteAndStakeApart()
        {
            var pg = _pg!;
            pg.SpawnDummy("elite");
            yield return Frames(4);
            _input!.Press("w");
            yield return Frames(14);
            _input.Release("w");
            yield return Frames(2);
            pg.SpawnDummy("stake");
            _input.Press("s");
            yield return Frames(14);
            _input.Release("s");
            yield return Frames(2);
            _input.Press("d");
            yield return Frames(2);
            _input.Release("d");
            yield return Frames(2);
        }

        /// <summary>每个单位当前身体精灵的屏幕包围盒（由精灵自身的包围盒四角经渲染根投到屏幕，不写死像素）。</summary>
        private Dictionary<string, Rect> BodyScreenRects(LabPlayground pg)
        {
            var stage = pg.Stage!;
            var cam = stage.StageCamera!;
            var result = new Dictionary<string, Rect>();
            foreach (var pair in pg.Session!.Context!.Labels)
            {
                var layers = stage.LayersRootOf(pair.Key);
                if (layers == null)
                {
                    continue;
                }

                var body = layers.GetComponentsInChildren<SpriteRenderer>(false).FirstOrDefault(r => r.enabled && r.sprite != null);
                if (body == null)
                {
                    continue;
                }

                var b = body.sprite.bounds;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var sx in new[] { b.min.x, b.max.x })
                {
                    foreach (var sy in new[] { b.min.y, b.max.y })
                    {
                        var p = cam.WorldToScreenPoint(body.transform.TransformPoint(new Vector3(sx, sy, 0f)));
                        minX = Mathf.Min(minX, p.x);
                        maxX = Mathf.Max(maxX, p.x);
                        minY = Mathf.Min(minY, p.y);
                        maxY = Mathf.Max(maxY, p.y);
                    }
                }

                result[pair.Value] = Rect.MinMaxRect(minX, minY, maxX, maxY);
            }

            return result;
        }

        private static float OverlapFractionOfSmaller(Rect a, Rect b)
        {
            var w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            var h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            if (w <= 0f || h <= 0f)
            {
                return 0f;
            }

            return w * h / Mathf.Min(a.width * a.height, b.width * b.height);
        }

        private static float WorstOverlap(Dictionary<string, Rect> rects, out string pair)
        {
            var worst = 0f;
            pair = string.Empty;
            var keys = rects.Keys.ToList();
            for (var i = 0; i < keys.Count; i++)
            {
                for (var j = i + 1; j < keys.Count; j++)
                {
                    var f = OverlapFractionOfSmaller(rects[keys[i]], rects[keys[j]]);
                    if (f > worst)
                    {
                        worst = f;
                        pair = keys[i] + " × " + keys[j];
                    }
                }
            }

            return worst;
        }

        private const float MaxIdleOverlapFraction = 0.15f;

        private IEnumerator AssertNoIdleOverlap(string cell)  // 2D 不建单位渲染根（LayersRootOf 为空），所以只在 2.5D 上量
        {
            var pg = NewPlayground(cell, true);
            yield return SpawnEliteAndStakeApart();
            yield return Frames(60);
            var rects = BodyScreenRects(pg);
            Assert.GreaterOrEqual(rects.Count, 3, "玩家 + 精英 + 木桩都有身体精灵：" + string.Join(",", rects.Keys));
            var worst = WorstOverlap(rects, out var pair);
            Assert.LessOrEqual(worst, MaxIdleOverlapFraction, cell + "：待机时两只单位的身体精灵不应叠在一起：" + pair);
        }

        [UnityTest]
        public IEnumerator Showcase25D_IdleLayout_NoTwoBodySpritesOverlap()
        {
            yield return AssertNoIdleOverlap(Cell25);
        }

        [UnityTest]
        public IEnumerator OverlapMetric_CatchesTwoSingleSpawnsAtTheSameLogicalPoint()
        {
            // 对照：不拉开布局，连出两只单个靶子，它们共点（两个实体各一个身体），度量必须报出重叠，证明上面的断言不是空转。
            var pg = NewPlayground(Cell25, true);
            pg.SpawnDummy("elite");
            pg.SpawnDummy("stake");
            yield return Frames(40);
            var rects = BodyScreenRects(pg);
            var worst = WorstOverlap(rects, out var pair);
            Assert.Greater(worst, MaxIdleOverlapFraction, "共点的两个靶子应被度量为重叠");
            StringAssert.Contains("elite", pair);
            StringAssert.Contains("stake", pair);
        }

        // ───────── 场景文件 ─────────

        [Test]
        public void ShowcaseScenes_Exist_ForBothCells_WithShowcaseFlagAndCell()
        {
            foreach (var cell in new[] { Cell2D, Cell25 })
            {
                var path = Path.Combine(Application.dataPath, "Framework", "Scenes", "LabShowcase_" + cell + ".unity");
                Assert.IsTrue(File.Exists(path), "缺演示场景 " + path);
                var text = File.ReadAllText(path);
                Assert.IsTrue(text.Contains("cell: " + cell), cell + "：场景的格子字段");
                Assert.IsTrue(text.Contains("showcase: 1"), cell + "：场景打开了演示场景字段");
            }
        }

        // ───────── 可选：2.5D 演示场景截图（GF_LAB_SCREENSHOT_DIR 指定目录才跑；要有窗口的编辑器，批处理下不渲染屏幕；门禁不设）─────────

        private IEnumerator Shot(string dir, string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return null;
            yield return null;
        }

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

            var cell = Environment.GetEnvironmentVariable("GF_LAB_SCREENSHOT_CELL");
            if (string.IsNullOrEmpty(cell))
            {
                cell = Cell25;
            }

            Directory.CreateDirectory(dir!);
            var pg = NewPlayground(cell!, true);
            var director = pg.Stage!.Showcase!;

            // 一、精英单挑：全景、三连击命中瞬间。
            yield return SpawnEliteAndStakeApart();
            yield return Frames(40);
            yield return Shot(dir!, "01_overall_idle");
            _input!.Press("d");
            yield return Frames(12);
            _input.Release("d");
            var n0 = director.HitLog.Count;
            _input.Press(AttackKey);
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
            yield return Frames(3);
            yield return Shot(dir!, "02b_combo_hit3_impact");
            yield return Frames(30);
            yield return Shot(dir!, "03_combat_aftermath");

            // 二、一群小怪：重击击退、蓄力击倒序列。
            pg.ClearDummies();
            pg.SetWeapon("feel.weapon.tpl_heavy_greatsword");
            pg.SetArchetype("feel.archetype.heavy");
            pg.SpawnDummy("mob", 3);
            yield return Frames(50);
            yield return Shot(dir!, "04_pack_overall");
            var seen = director.HitLog.Count;
            _input.Press("u");
            yield return Frames(70);
            _input.Release("u");
            for (var i = 0; i < 150 && !director.HitLog.Skip(seen).Any(h => h.Reaction == "Knockback" || h.Reaction == "Knockdown"); i++)
            {
                yield return Frames(1);
            }

            foreach (var (frames, name) in new[] { (2, "05a_knock_plus2"), (6, "05b_knock_plus8"), (10, "05c_knock_plus18"), (12, "05d_knock_plus30"), (20, "05e_knock_plus50"), (30, "05f_knock_plus80") })
            {
                yield return Frames(frames);
                yield return Shot(dir!, name);
            }

            yield return Frames(30);
            yield return Shot(dir!, "07_hud_full");

            // 三、F12 调参页叠在演示画面上；再换一个模板看取景（不同俯角）。
            pg.Model.PanelVisible = true;
            pg.SetTab(LabTab.Tuning);
            yield return Frames(12);
            yield return Shot(dir!, "08_f12_tuning_over_showcase");
            pg.SetTab(LabTab.Scene);
            yield return Frames(12);
            yield return Shot(dir!, "09_f1_scene_panel_over_showcase");
            pg.Model.PanelVisible = false;
            pg.SetWeapon(string.Empty);
            pg.SetArchetype(string.Empty);
            pg.SetPreset("feel.preset.tpl_horde");
            yield return Frames(90);
            yield return Shot(dir!, "10_template_horde_framing");
            Debug.Log("[Showcase25DTests] screenshots done: cell=" + cell + " hits=" + director.HitLog.Count);
        }
    }
}
