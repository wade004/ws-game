#nullable enable
// StdDummyModelClipsPlayModeTests：框架级 model 型假人姿势集（手感设计/04 第 6.1 节、ADR-0119；
// toolchain/gen_std_dummy_model_clips.py + Editor/GenerateStdDummyModelAssets.cs 产出的预制体 model.std_dummy_biped、
// 数据行 display.anim_set.std_dummy_biped_model 及其 extends 体量组行 medium/light/heavy，剪辑数量以规格为准）的引擎侧验收；
// 手感落地 M4-D 起：体量组（轻/重）覆盖全部键，medium 行 = 主集（空 extends 行）；数据行带逐键 blend_ms 与每对键 blends；
// 文件末尾另有 sprite 型假人集的命中帧关键帧验收（StdDummySpriteHitFramePlayModeTests）。
//
// 验收对象（全部读运行时可观测量，期望值由规则/数据算出，不写死裸数）：
//   一、model 型单位按姿势键播放剪辑：状态 + 步态 + 武器族 -> 04 第 2.1 节键 -> 数据行 resource_ref -> Animator 正在播放的状态；
//   二、换武器族后剪辑键切到对应变体（待机/移动立即切；攻击按族取 attack.<族>），清除武器族回基础键；
//   三、命名事件在 04 规定的时刻触发：命中点 = 起手 + 判定相/2（起手与判定相取自手感武器档 feel.weapon 的 timeline_reference，
//       即 05 第 9 节标定），其余事件取规格 time_pct × 时长；带参标记 cancel_open:dodge 经 RaiseAnimEvent 换算成合法 Id；
//   四、骨骼路径与预制体一致：把剪辑采样到预制体实例上，每根骨骼的局部旋转/髋位置等于规格里的关键帧值；
//   五、数据行的每个键都能被引擎播放（控制器里有该状态、剪辑能经 IResourceLoader 取到且时长与规格一致）；
//   六、（M4-D）新增键（空中键/细节键/带伤全覆盖）可解析并播放、体量组按 extends 继承、切剪辑的 blend_ms 交叉淡入时长实测。
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
using UnityEngine.TestTools;

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
        [Serializable] private class Spec { public SkeletonSpec skeleton = new SkeletonSpec(); public ClipSpec[] clips = Array.Empty<ClipSpec>(); public MassGroupSpec[] mass_groups = Array.Empty<MassGroupSpec>(); }
        [Serializable] private class MassGroupSpec { public string id = ""; public string mass = ""; public ClipSpec[] clips = Array.Empty<ClipSpec>(); }
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

        private static JsonObject AnimSetRowById(string id) =>
            FindRow("data/_framework/display/display.anim_set.json", r => ((JsonString)r["id"]).Value == id);

        /// <summary>主集 + 全部体量组的剪辑（体量组条目按各自 state 名区分，key 与主集同名）。</summary>
        private static IEnumerable<ClipSpec> EveryClipIncludingMassGroups()
        {
            foreach (var c in LoadSpec().clips)
            {
                yield return c;
            }
            foreach (var g in LoadSpec().mass_groups)
            {
                foreach (var c in g.clips)
                {
                    yield return c;
                }
            }
        }

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

        /// <summary>步态/武器族/变体由用例脚本直接给定的姿势上下文来源（PoseSelector 在没有手感视图时不会派生冲刺档，
        /// 冲刺步态用本替身直接指定）。</summary>
        private sealed class ScriptedContext : IPoseContextSource
        {
            public PoseContext Current;

            public event Action<Id>? ContextChanged;

            public PoseContext GetContext(Id entityId) => Current;

            public void Set(Id entityId, PoseContext context)
            {
                Current = context;
                ContextChanged?.Invoke(entityId);
            }
        }

        private static Fx Build(string animSetId = AnimSetIdValue, IPoseContextSource? contextSource = null)
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
                model: new ModelInfo(new Id(ModelIdValue), new Id(animSetId), sockets: new[] { new Id("socket.main_hand") }));
            var displayInfoRegistry = new FakeDisplayInfoRegistryForAnim();
            displayInfoRegistry.Add(info);

            // 主集行 + 体量组行（extends 主集）都登记：体量组行靠 AnimSetDef.FromRecord 按 extends 到登记表里取主集合并。
            var dataRegistry = new FakeAnimSetAndWeaponStyleRegistry();
            var schema = new TableSchema("display.anim_set", "id", 1, Array.Empty<FieldSchema>());
            foreach (var rowId in new[] { AnimSetIdValue, AnimSetIdValue + "_medium", AnimSetIdValue + "_light", AnimSetIdValue + "_heavy" })
            {
                dataRegistry.Add("display.anim_set", new DataRecord(schema, rowId, new Id(rowId), AnimSetRowById(rowId)));
            }

            var factory = new UnityViewFactory(
                host.Renderer2D, new RenderConventionHost(), displayInfoRegistry, host.ResourceLoader,
                bus: bus, dataRegistry: dataRegistry, renderer3D: host.Renderer3D);
            var selector = new PoseSelector();
            factory.SetPoseContextSource(contextSource ?? selector);

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
            foreach (var c in EveryClipIncludingMassGroups())
            {
                if (string.IsNullOrEmpty(c.alias_of))
                {
                    yield return c.state;
                }
            }
        }

        /// <summary>某姿势集行里键 <paramref name="key"/> 的 resource_ref：行自己声明了就取行内的（体量组覆盖键），
        /// 没声明就沿 extends 取主集的（04 第 7 节：只声明要覆盖的键，其余沿用模板）。</summary>
        private static string ResolveRef(string rowId, string key)
        {
            var clips = (JsonObject)AnimSetRowById(rowId)["clips"];
            if (clips.ContainsKey(key))
            {
                return ((JsonString)((JsonObject)clips[key])["resource_ref"]).Value;
            }
            return RowResourceRef(key);
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

        private static void AssertPlays(Fx fx, string expectedKey, string context, string rowId = AnimSetIdValue)
        {
            var expectedState = StateNameOf(ResolveRef(rowId, expectedKey));
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
        [TestCase(AnimState.Idle, null, "polearm", null)]
        [TestCase(AnimState.Idle, null, "bow", null)]
        [TestCase(AnimState.Idle, null, "staff", null)]
        [TestCase(AnimState.Idle, null, "dual", null)]
        [TestCase(AnimState.Idle, null, "shield", null)]
        [TestCase(AnimState.Move, "walk", "polearm", 0.45)]
        [TestCase(AnimState.Move, "run", "bow", 1.0)]
        [TestCase(AnimState.Move, "run", "staff", 1.0)]
        [TestCase(AnimState.Move, "run", "dual", 1.0)]
        [TestCase(AnimState.Move, "run", "shield", 1.0)]
        [TestCase(AnimState.Attack, null, "polearm", null)]
        [TestCase(AnimState.Attack, null, "bow", null)]
        [TestCase(AnimState.Attack, null, "staff", null)]
        [TestCase(AnimState.Attack, null, "dual", null)]
        [TestCase(AnimState.Attack, null, "shield", null)]
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

        // ---------------- 二补、可选键：冲刺 / 带伤变体 / 体量组 ----------------

        [Test]
        public void SprintGait_PlaysSprintClipAndFamilySprintVariants_ThenBackToRun()
        {
            var ctx = new ScriptedContext();
            using var fx = Build(contextSource: ctx);
            EnterState(fx, AnimState.Move);

            ctx.Set(fx.Entity, new PoseContext(LocomotionGait.Sprint));
            AssertPlays(fx, "move.sprint", "冲刺（无武器族）");
            foreach (var family in new[] { "1h", "2h", "polearm", "bow", "staff", "dual", "shield" })
            {
                ctx.Set(fx.Entity, new PoseContext(LocomotionGait.Sprint, family));
                AssertPlays(fx, "move.sprint." + family, $"冲刺（武器族 {family}）");
            }

            ctx.Set(fx.Entity, new PoseContext(LocomotionGait.Run));
            AssertPlays(fx, "move.run", "冲刺回到跑步");
            Assert.AreNotEqual(StateNameOf(RowResourceRef("move.sprint")), StateNameOf(RowResourceRef("move.run")), "冲刺与跑步是两份剪辑");
        }

        [Test]
        public void WoundedVariant_PlaysWoundedClips_AndDropsVariantWhenFamilyKeyHasNoWoundedVersion()
        {
            using var fx = Build();
            fx.Selector.SetVariant(fx.Entity, "wounded");
            AssertPlays(fx, "idle.wounded", "带伤待机");

            fx.Selector.Observe(fx.Entity, 0.45);
            EnterState(fx, AnimState.Move);
            AssertPlays(fx, "move.walk.wounded", "带伤走");

            fx.Selector.Observe(fx.Entity, 1.0);
            AssertPlays(fx, "move.run.wounded", "带伤跑");

            // 回落链先去变体：没有 move.run.1h.wounded，应落到 move.run.1h（04 第 2.2 节）
            fx.Selector.SetFamily(fx.Entity, "1h");
            AssertPlays(fx, "move.run.1h", "带伤 + 单手武器奔跑");

            fx.Selector.SetFamily(fx.Entity, null);
            fx.Selector.SetVariant(fx.Entity, null);
            AssertPlays(fx, "move.run", "清除变体后奔跑");
        }

        [TestCase("light")]
        [TestCase("heavy")]
        public void MassGroupRow_CoversEveryKeyWithItsOwnClips_AndPlaysThem(string mass)
        {
            var rowId = AnimSetIdValue + "_" + mass;
            using var fx = Build(rowId);

            // 控制器默认状态是主集待机；体量组的待机要在单位进入待机状态后才由解析器按合并后的数据行播放
            // 单位创建时已处于待机（Idle -> Idle 不触发切换），先走一步 Move 再回 Idle 才会让解析器按体量组行取待机。
            EnterState(fx, AnimState.Move);
            EnterState(fx, AnimState.Idle);
            AssertPlays(fx, "idle", $"{mass} 体量待机", rowId);
            Assert.AreEqual($"anim.std_dummy_{mass}_idle", ResolveRef(rowId, "idle"), "待机应取体量组自己的剪辑");
            Assert.AreNotEqual(RowResourceRef("idle"), ResolveRef(rowId, "idle"));

            fx.Selector.Observe(fx.Entity, 1.0);
            EnterState(fx, AnimState.Move);
            AssertPlays(fx, "move.run", $"{mass} 体量奔跑", rowId);

            // M4-D：体量组覆盖全部键——单手攻击、受击也是组内自己的剪辑，不再沿 extends 取主集
            fx.Selector.SetFamily(fx.Entity, "1h");
            EnterState(fx, AnimState.Attack);
            AssertPlays(fx, "attack.1h", $"{mass} 体量单手攻击", rowId);
            Assert.AreEqual($"anim.std_dummy_{mass}_attack_1h", ResolveRef(rowId, "attack.1h"));
            Assert.AreNotEqual(RowResourceRef("attack.1h"), ResolveRef(rowId, "attack.1h"));
            EnterState(fx, AnimState.Hit);
            AssertPlays(fx, "hit", $"{mass} 体量受击", rowId);
            Assert.AreNotEqual(RowResourceRef("hit"), ResolveRef(rowId, "hit"));
        }

        [Test]
        public void MediumTierRow_IsTheMainSetByEmptyExtends_EveryKeyInherits()
        {
            var rowId = AnimSetIdValue + "_medium";
            var own = (JsonObject)AnimSetRowById(rowId)["clips"];
            Assert.AreEqual(0, own.Count, "medium 行是主集的空 extends 行：不声明任何键");
            Assert.AreEqual(AnimSetIdValue, ((JsonString)AnimSetRowById(rowId)["extends"]).Value);

            using var fx = Build(rowId);
            foreach (var kv in (JsonObject)AnimSetRow()["clips"])
            {
                Assert.AreEqual(RowResourceRef(kv.Key), ResolveRef(rowId, kv.Key), $"medium 体量 {kv.Key} 应沿 extends 取主集的剪辑");
            }
            EnterState(fx, AnimState.Move);
            EnterState(fx, AnimState.Idle);
            AssertPlays(fx, "idle", "medium 体量待机（= 主集）", rowId);
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
        [TestCase("attack.polearm")]
        [TestCase("attack.polearm.02")]
        [TestCase("attack.bow")]
        [TestCase("attack.staff")]
        [TestCase("attack.dual")]
        [TestCase("attack.dual.03")]
        [TestCase("attack.shield")]
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
        public void EveryAttackClip_HitFrameEventFiresExactlyOnce_AtSameMomentAsHit()
        {
            using var fx = Build();
            var n = 0;
            foreach (var c in LoadSpec().clips)
            {
                if (!string.IsNullOrEmpty(c.alias_of) || !c.key.StartsWith("attack.", StringComparison.Ordinal))
                {
                    continue;
                }
                var rec = PlayAndRecord(fx, c.key);
                var hitFrameAt = TimeOfId(rec, ModelCharacterRig.HitFrameEventId.Value);   // 恰好一次（TimeOfId 内断言）
                Assert.AreEqual(TimeOf(rec, "hit"), hitFrameAt, 1e-4f, $"{c.key}：hit_frame 应与 04 第 5 节 hit 同刻");
                n++;
            }
            Assert.Greater(n, 10, "应当核对到全部攻击剪辑（含五个新武器族）");
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
                foreach (var c in EveryClipIncludingMassGroups())
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

        [Test]
        public void EveryMassGroupRowKey_ResolvesToPlayableOwnClipWithSpecDuration()
        {
            using var fx = Build();
            var n = 0;
            foreach (var g in LoadSpec().mass_groups)
            {
                var rowClips = (JsonObject)AnimSetRowById(g.id)["clips"];
                var rowKeyCount = 0;
                foreach (var _ in rowClips)
                {
                    rowKeyCount++;
                }
                Assert.AreEqual(g.clips.Length, rowKeyCount, $"{g.id} 数据行键数应当等于规格里的体量组剪辑数");
                foreach (var spec in g.clips)
                {
                    var resourceRef = ResolveRef(g.id, spec.key);
                    Assert.AreEqual(spec.resource_ref, resourceRef, $"{g.id}/{spec.key} 数据行与规格的资源引用应一致");
                    Assert.IsTrue(fx.Host.ResourceLoader.TryLoadAnimationClipSync(new Id(resourceRef), out var clip), $"{g.id}/{spec.key}：{resourceRef} 应能经 IResourceLoader 取到剪辑");
                    Assert.AreEqual(spec.total_ms / 1000f, clip.length, 1e-3f, $"{g.id}/{spec.key} 剪辑时长应等于规格 total_ms（体量组不改时长）");
                    fx.Host.Renderer3D.PlayAnim(fx.Handle, new Id(resourceRef), loop: false, speed: 1.0, blendSeconds: 0.0);
                    Assert.AreEqual(StateNameOf(resourceRef), PlayingState(fx.Animator, new[] { StateNameOf(resourceRef) }),
                        $"{g.id}/{spec.key}：控制器里应当有状态 {StateNameOf(resourceRef)}");
                    n++;
                }
            }
            Assert.AreEqual(2 * LoadSpec().clips.Length, n, "轻/重两个体量组各覆盖主集的全部键（M4-D：体量组不再只覆盖姿态键）");
        }

        // ---------------- 六、手感落地 M4-D：新增键 / 体量组继承 / blend_ms 交叉淡入 ----------------

        private static readonly string[] AirKeys =
        {
            "jump.rise", "jump.fall", "jump.land", "hit.air",
            "attack.air", "attack.air.unarmed", "attack.air.1h", "attack.air.2h", "attack.air.polearm",
            "attack.air.bow", "attack.air.staff", "attack.air.dual", "attack.air.shield",
        };

        private static readonly string[] DetailKeys = { "hit.block", "hit.block.shield", "stunned.sway", "hit.launch.tumble", "hit.launch.land" };

        private static readonly string[] WoundedMoveKeys =
        {
            "move.sprint.wounded", "move.sprint.combat.wounded", "move.walk.combat.wounded", "move.run.combat.wounded",
            "move.start.wounded", "move.stop.wounded", "move.pivot.wounded",
        };

        private static IEnumerable<string> NewKeys()
        {
            foreach (var k in AirKeys) { yield return k; }
            foreach (var k in DetailKeys) { yield return k; }
            foreach (var k in WoundedMoveKeys) { yield return k; }
        }

        [Test]
        public void NewKeys_ResolveAndPlay_InMainSetAndEveryTier()
        {
            var rows = new[] { AnimSetIdValue, AnimSetIdValue + "_medium", AnimSetIdValue + "_light", AnimSetIdValue + "_heavy" };
            using var fx = Build();
            var n = 0;
            foreach (var key in NewKeys())
            {
                var spec = Array.Find(LoadSpec().clips, c => c.key == key);
                Assert.IsNotNull(spec, $"规格里应有新增键 {key}");
                foreach (var rowId in rows)
                {
                    var resourceRef = ResolveRef(rowId, key);
                    Assert.IsTrue(fx.Host.ResourceLoader.TryLoadAnimationClipSync(new Id(resourceRef), out var clip), $"{rowId}/{key}：{resourceRef} 应能经 IResourceLoader 取到剪辑");
                    fx.Host.Renderer3D.PlayAnim(fx.Handle, new Id(resourceRef), loop: false, speed: 1.0, blendSeconds: 0.0);
                    Assert.AreEqual(StateNameOf(resourceRef), PlayingState(fx.Animator, new[] { StateNameOf(resourceRef) }),
                        $"{rowId}/{key}：控制器里应当有状态 {StateNameOf(resourceRef)} 且确实在播放");
                    if (string.IsNullOrEmpty(spec!.alias_of))
                    {
                        Assert.AreEqual(spec.total_ms / 1000f, clip.length, 1e-3f, $"{rowId}/{key} 时长应等于规格 total_ms（体量组不改时长）");
                    }
                    n++;
                }
            }
            Assert.AreEqual(CountOf(NewKeys()) * rows.Length, n);
        }

        private static int CountOf(IEnumerable<string> keys)
        {
            var c = 0;
            foreach (var _ in keys) { c++; }
            return c;
        }

        [Test]
        public void NewKeys_TierGroupsCarryTheirOwnClips_MediumInheritsMain()
        {
            foreach (var key in NewKeys())
            {
                var main = ResolveRef(AnimSetIdValue, key);
                Assert.AreEqual(main, ResolveRef(AnimSetIdValue + "_medium", key), $"{key}：medium = 主集");
                foreach (var mass in new[] { "light", "heavy" })
                {
                    var tier = ResolveRef(AnimSetIdValue + "_" + mass, key);
                    Assert.AreNotEqual(main, tier, $"{key}：{mass} 体量应有自己的剪辑");
                    Assert.IsTrue(tier.StartsWith($"anim.std_dummy_{mass}_", StringComparison.Ordinal), $"{key}：{mass} 体量资源引用应带体量前缀，实际 {tier}");
                }
            }
        }

        [Test]
        public void WoundedVariantAndAirKeys_ResolveThroughTheSelector()
        {
            var ctx = new ScriptedContext();
            using var fx = Build(contextSource: ctx);
            EnterState(fx, AnimState.Move);

            ctx.Set(fx.Entity, new PoseContext(LocomotionGait.Sprint, null, "wounded"));
            AssertPlays(fx, "move.sprint.wounded", "带伤冲刺");
            ctx.Set(fx.Entity, new PoseContext(LocomotionGait.Walk, null, "wounded"));
            AssertPlays(fx, "move.walk.wounded", "带伤走（既有键）");
            Assert.AreNotEqual(RowResourceRef("move.sprint"), RowResourceRef("move.sprint.wounded"), "带伤冲刺是独立剪辑");
        }

        // ---- blend_ms 交叉淡入实测 ----

        private const float BlendStep = 0.002f;

        /// <summary>从 <paramref name="fromRef"/> 切到 <paramref name="toRef"/>（经 ModelCharacterRig.PlayClip，即生产路径），
        /// 手动步进 Animator 直到离开过渡，返回过渡持续的秒数（含最后一步的相位，误差 &lt;= 一个步长）。</summary>
        private static double MeasureCrossFade(Fx fx, Id fromRef, Id toRef, out bool sawTransition)
        {
            var rig = (ModelCharacterRig)fx.View.Rig;
            rig.PlayClip(fromRef, loop: true, speed: 1.0);
            fx.Animator.Update(1.0f);                       // 先充分进入起点剪辑（走完它自己的进入过渡）
            Assert.IsFalse(fx.Animator.IsInTransition(0), "起点剪辑应已稳定播放");
            rig.PlayClip(toRef, loop: true, speed: 1.0);

            sawTransition = false;
            var elapsed = 0.0;
            for (var i = 0; i < 2000; i++)
            {
                fx.Animator.Update(BlendStep);
                elapsed += BlendStep;
                if (fx.Animator.IsInTransition(0))
                {
                    sawTransition = true;
                }
                else
                {
                    break;
                }
            }
            return elapsed;
        }

        private static double RowBlendMs(string rowId, string key)
        {
            var clips = (JsonObject)AnimSetRowById(rowId)["clips"];
            var entry = clips.ContainsKey(key) ? (JsonObject)clips[key] : (JsonObject)((JsonObject)AnimSetRow()["clips"])[key];
            return ((JsonNumber)entry["blend_ms"]).Value;
        }

        private static List<(string From, string To, double Ms)> RowPairs()
        {
            var list = new List<(string, string, double)>();
            foreach (var v in (JsonArray)AnimSetRow()["blends"])
            {
                var o = (JsonObject)v;
                list.Add((((JsonString)o["from"]).Value, ((JsonString)o["to"]).Value, ((JsonNumber)o["blend_ms"]).Value));
            }
            return list;
        }

        private static void AssertDuration(double expectedMs, double measuredSeconds, bool saw, string context)
        {
            Debug.Log($"[blend-smoke] {context}：声明 {expectedMs:F0} 毫秒，实测 {measuredSeconds * 1000:F1} 毫秒");
            if (expectedMs > 0)
            {
                Assert.IsTrue(saw, $"{context}：声明 {expectedMs} 毫秒的交叉淡入应当真的进入过渡");
            }
            Assert.AreEqual(expectedMs / 1000.0, measuredSeconds, 2.5 * BlendStep + 1e-4,
                $"{context}：交叉淡入时长应等于数据声明的 {expectedMs} 毫秒（实测 {measuredSeconds * 1000:F1} 毫秒）");
        }

        [Test]
        public void BlendMs_PerPairDeclaration_ControlsMeasuredCrossFade()
        {
            using var fx = Build();
            Assert.IsNotNull(((ModelCharacterRig)fx.View.Rig).AnimBlendSource, "装配层应把姿势集挂给 ModelCharacterRig.AnimBlendSource");
            var measured = 0;
            foreach (var (from, to, ms) in RowPairs())
            {
                var fromRef = RowResourceRef(from);
                Assert.IsTrue(fx.Host.ResourceLoader.TryLoadAnimationClipSync(new Id(fromRef), out var fromClip));
                if (fromClip.length * 1000f < ms + 100f)
                {
                    continue; // 起点剪辑比过渡还短，循环播放下起点会重复，不适合量过渡时长
                }
                var seconds = MeasureCrossFade(fx, new Id(fromRef), new Id(RowResourceRef(to)), out var saw);
                AssertDuration(ms, seconds, saw, $"{from} -> {to}（每对键声明）");
                measured++;
            }
            Assert.GreaterOrEqual(measured, 5, "至少应当实测到 5 对键的交叉淡入");
        }

        [Test]
        public void BlendMs_PerKeyDeclaration_AppliesWhenNoPairDeclared_AndPairBeatsPerKey()
        {
            using var fx = Build();
            var pairs = RowPairs();

            // 逐键：idle -> move.run 没有对声明，取目标键 move.run 的 blend_ms
            Assert.IsFalse(pairs.Exists(p => p.From == "idle" && p.To == "move.run"), "前提：idle -> move.run 没有对声明");
            var perKey = RowBlendMs(AnimSetIdValue, "move.run");
            var s1 = MeasureCrossFade(fx, new Id(RowResourceRef("idle")), new Id(RowResourceRef("move.run")), out var saw1);
            AssertDuration(perKey, s1, saw1, "idle -> move.run（逐键 blend_ms）");

            // 每对键压过逐键：hit.getup -> idle 声明了对值，且与 idle 的逐键值不同
            var pair = pairs.Find(p => p.From == "hit.getup" && p.To == "idle");
            Assert.IsNotNull(pair.From, "前提：hit.getup -> idle 有对声明");
            Assert.AreNotEqual(RowBlendMs(AnimSetIdValue, "idle"), pair.Ms, "前提：对值与目标逐键值不同才能区分优先级");
            var s2 = MeasureCrossFade(fx, new Id(RowResourceRef("hit.getup")), new Id(RowResourceRef("idle")), out var saw2);
            AssertDuration(pair.Ms, s2, saw2, "hit.getup -> idle（每对键 > 逐键）");
        }

        [Test]
        public void BlendMs_Undeclared_KeepsDefaultCrossFade_AndExplicitZeroIsHardCut()
        {
            using var fx = Build();
            var rig = (ModelCharacterRig)fx.View.Rig;
            var from = new Id(RowResourceRef("idle"));
            var to = new Id(RowResourceRef("move.run"));

            // 缺省（不挂数据）= 此前的固定默认 0.15 秒
            rig.AnimBlendSource = null;
            var s0 = MeasureCrossFade(fx, from, to, out var saw0);
            AssertDuration(ModelCharacterRig.DefaultBlendSeconds * 1000.0, s0, saw0, "未挂 AnimBlendSource（缺省）");

            // 显式 0 = 硬切：一步之内离开过渡
            rig.AnimBlendSource = new FixedBlend(0.0);
            var sHard = MeasureCrossFade(fx, from, to, out _);
            Assert.LessOrEqual(sHard, BlendStep + 1e-4, $"显式 0 应硬切（实测 {sHard * 1000:F1} 毫秒）");
            Assert.AreEqual(StateNameOf(RowResourceRef("move.run")),
                PlayingState(fx.Animator, new[] { StateNameOf(RowResourceRef("move.run")) }), "硬切后直接在目标状态");
        }

        private sealed class FixedBlend : IAnimBlendSource
        {
            private readonly double _seconds;

            public FixedBlend(double seconds) { _seconds = seconds; }

            public bool TryGetBlendSeconds(Id? fromClip, Id toClip, out double seconds)
            {
                seconds = _seconds;
                return true;
            }
        }

        [TestCase("light")]
        [TestCase("heavy")]
        public void BlendMs_TierGroupInheritsMainSetDeclarations(string mass)
        {
            var rowId = AnimSetIdValue + "_" + mass;
            using var fx = Build(rowId);
            Assert.IsNotNull(((ModelCharacterRig)fx.View.Rig).AnimBlendSource);
            var pair = RowPairs().Find(p => p.From == "hit.getup" && p.To == "idle");
            Assert.IsNotNull(pair.From);

            // 体量组行自己不声明 blend_ms/blends，沿 extends 取主集；切换用的是组内自己的剪辑（资源引用带体量前缀）
            var from = ResolveRef(rowId, "hit.getup");
            var to = ResolveRef(rowId, "idle");
            Assert.IsTrue(from.StartsWith($"anim.std_dummy_{mass}_", StringComparison.Ordinal));
            var seconds = MeasureCrossFade(fx, new Id(from), new Id(to), out var saw);
            AssertDuration(pair.Ms, seconds, saw, $"{mass} 体量 hit.getup -> idle（继承主集的每对键声明）");

            var perKey = RowBlendMs(AnimSetIdValue, "move.run");
            var s2 = MeasureCrossFade(fx, new Id(ResolveRef(rowId, "idle")), new Id(ResolveRef(rowId, "move.run")), out var saw2);
            AssertDuration(perKey, s2, saw2, $"{mass} 体量 idle -> move.run（继承主集的逐键 blend_ms）");
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // sprite 型假人集的命中帧关键帧验收（手感落地 M3-D）：数据行里每个攻击剪辑的 hit_frame 事件，经引擎侧换算后得到的帧下标
    // 必须落在该剪辑判定相的帧范围内，且序列帧播放器按这个下标恰好触发一次 FrameAnimClip.HitFrameMarker（"hit_frame"，
    // 与 model 型 ModelCharacterRig 识别的同名事件一致）。用仓库里的真实序列帧资源（assets/_placeholder 指向的隔离根目录）。
    //
    // 判断记录（经反射调用 UnityViewFactory.ComputeKeyframes）：它是工厂内部私有的纯函数，生产路径对每个登记的剪辑都用它把
    // 数据行 events 换算成关键帧表；用例要验的正是"生产换算的结果"，复刻一份公式会变成自己验自己。反射只取这一个静态方法，
    // 签名变了用例会在第一行失败并指出，不会静默通过。
    // ------------------------------------------------------------------------------------------------------------
    [Category("module:render")]
    public sealed class StdDummySpriteHitFramePlayModeTests : PlayModeTestBase
    {
        private const string SpriteAnimSetId = "display.anim_set.std_dummy_biped";

        private bool _overridden;

        [SetUp]
        public void SetUp()
        {
            // 仓库的占位资产根；RootDirOverrideForTests 是 internal 静态字段，测试程序集已获可见性，用例结束必须还原。
            UnityResourceLoader.RootDirOverrideForTests = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..")), "assets", "_placeholder");
            _overridden = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_overridden)
            {
                UnityResourceLoader.RootDirOverrideForTests = null;
                _overridden = false;
            }
        }

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private static JsonObject ReadJson(string relative) => (JsonObject)JsonReader.Parse(File.ReadAllText(Path.Combine(RepoRoot, relative)));

        private static AnimSetDef LoadSpriteAnimSet()
        {
            var doc = ReadJson("data/_framework/display/display.anim_set.json");
            JsonObject? row = null;
            foreach (var v in (JsonArray)doc["rows"])
            {
                var r = (JsonObject)v;
                if (((JsonString)r["id"]).Value == SpriteAnimSetId)
                {
                    row = r;
                }
            }
            Assert.IsNotNull(row, $"数据文件里应有 {SpriteAnimSetId} 行");
            var schema = new TableSchema("display.anim_set", "id", 1, Array.Empty<FieldSchema>());
            return AnimSetDef.FromRecord(new DataRecord(schema, SpriteAnimSetId, new Id(SpriteAnimSetId), row!));
        }

        private static IReadOnlyDictionary<string, int>? ComputeKeyframes(AnimClipDef clip, int frameCount)
        {
            var method = typeof(UnityViewFactory).GetMethod("ComputeKeyframes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method, "UnityViewFactory.ComputeKeyframes 应存在（私有静态：数据行 events -> 帧下标关键帧表）");
            return (IReadOnlyDictionary<string, int>?)method!.Invoke(null, new object[] { clip.Events, frameCount });
        }

        private static readonly string[] SpriteAttackKeys =
        {
            "attack.unarmed", "attack.1h", "attack.1h.02", "attack.2h", "attack.polearm", "attack.bow", "attack.staff",
            "attack.dual", "attack.shield",
        };

        [UnityTest]
        public System.Collections.IEnumerator AttackClip_HitFrameKeyframeLiesInActivePhase_AndPlayerFiresItOnce(
            [ValueSource(nameof(SpriteAttackKeys))] string key)
        {
            var set = LoadSpriteAnimSet();
            Assert.IsTrue(set.Clips.TryGetValue(key, out var clipDef), $"{key} 应在 sprite 数据行里");
            var resourceId = clipDef!.ResourceRef;

            var spec = ReadJson("assets/_placeholder/std_dummy_poses.json");
            var specClip = (JsonObject)((JsonObject)spec["clips"])[key];
            var perPhase = (JsonArray)specClip["frames_per_phase"];
            var windupFrames = (int)((JsonNumber)perPhase[0]).Value;
            var activeFrames = (int)((JsonNumber)perPhase[1]).Value;
            var frameCount = (int)((JsonNumber)specClip["frame_count"]).Value;

            var loader = new UnityResourceLoader();
            var loaded = false;
            loader.LoadAsync(resourceId, ResourceKind.Effect, (_, ok) =>
            {
                Assert.IsTrue(ok, $"{resourceId} 应能从 assets/_placeholder 加载");
                loaded = true;
            });
            var deadline = Time.realtimeSinceStartup + 10f;
            while (!loaded && Time.realtimeSinceStartup < deadline)
            {
                loader.Tick();
                yield return null;
            }
            Assert.IsTrue(loaded, $"{resourceId} 加载超时");
            Assert.IsTrue(loader.TryGetEffect(resourceId, out var effect));
            Assert.AreEqual(frameCount, effect.Frames.Length, $"{key} 序列帧数应等于规格帧数");

            var keyframes = ComputeKeyframes(clipDef, effect.Frames.Length);
            Assert.IsNotNull(keyframes, $"{key} 的数据行应有事件，换算出关键帧表");
            Assert.IsTrue(keyframes!.TryGetValue(FrameAnimClip.HitFrameMarker, out var hitIndex),
                $"{key}：数据行事件里应有 {FrameAnimClip.HitFrameMarker}，换算后进关键帧表（sprite 型命中帧同步）");
            Assert.GreaterOrEqual(hitIndex, windupFrames, $"{key} 命中帧下标 {hitIndex} 不应早于判定相首帧（前摇 {windupFrames} 帧）");
            Assert.Less(hitIndex, windupFrames + activeFrames, $"{key} 命中帧下标 {hitIndex} 应落在判定相内（{activeFrames} 帧）");

            var go = new GameObject("StdDummySpriteHitFramePlayer");
            try
            {
                var renderer = go.AddComponent<SpriteRenderer>();
                var player = go.AddComponent<UnityFrameAnimPlayer>();
                var clipId = new Id("anim.std_dummy_hit_frame_probe");
                player.RegisterClipFromEffect(clipId, effect, keyframes);
                var fired = 0;
                Sprite? spriteAtHit = null;
                player.OnAnimEvent(marker =>
                {
                    if (marker == FrameAnimClip.HitFrameMarker)
                    {
                        fired++;
                        spriteAtHit = renderer.sprite;
                    }
                });
                var completed = false;
                player.OnComplete(() => completed = true);
                player.Play(clipId, loop: false, speed: 1.0);

                var until = Time.realtimeSinceStartup + 10f;
                while (!completed && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }
                Assert.IsTrue(completed, $"{key} 非循环剪辑应自然播完");
                Assert.AreEqual(1, fired, $"{key} 命中帧标记应恰好触发一次");
                Assert.AreSame(effect.Frames[hitIndex].Sprite, spriteAtHit, $"{key} 命中帧事件触发时显示的应是第 {hitIndex} 帧");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
