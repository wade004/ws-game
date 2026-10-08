using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// <see cref="IVerticalMotion"/> 的唯一实现：为被抛起的单位积分抛体运动并写回 <see cref="Unit.HeightOffset"/>
    /// （手感设计/06 第 1.2 节；单位、公式与设计决定见 <see cref="VerticalAxisOptions"/>/<see cref="IVerticalMotion"/>
    /// 与 unit README 判断记录）。
    /// <para>
    /// 判断记录（只积分"被抛起"的单位）：没有被 <see cref="Launch"/> 的单位 <see cref="Unit.HeightOffset"/> 保持原值不动——
    /// 飘浮怪、悬空平台上的单位是"静态高度"，不受重力；只有跳跃、击飞这类"离地"事件才进入积分。落地后高度恒为 0
    /// （地面就是 0，没有斜坡与台阶）。
    /// </para>
    /// <para>
    /// 判断记录（解析式积分，不做欧拉累加）：每步按累计飞行时间代入 <c>h0 + v0·t − g·t²/2</c>，帧长不均匀时没有积分漂移；
    /// 累计时间用逐步累加（步长恒定时与 tick 数 × dt 相同）。落地判定 <c>h ≤ 0</c>，落地步高度写 0。
    /// </para>
    /// <para>
    /// 判断记录（确定性）：每步按单位 Id 序数遍历；已被销毁的实体静默丢弃其飞行状态。离散步（回合制）不推进——
    /// 竖直运动是连续时间模型的概念，回合制没有"下落过程"。
    /// </para>
    /// </summary>
    public sealed class VerticalMotionHost : IVerticalMotion, Core.Rules.Common.ILaunchSink, Core.Rules.Common.IAirborneQuery, ITerrainStepConstraint
    {
        private sealed class Flight
        {
            public double StartHeight;
            public double InitialSpeed;
            public double Elapsed;

            /// <summary>本次离地以来累计的空中时间（秒）：重新抛起（再次击飞、空中跳跃、天花板反弹不清零）沿用，落地事件据此报告空中时长。</summary>
            public double AirTime;

            /// <summary>本次离地以来已用掉的空中跳跃次数（地面起跳不计）。</summary>
            public int AirJumps;

            /// <summary>这次飞行是走出平台边缘的自然下落（初速 0，没被抛起过）：土狼时间起跳的前提（ADR-0143）。</summary>
            public bool LedgeFall;

            /// <summary>主动下穿的那块平台（<see cref="DropThrough"/>）：这次飞行里不再被它接住；落地即结束飞行，不需要显式清除。</summary>
            public string? IgnorePlatform;
        }

        /// <summary>地形能力装配后逐单位的"贴地"簿记：上一次观测的位置与是否贴着地面（只在 <see cref="VerticalAxisOptions.Terrain"/> 非空时使用）。</summary>
        private sealed class Walker
        {
            public Vec2 LastPosition;
            public bool Grounded;

            /// <summary>正站着的平台 id（ADR-0170）；站在地面上为 null。</summary>
            public string? PlatformId;
        }

        /// <summary>脚下与地面的距离不超过它视为"贴着地面"（浮点容差，不是口味配置）。</summary>
        private const double GroundEpsilon = 1e-6;

        private readonly IWorldSim _world;
        private readonly VerticalAxisOptions _options;
        private readonly IEventBus? _bus;
        private readonly Dictionary<Id, Flight> _flights = new Dictionary<Id, Flight>();
        private readonly Dictionary<Id, Walker> _walkers = new Dictionary<Id, Walker>();
        private readonly List<Id> _scratch = new List<Id>();

        public VerticalMotionHost(IWorldSim world, VerticalAxisOptions options)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();
        }

        /// <summary>
        /// 带事件总线的构造（手感落地 M4-W1b）：<see cref="VerticalAxisOptions.EmitLandedEvent"/> 为真时，单位落地发 <c>unit.landed</c>
        /// （<see cref="UnitLandedEvent"/>：落地高度、空中时长、落地时下落速度）。不带总线的重载、或选项缺省（假）都不发事件（旧调用方行为不变）。
        /// </summary>
        public VerticalMotionHost(IWorldSim world, VerticalAxisOptions options, IEventBus bus)
            : this(world, options)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        /// <summary>
        /// 单位访问口（可选；<c>CarriersAssembly</c> 装配时回填）：移动平台带走站在上面的单位时经它写位置，空间索引随之同步
        /// （同 <see cref="WorldUnitAccess.SetPosition"/>）。缺省 null 时只写实体位置（测试与不带空间索引的宿主）。
        /// </summary>
        public Core.Rules.Common.IUnitAccess? UnitAccess { get; set; }

        /// <summary>当前在空中的单位数（诊断用）。</summary>
        public int AirborneCount => _flights.Count;

        /// <summary>是否声明了地形高度能力（<see cref="VerticalAxisOptions.Terrain"/> 非空）。</summary>
        public bool TerrainActive => _options.Terrain != null;

        /// <summary>是否启用台阶/坡度阻挡（声明了地形与 <see cref="VerticalAxisOptions.StepHeight"/>）。</summary>
        public bool StepBlockingActive => _options.Terrain != null && _options.StepHeight.HasValue;

        public bool IsAirborne(Id unitId) => _flights.ContainsKey(unitId);

        public double GetVerticalSpeed(Id unitId) =>
            _flights.TryGetValue(unitId, out var f) ? f.InitialSpeed - _options.Gravity * f.Elapsed : 0.0;

        public System.Collections.Generic.IReadOnlyList<Id> AirborneUnits()
        {
            var list = new System.Collections.Generic.List<Id>(_flights.Keys);
            list.Sort();
            return list;
        }

        public int AirJumpsUsed(Id unitId) => _flights.TryGetValue(unitId, out var f) ? f.AirJumps : 0;

        public double GetAirControl(Id unitId) =>
            _options.AirControl.HasValue && _flights.ContainsKey(unitId) ? _options.AirControl.Value : 1.0;

        /// <summary>该点的地面高度（没有地形能力时恒为 0）。</summary>
        public double GroundHeightAt(Id mapId, Vec2 point) =>
            _options.Terrain != null ? _options.Terrain.GetGroundHeight(mapId, point) : 0.0;

        public bool Launch(Id unitId, double initialSpeed)
        {
            if (!(initialSpeed > 0.0) || double.IsInfinity(initialSpeed))
            {
                throw new ArgumentOutOfRangeException(nameof(initialSpeed), initialSpeed, "初速必须为正的有限数");
            }

            if (!(_world.GetEntity(unitId) is Unit unit) || !unit.Alive)
            {
                return false;
            }

            StartFlight(unitId, unit, initialSpeed);
            return true;
        }

        public bool LaunchToApex(Id unitId, double apexHeight)
        {
            if (!(apexHeight > 0.0) || double.IsInfinity(apexHeight))
            {
                throw new ArgumentOutOfRangeException(nameof(apexHeight), apexHeight, "顶点高度必须为正的有限数");
            }

            return Launch(unitId, Math.Sqrt(2.0 * _options.Gravity * apexHeight));
        }

        /// <summary><see cref="Core.Rules.Common.ILaunchSink"/>：受击裁决的击飞，转 <see cref="LaunchToApex"/>。</summary>
        public void BeginLaunch(Id unitId, double apexHeightWorld) => LaunchToApex(unitId, apexHeightWorld);

        /// <summary>
        /// 带叠加语义的击飞（<see cref="Core.Rules.Common.ILaunchSink.BeginLaunch(Id, double, Core.Rules.Common.LaunchStackMode, double)"/>）：
        /// 叠加且单位已在空中时，新初速 = 当前竖直速度 + 本次击飞初速（<c>sqrt(2·g·H)</c>），起点为当前高度；有上限（<paramref name="stackCapApexWorld"/> &gt; 0）
        /// 时把结果限制在"升到该顶点高度所需初速"以内。叠加后初速可以仍为负（下落速度被部分抵消），照常从当前高度继续抛体。
        /// 其余情形（重新抛起、单位在地面）与 <see cref="BeginLaunch(Id, double)"/> 一致。
        /// </summary>
        public void BeginLaunch(Id unitId, double apexHeightWorld, Core.Rules.Common.LaunchStackMode stack, double stackCapApexWorld)
        {
            if (stack != Core.Rules.Common.LaunchStackMode.Add || !_flights.TryGetValue(unitId, out var flight))
            {
                BeginLaunch(unitId, apexHeightWorld);
                return;
            }

            if (!(apexHeightWorld > 0.0) || double.IsInfinity(apexHeightWorld))
            {
                throw new ArgumentOutOfRangeException(nameof(apexHeightWorld), apexHeightWorld, "顶点高度必须为正的有限数");
            }

            if (!(_world.GetEntity(unitId) is Unit unit) || !unit.Alive)
            {
                return;
            }

            var speed = flight.InitialSpeed - _options.Gravity * flight.Elapsed + Math.Sqrt(2.0 * _options.Gravity * apexHeightWorld);
            if (stackCapApexWorld > 0.0 && !double.IsInfinity(stackCapApexWorld))
            {
                var cap = Math.Sqrt(2.0 * _options.Gravity * stackCapApexWorld);
                if (speed > cap)
                {
                    speed = cap;
                }
            }

            StartFlight(unitId, unit, speed);
        }

        /// <summary>
        /// 带绝对高度上限的击飞（<see cref="Core.Rules.Common.ILaunchSink.BeginLaunch(Id, double, Core.Rules.Common.LaunchStackMode, double, double)"/>，
        /// 手感档案 <c>launch_height_cap</c>）：在叠加语义与叠加上限之外，把击飞后的初速再限制到"从当前脚下高度升到 <paramref name="heightCapWorld"/>
        /// 所需的初速"以内（高度上限是世界高度，同 <see cref="Unit.HeightOffset"/>）；脚下已不低于上限时初速限制为 0——地面单位不被抛起（无事发生），
        /// 空中单位停止上升、从当前高度起下落（叠加后仍向下的速度保持）。<paramref name="heightCapWorld"/> 非正时与 4 参数重载完全一致。
        /// </summary>
        public void BeginLaunch(
            Id unitId, double apexHeightWorld, Core.Rules.Common.LaunchStackMode stack, double stackCapApexWorld, double heightCapWorld)
        {
            if (!(heightCapWorld > 0.0) || double.IsInfinity(heightCapWorld))
            {
                BeginLaunch(unitId, apexHeightWorld, stack, stackCapApexWorld);
                return;
            }

            if (!(apexHeightWorld > 0.0) || double.IsInfinity(apexHeightWorld))
            {
                throw new ArgumentOutOfRangeException(nameof(apexHeightWorld), apexHeightWorld, "顶点高度必须为正的有限数");
            }

            if (!(_world.GetEntity(unitId) is Unit unit) || !unit.Alive)
            {
                return;
            }

            var airborne = _flights.TryGetValue(unitId, out var flight);
            var speed = Math.Sqrt(2.0 * _options.Gravity * apexHeightWorld);
            if (stack == Core.Rules.Common.LaunchStackMode.Add && airborne)
            {
                speed += flight!.InitialSpeed - _options.Gravity * flight.Elapsed;
                if (stackCapApexWorld > 0.0 && !double.IsInfinity(stackCapApexWorld))
                {
                    var stackCap = Math.Sqrt(2.0 * _options.Gravity * stackCapApexWorld);
                    if (speed > stackCap)
                    {
                        speed = stackCap;
                    }
                }
            }

            var room = heightCapWorld - unit.HeightOffset;
            var allowed = room > 0.0 ? Math.Sqrt(2.0 * _options.Gravity * room) : 0.0;
            if (speed > allowed)
            {
                speed = allowed;
            }

            if (speed <= 0.0 && !airborne)
            {
                return;
            }

            StartFlight(unitId, unit, speed);
        }

        public bool Jump(Id unitId)
        {
            var airborne = _flights.TryGetValue(unitId, out var existing);
            if (airborne && !AirJumpAllowed(existing!.AirJumps))
            {
                return false;
            }

            var airJumps = airborne ? existing!.AirJumps + 1 : 0;
            if (!LaunchToApex(unitId, _options.JumpHeight))
            {
                return false;
            }

            _flights[unitId].AirJumps = airJumps;
            return true;
        }

        public bool IsLedgeFall(Id unitId) => _flights.TryGetValue(unitId, out var f) && f.LedgeFall;

        public string? StandingPlatform(Id unitId) =>
            !_flights.ContainsKey(unitId) && _walkers.TryGetValue(unitId, out var w) ? w.PlatformId : null;

        public bool Plunge(Id unitId, double downSpeed)
        {
            if (!(downSpeed > 0.0) || double.IsInfinity(downSpeed))
            {
                throw new ArgumentOutOfRangeException(nameof(downSpeed), downSpeed, "俯冲速度必须为正的有限数");
            }

            if (!_flights.ContainsKey(unitId) || !(_world.GetEntity(unitId) is Unit unit) || !unit.Alive)
            {
                return false;
            }

            StartFlight(unitId, unit, -downSpeed);
            return true;
        }

        public bool DropThrough(Id unitId)
        {
            if (_options.Platforms == null || _flights.ContainsKey(unitId) || !_walkers.TryGetValue(unitId, out var walker) || walker.PlatformId == null)
            {
                return false;
            }

            if (!(_world.GetEntity(unitId) is Unit unit) || !unit.Alive)
            {
                return false;
            }

            var platform = walker.PlatformId;
            StartFlight(unitId, unit, 0.0);
            _flights[unitId].IgnorePlatform = platform;
            return true;
        }

        public bool JumpFromLedge(Id unitId)
        {
            if (!_flights.TryGetValue(unitId, out var existing) || !existing.LedgeFall)
            {
                return false;
            }

            var airJumps = existing.AirJumps;
            if (!LaunchToApex(unitId, _options.JumpHeight))
            {
                return false;
            }

            _flights[unitId].AirJumps = airJumps;
            return true;
        }

        public bool CutAscent(Id unitId, double ratio)
        {
            if (!(ratio > 0.0 && ratio < 1.0) || !_flights.TryGetValue(unitId, out var f))
            {
                return false;
            }

            var speed = f.InitialSpeed - _options.Gravity * f.Elapsed;
            if (!(speed > 0.0) || !(_world.GetEntity(unitId) is Unit unit) || !unit.Alive)
            {
                return false;
            }

            StartFlight(unitId, unit, speed * ratio);
            return true;
        }

        private bool AirJumpAllowed(int used) =>
            _options.MaxAirJumps.HasValue ? used < _options.MaxAirJumps.Value : _options.AllowAirJump;

        /// <summary>
        /// 以 <paramref name="initialSpeed"/>（可为 0 或负：离开平台的下落、叠加后仍向下）从单位当前高度开始一次飞行；已在空中则替换，
        /// 空中跳跃计数沿用（被击飞不重置计数，也不会白送一次空中跳跃）。
        /// </summary>
        private void StartFlight(Id unitId, Unit unit, double initialSpeed)
        {
            var airJumps = 0;
            var airTime = 0.0;
            if (_flights.TryGetValue(unitId, out var old))
            {
                airJumps = old.AirJumps;
                airTime = old.AirTime;
            }

            _flights[unitId] = new Flight
            {
                StartHeight = unit.HeightOffset, InitialSpeed = initialSpeed, Elapsed = 0.0, AirJumps = airJumps, AirTime = airTime,
            };
            if (_walkers.TryGetValue(unitId, out var walker))
            {
                walker.Grounded = false;
                walker.PlatformId = null;
            }
        }

        /// <summary>
        /// 推进一步：对每个在空中的单位按累计飞行时间重算高度，落地（脚下高度不高于该点地面）的写地面高度并结束飞行；
        /// 上升中碰到天花板的竖直速度清零、从天花板高度起开始下落；声明了地形能力时再让贴地行走的单位贴合地面。
        /// </summary>
        public void Advance(double dt)
        {
            if (!(dt > 0.0))
            {
                return;
            }

            if (_options.Platforms != null)
            {
                _options.Platforms.Advance(dt);
                CarryRiders(_options.Platforms);
            }

            if (_flights.Count > 0)
            {
                AdvanceFlights(dt);
            }

            if (_options.Terrain != null)
            {
                FollowGround();
            }
        }

        /// <summary>
        /// 移动平台带走站在上面的单位（ADR-0170）：平台本步的位移（<see cref="ITerrainPlatforms2D.LastMotions"/>）加到每个站在它上面的单位
        /// 的位置与脚下高度上，并更新"上次位置"簿记（被带走不算自己走动，不会触发悬崖判定）。按 Id 序数遍历，保证确定性。
        /// </summary>
        private void CarryRiders(ITerrainPlatforms2D platforms)
        {
            var motions = platforms.LastMotions;
            if (motions.Count == 0 || _walkers.Count == 0)
            {
                return;
            }

            _scratch.Clear();
            foreach (var pair in _walkers)
            {
                if (pair.Value.PlatformId != null)
                {
                    _scratch.Add(pair.Key);
                }
            }

            if (_scratch.Count == 0)
            {
                return;
            }

            _scratch.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            for (var i = 0; i < _scratch.Count; i++)
            {
                var id = _scratch[i];
                var walker = _walkers[id];
                if (!(_world.GetEntity(id) is Unit unit))
                {
                    continue;
                }

                for (var m = 0; m < motions.Count; m++)
                {
                    var motion = motions[m];
                    if (!string.Equals(motion.PlatformId, walker.PlatformId, StringComparison.Ordinal) || !motion.MapId.Equals(unit.MapId))
                    {
                        continue;
                    }

                    var moved = unit.Position + motion.Delta;
                    if (UnitAccess != null)
                    {
                        UnitAccess.SetPosition(id, moved);
                    }
                    else
                    {
                        unit.Position = moved;
                    }

                    unit.HeightOffset += motion.HeightDelta;
                    walker.LastPosition = unit.Position;
                    break;
                }
            }
        }

        private void AdvanceFlights(double dt)
        {
            var terrain = _options.Terrain;
            _scratch.Clear();
            _scratch.AddRange(_flights.Keys);
            _scratch.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            for (var i = 0; i < _scratch.Count; i++)
            {
                var id = _scratch[i];
                var flight = _flights[id];
                if (!(_world.GetEntity(id) is Unit unit))
                {
                    _flights.Remove(id);
                    _walkers.Remove(id);
                    continue;
                }

                flight.Elapsed += dt;
                flight.AirTime += dt;
                var t = flight.Elapsed;
                var footBefore = unit.HeightOffset;
                var h = flight.StartHeight + flight.InitialSpeed * t - 0.5 * _options.Gravity * t * t;
                var ground = terrain != null ? terrain.GetGroundHeight(unit.MapId, unit.Position) : 0.0;
                var descending = flight.InitialSpeed - _options.Gravity * t <= 0.0;
                // 落地只发生在下落（竖直速度 ≤ 0）时：上升中飞进不超过台阶高度的更高地面（平台边缘）不算落地，继续上升；
                // 到达顶点或开始下落之后才落到那块地面上。没有地形能力时地面恒为 0，h ≤ 0 必然已在下落，沿用旧判定（逐位不变）。
                var landed = h <= ground && (terrain == null || descending);
                string? landedPlatform = null;
                if (_options.Platforms != null && descending
                    && _options.Platforms.TryLand(unit.MapId, unit.Position, footBefore, h, flight.IgnorePlatform, out var contact)
                    && (!landed || contact.Height >= ground))
                {
                    // 单向平台（ADR-0170）：脚下从顶面上方穿到顶面或以下才落在平台上；平台与地面同时满足时落在更高的那个（先碰到的）。
                    landed = true;
                    ground = contact.Height;
                    landedPlatform = contact.PlatformId;
                }

                if (landed)
                {
                    unit.HeightOffset = ground;
                    _flights.Remove(id);
                    MarkGrounded(id, unit, landedPlatform);
                    if (_bus != null && _options.EmitLandedEvent)
                    {
                        _bus.Enqueue(new UnitLandedEvent(id, ground, flight.AirTime, -(flight.InitialSpeed - _options.Gravity * t)));
                    }

                    continue;
                }

                if (terrain != null)
                {
                    var ceiling = terrain.GetCeilingHeight(unit.MapId, unit.Position);
                    var rising = flight.InitialSpeed - _options.Gravity * t > 0.0;
                    if (rising && h >= ceiling && unit.HeightOffset < ceiling)
                    {
                        // 撞天花板：竖直速度清零，从天花板高度起开始下落（初速 0 的抛体）。
                        unit.HeightOffset = ceiling;
                        flight.StartHeight = ceiling;
                        flight.InitialSpeed = 0.0;
                        flight.Elapsed = 0.0;
                        continue;
                    }
                }

                unit.HeightOffset = h;
            }
        }

        private void MarkGrounded(Id id, Unit unit, string? platformId = null)
        {
            if (_options.Terrain == null)
            {
                return;
            }

            _walkers[id] = new Walker { LastPosition = unit.Position, Grounded = true, PlatformId = platformId };
        }

        /// <summary>
        /// 贴地行走（声明了地形能力才运行）：对每个不在空中的单位（按 Id 序）——首次观测时脚下低于地面的抬到地面、记下"是否贴地"；
        /// 之后位置变化过且贴地的单位：本 tick 走过的线段上出现悬崖（<see cref="TerrainStepMath.FirstDrop"/>，阈值见
        /// <see cref="FallThreshold"/>）时离地下落（初速 0 的抛体），否则脚下高度写成新位置的地面高度（上坡、缓下坡、小台阶）；悬空靶（脚下高于地面、从未贴地）保持静态高度，只在被地面顶到时抬起。
        /// </summary>
        private void FollowGround()
        {
            var terrain = _options.Terrain!;
            var units = _world.QueryEntities(new EntityFilter(predicate: e => e is Unit));
            for (var i = 0; i < units.Count; i++)
            {
                var unit = (Unit)units[i];
                var id = unit.EntityId;
                if (_flights.ContainsKey(id))
                {
                    continue;
                }

                var ground = terrain.GetGroundHeight(unit.MapId, unit.Position);
                if (!_walkers.TryGetValue(id, out var walker))
                {
                    if (unit.HeightOffset < ground)
                    {
                        unit.HeightOffset = ground;
                    }

                    var firstWalker = new Walker { LastPosition = unit.Position, Grounded = unit.HeightOffset - ground <= GroundEpsilon };
                    if (!firstWalker.Grounded && _options.Platforms != null
                        && _options.Platforms.TryGetSupport(unit.MapId, unit.Position, unit.HeightOffset, out var support))
                    {
                        // 出生/传送到平台上：脚下与某块平台顶面齐平，认出支撑（悬空靶的静态高度不会碰巧齐平，不受影响）。
                        firstWalker.Grounded = true;
                        firstWalker.PlatformId = support.PlatformId;
                    }

                    _walkers[id] = firstWalker;
                    continue;
                }

                if (walker.PlatformId != null && _options.Platforms != null)
                {
                    // 站在平台上（ADR-0170）：平台还托着这个位置就贴着它的顶面；走出了平台范围就离地下落（走出平台边缘），
                    // 地面比脚下高（走进了高台）则被抬上去。
                    walker.LastPosition = unit.Position;
                    if (_options.Platforms.TryGetTop(unit.MapId, walker.PlatformId, unit.Position, out var top) && top >= ground)
                    {
                        unit.HeightOffset = top;
                        continue;
                    }

                    walker.PlatformId = null;
                    if (unit.HeightOffset - ground > GroundEpsilon)
                    {
                        StartFlight(id, unit, 0.0);
                        _flights[id].LedgeFall = true;
                    }
                    else
                    {
                        unit.HeightOffset = ground;
                    }

                    continue;
                }

                if (unit.Position.Equals(walker.LastPosition))
                {
                    continue;
                }

                var previous = walker.LastPosition;
                walker.LastPosition = unit.Position;
                if (!walker.Grounded)
                {
                    if (unit.HeightOffset < ground)
                    {
                        unit.HeightOffset = ground;
                    }

                    continue;
                }

                // 走出平台边缘：无论有没有声明台阶高度，只要本 tick 走过的线段上地面在一个窗口内下降超过下落阈值（悬崖），单位就离地下落
                // （从当前脚下高度起、初速 0 的抛体）；缓坡贴着地面走下。台阶高度只决定"能不能走上去"。
                if (unit.HeightOffset - ground > GroundEpsilon &&
                    TerrainStepMath.FirstDrop(terrain, unit.MapId, previous, unit.Position, FallThreshold, _options.StepSampleDistance).HasValue)
                {
                    StartFlight(id, unit, 0.0);
                    _flights[id].LedgeFall = true;
                    continue;
                }

                unit.HeightOffset = ground;
            }

            // 已销毁实体的簿记。
            if (_walkers.Count > units.Count)
            {
                _scratch.Clear();
                foreach (var key in _walkers.Keys)
                {
                    if (_world.GetEntity(key) == null)
                    {
                        _scratch.Add(key);
                    }
                }

                for (var i = 0; i < _scratch.Count; i++)
                {
                    _walkers.Remove(_scratch[i]);
                }
            }
        }

        // ------------------------------------------------------------------ 台阶 / 坡度阻挡（移动系统的地形阻挡出口）

        /// <summary>离地下落的落差阈值（<see cref="VerticalAxisOptions.FallHeight"/>，缺省取台阶高度、再缺省取采样间距即 45° 坡）。</summary>
        public double FallThreshold =>
            _options.FallHeight ?? _options.StepHeight ?? _options.StepSampleDistance;

        /// <summary>
        /// 沿线段 <paramref name="from"/> → <paramref name="to"/> 找第一个被地形台阶/坡度挡住的点；没有挡住（或没有启用台阶阻挡）返回 <c>null</c>。
        /// 判定规则是 <see cref="TerrainStepMath"/> 的滑窗规则（见 <see cref="VerticalAxisOptions.StepHeight"/>）：贴地单位在一个
        /// <see cref="VerticalAxisOptions.StepSampleDistance"/> 窗口内地面升高超过台阶高度即挡，空中单位比较前方地面与当前脚下高度之差；
        /// 返回的点就是台阶边缘/坡脚之后的转换点（二分求精到 <see cref="TerrainStepMath.RefineTolerance"/>），与线段起点、采样格对齐无关
        /// （与导航阻挡的 <c>Raycast</c> 同口径，调用方照常回退一个到达容差）。
        /// </summary>
        public Vec2? TerrainBlockPoint(Unit unit, Vec2 from, Vec2 to)
        {
            if (!StepBlockingActive)
            {
                return null;
            }

            var terrain = _options.Terrain!;
            var step = _options.StepHeight!.Value;
            return _flights.ContainsKey(unit.EntityId)
                ? TerrainStepMath.FirstRiseBlockFromFoot(terrain, unit.MapId, from, to, step, _options.StepSampleDistance, unit.HeightOffset)
                : TerrainStepMath.FirstRiseBlock(terrain, unit.MapId, from, to, step, _options.StepSampleDistance);
        }

        /// <summary>
        /// <see cref="ITerrainStepConstraint"/>：贴地行走者沿线段第一个被台阶挡住的点（导航规划用，与实际移动阻挡同一条滑窗规则；
        /// 空中单位的参照随时间变化，规划一律按贴地行走者）。没有启用台阶阻挡返回 <c>null</c>。
        /// </summary>
        public Vec2? FirstStepBlock(Id mapId, Vec2 from, Vec2 to) =>
            StepBlockingActive
                ? TerrainStepMath.FirstRiseBlock(_options.Terrain!, mapId, from, to, _options.StepHeight!.Value, _options.StepSampleDistance)
                : (Vec2?)null;

        /// <summary>地形阻挡点 <paramref name="at"/> 处的表面法线（指向"低处"一侧，供贴墙滑动）：取地面高度梯度的反方向，梯度为零时取移动方向的反方向。</summary>
        public Vec2 TerrainBlockNormal(Unit unit, Vec2 at, Vec2 moveDirection)
        {
            var terrain = _options.Terrain;
            if (terrain != null)
            {
                var e = _options.StepSampleDistance * 0.5;
                var gx = terrain.GetGroundHeight(unit.MapId, new Vec2(at.X + e, at.Y)) - terrain.GetGroundHeight(unit.MapId, new Vec2(at.X - e, at.Y));
                var gy = terrain.GetGroundHeight(unit.MapId, new Vec2(at.X, at.Y + e)) - terrain.GetGroundHeight(unit.MapId, new Vec2(at.X, at.Y - e));
                var len = Math.Sqrt(gx * gx + gy * gy);
                if (len > 1e-12)
                {
                    return new Vec2(-gx / len, -gy / len);
                }
            }

            var dl = moveDirection.Length;
            return dl > 1e-12 ? new Vec2(-moveDirection.X / dl, -moveDirection.Y / dl) : Vec2.Zero;
        }
    }

    /// <summary>把 <see cref="VerticalMotionHost.Advance"/> 挂到 <see cref="TickPhase.MovementAndNavigation"/>（连续步才推进）。</summary>
    public sealed class VerticalMotionTickHandler : ITickPhaseHandler
    {
        private readonly VerticalMotionHost _host;

        public VerticalMotionTickHandler(VerticalMotionHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            if (step.Kind == SimStepKind.Continuous)
            {
                _host.Advance(step.Dt);
            }
        }
    }
}
