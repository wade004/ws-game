using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using Presentation.FeedbackBinder.Core;
using Adapter.Unity.Presentation;

internal static class Program
{
    private static readonly Id Hero = new Id("unit.probe.hero");
    private static readonly Id Target = new Id("unit.probe.target");

    private static int Main()
    {
        Console.WriteLine("presentation-mechanism-probe baseline=5c444f1 version=1.3.0");
        Console.WriteLine($"runtime={Environment.Version} utc={DateTime.UtcNow:O}");
        var failures = new List<string>();

        Run("anim-state-and-resolver", ProbeAnimStateAndResolver, failures);
        Run("feedback-binder-hit-frame", ProbeFeedbackBinderHitFrame, failures);
        Run("frame-player-and-weapon-override", ProbeFramePlayerAndWeaponOverride, failures);

        Console.WriteLine($"RESULT={(failures.Count == 0 ? "PASS" : "FAIL")} failures={failures.Count}");
        foreach (var failure in failures)
        {
            Console.WriteLine("FAILURE " + failure);
        }
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Run(string name, Action probe, List<string> failures)
    {
        Console.WriteLine($"BEGIN {name}");
        try
        {
            probe();
            Console.WriteLine($"END {name} PASS");
        }
        catch (Exception ex)
        {
            failures.Add(name + ": " + ex.GetType().Name + ": " + ex.Message);
            Console.WriteLine($"END {name} FAIL {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static EventBus NewBus() =>
        new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

    private static void ProbeAnimStateAndResolver()
    {
        var bus = NewBus();
        using var machine = new AnimStateMachine(bus);
        var plays = new List<string>();
        using var resolver = new AnimClipResolver(
            machine,
            _ => new Dictionary<string, Id>
            {
                ["idle"] = new Id("anim.default.idle"),
                ["move"] = new Id("anim.default.move"),
                ["attack"] = new Id("anim.default.hero.attack"),
                ["hit"] = new Id("anim.default.hit"),
            },
            (entity, clip, loop, speed) => plays.Add($"{entity}:{clip}:loop={loop}:speed={speed}"));

        bus.PublishImmediate(new SkillCastStartEvent(Hero, new Id("skill.auto_attack"), 0.0));
        var afterAttack = machine.GetState(Hero);
        bus.PublishImmediate(new SkillCastSuccessEvent(Hero, new Id("skill.auto_attack"), Array.Empty<Id>()));
        var afterSuccess = machine.GetState(Hero);
        bus.PublishImmediate(new UnitStateChangedEvent(Hero, "Idle", "Walk"));
        var afterWalkWhileAttack = machine.GetState(Hero);
        var beforeRepeat = plays.Count;
        bus.PublishImmediate(new SkillCastStartEvent(Hero, new Id("skill.auto_attack"), 0.0));
        var afterRepeat = machine.GetState(Hero);
        var repeatPlayDelta = plays.Count - beforeRepeat;

        Assert(afterAttack == AnimState.Attack, $"expected Attack after cast start, actual {afterAttack}");
        Assert(afterSuccess == AnimState.Attack, $"expected Attack after cast success, actual {afterSuccess}");
        Assert(afterWalkWhileAttack == AnimState.Attack, $"expected Attack while locomotion deferred, actual {afterWalkWhileAttack}");
        Assert(afterRepeat == AnimState.Attack, $"expected Attack on repeated cast, actual {afterRepeat}");
        Assert(repeatPlayDelta == 0, $"expected no same-state replay, actual delta {repeatPlayDelta}");
        Console.WriteLine($"anim attack_flow expected=Attack,Attack,Attack,Attack actual={afterAttack},{afterSuccess},{afterWalkWhileAttack},{afterRepeat} initial_play_count={beforeRepeat} repeat_play_delta={repeatPlayDelta}");

        bus.PublishImmediate(new CombatDamageDealtEvent(new Id("unit.enemy"), Target, new Id("skill.school.physical"), 10.0, false, HitResult.Hit));
        var afterHit = machine.GetState(Target);
        bus.PublishImmediate(new UnitStateChangedEvent(Target, "Idle", "Walk"));
        bus.PublishImmediate(new SkillCastStartEvent(Target, new Id("skill.auto_attack"), 0.0));
        var hitAfterMoveAndAttack = machine.GetState(Target);
        Assert(afterHit == AnimState.Hit, $"expected Hit after damage, actual {afterHit}");
        Assert(hitAfterMoveAndAttack == AnimState.Hit, $"expected Hit to block move/attack, actual {hitAfterMoveAndAttack}");
        Console.WriteLine($"anim hit_priority expected=Hit,Hit actual={afterHit},{hitAfterMoveAndAttack}");
    }

    private static void ProbeFeedbackBinderHitFrame()
    {
        var bus = NewBus();
        var source = new ProbeHitFrameSource();
        source.RegisterRig(Hero, null!);
        var sink = new ProbeFeedbackSink();
        var rules = new List<FeedbackRule>
        {
            new FeedbackRule(new Id("feedback.hit_frame.a"), RulesEventKeys.CombatDamageDealt, null,
                new[] { new PlaySfxAction(new Id("sfx.hit.a"), null) }, FeedbackSyncMode.HitFrame),
            new FeedbackRule(new Id("feedback.hit_frame.b"), RulesEventKeys.CombatDamageDealt, null,
                new[] { new PlaySfxAction(new Id("sfx.hit.b"), null) }, FeedbackSyncMode.HitFrame),
        };
        var options = new FeedbackOptions
        {
            HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven,
            HitFrameSyncTimeoutSeconds = 0.2,
        };

        using var binder = new FeedbackBinder(
            bus, new ProbeExprHostFactory(), rules, sink, options: options, hitFrameSource: source);
        bus.PublishImmediate(new CombatDamageDealtEvent(Hero, Target, new Id("skill.school.physical"), 10.0, false, HitResult.Hit));
        var beforeFire = sink.Sfx.Count;
        var pendingBeforeFire = binder.HasPendingPlayback;
        source.Fire(Hero);
        var afterOneFire = sink.Sfx.Count;
        var pendingAfterOneFire = binder.HasPendingPlayback;
        binder.Update(0.3);
        var afterTimeout = sink.Sfx.Count;
        var pendingAfterTimeout = binder.HasPendingPlayback;

        Assert(beforeFire == 0, $"expected 0 SFX before hit frame, actual {beforeFire}");
        Assert(pendingBeforeFire, "expected binder pending before hit frame");
        Assert(afterOneFire == 1, $"expected one of two matching rules after one Emit, actual {afterOneFire}");
        Assert(pendingAfterOneFire, "expected second matching rule pending after one Emit");
        Assert(afterTimeout == 2, $"expected second rule timeout release, actual {afterTimeout}");
        Assert(!pendingAfterTimeout, "expected no pending playback after timeout release");
        Console.WriteLine($"feedback two_rules_same_damage expected=0,1,2 actual={beforeFire},{afterOneFire},{afterTimeout} pending={pendingBeforeFire},{pendingAfterOneFire},{pendingAfterTimeout} sfx={string.Join(",", sink.Sfx)}");
    }

    private static void ProbeFramePlayerAndWeaponOverride()
    {
        var syntheticId = new Id("anim.default." + Hero + ".attack");
        var clips = new Dictionary<Id, FrameAnimClip>
        {
            [syntheticId] = new FrameAnimClip(syntheticId, 10, 10.0,
                new Dictionary<string, int> { [FrameAnimClip.HitFrameMarker] = 2 }),
        };
        var player = new FrameAnimPlayer(clips);
        var animEvents = new List<string>();
        player.OnAnimEvent(animEvents.Add);
        player.Play(syntheticId, loop: false, speed: 1.0);
        player.Update(0.35);
        Assert(player.CurrentFrame == 3, $"expected real FrameAnimPlayer current frame 3 at 0.35s, actual {player.CurrentFrame}");
        Assert(animEvents.Count == 1 && animEvents[0] == FrameAnimClip.HitFrameMarker,
            $"expected hit_frame event once at crossed frame 2, actual count={animEvents.Count} values={string.Join(",", animEvents)}");
        Console.WriteLine($"frame_player expected=current_frame:3 hit_events:1 actual=current_frame:{player.CurrentFrame} hit_events:{animEvents.Count}");

        var bus = NewBus();
        using var machine = new AnimStateMachine(bus);
        var styleRef = new Id("display.weapon_style.sword");
        var styleClip = new Id("anim.sample_sword_swing");
        var styles = new Dictionary<Id, WeaponStyleDef>
        {
            [styleRef] = new WeaponStyleDef(styleRef, styleClip, null, null, null),
        };
        var caught = (Exception?)null;
        var selected = (Id?)null;
        using var resolver = new AnimClipResolver(
            machine,
            _ => new Dictionary<string, Id> { ["attack"] = syntheticId },
            (entity, clip, loop, speed) =>
            {
                selected = clip;
                try
                {
                    player.Play(clip, loop, speed);
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
            },
            new ProbeWeaponStyleSource(styleRef), styles);

        bus.PublishImmediate(new SkillCastStartEvent(Hero, new Id("skill.auto_attack"), 0.0));
        Assert(selected == styleClip, $"expected resolver to select weapon style clip {styleClip}, actual {selected}");
        Assert(caught is ArgumentException, $"expected real FrameAnimPlayer.Play ArgumentException for unregistered style clip, actual {(caught == null ? "none" : caught.GetType().Name)}");
        Console.WriteLine($"resolver weapon_override expected_selected={styleClip} actual_selected={selected} expected_exception=ArgumentException actual_exception={caught?.GetType().Name ?? "none"}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ProbeWeaponStyleSource : IWeaponStyleSource
    {
        private readonly Id _styleRef;
        public ProbeWeaponStyleSource(Id styleRef) => _styleRef = styleRef;
        public Id? GetWeaponStyleRef(Id entityId) => _styleRef;
    }

    private sealed class ProbeHitFrameSource : IHitFrameSource
    {
        private readonly HashSet<Id> _rigs = new HashSet<Id>();
        public event Action<Id>? HitFrameReached;
        public void RegisterRig(Id entityId, ICharacterRig rig) => _rigs.Add(entityId);
        public void UnregisterRig(Id entityId) => _rigs.Remove(entityId);
        public bool HasRig(Id entityId) => _rigs.Contains(entityId);
        public void Fire(Id entityId) => HitFrameReached?.Invoke(entityId);
    }

    private sealed class ProbeFeedbackSink : IFeedbackSink
    {
        public List<Id> Sfx { get; } = new List<Id>();
        public bool HasPendingPlayback => false;
        public event Action? PendingPlaybackChanged;
        public void FloatingText(Id entityId, Id styleId, string text) { }
        public void PlayVfx(Id vfxId, FeedbackAttachSpec attach) { }
        public void PlaySfx(Id sfxId, Vec2? at) => Sfx.Add(sfxId);
        public void Freeze(double durationMs) { }
        public void ShakeCamera(Id profileId) { }
        public void Flash(Id entityId, Id profileId) { }
    }

    private sealed class ProbeExprHostFactory : IExprHostFactory
    {
        private sealed class Host : IExprHost
        {
            public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) => ExprValue.OfBool(false);
        }
        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new Host();
    }
}
