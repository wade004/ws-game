#nullable enable
// CombatStanceAnimInvariantTests：ADR-0111（消费方反馈第六十一批"战斗待机"）不变量用例——一支 UnityTest，
// 各分支用独立实体（同既有 ADR-0100/0101 系列 Invariant 用例一贯惯例），全部断言运行时可观测结果：
//   ① 没有变体键的外形进出战：剪辑不重播、时间轴不被打断（阳性对照：有变体键的外形同样操作会切换并从头播）；
//   ② 攻击中进战/脱战：不打断，攻击播完回落到"当时姿态"的待机（combat_idle / idle）；
//   ③ 冷加载一致：进战瞬间 combat_idle 逐层资源尚未加载完 -> 维持当前显示，加载完成后才切；期间已脱战 ->
//      加载完成后不得切到战斗待机（两种先后顺序，AGENTS.md 冷/热路径一致性）；
//   ④ 视图创建时单位已在战中：初始即 combat_idle（探针）；阳性对照：探针为 false、或外形没有变体键时与
//      1.89.0 一致（什么都不播）；
//   ⑤ 战斗待机中换向与换装：新方向/新装备层用 combat_idle 的逐层剪辑，时间轴按既有换向规则不重置，
//      重探测期间不掉回 idle（不闪）；
//   ⑥ 优先级：武器风格覆盖剪辑 > 战斗姿态变体键 > 普通键；
//   ⑦ 解析层（无播放器、model 路线同一张 "键 -> 剪辑" 表）：战斗中移动（没有 combat_move）播普通 move、停下
//      回 combat_idle；剪辑就绪探针的冷加载语义；无变体键外形进出战不产生任何多余播放。
//
// 第六十二批（ADR-0111 相邻缺陷，复活 = 显示复位）：Invariant_Respawn_* 一支用例，各分支独立实体——
//   ⑧ 复活时在战 + 变体已就绪 -> combat_idle 那一套，全程无死亡帧；
//   ⑨ 复活时在战 + 变体未就绪（冷）-> 先 idle（立刻站起来），加载完成后切 combat_idle，全程无死亡帧；
//   ⑩ 外形没有任何 combat_* 键，复活 -> idle，从头播（帧回到 0），复活后立刻移动正常切到 move；
//   ⑪ 解析层播放计数：复位恰好播放一次，阳性对照 Refresh（旧路径）一次都不播；视图新建（非复活）无变体键零额外播放；
//      冷复位记账记实际播放的普通键，变体就绪后 Refresh 补切一次。
// "规则层已脱战/在战"用夹具的战斗探针（生产装配接 ICombatHost.IsInCombat）表达，同复现用例。
using System.Collections;
using System.Collections.Generic;
using Adapter.Unity.Presentation;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using NUnit.Framework;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class CombatStanceAnimInvariantTests : CombatStanceAnimFixtureBase
    {
        private Id WriteArt(string? meshRef, string clipName, string dir, string layerName)
        {
            var id = LayerResourceId(meshRef, clipName, dir, layerName);
            WriteEffectResource(id, Color.red, Color.green);
            return id;
        }

        private static void Stand(Fx fx)
        {
            fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
            fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Walk", "Idle"));
        }

        private static AnimStateMachine Machine(Fx fx) => fx.Factory.AnimStateMachine!;

        /// <summary>有界等待到 <see cref="UnityFrameAnimPlayer.CurrentFrame"/> 等于 1（两帧剪辑第二帧），
        /// 不在此失败；返回后同一帧内调用方立即读数/发事件，中间不会有 Update 推进时间轴。</summary>
        private static IEnumerator WaitForFrameOne(UnityFrameAnimPlayer player)
        {
            yield return WaitBounded(() => player.CurrentFrame == 1, 3f);
            Assert.AreEqual(1, player.CurrentFrame, "前置条件：应当能等到剪辑播到第 1 帧（循环剪辑，4fps 两帧）");
        }

        [UnityTest]
        public IEnumerator Invariant_CombatStance_Variants_NoRestart_ColdLoad_InitialStance_ReprobeAndOverridePriority()
        {
            // =====================================================================================
            // ① 没有变体键的外形进出战：不重播、不打断时间轴；阳性对照：有变体键的外形会切换并从头播。
            // =====================================================================================
            {
                var idleBody = WriteArt(null, "h1n_idle", SideDir, "body");
                yield return WarmEffectCache(idleBody);
                var plain = BuildFixture("inv1_plain", new Dictionary<string, string> { ["idle"] = ResourceRefOf("h1n_idle") });
                Stand(plain);
                var idleClip = plain.DefaultClipId("idle");
                Assert.AreEqual(idleClip, plain.Player.CurrentClipId, "前置条件：站着播 idle");
                yield return WaitForFrameOne(plain.Player);

                plain.Bus.PublishImmediate(new CombatEnteredEvent(plain.EntityId));
                Assert.IsTrue(Machine(plain).IsInCombatStance(plain.EntityId), "进战事件应当被状态机记账（姿态确实变了）");
                Assert.AreEqual(idleClip, plain.Player.CurrentClipId, "①没有变体键：进战后仍是 idle 剪辑");
                Assert.AreEqual(1, plain.Player.CurrentFrame, "①没有变体键：进战不重播，时间轴不被打断（仍在第 1 帧）");
                plain.Bus.PublishImmediate(new CombatLeftEvent(plain.EntityId));
                Assert.IsFalse(Machine(plain).IsInCombatStance(plain.EntityId));
                Assert.AreEqual(idleClip, plain.Player.CurrentClipId);
                Assert.AreEqual(1, plain.Player.CurrentFrame, "①没有变体键：脱战同样不重播");

                var vIdle = WriteArt(null, "h1v_idle", SideDir, "body");
                var vCombat = WriteArt(null, "h1v_combat_idle", SideDir, "body");
                yield return WarmEffectCache(vIdle);
                yield return WarmEffectCache(vCombat);
                var variant = BuildFixture("inv1_variant", new Dictionary<string, string>
                {
                    ["idle"] = ResourceRefOf("h1v_idle"),
                    ["combat_idle"] = ResourceRefOf("h1v_combat_idle"),
                });
                Stand(variant);
                yield return WaitForFrameOne(variant.Player);

                variant.Bus.PublishImmediate(new CombatEnteredEvent(variant.EntityId));
                Assert.AreEqual(variant.DefaultClipId("combat_idle"), variant.Player.CurrentClipId, "①阳性对照：有变体键的外形进战切到 combat_idle 剪辑");
                Assert.AreEqual(0, variant.Player.CurrentFrame, "①阳性对照：切换后从头播");
                yield return WaitForFrameOne(variant.Player);
                variant.Bus.PublishImmediate(new CombatLeftEvent(variant.EntityId));
                Assert.AreEqual(variant.DefaultClipId("idle"), variant.Player.CurrentClipId, "①阳性对照：脱战切回 idle 剪辑");
                Assert.AreEqual(0, variant.Player.CurrentFrame);

                plain.EquipSource.Dispose();
                plain.View.Destroy();
                variant.EquipSource.Dispose();
                variant.View.Destroy();
            }

            // =====================================================================================
            // ② 攻击中进战：不打断，攻击播完回落到 combat_idle；战斗中攻击时脱战：攻击播完回落到 idle。
            // =====================================================================================
            {
                var idleBody = WriteArt(null, "h2_idle", SideDir, "body");
                var combatBody = WriteArt(null, "h2_combat_idle", SideDir, "body");
                var attackBody = WriteArt(null, "h2_attack", SideDir, "body");
                yield return WarmEffectCache(idleBody);
                yield return WarmEffectCache(combatBody);
                yield return WarmEffectCache(attackBody);
                var fx = BuildFixture("inv2", new Dictionary<string, string>
                {
                    ["idle"] = ResourceRefOf("h2_idle"),
                    ["combat_idle"] = ResourceRefOf("h2_combat_idle"),
                    ["attack"] = ResourceRefOf("h2_attack"),
                });
                Stand(fx);

                fx.Bus.PublishImmediate(new SkillCastStartEvent(fx.EntityId, new Id("skill.h2_auto"), 0.0));
                Assert.AreEqual(fx.DefaultClipId("attack"), fx.Player.CurrentClipId, "前置条件：攻击中");
                fx.Bus.PublishImmediate(new CombatEnteredEvent(fx.EntityId));
                Assert.AreEqual(AnimState.Attack, Machine(fx).GetState(fx.EntityId), "②攻击中进战：状态不被打断");
                Assert.AreEqual(fx.DefaultClipId("attack"), fx.Player.CurrentClipId, "②攻击中进战：攻击剪辑不被打断");

                yield return WaitBounded(() => fx.Player.CurrentClipId == fx.DefaultClipId("combat_idle"), 3f);
                Assert.AreEqual(fx.DefaultClipId("combat_idle"), fx.Player.CurrentClipId, "②攻击播完回落到 combat_idle（当时姿态），不是普通 idle");
                Assert.AreEqual(AnimState.Idle, Machine(fx).GetState(fx.EntityId));
                Assert.AreEqual(combatBody.Value, AppliedResource(fx, "body"), "②回落后身体层应用 combat_idle 逐层资源");

                fx.Bus.PublishImmediate(new SkillCastStartEvent(fx.EntityId, new Id("skill.h2_auto"), 0.0));
                Assert.AreEqual(fx.DefaultClipId("attack"), fx.Player.CurrentClipId);
                fx.Bus.PublishImmediate(new CombatLeftEvent(fx.EntityId));
                Assert.AreEqual(fx.DefaultClipId("attack"), fx.Player.CurrentClipId, "②战斗中攻击时脱战：攻击剪辑不被打断");
                yield return WaitBounded(() => fx.Player.CurrentClipId == fx.DefaultClipId("idle"), 3f);
                Assert.AreEqual(fx.DefaultClipId("idle"), fx.Player.CurrentClipId, "②攻击播完回落到 idle（脱战后的姿态）");
                Assert.AreEqual(idleBody.Value, AppliedResource(fx, "body"));

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // =====================================================================================
            // ③ 冷加载一致（两种先后顺序）：combat_idle 的逐层资源（身体 + 装备层）进战时尚未加载。
            //    夹具用独立 UnityResourceLoader，不调用 Tick 就不会有任何异步加载完成。
            // =====================================================================================
            for (var order = 0; order < 2; order++)
            {
                var enterThenLoad = order == 0;
                var tag = enterThenLoad ? "inv3a" : "inv3b";
                var clipPrefix = enterThenLoad ? "h3a" : "h3b";
                const string mesh = "mesh.w61c";
                var idleBody = WriteArt(null, clipPrefix + "_idle", SideDir, "body");
                var idleWeapon = WriteArt(mesh, clipPrefix + "_idle", SideDir, "mainhand");
                var combatBody = WriteArt(null, clipPrefix + "_combat_idle", SideDir, "body");
                var combatWeapon = WriteArt(mesh, clipPrefix + "_combat_idle", SideDir, "mainhand");
                yield return WarmEffectCache(idleBody);
                yield return WarmEffectCache(idleWeapon);

                var fx = BuildFixture(tag, new Dictionary<string, string>
                {
                    ["idle"] = ResourceRefOf(clipPrefix + "_idle"),
                    ["combat_idle"] = ResourceRefOf(clipPrefix + "_combat_idle"),
                });
                EquipMesh(fx, tag + "_w", "slot.mainhand", mesh);
                Stand(fx);
                yield return WaitBounded(() => AppliedResource(fx, "body") == idleBody.Value && AppliedResource(fx, "mainhand") == idleWeapon.Value);
                Assert.AreEqual(idleBody.Value, AppliedResource(fx, "body"), "前置条件：站着身体层应用 idle");
                Assert.AreEqual(idleWeapon.Value, AppliedResource(fx, "mainhand"), "前置条件：站着装备层应用 idle");
                Assert.IsFalse(Loader.TryGetEffect(combatBody, out _), "前置条件：combat_idle 身体层资源尚未加载（冷）");

                fx.Bus.PublishImmediate(new CombatEnteredEvent(fx.EntityId));
                yield return null;
                yield return null;
                Assert.IsTrue(Machine(fx).IsInCombatStance(fx.EntityId));
                Assert.AreEqual(fx.DefaultClipId("idle"), fx.Player.CurrentClipId, $"③({tag})进战时 combat_idle 尚未加载完：维持当前 idle 剪辑");
                Assert.AreEqual(idleBody.Value, AppliedResource(fx, "body"), $"③({tag})冷加载中：身体层维持当前显示");
                Assert.AreEqual(idleWeapon.Value, AppliedResource(fx, "mainhand"), $"③({tag})冷加载中：装备层维持当前显示");

                if (enterThenLoad)
                {
                    yield return PumpUntilCached(combatBody, combatWeapon);
                    yield return WaitBounded(() => AppliedResource(fx, "body") == combatBody.Value && AppliedResource(fx, "mainhand") == combatWeapon.Value);
                    Assert.AreEqual(combatBody.Value, AppliedResource(fx, "body"), "③进战后加载完成：身体层切到 combat_idle（与热路径同一结果）");
                    Assert.AreEqual(combatWeapon.Value, AppliedResource(fx, "mainhand"), "③进战后加载完成：装备层切到 combat_idle");
                    Assert.AreEqual(fx.DefaultClipId("combat_idle"), fx.Player.CurrentClipId);

                    fx.Bus.PublishImmediate(new CombatLeftEvent(fx.EntityId));
                    Assert.AreEqual(fx.DefaultClipId("idle"), fx.Player.CurrentClipId, "③脱战：立即切回 idle");
                    yield return WaitBounded(() => AppliedResource(fx, "body") == idleBody.Value && AppliedResource(fx, "mainhand") == idleWeapon.Value);
                    Assert.AreEqual(idleBody.Value, AppliedResource(fx, "body"));
                    Assert.AreEqual(idleWeapon.Value, AppliedResource(fx, "mainhand"));
                }
                else
                {
                    fx.Bus.PublishImmediate(new CombatLeftEvent(fx.EntityId));
                    Assert.IsFalse(Machine(fx).IsInCombatStance(fx.EntityId));
                    yield return PumpUntilCached(combatBody, combatWeapon);
                    yield return PumpFrames(8);
                    var combatClip = fx.DefaultClipId("combat_idle");
                    Assert.IsTrue(fx.Player.HasRealContent(combatClip), "③进战后又脱战：combat_idle 内容确实已经加载到位（下面的断言才有意义）");
                    Assert.AreEqual(fx.DefaultClipId("idle"), fx.Player.CurrentClipId, "③期间已脱战：加载完成后不得切到战斗待机");
                    Assert.AreEqual(idleBody.Value, AppliedResource(fx, "body"), "③期间已脱战：身体层仍是 idle");
                    Assert.AreEqual(idleWeapon.Value, AppliedResource(fx, "mainhand"), "③期间已脱战：装备层仍是 idle");
                }

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // =====================================================================================
            // ④ 视图创建时单位已在战中：初始即 combat_idle（探针）。阳性对照：探针 false、无变体键外形。
            // =====================================================================================
            {
                const string mesh = "mesh.w61d";
                var idleBody = WriteArt(null, "h4_idle", SideDir, "body");
                var idleWeapon = WriteArt(mesh, "h4_idle", SideDir, "mainhand");
                var combatBody = WriteArt(null, "h4_combat_idle", SideDir, "body");
                var combatWeapon = WriteArt(mesh, "h4_combat_idle", SideDir, "mainhand");
                yield return WarmEffectCache(idleBody);
                yield return WarmEffectCache(idleWeapon);
                yield return WarmEffectCache(combatBody);
                yield return WarmEffectCache(combatWeapon);
                var clips = new Dictionary<string, string>
                {
                    ["idle"] = ResourceRefOf("h4_idle"),
                    ["combat_idle"] = ResourceRefOf("h4_combat_idle"),
                };

                var inCombat = BuildFixture("inv4_in", clips, configureFactory: f => f.CombatProbe = id => true);
                Assert.IsTrue(Machine(inCombat).IsInCombatStance(inCombat.EntityId), "④视图创建时探针说已在战中：初始姿态为战斗");
                yield return WaitBounded(() => inCombat.Player.CurrentClipId == inCombat.DefaultClipId("combat_idle"));
                Assert.AreEqual(inCombat.DefaultClipId("combat_idle"), inCombat.Player.CurrentClipId, "④初始即 combat_idle 剪辑");
                EquipMesh(inCombat, "inv4_w", "slot.mainhand", mesh);
                yield return WaitBounded(() => AppliedResource(inCombat, "body") == combatBody.Value && AppliedResource(inCombat, "mainhand") == combatWeapon.Value);
                Assert.AreEqual(combatBody.Value, AppliedResource(inCombat, "body"), "④初始战斗待机：身体层应用 combat_idle 逐层资源");
                Assert.AreEqual(combatWeapon.Value, AppliedResource(inCombat, "mainhand"), "④初始战斗待机：装备层应用 combat_idle 逐层资源");
                // 已经在战中的单位随后收到 combat.left：应切回 idle（初始姿态是"视图已知的基线"，此后增量正确）。
                inCombat.Bus.PublishImmediate(new CombatLeftEvent(inCombat.EntityId));
                Assert.AreEqual(inCombat.DefaultClipId("idle"), inCombat.Player.CurrentClipId, "④初始在战中、随后脱战：切回 idle");

                var notInCombat = BuildFixture("inv4_out", clips, configureFactory: f => f.CombatProbe = id => false);
                Assert.IsFalse(Machine(notInCombat).IsInCombatStance(notInCombat.EntityId));
                Assert.IsNull(notInCombat.Player.CurrentClipId, "④阳性对照：探针 false 时与 1.89.0 一致，视图创建时什么都不播");

                var noVariant = BuildFixture("inv4_novariant",
                    new Dictionary<string, string> { ["idle"] = ResourceRefOf("h4_idle") },
                    configureFactory: f => f.CombatProbe = id => true);
                Assert.IsTrue(Machine(noVariant).IsInCombatStance(noVariant.EntityId));
                Assert.IsNull(noVariant.Player.CurrentClipId, "④阳性对照：外形没有变体键时，即使已在战中也与 1.89.0 一致，什么都不播");

                inCombat.EquipSource.Dispose();
                inCombat.View.Destroy();
                notInCombat.EquipSource.Dispose();
                notInCombat.View.Destroy();
                noVariant.EquipSource.Dispose();
                noVariant.View.Destroy();
            }

            // =====================================================================================
            // ⑤ 战斗待机中换向与换装：新方向/新装备层用 combat_idle 逐层剪辑；换向不重置时间轴；重探测不闪回 idle。
            // =====================================================================================
            {
                const string meshA = "mesh.w61ea";
                const string meshB = "mesh.w61eb";
                var arts = new List<Id>
                {
                    WriteArt(null, "h5_idle", SideDir, "body"), WriteArt(null, "h5_idle", FrontDir, "body"),
                    WriteArt(null, "h5_combat_idle", SideDir, "body"), WriteArt(null, "h5_combat_idle", FrontDir, "body"),
                    WriteArt(meshA, "h5_idle", SideDir, "mainhand"), WriteArt(meshA, "h5_idle", FrontDir, "mainhand"),
                    WriteArt(meshA, "h5_combat_idle", SideDir, "mainhand"), WriteArt(meshA, "h5_combat_idle", FrontDir, "mainhand"),
                    WriteArt(meshB, "h5_idle", SideDir, "mainhand"), WriteArt(meshB, "h5_idle", FrontDir, "mainhand"),
                    WriteArt(meshB, "h5_combat_idle", SideDir, "mainhand"), WriteArt(meshB, "h5_combat_idle", FrontDir, "mainhand"),
                };
                for (var i = 0; i < arts.Count; i++)
                {
                    yield return WarmEffectCache(arts[i]);
                }

                var fx = BuildFixture("inv5", new Dictionary<string, string>
                {
                    ["idle"] = ResourceRefOf("h5_idle"),
                    ["combat_idle"] = ResourceRefOf("h5_combat_idle"),
                });
                EquipMesh(fx, "inv5_a", "slot.mainhand", meshA);
                Stand(fx);
                fx.Bus.PublishImmediate(new CombatEnteredEvent(fx.EntityId));
                var combatClip = fx.DefaultClipId("combat_idle");
                var idleClip = fx.DefaultClipId("idle");
                Assert.AreEqual(combatClip, fx.Player.CurrentClipId, "前置条件：战斗待机中");

                // 换向：front。时间轴按既有换向规则不重置（同一 clip id 原地升级内容，不重新 Play）。
                yield return WaitForFrameOne(fx.Player);
                fx.View.SyncPose(Vec2.Zero, Direction.FromQuantized(System.Math.PI / 2.0, 8), height: 0.0);
                Isolate();
                Assert.AreEqual(combatClip, fx.Player.CurrentClipId, "⑤换向：仍是 combat_idle 剪辑（没有掉回 idle）");
                Assert.AreEqual(1, fx.Player.CurrentFrame, "⑤换向：时间轴按既有换向规则保持，不重置");
                var frontBody = LayerResourceId(null, "h5_combat_idle", FrontDir, "body");
                var frontWeaponA = LayerResourceId(meshA, "h5_combat_idle", FrontDir, "mainhand");
                yield return WaitBounded(() => AppliedResource(fx, "body") == frontBody.Value && AppliedResource(fx, "mainhand") == frontWeaponA.Value);
                Assert.AreEqual(frontBody.Value, AppliedResource(fx, "body"), "⑤换向后身体层应用新方向的 combat_idle 逐层资源");
                Assert.AreEqual(frontWeaponA.Value, AppliedResource(fx, "mainhand"), "⑤换向后装备层应用新方向的 combat_idle 逐层资源");

                // 换装：同槽位换成 B。重探测期间任何一帧都不许掉回 idle 剪辑。
                EquipMesh(fx, "inv5_b", "slot.mainhand", meshB);
                var frontWeaponB = LayerResourceId(meshB, "h5_combat_idle", FrontDir, "mainhand");
                var droppedToIdle = false;
                var elapsed = 0f;
                while (AppliedResource(fx, "mainhand") != frontWeaponB.Value && elapsed < 3f)
                {
                    droppedToIdle |= fx.Player.CurrentClipId == idleClip;
                    yield return null;
                    elapsed += Time.unscaledDeltaTime;
                }
                Assert.AreEqual(frontWeaponB.Value, AppliedResource(fx, "mainhand"), "⑤换装后新装备层应用 combat_idle 逐层资源");
                Assert.AreEqual(frontBody.Value, AppliedResource(fx, "body"), "⑤换装不影响身体层");
                Assert.IsFalse(droppedToIdle, "⑤换装重探测期间不得掉回 idle 剪辑（不闪）");
                Assert.AreEqual(combatClip, fx.Player.CurrentClipId);

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // =====================================================================================
            // ⑥ 优先级：武器风格覆盖 > 战斗姿态变体键 > 普通键。
            // =====================================================================================
            {
                var swingClipName = "h6_swing";
                var arts = new List<Id>();
                foreach (var name in new[] { "h6_idle", "h6_combat_idle", "h6_attack", "h6_combat_attack", "h6_cast", "h6_combat_cast", swingClipName })
                {
                    arts.Add(WriteArt(null, name, SideDir, "body"));
                }
                for (var i = 0; i < arts.Count; i++)
                {
                    yield return WarmEffectCache(arts[i]);
                }

                var swingClipId = new Id(ResourceRefOf(swingClipName));
                var fx = BuildFixture("inv6", new Dictionary<string, string>
                {
                    ["idle"] = ResourceRefOf("h6_idle"),
                    ["combat_idle"] = ResourceRefOf("h6_combat_idle"),
                    ["attack"] = ResourceRefOf("h6_attack"),
                    ["combat_attack"] = ResourceRefOf("h6_combat_attack"),
                    ["cast"] = ResourceRefOf("h6_cast"),
                    ["combat_cast"] = ResourceRefOf("h6_combat_cast"),
                }, weaponStyleAutoAttack: swingClipId);
                Stand(fx);
                fx.Bus.PublishImmediate(new CombatEnteredEvent(fx.EntityId));
                Assert.AreEqual(fx.DefaultClipId("combat_idle"), fx.Player.CurrentClipId);

                fx.Bus.PublishImmediate(new SkillCastStartEvent(fx.EntityId, new Id("skill.h6_auto"), 0.0));
                Assert.AreEqual(swingClipId, fx.Player.CurrentClipId, "⑥战斗中攻击：武器风格覆盖剪辑优先于 combat_attack 变体键");
                Assert.AreNotEqual(fx.DefaultClipId("combat_attack"), fx.Player.CurrentClipId);
                yield return WaitBounded(() => fx.Player.CurrentClipId == fx.DefaultClipId("combat_idle"), 3f);
                Assert.AreEqual(fx.DefaultClipId("combat_idle"), fx.Player.CurrentClipId, "⑥攻击结束回 combat_idle");

                var castSkill = new Id("skill.h6_fire");
                fx.Bus.PublishImmediate(new SkillCastStartEvent(fx.EntityId, castSkill, 1.0));
                Assert.AreEqual(fx.DefaultClipId("combat_cast"), fx.Player.CurrentClipId, "⑥战斗中施法（无覆盖）：变体键 combat_cast 优先于普通键 cast");
                fx.Bus.PublishImmediate(new SkillCastSuccessEvent(fx.EntityId, castSkill, System.Array.Empty<Id>()));
                Assert.AreEqual(fx.DefaultClipId("combat_idle"), fx.Player.CurrentClipId, "⑥施法结束回 combat_idle");

                fx.Bus.PublishImmediate(new CombatLeftEvent(fx.EntityId));
                Assert.AreEqual(fx.DefaultClipId("idle"), fx.Player.CurrentClipId);
                fx.Bus.PublishImmediate(new SkillCastStartEvent(fx.EntityId, castSkill, 1.0));
                Assert.AreEqual(fx.DefaultClipId("cast"), fx.Player.CurrentClipId, "⑥脱战后施法：普通键 cast");
                fx.Bus.PublishImmediate(new SkillCastSuccessEvent(fx.EntityId, castSkill, System.Array.Empty<Id>()));

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // =====================================================================================
            // ⑦ 解析层（无播放器；model 路线走同一张"键 -> 剪辑"表，同一个 AnimClipResolver）。
            // =====================================================================================
            {
                var bus = NewBus();
                var entity = new Id("unit.inv7");
                var idle = new Id("clip.idle");
                var combatIdle = new Id("clip.combat_idle");
                var move = new Id("clip.move");
                var table = new Dictionary<string, Id>
                {
                    ["idle"] = idle,
                    ["move"] = move,
                    [AnimClipResolver.CombatStateKey(AnimState.Idle)] = combatIdle,
                };
                var calls = new List<(Id Clip, bool Loop)>();
                var ready = true;
                using var machine = new AnimStateMachine(bus);
                using var resolver = new AnimClipResolver(
                    machine, id => table, (id, clip, loop, speed) => calls.Add((clip, loop)),
                    weaponStyleSource: null, weaponStyles: null, isClipReady: (id, clip) => ready);
                machine.Track(entity);

                // 战斗中移动（没有 combat_move）：播普通 move；停下回到 combat_idle。
                bus.PublishImmediate(new CombatEnteredEvent(entity));
                Assert.AreEqual(new[] { (combatIdle, true) }, calls, "⑦站着进战：立即切到 combat_idle（循环）");
                bus.PublishImmediate(new UnitStateChangedEvent(entity, "Idle", "Walk"));
                Assert.AreEqual((move, true), calls[calls.Count - 1], "⑦战斗中移动：外形没有 combat_move，回落普通 move");
                bus.PublishImmediate(new UnitStateChangedEvent(entity, "Walk", "Idle"));
                Assert.AreEqual((combatIdle, true), calls[calls.Count - 1], "⑦停下回到 combat_idle");
                var beforeRedundant = calls.Count;
                bus.PublishImmediate(new CombatEnteredEvent(entity));
                Assert.AreEqual(beforeRedundant, calls.Count, "⑦重复进战事件不产生任何播放");
                bus.PublishImmediate(new CombatLeftEvent(entity));
                Assert.AreEqual((idle, true), calls[calls.Count - 1], "⑦站着脱战：立即切回 idle");

                // 剪辑就绪探针：变体内容未就绪时不切（姿态切换什么都不做），就绪后 Refresh 补切。
                calls.Clear();
                ready = false;
                bus.PublishImmediate(new CombatEnteredEvent(entity));
                Assert.AreEqual(0, calls.Count, "⑦变体未就绪：进战不切（维持当前显示）");
                ready = true;
                resolver.Refresh(entity);
                Assert.AreEqual(new[] { (combatIdle, true) }, calls, "⑦变体就绪后补切");
                resolver.Refresh(entity);
                Assert.AreEqual(1, calls.Count, "⑦Refresh 幂等：已在播的变体不重播");
                // 就绪探针在重探测期间变回 false：已经在播变体时不得被踢回普通键（不闪）。
                ready = false;
                resolver.Refresh(entity);
                Assert.AreEqual(1, calls.Count, "⑦已在播变体：探针暂时 false 不导致回落");
                bus.PublishImmediate(new CombatLeftEvent(entity));
                ready = false;
                calls.Clear();
                bus.PublishImmediate(new CombatEnteredEvent(entity));
                bus.PublishImmediate(new CombatLeftEvent(entity));
                ready = true;
                resolver.Refresh(entity);
                Assert.AreEqual(0, calls.Count, "⑦进战后加载完成前已脱战：加载完成后不切战斗待机");

                // 外形完全没有变体键：进出战不产生任何多余播放（含从未播放过任何剪辑的实体）。
                var plainTable = new Dictionary<string, Id> { ["idle"] = idle, ["move"] = move };
                var plainCalls = new List<(Id Clip, bool Loop)>();
                var other = new Id("unit.inv7_plain");
                using var plainResolver = new AnimClipResolver(
                    machine, id => plainTable, (id, clip, loop, speed) => plainCalls.Add((clip, loop)));
                machine.Track(other);
                bus.PublishImmediate(new CombatEnteredEvent(other));
                bus.PublishImmediate(new CombatLeftEvent(other));
                bus.PublishImmediate(new UnitStateChangedEvent(other, "Idle", "Walk"));
                bus.PublishImmediate(new CombatEnteredEvent(other));
                bus.PublishImmediate(new CombatLeftEvent(other));
                Assert.AreEqual(new[] { (move, true) }, plainCalls, "⑦无变体键外形：只有移动触发的那一次播放，进出战不产生任何多余播放");
            }
        }

        private sealed class DeathSampler
        {
            public bool SawDeath;
        }

        /// <summary>打死并等死亡剪辑播完（两层都停在死亡资源上），返回后单位停在倒地末帧。</summary>
        private IEnumerator DieAndFinish(Fx fx, string deathBody, string? deathWeapon)
        {
            var finished = false;
            fx.Player.OnComplete(() => finished = true);
            fx.Bus.PublishImmediate(new UnitDiedEvent(fx.EntityId, new Id("unit.attacker")));
            yield return WaitBounded(() => finished && AppliedResource(fx, "body") == deathBody
                && (deathWeapon == null || AppliedResource(fx, "mainhand") == deathWeapon));
            Assert.IsTrue(finished, "前置条件：死亡剪辑应当播放完毕");
            Assert.AreEqual(deathBody, AppliedResource(fx, "body"), "前置条件：死亡后身体层应用死亡剪辑");
        }

        /// <summary>从当前帧起逐帧采样 <paramref name="frames"/> 帧，记录是否出现过死亡资源。</summary>
        private IEnumerator SampleDeath(Fx fx, DeathSampler sampler, string deathBody, string? deathWeapon, int frames, bool pumpLoader = false)
        {
            for (var i = 0; i < frames; i++)
            {
                sampler.SawDeath |= AppliedResource(fx, "body") == deathBody
                    || (deathWeapon != null && AppliedResource(fx, "mainhand") == deathWeapon);
                if (pumpLoader)
                {
                    Loader.Tick();
                }
                yield return null;
            }
            sampler.SawDeath |= AppliedResource(fx, "body") == deathBody
                || (deathWeapon != null && AppliedResource(fx, "mainhand") == deathWeapon);
        }

        private static void Respawn(Fx fx) =>
            fx.Bus.PublishImmediate(new UnitRespawnedEvent(fx.EntityId, Core.Rules.Common.RespawnPolicy.RespawnPoint));

        [UnityTest]
        public IEnumerator Invariant_Respawn_ResetsDisplayToLocomotionClip_HotCold_NoVariant_Move_PlayCounts()
        {
            // =====================================================================================
            // ⑧ 复活时在战 + 变体已就绪：combat_idle 那一套，全程无死亡帧。
            // =====================================================================================
            {
                const string mesh = "mesh.w62a";
                var idleB = WriteArt(null, "h62a_idle", SideDir, "body");
                var idleW = WriteArt(mesh, "h62a_idle", SideDir, "mainhand");
                var combatB = WriteArt(null, "h62a_combat_idle", SideDir, "body");
                var combatW = WriteArt(mesh, "h62a_combat_idle", SideDir, "mainhand");
                var deathB = WriteArt(null, "h62a_death", SideDir, "body");
                var deathW = WriteArt(mesh, "h62a_death", SideDir, "mainhand");
                foreach (var id in new[] { idleB, idleW, combatB, combatW, deathB, deathW })
                {
                    yield return WarmEffectCache(id);
                }

                var inCombat = true;
                var fx = BuildFixture("inv62a", new Dictionary<string, string>
                {
                    ["idle"] = ResourceRefOf("h62a_idle"),
                    ["combat_idle"] = ResourceRefOf("h62a_combat_idle"),
                    ["death"] = ResourceRefOf("h62a_death"),
                }, configureFactory: f => f.CombatProbe = id => inCombat);
                EquipMesh(fx, "inv62a_w", "slot.mainhand", mesh);
                yield return DieAndFinish(fx, deathB.Value, deathW.Value);

                Respawn(fx);
                var sampler = new DeathSampler();
                Assert.AreEqual(fx.DefaultClipId("combat_idle"), fx.Player.CurrentClipId, "⑧复活（在战、变体就绪）当帧起就是 combat_idle 剪辑");
                yield return SampleDeath(fx, sampler, deathB.Value, deathW.Value, 30);
                Assert.AreEqual(combatB.Value, AppliedResource(fx, "body"), "⑧身体层应用 combat_idle 那一套");
                Assert.AreEqual(combatW.Value, AppliedResource(fx, "mainhand"), "⑧装备层应用 combat_idle 那一套");
                Assert.IsFalse(sampler.SawDeath, "⑧复活后不应出现死亡剪辑的资源");

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // =====================================================================================
            // ⑨ 复活时在战 + 变体未就绪（冷）：先 idle（立刻站起来），加载完成后切 combat_idle，全程无死亡帧。
            //    combat_idle 资源写盘但不预热；夹具的独立加载器不 Tick 就不会有任何异步加载完成。
            // =====================================================================================
            {
                const string mesh = "mesh.w62b";
                var idleB = WriteArt(null, "h62b_idle", SideDir, "body");
                var idleW = WriteArt(mesh, "h62b_idle", SideDir, "mainhand");
                var combatB = WriteArt(null, "h62b_combat_idle", SideDir, "body");
                var combatW = WriteArt(mesh, "h62b_combat_idle", SideDir, "mainhand");
                var deathB = WriteArt(null, "h62b_death", SideDir, "body");
                var deathW = WriteArt(mesh, "h62b_death", SideDir, "mainhand");
                foreach (var id in new[] { idleB, idleW, deathB, deathW })
                {
                    yield return WarmEffectCache(id);
                }

                var inCombat = false;
                var fx = BuildFixture("inv62b", new Dictionary<string, string>
                {
                    ["idle"] = ResourceRefOf("h62b_idle"),
                    ["combat_idle"] = ResourceRefOf("h62b_combat_idle"),
                    ["death"] = ResourceRefOf("h62b_death"),
                }, configureFactory: f => f.CombatProbe = id => inCombat);
                EquipMesh(fx, "inv62b_w", "slot.mainhand", mesh);
                yield return DieAndFinish(fx, deathB.Value, deathW.Value);
                Assert.IsFalse(Loader.TryGetEffect(combatB, out _), "前置条件：combat_idle 身体层资源尚未加载（冷）");

                inCombat = true;
                Respawn(fx);
                var sampler = new DeathSampler();
                Assert.AreEqual(fx.DefaultClipId("idle"), fx.Player.CurrentClipId, "⑨变体没就绪：复活当帧先播普通 idle，立刻站起来");
                yield return SampleDeath(fx, sampler, deathB.Value, deathW.Value, 10);
                Assert.AreEqual(idleB.Value, AppliedResource(fx, "body"), "⑨冷加载期间身体层是 idle 那一套（不是死亡末帧）");
                Assert.AreEqual(idleW.Value, AppliedResource(fx, "mainhand"), "⑨冷加载期间装备层是 idle 那一套");

                yield return PumpUntilCached(combatB, combatW);
                yield return WaitBounded(() => AppliedResource(fx, "body") == combatB.Value && AppliedResource(fx, "mainhand") == combatW.Value);
                yield return SampleDeath(fx, sampler, deathB.Value, deathW.Value, 5, pumpLoader: true);
                Assert.AreEqual(fx.DefaultClipId("combat_idle"), fx.Player.CurrentClipId, "⑨变体就绪后切 combat_idle");
                Assert.AreEqual(combatB.Value, AppliedResource(fx, "body"), "⑨加载完成后身体层切到 combat_idle");
                Assert.AreEqual(combatW.Value, AppliedResource(fx, "mainhand"), "⑨加载完成后装备层切到 combat_idle");
                Assert.IsFalse(sampler.SawDeath, "⑨全程不应出现死亡剪辑的资源");

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // =====================================================================================
            // ⑩ 外形没有任何 combat_* 键：复活 -> idle 从头播；复活后立刻移动正常切到 move，无残留。
            // =====================================================================================
            {
                var idleB = WriteArt(null, "h62c_idle", SideDir, "body");
                var moveB = WriteArt(null, "h62c_move", SideDir, "body");
                var deathB = WriteArt(null, "h62c_death", SideDir, "body");
                foreach (var id in new[] { idleB, moveB, deathB })
                {
                    yield return WarmEffectCache(id);
                }

                var fx = BuildFixture("inv62c", new Dictionary<string, string>
                {
                    ["idle"] = ResourceRefOf("h62c_idle"),
                    ["move"] = ResourceRefOf("h62c_move"),
                    ["death"] = ResourceRefOf("h62c_death"),
                });
                yield return DieAndFinish(fx, deathB.Value, null);

                Respawn(fx);
                Assert.AreEqual(fx.DefaultClipId("idle"), fx.Player.CurrentClipId, "⑩没有变体键：复活后是 idle 剪辑");
                Assert.AreEqual(0, fx.Player.CurrentFrame, "⑩复活重新从头播 idle（帧回到 0）");
                var sampler = new DeathSampler();
                yield return SampleDeath(fx, sampler, deathB.Value, null, 10);
                Assert.AreEqual(idleB.Value, AppliedResource(fx, "body"), "⑩复活后身体层是 idle 那一套");
                Assert.IsFalse(sampler.SawDeath, "⑩复活后不应出现死亡剪辑的资源");

                fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
                Assert.AreEqual(fx.DefaultClipId("move"), fx.Player.CurrentClipId, "⑩复活后立刻移动：切到 move 剪辑");
                yield return SampleDeath(fx, sampler, deathB.Value, null, 10);
                Assert.AreEqual(moveB.Value, AppliedResource(fx, "body"), "⑩移动时身体层是 move 那一套，无残留");
                Assert.IsFalse(sampler.SawDeath);

                fx.View.Destroy();
            }

            // =====================================================================================
            // ⑪ 解析层播放计数（无播放器）。
            // =====================================================================================
            {
                var bus = NewBus();
                var idle = new Id("clip.idle62");
                var combatIdle = new Id("clip.combat_idle62");
                var ready = true;
                var inCombatFlag = false;

                // 无变体键的外形：复位恰好播放一次；阳性对照——Refresh（旧路径）一次都不播；视图新建（Track + Refresh）零播放。
                var plainTable = new Dictionary<string, Id> { ["idle"] = idle };
                var plainCalls = new List<(Id Clip, bool Loop)>();
                using var plainMachine = new AnimStateMachine(bus, id => inCombatFlag);
                using var plainResolver = new AnimClipResolver(plainMachine, id => plainTable, (id, clip, loop, speed) => plainCalls.Add((clip, loop)));
                var fresh = new Id("unit.inv62_fresh");
                plainMachine.Track(fresh);
                plainResolver.Refresh(fresh);
                Assert.AreEqual(0, plainCalls.Count, "⑪视图新建（非复活）、无变体键：零额外播放");

                var refreshOnly = new Id("unit.inv62_refresh");
                plainMachine.Forget(refreshOnly);
                plainResolver.Forget(refreshOnly);
                plainMachine.Track(refreshOnly);
                plainResolver.Refresh(refreshOnly);
                Assert.AreEqual(0, plainCalls.Count, "⑪阳性对照：复活路径若仍走 Refresh，无变体键/已脱战什么都不播（缺陷本身）");

                var respawned = new Id("unit.inv62_respawn");
                bus.PublishImmediate(new UnitDiedEvent(respawned, new Id("unit.attacker")));
                Assert.AreEqual(AnimState.Death, plainMachine.GetState(respawned));
                plainMachine.Forget(respawned);
                plainResolver.Forget(respawned);
                plainMachine.Track(respawned);
                plainResolver.ResetToLocomotionClip(respawned);
                Assert.AreEqual(new[] { (idle, true) }, plainCalls, "⑪复位：恰好播放一次 idle（循环）");

                // 冷复位：在战但变体没就绪 -> 复位播普通键（记账记实际播放的），变体就绪后 Refresh 补切一次，再 Refresh 不重播。
                var variantTable = new Dictionary<string, Id> { ["idle"] = idle, [AnimClipResolver.CombatStateKey(AnimState.Idle)] = combatIdle };
                var variantCalls = new List<(Id Clip, bool Loop)>();
                inCombatFlag = true;
                using var variantMachine = new AnimStateMachine(bus, id => inCombatFlag);
                using var variantResolver = new AnimClipResolver(
                    variantMachine, id => variantTable, (id, clip, loop, speed) => variantCalls.Add((clip, loop)),
                    weaponStyleSource: null, weaponStyles: null, isClipReady: (id, clip) => ready);
                var cold = new Id("unit.inv62_cold");
                variantMachine.Track(cold);
                Assert.IsTrue(variantMachine.IsInCombatStance(cold));
                ready = false;
                variantResolver.ResetToLocomotionClip(cold);
                Assert.AreEqual(new[] { (idle, true) }, variantCalls, "⑪冷复位：变体没就绪先播普通 idle");
                variantResolver.Refresh(cold);
                Assert.AreEqual(1, variantCalls.Count, "⑪变体仍没就绪：Refresh 不动");
                ready = true;
                variantResolver.Refresh(cold);
                Assert.AreEqual(new[] { (idle, true), (combatIdle, true) }, variantCalls, "⑪变体就绪后补切一次");
                variantResolver.Refresh(cold);
                Assert.AreEqual(2, variantCalls.Count, "⑪补切之后 Refresh 幂等");

                // 热复位（变体就绪）：直接播变体一次。
                var hot = new Id("unit.inv62_hot");
                variantMachine.Track(hot);
                variantCalls.Clear();
                variantResolver.ResetToLocomotionClip(hot);
                Assert.AreEqual(new[] { (combatIdle, true) }, variantCalls, "⑪热复位：变体已就绪，直接播 combat_idle 恰好一次");
            }
        }

        private static IEventBus NewBus()
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, System.Array.Empty<string>()));
            }
            return new EventBus(EventCatalog.FromDefinitions(definitions), new EventBusOptions { StrictCatalog = false, AuditLog = false });
        }
    }
}
