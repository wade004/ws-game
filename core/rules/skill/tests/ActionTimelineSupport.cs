using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Tests.Rules.Skill
{
    /// <summary>可设置的行动者动作时钟替身（<see cref="IActorActionClockQuery"/>）：测试自己决定每个 tick 是否推进（顿帧 = 不推进）。</summary>
    internal sealed class StubActionClock : IActorActionClockQuery
    {
        private readonly Dictionary<Id, long> _ticks = new Dictionary<Id, long>();
        private readonly HashSet<Id> _paused = new HashSet<Id>();

        public void Advance(Id actorId, int ticks = 1)
        {
            _ticks[actorId] = ActionTicks(actorId) + ticks;
        }

        public void SetPaused(Id actorId, bool paused)
        {
            if (paused) _paused.Add(actorId); else _paused.Remove(actorId);
        }

        public bool IsPaused(Id actorId) => _paused.Contains(actorId);

        public int RemainingPausedTicks(Id actorId) => _paused.Contains(actorId) ? 1 : 0;

        public long ActionTicks(Id actorId) => _ticks.TryGetValue(actorId, out var t) ? t : 0;

        public int PauseHandleCount(Id actorId) => _paused.Contains(actorId) ? 1 : 0;

        public int TotalPauseHandleCount => _paused.Count;
    }

    /// <summary>输入缓冲替身（S1 并行开发期间，时间线只依赖 <see cref="IInputBufferQuery"/> 的取用口）：按优先级降序、提交 tick 升序取用。</summary>
    internal sealed class FakeInputBuffer : IInputBufferQuery
    {
        private sealed class Slot
        {
            public BufferedIntent Intent;
            public bool Consumed;
            public bool Dropped;
        }

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly StubActionClock _clock;

        public readonly List<(Id ActionId, string Reason, bool TimeSolvable)> Rejections = new List<(Id, string, bool)>();

        public FakeInputBuffer(StubActionClock clock)
        {
            _clock = clock;
        }

        public void Push(Id actorId, Id actionId, ActionClass cls, int validTicks = 1000, int priority = 0, Vec2? direction = null,
            BufferHoldState hold = BufferHoldState.Tap, int heldTicks = 0)
        {
            var now = _clock.ActionTicks(actorId);
            _slots.Add(new Slot { Intent = new BufferedIntent(actionId, cls, now, now + validTicks, priority, direction, hold, heldTicks, consumed: false) });
        }

        public IReadOnlyList<Id> PendingActions => _slots.Where(s => !s.Consumed && !s.Dropped).Select(s => s.Intent.ActionId).ToList();

        public int Pending => _slots.Count(s => !s.Consumed && !s.Dropped);

        public IReadOnlyList<BufferedIntent> Snapshot(Id actorId) => _slots.Where(s => !s.Dropped).Select(s => s.Intent).ToList();

        public bool TryConsume(Id actorId, Func<BufferedIntent, bool>? accepts, out BufferedIntent consumed) =>
            TryConsume(actorId, accepts, null, out consumed);

        public bool TryConsume(Id actorId, Func<BufferedIntent, bool>? accepts, Func<BufferedIntent, bool>? skip, out BufferedIntent consumed)
        {
            var now = _clock.ActionTicks(actorId);
            var candidate = _slots
                .Where(s => !s.Consumed && !s.Dropped && s.Intent.ExpiresAtActionTime >= now && (skip == null || !skip(s.Intent)))
                .OrderByDescending(s => s.Intent.Priority)
                .ThenBy(s => s.Intent.SubmittedTick)
                .FirstOrDefault();
            if (candidate == null || (accepts != null && !accepts(candidate.Intent)))
            {
                consumed = default;
                return false;
            }

            candidate.Consumed = true;
            consumed = candidate.Intent;
            return true;
        }

        public void ReportRejected(Id actorId, Id actionId, string reasonCode, bool timeSolvable)
        {
            Rejections.Add((actionId, reasonCode, timeSolvable));
            var slot = _slots.FirstOrDefault(s => s.Consumed && s.Intent.ActionId == actionId);
            if (slot == null) return;
            if (timeSolvable) slot.Consumed = false; else slot.Dropped = true;
        }
    }

    internal sealed class StubSkillBinding : IActionSkillBinding
    {
        private readonly Dictionary<Id, Id> _map = new Dictionary<Id, Id>();

        public StubSkillBinding Map(string actionId, string skillId)
        {
            _map[new Id(actionId)] = new Id(skillId);
            return this;
        }

        public bool TryResolveSkill(Id actorId, BufferedIntent intent, out Id skillId) => _map.TryGetValue(intent.ActionId, out skillId);
    }

    internal sealed class RecordingHitResolver : ITimelineHitResolver
    {
        public readonly List<(Id Skill, int ComboIndex, int Segment)> Hits = new List<(Id, int, int)>();

        public bool Settle { get; set; }

        public void ResolveHit(in TimelineHitContext context)
        {
            Hits.Add((context.Def.Id, context.ComboIndex, context.Segment));
            if (Settle) context.Settlement.SettleInstant();
        }
    }

    /// <summary>动作时间线测试夹具：真实手感解析器（框架 <c>data/_feel</c> 数据）+ 替身动作时钟/输入缓冲 + 按 tick 记录的事件流。</summary>
    internal sealed class TimelineHarness
    {
        public const double Step = 1.0 / 60.0;
        public static readonly Id Actor = new Id("unit.actor");
        public static readonly Id Foe = new Id("unit.foe");
        public static readonly Id Chain = new Id("target.chain.sample");
        public static readonly Id Energy = new Id("power.sample_energy");
        public static readonly Id HasteStat = new Id("stat.sample_haste");

        public SkillWorld World = default!;
        public StubActionClock Clock = new StubActionClock();
        public FakeInputBuffer Input = default!;
        public StubSkillBinding Binding = new StubSkillBinding();
        public RecordingHitResolver Hits = new RecordingHitResolver();
        public FeelResolver? Feel;

        /// <summary>按派发顺序记录的 (模拟 tick, 事件)；tick 由 <see cref="Tick"/> 递增。</summary>
        public readonly List<(int Tick, IEvent Event)> Log = new List<(int, IEvent)>();

        public int TickIndex { get; private set; }

        public IActionStateQuery Query => World.Host.ActionStateQuery;

        public static TimelineHarness Create(
            IEnumerable<JsonObject> skills, Action<SkillWorldBuilder>? configure = null, string? presetId = "feel.preset.arpg_responsive",
            bool withHitResolver = true)
        {
            var h = new TimelineHarness();
            var builder = new SkillWorldBuilder()
                .Power(Energy.Value, 100)
                .Stat(HasteStat.Value)
                .ValidationRule(new SkillTimelineRule());
            foreach (var s in skills) builder.SkillDef(s);
            configure?.Invoke(builder);
            h.World = builder.Build();
            h.World.AddUnit(Actor);
            h.World.AddUnit(Foe);
            h.World.Targets.SetChain(Chain, Foe);

            h.Input = new FakeInputBuffer(h.Clock);
            var services = new TimelineServices { Clock = h.Clock, Input = h.Input, Binding = h.Binding };
            if (withHitResolver) services.HitResolver = h.Hits;
            if (presetId != null)
            {
                h.Feel = CreateFeel(presetId);
                services.Feel = h.Feel;
            }

            h.World.Host.AttachTimelineServices(services);

            foreach (var key in new[]
            {
                RulesEventKeys.ActionStarted, RulesEventKeys.ActionPhaseChanged, RulesEventKeys.ActionMarker,
                RulesEventKeys.ActionCancelled, RulesEventKeys.ActionFinished, RulesEventKeys.SkillCastStart,
                RulesEventKeys.SkillCastSuccess, RulesEventKeys.SkillCastInterrupted, RulesEventKeys.SkillCastFailed,
            })
            {
                h.World.Bus.Subscribe(key, e => h.Log.Add((h.TickIndex, e)));
            }

            return h;
        }

        /// <summary>
        /// 推进一个模拟 tick，次序与生产循环一致：动作时钟 +1（未顿帧时）→ <paramref name="beforeUpdate"/>（施法/输入等）→ 技能 Update → 派发事件。
        /// </summary>
        public void Tick(Action? beforeUpdate = null, bool advanceClock = true)
        {
            TickIndex++;
            if (advanceClock) Clock.Advance(Actor);
            beforeUpdate?.Invoke();
            World.Host.Update(Step);
            World.Flush();
        }

        public void TickN(int n)
        {
            for (var i = 0; i < n; i++) Tick();
        }

        /// <summary>在新的一个 tick 里施法（时钟 +1 → 施法 → Update），返回施法所在的 tick 序号（动作时间 0）。</summary>
        public int CastInTick(string skillId, string? expectFail = null)
        {
            Tick(() =>
            {
                var r = World.Host.CastSkill(Actor, new Id(skillId), Array.Empty<Id>());
                if (expectFail == null && !r.Success) throw new InvalidOperationException("施法失败：" + r.Reason);
            });
            return TickIndex;
        }

        public IEnumerable<(int Tick, T Event)> Of<T>() where T : IEvent =>
            Log.Where(l => l.Event is T).Select(l => (l.Tick, (T)l.Event));

        public int ElapsedTicks => Query.Current(Actor)?.ElapsedTicks ?? -1;

        public string Fingerprint() => string.Join("\n", Log.Select(l => l.Tick + ":" + Describe(l.Event)));

        private static string Describe(IEvent e)
        {
            switch (e)
            {
                case ActionStartedEvent s: return $"started {s.SkillId} c{s.ComboIndex} d{s.DurationTicks} r{s.ChargeRatio:R}";
                case ActionPhaseChangedEvent p: return "phase " + p.Phase;
                case ActionMarkerEvent m: return "marker " + m.Name + "{" + string.Join(",", m.Args.Select(kv => kv.Key + "=" + kv.Value)) + "}";
                case ActionCancelledEvent c: return "cancelled " + c.Reason + " next=" + c.NextSkillId;
                case ActionFinishedEvent _: return "finished";
                case SkillCastInterruptedEvent i: return "interrupted " + i.SkillId + " reason=" + i.Reason;
                case SkillCastStartEvent s: return "cast_start " + s.SkillId;
                case SkillCastSuccessEvent s: return "cast_success " + s.SkillId;
                default: return e.GetType().Name;
            }
        }

        // ------------------------------------------------------------------ 手感解析器（框架数据）

        private static FeelResolver CreateFeel(string presetId)
        {
            var root = FindRepoRoot();
            var source = new InMemoryDataSource();
            var dir = Path.Combine(root, "data", "_feel", "feel");
            foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                source.Add(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
            }

            source.Add("feel.calibration", File.ReadAllText(Path.Combine(root, "core", "foundation", "feel", "tests", "data", "feel", "feel.calibration.json")));

            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
            });
            var registry = new DataRegistry(source, new EventBus(catalog), new DataRegistryOptions { FailOnUnknownTable = false });
            FeelSchemas.RegisterAll(registry);
            var report = registry.LoadAll();
            if (report.ErrorCount > 0) throw new InvalidOperationException("框架手感数据加载有错误：" + string.Join("\n", report.Issues));

            var profiles = FeelProfileSet.FromRegistry(registry, FeelFields.Default);
            var cal = new FeelCalibration("feel.calibration.test_a", presetId, 2.0, 4.0, 30.0, 10.0, 1.0, 32.0, 50.0);
            return new FeelResolver(profiles, cal, Step);
        }

        private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (var i = 0; i < 4; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException("源文件路径层级不足：" + sourceFilePath);
            }

            return dir.FullName;
        }

        // ------------------------------------------------------------------ 技能数据构造

        public static JsonObject TlSkill(
            string id, double startupMs, double activeMs, double recoveryMs,
            IEnumerable<JsonValue>? markers = null, IEnumerable<JsonValue>? cancelWindows = null, JsonObject? combo = null,
            string? costAt = null, string? cooldownAt = null, double cost = 0, double cooldown = 0, bool withEffect = true,
            double? castTimeSeconds = null)
        {
            var timeline = new List<(string, JsonValue)>
            {
                ("startup_ms", J.N(startupMs)), ("active_ms", J.N(activeMs)), ("recovery_ms", J.N(recoveryMs)),
            };
            if (markers != null) timeline.Add(("markers", new JsonArray(markers)));
            if (cancelWindows != null) timeline.Add(("cancel_windows", new JsonArray(cancelWindows)));
            if (combo != null) timeline.Add(("combo", combo));
            if (costAt != null) timeline.Add(("cost_at", J.S(costAt)));
            if (cooldownAt != null) timeline.Add(("cooldown_at", J.S(cooldownAt)));

            var fields = new List<(string, JsonValue)>
            {
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(castTimeSeconds ?? (startupMs + activeMs + recoveryMs) / 1000.0)),
                ("respects_gcd", J.B(true)),
                ("cooldown_duration", J.N(cooldown)),
                ("target_shape_ref", J.S(Chain.Value)),
                ("timeline", J.O(timeline.ToArray())),
            };
            if (withEffect)
            {
                fields.Add(("effects", J.A(
                    J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(7)), ("coefficient", J.N(0))))))));
            }

            if (cost > 0)
            {
                fields.Add(("cost", J.A(J.O(("power_type", J.S(Energy.Value)), ("amount", J.N(cost))))));
            }

            return J.O(fields.ToArray());
        }

        public static JsonValue Hit(double atMs, string? name = null) => J.O(("name", J.S(name ?? "hit")), ("at_ms", J.N(atMs)));

        public static JsonValue Marker(string name, double atMs) => J.O(("name", J.S(name)), ("at_ms", J.N(atMs)));

        public static JsonValue CancelWindow(string cls, double openMs, double? closeMs = null) =>
            closeMs.HasValue
                ? J.O(("class", J.S(cls)), ("open_ms", J.N(openMs)), ("close_ms", J.N(closeMs.Value)))
                : J.O(("class", J.S(cls)), ("open_ms", J.N(openMs)));

        public static JsonObject ComboBlock(string next, double openMs, double closeMs) =>
            J.O(("next", J.S(next)), ("open_ms", J.N(openMs)), ("close_ms", J.N(closeMs)));

        /// <summary>毫秒 → tick（与生产同一换算：<see cref="FeelCalibration.MillisecondsToTicks"/>）。</summary>
        public static int Ticks(double ms) => FeelCalibration.MillisecondsToTicks(ms, Step);
    }
}
