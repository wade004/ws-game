using System;
using System.Collections.Generic;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using Presentation.Render;

namespace Lab
{
    /// <summary>
    /// 空中姿势装置（ADR-0130 追加决定"空中姿势"）：脚本声明了合成姿势键表（<c>meta.spaceExt.poseKeys</c>）且格子带竖直轴时装出——
    /// 用与生产装配同一批部件：<see cref="PoseSelector"/>（姿势上下文）+ <see cref="AirPoseFeeder"/>（按竖直运动服务的竖直状态发布空中阶段），
    /// 再用 <see cref="PoseContext.TryGetAirRequest"/> 与 <see cref="PoseResolver"/> 解析 <c>jump.rise/fall/land</c>、<c>hit.air</c>、
    /// <c>attack.air[.族]</c> 请求。键表是合成的（没有真实姿势集），只回答"请求哪个键、链上命中了哪个、回落了几级"，不播放任何东西。
    /// <para>
    /// 判断记录（受击/出手的时间口径）：命中/出手看到的是<b>上一个固定步末尾</b>的空中阶段——核心 tick 内技能管线（命中判定、击飞开始）
    /// 早于移动与竖直积分，且呈现层的空中阶段在 tick 结束事件里才更新；用本步末尾的状态会把"这一击把目标打飞"误判成"在空中被打"。
    /// 同 <see cref="SpaceMetricGroup"/> 的命中高度差口径。
    /// </para>
    /// </summary>
    internal sealed class AirPoseRig : IDisposable
    {
        private readonly PoseSelector _selector = new PoseSelector();
        private readonly AirPoseFeeder _feeder;
        private readonly HashSet<string> _keys;
        private readonly string _family;
        private readonly SpaceExtRecording _record;
        private readonly Id _player;
        private readonly Dictionary<Id, AirPhase> _previous = new Dictionary<Id, AirPhase>();
        private readonly Func<Id, string> _label;
        private AirPhase _lastPlayerPhase = AirPhase.None;

        public AirPoseRig(
            IEventBus bus, IVerticalMotion motion, ScriptSpaceOptions options, SpaceExtRecording record, Id player, Func<Id, string> label)
        {
            _keys = new HashSet<string>(options.PoseKeys, StringComparer.Ordinal);
            _family = options.PoseFamily;
            _record = record;
            _player = player;
            _label = label;
            _feeder = new AirPoseFeeder(bus, motion, _selector);
        }

        private static string PhaseName(AirPhase phase) => phase switch
        {
            AirPhase.Rise => "rise",
            AirPhase.Fall => "fall",
            AirPhase.Land => "land",
            _ => string.Empty,
        };

        private void Resolve(int tick, string kind, Id entity, AirPoseRequest request)
        {
            var resolution = PoseResolver.Resolve(request, _keys.Contains);
            _record.AirPoses.Add(new AirPoseRecord(
                tick, kind, _label(entity), request.FullKey(), resolution.Found ? resolution.CanonicalKey : string.Empty,
                resolution.Found ? resolution.FallbackDepth : -1));
        }

        /// <summary>每个固定步结束（派发事件之后）：记玩家当前阶段（并在阶段切换处解析跳跃姿势），再快照全部观测实体的阶段供下一步的命中/出手用。</summary>
        public void EndTick(int tick, IEnumerable<Id> entities)
        {
            var phase = _selector.GetContext(_player).Air;
            _record.PlayerAirPhases.Add(PhaseName(phase));
            if (phase != _lastPlayerPhase && phase != AirPhase.None)
            {
                Resolve(tick, "phase", _player, AirPoseRequest.Jump(phase == AirPhase.Rise ? AirPoseRequest.PhaseRise
                    : phase == AirPhase.Fall ? AirPoseRequest.PhaseFall : AirPoseRequest.PhaseLand));
            }

            _lastPlayerPhase = phase;
            _previous[_player] = phase;
            foreach (var id in entities)
            {
                _previous[id] = _selector.GetContext(id).Air;
            }
        }

        /// <summary>派发到的逻辑事件：目标被打（<c>hit</c>）与玩家出手（<c>attack</c>）按上一步末尾的阶段解析空中请求。</summary>
        public void OnEvent(IEvent evt, int tick)
        {
            switch (evt)
            {
                case CombatDamageDealtEvent d when d.SourceId.Equals(_player):
                    Request(tick, "hit", d.TargetId, "hit", null);
                    break;
                case SkillCastSuccessEvent s when s.CasterId.Equals(_player):
                    Request(tick, "attack", s.CasterId, "attack", _family);
                    break;
            }
        }

        private void Request(int tick, string kind, Id entity, string stateKey, string? family)
        {
            var phase = _previous.TryGetValue(entity, out var p) ? p : AirPhase.None;
            var context = new PoseContext(LocomotionGait.Idle, family, null, phase);
            if (context.TryGetAirRequest(stateKey, out var request))
            {
                Resolve(tick, kind, entity, request);
            }
        }

        public void Dispose() => _feeder.Dispose();
    }
}
