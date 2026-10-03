using System;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Carriers.Assembly
{
    /// <summary>
    /// 宽限条件求值时 Expr 可读的"施法瞄点上下文"（手感落地 M4-G，手感设计/01 第 2.4 节）：经表达式宿主的 <c>event</c> 分组暴露——条件表达式里写
    /// <c>event.aim_in_range</c> 一类引用。瞄点 = 本次施法请求自己携带的目标/落点，没有时才是求值器的缺省目标（自动攻击的当前目标，或游戏的
    /// <c>GraceTargetResolver</c>）。字段全部惰性计算（距离、视线查询只在表达式真正引用时才做，单位很多时不为用不到的单位付代价）。
    /// <para>
    /// 字段（snake_case 书写，等价的 camelCase 同样可读）：<c>has_aim</c>（Bool，有瞄点）、<c>aim_distance</c>（Number，行动者到瞄点的距离，没有瞄点为 1000000）、
    /// <c>aim_range</c>（Number，这次施法的射程，0 = 没有射程限制）、<c>aim_in_range</c>（Bool，有瞄点且射程为 0 或距离不超过射程）、
    /// <c>aim_line_of_sight</c>（Bool，有瞄点且行动者到瞄点之间视线畅通；没有视线查询的世界视为畅通）、<c>aim_is_ground</c>（Bool，瞄点是地面落点而不是单位）。
    /// </para>
    /// <para>
    /// 判断记录（射程来源）：瞄点自己带射程（施法请求给的，射程取该技能的）优先；没有时取"引用这个条件的输入动作"绑定技能的射程，多个动作引用同一条件时取其中
    /// 最小的正射程——保守：历史里"曾经够得着"的判据宁可严一点，不放行另一个射程更短的动作。所有动作都没有射程限制（或没有动作引用）时射程为 0。
    /// </para>
    /// </summary>
    public sealed class GraceAimContext : IEvent, IExprReadableEvent
    {
        /// <summary>没有瞄点时 <c>aim_distance</c> 的取值（同 <c>enemies.nearest_distance</c> 无敌人的大数约定，可安全参与比较与四则运算）。</summary>
        public const double NoAimDistance = 1_000_000.0;

        /// <summary>事件键（仅用于满足 <see cref="IEvent"/>，上下文不经事件总线发布）。</summary>
        public static readonly Id EventKey = new Id("feel.grace_aim");

        private readonly Vec2? _actorPosition;
        private readonly Vec2? _aimPosition;
        private readonly bool _isGround;
        private readonly Func<double> _range;
        private readonly Func<Vec2, Vec2, bool>? _lineOfSight;
        private readonly Func<bool>? _grounded;

        private double? _rangeValue;
        private double? _distance;
        private bool? _lineOfSightValue;

        /// <param name="actorPosition">行动者位置（查不到为 null，此时没有瞄点）。</param>
        /// <param name="aimPosition">瞄点位置（目标位置或落点；没有为 null）。</param>
        /// <param name="isGround">瞄点是否是地面落点。</param>
        /// <param name="range">这次施法的射程（惰性取值；0 = 没有射程限制）。</param>
        /// <param name="lineOfSight">视线查询（null 视为畅通）。</param>
        public GraceAimContext(
            Vec2? actorPosition, Vec2? aimPosition, bool isGround, Func<double> range, Func<Vec2, Vec2, bool>? lineOfSight)
            : this(actorPosition, aimPosition, isGround, range, lineOfSight, null)
        {
        }

        /// <param name="grounded">行动者此刻是否在地面（ADR-0143，<c>event.grounded</c>；null = 没有竖直运动，恒视为在地面）。</param>
        public GraceAimContext(
            Vec2? actorPosition, Vec2? aimPosition, bool isGround, Func<double> range, Func<Vec2, Vec2, bool>? lineOfSight,
            Func<bool>? grounded)
        {
            _grounded = grounded;
            _actorPosition = actorPosition;
            _aimPosition = aimPosition;
            _isGround = isGround;
            _range = range ?? throw new ArgumentNullException(nameof(range));
            _lineOfSight = lineOfSight;
        }

        public Id Key => EventKey;

        /// <summary>行动者此刻在地面（没有竖直运动查询时恒为真）。</summary>
        public bool Grounded => _grounded == null || _grounded();

        /// <summary>有瞄点（行动者与瞄点的位置都查得到）。</summary>
        public bool HasAim => _actorPosition.HasValue && _aimPosition.HasValue;

        public double AimDistance => _distance ??= HasAim ? Vec2.Distance(_actorPosition!.Value, _aimPosition!.Value) : NoAimDistance;

        public double AimRange => _rangeValue ??= Math.Max(0, _range());

        public bool AimInRange => HasAim && (AimRange <= 0 || AimDistance <= AimRange);

        public bool AimLineOfSight =>
            _lineOfSightValue ??= HasAim && (_lineOfSight == null || _lineOfSight(_actorPosition!.Value, _aimPosition!.Value));

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "has_aim":
                case "hasAim":
                    value = ExprValue.OfBool(HasAim);
                    return true;
                case "aim_distance":
                case "aimDistance":
                    value = ExprValue.OfNumber(AimDistance);
                    return true;
                case "aim_range":
                case "aimRange":
                    value = ExprValue.OfNumber(AimRange);
                    return true;
                case "aim_in_range":
                case "aimInRange":
                    value = ExprValue.OfBool(AimInRange);
                    return true;
                case "aim_line_of_sight":
                case "aimLineOfSight":
                    value = ExprValue.OfBool(AimLineOfSight);
                    return true;
                case "grounded":
                    value = ExprValue.OfBool(Grounded);
                    return true;
                case "aim_is_ground":
                case "aimIsGround":
                    value = ExprValue.OfBool(_isGround);
                    return true;
                default:
                    value = default;
                    return false;
            }
        }
    }

    /// <summary>
    /// 缺省 Expr 宽限求值器取"瞄点上下文"所需的世界查询（手感落地 M4-G）：位置、视线、"引用某条件的动作绑定技能的射程"。生产装配（<see cref="CarriersFeelAssembly"/>）
    /// 自动提供；测试或自建装配可自行赋值。<see cref="Position"/> 缺省时求值器不提供 <c>event.aim_*</c> 上下文。
    /// </summary>
    public sealed class GraceAimServices
    {
        /// <summary>实体位置；查不到返回 null。</summary>
        public Func<Id, Vec2?>? Position { get; set; }

        /// <summary>两点之间视线是否畅通；null 视为畅通。</summary>
        public Func<Vec2, Vec2, bool>? LineOfSight { get; set; }

        /// <summary>（行动者, 条件）对应的射程：引用该条件的输入动作所绑定技能的射程（取最小的正射程）；null 或返回 0 视为没有射程限制。</summary>
        public Func<Id, Id, double>? ConditionRange { get; set; }

        /// <summary>行动者此刻是否在地面（ADR-0143，<c>event.grounded</c>）；null 视为恒在地面。生产装配在装了竖直轴时提供。</summary>
        public Func<Id, bool>? Grounded { get; set; }
    }
}
