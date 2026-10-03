#nullable enable
// AirPoseAnimPlayModeTests：空中姿势请求（ADR-0130 追加决定，竖直轴能力包补完 M4-V）的引擎侧冒烟。
//
// 验收对象（读运行时可观测量——AnimClipResolver 实际请求播放的剪辑 id；期望值由"固定回落链约定"算出，不写死裸数）：
//   一、键存在：空中阶段 rise/fall/land 请求 jump.rise/jump.fall/jump.land；空中受击请求 hit.air；空中攻击请求
//       attack.air.<武器族>（无武器族时 attack.air）；
//   二、键缺失：沿固定链逐级回落——jump.rise|fall → jump → idle、jump.land → idle、hit.air → hit.launch → hit、
//       attack.air.<族> → attack.air → attack.<族> → attack；
//   三、不变量：没有任何空中阶段时（地面）请求与改动前一致；
//   四、工厂接线：UnityViewFactory 把姿势上下文来源挂到它持有的 AnimStateMachine 上（腾空即进 Jump，阶段清除即回运动态）。
//
// 判断记录（用记录型假 playClip 委托，不经真实播放器）：同 AnimClipResolverTests 顶部判断记录——本文件验证的是"该请求哪个剪辑"
// 这一决策，不是"该剪辑是否已在某个播放器上登记"。工厂接线一条用例直接构造 UnityViewFactory 本体（同
// UnityViewFactoryAnimStateMachineAccessTests），只读它公开的 AnimStateMachine 状态。
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using NUnit.Framework;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:render")]
    [Category("interaction:air_pose")]
    public sealed class AirPoseAnimPlayModeTests : PlayModeTestBase
    {
        private static readonly Id Entity = new Id("unit.air_pose_pm");

        private static IEventBus NewBus()
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, System.Array.Empty<string>()));
            }
            return new EventBus(EventCatalog.FromDefinitions(definitions), new EventBusOptions { StrictCatalog = false, AuditLog = false });
        }

        /// <summary>键 -> 剪辑 id 的表：剪辑 id 与键同名（<c>clip.&lt;键&gt;</c>），便于由请求键直接算出期望值。</summary>
        private static Dictionary<string, Id> Table(params string[] keys)
        {
            var table = new Dictionary<string, Id>();
            foreach (var key in keys) table[key] = new Id("clip." + key);
            return table;
        }

        private sealed class Rig : System.IDisposable
        {
            public IEventBus Bus = NewBus();
            public AnimStateMachine Machine = null!;
            public PoseSelector Selector = new PoseSelector();
            public AnimClipResolver Resolver = null!;
            public List<Id> Played = new List<Id>();

            public Rig(params string[] keys)
            {
                Machine = new AnimStateMachine(Bus);
                Machine.AttachAirPhaseSource(Selector);
                var table = Table(keys);
                Resolver = new AnimClipResolver(
                    Machine,
                    defaultClipsForEntity: _ => table,
                    playClip: (e, c, l, s) => Played.Add(c),
                    weaponStyleSource: null,
                    weaponStyles: null,
                    isClipReady: null,
                    poseContext: Selector);
            }

            public Id Last => Played[Played.Count - 1];

            public void Dispose()
            {
                Resolver.Dispose();
                Machine.Dispose();
            }
        }

        // ---------- 一、键存在 ----------

        [Test]
        public void Jump_RequestsRiseThenFallThenLandClips_AndReturnsToIdleWhenTheWindowEnds()
        {
            using var rig = new Rig("idle", "jump", "jump.rise", "jump.fall", "jump.land");

            rig.Selector.SetAirPhase(Entity, AirPhase.Rise);
            Assert.AreEqual(new Id("clip.jump.rise"), rig.Last);
            rig.Selector.SetAirPhase(Entity, AirPhase.Fall);
            Assert.AreEqual(new Id("clip.jump.fall"), rig.Last);
            rig.Selector.SetAirPhase(Entity, AirPhase.Land);
            Assert.AreEqual(new Id("clip.jump.land"), rig.Last);
            rig.Selector.SetAirPhase(Entity, AirPhase.None);
            Assert.AreEqual(new Id("clip.idle"), rig.Last);
            Assert.AreEqual(AnimState.Idle, rig.Machine.GetState(Entity));
        }

        [Test]
        public void HitInTheAir_RequestsHitAir_AndGroundHitStaysPlainHit()
        {
            using var rig = new Rig("idle", "hit", "hit.launch", "hit.air", "jump");
            rig.Machine.RequestOverride(Entity, AnimState.Hit);
            Assert.AreEqual(new Id("clip.hit"), rig.Last, "地面受击：与改动前一致");
            rig.Machine.NotifyTransientStateFinished(Entity, AnimState.Hit);

            rig.Selector.SetAirPhase(Entity, AirPhase.Rise);
            rig.Machine.RequestOverride(Entity, AnimState.Hit);
            Assert.AreEqual(new Id("clip.hit.air"), rig.Last);
        }

        [Test]
        public void AttackInTheAir_RequestsTheFamilyAirClip_ThenTheFamilylessAirClip()
        {
            using var rig = new Rig("idle", "attack", "attack.greatsword", "attack.air", "attack.air.greatsword", "jump");
            rig.Selector.SetFamily(Entity, "greatsword");
            rig.Selector.SetAirPhase(Entity, AirPhase.Fall);
            rig.Machine.RequestOverride(Entity, AnimState.Attack);
            Assert.AreEqual(new Id("clip.attack.air.greatsword"), rig.Last);

            rig.Machine.NotifyTransientStateFinished(Entity, AnimState.Attack);
            rig.Selector.SetFamily(Entity, null);
            rig.Machine.RequestOverride(Entity, AnimState.Attack);
            Assert.AreEqual(new Id("clip.attack.air"), rig.Last);
        }

        // ---------- 二、键缺失：沿固定链回落 ----------

        [Test]
        public void MissingJumpPhaseKeys_FallBackToJump_ThenIdle()
        {
            using (var rig = new Rig("idle", "jump"))
            {
                rig.Selector.SetAirPhase(Entity, AirPhase.Rise);
                Assert.AreEqual(new Id("clip.jump"), rig.Last, "jump.rise 缺 -> jump");
                rig.Selector.SetAirPhase(Entity, AirPhase.Fall);
                Assert.AreEqual(new Id("clip.jump"), rig.Last, "jump.fall 缺 -> jump");
                rig.Selector.SetAirPhase(Entity, AirPhase.Land);
                Assert.AreEqual(new Id("clip.idle"), rig.Last, "jump.land 缺 -> idle（不借用 jump 的空中剪辑）");
            }

            using (var bare = new Rig("idle"))
            {
                bare.Selector.SetAirPhase(Entity, AirPhase.Rise);
                Assert.AreEqual(new Id("clip.idle"), bare.Last, "jump.rise 与 jump 都缺 -> idle");
            }
        }

        [Test]
        public void MissingHitAir_FallsBackToHitLaunch_ThenHit()
        {
            using (var rig = new Rig("idle", "hit", "hit.launch"))
            {
                rig.Selector.SetAirPhase(Entity, AirPhase.Rise);
                rig.Machine.RequestOverride(Entity, AnimState.Hit);
                Assert.AreEqual(new Id("clip.hit.launch"), rig.Last);
            }

            using (var rig = new Rig("idle", "hit"))
            {
                rig.Selector.SetAirPhase(Entity, AirPhase.Rise);
                rig.Machine.RequestOverride(Entity, AnimState.Hit);
                Assert.AreEqual(new Id("clip.hit"), rig.Last);
            }
        }

        [Test]
        public void MissingAttackAirKeys_FallBackThroughAirThenFamilyThenBase()
        {
            foreach (var (keys, expected) in new[]
            {
                (new[] { "idle", "attack", "attack.greatsword", "attack.air" }, "attack.air"),
                (new[] { "idle", "attack", "attack.greatsword" }, "attack.greatsword"),
                (new[] { "idle", "attack" }, "attack"),
            })
            {
                using var rig = new Rig(keys);
                rig.Selector.SetFamily(Entity, "greatsword");
                rig.Selector.SetAirPhase(Entity, AirPhase.Rise);
                rig.Machine.RequestOverride(Entity, AnimState.Attack);
                Assert.AreEqual(new Id("clip." + expected), rig.Last);
            }
        }

        // ---------- 二点五、变体维度（手感落地 M4-W1b）：战斗姿态 / 变体进空中键，按"先去变体、再去武器族、再去姿态"回落 ----------

        [Test]
        public void AirKeys_CarryCombatStanceAndVariant_AndFallBackOneDimensionAtATime()
        {
            using var rig = new Rig("idle", "jump", "jump.rise", "jump.rise.combat", "jump.rise.combat.wounded");
            rig.Bus.PublishImmediate(new CombatEnteredEvent(Entity));
            rig.Selector.SetVariant(Entity, "wounded");
            rig.Selector.SetAirPhase(Entity, AirPhase.Rise);
            Assert.AreEqual(new Id("clip.jump.rise.combat.wounded"), rig.Last, "战斗 + 变体：最具体的键");

            rig.Selector.SetVariant(Entity, null);
            rig.Selector.SetAirPhase(Entity, AirPhase.None);
            rig.Selector.SetAirPhase(Entity, AirPhase.Rise);
            Assert.AreEqual(new Id("clip.jump.rise.combat"), rig.Last, "没有变体：去变体");

            using var peace = new Rig("idle", "jump", "jump.rise", "jump.rise.combat");
            peace.Selector.SetAirPhase(Entity, AirPhase.Rise);
            Assert.AreEqual(new Id("clip.jump.rise"), peace.Last, "和平姿态：不带 .combat（与改动前一致）");
        }

        // ---------- 三、不变量：没有空中阶段 ----------

        [Test]
        public void WithoutAnyAirPhase_RequestsAreTheGroundOnes_AndJumpHasNoDefaultClip()
        {
            using var rig = new Rig("idle", "attack", "attack.air", "hit", "hit.air", "jump.rise");
            rig.Machine.RequestOverride(Entity, AnimState.Attack);
            Assert.AreEqual(new Id("clip.attack"), rig.Last);
            rig.Machine.NotifyTransientStateFinished(Entity, AnimState.Attack);
            rig.Machine.RequestOverride(Entity, AnimState.Hit);
            Assert.AreEqual(new Id("clip.hit"), rig.Last);
            rig.Machine.NotifyTransientStateFinished(Entity, AnimState.Hit);

            // 游戏自己 RequestOverride(Jump)、没有空中阶段：按改动前的解析（表里没有无后缀的 jump 键 -> 什么都不播）。
            var before = rig.Played.Count;
            rig.Machine.RequestOverride(Entity, AnimState.Jump);
            Assert.AreEqual(before, rig.Played.Count);
        }

        // ---------- 三点五、NF2：无后缀 jump 键进默认键表 ----------

        private static Core.Foundation.DisplayInfo.AnimSetDef AnimSet(params string[] keys)
        {
            var clips = new Dictionary<string, Core.Foundation.DisplayInfo.AnimClipDef>();
            foreach (var key in keys)
            {
                clips[key] = new Core.Foundation.DisplayInfo.AnimClipDef(new Id("sprite_anim.nf2_" + key.Replace('.', '_')));
            }

            return new Core.Foundation.DisplayInfo.AnimSetDef(new Id("display.anim_set.nf2_jump"), clips);
        }

        [Test]
        public void AnimStateKeysFor_PlainJumpDeclared_IsRegistered_RightAfterTheBaseKeys_AndAirFallbackChainReachesIt()
        {
            // 复现：外形声明了无后缀 jump 时，改动前键表里没有它（表现为 jump.rise 缺失的回落链一路掉到 idle）。
            // 期望由规则算出：键表 = 基础六键 + jump（声明了才有）+ 其余维度键；回落链沿用既有固定链。
            var withJump = UnityViewFactory.AnimStateKeysFor(AnimSet("idle", "move", "attack", "cast", "hit", "death", "jump"));
            var baseKeys = new[] { "idle", "move", "attack", "cast", "hit", "death" };
            CollectionAssert.AreEqual(new List<string>(baseKeys) { "jump" }, new List<string>(withJump));

            // 经解析层：键表里有 jump，空中上升键缺失时回落到 jump（不再掉到 idle）。
            using var rig = new Rig(new List<string>(withJump).ToArray());
            rig.Selector.SetAirPhase(Entity, AirPhase.Rise);
            Assert.AreEqual(new Id("clip.jump"), rig.Last);
        }

        [Test]
        public void AnimStateKeysFor_NoJumpDeclared_IsExactlyTheOldBaseKeys_AndVariantKeysKeepTheirPlace()
        {
            // 不变量：没声明 jump 的外形键表与改动前逐项一致（基础六键，其后依次是战斗变体键、姿势维度键）。
            var plain = UnityViewFactory.AnimStateKeysFor(AnimSet("idle", "move", "attack", "cast", "hit", "death"));
            CollectionAssert.AreEqual(new[] { "idle", "move", "attack", "cast", "hit", "death" }, new List<string>(plain));

            var variants = UnityViewFactory.AnimStateKeysFor(AnimSet("idle", "move", "attack", "cast", "hit", "death", "combat_idle", "jump.rise"));
            CollectionAssert.AreEqual(new[] { "idle", "move", "attack", "cast", "hit", "death", "combat_idle", "jump.rise" }, new List<string>(variants));

            CollectionAssert.AreEqual(new[] { "idle", "move", "attack", "cast", "hit", "death" }, new List<string>(UnityViewFactory.AnimStateKeysFor(null)));
        }

        // ---------- 四、工厂接线 ----------

        [Test]
        public void ViewFactory_AttachesThePoseSourceToItsStateMachine_AirPhaseDrivesJump()
        {
            var rootGo = new GameObject("AirPoseFactoryRoot");
            try
            {
                var loader = new UnityResourceLoader();
                var renderer = new UnityRenderer2D(rootGo.transform, loader);
                var displayInfo = new FakeDisplayInfoRegistryForAnim();
                var info = DisplayInfoTestSupportForAnimTests.CreateSpriteInfo();
                displayInfo.Add(info);
                var factory = new UnityViewFactory(renderer, new RenderConventionHost(), displayInfo, loader, bus: NewBus(), dataRegistry: null);
                var selector = new PoseSelector();
                ((IPoseContextReceiver)factory).SetPoseContextSource(selector);

                var entityId = new Id("unit.air_pose_factory");
                factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
                var machine = factory.AnimStateMachine;
                Assert.IsNotNull(machine);
                Assert.AreEqual(AnimState.Idle, machine!.GetState(entityId));

                selector.SetAirPhase(entityId, AirPhase.Rise);
                Assert.AreEqual(AnimState.Jump, machine.GetState(entityId), "腾空 -> 状态机进 Jump");
                selector.SetAirPhase(entityId, AirPhase.None);
                Assert.AreEqual(AnimState.Idle, machine.GetState(entityId), "阶段清除 -> 回运动态");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rootGo);
            }
        }
    }
}
