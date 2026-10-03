using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
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
    /// 把出批结果记成 <see cref="FeelPresentationRecord"/>（表现时间线）。反馈包与手感音效索引（<see cref="LabImpactProfile"/>）
    /// 从实验室数据根读取，指纹只反映流水线行为。</item>
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
            var profile = LabImpactProfile.Load(world.Registry);
            var feelSource = new PresentingImpactFeelSource(feel.Resolver);

            // 玩家的手感表声明了反馈包引用（impact_profile_ref，例如框架默认手感模板 feel.preset.tpl_*）时，
            // 这一套反馈就是该包，而不是实验室缺省包；既有预设没有该字段，行为与此前逐位一致。
            var profileId = LabImpactProfile.ProfileId;
            var ownProfile = feelSource.Get(_playerId)?.ProfileRef;
            ImpactProfile? ownLoaded = null;
            if (ownProfile.HasValue && !ownProfile.Value.Equals(LabImpactProfile.ProfileId))
            {
                profileId = ownProfile.Value;
                ownLoaded = LabImpactProfile.Load(world.Registry, profileId);
            }

            var options = new ImpactOptions
            {
                FeelSource = feelSource,
                ProfileResolver = id =>
                    id.Equals(LabImpactProfile.ProfileId) ? profile : (ownLoaded != null && id.Equals(profileId) ? ownLoaded : null),
                SfxLayers = new SfxLayerIndex(LabImpactProfile.LoadSfxRows(world.Registry)),
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
                    new FeedbackAction[] { new PlayImpactAction(profileId) }),
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
                case CombatPoiseChangedEvent pc:
                    // D = 这一击声明的韧性伤害；度量组据 before/after/max 与 D 自己核对扣减规则。
                    _record.Events.Add(new FeelEventRecord(
                        tick, "poise_changed", Label(pc.SourceId), Label(pc.TargetId), string.Empty, string.Empty,
                        "before=" + FeelMetricUtil.Num(pc.Before) + ";after=" + FeelMetricUtil.Num(pc.After) + ";max=" + FeelMetricUtil.Num(pc.Max),
                        pc.Broken ? 1 : 0, 0, 0, pc.Damage));
                    break;
                case CombatPoiseRecoveredEvent pr:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "poise_recovered", string.Empty, Label(pr.TargetId), string.Empty, string.Empty, string.Empty));
                    break;
                case ActionProjectileLaunchedEvent pl:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "projectile_launched", Label(pl.ActorId), string.Empty, string.Empty, string.Empty,
                        "cast=" + _ordinal(pl.CastInstanceId), pl.Segment));
                    break;
                case ActionProjectileEndedEvent pe:
                    _record.Events.Add(new FeelEventRecord(
                        tick, "projectile_ended", Label(pe.ActorId), string.Empty, string.Empty, pe.Reason.ToString(),
                        "cast=" + _ordinal(pe.CastInstanceId), pe.Segment));
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
    /// 实验室的打击反馈包与手感音效索引：读 <c>data/_lab_action</c> 里的 <c>feedback.impact_profile</c> 行
    /// （<see cref="ProfileId"/>）与 <c>sfx.def</c> 里声明了 <c>feel_layer</c> 的行，经生产的 <c>ImpactProfile.FromRecord</c> /
    /// <c>SfxDef.FromRecord</c> 解析（不再内置在内核里，游戏仓库跑自己的实验室时换自己的行）。三个冲击等级（light/medium/heavy）×
    /// 结局（命中/击杀/回避/挥空）；镜头冲击增益 light 0.6、medium 1.0、heavy 1.6，击杀再乘 2，实际幅度还要乘档案的
    /// <c>camera_impulse_gain</c> 并受 <c>camera_shake_cap</c> 限幅（目标选择式预设两者为 0，因此该类格子不出镜头冲击）。
    /// 音效层 id 为 <c>sfx.lab_&lt;层&gt;_t&lt;档&gt;</c>。数据缺行时抛 <see cref="LabFormatException"/>，不静默降级。
    /// </summary>
    internal static class LabImpactProfile
    {
        public static readonly Id ProfileId = new Id("feedback.impact_profile.lab_default");

        /// <summary>
        /// 顿帧期间冻结的表现层：骨骼/序列帧恒冻，粒子也冻（引擎宿主据此验证"顿帧期间被冻结单位名下的粒子停推进"）。
        /// 判断记录：该声明只经反馈 sink 的 <c>FreezePresentation</c> 的 layers 参数传出，记录型假 sink 不记它，
        /// 因此无头宿主的表现时间线与指纹不受影响；它由实验室在读入数据档案后统一覆写到每个变体上
        /// （不放进数据行，免得改动 <c>data/_lab_action</c> 的数据集哈希、进而改动全部既有基线）。
        /// </summary>
        private static readonly ImpactFreezeLayers LabFreezeLayers = new ImpactFreezeLayers(true, false);

        public static ImpactProfile Load(IDataRegistry registry) => Load(registry, ProfileId);

        /// <summary>按 id 读冲击档案行（实验室缺省包或手感表 <c>impact_profile_ref</c> 指到的包），同样覆写顿帧冻结层。</summary>
        public static ImpactProfile Load(IDataRegistry registry, Id profileId)
        {
            var record = registry.Get("feedback.impact_profile", profileId);
            if (record == null)
            {
                throw new LabFormatException(
                    "手感场景需要冲击档案行 " + profileId.Value + "（表 feedback.impact_profile，实验室缺省包随 data/_lab_action）；数据根里没有它。");
            }

            var loaded = ImpactProfile.FromRecord(record);
            var variants = new List<ImpactVariant>(loaded.Variants.Count);
            foreach (var v in loaded.Variants)
            {
                variants.Add(new ImpactVariant(
                    v.ImpactClass, v.Outcome, v.Flash, v.Vfx, v.Sfx, v.Camera, v.FloatingTextStyle, v.Trail, LabFreezeLayers, v.Intensity));
            }

            return new ImpactProfile(loaded.Id, variants);
        }

        public static IEnumerable<SfxDef> LoadSfxRows(IDataRegistry registry)
        {
            var rows = new List<SfxDef>();
            foreach (var record in registry.GetAll("sfx.def"))
            {
                rows.Add(SfxDef.FromRecord(record));
            }

            var layered = 0;
            foreach (var row in rows)
            {
                if (row.FeelLayer != null)
                {
                    layered++;
                }
            }

            if (layered == 0)
            {
                throw new LabFormatException(
                    "手感场景需要带 feel_layer 的 sfx.def 行（实验室 sfx.lab_<层>_t<档>，随 data/_lab_action）；数据根里一行都没有。");
            }

            return rows;
        }
    }

    /// <summary>
    /// 实验室的打击反馈包目录（只读出口）：引擎宿主据此在引擎侧装出同一个反馈包与手感音效目录，不另写一份。
    /// </summary>
    public static class LabFeedbackCatalog
    {
        public static Id ProfileId => LabImpactProfile.ProfileId;

        public static ImpactProfile BuildProfile(IDataRegistry registry) => LabImpactProfile.Load(registry);

        public static IEnumerable<SfxDef> SfxRows(IDataRegistry registry) => LabImpactProfile.LoadSfxRows(registry);
    }
}
