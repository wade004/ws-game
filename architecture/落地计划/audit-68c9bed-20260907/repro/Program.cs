using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Tests.Presentation.FeedbackBinder;

internal static class Program
{
    private static readonly List<string> Results = new List<string>();
    private static int ExecutionErrors;

    private static int Main()
    {
        Run("A SaveSystem formal corruption and backup fallback", ReproSaveBackupFallback);
        Run("C FeedbackBinder sequential cold sink", ReproSequentialColdSink);
        Run("C FeedbackBinder immediate cold sink", ReproImmediateColdSink);
        Console.WriteLine(string.Join(Environment.NewLine, Results));
        return ExecutionErrors == 0 ? 0 : 1;
    }

    private static void Run(string name, Func<string> repro)
    {
        try
        {
            Results.Add(repro());
        }
        catch (Exception ex)
        {
            ExecutionErrors++;
            Results.Add(name + " | actual=EXCEPTION " + ex.GetType().Name + ": " + ex.Message + " | EXECUTION_ERROR");
        }
    }

    private static string ReproSaveBackupFallback()
    {
        var fs = new StubFileSystem();
        var save = new Core.Foundation.SaveSystem.SaveSystem(
            fs, new SaveSystemOptions(new Id("game.audit")) { BackupCount = 1 });
        save.RegisterPersistable(new ConstantPersistable("custom.audit", new JsonString("payload")));
        var slot = new Id("slot.audit");
        var first = save.Save(new SaveRequest(slot, "t1"));
        var second = save.Save(new SaveRequest(slot, "t2"));
        var formalPath = "user://saves/slot.audit.json";
        var backupPath = "user://saves/backups/slot.audit.bak1.json";
        var backupBeforeCorruption = fs.ReadText(backupPath) ?? "<missing>";

        // 正式文件的 sections 仍是对象，但 meta 缺失：这是合法 JSON、浅层信封却无效的存档。
        fs.WriteTextAtomic(formalPath, "{\"save_version\":1,\"sections\":{}}");
        var corruptedFormal = fs.ReadText(formalPath);
        var fallback = save.Load(slot);

        // 保留完整有效 sections，只替换顶层版本号；嵌套 sections.meta.save_version 不能被一并破坏。
        var validSectionsInvalidVersion = ReplaceTopLevelVersion(backupBeforeCorruption, 1.5);
        fs.WriteTextAtomic(formalPath, validSectionsInvalidVersion);
        var fractional = save.Load(slot);
        var defectObserved = first.Success && second.Success
            && backupBeforeCorruption != "<missing>"
            && fallback.Status == LoadStatus.Corrupted
            && fractional.Status == LoadStatus.Corrupted;

        return "A SaveSystem formal corruption and backup fallback" +
            " | expectedCorrectBehavior=two successful saves create bak1; invalid formal envelope and top-level save_version=1.5 with valid sections fall back to bak1 as LoadedFromBackup" +
            " | actual=save1=" + first.Success + ", save2=" + second.Success +
            ", backupExists=" + (backupBeforeCorruption != "<missing>") +
            ", formalAfterCorruption=" + corruptedFormal +
            ", invalidFormalLoad=" + fallback.Status +
            ", fractionalFormal=" + validSectionsInvalidVersion +
            ", fractionalVersionLoad=" + fractional.Status +
            ", defectObserved=" + (fallback.Status == LoadStatus.Corrupted && fractional.Status == LoadStatus.Corrupted) +
            " | " + (defectObserved ? "REPRODUCED" : "NOT_REPRODUCED");
    }

    private static string ReplaceTopLevelVersion(string text, double version)
    {
        if (!(JsonReader.Parse(text) is JsonObject original))
        {
            throw new InvalidOperationException("backup 文档不是 JsonObject");
        }

        var builder = new JsonObjectBuilder();
        foreach (var entry in original)
        {
            builder.Add(entry.Key, entry.Key == "save_version" ? new JsonNumber(version) : entry.Value);
        }

        return JsonWriter.Write(builder.Build());
    }

    private static string ReproSequentialColdSink()
    {
        var bus = FeedbackBinderTestSupport.CreateBus();
        var sink = new ControllableSink();
        var rules = new[]
        {
            new FeedbackRule(new Id("feedback.audit.sfx"), RulesEventKeys.CombatDamageDealt, null,
                new FeedbackAction[] { new PlaySfxAction(new Id("sfx.audit.cold"), null) }),
        };
        var options = new FeedbackOptions
        {
            QueueMode = QueueMode.Sequential,
            SequentialStepSeconds = 0.1,
        };
        using var binder = new FeedbackBinder(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options);
        var policy = new WaitForPlaybackPacingPolicy(() => binder.HasPendingPlayback);
        bus.Subscribe(EventKeys.PresentationPlaybackFinished, _ => policy.OnPlaybackFinished());

        bus.PublishImmediate(new CombatDamageDealtEvent(new Id("unit.hero"), new Id("unit.target"),
            new Id("skill.school.physical"), 1.0, false, HitResult.Hit));
        policy.BeginStep();
        var before = "queue=" + binder.Queue.PendingCount + ", sinkPending=" + sink.HasPendingPlayback + ", gateOpen=" + policy.IsPlaybackFinished;
        binder.Update(0.1);
        var after = "queue=" + binder.Queue.PendingCount + ", sinkPending=" + sink.HasPendingPlayback + ", gateOpen=" + policy.IsPlaybackFinished;

        var reproduced = before == "queue=1, sinkPending=False, gateOpen=False"
            && after == "queue=0, sinkPending=True, gateOpen=True";
        return "C FeedbackBinder sequential cold sink" +
            " | expectedCorrectBehavior=queue drain must not open pacing gate while PlaySfx sink still reports pending playback" +
            " | actual=before(" + before + "), after(" + after + ")" +
            " | " + (reproduced ? "REPRODUCED" : "NOT_REPRODUCED");
    }

    private static string ReproImmediateColdSink()
    {
        var bus = FeedbackBinderTestSupport.CreateBus();
        var sink = new ControllableSink();
        var rules = new[]
        {
            new FeedbackRule(new Id("feedback.audit.sfx"), RulesEventKeys.CombatDamageDealt, null,
                new FeedbackAction[] { new PlaySfxAction(new Id("sfx.audit.cold"), null) }),
        };
        var options = new FeedbackOptions { QueueMode = QueueMode.Immediate };
        using var binder = new FeedbackBinder(bus, new FeedbackBinderTestSupport.FakeExprHostFactory(), rules, sink, options: options);
        var policy = new WaitForPlaybackPacingPolicy(() => binder.HasPendingPlayback);
        bus.Subscribe(EventKeys.PresentationPlaybackFinished, _ => policy.OnPlaybackFinished());

        bus.PublishImmediate(new CombatDamageDealtEvent(new Id("unit.hero"), new Id("unit.target"),
            new Id("skill.school.physical"), 1.0, false, HitResult.Hit));
        policy.BeginStep();
        var whilePending = "queue=" + binder.Queue.PendingCount + ", sinkPending=" + sink.HasPendingPlayback + ", gateOpen=" + policy.IsPlaybackFinished;
        sink.CompletePlayback();
        binder.Update(1.0);
        var afterSinkCompletes = "queue=" + binder.Queue.PendingCount + ", sinkPending=" + sink.HasPendingPlayback + ", gateOpen=" + policy.IsPlaybackFinished;

        var reproduced = whilePending == "queue=0, sinkPending=True, gateOpen=False"
            && afterSinkCompletes == "queue=0, sinkPending=False, gateOpen=False";
        return "C FeedbackBinder immediate cold sink" +
            " | expected=immediate mode with only cold sink pending must eventually emit/forward completion or otherwise release pacing gate" +
            " | actual=whilePending(" + whilePending + "), afterSinkCompletes(" + afterSinkCompletes + ")" +
            " | " + (reproduced ? "REPRODUCED" : "NOT_REPRODUCED");
    }

    private sealed class ConstantPersistable : IPersistable
    {
        private readonly JsonValue _value;
        public string SectionKey { get; }
        public ConstantPersistable(string sectionKey, JsonValue value)
        {
            SectionKey = sectionKey;
            _value = value;
        }
        public JsonValue Save() => _value;
        public void Load(JsonValue data) { }
    }

    private sealed class ControllableSink : IFeedbackSink
    {
        public bool HasPendingPlayback { get; private set; }
        public void FloatingText(Id entityId, Id styleId, string text) { }
        public void PlayVfx(Id vfxId, FeedbackAttachSpec attach) { }
        public void PlaySfx(Id sfxId, Vec2? at) => HasPendingPlayback = true;
        public void CompletePlayback() => HasPendingPlayback = false;
        public void Freeze(double durationMs) { }
        public void ShakeCamera(Id profileId) { }
        public void Flash(Id entityId, Id profileId) { }
    }
}
