using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 空间命中测试用的目标宿主替身：用形状几何（<see cref="ShapeGeometry"/>）而不是手工登记的结果解析目标。
    /// 候选是除施法者外的全部存活单位；没有登记形状的链是"自身链"（解析为施法者自己，供自我增益/闪避类技能用）。
    /// 一个目标被命中当且仅当形状与目标（半径 <see cref="SetRadius"/>，缺省 0 即目标中心）相交。
    /// 真实 <c>TargetHost.ResolveAtPose</c>/<c>TryGetChainShape</c> 由 <c>core/rules/targeting/tests</c> 单独覆盖。
    /// </summary>
    internal sealed class ShapeTargetHost : ITargetHost
    {
        private readonly FakeUnitAccess _units;
        private readonly Dictionary<Id, Shape> _shapes = new Dictionary<Id, Shape>();
        private readonly Dictionary<Id, double> _radius = new Dictionary<Id, double>();

        public int PoseResolveCalls { get; private set; }

        public ShapeTargetHost(FakeUnitAccess units)
        {
            _units = units;
        }

        public ShapeTargetHost SetShape(Id chain, Shape template)
        {
            _shapes[chain] = template;
            return this;
        }

        public ShapeTargetHost SetRadius(Id unit, double radius)
        {
            _radius[unit] = radius;
            return this;
        }

        public bool TryGetChainShape(Id chainId, out Shape template) => _shapes.TryGetValue(chainId, out template);

        public IReadOnlyList<Id> Resolve(Id chainId, Id casterId) => Resolve(chainId, casterId, null);

        public IReadOnlyList<Id> Resolve(Id chainId, Id casterId, Id? currentTarget) =>
            Query(chainId, casterId, _units.GetPosition(casterId), _units.GetFacing(casterId)).Select(t => t.Target).ToList();

        public TargetResolution ResolveWithCoefficients(Id chainId, Id casterId, Id? currentTarget = null) =>
            new TargetResolution(
                Query(chainId, casterId, _units.GetPosition(casterId), _units.GetFacing(casterId)),
                TargetOverflowPolicy.Truncate, cap: 0);

        public TargetResolution ResolveAtPose(Id chainId, Id casterId, Vec2 origin, double facing, Id? currentTarget = null)
        {
            PoseResolveCalls++;
            return new TargetResolution(Query(chainId, casterId, origin, facing), TargetOverflowPolicy.Truncate, cap: 0);
        }

        public IReadOnlyList<Id> FilterExplicitTargets(Id chainId, Id casterId, IReadOnlyList<Id> targets) => targets;

        private List<(Id Target, double Coefficient)> Query(Id chainId, Id casterId, Vec2 origin, double facing)
        {
            var result = new List<(Id, double)>();
            if (!_shapes.TryGetValue(chainId, out var template))
            {
                result.Add((casterId, 1.0));
                return result;
            }

            var shape = ShapeGeometry.RebaseAt(template, origin, facing);
            var hits = new List<(Id Id, double Distance)>();
            foreach (var id in _units.AllUnits)
            {
                if (id.Equals(casterId) || !_units.IsAlive(id))
                {
                    continue;
                }

                var position = _units.GetPosition(id);
                var radius = _radius.TryGetValue(id, out var r) ? r : 0.0;
                var closest = ShapeGeometry.ClosestPoint(shape, position);
                if ((closest - position).Length <= radius + 1e-12)
                {
                    hits.Add((id, (position - origin).Length));
                }
            }

            foreach (var hit in hits.OrderBy(h => h.Distance).ThenBy(h => h.Id.Value, StringComparer.Ordinal))
            {
                result.Add((hit.Id, 1.0));
            }

            return result;
        }
    }

    /// <summary>受击裁决替身：回放固定顿帧/反应，并记录每次裁决的输入（验证无敌/回避不产生顿帧、命中事件填入裁决结果）。</summary>
    internal sealed class StubHitFeelArbiter : IHitFeelArbiter
    {
        public readonly List<(Id Attacker, Id Target, HitResult Result, double Amount)> Inputs = new List<(Id, Id, HitResult, double)>();

        public HitFeelOutcome ForHit { get; set; } = new HitFeelOutcome("impact.heavy", 5, 4, HitReaction.Flinch, 0);

        public HitFeelOutcome Evaluate(in HitFeelInput input)
        {
            Inputs.Add((input.AttackerId, input.TargetId, input.HitResult, input.Amount));
            switch (input.HitResult)
            {
                case HitResult.Miss:
                case HitResult.Dodge:
                case HitResult.Parry:
                case HitResult.Immune:
                case HitResult.Invulnerable:
                    return HitFeelOutcome.None();
                default:
                    return ForHit;
            }
        }
    }

    /// <summary>目标辅助替身：返回配置的原始修正（朝向修正未限幅，由时间线按档案上限截断）。</summary>
    internal sealed class StubTargetAssist : IActionTargetAssist
    {
        public Id? Target { get; set; }

        public double FacingDeltaDeg { get; set; }

        public double DistanceAdjust { get; set; }

        public readonly List<ActionAssistRequest> Requests = new List<ActionAssistRequest>();

        public bool TryAssist(in ActionAssistRequest request, out ActionAssistOutcome outcome)
        {
            Requests.Add(request);
            if (!Target.HasValue)
            {
                outcome = default;
                return false;
            }

            outcome = new ActionAssistOutcome(Target.Value, FacingDeltaDeg, DistanceAdjust);
            return true;
        }
    }

    /// <summary>投射物生成器替身：记录每次 Spawn 的上下文与时间线交来的命中钩子，由测试手动驱动命中。</summary>
    internal sealed class RecordingProjectileSpawner : IProjectileSpawner
    {
        public readonly List<(EffectContext Context, IEffectSink Sink, IProjectileHitHook? Hook)> Spawns =
            new List<(EffectContext, IEffectSink, IProjectileHitHook?)>();

        public void Spawn(EffectContext context, IEffectSink effectSink) => Spawns.Add((context, effectSink, null));

        public void Spawn(EffectContext context, IEffectSink effectSink, IProjectileHitHook? hitHook) =>
            Spawns.Add((context, effectSink, hitHook));
    }

    /// <summary>空间命中测试夹具：时间线夹具 + 形状目标宿主 + 命中/辅助事件订阅。</summary>
    internal sealed class SpatialRig
    {
        public static readonly Id SelfChain = new Id("target.chain.self");

        public TimelineHarness H = default!;
        public ShapeTargetHost Shapes = default!;
        public StubHitFeelArbiter? Arbiter;
        public StubTargetAssist? Assist;

        public FakeUnitAccess Units => H.World.Units;

        /// <param name="shape">技能链（<c>target.chain.sample</c>）的命中形状模板；传 null 表示链没有形状（instant 路径）。</param>
        public static SpatialRig Create(
            IEnumerable<JsonObject> skills, Shape? shape, bool arbiter = true, bool assist = false,
            Action<SkillWorldBuilder>? configure = null)
        {
            var rig = new SpatialRig();
            ShapeTargetHost? shapes = null;
            rig.H = TimelineHarness.Create(
                skills,
                b =>
                {
                    b.TargetHostFactory = u =>
                    {
                        shapes = new ShapeTargetHost(u);
                        if (shape.HasValue)
                        {
                            shapes.SetShape(TimelineHarness.Chain, shape.Value);
                        }

                        return shapes;
                    };
                    configure?.Invoke(b);
                },
                withHitResolver: false);
            rig.Shapes = shapes!;
            rig.Arbiter = arbiter ? new StubHitFeelArbiter() : null;
            rig.Assist = assist ? new StubTargetAssist() : null;

            rig.H.World.Host.AttachTimelineServices(new TimelineServices
            {
                Clock = rig.H.Clock,
                Input = rig.H.Input,
                Binding = rig.H.Binding,
                Feel = rig.H.Feel,
                HitFeel = rig.Arbiter,
                TargetAssist = rig.Assist,
            });

            foreach (var key in new[] { RulesEventKeys.CombatHitConfirmed, RulesEventKeys.CombatAttackAvoided, RulesEventKeys.ActionTargetAssisted })
            {
                var h = rig.H;
                h.World.Bus.Subscribe(key, e => h.Log.Add((h.TickIndex, e)));
            }

            return rig;
        }

        public IEnumerable<CombatHitConfirmedEvent> Hits => H.Of<CombatHitConfirmedEvent>().Select(t => t.Event);

        public IEnumerable<CombatHitConfirmedEvent> HitsOn(Id target) => Hits.Where(e => e.TargetId.Equals(target));

        /// <summary>从施法开始推进到动作结束（含），返回动作经过的 tick 数。</summary>
        public void RunToEnd(Action<int>? perTick = null, int maxTicks = 600)
        {
            var i = 0;
            while (H.Query.Current(TimelineHarness.Actor).HasValue)
            {
                if (++i > maxTicks) throw new InvalidOperationException("动作未在预期 tick 数内结束");
                var n = i;
                H.Tick(() => perTick?.Invoke(n));
            }
        }

        public static string Describe(CombatHitConfirmedEvent e) =>
            $"{e.SourceId}->{e.TargetId} {e.SkillId} seg{e.Segment} {e.HitResult} a{e.Amount:R} cast={e.CastInstanceId} atk={e.AttackInstanceId} c=({e.ContactPoint.X:R},{e.ContactPoint.Y:R}) n=({e.ContactNormal.X:R},{e.ContactNormal.Y:R}) " +
            $"{e.ImpactClass} {e.AttackerHitStopTicks}/{e.TargetHitStopTicks} {e.Reaction}";

        // ------------------------------------------------------------------ 技能数据

        public static JsonObject SpSkill(
            string id, double startupMs, double activeMs, double recoveryMs, IEnumerable<JsonValue> markers,
            string? hitPolicy = null, double rehitMs = 0, double sampleStepMs = 0, string? hitMode = null,
            JsonObject? targetAssist = null, JsonObject? motion = null, IEnumerable<JsonValue>? effects = null,
            string? costAt = null, double cost = 0, string chain = "target.chain.sample", double range = 0, JsonObject? charge = null, bool? isAttack = null)
        {
            var timeline = new List<(string, JsonValue)>
            {
                ("startup_ms", J.N(startupMs)), ("active_ms", J.N(activeMs)), ("recovery_ms", J.N(recoveryMs)),
                ("markers", new JsonArray(markers)),
            };
            if (hitPolicy != null) timeline.Add(("hit_policy", J.S(hitPolicy)));
            if (rehitMs > 0) timeline.Add(("rehit_interval_ms", J.N(rehitMs)));
            if (sampleStepMs > 0) timeline.Add(("sample_step_ms", J.N(sampleStepMs)));
            if (hitMode != null) timeline.Add(("hit_mode", J.S(hitMode)));
            if (targetAssist != null) timeline.Add(("target_assist", targetAssist));
            if (motion != null) timeline.Add(("motion", motion));
            if (costAt != null) timeline.Add(("cost_at", J.S(costAt)));
            if (charge != null) timeline.Add(("charge", charge));
            if (isAttack.HasValue) timeline.Add(("is_attack", J.B(isAttack.Value)));

            var fields = new List<(string, JsonValue)>
            {
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(range)),
                ("cast_time", J.N((startupMs + activeMs + recoveryMs) / 1000.0)),
                ("respects_gcd", J.B(true)),
                ("cooldown_duration", J.N(0)),
                ("target_shape_ref", J.S(chain)),
                ("timeline", J.O(timeline.ToArray())),
                ("effects", new JsonArray(effects ?? new[] { Damage() })),
            };
            if (cost > 0)
            {
                fields.Add(("cost", J.A(J.O(("power_type", J.S(TimelineHarness.Energy.Value)), ("amount", J.N(cost))))));
            }

            return J.O(fields.ToArray());
        }

        public static JsonValue Damage(double value = 7) =>
            J.O(("kind", J.S("school_damage")), ("params", J.O(("base_value", J.N(value)), ("coefficient", J.N(0)))));

        public static JsonValue Projectile(int? maxPierce = null, string hitBehavior = "pierce", double speed = 10) =>
            J.O(("kind", J.S("projectile")), ("params", maxPierce.HasValue
                ? J.O(("hit_behavior", J.S(hitBehavior)), ("speed", J.N(speed)), ("max_pierce_count", J.N(maxPierce.Value)))
                : J.O(("hit_behavior", J.S(hitBehavior)), ("speed", J.N(speed)))));

        public static JsonValue HitAt(double atMs, int? segment = null) =>
            TimelineHarness.Hit(atMs, segment.HasValue ? "hit:" + segment.Value : null);

        public static JsonObject SelfSkill(string id, double startupMs, double activeMs, double recoveryMs, IEnumerable<JsonValue> markers, bool? isAttack = null) =>
            SpSkill(id, startupMs, activeMs, recoveryMs, markers, chain: SelfChain.Value, isAttack: isAttack,
                effects: new[] { (JsonValue)J.O(("kind", J.S("heal")), ("params", J.O(("base_value", J.N(1)), ("coefficient", J.N(0))))) });
    }
}
