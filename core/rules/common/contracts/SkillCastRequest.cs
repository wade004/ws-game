using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 一次施法请求（见 06 第 6.5 节 <c>AiHost.evaluate</c> 返回值 <c>Optional&lt;SkillCastRequest&gt;</c>）。
    /// 玩家输入层与 AI 的 <c>Rotation</c> 求值都产出同一种请求结构，再统一交给
    /// <see cref="ISkillHost.CastSkill"/>（06 第 6.2 节"玩家辅助施法与敌方 AI 使用同一种数据格式"的
    /// 请求侧对应物）。
    /// </summary>
    public readonly struct SkillCastRequest
    {
        public Id CasterId { get; }

        public Id SkillId { get; }

        private readonly IReadOnlyList<Id>? _targets;

        /// <summary>目标单位列表；未指定时为空列表（不是 null），供仅需 <see cref="TargetPoint"/> 的
        /// 落点类技能（如 <c>projectile</c>/<c>teleport</c> 指向坐标）省略。</summary>
        public IReadOnlyList<Id> Targets => _targets ?? Array.Empty<Id>();

        /// <summary>落点坐标；仅当技能的目标解析形状需要坐标（而非单位）时提供。</summary>
        public Vec2? TargetPoint { get; }

        public SkillCastRequest(Id casterId, Id skillId, IReadOnlyList<Id>? targets = null, Vec2? targetPoint = null)
        {
            CasterId = casterId;
            SkillId = skillId;
            _targets = targets ?? Array.Empty<Id>();
            TargetPoint = targetPoint;
        }
    }
}
