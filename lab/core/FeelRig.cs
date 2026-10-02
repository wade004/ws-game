using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Rules.Common;
using Core.Sim;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using FeedbackBinderCore = Presentation.FeedbackBinder.Core.FeedbackBinder;

namespace Lab
{
    /// <summary>
    /// 手感场景的记录与表现装置（脚本 meta 的 <c>feel</c> 为真且手感装配开着时由宿主创建）：
    /// <list type="bullet">
    /// <item>把动作/命中/受击/顿帧/缓冲丢弃事件按出场标签记成 <see cref="FeelEventRecord"/>（逻辑时间线）；</item>
    /// <item>每个固定步末尾采缓冲槽与运动层状态（<see cref="FeelTickSample"/>）；</item>
    /// <item>装一个真实的 <c>FeedbackBinder</c> + <c>ImpactPipeline</c>（生产的反馈包流水线），sink 换成记录型假实现，
    /// 把出批结果记成 <see cref="FeelPresentationRecord"/>（表现时间线）。反馈包与手感音效索引由本类按代码定义
    /// （<see cref="LabImpactProfile"/>），不读数据表——实验室内核不依赖表现层数据 schema 的装载，指纹因此只反映流水线行为。</item>
    /// </list>
    /// </summary>
    internal sealed class FeelRig : IDisposable
    {
        private readonly HeadlessWorld _world;
        private readonly FeelRecording _record;
        private readonly Dictionary<Id, string> _labels;
        private readonly Func<Id?, int> _ordinal;
        private readonly Id _playerId;
        private readonly double _step;
        private readonly List<KeyValuePair<string, Id>> _targets = new List<KeyValuePair<string, Id>>();
        private readonly FeedbackBinderCore? _binder;
        private readonly ImpactPipeline? _pipeline;
        private int _tick;

        public FeelRig(
            HeadlessWorld world, FeelRecording record, Dictionary<Id, string> labels, Func<Id?, int> ordinal, double step,
            IEnumerable<KeyValuePair<string, Id>> targets, IFeedbackSink? tee = null)
        {
            _world = world;
            _record = record;
            _labels = labels;
            _ordinal = ordinal;
            _step = step;
            _playerId = world.Player.EntityId;
            _targets.AddRange(targets);

            var feel = world.Gameplay.Feel;
            if (feel == null)
            {
                return;
            }

            var recordingSink = new RecordingSink(this, tee);
            var profile = LabImpactProfile.Build();
            var options = new ImpactOptions
            {
                FeelSource = new PresentingImpactFeelSource(feel.Resolver),
                ProfileResolver = id => id.Equals(LabImpactProfile.ProfileId) ? profile : null,
                SfxLayers = new SfxLayerIndex(LabImpactProfile.SfxRows()),
                CameraOwnerResolver = () => _playerId,
                PositionResolver = id => world.World.GetEntity(id)?.Position,
                StepSeconds = step,
                ReferenceHeight = feel.Feel.Calibration.ReferenceHeight,
            };
            _pipeline = new ImpactPipeline(options);
            var rules = new[]
            {
                new FeedbackRule(
                    new Id("feedback.lab_impact"), RulesEventKeys.CombatHitConfirmed, null,
                    new FeedbackAction[] { new PlayImpactAction(LabImpactProfile.ProfileId) }),
            };
            _binder = new FeedbackBinderCore(
                world.Bus, world.Gameplay.ExprHostFactory, rules, recordingSink,
                null, null, null, null, null, null, null, null, _pipeline);
        }

        public void BeginTick(int tick) => _tick = tick;

        private string Label(Id id) => _labels.TryGetValue(id, out var l) ? l : id.Value;

        private string Labels(IReadOnlyList<Id> ids)
        {
            var parts = new List<string>(ids.Count);
            foreach (var id in ids)
            {
                parts.Add(Label(id));
            }

            return string.Join("+", parts);
        }

        /// <summary>从总线事件里挑手感相关的几类记成逻辑时间线事件。</summary>
        public void OnEvent(IEvent evt, int tick)
        {
            switch (evt)
            {
                case ActionStartedEvent s:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "action_started", Label(s.ActorId), string.Empty, s.SkillId.Value, string.Empty, string.Empty,
                        s.ComboIndex, s.DurationTicks, 0, s.ChargeRatio));
                    break;
                case ActionPhaseChangedEvent p:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "action_phase", Label(p.ActorId), string.Empty, string.Empty, p.Phase.ToString(), string.Empty));
                    break;
                case ActionMarkerEvent m:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "action_marker", Label(m.ActorId), string.Empty, string.Empty, m.Name, string.Empty));
                    break;
                case ActionCancelledEvent c:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "action_cancelled", Label(c.ActorId), string.Empty, c.NextSkillId?.Value ?? string.Empty,
                        c.Reason.ToString(), string.Empty));
                    break;
                case ActionFinishedEvent f:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "action_finished", Label(f.ActorId), string.Empty, string.Empty, string.Empty, string.Empty));
                    break;
                case ActionTargetAssistedEvent a:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "target_assisted", Label(a.ActorId), Label(a.TargetId), string.Empty, string.Empty, string.Empty,
                        0, 0, 0, a.FacingDelta));
                    break;
                case CombatHitConfirmedEvent h:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "hit_confirmed", Label(h.SourceId), Label(h.TargetId), h.SkillId?.Value ?? string.Empty,
                        h.HitResult.ToString(),
                        "reaction=" + h.Reaction + ";class=" + h.ImpactClass + ";kill=" + (h.IsKill ? 1 : 0) + ";crit=" + (h.IsCrit ? 1 : 0)
                        + ";inst=" + _ordinal(h.AttackInstanceId) + ";cast=" + _ordinal(h.CastInstanceId),
                        h.Segment, h.AttackerHitStopTicks, h.TargetHitStopTicks, h.Amount));
                    break;
                case CombatReactionAppliedEvent r:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "reaction", Label(r.SourceId), Label(r.TargetId), string.Empty, r.Reaction.ToString(), string.Empty,
                        r.DurationTicks));
                    break;
                case FeelHitstopStartedEvent hs:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "hitstop_started", string.Empty, string.Empty, string.Empty, Labels(hs.UnitIds), string.Empty, hs.Ticks));
                    break;
                case FeelHitstopEndedEvent he:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "hitstop_ended", string.Empty, string.Empty, string.Empty, Labels(he.UnitIds), string.Empty));
                    break;
                case InputBufferDroppedEvent d:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "buffer_dropped", Label(d.ActorId), string.Empty, string.Empty, d.ActionId.Value, d.Reason.ToString()));
                    break;
            }
        }

        /// <summary>阻挡变更（可破坏障碍被打掉后宿主经导航契约的批量替换更新阻挡）。</summary>
        public void NoteBlockingChanged(int tick, string who, int rectCount, int version) =>
            _record.Events.Add(new FeelEventRecord(tick, "blocking_changed", who, string.Empty, string.Empty, string.Empty, string.Empty, rectCount, version));

        /// <summary>该宿主固定步末尾：采缓冲槽与运动层状态，并让反馈流水线兜底出批。</summary>
        public void EndTick(int tick)
        {
            var feel = _world.Gameplay.Feel;
            var slots = new StringBuilder();
            if (feel != null)
            {
                var snapshot = feel.InputBuffer.Snapshot(_playerId);
                for (var i = 0; i < snapshot.Count; i++)
                {
                    if (i > 0)
                    {
                        slots.Append('|');
                    }

                    var name = snapshot[i].ActionId.Value;
                    slots.Append(name.StartsWith("input.action.", StringComparison.Ordinal) ? name.Substring("input.action.".Length) : name);
                }
            }

            var motion = _world.Player.MovementState.Motion;
            var modes = new List<KeyValuePair<string, string>>();
            var positions = new List<KeyValuePair<string, Vec2>>();
            foreach (var target in _targets)
            {
                if (_world.World.GetEntity(target.Value) is Unit unit)
                {
                    positions.Add(new KeyValuePair<string, Vec2>(target.Key, unit.Position));
                    if (unit.MovementState.Motion.Mode != MotionMode.Grounded)
                    {
                        modes.Add(new KeyValuePair<string, string>(target.Key, unit.MovementState.Motion.Mode.ToString()));
                    }
                }
            }

            _record.Ticks.Add(new FeelTickSample(
                tick, slots.ToString(), motion.Mode.ToString(), motion.Source.ToString(), motion.Speed, modes, positions));
            _binder?.Update(_step);
        }

        public void Dispose()
        {
            if (_pipeline != null)
            {
                _record.FrozenAtEnd = _pipeline.Freezes.FrozenCount;
            }

            _binder?.Dispose();
        }

        /// <summary>记录型假 sink：反馈流水线出批的每条指令记成表现时间线条目。</summary>
        private sealed class RecordingSink : IFeedbackSink
        {
            private readonly FeelRig _rig;
            private readonly IFeedbackSink? _tee;

            public RecordingSink(FeelRig rig, IFeedbackSink? tee)
            {
                _rig = rig;
                _tee = tee;
            }

            private void Add(string kind, string text, double value = 0, double value2 = 0, int count = 0) =>
                _rig._record.Presentation.Add(new FeelPresentationRecord(_rig._tick, kind, text, value, value2, count));

            public void FloatingText(Id entityId, Id styleId, string text)
            {
                Add("text", styleId.Value);
                _tee?.FloatingText(entityId, styleId, text);
            }

            public void PlayVfx(Id vfxId, FeedbackAttachSpec attach)
            {
                Add("vfx", vfxId.Value);
                _tee?.PlayVfx(vfxId, attach);
            }

            public void PlaySfx(Id sfxId, Vec2? at)
            {
                Add("sfx", sfxId.Value);
                _tee?.PlaySfx(sfxId, at);
            }

            public void Freeze(double durationMs)
            {
                Add("freeze_ms", string.Empty, durationMs);
                _tee?.Freeze(durationMs);
            }

            public void ShakeCamera(Id profileId)
            {
                Add("shake", profileId.Value);
                _tee?.ShakeCamera(profileId);
            }

            public void Flash(Id entityId, Id profileId)
            {
                Add("flash", _rig.Label(entityId));
                _tee?.Flash(entityId, profileId);
            }

            public void ImpactCamera(ImpactCameraCue cue)
            {
                Add("camera", cue.ShakeProfileId?.Value ?? string.Empty, cue.Magnitude, cue.DecayMs, cue.HitCount);
                _tee?.ImpactCamera(cue);
            }

            public void FreezePresentation(IReadOnlyList<Id> unitIds, int ticks, ImpactFreezeLayers layers)
            {
                Add("freeze", _rig.Labels(unitIds), ticks);
                _tee?.FreezePresentation(unitIds, ticks, layers);
            }

            public void ReleasePresentation(IReadOnlyList<Id> unitIds)
            {
                Add("release", _rig.Labels(unitIds));
                _tee?.ReleasePresentation(unitIds);
            }

            public bool HasPendingPlayback => false;

            public event Action? PendingPlaybackChanged
            {
                add { }
                remove { }
            }
        }
    }

    /// <summary>
    /// 实验室的打击反馈包与手感音效索引（代码定义，不读数据表）：三个冲击等级（light/medium/heavy）× 结局（命中/击杀/回避/挥空）。
    /// 镜头冲击增益乘数：light 0.6、medium 1.0、heavy 1.6，击杀再乘 2；实际幅度还要乘档案的 <c>camera_impulse_gain</c> 并受
    /// <c>camera_shake_cap</c> 限幅（目标选择式预设两者为 0，因此该类格子不出镜头冲击）。音效层 id 为 <c>sfx.lab_&lt;层&gt;_t&lt;档&gt;</c>。
    /// </summary>
    internal static class LabImpactProfile
    {
        public static readonly Id ProfileId = new Id("feedback.impact_profile.lab_default");

        /// <summary>
        /// 顿帧期间冻结的表现层：骨骼/序列帧恒冻，粒子也冻（引擎宿主据此验证"顿帧期间被冻结单位名下的粒子停推进"）。
        /// 判断记录：该声明只经反馈 sink 的 <c>FreezePresentation</c> 的 layers 参数传出，记录型假 sink 不记它，
        /// 因此无头宿主的表现时间线与指纹不受影响。
        /// </summary>
        private static readonly ImpactFreezeLayers LabFreezeLayers = new ImpactFreezeLayers(true, false);

        public static ImpactProfile Build()
        {
            var variants = new List<ImpactVariant>();
            foreach (var pair in new[] { ("light", 0.6), ("medium", 1.0), ("heavy", 1.6) })
            {
                var impactClass = pair.Item1;
                var gain = pair.Item2;
                variants.Add(new ImpactVariant(
                    impactClass, ImpactOutcome.Hit, null, null, new[] { new ImpactSfxSpec(SfxFeelLayer.Impact, null) },
                    new ImpactCameraSpec(gain, null, 120), null, null, LabFreezeLayers, null));
                variants.Add(new ImpactVariant(
                    impactClass, ImpactOutcome.Kill, null, null,
                    new[] { new ImpactSfxSpec(SfxFeelLayer.Impact, null), new ImpactSfxSpec(SfxFeelLayer.Sweetener, null) },
                    new ImpactCameraSpec(gain, null, 160), null, null, LabFreezeLayers, new ImpactIntensity(null, 1.0, 2.0)));
            }

            variants.Add(new ImpactVariant(
                "medium", ImpactOutcome.Avoided, null, null, null, null, null, null, LabFreezeLayers, null));
            variants.Add(new ImpactVariant(
                "medium", ImpactOutcome.Whiff, null, null, new[] { new ImpactSfxSpec(SfxFeelLayer.Whiff, null) }, null, null, null,
                LabFreezeLayers, null));
            return new ImpactProfile(ProfileId, variants);
        }

        public static IEnumerable<SfxDef> SfxRows()
        {
            foreach (var layer in new[] { SfxFeelLayer.Swing, SfxFeelLayer.Whiff, SfxFeelLayer.Impact, SfxFeelLayer.Sweetener })
            {
                for (var tier = 1; tier <= 3; tier++)
                {
                    var name = "sfx.lab_" + SfxFeelLayers.ToName(layer) + "_t" + tier.ToString(CultureInfo.InvariantCulture);
                    yield return new SfxDef(new Id(name), "combat", null, null, new Id("sfx.res." + name.Substring(4)), false, layer, tier, "generic");
                }
            }
        }
    }

    /// <summary>
    /// 实验室的打击反馈包目录（只读出口）：引擎宿主据此在引擎侧装出同一个反馈包与手感音效目录，不另写一份。
    /// </summary>
    public static class LabFeedbackCatalog
    {
        public static Id ProfileId => LabImpactProfile.ProfileId;

        public static ImpactProfile BuildProfile() => LabImpactProfile.Build();

        public static IEnumerable<SfxDef> SfxRows() => LabImpactProfile.SfxRows();
    }
}
