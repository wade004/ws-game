using Core.Foundation.Common;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 硬直状态只读查询（手感设计/02 第 3.1 节 <c>staggered</c>）：受击裁决（03 第 4 节）落地后由受击裁决模块实现。
    /// 运动仲裁器只问"此刻是否处于硬直"，不知道硬直怎么来的。
    /// </summary>
    public interface IStaggerStateQuery
    {
        bool IsStaggered(Id unitId);
    }

    /// <summary>
    /// 已删除（ADR-0147）：剪辑根运动来源。根运动由表现帧累加、逻辑 tick 取走，模拟结果依赖表现帧率与引擎动画求值，违反"判定与帧率无关"，
    /// 无头回放与指纹基线无法覆盖。接口只为程序集接口兼容保留，运动层不再读取它；带位移的动画改走导入期烘焙的位移曲线（<see cref="IMotionCurveSource"/>）。
    /// </summary>
    [System.Obsolete("根运动已删除（ADR-0147）：改用导入期烘焙的位移曲线（skill.motion_curve + motion.curve = custom:<id>）")]
    public interface IRootMotionSource
    {
        /// <summary>适配层能力 <c>supportsRootMotion</c>；为 false 时声明了 <c>root_motion</c> 的动作位移报错，不降级。</summary>
        bool SupportsRootMotion { get; }

        /// <summary>取走并清零该单位自上次取走以来累加的根位移增量（世界单位）。</summary>
        Vec2 ConsumeRootMotionDelta(Id unitId);
    }

    /// <summary>
    /// 曲线引用 <c>custom:&lt;curve_id&gt;</c> 的解析（04 第 3.6 节曲线形态登记）：返回把 [0,1] 映到 [0,1] 的断点曲线
    /// （单调不减）；找不到返回 null（运行期按"曲线引用无法解析"报错，不静默改为线性）。
    /// </summary>
    public interface IMotionCurveSource
    {
        PiecewiseCurve? GetCurve(string curveId);
    }

    /// <summary>目标辅助的模式（手感设计/02 第 5 节 <c>target_assist.mode</c>）。</summary>
    public enum TargetAssistMode
    {
        /// <summary>只把朝向对齐到目标（不超过 <c>turn_assist_deg</c>）。</summary>
        FaceOnly,

        /// <summary>还把本次位移距离缩放到"判定相形状恰好覆盖目标"所需的值（不超过声明距离上限）。</summary>
        CloseDistance,
    }

    /// <summary>目标辅助的解析请求（手感设计/02 第 5 节）；候选由目标选择链（06 第 5 节）产出。</summary>
    public readonly struct TargetAssistRequest
    {
        public Id ActorId { get; }

        /// <summary>动作定义里 <c>target_assist.chain_ref</c>（目标选择链引用）。</summary>
        public string ChainRef { get; }

        /// <summary>候选目标允许的最大距离（世界单位）。</summary>
        public double MaxDistance { get; }

        /// <summary>候选目标相对朝向允许的最大角度（度，单侧）。</summary>
        public double MaxAngleDeg { get; }

        public TargetAssistMode Mode { get; }

        public TargetAssistRequest(Id actorId, string chainRef, double maxDistance, double maxAngleDeg, TargetAssistMode mode)
        {
            ActorId = actorId;
            ChainRef = chainRef;
            MaxDistance = maxDistance;
            MaxAngleDeg = maxAngleDeg;
            Mode = mode;
        }
    }

    /// <summary>目标辅助候选（目标选择链解析出的第一个候选）。</summary>
    public readonly struct TargetAssistCandidate
    {
        public Id TargetId { get; }

        public Vec2 Position { get; }

        public TargetAssistCandidate(Id targetId, Vec2 position)
        {
            TargetId = targetId;
            Position = position;
        }
    }

    /// <summary>
    /// 目标辅助的候选解析入口（手感设计/02 第 5 节）。<b>缺省关闭</b>：没有注入实现（<see cref="MotionServices.TargetAssist"/>
    /// 为 null）即没有任何目标辅助；实现随目标选择链接入的切片提供。动作时间线在动作被接受时调用，再用
    /// <see cref="TargetAssistEvaluator"/> 算出朝向修正与距离缩放并发 <c>action.target_assisted</c>。
    /// </summary>
    public interface ITargetAssistResolver
    {
        /// <summary>按目标选择链取第一个落在 <c>max_distance</c> 与 <c>max_angle_deg</c> 内的候选；没有候选返回 false（静默）。</summary>
        bool TryResolve(in TargetAssistRequest request, out TargetAssistCandidate candidate);
    }

    /// <summary>
    /// 运动服务装配点（手感设计/02；ADR-0116）：<see cref="MovementHost.Motion"/> 持有它，<c>MovementTickHandler</c>
    /// 每 tick 读取。<b>不赋值（null）或 <see cref="Feel"/> 为 null 即没有运动档案，移动系统逐位保持既有行为</b>。
    /// 全部成员可空（缺省视为"该来源恒为空"）：没有 <see cref="Actions"/> 就没有 <c>action</c> 模式，没有
    /// <see cref="ActionClock"/> 就没有顿帧叠加态，依此类推。
    /// </summary>
    public sealed class MotionServices
    {
        /// <summary>判定型手感入口：运动档案（<c>accel_ms</c> 等判定型字段）经它读取。</summary>
        public IFeelJudgingSource? Feel { get; set; }

        /// <summary>动作状态只读查询：<c>action</c> 模式与动作位移段。</summary>
        public IActionStateQuery? Actions { get; set; }

        /// <summary>行动者动作时钟只读查询：被暂停即 <c>frozen</c> 叠加态。</summary>
        public IActorActionClockQuery? ActionClock { get; set; }

        /// <summary>硬直状态只读查询：<c>staggered</c> 模式。</summary>
        public IStaggerStateQuery? Stagger { get; set; }

        /// <summary>已删除（ADR-0147）：赋值被忽略，运动层不读取。</summary>
        [System.Obsolete("根运动已删除（ADR-0147）；赋值被忽略")]
        public IRootMotionSource? RootMotion { get; set; }

        /// <summary>自定义曲线解析（<c>custom:&lt;id&gt;</c>）。</summary>
        public IMotionCurveSource? Curves { get; set; }

        /// <summary>目标辅助候选解析（缺省 null：关闭）。</summary>
        public ITargetAssistResolver? TargetAssist { get; set; }

        /// <summary>运动模式规则（<c>feel.motion_mode_rules</c> 的消费结果）；缺省等于框架数据里的试调起点。</summary>
        public MotionModeRuleSet ModeRules { get; set; } = MotionModeRuleSet.Default;
    }
}
