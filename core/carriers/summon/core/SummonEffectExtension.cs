using System;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Core.Carriers.Summon
{
    /// <summary>
    /// <see cref="IEffectExtension"/> 的召唤落地实现（见 06 第 3.2 节 <c>summon</c> 原语"
    /// creature.template 引用、归属"、07 第 4 节）。只处理 <see cref="EffectContext.Kind"/> 为
    /// <see cref="EffectKind.Summon"/> 的效果，其余五类（<c>projectile</c>/<c>open_lock</c>/
    /// <c>create_item</c>/<c>set_world_flag</c>/<c>script</c>）返回 false，交由
    /// <c>EffectDispatcher</c> 兜底或链上的其它扩展实现处理（见 <see cref="IEffectExtension"/>
    /// 顶部注释"六类原语的落地出口...均不在 L2 规则层依赖范围内...本模块只声明这一扩展点"）。
    /// </summary>
    public sealed class SummonEffectExtension : IEffectExtension
    {
        /// <summary><c>position</c> 参数缺省时，召唤物生成点相对施法者位置的偏移量（见任务书拍板
        /// "位置默认施法者位置偏移 1"），沿施法者当前朝向的正前方。</summary>
        private const double DefaultSpawnOffset = 1.0;

        private readonly ISummonHost _summonHost;
        private readonly IUnitAccess _units;

        public SummonEffectExtension(ISummonHost summonHost, IUnitAccess units)
        {
            _summonHost = summonHost ?? throw new ArgumentNullException(nameof(summonHost));
            _units = units ?? throw new ArgumentNullException(nameof(units));
        }

        /// <summary><paramref name="context"/>.Params 期望的形状：
        /// <c>{creature_template: Id（必填）, duration: Number（可选）, position: {x: Number, y: Number}（可选）}</c>
        /// （见 06 第 3.2 节 <c>summon</c> 行参数列"creature.template 引用、归属"，<c>归属</c> 取
        /// <see cref="EffectContext.SourceId"/>，不走 params）。</summary>
        public bool TryHandle(EffectContext context, out ResolveResult result)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (context.Kind != EffectKind.Summon)
            {
                result = FailureResult();
                return false;
            }

            if (!TryReadTemplateId(context.Params, out var templateId))
            {
                result = FailureResult();
                return false;
            }

            double? duration = context.Params.TryGetValue("duration", out var durationValue) &&
                                durationValue is JsonNumber durationNumber
                ? durationNumber.Value
                : (double?)null;

            var position = TryReadPosition(context.Params, out var explicitPosition)
                ? explicitPosition
                : DefaultSpawnPosition(context.SourceId);

            var summonId = _summonHost.Summon(context.SourceId, templateId, position, duration);

            result = new ResolveResult(
                HitResult.Hit,
                requestedAmount: 0,
                finalAmount: 0,
                absorbed: 0,
                immune: false,
                isHeal: false,
                steps: new[] { $"summon:{summonId}" });
            return true;
        }

        private Vec2 DefaultSpawnPosition(Id sourceId)
        {
            var origin = _units.GetPosition(sourceId);
            var facing = _units.GetFacing(sourceId);
            return origin + new Vec2(Math.Cos(facing), Math.Sin(facing)) * DefaultSpawnOffset;
        }

        private static bool TryReadTemplateId(JsonObject @params, out Id templateId)
        {
            if (@params.TryGetValue("creature_template", out var value) &&
                value is JsonString text &&
                Id.TryParse(text.Value, out templateId))
            {
                return true;
            }

            templateId = default;
            return false;
        }

        private static bool TryReadPosition(JsonObject @params, out Vec2 position)
        {
            if (@params.TryGetValue("position", out var value) && value is JsonObject obj &&
                obj.TryGetValue("x", out var xValue) && xValue is JsonNumber x &&
                obj.TryGetValue("y", out var yValue) && yValue is JsonNumber y)
            {
                position = new Vec2(x.Value, y.Value);
                return true;
            }

            position = default;
            return false;
        }

        private static ResolveResult FailureResult() =>
            new ResolveResult(HitResult.Miss, requestedAmount: 0, finalAmount: 0, absorbed: 0, immune: false, isHeal: false);
    }
}
