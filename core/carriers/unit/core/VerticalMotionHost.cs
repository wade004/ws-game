using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// <see cref="IVerticalMotion"/> 的唯一实现：为被抛起的单位积分抛体运动并写回 <see cref="Unit.HeightOffset"/>
    /// （手感设计/06 第 10 节勘误 9；单位、公式与已知局限见 <see cref="VerticalAxisOptions"/>/<see cref="IVerticalMotion"/>
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
    public sealed class VerticalMotionHost : IVerticalMotion, Core.Rules.Common.ILaunchSink
    {
        private sealed class Flight
        {
            public double StartHeight;
            public double InitialSpeed;
            public double Elapsed;
        }

        private readonly IWorldSim _world;
        private readonly VerticalAxisOptions _options;
        private readonly Dictionary<Id, Flight> _flights = new Dictionary<Id, Flight>();
        private readonly List<Id> _scratch = new List<Id>();

        public VerticalMotionHost(IWorldSim world, VerticalAxisOptions options)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();
        }

        /// <summary>当前在空中的单位数（诊断用）。</summary>
        public int AirborneCount => _flights.Count;

        public bool IsAirborne(Id unitId) => _flights.ContainsKey(unitId);

        public double GetVerticalSpeed(Id unitId) =>
            _flights.TryGetValue(unitId, out var f) ? f.InitialSpeed - _options.Gravity * f.Elapsed : 0.0;

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

            _flights[unitId] = new Flight { StartHeight = unit.HeightOffset, InitialSpeed = initialSpeed, Elapsed = 0.0 };
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

        public bool Jump(Id unitId)
        {
            if (_flights.ContainsKey(unitId) && !_options.AllowAirJump)
            {
                return false;
            }

            return LaunchToApex(unitId, _options.JumpHeight);
        }

        /// <summary>推进一步：对每个在空中的单位按累计飞行时间重算高度，落地的写 0 并结束飞行。</summary>
        public void Advance(double dt)
        {
            if (_flights.Count == 0 || !(dt > 0.0))
            {
                return;
            }

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
                    continue;
                }

                flight.Elapsed += dt;
                var t = flight.Elapsed;
                var h = flight.StartHeight + flight.InitialSpeed * t - 0.5 * _options.Gravity * t * t;
                if (h <= 0.0)
                {
                    unit.HeightOffset = 0.0;
                    _flights.Remove(id);
                }
                else
                {
                    unit.HeightOffset = h;
                }
            }
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
