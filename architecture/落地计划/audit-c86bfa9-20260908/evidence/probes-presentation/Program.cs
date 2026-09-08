using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.Render;

internal static class Program
{
    private static readonly Id Attacker = new Id("unit.aoe.attacker");

    private static int Main()
    {
        Console.WriteLine("feedback-hit-frame-aoe-probe baseline=c86bfa9 version=1.4.0");
        var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
        var source = new FakeHitFrameSource();
        source.RegisterRig(Attacker, null!);
        var sink = new Sink();
        var rule = new FeedbackRule(
            new Id("feedback.aoe.hit_frame"), RulesEventKeys.CombatDamageDealt, null,
            new[] { new FloatingTextAction(new Id("style.damage"), TextSource.Literal(new Id("text.damage"))) }, FeedbackSyncMode.HitFrame);
        var options = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven, HitFrameSyncTimeoutSeconds = 0.5 };
        using var binder = new FeedbackBinder(bus, new ExprFactory(), new[] { rule }, sink, options: options, hitFrameSource: source);

        Publish(bus, new Id("unit.target.a"));
        Publish(bus, new Id("unit.target.b"));
        var before = sink.Count;
        var pendingBefore = binder.HasPendingPlayback;
        source.Fire(Attacker);
        var afterFirstFrame = sink.Count;
        var pendingAfterFirst = binder.HasPendingPlayback;

        Publish(bus, new Id("unit.target.c"));
        source.Fire(Attacker);
        var afterSecondFrame = sink.Count;
        var pendingAfterSecond = binder.HasPendingPlayback;
        binder.Update(0.5);
        var afterTimeout = sink.Count;
        var pendingAfterTimeout = binder.HasPendingPlayback;

        Console.WriteLine($"same_attacker_targets expected_before=0 actual_before={before} expected_pending_before=true actual_pending_before={pendingBefore}");
        Console.WriteLine($"same_attacker_targets expected_after_one_frame=1 actual_after_one_frame={afterFirstFrame} expected_pending_after_one=true actual_pending_after_one={pendingAfterFirst}");
        Console.WriteLine($"new_target_then_second_frame expected_after_second_frame=2 actual_after_second_frame={afterSecondFrame} expected_pending_after_second=true actual_pending_after_second={pendingAfterSecond}");
        Console.WriteLine($"timeout expected_total=3 actual_total={afterTimeout} expected_pending_final=false actual_pending_final={pendingAfterTimeout}");
        Console.WriteLine("release_order=" + string.Join(",", sink.PlayedTargets));

        // PASS means the observed production queue behavior matched the reproduction assertions.
        var pass = before == 0 && pendingBefore && afterFirstFrame == 1 && pendingAfterFirst
                   && afterSecondFrame == 2 && pendingAfterSecond && afterTimeout == 3 && !pendingAfterTimeout
                   && sink.PlayedTargets.SequenceEqual(new[] { new Id("unit.target.a"), new Id("unit.target.b"), new Id("unit.target.c") });
        Console.WriteLine($"RESULT={(pass ? "PASS" : "FAIL")} reproduction=one_hit_frame_releases_only_first_pending_entry_then_old_second_then_timeout_third");
        return pass ? 0 : 1;
    }

    private static void Publish(EventBus bus, Id target) => bus.PublishImmediate(new CombatDamageDealtEvent(
        Attacker, target, new Id("skill.school.physical"), 10.0, false, HitResult.Hit));

    private sealed class FakeHitFrameSource : IHitFrameSource
    {
        private readonly HashSet<Id> _rigs = new();
        public event Action<Id>? HitFrameReached;
        public void RegisterRig(Id entityId, ICharacterRig rig) => _rigs.Add(entityId);
        public void UnregisterRig(Id entityId) => _rigs.Remove(entityId);
        public bool HasRig(Id entityId) => _rigs.Contains(entityId);
        public void Fire(Id entityId) => HitFrameReached?.Invoke(entityId);
    }

    private sealed class Sink : IFeedbackSink
    {
        public readonly List<Id> PlayedTargets = new();
        public int Count => PlayedTargets.Count;
        public bool HasPendingPlayback => false;
        public event Action? PendingPlaybackChanged;
        public void FloatingText(Id entityId, Id styleId, string text) => PlayedTargets.Add(entityId);
        public void PlayVfx(Id vfxId, FeedbackAttachSpec attach) { }
        public void PlaySfx(Id sfxId, Vec2? at) { }
        public void Freeze(double durationMs) { }
        public void ShakeCamera(Id profileId) { }
        public void Flash(Id entityId, Id profileId) { }
    }

    private sealed class ExprFactory : IExprHostFactory
    {
        private sealed class Host : IExprHost
        {
            public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) => ExprValue.OfBool(false);
        }
        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => new Host();
    }
}
