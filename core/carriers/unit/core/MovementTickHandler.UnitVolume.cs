using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 单位间体积阻挡（手感设计/02 第 3.5 节；ADR-0128；运动档案字段 <c>unit_body_radius</c> 等）：<see cref="MovementTickHandler"/>
    /// 运动层的一部分。本文件放"逐单位的扫掠裁决"（阶段 A：对别的单位 tick 起点位置的快照做连续扫掠、绕行），
    /// <c>MovementTickHandler.UnitVolumeResolve.cs</c> 放"tick 末的成对裁决"（阶段 B：两个都在动的单位互相撞停、重叠分离、受控位移推人）。
    /// <para>
    /// <b>开关与逐位等价</b>：只有运动层启用（见 <see cref="MovementTickHandler.Motion"/> 文件头）且<b>本单位</b>的档案声明了
    /// <c>unit_body_radius &gt; 0</c> 时才进入本文件的任何分支；没有声明体积半径的单位（含所有既有预设）既不触发快照、也不被延迟写回，
    /// 既有算式一字未动。体积是<b>成对</b>语义：两个都声明了半径的单位之间，中心距不得小于半径之和；一方没有体积则双方互不影响。
    /// </para>
    /// <para>
    /// <b>顺序无关</b>：每个有体积的单位在本 tick 内读到的"别的单位的位置"一律是 tick 起点的快照（<see cref="EnsureVolumeSnapshot"/>：
    /// 第一个有体积的单位被处理前一次性取下，按单位 id 排序），而不是"先走的单位已经写回的新位置"，所以阶段 A 的结果只依赖快照与单位自己的状态，
    /// 不依赖单位处理顺序；快照里的单位位置在 tick 末由阶段 B 统一裁决（对称地处理两个单位同时互相靠近）后才一次性写回、发出 <c>unit.moved</c>。
    /// </para>
    /// <para>
    /// <b>扫掠算法</b>：把"本 tick 从 <c>from</c> 走到 <c>to</c>"当成线段（折线时逐段），对每个有体积的其他存活同图单位（以其快照位置为圆心、
    /// 半径之和为半径）求线段首次进入圆的位置（连续扫掠，高速位移也不会隧穿）。命中后停在入射点之前一小段
    /// （<see cref="MovementOptions.ArrivalEpsilon"/>，同墙体阻挡的回退约定，保证下一 tick 不会从圆里起步）；允许滑动的来源
    /// （<c>wall_slide</c> 为真的输入位移、<c>blocking: slide</c> 的动作位移）把剩余位移去掉沿圆心连线方向的分量后再走一段（最多一次，
    /// 切向位移仍经地形裁决与第二次扫掠，不递归），否则整体停下。起点已在别人体积内（出生重叠、穿过式闪避的落点）时只拦"让距离变近"的位移，
    /// 允许走开；真正把重叠推开的是阶段 B 的分离。末尾有一道守卫：任何结果若落进体积且比起点更深，一律退回起点。
    /// </para>
    /// </summary>
    public sealed partial class MovementTickHandler
    {
        /// <summary>本 tick 快照里一个有体积的单位：id、半径、tick 起点位置、所在地图与分离速率倍数；其余字段是阶段 B 的草稿区。</summary>
        private sealed class VolumeBody
        {
            public Id Id;
            public double Radius;
            public Vec2 Start;
            public Id? Map;
            public double SeparationRatio;

            /// <summary>
            /// 本 tick 开头预判"这个单位这 tick 可能会动"（见 <see cref="PredictMayMove"/>）。会动的单位在阶段 A 里不当作障碍（别的单位不被它
            /// tick 起点的位置拦住），它与别人的接触留给阶段 B 按最终位置求解；不会动的单位在阶段 A 里是精确的静止障碍（撞停、滑开、绕行）。
            /// </summary>
            public bool Free;

            /// <summary>按 id 排序后的下标（成对计算与累加的规范顺序）。</summary>
            public int Index;

            /// <summary>阶段 A 里这个单位实际走过的折线点（含 tick 起点），逐条边追加；没有位移为 null。</summary>
            public List<Vec2>? Trail;

            /// <summary>与 <see cref="Trail"/> 每条边一一对应：该边走完时的路径下标（路径跟随与追击），其余来源为 -1。</summary>
            public List<int>? TrailIndex;

            /// <summary>折线不连续（位置写入没有接在上一条边之后）：成对裁决退回用起终点的弦。</summary>
            public bool TrailBroken;

            /// <summary>阶段 B 把位移缩短后，路径下标应回到的值（-1 = 不知道，沿用 tick 开始时的下标）。</summary>
            public int RestoreIndex = -1;

            /// <summary>
            /// 本 tick 内这个单位的"提议位移"在体积裁决之前是非零的（经过 <see cref="ClipPathByUnitVolumes"/> 的非零线段）。只由单位自己的状态、
            /// 意图与地形决定，与别的单位被分到"会动"还是"不会动"无关——这是第二遍求解的规范起点（见 VolumePasses 文件头）。
            /// </summary>
            public bool Attempted;

            /// <summary>轨迹里路径下标所指的那条路径（路径跟随/追击）与本 tick 开始推进时的下标；没有路径轨迹为 null。</summary>
            public IReadOnlyList<Vec2>? TrailPath;

            public int TrailStartIndex;

            /// <summary>路径跟随本 tick 走到终点（状态已写成到达/Idle）时，被拉回后恢复用的"未到达"状态模板（下标待填）与到达事件在发件箱里的位置。</summary>
            public MovementState? UnarriveTemplate;

            public int ArrivalOutboxIndex = -1;

            /// <summary>阶段 A 记下的本 tick 速度剖面（加减速/曲线）；null = 匀速。</summary>
            public SpeedProfile? Profile;

            /// <summary>速度剖面已经登记过（一个 tick 里只有第一个位移来源的剖面有效）；第二个来源也要写位移时标记混合，成对裁决退回匀速。</summary>
            public bool ProfileNoted;

            public bool ProfileMixed;

            /// <summary>被成对裁决拉回时，决定最终比例的那次接触的法线（从对方指向本单位）；用于保留切向速度。</summary>
            public Vec2 PullNormal;

            public bool HasPullNormal;

            // ---- 阶段 B 草稿区 ----
            public Unit? Unit;
            public bool Active;
            public bool Ghost;

            /// <summary>穿过式位移的最后一个 tick：落点必须在别的单位体积之外（见 UnitVolumeGhost）。</summary>
            public bool GhostLanding;
            public Vec2 Delta;
            public double Scale;
            public bool PulledBack;
            public Vec2 Final;
        }

        private struct VolumeClip
        {
            /// <summary>裁决后的终点（未阻挡时等于传入的 <c>to</c>）。</summary>
            public Vec2 End;

            public bool Blocked;

            /// <summary>阻挡后改为沿体积切向滑开（<see cref="Normal"/> 有效）。</summary>
            public bool Slid;

            /// <summary>第一个被撞体积在入射点的外法线（从被撞单位中心指向入射点）。</summary>
            public Vec2 Normal;

            /// <summary>滑动那一段又撞上第二个体积（<see cref="SecondNormal"/> 有效）。</summary>
            public bool SecondBlocked;

            public Vec2 SecondNormal;

            /// <summary>第一个被撞单位（<see cref="Blocked"/> 为真时有效）。</summary>
            public Id HitId;

            /// <summary>阻挡后实际走的折线（含起点与终点，<see cref="Blocked"/> 为真时有效）：最多 起点、拐点、入射点前、滑开终点 四个点。</summary>
            public Vec2[]? Poly;
        }

        // 本 tick 内有体积的单位快照（id、半径、tick 起点位置，按 id 排序）。每 tick 在第一个有体积的单位被处理前懒建一次。
        private readonly List<VolumeBody> _volumes = new List<VolumeBody>();
        private readonly Dictionary<Id, VolumeBody> _volumeById = new Dictionary<Id, VolumeBody>();
        private long _volumesStamp = -1;

        /// <summary>本 tick 位置写回被延迟到阶段 B 的单位（它们的 <c>unit.moved</c> 也延迟到阶段 B 之后、按 id 顺序发出）。</summary>
        private readonly HashSet<Id> _movedDeferredSet = new HashSet<Id>();

        /// <summary>
        /// 本 tick 开头所有单位（含没有体积的）的位置快照。有体积的单位读"别的单位的位置"（追击目标、扑向目标）一律读这里，
        /// 而不是别人此刻已经走到的位置，所以结果与单位处理顺序无关。
        /// </summary>
        private readonly Dictionary<Id, Vec2> _unitStart = new Dictionary<Id, Vec2>();

        // 本 tick 的世界与意图（Execute 开头登记，供 tick 开头预判"谁会动"读取；不进存档，每 tick 覆盖）。
        private IWorldSim? _tickWorld;
        private IReadOnlyList<Intent>? _tickIntents;
        private Dictionary<Id, int>? _tickLastStop;

        private readonly HashSet<Id> _intentMovers = new HashSet<Id>();
        private readonly HashSet<Id> _intentDisplacers = new HashSet<Id>();

        /// <summary>本 tick（最后一遍求解）里这个单位被当作"会动"（不是阶段 A 的静止障碍）与否，供测试与实验室核对（只读）。</summary>
        internal bool WasPredictedToMove(Id id) => VolumeSnapshotCurrent && _volumeById.TryGetValue(id, out var b) && b.Free;

        // 本 tick 内最大的体积半径（宽相格子与扫掠膨胀量用）；EnsureVolumeSnapshot 里重算。
        private double _maxVolumeRadius;

        // 本遍求解里"分类起了作用"：某次扫掠/守卫的几何上碰到了一个单位的体积，而这个单位是否当作静止障碍决定了结果（见 VolumePasses 文件头）。
        private bool _volSensitive;

        /// <summary>
        /// 阶段 B 的"同 tick 推人"微阶段（见 ApplyPendingPushes）：被推单位用真实的受控位移推进一个 tick，扫掠读的是别的单位已裁决完的最终位置
        /// （<see cref="VolumeBody.Final"/>）而不是 tick 起点快照，宽相网格（按起点建的）不用，本阶段的接触不计入分类敏感性。
        /// </summary>
        private bool _microPhase;

        private Vec2 BodyAt(VolumeBody b) => _microPhase ? b.Final : b.Start;

        // 阶段 A 扫掠用的候选下标缓冲（扫掠与守卫不嵌套，各一个）。
        private int[] _candSweep = new int[16];
        private int[] _candGuard = new int[16];

        /// <summary>未启用运动层、离散步或本单位没有声明体积时返回 0。</summary>
        private double SelfVolumeRadius(Unit unit)
        {
            var t = GetMotionTick(unit);
            return t == null ? 0.0 : t.Profile.UnitBodyRadius;
        }

        /// <summary>任一单位的运动档案（与 <see cref="GetMotionTick"/> 共用档案缓存）。仅在运动层启用时调用。</summary>
        private MotionProfile ProfileOf(Id id)
        {
            var view = _feel!.ResolveJudging(id);
            if (!_motionProfiles.TryGetValue(id, out var profile) || profile.Version != view.Version)
            {
                profile = MotionProfile.Read(view);
                _motionProfiles[id] = profile;
            }

            return profile;
        }

        /// <summary>任一单位的体积半径。仅在运动层启用时调用。</summary>
        private double VolumeRadiusOf(Id id) => ProfileOf(id).UnitBodyRadius;

        private bool VolumeSnapshotCurrent => _volumesStamp == _motionStamp;

        private bool TryGetBody(Id id, out VolumeBody body)
        {
            if (VolumeSnapshotCurrent && _volumeById.TryGetValue(id, out body!))
            {
                return true;
            }

            body = null!;
            return false;
        }

        private void EnsureVolumeSnapshot()
        {
            if (_volumesStamp == _motionStamp)
            {
                return;
            }

            _volumesStamp = _motionStamp;
            _volumes.Clear();
            _volumeById.Clear();
            _movedDeferredSet.Clear();
            _deferredKin.Clear();
            _deferredStops.Clear();
            _pendingPushes.Clear();
            _unitStart.Clear();
            var all = _units.AllUnits;
            for (var i = 0; i < all.Count; i++)
            {
                var id = all[i];
                _unitStart[id] = _units.GetPosition(id);
                var profile = ProfileOf(id);
                if (!(profile.UnitBodyRadius > 0.0) || !_units.IsAlive(id))
                {
                    continue;
                }

                _volumes.Add(new VolumeBody
                {
                    Id = id,
                    Radius = profile.UnitBodyRadius,
                    Start = _units.GetPosition(id),
                    Map = _units.GetMapId(id),
                    SeparationRatio = profile.UnitSeparationSpeedRatio,
                });
            }

            _volumes.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (var i = 0; i < _volumes.Count; i++)
            {
                _volumes[i].Index = i;
                _volumeById[_volumes[i].Id] = _volumes[i];
            }

            _volSensitive = false;
            _maxVolumeRadius = 0.0;
            for (var i = 0; i < _volumes.Count; i++)
            {
                if (_volumes[i].Radius > _maxVolumeRadius) _maxVolumeRadius = _volumes[i].Radius;
            }

            BuildVolumeGrid();

            // 起点分类：求解遍给定了明确的"会动"集合就用它（见 VolumePasses），否则取起点预判（只读 tick 开头的状态与本 tick 的意图，
            // 与处理顺序无关；测试可用 VolumeStartClassifier 覆盖）。分类只是起点，最终结果由求解遍收敛决定，与起点无关。
            IndexTickIntents();
            for (var i = 0; i < _volumes.Count; i++)
            {
                var body = _volumes[i];
                if (_freeForPass != null)
                {
                    body.Free = _freeForPass.Contains(body.Id);
                }
                else if (VolumeStartClassifier != null)
                {
                    body.Free = VolumeStartClassifier(body.Id);
                }
                else
                {
                    body.Free = PredictMayMove(body.Id, ProfileOf(body.Id));
                }
            }
        }

        // ================================================================== 宽相（均匀网格）

        /// <summary>
        /// 诊断/测试钩子：为真时阶段 A 的扫掠与阶段 B 的近邻对收集都退回逐个线性扫描（暴力）。网格宽相只缩小候选集，不改任何算式，
        /// 所以两种模式的结果逐位一致（测试以暴力为基准核对）；缺省 false。
        /// </summary>
        public bool VolumeBroadPhaseBruteForce { get; set; }

        // 以 tick 起点位置（体积中心）为键的均匀网格：格边长 = 2 × 最大半径（至少一个很小的正数），每个单位恰在一个格子里。
        private readonly Dictionary<long, List<int>> _gridCells = new Dictionary<long, List<int>>();
        private readonly List<List<int>> _gridListPool = new List<List<int>>();
        private double _gridCell = 1.0;

        private static long GridKey(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;

        private int GridCoord(double v)
        {
            var c = Math.Floor(v / _gridCell);
            return c > int.MaxValue / 2 ? int.MaxValue / 2 : (c < int.MinValue / 2 ? int.MinValue / 2 : (int)c);
        }

        private void BuildVolumeGrid()
        {
            foreach (var kv in _gridCells)
            {
                kv.Value.Clear();
                _gridListPool.Add(kv.Value);
            }

            _gridCells.Clear();
            if (_volumes.Count == 0)
            {
                return;
            }

            _gridCell = Math.Max(2.0 * _maxVolumeRadius, 1e-6);
            for (var i = 0; i < _volumes.Count; i++)
            {
                var p = _volumes[i].Start;
                var key = GridKey(GridCoord(p.X), GridCoord(p.Y));
                if (!_gridCells.TryGetValue(key, out var list))
                {
                    if (_gridListPool.Count > 0)
                    {
                        list = _gridListPool[_gridListPool.Count - 1];
                        _gridListPool.RemoveAt(_gridListPool.Count - 1);
                    }
                    else
                    {
                        list = new List<int>(4);
                    }

                    _gridCells[key] = list;
                }

                list.Add(i); // i 递增：每个格子里的下标有序。
            }
        }

        /// <summary>
        /// 收集"体积中心离线段 <paramref name="a"/>→<paramref name="b"/> 不超过 <paramref name="inflate"/>"的候选快照下标（升序、无重复）到 <paramref name="buffer"/>，
        /// 返回个数。候选集是真正可能命中者的超集（按包围盒膨胀取格子），所以按下标升序逐个计算与暴力扫描逐位一致。线段太长（格子数超过单位数）或暴力钩子打开时返回全部。
        /// </summary>
        private int GatherVolumeCandidates(Vec2 a, Vec2 b, double inflate, ref int[] buffer)
        {
            var n = _volumes.Count;
            if (buffer.Length < n)
            {
                buffer = new int[Math.Max(n, buffer.Length * 2)];
            }

            if (VolumeBroadPhaseBruteForce || _microPhase || n <= 8)
            {
                for (var i = 0; i < n; i++) buffer[i] = i;
                return n;
            }

            var x0 = GridCoord(Math.Min(a.X, b.X) - inflate);
            var x1 = GridCoord(Math.Max(a.X, b.X) + inflate);
            var y0 = GridCoord(Math.Min(a.Y, b.Y) - inflate);
            var y1 = GridCoord(Math.Max(a.Y, b.Y) + inflate);
            var cells = ((long)x1 - x0 + 1) * ((long)y1 - y0 + 1);
            if (cells > n)
            {
                for (var i = 0; i < n; i++) buffer[i] = i;
                return n;
            }

            var count = 0;
            for (var cx = x0; cx <= x1; cx++)
            {
                for (var cy = y0; cy <= y1; cy++)
                {
                    if (!_gridCells.TryGetValue(GridKey(cx, cy), out var list))
                    {
                        continue;
                    }

                    for (var k = 0; k < list.Count; k++)
                    {
                        buffer[count++] = list[k];
                    }
                }
            }

            Array.Sort(buffer, 0, count);
            return count;
        }

        /// <summary>把本 tick 存活的 <c>move</c>/<c>move_to_unit</c>/<c>move_displace</c> 意图按行动者归类（被同 tick 更晚的 <c>move_stop</c> 取消的不算）。</summary>
        private void IndexTickIntents()
        {
            _intentMovers.Clear();
            _intentDisplacers.Clear();
            var intents = _tickIntents;
            if (intents == null)
            {
                return;
            }

            for (var i = 0; i < intents.Count; i++)
            {
                var intent = intents[i];
                var isMove = intent.Kind == "move" || intent.Kind == "move_to_unit";
                var isDisplace = intent.Kind == "move_displace";
                if (!isMove && !isDisplace)
                {
                    continue;
                }

                if (_tickLastStop != null && _tickLastStop.TryGetValue(intent.ActorId, out var stopIndex) && stopIndex > i)
                {
                    continue;
                }

                (isMove ? _intentMovers : _intentDisplacers).Add(intent.ActorId);
            }
        }

        /// <summary>
        /// tick 开头预判一个有体积的单位这 tick 会不会自己动。只读 tick 开头状态与意图，所以与处理顺序无关；预判错了也不影响安全
        /// （阶段 B 对所有成对接触都兜底，保证不比起点更深地重叠），只影响手感：预判"不动"但实际动了，旁边的单位这一 tick 仍被它起点
        /// 位置拦住（旧行为）；预判"会动"但实际没动（输入被地形顶住、锁定等），旁边的单位这一 tick 在阶段 B 里撞停而不滑开。
        /// 会动的来源：受控位移进行中或本 tick 有新的位移意图（顿帧、死亡除外）、动作位移窗口、移动/追击意图、路径、会走动的追击、
        /// 档案声明了减速滑行时仍有残余速度。被控制/硬直的单位不会走动（受控位移除外）。
        /// </summary>
        private bool PredictMayMove(Id id, MotionProfile profile)
        {
            var world = _tickWorld;
            var mot = _mot;
            if (world == null || mot == null || !(world.GetEntity(id) is Unit unit) || world.IsPendingDestruction(id) || !unit.Alive)
            {
                return false;
            }

            if (mot.ActionClock?.IsPaused(id) ?? false)
            {
                return false; // 顿帧：位移保留、不推进。
            }

            var state = unit.MovementState;
            if (state.Displacement.HasValue || _intentDisplacers.Contains(id))
            {
                return true; // forced 优先于 rooted/staggered。
            }

            if ((mot.Stagger?.IsStaggered(id) ?? false) || IsLocked(unit))
            {
                return false;
            }

            var action = mot.Actions?.Current(id);
            if (action.HasValue && action.Value.Motion.HasValue && action.Value.Motion.Value.IsActiveAt(action.Value.ElapsedTicks))
            {
                return true;
            }

            if (_intentMovers.Contains(id) || state.CurrentPath != null)
            {
                return true;
            }

            if (state.Chase.HasValue)
            {
                return ChaseMayMove(unit, state, profile);
            }

            var v = state.Motion.Velocity;
            return profile.DecelMs > 0.0 && (v.X != 0.0 || v.Y != 0.0);
        }

        /// <summary>追击中的单位这 tick 会不会走：目标还在、同图，且距离没有落进停步距离（含滞回）里；口径同 <see cref="AdvanceChase"/>。</summary>
        private bool ChaseMayMove(Unit unit, MovementState state, MotionProfile profile)
        {
            var chase = state.Chase!.Value;
            var world = _tickWorld!;
            if (!(world.GetEntity(chase.TargetUnitId) is Unit target) || world.IsPendingDestruction(target.EntityId) || !target.Alive ||
                !target.MapId.Equals(unit.MapId) || !_unitStart.TryGetValue(target.EntityId, out var targetStart) ||
                !_unitStart.TryGetValue(unit.EntityId, out var selfStart))
            {
                return false;
            }

            var stopRange = chase.StopRange;
            if (profile.UnitBodyRadius > 0.0)
            {
                var targetRadius = ProfileOf(target.EntityId).UnitBodyRadius;
                if (targetRadius > 0.0)
                {
                    stopRange = Math.Max(stopRange, profile.UnitBodyRadius + targetRadius + 2.0 * _options.ArrivalEpsilon);
                }
            }

            var distance = (targetStart - selfStart).Length;
            return state.Mode != MoveMode.Idle ? distance > stopRange : distance > stopRange + _options.FollowResumeSlack;
        }

        /// <summary>
        /// 有体积的追击者读目标位置：读 tick 起点快照（顺序无关）；没有体积的追击者、未启用运动层、或目标不在快照里时读目标此刻的位置（既有行为）。
        /// </summary>
        private Vec2 ObservedTargetPosition(Unit observer, Unit target)
        {
            if (SelfVolumeRadius(observer) > 0.0 && VolumeSnapshotCurrent && _unitStart.TryGetValue(target.EntityId, out var start))
            {
                return start;
            }

            return target.Position;
        }

        /// <summary>
        /// 线段 <paramref name="from"/> → <paramref name="to"/> 与其他单位体积（快照位置）的首次接触：命中返回 true，并给出沿线段方向的入射距离
        /// <paramref name="hitDistance"/> 与入射点处的外法线。<paramref name="ignore"/> 非空时跳过该单位（滑动第二段不再和刚撞的那个比）。
        /// 起点已在体积内（<c>c &lt;= 0</c>）时只有"朝圆心走"才算命中，入射距离为 0。相切不算命中（判别式 &lt;= 0）。
        /// </summary>
        private bool SweepVolumes(
            Unit unit, double selfRadius, Vec2 from, Vec2 to, Id? ignore, out double hitDistance, out Vec2 normal, out Id hitId)
        {
            hitDistance = 0.0;
            normal = Vec2.Zero;
            hitId = default;
            var d = to - from;
            var len = d.Length;
            if (len <= ZeroLengthEpsilon)
            {
                return false;
            }

            var dir = new Vec2(d.X / len, d.Y / len);
            var best = double.MaxValue;
            var found = false;
            var candidates = GatherVolumeCandidates(from, to, selfRadius + _maxVolumeRadius, ref _candSweep);
            for (var k = 0; k < candidates; k++)
            {
                var entry = _volumes[_candSweep[k]];
                if (entry.Id.Equals(unit.EntityId) || (ignore.HasValue && entry.Id.Equals(ignore.Value)))
                {
                    continue;
                }

                if (entry.Map.HasValue && !entry.Map.Value.Equals(unit.MapId))
                {
                    continue;
                }

                var center = BodyAt(entry);
                var sum = selfRadius + entry.Radius;
                var f = from - center;
                var c = f.Dot(f) - sum * sum;
                var b = f.Dot(dir);
                double s;
                Vec2 n;
                if (c <= 0.0)
                {
                    if (!(b < 0.0))
                    {
                        continue; // 已在体积内但在走开（或切向）：放行。
                    }

                    s = 0.0;
                    var fl = f.Length;
                    n = fl > 1e-12 ? new Vec2(f.X / fl, f.Y / fl) : new Vec2(-dir.X, -dir.Y);
                }
                else
                {
                    if (b >= 0.0)
                    {
                        continue;
                    }

                    var disc = b * b - c;
                    if (disc <= 0.0)
                    {
                        continue;
                    }

                    s = -b - Math.Sqrt(disc);
                    if (s < 0.0 || s > len)
                    {
                        continue;
                    }

                    var hitPoint = from + dir * s;
                    n = new Vec2((hitPoint.X - center.X) / sum, (hitPoint.Y - center.Y) / sum);
                }

                // 几何上碰到了这个单位：它算不算静止障碍决定了结果（求解遍据此判断分类是否起作用）。会动的单位这一遍不是阶段 A 的静止障碍
                // （见 VolumeBody.Free）：与它的接触留给阶段 B 按最终位置求解。
                if (entry.Free)
                {
                    _volSensitive |= !_microPhase;
                    continue;
                }

                _volSensitive |= !_microPhase;

                // 同距离命中保留 id 更小的那个（快照按 id 排序、候选按下标升序、严格小于才替换）：结果不依赖单位处理顺序。
                if (s < best)
                {
                    best = s;
                    found = true;
                    normal = n;
                    hitId = entry.Id;
                }
            }

            if (found)
            {
                hitDistance = best;
            }

            return found;
        }

        /// <summary>结果守卫：<paramref name="end"/> 落进某个体积（快照位置）、且比起点 <paramref name="from"/> 更深（或起点在体积外）即违规。</summary>
        private bool ViolatesVolumes(Unit unit, double selfRadius, Vec2 from, Vec2 end)
        {
            const double tolerance = 1e-9;
            var candidates = GatherVolumeCandidates(end, end, selfRadius + _maxVolumeRadius, ref _candGuard);
            for (var k = 0; k < candidates; k++)
            {
                var entry = _volumes[_candGuard[k]];
                if (entry.Id.Equals(unit.EntityId))
                {
                    continue;
                }

                if (entry.Map.HasValue && !entry.Map.Value.Equals(unit.MapId))
                {
                    continue;
                }

                var center = BodyAt(entry);
                var sum = selfRadius + entry.Radius;
                var endDist = (end - center).Length;
                if (endDist >= sum - tolerance)
                {
                    continue;
                }

                var startDist = (from - center).Length;
                if (startDist < sum && endDist >= startDist - tolerance)
                {
                    continue; // 起点本来就在里面，这次没有更深。
                }

                _volSensitive |= !_microPhase;
                if (entry.Free)
                {
                    continue; // 会动的单位这一遍不是静止障碍（同 SweepVolumes）。
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// 把本 tick 的位移 <paramref name="from"/> → <paramref name="to"/> 按单位体积裁决。<paramref name="selfRadius"/> 为 0 或没有别的体积单位时
        /// 原样返回（<see cref="VolumeClip.Blocked"/> 为假）。
        /// </summary>
        private VolumeClip ClipByUnitVolumes(Unit unit, double selfRadius, Vec2 from, Vec2 to, bool slide) =>
            ClipPathByUnitVolumes(unit, selfRadius, from, to, null, slide);

        /// <summary>
        /// 折线版：位移实际走的是 <paramref name="from"/> → <paramref name="via"/> → <paramref name="to"/>（先撞墙截断、再沿墙切向滑一段）时，
        /// 两段各自做精确扫掠，而不是把起终点连成一条弦（弦会漏掉折线绕开的体积、也会误拦折线没碰到的体积）。<paramref name="via"/> 为空时是单段。
        /// 命中发生在哪一段就用那一段的方向与长度做停止/滑开；命中前的段完整走过。
        /// </summary>
        private VolumeClip ClipPathByUnitVolumes(Unit unit, double selfRadius, Vec2 from, Vec2 to, Vec2? via, bool slide)
        {
            var clip = new VolumeClip { End = to };
            if (!(selfRadius > 0.0))
            {
                return clip;
            }

            EnsureVolumeSnapshot();
            if (_volumes.Count == 0)
            {
                return clip;
            }

            // 记下"这个单位在体积裁决之前提议了非零位移"：只由它自己的状态、意图与地形决定，与别的单位被怎样分类无关。
            var proposed = via.HasValue ? (via.Value - from).Length + (to - via.Value).Length : (to - from).Length;
            if (proposed > ZeroLengthEpsilon && _volumeById.TryGetValue(unit.EntityId, out var attemptBody))
            {
                attemptBody.Attempted = true;
            }

            var legStart = from;
            var legCount = via.HasValue ? 2 : 1;
            for (var leg = 0; leg < legCount; leg++)
            {
                var legEnd = leg == legCount - 1 ? to : via!.Value;
                if (!SweepVolumes(unit, selfRadius, legStart, legEnd, null, out var hitDistance, out var normal, out var hitId))
                {
                    legStart = legEnd;
                    continue;
                }

                var d = legEnd - legStart;
                var len = d.Length;
                var dir = new Vec2(d.X / len, d.Y / len);
                var pullBack = Math.Min(hitDistance, _options.ArrivalEpsilon);
                var advance = hitDistance - pullBack;
                var p1 = legStart + dir * advance;
                clip.Blocked = true;
                clip.End = p1;
                clip.HitId = hitId;
                clip.Normal = normal;
                var poly = new List<Vec2> { from };
                if (leg == 1)
                {
                    poly.Add(legStart); // 第二段命中：折线拐点是第一段的终点。
                }

                poly.Add(p1);

                if (slide)
                {
                    var remaining = dir * (len - advance);
                    var into = remaining.Dot(normal);
                    if (into < 0.0)
                    {
                        var tangent = new Vec2(remaining.X - normal.X * into, remaining.Y - normal.Y * into);
                        var tangentLen = tangent.Length;
                        if (tangentLen > ZeroLengthEpsilon)
                        {
                            var tangentDir = new Vec2(tangent.X / tangentLen, tangent.Y / tangentLen);
                            var p2 = p1 + tangent;
                            var secondBlocked = false;
                            var secondNormal = Vec2.Zero;
                            if (SweepVolumes(unit, selfRadius, p1, p2, hitId, out var hit2, out var n2, out _))
                            {
                                p2 = p1 + tangentDir * (hit2 - Math.Min(hit2, _options.ArrivalEpsilon));
                                secondBlocked = true;
                                secondNormal = n2;
                            }

                            if (_navigation != null && (p2 - p1).Length > ZeroLengthEpsilon)
                            {
                                // 切向那一段同样要过地形裁决（滑开不能滑进墙里）。
                                var wallHit = NavRaycast(unit, p1, p2);
                                if (wallHit.HasValue)
                                {
                                    var wallDistance = (wallHit.Value - p1).Length;
                                    p2 = p1 + tangentDir * (wallDistance - Math.Min(wallDistance, _options.ArrivalEpsilon));
                                }

                                if (!_navigation.IsWalkable(unit.MapId, p2))
                                {
                                    p2 = p1;
                                }
                            }

                            if ((p2 - p1).Length > ZeroLengthEpsilon)
                            {
                                clip.End = p2;
                                clip.Slid = true;
                                clip.SecondBlocked = secondBlocked;
                                clip.SecondNormal = secondNormal;
                                poly.Add(p2);
                            }
                        }
                    }
                }

                if (ViolatesVolumes(unit, selfRadius, from, clip.End))
                {
                    clip.End = from;
                    clip.Slid = false;
                    clip.SecondBlocked = false;
                    poly.Clear();
                    poly.Add(from);
                }

                clip.Poly = poly.ToArray();
                return clip;
            }

            return clip;
        }

        // ================================================================== 位移折线记录（阶段 A → 阶段 B）

        /// <summary>
        /// 记下一条边（<paramref name="from"/> → <paramref name="to"/>）：阶段 B 的成对撞停按单位实际走的折线逐段求接触，而不是起终点的弦。
        /// 没有体积的单位、未启用运动层时什么也不做；<paramref name="indexAfter"/> 是路径跟随/追击走完这条边时的路径下标（其余来源 -1）。
        /// 边必须接在上一条边之后，否则标记折线不连续，阶段 B 对该单位退回用起终点的弦。
        /// </summary>
        private void NoteTrail(Id id, Vec2 from, Vec2 to, int indexAfter)
        {
            if (!VolumeSnapshotCurrent || !_volumeById.TryGetValue(id, out var body) || from.Equals(to))
            {
                return;
            }

            if (body.Trail == null)
            {
                body.Trail = new List<Vec2> { body.Start };
                body.TrailIndex = new List<int>();
            }

            if (!body.Trail[body.Trail.Count - 1].Equals(from))
            {
                body.TrailBroken = true;
                return;
            }

            body.Trail.Add(to);
            body.TrailIndex!.Add(indexAfter);
        }

        /// <summary>记下一条折线（点序列，首点是本段起点）；<paramref name="indexAfter"/> 同 <see cref="NoteTrail"/>，整条折线共用。</summary>
        private void NoteTrailPoly(Id id, IReadOnlyList<Vec2> points, int indexAfter)
        {
            for (var k = 1; k < points.Count; k++)
            {
                NoteTrail(id, points[k - 1], points[k], indexAfter);
            }
        }

        /// <summary>
        /// 方向位移、动作位移写位置时记折线：撞墙后沿墙滑动的折线拐点 <paramref name="via"/>，或体积裁决给出的折线 <paramref name="poly"/>
        /// （命中时的实际折线），都没有就是起终点一条边。
        /// </summary>
        private void NoteMotionTrail(Unit unit, Vec2 from, Vec2 to, Vec2? via, Vec2[]? poly)
        {
            if (!VolumeSnapshotCurrent)
            {
                return;
            }

            if (poly != null)
            {
                NoteTrailPoly(unit.EntityId, poly, -1);
            }
            else if (via.HasValue)
            {
                NoteTrail(unit.EntityId, from, via.Value, -1);
                NoteTrail(unit.EntityId, via.Value, to, -1);
            }
            else
            {
                NoteTrail(unit.EntityId, from, to, -1);
            }
        }

        /// <summary>
        /// 登记本 tick 位移的速度剖面（<c>null</c> = 匀速）：必须在这个来源写第一条边之前调用。一个 tick 里只有"单一来源写了全部边"的剖面可信，
        /// 第二个来源（或登记之前就已有边）使剖面作废（<see cref="VolumeBody.ProfileMixed"/>，成对裁决退回匀速）。
        /// </summary>
        private void NoteProfile(Id id, SpeedProfile? profile)
        {
            if (!VolumeSnapshotCurrent || !_volumeById.TryGetValue(id, out var body))
            {
                return;
            }

            if (body.ProfileNoted || (body.Trail != null && body.Trail.Count > 1))
            {
                body.ProfileMixed = true;
                return;
            }

            body.ProfileNoted = true;
            body.Profile = profile;
        }

        /// <summary>登记"这个单位提议了非零位移"（穿过式位移不经体积裁决，在这里补记；见 <see cref="VolumeBody.Attempted"/>）。</summary>
        private void NoteAttempt(Id id, Vec2 from, Vec2 to)
        {
            if (VolumeSnapshotCurrent && _volumeById.TryGetValue(id, out var body) && (to - from).Length > ZeroLengthEpsilon)
            {
                body.Attempted = true;
            }
        }

        /// <summary>路径跟随/追击一个 tick 里的推进轨迹（折线点与每条边走完时的路径下标）；只在本单位有体积时创建。</summary>
        private sealed class PathTrail
        {
            public readonly List<Vec2> Points;
            public readonly List<int> Index = new List<int>();

            /// <summary>轨迹里下标所指的路径与本 tick 开始推进时的下标（路径可能是本 tick 刚建的新路径，下标从 0 起）。</summary>
            public readonly IReadOnlyList<Vec2> Path;

            public readonly int StartIndex;

            /// <summary>本 tick 路径推进的速度剖面（来自运动层的速度积分，null = 匀速）。</summary>
            public SpeedProfile? Profile;

            public PathTrail(Vec2 start, IReadOnlyList<Vec2> path, int startIndex)
            {
                Points = new List<Vec2> { start };
                Path = path;
                StartIndex = startIndex;
            }

            public void Add(Vec2 to, int indexAfter)
            {
                Points.Add(to);
                Index.Add(indexAfter);
            }

            public void AddPoly(Vec2[] poly, int indexAfter)
            {
                for (var k = 1; k < poly.Length; k++)
                {
                    Add(poly[k], indexAfter);
                }
            }
        }

        /// <summary>路径推进的位置写入通过之后（没有被全局单位阻挡丢弃），把轨迹交给阶段 B。</summary>
        private void CommitPathTrail(Unit unit, PathTrail? trail)
        {
            if (trail == null)
            {
                return;
            }

            NoteProfile(unit.EntityId, trail.Profile);
            for (var k = 1; k < trail.Points.Count; k++)
            {
                NoteTrail(unit.EntityId, trail.Points[k - 1], trail.Points[k], trail.Index[k - 1]);
            }

            if (VolumeSnapshotCurrent && _volumeById.TryGetValue(unit.EntityId, out var body))
            {
                body.TrailPath = trail.Path;
                body.TrailStartIndex = trail.StartIndex;
            }
        }

        /// <summary>路径跟随本 tick 走到终点：记下"未到达"状态模板（路径与原状态，下标待填），阶段 B 把位移缩短时据此取消到达。</summary>
        private void NoteArrival(Unit unit, MovementState state, IReadOnlyList<Vec2> path)
        {
            if (VolumeSnapshotCurrent && _volumeById.TryGetValue(unit.EntityId, out var body))
            {
                body.UnarriveTemplate = new MovementState(
                    path, state.Mode, state.MovementLocked, 0, state.NavVersion, null, null, state.RequestedTarget);
            }
        }

        /// <summary>记下到达事件（模式变 Idle 的 <c>unit.state_changed</c>）在发件箱里的位置，供取消到达时撤销。</summary>
        private void NoteArrivalEvent(Id unitId, int outboxIndex)
        {
            if (VolumeSnapshotCurrent && _volumeById.TryGetValue(unitId, out var body))
            {
                body.ArrivalOutboxIndex = outboxIndex;
            }
        }

        // ================================================================== 路径跟随与追击：局部绕行

        /// <summary>
        /// 路径跟随与追击被体积挡住后的局部绕行（<c>path_avoid_units</c>，缺省真）：<paramref name="blocked"/> 是紧贴体积边界的那次裁决，
        /// <paramref name="pos"/> 是停下的位置（边界前一个到达容差），<paramref name="goal"/> 是当前路点，<paramref name="budget"/> 是本 tick 剩余的位移预算。
        /// 绕行方向 = 目标方向去掉沿"被撞单位中心 → 本单位"法向的分量后的切向（正对着撞上、切向分量为零时取法向逆时针的垂线，保证确定），
        /// 以满速度走完剩余预算：沿体积边界外侧擦过去，直到目标方向不再穿过体积，下一 tick 起恢复沿路径直走。
        /// 目标方向偏向的一侧被地形或别的体积挡死就停下，只有正对着撞上（没有偏向）时才再试另一侧；两侧都走不动、或目标点本身就落在被撞单位的体积里（绕过去也到不了）时返回 false，
        /// 调用方保持"停在体积前"。绕行步同样经别的体积扫掠与地形裁决，不穿墙、不进别的体积。
        /// </summary>
        private bool TryAvoidUnits(Unit unit, double selfRadius, Vec2 pos, Vec2 goal, double budget, in VolumeClip blocked, out Vec2 end)
        {
            end = pos;
            if (!blocked.Blocked || blocked.Slid || !(budget > ZeroLengthEpsilon))
            {
                return false;
            }

            if (!TryGetBody(blocked.HitId, out var hitBody))
            {
                return false;
            }

            var center = BodyAt(hitBody);
            var sum = selfRadius + hitBody.Radius;
            if ((goal - center).Length < sum + 2.0 * _options.ArrivalEpsilon)
            {
                return false; // 目标点就在挡路单位的体积里：绕过去也到不了，停下。
            }

            var toGoal = goal - pos;
            var goalLen = toGoal.Length;
            if (goalLen <= ZeroLengthEpsilon)
            {
                return false;
            }

            var g = new Vec2(toGoal.X / goalLen, toGoal.Y / goalLen);
            var fromCenter = pos - center;
            var fl = fromCenter.Length;
            var n = fl > 1e-12 ? new Vec2(fromCenter.X / fl, fromCenter.Y / fl) : blocked.Normal;
            var gn = g.Dot(n);
            var tangent = new Vec2(g.X - n.X * gn, g.Y - n.Y * gn);
            var tl = tangent.Length;
            var t = tl > 1e-9 ? new Vec2(tangent.X / tl, tangent.Y / tl) : new Vec2(-n.Y, n.X);

            if (TryAvoidStep(unit, selfRadius, pos, t, budget, blocked.HitId, out end))
            {
                return true;
            }

            // 只有正对着撞上（切向分量为零、侧由垂线任取）时才试另一侧；目标方向已经偏向某一侧时，那一侧被挡死就停下——
            // 否则窄道里会在两侧之间来回弹（贴墙的一侧走不动 → 换到另一侧 → 沿体积边界滑回正中 → 又换回来）。
            return tl > 1e-9 ? false : TryAvoidStep(unit, selfRadius, pos, new Vec2(n.Y, -n.X), budget, blocked.HitId, out end);
        }

        private bool TryAvoidStep(Unit unit, double selfRadius, Vec2 pos, Vec2 dir, double budget, Id ignoreId, out Vec2 end)
        {
            end = pos;
            var length = budget;
            var p2 = pos + dir * length;

            // 沿切向走不会更深地进入刚撞的那个单位（法向分量 ≥ 0），所以扫掠时忽略它，只防别的体积。
            if (SweepVolumes(unit, selfRadius, pos, p2, ignoreId, out var hit, out _, out _))
            {
                length = hit - Math.Min(hit, _options.ArrivalEpsilon);
                p2 = pos + dir * length;
            }

            if (_navigation != null && length > ZeroLengthEpsilon)
            {
                var wallHit = NavRaycast(unit, pos, p2);
                if (wallHit.HasValue)
                {
                    var wallDistance = (wallHit.Value - pos).Length;
                    length = Math.Min(length, wallDistance - Math.Min(wallDistance, _options.ArrivalEpsilon));
                    p2 = pos + dir * length;
                }

                if (length > ZeroLengthEpsilon && !_navigation.IsWalkable(unit.MapId, p2))
                {
                    return false;
                }
            }

            if (!(length > ZeroLengthEpsilon) || ViolatesVolumes(unit, selfRadius, pos, p2))
            {
                return false;
            }

            end = p2;
            return true;
        }
    }
}
