using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;

namespace Core.Foundation.SaveSystem
{
    /// <summary>
    /// <see cref="IReplayPlayer"/> 的默认实现（T1-9，见 10_存档与持久化.md 第 8 节）。
    /// 本类不知道任何具体游戏内容（阶段处理器、意图词汇表等）——世界如何组装由调用方经
    /// <see cref="WorldFactory"/> 提供；本类只负责：
    /// 1) <see cref="Load"/> 时用工厂构造世界，并把 <see cref="ReplayData.RngSeeds"/>
    ///    逐条 <c>SetStreamState</c> 回随机源（10 第 8 节"分流随机源初始状态"）；
    /// 2) <see cref="StepTo"/> 时按 tick 顺序把该 tick 录入的意图 <c>SubmitIntent</c> 后
    ///    调用一次 <c>world.Tick(Continuous)</c>（10 第 8 节"输入录像"+"固定步长"）；
    /// 3) 从调用方提供的 <see cref="InMemoryEventAudit"/> 读出自开始以来的事件 key 序列，
    ///    连同当前世界状态一起交给 <see cref="WorldSnapshot.Capture"/> 算出确定性摘要。
    /// 不读取任何系统时间、不使用线程/反射，只使用调用方传入的 <see cref="ReplayData"/>
    /// 与 <see cref="WorldFactory"/> 构造出的对象。
    /// </summary>
    public sealed class ReplayPlayer : IReplayPlayer
    {
        private readonly WorldFactory _factory;
        private readonly IEventBus _bus;
        private readonly InMemoryEventAudit _audit;

        private ReplayData? _data;
        private IWorldSim? _world;
        private long _ticksAdvanced;

        /// <summary>
        /// <paramref name="bus"/>/<paramref name="audit"/> 由调用方构造并持有：<paramref name="bus"/>
        /// 必须以 <c>EventBusOptions.AuditLog = true</c>、且以 <paramref name="audit"/> 作为其
        /// <c>IEventAudit</c> 构造（否则 <see cref="StepTo"/> 读不到事件流，<c>EventLog</c> 恒为空）；
        /// <paramref name="factory"/> 只负责在 <paramref name="bus"/> 上注册处理器、构造世界与随机源，
        /// 不应另建事件总线（见 <see cref="WorldFactory"/> 注释）。
        /// </summary>
        public ReplayPlayer(WorldFactory factory, IEventBus bus, InMemoryEventAudit audit)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public void Load(ReplayData data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));

            var (world, rng) = _factory(0UL, _bus);
            _world = world ?? throw new InvalidOperationException("WorldFactory 返回的 World 不能为 null");

            if (rng == null)
            {
                throw new InvalidOperationException("WorldFactory 返回的 Rng 不能为 null");
            }

            // 10 第 8 节"分流随机源初始状态"：把录制时记下的每条流状态原样恢复；工厂内部用
            // 何种主种子构造 IRngHost 不重要，SetStreamState 会覆盖到位（见 WorldFactory 注释）。
            foreach (var pair in data.RngSeeds)
            {
                rng.SetStreamState(new Id(pair.Key), pair.Value);
            }

            _ticksAdvanced = 0;
        }

        public WorldSnapshot StepTo(long tick)
        {
            if (_data == null || _world == null)
            {
                throw new InvalidOperationException("StepTo 之前必须先调用 Load");
            }

            if (tick < _ticksAdvanced)
            {
                throw new ArgumentException(
                    $"StepTo 只能向前推进：当前已推进到 tick {_ticksAdvanced}，不能回退到 {tick}", nameof(tick));
            }

            var inputs = _data.Inputs;

            while (_ticksAdvanced < tick)
            {
                // 录像里的 tick 序号从 1 起（见 10_存档与持久化.md 第 8 节、本任务
                // DeterminismTests 的意图脚本约定）：第 _ticksAdvanced+1 个 Tick() 调用对应
                // 录像里 Tick == _ticksAdvanced+1 的输入。
                var tickNumber = _ticksAdvanced + 1;

                for (var i = 0; i < inputs.Count; i++)
                {
                    var input = inputs[i];
                    if (input.Tick == tickNumber)
                    {
                        _world.SubmitIntent(new Intent(input.ActorId, input.IntentKind, input.Args));
                    }
                }

                _world.Tick(SimStep.Continuous(_data.StepSeconds));
                _ticksAdvanced++;
            }

            return WorldSnapshot.Capture(_ticksAdvanced, BuildEventLog(), _world);
        }

        private IReadOnlyList<string> BuildEventLog()
        {
            var records = _audit.Records;
            var log = new List<string>(records.Count);

            for (var i = 0; i < records.Count; i++)
            {
                log.Add(records[i].Key.Value);
            }

            return log;
        }
    }
}
