using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 输入动作到技能的映射（动作时间线的取消进入用，手感设计/01 第 2.3/3.4 节）：缓冲记录在取消窗口内被接受后，由本接口给出
    /// "这条记录要开始哪个技能"。映射是游戏装配层的事（输入动作 id 与技能的对应、武器切换后的普攻技能等），框架不预置。
    /// 返回 false 表示这条记录不对应任何技能（时间线不接受它，记录保留在缓冲里）。
    /// </summary>
    public interface IActionSkillBinding
    {
        bool TryResolveSkill(Id actorId, BufferedIntent intent, out Id skillId);
    }

    /// <summary>
    /// 时间线动作的开始上下文（随施法请求携带，不改 <see cref="ISkillHost.CastSkill"/> 契约）：按下瞬间的移动轴方向与蓄力按住时长。
    /// 取消进入由时间线从缓冲记录自行填入；游戏装配层经意图参数（<c>direction</c>、<c>held_ticks</c>）或
    /// <see cref="SkillHost.CastSkillWithContext"/> 传入。
    /// </summary>
    public readonly struct ActionCastContext
    {
        /// <summary>按下瞬间的移动轴方向；无轴输入为 null。</summary>
        public Vec2? Direction { get; }

        /// <summary>蓄力按住的 tick 数（非蓄力动作为 0）。</summary>
        public int HeldTicks { get; }

        public ActionCastContext(Vec2? direction, int heldTicks)
        {
            Direction = direction;
            HeldTicks = heldTicks;
        }
    }

    /// <summary>
    /// 一次 <c>hit</c> 标记到达时交给命中解析器的上下文（手感设计/03 第 2.2 节）。只含结算所需的身份与出口，
    /// 空间命中（目标选择链的几何、攻击实例去重、无敌前置检查）由命中解析器自己负责。
    /// </summary>
    public readonly struct TimelineHitContext
    {
        public Id CasterId { get; }

        public SkillDef Def { get; }

        public Id CastInstanceId { get; }

        public int ComboIndex { get; }

        /// <summary>多段技能的段序号（单段为 0，来自标记 <c>segment</c> 参数或 <c>hit:&lt;段&gt;</c> 写法）。</summary>
        public int Segment { get; }

        /// <summary>施法请求显式给出的目标（可为空）。</summary>
        public IReadOnlyList<Id> ExplicitTargets { get; }

        /// <summary>结算出口：解析目标与按既有效果管线结算。</summary>
        public ITimelineSettlement Settlement { get; }

        public TimelineHitContext(
            Id casterId, SkillDef def, Id castInstanceId, int comboIndex, int segment,
            IReadOnlyList<Id> explicitTargets, ITimelineSettlement settlement)
        {
            CasterId = casterId;
            Def = def;
            CastInstanceId = castInstanceId;
            ComboIndex = comboIndex;
            Segment = segment;
            ExplicitTargets = explicitTargets;
            Settlement = settlement;
        }
    }

    /// <summary>时间线命中到结算的出口（由施法管线提供，复用 <c>instant</c> 模式的目标选择与效果结算）。</summary>
    public interface ITimelineSettlement
    {
        /// <summary>用技能的目标选择链（<c>target_shape_ref</c>）以施法者当前位置为锚点解析一次目标。</summary>
        IReadOnlyList<Id> ResolveTargets();

        /// <summary>对给定目标按技能效果列表结算一次（走既有效果结算管线，每次调用一个攻击实例 id）。</summary>
        void SettleOn(IReadOnlyList<Id> targets);

        /// <summary>
        /// 缺省结算（等价于 <c>instant</c> 模式的步骤 6/9）：有显式目标则按显式目标过滤后结算，否则以目标选择链解析后结算，
        /// 没有目标不结算（时间线模式没有目标不是失败，手感设计/03 第 2.1 节）。
        /// </summary>
        void SettleInstant();
    }

    /// <summary>
    /// 命中解析钩子（手感设计/03 第 2.2 节）：<c>hit</c> 标记到达时调用一次。缺省实现 <see cref="InstantSettlementHitResolver"/>
    /// 复用 <c>instant</c> 模式的目标选择与结算；时间线命中切片以空间命中、攻击实例去重、无敌前置检查替换它。
    /// </summary>
    public interface ITimelineHitResolver
    {
        void ResolveHit(in TimelineHitContext context);
    }

    /// <summary>缺省命中解析：每个 <c>hit</c> 标记到达时按 <c>instant</c> 模式结算一次（目标选择链 → 效果结算）。</summary>
    public sealed class InstantSettlementHitResolver : ITimelineHitResolver
    {
        public void ResolveHit(in TimelineHitContext context) => context.Settlement.SettleInstant();
    }

    /// <summary>目标辅助的解析请求（时间线 → 目标辅助实现；全部量已换算为世界单位/度，手感设计/02 第 5 节）。</summary>
    public readonly struct ActionAssistRequest
    {
        public Id ActorId { get; }

        public Id CastInstanceId { get; }

        /// <summary>候选解析用的目标选择链。</summary>
        public Id ChainRef { get; }

        /// <summary>候选允许的最大距离（世界单位）。</summary>
        public double MaxDistance { get; }

        /// <summary>候选相对朝向允许的最大角度（度，单侧）。</summary>
        public double MaxAngleDeg { get; }

        public TimelineAssistMode Mode { get; }

        /// <summary>档案动作组 <c>turn_assist_deg</c>（度）：朝向修正上限。</summary>
        public double TurnAssistDeg { get; }

        /// <summary>动作位移声明距离（世界单位，标定后；无位移块为 0）：<c>close_distance</c> 缩放上限。</summary>
        public double DeclaredDistance { get; }

        /// <summary>判定形状从行动者向前的覆盖深度（世界单位）：<c>close_distance</c> 缩放依据。</summary>
        public double ShapeReach { get; }

        public ActionAssistRequest(
            Id actorId, Id castInstanceId, Id chainRef, double maxDistance, double maxAngleDeg, TimelineAssistMode mode,
            double turnAssistDeg, double declaredDistance, double shapeReach)
        {
            ActorId = actorId;
            CastInstanceId = castInstanceId;
            ChainRef = chainRef;
            MaxDistance = maxDistance;
            MaxAngleDeg = maxAngleDeg;
            Mode = mode;
            TurnAssistDeg = turnAssistDeg;
            DeclaredDistance = declaredDistance;
            ShapeReach = shapeReach;
        }
    }

    /// <summary>目标辅助的结果：辅助目标、朝向修正量（度，带符号，已按 <c>turn_assist_deg</c> 限幅）、距离修正量（世界单位）。</summary>
    public readonly struct ActionAssistOutcome
    {
        public Id TargetId { get; }

        public double FacingDeltaDeg { get; }

        public double DistanceAdjust { get; }

        public ActionAssistOutcome(Id targetId, double facingDeltaDeg, double distanceAdjust)
        {
            TargetId = targetId;
            FacingDeltaDeg = facingDeltaDeg;
            DistanceAdjust = distanceAdjust;
        }
    }

    /// <summary>
    /// 目标辅助入口（手感设计/02 第 5 节）。<b>缺省关闭</b>：<see cref="TimelineServices.TargetAssist"/> 为空即没有目标辅助，
    /// 声明了 <c>target_assist</c> 的动作也按未声明处理。时间线（L2）只认本接口；实现在载体层
    /// （<c>Core.Carriers.Unit.ActionTargetAssistAdapter</c>，复用运动侧 <c>TargetAssistEvaluator</c> 与目标选择链候选解析）。
    /// 没有候选返回 false（静默，不发事件）。
    /// </summary>
    public interface IActionTargetAssist
    {
        bool TryAssist(in ActionAssistRequest request, out ActionAssistOutcome outcome);
    }

    /// <summary>剪辑里的一个事件（<c>display.anim_set.clips[*].events</c> 的一项：名 + 时间百分比）。</summary>
    public readonly struct ClipEvent
    {
        public string Name { get; }

        /// <summary>占整段剪辑时长的比例（0～1）。</summary>
        public double TimePct { get; }

        public ClipEvent(string name, double timePct)
        {
            Name = name;
            TimePct = timePct;
        }
    }

    /// <summary>一段剪辑的标记元数据（总时长 + 事件列表）；校验器与导入工具读它，运行期不读（规则层不读表现域）。</summary>
    public sealed class ClipMarkerSet
    {
        public string ClipName { get; }

        public double TotalMs { get; }

        public IReadOnlyList<ClipEvent> Events { get; }

        public ClipMarkerSet(string clipName, double totalMs, IReadOnlyList<ClipEvent> events)
        {
            ClipName = clipName;
            TotalMs = totalMs;
            Events = events;
        }
    }

    /// <summary>
    /// 技能对应剪辑标记的读取口（手感设计/01 第 3.2 节 <c>source: clip</c>）：只用于作者态校验与导入，运行期永远只读
    /// <c>skill.def.timeline</c>。生产实现需要"技能 → 动画集 → 剪辑"的对应关系，当前数据里没有这条链接（见 skill 模块 README
    /// 已知局限），所以本切片只提供接口与内存替身（<see cref="InMemoryClipMarkerSource"/>）。
    /// </summary>
    public interface IClipMarkerSource
    {
        bool TryGetClip(Id skillId, out ClipMarkerSet clip);
    }

    /// <summary>内存版 <see cref="IClipMarkerSource"/>（测试与工具替身）。</summary>
    public sealed class InMemoryClipMarkerSource : IClipMarkerSource
    {
        private readonly Dictionary<Id, ClipMarkerSet> _clips = new Dictionary<Id, ClipMarkerSet>();

        public InMemoryClipMarkerSource Add(Id skillId, ClipMarkerSet clip)
        {
            _clips[skillId] = clip;
            return this;
        }

        public bool TryGetClip(Id skillId, out ClipMarkerSet clip) => _clips.TryGetValue(skillId, out clip!);
    }

    /// <summary>
    /// 动作时间线的可选协作者（<see cref="SkillHost.AttachTimelineServices"/>）。判断记录（为什么不是构造参数）：
    /// <see cref="SkillHost"/> 的构造函数已经历多轮"追加参数破坏 ABI"，本切片沿用 <see cref="SkillHost.DisplacementSink"/>
    /// 的组装期属性赋值惯例。全部成员可空：缺失时对应能力按保守缺省降级（见各成员说明）。
    /// </summary>
    public sealed class TimelineServices
    {
        /// <summary>
        /// 行动者动作时钟（被局部顿帧暂停）。为空时时间线每个 <c>SkillHost.Update</c> 调用推进一个动作 tick
        /// （没有顿帧语义，仅供未装配动作时钟的最小组合使用）。
        /// </summary>
        public IActorActionClockQuery? Clock { get; set; }

        /// <summary>
        /// 手感解析器：用于动作开始快照（<c>BeginAction</c>）与读取判定型倍率。为空时全部档案倍率取 1、窗口倍率取 1、
        /// 无动作时长下限、连招重置时长为 0（连招只在窗口内接续，不跨待机残留）。
        /// </summary>
        public IFeelResolver? Feel { get; set; }

        /// <summary>
        /// 输入缓冲（只用 <see cref="IInputBufferQuery.TryConsume"/>/<see cref="IInputBufferQuery.ReportRejected"/>）。为空时
        /// 时间线不主动拉取缓冲：取消进入与窗口内连招只能经 <see cref="SkillHost.CastSkill"/> 之外的显式入口触发。
        /// </summary>
        public IInputBufferQuery? Input { get; set; }

        /// <summary>输入动作 → 技能映射；为空时取消进入不可用（窗口内连招不需要映射，仍可用）。</summary>
        public IActionSkillBinding? Binding { get; set; }

        /// <summary>
        /// 命中解析钩子；为空时由施法管线按技能数据选择内建路径：目标选择链声明了 <c>shape</c> 则空间命中
        /// （去重、无敌前置检查、<c>combat.hit_confirmed</c>，手感设计/03 第 2.2/2.3 节），没有 <c>shape</c> 则
        /// <see cref="InstantSettlementHitResolver"/> 式 instant 结算。设置了本钩子即完全由它接管 <c>hit</c> 标记的解析
        /// （去重与无敌检查随之由钩子负责）。
        /// </summary>
        public ITimelineHitResolver? HitResolver { get; set; }

        /// <summary>
        /// 受击裁决（<see cref="IHitFeelArbiter"/>，<c>HitFeelHost</c> 实现）：时间线命中发 <c>combat.hit_confirmed</c> 前取冲击等级、
        /// 两侧顿帧 tick 数与受击反应。为空时全部取 <see cref="HitFeelOutcome.None"/>（不顿帧、不裁决，但事件照发、几何字段齐全）。
        /// </summary>
        public IHitFeelArbiter? HitFeel { get; set; }

        /// <summary>目标辅助入口（缺省 null：关闭，见 <see cref="IActionTargetAssist"/>）。</summary>
        public IActionTargetAssist? TargetAssist { get; set; }
    }
}
