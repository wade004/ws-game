#nullable enable
// StdDummyModelClipsPlayModeTests：框架级 model 型假人姿势集（手感设计/04 第 6.1 节、ADR-0119；
// toolchain/gen_std_dummy_model_clips.py + Editor/GenerateStdDummyModelAssets.cs 产出的预制体 model.std_dummy_biped、
// 33 份 .anim、数据行 display.anim_set.std_dummy_biped_model）的引擎侧验收。
//
// 验收对象（全部读运行时可观测量，期望值由规则/数据算出，不写死裸数）：
//   一、model 型单位按姿势键播放剪辑：状态 + 步态 + 武器族 -> 04 第 2.1 节键 -> 数据行 resource_ref -> Animator 正在播放的状态；
//   二、换武器族后剪辑键切到对应变体（待机/移动立即切；攻击按族取 attack.<族>），清除武器族回基础键；
//   三、命名事件在 04 规定的时刻触发：命中点 = 起手 + 判定相/2（起手与判定相取自手感武器档 feel.weapon 的 timeline_reference，
//       即 05 第 9 节标定），其余事件取规格 time_pct × 时长；带参标记 cancel_open:dodge 经 RaiseAnimEvent 换算成合法 Id；
//   四、骨骼路径与预制体一致：把剪辑采样到预制体实例上，每根骨骼的局部旋转/髋位置等于规格里的关键帧值；
//   五、数据行的每个键都能被引擎播放（控制器里有该状态、剪辑能经 IResourceLoader 取到且时长与规格一致）。
//
// 判断记录（手动步进 Animator，不依赖帧率）：用 Animator.Update(dt) 在同步用例里按固定 5 ms 步长推进，事件回调里读累计时间；
// 事件触发在跨过时刻的那一步内，所以观测值落在 (期望, 期望 + dt] ——容差取两个步长（含淡入第一步的相位误差）。不使用 Time.captureFramerate
// （全局状态，会污染同进程其它用例）。播放命令用混合时长 0（直接硬切），避免交叉淡入期间事件权重阈值的实现细节干扰时刻读数。
//
// 判断记录（数据与规格从仓库源目录读，同 DiscreteCombatTests 惯例）：不经 StreamingAssets 同步，不依赖"最近跑没跑过 -SyncContent"。
using System;
using System.Collections.Generic;
using System.IO;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using NUnit.Framework;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;

using DisplayInfo = Core.Foundation.DisplayInfo.DisplayInfo;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:render")]
    public sealed class StdDummyModelClipsPlayModeTests : PlayModeTestBase
    {
        private const string AnimSetIdValue = "display.anim_set.std_dummy_biped_model";
        private const string ModelIdValue = "model.std_dummy_biped";
        private const float StepSeconds = 0.005f;

        // ---------------- 规格（JsonUtility 形状，只声明用到的字段）----------------
        [Serializable] private class Spec { public SkeletonSpec skeleton = new SkeletonSpec(); public ClipSpec[] clips = Array.Empty<ClipSpec>(); }
        [Serializable] private class SkeletonSpec { public string root = ""; }
        [Serializable] private class EventSpec { public string name = ""; public float time_pct; }
        [Serializable] private class TrackSpec { public string path = ""; public float[] rot = Array.Empty<float>(); public float[] pos = Array.Empty<float>(); }
        [Serializable]
        private class ClipSpec
        {
            public string key = "";
            public string state = "";
            public string alias_of = "";
            public string resource_ref = "";
            public float total_ms;
            public float[] times_ms = Array.Empty<float>();
            public EventSpec[] anim_events = Array.Empty<EventSpec>();
            public TrackSpec[] tracks = Array.Empty<TrackSpec>();
        }

        private static Spec? s_spec;
        private static JsonObject? s_animSetRow;

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private static Spec LoadSpec()
        {
            s_spec ??= JsonUtility.FromJson<Spec>(File.ReadAllText(Path.Combine(RepoRoot, "assets", "_placeholder", "std_dummy_model_clips.json")));
            return s_spec;
        }

        private static JsonObject FindRow(string relativePath, Func<JsonObject, bool> match)
        {
            var doc = (JsonObject)JsonReader.Parse(File.ReadAllText(Path.Combine(RepoRoot, relativePath)));
            foreach (var v in (JsonArray)doc["rows"])
            {
                var row = (JsonObject)v;
                if (match(row))
                {
                    return row;
                }
            }
            throw new InvalidOperationException($"{relativePath} 里没有符合条件的行");
        }

        private static JsonObject AnimSetRow() =>
            s_animSetRow ??= FindRow("data/_framework/display/display.anim_set.json", r => ((JsonString)r["id"]).Value == AnimSetIdValue);

        private static string RowResourceRef(string key) =>
            ((JsonString)((JsonObject)((JsonObject)AnimSetRow()["clips"])[key])["resource_ref"]).Value;

        /// <summary>04 第 2.1 节键语法：<c>&lt;状态&gt;[.&lt;步态&gt;][.&lt;武器族&gt;]</c>（本用例不涉及姿态/变体维度）。</summary>
        private static string BuildKey(string state, string? gait, string? family)
        {
            var key = state;
            if (!string.IsNullOrEmpty(gait))
            {
                key += "." + gait;
            }
            if (!string.IsNullOrEmpty(family))
            {
                key += "." + family;
            }
            return key;
        }

        /// <summary>数据行 resource_ref（<c>anim.&lt;名&gt;</c>）-> Animator 状态名（UnityRenderer3D.PlayAnim 取 clipId 末段）。</summary>
        private static string StateNameOf(string resourceRef) => resourceRef.Substring(resourceRef.LastIndexOf('.') + 1);

        // ---------------- 夹具 ----------------

        private sealed class Fx : IDisposable
        {
            public UnityEngineHost Host = null!;
            public IEventBus Bus = null!;
            public UnityViewFactory Factory = null!;
            public UnityModelView View = null!;
            public Id Entity;
            public PoseSelector Selector = null!;
            public Animator Animator = null!;
            public ModelHandle Handle;

            public void Dispose()
            {
                View.Destroy();
            }
        }

        private static Fx Build()
        {
            var host = UnityEngineHost.Ensure();
            var definitions = new List<EventDefinition>();
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, Array.Empty<string>()));
            }
            var bus = new EventBus(EventCatalog.FromDefinitions(definitions), new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var logicalId = new Id("creature.std_dummy_model_test");
            var info = new DisplayInfo(
                id: new Id("display.map.std_dummy_model_test"),
                category: DisplayCategory.Creature,
                logicalId: logicalId,
                kind: DisplayKind.Model,
                iconId: null, vfxId: null, sfxId: null, scale: 1.0,
                shadow: Core.Foundation.DisplayInfo.ShadowMode.None, sortOffset: 0.0, weaponStyleRef: null,
                sprite: null,
                model: new ModelInfo(new Id(ModelIdValue), new Id(AnimSetIdValue), sockets: new[] { new Id("socket.main_hand") }));
            var displayInfoRegistry = new FakeDisplayInfoRegistryForAnim();
            displayInfoRegistry.Add(info);

            var dataRegistry = new FakeAnimSetAndWeaponStyleRegistry();
            var schema = new TableSchema("display.anim_set", "id", 1, Array.Empty<FieldSchema>());
            dataRegistry.Add("display.anim_set", new DataRecord(schema, AnimSetIdValue, new Id(AnimSetIdValue), AnimSetRow()));

            var factory = new UnityViewFactory(
                host.Renderer2D, new RenderConventionHost(), displayInfoRegistry, host.ResourceLoader,
                bus: bus, dataRegistry: dataRegistry, renderer3D: host.Renderer3D);
            var selector = new PoseSelector();
            factory.SetPoseContextSource(selector);

            var entity = new Id("unit.std_dummy_model_test_" + Guid.NewGuid().ToString("N"));
            var view = (UnityModelView)factory.CreateView(ViewKind.Unit, logicalId, entity);
            var animator = host.Renderer3D.GetModelVisualRoot(view.EngineHandle)!.GetComponentInChildren<Animator>();
            Assert.IsNotNull(animator, "std_dummy_biped 预制体应当带有 Animator");

            return new Fx
            {
                Host = host, Bus = bus, Factory = factory, View = view, Entity = entity, Selector = selector,
                Animator = animator!, Handle = view.EngineHandle,
            };
        }

        /// <summary>步进 Animator 越过默认混合时长（0.15 秒）后，返回正在播放的状态名（候选之外返回 null）。</summary>
        private static string? PlayingState(Animator animator, IEnumerable<string> candidates)
        {
            animator.Update(0.5f);
            var hash = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            foreach (var name in candidates)
            {
                if (Animator.StringToHash(name) == hash)
                {
                    return name;
                }
            }
            return null;
        }

        private static IEnumerable<string> AllStateNames()
        {
            foreach (var c in LoadSpec().clips)
            {
                if (string.IsNullOrEmpty(c.alias_of))
                {
                    yield return c.state;
                }
            }
        }

        /// <summary>进入 <paramref name="state"/>：运动态（Idle/Move）只接受 unit.state_changed（RequestOverride 会拒绝，见
        /// AnimStateMachine.RequestOverride 判断记录），动作态/受击/死亡走 RequestOverride 扩展点。</summary>
        private static void EnterState(Fx fx, AnimState state)
        {
            switch (state)
            {
                case AnimState.Idle:
                    fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.Entity, "Walk", "Idle"));
                    break;
                case AnimState.Move:
                    fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.Entity, "Idle", "Run"));
                    break;
                default:
                    fx.Factory.AnimStateMachine!.RequestOverride(fx.Entity, state);
                    break;
            }
        }

        private static void AssertPlays(Fx fx, string expectedKey, string context)
        {
            var expectedState = StateNameOf(RowResourceRef(expectedKey));
            var playing = PlayingState(fx.Animator, AllStateNames());
            Assert.AreEqual(expectedState, playing,
                $"{context}：应当按姿势键 \"{expectedKey}\" 播放（数据行 resource_ref -> Animator 状态 \"{expectedState}\"），实际正在播放 \"{playing ?? "(非假人集状态)"}\"");
        }

        // ---------------- 一、姿势键 -> 播放的剪辑 ----------------

        [TestCase(AnimState.Idle, null, null, null)]
        [TestCase(AnimState.Idle, null, "1h", null)]
        [TestCase(AnimState.Idle, null, "2h", null)]
        [TestCase(AnimState.Move, "walk", null, 0.45)]
        [TestCase(AnimState.Move, "run", null, 1.0)]
        [TestCase(AnimState.Move, "run", "1h", 1.0)]
        [TestCase(AnimState.Move, "run", "2h", 1.0)]
        [TestCase(AnimState.Attack, null, "unarmed", null)]
        [TestCase(AnimState.Attack, null, "1h", null)]
        [TestCase(AnimState.Attack, null, "2h", null)]
        [TestCase(AnimState.Attack, null, null, null)]
        [TestCase(AnimState.Hit, null, null, null)]
        [TestCase(AnimState.Death, null, null, null)]
        public void PoseKey_PlaysMatchingClipState(AnimState state, string? gait, string? family, double? speedRatio)
        {
            using var fx = Build();
            if (speedRatio.HasValue)
            {
                fx.Selector.Observe(fx.Entity, speedRatio.Value);
            }
            fx.Selector.SetFamily(fx.Entity, family);
            EnterState(fx, state);

            var stateKey = AnimClipResolver.StateKey(state);
            // 待机/移动按武器族取变体；攻击键只用武器族（无族回到基础键 attack，数据行里它是 attack.unarmed 的别名）；
            // 受击/死亡不分族（04 第 3 节：hit/death 无武器族变体）。
            var useFamily = state == AnimState.Idle || state == AnimState.Move || state == AnimState.Attack;
            var key = BuildKey(stateKey, gait, useFamily ? family : null);
            AssertPlays(fx, key, $"状态 {state}，步态 {gait ?? "-"}，武器族 {family ?? "-"}");
        }

        // ---------------- 二、换武器族后剪辑键切到变体 ----------------

        [Test]
        public void SwitchWeaponFamily_IdleAndMove_SwitchToFamilyVariantAndBack()
        {
            using var fx = Build();

            AssertPlays(fx, "idle", "初始无武器族");

            fx.Selector.SetFamily(fx.Entity, "1h");
            AssertPlays(fx, "idle.1h", "换成单手武器后待机");

            fx.Selector.SetFamily(fx.Entity, "2h");
            AssertPlays(fx, "idle.2h", "换成双手武器后待机");

            fx.Selector.Observe(fx.Entity, 1.0);
            EnterState(fx, AnimState.Move);
            AssertPlays(fx, "move.run.2h", "双手武器奔跑");

            fx.Selector.SetFamily(fx.Entity, "1h");
            AssertPlays(fx, "move.run.1h", "奔跑中换成单手武器");

            fx.Selector.SetFamily(fx.Entity, null);
            AssertPlays(fx, "move.run", "卸下武器后奔跑");
        }

        // ---------------- 三、命名事件触发时刻 ----------------

        private static (double StartupMs, double ActiveMs, double RecoveryMs) WeaponTimeline(string family)
        {
            var row = FindRow("data/_feel/feel/feel.weapon.json", r => r.ContainsKey("family") && ((JsonString)r["family"]).Value == family);
            var t = (JsonObject)row["timeline_reference"];
            return (((JsonNumber)t["startup_ms"]).Value, ((JsonNumber)t["active_ms"]).Value, ((JsonNumber)t["recovery_ms"]).Value);
        }

        private sealed class Recorded
        {
            public readonly List<(string Name, float At)> Events = new List<(string, float)>();
            public float Elapsed;
        }

        /// <summary>硬切播放 <paramref name="clipKey"/>，按固定步长推进到比时长多 0.1 秒，返回记录到的全部事件（名字 -> 累计时间）。</summary>
        private static Recorded PlayAndRecord(Fx fx, string clipKey)
        {
            var rec = new Recorded();
            fx.Host.Renderer3D.OnAnimEvent(fx.Handle, (_, eventId) => rec.Events.Add((eventId.Value, rec.Elapsed)));
            var spec = Array.Find(LoadSpec().clips, c => c.key == clipKey)!;
            var clipName = StateNameOf(RowResourceRef(clipKey));
            fx.Host.Renderer3D.PlayAnim(fx.Handle, new Id("anim." + clipName), loop: false, speed: 1.0, blendSeconds: 0.0);

            var total = spec.total_ms / 1000f;
            var steps = (int)Math.Ceiling((total + 0.1f) / StepSeconds);
            for (var i = 0; i < steps; i++)
            {
                rec.Elapsed += StepSeconds;
                fx.Animator.Update(StepSeconds);
            }
            return rec;
        }

        /// <summary>裸事件名（04 第 5 节标记名，带参标记的冒号后缀换成点）-> 引擎事件 Id（加 anim_event. 域前缀）。</summary>
        private static string EventIdOf(string bareName) => UnityRenderer3D.AnimEventDomainPrefix + bareName.Replace(':', '.');

        private static float TimeOfId(Recorded rec, string eventId)
        {
            var found = rec.Events.FindAll(e => e.Name == eventId);
            Assert.AreEqual(1, found.Count, $"事件 \"{eventId}\" 应当恰好触发一次，实际 {found.Count} 次；全部事件：" +
                string.Join(", ", rec.Events.ConvertAll(e => $"{e.Name}@{e.At:F3}")));
            return found[0].At;
        }

        private static float TimeOf(Recorded rec, string bareName) => TimeOfId(rec, EventIdOf(bareName));

        [TestCase("1h")]
        [TestCase("2h")]
        public void AttackHitFrame_FiresAtCalibratedMoment(string family)
        {
            using var fx = Build();
            var (startupMs, activeMs, _) = WeaponTimeline(family);
            var rec = PlayAndRecord(fx, BuildKey("attack", null, family));

            // 04 第 5 节：命中点落在判定相中点；判定相起止 = 起手结束 / 起手 + 判定相（05 第 9 节标定）。
            var expectedHit = (float)((startupMs + activeMs / 2.0) / 1000.0);
            var tol = 2 * StepSeconds + 1e-4f;
            Assert.AreEqual(expectedHit, TimeOfId(rec, ModelCharacterRig.HitFrameEventId.Value), tol,
                $"{family} 命中帧事件（ModelCharacterRig 识别的 hit_frame）应在 起手 {startupMs} ms + 判定相 {activeMs} ms / 2 处触发");
            Assert.AreEqual(expectedHit, TimeOf(rec, "hit"), tol, $"{family} 04 第 5 节命名事件 hit 应与命中帧同刻");
            Assert.AreEqual((float)(startupMs / 1000.0), TimeOf(rec, "active_start"), tol, $"{family} 判定相起点");
            Assert.AreEqual((float)((startupMs + activeMs) / 1000.0), TimeOf(rec, "active_end"), tol, $"{family} 判定相终点");
        }

        [TestCase("attack.unarmed")]
        [TestCase("attack.1h")]
        [TestCase("attack.1h.02")]
        [TestCase("attack.2h")]
        [TestCase("dodge")]
        [TestCase("cast")]
        public void ClipEvents_AllFireOnceAtSpecTime_IncludingParameterizedMarker(string clipKey)
        {
            using var fx = Build();
            var spec = Array.Find(LoadSpec().clips, c => c.key == clipKey)!;
            var total = spec.total_ms / 1000f;
            var rec = PlayAndRecord(fx, clipKey);
            var tol = 2 * StepSeconds + 1e-4f;
            var checkedEvents = 0;
            foreach (var e in spec.anim_events)
            {
                if (e.time_pct <= 0f)
                {
                    continue; // 0 时刻的事件是否在首步触发取决于引擎对起点的开闭区间处理，不属于本契约要验的时刻。
                }
                // 带参标记的冒号后缀在引擎事件 Id 里换成点分段（Id 格式不允许冒号）。
                Assert.AreEqual(e.time_pct * total, TimeOf(rec, e.name), tol, $"{clipKey} 事件 {e.name} 的触发时刻（time_pct {e.time_pct} × 时长 {total:F3} s）");
                checkedEvents++;
            }
            Assert.Greater(checkedEvents, 0, $"{clipKey} 应当至少核对到一个事件");
        }

        [Test]
        public void RaiseAnimEvent_ParameterizedMarker_ConvertsColonToDotId_AndInvalidNameIsDroppedNotThrown()
        {
            var host = UnityEngineHost.Ensure();
            var handle = host.Renderer3D.CreateModelInstance(new Id(ModelIdValue));
            try
            {
                var received = new List<string>();
                host.Renderer3D.OnAnimEvent(handle, (_, eventId) => received.Add(eventId.Value));

                host.Renderer3D.RaiseAnimEvent(handle.Value, "cancel_open:dodge");
                host.Renderer3D.RaiseAnimEvent(handle.Value, "Bad Name:X");
                host.Renderer3D.RaiseAnimEvent(handle.Value, "hit");

                CollectionAssert.AreEqual(
                    new[] { UnityRenderer3D.AnimEventDomainPrefix + "cancel_open.dodge", UnityRenderer3D.AnimEventDomainPrefix + "hit" },
                    received,
                    "带参标记换成点分段 Id；非法名字静默丢弃（不抛异常、不打断后续事件）；普通名字原样加域前缀");
            }
            finally
            {
                host.Renderer3D.DestroyModelInstance(handle);
            }
        }

        // ---------------- 四、骨骼路径与预制体一致（采样回读）----------------

        [Test]
        public void ClipsSampledOnPrefab_EveryBoneMatchesSpecKeyframes()
        {
            var host = UnityEngineHost.Ensure();
            var handle = host.Renderer3D.CreateModelInstance(new Id(ModelIdValue));
            try
            {
                var animator = host.Renderer3D.GetModelVisualRoot(handle)!.GetComponentInChildren<Animator>();
                Assert.IsNotNull(animator);
                var root = animator!.gameObject;
                var checkedClips = 0;
                foreach (var c in LoadSpec().clips)
                {
                    if (!string.IsNullOrEmpty(c.alias_of))
                    {
                        continue;
                    }
                    Assert.IsTrue(host.ResourceLoader.TryLoadAnimationClipSync(new Id("anim." + c.state), out var clip), $"{c.key} 剪辑资产应能经 IResourceLoader 取到");
                    // 取首、中、末三个关键帧采样（含终点），逐骨骼回读。
                    var idxs = new[] { 0, c.times_ms.Length / 2, c.times_ms.Length - 1 };
                    foreach (var idx in idxs)
                    {
                        clip.SampleAnimation(root, c.times_ms[idx] / 1000f);
                        foreach (var t in c.tracks)
                        {
                            var bone = root.transform.Find(t.path);
                            Assert.IsNotNull(bone, $"{c.key}：预制体里找不到曲线路径 \"{t.path}\"（骨骼路径与预制体不匹配）");
                            if (t.rot != null && t.rot.Length > 0)
                            {
                                var want = new Quaternion(t.rot[idx * 4], t.rot[idx * 4 + 1], t.rot[idx * 4 + 2], t.rot[idx * 4 + 3]);
                                Assert.Less(Quaternion.Angle(want, bone!.localRotation), 0.2f,
                                    $"{c.key} 关键帧 {idx} 骨骼 {t.path} 的局部旋转应等于规格值");
                            }
                            if (t.pos != null && t.pos.Length > 0)
                            {
                                var want = new Vector3(t.pos[idx * 3], t.pos[idx * 3 + 1], t.pos[idx * 3 + 2]);
                                Assert.Less(Vector3.Distance(want, bone!.localPosition), 1e-3f,
                                    $"{c.key} 关键帧 {idx} 骨骼 {t.path} 的局部位置应等于规格值");
                            }
                        }
                    }
                    checkedClips++;
                }
                Assert.Greater(checkedClips, 0, "至少应当核对到一份剪辑");
            }
            finally
            {
                host.Renderer3D.DestroyModelInstance(handle);
            }
        }

        // ---------------- 五、数据行每个键都能被引擎播放 ----------------

        [Test]
        public void EveryAnimSetKey_ResolvesToPlayableStateWithSpecDuration()
        {
            using var fx = Build();
            var clips = (JsonObject)AnimSetRow()["clips"];
            var keys = new List<string>();
            foreach (var kv in clips)
            {
                keys.Add(kv.Key);
            }
            Assert.AreEqual(LoadSpec().clips.Length, keys.Count, "数据行键数应当等于规格剪辑数");

            foreach (var key in keys)
            {
                var spec = Array.Find(LoadSpec().clips, c => c.key == key);
                Assert.IsNotNull(spec, $"数据行键 {key} 在规格里应当存在");
                var resourceRef = RowResourceRef(key);
                Assert.IsTrue(fx.Host.ResourceLoader.TryLoadAnimationClipSync(new Id(resourceRef), out var clip), $"{key}：{resourceRef} 应能经 IResourceLoader 取到剪辑");
                Assert.AreEqual(spec!.total_ms / 1000f, clip.length, 1e-3f, $"{key} 剪辑时长应等于规格 total_ms");

                fx.Host.Renderer3D.PlayAnim(fx.Handle, new Id(resourceRef), loop: false, speed: 1.0, blendSeconds: 0.0);
                var playing = PlayingState(fx.Animator, new[] { StateNameOf(resourceRef) });
                Assert.AreEqual(StateNameOf(resourceRef), playing, $"{key}：控制器里应当有状态 {StateNameOf(resourceRef)} 且 PlayAnim 后确实在播放");
            }
        }
    }
}
