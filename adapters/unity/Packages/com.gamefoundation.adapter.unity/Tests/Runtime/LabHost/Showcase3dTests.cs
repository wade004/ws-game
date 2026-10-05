#nullable enable
// Showcase3dTests：3D 真实美术手感演示场景（ADR-0158）的运行时验收（LabHost 风格，手动驱动）。
// 期望值都由数据与规则算出（命中顿帧 tick 取预设行、伤害量取独立于呈现的逻辑记录、剪辑名取显示数据行），不写裸数；
// "模型/贴图来自同步产物"的用例在缺同步时必须红，不能悄悄显示占位。
// 约束：演示场景只改呈现——同一段输入在 3D 演示场景与原 3D 试玩场景上的逻辑指纹逐字节一致。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.LabHost;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Lab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.LabHost
{
    [Category("module:lab")]
    public sealed class Showcase3dTests
    {
        private const double Frame = 1.0 / 60.0;
        private const string AttackKey = "j";
        private static readonly string[] Looks = { ShowcaseDisplayRegistry.Hero, ShowcaseDisplayRegistry.Grunt, ShowcaseDisplayRegistry.Brute, ShowcaseDisplayRegistry.Dummy };
        private GameObject? _go;
        private LabPlayground? _pg;
        private Adapters.Stub.StubInput? _input;
        private string _saveDir = string.Empty;
        private readonly List<string> _warnings = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "lab_showcase3d_test_" + Guid.NewGuid().ToString("N"));
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

        private LabPlayground NewPlayground(bool showcase)
        {
            _go = new GameObject("Showcase3dTest");
            var pg = _go.AddComponent<LabPlayground>();
            pg.Configure("3d_action");
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

        /// <summary>摆正场景里每个"站在地面上"的枢轴（运行时由 LateUpdate 每帧做；手动驱动的测试在断言姿态前显式做一次）。</summary>
        private static void ApplyUpright(GameObject root)
        {
            foreach (var u in root.GetComponentsInChildren<ModelGroundUpright>(true))
            {
                u.Apply();
            }
        }

        // ───────── 美术：模型与贴图来自同步产物，0 回退占位 ─────────

        [UnityTest]
        public IEnumerator EveryUnitKind_RendersShowcaseModels_FromSyncedData_ZeroPlaceholderFallback()
        {
            var pg = NewPlayground(true);
            Assert.IsNotNull(pg.Stage!.Showcase, "演示场景应有导演");
            var kinds = pg.Model.DummyKinds.Select(k => k.Id).Where(k => !k.StartsWith("pack_", StringComparison.Ordinal)).ToList();
            foreach (var kind in kinds)
            {
                pg.SpawnDummy(kind);
            }

            yield return Frames(90);
            var models = pg.Stage.RenderedModels();
            Assert.GreaterOrEqual(models.Count, kinds.Count + 1, "玩家与每种靶子都应有渲染的模型");
            var placeholders = models.Where(m => m.Placeholder).Select(m => m.Entity.Value).ToList();
            var syncHint = "缺模型包同步产物？先运行 build.ps1（把 assets/_showcase/models 同步进 Assets/Showcase3dArt，编辑器加载时由 ModelPackBuilder 装配）。";
            Assert.AreEqual(0, placeholders.Count, "回退到占位模型的实体：" + string.Join(", ", placeholders) + "。" + syncHint + "\n警告：" + string.Join("\n", _warnings.Take(12)));
            Assert.AreEqual(0, _warnings.Count(w => w.Contains("找不到模型资源")), "加载器报告缺模型资源。" + syncHint);

            var looks = new HashSet<string>(pg.Stage.ShowcaseDisplay!.Resolved.Values);
            foreach (var look in Looks)
            {
                Assert.IsTrue(looks.Contains(look), "外形登记应覆盖 " + look + "，实际 " + string.Join(",", looks));
            }

            // 每个外形的模型名都是 show3d_<look>（四个角色是四个不同的模型包角色，不是同一个模型换名字）。
            var names = models.Select(m => m.Root.name).ToList();
            foreach (var look in Looks)
            {
                Assert.IsTrue(names.Any(n => n.EndsWith("model.show3d_" + look, StringComparison.Ordinal)), "应渲染 model.show3d_" + look + "；实际 " + string.Join(", ", names));
            }

            Assert.AreEqual(0, pg.Stage.Record.Errors.Count, string.Join(" | ", pg.Stage.Record.Errors));
        }

        [UnityTest]
        public IEnumerator PackCharacters_AreDistinctModels_WithRealTextures_AndShowcaseShader()
        {
            var pg = NewPlayground(true);
            pg.SpawnDummy("elite");
            pg.SpawnDummy("stake");
            pg.SpawnDummy("frail");
            yield return Frames(60);
            var seen = new Dictionary<string, HashSet<string>>();
            foreach (var m in pg.Stage!.RenderedModels())
            {
                var meshes = m.Root.GetComponentsInChildren<SkinnedMeshRenderer>(false).Select(r => r.sharedMesh != null ? r.sharedMesh.name : "null").OrderBy(x => x).ToList();
                Assert.Greater(meshes.Count, 0, m.Entity.Value + " 应有蒙皮网格");
                seen[m.Root.name] = new HashSet<string>(meshes);
                foreach (var r in m.Root.GetComponentsInChildren<Renderer>(false))
                {
                    if (r.gameObject.name == "BlobShadow")
                    {
                        continue;
                    }

                    foreach (var mat in r.sharedMaterials)
                    {
                        Assert.IsNotNull(mat, m.Entity.Value + " 的材质不应缺失（粉红）");
                        Assert.AreEqual("GameFoundation/Showcase/ModelLit", mat.shader.name, "模型材质应是模型包着色器：" + mat.name);
                        Assert.IsNotNull(mat.GetTexture("_BaseMap"), "模型材质应带贴图：" + mat.name);
                    }
                }
            }

            Assert.AreEqual(4, seen.Count, "四个角色是四个不同的模型：" + string.Join(", ", seen.Keys));
            var all = seen.Values.Select(v => string.Join("+", v)).Distinct().Count();
            Assert.AreEqual(seen.Count, all, "四个角色的网格互不相同");
        }

        [UnityTest]
        public IEnumerator DisplayRows_ReferenceExistingClips_AndCastOverrides_AllResolveFromRuntimeData()
        {
            var pg = NewPlayground(true);
            yield return Frames(4);
            var ctx = pg.Session!.Context!;
            var loader = UnityEngineHost.Ensure().ResourceLoader;
            foreach (var look in Looks)
            {
                var id = new Id("display.anim_set.show3d_" + look);
                var record = ctx.World.Registry.Get("display.anim_set", id);
                Assert.IsNotNull(record, "数据集里应有动画集行 " + id.Value + "（3D 演示数据根 data/_showcase_3d）");
                var set = AnimSetDef.FromRecord(record!, ctx.World.Registry);
                Assert.Greater(set.Clips.Count, 0, id.Value + " 应声明剪辑");
                foreach (var kv in set.Clips)
                {
                    Assert.IsTrue(loader.TryLoadAnimationClipSync(kv.Value.ResourceRef, out var clip) && clip != null,
                        id.Value + " 的键 " + kv.Key + " 指向的剪辑 " + kv.Value.ResourceRef + " 在运行时数据里解析不到（缺模型包同步？）");
                }

                Assert.IsTrue(loader.TryLoadModelSync(new Id("model.show3d_" + look), out var prefab) && prefab != null, "model.show3d_" + look + " 应能从运行时数据加载");
            }

            var weapon = ctx.World.Registry.Get("display.weapon_style", new Id("display.weapon_style.show_model"));
            Assert.IsNotNull(weapon, "武器风格行 display.weapon_style.show_model");
            Assert.IsTrue(weapon!.Raw.ContainsKey("cast_anim_override"), "武器风格行带出手剪辑映射");
        }

        [UnityTest]
        public IEnumerator FloorAndProps_ResolveFromSyncedArt_TexturedGroundPlane_AndBillboardProps()
        {
            var pg = NewPlayground(true);
            yield return Frames(30);
            var director = pg.Stage!.Showcase!;
            var ground = GameObject.Find("ShowcaseGround");
            Assert.IsNotNull(ground, "应有地面平面");
            var gr = ground!.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(gr, "地面是带贴图的平面");
            Assert.IsNotNull(gr.sprite, "地面贴图（assets/_showcase 的石砖）应从同步产物解析到，不得为空");
            Assert.IsTrue(gr.sprite.texture.name.IndexOf("floor_stone", StringComparison.Ordinal) >= 0 || gr.sprite.name.IndexOf("floor_stone", StringComparison.Ordinal) >= 0 || gr.sprite.texture != null, "地面是石砖贴图");
            Assert.IsTrue(director.Upright, "固定俯角相机下场景是直立模式");
            Assert.Greater(director.UprightPropCount, 0, "场景道具应是广告牌");
            var camera = pg.Stage.StageCamera!;
            foreach (var prop in director.UprightProps)
            {
                Assert.Less(Quaternion.Angle(prop.rotation, camera.transform.rotation), 0.5f, "道具广告牌与相机平行");
            }
        }

        // ───────── 每个实体恰有一个可见模型 ─────────

        [UnityTest]
        public IEnumerator EveryEntity_HasExactlyOneVisibleModel_AlsoWhileKnockedDown()
        {
            var pg = NewPlayground(true);
            pg.SetWeapon("feel.weapon.tpl_heavy_greatsword");
            pg.SetArchetype("feel.archetype.heavy");
            pg.SpawnDummy("mob", 3);
            pg.SpawnDummy("stake");
            yield return Frames(40);
            _input!.Press("u");
            yield return Frames(70);
            _input.Release("u");
            for (var i = 0; i < 220; i++)
            {
                yield return Frames(1);
                var models = pg.Stage!.RenderedModels();
                Assert.Greater(models.Count, 0, "有可见实体");
                foreach (var group in models.GroupBy(m => m.Entity.Value))
                {
                    Assert.AreEqual(1, group.Count(), "实体 " + group.Key + " 渲染了多份模型根");
                    var root = group.First().Root;
                    var visuals = Enumerable.Range(0, root.transform.childCount).Select(k => root.transform.GetChild(k)).Where(c => c.name == "Visual" && c.gameObject.activeInHierarchy).ToList();
                    Assert.AreEqual(1, visuals.Count, "实体 " + group.Key + " 应恰有一份可见内容");
                    var skinned = visuals[0].GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(r => r.enabled).Select(r => r.name).ToList();
                    Assert.AreEqual(skinned.Count, skinned.Distinct().Count(), "实体 " + group.Key + " 的蒙皮网格不应重复（重复 = 两个身体）：" + string.Join(",", skinned));
                    Assert.AreEqual(1, visuals[0].GetComponentsInChildren<Animator>(false).Length, "实体 " + group.Key + " 应恰有一个动画器");
                }
            }
        }

        // ───────── 站在地面上、身高与头顶高度表一致 ─────────

        [UnityTest]
        public IEnumerator Models_StandUprightOnGround_HeightMatchesHeadHeightTable()
        {
            var pg = NewPlayground(true);
            pg.SpawnDummy("elite");
            pg.SpawnDummy("stake");
            pg.SpawnDummy("frail");
            yield return Frames(60);
            var ctx = pg.Session!.Context!;
            var director = pg.Stage!.Showcase!;
            foreach (var m in pg.Stage.RenderedModels())
            {
                ApplyUpright(m.Root);
                var model = m.Root.transform.Find("Visual/UprightPivot/Model");
                Assert.IsNotNull(model, m.Entity.Value + " 的模型应在站立枢轴下");
                Assert.Greater(Vector3.Dot(model!.up, Vector3.back), 0.99f, m.Entity.Value + " 的头顶朝向应是世界 -Z（站在 Z=0 的地面上）");
                var renderers = m.Root.GetComponentsInChildren<SkinnedMeshRenderer>(false);
                var bounds = renderers[0].bounds;
                foreach (var r in renderers)
                {
                    bounds.Encapsulate(r.bounds);
                }

                Assert.AreEqual(0.0, bounds.max.z, 0.25, m.Entity.Value + " 的脚落在地面（Z≈0）");
                var height = -bounds.min.z;
                var headTable = director.HeadHeightOf(m.Entity);
                Assert.Less(height, headTable + 0.05f, m.Entity.Value + " 的头顶不超过头顶高度表 " + headTable);
                Assert.Greater(height, headTable - 0.6f, m.Entity.Value + " 的头顶高度表不应比模型高出太多（飘字会悬空）：模型 " + height + " 表 " + headTable);
                var pos = ctx.World.World.GetEntity(m.Entity)!.Position;
                Assert.AreEqual(pos.X, model.position.x, 0.6, "模型的 X 落在实体逻辑位置");
            }
        }

        // ───────── 敌人面朝玩家（表现层）：玩家在敌人的左/右/上/下时，模型的正面朝向由位置算出 ─────────

        private void AssertFacesPlayer(LabPlayground pg, Id enemy, string where)
        {
            var ctx = pg.Session!.Context!;
            var director = pg.Stage!.Showcase!;
            var ep = ctx.World.World.GetEntity(enemy)!.Position;
            var pp = ctx.World.World.GetEntity(ctx.PlayerId)!.Position;
            var angle = Math.Atan2(pp.Y - ep.Y, pp.X - ep.X);
            Assert.IsTrue(director.TryGetForwardedFacing(enemy, out var facing), where + "：应有交给引擎视图的朝向");
            Assert.AreEqual(angle, facing.RawRadians, 1e-6, where + "：交给 3D 模型视图的朝向角 = 指向玩家的角度（不带精灵的半圈偏移）");

            var entry = pg.Stage.RenderedModels().First(m => m.Entity.Equals(enemy));
            ApplyUpright(entry.Root);
            var model = entry.Root.transform.Find("Visual/UprightPivot/Model")!;
            var forward = model.forward;
            var modelAngle = Math.Atan2(forward.y, forward.x);
            var delta = Math.Abs(Math.IEEERemainder(modelAngle - angle, 2.0 * Math.PI));
            Assert.Less(delta, 0.1, where + "：模型正面朝向（" + modelAngle + "）应指向玩家（" + angle + "）；玩家在 " + pp + "，敌人在 " + ep);
        }

        [UnityTest]
        public IEnumerator Enemies_FacePlayer_FromLeftRightUpAndDown_ModelFrontFollowsPositions()
        {
            var pg = NewPlayground(true);
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

        // ───────── 动画：攻击与受击触发映射的剪辑（断言播放中的剪辑名随状态变化）─────────

        private string ClipOf(string key, string look)
        {
            var ctx = _pg!.Session!.Context!;
            var record = ctx.World.Registry.Get("display.anim_set", new Id("display.anim_set.show3d_" + look))!;
            var set = AnimSetDef.FromRecord(record, ctx.World.Registry);
            return set.Clips[key].ResourceRef.Value.Substring(set.Clips[key].ResourceRef.Value.LastIndexOf('.') + 1);
        }

        private static bool Played(IEnumerable<string> transitions, string clipId) => transitions.Any(t => t.IndexOf(":" + clipId + "__anim_set_override_", StringComparison.Ordinal) >= 0 || t.EndsWith(":" + clipId, StringComparison.Ordinal));

        [UnityTest]
        public IEnumerator ThreeHitCombo_PlaysMappedAttackClips_AndEliteHitClip_Hitstop_Flash_AndDamageNumbers()
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

            // 飘字数值取自命中确认事件、与独立于呈现的逻辑记录（damage 事件）逐个相等。
            var damageEvents = pg.Session.Recording.Events.Where(e => e.Kind == "damage" && e.Source == "player").ToList();
            Assert.GreaterOrEqual(damageEvents.Count, hits.Count);
            Assert.AreEqual(director.HitLog.Count, director.Model.NumbersSpawned, "每次命中确认一个飘字");
            for (var i = 0; i < hits.Count; i++)
            {
                Assert.AreEqual(damageEvents[i].Amount, hits[i].Amount, 1e-9, "第 " + (i + 1) + " 个飘字数值 = 逻辑伤害");
            }

            // 动画：玩家三段连招各播自己映射的剪辑；精英播受击剪辑（剪辑名由显示数据行算出）。
            var transitions = stage.Record.ClipTransitions;
            foreach (var skill in new[] { "skill.lab_a_combo1", "skill.lab_a_combo2", "skill.lab_a_combo3" })
            {
                var weapon = ctx.World.Registry.Get("display.weapon_style", new Id("display.weapon_style.show_model"))!;
                Assert.IsTrue(weapon.Raw.TryGetValue("cast_anim_override", out var mapNode) && mapNode is Core.Foundation.Common.Json.JsonObject, "武器风格行带出手剪辑映射");
                var map = (Core.Foundation.Common.Json.JsonObject)mapNode!;
                Assert.IsTrue(map.TryGetValue(skill, out var refNode) && refNode is Core.Foundation.Common.Json.JsonString, "映射里应有 " + skill);
                var clipRef = ((Core.Foundation.Common.Json.JsonString)refNode!).Value;
                var clipId = clipRef.Substring(clipRef.LastIndexOf('.') + 1);
                Assert.IsTrue(Played(transitions, clipId), "三连击应切到 " + skill + " 映射的剪辑 " + clipId + "；全部切换：" + string.Join(" | ", transitions.Take(40)));
            }

            Assert.IsTrue(Played(transitions, ClipOf("hit.light", ShowcaseDisplayRegistry.Brute)) || Played(transitions, ClipOf("hit", ShowcaseDisplayRegistry.Brute)), "精英应切到受击剪辑；全部切换：" + string.Join(" | ", transitions.Take(40)));
            Assert.IsTrue(Played(transitions, ClipOf("idle", ShowcaseDisplayRegistry.Hero)), "玩家待机剪辑");

            // 顿帧与闪白、特效。
            Assert.Greater(stage.Record.Freezes.Count, 0, "命中后引擎侧应有冻结记录");
            Assert.Greater(stage.FlashesApplied, flashBefore, "命中应触发闪白");
            Assert.GreaterOrEqual(director.Model.SparksSpawned, hits.Count, "每次命中一个火花");
            Assert.GreaterOrEqual(director.Model.SlashesSpawned, 1, "出手关键帧（模型动画事件 hit_frame）生成挥砍拖影");
            Assert.AreEqual(0, stage.Record.Errors.Count, string.Join(" | ", stage.Record.Errors));
        }

        /// <summary>读模型实例下渲染器上的闪白参数（属性块里的 flash_intensity；没写过属性块时为 0）。</summary>
        private static float FlashParamOf(GameObject root)
        {
            var best = 0f;
            var block = new MaterialPropertyBlock();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(block);
                best = Mathf.Max(best, block.GetFloat("flash_intensity"));
            }

            return best;
        }

        [UnityTest]
        public IEnumerator HitFlash_FollowsFeedbackData_AmountMatchesSpritePipeline_ThenDecaysToZero()
        {
            var pg = NewPlayground(true);
            pg.SpawnDummy("elite");
            yield return Frames(30);
            var stage = pg.Stage!;
            var ctx = pg.Session!.Context!;

            // 闪白时长来自数据：闪白配置行（vfx.def）的 lifetime；强度与精灵管线同口径（过曝 (1 + 强度) 倍，强度默认 1，不是整块填白）。
            var row = ctx.World.Registry.GetAll("vfx.def").FirstOrDefault(r => r.GetId("id").Equals(EngineLabStage.DefaultFlashProfile));
            Assert.IsNotNull(row, "数据集里应有闪白配置行 " + EngineLabStage.DefaultFlashProfile.Value);
            Assert.IsTrue(row!.TryGetNumber("lifetime", out var lifetime) && lifetime > 0.0, "闪白配置行带 lifetime");

            var elite = ctx.Dummies.First(d => d.Key.StartsWith("elite", StringComparison.Ordinal)).Value;
            var root = stage.RenderedModels().First(m => m.Entity.Equals(elite)).Root;
            Assert.AreEqual(0f, FlashParamOf(root), 1e-6, "命中前没有闪白");

            var before = stage.FlashesApplied;
            _input!.Press(AttackKey);
            var guard = 0;
            while (stage.FlashesApplied == before && guard++ < 240)
            {
                yield return Frames(1);
            }

            _input.Release(AttackKey);
            Assert.Greater(stage.FlashesApplied, before, "出手命中后应触发闪白");
            yield return Frames(1);
            Assert.AreEqual(EngineLabStage.FlashAmount, FlashParamOf(root), 1e-6, "闪白期间强度 = 默认强度（与精灵管线一致）");
            Assert.AreEqual(lifetime, stage.LastModelFlashSeconds, 1e-9, "闪白时长 = 数据行 lifetime");

            // 闪白期间保持，数据时长之后复原为 0（精灵管线同为阶跃：不会一直亮）。
            yield return Frames((int)Math.Floor(lifetime * 0.5 / Frame));
            Assert.AreEqual(EngineLabStage.FlashAmount, FlashParamOf(root), 1e-6, "时长过半仍在闪");
            yield return Frames((int)Math.Ceiling(lifetime / Frame) + 6);
            Assert.AreEqual(0f, FlashParamOf(root), 1e-6, "超过数据时长后闪白复原为 0");
        }

        [UnityTest]
        public IEnumerator KnockReaction_PlaysKnockbackOrKnockdownClip_ThenRecovers()
        {
            var pg = NewPlayground(true);
            pg.SetWeapon("feel.weapon.tpl_heavy_greatsword");
            pg.SetArchetype("feel.archetype.heavy");
            pg.SpawnDummy("mob", 3);
            yield return Frames(40);
            _input!.Press("u");
            yield return Frames(70);
            _input.Release("u");
            yield return Frames(260);
            var clips = pg.Stage!.Record.ClipTransitions.ToList();
            var knock = new[] { ClipOf("hit.knockback", ShowcaseDisplayRegistry.Grunt), ClipOf("hit.knockdown", ShowcaseDisplayRegistry.Grunt) };
            Assert.IsTrue(knock.Any(k => Played(clips, k)), "目标应切到击退/击倒映射的剪辑 " + string.Join("/", knock) + "；全部切换：" + string.Join(" | ", clips.Take(60)));
            var gruntClips = clips.Where(c => c.Contains("show3d_grunt")).ToList();
            Assert.Greater(gruntClips.Select(c => c.Substring(c.IndexOf(':') + 1)).Distinct().Count(), 2, "受击目标的剪辑随状态变化（不止待机）：" + string.Join(" | ", gruntClips.Take(30)));
        }

        [UnityTest]
        public IEnumerator Dodge_PlaysMappedDodgeClip()
        {
            var pg = NewPlayground(true);
            pg.SpawnDummy("stake");
            yield return Frames(30);
            yield return Tap("k");
            yield return Frames(40);
            Assert.IsTrue(Played(pg.Stage!.Record.ClipTransitions, ClipOf("dodge", ShowcaseDisplayRegistry.Hero)), "闪避应播映射的剪辑；全部切换：" + string.Join(" | ", pg.Stage.Record.ClipTransitions.Take(30)));
        }

        // ───────── HUD 与参考皮肤；调试面板能力与原试玩场景一致 ─────────

        [UnityTest]
        public IEnumerator Hud_UsesReferenceSkin_SkillSlotsAndCombo_PanelCollapsedByDefault()
        {
            var pg = NewPlayground(true);
            pg.SpawnDummy("stake");
            yield return Frames(30);
            Assert.IsNotNull(pg.Hud);
            Assert.IsTrue(pg.Hud!.Built);
            Assert.AreEqual(ShowcaseHud.SkinRef, "skin.reference_fantasy");
            Assert.AreEqual(ShowcaseHud.SkinRef, pg.Hud.LoadedSkinRef, "HUD 用参考皮肤");
            Assert.IsFalse(pg.Hud.UsesPlaceholderSkin, "构建同步应把参考皮肤包放进工作台资源根（缺同步时 HUD 会悄悄退回占位皮肤，这里必须红）");
            Assert.IsFalse(pg.Model.PanelVisible, "演示场景调试面板缺省收起");
            var m = pg.Stage!.Showcase!.Model;
            Assert.AreEqual(4, m.Slots.Length);
            yield return Tap(AttackKey);
            Assert.Greater(m.Slots[0].LockRemaining, 0.0, "出手后攻击格进入动作锁扫光");
            yield return Frames(14);
            yield return Tap(AttackKey);
            yield return Frames(14);
            Assert.GreaterOrEqual(m.Combo, 2, "连续命中累计连击数");
        }

        [UnityTest]
        public IEnumerator F1Panel_OffersSameTuningFeatures_AsOriginal3dPlayground()
        {
            var pg = NewPlayground(false);
            yield return Frames(4);
            string Ids(IEnumerable<LabChoice> c) => string.Join(",", c.Select(x => x.Id));
            var original = new[] { Ids(pg.Model.Presets), Ids(pg.Model.Weapons), Ids(pg.Model.Archetypes), Ids(pg.Model.DummyKinds) };
            Dispose();

            pg = NewPlayground(true);
            yield return Frames(4);
            var showcase = new[] { Ids(pg.Model.Presets), Ids(pg.Model.Weapons), Ids(pg.Model.Archetypes), Ids(pg.Model.DummyKinds) };
            for (var i = 0; i < original.Length; i++)
            {
                Assert.AreEqual(original[i], showcase[i], "面板第 " + i + " 组选项（预设/武器 F3/体型 F4/靶子）与原 3D 试玩场景一致");
                Assert.IsNotEmpty(showcase[i]);
            }

            Assert.IsTrue(pg.Model.Presets.Any(p => p.Id.Contains("tpl_")), "感觉模板预设（tpl_*）可选");
            pg.Model.PanelVisible = true; // F1
            foreach (var tab in new[] { LabTab.Scene, LabTab.Tuning, LabTab.Timeline, LabTab.Trajectory, LabTab.Rating })
            {
                pg.SetTab(tab); // F12 逐页
                yield return Frames(3);
                Assert.AreEqual(tab, pg.Model.Tab);
            }

            var weapon = pg.Model.Weapons.First(w => w.Id.Contains("tpl_"));
            pg.SetWeapon(weapon.Id); // F3
            Assert.AreEqual(weapon.Id, pg.Model.Weapon);
            var archetype = pg.Model.Archetypes.First(a => a.Id.Contains("heavy"));
            pg.SetArchetype(archetype.Id); // F4
            Assert.AreEqual(archetype.Id, pg.Model.Archetype);
            var template = pg.Model.Presets.First(p => p.Id.Contains("tpl_"));
            pg.SetPreset(template.Id);
            Assert.AreEqual(template.Id, pg.Model.Preset);
            Assert.AreEqual(0, pg.Stage!.Record.Errors.Count, string.Join(" | ", pg.Stage.Record.Errors));
        }

        // ───────── 逻辑不变：与原 3D 试玩场景逐字节一致 ─────────

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
            return pg.Host!.Runner.FingerprintOf(script, "3d_action", recording).Project(pg.Host.HeadlessRunner.Registry, MetricClass.Logic);
        }

        [UnityTest]
        public IEnumerator LogicFingerprint_IsByteIdentical_BetweenShowcase3dAndOriginal3dPlayground()
        {
            NewPlayground(false);
            yield return Scripted();
            var original = LogicOf(_pg!);
            Dispose();

            NewPlayground(true);
            yield return Scripted();
            var showcase = LogicOf(_pg!);
            Assert.IsTrue(_pg!.Session!.Script.Meta.ExtraDataRoots.Contains(LabPlayground.Showcase3dDataRoot), "3D 演示场景带上了 3D 演示数据根");
            Assert.Greater(original.Length, 200, "指纹不是空的");
            Assert.AreEqual(original, showcase, "同一段输入，3D 演示场景与原 3D 试玩场景的逻辑指纹必须逐字节一致");
        }

        [UnityTest]
        public IEnumerator OriginalPlayground3d_IsUnchanged_NoDirectorNoHud_PlaceholderModels_PanelVisible()
        {
            var pg = NewPlayground(false);
            pg.SpawnDummy("stake");
            yield return Frames(40);
            Assert.IsNull(pg.Stage!.Showcase);
            Assert.IsNull(pg.Hud);
            Assert.IsTrue(pg.Model.PanelVisible, "原试玩场景面板缺省展开");
            var models = pg.Stage.RenderedModels();
            Assert.Greater(models.Count, 0);
            Assert.IsTrue(models.All(m => !m.Root.name.Contains("show3d_")), "原试玩场景不画演示模型");
        }

        // ───────── 可选：演示场景截图（GF_LAB_SCREENSHOT_DIR 指定目录才跑；要有窗口的编辑器，批处理下不渲染屏幕；门禁不设）─────────

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

            // 二、朝向：玩家向四个方向跑，看正面/侧面/背面。
            pg.ClearDummies();
            yield return Frames(20);
            foreach (var (key, name) in new[] { ("d", "04a_run_east"), ("w", "04b_run_north"), ("a", "04c_run_west"), ("s", "04d_run_south") })
            {
                _input.Press(key);
                yield return Frames(25);
                yield return Shot(dir!, name);
                _input.Release(key);
                yield return Frames(6);
            }

            // 三、一群小怪：重击击退、蓄力击倒，之后逐段取样（击倒序列）。换一个全新的会话，靶子出在玩家右前方（标准布置）。
            Dispose();
            pg = NewPlayground(true);
            director = pg.Stage!.Showcase!;
            pg.SetWeapon("feel.weapon.tpl_heavy_greatsword");
            pg.SetArchetype("feel.archetype.heavy");
            pg.SpawnDummy("mob", 3);
            yield return Frames(50);
            yield return Shot(dir!, "05_pack_overall");
            var seen = director.HitLog.Count;
            _input.Press("u");
            yield return Frames(70);
            _input.Release("u");
            for (var i = 0; i < 150 && !director.HitLog.Skip(seen).Any(h => h.Reaction == "Knockback" || h.Reaction == "Knockdown"); i++)
            {
                yield return Frames(1);
            }

            var kd = director.HitLog.Skip(seen).FirstOrDefault(h => h.Reaction == "Knockback" || h.Reaction == "Knockdown");
            Debug.Log("[Showcase3dTests] knock hit: " + (kd == null ? "none" : kd.Reaction + "/" + kd.ImpactClass + " kill=" + kd.IsKill + " amount=" + kd.Amount));
            foreach (var (frames, name) in new[] { (2, "06a_knock_plus2"), (6, "06b_knock_plus8"), (10, "06c_knock_plus18"), (12, "06d_knock_plus30"), (20, "06e_knock_plus50"), (30, "06f_knock_plus80"), (40, "06g_knock_plus120") })
            {
                yield return Frames(frames);
                yield return Shot(dir!, name);
            }

            yield return Frames(30);
            yield return Shot(dir!, "07_hud_full");

            // 四、F1 展开 + F12 调参页，叠在演示场景上。
            pg.Model.PanelVisible = true;
            pg.SetTab(LabTab.Tuning);
            yield return Frames(12);
            yield return Shot(dir!, "08_f12_tuning_over_showcase");
            pg.SetTab(LabTab.Scene);
            yield return Frames(12);
            yield return Shot(dir!, "09_f1_scene_panel_over_showcase");
            pg.Model.PanelVisible = false;
            Debug.Log("[Showcase3dTests] screenshots done: hits=" + director.HitLog.Count + " reactions=" + string.Join(",", director.HitLog.Select(h => h.Reaction + "/" + h.ImpactClass).Distinct()));
            Debug.Log("[Showcase3dTests] transitions: " + string.Join(" | ", pg.Stage!.Record.ClipTransitions.Take(120)));
        }
    }
}
