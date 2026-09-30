#nullable enable
// CombatStanceAnimReproTests：ADR-0111（消费方反馈第六十一批）复现用例——生产装配级（UnityViewFactory +
// 真实 EventBus + 真实 UnityRenderer2D/UnityResourceLoader，纸娃娃层 + 装备层逐层剪辑）。
//
// 复现：外形 anim_set 声明 idle 与 combat_idle 两个剪辑，带一个装备层（mainhand 武器）。单位站着（idle 逐层
// 剪辑在播），发 combat.entered——身体层与装备层实际应用的资源必须是 combat_idle 那一套；发 combat.left——
// 必须切回 idle 那一套。修复前（1.89.0）AnimStateMachine 不订阅 combat.entered/left、AnimClipResolver
// 只查七个固定键，进战后两层仍停在 idle 那一套，本用例的两处 Assert.AreEqual 红。
//
// 第六十二批复现（ADR-0111 相邻缺陷）：单位被打死、死亡剪辑播到末帧、复活那一刻规则层已经脱战——
// 复活后身体层与装备层必须显示 idle 那一套，而不是停在死亡剪辑末帧。"规则层已脱战"用夹具里的战斗探针
// （生产装配把它接到 ICombatHost.IsInCombat）翻成 false 表达——与 CombatHost.ClearCombatState（读档同一个公开
// 方法，不发 combat.left）执行后 IsInCombat 的读数一致；复活事件用真实总线上的 unit.respawned。
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;
using NUnit.Framework;
using UnityEngine.TestTools;
using Core.Carriers.Common;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class CombatStanceAnimReproTests : CombatStanceAnimFixtureBase
    {
        [UnityTest]
        public IEnumerator CombatEnteredLeft_SwitchesBodyAndEquipLayersBetweenIdleAndCombatIdleResourceSets()
        {
            const string idleName = "hero61r_idle";
            const string combatIdleName = "hero61r_combat_idle";
            const string weaponMesh = "mesh.w61r";
            const string weaponMeshStripped = "w61r";

            // 期望的资源 id 按命名规则拼出（不写死）。
            var idleBody = LayerResourceId(null, idleName, SideDir, "body");
            var idleWeapon = LayerResourceId(weaponMesh, idleName, SideDir, "mainhand");
            var combatBody = LayerResourceId(null, combatIdleName, SideDir, "body");
            var combatWeapon = LayerResourceId(weaponMesh, combatIdleName, SideDir, "mainhand");
            Assert.AreEqual($"sprite_anim.{weaponMeshStripped}__{combatIdleName}__{SideDir}__mainhand", combatWeapon.Value, "命名规则自检");

            WriteEffectResource(idleBody, UnityEngine.Color.red, UnityEngine.Color.green);
            WriteEffectResource(idleWeapon, UnityEngine.Color.blue, UnityEngine.Color.yellow);
            WriteEffectResource(combatBody, UnityEngine.Color.black, UnityEngine.Color.white);
            WriteEffectResource(combatWeapon, UnityEngine.Color.cyan, UnityEngine.Color.magenta);
            yield return WarmEffectCache(idleBody);
            yield return WarmEffectCache(idleWeapon);
            yield return WarmEffectCache(combatBody);
            yield return WarmEffectCache(combatWeapon);

            var fx = BuildFixture("repro", new Dictionary<string, string>
            {
                ["idle"] = ResourceRefOf(idleName),
                ["combat_idle"] = ResourceRefOf(combatIdleName),
            });
            EquipMesh(fx, "repro_w", "slot.mainhand", weaponMesh);

            // 前置条件：站着（Walk -> Idle，idle 逐层剪辑在播），身体层与装备层都应用 idle 那一套。
            fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
            fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Walk", "Idle"));
            yield return WaitBounded(() => AppliedResource(fx, "body") == idleBody.Value && AppliedResource(fx, "mainhand") == idleWeapon.Value);
            Assert.AreEqual(idleBody.Value, AppliedResource(fx, "body"), "前置条件：站着时身体层应用 idle 逐层剪辑");
            Assert.AreEqual(idleWeapon.Value, AppliedResource(fx, "mainhand"), "前置条件：站着时装备层应用 idle 逐层剪辑");

            // 进战：身体层与装备层都必须切到 combat_idle 那一套。
            fx.Bus.PublishImmediate(new CombatEnteredEvent(fx.EntityId));
            yield return WaitBounded(() => AppliedResource(fx, "body") == combatBody.Value && AppliedResource(fx, "mainhand") == combatWeapon.Value);
            Assert.AreEqual(combatBody.Value, AppliedResource(fx, "body"), "进战后身体层应用的应是 combat_idle 逐层剪辑资源");
            Assert.AreEqual(combatWeapon.Value, AppliedResource(fx, "mainhand"), "进战后装备层应用的应是 combat_idle 逐层剪辑资源");

            // 脱战：切回 idle 那一套。
            fx.Bus.PublishImmediate(new CombatLeftEvent(fx.EntityId));
            yield return WaitBounded(() => AppliedResource(fx, "body") == idleBody.Value && AppliedResource(fx, "mainhand") == idleWeapon.Value);
            Assert.AreEqual(idleBody.Value, AppliedResource(fx, "body"), "脱战后身体层应切回 idle 逐层剪辑资源");
            Assert.AreEqual(idleWeapon.Value, AppliedResource(fx, "mainhand"), "脱战后装备层应切回 idle 逐层剪辑资源");

            fx.EquipSource.Dispose();
            fx.View.Destroy();
        }

        [UnityTest]
        public IEnumerator RespawnAfterLeavingCombat_ShowsIdleOnBodyAndEquipLayers_NeverDeathFrames()
        {
            const string idleName = "hero62r_idle";
            const string combatIdleName = "hero62r_combat_idle";
            const string deathName = "hero62r_death";
            const string weaponMesh = "mesh.w62r";

            var idleBody = LayerResourceId(null, idleName, SideDir, "body");
            var idleWeapon = LayerResourceId(weaponMesh, idleName, SideDir, "mainhand");
            var combatBody = LayerResourceId(null, combatIdleName, SideDir, "body");
            var combatWeapon = LayerResourceId(weaponMesh, combatIdleName, SideDir, "mainhand");
            var deathBody = LayerResourceId(null, deathName, SideDir, "body");
            var deathWeapon = LayerResourceId(weaponMesh, deathName, SideDir, "mainhand");
            var all = new[] { idleBody, idleWeapon, combatBody, combatWeapon, deathBody, deathWeapon };
            for (var i = 0; i < all.Length; i++)
            {
                WriteEffectResource(all[i], UnityEngine.Color.red, UnityEngine.Color.green);
            }
            for (var i = 0; i < all.Length; i++)
            {
                yield return WarmEffectCache(all[i]);
            }

            var inCombat = false;
            var fx = BuildFixture("repro62", new Dictionary<string, string>
            {
                ["idle"] = ResourceRefOf(idleName),
                ["combat_idle"] = ResourceRefOf(combatIdleName),
                ["death"] = ResourceRefOf(deathName),
            }, configureFactory: f => f.CombatProbe = id => inCombat);
            EquipMesh(fx, "repro62_w", "slot.mainhand", weaponMesh);

            // 前置条件：在战中被打死，死亡剪辑播到末帧，两层都应用死亡那一套。
            fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
            fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Walk", "Idle"));
            fx.Bus.PublishImmediate(new CombatEnteredEvent(fx.EntityId));
            inCombat = true;
            var deathFinished = false;
            fx.Player.OnComplete(() => deathFinished = true);
            fx.Bus.PublishImmediate(new UnitDiedEvent(fx.EntityId, new Id("unit.attacker")));
            yield return WaitBounded(() => deathFinished && AppliedResource(fx, "body") == deathBody.Value && AppliedResource(fx, "mainhand") == deathWeapon.Value);
            Assert.IsTrue(deathFinished, "前置条件：死亡剪辑应当播放完毕（停在末帧）");
            Assert.AreEqual(deathBody.Value, AppliedResource(fx, "body"), "前置条件：死亡后身体层应用死亡剪辑");
            Assert.AreEqual(deathWeapon.Value, AppliedResource(fx, "mainhand"), "前置条件：死亡后装备层应用死亡剪辑");

            // 复活前规则层已脱战（死亡不脱战；读档类路径清掉战斗态不发 combat.left），随后复活。
            inCombat = false;
            fx.Bus.PublishImmediate(new UnitRespawnedEvent(fx.EntityId, Core.Rules.Common.RespawnPolicy.RespawnPoint));

            // 采样复活后若干帧：从未出现死亡资源，最终两层都是 idle 那一套。
            var sawDeath = false;
            var sampleFrames = 0;
            while (sampleFrames < 30)
            {
                var body = AppliedResource(fx, "body");
                var weapon = AppliedResource(fx, "mainhand");
                sawDeath |= body == deathBody.Value || weapon == deathWeapon.Value;
                yield return null;
                sampleFrames++;
            }
            Assert.AreEqual(idleBody.Value, AppliedResource(fx, "body"), "复活（已脱战）后身体层应应用 idle 逐层剪辑资源");
            Assert.AreEqual(idleWeapon.Value, AppliedResource(fx, "mainhand"), "复活（已脱战）后装备层应应用 idle 逐层剪辑资源");
            Assert.IsFalse(sawDeath, "复活后采样期间不应再出现死亡剪辑的资源");

            fx.EquipSource.Dispose();
            fx.View.Destroy();
        }
    }
}
