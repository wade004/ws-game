#nullable enable
// CombatStanceAnimReproTests：ADR-0111（消费方反馈第六十一批）复现用例——生产装配级（UnityViewFactory +
// 真实 EventBus + 真实 UnityRenderer2D/UnityResourceLoader，纸娃娃层 + 装备层逐层剪辑）。
//
// 复现：外形 anim_set 声明 idle 与 combat_idle 两个剪辑，带一个装备层（mainhand 武器）。单位站着（idle 逐层
// 剪辑在播），发 combat.entered——身体层与装备层实际应用的资源必须是 combat_idle 那一套；发 combat.left——
// 必须切回 idle 那一套。修复前（1.89.0）AnimStateMachine 不订阅 combat.entered/left、AnimClipResolver
// 只查七个固定键，进战后两层仍停在 idle 那一套，本用例的两处 Assert.AreEqual 红。
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
    }
}
