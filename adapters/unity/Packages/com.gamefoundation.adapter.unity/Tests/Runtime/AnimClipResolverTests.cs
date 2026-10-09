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
using Core.Carriers.Common;
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

    [Category("module:render")]
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

        // ------------------------------------------------------------------
        // ADR-0174：施法三段动作（读条循环 / 释放 / 瞬发释放）
        // ------------------------------------------------------------------

        private static (IEventBus Bus, AnimStateMachine Machine, List<(Id Clip, bool Loop)> Calls, AnimClipResolver Resolver) ReleaseRig(Id skillId, Id castClip, Id releaseClip, Id autoAttack, bool declareRelease)
        {
            var bus = NewBus();
            var stateMachine = new AnimStateMachine(bus);
            var styleRef = new Id("display.weapon_style.acr_release_staff");
            var weaponStyle = new WeaponStyleDef(
                styleRef, autoAttackAnim: autoAttack,
                castAnimOverride: new Dictionary<Id, Id> { [skillId] = castClip },
                swingVfx: null, impactVfxOverride: null,
                releaseAnimOverride: declareRelease ? new Dictionary<Id, Id> { [skillId] = releaseClip } : null);
            var calls = new List<(Id, bool)>();
            var resolver = new AnimClipResolver(
                stateMachine,
                defaultClipsForEntity: _ => new Dictionary<string, Id> { ["cast"] = new Id("anim.default.cast_fallback"), ["idle"] = new Id("anim.default.idle"), ["attack"] = new Id("anim.default.attack") },
                playClip: (e, c, l, s) => calls.Add((c, l)),
                weaponStyleSource: new FixedWeaponStyleSource(styleRef),
                weaponStyles: new Dictionary<Id, WeaponStyleDef> { [styleRef] = weaponStyle });
            return (bus, stateMachine, calls, resolver);
        }

        [Test]
        public void ReleaseDeclared_CastLoopsThenCompletionPlaysRelease_ThenFallsBack()
        {
            var entity = new Id("unit.acr_rel_cast");
            var skill = new Id("skill.acr_rel_bolt");
            var (bus, machine, calls, resolver) = ReleaseRig(skill, new Id("anim.acr_cast_loop"), new Id("anim.acr_release"), new Id("anim.acr_jab"), declareRelease: true);
            using var _ = resolver;

            bus.PublishImmediate(new SkillCastStartEvent(entity, skill, castTime: 1.5));
            Assert.AreEqual((new Id("anim.acr_cast_loop"), true), calls[^1], "声明了释放动作的读条技能，读条剪辑循环播放");

            bus.PublishImmediate(new SkillCastSuccessEvent(entity, skill, System.Array.Empty<Id>(), false, 1.5));
            Assert.AreEqual((new Id("anim.acr_release"), false), calls[^1], "读条完成播一遍释放剪辑");
            Assert.AreEqual(AnimState.Attack, machine.GetState(entity));

            machine.NotifyTransientStateFinished(entity, AnimState.Attack);
            Assert.AreEqual(AnimState.Idle, machine.GetState(entity));
            machine.Dispose();
        }

        [Test]
        public void ReleaseDeclared_CastInterrupted_FallsBackWithoutPlayingRelease()
        {
            var entity = new Id("unit.acr_rel_int");
            var skill = new Id("skill.acr_rel_bolt2");
            var (bus, machine, calls, resolver) = ReleaseRig(skill, new Id("anim.acr_cast_loop"), new Id("anim.acr_release"), new Id("anim.acr_jab"), declareRelease: true);
            using var _ = resolver;

            bus.PublishImmediate(new SkillCastStartEvent(entity, skill, castTime: 1.5));
            bus.PublishImmediate(new SkillCastInterruptedEvent(entity, skill, new Id("unit.acr_foe")));

            Assert.AreEqual(AnimState.Idle, machine.GetState(entity));
            Assert.IsFalse(calls.Exists(c => c.Clip.Equals(new Id("anim.acr_release"))), "被打断不播释放");
            machine.Dispose();
        }

        [Test]
        public void ReleaseDeclared_InstantSkill_PlaysReleaseInsteadOfAutoAttackClip()
        {
            var entity = new Id("unit.acr_rel_instant");
            var skill = new Id("skill.acr_rel_blink");
            var (bus, machine, calls, resolver) = ReleaseRig(skill, new Id("anim.acr_cast_loop"), new Id("anim.acr_release"), new Id("anim.acr_jab"), declareRelease: true);
            using var _ = resolver;

            bus.PublishImmediate(new SkillCastStartEvent(entity, skill, castTime: 0.0));
            Assert.AreEqual((new Id("anim.acr_release"), false), calls[^1]);

            // 普通攻击（没有技能 id）仍用普攻剪辑。
            machine.NotifyTransientStateFinished(entity, AnimState.Attack);
            bus.PublishImmediate(new AutoAttackSwingEvent(entity, new Id("unit.acr_foe")));
            Assert.AreEqual((new Id("anim.acr_jab"), false), calls[^1]);
            machine.Dispose();
        }

        [Test]
        public void ReleaseNotDeclared_BehaviourUnchanged_CastPlaysOnce_InstantUsesAutoAttackClip()
        {
            var entity = new Id("unit.acr_rel_none");
            var skill = new Id("skill.acr_rel_plain");
            var (bus, machine, calls, resolver) = ReleaseRig(skill, new Id("anim.acr_cast_once"), new Id("anim.acr_release"), new Id("anim.acr_jab"), declareRelease: false);
            using var _ = resolver;

            bus.PublishImmediate(new SkillCastStartEvent(entity, skill, castTime: 1.5));
            Assert.AreEqual((new Id("anim.acr_cast_once"), false), calls[^1], "没声明释放动作：读条剪辑只播一遍");
            bus.PublishImmediate(new SkillCastSuccessEvent(entity, skill, System.Array.Empty<Id>(), false, 1.5));
            Assert.AreEqual(AnimState.Idle, machine.GetState(entity), "没声明释放动作：读条完成立即回落");

            bus.PublishImmediate(new SkillCastStartEvent(entity, skill, castTime: 0.0));
            Assert.AreEqual((new Id("anim.acr_jab"), false), calls[^1], "没声明释放动作：瞬发沿用普攻剪辑");
            machine.Dispose();
        }

        // ------------------------------------------------------------------
        // ADR-0147（M5-S4）：受击反应 -> 受击子键剪辑；动作分相 / 移动速率 -> 播放速率
        // ------------------------------------------------------------------

        private sealed class FakeHitReactions : IHitReactionQuery
        {
            public readonly Dictionary<Id, int> Remaining = new Dictionary<Id, int>();
            public bool IsStaggered(Id unitId) => Remaining.ContainsKey(unitId);
            public bool IsDowned(Id unitId) => false;
            public int RemainingStaggerTicks(Id unitId) => Remaining.TryGetValue(unitId, out var n) ? n : 0;
        }

        private sealed class FixedBlends : ILocomotionBlendSource
        {
            private readonly double _start;
            private readonly double _stop;
            public FixedBlends(double startSeconds, double stopSeconds) { _start = startSeconds; _stop = stopSeconds; }

            public bool TryGetBlendSeconds(Id entityId, AnimState from, AnimState to, out double seconds)
            {
                seconds = 0.0;
                if (from == AnimState.Idle && to == AnimState.Move) { seconds = _start; return true; }
                if (from == AnimState.Move && to == AnimState.Idle) { seconds = _stop; return true; }
                return false;
            }
        }

        private static readonly IReadOnlyDictionary<string, Id> HitTable = new Dictionary<string, Id>
        {
            ["idle"] = new Id("anim.t.idle"),
            ["move"] = new Id("anim.t.move"),
            ["attack"] = new Id("anim.t.attack"),
            ["hit"] = new Id("anim.t.hit"),
            ["hit.light"] = new Id("anim.t.hit_light"),
            ["hit.heavy"] = new Id("anim.t.hit_heavy"),
            ["hit.knockback"] = new Id("anim.t.hit_knockback"),
            ["hit.knockdown"] = new Id("anim.t.hit_knockdown"),
            ["hit.getup"] = new Id("anim.t.hit_getup"),
            ["hit.block"] = new Id("anim.t.hit_block"),
        };

        [Test]
        public void HitReactions_PlayTheClipOfTheirPoseKey_AndNoneReactionPlaysNothing()
        {
            var bus = NewBus();
            var reactions = new FakeHitReactions();
            var stateMachine = new AnimStateMachine(bus, null, reactions);
            var target = new Id("unit.acr_hit_target");
            var foe = new Id("unit.acr_foe");
            var calls = new List<(Id ClipId, bool Loop, double Speed)>();
            using var resolver = new AnimClipResolver(
                stateMachine,
                defaultClipsForEntity: _ => HitTable,
                playClip: (e, c, l, s) => calls.Add((c, l, s)));

            // 期望由映射规则（03 第 4.5 节）算出：重硬直 -> hit.heavy -> 该键对应剪辑；不循环、1 倍速。
            reactions.Remaining[target] = 8;
            bus.PublishImmediate(new CombatReactionAppliedEvent(target, HitReaction.Stagger, foe, new Id("atk.1"), 8, 8, 0, 0));
            Assert.AreEqual(1, calls.Count);
            Assert.AreEqual(HitTable["hit.heavy"], calls[0].ClipId);
            Assert.IsFalse(calls[0].Loop);
            Assert.AreEqual(1.0, calls[0].Speed);

            // 击退（更重的反应）换剪辑。
            reactions.Remaining[target] = 10;
            bus.PublishImmediate(new CombatReactionAppliedEvent(target, HitReaction.Knockback, foe, new Id("atk.2"), 10, 10, 0, 0));
            Assert.AreEqual(2, calls.Count);
            Assert.AreEqual(HitTable["hit.knockback"], calls[1].ClipId);

            // 反应为 none（霸体）：裁决事件不进受击，伤害落地也不驱动（反应驱动模式下受击动画只由裁决决定）。
            var armored = new Id("unit.acr_armored");
            var before = calls.Count;
            bus.PublishImmediate(new CombatReactionAppliedEvent(armored, HitReaction.None, foe, new Id("atk.3"), 0, 0, 0, 0));
            bus.PublishImmediate(new CombatDamageDealtEvent(foe, armored, new Id("school.physical"), 5, false, HitResult.Hit));
            Assert.AreEqual(before, calls.Count, "反应为 none 的单位不播任何受击剪辑");

            stateMachine.Dispose();
        }

        [Test]
        public void HitReactions_MissingSubKey_FallsBackToPlainHit()
        {
            var bus = NewBus();
            var reactions = new FakeHitReactions();
            var stateMachine = new AnimStateMachine(bus, null, reactions);
            var target = new Id("unit.acr_phase_target");
            var calls = new List<Id>();
            var plainOnly = new Dictionary<string, Id> { ["idle"] = new Id("anim.p.idle"), ["hit"] = new Id("anim.p.hit") };
            using var resolver = new AnimClipResolver(
                stateMachine,
                defaultClipsForEntity: _ => plainOnly,
                playClip: (e, c, l, s) => calls.Add(c));

            reactions.Remaining[target] = 6;
            bus.PublishImmediate(new CombatReactionAppliedEvent(target, HitReaction.Stagger, new Id("unit.acr_foe"), new Id("atk.1"), 6, 6, 0, 0));
            Assert.AreEqual(1, calls.Count);
            Assert.AreEqual(plainOnly["hit"], calls[0], "姿势集没有 hit.heavy 时按回落链落到 hit");

            stateMachine.Dispose();
        }

        [Test]
        public void ClipPlaybackRates_FollowActionPhases_ThroughPlayClipSpeedAndSetClipSpeed()
        {
            var bus = NewBus();
            var stateMachine = new AnimStateMachine(bus);
            var actor = new Id("unit.acr_rate_actor");
            var rates = new ClipPlaybackRates(bus, stateMachine, null);
            var plays = new List<double>();
            var speeds = new List<double>();
            using var resolver = new AnimClipResolver(
                stateMachine,
                defaultClipsForEntity: _ => HitTable,
                playClip: (e, c, l, s) => plays.Add(s),
                weaponStyleSource: null, weaponStyles: null, isClipReady: null, poseContext: null,
                playbackRates: rates,
                setClipSpeed: (e, s) => speeds.Add(s),
                blends: null, hintBlend: null);

            // 作者毫秒 / 重映射后的实际毫秒 = 逐相速率；动画按此速率播，与重映射后的判定时间线同长。
            const double authoredStartup = 200, authoredActive = 100, authoredRecovery = 300;
            const double actualStartup = 100, actualActive = 100, actualRecovery = 450;
            var cast = new Id("cast.acr.1");
            bus.PublishImmediate(new SkillCastStartEvent(actor, new Id("skill.acr_slash"), castTime: 0.0));
            bus.PublishImmediate(new ActionStartedEvent(actor, new Id("skill.acr_slash"), cast, 0, 10, 0, true,
                authoredStartup / actualStartup, authoredActive / actualActive, authoredRecovery / actualRecovery));

            // 同批事件先后不固定：切入动作剪辑那一刻速率可能还是 1，随后由 action.started 补一次速率下发——最终前摇速率必须对。
            var startupRate = authoredStartup / actualStartup;
            Assert.AreEqual(1, plays.Count);
            Assert.AreEqual(startupRate, speeds.Count > 0 ? speeds[speeds.Count - 1] : plays[0], 1e-9, "前摇速率 = 作者毫秒 / 实际毫秒");

            bus.PublishImmediate(new ActionPhaseChangedEvent(actor, cast, ActionPhase.Active));
            bus.PublishImmediate(new ActionPhaseChangedEvent(actor, cast, ActionPhase.Recovery));
            Assert.AreEqual(authoredRecovery / actualRecovery, speeds[speeds.Count - 1], 1e-9);
            Assert.AreEqual(authoredRecovery, speeds[speeds.Count - 1] * actualRecovery, 1e-9, "不变量：速率 x 实际毫秒 = 作者毫秒");

            bus.PublishImmediate(new ActionFinishedEvent(actor, cast));
            Assert.AreEqual(1.0, speeds[speeds.Count - 1], "动作结束速率复位为 1");

            // 同一速率不重复下发。
            var count = speeds.Count;
            bus.PublishImmediate(new ActionFinishedEvent(actor, cast));
            Assert.AreEqual(count, speeds.Count);

            rates.Dispose();
            stateMachine.Dispose();
        }

        [Test]
        public void LocomotionBlendHint_IsGivenOnlyForIdleMoveSwitches_BeforeThePlay()
        {
            var bus = NewBus();
            var stateMachine = new AnimStateMachine(bus);
            var actor = new Id("unit.acr_blend_actor");
            var log = new List<string>();
            stateMachine.Track(actor);
            using var resolver = new AnimClipResolver(
                stateMachine,
                defaultClipsForEntity: _ => HitTable,
                playClip: (e, c, l, s) => log.Add("play:" + c.Value),
                weaponStyleSource: null, weaponStyles: null, isClipReady: null, poseContext: null,
                playbackRates: null, setClipSpeed: null,
                blends: new FixedBlends(0.12, 0.04),
                hintBlend: (e, sec) => log.Add("blend:" + sec));

            bus.PublishImmediate(new UnitStateChangedEvent(actor, "Idle", "Run"));
            bus.PublishImmediate(new UnitStateChangedEvent(actor, "Run", "Idle"));
            bus.PublishImmediate(new SkillCastStartEvent(actor, new Id("skill.acr_slash"), castTime: 0.0));

            Assert.AreEqual(new[]
            {
                "blend:0.12", "play:anim.t.move",
                "blend:0.04", "play:anim.t.idle",
                "play:anim.t.attack",
            }, log);

            stateMachine.Dispose();
        }
    }
}
