using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Sim;
using Presentation.Render;

namespace Lab
{
    /// <summary>
    /// 姿势与动画观测装置（脚本 <c>meta.poseExt</c>，M5-S4，ADR-0147）：装出与生产装配同一批表现层部件——<see cref="AnimStateMachine"/>
    /// （装配了手感受击裁决且为动作式时带 <c>IHitReactionQuery</c> 进入受击反应驱动模式，同 <c>PresentationAssembly</c> 的判断）、
    /// <see cref="PoseSelector"/> + <see cref="PoseGaitFeeder"/>（步态与移动呈现参数）、<see cref="ClipPlaybackRates"/>（动作各相位与移动的剪辑播放速率）——
    /// 并把它们对外说的话记成逻辑时间线事件：
    /// <list type="bullet">
    /// <item><c>pose_state</c>：动画状态进入或再触发（<c>Target</c> = 出场标签，<c>Detail</c> = 状态名，<c>Detail2</c> = 受击姿势子键，没有则空）；</item>
    /// <item><c>pose_rate</c>：该实体当前状态下的剪辑播放速率变化（<c>Detail</c> = 状态名，<c>D</c> = 速率）。</item>
    /// </list>
    /// 判断记录：本装置只订阅、只读，不向核心写任何东西；没有引擎，所以"剪辑播放"本身不在这里——这里只验证表现层对外发出的请求
    /// （哪个姿势、什么速率），引擎侧把速率交给播放器的最后一跳由引擎侧 PlayMode 用例验收。
    /// </summary>
    internal sealed class PoseRig : IDisposable
    {
        private readonly FeelRecording _record;
        private readonly Func<Id, string> _label;
        private readonly Func<int> _tick;
        private readonly AnimStateMachine _machine;
        private readonly ClipPlaybackRates _rates;
        private readonly PoseGaitFeeder _gait;
        private readonly LocomotionPresentation _locomotion = new LocomotionPresentation();
        private readonly Dictionary<Id, double> _lastRate = new Dictionary<Id, double>();

        public PoseRig(
            HeadlessWorld world, Core.Carriers.Assembly.CarriersFeelSystem feel, FeelRecording record, Dictionary<Id, string> labels,
            double step, Id playerId, IEnumerable<KeyValuePair<string, Id>> targets, Func<int> tick)
        {
            _record = record;
            _tick = tick;
            _label = id => labels.TryGetValue(id, out var l) ? l : id.Value;

            var host = feel.Rules.HitFeel.Host;
            _machine = host != null && host.Active
                ? new AnimStateMachine(world.Bus, null, host)
                : new AnimStateMachine(world.Bus);
            var selector = new PoseSelector();
            _gait = new PoseGaitFeeder(world.Bus, world.World, selector, feel.Resolver, _locomotion, step);
            _rates = new ClipPlaybackRates(world.Bus, _machine, _locomotion);

            _machine.Track(playerId);
            foreach (var target in targets)
            {
                _machine.Track(target.Value);
            }

            _machine.StateChangedWithSkill += (id, _, state, __) => OnState(id, state);
            _machine.StateRetriggered += (id, state, _) => OnState(id, state);
            _rates.RateChanged += id => RecordRate(id);
        }

        private void OnState(Id id, AnimState state)
        {
            var sub = state == AnimState.Hit ? _machine.GetHitPoseSub(id) : null;
            _record.Events.Add(new FeelEventRecord(
                _tick(), "pose_state", string.Empty, _label(id), string.Empty, state.ToString(), sub ?? string.Empty));
            RecordRate(id);
        }

        private void RecordRate(Id id)
        {
            var rate = _rates.GetRate(id);
            if (_lastRate.TryGetValue(id, out var last) && Math.Abs(last - rate) < 1e-9)
            {
                return;
            }

            _lastRate[id] = rate;
            _record.Events.Add(new FeelEventRecord(
                _tick(), "pose_rate", string.Empty, _label(id), string.Empty, _machine.GetState(id).ToString(), string.Empty,
                0, 0, 0, rate));
        }

        public void Dispose()
        {
            _rates.Dispose();
            _gait.Dispose();
            _machine.Dispose();
        }
    }
}
