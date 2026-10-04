using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 动作位移的驱动方式（手感设计/02 第 4 节）。ADR-0147 起只剩代码驱动：位移由逻辑层按距离与曲线逐固定步算出（确定性，不依赖表现帧率）；
    /// 美术带位移的动画走导入期烘焙（根位移曲线抄写为 <c>skill.motion_curve</c> 行，动作位移引用 <c>custom:&lt;curve_id&gt;</c>）。
    /// </summary>
    public enum ActionMotionDriver
    {
        /// <summary>代码驱动：逻辑层按距离与曲线逐 tick 算出位移。</summary>
        Code,

        /// <summary>
        /// 已删除（ADR-0147）：枚举成员只为保持程序集接口兼容而保留（ABI 只增不删）。数据里写 <c>driver: root_motion</c> 在加载期报错并给出迁移说明
        /// （<c>SkillTimelineRule</c>），运动层遇到该值直接抛异常，不再有根运动路径。
        /// </summary>
        [System.Obsolete("root_motion 驱动已删除（ADR-0147）：改用导入期烘焙的位移曲线（skill.motion_curve + motion.curve = custom:<id>）")]
        RootMotion,
    }

    /// <summary>动作位移的种类（手感设计/02 第 4 节 <c>ActionMotion.kind</c>）。</summary>
    public enum ActionMotionKind
    {
        Lunge,
        Dash,
        StepBack,

        /// <summary>冲向目标：距离上限、转角上限与有效时间三者必须声明，到达目标身前 <c>stop_distance</c> 即停。</summary>
        Charge,
    }

    /// <summary>位移方向的来源（手感设计/02 第 4 节 <c>ActionMotion.direction</c>）。</summary>
    public enum ActionMotionDirection
    {
        /// <summary>当前朝向。</summary>
        Facing,

        /// <summary>动作被接受那一刻的移动输入方向快照。</summary>
        InputSnapshot,

        /// <summary>朝目标（目标辅助或冲向目标）。</summary>
        TowardTarget,
    }

    /// <summary>位移受阻的处理（手感设计/02 第 4 节 <c>ActionMotion.blocking</c>；同 ADR-0026 的裁决语义）。</summary>
    public enum ActionMotionBlocking
    {
        /// <summary>受阻即停在阻挡前。</summary>
        Stop,

        /// <summary>受阻时沿墙滑动（去掉法向分量）。</summary>
        Slide,
    }

    /// <summary>
    /// 时间线技能的 <c>motion</c> 块声明（手感设计/02 第 4 节 <c>ActionMotion</c>；ADR-0116 决策 6）。
    /// 距离单位是身高倍数（<c>body_heights</c>），标定后才是世界单位——运行时位移用
    /// <see cref="ActionMotionState.DistanceWorld"/>，不直接用本声明的 <see cref="Distance"/>。
    /// </summary>
    public readonly struct ActionMotion
    {
        /// <summary>
        /// 旧数据里 <c>driver: root_motion</c> 的迁移说明（ADR-0147）：加载期校验、解析与运动层共用同一段文字。
        /// </summary>
        public const string RootMotionMigrationNote =
            "root_motion 由表现帧累加剪辑根位移，模拟结果依赖表现帧率与引擎动画求值，无法确定性重放，已删除。迁移：用导入工具把剪辑根位移曲线烘焙为 " +
            "skill.motion_curve 行（import_assets bake-motion），把 motion.curve 改为 custom:<curve_id>、motion.distance 改为烘焙报告给出的位移（身高倍数），" +
            "并删除 motion.driver 字段";

        public ActionMotionDriver Driver { get; }

        public ActionMotionKind Kind { get; }

        /// <summary>声明距离（身高倍数）；<see cref="ActionMotionKind.Charge"/> 为最大距离。</summary>
        public double Distance { get; }

        /// <summary>位移随 <c>motion_start..motion_end</c> 进度的曲线引用：<c>linear</c>、内建 <c>ease_in</c>/
        /// <c>ease_out</c>/<c>ease_in_out</c>，或 <c>custom:&lt;curve_id&gt;</c>（曲线形态登记，见 04 第 3.6 节）。</summary>
        public string Curve { get; }

        public ActionMotionDirection Direction { get; }

        /// <summary>位移方向相对当前朝向允许的最大偏转（度）。</summary>
        public double MaxTurnDeg { get; }

        public ActionMotionBlocking Blocking { get; }

        public ActionMotion(
            ActionMotionDriver driver, ActionMotionKind kind, double distance, string curve,
            ActionMotionDirection direction, double maxTurnDeg, ActionMotionBlocking blocking)
        {
            Driver = driver;
            Kind = kind;
            Distance = distance;
            Curve = curve ?? "linear";
            Direction = direction;
            MaxTurnDeg = maxTurnDeg;
            Blocking = blocking;
        }
    }

    /// <summary>
    /// 进行中动作的位移段只读快照（<see cref="ActionState.Motion"/>）：由动作时间线在动作被接受时解析并随动作状态
    /// 提供（方向、标定后的距离、目标辅助缩放都已落定），运动仲裁器只消费它，不再回头读数据表。
    /// <para>
    /// 判断记录：位移窗口用动作时钟 tick 表示（<see cref="StartTick"/> 含、<see cref="EndTick"/> 不含，与
    /// <see cref="ActionState.ElapsedTicks"/> 同一时钟）；<c>ElapsedTicks = e</c> 的那个 tick 覆盖进度区间
    /// <c>[(e−Start)/len, (e+1−Start)/len]</c>，逐 tick 位移 = <c>DistanceWorld × (f(p1) − f(p0))</c>，窗口内各 tick 之和恰为
    /// <c>DistanceWorld × (f(1) − f(0))</c>。顿帧期间动作时钟不前进，运动侧处于 frozen 叠加态不位移，所以不会漏算或重复。
    /// </para>
    /// </summary>
    public readonly struct ActionMotionState
    {
        public ActionMotion Declaration { get; }

        /// <summary>标定后的位移距离（世界单位）；已含目标辅助的 <c>close_distance</c> 缩放。<c>charge</c> 为最大距离。</summary>
        public double DistanceWorld { get; }

        /// <summary>位移窗口起点（动作时钟 tick，含）：<c>motion_start</c> 标记。</summary>
        public int StartTick { get; }

        /// <summary>位移窗口终点（动作时钟 tick，不含）：<c>motion_end</c> 标记。</summary>
        public int EndTick { get; }

        /// <summary>已解析的位移方向（单位向量；<c>facing</c>/<c>input_snapshot</c> 在动作被接受时落定）。
        /// <c>toward_target</c> 与 <c>charge</c> 逐 tick 朝 <see cref="TargetId"/> 重算，本字段作为目标丢失时的兜底方向。</summary>
        public Vec2 Direction { get; }

        /// <summary>冲向/朝向的目标单位（<c>toward_target</c> 与 <c>charge</c>）；无则 null。</summary>
        public Id? TargetId { get; }

        /// <summary><c>charge</c> 到达目标身前的停止距离（世界单位，来自动作快照里标定后的 <c>stop_distance</c>）。</summary>
        public double StopDistanceWorld { get; }

        public ActionMotionState(
            ActionMotion declaration, double distanceWorld, int startTick, int endTick, Vec2 direction,
            Id? targetId = null, double stopDistanceWorld = 0.0)
        {
            Declaration = declaration;
            DistanceWorld = distanceWorld;
            StartTick = startTick;
            EndTick = endTick;
            Direction = direction;
            TargetId = targetId;
            StopDistanceWorld = stopDistanceWorld;
        }

        /// <summary>给定动作时钟 tick 是否落在位移窗口内。</summary>
        public bool IsActiveAt(int elapsedTicks) => elapsedTicks >= StartTick && elapsedTicks < EndTick;
    }
}
