#nullable enable
// AnimationLayerTests：W3b 收边（拍板 6，09 §4 动画层落地）PlayMode 验收——
// UnityFrameAnimPlayer（序列帧播放：播放/循环/hit_frame 回调，≥3 条）、AnimClipResolver（状态机
// 驱动到具体剪辑：移动/施法/受击/死亡各触发对应剪辑，≥4 条）、八个程序动画原语可视化（每个原语
// 触发后对应属性在时长内变化并回到终态，≥8 条）。
//
// 判断记录（不经完整 FrameworkResidentHost/ShellRoot 装配，直接构造最小夹具）：本文件验证的是
// "UnityFrameAnimPlayer/AnimClipResolver/UnitySpriteView 程序动画原语落地"这些独立单元本身的正确
// 性，不依赖完整游戏世界装配（玩家/生物/AI/战斗）——同 UnityRenderer2DTests.cs 的既有惯例（直接
// new UnityRenderer2D + UnityResourceLoader），减少无关装配环节引入的不稳定性。
using System.Collections;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using NUnit.Framework;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class AnimationLayerTests : PlayModeTestBase
    {
        private GameObject _rootGo = null!;
        private UnityResourceLoader _resourceLoader = null!;
        private UnityRenderer2D _renderer = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("AnimationLayerTestsRoot");
            _resourceLoader = new UnityResourceLoader();
            _renderer = new UnityRenderer2D(_rootGo.transform, _resourceLoader);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_rootGo);
        }

        private static Sprite MakeSprite(Color color)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply();
            return Sprite.Create(texture, new UnityEngine.Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f);
        }

        // ------------------------------------------------------------------
        // UnityFrameAnimPlayer：IFrameAnimPlayer 契约（播放/循环/hit_frame），≥3 条。
        // ------------------------------------------------------------------

        [Test]
        public void FrameAnimPlayer_Play_SwitchesRendererSprite()
        {
            var go = new GameObject("Player");
            var player = go.AddComponent<UnityFrameAnimPlayer>();
            var frame0 = MakeSprite(Color.red);
            var frame1 = MakeSprite(Color.blue);
            player.RegisterClip(new Id("anim.test_clip"), new[] { frame0, frame1 }, frameRate: 10.0);

            player.Play(new Id("anim.test_clip"), loop: false, speed: 1.0);

            var renderer = go.GetComponent<SpriteRenderer>();
            Assert.AreEqual(frame0, renderer.sprite, "Play 应立即以第 0 帧触发一次 FrameChanged");

            Object.DestroyImmediate(go);
        }

        [UnityTest]
        public IEnumerator FrameAnimPlayer_Loop_CyclesBackToFirstFrame()
        {
            var go = new GameObject("Player");
            var player = go.AddComponent<UnityFrameAnimPlayer>();
            var frame0 = MakeSprite(Color.red);
            var frame1 = MakeSprite(Color.green);
            player.RegisterClip(new Id("anim.loop_clip"), new[] { frame0, frame1 }, frameRate: 20.0); // 20fps：每帧 0.05s。

            player.Play(new Id("anim.loop_clip"), loop: true, speed: 1.0);
            var renderer = go.GetComponent<SpriteRenderer>();
            Assert.AreEqual(frame0, renderer.sprite);

            var sawFrame1 = false;
            var sawFrame0Again = false;
            for (var i = 0; i < 30 && !sawFrame0Again; i++)
            {
                yield return new WaitForFixedUpdate();
                yield return null;
                if (renderer.sprite == frame1) sawFrame1 = true;
                if (sawFrame1 && renderer.sprite == frame0) sawFrame0Again = true;
            }

            Assert.IsTrue(sawFrame1, "循环剪辑推进期间应当至少看到过第 1 帧");
            Assert.IsTrue(sawFrame0Again, "循环剪辑到达末帧后应当回到第 0 帧继续循环，不停止");

            Object.DestroyImmediate(go);
        }

        [UnityTest]
        public IEnumerator FrameAnimPlayer_HitFrameKeyframe_FiresOnAnimEventOnce()
        {
            var go = new GameObject("Player");
            var player = go.AddComponent<UnityFrameAnimPlayer>();
            var frames = new[] { MakeSprite(Color.red), MakeSprite(Color.green), MakeSprite(Color.blue) };
            var keyframes = new Dictionary<string, int> { [FrameAnimClip.HitFrameMarker] = 1 };
            player.RegisterClip(new Id("anim.hit_clip"), frames, frameRate: 20.0, keyframes: keyframes);

            var hitFrameFireCount = 0;
            player.OnAnimEvent(marker =>
            {
                if (marker == FrameAnimClip.HitFrameMarker) hitFrameFireCount++;
            });

            var completeCount = 0;
            player.OnComplete(() => completeCount++);

            player.Play(new Id("anim.hit_clip"), loop: false, speed: 1.0);

            for (var i = 0; i < 30 && completeCount == 0; i++)
            {
                yield return new WaitForFixedUpdate();
                yield return null;
            }

            Assert.AreEqual(1, hitFrameFireCount, "命中帧标记（帧下标 1）应当恰好触发一次 OnAnimEvent");
            Assert.AreEqual(1, completeCount, "非循环剪辑自然播完应当触发一次 OnComplete");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SpriteView_PlayClip_ForwardsToAttachedFrameAnimPlayer()
        {
            var info = DisplayInfoTestSupportForAnimTests.CreateSpriteInfo();
            var view = new UnitySpriteView(_renderer, new RenderConventionHost(), info, _resourceLoader);

            var go = new GameObject("Attached");
            var player = go.AddComponent<UnityFrameAnimPlayer>();
            var frame = MakeSprite(Color.white);
            player.RegisterSingleFrameClip(new Id("anim.single"), frame);

            view.AttachFrameAnimPlayer(player);
            view.PlayClip(new Id("anim.single"), loop: false, speed: 1.0);

            Assert.AreEqual(new Id("anim.single"), player.CurrentClipId, "UnitySpriteView.PlayClip 应当转发给已挂接的 IFrameAnimPlayer");

            Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------------
        // AnimClipResolver：状态机驱动到具体剪辑，≥4 条（移动/施法/受击/死亡）。
        // ------------------------------------------------------------------

        private static (IEventBus Bus, AnimStateMachine Machine) BuildStateMachine()
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, System.Array.Empty<string>()));
            }
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });
            return (bus, new AnimStateMachine(bus));
        }

        private static IReadOnlyDictionary<string, Id> SampleClips() => new Dictionary<string, Id>
        {
            ["idle"] = new Id("anim.t_idle"),
            ["move"] = new Id("anim.t_move"),
            ["attack"] = new Id("anim.t_attack"),
            ["cast"] = new Id("anim.t_cast"),
            ["hit"] = new Id("anim.t_hit"),
            ["death"] = new Id("anim.t_death"),
        };

        private static (Id EntityId, Id ClipId, bool Loop)? LastResolved(List<(Id, Id, bool)> log) =>
            log.Count > 0 ? log[log.Count - 1] : ((Id, Id, bool)?)null;

        [Test]
        public void AnimClipResolver_Move_ResolvesMoveClip()
        {
            var (bus, machine) = BuildStateMachine();
            var log = new List<(Id, Id, bool)>();
            var resolver = new AnimClipResolver(machine, _ => SampleClips(), (entityId, clipId, loop, speed) => log.Add((entityId, clipId, loop)));

            var unitId = new Id("unit.t1");
            bus.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(unitId, "Idle", "Walk"));

            var last = LastResolved(log);
            Assert.IsTrue(last.HasValue, "移动状态变化应当触发一次剪辑解析");
            Assert.AreEqual(new Id("anim.t_move"), last!.Value.ClipId, "move 状态应解出 display.anim_set 的 move 剪辑");
            Assert.IsTrue(last.Value.Loop, "move 是循环态");

            resolver.Dispose();
        }

        [Test]
        public void AnimClipResolver_Cast_ResolvesCastClip()
        {
            var (bus, machine) = BuildStateMachine();
            var log = new List<(Id, Id, bool)>();
            var resolver = new AnimClipResolver(machine, _ => SampleClips(), (entityId, clipId, loop, speed) => log.Add((entityId, clipId, loop)));

            var unitId = new Id("unit.t2");
            bus.PublishImmediate(new Core.Rules.Common.SkillCastStartEvent(unitId, new Id("skill.t_fireball"), castTime: 1.5));

            var last = LastResolved(log);
            Assert.IsTrue(last.HasValue);
            Assert.AreEqual(new Id("anim.t_cast"), last!.Value.ClipId, "castTime>0 应判定为 Cast 状态并解出 cast 剪辑");
            Assert.IsFalse(last.Value.Loop, "cast 不是循环态");

            resolver.Dispose();
        }

        [Test]
        public void AnimClipResolver_Hit_ResolvesHitClip()
        {
            var (bus, machine) = BuildStateMachine();
            var log = new List<(Id, Id, bool)>();
            var resolver = new AnimClipResolver(machine, _ => SampleClips(), (entityId, clipId, loop, speed) => log.Add((entityId, clipId, loop)));

            var unitId = new Id("unit.t3");
            bus.PublishImmediate(new Core.Rules.Common.CombatDamageDealtEvent(
                new Id("unit.attacker"), unitId, new Id("school.physical"), 3.0, isCrit: false, Core.Rules.Common.HitResult.Hit));

            var last = LastResolved(log);
            Assert.IsTrue(last.HasValue);
            Assert.AreEqual(new Id("anim.t_hit"), last!.Value.ClipId, "受击事件应解出 hit 剪辑");

            resolver.Dispose();
        }

        [Test]
        public void AnimClipResolver_Death_ResolvesDeathClip()
        {
            var (bus, machine) = BuildStateMachine();
            var log = new List<(Id, Id, bool)>();
            var resolver = new AnimClipResolver(machine, _ => SampleClips(), (entityId, clipId, loop, speed) => log.Add((entityId, clipId, loop)));

            var unitId = new Id("unit.t4");
            bus.PublishImmediate(new Core.Rules.Common.UnitDiedEvent(unitId, new Id("cause.test")));

            var last = LastResolved(log);
            Assert.IsTrue(last.HasValue);
            Assert.AreEqual(new Id("anim.t_death"), last!.Value.ClipId, "死亡事件应解出 death 剪辑");
            Assert.IsFalse(last.Value.Loop, "death 是终态、不循环");

            resolver.Dispose();
        }

        // ------------------------------------------------------------------
        // 八个程序动画原语可视化：触发后对应属性在时长内变化并回到终态，≥8 条。
        // ------------------------------------------------------------------

        private UnitySpriteView CreateView()
        {
            var view = new UnitySpriteView(_renderer, new RenderConventionHost(), DisplayInfoTestSupportForAnimTests.CreateSpriteInfo(), _resourceLoader);
            view.Bind(new Id("unit.test_anim_layer")); // SyncPose 要求 IsAlive（Bind 之后才为真），见 SpriteViewBase.EnsureAlive。
            return view;
        }

        private static IEnumerator AdvanceRig(UnitySpriteView view, int frames = 10, float dt = 0.05f)
        {
            for (var i = 0; i < frames; i++)
            {
                view.Rig.Update(dt);
                view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), 0.0);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Procedural_Move_OffsetsThenHoldsAtTarget()
        {
            var view = CreateView();
            view.PlayMove(new MoveParams(new Vec2(2, 0), 0.2));
            yield return AdvanceRig(view, frames: 15, dt: 0.05f);

            var root = _renderer.GetSpriteRoot(view.EngineHandle);
            Assert.IsNotNull(root);
            Assert.AreEqual(2f, root!.transform.localPosition.x, 0.05f, "Move 原语时长过后应停留在目标偏移");
        }

        [UnityTest]
        public IEnumerator Procedural_Rotate_RotatesThenHoldsAtTarget()
        {
            var view = CreateView();
            view.PlayRotate(new RotateParams(System.Math.PI / 2.0, 0.2));
            yield return AdvanceRig(view, frames: 15, dt: 0.05f);

            var root = _renderer.GetSpriteRoot(view.EngineHandle);
            var eulerZ = root!.transform.localRotation.eulerAngles.z;
            Assert.AreEqual(90f, eulerZ, 2f, "Rotate 原语时长过后应停留在目标角度（90 度）");
        }

        [UnityTest]
        public IEnumerator Procedural_Scale_PunchesThenReturnsToOne()
        {
            var view = CreateView();
            view.PlayScale(new ScaleParams(1.5, 0.2));

            var root = _renderer.GetSpriteRoot(view.EngineHandle);
            view.Rig.Update(0.05); // 前半程：应已经放大。
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), 0.0);
            Assert.Greater(root!.transform.localScale.x, 1.0f, "冲击曲线前半程应大于基准缩放 1.0");

            yield return AdvanceRig(view, frames: 15, dt: 0.05f);
            Assert.AreEqual(1f, root.transform.localScale.x, 0.05f, "Scale 原语播完后应回到基准缩放 1.0（对称冲击曲线终态）");
        }

        [UnityTest]
        public IEnumerator Procedural_Flash_SetsShaderParamThenDecaysToZero()
        {
            var view = CreateView();
            // 先合成出至少一层纸娃娃层（SetShaderParam 才有渲染器可应用）：SyncPose 覆写在层集合
            // 非空时会自动调用基类 SetPaperdollLayers（见 UnitySpriteView.SyncPose），不需要直接
            // 调用该 protected 方法。
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), 0.0);

            view.Rig.ProceduralAnim.Flash(new FlashParams(1.0, 0.1));
            var root = _renderer.GetSpriteRoot(view.EngineHandle);
            var layersRoot = root!.transform.Find("LayersRoot");
            var rendererComp = layersRoot.GetChild(0).GetComponent<SpriteRenderer>();
            Assert.Greater(rendererComp.color.r, 1.0f, "Flash 触发后应立即过曝（RGB > 1）");

            for (var i = 0; i < 15; i++)
            {
                view.Rig.Update(0.05);
                yield return null;
            }
            Assert.AreEqual(1.0f, rendererComp.color.r, 0.05f, "Flash 衰减到 0 后应复原为正常颜色（乘数 1）");
        }

        [UnityTest]
        public IEnumerator Procedural_Fade_ReducesAlphaThenHoldsAtTarget()
        {
            var view = CreateView();
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), 0.0);

            view.PlayFade(new FadeParams(0.3, 0.2));
            yield return AdvanceRig(view, frames: 15, dt: 0.05f);

            var root = _renderer.GetSpriteRoot(view.EngineHandle);
            var rendererComp = root!.transform.Find("LayersRoot").GetChild(0).GetComponent<SpriteRenderer>();
            Assert.AreEqual(0.3f, rendererComp.color.a, 0.05f, "Fade 原语时长过后应停留在目标透明度");
        }

        [UnityTest]
        public IEnumerator Procedural_Trail_EnablesThenDisablesEmitting()
        {
            var view = CreateView();
            var root = _renderer.GetSpriteRoot(view.EngineHandle)!;

            view.PlayTrail(root, new TrailParams(0.15));
            var trail = root.GetComponent<TrailRenderer>();
            Assert.IsNotNull(trail, "Trail 原语触发后应挂上 TrailRenderer 组件");
            Assert.IsTrue(trail.emitting, "Trail 触发后应处于发射状态");

            yield return AdvanceRig(view, frames: 15, dt: 0.05f);
            Assert.IsFalse(trail.emitting, "Trail 时长过后应停止发射（回到静止终态）");
        }

        [UnityTest]
        public IEnumerator Procedural_Stagger_OffsetsThenReturnsToZero()
        {
            var view = CreateView();
            view.PlayStagger(new StaggerParams(new Vec2(1, 0), 0.2));

            var root = _renderer.GetSpriteRoot(view.EngineHandle);
            view.Rig.Update(0.05);
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), 0.0);
            Assert.Greater(root!.transform.localPosition.x, 0f, "回弹曲线前半程应产生非零偏移");

            yield return AdvanceRig(view, frames: 15, dt: 0.05f);
            Assert.AreEqual(0f, root.transform.localPosition.x, 0.05f, "Stagger 播完后应回弹到 0（对称回弹曲线终态）");
        }

        [UnityTest]
        public IEnumerator Procedural_Topple_RotatesThenHoldsAtTarget()
        {
            var view = CreateView();
            view.PlayTopple(ToppleParams.Default(0.2));
            yield return AdvanceRig(view, frames: 15, dt: 0.05f);

            var root = _renderer.GetSpriteRoot(view.EngineHandle);
            var eulerZ = root!.transform.localRotation.eulerAngles.z;
            Assert.AreEqual(90f, eulerZ, 2f, "Topple 播完后应停留在倒地角度（默认 90 度）并保持，不回弹");
        }
    }

    /// <summary>本文件专用的最小 sprite 型 <see cref="Core.Foundation.DisplayInfo.DisplayInfo"/> 构造
    /// 帮助方法，字段取值只为满足 <see cref="Adapter.Unity.Presentation.UnitySpriteView"/> 构造期的
    /// 最低要求（同 <see cref="UnityRenderer2DTests"/> 用到的 <c>sprite.creature.sample_wolf</c> 惯例，
    /// 但不依赖任何 data/_sample 记录，纯内存构造，避免与其它套件共享数据集状态）。</summary>
    internal static class DisplayInfoTestSupportForAnimTests
    {
        public static Core.Foundation.DisplayInfo.DisplayInfo CreateSpriteInfo()
        {
            var sprite = new Core.Foundation.DisplayInfo.SpriteInfo(
                spriteSetId: "sprite.test_anim_layer",
                directionCount: 8,
                paperdollLayers: new List<string> { "body" });

            return new Core.Foundation.DisplayInfo.DisplayInfo(
                id: new Id("display.map.test_anim_layer"),
                category: Core.Foundation.DisplayInfo.DisplayCategory.Creature,
                logicalId: new Id("creature.test_anim_layer"),
                kind: DisplayKind.Sprite,
                iconId: null,
                vfxId: null,
                sfxId: null,
                scale: 1.0,
                shadow: Core.Foundation.DisplayInfo.ShadowMode.None,
                sortOffset: 0.0,
                weaponStyleRef: null,
                sprite: sprite,
                model: null);
        }
    }
}
