using Core.Foundation.Common;
using Core.Foundation.InputMap;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 玩家移动意图解析器（手感设计/02 第 3.3 节"冲刺"、01 第 2 节，ADR-0153）：把"移动轴向量 + 可选的冲刺输入动作按住状态"
    /// 翻译成一条 <see cref="MoveRequest"/>（方向 + <see cref="MoveMode"/>）。宿主（生产引导、常驻宿主、实验室手玩）每个固定步用它生成玩家的移动请求，
    /// 不再各自手写 <c>MoveRequest.InDirection(player, axis)</c>。
    /// <para>
    /// 模式规则：没有声明冲刺动作（或冲刺键没按住）时与此前逐位一致——<see cref="MoveMode.Walk"/>（或调用方给的基础模式）；冲刺键按住且移动幅度非零
    /// （轴向量平方长 &gt; <see cref="MinSqrLength"/>）才升到 <see cref="MoveMode.Sprint"/>。原地按住冲刺键不产生请求（没有移动就没有模式）。
    /// 基础模式只有 Walk/Run 会被冲刺覆盖；Idle/Forced 等不是自主移动的模式原样保留。
    /// </para>
    /// <para>
    /// 判断记录（冲刺动作怎么声明）：冲刺是一个数据声明的输入动作——按钮型、类别 <c>move</c>（<c>found.input_action</c> 行，
    /// 缺省 id <see cref="DefaultSprintAction"/>）。<c>move</c> 类按钮不入缓冲，输入缓冲只追踪它的按住状态
    /// （<see cref="ActionDefinition.IsHeldTracked"/>，按下/抬起即时生效），本类型经 <see cref="IInputBufferQuery.IsHeld"/> 读取。
    /// 以输入缓冲宿主构造时（<see cref="PlayerMoveIntentResolver(InputBufferHost?)"/>）按"数据里有没有声明这个动作"逐次判定：没声明就没有冲刺，
    /// 数据热加载声明后即时生效；显式构造（<see cref="PlayerMoveIntentResolver(IInputBufferQuery?, Id?)"/>）则固定用给定的动作 id。
    /// </para>
    /// <para>
    /// 判断记录（只读按住、无延迟）：按住状态在采样（按下/抬起边沿到达）时即时更新，与移动轴的采样同一口径，所以同一固定步里"先采样输入、
    /// 再解析移动请求"读到的就是本步的按键状态，没有"晚一 tick"的滞后。
    /// </para>
    /// </summary>
    public sealed class PlayerMoveIntentResolver
    {
        /// <summary>移动轴"有输入"的平方长下限（与此前各宿主手写的 <c>0.0001</c> 阈值一致，也是输入缓冲方向快照的阈值）。</summary>
        public const double MinSqrLength = 0.0001;

        /// <summary>缺省的冲刺输入动作 id（按钮型、类别 <c>move</c>）。</summary>
        public static readonly Id DefaultSprintAction = new Id("input.action.sprint");

        private readonly IInputBufferQuery? _held;
        private readonly InputBufferHost? _buffer;
        private readonly Id? _explicitSprintAction;

        /// <summary>显式构造：固定使用 <paramref name="sprintAction"/> 作冲刺键；<paramref name="held"/> 或 <paramref name="sprintAction"/> 为空即没有冲刺（与此前逐位一致）。</summary>
        public PlayerMoveIntentResolver(IInputBufferQuery? held, Id? sprintAction)
        {
            _held = held;
            _explicitSprintAction = sprintAction;
        }

        /// <summary>按输入缓冲宿主构造：数据里把 <see cref="DefaultSprintAction"/> 声明成按钮型 <c>move</c> 类动作时才有冲刺，没声明即没有（与此前逐位一致）；
        /// <paramref name="buffer"/> 为空（没有手感装配）同样没有冲刺。</summary>
        public PlayerMoveIntentResolver(InputBufferHost? buffer)
        {
            _buffer = buffer;
            _held = buffer;
        }

        /// <summary>当前生效的冲刺动作 id；没有冲刺时为 null。</summary>
        public Id? SprintAction
        {
            get
            {
                if (_explicitSprintAction.HasValue) return _held != null ? _explicitSprintAction : null;
                return _buffer != null && _buffer.IsHeldTracked(DefaultSprintAction) ? DefaultSprintAction : (Id?)null;
            }
        }

        /// <summary>该单位此刻应使用的移动模式（不判断是否有移动输入）。</summary>
        public MoveMode ModeFor(Id unitId, MoveMode baseMode = MoveMode.Walk)
        {
            if (baseMode != MoveMode.Walk && baseMode != MoveMode.Run) return baseMode;
            var sprint = SprintAction;
            return sprint.HasValue && _held != null && _held.IsHeld(unitId, sprint.Value) ? MoveMode.Sprint : baseMode;
        }

        /// <summary>
        /// 解析本步的玩家移动请求：轴向量平方长 &gt; <see cref="MinSqrLength"/> 时返回 true 并给出方向请求（模式见类型注释），否则返回 false（宿主此时不下请求，同此前）。
        /// <paramref name="baseMode"/> 缺省 Walk（与此前宿主一致）；传 Run 即"默认跑步、按冲刺键升冲刺"。
        /// </summary>
        public bool TryResolve(Id unitId, Vec2 axis, out MoveRequest request, MoveMode baseMode = MoveMode.Walk)
        {
            if (!(axis.SqrLength > MinSqrLength))
            {
                request = default;
                return false;
            }

            request = MoveRequest.InDirection(unitId, axis, ModeFor(unitId, baseMode));
            return true;
        }
    }
}
