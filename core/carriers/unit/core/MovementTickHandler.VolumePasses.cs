using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 单位间体积阻挡的"求解遍"（M4-W2）：<see cref="MovementTickHandler.Execute"/> 在本 tick 有声明了体积的单位时，把整个 tick 的移动处理当成一个
    /// 可重放的事务——先取下所有单位的位置/朝向/移动状态与运动层的簿记，按"分类"跑一遍（<see cref="ExecutePass"/>），必要时恢复到 tick 开头、换一个分类
    /// 再跑，直到分类稳定；期间产生的事件与对外回调全部缓存在发件箱里，最后一遍的结果才按原有顺序发出，所以外部看到的和"只跑了一遍"没有区别。
    /// <para>
    /// <b>分类</b>（<see cref="VolumeBody.Free"/>）：阶段 A 里"会动"的单位不是静止障碍（别人不被它 tick 起点的位置拦住，接触留给阶段 B 按最终位置求解），
    /// "不会动"的单位是精确的静止障碍（撞停、滑开、绕行）。M4-B 用 tick 起点的预判（<see cref="PredictMayMove"/>）分类，预判错了结果退回旧语义。
    /// 本文件把分类改成<b>由求解结果决定、与预判无关</b>：
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>规范起点 = 尝试位移的单位</b>（<see cref="VolumeBody.Attempted"/>）：单位在体积裁决之前是否提议了非零位移，只由它自己的状态、意图与地形决定，
    /// 与别的单位怎么分类无关，所以任何一遍都能读出同一个集合 P。第一遍用预判（或测试给的覆盖）起步；读出 P 之后，若起步分类不等于 P 就用 P 重跑。
    /// 因此起点集合是 P 与预判无关；预判只决定"第一遍能不能直接当规范遍用"（预判对了省一遍）。
    /// </description></item>
    /// <item><description>
    /// <b>收缩到不动点</b>：规范遍之后看实际位移（成对接触求解之后、重叠分离之前，<see cref="VolumeBody.Final"/> 与 <see cref="VolumeBody.Start"/> 不同）：
    /// 被当作"会动"、实际却被挡死没有动的单位（顶着别人的体积、被卡住）从分类里去掉，再重跑；去掉只减不增，所以必然终止
    /// （<see cref="MaxVolumePasses"/> 为上限）。收敛后："会动"集合里的每个单位实际都动了，其余按静止障碍（旧语义精确滑开/绕行，阶段 B 仍兜底不重叠）。
    /// 互相顶着、既不能都当"会动"也不能都当"静止"自洽的配置（例如两个单位斜着互相顶），按静止处理——它们各自沿对方起点位置的体积滑开，结果确定。
    /// </description></item>
    /// <item><description>
    /// <b>分类不起作用就不重跑</b>：这一遍里没有任何一次扫掠/守卫在几何上碰到别的单位的体积（<see cref="_volSensitive"/> 为假），
    /// 则任何分类下结果都相同，直接定稿——人群里相互不挨着的单位只跑一遍。
    /// </description></item>
    /// </list>
    /// 以上只依赖"每一遍是分类的确定函数"：每遍从同一份快照恢复，重放同样的意图，不读系统时间与枚举顺序。测试用 <see cref="VolumeStartClassifier"/>
    /// 把起步分类覆盖成"全部会动 / 全部静止 / 随机"，位置、状态与事件流必须逐位一致（<c>MotionArbiterTests.UnitVolumePhase3</c>）。
    /// </summary>
    public sealed partial class MovementTickHandler
    {
        /// <summary>
        /// 诊断/测试钩子：覆盖第一遍求解的起步分类（返回 true = 当作"会动"）。缺省 null = 用 tick 起点预判。起步分类只影响求解遍数，不影响结果
        /// （见类型注释）；测试用它证明"预判与否不影响结果"。
        /// </summary>
        public Func<Id, bool>? VolumeStartClassifier { get; set; }

        /// <summary>上一次 <see cref="Execute"/> 的求解遍数：本 tick 没有有体积的单位（含离散步、未启用运动层）为 0，只跑了一遍为 1。</summary>
        public int LastVolumePassCount { get; private set; }

        /// <summary>求解遍数上限（收缩集合只减不增，实际两三遍内收敛；到上限时取最后一遍的结果，仍然确定）。</summary>
        private const int MaxVolumePasses = 12;

        // 本遍明确指定的"会动"集合；null = 用起步分类（预判或测试覆盖）。
        private HashSet<Id>? _freeForPass;

        /// <summary>一遍求解结束时读出的结果：分类是否起作用、本遍用的"会动"集合、尝试位移的集合、成对求解后实际动了的集合。</summary>
        private sealed class PassOutcome
        {
            public bool Sensitive;
            public readonly HashSet<Id> FreeUsed = new HashSet<Id>();
            public readonly HashSet<Id> Attempted = new HashSet<Id>();
            public readonly HashSet<Id> Movers = new HashSet<Id>();

            public void Reset()
            {
                Sensitive = false;
                FreeUsed.Clear();
                Attempted.Clear();
                Movers.Clear();
            }
        }

        private readonly PassOutcome _passOutcome = new PassOutcome();

        // ================================================================== 入口

        public void Execute(SimStep step, IWorldSim world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            LastVolumePassCount = 0;
            if (!ShouldSolveVolumePasses(step))
            {
                ExecutePass(step, world);
                return;
            }

            ExecuteWithVolumePasses(step, world);
        }

        /// <summary>连续步、运动层启用且有存活的有体积单位时，才进入求解遍；其余情况（含所有既有预设）走一遍旧流程，一字未动。</summary>
        private bool ShouldSolveVolumePasses(SimStep step)
        {
            if (step.Kind != SimStepKind.Continuous || !(step.Dt > 0.0))
            {
                return false;
            }

            var mot = _movementHost.Motion;
            if (mot == null || mot.Feel == null)
            {
                return false;
            }

            _mot = mot;
            _feel = mot.Feel;
            var all = _units.AllUnits;
            for (var i = 0; i < all.Count; i++)
            {
                var id = all[i];
                if (ProfileOf(id).UnitBodyRadius > 0.0 && _units.IsAlive(id))
                {
                    return true;
                }
            }

            return false;
        }

        private void ExecuteWithVolumePasses(SimStep step, IWorldSim world)
        {
            CaptureTickState(world);
            _buffering = true;
            _outbox.Clear();
            _rootMotionCache.Clear();
            var passes = 0;
            try
            {
                HashSet<Id>? free = null;
                var canonical = false;
                while (true)
                {
                    if (passes > 0)
                    {
                        RestoreTickState();
                        _outbox.Clear();
                    }

                    _freeForPass = free;
                    ExecutePass(step, world);
                    passes++;
                    var outcome = _passOutcome;
                    if (!outcome.Sensitive || passes >= MaxVolumePasses)
                    {
                        break;
                    }

                    if (!canonical)
                    {
                        canonical = true;
                        if (!outcome.FreeUsed.SetEquals(outcome.Attempted))
                        {
                            free = new HashSet<Id>(outcome.Attempted);
                            continue;
                        }

                        free = new HashSet<Id>(outcome.FreeUsed);
                    }

                    // 收缩：当作"会动"却没有实际位移的单位从分类里去掉。
                    var next = new HashSet<Id>(free!);
                    next.IntersectWith(outcome.Movers);
                    if (next.Count == free!.Count)
                    {
                        break;
                    }

                    free = next;
                }
            }
            finally
            {
                _freeForPass = null;
                _buffering = false;
                LastVolumePassCount = passes;
                FlushOutbox();
            }
        }

        // ================================================================== 发件箱

        // 求解遍期间对外的事件与回调都先进发件箱，最后一遍的才按原有顺序发出；单项可以被置空（到达被拉回时撤销到达事件）。
        private bool _buffering;
        private readonly List<Action?> _outbox = new List<Action?>();

        private void FlushOutbox()
        {
            var count = _outbox.Count;
            for (var i = 0; i < count; i++)
            {
                var action = _outbox[i];
                _outbox[i] = null;
                action?.Invoke();
            }

            _outbox.Clear();
        }

        private void EmitBus(IEvent evt)
        {
            if (_buffering)
            {
                _outbox.Add(() => _bus.Enqueue(evt));
                return;
            }

            _bus.Enqueue(evt);
        }

        private void EmitMoveStopped(Id unitId, Vec2 position, MoveStopReason reason)
        {
            if (_buffering)
            {
                _outbox.Add(() => _movementHost.RaiseMoveStopped(unitId, position, reason));
                return;
            }

            _movementHost.RaiseMoveStopped(unitId, position, reason);
        }

        private void EmitMoveFailed(Id unitId, Vec2 from, Vec2 to)
        {
            if (_buffering)
            {
                _outbox.Add(() => _movementHost.RaiseMoveFailed(unitId, from, to));
                return;
            }

            _movementHost.RaiseMoveFailed(unitId, from, to);
        }

        private void EmitMoveFailedDetailed(Id unitId, Vec2 from, Vec2 to, MoveFailReason reason)
        {
            if (_buffering)
            {
                _outbox.Add(() => _movementHost.RaiseMoveFailedDetailed(unitId, from, to, reason));
                return;
            }

            _movementHost.RaiseMoveFailedDetailed(unitId, from, to, reason);
        }

        private void EmitMoveTargetAdjusted(Id unitId, Vec2 requested, Vec2 resolved)
        {
            if (_buffering)
            {
                _outbox.Add(() => _movementHost.RaiseMoveTargetAdjusted(unitId, requested, resolved));
                return;
            }

            _movementHost.RaiseMoveTargetAdjusted(unitId, requested, resolved);
        }

        /// <summary>当前发件箱里下一项的位置（没有缓存时为 -1）；记下它就能在之后撤销紧随其后发出的那个事件。</summary>
        private int OutboxMark() => _buffering ? _outbox.Count : -1;

        /// <summary>撤销发件箱里 <paramref name="index"/> 处的事件（已发出或没有缓存时无效）。</summary>
        private void OutboxCancel(int index)
        {
            if (_buffering && index >= 0 && index < _outbox.Count)
            {
                _outbox[index] = null;
            }
        }

        // 根运动是"消费型"读取：重放时同一 tick 的第二遍读不到了，所以第一次读到的值缓存下来供各遍共用。
        private readonly Dictionary<Id, Vec2> _rootMotionCache = new Dictionary<Id, Vec2>();

        private Vec2 ConsumeRootMotion(IRootMotionSource source, Id unitId)
        {
            if (!_buffering)
            {
                return source.ConsumeRootMotionDelta(unitId);
            }

            if (!_rootMotionCache.TryGetValue(unitId, out var delta))
            {
                delta = source.ConsumeRootMotionDelta(unitId);
                _rootMotionCache[unitId] = delta;
            }

            return delta;
        }

        // ================================================================== tick 状态的取下与恢复

        private Unit[] _capUnits = Array.Empty<Unit>();
        private Vec2[] _capPosition = Array.Empty<Vec2>();
        private double[] _capFacing = Array.Empty<double>();
        private MovementState[] _capState = Array.Empty<MovementState>();
        private int _capCount;
        private readonly Dictionary<Id, (Id Cast, double Traveled)> _capCharge = new Dictionary<Id, (Id, double)>();
        private readonly HashSet<Id> _capMotionLive = new HashSet<Id>();
        private readonly Dictionary<Id, SuspendedPath> _capSuspended = new Dictionary<Id, SuspendedPath>();
        private readonly List<Id> _capResume = new List<Id>();

        /// <summary>取下 tick 开头的状态：每个单位的位置、朝向、移动状态，以及本处理器会改的运动层簿记（冲锋已走距离、动作位移窗口标志、挂起路径）。</summary>
        private void CaptureTickState(IWorldSim world)
        {
            var all = _units.AllUnits;
            var n = all.Count;
            if (_capUnits.Length < n)
            {
                var size = Math.Max(n, _capUnits.Length * 2);
                _capUnits = new Unit[size];
                _capPosition = new Vec2[size];
                _capFacing = new double[size];
                _capState = new MovementState[size];
            }

            _capCount = 0;
            for (var i = 0; i < n; i++)
            {
                if (!(world.GetEntity(all[i]) is Unit unit))
                {
                    continue;
                }

                _capUnits[_capCount] = unit;
                _capPosition[_capCount] = unit.Position;
                _capFacing[_capCount] = unit.Facing;
                _capState[_capCount] = unit.MovementState;
                _capCount++;
            }

            _capCharge.Clear();
            foreach (var kv in _chargeTraveled) _capCharge[kv.Key] = kv.Value;
            _capMotionLive.Clear();
            foreach (var id in _actionMotionLive) _capMotionLive.Add(id);
            _capSuspended.Clear();
            foreach (var kv in _suspendedPaths) _capSuspended[kv.Key] = kv.Value;
            _capResume.Clear();
            _capResume.AddRange(_pendingResume);
        }

        /// <summary>恢复到 <see cref="CaptureTickState"/> 取下时的状态，准备按另一个分类重放本 tick。</summary>
        private void RestoreTickState()
        {
            for (var i = 0; i < _capCount; i++)
            {
                var unit = _capUnits[i];
                unit.MovementState = _capState[i];
                unit.Facing = _capFacing[i];
                if (!unit.Position.Equals(_capPosition[i]))
                {
                    _units.SetPosition(unit.EntityId, _capPosition[i]);
                }
            }

            _chargeTraveled.Clear();
            foreach (var kv in _capCharge) _chargeTraveled[kv.Key] = kv.Value;
            _actionMotionLive.Clear();
            foreach (var id in _capMotionLive) _actionMotionLive.Add(id);
            _suspendedPaths.Clear();
            foreach (var kv in _capSuspended) _suspendedPaths[kv.Key] = kv.Value;
            _pendingResume.Clear();
            _pendingResume.AddRange(_capResume);
            _movedDeferredSet.Clear();
            _deferredKin.Clear();
            _deferredStops.Clear();
            _pendingPushes.Clear();
        }

        // ================================================================== 一遍求解结束时读出结果

        /// <summary>
        /// 阶段 B 的成对接触求解之后（重叠分离之前）读出本遍的结果：分类是否起作用、本遍用的"会动"集合、尝试位移的集合、实际动了的集合。
        /// "实际动了" = 成对求解之后的最终位置与 tick 起点位置不同（超过零长度阈值）。
        /// </summary>
        private void CapturePassOutcome()
        {
            var o = _passOutcome;
            o.Reset();
            o.Sensitive = _volSensitive;
            for (var i = 0; i < _volumes.Count; i++)
            {
                var b = _volumes[i];
                if (b.Free) o.FreeUsed.Add(b.Id);
                if (b.Attempted) o.Attempted.Add(b.Id);
                if (b.Active && (b.Final - b.Start).Length > ZeroLengthEpsilon) o.Movers.Add(b.Id);
            }
        }
    }
}
