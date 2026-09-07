#nullable enable
// AnimClipResolverTests：W6-B 收口验收——AnimClipResolver 改用 IWeaponStyleSource（取代
// weaponStyleRefForEntity 委托）与 AnimStateMachine.StateChangedWithSkill（取代 StateChanged），
// Attack 状态沿用既有 AutoAttackAnim 覆盖行为，Cast 状态新增按触发技能 id 的 CastAnimOverride 覆盖
// （根治 W6-A 之前"cast 状态不做按技能覆盖"的已知简化，见该类型文件顶部判断记录）。
//
// 判断记录（用记录型假 playClip 委托，不经真实 UnityFrameAnimPlayer）：AnimClipResolver 本身只关心
// "该播放哪个 clipId"这一决策逻辑（09 第 4 节判断记录"解析工作留给调用方"），与"这个 clipId 是否已经
// 在某个具体播放器上注册"是两件独立的事——后者是 UnityViewFactory.RegisterDefaultClips 的职责（只
// 注册"anim.default.<displayId>.<state>"这一组 id，不注册武器风格覆盖用到的 clipId，见该方法与
// FrameAnimPlayer.Play 判断记录"未登记的序列帧剪辑会抛 ArgumentException"）。本文件因此直接验证
// AnimClipResolver 的决策输出（记录 playClip 被调用时传入的 clipId），不经过真实 UnityFrameAnimPlayer
// 播放管线，避免与"该 clipId 是否已注册"这个不相关的问题耦合。
using System.Collections.Generic;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using NUnit.Framework;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;

namespace Adapter.Unity.Tests.Runtime
{
    /// <summary>固定返回构造期指定的 weaponStyleRef 的最小 <see cref="IWeaponStyleSource"/> 测试替身。</summary>
    internal sealed class FixedWeaponStyleSource : IWeaponStyleSource
    {
        private readonly Id? _ref;
        public FixedWeaponStyleSource(Id? styleRef) => _ref = styleRef;
        public Id? GetWeaponStyleRef(Id entityId) => _ref;
    }

    public sealed class AnimClipResolverTests : PlayModeTestBase
    {
        private static IEventBus NewBus()
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, System.Array.Empty<string>()));
            }
            var catalog = EventCatalog.FromDefinitions(definitions);
            return new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });
        }

        [Test]
        public void Attack_WithWeaponStyle_UsesAutoAttackAnim_NotDefaultTable()
        {
            var bus = NewBus();
            var stateMachine = new AnimStateMachine(bus);
            var entityId = new Id("unit.acr_attack_test");
            var styleRef = new Id("display.weapon_style.acr_test_sword");
            var weaponStyle = new WeaponStyleDef(
                styleRef, autoAttackAnim: new Id("anim.acr_sword_swing"),
                castAnimOverride: null, swingVfx: null, impactVfxOverride: null);
            var weaponStyles = new Dictionary<Id, WeaponStyleDef> { [styleRef] = weaponStyle };

            var calls = new List<(Id EntityId, Id ClipId, bool Loop, double Speed)>();
            using var resolver = new AnimClipResolver(
                stateMachine,
                defaultClipsForEntity: _ => new Dictionary<string, Id> { ["attack"] = new Id("anim.default.attack_fallback") },
                playClip: (e, c, l, s) => calls.Add((e, c, l, s)),
                weaponStyleSource: new FixedWeaponStyleSource(styleRef),
                weaponStyles: weaponStyles);

            // 瞬发（castTime<=0）进入 Attack 状态。
            bus.PublishImmediate(new SkillCastStartEvent(entityId, new Id("skill.acr_test_strike"), castTime: 0.0));

            Assert.AreEqual(1, calls.Count);
            Assert.AreEqual(new Id("anim.acr_sword_swing"), calls[0].ClipId, "应当优先使用武器风格的 AutoAttackAnim，而不是默认剪辑表");

            stateMachine.Dispose();
        }

        [Test]
        public void Cast_WithMatchingSkillOverride_UsesCastAnimOverride()
        {
            var bus = NewBus();
            var stateMachine = new AnimStateMachine(bus);
            var entityId = new Id("unit.acr_cast_test");
            var skillId = new Id("skill.acr_test_fireball");
            var styleRef = new Id("display.weapon_style.acr_test_staff");
            var weaponStyle = new WeaponStyleDef(
                styleRef, autoAttackAnim: new Id("anim.acr_staff_jab"),
                castAnimOverride: new Dictionary<Id, Id> { [skillId] = new Id("anim.acr_staff_cast") },
                swingVfx: null, impactVfxOverride: null);
            var weaponStyles = new Dictionary<Id, WeaponStyleDef> { [styleRef] = weaponStyle };

            var calls = new List<Id>();
            using var resolver = new AnimClipResolver(
                stateMachine,
                defaultClipsForEntity: _ => new Dictionary<string, Id> { ["cast"] = new Id("anim.default.cast_fallback") },
                playClip: (e, c, l, s) => calls.Add(c),
                weaponStyleSource: new FixedWeaponStyleSource(styleRef),
                weaponStyles: weaponStyles);

            // 读条（castTime>0）进入 Cast 状态，携带触发技能 id。
            bus.PublishImmediate(new SkillCastStartEvent(entityId, skillId, castTime: 1.5));

            Assert.AreEqual(1, calls.Count);
            Assert.AreEqual(new Id("anim.acr_staff_cast"), calls[0], "触发技能命中 CastAnimOverride 时应当使用覆盖剪辑（W6-B 根治 cast 不做按技能覆盖）");

            stateMachine.Dispose();
        }

        [Test]
        public void Cast_WithNonMatchingSkill_FallsBackToDefaultClipsTable()
        {
            var bus = NewBus();
            var stateMachine = new AnimStateMachine(bus);
            var entityId = new Id("unit.acr_cast_fallback_test");
            var styleRef = new Id("display.weapon_style.acr_test_staff2");
            var weaponStyle = new WeaponStyleDef(
                styleRef, autoAttackAnim: new Id("anim.acr_staff_jab2"),
                castAnimOverride: new Dictionary<Id, Id> { [new Id("skill.acr_other_skill")] = new Id("anim.acr_staff_cast2") },
                swingVfx: null, impactVfxOverride: null);
            var weaponStyles = new Dictionary<Id, WeaponStyleDef> { [styleRef] = weaponStyle };

            var calls = new List<Id>();
            using var resolver = new AnimClipResolver(
                stateMachine,
                defaultClipsForEntity: _ => new Dictionary<string, Id> { ["cast"] = new Id("anim.default.cast_fallback2") },
                playClip: (e, c, l, s) => calls.Add(c),
                weaponStyleSource: new FixedWeaponStyleSource(styleRef),
                weaponStyles: weaponStyles);

            bus.PublishImmediate(new SkillCastStartEvent(entityId, new Id("skill.acr_unrelated"), castTime: 1.0));

            Assert.AreEqual(1, calls.Count);
            Assert.AreEqual(new Id("anim.default.cast_fallback2"), calls[0], "触发技能未命中 CastAnimOverride 时应当退回默认剪辑表");

            stateMachine.Dispose();
        }
    }
}
