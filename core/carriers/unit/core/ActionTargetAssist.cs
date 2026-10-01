using System;
using Core.Foundation.Common;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 目标辅助的候选解析：用目标选择链（<c>target.chain_def</c>，06 第 5 节）解析候选，取第一个落在 <c>max_distance</c> 与
    /// <c>max_angle_deg</c> 内的存活候选（手感设计/02 第 5 节）。实现 <see cref="ITargetAssistResolver"/>，可直接赋给
    /// <see cref="MotionServices.TargetAssist"/>，也被 <see cref="ActionTargetAssistAdapter"/> 复用。链的顺序即候选顺序
    /// （按链自己的 <c>sort_by</c>；没有 <c>sort_by</c> 的链按来源策略的自然序）。没有候选返回 false（静默）。
    /// </summary>
    public sealed class TargetChainAssistResolver : ITargetAssistResolver
    {
        private readonly ITargetHost _targets;
        private readonly IUnitAccess _units;

        public TargetChainAssistResolver(ITargetHost targets, IUnitAccess units)
        {
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
            _units = units ?? throw new ArgumentNullException(nameof(units));
        }

        public bool TryResolve(in TargetAssistRequest request, out TargetAssistCandidate candidate)
        {
            candidate = default;
            var actor = request.ActorId;
            if (!_units.Exists(actor))
            {
                return false;
            }

            var actorPosition = _units.GetPosition(actor);
            var actorFacing = _units.GetFacing(actor);
            var candidates = _targets.Resolve(new Id(request.ChainRef), actor);
            for (var i = 0; i < candidates.Count; i++)
            {
                var id = candidates[i];
                if (id.Equals(actor) || !_units.Exists(id) || !_units.IsAlive(id))
                {
                    continue;
                }

                var position = _units.GetPosition(id);
                var to = position - actorPosition;
                var distance = to.Length;
                if (distance > request.MaxDistance)
                {
                    continue;
                }

                var angleDeg = distance > 1e-9
                    ? MotionMath.WrapAngle(Math.Atan2(to.Y, to.X) - actorFacing) * (180.0 / Math.PI)
                    : 0.0;
                if (Math.Abs(angleDeg) > request.MaxAngleDeg)
                {
                    continue;
                }

                candidate = new TargetAssistCandidate(id, position);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 时间线（L2）目标辅助入口 <see cref="IActionTargetAssist"/> 的载体层实现：候选解析交给 <see cref="ITargetAssistResolver"/>
    /// （通常是 <see cref="TargetChainAssistResolver"/>，也可以是游戏层自己的软锁定实现），再用运动侧纯函数
    /// <see cref="TargetAssistEvaluator"/> 算朝向修正（不超过档案 <c>turn_assist_deg</c>）与 <c>close_distance</c> 的距离缩放。
    /// 装配：<c>skillHost.AttachTimelineServices(new TimelineServices { TargetAssist = new ActionTargetAssistAdapter(resolver, units), ... })</c>；
    /// 不装配即没有目标辅助（缺省关闭）。
    /// </summary>
    public sealed class ActionTargetAssistAdapter : IActionTargetAssist
    {
        private readonly ITargetAssistResolver _resolver;
        private readonly IUnitAccess _units;

        public ActionTargetAssistAdapter(ITargetAssistResolver resolver, IUnitAccess units)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _units = units ?? throw new ArgumentNullException(nameof(units));
        }

        public bool TryAssist(in ActionAssistRequest request, out ActionAssistOutcome outcome)
        {
            outcome = default;
            var mode = request.Mode == TimelineAssistMode.CloseDistance ? TargetAssistMode.CloseDistance : TargetAssistMode.FaceOnly;
            var resolverRequest = new TargetAssistRequest(
                request.ActorId, request.ChainRef.ToString(), request.MaxDistance, request.MaxAngleDeg, mode);
            if (!_resolver.TryResolve(resolverRequest, out var candidate))
            {
                return false;
            }

            if (!TargetAssistEvaluator.TryEvaluate(
                    _units.GetPosition(request.ActorId), _units.GetFacing(request.ActorId), candidate, resolverRequest,
                    request.TurnAssistDeg, request.DeclaredDistance, request.ShapeReach, out var result))
            {
                return false;
            }

            outcome = new ActionAssistOutcome(result.TargetId, result.FacingDeltaDeg, result.DistanceAdjust);
            return true;
        }
    }
}
